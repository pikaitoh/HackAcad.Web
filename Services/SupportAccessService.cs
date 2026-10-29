using System.Security.Claims;
using System.Security.Cryptography;
using HackAcad.Web.Data;
using HackAcad.Web.Models.Entities;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace HackAcad.Web.Services;

// ADMIN - Mailbox verification grants support-only access; disabled users do not regain normal website access.

public class SupportAccessService(ApplicationDbContext db, UserManager<ApplicationUser> users, EmailVerification email, TimeProvider clock)
{
    public const string Scheme = "Support";
    // Send a short-lived support link without disclosing whether an account exists.
    public async Task RequestAsync(string address)
    {
        var user = await users.FindByEmailAsync(address.Trim());
        if (user?.Email is null)
            return;
        var now = clock.GetUtcNow().UtcDateTime;
        string token;
        await using (var transaction = await db.Database.BeginTransactionAsync())
        {
            await db.Users.FromSqlInterpolated($"SELECT * FROM `users` WHERE Id={user.Id} FOR UPDATE").ToListAsync();
            await db.Entry(user).ReloadAsync();
            if (await db.SupportAccesses.AnyAsync(a => a.UserId == user.Id && a.CreatedUtc > now.AddMinutes(-1)))
                return;
            token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            db.SupportAccesses.Add(new SupportAccess { UserId = user.Id, Stamp = user.SecurityStamp ?? "", TokenHash = SecurityMonitor.Hash(token), CreatedUtc = now, ExpiresUtc = now.AddMinutes(10) });
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        var link = email.SupportLink("Support/Verify?code=" + token);
        if (link is not null)
            await email.SendSupportAsync(user.Email!, "Verify your support access", $"Confirm your email to contact HackAcad support. This does not sign you in to the website or reactivate a disabled account.\n\nOpen this link within 10 minutes:\n{link}\n\nIf you did not request this, ignore this email.");
    }

    // Consume a valid support token once and recover the current account.
    public async Task<ApplicationUser?> RedeemAsync(string code)
    {
        if (code.Length != 64 || !code.All(Uri.IsHexDigit))
            return null;
        var hash = SecurityMonitor.Hash(code);
        var now = clock.GetUtcNow().UtcDateTime;
        await using var transaction = await db.Database.BeginTransactionAsync();
        var access = await db.SupportAccesses.AsNoTracking().SingleOrDefaultAsync(a => a.TokenHash == hash && a.ConsumedUtc == null && a.ExpiresUtc > now);
        if (access is null)
            return null;
        await db.Users.FromSqlInterpolated($"SELECT * FROM `users` WHERE Id={access.UserId} FOR UPDATE").ToListAsync();
        var user = await users.FindByIdAsync(access.UserId);
        if (user is null || user.SecurityStamp != access.Stamp)
            return null;
        var used = await db.SupportAccesses.Where(a => a.Id == access.Id && a.ConsumedUtc == null && a.ExpiresUtc > now)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.ConsumedUtc, now));
        if (used != 1)
            return null;
        await transaction.CommitAsync();
        return user;
    }

    // Validate the separate support cookie without restoring normal sign-in permissions.
    public async Task ValidateAsync(CookieValidatePrincipalContext context)
    {
        var user = await users.FindByIdAsync(context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier) ?? "");
        // Disabled users are intentionally allowed only in this separate, mailbox-verified support identity.
        if (user is null || user.SecurityStamp != context.Principal?.FindFirstValue("support:stamp"))
        {
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync(Scheme);
        }
    }
}
