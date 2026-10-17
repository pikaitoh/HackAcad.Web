using System.ComponentModel.DataAnnotations;

namespace HackAcad.Web.Models.Entities;

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
    public long Id { get; set; }
    [MaxLength(255)] public string? ActorUserId { get; set; }
    public DateTime OccurredUtc { get; set; } = DateTime.UtcNow;
    [MaxLength(80)] public string Action { get; set; } = "";
    [MaxLength(40)] public string TargetType { get; set; } = "";
    [MaxLength(255)] public string TargetId { get; set; } = "";
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
}
