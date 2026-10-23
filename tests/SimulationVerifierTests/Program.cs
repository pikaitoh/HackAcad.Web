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
      "command": "show ip interface brief",
      "output": "Fa0/1 192.168.1.1 up up",
      "enabled": true
    },
    {
      "id": "cc_2",
      "command": "show running-config",
      "output": "hostname Router1",
      "enabled": false
    }
  ],
  "objectives": [
    {
      "id": "obj_cmd",
      "type": "command",
      "target": "show ip interface brief"
    },
    {
      "id": "obj_cat",
      "type": "cat",
      "target": "net.txt"
    }
  ]
}
""";

// 1. TryConfiguration Validation Tests
Check(SimulationVerifier.TryConfiguration(baseConfig, out _, out _), "Valid configuration with customCommands accepted");

// Reject duplicate custom command
const string dupConfig = """
{
  "allowedCommands": ["pwd"],
  "initialDirectory": "/home/student",
  "fileSystem": { "home": { "student": {} } },
  "customCommands": [
    { "command": "show vlan", "output": "vlan 1", "enabled": true },
    { "command": "show vlan", "output": "vlan 2", "enabled": true }
  ],
  "objectives": [{ "id": "1", "type": "command", "target": "show vlan" }]
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
  "objectives": [{ "id": "1", "type": "command", "target": "ls" }]
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
    { "command": "ping 8.8.8.8 > output.txt", "output": "ok", "enabled": true }
  ],
  "objectives": [{ "id": "1", "type": "command", "target": "pwd" }]
}
""";
Check(!SimulationVerifier.TryConfiguration(reservedConfig, out _, out _), "Custom command containing '>' rejected");

// 2. IsComplete: Exact-Match Execution & Redirection
Check(!SimulationVerifier.IsComplete(baseConfig, null), "Null history rejected");
Check(!SimulationVerifier.IsComplete(baseConfig, Array.Empty<string>()), "Empty history rejected");

// Exact match satisfies custom command objective
Check(!SimulationVerifier.IsComplete(baseConfig, new[] { "show ip interface brief" }), "Incomplete when second objective not met");

// Redirection of custom command into net.txt satisfies cat objective
Check(SimulationVerifier.IsComplete(baseConfig, new[] {
    "show ip interface brief > net.txt",
    "cat net.txt"
}), "Exact custom command with redirection creates file and completes lab");

// Partial custom command does NOT match
Check(!SimulationVerifier.IsComplete(baseConfig, new[] {
    "show ip interface > net.txt",
    "cat net.txt"
}), "Partial custom command does not match");

// Extra arguments do NOT match
Check(!SimulationVerifier.IsComplete(baseConfig, new[] {
    "show ip interface brief extra > net.txt",
    "cat net.txt"
}), "Custom command with extra arguments does not match");

// Incorrect casing does NOT match
Check(!SimulationVerifier.IsComplete(baseConfig, new[] {
    "SHOW IP INTERFACE BRIEF > net.txt",
    "cat net.txt"
}), "Uppercase custom command does not match");

// Disabled custom command does NOT execute
const string disabledGoalConfig = """
{
  "allowedCommands": ["pwd", "cat"],
  "initialDirectory": "/home/student",
  "fileSystem": { "home": { "student": {} } },
  "customCommands": [
    { "command": "show running-config", "output": "hostname R1", "enabled": false }
  ],
  "objectives": [{ "id": "1", "type": "cat", "target": "out.txt" }]
}
""";
Check(!SimulationVerifier.IsComplete(disabledGoalConfig, new[] { "show running-config > out.txt", "cat out.txt" }),
    "Disabled custom command cannot execute redirection or create files");

const string disabledCommandGoalConfig = """
{
  "allowedCommands": ["pwd"],
  "initialDirectory": "/home/student",
  "fileSystem": { "home": { "student": {} } },
  "customCommands": [
    { "command": "show running-config", "output": "hostname R1", "enabled": false }
  ],
  "objectives": [{ "id": "1", "type": "command", "target": "show running-config" }]
}
""";
Check(!SimulationVerifier.IsComplete(disabledCommandGoalConfig, new[] { "show running-config" }),
    "Disabled custom command cannot satisfy command objective");

// 3. File Operations: touch and nano
const string touchLabConfig = """
{
  "allowedCommands": ["touch", "cat", "pwd"],
  "initialDirectory": "/home/student",
  "fileSystem": { "home": { "student": {} } },
  "objectives": [
    { "id": "1", "type": "command", "target": "touch" },
    { "id": "2", "type": "cat", "target": "created.txt" }
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

// 4. Backward Compatibility: Existing configs without customCommands
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

Console.WriteLine("ALL SIMULATION VERIFIER TESTS PASSED!");
