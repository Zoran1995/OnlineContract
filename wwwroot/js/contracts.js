
(() => {
  const apiBase = "/api/contracts";

  let page = 1;
  let pageSize = 10;
  let totalPages = 1;
  let totalCount = 0;
  let selectedId = null;
  let items = [];

  const ddlState = document.getElementById("ddlState");
  const txtName = document.getElementById("txtName");
  const dtFrom = document.getElementById("dtFrom");
  const dtTo = document.getElementById("dtTo");

  const btnOpen = document.getElementById("btnOpen");
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
  function updateExportState() {
    if (exportBtn) exportBtn.disabled = !(items && items.length > 0);
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

  function datesInvalid() {
    const from = (dtFrom?.value || '').trim();
    const to   = (dtTo?.value   || '').trim();
    return from && to && from > to; // ISO yyyy-mm-dd comparison works directly
  }

  async function load() {
    try {
      if (datesInvalid()) {
        try { showToast('warning', 'From Date cannot be after To Date'); } catch {}
        setEmptyState(true);
        updateExportState();
        renderPager();
        selectedId = null;
        updateOpenState();
        return;
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
      });

      if (selectedId && it.id === selectedId) tr.classList.add('active');

      const cells = [
        String(it.id),
        String(it.customerFullName ?? ''),
        String(it.amount ?? ''),
        stateBadge(it.contractState),
        String(it.entryDate ?? '')
      ];
      cells.forEach(html => {
        const td = document.createElement("td");
        if (typeof html === "string" && html.startsWith("<")) td.innerHTML = html; else td.textContent = html;
        tr.appendChild(td);
      });
      tbody.appendChild(tr);
    });
    try { if (window.attachTableSort) window.attachTableSort('#grid'); } catch {}
  }

  function stateBadge(state) {
    const s = String(state || '')
      .toLowerCase()
      .replace(/\s+/g, ''); // "written off" -> "writtenoff"
    const color = (s === 'accepted' || s === 'completed' || s === 'delivered') ? 'badge-ok'
      : (s === 'rejected' || s === 'cancelled' || s === 'writtenoff') ? 'badge-bad'
      : 'badge-mid';
    return `<span class="badge ${color}">${state}</span>`;
  }

  function renderPager() {
    const pages = Math.max(1, Number(totalPages || 1));
    if (page > pages) page = pages;
    const start = totalCount === 0 ? 0 : (page - 1) * pageSize + 1;
    const end = totalCount === 0 ? 0 : Math.min(page * pageSize, totalCount);

    if (pageInfo) pageInfo.textContent = `Page ${page} of ${pages}`;
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
        a.download = 'contracts.csv';
        a.click();
        window.URL.revokeObjectURL(url);
        try { showToast('info', 'Export successful'); } catch {}
      } catch (err) {
        try { showToast('error', 'Export failed'); } catch {}
      }
    });
  });
})();