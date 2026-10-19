// Razor emits a hidden false value beside the IsPublished checkbox.
// Exercise the actual dirty-state handler with both controls present.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const path = require('node:path');
const fields = [
    { name: 'Title', type: 'text', value: 'Security foundations' },
    { name: 'Category', type: 'text', value: '' },
    { name: 'Description', type: 'textarea', value: 'Practical security' },
    { name: 'IsPublished', type: 'checkbox', checked: true },
    { name: 'IsPublished', type: 'hidden', value: 'false' }
];
const handlers = {}, save = { disabled: false };
const form = {
    dataset: { originalValues: JSON.stringify({ Title: fields[0].value, Category: null, Description: fields[2].value, IsPublished: true }) },
    elements: fields,
    querySelector: () => save,
    addEventListener: (name, handler) => (handlers[name] ??= []).push(handler)
};
vm.runInNewContext(fs.readFileSync(path.join(__dirname, '../../wwwroot/js/account-forms.js'), 'utf8'), {
    document: { querySelectorAll: () => [form], querySelector: () => null }, queueMicrotask
});
assert.equal(save.disabled, true, 'unchanged form starts disabled');
fields[0].value += ' updated'; handlers.input[0]();
assert.equal(save.disabled, false, 'changing text enables save');
fields[0].value = 'Security foundations'; handlers.input[0]();
assert.equal(save.disabled, true, 'reverting text disables save');
fields[3].checked = false; handlers.change[0]();
assert.equal(save.disabled, false, 'publication-only change enables save');
fields[3].checked = true; handlers.change[0]();
assert.equal(save.disabled, true, 'restoring publication state disables save despite hidden helper');
fields[1].value = '  '; handlers.input[0]();
assert.equal(save.disabled, true, 'whitespace-only optional value remains unchanged');
console.log('All 6 course form state checks passed.');
