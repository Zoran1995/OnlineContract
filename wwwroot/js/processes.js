/* global showToast */
(() => {
  const apiBase = "/api/processes";

  let page = 1;
  let pageSize = 10;
  let totalPages = 1;
  let totalCount = 0;
  let selectedId = null;
  let items = [];
  let sortBy = null; // 'id'|'name'|'description'|'lastStart'|'lastEnd'|'nextRun'|'duration'|'status'|'active'
  let sortDir = 'asc';

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

  function updateActions() {
    const hasSel = !!selectedId;
    if (btnRun) btnRun.disabled = !hasSel;
    [btnToggleActive, btnDelete].forEach(btn => {
      if (!btn) return;
      btn.classList.toggle('disabled', !hasSel);
      btn.classList.toggle('opacity-50', !hasSel);
      btn.classList.toggle('cursor-not-allowed', !hasSel);
      btn.setAttribute('aria-disabled', (!hasSel).toString());
      btn.style.pointerEvents = hasSel ? '' : 'none';
      btn.tabIndex = hasSel ? 0 : -1;
    });

    const row = (items || []).find(x => x.id === selectedId);
    const isActive = !!(row && row.isActive);
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

  function statusTextFromId(id) {
    switch (Number(id)) {
      case 37: return 'Successful';
      case 38: return 'Warning';
      case 39: return 'Failed';
      case 40: return 'Successful (No Work)';
      default: return 'Unknown';
    }
  }

  function getSortVal(it, key) {
    switch (key) {
      case 'id': return Number(it.id || 0);
      case 'name': return String(it.name || '');
      case 'description': return String(it.description || '');
      case 'lastStart': return it.lastStartDt ? new Date(it.lastStartDt).getTime() : 0;
      case 'lastEnd': return it.lastEndDt ? new Date(it.lastEndDt).getTime() : 0;
      case 'nextRun': return it.nextRunDt ? new Date(it.nextRunDt).getTime() : 0;
      case 'duration': return Number(it.duration || 0);
      case 'status': return Number(it.statusId || 0);
      case 'active': return it.isActive ? 1 : 0;
      default: return '';
    }
  }

  function renderRows() {
    if (!tbody) return;
    tbody.innerHTML = '';
    if (items.length === 0) {
      const tr = document.createElement('tr');
      const td = document.createElement('td');
      td.colSpan = 9; td.className = 'empty'; td.textContent = 'No results.';
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
        String(it.duration ?? ''),
        statusTextFromId(it.statusId),
        it.isActive ? 'Active' : 'Inactive'
      ];
      cells.forEach(text => { const td = document.createElement('td'); td.textContent = text; tr.appendChild(td); });
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

  document.addEventListener('DOMContentLoaded', () => {
    try { document.getElementById('exportBtn')?.classList.add('hidden'); } catch {}

    load();

    btnRun?.addEventListener('click', (e) => {
      e.preventDefault();
      if (!selectedId) { try { showToast('warning','Select a row first'); } catch {} return; }
      // Placeholder; backend run endpoint not implemented in this task
      try { showToast('info', `Process ${selectedId} run requested.`); } catch {}
    });

    // Pager
    prevPage?.addEventListener('click', () => { if (page > 1) { page--; load(); } });
    nextPage?.addEventListener('click', () => { const pages = Math.max(1, Number(totalPages||1)); if (page < pages) { page++; load(); } });
    pageSizeSel?.addEventListener('change', (e) => { pageSize = parseInt(e.target.value, 10) || 10; page = 1; load(); });

    // Column sorting
    const ths = document.querySelectorAll('#grid thead th');
    ths.forEach((th, idx) => {
      const key = ['id','name','description','lastStart','lastEnd','nextRun','duration','status','active'][idx];
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
      menu.classList.toggle('dropdown-open');
      menuContent.classList.toggle('hidden');
    });
    document.addEventListener('click', (e) => {
      if (!menu.contains(e.target)) {
        menu.classList.remove('dropdown-open');
        menuContent.classList.add('hidden');
      }
    });
    btnToolbarMenu?.addEventListener('dblclick', () => {
      menu.classList.remove('dropdown-open');
      menuContent.classList.add('hidden');
    });

    // Kebab options (no backend implemented here)
    btnToggleActive?.addEventListener('click', (e) => { e.preventDefault(); menu.classList.remove('dropdown-open'); menuContent.classList.add('hidden'); });
    btnDelete?.addEventListener('click', (e) => { e.preventDefault(); menu.classList.remove('dropdown-open'); menuContent.classList.add('hidden'); });
  });
})();
