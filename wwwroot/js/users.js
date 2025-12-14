(function(){
  let selectedUserId = null;
  let items = [];
  let page = 1;
  let pageSize = 10;
  let totalPages = 1;
  let totalCount = 0;

  function roleName(id){
    switch(id){
      case 8: return 'Administrator';
      case 7: return 'Manager';
      case 6: return 'Worker';
      case 5: return 'Customer';
      default: return String(id);
    }
  }

  async function loadUsers(){
    try {
      const name = document.getElementById('fltName')?.value.trim() || '';
      const team = document.getElementById('fltTeam')?.value.trim() || '';
      const userId = localStorage.getItem('userId') || 2;
      const res = await fetch(`/api/users?name=${encodeURIComponent(name)}&team=${encodeURIComponent(team)}&page=${page}&pageSize=${pageSize}&userId=${userId}`);
      const data = await res.json();
      items = data.items || [];
      totalPages = data.totalPages || 1;
      totalCount = data.totalCount || items.length;
      renderTable();
      renderPagination();
    } catch (err) {
      showToast('error', 'Failed to load users');
    }
  }

  function renderTable(){
    const body = document.getElementById('usersBody');
    body.innerHTML = '';
    items.forEach(u => {
      const tr = document.createElement('tr');
      tr.className = selectedUserId === u.id ? 'active' : '';
      tr.addEventListener('click', () => { selectedUserId = u.id; renderTable(); updateOpenState(); updateDeactivateLabel(u); });
      const status = u.isDeleted ? 'Deleted' : (u.isActive ? 'Active' : 'Inactive');
      tr.innerHTML = `
        <td>${u.id}</td>
        <td>${u.code || ''}</td>
        <td>${u.firstName || ''}</td>
        <td>${roleName(u.roleId)}</td>
        <td>${u.team || ''}</td>
        <td>${status}</td>
        <td></td>
      `;
      body.appendChild(tr);
    });
    updateOpenState();
  }

  function updateOpenState(){
    const btn = document.getElementById('btnOpen');
    btn.disabled = !selectedUserId;
  }

  function updateDeactivateLabel(user){
    const link = document.getElementById('btnDeactivate');
    if (!link) return;
    const isActive = user ? user.isActive : false;
    link.innerHTML = `<span class="material-icons mr-2">${isActive ? 'block' : 'check_circle'}</span>${isActive ? 'Deactivate' : 'Activate'}`;
  }

  function openUserModal(user){
    const modal = document.getElementById('userModal');
    document.getElementById('userModalTitle').textContent = user ? 'Edit User' : 'New User';
    const isManager = parseInt(localStorage.getItem('roleId') || '0', 10) === 7;
    const uCode = document.getElementById('uCode');
    const uRole = document.getElementById('uRole');
    const uFirst = document.getElementById('uFirst');
    const uLast = document.getElementById('uLast');
    const uEmail = document.getElementById('uEmail');
    const uPhone = document.getElementById('uPhone');
    const uTeamCode = document.getElementById('uTeamCode');
    const uPassword = document.getElementById('uPassword');

    if (user){
      uCode.value = user.code || '';
      uRole.value = String(user.roleId || 5);
      uFirst.value = user.firstName || '';
      uLast.value = user.lastName || '';
      uEmail.value = user.email || '';
      uPhone.value = user.phone || '';
      uTeamCode.value = user.team || '';
      // Show masked password dots when editing existing user
      uPassword.value = '••••••••';
    } else {
      uCode.value = '';
      uRole.value = '5';
      uFirst.value = '';
      uLast.value = '';
      uEmail.value = '';
      uPhone.value = '';
      uTeamCode.value = '';
      uPassword.value = '';
    }

    // Managers cannot set Administrator
    const adminOpt = Array.from(uRole.options).find(o => o.value === '8');
    if (adminOpt) adminOpt.disabled = isManager;

    try { modal.showModal(); } catch { modal.classList.remove('hidden'); }

    const cancel = document.getElementById('userCancel');
    const save = document.getElementById('userSave');
    if (!cancel._bound){
      cancel._bound = true;
      cancel.addEventListener('click', (e)=>{ e.preventDefault(); try { modal.close(); } catch { modal.classList.add('hidden'); } });
    }
    if (!save._bound){
      save._bound = true;
      save.addEventListener('click', async (e)=>{
        e.preventDefault();
        const payload = {
          Code: uCode.value.trim(),
          FirstName: uFirst.value.trim(),
          LastName: uLast.value.trim(),
          Email: uEmail.value.trim(),
          Phone: uPhone.value.trim(),
          RoleId: parseInt(uRole.value, 10),
          IsGroup: false,
          OwnerId: 0,
          City: null,
          StreetAddress: null,
          PostalCode: null,
          Password: uPassword.value
        };
        const userId = localStorage.getItem('userId') || 2;
        try{
          if (user){
            // If password field still shows masked dots, do not send password update
            const updatePayload = { FirstName: payload.FirstName, LastName: payload.LastName, Email: payload.Email, Phone: payload.Phone, RoleId: payload.RoleId };
            if (payload.Password && payload.Password !== '••••••••') {
              updatePayload.Password = payload.Password;
            }
            const res = await fetch(`/api/users/${user.id}?userId=${userId}`, { method: 'PUT', headers: { 'Content-Type':'application/json' }, body: JSON.stringify(updatePayload) });
            if (res.ok){ showToast('info','User updated'); try { modal.close(); } catch { modal.classList.add('hidden'); } await loadUsers(); }
            else { showToast('error','Update failed'); }
          } else {
            const res = await fetch(`/api/users?userId=${userId}`, { method: 'POST', headers: { 'Content-Type':'application/json' }, body: JSON.stringify(payload) });
            if (res.ok){ showToast('info','User created'); try { modal.close(); } catch { modal.classList.add('hidden'); } await loadUsers(); }
            else { showToast('error','Create failed'); }
          }
        } catch(err){ showToast('error','Save failed'); }
      });
    }
  }

  function openTeamModal(){
    const modal = document.getElementById('teamModal');
    const tCode = document.getElementById('tCode');
    tCode.value = '';
    try { modal.showModal(); } catch { modal.classList.remove('hidden'); }
    const cancel = document.getElementById('teamCancel');
    const save = document.getElementById('teamSave');
    if (!cancel._bound){ cancel._bound = true; cancel.addEventListener('click',(e)=>{ e.preventDefault(); try { modal.close(); } catch { modal.classList.add('hidden'); } }); }
    if (!save._bound){
      save._bound = true;
      save.addEventListener('click', async (e)=>{
        e.preventDefault();
        const payload = { Code: tCode.value.trim(), FirstName: '', LastName: '', Email: '', Phone: '', RoleId: 6, IsGroup: true, OwnerId: 0, City: null, StreetAddress: null, PostalCode: null, Password: '' };
        const userId = localStorage.getItem('userId') || 2;
        try{
          const res = await fetch(`/api/users?userId=${userId}`, { method: 'POST', headers: { 'Content-Type':'application/json' }, body: JSON.stringify(payload) });
          if (res.ok){ showToast('info','Team created'); try { modal.close(); } catch { modal.classList.add('hidden'); } await loadUsers(); }
          else { showToast('error','Create failed'); }
        } catch(err){ showToast('error','Create team failed'); }
      });
    }
  }

  async function deactivateSelected(){
    if (!selectedUserId){ showToast('warning','Select a user first'); return; }
    const userId = localStorage.getItem('userId') || 2;
    const u = items.find(x => x.id === selectedUserId);
    try{
      const url = u && u.isActive ? `/api/users/${selectedUserId}/deactivate?userId=${userId}` : `/api/users/${selectedUserId}/activate?userId=${userId}`;
      const res = await fetch(url, { method: 'POST' });
      if (res.ok){ showToast('info', u && u.isActive ? 'User deactivated' : 'User activated'); await loadUsers(); }
      else { showToast('error','Deactivate failed'); }
    } catch(err){ showToast('error','Deactivate failed'); }
  }

  async function deleteSelected(){
    if (!selectedUserId){ showToast('warning','Select a user first'); return; }
    const userId = localStorage.getItem('userId') || 2;
    try{
      const res = await fetch(`/api/users/${selectedUserId}/delete?userId=${userId}`, { method: 'POST' });
      if (res.ok){ showToast('info','User deleted'); await loadUsers(); }
      else { showToast('error','Delete failed'); }
    } catch(err){ showToast('error','Delete failed'); }
  }

  document.addEventListener('DOMContentLoaded', () => {
    document.getElementById('btnSearch')?.addEventListener('click', loadUsers);
    ['fltName','fltTeam'].forEach(id => {
      const el = document.getElementById(id);
      if (el && !el._boundEnter){ el._boundEnter = true; el.addEventListener('keydown', (e)=>{ if (e.key === 'Enter') { e.preventDefault(); loadUsers(); } }); }
    });
    document.getElementById('btnClear')?.addEventListener('click', () => { document.getElementById('fltName').value=''; document.getElementById('fltTeam').value=''; loadUsers(); });
    document.getElementById('btnNewUser')?.addEventListener('click', () => openUserModal(null));
    document.getElementById('btnNewTeam')?.addEventListener('click', openTeamModal);
    document.getElementById('btnDeactivate')?.addEventListener('click', deactivateSelected);
    document.getElementById('btnDelete')?.addEventListener('click', deleteSelected);
    document.getElementById('btnOpen')?.addEventListener('click', () => {
      if (!selectedUserId) { showToast('warning','Select a user first'); return; }
      const u = items.find(x => x.id === selectedUserId);
      openUserModal(u);
    });
    document.getElementById('pgPrev')?.addEventListener('click', () => { if (page > 1) { page--; loadUsers(); } });
    document.getElementById('pgNext')?.addEventListener('click', () => { if (page < totalPages) { page++; loadUsers(); } });
    document.getElementById('pgIndex')?.addEventListener('change', (e) => { const v = parseInt(e.target.value, 10) || 1; page = Math.min(Math.max(1, v), totalPages); loadUsers(); });
    document.getElementById('pgSize')?.addEventListener('change', (e) => { pageSize = parseInt(e.target.value, 10) || 10; page = 1; loadUsers(); });
    loadUsers();
  });

  function renderPagination(){
    const idx = document.getElementById('pgIndex');
    const info = document.getElementById('pgInfo');
    if (idx) idx.value = String(page);
    const start = (page - 1) * pageSize + 1;
    const end = Math.min(page * pageSize, totalCount);
    if (info) info.textContent = `${start}-${end} of ${totalCount} · pages ${totalPages}`;
  }
})();
