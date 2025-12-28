(() => {
  const apiBase = '/api/products';
  const loadingOverlay = document.getElementById('loadingOverlay');
  const detailsEmpty = document.getElementById('detailsEmpty');
  const pageTitle = document.getElementById('pageTitle');
  const pageSubtitle = document.getElementById('pageSubtitle');

  const prodName = document.getElementById('prodName');
  const prodActive = document.getElementById('prodActive');
  // Notes UI will be implemented later; remove legacy description.

  const prodId = document.getElementById('prodId');
  const prodInputDt = document.getElementById('prodInputDt');
  const prodInputUserId = document.getElementById('prodInputUserId');
  const prodLastUpdated = document.getElementById('prodLastUpdated');
  const prodLastModifiedBy = document.getElementById('prodLastModifiedBy');

  const btnSave = document.getElementById('btnSave');
  const btnAddVariant = document.getElementById('btnAddVariant');
  const btnDeleteVariant = document.getElementById('btnDeleteVariant');
  const btnOpenVariant = document.getElementById('btnOpenVariant');

  const variantsTableBody = document.querySelector('#variantsTable tbody');
  const variantModal = document.getElementById('variantModal');
  const variantModalTitle = document.getElementById('variantModalTitle');
  const vSize = document.getElementById('vSize');
  const vColor = document.getElementById('vColor');
  const vAmount = document.getElementById('vAmount');
  const vPrice = null; // legacy alias removed; use vAmount
  const vPhotoFile = document.getElementById('vPhotoFile');
  const vPhotoUploadBtn = document.getElementById('vPhotoUploadBtn');
  const vPhotoRemoveBtn = document.getElementById('vPhotoRemoveBtn');
  const vPhotoFileName = document.getElementById('vPhotoFileName');
  const vQty1 = document.getElementById('vQty1');
  const vQty2 = document.getElementById('vQty2');
  const vActive = document.getElementById('vActive');
  const variantAdd = document.getElementById('variantAdd');

  // Notes UI
  const notesTableBody = document.querySelector('#notesTable tbody');
  const btnAddNote = document.getElementById('btnAddNote');
  const btnDeleteNote = document.getElementById('btnDeleteNote');
  const btnOpenNote = document.getElementById('btnOpenNote');
  const btnSetMainNote = document.getElementById('btnSetMainNote');
  const noteModal = document.getElementById('noteModal');
  const noteModalTitle = document.getElementById('noteModalTitle');
  const noteSubject = document.getElementById('noteSubject');
  const noteComment = document.getElementById('noteComment');
  const noteProductId = document.getElementById('noteProductId');
  const noteContractId = document.getElementById('noteContractId');
  const noteActive = document.getElementById('noteActive');
  const noteSave = document.getElementById('noteSave');

  const UPLOAD_PATH = 'C:\\Projects\\Build\\InstallDocs';
  let selectedVariantId = null;
  let modalUploadedPhotoFileName = '';
  let modalEditingVariantId = null;
  let selectedNoteId = null;
  let modalEditingNoteId = null;

  const state = {
    id: 0,
    isNew: false,
    product: {
      id: 0,
      name: '',
      // description removed; Notes feature will handle content separately,
      isActive: true,
      inputDt: '',
      inputUserId: null,
      inputUserCode: '',
      lastUpdatedDt: '',
      lastModifiedById: null,
      lastModifiedByCode: '',
      _tmpNextId: -1
    },
    variants: [],
    variantsPage: 1,
    variantsPageSize: 10,
    deletedVariantIds: [],
    notes: [],
    notesPage: 1,
    notesPageSize: 10,
    deletedNoteIds: [],
    setMainNoteId: null
  };

  function setLoading(isLoading) {
    loadingOverlay?.classList.toggle('hidden', !isLoading);
  }
  function showNotFound() {
    detailsEmpty?.classList.remove('hidden');
  }
  function hideNotFound() {
    detailsEmpty?.classList.add('hidden');
  }

  function getIdFromPath() {
    const path = (window.location.pathname || '').toLowerCase();
    if (path.endsWith('/products/new')) return 0;
    const m = path.match(/\/products\/(\d+)\/?$/);
    if (!m) return 0;
    const n = Number(m[1]);
    return Number.isFinite(n) ? n : 0;
  }

  function escapeHtml(s) {
    return String(s ?? '')
      .replaceAll('&', '&amp;')
      .replaceAll('<', '&lt;')
      .replaceAll('>', '&gt;')
      .replaceAll('"', '&quot;')
      .replaceAll("'", '&#39;');
  }

  function safeNumber(n) {
    const v = Number(n);
    return Number.isFinite(v) ? v : 0;
  }

  function clampInt(n) {
    const v = Number(n);
    if (!Number.isFinite(v)) return 0;
    return v < 0 ? 0 : Math.trunc(v);
  }

  function safeInt(n, fallback = 0) {
    const v = Number(n);
    return Number.isFinite(v) ? Math.trunc(v) : fallback;
  }

  function formatAmount2(n) {
    const v = Number(n);
    if (!Number.isFinite(v)) return '0.00';
    return v.toFixed(2);
  }

  function computeTotalAmount() {
    // compute over visible variants page
    const vp = state.variants || [];
    const start = (Math.max(1, state.variantsPage) - 1) * state.variantsPageSize;
    const pageItems = vp.slice(start, start + state.variantsPageSize);
    const total = pageItems.reduce((acc, v) => {
      if (!v) return acc;
      if (v.isDeleted) return acc;
      if (!v.isActive) return acc;
      const qty = (Number(v.qtyStore1) || 0) + (Number(v.qtyStore2) || 0);
      const amt = Number(v.amount) || 0;
      return acc + qty * amt;
    }, 0);
    const el = document.getElementById('totalAmount');
    if (el) el.textContent = formatAmount2(total);
  }

  function updateTotalAmountVisibility() {
    try {
      const roleId = (typeof _oc_getRoleId === 'function') ? _oc_getRoleId() : (parseInt(localStorage.getItem('roleId')||'0',10)||0);
      const allowed = [6,7,8]; // Worker=6, Manager=7, Administrator=8
      const wrap = document.getElementById('totalAmount')?.parentElement;
      if (wrap) wrap.style.display = (allowed.includes(roleId) ? '' : 'none');
    } catch {}
  }

  function updateVariantsPaginationUI() {
    const total = (state.variants || []).length;
    const pages = Math.max(1, Math.ceil(total / state.variantsPageSize));
    const page = Math.min(Math.max(1, state.variantsPage), pages);
    state.variantsPage = page;
    const el = document.getElementById('variantsPage'); if (el) el.textContent = String(page);
    const prev = document.getElementById('variantsPrev');
    const next = document.getElementById('variantsNext');
    if (prev) {
      prev.disabled = page <= 1;
      prev.setAttribute('aria-disabled', String(page <= 1));
      prev.classList.toggle('opacity-50', page <= 1);
    }
    if (next) {
      next.disabled = page >= pages;
      next.setAttribute('aria-disabled', String(page >= pages));
      next.classList.toggle('opacity-50', page >= pages);
    }
  }

  function updateNotesPaginationUI() {
    const total = (state.notes || []).length;
    const pages = Math.max(1, Math.ceil(total / state.notesPageSize));
    const page = Math.min(Math.max(1, state.notesPage), pages);
    state.notesPage = page;
    const el = document.getElementById('notesPage'); if (el) el.textContent = String(page);
    const prev = document.getElementById('notesPrev');
    const next = document.getElementById('notesNext');
    if (prev) {
      prev.disabled = page <= 1;
      prev.setAttribute('aria-disabled', String(page <= 1));
      prev.classList.toggle('opacity-50', page <= 1);
    }
    if (next) {
      next.disabled = page >= pages;
      next.setAttribute('aria-disabled', String(page >= pages));
      next.classList.toggle('opacity-50', page >= pages);
    }
  }

  function updateVariantToolbar() {
    const hasSel = selectedVariantId != null;
    if (btnDeleteVariant) btnDeleteVariant.disabled = !hasSel;
    if (btnOpenVariant) btnOpenVariant.disabled = !hasSel;
  }

  // menu items for notes overflow
  const menuSetMainNote = document.getElementById('menuSetMainNote');
  const menuDeleteNote = document.getElementById('menuDeleteNote');

  function updateNotesToolbar() {
    const hasSel = selectedNoteId != null;
    if (btnDeleteNote) btnDeleteNote.disabled = !hasSel;
    if (btnOpenNote) btnOpenNote.disabled = !hasSel;
    if (btnSetMainNote) btnSetMainNote.disabled = !hasSel;
    if (menuDeleteNote) {
      menuDeleteNote.classList.toggle('opacity-50', !hasSel);
      menuDeleteNote.classList.toggle('pointer-events-none', !hasSel);
      if (!hasSel) { menuDeleteNote.setAttribute('aria-disabled', 'true'); menuDeleteNote.setAttribute('tabindex', '-1'); }
      else { menuDeleteNote.removeAttribute('aria-disabled'); menuDeleteNote.removeAttribute('tabindex'); }
    }
    if (menuSetMainNote) {
      menuSetMainNote.classList.toggle('opacity-50', !hasSel);
      menuSetMainNote.classList.toggle('pointer-events-none', !hasSel);
      if (!hasSel) { menuSetMainNote.setAttribute('aria-disabled', 'true'); menuSetMainNote.setAttribute('tabindex', '-1'); }
      else { menuSetMainNote.removeAttribute('aria-disabled'); menuSetMainNote.removeAttribute('tabindex'); }
    }
  }

  function readProductFormIntoState() {
    state.product.name = (prodName?.value || '').trim();
    state.product.isActive = !!prodActive?.checked;
  }

  function renderHeader() {
    if (state.isNew) {
      if (pageTitle) pageTitle.textContent = 'New Product';
      if (pageSubtitle) pageSubtitle.textContent = '';
    } else {
      if (pageTitle) pageTitle.textContent = `Products ${state.id}`;
      if (pageSubtitle) pageSubtitle.textContent = '';
    }
  }

  function renderBasic() {
    if (prodName) prodName.value = state.product.name || '';
    if (prodActive) prodActive.checked = !!state.product.isActive;
    // description removed

    if (prodId) prodId.textContent = state.isNew ? '—' : String(state.product.id ?? state.id);
    if (prodInputDt) prodInputDt.textContent = state.product.inputDt || (state.isNew ? '—' : '');
    if (prodInputUserId) prodInputUserId.textContent = (state.product.inputUserCode || '—');
    if (prodLastUpdated) prodLastUpdated.textContent = state.product.lastUpdatedDt || '—';
    if (prodLastModifiedBy) prodLastModifiedBy.textContent = (state.product.lastModifiedByCode || '—');
  }

  function renderVariants() {
    if (!variantsTableBody) return;
    variantsTableBody.innerHTML = '';

    const all = state.variants || [];
    const start = (Math.max(1, state.variantsPage) - 1) * state.variantsPageSize;
    const pageItems = all.slice(start, start + state.variantsPageSize);

    if (!pageItems || pageItems.length === 0) {
      const tr = document.createElement('tr');
      const td = document.createElement('td');
      td.colSpan = 7;
      td.className = 'empty';
      td.textContent = 'No variants.';
      tr.appendChild(td);
      variantsTableBody.appendChild(tr);
      updateVariantsPaginationUI();
      computeTotalAmount();
      return;
    }

    pageItems.forEach(v => {
      const tr = document.createElement('tr');
      tr.setAttribute('data-vid', String(v.id));

      tr.addEventListener('click', () => {
        selectedVariantId = v.id;
        updateVariantToolbar();
        renderVariants();
      });

      if (selectedVariantId != null && v.id === selectedVariantId) tr.classList.add('active');

      const photo = String(v.photoFileName ?? '').trim();
      const cells = [
        String(v.size ?? ''),
        String(v.color ?? ''),
        formatAmount2(v.amount),
        (photo || '—'),
        String(clampInt(v.qtyStore1)),
        String(clampInt(v.qtyStore2)),
        (v.isActive ? 'Active' : 'Inactive')
      ];

      cells.forEach((text, idx) => {
        const td = document.createElement('td');
        if (idx === 3) td.className = 'text-xs text-gray-500';
        td.textContent = text;
        tr.appendChild(td);
      });

      variantsTableBody.appendChild(tr);
    });

    updateVariantsPaginationUI();
    computeTotalAmount();
  }

  function renderNotes() {
    if (!notesTableBody) return;
    notesTableBody.innerHTML = '';
    const all = state.notes || [];
    const start = (Math.max(1, state.notesPage) - 1) * state.notesPageSize;
    const pageItems = all.slice(start, start + state.notesPageSize);

    if (!pageItems || pageItems.length === 0) {
      const tr = document.createElement('tr');
      const td = document.createElement('td');
      td.colSpan = 5;
      td.className = 'empty';
      td.textContent = 'No notes.';
      tr.appendChild(td);
      notesTableBody.appendChild(tr);
      return;
    }

    pageItems.forEach(n => {
      const tr = document.createElement('tr');
      tr.setAttribute('data-nid', String(n.id));

      tr.addEventListener('click', () => {
        selectedNoteId = n.id;
        updateNotesToolbar();
        renderNotes();
      });

      if (selectedNoteId != null && n.id === selectedNoteId) tr.classList.add('active');

      const cells = [
        String(n.text ?? ''),
        String(n.inputDt ?? ''),
        String(n.inputUserCode ?? '—'),
        (n.isActive ? 'Active' : 'Inactive'),
        (n.isMain ? 'Yes' : 'No')
      ];

      cells.forEach((text, idx) => {
        const td = document.createElement('td');
        if (idx === 0) td.className = 'text-sm';
        td.textContent = text;
        tr.appendChild(td);
      });

      notesTableBody.appendChild(tr);
    });
    updateNotesPaginationUI();
  }

  function deleteSelectedVariant() {
    if (selectedVariantId == null) { try { showToast('warning', 'Please select an inventory row first.'); } catch {} return; }

    const variantId = selectedVariantId;
    const v = state.variants.find(x => x.id === variantId);
    if (!v) return;

    const dlg = document.getElementById('variantDeleteConfirm');
    const okBtn = document.getElementById('variantConfirmOk');
    if (!dlg || !okBtn || !dlg.showModal) {
      if (!confirm('Are you sure you want to remove this inventory row? It will be deleted from the database after you click Save.')) return;
    } else {
      let resolved = false;
      const onOk = (e) => { e.preventDefault(); resolved = true; try { dlg.close(); } catch {} };
      okBtn.addEventListener('click', onOk, { once: true });
      dlg.addEventListener('close', () => {
        if (!resolved) return;
        proceedDelete();
      }, { once: true });
      try { dlg.showModal(); } catch { /* fallback to confirm */ if (!confirm('Are you sure you want to remove this inventory row? It will be deleted from the database after you click Save.')) return; }
      if (!dlg.open && !resolved) return; // user canceled in fallback path
      if (!dlg.open && resolved) return; // handled via proceedDelete in close
      return; // deletion will happen in close handler
    }

    // Fallback confirm path proceeds immediately
    proceedDelete();

    function proceedDelete() {
      if (variantId > 0) {
        if (!state.deletedVariantIds.includes(variantId)) state.deletedVariantIds.push(variantId);
      }
      state.variants = state.variants.filter(x => x.id !== variantId);
      selectedVariantId = null;
      updateVariantToolbar();
      renderVariants();
      try { showToast('info', 'The row was removed from the grid and marked for deletion. Click Save to apply the change.'); } catch {}
    }
  }

  async function uploadPhotoFile(file) {
    const fd = new FormData();
    fd.append('file', file);

    const res = await fetch('/api/products/upload-photo', {
      method: 'POST',
      credentials: 'include',
      body: fd
    });

    if (res.status === 401) { window.location.href = '/login?mode=login'; return null; }
    if (res.status === 403) { window.location.href = '/home'; return null; }
    if (!res.ok) throw new Error(`HTTP ${res.status}`);

    const data = await res.json();
    if (!data?.success) throw new Error(data?.message || 'Upload failed');
    return data;
  }

  async function loadDetails() {
    state.id = getIdFromPath();
    state.isNew = state.id === 0;
    renderHeader();

    if (state.isNew) {
      state.product = { id: 0, name: '', isActive: true, inputDt: '', inputUserId: null, inputUserCode: '', lastUpdatedDt: '', lastModifiedById: null, lastModifiedByCode: '', _tmpNextId: -1 };
      state.variants = [];
      state.deletedVariantIds = [];
      renderBasic();
      renderVariants();
      state.notes = [];
      renderNotes();
      return;
    }

    setLoading(true);
    hideNotFound();

    try {
      const res = await fetch(`${apiBase}/${encodeURIComponent(state.id)}`, {
        method: 'GET',
        credentials: 'include',
        headers: { 'Accept': 'application/json' }
      });

      if (res.status === 401) { window.location.href = '/login?mode=login'; return; }
      if (res.status === 403) { window.location.href = '/home'; return; }
      if (!res.ok) { showNotFound(); return; }

      const data = await res.json();

      state.product = {
        id: data.id,
        name: data.name ?? '',
        // description removed
        isActive: !!data.isActive,
        inputDt: data.inputDt ?? '',
        inputUserId: data.inputUserId ?? null,
        inputUserCode: data.inputUserCode ?? '',
        lastUpdatedDt: data.lastUpdatedDt ?? '',
        lastModifiedById: data.lastModifiedById ?? null,
        lastModifiedByCode: data.lastModifiedByCode ?? '',
        stamp: safeInt(data.stamp, 0),
        _tmpNextId: -1
      };

      state.variants = Array.isArray(data.variants)
        ? data.variants.map(v => ({
            id: safeInt(v.id, 0),
            size: v.size ?? '',
            color: v.color ?? '',
            amount: safeNumber(v.amount),
            isActive: !!v.isActive,
            photoFileName: v.photoFileName ?? '',
            qtyStore1: clampInt(v.qtyStore1),
            qtyStore1Stamp: safeInt(v.qtyStore1Stamp, 0),
            qtyStore2: clampInt(v.qtyStore2),
            qtyStore2Stamp: safeInt(v.qtyStore2Stamp, 0),
            stamp: safeInt(v.stamp, 0)
          }))
        : [];

      state.deletedVariantIds = [];
      renderBasic();
      renderVariants();

      // Load notes
      try {
        const resNotes = await fetch(`${apiBase}/${encodeURIComponent(state.id)}/notes?page=1&pageSize=200`, {
          method: 'GET',
          credentials: 'include',
          headers: { 'Accept': 'application/json' }
        });
        if (resNotes.ok) {
          const dataNotes = await resNotes.json();
          const items = Array.isArray(dataNotes.items) ? dataNotes.items : [];
          state.notes = items.map(n => ({
            id: safeInt(n.id, 0),
            text: n.text ?? '',
            isMain: !!n.isMain,
            isActive: !!n.isActive,
            inputDt: n.inputDt ?? '',
            inputUserCode: n.inputUserCode ?? '',
            stamp: safeInt(n.stamp, 0)
          }));
        } else {
          state.notes = [];
        }
      } catch { state.notes = []; }
      renderNotes();
    } catch {
      showNotFound();
      try { showToast('error', 'Product details could not be loaded. Please try again.'); } catch {}
    } finally {
      setLoading(false);
    }
  }

  // Fetch notes only and merge into current draft state (preserve variants/draft)
  async function loadNotesOnly() {
    if (!state.id || state.id === 0) return;
    try {
      const resNotes = await fetch(`${apiBase}/${encodeURIComponent(state.id)}/notes?page=1&pageSize=200`, {
        method: 'GET', credentials: 'include', headers: { 'Accept': 'application/json' }
      });
      if (!resNotes.ok) return;
      const dataNotes = await resNotes.json();
      const items = Array.isArray(dataNotes.items) ? dataNotes.items : [];
      state.notes = items.map(n => ({
        id: safeInt(n.id, 0),
        text: n.text ?? '',
        isMain: !!n.isMain,
        isActive: !!n.isActive,
        inputDt: n.inputDt ?? '',
        inputUserCode: n.inputUserCode ?? '',
        stamp: safeInt(n.stamp, 0)
      }));
      renderNotes();
    } catch {
      // ignore - keep local notes draft as-is
    }
  }

  function openNoteModal(edit = false) {
    if (!noteModal) return;
    modalEditingNoteId = null;
    if (noteModalTitle) noteModalTitle.textContent = edit ? 'Edit Note' : 'Add Note';
    if (noteSubject) noteSubject.value = '';
    if (noteComment) noteComment.value = '';
    if (noteProductId) noteProductId.value = state.id ? String(state.id) : '';
    if (noteContractId) noteContractId.value = '';
    if (noteActive) noteActive.checked = true;
    if (edit) {
      if (selectedNoteId == null) { try { showToast('warning', 'Select a note first'); } catch {} return; }
      const n = state.notes.find(x => x.id === selectedNoteId);
      if (!n) return;
      modalEditingNoteId = n.id;
      if (noteSubject) noteSubject.value = n.subject || n.text || '';
      if (noteComment) noteComment.value = n.comment || n.text || '';
      if (noteProductId) noteProductId.value = n.productId ? String(n.productId) : (state.id ? String(state.id) : '');
      if (noteContractId) noteContractId.value = n.contractId ? String(n.contractId) : '';
      if (noteActive) noteActive.checked = !!n.isActive;
    }
    noteModal.showModal();
  }

  function addNoteFromModal() {
    const subject = (noteSubject?.value || '').trim();
    const comment = (noteComment?.value || '').trim();
    const active = !!noteActive?.checked;
    if (!subject) { try { showToast('warning', 'Subject is required'); } catch {} return; }

    if (modalEditingNoteId != null) {
      const n = state.notes.find(x => x.id === modalEditingNoteId);
      if (n) {
        n.subject = subject;
        n.comment = comment;
        n.isActive = active;
        selectedNoteId = n.id;
      }
    } else {
      const tmpId = (typeof state.product._tmpNextId === 'number' ? state.product._tmpNextId : -1);
      state.product._tmpNextId = tmpId - 1;
      state.notes.push({ id: tmpId, subject, comment, text: comment, isActive: active, isMain: false, inputDt: '', inputUserCode: '' });
      selectedNoteId = tmpId;
    }
    updateNotesToolbar();
    renderNotes();
    try { noteModal.close(); } catch {}

    // If product already exists, persist this single change immediately via /api/notes
    if (!state.isNew) {
      const payload = { Subject: subject, Comment: comment, ProductId: state.id || null, ContractId: null, IsActive: active };
      (async () => {
        try {
          const res = await fetch('/api/notes', {
            method: 'POST', credentials: 'include', headers: { 'Content-Type': 'application/json', 'Accept': 'application/json' },
            body: JSON.stringify(payload)
          });
          if (res.status === 401) { window.location.href = '/login?mode=login'; return; }
          if (res.status === 403) { window.location.href = '/home'; return; }
          let resJson = null;
          try { resJson = await res.json(); } catch {}
          if (!res.ok || !(resJson && resJson.success)) {
            const msg = (resJson && resJson.message) ? resJson.message : 'Failed to save the note. Please try again later.';
            try { showToast('error', msg); } catch {}
            // Refresh notes only (preserve draft variants)
            await loadNotesOnly();
            return;
          }
          // Merge returned note metadata into local draft (replace tmp id)
          try {
            const createdId = safeInt(resJson.id, 0);
            const createdStamp = safeInt(resJson.stamp, 0);
            const inputDt = resJson.inputDt || '';
            const inputUserCode = resJson.inputUserCode || '';
            // find temporary note (matching negative id created above)
            const tmpNoteIndex = state.notes.findIndex(n => n.id <= 0 && (n.text === comment || n.comment === comment || n.subject === subject));
            if (tmpNoteIndex >= 0) {
              const tmp = state.notes[tmpNoteIndex];
              tmp.id = createdId;
              tmp.stamp = createdStamp;
              tmp.inputDt = inputDt;
              tmp.inputUserCode = inputUserCode;
              tmp.text = comment;
            } else {
              state.notes.push({ id: createdId, subject, comment, text: comment, isActive: active, isMain: false, inputDt: inputDt, inputUserCode: inputUserCode, stamp: createdStamp });
            }
            renderNotes();
          } catch {}
          try { showToast('info', resJson.message || 'Note has been saved successfully.'); } catch {}
          // Refresh only notes (keeps inventory draft intact)
          await loadNotesOnly();
        } catch (err) {
          try { showToast('error', 'Failed to save the note. Please try again later.'); } catch {}
        }
      })();
    }
  }

  function deleteSelectedNote() {
    if (selectedNoteId == null) { try { showToast('warning', 'Please select a note before proceeding.'); } catch {} return; }
    const noteId = selectedNoteId;
    const n = state.notes.find(x => x.id === noteId);
    if (!n) return;
    const dlg = document.getElementById('noteDeleteConfirm');
    const okBtn = document.getElementById('noteConfirmOk');
    if (!dlg || !okBtn || !dlg.showModal) {
      if (!confirm('Remove the selected note from the grid? It will be deleted from the database when you click Save.')) return;
      proceedDelete();
    } else {
      let resolved = false;
      const onOk = (e) => { e.preventDefault(); resolved = true; try { dlg.close(); } catch {} };
      okBtn.addEventListener('click', onOk, { once: true });
      dlg.addEventListener('close', () => {
        if (!resolved) return;
        proceedDelete();
      }, { once: true });
      try { dlg.showModal(); } catch { dlg.classList.remove('hidden'); if (!confirm('Remove the selected note from the grid? It will be deleted from the database when you click Save.')) return; proceedDelete(); }
    }

    function proceedDelete() {
      if (noteId > 0) {
        const existing = state.notes.find(x => x.id === noteId);
        if (!state.deletedNoteIds.some(x => x && x.id === noteId)) state.deletedNoteIds.push({ id: noteId, stamp: existing ? safeInt(existing.stamp, 0) : 0 });
      }

      // If deleting the currently-set main note, clear the setMainNoteId so server doesn't attempt to set it
      if (state.setMainNoteId === noteId) state.setMainNoteId = null;

      state.notes = state.notes.filter(x => x.id !== noteId);
      selectedNoteId = null;
      updateNotesToolbar();
      renderNotes();
      if (state.isNew) {
        try { showToast('info', 'The note was removed from the grid and marked for deletion. Click Save to persist changes.'); } catch {}
        return;
      }

      if (!state.isNew && noteId > 0) {
        (async () => {
          try {
            const n2 = state.notes.find(x => x.id === noteId);
            const payload = { Add: [], Update: [], Delete: [{ Id: noteId, Stamp: n2 ? safeInt(n2.stamp, 0) : 0 }], SetMainId: (state.setMainNoteId || null), SetMainStamp: (state.setMainNoteId ? (state.notes.find(x => x.id === state.setMainNoteId)?.stamp ?? 0) : null) };
            const res = await fetch(`${apiBase}/${encodeURIComponent(state.id)}/notes`, {
              method: 'PUT', credentials: 'include', headers: { 'Content-Type': 'application/json', 'Accept': 'application/json' },
              body: JSON.stringify(payload)
            });
            if (res.status === 401) { window.location.href = '/login?mode=login'; return; }
            if (res.status === 403) { window.location.href = '/home'; return; }
            let resJson = null;
            try { resJson = await res.json(); } catch {}
            if (!res.ok || !(resJson && resJson.success)) {
              const msg = (resJson && resJson.message) ? resJson.message : 'Failed to delete the note. Please try again later.';
              try { showToast('error', msg); } catch {}
              await loadNotesOnly();
              return;
            }
            try { showToast('info', resJson.message || 'Deletion has been saved successfully.'); } catch {}
            await loadNotesOnly();
          } catch {
            try { showToast('error', 'Failed to delete the note. Please try again later.'); } catch {}
          }
        })();
      }
    }
  }

  function setMainSelectedNote() {
    if (selectedNoteId == null) { try { showToast('warning', 'Please select a note before proceeding.'); } catch {} return; }
    const noteId = selectedNoteId;
    const existing = state.notes.find(x => x.id === noteId);
    if (existing && existing.isMain) { try { showToast('warning', 'The note is already set as main. No changes were made.'); } catch {} return; }
    state.setMainNoteId = (noteId > 0 ? noteId : null);
    state.notes = state.notes.map(n => ({ ...n, isMain: n.id === noteId }));
    renderNotes();
    // For new products, inform user to click Save. For existing products, defer showing
    // confirmation until server responds so we don't show conflicting messages.
    if (state.isNew) {
      try { showToast('info', 'Selected note marked as main. Click Save to apply the change.'); } catch {}
    }

    // Persist set-main immediately for existing products
    if (!state.isNew && noteId > 0) {
      (async () => {
        try {
          const target = state.notes.find(x => x.id === noteId);
          const payload = { Add: [], Update: [], Delete: [], SetMainId: noteId, SetMainStamp: target ? safeInt(target.stamp, 0) : 0 };
          const res = await fetch(`${apiBase}/${encodeURIComponent(state.id)}/notes`, {
            method: 'PUT', credentials: 'include', headers: { 'Content-Type': 'application/json', 'Accept': 'application/json' },
            body: JSON.stringify(payload)
          });
          if (res.status === 401) { window.location.href = '/login?mode=login'; return; }
          if (res.status === 403) { window.location.href = '/home'; return; }
          let resJson = null;
          try { resJson = await res.json(); } catch {}
          if (!res.ok || !(resJson && resJson.success)) {
            const msg = (resJson && resJson.message) ? resJson.message : 'Failed to set the main note. Please try again later.';
            try { showToast('error', msg); } catch {}
            return;
          }
          try { showToast('info', resJson.message || 'Main note has been set successfully.'); } catch {}
          await loadNotesOnly();
        } catch {
          try { showToast('error', 'Failed to set the main note. Please try again later.'); } catch {}
        }
      })();
    }
  }

  function buildCleanVariantsPayload() {
    return state.variants.map(v => ({
      id: v.id > 0 ? v.id : 0,
      size: v.size ?? '',
      color: v.color ?? '',
      amount: safeNumber(v.amount),
      isActive: !!v.isActive,
      qtyStore1: clampInt(v.qtyStore1),
      qtyStore1Stamp: safeInt(v.qtyStore1Stamp, 0),
      qtyStore2: clampInt(v.qtyStore2),
      qtyStore2Stamp: safeInt(v.qtyStore2Stamp, 0),
      stamp: safeInt(v.stamp, 0),
      photoFileName: (v.photoFileName ?? '')
    }));
  }

  function buildDeletedIdsPayload() {
    return state.deletedVariantIds
      .map(clampInt)
      .filter(id => id > 0);
  }

  async function save() {
    readProductFormIntoState();
    if (!state.product.name) {
      try { showToast('warning', 'Product name is required.'); } catch {}
      return;
    }

    setLoading(true);
    try {
      if (state.isNew) {
        const res = await fetch(`${apiBase}`, {
          method: 'POST',
          credentials: 'include',
          headers: { 'Content-Type': 'application/json', 'Accept': 'application/json' },
          body: JSON.stringify({
            name: state.product.name,
            // description removed
            isActive: !!state.product.isActive
          })
        });

        if (res.status === 401) { window.location.href = '/login?mode=login'; return; }
        if (res.status === 403) { window.location.href = '/home'; return; }

        const data = await res.json();
        if (!data?.success) {
          try { showToast('error', data?.message || 'Product could not be created. Please try again.'); } catch {}
          return;
        }

        const newId = clampInt(data.id);
        if (!newId) {
          try { showToast('error', 'Product could not be created. Please try again.'); } catch {}
          return;
        }

        if (state.variants.length > 0) {
          state.id = newId;
          state.isNew = false;
          // Persist variants and notes in one atomic call
          const notesPayloadNew = buildNotesPayload();
          const res2 = await fetch(`${apiBase}/${encodeURIComponent(newId)}/details`, {
            method: 'PUT',
            credentials: 'include',
            headers: { 'Content-Type': 'application/json', 'Accept': 'application/json' },
            body: JSON.stringify({
              name: state.product.name,
              // description removed
              isActive: !!state.product.isActive,
              variants: buildCleanVariantsPayload(),
              deletedVariantIds: [],
              Notes: notesPayloadNew
            })
          });

          if (res2.status === 401) { window.location.href = '/login?mode=login'; return; }
          if (res2.status === 403) { window.location.href = '/home'; return; }
        }
        // notes have been sent together with details above when present

        try {
          sessionStorage.setItem('pendingToast', JSON.stringify({ type: 'info', message: 'Product was successfully created.' }));
        } catch {}
        window.location.href = `/products/${newId}`;
        return;
      }

      // Update existing product
        const res = await fetch(`${apiBase}/${encodeURIComponent(state.id)}/details`, {
        method: 'PUT',
        credentials: 'include',
        headers: { 'Content-Type': 'application/json', 'Accept': 'application/json' },
        body: JSON.stringify({
          name: state.product.name,
          // description removed
          isActive: !!state.product.isActive,
            variants: buildCleanVariantsPayload(),
            deletedVariantIds: buildDeletedIdsPayload(),
            stamp: state.product.stamp,
            Notes: buildNotesPayload()
        })
      });

      if (res.status === 401) { window.location.href = '/login?mode=login'; return; }
      if (res.status === 403) { window.location.href = '/home'; return; }
      if (!res.ok) throw new Error(`HTTP ${res.status}`);

      const data = await res.json();
      if (!data?.success) {
        try { showToast('error', data?.message || 'Product could not be saved. Please try again.'); } catch {}
        return;
      }
      // Notes have been included in details payload (atomic)
      try { showToast('info', data?.message || 'Product was successfully saved.'); } catch {}
      await loadDetails();
    } catch {
      try { showToast('error', 'Product could not be saved. Please try again.'); } catch {}
    } finally {
      setLoading(false);
    }
  }

  function buildNotesPayload() {
    const add = [];
    const update = [];
    const del = [];
    (state.notes || []).forEach(n => {
      if (n.id <= 0) {
        add.push({ Comment: n.text ?? '', IsDeleted: !!n.isDeleted, IsActive: !!n.isActive });
      } else {
        update.push({ Id: n.id, Comment: n.text ?? '', IsDeleted: !!n.isDeleted, IsActive: !!n.isActive, Stamp: safeInt(n.stamp, 0) });
      }
    });
    (state.deletedNoteIds || []).forEach(item => {
      if (!item) return;
      if (typeof item === 'number') {
        if (item > 0) del.push({ Id: item, Stamp: 0 });
      } else {
        const iid = safeInt(item.id, 0);
        if (iid > 0) del.push({ Id: iid, Stamp: safeInt(item.stamp, 0) });
      }
    });
    const setMainStamp = state.setMainNoteId ? (state.notes.find(x => x.id === state.setMainNoteId)?.stamp ?? 0) : null;
    return { Add: add, Update: update, Delete: del, SetMainId: (state.setMainNoteId || null), SetMainStamp: setMainStamp };
  }

  function openVariantModal() {
    if (!variantModal) return;
    modalEditingVariantId = null;
    modalUploadedPhotoFileName = '';
    if (variantModalTitle) variantModalTitle.textContent = 'Add Inventory';
    if (vSize) vSize.value = '';
    if (vColor) vColor.value = '';
    if (vAmount) vAmount.value = '0.00';
    if (vPhotoFileName) {
      vPhotoFileName.textContent = '';
      vPhotoFileName.title = '';
    }
    if (vPhotoRemoveBtn) vPhotoRemoveBtn.disabled = true;
    if (vQty1) vQty1.value = '0';
    if (vQty2) vQty2.value = '0';
    if (vActive) vActive.checked = true;
    variantModal.showModal();
  }

  function openSelectedVariantForEdit() {
    if (selectedVariantId == null) {
      try { showToast('warning', 'Select a row first'); } catch {}
      return;
    }
    const v = state.variants.find(x => x.id === selectedVariantId);
    if (!v) return;
    if (!variantModal) return;

    modalEditingVariantId = v.id;
    modalUploadedPhotoFileName = (v.photoFileName || '');
    if (variantModalTitle) variantModalTitle.textContent = 'Edit Inventory';

    if (vSize) vSize.value = (v.size || '');
    if (vColor) vColor.value = (v.color || '');
    if (vAmount) vAmount.value = formatAmount2(v.amount);
    if (vQty1) vQty1.value = String(clampInt(v.qtyStore1));
    if (vQty2) vQty2.value = String(clampInt(v.qtyStore2));
    if (vActive) vActive.checked = !!v.isActive;

    if (vPhotoFileName) {
      vPhotoFileName.textContent = (modalUploadedPhotoFileName || '');
      vPhotoFileName.title = modalUploadedPhotoFileName ? `${UPLOAD_PATH}\\${modalUploadedPhotoFileName}` : '';
    }
    if (vPhotoRemoveBtn) vPhotoRemoveBtn.disabled = !modalUploadedPhotoFileName;

    variantModal.showModal();
  }

  function addVariantFromModal() {
    const size = (vSize?.value || '').trim();
    const color = (vColor?.value || '').trim();
    const amount = safeNumber(vAmount?.value || '0');
    const qty1 = clampInt(vQty1?.value);
    const qty2 = clampInt(vQty2?.value);
    const photo = (modalUploadedPhotoFileName || '').trim();
    const active = !!vActive?.checked;

    // Required fields (qty can be 0)
    if (!size) { try { showToast('warning', 'Size is required'); } catch {} return; }
    if (!color) { try { showToast('warning', 'Color is required'); } catch {} return; }
    if (!Number.isFinite(amount) || amount <= 0) { try { showToast('warning', 'Amount must be greater than 0'); } catch {} return; }
    // Photo is optional. If uploaded, server will validate format and store it.

    if (modalEditingVariantId != null) {
      const v = state.variants.find(x => x.id === modalEditingVariantId);
      if (v) {
        v.size = size;
        v.color = color;
        v.amount = amount;
        v.qtyStore1 = qty1;
        v.qtyStore2 = qty2;
        v.isActive = active;
        v.photoFileName = photo;
        selectedVariantId = v.id;
      }
    } else {
      // Temporary negative id for new variants on client
      if (!Number.isFinite(Number(state.product._tmpNextId))) state.product._tmpNextId = -1;
      const tmpId = safeInt(state.product._tmpNextId, -1);
      state.product._tmpNextId = tmpId - 1;
      state.variants.push({
        id: tmpId,
        size,
        color,
        amount,
        isActive: active,
        qtyStore1: qty1,
        qtyStore2: qty2,
        qtyStore1Stamp: 0,
        qtyStore2Stamp: 0,
        stamp: 0,
        photoFileName: photo
      });
      selectedVariantId = tmpId;
    }

    updateVariantToolbar();

    renderVariants();
    try { variantModal.close(); } catch {}
  }

  document.addEventListener('DOMContentLoaded', () => {
    renderHeader();
    btnSave?.addEventListener('click', (e) => { e.preventDefault(); save(); });
    btnAddVariant?.addEventListener('click', (e) => { e.preventDefault(); selectedVariantId = null; try{ updateVariantToolbar(); }catch{} try{ renderVariants(); }catch{} openVariantModal(); });
    variantAdd?.addEventListener('click', (e) => { e.preventDefault(); addVariantFromModal(); });

    btnDeleteVariant?.addEventListener('click', (e) => {
      e.preventDefault();
      deleteSelectedVariant();
    });

    btnOpenVariant?.addEventListener('click', (e) => {
      e.preventDefault();
      openSelectedVariantForEdit();
    });
    document.getElementById('variantsPrev')?.addEventListener('click', (e) => { e.preventDefault(); state.variantsPage = Math.max(1, state.variantsPage - 1); renderVariants(); });
    document.getElementById('variantsNext')?.addEventListener('click', (e) => { e.preventDefault(); state.variantsPage = state.variantsPage + 1; renderVariants(); });
  btnAddNote?.addEventListener('click', (e) => { e.preventDefault(); selectedNoteId = null; try{ updateNotesToolbar(); }catch{} try{ renderNotes(); }catch{} openNoteModal(false); });
  btnOpenNote?.addEventListener('click', (e) => { e.preventDefault(); openNoteModal(true); });
  btnDeleteNote?.addEventListener('click', (e) => { e.preventDefault(); deleteSelectedNote(); });
  btnSetMainNote?.addEventListener('click', (e) => { e.preventDefault(); setMainSelectedNote(); });
  menuDeleteNote?.addEventListener('click', (e) => { e.preventDefault(); if (selectedNoteId == null) return; deleteSelectedNote(); });
  menuSetMainNote?.addEventListener('click', (e) => { e.preventDefault(); if (selectedNoteId == null) return; setMainSelectedNote(); });
  document.getElementById('notesPrev')?.addEventListener('click', (e) => { e.preventDefault(); state.notesPage = Math.max(1, state.notesPage - 1); renderNotes(); });
  document.getElementById('notesNext')?.addEventListener('click', (e) => { e.preventDefault(); state.notesPage = state.notesPage + 1; renderNotes(); });
  noteSave?.addEventListener('click', (e) => { e.preventDefault(); addNoteFromModal(); });

    vPhotoUploadBtn?.addEventListener('click', async (e) => {
      e.preventDefault();
      vPhotoFile?.click();
    });

    vPhotoRemoveBtn?.addEventListener('click', (e) => {
      e.preventDefault();
      modalUploadedPhotoFileName = '';
      if (vPhotoFile) vPhotoFile.value = '';
      if (vPhotoFileName) {
        vPhotoFileName.textContent = '';
        vPhotoFileName.title = '';
      }
      if (vPhotoRemoveBtn) vPhotoRemoveBtn.disabled = true;
      try { showToast('info', 'Photo was removed from this inventory row. Click Save to apply the change.'); } catch {}
    });

    vPhotoFile?.addEventListener('change', async (e) => {
      try {
        const file = e.target?.files?.[0];
        if (!file) return;

        // Client-side validation for image format
        const allowedExt = ['.png', '.jpg', '.jpeg', '.gif', '.webp'];
        const name = String(file.name || '').toLowerCase();
        const ext = name.includes('.') ? name.slice(name.lastIndexOf('.')) : '';
        if (!String(file.type || '').toLowerCase().startsWith('image/')) {
          try { showToast('warning', 'Invalid file type. Please upload an image.'); } catch {}
          return;
        }
        if (ext && !allowedExt.includes(ext)) {
          try { showToast('warning', 'Invalid image format. Allowed: PNG, JPG/JPEG, GIF, WEBP.'); } catch {}
          return;
        }

        const result = await uploadPhotoFile(file);
        if (!result) return;

        modalUploadedPhotoFileName = result.fileName;
        if (vPhotoFileName) {
          vPhotoFileName.textContent = result.fileName;
          vPhotoFileName.title = `${UPLOAD_PATH}\\${result.fileName}`;
        }
        if (vPhotoRemoveBtn) vPhotoRemoveBtn.disabled = !modalUploadedPhotoFileName;
        try { showToast('info', `Image was uploaded successfully: ${result.fileName}`); } catch {}
      } catch {
        try { showToast('error', 'Image upload failed. Please try again.'); } catch {}
      }
    });

    updateVariantToolbar();
    updateNotesToolbar();
    updateTotalAmountVisibility();
    loadDetails();
  });
})();