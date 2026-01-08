/* global showToast */
(function(){
  let modalEl, okBtn, cancelBtn, imgEl, titleEl, sizeSel, colorSel, qtyInput, storesEl, totalEl;
  let currentProductId = 0;
  let currentVariant = null;
  let inFlight = false;
  let modalMode = 'add'; // 'add' | 'readonly'

  function ensureModal() {
    if (modalEl) return;
    modalEl = document.createElement('dialog');
    modalEl.id = 'variantModal';
    modalEl.className = 'modal';
    modalEl.innerHTML = `
      <form method="dialog" class="modal-box" style="max-width:720px" aria-modal="true" role="dialog" aria-labelledby="vmTitle">
        <h3 id="vmTitle" class="font-bold text-lg mb-2">Select Variant</h3>
        <div class="grid grid-cols-3 gap-4">
          <div class="col-span-1">
            <img id="vmImg" class="rounded w-full h-auto" alt="Product" />
          </div>
          <div class="col-span-2 flex flex-col gap-3">
            <div class="flex gap-3">
              <div class="flex-1">
                <label class="label"><span class="label-text">Available sizes</span></label>
                <select id="vmSize" class="select select-bordered w-full"></select>
              </div>
              <div class="flex-1">
                <label class="label"><span class="label-text">Available colors</span></label>
                <select id="vmColor" class="select select-bordered w-full"></select>
              </div>
            </div>
            <div class="flex items-center gap-3">
              <label class="label"><span class="label-text">Quantity</span></label>
              <input id="vmQty" type="number" class="input input-bordered w-24" min="1" value="1" />
            </div>
            <div id="vmStores" class="text-sm text-gray-700"></div>
            <div id="vmTotal" class="font-semibold text-right">Total: 0.00 RSD</div>
          </div>
        </div>
        <div class="modal-action">
          <button id="vmCancel" class="btn">Cancel</button>
          <button id="vmOk" class="btn btn-primary">OK</button>
        </div>
      </form>`;
    document.body.appendChild(modalEl);
    okBtn = modalEl.querySelector('#vmOk');
    cancelBtn = modalEl.querySelector('#vmCancel');
    imgEl = modalEl.querySelector('#vmImg');
    titleEl = modalEl.querySelector('#vmTitle');
    sizeSel = modalEl.querySelector('#vmSize');
    colorSel = modalEl.querySelector('#vmColor');
    qtyInput = modalEl.querySelector('#vmQty');
    storesEl = modalEl.querySelector('#vmStores');
    totalEl = modalEl.querySelector('#vmTotal');

    // Focus trap within the modal
    function trapFocus(e) {
      if (e.key !== 'Tab') return;
      const focusables = modalEl.querySelectorAll('a[href], button:not([disabled]), textarea, input, select, [tabindex]:not([tabindex="-1"])');
      if (!focusables.length) return;
      const first = focusables[0];
      const last = focusables[focusables.length - 1];
      if (e.shiftKey) {
        if (document.activeElement === first) { e.preventDefault(); last.focus(); }
      } else {
        if (document.activeElement === last) { e.preventDefault(); first.focus(); }
      }
    }

    modalEl.addEventListener('keydown', (e) => {
      // Trap focus and support Enter/Escape
      try { trapFocus(e); } catch {}
      if (e.key === 'Enter') {
        // Submit via OK when appropriate
        const tag = (e.target && e.target.tagName) ? e.target.tagName.toLowerCase() : '';
        if (tag !== 'textarea') {
          e.preventDefault();
          try { okBtn.click(); } catch {}
        }
      }
      if (e.key === 'Escape') {
        try { modalEl.close(); } catch {}
      }
    });

    cancelBtn.addEventListener('click', (e) => { e.preventDefault(); try { modalEl.close(); } catch {} });
    okBtn.addEventListener('click', async (e) => {
      e.preventDefault();
      if (modalMode === 'readonly') { try { modalEl.close(); } catch {} return; }
      if (inFlight) return;
      if (!currentVariant) { try { showToast('warning', 'Please select size and color'); } catch {} return; }
      const qty = parseInt(qtyInput.value, 10) || 1;
      if (qty < 1) { try { showToast('warning', 'Quantity must be at least 1'); } catch {} return; }
      inFlight = true; okBtn.disabled = true;
      try {
        // Try authenticated add first
        const payload = { productVariantId: currentVariant.productVariantId, quantity: qty };
        let res = await fetch('/api/cart/items', {
          method: 'POST', credentials: 'include', headers: { 'Content-Type': 'application/json', 'Accept': 'application/json' },
          body: JSON.stringify(payload)
        });
        if (res.status === 401) {
          // Fallback to anonymous cart (no redirect)
          res = await fetch('/api/anon-cart/items', {
            method: 'POST', credentials: 'include', headers: { 'Content-Type': 'application/json', 'Accept': 'application/json' },
            body: JSON.stringify(payload)
          });
          if (res.status === 400) {
            const j2 = await res.json();
            try { showToast('error', j2.message || 'Validation error'); } catch {}
            return;
          }
          if (!res.ok) { try { showToast('error', 'Failed to add to cart'); } catch {} return; }
          try { showToast('info', 'Added to cart — sign in to checkout.'); } catch {}
          try { modalEl.close(); } catch {}
          return;
        }
        if (res.status === 400) {
          const j = await res.json();
          try { showToast('error', j.message || 'Validation error'); } catch {}
          return;
        }
        if (!res.ok) { try { showToast('error', 'Failed to add to cart'); } catch {} return; }
        const j = await res.json();
        if (j.warning) { try { showToast('warning', j.warning); } catch {} }
        try { showToast('info', 'Added to cart'); } catch {}
        try { modalEl.close(); } catch {}
      } finally {
        inFlight = false; okBtn.disabled = false;
      }
    });

    // Restore focus to triggering element on close
    modalEl.addEventListener('close', () => {
      try {
        const el = window._oc_modalReturnFocusEl;
        if (el && typeof el.focus === 'function') { el.focus(); }
        window._oc_modalReturnFocusEl = null;
      } catch {}
    });
  }

  async function fetchSizesColors(productId) {
    const res = await fetch(`/api/products/${productId}/variants`, { method: 'GET', credentials: 'include', headers: { 'Accept': 'application/json' } });
    if (!res.ok) throw new Error('Failed to load variants');
    return await res.json();
  }

  async function resolveVariant(productId, size, color) {
    const p = new URLSearchParams({ productId: String(productId), size: size || '', color: color || '' });
    const res = await fetch(`/api/variants/by-selection?${p}`, { method: 'GET', credentials: 'include', headers: { 'Accept': 'application/json' } });
    if (!res.ok) return null;
    return await res.json();
  }

  async function fetchAvailability(variantId) {
    const res = await fetch(`/api/variants/${variantId}/availability`, { method: 'GET', credentials: 'include', headers: { 'Accept': 'application/json' } });
    if (!res.ok) return [];
    return await res.json();
  }

  async function onSelectionChange() {
    const size = sizeSel.value; const color = colorSel.value;
    if (!size || !color) { currentVariant = null; updateTotal(0, 1); storesEl.textContent = ''; return; }
    const v = await resolveVariant(currentProductId, size, color);
    currentVariant = v;
    const qty = parseInt(qtyInput.value, 10) || 1;
    if (v) {
      updateImage(v.photoFileName);
      updateTotal(v.amount, qty);
      const avail = await fetchAvailability(v.productVariantId);
      if (!avail || avail.length === 0) {
        storesEl.textContent = 'Currently unavailable. We will contact you ASAP.';
      } else {
        const names = avail.map(a => a.storeName).join(', ');
        storesEl.textContent = `This item is available at store(s): ${names}`;
      }
    } else {
      updateTotal(0, qty);
      storesEl.textContent = '';
    }
  }

  function rsd(n) {
    return (Number(n || 0).toFixed(2)) + ' RSD';
  }
  function updateTotal(amount, qty) {
    totalEl.textContent = `Total: ${rsd((amount || 0) * (qty || 1))}`;
  }
  function updateImage(photoFileName) {
    const src = photoFileName ? `/product-images/${encodeURIComponent(photoFileName)}` : '/resources/photos/placeholder.png';
    imgEl.src = src;
  }

  async function openVariantModal(productId, productName, photoFileName) {
    ensureModal();
    modalMode = 'add';
    currentProductId = productId; currentVariant = null;
    titleEl.textContent = productName || 'Select Variant';
    updateImage(photoFileName || null);
    qtyInput.value = '1'; storesEl.textContent = ''; updateTotal(0, 1);

    // Load sizes/colors
    try {
      const v = await fetchSizesColors(productId);
      sizeSel.innerHTML = ''; colorSel.innerHTML = '';
      (v.sizes || []).forEach(s => { const o = document.createElement('option'); o.value = s; o.textContent = s; sizeSel.appendChild(o); });
      (v.colors || []).forEach(c => { const o = document.createElement('option'); o.value = c; o.textContent = c; colorSel.appendChild(o); });
    } catch { try { showToast('error', 'Failed to load variants'); } catch {} }

    sizeSel.onchange = onSelectionChange;
    colorSel.onchange = onSelectionChange;
    qtyInput.oninput = () => { const qty = parseInt(qtyInput.value, 10) || 1; updateTotal(currentVariant ? currentVariant.amount : 0, qty); };

    try { modalEl.showModal(); } catch { /* ignore */ }
    // Move initial focus to size selector for accessibility
    try { sizeSel.focus(); } catch {}
  }

  // Expose API
  window.openVariantModal = openVariantModal;
  window.openVariantModalReadOnly = function(data){
    ensureModal();
    modalMode = 'readonly';
    currentProductId = 0; currentVariant = null;
    titleEl.textContent = data && data.productName ? String(data.productName) : 'Item Details';
    updateImage(data && data.photoFileName ? data.photoFileName : null);
    // Reset selects to a single option reflecting provided values and disable controls
    sizeSel.innerHTML = '';
    colorSel.innerHTML = '';
    const so = document.createElement('option'); so.value = data?.size || ''; so.textContent = data?.size || ''; sizeSel.appendChild(so);
    const co = document.createElement('option'); co.value = data?.color || ''; co.textContent = data?.color || ''; colorSel.appendChild(co);
    sizeSel.disabled = true; colorSel.disabled = true; qtyInput.disabled = true;
    const qty = parseInt(data?.quantity, 10) || 1;
    qtyInput.value = String(qty);
    updateTotal(Number(data?.amount) || 0, qty);
    storesEl.textContent = '';
    try { modalEl.showModal(); } catch {}
    try { okBtn.textContent = 'OK'; } catch {}
  };
})();

// Global delegation for Add-to-cart across pages
document.addEventListener('click', (ev) => {
  const btn = ev.target.closest('.btn-add-to-cart');
  if (!btn) return;
  ev.preventDefault();
  ev.stopPropagation();
  try { window._oc_modalReturnFocusEl = btn; } catch {}
  const productIdRaw = btn.dataset.productId;
  const productId = productIdRaw ? parseInt(productIdRaw, 10) : NaN;
  if (!productId || Number.isNaN(productId)) {
    if (typeof window.showError === 'function') {
      try { window.showError('Please select a product card that supports Add to cart.'); } catch {}
    } else {
      try { console.warn('Add to cart clicked without data-product-id.'); } catch {}
    }
    return;
  }
  try { console.info('Modal opened', { productId }); } catch {}
  try { window.openVariantModal?.(productId); } catch {}
});
