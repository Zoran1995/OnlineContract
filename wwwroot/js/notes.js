(() => {
  const apiBase = '/api/notes';
  let page = 1, pageSize = 10, totalPages = 1, totalCount = 0;
  let selectedId = null, selectedRow = null, items = [];

  const tbl = document.querySelector('#notesTable tbody');
  const fltContract = document.getElementById('fltContractId');
  const fltProduct = document.getElementById('fltProductId');
  const btnSearch = document.getElementById('btnSearch');
  const btnClear = document.getElementById('btnClear');
  const btnOpen = document.getElementById('btnOpen');
  const btnAdd = document.getElementById('btnAddNote');
  const pgPrev = document.getElementById('pgPrev');
  const pgNext = document.getElementById('pgNext');
  const pgInfo = document.getElementById('pgInfo');
  const pageCountInfo = document.getElementById('pageCountInfo');
  const pageSizeSel = document.getElementById('pgSize');

  function showLoading(){}
  function hideLoading(){}

  async function load() {
    try {
      const q = new URLSearchParams();
      q.set('page', String(page));
      q.set('pageSize', String(pageSize));
      const c = (fltContract?.value || '').trim();
      const p = (fltProduct?.value || '').trim();
      if (c) q.set('contractId', c);
      if (p) q.set('productId', p);

      const res = await fetch(`${apiBase}?${q.toString()}`, { method: 'GET', credentials: 'include', headers: { 'Accept': 'application/json' }});
      if (!res.ok) { items = []; render(); return; }
      const data = await res.json();
      items = Array.isArray(data.items) ? data.items : [];
      totalCount = Number(data.totalCount ?? items.length);
      totalPages = Number(data.totalPages) || Math.max(1, Math.ceil(totalCount / pageSize));
      render();
    } catch (e) { items = []; render(); }
  }

  function render() {
    if (!tbl) return;
    tbl.innerHTML = '';
    if (!items || items.length === 0) {
      const tr = document.createElement('tr');
      const td = document.createElement('td'); td.colSpan = 7; td.className = 'empty'; td.textContent = 'No results.'; tr.appendChild(td); tbl.appendChild(tr); updatePager(); return;
    }
    items.forEach(it => {
      const tr = document.createElement('tr');
      tr.addEventListener('click', () => { Array.from(tbl.querySelectorAll('tr.active')).forEach(r=>r.classList.remove('active')); tr.classList.add('active'); selectedId = it.id; updateOpenState(); });
      const cells = [ String(it.id ?? ''), String(it.contractId ?? ''), String(it.productId ?? ''), String(it.subject ?? ''), String(it.inputDt ?? ''), String(it.inputUserId ?? ''), String(it.status ?? '') ];
      cells.forEach(text => { const td = document.createElement('td'); td.textContent = text; tr.appendChild(td); });
      tbl.appendChild(tr);
    });
    updatePager();
  }

  function updateOpenState(){ if (btnOpen) btnOpen.disabled = !selectedId; }
  function updatePager(){
    const total = Number(totalCount) || 0;
    const pages = Math.max(1, Number(totalPages) || Math.ceil(total / pageSize));
    const start = total === 0 ? 0 : ((page - 1) * pageSize) + 1;
    const end = total === 0 ? 0 : Math.min(page * pageSize, total);
    if (pgInfo) pgInfo.textContent = `${start}–${end} of ${total} · pages ${pages}`;
    if (pageCountInfo) pageCountInfo.textContent = `Total records: ${total}`;
    if (pgPrev) pgPrev.disabled = page<=1;
    if (pgNext) pgNext.disabled = page>=pages;
  }

  document.addEventListener('DOMContentLoaded', () => {
    // Do not trigger a load on initial page open — start with an empty grid
    pageSizeSel?.addEventListener('change', (e)=>{ pageSize = Number(e.target.value)||10; page=1; load(); });
    btnSearch?.addEventListener('click', (e)=>{ 
      e.preventDefault(); 
      // require at least one filter
      const c = (fltContract?.value || '').trim();
      const p = (fltProduct?.value || '').trim();
      if (!c && !p) { try { showToast('warning', 'Provide more search criteria before proceeding.'); } catch {} return; }
      page=1; load(); 
    });
    btnClear?.addEventListener('click', (e)=>{ e.preventDefault(); if(fltContract) fltContract.value=''; if(fltProduct) fltProduct.value=''; page=1; items = []; render(); });
    pgPrev?.addEventListener('click', ()=>{ if(page>1){ page--; load(); } });
    pgNext?.addEventListener('click', ()=>{ if(page<totalPages){ page++; load(); } });
    btnOpen?.addEventListener('click', async ()=>{
      if (!selectedId) return;
      try {
        const res = await fetch(`${apiBase}/${selectedId}`, { method: 'GET', credentials: 'include', headers: { 'Accept': 'application/json' }});
        if (!res.ok) return;
        const data = await res.json();
        // populate modal for editing
        document.getElementById('editNoteId').value = String(data.id || '');
        document.getElementById('newSubject').value = data.subject ?? '';
        document.getElementById('newComment').value = data.comment ?? '';
        document.getElementById('newContractId').value = data.contractId ?? '';
        document.getElementById('newProductId').value = data.productId ?? '';
        document.getElementById('noteModalTitle').textContent = 'Edit Note';
        const dlg = document.getElementById('addNoteModal');
        try{ dlg.showModal(); } catch { dlg.classList.remove('hidden'); }
      } catch (e) { }
    });
    btnAdd?.addEventListener('click', ()=>{
      // Ensure Add always creates a new note regardless of current selection
      selectedId = null;
      try { render(); } catch {}
      try { updateOpenState(); } catch {}
      // clear modal inputs for new note
      document.getElementById('editNoteId').value = '';
      document.getElementById('newSubject').value = '';
      document.getElementById('newComment').value = '';
      document.getElementById('newContractId').value = '';
      document.getElementById('newProductId').value = '';
      document.getElementById('noteModalTitle').textContent = 'Add Note';
      const dlg = document.getElementById('addNoteModal');
      try{ dlg.showModal(); }catch{ dlg.classList.remove('hidden'); }
    });

    // Add note save
    const addSave = document.getElementById('addNoteSave');
    addSave?.addEventListener('click', async (e)=>{
      e.preventDefault();
      const subject = document.getElementById('newSubject')?.value || '';
      const comment = document.getElementById('newComment')?.value || '';
      const contractIdVal = document.getElementById('newContractId')?.value || '';
      const productIdVal = document.getElementById('newProductId')?.value || '';
      const contractId = contractIdVal === '' ? null : Number(contractIdVal);
      const productId = productIdVal === '' ? null : Number(productIdVal);
      const editId = (document.getElementById('editNoteId')?.value || '').trim();
      try {
        const payload = { Subject: subject, Comment: comment, ContractId: contractId, ProductId: productId };
        let res;
        if (editId) {
          res = await fetch(`/api/notes/${encodeURIComponent(editId)}`, { method: 'PUT', credentials: 'include', headers: {'Content-Type':'application/json','Accept':'application/json'}, body: JSON.stringify(payload)});
        } else {
          res = await fetch('/api/notes', { method: 'POST', credentials: 'include', headers: {'Content-Type':'application/json','Accept':'application/json'}, body: JSON.stringify(payload)});
        }
        if (res && res.ok) { try{ showToast('info', editId ? 'Note updated.' : 'Note has been added successfully.'); }catch{}; const dlg = document.getElementById('addNoteModal'); try{ dlg.close(); }catch{}; await load(); }
        else { try{ showToast('error','Failed to save the note. Please try again later.'); }catch{} }
      } catch { try{ showToast('error','Failed to save note'); }catch{} }
    });

    load();
  });
})();
