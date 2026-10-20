(() => {
    document.querySelectorAll('[data-audit-toggle]').forEach(button => {
        button.addEventListener('click', () => {
            const panel = document.getElementById(button.getAttribute('aria-controls'));
            panel.hidden = !panel.hidden;
            button.setAttribute('aria-expanded', String(!panel.hidden));
        });
    });
    const dialog = document.getElementById('admin-verify-dialog');
    if (!dialog || typeof dialog.showModal !== 'function') return;
    const verification = document.getElementById('admin-verify-form');
    const errors = dialog.querySelector('[data-verify-errors]');
    const notice = dialog.querySelector('[data-verify-notice]');
    const password = verification.elements.CurrentPassword;
    const code = verification.elements.Code;
    const sms = dialog.querySelector('[data-send-verify-sms]');
    let pending = null;
    let busy = false;
    function showError(form, text, informational = false) {
        let box = form.querySelector('[data-admin-feedback]');
        if (!box) { box = document.createElement('p'); box.dataset.adminFeedback = ''; box.className = 'field-error mb-3'; box.setAttribute('role', 'alert'); box.tabIndex = -1; form.prepend(box); }
        box.className = informational ? 'text-secondary mb-3' : 'field-error mb-3'; box.setAttribute('role', informational ? 'status' : 'alert');
        box.textContent = text; box.focus();
    }
    function controls(form, disabled) { form.querySelectorAll('button[type="submit"],button:not([type])').forEach(button => { button.disabled = disabled; }); }
    async function request(url, data) {
        const response = await fetch(url, { method: 'POST', body: data, headers: { 'X-Admin-Request': 'fetch' }, credentials: 'same-origin' });
        const json = response.headers.get('content-type')?.includes('application/json') ? await response.json() : null;
        if (response.status === 428 && json?.verificationRequired) return { challenge: json };
        if (!response.ok) {
            if (response.status === 409) throw new Error('This record or operation changed. Reload the page and review the latest information before submitting.');
            if (response.status === 429) throw new Error('Too many attempts. Wait 10 minutes before trying again.');
            throw new Error(json?.errors?.join(' ') || 'The operation could not be completed. Your form is unchanged; check your access or reload the page before trying again.');
        }
        return { response, json };
    }
    async function perform(operation) {
        const result = await request(operation.form.action, operation.data);
        if (result.challenge) {
            pending = operation;
            verification.reset(); errors.textContent = ''; notice.textContent = '';
            verification.elements.Ticket.value = result.challenge.ticket;
            password.required = result.challenge.hasPassword;
            dialog.querySelector('[data-password-field]').hidden = !result.challenge.hasPassword;
            sms.hidden = !result.challenge.smsEnabled;
            dialog.querySelector('[data-code-label]').textContent = result.challenge.smsEnabled ? 'SMS or recovery code' : 'Authenticator or recovery code';
            dialog.showModal(); (result.challenge.hasPassword ? password : code).focus();
            return;
        }
        if (result.json?.redirectUrl) { window.location.assign(result.json.redirectUrl); return; }
        if (result.response.redirected) { window.location.assign(result.response.url); return; }
        showError(operation.form, result.json?.notice || 'The operation returned without a change. Review the form before trying again.', !!result.json?.notice);
    }
    document.addEventListener('submit', async event => {
        const form = event.target;
        if (!(form instanceof HTMLFormElement) || !form.matches('[data-admin-operation]') || event.defaultPrevented) return;
        event.preventDefault();
        if (busy || pending || !form.reportValidity()) return;
        busy = true; controls(form, true);
        const data = new FormData(form);
        if (event.submitter?.name) data.append(event.submitter.name, event.submitter.value);
        // Keep the same key when retrying an uncertain network response; a changed draft gets a new key.
        const payload = [...data].map(([key, value]) => [key, String(value)]);
        const signature = JSON.stringify(payload);
        if (form.dataset.operationSignature !== signature) { form.dataset.operationSignature = signature; form.dataset.operationId = crypto.randomUUID().replaceAll('-', ''); }
        data.set('AdminOperationId', form.dataset.operationId);
        try { await perform({ form, data }); }
        catch (error) { showError(form, error.message || 'Connection failed. Your form is unchanged; try again.'); }
        finally { busy = false; if (!pending) controls(form, false); }
    });
    verification.addEventListener('submit', async event => {
        event.preventDefault();
        if (busy || !pending || !verification.reportValidity()) return;
        busy = true; controls(verification, true); errors.textContent = '';
        const operation = pending;
        try {
            const result = await request(verification.action, new FormData(verification));
            if (!result.json?.verified) throw new Error('Verification could not be completed. Sign in again if your session expired.');
            pending = null; dialog.close(); password.value = ''; code.value = '';
            await perform(operation);
        } catch (error) {
            if (dialog.open) { errors.textContent = error.message; errors.focus(); }
            else showError(operation.form, error.message);
        } finally { busy = false; controls(verification, false); if (!pending) controls(operation.form, false); password.value = ''; code.value = ''; }
    });
    sms.addEventListener('click', async () => {
        if (busy) return;
        busy = true; sms.disabled = true; errors.textContent = '';
        try { const result = await request(sms.dataset.url, new FormData(verification)); notice.textContent = result.json?.notice || 'SMS code sent.'; }
        catch (error) { errors.textContent = error.message; }
        finally { busy = false; sms.disabled = false; }
    });
    dialog.querySelector('[data-cancel-verify]').addEventListener('click', () => { if (!busy) dialog.close(); });
    dialog.addEventListener('cancel', event => { if (busy) event.preventDefault(); });
    dialog.addEventListener('close', () => {
        if (dialog.open) return;
        if (pending) { controls(pending.form, false); pending.form.querySelector('button[type="submit"],button:not([type])')?.focus(); }
        pending = null; password.value = ''; code.value = '';
    });
})();
