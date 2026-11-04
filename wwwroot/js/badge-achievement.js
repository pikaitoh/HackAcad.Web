(() => {
    'use strict';

    const dialog = document.getElementById('badge-achievement-dialog');
    if (!dialog) return;

    const userKey = dialog.dataset.userKey || 'current-user';
    const queueStorageKey = `hackacad.badge-achievements.queue.${userKey}`;
    const shownStorageKey = `hackacad.badge-achievements.shown.${userKey}`;
    const iconHost = dialog.querySelector('[data-badge-icon]');
    const title = dialog.querySelector('[data-badge-title]');
    const description = dialog.querySelector('[data-badge-description]');
    const congratulation = dialog.querySelector('[data-badge-congratulation]');
    const confettiHost = dialog.querySelector('[data-badge-confetti]');
    const shownIds = readStoredIds(shownStorageKey);
    const soundPlayedIds = new Set();
    let queue = readStoredQueue();
    let currentBadge = null;
    let confettiTimer = 0;

    function readStoredIds(key) {
        try {
            const value = JSON.parse(sessionStorage.getItem(key) || '[]');
            return new Set(Array.isArray(value) ? value.filter(id => typeof id === 'string') : []);
        } catch {
            return new Set();
        }
    }

    function readStoredQueue() {
        try {
            const value = JSON.parse(sessionStorage.getItem(queueStorageKey) || '[]');
            return Array.isArray(value) ? value.map(normalizeBadge).filter(Boolean) : [];
        } catch {
            return [];
        }
    }

    function normalizeBadge(value) {
        if (!value || typeof value !== 'object') return null;
        const id = value.id ?? value.Id;
        const badgeTitle = value.title ?? value.Title;
        if (typeof id !== 'string' || !id || typeof badgeTitle !== 'string' || !badgeTitle) return null;

        const badgeColor = value.colorHex ?? value.ColorHex;
        return {
            id,
            title: badgeTitle,
            description: String(value.description ?? value.Description ?? ''),
            iconSvg: String(value.iconSvg ?? value.IconSvg ?? ''),
            colorHex: typeof badgeColor === 'string' && /^#[0-9a-f]{6}$/i.test(badgeColor)
                ? badgeColor
                : '#33796e'
        };
    }

    function saveState() {
        try {
            sessionStorage.setItem(queueStorageKey, JSON.stringify(queue));
            sessionStorage.setItem(shownStorageKey, JSON.stringify([...shownIds]));
        } catch {
            // The in-memory queue still works when browser storage is unavailable.
        }
    }

    function enqueue(badges) {
        if (!Array.isArray(badges)) return;

        for (const value of badges) {
            const badge = normalizeBadge(value);
            if (!badge || shownIds.has(badge.id) || queue.some(item => item.id === badge.id)) continue;
            queue.push(badge);
        }

        saveState();
        showNext();
    }

    function importBadgeIcon(markup) {
        if (!iconHost) return;
        iconHost.replaceChildren();

        try {
            const documentFromSvg = new DOMParser().parseFromString(markup, 'image/svg+xml');
            const root = documentFromSvg.documentElement;
            const allowedTags = new Set(['svg', 'path', 'circle', 'rect', 'line', 'polyline', 'polygon']);
            const allowedAttributes = new Set([
                'viewBox', 'fill', 'stroke', 'stroke-width', 'stroke-linecap', 'stroke-linejoin',
                'aria-hidden', 'd', 'cx', 'cy', 'r', 'x', 'y', 'width', 'height', 'rx', 'points'
            ]);
            const elements = [root, ...root.querySelectorAll('*')];

            if (root.localName !== 'svg' || elements.some(element => !allowedTags.has(element.localName))) {
                throw new Error('Unsupported badge artwork.');
            }

            for (const element of elements) {
                for (const attribute of [...element.attributes]) {
                    if (!allowedAttributes.has(attribute.name)) element.removeAttribute(attribute.name);
                }
            }

            // Reparse the sanitized SVG in the HTML document so the browser renders it as inline SVG.
            iconHost.innerHTML = root.outerHTML;
        } catch {
            const fallback = document.createElement('span');
            fallback.textContent = '★';
            fallback.setAttribute('aria-hidden', 'true');
            iconHost.appendChild(fallback);
        }
    }

    function throwConfetti() {
        if (!confettiHost) return;
        window.clearTimeout(confettiTimer);
        confettiHost.replaceChildren();

        const colors = ['#f2cf45', '#33796e', '#624080', '#d9534f', '#62b891'];
        for (let index = 0; index < 28; index++) {
            const piece = document.createElement('span');
            piece.className = 'badge-confetti-piece';
            piece.style.setProperty('--piece-x', `${Math.random() * 100}%`);
            piece.style.setProperty('--piece-drift', `${Math.round(Math.random() * 150 - 75)}px`);
            piece.style.setProperty('--piece-spin', `${Math.round(Math.random() * 720 - 360)}deg`);
            piece.style.setProperty('--piece-delay', `${Math.random() * 0.35}s`);
            piece.style.setProperty('--piece-duration', `${1.4 + Math.random() * 0.7}s`);
            piece.style.setProperty('--piece-color', colors[index % colors.length]);
            confettiHost.appendChild(piece);
        }

        confettiTimer = window.setTimeout(() => confettiHost.replaceChildren(), 2400);
    }

    async function playAchievementSound(id) {
        if (soundPlayedIds.has(id)) return;
        soundPlayedIds.add(id);

        const AudioContextClass = window.AudioContext || window.webkitAudioContext;
        if (!AudioContextClass) return;

        let audioContext;
        try {
            audioContext = new AudioContextClass();
            await audioContext.resume();
            const startTime = audioContext.currentTime;
            [659.25, 783.99, 987.77].forEach((frequency, index) => {
                const oscillator = audioContext.createOscillator();
                const gain = audioContext.createGain();
                const noteStart = startTime + index * 0.11;
                oscillator.type = 'sine';
                oscillator.frequency.setValueAtTime(frequency, noteStart);
                gain.gain.setValueAtTime(0.0001, noteStart);
                gain.gain.exponentialRampToValueAtTime(0.055, noteStart + 0.025);
                gain.gain.exponentialRampToValueAtTime(0.0001, noteStart + 0.42);
                oscillator.connect(gain);
                gain.connect(audioContext.destination);
                oscillator.start(noteStart);
                oscillator.stop(noteStart + 0.44);
            });
            window.setTimeout(() => audioContext.close().catch(() => {}), 900);
        } catch {
            if (audioContext) audioContext.close().catch(() => {});
            // Audio restrictions must never prevent the achievement popup.
        }
    }

    function showNext() {
        if (currentBadge || queue.length === 0) return;

        currentBadge = queue.shift();
        shownIds.add(currentBadge.id);
        saveState();

        title.textContent = currentBadge.title;
        description.textContent = currentBadge.description;
        congratulation.textContent = `You did it! You earned the ${currentBadge.title} badge.`;
        iconHost.style.setProperty('--badge-earned-color', currentBadge.colorHex);
        importBadgeIcon(currentBadge.iconSvg);
        throwConfetti();

        if (typeof dialog.showModal === 'function') {
            try {
                dialog.showModal();
            } catch {
                dialog.setAttribute('open', '');
            }
        } else {
            dialog.setAttribute('open', '');
        }

        dialog.classList.remove('badge-achievement-revealing');
        requestAnimationFrame(() => dialog.classList.add('badge-achievement-revealing'));
        playAchievementSound(currentBadge.id);
    }

    function dismissCurrent() {
        if (!currentBadge) return;
        currentBadge = null;
        dialog.classList.remove('badge-achievement-revealing');
        if (dialog.open && typeof dialog.close === 'function') dialog.close();
        else dialog.removeAttribute('open');
        window.setTimeout(showNext, 80);
    }

    dialog.querySelectorAll('[data-badge-close]').forEach(button => {
        button.addEventListener('click', dismissCurrent);
    });
    dialog.addEventListener('cancel', event => {
        event.preventDefault();
        dismissCurrent();
    });
    dialog.addEventListener('click', event => {
        if (event.target === dialog) dismissCurrent();
    });

    window.HackAcadBadgeAchievements = Object.freeze({ enqueue });

    const bootData = document.getElementById('badge-achievement-boot-data');
    if (bootData) {
        try {
            enqueue(JSON.parse(bootData.textContent || '[]'));
        } catch {
            // Invalid one-time page data is ignored; the rest of the page remains usable.
        }
    }
    showNext();
})();
