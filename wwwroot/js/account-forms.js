// Native HTML validation handles required fields and email syntax.
// Keep password confirmation in sync before the form reaches the server.
document.querySelectorAll('[data-match-password]').forEach(confirmation => {
    const password = document.getElementById(confirmation.dataset.matchPassword);
    if (!password) return;
    const validate = () => confirmation.setCustomValidity(
        confirmation.value && confirmation.value !== password.value ? 'The passwords do not match.' : ''
    );
    confirmation.addEventListener('input', validate);
    password.addEventListener('input', validate);
});

const invalidField = document.querySelector('[aria-invalid="true"]');
if (invalidField) invalidField.focus();
