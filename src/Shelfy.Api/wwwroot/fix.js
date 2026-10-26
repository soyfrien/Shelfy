// Tap to fix (ADR-011): a bottom sheet for one unmatched spine. It shows what was read, a few suggested books, a search
// box, and "Not a book". Picking a book adds it to the shelf; dismissing hides the spine from the list.
// Everything from the API goes in with textContent, never innerHTML.
(function () {
  'use strict';
  var $ = function (id) { return document.getElementById(id); };
  var modal = $('fixModal');
  if (!modal) return;
  var els = {
    raw: $('fixRaw'), status: $('fixStatus'), list: $('fixList'), form: $('fixSearchForm'), query: $('fixQuery'),
    error: $('fixError'), dismiss: $('fixDismiss'), cancel: $('fixCancel'), toast: $('toast')
  };
  var cur = null;          // { shelfId, item, callbacks, seq, busy, focus }
  var toastTimer = null, dismissTimer = null;

  function show(el, on) { el.classList.toggle('hidden', !on); }
  function api(method, url, body) {
    return fetch(url, {
      method: method,
      headers: body ? { 'Content-Type': 'application/json', Accept: 'application/json' } : { Accept: 'application/json' },
      body: body ? JSON.stringify(body) : undefined
    }).then(function (r) {
      return r.text().then(function (t) {
        var data = null; try { data = t ? JSON.parse(t) : null; } catch (e) { /* not JSON */ }
        if (!r.ok) { var err = new Error((data && data.error) || ('HTTP ' + r.status)); err.status = r.status; throw err; }
        return data;
      });
    });
  }

  function toast(text) {
    els.toast.textContent = text; show(els.toast, true);
    clearTimeout(toastTimer);
    toastTimer = setTimeout(function () { show(els.toast, false); }, 4500);
  }

  function setBusy(on) {
    if (!cur) return;
    cur.busy = on;
    modal.classList.toggle('busy', on);
    Array.prototype.forEach.call(modal.querySelectorAll('button'), function (b) { if (b !== els.cancel) b.disabled = on; });
    els.query.disabled = on;
  }

  function resetDismiss() {
    clearTimeout(dismissTimer);
    els.dismiss.textContent = 'Not a book / dismiss';
    els.dismiss.classList.remove('armed');
  }

  function showError(text) { els.error.textContent = text; show(els.error, !!text); }

  function cover(b) {
    var d = document.createElement('div'); d.className = 'sg-cover';
    var ph = document.createElement('span'); ph.textContent = (b.title || '?').charAt(0); d.appendChild(ph);
    if (b.coverUrl && window.Shelfy.isHttpUrl(b.coverUrl)) {
      var img = document.createElement('img');
      img.alt = ''; img.loading = 'lazy'; img.referrerPolicy = 'no-referrer';
      img.onload = function () { if (img.naturalWidth >= 20) img.classList.add('loaded'); };
      img.onerror = function () { img.remove(); };
      img.src = b.coverUrl;
      d.appendChild(img);
    }
    return d;
  }

  function render(list, emptyText) {
    els.list.textContent = '';
    list.forEach(function (b) {
      var li = document.createElement('li');
      var btn = document.createElement('button');
      btn.type = 'button'; btn.className = 'sg';
      btn.setAttribute('data-isbn', b.isbn13);
      var meta = document.createElement('span'); meta.className = 'sg-meta';
      var t = document.createElement('span'); t.className = 'sg-title'; t.textContent = b.title;
      var a = document.createElement('span'); a.className = 'sg-author'; a.textContent = b.author && b.author !== 'Unknown' ? b.author : '';
      meta.appendChild(t); meta.appendChild(a);
      var add = document.createElement('span'); add.className = 'sg-add'; add.textContent = 'Add';
      btn.appendChild(cover(b)); btn.appendChild(meta); btn.appendChild(add);
      btn.addEventListener('click', function () { pick(b); });
      li.appendChild(btn); els.list.appendChild(li);
    });
    els.status.textContent = list.length ? '' : emptyText;
    show(els.status, !list.length);
  }

  function load(q) {
    var c = cur; if (!c) return;
    var seq = ++c.seq;
    showError('');
    els.list.textContent = '';
    els.status.textContent = q ? 'Searching…' : 'Looking for matches…';
    show(els.status, true);
    var url = '/api/shelves/' + c.shelfId + '/spines/' + c.item.id + '/suggestions' + (q ? '?q=' + encodeURIComponent(q) : '');
    api('GET', url).then(function (r) {
      if (cur !== c || seq !== c.seq) return;
      render(r.suggestions || [], r.unavailable
        ? 'The book catalogs aren’t answering right now. Try the search box again in a moment.'
        : q ? 'Nothing found for “' + q + '”. Try just the title, or the author’s last name, or an ISBN.'
            : 'No good guesses. Try the search box with the title, the author, or an ISBN.');
    }, function (e) {
      if (cur !== c || seq !== c.seq) return;
      if (e.status === 404) { close(); toast('That spine is gone. The list was refreshed.'); c.callbacks.onChanged(); return; }
      els.list.textContent = '';
      els.status.textContent = 'Couldn’t look that up. Check your connection and try again.';
    });
  }

  function pick(b) {
    var c = cur; if (!c || c.busy) return;
    setBusy(true); showError('');
    api('POST', '/api/shelves/' + c.shelfId + '/spines/' + c.item.id + '/resolve',
      { isbn13: b.isbn13, bookId: b.bookId || null, title: b.title, author: b.author, coverUrl: b.coverUrl || null }).then(function (r) {
      setBusy(false);
      close(true);
      toast((r.addedToShelf ? 'Added “' : '“') + r.book.title + (r.addedToShelf ? '” to your shelf.' : '” was already on your shelf.'));
      c.callbacks.onChanged(r);
    }, function (e) {
      setBusy(false);
      if (e.status === 409) { close(true); toast('That spine was already matched. The list was refreshed.'); c.callbacks.onChanged(); return; }
      showError(e.status === 404 ? 'That spine is gone.' : e.status === 0 || e instanceof TypeError
        ? 'No connection. Nothing was changed, please try again.' : 'Couldn’t add that book. Nothing was changed, please try again.');
    });
  }

  function dismiss() {
    var c = cur; if (!c || c.busy) return;
    if (!els.dismiss.classList.contains('armed')) {
      // Two taps, so one stray tap does not hide a spine.
      els.dismiss.classList.add('armed'); els.dismiss.textContent = 'Tap again to dismiss';
      clearTimeout(dismissTimer); dismissTimer = setTimeout(resetDismiss, 4000);
      return;
    }
    resetDismiss(); setBusy(true); showError('');
    api('POST', '/api/shelves/' + c.shelfId + '/spines/' + c.item.id + '/dismiss').then(function () {
      setBusy(false); close(true); toast('Dismissed. It won’t be listed again.'); c.callbacks.onChanged();
    }, function (e) {
      setBusy(false);
      if (e.status === 409 || e.status === 404) { close(true); toast('The list was refreshed.'); c.callbacks.onChanged(); return; }
      showError('Couldn’t dismiss it. Nothing was changed, please try again.');
    });
  }

  function close(force) {
    if (!cur || (cur.busy && !force)) return;
    var c = cur; cur = null;
    resetDismiss();
    show(modal, false);
    document.body.classList.remove('sheet-open');
    if (c.focus && c.focus.focus) { try { c.focus.focus(); } catch (e) { /* element is gone */ } }
  }

  // open(shelfId, item, { onChanged(result?) }, returnFocusTo)
  function open(shelfId, item, callbacks, focus) {
    if (cur) return;
    cur = { shelfId: shelfId, item: item, callbacks: callbacks, seq: 0, busy: false, focus: focus || document.activeElement };
    els.raw.textContent = item.text;
    els.query.value = '';
    showError(''); resetDismiss(); setBusy(false);
    show(modal, true);
    document.body.classList.add('sheet-open');
    modal.querySelector('.sheet').scrollTop = 0;
    load('');
  }

  els.form.addEventListener('submit', function (e) {
    e.preventDefault();
    var q = els.query.value.trim();
    els.query.blur();                      // closes the phone keyboard
    load(q);
  });
  els.dismiss.addEventListener('click', dismiss);
  els.cancel.addEventListener('click', function () { close(); });
  modal.addEventListener('click', function (e) { if (e.target === modal) close(); });
  document.addEventListener('keydown', function (e) { if (e.key === 'Escape' && cur) close(); });

  window.ShelfyFix = { open: open, close: close, toast: toast, isOpen: function () { return !!cur; } };
})();
