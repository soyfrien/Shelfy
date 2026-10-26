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
    li._parts = { t: t, a: a, title: b.title, author: a.textContent };
    return li;
  }

  // Put text into el, with the parts that match the search words wrapped in <mark>. Text nodes only, never innerHTML.
  function setMarked(el, text, toks) {
    el.textContent = '';
    var ranges = toks && toks.length ? window.ShelfySearch.highlightRanges(text, toks) : [], at = 0;
    ranges.forEach(function (r) {
      if (r[0] > at) el.appendChild(document.createTextNode(text.slice(at, r[0])));
      var m = document.createElement('mark'); m.textContent = text.slice(r[0], r[1]); el.appendChild(m);
      at = r[1];
    });
    if (at < text.length || !ranges.length) el.appendChild(document.createTextNode(text.slice(at)));
  }

  function renderBooks(ul, books) {
    ul.textContent = '';
    books.forEach(function (b) { ul.appendChild(bookCard(b)); });
  }

  // ---- search bar above a cover grid ----
  // createSearch({ ul, unmatchedUl, unmatchedWrap, unmatchedTitle }) puts a sticky bar before `ul` and returns
  //   setBooks(books)      show these books (existing cards are kept, so covers don't flash while photos are still being read)
  //   setUnmatched(items, opts)  spines that were not matched to a book (home page only); they are searched too.
  //                        Items are strings or { id, text, lowQuality }. opts.hidden = how many low-quality reads are not listed.
  //                        With o.onUnmatchedTap, an item with an id is tappable (tap to fix) and calls it with the item.
  //   reset()              clear the query
  function createSearch(o) {
    var S = window.ShelfySearch, ul = o.ul;
    var bar = document.createElement('div');
    bar.className = 'searchbar hidden';
    bar.setAttribute('role', 'search');
    var row = document.createElement('div'); row.className = 'sb-row';
    var input = document.createElement('input');
    input.type = 'search'; input.className = 'sb-input'; input.placeholder = 'Search title or author';
    input.setAttribute('aria-label', 'Search title or author');
    input.setAttribute('enterkeyhint', 'search');
    input.autocomplete = 'off'; input.spellcheck = false;
    input.setAttribute('autocapitalize', 'off'); input.setAttribute('autocorrect', 'off');
    var clear = document.createElement('button');
    clear.type = 'button'; clear.className = 'sb-clear hidden'; clear.setAttribute('aria-label', 'Clear search'); clear.textContent = '×';
    row.appendChild(input); row.appendChild(clear);
    var meta = document.createElement('div'); meta.className = 'sb-meta';
    var count = document.createElement('span'); count.className = 'sb-count'; count.setAttribute('aria-live', 'polite');
    var jump = document.createElement('button'); jump.type = 'button'; jump.className = 'sb-jump hidden';
    var sortLabel = document.createElement('label'); sortLabel.className = 'sb-sort';
    var sortWord = document.createElement('span'); sortWord.className = 'sb-sortword'; sortWord.textContent = 'Sort';
    sortLabel.appendChild(sortWord);
    var sort = document.createElement('select'); sort.setAttribute('aria-label', 'Sort books');
    [['title', 'Title A–Z'], ['author', 'Author A–Z']].forEach(function (p) {
      var op = document.createElement('option'); op.value = p[0]; op.textContent = p[1]; sort.appendChild(op);
    });
    sortLabel.appendChild(sort);
    meta.appendChild(count); meta.appendChild(jump); meta.appendChild(sortLabel);
    bar.appendChild(row); bar.appendChild(meta);
    var empty = document.createElement('div'); empty.className = 'sb-empty hidden'; empty.setAttribute('role', 'status');
    var emptyTitle = document.createElement('strong'); var emptyHint = document.createElement('span');
    empty.appendChild(emptyTitle); empty.appendChild(emptyHint);
    ul.parentNode.insertBefore(bar, ul);
    ul.parentNode.insertBefore(empty, ul);

    var items = [];          // { id, book, li }
    var unmatched = [];      // { text, li, item }
    var hiddenReads = 0;     // low-quality reads that are not listed (the list is kept visible for them)
    var toks = [], query = '';

    function sorted() {
      var order = S.sortBooks(items.map(function (i) { return i.book; }), sort.value);
      var byBook = new Map(items.map(function (i) { return [i.book, i]; }));
      return order.map(function (b) { return byBook.get(b); });
    }
    function apply() {
      toks = S.tokens(input.value); query = input.value.trim();
      var searching = toks.length > 0;
      var list = sorted(), shown = 0;
      list.forEach(function (it) { ul.appendChild(it.li); });
      list.forEach(function (it) {
        var ok = S.matches(S.bookText(it.book), toks);
        it.li.classList.toggle('hidden', !ok);
        if (ok) shown++;
        var p = it.li._parts;
        setMarked(p.t, p.title, ok ? toks : []);
        setMarked(p.a, p.author, ok ? toks : []);
      });
      var um = 0;
      unmatched.forEach(function (u) {
        var ok = S.matches(u.text, toks);
        u.li.classList.toggle('hidden', !ok);
        u.li.classList.toggle('first', ok && um === 0);   // no divider line above the first visible entry
        if (ok) { um++; setMarked(u.li, u.text, toks); } else if (u.marked) { u.li.textContent = u.text; }
        u.marked = ok && searching;
      });
      var total = items.length;
      count.textContent = searching ? shown + ' of ' + total + (total === 1 ? ' book' : ' books') : total + (total === 1 ? ' book' : ' books');
      clear.classList.toggle('hidden', !input.value);
      bar.classList.toggle('hidden', total === 0);
      ul.classList.toggle('hidden', searching && shown === 0);
      jump.classList.toggle('hidden', !(searching && um > 0 && o.unmatchedWrap));
      if (!jump.classList.contains('hidden')) jump.textContent = '+ ' + um + ' unmatched ↓';

      var showEmpty = searching && shown === 0 && total > 0;
      empty.classList.toggle('hidden', !showEmpty);
      if (showEmpty) {
        emptyTitle.textContent = 'No books match “' + query + '”.';
        emptyHint.textContent = !o.unmatchedWrap ? 'Try fewer words, or just the author’s last name.'
          : um > 0 ? um + (um === 1 ? ' spine' : ' spines') + ' in “Spines I couldn’t match” below mention it, so the book may be there.'
          : unmatched.length > 0 ? 'It isn’t in “Spines I couldn’t match” below either. Try fewer words, or add another photo of that part of the shelf.'
          : 'Check “Spines I couldn’t match” below if there is one: the book may not have been recognized. Try fewer words, or just the author’s last name.';
      }
      if (o.unmatchedWrap) {
        var any = unmatched.length > 0 || hiddenReads > 0;
        o.unmatchedWrap.classList.toggle('hidden', !any || (searching && um === 0));
        if (o.unmatchedTitle) o.unmatchedTitle.textContent = searching ? 'Spines I couldn’t match · ' + um + ' of ' + unmatched.length : 'Spines I couldn’t match';
      }
    }

    input.addEventListener('input', apply);
    input.addEventListener('keydown', function (e) {
      if (e.key === 'Enter') { e.preventDefault(); input.blur(); }          // closes the phone keyboard
      else if (e.key === 'Escape') { input.value = ''; apply(); }
    });
    clear.addEventListener('click', function () { input.value = ''; apply(); input.focus(); });
    sort.addEventListener('change', apply);
    jump.addEventListener('click', function () {
      input.blur();
      if (o.unmatchedWrap) o.unmatchedWrap.scrollIntoView({ behavior: 'smooth', block: 'start' });
    });

    return {
      input: input,
      setBooks: function (books) {
        var old = new Map(items.map(function (i) { return [i.id, i]; }));
        items = (books || []).map(function (b) {
          var id = b.bookId || (b.title + '|' + b.author), prev = old.get(id);
          if (prev) { prev.book = b; return prev; }
          return { id: id, book: b, li: bookCard(b) };
        });
        ul.textContent = '';
        apply();
      },
      setUnmatched: function (items, opts) {
        if (!o.unmatchedUl) return;
        o.unmatchedUl.textContent = '';
        hiddenReads = (opts && opts.hidden) || 0;
        unmatched = (items || []).map(function (t) {
          var item = typeof t === 'string' ? { text: t } : t;
          var li = document.createElement('li'); li.textContent = item.text; o.unmatchedUl.appendChild(li);
          if (item.lowQuality) li.classList.add('lowq');
          if (o.onUnmatchedTap && item.id) {
            li.classList.add('tap'); li.tabIndex = 0; li.setAttribute('role', 'button');
            li.setAttribute('aria-label', 'Fix: ' + item.text);
            li.addEventListener('click', function () { o.onUnmatchedTap(item, li); });
            li.addEventListener('keydown', function (e) { if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); o.onUnmatchedTap(item, li); } });
          }
          return { text: item.text, li: li, item: item, marked: false };
        });
        apply();
      },
      reset: function () { input.value = ''; apply(); }
    };
  }

  window.Shelfy = { bookCard: bookCard, renderBooks: renderBooks, isHttpUrl: isHttpUrl, createSearch: createSearch, setMarked: setMarked };
})();
