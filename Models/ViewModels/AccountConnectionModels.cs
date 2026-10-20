using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace HackAcad.Web.Models.ViewModels;

public class ConnectedAccount
{
    public string Provider { get; set; } = "";
    public string? Email { get; set; }
    public string? Name { get; set; }
    public bool CanDisconnect { get; set; }
}

public class DisconnectModel
{
    [BindNever] public string Provider { get; set; } = "";
    [BindNever] public string? Account { get; set; }
    [BindNever] public bool CanDisconnect { get; set; }
    [BindNever] public bool HasPassword { get; set; }
    [BindNever] public List<string> RemainingProviders { get; set; } = [];
    [BindNever] public bool IdentityVerified { get; set; }
    [BindNever] public bool Ready { get; set; }
    [BindNever] public DateTime? VerificationExpiresUtc { get; set; }
    [BindNever] public bool SmsEnabled { get; set; }
    [BindNever] public string VerifiedMethod { get; set; } = "";
    [StringLength(100), DataType(DataType.Password), Display(Name = "Current password")]
    public string? CurrentPassword { get; set; }
    [StringLength(32), Display(Name = "Verification or recovery code")]
    public string? Code { get; set; }
    public bool Confirm { get; set; }
}
