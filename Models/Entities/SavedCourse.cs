namespace HackAcad.Web.Models.Entities;

public class SavedCourse
{
    public int SavedCourseId { get; set; }
    public string UserId { get; set; } = string.Empty;
    public int CourseId { get; set; }
    public DateTime SavedUtc { get; set; } = DateTime.UtcNow;
}
