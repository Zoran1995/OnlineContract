(() => {
  const apiBase = "/api/tasks";
  let page = 1;
  let pageSize = 10;
  let totalPages = 0;
  let totalCount = 0;
  let items = [];
  let selectedId = null;
  let sortBy = null;
  let sortDir = 'asc';

  const filterTaskId = document.getElementById('filterTaskId');
  const filterContractId = document.getElementById('filterContractId');
  const filterPriority = document.getElementById('filterPriority');
  const filterStatus = document.getElementById('filterStatus');
  const btnSearch = document.getElementById('btnSearch');
  const btnClean = document.getElementById('btnClean');
  const btnOpen = document.getElementById('btnOpen');

  const tbody = document.querySelector('#grid tbody');
  const pageInfo = document.getElementById('pageInfo');
  const pageCountInfo = document.getElementById('pageCountInfo');
  const prevPage = document.getElementById('prevPage');
  const nextPage = document.getElementById('nextPage');
  const pageSizeSel = document.getElementById('pageSize');

  const modal = document.getElementById('taskModal');
  const taskModalTitle = document.getElementById('taskModalTitle');
  const tSubject = document.getElementById('tSubject');
  const tComments = document.getElementById('tComments');
  const tPrioritySel = document.getElementById('tPrioritySel');
  const tStatusBtn = document.getElementById('tStatusBtn');
  const tStatusLbl = document.getElementById('tStatusLbl');
  const tEntryDate = document.getElementById('tEntryDate');
  const tContractIdLink = document.getElementById('tContractIdLink');
  const tContractIdText = document.getElementById('tContractIdText');
  const tInitiatedBy = document.getElementById('tInitiatedBy');
  const tClose = document.getElementById('tClose');
  const tSave = document.getElementById('tSave');

  // Status modal elements
  const statusModal = document.getElementById('taskStatusModal');
  const tsCurrent = document.getElementById('tsCurrent');
  const tsNext = document.getElementById('tsNext');
  const tsComment = document.getElementById('tsComment');
  const tsCancel = document.getElementById('tsCancel');
  const tsOk = document.getElementById('tsOk');

  function showLoading(){ document.getElementById('loadingOverlay')?.classList.remove('hidden'); }
  function hideLoading(){ document.getElementById('loadingOverlay')?.classList.add('hidden'); }

  function updateActions(){
    const row = getSelectedRow();
    // Disable Open button if no selection or if task is assigned to system user (id=2)
    // System tasks (like EOM Report) are assigned to user 2 and cannot be edited by users
    const isSystemTask = row && (row.assignedToUserId === 2 || row.initiatedByUserId === 2);
    btnOpen.disabled = !selectedId || isSystemTask;
  }

  function getSelectedRow() {
    return (items || []).find(x => x.id === selectedId) || null;
  }

  async function loadLookups(){
    try {
      const res = await fetch(`${apiBase}/lookups`, { credentials: 'include', headers: { 'Accept': 'application/json' }});
      if (res.status === 401){ window.location.href = '/login?mode=login'; return; }
      const data = await res.json();
      const pr = Array.isArray(data.priority) ? data.priority : [];
      const st = Array.isArray(data.status) ? data.status : [];
      filterPriority.innerHTML = '<option value="">-- Any --</option>' + pr.map(p => `<option value="${p.id}">${p.name}</option>`).join('');
      filterStatus.innerHTML = '<option value="">-- Any --</option>' + st.map(s => `<option value="${s.id}">${s.name}</option>`).join('');
      // Fill modal priority select
      if (tPrioritySel) tPrioritySel.innerHTML = pr.map(p => `<option value="${p.id}">${p.name}</option>`).join('');
    } catch {}
  }

  async function search(skipValidation = false){
    // Require at least one filter (unless auto-search from URL params)
    const idVal = (filterTaskId.value || '').trim();
    const cVal = (filterContractId?.value || '').trim();
    const pr = (filterPriority.value || '').trim();
    const st = (filterStatus.value || '').trim();
    if (!skipValidation && !idVal && !cVal && !pr && !st) {
      try { showToast('warning', 'Please enter at least one search criteria.'); } catch {}
      return;
    }
    try {
      showLoading();
      selectedId = null; updateActions();
      const params = new URLSearchParams();
      if (idVal) params.set('taskId', idVal);
      if (cVal) params.set('contractId', cVal);
      if (pr) params.set('priorityId', pr);
      if (st) params.set('statusId', st);
      params.set('page', String(page)); params.set('pageSize', String(pageSize));
      if (sortBy){ params.set('sortBy', sortBy); params.set('sortDir', sortDir); }
      const res = await fetch(`${apiBase}?${params.toString()}`, { credentials: 'include', headers: { 'Accept': 'application/json' }});
      if (res.status === 401){ window.location.href = '/login?mode=login'; return; }
      if (!res.ok) throw new Error(`HTTP ${res.status}`);
      const data = await res.json();
      items = Array.isArray(data.items) ? data.items : [];
      totalPages = Number(data.totalPages || 0);
      totalCount = Number(data.totalCount || items.length);
      renderRows(); renderPager(); updateActions();
    } catch (err) {
      try { showToast('error', 'Search failed'); } catch {}
    } finally { hideLoading(); }
  }

  function renderRows(){
    tbody.innerHTML = '';
    if (!items.length){
      const tr = document.createElement('tr'); const td = document.createElement('td'); td.colSpan = 8; td.className = 'empty'; td.textContent = 'No results.'; tr.appendChild(td); tbody.appendChild(tr); return;
    }
    const rows = items.slice();
    rows.forEach(it => {
      const tr = document.createElement('tr');
      tr.addEventListener('click', () => {
        Array.from(tbody.querySelectorAll('tr.active')).forEach(r => r.classList.remove('active'));
        tr.classList.add('active'); selectedId = it.id; updateActions();
      });
      if (selectedId && it.id === selectedId) tr.classList.add('active');
      const cells = [ it.id, it.subject || '', it.comment || '', it.entryDate || '', it.initiatedByUserCode ?? '', it.contractId ?? '', it.priorityText || '', it.statusText || '' ];
      cells.forEach(text => { const td = document.createElement('td'); td.textContent = String(text ?? ''); tr.appendChild(td); });
      tbody.appendChild(tr);
    });
  }

  function renderPager(){
    const pages = Math.max(1, totalPages || 0);
    if (page > pages) page = pages;
    pageInfo.innerHTML = `Page <b>${page}</b> of <b>${pages}</b>`;
    pageCountInfo.textContent = `Total records: ${totalCount}`;
    prevPage.disabled = (page <= 1 || totalCount === 0);
    nextPage.disabled = (page >= pages || totalCount === 0);
  }

  async function openModal(){
    if (!selectedId){ try { showToast('warning', 'Select a row first'); } catch {} return; }
    try {
      const res = await fetch(`${apiBase}/${selectedId}`, { credentials: 'include', headers: { 'Accept': 'application/json' }});
      if (!res.ok) throw new Error(`HTTP ${res.status}`);
      const d = await res.json();
      taskModalTitle.textContent = `Task ${d.id}`;
      tSubject.value = d.subject || '';
      tComments.value = d.comments || '';
      if (tPrioritySel) { const opts = Array.from(tPrioritySel.querySelectorAll('option')); const match = opts.find(o => Number(o.value) === Number(d.priority)); if (match) { tPrioritySel.value = match.value; } }
      if (tStatusLbl) tStatusLbl.textContent = d.statusText || '';
      tEntryDate.value = d.entryDate || '';
      // Contract ID link
      if (d.contractId != null) {
        tContractIdLink.href = `/contracts/${d.contractId}`;
        tContractIdLink.textContent = String(d.contractId);
        tContractIdLink.style.display = 'inline';
        tContractIdText.style.display = 'none';
      } else {
        tContractIdLink.style.display = 'none';
        tContractIdText.textContent = '';
        tContractIdText.style.display = 'inline';
      }
      if (tInitiatedBy) tInitiatedBy.value = (d.initiatedByUserCode == null ? '' : String(d.initiatedByUserCode));
      try { modal.showModal(); } catch(e) { console.error('Modal open error:', e); }
    } catch(err) { console.error('openModal error:', err); try { showToast('error', 'Failed to load task'); } catch {} }
  }

  document.addEventListener('DOMContentLoaded', () => {
    // Check if user is privileged before loading data
    const isLoggedIn = localStorage.getItem('isLoggedIn') === 'true';
    const roleId = parseInt(localStorage.getItem('roleId') || '0', 10);
    const isPrivileged = isLoggedIn && (roleId === 7 || roleId === 8);
    if (!isPrivileged) return; // Access Denied is handled in HTML script
    
    // Grid starts empty until Search is clicked
    updateActions();
    loadLookups();

    // Check for URL query parameters (for auto-search and auto-open from notification links)
    const urlParams = new URLSearchParams(window.location.search);
    const autoTaskId = urlParams.get('taskId');
    const autoOpen = urlParams.get('autoOpen') === '1';
    
    if (autoTaskId) {
      // Set the task ID filter and trigger search (skip validation for auto-search)
      filterTaskId.value = autoTaskId;
      page = 1;
      search(true).then(() => {
        // After search completes, if autoOpen is set and we have results, open the modal
        if (autoOpen && items.length > 0) {
          // Select the first item (should be the one we searched for)
          selectedId = items[0].id;
          updateActions();
          // Highlight the row
          const tbody = document.querySelector('#grid tbody');
          const rows = tbody?.querySelectorAll('tr');
          rows?.forEach(r => r.classList.remove('table-row-selected'));
          const firstRow = tbody?.querySelector('tr');
          if (firstRow) firstRow.classList.add('table-row-selected');
          // Open the modal
          openModal();
        }
      });
      // Clear URL params to avoid re-triggering on refresh
      window.history.replaceState({}, document.title, window.location.pathname);
    }

    // Paging & page size
    prevPage.addEventListener('click', () => { if (page > 1){ page--; search(); } });
    nextPage.addEventListener('click', () => { const pages = Math.max(1, totalPages || 0); if (page < pages){ page++; search(); } });
    pageSizeSel.addEventListener('change', (e) => { pageSize = parseInt(e.target.value, 10) || 10; page = 1; search(); });

    // Sorting
    const ths = document.querySelectorAll('#grid thead th');
    ths.forEach((th, idx) => {
      const keys = ['id','subject','comment','entryDate','initiatedByUserId','contractId','priority','status'];
      const key = keys[idx]; if (!key) return; th.style.cursor = 'pointer'; th.title = 'Click to sort';
      th.addEventListener('click', () => {
        if (sortBy === key) sortDir = (sortDir === 'asc' ? 'desc' : 'asc'); else { sortBy = key; sortDir = 'asc'; }
        ths.forEach(h => { h.dataset.sort = ''; h.textContent = h.textContent?.replace(/[▲▼]\s*$/,'').trim(); });
        const label = th.textContent?.replace(/[▲▼]\s*$/,'').trim() || '';
        th.dataset.sort = sortDir; th.textContent = label + (sortDir === 'asc' ? ' ▲' : ' ▼');
        // Reset to first page on sort change
        page = 1; search();
      });
    });

    // Search & Clean
    btnSearch.addEventListener('click', (e) => { e.preventDefault(); page = 1; search(); });
    btnClean.addEventListener('click', (e) => { e.preventDefault(); filterTaskId.value=''; if(filterContractId) filterContractId.value=''; filterPriority.value=''; filterStatus.value=''; items=[]; selectedId=null; page=1; totalPages=0; totalCount=0; renderRows(); renderPager(); updateActions(); });

    // Enter key triggers search on filter inputs
    [filterTaskId, filterContractId, filterPriority, filterStatus].forEach(el => {
      el?.addEventListener('keydown', (e) => {
        if (e.key === 'Enter') { e.preventDefault(); page = 1; search(); }
      });
    });

    // Open modal
    btnOpen.addEventListener('click', (e) => { e.preventDefault(); openModal(); });

    // Hide Export CSV on this page
    try { document.getElementById('exportBtn')?.classList.add('hidden'); } catch {}

    // Close modal
    tClose?.addEventListener('click', (e) => { e.preventDefault(); try { modal.close(); } catch {} });

    // Save priority/comments
    tSave?.addEventListener('click', async (e) => {
      e.preventDefault();
      if (!selectedId) { try { showToast('warning','Select a task first.'); } catch {} return; }
      try {
        const payload = { PriorityId: Number(tPrioritySel?.value || '0') || null, Comments: (tComments?.value || '').trim() };
        const res = await fetch(`${apiBase}/${encodeURIComponent(selectedId)}`, { method: 'PUT', credentials: 'include', headers: { 'Content-Type': 'application/json', 'Accept': 'application/json' }, body: JSON.stringify(payload) });
        if (res.status === 401){ window.location.href = '/login?mode=login'; return; }
        if (!res.ok) throw new Error('HTTP ' + res.status);
        try { showToast('info','Task has been saved.'); } catch {}
      } catch { try { showToast('error','Failed to save task.'); } catch {} }
    });

    // Status change flow
    tStatusBtn?.addEventListener('click', async (e) => {
      e.preventDefault();
      if (!selectedId){ try { showToast('warning','Select a row first'); } catch {} return; }
      try {
        // Load modal-data
        const res = await fetch(`${apiBase}/${encodeURIComponent(selectedId)}/status/modal-data`, { credentials: 'include', headers: { 'Accept': 'application/json' }});
        if (res.status === 401){ window.location.href = '/login?mode=login'; return; }
        if (!res.ok) throw new Error('HTTP ' + res.status);
        const data = await res.json();
        tsCurrent.value = data.currentStatus || '';
        const next = Array.isArray(data.nextStatuses) ? data.nextStatuses : [];
        tsNext.innerHTML = next.map(n => `<option value="${n.id}">${n.name}</option>`).join('');
        tsComment.value = '';
        statusModal.showModal();
      } catch { try { showToast('error','Failed to load status options.'); } catch {} }
    });

    tsCancel?.addEventListener('click', (e) => { e.preventDefault(); try { statusModal.close(); } catch {} });
    tsOk?.addEventListener('click', async (e) => {
      e.preventDefault();
      if (!selectedId){ try { showToast('warning','Please select a task first.'); } catch {} return; }
      try {
        const nextId = Number(tsNext?.value || '0');
        if (!nextId){ try { showToast('warning','Please select next status.'); } catch {} return; }
        const taskIdToUpdate = selectedId; // preserve before search clears it
        const payload = { nextStatusId: nextId, comment: (tsComment?.value || '').trim() };
        const res = await fetch(`${apiBase}/${encodeURIComponent(taskIdToUpdate)}/status`, { method: 'POST', credentials: 'include', headers: { 'Content-Type': 'application/json', 'Accept': 'application/json' }, body: JSON.stringify(payload) });
        if (res.status === 401){ window.location.href = '/login?mode=login'; return; }
        if (!res.ok) throw new Error('HTTP ' + res.status);
        const data = await res.json();
        // Update status label and grid
        if (tStatusLbl) tStatusLbl.textContent = String(data.newStatusName || '');
        try { showToast('info','Status updated.'); } catch {}
        try { statusModal.close(); } catch {}
        // Reload grid row details to reflect changes, then restore selection
        await search();
        selectedId = taskIdToUpdate;
        updateActions();
        // Re-highlight the row in the grid
        const rows = tbody?.querySelectorAll('tr');
        rows?.forEach(r => {
          r.classList.remove('active');
          const cells = r.querySelectorAll('td');
          if (cells.length > 0 && String(cells[0].textContent) === String(taskIdToUpdate)) {
            r.classList.add('active');
          }
        });
      } catch { try { showToast('error','Failed to update status.'); } catch {} }
    });
  });
})();