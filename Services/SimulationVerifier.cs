using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace HackAcad.Web.Services;

// Replay virtual commands only. This never accesses the real filesystem, shell or network.
public static class SimulationVerifier
{
    private static readonly HashSet<string> Supported = ["help", "clear", "pwd", "ls", "cd", "cat", "grep", "chmod", "whoami", "id", "echo", "base64", "ping", "nmap", "curl", "history", "date", "touch", "nano"];
    private static string Text(JsonElement item, string name, string fallback = "") =>
        item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString()! : fallback;

    public static bool TryConfiguration(string? json, out JsonElement root, out string error)
    {
        root = default;
        error = "Enter a JSON object with a filesystem, a valid starting directory and at least one supported objective (up to 64 KB).";
        if (string.IsNullOrWhiteSpace(json) || Encoding.UTF8.GetByteCount(json) > 65536) return false;
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 24 });
            var value = document.RootElement;
            if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty("fileSystem", out var fs)
                || fs.ValueKind != JsonValueKind.Object || !ValidTree(fs)
                || !value.TryGetProperty("objectives", out var goals) || goals.ValueKind != JsonValueKind.Array
                || goals.GetArrayLength() is 0 or > 64) return false;
            if (value.TryGetProperty("allowedCommands", out var commands)
                && (commands.ValueKind != JsonValueKind.Array || commands.EnumerateArray().Any(c =>
                    c.ValueKind != JsonValueKind.String || !Supported.Contains(c.GetString()!.Trim().ToLowerInvariant())))) return false;

            // Validate custom commands if present
            var customCommandNames = new HashSet<string>(StringComparer.Ordinal);
            if (value.TryGetProperty("customCommands", out var customCmds))
            {
                if (customCmds.ValueKind != JsonValueKind.Array || customCmds.GetArrayLength() > 64) return false;
                foreach (var cc in customCmds.EnumerateArray())
                {
                    if (cc.ValueKind != JsonValueKind.Object) return false;
                    var ccCmd = Text(cc, "command").Trim();
                    if (string.IsNullOrWhiteSpace(ccCmd) || ccCmd.Length > 512) return false;
                    if (ccCmd.Any(c => c is '>' or '<' or '|' or ';' or '&')) return false; // Reserved shell syntax
                    var enabled = !cc.TryGetProperty("enabled", out var enProp) || enProp.ValueKind != JsonValueKind.False;
                    if (enabled)
                    {
                        if (!customCommandNames.Add(ccCmd)) return false; // Duplicate custom command
                        if (Supported.Contains(ccCmd.ToLowerInvariant())) return false; // Conflicts with built-in
                    }
                }
            }

            var initial = Normalize(Text(value, "initialDirectory", "/home/student"), "/");
            if (Node(fs, initial)?.ValueKind != JsonValueKind.Object) return false;
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var goal in goals.EnumerateArray())
            {
                if (goal.ValueKind != JsonValueKind.Object || string.IsNullOrWhiteSpace(Text(goal, "id"))
                    || !ids.Add(Text(goal, "id")) || string.IsNullOrWhiteSpace(Text(goal, "target"))) return false;
                var type = Text(goal, "type");
                if (type is not ("cwd" or "cat" or "chmod" or "command")) return false;
                if (type == "command")
                {
                    var target = Text(goal, "target");
                    if (!Supported.Contains(target) && !customCommandNames.Contains(target)) return false;
                }
                if (type == "chmod" && !Regex.IsMatch(Text(goal, "mode", "755"), "^[0-7]{3,4}$")) return false;
            }
            root = value.Clone();
            error = "";
            return true;
        }
        catch (JsonException) { return false; }
    }

    private static bool ValidTree(JsonElement node) => node.EnumerateObject().All(p =>
        p.Name.Length is > 0 and <= 255 && p.Name is not ("." or ".." or "__proto__" or "constructor" or "prototype")
        && !p.Name.Any(c => char.IsControl(c) || c is '/' or '\\')
        && (p.Value.ValueKind == JsonValueKind.String || p.Value.ValueKind == JsonValueKind.Object && ValidTree(p.Value)));

    public static string SafeJson(string? json) => TryConfiguration(json, out var root, out _) ? JsonSerializer.Serialize(root) : "null";

    private static string Normalize(string path, string cwd)
    {
        if (path == "~") path = "/home/student";
        var parts = new List<string>();
        foreach (var part in (path.StartsWith('/') ? path : cwd + "/" + path).Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (part == ".") continue;
            if (part == "..") { if (parts.Count > 0) parts.RemoveAt(parts.Count - 1); }
            else parts.Add(part);
        }
        return "/" + string.Join('/', parts);
    }

    private static JsonElement? Node(JsonElement fs, string path)
    {
        var node = fs;
        foreach (var part in path.Split('/', StringSplitOptions.RemoveEmptyEntries))
            if (node.ValueKind != JsonValueKind.Object || !node.TryGetProperty(part, out node)) return null;
        return node;
    }

    public static bool IsComplete(string? json, IReadOnlyList<string>? commands)
    {
        if (commands is null || commands.Count is 0 or > 256 || !TryConfiguration(json, out var config, out _)) return false;
        var fs = config.GetProperty("fileSystem");
        var goals = config.GetProperty("objectives").EnumerateArray().ToArray();
        var allowed = config.TryGetProperty("allowedCommands", out var permitted)
            ? permitted.EnumerateArray().Select(c => c.GetString()!.Trim().ToLowerInvariant()).ToHashSet() : Supported;

        // Collect enabled custom commands
        var customCommands = new Dictionary<string, string>(StringComparer.Ordinal);
        if (config.TryGetProperty("customCommands", out var customArray) && customArray.ValueKind == JsonValueKind.Array)
        {
            foreach (var cc in customArray.EnumerateArray())
            {
                if (cc.ValueKind == JsonValueKind.Object)
                {
                    var enabled = !cc.TryGetProperty("enabled", out var enProp) || enProp.ValueKind != JsonValueKind.False;
                    if (enabled)
                    {
                        var cStr = Text(cc, "command").Trim();
                        if (!string.IsNullOrEmpty(cStr))
                            customCommands[cStr] = Text(cc, "output");
                    }
                }
            }
        }

        var cwd = Normalize(Text(config, "initialDirectory", "/home/student"), "/");
        var completed = new HashSet<string>(StringComparer.Ordinal);
        var dynamicFiles = new Dictionary<string, string>(StringComparer.Ordinal);

        bool DirExists(string path) => Node(fs, path)?.ValueKind == JsonValueKind.Object;
        bool FileExists(string path) => dynamicFiles.ContainsKey(path) || Node(fs, path)?.ValueKind == JsonValueKind.String;

        foreach (var rawInput in commands)
        {
            if (rawInput is null || rawInput.Length > 1024) return false;
            var input = rawInput.Trim();
            if (string.IsNullOrEmpty(input)) continue;

            // Handle output redirection '>'
            string commandToRun = input;
            string? redirectDest = null;
            if (input.Contains('>'))
            {
                var firstIdx = input.IndexOf('>');
                var secondIdx = input.IndexOf('>', firstIdx + 1);
                if (secondIdx != -1 || firstIdx == 0) continue; // multiple or leading redirection invalid
                var leftPart = input.Substring(0, firstIdx).Trim();
                var rightPart = input.Substring(firstIdx + 1).Trim();
                if (string.IsNullOrEmpty(leftPart) || string.IsNullOrEmpty(rightPart)) continue;
                var rightParts = rightPart.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                if (rightParts.Length != 1) continue; // ambiguous destination invalid
                commandToRun = leftPart;
                redirectDest = rightParts[0];
            }

            // 1. Check exact-match custom commands (case-sensitive)
            if (customCommands.TryGetValue(commandToRun, out var customOutput))
            {
                if (!string.IsNullOrEmpty(redirectDest))
                {
                    var destPath = Normalize(redirectDest, cwd);
                    var pPath = destPath.Contains('/') ? destPath.Substring(0, destPath.LastIndexOf('/')) : "";
                    if (string.IsNullOrEmpty(pPath)) pPath = "/";
                    if (!DirExists(pPath) || DirExists(destPath)) continue; // destination invalid
                    dynamicFiles[destPath] = customOutput;
                }
                foreach (var goal in goals)
                {
                    var type = Text(goal, "type");
                    var target = Text(goal, "target");
                    if (type == "command" && commandToRun == target)
                        completed.Add(Text(goal, "id"));
                }
                continue;
            }

            // 2. Built-in command processing
            var parts = Regex.Split(commandToRun, @"\s+");
            var cmd = parts[0].ToLowerInvariant();
            var args = parts.Skip(1).ToArray();
            if (!Supported.Contains(cmd) || !allowed.Contains(cmd) && cmd is not ("help" or "clear")) continue;
            string? operatedPath = null;
            var success = true;
            string generatedOutput = "";
            switch (cmd)
            {
                case "cd":
                    var destination = Normalize(args.FirstOrDefault() ?? "/home/student", cwd);
                    success = DirExists(destination);
                    if (success) cwd = destination;
                    break;
                case "cat": case "chmod": case "grep":
                    var index = cmd == "cat" ? 0 : 1;
                    if (args.Length <= index) { success = false; break; }
                    operatedPath = Normalize(args[index], cwd);
                    success = FileExists(operatedPath);
                    if (cmd == "chmod") success &= Regex.IsMatch(args[0], "^[0-7]{3,4}$");
                    if (success && cmd == "cat")
                    {
                        generatedOutput = dynamicFiles.TryGetValue(operatedPath, out var fContent)
                            ? fContent : (Node(fs, operatedPath)?.GetString() ?? "");
                    }
                    break;
                case "touch": case "nano":
                    if (args.Length == 0) { success = false; break; }
                    operatedPath = Normalize(args[0], cwd);
                    var parentPath = operatedPath.Contains('/') ? operatedPath.Substring(0, operatedPath.LastIndexOf('/')) : "";
                    if (string.IsNullOrEmpty(parentPath)) parentPath = "/";
                    success = DirExists(parentPath) && !DirExists(operatedPath);
                    if (success && !dynamicFiles.ContainsKey(operatedPath))
                    {
                        dynamicFiles[operatedPath] = "";
                    }
                    break;
                case "echo":
                    generatedOutput = string.Join(' ', args);
                    break;
                case "pwd":
                    generatedOutput = cwd;
                    break;
                case "nmap":
                    generatedOutput = "nmap done";
                    break;
                case "curl":
                    generatedOutput = "curl output";
                    break;
                case "base64":
                    if (args.Contains("-d"))
                    {
                        var index64 = Array.IndexOf(args, "-d") + 1;
                        if (index64 >= args.Length) { success = false; break; }
                        try {
                            var b64Bytes = Convert.FromBase64String(args[index64]);
                            generatedOutput = Encoding.UTF8.GetString(b64Bytes);
                        }
                        catch (FormatException) { success = false; }
                    }
                    else
                    {
                        var target = args.FirstOrDefault() ?? "";
                        generatedOutput = Convert.ToBase64String(Encoding.UTF8.GetBytes(target));
                    }
                    break;
            }
            if (!success) continue;

            if (!string.IsNullOrEmpty(redirectDest))
            {
                var destPath = Normalize(redirectDest, cwd);
                var pPath = destPath.Contains('/') ? destPath.Substring(0, destPath.LastIndexOf('/')) : "";
                if (string.IsNullOrEmpty(pPath)) pPath = "/";
                if (!DirExists(pPath) || DirExists(destPath)) continue;
                dynamicFiles[destPath] = generatedOutput;
            }

            foreach (var goal in goals)
            {
                var type = Text(goal, "type");
                var target = Text(goal, "target");
                var matchPath = operatedPath is not null && (target.Contains('/')
                    ? operatedPath == Normalize(target, cwd) : operatedPath.Split('/').Last() == target);
                if (type == "cwd" && cwd == Normalize(target, "/")
                    || type == "cat" && cmd == "cat" && matchPath
                    || type == "chmod" && cmd == "chmod" && matchPath && args[0] == Text(goal, "mode", "755")
                    || type == "command" && (cmd == target || commandToRun == target))
                    completed.Add(Text(goal, "id"));
            }
        }
        return completed.Count == goals.Length;
    }
}
