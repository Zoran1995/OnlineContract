(() => {
  const apiBase = "/api/processlog";
  let page = 1;
  let pageSize = 10;
  let totalPages = 0;
  let totalCount = 0;
  let items = [];
  let selectedId = null;
  let sortBy = 'entryDate';
  let sortDir = 'desc';
  let hasSearched = false; // Track if user has clicked Search

  const filterProcess = document.getElementById('filterProcess');
  const filterStatus = document.getElementById('filterStatus');
  const filterDateFrom = document.getElementById('filterDateFrom');
  const filterDateTo = document.getElementById('filterDateTo');
  const btnSearch = document.getElementById('btnSearch');
  const btnClear = document.getElementById('btnClear');
  const btnCancel = document.getElementById('btnCancel');

  const tbody = document.querySelector('#grid tbody');
  const pageInfo = document.getElementById('pageInfo');
  const pageCountInfo = document.getElementById('pageCountInfo');
  const prevPage = document.getElementById('prevPage');
  const nextPage = document.getElementById('nextPage');
  const pageSizeSel = document.getElementById('pageSize');

  function showLoading() { document.getElementById('loadingOverlay')?.classList.remove('hidden'); }
  function hideLoading() { document.getElementById('loadingOverlay')?.classList.add('hidden'); }

  // Get selected row data
  function getSelectedRow() {
    return (items || []).find(x => x.id === selectedId) || null;
  }

  // Update Cancel button state based on selection
  function updateCancelState() {
    const row = getSelectedRow();
    // Can cancel if status is Started (31)
    const canCancel = row && row.status === 31;
    if (btnCancel) btnCancel.disabled = !canCancel;
  }

  // Set default dates to today
  function setDefaultDates() {
    const today = new Date();
    const yyyy = today.getFullYear();
    const mm = String(today.getMonth() + 1).padStart(2, '0');
    const dd = String(today.getDate()).padStart(2, '0');
    const todayStr = `${yyyy}-${mm}-${dd}`;
    filterDateFrom.value = todayStr;
    filterDateTo.value = todayStr;
  }

  async function search() {
    try {
      // Validate date range similar to Contracts/EventLog pages
      const dateFromVal = (filterDateFrom.value || '').trim();
      const dateToVal = (filterDateTo.value || '').trim();
      if (dateFromVal && dateToVal) {
        const from = new Date(dateFromVal);
        const to = new Date(dateToVal);
        if (isNaN(from.getTime()) || isNaN(to.getTime())) { try { showToast('error', 'Invalid date format'); } catch {} return; }
        if (from > to) { try { showToast('warning', 'The start date must be before the end date. Please adjust the date range and try again.'); } catch {} return; }
      }
      showLoading();
      hasSearched = true;
      selectedId = null;
      updateCancelState();
      
      const params = new URLSearchParams();
      const processVal = (filterProcess.value || '').trim();
      const statusVal = (filterStatus.value || '').trim();
      const df = (filterDateFrom.value || '').trim();
      const dt = (filterDateTo.value || '').trim();
      
      if (processVal) params.set('process', processVal);
      if (statusVal) params.set('statusId', statusVal);
      if (df) params.set('dateFrom', df);
      if (dt) params.set('dateTo', dt);
      params.set('page', String(page));
      params.set('pageSize', String(pageSize));
      if (sortBy) { params.set('sortBy', sortBy); params.set('sortDir', sortDir); }
      
      const res = await fetch(`${apiBase}?${params.toString()}`, { 
        credentials: 'include', 
        headers: { 'Accept': 'application/json' }
      });
      if (res.status === 401) { window.location.href = '/login?mode=login'; return; }
      if (!res.ok) throw new Error(`HTTP ${res.status}`);
      const data = await res.json();
      items = Array.isArray(data.items) ? data.items : [];
      totalPages = Number(data.totalPages || 0);
      totalCount = Number(data.totalCount || items.length);
      renderRows();
      renderPager();
    } catch (err) {
      console.error('Search failed:', err);
      try { showToast('error', 'Search failed'); } catch {}
    } finally { hideLoading(); }
  }

  function renderRows() {
    tbody.innerHTML = '';
    
    // Show empty state if no search yet or no results
    if (!hasSearched) {
      const tr = document.createElement('tr');
      const td = document.createElement('td');
      td.colSpan = 6;
      td.className = 'empty';
      td.textContent = 'Use the filters above and click Search to view process log entries.';
      tr.appendChild(td);
      tbody.appendChild(tr);
      return;
    }
    
    if (!items.length) {
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
      tr.style.cursor = 'pointer';
      
      // Row selection click handler
      tr.addEventListener('click', () => {
        Array.from(tbody.querySelectorAll('tr.active')).forEach(r => r.classList.remove('active'));
        tr.classList.add('active');
        selectedId = it.id;
        updateCancelState();
      });
      
      // Mark as active if selected
      if (selectedId && it.id === selectedId) tr.classList.add('active');
      
      const statusTextSafe = (function(){
        try { const s = ensureStatusText(it); return (s && s.trim()) ? s : 'Unknown'; } catch { return 'Unknown'; }
      })();
      tr.innerHTML = `
        <td>${it.id}</td>
        <td>${escapeHtml(it.process || '')}</td>
        <td>${escapeHtml(it.description || '')}</td>
        <td><span class="badge ${getStatusBadgeClass(Number(it.status))}">${escapeHtml(statusTextSafe)}</span></td>
        <td>${escapeHtml(it.initiatedBy || '')}</td>
        <td>${it.entryDate || ''}</td>
      `;
      tbody.appendChild(tr);
    });
  }

  function getStatusBadgeClass(status) {
    switch (status) {
      case 31: return 'badge-info';      // Started
      case 34: return 'badge-ghost';     // Cancelled
      case 35: return 'badge-success';   // Completed
      case 36: return 'badge-error';     // Failed
      case 37: return 'badge-success';   // Successful
      case 38: return 'badge-warning';   // Warning
      case 39: return 'badge-ghost';     // Nothing Processed
      default: return 'badge-ghost';
    }
  }

  // Ensure status text is not empty; fallback to mapped text if server sent empty
  function ensureStatusText(it) {
    const txt = String(it.statusText || '').trim();
    if (txt) return txt;
    switch (Number(it.status)) {
      case 30: return 'Not Started';
      case 31: return 'Started';
      case 32: return 'Approved';
      case 33: return 'Rejected';
      case 34: return 'Cancelled';
      case 35: return 'Completed';
      case 36: return 'Failed';
      case 37: return 'Successful';
      case 38: return 'Warning';
      case 39: return 'Successful Nothing Processed';
      default: return 'Unknown';
    }
  }

  function escapeHtml(str) {
    if (!str) return '';
    return String(str).replace(/[&<>"']/g, m => ({
      '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;'
    }[m]));
  }

  function renderPager() {
    if (!hasSearched) {
      pageInfo.textContent = 'Page 0 of 0';
      pageCountInfo.textContent = 'Showing 0–0 of 0';
      prevPage.disabled = true;
      nextPage.disabled = true;
      return;
    }
    const start = totalCount === 0 ? 0 : (page - 1) * pageSize + 1;
    const end = Math.min(page * pageSize, totalCount);
    pageInfo.textContent = `Page ${page} of ${totalPages || 1}`;
    pageCountInfo.textContent = `Showing ${start}–${end} of ${totalCount}`;
    prevPage.disabled = page <= 1;
    nextPage.disabled = page >= totalPages;
  }

  function updateSortIndicators() {
    document.querySelectorAll('#grid thead th[data-sort]').forEach(th => {
      const indicator = th.querySelector('.sort-indicator');
      if (!indicator) return;
      const col = th.dataset.sort;
      if (col === sortBy) {
        indicator.textContent = sortDir === 'asc' ? ' ▲' : ' ▼';
      } else {
        indicator.textContent = '';
      }
    });
  }

  // Cancel process - similar to processes.js cancel functionality
  async function cancelProcess() {
    const row = getSelectedRow();
    if (!row) { 
      try { showToast('warning', 'Select a row first'); } catch {} 
      return; 
    }
    
    // Only allow cancel if status is Started (31)
    if (row.status !== 31) {
      try { showToast('warning', 'Can only cancel processes that are in Started status'); } catch {}
      return;
    }

    const processName = row.process || 'Process';

    try {
      showLoading();
      const res = await fetch(`${apiBase}/${row.id}/cancel`, {
        method: 'POST',
        credentials: 'include',
        headers: { 'Accept': 'application/json', 'Content-Type': 'application/json' }
      });

      if (res.status === 401) { window.location.href = '/login?mode=login'; return; }

      const data = await res.json();

      if (!res.ok) {
        try { showToast('error', data.message || `Failed to cancel '${processName}'`); } catch {}
        return;
      }

      try { showToast('info', data.message || `'${processName}' has been cancelled.`); } catch {}
      await search(); // Refresh the grid
    } catch (err) {
      try { showToast('error', `Failed to cancel '${processName}'`); } catch {}
    } finally {
      hideLoading();
    }
  }

  // Event listeners
  btnSearch.addEventListener('click', () => { page = 1; search(); });
  
  btnClear.addEventListener('click', () => {
    filterProcess.value = '';
    filterStatus.value = '';
    setDefaultDates();
    page = 1;
    hasSearched = false;
    selectedId = null;
    items = [];
    totalPages = 0;
    totalCount = 0;
    updateCancelState();
    renderRows();
    renderPager();
  });

  btnCancel.addEventListener('click', () => {
    cancelProcess();
  });

  prevPage.addEventListener('click', () => {
    if (page > 1) { page--; search(); }
  });

  nextPage.addEventListener('click', () => {
    if (page < totalPages) { page++; search(); }
  });

  pageSizeSel.addEventListener('change', () => {
    pageSize = parseInt(pageSizeSel.value, 10) || 10;
    page = 1;
    if (hasSearched) search();
  });

  // Column sorting
  document.querySelectorAll('#grid thead th[data-sort]').forEach(th => {
    th.addEventListener('click', () => {
      const col = th.dataset.sort;
      if (sortBy === col) {
        sortDir = sortDir === 'asc' ? 'desc' : 'asc';
      } else {
        sortBy = col;
        sortDir = 'asc';
      }
      updateSortIndicators();
      page = 1;
      if (hasSearched) search();
    });
  });

  // Allow Enter key to trigger search in filter inputs
  [filterProcess, filterDateFrom, filterDateTo].forEach(el => {
    if (el) {
      el.addEventListener('keydown', (e) => {
        if (e.key === 'Enter') { page = 1; search(); }
      });
    }
  });

  filterStatus.addEventListener('change', () => { 
    // Don't auto-search on status change, wait for Search click
  });

  // Initialize - grid is empty until search is clicked
  setDefaultDates();
  updateSortIndicators();
  updateCancelState();
  renderRows();
  renderPager();
})();
