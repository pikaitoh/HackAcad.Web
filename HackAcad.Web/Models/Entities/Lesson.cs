using System.ComponentModel.DataAnnotations;

namespace HackAcad.Web.Models.Entities;

public class Lesson
{
    public int LessonId { get; set; }

    public int CourseId { get; set; }

    [Required, StringLength(150)]
    public string Title { get; set; } = string.Empty;

    public string? Content { get; set; }

    public int OrderIndex { get; set; }

    [StringLength(300)]
    public string? VideoUrl { get; set; }
}
