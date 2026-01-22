
(() => {
  const apiBase = "/api/contracts";

  let page = 1;
  let pageSize = 10;
  let totalPages = 1;
  let totalCount = 0;
  let selectedId = null;
  let items = [];
  let sortBy = '', sortDir = '';

  const ddlState = document.getElementById("ddlState");
  const txtName = document.getElementById("txtName");
  const dtFrom = document.getElementById("dtFrom");
  const dtTo = document.getElementById("dtTo");

  const btnOpen = document.getElementById("btnOpen");
  const btnRefund = document.getElementById("btnRefund");
  const btnSearch = document.getElementById("btnSearch");
  const btnClear = document.getElementById("btnClear");
  const exportBtn = document.getElementById("exportBtn");

  const tbody = document.querySelector("#grid tbody");
  const pageInfo = document.getElementById("pageInfo");
  const pageCountInfo = document.getElementById("pageCountInfo");
  const prevPage = document.getElementById("prevPage");
  const nextPage = document.getElementById("nextPage");
  const pageSizeSel = document.getElementById("pageSize");
  const filtersSection = document.getElementById("contractsFilters");

  let authRoleId = 0;
  let isCustomerView = false;

  async function fetchWhoAmI() {
    try {
      const res = await fetch('/whoami', {
        method: 'GET',
        credentials: 'include',
        headers: { 'Accept': 'application/json' }
      });
      if (res.status === 401) return { isAuthenticated: false, roleId: 0 };
      return await res.json();
    } catch {
      return { isAuthenticated: false, roleId: 0 };
    }
  }

  function showLoading() {
    document.getElementById("loadingOverlay")?.classList.remove("hidden");
  }
  function hideLoading() {
    document.getElementById("loadingOverlay")?.classList.add("hidden");
  }
  function setEmptyState(visible) {
    document.getElementById("emptyState")?.classList.toggle("hidden", !visible);
  }
  function updateOpenState() {
    if (btnOpen) btnOpen.disabled = !selectedId;
  }
  function updateRefundState() {
    if (!btnRefund) return;
    if (!selectedId) { btnRefund.disabled = true; return; }
    const row = (items || []).find(x => x.id === selectedId);
    if (!row) { btnRefund.disabled = true; return; }
    const amt = Number(row.amount || 0);
    const matched = Number(row.amtMatched || 0);
    const amountsMatch = Math.abs(amt - matched) < 0.000001;
    const stateText = String(row.contractStateText || row.contractState || '').trim().toLowerCase();
    const allowedStates = new Set(['cancelled','returned','rejected','written off']);
    const isAllowedState = allowedStates.has(stateText);
    btnRefund.disabled = !(amountsMatch && isAllowedState);
  }
  function updateExportState() {
    // Keep the navbar export button always enabled; show a warning on click when grid is empty
    if (exportBtn) exportBtn.disabled = false;
  }

  function buildQuery() {
    const qs = new URLSearchParams();
    qs.set("page", String(page));
    qs.set("pageSize", String(pageSize));
    const st = (ddlState?.value || "").trim();
    const nm = (txtName?.value || "").trim();
    const from = (dtFrom?.value || '').trim();
    const to = (dtTo?.value || '').trim();
    if (st) qs.set("state", st);
    if (nm) qs.set("name", nm);
    if (from) qs.set('fromDate', from);
    if (to) qs.set('toDate', to);
    if (sortBy) qs.set('sortBy', sortBy);
    if (sortDir) qs.set('sortDir', sortDir);
    return `?${qs.toString()}`;
  }

  function buildExportQuery() {
    const qs = new URLSearchParams();
    const st = (ddlState?.value || "").trim();
    const nm = (txtName?.value || "").trim();
    const from = (dtFrom?.value || '').trim();
    const to = (dtTo?.value || '').trim();
    if (st) qs.set("state", st);
    if (nm) qs.set("name", nm);
    if (from) qs.set('fromDate', from);
    if (to) qs.set('toDate', to);
    const s = qs.toString();
    return s ? `?${s}` : "";
  }

  async function load() {
    try {
      // Validate date range like EventLog page: parse dates and show friendly warnings
      if (dtFrom && dtTo && dtFrom.value && dtTo.value) {
        const from = new Date(dtFrom.value);
        const to = new Date(dtTo.value);
        if (isNaN(from.getTime()) || isNaN(to.getTime())) {
          try { showToast('error', 'Invalid date format'); } catch {}
          return;
        }
        if (from > to) {
          try { showToast('warning', 'The start date must be before the end date. Please adjust the date range and try again.'); } catch {}
          return;
        }
      }

      showLoading();
      setEmptyState(false);
      selectedId = null;
      updateOpenState();

      const res = await fetch(`${apiBase}${buildQuery()}`, {
        method: "GET",
        credentials: "include",
        headers: { "Accept": "application/json" }
      });
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
      setEmptyState(items.length === 0);
      updateExportState();
      updateRefundState();
    } catch (err) {
      try { showToast('error', 'Failed to load contracts'); } catch {}
      setEmptyState(true);
      updateExportState();
    } finally {
      hideLoading();
    }
  }

  function renderRows() {
    if (!tbody) return;
    tbody.innerHTML = "";
    if (items.length === 0) {
      const tr = document.createElement("tr");
      const td = document.createElement("td");
      td.colSpan = 5;
      td.className = "empty";
      td.textContent = "No results.";
      tr.appendChild(td);
      tbody.appendChild(tr);
      return;
    }

    items.forEach(it => {
      const tr = document.createElement("tr");
      tr.addEventListener("click", () => {
        Array.from(tbody.querySelectorAll("tr.active")).forEach(r => r.classList.remove("active"));
        tr.classList.add("active");
        selectedId = it.id;
        updateOpenState();
        updateRefundState();
      });

      if (selectedId && it.id === selectedId) tr.classList.add('active');

      function fmt2(n){ const v = Number(n); return Number.isFinite(v) ? v.toFixed(2) : (String(n||'')); }
      const isFullyMatched = Number(it.amtMatched ?? 0) === Number(it.amount ?? 0);
      const bracketGlyph = isFullyMatched ? '[ ]' : '[ ';
      const cells = [
        String(it.id),
        // Allocation Status cell: styled bracket glyph
        (() => {
          const span = document.createElement('span');
          span.className = 'alloc-bracket text-xl font-bold leading-none inline-flex items-center justify-center';
          span.textContent = bracketGlyph;
          span.setAttribute('aria-label', isFullyMatched ? 'Fully matched allocation' : 'Cash on delivery or partial allocation');
          return span;
        })(),
        String(it.customerFullName ?? ''),
        fmt2(it.amount),
        String(it.contractStateText || it.contractState || ''),
        String(it.entryDate ?? ''),
        String(it.deliveredDate ?? ''),
        String(it.writtenOffDate ?? ''),
        String(it.rejectedDate ?? ''),
        String(it.cancelledDate ?? '')
      ];
      cells.forEach((html, idx) => {
        const td = document.createElement("td");
        if (idx === 1 && html instanceof HTMLElement) {
          td.appendChild(html);
        } else {
          td.textContent = html;
        }
        if (idx === 1) { // Allocation Status accessibility label
          td.classList.add('text-center');
        }
        tr.appendChild(td);
      });
      tbody.appendChild(tr);
    });
    try {
      const keys = ['id','', 'customerFullName','amount','contractState','entryDate','','','',''];
      document.querySelectorAll('#grid thead th').forEach((th, idx) => {
        th.style.cursor = 'pointer';
        const ex = th.querySelector('.sort-indicator'); if (ex) ex.remove();
        const key = keys[idx] || '';
        const span = document.createElement('span'); span.className = 'sort-indicator ml-2';
        if (key && key === sortBy) { span.textContent = sortDir === 'desc' ? ' ▼' : ' ▲'; }
        th.appendChild(span);
        th.onclick = () => { if (!key) return; if (sortBy === key) sortDir = (sortDir === 'desc' ? 'asc' : 'desc'); else { sortBy = key; sortDir = 'asc'; } page = 1; load(); };
      });
    } catch {}
  }

  // badge rendering removed; render plain lookup-based text from API

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

  document.addEventListener("DOMContentLoaded", () => {
    // Helper: produce yyyy-mm-dd
    function todayString() {
      const d = new Date();
      const mm = String(d.getMonth() + 1).padStart(2, '0');
      const dd = String(d.getDate()).padStart(2, '0');
      return `${d.getFullYear()}-${mm}-${dd}`;
    }

    // Set default dates to today on initial load
    if (dtFrom && !dtFrom.value) dtFrom.value = todayString();
    if (dtTo && !dtTo.value) dtTo.value = todayString();

    // Determine role to decide whether to hide filters (Customer view)
    fetchWhoAmI().then(auth => {
      authRoleId = parseInt(String(auth?.roleId ?? 0), 10) || 0;
      isCustomerView = authRoleId === 5;
      if (isCustomerView) {
        // Customers see only their orders; filters and Search/Clear are hidden.
        if (filtersSection) filtersSection.classList.add('hidden');
      }
    }).finally(() => {
      // Initial load after role is known (API also enforces customer ownership)
      load();
    });

    btnSearch?.addEventListener("click", (e) => { e.preventDefault(); page = 1; load(); });
    btnClear?.addEventListener("click", (e) => {
      e.preventDefault();
      ddlState && (ddlState.value = "");
      txtName && (txtName.value = "");
      if (dtFrom) dtFrom.value = todayString();
      if (dtTo) dtTo.value = todayString();
      page = 1;
      load();
    });

    [ddlState, txtName].forEach(el => {
      if (el && !el._boundEnter) {
        el._boundEnter = true;
        el.addEventListener("keydown", (e) => { if (e.key === "Enter") { e.preventDefault(); page = 1; load(); } });
      }
    });

    prevPage?.addEventListener("click", () => { if (page > 1) { page--; load(); } });
    nextPage?.addEventListener("click", () => {
      const pages = Math.max(1, Number(totalPages || 1));
      if (page < pages) { page++; load(); }
    });

    pageSizeSel?.addEventListener("change", (e) => {
      pageSize = parseInt(e.target.value, 10) || 10;
      page = 1;
      load();
    });

    btnOpen?.addEventListener("click", () => {
      if (!selectedId) { try { showToast('warning', 'Select a contract first'); } catch {} return; }
      window.open(`/contracts/${selectedId}`, '_blank');
    });
    btnRefund?.addEventListener("click", () => {
      if (!selectedId) { try { showToast('warning', 'Select a contract first'); } catch {} return; }
      onRefund();
    });

    // Refund modal logic
    function ensureRefundModal() {
      let dlg = document.getElementById('refundModal');
      if (dlg) return dlg;
      dlg = document.createElement('dialog');
      dlg.id = 'refundModal';
      dlg.className = 'modal';
      dlg.innerHTML = `
        <form method="dialog" class="modal-box w-96">
          <h3 class="font-bold text-lg mb-4">Refund Payment</h3>
          <div class="form-control mb-3">
            <label class="label"><span class="label-text">Amount</span></label>
            <input id="refundAmount" type="number" step="0.01" min="0" class="input input-bordered" placeholder="e.g. 100.00" />
          </div>
          <div class="form-control mb-3">
            <label class="label"><span class="label-text">Percentage</span></label>
            <input id="refundPercent" type="number" step="0.01" min="0" max="100" class="input input-bordered" placeholder="e.g. 25.00" />
          </div>
          <div id="refundTotal" class="mb-4 text-sm">Total for refund: 0.00</div>
          <div class="modal-action">
            <button type="button" id="refundCancel" class="btn btn-outline">Cancel</button>
            <button type="button" id="refundOk" class="btn">OK</button>
          </div>
        </form>`;
      document.body.appendChild(dlg);

      dlg.querySelector('#refundCancel')?.addEventListener('click', () => { try { dlg.close(); } catch {} });
      dlg.addEventListener('cancel', () => { try { dlg.close(); } catch {} });

      const amtEl = dlg.querySelector('#refundAmount');
      const pctEl = dlg.querySelector('#refundPercent');
      const totalEl = dlg.querySelector('#refundTotal');

      function format2(n) { const v = Number(n || 0); return Number.isFinite(v) ? v.toFixed(2) : '0.00'; }
      function clampAmount(val, max) {
        let v = Number(val || 0);
        if (v < 0) v = 0;
        if (v > max) v = max;
        return v;
      }
      function clampPercent(val) {
        let v = Number(val || 0);
        if (v < 0) v = 0;
        if (v > 100) v = 100;
        return v;
      }
      function updateTotal(amount) { if (totalEl) totalEl.textContent = `Total for refund: ${format2(amount)}`; }

      amtEl?.addEventListener('blur', () => {
        const row = (items || []).find(x => x.id === selectedId);
        const max = Number(row?.amtMatched || 0);
        let amount = clampAmount(amtEl.value, max);
        amtEl.value = format2(amount);
        const pct = max > 0 ? (amount / max) * 100 : 0;
        pctEl.value = format2(clampPercent(pct));
        const errors = validateRefund();
        if (!errors.length) updateTotal(amount); else updateTotal(Number(amtEl.value || 0));
      });

      pctEl?.addEventListener('blur', () => {
        const row = (items || []).find(x => x.id === selectedId);
        const max = Number(row?.amtMatched || 0);
        let pct = clampPercent(pctEl.value);
        pctEl.value = format2(pct);
        const amount = max * (pct / 100);
        amtEl.value = format2(clampAmount(amount, max));
        const errors = validateRefund();
        updateTotal(Number(amtEl.value || 0));
      });

      dlg.querySelector('#refundOk')?.addEventListener('click', () => {
        const errors = validateRefund();
        if (errors.length) { try { showToast('error', errors.join('\n')); } catch {} return; }
        try { showToast('info', 'Refund submission is not implemented yet.'); } catch {}
      });

      return dlg;
    }

    function validateRefund() {
      const dlg = document.getElementById('refundModal');
      const amtEl = dlg?.querySelector('#refundAmount');
      const pctEl = dlg?.querySelector('#refundPercent');
      const row = (items || []).find(x => x.id === selectedId);
      const max = Number(row?.amtMatched || 0);
      const amount = Number(amtEl?.value || 0);
      const pct = Number(pctEl?.value || 0);
      const errs = [];
      if (!(amount > 0)) errs.push('Amount must be greater than 0.');
      if (amount > max) errs.push('Amount cannot exceed matched amount.');
      if (pct < 0 || pct > 100) errs.push('Percentage must be between 0 and 100.');
      if ((amtEl?.value === '' || amtEl?.value == null) && (pctEl?.value === '' || pctEl?.value == null)) {
        errs.push('Enter amount or percentage.');
      }
      return errs;
    }

    function onRefund() {
      const dlg = ensureRefundModal();
      dlg.querySelector('#refundAmount').value = '';
      dlg.querySelector('#refundPercent').value = '';
      dlg.querySelector('#refundTotal').textContent = 'Total for refund: 0.00';
      try { dlg.showModal(); } catch {}
    }

    exportBtn?.addEventListener("click", async () => {
      try {
        if (!items || items.length === 0) {
          try { showToast('warning', 'There is no data to export. Please run a search or adjust filters before exporting.'); } catch {}
          return;
        }

        const res = await fetch(`${apiBase}/export${buildExportQuery()}`, {
          method: "GET",
          credentials: "include",
          headers: { "Accept": "text/csv" }
        });
        if (res.status === 401) { window.location.href = '/login?mode=login'; return; }
        if (!res.ok) throw new Error(`HTTP ${res.status}`);

        const blob = await res.blob();
        const url = window.URL.createObjectURL(blob);
        const a = document.createElement('a');
        a.href = url;
        // Timestamped filename: Contract_yyyy-MM-dd_HH-mm-ss.csv
        const d = new Date();
        const ts = `${d.getFullYear()}-${String(d.getMonth()+1).padStart(2,'0')}-${String(d.getDate()).padStart(2,'0')}_${String(d.getHours()).padStart(2,'0')}-${String(d.getMinutes()).padStart(2,'0')}-${String(d.getSeconds()).padStart(2,'0')}`;
        a.download = `Contract_${ts}.csv`;
        a.click();
        window.URL.revokeObjectURL(url);
        try { showToast('info', 'Export has been successfully generated and downloaded.'); } catch {}
      } catch (err) {
        try { showToast('error', 'Export failed'); } catch {}
      }
    });
  });
})();