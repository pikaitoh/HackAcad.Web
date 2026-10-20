using System.ComponentModel.DataAnnotations;
namespace HackAcad.Web.Models.Entities;

public class SecurityEvent
{
    public long Id { get; set; }
    [MaxLength(255)] public string? UserId { get; set; }
    [MaxLength(64)] public string SubjectHash { get; set; } = "";
    [MaxLength(45)] public string IpAddress { get; set; } = "";
    [MaxLength(80)] public string Device { get; set; } = "";
    [MaxLength(80)] public string Outcome { get; set; } = "";
    public bool Success { get; set; }
    public DateTime OccurredUtc { get; set; }
}
public class SecurityAlert
{
    public long Id { get; set; }
    [MaxLength(180)] public string EventKey { get; set; } = "";
    [MaxLength(100)] public string Title { get; set; } = "";
    [MaxLength(600)] public string Explanation { get; set; } = "";
    [MaxLength(255)] public string? UserId { get; set; }
    [MaxLength(45)] public string IpAddress { get; set; } = "";
    [MaxLength(20)] public string Status { get; set; } = "New";
    [MaxLength(1000)] public string Notes { get; set; } = "";
    public DateTime CreatedUtc { get; set; }
    public DateTime UpdatedUtc { get; set; }
    [ConcurrencyCheck, MaxLength(32)] public string Version { get; set; } = Guid.NewGuid().ToString("N");
}
public class SupportAccess
{
    public long Id { get; set; }
    [MaxLength(255)] public string UserId { get; set; } = "";
    [MaxLength(256)] public string Stamp { get; set; } = "";
    [MaxLength(64)] public string TokenHash { get; set; } = "";
    public DateTime CreatedUtc { get; set; }
    public DateTime ExpiresUtc { get; set; }
    public DateTime? ConsumedUtc { get; set; }
}
public class SupportTicket
{
    public long Id { get; set; }
    [MaxLength(255)] public string UserId { get; set; } = "";
    [MaxLength(120)] public string Subject { get; set; } = "";
    [MaxLength(24)] public string Status { get; set; } = "Awaiting admin";
    public DateTime CreatedUtc { get; set; }
    public DateTime UpdatedUtc { get; set; }
    public DateTime? UserReadUtc { get; set; }
    public DateTime? AdminReadUtc { get; set; }
    [ConcurrencyCheck, MaxLength(32)] public string Version { get; set; } = Guid.NewGuid().ToString("N");
}
public class SupportMessage
{
    public long Id { get; set; }
    public long TicketId { get; set; }
    [MaxLength(255)] public string AuthorId { get; set; } = "";
    [MaxLength(80)] public string AuthorName { get; set; } = "";
    public bool ByAdmin { get; set; }
    public bool Internal { get; set; }
    [MaxLength(3000)] public string Body { get; set; } = "";
    public DateTime CreatedUtc { get; set; }
}
