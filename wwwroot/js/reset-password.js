(async () => {
  try {
    const params = new URLSearchParams(window.location.search);
    const token = params.get('token') || '';
    const msgEl = document.getElementById('resetMsg');
    const form = document.getElementById('resetForm');
    const codeEl = document.getElementById('rpCode');
    const newEl = document.getElementById('rpNew');
    const confirmEl = document.getElementById('rpConfirm');
    const submit = document.getElementById('rpSubmit');

    if (!token) {
      if (msgEl) msgEl.textContent = 'This reset link is invalid or has expired. Please request a new password reset.';
      return;
    }

    // validate token
    const res = await fetch('/api/auth/reset-token/' + encodeURIComponent(token));
    const data = await res.json();
    if (!data || data.success !== true) {
      if (msgEl) msgEl.textContent = data?.message || 'This reset link is invalid or has expired. Please request a new password reset.';
      return;
    }

    if (codeEl) codeEl.value = data.code || '';
    if (msgEl) msgEl.classList.add('hidden');
    if (form) form.classList.remove('hidden');

    submit?.addEventListener('click', async (e) => {
      e.preventDefault();
      const newPw = (newEl?.value || '').trim();
      const conf = (confirmEl?.value || '').trim();
      if (!newPw) { showToast('error', 'Password is required. Please enter a new password.'); return; }
      if (newPw !== conf) { showToast('error', 'Your password and confirmation do not match.'); return; }
      if (newPw.length < 8) { showToast('error', 'Your password must be at least 8 characters, include one uppercase letter and one number.'); return; }
      if (!/[A-Z]/.test(newPw)) { showToast('error', 'Your password must include at least one uppercase letter.'); return; }
      if (!/\d/.test(newPw)) { showToast('error', 'Your password must include at least one number.'); return; }

      try {
        submit.disabled = true;
        const r = await fetch('/api/auth/reset-password', {
          method: 'POST',
          credentials: 'include',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ token, newPassword: newPw, confirmPassword: conf })
        });
        const d = await r.json().catch(() => ({ success: r.ok }));
        if (!d || d.success !== true) {
          showToast('error', d?.message || 'Failed to reset password. Please try again.');
          submit.disabled = false;
          return;
        }
        showToast('info', 'Your password has been changed successfully. You are now signed in.');
        // set local session and redirect
        localStorage.setItem('isLoggedIn', 'true');
        localStorage.setItem('userId', d.userId || 2);
        if (typeof d.roleId !== 'undefined') localStorage.setItem('roleId', String(d.roleId));
        try { if (typeof updateNavbarAuth === 'function') updateNavbarAuth(); } catch {}
        setTimeout(() => { window.location.href = '/home'; }, 800);
      } catch (err) {
        showToast('error', 'Failed to reset password. Please try again later.');
      } finally {
        submit.disabled = false;
      }
    });
  } catch (err) {
    try { showToast('error', 'An unexpected error occurred. Please try again later.'); } catch {}
  }
})();
