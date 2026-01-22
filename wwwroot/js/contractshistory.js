
/* global showToast */

(() => {
  const apiBase = '/api/contracts';

  let page = 1;
  let pageSize = 10;
  let totalPages = 1;
  let totalCount = 0;
  let selectedId = null;
  let items = [];
  let sortBy = '', sortDir = '';

  const btnOpen = document.getElementById('btnOpen');
  const btnDelete = document.getElementById('btnDelete');
  const tbody = document.querySelector('#grid tbody');
  const pageInfo = document.getElementById('pageInfo');
  const pageCountInfo = document.getElementById('pageCountInfo');
  const prevPage = document.getElementById('prevPage');
  const nextPage = document.getElementById('nextPage');
  const pageSizeSel = document.getElementById('pageSize');
  const emptyState = document.getElementById('emptyState');

  function showLoading() { document.getElementById('loadingOverlay')?.classList.remove('hidden'); }
  function hideLoading() { document.getElementById('loadingOverlay')?.classList.add('hidden'); }
  function setEmptyState(v) { emptyState?.classList.toggle('hidden', !v); }

  function updateButtonsState() {
    const hasSel = !!selectedId;
    if (btnOpen) btnOpen.disabled = !hasSel;
    // Delete enabled only for Draft rows
    const sel = items.find(x => (x.id ?? x.contractId ?? x.contract_id) === selectedId);
    const selState = String(sel?.contractState ?? sel?.contract_state ?? sel?.state ?? '').toLowerCase();
    const canDelete = hasSel && selState === 'draft';
    if (btnDelete) btnDelete.disabled = !canDelete;
  }

  function buildQuery() {
    const qs = new URLSearchParams();
    qs.set('page', String(page));
    qs.set('pageSize', String(pageSize));
    // Restrict to logged-in customer orders on the server when supported
    try {
      const userId = localStorage.getItem('userId');
      if (userId) qs.set('userId', String(userId));
    } catch {}
    if (sortBy) qs.set('sortBy', sortBy);
    if (sortDir) qs.set('sortDir', sortDir);
    return `?${qs.toString()}`;
  }

  function mapRow(it) {
    // Map API fields to the required columns; support multiple shapes
    const id = it.id ?? it.contractId ?? it.contract_id;
    const entryDt = it.entryDate ?? it.input_dt ?? it.inputDt ?? '';
    const state = it.contractState ?? it.contract_state ?? it.state ?? '';
    const deliveredDt = it.deliveredDate ?? it.delivered_dt ?? '';
    const amount = it.amount ?? 0;
    return { id, entryDt, state, deliveredDt, amount };
  }

  async function load() {
    try {
      showLoading();
      setEmptyState(false);
      selectedId = null;
      updateButtonsState();

      const res = await fetch(`${apiBase}${buildQuery()}`, {
        method: 'GET',
        credentials: 'include',
        headers: { 'Accept': 'application/json' }
      });
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
      setEmptyState(items.length === 0);
    } catch (err) {
      try { showToast('error', 'Failed to load contracts history'); } catch {}
      setEmptyState(true);
    } finally { hideLoading(); }
  }

  function renderRows() {
    if (!tbody) return;
    tbody.innerHTML = '';
    if (!items || items.length === 0) {
      const tr = document.createElement('tr');
      const td = document.createElement('td'); td.colSpan = 5; td.className = 'empty'; td.textContent = 'No results.'; tr.appendChild(td); tbody.appendChild(tr);
      return;
    }

    items.forEach(raw => {
      const it = mapRow(raw);
      const tr = document.createElement('tr');
      tr.addEventListener('click', () => {
        Array.from(tbody.querySelectorAll('tr.active')).forEach(r => r.classList.remove('active'));
        tr.classList.add('active');
        selectedId = it.id;
        updateButtonsState();
      });
      if (selectedId && it.id === selectedId) tr.classList.add('active');

      const amt = Number(it.amount || 0).toFixed(2);
      const cells = [ String(it.id ?? ''), String(it.entryDt ?? it.entry_dt ?? it.entryDt ?? ''), String(it.state ?? ''), String(it.deliveredDt ?? ''), amt ];
      cells.forEach(text => { const td = document.createElement('td'); td.textContent = text; tr.appendChild(td); });
      tbody.appendChild(tr);
    });

    try {
      const keys = ['id','entryDate','contractState','deliveredDate','amount'];
      document.querySelectorAll('#grid thead th').forEach((th, idx) => {
        th.style.cursor = 'pointer';
        const ex = th.querySelector('.sort-indicator'); if (ex) ex.remove();
        const key = keys[idx] || '';
        const span = document.createElement('span'); span.className = 'sort-indicator ml-2';
        if (key && key === sortBy) { span.textContent = sortDir === 'desc' ? ' ▼' : ' ▲'; }
        th.appendChild(span);
        th.onclick = () => { if (!key) return; if (sortBy === key) sortDir = (sortDir === 'desc' ? 'asc' : 'desc'); else { sortBy = key; sortDir = 'asc'; } page = 1; load(); };
      });
    } catch {}
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

  async function doDelete() {
    if (!selectedId) { try { showToast('warning', 'Select a contract first'); } catch {} return; }
    const dlg = document.getElementById('contractDeleteConfirm');
    const okBtn = document.getElementById('contractConfirmOk');
    if (!dlg || !okBtn || !dlg.showModal) {
      if (!confirm('Delete the selected contract? This action cannot be undone.')) return;
    } else {
      let resolved = false;
      const onOk = (e) => { e.preventDefault(); resolved = true; try { dlg.close(); } catch {} };
      okBtn.addEventListener('click', onOk, { once: true });
      dlg.addEventListener('close', () => { if (resolved) proceedDelete(); }, { once: true });
      try { dlg.showModal(); } catch { if (!confirm('Delete the selected contract? This action cannot be undone.')) return; }
      if (!dlg.open && !resolved) return;
      if (!dlg.open && resolved) return;
      return;
    }

    await proceedDelete();
  }

  async function proceedDelete() {
    try {
      const res = await fetch(`${apiBase}/${encodeURIComponent(selectedId)}/delete`, {
        method: 'POST',
        credentials: 'include',
        headers: { 'Accept': 'application/json' }
      });
      if (res.status === 401) { window.location.href = '/login?mode=login'; return; }
      if (!res.ok) throw new Error(`HTTP ${res.status}`);
      try { showToast('info', 'The contract was deleted successfully.'); } catch {}
      selectedId = null; updateButtonsState(); await load();
    } catch {
      try { showToast('error', 'Failed to delete contract'); } catch {}
    }
  }

  document.addEventListener('DOMContentLoaded', () => {
    // Only Customers can access this page
    function showAccessDeniedInline() {
      try {
        const main = document.querySelector('main');
        if (main) main.classList.add('hidden');
        const overlay = document.getElementById('loadingOverlay');
        if (overlay) overlay.classList.add('hidden');
        const section = document.createElement('section');
        section.className = 'container mx-auto p-6';
        section.innerHTML = `
          <div class="bg-base-100 rounded-xl shadow p-8 text-center">
            <div class="flex items-center justify-center gap-2 mb-3">
              <span class="material-icons text-rose-600">block</span>
              <h2 class="text-2xl font-bold">Access Denied</h2>
            </div>
            <p class="text-gray-700">You are not allowed to see this page.</p>
          </div>
        `;
        document.body.appendChild(section);
      } catch {}
    }
    try {
      const roleId = parseInt(localStorage.getItem('roleId') || '0', 10) || 0;
      const isLoggedIn = !!localStorage.getItem('userId');
      if (!isLoggedIn) { window.location.href = '/login?mode=login'; return; }
      if (roleId !== 5) { showAccessDeniedInline(); return; }
    } catch {}

    btnOpen?.addEventListener('click', () => {
      if (!selectedId) { try { showToast('warning', 'Select a contract first'); } catch {} return; }
      // Navigate to items-only contract history page
      window.location.href = `/contractshistory/${selectedId}`;
    });
    btnDelete?.addEventListener('click', () => { doDelete(); });

    prevPage?.addEventListener('click', () => { if (page > 1) { page--; load(); } });
    nextPage?.addEventListener('click', () => { const pages = Math.max(1, Number(totalPages || 1)); if (page < pages) { page++; load(); } });
    pageSizeSel?.addEventListener('change', (e) => { pageSize = parseInt(e.target.value, 10) || 10; page = 1; load(); });

    load();
  });
})();
