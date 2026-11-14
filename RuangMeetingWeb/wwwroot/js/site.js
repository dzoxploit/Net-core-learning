// Shared page behaviour (kept in a file so the CSP can forbid inline scripts).
(function () {
    // mobile menu
    document.getElementById('menuToggle')?.addEventListener('click', () => document.body.classList.toggle('menu-open'));

    // grid row -> load the record into the form
    document.querySelectorAll('tr[data-href]').forEach(tr =>
        tr.addEventListener('click', () => { location.href = tr.dataset.href; }));

    // confirm destructive buttons + block double submit (the server is idempotent anyway)
    document.querySelectorAll('form[data-guard]').forEach(form => {
        form.addEventListener('submit', e => {
            const btn = e.submitter;
            if (btn?.dataset.confirm && !confirm(btn.dataset.confirm)) { e.preventDefault(); return; }
            if (form.dataset.sending) { e.preventDefault(); return; }
            form.dataset.sending = '1';
            setTimeout(() => form.querySelectorAll('button[type=submit]').forEach(b => { b.disabled = true; }));
        });
    });
})();
