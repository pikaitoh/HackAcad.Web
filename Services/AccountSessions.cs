using System.Security.Claims;
using HackAcad.Web.Data;
using HackAcad.Web.Models.Entities;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace HackAcad.Web.Services;

// ADMIN - Persist browser sessions so Admin revocation and session timeouts are enforced by cookie validation.

public class AccountSessions(ApplicationDbContext db, SecurityMonitor monitor, TimeProvider clock)
{
    public const string GoogleTimeClaim = "hackacad:google-auth-time"; // Existing signed-in Google sessions.
    public const string ExternalTimeClaim = "hackacad:external-auth-time";
    public const string ExternalProviderClaim = "hackacad:external-auth-provider";
    public const string ClaimName = "hackacad:session";

    // Create or refresh a session record and add its identifier to the authentication cookie.
    public async Task SigningInAsync(CookieSigningInContext context)
    {
        var userId = context.Principal!.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var stamp = context.Principal!.FindFirstValue("AspNet.Identity.SecurityStamp") ?? "";
        var existingId = context.HttpContext.User.FindFirstValue(ClaimTypes.NameIdentifier) == userId
            ? context.HttpContext.User.FindFirstValue(ClaimName) : null;
        var session = existingId is null ? null : await db.AccountSessions.SingleOrDefaultAsync(s => s.Id == existingId && s.UserId == userId && s.RevokedUtc == null);
        var now = clock.GetUtcNow().UtcDateTime;
        if (session is null)
        {
            session = new AccountSession
            {
                UserId = userId,
                CreatedUtc = now,
                Device = AccountRecords.Device(context.HttpContext),
                IpAddress = context.HttpContext.Connection.RemoteIpAddress?.ToString() ?? "Unknown"
            };
            if (await db.Admins.AnyAsync(a => a.UserId == userId) && await db.AccountSessions.AnyAsync(s => s.UserId == userId)
                && !await db.AccountSessions.AnyAsync(s => s.UserId == userId && s.Device == session.Device && s.IpAddress == session.IpAddress))
                await monitor.RaiseAsync($"device:{userId}:{now:yyyyMMdd}:{SecurityMonitor.Hash(session.Device + session.IpAddress)}", "Admin signed in from a new device or network", "This device and IP combination has not been used by this administrator before. Compare the session with the administrator's recent activity.", userId, session.IpAddress);
            db.AccountSessions.Add(session);
        }
        session.SecurityStamp = stamp;
        session.LastSeenUtc = now;
        session.ExpiresUtc = context.Properties.ExpiresUtc?.UtcDateTime ?? now.Add(context.Options.ExpireTimeSpan);
        var identity = (ClaimsIdentity)context.Principal!.Identity!;
        foreach (var claim in identity.FindAll(ClaimName).ToArray())
            identity.RemoveClaim(claim);
        identity.AddClaim(new Claim(ClaimName, session.Id));
        context.HttpContext.Items["hackacad:new-session"] = session.Id;
        await db.SaveChangesAsync();
    }

    // Reject revoked/stale sessions and enforce Admin idle and absolute lifetime limits.
    public async Task ValidateAsync(CookieValidatePrincipalContext context)
    {
        await SecurityStampValidator.ValidatePrincipalAsync(context);
        if (context.Principal?.Identity?.IsAuthenticated != true)
            return;
        var id = context.Principal.FindFirstValue(ClaimName);
        var userId = context.Principal.FindFirstValue(ClaimTypes.NameIdentifier);
        var stamp = context.Principal.FindFirstValue("AspNet.Identity.SecurityStamp");
        var now = clock.GetUtcNow().UtcDateTime;
        var session = await db.AccountSessions.SingleOrDefaultAsync(s => s.Id == id && s.UserId == userId
            && s.SecurityStamp == stamp && s.RevokedUtc == null && s.ExpiresUtc > now);
        var admin = await db.Admins.AnyAsync(a => a.UserId == userId);
        // ADMIN - For Admins, reject sessions after 15 idle minutes or eight hours from creation.
        if (session is null || admin && (session.LastSeenUtc <= now.AddMinutes(-15) || session.CreatedUtc <= now.AddHours(-8)))
        {
            if (session is not null)
            {
                session.RevokedUtc = now;
                await db.SaveChangesAsync();
            }
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync(IdentityConstants.ApplicationScheme);
            return;
        }
        if (context.ShouldRenew || session.LastSeenUtc < now.AddMinutes(-1))
        {
            session.LastSeenUtc = now;
            if (context.ShouldRenew)
                session.ExpiresUtc = now.Add(context.Options.ExpireTimeSpan);
            await db.SaveChangesAsync();
        }
    }
}
