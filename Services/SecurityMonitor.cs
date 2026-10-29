using System.Security.Cryptography;
using System.Text;
using HackAcad.Web.Data;
using HackAcad.Web.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace HackAcad.Web.Services;

// ADMIN - Record security signals and raise reviewable alerts; detection alone never disables an account.

public class SecurityMonitor(ApplicationDbContext db, TimeProvider clock)
{
    public static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    // Persist an outcome and inspect recent failures for account repetition or IP spraying.
    public async Task LoginAsync(string? userId, string identifier, string outcome, HttpContext context)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var ip = context.Connection.RemoteIpAddress?.ToString() ?? "Unknown";
        var subject = Hash(userId ?? identifier.Trim().ToUpperInvariant());
        var success = outcome.StartsWith("Signed in", StringComparison.Ordinal);
        db.SecurityEvents.Add(new SecurityEvent
        {
            UserId = userId,
            SubjectHash = subject,
            IpAddress = ip,
            Device = AccountRecords.Device(context),
            Outcome = outcome,
            Success = success,
            OccurredUtc = now
        });
        await db.SaveChangesAsync();
        if (success)
            return;
        var since = now.AddMinutes(-10);
        var attempts = db.SecurityEvents.Where(e => !e.Success && e.OccurredUtc >= since);
        var bucket = new DateTimeOffset(now).ToUnixTimeSeconds() / 600;
        // ADMIN - Five failures for one account identifier within ten minutes raise a repeated-sign-in alert.
        if (await attempts.CountAsync(e => e.SubjectHash == subject) >= 5)
            await RaiseAsync($"account:{subject}:{bucket}", "Repeated failed sign-ins", "At least 5 failed sign-ins used the same account identifier in 10 minutes. Review the login records before taking action.", userId, ip);
        // ADMIN - Ten distinct account identifiers from one IP raise a possible password-spraying signal.
        if (await attempts.Where(e => e.IpAddress == ip).Select(e => e.SubjectHash).Distinct().CountAsync() >= 10)
            await RaiseAsync($"spray:{Hash(ip)}:{bucket}", "One IP tried multiple accounts", "This IP attempted at least 10 distinct account identifiers in 10 minutes. Shared networks can cause false positives; this alert does not automatically block the IP.", null, ip);
    }

    // Use a unique event key to avoid duplicate alerts and leave resolved events resolved.
    public async Task RaiseAsync(string key, string title, string explanation, string? userId, string ip)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        // Unique event keys deduplicate concurrent detections and keep resolved alerts resolved.
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT IGNORE INTO `security_alerts` (EventKey,Title,Explanation,UserId,IpAddress,Status,Notes,CreatedUtc,UpdatedUtc,Version) VALUES ({key},{title},{explanation},{userId},{ip},{"New"},{""},{now},{now},{Guid.NewGuid().ToString("N")})");
    }
}
