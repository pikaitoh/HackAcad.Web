using System.ComponentModel.DataAnnotations;
using HackAcad.Web.Models.Entities;

namespace HackAcad.Web.Models.ViewModels;

// ADMIN - Keep the review form separate from the course entity so posted fields cannot overwrite course ownership.

public class CourseReviewInput : IValidatableObject
{
    [Required, StringLength(32)] public string Version { get; set; } = "";
    [Required(ErrorMessage = "Choose a decision."), RegularExpression("Approved|Rejected", ErrorMessage = "Choose Approve or Reject.")]
    public string Decision { get; set; } = "";
    [StringLength(1000, ErrorMessage = "Use no more than 1,000 characters for the reason.")]
    public string? Reason { get; set; }
    public IEnumerable<ValidationResult> Validate(ValidationContext context)
    {
        // ADMIN - Rejection needs actionable feedback; approval feedback is optional.
        if (Decision == CourseReviewStatus.Rejected && string.IsNullOrWhiteSpace(Reason))
            yield return new ValidationResult("Explain why this course is rejected so the instructor can revise it.", [nameof(Reason)]);
    }
}

public class CourseReviewModel
{
    public Course Course { get; set; } = null!;
    public string Instructor { get; set; } = "";
    public CourseReviewInput Input { get; set; } = new();
    public List<Lesson> Lessons { get; set; } = [];
    public List<Challenge> Challenges { get; set; } = [];
    public List<SimulationTask> Simulations { get; set; } = [];
    public List<CourseNotebook> Notebooks { get; set; } = [];
}
