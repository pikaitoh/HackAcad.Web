using System.ComponentModel.DataAnnotations;
using HackAcad.Web.Models.Entities;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;

namespace HackAcad.Web.Models.ViewModels;

public class TeachingExperience
{
    [Required, StringLength(150)] public string Institution { get; set; } = "";
    [Required, StringLength(100)] public string Position { get; set; } = "";
    [Required, StringLength(200, MinimumLength = 10, ErrorMessage = "Describe the subjects you taught using 10–200 characters.")]
    public string Subjects { get => subjects; set => subjects = value?.Trim() ?? ""; }
    private string subjects = "";
    [Required(ErrorMessage = "Enter a start date."), DataType(DataType.Date)] public DateOnly? StartDate { get; set; }
    [DataType(DataType.Date)] public DateOnly? EndDate { get; set; }
}

public class InstructorApplicationInput
{
    [Required, StringLength(100), Display(Name = "Full name")]
    [RegularExpression(@"^(?=.*\p{L})[\p{L}\p{M}\p{Zs} .'\u2019/()\u00B7-]+$", ErrorMessage = "Enter your name using letters, spaces and name punctuation, without numbers.")]
    public string FullName { get => fullName; set => fullName = value?.Trim() ?? ""; }
    private string fullName = "";
    [Required(ErrorMessage = "Enter your work or professional email."), EmailAddress(ErrorMessage = "Please enter a valid email address."), EmailDomainTypo, StringLength(256), Display(Name = "Work / professional email")] public string WorkEmail { get => workEmail; set => workEmail = value?.Trim() ?? ""; }
    private string workEmail = "";
    [Required, StringLength(200, MinimumLength = 10, ErrorMessage = "Describe the topics you want to teach using 10–200 characters."), Display(Name = "Topics you want to teach")]
    public string TeachingTopics { get => teachingTopics; set => teachingTopics = value?.Trim() ?? ""; }
    private string teachingTopics = "";
    [Required, MinLength(1), MaxLength(10)] public List<TeachingExperience> Experience { get; set; } = [new()];
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
    [Required(ErrorMessage = "Please enter a reason for this account access change."), StringLength(500, MinimumLength = 10, ErrorMessage = "Please enter between 10 and 500 characters.")] public string Reason { get; set; } = "";
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
