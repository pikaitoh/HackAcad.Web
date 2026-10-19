using Microsoft.AspNetCore.Identity;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace HackAcad.Web.Models.Entities;

public class ApplicationUser : IdentityUser
{
    [MaxLength(10)] public string Appearance { get; set; } = "system";
    [MaxLength(60)] public string BannerKey { get; set; } = "foundation";
    public bool IsDisabled { get; set; }
    public bool SmsTwoFactorEnabled { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    [MaxLength(100)]
    public string? FullName { get; set; }
    [MaxLength(100)]
    public string TimeZoneId { get; set; } = "Asia/Kuala_Lumpur";
    public DateTime? LastPasswordChangedUtc { get; set; }
    public DateTime JoinedDate { get; set; } = DateTime.UtcNow;
    [Column(TypeName = "mediumblob")]
    public byte[]? AvatarImage { get; set; }
}
