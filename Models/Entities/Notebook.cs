using System.ComponentModel.DataAnnotations;

namespace HackAcad.Web.Models.Entities;

public class Notebook
{
    [Key]
    public int NotebookId { get; set; }

    [Required]
    public string UserId { get; set; } = string.Empty;

    [Required, StringLength(150)]
    public string Title { get; set; } = "Untitled Notebook";

    // JSON structure storing the notebook blocks (Markdown, Interactive Terminal, Static Example)
    public string ContentJson { get; set; } = "{\"version\":1,\"blocks\":[]}";

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
