/**
 * HackAcad Simulated Terminal Engine
<<<<<<< Updated upstream
 * Client-side CLI simulation environment.
=======
 * NetAcad-style client-side CLI simulation environment.
>>>>>>> Stashed changes
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

<<<<<<< Updated upstream
        const allBuiltins = ["help", "clear", "pwd", "ls", "cd", "cat", "grep", "whoami", "id", "echo", "history", "date", "touch", "nano", "chmod"];
        const obsoleteBuiltins = ["ping", "nmap", "curl", "base64"];

        let allowedCommands = ["help", "clear", "pwd", "ls", "cd", "cat", "grep", "whoami", "chmod", "echo", "history", "date", "id", "touch", "nano"];
        let customCommands = [];
        let currentPath = ["home", "student"];
        let filePermissions = {};
        let objectives = [];
        let isSequential = true;
        let commandHistory = [];
        let historyIndex = -1;
        let isTaskCompleted = false;
        let commandSucceeded = true;

        const hintStorageKey = 'hackacad_introduced_hints_' + containerId;
        const introducedHints = new Set();
        try {
            const stored = JSON.parse(sessionStorage.getItem(hintStorageKey) || '[]');
            stored.forEach(k => introducedHints.add(k));
        } catch (e) { }

        function decodeHtmlEntities(str) {
            return String(str || '')
                .replace(/&nbsp;/g, ' ')
                .replace(/&lt;/g, '<')
                .replace(/&gt;/g, '>')
                .replace(/&amp;/g, '&')
                .replace(/&quot;/g, '"')
                .replace(/&#39;/g, "'");
        }

        function escapeHtml(value) {
            return String(value ?? '').replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;').replace(/'/g, '&#39;');
        }

        function resolvePath(path) {
            if (!path || path === '~') path = '/home/student';
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

        let isNotebook = false;

        // Parse instructor config
        if (config) {
            try {
                const parsed = typeof config === 'string' ? JSON.parse(config) : config;
                if (parsed.isNotebook === true || parsed.noObjectives === true) {
                    isNotebook = true;
                }
                if (parsed.allowedCommands && Array.isArray(parsed.allowedCommands)) {
                    allowedCommands = parsed.allowedCommands
                        .map(c => c.toLowerCase().trim())
                        .filter(c => allBuiltins.includes(c)); // discard obsolete/removed commands safely
                }
                // Permanently enabled built-in system commands
                if (!allowedCommands.includes("help")) allowedCommands.push("help");
                if (!allowedCommands.includes("clear")) allowedCommands.push("clear");

=======
        let allowedCommands = ["help", "clear", "pwd", "ls", "cd", "cat", "grep", "whoami", "chmod", "echo", "base64", "ping", "nmap", "curl", "history", "date", "id"];
        let currentPath = ["home", "student"];
        let filePermissions = {}; // e.g. "audit.sh": "755"
        let objectives = [];
        let commandHistory = [];
        let historyIndex = -1;
        let isTaskCompleted = false;

        // Parse teacher config
        if (config) {
            try {
                const parsed = typeof config === 'string' ? JSON.parse(config) : config;
                if (parsed.allowedCommands && Array.isArray(parsed.allowedCommands)) {
                    allowedCommands = parsed.allowedCommands.map(c => c.toLowerCase().trim());
                    if (!allowedCommands.includes("help")) allowedCommands.push("help");
                    if (!allowedCommands.includes("clear")) allowedCommands.push("clear");
                }
>>>>>>> Stashed changes
                if (parsed.fileSystem) virtualFS = parsed.fileSystem;
                if (parsed.initialDirectory) {
                    currentPath = parsed.initialDirectory.replace(/^\/+|\/+$/g, '').split('/').filter(Boolean);
                }
<<<<<<< Updated upstream

                isSequential = parsed.sequentialObjectives === true;

                if (parsed.objectives && Array.isArray(parsed.objectives)) {
                    objectives = parsed.objectives.map((o, idx) => {
                        let cmd = (o.command || o.expectedCommand || '').trim();
                        // Backward-compatibility fallback for legacy objectives
                        if (!cmd && o.target) {
                            if (o.type === 'cwd') cmd = `cd ${o.target}`;
                            else if (o.type === 'cat') cmd = `cat ${o.target}`;
                            else if (o.type === 'chmod') cmd = `chmod ${o.mode || '755'} ${o.target}`;
                            else cmd = o.target;
                        }
                        return {
                            id: o.id || `obj_${idx + 1}`,
                            text: o.text || '',
                            command: cmd,
                            executionDirectory: o.executionDirectory ? '/' + o.executionDirectory.replace(/^\/+|\/+$/g, '') : '',
                            hint: (o.hint || '').trim(),
                            completed: false
                        };
                    });
                }

                if (parsed.customCommands && Array.isArray(parsed.customCommands)) {
                    customCommands = parsed.customCommands.filter(c => c && c.enabled !== false && typeof c.command === 'string' && c.command.trim()).map(c => {
                        let reqFiles = [];
                        if (Array.isArray(c.requiredFiles)) {
                            reqFiles = c.requiredFiles.map(s => String(s).trim()).filter(Boolean);
                        } else if (typeof c.requiredFiles === 'string') {
                            reqFiles = c.requiredFiles.split(/[\r\n,]+/).map(s => s.trim()).filter(Boolean);
                        }
                        return {
                            ...c,
                            requiredFiles: reqFiles
                        };
                    });
=======
                if (parsed.objectives && Array.isArray(parsed.objectives)) {
                    objectives = parsed.objectives.map(o => ({ ...o, completed: false }));
>>>>>>> Stashed changes
                }
            } catch (e) {
                console.warn("Using default simulation environment:", e);
            }
        }

<<<<<<< Updated upstream
        if (objectives.length === 0 && !isNotebook) {
            objectives = [
                { id: "explore", text: "Explore the filesystem with pwd", command: "pwd", executionDirectory: "", hint: "Type 'pwd' and press Enter", completed: false },
                { id: "find_flag", text: "Locate and read notes.txt using cat", command: "cat notes.txt", executionDirectory: "/home/student", hint: "Run 'cat notes.txt'", completed: false }
=======
        if (objectives.length === 0) {
            objectives = [
                { id: "explore", text: "Explore the filesystem using cd and ls", completed: false },
                { id: "find_flag", text: "Locate and read the secret file using cat", completed: false }
>>>>>>> Stashed changes
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
<<<<<<< Updated upstream
                    <div class="terminal-badge">HackAcad Sandbox</div>
=======
                    <div class="terminal-badge">NetAcad Sandbox</div>
>>>>>>> Stashed changes
                </div>
                <div class="terminal-body" id="term-output">
                    <div class="term-line term-banner">
                        <pre>  _    _            _        _                   _ 
 | |  | |          | |      / \\                 | |
 | |__| | __ _  ___| | __  / _ \\   ___ __ _   __| |
 |  __  |/ _\` |/ __| |/ / / ___ \\ / __/ _\` | / _\` |
 |_|  |_|\\__,_|\\___|_|\\_\\/_/   \\_\\___\\__,_|\\__,_|
<<<<<<< Updated upstream
  Virtual Security Sandbox • Terminal Engine v2.0</pre>
=======
  Virtual Security Sandbox • NetAcad Engine v2.0</pre>
>>>>>>> Stashed changes
                    </div>
                    <div class="term-line text-muted">Type <span class="term-highlight">help</span> to view allowed lab commands. Tab to auto-complete.</div>
                    <div class="term-line text-muted">Type <span class="term-highlight">clear</span> to reset screen.</div>
                    <div class="term-line">&nbsp;</div>
                </div>
                <div class="terminal-input-row">
<<<<<<< Updated upstream
                    <span class="terminal-prompt" id="term-prompt">student@hackacad:${escapeHtml(getPathString())}$ </span>
=======
                    <span class="terminal-prompt" id="term-prompt">student@hackacad:${getPathString()}$ </span>
>>>>>>> Stashed changes
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
<<<<<<< Updated upstream
                if (!curr || typeof curr !== 'object' || !Object.hasOwn(curr, part)) {
=======
                if (!curr || typeof curr !== 'object' || curr[part] === undefined) {
>>>>>>> Stashed changes
                    return null;
                }
                curr = curr[part];
            }
            return curr;
        }

        function printLine(content, className = '') {
            const line = document.createElement('div');
            line.className = `term-line ${className}`;
<<<<<<< Updated upstream
            if (className === 'term-error') commandSucceeded = false;
=======
>>>>>>> Stashed changes
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

<<<<<<< Updated upstream
        function printHint(idx, hintText) {
            if (!hintText || !hintText.trim()) return;
            const cleanHint = hintText.trim();
            const hintKey = `step_${idx}_${cleanHint}`;
            if (introducedHints.has(hintKey)) return;
            introducedHints.add(hintKey);
            try {
                const stored = JSON.parse(sessionStorage.getItem(hintStorageKey) || '[]');
                if (!stored.includes(hintKey)) {
                    stored.push(hintKey);
                    sessionStorage.setItem(hintStorageKey, JSON.stringify(stored));
                }
            } catch (e) { }

            const stepLabel = isSequential ? `Step #${idx + 1}` : `Objective`;
            printLine(`<div class="term-line" style="color: #38bdf8; margin: 4px 0;"><span class="term-highlight"><svg viewBox="0 0 24 24" width="14" height="14" fill="none" stroke="currentColor" stroke-width="2" style="vertical-align:-2px;margin-right:4px;"><path d="M9 18h6"></path><path d="M10 22h4"></path><path d="M12 2a7 7 0 0 0-7 7c0 2.5 1.5 4.5 3 6h8c1.5-1.5 3-3.5 3-6a7 7 0 0 0-7-7z"></path></svg>Hint (${stepLabel}):</span> ${escapeHtml(cleanHint)}</div>`);
        }

        function introduceAvailableHints() {
            if (isSequential) {
                const nextIdx = objectives.findIndex(o => !o.completed);
                if (nextIdx !== -1 && objectives[nextIdx].hint) {
                    printHint(nextIdx, objectives[nextIdx].hint);
                }
            } else {
                objectives.forEach((obj, idx) => {
                    if (!obj.completed && obj.hint) {
                        printHint(idx, obj.hint);
                    }
                });
            }
        }

        function renderObjectives() {
            const listEl = document.getElementById('simulation-objectives-list');
            if (!listEl) return;
            const checkSvg = '<svg class="sim-icon" viewBox="0 0 24 24" width="16" height="16" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><polyline points="20 6 9 17 4 12"></polyline></svg>';
            const pendingSvg = '<svg class="sim-icon" viewBox="0 0 24 24" width="16" height="16" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><circle cx="12" cy="12" r="9"></circle></svg>';
            listEl.innerHTML = objectives.map((obj, idx) => `
                <li class="objective-item ${obj.completed ? 'completed' : 'pending'}">
                    <span class="obj-icon">${obj.completed ? checkSvg : pendingSvg}</span>
                    <span class="obj-text">${isSequential ? `<strong>Step ${idx + 1}:</strong> ` : ''}${escapeHtml(obj.text)}</span>
=======
        function renderObjectives() {
            const listEl = document.getElementById('simulation-objectives-list');
            if (!listEl) return;
            listEl.innerHTML = objectives.map(obj => `
                <li class="objective-item ${obj.completed ? 'completed' : 'pending'}">
                    <span class="obj-icon">${obj.completed ? '✓' : '○'}</span>
                    <span class="obj-text">${obj.text}</span>
>>>>>>> Stashed changes
                </li>
            `).join('');
        }

<<<<<<< Updated upstream
        function checkObjectives(submittedCommand, commandToRun, dirBeforeCommand) {
            if (!commandSucceeded) return;
            const cleanSubmitted = (submittedCommand || '').trim();
            const cleanRun = (commandToRun || '').trim();

            if (isSequential) {
                const nextIdx = objectives.findIndex(o => !o.completed);
                if (nextIdx === -1) return;

                const targetObj = objectives[nextIdx];
                const expectedCmd = (targetObj.command || '').trim();
                const matchCmd = (expectedCmd === cleanSubmitted || expectedCmd === cleanRun);
                const reqDir = targetObj.executionDirectory ? '/' + resolvePath(targetObj.executionDirectory).join('/') : '';
                const matchDir = !reqDir || dirBeforeCommand === reqDir;

                if (matchCmd && matchDir) {
                    targetObj.completed = true;
                    renderObjectives();
                    printLine(`<div class="term-line" style="color: #4ade80;"><strong><svg viewBox="0 0 24 24" width="14" height="14" fill="none" stroke="currentColor" stroke-width="2.5" style="vertical-align:-2px;margin-right:4px;"><polyline points="20 6 9 17 4 12"></polyline></svg>Completed Step #${nextIdx + 1}:</strong> ${escapeHtml(targetObj.text)}</div>`);

                    // Introduce hint for next available objective
                    const nextIncompleteIdx = objectives.findIndex(o => !o.completed);
                    if (nextIncompleteIdx !== -1) {
                        if (objectives[nextIncompleteIdx].hint) {
                            printHint(nextIncompleteIdx, objectives[nextIncompleteIdx].hint);
                        }
                    } else if (!isTaskCompleted) {
                        isTaskCompleted = true;
                        triggerSuccess();
                    }
                } else {
                    // Check if student attempted a later objective before completing the current one
                    const futureIdx = objectives.findIndex((o, i) => i > nextIdx && !o.completed && ((o.command || '').trim() === cleanSubmitted || (o.command || '').trim() === cleanRun));
                    if (futureIdx !== -1) {
                        printLine(`<div class="term-line" style="color: #fbbf24;"><strong>[!] Sequential Step Required:</strong> Complete Step #${nextIdx + 1} ("${escapeHtml(targetObj.text)}") before Step #${futureIdx + 1}.</div>`);
                    }
                }
            } else {
                // Non-sequential mode: any incomplete objective can be completed
                let newlyCompleted = false;
                objectives.forEach((obj, idx) => {
                    if (obj.completed) return;
                    const expectedCmd = (obj.command || '').trim();
                    const matchCmd = (expectedCmd === cleanSubmitted || expectedCmd === cleanRun);
                    const reqDir = obj.executionDirectory ? '/' + resolvePath(obj.executionDirectory).join('/') : '';
                    const matchDir = !reqDir || dirBeforeCommand === reqDir;
                    if (matchCmd && matchDir) {
                        obj.completed = true;
                        newlyCompleted = true;
                        printLine(`<div class="term-line" style="color: #4ade80;"><strong><svg viewBox="0 0 24 24" width="14" height="14" fill="none" stroke="currentColor" stroke-width="2.5" style="vertical-align:-2px;margin-right:4px;"><polyline points="20 6 9 17 4 12"></polyline></svg>Completed Objective:</strong> ${escapeHtml(obj.text)}</div>`);
                    }
                });

                if (newlyCompleted) {
                    renderObjectives();
                    if (!isTaskCompleted && objectives.every(o => o.completed)) {
                        isTaskCompleted = true;
                        triggerSuccess();
                    }
                }
=======
        function checkObjectives(cmd, args, output) {
            const fullCmd = `${cmd} ${args.join(' ')}`.trim();
            const currDirStr = getPathString();

            objectives.forEach(obj => {
                if (obj.completed) return;

                if (obj.type === 'cwd' && currDirStr.includes(obj.target)) {
                    obj.completed = true;
                } else if (obj.type === 'cat' && cmd === 'cat' && args.some(a => a.includes(obj.target))) {
                    obj.completed = true;
                } else if (obj.type === 'chmod' && cmd === 'chmod' && args.some(a => a.includes(obj.target))) {
                    obj.completed = true;
                } else if (obj.type === 'command' && cmd === obj.target) {
                    obj.completed = true;
                } else if (cmd === 'cd') {
                    if (obj.id === 'explore') obj.completed = true;
                } else if (cmd === 'cat') {
                    if (obj.id === 'find_flag' && (output.includes('flag{') || output.includes('FLAG_'))) {
                        obj.completed = true;
                    }
                }
            });

            renderObjectives();

            // Check if all objectives are completed
            if (!isTaskCompleted && objectives.every(o => o.completed)) {
                isTaskCompleted = true;
                triggerSuccess();
>>>>>>> Stashed changes
            }
        }

        function triggerSuccess() {
<<<<<<< Updated upstream
            printOutput('Lab objectives met. Waiting for server verification...');
            if (typeof onCompleteCallback === 'function') {
                Promise.resolve(onCompleteCallback([...commandHistory])).catch(error => {
                    isTaskCompleted = false;
                    printOutput(error.message || 'Progress could not be saved. Run a command to try again.', 'term-error');
                });
            }
        }

        // Output Redirection File Writer
        function writeOutputFile(targetPath, text) {
            const parts = resolvePath(targetPath);
            const name = parts.pop();
            if (!name) {
                printLine(`hackacad-sh: ${escapeHtml(targetPath)}: Invalid path`, 'term-error');
                return false;
            }
            const parentDir = getDirectoryNode(parts);
            if (!parentDir || typeof parentDir !== 'object') {
                printLine(`hackacad-sh: ${escapeHtml(targetPath)}: No such file or directory`, 'term-error');
                return false;
            }
            if (Object.hasOwn(parentDir, name) && typeof parentDir[name] === 'object') {
                printLine(`hackacad-sh: ${escapeHtml(targetPath)}: Is a directory`, 'term-error');
                return false;
            }
            parentDir[name] = text;
            return true;
        }

        // Simulated Nano Editor UI
        function openNanoEditor(filepath) {
            const parts = resolvePath(filepath);
            const filename = parts.pop();
            if (!filename) {
                printLine(`nano: ${escapeHtml(filepath)}: Invalid path`, 'term-error');
                return;
            }
            const parentDir = getDirectoryNode(parts);
            if (!parentDir || typeof parentDir !== 'object') {
                printLine(`nano: ${escapeHtml(filepath)}: No such file or directory`, 'term-error');
                return;
            }
            if (Object.hasOwn(parentDir, filename) && typeof parentDir[filename] === 'object') {
                printLine(`nano: ${escapeHtml(filepath)}: Is a directory`, 'term-error');
                return;
            }
            const isExisting = Object.hasOwn(parentDir, filename) && typeof parentDir[filename] === 'string';
            const initialContent = isExisting ? parentDir[filename] : "";

            const inputRow = container.querySelector('.terminal-input-row');
            outputEl.style.display = 'none';
            inputRow.style.display = 'none';

            const nanoDiv = document.createElement('div');
            nanoDiv.className = 'nano-wrapper';
            nanoDiv.innerHTML = `
                <div class="nano-titlebar">
                    <span>GNU nano 7.2 (Simulated)</span>
                    <span class="font-monospace fw-bold">File: ${escapeHtml(filename)}</span>
                    <span>${isExisting ? '' : '[New File]'}</span>
                </div>
                <textarea class="nano-editor-area" id="nano-textarea" spellcheck="false">${escapeHtml(initialContent)}</textarea>
                <div class="nano-statusbar">
                    <div>
                        <span class="badge bg-secondary me-2">^O / [Save] WriteOut</span>
                        <span class="badge bg-secondary">^X / [Exit] Close</span>
                    </div>
                    <div class="nano-actions">
                        <button type="button" class="btn btn-sm btn-success py-0 px-2" id="nano-save-btn">Save (^O)</button>
                        <button type="button" class="btn btn-sm btn-outline-light py-0 px-2" id="nano-cancel-btn">Exit (^X)</button>
                    </div>
                </div>
            `;

            const termWrapper = container.querySelector('.terminal-wrapper');
            termWrapper.appendChild(nanoDiv);

            const textarea = nanoDiv.querySelector('#nano-textarea');
            textarea.focus();

            function closeNano(saved, content) {
                nanoDiv.remove();
                outputEl.style.display = '';
                inputRow.style.display = '';
                inputEl.focus();

                if (saved) {
                    parentDir[filename] = content;
                    const lineCount = content ? content.split('\n').length : 0;
                    printLine(`[ Wrote ${lineCount} line(s) to ${escapeHtml(filename)} ]`);
                    checkObjectives(`nano ${filepath}`, `nano ${filepath}`, getPathString());
                } else {
                    printLine(`[ nano: closed without saving ]`);
                }
            }

            nanoDiv.querySelector('#nano-save-btn').addEventListener('click', () => closeNano(true, textarea.value));
            nanoDiv.querySelector('#nano-cancel-btn').addEventListener('click', () => closeNano(false, null));

            textarea.addEventListener('keydown', (e) => {
                if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === 'o') {
                    e.preventDefault();
                    closeNano(true, textarea.value);
                } else if (((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === 'x') || e.key === 'Escape') {
                    e.preventDefault();
                    closeNano(false, null);
                }
            });
        }

        // Validate custom command required files and directories
        function checkCustomCommandPrerequisites(cmdLine, requiredFiles) {
            if (!requiredFiles || !Array.isArray(requiredFiles) || requiredFiles.length === 0) {
                return { satisfied: true };
            }

            const cmdFirstWord = (cmdLine || '').trim().split(/\s+/)[0] || 'hackacad-sh';
            const isPythonCmd = cmdFirstWord === 'python' || cmdFirstWord === 'python3' || cmdFirstWord.endsWith('/python') || cmdFirstWord.endsWith('/python3');

            for (const req of requiredFiles) {
                if (!req || typeof req !== 'string' || !req.trim()) continue;
                const cleanReq = req.trim();
                const requiresDir = cleanReq.endsWith('/');

                // Reject absolute path bypass or illegal characters
                // Strip leading slashes to force relative resolution against cwd
                const rawClean = cleanReq.replace(/^\/+/, '');
                const rawParts = rawClean.split('/').filter(Boolean);

                // Check for traversal escaping above currentPath
                let depth = 0;
                let escapesCwd = false;
                for (const seg of rawParts) {
                    if (seg === '.') continue;
                    if (seg === '..') {
                        depth--;
                        if (depth < 0) {
                            escapesCwd = true;
                            break;
                        }
                    } else {
                        depth++;
                    }
                }

                if (escapesCwd || cleanReq.includes('\\')) {
                    return {
                        satisfied: false,
                        error: `${cmdFirstWord}: ${cleanReq}: Invalid path`
                    };
                }

                // Resolve target path strictly against currentPath
                const targetParts = [...currentPath];
                for (const seg of rawParts) {
                    if (seg === '.') continue;
                    if (seg === '..') {
                        targetParts.pop();
                    } else {
                        targetParts.push(seg);
                    }
                }

                // Walk virtualFS to check existence and node type
                let curr = virtualFS;
                let missing = false;

                for (let i = 0; i < targetParts.length; i++) {
                    const part = targetParts[i];
                    if (!curr || typeof curr !== 'object' || !Object.hasOwn(curr, part)) {
                        missing = true;
                        break;
                    }
                    curr = curr[part];
                }

                if (missing || curr === null || curr === undefined) {
                    if (requiresDir) {
                        return {
                            satisfied: false,
                            error: `${cmdFirstWord}: ${cleanReq}: No such directory`
                        };
                    } else if (isPythonCmd) {
                        return {
                            satisfied: false,
                            error: `${cmdFirstWord}: can't open file '${cleanReq}': [Errno 2] No such file or directory`
                        };
                    } else {
                        return {
                            satisfied: false,
                            error: `${cmdFirstWord}: ${cleanReq}: No such file or directory`
                        };
                    }
                }

                const isDir = typeof curr === 'object';
                const isFile = typeof curr === 'string';

                if (requiresDir && !isDir) {
                    return {
                        satisfied: false,
                        error: `${cmdFirstWord}: ${cleanReq}: Not a directory`
                    };
                }

                if (!requiresDir && isDir && (cleanReq.includes('.') || !cleanReq.endsWith('/'))) {
                    // Item exists, but it's a directory when a file was expected
                    if (isPythonCmd) {
                        return {
                            satisfied: false,
                            error: `${cmdFirstWord}: can't open file '${cleanReq}': [Errno 21] Is a directory`
                        };
                    } else {
                        return {
                            satisfied: false,
                            error: `${cmdFirstWord}: ${cleanReq}: Is a directory`
                        };
                    }
                }
            }

            return { satisfied: true };
        }

=======
            printLine('&nbsp;');
            printLine(`<div class="term-success-box">
  ★ ==================================================== ★<br>
  [+] MISSION OBJECTIVES COMPLETE!<br>
  [+] Great job, operator. Your solution has been verified.<br>
  ★ ==================================================== ★
</div>`, 'term-success');

            const bannerEl = document.getElementById('simulation-success-banner');
            if (bannerEl) {
                bannerEl.style.display = 'block';
            }

            if (typeof onCompleteCallback === 'function') {
                onCompleteCallback();
            }
        }

>>>>>>> Stashed changes
        // Handle Command Execution
        function executeCommand(rawInput) {
            const trimmed = rawInput.trim();
            if (!trimmed) {
<<<<<<< Updated upstream
                printLine(`<span class="term-prompt">${escapeHtml(promptEl.textContent)}</span>`);
                return;
            }

            commandSucceeded = true;
            commandHistory.push(trimmed);
            historyIndex = commandHistory.length;

            const dirBeforeCommand = getPathString();
            printLine(`<span class="term-prompt">${escapeHtml(promptEl.textContent)}</span> <span class="term-cmd">${escapeHtml(trimmed)}</span>`);

            // Output Redirection Detection & Parsing
            let redirectDest = null;
            let commandToRun = trimmed;

            if (trimmed.includes('>')) {
                const firstIdx = trimmed.indexOf('>');
                const secondIdx = trimmed.indexOf('>', firstIdx + 1);
                if (secondIdx !== -1) {
                    printLine('hackacad-sh: syntax error: multiple redirection not supported', 'term-error');
                    return;
                }
                if (firstIdx === 0) {
                    printLine("hackacad-sh: syntax error near unexpected token '>'", 'term-error');
                    return;
                }
                const leftPart = trimmed.substring(0, firstIdx).trim();
                const rightPart = trimmed.substring(firstIdx + 1).trim();
                if (!leftPart) {
                    printLine("hackacad-sh: syntax error near unexpected token '>'", 'term-error');
                    return;
                }
                if (!rightPart) {
                    printLine("hackacad-sh: syntax error near unexpected token 'newline'", 'term-error');
                    return;
                }
                const destTokens = rightPart.split(/\s+/).filter(Boolean);
                if (destTokens.length > 1) {
                    printLine("hackacad-sh: syntax error: ambiguous redirect", 'term-error');
                    return;
                }
                commandToRun = leftPart;
                redirectDest = destTokens[0];
            }

            // 1. Check Exact-Match Custom Commands (case-sensitive)
            const matchedCustom = customCommands.find(c => c && c.enabled !== false && c.command === commandToRun);
            if (matchedCustom) {
                // Validate Required Files and Directories prerequisite
                const reqResult = checkCustomCommandPrerequisites(commandToRun, matchedCustom.requiredFiles);
                if (!reqResult.satisfied) {
                    printLine(escapeHtml(reqResult.error), 'term-error');
                    return;
                }

                if (redirectDest) {
                    const writeOk = writeOutputFile(redirectDest, matchedCustom.output);
                    if (writeOk) {
                        checkObjectives(trimmed, commandToRun, dirBeforeCommand);
                    }
                } else {
                    printOutput(matchedCustom.output);
                    checkObjectives(trimmed, commandToRun, dirBeforeCommand);
                }
                return;
            }

            // 2. Built-in Command Parsing
            const parts = commandToRun.split(/\s+/);
            const cmd = parts[0].toLowerCase();
            const args = parts.slice(1);

            // Obsolete networking utilities rejected with command not found
            if (obsoleteBuiltins.includes(cmd)) {
                printLine(`hackacad-sh: ${escapeHtml(cmd)}: command not found`, 'term-error');
                return;
            }

            if (!allBuiltins.includes(cmd)) {
                printLine(`hackacad-sh: ${escapeHtml(cmd)}: command not found`, 'term-error');
                return;
            }

            // Check if command is permitted by instructor in this exercise
            if (!allowedCommands.includes(cmd)) {
                printLine(`hackacad-sh: <strong>${escapeHtml(cmd)}</strong>: command not allowed in this lab exercise.`, 'term-error');
=======
                printLine(`<span class="term-prompt">${promptEl.textContent}</span>`);
                return;
            }

            commandHistory.push(trimmed);
            historyIndex = commandHistory.length;

            printLine(`<span class="term-prompt">${promptEl.textContent}</span> <span class="term-cmd">${trimmed}</span>`);

            const parts = trimmed.split(/\s+/);
            const cmd = parts[0].toLowerCase();
            const args = parts.slice(1);

            // Check if command is permitted by teacher in this exercise
            if (!allowedCommands.includes(cmd)) {
                printLine(`hackacad-sh: <strong>${cmd}</strong>: command not allowed in this lab exercise.`, 'term-error');
>>>>>>> Stashed changes
                printLine(`Type <span class="term-highlight">help</span> for allowed commands.`, 'text-secondary');
                return;
            }

<<<<<<< Updated upstream
            let capturedOutput = '';
            let captureLines = [];

            function emitOutput(text) {
                capturedOutput = text;
                if (!redirectDest) {
                    printOutput(text);
                }
            }

            function emitLine(html) {
                if (redirectDest) {
                    const plain = decodeHtmlEntities(html.replace(/<[^>]*>/g, ''));
                    captureLines.push(plain);
                } else {
                    printLine(html);
                }
            }

            switch (cmd) {
                case 'help':
                    emitLine('<strong>Available Lab Commands:</strong>');
=======
            let outputResult = '';

            switch (cmd) {
                case 'help':
                    printLine('<strong>Available Lab Commands:</strong>');
>>>>>>> Stashed changes
                    allowedCommands.forEach(c => {
                        let desc = '';
                        switch (c) {
                            case 'help': desc = 'Show this allowed command list'; break;
                            case 'clear': desc = 'Clear the terminal screen'; break;
                            case 'pwd': desc = 'Print current working directory'; break;
                            case 'ls': desc = 'List files and subdirectories'; break;
                            case 'cd': desc = 'Change working directory'; break;
                            case 'cat': desc = 'Display contents of a file'; break;
<<<<<<< Updated upstream
                            case 'touch': desc = 'Create an empty file or update timestamp'; break;
                            case 'nano': desc = 'Edit file in simulated terminal editor'; break;
=======
>>>>>>> Stashed changes
                            case 'grep': desc = 'Search for patterns in file'; break;
                            case 'chmod': desc = 'Change file access permissions'; break;
                            case 'whoami': desc = 'Display current user identity'; break;
                            case 'id': desc = 'Display user and group IDs'; break;
                            case 'echo': desc = 'Print text to terminal'; break;
<<<<<<< Updated upstream
=======
                            case 'base64': desc = 'Base64 encode or decode (-d)'; break;
                            case 'ping': desc = 'Simulate ICMP ping to a remote host'; break;
                            case 'nmap': desc = 'Network exploration and port scan'; break;
                            case 'curl': desc = 'Simulate HTTP request to a URL'; break;
>>>>>>> Stashed changes
                            case 'history': desc = 'Display command history'; break;
                            case 'date': desc = 'Display current system time'; break;
                            default: desc = 'Lab utility'; break;
                        }
<<<<<<< Updated upstream
                        emitLine(`  <span class="term-highlight">${escapeHtml(c.padEnd(10))}</span> - ${desc}`);
                    });
                    if (customCommands.length > 0) {
                        emitLine('<strong>Custom Scenario Commands:</strong>');
                        customCommands.forEach(cc => {
                            emitLine(`  <span class="term-highlight">${escapeHtml(cc.command.padEnd(20))}</span> - ${escapeHtml(cc.description || 'Scenario command')}`);
                        });
                    }
=======
                        printLine(`  <span class="term-highlight">${c.padEnd(10)}</span> - ${desc}`);
                    });
>>>>>>> Stashed changes
                    break;

                case 'clear':
                    outputEl.innerHTML = '';
                    break;

                case 'pwd':
<<<<<<< Updated upstream
                    emitOutput(getPathString());
                    break;

                case 'whoami':
                    emitOutput('student');
                    break;

                case 'id':
                    emitOutput('uid=1000(student) gid=1000(student) groups=1000(student),4(adm),27(sudo)');
                    break;

                case 'date':
                    emitOutput(new Date().toUTCString());
=======
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
>>>>>>> Stashed changes
                    break;

                case 'history':
                    commandHistory.forEach((h, idx) => {
<<<<<<< Updated upstream
                        emitLine(`  ${(idx + 1).toString().padStart(3, ' ')}  ${escapeHtml(h)}`);
                    });
                    break;

                case 'touch': {
                    if (args.length === 0) {
                        printLine('touch: missing file operand', 'term-error');
                        break;
                    }
                    for (const target of args) {
                        const tParts = resolvePath(target);
                        const tName = tParts.pop();
                        if (!tName) {
                            printLine(`touch: cannot touch '${escapeHtml(target)}': Invalid path`, 'term-error');
                            break;
                        }
                        const pDir = getDirectoryNode(tParts);
                        if (!pDir || typeof pDir !== 'object') {
                            printLine(`touch: cannot touch '${escapeHtml(target)}': No such file or directory`, 'term-error');
                            break;
                        }
                        if (Object.hasOwn(pDir, tName) && typeof pDir[tName] === 'object') {
                            printLine(`touch: cannot touch '${escapeHtml(target)}': Is a directory`, 'term-error');
                            break;
                        }
                        if (!Object.hasOwn(pDir, tName)) {
                            pDir[tName] = "";
                        }
                    }
                    break;
                }

                case 'nano': {
                    if (args.length === 0) {
                        printLine('nano: missing file operand', 'term-error');
                        break;
                    }
                    openNanoEditor(args[0]);
                    return; // Nano handles its own prompt restore
                }

=======
                        printLine(`  ${(idx + 1).toString().padStart(3, ' ')}  ${h}`);
                    });
                    break;

>>>>>>> Stashed changes
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
<<<<<<< Updated upstream
                        emitLine(`total ${entries.length * 4}`);
                        if (showAll) {
                            emitLine(`drwxr-xr-x 2 student student 4096 Oct 18 10:00 .`);
                            emitLine(`drwxr-xr-x 4 root    root    4096 Oct 18 10:00 ..`);
                        }
                        entries.forEach(name => {
                            const isDir = typeof targetDir[name] === 'object';
                            const perm = isDir ? 'drwxr-xr-x' : (filePermissions[[...currentPath, name].join('/')] ? `-rwxr-xr-x` : `-rw-r--r--`);
                            const size = isDir ? 4096 : (targetDir[name] ? targetDir[name].length : 0);
                            const coloredName = isDir
                                ? `<span class="term-dir">${escapeHtml(name)}/</span>`
                                : (filePermissions[[...currentPath, name].join('/')] ? `<span class="term-exec">${escapeHtml(name)}*</span>` : escapeHtml(name));
                            emitLine(`${perm} 1 student student ${size.toString().padStart(5, ' ')} Oct 18 10:00 ${coloredName}`);
=======
                        printLine(`total ${entries.length * 4}`);
                        if (showAll) {
                            printLine(`drwxr-xr-x 2 student student 4096 Oct 18 10:00 .`);
                            printLine(`drwxr-xr-x 4 root    root    4096 Oct 18 10:00 ..`);
                        }
                        entries.forEach(name => {
                            const isDir = typeof targetDir[name] === 'object';
                            const perm = isDir ? 'drwxr-xr-x' : (filePermissions[name] ? `-rwxr-xr-x` : `-rw-r--r--`);
                            const size = isDir ? 4096 : (targetDir[name] ? targetDir[name].length : 0);
                            const coloredName = isDir
                                ? `<span class="term-dir">${name}/</span>`
                                : (filePermissions[name] ? `<span class="term-exec">${name}*</span>` : name);
                            printLine(`${perm} 1 student student ${size.toString().padStart(5, ' ')} Oct 18 10:00 ${coloredName}`);
>>>>>>> Stashed changes
                        });
                    } else {
                        const formatted = entries.map(name => {
                            const isDir = typeof targetDir[name] === 'object';
                            return isDir
<<<<<<< Updated upstream
                                ? `<span class="term-dir">${escapeHtml(name)}/</span>`
                                : (filePermissions[[...currentPath, name].join('/')] ? `<span class="term-exec">${escapeHtml(name)}*</span>` : escapeHtml(name));
                        });
                        emitLine(formatted.join('&nbsp;&nbsp;&nbsp;&nbsp;'));
=======
                                ? `<span class="term-dir">${name}/</span>`
                                : (filePermissions[name] ? `<span class="term-exec">${name}*</span>` : name);
                        });
                        printLine(formatted.join('&nbsp;&nbsp;&nbsp;&nbsp;'));
>>>>>>> Stashed changes
                    }
                    break;
                }

                case 'cd': {
                    const dest = args[0] || '/home/student';
<<<<<<< Updated upstream
                    const resolved = resolvePath(dest);
                    const directory = getDirectoryNode(resolved);
                    if (directory && typeof directory === 'object') {
                        currentPath = resolved;
                        updatePrompt();
                    } else {
                        printOutput(`cd: ${dest}: No such directory`, 'term-error');
=======
                    if (dest === '~' || dest === '/home/student') {
                        currentPath = ["home", "student"];
                        updatePrompt();
                    } else if (dest === '/') {
                        currentPath = [];
                        updatePrompt();
                    } else if (dest === '..') {
                        if (currentPath.length > 0) currentPath.pop();
                        updatePrompt();
                    } else {
                        const parts = dest.startsWith('/') ? dest.split('/').filter(Boolean) : [...currentPath, ...dest.split('/').filter(Boolean)];
                        const resolved = [];
                        for (let p of parts) {
                            if (p === '.') continue;
                            if (p === '..') {
                                if (resolved.length > 0) resolved.pop();
                            } else {
                                resolved.push(p);
                            }
                        }

                        const target = getDirectoryNode(resolved);
                        if (target !== null && typeof target === 'object') {
                            currentPath = resolved;
                            updatePrompt();
                        } else {
                            printLine(`cd: ${dest}: No such directory`, 'term-error');
                        }
>>>>>>> Stashed changes
                    }
                    break;
                }

                case 'cat': {
<<<<<<< Updated upstream
                    const content = args[0] ? readFile(args[0]) : null;
                    if (content === null) {
                        printOutput('cat: choose an existing file', 'term-error');
                    } else {
                        emitOutput(content);
=======
                    if (args.length === 0) {
                        printLine('cat: missing file operand', 'term-error');
                        break;
                    }
                    const fileName = args[0];
                    const currNode = getDirectoryNode(currentPath);

                    if (currNode && typeof currNode === 'object' && currNode[fileName] !== undefined) {
                        if (typeof currNode[fileName] === 'object') {
                            printLine(`cat: ${fileName}: Is a directory`, 'term-error');
                        } else {
                            outputResult = currNode[fileName];
                            printOutput(outputResult);
                        }
                    } else {
                        // Check if absolute path like /var/secret/audit.sh
                        const absParts = fileName.replace(/^\/+|\/+$/g, '').split('/');
                        const file = absParts.pop();
                        const dir = getDirectoryNode(absParts);
                        if (dir && typeof dir === 'object' && dir[file] !== undefined && typeof dir[file] !== 'object') {
                            outputResult = dir[file];
                            printOutput(outputResult);
                        } else {
                            printLine(`cat: ${fileName}: No such file or directory`, 'term-error');
                        }
>>>>>>> Stashed changes
                    }
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
<<<<<<< Updated upstream
                                const literalPattern = escapeHtml(pattern).replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
                                const highlighted = escapeHtml(m).replace(new RegExp(literalPattern, 'gi'), '<span class="term-match">$&</span>');
                                emitLine(highlighted);
                            });
                            capturedOutput = matches.join('\n');
                        }
                    } else {
                        printLine(`grep: ${escapeHtml(fileName)}: No such file or directory`, 'term-error');
=======
                                const highlighted = m.replace(new RegExp(pattern, 'gi'), `<span class="term-match">$&</span>`);
                                printLine(highlighted);
                            });
                            outputResult = matches.join('\n');
                        }
                    } else {
                        printLine(`grep: ${fileName}: No such file or directory`, 'term-error');
>>>>>>> Stashed changes
                    }
                    break;
                }

                case 'chmod': {
<<<<<<< Updated upstream
                    if (args.length < 2 || !/^[0-7]{3,4}$/.test(args[0]) || readFile(args[1]) === null) {
                        printOutput('chmod: use a valid mode and an existing file', 'term-error');
                        break;
                    }
                    filePermissions[resolvePath(args[1]).join('/')] = args[0];
                    emitOutput(`Changed permissions of '${args[1]}' to ${args[0]}`);
=======
                    if (args.length < 2) {
                        printLine('Usage: chmod [MODE] [FILE]', 'term-error');
                        break;
                    }
                    const mode = args[0];
                    const fileName = args[1];
                    filePermissions[fileName] = mode;
                    printLine(`Changed permissions of '${fileName}' to ${mode}`);
>>>>>>> Stashed changes
                    break;
                }

                case 'echo': {
                    const text = args.join(' ');
<<<<<<< Updated upstream
                    emitOutput(text);
=======
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
                    printLine(`PING ${host} (127.0.0.1) 56(84) bytes of data.`);
                    printLine(`64 bytes from ${host} (127.0.0.1): icmp_seq=1 ttl=64 time=0.035 ms`);
                    printLine(`64 bytes from ${host} (127.0.0.1): icmp_seq=2 ttl=64 time=0.041 ms`);
                    printLine(`64 bytes from ${host} (127.0.0.1): icmp_seq=3 ttl=64 time=0.038 ms`);
                    printLine(`--- ${host} ping statistics ---`);
                    printLine(`3 packets transmitted, 3 received, 0% packet loss, time 2004ms`);
                    break;
                }

                case 'nmap': {
                    const target = args[args.length - 1] || 'target.hackacad.local';
                    printLine(`Starting Nmap 7.94 ( https://nmap.org ) at 2026-10-18 10:00 UTC`);
                    printLine(`Nmap scan report for ${target} (10.10.10.50)`);
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
                    printLine(`&gt; GET ${url} HTTP/1.1`);
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
>>>>>>> Stashed changes
                    break;
                }

                default:
<<<<<<< Updated upstream
                    printLine(`hackacad-sh: ${escapeHtml(cmd)}: command not found`, 'term-error');
                    break;
            }

            // Write redirection output if requested and command succeeded
            if (redirectDest && commandSucceeded) {
                const textToWrite = captureLines.length > 0 ? captureLines.join('\n') : capturedOutput;
                const ok = writeOutputFile(redirectDest, textToWrite);
                if (!ok) commandSucceeded = false;
            }

            checkObjectives(trimmed, commandToRun, dirBeforeCommand);
=======
                    printLine(`hackacad-sh: ${cmd}: command not found`, 'term-error');
                    break;
            }

            checkObjectives(cmd, args, outputResult);
>>>>>>> Stashed changes
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
<<<<<<< Updated upstream
=======
                // Autocomplete
>>>>>>> Stashed changes
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
<<<<<<< Updated upstream

        // Print initial hint(s) once on startup
        introduceAvailableHints();

        // Expose container-level reset
        container.resetTerminal = function () {
            try { sessionStorage.removeItem(hintStorageKey); } catch (e) { }
            window.initSimulationTerminal(containerId, config, onCompleteCallback);
        };
    };

    window.resetSimulationTerminal = function (cId) {
        const el = document.getElementById(cId);
        if (el && typeof el.resetTerminal === 'function') {
            el.resetTerminal();
        }
    };

    window.resetSimulationAttempt = function () {
        try { sessionStorage.clear(); } catch (e) { }
=======
>>>>>>> Stashed changes
    };
})();
