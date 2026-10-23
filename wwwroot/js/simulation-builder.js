/**
 * HackAcad Visual Simulation Builder
 * Form-based authoring interface for NetAcad-style terminal lab simulations.
 * Provides interactive virtual filesystem tree, command permissions manager,
 * visual objective builder, live isolated terminal preview, and validation.
 */
(function () {
    'use strict';

    // Supported commands strictly implemented by the simulation engine
    const ALL_SUPPORTED_COMMANDS = [
        "help", "clear", "pwd", "ls", "cd", "cat", "grep", "chmod",
        "whoami", "id", "echo", "base64", "ping", "nmap", "curl", "history", "date", "touch", "nano"
    ];

    // Mandatory built-in system commands permanently enabled in every lab
    const MANDATORY_SYSTEM_COMMANDS = [
        { id: "help", label: "help", desc: "Display allowed lab commands and hints" },
        { id: "clear", label: "clear", desc: "Clear terminal screen" }
    ];

    // Teacher-configurable command categories (help and clear are permanently enabled)
    const COMMAND_CATEGORIES = [
        {
            name: "Navigation & Shell Environment",
            commands: [
                { id: "pwd", label: "pwd", desc: "Print current working directory" },
                { id: "cd", label: "cd", desc: "Change working directory" },
                { id: "history", label: "history", desc: "List executed command history" }
            ]
        },
        {
            name: "File Operations & Permissions",
            commands: [
                { id: "ls", label: "ls", desc: "List directory contents (-l, -la)" },
                { id: "cat", label: "cat", desc: "Display file contents" },
                { id: "touch", label: "touch", desc: "Create empty file or update timestamp" },
                { id: "nano", label: "nano", desc: "Edit file in simulated terminal editor" },
                { id: "grep", label: "grep", desc: "Search pattern inside file" },
                { id: "chmod", label: "chmod", desc: "Change file access permissions" }
            ]
        },
        {
            name: "User & System Information",
            commands: [
                { id: "whoami", label: "whoami", desc: "Print current username (student)" },
                { id: "id", label: "id", desc: "Print user and group IDs" },
                { id: "date", label: "date", desc: "Display system date and time" },
                { id: "echo", label: "echo", desc: "Print text line to terminal" }
            ]
        }
    ];

    // Predefined Templates
    const TEMPLATES = {
        linux_standard: {
            name: "Linux Filesystem Template (Recommended)",
            description: "Standard Linux directories (/home/student, /etc, /tmp, /var, /var/log) with safe example files.",
            allowedCommands: ["help", "clear", "pwd", "ls", "cd", "cat", "grep", "whoami", "chmod", "touch", "nano"],
            initialDirectory: "/home/student",
            fileSystem: {
                "home": {
                    "student": {
                        "notes.txt": "Mission: Inspect the system log files in /var/log to discover the security audit token.",
                        "welcome.txt": "Welcome to the HackAcad Linux environment.\nUse 'ls' to view files and 'cat' to read their contents."
                    }
                },
                "etc": {
                    "hostname": "hackacad-sandbox",
                    "motd": "HackAcad Virtual Security Lab v2.0\nAuthorized access only."
                },
                "tmp": {
                    "sample.tmp": "Temporary system session data."
                },
                "var": {
                    "log": {
                        "syslog": "systemd[1]: Started HackAcad Lab Environment.\nsystemd[1]: Reached target Multi-User System.",
                        "auth.log": "Oct 18 10:00:01 server sshd[1234]: Accepted session for student\nOct 18 10:08:14 server audit[1400]: FLAG_TOKEN=flag{l1nux_sh3ll_m4st3ry}"
                    }
                }
            },
            customCommands: [],
            objectives: [
                { id: "obj_nav_log", text: "Navigate to the /var/log directory using cd", type: "cwd", target: "/var/log", hint: "Run 'cd /var/log'" },
                { id: "obj_read_auth", text: "Read auth.log using cat to find the secret token", type: "cat", target: "auth.log", hint: "Run 'cat auth.log'" }
            ]
        },
        scratch: {
            name: "Start from Scratch (Empty Filesystem)",
            description: "Minimal valid filesystem structure starting at / with an empty /home/student directory.",
            allowedCommands: ["help", "clear", "pwd", "ls", "cd", "cat", "whoami"],
            initialDirectory: "/home/student",
            fileSystem: {
                "home": {
                    "student": {}
                }
            },
            customCommands: [],
            objectives: [
                { id: "obj_verify_pwd", text: "Check your initial location using pwd", type: "command", target: "pwd", hint: "Type 'pwd' and press Enter" }
            ]
        }
    };

    window.initSimulationBuilder = function (options) {
        const rootContainer = document.getElementById(options.containerId);
        if (!rootContainer) return;

        const hiddenJsonInput = document.getElementById(options.hiddenJsonInputId);
        const formEl = document.getElementById(options.formId);

        // Core State
        let state = {
            title: options.initialTitle || "",
            description: options.initialDescription || "",
            difficulty: options.initialDifficulty || "Beginner",
            instructions: options.initialInstructions || "",
            rewardFlag: options.initialRewardFlag || "",
            isPublished: options.initialIsPublished ?? true,
            fileSystem: {},
            allowedCommands: ["help", "clear", "pwd", "ls", "cd", "cat", "whoami"],
            initialDirectory: "/home/student",
            customCommands: [],
            objectives: [],
            selectedPath: "/", // currently selected item in filesystem
            expandedPaths: new Set(["/", "/home", "/home/student", "/var"]),
            activeTab: "editor" // "editor" | "preview"
        };

        // Load initial JSON configuration if provided
        try {
            if (hiddenJsonInput && hiddenJsonInput.value.trim()) {
                const parsed = JSON.parse(hiddenJsonInput.value.trim());
                if (parsed.fileSystem && typeof parsed.fileSystem === 'object') {
                    state.fileSystem = JSON.parse(JSON.stringify(parsed.fileSystem));
                }
                if (Array.isArray(parsed.allowedCommands)) {
                    state.allowedCommands = parsed.allowedCommands.map(c => c.toLowerCase().trim());
                }
                if (parsed.initialDirectory && typeof parsed.initialDirectory === 'string') {
                    state.initialDirectory = normalizePath(parsed.initialDirectory);
                }
                if (Array.isArray(parsed.customCommands)) {
                    state.customCommands = JSON.parse(JSON.stringify(parsed.customCommands));
                }
                if (Array.isArray(parsed.objectives)) {
                    state.objectives = JSON.parse(JSON.stringify(parsed.objectives));
                }
                if (parsed.description) state.description = parsed.description;
                if (parsed.difficulty) state.difficulty = parsed.difficulty;
            }
        } catch (e) {
            console.warn("Failed to parse initial simulation JSON; starting with standard template:", e);
        }

        // If filesystem is empty, apply default Linux template
        if (!state.fileSystem || Object.keys(state.fileSystem).length === 0) {
            applyTemplate("linux_standard", false);
        }

        // Ensure mandatory commands are always present
        ensureMandatoryCommands();

        // SVG icon helper
        function svgIcon(name, extraClass = '', alt = '') {
            const altAttr = alt ? `alt="${escapeHtml(alt)}"` : 'alt="" aria-hidden="true"';
            return `<img src="/images/${name}.svg" class="sim-icon ${extraClass}" ${altAttr} />`;
        }

        // Path utility functions
        function normalizePath(p) {
            if (!p || p === '/') return '/';
            const parts = p.split('/').filter(Boolean);
            return '/' + parts.join('/');
        }

        function getNameFromPath(p) {
            const parts = p.split('/').filter(Boolean);
            return parts.length > 0 ? parts[parts.length - 1] : '/';
        }

        function getParentPath(p) {
            const parts = p.split('/').filter(Boolean);
            if (parts.length <= 1) return '/';
            parts.pop();
            return '/' + parts.join('/');
        }

        function getNode(fs, path) {
            if (path === '/') return fs;
            const parts = path.split('/').filter(Boolean);
            let curr = fs;
            for (let part of parts) {
                if (!curr || typeof curr !== 'object' || !Object.hasOwn(curr, part)) return null;
                curr = curr[part];
            }
            return curr;
        }

        function getNodeType(fs, path) {
            if (path === '/') return 'dir';
            const node = getNode(fs, path);
            if (node === null || node === undefined) return null;
            return typeof node === 'object' ? 'dir' : 'file';
        }

        function getAllDirectories(fs, basePath = '') {
            const dirs = [];
            if (basePath === '') dirs.push('/');
            const currNode = basePath === '' ? fs : getNode(fs, basePath);
            if (!currNode || typeof currNode !== 'object') return dirs;

            for (const key of Object.keys(currNode)) {
                if (typeof currNode[key] === 'object') {
                    const fullPath = basePath === '' ? `/${key}` : `${basePath}/${key}`;
                    dirs.push(fullPath);
                    dirs.push(...getAllDirectories(fs, fullPath));
                }
            }
            return dirs;
        }

        function getAllFiles(fs, basePath = '') {
            const files = [];
            const currNode = basePath === '' ? fs : getNode(fs, basePath);
            if (!currNode || typeof currNode !== 'object') return files;

            for (const key of Object.keys(currNode)) {
                const fullPath = basePath === '' ? `/${key}` : `${basePath}/${key}`;
                if (typeof currNode[key] === 'string') {
                    files.push({ name: key, path: fullPath });
                } else if (typeof currNode[key] === 'object') {
                    files.push(...getAllFiles(fs, fullPath));
                }
            }
            return files;
        }

        function ensureMandatoryCommands() {
            if (!state.allowedCommands.includes("help")) state.allowedCommands.push("help");
            if (!state.allowedCommands.includes("clear")) state.allowedCommands.push("clear");
        }

        // Tree Operations
        function createDirectory(parentPath, dirName) {
            const safeName = dirName.trim();
            if (!safeName || safeName.includes('/') || safeName.includes('\\')) {
                return { success: false, error: "Directory name cannot be empty or contain slashes." };
            }
            const parent = getNode(state.fileSystem, parentPath);
            if (!parent || typeof parent !== 'object') {
                return { success: false, error: "Parent directory does not exist." };
            }
            if (Object.hasOwn(parent, safeName)) {
                return { success: false, error: `An item named "${safeName}" already exists in this folder.` };
            }
            parent[safeName] = {};
            const newPath = parentPath === '/' ? `/${safeName}` : `${parentPath}/${safeName}`;
            state.expandedPaths.add(parentPath);
            state.expandedPaths.add(newPath);
            state.selectedPath = newPath;
            syncStateToInput();
            render();
            return { success: true, path: newPath };
        }

        function createFile(parentPath, fileName, content = "") {
            const safeName = fileName.trim();
            if (!safeName || safeName.includes('/') || safeName.includes('\\')) {
                return { success: false, error: "File name cannot be empty or contain slashes." };
            }
            const parent = getNode(state.fileSystem, parentPath);
            if (!parent || typeof parent !== 'object') {
                return { success: false, error: "Parent directory does not exist." };
            }
            if (Object.hasOwn(parent, safeName)) {
                return { success: false, error: `An item named "${safeName}" already exists in this folder.` };
            }
            parent[safeName] = content;
            const newPath = parentPath === '/' ? `/${safeName}` : `${parentPath}/${safeName}`;
            state.expandedPaths.add(parentPath);
            state.selectedPath = newPath;
            syncStateToInput();
            render();
            return { success: true, path: newPath };
        }

        function renameItem(itemPath, newName) {
            const safeName = newName.trim();
            if (itemPath === '/') return { success: false, error: "Root directory cannot be renamed." };
            if (!safeName || safeName.includes('/') || safeName.includes('\\')) {
                return { success: false, error: "Name cannot be empty or contain slashes." };
            }
            const parentPath = getParentPath(itemPath);
            const oldName = getNameFromPath(itemPath);
            const parent = getNode(state.fileSystem, parentPath);
            if (!parent || typeof parent !== 'object') return { success: false, error: "Parent directory not found." };
            if (safeName === oldName) return { success: true };
            if (Object.hasOwn(parent, safeName)) {
                return { success: false, error: `An item named "${safeName}" already exists in this folder.` };
            }

            parent[safeName] = parent[oldName];
            delete parent[oldName];

            const newPath = parentPath === '/' ? `/${safeName}` : `${parentPath}/${safeName}`;

            if (state.initialDirectory === itemPath) state.initialDirectory = newPath;
            else if (state.initialDirectory.startsWith(itemPath + '/')) {
                state.initialDirectory = newPath + state.initialDirectory.substring(itemPath.length);
            }

            state.objectives.forEach(obj => {
                if (obj.target === itemPath || obj.target === oldName) {
                    obj.target = obj.target === itemPath ? newPath : safeName;
                }
            });

            const newExpanded = new Set();
            state.expandedPaths.forEach(p => {
                if (p === itemPath) newExpanded.add(newPath);
                else if (p.startsWith(itemPath + '/')) newExpanded.add(newPath + p.substring(itemPath.length));
                else newExpanded.add(p);
            });
            state.expandedPaths = newExpanded;

            state.selectedPath = newPath;
            syncStateToInput();
            render();
            return { success: true };
        }

        function deleteItem(itemPath) {
            if (itemPath === '/') return { success: false, error: "Root directory cannot be deleted." };
            const parentPath = getParentPath(itemPath);
            const name = getNameFromPath(itemPath);
            const parent = getNode(state.fileSystem, parentPath);
            if (!parent || typeof parent !== 'object') return { success: false, error: "Parent directory not found." };

            delete parent[name];

            if (state.initialDirectory === itemPath || state.initialDirectory.startsWith(itemPath + '/')) {
                state.initialDirectory = parentPath;
            }

            state.selectedPath = parentPath;
            syncStateToInput();
            render();
            return { success: true };
        }

        function moveItem(sourcePath, destDirPath) {
            if (sourcePath === '/') return { success: false, error: "Root directory cannot be moved." };
            if (destDirPath.startsWith(sourcePath + '/') || destDirPath === sourcePath) {
                return { success: false, error: "Cannot move a directory into itself or its subdirectories." };
            }
            const srcParentPath = getParentPath(sourcePath);
            const srcName = getNameFromPath(sourcePath);
            const srcParent = getNode(state.fileSystem, srcParentPath);
            const destNode = getNode(state.fileSystem, destDirPath);

            if (!srcParent || !destNode || typeof destNode !== 'object') {
                return { success: false, error: "Invalid move operation." };
            }
            if (Object.hasOwn(destNode, srcName)) {
                return { success: false, error: `An item named "${srcName}" already exists in the destination folder.` };
            }

            destNode[srcName] = srcParent[srcName];
            delete srcParent[srcName];

            const newPath = destDirPath === '/' ? `/${srcName}` : `${destDirPath}/${srcName}`;

            if (state.initialDirectory === sourcePath) state.initialDirectory = newPath;
            else if (state.initialDirectory.startsWith(sourcePath + '/')) {
                state.initialDirectory = newPath + state.initialDirectory.substring(sourcePath.length);
            }

            state.selectedPath = newPath;
            state.expandedPaths.add(destDirPath);
            syncStateToInput();
            render();
            return { success: true };
        }

        function updateFileContent(filePath, newContent) {
            const parentPath = getParentPath(filePath);
            const parent = getNode(state.fileSystem, parentPath);
            const name = getNameFromPath(filePath);
            if (parent && typeof parent === 'object' && Object.hasOwn(parent, name)) {
                parent[name] = newContent;
                syncStateToInput();
            }
        }

        function applyTemplate(key, promptConfirm = true) {
            if (promptConfirm && Object.keys(state.fileSystem).length > 0) {
                if (!confirm(`Apply the "${TEMPLATES[key]?.name || key}" template? Any unsaved changes in the filesystem will be replaced.`)) {
                    return;
                }
            }
            const t = TEMPLATES[key];
            if (!t) return;
            state.fileSystem = JSON.parse(JSON.stringify(t.fileSystem));
            state.allowedCommands = [...t.allowedCommands];
            state.initialDirectory = t.initialDirectory;
            state.customCommands = t.customCommands ? JSON.parse(JSON.stringify(t.customCommands)) : [];
            state.objectives = JSON.parse(JSON.stringify(t.objectives));
            ensureMandatoryCommands();
            state.selectedPath = t.initialDirectory && getNode(state.fileSystem, t.initialDirectory) ? t.initialDirectory : "/";
            state.expandedPaths = new Set(["/", "/home", "/home/student", "/var", "/var/log"]);
            syncStateToInput();
            render();
        }

        // Custom Command Management
        function addCustomCommand() {
            const nextIdx = state.customCommands.length + 1;
            state.customCommands.push({
                id: `cc_${Date.now().toString(36)}_${nextIdx}`,
                command: "",
                output: "",
                description: "",
                hint: "",
                enabled: true
            });
            syncStateToInput();
            render();
        }

        function removeCustomCommand(index) {
            state.customCommands.splice(index, 1);
            syncStateToInput();
            render();
        }

        function moveCustomCommand(index, direction) {
            const targetIdx = index + direction;
            if (targetIdx < 0 || targetIdx >= state.customCommands.length) return;
            const temp = state.customCommands[index];
            state.customCommands[index] = state.customCommands[targetIdx];
            state.customCommands[targetIdx] = temp;
            syncStateToInput();
            render();
        }

        function toggleCustomCommand(index) {
            if (!state.customCommands[index]) return;
            state.customCommands[index].enabled = !state.customCommands[index].enabled;
            syncStateToInput();
            render();
        }

        function updateCustomCommand(index, field, value) {
            if (!state.customCommands[index]) return;
            state.customCommands[index][field] = value;
            syncStateToInput();
        }

        // Objective Management
        function addObjective() {
            const nextIdx = state.objectives.length + 1;
            const dirs = getAllDirectories(state.fileSystem);
            const files = getAllFiles(state.fileSystem);
            const defaultTarget = dirs.find(d => d !== '/' && d !== '/home/student') || files[0]?.name || "/home/student";

            state.objectives.push({
                id: `obj_${Date.now().toString(36)}_${nextIdx}`,
                text: "Complete lab step",
                type: "cwd",
                target: defaultTarget,
                hint: ""
            });
            syncStateToInput();
            render();
        }

        function removeObjective(index) {
            state.objectives.splice(index, 1);
            syncStateToInput();
            render();
        }

        function moveObjective(index, direction) {
            const targetIdx = index + direction;
            if (targetIdx < 0 || targetIdx >= state.objectives.length) return;
            const temp = state.objectives[index];
            state.objectives[index] = state.objectives[targetIdx];
            state.objectives[targetIdx] = temp;
            syncStateToInput();
            render();
        }

        function updateObjective(index, field, value) {
            if (!state.objectives[index]) return;
            state.objectives[index][field] = value;
            syncStateToInput();
            render();
        }

        // Serialization & Sync
        function serializeConfiguration() {
            return {
                description: state.description,
                difficulty: state.difficulty,
                allowedCommands: state.allowedCommands,
                initialDirectory: state.initialDirectory,
                fileSystem: state.fileSystem,
                customCommands: state.customCommands,
                objectives: state.objectives
            };
        }

        function syncStateToInput() {
            if (!hiddenJsonInput) return;
            hiddenJsonInput.value = JSON.stringify(serializeConfiguration(), null, 2);
        }

        // Full Validation
        function validateForPublish() {
            const errors = [];
            const titleInput = document.getElementById("Title");
            const titleVal = titleInput ? titleInput.value.trim() : state.title.trim();
            if (!titleVal) errors.push("Lab title is required.");

            // Filesystem check
            const dirs = getAllDirectories(state.fileSystem);
            if (dirs.length === 0) errors.push("Filesystem must contain at least one directory.");

            // Initial directory check
            if (!state.initialDirectory || !dirs.includes(state.initialDirectory)) {
                errors.push(`Initial directory "${state.initialDirectory}" does not exist in the virtual filesystem.`);
            }

            // Allowed commands
            if (state.allowedCommands.length === 0) {
                errors.push("Select at least one allowed command.");
            }

            // Custom commands check
            const seenCustomCommands = new Set();
            const rootBuiltins = ALL_SUPPORTED_COMMANDS.map(c => c.toLowerCase());
            state.customCommands.forEach((cc, idx) => {
                const cmdStr = (cc.command || "").trim();
                if (cc.enabled) {
                    if (!cmdStr) {
                        errors.push(`Custom Command #${idx + 1}: Command string cannot be empty.`);
                    } else {
                        if (cmdStr.length > 512) {
                            errors.push(`Custom Command #${idx + 1}: Command string is too long (max 512 characters).`);
                        }
                        if (/[><|;&]/.test(cmdStr)) {
                            errors.push(`Custom Command #${idx + 1}: "${cmdStr}" cannot contain redirection or shell operators (>, <, |, ;, &).`);
                        }
                        if (seenCustomCommands.has(cmdStr)) {
                            errors.push(`Custom Command #${idx + 1}: Duplicate command string "${cmdStr}". Each enabled custom command must be unique.`);
                        } else {
                            seenCustomCommands.add(cmdStr);
                        }
                        if (rootBuiltins.includes(cmdStr.toLowerCase())) {
                            errors.push(`Custom Command #${idx + 1}: "${cmdStr}" conflicts with the built-in system command of the same name.`);
                        }
                    }
                }
            });

            // Objectives check
            if (state.objectives.length === 0) {
                errors.push("Define at least one student task objective.");
            }

            state.objectives.forEach((obj, idx) => {
                if (!obj.text || !obj.text.trim()) {
                    errors.push(`Objective #${idx + 1} is missing a description.`);
                }
                if (!obj.target || !obj.target.trim()) {
                    errors.push(`Objective #${idx + 1} is missing a target.`);
                } else {
                    if (obj.type === 'cwd') {
                        const targetNorm = normalizePath(obj.target);
                        if (!dirs.includes(targetNorm)) {
                            errors.push(`Objective #${idx + 1} target directory "${obj.target}" does not exist.`);
                        }
                    } else if (obj.type === 'cat' || obj.type === 'chmod') {
                        const allFiles = getAllFiles(state.fileSystem);
                        const fileExists = allFiles.some(f => f.name === obj.target || f.path === normalizePath(obj.target));
                        if (!fileExists) {
                            errors.push(`Objective #${idx + 1} target file "${obj.target}" does not exist.`);
                        }
                        if (obj.type === 'chmod' && (!obj.mode || !/^[0-7]{3,4}$/.test(obj.mode))) {
                            errors.push(`Objective #${idx + 1} requires a valid permission mode (e.g. 755).`);
                        }
                    } else if (obj.type === 'command') {
                        const isAllowed = state.allowedCommands.includes(obj.target.toLowerCase().trim())
                            || state.customCommands.some(cc => cc.enabled && cc.command === obj.target);
                        if (!isAllowed) {
                            errors.push(`Objective #${idx + 1} command "${obj.target}" is not in the allowed or custom commands list.`);
                        }
                    }
                }
            });

            return errors;
        }

        // Live Preview Runner
        function startLivePreview() {
            state.activeTab = "preview";
            render();

            const previewContainer = document.getElementById("sim-builder-preview-terminal");
            if (!previewContainer) return;

            const previewConfig = serializeConfiguration();
            previewContainer.innerHTML = '';

            if (typeof window.initSimulationTerminal === 'function') {
                window.initSimulationTerminal("sim-builder-preview-terminal", previewConfig, function (commands) {
                    const banner = document.getElementById("sim-builder-preview-success");
                    if (banner) banner.style.display = "block";
                    return Promise.resolve();
                });
            } else {
                previewContainer.innerHTML = '<div class="alert alert-warning">Simulation terminal engine (simulation-terminal.js) is not loaded.</div>';
            }
        }

        // UI RENDERER
        function render() {
            // Save scroll position of tree if exists
            const existingTree = document.getElementById("fs-tree-container");
            const treeScrollTop = existingTree ? existingTree.scrollTop : 0;

            const isPublishedChecked = state.isPublished ? 'checked' : '';
            const allDirs = getAllDirectories(state.fileSystem);
            const allFiles = getAllFiles(state.fileSystem);

            rootContainer.innerHTML = `
                <!-- Main Builder Tabs -->
                <div class="d-flex justify-content-between align-items-center mb-3 pb-2 border-bottom">
                    <ul class="nav nav-pills" id="builderModeTabs">
                        <li class="nav-item">
                            <button type="button" class="nav-link ${state.activeTab === 'editor' ? 'active' : ''}" id="tabBtnEditor">
                                <strong>${svgIcon('terminal', 'me-1')} Visual Simulation Builder</strong>
                            </button>
                        </li>
                        <li class="nav-item">
                            <button type="button" class="nav-link ${state.activeTab === 'preview' ? 'active' : ''}" id="tabBtnPreview">
                                <strong>${svgIcon('play', 'me-1')} Live Terminal Preview</strong>
                            </button>
                        </li>
                    </ul>
                    <div class="d-flex gap-2">
                        <button type="button" class="btn btn-sm btn-outline-secondary" id="toggleRawJsonBtn">
                            { } Advanced JSON
                        </button>
                    </div>
                </div>

                <!-- Raw JSON Drawer (collapsible) -->
                <div id="rawJsonDrawer" class="mb-3 p-3 builder-well rounded border" style="display:none;">
                    <div class="d-flex justify-content-between align-items-center mb-2">
                        <label class="form-label mb-0"><strong>Serialized Environment JSON</strong> (Auto-synced with visual editor)</label>
                        <button type="button" id="copyJsonBtn" class="btn btn-sm btn-outline-primary">Copy JSON</button>
                    </div>
                    <textarea id="rawJsonTextarea" class="form-control font-monospace" rows="8" spellcheck="false"></textarea>
                    <div class="d-flex justify-content-end mt-2">
                        <button type="button" id="applyRawJsonBtn" class="btn btn-sm btn-outline-secondary">Apply JSON Changes</button>
                    </div>
                </div>

                <!-- TAB 1: VISUAL BUILDER -->
                <div id="builderTabContent" style="${state.activeTab === 'editor' ? '' : 'display:none;'}">

                    <!-- Section A: Template Starter Bar -->
                    <div class="builder-well mb-4 p-3">
                        <div class="row g-2 align-items-center">
                            <div class="col-md-auto">
                                <strong class="d-flex align-items-center gap-1">
                                    ${svgIcon('folder', 'me-1')} Filesystem Starting Preset:
                                </strong>
                            </div>
                            <div class="col-md">
                                <select id="presetTemplateSelect" class="form-select form-select-sm">
                                    <option value="linux_standard">Option 1: Linux Filesystem Template (/home, /etc, /tmp, /var/log)</option>
                                    <option value="scratch">Option 2: Start from Scratch (Empty Filesystem)</option>
                                </select>
                            </div>
                            <div class="col-md-auto">
                                <button type="button" id="loadPresetBtn" class="btn btn-sm btn-outline-secondary">Load Preset</button>
                            </div>
                        </div>
                    </div>

                    <!-- Section 1: Simulation Details -->
                    <div class="builder-card mb-4">
                        <div class="builder-card-header">
                            <h5 class="mb-0">1. Simulation Details &amp; Objectives Description</h5>
                        </div>
                        <div class="card-body p-4">
                            <div class="row g-3">
                                <div class="col-md-8">
                                    <label class="form-label">Simulation Title <span class="text-danger">*</span></label>
                                    <input type="text" id="builderTitle" class="form-control" value="${escapeHtml(state.title)}" placeholder="e.g. Linux Command Line Exploration & Secret Audit" maxlength="150" required />
                                    <div class="form-text">A descriptive title shown in the course curriculum and student player.</div>
                                </div>
                                <div class="col-md-4">
                                    <label class="form-label">Difficulty Level</label>
                                    <select id="builderDifficulty" class="form-select">
                                        <option value="Beginner" ${state.difficulty === 'Beginner' ? 'selected' : ''}>Beginner</option>
                                        <option value="Intermediate" ${state.difficulty === 'Intermediate' ? 'selected' : ''}>Intermediate</option>
                                        <option value="Advanced" ${state.difficulty === 'Advanced' ? 'selected' : ''}>Advanced</option>
                                    </select>
                                    <div class="form-text">Indicates exercise complexity for students.</div>
                                </div>
                                <div class="col-md-12">
                                    <label class="form-label">Short Summary / Description</label>
                                    <input type="text" id="builderDescription" class="form-control" value="${escapeHtml(state.description)}" placeholder="Brief overview of what students will explore in this lab..." maxlength="2000" />
                                </div>
                                <div class="col-md-12">
                                    <label class="form-label">Student-Facing Instructions</label>
                                    <textarea id="builderInstructions" class="form-control" rows="3" placeholder="Step-by-step guidance displayed right above the simulated terminal shell...">${escapeHtml(state.instructions)}</textarea>
                                </div>
                            </div>
                        </div>
                    </div>

                    <!-- Section 2: Visual Filesystem Editor (Two-Panel Layout) -->
                    <div class="builder-card mb-4">
                        <div class="builder-card-header d-flex justify-content-between align-items-center flex-wrap gap-2">
                            <div>
                                <h5 class="mb-0">2. Visual Filesystem Editor</h5>
                                <small class="text-secondary">Click any item to inspect/edit. Drag and drop files/folders to move them between directories.</small>
                            </div>
                            <div class="d-flex gap-2">
                                <button type="button" class="btn btn-sm btn-outline-primary" id="newFolderAtRootBtn">
                                    ${svgIcon('plus', 'me-1')} New Folder at /
                                </button>
                                <button type="button" class="btn btn-sm btn-outline-primary" id="newFileAtRootBtn">
                                    ${svgIcon('plus', 'me-1')} New File at /
                                </button>
                                <button type="button" class="btn btn-sm btn-outline-secondary" id="expandAllBtn">Expand All</button>
                                <button type="button" class="btn btn-sm btn-outline-secondary" id="collapseAllBtn">Collapse All</button>
                            </div>
                        </div>
                        <div class="card-body p-0">
                            <div class="row g-0" style="min-height: 480px;">
                                <!-- Left Panel: Filesystem Tree -->
                                <div class="col-md-6 border-end p-3 builder-tree-panel" id="fs-tree-container" style="max-height: 540px; overflow-y: auto;">
                                    <div class="fs-tree">
                                        ${renderTreeNode(state.fileSystem, '/', 0)}
                                    </div>
                                </div>
                                <!-- Right Panel: Item Properties Editor -->
                                <div class="col-md-6 p-4 builder-inspector-panel" id="fs-inspector-container">
                                    ${renderInspector()}
                                </div>
                            </div>
                        </div>
                    </div>

                    <!-- Section 3: Terminal Configuration (Allowed Commands & Initial Directory) -->
                    <div class="builder-card mb-4">
                        <div class="builder-card-header">
                            <h5 class="mb-0">3. Terminal Configuration &amp; Command Permissions</h5>
                            <small class="text-secondary">Configure available commands and starting working directory.</small>
                        </div>
                        <div class="card-body p-4">
                            <!-- Initial Working Directory -->
                            <div class="mb-4 p-3 builder-well rounded border">
                                <label class="form-label"><strong>Initial Working Directory ($CWD)</strong> <span class="text-danger">*</span></label>
                                <div class="row g-2 align-items-center">
                                    <div class="col-md-6">
                                        <select id="initialDirectorySelect" class="form-select font-monospace">
                                            ${allDirs.map(d => `<option value="${d}" ${d === state.initialDirectory ? 'selected' : ''}>${d}</option>`).join('')}
                                        </select>
                                    </div>
                                    <div class="col-md-6 text-muted small">
                                        Directory the student starts in when the lab terminal opens.
                                    </div>
                                </div>
                            </div>

                            <!-- Mandatory Commands Informational Note -->
                            <div class="alert alert-secondary py-2 px-3 mb-3 d-flex align-items-center gap-2 small">
                                ${svgIcon('settings', 'sim-icon-lg text-primary')}
                                <div>
                                    <strong>Mandatory Built-in Commands:</strong> <code>help</code> and <code>clear</code> are standard system commands permanently available in every terminal exercise and do not require configuration.
                                </div>
                            </div>

                            <!-- Command Selection Checkboxes -->
                            <div class="d-flex justify-content-between align-items-center mb-3">
                                <label class="form-label mb-0"><strong>Predefined Shell Commands:</strong></label>
                                <div class="d-flex gap-2">
                                    <button type="button" class="btn btn-sm btn-link p-0 text-decoration-none" id="cmdSelectAllBtn">Select All</button>
                                    <span class="text-muted">|</span>
                                    <button type="button" class="btn btn-sm btn-link p-0 text-decoration-none" id="cmdSelectRecommendedBtn">Standard Commands</button>
                                    <span class="text-muted">|</span>
                                    <button type="button" class="btn btn-sm btn-link p-0 text-decoration-none" id="cmdClearAllBtn">Clear All</button>
                                </div>
                            </div>

                            <div class="row g-3">
                                ${COMMAND_CATEGORIES.map(cat => `
                                    <div class="col-md-6">
                                        <div class="p-3 border rounded builder-item-card h-100">
                                            <h6 class="text-primary mb-2">${cat.name}</h6>
                                            <div class="d-flex flex-column gap-2">
                                                ${cat.commands.map(cmd => {
                                                    const isChecked = state.allowedCommands.includes(cmd.id);
                                                    return `
                                                        <div class="form-check">
                                                            <input class="form-check-input cmd-checkbox" type="checkbox" id="cmd_${cmd.id}" value="${cmd.id}" ${isChecked ? 'checked' : ''} />
                                                            <label class="form-check-label d-flex justify-content-between" for="cmd_${cmd.id}">
                                                                <span class="font-monospace fw-bold">${cmd.label}</span>
                                                                <span class="text-muted small">${cmd.desc}</span>
                                                            </label>
                                                        </div>
                                                    `;
                                                }).join('')}
                                            </div>
                                        </div>
                                    </div>
                                `).join('')}
                            </div>
                        </div>
                    </div>

                    <!-- Section 4: Exact-Match Custom Commands -->
                    <div class="builder-card mb-4">
                        <div class="builder-card-header d-flex justify-content-between align-items-center flex-wrap gap-2">
                            <div>
                                <h5 class="mb-0">4. Exact-Match Custom Commands</h5>
                                <small class="text-secondary">Define exact command-response pairs for Cisco / CCNA labs or specialized cybersecurity exercises. Supported commands execute on exact match and can redirect output to virtual files.</small>
                            </div>
                            <button type="button" class="btn btn-sm btn-primary" id="addCustomCommandBtn">
                                ${svgIcon('plus', 'me-1')} Add Custom Command
                            </button>
                        </div>
                        <div class="card-body p-4">
                            <div id="customCommandsListContainer" class="d-flex flex-column gap-2">
                                ${state.customCommands.length === 0 ? `
                                    <div class="text-center py-4 text-muted border border-dashed rounded">
                                        No custom commands defined yet. Click "<strong>+ Add Custom Command</strong>" to configure NetAcad / CCNA-style exact-match commands (e.g. <code>show ip interface brief</code>).
                                    </div>
                                ` : state.customCommands.map((cc, idx) => renderCustomCommandCard(cc, idx)).join('')}
                            </div>
                        </div>
                    </div>

                    <!-- Section 5: Objective Builder -->
                    <div class="builder-card mb-4">
                        <div class="builder-card-header d-flex justify-content-between align-items-center flex-wrap gap-2">
                            <div>
                                <h5 class="mb-0">5. Lab Objectives</h5>
                                <small class="text-secondary">Define verification steps tracked automatically as students execute commands.</small>
                            </div>
                            <button type="button" class="btn btn-sm btn-primary" id="addObjectiveBtn">
                                ${svgIcon('plus', 'me-1')} Add Task Objective
                            </button>
                        </div>
                        <div class="card-body p-4">
                            <div id="objectivesListContainer" class="d-flex flex-column gap-3">
                                ${state.objectives.length === 0 ? `
                                    <div class="text-center py-4 text-muted border border-dashed rounded">
                                        No objectives defined yet. Click "<strong>+ Add Task Objective</strong>" to create one.
                                    </div>
                                ` : state.objectives.map((obj, idx) => renderObjectiveCard(obj, idx, allDirs, allFiles)).join('')}
                            </div>
                        </div>
                    </div>

                    <!-- Section 6: Reward & Publish Settings -->
                    <div class="builder-card mb-4">
                        <div class="builder-card-header">
                            <h5 class="mb-0">6. Completion Reward &amp; Publishing</h5>
                        </div>
                        <div class="card-body p-4">
                            <div class="mb-3">
                                <label class="form-label">Reward Flag (Revealed upon completing all objectives)</label>
                                <input type="text" id="builderRewardFlag" class="form-control font-monospace" value="${escapeHtml(state.rewardFlag)}" placeholder="flag{...}" />
                                <div class="form-text">Revealed to the student when all lab objectives are successfully finished.</div>
                            </div>

                            <div class="form-check form-switch p-3 builder-well rounded border">
                                <input class="form-check-input" type="checkbox" id="builderIsPublished" ${isPublishedChecked} style="margin-left: 0; margin-right: 0.75rem;" />
                                <label class="form-check-label fw-bold" for="builderIsPublished">
                                    Publish this lab exercise in the course curriculum
                                </label>
                                <div class="text-muted small mt-1">If unchecked, this simulation will be saved as a private Draft. Incomplete configurations can be saved as drafts.</div>
                            </div>
                        </div>
                    </div>

                    <!-- Action Bar -->
                    <div id="validationAlertContainer"></div>
                    <div class="d-flex gap-3 align-items-center mt-3 pt-3 border-top">
                        <button type="button" id="saveDraftActionBtn" class="btn btn-outline-secondary">
                            ${svgIcon('save', 'me-1')} Save Draft
                        </button>
                        <button type="button" id="livePreviewActionBtn" class="btn btn-outline-primary">
                            ${svgIcon('play', 'me-1')} Live Preview
                        </button>
                        <button type="button" id="publishActionBtn" class="btn btn-success">
                            ${svgIcon('publish', 'me-1')} Publish Simulation Lab
                        </button>
                        <a href="${options.cancelUrl || '#'}" class="btn btn-link text-secondary text-decoration-none">Cancel</a>
                    </div>
                </div>

                <!-- TAB 2: LIVE TERMINAL PREVIEW -->
                <div id="previewTabContent" style="${state.activeTab === 'preview' ? '' : 'display:none;'}">
                    <div class="alert alert-info d-flex justify-content-between align-items-center mb-3">
                        <div>
                            <strong>${svgIcon('terminal', 'me-1')} Live Sandbox Preview Mode</strong>: Test filesystem navigation, commands, and objectives completion right now.
                            <div class="small text-secondary">Activity in preview mode is completely isolated and does not save student progress records.</div>
                        </div>
                        <div class="d-flex gap-2">
                            <button type="button" id="resetPreviewBtn" class="btn btn-sm btn-outline-secondary">
                                ${svgIcon('reset', 'me-1')} Reset Environment
                            </button>
                            <button type="button" id="backToEditorBtn" class="btn btn-sm btn-primary">← Back to Visual Editor</button>
                        </div>
                    </div>

                    <div id="sim-builder-preview-success" class="alert alert-success" style="display:none;">
                        ${svgIcon('check', 'me-2')} <strong>Preview Verified!</strong> All lab objectives have been satisfied!
                    </div>

                    <!-- Objectives Checklist -->
                    <div class="builder-card mb-3">
                        <div class="builder-card-header py-2">
                            <h6 class="mb-0">Lab Objectives:</h6>
                        </div>
                        <div class="card-body p-3">
                            <ul class="objectives-list mb-0" id="simulation-objectives-list"></ul>
                        </div>
                    </div>

                    <!-- Preview Terminal Host -->
                    <div id="sim-builder-preview-terminal"></div>
                </div>
            `;

            // Restore tree scroll
            const newTree = document.getElementById("fs-tree-container");
            if (newTree) newTree.scrollTop = treeScrollTop;

            // Wire up event listeners
            attachEventListeners();
        }

        // Render Tree Node recursively
        function renderTreeNode(fs, path, depth) {
            const isRoot = path === '/';
            const name = isRoot ? '/' : getNameFromPath(path);
            const isDir = isRoot || getNodeType(fs, path) === 'dir';
            const isSelected = state.selectedPath === path;
            const isExpanded = state.expandedPaths.has(path);

            const paddingLeft = depth * 18 + 8;
            let html = '';

            if (isDir) {
                const node = getNode(fs, path);
                const childrenKeys = node && typeof node === 'object' ? Object.keys(node) : [];
                const childCount = childrenKeys.length;

                html += `
                    <div class="tree-item tree-dir ${isSelected ? 'selected' : ''}" 
                         data-path="${escapeHtml(path)}" 
                         data-type="dir"
                         draggable="${!isRoot}"
                         style="padding-left: ${paddingLeft}px;">
                        <span class="tree-caret" data-path="${escapeHtml(path)}">${svgIcon(isExpanded ? 'chevron-down' : 'chevron-right', 'sim-icon-sm')}</span>
                        <span class="tree-icon">${svgIcon(isExpanded ? 'folder-open' : 'folder')}</span>
                        <span class="tree-label">${escapeHtml(name)}</span>
                        <span class="tree-badge">${childCount}</span>
                    </div>
                `;

                if (isExpanded && childCount > 0) {
                    html += `<div class="tree-children">`;
                    const sorted = [...childrenKeys].sort((a, b) => {
                        const aIsDir = typeof node[a] === 'object';
                        const bIsDir = typeof node[b] === 'object';
                        if (aIsDir && !bIsDir) return -1;
                        if (!aIsDir && bIsDir) return 1;
                        return a.localeCompare(b);
                    });
                    for (const childName of sorted) {
                        const childPath = path === '/' ? `/${childName}` : `${path}/${childName}`;
                        html += renderTreeNode(fs, childPath, depth + 1);
                    }
                    html += `</div>`;
                }
            } else {
                html += `
                    <div class="tree-item tree-file ${isSelected ? 'selected' : ''}" 
                         data-path="${escapeHtml(path)}" 
                         data-type="file"
                         draggable="true"
                         style="padding-left: ${paddingLeft}px;">
                        <span class="tree-caret-placeholder"></span>
                        <span class="tree-icon">${svgIcon('file')}</span>
                        <span class="tree-label">${escapeHtml(name)}</span>
                    </div>
                `;
            }

            return html;
        }

        // Render Inspector Panel
        function renderInspector() {
            const path = state.selectedPath;
            const type = getNodeType(state.fileSystem, path);

            if (!type) {
                return `
                    <div class="text-center py-5 text-muted">
                        <div class="mb-2">${svgIcon('folder', 'sim-icon-lg')}</div>
                        <p class="mt-2">Select a file or folder from the tree to edit its properties.</p>
                    </div>
                `;
            }

            const isRoot = path === '/';
            const name = isRoot ? '/' : getNameFromPath(path);
            const parentPath = getParentPath(path);

            if (type === 'dir') {
                const dirNode = getNode(state.fileSystem, path);
                const children = dirNode && typeof dirNode === 'object' ? Object.keys(dirNode) : [];
                const isInitialCwd = state.initialDirectory === path;

                return `
                    <div class="inspector-panel">
                        <div class="d-flex justify-content-between align-items-center mb-3">
                            <span class="badge bg-primary">Directory</span>
                            <span class="badge ${isInitialCwd ? 'bg-success' : 'bg-secondary'}">
                                ${isInitialCwd ? 'Initial Working Directory' : 'Subfolder'}
                            </span>
                        </div>

                        <div class="mb-3">
                            <label class="form-label text-muted small mb-1">Absolute Path</label>
                            <div class="font-monospace p-2 builder-well rounded border">${escapeHtml(path)}</div>
                        </div>

                        ${isRoot ? `
                            <div class="alert alert-secondary py-2 small">Virtual Root Directory (/) cannot be renamed or deleted.</div>
                        ` : `
                            <div class="mb-3">
                                <label class="form-label">Folder Name</label>
                                <div class="input-group">
                                    <input type="text" id="inspectorItemName" class="form-control font-monospace" value="${escapeHtml(name)}" maxlength="255" />
                                    <button type="button" id="saveRenameBtn" class="btn btn-outline-secondary">Rename</button>
                                </div>
                                <div id="renameErrorMsg" class="text-danger small mt-1" style="display:none;"></div>
                            </div>
                        `}

                        <div class="mb-4">
                            <label class="form-label text-muted small mb-1">Contains</label>
                            <div>${children.length} item(s) in this folder.</div>
                        </div>

                        <div class="d-flex flex-wrap gap-2 pt-3 border-top">
                            <button type="button" class="btn btn-sm btn-outline-primary" id="addFolderInsideBtn">
                                ${svgIcon('plus', 'me-1')} Add Subfolder
                            </button>
                            <button type="button" class="btn btn-sm btn-outline-primary" id="addFileInsideBtn">
                                ${svgIcon('plus', 'me-1')} Add File
                            </button>
                            ${!isInitialCwd ? `
                                <button type="button" class="btn btn-sm btn-outline-success" id="setAsInitialCwdBtn">Set as Initial $CWD</button>
                            ` : ''}
                            ${!isRoot ? `
                                <button type="button" class="btn btn-sm btn-outline-danger ms-auto" id="deleteCurrentItemBtn">
                                    ${svgIcon('trash', 'me-1')} Delete Folder
                                </button>
                            ` : ''}
                        </div>
                    </div>
                `;
            } else {
                const content = getNode(state.fileSystem, path) || "";
                const lines = content.split('\n').length;

                return `
                    <div class="inspector-panel">
                        <div class="d-flex justify-content-between align-items-center mb-3">
                            <span class="badge bg-secondary">Text File</span>
                            <span class="badge bg-secondary">${content.length} bytes • ${lines} line(s)</span>
                        </div>

                        <div class="mb-3">
                            <label class="form-label text-muted small mb-1">Absolute Path</label>
                            <div class="font-monospace p-2 builder-well rounded border">${escapeHtml(path)}</div>
                        </div>

                        <div class="mb-3">
                            <label class="form-label">File Name</label>
                            <div class="input-group">
                                <input type="text" id="inspectorItemName" class="form-control font-monospace" value="${escapeHtml(name)}" maxlength="255" />
                                <button type="button" id="saveRenameBtn" class="btn btn-outline-secondary">Rename</button>
                            </div>
                            <div id="renameErrorMsg" class="text-danger small mt-1" style="display:none;"></div>
                        </div>

                        <div class="mb-3">
                            <div class="d-flex justify-content-between align-items-center mb-1">
                                <label class="form-label mb-0">File Text Content</label>
                                <span class="text-muted small">Supports empty files and multiline text</span>
                            </div>
                            <textarea id="fileContentEditor" class="form-control font-monospace" rows="10" spellcheck="false" placeholder="Enter text contents of the simulated file...">${escapeHtml(content)}</textarea>
                        </div>

                        <div class="d-flex justify-content-between align-items-center pt-3 border-top">
                            <button type="button" class="btn btn-sm btn-primary" id="saveFileContentBtn">
                                ${svgIcon('save', 'me-1')} Save Content
                            </button>
                            <button type="button" class="btn btn-sm btn-outline-danger" id="deleteCurrentItemBtn">
                                ${svgIcon('trash', 'me-1')} Delete File
                            </button>
                        </div>
                    </div>
                `;
            }
        }

        // Render Custom Command Card
        function renderCustomCommandCard(cc, idx) {
            const isFirst = idx === 0;
            const isLast = idx === state.customCommands.length - 1;
            const cmdStr = (cc.command || '').trim();

            let warningMsg = '';
            if (cc.enabled) {
                if (!cmdStr) {
                    warningMsg = `<span class="badge-builder-warning">${svgIcon('warning', 'me-1')} Command required</span>`;
                } else if (/[><|;&]/.test(cmdStr)) {
                    warningMsg = `<span class="badge-builder-warning">${svgIcon('warning', 'me-1')} Contains reserved syntax (>, <, |, ;, &)</span>`;
                } else if (ALL_SUPPORTED_COMMANDS.includes(cmdStr.toLowerCase())) {
                    warningMsg = `<span class="badge-builder-warning">${svgIcon('warning', 'me-1')} Conflicts with built-in command '${escapeHtml(cmdStr)}'</span>`;
                } else {
                    const duplicates = state.customCommands.filter((c, i) => i !== idx && c.enabled && (c.command || '').trim() === cmdStr);
                    if (duplicates.length > 0) {
                        warningMsg = `<span class="badge-builder-warning">${svgIcon('warning', 'me-1')} Duplicate command definition</span>`;
                    }
                }
            }

            return `
                <div class="custom-cmd-card p-3 rounded mb-3 ${cc.enabled ? '' : 'opacity-75'}" data-idx="${idx}">
                    <div class="d-flex justify-content-between align-items-center mb-2 pb-2 border-bottom">
                        <div class="d-flex align-items-center gap-2">
                            <span class="badge ${cc.enabled ? 'bg-primary' : 'bg-secondary'}">#${idx + 1}</span>
                            <div class="form-check form-switch mb-0">
                                <input class="form-check-input cc-enable-toggle" type="checkbox" id="cc_enable_${idx}" data-idx="${idx}" ${cc.enabled ? 'checked' : ''} />
                                <label class="form-check-label small fw-bold" for="cc_enable_${idx}">${cc.enabled ? 'Enabled' : 'Disabled'}</label>
                            </div>
                            <span class="font-monospace text-muted small ms-2">ID: ${escapeHtml(cc.id)}</span>
                            ${warningMsg}
                        </div>
                        <div class="d-flex gap-1 align-items-center">
                            <button type="button" class="btn btn-sm btn-outline-secondary cc-move-up" data-idx="${idx}" ${isFirst ? 'disabled' : ''} title="Move Up" aria-label="Move Up">
                                ${svgIcon('arrow-up')}
                            </button>
                            <button type="button" class="btn btn-sm btn-outline-secondary cc-move-down" data-idx="${idx}" ${isLast ? 'disabled' : ''} title="Move Down" aria-label="Move Down">
                                ${svgIcon('arrow-down')}
                            </button>
                            <button type="button" class="btn btn-sm btn-outline-danger cc-delete" data-idx="${idx}" title="Delete Custom Command" aria-label="Delete Custom Command">
                                ${svgIcon('trash')}
                            </button>
                        </div>
                    </div>

                    <div class="row g-2">
                        <div class="col-md-12">
                            <label class="form-label small text-muted mb-1">Exact Command String <span class="text-danger">*</span></label>
                            <input type="text" class="form-control form-control-sm font-monospace cc-command-input" data-idx="${idx}" value="${escapeHtml(cc.command)}" placeholder="e.g. nmap -sV 192.168.1.0/24" maxlength="512" required />
                            <div class="form-text small">Must be matched exactly by the student (case-sensitive, order of arguments preserved).</div>
                        </div>

                        <div class="col-md-12">
                            <label class="form-label small text-muted mb-1">Predefined Simulated Output <span class="text-danger">*</span></label>
                            <textarea class="form-control form-control-sm font-monospace cc-output-input" data-idx="${idx}" rows="4" placeholder="Simulated CLI response returned when this exact command is submitted...">${escapeHtml(cc.output)}</textarea>
                            <div class="form-text small">This simulated text is printed to the terminal or written to file when redirection (>) is used.</div>
                        </div>

                        <div class="col-md-6">
                            <label class="form-label small text-muted mb-1">Teacher Description (Optional)</label>
                            <input type="text" class="form-control form-control-sm cc-desc-input" data-idx="${idx}" value="${escapeHtml(cc.description || '')}" placeholder="e.g. Use nmap to scan the network" />
                        </div>

                        <div class="col-md-6">
                            <label class="form-label small text-muted mb-1">Student Hint (Optional)</label>
                            <input type="text" class="form-control form-control-sm cc-hint-input" data-idx="${idx}" value="${escapeHtml(cc.hint || '')}" placeholder="e.g. Use 'network enumerator'" />
                        </div>
                    </div>
                </div>
            `;
        }

        // Render Objective Card
        function renderObjectiveCard(obj, idx, allDirs, allFiles) {
            const isFirst = idx === 0;
            const isLast = idx === state.objectives.length - 1;

            let targetWarning = '';
            if (obj.type === 'cwd') {
                if (!allDirs.includes(normalizePath(obj.target))) {
                    targetWarning = `<span class="badge bg-danger">${svgIcon('warning', 'me-1')} Target directory does not exist</span>`;
                }
            } else if (obj.type === 'cat' || obj.type === 'chmod') {
                const fileExists = allFiles.some(f => f.name === obj.target || f.path === normalizePath(obj.target));
                if (!fileExists) {
                    targetWarning = `<span class="badge bg-danger">${svgIcon('warning', 'me-1')} Target file does not exist</span>`;
                }
            } else if (obj.type === 'command') {
                const isAllowed = state.allowedCommands.includes(obj.target.toLowerCase().trim())
                    || state.customCommands.some(cc => cc.command === obj.target);
                if (!isAllowed) {
                    targetWarning = `<span class="badge bg-danger">${svgIcon('warning', 'me-1')} Command not in allowed or custom commands</span>`;
                }
            }

            return `
                <div class="objective-card p-3 rounded mb-3" data-idx="${idx}">
                    <div class="d-flex justify-content-between align-items-center mb-2 pb-2 border-bottom">
                        <div class="d-flex align-items-center gap-2">
                            <span class="badge bg-secondary">Step #${idx + 1}</span>
                            <span class="font-monospace text-muted small">ID: ${escapeHtml(obj.id)}</span>
                            ${targetWarning}
                        </div>
                        <div class="d-flex gap-1 align-items-center">
                            <button type="button" class="btn btn-sm btn-outline-secondary obj-move-up" data-idx="${idx}" ${isFirst ? 'disabled' : ''} title="Move Up" aria-label="Move Up">
                                ${svgIcon('arrow-up')}
                            </button>
                            <button type="button" class="btn btn-sm btn-outline-secondary obj-move-down" data-idx="${idx}" ${isLast ? 'disabled' : ''} title="Move Down" aria-label="Move Down">
                                ${svgIcon('arrow-down')}
                            </button>
                            <button type="button" class="btn btn-sm btn-outline-danger obj-delete" data-idx="${idx}" title="Delete Objective" aria-label="Delete Objective">
                                ${svgIcon('trash')}
                            </button>
                        </div>
                    </div>

                    <div class="row g-2">
                        <div class="col-md-7">
                            <label class="form-label small text-muted mb-1">Objective Description (Student-facing)</label>
                            <input type="text" class="form-control form-control-sm obj-text-input" data-idx="${idx}" value="${escapeHtml(obj.text)}" placeholder="e.g. Navigate to /var/log directory" required />
                        </div>
                        <div class="col-md-5">
                            <label class="form-label small text-muted mb-1">Verification Type</label>
                            <select class="form-select form-select-sm obj-type-select" data-idx="${idx}">
                                <option value="cwd" ${obj.type === 'cwd' ? 'selected' : ''}>Navigate to Directory (cwd)</option>
                                <option value="cat" ${obj.type === 'cat' ? 'selected' : ''}>Read File (cat)</option>
                                <option value="chmod" ${obj.type === 'chmod' ? 'selected' : ''}>Change Permissions (chmod)</option>
                                <option value="command" ${obj.type === 'command' ? 'selected' : ''}>Run Command (command)</option>
                            </select>
                        </div>

                        <!-- Target Selector depending on Type -->
                        <div class="col-md-7">
                            <label class="form-label small text-muted mb-1">Target Value</label>
                            ${renderTargetInput(obj, idx, allDirs, allFiles)}
                        </div>

                        ${obj.type === 'chmod' ? `
                            <div class="col-md-2">
                                <label class="form-label small text-muted mb-1">Mode (Octal)</label>
                                <input type="text" class="form-control form-control-sm font-monospace obj-mode-input" data-idx="${idx}" value="${escapeHtml(obj.mode || '755')}" placeholder="755" maxlength="4" />
                            </div>
                        ` : ''}

                        <div class="${obj.type === 'chmod' ? 'col-md-3' : 'col-md-5'}">
                            <label class="form-label small text-muted mb-1">Student Hint (Optional)</label>
                            <input type="text" class="form-control form-control-sm obj-hint-input" data-idx="${idx}" value="${escapeHtml(obj.hint || '')}" placeholder="e.g. Use 'cd /var/log'" />
                        </div>
                    </div>
                </div>
            `;
        }

        // Render Target Input selector depending on objective type
        function renderTargetInput(obj, idx, allDirs, allFiles) {
            if (obj.type === 'cwd') {
                return `
                    <select class="form-select form-select-sm font-monospace obj-target-input" data-idx="${idx}">
                        ${allDirs.map(d => `<option value="${d}" ${d === obj.target ? 'selected' : ''}>${d}</option>`).join('')}
                    </select>
                `;
            } else if (obj.type === 'cat' || obj.type === 'chmod') {
                return `
                    <select class="form-select form-select-sm font-monospace obj-target-input" data-idx="${idx}">
                        ${allFiles.map(f => `<option value="${f.name}" ${f.name === obj.target || f.path === obj.target ? 'selected' : ''}>${f.name} (${f.path})</option>`).join('')}
                    </select>
                `;
            } else if (obj.type === 'command') {
                const builtins = state.allowedCommands.filter(c => c !== 'help' && c !== 'clear');
                const customs = state.customCommands.filter(c => c.enabled && (c.command || '').trim()).map(c => c.command.trim());
                return `
                    <select class="form-select form-select-sm font-monospace obj-target-input" data-idx="${idx}">
                        <optgroup label="Built-in Allowed Commands">
                            ${builtins.map(c => `<option value="${escapeHtml(c)}" ${c === obj.target ? 'selected' : ''}>${escapeHtml(c)}</option>`).join('')}
                        </optgroup>
                        ${customs.length > 0 ? `
                            <optgroup label="Custom Scenario Commands">
                                ${customs.map(c => `<option value="${escapeHtml(c)}" ${c === obj.target ? 'selected' : ''}>${escapeHtml(c)}</option>`).join('')}
                            </optgroup>
                        ` : ''}
                    </select>
                `;
            }
            return `<input type="text" class="form-control form-control-sm font-monospace obj-target-input" data-idx="${idx}" value="${escapeHtml(obj.target)}" />`;
        }

        // Attach Event Listeners
        function attachEventListeners() {
            // Mode Tabs
            document.getElementById("tabBtnEditor")?.addEventListener("click", () => {
                state.activeTab = "editor";
                render();
            });

            document.getElementById("tabBtnPreview")?.addEventListener("click", () => {
                startLivePreview();
            });

            document.getElementById("backToEditorBtn")?.addEventListener("click", () => {
                state.activeTab = "editor";
                render();
            });

            document.getElementById("resetPreviewBtn")?.addEventListener("click", () => {
                startLivePreview();
            });

            // Preset Template Button
            document.getElementById("loadPresetBtn")?.addEventListener("click", () => {
                const sel = document.getElementById("presetTemplateSelect")?.value;
                if (sel) applyTemplate(sel, true);
            });

            // Raw JSON Drawer Toggle
            const rawDrawer = document.getElementById("rawJsonDrawer");
            document.getElementById("toggleRawJsonBtn")?.addEventListener("click", () => {
                if (rawDrawer) {
                    const isVisible = rawDrawer.style.display !== "none";
                    rawDrawer.style.display = isVisible ? "none" : "block";
                    if (!isVisible) {
                        const rawText = document.getElementById("rawJsonTextarea");
                        if (rawText) rawText.value = JSON.stringify(serializeConfiguration(), null, 2);
                    }
                }
            });

            document.getElementById("copyJsonBtn")?.addEventListener("click", () => {
                const rawText = document.getElementById("rawJsonTextarea");
                if (rawText) {
                    navigator.clipboard.writeText(rawText.value);
                    alert("Configuration JSON copied to clipboard!");
                }
            });

            document.getElementById("applyRawJsonBtn")?.addEventListener("click", () => {
                const rawText = document.getElementById("rawJsonTextarea")?.value;
                try {
                    const parsed = JSON.parse(rawText);
                    if (parsed.fileSystem && typeof parsed.fileSystem === 'object') state.fileSystem = parsed.fileSystem;
                    if (Array.isArray(parsed.allowedCommands)) state.allowedCommands = parsed.allowedCommands;
                    if (parsed.initialDirectory) state.initialDirectory = parsed.initialDirectory;
                    if (Array.isArray(parsed.customCommands)) state.customCommands = parsed.customCommands;
                    if (Array.isArray(parsed.objectives)) state.objectives = parsed.objectives;
                    ensureMandatoryCommands();
                    syncStateToInput();
                    render();
                } catch (e) {
                    alert("Invalid JSON: " + e.message);
                }
            });

            // Simulation Details Fields
            document.getElementById("builderTitle")?.addEventListener("input", (e) => {
                state.title = e.target.value;
                const formTitle = document.getElementById("Title");
                if (formTitle) formTitle.value = state.title;
            });

            document.getElementById("builderDifficulty")?.addEventListener("change", (e) => {
                state.difficulty = e.target.value;
                syncStateToInput();
            });

            document.getElementById("builderDescription")?.addEventListener("input", (e) => {
                state.description = e.target.value;
                syncStateToInput();
            });

            document.getElementById("builderInstructions")?.addEventListener("input", (e) => {
                state.instructions = e.target.value;
                const formInstructions = document.getElementById("Instructions");
                if (formInstructions) formInstructions.value = state.instructions;
            });

            document.getElementById("builderRewardFlag")?.addEventListener("input", (e) => {
                state.rewardFlag = e.target.value;
                const formReward = document.getElementById("RewardFlag");
                if (formReward) formReward.value = state.rewardFlag;
            });

            document.getElementById("builderIsPublished")?.addEventListener("change", (e) => {
                state.isPublished = e.target.checked;
                const formPublished = document.getElementById("isPublishedCheck");
                if (formPublished) formPublished.checked = state.isPublished;
            });

            // Tree Carets & Selection
            rootContainer.querySelectorAll(".tree-caret").forEach(caret => {
                caret.addEventListener("click", (e) => {
                    e.stopPropagation();
                    const path = caret.getAttribute("data-path");
                    if (state.expandedPaths.has(path)) state.expandedPaths.delete(path);
                    else state.expandedPaths.add(path);
                    render();
                });
            });

            rootContainer.querySelectorAll(".tree-item").forEach(item => {
                item.addEventListener("click", () => {
                    state.selectedPath = item.getAttribute("data-path");
                    render();
                });
            });

            // Drag and Drop in Tree
            let draggedPath = null;
            rootContainer.querySelectorAll(".tree-item[draggable='true']").forEach(draggable => {
                draggable.addEventListener("dragstart", (e) => {
                    draggedPath = draggable.getAttribute("data-path");
                    e.dataTransfer.setData("text/plain", draggedPath);
                    e.dataTransfer.effectAllowed = "move";
                });
            });

            rootContainer.querySelectorAll(".tree-dir").forEach(dirItem => {
                dirItem.addEventListener("dragover", (e) => {
                    const targetPath = dirItem.getAttribute("data-path");
                    if (draggedPath && targetPath !== draggedPath && !targetPath.startsWith(draggedPath + '/')) {
                        e.preventDefault();
                        e.dataTransfer.dropEffect = "move";
                        dirItem.classList.add("drop-hover");
                    }
                });

                dirItem.addEventListener("dragleave", () => {
                    dirItem.classList.remove("drop-hover");
                });

                dirItem.addEventListener("drop", (e) => {
                    e.preventDefault();
                    dirItem.classList.remove("drop-hover");
                    const targetPath = dirItem.getAttribute("data-path");
                    if (draggedPath && targetPath) {
                        const result = moveItem(draggedPath, targetPath);
                        if (!result.success) {
                            alert(result.error);
                        }
                    }
                });
            });

            // Tree Toolbar Actions
            document.getElementById("expandAllBtn")?.addEventListener("click", () => {
                state.expandedPaths = new Set(getAllDirectories(state.fileSystem));
                render();
            });

            document.getElementById("collapseAllBtn")?.addEventListener("click", () => {
                state.expandedPaths = new Set(["/"]);
                render();
            });

            document.getElementById("newFolderAtRootBtn")?.addEventListener("click", () => {
                const name = prompt("Enter new folder name inside / :");
                if (name) {
                    const res = createDirectory("/", name);
                    if (!res.success) alert(res.error);
                }
            });

            document.getElementById("newFileAtRootBtn")?.addEventListener("click", () => {
                const name = prompt("Enter new file name inside / :");
                if (name) {
                    const res = createFile("/", name, "");
                    if (!res.success) alert(res.error);
                }
            });

            // Inspector Actions
            document.getElementById("addFolderInsideBtn")?.addEventListener("click", () => {
                const name = prompt(`Enter new subfolder name inside ${state.selectedPath} :`);
                if (name) {
                    const res = createDirectory(state.selectedPath, name);
                    if (!res.success) alert(res.error);
                }
            });

            document.getElementById("addFileInsideBtn")?.addEventListener("click", () => {
                const name = prompt(`Enter new file name inside ${state.selectedPath} :`);
                if (name) {
                    const res = createFile(state.selectedPath, name, "");
                    if (!res.success) alert(res.error);
                }
            });

            document.getElementById("saveRenameBtn")?.addEventListener("click", () => {
                const newName = document.getElementById("inspectorItemName")?.value;
                if (newName) {
                    const res = renameItem(state.selectedPath, newName);
                    if (!res.success) {
                        const errEl = document.getElementById("renameErrorMsg");
                        if (errEl) { errEl.textContent = res.error; errEl.style.display = "block"; }
                    }
                }
            });

            document.getElementById("saveFileContentBtn")?.addEventListener("click", () => {
                const content = document.getElementById("fileContentEditor")?.value ?? "";
                updateFileContent(state.selectedPath, content);
                alert("File content saved.");
            });

            document.getElementById("deleteCurrentItemBtn")?.addEventListener("click", () => {
                const name = getNameFromPath(state.selectedPath);
                if (confirm(`Are you sure you want to delete "${name}"? This cannot be undone.`)) {
                    const res = deleteItem(state.selectedPath);
                    if (!res.success) alert(res.error);
                }
            });

            document.getElementById("setAsInitialCwdBtn")?.addEventListener("click", () => {
                state.initialDirectory = state.selectedPath;
                syncStateToInput();
                render();
            });

            // Initial Directory Selector
            document.getElementById("initialDirectorySelect")?.addEventListener("change", (e) => {
                state.initialDirectory = e.target.value;
                syncStateToInput();
            });

            // Command Checkboxes
            rootContainer.querySelectorAll(".cmd-checkbox").forEach(cb => {
                cb.addEventListener("change", () => {
                    const cmdId = cb.value;
                    if (cb.checked) {
                        if (!state.allowedCommands.includes(cmdId)) state.allowedCommands.push(cmdId);
                    } else {
                        state.allowedCommands = state.allowedCommands.filter(c => c !== cmdId);
                    }
                    ensureMandatoryCommands();
                    syncStateToInput();
                });
            });

            document.getElementById("cmdSelectAllBtn")?.addEventListener("click", () => {
                state.allowedCommands = [...ALL_SUPPORTED_COMMANDS];
                ensureMandatoryCommands();
                syncStateToInput();
                render();
            });

            document.getElementById("cmdSelectRecommendedBtn")?.addEventListener("click", () => {
                state.allowedCommands = ["help", "clear", "pwd", "ls", "cd", "cat", "touch", "nano", "grep", "whoami", "chmod"];
                ensureMandatoryCommands();
                syncStateToInput();
                render();
            });

            document.getElementById("cmdClearAllBtn")?.addEventListener("click", () => {
                state.allowedCommands = ["help", "clear"];
                syncStateToInput();
                render();
            });

            // Custom Commands Controls
            document.getElementById("addCustomCommandBtn")?.addEventListener("click", () => {
                addCustomCommand();
            });

            rootContainer.querySelectorAll(".cc-move-up").forEach(btn => {
                btn.addEventListener("click", () => {
                    const idx = parseInt(btn.getAttribute("data-idx"), 10);
                    moveCustomCommand(idx, -1);
                });
            });

            rootContainer.querySelectorAll(".cc-move-down").forEach(btn => {
                btn.addEventListener("click", () => {
                    const idx = parseInt(btn.getAttribute("data-idx"), 10);
                    moveCustomCommand(idx, 1);
                });
            });

            rootContainer.querySelectorAll(".cc-delete").forEach(btn => {
                btn.addEventListener("click", () => {
                    const idx = parseInt(btn.getAttribute("data-idx"), 10);
                    if (confirm("Delete this custom command?")) removeCustomCommand(idx);
                });
            });

            rootContainer.querySelectorAll(".cc-enable-toggle").forEach(toggle => {
                toggle.addEventListener("change", () => {
                    const idx = parseInt(toggle.getAttribute("data-idx"), 10);
                    toggleCustomCommand(idx);
                });
            });

            rootContainer.querySelectorAll(".cc-command-input").forEach(inp => {
                inp.addEventListener("input", () => {
                    const idx = parseInt(inp.getAttribute("data-idx"), 10);
                    updateCustomCommand(idx, "command", inp.value);
                });
            });

            rootContainer.querySelectorAll(".cc-output-input").forEach(inp => {
                inp.addEventListener("input", () => {
                    const idx = parseInt(inp.getAttribute("data-idx"), 10);
                    updateCustomCommand(idx, "output", inp.value);
                });
            });

            rootContainer.querySelectorAll(".cc-desc-input").forEach(inp => {
                inp.addEventListener("input", () => {
                    const idx = parseInt(inp.getAttribute("data-idx"), 10);
                    updateCustomCommand(idx, "description", inp.value);
                });
            });

            rootContainer.querySelectorAll(".cc-hint-input").forEach(inp => {
                inp.addEventListener("input", () => {
                    const idx = parseInt(inp.getAttribute("data-idx"), 10);
                    updateCustomCommand(idx, "hint", inp.value);
                });
            });

            // Objective Builder Controls
            document.getElementById("addObjectiveBtn")?.addEventListener("click", () => {
                addObjective();
            });

            rootContainer.querySelectorAll(".obj-move-up").forEach(btn => {
                btn.addEventListener("click", () => {
                    const idx = parseInt(btn.getAttribute("data-idx"), 10);
                    moveObjective(idx, -1);
                });
            });

            rootContainer.querySelectorAll(".obj-move-down").forEach(btn => {
                btn.addEventListener("click", () => {
                    const idx = parseInt(btn.getAttribute("data-idx"), 10);
                    moveObjective(idx, 1);
                });
            });

            rootContainer.querySelectorAll(".obj-delete").forEach(btn => {
                btn.addEventListener("click", () => {
                    const idx = parseInt(btn.getAttribute("data-idx"), 10);
                    if (confirm("Delete this objective?")) removeObjective(idx);
                });
            });

            rootContainer.querySelectorAll(".obj-text-input").forEach(inp => {
                inp.addEventListener("change", () => {
                    const idx = parseInt(inp.getAttribute("data-idx"), 10);
                    updateObjective(idx, "text", inp.value);
                });
            });

            rootContainer.querySelectorAll(".obj-type-select").forEach(sel => {
                sel.addEventListener("change", () => {
                    const idx = parseInt(sel.getAttribute("data-idx"), 10);
                    const newType = sel.value;
                    const dirs = getAllDirectories(state.fileSystem);
                    const files = getAllFiles(state.fileSystem);

                    let newTarget = "";
                    if (newType === 'cwd') newTarget = dirs.find(d => d !== '/') || "/home/student";
                    else if (newType === 'cat' || newType === 'chmod') newTarget = files[0]?.name || "notes.txt";
                    else if (newType === 'command') newTarget = state.allowedCommands.find(c => c !== 'help' && c !== 'clear') || "pwd";

                    state.objectives[idx].type = newType;
                    state.objectives[idx].target = newTarget;
                    if (newType === 'chmod' && !state.objectives[idx].mode) state.objectives[idx].mode = "755";
                    syncStateToInput();
                    render();
                });
            });

            rootContainer.querySelectorAll(".obj-target-input").forEach(inp => {
                inp.addEventListener("change", () => {
                    const idx = parseInt(inp.getAttribute("data-idx"), 10);
                    updateObjective(idx, "target", inp.value);
                });
            });

            rootContainer.querySelectorAll(".obj-mode-input").forEach(inp => {
                inp.addEventListener("change", () => {
                    const idx = parseInt(inp.getAttribute("data-idx"), 10);
                    updateObjective(idx, "mode", inp.value);
                });
            });

            rootContainer.querySelectorAll(".obj-hint-input").forEach(inp => {
                inp.addEventListener("change", () => {
                    const idx = parseInt(inp.getAttribute("data-idx"), 10);
                    updateObjective(idx, "hint", inp.value);
                });
            });

            // Action Buttons: Save Draft, Preview, Publish
            document.getElementById("livePreviewActionBtn")?.addEventListener("click", () => {
                startLivePreview();
            });

            document.getElementById("saveDraftActionBtn")?.addEventListener("click", () => {
                state.isPublished = false;
                submitForm(false);
            });

            document.getElementById("publishActionBtn")?.addEventListener("click", () => {
                state.isPublished = true;
                submitForm(true);
            });
        }

        function submitForm(forPublish) {
            syncStateToInput();

            const titleInput = document.getElementById("Title");
            if (titleInput) titleInput.value = state.title;

            const descInput = document.getElementById("Description");
            if (descInput) descInput.value = state.description;

            const diffInput = document.getElementById("Difficulty");
            if (diffInput) diffInput.value = state.difficulty;

            const instInput = document.getElementById("Instructions");
            if (instInput) instInput.value = state.instructions;

            const rewardInput = document.getElementById("RewardFlag");
            if (rewardInput) rewardInput.value = state.rewardFlag;

            const pubCheck = document.getElementById("isPublishedCheck");
            if (pubCheck) pubCheck.checked = state.isPublished;

            const alertContainer = document.getElementById("validationAlertContainer");

            if (forPublish) {
                const errors = validateForPublish();
                if (errors.length > 0) {
                    if (alertContainer) {
                        alertContainer.innerHTML = `
                            <div class="alert alert-danger mt-3">
                                <h6 class="alert-heading">Cannot publish simulation. Please correct the following:</h6>
                                <ul class="mb-0 ps-3">
                                    ${errors.map(e => `<li>${escapeHtml(e)}</li>`).join('')}
                                </ul>
                            </div>
                        `;
                        alertContainer.scrollIntoView({ behavior: 'smooth' });
                    } else {
                        alert(errors.join("\n"));
                    }
                    return;
                }
            } else {
                if (!state.title.trim()) {
                    alert("Please provide at least a simulation title to save a draft.");
                    return;
                }
            }

            if (formEl) {
                formEl.submit();
            }
        }

        function escapeHtml(str) {
            return String(str ?? '')
                .replace(/&/g, '&amp;')
                .replace(/</g, '&lt;')
                .replace(/>/g, '&gt;')
                .replace(/"/g, '&quot;')
                .replace(/'/g, '&#39;');
        }

        // Initial Render
        render();
    };
})();
