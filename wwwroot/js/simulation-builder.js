/**
 * HackAcad Visual Simulation Builder
 * Form-based authoring interface for terminal lab simulations.
 * Provides interactive virtual filesystem tree, command permissions manager,
 * exact-match custom commands, visual objective builder, live isolated terminal preview, and validation.
 */
(function () {
    'use strict';

    // Supported commands strictly implemented by the simulation engine (13 configurable + help & clear)
    const ALL_SUPPORTED_COMMANDS = [
        "help", "clear", "pwd", "ls", "cd", "cat", "grep", "chmod",
        "whoami", "id", "echo", "history", "date", "touch", "nano"
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
            sequentialObjectives: true,
            objectives: [
                { id: "obj_nav_log", text: "Navigate to the /var/log directory using cd", command: "cd /var/log", executionDirectory: "", hint: "Run 'cd /var/log'" },
                { id: "obj_read_auth", text: "Read auth.log using cat to find the secret token", command: "cat auth.log", executionDirectory: "/var/log", hint: "Run 'cat auth.log'" }
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
            sequentialObjectives: true,
            objectives: [
                { id: "obj_verify_pwd", text: "Check your initial location using pwd", command: "pwd", executionDirectory: "", hint: "Type 'pwd' and press Enter" }
            ]
        }
    };

    window.initSimulationBuilder = function (options) {
        const rootContainer = document.getElementById(options.containerId);
        if (!rootContainer) return;

        const hiddenJsonInput = document.getElementById(options.hiddenJsonInputId);
        const formEl = document.getElementById(options.formId);

        // Core State
        const isNotebookBlock = options.isNotebookBlock === true || options.mode === 'notebook_block';

        let state = {
            title: options.initialTitle || "",
            description: options.initialDescription || "",
            difficulty: options.initialDifficulty || "Beginner",
            instructions: options.initialInstructions || "",
            isPublished: options.initialIsPublished ?? true,
            fileSystem: {},
            allowedCommands: ["help", "clear", "pwd", "ls", "cd", "cat", "whoami"],
            initialDirectory: "/home/student",
            customCommands: [],
            sequentialObjectives: true,
            objectives: [],
            selectedPath: "/", // currently selected item in filesystem
            expandedPaths: new Set(["/", "/home", "/home/student", "/var"]),
            activeTab: "editor" // "editor" | "preview"
        };

        // Load initial JSON configuration if provided
        try {
            let parsed = null;
            if (options.initialConfig) {
                parsed = typeof options.initialConfig === 'string' ? JSON.parse(options.initialConfig) : JSON.parse(JSON.stringify(options.initialConfig));
            } else if (hiddenJsonInput && hiddenJsonInput.value.trim()) {
                parsed = JSON.parse(hiddenJsonInput.value.trim());
            }

            if (parsed) {
                if (parsed.fileSystem && typeof parsed.fileSystem === 'object') {
                    state.fileSystem = JSON.parse(JSON.stringify(parsed.fileSystem));
                }
                if (Array.isArray(parsed.allowedCommands)) {
                    state.allowedCommands = parsed.allowedCommands
                        .map(c => c.toLowerCase().trim())
                        .filter(c => ALL_SUPPORTED_COMMANDS.includes(c)); // Discard obsolete commands safely
                }
                if (parsed.initialDirectory && typeof parsed.initialDirectory === 'string') {
                    state.initialDirectory = normalizePath(parsed.initialDirectory);
                }
                if (typeof parsed.sequentialObjectives === 'boolean') {
                    state.sequentialObjectives = parsed.sequentialObjectives;
                }
                if (Array.isArray(parsed.customCommands)) {
                    state.customCommands = parsed.customCommands.map(cc => {
                        let reqFiles = [];
                        if (Array.isArray(cc.requiredFiles)) {
                            reqFiles = cc.requiredFiles.map(s => String(s).trim()).filter(Boolean);
                        } else if (typeof cc.requiredFiles === 'string') {
                            reqFiles = cc.requiredFiles.split(/[\r\n,]+/).map(s => s.trim()).filter(Boolean);
                        }
                        return {
                            id: cc.id || `cc_${Math.random().toString(36).substring(2, 9)}`,
                            command: cc.command || "",
                            output: cc.output || "",
                            description: cc.description || "",
                            hint: cc.hint || "",
                            enabled: cc.enabled !== false,
                            requiredFiles: reqFiles,
                            _advancedOpen: false
                        };
                    });
                }
                if (Array.isArray(parsed.objectives)) {
                    state.objectives = parsed.objectives.map((o, idx) => {
                        let cmd = (o.command || o.expectedCommand || '').trim();
                        // Backward-compatibility fallback for legacy objectives
                        if (!cmd && o.target) {
                            if (o.type === 'cwd') cmd = `cd ${o.target}`;
                            else if (o.type === 'cat') cmd = `cat ${o.target}`;
                            else if (o.type === 'chmod') cmd = `chmod ${o.mode || '755'} ${o.target}`;
                            else cmd = o.target;
                        }
                        return {
                            id: o.id || `obj_${Date.now().toString(36)}_${idx + 1}`,
                            text: o.text || '',
                            command: cmd,
                            executionDirectory: o.executionDirectory ? normalizePath(o.executionDirectory) : '',
                            hint: o.hint || ''
                        };
                    });
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

        function getAllDirectories(fs, currentPath = '') {
            let dirs = ['/'];
            function traverse(node, p) {
                if (!node || typeof node !== 'object') return;
                for (let key of Object.keys(node)) {
                    if (typeof node[key] === 'object') {
                        const subPath = p === '/' ? `/${key}` : `${p}/${key}`;
                        dirs.push(subPath);
                        traverse(node[key], subPath);
                    }
                }
            }
            traverse(fs, '/');
            return Array.from(new Set(dirs)).sort();
        }

        function getAllFiles(fs) {
            let files = [];
            function traverse(node, p) {
                if (!node || typeof node !== 'object') return;
                for (let key of Object.keys(node)) {
                    const subPath = p === '/' ? `/${key}` : `${p}/${key}`;
                    if (typeof node[key] === 'string') {
                        files.push({ name: key, path: subPath, content: node[key] });
                    } else if (typeof node[key] === 'object') {
                        traverse(node[key], subPath);
                    }
                }
            }
            traverse(fs, '/');
            return files;
        }

        function ensureMandatoryCommands() {
            MANDATORY_SYSTEM_COMMANDS.forEach(mc => {
                if (!state.allowedCommands.includes(mc.id)) {
                    state.allowedCommands.push(mc.id);
                }
            });
        }

        function escapeHtml(str) {
            return String(str ?? '')
                .replace(/&/g, '&amp;')
                .replace(/</g, '&lt;')
                .replace(/>/g, '&gt;')
                .replace(/"/g, '&quot;')
                .replace(/'/g, '&#39;');
        }

        // Filesystem Mutators
        function addFolder(parentPath, folderName) {
            const safeName = folderName.trim();
            if (!safeName || safeName.includes('/') || safeName.includes('\\')) {
                return { success: false, error: "Invalid directory name. Must not contain slashes or be empty." };
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

        function addFile(parentPath, fileName, content = "") {
            const safeName = fileName.trim();
            if (!safeName || safeName.includes('/') || safeName.includes('\\')) {
                return { success: false, error: "Invalid file name. Must not contain slashes or be empty." };
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

            // Update objective execution directories safely
            state.objectives.forEach(obj => {
                if (obj.executionDirectory === itemPath) {
                    obj.executionDirectory = newPath;
                } else if (obj.executionDirectory && obj.executionDirectory.startsWith(itemPath + '/')) {
                    obj.executionDirectory = newPath + obj.executionDirectory.substring(itemPath.length);
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

        function updateFileContent(filePath, content) {
            const parentPath = getParentPath(filePath);
            const name = getNameFromPath(filePath);
            const parent = getNode(state.fileSystem, parentPath);
            if (!parent || typeof parent !== 'object' || typeof parent[name] !== 'string') {
                return { success: false, error: "File not found." };
            }
            parent[name] = content;
            syncStateToInput();
            return { success: true };
        }

        // Apply Preset Template
        function applyTemplate(templateKey, confirmPrompt = true) {
            const t = TEMPLATES[templateKey];
            if (!t) return;
            if (confirmPrompt && !confirm(`Apply the "${t.name}"? This will replace your current filesystem, commands, and objectives.`)) {
                return;
            }

            state.fileSystem = JSON.parse(JSON.stringify(t.fileSystem));
            state.allowedCommands = [...t.allowedCommands];
            ensureMandatoryCommands();
            state.initialDirectory = t.initialDirectory || "/home/student";
            state.customCommands = (t.customCommands && Array.isArray(t.customCommands)) ? t.customCommands.map(cc => ({
                id: cc.id || `cc_${Math.random().toString(36).substring(2, 9)}`,
                command: cc.command || "",
                output: cc.output || "",
                description: cc.description || "",
                hint: cc.hint || "",
                enabled: cc.enabled !== false,
                requiredFiles: Array.isArray(cc.requiredFiles) ? [...cc.requiredFiles] : [],
                _advancedOpen: false
            })) : [];
            state.sequentialObjectives = t.sequentialObjectives !== false;
            state.objectives = JSON.parse(JSON.stringify(t.objectives));
            state.selectedPath = state.initialDirectory;
            state.expandedPaths = new Set(["/", "/home", "/home/student", "/var"]);

            syncStateToInput();
            render();
        }

        // Custom Commands Management
        function addCustomCommand() {
            const nextIdx = state.customCommands.length + 1;
            state.customCommands.push({
                id: `cc_${Date.now().toString(36)}_${nextIdx}`,
                command: "",
                output: "",
                description: "",
                hint: "",
                enabled: true,
                requiredFiles: [],
                _advancedOpen: false
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

        function updateCustomCommand(index, field, value) {
            if (!state.customCommands[index]) return;
            state.customCommands[index][field] = value;
            syncStateToInput();
        }

        // Objective Management
        function addObjective() {
            const nextIdx = state.objectives.length + 1;
            state.objectives.push({
                id: `obj_${Date.now().toString(36)}_${nextIdx}`,
                text: "",
                command: "",
                executionDirectory: "",
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
            const config = {
                allowedCommands: state.allowedCommands,
                initialDirectory: state.initialDirectory,
                fileSystem: state.fileSystem,
                customCommands: state.customCommands.map(cc => {
                    let reqFiles = [];
                    if (Array.isArray(cc.requiredFiles)) {
                        reqFiles = cc.requiredFiles.map(s => String(s).trim()).filter(Boolean);
                    } else if (typeof cc.requiredFiles === 'string') {
                        reqFiles = cc.requiredFiles.split(/[\r\n,]+/).map(s => s.trim()).filter(Boolean);
                    }
                    return {
                        id: cc.id || `cc_${Math.random().toString(36).substring(2, 9)}`,
                        command: (cc.command || "").trim(),
                        output: cc.output || "",
                        description: (cc.description || "").trim(),
                        hint: (cc.hint || "").trim(),
                        enabled: cc.enabled !== false,
                        requiredFiles: reqFiles
                    };
                }),
                isNotebook: isNotebookBlock
            };
            if (!isNotebookBlock) {
                config.description = state.description;
                config.difficulty = state.difficulty;
                config.sequentialObjectives = state.sequentialObjectives !== false;
                config.objectives = state.objectives.map(o => ({
                    id: o.id,
                    text: (o.text || '').trim(),
                    command: (o.command || '').trim(),
                    executionDirectory: o.executionDirectory ? normalizePath(o.executionDirectory) : '',
                    hint: (o.hint || '').trim()
                }));
            }
            return config;
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
                if (cc.enabled !== false) {
                    const cmdStr = (cc.command || "").trim();
                    if (!cmdStr) {
                        errors.push(`Custom Command #${idx + 1} has an empty command string.`);
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

                        // Validate Required Files and Directories
                        const reqFiles = Array.isArray(cc.requiredFiles)
                            ? cc.requiredFiles
                            : (typeof cc.requiredFiles === 'string' ? cc.requiredFiles.split(/[\r\n,]+/) : []);
                        reqFiles.forEach(rf => {
                            const pathStr = String(rf).trim();
                            if (!pathStr) return;
                            if (pathStr.startsWith('/')) {
                                errors.push(`Custom Command #${idx + 1}: Required file '${pathStr}' must be a relative path, not an absolute path.`);
                            }
                            if (pathStr.includes('\\')) {
                                errors.push(`Custom Command #${idx + 1}: Required file '${pathStr}' contains invalid backslashes.`);
                            }
                            const segs = pathStr.split('/').filter(Boolean);
                            if (segs.some(s => s === '..' || s === '.')) {
                                errors.push(`Custom Command #${idx + 1}: Required file '${pathStr}' contains traversal segments ('..' or '.').`);
                            }
                        });
                    }
                }
            });

            // Objectives check (Bypassed for Notebook Blocks)
            if (!isNotebookBlock) {
                if (state.objectives.length === 0) {
                    errors.push("Define at least one student task objective.");
                }

                state.objectives.forEach((obj, idx) => {
                    if (!obj.text || !obj.text.trim()) {
                        errors.push(`Objective #${idx + 1} is missing a description.`);
                    }
                    const cmd = (obj.command || '').trim();
                    if (!cmd) {
                        errors.push(`Objective #${idx + 1} requires an Expected Command.`);
                    }
                    if (obj.executionDirectory) {
                        const normDir = normalizePath(obj.executionDirectory);
                        if (!dirs.includes(normDir)) {
                            errors.push(`Objective #${idx + 1} references non-existent execution directory "${obj.executionDirectory}".`);
                        }
                    }
                });
            }

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
                    <div class="d-flex gap-2 align-items-center">
                        <button type="button" id="toggleRawJsonBtn" class="btn btn-sm btn-outline-secondary">
                            ${svgIcon('settings', 'me-1')} View / Edit Raw JSON
                        </button>
                    </div>
                </div>

                <!-- TAB 1: VISUAL BUILDER EDITOR -->
                <div id="editorTabContent" style="${state.activeTab === 'editor' ? '' : 'display:none;'}">

                    <!-- Presets & Template Banner -->
                    <div class="builder-card mb-4">
                        <div class="card-body p-3 d-flex justify-content-between align-items-center flex-wrap gap-2">
                            <div class="d-flex align-items-center gap-2">
                                <span class="fw-bold">${svgIcon('folder', 'me-1')} Quick Preset Template:</span>
                                <select id="presetTemplateSelect" class="form-select form-select-sm">
                                    <option value="linux_standard">Linux Filesystem Template (Recommended)</option>
                                    <option value="scratch">Start from Scratch (Empty Filesystem)</option>
                                </select>
                            </div>
                            <button type="button" id="loadPresetBtn" class="btn btn-sm btn-outline-primary">
                                Apply Preset
                            </button>
                        </div>
                    </div>

                    <!-- Collapsible Raw JSON Drawer -->
                    <div id="rawJsonDrawer" class="builder-card mb-4" style="display: none;">
                        <div class="builder-card-header d-flex justify-content-between align-items-center">
                            <h6 class="mb-0">Raw Configuration JSON</h6>
                            <div class="d-flex gap-2">
                                <button type="button" id="copyJsonBtn" class="btn btn-sm btn-outline-secondary">Copy JSON</button>
                                <button type="button" id="applyRawJsonBtn" class="btn btn-sm btn-success">Apply JSON to Builder</button>
                            </div>
                        </div>
                        <div class="card-body p-3">
                            <textarea id="rawJsonTextarea" class="form-control font-monospace text-xs" rows="10"></textarea>
                            <div class="form-text small mt-1">Manual JSON changes will overwrite the visual editor state.</div>
                        </div>
                    </div>

                    <!-- Section 1: Lab Details -->
                    ${isNotebookBlock ? `
                    <div class="builder-card mb-4">
                        <div class="builder-card-header">
                            <h5 class="mb-0">1. Demonstration Metadata</h5>
                            <small class="text-secondary">Set a title and description for this interactive notebook sandbox</small>
                        </div>
                        <div class="card-body p-4">
                            <div class="row g-3">
                                <div class="col-12">
                                    <label class="form-label fw-bold">Demonstration Title <span class="text-danger">*</span></label>
                                    <input type="text" id="builderTitle" class="form-control" value="${escapeHtml(state.title)}" placeholder="e.g. Linux Navigation Sandbox" required />
                                </div>
                                <div class="col-12">
                                    <label class="form-label fw-bold">Summary Description</label>
                                    <input type="text" id="builderDescription" class="form-control" value="${escapeHtml(state.description)}" placeholder="Brief explanation of the terminal demonstration..." />
                                </div>
                            </div>
                        </div>
                    </div>
                    ` : `
                    <div class="builder-card mb-4">
                        <div class="builder-card-header">
                            <h5 class="mb-0">1. Simulation Metadata</h5>
                        </div>
                        <div class="card-body p-4">
                            <div class="row g-3">
                                <div class="col-md-8">
                                    <label class="form-label fw-bold">Lab Title <span class="text-danger">*</span></label>
                                    <input type="text" id="builderTitle" class="form-control" value="${escapeHtml(state.title)}" placeholder="e.g. Linux Log Inspection and Permission Auditing" required />
                                </div>
                                <div class="col-md-4">
                                    <label class="form-label fw-bold">Difficulty Level</label>
                                    <select id="builderDifficulty" class="form-select">
                                        <option value="Beginner" ${state.difficulty === 'Beginner' ? 'selected' : ''}>Beginner</option>
                                        <option value="Intermediate" ${state.difficulty === 'Intermediate' ? 'selected' : ''}>Intermediate</option>
                                        <option value="Advanced" ${state.difficulty === 'Advanced' ? 'selected' : ''}>Advanced</option>
                                    </select>
                                </div>
                                <div class="col-12">
                                    <label class="form-label fw-bold">Summary Description</label>
                                    <input type="text" id="builderDescription" class="form-control" value="${escapeHtml(state.description)}" placeholder="Brief overview of lab skills and tools practiced..." />
                                </div>
                                <div class="col-12">
                                    <label class="form-label fw-bold">Student Instructions / Lab Briefing</label>
                                    <textarea id="builderInstructions" class="form-control" rows="3" placeholder="Provide background story, context, or starting instructions for the student...">${escapeHtml(state.instructions)}</textarea>
                                </div>
                            </div>
                        </div>
                    </div>
                    `}

                    <!-- Section 2: Virtual Filesystem Builder -->
                    <div class="builder-card mb-4">
                        <div class="builder-card-header d-flex justify-content-between align-items-center flex-wrap gap-2">
                            <div>
                                <h5 class="mb-0">2. Virtual Filesystem Tree</h5>
                                <small class="text-secondary">Click any folder or file to inspect, create subfolders, or edit contents</small>
                            </div>
                            <div class="d-flex align-items-center gap-2">
                                <span class="text-muted small">Initial Working Directory:</span>
                                <select id="builderInitialDirectory" class="form-select form-select-sm font-monospace">
                                    ${allDirs.map(d => `<option value="${d}" ${d === state.initialDirectory ? 'selected' : ''}>${d}</option>`).join('')}
                                </select>
                            </div>
                        </div>
                        <div class="card-body p-4">
                            <div class="row g-3">
                                <!-- Tree Panel -->
                                <div class="col-md-5">
                                    <div class="builder-tree-panel rounded border p-2" id="fs-tree-container" style="max-height: 480px; overflow-y: auto;">
                                        ${renderFsTree(state.fileSystem, '/')}
                                    </div>
                                    <div class="d-flex gap-2 mt-2">
                                        <button type="button" id="treeNewFolderBtn" class="btn btn-sm btn-outline-secondary">
                                            ${svgIcon('folder', 'me-1')} New Folder
                                        </button>
                                        <button type="button" id="treeNewFileBtn" class="btn btn-sm btn-outline-secondary">
                                            ${svgIcon('file', 'me-1')} New File
                                        </button>
                                    </div>
                                </div>

                                <!-- Node Inspector & Editor Panel -->
                                <div class="col-md-7">
                                    <div class="builder-inspector-panel rounded border p-3" id="fs-inspector-panel">
                                        ${renderInspectorPanel(state.selectedPath)}
                                    </div>
                                </div>
                            </div>
                        </div>
                    </div>

                    <!-- Section 3: Allowed Built-in Commands -->
                    <div class="builder-card mb-4">
                        <div class="builder-card-header">
                            <h5 class="mb-0">3. Built-in Command Permissions</h5>
                            <small class="text-secondary">Select which simulated Linux utilities students are permitted to execute</small>
                        </div>
                        <div class="card-body p-4">
                            <div class="alert alert-info py-2 px-3 mb-3 small d-flex align-items-center gap-2">
                                <span>${svgIcon('terminal', 'me-1')}</span>
                                <div>
                                    <strong>Built-in Core Commands:</strong> <code>help</code> and <code>clear</code> are mandatory system commands and permanently available in every terminal lab.
                                </div>
                            </div>
                            <div class="row g-3">
                                ${COMMAND_CATEGORIES.map(category => `
                                    <div class="col-md-4">
                                        <div class="p-3 builder-well rounded border h-100">
                                            <h6 class="fw-bold mb-2 pb-1 border-bottom text-primary">${escapeHtml(category.name)}</h6>
                                            <div class="d-flex flex-column gap-2 mt-2">
                                                ${category.commands.map(cmd => {
                                                    const isChecked = state.allowedCommands.includes(cmd.id) ? 'checked' : '';
                                                    return `
                                                        <div class="form-check">
                                                            <input class="form-check-input cmd-checkbox" type="checkbox" value="${cmd.id}" id="cmd_${cmd.id}" ${isChecked}>
                                                            <label class="form-check-label font-monospace fw-bold" for="cmd_${cmd.id}">
                                                                ${cmd.label}
                                                            </label>
                                                            <div class="text-muted text-xs">${cmd.desc}</div>
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
                                <small class="text-secondary">Define exact command-response pairs for scenario-specific tools or specialized cybersecurity exercises. Supported commands execute on exact match and can redirect output to virtual files.</small>
                            </div>
                            <button type="button" class="btn btn-sm btn-primary" id="addCustomCommandBtn">
                                ${svgIcon('plus', 'me-1')} Add Custom Command
                            </button>
                        </div>
                        <div class="card-body p-4">
                            <div id="customCommandsListContainer" class="d-flex flex-column gap-2">
                                ${state.customCommands.length === 0 ? `
                                    <div class="text-center py-4 text-muted border border-dashed rounded">
                                        No custom commands defined yet. Click "<strong>+ Add Custom Command</strong>" to configure scenario-specific exact-match commands (e.g. <code>whois target.com</code>).
                                    </div>
                                ` : state.customCommands.map((cc, idx) => renderCustomCommandCard(cc, idx)).join('')}
                            </div>
                        </div>
                    </div>

                    ${isNotebookBlock ? `
                    <!-- Section 5: Notebook Sandbox Info -->
                    <div class="builder-card mb-4">
                        <div class="builder-card-header d-flex justify-content-between align-items-center flex-wrap gap-2">
                            <div>
                                <h5 class="mb-0">5. Notebook Sandbox Environment</h5>
                                <small class="text-secondary">Open exploration and demonstration mode</small>
                            </div>
                            <span class="badge bg-info-subtle text-info border font-monospace small">Open Sandbox</span>
                        </div>
                        <div class="card-body p-4">
                            <div class="alert alert-info py-3 px-3 mb-0 small d-flex align-items-center gap-2">
                                <span>${svgIcon('terminal', 'me-1')}</span>
                                <div>
                                    <strong>Interactive Notebook Block:</strong> Notebook terminal blocks operate as open exploration sandboxes for demonstrations and practice. Lab objectives, flags, and completion tracking are bypassed for notebook blocks.
                                </div>
                            </div>
                        </div>
                    </div>
                    ` : `
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
                            <!-- Sequential Completion Setting -->
                            <div class="form-check form-switch p-3 builder-well rounded border mb-3">
                                <input class="form-check-input" type="checkbox" id="builderSequentialObjectives" ${state.sequentialObjectives !== false ? 'checked' : ''} style="margin-left: 0; margin-right: 0.75rem;" />
                                <label class="form-check-label fw-bold" for="builderSequentialObjectives">
                                    Require objectives to be completed sequentially
                                </label>
                                <div class="text-muted small mt-1">When enabled, students must complete objectives in their configured order. When disabled, objectives may be satisfied in any order.</div>
                            </div>

                            <div id="objectivesListContainer" class="d-flex flex-column gap-3">
                                ${state.objectives.length === 0 ? `
                                    <div class="text-center py-4 text-muted border border-dashed rounded">
                                        No objectives defined yet. Click "<strong>+ Add Task Objective</strong>" to create one.
                                    </div>
                                ` : state.objectives.map((obj, idx) => renderObjectiveCard(obj, idx, allDirs)).join('')}
                            </div>
                        </div>
                    </div>

                    <!-- Publication Status Setting -->
                    <div class="builder-card mb-4">
                        <div class="card-body p-4">
                            <div class="form-check form-switch p-3 builder-well rounded border mb-0">
                                <input class="form-check-input" type="checkbox" id="builderIsPublished" ${isPublishedChecked} style="margin-left: 0; margin-right: 0.75rem;" />
                                <label class="form-check-label fw-bold" for="builderIsPublished">
                                    Publish this lab exercise in the course curriculum
                                </label>
                                <div class="text-muted small mt-1">If unchecked, this simulation will be saved as a private Draft. Incomplete configurations can be saved as drafts.</div>
                            </div>
                        </div>
                    </div>
                    `}

                    <!-- Action Bar -->
                    <div id="validationAlertContainer"></div>
                    <div class="d-flex gap-3 align-items-center mt-3 pt-3 border-top">
                        ${isNotebookBlock ? `
                            <button type="button" id="notebookSaveBlockBtn" class="btn btn-primary">
                                ${svgIcon('save', 'me-1')} Save Configuration to Notebook Block
                            </button>
                            <button type="button" id="livePreviewActionBtn" class="btn btn-outline-primary">
                                ${svgIcon('play', 'me-1')} Test in Live Preview
                            </button>
                            <button type="button" id="notebookCancelBlockBtn" class="btn btn-outline-secondary">
                                Cancel
                            </button>
                        ` : `
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
                        `}
                    </div>
                </div>

                <!-- TAB 2: LIVE TERMINAL PREVIEW -->
                <div id="previewTabContent" style="${state.activeTab === 'preview' ? '' : 'display:none;'}">
                    <div class="alert alert-info d-flex justify-content-between align-items-center mb-3">
                        <div>
                            <strong>${svgIcon('terminal', 'me-1')} Live Sandbox Preview Mode</strong>: Test filesystem navigation, commands, and script execution.
                            <div class="small text-secondary">Activity in preview mode is completely isolated and does not modify saved notebook data.</div>
                        </div>
                        <div class="d-flex gap-2">
                            <button type="button" id="resetPreviewBtn" class="btn btn-sm btn-outline-secondary">
                                ${svgIcon('reset', 'me-1')} Reset Environment
                            </button>
                            <button type="button" id="backToEditorBtn" class="btn btn-sm btn-primary">← Back to Visual Editor</button>
                        </div>
                    </div>

                    ${!isNotebookBlock ? `
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
                    ` : ''}

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

        // Render Tree Nodes recursively
        function renderFsTree(node, currentPath) {
            if (currentPath === '/') {
                const isSelected = state.selectedPath === '/';
                const isExpanded = state.expandedPaths.has('/');
                const caretSvg = isExpanded ? svgIcon('chevron-down', 'me-1') : svgIcon('chevron-right', 'me-1');
                const childrenHtml = isExpanded ? renderTreeChildren(node, '/') : '';

                return `
                    <div class="tree-root">
                        <div class="tree-item d-flex align-items-center py-1 px-2 rounded ${isSelected ? 'selected' : ''}" data-path="/">
                            <span class="tree-caret cursor-pointer" data-path="/">${caretSvg}</span>
                            <span class="tree-icon me-1">${svgIcon('folder')}</span>
                            <span class="tree-name fw-bold">/ (root)</span>
                        </div>
                        <div class="tree-children ps-3">
                            ${childrenHtml}
                        </div>
                    </div>
                `;
            }
            return '';
        }

        function renderTreeChildren(parentObj, parentPath) {
            if (!parentObj || typeof parentObj !== 'object') return '';
            const keys = Object.keys(parentObj).sort((a, b) => {
                const isDirA = typeof parentObj[a] === 'object';
                const isDirB = typeof parentObj[b] === 'object';
                if (isDirA && !isDirB) return -1;
                if (!isDirA && isDirB) return 1;
                return a.localeCompare(b);
            });

            return keys.map(key => {
                const childPath = parentPath === '/' ? `/${key}` : `${parentPath}/${key}`;
                const isDir = typeof parentObj[key] === 'object';
                const isSelected = state.selectedPath === childPath;
                const isExpanded = state.expandedPaths.has(childPath);

                if (isDir) {
                    const caretSvg = isExpanded ? svgIcon('chevron-down', 'me-1') : svgIcon('chevron-right', 'me-1');
                    const childrenHtml = isExpanded ? renderTreeChildren(parentObj[key], childPath) : '';
                    return `
                        <div class="tree-node">
                            <div class="tree-item d-flex align-items-center py-1 px-2 rounded ${isSelected ? 'selected' : ''}" data-path="${childPath}">
                                <span class="tree-caret cursor-pointer" data-path="${childPath}">${caretSvg}</span>
                                <span class="tree-icon me-1">${svgIcon('folder')}</span>
                                <span class="tree-name">${escapeHtml(key)}</span>
                            </div>
                            <div class="tree-children ps-3">
                                ${childrenHtml}
                            </div>
                        </div>
                    `;
                } else {
                    return `
                        <div class="tree-item d-flex align-items-center py-1 px-2 rounded ${isSelected ? 'selected' : ''}" data-path="${childPath}">
                            <span class="tree-spacer" style="display:inline-block; width:16px;"></span>
                            <span class="tree-icon me-1">${svgIcon('file')}</span>
                            <span class="tree-name font-monospace text-xs">${escapeHtml(key)}</span>
                        </div>
                    `;
                }
            }).join('');
        }

        // Render Inspector Panel for selected filesystem item
        function renderInspectorPanel(path) {
            const nodeType = getNodeType(state.fileSystem, path);
            const name = getNameFromPath(path);

            if (path === '/') {
                return `
                    <div class="inspector-header mb-3 pb-2 border-bottom d-flex justify-content-between align-items-center">
                        <div class="d-flex align-items-center gap-2">
                            <span>${svgIcon('folder')}</span>
                            <h6 class="mb-0 font-monospace">/ (Root Directory)</h6>
                        </div>
                    </div>
                    <div class="alert alert-secondary py-2 small">Virtual Root Directory (/) cannot be renamed or deleted.</div>
                    <div class="d-flex gap-2">
                        <button type="button" class="btn btn-sm btn-outline-primary" id="panelAddFolderBtn">
                            ${svgIcon('folder', 'me-1')} Add Subfolder
                        </button>
                        <button type="button" class="btn btn-sm btn-outline-primary" id="panelAddFileBtn">
                            ${svgIcon('file', 'me-1')} Add File
                        </button>
                    </div>
                `;
            }

            if (nodeType === 'dir') {
                return `
                    <div class="inspector-header mb-3 pb-2 border-bottom d-flex justify-content-between align-items-center">
                        <div class="d-flex align-items-center gap-2">
                            <span>${svgIcon('folder')}</span>
                            <h6 class="mb-0 font-monospace">${escapeHtml(path)}</h6>
                        </div>
                        <button type="button" class="btn btn-sm btn-outline-danger" id="panelDeleteBtn" title="Delete Directory">
                            ${svgIcon('trash', 'me-1')} Delete
                        </button>
                    </div>
                    <div class="mb-3">
                        <label class="form-label small text-muted mb-1">Directory Name</label>
                        <div class="input-group input-group-sm">
                            <input type="text" id="panelRenameInput" class="form-control font-monospace" value="${escapeHtml(name)}" />
                            <button type="button" id="panelRenameBtn" class="btn btn-outline-secondary">Rename</button>
                        </div>
                        <div id="renameErrorMsg" class="text-danger small mt-1" style="display:none;"></div>
                    </div>
                    <div class="d-flex gap-2 pt-2 border-top">
                        <button type="button" class="btn btn-sm btn-outline-primary" id="panelAddFolderBtn">
                            ${svgIcon('folder', 'me-1')} Add Subfolder
                        </button>
                        <button type="button" class="btn btn-sm btn-outline-primary" id="panelAddFileBtn">
                            ${svgIcon('file', 'me-1')} Add File
                        </button>
                    </div>
                `;
            }

            if (nodeType === 'file') {
                const parentPath = getParentPath(path);
                const parent = getNode(state.fileSystem, parentPath);
                const content = parent ? parent[name] : "";

                return `
                    <div class="inspector-header mb-3 pb-2 border-bottom d-flex justify-content-between align-items-center">
                        <div class="d-flex align-items-center gap-2">
                            <span>${svgIcon('file')}</span>
                            <h6 class="mb-0 font-monospace">${escapeHtml(path)}</h6>
                        </div>
                        <button type="button" class="btn btn-sm btn-outline-danger" id="panelDeleteBtn" title="Delete File">
                            ${svgIcon('trash', 'me-1')} Delete
                        </button>
                    </div>
                    <div class="mb-3">
                        <label class="form-label small text-muted mb-1">File Name</label>
                        <div class="input-group input-group-sm">
                            <input type="text" id="panelRenameInput" class="form-control font-monospace" value="${escapeHtml(name)}" />
                            <button type="button" id="panelRenameBtn" class="btn btn-outline-secondary">Rename</button>
                        </div>
                        <div id="renameErrorMsg" class="text-danger small mt-1" style="display:none;"></div>
                    </div>
                    <div class="mb-3">
                        <div class="d-flex justify-content-between align-items-center mb-1">
                            <label class="form-label small text-muted mb-0">File Content (Plain text / Logs / Scripts)</label>
                            <span class="text-muted small">${content.length} bytes</span>
                        </div>
                        <textarea id="panelFileContentTextarea" class="form-control font-monospace text-xs" rows="8">${escapeHtml(content)}</textarea>
                    </div>
                `;
            }

            return `<div class="text-muted small">Select an item from the tree to inspect and edit.</div>`;
        }

        // Render Custom Command Card
        function renderCustomCommandCard(cc, idx) {
            const isFirst = idx === 0;
            const isLast = idx === state.customCommands.length - 1;
            const isEnabled = cc.enabled !== false;
            const isAdvancedOpen = cc._advancedOpen === true;

            const reqFilesList = Array.isArray(cc.requiredFiles)
                ? cc.requiredFiles
                : (typeof cc.requiredFiles === 'string' ? cc.requiredFiles.split(/[\r\n,]+/).map(s => s.trim()).filter(Boolean) : []);
            const reqFilesText = reqFilesList.join('\n');
            const hasAdvancedSettings = reqFilesList.length > 0 || !!(cc.description && cc.description.trim()) || !!(cc.hint && cc.hint.trim());

            return `
                <div class="custom-cmd-card p-3 rounded mb-2 border ${isEnabled ? '' : 'opacity-75'}" data-idx="${idx}">
                    <div class="d-flex justify-content-between align-items-center mb-2 pb-2 border-bottom flex-wrap gap-2">
                        <div class="d-flex align-items-center gap-2">
                            <span class="badge ${isEnabled ? 'bg-primary' : 'bg-secondary'}">Custom Command #${idx + 1}</span>
                            <span class="font-monospace text-muted small">ID: ${escapeHtml(cc.id || '')}</span>
                        </div>
                        <div class="d-flex gap-1 align-items-center">
                            <div class="form-check form-switch mb-0 me-2">
                                <input class="form-check-input cc-enable-toggle" type="checkbox" data-idx="${idx}" ${isEnabled ? 'checked' : ''} title="Toggle Command Enabled">
                            </div>
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

                    <!-- 2.1 Always-visible Essential Fields -->
                    <div class="row g-2 mb-2">
                        <div class="col-md-12">
                            <label class="form-label small text-muted mb-1">Exact Command String <span class="text-danger">*</span></label>
                            <input type="text" class="form-control form-control-sm font-monospace cc-command-input" data-idx="${idx}" value="${escapeHtml(cc.command)}" placeholder="e.g. python main.py" maxlength="512" required />
                            <div class="form-text small">Must be matched exactly by the student (case-sensitive, order of arguments preserved).</div>
                        </div>

                        <div class="col-md-12">
                            <label class="form-label small text-muted mb-1">Predefined Simulated Output <span class="text-danger">*</span></label>
                            <textarea class="form-control form-control-sm font-monospace cc-output-input" data-idx="${idx}" rows="3" placeholder="Simulated CLI response returned when this exact command is submitted...">${escapeHtml(cc.output)}</textarea>
                            <div class="form-text small">This simulated text is printed to the terminal or written to file when redirection (>) is used.</div>
                        </div>
                    </div>

                    <!-- 2.2 Collapsible Advanced Options -->
                    <div class="pt-2 border-top">
                        <button type="button" class="btn btn-sm btn-link text-decoration-none p-0 d-inline-flex align-items-center gap-1 cc-advanced-toggle" data-idx="${idx}">
                            <svg class="cc-advanced-chevron" viewBox="0 0 24 24" width="13" height="13" fill="none" stroke="currentColor" stroke-width="2.5" style="transition: transform 180ms ease; ${isAdvancedOpen ? 'transform: rotate(90deg);' : ''}">
                                <polyline points="9 18 15 12 9 6"></polyline>
                            </svg>
                            <span class="fw-semibold small">Advanced Options</span>
                            ${hasAdvancedSettings ? `<span class="badge bg-secondary-subtle text-secondary ms-1 font-monospace" style="font-size:0.65rem;">Configured</span>` : ''}
                        </button>

                        <div class="cc-advanced-pane mt-2 ${isAdvancedOpen ? '' : 'd-none'}" id="cc-advanced-pane-${idx}">
                            <div class="row g-2">
                                <div class="col-md-12">
                                    <label class="form-label small text-muted mb-1">Required Files and Directories</label>
                                    <textarea class="form-control form-control-sm font-monospace cc-required-files-input" data-idx="${idx}" rows="2" placeholder="e.g. main.py&#10;src/utils.py&#10;data/">${escapeHtml(reqFilesText)}</textarea>
                                    <div class="form-text small">
                                        Optional files or directories that must exist in the simulated current working directory for this command to succeed. End with a slash (e.g. <code>data/</code>) to explicitly require a directory. Multiple entries must be separated by newlines or commas.
                                    </div>
                                </div>

                                <div class="col-md-6">
                                    <label class="form-label small text-muted mb-1">Teacher Description (Optional)</label>
                                    <input type="text" class="form-control form-control-sm cc-desc-input" data-idx="${idx}" value="${escapeHtml(cc.description || '')}" placeholder="e.g. Query domain registration records" />
                                </div>

                                <div class="col-md-6">
                                    <label class="form-label small text-muted mb-1">Student Hint (Optional)</label>
                                    <input type="text" class="form-control form-control-sm cc-hint-input" data-idx="${idx}" value="${escapeHtml(cc.hint || '')}" placeholder="e.g. Try using whois" />
                                </div>
                            </div>
                        </div>
                    </div>
                </div>
            `;
        }

        // Render Objective Card
        function renderObjectiveCard(obj, idx, allDirs) {
            const isFirst = idx === 0;
            const isLast = idx === state.objectives.length - 1;

            let warnings = [];
            if (!obj.command || !obj.command.trim()) {
                warnings.push(`<span class="badge bg-danger">${svgIcon('warning', 'me-1')} Expected command is required</span>`);
            }
            if (obj.executionDirectory && !allDirs.includes(normalizePath(obj.executionDirectory))) {
                warnings.push(`<span class="badge bg-danger">${svgIcon('warning', 'me-1')} Directory '${escapeHtml(obj.executionDirectory)}' does not exist</span>`);
            }

            return `
                <div class="objective-card p-3 rounded mb-3 border" data-idx="${idx}">
                    <div class="d-flex justify-content-between align-items-center mb-2 pb-2 border-bottom flex-wrap gap-2">
                        <div class="d-flex align-items-center gap-2">
                            <span class="badge bg-secondary">Step #${idx + 1}</span>
                            <span class="font-monospace text-muted small">ID: ${escapeHtml(obj.id)}</span>
                            ${warnings.join(' ')}
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
                        <div class="col-md-12">
                            <label class="form-label small text-muted mb-1">Objective Description (Student-facing) <span class="text-danger">*</span></label>
                            <input type="text" class="form-control form-control-sm obj-text-input" data-idx="${idx}" value="${escapeHtml(obj.text)}" placeholder="e.g. Inspect the system authentication log" required />
                        </div>

                        <div class="col-md-7">
                            <label class="form-label small text-muted mb-1">Expected Command <span class="text-danger">*</span></label>
                            <input type="text" class="form-control form-control-sm font-monospace obj-command-input" data-idx="${idx}" value="${escapeHtml(obj.command || '')}" placeholder="e.g. cat /var/log/auth.log" required />
                            <div class="form-text small">Exact command expression with arguments student must execute.</div>
                        </div>

                        <div class="col-md-5">
                            <label class="form-label small text-muted mb-1">Required Execution Directory (Optional)</label>
                            <select class="form-select form-select-sm font-monospace obj-execdir-select" data-idx="${idx}">
                                <option value="" ${!obj.executionDirectory ? 'selected' : ''}>Any Working Directory</option>
                                ${allDirs.map(d => `<option value="${d}" ${d === obj.executionDirectory ? 'selected' : ''}>${d}</option>`).join('')}
                            </select>
                            <div class="form-text small">Leave as 'Any' or choose directory where command must run.</div>
                        </div>

                        <div class="col-md-12">
                            <label class="form-label small text-muted mb-1">Student Hint (Optional)</label>
                            <input type="text" class="form-control form-control-sm obj-hint-input" data-idx="${idx}" value="${escapeHtml(obj.hint || '')}" placeholder="e.g. Try running 'cat /var/log/auth.log'" />
                            <div class="form-text small">Printed once to terminal when this objective becomes active.</div>
                        </div>
                    </div>
                </div>
            `;
        }

        // Attach DOM Event Listeners
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

            // Live Preview Action Button
            document.getElementById("livePreviewActionBtn")?.addEventListener("click", () => {
                startLivePreview();
            });

            // Reset Preview Button
            document.getElementById("resetPreviewBtn")?.addEventListener("click", () => {
                if (typeof window.resetSimulationAttempt === 'function') {
                    window.resetSimulationAttempt();
                }
                try {
                    sessionStorage.removeItem('hackacad_introduced_hints_sim-builder-preview-terminal');
                } catch (e) { }
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
                    if (Array.isArray(parsed.allowedCommands)) {
                        state.allowedCommands = parsed.allowedCommands
                            .map(c => c.toLowerCase().trim())
                            .filter(c => ALL_SUPPORTED_COMMANDS.includes(c));
                    }
                    if (parsed.initialDirectory) state.initialDirectory = parsed.initialDirectory;
                    if (typeof parsed.sequentialObjectives === 'boolean') state.sequentialObjectives = parsed.sequentialObjectives;
                    if (Array.isArray(parsed.customCommands)) state.customCommands = parsed.customCommands;
                    if (Array.isArray(parsed.objectives)) {
                        state.objectives = parsed.objectives.map((o, idx) => {
                            let cmd = (o.command || o.expectedCommand || '').trim();
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
                                executionDirectory: o.executionDirectory ? normalizePath(o.executionDirectory) : '',
                                hint: o.hint || ''
                            };
                        });
                    }
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

            document.getElementById("builderIsPublished")?.addEventListener("change", (e) => {
                state.isPublished = e.target.checked;
                const formPublished = document.getElementById("isPublishedCheck");
                if (formPublished) formPublished.value = state.isPublished ? "true" : "false";
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
                item.addEventListener("click", (e) => {
                    if (e.target.closest('.tree-caret')) return;
                    state.selectedPath = item.getAttribute("data-path");
                    render();
                });
            });

            // Initial Directory Selector
            document.getElementById("builderInitialDirectory")?.addEventListener("change", (e) => {
                state.initialDirectory = e.target.value;
                syncStateToInput();
            });

            // Tree Panel Toolbar Buttons
            document.getElementById("treeNewFolderBtn")?.addEventListener("click", () => {
                promptAddFolder(state.selectedPath);
            });
            document.getElementById("treeNewFileBtn")?.addEventListener("click", () => {
                promptAddFile(state.selectedPath);
            });

            // Inspector Panel Buttons
            document.getElementById("panelAddFolderBtn")?.addEventListener("click", () => {
                promptAddFolder(state.selectedPath);
            });
            document.getElementById("panelAddFileBtn")?.addEventListener("click", () => {
                promptAddFile(state.selectedPath);
            });

            function promptAddFolder(targetPath) {
                const parentDir = getNodeType(state.fileSystem, targetPath) === 'dir' ? targetPath : getParentPath(targetPath);
                const folderName = prompt(`Enter folder name to create inside ${parentDir}:`);
                if (folderName) {
                    const res = addFolder(parentDir, folderName);
                    if (!res.success) alert(res.error);
                }
            }

            function promptAddFile(targetPath) {
                const parentDir = getNodeType(state.fileSystem, targetPath) === 'dir' ? targetPath : getParentPath(targetPath);
                const fileName = prompt(`Enter file name to create inside ${parentDir}:`);
                if (fileName) {
                    const res = addFile(parentDir, fileName, "");
                    if (!res.success) alert(res.error);
                }
            }

            document.getElementById("panelRenameBtn")?.addEventListener("click", () => {
                const newName = document.getElementById("panelRenameInput")?.value;
                if (newName) {
                    const res = renameItem(state.selectedPath, newName);
                    if (!res.success) {
                        const errEl = document.getElementById("renameErrorMsg");
                        if (errEl) {
                            errEl.textContent = res.error;
                            errEl.style.display = "block";
                        }
                    }
                }
            });

            document.getElementById("panelDeleteBtn")?.addEventListener("click", () => {
                if (confirm(`Are you sure you want to delete "${state.selectedPath}"?`)) {
                    const res = deleteItem(state.selectedPath);
                    if (!res.success) alert(res.error);
                }
            });

            document.getElementById("panelFileContentTextarea")?.addEventListener("input", (e) => {
                updateFileContent(state.selectedPath, e.target.value);
            });

            // Section 3: Allowed Commands Checkboxes
            rootContainer.querySelectorAll(".cmd-checkbox").forEach(chk => {
                chk.addEventListener("change", () => {
                    const cmdId = chk.value;
                    if (chk.checked) {
                        if (!state.allowedCommands.includes(cmdId)) state.allowedCommands.push(cmdId);
                    } else {
                        state.allowedCommands = state.allowedCommands.filter(c => c !== cmdId);
                    }
                    ensureMandatoryCommands();
                    syncStateToInput();
                });
            });

            // Section 4: Custom Commands Listeners
            document.getElementById("addCustomCommandBtn")?.addEventListener("click", () => {
                addCustomCommand();
            });

            rootContainer.querySelectorAll(".cc-enable-toggle").forEach(chk => {
                chk.addEventListener("change", () => {
                    const idx = parseInt(chk.getAttribute("data-idx"), 10);
                    updateCustomCommand(idx, "enabled", chk.checked);
                    render();
                });
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
                    removeCustomCommand(idx);
                });
            });

            rootContainer.querySelectorAll(".cc-command-input").forEach(inp => {
                const handler = () => {
                    const idx = parseInt(inp.getAttribute("data-idx"), 10);
                    updateCustomCommand(idx, "command", inp.value);
                };
                inp.addEventListener("change", handler);
                inp.addEventListener("input", handler);
            });

            rootContainer.querySelectorAll(".cc-output-input").forEach(inp => {
                const handler = () => {
                    const idx = parseInt(inp.getAttribute("data-idx"), 10);
                    updateCustomCommand(idx, "output", inp.value);
                };
                inp.addEventListener("change", handler);
                inp.addEventListener("input", handler);
            });

            // Collapsible Advanced Options Toggle
            rootContainer.querySelectorAll(".cc-advanced-toggle").forEach(btn => {
                btn.addEventListener("click", () => {
                    const idx = parseInt(btn.getAttribute("data-idx"), 10);
                    const pane = document.getElementById(`cc-advanced-pane-${idx}`);
                    const chevron = btn.querySelector(".cc-advanced-chevron");
                    if (pane) {
                        const isHidden = pane.classList.contains("d-none");
                        if (isHidden) {
                            pane.classList.remove("d-none");
                            if (chevron) chevron.style.transform = "rotate(90deg)";
                            if (state.customCommands[idx]) state.customCommands[idx]._advancedOpen = true;
                        } else {
                            pane.classList.add("d-none");
                            if (chevron) chevron.style.transform = "";
                            if (state.customCommands[idx]) state.customCommands[idx]._advancedOpen = false;
                        }
                    }
                });
            });

            rootContainer.querySelectorAll(".cc-required-files-input").forEach(inp => {
                const handler = () => {
                    const idx = parseInt(inp.getAttribute("data-idx"), 10);
                    const lines = inp.value.split(/[\r\n,]+/).map(s => s.trim()).filter(Boolean);
                    updateCustomCommand(idx, "requiredFiles", lines);
                };
                inp.addEventListener("change", handler);
                inp.addEventListener("input", handler);
            });

            rootContainer.querySelectorAll(".cc-desc-input").forEach(inp => {
                const handler = () => {
                    const idx = parseInt(inp.getAttribute("data-idx"), 10);
                    updateCustomCommand(idx, "description", inp.value);
                };
                inp.addEventListener("change", handler);
                inp.addEventListener("input", handler);
            });

            rootContainer.querySelectorAll(".cc-hint-input").forEach(inp => {
                const handler = () => {
                    const idx = parseInt(inp.getAttribute("data-idx"), 10);
                    updateCustomCommand(idx, "hint", inp.value);
                };
                inp.addEventListener("change", handler);
                inp.addEventListener("input", handler);
            });

            // Section 5: Objective Listeners
            document.getElementById("builderSequentialObjectives")?.addEventListener("change", (e) => {
                state.sequentialObjectives = e.target.checked;
                syncStateToInput();
            });

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
                    removeObjective(idx);
                });
            });

            rootContainer.querySelectorAll(".obj-text-input").forEach(inp => {
                inp.addEventListener("change", () => {
                    const idx = parseInt(inp.getAttribute("data-idx"), 10);
                    updateObjective(idx, "text", inp.value);
                });
            });

            rootContainer.querySelectorAll(".obj-command-input").forEach(inp => {
                inp.addEventListener("change", () => {
                    const idx = parseInt(inp.getAttribute("data-idx"), 10);
                    updateObjective(idx, "command", inp.value);
                });
            });

            rootContainer.querySelectorAll(".obj-execdir-select").forEach(sel => {
                sel.addEventListener("change", () => {
                    const idx = parseInt(sel.getAttribute("data-idx"), 10);
                    updateObjective(idx, "executionDirectory", sel.value);
                });
            });

            rootContainer.querySelectorAll(".obj-hint-input").forEach(inp => {
                inp.addEventListener("change", () => {
                    const idx = parseInt(inp.getAttribute("data-idx"), 10);
                    updateObjective(idx, "hint", inp.value);
                });
            });

            // Action Buttons: Save Draft, Preview, Publish
            document.getElementById("notebookSaveBlockBtn")?.addEventListener("click", () => {
                const bTitle = document.getElementById("builderTitle");
                if (bTitle) {
                    state.title = bTitle.value;
                    const fTitle = document.getElementById("Title");
                    if (fTitle) fTitle.value = state.title;
                }
                const bDesc = document.getElementById("builderDescription");
                if (bDesc) {
                    state.description = bDesc.value;
                    const fDesc = document.getElementById("Description");
                    if (fDesc) fDesc.value = state.description;
                }
                syncStateToInput();
                if (typeof options.onSave === 'function') {
                    options.onSave(serializeConfiguration());
                } else if (formEl) {
                    formEl.submit();
                }
            });

            document.getElementById("notebookCancelBlockBtn")?.addEventListener("click", () => {
                if (typeof options.onCancel === 'function') {
                    options.onCancel();
                } else if (options.cancelUrl) {
                    window.location.href = options.cancelUrl;
                }
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

            const isPubInput = document.getElementById("isPublishedCheck");
            if (isPubInput) isPubInput.value = state.isPublished ? "true" : "false";

            if (forPublish) {
                const errors = validateForPublish();
                const alertContainer = document.getElementById("validationAlertContainer");
                if (errors.length > 0) {
                    if (alertContainer) {
                        alertContainer.innerHTML = `
                            <div class="alert alert-danger mb-3">
                                <h6 class="alert-heading fw-bold">Cannot Publish Simulation:</h6>
                                <ul class="mb-0 ps-3">
                                    ${errors.map(err => `<li>${escapeHtml(err)}</li>`).join('')}
                                </ul>
                            </div>
                        `;
                        alertContainer.scrollIntoView({ behavior: 'smooth' });
                    }
                    return;
                }
            }

            if (formEl) formEl.submit();
        }

        // Initial sync and render
        syncStateToInput();
        render();
    };
})();
