// ADMIN - Native-form fallback: continue the server-preserved form, including its fresh CSRF token.
(() => {
    document.getElementById('admin-resume-form')?.requestSubmit();
})();
