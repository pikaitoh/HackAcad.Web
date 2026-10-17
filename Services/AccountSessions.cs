using System.Security.Claims;
using HackAcad.Web.Data;
using HackAcad.Web.Models.Entities;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace HackAcad.Web.Services;

public class AccountSessions(ApplicationDbContext db)
{
    public const string ClaimName = "hackacad:session";

    public async Task SigningInAsync(CookieSigningInContext context)
    {
        var userId = context.Principal!.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var stamp = context.Principal!.FindFirstValue("AspNet.Identity.SecurityStamp") ?? "";
        var existingId = context.HttpContext.User.FindFirstValue(ClaimTypes.NameIdentifier) == userId
            ? context.HttpContext.User.FindFirstValue(ClaimName) : null;
        var session = existingId is null ? null : await db.AccountSessions.SingleOrDefaultAsync(s => s.Id == existingId && s.UserId == userId && s.RevokedUtc == null);
        var now = DateTime.UtcNow;
        if (session is null)
        {
            session = new AccountSession { UserId = userId, Device = AccountRecords.Device(context.HttpContext),
                IpAddress = context.HttpContext.Connection.RemoteIpAddress?.ToString() ?? "Unknown" };
            db.AccountSessions.Add(session);
        }
        session.SecurityStamp = stamp;
        session.LastSeenUtc = now;
        session.ExpiresUtc = context.Properties.ExpiresUtc?.UtcDateTime ?? now.Add(context.Options.ExpireTimeSpan);
        var identity = (ClaimsIdentity)context.Principal!.Identity!;
        foreach (var claim in identity.FindAll(ClaimName).ToArray()) identity.RemoveClaim(claim);
        identity.AddClaim(new Claim(ClaimName, session.Id));
        await db.SaveChangesAsync();
    }

    public async Task ValidateAsync(CookieValidatePrincipalContext context)
    {
        await SecurityStampValidator.ValidatePrincipalAsync(context);
        if (context.Principal?.Identity?.IsAuthenticated != true) return;
        var id = context.Principal.FindFirstValue(ClaimName);
        var userId = context.Principal.FindFirstValue(ClaimTypes.NameIdentifier);
        var stamp = context.Principal.FindFirstValue("AspNet.Identity.SecurityStamp");
        var now = DateTime.UtcNow;
        var session = await db.AccountSessions.SingleOrDefaultAsync(s => s.Id == id && s.UserId == userId
            && s.SecurityStamp == stamp && s.RevokedUtc == null && s.ExpiresUtc > now);
        if (session is null)
        {
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync(IdentityConstants.ApplicationScheme);
            return;
        }
        if (context.ShouldRenew || session.LastSeenUtc < now.AddMinutes(-1))
        {
            session.LastSeenUtc = now;
            if (context.ShouldRenew) session.ExpiresUtc = now.Add(context.Options.ExpireTimeSpan);
            await db.SaveChangesAsync();
        }
    }
}
