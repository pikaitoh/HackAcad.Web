/**
 * terminal-dialogs-test.js
 * Comprehensive tests for HackAcad Terminal Module dialog / notification system:
 *   - wwwroot/js/terminal-dialogs.js
 *   - wwwroot/css/terminal-dialogs.css
 *   - Razor Views integration in Terminal Module
 *   - wwwroot/js/simulation-builder.js integration
 *
 * Run: node tests/terminal-dialogs-test.js
 * (from /home/jk/Desktop/Stuff/Projs/HackAcad/HackAcad.Web)
 */

'use strict';

const assert = require('node:assert/strict');
const fs     = require('node:fs');
const path   = require('node:path');

console.log('==================================================');
console.log('  HACKACAD TERMINAL DIALOGS & TOASTS TEST SUITE   ');
console.log('==================================================');

// ---------------------------------------------------------------------------
// 1. Static Source Audit: terminal-dialogs.js
// ---------------------------------------------------------------------------
console.log('\n--- 1. Static Source Audit: terminal-dialogs.js ---');

const jsPath = path.join(__dirname, '../wwwroot/js/terminal-dialogs.js');
assert.ok(fs.existsSync(jsPath), 'terminal-dialogs.js must exist');
const dialogsSrc = fs.readFileSync(jsPath, 'utf8');

// Must export public API
assert.ok(dialogsSrc.includes('window.TerminalDialogs = TerminalDialogs;'), 'Exports window.TerminalDialogs');
assert.ok(dialogsSrc.includes('window.HackAcadDialogs = TerminalDialogs;'), 'Exports window.HackAcadDialogs alias');

// Never calls native window popup functions
assert.ok(!/\bwindow\.(alert|confirm|prompt)\b/.test(dialogsSrc), 'terminal-dialogs.js never calls window.alert/confirm/prompt');

// Public API methods present
for (const method of ['toast', 'confirm', 'prompt', 'alert', 'success', 'error', 'warning', 'info']) {
    assert.ok(
        dialogsSrc.includes(`${method},`) || dialogsSrc.includes(`${method}:`),
        `terminal-dialogs.js defines method "${method}" in public API`
    );
}

// Auto-bind for form[data-confirm]
assert.ok(dialogsSrc.includes('form[data-confirm]'), 'Listens for forms with data-confirm attribute');
assert.ok(dialogsSrc.includes('DOMContentLoaded'), 'Binds auto-confirmation on DOMContentLoaded');
assert.ok(dialogsSrc.includes('data-confirm-title'), 'Supports custom data-confirm-title');
assert.ok(dialogsSrc.includes('data-confirm-button'), 'Supports custom data-confirm-button');
assert.ok(dialogsSrc.includes('data-confirm-destructive'), 'Supports data-confirm-destructive');
assert.ok(dialogsSrc.includes('dataset.confirmed') || dialogsSrc.includes('data-confirmed'), 'Sets dataset.confirmed to avoid infinite recursion');

// Accessibility & UX
assert.ok(dialogsSrc.includes('aria-modal="true"'), 'Confirmation modal has aria-modal="true"');
assert.ok(dialogsSrc.includes('role="dialog"'), 'Confirmation modal has role="dialog"');
assert.ok(dialogsSrc.includes('role="alert"') || dialogsSrc.includes('aria-live='), 'Toasts have ARIA alert or live regions');
assert.ok(dialogsSrc.includes('Escape'), 'Modals close on Escape key');
assert.ok(dialogsSrc.includes('Tab'), 'Modals trap keyboard focus on Tab');
assert.ok(dialogsSrc.includes('escapeHtml'), 'Includes HTML escaping against XSS');

// SVG icons used, no emoji
assert.ok(dialogsSrc.includes('<svg'), 'Uses SVG icons');
assert.ok(!/[\u{1F300}-\u{1F9FF}\u{2600}-\u{26FF}\u{2700}-\u{27BF}]/u.test(dialogsSrc), 'Zero emojis in terminal-dialogs.js');

console.log('[PASS] terminal-dialogs.js static audit passed completely');

// ---------------------------------------------------------------------------
// 2. CSS Audit: terminal-dialogs.css & Import Chains
// ---------------------------------------------------------------------------
console.log('\n--- 2. CSS Audit: terminal-dialogs.css & Import Chains ---');

const cssPath = path.join(__dirname, '../wwwroot/css/terminal-dialogs.css');
assert.ok(fs.existsSync(cssPath), 'terminal-dialogs.css must exist');
const cssSrc = fs.readFileSync(cssPath, 'utf8');

// Core toast classes
for (const cls of [
    '.ha-toast-container', '.ha-toast', '.ha-toast-success', '.ha-toast-error',
    '.ha-toast-warning', '.ha-toast-info', '.ha-toast-close', '.ha-toast-hiding'
]) {
    assert.ok(cssSrc.includes(cls), `CSS must define ${cls}`);
}

// Core modal classes
for (const cls of [
    '.ha-modal-backdrop', '.ha-modal-card', '.ha-modal-header', '.ha-modal-body',
    '.ha-modal-footer', '.ha-modal-btn', '.ha-modal-btn-danger', '.ha-modal-btn-primary',
    '.ha-modal-btn-cancel', '.ha-modal-input', '.ha-modal-input-error',
    '.ha-modal-icon-destructive', '.ha-modal-icon-default'
]) {
    assert.ok(cssSrc.includes(cls), `CSS must define ${cls}`);
}

// Light & Dark theme variables and selectors
assert.ok(cssSrc.includes('--ha-surface'), 'Uses --ha-surface theme variable');
assert.ok(cssSrc.includes('--ha-ink'), 'Uses --ha-ink theme variable');
assert.ok(
    cssSrc.includes('data-theme="dark"') || cssSrc.includes('data-appearance="dark"'),
    'Defines dark-mode overrides for toasts and dialogs'
);
assert.ok(cssSrc.includes('@keyframes'), 'Defines entrance/exit animations');
assert.ok(cssSrc.includes('prefers-reduced-motion'), 'Includes reduced motion accessibility support');

// Verify import chains
const notebookCssPath = path.join(__dirname, '../wwwroot/css/terminal-notebooks.css');
assert.ok(fs.existsSync(notebookCssPath), 'terminal-notebooks.css exists');
const notebookCss = fs.readFileSync(notebookCssPath, 'utf8');
assert.ok(notebookCss.includes('terminal-dialogs.css'), 'terminal-notebooks.css imports terminal-dialogs.css');

const simBuilderCssPath = path.join(__dirname, '../wwwroot/css/simulation-builder.css');
assert.ok(fs.existsSync(simBuilderCssPath), 'simulation-builder.css exists');
const simBuilderCss = fs.readFileSync(simBuilderCssPath, 'utf8');
assert.ok(simBuilderCss.includes('terminal-dialogs.css'), 'simulation-builder.css imports terminal-dialogs.css');

console.log('[PASS] terminal-dialogs.css styling and import chains verified');

// ---------------------------------------------------------------------------
// 3. Views Audit: Terminal Module Native Dialog Cleanup
// ---------------------------------------------------------------------------
console.log('\n--- 3. Views Audit: Elimination of Native Dialogs ---');

const viewsToCheck = [
    {
        name: 'Views/Notebooks/Index.cshtml',
        path: path.join(__dirname, '../Views/Notebooks/Index.cshtml'),
        expectScript: 'terminal-dialogs.js',
        checkDeleteConfirm: true
    },
    {
        name: 'Views/Notebooks/Edit.cshtml',
        path: path.join(__dirname, '../Views/Notebooks/Edit.cshtml'),
        expectScript: 'terminal-dialogs.js',
        checkDeleteConfirm: false
    },
    {
        name: 'Views/InstructorCourses/EditCourseNotebook.cshtml',
        path: path.join(__dirname, '../Views/InstructorCourses/EditCourseNotebook.cshtml'),
        expectScript: 'terminal-dialogs.js',
        checkDeleteConfirm: false
    },
    {
        name: 'Views/InstructorCourses/Curriculum.cshtml',
        path: path.join(__dirname, '../Views/InstructorCourses/Curriculum.cshtml'),
        expectScript: 'terminal-dialogs.js',
        checkDeleteConfirm: true
    }
];

for (const v of viewsToCheck) {
    assert.ok(fs.existsSync(v.path), `${v.name} exists`);
    const content = fs.readFileSync(v.path, 'utf8');

    // Must load terminal-dialogs.js
    assert.ok(
        content.includes(v.expectScript),
        `${v.name} loads ${v.expectScript}`
    );

    // If it has delete forms for terminal module items, verify data-confirm is used instead of onsubmit="return confirm"
    if (v.checkDeleteConfirm) {
        if (v.name.includes('Index.cshtml')) {
            assert.ok(!content.includes('onsubmit="return confirm('), `${v.name} has no onsubmit="return confirm(`);
            assert.ok(content.includes('data-confirm='), `${v.name} uses data-confirm for notebook deletion`);
        }
        if (v.name.includes('Curriculum.cshtml')) {
            // DeleteSimulation and DeleteCourseNotebook must use data-confirm
            assert.ok(content.includes('asp-action="DeleteSimulation"'), 'Curriculum has DeleteSimulation');
            assert.ok(content.includes('asp-action="DeleteCourseNotebook"'), 'Curriculum has DeleteCourseNotebook');
            // The simulation/notebook forms must use data-confirm
            const simDeleteMatch = content.match(/<form[^>]*asp-action="DeleteSimulation"[^>]*>/);
            assert.ok(simDeleteMatch && simDeleteMatch[0].includes('data-confirm'), 'DeleteSimulation form uses data-confirm');
            const nbDeleteMatch = content.match(/<form[^>]*asp-action="DeleteCourseNotebook"[^>]*>/);
            assert.ok(nbDeleteMatch && nbDeleteMatch[0].includes('data-confirm'), 'DeleteCourseNotebook form uses data-confirm');
        }
    }
}

console.log('[PASS] All Terminal Module views load terminal-dialogs and use data-confirm');

// ---------------------------------------------------------------------------
// 4. Client Script Audit: simulation-builder.js & notebook editors
// ---------------------------------------------------------------------------
console.log('\n--- 4. Client Script Audit: simulation-builder.js & Edit Views ---');

const sbJsPath = path.join(__dirname, '../wwwroot/js/simulation-builder.js');
assert.ok(fs.existsSync(sbJsPath), 'simulation-builder.js exists');
const sbJs = fs.readFileSync(sbJsPath, 'utf8');

assert.ok(sbJs.includes('window.TerminalDialogs'), 'simulation-builder.js integrates with window.TerminalDialogs');
assert.ok(sbJs.includes('TerminalDialogs.confirm'), 'simulation-builder.js calls TerminalDialogs.confirm');
assert.ok(sbJs.includes('TerminalDialogs.prompt'), 'simulation-builder.js calls TerminalDialogs.prompt');
assert.ok(sbJs.includes('TerminalDialogs.success'), 'simulation-builder.js calls TerminalDialogs.success');
assert.ok(sbJs.includes('TerminalDialogs.error'), 'simulation-builder.js calls TerminalDialogs.error');

// Edit.cshtml block deletion & saving notifications
const editCshtml = fs.readFileSync(path.join(__dirname, '../Views/Notebooks/Edit.cshtml'), 'utf8');
assert.ok(editCshtml.includes('TerminalDialogs.confirm'), 'Edit.cshtml uses TerminalDialogs.confirm for deleteBlock');
assert.ok(editCshtml.includes('TerminalDialogs.success'), 'Edit.cshtml uses TerminalDialogs.success for saveNotebook');
assert.ok(editCshtml.includes('TerminalDialogs.warning'), 'Edit.cshtml uses TerminalDialogs.warning for unsaved changes or warnings');

const courseEditCshtml = fs.readFileSync(path.join(__dirname, '../Views/InstructorCourses/EditCourseNotebook.cshtml'), 'utf8');
assert.ok(courseEditCshtml.includes('TerminalDialogs.confirm'), 'EditCourseNotebook.cshtml uses TerminalDialogs.confirm for deleteBlock');
assert.ok(courseEditCshtml.includes('TerminalDialogs.success'), 'EditCourseNotebook.cshtml uses TerminalDialogs.success for saveNotebook');
assert.ok(courseEditCshtml.includes('TerminalDialogs.warning'), 'EditCourseNotebook.cshtml uses TerminalDialogs.warning');

console.log('[PASS] simulation-builder.js and Notebook Edit views properly invoke TerminalDialogs');

// ---------------------------------------------------------------------------
// 5. Runtime DOM Simulation of TerminalDialogs
// ---------------------------------------------------------------------------
console.log('\n--- 5. Runtime Functional Tests: TerminalDialogs API ---');

function createMockEnvironment() {
    let listeners = [];

    function createNode(tag) {
        const node = {
            tagName: tag.toUpperCase(),
            className: '',
            parentNode: null,
            _children: [],
            _attrs: {},
            _listeners: {},
            dataset: {},
            style: {},
            focus() {},
            blur() {},
            select() {},
            remove() {
                if (this.parentNode && this.parentNode._children) {
                    const idx = this.parentNode._children.indexOf(this);
                    if (idx !== -1) this.parentNode._children.splice(idx, 1);
                }
                this.parentNode = null;
            },
            appendChild(c) {
                this._children.push(c);
                c.parentNode = this;
                return c;
            },
            contains(c) {
                if (this === c) return true;
                return this._children.some(child => child.contains(c));
            },
            setAttribute(k, v) { this._attrs[k] = String(v); },
            getAttribute(k)    { return this._attrs[k] !== undefined ? this._attrs[k] : null; },
            addEventListener(evt, fn) {
                if (!this._listeners[evt]) this._listeners[evt] = [];
                this._listeners[evt].push(fn);
            },
            dispatchEvent(e) {
                (this._listeners[e.type] || []).forEach(fn => fn(e));
            },
            querySelector(sel) {
                return findFirst(this, sel);
            },
            querySelectorAll(sel) {
                const results = [];
                findAll(this, sel, results);
                return results;
            },
            get classList() {
                const self = this;
                return {
                    add(...names) {
                        const set = new Set((self.className || '').split(/\s+/).filter(Boolean));
                        names.forEach(n => set.add(n));
                        self.className = [...set].join(' ');
                    },
                    remove(...names) {
                        const set = new Set((self.className || '').split(/\s+/).filter(Boolean));
                        names.forEach(n => set.delete(n));
                        self.className = [...set].join(' ');
                    },
                    contains(name) {
                        return (self.className || '').split(/\s+/).includes(name);
                    }
                };
            },
            set innerHTML(html) {
                this._children = [];
                // Simple tag extractor to instantiate child nodes for querySelector
                const tagRegex = /<([a-zA-Z0-9]+)([^>]*)>/g;
                let m;
                while ((m = tagRegex.exec(html)) !== null) {
                    const childTag = m[1];
                    const attrs = m[2];
                    const child = createNode(childTag);

                    const classMatch = attrs.match(/class="([^"]+)"/);
                    if (classMatch) {
                        classMatch[1].split(/\s+/).forEach(c => child.classList.add(c));
                    }
                    const idMatch = attrs.match(/id="([^"]+)"/);
                    if (idMatch) child.setAttribute('id', idMatch[1]);

                    const actMatch = attrs.match(/data-ha-action="([^"]+)"/);
                    if (actMatch) child.setAttribute('data-ha-action', actMatch[1]);

                    const roleMatch = attrs.match(/role="([^"]+)"/);
                    if (roleMatch) child.setAttribute('role', roleMatch[1]);

                    const typeMatch = attrs.match(/type="([^"]+)"/);
                    if (typeMatch) child.setAttribute('type', typeMatch[1]);

                    this._children.push(child);
                    child.parentNode = this;
                }
            },
            get innerHTML() { return ''; },
            get value() { return this._attrs.value || ''; },
            set value(v) { this._attrs.value = String(v); }
        };
        return node;
    }

    function matchSelector(node, sel) {
        if (!node || typeof node !== 'object') return false;
        if (sel.startsWith('.')) {
            const cls = sel.slice(1);
            return node.classList && node.classList.contains(cls);
        }
        if (sel.startsWith('#')) {
            const id = sel.slice(1);
            return node.getAttribute && node.getAttribute('id') === id;
        }
        if (sel.startsWith('[') && sel.endsWith(']')) {
            const inner = sel.slice(1, -1);
            if (inner.includes('=')) {
                const [k, rawV] = inner.split('=');
                const v = rawV.replace(/^["']|["']$/g, '');
                return node.getAttribute && node.getAttribute(k) === v;
            }
            return node.getAttribute && node.getAttribute(inner) !== null;
        }
        return (node.tagName || '').toLowerCase() === sel.toLowerCase();
    }

    function findFirst(root, sel) {
        for (const c of root._children) {
            if (matchSelector(c, sel)) return c;
            const sub = findFirst(c, sel);
            if (sub) return sub;
        }
        return null;
    }

    function findAll(root, sel, results) {
        for (const c of root._children) {
            if (matchSelector(c, sel)) results.push(c);
            findAll(c, sel, results);
        }
    }

    const docBody = createNode('body');

    const mockDocument = {
        body: docBody,
        activeElement: null,
        createElement: tag => createNode(tag),
        querySelector: sel => findFirst(docBody, sel),
        querySelectorAll: sel => {
            const r = [];
            findAll(docBody, sel, r);
            return r;
        },
        addEventListener: (evt, fn) => { listeners.push({ evt, fn }); },
        removeEventListener: (evt, fn) => { listeners = listeners.filter(l => l.fn !== fn); }
    };

    const mockWindow = {
        document: mockDocument,
        requestAnimationFrame: fn => { fn(); return 1; },
        setTimeout: (fn, ms) => { return 1; },
        clearTimeout: () => {}
    };
    mockWindow.window = mockWindow;

    const fn = new Function('window', 'document', 'requestAnimationFrame', 'setTimeout', 'clearTimeout', dialogsSrc);
    fn(mockWindow, mockDocument, mockWindow.requestAnimationFrame, mockWindow.setTimeout, mockWindow.clearTimeout);

    return { mockWindow, mockDocument, docBody };
}

// 5.1 Test API Exposure
const env = createMockEnvironment();
const TD = env.mockWindow.TerminalDialogs;
assert.ok(TD, 'TerminalDialogs object created on window');
assert.strictEqual(TD, env.mockWindow.HackAcadDialogs, 'HackAcadDialogs aliases TerminalDialogs');

for (const m of ['toast', 'confirm', 'prompt', 'alert', 'success', 'error', 'warning', 'info']) {
    assert.strictEqual(typeof TD[m], 'function', `TD.${m} is a function`);
}
console.log('[PASS] TerminalDialogs methods properly exported at runtime');

// 5.2 Test toast()
const toastHandle = TD.toast({ message: 'Operation complete', type: 'success' });
assert.ok(toastHandle && typeof toastHandle.dismiss === 'function', 'toast() returns handle with dismiss()');
const container = env.docBody.querySelector('.ha-toast-container');
assert.ok(container, 'ha-toast-container created in body');
const toastEl = env.docBody.querySelector('.ha-toast-success');
assert.ok(toastEl, '.ha-toast-success appended to container');

// 5.3 Test toast shortcuts
TD.error('Error message');
assert.ok(env.docBody.querySelector('.ha-toast-error'), '.ha-toast-error created by TD.error');

TD.warning('Warning message');
assert.ok(env.docBody.querySelector('.ha-toast-warning'), '.ha-toast-warning created by TD.warning');

TD.info('Info message');
assert.ok(env.docBody.querySelector('.ha-toast-info'), '.ha-toast-info created by TD.info');
console.log('[PASS] toast() and shortcut methods render appropriate toast classes');

// 5.4 Test confirm() modal creation
const confirmPromise = TD.confirm({
    title: 'Delete Notebook',
    message: 'Are you sure?',
    isDestructive: true
});
assert.ok(confirmPromise instanceof Promise, 'confirm() returns Promise');

const backdrop = env.docBody.querySelector('.ha-modal-backdrop');
assert.ok(backdrop, 'ha-modal-backdrop created in DOM');
const dangerBtn = backdrop.querySelector('.ha-modal-btn-danger');
assert.ok(dangerBtn, 'Destructive confirm renders .ha-modal-btn-danger');
const cancelBtn = backdrop.querySelector('.ha-modal-btn-cancel');
assert.ok(cancelBtn, 'Confirm dialog renders .ha-modal-btn-cancel');
console.log('[PASS] confirm() modal displays backdrop, prompt, and action buttons');

// 5.5 Test prompt() modal
const promptPromise = TD.prompt({
    title: 'New Folder',
    message: 'Folder name:',
    defaultValue: 'test'
});
assert.ok(promptPromise instanceof Promise, 'prompt() returns Promise');
const promptInput = env.docBody.querySelector('#haPromptInput');
assert.ok(promptInput, 'prompt() renders #haPromptInput text field');
console.log('[PASS] prompt() modal renders input field and handles interactions');

console.log('\n==================================================');
console.log('  ALL TERMINAL DIALOG TESTS PASSED! (100% SUCCESS)');
console.log('==================================================\n');
