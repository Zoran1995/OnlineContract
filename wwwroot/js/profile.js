(function(){
  const btnSave = document.getElementById('btnSave');
  const pCode = document.getElementById('pCode');
  const pEmail = document.getElementById('pEmail');
  const pFirst = document.getElementById('pFirstName');
  const pLast = document.getElementById('pLastName');
  const pPhone = document.getElementById('pPhone');
  const pCur = document.getElementById('pCur');
  const pNew = document.getElementById('pNew');
  const pNew2 = document.getElementById('pNew2');
  const pCity = document.getElementById('pCity');
  const pStreet = document.getElementById('pStreet');
  const pPostal = document.getElementById('pPostal');

  let stamp = 0;

  async function loadProfile(){
    try{
      const res = await fetch('/api/profile', { method:'GET', credentials:'include', headers: { 'Accept':'application/json' } });
      if (res.status === 401){ window.location.href = '/login?mode=login'; return; }
      if (!res.ok) throw new Error('HTTP '+res.status);
      const j = await res.json();
      pCode.value = j.code || '';
      pEmail.value = j.email || '';
      pFirst.value = j.firstName || '';
      pLast.value = j.lastName || '';
      pPhone.value = j.phoneNumber || '';
      pCity.value = j.city || '';
      pStreet.value = j.streetAddress || '';
      pPostal.value = j.postalCode || '';
      stamp = j.stamp || 0;
    }catch{ try{ showToast('error','Failed to load profile.'); }catch{} }
  }

  async function saveProfile(){
    try{
      const body = {
        code: (pCode.value||'').trim(),
        email: (pEmail.value||'').trim(),
        firstName: (pFirst.value||'').trim(),
        lastName: (pLast.value||'').trim(),
        phoneNumber: (pPhone.value||'').trim(),
        city: (pCity.value||'').trim(),
        streetAddress: (pStreet.value||'').trim(),
        postalCode: (pPostal.value||'').trim(),
        stamp: stamp,
        currentPassword: (pCur.value||'').trim() || null,
        newPassword: (pNew.value||'').trim() || null,
        confirmNewPassword: (pNew2.value||'').trim() || null
      };
      const res = await fetch('/api/profile', { method:'PUT', credentials:'include', headers: { 'Content-Type':'application/json', 'Accept':'application/json' }, body: JSON.stringify(body) });
      if (res.status === 401){ window.location.href = '/login?mode=login'; return; }
      if (res.status === 409){ try{ showToast('warning','Your profile was changed by someone else. Please reload and try again.'); }catch{} return; }
      if (!res.ok) throw new Error('HTTP '+res.status);
      const j = await res.json();
      if (j && j.success){
        stamp = j.stamp || stamp;
        try{ showToast('info','Profile has been successfully saved.'); }catch{}
        // clear password boxes after success
        pCur.value = '';
        pNew.value = '';
        pNew2.value = '';
      }else{
        try{ showToast('warning', j.message || 'Validation failed'); }catch{}
      }
    }catch{ try{ showToast('error','Failed to save profile.'); }catch{} }
  }

  document.addEventListener('DOMContentLoaded', () => {
    loadProfile();
    btnSave?.addEventListener('click', (e) => { e.preventDefault(); saveProfile(); });
  });
})();
