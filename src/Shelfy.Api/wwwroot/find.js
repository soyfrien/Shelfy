// Cross-shelf search UI for /search.html. User data is written with textContent and createElement only.
(function () {
  'use strict';
  var LIMIT = 20;
  var form = document.getElementById('form');
  var input = document.getElementById('q');
  var hits = document.getElementById('hits');
  var status = document.getElementById('status');
  var err = document.getElementById('err');
  var empty = document.getElementById('empty');
  var emptyTitle = document.getElementById('emptyTitle');
  var emptyText = document.getElementById('emptyText');
  var moreWrap = document.getElementById('moreWrap');
  var moreBtn = document.getElementById('more');
  if (!form || !input || !hits) return;

  var offset = 0;
  var total = 0;
  var pending = null;

  function show(el, on) { el.classList.toggle('hidden', !on); }
  function setText(el, text) { el.textContent = text == null ? '' : String(text); }
  function isHttpUrl(url) {
    try { var u = new URL(url); return u.protocol === 'http:' || u.protocol === 'https:'; }
    catch (e) { return false; }
  }

  function cover(url, title) {
    var box = document.createElement('div'); box.className = 'cover';
    var initial = document.createElement('span');
    initial.textContent = (title || '?').replace(/^\s*(the|a|an)\s+/i, '').charAt(0).toUpperCase() || '?';
    box.appendChild(initial);
    if (url && isHttpUrl(url)) {
      var img = document.createElement('img');
      img.alt = '';
      img.src = url;
      img.addEventListener('load', function () { img.classList.add('loaded'); });
      img.addEventListener('error', function () { img.remove(); });
      box.appendChild(img);
    }
    return box;
  }

  function holdRow(hold) {
    var li = document.createElement('li'); li.className = 'hold';
    var shelf = document.createElement('a');
    shelf.href = '/s/' + hold.id;
    setText(shelf, (hold.ownerName || 'Someone') + "'s shelf");
    li.appendChild(shelf);
    var pill = document.createElement('span');
    pill.className = 'pill';
    setText(pill, hold.available ? 'Available to borrow' : 'Not available just now');
    li.appendChild(pill);
    if (hold.copies && hold.copies.length) {
      var copies = document.createElement('span'); copies.className = 'copies';
      hold.copies.forEach(function (c) {
        var a = document.createElement('a');
        a.href = '/c/' + c.code;
        setText(a, c.code + (c.status && c.status !== 'Available' ? ' · ' + c.status : ''));
        copies.appendChild(a);
      });
      li.appendChild(copies);
    }
    return li;
  }

  function bookCard(book) {
    var li = document.createElement('li'); li.className = 'hit';
    var head = document.createElement('div'); head.className = 'hithead';
    head.appendChild(cover(book.coverUrl, book.title));
    var meta = document.createElement('div'); meta.className = 'hitmeta';
    var title = document.createElement('h2'); title.className = 'hit-title'; setText(title, book.title || 'Untitled');
    var author = document.createElement('div'); author.className = 'hit-author'; setText(author, book.author || '');
    meta.appendChild(title); meta.appendChild(author);
    if (book.isbn) {
      var isbn = document.createElement('div'); isbn.className = 'hit-isbn'; setText(isbn, 'ISBN ' + book.isbn);
      meta.appendChild(isbn);
    }
    var avail = document.createElement('div'); avail.className = 'pill hit-avail';
    setText(avail, book.available ? 'A copy is available to borrow' : 'No copy is free to borrow just now');
    meta.appendChild(avail);
    head.appendChild(meta);
    li.appendChild(head);
    if (book.shelves && book.shelves.length) {
      var holds = document.createElement('ul'); holds.className = 'holds';
      book.shelves.forEach(function (s) { holds.appendChild(holdRow(s)); });
      li.appendChild(holds);
    }
    return li;
  }

  function setError(message) {
    show(status, false); show(empty, false); show(moreWrap, false);
    if (!message) { show(err, false); err.textContent = ''; return; }
    setText(err, message);
    show(err, true);
  }

  function setStatus(text) {
    if (!text) { show(status, false); status.textContent = ''; return; }
    setText(status, text);
    show(status, true);
  }

  function render(data, append) {
    setError(null);
    total = data.total || 0;
    offset = (data.offset || 0) + (data.books ? data.books.length : 0);
    if (!append) hits.textContent = '';
    (data.books || []).forEach(function (b) { hits.appendChild(bookCard(b)); });
    if (total === 0) {
      setText(emptyTitle, 'No public shelves have that book');
      setText(emptyText, 'Try another title, author or ISBN. Private shelves stay out of search.');
      show(empty, true);
      setStatus('');
    } else {
      show(empty, false);
      var shown = hits.children.length;
      setStatus(shown + ' of ' + total + ' book' + (total === 1 ? '' : 's'));
    }
    show(moreWrap, offset < total);
  }

  function search(nextOffset, append) {
    var q = input.value.trim();
    if (!q) {
      hits.textContent = '';
      show(empty, false); show(moreWrap, false);
      setError(null);
      setStatus('Type a title, author or ISBN to search public shelves.');
      return;
    }
    if (pending && pending.abort) pending.abort();
    var ac = new AbortController();
    pending = ac;
    moreBtn.disabled = true;
    var url = '/api/search?q=' + encodeURIComponent(q) + '&limit=' + LIMIT + '&offset=' + (nextOffset || 0);
    fetch(url, { credentials: 'same-origin', headers: { Accept: 'application/json' }, signal: ac.signal })
      .then(function (r) {
        return r.text().then(function (t) {
          var data = null; try { data = t ? JSON.parse(t) : null; } catch (e) { /* not JSON */ }
          if (!r.ok) throw new Error((data && data.error) || ('HTTP ' + r.status));
          return data;
        });
      })
      .then(function (data) { render(data, append); })
      .catch(function (e) {
        if (e.name === 'AbortError') return;
        setError(e.message || 'Search failed. Please try again.');
      })
      .then(function () { moreBtn.disabled = false; });
  }

  form.addEventListener('submit', function (e) {
    e.preventDefault();
    var q = input.value.trim();
    var url = '/search.html' + (q ? '?q=' + encodeURIComponent(q) : '');
    if (history.replaceState) history.replaceState(null, '', url);
    search(0, false);
  });
  moreBtn.addEventListener('click', function () { search(offset, true); });

  var start = new URLSearchParams(location.search).get('q') || '';
  input.value = start;
  if (start.trim()) search(0, false);
  else setStatus('Type a title, author or ISBN to search public shelves.');
})();
