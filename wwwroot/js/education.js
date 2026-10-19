(() => {
    const form = document.querySelector('[data-experience-form]');
    if (!form) return;
    const rows = form.querySelector('[data-experience-rows]');
    const add = form.querySelector('[data-add-experience]');
    const groupError = form.querySelector('#experience-error');
    const refresh = () => {
        const items = [...rows.querySelectorAll('[data-experience-row]')];
        items.forEach((row, index) => {
            row.querySelector('legend').textContent = `Teaching role ${index + 1}`;
            row.querySelectorAll('input:not([type="hidden"])').forEach(input => {
                const field = input.name.split('.').pop();
                input.name = `Experience[${index}].${field}`; input.id = `Experience_${index}__${field}`;
                input.closest('div').querySelector('label').htmlFor = input.id;
                const error = input.closest('div').querySelector('[data-valmsg-for]');
                error.dataset.valmsgFor = input.name; error.id = input.id + '-error'; input.setAttribute('aria-describedby', error.id);
            });
            row.querySelectorAll('input[name="__Invariant"]').forEach(input => {
                input.value = input.value.replace(/Experience\[\d+\]/, `Experience[${index}]`);
            });
            row.querySelector('[data-remove-experience]').disabled = items.length === 1;
        });
        add.disabled = items.length >= 10;
    };
    add.addEventListener('click', () => {
        if (rows.children.length >= 10) return;
        const row = rows.firstElementChild.cloneNode(true);
        row.querySelectorAll('input:not([type="hidden"])').forEach(input => { input.value = ''; input.removeAttribute('value'); input.removeAttribute('aria-invalid'); input.classList.remove('input-validation-error'); });
        row.querySelectorAll('[data-valmsg-for]').forEach(e => { e.textContent = ''; e.classList.remove('field-validation-error'); });
        rows.appendChild(row); refresh(); row.querySelector('input').focus(); groupError.textContent = '';
    });
    rows.addEventListener('click', event => {
        if (!event.target.matches('[data-remove-experience]') || rows.children.length <= 1) return;
        event.target.closest('[data-experience-row]').remove(); refresh(); groupError.textContent = '';
    });
    rows.addEventListener('input', () => groupError.textContent = '');
    form.addEventListener('submit', event => {
        if (event.defaultPrevented) return;
        const day = value => Date.parse(value + 'T00:00:00Z') / 86400000;
        const today = Math.floor(Date.now() / 86400000);
        const current = new Date(today * 86400000), year = current.getUTCFullYear() - 5, month = current.getUTCMonth();
        const previous = new Date(Date.UTC(year, month, Math.min(current.getUTCDate(), new Date(Date.UTC(year, month + 1, 0)).getUTCDate())));
        let total = 0, until = 0;
        const periods = [...rows.querySelectorAll('[data-experience-row]')].map(row => {
            const start = row.querySelector('[name$=".StartDate"]').value, end = row.querySelector('[name$=".EndDate"]').value;
            return [day(start), end && day(end) < today ? day(end) + 1 : today];
        }).sort((a, b) => a[0] - b[0]);
        for (const [start, end] of periods) { total += Math.max(0, end - Math.max(start, until)); until = Math.max(until, end); }
        if (total < today - previous.getTime() / 86400000) {
            event.preventDefault(); groupError.textContent = 'At least five years of relevant teaching experience is required. Overlapping periods count once.';
            rows.querySelector('input[name$=".StartDate"]').focus();
        }
    });
    refresh();
})();
