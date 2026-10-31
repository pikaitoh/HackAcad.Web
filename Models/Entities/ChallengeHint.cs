using System.ComponentModel.DataAnnotations;

namespace HackAcad.Web.Models.Entities;

public class ChallengeHint
{
    public int ChallengeHintId { get; set; }

    public int ChallengeId { get; set; }

    [Required]
    [StringLength(2000)]
    public string HintText { get; set; } = string.Empty;

    public int DisplayOrder { get; set; }

    [Range(0, 1000)]
    public int DeductionPoints { get; set; }
}