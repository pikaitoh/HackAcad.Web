(() => {
    const preview = document.querySelector('[data-learning-preview]');
    if (!preview) return;
    const tabs = [...preview.querySelectorAll('[role="tab"]')];
    const panels = [...preview.querySelectorAll('[role="tabpanel"]')];
    const select = (tab) => {
        tabs.forEach(item => {
            const selected = item === tab;
            item.setAttribute('aria-selected', String(selected));
            item.tabIndex = selected ? 0 : -1;
        });
        panels.forEach(panel => { panel.hidden = panel.id !== tab.getAttribute('aria-controls'); });
    };
    tabs.forEach((tab, index) => {
        tab.addEventListener('click', () => select(tab));
        tab.addEventListener('keydown', event => {
            let next;
            if (event.key === 'ArrowDown' || event.key === 'ArrowRight') next = (index + 1) % tabs.length;
            if (event.key === 'ArrowUp' || event.key === 'ArrowLeft') next = (index + tabs.length - 1) % tabs.length;
            if (event.key === 'Home') next = 0;
            if (event.key === 'End') next = tabs.length - 1;
            if (next === undefined) return;
            event.preventDefault();
            select(tabs[next]);
            tabs[next].focus();
        });
    });
})();
