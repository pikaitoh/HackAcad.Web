// Run: node tests/phone_verification_ui.js. No browser, SMS, network or additional packages.
const fs = require('node:fs');
const vm = require('node:vm');
const assert = require('node:assert/strict');
const source = fs.readFileSync(require('node:path').join(__dirname, '../wwwroot/js/phone-verification.js'), 'utf8');
let now = 1000, tick, inputChanged;
const input = { value: '', addEventListener: (event, fn) => { inputChanged = fn; } };
const action = { dataset: {}, textContent: '', attributes: {}, setAttribute(name, value) { this.attributes[name] = value; } };
const status = { textContent: '' };
const panel = { dataset: { resendSeconds: '60' }, querySelector(selector) {
    return selector.includes('input') ? input : selector === '#phone-code-action' ? action : status;
} };
const document = { querySelector: () => panel, addEventListener() {} };
vm.runInNewContext(source, { document, performance: { now: () => now }, setInterval: fn => { tick = fn; } });
assert.equal(action.textContent, 'Verify phone number');
assert.equal(action.attributes.form, 'phone-code-form');
now += 59000; tick();
assert.match(status.textContent, /1 seconds/);
assert.equal(action.attributes.form, 'phone-code-form');
now += 1000; tick();
assert.equal(action.textContent, 'Resend verification code');
assert.equal(action.attributes.form, 'phone-resend-form');
input.value = '123456'; inputChanged();
assert.equal(action.textContent, 'Verify phone number');
assert.equal(action.attributes.form, 'phone-code-form');
input.value = ''; inputChanged();
assert.equal(action.attributes.form, 'phone-resend-form');
// A reload uses the server's remaining seconds rather than another full minute.
panel.dataset.resendSeconds = '12';
vm.runInNewContext(source, { document, performance: { now: () => now }, setInterval: fn => { tick = fn; } });
now += 11000; tick(); assert.equal(action.attributes.form, 'phone-code-form');
now += 1000; tick(); assert.equal(action.attributes.form, 'phone-resend-form');
vm.runInNewContext(source, { document: { querySelector: () => null } });
console.log('PASS timed resend, form routing, code entry after the minute, reload cooldown and unrelated pages. No messages sent.');
