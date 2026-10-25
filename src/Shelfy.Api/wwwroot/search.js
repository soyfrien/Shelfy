// Search helpers for the shelf views. Pure string logic (no DOM), so it can be unit-tested with node:
//   node tools/test-search.js
// A query is split into words; a book matches when EVERY word is found somewhere in "title author", in any order.
// Matching ignores case, accents (é = e), punctuation and apostrophes (O'Brien = obrien = o brien).
(function (root) {
  'use strict';
  var EXTRA = { 'ø': 'o', 'ł': 'l', 'đ': 'd', 'æ': 'ae', 'œ': 'oe', 'ß': 'ss', 'ð': 'd', 'þ': 'th', 'ı': 'i' };
  var APOSTROPHES = /['’‘`´ʼ]/;
  var MARKS = /[̀-ͯ]/g;
  var ALNUM = /[\p{L}\p{N}]/u;

  // Normalised text plus, for every output char, the [start, end) span of the original text it came from.
  function fold(text) {
    text = text == null ? '' : String(text);
    var out = '', starts = [], ends = [], i = 0;
    for (var ch of text) {
      var from = i, to = i + ch.length;
      i = to;
      if (APOSTROPHES.test(ch)) continue;          // dropped, so "don't" matches "dont"
      var n = ch.normalize('NFD').replace(MARKS, '').toLowerCase();
      for (var c of n) {
        c = EXTRA[c] || c;
        for (var k = 0; k < c.length; k++) {
          var cc = c.charAt(k);
          out += ALNUM.test(cc) ? cc : ' ';
          starts.push(from); ends.push(to);
        }
      }
    }
    return { s: out, starts: starts, ends: ends };
  }

  function normalize(text) { return fold(text).s.replace(/\s+/g, ' ').trim(); }

  function tokens(query) {
    var n = normalize(query);
    if (!n) return [];
    var seen = {}, out = [];
    n.split(' ').forEach(function (t) { if (!seen[t]) { seen[t] = 1; out.push(t); } });
    return out;
  }

  // True when every token is a substring of the (folded) text. No tokens matches everything.
  function matches(text, toks) {
    if (!toks.length) return true;
    var s = fold(text).s;
    for (var i = 0; i < toks.length; i++) if (s.indexOf(toks[i]) < 0) return false;
    return true;
  }

  // [start, end) ranges of the ORIGINAL text to highlight, merged and sorted.
  function highlightRanges(text, toks) {
    if (!toks.length) return [];
    var f = fold(text), ranges = [];
    toks.forEach(function (t) {
      var at = f.s.indexOf(t);
      while (at >= 0) {
        ranges.push([f.starts[at], f.ends[at + t.length - 1]]);
        at = f.s.indexOf(t, at + t.length);
      }
    });
    ranges.sort(function (a, b) { return a[0] - b[0] || a[1] - b[1]; });
    var merged = [];
    ranges.forEach(function (r) {
      var last = merged[merged.length - 1];
      if (last && r[0] <= last[1]) last[1] = Math.max(last[1], r[1]); else merged.push([r[0], r[1]]);
    });
    return merged;
  }

  function titleKey(b) { return normalize(String(b.title || '').replace(/^\s*(the|a|an)\s+/i, '')); }
  // "J.R.R. Tolkien" sorts under T; "Tolkien, J.R.R." too. Unknown authors go last.
  function authorKey(b) {
    var a = String(b.author || '').trim();
    if (!a || a === 'Unknown') return '\uffff';
    var first = a.split(/\s*(?:;|&|\band\b)\s*/i)[0];
    if (first.indexOf(',') < 0) { var w = first.split(/\s+/); if (w.length > 1) first = w[w.length - 1] + ' ' + w.slice(0, -1).join(' '); }
    return normalize(first);
  }
  function compareBy(keyFn) {
    return function (a, b) { var x = keyFn(a), y = keyFn(b); return x < y ? -1 : x > y ? 1 : titleKey(a) < titleKey(b) ? -1 : titleKey(a) > titleKey(b) ? 1 : 0; };
  }
  function sortBooks(books, mode) {
    var copy = books.slice();
    if (mode === 'author') return copy.sort(compareBy(authorKey));
    if (mode === 'title') return copy.sort(compareBy(titleKey));
    return copy;                                    // 'order': as the server sent them
  }

  function bookText(b) { return (b.title || '') + ' ' + (b.author && b.author !== 'Unknown' ? b.author : ''); }
  function filterBooks(books, query) {
    var toks = tokens(query);
    return books.filter(function (b) { return matches(bookText(b), toks); });
  }

  var api = {
    normalize: normalize, tokens: tokens, matches: matches, highlightRanges: highlightRanges,
    bookText: bookText, filterBooks: filterBooks, sortBooks: sortBooks
  };
  if (typeof module !== 'undefined' && module.exports) module.exports = api; else root.ShelfySearch = api;
})(typeof window !== 'undefined' ? window : this);
