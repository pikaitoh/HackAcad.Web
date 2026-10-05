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

    // FK points at Teachers.UserId, not AspNetUsers.Id directly — the
    // database itself refuses to let a non-Teacher own a course.
    // See Database_Schema.md v3.0.
    [Required]
    public string TeacherId { get; set; } = string.Empty;

    public DateTime CreatedDate { get; set; } = DateTime.UtcNow;
}
