using System.ComponentModel.DataAnnotations;

namespace HackAcad.Web.Models.ViewModels;

public class LoginModel
{
    [Required, EmailAddress, StringLength(256)]
    public string Email { get; set; } = "";
    [Required, DataType(DataType.Password)]
    public string Password { get; set; } = "";
    [Display(Name = "Keep me signed in")]
    public bool RememberMe { get; set; }
    public string? ReturnUrl { get; set; }
}

public class RegisterModel
{
    [Required, StringLength(100), Display(Name = "Display name")]
    public string DisplayName { get; set; } = "";
    [Required, EmailAddress, StringLength(256)]
    public string Email { get; set; } = "";
    [Required, StringLength(100, MinimumLength = 8), DataType(DataType.Password)]
    public string Password { get; set; } = "";
    [Required, Compare(nameof(Password)), DataType(DataType.Password), Display(Name = "Confirm password")]
    public string ConfirmPassword { get; set; } = "";
}

public class ProfileModel
{
    [Required, StringLength(100), Display(Name = "Display name")]
    public string DisplayName { get; set; } = "";
    public string Email { get; set; } = "";
    public string Role { get; set; } = "";
    public DateTime JoinedDate { get; set; }
}

public class ChangePasswordModel
{
    [Required, DataType(DataType.Password), Display(Name = "Current password")]
    public string CurrentPassword { get; set; } = "";
    [Required, StringLength(100, MinimumLength = 8), DataType(DataType.Password), Display(Name = "New password")]
    public string NewPassword { get; set; } = "";
    [Required, Compare(nameof(NewPassword)), DataType(DataType.Password), Display(Name = "Confirm new password")]
    public string ConfirmPassword { get; set; } = "";
}

public class DashboardModel
{
    public string DisplayName { get; set; } = "";
    public string Role { get; set; } = "";
    public int EnrollmentCount { get; set; }
    public int CompletedActivities { get; set; }
    public int SolvedChallenges { get; set; }
}
