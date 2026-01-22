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
  const statItemsEl = document.getElementById('statItems');
  const statTotalEl = document.getElementById('statTotal');
  const statDateEl = document.getElementById('statDate');
  let submitDebounceUntil = 0;
  let redirectOverlayEl = null;

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
    if (pageInfo) pageInfo.innerHTML = `Page <b>${page}</b> of <b>${pages}</b>`;
    if (pageCountInfo) pageCountInfo.textContent = `Total records: ${totalCount}`;
    if (prevPage) prevPage.disabled = (page <= 1 || totalCount === 0);
    if (nextPage) nextPage.disabled = (page >= pages || totalCount === 0);
  }

  function updateStatTiles() {
    // Items count: use totalCount from API (all items, not just current page)
    if (statItemsEl) {
      statItemsEl.textContent = String(totalCount || 0);
    }

    // Total: sum of qty × price for all items on current page
    // Note: For accurate total across all pages, we'd need API support.
    // Here we compute from visible rows.
    let total = 0;
    for (const row of items) {
      const qty = Number(row.quantity || 0);
      const price = Number(row.amount || 0);
      total += qty * price;
    }
    if (statTotalEl) {
      statTotalEl.textContent = `RSD ${Math.round(total).toLocaleString('sr-RS')}`;
    }
  }

  function updateOrderDate(inputDt) {
    if (!statDateEl) return;
    if (!inputDt) {
      statDateEl.textContent = '—';
      return;
    }
    try {
      const dt = new Date(inputDt);
      if (isNaN(dt.getTime())) {
        statDateEl.textContent = '—';
        return;
      }
      // Format as YYYY-MM-DD for clean display
      const y = dt.getFullYear();
      const m = String(dt.getMonth() + 1).padStart(2, '0');
      const d = String(dt.getDate()).padStart(2, '0');
      statDateEl.textContent = `${y}-${m}-${d}`;
    } catch {
      statDateEl.textContent = '—';
    }
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
    // Update Order Date tile with input_dt (date only)
    updateOrderDate(j.inputDt || j.input_dt || j.entryDate || null);
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
    updateStatTiles();
  }

  function renderRows(){
    tbody.innerHTML = '';
    if (items.length === 0){
      const tr = document.createElement('tr'); tr.classList.add('empty'); const td = document.createElement('td'); td.colSpan = 8; td.textContent = 'No results.'; tr.appendChild(td); tbody.appendChild(tr); return;
    }
    items.forEach(r => {
      const tr = document.createElement('tr');
      tr.onclick = () => { Array.from(tbody.querySelectorAll('tr.active')).forEach(r2 => r2.classList.remove('active')); tr.classList.add('active'); selectedId = r.id; updateButtons(); };
      const amount = Number(r.amount || 0);
      const total = Number(r.amtGross || 0);
      const qty = Number(r.quantity || 0);
      const cells = [
        { val: r.productName || '', attr: null },
        { val: r.size || '', attr: null },
        { val: r.color || '', attr: null },
        { val: String(qty), attr: 'qty' },
        { val: amount.toFixed(2), attr: 'price' },
        { val: total.toFixed(2), attr: null },
        { val: r.itemStateText || String(r.itemStateId || ''), attr: null },
        { val: r.inputDt || '', attr: null }
      ];
      for (const c of cells){ const td = document.createElement('td'); td.textContent = c.val; if (c.attr) td.setAttribute('data-col', c.attr); tr.appendChild(td); }
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
    // If there was a recent payment attempt, check status and show failure toast if needed
    try {
      const lastRef = localStorage.getItem('lastPaymentReference') || '';
      const lastCid = parseInt(localStorage.getItem('lastContractId')||'0',10) || 0;
      if (lastRef && lastCid === contractId) {
        const res = await fetch(`/api/payments/status?reference=${encodeURIComponent(lastRef)}`, { credentials: 'include' });
        const js = await res.json().catch(()=>({}));
        if (js && js.status === 'Failed') {
          try { showToast('error', 'Payment failed. Please try again.'); } catch {}
          // Offer Try again button next to Submit
          ensureTryAgainButton();
        }
        if (js && (js.status === 'Succeeded' || js.status === 'Failed')) {
          localStorage.removeItem('lastPaymentReference');
          localStorage.removeItem('lastContractId');
        }
      }
    } catch{}
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
    const now = Date.now();
    if (method === 'online' && now < submitDebounceUntil) return;
    try{
      const res = await fetch(`/api/contracts/${contractId}/submit?method=${encodeURIComponent(method)}`, { method:'POST', credentials:'include', headers:{'Accept':'application/json'} });
      const j = await res.json().catch(()=>({}));
      if (res.ok){
        if (method === 'online' && j && j.redirectUrl){
          // Guard multiple submits
          submitDebounceUntil = Date.now() + 5000;
          // Disable submit button and mark as in-progress
          if (submitOnline) {
            try { submitOnline.disabled = true; } catch{}
            try { submitOnline.classList.add('btn-disabled'); } catch{}
            try { submitOnline.setAttribute('aria-disabled','true'); } catch{}
          }
          // In non-production stub flow, open simulation modal instead of navigating
          const urlStr = String(j.redirectUrl || '');
          const isMock = urlStr.startsWith('/payments/wspay/mock');
          if (isMock) {
            openSimulatePaymentModal(String(j.reference || ''));
          } else {
            // Show overlay and navigate in real flow
            showRedirectOverlay(urlStr);
          }
          // Persist reference for return pages and status polling
          try {
            if (j.reference) localStorage.setItem('lastPaymentReference', String(j.reference));
            localStorage.setItem('lastContractId', String(contractId));
          } catch{}
          return;
        }
        // For online method without redirectUrl, treat as error
        if (method === 'online') {
          try { showToast('error', j.message || 'Payment initialization failed. Please try again.'); } catch {}
        } else {
          try { showToast('info', j.message || 'Your order has been submitted successfully. We’ll contact you shortly.'); } catch {}
        }
        await loadHeader(); await loadItems();
      }
      else {
        const msg = j.message || 'Submit failed';
        const isProfileIncomplete = msg === 'Please review your profile details and try again.';
        try { showToast(isProfileIncomplete ? 'warning' : 'error', msg); } catch {}
      }
    } catch{}
  }

  function openSimulatePaymentModal(reference){
    try{
      const dlg = document.getElementById('simulatePaymentModal');
      const refEl = document.getElementById('simulateRef');
      const btnOk = document.getElementById('btnSimPaySuccess');
      const btnFail = document.getElementById('btnSimPayFailure');
      const btnCancel = document.getElementById('btnSimPayCancel');
      if (!dlg || !dlg.showModal || !btnOk || !btnFail || !btnCancel) return;
      if (refEl) refEl.textContent = reference || '';
      const onSuccess = async (e) => {
        e.preventDefault();
        try{
          const r = await fetch('/api/payments/wspay/mock-callback', { method:'POST', headers:{'Accept':'application/json','Content-Type':'application/json'}, body: JSON.stringify({ reference, status: 'Succeeded', contractId }) });
          const js = await r.json().catch(()=>({}));
          if (r.ok && js && js.success){
            try { dlg.close(); } catch {}
            // Refresh header and items to show Submitted state
            await loadHeader(); await loadItems(); updateButtons();
          } else {
            try { showToast('error', (js && js.message) || 'Payment failed. Please try again.'); } catch {}
            ensureTryAgainButton();
          }
        }catch{}
      };
      const onFailure = async (e) => {
        e.preventDefault();
        try{
          const r = await fetch('/api/payments/wspay/mock-callback', { method:'POST', headers:{'Accept':'application/json','Content-Type':'application/json'}, body: JSON.stringify({ reference, status: 'Failed', contractId }) });
          const js = await r.json().catch(()=>({}));
          try { showToast('error', (js && js.message) || 'Payment failed. Please try again.'); } catch {}
          ensureTryAgainButton();
          try { dlg.close(); } catch {}
        }catch{}
      };
      const onCancel = (e) => {
        e.preventDefault();
        try { dlg.close(); } catch {}
        try { window.location.href = '/payments/wspay/return/cancel'; } catch {}
      };
      btnOk.addEventListener('click', onSuccess, { once: true });
      btnFail.addEventListener('click', onFailure, { once: true });
      btnCancel.addEventListener('click', onCancel, { once: true });
      try { dlg.showModal(); } catch{}
    }catch{}
  }

  function showRedirectOverlay(redirectUrl){
    try {
      if (!redirectOverlayEl) {
        redirectOverlayEl = document.createElement('div');
        redirectOverlayEl.id = 'redirectOverlay';
        redirectOverlayEl.className = 'fixed inset-0 bg-black/30 flex items-center justify-center z-50';
        redirectOverlayEl.setAttribute('role','status');
        redirectOverlayEl.setAttribute('aria-live','polite');
        redirectOverlayEl.setAttribute('aria-busy','true');
        const inner = document.createElement('div');
        inner.className = 'bg-white rounded shadow p-6 max-w-md w-full text-center';
        inner.innerHTML = `
          <div class="flex items-center justify-center mb-4">
            <span class="loading loading-spinner loading-md" aria-hidden="true"></span>
          </div>
          <div class="font-medium mb-2">Redirecting to secure payment…</div>
          <a id="redirectFallbackLink" class="link" href="#" rel="nofollow">If you are not redirected automatically, click here.</a>
        `;
        redirectOverlayEl.appendChild(inner);
        document.body.appendChild(redirectOverlayEl);
      }
      const link = document.getElementById('redirectFallbackLink');
      if (link) { try { link.href = redirectUrl; } catch{} }
      // Move focus to overlay
      try { redirectOverlayEl.tabIndex = -1; redirectOverlayEl.focus(); } catch{}
      // Navigate in same tab
      try { window.location.href = redirectUrl; } catch{}
    } catch{}
  }

  function ensureTryAgainButton(){
    try {
      if (!submitOnline) return;
      const existing = document.getElementById('tryAgainBtn');
      if (existing) return;
      const btn = document.createElement('button');
      btn.id = 'tryAgainBtn';
      btn.className = 'btn btn-secondary ml-2';
      btn.textContent = 'Try again';
      btn.addEventListener('click', async () => { await submit('online'); });
      submitOnline.parentElement?.appendChild(btn);
    } catch{}
  }

  prevPage?.addEventListener('click', () => { if (page>1){ page--; loadItems(); }});
  nextPage?.addEventListener('click', () => { if (page<totalPages){ page++; loadItems(); }});
  pageSizeSel?.addEventListener('change', (e) => { pageSize = parseInt(e.target.value,10) || 10; page = 1; loadItems(); });

  document.addEventListener('DOMContentLoaded', init);
})();
