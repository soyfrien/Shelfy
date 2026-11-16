// Sign in / create account (ADR-012). After success, goes back to ?next= (same-site paths only) or home.
(function () {
  'use strict';
  var $ = function (id) { return document.getElementById(id); };
  var next = new URLSearchParams(location.search).get('next') || '/';
  if (!/^\/(?!\/)/.test(next)) next = '/';                  // only local paths, never another site
  function tab(up) {
    $('tabIn').classList.toggle('on', !up); $('tabUp').classList.toggle('on', up);
    $('formIn').classList.toggle('hidden', up); $('formUp').classList.toggle('hidden', !up);
  }
  $('tabIn').onclick = function () { tab(false); };
  $('tabUp').onclick = function () { tab(true); };
  if (location.hash === '#register') tab(true);

  function submit(form, url) {
    form.addEventListener('submit', function (e) {
      e.preventDefault();
      var data = {}; new FormData(form).forEach(function (v, k) { data[k] = v; });
      var err = form.querySelector('.formerr'), btn = form.querySelector('button');
      err.classList.add('hidden'); btn.disabled = true;
      fetch(url, { method: 'POST', credentials: 'same-origin', headers: { 'Content-Type': 'application/json', Accept: 'application/json' }, body: JSON.stringify(data) })
        .then(function (r) { return r.json().catch(function () { return {}; }).then(function (j) { if (!r.ok) throw new Error(j.error || 'Something went wrong.'); }); })
        .then(function () { location.href = next; })
        .catch(function (x) { err.textContent = x.message; err.classList.remove('hidden'); btn.disabled = false; });
    });
  }
  submit($('formIn'), '/api/account/login');
  submit($('formUp'), '/api/account/register');
})();
