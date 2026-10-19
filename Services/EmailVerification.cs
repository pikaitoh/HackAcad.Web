using System.Text;
using HackAcad.Web.Models.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Caching.Memory;
using MimeKit;

namespace HackAcad.Web.Services;

public class EmailVerification(UserManager<ApplicationUser> users, IConfiguration config,
    IWebHostEnvironment environment, IMemoryCache cache, GmailEmail gmail, ILogger<EmailVerification> logger)
{
    private static readonly object CooldownLock = new();

    public Task<bool> SendAsync(ApplicationUser user, string? newEmail = null) => SendMessageAsync(user, newEmail, passwordReset: false);

    public Task<bool> SendPasswordResetAsync(ApplicationUser user) => SendMessageAsync(user, null, passwordReset: true);

    private async Task<bool> SendMessageAsync(ApplicationUser user, string? newEmail, bool passwordReset)
    {
        var changingEmail = newEmail is not null;
        if (user.IsDisabled) return !changingEmail;
        if (passwordReset && (!user.EmailConfirmed || string.IsNullOrEmpty(user.Email) || !await users.HasPasswordAsync(user))) return true;
        if (!passwordReset && !changingEmail && (user.EmailConfirmed || string.IsNullOrEmpty(user.Email))) return true;
        var recipient = newEmail ?? user.Email!;
        var pickup = environment.IsDevelopment() ? config["Email:DevelopmentPickupDirectory"] : null;
        var address = config["Email:GmailAddress"];
        if (!Uri.TryCreate(config["Email:PublicBaseUrl"], UriKind.Absolute, out var origin)
            || origin.AbsolutePath != "/" || !string.IsNullOrEmpty(origin.Query)
            || !string.IsNullOrEmpty(origin.Fragment) || !string.IsNullOrEmpty(origin.UserInfo)
            || (origin.Scheme != "https" && !(environment.IsDevelopment() && origin.Scheme == "http" && origin.IsLoopback))
            || (string.IsNullOrWhiteSpace(pickup) && (string.IsNullOrWhiteSpace(address) || !gmail.IsConfigured)))
        {
            logger.LogWarning("Account email delivery is not configured. Authorize the Gmail API sender locally.");
            return false;
        }

        var key = passwordReset ? "password-reset:" + user.Id : changingEmail ? "change-email:" + user.Id : "verification-email:" + user.Id + ":" + recipient.ToUpperInvariant();
        // Atomically reserve one delivery per account/address per minute, including concurrent requests.
        lock (CooldownLock)
        {
            if (cache.TryGetValue(key, out _)) return !changingEmail;
            cache.Set(key, true, TimeSpan.FromMinutes(1));
        }
        try
        {
            var token = passwordReset ? await users.GeneratePasswordResetTokenAsync(user) : changingEmail ? await users.GenerateChangeEmailTokenAsync(user, recipient)
                : await users.GenerateEmailConfirmationTokenAsync(user);
            var action = passwordReset ? "ResetPassword" : changingEmail ? "ConfirmEmailChange" : "ConfirmEmail";
            var link = QueryHelpers.AddQueryString(new Uri(origin, "Identity/Account/" + action).AbsoluteUri,
                new Dictionary<string, string?> {
                    ["userId"] = user.Id, ["email"] = recipient,
                    ["code"] = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token))
                });
            var message = new MimeMessage();
            message.From.Add(new MailboxAddress("HackAcad", string.IsNullOrWhiteSpace(pickup) ? address! : "preview@hackacad.local"));
            message.To.Add(MailboxAddress.Parse(recipient));
            message.Subject = passwordReset ? "Reset your HackAcad password" : changingEmail ? "Confirm your new HackAcad email" : "Verify your HackAcad email";
            var purpose = passwordReset ? "Reset your HackAcad password. This link does not sign you in or turn off two-factor authentication." : changingEmail
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

            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await gmail.SendAsync(message, timeout.Token);
            return true;
        }
        catch (Exception error)
        {
            cache.Remove(key);
            // Provider exception text can contain private addresses. Do not log bodies, tokens or credentials.
            logger.LogWarning("Account email delivery failed ({ErrorType}).", error.GetType().Name);
            return false;
        }
    }
}
