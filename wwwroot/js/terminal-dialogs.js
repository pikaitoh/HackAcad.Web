/**
 * HackAcad Terminal Module: Centralized Application-Level Dialogs & Notifications
 * Provides toasts, accessible confirmation modals, and prompt dialogs
 * fully styled for Light and Dark modes without browser-native popups.
 */
(() => {
    'use strict';

    // SVG Icons
    const ICONS = {
        success: `<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><polyline points="20 6 9 17 4 12"></polyline></svg>`,
        error: `<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><circle cx="12" cy="12" r="10"></circle><line x1="15" y1="9" x2="9" y2="15"></line><line x1="9" y1="9" x2="15" y2="15"></line></svg>`,
        warning: `<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M10.29 3.86L1.82 18a2 2 0 0 0 1.71 3h16.94a2 2 0 0 0 1.71-3L13.71 3.86a2 2 0 0 0-3.42 0z"></path><line x1="12" y1="9" x2="12" y2="13"></line><line x1="12" y1="17" x2="12.01" y2="17"></line></svg>`,
        info: `<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><circle cx="12" cy="12" r="10"></circle><line x1="12" y1="16" x2="12" y2="12"></line><line x1="12" y1="8" x2="12.01" y2="8"></line></svg>`,
        close: `<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><line x1="18" y1="6" x2="6" y2="18"></line><line x1="6" y1="6" x2="18" y2="18"></line></svg>`,
        trash: `<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><polyline points="3 6 5 6 21 6"></polyline><path d="M19 6v14a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6m3 0V4a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2"></path></svg>`
    };

    let toastContainer = null;
    const MAX_VISIBLE_TOASTS = 4;

    function getToastContainer() {
        if (!toastContainer || !document.body.contains(toastContainer)) {
            toastContainer = document.createElement('div');
            toastContainer.className = 'ha-toast-container';
            toastContainer.setAttribute('aria-label', 'Notifications');
            document.body.appendChild(toastContainer);
        }
        return toastContainer;
    }

    /**
     * Display an application toast notification.
     */
    function toast(options) {
        if (typeof options === 'string') {
            options = { message: options, type: 'info' };
        }
        const {
            type = 'info',
            message = '',
            title = '',
            duration = 4000
        } = options;

        const container = getToastContainer();

        // Prune oldest toasts if exceeding limit
        const activeToasts = container.querySelectorAll('.ha-toast:not(.ha-toast-hiding)');
        if (activeToasts.length >= MAX_VISIBLE_TOASTS) {
            for (let i = 0; i <= activeToasts.length - MAX_VISIBLE_TOASTS; i++) {
                dismissToast(activeToasts[i]);
            }
        }

        const toastEl = document.createElement('div');
        toastEl.className = `ha-toast ha-toast-${type}`;
        toastEl.setAttribute('role', type === 'error' || type === 'warning' ? 'alert' : 'status');
        toastEl.setAttribute('aria-live', type === 'error' ? 'assertive' : 'polite');

        const iconHtml = ICONS[type] || ICONS.info;

        toastEl.innerHTML = `
            <div class="ha-toast-icon-wrap" aria-hidden="true">${iconHtml}</div>
            <div class="ha-toast-content">
                ${title ? `<div class="ha-toast-title">${escapeHtml(title)}</div>` : ''}
                <div class="ha-toast-message">${escapeHtml(message)}</div>
            </div>
            <button type="button" class="ha-toast-close" aria-label="Dismiss notification">
                ${ICONS.close}
            </button>
        `;

        const closeBtn = toastEl.querySelector('.ha-toast-close');
        closeBtn.addEventListener('click', (e) => {
            e.stopPropagation();
            dismissToast(toastEl);
        });

        // Auto-dismiss timer with pause on hover
        let dismissTimer = null;
        let remaining = duration;
        let startTime = Date.now();

        function startTimer() {
            if (duration > 0) {
                startTime = Date.now();
                dismissTimer = setTimeout(() => {
                    dismissToast(toastEl);
                }, remaining);
            }
        }

        function pauseTimer() {
            if (dismissTimer) {
                clearTimeout(dismissTimer);
                dismissTimer = null;
                remaining -= Date.now() - startTime;
            }
        }

        toastEl.addEventListener('mouseenter', pauseTimer);
        toastEl.addEventListener('mouseleave', () => {
            if (remaining > 0) startTimer();
        });

        container.appendChild(toastEl);
        startTimer();

        return {
            dismiss: () => dismissToast(toastEl),
            element: toastEl
        };
    }

    function dismissToast(el) {
        if (!el || el.classList.contains('ha-toast-hiding')) return;
        el.classList.add('ha-toast-hiding');
        el.addEventListener('animationend', () => {
            el.remove();
        }, { once: true });
        // Fallback removal if animationend doesn't fire
        setTimeout(() => {
            if (el.parentNode) el.remove();
        }, 300);
    }

    /**
     * Display a modal confirmation dialog.
     * Returns a Promise resolving to true if confirmed, false if cancelled.
     */
    function confirm(options) {
        return new Promise((resolve) => {
            if (typeof options === 'string') {
                options = { message: options };
            }
            const {
                title = 'Confirm Action',
                message = 'Are you sure you want to proceed?',
                secondaryText = '',
                confirmText = 'Confirm',
                cancelText = 'Cancel',
                isDestructive = false
            } = options;

            const previousFocus = document.activeElement;

            const backdrop = document.createElement('div');
            backdrop.className = 'ha-modal-backdrop';
            backdrop.setAttribute('role', 'presentation');

            const iconWrapClass = isDestructive ? 'ha-modal-icon-destructive' : 'ha-modal-icon-default';
            const iconSvg = isDestructive ? ICONS.trash : ICONS.warning;
            const confirmBtnClass = isDestructive ? 'ha-modal-btn-danger' : 'ha-modal-btn-primary';

            backdrop.innerHTML = `
                <div class="ha-modal-card" role="dialog" aria-modal="true" aria-labelledby="haModalTitle" aria-describedby="haModalDesc">
                    <div class="ha-modal-header">
                        <div class="ha-modal-title-group">
                            <div class="ha-modal-icon ${iconWrapClass}" aria-hidden="true">
                                ${iconSvg}
                            </div>
                            <h2 class="ha-modal-title" id="haModalTitle">${escapeHtml(title)}</h2>
                        </div>
                        <button type="button" class="ha-modal-close-btn" aria-label="Close dialog" data-ha-action="cancel">
                            ${ICONS.close}
                        </button>
                    </div>
                    <div class="ha-modal-body">
                        <p class="ha-modal-message" id="haModalDesc">${escapeHtml(message)}</p>
                        ${secondaryText ? `<p class="ha-modal-secondary-text">${escapeHtml(secondaryText)}</p>` : ''}
                    </div>
                    <div class="ha-modal-footer">
                        <button type="button" class="ha-modal-btn ha-modal-btn-cancel" data-ha-action="cancel">
                            ${escapeHtml(cancelText)}
                        </button>
                        <button type="button" class="ha-modal-btn ${confirmBtnClass}" data-ha-action="confirm">
                            ${escapeHtml(confirmText)}
                        </button>
                    </div>
                </div>
            `;

            document.body.appendChild(backdrop);
            // Trigger transition
            requestAnimationFrame(() => {
                backdrop.classList.add('ha-modal-open');
            });

            const card = backdrop.querySelector('.ha-modal-card');
            const confirmBtn = backdrop.querySelector('[data-ha-action="confirm"]');
            const cancelBtn = backdrop.querySelector('[data-ha-action="cancel"].ha-modal-btn-cancel');

            // Set initial focus: if destructive, focus cancel for safety; otherwise confirm
            if (isDestructive && cancelBtn) {
                cancelBtn.focus();
            } else if (confirmBtn) {
                confirmBtn.focus();
            }

            let resolved = false;

            function finish(result) {
                if (resolved) return;
                resolved = true;

                if (result && confirmBtn) {
                    confirmBtn.disabled = true;
                }

                backdrop.classList.remove('ha-modal-open');
                backdrop.addEventListener('transitionend', () => {
                    backdrop.remove();
                    if (previousFocus && typeof previousFocus.focus === 'function') {
                        previousFocus.focus();
                    }
                }, { once: true });

                setTimeout(() => {
                    if (backdrop.parentNode) {
                        backdrop.remove();
                        if (previousFocus && typeof previousFocus.focus === 'function') {
                            previousFocus.focus();
                        }
                    }
                }, 250);

                resolve(result);
            }

            // Click actions
            backdrop.querySelectorAll('[data-ha-action="cancel"]').forEach(btn => {
                btn.addEventListener('click', (e) => {
                    e.stopPropagation();
                    finish(false);
                });
            });

            confirmBtn.addEventListener('click', (e) => {
                e.stopPropagation();
                finish(true);
            });

            // Backdrop click dismisses
            backdrop.addEventListener('click', (e) => {
                if (!card.contains(e.target)) {
                    finish(false);
                }
            });

            // Keyboard navigation: Escape and Focus Trap
            function onKeyDown(e) {
                if (e.key === 'Escape') {
                    e.preventDefault();
                    document.removeEventListener('keydown', onKeyDown);
                    finish(false);
                    return;
                }

                if (e.key === 'Tab') {
                    const focusables = card.querySelectorAll('button:not([disabled]), [tabindex]:not([tabindex="-1"])');
                    if (focusables.length === 0) return;
                    const first = focusables[0];
                    const last = focusables[focusables.length - 1];

                    if (e.shiftKey) {
                        if (document.activeElement === first) {
                            e.preventDefault();
                            last.focus();
                        }
                    } else {
                        if (document.activeElement === last) {
                            e.preventDefault();
                            first.focus();
                        }
                    }
                }
            }

            document.addEventListener('keydown', onKeyDown);
        });
    }

    /**
     * Display a modal prompt dialog with an input field.
     * Returns a Promise resolving to the entered string, or null if cancelled.
     */
    function prompt(options) {
        return new Promise((resolve) => {
            if (typeof options === 'string') {
                options = { message: options };
            }
            const {
                title = 'Input Required',
                message = '',
                label = '',
                placeholder = '',
                defaultValue = '',
                confirmText = 'OK',
                cancelText = 'Cancel',
                validate = null
            } = options;

            const previousFocus = document.activeElement;

            const backdrop = document.createElement('div');
            backdrop.className = 'ha-modal-backdrop';
            backdrop.setAttribute('role', 'presentation');

            backdrop.innerHTML = `
                <div class="ha-modal-card" role="dialog" aria-modal="true" aria-labelledby="haPromptTitle">
                    <div class="ha-modal-header">
                        <div class="ha-modal-title-group">
                            <div class="ha-modal-icon ha-modal-icon-default" aria-hidden="true">
                                ${ICONS.info}
                            </div>
                            <h2 class="ha-modal-title" id="haPromptTitle">${escapeHtml(title)}</h2>
                        </div>
                        <button type="button" class="ha-modal-close-btn" aria-label="Close dialog" data-ha-action="cancel">
                            ${ICONS.close}
                        </button>
                    </div>
                    <form id="haPromptForm" onsubmit="return false;">
                        <div class="ha-modal-body">
                            ${message ? `<p class="ha-modal-message">${escapeHtml(message)}</p>` : ''}
                            <div class="ha-modal-input-group">
                                ${label ? `<label for="haPromptInput">${escapeHtml(label)}</label>` : ''}
                                <input type="text" id="haPromptInput" class="ha-modal-input" placeholder="${escapeHtml(placeholder)}" value="${escapeHtml(defaultValue)}" autocomplete="off" />
                                <div class="ha-modal-input-error" id="haPromptError" role="alert"></div>
                            </div>
                        </div>
                        <div class="ha-modal-footer">
                            <button type="button" class="ha-modal-btn ha-modal-btn-cancel" data-ha-action="cancel">
                                ${escapeHtml(cancelText)}
                            </button>
                            <button type="submit" class="ha-modal-btn ha-modal-btn-primary" data-ha-action="confirm">
                                ${escapeHtml(confirmText)}
                            </button>
                        </div>
                    </form>
                </div>
            `;

            document.body.appendChild(backdrop);
            requestAnimationFrame(() => {
                backdrop.classList.add('ha-modal-open');
            });

            const card = backdrop.querySelector('.ha-modal-card');
            const input = backdrop.querySelector('#haPromptInput');
            const errorEl = backdrop.querySelector('#haPromptError');
            const form = backdrop.querySelector('#haPromptForm');
            const confirmBtn = backdrop.querySelector('[data-ha-action="confirm"]');

            if (input) {
                setTimeout(() => {
                    input.focus();
                    input.select();
                }, 50);
            }

            let resolved = false;

            function finish(result) {
                if (resolved) return;
                resolved = true;

                backdrop.classList.remove('ha-modal-open');
                backdrop.addEventListener('transitionend', () => {
                    backdrop.remove();
                    if (previousFocus && typeof previousFocus.focus === 'function') {
                        previousFocus.focus();
                    }
                }, { once: true });

                setTimeout(() => {
                    if (backdrop.parentNode) {
                        backdrop.remove();
                        if (previousFocus && typeof previousFocus.focus === 'function') {
                            previousFocus.focus();
                        }
                    }
                }, 250);

                resolve(result);
            }

            function handleConfirm() {
                const val = input.value;
                if (typeof validate === 'function') {
                    const err = validate(val);
                    if (err) {
                        if (errorEl) errorEl.textContent = err;
                        input.setAttribute('aria-invalid', 'true');
                        input.focus();
                        return;
                    }
                }
                finish(val);
            }

            form.addEventListener('submit', (e) => {
                e.preventDefault();
                handleConfirm();
            });

            backdrop.querySelectorAll('[data-ha-action="cancel"]').forEach(btn => {
                btn.addEventListener('click', (e) => {
                    e.stopPropagation();
                    finish(null);
                });
            });

            backdrop.addEventListener('click', (e) => {
                if (!card.contains(e.target)) {
                    finish(null);
                }
            });

            function onKeyDown(e) {
                if (e.key === 'Escape') {
                    e.preventDefault();
                    document.removeEventListener('keydown', onKeyDown);
                    finish(null);
                }
            }
            document.addEventListener('keydown', onKeyDown);
        });
    }

    /**
     * Display a simple alert modal (when blocking acknowledgement is needed).
     */
    function alert(options) {
        return new Promise((resolve) => {
            if (typeof options === 'string') {
                options = { message: options };
            }
            const {
                title = 'Notification',
                message = '',
                confirmText = 'OK'
            } = options;

            confirm({
                title,
                message,
                confirmText,
                cancelText: '',
                isDestructive: false
            }).then(() => resolve());
        });
    }

    function escapeHtml(str) {
        if (!str) return '';
        return String(str)
            .replace(/&/g, '&amp;')
            .replace(/</g, '&lt;')
            .replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;')
            .replace(/'/g, '&#39;');
    }

    // Public API
    const TerminalDialogs = {
        toast,
        confirm,
        prompt,
        alert,
        success: (message, title) => toast({ type: 'success', message, title }),
        error: (message, title) => toast({ type: 'error', message, title, duration: 6000 }),
        warning: (message, title) => toast({ type: 'warning', message, title, duration: 5000 }),
        info: (message, title) => toast({ type: 'info', message, title })
    };

    window.TerminalDialogs = TerminalDialogs;
    window.HackAcadDialogs = TerminalDialogs;

    // Auto-bind confirmation to forms or buttons with data-confirm
    document.addEventListener('DOMContentLoaded', () => {
        document.querySelectorAll('form[data-confirm]').forEach(form => {
            form.addEventListener('submit', async function (e) {
                if (form.dataset.confirmed === 'true') return;
                e.preventDefault();

                const message = form.getAttribute('data-confirm') || 'Are you sure you want to proceed?';
                const title = form.getAttribute('data-confirm-title') || 'Confirm Action';
                const confirmText = form.getAttribute('data-confirm-button') || 'Confirm';
                const isDestructive = form.getAttribute('data-confirm-destructive') !== 'false';

                const confirmed = await TerminalDialogs.confirm({
                    title,
                    message,
                    confirmText,
                    isDestructive
                });

                if (confirmed) {
                    form.dataset.confirmed = 'true';
                    form.submit();
                }
            });
        });
    });
})();
