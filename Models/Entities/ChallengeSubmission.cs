using System.ComponentModel.DataAnnotations;

namespace HackAcad.Web.Models.Entities;

public class ChallengeSubmission
{
    [Key]
    public int SubmissionId { get; set; }
    public string UserId { get; set; } = string.Empty;
    public int ChallengeId { get; set; }
    public string SubmittedFlag { get; set; } = string.Empty;
    public bool IsCorrect { get; set; }
    public DateTime SubmittedDate { get; set; } = DateTime.UtcNow;
}
