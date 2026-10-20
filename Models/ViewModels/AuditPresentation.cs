using System.Text.Json;
using System.Text.RegularExpressions;

namespace HackAcad.Web.Models.ViewModels;

public static class AuditPresentation
{
    public static string Summary(HackAcad.Web.Models.Entities.AdminActivity record)
    {
        if (record.Action == "Reviewed Instructor application") {
            var review = Regex.Match(record.Details ?? "", @"^(.+?); revision (\d+)\.");
            if (review.Success) return $"{review.Groups[1].Value} · Revision {review.Groups[2].Value}";
        }
        return Summary(record.Details);
    }
    public static string Recorded(string? value) => string.IsNullOrWhiteSpace(value) ? "Not recorded" : value;
    public static string Summary(string? value) => string.IsNullOrWhiteSpace(value) ? "Not recorded" : value.Length > 120 ? value[..120] + "…" : value;
    public static IEnumerable<(string Field, string Before, string After)> Changes(string? before, string? after)
    {
        var old = Fields(before); var current = Fields(after);
        return old.Keys.Concat(current.Keys).Distinct().Select(key =>
            (Regex.Replace(key, "(?<=[a-z])(?=[A-Z])", " "), old.GetValueOrDefault(key, "Not recorded"), current.GetValueOrDefault(key, "Not recorded")));
    }
    private static Dictionary<string, string> Fields(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return new();
        try {
            using var json = JsonDocument.Parse(value);
            if (json.RootElement.ValueKind == JsonValueKind.Object)
                return json.RootElement.EnumerateObject().ToDictionary(p => p.Name, p => Display(p.Value));
        } catch (JsonException) { }
        return new() { ["Value"] = value };
    }
    private static string Display(JsonElement value) => value.ValueKind switch {
        JsonValueKind.Null or JsonValueKind.Undefined => "Not recorded",
        JsonValueKind.True => "Yes", JsonValueKind.False => "No",
        JsonValueKind.String => Recorded(value.GetString()), _ => value.ToString()
    };
}
