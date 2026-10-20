using System.ComponentModel.DataAnnotations;
using HackAcad.Web.Models.Entities;

namespace HackAcad.Web.Models.ViewModels;

public class LoginModel
{
    [Required(ErrorMessage = "Enter your email or verified phone number."), StringLength(256), Display(Name = "Email or phone number")]
    [RegularExpression(@"(?:" + AccountValidation.EmailPattern + @")|(?:^\+[1-9][0-9]{7,14}$)", ErrorMessage = "Enter a valid email or a phone number with its country code, such as +60123456789.")]
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
    [Required(ErrorMessage = "Enter a username."), StringLength(50, MinimumLength = 2, ErrorMessage = AccountValidation.NameLengthMessage), Display(Name = "Username")]
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
    [StringLength(100, ErrorMessage = "Use up to 100 characters for your full name."), Display(Name = "Full name")]
    public string? FullName { get => fullName; set => fullName = string.IsNullOrWhiteSpace(value) ? null : value.Trim(); }
    private string? fullName;
    [Required(ErrorMessage = "Enter a username."), StringLength(50, MinimumLength = 2, ErrorMessage = AccountValidation.NameLengthMessage), Display(Name = "Username")]
    [RegularExpression(AccountValidation.NamePattern, ErrorMessage = AccountValidation.NameMessage)]
    public string DisplayName { get => displayName; set => displayName = value?.Trim() ?? ""; }
    private string displayName = "";
    public string Email { get; set; } = "";
    public string Role { get; set; } = "";
    public DateTime JoinedDate { get; set; }
    public List<BannerChoice> Banners { get; set; } = [];
    public string BannerKey { get; set; } = "foundation";
    public bool HasAvatar { get; set; }
    public bool EmailConfirmed { get; set; }
}

public class PhoneNumberInput : CurrentPasswordModel
{
    [Required(ErrorMessage = "Enter your phone number."), Display(Name = "Phone number")]
    [RegularExpression(@"^\+[1-9][0-9]{7,14}$", ErrorMessage = "Include the country code, such as +60123456789, without spaces.")]
    public string PhoneNumber { get => number; set => number = value?.Trim() ?? ""; }
    private string number = "";
}

public class PhoneCodeInput
{
    [Required(ErrorMessage = "Enter the six-digit code from your SMS."), Display(Name = "Verification code")]
    [RegularExpression(@"^[0-9]{6}$", ErrorMessage = "Enter the six-digit code from your SMS.")]
    public string Code { get; set; } = "";
}

public class AccountSettingsModel
{
    public List<ConnectedAccount> ConnectedAccounts { get; set; } = [];
    public bool TwoFactorEnabled { get; set; }
    public string Email { get; set; } = "";
    public bool EmailConfirmed { get; set; }
    public string? CurrentPhone { get; set; }
    public bool PhoneConfirmed { get; set; }
    public string? PendingPhone { get; set; }
    public int PhoneResendSeconds { get; set; }
    public bool IsSmsTestMode { get; set; }
    public PhoneNumberInput Phone { get; set; } = new();
    public PhoneCodeInput Confirmation { get; set; } = new();
}

public class ChangeEmailModel
{
    public string CurrentEmail { get; set; } = "";
    [Required(ErrorMessage = "Enter your new email address."), EmailAddress(ErrorMessage = AccountValidation.EmailMessage), StringLength(256), Display(Name = "New email")]
    [RegularExpression(AccountValidation.EmailPattern, ErrorMessage = AccountValidation.EmailMessage)]
    public string NewEmail { get => email; set => email = value?.Trim() ?? ""; }
    private string email = "";
    [Required(ErrorMessage = "Enter your current password."), DataType(DataType.Password), Display(Name = "Current password")]
    public string CurrentPassword { get; set; } = "";
}

public class ChangePasswordModel
{
    [Microsoft.AspNetCore.Mvc.ModelBinding.BindNever]
    public bool HasPassword { get; set; } = true;
    [Microsoft.AspNetCore.Mvc.ModelBinding.BindNever]
    public bool RecentExternalLogin { get; set; }
    [Microsoft.AspNetCore.Mvc.ModelBinding.BindNever]
    public List<string> ReauthenticationProviders { get; set; } = [];
    [Microsoft.AspNetCore.Mvc.ModelBinding.BindNever]
    public DateTime? LastLoginUtc { get; set; }
    [Microsoft.AspNetCore.Mvc.ModelBinding.BindNever]
    public DateTime? LastPasswordChangedUtc { get; set; }
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
    public int TotalPoints { get; set; }
    public List<CourseCardViewModel> EnrolledCourses { get; set; } = [];
    public CourseCardViewModel? RecentCourse { get; set; }
    public List<BadgeItem> Badges { get; set; } = [];
    public List<ChallengeSubmission> RecentSubmissions { get; set; } = [];
}
