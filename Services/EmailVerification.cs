using System.Text;
using HackAcad.Web.Models.Entities;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Caching.Memory;
using MimeKit;

namespace HackAcad.Web.Services;

public class EmailVerification(UserManager<ApplicationUser> users, IConfiguration config,
    IWebHostEnvironment environment, IMemoryCache cache, ILogger<EmailVerification> logger)
{
    private static readonly object CooldownLock = new();

    public async Task<bool> SendAsync(ApplicationUser user, string? newEmail = null)
    {
        var changingEmail = newEmail is not null;
        if (user.IsDisabled) return !changingEmail;
        if (!changingEmail && (user.EmailConfirmed || string.IsNullOrEmpty(user.Email))) return true;
        var recipient = newEmail ?? user.Email!;
        var pickup = environment.IsDevelopment() ? config["Email:DevelopmentPickupDirectory"] : null;
        var address = config["Email:GmailAddress"];
        var password = config["Email:AppPassword"]?.Replace(" ", "");
        if (!Uri.TryCreate(config["Email:PublicBaseUrl"], UriKind.Absolute, out var origin)
            || origin.AbsolutePath != "/" || !string.IsNullOrEmpty(origin.Query)
            || !string.IsNullOrEmpty(origin.Fragment) || !string.IsNullOrEmpty(origin.UserInfo)
            || (origin.Scheme != "https" && !(environment.IsDevelopment() && origin.Scheme == "http" && origin.IsLoopback))
            || (string.IsNullOrWhiteSpace(pickup) && (string.IsNullOrWhiteSpace(address) || string.IsNullOrWhiteSpace(password))))
        {
            logger.LogWarning("Email verification delivery is not configured. Set Email settings locally.");
            return false;
        }

        var key = changingEmail ? "change-email:" + user.Id : "verification-email:" + user.Id + ":" + recipient.ToUpperInvariant();
        // Atomically reserve one delivery per account/address per minute, including concurrent requests.
        lock (CooldownLock)
        {
            if (cache.TryGetValue(key, out _)) return !changingEmail;
            cache.Set(key, true, TimeSpan.FromMinutes(1));
        }
        try
        {
            var token = changingEmail ? await users.GenerateChangeEmailTokenAsync(user, recipient)
                : await users.GenerateEmailConfirmationTokenAsync(user);
            var action = changingEmail ? "ConfirmEmailChange" : "ConfirmEmail";
            var link = QueryHelpers.AddQueryString(new Uri(origin, "Identity/Account/" + action).AbsoluteUri,
                new Dictionary<string, string?> {
                    ["userId"] = user.Id, ["email"] = recipient,
                    ["code"] = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token))
                });
            var message = new MimeMessage();
            message.From.Add(new MailboxAddress("HackAcad", string.IsNullOrWhiteSpace(pickup) ? address! : "preview@hackacad.local"));
            message.To.Add(MailboxAddress.Parse(recipient));
            message.Subject = changingEmail ? "Confirm your new HackAcad email" : "Verify your HackAcad email";
            var purpose = changingEmail
                ? "Confirm this address as your new HackAcad login email. Your current email stays unchanged until you confirm. After confirmation, log in again using this new address."
                : "Verify your email to log in to HackAcad.";
            message.Body = new TextPart("plain") { Text = $"{purpose}\n\nOpen this link within 10 minutes:\n{link}\n\nIf you did not request this, you can ignore this email." };

            if (!string.IsNullOrWhiteSpace(pickup))
            {
                // Explicit, Development-only mail capture; never expose tokens in a page or log.
                var directory = Path.GetFullPath(pickup, environment.ContentRootPath);
                var webRoot = Path.GetFullPath(environment.WebRootPath ?? Path.Combine(environment.ContentRootPath, "wwwroot"));
                if (directory.Equals(webRoot, StringComparison.OrdinalIgnoreCase)
                    || directory.StartsWith(webRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Mail capture must be outside wwwroot.");
                Directory.CreateDirectory(directory);
                await message.WriteToAsync(Path.Combine(directory, Guid.NewGuid() + ".eml"));
                return true;
            }

            using var smtp = new SmtpClient { Timeout = 15000 };
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await smtp.ConnectAsync("smtp.gmail.com", 587, SecureSocketOptions.StartTls, timeout.Token);
            await smtp.AuthenticateAsync(address!, password!, timeout.Token);
            await smtp.SendAsync(message, timeout.Token);
            await smtp.DisconnectAsync(true, timeout.Token);
            return true;
        }
        catch (Exception error)
        {
            cache.Remove(key);
            // SMTP exception text can contain private addresses. Do not log bodies, tokens or credentials.
            logger.LogWarning("Verification email delivery failed ({ErrorType}).", error.GetType().Name);
            return false;
        }
    }
}
