/* global showToast */
(() => {
  const apiBase = "/api/approval-rules";

  let page = 1;
  let pageSize = 10;
  let totalPages = 1;
  let totalCount = 0;
  let selectedId = null;
  let items = [];
  let sortBy = null; // 'id' | 'name' | 'description' | 'status' | 'context'
  let sortDir = 'asc'; // 'asc' | 'desc'

  const btnOpen = document.getElementById("btnOpen");
  const btnToolbarMenu = document.getElementById("btnToolbarMenu");
  const menu = document.getElementById("toolbarMenu");
  const menuContent = document.getElementById("toolbarMenuContent");
  const btnToggleActive = document.getElementById("btnToggleActive");
  const toggleText = document.getElementById("toggleText");
  const toggleIcon = document.getElementById("toggleIcon");
  const btnDelete = document.getElementById("btnDelete");

  const tbody = document.querySelector('#grid tbody');
  const pageInfo = document.getElementById('pageInfo');
  const pageCountInfo = document.getElementById('pageCountInfo');
  const prevPage = document.getElementById('prevPage');
  const nextPage = document.getElementById('nextPage');
  const pageSizeSel = document.getElementById('pageSize');

  const modal = document.getElementById('approvalRuleModal');
  const modalTitle = document.getElementById('approvalRuleModalTitle');
  const arName = document.getElementById('arName');
  const arDescription = document.getElementById('arDescription');
  const arAssignedTo = document.getElementById('arAssignedTo');
  const arContext = document.getElementById('arContext');
  const arAmount = document.getElementById('arAmount');
  const arPercent = document.getElementById('arPercent');
  const arCancel = document.getElementById('arCancel');
  const arSave = document.getElementById('arSave');

  function showLoading() { document.getElementById('loadingOverlay')?.classList.remove('hidden'); }
  function hideLoading() { document.getElementById('loadingOverlay')?.classList.add('hidden'); }

  function updateActions() {
    const hasSel = !!selectedId;
    if (btnOpen) btnOpen.disabled = !hasSel;
    [btnToggleActive, btnDelete].forEach(btn => {
      if (!btn) return;
      btn.classList.toggle('disabled', !hasSel);
      btn.classList.toggle('opacity-50', !hasSel);
      btn.classList.toggle('cursor-not-allowed', !hasSel);
      btn.setAttribute('aria-disabled', (!hasSel).toString());
      btn.style.pointerEvents = hasSel ? '' : 'none';
      btn.tabIndex = hasSel ? 0 : -1;
    });

    // Toggle Activate/Deactivate label based on selected row state
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
      try { showToast('error', 'Failed to load approval rules'); } catch {}
    } finally { hideLoading(); }
  }

  function renderRows() {
    if (!tbody) return;
    tbody.innerHTML = '';
    if (items.length === 0) {
      const tr = document.createElement('tr');
      const td = document.createElement('td');
      td.colSpan = 5; td.className = 'empty'; td.textContent = 'No results.';
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
        // Normalize undefined/null
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
      const statusText = it.isActive ? 'Active' : 'Inactive';
      const contextText = (it.contextId === 41) ? 'Refund Payment' : (it.contextId === 42) ? 'Contract Write-Off' : '';
      const cells = [ String(it.id), String(it.name || ''), String(it.description || ''), statusText, contextText ];
      cells.forEach(text => { const td = document.createElement('td'); td.textContent = text; tr.appendChild(td); });
      tbody.appendChild(tr);
    });
  }

  function getSortVal(it, key) {
    switch (key) {
      case 'id': return Number(it.id || 0);
      case 'name': return String(it.name || '');
      case 'description': return String(it.description || '');
      case 'status': return it.isActive ? 1 : 0; // active after inactive when asc
      case 'context': {
        const ctx = (it.contextId === 41) ? 'Refund Payment' : (it.contextId === 42) ? 'Contract Write-Off' : '';
        return ctx;
      }
      default: return '';
    }
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

  async function populateAssignedTo() {
    try {
      const res = await fetch(`/api/groups?q=&page=1&pageSize=200`, { method: 'GET', credentials: 'include', headers: { 'Accept': 'application/json' } });
      if (!res.ok) return;
      const data = await res.json();
      const items = Array.isArray(data.items) ? data.items : [];
      arAssignedTo.innerHTML = '';
      items.forEach(g => {
        const opt = document.createElement('option');
        opt.value = String(g.id);
        opt.textContent = g.code || g.fullName || `Group ${g.id}`;
        arAssignedTo.appendChild(opt);
      });
    } catch {}
  }

  function populateContext(contextId) {
    if (!arContext) return;
    // Populate from known seeded values; dropdown is disabled (read-only)
    const options = [
      { id: 41, text: 'Refund Payment' },
      { id: 42, text: 'Contract Write-Off' }
    ];
    arContext.innerHTML = '';
    options.forEach(o => {
      const opt = document.createElement('option');
      opt.value = String(o.id);
      opt.textContent = o.text;
      arContext.appendChild(opt);
    });
    if (contextId) arContext.value = String(contextId);
  }

  function validateModal() {
    const name = (arName?.value || '').trim();
    const desc = (arDescription?.value || '').trim();
    const assignedId = parseInt(arAssignedTo?.value || '0', 10) || 0;
    const amt = Number(arAmount?.value || 0);
    const pct = Number(arPercent?.value || 0);
    const errs = [];
    if (!name) errs.push('Name is required.');
    if (!desc) errs.push('Description is required.');
    if (!assignedId) errs.push('Assigned To is required.');
    const amtPos = amt > 0; const pctPos = pct > 0;
    if (!(amtPos ^ pctPos)) errs.push('Enter either Amount Threshold or Amount Percentage (strictly > 0).');
    if (pct < 0 || pct > 100) errs.push('Amount Percentage must be between 0 and 100.');
    return errs;
  }

  async function openModal() {
    if (!selectedId) { try { showToast('warning', 'Select a row first'); } catch {} return; }
    try {
      const res = await fetch(`${apiBase}/${selectedId}`, { method: 'GET', credentials: 'include', headers: { 'Accept': 'application/json' } });
      if (!res.ok) throw new Error('HTTP '+res.status);
      const data = await res.json();
      await populateAssignedTo();
      modalTitle.textContent = `Approval Rule ${data.id}`;
      arName.value = String(data.name || '');
      arDescription.value = String(data.description || '');
      arAssignedTo.value = String(data.assignedToId || '');
      populateContext(data.contextId || 0);
      arAmount.value = Number(data.amtThreshold || 0).toFixed(2);
      arPercent.value = Number(data.pctThreshold || 0).toFixed(2);
      modal.dataset.stamp = String(data.stamp || '0');
      modal.showModal();
    } catch { try { showToast('error','Failed to load approval rule.'); } catch {} }
  }

  async function saveModal() {
    const errs = validateModal();
    if (errs.length) { try { showToast('error', errs.join('\n')); } catch {} return; }
    try {
      const payload = {
        name: (arName.value || '').trim(),
        description: (arDescription.value || '').trim(),
        assignedToId: parseInt(arAssignedTo.value || '0', 10) || 0,
        amtThreshold: Number(arAmount.value || 0),
        pctThreshold: Number(arPercent.value || 0),
        stamp: parseInt(modal.dataset.stamp || '0', 10) || 0
      };
      const res = await fetch(`${apiBase}/${selectedId}`, { method: 'PUT', credentials: 'include', headers: { 'Content-Type': 'application/json', 'Accept': 'application/json' }, body: JSON.stringify(payload) });
      if (res.status === 401) { window.location.href = '/login?mode=login'; return; }
      const j = await res.json();
      if (!res.ok || j.success === false) { throw new Error(j.message || (`HTTP ${res.status}`)); }
      try { showToast('info', 'Approval Rule has been saved.'); } catch {}
      try { modal.close(); } catch {}
      await load();
    } catch (err) { try { showToast('error', err.message || 'Failed to save approval rule.'); } catch {} }
  }

  async function activateDeactivate(kind) {
    if (!selectedId) return;
    const row = (items || []).find(x => x.id === selectedId);
    const stamp = row ? row.stamp : 0;
    try {
      const res = await fetch(`${apiBase}/${selectedId}/${kind}?stamp=${encodeURIComponent(stamp)}`, { method: 'POST', credentials: 'include', headers: { 'Accept':'application/json' } });
      const j = await res.json().catch(() => ({ success: res.ok }));
      if (!res.ok || j.success === false) throw new Error(j.message || 'Operation failed');
      try { showToast('info', `Approval Rule ${kind}d successfully.`); } catch {}
      await load();
    } catch (err) { try { showToast('error', err.message || 'Operation failed'); } catch {} }
  }

  async function deleteRule() {
    if (!selectedId) return;
    const row = (items || []).find(x => x.id === selectedId);
    const stamp = row ? row.stamp : 0;
    try {
      const res = await fetch(`${apiBase}/${selectedId}/delete?stamp=${encodeURIComponent(stamp)}`, { method: 'POST', credentials: 'include', headers: { 'Accept':'application/json' } });
      const j = await res.json().catch(() => ({ success: res.ok }));
      if (!res.ok || j.success === false) throw new Error(j.message || 'Delete failed');
      try { showToast('info', 'Approval Rule deleted successfully.'); } catch {}
      selectedId = null;
      await load();
    } catch (err) { try { showToast('error', err.message || 'Delete failed'); } catch {} }
  }

  document.addEventListener('DOMContentLoaded', () => {
    // Check if user is privileged before loading data
    const isLoggedIn = localStorage.getItem('isLoggedIn') === 'true';
    const roleId = parseInt(localStorage.getItem('roleId') || '0', 10);
    const isPrivileged = isLoggedIn && (roleId === 7 || roleId === 8);
    if (!isPrivileged) return; // Access Denied is handled in HTML script
    
    // Explicitly hide Export CSV on this page regardless of navbar layout tweaks
    try { document.getElementById('exportBtn')?.classList.add('hidden'); } catch {}

    load();

    btnOpen?.addEventListener('click', (e) => { e.preventDefault(); openModal(); });

    // Pager
    prevPage?.addEventListener('click', () => { if (page > 1) { page--; load(); } });
    nextPage?.addEventListener('click', () => { const pages = Math.max(1, Number(totalPages||1)); if (page < pages) { page++; load(); } });
    pageSizeSel?.addEventListener('change', (e) => { pageSize = parseInt(e.target.value, 10) || 10; page = 1; load(); });

    // Column sorting
    const ths = document.querySelectorAll('#grid thead th');
    ths.forEach((th, idx) => {
      const key = ['id', 'name', 'description', 'status', 'context'][idx];
      if (!key) return;
      th.style.cursor = 'pointer';
      th.title = 'Click to sort';
      th.addEventListener('click', () => {
        if (sortBy === key) {
          sortDir = sortDir === 'asc' ? 'desc' : 'asc';
        } else {
          sortBy = key; sortDir = 'asc';
        }
        // Simple indicator: append arrow
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

    // Kebab options
    btnToggleActive?.addEventListener('click', (e) => {
      e.preventDefault();
      if (selectedId) {
        const row = (items || []).find(x => x.id === selectedId);
        const isActive = !!(row && row.isActive);
        activateDeactivate(isActive ? 'deactivate' : 'activate');
      }
      menu.classList.remove('dropdown-open');
      menuContent.classList.add('hidden');
    });
    btnDelete?.addEventListener('click', (e) => { e.preventDefault(); if (selectedId) { deleteRule(); } menu.classList.remove('dropdown-open'); menuContent.classList.add('hidden'); });

    // Modal
    arCancel?.addEventListener('click', (e) => { e.preventDefault(); try { modal.close(); } catch {} });
    arSave?.addEventListener('click', (e) => { e.preventDefault(); saveModal(); });
  });
})();
