using System.ComponentModel.DataAnnotations;

namespace HackAcad.Web.Models.Entities;

// ADMIN - Application revisions preserve submitted evidence; separate review records preserve Admin decisions.

public static class ApplicationStatus
{
    public const string Pending = "Pending";
    public const string MoreInformation = "Needs more information";
    public const string Approved = "Approved";
    public const string Rejected = "Rejected";
}

// Each resubmission creates a new immutable set of applicant evidence.
public class InstructorApplication
{
    public long Id { get; set; }
    [MaxLength(255)] public string UserId { get; set; } = "";
    public int Revision { get; set; }
    [MaxLength(100)] public string FullName { get; set; } = "";
    [MaxLength(256)] public string WorkEmail { get; set; } = "";
    [MaxLength(200)] public string TeachingTopics { get; set; } = "";
    [MaxLength(6000)] public string ExperienceJson { get; set; } = "[]";
    [MaxLength(3000)] public string SupportingInformation { get; set; } = "";
    [MaxLength(2000)] public string SubmissionNote { get; set; } = "";
    public byte[] EvidencePdf { get; set; } = [];
    [MaxLength(150)] public string EvidenceName { get; set; } = "evidence.pdf";
    public int TeachingDays { get; set; }
    [MaxLength(32)] public string Status { get; set; } = ApplicationStatus.Pending;
    public DateTime SubmittedUtc { get; set; } = DateTime.UtcNow;
}

// Append-only in the application: there are no edit/delete review endpoints.
public class InstructorReview
{
    public long Id { get; set; }
    public long ApplicationId { get; set; }
    [MaxLength(255)] public string ReviewerId { get; set; } = "";
    [MaxLength(150)] public string ReviewerName { get; set; } = "";
    [MaxLength(32)] public string Decision { get; set; } = "";
    [MaxLength(100)] public string ReasonCategory { get; set; } = "";
    [MaxLength(1000)] public string Reason { get; set; } = "";
    [MaxLength(2000)] public string VerificationNotes { get; set; } = "";
    public decimal VerifiedYears { get; set; }
    public bool RelevantTeaching { get; set; }
    public bool ExperienceVerified { get; set; }
    public bool EvidenceVerified { get; set; }
    public bool DetailsConsistent { get; set; }
    public DateTime ReviewedUtc { get; set; } = DateTime.UtcNow;
}
