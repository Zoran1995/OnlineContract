/* global showToast */
(() => {
  const apiBase = "/api/processes";

  let page = 1;
  let pageSize = 10;
  let totalPages = 1;
  let totalCount = 0;
  let selectedId = null;
  let items = [];
  let sortBy = null; // 'id'|'name'|'description'|'lastStart'|'lastEnd'|'nextRun'|'duration'|'status'
  let sortDir = 'asc';

  const btnCancel = document.getElementById('btnCancel');
  const btnRun = document.getElementById('btnRun');
  const btnToolbarMenu = document.getElementById('btnToolbarMenu');
  const menu = document.getElementById('toolbarMenu');
  const menuContent = document.getElementById('toolbarMenuContent');
  const btnToggleActive = document.getElementById('btnToggleActive');
  const toggleText = document.getElementById('toggleText');
  const toggleIcon = document.getElementById('toggleIcon');
  const btnDelete = document.getElementById('btnDelete');

  const tbody = document.querySelector('#grid tbody');
  const pageInfo = document.getElementById('pageInfo');
  const pageCountInfo = document.getElementById('pageCountInfo');
  const prevPage = document.getElementById('prevPage');
  const nextPage = document.getElementById('nextPage');
  const pageSizeSel = document.getElementById('pageSize');

  function showLoading() { document.getElementById('loadingOverlay')?.classList.remove('hidden'); }
  function hideLoading() { document.getElementById('loadingOverlay')?.classList.add('hidden'); }

  function getSelectedRow() {
    return (items || []).find(x => x.id === selectedId) || null;
  }

  function closeKebab() {
    menu?.classList.remove('dropdown-open');
    menuContent?.classList.add('hidden');
  }

  function updateActions() {
    const hasSel = !!selectedId;
    const row = getSelectedRow();
    const isActive = !!(row && row.isActive);
    const isRunning = !!(row && row.isRunning);
    // Process can be cancelled if it's running OR if lastEndDt/duration are not populated
    const canCancel = !!(row && (row.isRunning || !row.lastEndDt || !row.durationFmt));
    const isAdmin = parseInt(localStorage.getItem('roleId') || '0', 10) === 8;
    // Run button disabled if no selection or if selected process is inactive
    if (btnRun) btnRun.disabled = !hasSel || !isActive;
    // Cancel button disabled if no selection or if process cannot be cancelled
    if (btnCancel) btnCancel.disabled = !hasSel || !canCancel;
    [btnToggleActive, btnDelete].forEach(btn => {
      if (!btn) return;
      btn.classList.toggle('disabled', !hasSel);
      btn.classList.toggle('opacity-50', !hasSel);
      btn.classList.toggle('cursor-not-allowed', !hasSel);
      btn.setAttribute('aria-disabled', (!hasSel).toString());
      btn.style.pointerEvents = hasSel ? '' : 'none';
      btn.tabIndex = hasSel ? 0 : -1;
    });
    if (toggleText) toggleText.textContent = isActive ? 'Deactivate' : 'Activate';
    if (toggleIcon) toggleIcon.textContent = isActive ? 'block' : 'check_circle';
  }

  async function load() {
    try {
      showLoading();
      selectedId = null;
      updateActions();
      const res = await fetch(`${apiBase}?page=${page}&pageSize=${pageSize}`, { method: 'GET', credentials: 'include', headers: { 'Accept': 'application/json' } });
      if (res.status === 401) { window.location.href = '/login?mode=login'; return; }
      if (!res.ok) throw new Error(`HTTP ${res.status}`);
      const data = await res.json();
      items = Array.isArray(data.items) ? data.items : [];
      totalCount = Number(data.totalCount ?? items.length);
      {
        const apiPages = Number(data.totalPages);
        const computedPages = Math.ceil(totalCount / pageSize) || 1;
        totalPages = (Number.isFinite(apiPages) && apiPages > 0) ? apiPages : computedPages;
      }
      renderRows();
      renderPager();
      updateActions();
    } catch (err) {
      try { showToast('error', 'Failed to load processes'); } catch {}
    } finally { hideLoading(); }
  }

  function getSortVal(it, key) {
    switch (key) {
      case 'id': return Number(it.id || 0);
      case 'name': return String(it.name || '');
      case 'description': return String(it.description || '');
      case 'lastStart': return it.lastStartDt ? new Date(it.lastStartDt).getTime() : 0;
      case 'lastEnd': return it.lastEndDt ? new Date(it.lastEndDt).getTime() : 0;
      case 'nextRun': return it.nextRunDt ? new Date(it.nextRunDt).getTime() : 0;
      case 'duration': return Number(it.durationSec || 0);
      case 'status': return it.isActive ? 1 : 0;
      default: return '';
    }
  }

  function renderRows() {
    if (!tbody) return;
    tbody.innerHTML = '';
    if (items.length === 0) {
      const tr = document.createElement('tr');
      const td = document.createElement('td');
      td.colSpan = 8; td.className = 'empty'; td.textContent = 'No results.';
      tr.appendChild(td);
      tbody.appendChild(tr);
      return;
    }
    const rows = items.slice();
    if (sortBy) {
      const dirMul = sortDir === 'desc' ? -1 : 1;
      rows.sort((a, b) => {
        const va = getSortVal(a, sortBy);
        const vb = getSortVal(b, sortBy);
        const ax = va == null ? '' : va;
        const bx = vb == null ? '' : vb;
        if (typeof ax === 'number' && typeof bx === 'number') return (ax - bx) * dirMul;
        const sa = String(ax).toLowerCase();
        const sb = String(bx).toLowerCase();
        if (sa < sb) return -1 * dirMul;
        if (sa > sb) return 1 * dirMul;
        return 0;
      });
    }
    rows.forEach(it => {
      const tr = document.createElement('tr');
      tr.addEventListener('click', () => {
        Array.from(tbody.querySelectorAll('tr.active')).forEach(r => r.classList.remove('active'));
        tr.classList.add('active');
        selectedId = it.id;
        updateActions();
      });
      if (selectedId && it.id === selectedId) tr.classList.add('active');
      const cells = [
        String(it.id),
        String(it.name || ''),
        String(it.description || ''),
        it.lastStartDt ? new Date(it.lastStartDt).toLocaleString() : '',
        it.lastEndDt ? new Date(it.lastEndDt).toLocaleString() : '',
        it.nextRunDt ? new Date(it.nextRunDt).toLocaleString() : '',
        String(it.durationFmt || ''),
        it.isActive ? 'Active' : 'Inactive'
      ];
      cells.forEach((text) => {
        const td = document.createElement('td');
        td.textContent = text;
        tr.appendChild(td);
      });
      tbody.appendChild(tr);
    });
  }

  function renderPager() {
    const pages = Math.max(1, Number(totalPages || 1));
    if (page > pages) page = pages;
    const start = totalCount === 0 ? 0 : (page - 1) * pageSize + 1;
    const end = totalCount === 0 ? 0 : Math.min(page * pageSize, totalCount);
    if (pageInfo) pageInfo.innerHTML = `Page <b>${page}</b> of <b>${pages}</b>`;
    if (pageCountInfo) pageCountInfo.textContent = `Total records: ${totalCount}`;
    if (prevPage) prevPage.disabled = (page <= 1 || totalCount === 0);
    if (nextPage) nextPage.disabled = (page >= pages || totalCount === 0);
  }

  // API call for Run
  async function runProcess() {
    const row = getSelectedRow();
    if (!row) { try { showToast('warning', 'Select a row first'); } catch {} return; }

    const processName = row.name || 'Process';
    try {
      showLoading();
      const res = await fetch(`${apiBase}/${row.id}/run`, {
        method: 'POST',
        credentials: 'include',
        headers: { 'Accept': 'application/json', 'Content-Type': 'application/json' }
      });

      if (res.status === 401) { window.location.href = '/login?mode=login'; return; }

      const data = await res.json();

      if (res.status === 409) {
        // Conflict - execution already in progress
        try { showToast('warning', data.message || `The '${processName}' process cannot be started because another run is already in progress.`); } catch {}
        return;
      }

      if (!res.ok) {
        try { showToast('error', data.message || `Failed to run '${processName}'`); } catch {}
        return;
      }

      if (data.success) {
        // First show initiation message
        try { showToast('info', `The '${processName}' process has been initiated.`); } catch {}
        
        // Then show completion message based on status
        const status = (data.status || '').toLowerCase();
        setTimeout(() => {
          if (status === 'warning' || status === 'successfulnothingprocessed') {
            try { showToast('warning', data.message || `The '${processName}' process completed with warnings.`); } catch {}
          } else if (status === 'failed') {
            try { showToast('error', data.message || `The '${processName}' process failed.`); } catch {}
          } else {
            try { showToast('info', data.message || `The '${processName}' process completed successfully.`); } catch {}
          }
        }, 1500);
      } else {
        try { showToast('error', data.message || `The '${processName}' process failed.`); } catch {}
      }

      await load();
    } catch (err) {
      try { showToast('error', `Failed to run '${processName}'`); } catch {}
    } finally {
      hideLoading();
    }
  }

  // API call for Cancel
  async function cancelProcess() {
    const row = getSelectedRow();
    if (!row) return;
    // Allow cancel if running OR if lastEndDt/duration not populated
    const canCancel = row.isRunning || !row.lastEndDt || !row.durationFmt;
    if (!canCancel) return; // Silently do nothing if process cannot be cancelled

    const processName = row.name || 'Process';

    try {
      showLoading();
      const res = await fetch(`${apiBase}/${row.id}/cancel`, {
        method: 'POST',
        credentials: 'include',
        headers: { 'Accept': 'application/json', 'Content-Type': 'application/json' }
      });

      if (res.status === 401) { window.location.href = '/login?mode=login'; return; }

      const data = await res.json();

      if (!res.ok) {
        try { showToast('error', data.message || `Failed to cancel '${processName}'`); } catch {}
        return;
      }

      try { showToast('info', data.message || `'${processName}' has been cancelled.`); } catch {}
      await load();
    } catch (err) {
      try { showToast('error', `Failed to cancel '${processName}'`); } catch {}
    } finally {
      hideLoading();
    }
  }

  // API call for Activate/Deactivate
  async function toggleActive() {
    const row = getSelectedRow();
    if (!row) return;

    const processName = row.name || 'Process';
    const endpoint = row.isActive ? 'deactivate' : 'activate';
    const action = row.isActive ? 'deactivated' : 'activated';

    try {
      showLoading();
      closeKebab();

      const res = await fetch(`${apiBase}/${row.id}/${endpoint}`, {
        method: 'POST',
        credentials: 'include',
        headers: { 'Accept': 'application/json', 'Content-Type': 'application/json' }
      });

      if (res.status === 401) { window.location.href = '/login?mode=login'; return; }

      const data = await res.json();

      if (!res.ok) {
        try { showToast('error', data.message || `Failed to ${endpoint} '${processName}'`); } catch {}
        return;
      }

      try { showToast('info', `'${processName}' has been ${action}.`); } catch {}
      await load();
    } catch (err) {
      try { showToast('error', `Failed to ${endpoint} '${processName}'`); } catch {}
    } finally {
      hideLoading();
    }
  }

  // API call for Delete
  async function deleteProcess() {
    const row = getSelectedRow();
    if (!row) return;

    const processName = row.name || 'Process';

    if (!confirm(`Are you sure you want to delete '${processName}'?`)) {
      closeKebab();
      return;
    }

    try {
      showLoading();
      closeKebab();

      const res = await fetch(`${apiBase}/${row.id}`, {
        method: 'DELETE',
        credentials: 'include',
        headers: { 'Accept': 'application/json' }
      });

      if (res.status === 401) { window.location.href = '/login?mode=login'; return; }

      const data = await res.json();

      if (!res.ok) {
        try { showToast('error', data.message || `Failed to delete '${processName}'`); } catch {}
        return;
      }

      try { showToast('info', `'${processName}' has been deleted.`); } catch {}
      await load();
    } catch (err) {
      try { showToast('error', `Failed to delete '${processName}'`); } catch {}
    } finally {
      hideLoading();
    }
  }

  document.addEventListener('DOMContentLoaded', () => {
    // Check if user is privileged before loading data
    const isLoggedIn = localStorage.getItem('isLoggedIn') === 'true';
    const roleId = parseInt(localStorage.getItem('roleId') || '0', 10);
    const isPrivileged = isLoggedIn && (roleId === 7 || roleId === 8);
    if (!isPrivileged) return; // Access Denied is handled in HTML script
    
    try { document.getElementById('exportBtn')?.classList.add('hidden'); } catch {}

    load();

    // Run button
    btnRun?.addEventListener('click', (e) => {
      e.preventDefault();
      runProcess();
    });

    // Cancel button
    btnCancel?.addEventListener('click', (e) => {
      e.preventDefault();
      cancelProcess();
    });

    // Pager
    prevPage?.addEventListener('click', () => { if (page > 1) { page--; load(); } });
    nextPage?.addEventListener('click', () => { const pages = Math.max(1, Number(totalPages||1)); if (page < pages) { page++; load(); } });
    pageSizeSel?.addEventListener('change', (e) => { pageSize = parseInt(e.target.value, 10) || 10; page = 1; load(); });

    // Column sorting
    const ths = document.querySelectorAll('#grid thead th');
    ths.forEach((th, idx) => {
      const key = ['id','name','description','lastStart','lastEnd','nextRun','duration','status'][idx];
      if (!key) return;
      th.style.cursor = 'pointer';
      th.title = 'Click to sort';
      th.addEventListener('click', () => {
        if (sortBy === key) {
          sortDir = sortDir === 'asc' ? 'desc' : 'asc';
        } else {
          sortBy = key; sortDir = 'asc';
        }
        ths.forEach(h => { h.dataset.sort = ''; h.textContent = h.textContent?.replace(/[▲▼]\s*$/,'').trim(); });
        th.dataset.sort = sortDir;
        const label = th.textContent?.replace(/[▲▼]\s*$/,'').trim() || '';
        th.textContent = label + (sortDir === 'asc' ? ' ▲' : ' ▼');
        renderRows();
      });
    });

    // Kebab open/close behaviors
    btnToolbarMenu?.addEventListener('click', (e) => {
      e.preventDefault();
      e.stopPropagation();
      menu.classList.toggle('dropdown-open');
      menuContent.classList.toggle('hidden');
    });
    document.addEventListener('click', (e) => {
      if (!menu?.contains(e.target)) {
        closeKebab();
      }
    });

    // Kebab options
    btnToggleActive?.addEventListener('click', (e) => {
      e.preventDefault();
      e.stopPropagation();
      if (!selectedId) return;
      toggleActive();
    });

    btnDelete?.addEventListener('click', (e) => {
      e.preventDefault();
      e.stopPropagation();
      if (!selectedId) return;
      deleteProcess();
    });
  });
})();
