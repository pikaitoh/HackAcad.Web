const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

// Mock DOM elements
class Element {
    constructor(id = '', tagName = 'div') {
        this.id = id;
        this.tagName = tagName.toUpperCase();
        this.innerHTML = '';
        this.value = '';
        this.checked = false;
        this.style = {};
        this.children = [];
        this.attributes = {};
        this.listeners = {};
        this.classList = {
            add: (c) => this.classes.add(c),
            remove: (c) => this.classes.delete(c),
            contains: (c) => this.classes.has(c)
        };
        this.classes = new Set();
    }
    setAttribute(name, val) { this.attributes[name] = val; }
    getAttribute(name) { return this.attributes[name] || null; }
    addEventListener(evt, fn) {
        if (!this.listeners[evt]) this.listeners[evt] = [];
        this.listeners[evt].push(fn);
    }
    trigger(evt, eventObj = {}) {
        if (this.listeners[evt]) {
            this.listeners[evt].forEach(fn => fn(eventObj));
        }
    }
    appendChild(child) { this.children.push(child); }
    querySelectorAll(selector) {
        const results = [];
        function traverse(node) {
            if (selector.startsWith('.')) {
                const cls = selector.slice(1);
                if (node.classes && node.classes.has(cls)) results.push(node);
            } else if (selector.startsWith('#')) {
                const id = selector.slice(1);
                if (node.id === id) results.push(node);
            }
            if (node.children) {
                node.children.forEach(traverse);
            }
        }
        traverse(this);
        return results;
    }
    querySelector(selector) {
        const all = this.querySelectorAll(selector);
        return all.length > 0 ? all[0] : null;
    }
}

// Set up virtual DOM
const elements = {
    'simulation-builder-mount': new Element('simulation-builder-mount'),
    'simulationForm': new Element('simulationForm', 'form'),
    'VirtualFileSystemJson': new Element('VirtualFileSystemJson', 'input'),
    'Title': new Element('Title', 'input'),
    'Description': new Element('Description', 'input'),
    'Difficulty': new Element('Difficulty', 'input'),
    'Instructions': new Element('Instructions', 'textarea'),
    'isPublishedCheck': new Element('isPublishedCheck', 'input')
};

const context = vm.createContext({
    window: {},
    document: {
        getElementById: id => elements[id] || null,
        createElement: (tag) => new Element('', tag)
    },
    console,
    navigator: { clipboard: { writeText: () => {} } },
    alert: (msg) => { console.log('[Mock Alert]:', msg); },
    confirm: (msg) => true,
    prompt: (msg) => "new_folder"
});

// Load simulation-builder.js into context
const builderCode = fs.readFileSync(path.join(__dirname, '../wwwroot/js/simulation-builder.js'), 'utf8');
vm.runInContext(builderCode, context);

assert(typeof context.window.initSimulationBuilder === 'function', 'initSimulationBuilder should be exposed');

// Initialize the builder
context.window.initSimulationBuilder({
    containerId: "simulation-builder-mount",
    formId: "simulationForm",
    hiddenJsonInputId: "VirtualFileSystemJson",
    initialTitle: "Linux Lab Test",
    initialDescription: "Lab test description",
    initialDifficulty: "Beginner",
    initialInstructions: "Follow instructions",
    initialIsPublished: true
});

// Verify state synced to JSON
const initialJson = elements['VirtualFileSystemJson'].value;
assert(initialJson, 'Hidden input should be populated with initial JSON');
const parsed = JSON.parse(initialJson);

// Verify filesystem structure
assert(parsed.fileSystem.home, 'Template must have /home');
assert(parsed.fileSystem.home.student, 'Template must have /home/student');
assert(parsed.fileSystem.etc, 'Template must have /etc');
assert(parsed.fileSystem.tmp, 'Template must have /tmp');
assert(parsed.fileSystem.var.log, 'Template must have /var/log');

// Verify initial directory
assert.equal(parsed.initialDirectory, '/home/student');

// Verify allowed commands contain 13 configurable + mandatory
assert(parsed.allowedCommands.includes('pwd'));
assert(parsed.allowedCommands.includes('cd'));
assert(parsed.allowedCommands.includes('cat'));
assert(parsed.allowedCommands.includes('clear'));
assert(parsed.allowedCommands.includes('help'));
assert(parsed.allowedCommands.includes('touch'), 'Template should include touch in allowed commands');
assert(parsed.allowedCommands.includes('nano'), 'Template should include nano in allowed commands');

// Obsolete commands must NOT be in allowedCommands
assert(!parsed.allowedCommands.includes('ping'), 'ping must not be in allowed commands');
assert(!parsed.allowedCommands.includes('nmap'), 'nmap must not be in allowed commands');
assert(!parsed.allowedCommands.includes('curl'), 'curl must not be in allowed commands');
assert(!parsed.allowedCommands.includes('base64'), 'base64 must not be in allowed commands');

// Verify custom commands array is initialized
assert(Array.isArray(parsed.customCommands), 'customCommands must be an array');

// Verify sequentialObjectives setting
assert.equal(parsed.sequentialObjectives, true, 'Default sequentialObjectives should be true');

// Verify objectives use Run Command with expected command
assert(parsed.objectives.length >= 2, 'Template must have objectives');
assert(parsed.objectives.some(o => o.command === 'cd /var/log'), 'Template has cd /var/log objective');
assert(parsed.objectives.some(o => o.command === 'cat auth.log' && o.executionDirectory === '/var/log'), 'Template has cat auth.log with executionDirectory');

// Verify no emoji icons exist in rendered HTML
const mountHtml = elements['simulation-builder-mount'].innerHTML;
const emojiRegex = /[\u{1F300}-\u{1F6FF}\u{2600}-\u{26FF}\u{2700}-\u{27BF}]/u;
assert(!emojiRegex.test(mountHtml), 'Builder interface must not contain emoji icons');
assert(mountHtml.includes('/images/folder.svg'), 'Builder should use folder.svg');
assert(mountHtml.includes('/images/terminal.svg'), 'Builder should use terminal.svg');
assert(mountHtml.includes('/images/settings.svg'), 'Builder should use settings.svg');

// Verify help and clear are NOT rendered as configurable checkboxes
assert(!mountHtml.includes('id="cmd_help"'), 'help must not be a configurable checkbox');
assert(!mountHtml.includes('id="cmd_clear"'), 'clear must not be a configurable checkbox');
assert(mountHtml.includes('id="cmd_touch"'), 'touch must be a configurable checkbox');
assert(mountHtml.includes('id="cmd_nano"'), 'nano must be a configurable checkbox');

// Verify obsolete networking commands are NOT rendered
assert(!mountHtml.includes('id="cmd_ping"'), 'ping checkbox must be removed');
assert(!mountHtml.includes('id="cmd_nmap"'), 'nmap checkbox must be removed');
assert(!mountHtml.includes('id="cmd_curl"'), 'curl checkbox must be removed');
assert(!mountHtml.includes('id="cmd_base64"'), 'base64 checkbox must be removed');
assert(!mountHtml.includes('Networking &amp; Encoding Utilities'), 'Networking section must be removed');

// Verify Completion Reward section is removed while publication checkbox is retained
assert(!mountHtml.includes('builderRewardFlag'), 'builderRewardFlag field must be removed');
assert(mountHtml.includes('Publish this lab exercise in the course curriculum'), 'Publication checkbox label must be retained');
assert(mountHtml.includes('If unchecked, this simulation will be saved as a private Draft'), 'Publication checkbox description must be retained');

// Verify Lab Objectives simplified UI
assert(!mountHtml.includes('data-field="type"'), 'Verification type dropdown must be removed');
assert(mountHtml.includes('Expected Command'), 'Expected Command label must be present');
assert(mountHtml.includes('Required Execution Directory'), 'Required Execution Directory label must be present');
assert(mountHtml.includes('id="builderSequentialObjectives"'), 'Sequential objectives toggle must be present');

// Test Notebook Block Mode (isNotebookBlock: true)
elements['simulation-builder-mount'].innerHTML = '';
elements['VirtualFileSystemJson'].value = JSON.stringify({
    allowedCommands: ['pwd', 'ls', 'cd', 'cat', 'echo', 'touch', 'nano'],
    initialDirectory: '/home/student',
    fileSystem: { home: { student: { 'demo.txt': 'Sample' } } },
    customCommands: [{ command: 'git status', output: 'On branch main' }]
});

context.window.initSimulationBuilder({
    containerId: "simulation-builder-mount",
    formId: "simulationForm",
    hiddenJsonInputId: "VirtualFileSystemJson",
    isNotebookBlock: true,
    initialTitle: "Demo Sandbox Block",
    initialDescription: "Sandbox for live demonstrations"
});

const nbMountHtml = elements['simulation-builder-mount'].innerHTML;
assert(!emojiRegex.test(nbMountHtml), 'Notebook builder interface must not contain emoji icons');
assert(nbMountHtml.includes('Demonstration Metadata'), 'Notebook builder must include metadata section');
assert(nbMountHtml.includes('Notebook Sandbox Environment'), 'Notebook builder must include Section 5 sandbox info');
assert(nbMountHtml.includes('id="notebookSaveBlockBtn"'), 'Notebook builder must render Save Configuration to Notebook Block button');
assert(nbMountHtml.includes('id="notebookCancelBlockBtn"'), 'Notebook builder must render Cancel button');
assert(!nbMountHtml.includes('builderSequentialObjectives'), 'Notebook builder should not display course sequential objectives toggle');

console.log('PASS Visual Simulation Builder initialization, SVG icons, touch/nano commands, clean commands, simplified objectives, and notebook block sandbox mode');
