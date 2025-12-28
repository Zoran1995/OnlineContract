
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
      const teamEl = document.getElementById('fltTeam');
      const name = (nameEl?.value || '').trim();
      const team = (teamEl?.value || '').trim();

      // Read pageSize from the select if it exists
      const sizeEl = document.getElementById('pgSize') || document.getElementById('pageSize');
      if (sizeEl) pageSize = parseInt(sizeEl.value || pageSize, 10) || pageSize;

      const userId = localStorage.getItem('userId') ?? 2;
      const res = await fetch(`/api/users?name=${encodeURIComponent(name)}&team=${encodeURIComponent(team)}&page=${page}&pageSize=${pageSize}&userId=${userId}`, {
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
        status,
        '' // actions placeholder
      ];

      body.appendChild(tr);
      cells.forEach(text => {
        const td = document.createElement('td');
        td.textContent = text;
        tr.appendChild(td);
      });
    });
    try { if (window.attachTableSort) window.attachTableSort('#usersTable'); } catch {}
  }

  function updateOpenState() {
    const btn = document.getElementById('btnOpen');
    if (!btn) return;
    btn.disabled = !selectedUserId;
  }

  function updateMenuActionsState() {
    const deactivateLink = document.getElementById('btnDeactivate');
    const deleteLink = document.getElementById('btnDelete');
    const deactivateItem = deactivateLink ? deactivateLink.closest('li') : null;
    const hasSelection = !!selectedUserId;

    if (deactivateItem) {
      deactivateItem.classList.toggle('hidden', !hasSelection);
    }

    if (deleteLink) {
      if (!hasSelection) {
        deleteLink.setAttribute('aria-disabled', 'true');
        deleteLink.classList.add('pointer-events-none', 'opacity-50');
      } else {
        deleteLink.removeAttribute('aria-disabled');
        deleteLink.classList.remove('pointer-events-none', 'opacity-50');
      }
    }
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

    if (title) title.textContent = user ? 'Edit User' : 'New User';
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
      } else {
        if (pwdCtl) pwdCtl.classList.remove('hidden');
        if (teamCtl) teamCtl.classList.remove('hidden');
        if (uPassword) uPassword.value = '••••••••';
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
            const res = await fetch(`/api/users?userId=${userId}`, {
              method: 'POST',
              credentials: 'include',
              headers: { 'Content-Type': 'application/json', 'Accept': 'application/json' },
              body: JSON.stringify(payload)
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

    if (info) info.textContent = `${start}–${end} of ${count} · pages ${pages}`;
    if (pageInfoEl) pageInfoEl.textContent = `Page ${page} of ${pages}`;
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
      const t = document.getElementById('fltTeam'); if (t) t.value = '';
      page = 1; loadUsers();
    });
    ['fltName', 'fltTeam'].forEach(id => {
      const el = document.getElementById(id);
      if (el && !el._boundEnter) {
        el._boundEnter = true;
        el.addEventListener('keydown', (e) => { if (e.key === 'Enter') { e.preventDefault(); page = 1; loadUsers(); } });
      }
    });

    const menuContainer = document.getElementById('toolbarMenu');
    const menuButton   = document.getElementById('btnToolbarMenu');

    function setMenuOpen(open) {
      if (!menuContainer || !menuButton) return;
      const content = document.getElementById('toolbarMenuContent');
      menuContainer.classList.toggle('dropdown-open', !!open);
      menuButton.setAttribute('aria-expanded', open ? 'true' : 'false');
      if (content) content.classList.toggle('hidden', !open);
    }

    menuContainer?.querySelector('.dropdown-content')
      ?.addEventListener('click', () => setMenuOpen(false))
    
    menuButton?.addEventListener('click', (e) => {
      e.preventDefault();
      e.stopPropagation();
      const isOpen = menuButton?.getAttribute('aria-expanded') === 'true';
      setMenuOpen(!isOpen);
    }); 

    // Close on outside click

    document.addEventListener('pointerdown', (e) => {
    if (!menuContainer) return;
      if (!menuContainer.contains(e.target)) {
        setMenuOpen(false);
      }
    });

    // Close on Escape
    document.addEventListener('keydown', (e) => {
      if (e.key === 'Escape') setMenuOpen(false);
    });

    document.getElementById('btnDeactivate')?.addEventListener('click', () => { setMenuOpen(false); deactivateSelected(); });
    document.getElementById('btnDelete')?.addEventListener('click',     () => { setMenuOpen(false); deleteSelected(); });

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

    // Init: load groups for dropdown and mapping, then users
    loadGroups().finally(() => loadUsers());
  });

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
        showToast('info', 'User deleted');
        selectedUserId = null;
        await loadUsers();
      } else {
        showToast('error', 'Delete failed');
      }
    } catch {
      showToast('error', 'Delete failed');
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
            showToast('info', 'User deleted');
            selectedUserId = null;
            await loadUsers();
          } else {
            showToast('error', 'Delete failed');
          }
        } catch {
          showToast('error', 'Delete failed');
        }
      })();
    }
  }
})();