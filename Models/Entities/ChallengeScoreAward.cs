using System.ComponentModel.DataAnnotations;

namespace HackAcad.Web.Models.Entities;

public class ChallengeScoreAward
{
    public int ChallengeScoreAwardId { get; set; }

    public string UserId { get; set; } = string.Empty;

    public int ChallengeId { get; set; }

    // The challenge's maximum points when the learner solved it.
    public int MaximumPointsSnapshot { get; set; }

    // Total hint deductions applied when the learner solved it.
    public int TotalDeductionSnapshot { get; set; }

    // Final points awarded after deductions (minimum 0).
    public int AwardedPoints { get; set; }

    public DateTime CompletedUtc { get; set; } = DateTime.UtcNow;
}