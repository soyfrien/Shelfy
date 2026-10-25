// Headless mobile-Chrome check of the shelf search on "/" and "/s/{id}". It only READS the shelf it is pointed at.
//   node tools/ui-check-search.js <baseUrl> <shelfId> [screenshotDir]
// Needs puppeteer-core and a Chrome (CHROME=/usr/bin/google-chrome).
const puppeteer = require('puppeteer-core');
const assert = require('assert');
const [base, shelfId, shotDir = '/tmp'] = process.argv.slice(2);
if (!base || !shelfId) { console.error('usage: node tools/ui-check-search.js <baseUrl> <shelfId> [dir]'); process.exit(2); }

const fold = (s) => s.normalize('NFD').replace(/[\u0300-\u036f]/g, '').toLowerCase();
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

(async () => {
  const browser = await puppeteer.launch({ executablePath: process.env.CHROME || '/usr/bin/google-chrome', headless: 'new', args: ['--no-sandbox'] });
  const shelf = await (await fetch(`${base}/api/shelves/${shelfId}`)).json();
  const unmatched = (await (await fetch(`${base}/api/shelves/${shelfId}/unmatched`)).json()).spines.map((s) => s.text);
  const books = shelf.books;
  console.log(`shelf has ${books.length} books, ${unmatched.length} unmatched spines`);
  assert.ok(books.length >= 1);
  const pick = books[Math.min(1, books.length - 1)];
  const word = fold(pick.title).split(/[^a-z0-9]+/).filter((w) => w.length >= 4).sort((a, b) => b.length - a.length)[0] || fold(pick.title).slice(0, 3);
  const expected = books.filter((b) => fold(b.title + ' ' + b.author).includes(word)).length;

  async function openPage(url, preload) {
    const page = await browser.newPage();
    await page.setViewport({ width: 390, height: 844, deviceScaleFactor: 2, isMobile: true, hasTouch: true });
    await page.setUserAgent('Mozilla/5.0 (iPhone; CPU iPhone OS 17_0 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.0 Mobile/15E148 Safari/604.1');
    const errors = [];
    page.on('pageerror', (e) => errors.push(String(e)));
    page.on('console', (m) => { if (m.type() === 'error' && !/Failed to load resource/.test(m.text())) errors.push(m.text()); });
    if (preload) await page.evaluateOnNewDocument(preload);
    await page.goto(base + url, { waitUntil: 'networkidle2' });
    await page.waitForSelector('.searchbar:not(.hidden)', { timeout: 15000 });
    return { page, errors };
  }
  const visibleCards = (page) => page.$$eval('#books .book', (l) => l.filter((x) => x.offsetParent !== null).length);
  const count = (page) => page.$eval('.sb-count', (e) => e.textContent);
  const type = async (page, text) => { await page.$eval('.sb-input', (e) => { e.value = ''; }); await page.focus('.sb-input'); await page.keyboard.type(text); };

  async function check(name, url, preload, home) {
    console.log(`\n== ${name} ${url}`);
    const { page, errors } = await openPage(url, preload);
    const fs = await page.$eval('.sb-input', (e) => parseFloat(getComputedStyle(e).fontSize));
    assert.ok(fs >= 16, 'input font-size ' + fs);
    assert.strictEqual(await page.$eval('.sb-input', (e) => e.placeholder), 'Search title or author');
    assert.strictEqual(await visibleCards(page), books.length);
    console.log('count:', await count(page));
    assert.strictEqual(await count(page), `${books.length} ${books.length === 1 ? 'book' : 'books'}`);

    await type(page, word.toUpperCase());                       // case-insensitive
    await sleep(150);
    console.log(`typed "${word.toUpperCase()}":`, await count(page), '| expected', expected);
    assert.strictEqual(await visibleCards(page), expected);
    assert.strictEqual(await count(page), `${expected} of ${books.length} ${books.length === 1 ? 'book' : 'books'}`);
    assert.ok((await page.$$('#books mark')).length >= 1, 'highlight');
    assert.ok(await page.$eval('.sb-clear', (e) => !e.classList.contains('hidden')));
    await page.screenshot({ path: `${shotDir}/search-${name}-filtered.png` });

    // several words, any order: author word + title word of the picked book
    const authorWord = fold(pick.author || '').split(/[^a-z0-9]+/).filter((w) => w.length >= 3).pop();
    if (authorWord && authorWord !== word) {
      await type(page, `${authorWord} ${word}`);
      await sleep(100);
      const exp2 = books.filter((b) => { const f = fold(b.title + ' ' + b.author); return f.includes(word) && f.includes(authorWord); }).length;
      assert.strictEqual(await visibleCards(page), exp2);
      console.log(`typed "${authorWord} ${word}":`, await count(page));
    }

    await type(page, 'qqzzxx');                                 // nothing matches
    await sleep(100);
    assert.strictEqual(await visibleCards(page), 0);
    const emptyText = await page.$eval('.sb-empty', (e) => e.classList.contains('hidden') ? '' : e.textContent);
    console.log('empty state:', emptyText);
    assert.ok(/No books match/.test(emptyText));
    assert.ok(/couldn.t match|last name/.test(emptyText));
    await page.screenshot({ path: `${shotDir}/search-${name}-empty.png` });

    await page.click('.sb-clear');                              // x button resets
    await sleep(100);
    assert.strictEqual(await page.$eval('.sb-input', (e) => e.value), '');
    assert.strictEqual(await visibleCards(page), books.length);

    if (home && unmatched.length) {                             // the unmatched list is searched too
      const um = unmatched.find((t) => fold(t).replace(/[^a-z0-9 ]/g, ' ').trim().split(/\s+/).some((w) => w.length >= 4));
      const w = fold(um).split(/[^a-z0-9]+/).filter((x) => x.length >= 4)[0];
      const expU = unmatched.filter((t) => fold(t).includes(w)).length;
      await type(page, w); await sleep(150);
      const shownU = await page.$$eval('#unresolved li', (l) => l.filter((x) => x.offsetParent !== null).length);
      console.log(`unmatched search "${w}": ${shownU} shown, expected ${expU}; heading:`, await page.$eval('#unresolvedTitle', (e) => e.textContent));
      assert.strictEqual(shownU, expU);
      await type(page, 'qqzzxx'); await sleep(100);
      assert.ok(await page.$eval('#unresolvedWrap', (e) => e.classList.contains('hidden')), 'unmatched wrap hidden when nothing matches');
      await page.click('.sb-clear'); await sleep(100);
      assert.strictEqual(await page.$$eval('#unresolved li', (l) => l.filter((x) => x.offsetParent !== null).length), unmatched.length);
    }

    // sticky: after scrolling the bar is still at the top and does not cover the first grid row start
    await page.evaluate(() => window.scrollTo(0, 0));
    const scrollable = await page.evaluate(() => document.documentElement.scrollHeight - innerHeight);
    if (scrollable > 400) {
      await page.evaluate(() => window.scrollTo(0, 600)); await sleep(150);
      const top = await page.$eval('.searchbar', (e) => Math.round(e.getBoundingClientRect().top));
      console.log('sticky bar top after scroll:', top);
      assert.ok(Math.abs(top) <= 1, 'sticky top ' + top);
      await page.screenshot({ path: `${shotDir}/search-${name}-scrolled.png` });
    } else console.log('page too short to scroll; sticky not exercised');
    // sort
    await page.select('.sb-sort select', 'author'); await sleep(100);
    const first = await page.$eval('#books .book:not(.hidden) .a', (e) => e.textContent);
    console.log('first by author:', first);
    assert.strictEqual(await visibleCards(page), books.length);
    assert.deepStrictEqual(errors, [], 'page errors');
    await page.close();
  }

  await check('shelfpage', `/s/${shelfId}`, null, false);
  await check('home', '/', `localStorage.setItem('shelfy.shelfId', ${JSON.stringify(shelfId)});`, true);
  await browser.close();
  console.log('\nui checks ok');
})().catch((e) => { console.error(e); process.exit(1); });
