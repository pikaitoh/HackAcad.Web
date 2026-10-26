using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace HackAcad.Web.Services;

/// <summary>
/// Lightweight, safe Markdown to HTML parser and sanitizer.
/// Strictly escapes unsafe HTML and validates links to prevent XSS.
/// </summary>
public static class MarkdownService
{
    private static readonly Regex FencedCodeRegex = new(
        @"```([a-zA-Z0-9_\-]*)\n([\s\S]*?)```",
        RegexOptions.Compiled);

    private static readonly Regex InlineCodeRegex = new(
        @"`([^`\n]+)`",
        RegexOptions.Compiled);

    private static readonly Regex HeadingRegex = new(
        @"^(#{1,6})\s+(.+)$",
        RegexOptions.Compiled | RegexOptions.Multiline);

    private static readonly Regex BoldRegex = new(
        @"\*\*([^\*\n]+)\*\*|__([^_\n]+)__",
        RegexOptions.Compiled);

    private static readonly Regex ItalicRegex = new(
        @"(?<!\*)\*([^\*\n]+)\*(?!\*)|(?<!_)_([^_\n]+)_(?!_)",
        RegexOptions.Compiled);

    private static readonly Regex LinkRegex = new(
        @"\[([^\]]+)\]\(([^)\s]+)\)",
        RegexOptions.Compiled);

    private static readonly Regex BlockquoteRegex = new(
        @"^(?:&gt;|>)\s?(.*)$",
        RegexOptions.Compiled | RegexOptions.Multiline);

    public static string RenderHtml(string? markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown)) return string.Empty;

        // Normalize line endings
        var text = markdown.Replace("\r\n", "\n").Replace("\r", "\n");

        // 1. Extract and protect fenced code blocks before HTML encoding
        var codeBlocks = new List<(string lang, string code)>();
        text = FencedCodeRegex.Replace(text, match =>
        {
            var lang = match.Groups[1].Value.Trim().ToLowerInvariant();
            var code = match.Groups[2].Value;
            var placeholder = $"@@CODEBLOCK_{codeBlocks.Count}@@";
            codeBlocks.Add((lang, code));
            return placeholder;
        });

        // 2. Extract and protect inline code
        var inlineCodes = new List<string>();
        text = InlineCodeRegex.Replace(text, match =>
        {
            var code = match.Groups[1].Value;
            var placeholder = $"@@INLINECODE_{inlineCodes.Count}@@";
            inlineCodes.Add(code);
            return placeholder;
        });

        // 3. HTML encode all remaining text to prevent raw HTML execution / XSS
        text = WebUtility.HtmlEncode(text);

        // 4. Headings (# Heading)
        text = HeadingRegex.Replace(text, match =>
        {
            int level = match.Groups[1].Value.Length;
            string content = match.Groups[2].Value.Trim();
            return $"<h{level}>{content}</h{level}>";
        });

        // 5. Blockquotes (> quote)
        text = BlockquoteRegex.Replace(text, match =>
        {
            return $"<blockquote><p>{match.Groups[1].Value.Trim()}</p></blockquote>";
        });

        // 6. Bold
        text = BoldRegex.Replace(text, match =>
        {
            var content = match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value;
            return $"<strong>{content}</strong>";
        });

        // 7. Italic
        text = ItalicRegex.Replace(text, match =>
        {
            var content = match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value;
            return $"<em>{content}</em>";
        });

        // 8. Safe Links [text](url)
        text = LinkRegex.Replace(text, match =>
        {
            var linkText = match.Groups[1].Value;
            var rawUrl = WebUtility.HtmlDecode(match.Groups[2].Value).Trim();

            if (IsSafeUrl(rawUrl))
            {
                var safeUrl = WebUtility.HtmlEncode(rawUrl);
                return $"<a href=\"{safeUrl}\" target=\"_blank\" rel=\"noopener noreferrer\">{linkText}</a>";
            }

            // Unsafe link URL: render text only without active anchor
            return linkText;
        });

        // 9. Process Lists and Paragraphs
        var lines = text.Split('\n');
        var sb = new StringBuilder();
        bool inUl = false;
        bool inOl = false;
        var paraLines = new List<string>();

        void FlushPara()
        {
            if (paraLines.Count > 0)
            {
                var pContent = string.Join("<br />", paraLines);
                sb.AppendLine($"<p>{pContent}</p>");
                paraLines.Clear();
            }
        }

        void CloseLists()
        {
            if (inUl) { sb.AppendLine("</ul>"); inUl = false; }
            if (inOl) { sb.AppendLine("</ol>"); inOl = false; }
        }

        foreach (var rawLine in lines)
        {
            var line = rawLine.TrimEnd();
            var trimmed = line.Trim();

            // Headings, blockquotes or codeblock placeholders
            if (trimmed.StartsWith("<h") || trimmed.StartsWith("<blockquote") || trimmed.StartsWith("@@CODEBLOCK_"))
            {
                FlushPara();
                CloseLists();
                sb.AppendLine(trimmed);
                continue;
            }

            // Unordered list item: - item or * item
            var ulMatch = Regex.Match(trimmed, @"^[-*]\s+(.+)$");
            if (ulMatch.Success)
            {
                FlushPara();
                if (inOl) { sb.AppendLine("</ol>"); inOl = false; }
                if (!inUl) { sb.AppendLine("<ul>"); inUl = true; }
                sb.AppendLine($"<li>{ulMatch.Groups[1].Value}</li>");
                continue;
            }

            // Ordered list item: 1. item
            var olMatch = Regex.Match(trimmed, @"^\d+\.\s+(.+)$");
            if (olMatch.Success)
            {
                FlushPara();
                if (inUl) { sb.AppendLine("</ul>"); inUl = false; }
                if (!inOl) { sb.AppendLine("<ol>"); inOl = true; }
                sb.AppendLine($"<li>{olMatch.Groups[1].Value}</li>");
                continue;
            }

            // Blank line
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                FlushPara();
                CloseLists();
                continue;
            }

            // Regular paragraph line
            CloseLists();
            paraLines.Add(trimmed);
        }

        FlushPara();
        CloseLists();

        var result = sb.ToString();

        // 10. Restore code blocks with proper escaping
        for (int i = 0; i < codeBlocks.Count; i++)
        {
            var (lang, code) = codeBlocks[i];
            var encodedCode = WebUtility.HtmlEncode(code.TrimEnd('\n', '\r'));
            var langClass = string.IsNullOrEmpty(lang) ? "" : $" class=\"language-{WebUtility.HtmlEncode(lang)}\"";
            var blockHtml = $"<pre><code{langClass}>{encodedCode}</code></pre>";
            result = result.Replace($"@@CODEBLOCK_{i}@@", blockHtml);
        }

        // 11. Restore inline code with proper escaping
        for (int i = 0; i < inlineCodes.Count; i++)
        {
            var encodedInline = WebUtility.HtmlEncode(inlineCodes[i]);
            var inlineHtml = $"<code>{encodedInline}</code>";
            result = result.Replace($"@@INLINECODE_{i}@@", inlineHtml);
        }

        return result;
    }

    private static bool IsSafeUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;

        // Disallow javascript:, data:, vbscript:, etc.
        var lower = url.Trim().ToLowerInvariant();
        if (lower.StartsWith("javascript:") || lower.StartsWith("data:") || lower.StartsWith("vbscript:"))
        {
            return false;
        }

        // Allow relative paths, anchors, and standard safe schemes
        return lower.StartsWith("http://")
            || lower.StartsWith("https://")
            || lower.StartsWith("mailto:")
            || lower.StartsWith("/")
            || lower.StartsWith("./")
            || lower.StartsWith("../")
            || lower.StartsWith("#");
    }
}
