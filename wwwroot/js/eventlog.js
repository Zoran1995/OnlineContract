let currentPage = 1;
let pageSize = 10;
let totalPages = 0;

const typeFilter = document.getElementById('typeFilter');
const fromDate = document.getElementById('fromDate');
const toDate = document.getElementById('toDate');
const searchBtn = document.getElementById('searchBtn');
const clearBtn = document.getElementById('clearBtn');
const tableBody = document.querySelector('#logTable tbody');
const pageInfo = document.getElementById('pageInfo');
const pageCountInfo = document.getElementById('pageCountInfo');
const prevPage = document.getElementById('prevPage');
const nextPage = document.getElementById('nextPage');
const pageSizeSelect = document.getElementById('pageSize');
const exportBtn = document.getElementById('exportBtn');

function setDefaultDates() {
  const today = new Date();
  const yyyy = today.getFullYear();
  const mm = String(today.getMonth() + 1).padStart(2, '0');
  const dd = String(today.getDate()).padStart(2, '0');
  fromDate.value = `${yyyy}-${mm}-${dd}T00:00`;
  toDate.value = `${yyyy}-${mm}-${dd}T23:59`;
}

document.addEventListener('DOMContentLoaded', () => {
  setDefaultDates();
  // Decorate export button with icon for exporting data
  try {
    if (exportBtn) {
      exportBtn.innerHTML = '<span class="material-icons mr-2">file_download</span> Export CSV';
    }
    // Decorate search and clear buttons with icons consistent with navbar style
    if (searchBtn) {
      searchBtn.innerHTML = '<span class="material-icons mr-2">search</span> Search';
    }
    if (clearBtn) {
      clearBtn.innerHTML = '<span class="material-icons mr-2">clear_all</span> Clear';
    }
  } catch {}
  loadLogs();
});

searchBtn.addEventListener('click', () => {
  currentPage = 1;
  loadLogs();
});

clearBtn.addEventListener('click', () => {
  typeFilter.value = "0";
  setDefaultDates();
  currentPage = 1;

  // Behave like Users/Contracts: reset to defaults and run the default search.
  loadLogs();
});

pageSizeSelect.addEventListener('change', () => {
  pageSize = parseInt(pageSizeSelect.value, 10);
  currentPage = 1;
  loadLogs();
});

prevPage.addEventListener('click', () => {
  if (currentPage > 1) {
    currentPage--;
    loadLogs();
  }
});

nextPage.addEventListener('click', () => {
  if (currentPage < totalPages) {
    currentPage++;
    loadLogs();
  }
});

exportBtn.addEventListener('click', async () => {
  // If the grid is empty (no rows), show a friendly warning and do not export
  try {
    const rowCount = tableBody.querySelectorAll('tr').length;
    if (rowCount === 0) {
      showToast('warning', 'There is no data to export. Please run a search or adjust filters before exporting.');
      return;
    }

    const userId = localStorage.getItem('userId') || 2;
    const res = await fetch(`/api/event-log/export?userId=${userId}`);
    if (!res.ok) throw new Error("Export failed");
    const blob = await res.blob();
    const url = window.URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = "eventlog.csv";
    a.click();
    window.URL.revokeObjectURL(url);
    showToast('info', 'Export successful');
  } catch (err) {
    logClientError("Export failed", err.stack || "");
    showToast('error', 'Export failed');
  }
});

function showLoading() {
  document.getElementById('loadingOverlay').classList.remove('hidden');
}
function hideLoading() {
  document.getElementById('loadingOverlay').classList.add('hidden');
}

async function loadLogs() {
  try {
    if (fromDate.value && toDate.value) {
      const from = new Date(fromDate.value);
      const to = new Date(toDate.value);
      if (isNaN(from.getTime()) || isNaN(to.getTime())) {
        showToast('error', 'Invalid date format');
        return;
      }
      if (from > to) {
        showToast('warning', 'The start date must be before the end date. Please adjust the date range and try again.');
        return;
      }
    }

    searchBtn.disabled = true;
    showLoading();
    tableBody.innerHTML = "";

    const userId = localStorage.getItem('userId') || 2;
    const params = new URLSearchParams({
      userId,
      type: typeFilter.value,
      from: fromDate.value,
      to: toDate.value,
      page: currentPage,
      pageSize
    });

    const res = await fetch(`/api/event-log?${params.toString()}`);
    if (!res.ok) throw new Error("Failed to fetch logs");

    const data = await res.json();

    renderLogs(data.items);
    totalPages = Math.max(1, Number(data.totalPages) || 1);
    if (currentPage > totalPages) currentPage = totalPages;
    pageInfo.textContent = `Page ${currentPage} of ${totalPages}`;
    pageCountInfo.textContent = `Total records: ${data.totalCount}`;

    // Disable Prev/Next at edges (same UX as Users)
    prevPage.disabled = (currentPage <= 1 || data.totalCount === 0);
    nextPage.disabled = (currentPage >= totalPages || data.totalCount === 0);

    // Keep Clear enabled (same behavior as Users/Contracts)
    clearBtn.disabled = false;
    document.getElementById('emptyState').classList.toggle('hidden', data.items.length > 0);
  } catch (err) {
    logClientError("Load logs failed", err.stack || "");
    showToast('error', 'Failed to load logs');
  } finally {
    searchBtn.disabled = false;
    hideLoading();
  }
}

function renderLogs(items) {
  tableBody.innerHTML = "";
  items.forEach(item => {
    const tr = document.createElement('tr');

    const tdId = document.createElement('td');
    tdId.textContent = String(item.id);

    const tdType = document.createElement('td');
    tdType.textContent = String(item.type);

    const tdDate = document.createElement('td');
    tdDate.textContent = String(item.date);

    const tdDesc = document.createElement('td');
    tdDesc.textContent = item.description || "";

    const tdUser = document.createElement('td');
    tdUser.textContent = String(item.user);

    const tdStack = document.createElement('td');
    const stackDiv = document.createElement('div');
    stackDiv.className = 'stack-trace';
    stackDiv.textContent = item.stackTrace || "";
    stackDiv.addEventListener('click', () => {
      stackDiv.classList.toggle('expanded');
    });
    tdStack.appendChild(stackDiv);

    tr.appendChild(tdId);
    tr.appendChild(tdType);
    tr.appendChild(tdDate);
    tr.appendChild(tdDesc);
    tr.appendChild(tdUser);
    tr.appendChild(tdStack);

    tableBody.appendChild(tr);
  });
}

async function logClientError(description, stackTrace) {
  try {
    const userId = localStorage.getItem('userId') || 2;
    await fetch('/api/log-client-error', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ description, stackTrace, userId })
    });
  } catch {
    // ignore
  }
}