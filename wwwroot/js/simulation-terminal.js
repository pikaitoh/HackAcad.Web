/**
 * HackAcad Simulated Terminal Engine
 * NetAcad-style client-side CLI simulation environment.
 * Executes allowed commands inside a virtual filesystem and tracks lab objectives.
 */
(function () {
    'use strict';

    window.initSimulationTerminal = function (containerId, config, onCompleteCallback) {
        const container = document.getElementById(containerId);
        if (!container) return;

        let virtualFS = {
            "home": {
                "student": {
                    "notes.txt": "Mission: Navigate the system, inspect sensitive logs, and find the secret token.",
                    "readme.txt": "Welcome to HackAcad simulated terminal."
                }
            },
            "var": {
                "log": {
                    "auth.log": "Oct 18 10:00:01 server sshd[1234]: Accepted publickey for admin\nOct 18 10:05:22 server sshd[1288]: Failed password for invalid user hacker from 192.168.1.100\nOct 18 10:08:14 server audit[1400]: FLAG_TOKEN=flag{l1nux_sh3ll_m4st3ry}"
                },
                "secret": {
                    "audit.sh": "#!/bin/bash\n# Audit script\nTOKEN=\"flag{l1nux_sh3ll_m4st3ry}\"\necho \"Secret token: $TOKEN\""
                }
            },
            "etc": {
                "passwd": "root:x:0:0:root:/root:/bin/bash\nstudent:x:1000:1000:student:/home/student:/bin/bash\nguest:x:1001:1001:guest:/home/guest:/bin/sh",
                "hosts": "127.0.0.1 localhost\n10.10.10.50 target.hackacad.local"
            }
        };

        let allowedCommands = ["help", "clear", "pwd", "ls", "cd", "cat", "grep", "whoami", "chmod", "echo", "base64", "ping", "nmap", "curl", "history", "date", "id"];
        let currentPath = ["home", "student"];
        let filePermissions = {}; // e.g. "audit.sh": "755"
        let objectives = [];
        let commandHistory = [];
        let historyIndex = -1;
        let isTaskCompleted = false;
        let commandSucceeded = true;

        function escapeHtml(value) {
            return String(value).replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;').replace(/'/g, '&#39;');
        }

        function resolvePath(path) {
            if (path === '~') path = '/home/student';
            const resolved = path.startsWith('/') ? [] : [...currentPath];
            for (const part of path.split('/').filter(Boolean)) {
                if (part === '.') continue;
                if (part === '..') resolved.pop();
                else resolved.push(part);
            }
            return resolved;
        }

        function readFile(path) {
            const parts = resolvePath(path);
            const name = parts.pop();
            const directory = getDirectoryNode(parts);
            return directory && Object.hasOwn(directory, name) && typeof directory[name] === 'string' ? directory[name] : null;
        }

        function matchesFile(path, target) {
            return target.includes('/') ? resolvePath(path).join('/') === resolvePath(target).join('/') : resolvePath(path).at(-1) === target;
        }

        // Parse teacher config
        if (config) {
            try {
                const parsed = typeof config === 'string' ? JSON.parse(config) : config;
                if (parsed.allowedCommands && Array.isArray(parsed.allowedCommands)) {
                    allowedCommands = parsed.allowedCommands.map(c => c.toLowerCase().trim());
                    if (!allowedCommands.includes("help")) allowedCommands.push("help");
                    if (!allowedCommands.includes("clear")) allowedCommands.push("clear");
                }
                if (parsed.fileSystem) virtualFS = parsed.fileSystem;
                if (parsed.initialDirectory) {
                    currentPath = parsed.initialDirectory.replace(/^\/+|\/+$/g, '').split('/').filter(Boolean);
                }
                if (parsed.objectives && Array.isArray(parsed.objectives)) {
                    objectives = parsed.objectives.map(o => ({ ...o, completed: false }));
                }
            } catch (e) {
                console.warn("Using default simulation environment:", e);
            }
        }

        if (objectives.length === 0) {
            objectives = [
                { id: "explore", text: "Explore the filesystem using cd and ls", completed: false },
                { id: "find_flag", text: "Locate and read the secret file using cat", completed: false }
            ];
        }

        // Render terminal HTML
        container.innerHTML = `
            <div class="terminal-wrapper">
                <div class="terminal-header">
                    <div class="terminal-buttons">
                        <span class="t-btn t-btn-red"></span>
                        <span class="t-btn t-btn-yellow"></span>
                        <span class="t-btn t-btn-green"></span>
                    </div>
                    <div class="terminal-title">student@hackacad: ~ (bash simulation)</div>
                    <div class="terminal-badge">NetAcad Sandbox</div>
                </div>
                <div class="terminal-body" id="term-output">
                    <div class="term-line term-banner">
                        <pre>  _    _            _        _                   _ 
 | |  | |          | |      / \\                 | |
 | |__| | __ _  ___| | __  / _ \\   ___ __ _   __| |
 |  __  |/ _\` |/ __| |/ / / ___ \\ / __/ _\` | / _\` |
 |_|  |_|\\__,_|\\___|_|\\_\\/_/   \\_\\___\\__,_|\\__,_|
  Virtual Security Sandbox • NetAcad Engine v2.0</pre>
                    </div>
                    <div class="term-line text-muted">Type <span class="term-highlight">help</span> to view allowed lab commands. Tab to auto-complete.</div>
                    <div class="term-line text-muted">Type <span class="term-highlight">clear</span> to reset screen.</div>
                    <div class="term-line">&nbsp;</div>
                </div>
                <div class="terminal-input-row">
                    <span class="terminal-prompt" id="term-prompt">student@hackacad:${escapeHtml(getPathString())}$ </span>
                    <input type="text" id="term-input" class="terminal-input" autocomplete="off" autocorrect="off" autocapitalize="off" spellcheck="false" autofocus />
                </div>
            </div>
        `;

        const outputEl = container.querySelector('#term-output');
        const inputEl = container.querySelector('#term-input');
        const promptEl = container.querySelector('#term-prompt');

        function updatePrompt() {
            promptEl.textContent = `student@hackacad:${getPathString()}$ `;
        }

        function getPathString() {
            return '/' + currentPath.join('/');
        }

        function getDirectoryNode(pathArray) {
            let curr = virtualFS;
            for (let part of pathArray) {
                if (!curr || typeof curr !== 'object' || !Object.hasOwn(curr, part)) {
                    return null;
                }
                curr = curr[part];
            }
            return curr;
        }

        function printLine(content, className = '') {
            const line = document.createElement('div');
            line.className = `term-line ${className}`;
            if (className === 'term-error') commandSucceeded = false;
            line.innerHTML = content;
            outputEl.appendChild(line);
            outputEl.scrollTop = outputEl.scrollHeight;
        }

        function printOutput(text, className = '') {
            const escaped = text
                .replace(/&/g, '&amp;')
                .replace(/</g, '&lt;')
                .replace(/>/g, '&gt;');
            printLine(escaped, className);
        }

        function renderObjectives() {
            const listEl = document.getElementById('simulation-objectives-list');
            if (!listEl) return;
            listEl.innerHTML = objectives.map(obj => `
                <li class="objective-item ${obj.completed ? 'completed' : 'pending'}">
                    <span class="obj-icon">${obj.completed ? '✓' : '○'}</span>
                    <span class="obj-text">${escapeHtml(obj.text)}</span>
                </li>
            `).join('');
        }

        function checkObjectives(cmd, args) {
            if (!commandSucceeded) return;
            objectives.forEach(obj => {
                if (obj.completed) return;
                if (obj.type === 'cwd' && getPathString() === '/' + resolvePath(obj.target).join('/')) obj.completed = true;
                if (obj.type === 'cat' && cmd === 'cat' && args[0] && matchesFile(args[0], obj.target)) obj.completed = true;
                if (obj.type === 'chmod' && cmd === 'chmod' && args[1] && matchesFile(args[1], obj.target) && args[0] === (obj.mode || '755')) obj.completed = true;
                if (obj.type === 'command' && cmd === obj.target) obj.completed = true;
            });
            renderObjectives();
            if (!isTaskCompleted && objectives.every(o => o.completed)) {
                isTaskCompleted = true;
                triggerSuccess();
            }
        }

        function triggerSuccess() {
            printOutput('Lab objectives met. Waiting for server verification...');
            if (typeof onCompleteCallback === 'function') {
                Promise.resolve(onCompleteCallback([...commandHistory])).catch(error => {
                    isTaskCompleted = false;
                    printOutput(error.message || 'Progress could not be saved. Run a command to try again.', 'term-error');
                });
            }
        }

        // Handle Command Execution
        function executeCommand(rawInput) {
            const trimmed = rawInput.trim();
            if (!trimmed) {
                printLine(`<span class="term-prompt">${escapeHtml(promptEl.textContent)}</span>`);
                return;
            }

            commandSucceeded = true;
            commandHistory.push(trimmed);
            historyIndex = commandHistory.length;

            printLine(`<span class="term-prompt">${escapeHtml(promptEl.textContent)}</span> <span class="term-cmd">${escapeHtml(trimmed)}</span>`);

            const parts = trimmed.split(/\s+/);
            const cmd = parts[0].toLowerCase();
            const args = parts.slice(1);

            // Check if command is permitted by teacher in this exercise
            if (!allowedCommands.includes(cmd)) {
                printLine(`hackacad-sh: <strong>${escapeHtml(cmd)}</strong>: command not allowed in this lab exercise.`, 'term-error');
                printLine(`Type <span class="term-highlight">help</span> for allowed commands.`, 'text-secondary');
                return;
            }

            let outputResult = '';

            switch (cmd) {
                case 'help':
                    printLine('<strong>Available Lab Commands:</strong>');
                    allowedCommands.forEach(c => {
                        let desc = '';
                        switch (c) {
                            case 'help': desc = 'Show this allowed command list'; break;
                            case 'clear': desc = 'Clear the terminal screen'; break;
                            case 'pwd': desc = 'Print current working directory'; break;
                            case 'ls': desc = 'List files and subdirectories'; break;
                            case 'cd': desc = 'Change working directory'; break;
                            case 'cat': desc = 'Display contents of a file'; break;
                            case 'grep': desc = 'Search for patterns in file'; break;
                            case 'chmod': desc = 'Change file access permissions'; break;
                            case 'whoami': desc = 'Display current user identity'; break;
                            case 'id': desc = 'Display user and group IDs'; break;
                            case 'echo': desc = 'Print text to terminal'; break;
                            case 'base64': desc = 'Base64 encode or decode (-d)'; break;
                            case 'ping': desc = 'Simulate ICMP ping to a remote host'; break;
                            case 'nmap': desc = 'Network exploration and port scan'; break;
                            case 'curl': desc = 'Simulate HTTP request to a URL'; break;
                            case 'history': desc = 'Display command history'; break;
                            case 'date': desc = 'Display current system time'; break;
                            default: desc = 'Lab utility'; break;
                        }
                        printLine(`  <span class="term-highlight">${escapeHtml(c.padEnd(10))}</span> - ${desc}`);
                    });
                    break;

                case 'clear':
                    outputEl.innerHTML = '';
                    break;

                case 'pwd':
                    outputResult = getPathString();
                    printLine(outputResult);
                    break;

                case 'whoami':
                    outputResult = 'student';
                    printLine(outputResult);
                    break;

                case 'id':
                    outputResult = 'uid=1000(student) gid=1000(student) groups=1000(student),4(adm),27(sudo)';
                    printLine(outputResult);
                    break;

                case 'date':
                    outputResult = new Date().toUTCString();
                    printLine(outputResult);
                    break;

                case 'history':
                    commandHistory.forEach((h, idx) => {
                        printLine(`  ${(idx + 1).toString().padStart(3, ' ')}  ${escapeHtml(h)}`);
                    });
                    break;

                case 'ls': {
                    const isLong = args.includes('-l') || args.includes('-la') || args.includes('-al');
                    const showAll = args.includes('-a') || args.includes('-la') || args.includes('-al');
                    const targetDir = getDirectoryNode(currentPath);

                    if (!targetDir || typeof targetDir !== 'object') {
                        printLine('ls: cannot access directory: No such directory', 'term-error');
                        break;
                    }

                    const entries = Object.keys(targetDir);
                    if (isLong) {
                        printLine(`total ${entries.length * 4}`);
                        if (showAll) {
                            printLine(`drwxr-xr-x 2 student student 4096 Oct 18 10:00 .`);
                            printLine(`drwxr-xr-x 4 root    root    4096 Oct 18 10:00 ..`);
                        }
                        entries.forEach(name => {
                            const isDir = typeof targetDir[name] === 'object';
                            const perm = isDir ? 'drwxr-xr-x' : (filePermissions[[...currentPath, name].join('/')] ? `-rwxr-xr-x` : `-rw-r--r--`);
                            const size = isDir ? 4096 : (targetDir[name] ? targetDir[name].length : 0);
                            const coloredName = isDir
                                ? `<span class="term-dir">${escapeHtml(name)}/</span>`
                                : (filePermissions[[...currentPath, name].join('/')] ? `<span class="term-exec">${escapeHtml(name)}*</span>` : escapeHtml(name));
                            printLine(`${perm} 1 student student ${size.toString().padStart(5, ' ')} Oct 18 10:00 ${coloredName}`);
                        });
                    } else {
                        const formatted = entries.map(name => {
                            const isDir = typeof targetDir[name] === 'object';
                            return isDir
                                ? `<span class="term-dir">${escapeHtml(name)}/</span>`
                                : (filePermissions[[...currentPath, name].join('/')] ? `<span class="term-exec">${escapeHtml(name)}*</span>` : escapeHtml(name));
                        });
                        printLine(formatted.join('&nbsp;&nbsp;&nbsp;&nbsp;'));
                    }
                    break;
                }

                case 'cd': {
                    const dest = args[0] || '/home/student';
                    const resolved = resolvePath(dest);
                    const directory = getDirectoryNode(resolved);
                    if (directory && typeof directory === 'object') {
                        currentPath = resolved;
                        updatePrompt();
                    } else printOutput(`cd: ${dest}: No such directory`, 'term-error');
                    break;
                }

                case 'cat': {
                    const content = args[0] ? readFile(args[0]) : null;
                    if (content === null) printOutput('cat: choose an existing file', 'term-error');
                    else { outputResult = content; printOutput(content); }
                    break;
                }

                case 'grep': {
                    if (args.length < 2) {
                        printLine('Usage: grep [PATTERN] [FILE]', 'term-error');
                        break;
                    }
                    const pattern = args[0].replace(/['"]/g, '');
                    const fileName = args[1];
                    const currNode = getDirectoryNode(currentPath);

                    let content = '';
                    if (currNode && typeof currNode === 'object' && typeof currNode[fileName] === 'string') {
                        content = currNode[fileName];
                    } else {
                        const absParts = fileName.replace(/^\/+|\/+$/g, '').split('/');
                        const f = absParts.pop();
                        const dir = getDirectoryNode(absParts);
                        if (dir && typeof dir === 'object' && typeof dir[f] === 'string') content = dir[f];
                    }

                    if (content) {
                        const lines = content.split('\n');
                        const matches = lines.filter(l => l.toLowerCase().includes(pattern.toLowerCase()));
                        if (matches.length > 0) {
                            matches.forEach(m => {
                                const literalPattern = escapeHtml(pattern).replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
                                const highlighted = escapeHtml(m).replace(new RegExp(literalPattern, 'gi'), '<span class="term-match">$&</span>');
                                printLine(highlighted);
                            });
                            outputResult = matches.join('\n');
                        }
                    } else {
                        printLine(`grep: ${escapeHtml(fileName)}: No such file or directory`, 'term-error');
                    }
                    break;
                }

                case 'chmod': {
                    if (args.length < 2 || !/^[0-7]{3,4}$/.test(args[0]) || readFile(args[1]) === null) {
                        printOutput('chmod: use a valid mode and an existing file', 'term-error');
                        break;
                    }
                    filePermissions[resolvePath(args[1]).join('/')] = args[0];
                    printOutput(`Changed permissions of '${args[1]}' to ${args[0]}`);
                    break;
                }

                case 'echo': {
                    const text = args.join(' ');
                    printOutput(text);
                    break;
                }

                case 'base64': {
                    if (args.includes('-d')) {
                        const target = args[args.indexOf('-d') + 1];
                        if (!target) {
                            printLine('base64: missing argument to decode', 'term-error');
                            break;
                        }
                        try {
                            const decoded = atob(target);
                            outputResult = decoded;
                            printOutput(decoded);
                        } catch (e) {
                            printLine('base64: invalid input', 'term-error');
                        }
                    } else {
                        const target = args[0] || '';
                        printOutput(btoa(target));
                    }
                    break;
                }

                case 'ping': {
                    const host = args[args.length - 1] || 'localhost';
                    printLine(`PING ${escapeHtml(host)} (127.0.0.1) 56(84) bytes of data.`);
                    printLine(`64 bytes from ${escapeHtml(host)} (127.0.0.1): icmp_seq=1 ttl=64 time=0.035 ms`);
                    printLine(`64 bytes from ${escapeHtml(host)} (127.0.0.1): icmp_seq=2 ttl=64 time=0.041 ms`);
                    printLine(`64 bytes from ${escapeHtml(host)} (127.0.0.1): icmp_seq=3 ttl=64 time=0.038 ms`);
                    printLine(`--- ${escapeHtml(host)} ping statistics ---`);
                    printLine(`3 packets transmitted, 3 received, 0% packet loss, time 2004ms`);
                    break;
                }

                case 'nmap': {
                    const target = args[args.length - 1] || 'target.hackacad.local';
                    printLine(`Starting Nmap 7.94 ( https://nmap.org ) at 2026-10-18 10:00 UTC`);
                    printLine(`Nmap scan report for ${escapeHtml(target)} (10.10.10.50)`);
                    printLine(`Host is up (0.00045s latency).`);
                    printLine(`Not shown: 997 closed tcp ports (reset)`);
                    printLine(`PORT     STATE SERVICE VERSION`);
                    printLine(`22/tcp   open  ssh     OpenSSH 8.9p1 Ubuntu`);
                    printLine(`80/tcp   open  http    nginx 1.18.0`);
                    printLine(`8080/tcp open  http    HackAcad Internal Auth API`);
                    printLine(`Nmap done: 1 IP address (1 host up) scanned in 1.42 seconds`);
                    outputResult = 'nmap done';
                    break;
                }

                case 'curl': {
                    const url = args[0] || 'http://localhost';
                    printLine(`&gt; GET ${escapeHtml(url)} HTTP/1.1`);
                    printLine(`&lt; HTTP/1.1 200 OK`);
                    printLine(`&lt; Content-Type: application/json`);
                    printLine(`&lt; Server: HackAcad-Sandbox/1.0`);
                    printLine(`&lt; `);
                    if (url.includes('secret') || url.includes('flag') || url.includes('debug')) {
                        outputResult = '{"status":"success","data":{"token":"flag{w3b_r3c0n_curl_m4st3r}"}}';
                    } else {
                        outputResult = '{"status":"active","message":"HackAcad endpoint is operational."}';
                    }
                    printOutput(outputResult);
                    break;
                }

                default:
                    printLine(`hackacad-sh: ${escapeHtml(cmd)}: command not found`, 'term-error');
                    break;
            }

            checkObjectives(cmd, args, outputResult);
        }

        // Key Listeners
        inputEl.addEventListener('keydown', function (e) {
            if (e.key === 'Enter') {
                const val = inputEl.value;
                inputEl.value = '';
                executeCommand(val);
            } else if (e.key === 'ArrowUp') {
                e.preventDefault();
                if (historyIndex > 0) {
                    historyIndex--;
                    inputEl.value = commandHistory[historyIndex] || '';
                }
            } else if (e.key === 'ArrowDown') {
                e.preventDefault();
                if (historyIndex < commandHistory.length - 1) {
                    historyIndex++;
                    inputEl.value = commandHistory[historyIndex] || '';
                } else {
                    historyIndex = commandHistory.length;
                    inputEl.value = '';
                }
            } else if (e.key === 'Tab') {
                e.preventDefault();
                // Autocomplete
                const val = inputEl.value;
                const tokens = val.split(' ');
                const lastToken = tokens[tokens.length - 1];
                if (lastToken) {
                    const currNode = getDirectoryNode(currentPath);
                    if (currNode && typeof currNode === 'object') {
                        const match = Object.keys(currNode).find(k => k.startsWith(lastToken));
                        if (match) {
                            tokens[tokens.length - 1] = match;
                            inputEl.value = tokens.join(' ');
                        }
                    }
                }
            }
        });

        // Focus input on clicking anywhere in terminal
        container.addEventListener('click', function () {
            inputEl.focus();
        });

        // Initial render of objectives
        renderObjectives();
    };
})();
