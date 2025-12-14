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
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ code, password })
    });

    if (!res.ok) throw new Error('Login failed');
  const data = await res.json();

    if (!data || data.success !== true) {
      // Server reports failure (e.g. invalid code/password)
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

    // Client-side validation: email format and postal code
    const emailRegex = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;
    if (!emailRegex.test(email)) {
      showToast('error', 'Please enter a valid email address.');
      return;
    }
    // Postal code must be exactly 5 digits if provided
    if (postalCode && !/^\d{5}$/.test(postalCode)) {
      showToast('error', 'Postal code must be exactly 5 digits.');
      return;
    }

    const res = await fetch('/api/register', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ firstName, lastName, email, phone, username, password: regPassword, city, streetAddress, postalCode, roleId: desiredRoleId })
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