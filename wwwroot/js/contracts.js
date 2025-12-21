
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

  function buildQuery() {
    const qs = new URLSearchParams();
    qs.set("page", String(page));
    qs.set("pageSize", String(pageSize));
    const st = (ddlState?.value || "").trim();
    const nm = (txtName?.value || "").trim();
    if (st) qs.set("state", st);
    if (nm) qs.set("name", nm);
    return `?${qs.toString()}`;
  }

  function buildExportQuery() {
    const qs = new URLSearchParams();
    const st = (ddlState?.value || "").trim();
    const nm = (txtName?.value || "").trim();
    if (st) qs.set("state", st);
    if (nm) qs.set("name", nm);
    const s = qs.toString();
    return s ? `?${s}` : "";
  }

  async function load() {
    try {
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
    } catch (err) {
      try { showToast('error', 'Failed to load contracts'); } catch {}
      setEmptyState(true);
    } finally {
      hideLoading();
    }
  }

  function renderRows() {
    tbody.innerHTML = "";
    if (items.length === 0) {
      const tr = document.createElement("tr");
      const td = document.createElement("td");
      td.colSpan = 4;
      td.className = "empty";
      td.textContent = "No results.";
      tr.appendChild(td);
      tbody.appendChild(tr);
      return;
    }

    items.forEach(it => {
      const tr = document.createElement("tr");
      tr.addEventListener("click", () => {
        Array.from(tbody.querySelectorAll("tr.selected")).forEach(r => r.classList.remove("selected"));
        tr.classList.add("selected");
        selectedId = it.id;
        updateOpenState();
      });

      const cells = [
        String(it.id),
        String(it.customerFullName ?? ''),
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
  }

  function stateBadge(state) {
    const s = String(state || '').toLowerCase();
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

    prevPage.disabled = (page <= 1 || totalCount === 0);
    nextPage.disabled = (page >= pages || totalCount === 0);
  }

  document.addEventListener("DOMContentLoaded", () => {
    btnSearch?.addEventListener("click", (e) => { e.preventDefault(); page = 1; load(); });
    btnClear?.addEventListener("click", (e) => {
      e.preventDefault();
      ddlState.value = "";
      txtName.value = "";
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
      window.location.href = `/contract.html?id=${selectedId}`;
    });

    exportBtn?.addEventListener("click", async () => {
      try {
        // If the grid is empty (no contracts), show a friendly warning and do not export
        if (!items || items.length === 0) {
          showToast('warning', 'There is no data to export. Please run a search or adjust filters before exporting.');
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
        showToast('info', 'Export successful');
      } catch (err) {
        try { showToast('error', 'Export failed'); } catch {}
      }
    });

    // Initial load
    load();
  });
})();