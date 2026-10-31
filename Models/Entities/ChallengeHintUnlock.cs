using System.ComponentModel.DataAnnotations;

namespace HackAcad.Web.Models.Entities;

public class ChallengeHintUnlock
{
    public int ChallengeHintUnlockId { get; set; }

    public string UserId { get; set; } = string.Empty;

    public int ChallengeHintId { get; set; }

    // Preserve the deduction that applied when the hint was unlocked.
    public int PointsDeducted { get; set; }

    public DateTime UnlockedUtc { get; set; } = DateTime.UtcNow;
}