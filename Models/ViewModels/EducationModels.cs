using System.ComponentModel.DataAnnotations;
using HackAcad.Web.Models.Entities;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;

namespace HackAcad.Web.Models.ViewModels;

public class TeachingExperience
{
    [Required, StringLength(150)] public string Institution { get; set; } = "";
    [Required, StringLength(100)] public string Position { get; set; } = "";
    [Required, StringLength(200)] public string Subjects { get; set; } = "";
    [DataType(DataType.Date)] public DateOnly? StartDate { get; set; }
    [DataType(DataType.Date)] public DateOnly? EndDate { get; set; }
}

public class InstructorApplicationInput
{
    [Required, StringLength(100), Display(Name = "Full name")] public string FullName { get; set; } = "";
    [Required, EmailAddress, StringLength(256), Display(Name = "Work / professional email")] public string WorkEmail { get; set; } = "";
    [Required, StringLength(200), Display(Name = "Topics you want to teach")] public string TeachingTopics { get; set; } = "";
    [MinLength(1), MaxLength(10)] public List<TeachingExperience> Experience { get; set; } = [new()];
    [StringLength(3000), Display(Name = "Supporting qualifications, projects or teaching samples (optional)")]
    public string? SupportingInformation { get; set; }
    [StringLength(2000), Display(Name = "Response to the previous review (optional)")] public string? SubmissionNote { get; set; }
    [Display(Name = "Teaching experience evidence (PDF, up to 5 MB)")] public IFormFile? Evidence { get; set; }
    [Range(typeof(bool), "true", "true", ErrorMessage = "Confirm that the information and evidence are accurate.")]
    public bool Declaration { get; set; }
}

public class ApplicationDetailsModel
{
    public InstructorApplication Application { get; set; } = null!;
    public string ApplicantEmail { get; set; } = "";
    public List<TeachingExperience> Experience { get; set; } = [];
    public List<InstructorApplication> History { get; set; } = [];
    public List<InstructorReview> Reviews { get; set; } = [];
    public InstructorReviewInput Review { get; set; } = new();
    public bool IsLatest { get; set; }
}

public class InstructorReviewInput
{
    [Required] public string Decision { get; set; } = "";
    [Required, StringLength(100)] public string ReasonCategory { get; set; } = "";
    [Required, StringLength(1000, MinimumLength = 10), Display(Name = "Decision explanation (visible to the applicant)")]
    public string Reason { get; set; } = "";
    [StringLength(2000), Display(Name = "Internal verification sources and findings")]
    public string? VerificationNotes { get; set; }
    [Range(0, 80), Display(Name = "Verified years of relevant teaching")]
    public decimal VerifiedYears { get; set; }
    public bool RelevantTeaching { get; set; }
    public bool ExperienceVerified { get; set; }
    public bool EvidenceVerified { get; set; }
    public bool DetailsConsistent { get; set; }
}

public class InstructorApplicationListModel
{
    public string? Search { get; set; }
    public string? Status { get; set; }
    public int Page { get; set; } = 1;
    public int Total { get; set; }
    public int PageCount => Math.Max(1, (int)Math.Ceiling(Total / 20d));
    public List<InstructorApplication> Applications { get; set; } = [];
}

public class AccountStatusInput
{
    public bool Disable { get; set; }
    [Required, StringLength(500, MinimumLength = 10)] public string Reason { get; set; } = "";
}

public class AuditLogModel
{
    public string? Search { get; set; }
    public bool Mine { get; set; }
    public int Page { get; set; } = 1;
    public int Total { get; set; }
    public int PageCount => Math.Max(1, (int)Math.Ceiling(Total / 20d));
    public List<AdminActivity> Records { get; set; } = [];
    public Dictionary<string, string> Actors { get; set; } = [];
}
