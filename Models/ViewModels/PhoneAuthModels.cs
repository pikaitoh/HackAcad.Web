using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace HackAcad.Web.Models.ViewModels;

public class ForgotPasswordModel
{
    [Required(ErrorMessage = "Enter your email address."), EmailAddress(ErrorMessage = AccountValidation.EmailMessage), StringLength(256), Display(Name = "Email address")]
    [RegularExpression(AccountValidation.EmailPattern, ErrorMessage = AccountValidation.EmailMessage)]
    public string Email { get => email; set => email = value?.Trim() ?? ""; }
    private string email = "";
}

public class SmsChallengeModel : PhoneCodeInput
{
    [BindNever] public string Heading { get; set; } = "Enter your code";
    [BindNever] public string Description { get; set; } = "";
    [BindNever] public string SubmitAction { get; set; } = "";
    [BindNever] public string ResendAction { get; set; } = "";
    [BindNever] public string BackAction { get; set; } = "Login";
    [BindNever] public int ResendSeconds { get; set; }
    [BindNever] public bool IsTestMode { get; set; }
    [BindNever] public bool SettingsLayout { get; set; }
    public bool RememberMe { get; set; }
    public string? ReturnUrl { get; set; }
}

public class ResetPasswordModel
{
    [BindNever] public bool InvalidLink { get; set; }
    [Required(ErrorMessage = "Enter a new password."), StringLength(100, MinimumLength = 8, ErrorMessage = AccountValidation.PasswordMessage), DataType(DataType.Password), Display(Name = "New password")]
    [RegularExpression(AccountValidation.PasswordPattern, ErrorMessage = AccountValidation.PasswordMessage)]
    public string NewPassword { get; set; } = "";
    [Required, Compare(nameof(NewPassword), ErrorMessage = "Passwords do not match."), DataType(DataType.Password), Display(Name = "Confirm new password")]
    public string ConfirmPassword { get; set; } = "";
}
