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

const mockSessionStorage = {};
const sessionStorage = {
    getItem: (k) => mockSessionStorage[k] || null,
    setItem: (k, v) => { mockSessionStorage[k] = String(v); },
    removeItem: (k) => { delete mockSessionStorage[k]; },
    clear: () => { Object.keys(mockSessionStorage).forEach(k => delete mockSessionStorage[k]); }
};

const context = vm.createContext({
    window: {},
    sessionStorage,
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
context.window.sessionStorage = sessionStorage;

const terminalCode = fs.readFileSync(path.join(__dirname, '../wwwroot/js/simulation-terminal.js'), 'utf8');
vm.runInContext(terminalCode, context);

assert(typeof context.window.initSimulationTerminal === 'function');

let completionCalled = 0;
let completionHistory = [];

const labConfig = {
    allowedCommands: ['help', 'clear', 'pwd', 'ls', 'cd', 'cat', 'touch', 'nano', 'echo', 'whoami'],
    initialDirectory: '/home/student',
    sequentialObjectives: true,
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
            command: 'whois example.com',
            output: 'Domain Name: EXAMPLE.COM\nRegistry Domain ID: 2336799_DOMAIN_COM-VRSN',
            enabled: true
        },
        {
            id: 'cc_2',
            command: 'service audit status',
            output: 'audit.service: active (running)',
            enabled: false // Disabled
        },
        {
            id: 'cc_py',
            command: 'python main.py',
            output: 'Hello, World from Python!',
            enabled: true,
            requiredFiles: ['main.py']
        },
        {
            id: 'cc_dir',
            command: 'inspect data/',
            output: 'data directory contents verified',
            enabled: true,
            requiredFiles: ['data/']
        },
        {
            id: 'cc_bad_traversal',
            command: 'read secret',
            output: 'traversal bypassed',
            enabled: true,
            requiredFiles: ['../../etc/shadow']
        }
    ],
    objectives: [
        { id: 'obj_touch', text: 'Create flag.txt with touch', command: 'touch flag.txt', hint: 'Type touch flag.txt' },
        { id: 'obj_custom', text: 'Run whois example.com', command: 'whois example.com', hint: 'Type whois example.com' },
        { id: 'obj_read_redir', text: 'Read redirected output', command: 'cat netscan.txt', hint: 'Type cat netscan.txt' }
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

// 1. Verify initial hint display for step 1
let out = getLatestOutput();
assert(out.includes('Hint (Step #1)'), 'Initial Step #1 hint must be printed on startup');
assert(out.includes('<svg') && !out.includes('💡'), 'Hint should use SVG icon and not emoji');
assert(out.includes('Type touch flag.txt'), 'Step #1 hint content must be displayed');

// 2. Test touch command
enterCommand('touch myfile.txt');
out = getLatestOutput();
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

// 3. Test Output Redirection with >
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

// 4. Test Exact-Match Custom Commands
// Test premature execution of Step 2 (whois example.com) while Step 1 is incomplete
enterCommand('whois example.com');
out = getLatestOutput();
assert(out.includes('Domain Name: EXAMPLE.COM'), 'Custom command executes and produces output');
assert(out.includes('Sequential Step Required'), 'Attempting Step 2 prematurely warns user in sequential mode');

// Partial match must NOT execute custom command
enterCommand('whois example');
out = getLatestOutput();
assert(out.includes('command not found'), 'Partial command must return command not found');

// Extra arguments must NOT execute custom command
enterCommand('whois example.com extra');
out = getLatestOutput();
assert(out.includes('command not found'), 'Extra arguments must return command not found');

// Case sensitive: incorrect case must NOT match
enterCommand('WHOIS EXAMPLE.COM');
out = getLatestOutput();
assert(out.includes('command not found'), 'Uppercase command must not match case-sensitive custom command');

// Disabled custom command must NOT execute
enterCommand('service audit status');
out = getLatestOutput();
assert(out.includes('command not found'), 'Disabled custom command must not execute');

// Obsolete command must not execute
enterCommand('ping 127.0.0.1');
out = getLatestOutput();
assert(out.includes('command not found'), 'Obsolete ping command must return command not found');

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

// 6. Test Sequential Objectives Completion
assert.equal(completionCalled, 0, 'Lab must not be completed yet');

// Complete Step 1
enterCommand('touch flag.txt');
out = getLatestOutput();
assert(out.includes('Completed Step #1'), 'Step 1 must be marked completed');
assert(out.includes('Hint (Step #2)'), 'Step 2 hint must be printed when Step 1 is completed');

// Complete Step 2 with redirection
enterCommand('whois example.com > netscan.txt');
out = getLatestOutput();
assert(out.includes('Completed Step #2'), 'Step 2 must be marked completed');
assert(out.includes('Hint (Step #3)'), 'Step 3 hint must be printed when Step 2 is completed');

// Complete Step 3
enterCommand('cat netscan.txt');
out = getLatestOutput();
assert(out.includes('Completed Step #3'), 'Step 3 must be marked completed');

// Assert verification callback invoked
assert.equal(completionCalled, 1, 'All 3 sequential objectives satisfied must trigger verification callback once');
assert(completionHistory.includes('whois example.com > netscan.txt') || completionHistory.includes('whois example.com'), 'Command history must include exact custom command');
assert(completionHistory.includes('cat netscan.txt'), 'Command history must include cat command');

// 7. Test Objectives rendering SVG icons
const objHtml = elements['simulation-objectives-list'].innerHTML;
assert(!/[\u{1F300}-\u{1F6FF}\u{2600}-\u{26FF}\u{2700}-\u{27BF}]/u.test(objHtml), 'Objectives list must not contain emojis');
assert(objHtml.includes('<svg class="sim-icon"'), 'Objectives list must use SVG icons');

// 8. Test Reset Simulation Attempt
assert(typeof context.window.resetSimulationAttempt === 'function', 'resetSimulationAttempt must be exposed');
context.window.resetSimulationAttempt();
const remainingStorageKeys = Object.keys(mockSessionStorage).filter(k => k.includes('hackacad_introduced_hints'));
assert.equal(remainingStorageKeys.length, 0, 'resetSimulationAttempt should clear session storage hints');

// 9. Test Custom Command Required Files and Directories validation
// 9.1 Fails when required file main.py is absent from /home/student
enterCommand('python main.py');
out = getLatestOutput();
assert(out.includes("python:") && out.includes("main.py") && out.includes("No such file or directory"), 'python main.py must fail with simulated Linux error when main.py does not exist');
assert(!out.includes('Hello, World from Python!'), 'Success output must NOT be printed when prerequisite file is missing');

// 9.2 Create main.py via touch
enterCommand('touch main.py');
enterCommand('python main.py');
out = getLatestOutput();
assert(out.includes('Hello, World from Python!'), 'python main.py must succeed when main.py exists in cwd');

// 9.3 Change directory to /var/log where main.py does not exist
enterCommand('cd /var/log');
enterCommand('python main.py');
out = getLatestOutput();
assert(out.includes("python:") && out.includes("main.py") && out.includes("No such file or directory"), 'python main.py must fail from /var/log even though main.py exists in /home/student');

// 9.4 Change back to /home/student where main.py exists
enterCommand('cd /home/student');
enterCommand('python main.py');
out = getLatestOutput();
assert(out.includes('Hello, World from Python!'), 'python main.py must succeed again after returning to /home/student');

// 9.5 Missing directory requirement
enterCommand('inspect data/');
out = getLatestOutput();
assert(out.includes('inspect:') && out.includes('data/') && out.includes('No such directory'), 'Missing directory prerequisite must fail with directory error');

// 9.6 Traversal attempt
enterCommand('read secret');
out = getLatestOutput();
assert(out.includes('Invalid path'), 'Path traversal prerequisite must be rejected with Invalid path error');

console.log('PASS Simulated Terminal: touch, nano editor, output redirection (>), exact-match custom commands, sequential objectives, SVG icons, and custom command file prerequisites');
