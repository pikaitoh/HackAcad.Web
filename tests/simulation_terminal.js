const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

// Execute the real terminal UI with only the small DOM surface it uses.
class Element {
  constructor() { this.innerHTML = ''; this.textContent = ''; this.value = ''; this.style = {}; this.children = []; this.listeners = {}; }
  addEventListener(name, callback) { this.listeners[name] = callback; }
  appendChild(child) { this.children.push(child); }
  focus() {}
  querySelector(selector) { return elements[selector.slice(1)]; }
}
const elements = Object.fromEntries(['terminal', 'term-output', 'term-input', 'term-prompt', 'simulation-objectives-list', 'simulation-success-banner'].map(id => [id, new Element()]));
const context = vm.createContext({window: {}, document: {getElementById: id => elements[id], createElement: () => new Element()},
  console, atob: value => Buffer.from(value, 'base64').toString('binary'), btoa: value => Buffer.from(value, 'binary').toString('base64')});
vm.runInContext(fs.readFileSync(path.join(__dirname, '../wwwroot/js/simulation-terminal.js'), 'utf8'), context);
const payload = '<img src=x onerror="window.review=1">';
let calls = 0; let transcript;
context.window.initSimulationTerminal('terminal', {
  allowedCommands: ['cd', 'cat', 'chmod', 'ls', 'grep', 'echo', 'history'],
  initialDirectory: '/home/student',
  fileSystem: {home: {student: {[payload]: 'file', 'notes.txt': payload}}, var: {secret: {'audit.sh': 'review token'}}},
  objectives: [{id: 'nav', type: 'cwd', target: '/var/secret', text: payload}, {id: 'mode', type: 'chmod', target: 'audit.sh'}, {id: 'read', type: 'cat', target: 'audit.sh'}]
}, commands => { calls++; transcript = Array.from(commands); return Promise.resolve(); });
function enter(command) { elements['term-input'].value = command; elements['term-input'].listeners.keydown({key: 'Enter'}); }
function output() { return elements['term-output'].children.map(node => node.innerHTML).join('\n'); }
assert(!elements['simulation-objectives-list'].innerHTML.includes('<img'), 'objective text must be escaped');
enter(payload); enter('echo ' + payload); enter('ls'); enter('cat notes.txt'); enter('grep img notes.txt'); enter('history');
assert(!output().includes('<img'), 'commands, filenames, history and file/grep output must not inject HTML');
assert(output().includes('&lt;img'), 'unsafe strings remain visible as text');
enter('cd /missing'); enter('cat audit.sh'); enter('chmod 755 audit.sh');
assert.equal(calls, 0, 'failed commands do not complete objectives');
enter('cd /var/secret'); enter('chmod 644 audit.sh'); enter('cat audit.sh');
assert.equal(calls, 0, 'incorrect permission mode cannot complete the lab');
enter('chmod 755 audit.sh');
assert.equal(calls, 1, 'valid commands trigger one verification request');
assert.equal(transcript.at(-1), 'chmod 755 audit.sh');
assert(transcript.includes('cat audit.sh'), 'the server receives the command history rather than completion claims');
assert(!elements['simulation-success-banner'].style.display, 'client completion alone must not display a saved-success banner');
enter('pwd');
assert.equal(calls, 1, 'a completed local attempt does not submit twice');
console.log('PASS terminal input/content escaping, failed-command handling, chmod verification and server-bound transcript');
