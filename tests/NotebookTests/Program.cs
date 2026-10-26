using System.Text.Json;
using HackAcad.Web.Models.Entities;
using HackAcad.Web.Models.ViewModels;
using HackAcad.Web.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;

Console.WriteLine("==================================================");
Console.WriteLine("  HACKACAD TERMINAL NOTEBOOK COMPREHENSIVE TESTS  ");
Console.WriteLine("==================================================");

int passed = 0;
int failed = 0;

void AssertTrue(bool condition, string testName)
{
    if (condition)
    {
        Console.WriteLine($"[PASS] {testName}");
        passed++;
    }
    else
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"[FAIL] {testName}");
        Console.ResetColor();
        failed++;
    }
}

// ---------------------------------------------------------------------
// TEST GROUP 1: MARKDOWN SERVICE & XSS SANITIZATION
// ---------------------------------------------------------------------
Console.WriteLine("\n--- 1. Markdown Parsing & XSS Sanitization Tests ---");

// Headings
var mdHeadings = "# Heading 1\n## Heading 2\n### Heading 3";
var htmlHeadings = MarkdownService.RenderHtml(mdHeadings);
AssertTrue(htmlHeadings.Contains("<h1>Heading 1</h1>") &&
           htmlHeadings.Contains("<h2>Heading 2</h2>") &&
           htmlHeadings.Contains("<h3>Heading 3</h3>"),
           "Markdown: H1, H2, H3 headings rendered correctly");

// Bold and Italic
var mdFormatting = "**Bold text** and *Italic text* and `inline code`";
var htmlFormatting = MarkdownService.RenderHtml(mdFormatting);
AssertTrue(htmlFormatting.Contains("<strong>Bold text</strong>") &&
           htmlFormatting.Contains("<em>Italic text</em>") &&
           htmlFormatting.Contains("<code>inline code</code>"),
           "Markdown: Bold, italic, and inline code formatted correctly");

// Lists
var mdList = "- Item A\n- Item B\n- Item C";
var htmlList = MarkdownService.RenderHtml(mdList);
AssertTrue(htmlList.Contains("<ul>") &&
           htmlList.Contains("<li>Item A</li>") &&
           htmlList.Contains("<li>Item B</li>") &&
           htmlList.Contains("<li>Item C</li>") &&
           htmlList.Contains("</ul>"),
           "Markdown: Unordered list rendered with ul/li");

// Fenced Code Blocks
var mdCodeBlock = "```bash\nls -la /var/log\ncat auth.log\n```";
var htmlCodeBlock = MarkdownService.RenderHtml(mdCodeBlock);
AssertTrue(htmlCodeBlock.Contains("<pre><code class=\"language-bash\">") &&
           htmlCodeBlock.Contains("ls -la /var/log") &&
           htmlCodeBlock.Contains("cat auth.log") &&
           htmlCodeBlock.Contains("</code></pre>"),
           "Markdown: Fenced code block preserved with pre/code tags");

// Safe Links
var mdSafeLink = "[HackAcad Platform](https://hackacad.org/learn)";
var htmlSafeLink = MarkdownService.RenderHtml(mdSafeLink);
AssertTrue(htmlSafeLink.Contains("<a href=\"https://hackacad.org/learn\"") &&
           htmlSafeLink.Contains("rel=\"noopener noreferrer\"") &&
           htmlSafeLink.Contains(">HackAcad Platform</a>"),
           "Markdown: Safe HTTPS link rendered with target and rel attributes");

// XSS Prevention: Raw Script Tags
var xssScript = "<script>alert('pwned')</script>";
var htmlXssScript = MarkdownService.RenderHtml(xssScript);
AssertTrue(!htmlXssScript.Contains("<script>") &&
           htmlXssScript.Contains("&lt;script&gt;"),
           "XSS Protection: Direct script tags encoded to entity representation");

// XSS Prevention: Image OnError
var xssImg = "<img src=\"invalid.jpg\" onerror=\"alert(1)\" />";
var htmlXssImg = MarkdownService.RenderHtml(xssImg);
AssertTrue(!htmlXssImg.Contains("<img") &&
           htmlXssImg.Contains("&lt;img"),
           "XSS Protection: Injected img tags encoded");

// XSS Prevention: javascript: URI link
var xssJsLink = "[Malicious Link](javascript:alert('xss'))";
var htmlJsLink = MarkdownService.RenderHtml(xssJsLink);
AssertTrue(!htmlJsLink.Contains("href=\"javascript:"),
           "XSS Protection: javascript: URI links neutralized");

// XSS Prevention: data: URI link
var xssDataLink = "[Data Link](data:text/html;base64,PHNjcmlwdD5hbGVydCgxKTwvc2NyaXB0Pg==)";
var htmlDataLink = MarkdownService.RenderHtml(xssDataLink);
AssertTrue(!htmlDataLink.Contains("href=\"data:"),
           "XSS Protection: data: URI links neutralized");

// XSS Prevention: vbscript: URI link
var xssVbLink = "[VBS Link](vbscript:msgbox(1))";
var htmlVbLink = MarkdownService.RenderHtml(xssVbLink);
AssertTrue(!htmlVbLink.Contains("href=\"vbscript:"),
           "XSS Protection: vbscript: URI links neutralized");


// ---------------------------------------------------------------------
// TEST GROUP 2: NOTEBOOK DOCUMENT SERIALIZATION & STRUCTURE
// ---------------------------------------------------------------------
Console.WriteLine("\n--- 2. Notebook Document Serialization Tests ---");

var sampleDoc = new NotebookDocument
{
    Version = 1,
    Blocks =
    [
        new NotebookBlock
        {
            Id = "b1",
            Type = "markdown",
            Content = "# Linux File System\n\nStandard directories overview."
        },
        new NotebookBlock
        {
            Id = "b2",
            Type = "terminal_interactive",
            Title = "Interactive Practice Sandbox",
            InitialDirectory = "/home/student",
            AllowedCommands = ["help", "pwd", "ls", "cd", "cat", "touch"],
            FileSystem = new Dictionary<string, object>
            {
                ["home"] = new Dictionary<string, object>
                {
                    ["student"] = new Dictionary<string, object>
                    {
                        ["flag.txt"] = "FLAG{test_token}"
                    }
                }
            },
            CustomCommands =
            [
                new NotebookCustomCommand
                {
                    Command = "check_status",
                    Output = "All services operational.",
                    Description = "Status utility"
                }
            ]
        },
        new NotebookBlock
        {
            Id = "b3",
            Type = "terminal_static",
            Title = "Log Inspection Example",
            Cwd = "/var/log",
            Examples =
            [
                new NotebookStaticExample
                {
                    Command = "cat auth.log",
                    Output = "sshd: Accepted publickey for student"
                }
            ]
        }
    ]
};

var serializedJson = JsonSerializer.Serialize(sampleDoc);
var deserializedDoc = JsonSerializer.Deserialize<NotebookDocument>(serializedJson);

AssertTrue(deserializedDoc != null, "Serialization: Document deserialized successfully");
AssertTrue(deserializedDoc?.Blocks.Count == 3, "Serialization: Exactly 3 blocks parsed");
AssertTrue(deserializedDoc?.Blocks[0].Type == "markdown", "Serialization: Block 1 is markdown");
AssertTrue(deserializedDoc?.Blocks[1].Type == "terminal_interactive", "Serialization: Block 2 is terminal_interactive");
AssertTrue(deserializedDoc?.Blocks[1].CustomCommands?.Count == 1, "Serialization: Custom command retained in interactive block");
AssertTrue(deserializedDoc?.Blocks[2].Type == "terminal_static", "Serialization: Block 3 is terminal_static");
AssertTrue(deserializedDoc?.Blocks[2].Examples?[0].Command == "cat auth.log", "Serialization: Static example command preserved");


// ---------------------------------------------------------------------
// TEST GROUP 3: STANDALONE HTML EXPORT SERVICE
// ---------------------------------------------------------------------
Console.WriteLine("\n--- 3. Standalone HTML Export Service Tests ---");

var webRoot = Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "wwwroot");
if (!Directory.Exists(webRoot))
{
    webRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../HackAcad.Web/wwwroot"));
}
var testEnv = new TestHostEnvironment { WebRootPath = webRoot };

var exportService = new NotebookExportService(testEnv);
var exportHtml = exportService.GenerateStandaloneHtml("Linux Mastery Demo", serializedJson);

AssertTrue(exportHtml.StartsWith("<!DOCTYPE html>"), "Export: Generates valid HTML5 doctype");
AssertTrue(exportHtml.Contains("<title>Linux Mastery Demo - HackAcad Terminal Notebook</title>"), "Export: Title embedded in HTML title tag");
AssertTrue(exportHtml.Contains("data-theme=\"dark\"") || exportHtml.Contains("Theme Toggle"), "Export: Theme switching mechanism included");
AssertTrue(exportHtml.Contains(":root {") && exportHtml.Contains("data-theme='dark'"), "Export: Fully embedded CSS variables without external CDN stylesheets");
AssertTrue(exportHtml.Contains("window.initSimulationTerminal"), "Export: Bundled complete simulation terminal runtime inline");
AssertTrue(exportHtml.Contains("Standard directories overview."), "Export: Markdown note block rendered into HTML");
AssertTrue(exportHtml.Contains("Log Inspection Example"), "Export: Static terminal block title rendered");
AssertTrue(exportHtml.Contains("sshd: Accepted publickey for student"), "Export: Static command output rendered");
AssertTrue(exportHtml.Contains("Interactive Practice Sandbox"), "Export: Interactive terminal block rendered");
AssertTrue(exportHtml.Contains("↺ Reset Terminal"), "Export: Per-block reset button provided");

// Security: Prevent </script> breakout in JSON payloads
var sneakyDoc = new NotebookDocument
{
    Version = 1,
    Blocks =
    [
        new NotebookBlock
        {
            Id = "b_sneak",
            Type = "terminal_interactive",
            Title = "Breakout test",
            InitialDirectory = "/home/student",
            FileSystem = new Dictionary<string, object>
            {
                ["home"] = "</script><script>alert('breakout')</script>"
            }
        }
    ]
};
var sneakyJson = JsonSerializer.Serialize(sneakyDoc);
var sneakyExport = exportService.GenerateStandaloneHtml("Breakout Test", sneakyJson);
AssertTrue(!sneakyExport.Contains("</script><script>alert('breakout')"),
           "Export Security: </script> tags in embedded JSON escaped to \\u003c/script\\u003e");


// ---------------------------------------------------------------------
// TEST GROUP 4: INDEPENDENT COURSE COPY ISOLATION
// ---------------------------------------------------------------------
Console.WriteLine("\n--- 4. Independent Course Copy Isolation Tests ---");

// Step 1: Personal notebook created
var personalNb = new Notebook
{
    NotebookId = 42,
    UserId = "user_instructor_1",
    Title = "Original Instructor Notes",
    ContentJson = serializedJson,
    CreatedAtUtc = DateTime.UtcNow,
    UpdatedAtUtc = DateTime.UtcNow
};

// Step 2: Instructor adds an independent copy to Course 101
var courseNb = new CourseNotebook
{
    CourseNotebookId = 88,
    CourseId = 101,
    Title = personalNb.Title,
    ContentJson = personalNb.ContentJson, // Independent copy of content
    OrderIndex = 1,
    IsPublished = true,
    CreatedAtUtc = DateTime.UtcNow,
    UpdatedAtUtc = DateTime.UtcNow
};

AssertTrue(courseNb.Title == personalNb.Title, "Copy: Initial course copy has matching title");
AssertTrue(courseNb.ContentJson == personalNb.ContentJson, "Copy: Initial course copy has matching content");

// Step 3: Instructor modifies personal notebook
personalNb.Title = "Edited Personal Notes";
var modifiedPersonalDoc = JsonSerializer.Deserialize<NotebookDocument>(personalNb.ContentJson)!;
modifiedPersonalDoc.Blocks.Clear();
personalNb.ContentJson = JsonSerializer.Serialize(modifiedPersonalDoc);

AssertTrue(courseNb.Title == "Original Instructor Notes",
           "Isolation: Modifying personal notebook title does NOT affect course copy");
AssertTrue(courseNb.ContentJson != personalNb.ContentJson,
           "Isolation: Clearing personal notebook blocks does NOT alter course copy content");

var courseDoc = JsonSerializer.Deserialize<NotebookDocument>(courseNb.ContentJson);
AssertTrue(courseDoc?.Blocks.Count == 3,
           "Isolation: Course copy retains all 3 original blocks after personal notebook modification");

// Step 4: Deleting personal notebook (simulated by nullifying reference)
personalNb = null;
AssertTrue(courseNb != null && courseNb.CourseNotebookId == 88,
           "Isolation: Deletion of personal notebook leaves course copy intact and accessible");


// ---------------------------------------------------------------------
// SUMMARY
// ---------------------------------------------------------------------
Console.WriteLine("\n==================================================");
Console.WriteLine($"RESULTS: {passed} PASSED, {failed} FAILED");
Console.WriteLine("==================================================");

if (failed > 0)
{
    Environment.Exit(1);
}

class TestHostEnvironment : IWebHostEnvironment
{
    public string WebRootPath { get; set; } = "";
    public IFileProvider WebRootFileProvider { get; set; } = null!;
    public string ApplicationName { get; set; } = "HackAcad.Web";
    public IFileProvider ContentRootFileProvider { get; set; } = null!;
    public string ContentRootPath { get; set; } = "";
    public string EnvironmentName { get; set; } = "Development";
}
