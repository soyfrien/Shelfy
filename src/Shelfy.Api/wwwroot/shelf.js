(function () {
  var m = location.pathname.match(/\/s\/([0-9a-fA-F-]{36})/);
  var $ = function (id) { return document.getElementById(id); };
  if (!m) { $('loading').classList.add('hidden'); $('err').classList.remove('hidden'); return; }
  var shelfId = m[1];
  var A = window.ShelfyAccount, el = A.el;
  var search = null, state = { shelf: null, copies: [], me: null };

  // Lending (ADR-012): the shelf's books plus the copies on it. Copies borrowed from someone else ("visiting") show as extra books.
  function load() {
    return Promise.all([
      fetch('/api/shelves/' + shelfId, { headers: { Accept: 'application/json' } }).then(function (r) { if (!r.ok) throw new Error(r.status); return r.json(); }),
      A.api('GET', '/api/shelves/' + shelfId + '/copies').catch(function () { return []; }),
      A.me()
    ]).then(function (r) {
      state.shelf = r[0]; state.copies = r[1] || []; state.me = r[2];
      $('loading').classList.add('hidden');
      var books = (state.shelf.books || []).map(function (b) {
        var cs = copiesOf(b.bookId);
        return Object.assign({}, b, { lendLabel: cs.length ? label(cs) : '' });
      });
      state.copies.filter(function (c) { return c.isVisiting; }).forEach(function (c) {
        books.push({ bookId: 'copy:' + c.id, title: c.title, author: c.author, coverUrl: c.coverUrl, visiting: c, lendLabel: 'Borrowed from ' + c.ownerName });
      });
      if (!books.length) { $('empty').classList.remove('hidden'); return; }
      if (!search) search = window.Shelfy.createSearch({ ul: $('books'), onBookTap: openSheet });
      search.setBooks(books);
    });
  }
  function copiesOf(bookId) { return state.copies.filter(function (c) { return c.bookId === bookId && c.isHome; }); }
  function label(cs) {
    if (cs.some(function (c) { return c.status === 'Available'; })) return 'Available to borrow';
    return A.statusText(cs[0].status);
  }
  function isOwner() { return state.me && state.shelf && state.me.id === state.shelf.userId; }

  // ---- the bottom sheet ----
  var modal = el('div', 'modal hidden'); modal.setAttribute('role', 'dialog'); modal.setAttribute('aria-modal', 'true');
  var sheet = el('div', 'sheet lend'); modal.appendChild(sheet); document.body.appendChild(modal);
  modal.addEventListener('click', function (e) { if (e.target === modal) close(); });
  modal.setAttribute('aria-label', 'Borrow or lend this book');
  // Keyboard: focus moves into the sheet, Tab stays inside it, the shelf behind is inert, and focus returns to the book on close.
  var opener = null, page = document.querySelector('.wrap');
  function isOpen() { return !modal.classList.contains('hidden'); }
  function focusables() {
    return Array.prototype.filter.call(sheet.querySelectorAll('a[href], button:not([disabled]), input:not([disabled]), select:not([disabled]), textarea:not([disabled])'),
      function (n) { return n.offsetParent !== null; });
  }
  document.addEventListener('keydown', function (e) {
    if (!isOpen()) return;
    if (e.key === 'Escape') { close(); return; }
    if (e.key !== 'Tab') return;
    var f = focusables(); if (!f.length) { e.preventDefault(); return; }
    var first = f[0], last = f[f.length - 1];
    if (e.shiftKey && (document.activeElement === first || !sheet.contains(document.activeElement))) { e.preventDefault(); last.focus(); }
    else if (!e.shiftKey && (document.activeElement === last || !sheet.contains(document.activeElement))) { e.preventDefault(); first.focus(); }
  });
  function close() {
    if (!isOpen()) return;
    modal.classList.add('hidden'); document.body.classList.remove('sheet-open');
    if (page) { page.removeAttribute('inert'); page.removeAttribute('aria-hidden'); }
    if (opener && opener.focus && document.contains(opener)) opener.focus();
    opener = null;
  }
  function btn(text, cls, fn) { var b = el('button', 'btn ' + (cls || ''), text); b.type = 'button'; b.onclick = fn; return b; }
  function err(msg) { var p = sheet.querySelector('.formerr'); p.textContent = msg; p.classList.remove('hidden'); }
  function act(promise) { return promise.then(function () { return load(); }).then(function () { if (current) openSheet(current); }, function (e) { err(e.message); }); }
  var current = null;

  function openSheet(book) {
    current = book;
    sheet.textContent = '';
    sheet.appendChild(el('h2', null, book.title));
    if (book.author && book.author !== 'Unknown') sheet.appendChild(el('p', 'notice', book.author));
    var cs = book.visiting ? [book.visiting] : copiesOf(book.bookId);
    if (!cs.length) {
      if (isOwner()) {
        sheet.appendChild(el('p', 'notice', 'Lend this book to other readers? It gets a code for a sticker so it can find its way back to you. You approve every request.'));
        sheet.appendChild(btn('Lend this book', '', function () { act(A.api('POST', '/api/shelves/' + shelfId + '/books/' + book.bookId + '/copies', {})); }));
      } else {
        sheet.appendChild(el('p', 'notice', 'The owner isn’t lending this book (yet).'));
      }
    }
    cs.forEach(function (c) { sheet.appendChild(copyRow(c)); });
    if (isOwner() && cs.length && !book.visiting) {
      sheet.appendChild(btn('I have another copy to lend', 'secondary', function () { act(A.api('POST', '/api/shelves/' + shelfId + '/books/' + book.bookId + '/copies', {})); }));
    }
    sheet.appendChild(el('p', 'formerr hidden'));
    var c2 = el('div'); c2.style.textAlign = 'center'; c2.appendChild(btn('Close', 'link', close)); c2.firstChild.className = 'link'; sheet.appendChild(c2);
    if (!isOpen()) opener = document.activeElement;
    modal.classList.remove('hidden'); document.body.classList.add('sheet-open');
    if (page) { page.setAttribute('inert', ''); page.setAttribute('aria-hidden', 'true'); }
    sheet.setAttribute('tabindex', '-1');
    var f = focusables(); (f[0] || sheet).focus();
  }

  function copyRow(c) {
    var row = el('div', 'copyrow');
    var head = el('div'); head.appendChild(el('span', 'pill', A.statusText(c.status)));
    head.appendChild(document.createTextNode(' ' + c.ownerName + '’s copy · '));
    var link = el('a', 'code', c.code.slice(0, 4) + '-' + c.code.slice(4)); link.href = '/c/' + c.code; head.appendChild(link);
    if (c.waiting) head.appendChild(document.createTextNode(' · ' + c.waiting + ' waiting'));
    row.appendChild(head);
    var v = c.viewer;
    if (!v.signedIn) {
      var s = el('a', 'btn secondary', 'Sign in to borrow or reserve'); s.href = A.signInUrl(); row.appendChild(s);
    } else if (v.isOwner) {
      var lab = el('a', 'btn secondary', 'Print the QR label'); lab.href = '/api/copies/' + c.id + '/label.svg'; lab.target = '_blank'; row.appendChild(lab);
    } else if (v.myLoan) {
      row.appendChild(el('p', 'notice', 'Your request: ' + A.statusText(v.myLoan.status) + '. '));
      var ml = el('a', 'link', 'Open My loans'); ml.href = '/loans.html'; row.appendChild(ml);
    } else if (v.canBorrow) {
      row.appendChild(btn('Ask to borrow', '', function () {
        if (!state.me.mailingAddress) { location.href = '/loans.html'; return; }
        act(A.api('POST', '/api/copies/' + c.id + '/borrow', {}));
      }));
    } else if (v.canReserve) {
      row.appendChild(btn('Reserve (join the waitlist)', 'secondary', function () { act(A.api('POST', '/api/copies/' + c.id + '/reserve', {})); }));
    }
    return row;
  }

  load().catch(function () { $('loading').classList.add('hidden'); $('err').classList.remove('hidden'); });
})();
