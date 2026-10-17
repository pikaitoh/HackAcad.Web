using System.ComponentModel.DataAnnotations;

namespace HackAcad.Web.Models.ViewModels;

public class LoginModel
{
    [Required(ErrorMessage = "Enter your email address."), EmailAddress(ErrorMessage = AccountValidation.EmailMessage), StringLength(256)]
    [RegularExpression(AccountValidation.EmailPattern, ErrorMessage = AccountValidation.EmailMessage)]
    public string Email { get => email; set => email = value?.Trim() ?? ""; }
    private string email = "";
    [Required, DataType(DataType.Password)]
    public string Password { get; set; } = "";
    [Display(Name = "Keep me signed in")]
    public bool RememberMe { get; set; }
    public string? ReturnUrl { get; set; }
}

public class RegisterModel
{
    [Required(ErrorMessage = "Enter a display name."), StringLength(50, MinimumLength = 2, ErrorMessage = AccountValidation.NameLengthMessage), Display(Name = "Display name")]
    [RegularExpression(AccountValidation.NamePattern, ErrorMessage = AccountValidation.NameMessage)]
    public string DisplayName { get => displayName; set => displayName = value?.Trim() ?? ""; }
    private string displayName = "";
    [Required(ErrorMessage = "Enter your email address."), EmailAddress(ErrorMessage = AccountValidation.EmailMessage), StringLength(256)]
    [RegularExpression(AccountValidation.EmailPattern, ErrorMessage = AccountValidation.EmailMessage)]
    public string Email { get => email; set => email = value?.Trim() ?? ""; }
    private string email = "";
    [Required(ErrorMessage = "Enter a password."), StringLength(100, MinimumLength = 8, ErrorMessage = AccountValidation.PasswordMessage), DataType(DataType.Password)]
    [RegularExpression(AccountValidation.PasswordPattern, ErrorMessage = AccountValidation.PasswordMessage)]
    public string Password { get; set; } = "";
    [Required, Compare(nameof(Password), ErrorMessage = "Passwords do not match."), DataType(DataType.Password), Display(Name = "Confirm password")]
    public string ConfirmPassword { get; set; } = "";
}

public class ProfileModel
{
    [Required(ErrorMessage = "Enter a display name."), StringLength(50, MinimumLength = 2, ErrorMessage = AccountValidation.NameLengthMessage), Display(Name = "Display name")]
    [RegularExpression(AccountValidation.NamePattern, ErrorMessage = AccountValidation.NameMessage)]
    public string DisplayName { get => displayName; set => displayName = value?.Trim() ?? ""; }
    private string displayName = "";
    public string Email { get; set; } = "";
    public string Role { get; set; } = "";
    public DateTime JoinedDate { get; set; }
}

public class ChangePasswordModel
{
    [Required, DataType(DataType.Password), Display(Name = "Current password")]
    public string CurrentPassword { get; set; } = "";
    [Required(ErrorMessage = "Enter a new password."), StringLength(100, MinimumLength = 8, ErrorMessage = AccountValidation.PasswordMessage), DataType(DataType.Password), Display(Name = "New password")]
    [RegularExpression(AccountValidation.PasswordPattern, ErrorMessage = AccountValidation.PasswordMessage)]
    public string NewPassword { get; set; } = "";
    [Required, Compare(nameof(NewPassword), ErrorMessage = "Passwords do not match."), DataType(DataType.Password), Display(Name = "Confirm new password")]
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
