/* global showToast */

(function () {
  const loadingOverlay = document.getElementById('loadingOverlay');
  const detailsEmpty = document.getElementById('detailsEmpty');

  const contractIdEl = document.getElementById('contractId');
  const entryDateEl = document.getElementById('entryDate');
  const customerFullNameEl = document.getElementById('customerFullName');
  const contractStateEl = document.getElementById('contractState');
  const inputUserIdEl = document.getElementById('inputUserId');
  const lastModifiedByIdEl = document.getElementById('lastModifiedById');
  const lastUpdatedDtEl = document.getElementById('lastUpdatedDt');
  const stampEl = document.getElementById('stamp');

  function setLoading(isLoading) {
    if (!loadingOverlay) return;
    loadingOverlay.classList.toggle('hidden', !isLoading);
  }

  function getContractIdFromPath() {
    const m = (window.location.pathname || '').match(/\/contracts\/(\d+)\/?$/i);
    if (!m) return 0;
    const n = Number(m[1]);
    return Number.isFinite(n) ? n : 0;
  }

  function setText(el, value) {
    if (!el) return;
    el.textContent = (value ?? '').toString();
  }

  async function loadDetails() {
    const id = getContractIdFromPath();
    if (!id) {
      if (detailsEmpty) detailsEmpty.classList.remove('hidden');
      setText(contractIdEl, '');
      return;
    }

    setLoading(true);
    if (detailsEmpty) detailsEmpty.classList.add('hidden');

    try {
      const res = await fetch(`/api/contracts/${encodeURIComponent(id)}`, {
        method: 'GET',
        credentials: 'include',
        headers: { 'Accept': 'application/json' }
      });

      if (res.status === 401) {
        window.location.href = '/login?mode=login';
        return;
      }

      if (!res.ok) {
        if (detailsEmpty) detailsEmpty.classList.remove('hidden');
        try { showToast('warning', 'Contract not found.'); } catch {}
        return;
      }

      const data = await res.json();

      setText(contractIdEl, data.id ?? id);
      setText(entryDateEl, data.entryDate ?? '');
      setText(customerFullNameEl, data.customerFullName ?? '');
      setText(contractStateEl, data.contractState ?? '');
      setText(inputUserIdEl, data.inputUserId ?? '');
      setText(lastModifiedByIdEl, data.lastModifiedById ?? '');
      setText(lastUpdatedDtEl, data.lastUpdatedDt ?? '');
      setText(stampEl, data.stamp ?? '');
    } catch (e) {
      if (detailsEmpty) detailsEmpty.classList.remove('hidden');
      try { showToast('error', 'Failed to load contract details.'); } catch {}
    } finally {
      setLoading(false);
    }
  }

  document.addEventListener('DOMContentLoaded', () => {
    loadDetails();
  });
})();
