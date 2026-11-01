using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace HackAcad.Web.Models.Entities;

public class CourseReviewHistory
{
    public int CourseReviewHistoryId { get; set; }

    [Required]
    public int CourseId { get; set; }

    [ForeignKey(nameof(CourseId))]
    public Course? Course { get; set; }

    [Required, StringLength(50)]
    public string Action { get; set; } = string.Empty;

    [StringLength(1000)]
    public string? Remarks { get; set; }

    [Required]
    public string PerformedBy { get; set; } = string.Empty;

    public DateTime PerformedAtUtc { get; set; } = DateTime.UtcNow;
}