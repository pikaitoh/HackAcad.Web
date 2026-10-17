// Read Razor's server-generated validation rules; show one message beside each field.
// Without JavaScript, native constraints and server validation still apply.
document.querySelectorAll('form[data-account-form], form[data-validated-form]').forEach(form => {
    const fields = [...form.querySelectorAll('input[data-val="true"], select[data-val="true"], textarea[data-val="true"]')]
        .filter(field => field.type !== 'hidden' && field.type !== 'checkbox');
    form.noValidate = true;

    const messageFor = field => {
        const value = field.hasAttribute('data-trim') ? field.value.trim() : field.value;
        const rules = field.dataset;
        if (!value) return rules.valRequired || '';
        if (field.type === 'file' && field.files[0]?.size > Number(field.dataset.fileMaxSize))
            return 'Choose an image up to 2 MB.';
        if (rules.valMatchValue !== undefined && value.toLowerCase() !== rules.valMatchValue.toLowerCase()) return rules.valMatch;
        if (field.validity.typeMismatch) return rules.valEmail || 'Enter a valid email address.';
        if ((rules.valLengthMin && value.length < Number(rules.valLengthMin)) ||
            (rules.valLengthMax && value.length > Number(rules.valLengthMax))) return rules.valLength;
        if (rules.valRegexPattern && !new RegExp(rules.valRegexPattern, 'u').test(value)) return rules.valRegex;
        const other = rules.valEqualtoOther?.replace(/^\*\./, '');
        if (other && value !== form.elements.namedItem(other)?.value) return rules.valEqualto;
        return '';
    };
    const validate = field => {
        const message = messageFor(field);
        const error = form.querySelector('[data-valmsg-for="' + field.name + '"]');
        if (error) {
            error.textContent = message;
            error.classList.toggle('field-validation-error', Boolean(message));
            error.classList.toggle('field-validation-valid', !message);
        }
        field.setAttribute('aria-invalid', message ? 'true' : 'false');
        field.classList.toggle('input-validation-error', Boolean(message));
        return !message;
    };
    fields.forEach(field => {
        field.addEventListener('blur', () => {
            if (field.hasAttribute('data-trim')) field.value = field.value.trim();
            if (field.value || field.getAttribute('aria-invalid') === 'true') validate(field);
        });
        field.addEventListener('input', () => {
            if (field.getAttribute('aria-invalid') === 'true') validate(field);
            fields.filter(other => other.dataset.valEqualtoOther?.endsWith('.' + field.name) &&
                other.getAttribute('aria-invalid') === 'true').forEach(validate);
        });
    });
    form.addEventListener('submit', event => {
        fields.filter(field => field.hasAttribute('data-trim')).forEach(field => field.value = field.value.trim());
        const invalid = fields.filter(field => !validate(field));
        if (invalid.length) {
            event.preventDefault();
            invalid[0].focus();
        }
    });
});

const invalidField = document.querySelector('[aria-invalid="true"]');
if (invalidField) invalidField.focus();
