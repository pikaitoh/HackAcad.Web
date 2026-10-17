using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace HackAcad.Web.Models.ViewModels;

public static class AccountValidation
{
    public const string EmailPattern = @"^[A-Za-z0-9!#$%&'*+/=?^_`{|}~-]+(?:\.[A-Za-z0-9!#$%&'*+/=?^_`{|}~-]+)*@(?:[A-Za-z0-9](?:[A-Za-z0-9-]{0,61}[A-Za-z0-9])?\.)+[A-Za-z]{2,63}$";
    public const string EmailMessage = "Enter a complete email address, such as name@example.com.";
    public const string NamePattern = @"^(?=.*[\p{L}\p{N}])[\p{L}\p{M}\p{N} .'’_-]+$";
    public const string NameMessage = "Use letters, numbers, spaces, apostrophes, dots, underscores or hyphens, with at least one letter or number.";
    public const string NameLengthMessage = "Use 2–50 characters for your display name.";
    public const string PasswordPattern = @"^(?=[\s\S]*[a-z])(?=[\s\S]*[A-Z])(?=[\s\S]*[0-9])(?=[\s\S]*[^a-zA-Z0-9])[\s\S]{8,100}$";
    public const string PasswordMessage = "Use 8–100 characters with an uppercase letter, a lowercase letter, a number and a symbol.";

    public static void AddIdentityErrors(ModelStateDictionary state, IdentityResult result, string passwordField = "Password")
    {
        foreach (var error in result.Errors)
        {
            var field = error.Code switch
            {
                "DuplicateEmail" or "DuplicateUserName" or "InvalidEmail" or "InvalidUserName" => "Email",
                "PasswordMismatch" => "CurrentPassword",
                _ when error.Code?.StartsWith("Password", StringComparison.Ordinal) == true => passwordField,
                _ => ""
            };
            // Profile has no editable email/password field; keep unexpected errors visible.
            state.AddModelError(state.ContainsKey(field) ? field : "", error.Description);
        }
    }
}
