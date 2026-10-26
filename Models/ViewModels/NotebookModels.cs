using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HackAcad.Web.Models.ViewModels;

public class NotebookDocument
{
    [JsonPropertyName("version")]
    public int Version { get; set; } = 1;

    [JsonPropertyName("blocks")]
    public List<NotebookBlock> Blocks { get; set; } = [];
}

public class NotebookBlock
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString("n");

    [JsonPropertyName("type")]
    public string Type { get; set; } = "markdown"; // "markdown" | "terminal_interactive" | "terminal_static"

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    // Markdown content
    [JsonPropertyName("content")]
    public string? Content { get; set; }

    // Interactive terminal configuration
    [JsonPropertyName("initialDirectory")]
    public string? InitialDirectory { get; set; } = "/home/student";

    [JsonPropertyName("allowedCommands")]
    public List<string>? AllowedCommands { get; set; }

    [JsonPropertyName("fileSystem")]
    public object? FileSystem { get; set; }

    [JsonPropertyName("customCommands")]
    public List<NotebookCustomCommand>? CustomCommands { get; set; }

    // Static terminal example configuration
    [JsonPropertyName("cwd")]
    public string? Cwd { get; set; } = "/home/student";

    [JsonPropertyName("examples")]
    public List<NotebookStaticExample>? Examples { get; set; }
}

public class NotebookCustomCommand
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("command")]
    public string Command { get; set; } = string.Empty;

    [JsonPropertyName("output")]
    public string Output { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;
}

public class NotebookStaticExample
{
    [JsonPropertyName("command")]
    public string Command { get; set; } = string.Empty;

    [JsonPropertyName("output")]
    public string Output { get; set; } = string.Empty;
}

public class NotebookSaveModel
{
    [Required, StringLength(150)]
    public string Title { get; set; } = "Untitled Notebook";

    public string? ContentJson { get; set; }
}

public class NotebookListItemViewModel
{
    public int NotebookId { get; set; }
    public string Title { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public int MarkdownBlockCount { get; set; }
    public int InteractiveTerminalCount { get; set; }
    public int StaticExampleCount { get; set; }
}

public class AddNotebookToCourseModel
{
    public int CourseId { get; set; }
    public int? SelectedNotebookId { get; set; }
    public string? NewNotebookTitle { get; set; }
}
