using System.ComponentModel.DataAnnotations;
namespace HackAcad.Web.Models.Entities;
public class UserNotification
{
    public long Id { get; set; }
    [MaxLength(255)] public string UserId { get; set; } = "";
    [MaxLength(160)] public string EventKey { get; set; } = "";
    [MaxLength(160)] public string Title { get; set; } = "";
    [MaxLength(500)] public string Message { get; set; } = "";
    [MaxLength(300)] public string Link { get; set; } = "";
    public DateTime CreatedUtc { get; set; }
    public DateTime? ReadUtc { get; set; }
}
public class BannerReward
{
    public long Id { get; set; }
    [MaxLength(255)] public string UserId { get; set; } = "";
    public int CourseId { get; set; }
    [MaxLength(150)] public string CourseTitle { get; set; } = "";
    [MaxLength(20)] public string Theme { get; set; } = "scholar";
    public DateTime UnlockedUtc { get; set; }
}
