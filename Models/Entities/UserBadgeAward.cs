using System.ComponentModel.DataAnnotations;

namespace HackAcad.Web.Models.Entities;

public class UserBadgeAward
{
    public int UserBadgeAwardId { get; set; }

    [Required, MaxLength(450)]
    public string UserId { get; set; } = string.Empty;

    [Required, MaxLength(80)]
    public string BadgeId { get; set; } = string.Empty;

    public DateTime AwardedUtc { get; set; }
}
