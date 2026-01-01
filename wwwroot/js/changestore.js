// changestore grid script
document.addEventListener('DOMContentLoaded', () => {
  const openBtn = document.getElementById('openStoreBtn');
  const tableBody = document.getElementById('storesTbody');
  const pgPrev = document.getElementById('pgPrev');
  const pgNext = document.getElementById('pgNext');
  const pgInfo = document.getElementById('pgInfo');
  const pageSizeSel = document.getElementById('pageSize');

  let stores = [];
  let page = 1;
  let pageSize = parseInt(pageSizeSel?.value || '10', 10) || 10;
  let totalPages = 1;
  let selectedId = null;
  let sortBy = '';
  let sortDir = '';

  async function loadStores() {
    try {
      const userId = localStorage.getItem('userId') || 2;
      // Request fresh data; avoid cached responses so grid reflects latest updates
      const qs = new URLSearchParams();
      qs.set('userId', String(userId));
      qs.set('t', String(Date.now()));
      if (sortBy) qs.set('sortBy', sortBy);
      if (sortDir) qs.set('sortDir', sortDir);
      const res = await fetch(`/api/stores?${qs.toString()}`, { cache: 'no-store', credentials: 'include', headers: { 'Accept': 'application/json' } });
      const data = await res.json();
      stores = data.items || [];
      page = 1;
      render();
    } catch (err) {
      console.error(err);
      showToast('error', 'Failed to load stores.');
    }
  }

  function render() {
    pageSize = parseInt(pageSizeSel?.value || pageSize, 10) || pageSize;
    totalPages = Math.max(1, Math.ceil((stores.length || 0) / pageSize));
    if (page > totalPages) page = totalPages;
    const start = (page - 1) * pageSize;
    const pageItems = stores.slice(start, start + pageSize);
    tableBody.innerHTML = '';
    pageItems.forEach(s => {
      const tr = document.createElement('tr');
      tr.dataset.id = s.id;
      tr.innerHTML = `
        <td class="px-3 py-2">${s.id}</td>
        <td class="px-3 py-2">${escapeHtml(s.name || '')}</td>
        <td class="px-3 py-2">${escapeHtml(s.address || '')}</td>
        <td class="px-3 py-2">${escapeHtml(s.email || '')}</td>
        <td class="px-3 py-2">${escapeHtml(s.phone || '')}</td>
        <td class="px-3 py-2">${escapeHtml(s.lastUpdatedBy || '')}</td>
      `;
      tr.addEventListener('click', () => {
        // select row — use same 'active' class as Users/Products grid
        tableBody.querySelectorAll('tr').forEach(r => r.classList.remove('active'));
        tr.classList.add('active');
        selectedId = s.id;
        if (openBtn) openBtn.disabled = false;
      });
      tableBody.appendChild(tr);
    });

    try {
      const keys = ['id','name','address','email','phone','lastUpdatedBy'];
      document.querySelectorAll('table thead th').forEach((th, idx) => {
        th.style.cursor = 'pointer';
        const ex = th.querySelector('.sort-indicator'); if (ex) ex.remove();
        const key = keys[idx] || '';
        const span = document.createElement('span'); span.className = 'sort-indicator ml-2';
        if (key && key === sortBy) { span.textContent = sortDir === 'desc' ? ' ▼' : ' ▲'; }
        th.appendChild(span);
        th.onclick = () => { if (!key) return; if (sortBy === key) sortDir = (sortDir === 'desc' ? 'asc' : 'desc'); else { sortBy = key; sortDir = 'asc'; } page = 1; loadStores(); };
      });
    } catch {}

    // pager UI
    const total = stores.length || 0;
    const startIndex = total === 0 ? 0 : start + 1;
    const endIndex = Math.min(start + pageSize, total);
    if (pgInfo) pgInfo.textContent = `${startIndex}–${endIndex} of ${total} · pages ${totalPages}`;
    if (pgPrev) pgPrev.disabled = page <= 1;
    if (pgNext) pgNext.disabled = page >= totalPages;
    const pageCountInfo = document.getElementById('pageCountInfo');
    if (pageCountInfo) pageCountInfo.textContent = `Total records: ${total}`;
    if (!openBtn) return;
    openBtn.disabled = selectedId == null;
  }

  pgPrev?.addEventListener('click', (e) => { e.preventDefault(); if (page>1) { page--; render(); } });
  pgNext?.addEventListener('click', (e) => { e.preventDefault(); if (page<totalPages) { page++; render(); } });
  pageSizeSel?.addEventListener('change', () => { page = 1; render(); });

  if (openBtn) {
    openBtn.addEventListener('click', async (e) => {
      if (!selectedId) return;
      try {
        // Open existing modal and preload stores; then select chosen id inside select and trigger change
        try { window.ensureAdminChangeStoreModal(); } catch { }
        try { await window.preloadStoresIntoModal(); } catch { }
        const sel = document.getElementById('storeSelect');
        if (sel) {
          // Ensure select is enabled before setting value (preload may disable it)
          sel.disabled = false;
          sel.value = String(selectedId);
          sel.dispatchEvent(new Event('change'));
          // Lock the dropdown so user cannot change the selected store
          sel.disabled = true;
        }
        try { window.openAdminChangeStoreModal(); } catch { }
      } catch (err) {
        console.error(err);
        showToast('error', 'Failed to open store editor.');
      }
    });
  }

  // Utility
  function escapeHtml(s) { return String(s).replace(/&/g,'&amp;').replace(/</g,'&lt;').replace(/>/g,'&gt;'); }

  // initial load
  // expose a refresh function so other scripts (admin modal) can trigger a reload after save
  try { window.refreshStores = loadStores; } catch {}
  loadStores();
});
