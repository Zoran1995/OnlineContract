/* global showToast */
(function () {
  const loadingOverlay = document.getElementById('loadingOverlay');
  const detailsEmpty = document.getElementById('detailsEmpty');
  const pageTitle = document.getElementById('pageTitle');
  const pendingBanner = document.getElementById('pendingTasksBanner');
  const pendingBannerText = document.getElementById('pendingTasksText');

  // Basic details elements
  const contractIdEl = document.getElementById('contractId');
  const entryDateEl = document.getElementById('entryDate');
  const customerFullNameEl = document.getElementById('customerFullName');
  const contractStateEl = document.getElementById('contractState');
  const inputUserCodeEl = document.getElementById('inputUserCode');
  const lastModifiedByCodeEl = document.getElementById('lastModifiedByCode');
  const btnContractState = document.getElementById('btnContractState');

  // Items UI
  const itemsTableBody = document.querySelector('#itemsTable tbody');
  const btnOpenItem = document.getElementById('btnOpenItem');
  const btnChangeState = document.getElementById('btnChangeState');
  const itemsPageInfo = document.getElementById('itemsPageInfo');
  const itemsPageCountInfo = document.getElementById('itemsPageCountInfo');
  const itemsPrev = document.getElementById('itemsPrev');
  const itemsNext = document.getElementById('itemsNext');
  const itemsPageSizeSel = document.getElementById('itemsPageSizeSel');

  // Notes UI
  const notesTableBody = document.querySelector('#notesTable tbody');
  const btnOpenNote = document.getElementById('btnOpenNote');
  const btnAddNote = document.getElementById('btnAddNote');
  const btnDeleteNote = document.getElementById('btnDeleteNote');
  const notesPageInfo = document.getElementById('notesPageInfo');
  const notesPageCountInfo = document.getElementById('notesPageCountInfo');
  const notesPrev = document.getElementById('notesPrev');
  const notesNext = document.getElementById('notesNext');
  const notesPageSizeSel = document.getElementById('notesPageSizeSel');

  // Note modal elements
  let noteModal, noteModalTitle, noteSubject, noteComment, noteProductId, noteContractId, noteActive, noteCancel, noteSave;

  const state = {
    id: 0,
    contractStateName: '',
    hasPendingTasks: false,
    items: [],
    itemsPage: 1,
    itemsPageSize: 10,
    itemsTotalPages: 1,
    itemsTotalCount: 0,
    selectedItemId: null,
    notes: [],
    notesPage: 1,
    notesPageSize: 10,
    notesTotalPages: 1,
    notesTotalCount: 0,
    selectedNoteId: null
  };

  function ensureNoteModal() {
    if (noteModal) return;
    noteModal = document.createElement('dialog');
    noteModal.id = 'contractNoteModal';
    noteModal.className = 'modal';
    noteModal.innerHTML = `
      <form method="dialog" class="modal-box">
        <h3 id="contractNoteModalTitle" class="font-bold text-lg mb-2">Add Note</h3>
        <div class="grid grid-cols-1 gap-3">
          <div class="form-control">
            <label class="label"><span class="label-text">Subject</span></label>
            <input id="contractNoteSubject" class="input input-bordered w-full" type="text" />
          </div>
          <div class="form-control">
            <label class="label"><span class="label-text">Comment</span></label>
            <textarea id="contractNoteComment" class="textarea textarea-bordered w-full" rows="4" placeholder="Comment..."></textarea>
          </div>
          <div class="form-control">
            <label class="label"><span class="label-text">Product Id</span></label>
            <input id="contractNoteProductId" class="input input-bordered w-full" type="text" disabled />
          </div>
          <div class="form-control">
            <label class="label"><span class="label-text">Contract Id</span></label>
            <input id="contractNoteContractId" class="input input-bordered w-full" type="text" disabled />
          </div>
          <div class="form-control mt-1">
            <label class="label cursor-pointer gap-3">
              <span class="label-text">Active</span>
              <input id="contractNoteActive" type="checkbox" class="toggle toggle-primary" checked />
            </label>
          </div>
        </div>
        <div class="modal-action">
          <button id="contractNoteCancel" class="btn">Cancel</button>
          <button id="contractNoteSave" class="btn btn-primary">Save</button>
        </div>
      </form>`;
    document.body.appendChild(noteModal);
    noteModalTitle = noteModal.querySelector('#contractNoteModalTitle');
    noteSubject = noteModal.querySelector('#contractNoteSubject');
    noteComment = noteModal.querySelector('#contractNoteComment');
    noteProductId = noteModal.querySelector('#contractNoteProductId');
    noteContractId = noteModal.querySelector('#contractNoteContractId');
    noteActive = noteModal.querySelector('#contractNoteActive');
    noteCancel = noteModal.querySelector('#contractNoteCancel');
    noteSave = noteModal.querySelector('#contractNoteSave');
    noteCancel.addEventListener('click', (e) => { e.preventDefault(); try { noteModal.close(); } catch {} });
    noteSave.addEventListener('click', onSaveNote);
  }

  function setLoading(isLoading) { loadingOverlay?.classList.toggle('hidden', !isLoading); }

  function getContractIdFromPath() {
    const m = (window.location.pathname || '').match(/\/contracts\/(\d+)\/?$/i);
    if (!m) return 0;
    const n = Number(m[1]);
    return Number.isFinite(n) ? n : 0;
  }

  function setText(el, value) { if (el) el.textContent = (value ?? '').toString(); }

  async function loadDetails() {
    const id = state.id;
    setLoading(true);
    try {
      const res = await fetch(`/api/contracts/${encodeURIComponent(id)}`, { method: 'GET', credentials: 'include', headers: { 'Accept': 'application/json' } });
      if (res.status === 401) { window.location.href = '/login?mode=login'; return; }
      if (!res.ok) { detailsEmpty?.classList.remove('hidden'); try { showToast('warning', 'Contract not found.'); } catch {} return; }
      const data = await res.json();
      if (pageTitle) pageTitle.textContent = `Contract ${data.id ?? id}`;
      setText(contractIdEl, data.id ?? id);
      setText(entryDateEl, data.entryDate ?? '');
      setText(customerFullNameEl, data.customerFullName ?? '');
      const stName = (data.contractStateText || data.contractState || '').toString();
      state.contractStateName = stName;
      if (btnContractState) {
        btnContractState.innerHTML = `<span class="material-icons" aria-hidden="true">swap_horiz</span><span>${stName || '—'}</span>`;
        btnContractState.setAttribute('aria-label', `Contract state: ${stName || '—'}`);
        // Defer disable logic to after checkPendingTasks runs
        updateContractStateButton();
      }
      setText(inputUserCodeEl, data.inputUserCode || '');
      setText(lastModifiedByCodeEl, data.lastModifiedByCode || '');
    } catch {
      detailsEmpty?.classList.remove('hidden');
      try { showToast('error', 'Failed to load contract details.'); } catch {}
    } finally { setLoading(false); }
  }

  async function checkPendingTasks() {
    const id = state.id;
    try {
      const res = await fetch(`/api/tasks/contract-pending?contractId=${encodeURIComponent(id)}`, { method: 'GET', credentials: 'include', headers: { 'Accept': 'application/json' } });
      if (res.status === 401) { window.location.href = '/login?mode=login'; return; }
      if (!res.ok) return;
      const data = await res.json();
      const show = !!(data && data.hasPending);
      state.hasPendingTasks = show;
      pendingBanner?.classList.toggle('hidden', !show);
      if (show && pendingBannerText) {
        const count = Number(data.count || 0);
        pendingBannerText.textContent = count > 1 ? 'This contract has tasks in pending state.' : 'This contract has a task in pending state.';
      }
      // Update button states based on pending tasks
      updateContractStateButton();
    } catch {}
  }

  function updateContractStateButton() {
    if (!btnContractState) return;
    const isDraft = (state.contractStateName || '').toLowerCase() === 'draft';
    const hasPending = state.hasPendingTasks;
    btnContractState.disabled = isDraft || hasPending;
    if (hasPending && !isDraft) {
      btnContractState.title = 'State changes are disabled while pending tasks exist';
    } else if (isDraft) {
      btnContractState.title = 'State change is not allowed for Draft contracts';
    } else {
      btnContractState.title = 'Open Contracts State Workflow';
    }
  }

  async function loadItems() {
    const id = state.id;
    try {
      const res = await fetch(`/api/contracts/${encodeURIComponent(id)}/items?page=${state.itemsPage}&pageSize=${state.itemsPageSize}`, { method: 'GET', credentials: 'include', headers: { 'Accept': 'application/json' } });
      if (res.status === 401) { window.location.href = '/login?mode=login'; return; }
      if (!res.ok) throw new Error('HTTP ' + res.status);
      const data = await res.json();
      state.items = Array.isArray(data.items) ? data.items : [];
      state.itemsTotalCount = Number(data.totalCount || state.items.length);
      const apiPages = Number(data.totalPages);
      state.itemsTotalPages = (Number.isFinite(apiPages) && apiPages > 0) ? apiPages : Math.max(1, Math.ceil(state.itemsTotalCount / state.itemsPageSize));
      renderItems();
      updateItemsToolbar();
    } catch { try { showToast('error', 'Failed to load items'); } catch {} }
  }

  function renderItems() {
    if (!itemsTableBody) return;
    itemsTableBody.innerHTML = '';
    const rows = state.items || [];
    if (rows.length === 0) {
      const tr = document.createElement('tr');
      const td = document.createElement('td');
      td.colSpan = 8; td.className = 'empty'; td.textContent = 'No items.';
      tr.appendChild(td); itemsTableBody.appendChild(tr);
    } else {
      rows.forEach(r => {
        const tr = document.createElement('tr');
        tr.addEventListener('click', () => {
          state.selectedItemId = r.id;
          updateItemsToolbar();
          renderItems();
        });
        if (state.selectedItemId && r.id === state.selectedItemId) tr.classList.add('active');
        function fmt2(n){ const v = Number(n); return Number.isFinite(v) ? v.toFixed(2) : (String(n||'')); }
        const cells = [
          String(r.productName || ''),
          String(r.size || ''),
          String(r.color || ''),
          String(r.quantity ?? ''),
          fmt2(r.amount),
          fmt2(r.amtGross),
          String(r.itemStateText || ''),
          (r.isActive ? 'Active' : 'Inactive'),
          String(r.inputDt || '')
        ];
        cells.forEach(text => { const td = document.createElement('td'); td.textContent = text; tr.appendChild(td); });
        itemsTableBody.appendChild(tr);
      });
    }
    // pager
    const pages = Math.max(1, Number(state.itemsTotalPages || 1));
    const page = Math.min(Math.max(1, state.itemsPage), pages);
    if (itemsPageInfo) itemsPageInfo.innerHTML = `Page <b>${page}</b> of <b>${pages}</b>`;
    if (itemsPageCountInfo) itemsPageCountInfo.textContent = `Total records: ${state.itemsTotalCount}`;
    if (itemsPrev) itemsPrev.disabled = (page <= 1 || state.itemsTotalCount === 0);
    if (itemsNext) itemsNext.disabled = (page >= pages || state.itemsTotalCount === 0);
    // total amount for Active items on current page
    try {
      const total = (state.items || []).reduce((acc, r) => acc + ((r && r.isActive) ? (Number(r.amtGross)||0) : 0), 0);
      const el = document.getElementById('itemsTotalAmount');
      if (el) el.value = Number(total).toFixed(2);
    } catch {}
  }

  function updateItemsToolbar() {
    const hasSel = !!state.selectedItemId;
    if (btnOpenItem) btnOpenItem.disabled = !hasSel;
    if (btnChangeState) {
      let disable = !hasSel;
      if (hasSel) {
        const item = (state.items || []).find(x => x.id === state.selectedItemId);
        if (item && item.isActive === false) disable = true;
      }
      // Disable item state changes when contract is in Draft or has pending tasks
      if ((state.contractStateName || '').toLowerCase() === 'draft') disable = true;
      if (state.hasPendingTasks) disable = true;
      btnChangeState.disabled = disable;
      if (state.hasPendingTasks) {
        btnChangeState.title = 'State changes are disabled while pending tasks exist';
      } else {
        btnChangeState.title = '';
      }
    }
  }

  function openItemReadOnly() {
    if (!state.selectedItemId) return;
    const item = (state.items || []).find(x => x.id === state.selectedItemId);
    if (!item) return;
    try {
      // Reuse variant modal in read-only mode
      if (typeof window.openVariantModalReadOnly === 'function') {
        window.openVariantModalReadOnly({
          productName: item.productName,
          size: item.size,
          color: item.color,
          quantity: item.quantity,
          amount: item.amount,
          photoFileName: item.photoFileName
        });
      }
    } catch {}
  }

  async function loadNotes() {
    const id = state.id;
    try {
      const res = await fetch(`/api/contracts/${encodeURIComponent(id)}/notes?q=&page=${state.notesPage}&pageSize=${state.notesPageSize}`, { method: 'GET', credentials: 'include', headers: { 'Accept': 'application/json' } });
      if (res.status === 401) { window.location.href = '/login?mode=login'; return; }
      if (!res.ok) throw new Error('HTTP ' + res.status);
      const data = await res.json();
      state.notes = Array.isArray(data.items) ? data.items : [];
      state.notesTotalCount = Number(data.totalCount || state.notes.length);
      const apiPages = Number(data.totalPages);
      state.notesTotalPages = (Number.isFinite(apiPages) && apiPages > 0) ? apiPages : Math.max(1, Math.ceil(state.notesTotalCount / state.notesPageSize));
      renderNotes();
      updateNotesToolbar();
    } catch { try { showToast('error', 'Failed to load notes'); } catch {} }
  }

  function renderNotes() {
    if (!notesTableBody) return;
    notesTableBody.innerHTML = '';
    const rows = state.notes || [];
    if (rows.length === 0) {
      const tr = document.createElement('tr');
      const td = document.createElement('td');
      td.colSpan = 4; td.className = 'empty'; td.textContent = 'No notes.';
      tr.appendChild(td); notesTableBody.appendChild(tr);
    } else {
      rows.forEach(n => {
        const tr = document.createElement('tr');
        tr.addEventListener('click', () => {
          state.selectedNoteId = n.id;
          updateNotesToolbar();
          renderNotes();
        });
        if (state.selectedNoteId && n.id === state.selectedNoteId) tr.classList.add('active');
        const cells = [
          String(n.subject || ''),
          String(n.inputDt || ''),
          String(n.inputUserCode || '—'),
          (n.isActive ? 'Active' : 'Inactive')
        ];
        cells.forEach(text => { const td = document.createElement('td'); td.textContent = text; tr.appendChild(td); });
        notesTableBody.appendChild(tr);
      });
    }
    const pages = Math.max(1, Number(state.notesTotalPages || 1));
    const page = Math.min(Math.max(1, state.notesPage), pages);
    if (notesPageInfo) notesPageInfo.innerHTML = `Page <b>${page}</b> of <b>${pages}</b>`;
    if (notesPageCountInfo) notesPageCountInfo.textContent = `Total records: ${state.notesTotalCount}`;
    if (notesPrev) notesPrev.disabled = (page <= 1 || state.notesTotalCount === 0);
    if (notesNext) notesNext.disabled = (page >= pages || state.notesTotalCount === 0);
  }

  function updateNotesToolbar() {
    const hasSel = !!state.selectedNoteId;
    if (btnOpenNote) btnOpenNote.disabled = !hasSel;
    if (btnDeleteNote) {
      let disable = !hasSel;
      if (hasSel) {
        const row = (state.notes || []).find(x => x.id === state.selectedNoteId);
        if (row && (String(row.inputUserCode || '').toLowerCase() === 'system')) disable = true;
      }
      // Disable delete when pending tasks
      if (state.hasPendingTasks) disable = true;
      btnDeleteNote.disabled = disable;
      if (state.hasPendingTasks) {
        btnDeleteNote.title = 'Note changes are disabled while pending tasks exist';
      } else {
        btnDeleteNote.title = '';
      }
    }
    // Also handle Add Note button
    if (btnAddNote) {
      const isDraft = (state.contractStateName || '').toLowerCase() === 'draft';
      btnAddNote.disabled = state.hasPendingTasks || isDraft;
      if (state.hasPendingTasks) {
        btnAddNote.title = 'Note changes are disabled while pending tasks exist';
      } else if (isDraft) {
        btnAddNote.title = 'Note changes are disabled for Draft contracts';
      } else {
        btnAddNote.title = '';
      }
    }
  }

  function onAddNote() {
    const isDraft = (state.contractStateName || '').toLowerCase() === 'draft';
    if (isDraft) { try { showToast('warning','Note changes are disabled for Draft contracts.'); } catch {} return; }
    if (state.hasPendingTasks) { try { showToast('warning','Note changes are disabled while pending tasks exist.'); } catch {} return; }
    ensureNoteModal();
    if (noteModalTitle) noteModalTitle.textContent = 'Add Note';
    if (noteSubject) noteSubject.value = '';
    if (noteComment) noteComment.value = '';
    if (noteProductId) noteProductId.value = '';
    if (noteContractId) noteContractId.value = String(state.id || '');
    if (noteActive) noteActive.checked = true;
    if (noteModal) { noteModal.dataset.mode = 'add'; noteModal.dataset.editId = ''; noteModal.dataset.editStamp = ''; }
    if (noteSubject) noteSubject.disabled = false;
    if (noteComment) noteComment.disabled = false;
    if (noteActive) noteActive.disabled = false;
    if (noteSave) { noteSave.disabled = false; noteSave.classList.remove('hidden'); }
    try { noteModal.showModal(); } catch {}
  }

  async function onSaveNote(e) {
    e.preventDefault();
    if (state.hasPendingTasks) { try { showToast('warning','Note changes are disabled while pending tasks exist.'); } catch {} return; }
    const id = state.id;
    const mode = noteModal?.dataset?.mode || 'add';
    try {
      const subj = (noteSubject?.value || '').trim();
      const comm = (noteComment?.value || '').trim();
      const isActive = !!(noteActive?.checked);
      if (!comm) { try { showToast('warning','Comment is required'); } catch {} return; }
      let res;
      if (mode === 'edit') {
        const editId = Number(noteModal?.dataset?.editId || '0');
        if (!editId || Number.isNaN(editId)) { try { showToast('error','Invalid note selected.'); } catch {} return; }
        const body = { Subject: subj, Comment: comm, ContractId: id, ProductId: null, IsActive: isActive };
        res = await fetch(`/api/notes/${encodeURIComponent(editId)}`, { method: 'PUT', credentials: 'include', headers: { 'Content-Type': 'application/json', 'Accept': 'application/json' }, body: JSON.stringify(body) });
        if (res.status === 401) { window.location.href = '/login?mode=login'; return; }
        if (!res.ok) throw new Error('HTTP ' + res.status);
        try { showToast('info', 'Note has been successfully updated.'); } catch {}
      } else {
        const payload = {
          add: [ { comment: comm, subject: subj, isActive } ],
          update: [],
          delete: [],
          setMainId: null,
          setMainStamp: null
        };
        res = await fetch(`/api/contracts/${encodeURIComponent(id)}/notes`, { method: 'PUT', credentials: 'include', headers: { 'Content-Type': 'application/json', 'Accept': 'application/json' }, body: JSON.stringify(payload) });
        if (res.status === 401) { window.location.href = '/login?mode=login'; return; }
        if (!res.ok) throw new Error('HTTP ' + res.status);
        try { showToast('info', 'Note has been successfully added.'); } catch {}
      }
      try { noteModal.close(); } catch {}
      state.notesPage = 1; await loadNotes();
    } catch { try { showToast('error', 'Failed to save note.'); } catch {} }
  }

  async function onDeleteNote() {
    if (state.hasPendingTasks) { try { showToast('warning','Note changes are disabled while pending tasks exist.'); } catch {} return; }
    const id = state.selectedNoteId;
    if (!id) return;
    const row = (state.notes || []).find(x => x.id === id);
    const stamp = row ? row.stamp : null;
    const payload = { add: [], update: [], delete: [ { id, stamp } ], setMainId: null, setMainStamp: null };
    try {
      const res = await fetch(`/api/contracts/${encodeURIComponent(state.id)}/notes`, { method: 'PUT', credentials: 'include', headers: { 'Content-Type': 'application/json', 'Accept': 'application/json' }, body: JSON.stringify(payload) });
      if (res.status === 401) { window.location.href = '/login?mode=login'; return; }
      if (!res.ok) throw new Error('HTTP ' + res.status);
      try { showToast('info', 'The note was deleted successfully.'); } catch {}
      state.selectedNoteId = null;
      await loadNotes();
    } catch { try { showToast('error', 'Failed to delete note.'); } catch {} }
  }


  document.addEventListener('DOMContentLoaded', async () => {
    state.id = getContractIdFromPath();
    if (!state.id) { detailsEmpty?.classList.remove('hidden'); return; }
    await loadDetails();
    await checkPendingTasks();
    await loadItems();
    await loadNotes();

    btnOpenItem?.addEventListener('click', (e) => { e.preventDefault(); openItemReadOnly(); });

    // Contract State Workflow (header button)
    btnContractState?.addEventListener('click', async (e) => {
      e.preventDefault();
      if (state.hasPendingTasks) { try { showToast('warning','State changes are disabled while pending tasks exist.'); } catch {} return; }
      if ((state.contractStateName || '').toLowerCase() === 'draft') { try { showToast('warning','State change is not allowed for Draft contracts.'); } catch {} return; }
      const dlg = document.getElementById('contractStateModal');
      const cur = document.getElementById('ctwCurrentState');
      const nextSel = document.getElementById('ctwNextState');
      const commentEl = document.getElementById('ctwComment');
      const btnOk = document.getElementById('ctwOk');
      const btnCancel = document.getElementById('ctwCancel');
      const btnSet = document.getElementById('ctwSetState');

      // Load modal data (current + next states)
      try {
        const res = await fetch(`/api/contracts/${encodeURIComponent(state.id)}/state/modal-data`, { method: 'GET', credentials: 'include', headers: { 'Accept': 'application/json' } });
        if (res.status === 401) { window.location.href = '/login?mode=login'; return; }
        if (!res.ok) throw new Error('HTTP ' + res.status);
        const data = await res.json();
        if (cur) cur.value = String(data.currentStateName || '');
        if (nextSel) {
          nextSel.innerHTML = '';
          (data.nextStates || []).forEach(ns => {
            const opt = document.createElement('option');
            opt.value = String(ns.id);
            opt.textContent = String(ns.name || '');
            nextSel.appendChild(opt);
          });
          const disabled = (!data.nextStates || data.nextStates.length === 0);
          nextSel.disabled = disabled;
          // Preselect first available next state and enable Set if exists
          if (!disabled && nextSel.options.length > 0) {
            nextSel.selectedIndex = 0;
            if (btnSet) btnSet.disabled = false;
          } else {
            if (btnSet) btnSet.disabled = true;
          }
          nextSel.addEventListener('change', () => {
            const val = nextSel.value;
            if (btnSet) btnSet.disabled = !val;
          });
        }
        if (btnOk) btnOk.disabled = true;
        if (commentEl) commentEl.value = '';
      } catch { try { showToast('error','Failed to load workflow'); } catch {} return; }

      try { dlg.showModal(); } catch {}
      btnCancel?.addEventListener('click', (ev) => { ev.preventDefault(); try { dlg.close(); } catch {} });
      btnOk?.addEventListener('click', (ev) => { ev.preventDefault(); try { dlg.close(); } catch {} });
      dlg?.addEventListener('keydown', (ev) => { if (ev.key === 'Escape') { try { dlg.close(); } catch {} } });

      btnSet?.addEventListener('click', async (ev) => {
        ev.preventDefault();
        try {
          const val = nextSel?.value ? parseInt(nextSel.value,10) : NaN;
          if (!val || Number.isNaN(val)) { try { showToast('warning','Next state is required.'); } catch {} return; }
          if ((state.contractStateName || '').toLowerCase() === 'draft') { try { showToast('warning','State change is not allowed for Draft contracts.'); } catch {} return; }
          const payload = { nextStateId: val, comment: (commentEl?.value || '').trim() };
          const res = await fetch(`/api/contracts/${encodeURIComponent(state.id)}/state`, {
            method: 'POST', credentials: 'include', headers: { 'Content-Type': 'application/json', 'Accept': 'application/json' }, body: JSON.stringify(payload)
          });
          if (res.status === 401) { window.location.href = '/login?mode=login'; return; }
          if (res.status === 400) { const j = await res.json(); try { showToast('error', j.message || 'Selected next state is not allowed.'); } catch {} return; }
          if (!res.ok) throw new Error('HTTP ' + res.status);
          const j = await res.json();
          // Update state button, reload notes
          state.contractStateName = String(j.newStateName || state.contractStateName || '');
          if (btnContractState) btnContractState.innerHTML = `<span class="material-icons" aria-hidden="true">swap_horiz</span><span>${state.contractStateName || '—'}</span>`;
          await loadNotes();
          // Refresh modal data to reflect new current/next states
          try {
            const res2 = await fetch(`/api/contracts/${encodeURIComponent(state.id)}/state/modal-data`, { method: 'GET', credentials: 'include', headers: { 'Accept': 'application/json' } });
            if (res2.ok) {
              const data2 = await res2.json();
              if (cur) cur.value = String(data2.currentStateName || '');
              if (nextSel) {
                nextSel.innerHTML = '';
                (data2.nextStates || []).forEach(ns => {
                  const opt = document.createElement('option');
                  opt.value = String(ns.id);
                  opt.textContent = String(ns.name || '');
                  nextSel.appendChild(opt);
                });
                const disabled2 = (!data2.nextStates || data2.nextStates.length === 0);
                nextSel.disabled = disabled2;
                if (!disabled2 && nextSel.options.length > 0) { nextSel.selectedIndex = 0; if (btnSet) btnSet.disabled = false; }
                else { if (btnSet) btnSet.disabled = true; }
              }
              if (commentEl) commentEl.value = '';
            }
          } catch {}
          // Enable OK
          if (btnOk) btnOk.disabled = false;
          try { showToast('info','Contract state has been successfully updated.'); } catch {}
        } catch { try { showToast('error','Failed to set state'); } catch {} }
      });
    });
    btnChangeState?.addEventListener('click', async (e) => {
      e.preventDefault();
      const item = (state.items || []).find(x => x.id === state.selectedItemId);
      if (!item) return;
      const dlg = document.getElementById('changeStateModal');
      const cur = document.getElementById('cswCurrentState');
      const nextSel = document.getElementById('cswNextState');
      const btnOk = document.getElementById('cswOk');
      const btnCancel = document.getElementById('cswCancel');
      const btnSet = document.getElementById('cswSetState');

      // Fetch modal data (current + next states)
      try {
        const res = await fetch(`/api/contracts/items/${encodeURIComponent(item.id)}/state/modal-data`, { method: 'GET', credentials: 'include', headers: { 'Accept': 'application/json' } });
        if (res.status === 401) { window.location.href = '/login?mode=login'; return; }
        if (!res.ok) throw new Error('HTTP ' + res.status);
        const data = await res.json();
        if (cur) cur.value = String(data.currentStateName || '');
        // Populate next states
        if (nextSel) {
          nextSel.innerHTML = '';
          (data.nextStates || []).forEach(ns => {
            const opt = document.createElement('option');
            opt.value = String(ns.id);
            opt.textContent = String(ns.name || '');
            nextSel.appendChild(opt);
          });
          const disabled = (!!data.isEndState) || (!data.nextStates || data.nextStates.length === 0);
          nextSel.disabled = disabled;
          if (!disabled && nextSel.options.length > 0) { try { nextSel.focus(); } catch {} }
          if (btnSet) btnSet.disabled = disabled;
        }
      } catch {
        try { showToast('error','Failed to load workflow'); } catch {}
        return;
      }

      try { dlg.showModal(); } catch {}
      btnCancel?.addEventListener('click', (ev) => { ev.preventDefault(); try { dlg.close(); } catch {} });
      btnOk?.addEventListener('click', (ev) => { ev.preventDefault(); try { dlg.close(); } catch {} });
      dlg?.addEventListener('keydown', (ev) => { if (ev.key === 'Escape') { try { dlg.close(); } catch {} } });

      btnSet?.addEventListener('click', async (ev) => {
        ev.preventDefault();
        try {
          const val = nextSel?.value ? parseInt(nextSel.value,10) : NaN;
          if (!val || Number.isNaN(val)) { try { showToast('warning','Select next state'); } catch {} return; }
          const res = await fetch(`/api/contracts/items/${encodeURIComponent(item.id)}/state/set`, {
            method: 'POST', credentials: 'include', headers: { 'Content-Type': 'application/json', 'Accept': 'application/json' },
            body: JSON.stringify({ nextStateId: val })
          });
          if (res.status === 401) { window.location.href = '/login?mode=login'; return; }
          if (res.status === 400) { const j = await res.json(); try { showToast('error', j.message || 'Next state not allowed'); } catch {} return; }
          if (!res.ok) throw new Error('HTTP ' + res.status);
          const j = await res.json();
          // Update the selected row
          item.itemStateText = j.newStateName || item.itemStateText;
          // Re-render items to reflect change
          renderItems();
          try { showToast('info','Item state has been successfully updated.'); } catch {}
          try { dlg.close(); } catch {}
        } catch {
          try { showToast('error','Failed to set state'); } catch {}
        }
      });
    });
    itemsPrev?.addEventListener('click', () => { if (state.itemsPage > 1) { state.itemsPage--; loadItems(); } });
    itemsNext?.addEventListener('click', () => { const p = Math.max(1, Number(state.itemsTotalPages||1)); if (state.itemsPage < p) { state.itemsPage++; loadItems(); } });
    itemsPageSizeSel?.addEventListener('change', (e) => { state.itemsPageSize = parseInt(e.target.value, 10) || 10; state.itemsPage = 1; loadItems(); });

    btnAddNote?.addEventListener('click', (e) => { e.preventDefault(); onAddNote(); });
    btnDeleteNote?.addEventListener('click', (e) => { e.preventDefault(); onDeleteNote(); });
    btnOpenNote?.addEventListener('click', async (e) => {
      e.preventDefault();
      if (!state.selectedNoteId) { try { showToast('warning','Select a note first.'); } catch {} return; }
      try {
        const res = await fetch(`/api/notes/${encodeURIComponent(state.selectedNoteId)}`, { method: 'GET', credentials: 'include', headers: { 'Accept': 'application/json' } });
        if (!res.ok) { try { showToast('error','Failed to load note.'); } catch {} return; }
        const data = await res.json();
        ensureNoteModal();
        const isSystem = Number(data.inputUserId) === 2 || String(data.inputUserCode || '').toLowerCase() === 'system';
        if (noteSubject) { noteSubject.value = String(data.subject || ''); noteSubject.disabled = isSystem ? true : false; }
        if (noteComment) { noteComment.value = String(data.comment || ''); noteComment.disabled = isSystem ? true : false; }
        if (noteProductId) { noteProductId.value = ''; noteProductId.disabled = true; }
        if (noteContractId) { noteContractId.value = String(state.id || ''); noteContractId.disabled = true; }
        if (noteActive) { noteActive.checked = !!data.isActive; noteActive.disabled = isSystem ? true : false; }
        if (noteModalTitle) noteModalTitle.textContent = (isSystem ? 'View Note' : 'Edit Note') + (data?.id ? `${data.id}` : '');
        if (noteModal) { noteModal.dataset.mode = isSystem ? 'view' : 'edit'; noteModal.dataset.editId = String(data.id || ''); noteModal.dataset.editStamp = String(data.stamp || ''); }
        if (noteSave) {
          if (isSystem) { noteSave.disabled = true; noteSave.classList.add('hidden'); }
          else { noteSave.disabled = false; noteSave.classList.remove('hidden'); }
        }
        try { noteModal.showModal(); } catch {}
      } catch { try { showToast('error','Failed to load note.'); } catch {} }
    });
    notesPrev?.addEventListener('click', () => { if (state.notesPage > 1) { state.notesPage--; loadNotes(); } });
    notesNext?.addEventListener('click', () => { const p = Math.max(1, Number(state.notesTotalPages||1)); if (state.notesPage < p) { state.notesPage++; loadNotes(); } });
    notesPageSizeSel?.addEventListener('change', (e) => { state.notesPageSize = parseInt(e.target.value, 10) || 10; state.notesPage = 1; loadNotes(); });
  });
})();
