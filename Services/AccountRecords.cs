using HackAcad.Web.Data;
using HackAcad.Web.Models.Entities;

namespace HackAcad.Web.Services;

// ADMIN - Shared writer for sign-in records and audit entries consumed by the Admin log pages.

public class AccountRecords(ApplicationDbContext db, IHttpContextAccessor accessor, SecurityMonitor monitor)
{
    // Estimate a browser/platform label from the user agent; it is not a verified device identity.
    public static string Device(HttpContext context)
    {
        var agent = context.Request.Headers.UserAgent.ToString();
        var browser = agent.Contains("Edg/") ? "Edge" : agent.Contains("OPR/") ? "Opera"
            : agent.Contains("Firefox/") ? "Firefox" : agent.Contains("Chrome/") ? "Chrome"
            : agent.Contains("Safari/") ? "Safari" : "Unknown browser";
        var system = agent.Contains("Android") ? "Android" : agent.Contains("iPhone") || agent.Contains("iPad") ? "iOS"
            : agent.Contains("Windows") ? "Windows" : agent.Contains("Macintosh") ? "macOS" : agent.Contains("Linux") ? "Linux" : "Unknown system";
        return browser + " on " + system;
    }

    // Store sign-in history, then feed the same outcome into security-signal detection.
    public async Task LoginAsync(string userId, string outcome)
    {
        var context = accessor.HttpContext!;
        db.LoginRecords.Add(new LoginRecord
        {
            UserId = userId,
            Outcome = outcome,
            Device = Device(context),
            IpAddress = context.Connection.RemoteIpAddress?.ToString() ?? "Unknown"
        });
        await db.SaveChangesAsync();
        await monitor.LoginAsync(userId, userId, outcome, context);
    }

    // Record attempted sign-ins, including failures without a known user account.
    public Task LoginAttemptAsync(string? userId, string identifier, string outcome) => monitor.LoginAsync(userId, identifier, outcome, accessor.HttpContext!);

    // Add an account-security audit event and notify that account in the caller's save.
    public void AddSecurityActivity(ApplicationUser user, string action, string details)
    {
        AddActivity(user.Id, action, "Account security", user.Id, user.DisplayName, details.Length > 512 ? details[..512] : details);
        db.UserNotifications.Add(new UserNotification
        {
            UserId = user.Id,
            EventKey = "security:" + Guid.NewGuid().ToString("N"),
            Title = action,
            Message = details.Length > 400 ? details[..400] : details,
            Link = "/Identity/Account/SecurityHistory",
            CreatedUtc = DateTime.UtcNow
        });
    }

    // Add before the caller's SaveChanges/commit so the action and audit record are atomic.
    // Queue an audit entry before the caller commits, preserving action and evidence together.
    public void AddActivity(string actorId, string action, string targetType, string targetId, string targetName, string details = "", string? before = null, string? after = null) =>
        db.AdminActivities.Add(new AdminActivity
        {
            ActorUserId = actorId,
            Action = action,
            TargetType = targetType,
            TargetId = targetId,
            TargetName = targetName.Length > 150 ? targetName[..150] : targetName,
            Details = details,
            IpAddress = accessor.HttpContext?.Connection.RemoteIpAddress?.ToString() ?? "Unknown",
            Device = accessor.HttpContext is { } context ? Device(context) : "System",
            CorrelationId = accessor.HttpContext?.TraceIdentifier ?? Guid.NewGuid().ToString("N"),
            Before = before,
            After = after
        });
}
