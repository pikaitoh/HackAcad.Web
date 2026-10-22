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
    'RewardFlag': new Element('RewardFlag', 'input'),
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
    initialRewardFlag: "flag{test_123}",
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

// Verify allowed commands
assert(parsed.allowedCommands.includes('pwd'));
assert(parsed.allowedCommands.includes('cd'));
assert(parsed.allowedCommands.includes('cat'));
assert(parsed.allowedCommands.includes('clear'));
assert(parsed.allowedCommands.includes('help'));

// Verify objectives
assert(parsed.objectives.length >= 2, 'Template must have objectives');
assert(parsed.objectives.some(o => o.type === 'cwd' && o.target === '/var/log'));
assert(parsed.objectives.some(o => o.type === 'cat' && o.target === 'auth.log'));

console.log('PASS Visual Simulation Builder initialization and Linux template structure');
