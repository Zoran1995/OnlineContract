/**
 * Unified Grid Footer Adapter
 * Provides consistent footer behavior across all grid pages.
 * 
 * Usage:
 *   bindGridFooter(footerRoot, {
 *     onPrev: () => { ... },
 *     onNext: () => { ... },
 *     onPageSizeChange: (size) => { ... },
 *     getState: () => ({ page, totalPages, totalRecords, pageSize, pageSizes })
 *   });
 */

/**
 * Bind a grid footer to pagination controls.
 * @param {HTMLElement} footerRoot - The footer container element
 * @param {Object} opts - Configuration options
 * @param {Function} opts.onPrev - Callback when Previous is clicked
 * @param {Function} opts.onNext - Callback when Next is clicked
 * @param {Function} opts.onPageSizeChange - Callback when page size changes (receives new size)
 * @param {Function} opts.getState - Returns current state { page, totalPages, totalRecords, pageSize, pageSizes? }
 */
export function bindGridFooter(footerRoot, { onPrev, onNext, onPageSizeChange, getState }) {
  if (!footerRoot) return;

  // Find elements - support multiple ID naming conventions
  const pageSizeSelect = footerRoot.querySelector('select[id*="Size"], select[id*="size"]');
  const prevBtn = footerRoot.querySelector('button[id*="Prev"], button[id*="prev"]');
  const nextBtn = footerRoot.querySelector('button[id*="Next"], button[id*="next"]');
  const pageInfoEl = footerRoot.querySelector('[id*="Info"]:not([id*="Count"]), [id*="info"]:not([id*="Count"])');
  const pageCountEl = footerRoot.querySelector('[id*="Count"], [id*="count"]');

  // Attach event listeners
  if (pageSizeSelect && onPageSizeChange) {
    pageSizeSelect.addEventListener('change', (e) => {
      const size = parseInt(e.target.value, 10) || 10;
      onPageSizeChange(size);
    });
  }

  if (prevBtn && onPrev) {
    prevBtn.addEventListener('click', () => {
      const state = getState();
      if (state.page > 1) {
        onPrev();
      }
    });
  }

  if (nextBtn && onNext) {
    nextBtn.addEventListener('click', () => {
      const state = getState();
      if (state.page < state.totalPages) {
        onNext();
      }
    });
  }

  // Return an update function for refreshing the UI
  return function updateFooter() {
    const state = getState();
    const page = Number(state.page) || 1;
    const totalPages = Math.max(1, Number(state.totalPages) || 1);
    const totalRecords = Number(state.totalRecords) || 0;
    const pageSize = Number(state.pageSize) || 10;

    // Update page info text
    if (pageInfoEl) {
      pageInfoEl.innerHTML = `Page <b>${page}</b> of <b>${totalPages}</b>`;
    }

    // Update total records
    if (pageCountEl) {
      pageCountEl.textContent = `Total records: ${totalRecords}`;
    }

    // Update button states
    if (prevBtn) {
      prevBtn.disabled = page <= 1 || totalRecords === 0;
    }
    if (nextBtn) {
      nextBtn.disabled = page >= totalPages || totalRecords === 0;
    }

    // Update page size select if needed
    if (pageSizeSelect && pageSizeSelect.value !== String(pageSize)) {
      pageSizeSelect.value = String(pageSize);
    }
  };
}

/**
 * Renders a standard grid footer HTML structure.
 * @param {Object} opts - Options for rendering
 * @param {string} opts.pageSizeId - ID for the page size select
 * @param {string} opts.prevId - ID for the Previous button
 * @param {string} opts.nextId - ID for the Next button
 * @param {string} opts.pageInfoId - ID for the page info span
 * @param {string} opts.pageCountId - ID for the total records span
 * @param {number[]} opts.pageSizes - Available page sizes (default: [10, 20, 50, 100])
 * @param {number} opts.defaultPageSize - Default selected page size (default: 10)
 * @returns {string} HTML string for the footer
 */
export function renderGridFooterHtml({
  pageSizeId = 'pageSize',
  prevId = 'prevPage',
  nextId = 'nextPage',
  pageInfoId = 'pageInfo',
  pageCountId = 'pageCountInfo',
  pageSizes = [10, 20, 50, 100],
  defaultPageSize = 10
} = {}) {
  const options = pageSizes.map(size => 
    `<option value="${size}"${size === defaultPageSize ? ' selected' : ''}>${size}</option>`
  ).join('');

  return `
    <div class="grid-footer">
      <div class="rows-per-page">
        <label class="text-sm">Rows per page:</label>
        <select id="${pageSizeId}" class="select select-bordered w-20 select-sm">
          ${options}
        </select>
      </div>
      <div class="pager">
        <button id="${prevId}" class="btn btn-outline btn-sm" disabled>
          <span class="material-icons mr-2">chevron_left</span>
          Previous
        </button>
        <span id="${pageInfoId}" class="page-indicator">Page <b>1</b> of <b>1</b></span>
        <button id="${nextId}" class="btn btn-outline btn-sm" disabled>
          Next
          <span class="material-icons ml-2">chevron_right</span>
        </button>
      </div>
      <span id="${pageCountId}" class="total-records">Total records: 0</span>
    </div>
  `.trim();
}
