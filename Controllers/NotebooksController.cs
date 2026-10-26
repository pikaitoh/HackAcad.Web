using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using HackAcad.Web.Data;
using HackAcad.Web.Models.Entities;
using HackAcad.Web.Models.ViewModels;
using HackAcad.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HackAcad.Web.Controllers;

[Authorize]
public class NotebooksController(
    ApplicationDbContext db,
    NotebookExportService exportService) : Controller
{
    // ==========================================
    // 1. NOTEBOOK LIST
    // ==========================================
    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;

        var notebooks = await db.Notebooks
            .AsNoTracking()
            .Where(n => n.UserId == currentUserId)
            .OrderByDescending(n => n.UpdatedAtUtc)
            .ToListAsync();

        var rows = new List<NotebookListItemViewModel>();
        foreach (var nb in notebooks)
        {
            int mdCount = 0;
            int termCount = 0;
            int staticCount = 0;

            try
            {
                if (!string.IsNullOrWhiteSpace(nb.ContentJson))
                {
                    var doc = JsonSerializer.Deserialize<NotebookDocument>(nb.ContentJson);
                    if (doc?.Blocks != null)
                    {
                        mdCount = doc.Blocks.Count(b => b.Type == "markdown");
                        termCount = doc.Blocks.Count(b => b.Type == "terminal_interactive");
                        staticCount = doc.Blocks.Count(b => b.Type == "terminal_static");
                    }
                }
            }
            catch
            {
                // Fallback gracefully
            }

            rows.Add(new NotebookListItemViewModel
            {
                NotebookId = nb.NotebookId,
                Title = nb.Title,
                CreatedAtUtc = nb.CreatedAtUtc,
                UpdatedAtUtc = nb.UpdatedAtUtc,
                MarkdownBlockCount = mdCount,
                InteractiveTerminalCount = termCount,
                StaticExampleCount = staticCount
            });
        }

        return View(rows);
    }

    // ==========================================
    // 2. CREATE NOTEBOOK
    // ==========================================
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(string? title)
    {
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;

        var initialTitle = string.IsNullOrWhiteSpace(title) ? "Untitled Notebook" : title.Trim();
        if (initialTitle.Length > 150) initialTitle = initialTitle[..150];

        var initialDoc = new NotebookDocument
        {
            Version = 1,
            Blocks =
            [
                new NotebookBlock
                {
                    Id = "blk_" + Guid.NewGuid().ToString("n")[..8],
                    Type = "markdown",
                    Content = $"# {initialTitle}\n\nStart drafting your cybersecurity notes and terminal demonstrations here.\n\nUse the buttons below to insert **Markdown Notes**, an **Interactive Simulated Terminal**, or a **Static Command Example**."
                }
            ]
        };

        var notebook = new Notebook
        {
            UserId = currentUserId,
            Title = initialTitle,
            ContentJson = JsonSerializer.Serialize(initialDoc),
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

        db.Notebooks.Add(notebook);
        await db.SaveChangesAsync();

        TempData["Success"] = $"Created notebook \"{notebook.Title}\".";
        return RedirectToAction(nameof(Edit), new { id = notebook.NotebookId });
    }

    // ==========================================
    // 3. VIEW NOTEBOOK (READ-ONLY READER VIEW)
    // ==========================================
    [HttpGet]
    public async Task<IActionResult> View(int id)
    {
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;

        var notebook = await db.Notebooks
            .AsNoTracking()
            .FirstOrDefaultAsync(n => n.NotebookId == id && n.UserId == currentUserId);

        if (notebook == null)
        {
            return NotFound("Notebook not found or you do not have permission to view it.");
        }

        NotebookDocument doc;
        try
        {
            doc = string.IsNullOrWhiteSpace(notebook.ContentJson)
                ? new NotebookDocument()
                : JsonSerializer.Deserialize<NotebookDocument>(notebook.ContentJson) ?? new NotebookDocument();
        }
        catch
        {
            doc = new NotebookDocument();
        }

        ViewBag.Document = doc;
        return View(notebook);
    }

    // ==========================================
    // 4. EDIT NOTEBOOK (FULL EDITOR)
    // ==========================================
    [HttpGet]
    public async Task<IActionResult> Edit(int id)
    {
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;

        var notebook = await db.Notebooks
            .FirstOrDefaultAsync(n => n.NotebookId == id && n.UserId == currentUserId);

        if (notebook == null)
        {
            return NotFound("Notebook not found or you do not have permission to edit it.");
        }

        return View(notebook);
    }

    [HttpGet]
    public IActionResult EditSimulation(int notebookId, string blockId)
    {
        return RedirectToAction("EditSimulation", "InstructorCourses", new { notebookId, blockId });
    }

    // ==========================================
    // 5. SAVE NOTEBOOK (AJAX SAVE)
    // ==========================================
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(int id, [FromBody] NotebookSaveModel model)
    {
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;

        var notebook = await db.Notebooks
            .FirstOrDefaultAsync(n => n.NotebookId == id && n.UserId == currentUserId);

        if (notebook == null)
        {
            return NotFound(new { success = false, message = "Notebook not found or unauthorized." });
        }

        if (string.IsNullOrWhiteSpace(model.Title))
        {
            return BadRequest(new { success = false, message = "Notebook title is required." });
        }

        // Validate JSON
        if (!string.IsNullOrWhiteSpace(model.ContentJson))
        {
            try
            {
                var doc = JsonSerializer.Deserialize<NotebookDocument>(model.ContentJson);
                if (doc == null)
                {
                    return BadRequest(new { success = false, message = "Invalid notebook content structure." });
                }
            }
            catch (Exception ex)
            {
                return BadRequest(new { success = false, message = "JSON structure error: " + ex.Message });
            }
        }

        notebook.Title = model.Title.Trim();
        if (notebook.Title.Length > 150) notebook.Title = notebook.Title[..150];

        notebook.ContentJson = model.ContentJson ?? "{\"version\":1,\"blocks\":[]}";
        notebook.UpdatedAtUtc = DateTime.UtcNow;

        await db.SaveChangesAsync();

        return Ok(new
        {
            success = true,
            title = notebook.Title,
            updatedAt = notebook.UpdatedAtUtc.ToString("o"),
            message = "Notebook saved successfully."
        });
    }

    // ==========================================
    // 6. RENAME NOTEBOOK
    // ==========================================
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Rename(int id, string title)
    {
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;

        var notebook = await db.Notebooks
            .FirstOrDefaultAsync(n => n.NotebookId == id && n.UserId == currentUserId);

        if (notebook == null)
        {
            return NotFound("Notebook not found or unauthorized.");
        }

        if (!string.IsNullOrWhiteSpace(title))
        {
            var cleanTitle = title.Trim();
            if (cleanTitle.Length > 150) cleanTitle = cleanTitle[..150];
            notebook.Title = cleanTitle;
            notebook.UpdatedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync();
            TempData["Success"] = $"Renamed notebook to \"{notebook.Title}\".";
        }

        return RedirectToAction(nameof(Index));
    }

    // ==========================================
    // 7. DELETE NOTEBOOK
    // ==========================================
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;

        var notebook = await db.Notebooks
            .FirstOrDefaultAsync(n => n.NotebookId == id && n.UserId == currentUserId);

        if (notebook == null)
        {
            return NotFound("Notebook not found or unauthorized.");
        }

        var title = notebook.Title;
        db.Notebooks.Remove(notebook);
        await db.SaveChangesAsync();

        TempData["Success"] = $"Notebook \"{title}\" deleted.";
        return RedirectToAction(nameof(Index));
    }

    // ==========================================
    // 8. EXPORT STANDALONE HTML
    // ==========================================
    [HttpGet]
    public async Task<IActionResult> Export(int id)
    {
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;

        var notebook = await db.Notebooks
            .AsNoTracking()
            .FirstOrDefaultAsync(n => n.NotebookId == id && n.UserId == currentUserId);

        if (notebook == null)
        {
            return NotFound("Notebook not found or unauthorized.");
        }

        var html = exportService.GenerateStandaloneHtml(notebook.Title, notebook.ContentJson);

        var safeFilename = Regex.Replace(notebook.Title, @"[^a-zA-Z0-9_\-]+", "_").Trim('_');
        if (string.IsNullOrWhiteSpace(safeFilename)) safeFilename = "terminal_notebook";

        return File(Encoding.UTF8.GetBytes(html), "text/html", $"{safeFilename}.html");
    }
}
