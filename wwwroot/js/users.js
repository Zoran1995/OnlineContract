
(function () {
  let selectedUserId = null;
  let items = [];
  let groups = [];
  let groupMap = {}; // id -> code
  let groupCodeToId = {}; // code -> id
  let page = 1;
  let pageSize = 10;
  let totalPages = 1;
  let totalCount = 0;
  let sortBy = '', sortDir = '';

  function roleName(id) {
    switch (id) {
      case 8: return 'Administrator';
      case 7: return 'Manager';
      case 6: return 'Worker';
      case 5: return 'Customer';
      default: return String(id);
    }
  }

  function showLoading() {
    const ov = document.getElementById('loadingOverlay');
    if (ov) ov.classList.remove('hidden');
  }
  function hideLoading() {
    const ov = document.getElementById('loadingOverlay');
    if (ov) ov.classList.add('hidden');
  }
  function setEmptyState(visible) {
    const es = document.getElementById('emptyState');
    if (!es) return;
    es.classList.toggle('hidden', !visible);
  }

  async function loadGroups() {
    try {
      // Request first page with large pageSize to load all groups for dropdown
      const res = await fetch('/api/groups?page=1&pageSize=1000', { method: 'GET', credentials: 'include', headers: { 'Accept': 'application/json' } });
      if (res.status === 401) { window.location.href = '/login?mode=login'; return; }
      if (res.status === 403) { return; }
      const data = await res.json();
      groups = Array.isArray(data.items) ? data.items : (Array.isArray(data) ? data : []);
      groupMap = {};
      // /api/groups already returns only groups; map all
      groups.forEach(g => {
        if (g && g.id != null) {
          const code = g.code || g.Code || '';
          groupMap[g.id] = code;
          if (code) groupCodeToId[code] = g.id;
        }
      });

      const teamSel = document.getElementById('fltTeam');
      if (teamSel) {
        const current = teamSel.value;
        teamSel.innerHTML = '';
        const optAll = document.createElement('option'); optAll.value = ''; optAll.textContent = 'All teams'; teamSel.appendChild(optAll);
        groups.forEach(g => {
          if (!g) return;
          const opt = document.createElement('option');
          opt.value = g.code || g.Code || '';
          opt.textContent = g.code || g.Code || '';
          teamSel.appendChild(opt);
        });
        // restore selection if possible
        teamSel.value = current || '';
      }
    } catch {}
  }

  async function loadUsers() {
    try {
      showLoading();
      setEmptyState(false);

      const nameEl = document.getElementById('fltName');
      const emailEl = document.getElementById('fltEmail');
      const teamEl = document.getElementById('fltTeam');
      const name = (nameEl?.value || '').trim();
      const email = (emailEl?.value || '').trim();
      const team = (teamEl?.value || '').trim();

      // Read pageSize from the select if it exists
      const sizeEl = document.getElementById('pgSize') || document.getElementById('pageSize');
      if (sizeEl) pageSize = parseInt(sizeEl.value || pageSize, 10) || pageSize;

      const userId = localStorage.getItem('userId') ?? 2;
      const qs = new URLSearchParams();
      qs.set('name', name);
      if (email) qs.set('email', email);
      qs.set('team', team);
      qs.set('page', String(page));
      qs.set('pageSize', String(pageSize));
      qs.set('userId', String(userId));
      if (sortBy) qs.set('sortBy', sortBy);
      if (sortDir) qs.set('sortDir', sortDir);
      const res = await fetch(`/api/users?${qs.toString()}`, {
        method: 'GET',
        credentials: 'include',
        headers: { 'Accept': 'application/json' }
      });

      if (res.status === 401) {
        window.location.href = '/login?mode=login';
        return;
      }
      if (res.status === 403) {
        showAccessDeniedInline(); // inline Access Denied
        return;
      }

      const data = await res.json();
      items = Array.isArray(data.items) ? data.items : [];
      totalCount = Number(data.totalCount ?? items.length);
      {
        const apiPages = Number(data.totalPages);
        const computedPages = Math.ceil(totalCount / pageSize) || 1;
        totalPages = (Number.isFinite(apiPages) && apiPages > 0) ? apiPages : computedPages;
      }

      renderTable();
      renderPagination();
      setEmptyState(items.length === 0);
      updateOpenState();
      updateMenuActionsState();

      if (selectedUserId) {
        const u = items.find(x => x.id === selectedUserId);
        if (u) updateDeactivateLabel(u);
      }
    } catch (err) {
      try { showToast('error', 'Failed to load users'); } catch {}
    } finally {
      hideLoading();
    }
  }

  function renderTable() {
    const body = document.getElementById('usersBody');
    if (!body) return;
    body.innerHTML = '';

    items.forEach(u => {
      const tr = document.createElement('tr');
      if (selectedUserId === u.id) tr.className = 'active';

      tr.addEventListener('click', () => {
        selectedUserId = u.id;
        renderTable();
        updateOpenState();
        updateDeactivateLabel(u);
        updateMenuActionsState();
      });

      const status = u.isDeleted ? 'Deleted' : (u.isActive ? 'Active' : 'Inactive');
      const name = String((u.firstName ?? '') + (u.lastName ? ' ' + u.lastName : '')).trim();
      const ownerId = (u.ownerId != null ? Number(u.ownerId) : null);
      const codeFromOwner = (ownerId != null && groupMap[ownerId]) ? groupMap[ownerId] : '';
      const teamDisplay = (u.groupName && String(u.groupName).trim().length > 0)
        ? u.groupName
        : ((codeFromOwner && String(codeFromOwner).trim().length > 0) ? codeFromOwner : '—');

      const cells = [
        String(u.id),
        String(u.code ?? ''),
        name,
        roleName(u.roleId),
        teamDisplay,
        status
      ];

      body.appendChild(tr);
      cells.forEach(text => {
        const td = document.createElement('td');
        td.textContent = text;
        tr.appendChild(td);
      });
    });
    try {
      const keys = ['id','code','name','roleId','groupName','status'];
      document.querySelectorAll('#usersTable thead th').forEach((th, idx) => {
        th.style.cursor = 'pointer';
        const ex = th.querySelector('.sort-indicator'); if (ex) ex.remove();
        const key = keys[idx] || '';
        const span = document.createElement('span'); span.className = 'sort-indicator ml-2';
        if (key && key === sortBy) { span.textContent = sortDir === 'desc' ? ' ▼' : ' ▲'; }
        th.appendChild(span);
        th.onclick = () => {
          if (!key) return;
          // map displayed 'name' to firstName sort, 'status' to isActive
          const mapKey = key === 'name' ? 'firstName' : key === 'status' ? 'isActive' : key;
          if (sortBy === mapKey) sortDir = (sortDir === 'desc' ? 'asc' : 'desc'); else { sortBy = mapKey; sortDir = 'asc'; }
          page = 1; loadUsers();
        };
      });
    } catch {}
  }

  function updateOpenState() {
    const btn = document.getElementById('btnOpen');
    if (!btn) return;
    // Disable Open when no selection OR when selected user is inactive
    const u = selectedUserId ? items.find(x => x.id === selectedUserId) : null;
    btn.disabled = !(selectedUserId && u && !!u.isActive);
  }

  function updateMenuActionsState() {
    const deactivateLink = document.getElementById('btnDeactivate');
    const deleteLink = document.getElementById('btnDelete');
    const hasSelection = !!selectedUserId;

    // Hide Activate/Deactivate item when no row is selected
    try {
      const deactivateItem = deactivateLink ? deactivateLink.closest('li') : null;
      if (deactivateItem) deactivateItem.classList.toggle('hidden', !hasSelection);
    } catch {}

    const setState = (el, enabled) => {
      if (!el) return;
      if (enabled) {
        el.removeAttribute('aria-disabled');
        el.classList.remove('pointer-events-none', 'opacity-50');
      } else {
        el.setAttribute('aria-disabled', 'true');
        el.classList.add('pointer-events-none', 'opacity-50');
      }
    };

    // Delete stays visible; disabled when no selection
    setState(deleteLink, hasSelection);
    // Activate/Deactivate enabled only with selection (hidden when none)
    setState(deactivateLink, hasSelection);

    // Update label/icon based on current selection's active state
    try {
      const u = hasSelection ? items.find(x => x.id === selectedUserId) : null;
      if (u) updateDeactivateLabel(u);
    } catch {}
  }

  function updateDeactivateLabel(user) {
    const link = document.getElementById('btnDeactivate');
    if (!link) return;
    const isActive = user ? !!user.isActive : false;
    const icon = isActive ? 'block' : 'check_circle';
    const label = isActive ? 'Deactivate' : 'Activate';
    link.innerHTML = `<span class="material-icons">${icon}</span><span>${label}</span>`;
  }

  function openUserModal(user) {
    const modal = document.getElementById('userModal');
    const title = document.getElementById('userModalTitle');
    const uCode = document.getElementById('uCode');
    const uRole = document.getElementById('uRole');
    const uFirst = document.getElementById('uFirst');
    const uLast = document.getElementById('uLast');
    const uEmail = document.getElementById('uEmail');
    const uPhone = document.getElementById('uPhone');
    const uTeamCode = document.getElementById('uTeamCode');
    const uPassword = document.getElementById('uPassword');
    let selectedGroupIdEl = document.getElementById('uOwnerId');

    if (!selectedGroupIdEl && uTeamCode?.parentElement) {
      selectedGroupIdEl = document.createElement('input');
      selectedGroupIdEl.type = 'hidden';
      selectedGroupIdEl.id = 'uOwnerId';
      uTeamCode.parentElement.appendChild(selectedGroupIdEl);
    }

    if (title) {
      if (user && (user.id != null)) title.textContent = `Edit User ${user.id}`;
      else title.textContent = 'New User';
    }
    const isManager = parseInt(localStorage.getItem('roleId') ?? '0', 10) === 7;

    // Populate Team dropdown with groups
    if (uTeamCode && uTeamCode.tagName.toLowerCase() === 'select') {
      const current = uTeamCode.value;
      uTeamCode.innerHTML = '';
      const optNone = document.createElement('option'); optNone.value = ''; optNone.textContent = ''; uTeamCode.appendChild(optNone);
      groups.forEach(g => {
        if (!g) return;
        const code = g.code || g.Code || '';
        const opt = document.createElement('option');
        opt.value = code; opt.textContent = code;
        uTeamCode.appendChild(opt);
      });
      uTeamCode.value = current || '';
    }

    if (user) {
      if (uCode) uCode.value = user.code ?? '';
      if (uRole) uRole.value = String(user.roleId ?? 5);
      if (uFirst) uFirst.value = user.firstName ?? '';
      if (uLast) uLast.value = user.lastName ?? '';
      if (uEmail) uEmail.value = user.email ?? '';
      if (uPhone) uPhone.value = user.phone ?? '';
      // Select team by ownerId mapping or groupName
      const codeFromOwner = (user.ownerId != null && groupMap[user.ownerId]) ? groupMap[user.ownerId] : '';
      const selectedCode = (user.groupName && user.groupName.trim()) ? user.groupName : codeFromOwner;
      if (uTeamCode) uTeamCode.value = selectedCode || '';
      if (selectedGroupIdEl) {
        const mappedId = selectedCode ? groupCodeToId[selectedCode] : null;
        selectedGroupIdEl.value = (mappedId != null ? String(mappedId) : (user.ownerId != null ? String(user.ownerId) : ''));
      }

      const pwdCtl = uPassword ? uPassword.closest('.form-control') : null;
      const teamCtl = uTeamCode ? uTeamCode.closest('.form-control') : null;

      if (user.isGroup) {
        if (pwdCtl) pwdCtl.classList.add('hidden');
        if (teamCtl) teamCtl.classList.add('hidden');
        if (uPassword) uPassword.value = '';
        const tmpChk = document.getElementById('uIsTempPassword');
        if (tmpChk) { tmpChk.checked = false; tmpChk._original = false; tmpChk.closest && tmpChk.closest('.form-control') && tmpChk.closest('.form-control').classList.add('hidden'); }
      } else {
        if (pwdCtl) pwdCtl.classList.remove('hidden');
        if (teamCtl) teamCtl.classList.remove('hidden');
        if (uPassword) uPassword.value = '••••••••';
        const tmpChk = document.getElementById('uIsTempPassword');
        if (tmpChk) { tmpChk.checked = !!user.isTempPassword; tmpChk._original = !!user.isTempPassword; tmpChk.closest && tmpChk.closest('.form-control') && tmpChk.closest('.form-control').classList.remove('hidden'); }
      }
    } else {
      if (uCode) uCode.value = '';
      if (uRole) uRole.value = '5';
      if (uFirst) uFirst.value = '';
      if (uLast) uLast.value = '';
      if (uEmail) uEmail.value = '';
      if (uPhone) uPhone.value = '';
      if (uTeamCode) uTeamCode.value = '';
      if (selectedGroupIdEl) selectedGroupIdEl.value = '';
      if (uPassword) uPassword.value = '';

      const tmpChk = document.getElementById('uIsTempPassword');
      if (tmpChk) { tmpChk.checked = false; tmpChk._original = false; tmpChk.closest && tmpChk.closest('.form-control') && tmpChk.closest('.form-control').classList.remove('hidden'); }

      const pwdCtl = uPassword ? uPassword.closest('.form-control') : null;
      const teamCtl = uTeamCode ? uTeamCode.closest('.form-control') : null;
      if (pwdCtl) pwdCtl.classList.remove('hidden');
      if (teamCtl) teamCtl.classList.remove('hidden');
    }

    if (uRole) {
      const adminOpt = Array.from(uRole.options).find(o => o.value === '8');
      if (adminOpt) adminOpt.disabled = isManager;
    }

    // Keep ownerId in sync with team code selection
    if (uTeamCode && !uTeamCode._bound) {
      uTeamCode._bound = true;
      uTeamCode.addEventListener('change', () => {
        const code = uTeamCode.value || '';
        const gid = code ? groupCodeToId[code] : null;
        const hidden = document.getElementById('uOwnerId');
        if (hidden) hidden.value = gid != null ? String(gid) : '';
      });
    }

    try { modal?.showModal(); } catch { modal?.classList.remove('hidden'); }

    const cancel = document.getElementById('userCancel');
    const save = document.getElementById('userSave');

    if (cancel && !cancel._bound) {
      cancel._bound = true;
      cancel.addEventListener('click', (e) => {
        e.preventDefault();
        try { modal?.close(); } catch { modal?.classList.add('hidden'); }
      });
    }

    if (save && !save._bound) {
      save._bound = true;
      save.addEventListener('click', async (e) => {
        e.preventDefault();

        const payload = {
          Code: uCode?.value.trim() ?? '',
          FirstName: uFirst?.value.trim() ?? '',
          LastName: uLast?.value.trim() ?? '',
          Email: uEmail?.value.trim() ?? '',
          Phone: uPhone?.value.trim() ?? '',
          RoleId: parseInt(uRole?.value ?? '5', 10),
          IsGroup: false,
          OwnerId: (document.getElementById('uOwnerId')?.value ? parseInt(document.getElementById('uOwnerId').value, 10) : 0),
          City: null, StreetAddress: null, PostalCode: null,
          Password: uPassword?.value ?? ''
        };

        const userId = localStorage.getItem('userId') ?? 2;

        try {
            if (selectedUserId) {
            const user = items.find(x => x.id === selectedUserId);
            const updatePayload = {
              Code: payload.Code,
              FirstName: payload.FirstName,
              LastName: payload.LastName,
              Email: payload.Email,
              Phone: payload.Phone,
              RoleId: payload.RoleId,
              OwnerId: payload.OwnerId,
              Stamp: user ? user.stamp : 0
            };
            // If the password field was changed from the placeholder, include it in the update.
            // This ensures an explicitly-cleared password (empty string) is sent so server validation runs.
            if (payload.Password !== '••••••••') {
              updatePayload.Password = payload.Password;
            }
            // include IsTempPassword only if admin toggled it (explicit change)
            const tmpChk = document.getElementById('uIsTempPassword');
            if (tmpChk && typeof tmpChk._original !== 'undefined') {
              const current = !!tmpChk.checked;
              if (current !== !!tmpChk._original) updatePayload.IsTempPassword = current;
            }

            const res = await fetch(`/api/users/${user.id}?userId=${userId}`, {
              method: 'PUT',
              credentials: 'include',
              headers: { 'Content-Type': 'application/json', 'Accept': 'application/json' },
              body: JSON.stringify(updatePayload)
            });
            const data = await res.json().catch(() => ({ success: res.ok }));

            if (res.ok && data && data.success) {
              showToast('info', 'User has been updated successfully.');
              try { modal?.close(); } catch { modal?.classList.add('hidden'); }
              await loadUsers();
            } else {
              const msg = (data && data.message) ? data.message : 'Failed to update user. Please try again later.';
              showToast('error', msg);
            }
          } else {
            // Client-side validation: mirror server password policy to provide immediate feedback
            try {
              const pw = payload.Password || '';
              if (pw.length < 8 || !/[A-Z]/.test(pw) || !/\d/.test(pw)) {
                showToast('error', 'Password must be at least 8 characters long and include upper and lower case letters and at least one number.');
                return;
              }
            } catch (e) { /* ignore and continue to server validation */ }

            const res = await fetch(`/api/users?userId=${userId}`, {
              method: 'POST',
              credentials: 'include',
              headers: { 'Content-Type': 'application/json', 'Accept': 'application/json' },
              body: JSON.stringify(Object.assign({}, payload, { IsTempPassword: (document.getElementById('uIsTempPassword')?.checked ? true : false) }))
            });
            const data = await res.json().catch(() => ({ success: res.ok }));

            if (res.ok && data && data.success) {
              showToast('info', 'User has been created successfully.');
              try { modal?.close(); } catch { modal?.classList.add('hidden'); }
              await loadUsers();
            } else {
              const msg = (data && data.message) ? data.message : 'Failed to create user. Please try again later.';
              showToast('error', msg);
            }
          }
        } catch (err) {
          showToast('error', 'Failed to save changes. Please try again later.');
        }
      });
    }
  }

  function openTeamModal() {
    const modal = document.getElementById('teamModal');
    const tCode = document.getElementById('tCode');
    const tRole = document.getElementById('tRole');
    const tFull = document.getElementById('tFullName');
    const tEmail = document.getElementById('tEmail');
    const tPhone = document.getElementById('tPhone');

    if (tCode) tCode.value = '';
    if (tRole) tRole.value = '6';
    if (tFull) tFull.value = '';
    if (tEmail) tEmail.value = '';
    if (tPhone) tPhone.value = '';

    try { modal?.showModal(); } catch { modal?.classList.remove('hidden'); }

    const cancel = document.getElementById('teamCancel');
    const save = document.getElementById('teamSave');

    if (cancel && !cancel._bound) {
      cancel._bound = true;
      cancel.addEventListener('click', (e) => {
        e.preventDefault();
        try { modal?.close(); } catch { modal?.classList.add('hidden'); }
      });
    }

    if (save && !save._bound) {
      save._bound = true;
      save.addEventListener('click', async (e) => {
        e.preventDefault();

        const full = (tFull?.value || '').trim();
        const payload = {
          Code: (tCode?.value || '').trim(),
          FirstName: full,
          LastName: full,
          Email: (tEmail?.value || '').trim(),
          Phone: (tPhone?.value || '').trim(),
          RoleId: parseInt(tRole?.value || '6', 10),
          IsGroup: true,
          OwnerId: 0,
          City: null, StreetAddress: null, PostalCode: null,
          Password: ''
        };

        const userId = localStorage.getItem('userId') ?? 2;

        try {
          const res = await fetch(`/api/users?userId=${userId}`, {
            method: 'POST',
            credentials: 'include',
            headers: { 'Content-Type': 'application/json', 'Accept': 'application/json' },
            body: JSON.stringify(payload)
          });
          const data = await res.json().catch(() => ({ success: res.ok }));

          if (res.ok && data && data.success) {
            showToast('info', 'Team has been created successfully.');
            try { modal?.close(); } catch { modal?.classList.add('hidden'); }
            await loadUsers();
          } else {
            const msg = (data && data.message) ? data.message : 'Failed to create the team. Please try again later.';
            showToast('error', msg);
          }
        } catch (err) {
          showToast('error', 'Failed to create the team. Please try again later.');
        }
      });
    }
  }

  function renderPagination() {
    const idx = document.getElementById('pgIndex'); // optional
    const info = document.getElementById('pgInfo'); // legacy Users wording
    const prev = document.getElementById('pgPrev') || document.getElementById('prevPage');
    const next = document.getElementById('pgNext') || document.getElementById('nextPage');
    const sizeEl = document.getElementById('pgSize') || document.getElementById('pageSize');
    const pageInfoEl = document.getElementById('pageInfo'); // EventLog wording
    const pageCountInfoEl = document.getElementById('pageCountInfo'); // EventLog total counter

    const size = Number(pageSize || (sizeEl ? Number(sizeEl.value || 10) : 10));
    const count = Number(totalCount || 0);
    const pages = Math.max(1, Number(totalPages || Math.ceil(count / size) || 1));

    if (page > pages) page = pages;
    if (page < 1) page = 1;
    if (idx) idx.value = String(page);

    const start = count === 0 ? 0 : (page - 1) * size + 1;
    const end = count === 0 ? 0 : Math.min(page * size, count);

    if (info) info.innerHTML = `<b>${start}</b>–<b>${end}</b> of <b>${count}</b> · pages <b>${pages}</b>`;
    if (pageInfoEl) pageInfoEl.innerHTML = `Page <b>${page}</b> of <b>${pages}</b>`;
    if (pageCountInfoEl) pageCountInfoEl.textContent = `Total records: ${count}`;

    const disable = (btn, v) => { if (btn) btn.disabled = !!v; };
    disable(prev, page <= 1 || count === 0);
    disable(next, page >= pages || count === 0);
  }

  function showAccessDeniedInline() {
    const main = document.querySelector('main');
    if (main) main.classList.add('hidden');

    const container = document.createElement('section');
    container.className = 'container mx-auto p-6';
    container.innerHTML = `
      <div class="bg-base-100 rounded-xl shadow p-8 text-center">
        <div class="flex items-center justify-center gap-2 mb-3">
          <span class="material-icons text-rose-600">block</span>
          <h2 class="text-2xl font-bold">Access Denied</h2>
        </div>
        <p class="text-gray-700">You are not allowed to see this page.</p>
      </div>`;
    document.body.appendChild(container);
  }

  // ---- DOM wiring ----
  document.addEventListener('DOMContentLoaded', () => {
    // Search/Clear
    document.getElementById('btnSearch')?.addEventListener('click', (e) => { e.preventDefault(); page = 1; loadUsers(); });
    document.getElementById('btnClear')?.addEventListener('click', (e) => {
      e.preventDefault();
      const n = document.getElementById('fltName'); if (n) n.value = '';
      const em = document.getElementById('fltEmail'); if (em) em.value = '';
      const t = document.getElementById('fltTeam'); if (t) t.value = '';
      page = 1; loadUsers();
    });
    ['fltName', 'fltEmail', 'fltTeam'].forEach(id => {
      const el = document.getElementById(id);
      if (el && !el._boundEnter) {
        el._boundEnter = true;
        el.addEventListener('keydown', (e) => { if (e.key === 'Enter') { e.preventDefault(); page = 1; loadUsers(); } });
      }
    });

    const menuContainer = document.getElementById('toolbarMenu');
    const menuButton   = document.getElementById('btnToolbarMenu');
    if (menuButton) { try { menuButton.disabled = false; } catch {} }

    document.getElementById('btnNewUser')?.addEventListener('click', () => {
      // Ensure Add User always opens a blank form and does not reuse any selection
      selectedUserId = null;
      try { renderTable(); } catch { }
      try { updateOpenState(); } catch { }
      try { updateMenuActionsState(); } catch { }
      openUserModal(null);
    });
    document.getElementById('btnNewTeam')?.addEventListener('click', () => openTeamModal());
    document.getElementById('btnOpen')?.addEventListener('click', () => {
      if (!selectedUserId) { try { showToast('warning', 'Please select a user before proceeding.'); } catch { } return; }
      const u = items.find(x => x.id === selectedUserId);
      openUserModal(u);
    });

    // Pagination
    const prevBtn = document.getElementById('pgPrev') || document.getElementById('prevPage');
    const nextBtn = document.getElementById('pgNext') || document.getElementById('nextPage');

    prevBtn?.addEventListener('click', () => { if (page > 1) { page--; loadUsers(); } });
    nextBtn?.addEventListener('click', () => {
      const sizeSel = document.getElementById('pgSize') || document.getElementById('pageSize');
      const size = Number(sizeSel?.value || pageSize);
      const pages = Math.max(1, Number(totalPages || Math.ceil((totalCount || 0) / size) || 1));
      if (page < pages) { page++; loadUsers(); }
    });

    document.getElementById('pgIndex')?.addEventListener('change', (e) => {
      const sizeSel = document.getElementById('pgSize') || document.getElementById('pageSize');
      const size = Number(sizeSel?.value || pageSize);
      const pages = Math.max(1, Number(totalPages || Math.ceil((totalCount || 0) / size) || 1));
      const v = parseInt(e.target.value, 10) || 1;
      page = Math.min(Math.max(1, v), pages);
      loadUsers();
    });

    const sizeEl = document.getElementById('pgSize') || document.getElementById('pageSize');
    sizeEl?.addEventListener('change', (e) => {
      pageSize = parseInt(e.target.value, 10) || 10;
      page = 1;
      loadUsers();
    });

    // Recompute enabled state when toolbar menu opens
    try {
      menuContainer?.addEventListener('oc-toolbar-toggle', (ev) => {
        try { if (ev?.detail?.open) { updateOpenState(); updateMenuActionsState(); } } catch {}
      });
    } catch {}

    // Bind toolbar actions once: close menu, then execute page handlers
    const dd = document.getElementById('toolbarMenuContent');
    const aDeactivate = document.getElementById('btnDeactivate');
    const aDelete = document.getElementById('btnDelete');
    if (aDeactivate && !aDeactivate._oc_bound) {
      aDeactivate._oc_bound = true;
      aDeactivate.addEventListener('click', (e) => {
        try { window._oc_toolbar_closeAll && window._oc_toolbar_closeAll(); } catch {}
        deactivateSelected();
      });
    }
    if (aDelete && !aDelete._oc_bound) {
      aDelete._oc_bound = true;
      aDelete.addEventListener('click', (e) => {
        try { window._oc_toolbar_closeAll && window._oc_toolbar_closeAll(); } catch {}
        deleteSelected();
      });
    }

    // Init: load groups for dropdown and mapping, then users
    loadGroups().finally(() => loadUsers());
  });

  // per-row menu removed: toolbar menu controls actions for selected row

  async function deactivateSelected() {
    if (!selectedUserId) { showToast('warning', 'Please select a user before proceeding.'); return; }
    const u = items.find(x => x.id === selectedUserId);
    if (!u) { showToast('error', 'User not found. Please refresh the list and try again.'); return; }

    try {
      const userId = localStorage.getItem('userId') ?? 2;
      const action = u.isActive ? 'deactivate' : 'activate';
      const res = await fetch(`/api/users/${u.id}/${action}?userId=${userId}&stamp=${encodeURIComponent(u.stamp ?? 0)}`, {
        method: 'POST',
        credentials: 'include',
        headers: { 'Accept': 'application/json' }
      });
      if (res.ok) {
          showToast('info', (u.isActive ? 'User has been deactivated successfully.' : 'User has been activated successfully.'));
          // Clear selection after action per requirements
          selectedUserId = null;
          try { renderTable(); } catch {}
          try { updateOpenState(); } catch {}
          try { updateMenuActionsState(); } catch {}
          await loadUsers();
      } else {
        showToast('error', 'Failed to change user status. Please try again later.');
      }
    } catch {
      showToast('error', 'Failed to change user status. Please try again later.');
    }
  }

  async function deleteSelected() {
    if (!selectedUserId) { showToast('warning', 'Please select a user before proceeding.'); return; }
    const u = items.find(x => x.id === selectedUserId);
    if (!u) { showToast('error', 'User not found. Please refresh the list and try again.'); return; }
    const dlg = document.getElementById('userDeleteConfirm');
    const okBtn = document.getElementById('userConfirmOk');
    if (!dlg || !okBtn || !dlg.showModal) {
      if (!confirm('Delete the selected user? This action cannot be undone.')) return;
    } else {
      let resolved = false;
      const onOk = (e) => { e.preventDefault(); resolved = true; try { dlg.close(); } catch {} };
      okBtn.addEventListener('click', onOk, { once: true });
      dlg.addEventListener('close', () => {
        if (!resolved) return;
        proceedDelete();
      }, { once: true });
      try { dlg.showModal(); } catch { if (!confirm('Delete the selected user? This action cannot be undone.')) return; }
      if (!dlg.open && !resolved) return;
      if (!dlg.open && resolved) return;
      return;
    }

    try {
      const userId = localStorage.getItem('userId') ?? 2;
      const res = await fetch(`/api/users/${u.id}/delete?userId=${userId}`, {
        method: 'POST',
        credentials: 'include',
        headers: { 'Accept': 'application/json' }
      });
      if (res.ok) {
        showToast('info', 'User has been deleted successfully.');
        selectedUserId = null;
        await loadUsers();
      } else {
        showToast('error', 'Failed to delete user. Please try again later.');
      }
    } catch {
      showToast('error', 'Failed to delete user. Please try again later.');
    }

    function proceedDelete() {
      (async function() {
        try {
          const userId = localStorage.getItem('userId') ?? 2;
          const res = await fetch(`/api/users/${u.id}/delete?userId=${userId}`, {
            method: 'POST',
            credentials: 'include',
            headers: { 'Accept': 'application/json' }
          });
          if (res.ok) {
            showToast('info', 'User has been deleted successfully.');
            selectedUserId = null;
            await loadUsers();
          } else {
            showToast('error', 'Failed to delete user. Please try again later.');
          }
        } catch {
          showToast('error', 'Failed to delete user. Please try again later.');
        }
      })();
    }
  }
})();