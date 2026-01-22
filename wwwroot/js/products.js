
(() => {
  const apiBase = '/api/products';

  let page = 1;
  let pageSize = 10;
  let totalPages = 1;
  let totalCount = 0;

  let selectedId = null;
  let selectedRow = null;
  let selectedItem = null;
  let items = [];
  let sortBy = '', sortDir = '';

  const tbody = document.querySelector('#grid tbody');
  const emptyState = document.getElementById('emptyState');
  const loadingOverlay = document.getElementById('loadingOverlay');

  const fltQuery = document.getElementById('fltQuery');
  const fltStore = document.getElementById('fltStore');
  const btnSearch = document.getElementById('btnSearch');
  const btnClear = document.getElementById('btnClear');

  const btnOpen = document.getElementById('btnOpen');
  const btnAdd = document.getElementById('btnAdd');

  const btnDeactivate = document.getElementById('btnDeactivate');
  const btnDelete = document.getElementById('btnDelete');

  const prevPage = document.getElementById('prevPage');
  const nextPage = document.getElementById('nextPage');
  const pageInfo = document.getElementById('pageInfo');
  const pageCountInfo = document.getElementById('pageCountInfo');
  const pageSizeSel = document.getElementById('pageSize');

  function showLoading() { loadingOverlay?.classList.remove('hidden'); }
  function hideLoading() { loadingOverlay?.classList.add('hidden'); }

  function setEmptyState(visible, text) {
    if (!emptyState) return;
    if (typeof text === 'string') emptyState.textContent = text;
    emptyState.classList.toggle('hidden', !visible);
  }

  function updateOpenState() {
    if (btnOpen) btnOpen.disabled = !selectedId || (selectedItem && !selectedItem.isActive);

    const hasSelection = !!selectedId;
    // Hide Deactivate when no selection; Delete remains visible but disabled
    const deactivateItem = btnDeactivate ? btnDeactivate.closest('li') : null;
    if (deactivateItem) deactivateItem.classList.toggle('hidden', !hasSelection);

    // Disable/enable helper for menu items
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
    // Delete is present even with no selection (disabled in that case)
    setState(btnDelete, hasSelection);
    // Deactivate enabled only when a row is selected (hidden otherwise as above)
    setState(btnDeactivate, hasSelection);

    if (btnDeactivate && selectedItem) {
      const icon = selectedItem.isActive ? 'block' : 'check_circle';
      const label = selectedItem.isActive ? 'Deactivate' : 'Activate';
      // Render icon using material-icons so it looks consistent with Users page
      btnDeactivate.innerHTML = `<span class="material-icons">${icon}</span><span class="ml-2">${label}</span>`;
    }
  }

  function buildQuery() {
    const qs = new URLSearchParams();
    qs.set('page', String(page));
    qs.set('pageSize', String(pageSize));
    const q = (fltQuery?.value ?? '').trim();
    const storeId = (fltStore?.value ?? '').trim();
    if (q) qs.set('q', q);
    if (storeId) qs.set('storeId', storeId);
    if (sortBy) qs.set('sortBy', sortBy);
    if (sortDir) qs.set('sortDir', sortDir);
    return `?${qs.toString()}`;
  }

  async function loadStores() {
    if (!fltStore) return;
    try {
      const current = fltStore.value;
      const userId = localStorage.getItem('userId') ?? '';
      const res = await fetch(`/api/stores${userId ? `?userId=${encodeURIComponent(userId)}` : ''}`, {
        method: 'GET',
        credentials: 'include',
        headers: { 'Accept': 'application/json' }
      });
      if (!res.ok) return;
      const data = await res.json();
      const stores = Array.isArray(data?.items) ? data.items : [];
      fltStore.innerHTML = '';
      const optAll = document.createElement('option');
      optAll.value = '';
      optAll.textContent = 'All Stores';
      fltStore.appendChild(optAll);
      stores.forEach(s => {
        if (!s) return;
        const id = Number(s.id);
        if (!Number.isFinite(id) || id <= 0) return;
        const opt = document.createElement('option');
        opt.value = String(id);
        opt.textContent = s.name ? String(s.name) : `Store ${id}`;
        fltStore.appendChild(opt);
      });
      fltStore.value = (current ?? '');
    } catch { /* noop */ }
  }

  async function load() {
    try {
      showLoading();
      setEmptyState(false);
      selectedId = null;
      selectedRow = null;
      selectedItem = null;
      updateOpenState();

      // Add cache: 'no-store' and debug logging to avoid stale responses when sorting by qty
      try { console.debug('Loading products with sort', sortBy, sortDir); } catch {}
      const res = await fetch(`${apiBase}${buildQuery()}`, {
        method: 'GET',
        cache: 'no-store',
        credentials: 'include',
        headers: { 'Accept': 'application/json' }
      });

      if (res.status === 401) { window.location.href = '/login?mode=login'; return; }
      if (res.status === 403) {
        setEmptyState(true, 'Access denied. You do not have permission to view this content.');
        items = [];
        totalCount = 0;
        totalPages = 1;
        renderRows();
        renderPager();
        return;
      }
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
    } catch {
      setEmptyState(true, 'Products could not be loaded. Please refresh the page or try again later.');
      try { showToast('error', 'Failed to load products. Please refresh the page or try again later.'); } catch { /* noop */ }
    } finally {
      hideLoading();
    }
  }

  function renderRows() {
    if (!tbody) return;
    tbody.innerHTML = '';

    if (!items || items.length === 0) {
      const tr = document.createElement('tr');
      const td = document.createElement('td');
      td.colSpan = 6;
      td.className = 'empty';
      td.textContent = 'No results.';
      tr.appendChild(td);
      tbody.appendChild(tr);
      return;
    }

    items.forEach(it => {
      const tr = document.createElement('tr');
      tr.addEventListener('click', () => {
        Array.from(tbody.querySelectorAll('tr.active')).forEach(r => r.classList.remove('active'));
        tr.classList.add('active');
        selectedRow = tr;
        selectedId = it.id;
        selectedItem = it;
        updateOpenState();
      });
      if (selectedId && it.id === selectedId) tr.classList.add('active');

      const statusText = it.isActive ? 'Active' : 'Inactive';

      const tdId = document.createElement('td'); tdId.textContent = String(it.id ?? ''); tr.appendChild(tdId);
      const tdName = document.createElement('td'); tdName.textContent = String(it.name ?? ''); tr.appendChild(tdName);
      const tdDt = document.createElement('td'); tdDt.textContent = String(it.inputDt ?? ''); tr.appendChild(tdDt);
      const tdQ1 = document.createElement('td'); tdQ1.textContent = String(it.qtyStore1 ?? 0); tr.appendChild(tdQ1);
      const tdQ2 = document.createElement('td'); tdQ2.textContent = String(it.qtyStore2 ?? 0); tr.appendChild(tdQ2);
      const tdStatus = document.createElement('td'); tdStatus.textContent = statusText; tr.appendChild(tdStatus);

      // No actions column in rows: toolbar kebab above the grid controls actions
      tbody.appendChild(tr);
    });

    try {
      const keys = ['id','name','inputDt','qtyStore1','qtyStore2','isActive'];
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

  // per-row menu removed: toolbar menu controls actions for selected row

  function renderPager() {
    const pages = Math.max(1, Number(totalPages || 1));
    if (page > pages) page = pages;

    if (pageInfo) pageInfo.innerHTML = `Page <b>${page}</b> of <b>${pages}</b>`;
    if (pageCountInfo) pageCountInfo.textContent = `Total records: ${totalCount}`;

    if (prevPage) prevPage.disabled = (page <= 1 || totalCount === 0);
    if (nextPage) nextPage.disabled = (page >= pages || totalCount === 0);
  }

  async function doActivateDeactivate() {
    if (!selectedId || !selectedItem) {
      try { showToast('warning', 'Please select a product row first.'); } catch { /* noop */ }
      return;
    }
    try {
      const shouldDeactivate = !!selectedItem.isActive;
      const endpoint = shouldDeactivate ? 'deactivate' : 'activate';
      const res = await fetch(`${apiBase}/${encodeURIComponent(selectedId)}/${endpoint}?stamp=${encodeURIComponent(selectedItem.stamp ?? 0)}`, {
        method: 'POST',
        credentials: 'include',
        headers: { 'Accept': 'application/json' }
      });

      if (res.status === 401) { window.location.href = '/login?mode=login'; return; }
      if (res.status === 403) { try { showToast('error', 'Access denied. You do not have permission to perform this action.'); } catch { /* noop */ } return; }
      if (!res.ok) throw new Error(`HTTP ${res.status}`);

      try { showToast('info', shouldDeactivate ? 'Product has been deactivated successfully.' : 'Product has been activated successfully.'); } catch { /* noop */ }
      // Clear selection after action per requirements
      selectedId = null; selectedRow = null; selectedItem = null;
      try { renderRows(); } catch {}
      try { updateOpenState(); } catch {}
      await load();
    } catch {
      try { showToast('error', 'Operation failed. Please try again later.'); } catch { /* noop */ }
    }
  }

  async function doDelete() {
      if (!selectedId) {
      try { showToast('warning', 'Please select a product row before performing this action.'); } catch { /* noop */ }
      return;
    }
    const dlg = document.getElementById('productDeleteConfirm');
    const okBtn = document.getElementById('productConfirmOk');
    if (!dlg || !okBtn || !dlg.showModal) {
      if (!confirm('Delete the selected product? This will remove it from the Products list.')) return;
    } else {
      let resolved = false;
      const onOk = (e) => { e.preventDefault(); resolved = true; try { dlg.close(); } catch {} };
      okBtn.addEventListener('click', onOk, { once: true });
      dlg.addEventListener('close', () => {
        if (!resolved) return;
        proceedDelete();
      }, { once: true });
      try { dlg.showModal(); } catch { /* fallback to confirm */ if (!confirm('Delete the selected product? This will remove it from the Products list.')) return; }
      if (!dlg.open && !resolved) return; // user canceled in fallback path
      if (!dlg.open && resolved) return; // handled via proceedDelete in close
      return; // deletion will happen in close handler
    }

    try {
      const res = await fetch(`${apiBase}/${encodeURIComponent(selectedId)}/delete`, {
        method: 'POST',
        credentials: 'include',
        headers: { 'Accept': 'application/json' }
      });

      if (res.status === 401) { window.location.href = '/login?mode=login'; return; }
      if (res.status === 403) { try { showToast('error', 'Access denied. You do not have permission to perform this action.'); } catch { /* noop */ } return; }
      if (!res.ok) throw new Error(`HTTP ${res.status}`);

      try { showToast('info', 'Product has been deleted successfully.'); } catch { /* noop */ }
      // Clear selection after delete
      selectedId = null; selectedRow = null; selectedItem = null;
      try { renderRows(); } catch {}
      try { updateOpenState(); } catch {}
      await load();
    } catch {
      try { showToast('error', 'Failed to delete product. Please try again later.'); } catch { /* noop */ }
    }
    
    function proceedDelete() {
      // reuse the same deletion path as below
      (async function() {
        try {
          const res = await fetch(`${apiBase}/${encodeURIComponent(selectedId)}/delete`, {
            method: 'POST',
            credentials: 'include',
            headers: { 'Accept': 'application/json' }
          });

          if (res.status === 401) { window.location.href = '/login?mode=login'; return; }
          if (res.status === 403) { try { showToast('error', 'Access denied'); } catch { /* noop */ } return; }
          if (!res.ok) throw new Error(`HTTP ${res.status}`);

          try { showToast('info', 'Product was successfully deleted.'); } catch { /* noop */ }
          await load();
        } catch {
          try { showToast('error', 'Product could not be deleted. Please try again.'); } catch { /* noop */ }
        }
      })();
    }
  }

  // -------------------- DOM wiring --------------------
  document.addEventListener('DOMContentLoaded', () => {
    // Search/Clear
    btnSearch?.addEventListener('click', (e) => { e.preventDefault(); page = 1; load(); });
    btnClear?.addEventListener('click', (e) => {
      e.preventDefault();
      if (fltQuery) fltQuery.value = '';
      if (fltStore) fltStore.value = '';
      page = 1; load();
    });

    [fltQuery].forEach(el => {
      if (!el || el._oc_enter) return;
      el._oc_enter = true;
      el.addEventListener('keydown', (e) => { if (e.key === 'Enter') { e.preventDefault(); page = 1; load(); } });
    });

    prevPage?.addEventListener('click', () => { if (page > 1) { page--; load(); } });
    nextPage?.addEventListener('click', () => {
      const pages = Math.max(1, Number(totalPages || 1));
      if (page < pages) { page++; load(); }
    });

    pageSizeSel?.addEventListener('change', (e) => {
      pageSize = parseInt(e.target.value, 10) || 10;
      page = 1; load();
    });

    // Do not auto-trigger search when the store dropdown changes.
    // User must click the Search button explicitly.
    fltStore?.addEventListener('change', () => { page = 1; });

    btnOpen?.addEventListener('click', () => {
      if (!selectedId) {
        try { showToast('warning', 'Select a product first'); } catch { /* noop */ }
        return;
      }
      window.open(`/products/${selectedId}`, '_blank');
    });

    btnAdd?.addEventListener('click', () => { window.location.href = '/products/new'; });

    const menuContainer = document.getElementById('toolbarMenu');
    const menuButton = document.getElementById('btnToolbarMenu');
    const menuContent = document.getElementById('toolbarMenuContent');

    // Toolbar menu behavior handled centrally in shared.js; recompute state when opened
      // Use shared toolbar initializer for open/close behavior. Listen for its
      // toggle event so we can recompute enabled state when opened.
      try {
        menuContainer?.addEventListener('oc-toolbar-toggle', (ev) => {
          try { if (ev?.detail?.open) updateOpenState(); } catch {}
        });
      } catch {}
      // Toolbar action bindings: close menu then invoke existing handlers
      document.getElementById('btnDeactivate')?.addEventListener('click', () => { try { window._oc_toolbar_closeAll && window._oc_toolbar_closeAll(); } catch {} doActivateDeactivate(); });
      document.getElementById('btnDelete')?.addEventListener('click',     () => { try { window._oc_toolbar_closeAll && window._oc_toolbar_closeAll(); } catch {} doDelete(); });

    // Ensure initial data load: populate store filter then load products
    try {
      if (typeof loadStores === 'function') {
        loadStores().finally(() => { try { load(); } catch {} });
      } else {
        try { load(); } catch {}
      }
    } catch {}
  });
})();