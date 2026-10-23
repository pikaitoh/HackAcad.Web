const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

// Mock DOM Element
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
            }
            for (const ch of node.children || []) {
                const res = search(ch);
                if (res) return res;
            }
            return null;
        }
        const found = search(this);
        if (found) return found;
        if (selector.startsWith('#') && elements[selector.slice(1)]) return elements[selector.slice(1)];
        return null;
    }
}

const elements = {
    'terminal': new Element('terminal'),
    'term-output': new Element('term-output'),
    'term-input': new Element('term-input', 'input'),
    'term-prompt': new Element('term-prompt'),
    'simulation-objectives-list': new Element('simulation-objectives-list'),
    'simulation-success-banner': new Element('simulation-success-banner')
};

// Also give terminal container querySelector capability for .terminal-input-row, etc.
const inputRow = new Element('terminal-input-row');
inputRow.classes.add('terminal-input-row');
elements['terminal'].children.push(inputRow);
const termWrapper = new Element('terminal-wrapper');
termWrapper.classes.add('terminal-wrapper');
elements['terminal'].children.push(termWrapper);

const context = vm.createContext({
    window: {},
    document: {
        getElementById: id => elements[id] || null,
        createElement: (tag) => {
            const el = new Element('', tag);
            return el;
        }
    },
    console,
    atob: val => Buffer.from(val, 'base64').toString('binary'),
    btoa: val => Buffer.from(val, 'binary').toString('base64')
});

const terminalCode = fs.readFileSync(path.join(__dirname, '../wwwroot/js/simulation-terminal.js'), 'utf8');
vm.runInContext(terminalCode, context);

assert(typeof context.window.initSimulationTerminal === 'function');

let completionCalled = 0;
let completionHistory = [];

const labConfig = {
    allowedCommands: ['help', 'clear', 'pwd', 'ls', 'cd', 'cat', 'touch', 'nano', 'echo', 'whoami'],
    initialDirectory: '/home/student',
    fileSystem: {
        home: {
            student: {
                'notes.txt': 'Initial student notes.'
            }
        },
        var: {
            log: {}
        }
    },
    customCommands: [
        {
            id: 'cc_1',
            command: 'show ip interface brief',
            output: 'Interface IP-Address OK? Method Status Protocol\nFa0/1 192.168.1.1 YES manual up up',
            enabled: true
        },
        {
            id: 'cc_2',
            command: 'show running-config',
            output: 'hostname Router-Core\nversion 15.1',
            enabled: false // Disabled
        }
    ],
    objectives: [
        { id: 'obj_touch', text: 'Create flag.txt with touch', type: 'command', target: 'touch' },
        { id: 'obj_custom', text: 'Run show ip interface brief', type: 'command', target: 'show ip interface brief' },
        { id: 'obj_read_redir', text: 'Read redirected output', type: 'cat', target: 'netscan.txt' }
    ]
};

context.window.initSimulationTerminal('terminal', labConfig, function (history) {
    completionCalled++;
    completionHistory = Array.from(history);
    return Promise.resolve();
});

function enterCommand(cmd) {
    elements['term-input'].value = cmd;
    elements['term-input'].listeners.keydown.forEach(fn => fn({ key: 'Enter' }));
}

function getLatestOutput() {
    return elements['term-output'].children.map(n => n.innerHTML).join('\n');
}

// 1. Test touch command
enterCommand('touch myfile.txt');
let out = getLatestOutput();
assert(!out.includes('term-error'), 'touch myfile.txt should succeed');
enterCommand('ls');
out = getLatestOutput();
assert(out.includes('myfile.txt'), 'myfile.txt must appear in ls output');
enterCommand('cat myfile.txt');
out = getLatestOutput();
assert(!out.includes('term-error'), 'cat myfile.txt should succeed with empty content');

// touch absolute path
enterCommand('touch /var/log/audit.log');
enterCommand('cd /var/log');
enterCommand('ls');
out = getLatestOutput();
assert(out.includes('audit.log'), 'audit.log must appear in /var/log');
enterCommand('cd /home/student');

// touch invalid directory
enterCommand('touch /nonexistent/dir/file.txt');
out = getLatestOutput();
assert(out.includes('No such file or directory'), 'touch with missing parent directory must fail');

// 2. Test Output Redirection with >
enterCommand('echo hello_world > hello.txt');
enterCommand('cat hello.txt');
out = getLatestOutput();
assert(out.includes('hello_world'), 'cat hello.txt must return redirected output');

// Overwrite existing file
enterCommand('clear');
enterCommand('echo replaced_text > hello.txt');
enterCommand('cat hello.txt');
out = getLatestOutput();
assert(out.includes('replaced_text'), 'redirection must overwrite existing file');
assert(!out.includes('hello_world'), 'previous content must be overwritten');

// Multiple redirection syntax error
enterCommand('echo test > file1.txt > file2.txt');
out = getLatestOutput();
assert(out.includes('multiple redirection not supported'), 'multiple redirection must be rejected');

// Ambiguous redirect
enterCommand('echo test > file1.txt file2.txt');
out = getLatestOutput();
assert(out.includes('ambiguous redirect'), 'ambiguous redirect must be rejected');

// 3. Test Exact-Match Custom Commands
enterCommand('show ip interface brief');
out = getLatestOutput();
assert(out.includes('Fa0/1 192.168.1.1'), 'Exact custom command must output configured text');

// Partial match must NOT execute custom command
enterCommand('show ip interface');
out = getLatestOutput();
assert(out.includes('command not found'), 'Partial command must return command not found');

// Extra arguments must NOT execute custom command
enterCommand('show ip interface brief extra');
out = getLatestOutput();
assert(out.includes('command not found'), 'Extra arguments must return command not found');

// Case sensitive: incorrect case must NOT match
enterCommand('SHOW IP INTERFACE BRIEF');
out = getLatestOutput();
assert(out.includes('command not found'), 'Uppercase command must not match case-sensitive custom command');

// Disabled custom command must NOT execute
enterCommand('show running-config');
out = getLatestOutput();
assert(out.includes('command not found'), 'Disabled custom command must not execute');

// 4. Test Custom Command Output Redirection
enterCommand('show ip interface brief > netscan.txt');
enterCommand('cat netscan.txt');
out = getLatestOutput();
assert(out.includes('Fa0/1 192.168.1.1'), 'Redirected custom command output must be readable via cat');

// 5. Test Simulated nano Editor
enterCommand('nano new_notes.txt');
const nanoTextarea = elements['nano-textarea'];
assert(nanoTextarea, 'nano textarea must be present');
nanoTextarea.value = 'Simulated nano content 123';
const nanoSaveBtn = elements['nano-save-btn'];
assert(nanoSaveBtn, 'nano save button must be present');
nanoSaveBtn.listeners.click[0]();

enterCommand('cat new_notes.txt');
out = getLatestOutput();
assert(out.includes('Simulated nano content 123'), 'Saved nano content must be readable with cat');

// 6. Test Objectives Completion
enterCommand('touch flag.txt');
assert.equal(completionCalled, 1, 'All 3 objectives satisfied must trigger verification callback once');
assert(completionHistory.includes('show ip interface brief'), 'Command history must include exact custom command');
assert(completionHistory.includes('cat netscan.txt'), 'Command history must include cat command');

// 7. Test Objectives rendering SVG icons
const objHtml = elements['simulation-objectives-list'].innerHTML;
assert(!/[\u{1F300}-\u{1F6FF}\u{2600}-\u{26FF}\u{2700}-\u{27BF}]/u.test(objHtml), 'Objectives list must not contain emojis');
assert(objHtml.includes('<svg class="sim-icon"'), 'Objectives list must use SVG icons');

console.log('PASS Simulated Terminal: touch, nano editor, output redirection (>), exact-match custom commands, and SVG icons');
