(() => {
  const apiBase = '/api/products';
  const loadingOverlay = document.getElementById('loadingOverlay');
  const detailsEmpty = document.getElementById('detailsEmpty');
  const pageTitle = document.getElementById('pageTitle');
  const pageSubtitle = document.getElementById('pageSubtitle');

  const prodName = document.getElementById('prodName');
  const prodActive = document.getElementById('prodActive');
  const prodNotes = document.getElementById('prodNotes');

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
  const vPrice = document.getElementById('vPrice');
  const vPhotoFile = document.getElementById('vPhotoFile');
  const vPhotoUploadBtn = document.getElementById('vPhotoUploadBtn');
  const vPhotoRemoveBtn = document.getElementById('vPhotoRemoveBtn');
  const vPhotoFileName = document.getElementById('vPhotoFileName');
  const vQty1 = document.getElementById('vQty1');
  const vQty2 = document.getElementById('vQty2');
  const vActive = document.getElementById('vActive');
  const variantAdd = document.getElementById('variantAdd');

  const UPLOAD_PATH = 'C:\\Projects\\Build\\InstallDocs';
  let selectedVariantId = null;
  let modalUploadedPhotoFileName = '';
  let modalEditingVariantId = null;

  const state = {
    id: 0,
    isNew: false,
    product: {
      id: 0,
      name: '',
      description: '',
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
    deletedVariantIds: []
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

  function formatPrice2(n) {
    const v = Number(n);
    if (!Number.isFinite(v)) return '0.00';
    return v.toFixed(2);
  }

  function updateVariantToolbar() {
    const hasSel = selectedVariantId != null;
    if (btnDeleteVariant) btnDeleteVariant.disabled = !hasSel;
    if (btnOpenVariant) btnOpenVariant.disabled = !hasSel;
  }

  function readProductFormIntoState() {
    state.product.name = (prodName?.value || '').trim();
    state.product.isActive = !!prodActive?.checked;
    state.product.description = (prodNotes?.value || '');
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
    if (prodNotes) prodNotes.value = state.product.description || '';

    if (prodId) prodId.textContent = state.isNew ? '—' : String(state.product.id ?? state.id);
    if (prodInputDt) prodInputDt.textContent = state.product.inputDt || (state.isNew ? '—' : '');
    if (prodInputUserId) prodInputUserId.textContent = (state.product.inputUserCode || '—');
    if (prodLastUpdated) prodLastUpdated.textContent = state.product.lastUpdatedDt || '—';
    if (prodLastModifiedBy) prodLastModifiedBy.textContent = (state.product.lastModifiedByCode || '—');
  }

  function renderVariants() {
    if (!variantsTableBody) return;
    variantsTableBody.innerHTML = '';

    if (!state.variants || state.variants.length === 0) {
      const tr = document.createElement('tr');
      const td = document.createElement('td');
      td.colSpan = 7;
      td.className = 'empty';
      td.textContent = 'No variants.';
      tr.appendChild(td);
      variantsTableBody.appendChild(tr);
      return;
    }

    state.variants.forEach(v => {
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
        formatPrice2(v.price),
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
      state.product = { id: 0, name: '', description: '', isActive: true, inputDt: '', inputUserId: null, inputUserCode: '', lastUpdatedDt: '', lastModifiedById: null, lastModifiedByCode: '', _tmpNextId: -1 };
      state.variants = [];
      state.deletedVariantIds = [];
      renderBasic();
      renderVariants();
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
        description: data.description ?? '',
        isActive: !!data.isActive,
        inputDt: data.inputDt ?? '',
        inputUserId: data.inputUserId ?? null,
        inputUserCode: data.inputUserCode ?? '',
        lastUpdatedDt: data.lastUpdatedDt ?? '',
        lastModifiedById: data.lastModifiedById ?? null,
        lastModifiedByCode: data.lastModifiedByCode ?? '',
        _tmpNextId: -1
      };

      state.variants = Array.isArray(data.variants)
        ? data.variants.map(v => ({
            id: safeInt(v.id, 0),
            size: v.size ?? '',
            color: v.color ?? '',
            price: safeNumber(v.price),
            isActive: !!v.isActive,
            photoFileName: v.photoFileName ?? '',
            qtyStore1: clampInt(v.qtyStore1),
            qtyStore2: clampInt(v.qtyStore2)
          }))
        : [];

      state.deletedVariantIds = [];
      renderBasic();
      renderVariants();
    } catch {
      showNotFound();
      try { showToast('error', 'Product details could not be loaded. Please try again.'); } catch {}
    } finally {
      setLoading(false);
    }
  }

  function buildCleanVariantsPayload() {
    return state.variants.map(v => ({
      id: v.id > 0 ? v.id : 0,
      size: v.size ?? '',
      color: v.color ?? '',
      price: safeNumber(v.price),
      isActive: !!v.isActive,
      qtyStore1: clampInt(v.qtyStore1),
      qtyStore2: clampInt(v.qtyStore2),
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
            description: state.product.description,
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

          const res2 = await fetch(`${apiBase}/${encodeURIComponent(newId)}/details`, {
            method: 'PUT',
            credentials: 'include',
            headers: { 'Content-Type': 'application/json', 'Accept': 'application/json' },
            body: JSON.stringify({
              name: state.product.name,
              description: state.product.description,
              isActive: !!state.product.isActive,
              variants: buildCleanVariantsPayload(),
              deletedVariantIds: []
            })
          });

          if (res2.status === 401) { window.location.href = '/login?mode=login'; return; }
          if (res2.status === 403) { window.location.href = '/home'; return; }
        }

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
          description: state.product.description,
          isActive: !!state.product.isActive,
          variants: buildCleanVariantsPayload(),
          deletedVariantIds: buildDeletedIdsPayload()
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
      try { showToast('info', data?.message || 'Product was successfully saved.'); } catch {}
      await loadDetails();
    } catch {
      try { showToast('error', 'Product could not be saved. Please try again.'); } catch {}
    } finally {
      setLoading(false);
    }
  }

  function openVariantModal() {
    if (!variantModal) return;
    modalEditingVariantId = null;
    modalUploadedPhotoFileName = '';
    if (variantModalTitle) variantModalTitle.textContent = 'Add Inventory';
    if (vSize) vSize.value = '';
    if (vColor) vColor.value = '';
    if (vPrice) vPrice.value = '0.00';
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
    if (vPrice) vPrice.value = formatPrice2(v.price);
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
    const price = safeNumber(vPrice?.value || '0');
    const qty1 = clampInt(vQty1?.value);
    const qty2 = clampInt(vQty2?.value);
    const photo = (modalUploadedPhotoFileName || '').trim();
    const active = !!vActive?.checked;

    // Required fields (qty can be 0)
    if (!size) { try { showToast('warning', 'Size is required'); } catch {} return; }
    if (!color) { try { showToast('warning', 'Color is required'); } catch {} return; }
    if (!Number.isFinite(price) || price <= 0) { try { showToast('warning', 'Price must be greater than 0'); } catch {} return; }
    // Photo is optional. If uploaded, server will validate format and store it.

    if (modalEditingVariantId != null) {
      const v = state.variants.find(x => x.id === modalEditingVariantId);
      if (v) {
        v.size = size;
        v.color = color;
        v.price = price;
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
        price,
        isActive: active,
        qtyStore1: qty1,
        qtyStore2: qty2,
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
    btnAddVariant?.addEventListener('click', (e) => { e.preventDefault(); openVariantModal(); });
    variantAdd?.addEventListener('click', (e) => { e.preventDefault(); addVariantFromModal(); });

    btnDeleteVariant?.addEventListener('click', (e) => {
      e.preventDefault();
      deleteSelectedVariant();
    });

    btnOpenVariant?.addEventListener('click', (e) => {
      e.preventDefault();
      openSelectedVariantForEdit();
    });

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
    loadDetails();
  });
})();