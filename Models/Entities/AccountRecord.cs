using System.ComponentModel.DataAnnotations;

namespace HackAcad.Web.Models.Entities;

// ADMIN - Persist separate records for sign-in history, administrative changes and usable browser sessions.

public class LoginRecord
{
    public long Id { get; set; }
    [MaxLength(255)] public string UserId { get; set; } = "";
    public DateTime OccurredUtc { get; set; } = DateTime.UtcNow;
    [MaxLength(40)] public string Outcome { get; set; } = "";
    [MaxLength(80)] public string Device { get; set; } = "";
    [MaxLength(45)] public string IpAddress { get; set; } = "";
}

public class AdminActivity
{
    [MaxLength(45)] public string IpAddress { get; set; } = "";
    [MaxLength(80)] public string Device { get; set; } = "";
    [MaxLength(100)] public string CorrelationId { get; set; } = "";
    // ADMIN - Before/After store event-time evidence, including structured snapshots for field comparisons.
    [MaxLength(12000)]
    public string? Before { get; set; }
    [MaxLength(12000)]
    public string? After { get; set; }
    [MaxLength(30)] public string Result { get; set; } = "Succeeded";
    public long Id { get; set; }
    // ADMIN - A nullable actor preserves the event when its original account has been deleted.
    [MaxLength(255)]
    public string? ActorUserId { get; set; }
    public DateTime OccurredUtc { get; set; } = DateTime.UtcNow;
    [MaxLength(80)] public string Action { get; set; } = "";
    [MaxLength(40)] public string TargetType { get; set; } = "";
    [MaxLength(255)] public string TargetId { get; set; } = "";
    // ADMIN - Keep the recorded name as historical evidence; the audit UI may resolve a newer account name.
    [MaxLength(150)] public string TargetName { get; set; } = "";
    [MaxLength(512)] public string Details { get; set; } = "";
}

public class AccountSession
{
    [MaxLength(32)] public string Id { get; set; } = Guid.NewGuid().ToString("N");
    [MaxLength(255)] public string UserId { get; set; } = "";
    [MaxLength(256)] public string SecurityStamp { get; set; } = "";
    [MaxLength(80)] public string Device { get; set; } = "";
    [MaxLength(45)] public string IpAddress { get; set; } = "";
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime LastSeenUtc { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresUtc { get; set; }
    public DateTime? RevokedUtc { get; set; }
    // ADMIN - Session MFA is separate from a short-lived grant for one sensitive action.
    public DateTime? MfaVerifiedUtc { get; set; }
}
