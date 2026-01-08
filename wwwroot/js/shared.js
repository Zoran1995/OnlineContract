
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

// Minimal, resilient capture-phase handler to toggle navbar dropdowns
// Ensures `#eventlogDropdown` (Menu) and `#userDropdown` (Admin/user) open on click,
// and prevents immediate document-level handlers from closing them.
if (!window._oc_navbar_capture_bound) {
  window._oc_navbar_capture_bound = true;
  document.addEventListener('pointerdown', function (e) {
    try {
      const tgt = e.target;

      // Helper to toggle a dropdown element and its .dropdown-content
      function toggleDropdown(wrapper) {
        if (!wrapper) return false;
        wrapper.classList.toggle('dropdown-open');
        const content = wrapper.querySelector('.dropdown-content');
        if (content) content.classList.toggle('hidden');
        return true;
      }

      // If clicking the EventLog/Menu activator or its children
      const eventWrapper = document.getElementById('eventlogDropdown');
      if (eventWrapper) {
        const eventActivator = eventWrapper.querySelector('[role="button"], .oc-dropdown-activator');
        // Only treat clicks on the activator itself (or its children). Do NOT treat clicks
        // inside the dropdown menu (anchors) as activator clicks — otherwise navigation is blocked.
        if (eventActivator && (eventActivator === tgt || eventActivator.contains(tgt))) {
          // Prevent the global outside-click handler from immediately closing it
          window._oc_ignoreNextDocClick = true;
          setTimeout(function () { window._oc_ignoreNextDocClick = false; }, 250);
          toggleDropdown(eventWrapper);
          e.preventDefault();
          e.stopPropagation();
          return;
        }
      }

      // If clicking the Admin/User label
      const adminLabel = document.getElementById('adminLabel');
      const userWrapper = document.getElementById('userDropdown');
      if (adminLabel && (adminLabel === tgt || adminLabel.contains(tgt))) {
        window._oc_ignoreNextDocClick = true;
        setTimeout(function () { window._oc_ignoreNextDocClick = false; }, 250);
        if (toggleDropdown(userWrapper)) {
          e.preventDefault();
          e.stopPropagation();
        }
        return;
      }
    } catch (err) {
      // swallow errors to avoid breaking other scripts
    }
  }, true);
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

// Debug: monitor when `notificationPanel` class/style changes or when its
// classList is mutated. Temporary instrumentation to diagnose why the panel
// jumps to bottom-right on Collections after open/close cycles.
try {
  if (!window._oc_debug_classlist_wrapped) {
    const _origAdd = DOMTokenList.prototype.add;
    const _origRemove = DOMTokenList.prototype.remove;
    DOMTokenList.prototype.add = function (...args) {
      const res = _origAdd.apply(this, args);
      try {
        const p = document.getElementById('notificationPanel');
        if (p && p.classList === this) {
          console.debug('OC: classList.add on notificationPanel ->', args, '\nSTACK:\n', new Error().stack);
        }
      } catch { }
      return res;
    };
    DOMTokenList.prototype.remove = function (...args) {
      const res = _origRemove.apply(this, args);
      try {
        const p = document.getElementById('notificationPanel');
        if (p && p.classList === this) {
          console.debug('OC: classList.remove on notificationPanel ->', args, '\nSTACK:\n', new Error().stack);
        }
      } catch { }
      return res;
    };
    window._oc_debug_classlist_wrapped = true;
  }
} catch { }

// Attach a MutationObserver to the panel once it exists (poll briefly).
try {
  (function attachPanelObserver() {
    let tries = 0;
    const t = setInterval(() => {
      try {
        const panel = document.getElementById('notificationPanel');
        if (!panel) {
          tries++;
          if (tries > 50) clearInterval(t);
          return;
        }
        clearInterval(t);
        try {
          const mo = new MutationObserver((mutations) => {
            mutations.forEach(m => {
              try {
                if (m.attributeName === 'class' || m.attributeName === 'style') {
                  console.debug('OC: panel mutation', m.attributeName, 'class=', panel.className, 'style=', panel.getAttribute('style'));
                }
              } catch { }
            });
          });
          mo.observe(panel, { attributes: true, attributeFilter: ['class', 'style'] });
        } catch { }
      } catch { clearInterval(t); }
    }, 100);
  })();
} catch { }

// Global client error logger to help diagnose runtime issues
try {
  if (!window._oc_error_bound) {
    window._oc_error_bound = true;
    window.addEventListener('error', async (e) => {
      try {
        const userId = parseInt(localStorage.getItem('userId') || '2', 10) || 2;
        await fetch('/api/log-client-error', {
          method: 'POST', headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ userId, description: 'Client JS error', stackTrace: String(e.error || e.message || e.filename) })
        });
      } catch { }
    });
    window.addEventListener('unhandledrejection', async (e) => {
      try {
        const userId = parseInt(localStorage.getItem('userId') || '2', 10) || 2;
        await fetch('/api/log-client-error', {
          method: 'POST', headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ userId, description: 'Unhandled promise rejection', stackTrace: String(e.reason) })
        });
      } catch { }
    });
  }
} catch { }

// -- Cart helpers (visibility + click) --
function _oc_isLoggedIn() {
  try { return localStorage.getItem('isLoggedIn') === 'true' || !!localStorage.getItem('userId'); } catch { return false; }
}
function _oc_getRoleId() {
  try { const raw = localStorage.getItem('roleId'); return raw ? parseInt(raw, 10) : 0; } catch { return 0; }
}
function _oc_updateCartVisibility() {
  const cartWrap = document.getElementById('hdrCart');
  if (!cartWrap) return;
  const logged = _oc_isLoggedIn();
  const roleId = _oc_getRoleId();
  const shouldShow = (!logged) || (logged && roleId === 5); // 5 = Customer
  cartWrap.classList.toggle('hidden', !shouldShow);
}

// Create notification UI (toast container, bell, panel) if it's not present in the page.
function ensureNotificationUI() {
  // Ensure shared styles for notification panel and items exist (colors match toast variants)
  try {
    const styleId = 'oc-notif-styles';
    if (!document.getElementById(styleId)) {
      const st = document.createElement('style');
      st.id = styleId;
      st.textContent = `
      
      .notification-panel{
      background:#fff;
      border:1px solid rgba(0,0,0,0.08);
      border-radius:12px;
      padding:8px;
      box-shadow:0 12px 24px rgba(0,0,0,0.14);
      color:#111;
      position:fixed !important; /* ensure it's rendered above page containers */
      width:480px !important; /* slightly wider so control buttons fit */
      max-width: calc(100vw - 32px) !important;
      /* no forced min-height: let height adapt to content; allow scrolling when very tall */
      max-height:60vh !important;
      display:flex;
      flex-direction:column;
      box-sizing: border-box;
      z-index: 1100 !important;
      overflow-x: hidden !important;
    }

/* ensure internal list never creates horizontal scroll */
#notificationList{display:flex;flex-direction:column;gap:8px;flex:1 1 auto;overflow-y:auto;word-wrap:break-word;overflow-x:hidden}

      .notif-card{display:grid;grid-template-columns:32px 1fr 24px;align-items:start;gap:12px;border-radius:8px;border:1px solid rgba(0,0,0,0.04);background:#fff;padding:10px 12px;position:relative}
      .notif-accent{width:32px;height:32px;border-radius:50%;display:flex;justify-content:center;align-items:center;color:#fff}
      .notif-accent .material-icons{font-size:18px;line-height:1}
      .notif-content{line-height:1.3;color:#111;overflow-wrap:break-word}
      .notification-close{justify-self:end;color:#666;cursor:pointer;border:none;background:transparent;font-size:16px;line-height:1}
      .notif--info .notif-accent{background:#2563eb}
      .notif--error .notif-accent{background:#ef4444}
      .notif--warning .notif-accent{background:#f59e0b}
      /* Panel filter buttons - softer pastel variants with left icons */
      /* Make information button pastel light-blue (not green) */
      .notif-info{background:rgba(191,219,254,0.22) !important;color:#0b5cff;border-color:rgba(191,219,254,0.28) !important}
      .notif-info .material-icons{color:#0b5cff;margin-right:6px}
      .notif-info:hover{background:rgba(191,219,254,0.28) !important}
      .notif-warning{background:rgba(250,204,21,0.12);color:#a16207;border-color:rgba(250,204,21,0.15);}
      .notif-warning .material-icons{color:#a16207;margin-right:6px}
      .notif-warning:hover{background:rgba(250,204,21,0.18)}
      .notif-error{background:rgba(239,68,68,0.08);color:#b91c1c;border-color:rgba(239,68,68,0.12);}
      .notif-error .material-icons{color:#b91c1c;margin-right:6px}
      .notif-error:hover{background:rgba(239,68,68,0.12)}
      /* Global grid pagination style: smaller, consistent buttons */
      .grid-pagination .btn, .grid-pagination .btn-sm, .grid-pagination .btn-xs { padding: 6px 8px; font-size: 0.85rem; }
      .grid-pagination .page-indicator { font-size: 0.9rem; color: #4b5563; }
      /* Make dropdown contents a bit wider and prevent horizontal scroll across all pages */
      .dropdown .dropdown-content, .dropdown-content.w-40, .dropdown-content.w-52, .dropdown-content.w-56, .dropdown-content.w-64 {
        /* Relax large forced width so dropdowns can adapt on narrow pages */
        min-width: 10rem !important;
        max-width: calc(100vw - 48px) !important;
        overflow-x: hidden !important;
        box-sizing: border-box;
        white-space: normal !important;
        word-break: break-word !important;
      }
      `;
      document.head.appendChild(st);
      // Ensure strong overrides for notification control visuals (pastel info button, aligned controls)
      try {
        if (!document.getElementById('oc-notif-override')) {
          const ov = document.createElement('style');
          ov.id = 'oc-notif-override';
          ov.textContent = `
            /* Normalize control button sizing and alignment inside notification panel */
            #notificationPanel .notification-controls .btn {
              position: relative !important;
              padding: 6px 12px !important;
              display: inline-flex !important;
              align-items: center !important;
              justify-content: center !important;
              gap: 8px !important;
              line-height: 1 !important;
              box-shadow: none !important;
              text-align: center !important;
              min-width: 92px !important;
              padding-left: 44px !important; /* make space for absolute icon */
            }
            /* Position icon absolutely so text can be perfectly centered */
            #notificationPanel .notification-controls .btn .material-icons {
              position: absolute !important;
              left: 12px !important;
              top: 50% !important;
              transform: translateY(-50%) !important;
              margin: 0 !important;
            }
            /* Pastel blue Information button */
            #notificationPanel .notification-controls .notif-info {
              background: rgba(191,219,254,0.22) !important;
              color: #0b5cff !important;
              border: 1px solid rgba(191,219,254,0.28) !important;
              box-shadow: none !important;
            }
            #notificationPanel .notification-controls .notif-info .material-icons {
              color: #0b5cff !important;
            }
            /* Prevent the info button from being colored by other theme rules */
            #notificationPanel .notification-controls .notif-info:hover { background: rgba(191,219,254,0.28) !important; }
          `;
          document.head.appendChild(ov);
        }
      } catch { }
    }
  } catch { }
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
  } catch { }

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
    // Use event listener and stop propagation so opening the panel
    // doesn't trigger a document-level click-away handler immediately.
    if (!bell._oc_bound_toggle) {
      bell._oc_bound_toggle = true;
      // Ensure bell is on top so overlays/selected rows don't block clicks
      try { bell.style.zIndex = '200000'; bell.style.pointerEvents = 'auto'; } catch { }
      // Use capture-phase pointerdown so we run before most grid handlers
      bell.addEventListener('pointerdown', (e) => {
        try { e.preventDefault(); e.stopPropagation(); if (e.stopImmediatePropagation) e.stopImmediatePropagation(); } catch { }
        try { window._oc_ignoreNextDocClick = true; } catch { }
        setTimeout(() => { try { window._oc_ignoreNextDocClick = false; } catch { } }, 300);
        const panel = document.getElementById('notificationPanel');
        if (!panel) return;
        try { if (window._notificationPanelOpen) { closeNotificationPanel(); return; } } catch { }
        openNotificationPanel();
      }, { capture: true });
      // Also keep a safe click-phase handler that stops propagation
      bell.addEventListener('click', (e) => { try { e.preventDefault(); e.stopPropagation(); if (e.stopImmediatePropagation) e.stopImmediatePropagation(); } catch { } });
    }

    // Bell icon
    const icon = document.createElement('span');
    icon.className = 'material-icons';
    icon.textContent = 'notifications_active';
    bell.appendChild(icon);

    // Badge showing notification count
    if (!document.getElementById('notificationCount')) {
      const badge = document.createElement('span');
      badge.id = 'notificationCount';
      badge.className = 'badge badge-error';
      // Initialize badge with current count and hidden state
      const initCount = (window.notifications || []).length;
      badge.textContent = initCount > 0 ? String(initCount) : '';
      const hide = initCount === 0;
      badge.classList.toggle('hidden', hide);
      badge.style.display = hide ? 'none' : '';
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
        // start hidden so the activator's first click shows the menu
        menu.classList.add('hidden');
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
      // Ensure export button is centered between user label and bell
      const exportBtnExisting = document.getElementById('exportBtn');
      if (exportBtnExisting && exportBtnExisting.parentElement !== rightContainer) {
        // Move export button into navbar right container
        rightContainer.appendChild(exportBtnExisting);
      }
      // Place elements: user dropdown -> export button -> bell (far right)
      if (exportBtnExisting) {
        if (userDd && userDd.parentElement === rightContainer) {
          rightContainer.insertBefore(exportBtnExisting, userDd.nextSibling);
        }
        // Append bell after export button
        if (exportBtnExisting.parentElement === rightContainer) {
          rightContainer.insertBefore(bell, exportBtnExisting.nextSibling);
        } else {
          rightContainer.appendChild(bell);
        }
      } else {
        // Fallback when export button is absent: keep bell after user dropdown
        rightContainer.appendChild(bell);
      }
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
        const exportBtn = document.getElementById('exportBtn');
        if (exportBtn && exportBtn.parentElement !== rightContainer) {
          rightContainer.appendChild(exportBtn);
        }
        if (userDd && userDd.parentElement === rightContainer) {
          if (exportBtn && exportBtn.parentElement === rightContainer) {
            // user -> export -> bell
            rightContainer.insertBefore(exportBtn, userDd.nextSibling);
            rightContainer.insertBefore(bell, exportBtn.nextSibling);
          } else {
            rightContainer.insertBefore(bell, userDd.nextSibling);
          }
        } else {
          if (exportBtn && exportBtn.parentElement === rightContainer) {
            rightContainer.insertBefore(bell, exportBtn.nextSibling);
          } else {
            rightContainer.appendChild(bell);
          }
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

    // --- CART: create button and place next to bell ---
    (function ensureCartButtonPlacement() {
      // If it already exists, just place/refresh it
      let cartWrap = document.getElementById('hdrCart');
      if (!cartWrap) {
        cartWrap = document.createElement('div');
        cartWrap.id = 'hdrCart';
        cartWrap.className = 'indicator';

        const cartBtn = document.createElement('button');
        cartBtn.id = 'btnCart';
        cartBtn.type = 'button';
        cartBtn.className = 'btn btn-ghost btn-circle';
        cartBtn.setAttribute('aria-label', 'Shopping cart');
        cartBtn.title = 'Shopping cart';
        cartBtn.innerHTML = '<span class="material-icons">shopping_cart</span>';

        const CART_URL = '/contracts';
        const LOGIN_URL = '/login?mode=login';
        const RETURN_URL = location.pathname + location.search;


        cartBtn.addEventListener('click', (e) => {
          e.preventDefault();

          const CART_URL = '/contracts';
          const LOGIN_URL = '/login?mode=login';
          const RETURN_URL = location.pathname + location.search;

          if (!_oc_isLoggedIn()) {
            try {
              sessionStorage.setItem('pendingToast', JSON.stringify({
                type: 'info',
                message: 'Please sign in to use your cart'
              }));
            } catch { }

            window.location.href = `${LOGIN_URL}&returnUrl=${encodeURIComponent(RETURN_URL)}`;
            return;
          }

          if (_oc_getRoleId() === 5) {
            window.location.href = CART_URL;
          } else {
            try { showToast?.('warning', 'Cart is available to Customers only'); } catch { }
          }
        });


        cartWrap.appendChild(cartBtn);
      }

      const rightContainer =
        document.querySelector('.navbar .flex-none') ||
        document.querySelector('.navbar .flex-none.items-center');
      const navbarRoot = document.querySelector('.navbar');
      const bellEl = document.getElementById('notificationBell');

      if (rightContainer) {
        if (bellEl && bellEl.parentElement === rightContainer) {
          rightContainer.insertBefore(cartWrap, bellEl.nextSibling);
        } else {
          rightContainer.appendChild(cartWrap);
        }
        cartWrap.classList.remove('fixed');
        cartWrap.style.position = 'relative';
        cartWrap.style.marginLeft = '8px';
      } else if (navbarRoot) {
        navbarRoot.appendChild(cartWrap);
        cartWrap.classList.remove('fixed');
        cartWrap.style.position = 'relative';
        cartWrap.style.marginLeft = '8px';
      } else {
        cartWrap.classList.add('fixed');
        cartWrap.style.position = 'fixed';
        cartWrap.style.top = '16px';
        cartWrap.style.right = '56px';
        cartWrap.style.zIndex = '99999';
        document.body.appendChild(cartWrap);
      }

      _oc_updateCartVisibility();
    })();

  } catch { }

  if (!document.getElementById('notificationPanel')) {
    const panel = document.createElement('div');
    panel.id = 'notificationPanel';
    panel.className = 'notification-panel hidden';

    // Defensive inline-hidden state: some pages may not include the
    // expected `.hidden { display:none }` rule early, so ensure the
    // panel is invisible until explicitly opened.
    try {
      panel.style.display = 'none';
      panel.style.visibility = 'hidden';
      panel.style.left = '';
      panel.style.right = '';
      panel.style.top = '';
      panel.style.bottom = 'auto';
    } catch { }

    const list = document.createElement('div');
    list.id = 'notificationList';
    panel.appendChild(list);

    // Controls row: Dismiss all (thin) + type filters (info/warning/error)
    const controls = document.createElement('div');
    controls.className = 'notification-controls';
    controls.style.display = 'flex';
    controls.style.gap = '8px';
    controls.style.marginTop = '8px';
    // keep all control buttons on one line
    controls.style.flexWrap = 'nowrap';
    controls.style.alignItems = 'center';
    controls.style.justifyContent = 'flex-start';

    const dismissBtn = document.createElement('button');
    dismissBtn.className = 'btn btn-xs';
    dismissBtn.textContent = 'Dismiss all';
    dismissBtn.onclick = dismissAll;
    dismissBtn.style.flex = '0 0 auto';
    controls.appendChild(dismissBtn);

    const infoBtn = document.createElement('button');
    infoBtn.className = 'btn btn-xs notif-info';
    infoBtn.setAttribute('aria-label', 'Filter information notifications');
    infoBtn.innerHTML = '<span class="material-icons">info</span>Information';
    infoBtn.onclick = function () {
      window._notificationFilterType = 'info';
      updateNotificationBell();
    };
    // Force pastel blue styling inline to override theme colors
    try {
      infoBtn.style.flex = '0 0 auto';
      infoBtn.style.backgroundColor = 'rgba(191,219,254,0.22)';
      infoBtn.style.color = '#0b5cff';
      infoBtn.style.border = '1px solid rgba(191,219,254,0.28)';
      infoBtn.style.boxShadow = 'none';
      infoBtn.style.padding = '6px 10px';
      const ico = infoBtn.querySelector('.material-icons');
      if (ico) ico.style.color = '#0b5cff';
    } catch { }
    controls.appendChild(infoBtn);

    const warnBtn = document.createElement('button');
    warnBtn.className = 'btn btn-xs notif-warning';
    warnBtn.setAttribute('aria-label', 'Filter warning notifications');
    warnBtn.innerHTML = '<span class="material-icons">warning</span>Warning';
    warnBtn.onclick = function () {
      window._notificationFilterType = 'warning';
      updateNotificationBell();
    };
    warnBtn.style.flex = '0 0 auto';
    controls.appendChild(warnBtn);

    const errorBtn = document.createElement('button');
    errorBtn.className = 'btn btn-xs notif-error';
    errorBtn.setAttribute('aria-label', 'Filter error notifications');
    errorBtn.innerHTML = '<span class="material-icons">error</span>Error';
    errorBtn.onclick = function () {
      window._notificationFilterType = 'error';
      updateNotificationBell();
    };
    errorBtn.style.flex = '0 0 auto';
    controls.appendChild(errorBtn);

    panel.appendChild(controls);

    // Force panel to be fixed to the viewport so it never scrolls with page containers
    // Use CSS priority to override any page styles that might create transformed ancestors
    try {
      panel.style.setProperty('position', 'fixed', 'important');
      panel.style.setProperty('z-index', '100000', 'important');
    } catch { /* ignore */ }
    // Ensure the panel is attached directly to the document body (viewport context)
    try { document.body.appendChild(panel); } catch { document.documentElement.appendChild(panel); }
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
  // Also sync on DOM ready to avoid stale badge visibility across pages
  try {
    window.addEventListener('DOMContentLoaded', () => {
      if (typeof updateNotificationBell === 'function') {
        updateNotificationBell();
      }
    });
  } catch { }

  // Prevent the Menu (eventlogDropdown) from being auto-closed by other document
  // click handlers: when the Menu is open, capture outside clicks and stop
  // other listeners from running so the Menu only closes when its activator
  // is clicked again.
  try {
    // Close `#eventlogDropdown` when clicking outside of it.
    document.addEventListener('click', (e) => {
      try {
        const dd = document.getElementById('eventlogDropdown');
        if (!dd) return;
        if (!dd.classList.contains('dropdown-open')) return;
        // If the click is inside the dropdown or on the activator, allow it
        if (dd.contains(e.target)) return;
        const activator = dd.querySelector('[role="button"]') || dd.querySelector('#adminLabel');
        if (activator && activator.contains(e.target)) return;
        // Click outside: close the dropdown
        try {
          dd.classList.remove('dropdown-open');
          const content = dd.querySelector('.dropdown-content');
          if (content) content.classList.add('hidden');
        } catch { }
      } catch { }
    }, true); // use capture phase so we run before bubble-phase closers
  } catch { }

  // Ensure admin change-store modal HTML exists
  ensureAdminChangeStoreModal();

  // Navbar auth controls: show Log Out when logged in; redirect to /login on logout
  try {
    // Read auth state from the server (cookie-based) and avoid client-controlled flags
    const syncAuthFromServer = async () => {
      try {
        const res = await fetch('/whoami', { method: 'GET', credentials: 'include', headers: { 'Accept': 'application/json' } });
        if (res.status === 401) {
          return { isAuthenticated: false, code: '', roleId: 0 };
        }
        const data = await res.json();
        const isAuth = !!data?.isAuthenticated;
        const claims = Array.isArray(data?.claims) ? data.claims : [];
        const claimVal = (t) => {
          const c = claims.find(x => String(x.Type).toLowerCase().includes(String(t).toLowerCase()));
          return c ? String(c.Value) : null;
        };
        const uid = claimVal('nameidentifier') || claimVal('ClaimTypes.NameIdentifier');
        // Prefer server-provided roleId; fallback to parsing claims
        const serverRoleId = data?.roleId ?? 0;
        const roleFromServer = Number(serverRoleId) || 0;
        const roleFromClaims = parseInt((claimVal('role') || claimVal('ClaimTypes.Role') || '0'), 10) || 0;
        const role = roleFromServer || roleFromClaims || 0;
        const code = data?.name || claimVal('name') || claimVal('ClaimTypes.Name') || '';
        return { isAuthenticated: isAuth, userId: uid ? parseInt(uid, 2) : 2, roleId: role, code };
      } catch { return { isAuthenticated: false, code: '', roleId: 0 }; }
    };

    const updateNavbarAuth = (auth) => {
      const isLoggedIn = !!auth?.isAuthenticated;
      const roleIdRaw = auth?.roleId ?? 0;
      const roleId = parseInt(String(roleIdRaw), 10) || 0;
      // Privileged roles: Manager=8, Administrator=7 (per ax_user.role_id)
      const isPrivileged = isLoggedIn && (roleId === 7 || roleId === 8);
      const isCustomer = isLoggedIn && roleId === 5;
      const canManageProducts = isLoggedIn && (roleId === 6 || roleId === 7 || roleId === 8);

      const pathNow = (window.location && window.location.pathname || '').toLowerCase();
      const isEventLogPage = pathNow.includes('/eventlog');

      // --- Cart visibility rule: show for guests, or for logged-in Customer (roleId=5) ---
      try { _oc_updateCartVisibility(); } catch { }

      // Navbar links:
      // - Users is privileged-only
      // - EventLog link is used as the Menu anchor; keep it visible for any logged-in user
      const userLinks = Array.from(document.querySelectorAll('.navbar a'))
        .filter(a => (a.getAttribute('href') || '').toLowerCase().includes('/users'));
      userLinks.forEach(a => a.classList.toggle('hidden', !isPrivileged));

      const signIn = document.getElementById('navSignIn');
      const logIn = document.getElementById('navLogIn');
      const logOut = document.getElementById('navLogOut');
      if (signIn) signIn.classList.toggle('hidden', isLoggedIn);
      if (logIn) logIn.classList.toggle('hidden', isLoggedIn);
      if (logOut) logOut.classList.toggle('hidden', !isLoggedIn);

      // Hide the Menu anchor (EventLog link) unless logged in
      const eventLogLinks = Array.from(document.querySelectorAll('.navbar a'))
        .filter(a => (a.getAttribute('href') || '').toLowerCase().includes('/eventlog'));
      eventLogLinks.forEach(a => a.classList.toggle('hidden', !isLoggedIn));

      // Export button rules:
      // - EventLog export is privileged-only
      // - Contracts export is available to any authenticated user
      const exportBtn = document.getElementById('exportBtn');
      if (exportBtn) {
        const canSeeExport = isEventLogPage ? isPrivileged : (isLoggedIn && !isCustomer);
        exportBtn.classList.toggle('hidden', !canSeeExport);
      }

      // Ensure Admin dropdown menu exists and is visible only for privileged users
      const rightContainer = document.querySelector('.navbar .flex-none') || document.querySelector('.navbar .flex-none.items-center');

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
        // start hidden so the activator's first click shows the menu
        menu.classList.add('hidden');
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
          } catch { }
        };
        // Bind focus/click to adjust position just-in-time
        const activatorEl = document.getElementById('adminLabel');
        if (activatorEl && !activatorEl._oc_bound_pos) {
          activatorEl._oc_bound_pos = true;
          ['click', 'focus', 'mouseenter'].forEach(evt => {
            activatorEl.addEventListener(evt, () => setTimeout(positionUserDropdown, 0));
          });
        }
      }

      const adminLabel = document.getElementById('adminLabel');
      const userDropdown = document.getElementById('userDropdown');
      if (adminLabel) {
        const code = (auth?.code || '').trim();
        // Show ax_user.code clearly inside button with icon and dropdown hint
        adminLabel.innerHTML = isLoggedIn && code
          ? `<span class="material-icons" style="font-size:18px;color:#4b5563;">person</span><span class="font-semibold" style="color:#111827;">${code}</span><span class="material-icons" style="font-size:18px;color:#6b7280;">expand_more</span>`
          : '';
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
        ddLogout.addEventListener('click', async (e) => {
          e.preventDefault();
          // Perform logout: notify server, clear client state and redirect.
          try {
            try { await fetch('/api/logout', { method: 'POST', credentials: 'include', headers: { 'Accept': 'application/json' } }); } catch { }
            try {
              const currentKey = _oc_getNotificationKey();
              try { localStorage.removeItem(currentKey); } catch { }
              window.notifications = [];
              try { updateNotificationBell(); } catch { }
              localStorage.removeItem('isLoggedIn');
              localStorage.removeItem('userId');
              localStorage.removeItem('lastActivityTs');
              localStorage.removeItem('roleId');
              localStorage.removeItem('code');
              localStorage.removeItem('username');
              localStorage.removeItem('email');
            } catch { }
            try { updateNavbarAuth({ isAuthenticated: false, roleId: 0, code: '' }); } catch { }
            window.location.href = '/login?mode=login';
          } catch {
            try { window.location.href = '/login?mode=login'; } catch { }
          }
        });
      }

        // Ensure admin label toggles its dropdown menu on click
        try {
          if (adminLabel && !adminLabel._oc_bound_toggle) {
            adminLabel._oc_bound_toggle = true;
            adminLabel.addEventListener('click', (e) => {
              try { e.preventDefault(); e.stopPropagation(); if (e.stopImmediatePropagation) e.stopImmediatePropagation(); } catch { }
              try { window._oc_ignoreNextDocClick = true; } catch { }
              setTimeout(() => { try { window._oc_ignoreNextDocClick = false; } catch { } }, 300);
              const dd = document.getElementById('userDropdown');
              if (!dd) return;
              dd.classList.toggle('dropdown-open');
              const menu = dd.querySelector('.dropdown-content');
              if (menu) menu.classList.toggle('hidden');
            });
          }
        } catch { }

        // Close the user dropdown when clicking outside of it
        try {
          if (!window._oc_bound_user_dropdown_doc) {
            window._oc_bound_user_dropdown_doc = true;
            document.addEventListener('click', (e) => {
              try {
                if (window._oc_ignoreNextDocClick) return;
                const dd = document.getElementById('userDropdown');
                if (!dd) return;
                if (!dd.classList.contains('dropdown-open')) return;
                const tgt = e.target || e.srcElement;
                if (dd.contains(tgt)) return; // click inside dropdown — ignore
                // otherwise close dropdown
                dd.classList.remove('dropdown-open');
                const menu = dd.querySelector('.dropdown-content');
                if (menu) menu.classList.add('hidden');
              } catch { }
            }, true);
          }
        } catch { }

      // >>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>
      // CALL to hide duplicate "Log Out" after dropdown is ready
      hideStandaloneLogout();
      // <<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<

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
            // start hidden so the activator's first click shows the menu
            menu.classList.add('hidden');
              menu.innerHTML = `
              <li><a href="/contracts" id="ddContracts"><span class="material-icons mr-2">receipt</span>Contracts</a></li>
              <li><a href="/products" id="ddProducts"><span class="material-icons mr-2">inventory_2</span>Products</a></li>
              <li><a href="/notes" id="ddNotes"><span class="material-icons mr-2">note</span>Notes</a></li>
              <li><a href="/changestore" id="ddChangeStore"><span class="material-icons mr-2">store</span>Store Details</a></li>
              <li><a href="/users" id="ddUsersTeams"><span class="material-icons mr-2">group</span>Users &amp; Teams</a></li>
              <li><a href="/eventlog" id="ddEventLog"><span class="material-icons mr-2">list</span>All Events</a></li>
            `;
            // Insert wrapper at the far left (as first interactive element)
            const parent = eventLogLink.parentElement;
            // Remove original link to avoid duplication
            parent.removeChild(eventLogLink);
            // Prepend wrapper so it anchors left when visible
            if (parent.firstChild) parent.insertBefore(wrapper, parent.firstChild); else parent.appendChild(wrapper);
            wrapper.appendChild(activator);
            wrapper.appendChild(menu);

            // Toggle dropdown on activator click (open on first click, close on second)
            if (!activator._oc_bound_toggle) {
              activator._oc_bound_toggle = true;
              // Use capture-phase pointerdown to open the menu and stop propagation
              // so other global handlers (pointerdown/click) won't immediately close it.
              if (!activator._oc_bound_pointer) {
                activator._oc_bound_pointer = true;
                activator.addEventListener('click', (e) => {
                  try { e.preventDefault(); e.stopPropagation(); if (e.stopImmediatePropagation) e.stopImmediatePropagation(); } catch { }
                  // Short ignore guard so document-level click handlers don't
                  // treat this opening click as a click-away.
                  try { window._oc_ignoreNextDocClick = true; } catch { }
                  setTimeout(() => { try { window._oc_ignoreNextDocClick = false; } catch { } }, 300);

                  const w = document.getElementById('eventlogDropdown');
                  if (!w) return;
                  w.classList.toggle('dropdown-open');
                  const content = w.querySelector('.dropdown-content');
                  if (content) content.classList.toggle('hidden');
                });
              }
            }

            // Change Store link navigates to /changestore (page implements grid + Open modal)
            // Users & Teams opens dedicated page
            const ddUsersTeams = menu.querySelector('#ddUsersTeams');
            if (ddUsersTeams && !ddUsersTeams._oc_bound) {
              ddUsersTeams._oc_bound = true;
              ddUsersTeams.addEventListener('click', (e) => {
                // default navigation behavior
              });
            }

            const ddContracts = menu.querySelector('#ddContracts');
            if (ddContracts && !ddContracts._oc_bound) {
              ddContracts._oc_bound = true;
              ddContracts.addEventListener('click', () => {
                // default navigation to /contracts (rewrite -> contracts.html)
              });
            }
          }

          // Toggle dropdown visibility: show Menu only when authenticated.
          // Privileged-only entries stay hidden for non-privileged users.
          const dd = document.getElementById('eventlogDropdown');
          if (dd) dd.classList.toggle('hidden', !isLoggedIn);

          try {
            const ddChangeStore = document.getElementById('ddChangeStore');
            const ddUsersTeams = document.getElementById('ddUsersTeams');
            const ddEventLog = document.getElementById('ddEventLog');
            const ddContracts = document.getElementById('ddContracts');
            const ddNotes = document.getElementById('ddNotes');
            const ddProducts = document.getElementById('ddProducts');
            if (ddChangeStore) ddChangeStore.classList.toggle('hidden', !isPrivileged);
            if (ddUsersTeams) ddUsersTeams.classList.toggle('hidden', !isPrivileged);
            if (ddEventLog) ddEventLog.classList.toggle('hidden', !isPrivileged);
            // Contracts and Notes should be hidden for ordinary Customers (roleId = 5)
            if (ddContracts) ddContracts.classList.toggle('hidden', (!isLoggedIn) || isCustomer);
            if (ddNotes) ddNotes.classList.toggle('hidden', (!isLoggedIn) || isCustomer);
            if (ddProducts) ddProducts.classList.toggle('hidden', !canManageProducts);
          } catch { }
        }
      }
    };

    try { window.updateNavbarAuth = updateNavbarAuth; } catch {}
    try { window.syncAuthFromServer = syncAuthFromServer; } catch {}

    // First, align with server cookie; then update the navbar view without touching localStorage
    syncAuthFromServer().then((auth) => {
      updateNavbarAuth(auth);
      try { gateProtectedPages(auth); } catch { }
    });

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
        // Also notify server to clear auth cookie
        try { fetch('/api/logout', { method: 'POST', credentials: 'include', headers: { 'Accept': 'application/json' } }); } catch { }
        try {
          // Clear current user's notification store on logout
          const currentKey = _oc_getNotificationKey();
          try { localStorage.removeItem(currentKey); } catch { }
          window.notifications = [];
          updateNotificationBell();

          localStorage.removeItem('isLoggedIn');
          localStorage.removeItem('userId');
          localStorage.removeItem('lastActivityTs');
          localStorage.removeItem('roleId');
          localStorage.removeItem('code');
          localStorage.removeItem('username');
          localStorage.removeItem('email');
        } catch { }
        try { updateNavbarAuth({ isAuthenticated: false, roleId: 0, code: '' }); } catch { }
        // redirect to login page
        window.location.href = '/login?mode=login';
      });
    }
  } catch { }

  // Inactivity logout: auto log out after exactly 30 minutes of no user activity
  try {
    const INACTIVITY_MS = 30 * 60 * 1000; // 30 minutes

    // Shared logout helper: call server logout, clear client state, optionally set pending toast, then redirect
    async function _oc_performLogout(showPendingToast = true) {
      try {
        if (window._oc_isLoggingOut) return; // prevent re-entrancy
        window._oc_isLoggingOut = true;
        // Notify server to clear auth cookie/session
        try { await fetch('/api/logout', { method: 'POST', credentials: 'include', headers: { 'Accept': 'application/json' } }); } catch { }

        // Optionally set a pending toast message to show after redirect
        if (showPendingToast) {
          try {
            sessionStorage.setItem('pendingToast', JSON.stringify({ type: 'warning', message: 'You have been logged out due to 30 minutes of inactivity. Please log in again to continue.' }));
          } catch { }
        }

        // Clear client-side auth and notification state
        try {
          const currentKey = _oc_getNotificationKey();
          try { localStorage.removeItem(currentKey); } catch { }
          window.notifications = [];
          try { updateNotificationBell(); } catch { }
          localStorage.removeItem('isLoggedIn');
          localStorage.removeItem('userId');
          localStorage.removeItem('lastActivityTs');
          localStorage.removeItem('roleId');
          localStorage.removeItem('code');
          localStorage.removeItem('username');
          localStorage.removeItem('email');
        } catch { }

        try { updateNavbarAuth({ isAuthenticated: false, roleId: 0, code: '' }); } catch { }
        // Redirect to login page
        try { window.location.href = '/login?mode=login'; } catch { }
      } finally {
        window._oc_isLoggingOut = false;
      }
    }

    const markActivity = () => {
      try {
        localStorage.setItem('lastActivityTs', Date.now().toString());
      } catch { }
    };

    // Initialize last activity when script loads
    markActivity();

    // Listen for common user interactions to update activity timestamp
    ['click', 'mousemove', 'keydown', 'scroll', 'touchstart'].forEach(evt => {
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
            // Auto-logout: call server and clear client state, then redirect
            try {
              _oc_performLogout(true);
            } catch { try { window.location.href = '/login?mode=login'; } catch {} }
          }
        } catch { }
      }, 15000); // check every 15s
    }
  } catch { }

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
  } catch { }
}

// Gate protected pages and show an inline Access Denied panel instead of redirecting
function gateProtectedPages(auth) {
  const path = (window.location && window.location.pathname || '').toLowerCase();
  const protectedAny = ['/users', '/users.html', '/eventlog', '/eventlog.html', '/changestore', '/changestore.html', '/contracts', '/contracts.html'];
  if (!protectedAny.includes(path)) return;

  const isAuth = !!auth?.isAuthenticated;
  const roleIdRaw = auth?.roleId ?? 0;
  const roleId = parseInt(String(roleIdRaw), 10) || 0;
  const isPrivileged = isAuth && (roleId === 7 || roleId === 8);
  const isCustomer = isAuth && roleId === 5;

  // EventLog and Change Store require privileged roles; Users page requires authentication only
  const requiresPrivilege = (p) =>
    ['/eventlog', '/eventlog.html', '/changestore', '/changestore.html', '/users', '/users.html'].includes(p);

  const mustBePrivileged = requiresPrivilege(path);

  const isContractsPage = path === '/contracts' || path === '/contracts.html';
  const denyContracts = isContractsPage && (!isAuth);

  const deny = denyContracts || (!isAuth) || (mustBePrivileged && !isPrivileged);
  if (!deny) return;

  // Hide known main content containers
  const hideEl = (sel) => { const el = document.querySelector(sel); if (el) el.classList.add('hidden'); };
  hideEl('main');
  hideEl('#eventlogContent');
  hideEl('#eventlogGrid');
  hideEl('#loadingOverlay');

  // Show inline Access Denied panel
  const container = document.createElement('section');
  container.className = 'container mx-auto p-6';
  container.innerHTML = `
    <div class="bg-base-100 rounded-xl shadow p-8 text-center">
      <div class="flex items-center justify-center gap-2 mb-3">
        <span class="material-icons text-rose-600">block</span>
        <h2 class="text-2xl font-bold">Access Denied</h2>
      </div>
      <p class="text-gray-700">You are not allowed to see this page.</p>
    </div>
  `;
  document.body.appendChild(container);
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
  } catch { }
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
  // Redesigned, accessible toast with left accent and icon
  return new Promise((resolve) => {
    const ensureToastContainer = () => {
      let el = document.getElementById('toastContainer');
      if (!el) {
        el = document.createElement('div');
        el.id = 'toastContainer';
        el.style.position = 'fixed';
        el.style.top = '16px';
        el.style.right = '16px';
        el.style.zIndex = '9999';
        el.style.display = 'flex';
        el.style.flexDirection = 'column';
        el.style.gap = '12px';
        document.body.appendChild(el);
      }
      return el;
    };

    // Inject minimal styles for toast variants if not present
    (function ensureToastStyles() {
      const styleId = 'oc-toast-styles';
      if (document.getElementById(styleId)) return;
      const st = document.createElement('style');
      st.id = styleId;
      st.textContent = `
        .oc-toast{display:flex;align-items:center;gap:12px;max-width:640px;padding:12px 16px;border-radius:10px;background:#fff;box-shadow:0 2px 10px rgba(0,0,0,0.08);border:1px solid rgba(0,0,0,0.06)}
        .oc-toast__accent{flex:0 0 28px;width:28px;height:28px;border-radius:50%;display:flex;justify-content:center;align-items:center;color:#fff}
        .oc-toast__accent .material-icons{font-size:18px;line-height:1}
        .oc-toast__content{flex:1 1 auto;line-height:1.4;color:#111}
        .oc-toast--info .oc-toast__accent{background:#2563eb}
        .oc-toast--error .oc-toast__accent{background:#ef4444}
        .oc-toast--warning .oc-toast__accent{background:#f59e0b}
        #notif-badge.is-hidden{display:none}
      `;
      document.head.appendChild(st);
    })();

    const container = ensureToastContainer();
    const t = (type || 'info').toLowerCase();

    const toast = document.createElement('div');
    toast.className = `oc-toast oc-toast--${t}`;
    // Allow interaction with the toast even if the container ignores pointer events
    toast.style.pointerEvents = 'auto';
    const isInfo = t === 'info';
    toast.setAttribute('role', isInfo ? 'status' : 'alert');
    toast.setAttribute('aria-live', isInfo ? 'polite' : 'assertive');

    const accent = document.createElement('div');
    accent.className = 'oc-toast__accent';
    accent.setAttribute('aria-hidden', 'true');
    accent.innerHTML = ({
      info: '<span class="material-icons">info</span>',
      error: '<span class="material-icons">error</span>',
      warning: '<span class="material-icons">warning</span>'
    }[t]) || '<span class="material-icons">info</span>';

    const content = document.createElement('div');
    content.className = 'oc-toast__content';
    content.textContent = message || '';

    // Dismiss by clicking anywhere on the toast (no visible X)
    toast.addEventListener('click', () => {
      if (container.contains(toast)) container.removeChild(toast);
      storeNotification();
    });

    toast.appendChild(accent);
    toast.appendChild(content);
    container.insertBefore(toast, container.firstChild);

    let stored = false;
    const storeNotification = () => {
      if (stored) return;
      stored = true;
      // Add newest notifications to the front so the bell panel shows newest first
      try { window.notifications.unshift({ type: t, message }); } catch { window.notifications.push({ type: t, message }); }
      updateNotificationBell();
      resolve();
    };

    const autoDismissMs = 10000;
    const timeout = setTimeout(() => {
      if (container.contains(toast)) container.removeChild(toast);
      storeNotification();
    }, autoDismissMs);

    window.addEventListener('beforeunload', () => {
      clearTimeout(timeout);
      storeNotification();
    });
  });
}

function updateNotificationBell(persist = true) {
  const count = (window.notifications || []).length;
  const badge = document.getElementById('notificationCount');
  const bell = document.getElementById('notificationBell');
  if (badge) {
    badge.textContent = count > 0 ? String(count) : '';
    // Hide badge when count is 0; show otherwise
    const hide = count === 0;
    badge.classList.toggle('hidden', hide);
    // Force-hide via inline style to defeat any conflicting CSS
    badge.style.display = hide ? 'none' : '';
    // Also support optional #notif-badge selector
    const nb = document.getElementById('notif-badge');
    if (nb) {
      nb.textContent = count > 0 ? String(count) : '';
      nb.classList.toggle('is-hidden', hide);
      nb.setAttribute('aria-hidden', hide ? 'true' : 'false');
      nb.style.display = hide ? 'none' : '';
    }
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
    .forEach((n) => {
      const el = document.createElement('div');
      const t = (n.type || 'info').toLowerCase();
      el.className = `notif-card notif--${t}`;

      const accent = document.createElement('div');
      accent.className = 'notif-accent';
      accent.setAttribute('aria-hidden', 'true');
      accent.innerHTML = ({
        info: '<span class="material-icons">info</span>',
        error: '<span class="material-icons">error</span>',
        warning: '<span class="material-icons">warning</span>'
      }[t]) || '<span class="material-icons">info</span>';

      const content = document.createElement('div');
      content.className = 'notif-content';
      content.textContent = n.message || '';

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

      el.appendChild(accent);
      el.appendChild(content);
      el.appendChild(closeBtn);
      list.appendChild(el);
    });

  // Persist to localStorage using per-user or guest scope
  if (persist) _oc_saveNotifications();

  // ensure bell UI exists and adjust position after rendering notifications
  adjustBellPosition();

  // If panel is currently open, keep it anchored at the cached position
  const panel = document.getElementById('notificationPanel');
  if (panel && !panel.classList.contains('hidden') && window._notificationPanelPos) {
    // Prefer anchoring by right/top when available (we anchor to bell by right edge)
    if (window._notificationPanelPos.right !== undefined) {
      panel.style.left = '';
      panel.style.right = `${window._notificationPanelPos.right}px`;
    }
    if (window._notificationPanelPos.top !== undefined) panel.style.top = `${window._notificationPanelPos.top}px`;
  }
}

function _oc_syncNotificationsFromStorage() {
  try {
    const key = _oc_getNotificationKey();
    const raw = localStorage.getItem(key);
    const parsed = raw ? JSON.parse(raw) : [];
    window.notifications = Array.isArray(parsed) ? parsed : [];
    updateNotificationBell(false);
  } catch {
    window.notifications = window.notifications || [];
    updateNotificationBell(false);
  }
}

// Keep notifications in sync across tabs for the same logged-in user.
try {
  if (!window._oc_notif_sync_bound) {
    window._oc_notif_sync_bound = true;
    window.addEventListener('storage', (e) => {
      try {
        if (!e) return;
        const key = _oc_getNotificationKey();
        if (e.key !== key) return;
        _oc_syncNotificationsFromStorage();
      } catch { }
    });
  }
} catch { }

// --- ADD start: hide duplicate "Log Out" buttons when dropdown exists ---
function hideStandaloneLogout() {
  try {
    const hasDropdown = !!document.getElementById('userDropdown');
    if (!hasDropdown) return;

    // 1) If an explicit #navLogOut exists anywhere in the layout, hide it
    const explicit = document.getElementById('navLogOut');
    if (explicit) explicit.classList.add('hidden');

    // 2) If another "Log Out" link/button exists in the navbar (without an ID), hide it
    const lone = Array.from(document.querySelectorAll('.navbar a, .navbar button'))
      .find(el =>
        el.id !== 'ddLogout' &&
        el.id !== 'adminLabel' &&
        el.textContent &&
        el.textContent.trim().toLowerCase() === 'log out'
      );
    if (lone) lone.classList.add('hidden');
  } catch { }
}
// --- ADD end ---

// Global helper to update badge by explicit count
function updateNotificationBadge(count) {
  const n = Number(count || 0);
  const nb = document.getElementById('notif-badge');
  const badge = document.getElementById('notificationCount');
  if (nb) {
    nb.textContent = n > 0 ? String(n) : '';
    nb.classList.toggle('is-hidden', n === 0);
    nb.setAttribute('aria-hidden', n === 0 ? 'true' : 'false');
  }
  if (badge) {
    badge.textContent = n > 0 ? String(n) : '';
    badge.classList.toggle('hidden', n === 0);
  }
}

// Export helpers
window.showToast = showToast;
window.updateNotificationBadge = updateNotificationBadge;
window.attachTableSort = attachTableSort;

function dismissAll() {
  window.notifications = [];
  updateNotificationBell();
}

// Simple client-side table sort for current page rows (ascending/descending per column)
function attachTableSort(selector) {
  try {
    const table = typeof selector === 'string' ? document.querySelector(selector) : selector;
    if (!table || table._oc_sort_bound) return;
    table._oc_sort_bound = true;
    const thead = table.querySelector('thead');
    const tbody = table.querySelector('tbody');
    if (!thead || !tbody) return;
    const ths = Array.from(thead.querySelectorAll('th'));
    const clearIndicators = () => ths.forEach(th => {
      th.removeAttribute('data-sort');
      const ind = th.querySelector('.oc-sort-ind');
      if (ind) ind.remove();
    });
    const addIndicator = (th, dir) => {
      let ind = th.querySelector('.oc-sort-ind');
      if (!ind) { ind = document.createElement('span'); ind.className = 'oc-sort-ind material-icons ml-1 text-sm align-middle'; th.appendChild(ind); }
      ind.textContent = dir === 'asc' ? 'arrow_upward' : 'arrow_downward';
    };
    ths.forEach((th, colIdx) => {
      th.style.cursor = 'pointer';
      th.addEventListener('click', () => {
        const current = th.getAttribute('data-sort') || '';
        const dir = current === 'asc' ? 'desc' : 'asc';
        clearIndicators();
        th.setAttribute('data-sort', dir);
        addIndicator(th, dir);
        const rows = Array.from(tbody.querySelectorAll('tr'));
        const parseVal = (td) => {
          const text = (td?.textContent || '').trim();
          const num = parseFloat(text.replace(/[^0-9.+-]/g, ''));
          return isNaN(num) ? text.toLowerCase() : num;
        };
        rows.sort((a, b) => {
          const av = parseVal(a.children[colIdx]);
          const bv = parseVal(b.children[colIdx]);
          let cmp = 0;
          if (typeof av === 'number' && typeof bv === 'number') cmp = av - bv; else cmp = String(av).localeCompare(String(bv));
          return dir === 'asc' ? cmp : -cmp;
        });
        // Re-append sorted rows
        rows.forEach(r => tbody.appendChild(r));
      });
    });
  } catch { }
}

function closeNotificationPanel() {
  const panel = document.getElementById('notificationPanel');
  if (!panel) return;
  try { console.debug('OC: closeNotificationPanel start', { class: panel.className, style: panel.getAttribute && panel.getAttribute('style') }); } catch {}
  panel.classList.add('hidden');
  // Strongly enforce hidden state to avoid page CSS/scripts moving the panel
  try { panel.style.display = 'none'; } catch {}
  try { panel.style.visibility = 'hidden'; } catch {}
  try { panel.style.left = ''; panel.style.right = ''; panel.style.top = ''; panel.style.bottom = 'auto'; } catch {}
  try { panel.style.width = ''; } catch {}
  try {
    // Remove any capture-phase outside handler if present
    if (window._notificationOutsideHandler) {
      try { document.removeEventListener('pointerdown', window._notificationOutsideHandler, true); } catch { }
      try { document.removeEventListener('click', window._notificationOutsideHandler, false); } catch { }
      window._notificationOutsideHandler = null;
    }
  } catch { }
  panel.style.left = '';
  panel.style.top = '';
  panel.style.width = '';
  panel.style.display = 'none';
  panel.style.visibility = 'hidden';
  window._notificationPanelPos = null;
  // Reset notification filter on close so all messages are shown next time
  window._notificationFilterType = null;
  updateNotificationBell();
  if (window._notificationOutsideHandler) {
    document.removeEventListener('click', window._notificationOutsideHandler);
    window._notificationOutsideHandler = null;
  }
  try {
    if (window._oc_notificationReposition) {
      window.removeEventListener('scroll', window._oc_notificationReposition, { passive: true });
      window.removeEventListener('resize', window._oc_notificationReposition);
      window._oc_notificationReposition = null;
    }
  } catch { }
    try { window._notificationPanelOpen = false; } catch { }

  // Temporary guard: reapply hidden inline styles a few times in case other
  // scripts race and try to re-show/move the panel. This is defensive and
  // kept short to avoid loops.
  try {
    let _tries = 0;
    const _ti = setInterval(() => {
      try {
        const p = document.getElementById('notificationPanel');
        if (!p || !p.classList.contains('hidden')) {
          _tries++;
          if (_tries > 8) clearInterval(_ti);
          return;
        }
        p.style.display = 'none';
        p.style.visibility = 'hidden';
        p.style.left = '';
        p.style.right = '';
        p.style.top = '';
        p.style.bottom = 'auto';
        p.style.width = '';
        _tries++;
        if (_tries > 8) clearInterval(_ti);
      } catch { clearInterval(_ti); }
    }, 80);
  } catch { }
}

function openNotificationPanel() {
  const panel = document.getElementById('notificationPanel');
  const bell = document.getElementById('notificationBell');
  if (!panel || !bell) return;
  try { console.debug('OC: openNotificationPanel start', { panelClass: panel.className }); } catch {}
  // Ensure panel is a direct child of document.body so fixed positioning
  // is relative to the viewport and not a transformed ancestor.
  try { if (panel.parentElement !== document.body) document.body.appendChild(panel); } catch { }
  // prevent re-entrancy: if already open, do nothing
  try { if (window._notificationPanelOpen) return; } catch { }
  updateNotificationBell();
  // Show the panel invisibly to measure its size (use fixed positioning)
  panel.classList.remove('hidden');
  // Re-assert fixed positioning to guard against page CSS that forces different behavior
  try { panel.style.setProperty('position', 'fixed', 'important'); panel.style.setProperty('z-index', '100000', 'important'); } catch { }
  panel.style.visibility = 'hidden';
  panel.style.display = 'block';
  const rect = bell.getBoundingClientRect();
  const desiredWidth = 480; // base target width (matches CSS min-width)
  // Allow the panel to shrink to fit viewport when necessary (keep 24px margin both sides)
  const maxAllowed = Math.max(200, window.innerWidth - 48);
  let panelWidth = Math.max(panel.offsetWidth || 0, desiredWidth);
  if (panelWidth > maxAllowed) panelWidth = maxAllowed;
  // Anchor panel's right edge to bell's right edge so it appears directly below the bell.
  // Special-case: some pages (Collections) have layout rules that affect bounding rects;
  // when on /collections, anchor relative to the navbar right edge to avoid jumping.
  let desiredRight;
  let top;
  try {
    const path = (location && location.pathname || '').toLowerCase();
    if (path.includes('/collections')) {
      const navbar = document.querySelector('.navbar');
      const nrect = navbar ? navbar.getBoundingClientRect() : rect;
      top = (nrect.bottom || rect.bottom) + 8;
      // anchor to the navbar right edge so panel aligns with header controls
      try {
        const navbarRight = nrect.right || rect.right;
        desiredRight = Math.max(8, Math.round(window.innerWidth - navbarRight + 8));
      } catch { desiredRight = Math.max(8, Math.round(window.innerWidth - rect.right + 8)); }
    } else {
      desiredRight = Math.max(8, Math.round(window.innerWidth - rect.right + 8));
      top = rect.bottom + 8; // viewport coordinates for fixed positioning
    }
  } catch { desiredRight = Math.max(8, Math.round(window.innerWidth - rect.right + 8)); top = rect.bottom + 8; }
  // Apply right anchoring and clear left to avoid conflicting layout rules
  panel.style.left = '';
  panel.style.right = `${desiredRight}px`;
  panel.style.top = `${top}px`;
  // Lock width while open so content changes (e.g., dismiss all) don't reflow and shift alignment
  // Apply inline width to win over page-specific CSS rules
  panel.style.width = `${panelWidth}px`;
  panel.style.visibility = '';
  panel.style.display = '';
  panel.style.bottom = 'auto';

  // Ensure toast container is aligned with the navbar as well
  try { adjustToastContainer(); } catch { }

  // Cache position while open to prevent jumps during content changes
  // Store right/top (we anchor panel by right to the bell)
  window._notificationPanelPos = { right: desiredRight, top };

  try { window._notificationPanelOpen = true; } catch { }

  // Add click-away handler after current event loop to avoid immediate close
  // Short guard: ignore any document clicks that occur immediately after opening.
  try { window._oc_ignoreNextDocClick = true; } catch { window._oc_ignoreNextDocClick = false; }
  setTimeout(() => { try { window._oc_ignoreNextDocClick = false; } catch { } }, 150);

  setTimeout(() => {
    // Ensure we don't leave duplicate handlers attached; attach capture-phase pointerdown
    try {
      if (window._notificationOutsideHandler) {
        try { document.removeEventListener('pointerdown', window._notificationOutsideHandler, true); } catch { }
        try { document.removeEventListener('click', window._notificationOutsideHandler, false); } catch { }
        window._notificationOutsideHandler = null;
      }
    } catch { }

    window._notificationOutsideHandler = function (e) {
      try { console.debug('OC: outsideHandler invoked', { target: e.target && (e.target.id || e.target.className || e.target.tagName) }); } catch {}
      try { if (window._oc_ignoreNextDocClick) return; } catch { }
      const panelEl = document.getElementById('notificationPanel');
      const bellEl = document.getElementById('notificationBell');
      if (!panelEl) return;
      // If click target is inside panel or bell, do nothing
      if (panelEl.contains(e.target) || (bellEl && bellEl.contains(e.target))) return;
      try { console.debug('OC: outsideHandler closing panel'); } catch {}
      closeNotificationPanel();
    };
    // Capture-phase pointerdown runs before many bubble-phase handlers and will catch outside clicks/taps
    document.addEventListener('pointerdown', window._notificationOutsideHandler, true);
    // Keep a bubble-phase click fallback for environments that may not fire pointer events
    document.addEventListener('click', window._notificationOutsideHandler, false);
  }, 0);

  // Keep the panel anchored to the bell while open (reposition on scroll/resize)
  try {
    window._oc_notificationReposition = function () {
      try {
        const bell = document.getElementById('notificationBell');
        const panel = document.getElementById('notificationPanel');
        if (!bell || !panel) return;
        const rect = bell.getBoundingClientRect();
        const desiredRight = Math.max(8, Math.round(window.innerWidth - rect.right + 8));
        const top = rect.bottom + 8;
        panel.style.right = `${desiredRight}px`;
        panel.style.top = `${top}px`;
      } catch { }
    };
    window.addEventListener('scroll', window._oc_notificationReposition, { passive: true });
    window.addEventListener('resize', window._oc_notificationReposition);
  } catch { }
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
    } catch { }
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
  } catch { }
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
        <select id="storeSelect" class="hidden">
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
            <div class="flex gap-3 w-full items-center">
              <div class="flex items-center gap-2">
                <select id="mhStartHour" class="select select-bordered select-sm">
                  ${Array.from({ length: 24 }, (_, h) => `<option value="${String(h).padStart(2, '0')}">${String(h).padStart(2, '0')}</option>`).join('')}
                </select>
                <select id="mhStartMin" class="select select-bordered select-sm">
                  <option value="00">00</option>
                  <option value="30">30</option>
                </select>
              </div>
              <span class="self-center text-sm text-base-content/70">to</span>
              <div class="flex items-center gap-2">
                <select id="mhEndHour" class="select select-bordered select-sm">
                  ${Array.from({ length: 24 }, (_, h) => `<option value="${String(h).padStart(2, '0')}">${String(h).padStart(2, '0')}</option>`).join('')}
                </select>
                <select id="mhEndMin" class="select select-bordered select-sm">
                  <option value="00">00</option>
                  <option value="30">30</option>
                </select>
              </div>
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
            <div class="flex gap-3 w-full items-center">
              <div class="flex items-center gap-2">
                <select id="shStartHour" class="select select-bordered select-sm">
                  ${Array.from({ length: 24 }, (_, h) => `<option value="${String(h).padStart(2, '0')}">${String(h).padStart(2, '0')}</option>`).join('')}
                </select>
                <select id="shStartMin" class="select select-bordered select-sm">
                  <option value="00">00</option>
                  <option value="30">30</option>
                </select>
              </div>
              <span class="self-center text-sm text-base-content/70">to</span>
              <div class="flex items-center gap-2">
                <select id="shEndHour" class="select select-bordered select-sm">
                  ${Array.from({ length: 24 }, (_, h) => `<option value="${String(h).padStart(2, '0')}">${String(h).padStart(2, '0')}</option>`).join('')}
                </select>
                <select id="shEndMin" class="select select-bordered select-sm">
                  <option value="00">00</option>
                  <option value="30">30</option>
                </select>
              </div>
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
            <div class="flex gap-3 w-full items-center">
              <div class="flex items-center gap-2">
                <select id="suStartHour" class="select select-bordered select-sm">
                  ${Array.from({ length: 24 }, (_, h) => `<option value="${String(h).padStart(2, '0')}">${String(h).padStart(2, '0')}</option>`).join('')}
                </select>
                <select id="suStartMin" class="select select-bordered select-sm">
                  <option value="00">00</option>
                  <option value="30">30</option>
                </select>
              </div>
              <span class="self-center text-sm text-base-content/70">to</span>
              <div class="flex items-center gap-2">
                <select id="suEndHour" class="select select-bordered select-sm">
                  ${Array.from({ length: 24 }, (_, h) => `<option value="${String(h).padStart(2, '0')}">${String(h).padStart(2, '0')}</option>`).join('')}
                </select>
                <select id="suEndMin" class="select select-bordered select-sm">
                  <option value="00">00</option>
                  <option value="30">30</option>
                </select>
              </div>
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
  try { bindHoursEditor(); } catch { }

  // Wire buttons
  const cancelBtn = modal.querySelector('#adminStoreCancel');
  const okBtn = modal.querySelector('#adminStoreOk');
  // Ensure buttons don't act as form submit buttons by default
  try { if (cancelBtn) cancelBtn.type = 'button'; } catch { }
  try { if (okBtn) okBtn.type = 'button'; } catch { }
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
          credentials: 'include',
          headers: { 'Content-Type': 'application/json', 'Accept': 'application/json' },
          body: JSON.stringify(payload)
        });
        if (res.ok) {
          showToast('info', 'Store has been successfully updated.');
          closeAdminChangeStoreModal();
          try { if (window.refreshStores) window.refreshStores(); } catch {}
        } else {
          // Log error to server event log with full server response text
          try {
            const userId = parseInt(localStorage.getItem('userId') || '2', 10) || 2;
            let serverText = '';
            try {
              serverText = await res.text();
            } catch { }
            await fetch('/api/log-client-error', {
              method: 'POST',
              headers: { 'Content-Type': 'application/json' },
              body: JSON.stringify({
                userId,
                description: 'Failed to save store changes',
                stackTrace: serverText || `HTTP ${res.status} - ${res.statusText}`
              })
            });
          } catch { }
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
        } catch { }
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
    { c: 'mhClosed', s: ['mhStartHour', 'mhStartMin'], e: ['mhEndHour', 'mhEndMin'] },
    { c: 'shClosed', s: ['shStartHour', 'shStartMin'], e: ['shEndHour', 'shEndMin'] },
    { c: 'suClosed', s: ['suStartHour', 'suStartMin'], e: ['suEndHour', 'suEndMin'] }
  ];
  pairs.forEach(({ c, s, e }) => {
    const cb = document.getElementById(c);
    const sH = document.getElementById(s[0]);
    const sM = document.getElementById(s[1]);
    const eH = document.getElementById(e[0]);
    const eM = document.getElementById(e[1]);
    if (!cb || !sH || !sM || !eH || !eM) return;
    const closed = !!cb.checked;
    [sH, sM, eH, eM].forEach(x => x.disabled = closed);
    if (closed) {
      sH.value = '00'; sM.value = '00'; eH.value = '00'; eM.value = '00';
    }
  });
}

function bindHoursEditor() {
  const ids = ['mhClosed', 'shClosed', 'suClosed'];
  ids.forEach(id => {
    const el = document.getElementById(id);
    if (!el || el._oc_bound) return;
    el._oc_bound = true;
    el.addEventListener('change', () => updateHoursEditorDisabled());
  });
  // Bind selects to fix ranges whenever hour/min changes
  const selIds = ['mhStartHour', 'mhStartMin', 'mhEndHour', 'mhEndMin', 'shStartHour', 'shStartMin', 'shEndHour', 'shEndMin', 'suStartHour', 'suStartMin', 'suEndHour', 'suEndMin'];
  selIds.forEach(id => {
    const el = document.getElementById(id);
    if (!el || el._oc_norm_bound) return;
    el._oc_norm_bound = true;
    const handler = () => { try { fixPairRanges(); } catch { } };
    ['change', 'blur'].forEach(evt => el.addEventListener(evt, handler));
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
  return `${String(h).padStart(2, '0')}:${String(m).padStart(2, '0')}`;
}

function prevHalfHour(val) {
  const mins = timeToMinutes(normalizeToHalfHour(val));
  if (mins == null) return '';
  const prev = mins - 30;
  if (prev < 0) return '';
  const h = Math.floor(prev / 60); const m = prev % 60;
  return `${String(h).padStart(2, '0')}:${String(m).padStart(2, '0')}`;
}

function ensureValidRange(startPrefix, endPrefix) {
  const sH = document.getElementById(`${startPrefix}Hour`);
  const sM = document.getElementById(`${startPrefix}Min`);
  const eH = document.getElementById(`${endPrefix}Hour`);
  const eM = document.getElementById(`${endPrefix}Min`);
  if (!sH || !sM || !eH || !eM) return;
  const s = `${sH.value}:${sM.value}`;
  const e = `${eH.value}:${eM.value}`;
  const sm = timeToMinutes(s), em = timeToMinutes(e);
  if (sm == null || em == null) return;
  if (em <= sm) {
    const cand = nextHalfHour(s);
    if (cand) {
      const [ch, cm] = cand.split(':');
      eH.value = ch; eM.value = cm;
    } else {
      // If start is at 23:30, fallback to 23:00–23:30
      sH.value = '23'; sM.value = '00';
      eH.value = '23'; eM.value = '30';
    }
  }
}

function fixPairRanges() {
  ensureValidRange('mhStart', 'mhEnd');
  ensureValidRange('shStart', 'shEnd');
  ensureValidRange('suStart', 'suEnd');
}

function setHoursEditorFromString(str) {
  const mhClosed = document.getElementById('mhClosed');
  const mhStartHour = document.getElementById('mhStartHour');
  const mhStartMin = document.getElementById('mhStartMin');
  const mhEndHour = document.getElementById('mhEndHour');
  const mhEndMin = document.getElementById('mhEndMin');
  const shClosed = document.getElementById('shClosed');
  const shStartHour = document.getElementById('shStartHour');
  const shStartMin = document.getElementById('shStartMin');
  const shEndHour = document.getElementById('shEndHour');
  const shEndMin = document.getElementById('shEndMin');
  const suClosed = document.getElementById('suClosed');
  const suStartHour = document.getElementById('suStartHour');
  const suStartMin = document.getElementById('suStartMin');
  const suEndHour = document.getElementById('suEndHour');
  const suEndMin = document.getElementById('suEndMin');

  const clear = () => {
    [mhClosed, shClosed, suClosed].forEach(cb => { if (cb) cb.checked = false; });
    [mhStartHour, mhStartMin, mhEndHour, mhEndMin, shStartHour, shStartMin, shEndHour, shEndMin, suStartHour, suStartMin, suEndHour, suEndMin]
      .forEach(i => { if (i) i.value = '00'; });
  };
  clear();

  if (!str || typeof str !== 'string') return;

  const norm = str.replace(/\s+/g, ' ').trim();
  const parts = norm.split(';').map(p => p.trim()).filter(Boolean);
  const parseClause = (label) => parts.find(p => p.toLowerCase().startsWith(label.toLowerCase()));

  const applyTimes = (closedEl, startHourEl, startMinEl, endHourEl, endMinEl, clause) => {
    if (!clause) return;
    if (/closed/i.test(clause)) { if (closedEl) closedEl.checked = true; return; }
    const m = clause.match(/(\d{1,2}:\d{2})\s*-\s*(\d{1,2}:\d{2})/);
    if (m) {
      const s = normalizeToHalfHour(m[1].padStart(5, '0'));
      const e = normalizeToHalfHour(m[2].padStart(5, '0'));
      const [sh, sm] = s.split(':');
      const [eh, em] = e.split(':');
      if (startHourEl) startHourEl.value = sh;
      if (startMinEl) startMinEl.value = sm;
      if (endHourEl) endHourEl.value = eh;
      if (endMinEl) endMinEl.value = em;
    }
  };

  // Support grouped labels
  const monSun = parseClause('Mon-Sun');
  if (monSun) {
    applyTimes(mhClosed, mhStartHour, mhStartMin, mhEndHour, mhEndMin, monSun);
    applyTimes(shClosed, shStartHour, shStartMin, shEndHour, shEndMin, monSun);
    applyTimes(suClosed, suStartHour, suStartMin, suEndHour, suEndMin, monSun);
    return;
  }
  const monSat = parseClause('Mon-Sat');
  if (monSat) {
    applyTimes(mhClosed, mhStartHour, mhStartMin, mhEndHour, mhEndMin, monSat);
    applyTimes(shClosed, shStartHour, shStartMin, shEndHour, shEndMin, monSat);
  }
  const monFri = parseClause('Mon-Fri');
  applyTimes(mhClosed, mhStartHour, mhStartMin, mhEndHour, mhEndMin, monFri);
  const sat = parseClause('Sat') || parseClause('Sat-Sun');
  applyTimes(shClosed, shStartHour, shStartMin, shEndHour, shEndMin, sat);
  const sun = parseClause('Sun') || parseClause('Sat-Sun');
  applyTimes(suClosed, suStartHour, suStartMin, suEndHour, suEndMin, sun);
  updateHoursEditorDisabled();
}

function getHoursStringFromEditor() {
  const mh = {
    closed: !!document.getElementById('mhClosed')?.checked,
    start: `${document.getElementById('mhStartHour')?.value || '00'}:${document.getElementById('mhStartMin')?.value || '00'}`,
    end: `${document.getElementById('mhEndHour')?.value || '00'}:${document.getElementById('mhEndMin')?.value || '00'}`
  };
  const sa = {
    closed: !!document.getElementById('shClosed')?.checked,
    start: `${document.getElementById('shStartHour')?.value || '00'}:${document.getElementById('shStartMin')?.value || '00'}`,
    end: `${document.getElementById('shEndHour')?.value || '00'}:${document.getElementById('shEndMin')?.value || '00'}`
  };
  const su = {
    closed: !!document.getElementById('suClosed')?.checked,
    start: `${document.getElementById('suStartHour')?.value || '00'}:${document.getElementById('suStartMin')?.value || '00'}`,
    end: `${document.getElementById('suEndHour')?.value || '00'}:${document.getElementById('suEndMin')?.value || '00'}`
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
    // Request fresh data; include no-store cache and a timestamp to avoid cached responses
    const res = await fetch(`/api/stores?userId=${userId}&t=${Date.now()}`, { cache: 'no-store', credentials: 'include', headers: { 'Accept': 'application/json' } });
    const data = await res.json();
    let items = data.items || [];
    try { items = items.slice().sort((a, b) => (a.id ?? 0) - (b.id ?? 0)); } catch { }
    const sel = document.getElementById('storeSelect');
    if (!sel) return;
    // Ensure select is enabled for normal operation
    try { sel.disabled = false; } catch { }
    // Clear and repopulate options
    sel.innerHTML = '<option value="">Select a store...</option>';
    items.forEach(s => {
      const opt = document.createElement('option');
      opt.value = s.id;
      opt.textContent = s.name;
      sel.appendChild(opt);
    });
    // Keep latest items available on the select so the change handler always reads fresh data
    sel._oc_items = items;
    // Bind change to populate fields; handler references sel._oc_items so it sees updates
    if (!sel._oc_bound) {
      sel._oc_bound = true;
      sel.addEventListener('change', () => {
        const id = parseInt(sel.value || '0', 10);
        const s = (sel._oc_items || []).find(x => x.id === id);
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

    // Also ensure select has the latest items reference after re-populating
    sel._oc_items = items;

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
    } catch { }
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

// Row action menus helper: attach once and manage open/close for row kebab menus
(function () {
  if (window._oc_row_menus_bound) return; window._oc_row_menus_bound = true;

  function closeAll(root) {
    // Close any open row menus inside the provided root (limit to rows)
    const scope = root || document;
    const candidates = Array.from(scope.querySelectorAll('.dropdown-content, .oc-row-menu'))
      .filter(m => !!m.closest && !!m.closest('tr'));
    candidates.forEach(m => {
      m.classList.add('hidden');
      // find a kebab button in the same row
      const row = m.closest('tr');
      if (row) {
        const btn = row.querySelector('button.oc-row-kebab, button[data-oc-kebab], .dropdown > button');
        if (btn) btn.setAttribute('aria-expanded', 'false');
        const wrap = m.closest('.dropdown') || m.closest('.oc-row-menu-wrapper');
        if (wrap) wrap.classList.remove('dropdown-open');
      }
    });
  }

  function toggleMenuFor(button) {
    if (!button) return;
    // Prefer the nearest dropdown wrapper
    const wrapper = button.closest('.dropdown') || button.closest('.oc-row-menu-wrapper') || button.parentElement;
    if (!wrapper) return;
    // Ensure this is a row-level menu (inside a table row)
    const row = wrapper.closest('tr');
    if (!row) return; // avoid toggling toolbar menus
    const menu = wrapper.querySelector('.dropdown-content') || wrapper.querySelector('.oc-row-menu');
    if (!menu) return;
    const isOpen = !menu.classList.contains('hidden');
    // Close others first (within same table)
    closeAll(row.closest('table') || document);
    if (isOpen) {
      menu.classList.add('hidden');
      button.setAttribute('aria-expanded', 'false');
      wrapper.classList.remove('dropdown-open');
    } else {
      menu.classList.remove('hidden');
      button.setAttribute('aria-expanded', 'true');
      wrapper.classList.add('dropdown-open');
    }
  }

  // Capture-phase pointerdown: open when clicking the kebab button, close when clicking outside
  document.addEventListener('pointerdown', function (ev) {
    try {
      const tgt = ev.target;
      // Only consider row-level kebabs inside a <tr>
      const row = tgt.closest && tgt.closest('tr');
      if (row) {
        const kebab = tgt.closest && (tgt.closest('button.oc-row-kebab') || row.querySelector('button.oc-row-kebab') || tgt.closest('button[data-oc-kebab]') || row.querySelector('button[data-oc-kebab]') || tgt.closest('.dropdown > button'));
        if (kebab) {
          // prevent outside closers from seeing this as a click-away
          try { ev.stopPropagation(); ev.preventDefault(); } catch {}
          toggleMenuFor(kebab);
          return;
        }
        // If click is inside any open row menu, ignore
        const openMenu = tgt.closest && (tgt.closest('.dropdown-content') || tgt.closest('.oc-row-menu'));
        if (openMenu && openMenu.closest('tr')) return;
      }
      // else close all row menus
      closeAll(document);
    } catch (e) { }
  }, true);

  // Close on Escape
  document.addEventListener('keydown', function (ev) {
    try { if (ev.key === 'Escape') closeAll(document); } catch (e) { }
  }, true);

  // Expose small helper to allow reusing when needed
  try { window._oc_rowMenusCloseAll = (root) => closeAll(root ? document.querySelector(root) : document); } catch {}
})();

// Toolbar dropdown initializer: ensure toolbar kebab buttons open/close reliably
(function () {
  const ids = ['btnToolbarMenu', 'btnMore'];
  function setup(btn) {
    if (!btn || btn._oc_toolbar_bound) return;
    btn._oc_toolbar_bound = true;
    const container = btn.closest('.dropdown');
    const content = container ? container.querySelector('.dropdown-content') : null;

    function setOpen(open) {
      try {
        if (!container) return;
        container.classList.toggle('dropdown-open', !!open);
        if (content) content.classList.toggle('hidden', !open);
        btn.setAttribute('aria-expanded', open ? 'true' : 'false');
        try {
          // Notify any page-specific logic that the toolbar menu toggled
          container.dispatchEvent(new CustomEvent('oc-toolbar-toggle', { detail: { open: !!open } }));
        } catch {}
      } catch { }
    }

    // Use capture-phase pointerdown so we run before other handlers
    btn.addEventListener('pointerdown', function (ev) {
      try {
        ev.preventDefault();
        ev.stopPropagation();
      } catch { }
      try { window._oc_ignoreNextDocClick = true; } catch { }
      setTimeout(function () { try { window._oc_ignoreNextDocClick = false; } catch { } }, 250);
      // Read open state from the container when possible (more reliable than aria attr alone)
      const isOpen = (container && container.classList && container.classList.contains('dropdown-open')) || btn.getAttribute('aria-expanded') === 'true';
      setOpen(!isOpen);
    }, true);

    // Close when clicking outside
    document.addEventListener('pointerdown', function (ev) {
      try {
        if (window._oc_ignoreNextDocClick) return;
        if (!container) return;
        if (!container.classList.contains('dropdown-open')) return;
        const tgt = ev.target || ev.srcElement;
        if (container.contains(tgt)) return;
        setOpen(false);
      } catch { }
    }, true);

    // Close on Escape
    document.addEventListener('keydown', function (ev) {
      try { if (ev.key === 'Escape') setOpen(false); } catch { }
    }, true);

    // Close when a menu item is clicked
    if (content) content.addEventListener('click', function () { try { setOpen(false); } catch { } });
  }

  function init() {
    ids.forEach(id => {
      try { setup(document.getElementById(id)); } catch { }
    });
  }

  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', init); else init();

  // Expose a helper to close all toolbar menus (used by page code when actions run)
  try {
    window._oc_toolbar_closeAll = function () {
      try {
        ids.forEach(id => {
          try {
            const btn = document.getElementById(id);
            if (!btn) return;
            const container = btn.closest && btn.closest('.dropdown');
            if (!container) return;
            const content = container.querySelector && container.querySelector('.dropdown-content');
            container.classList.remove('dropdown-open');
            if (content) content.classList.add('hidden');
            try { btn.setAttribute('aria-expanded', 'false'); } catch {}
          } catch {}
        });
      } catch {}
    };
  } catch {}
})();

// Additional minimal dropdown hardening for Menu and Admin
(function () {
  function closeDropdown(wrapper) {
    if (!wrapper) return;
    wrapper.classList.remove('dropdown-open');
    const menu = wrapper.querySelector('.dropdown-content');
    if (menu) menu.classList.add('hidden');
    const activator = wrapper.querySelector('[role="button"], #adminLabel');
    if (activator) activator.setAttribute('aria-expanded', 'false');
  }

  function openDropdown(wrapper) {
    if (!wrapper) return;
    wrapper.classList.add('dropdown-open');
    const menu = wrapper.querySelector('.dropdown-content');
    if (menu) menu.classList.remove('hidden');
    const activator = wrapper.querySelector('[role="button"], #adminLabel');
    if (activator) activator.setAttribute('aria-expanded', 'true');
  }

  function toggleDropdown(wrapper) {
    if (!wrapper) return;
    if (wrapper.classList.contains('dropdown-open')) closeDropdown(wrapper); else openDropdown(wrapper);
  }

  function setupNavbarDropdown(wrapperId, activatorId) {
    const wrapper = document.getElementById(wrapperId);
    if (!wrapper) return;
    const activator = activatorId ? document.getElementById(activatorId) : wrapper.querySelector('[role="button"]');
    const menu = wrapper.querySelector('.dropdown-content');
    if (!activator || !menu) return;
    if (activator._oc_nav_setup) return;
    activator._oc_nav_setup = true;

    // Use capture-phase pointerdown so this runs before other handlers
    activator.addEventListener('pointerdown', function (ev) {
      try {
        ev.preventDefault();
        ev.stopPropagation();
      } catch { }
      try { window._oc_ignoreNextDocClick = true; } catch { }
      setTimeout(function () { try { window._oc_ignoreNextDocClick = false; } catch { } }, 300);
      toggleDropdown(wrapper);
    }, true);

    // Anchor fallback: if navigation is prevented by other handlers, force it shortly after click
    Array.from(menu.querySelectorAll('a[href]')).forEach(a => {
      if (a._oc_nav_bound) return;
      a._oc_nav_bound = true;
      a.addEventListener('click', function (ev) {
        try {
          // Let default happen first; schedule a fallback
          const href = a.getAttribute('href');
          if (!href) return;
          setTimeout(function () {
            try {
              const dest = new URL(href, location.origin).pathname;
              if (location.pathname !== dest) location.href = href;
            } catch { }
          }, 50);
        } catch { }
      }, false);
    });

    // Close when pressing Escape
    document.addEventListener('keydown', function (ev) {
      try {
        if (ev.key === 'Escape' || ev.key === 'Esc') {
          if (wrapper.classList.contains('dropdown-open')) closeDropdown(wrapper);
        }
      } catch { }
    }, true);

    // Document click (capture) closes when clicking outside
    document.addEventListener('pointerdown', function (ev) {
      try {
        if (window._oc_ignoreNextDocClick) return;
        if (!wrapper.classList.contains('dropdown-open')) return;
        const tgt = ev.target || ev.srcElement;
        if (wrapper.contains(tgt)) return;
        closeDropdown(wrapper);
      } catch { }
    }, true);
  }

  document.addEventListener('DOMContentLoaded', function () {
    try { setupNavbarDropdown('eventlogDropdown'); } catch { }
    try { setupNavbarDropdown('userDropdown', 'adminLabel'); } catch { }
  });
})();

// (duplicate toolbar initializer removed; the unified initializer above handles both btnToolbarMenu and btnMore)