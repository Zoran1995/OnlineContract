/* global showToast */
(() => {
  const tbody = document.querySelector('#grid tbody');
  const itemsWrap = document.getElementById('itemsWrap');
  const accessDenied = document.getElementById('accessDenied');
  const pageInfo = document.getElementById('pageInfo');
  const pageCountInfo = document.getElementById('pageCountInfo');
  const prevPage = document.getElementById('btnPrev') || document.getElementById('prevPage');
  const nextPage = document.getElementById('btnNext') || document.getElementById('nextPage');
  const pageSizeSel = document.getElementById('pageSize');
  const btnOpen = document.getElementById('btnOpen');
  const btnDelete = document.getElementById('btnDelete');
  const btnSave = document.getElementById('btnSave');
  const submitCOD = document.getElementById('submitCOD');
  const submitOnline = document.getElementById('submitOnline');

  let contractId = 0;
  let selectedId = null;
  let page = 1, pageSize = 10, totalPages = 1, totalCount = 0;
  let items = [];
  let contractState = 'Draft';
  let headerStamp = 0;

  function showLoading(){ document.getElementById('loadingOverlay')?.classList.remove('hidden'); }
  function hideLoading(){ document.getElementById('loadingOverlay')?.classList.add('hidden'); }

  function parseIdFromPath(){
    const m = location.pathname.match(/contractshistory\/(\d+)/i);
    return m ? parseInt(m[1], 10) : 0;
  }

  function updateButtons(){
    const hasSel = !!selectedId;
    btnOpen && (btnOpen.disabled = !hasSel);
    btnDelete && (btnDelete.disabled = !hasSel || contractState !== 'Draft');
    btnSave && (btnSave.disabled = (contractState !== 'Draft'));
    if (submitCOD) {
      const submitLabel = submitCOD.parentElement && submitCOD.parentElement.parentElement && submitCOD.parentElement.parentElement.previousElementSibling;
      if (submitLabel) {
        const disable = (contractState !== 'Draft');
        try { submitLabel.disabled = disable; } catch {}
        try { if (disable) submitLabel.setAttribute('disabled', 'true'); else submitLabel.removeAttribute('disabled'); } catch {}
        try { submitLabel.setAttribute('aria-disabled', disable ? 'true' : 'false'); } catch {}
        try { submitLabel.classList.toggle('btn-disabled', disable); } catch {}
      }
    }
  }

  function renderPager(){
    const pages = Math.max(1, Number(totalPages || 1));
    if (page > pages) page = pages;
    if (pageInfo) pageInfo.textContent = `Page ${page} of ${pages}`;
    if (pageCountInfo) pageCountInfo.textContent = `Total records: ${totalCount}`;
    if (prevPage) prevPage.disabled = (page <= 1 || totalCount === 0);
    if (nextPage) nextPage.disabled = (page >= pages || totalCount === 0);
  }

  async function loadHeader(){
    const res = await fetch(`/api/contracts/${contractId}`, { credentials: 'include', headers: { 'Accept': 'application/json' }});
    if (res.status === 401) { window.location.href = '/login?mode=login'; return false; }
    if (res.status === 404 || res.status === 403) { itemsWrap.classList.add('hidden'); accessDenied.classList.remove('hidden'); return false; }
    if (!res.ok) return false;
    const j = await res.json();
    contractState = String(j.contractState || j.contract_state || 'Draft');
    headerStamp = Number(j.stamp || j.Stamp || 0) || 0;
    document.getElementById('title').textContent = `Contract ${j.id} · ${contractState}`;
    // Apply correct disabled/enabled state immediately after header loads
    try { updateButtons(); } catch {}
    return true;
  }

  async function loadItems(){
    const qs = new URLSearchParams({ page: String(page), pageSize: String(pageSize) });
    const res = await fetch(`/api/contracts/${contractId}/items?${qs}`, { credentials: 'include', headers: { 'Accept': 'application/json' }});
    if (res.status === 401) { window.location.href = '/login?mode=login'; return; }
    if (res.status === 404 || res.status === 403) { itemsWrap.classList.add('hidden'); accessDenied.classList.remove('hidden'); return; }
    const data = await res.json();
    items = Array.isArray(data.items) ? data.items : [];
    totalCount = Number(data.totalCount || items.length || 0);
    totalPages = Number(data.totalPages || Math.ceil(totalCount / pageSize) || 1);
    renderRows();
    renderPager();
  }

  function renderRows(){
    tbody.innerHTML = '';
    if (items.length === 0){
      const tr = document.createElement('tr'); const td = document.createElement('td'); td.colSpan = 8; td.textContent = 'No results.'; tr.appendChild(td); tbody.appendChild(tr); return;
    }
    items.forEach(r => {
      const tr = document.createElement('tr');
      tr.onclick = () => { Array.from(tbody.querySelectorAll('tr.active')).forEach(r2 => r2.classList.remove('active')); tr.classList.add('active'); selectedId = r.id; updateButtons(); };
      const amount = Number(r.amount || 0);
      const total = Number(r.amtGross || 0);
      const qty = Number(r.quantity || 0);
      const cells = [
        r.productName || '',
        r.size || '',
        r.color || '',
        String(qty),
        amount.toFixed(2),
        total.toFixed(2),
        r.itemStateText || String(r.itemStateId || ''),
        r.inputDt || ''
      ];
      for (const c of cells){ const td = document.createElement('td'); td.textContent = c; tr.appendChild(td); }
      tbody.appendChild(tr);
    });
  }

  async function init(){
    try {
      const role = parseInt(localStorage.getItem('roleId')||'0',10) || 0;
      const isLoggedIn = !!localStorage.getItem('userId');
      if (!isLoggedIn) { window.location.href = '/login?mode=login'; return; }
      if (role !== 5) { itemsWrap.classList.add('hidden'); accessDenied.classList.remove('hidden'); return; }
    } catch {}

    contractId = parseIdFromPath();
    if (!contractId) { accessDenied.classList.remove('hidden'); return; }

    showLoading();
    const ok = await loadHeader();
    if (ok) { itemsWrap.classList.remove('hidden'); await loadItems(); try { updateButtons(); } catch {} }
    hideLoading();
  }

  btnOpen?.addEventListener('click', () => {
    if (!selectedId) { try { showToast('warning', 'Select an item first.'); } catch {} return; }
    const it = items.find(x => x.id===selectedId) || null;
    if (!it) return;
    if (contractState === 'Draft') {
      try {
        window.openContractItemEditModal?.(it, contractId, headerStamp, async () => { await loadHeader(); await loadItems(); });
      } catch {}
    } else {
      try { window.openVariantModalReadOnly?.(it); } catch {}
    }
  });

  btnDelete?.addEventListener('click', async () => {
    if (!selectedId) return;
    if (contractState !== 'Draft') return;
    const dlg = document.getElementById('itemDeleteConfirm');
    const okBtn = document.getElementById('itemConfirmOk');
    if (!dlg || !okBtn || !dlg.showModal) {
      if (!confirm('Are you sure you want to delete this item? This action cannot be undone.')) return;
      await proceedItemDelete();
      return;
    }
    let resolved = false;
    const onOk = (e) => { e.preventDefault(); resolved = true; try { dlg.close(); } catch {} };
    okBtn.addEventListener('click', onOk, { once: true });
    dlg.addEventListener('close', async () => { if (resolved) { await proceedItemDelete(); } }, { once: true });
    try { dlg.showModal(); } catch { if (!confirm('Are you sure you want to delete this item? This action cannot be undone.')) return; await proceedItemDelete(); }
  });

  async function proceedItemDelete() {
    try {
      const res = await fetch(`/api/contracts/${contractId}/items/${selectedId}/delete`, { method:'POST', credentials:'include', headers:{'Accept':'application/json'} });
      if (res.ok){
        try { showToast('info','The item has been deleted successfully.'); } catch {}
        selectedId = null;
        const before = contractState;
        const ok = await loadHeader();
        // If contract got deleted (header 404/hidden) or state changed making it inaccessible, redirect to history
        if (!ok || before === 'Draft' && contractState !== 'Draft') {
          window.location.href = '/contractshistory';
          return;
        }
        await loadItems();
        try { window.refreshCartBadge?.(); } catch {}
      }
      else { try { showToast('error','Delete failed. Please try again.'); } catch {} }
    } catch {}
  }

  btnSave?.addEventListener('click', async () => {
    if (contractState !== 'Draft') return;
    try {
      const res = await fetch(`/api/contracts/${contractId}/save`, {
        method:'POST',
        credentials:'include',
        headers:{'Accept':'application/json','Content-Type':'application/json'},
        body: JSON.stringify({ contractStamp: headerStamp })
      });
      if (res.ok){ try { showToast('info','Your changes have been saved successfully.'); } catch {} await loadHeader(); }
      else { try { showToast('error','Save failed. Please try again.'); } catch {} }
    } catch {}
  });

  submitCOD?.addEventListener('click', async () => { await submit('cod'); });
  submitOnline?.addEventListener('click', async () => { await submit('online'); });
  async function submit(method){
    if (contractState !== 'Draft') return;
    try{
      const res = await fetch(`/api/contracts/${contractId}/submit?method=${encodeURIComponent(method)}`, { method:'POST', credentials:'include', headers:{'Accept':'application/json'} });
      const j = await res.json().catch(()=>({}));
      if (res.ok){ try { showToast('info', j.message || 'Your order has been submitted successfully. We’ll contact you shortly.'); } catch {} await loadHeader(); await loadItems(); }
      else {
        const msg = j.message || 'Submit failed';
        const isProfileIncomplete = msg === 'Please review your profile details and try again.';
        try { showToast(isProfileIncomplete ? 'warning' : 'error', msg); } catch {}
      }
    } catch{}
  }

  prevPage?.addEventListener('click', () => { if (page>1){ page--; loadItems(); }});
  nextPage?.addEventListener('click', () => { if (page<totalPages){ page++; loadItems(); }});
  pageSizeSel?.addEventListener('change', (e) => { pageSize = parseInt(e.target.value,10) || 10; page = 1; loadItems(); });

  document.addEventListener('DOMContentLoaded', init);
})();
