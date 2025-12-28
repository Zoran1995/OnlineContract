document.getElementById('navLogIn')?.addEventListener('click', (e) => {
  e.preventDefault();
  document.getElementById('loginFormContainer')?.classList.remove('hidden');
  document.getElementById('registerFormContainer')?.classList.add('hidden');
});

document.getElementById('navSignIn')?.addEventListener('click', (e) => {
  e.preventDefault();
  document.getElementById('registerFormContainer')?.classList.remove('hidden');
  document.getElementById('loginFormContainer')?.classList.add('hidden');
});

// On load, show the requested form based on URL query (?mode=login|signin)
(() => {
  try {
    const params = new URLSearchParams(window.location.search);
    const mode = (params.get('mode') || '').toLowerCase();
    const loginC = document.getElementById('loginFormContainer');
    const regC = document.getElementById('registerFormContainer');
    const roleRow = document.getElementById('roleSelectorRow');
    const roleSel = document.getElementById('roleSelector');
    if (!loginC || !regC) return;
    if (mode === 'signin') {
      regC.classList.remove('hidden');
      loginC.classList.add('hidden');
    } else if (mode === 'login') {
      loginC.classList.remove('hidden');
      regC.classList.add('hidden');
    }

    // Show role selector only for Admin (8) or Manager (7)
    try {
      const currentRole = parseInt(localStorage.getItem('roleId') || '0', 10);
      const isPrivileged = currentRole === 8 || currentRole === 7;
      if (roleRow) roleRow.classList.toggle('hidden', !isPrivileged);
      // Managers cannot assign Administrator: remove the Administrator option entirely
      if (roleSel) {
        const adminOpt = Array.from(roleSel.options).find(o => o.value === '8');
        if (currentRole === 7 && adminOpt) {
          roleSel.removeChild(adminOpt);
        }
      }
    } catch {}
  } catch {}
})();

// Ensure pressing Enter inside the registration form triggers the Create Account action
(() => {
  const regForm = document.getElementById('registerForm');
  const regBtn = document.getElementById('registerBtn');
  if (!regForm || !regBtn) return;
  regForm.addEventListener('keydown', (e) => {
    if (e.key === 'Enter') {
      // Prevent accidental double submits and let the submit handler run
      e.preventDefault();
      try { regBtn.click(); } catch { if (typeof regForm.requestSubmit === 'function') regForm.requestSubmit(); }
    }
  });
})();

// Change temporary password modal handling
(() => {
  const modal = document.getElementById('changeTempModal');
  const btnCancel = document.getElementById('tmpCancel');
  const btnSave = document.getElementById('tmpSave');
  const inpCode = document.getElementById('tmpCode');
  const inpNew = document.getElementById('tmpNewPassword');
  const inpConfirm = document.getElementById('tmpConfirmPassword');
  const inpStamp = document.getElementById('tmpStamp');

  if (btnCancel && !btnCancel._bound) {
    btnCancel._bound = true;
    btnCancel.addEventListener('click', (e) => {
      e.preventDefault();
      try { modal?.close(); } catch { modal?.classList.add('hidden'); }
    });
  }

  if (btnSave && !btnSave._bound) {
    btnSave._bound = true;
    btnSave.addEventListener('click', async (e) => {
      e.preventDefault();
      const code = (inpCode?.value || '').trim();
      const pw = (inpNew?.value || '').trim();
      const pw2 = (inpConfirm?.value || '').trim();
      const stamp = parseInt(inpStamp?.value || '0', 10) || 0;

      if (!code) { showToast('error', 'Username is required. Please enter your username.'); return; }
      if (!pw) { showToast('error', 'New password is required. Please provide a new password.'); return; }
      if (pw !== pw2) { showToast('error', 'Passwords do not match. Please ensure both entries are identical.'); return; }
      if (pw.length < 8) { showToast('error', 'Password must be at least 8 characters long.'); return; }
      if (!/[A-Z]/.test(pw)) { showToast('error', 'Password must contain at least one uppercase letter.'); return; }
      if (!/\d/.test(pw)) { showToast('error', 'Password must contain at least one number.'); return; }

      try {
        btnSave.disabled = true;
        const res = await fetch('/api/users/change-temp-password', {
          method: 'POST',
          credentials: 'include',
          headers: { 'Content-Type': 'application/json', 'Accept': 'application/json' },
          body: JSON.stringify({ code, newPassword: pw, confirmPassword: pw2, stamp })
        });
        const data = await res.json().catch(() => ({ success: res.ok }));
        if (!data || data.success !== true) {
          showToast('error', data?.message || 'Failed to change password. Please try again.');
          return;
        }

        // Successful: update local session and redirect home
        localStorage.setItem('isLoggedIn', 'true');
        localStorage.setItem('userId', data.userId || 2);
        if (typeof data.roleId !== 'undefined') localStorage.setItem('roleId', String(data.roleId));
        try { if (typeof updateNavbarAuth === 'function') updateNavbarAuth(); } catch {}
        sessionStorage.setItem('pendingToast', JSON.stringify({ type: 'info', message: 'Password changed and you are now logged in.' }));
        try { modal?.close(); } catch { modal?.classList.add('hidden'); }
        location.href = '/home';
      } catch (err) {
        showToast('error', 'Failed to change password. Please try again later.');
      } finally {
        btnSave.disabled = false;
      }
    });
  }
})();

document.getElementById('loginForm')?.addEventListener('submit', async (e) => {
  e.preventDefault();
  const code = document.getElementById('code').value.trim();
  const password = document.getElementById('password').value.trim();

  const loading = document.getElementById('loginLoading');
  const btn = document.getElementById('loginBtn');

  try {
    btn.disabled = true;
    loading.classList.remove('hidden');

    const res = await fetch('/api/login', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json', 'Accept':'application/json' },
      credentials: 'include',
      body: JSON.stringify({ code, password })
    });

    if (!res.ok) throw new Error('Login failed');
  const data = await res.json();

    if (!data || data.success !== true) {
      // Server reports failure (e.g. invalid code/password)
      if (data && data.mustChangePassword) {
        // Show modal to force password change
        try {
          const modal = document.getElementById('changeTempModal');
          const tmpCode = document.getElementById('tmpCode');
          const tmpStamp = document.getElementById('tmpStamp');
          if (tmpCode) tmpCode.value = code;
          if (tmpStamp) tmpStamp.value = String(data.stamp ?? '0');
          try { modal?.showModal(); } catch { modal?.classList.remove('hidden'); }
        } catch {}
        showToast('info', data?.message || 'Please change your password to continue.');
        return;
      }
      showToast('error', data?.message || 'Login failed');
      return;
    }


// success
localStorage.setItem('isLoggedIn', 'true');
localStorage.setItem('userId', data.userId || 2);
// Persist ax_user.code from the login form so navbar can immediately render it
localStorage.setItem('code', code);
if (typeof data.roleId !== 'undefined') {
  localStorage.setItem('roleId', String(data.roleId));
}

// Proactively update navbar label immediately if available
try { if (typeof updateNavbarAuth === 'function') updateNavbarAuth(); } catch {}

sessionStorage.setItem('pendingToast', JSON.stringify({
  type: 'info',
  message: "You've successfully logged in. Enjoy your session!"
}));

// Redirect home
location.href = '/home';
  } catch (err) {
    showToast('error', 'Login failed');
  } finally {
    btn.disabled = false;
    loading.classList.add('hidden');
  }
});

document.getElementById('loginClearBtn')?.addEventListener('click', () => {
  document.getElementById('code').value = '';
  document.getElementById('password').value = '';
});

document.getElementById('registerForm')?.addEventListener('submit', async (e) => {
  e.preventDefault();

  const firstName = document.getElementById('firstName').value.trim();
  const lastName = document.getElementById('lastName').value.trim();
  const email = document.getElementById('email').value.trim();
  const phone = document.getElementById('phone').value.trim();
  const username = document.getElementById('username').value.trim();
  const regPassword = document.getElementById('regPassword').value.trim();
  const city = document.getElementById('city')?.value.trim();
  const streetAddress = document.getElementById('streetAddress')?.value.trim();
  const postalCode = document.getElementById('postalCode')?.value.trim();
  const roleSel = document.getElementById('roleSelector');
  const desiredRoleId = roleSel && !roleSel.closest('.hidden') ? parseInt(roleSel.value || '5', 10) : null;

  const loading = document.getElementById('registerLoading');
  const btn = document.getElementById('registerBtn');

  try {
    btn.disabled = true;
    loading.classList.remove('hidden');

    // Client-side validation: required fields
    if (!firstName) { showToast('error', 'First name is required. Please enter your first name.'); return; }
    if (!lastName) { showToast('error', 'Last name is required. Please enter your last name.'); return; }
    if (!email) { showToast('error', 'Email is required. Please provide an email address.'); return; }
    // Email format
    const emailRegex = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;
    if (!emailRegex.test(email)) { showToast('error', 'Please enter a valid email address.'); return; }
    if (!username) { showToast('error', 'Username (code) is required. Please choose a username.'); return; }
    if (!phone) { showToast('error', 'Phone number is required. Please provide a phone number.'); return; }
    if (!regPassword) { showToast('error', 'Password is required. Please provide a password that meets the strength requirements.'); return; }

    // Normalize phone: client expects country prefix +381 in a readonly input and user provides the rest
    const phoneRaw = phone.replace(/[^0-9+()\-./\s]/g, '');
    const fullPhone = '+381' + phoneRaw.replace(/^0+/, '');

    // Password strength: min 8, upper, lower, digit
    const pwdStrong = /^(?=.*[a-z])(?=.*[A-Z])(?=.*\d).{8,}$/;
    if (!pwdStrong.test(regPassword)) {
      showToast('error', 'Password must be at least 8 characters long and include upper and lower case letters and at least one number.');
      return;
    }

    // Postal code must be exactly 5 digits if provided
    if (postalCode && !/^\d{5}$/.test(postalCode)) { showToast('error', 'Postal code must be exactly 5 digits.'); return; }

    const res = await fetch('/api/register', {
      method: 'POST',
      credentials: 'include',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ firstName, lastName, email, phone: fullPhone, username, password: regPassword, city, streetAddress, postalCode, roleId: desiredRoleId })
    });

    if (!res.ok) throw new Error('Register failed');
    const data = await res.json();

    if (!data || data.success !== true) {
      showToast('error', data?.message || 'Registration failed');
      return;
    }
    // Decide auto-login behavior based on role selection visibility and chosen role
    const roleRow = document.getElementById('roleSelectorRow');
    const roleSel2 = document.getElementById('roleSelector');
    const dropdownVisible = roleRow && !roleRow.classList.contains('hidden');
    const chosenRoleId = dropdownVisible && roleSel2 ? parseInt(roleSel2.value || '5', 10) : 5; // default Customer

    if (!dropdownVisible || chosenRoleId === 5) {
      // Auto-login only when registering as Customer or when no privileged user is logged in
      localStorage.setItem('isLoggedIn', 'true');
      localStorage.setItem('userId', (data.userId ?? '').toString());
      if (typeof data.roleId !== 'undefined' && data.roleId !== null) {
        localStorage.setItem('roleId', String(data.roleId));
      }
      // Store a usable code label; use provided username as a placeholder
      try { localStorage.setItem('code', username || ''); } catch {}
      try { if (typeof updateNavbarAuth === 'function') updateNavbarAuth(); } catch {}
      sessionStorage.setItem('pendingToast', JSON.stringify({
        type: 'info',
        message: 'Account created and you are now logged in.'
      }));
      location.href = '/home';
    } else {
      // Privileged creator: do not auto-login newly created user
      sessionStorage.setItem('pendingToast', JSON.stringify({
        type: 'info',
        message: 'User account successfully created.'
      }));
      // Refresh page to reset form and scroll to top
      try { window.scrollTo({ top: 0, behavior: 'auto' }); } catch {}
      location.reload();
      return;
    }
  } catch (err) {
    showToast('error', 'Registration failed');
  } finally {
    btn.disabled = false;
    loading.classList.add('hidden');
  }
});

// Tabs behavior for Register widget
(() => {
  const tabPersonal = document.getElementById('tabPersonal');
  const tabAddress = document.getElementById('tabAddress');
  const panelPersonal = document.getElementById('panelPersonal');
  const panelAddress = document.getElementById('panelAddress');

  if (tabPersonal && tabAddress && panelPersonal && panelAddress) {
    const activateTab = (tab) => {
      const isPersonal = tab === 'personal';
      // Toggle tab classes
      tabPersonal.classList.toggle('tab-active', isPersonal);
      tabPersonal.setAttribute('aria-selected', String(isPersonal));
      tabAddress.classList.toggle('tab-active', !isPersonal);
      tabAddress.setAttribute('aria-selected', String(!isPersonal));
      // Toggle panels
      panelPersonal.classList.toggle('hidden', !isPersonal);
      panelPersonal.classList.toggle('block', isPersonal);
      panelAddress.classList.toggle('hidden', isPersonal);
      panelAddress.classList.toggle('block', !isPersonal);
    };

    tabPersonal.addEventListener('click', (e) => {
      e.preventDefault();
      activateTab('personal');
    });
    tabAddress.addEventListener('click', (e) => {
      e.preventDefault();
      activateTab('address');
    });
  }
})();