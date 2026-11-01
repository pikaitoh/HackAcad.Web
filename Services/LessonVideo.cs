using System.Text.RegularExpressions;
using Microsoft.AspNetCore.WebUtilities;

namespace HackAcad.Web.Services;

// Convert supported YouTube links to one trusted player origin; never frame an arbitrary URL.
public static class LessonVideo
{
    public static string? YouTubeId(string? value)
    {
        if (!Uri.TryCreate(value?.Trim(), UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http") || !string.IsNullOrEmpty(uri.UserInfo) || !uri.IsDefaultPort) return null;
        var host = uri.IdnHost.ToLowerInvariant();
        var parts = uri.AbsolutePath.Trim('/').Split('/');
        var id = host is "youtu.be" or "www.youtu.be" && parts.Length == 1 ? parts[0]
            : host is "youtube.com" or "www.youtube.com" or "m.youtube.com" or "youtube-nocookie.com" or "www.youtube-nocookie.com"
                ? uri.AbsolutePath == "/watch" ? QueryHelpers.ParseQuery(uri.Query)["v"].FirstOrDefault()
                    : parts.Length == 2 && parts[0] is "embed" or "shorts" or "live" ? parts[1] : null
                : null;
        return id is not null && Regex.IsMatch(id, "^[A-Za-z0-9_-]{11}$") ? id : null;
    }

    public static string? EmbedUrl(string? value) => YouTubeId(value) is { } id ? $"https://www.youtube-nocookie.com/embed/{id}?rel=0&hl=en" : null;
    public static string? WatchUrl(string? value) => YouTubeId(value) is { } id ? $"https://www.youtube.com/watch?v={id}" : null;
    public static string? DirectUrl(string? value) => Uri.TryCreate(value?.Trim(), UriKind.Absolute, out var uri)
        && uri.Scheme is "https" or "http" && string.IsNullOrEmpty(uri.UserInfo)
        && new[] { ".mp4", ".webm", ".ogg" }.Any(extension => uri.AbsolutePath.EndsWith(extension, StringComparison.OrdinalIgnoreCase)) ? uri.AbsoluteUri : null;
    public static bool IsSupported(string? value) => string.IsNullOrWhiteSpace(value) || EmbedUrl(value) is not null || DirectUrl(value) is not null;
}
