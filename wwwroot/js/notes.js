(() => {
  const apiBase = '/api/notes';
  let page = 1, pageSize = 10, totalPages = 1, totalCount = 0;
  let selectedId = null, selectedRow = null, items = [];
  let sortBy = '', sortDir = '';

  const tbl = document.querySelector('#notesTable tbody');
  const fltContract = document.getElementById('fltContractId');
  const fltProduct = document.getElementById('fltProductId');
  const btnSearch = document.getElementById('btnSearch');
  const btnClear = document.getElementById('btnClear');
  const btnOpen = document.getElementById('btnOpen');
  const btnAdd = document.getElementById('btnAddNote');
  const pgPrev = document.getElementById('pgPrev');
  const pgNext = document.getElementById('pgNext');
  const pgInfo = document.getElementById('pgInfo');
  const pageCountInfo = document.getElementById('pageCountInfo');
  const pageSizeSel = document.getElementById('pgSize');

  function showLoading(){}
  function hideLoading(){}

  async function load() {
    try {
      const q = new URLSearchParams();
      q.set('page', String(page));
      q.set('pageSize', String(pageSize));
      const c = (fltContract?.value || '').trim();
      const p = (fltProduct?.value || '').trim();
      if (c) q.set('contractId', c);
      if (p) q.set('productId', p);

      if (sortBy) q.set('sortBy', sortBy);
      if (sortDir) q.set('sortDir', sortDir);
      const res = await fetch(`${apiBase}?${q.toString()}`, { method: 'GET', credentials: 'include', headers: { 'Accept': 'application/json' }});
      if (!res.ok) { items = []; render(); return; }
      const data = await res.json();
      items = Array.isArray(data.items) ? data.items : [];
      totalCount = Number(data.totalCount ?? items.length);
      totalPages = Number(data.totalPages) || Math.max(1, Math.ceil(totalCount / pageSize));
      render();
    } catch (e) { items = []; render(); }
  }

  function render() {
    if (!tbl) return;
    tbl.innerHTML = '';
    if (!items || items.length === 0) {
      const tr = document.createElement('tr');
      const td = document.createElement('td'); td.colSpan = 7; td.className = 'empty'; td.textContent = 'No results.'; tr.appendChild(td); tbl.appendChild(tr); updatePager(); return;
    }
    items.forEach(it => {
      const tr = document.createElement('tr');
      tr.addEventListener('click', () => {
        Array.from(tbl.querySelectorAll('tr.active')).forEach(r=>r.classList.remove('active'));
        tr.classList.add('active');
        selectedId = it.id;
        selectedRow = it;
        updateOpenState();
        updateMenuState();
      });
      const cells = [ String(it.id ?? ''), String(it.contractId ?? ''), String(it.productId ?? ''), String(it.subject ?? ''), String(it.inputDt ?? ''), String(it.inputUserCode ?? it.inputUserId ?? ''), String(it.status ?? '') ];
      cells.forEach(text => { const td = document.createElement('td'); td.textContent = text; tr.appendChild(td); });
      tbl.appendChild(tr);
    });
    // If the previously selected row no longer exists in the newly loaded items, clear selection
    if (selectedId) {
      const found = items.find(x => String(x.id) === String(selectedId) || x.id === selectedId);
      if (!found) { selectedId = null; selectedRow = null; Array.from(tbl.querySelectorAll('tr.active')).forEach(r=>r.classList.remove('active')); updateOpenState(); }
    }
    updatePager();
    // Keep menu state in sync after render
    updateMenuState();
    // Update header sort indicators and make headers clickable for server-side sorting
    try {
      const keys = ['id','contractId','productId','subject','inputDt','inputUserId','status'];
      document.querySelectorAll('#notesTable thead th').forEach((th, idx) => {
        th.style.cursor = 'pointer';
        // remove existing indicator
        const ex = th.querySelector('.sort-indicator'); if (ex) ex.remove();
        const key = keys[idx] || '';
        const span = document.createElement('span'); span.className = 'sort-indicator ml-2';
        if (key && key === sortBy) { span.textContent = sortDir === 'desc' ? ' ▼' : ' ▲'; }
        th.appendChild(span);
        th.onclick = () => {
          if (!key) return;
          if (sortBy === key) sortDir = (sortDir === 'desc' ? 'asc' : 'desc');
          else { sortBy = key; sortDir = 'asc'; }
          page = 1; load();
        };
      });
    } catch {}
  }

  function updateOpenState(){
    if (!btnOpen) return;
    // Disable Open when no selection OR when selected note is inactive
    btnOpen.disabled = !(selectedId && selectedRow && !!selectedRow.isActive);
  }

    function updateMenuState(){
    const btnMoreEl = document.getElementById('btnMore');
    const mDeactivate = document.getElementById('notesMenuDeactivate');
    const mDelete = document.getElementById('notesMenuDelete');
    const mSetMain = document.getElementById('notesMenuSetMain');
    // Three-dots button should always be enabled (per Users toolbar behavior)
    if (btnMoreEl) btnMoreEl.disabled = false;
    const hasSelection = !!selectedId && !!selectedRow;
    const setItemState = (el, enabled) => {
      if (!el) return;
      if (enabled) { el.classList.remove('opacity-50'); el.classList.remove('pointer-events-none'); el.removeAttribute('aria-disabled'); }
      else { el.classList.add('opacity-50'); el.classList.add('pointer-events-none'); el.setAttribute('aria-disabled','true'); }
    };
    // Delete enabled only when a row is selected
    let isSystem = false;
    try { isSystem = (String(selectedRow?.inputUserCode || '').toLowerCase() === 'system') || (Number(selectedRow?.inputUserId) === 2); } catch {}
    setItemState(mDelete, hasSelection && !isSystem);
    // Set Main only for product-linked AND active notes when a row is selected
    setItemState(mSetMain, hasSelection && !isSystem && selectedRow && Number(selectedRow.productId) > 0 && (selectedRow.isActive === undefined || selectedRow.isActive === null ? true : !!selectedRow.isActive));
    // Deactivate/Activate enabled only when a row is selected
    setItemState(mDeactivate, hasSelection && !isSystem);
    // Update Deactivate/Activate icon + label
    if (mDeactivate) {
      const active = selectedRow?.isActive;
      const icon = (active === false) ? 'check_circle' : 'block';
      const label = (active === false) ? 'Activate' : 'Deactivate';
      mDeactivate.innerHTML = `<span class="material-icons mr-2">${icon}</span>${label}`;
    }
    // Ensure Delete and SetMain have icons
    if (mDelete) mDelete.innerHTML = `<span class="material-icons mr-2">delete</span>Delete`;
    if (mSetMain) mSetMain.innerHTML = `<span class="material-icons mr-2">star</span>Set as Main`;
  }

  function closeMenu(){
    const btnMoreEl = document.getElementById('btnMore');
    const dd = btnMoreEl?.closest('.dropdown');
    if (dd && dd.classList.contains('dropdown-open')) {
      dd.classList.remove('dropdown-open');
      btnMoreEl?.setAttribute('aria-expanded','false');
      const menu = dd.querySelector('.dropdown-content');
      if (menu) menu.classList.add('hidden');
    }
  }
  function updatePager(){
    const total = Number(totalCount) || 0;
    const pages = Math.max(1, Number(totalPages) || Math.ceil(total / pageSize));
    const start = total === 0 ? 0 : ((page - 1) * pageSize) + 1;
    const end = total === 0 ? 0 : Math.min(page * pageSize, total);
    if (pgInfo) pgInfo.innerHTML = `Page <b>${page}</b> of <b>${pages}</b>`;
    if (pageCountInfo) pageCountInfo.textContent = `Total records: ${total}`;
    if (pgPrev) pgPrev.disabled = page<=1;
    if (pgNext) pgNext.disabled = page>=pages;
  }

  document.addEventListener('DOMContentLoaded', () => {
    // Do not trigger a load on initial page open — start with an empty grid
    pageSizeSel?.addEventListener('change', (e)=>{ pageSize = Number(e.target.value)||10; page=1; load(); });

    // Perform a search; when requireFilter=true, at least one filter must be provided (same behavior as UI button)
    const performSearch = (requireFilter = true) => {
      try {
        const c = (fltContract?.value || '').trim();
        const p = (fltProduct?.value || '').trim();
        if (requireFilter && !c && !p) { try { showToast('warning', 'Provide more search criteria before proceeding.'); } catch {} return; }
        page = 1; load();
      } catch (e) { /* noop */ }
    };

    btnSearch?.addEventListener('click', (e)=>{ e.preventDefault(); performSearch(true); });
    btnClear?.addEventListener('click', (e)=>{ e.preventDefault(); if(fltContract) fltContract.value=''; if(fltProduct) fltProduct.value=''; page=1; items = []; render(); });
    pgPrev?.addEventListener('click', ()=>{ if(page>1){ page--; load(); } });
    pgNext?.addEventListener('click', ()=>{ if(page<totalPages){ page++; load(); } });
    btnOpen?.addEventListener('click', async ()=>{
      if (!selectedId) { try { showToast('warning','Please select a row first.'); } catch {} return; }
      try {
        const res = await fetch(`${apiBase}/${selectedId}`, { method: 'GET', credentials: 'include', headers: { 'Accept': 'application/json' }});
        if (!res.ok) { try { showToast('error', `Failed to load note (${res.status}).`); } catch {} return; }
        const data = await res.json();
        // populate modal for editing (guard elements)
        const editIdEl = document.getElementById('editNoteId');
        const subjEl = document.getElementById('newSubject');
        const commEl = document.getElementById('newComment');
        // Contract ID elements
        const cInput = document.getElementById('newContractIdInput');
        const cDisplay = document.getElementById('newContractIdDisplay');
        const cLink = document.getElementById('newContractIdLink');
        // Product ID elements
        const pInput = document.getElementById('newProductIdInput');
        const pDisplay = document.getElementById('newProductIdDisplay');
        const pLink = document.getElementById('newProductIdLink');
        const titleEl = document.getElementById('noteModalTitle');
        const inputDtEl = document.getElementById('noteInputDt');
        const inputUserEl = document.getElementById('noteInputUser');
        const lastUpdEl = document.getElementById('noteLastUpdatedDt');
        const lastModByEl = document.getElementById('noteLastModifiedBy');

        if (editIdEl) editIdEl.value = String(data.id ?? '');
        if (subjEl) { subjEl.value = data.subject ?? ''; }
        if (commEl) { commEl.value = data.comment ?? ''; }
        // contract/product - show as links when viewing/editing
        if (cInput) cInput.style.display = 'none';
        if (pInput) pInput.style.display = 'none';
        if (data.contractId != null) {
          if (cDisplay) cDisplay.style.display = 'flex';
          if (cLink) { cLink.href = `/contracts/${data.contractId}`; cLink.textContent = String(data.contractId); }
        } else {
          if (cDisplay) cDisplay.style.display = 'none';
        }
        if (data.productId != null) {
          if (pDisplay) pDisplay.style.display = 'flex';
          if (pLink) { pLink.href = `/products/${data.productId}`; pLink.textContent = String(data.productId); }
        } else {
          if (pDisplay) pDisplay.style.display = 'none';
        }
        if (titleEl) titleEl.textContent = `Edit Note ${data.id ?? ''}`;

        // metadata fields (best-effort)
        if (inputDtEl) inputDtEl.value = data.inputDt ?? '';
        if (inputUserEl) inputUserEl.value = data.inputUserCode ?? (data.inputUserId ? String(data.inputUserId) : '');
        if (lastUpdEl) lastUpdEl.value = data.lastUpdatedDt ?? '';
        if (lastModByEl) lastModByEl.value = data.lastModifiedByCode ?? (data.lastModifiedById ? String(data.lastModifiedById) : '');

        // Active/Main are managed via the actions menu; modal does not expose controls.

        const dlg = document.getElementById('addNoteModal');
        // System notes are read-only in modal
        const isSystemNote = (Number(data.inputUserId) === 2) || (String(data.inputUserCode || '').toLowerCase() === 'system');
        if (isSystemNote) {
          try { subjEl && (subjEl.disabled = true); commEl && (commEl.disabled = true); } catch {}
          const saveBtn = document.getElementById('addNoteSave'); if (saveBtn) { saveBtn.setAttribute('disabled','true'); saveBtn.classList.add('opacity-50','pointer-events-none'); }
        } else {
          try { subjEl && (subjEl.disabled = false); commEl && (commEl.disabled = false); } catch {}
          const saveBtn = document.getElementById('addNoteSave'); if (saveBtn) { saveBtn.removeAttribute('disabled'); saveBtn.classList.remove('opacity-50','pointer-events-none'); }
        }
        try{ dlg.showModal(); } catch { if (dlg) dlg.classList.remove('hidden'); }
      } catch (e) { try { showToast('error','Failed to load note.'); } catch {} }
    });
    btnAdd?.addEventListener('click', ()=>{
      // Ensure Add always creates a new note regardless of current selection
      selectedId = null;
      try { render(); } catch {}
      try { updateOpenState(); } catch {}
      // clear modal inputs for new note and enable contract/product inputs
      document.getElementById('editNoteId').value = '';
      document.getElementById('newSubject').value = '';
      document.getElementById('newComment').value = '';
      // Show input fields, hide link displays for Add mode
      const cInput = document.getElementById('newContractIdInput');
      const cDisplay = document.getElementById('newContractIdDisplay');
      const pInput = document.getElementById('newProductIdInput');
      const pDisplay = document.getElementById('newProductIdDisplay');
      if (cInput) { cInput.value = ''; cInput.style.display = 'block'; }
      if (cDisplay) cDisplay.style.display = 'none';
      if (pInput) { pInput.value = ''; pInput.style.display = 'block'; }
      if (pDisplay) pDisplay.style.display = 'none';
      // Modal does not expose Active/Main controls; keep contract/product editable on Add.
      document.getElementById('noteModalTitle').textContent = 'Add Note';
      const dlg = document.getElementById('addNoteModal');
      try{ dlg.showModal(); }catch{ dlg.classList.remove('hidden'); }
    });

    // Trigger search when Enter is pressed in any filter input (behaves like clicking Search)
    [fltContract, fltProduct].forEach(inp => {
      try {
        inp?.addEventListener('keydown', (ev) => {
          if (ev.key === 'Enter') { ev.preventDefault(); performSearch(true); }
        });
      } catch {}
    });

    // Add note save
    const addSave = document.getElementById('addNoteSave');
    addSave?.addEventListener('click', async (e)=>{
      e.preventDefault();
      const subject = document.getElementById('newSubject')?.value || '';
      const comment = document.getElementById('newComment')?.value || '';
      const contractIdVal = document.getElementById('newContractIdInput')?.value || '';
      const productIdVal = document.getElementById('newProductIdInput')?.value || '';
      // client-side validation: subject & comment required
      if (!subject.trim() || !comment.trim()) { try{ showToast('warning', 'Both Subject and Comment are required. Please provide values for these fields before saving.'); }catch{}; return; }
      // require at least one of contractId or productId
      if (!contractIdVal.trim() && !productIdVal.trim()) { try{ showToast('warning', 'Please provide either a Contract Id or a Product Id. Enter a numeric id from the Contracts or Products list.'); }catch{}; return; }
      // do not allow both to be filled at the same time
      if (contractIdVal.trim() && productIdVal.trim()) { try{ showToast('warning', 'Please provide only one target: either a Contract Id or a Product Id, not both.'); }catch{}; return; }
      const contractId = contractIdVal === '' ? null : Number(contractIdVal);
      const productId = productIdVal === '' ? null : Number(productIdVal);
      const editId = (document.getElementById('editNoteId')?.value || '').trim();
      // ensure numeric ids when provided
      if (contractIdVal.trim() && (Number.isNaN(contractId) || !Number.isInteger(contractId) || contractId <= 0)) { try{ showToast('warning','Contract Id must be a positive integer (enter an existing Contract Id).'); }catch{}; return; }
      if (productIdVal.trim() && (Number.isNaN(productId) || !Number.isInteger(productId) || productId <= 0)) { try{ showToast('warning','Product Id must be a positive integer (enter an existing Product Id).'); }catch{}; return; }
      try {
        const payload = { Subject: subject, Comment: comment, ContractId: contractId, ProductId: productId };
        let res;
        if (editId) {
          res = await fetch(`/api/notes/${encodeURIComponent(editId)}`, { method: 'PUT', credentials: 'include', headers: {'Content-Type':'application/json','Accept':'application/json'}, body: JSON.stringify(payload)});
        } else {
          res = await fetch('/api/notes', { method: 'POST', credentials: 'include', headers: {'Content-Type':'application/json','Accept':'application/json'}, body: JSON.stringify(payload)});
        }
        if (res) {
          if (res.ok) {
            try{ const msg = editId ? 'Note has been successfully updated.' : 'Note has been added successfully.'; showToast('info', msg); }catch{};
            const dlg = document.getElementById('addNoteModal'); try{ dlg.close(); }catch{}; await load();
          } else {
            // Try to show a helpful message returned by the server (BadRequest etc.)
            try {
              const ct = res.headers.get('content-type') || '';
              if (ct.indexOf('application/json') !== -1) {
                const data = await res.json();
                const msg = data?.message || data?.Message || (data?.message_text ?? null) || null;
                if (msg) { showToast('warning', msg); }
                else { showToast('error', 'Failed to save the note. Please try again later.'); }
              } else {
                showToast('error', 'Failed to save the note. Please try again later.');
              }
            } catch (ee) { try{ showToast('error','Failed to save the note. Please try again later.'); }catch{} }
          }
        }
      } catch { try{ showToast('error','Failed to save note'); }catch{} }
    });

    // Actions menu handlers (Deactivate/Delete/Set as Main)
    const btnMore = document.getElementById('btnMore');
    const menuDeactivate = document.getElementById('notesMenuDeactivate');
    const menuDelete = document.getElementById('notesMenuDelete');
    const menuSetMain = document.getElementById('notesMenuSetMain');

    // Initialize menu state and wire open/close behavior (robust toggling)
    try {
      updateMenuState();
      const notesDropdown = btnMore?.closest('.dropdown');
      const notesMenu = notesDropdown ? notesDropdown.querySelector('.dropdown-content') : null;

      // Toolbar kebab is handled by shared initializer; recompute menu state when opened
      try {
        notesDropdown?.addEventListener('oc-toolbar-toggle', (ev) => {
          try { const open = ev?.detail?.open; if (open) updateMenuState(); } catch {}
        });
      } catch {}
    } catch {}

    async function fetchNoteMeta(id) {
      try {
        const r = await fetch(`${apiBase}/${id}`, { method: 'GET', credentials: 'include', headers: { 'Accept': 'application/json' } });
        if (!r.ok) return null;
        return await r.json();
      } catch { return null; }
    }

    menuDeactivate?.addEventListener('click', async (e) => {
      try { window._oc_toolbar_closeAll && window._oc_toolbar_closeAll(); } catch {}
      if (!selectedId) { try { showToast('warning', 'Please select a note first.'); } catch {} return; }
      try {
        const meta = await fetchNoteMeta(selectedId);
        if (!meta) { try { showToast('error','Failed to retrieve note info.'); } catch {} return; }
        const newActive = !meta.isActive;
        const res = await fetch(`${apiBase}/${selectedId}`, { method: 'PUT', credentials: 'include', headers: {'Content-Type':'application/json','Accept':'application/json'}, body: JSON.stringify({ IsActive: newActive }) });
        if (res && res.ok) {
          try { showToast('info', newActive ? 'Note has been activated.' : 'Note has been deactivated.'); } catch {}
        } else {
          try { showToast('error','Failed to change active state.'); } catch {}
        }
      } catch (e) {
        try { showToast('error','Failed to change active state.'); } catch {}
      } finally {
        // Clear selection after action (success or failure) and recompute disabled state
        selectedId = null; selectedRow = null;
        try { render(); } catch {}
        await load();
        try { updateMenuState(); } catch {}
      }
    });

    menuDelete?.addEventListener('click', async (e) => {
      try { window._oc_toolbar_closeAll && window._oc_toolbar_closeAll(); } catch {}
      if (!selectedId) { try { showToast('warning', 'Please select a note first.'); } catch {} return; }
      try {
        const meta = await fetchNoteMeta(selectedId);
        if (!meta) { try { showToast('error','Failed to retrieve note info.'); } catch {} return; }
        // Decide whether note is product or contract linked
        if (meta.productId && Number(meta.productId) > 0) {
          const body = { Delete: [{ Id: selectedId, Stamp: meta.stamp }] };
          const r = await fetch(`/api/products/${meta.productId}/notes`, { method: 'PUT', credentials: 'include', headers: {'Content-Type':'application/json','Accept':'application/json'}, body: JSON.stringify(body) });
          if (r && r.ok) { try { showToast('info','The note was deleted successfully.'); } catch {} 
            // clear selection after action
            selectedId = null; selectedRow = null; try { render(); } catch {};
            await load(); updateMenuState(); } else { try { showToast('error','Failed to delete note.'); } catch {} }
        } else if (meta.contractId && Number(meta.contractId) > 0) {
          const body = { Delete: [{ Id: selectedId, Stamp: meta.stamp }] };
          const r = await fetch(`/api/contracts/${meta.contractId}/notes`, { method: 'PUT', credentials: 'include', headers: {'Content-Type':'application/json','Accept':'application/json'}, body: JSON.stringify(body) });
          if (r && r.ok) { try { showToast('info','The note was deleted successfully.'); } catch {} 
            selectedId = null; selectedRow = null; try { render(); } catch {};
            await load(); updateMenuState(); } else { try { showToast('error','Failed to delete note.'); } catch {} }
        } else {
          try { showToast('error','Note is not linked to a product or contract; cannot delete.'); } catch {}
        }
      } catch (e) { try { showToast('error','Failed to delete note.'); } catch {} }
    });

    menuSetMain?.addEventListener('click', async (e) => {
      try { window._oc_toolbar_closeAll && window._oc_toolbar_closeAll(); } catch {}
      if (!selectedId) { try { showToast('warning', 'Please select a note first.'); } catch {} return; }
      try {
        const meta = await fetchNoteMeta(selectedId);
        if (!meta) { try { showToast('error','Failed to retrieve note info.'); } catch {} return; }
        if (meta.isMain) { try { showToast('info','This note is already set as main.'); } catch {} return; }
        if (meta.productId && Number(meta.productId) > 0) {
          const body = { SetMainId: selectedId, SetMainStamp: meta.stamp };
          const r = await fetch(`/api/products/${meta.productId}/notes`, { method: 'PUT', credentials: 'include', headers: {'Content-Type':'application/json','Accept':'application/json'}, body: JSON.stringify(body) });
          if (r && r.ok) { try { showToast('info','Note has been set as main.'); } catch {} 
            // clear selection after action
            selectedId = null; selectedRow = null; try { render(); } catch {};
            await load(); updateMenuState(); } else { try { showToast('error','Failed to set note as main.'); } catch {} }
        } else if (meta.contractId && Number(meta.contractId) > 0) {
          const body = { SetMainId: selectedId, SetMainStamp: meta.stamp };
          const r = await fetch(`/api/contracts/${meta.contractId}/notes`, { method: 'PUT', credentials: 'include', headers: {'Content-Type':'application/json','Accept':'application/json'}, body: JSON.stringify(body) });
          if (r && r.ok) { try { showToast('info','Note has been set as main.'); } catch {} await load(); updateMenuState(); } else { try { showToast('error','Failed to set note as main.'); } catch {} }
        } else {
          try { showToast('error','Note is not linked to a product or contract; cannot set as main.'); } catch {}
        }
      } catch (e) { try { showToast('error','Failed to set note as main.'); } catch {} }
    });

    // Initial page open: do NOT perform automatic search; start with empty grid.
    // User must click Search (or press Enter in a filter) to populate results.
  });
})();
