// Small helpers shared by the home page and the shelf page. Everything from the API is inserted with textContent
// (never innerHTML), so book data can't inject markup.
(function () {
  function isHttpUrl(u) {
    try { var p = new URL(u); return p.protocol === 'https:' || p.protocol === 'http:'; } catch (e) { return false; }
  }

  // One book card: cover (or a title placeholder if there is no cover or it fails to load), title, author.
  function bookCard(b) {
    var li = document.createElement('li');
    li.className = 'book';
    var cover = document.createElement('div');
    cover.className = 'cover';
    // Title text shows first and stays when there is no cover; the cover fades in over it once it has loaded.
    var ph = document.createElement('div');
    ph.className = 'ph';
    ph.textContent = b.title;
    cover.appendChild(ph);
    if (b.coverUrl && isHttpUrl(b.coverUrl)) {
      var img = document.createElement('img');
      img.alt = 'Cover of ' + b.title;
      img.loading = 'lazy';
      img.referrerPolicy = 'no-referrer';
      img.onload = function () { if (img.naturalWidth >= 20) img.classList.add('loaded'); }; // OpenLibrary serves a 1px image for unknown covers
      img.onerror = function () { img.remove(); };
      img.src = b.coverUrl;
      cover.appendChild(img);
    }
    var meta = document.createElement('div');
    meta.className = 'meta';
    var t = document.createElement('div'); t.className = 't'; t.textContent = b.title;
    var a = document.createElement('div'); a.className = 'a'; a.textContent = b.author && b.author !== 'Unknown' ? b.author : '';
    meta.appendChild(t); meta.appendChild(a);
    li.appendChild(cover); li.appendChild(meta);
    return li;
  }

  function renderBooks(ul, books) {
    ul.textContent = '';
    books.forEach(function (b) { ul.appendChild(bookCard(b)); });
  }

  window.Shelfy = { bookCard: bookCard, renderBooks: renderBooks, isHttpUrl: isHttpUrl };
})();
