using System;
using System.Collections.Generic;
using HackAcad.Web.Services;

void Check(bool ok, string msg)
{
    if (!ok) throw new Exception("FAIL: " + msg);
    Console.WriteLine("PASS: " + msg);
}

const string baseConfig = """
{
  "allowedCommands": ["pwd", "ls", "cd", "cat", "touch", "nano", "echo", "help", "clear"],
  "initialDirectory": "/home/student",
  "fileSystem": {
    "home": {
      "student": {
        "notes.txt": "Review secret audit"
      }
    },
    "var": {
      "log": {}
    }
  },
  "customCommands": [
    {
      "id": "cc_1",
      "command": "whois example.com",
      "output": "Domain Name: EXAMPLE.COM\nRegistry Domain ID: 2336799_DOMAIN_COM-VRSN",
      "enabled": true
    },
    {
      "id": "cc_2",
      "command": "service audit status",
      "output": "audit.service: active (running)",
      "enabled": false
    }
  ],
  "objectives": [
    {
      "id": "obj_cmd",
      "command": "whois example.com"
    },
    {
      "id": "obj_cat",
      "command": "cat net.txt"
    }
  ]
}
""";

// 1. TryConfiguration Validation Tests
Check(SimulationVerifier.TryConfiguration(baseConfig, out _, out _), "Valid configuration with customCommands and command objectives accepted");

// Reject duplicate custom command
const string dupConfig = """
{
  "allowedCommands": ["pwd"],
  "initialDirectory": "/home/student",
  "fileSystem": { "home": { "student": {} } },
  "customCommands": [
    { "command": "netstat -tuln", "output": "tcp 0.0.0.0:80", "enabled": true },
    { "command": "netstat -tuln", "output": "tcp 0.0.0.0:443", "enabled": true }
  ],
  "objectives": [{ "id": "1", "command": "netstat -tuln" }]
}
""";
Check(!SimulationVerifier.TryConfiguration(dupConfig, out _, out _), "Duplicate enabled custom command rejected");

// Reject conflict with built-in command
const string conflictConfig = """
{
  "allowedCommands": ["pwd"],
  "initialDirectory": "/home/student",
  "fileSystem": { "home": { "student": {} } },
  "customCommands": [
    { "command": "ls", "output": "fake ls", "enabled": true }
  ],
  "objectives": [{ "id": "1", "command": "ls" }]
}
""";
Check(!SimulationVerifier.TryConfiguration(conflictConfig, out _, out _), "Custom command conflicting with built-in 'ls' rejected");

// Reject reserved shell syntax
const string reservedConfig = """
{
  "allowedCommands": ["pwd"],
  "initialDirectory": "/home/student",
  "fileSystem": { "home": { "student": {} } },
  "customCommands": [
    { "command": "echo secret > output.txt", "output": "ok", "enabled": true }
  ],
  "objectives": [{ "id": "1", "command": "pwd" }]
}
""";
Check(!SimulationVerifier.TryConfiguration(reservedConfig, out _, out _), "Custom command containing '>' rejected");

// Obsolete commands (ping, nmap, curl, base64) are safely handled and ignored in TryConfiguration
const string obsoleteCommandsConfig = """
{
  "allowedCommands": ["pwd", "ping", "nmap", "curl", "base64", "cat"],
  "initialDirectory": "/home/student",
  "fileSystem": { "home": { "student": {} } },
  "objectives": [{ "id": "1", "command": "pwd" }]
}
""";
Check(SimulationVerifier.TryConfiguration(obsoleteCommandsConfig, out _, out _), "Configuration containing obsolete commands is accepted safely without errors");

// 2. IsComplete: Exact-Match Execution & Redirection
Check(!SimulationVerifier.IsComplete(baseConfig, null), "Null history rejected");
Check(!SimulationVerifier.IsComplete(baseConfig, Array.Empty<string>()), "Empty history rejected");

// Exact match satisfies custom command objective
Check(!SimulationVerifier.IsComplete(baseConfig, new[] { "whois example.com" }), "Incomplete when second objective not met");

// Redirection of custom command into net.txt satisfies cat objective
Check(SimulationVerifier.IsComplete(baseConfig, new[] {
    "whois example.com > net.txt",
    "cat net.txt"
}), "Exact custom command with redirection creates file and completes lab");

// Partial custom command does NOT match
Check(!SimulationVerifier.IsComplete(baseConfig, new[] {
    "whois example > net.txt",
    "cat net.txt"
}), "Partial custom command does not match");

// Extra arguments do NOT match
Check(!SimulationVerifier.IsComplete(baseConfig, new[] {
    "whois example.com extra > net.txt",
    "cat net.txt"
}), "Custom command with extra arguments does not match");

// Incorrect casing does NOT match
Check(!SimulationVerifier.IsComplete(baseConfig, new[] {
    "WHOIS EXAMPLE.COM > net.txt",
    "cat net.txt"
}), "Uppercase custom command does not match");

// Disabled custom command does NOT execute
const string disabledGoalConfig = """
{
  "allowedCommands": ["pwd", "cat"],
  "initialDirectory": "/home/student",
  "fileSystem": { "home": { "student": {} } },
  "customCommands": [
    { "command": "service audit status", "output": "active", "enabled": false }
  ],
  "objectives": [{ "id": "1", "command": "cat out.txt" }]
}
""";
Check(!SimulationVerifier.IsComplete(disabledGoalConfig, new[] { "service audit status > out.txt", "cat out.txt" }),
    "Disabled custom command cannot execute redirection or create files");

const string disabledCommandGoalConfig = """
{
  "allowedCommands": ["pwd"],
  "initialDirectory": "/home/student",
  "fileSystem": { "home": { "student": {} } },
  "customCommands": [
    { "command": "service audit status", "output": "active", "enabled": false }
  ],
  "objectives": [{ "id": "1", "command": "service audit status" }]
}
""";
Check(!SimulationVerifier.IsComplete(disabledCommandGoalConfig, new[] { "service audit status" }),
    "Disabled custom command cannot satisfy command objective");

// 3. Exact Command and Argument Matching
const string exactArgConfig = """
{
  "allowedCommands": ["cd", "cat", "pwd"],
  "initialDirectory": "/home/student",
  "fileSystem": {
    "home": { "student": {} },
    "var": { "secret": {} }
  },
  "objectives": [
    { "id": "nav_secret", "command": "cd /var/secret" }
  ]
}
""";
Check(!SimulationVerifier.IsComplete(exactArgConfig, new[] { "cd /home/student" }), "Different argument 'cd /home/student' does not satisfy 'cd /var/secret'");
Check(!SimulationVerifier.IsComplete(exactArgConfig, new[] { "cd" }), "'cd' with no argument does not satisfy 'cd /var/secret'");
Check(SimulationVerifier.IsComplete(exactArgConfig, new[] { "cd /var/secret" }), "Exact command 'cd /var/secret' satisfies objective");

// 4. Optional Required Execution Directory
const string execDirConfig = """
{
  "allowedCommands": ["cd", "cat", "pwd"],
  "initialDirectory": "/home/student",
  "fileSystem": {
    "home": { "student": {} },
    "var": { "secret": { "flag.txt": "FLAG{123}" } }
  },
  "objectives": [
    { "id": "read_flag", "command": "cat flag.txt", "executionDirectory": "/var/secret" }
  ]
}
""";
// Running 'cat flag.txt' from /home/student must fail because executionDirectory is /var/secret
Check(!SimulationVerifier.IsComplete(execDirConfig, new[] { "cat flag.txt" }), "Command executed in wrong directory fails objective with executionDirectory constraint");
Check(SimulationVerifier.IsComplete(execDirConfig, new[] { "cd /var/secret", "cat flag.txt" }), "Command executed in required directory satisfies objective");

// Invalid executionDirectory in config is rejected by TryConfiguration
const string invalidExecDirConfig = """
{
  "allowedCommands": ["cd"],
  "initialDirectory": "/home/student",
  "fileSystem": { "home": { "student": {} } },
  "objectives": [
    { "id": "invalid_dir", "command": "pwd", "executionDirectory": "/non/existent/path" }
  ]
}
""";
Check(!SimulationVerifier.TryConfiguration(invalidExecDirConfig, out _, out _), "Objective with non-existent executionDirectory is rejected during validation");

// 5. Sequential vs Non-Sequential Objective Completion
const string sequentialConfig = """
{
  "allowedCommands": ["cd", "cat", "pwd"],
  "initialDirectory": "/home/student",
  "sequentialObjectives": true,
  "fileSystem": {
    "home": { "student": {} },
    "var": { "secret": { "token.txt": "tok" } }
  },
  "objectives": [
    { "id": "step1", "command": "cd /var/secret" },
    { "id": "step2", "command": "cat token.txt" }
  ]
}
""";
// Executing step 2 before step 1 fails sequential completion
Check(!SimulationVerifier.IsComplete(sequentialConfig, new[] {
    "cat token.txt",
    "cd /var/secret"
}), "Executing future step prematurely fails sequential objective completion");

// Executing in correct order succeeds
Check(SimulationVerifier.IsComplete(sequentialConfig, new[] {
    "cd /var/secret",
    "cat token.txt"
}), "Executing steps in sequential order completes lab");

// Non-sequential configuration allows out-of-order execution
const string nonSequentialConfig = """
{
  "allowedCommands": ["cd", "cat", "pwd"],
  "initialDirectory": "/home/student",
  "sequentialObjectives": false,
  "fileSystem": {
    "home": { "student": {} },
    "var": { "secret": { "token.txt": "tok" } }
  },
  "objectives": [
    { "id": "step1", "command": "cd /var/secret" },
    { "id": "step2", "command": "pwd" }
  ]
}
""";
Check(SimulationVerifier.IsComplete(nonSequentialConfig, new[] {
    "pwd",
    "cd /var/secret"
}), "Non-sequential lab allows completing objectives in any order");

// 6. File Operations: touch and nano
const string touchLabConfig = """
{
  "allowedCommands": ["touch", "cat", "pwd"],
  "initialDirectory": "/home/student",
  "fileSystem": { "home": { "student": {} } },
  "objectives": [
    { "id": "1", "command": "touch created.txt" },
    { "id": "2", "command": "cat created.txt" }
  ]
}
""";
Check(SimulationVerifier.IsComplete(touchLabConfig, new[] {
    "touch created.txt",
    "cat created.txt"
}), "Touch creates virtual file that can be read by cat and satisfies objectives");

// Touch in invalid path fails
Check(!SimulationVerifier.IsComplete(touchLabConfig, new[] {
    "touch /invalid/dir/created.txt",
    "cat created.txt"
}), "Touch in non-existent directory fails");

// 7. Backward Compatibility: Existing configs using legacy type/target
const string legacyConfig = """
{
  "allowedCommands": ["cd", "cat", "chmod", "ls"],
  "initialDirectory": "/home/student",
  "fileSystem": { "home": { "student": {} }, "var": { "secret": { "audit.sh": "secret" } } },
  "objectives": [
    { "id": "nav", "type": "cwd", "target": "/var/secret" },
    { "id": "mode", "type": "chmod", "target": "audit.sh", "mode": "755" },
    { "id": "read", "type": "cat", "target": "audit.sh" }
  ]
}
""";
Check(SimulationVerifier.TryConfiguration(legacyConfig, out _, out _), "Legacy config without customCommands is accepted");
Check(SimulationVerifier.IsComplete(legacyConfig, new[] {
    "cd /var/secret",
    "chmod 755 audit.sh",
    "cat audit.sh"
}), "Legacy simulation executes and verifies without regressions");

// 8. Custom Command Required Files and Directories Tests
const string reqFilesConfig = """
{
  "allowedCommands": ["cd", "cat", "touch", "ls", "pwd"],
  "initialDirectory": "/home/student",
  "fileSystem": {
    "home": {
      "student": {
        "main.py": "print('hello')",
        "src": {
          "helper.py": "def help(): pass"
        },
        "data": {
          "items.csv": "a,b,c"
        }
      }
    },
    "var": {
      "log": {}
    }
  },
  "customCommands": [
    {
      "id": "cc_py",
      "command": "python main.py",
      "output": "hello from python",
      "enabled": true,
      "requiredFiles": ["main.py"]
    },
    {
      "id": "cc_nested",
      "command": "python src/helper.py",
      "output": "helper executed",
      "enabled": true,
      "requiredFiles": ["src/helper.py"]
    },
    {
      "id": "cc_multi",
      "command": "python run.py",
      "output": "multi-file success",
      "enabled": true,
      "requiredFiles": ["main.py", "data/"]
    },
    {
      "id": "cc_dynamic",
      "command": "python dynamic.py",
      "output": "dynamic file success",
      "enabled": true,
      "requiredFiles": ["created.py"]
    }
  ],
  "objectives": [
    { "id": "step1", "command": "python main.py", "executionDirectory": "/home/student" }
  ]
}
""";

Check(SimulationVerifier.TryConfiguration(reqFilesConfig, out _, out _), "Custom command with valid requiredFiles accepted");

// Reject absolute path in requiredFiles
const string invalidAbsPathConfig = """
{
  "allowedCommands": ["ls"],
  "initialDirectory": "/home/student",
  "fileSystem": { "home": { "student": {} } },
  "customCommands": [
    { "id": "cc1", "command": "python test.py", "output": "ok", "enabled": true, "requiredFiles": ["/etc/passwd"] }
  ],
  "objectives": [{ "id": "g1", "command": "ls" }]
}
""";
Check(!SimulationVerifier.TryConfiguration(invalidAbsPathConfig, out _, out _), "Custom command with absolute path in requiredFiles rejected");

// Reject traversal path in requiredFiles
const string invalidTraversalConfig = """
{
  "allowedCommands": ["ls"],
  "initialDirectory": "/home/student",
  "fileSystem": { "home": { "student": {} } },
  "customCommands": [
    { "id": "cc1", "command": "python test.py", "output": "ok", "enabled": true, "requiredFiles": ["../main.py"] }
  ],
  "objectives": [{ "id": "g1", "command": "ls" }]
}
""";
Check(!SimulationVerifier.TryConfiguration(invalidTraversalConfig, out _, out _), "Custom command with traversal path in requiredFiles rejected");

// 8.1 Command succeeds when required file exists in current directory
Check(SimulationVerifier.IsComplete(reqFilesConfig, new[] {
    "python main.py"
}), "python main.py succeeds when main.py exists in cwd (/home/student)");

// 8.2 Command fails when required file does not exist in cwd (/var/log) even though it exists in /home/student
Check(!SimulationVerifier.IsComplete(reqFilesConfig, new[] {
    "cd /var/log",
    "python main.py"
}), "python main.py fails when executed from /var/log where main.py is absent");

// 8.3 Changing cwd back to directory with file allows command to succeed
Check(SimulationVerifier.IsComplete(reqFilesConfig, new[] {
    "cd /var/log",
    "cd /home/student",
    "python main.py"
}), "Changing back to /home/student allows python main.py to succeed");

// 8.4 Nested relative path requirement
const string nestedObjConfig = """
{
  "allowedCommands": ["cd"],
  "initialDirectory": "/home/student",
  "fileSystem": {
    "home": {
      "student": {
        "src": { "helper.py": "content" }
      }
    }
  },
  "customCommands": [
    { "id": "cc1", "command": "python src/helper.py", "output": "ok", "enabled": true, "requiredFiles": ["src/helper.py"] }
  ],
  "objectives": [{ "id": "g1", "command": "python src/helper.py" }]
}
""";
Check(SimulationVerifier.IsComplete(nestedObjConfig, new[] {
    "python src/helper.py"
}), "Nested relative path requirement src/helper.py resolves correctly");

// 8.5 Multiple requirements: both file and directory must exist
const string multiReqConfig = """
{
  "allowedCommands": ["cd"],
  "initialDirectory": "/home/student",
  "fileSystem": {
    "home": {
      "student": {
        "main.py": "content",
        "data": {}
      }
    }
  },
  "customCommands": [
    { "id": "cc1", "command": "python run.py", "output": "ok", "enabled": true, "requiredFiles": ["main.py", "data/"] }
  ],
  "objectives": [{ "id": "g1", "command": "python run.py" }]
}
""";
Check(SimulationVerifier.IsComplete(multiReqConfig, new[] {
    "python run.py"
}), "Multiple requirements (main.py and data/) succeed when both exist");

// Fails if directory is missing
const string missingDirConfig = """
{
  "allowedCommands": ["cd"],
  "initialDirectory": "/home/student",
  "fileSystem": {
    "home": {
      "student": {
        "main.py": "content"
      }
    }
  },
  "customCommands": [
    { "id": "cc1", "command": "python run.py", "output": "ok", "enabled": true, "requiredFiles": ["main.py", "data/"] }
  ],
  "objectives": [{ "id": "g1", "command": "python run.py" }]
}
""";
Check(!SimulationVerifier.IsComplete(missingDirConfig, new[] {
    "python run.py"
}), "Multiple requirements fail when directory data/ is missing");

// 8.6 Dynamically created file via touch satisfies requirement
const string dynamicReqConfig = """
{
  "allowedCommands": ["touch"],
  "initialDirectory": "/home/student",
  "fileSystem": {
    "home": {
      "student": {}
    }
  },
  "customCommands": [
    { "id": "cc1", "command": "python script.py", "output": "script ok", "enabled": true, "requiredFiles": ["script.py"] }
  ],
  "objectives": [{ "id": "g1", "command": "python script.py" }]
}
""";
Check(!SimulationVerifier.IsComplete(dynamicReqConfig, new[] {
    "python script.py"
}), "Custom command fails before file is created");
Check(SimulationVerifier.IsComplete(dynamicReqConfig, new[] {
    "touch script.py",
    "python script.py"
}), "File created with touch satisfies requiredFiles requirement");

Console.WriteLine("ALL SIMULATION VERIFIER TESTS PASSED!");
