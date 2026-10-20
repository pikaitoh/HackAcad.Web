using System.ComponentModel.DataAnnotations;

namespace HackAcad.Web.Models.ViewModels;

public class UserRow
{
    public string Id { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string? FullName { get; set; }
    public string Email { get; set; } = "";
    public string Role { get; set; } = "";
    public bool IsDisabled { get; set; }
    public bool IsReadOnlyAdmin { get; set; }
    public bool EmailConfirmed { get; set; }
    public DateTime JoinedDate { get; set; }
    [Microsoft.AspNetCore.Mvc.ModelBinding.BindNever]
    public DateTime? LastLoginUtc { get; set; }
    [Microsoft.AspNetCore.Mvc.ModelBinding.BindNever]
    public DateTime? LastPasswordChangedUtc { get; set; }
}

public class UserListModel
{
    public string? Search { get; set; }
    public string? Role { get; set; }
    public string? Status { get; set; }
    public int Page { get; set; } = 1;
    public int Total { get; set; }
    public int PageCount => Math.Max(1, (int)Math.Ceiling(Total / 20d));
    public List<UserRow> Users { get; set; } = [];
}

public class AdminOverviewModel
{
    public int PendingApplications { get; set; }
    public int Users { get; set; }
    public int DisabledUsers { get; set; }
    public int Instructors { get; set; }
    public int Courses { get; set; }
    public int PublishedCourses { get; set; }
    public int Enrollments { get; set; }
    public List<UserRow> RecentUsers { get; set; } = [];
}

public class CourseRow
{
    public int CourseId { get; set; }
    public string Title { get; set; } = "";
    public string? Category { get; set; }
    public string Instructor { get; set; } = "";
    public bool IsPublished { get; set; }
    public int Enrollments { get; set; }
}

public class CourseListModel
{
    public string? Search { get; set; }
    public string? Status { get; set; }
    public int Page { get; set; } = 1;
    public int Total { get; set; }
    public int PageCount => Math.Max(1, (int)Math.Ceiling(Total / 20d));
    public List<CourseRow> Courses { get; set; } = [];
}

public class EditCourseModel
{
    public int CourseId { get; set; }
    [Required(ErrorMessage = "Enter a course title."), StringLength(150, ErrorMessage = "Use no more than 150 characters for the title.")]
    public string Title { get => title; set => title = value?.Trim() ?? ""; }
    private string title = "";
    [StringLength(1000, ErrorMessage = "Use no more than 1,000 characters for the description.")]
    public string? Description { get => description; set => description = string.IsNullOrWhiteSpace(value) ? null : value.Trim(); }
    private string? description;
    [StringLength(50, ErrorMessage = "Use no more than 50 characters for the category.")]
    public string? Category { get => category; set => category = string.IsNullOrWhiteSpace(value) ? null : value.Trim(); }
    private string? category;
    [Display(Name = "Publish this course")]
    public bool IsPublished { get; set; }
    public string Instructor { get; set; } = "";
}

public class LoginLogRow
{
    public long Id { get; set; }
    public string UserId { get; set; } = "";
    public string Username { get; set; } = "";
    public string Email { get; set; } = "";
    public DateTime OccurredUtc { get; set; }
    public string Outcome { get; set; } = "";
    public string Device { get; set; } = "";
    public string IpAddress { get; set; } = "";
}

public class LoginLogsModel
{
    public string? Search { get; set; }
    public string? Result { get; set; }
    public DateOnly? From { get; set; }
    public DateOnly? To { get; set; }
    public int Page { get; set; } = 1;
    public int Total { get; set; }
    public int PageCount => Math.Max(1, (int)Math.Ceiling(Total / 20d));
    public List<LoginLogRow> Records { get; set; } = [];
}
