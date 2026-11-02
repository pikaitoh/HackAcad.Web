using System.Net;
using System.Text;
using System.Text.Json;
using HackAcad.Web.Models.ViewModels;

namespace HackAcad.Web.Services;

public class NotebookExportService
{
    private readonly IWebHostEnvironment _env;

    public NotebookExportService(IWebHostEnvironment env)
    {
        _env = env;
    }

    public string GenerateStandaloneHtml(string notebookTitle, string? contentJson)
    {
        var title = string.IsNullOrWhiteSpace(notebookTitle) ? "Terminal Notebook" : notebookTitle.Trim();
        var safeTitle = WebUtility.HtmlEncode(title);

        NotebookDocument doc;
        try
        {
            doc = string.IsNullOrWhiteSpace(contentJson)
                ? new NotebookDocument()
                : JsonSerializer.Deserialize<NotebookDocument>(contentJson) ?? new NotebookDocument();
        }
        catch
        {
            doc = new NotebookDocument();
        }

        // Read client-side terminal simulation engine from wwwroot/js/simulation-terminal.js
        string terminalJs = "";
        try
        {
            var terminalJsPath = Path.Combine(_env.WebRootPath, "js", "simulation-terminal.js");
            if (File.Exists(terminalJsPath))
            {
                terminalJs = File.ReadAllText(terminalJsPath, Encoding.UTF8);
            }
        }
        catch
        {
            // Fallback handled gracefully
        }

        var sb = new StringBuilder();
        sb.AppendLine("<!DOCTYPE html>");
        sb.AppendLine("<html lang=\"en\" data-theme=\"dark\">");
        sb.AppendLine("<head>");
        sb.AppendLine("  <meta charset=\"utf-8\" />");
        sb.AppendLine("  <meta name=\"viewport\" content=\"width=device-width, initial-scale=1.0\" />");
        sb.AppendLine($"  <title>{safeTitle} - HackAcad Terminal Notebook</title>");
        sb.AppendLine("  <style>");
        sb.AppendLine(GetEmbeddedCss());
        sb.AppendLine("  </style>");
        sb.AppendLine("</head>");
        sb.AppendLine("<body>");

        // Top Navigation Header
        sb.AppendLine("  <header class=\"notebook-export-header\">");
        sb.AppendLine("    <div class=\"header-container\">");
        sb.AppendLine("      <div class=\"brand-area\">");
        sb.AppendLine("        <span class=\"brand-badge\">HackAcad</span>");
        sb.AppendLine($"       <h1 class=\"notebook-title\">{safeTitle}</h1>");
        sb.AppendLine("      </div>");
        sb.AppendLine("      <div class=\"header-actions\">");
        sb.AppendLine("        <span class=\"offline-pill\">Offline Standalone Document</span>");
        sb.AppendLine("        <button type=\"button\" class=\"theme-toggle-btn\" onclick=\"toggleTheme()\" id=\"themeBtn\"><svg viewBox=\"0 0 24 24\" width=\"14\" height=\"14\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"2\" style=\"vertical-align:-2px;margin-right:4px;\"><circle cx=\"12\" cy=\"12\" r=\"5\"></circle><line x1=\"12\" y1=\"1\" x2=\"12\" y2=\"3\"></line><line x1=\"12\" y1=\"21\" x2=\"12\" y2=\"23\"></line><line x1=\"4.22\" y1=\"4.22\" x2=\"5.64\" y2=\"5.64\"></line><line x1=\"18.36\" y1=\"18.36\" x2=\"19.78\" y2=\"19.78\"></line><line x1=\"1\" y1=\"12\" x2=\"3\" y2=\"12\"></line><line x1=\"21\" y1=\"12\" x2=\"23\" y2=\"12\"></line><line x1=\"4.22\" y1=\"19.78\" x2=\"5.64\" y2=\"18.36\"></line><line x1=\"18.36\" y1=\"5.64\" x2=\"19.78\" y2=\"4.22\"></line></svg>Light Mode</button>");
        sb.AppendLine("      </div>");
        sb.AppendLine("    </div>");
        sb.AppendLine("  </header>");

        sb.AppendLine("  <main class=\"notebook-body-container\">");

        if (doc.Blocks.Count == 0)
        {
            sb.AppendLine("    <div class=\"empty-block\">This notebook does not contain any notes or terminal demonstrations yet.</div>");
        }

        int blockIndex = 0;
        foreach (var block in doc.Blocks)
        {
            blockIndex++;
            var blockId = $"block_{blockIndex}";

            if (block.Type == "markdown")
            {
                var renderedHtml = MarkdownService.RenderHtml(block.Content);
                sb.AppendLine($"    <section class=\"notebook-card markdown-card\" id=\"{blockId}\">");
                sb.AppendLine("      <div class=\"markdown-content\">");
                sb.AppendLine(renderedHtml);
                sb.AppendLine("      </div>");
                sb.AppendLine("    </section>");
            }
            else if (block.Type == "terminal_static")
            {
                var blockTitle = string.IsNullOrWhiteSpace(block.Title) ? "Command & Output Example" : block.Title.Trim();
                var safeBlockTitle = WebUtility.HtmlEncode(blockTitle);
                var promptCwd = string.IsNullOrWhiteSpace(block.Cwd) ? "/home/student" : block.Cwd.Trim();
                var safeCwd = WebUtility.HtmlEncode(promptCwd);

                sb.AppendLine($"    <section class=\"notebook-card static-term-card\" id=\"{blockId}\">");
                sb.AppendLine($"      <div class=\"block-meta-header\"><strong>{safeBlockTitle}</strong> <span class=\"meta-tag\">Static Demonstration</span></div>");
                sb.AppendLine("      <div class=\"terminal-wrapper terminal-wrapper-static\">");
                sb.AppendLine("        <div class=\"terminal-header\">");
                sb.AppendLine("          <div class=\"terminal-buttons\">");
                sb.AppendLine("            <span class=\"t-btn t-btn-red\"></span>");
                sb.AppendLine("            <span class=\"t-btn t-btn-yellow\"></span>");
                sb.AppendLine("            <span class=\"t-btn t-btn-green\"></span>");
                sb.AppendLine("          </div>");
                sb.AppendLine($"         <div class=\"terminal-title\">student@hackacad: ~ (static reference)</div>");
                sb.AppendLine("          <div class=\"terminal-badge terminal-badge-static\">STATIC DEMO</div>");
                sb.AppendLine("        </div>");
                sb.AppendLine("        <div class=\"terminal-body terminal-body-static\">");

                if (block.Examples != null && block.Examples.Count > 0)
                {
                    foreach (var ex in block.Examples)
                    {
                        var safeCmd = WebUtility.HtmlEncode(ex.Command ?? "");
                        sb.AppendLine($"          <div class=\"term-line term-line-cmd\"><span class=\"term-prompt\">student@hackacad:{safeCwd}$ </span><span class=\"term-cmd\">{safeCmd}</span></div>");
                        if (!string.IsNullOrEmpty(ex.Output))
                        {
                            var outputLines = ex.Output.Replace("\r\n", "\n").Split('\n');
                            foreach (var line in outputLines)
                            {
                                sb.AppendLine($"          <div class=\"term-line term-line-out\">{(string.IsNullOrEmpty(line) ? "&nbsp;" : WebUtility.HtmlEncode(line))}</div>");
                            }
                        }
                    }
                }
                else
                {
                    sb.AppendLine($"          <div class=\"term-line text-muted fst-italic\"># No commands defined in this demonstration block.</div>");
                }

                sb.AppendLine("        </div>");
                sb.AppendLine("        <div class=\"terminal-static-footer\">");
                sb.AppendLine("          <span style=\"display:inline-flex;align-items:center;gap:4px;\"><svg viewBox=\"0 0 24 24\" width=\"13\" height=\"13\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"2\" aria-hidden=\"true\"><rect x=\"3\" y=\"11\" width=\"18\" height=\"11\" rx=\"2\" ry=\"2\"></rect><path d=\"M7 11V7a5 5 0 0 1 10 0v4\"></path></svg> Static Command Demonstration • Read-Only Reference</span>");
                sb.AppendLine("        </div>");
                sb.AppendLine("      </div>");
                sb.AppendLine("    </section>");
            }
            else if (block.Type == "terminal_interactive")
            {
                var blockTitle = string.IsNullOrWhiteSpace(block.Title) ? "Interactive Simulated Terminal" : block.Title.Trim();
                var safeBlockTitle = WebUtility.HtmlEncode(blockTitle);
                var mountId = $"term_mount_{blockIndex}";
                var cfgId = $"term_cfg_{blockIndex}";

                // Build isolated config object
                var configObj = new Dictionary<string, object?>
                {
                    ["allowedCommands"] = block.AllowedCommands ?? ["help", "clear", "pwd", "ls", "cd", "cat", "whoami", "chmod", "echo", "history", "date", "id", "touch", "nano"],
                    ["initialDirectory"] = string.IsNullOrWhiteSpace(block.InitialDirectory) ? "/home/student" : block.InitialDirectory,
                    ["fileSystem"] = block.FileSystem ?? new Dictionary<string, object>
                    {
                        ["home"] = new Dictionary<string, object>
                        {
                            ["student"] = new Dictionary<string, object>
                            {
                                ["notes.txt"] = "Welcome to your personal terminal notebook sandbox.\nUse standard Linux commands to inspect files and test scenarios."
                            }
                        }
                    },
                    ["customCommands"] = block.CustomCommands ?? new List<NotebookCustomCommand>(),
                    ["isNotebook"] = true,
                    ["objectives"] = Array.Empty<object>()
                };

                var configJson = JsonSerializer.Serialize(configObj);
                // Safe JSON script embedding: replace closing tags
                var safeConfigScriptContent = configJson.Replace("</", "<\\/");

                sb.AppendLine($"    <section class=\"notebook-card interactive-term-card\" id=\"{blockId}\">");
                sb.AppendLine("      <div class=\"block-meta-header d-flex-between\">");
                sb.AppendLine($"        <div><strong>{safeBlockTitle}</strong> <span class=\"meta-tag tag-interactive\">Interactive Sandbox</span></div>");
                sb.AppendLine($"        <button type=\"button\" class=\"reset-btn\" onclick=\"resetTerminal('{mountId}', '{cfgId}')\"><svg viewBox=\"0 0 24 24\" width=\"13\" height=\"13\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"2\" style=\"vertical-align:-2px;margin-right:4px;\"><polyline points=\"1 4 1 10 7 10\"></polyline><path d=\"M3.51 15a9 9 0 1 0 2.13-9.36L1 10\"></path></svg>Reset Terminal</button>");
                sb.AppendLine("      </div>");
                sb.AppendLine($"      <div id=\"{mountId}\" class=\"interactive-term-mount\" data-cfg-id=\"{cfgId}\"></div>");
                sb.AppendLine($"      <script type=\"application/json\" id=\"{cfgId}\">{safeConfigScriptContent}</script>");
                sb.AppendLine("    </section>");
            }
        }

        sb.AppendLine("  </main>");

        // Embedded Scripts
        sb.AppendLine("  <script>");
        // 1. Simulation Terminal Engine
        sb.AppendLine(terminalJs);
        // 2. Notebook Runner & Controller
        sb.AppendLine(@"
        function toggleTheme() {
            var html = document.documentElement;
            var current = html.getAttribute('data-theme') || 'dark';
            var next = current === 'dark' ? 'light' : 'dark';
            html.setAttribute('data-theme', next);
            var btn = document.getElementById('themeBtn');
            if (btn) {
                btn.innerHTML = next === 'dark' 
                    ? '<svg viewBox=""0 0 24 24"" width=""14"" height=""14"" fill=""none"" stroke=""currentColor"" stroke-width=""2"" style=""vertical-align:-2px;margin-right:4px;""><circle cx=""12"" cy=""12"" r=""5""></circle><line x1=""12"" y1=""1"" x2=""12"" y2=""3""></line><line x1=""12"" y1=""21"" x2=""12"" y2=""23""></line><line x1=""4.22"" y1=""4.22"" x2=""5.64"" y2=""5.64""></line><line x1=""18.36"" y1=""18.36"" x2=""19.78"" y2=""19.78""></line><line x1=""1"" y1=""12"" x2=""3"" y2=""12""></line><line x1=""21"" y1=""12"" x2=""23"" y2=""12""></line><line x1=""4.22"" y1=""19.78"" x2=""5.64"" y2=""18.36""></line><line x1=""18.36"" y1=""5.64"" x2=""19.78"" y2=""4.22""></line></svg>Light Mode' 
                    : '<svg viewBox=""0 0 24 24"" width=""14"" height=""14"" fill=""none"" stroke=""currentColor"" stroke-width=""2"" style=""vertical-align:-2px;margin-right:4px;""><path d=""M21 12.79A9 9 0 1 1 11.21 3 7 7 0 0 0 21 12.79z""></path></svg>Dark Mode';
            }
        }

        function resetTerminal(containerId, cfgId) {
            var cfgEl = document.getElementById(cfgId);
            if (!cfgEl) return;
            try {
                var cfg = JSON.parse(cfgEl.textContent);
                cfg.isNotebook = true;
                if (typeof window.initSimulationTerminal === 'function') {
                    window.initSimulationTerminal(containerId, cfg);
                }
            } catch(e) {
                console.error('Reset error:', e);
            }
        }

        document.addEventListener('DOMContentLoaded', function () {
            var mounts = document.querySelectorAll('.interactive-term-mount');
            mounts.forEach(function (mount) {
                var containerId = mount.id;
                var cfgId = mount.getAttribute('data-cfg-id');
                var cfgEl = document.getElementById(cfgId);
                if (cfgEl && typeof window.initSimulationTerminal === 'function') {
                    try {
                        var cfg = JSON.parse(cfgEl.textContent);
                        cfg.isNotebook = true;
                        window.initSimulationTerminal(containerId, cfg);
                    } catch(err) {
                        console.error('Terminal init error:', err);
                    }
                }
            });
        });
        ");
        sb.AppendLine("  </script>");

        sb.AppendLine("</body>");
        sb.AppendLine("</html>");

        return sb.ToString();
    }

    private static string GetEmbeddedCss()
    {
        return @"
        :root {
            --bg-canvas: #f8fafc;
            --bg-surface: #ffffff;
            --text-main: #0f172a;
            --text-muted: #64748b;
            --border-line: #e2e8f0;
            --primary: #0284c7;
            --primary-hover: #0369a1;
            --code-bg: #f1f5f9;
        }

        html[data-theme='dark'] {
            --bg-canvas: #0b1320;
            --bg-surface: #131e32;
            --text-main: #f1f5f9;
            --text-muted: #94a3b8;
            --border-line: #243553;
            --primary: #38bdf8;
            --primary-hover: #7dd3fc;
            --code-bg: #1e293b;
        }

        * { box-sizing: border-box; }
        body {
            margin: 0;
            padding: 0;
            font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif;
            background-color: var(--bg-canvas);
            color: var(--text-main);
            line-height: 1.6;
            transition: background-color 0.2s, color 0.2s;
        }

        .notebook-export-header {
            background-color: var(--bg-surface);
            border-bottom: 1px solid var(--border-line);
            padding: 1.25rem 2rem;
            position: sticky;
            top: 0;
            z-index: 100;
        }

        .header-container {
            max-width: 1040px;
            margin: 0 auto;
            display: flex;
            align-items: center;
            justify-content: space-between;
            flex-wrap: gap;
            gap: 1rem;
        }

        .brand-area { display: flex; align-items: center; gap: 0.75rem; }
        .brand-badge {
            background: #193e46;
            color: #f2cf45;
            font-weight: 800;
            font-size: 0.8rem;
            padding: 0.25rem 0.6rem;
            border-radius: 6px;
            letter-spacing: 0.05em;
        }

        .notebook-title {
            margin: 0;
            font-size: 1.35rem;
            font-weight: 800;
            color: var(--text-main);
        }

        .header-actions { display: flex; align-items: center; gap: 0.75rem; }
        .offline-pill {
            font-size: 0.75rem;
            background: rgba(56, 189, 248, 0.15);
            color: var(--primary);
            padding: 0.3rem 0.7rem;
            border-radius: 999px;
            font-weight: 600;
        }

        .theme-toggle-btn, .reset-btn {
            background: var(--bg-canvas);
            border: 1px solid var(--border-line);
            color: var(--text-main);
            padding: 0.4rem 0.85rem;
            border-radius: 8px;
            font-size: 0.825rem;
            font-weight: 600;
            cursor: pointer;
            transition: all 0.15s ease;
        }

        .theme-toggle-btn:hover, .reset-btn:hover {
            border-color: var(--primary);
            color: var(--primary);
        }

        .notebook-body-container {
            max-width: 1040px;
            margin: 2rem auto 4rem;
            padding: 0 1.5rem;
            display: flex;
            flex-direction: column;
            gap: 1.5rem;
        }

        .notebook-card {
            background-color: var(--bg-surface);
            border: 1px solid var(--border-line);
            border-radius: 12px;
            padding: 1.75rem;
            box-shadow: 0 4px 12px rgba(0, 0, 0, 0.05);
        }

        .block-meta-header {
            margin-bottom: 1rem;
            font-size: 0.95rem;
        }

        .d-flex-between {
            display: flex;
            align-items: center;
            justify-content: space-between;
            gap: 1rem;
        }

        .meta-tag {
            font-size: 0.75rem;
            padding: 0.15rem 0.5rem;
            border-radius: 4px;
            background: #334155;
            color: #cbd5e1;
            font-weight: 600;
            margin-left: 0.5rem;
        }

        .tag-interactive {
            background: #0369a1;
            color: #e0f2fe;
        }

        /* Markdown Formatting */
        .markdown-content h1 { font-size: 1.75rem; margin-top: 0; margin-bottom: 1rem; color: var(--text-main); }
        .markdown-content h2 { font-size: 1.4rem; margin-top: 1.5rem; margin-bottom: 0.75rem; color: var(--text-main); border-bottom: 1px solid var(--border-line); padding-bottom: 0.3rem; }
        .markdown-content h3 { font-size: 1.15rem; margin-top: 1.25rem; margin-bottom: 0.5rem; color: var(--text-main); }
        .markdown-content h4, .markdown-content h5, .markdown-content h6 { font-size: 1rem; margin-top: 1rem; margin-bottom: 0.5rem; color: var(--text-main); }
        .markdown-content p { margin-top: 0; margin-bottom: 1rem; }
        .markdown-content ul, .markdown-content ol { margin-top: 0; margin-bottom: 1rem; padding-left: 1.5rem; }
        .markdown-content li { margin-bottom: 0.25rem; }
        .markdown-content a { color: var(--primary); text-decoration: none; }
        .markdown-content a:hover { text-decoration: underline; }
        .markdown-content blockquote {
            margin: 1rem 0;
            padding: 0.5rem 1rem;
            border-left: 4px solid var(--primary);
            background: var(--code-bg);
            border-radius: 0 8px 8px 0;
            color: var(--text-muted);
        }
        .markdown-content code {
            font-family: 'Courier New', Courier, monospace;
            background: var(--code-bg);
            color: #e11d48;
            padding: 0.2rem 0.4rem;
            border-radius: 4px;
            font-size: 0.875em;
        }
        html[data-theme='dark'] .markdown-content code {
            color: #fb7185;
        }
        .markdown-content pre {
            background: #0f172a;
            color: #f8fafc;
            padding: 1rem 1.25rem;
            border-radius: 8px;
            overflow-x: auto;
            border: 1px solid #334155;
            margin: 1rem 0;
        }
        .markdown-content pre code {
            background: transparent;
            color: #f8fafc;
            padding: 0;
            font-size: 0.9em;
        }

        /* Terminal Component Styling (from learning.css) */
        .terminal-wrapper {
            background: #0f172a;
            border-radius: 12px;
            overflow: hidden;
            box-shadow: 0 12px 30px rgba(0,0,0,0.3);
            font-family: 'Courier New', Courier, monospace;
            margin-block: 0.75rem;
            border: 1px solid #334155;
        }
        .terminal-header {
            background: #1e293b;
            padding: 0.65rem 1rem;
            display: flex;
            align-items: center;
            justify-content: space-between;
            border-bottom: 1px solid #334155;
        }
        .terminal-buttons { display: flex; gap: 6px; }
        .t-btn { width: 12px; height: 12px; border-radius: 50%; display: inline-block; }
        .t-btn-red { background: #ef4444; }
        .t-btn-yellow { background: #eab308; }
        .t-btn-green { background: #22c55e; }
        .terminal-title { color: #94a3b8; font-size: 0.8rem; font-weight: 600; letter-spacing: 0.05em; }
        .terminal-badge {
            background: #0284c7;
            color: white;
            font-size: 0.65rem;
            font-weight: 700;
            padding: 0.15rem 0.5rem;
            border-radius: 4px;
            text-transform: uppercase;
        }
        .terminal-body {
            padding: 1.25rem;
            color: #f8fafc;
            height: 380px;
            overflow-y: auto;
            font-size: 0.875rem;
            line-height: 1.5;
        }
        .static-body { height: auto; min-height: 80px; max-height: 480px; }
        .terminal-wrapper.terminal-wrapper-static {
            margin-block: 0.5rem;
            box-shadow: 0 4px 18px rgba(0, 0, 0, 0.25);
        }
        .terminal-badge-static {
            background: #334155;
            color: #cbd5e1;
            border: 1px solid #475569;
        }
        .terminal-body-static {
            padding: 1rem 1.25rem;
            min-height: 70px;
            max-height: 380px;
            overflow-y: auto;
            line-height: 1.6;
        }
        .terminal-static-footer {
            display: flex;
            align-items: center;
            justify-content: space-between;
            padding: 0.45rem 1.25rem;
            background: #1e293b;
            border-top: 1px solid #334155;
            color: #94a3b8;
            font-size: 0.75rem;
            font-weight: 500;
        }
        .term-line-cmd { margin-top: 0.35rem; color: #f8fafc; }
        .term-line-cmd:first-child { margin-top: 0; }
        .term-line-out { color: #cbd5e1; white-space: pre-wrap; word-break: break-all; padding-left: 0.25rem; }
        .term-line { white-space: pre-wrap; word-break: break-all; margin-bottom: 0.2rem; }
        .term-banner pre { color: #38bdf8; font-size: 0.7rem; line-height: 1.1; margin: 0; font-family: monospace; }
        .term-highlight { color: #fef08a; font-weight: bold; }
        .term-prompt { color: #4ade80; font-weight: 600; }
        .term-cmd { color: #ffffff; font-weight: bold; }
        .term-dir { color: #60a5fa; font-weight: bold; }
        .term-exec { color: #4ade80; font-weight: bold; }
        .term-error { color: #f87171; }
        .term-match { background: #854d0e; color: #fef08a; padding: 0 2px; }
        .terminal-input-row {
            display: flex;
            align-items: center;
            background: #0f172a;
            padding: 0.5rem 1.25rem 0.85rem;
            border-top: 1px solid #1e293b;
        }
        .terminal-input {
            background: transparent;
            border: none;
            outline: none;
            color: #ffffff;
            font-family: inherit;
            font-size: 0.875rem;
            flex-grow: 1;
            padding: 0;
            margin-left: 0.5rem;
            caret-color: #38bdf8;
        }

        /* Simulated Nano UI */
        .nano-wrapper {
            background: #0f172a;
            border: 1px solid #334155;
            border-radius: 8px;
            display: flex;
            flex-direction: column;
            overflow: hidden;
            height: 380px;
        }
        .nano-titlebar {
            background: #ffffff;
            color: #0f172a;
            padding: 4px 12px;
            font-family: monospace;
            font-size: 0.825rem;
            display: flex;
            justify-content: space-between;
        }
        .nano-editor-area {
            flex-grow: 1;
            background: #0f172a;
            color: #f8fafc;
            border: none;
            outline: none;
            padding: 10px;
            font-family: monospace;
            font-size: 0.875rem;
            resize: none;
        }
        .nano-statusbar {
            background: #1e293b;
            color: #e2e8f0;
            padding: 6px 12px;
            font-family: monospace;
            font-size: 0.8rem;
            display: flex;
            justify-content: space-between;
            align-items: center;
        }
        .nano-actions button {
            margin-left: 6px;
            padding: 2px 8px;
            font-size: 0.75rem;
            border-radius: 4px;
            cursor: pointer;
        }

        .empty-block {
            text-align: center;
            padding: 3rem 1rem;
            color: var(--text-muted);
            border: 1px dashed var(--border-line);
            border-radius: 12px;
        }
        ";
    }
}
