using System.Security.Claims;
using System.Text.Json;
using HackAcad.Web.Data;
using HackAcad.Web.Models.Entities;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace HackAcad.Web.Services;

// ADMIN - Protected verification tickets and short-lived grants are bound to the current user, session and security stamp.

public class AdminSecurity(ApplicationDbContext db, IDataProtectionProvider protection, IMemoryCache cache, TimeProvider clock)
{
    private sealed record Challenge(string UserId, string SessionId, string Stamp, string Scope, string ReturnUrl, DateTimeOffset Expires, string? OperationId);
    private sealed record Grant(string Stamp, DateTimeOffset Expires);
    private readonly IDataProtector protector = protection.CreateProtector("HackAcad.AdminVerification.v2");
    private static readonly object OtpLock = new();
    private static string Key(string userId, string sessionId, string scope) => $"admin-grant:{userId}:{sessionId}:{scope}";

    // Accept a grant only while its security stamp and action scope still match.
    public bool HasGrant(ApplicationUser user, string sessionId, string scope) =>
        cache.TryGetValue<Grant>(Key(user.Id, sessionId, scope), out var grant)
        && grant!.Stamp == user.SecurityStamp && grant.Expires > clock.GetUtcNow();

    // Protect a ten-minute challenge that cannot be edited by the browser.
    public string ChallengeTicket(ApplicationUser user, string sessionId, string scope, string returnUrl, string? operationId = null) =>
        protector.Protect(JsonSerializer.Serialize(new Challenge(user.Id, sessionId, user.SecurityStamp ?? "", scope, returnUrl, clock.GetUtcNow().AddMinutes(10), operationId)));

    // Reject unreadable, expired or different-user/session tickets.
    public (string Scope, string ReturnUrl, string? OperationId)? ReadTicket(string ticket, ApplicationUser user, string sessionId)
    {
        try
        {
            var challenge = JsonSerializer.Deserialize<Challenge>(protector.Unprotect(ticket));
            if (challenge?.UserId == user.Id && challenge.SessionId == sessionId && challenge.Stamp == user.SecurityStamp && challenge.Expires > clock.GetUtcNow())
                return (challenge.Scope, challenge.ReturnUrl, challenge.OperationId);
        }
        catch (Exception e) when (e is System.Security.Cryptography.CryptographicException or JsonException or ArgumentException) { }
        return null;
    }

    // Cache a five-minute permission for one user/session/action scope.
    public void GrantAccess(ApplicationUser user, string sessionId, string scope)
    {
        var grant = new Grant(user.SecurityStamp ?? "", clock.GetUtcNow().AddMinutes(5));
        // ponytail: verification grants live in this single host's process; use shared storage before scaling out.
        cache.Set(Key(user.Id, sessionId, scope), grant, grant.Expires);
    }

    // Prevent replay after Identity has already validated this authenticator code.
    public bool UseAuthenticatorCode(ApplicationUser user, string code)
    {
        var key = "admin-otp:" + SecurityMonitor.Hash(user.Id + ":" + user.SecurityStamp + ":" + code);
        lock (OtpLock)
        {
            if (cache.TryGetValue(key, out _))
                return false;
            cache.Set(key, true, TimeSpan.FromMinutes(2));
            return true;
        }
    }

    // Record completed MFA on the active persisted browser session.
    public async Task MarkMfaAsync(HttpContext context, ApplicationUser user)
    {
        var id = context.Items["hackacad:new-session"] as string ?? context.User.FindFirstValue(AccountSessions.ClaimName);
        if (id is null)
            return;
        var now = clock.GetUtcNow().UtcDateTime;
        await db.AccountSessions.Where(s => s.Id == id && s.UserId == user.Id && s.SecurityStamp == user.SecurityStamp && s.RevokedUtc == null)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.MfaVerifiedUtc, now));
    }
}
