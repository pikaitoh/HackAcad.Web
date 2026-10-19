(() => {
    const root = document.documentElement;
    const media = matchMedia('(prefers-color-scheme: dark)');
    const apply = preference => {
        root.dataset.appearance = preference;
        root.dataset.theme = preference === 'system' ? (media.matches ? 'dark' : 'light') : preference;
        document.querySelectorAll('[data-appearance-form] button[value]').forEach(b => b.setAttribute('aria-pressed', String(b.value === preference)));
    };
    let preference = root.dataset.appearance;
    if (preference === 'guest') { try { preference = localStorage.getItem('hackacad-appearance') || 'system'; } catch { preference = 'system'; } }
    apply(['system', 'dark', 'light'].includes(preference) ? preference : 'system');
    media.addEventListener('change', () => { if (root.dataset.appearance === 'system') apply('system'); });
    window.HackAcadAppearance = apply;
})();
