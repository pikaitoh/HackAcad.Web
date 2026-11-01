// Share Razor's validation rules, including dynamically added application rows.
(() => {
    const fields = form => [...form.querySelectorAll('input,select,textarea')].filter(f => f.type !== 'hidden' && f.type !== 'submit' && !f.disabled && (f.dataset.val === 'true' || f.required || f.type === 'date' || f.type === 'number' || f.hasAttribute('pattern')));
    const messageFor = (field, form) => {
        const value = field.hasAttribute('data-trim') ? field.value.trim() : field.value;
        const rules = field.dataset;
        if (field.validity.badInput) return field.type === 'number' ? 'Enter a valid number.' : 'Enter a valid date.';
        if (field.type === 'checkbox') return field.required && !field.checked ? rules.valRange || rules.valRequired || 'Please confirm this declaration.' : '';
        if (!value) return rules.valRequired || (field.required ? rules.requiredMessage || 'Please complete this field.' : '');
        if (field.type === 'file') {
            if (field.files[0]?.size > Number(rules.fileMaxSize)) return rules.fileKind === 'pdf' ? 'Upload a PDF up to 5 MB.' : 'Choose an image up to 2 MB.';
            if (rules.fileKind === 'pdf' && !/\.pdf$/i.test(field.files[0]?.name || '')) return 'Upload a PDF document.';
        }
        if (rules.valEmaildomainMap && value.includes('@')) {
            const domain = value.slice(value.lastIndexOf('@') + 1).toLowerCase();
            const suggestion = JSON.parse(rules.valEmaildomainMap)[domain];
            if (suggestion) return rules.valEmaildomain.replace('{0}', suggestion);
        }
        if (rules.valMatchValue !== undefined && value.toLowerCase() !== rules.valMatchValue.toLowerCase()) return rules.valMatch;
        if (field.validity.typeMismatch || (rules.valEmail && !/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(value))) return rules.valEmail || 'Please enter a valid email address.';
        const min = Number(rules.valLengthMin || field.getAttribute('minlength') || 0);
        const max = Number(rules.valLengthMax || field.getAttribute('maxlength') || Infinity);
        if (value.length < min || value.length > max) return rules.valLength || `Use between ${min} and ${max} characters.`;
        if (field.type === 'number') {
            const lower = rules.valRangeMin ?? field.getAttribute('min');
            const upper = rules.valRangeMax ?? field.getAttribute('max');
            if ((lower !== null && Number(value) < Number(lower)) || (upper !== null && Number(value) > Number(upper)))
                return rules.valRange || (lower !== null && upper !== null ? `Enter a number between ${lower} and ${upper}.` : lower !== null ? `Enter a number of at least ${lower}.` : `Enter a number no greater than ${upper}.`);
            if (field.validity.stepMismatch) return 'Enter a number using the allowed increment.';
        }
        if (field.type === 'date' && (field.validity.rangeUnderflow || field.validity.rangeOverflow || field.validity.badInput)) return 'Enter a date between 1950 and today.';
        const startField = rules.valAfterOther || (field.name.endsWith('.EndDate') ? field.name.replace('.EndDate', '.StartDate') : null);
        if (startField) {
            const start = form.elements.namedItem(startField)?.value;
            if (start && value < start) return rules.valAfter || 'End date must be on or after the start date.';
        }
        if (rules.valRegexPattern && !new RegExp(rules.valRegexPattern, 'u').test(value)) return rules.valRegex;
        if (field.validity.patternMismatch) return 'Enter a value in the required format.';
        const other = rules.valEqualtoOther?.replace(/^\*\./, '');
        if (other && value !== form.elements.namedItem(other)?.value) return rules.valEqualto;
        return '';
    };
    const validate = (field, form) => {
        const message = messageFor(field, form);
        let error = [...form.querySelectorAll('[data-valmsg-for]')].find(e => e.dataset.valmsgFor === field.name);
        // Keep every invalid field readable, even when a view omitted its message element.
        if (!error && message) {
            error = document.createElement('span');
            error.className = 'field-error';
            error.dataset.valmsgFor = field.name;
            error.setAttribute('aria-live', 'polite');
            field.insertAdjacentElement('afterend', error);
        }
        if (error) {
            if (!error.id) error.id = `${field.id || field.name.replace(/[^\w-]/g, '-')}-error`;
            const describedBy = new Set((field.getAttribute('aria-describedby') || '').split(/\s+/).filter(Boolean));
            describedBy.add(error.id);
            field.setAttribute('aria-describedby', [...describedBy].join(' '));
            error.textContent = message;
            error.classList.toggle('field-validation-error', !!message);
            error.classList.toggle('field-validation-valid', !message);
        }
        field.setAttribute('aria-invalid', message ? 'true' : 'false');
        field.classList.toggle('input-validation-error', !!message);
        return !message;
    };
    document.querySelectorAll('form[data-account-form],form[data-validated-form],form[data-admin-operation]').forEach(form => {
        form.noValidate = true;
        if (form.dataset.originalValues) {
            const original = JSON.parse(form.dataset.originalValues);
            const save = form.querySelector('button[type="submit"]');
            const updateSave = () => {
                save.disabled = Object.entries(original).every(([name, value]) => {
                    const field = [...form.elements].find(f => f.name === name && f.type !== 'hidden');
                    return field.type === 'checkbox' ? field.checked === value : field.value.trim() === (value ?? '').trim();
                });
            };
            form.addEventListener('input', updateSave); form.addEventListener('change', updateSave);
            form.addEventListener('reset', () => queueMicrotask(updateSave));
            updateSave();
        }
        form.addEventListener('blur', event => {
            if (!fields(form).includes(event.target)) return;
            if (event.target.hasAttribute('data-trim')) event.target.value = event.target.value.trim();
            validate(event.target, form);
        }, true);
        const changed = event => {
            if (!fields(form).includes(event.target)) return;
            if (event.target.getAttribute('aria-invalid') === 'true') validate(event.target, form);
            fields(form).filter(other => other.getAttribute('aria-invalid') === 'true' && (other.dataset.valEqualtoOther?.endsWith('.' + event.target.name) || other.dataset.valAfterOther === event.target.name || other.name.endsWith('.EndDate'))).forEach(other => validate(other, form));
        };
        form.addEventListener('input', changed); form.addEventListener('change', changed);
        form.addEventListener('submit', event => {
            fields(form).filter(f => f.hasAttribute('data-trim') && f.type !== 'file').forEach(f => f.value = f.value.trim());
            const invalid = fields(form).filter(f => !validate(f, form));
            if (invalid.length) { event.preventDefault(); invalid[0].focus(); }
        });
    });
    const invalid = document.querySelector('[aria-invalid="true"], .input-validation-error');
    if (invalid) invalid.focus();
})();
