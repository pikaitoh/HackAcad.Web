namespace HackAcad.Web.Models.Entities;

public class Enrollment
{
    public int EnrollmentId { get; set; }

    // Points at AspNetUsers, not Students — a Instructor can also enrol and
    // learn (spec v2.0, Section 2). Only course OWNERSHIP is Instructor-restricted.
    public string UserId { get; set; } = string.Empty;

    public int CourseId { get; set; }

    // Denormalized "resume pointer" cache. Progress is still the authoritative
    // record — update both together or this drifts out of sync.
    public int? LastLessonId { get; set; }

    public string Status { get; set; } = "InProgress";
    public DateTime EnrolledDate { get; set; } = DateTime.UtcNow;
}
