using System.ComponentModel.DataAnnotations;

namespace HackAcad.Web.Models.Entities;

public class Challenge
{
    public int ChallengeId { get; set; }

    // Every challenge belongs to exactly one course — no standalone challenges.
    public int CourseId { get; set; }

    [Required, StringLength(150)]
    public string Title { get; set; } = string.Empty;

    [StringLength(50)]
    public string? Category { get; set; }

    [StringLength(20)]
    public string? Difficulty { get; set; }

    [StringLength(2000)]
    public string? Description { get; set; }

    // SHA-256 hash of the correct flag — never store the plaintext flag.
    [Required, StringLength(256)]
    public string FlagHash { get; set; } = string.Empty;

    public int Points { get; set; }

    [StringLength(300)]
    public string? FilePath { get; set; }

    public bool IsPublished { get; set; }
}
