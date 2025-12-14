// Notification storage helpers: keep notifications per-user when logged in, per-browser when guest
function _oc_getNotificationKey() {
  try {
    const isLoggedIn = localStorage.getItem('isLoggedIn') === 'true';
    if (isLoggedIn) {
      const uid = localStorage.getItem('userId');
      return uid ? `oc.notifications.user.${uid}` : 'oc.notifications.user.unknown';
    }
    // Guest (not logged in): per-browser storage
    return 'oc.notifications.guest';
  } catch {
    return 'oc.notifications.guest';
  }
}

function _oc_loadNotifications() {
  try {
    const key = _oc_getNotificationKey();
    const persisted = localStorage.getItem(key);
    window.notifications = window.notifications || (persisted ? JSON.parse(persisted) : []);
  } catch {
    window.notifications = window.notifications || [];
  }
}

function _oc_saveNotifications() {
  try {
    const key = _oc_getNotificationKey();
    localStorage.setItem(key, JSON.stringify(window.notifications || []));
  } catch { /* ignore */ }
}

// Initialize notifications from the appropriate scope
_oc_loadNotifications();

// Create notification UI (toast container, bell, panel) if it's not present in the page.
function ensureNotificationUI() {
  // Defensive cleanup: ensure only one bell and one notification badge exist
  try {
    const bells = document.querySelectorAll('#notificationBell');
    if (bells.length > 1) {
      // Keep the first, remove the rest
      bells.forEach((b, idx) => { if (idx > 0 && b.parentElement) b.parentElement.removeChild(b); });
    }
    const badges = document.querySelectorAll('#notificationCount');
    if (badges.length > 1) {
      // Keep the first, remove duplicates
      badges.forEach((bn, idx) => { if (idx > 0 && bn.parentElement) bn.parentElement.removeChild(bn); });
    }
  } catch {}

  if (!document.getElementById('toastContainer')) {
    const tc = document.createElement('div');
    tc.id = 'toastContainer';
    document.body.appendChild(tc);
  }

  if (!document.getElementById('notificationBell')) {
    const bell = document.createElement('div');
    bell.id = 'notificationBell';
    bell.className = 'bell';
    bell.setAttribute('aria-label', 'Notifications');
    bell.onclick = toggleNotificationPanel;

    // Bell icon
    const icon = document.createElement('span');
    icon.className = 'material-icons';
    icon.textContent = 'notifications_active';
    bell.appendChild(icon);

    // Badge showing notification count
    if (!document.getElementById('notificationCount')) {
      const badge = document.createElement('span');
      badge.id = 'notificationCount';
      badge.className = 'badge hidden'; // hidden when count is 0
      badge.textContent = '0';
        // Keep the badge inside the bell and small enough so it never peeks out of the viewport
        badge.style.position = 'absolute';
        badge.style.top = '-6px';
        badge.style.right = '-6px';
        badge.style.maxWidth = '24px';
        badge.style.maxHeight = '24px';
        // Ensure bell is a positioning context
        const bellStyle = bell.style || {};
        bellStyle.position = bellStyle.position || 'relative';
      bell.appendChild(badge);
    }

    // Prefer to place the bell inside the navbar's right-side controls so it won't overlap
    // other header buttons. If no navbar exists, fall back to fixed position on the viewport.
    const rightContainer = document.querySelector('.navbar .flex-none') || document.querySelector('.navbar .flex-none.items-center');
    const navbarRoot = document.querySelector('.navbar');
    const exportBtn = document.getElementById('exportBtn');
    if (rightContainer) {
      // Ensure user dropdown exists and is placed just to the left of the bell
      let userDd = document.getElementById('userDropdown');
      if (!userDd) {
        userDd = document.createElement('div');
        userDd.id = 'userDropdown';
        userDd.className = 'dropdown';
        const activator = document.createElement('button');
        activator.id = 'adminLabel';
        activator.type = 'button';
        activator.tabIndex = 0;
        activator.role = 'button';
        activator.className = 'btn btn-sm px-3 py-1 rounded select-none cursor-pointer bg-base-200 text-base-content border border-base-300 flex items-center gap-2';
        const menu = document.createElement('ul');
        menu.tabIndex = 0;
        menu.className = 'dropdown-content menu p-2 shadow bg-base-100 rounded-box w-40';
        menu.style.left = '0';
        menu.style.right = 'auto';
        menu.style.top = 'calc(100% + 4px)';
        menu.innerHTML = `
          <li><a href="#" id="ddLogout"><span class="material-icons mr-2">exit_to_app</span>Log Out</a></li>
        `;
        userDd.appendChild(activator);
        userDd.appendChild(menu);
        rightContainer.appendChild(userDd);
      }
      // Append the bell after the user dropdown so it sits on the far right
      rightContainer.appendChild(bell);
      // when inside navbar use relative positioning so it participates in the layout
      bell.classList.remove('fixed');
      bell.style.position = 'relative';
      bell.style.top = '';
      bell.style.right = '';
      bell.style.left = '';
      bell.style.marginLeft = '8px';
      bell.style.zIndex = '9999';
    } else if (navbarRoot) {
      // Append directly to navbar when specific right container is missing
      navbarRoot.appendChild(bell);
      bell.classList.remove('fixed');
      bell.style.position = 'relative';
      bell.style.top = '';
      bell.style.right = '';
      bell.style.left = '';
      bell.style.marginLeft = '8px';
      bell.style.zIndex = '9999';
    } else {
      // fallback to fixed positioning in the viewport
      bell.classList.add('fixed');
      bell.style.zIndex = '99999';
      document.body.appendChild(bell);
    }
  }

  // Ensure bell is present and properly placed even if HTML structure varies
  try {
    const bell = document.getElementById('notificationBell');
    if (bell) {
      const rightContainer = document.querySelector('.navbar .flex-none') || document.querySelector('.navbar .flex-none.items-center');
      const navbarRoot = document.querySelector('.navbar');
      if (rightContainer && !rightContainer.contains(bell)) {
        // Keep bell on far right: insert after user dropdown if present
        const userDd = document.getElementById('userDropdown');
        if (userDd && userDd.parentElement === rightContainer) {
          rightContainer.insertBefore(bell, userDd.nextSibling);
        } else {
          rightContainer.appendChild(bell);
        }
        bell.classList.remove('fixed');
        bell.style.position = 'relative';
        bell.style.top = '';
        bell.style.right = '';
        bell.style.left = '';
        bell.style.marginLeft = '8px';
        bell.style.zIndex = '9999';
      } else if (navbarRoot && !navbarRoot.contains(bell)) {
        navbarRoot.appendChild(bell);
        bell.classList.remove('fixed');
        bell.style.position = 'relative';
        bell.style.top = '';
        bell.style.right = '';
        bell.style.left = '';
        bell.style.marginLeft = '8px';
        bell.style.zIndex = '9999';
      }
      // Make sure bell itself is never hidden
      bell.classList.remove('hidden');
    }
  } catch {}

  if (!document.getElementById('notificationPanel')) {
    const panel = document.createElement('div');
    panel.id = 'notificationPanel';
    panel.className = 'notification-panel hidden';

    const list = document.createElement('div');
    list.id = 'notificationList';
    panel.appendChild(list);

    // Controls row: Dismiss all (thin) + type filters (info/warning/error)
    const controls = document.createElement('div');
    controls.style.display = 'flex';
    controls.style.gap = '8px';
    controls.style.marginTop = '8px';

    const dismissBtn = document.createElement('button');
    dismissBtn.className = 'btn btn-xs';
    dismissBtn.textContent = 'Dismiss all';
    dismissBtn.onclick = dismissAll;
    controls.appendChild(dismissBtn);

  const infoBtn = document.createElement('button');
  infoBtn.className = 'btn btn-xs notif-info';
  infoBtn.setAttribute('aria-label', 'Filter information notifications');
  infoBtn.textContent = 'Information';
    infoBtn.onclick = function() {
      window._notificationFilterType = 'info';
      updateNotificationBell();
    };
    controls.appendChild(infoBtn);

  const warnBtn = document.createElement('button');
  warnBtn.className = 'btn btn-xs notif-warning';
  warnBtn.setAttribute('aria-label', 'Filter warning notifications');
  warnBtn.textContent = 'Warning';
    warnBtn.onclick = function() {
      window._notificationFilterType = 'warning';
      updateNotificationBell();
    };
    controls.appendChild(warnBtn);

  const errorBtn = document.createElement('button');
  errorBtn.className = 'btn btn-xs notif-error';
  errorBtn.setAttribute('aria-label', 'Filter error notifications');
  errorBtn.textContent = 'Error';
    errorBtn.onclick = function() {
      window._notificationFilterType = 'error';
      updateNotificationBell();
    };
    controls.appendChild(errorBtn);

    panel.appendChild(controls);

    // panel will be absolutely positioned by JS when opened
    panel.style.position = 'absolute';
    panel.style.zIndex = '100000';

    document.body.appendChild(panel);
  }

  // adjust position to avoid overlapping export button
  adjustBellPosition();
  adjustToastContainer();
  window.addEventListener('resize', adjustBellPosition);
  window.addEventListener('resize', adjustToastContainer);
  // Recalculate toast position on scroll so it stays strictly below the navbar
  window.addEventListener('scroll', adjustToastContainer, { passive: true });
  // observe DOM changes in case export button appears later
  const obs = new MutationObserver(() => { adjustBellPosition(); adjustToastContainer(); });
  obs.observe(document.body, { childList: true, subtree: true });

  // Ensure initial visibility/count state is correct
  if (typeof updateNotificationBell === 'function') updateNotificationBell();

  // Ensure admin change-store modal HTML exists
  ensureAdminChangeStoreModal();

  // Navbar auth controls: show Log Out when logged in; redirect to /login on logout
  try {
    const updateNavbarAuth = () => {
  const isLoggedIn = localStorage.getItem('isLoggedIn') === 'true';
  const roleId = parseInt(localStorage.getItem('roleId') || '0', 10);
  // Privileged roles: Manager=8, Administrator=7 (per ax_user.role_id)
  const isPrivileged = isLoggedIn && (roleId === 7 || roleId === 8);
      const signIn = document.getElementById('navSignIn');
      const logIn = document.getElementById('navLogIn');
      const logOut = document.getElementById('navLogOut');
      if (signIn) signIn.classList.toggle('hidden', isLoggedIn);
      if (logIn) logIn.classList.toggle('hidden', isLoggedIn);
      if (logOut) logOut.classList.toggle('hidden', !isLoggedIn);

      // Hide EventLog nav link unless logged in (applies across pages)
      const eventLogLinks = Array.from(document.querySelectorAll('.navbar a'))
        .filter(a => (a.getAttribute('href') || '').toLowerCase().includes('/eventlog'));
  eventLogLinks.forEach(a => a.classList.toggle('hidden', !isPrivileged));

      // Hide export button unless logged in (EventLog page)
  const exportBtn = document.getElementById('exportBtn');
  if (exportBtn) exportBtn.classList.toggle('hidden', !isPrivileged);

      // Ensure Admin dropdown menu exists and is visible only for privileged users
      const rightContainer = document.querySelector('.navbar .flex-none') || document.querySelector('.navbar .flex-none.items-center');
      // Replace Admin button with a plain label showing the logged-in username
      // User label with dropdown for Logout
      let userDd = document.getElementById('userDropdown');
      if (!userDd && rightContainer) {
        userDd = document.createElement('div');
        userDd.id = 'userDropdown';
        userDd.className = 'dropdown';
        const activator = document.createElement('button');
        activator.id = 'adminLabel';
        activator.type = 'button';
        activator.tabIndex = 0;
        activator.role = 'button';
        // Make user code clearly visible on a neutral button
        activator.className = 'btn btn-sm px-3 py-1 rounded select-none cursor-pointer bg-base-200 text-base-content border border-base-300 flex items-center gap-2';
        const menu = document.createElement('ul');
        menu.tabIndex = 0;
  menu.className = 'dropdown-content menu p-2 shadow bg-base-100 rounded-box w-40';
  // Open directly below the activator, but align to open toward the left side to keep content visible
  menu.style.left = 'auto';
  menu.style.right = '0';
  menu.style.transform = 'translateX(-100%)';
  menu.style.top = 'calc(100% + 4px)';
  menu.style.minWidth = '10rem';
        menu.innerHTML = `
          <li><a href="#" id="ddLogout"><span class="material-icons mr-2">exit_to_app</span>Log Out</a></li>
        `;
        userDd.appendChild(activator);
        userDd.appendChild(menu);
        // Place it to the left of the bell if bell exists
        const bell = document.getElementById('notificationBell');
        if (bell && bell.parentElement === rightContainer) {
          rightContainer.insertBefore(userDd, bell);
        } else {
          rightContainer.insertBefore(userDd, rightContainer.firstChild);
        }
        // When opening, ensure the dropdown shifts left if it would overflow the right edge
        const positionUserDropdown = () => {
          try {
            const dd = document.getElementById('userDropdown');
            const act = document.getElementById('adminLabel');
            const m = dd ? dd.querySelector('.dropdown-content') : null;
            if (!dd || !act || !m) return;
            // Reset to base position under activator
            m.style.left = '0';
            m.style.right = 'auto';
            m.style.transform = 'none';
            m.style.top = 'calc(100% + 4px)';
            // Measure and shift left if overflowing viewport
            const mr = m.getBoundingClientRect();
            const overflowRight = mr.right - window.innerWidth;
            if (overflowRight > 0) {
              // Shift left so the right edge fits, keep a small margin
              const shift = overflowRight + 8;
              m.style.left = `-${shift}px`;
            }
          } catch {}
        };
        // Bind focus/click to adjust position just-in-time
        if (!activator._oc_bound_pos) {
          activator._oc_bound_pos = true;
          ['click','focus','mouseenter'].forEach(evt => {
            activator.addEventListener(evt, () => setTimeout(positionUserDropdown, 0));
          });
        }
      }
      const adminLabel = document.getElementById('adminLabel');
      const userDropdown = document.getElementById('userDropdown');
      if (adminLabel) {
        // Prefer exact ax_user.code stored in localStorage under 'code'
        const rawCode = localStorage.getItem('code') || localStorage.getItem('username') || '';
        const code = (rawCode || '').trim();
        // Show ax_user.code clearly inside button with icon and dropdown hint
        adminLabel.innerHTML = isLoggedIn && code ? `<span class="material-icons" style="font-size:18px;color:#4b5563;">person<\/span><span class="font-semibold" style="color:#111827;">${code}<\/span><span class="material-icons" style="font-size:18px;color:#6b7280;">expand_more<\/span>` : '';
        adminLabel.title = code || '';
        adminLabel.classList.toggle('hidden', !isLoggedIn);
        if (!code) {
          // If code is missing/empty, hide the entire dropdown to avoid a "dot"
          adminLabel.classList.add('hidden');
          if (userDropdown) userDropdown.classList.add('hidden');
        }
        // Hide entire dropdown container when not logged in
        if (userDropdown) userDropdown.classList.toggle('hidden', !isLoggedIn);
        // Button layout and readability
        adminLabel.style.display = 'flex';
        adminLabel.style.alignItems = 'center';
        adminLabel.style.gap = '8px';
        adminLabel.style.whiteSpace = 'nowrap';
        adminLabel.style.textOverflow = 'ellipsis';
        adminLabel.style.overflow = 'hidden';
        adminLabel.style.maxWidth = '320px';
      }
      const ddLogout = document.getElementById('ddLogout');
      if (ddLogout && !ddLogout._oc_bound) {
        ddLogout._oc_bound = true;
        ddLogout.addEventListener('click', (e) => {
          e.preventDefault();
          try {
            // Reuse existing logout behavior
            const logoutBtn = document.getElementById('navLogOut');
            if (logoutBtn) {
              logoutBtn.click();
              return;
            }
          } catch {}
          // Fallback: clear session and redirect
          try {
            const currentKey = _oc_getNotificationKey();
            try { localStorage.removeItem(currentKey); } catch {}
            window.notifications = [];
            updateNotificationBell();
            localStorage.removeItem('isLoggedIn');
            localStorage.removeItem('userId');
            localStorage.removeItem('lastActivityTs');
            localStorage.removeItem('roleId');
          } catch {}
          window.location.href = '/login';
        });
      }

      // Transform EventLog navbar link into a dropdown with Event Log + Change store details
      const navbar = document.querySelector('.navbar');
      if (navbar) {
        const links = Array.from(navbar.querySelectorAll('a'));
        const eventLogLink = links.find(a => (a.getAttribute('href') || '').toLowerCase().includes('/eventlog'));
        if (eventLogLink) {
          // Wrap the EventLog link with dropdown container if not already
          if (!eventLogLink.closest('#eventlogDropdown')) {
            const wrapper = document.createElement('div');
            wrapper.id = 'eventlogDropdown';
            wrapper.className = 'dropdown';
            // Create activator (use original EventLog button text)
            const activator = document.createElement('div');
            activator.tabIndex = 0;
            activator.role = 'button';
            activator.className = eventLogLink.className;
            // Rename to Menu with a down arrow to hint dropdown
            activator.innerHTML = `<span class="material-icons mr-2">menu</span> Menu <span class="material-icons ml-2">expand_more</span>`;
            // Create dropdown menu
            const menu = document.createElement('ul');
            menu.tabIndex = 0;
            menu.className = 'dropdown-content menu p-2 shadow bg-base-100 rounded-box w-56';
            menu.innerHTML = `
              <li><a href="#" id="ddChangeStore"><span class="material-icons mr-2">edit</span>Change Store Details</a></li>
              <li><a href="/eventlog" id="ddEventLog"><span class="material-icons mr-2">list</span>All Events</a></li>
            `;
            // Insert wrapper before original link and remove original
            const parent = eventLogLink.parentElement;
            parent.insertBefore(wrapper, eventLogLink);
            parent.removeChild(eventLogLink);
            wrapper.appendChild(activator);
            wrapper.appendChild(menu);

            // Bind Change Store modal open
            const ddChange = menu.querySelector('#ddChangeStore');
            if (ddChange && !ddChange._oc_bound) {
              ddChange._oc_bound = true;
              ddChange.addEventListener('click', (e) => {
                e.preventDefault();
                ensureAdminChangeStoreModal();
                preloadStoresIntoModal();
                openAdminChangeStoreModal();
              });
            }
          }
          // Toggle dropdown visibility based on privilege
          const dd = document.getElementById('eventlogDropdown');
          if (dd) dd.classList.toggle('hidden', !isPrivileged);
        }
      }
    };
    updateNavbarAuth();

    // If code is set slightly after page load (e.g., post-login async),
    // retry updating the label for a short period until it appears.
    try {
      let attempts = 0;
      const maxAttempts = 40; // ~10 seconds at 250ms
      const retry = setInterval(() => {
        attempts++;
        const isLoggedIn = localStorage.getItem('isLoggedIn') === 'true';
        const code = (localStorage.getItem('code') || localStorage.getItem('username') || '').trim();
        const adminLabel = document.getElementById('adminLabel');
        // If logged in and we now have code but label is still empty, refresh UI
        if (isLoggedIn && code && adminLabel && adminLabel.textContent.trim() === '') {
          updateNavbarAuth();
          clearInterval(retry);
        }
        if (attempts >= maxAttempts) clearInterval(retry);
      }, 250);
    } catch {}

    // Global nav actions: ensure Log In / Sign In open the right form immediately
    const navLogIn = document.getElementById('navLogIn');
    if (navLogIn && !navLogIn._oc_bound) {
      navLogIn._oc_bound = true;
      navLogIn.addEventListener('click', (e) => {
        e.preventDefault();
        // Always go to /login with mode=login so the login form shows immediately
        window.location.href = '/login?mode=login';
      });
    }
    const navSignIn = document.getElementById('navSignIn');
    if (navSignIn && !navSignIn._oc_bound) {
      navSignIn._oc_bound = true;
      navSignIn.addEventListener('click', (e) => {
        e.preventDefault();
        // Always go to /login with mode=signin so the register form shows immediately
        window.location.href = '/login?mode=signin';
      });
    }
    const logoutBtn = document.getElementById('navLogOut');
    if (logoutBtn && !logoutBtn._oc_bound) {
      logoutBtn._oc_bound = true;
      logoutBtn.addEventListener('click', () => {
        try {
          // Clear current user's notification store on logout
          const currentKey = _oc_getNotificationKey();
          try { localStorage.removeItem(currentKey); } catch {}
          window.notifications = [];
          updateNotificationBell();

          localStorage.removeItem('isLoggedIn');
          localStorage.removeItem('userId');
          localStorage.removeItem('lastActivityTs');
          localStorage.removeItem('roleId');
        } catch {}
        updateNavbarAuth();
        // redirect to login page
        window.location.href = '/login';
      });
    }
  } catch {}

  // Inactivity logout: auto log out after exactly 30 minutes of no user activity
  try {
    const INACTIVITY_MS = 30 * 60 * 1000; // 30 minutes

    const markActivity = () => {
      try {
        localStorage.setItem('lastActivityTs', Date.now().toString());
      } catch {}
    };

    // Initialize last activity when script loads
    markActivity();

    // Listen for common user interactions to update activity timestamp
    ['click','mousemove','keydown','scroll','touchstart'].forEach(evt => {
      window.addEventListener(evt, markActivity, { passive: true });
    });

    // Periodically check for inactivity and enforce logout
    if (!window._oc_inactivityTimer) {
      window._oc_inactivityTimer = setInterval(() => {
        try {
          const isLoggedIn = localStorage.getItem('isLoggedIn') === 'true';
          if (!isLoggedIn) return; // nothing to do
          const ts = parseInt(localStorage.getItem('lastActivityTs') || '0', 10);
          const now = Date.now();
          if (ts > 0 && (now - ts) >= INACTIVITY_MS) {
            // Auto-logout
            localStorage.removeItem('isLoggedIn');
            localStorage.removeItem('userId');
            localStorage.removeItem('lastActivityTs');
            // Optional: show a toast after redirect indicating session timeout
            try {
              sessionStorage.setItem('pendingToast', JSON.stringify({
                type: 'warning',
                message: 'You have been logged out due to 30 minutes of inactivity.'
              }));
            } catch {}
            // Redirect to login
            window.location.href = '/login';
          }
        } catch {}
      }, 15000); // check every 15s
    }
  } catch {}

  // If a redirect stored a pending toast (via sessionStorage), show it now and then clear it.
  try {
    const pending = sessionStorage.getItem('pendingToast');
    if (pending) {
      const payload = JSON.parse(pending);
      // Ensure container is positioned before showing
      adjustToastContainer();
      showToast(payload?.type || 'info', payload?.message || '');
      sessionStorage.removeItem('pendingToast');
    }
  } catch {}
}

// Position the toast stack so it starts just below the navbar and never covers
// the bell or right-side header buttons.
function adjustToastContainer() {
  const tc = document.getElementById('toastContainer');
  if (!tc) return;
  const navbar = document.querySelector('.navbar');
  const bell = document.getElementById('notificationBell');
  const exportBtn = document.getElementById('exportBtn');

  // Base placement: fixed top-right
  tc.style.position = 'fixed';
  tc.style.right = '20px';
  tc.style.left = 'auto';
  tc.style.bottom = 'auto';
  // Ensure toasts render above page content but stay below the navbar by placement, not z-index
  // Use a high z-index so they aren't hidden by content containers.
  tc.style.zIndex = '9999';
  // Prevent the toast container from blocking navbar clicks, while allowing clicks on toasts themselves
  tc.style.pointerEvents = 'none';

  let topOffset = 20;
  try {
    const candidates = [];
    if (navbar) {
      const r = navbar.getBoundingClientRect();
      candidates.push(Math.round(r.bottom));
    }
    if (bell) {
      const r = bell.getBoundingClientRect();
      candidates.push(Math.round(r.bottom));
    }
    if (exportBtn) {
      const r = exportBtn.getBoundingClientRect();
      candidates.push(Math.round(r.bottom));
    }
    const anchorBottom = candidates.length ? Math.max(...candidates) : 0;
    // Align toasts exactly with the bottom edge of the highest top element, plus 1px to avoid overlap
    topOffset = Math.max(0, anchorBottom + 1);
  } catch {}
  tc.style.top = `${topOffset}px`;

  // If bell is fixed (no navbar), keep some separation implicitly via topOffset.
}

function adjustBellPosition() {
  const bell = document.getElementById('notificationBell');
  if (!bell) return;
  // If the bell is inside the navbar layout, let CSS/layout handle placement.
  const inNavbar = !!bell.closest('.navbar');
  const b = bell.getBoundingClientRect();
  if (inNavbar) {
    // ensure it doesn't accidentally use absolute/fixed offsets
    bell.style.left = '';
    bell.style.top = '';
    bell.style.right = '';
    return;
  }

  // default: fixed to top-right of viewport
  bell.style.left = '';
  bell.style.top = '16px';
  bell.style.right = '16px';

  // If an explicit export button exists and still overlaps, shift bell left of it
  const exportBtn = document.getElementById('exportBtn');
  if (exportBtn) {
    try {
      const e = exportBtn.getBoundingClientRect();
      const bellLeft = window.innerWidth - 16 - b.width;
      if (bellLeft < e.right) {
        const newLeft = Math.max(8, e.left - b.width - 8);
        bell.style.left = `${newLeft}px`;
        bell.style.right = '';
      }
    } catch (err) { /* ignore */ }
  }
}

function showToast(type, message) {
  // Returns a Promise that resolves when the toast is dismissed and the
  // notification is stored to the bell (either after timeout or when user
  // clicks the toast). This allows callers to await the toast lifecycle
  // (useful for delaying redirects until the user saw the message).
  return new Promise((resolve) => {
    const container = document.getElementById('toastContainer');
    const toast = document.createElement('div');
    toast.className = `toast ${type}`;
    toast.textContent = message;
  // Allow interaction with the toast itself
  toast.style.pointerEvents = 'auto';
    // Insert newest toasts at the top so they stack from top->bottom
    container.insertBefore(toast, container.firstChild);

    let stored = false;
    const storeNotification = () => {
      if (stored) return;
      stored = true;
      window.notifications.push({ type, message });
      updateNotificationBell();
      resolve();
    };

    // Clicking the visible toast removes it and stores as a notification
    toast.addEventListener('click', () => {
      if (container.contains(toast)) container.removeChild(toast);
      storeNotification();
    });

    // After 10s, remove the visible toast and store it in the bell
    const t = setTimeout(() => {
      if (container.contains(toast)) container.removeChild(toast);
      storeNotification();
    }, 10000);

    // If the page unloads before the timeout we still want to store the
    // notification so it appears in the bell on the next page.
    window.addEventListener('beforeunload', () => {
      clearTimeout(t);
      storeNotification();
    });
  });
}

function updateNotificationBell() {
  const count = (window.notifications || []).length;
  const badge = document.getElementById('notificationCount');
  const bell = document.getElementById('notificationBell');
  if (badge) {
    badge.textContent = count;
    // Hide badge when count is 0; show otherwise
    badge.classList.toggle('hidden', count === 0);
  }

  // Keep the bell visible at all times; badge visibility depends on count (hidden when 0).
  // Ensure bell remains visible:
  if (bell) {
    bell.classList.remove('hidden');
  }

  const list = document.getElementById('notificationList');
  // Render notifications safely using textContent to avoid XSS
  list.innerHTML = '';
  const filter = window._notificationFilterType || null;
  (window.notifications || [])
    .filter(n => !filter || n.type === filter)
    .forEach((n, idx) => {
    const el = document.createElement('div');
    el.className = `notification-item ${n.type}`;
    el.textContent = n.message;

    // allow removing this specific notification
    const closeBtn = document.createElement('button');
    closeBtn.className = 'notification-close';
    closeBtn.setAttribute('aria-label', 'Dismiss notification');
    closeBtn.textContent = '×';
    closeBtn.onclick = function (e) {
      e.stopPropagation();
      // remove the notification at this index and re-render
        const indexToRemove = (window.notifications || []).indexOf(n);
        if (indexToRemove > -1) {
          window.notifications.splice(indexToRemove, 1);
        }
      updateNotificationBell();
    };

    // position close button inside the notification item
    el.style.position = 'relative';
    closeBtn.style.position = 'absolute';
    closeBtn.style.top = '6px';
    closeBtn.style.right = '8px';
    closeBtn.style.border = 'none';
    closeBtn.style.background = 'transparent';
    closeBtn.style.cursor = 'pointer';
    closeBtn.style.fontSize = '14px';
    closeBtn.style.lineHeight = '1';

    el.appendChild(closeBtn);
    list.appendChild(el);
  });

  // Persist to localStorage using per-user or guest scope
  _oc_saveNotifications();

  // ensure bell UI exists and adjust position after rendering notifications
  adjustBellPosition();

  // If panel is currently open, keep it anchored at the cached position
  const panel = document.getElementById('notificationPanel');
  if (panel && !panel.classList.contains('hidden') && window._notificationPanelPos) {
    panel.style.left = `${window._notificationPanelPos.left}px`;
    panel.style.top = `${window._notificationPanelPos.top}px`;
  }
}

function dismissAll() {
  window.notifications = [];
  updateNotificationBell();
}

function closeNotificationPanel() {
  const panel = document.getElementById('notificationPanel');
  if (!panel) return;
  panel.classList.add('hidden');
  panel.style.left = '';
  panel.style.top = '';
  panel.style.width = '';
  panel.style.display = '';
  panel.style.visibility = '';
  window._notificationPanelPos = null;
  // Reset notification filter on close so all messages are shown next time
  window._notificationFilterType = null;
  updateNotificationBell();
  if (window._notificationOutsideHandler) {
    document.removeEventListener('click', window._notificationOutsideHandler);
    window._notificationOutsideHandler = null;
  }
}

function openNotificationPanel() {
  const panel = document.getElementById('notificationPanel');
  const bell = document.getElementById('notificationBell');
  if (!panel || !bell) return;
  updateNotificationBell();
  // Show the panel invisibly to measure its size
  panel.classList.remove('hidden');
  panel.style.visibility = 'hidden';
  panel.style.display = 'block';
  const rect = bell.getBoundingClientRect();
  const panelWidth = panel.offsetWidth || 320;
  // compute left so panel's right edge aligns with bell's right edge
  // With fixed-position panel, compute using viewport coordinates
  const left = Math.min(rect.right - panelWidth + 8, window.innerWidth - panelWidth - 8);
  const top = rect.bottom + 8;
  panel.style.left = `${left}px`;
  panel.style.top = `${top}px`;
  // Lock width while open so content changes (e.g., dismiss all) don't reflow and shift alignment
  panel.style.width = `${panelWidth}px`;
  panel.style.visibility = '';
  panel.style.display = '';

  // Cache position while open to prevent jumps during content changes
  window._notificationPanelPos = { left, top };

  // Add click-away handler after current event loop to avoid immediate close
  setTimeout(() => {
    window._notificationOutsideHandler = function (e) {
      const panelEl = document.getElementById('notificationPanel');
      const bellEl = document.getElementById('notificationBell');
      if (!panelEl) return;
      if (panelEl.contains(e.target) || (bellEl && bellEl.contains(e.target))) return;
      closeNotificationPanel();
    };
    document.addEventListener('click', window._notificationOutsideHandler);
  }, 0);
}

function toggleNotificationPanel() {
  const panel = document.getElementById('notificationPanel');
  if (!panel) return;
  if (panel.classList.contains('hidden')) openNotificationPanel(); else closeNotificationPanel();
}

// Initialize notification UI on load
if (document.readyState === 'loading') {
  document.addEventListener('DOMContentLoaded', () => {
    ensureNotificationUI();
    try {
      // Prevent horizontal scroll across pages
      const styleId = 'global-layout-fixes';
      if (!document.getElementById(styleId)) {
        const style = document.createElement('style');
        style.id = styleId;
        style.textContent = `html, body { overflow-x: hidden; }`;
        document.head.appendChild(style);
      }
    } catch {}
  });
} else {
  ensureNotificationUI();
  try {
    const styleId = 'global-layout-fixes';
    if (!document.getElementById(styleId)) {
      const style = document.createElement('style');
      style.id = styleId;
      style.textContent = `html, body { overflow-x: hidden; }`;
      document.head.appendChild(style);
    }
  } catch {}
}

// --- Admin Change Store modal helpers ---
function ensureAdminChangeStoreModal() {
  if (document.getElementById('adminChangeStoreModal')) return;
  const modal = document.createElement('dialog');
  modal.id = 'adminChangeStoreModal';
  modal.className = 'modal';
  modal.innerHTML = `
    <div class="modal-box">
      <h3 class="font-bold text-lg mb-2 flex items-center"><span class="material-icons mr-2 text-emerald-600">edit</span>Change Store Details</h3>
      <p class="text-sm text-gray-600 mb-4">Update store name, address, phone, email, and working hours.</p>
      <form id="adminStoreForm" class="space-y-3">
        <select id="storeSelect" class="select select-bordered w-full">
          <option value="">Select a store...</option>
        </select>
        <input type="text" class="input input-bordered w-full" id="storeName" placeholder="Store name" />
        <input type="text" class="input input-bordered w-full" id="storeAddress" placeholder="Address" />
        <input type="text" class="input input-bordered w-full" id="storePhone" placeholder="Phone" />
        <input type="email" class="input input-bordered w-full" id="storeEmail" placeholder="Email" />
        <div id="hoursEditor" class="space-y-3">
          <label class="block font-semibold">Working hours</label>
          <div class="p-3 rounded border border-base-300 bg-base-200/30">
            <div class="flex items-center justify-between mb-2">
              <div class="text-sm font-medium">Mon–Fri</div>
              <label class="label cursor-pointer gap-2 m-0">
                <span class="label-text">Closed</span>
                <input type="checkbox" id="mhClosed" class="checkbox checkbox-sm" />
              </label>
            </div>
            <div class="flex gap-3 w-full">
              <input type="time" id="mhStart" class="input input-bordered input-sm w-full" step="1800" min="00:00" max="23:30" />
              <span class="self-center text-sm text-base-content/70">to</span>
              <input type="time" id="mhEnd" class="input input-bordered input-sm w-full" step="1800" min="00:00" max="23:30" />
            </div>
          </div>

          <div class="p-3 rounded border border-base-300 bg-base-200/30">
            <div class="flex items-center justify-between mb-2">
              <div class="text-sm font-medium">Sat</div>
              <label class="label cursor-pointer gap-2 m-0">
                <span class="label-text">Closed</span>
                <input type="checkbox" id="shClosed" class="checkbox checkbox-sm" />
              </label>
            </div>
            <div class="flex gap-3 w-full">
              <input type="time" id="shStart" class="input input-bordered input-sm w-full" step="1800" min="00:00" max="23:30" />
              <span class="self-center text-sm text-base-content/70">to</span>
              <input type="time" id="shEnd" class="input input-bordered input-sm w-full" step="1800" min="00:00" max="23:30" />
            </div>
          </div>

          <div class="p-3 rounded border border-base-300 bg-base-200/30">
            <div class="flex items-center justify-between mb-2">
              <div class="text-sm font-medium">Sun</div>
              <label class="label cursor-pointer gap-2 m-0">
                <span class="label-text">Closed</span>
                <input type="checkbox" id="suClosed" class="checkbox checkbox-sm" />
              </label>
            </div>
            <div class="flex gap-3 w-full">
              <input type="time" id="suStart" class="input input-bordered input-sm w-full" step="1800" min="00:00" max="23:30" />
              <span class="self-center text-sm text-base-content/70">to</span>
              <input type="time" id="suEnd" class="input input-bordered input-sm w-full" step="1800" min="00:00" max="23:30" />
            </div>
          </div>
        </div>
        <textarea class="textarea textarea-bordered w-full hidden" id="storeHours" placeholder="Working hours"></textarea>
      </form>
      <div class="modal-action">
        <button id="adminStoreCancel" class="btn">Cancel</button>
        <button id="adminStoreOk" class="btn btn-primary">OK</button>
      </div>
    </div>
  `;
  document.body.appendChild(modal);

  // Bind hours editor interactions (disable inputs when closed)
  try { bindHoursEditor(); } catch {}

  // Wire buttons
  const cancelBtn = modal.querySelector('#adminStoreCancel');
  const okBtn = modal.querySelector('#adminStoreOk');
  if (cancelBtn && !cancelBtn._oc_bound) {
    cancelBtn._oc_bound = true;
    cancelBtn.addEventListener('click', (e) => {
      e.preventDefault();
      closeAdminChangeStoreModal();
    });
  }
  if (okBtn && !okBtn._oc_bound) {
    okBtn._oc_bound = true;
    okBtn.addEventListener('click', async (e) => {
      e.preventDefault();
      try {
        const sel = document.getElementById('storeSelect');
        const id = parseInt(sel?.value || '0', 10);
        if (!id) { showToast('warning', 'Please select a store.'); return; }
        const hoursStr = getHoursStringFromEditor();
        const payload = {
          Name: document.getElementById('storeName')?.value || '',
          Address: document.getElementById('storeAddress')?.value || '',
          Phone_Number: document.getElementById('storePhone')?.value || '',
          Email: document.getElementById('storeEmail')?.value || '',
          Working_Hours: hoursStr
        };
        const hiddenHours = document.getElementById('storeHours');
        if (hiddenHours) hiddenHours.value = hoursStr;
        const userId = localStorage.getItem('userId') || 2;
        const res = await fetch(`/api/stores/${id}?userId=${userId}`, {
          method: 'PUT',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify(payload)
        });
        if (res.ok) {
          showToast('info', 'Store changes saved.');
          closeAdminChangeStoreModal();
        } else {
          // Log error to server event log with full server response text
          try {
            const userId = parseInt(localStorage.getItem('userId') || '2', 10) || 2;
            let serverText = '';
            try {
              serverText = await res.text();
            } catch {}
            await fetch('/api/log-client-error', {
              method: 'POST',
              headers: { 'Content-Type': 'application/json' },
              body: JSON.stringify({
                userId,
                description: 'Failed to save store changes',
                stackTrace: serverText || `HTTP ${res.status} - ${res.statusText}`
              })
            });
          } catch {}
          showToast('error', 'Failed to save changes.');
        }
      } catch (err) {
        // Log unexpected exception to server event log
        try {
          const userId = parseInt(localStorage.getItem('userId') || '2', 10) || 2;
          await fetch('/api/log-client-error', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({
              userId,
              description: 'Error while saving store changes',
              stackTrace: (err && err.stack) ? String(err.stack) : String(err)
            })
          });
        } catch {}
        showToast('error', 'Error while saving changes.');
      }
    });
  }
}

function openAdminChangeStoreModal() {
  const modal = document.getElementById('adminChangeStoreModal');
  if (!modal) return;
  try { modal.showModal(); } catch { modal.classList.remove('hidden'); }
}

function closeAdminChangeStoreModal() {
  const modal = document.getElementById('adminChangeStoreModal');
  if (!modal) return;
  try { modal.close(); } catch { modal.classList.add('hidden'); }
}

// --- Working hours editor helpers ---
function updateHoursEditorDisabled() {
  const pairs = [
    { c: 'mhClosed', s: 'mhStart', e: 'mhEnd' },
    { c: 'shClosed', s: 'shStart', e: 'shEnd' },
    { c: 'suClosed', s: 'suStart', e: 'suEnd' }
  ];
  pairs.forEach(({ c, s, e }) => {
    const cb = document.getElementById(c);
    const si = document.getElementById(s);
    const ei = document.getElementById(e);
    if (!cb || !si || !ei) return;
    const closed = !!cb.checked;
    si.disabled = closed;
    ei.disabled = closed;
    if (closed) { si.value = ''; ei.value = ''; }
  });
}

function bindHoursEditor() {
  const ids = ['mhClosed','shClosed','suClosed'];
  ids.forEach(id => {
    const el = document.getElementById(id);
    if (!el || el._oc_bound) return;
    el._oc_bound = true;
    el.addEventListener('change', () => updateHoursEditorDisabled());
  });
  // Bind time inputs to normalize to 30-minute steps (00 or 30) and fix ranges
  const timeIds = ['mhStart','mhEnd','shStart','shEnd','suStart','suEnd'];
  timeIds.forEach(id => {
    const el = document.getElementById(id);
    if (!el || el._oc_norm_bound) return;
    el._oc_norm_bound = true;
    const handler = () => { try { el.value = normalizeToHalfHour(el.value); fixPairRanges(); } catch {} };
    ['change','blur'].forEach(evt => el.addEventListener(evt, handler));
  });
  // Also enforce ranges initially
  fixPairRanges();
  updateHoursEditorDisabled();
}

function normalizeToHalfHour(val) { // kept for parser compatibility
  if (!val || typeof val !== 'string') return '';
  const m = val.match(/^(\d{1,2}):(\d{2})/);
  if (!m) return '';
  let h = parseInt(m[1], 10);
  let min = parseInt(m[2], 10);
  if (isNaN(h) || isNaN(min)) return '';
  if (h < 0) h = 0; if (h > 23) h = 23;
  min = (min < 15) ? 0 : (min < 45) ? 30 : (h < 23 ? (h++, 0) : 30);
  const hh = String(h).padStart(2, '0');
  const mm = String(min).padStart(2, '0');
  return `${hh}:${mm}`;
}

// (reverted) select-based helpers removed

function timeToMinutes(val) {
  if (!val) return null;
  const m = val.match(/^(\d{1,2}):(\d{2})$/);
  if (!m) return null;
  const h = parseInt(m[1], 10); const min = parseInt(m[2], 10);
  if (isNaN(h) || isNaN(min)) return null;
  return (h * 60) + min;
}

function nextHalfHour(val) {
  const mins = timeToMinutes(normalizeToHalfHour(val));
  if (mins == null) return '';
  const next = mins + 30;
  if (next > (23 * 60 + 30)) return '';
  const h = Math.floor(next / 60); const m = next % 60;
  return `${String(h).padStart(2,'0')}:${String(m).padStart(2,'0')}`;
}

function prevHalfHour(val) {
  const mins = timeToMinutes(normalizeToHalfHour(val));
  if (mins == null) return '';
  const prev = mins - 30;
  if (prev < 0) return '';
  const h = Math.floor(prev / 60); const m = prev % 60;
  return `${String(h).padStart(2,'0')}:${String(m).padStart(2,'0')}`;
}

function ensureValidRange(startId, endId) {
  const sEl = document.getElementById(startId);
  const eEl = document.getElementById(endId);
  if (!sEl || !eEl) return;
  const s = normalizeToHalfHour(sEl.value || '');
  const e = normalizeToHalfHour(eEl.value || '');
  if (!s || !e) { sEl.value = s; eEl.value = e; return; }
  const sm = timeToMinutes(s), em = timeToMinutes(e);
  if (sm == null || em == null) { sEl.value = s; eEl.value = e; return; }
  if (em <= sm) {
    const cand = nextHalfHour(s);
    if (cand) {
      eEl.value = cand;
    } else {
      // If s is already at 23:30, fallback to 23:00–23:30
      sEl.value = '23:00';
      eEl.value = '23:30';
    }
  } else {
    sEl.value = s; eEl.value = e;
  }
}

function fixPairRanges() {
  ensureValidRange('mhStart','mhEnd');
  ensureValidRange('shStart','shEnd');
  ensureValidRange('suStart','suEnd');
}
function setHoursEditorFromString(str) {
  const mhClosed = document.getElementById('mhClosed');
  const mhStart = document.getElementById('mhStart');
  const mhEnd = document.getElementById('mhEnd');
  const shClosed = document.getElementById('shClosed');
  const shStart = document.getElementById('shStart');
  const shEnd = document.getElementById('shEnd');
  const suClosed = document.getElementById('suClosed');
  const suStart = document.getElementById('suStart');
  const suEnd = document.getElementById('suEnd');

  const clear = () => {
    [mhClosed, shClosed, suClosed].forEach(cb => { if (cb) cb.checked = false; });
    [mhStart, mhEnd, shStart, shEnd, suStart, suEnd].forEach(i => { if (i) i.value = ''; });
  };
  clear();

  if (!str || typeof str !== 'string') return;

  const norm = str.replace(/\s+/g, ' ').trim();
  const parts = norm.split(';').map(p => p.trim()).filter(Boolean);
  const parseClause = (label) => parts.find(p => p.toLowerCase().startsWith(label.toLowerCase()));

  const applyTimes = (closedEl, startEl, endEl, clause) => {
    if (!clause) return;
    if (/closed/i.test(clause)) { if (closedEl) closedEl.checked = true; return; }
    const m = clause.match(/(\d{1,2}:\d{2})\s*-\s*(\d{1,2}:\d{2})/);
    if (m) {
      if (startEl) startEl.value = normalizeToHalfHour(m[1].padStart(5, '0'));
      if (endEl) endEl.value = normalizeToHalfHour(m[2].padStart(5, '0'));
    }
  };

  // Support grouped labels
  const monSun = parseClause('Mon-Sun');
  if (monSun) {
    applyTimes(mhClosed, mhStart, mhEnd, monSun);
    applyTimes(shClosed, shStart, shEnd, monSun);
    applyTimes(suClosed, suStart, suEnd, monSun);
    return;
  }
  const monSat = parseClause('Mon-Sat');
  if (monSat) {
    applyTimes(mhClosed, mhStart, mhEnd, monSat);
    applyTimes(shClosed, shStart, shEnd, monSat);
  }
  const monFri = parseClause('Mon-Fri');
  applyTimes(mhClosed, mhStart, mhEnd, monFri);
  const sat = parseClause('Sat') || parseClause('Sat-Sun');
  applyTimes(shClosed, shStart, shEnd, sat);
  const sun = parseClause('Sun') || parseClause('Sat-Sun');
  applyTimes(suClosed, suStart, suEnd, sun);
  updateHoursEditorDisabled();
}

function getHoursStringFromEditor() {
  const mh = {
    closed: !!document.getElementById('mhClosed')?.checked,
    start: normalizeToHalfHour(document.getElementById('mhStart')?.value || ''),
    end: normalizeToHalfHour(document.getElementById('mhEnd')?.value || '')
  };
  const sa = {
    closed: !!document.getElementById('shClosed')?.checked,
    start: normalizeToHalfHour(document.getElementById('shStart')?.value || ''),
    end: normalizeToHalfHour(document.getElementById('shEnd')?.value || '')
  };
  const su = {
    closed: !!document.getElementById('suClosed')?.checked,
    start: normalizeToHalfHour(document.getElementById('suStart')?.value || ''),
    end: normalizeToHalfHour(document.getElementById('suEnd')?.value || '')
  };

  const fmt = (t) => t ? t.padStart(5, '0') : '';
  const sameTimes = (a, b) => !a.closed && !b.closed && a.start === b.start && a.end === b.end && a.start && a.end;
  const isClosed = (x) => x.closed || (!x.start && !x.end);

  // All closed
  if (isClosed(mh) && isClosed(sa) && isClosed(su)) return 'Mon-Sun Closed';

  // All same non-closed times
  if (sameTimes(mh, sa) && sameTimes(sa, su)) return `Mon-Sun ${fmt(mh.start)}-${fmt(mh.end)}`;

  const segments = [];
  // Try merge Mon-Fri with Sat
  if (sameTimes(mh, sa)) {
    segments.push(`Mon-Sat ${fmt(mh.start)}-${fmt(mh.end)}`);
  } else {
    if (isClosed(mh)) segments.push('Mon-Fri Closed'); else segments.push(`Mon-Fri ${fmt(mh.start)}-${fmt(mh.end)}`);
    if (isClosed(sa)) segments.push('Sat Closed'); else segments.push(`Sat ${fmt(sa.start)}-${fmt(sa.end)}`);
  }

  // Merge Sat/Sun if not already merged into Mon-Sat
  if (segments.length === 1) {
    // We already pushed Mon-Sat; decide Sun
    if (sameTimes(mh, su)) {
      // Upgrade to Mon-Sun
      return `Mon-Sun ${fmt(mh.start)}-${fmt(mh.end)}`;
    } else {
      if (isClosed(su)) segments.push('Sun Closed'); else segments.push(`Sun ${fmt(su.start)}-${fmt(su.end)}`);
      return segments.join('; ');
    }
  } else {
    // Segments has Mon-Fri and Sat separately
    if (sameTimes(sa, su)) {
      // Replace last segment (Sat ...) with Sat-Sun ...
      segments.pop();
      if (isClosed(sa)) segments.push('Sat-Sun Closed'); else segments.push(`Sat-Sun ${fmt(sa.start)}-${fmt(sa.end)}`);
    } else {
      if (isClosed(su)) segments.push('Sun Closed'); else segments.push(`Sun ${fmt(su.start)}-${fmt(su.end)}`);
    }
    return segments.join('; ');
  }
}

async function preloadStoresIntoModal() {
  try {
    const userId = localStorage.getItem('userId') || 2;
    const res = await fetch(`/api/stores?userId=${userId}`);
    const data = await res.json();
    let items = data.items || [];
    try { items = items.slice().sort((a, b) => (a.id ?? 0) - (b.id ?? 0)); } catch {}
    const sel = document.getElementById('storeSelect');
    if (!sel) return;
    // Clear and repopulate options
    sel.innerHTML = '<option value="">Select a store...</option>';
    items.forEach(s => {
      const opt = document.createElement('option');
      opt.value = s.id;
      opt.textContent = s.name;
      sel.appendChild(opt);
    });
    // Bind change to populate fields
    if (!sel._oc_bound) {
      sel._oc_bound = true;
      sel.addEventListener('change', () => {
        const id = parseInt(sel.value || '0', 10);
        const s = items.find(x => x.id === id);
        document.getElementById('storeName').value = s?.name || '';
        document.getElementById('storeAddress').value = s?.address || '';
        document.getElementById('storePhone').value = s?.phone || '';
        document.getElementById('storeEmail').value = s?.email || '';
        document.getElementById('storeHours').value = s?.hours || '';
        setHoursEditorFromString(s?.hours || '');
        fixPairRanges();
        updateHoursEditorDisabled();
      });
    }

    // Always start with an empty selection and blank fields when opening
    sel.value = '';
    document.getElementById('storeName').value = '';
    document.getElementById('storeAddress').value = '';
    document.getElementById('storePhone').value = '';
    document.getElementById('storeEmail').value = '';
    document.getElementById('storeHours').value = '';
    setHoursEditorFromString('');
    updateHoursEditorDisabled();
  } catch (err) {
    // Log load error: use logged-in userId, else default to system (2)
    try {
      const isLoggedIn = localStorage.getItem('isLoggedIn') === 'true';
      const uid = isLoggedIn ? parseInt(localStorage.getItem('userId') || '2', 10) || 2 : 2;
      await fetch('/api/log-client-error', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          userId: uid,
          description: 'Failed to load stores for address page',
          stackTrace: (err && err.stack) ? String(err.stack) : String(err)
        })
      });
    } catch {}
    showToast('error', 'Failed to load stores.');
  }
}


document.addEventListener('DOMContentLoaded', () => {
  try {
    const raw = sessionStorage.getItem('pendingToast');
    if (!raw) return;

    const { type, message } = JSON.parse(raw);

    showToast(type, message);

    sessionStorage.removeItem('pendingToast');
  } catch (err) {
    sessionStorage.removeItem('pendingToast');
  }
});

// Global image styling: apply consistent cover/rounded and lazy-load
(() => {
  const enhance = (img) => {
    if (!img || img._oc_img_bound) return;
    img._oc_img_bound = true;
    img.classList.add('object-cover');
    img.classList.add('rounded-lg');
    img.loading = img.loading || 'lazy';
    // Note: No automatic placeholder swap; we only show concrete images provided in the app.
  };

  document.querySelectorAll('img').forEach(enhance);

  const mo = new MutationObserver((mutations) => {
    for (const m of mutations) {
      m.addedNodes?.forEach((n) => {
        if (n && n.tagName === 'IMG') enhance(n);
        if (n && n.querySelectorAll) n.querySelectorAll('img').forEach(enhance);
      });
    }
  });
  mo.observe(document.body, { childList: true, subtree: true });
})();