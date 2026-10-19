using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;

namespace HackAcad.Web.Models.ViewModels;

// These are common provider misspellings, not a list of allowed email domains.
[AttributeUsage(AttributeTargets.Property)]
public sealed class EmailDomainTypoAttribute : ValidationAttribute, IClientModelValidator
{
    private static readonly Dictionary<string, string> Suggestions = CreateSuggestions();

    private static Dictionary<string, string> CreateSuggestions()
    {
        var suggestions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var domain in new[] { "gmail.com", "yahoo.com", "outlook.com", "hotmail.com", "live.com",
            "icloud.com", "me.com", "mac.com", "protonmail.com", "aol.com", "qq.com", "163.com", "126.com" })
        {
            var provider = domain[..domain.LastIndexOf('.')];
            foreach (var suffix in new[] { "c", "con", "cmo", "cm" }) suggestions[$"{provider}.{suffix}"] = domain;
        }
        // Match exact known mistakes only; .co and country-specific domains remain supported.
        suggestions["gmail.co"] = "gmail.com";
        suggestions["gmial.com"] = "gmail.com";
        suggestions["gmai.com"] = "gmail.com";
        suggestions["yaho.com"] = "yahoo.com";
        suggestions["hotmial.com"] = "hotmail.com";
        suggestions["outlok.com"] = "outlook.com";
        foreach (var suffix in new[] { "m", "em", "mee" }) suggestions[$"proton.{suffix}"] = "proton.me";
        return suggestions;
    }
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
