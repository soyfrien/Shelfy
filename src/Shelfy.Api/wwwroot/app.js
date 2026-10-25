// Shelfy home page: photo -> shelf -> upload -> poll the recognition job -> show the shelf.
// Plain JS, no build step. The API is open for now (no sign-in), so the browser keeps an anonymous guest id and the
// last shelf id in localStorage.
(function () {
  'use strict';
  var KEY_USER = 'shelfy.userId', KEY_SHELF = 'shelfy.shelfId';
  var MAX_EDGE = 3200;            // longest side after downscaling; keeps spine text readable, uploads small
  var MAX_UPLOAD = 20 * 1024 * 1024;
  var POLL_MS = 2000, POLL_LIMIT_MS = 5 * 60 * 1000;

  var $ = function (id) { return document.getElementById(id); };
  var els = {
    pick: $('pick'), progress: $('progress'), preview: $('preview'), statusText: $('statusText'), spinner: $('spinner'),
    bar: $('bar'), barWrap: $('barWrap'), errorBox: $('errorBox'), errorTitle: $('errorTitle'), errorText: $('errorText'),
    result: $('result'), resultTitle: $('resultTitle'), resultNote: $('resultNote'), books: $('books'),
    unresolvedWrap: $('unresolvedWrap'), unresolved: $('unresolved'), shareLink: $('shareLink'), copyBtn: $('copyBtn'),
    file: $('file'), newShelf: $('newShelf')
  };
  var busy = false;

  function show(el, on) { el.classList.toggle('hidden', !on); }
  function ls(k, v) {
    try { if (v === undefined) return localStorage.getItem(k); if (v === null) localStorage.removeItem(k); else localStorage.setItem(k, v); }
    catch (e) { return null; }
  }
  function setStatus(text, pct, spin) {
    els.statusText.textContent = text;
    show(els.barWrap, pct != null);
    if (pct != null) els.bar.style.width = Math.max(0, Math.min(100, pct)) + '%';
    show(els.spinner, spin !== false);
  }
  function fail(title, text) {
    busy = false;
    els.file.disabled = false;
    show(els.progress, false);
    show(els.pick, true);
    els.errorTitle.textContent = title;
    els.errorText.textContent = text;
    show(els.errorBox, true);
  }

  // ---- API ----
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
  function createUser() {
    return api('POST', '/api/guests').then(function (u) { ls(KEY_USER, u.id); return u.id; });
  }
  function createShelf() {
    function make(userId) { return api('POST', '/api/shelves', { userId: userId }); }
    var uid = ls(KEY_USER);
    var p = uid ? make(uid).catch(function (e) { if (e.status === 404) return createUser().then(make); throw e; }) : createUser().then(make);
    return p.then(function (s) { ls(KEY_SHELF, s.id); return s.id; });
  }
  // Reuse the last shelf if it still exists, otherwise make a new one.
  function ensureShelf() {
    var id = ls(KEY_SHELF);
    if (!id) return createShelf();
    return api('GET', '/api/shelves/' + id).then(function () { return id; }, function (e) {
      if (e.status === 404) return createShelf();
      throw e;
    });
  }

  // ---- photo preparation: decode (handles HEIC on iOS/Safari), fix rotation, downscale, re-encode as JPEG ----
  function decode(file) {
    if (window.createImageBitmap) {
      return createImageBitmap(file, { imageOrientation: 'from-image' }).catch(function () { return viaImg(file); });
    }
    return viaImg(file);
  }
  function viaImg(file) {
    return new Promise(function (resolve, reject) {
      var url = URL.createObjectURL(file), img = new Image();
      img.onload = function () { URL.revokeObjectURL(url); resolve(img); };
      img.onerror = function () { URL.revokeObjectURL(url); reject(new Error('decode')); };
      img.src = url;
    });
  }
  function prepare(file) {
    return decode(file).then(function (img) {
      var w = img.naturalWidth || img.width, h = img.naturalHeight || img.height;
      var scale = Math.min(1, MAX_EDGE / Math.max(w, h));
      var cw = Math.round(w * scale), ch = Math.round(h * scale);
      var canvas = document.createElement('canvas');
      canvas.width = cw; canvas.height = ch;
      canvas.getContext('2d').drawImage(img, 0, 0, cw, ch);
      if (img.close) img.close();
      return new Promise(function (resolve, reject) {
        canvas.toBlob(function (b) { b ? resolve(b) : reject(new Error('encode')); }, 'image/jpeg', 0.92);
      });
    }).catch(function () {
      // Could not decode in the browser: send the original if the server accepts that kind of file.
      if ((file.type === 'image/jpeg' || file.type === 'image/png') && file.size <= MAX_UPLOAD) return file;
      throw new Error('unsupported');
    });
  }

  function upload(shelfId, blob, name) {
    return new Promise(function (resolve, reject) {
      var xhr = new XMLHttpRequest(), form = new FormData();
      form.append('file', blob, name);
      xhr.open('POST', '/api/shelves/' + shelfId + '/photos');
      xhr.setRequestHeader('Accept', 'application/json');
      xhr.upload.onprogress = function (e) {
        if (e.lengthComputable) setStatus('Uploading photo… ' + Math.round(e.loaded / e.total * 100) + '%', e.loaded / e.total * 100);
      };
      xhr.onload = function () {
        var data = null; try { data = JSON.parse(xhr.responseText); } catch (e) { /* ignore */ }
        if (xhr.status === 202 || xhr.status === 200) return resolve(data);
        var err = new Error((data && data.error) || ('HTTP ' + xhr.status)); err.status = xhr.status; reject(err);
      };
      xhr.onerror = function () { var e = new Error('network'); e.status = 0; reject(e); };
      xhr.ontimeout = xhr.onerror; xhr.timeout = 3 * 60 * 1000;
      xhr.send(form);
    });
  }

  function poll(jobId, started) {
    return api('GET', '/api/recognition-jobs/' + jobId).then(function (job) {
      if (job.status === 'Completed' || job.status === 'Failed') return job;
      if (Date.now() - started > POLL_LIMIT_MS) { var e = new Error('timeout'); e.status = -1; throw e; }
      if (job.status === 'Pending' && job.error) setStatus('Hit a snag, trying again…', null);
      else if (job.status === 'Pending') setStatus('Waiting in line…', null);
      else setStatus('Reading the spines and looking up the books… this can take up to a minute.', null);
      return new Promise(function (r) { setTimeout(r, POLL_MS); }).then(function () { return poll(jobId, started); });
    });
  }

  // ---- results ----
  function renderShelf(shelf, job) {
    var books = shelf.books || [];
    els.resultTitle.textContent = books.length ? 'Your shelf · ' + books.length + (books.length === 1 ? ' book' : ' books') : 'Your shelf';
    window.Shelfy.renderBooks(els.books, books);
    var link = location.origin + '/s/' + shelf.id;
    els.shareLink.href = link; els.shareLink.textContent = link;

    var note = '';
    if (job) {
      var spines = job.spines || [];
      var matched = spines.filter(function (s) { return s.book; }).length;
      if (!spines.length) note = 'I couldn\'t read any spine text in that photo. Try better light, move a bit closer, and keep the spines facing the camera.';
      else if (!matched) note = 'I found ' + spines.length + ' spine' + (spines.length === 1 ? '' : 's') + ' but couldn\'t match any to a book. Try a sharper, closer photo.';
      else note = 'In this photo I found ' + spines.length + ' spine' + (spines.length === 1 ? '' : 's') + ' and matched ' + matched + ' to books.';
    }
    els.resultNote.textContent = note;
    show(els.resultNote, !!note);

    els.unresolved.textContent = '';
    var un = job ? (job.spines || []).filter(function (s) { return !s.book; }) : [];
    un.forEach(function (s) {
      var li = document.createElement('li');
      li.textContent = s.rawText || s.candidateTitle || '(unreadable)';
      els.unresolved.appendChild(li);
    });
    show(els.unresolvedWrap, un.length > 0);
    show(els.pick, false);
    show(els.result, true);
  }

  function loadExisting() {
    var id = ls(KEY_SHELF);
    if (!id) return;
    api('GET', '/api/shelves/' + id).then(function (shelf) {
      if (shelf.books && shelf.books.length) renderShelf(shelf, null);
    }, function () { /* shelf is gone or the server is unreachable: show the empty start screen */ });
  }

  // ---- main flow ----
  function onFile() {
    var file = els.file.files && els.file.files[0];
    els.file.value = '';      // allow choosing the same photo again
    if (!file || busy) return;
    busy = true; els.file.disabled = true;
    show(els.errorBox, false); show(els.result, false); show(els.pick, false);
    show(els.progress, true);
    show(els.preview, false);
    setStatus('Preparing your photo…', null);
    try {
      els.preview.onload = function () { show(els.preview, true); };
      els.preview.src = URL.createObjectURL(file);
    } catch (e) { /* preview is optional */ }

    var shelfId;
    prepare(file).then(function (blob) {
      setStatus('Setting up your shelf…', null);
      return ensureShelf().then(function (id) {
        shelfId = id;
        setStatus('Uploading photo…', 0);
        return upload(id, blob, 'shelf.jpg');
      });
    }).then(function (res) {
      setStatus('Reading the spines and looking up the books…', null);
      return poll(res.jobId, Date.now());
    }).then(function (job) {
      if (job.status === 'Failed') {
        var e = new Error(job.error || 'The photo could not be processed.'); e.status = -2; throw e;
      }
      return api('GET', '/api/shelves/' + shelfId).then(function (shelf) {
        busy = false; els.file.disabled = false;
        show(els.progress, false); show(els.pick, true);
        renderShelf(shelf, job);
        window.scrollTo(0, 0);
      });
    }).catch(function (e) {
      var msg = e && e.message;
      if (msg === 'unsupported') return fail('I can\'t open that photo', 'Please choose a JPEG or PNG photo, or take a new one with the camera.');
      if (e.status === 413) return fail('That photo is too large', 'The limit is 20 MB. Take the photo again or choose a smaller one.');
      if (e.status === 415 || e.status === 400) return fail('That doesn\'t look like a photo I can read', 'Please use a JPEG or PNG image. ' + (msg || ''));
      if (e.status === 429) return fail('Too many requests', 'Please wait a few seconds and try again.');
      if (e.status === -1) return fail('This is taking longer than expected', 'The photo is still being processed. Your shelf link keeps working: reload this page in a minute to see the books.');
      if (e.status === -2) return fail('I couldn\'t read that photo', msg);
      if (e.status === 0) return fail('No connection', 'Check your internet connection and try again.');
      return fail('Something went wrong', 'Please try again in a moment.' + (msg ? ' (' + msg + ')' : ''));
    });
  }

  els.file.addEventListener('change', onFile);
  els.newShelf.addEventListener('click', function () {
    if (!confirm('Start a new, empty shelf? Your old shelf keeps its link.')) return;
    ls(KEY_SHELF, null);
    show(els.result, false);
    show(els.pick, true);
    window.scrollTo(0, 0);
  });
  els.copyBtn.addEventListener('click', function () {
    var link = els.shareLink.href;
    (navigator.clipboard ? navigator.clipboard.writeText(link) : Promise.reject()).then(function () {
      els.copyBtn.textContent = 'Copied ✓';
    }, function () {
      window.prompt('Copy this link:', link);
    }).then(function () { setTimeout(function () { els.copyBtn.textContent = 'Copy link'; }, 2000); });
  });

  loadExisting();
})();
