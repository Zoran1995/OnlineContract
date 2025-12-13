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
    if (!loginC || !regC) return;
    if (mode === 'signin') {
      regC.classList.remove('hidden');
      loginC.classList.add('hidden');
    } else if (mode === 'login') {
      loginC.classList.remove('hidden');
      regC.classList.add('hidden');
    }
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
      body: JSON.stringify({ firstName, lastName, email, phone, username, password: regPassword, city, streetAddress, postalCode })
    });

    if (!res.ok) throw new Error('Register failed');
    const data = await res.json();

    if (!data || data.success !== true) {
      showToast('error', data?.message || 'Registration failed');
      return;
    }


sessionStorage.setItem('pendingToast', JSON.stringify({
  type: 'info',
  message: 'User successfully created account'
}));

    location.href = '/home';
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