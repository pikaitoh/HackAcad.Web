// The server supplies the remaining cooldown so reloads do not restart the minute.
const phoneVerification = document.querySelector('[data-phone-verification]');
if (phoneVerification) {
    const input = phoneVerification.querySelector('[data-sms-code], input[name="Confirmation.Code"]');
    const action = phoneVerification.querySelector('#phone-code-action');
    const status = phoneVerification.querySelector('[data-resend-status]');
    const deadline = performance.now() + Number(phoneVerification.dataset.resendSeconds) * 1000;
    const update = () => {
        const seconds = Math.max(0, Math.ceil((deadline - performance.now()) / 1000));
        const resend = seconds === 0 && input.value.trim() === '';
        action.textContent = resend ? 'Resend verification code' : (action.dataset.verifyLabel || 'Verify phone number');
        action.setAttribute('form', resend ? 'phone-resend-form' : 'phone-code-form');
        status.textContent = seconds > 0 ? `You can resend the code in ${seconds} seconds.`
            : 'No code yet? Leave the code field empty to resend, or enter your code to verify.';
    };
    input.addEventListener('input', update);
    document.addEventListener('visibilitychange', update);
    setInterval(update, 1000);
    update();
}
