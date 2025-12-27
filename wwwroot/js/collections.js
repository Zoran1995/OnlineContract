// Page-specific fix for Collections: ensure notification panel anchors to navbar
(function () {
  // Ensure the panel is hidden immediately (defensive): some pages may briefly
  // render the panel before shared handlers run. Try immediate hide, fallback
  // to short retry if element not yet created.
  (function ensureHiddenNow() {
    const hide = (panel) => {
      try {
        if (!panel) return;
        if (!panel.classList.contains('hidden')) panel.classList.add('hidden');
        try { panel.style.display = 'none'; } catch {}
        try { panel.style.visibility = 'hidden'; } catch {}
        try { panel.style.left = ''; panel.style.right = ''; panel.style.top = ''; panel.style.bottom = 'auto'; } catch {}
      } catch { }
    };
    try {
      const p = document.getElementById('notificationPanel');
      if (p) { hide(p); return; }
    } catch { }
    // Retry a few times shortly after load in case shared.js creates it later
    let tries = 0;
    const t = setInterval(() => {
      try {
        const p = document.getElementById('notificationPanel');
        if (p) { hide(p); clearInterval(t); return; }
        tries++;
        if (tries > 8) clearInterval(t);
      } catch { clearInterval(t); }
    }, 80);
  })();

  function fixNotificationPanel() {
    try {
      const panel = document.getElementById('notificationPanel');
      const navbar = document.querySelector('.navbar');
      if (!panel || !navbar) return;
      if (panel.classList.contains('hidden')) return;
      // ensure panel is attached to body
      if (panel.parentElement !== document.body) try { document.body.appendChild(panel); } catch {}
      const nrect = navbar.getBoundingClientRect();
      const desiredRight = Math.max(8, Math.round(window.innerWidth - (nrect.right || 0) + 8));
      const top = (nrect.bottom || 0) + 8;
      panel.style.left = '';
      panel.style.right = `${desiredRight}px`;
      panel.style.top = `${top}px`;
      panel.style.bottom = 'auto';
      // ensure fixed positioning
      try { panel.style.setProperty('position', 'fixed', 'important'); } catch {}
    } catch { }
  }

  document.addEventListener('DOMContentLoaded', () => {
    // Reparent immediately in case shared.js attached it elsewhere
    try { const panel = document.getElementById('notificationPanel'); if (panel && panel.parentElement !== document.body) document.body.appendChild(panel); } catch {}

    // Reposition on open via mutation observer — override shared reposition handler
    const panel = document.getElementById('notificationPanel');
    if (panel) {
      let prevReposition = null;
      const mo = new MutationObserver(() => {
        try {
          // Always attempt to keep panel anchored when attributes change
          try { fixNotificationPanel(); } catch {}
          const isOpen = !panel.classList.contains('hidden');
          try { console.debug('OC: collections mutation isOpen=', isOpen, 'panel.class=', panel.className); } catch {}
          if (isOpen) {
            // Apply immediate fix
            fixNotificationPanel();

            // Save previous shared reposition function and replace with navbar-based one
            try {
              if (!prevReposition) prevReposition = window._oc_notificationReposition || null;
            } catch { prevReposition = null; }

            try {
              window._oc_notificationReposition = function () {
                try {
                  const navbar = document.querySelector('.navbar');
                  const panelEl = document.getElementById('notificationPanel');
                  if (!navbar || !panelEl) return;
                  const nrect = navbar.getBoundingClientRect();
                  const desiredRight = Math.max(8, Math.round(window.innerWidth - (nrect.right || 0) + 8));
                  const top = (nrect.bottom || 0) + 8;
                  panelEl.style.left = '';
                  panelEl.style.right = `${desiredRight}px`;
                  panelEl.style.top = `${top}px`;
                  panelEl.style.bottom = 'auto';
                  // keep cached pos in shared shape
                  try { window._notificationPanelPos = { right: desiredRight, top }; } catch { }
                } catch { }
              };
              // ensure shared listeners exist for reposition
              try {
                window.removeEventListener('scroll', prevReposition, { passive: true });
              } catch { }
              try { window.removeEventListener('resize', prevReposition); } catch { }
              try { window.addEventListener('scroll', window._oc_notificationReposition, { passive: true }); } catch { }
              try { window.addEventListener('resize', window._oc_notificationReposition); } catch { }
            } catch { }

            // Also keep a short interval to resist other layout thrash while open
            window._oc_collections_fix_interval = window._oc_collections_fix_interval || setInterval(fixNotificationPanel, 200);
          } else {
            // Panel closed: restore previous reposition handler and clear interval
            try {
              if (window._oc_collections_fix_interval) { clearInterval(window._oc_collections_fix_interval); window._oc_collections_fix_interval = null; }
            } catch { }
            try {
              // remove our reposition
              try { window.removeEventListener('scroll', window._oc_notificationReposition, { passive: true }); } catch { }
              try { window.removeEventListener('resize', window._oc_notificationReposition); } catch { }
              window._oc_notificationReposition = null;
              // restore previous if present
              if (prevReposition) {
                window._oc_notificationReposition = prevReposition;
                try { window.addEventListener('scroll', window._oc_notificationReposition, { passive: true }); } catch { }
                try { window.addEventListener('resize', window._oc_notificationReposition); } catch { }
                prevReposition = null;
              }
            } catch { }
            // ensure panel position cache cleared
            try { window._notificationPanelPos = null; } catch { }
          }
        } catch { }
      });
      try { mo.observe(panel, { attributes: true, attributeFilter: ['class', 'style'] }); } catch { }
      // Also observe the document body for reparenting or unexpected DOM moves
      try {
        const bodyObs = new MutationObserver(() => {
          try {
            const pnow = document.getElementById('notificationPanel');
            if (!pnow) return;
            if (pnow.parentElement !== document.body) {
              try { console.debug('OC: notificationPanel reparented to', pnow.parentElement && (pnow.parentElement.id || pnow.parentElement.className || pnow.parentElement.tagName)); } catch {}
              try { document.body.appendChild(pnow); } catch {}
              try { fixNotificationPanel(); } catch {}
            }
          } catch { }
        });
        bodyObs.observe(document.body, { childList: true, subtree: true });
      } catch { }
    }

    // Add capture-phase handlers on navbar and bell to mirror Products behavior:
    // when user interacts with navbar or bell, set the shared ignore-guard so
    // document-level click-away handlers don't immediately close the panel.
    function _oc_setCollectionsIgnore() {
      try { window._oc_ignoreNextDocClick = true; } catch { }
      try { if (window._oc_collections_ignore_timer) clearTimeout(window._oc_collections_ignore_timer); } catch { }
      try { window._oc_collections_ignore_timer = setTimeout(() => { try { window._oc_ignoreNextDocClick = false; } catch { } }, 350); } catch { }
    }

    try {
      const navbar = document.querySelector('.navbar');
      if (navbar && !navbar._oc_col_bound) {
        navbar._oc_col_bound = true;
        navbar.addEventListener('pointerdown', (e) => { try { _oc_setCollectionsIgnore(); } catch { } }, true);
        navbar.addEventListener('click', (e) => { try { _oc_setCollectionsIgnore(); } catch { } }, true);
      }
    } catch { }

    try {
      const bell = document.getElementById('notificationBell');
      if (bell && !bell._oc_col_bound) {
        bell._oc_col_bound = true;
        // Do not stop propagation: allow shared handlers to receive the
        // event so open/close toggle works reliably. Only set ignore-guard.
        bell.addEventListener('pointerdown', (e) => { try { _oc_setCollectionsIgnore(); } catch { } }, true);
        bell.addEventListener('click', (e) => { try { _oc_setCollectionsIgnore(); } catch { } }, true);
      }
    } catch { }

    window.addEventListener('resize', fixNotificationPanel);
    window.addEventListener('scroll', fixNotificationPanel, { passive: true });
  });
})();
