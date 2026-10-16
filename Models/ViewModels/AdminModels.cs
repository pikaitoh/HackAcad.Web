using System.ComponentModel.DataAnnotations;

namespace HackAcad.Web.Models.ViewModels;

public class UserRow
{
    public string Id { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Email { get; set; } = "";
    public string Role { get; set; } = "";
    public bool IsDisabled { get; set; }
    public DateTime JoinedDate { get; set; }
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

public class EditUserModel
{
    public string Id { get; set; } = "";
    [Required, StringLength(100), Display(Name = "Display name")]
    public string DisplayName { get; set; } = "";
    [Required, EmailAddress, StringLength(256)]
    public string Email { get; set; } = "";
    [Required]
    public string Role { get; set; } = "Student";
    [Display(Name = "Disable this account")]
    public bool IsDisabled { get; set; }
}

public class CreateUserModel : RegisterModel
{
    [Required]
    public string Role { get; set; } = "Student";
}

public class AdminOverviewModel
{
    public int Users { get; set; }
    public int DisabledUsers { get; set; }
    public int Teachers { get; set; }
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
    public string Teacher { get; set; } = "";
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
    [Required, StringLength(150)]
    public string Title { get; set; } = "";
    [StringLength(1000)]
    public string? Description { get; set; }
    [StringLength(50)]
    public string? Category { get; set; }
    [Display(Name = "Publish this course")]
    public bool IsPublished { get; set; }
    public string Teacher { get; set; } = "";
}
