// Run: node tests/admin_form_validation_test.js. No database, network or extra packages.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const root = process.argv[2] || path.resolve(__dirname, '..');
let focused, checks = 0;
class Element {
    constructor(properties = {}) {
        Object.assign(this, { listeners: {}, attributes: {}, dataset: {}, validity: {}, value: '', textContent: '', type: 'text', disabled: false }, properties);
        const classes = new Set();
        this.classList = { toggle: (name, enabled) => enabled ? classes.add(name) : classes.delete(name), contains: name => classes.has(name) };
    }
    addEventListener(name, handler) { (this.listeners[name] ||= []).push(handler); }
    emit(name, target = this) {
        const event = { target, defaultPrevented: false, preventDefault() { this.defaultPrevented = true; } };
        for (const handler of this.listeners[name] || []) handler(event);
        return event;
    }
    setAttribute(name, value) { this.attributes[name] = value; }
    getAttribute(name) { return this.attributes[name] ?? null; }
    hasAttribute(name) { return name in this.attributes; }
    focus() { focused = this; }
    insertAdjacentElement(position, element) { assert.equal(position, 'afterend'); this.form.messages.push(element); }
}
class Form extends Element {
    constructor(fields, messages = []) {
        super(); this.elements = fields; this.messages = messages;
        this.elements.namedItem = name => fields.find(field => field.name === name);
        fields.forEach(field => field.form = this);
    }
    querySelectorAll(selector) { return selector === 'input,select,textarea' ? this.elements : selector === '[data-valmsg-for]' ? this.messages : []; }
    reportValidity() { assert.fail('Native popup validation must not be used.'); }
}
const field = (name, properties = {}) => new Element({ name, id: name.replaceAll('.', '_'), ...properties });
const body = field('Body', { required: true, dataset: { valRequired: 'Please enter a message.', valLength: 'Please enter between 10 and 3,000 characters.' }, attributes: { minlength: '10', maxlength: '3000', 'data-trim': '', 'aria-describedby': 'message-help' } });
const reply = new Form([body]);
const notes = field('Notes', { required: true, dataset: { valRequired: 'Please enter investigation notes.' }, attributes: { minlength: '10', maxlength: '1000', 'data-trim': '' } });
const alert = new Form([notes]);
const reason = field('Input.Reason', { dataset: { val: 'true', requiredMessage: 'Explain why this course is rejected so the instructor can revise it.' }, attributes: { maxlength: '1000', 'data-trim': '' } });
const review = new Form([reason]);
const years = field('VerifiedYears', { type: 'number', dataset: { val: 'true' }, attributes: { min: '0', max: '80' } });
const application = new Form([years]);
const from = field('From', { type: 'date', value: '2026-11-02' });
const to = field('To', { type: 'date', value: '2026-11-01', dataset: { valAfterOther: 'From', valAfter: 'Choose an end date on or after the start date.' } });
const dates = new Form([from, to]);
const password = field('CurrentPassword', { type: 'password', required: false, dataset: { valRequired: 'Enter your current password.' }, attributes: { maxlength: '100' } });
const verification = new Form([password]);
const forms = [reply, alert, review, application, dates, verification];
const document = {
    querySelectorAll(selector) { assert.ok(selector.includes('form[data-admin-operation]'), 'Every Admin operation must use inline validation'); return forms; },
    querySelector() { return null; },
    createElement(tag) { assert.equal(tag, 'span'); return new Element(); }
};
vm.runInNewContext(fs.readFileSync(path.join(root, 'wwwroot/js/account-forms.js'), 'utf8'), { document, queueMicrotask });
function check(condition, message) { assert.ok(condition, message); checks++; }
function message(form, name) { return form.messages.find(item => item.dataset.valmsgFor === name)?.textContent; }
check(forms.every(form => form.noValidate), 'Native popup validation is disabled for Admin and shared forms');
check(reply.emit('submit').defaultPrevented, 'An empty support reply is blocked');
check(message(reply, 'Body') === 'Please enter a message.', 'A missing message element is created with readable text');
check(body.getAttribute('aria-describedby').includes('message-help') && body.getAttribute('aria-describedby').includes('Body-error'), 'Help and error text remain associated with the field');
check(body.getAttribute('aria-invalid') === 'true' && focused === body, 'The first invalid field is focused and announced');
body.value = '          ';
check(reply.emit('submit').defaultPrevented && message(reply, 'Body') === 'Please enter a message.', 'Whitespace-only replies are rejected');
body.value = 'too short';
check(reply.emit('submit').defaultPrevented && message(reply, 'Body').includes('10 and 3,000'), 'Short replies explain the allowed length');
body.value = 'A complete support reply.';
reply.emit('input', body);
check(message(reply, 'Body') === '' && body.getAttribute('aria-invalid') === 'false', 'Correcting a reply clears its error');
check(!reply.emit('submit').defaultPrevented && reply.messages.length === 1, 'A valid reply proceeds without duplicated messages');
notes.value = '';
check(alert.emit('submit').defaultPrevented && message(alert, 'Notes') === 'Please enter investigation notes.', 'Empty investigation notes have an inline message');
check(!review.emit('submit').defaultPrevented, 'Approval permits empty optional course feedback');
reason.required = true;
check(review.emit('submit').defaultPrevented && message(review, 'Input.Reason').startsWith('Explain why'), 'Rejection requires explanatory feedback');
reason.required = false;
review.emit('input', reason);
check(message(review, 'Input.Reason') === '', 'Changing the decision back to approval clears the conditional error');
for (const value of ['-1', '81']) {
    years.value = value;
    check(application.emit('submit').defaultPrevented && message(application, 'VerifiedYears') === 'Enter a number between 0 and 80.', 'Out-of-range teaching experience has a readable error');
}
years.value = '5';
check(!application.emit('submit').defaultPrevented, 'Valid teaching experience proceeds');
check(dates.emit('submit').defaultPrevented && message(dates, 'To').startsWith('Choose an end date'), 'Reversed login-log dates have an inline error');
from.value = '2026-10-31';
dates.emit('change', from);
check(message(dates, 'To') === '' && !dates.emit('submit').defaultPrevented, 'Correcting the start date clears the end-date error');
check(!verification.emit('submit').defaultPrevented, 'An external-login Admin without a password is not forced to supply one');
password.required = true;
check(verification.emit('submit').defaultPrevented && message(verification, 'CurrentPassword') === 'Enter your current password.', 'A password-owning Admin must supply the password');
for (const file of ['Views/Admin/SupportThread.cshtml', 'Views/Admin/Alert.cshtml', 'Views/Admin/Sessions.cshtml', 'Views/Admin/UserDetails.cshtml', 'Views/Support/Thread.cshtml'])
    check(/data-valmsg-for=|asp-validation-for=/.test(fs.readFileSync(path.join(root, file), 'utf8')), file + ' retains a server-rendered error element');
check(!fs.readFileSync(path.join(root, 'wwwroot/js/admin-operations.js'), 'utf8').includes('.reportValidity('), 'Sensitive operations do not reintroduce browser popups');
console.log(`PASS ${checks} Admin/shared-form validation checks: inline messages, whitespace, lengths, conditional feedback, number/date rules, correction and MFA prerequisites.`);
