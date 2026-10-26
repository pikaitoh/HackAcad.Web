const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

// Mock DOM
const elements = {};
class Element {
    constructor(id = '', tag = 'div') {
        this.id = id;
        this.tagName = tag.toUpperCase();
        this._innerHTML = '';
        this.textContent = '';
        this.value = '';
        this.style = {};
        this.children = [];
        this.listeners = {};
        this.classes = new Set();
        this.classList = {
            add: (c) => this.classes.add(c),
            remove: (c) => this.classes.delete(c),
            contains: (c) => this.classes.has(c)
        };
        if (id) elements[id] = this;
    }
    get innerHTML() { return this._innerHTML; }
    set innerHTML(val) {
        this._innerHTML = val;
        this.children = [];
        if (!val) return;
        const tagRegex = /<([a-z0-9]+)([^>]*)>/gi;
        let match;
        while ((match = tagRegex.exec(val)) !== null) {
            const tagName = match[1];
            const attrs = match[2];
            const idM = attrs.match(/id="([^"]+)"/);
            const classM = attrs.match(/class="([^"]+)"/);
            if (idM || classM) {
                const el = new Element(idM ? idM[1] : '', tagName);
                if (classM) {
                    classM[1].split(/\s+/).forEach(c => el.classes.add(c));
                }
                el.parent = this;
                this.children.push(el);
                if (idM) elements[idM[1]] = el;
            }
        }
    }
    addEventListener(name, callback) {
        if (!this.listeners[name]) this.listeners[name] = [];
        this.listeners[name].push(callback);
    }
    trigger(name, evt = {}) {
        if (this.listeners[name]) {
            this.listeners[name].forEach(fn => fn(evt));
        }
    }
    appendChild(child) {
        child.parent = this;
        this.children.push(child);
    }
    remove() {
        if (this.parent && this.parent.children) {
            const idx = this.parent.children.indexOf(this);
            if (idx !== -1) this.parent.children.splice(idx, 1);
        }
    }
    focus() {}
    querySelector(selector) {
        function search(node) {
            if (selector.startsWith('#')) {
                const id = selector.slice(1);
                if (node.id === id) return node;
            } else if (selector.startsWith('.')) {
                const cls = selector.slice(1);
                if (node.classes && node.classes.has(cls)) return node;
            } else if (node.tagName && node.tagName.toLowerCase() === selector.toLowerCase()) {
                return node;
            }
            if (node.children) {
                for (const child of node.children) {
                    const found = search(child);
                    if (found) return found;
                }
            }
            return null;
        }
        return search(this);
    }
    querySelectorAll(selector) {
        const results = [];
        function search(node) {
            if (selector.startsWith('.')) {
                const cls = selector.slice(1);
                if (node.classes && node.classes.has(cls)) results.push(node);
            }
            if (node.children) {
                for (const child of node.children) {
                    search(child);
                }
            }
        }
        search(this);
        return results;
    }
}

const sessionStorage = {
    _data: {},
    getItem: (k) => sessionStorage._data[k] || null,
    setItem: (k, v) => { sessionStorage._data[k] = String(v); },
    removeItem: (k) => { delete sessionStorage._data[k]; },
    clear: () => { sessionStorage._data = {}; }
};

const document = {
    getElementById: (id) => elements[id] || null,
    createElement: (tag) => new Element('', tag),
    body: new Element('body', 'body')
};

const window = {
    document,
    sessionStorage,
    addEventListener: () => {},
    setTimeout: (fn) => fn(),
    clearTimeout: () => {}
};

const context = vm.createContext({
    window,
    document,
    console,
    sessionStorage,
    Element,
    setTimeout: (fn) => fn(),
    clearTimeout: () => {}
});

// Load simulation-terminal.js
const terminalCode = fs.readFileSync(path.join(__dirname, '../wwwroot/js/simulation-terminal.js'), 'utf8');
vm.runInContext(terminalCode, context);

assert(typeof window.initSimulationTerminal === 'function', 'initSimulationTerminal must be exposed on window');
assert(typeof window.resetSimulationTerminal === 'function', 'resetSimulationTerminal must be exposed on window');

console.log('Testing Notebook Mode with isNotebook: true...');

// Setup container 1
const container1 = new Element('term-notebook-1');
elements['term-notebook-1'] = container1;

const config1 = {
    isNotebook: true,
    initialDirectory: '/home/student',
    allowedCommands: ['help', 'clear', 'pwd', 'ls', 'cd', 'cat', 'touch', 'echo', 'whoami'],
    fileSystem: {
        home: {
            student: {
                'notes.txt': 'Notebook sandbox notes.'
            }
        }
    },
    customCommands: [
        {
            command: 'sysinfo',
            output: 'Kernel: Linux 6.8.0-hackacad x86_64\nUptime: 42 days',
            description: 'Print simulated system info'
        }
    ]
};

window.initSimulationTerminal('term-notebook-1', config1);

// Verify container initialized
assert(container1.children.length > 0, 'Container 1 should render terminal elements');
assert(typeof container1.resetTerminal === 'function', 'Container must have resetTerminal attached');

function enterCommand(cmd) {
    const input = elements['term-input'];
    assert(input, 'term-input must exist');
    input.value = cmd;
    input.listeners.keydown.forEach(fn => fn({ key: 'Enter' }));
}

function getLatestOutput() {
    const out = elements['term-output'];
    if (!out) return '';
    return out.children.map(n => n.innerHTML).join('\n');
}

// 1. Verify no objectives banner/hints are required in notebook mode
let out = getLatestOutput();
assert(!out.includes('💡 Hint'), 'Notebook mode should not inject lab objectives hints');

// 2. Test running commands in sandbox
enterCommand('pwd');
out = getLatestOutput();
assert(out.includes('/home/student'), 'pwd should output /home/student');

enterCommand('ls');
out = getLatestOutput();
assert(out.includes('notes.txt'), 'ls should show notes.txt');

// 3. Test exact custom command
enterCommand('sysinfo');
out = getLatestOutput();
assert(out.includes('Kernel: Linux 6.8.0-hackacad'), 'sysinfo custom command should execute');

// 4. Test touch command in sandbox
enterCommand('touch demo.md');
enterCommand('ls');
out = getLatestOutput();
assert(out.includes('demo.md'), 'demo.md should be created and listed');

// 5. Test container reset via window.resetSimulationTerminal
window.resetSimulationTerminal('term-notebook-1');
assert(container1.children.length > 0, 'Container 1 should be re-rendered after reset');

console.log('PASS Notebook Terminal: isNotebook mode, execution, custom command, and reset successfully verified!');
