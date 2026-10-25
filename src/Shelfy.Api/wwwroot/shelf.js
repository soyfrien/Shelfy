(function () {
  var m = location.pathname.match(/\/s\/([0-9a-fA-F-]{36})/);
  var $ = function (id) { return document.getElementById(id); };
  if (!m) { $('loading').classList.add('hidden'); $('err').classList.remove('hidden'); return; }
  fetch('/api/shelves/' + m[1], { headers: { Accept: 'application/json' } })
    .then(function (r) { if (!r.ok) throw new Error(r.status); return r.json(); })
    .then(function (shelf) {
      $('loading').classList.add('hidden');
      var books = shelf.books || [];
      if (!books.length) { $('empty').classList.remove('hidden'); return; }
      window.Shelfy.renderBooks($('books'), books);
    })
    .catch(function () { $('loading').classList.add('hidden'); $('err').classList.remove('hidden'); });
})();
