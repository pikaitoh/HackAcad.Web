using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;

namespace HackAcad.Web.Models.ViewModels;

// These are common provider misspellings, not a list of allowed email domains.
[AttributeUsage(AttributeTargets.Property)]
public sealed class EmailDomainTypoAttribute : ValidationAttribute, IClientModelValidator
{
    private static readonly Dictionary<string, string> Suggestions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["gmail.c"] = "gmail.com", ["gmail.co"] = "gmail.com", ["gmail.con"] = "gmail.com",
        ["gmail.cmo"] = "gmail.com", ["gmial.com"] = "gmail.com", ["gmai.com"] = "gmail.com",
        ["hotmail.con"] = "hotmail.com", ["outlook.con"] = "outlook.com", ["yahoo.con"] = "yahoo.com"
    };
    private const string Message = "Did you mean {0}? Check your email domain.";

    protected override ValidationResult? IsValid(object? value, ValidationContext context)
    {
        var email = (value as string)?.Trim() ?? "";
        var domain = email[(email.LastIndexOf('@') + 1)..];
        return Suggestions.TryGetValue(domain, out var suggestion)
            ? new ValidationResult(string.Format(Message, suggestion)) : ValidationResult.Success;
    }

    public void AddValidation(ClientModelValidationContext context)
    {
        context.Attributes.TryAdd("data-val", "true");
        context.Attributes.TryAdd("data-val-emaildomain", Message);
        context.Attributes.TryAdd("data-val-emaildomain-map", JsonSerializer.Serialize(Suggestions));
    }
}
