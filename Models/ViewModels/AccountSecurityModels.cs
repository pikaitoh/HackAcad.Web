using System.ComponentModel.DataAnnotations;
using HackAcad.Web.Models.Entities;

namespace HackAcad.Web.Models.ViewModels;

public class CurrentPasswordModel
{
    [Required(ErrorMessage = "Enter your current password."), DataType(DataType.Password), Display(Name = "Current password")]
    public string CurrentPassword { get; set; } = "";
}

public class SessionsModel : CurrentPasswordModel
{
    public string? CurrentSessionId { get; set; }
    public List<AccountSession> Sessions { get; set; } = [];
}

public class LoginHistoryModel
{
    public List<LoginRecord> Records { get; set; } = [];
    public int Page { get; set; }
    public int Total { get; set; }
}

public class AdminActivityModel
{
    public List<AdminActivity> Records { get; set; } = [];
    public int Page { get; set; }
    public int Total { get; set; }
}

public class TwoFactorModel : CurrentPasswordModel
{
    public bool Enabled { get; set; }
    public bool SmsEnabled { get; set; }
    public bool HasVerifiedPhone { get; set; }
    public int RecoveryCodesLeft { get; set; }
    public string? Code { get; set; }
}

public class EnableAuthenticatorModel
{
    [Required(ErrorMessage = "Enter the six-digit code."), RegularExpression(@"^\d{6}$", ErrorMessage = "Enter the six digits shown in your authenticator app.")]
    public string Code { get; set; } = "";
    [Microsoft.AspNetCore.Mvc.ModelBinding.Validation.ValidateNever]
    public string SharedKey { get; set; } = "";
    [Microsoft.AspNetCore.Mvc.ModelBinding.Validation.ValidateNever]
    public string QrImage { get; set; } = "";
}

public class TwoFactorLoginModel
{
    [Required(ErrorMessage = "Enter your verification code."), StringLength(32)]
    public string Code { get; set; } = "";
    public bool RememberMe { get; set; }
    public string? ReturnUrl { get; set; }
}
