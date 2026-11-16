// /c/{code}: the page a copy's QR sticker opens (ADR-012). Shows the copy and its journey; the person it was mailed to gets
// "I received this" (moves it to their shelf), its owner gets the same when it comes home, plus the printable label.
(function () {
  'use strict';
  var A = window.ShelfyAccount, el = A.el, $ = function (id) { return document.getElementById(id); };
  var code = decodeURIComponent(location.pathname.split('/').pop() || '');
  function when(d) { return d ? new Date(d).toLocaleDateString(undefined, { year: 'numeric', month: 'short', day: 'numeric' }) : ''; }
  function fail(msg) { $('actErr').textContent = msg; $('actErr').classList.remove('hidden'); }
  function button(text, cls, fn) { var b = el('button', 'btn ' + (cls || ''), text); b.type = 'button'; b.onclick = fn; return b; }

  function load() {
    Promise.all([A.api('GET', '/api/copies/by-code/' + encodeURIComponent(code)), A.me()]).then(function (r) { render(r[0], r[1]); })
      .catch(function () { $('loading').classList.add('hidden'); $('err').classList.remove('hidden'); });
  }

  function render(c, me) {
    $('loading').classList.add('hidden'); $('copy').classList.remove('hidden');
    $('title').textContent = c.title; $('author').textContent = c.author && c.author !== 'Unknown' ? c.author : '';
    $('code').textContent = c.displayCode; $('status').textContent = A.statusText(c.status);
    $('cover').textContent = '';
    if (c.coverUrl && /^https?:\/\//.test(c.coverUrl)) { var img = el('img'); img.alt = ''; img.referrerPolicy = 'no-referrer'; img.src = c.coverUrl; img.onerror = function () { img.remove(); }; $('cover').appendChild(img); }
    $('where').textContent = c.ownerName + '’s copy' + (c.status === 'InTransit' ? ', in the mail.' : c.holderName !== c.ownerName ? ', now with ' + c.holderName + '.' : ', at home.') +
      (c.waiting ? ' ' + c.waiting + ' waiting.' : '');

    var box = $('actions'); box.textContent = ''; $('actErr').classList.add('hidden');
    var v = c.viewer, my = v.myLoan;
    if (!v.signedIn) {
      var s = el('a', 'btn secondary', 'Sign in to borrow, reserve or confirm you received it'); s.href = A.signInUrl(); box.appendChild(s);
    } else if (my && my.actions.indexOf('received') >= 0) {
      box.appendChild(receiveUi(me));
    } else if (v.isOwner) {
      if (v.awaitingReturn) {
        box.appendChild(button('I got it back', '', function () { receive(null); }));
      }
      var lab = el('a', 'btn secondary', 'Print the QR label'); lab.href = '/api/copies/' + c.id + '/label.svg'; lab.target = '_blank'; box.appendChild(lab);
    } else if (v.canBorrow) {
      box.appendChild(button('Ask to borrow', '', function () { ask('borrow'); }));
    } else if (v.canReserve) {
      box.appendChild(button('Reserve (join the waitlist)', 'secondary', function () { ask('reserve'); }));
    } else if (my) {
      box.appendChild(el('p', 'notice', 'Your request: ' + A.statusText(my.status, { statusLabel: my.statusLabel, ownerName: c.ownerName, isOwner: v.isOwner }) + '.'));
    }
    if (v.signedIn) { var ml = el('a', 'link', 'My loans'); ml.href = '/loans.html'; box.appendChild(ml); }

    var h = $('history'); h.textContent = '';
    (c.history || []).slice().reverse().forEach(function (m) {
      var li = el('li');
      var text = m.kind === 'Registered' ? 'Joined Shelfy on ' + m.to + '’s shelf'
        : m.kind === 'Returned' ? m.from + ' mailed it back to ' + m.to
        : m.from + ' mailed it to ' + m.to;
      li.appendChild(el('span', null, text));
      li.appendChild(el('small', null, when(m.startedAt) + (m.kind !== 'Registered' ? (m.receivedAt ? ' · arrived ' + when(m.receivedAt) : ' · in the mail') : '')));
      h.appendChild(li);
    });
    $('historyBox').classList.toggle('hidden', !(c.history || []).length);
  }

  function receiveUi(me) {
    var wrap = el('div');
    var shelves = (me && me.shelves) || [], select = null;
    if (shelves.length > 1) {
      var label = el('label', 'notice', 'Put it on: ');
      select = el('select');
      shelves.forEach(function (s, i) { var o = el('option', null, 'Shelf ' + (i + 1) + ' (' + s.bookCount + ' books)'); o.value = s.id; select.appendChild(o); });
      label.appendChild(select); wrap.appendChild(label);
    }
    wrap.appendChild(button('I received this', '', function () {
      receive(select ? select.value : shelves.length ? shelves[0].id : null);
    }));
    return wrap;
  }

  function receive(shelfId) {
    A.api('POST', '/api/copies/by-code/' + encodeURIComponent(code) + '/received', { shelfId: shelfId }).then(load, function (e) { fail(e.message); });
  }
  function ask(kind) {
    A.api('GET', '/api/copies/by-code/' + encodeURIComponent(code)).then(function (c) {
      return A.api('POST', '/api/copies/' + c.id + '/' + kind, {});
    }).then(load, function (e) { fail(e.message); });
  }
  load();
})();
