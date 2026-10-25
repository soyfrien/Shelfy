// Checks for the shelf search logic: node tools/test-search.js
const S = require('../src/Shelfy.Api/wwwroot/search.js');
const assert = require('assert');

const books = [
  { title: 'The Hobbit', author: 'J.R.R. Tolkien' },
  { title: 'The Fellowship of the Ring', author: 'J. R. R. Tolkien' },
  { title: 'Dune', author: 'Frank Herbert' },
  { title: 'Foundation', author: 'Isaac Asimov' },
  { title: 'Les Misérables', author: 'Victor Hugo' },
  { title: "Ender's Game", author: 'Orson Scott Card' },
  { title: 'Ficciones', author: 'Jorge Luis Borges' },
  { title: 'Øen', author: 'Unknown' },
  { title: '1984', author: 'George Orwell' },
];
const titles = (q) => S.filterBooks(books, q).map((b) => b.title);

// normalize
assert.strictEqual(S.normalize('  Les   MISÉRABLES! '), 'les miserables');
assert.strictEqual(S.normalize("O’Brien's"), 'obriens');
assert.strictEqual(S.normalize(null), '');
assert.deepStrictEqual(S.tokens('Tolkien  hobbit tolkien'), ['tolkien', 'hobbit']);
assert.deepStrictEqual(S.tokens('   '), []);

// filter: case, accents, substring, any order, several words
assert.deepStrictEqual(titles(''), books.map((b) => b.title));
assert.deepStrictEqual(titles('hobb'), ['The Hobbit']);
assert.deepStrictEqual(titles('TOLKIEN'), ['The Hobbit', 'The Fellowship of the Ring']);
assert.deepStrictEqual(titles('miserables'), ['Les Misérables']);
assert.deepStrictEqual(titles('MISÉRABLES hugo'), ['Les Misérables']);
assert.deepStrictEqual(titles('hugo les'), ['Les Misérables']);          // any order
assert.deepStrictEqual(titles('tolkien ring'), ['The Fellowship of the Ring']);
assert.deepStrictEqual(titles('enders'), ["Ender's Game"]);              // apostrophe ignored
assert.deepStrictEqual(titles("ender's"), ["Ender's Game"]);
assert.deepStrictEqual(titles('jose'), []);
assert.deepStrictEqual(titles('borges ficciones'), ['Ficciones']);
assert.deepStrictEqual(titles('oen'), ['Øen']);
assert.deepStrictEqual(titles('1984'), ['1984']);
assert.deepStrictEqual(titles('zzz'), []);
assert.deepStrictEqual(titles('unknown'), []);                           // the "Unknown" author placeholder is not searchable
assert.deepStrictEqual(titles('j.r.r. tolkien'), ['The Hobbit', 'The Fellowship of the Ring']);

// matches() on raw spine text
assert.ok(S.matches('DUNE FRANK HERBERT', S.tokens('herbert dune')));
assert.ok(!S.matches('DUNE', S.tokens('herbert')));
assert.ok(S.matches('anything', []));

// highlight ranges are in the ORIGINAL text, accents and apostrophes included
const hl = (text, q) => S.highlightRanges(text, S.tokens(q)).map(([a, b]) => text.slice(a, b));
assert.deepStrictEqual(hl('Les Misérables', 'miserables'), ['Misérables']);
assert.deepStrictEqual(hl("Ender's Game", 'enders game'), ["Ender's", 'Game']);
assert.deepStrictEqual(hl('The Hobbit', 'the hob'), ['The', 'Hob']);
assert.deepStrictEqual(hl('Tolkien Tolkien', 'tolkien'), ['Tolkien', 'Tolkien']);
assert.deepStrictEqual(hl('Dune', 'du un'), ['Dun']);                    // overlapping ranges merge
assert.deepStrictEqual(hl('Dune', ''), []);

// sorting
const t = S.sortBooks(books, 'title').map((b) => b.title);
assert.deepStrictEqual(t.slice(0, 3), ['1984', 'Dune', "Ender's Game"]);
assert.strictEqual(t.indexOf('The Hobbit') < t.indexOf('Les Misérables'), true);   // "The" is ignored: Hobbit sorts under H
const a = S.sortBooks(books, 'author').map((b) => b.author);
assert.strictEqual(a[0], 'Isaac Asimov');                                  // by surname
assert.strictEqual(a[a.length - 1], 'Unknown');                            // unknown last
assert.deepStrictEqual(S.sortBooks(books, 'order'), books);
assert.notStrictEqual(S.sortBooks(books, 'title'), books);                 // never mutates the input

console.log('search tests ok');
