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
    const badge = document.createElement('span');
    badge.id = 'notificationCount';
    badge.className = 'badge hidden'; // hidden when count is 0
    badge.textContent = '0';
    bell.appendChild(badge);

    // Prefer to place the bell inside the navbar's right-side controls so it won't overlap
    // other header buttons. If no navbar exists, fall back to fixed position on the viewport.
    const rightContainer = document.querySelector('.navbar .flex-none') || document.querySelector('.navbar .flex-none.items-center');
    const navbarRoot = document.querySelector('.navbar');
    const exportBtn = document.getElementById('exportBtn');
    if (rightContainer) {
      // Always append the bell as the last child of the navbar right-controls so it is
      // the right-most element across all pages (per user request).
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
        rightContainer.appendChild(bell);
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
    infoBtn.className = 'btn btn-xs btn-success';
    infoBtn.textContent = 'Information';
    infoBtn.onclick = function() {
      window._notificationFilterType = 'info';
      updateNotificationBell();
    };
    controls.appendChild(infoBtn);

    const warnBtn = document.createElement('button');
    warnBtn.className = 'btn btn-xs btn-warning';
    warnBtn.textContent = 'Warning';
    warnBtn.onclick = function() {
      window._notificationFilterType = 'warning';
      updateNotificationBell();
    };
    controls.appendChild(warnBtn);

    const errorBtn = document.createElement('button');
    errorBtn.className = 'btn btn-xs btn-error';
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

  // Navbar auth controls: show Log Out when logged in; redirect to /login on logout
  try {
    const updateNavbarAuth = () => {
      const isLoggedIn = localStorage.getItem('isLoggedIn') === 'true';
      const signIn = document.getElementById('navSignIn');
      const logIn = document.getElementById('navLogIn');
      const logOut = document.getElementById('navLogOut');
      if (signIn) signIn.classList.toggle('hidden', isLoggedIn);
      if (logIn) logIn.classList.toggle('hidden', isLoggedIn);
      if (logOut) logOut.classList.toggle('hidden', !isLoggedIn);

      // Hide EventLog nav link unless logged in (applies across pages)
      const eventLogLinks = Array.from(document.querySelectorAll('.navbar a'))
        .filter(a => (a.getAttribute('href') || '').toLowerCase().includes('/eventlog'));
      eventLogLinks.forEach(a => a.classList.toggle('hidden', !isLoggedIn));

      // Hide export button unless logged in (EventLog page)
      const exportBtn = document.getElementById('exportBtn');
      if (exportBtn) exportBtn.classList.toggle('hidden', !isLoggedIn);
    };
    updateNavbarAuth();

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
  document.addEventListener('DOMContentLoaded', ensureNotificationUI);
} else {
  ensureNotificationUI();
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