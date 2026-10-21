(() => {
    document.querySelectorAll('[data-chart]').forEach(chart => {
        const tooltip = chart.querySelector('[role="tooltip"]');
        if (!tooltip) return;
        const hide = () => { tooltip.hidden = true; };
        const show = point => {
            const title = document.createElement('strong');
            const value = document.createElement('span');
            title.textContent = point.dataset.chartLabel;
            value.textContent = point.dataset.chartValue;
            tooltip.replaceChildren(title, value);
            tooltip.hidden = false;
            const area = chart.getBoundingClientRect();
            const target = point.getBoundingClientRect();
            const left = target.left - area.left + target.width / 2 - tooltip.offsetWidth / 2;
            tooltip.style.left = `${Math.max(0, Math.min(left, area.width - tooltip.offsetWidth))}px`;
            tooltip.style.top = `${Math.max(0, target.top - area.top - tooltip.offsetHeight - 8)}px`;
        };
        const points = [...chart.querySelectorAll('[data-chart-label]')];
        points.forEach((point, index) => {
            point.addEventListener('pointerenter', () => show(point));
            point.addEventListener('pointerleave', hide);
            point.addEventListener('focus', () => {
                points.forEach(item => item.setAttribute('tabindex', item === point ? '0' : '-1'));
                show(point);
            });
            point.addEventListener('blur', hide);
            point.addEventListener('keydown', event => {
                if (event.key === 'Escape') { hide(); return; }
                const targets = {ArrowLeft: Math.max(0, index - 1), ArrowRight: Math.min(points.length - 1, index + 1), Home: 0, End: points.length - 1};
                if (event.key in targets) { event.preventDefault(); points[targets[event.key]].focus(); }
            });
        });
    });
})();
