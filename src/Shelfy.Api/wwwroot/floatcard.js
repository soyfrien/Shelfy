// A small library card drifting in the page margin (decorative, ADR-012 lending): the member's own card when signed in,
// a sample "Your name here" card that leads to sign-up otherwise. Loaded after the page, so it never delays or shifts content.
(function () {
  'use strict';
  function me() {
    if (window.ShelfyAccount) return window.ShelfyAccount.me();
    return fetch('/api/account/me', { credentials: 'same-origin', headers: { Accept: 'application/json' } })
      .then(function (r) { return r.ok ? r.json() : null; }).catch(function () { return null; });
  }
  function add() {
    me().then(function (u) {
      if (document.querySelector('.floatcard')) return;
      var a = document.createElement('a');
      a.className = 'floatcard';
      if (u) {
        a.href = '/loans.html#card';
        a.setAttribute('aria-label', 'Your Shelfy Library card: open My loans');
      } else {
        a.href = '/signin.html#register';
        a.setAttribute('aria-label', 'Forge your own Shelfy Library card');
      }
      var img = document.createElement('img');
      img.src = u ? '/api/account/card.svg' : '/api/card/sample.svg';
      img.alt = ''; img.setAttribute('aria-hidden', 'true');
      img.width = 856; img.height = 540; img.loading = 'lazy'; img.decoding = 'async';
      img.onerror = function () { a.remove(); };
      a.appendChild(img);
      document.body.appendChild(a);
    });
  }
  if (document.readyState === 'complete') setTimeout(add, 0);
  else window.addEventListener('load', function () { setTimeout(add, 0); });
})();
