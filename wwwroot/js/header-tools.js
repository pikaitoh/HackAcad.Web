(() => {
    const menus = [...document.querySelectorAll('[data-header-menu], [data-account-menu]')];
    menus.forEach(menu => menu.addEventListener('toggle', () => { if (menu.open) menus.filter(other => other !== menu).forEach(other => other.open = false); }));
    document.addEventListener('click', event => menus.filter(menu => !menu.contains(event.target)).forEach(menu => menu.open = false));
    document.addEventListener('keydown', event => { if (event.key === 'Escape') menus.filter(m => m.open).forEach(menu => { menu.open = false; menu.querySelector('summary').focus(); }); });
    document.querySelectorAll('[data-appearance-form]').forEach(form => {
        form.querySelectorAll('button[value]').forEach(button => button.setAttribute('aria-pressed', String(button.value === document.documentElement.dataset.appearance)));
        form.addEventListener('submit', async event => {
            event.preventDefault(); const value = event.submitter?.value; if (!value) return;
            const error = form.querySelector('[data-appearance-error]'); error.textContent = '';
            if (form.dataset.guest === 'true') {
                try { localStorage.setItem('hackacad-appearance', value); } catch { /* Current tab still supports switching. */ }
                window.HackAcadAppearance(value); form.closest('details').open = false; form.closest('details').querySelector('summary').focus(); return;
            }
            const data = new FormData(form); data.set('value', value);
            try {
                const response = await fetch(form.action, { method: 'POST', body: data, headers: { 'X-Requested-With': 'XMLHttpRequest' } });
                if (!response.ok || response.redirected) throw new Error();
                window.HackAcadAppearance(value);
                form.closest('details').open = false; form.closest('details').querySelector('summary').focus();
            } catch { error.textContent = 'Could not save your appearance. Please try again.'; }
        });
    });
    const header = document.querySelector('.site-header');
    const scroll = () => header?.classList.toggle('is-scrolled', window.scrollY > 4);
    addEventListener('scroll', scroll, { passive: true }); scroll();
})();
