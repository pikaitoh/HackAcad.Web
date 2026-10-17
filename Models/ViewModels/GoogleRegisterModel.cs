using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace HackAcad.Web.Models.ViewModels;

public class GoogleRegisterModel
{
    [Required(ErrorMessage = "Enter a username."), StringLength(50, MinimumLength = 2, ErrorMessage = AccountValidation.NameLengthMessage)]
    [RegularExpression(AccountValidation.NamePattern, ErrorMessage = AccountValidation.NameMessage), Display(Name = "Username")]
    public string DisplayName { get => displayName; set => displayName = value?.Trim() ?? ""; }
    private string displayName = "";
    [BindNever]
    public string Email { get; set; } = "";
    public string? ReturnUrl { get; set; }
}
