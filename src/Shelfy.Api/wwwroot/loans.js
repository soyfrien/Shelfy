// "My loans" (ADR-012): what I borrow and what others asked of me, with the next step as a button, plus my profile.
(function () {
  'use strict';
  var A = window.ShelfyAccount, el = A.el, $ = function (id) { return document.getElementById(id); };
  var LABEL = {
    approve: 'Approve', decline: 'Decline', cancel: 'Cancel', mailed: 'I mailed it', received: 'I received it',
    'request-return': 'Ask for it back', 'return-mailed': 'I mailed it back', returned: 'I got it back', 'pass-along': 'Let them pass it on to the next reader'
  };
  var SECONDARY = { decline: 1, cancel: 1, 'request-return': 1, 'pass-along': 1 };

  function card(l, me) {
    var li = el('li', 'card loan');
    var head = el('div', 'loanhead');
    var a = el('a', 'lt', l.title); a.href = '/c/' + l.copyCode.replace('-', ''); head.appendChild(a);
    head.appendChild(el('span', 'pill', (l.statusLabel || A.statusText(l.status, l)) + (l.waitlistPosition ? ' · #' + l.waitlistPosition : '')));
    li.appendChild(head);
    var who = l.role === 'borrower' ? 'From ' + l.ownerName + (l.isPassAlong && l.senderName ? ', passed on by ' + l.senderName : '')
      : l.role === 'owner' ? 'Requested by ' + l.borrowerName : 'Pass it on to ' + l.borrowerName;
    li.appendChild(el('div', 'notice', who + ' · ' + l.copyCode));
    if (l.message) li.appendChild(el('blockquote', null, l.message));
    if (l.shipTo) {
      var ship = el('div', 'address');
      var toMail = l.role !== 'borrower' && l.status === 'Approved';
      var addrLabel = l.role === 'borrower' ? (l.ownerName || 'Owner') + '’s address, for the return'
        : toMail ? 'Mail it to' : (l.borrowerName || 'Borrower') + '’s address';
      ship.appendChild(el('small', null, addrLabel + ' (private)'));
      ship.appendChild(el('div', null, l.shipToName));
      ship.appendChild(el('div', 'pre', l.shipTo));
      li.appendChild(ship);
    }
    var row = el('div', 'actions');
    l.actions.forEach(function (act) {
      var b = el('button', 'btn' + (SECONDARY[act] ? ' secondary' : ''), LABEL[act] || act); b.type = 'button';
      b.onclick = function () {
        var body;
        if (act === 'received') {
          // The sticker code proves the book is in your hands (the server checks it).
          var typed = window.prompt('Type the code on the book’s sticker (like 7K3M-Q9XA) to confirm it arrived:');
          if (typed === null || !typed.trim()) return;
          body = { code: typed.trim() };
          if (me.shelves.length) body.shelfId = me.shelves[0].id;
        }
        b.disabled = true;
        A.api('POST', '/api/loans/' + l.id + '/' + act, body).then(load, function (e) { $('err').textContent = e.message; $('err').classList.remove('hidden'); b.disabled = false; });
      };
      row.appendChild(b);
    });
    if (row.childNodes.length) li.appendChild(row);
    return li;
  }

  function load() {
    A.me(true).then(function (me) {
      if (!me) { location.href = A.signInUrl(); return; }
      var f = $('profileForm');
      f.displayName.value = me.displayName; f.city.value = me.city || ''; f.mailingAddress.value = me.mailingAddress || '';
      if (!me.mailingAddress) $('profile').open = true;
      if (!$('libCard').src) $('libCard').src = '/api/account/card.svg';
      return A.api('GET', '/api/loans').then(function (r) {
        $('loading').classList.add('hidden'); $('main').classList.remove('hidden'); $('err').classList.add('hidden');
        [['borrowing', 'noBorrowing'], ['lending', 'noLending']].forEach(function (p) {
          var ul = $(p[0]); ul.textContent = '';
          r[p[0]].forEach(function (l) { ul.appendChild(card(l, me)); });
          $(p[1]).classList.toggle('hidden', r[p[0]].length > 0);
        });
      });
    });
  }

  $('profileForm').addEventListener('submit', function (e) {
    e.preventDefault();
    var f = e.target;
    A.api('PUT', '/api/account/profile', { displayName: f.displayName.value, city: f.city.value, mailingAddress: f.mailingAddress.value })
      .then(function () { $('saved').classList.remove('hidden'); $('libCard').src = '/api/account/card.svg?v=' + Date.now(); }, function (x) { $('err').textContent = x.message; $('err').classList.remove('hidden'); });
  });
  load();
})();
