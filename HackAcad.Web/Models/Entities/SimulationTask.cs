using System.ComponentModel.DataAnnotations;

namespace HackAcad.Web.Models.Entities;

public class SimulationTask
{
    [Key]
    public int TaskId { get; set; }

    // Every simulation task belongs to exactly one course — same rule as Challenge.
    public int CourseId { get; set; }

    public string Title { get; set; } = string.Empty;
    public string? Instructions { get; set; }

    // JSON tree describing the fake filesystem the JS terminal reads.
    public string? VirtualFileSystemJson { get; set; }

    // Optional: SHA-256 hash of a flag revealed on completion.
    public string? RewardFlagHash { get; set; }

    public bool IsPublished { get; set; }
}
