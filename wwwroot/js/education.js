(() => {
    const form = document.querySelector('[data-experience-form]');
    if (!form) return;
    const rows = form.querySelector('[data-experience-rows]');
    const add = form.querySelector('[data-add-experience]');
    const refresh = () => {
        const items = [...rows.querySelectorAll('[data-experience-row]')];
        items.forEach((row, index) => {
            row.querySelector('legend').textContent = `Teaching role ${index + 1}`;
            row.querySelectorAll('input').forEach(input => {
                const field = input.name.split('.').pop();
                input.name = `Experience[${index}].${field}`;
                input.id = `Experience_${index}__${field}`;
                input.previousElementSibling.htmlFor = input.id;
            });
            row.querySelector('[data-remove-experience]').disabled = items.length === 1;
        });
        add.disabled = items.length >= 10;
    };
    add.addEventListener('click', () => {
        if (rows.children.length >= 10) return;
        const row = rows.firstElementChild.cloneNode(true);
        row.querySelectorAll('input').forEach(input => { input.value = ''; input.removeAttribute('value'); });
        rows.appendChild(row); refresh(); row.querySelector('input').focus();
    });
    rows.addEventListener('click', event => {
        if (!event.target.matches('[data-remove-experience]') || rows.children.length <= 1) return;
        event.target.closest('[data-experience-row]').remove(); refresh();
    });
    refresh();
})();
