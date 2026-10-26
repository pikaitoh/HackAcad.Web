using System.ComponentModel.DataAnnotations;

namespace HackAcad.Web.Models.Entities;

public class CourseNotebook
{
    [Key]
    public int CourseNotebookId { get; set; }

    public int CourseId { get; set; }

    [Required, StringLength(150)]
    public string Title { get; set; } = "Course Notebook";

    // Independent copy of the notebook blocks JSON in the course curriculum
    public string ContentJson { get; set; } = "{\"version\":1,\"blocks\":[]}";

    public int OrderIndex { get; set; }

    public bool IsPublished { get; set; } = true;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
