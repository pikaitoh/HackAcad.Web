using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace HackAcad.Web.Services;

// Replay virtual commands only. This never accesses the real filesystem, shell or network.
public static class SimulationVerifier
{
    private static readonly HashSet<string> Supported = ["help", "clear", "pwd", "ls", "cd", "cat", "grep", "chmod", "whoami", "id", "echo", "history", "date", "touch", "nano"];
    private static readonly HashSet<string> ObsoleteCommands = ["ping", "nmap", "curl", "base64"];

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

            if (value.TryGetProperty("allowedCommands", out var commands))
            {
                if (commands.ValueKind != JsonValueKind.Array) return false;
                foreach (var c in commands.EnumerateArray())
                {
                    if (c.ValueKind != JsonValueKind.String) return false;
                    var cName = c.GetString()!.Trim().ToLowerInvariant();
                    if (ObsoleteCommands.Contains(cName)) continue; // Obsolete commands are safely ignored
                    if (!Supported.Contains(cName)) return false;
                }
            }

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

                    // Validate optional requiredFiles if present
                    if (cc.TryGetProperty("requiredFiles", out var rfProp) && rfProp.ValueKind == JsonValueKind.Array)
                    {
                        if (rfProp.GetArrayLength() > 64) return false;
                        foreach (var rf in rfProp.EnumerateArray())
                        {
                            if (rf.ValueKind != JsonValueKind.String) return false;
                            var pathStr = rf.GetString()?.Trim() ?? "";
                            if (string.IsNullOrEmpty(pathStr) || pathStr.Length > 512) return false;
                            if (pathStr.StartsWith('/') || pathStr.Contains('\\')) return false; // Must be relative
                            var segments = pathStr.Split('/', StringSplitOptions.RemoveEmptyEntries);
                            if (segments.Any(s => s is ".." or "." || s.Any(c => char.IsControl(c) || c is '<' or '>' or '|' or '&' or ';')))
                                return false; // Traversal and illegal characters rejected
                        }
                    }
                }
            }

            var initial = Normalize(Text(value, "initialDirectory", "/home/student"), "/");
            if (Node(fs, initial)?.ValueKind != JsonValueKind.Object) return false;
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var goal in goals.EnumerateArray())
            {
                if (goal.ValueKind != JsonValueKind.Object || string.IsNullOrWhiteSpace(Text(goal, "id"))
                    || !ids.Add(Text(goal, "id"))) return false;

                // Extract and validate expected command
                var cmd = Text(goal, "command").Trim();
                if (string.IsNullOrWhiteSpace(cmd))
                {
                    // Backward-compatibility fallback to target
                    cmd = Text(goal, "target").Trim();
                    var legacyType = Text(goal, "type");
                    if (legacyType == "cwd") cmd = "cd " + cmd;
                    else if (legacyType == "cat") cmd = "cat " + cmd;
                    else if (legacyType == "chmod") cmd = $"chmod {Text(goal, "mode", "755")} " + cmd;
                }
                if (string.IsNullOrWhiteSpace(cmd)) return false;

                // Validate optional required execution directory
                var execDir = Text(goal, "executionDirectory").Trim();
                if (!string.IsNullOrEmpty(execDir))
                {
                    var normDir = Normalize(execDir, "/");
                    if (Node(fs, normDir)?.ValueKind != JsonValueKind.Object) return false;
                }
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
        var isSequential = config.TryGetProperty("sequentialObjectives", out var seqProp) && seqProp.ValueKind == JsonValueKind.True;
        var allowed = config.TryGetProperty("allowedCommands", out var permitted)
            ? permitted.EnumerateArray()
                .Where(c => c.ValueKind == JsonValueKind.String)
                .Select(c => c.GetString()!.Trim().ToLowerInvariant())
                .Where(c => Supported.Contains(c))
                .ToHashSet() : Supported;

        // Collect enabled custom commands
        var customCommands = new Dictionary<string, (string Output, List<string> RequiredFiles)>(StringComparer.Ordinal);
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
                        {
                            var reqList = new List<string>();
                            if (cc.TryGetProperty("requiredFiles", out var rfProp) && rfProp.ValueKind == JsonValueKind.Array)
                            {
                                foreach (var rf in rfProp.EnumerateArray())
                                {
                                    if (rf.ValueKind == JsonValueKind.String)
                                    {
                                        var s = rf.GetString()?.Trim();
                                        if (!string.IsNullOrEmpty(s)) reqList.Add(s);
                                    }
                                }
                            }
                            customCommands[cStr] = (Text(cc, "output"), reqList);
                        }
                    }
                }
            }
        }

        var cwd = Normalize(Text(config, "initialDirectory", "/home/student"), "/");
        var dynamicFiles = new Dictionary<string, string>(StringComparer.Ordinal);

        bool DirExists(string path) => Node(fs, path)?.ValueKind == JsonValueKind.Object;
        bool FileExists(string path) => dynamicFiles.ContainsKey(path) || Node(fs, path)?.ValueKind == JsonValueKind.String;

        // Parse goals into structured expected objectives
        var parsedGoals = new List<(string Id, string Command, string ExecDir)>();
        foreach (var goal in goals)
        {
            var id = Text(goal, "id");
            var cmd = Text(goal, "command").Trim();
            if (string.IsNullOrEmpty(cmd))
            {
                cmd = Text(goal, "target").Trim();
                var lType = Text(goal, "type");
                if (lType == "cwd") cmd = "cd " + cmd;
                else if (lType == "cat") cmd = "cat " + cmd;
                else if (lType == "chmod") cmd = $"chmod {Text(goal, "mode", "755")} " + cmd;
            }
            var execDir = Text(goal, "executionDirectory").Trim();
            if (!string.IsNullOrEmpty(execDir)) execDir = Normalize(execDir, "/");
            parsedGoals.Add((id, cmd, execDir));
        }

        var completedIds = new HashSet<string>(StringComparer.Ordinal);
        int nextSequentialIndex = 0;

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

            var directoryBeforeCommand = cwd;
            bool commandSuccess = false;
            string generatedOutput = "";

            // 1. Check exact-match custom commands (case-sensitive)
            if (customCommands.TryGetValue(commandToRun, out var customCmdInfo))
            {
                bool requirementsMet = true;
                foreach (var req in customCmdInfo.RequiredFiles)
                {
                    var cleanReq = req.Trim();
                    bool requiresDir = cleanReq.EndsWith('/');
                    var normReq = Normalize(cleanReq, cwd);

                    // Check that the prerequisite path is relative to cwd and does not escape cwd
                    var normCwdPrefix = cwd == "/" ? "/" : cwd + "/";
                    if (!normReq.StartsWith(normCwdPrefix) && normReq != cwd)
                    {
                        requirementsMet = false;
                        break;
                    }

                    if (requiresDir)
                    {
                        if (!DirExists(normReq))
                        {
                            requirementsMet = false;
                            break;
                        }
                    }
                    else
                    {
                        if (!FileExists(normReq) && !DirExists(normReq))
                        {
                            requirementsMet = false;
                            break;
                        }
                    }
                }

                if (!requirementsMet)
                {
                    commandSuccess = false;
                }
                else
                {
                    commandSuccess = true;
                    generatedOutput = customCmdInfo.Output;
                    if (!string.IsNullOrEmpty(redirectDest))
                    {
                        var destPath = Normalize(redirectDest, cwd);
                        var pPath = destPath.Contains('/') ? destPath.Substring(0, destPath.LastIndexOf('/')) : "";
                        if (string.IsNullOrEmpty(pPath)) pPath = "/";
                        if (!DirExists(pPath) || DirExists(destPath)) commandSuccess = false;
                        else dynamicFiles[destPath] = customCmdInfo.Output;
                    }
                }
            }
            else
            {
                // 2. Built-in command processing
                var parts = Regex.Split(commandToRun, @"\s+");
                var cmd = parts[0].ToLowerInvariant();
                var args = parts.Skip(1).ToArray();
                if (!Supported.Contains(cmd) || (!allowed.Contains(cmd) && cmd is not ("help" or "clear"))) continue;

                commandSuccess = true;
                string? operatedPath = null;
                switch (cmd)
                {
                    case "cd":
                        var destination = Normalize(args.FirstOrDefault() ?? "/home/student", cwd);
                        commandSuccess = DirExists(destination);
                        if (commandSuccess) cwd = destination;
                        break;
                    case "cat": case "chmod": case "grep":
                        var index = cmd == "cat" ? 0 : 1;
                        if (args.Length <= index) { commandSuccess = false; break; }
                        operatedPath = Normalize(args[index], cwd);
                        commandSuccess = FileExists(operatedPath);
                        if (cmd == "chmod") commandSuccess &= Regex.IsMatch(args[0], "^[0-7]{3,4}$");
                        if (commandSuccess && cmd == "cat")
                        {
                            generatedOutput = dynamicFiles.TryGetValue(operatedPath, out var fContent)
                                ? fContent : (Node(fs, operatedPath)?.GetString() ?? "");
                        }
                        break;
                    case "touch": case "nano":
                        if (args.Length == 0) { commandSuccess = false; break; }
                        operatedPath = Normalize(args[0], cwd);
                        var parentPath = operatedPath.Contains('/') ? operatedPath.Substring(0, operatedPath.LastIndexOf('/')) : "";
                        if (string.IsNullOrEmpty(parentPath)) parentPath = "/";
                        commandSuccess = DirExists(parentPath) && !DirExists(operatedPath);
                        if (commandSuccess && !dynamicFiles.ContainsKey(operatedPath))
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
                    case "whoami":
                        generatedOutput = "student";
                        break;
                    case "id":
                        generatedOutput = "uid=1000(student) gid=1000(student) groups=1000(student)";
                        break;
                    case "date":
                        generatedOutput = DateTime.UtcNow.ToString();
                        break;
                    case "history":
                    case "help":
                    case "clear":
                    case "ls":
                        break;
                    default:
                        commandSuccess = false;
                        break;
                }

                if (commandSuccess && !string.IsNullOrEmpty(redirectDest))
                {
                    var destPath = Normalize(redirectDest, directoryBeforeCommand);
                    var pPath = destPath.Contains('/') ? destPath.Substring(0, destPath.LastIndexOf('/')) : "";
                    if (string.IsNullOrEmpty(pPath)) pPath = "/";
                    if (!DirExists(pPath) || DirExists(destPath)) commandSuccess = false;
                    else dynamicFiles[destPath] = generatedOutput;
                }
            }

            if (!commandSuccess) continue;

            // Objective completion check
            if (isSequential)
            {
                if (nextSequentialIndex < parsedGoals.Count)
                {
                    var targetGoal = parsedGoals[nextSequentialIndex];
                    bool matchesCmd = (commandToRun == targetGoal.Command || input == targetGoal.Command);
                    bool matchesDir = string.IsNullOrEmpty(targetGoal.ExecDir) || directoryBeforeCommand == targetGoal.ExecDir;
                    if (matchesCmd && matchesDir)
                    {
                        completedIds.Add(targetGoal.Id);
                        nextSequentialIndex++;
                    }
                }
            }
            else
            {
                for (int i = 0; i < parsedGoals.Count; i++)
                {
                    var goal = parsedGoals[i];
                    if (completedIds.Contains(goal.Id)) continue;
                    bool matchesCmd = (commandToRun == goal.Command || input == goal.Command);
                    bool matchesDir = string.IsNullOrEmpty(goal.ExecDir) || directoryBeforeCommand == goal.ExecDir;
                    if (matchesCmd && matchesDir)
                    {
                        completedIds.Add(goal.Id);
                    }
                }
            }
        }

        return completedIds.Count == parsedGoals.Count;
    }
}
