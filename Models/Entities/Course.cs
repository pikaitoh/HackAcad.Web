using System.ComponentModel.DataAnnotations;

namespace HackAcad.Web.Models.Entities;

public class Course
{
    public int CourseId { get; set; }

    [Required, StringLength(150)]
    public string Title { get; set; } = string.Empty;

    [StringLength(1000)]
    public string? Description { get; set; }

    [StringLength(50)]
    public string? Category { get; set; }

    [StringLength(300)]
    public string? ThumbnailUrl { get; set; }

    public bool IsPublished { get; set; }

    [Required, StringLength(20)]
    public string ReviewStatus { get; set; } = CourseReviewStatus.Draft;
    [StringLength(1000)]
    public string? ReviewReason { get; set; }
    public int ReviewRevision { get; set; }
    public DateTime? SubmittedForReviewUtc { get; set; }
    public DateTime? ReviewedUtc { get; set; }
    [Required, StringLength(32)]
    public string ReviewVersion { get; set; } = Guid.NewGuid().ToString("N");

    // FK points at Instructors.UserId, not users.Id directly — the
    // database itself refuses to let a non-Instructor own a course.
    // See Database_Schema.md v3.0.
    [Required]
    public string InstructorId { get; set; } = string.Empty;

    public DateTime CreatedDate { get; set; } = DateTime.UtcNow;
}
