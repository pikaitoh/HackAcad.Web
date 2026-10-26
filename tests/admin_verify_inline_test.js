const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const path = require('node:path');
const root = process.argv[2] || path.resolve(__dirname, '..');

class Element {
    constructor(properties = {}) {
        Object.assign(this, { listeners: {}, attributes: {}, dataset: {}, validity: {}, value: '', textContent: '', type: 'text', disabled: false }, properties);
        const classes = new Set();
        this.classList = { add: c => classes.add(c), remove: c => classes.delete(c), toggle: (c, on) => on ? classes.add(c) : classes.delete(c), contains: c => classes.has(c) };
    }
    addEventListener(name, handler) { (this.listeners[name] ||= []).push(handler); }
    async emit(name, target = this) {
        const event = { target, defaultPrevented: false, preventDefault() { this.defaultPrevented = true; } };
        for (const handler of this.listeners[name] || []) await handler(event);
        return event;
    }
    setAttribute(name, value) { this.attributes[name] = value; }
    removeAttribute(name) { delete this.attributes[name]; }
    getAttribute(name) { return this.attributes[name] ?? null; }
    hasAttribute(name) { return name in this.attributes; }
    focus() { focused = this; }
}
let focused;
class Form extends Element {
    querySelector() { return null; }
    querySelectorAll(selector) {
        if (selector === 'input,select,textarea') return Object.values(this.elements);
        if (selector === '[data-valmsg-for]') return this.messages || [];
        return [];
    }
    reset() { Object.values(this.elements).forEach(f => { f.value = ''; }); this.emit('reset'); }
    matches(selector) { return selector === '[data-admin-operation]' && this === operation; }
    reportValidity() { assert.equal(this, operation, 'Verification must not invoke browser validation popups'); return true; }
}
const source = fs.readFileSync(path.join(root, 'Views/Shared/_AdminVerifyDialog.cshtml'), 'utf8');
assert.match(source, /id="admin-verify-form"[^>]*data-validated-form/);
const password = new Element({ name: 'CurrentPassword', type: 'password', dataset: { valRequired: 'Enter your current HackAcad password.' } });
const code = new Element({ name: 'Code', required: true, dataset: { valRequired: 'Enter your verification or recovery code.' } });
const messages = [password, code].map(field => {
    assert.match(source, new RegExp(`data-valmsg-for="${field.name}" class="field-error"`));
    return new Element({ dataset: { valmsgFor: field.name } });
});
const verification = new Form({ action: '/verify', elements: { CurrentPassword: password, Code: code, Ticket: new Element({ type: 'hidden' }) }, messages });
const operation = new Form({ action: '/save', elements: { Title: new Element({ value: 'Course draft' }) } });
const errors = new Element(), notice = new Element(), sms = new Element({ dataset: { url: '/sms' } }), cancel = new Element();
const passwordContainer = new Element(), codeLabel = new Element();
const dialog = new Element({ open: false });
dialog.showModal = () => { dialog.open = true; };
dialog.close = () => { dialog.open = false; dialog.emit('close'); };
dialog.querySelector = selector => ({ '[data-verify-errors]': errors, '[data-verify-notice]': notice, '[data-send-verify-sms]': sms, '[data-cancel-verify]': cancel, '[data-password-field]': passwordContainer, '[data-code-label]': codeLabel })[selector];
const document = new Element();
document.getElementById = id => ({ 'admin-verify-dialog': dialog, 'admin-verify-form': verification })[id];
document.querySelectorAll = selector => selector.includes('form[data-account-form]') ? [verification] : [];
document.querySelector = () => null;
class Data extends Map { constructor(form) { super(Object.values(form.elements).map(f => [f.name, f.value])); } }
let hasPassword = true, verified = false, rejectVerification = false;
const requests = [], redirects = [];
const context = vm.createContext({ document, HTMLFormElement: Form, FormData: Data, crypto: { randomUUID: () => 'test-operation' }, window: { location: { assign: url => redirects.push(url) } }, fetch: async (url) => {
    requests.push(url);
    const status = url === '/save' && !verified ? 428 : url === '/verify' && rejectVerification ? 400 : 200;
    const json = status === 428 ? { verificationRequired: true, ticket: 'test-ticket', hasPassword, smsEnabled: !hasPassword } : status === 400 ? { errors: ['Invalid verification code.'] } : url === '/verify' ? { verified: (verified = true) } : url === '/sms' ? { notice: 'SMS code sent.' } : { redirectUrl: '/courses' };
    return { status, ok: status === 200, headers: { get: () => 'application/json' }, json: async () => json };
} });
for (const file of ['account-forms.js', 'admin-operations.js']) vm.runInContext(fs.readFileSync(path.join(root, 'wwwroot/js', file), 'utf8'), context);

(async () => {
    await document.emit('submit', operation);
    assert.equal(dialog.open, true);
    assert.equal(verification.noValidate, true);
    await verification.emit('submit');
    assert.deepEqual(messages.map(m => m.textContent), ['Enter your current HackAcad password.', 'Enter your verification or recovery code.']);
    assert.equal(focused, password);
    assert.deepEqual(requests, ['/save'], 'Empty inputs must not reach the server');
    password.value = 'test-password';
    await verification.emit('input', password);
    assert.equal(messages[0].textContent, '');
    await verification.emit('submit');
    assert.equal(focused, code);
    assert.deepEqual(requests, ['/save']);
    await cancel.emit('click');
    await document.emit('submit', operation);
    assert.ok(messages.every(m => !m.textContent));
    assert.equal(code.getAttribute('aria-invalid'), null);
    password.value = 'test-password'; code.value = '123456'; rejectVerification = true;
    await verification.emit('submit');
    assert.equal(errors.textContent, 'Invalid verification code.');
    assert.equal(dialog.open, true);
    assert.equal(operation.elements.Title.value, 'Course draft');
    await cancel.emit('click');
    hasPassword = false; rejectVerification = false;
    await document.emit('submit', operation);
    assert.equal(password.required, false);
    assert.equal(passwordContainer.hidden, true);
    await sms.emit('click');
    assert.equal(notice.textContent, 'SMS code sent.');
    code.value = '123456';
    await verification.emit('submit');
    assert.deepEqual(redirects, ['/courses']);
    assert.equal(requests.filter(url => url === '/verify').length, 2);
    assert.equal(dialog.open, false);
    console.log('Admin verification inline validation passed: required fields, correction, reopen, server failure, SMS, and automatic continuation.');
})().catch(error => { console.error(error); process.exitCode = 1; });
