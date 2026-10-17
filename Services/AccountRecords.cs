using HackAcad.Web.Data;
using HackAcad.Web.Models.Entities;

namespace HackAcad.Web.Services;

public class AccountRecords(ApplicationDbContext db, IHttpContextAccessor accessor)
{
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

    public async Task LoginAsync(string userId, string outcome)
    {
        var context = accessor.HttpContext!;
        db.LoginRecords.Add(new LoginRecord { UserId = userId, Outcome = outcome,
            Device = Device(context), IpAddress = context.Connection.RemoteIpAddress?.ToString() ?? "Unknown" });
        await db.SaveChangesAsync();
    }

    // Add before the caller's SaveChanges/commit so the action and audit record are atomic.
    public void AddActivity(string actorId, string action, string targetType, string targetId, string targetName, string details = "") =>
        db.AdminActivities.Add(new AdminActivity { ActorUserId = actorId, Action = action,
            TargetType = targetType, TargetId = targetId, TargetName = targetName.Length > 150 ? targetName[..150] : targetName, Details = details });
}
