// Shelfy home page: photo -> shelf -> upload -> poll the recognition job -> show the shelf.
// Plain JS, no build step. The API is open for now (no sign-in), so the browser keeps an anonymous guest id and the
// last shelf id in localStorage.
(function () {
  'use strict';
  var KEY_USER = 'shelfy.userId', KEY_SHELF = 'shelfy.shelfId';
  var MAX_UPLOAD = 20 * 1024 * 1024;
  var POLL_FAST_MS = 1500, POLL_SLOW_MS = 2500, POLL_ERR_MAX_MS = 8000;
  var POLL_LIMIT_BASE_MS = 3 * 60 * 1000, POLL_LIMIT_PER_PART_MS = 90 * 1000;   // overall patience: 3 min + 90 s per part

  var $ = function (id) { return document.getElementById(id); };
  var els = {
    pick: $('pick'), progress: $('progress'), preview: $('preview'), statusText: $('statusText'), spinner: $('spinner'),
    bar: $('bar'), barWrap: $('barWrap'), errorBox: $('errorBox'), errorTitle: $('errorTitle'), errorText: $('errorText'),
    result: $('result'), resultTitle: $('resultTitle'), resultNote: $('resultNote'), books: $('books'),
    unresolvedWrap: $('unresolvedWrap'), unresolved: $('unresolved'), shareLink: $('shareLink'), copyBtn: $('copyBtn'),
    file: $('file'), newShelf: $('newShelf'), addMore: $('addMore'), parts: $('parts'), partErrors: $('partErrors')
  };
  var busy = false;
  var run = null;   // the batch being processed: { shelfId, batchId, n, parts[], ... }

  // While photos are being read, the buttons that start another photo or a new shelf are switched off.
  function lockUi(on) {
    els.file.disabled = on; els.addMore.classList.toggle('disabled', on); els.newShelf.disabled = on;
  }
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
    lockUi(false);
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
  // Cuts the photo into one image (normal photos) or overlapping tiles (wide / large ones), see tiles.js.
  function encode(canvas) {
    return new Promise(function (resolve, reject) {
      canvas.toBlob(function (b) { b ? resolve(b) : reject(new Error('encode')); }, 'image/jpeg', 0.9);
    });
  }
  function prepare(file) {
    return decode(file).then(function (img) {
      var w = img.naturalWidth || img.width, h = img.naturalHeight || img.height;
      var plan = window.ShelfyTiles.planTiles(w, h);
      var out = [];
      // One tile at a time (encode, then free the canvas): keeps memory low on phones.
      return plan.tiles.reduce(function (chain, t) {
        return chain.then(function () {
          var canvas = document.createElement('canvas');
          canvas.width = t.outW; canvas.height = t.outH;
          canvas.getContext('2d').drawImage(img, t.x, t.y, t.w, t.h, 0, 0, t.outW, t.outH);
          return encode(canvas).then(function (blob) { canvas.width = canvas.height = 0; out.push(blob); });
        });
      }, Promise.resolve()).then(function () { if (img.close) img.close(); return out; });
    }).catch(function (e) {
      if (e && e.message === 'encode') throw e;
      // Could not decode in the browser: send the original if the server accepts that kind of file.
      if ((file.type === 'image/jpeg' || file.type === 'image/png') && file.size <= MAX_UPLOAD) return [file];
      throw new Error('unsupported');
    });
  }

  function uuid() {
    if (window.crypto && crypto.randomUUID) return crypto.randomUUID();
    var b = new Uint8Array(16);
    if (window.crypto && crypto.getRandomValues) crypto.getRandomValues(b); else for (var i = 0; i < 16; i++) b[i] = Math.random() * 256;
    b[6] = (b[6] & 15) | 64; b[8] = (b[8] & 63) | 128;
    var h = Array.prototype.map.call(b, function (x) { return ('0' + x.toString(16)).slice(-2); }).join('');
    return h.slice(0, 8) + '-' + h.slice(8, 12) + '-' + h.slice(12, 16) + '-' + h.slice(16, 20) + '-' + h.slice(20);
  }

  function upload(r, part) {
    return new Promise(function (resolve, reject) {
      var xhr = new XMLHttpRequest(), form = new FormData();
      form.append('batchId', r.batchId); form.append('part', String(part.index)); form.append('parts', String(r.n));
      form.append('file', part.blob, r.n > 1 ? 'shelf-part-' + part.index + '.jpg' : 'shelf.jpg');
      xhr.open('POST', '/api/shelves/' + r.shelfId + '/photos');
      xhr.setRequestHeader('Accept', 'application/json');
      xhr.upload.onprogress = function (e) {
        if (e.lengthComputable) { part.uploadPct = e.loaded / e.total * 100; renderProgress(); }
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

  // Uploads the parts that are not on the server yet, one after another. The worker reads them in the same order.
  function uploadAll(r) {
    if (r.uploading) return r.uploading;
    r.uploading = r.parts.reduce(function (chain, part) {
      return chain.then(function () {
        if (part.uploaded || r.aborted) return;
        part.uploadError = null; part.uploadPct = 0; part.uploadingNow = true; renderProgress();
        return upload(r, part).then(function (res) { part.uploaded = true; part.jobId = res.jobId; }, function (e) {
          if (e.status === 413 || e.status === 415 || e.status === 400 || e.status === 404) { e.fatal = true; }
          part.uploadError = e;
        }).then(function () { part.uploadingNow = false; renderProgress(); });
      });
    }, Promise.resolve()).then(function () { r.uploading = null; });
    return r.uploading;
  }

  // ---- progress ----
  function partState(r, part) {
    if (!part.uploaded) return part.uploadError ? 'error' : (part.uploadingNow ? 'uploading' : 'queued');
    var job = r.jobs && r.jobs[part.index];
    // After a retry the batch may still report the old failed job for this part until the new job shows up.
    if (job && part.jobId && job.jobId !== part.jobId) job = null;
    if (job && job.status === 'Completed') return 'done';
    if (job && job.status === 'Failed') return 'error';
    if (job && job.status === 'Processing') return 'reading';
    if (job && job.status === 'Pending') return job.error ? 'retry' : 'waiting';
    return 'waiting';
  }
  var CHIP_TEXT = { done: '✓', reading: '…', uploading: '↑', retry: '↻', waiting: '·', queued: '·', error: '!' };

  function renderProgress() {
    if (!run) return;
    var r = run, n = r.n, states = r.parts.map(function (p) { return partState(r, p); });
    var count = function (s) { return states.filter(function (x) { return x === s; }).length; };
    var done = count('done'), reading = states.indexOf('reading'), uploading = states.indexOf('uploading');
    var retry = states.indexOf('retry'), waiting = states.indexOf('waiting');
    var text;
    if (n === 1) {
      if (uploading === 0) text = 'Uploading photo… ' + Math.round(r.parts[0].uploadPct || 0) + '%';
      else if (reading === 0) text = 'Reading the spines and looking up the books… this can take up to a minute.';
      else if (retry === 0) text = 'Hit a snag, trying again…';
      else text = 'Waiting in line…';
    } else if (reading >= 0) text = 'Reading part ' + (reading + 1) + ' of ' + n + '…';
    else if (uploading >= 0) text = 'Uploading part ' + (uploading + 1) + ' of ' + n + '… ' + Math.round(r.parts[uploading].uploadPct || 0) + '%';
    else if (retry >= 0) text = 'Part ' + (retry + 1) + ' of ' + n + ' hit a snag, trying again…';
    else if (waiting >= 0) text = 'Part ' + (waiting + 1) + ' of ' + n + ' is waiting in line…';
    else text = 'Finishing up…';
    var active = states.some(function (s) { return s !== 'done' && s !== 'error'; });
    var pct = (done + (reading >= 0 ? 0.5 : 0) + (uploading >= 0 ? 0.1 : 0)) / n * 100;
    if (!active) text = done === n ? 'All ' + (n === 1 ? 'done' : n + ' parts read') : done + ' of ' + n + ' parts read';
    var bar = n === 1 ? (uploading === 0 ? r.parts[0].uploadPct || 0 : (active ? null : 100)) : pct;
    setStatus(text, bar, active);

    // One chip per part (only for tiled photos), plus an error line with a retry button for each part that failed.
    els.parts.textContent = '';
    show(els.parts, n > 1);
    if (n > 1) states.forEach(function (s, i) {
      var chip = document.createElement('span');
      chip.className = 'chip ' + s; chip.textContent = CHIP_TEXT[s] || '·'; chip.title = 'Part ' + (i + 1) + ': ' + s;
      els.parts.appendChild(chip);
    });
    els.partErrors.textContent = '';
    states.forEach(function (s, i) {
      if (s !== 'error') return;
      var part = r.parts[i], job = r.jobs && r.jobs[part.index];
      var row = document.createElement('div'); row.className = 'parterr';
      var msg = document.createElement('span');
      msg.textContent = (n > 1 ? 'Part ' + (i + 1) + ' of ' + n : 'The photo') + ' couldn\'t be read' +
        (part.uploadError ? ' (upload failed).' : (job && job.error ? ': ' + job.error : '.'));
      row.appendChild(msg);
      if (!r.polling && !(part.uploadError && part.uploadError.fatal)) {
        var btn = document.createElement('button');
        btn.type = 'button'; btn.className = 'btn secondary retry'; btn.textContent = 'Retry' + (n > 1 ? ' part ' + (i + 1) : '');
        btn.addEventListener('click', function () { retryPart(r, part); });
        row.appendChild(btn);
      }
      els.partErrors.appendChild(row);
    });
    show(els.partErrors, els.partErrors.childNodes.length > 0);
  }

  function retryPart(r, part) {
    if (r !== run || r.polling) return;
    part.uploaded = false; part.uploadError = null; part.jobId = null;
    busy = true; lockUi(true); show(els.pick, false);
    renderProgress();
    uploadAll(r);
    pollLoop(r);
  }

  // Polls the batch until every part is read (or failed), refreshing the shelf as parts complete.
  function pollLoop(r) {
    if (r.polling) return;
    r.polling = true;
    var started = Date.now(), delay = POLL_FAST_MS, errors = 0, lastDone = -1, lastBooks = -1, lastShown = null;
    var limit = POLL_LIMIT_BASE_MS + POLL_LIMIT_PER_PART_MS * r.n;
    function finish(timedOut) {
      r.polling = false;
      busy = false; lockUi(false);
      renderProgress();
      if (timedOut) {
        els.partErrors.textContent = '';
        var d = document.createElement('div'); d.className = 'parterr';
        d.textContent = 'This is taking longer than expected. Your shelf link keeps working: reload this page in a minute to see the books.';
        els.partErrors.appendChild(d); show(els.partErrors, true); setStatus('Still working…', null, false);
      }
      var failedParts = r.parts.some(function (p) { return partState(r, p) === 'error'; });
      // Keep the progress card for tiled photos (parts summary) and whenever something needs attention.
      show(els.progress, failedParts || timedOut || r.n > 1);
      show(els.pick, els.result.classList.contains('hidden'));   // the result has its own "add another photo" button
      window.scrollTo(0, 0);
    }
    function refreshShelf(batch) {
      return api('GET', '/api/shelves/' + r.shelfId).then(function (shelf) {
        if (r !== run) return;
        renderShelf(shelf, batch);
      }, function () { /* try again at the next change */ lastDone = -1; });
    }
    function tick() {
      if (r !== run || r.aborted) { r.polling = false; return; }
      var uploadsPending = r.parts.some(function (p) { return !p.uploaded && !p.uploadError; });
      // Nothing is on the server before the first upload has finished: don't ask yet (it would be a 404).
      if (uploadsPending && !r.parts.some(function (p) { return p.uploaded; })) { setTimeout(tick, 500); return; }
      api('GET', '/api/shelves/' + r.shelfId + '/batches/' + r.batchId).then(function (batch) {
        errors = 0;
        r.jobs = {};
        batch.jobs.forEach(function (j) { r.jobs[j.part] = j; });
        // A job we retried may still show the old failed one until the new upload is registered; the upload sets part.uploaded.
        renderProgress();
        var finished = batch.completed + batch.failed;
        var changed = finished !== lastDone || batch.books.length !== lastBooks;
        lastDone = finished; lastBooks = batch.books.length;
        var working = batch.pending + batch.processing > 0 || uploadsPending;
        var p = changed || !working ? refreshShelf(batch) : Promise.resolve();
        return p.then(function () {
          var states = r.parts.map(function (part) { return partState(r, part); });
          var allEnded = states.every(function (s) { return s === 'done' || s === 'error'; });
          if (allEnded && !uploadsPending) return finish(false);
          if (Date.now() - started > limit) return finish(true);
          delay = batch.processing > 0 || uploadsPending ? POLL_FAST_MS : POLL_SLOW_MS;
          setTimeout(tick, delay);
        });
      }, function (e) {
        if (e.status === 404) {   // nothing uploaded yet
          if (r.parts.every(function (p) { return p.uploadError; })) return finish(false);
          if (Date.now() - started > limit) return finish(true);
          return setTimeout(tick, POLL_FAST_MS);
        }
        errors++;
        delay = Math.min(POLL_ERR_MAX_MS, delay * 1.6);   // back off while the server or the connection is struggling
        if (Date.now() - started > limit) return finish(true);
        if (errors >= 3) setStatus('Connection trouble, still trying…', null);
        setTimeout(tick, delay);
      });
    }
    tick();
  }

  // ---- results ----
  function renderShelf(shelf, batch) {
    var books = shelf.books || [];
    els.resultTitle.textContent = books.length ? 'Your shelf · ' + books.length + (books.length === 1 ? ' book' : ' books') : 'Your shelf';
    window.Shelfy.renderBooks(els.books, books);
    var link = location.origin + '/s/' + shelf.id;
    els.shareLink.href = link; els.shareLink.textContent = link;

    var note = '', un = [];
    if (batch) {
      var spines = batch.spines || [];
      var matched = spines.filter(function (s) { return s.book; }).length;
      var ended = batch.completed + batch.failed >= batch.parts && batch.pending + batch.processing === 0;
      var what = batch.parts > 1 ? 'In this photo (' + batch.parts + ' parts) ' : 'In this photo ';
      if (!ended) note = 'So far ' + (matched ? 'I matched ' + matched + ' book' + (matched === 1 ? '' : 's') + '.' : 'nothing matched yet.') + ' More coming…';
      else if (!spines.length) note = 'I couldn\'t read any spine text in that photo. Try better light, move a bit closer, and keep the spines facing the camera.';
      else if (!matched) note = 'I found ' + spines.length + ' spine' + (spines.length === 1 ? '' : 's') + ' but couldn\'t match any to a book. Try a sharper, closer photo.';
      else note = what + 'I found ' + spines.length + ' spine' + (spines.length === 1 ? '' : 's') + ' and matched ' + matched + ' to books.';
      un = spines.filter(function (s) { return !s.book; });
    }
    els.resultNote.textContent = note;
    show(els.resultNote, !!note);

    els.unresolved.textContent = '';
    un.forEach(function (s) {
      var li = document.createElement('li');
      li.textContent = s.rawText || s.candidateTitle || '(unreadable)';
      els.unresolved.appendChild(li);
    });
    show(els.unresolvedWrap, un.length > 0);
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
    busy = true; lockUi(true);
    if (run) run.aborted = true;
    run = null;
    show(els.errorBox, false); show(els.result, false); show(els.pick, false);
    show(els.parts, false); show(els.partErrors, false);
    show(els.progress, true);
    show(els.preview, false);
    setStatus('Preparing your photo…', null);
    try {
      els.preview.onload = function () { show(els.preview, true); };
      els.preview.src = URL.createObjectURL(file);
    } catch (e) { /* preview is optional */ }

    var blobs;
    prepare(file).then(function (b) {
      blobs = b;
      setStatus('Setting up your shelf…', null);
      return ensureShelf();
    }).then(function (shelfId) {
      var r = run = {
        shelfId: shelfId, batchId: uuid(), n: blobs.length, jobs: {}, polling: false, uploading: null, aborted: false,
        parts: blobs.map(function (b, i) { return { index: i + 1, blob: b, uploaded: false, uploadPct: 0 }; })
      };
      renderProgress();
      uploadAll(r);
      pollLoop(r);
    }).catch(function (e) {
      var msg = e && e.message;
      if (msg === 'unsupported') return fail('I can\'t open that photo', 'Please choose a JPEG or PNG photo, or take a new one with the camera.');
      if (e.status === 429) return fail('Too many requests', 'Please wait a few seconds and try again.');
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
