namespace HackAcad.Web.Models.Entities;

// A row is about exactly ONE of LessonId / ChallengeId / SimulationTaskId,
// indicated by ContentType. See Database_Schema.md v3.0 for the full rules
// and the matching CHECK constraint in schema.sql.
public enum ProgressContentType
{
    Lesson,
    Challenge,
    Simulation
}

public class Progress
{
    public int ProgressId { get; set; }
    public int EnrollmentId { get; set; }
    public ProgressContentType ContentType { get; set; }

    // Exactly one of these three is set, matching ContentType.
    public int? LessonId { get; set; }
    public int? ChallengeId { get; set; }
    public int? SimulationTaskId { get; set; }

    public bool IsCompleted { get; set; }
    // Stored in UTC and set by the activity completion handlers.
    public DateTime LastUpdatedDate { get; set; } = DateTime.UtcNow;
}
