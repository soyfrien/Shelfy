// Headless mobile-Chrome check of "tap to fix" on the home page (ADR-011). Needs puppeteer-core and a Chrome (CHROME=/usr/bin/google-chrome).
//
//   node tools/ui-check-fix.js --mock [screenshotDir]
//       Runs against a small in-process mock of the API (serves wwwroot), so no server is needed.
//   node tools/ui-check-fix.js <baseUrl> <shelfId> <spineTextFragment> <searchQuery> <expectedTitle> [screenshotDir]
//       Runs against a live server and a TEST shelf only: taps the unmatched spine whose text contains the fragment, uses the
//       suggestions (or the search box with <searchQuery> when the title is not suggested), picks <expectedTitle>, and checks that
//       the book joined the shelf and the spine left the list. It CHANGES that shelf; never point it at a real one.
const puppeteer = require('puppeteer-core');
const assert = require('assert');
const http = require('http');
const fs = require('fs');
const path = require('path');

const args = process.argv.slice(2);
const MOCK = args[0] === '--mock';
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

// ---------------------------------------------------------------- mock API
function startMock() {
  const root = path.join(__dirname, '..', 'src', 'Shelfy.Api', 'wwwroot');
  const id = '11111111-1111-1111-1111-111111111111';
  const state = {
    books: [{ bookId: 'b-hobbit', title: 'The Hobbit', author: 'J.R.R. Tolkien', isbn: '9780547928227', coverUrl: null, status: 'Private' }],
    spines: [
      { id: 's-found', text: 'Foundaton | Isaac Asimov', candidateTitle: 'Foundaton' },
      { id: 's-hyp', text: 'HYPERIOM DAN SIMMONS', candidateTitle: 'HYPERIOM' },
      { id: 's-nope', text: 'Library of Congress sticker 4421', candidateTitle: 'Library of Congress sticker 4421' },
      { id: 's-slow', text: 'SLOWPOKE CHRONICLES', candidateTitle: 'SLOWPOKE' }
    ],
    low: [{ id: 's-th', text: 'TH', candidateTitle: 'TH', lowQuality: true, reason: 'short' }],
    calls: [], failNext: null, unavailable: false
  };
  const sug = {
    's-found': [{ isbn13: '9780553293357', title: 'Foundation', author: 'Isaac Asimov', coverUrl: null, score: 0.9, source: 'OpenLibrary', strategy: 'parsed' },
                { isbn13: '9780553293371', title: 'Foundation and Empire', author: 'Isaac Asimov', coverUrl: null, score: 0.6, source: 'OpenLibrary', strategy: 'full' }],
    's-hyp': [], 's-nope': [], 's-slow': []
  };
  const server = http.createServer((q, r) => {
    const u = new URL(q.url, 'http://x'); const p = u.pathname;
    const json = (o, code = 200) => { r.statusCode = code; r.setHeader('content-type', 'application/json'); r.end(JSON.stringify(o)); };
    let body = ''; q.on('data', (c) => { body += c; });
    q.on('end', () => {
      state.calls.push(`${q.method} ${p}${u.search}`);
      if (p === `/api/shelves/${id}` && q.method === 'GET') return json({ id, userId: id, isPublic: false, books: state.books });
      if (p === `/api/shelves/${id}/unmatched`) {
        const inc = u.searchParams.get('includeHidden') === 'true';
        return json({ shelfId: id, spines: state.spines.concat(inc ? state.low : []), hiddenLowQuality: state.low.length });
      }
      const m = p.match(/^\/api\/shelves\/[^/]+\/spines\/([^/]+)\/(suggestions|resolve|dismiss)$/);
      if (m) {
        const sid = m[1]; const spine = state.spines.find((s) => s.id === sid);
        if (!spine) return json({ error: 'Spine not found.' }, 404);
        if (m[2] === 'suggestions') {
          if (state.unavailable) return json({ spineId: sid, text: spine.text, suggestions: [], unavailable: true });
          const query = u.searchParams.get('q');
          const delay = sid === 's-slow' ? 600 : 0;
          return setTimeout(() => json({ spineId: sid, text: spine.text,
            suggestions: query ? (/hyperion/i.test(query) ? [{ isbn13: '9780553283686', title: 'Hyperion', author: 'Dan Simmons', coverUrl: null, score: 0, source: 'OpenLibrary', strategy: 'search' }] : []) : sug[sid], unavailable: false }), delay);
        }
        if (state.failNext) { const c = state.failNext; state.failNext = null; return json({ error: 'boom' }, c); }
        if (m[2] === 'resolve') {
          const b = JSON.parse(body || '{}');
          if (!b.isbn13) return json({ error: 'A valid isbn13 (or a bookId) is required.' }, 400);
          state.spines = state.spines.filter((s) => s.id !== sid);
          const added = !state.books.some((x) => x.isbn === b.isbn13);
          if (added) state.books.push({ bookId: 'b-' + b.isbn13, title: b.title, author: b.author, isbn: b.isbn13, coverUrl: null, status: 'Private' });
          return json({ spineId: sid, book: { bookId: 'b-' + b.isbn13, title: b.title, author: b.author, isbn: b.isbn13, coverUrl: null }, addedToShelf: added, spinesResolved: 1, remembered: true });
        }
        state.spines = state.spines.filter((s) => s.id !== sid);
        return json({ spineId: sid, dismissed: true, spinesDismissed: 1 });
      }
      let f = p === '/' ? 'index.html' : p.slice(1);
      try {
        const buf = fs.readFileSync(path.join(root, f));
        r.setHeader('content-type', f.endsWith('.js') ? 'text/javascript' : f.endsWith('.css') ? 'text/css' : 'text/html'); r.end(buf);
      } catch (e) { r.statusCode = 404; r.end('nf'); }
    });
  });
  return new Promise((res) => server.listen(0, () => res({ server, state, id, base: `http://127.0.0.1:${server.address().port}` })));
}

// ---------------------------------------------------------------- helpers
async function openHome(browser, base, shelfId, shotDir) {
  const page = await browser.newPage();
  await page.setViewport({ width: 390, height: 844, deviceScaleFactor: 2, isMobile: true, hasTouch: true });
  await page.setUserAgent('Mozilla/5.0 (iPhone; CPU iPhone OS 17_0 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.0 Mobile/15E148 Safari/604.1');
  const errors = [];
  page.on('pageerror', (e) => errors.push(String(e)));
  page.on('console', (m) => { if (m.type() === 'error' && !/Failed to load resource/.test(m.text())) errors.push(m.text()); });
  await page.evaluateOnNewDocument(`localStorage.setItem('shelfy.shelfId', ${JSON.stringify(shelfId)});`);
  await page.goto(base + '/', { waitUntil: 'networkidle2' });
  await page.waitForSelector('#unresolved li.tap', { timeout: 15000 });
  return { page, errors };
}
const listTexts = (page) => page.$$eval('#unresolved li', (l) => l.filter((x) => x.offsetParent !== null).map((x) => x.textContent));
const cardTitles = (page) => page.$$eval('#books .book .t', (l) => l.filter((x) => x.closest('.book').offsetParent !== null).map((x) => x.textContent));
const sheetOpen = (page) => page.$eval('#fixModal', (e) => !e.classList.contains('hidden'));
async function tapSpine(page, fragment) {
  const handle = await page.evaluateHandle((f) => [...document.querySelectorAll('#unresolved li.tap')].find((li) => li.textContent.includes(f)), fragment);
  assert.ok(handle.asElement(), 'no tappable spine containing ' + fragment);
  await handle.asElement().tap();
  await page.waitForFunction(() => !document.getElementById('fixModal').classList.contains('hidden'));
}
async function waitSuggestionsSettled(page) {
  await page.waitForFunction(() => { const s = document.getElementById('fixStatus'); return !/Looking|Searching/.test(s.textContent); }, { timeout: 40000 });
}
const sugTitles = (page) => page.$$eval('#fixList .sg-title', (l) => l.map((x) => x.textContent));
async function search(page, q) {
  await page.$eval('#fixQuery', (e) => { e.value = ''; });
  await page.focus('#fixQuery'); await page.keyboard.type(q); await page.keyboard.press('Enter');
  await sleep(100);
  await waitSuggestionsSettled(page);
}
async function toastText(page) {
  await page.waitForFunction(() => !document.getElementById('toast').classList.contains('hidden'), { timeout: 5000 });
  return page.$eval('#toast', (e) => e.textContent);
}
async function pickByTitle(page, title) {
  const handle = await page.evaluateHandle((t) => [...document.querySelectorAll('#fixList .sg')].find((b) => b.querySelector('.sg-title').textContent === t), title);
  assert.ok(handle.asElement(), 'suggestion not offered: ' + title);
  await handle.asElement().tap();
  await page.waitForFunction(() => document.getElementById('fixModal').classList.contains('hidden'), { timeout: 20000 });
}

// ---------------------------------------------------------------- mock scenario
async function mockRun(browser, shotDir) {
  const { server, state, id, base } = await startMock();
  try {
    const { page, errors } = await openHome(browser, base, id, shotDir);

    console.log('== list');
    let texts = await listTexts(page);
    assert.strictEqual(texts.length, 4);
    assert.ok(await page.$eval('#hiddenReads', (e) => !e.classList.contains('hidden')), 'hidden note shown');
    console.log('hidden note:', await page.$eval('#hiddenReadsText', (e) => e.textContent));
    const li = await page.$('#unresolved li.tap'); const box = await li.boundingBox();
    assert.ok(box.height >= 44, 'tap target height ' + box.height);
    await page.screenshot({ path: `${shotDir}/fix-list.png` });

    console.log('== show / hide low-quality reads');
    await page.click('#toggleHidden'); await sleep(200);
    texts = await listTexts(page);
    assert.strictEqual(texts.length, 5);
    assert.ok(await page.$eval('#unresolved li.lowq', () => true));
    await page.click('#toggleHidden'); await sleep(200);
    assert.strictEqual((await listTexts(page)).length, 4);

    console.log('== open the sheet, see suggestions');
    await tapSpine(page, 'Foundaton');
    assert.strictEqual(await page.$eval('#fixRaw', (e) => e.textContent), 'Foundaton | Isaac Asimov');
    await waitSuggestionsSettled(page);
    assert.deepStrictEqual(await sugTitles(page), ['Foundation', 'Foundation and Empire']);
    const fs16 = await page.$eval('#fixQuery', (e) => parseFloat(getComputedStyle(e).fontSize));
    assert.ok(fs16 >= 16, 'search font-size ' + fs16);
    const sheetBox = await page.$eval('#fixModal .sheet', (e) => { const b = e.getBoundingClientRect(); return { top: b.top, bottom: b.bottom, h: innerHeight }; });
    assert.ok(Math.abs(sheetBox.bottom - sheetBox.h) <= 1, 'sheet sits at the bottom');
    await page.screenshot({ path: `${shotDir}/fix-sheet.png` });

    console.log('== cancel changes nothing');
    await page.tap('#fixCancel'); await sleep(150);
    assert.ok(!(await sheetOpen(page)));
    assert.strictEqual((await listTexts(page)).length, 4);
    assert.ok(!state.calls.some((c) => c.startsWith('POST')));

    console.log('== pick a suggestion');
    await tapSpine(page, 'Foundaton'); await waitSuggestionsSettled(page);
    await pickByTitle(page, 'Foundation');
    assert.match(await toastText(page), /Added .Foundation. to your shelf/);
    await page.waitForFunction(() => document.querySelectorAll('#unresolved li').length === 3);
    assert.deepStrictEqual((await cardTitles(page)).sort(), ['Foundation', 'The Hobbit']);
    assert.ok(!(await listTexts(page)).some((t) => t.includes('Foundaton')));
    assert.ok(/2 books/.test(await page.$eval('#resultTitle', (e) => e.textContent)));
    await page.screenshot({ path: `${shelfShot(shotDir)}-picked.png` });

    console.log('== search box when nothing is suggested');
    await tapSpine(page, 'HYPERIOM'); await waitSuggestionsSettled(page);
    assert.strictEqual((await sugTitles(page)).length, 0);
    assert.ok(/No good guesses/.test(await page.$eval('#fixStatus', (e) => e.textContent)));
    await search(page, 'hyperion simmons');
    assert.deepStrictEqual(await sugTitles(page), ['Hyperion']);
    assert.ok(state.calls.some((c) => c.includes('suggestions?q=hyperion%20simmons')));
    await page.screenshot({ path: `${shotDir}/fix-search.png` });
    await search(page, 'qqzz');
    assert.ok(/Nothing found/.test(await page.$eval('#fixStatus', (e) => e.textContent)));

    console.log('== error keeps the sheet open and changes nothing');
    await search(page, 'hyperion');
    state.failNext = 500;
    await page.tap('#fixList .sg'); await sleep(300);
    assert.ok(await sheetOpen(page));
    assert.ok(/Nothing was changed/.test(await page.$eval('#fixError', (e) => e.textContent)));
    await page.screenshot({ path: `${shotDir}/fix-error.png` });
    await page.tap('#fixList .sg');
    await page.waitForFunction(() => document.getElementById('fixModal').classList.contains('hidden'));
    assert.ok((await cardTitles(page)).includes('Hyperion'));

    console.log('== dismiss needs two taps');
    await tapSpine(page, 'Library of Congress'); await waitSuggestionsSettled(page);
    await page.tap('#fixDismiss'); await sleep(100);
    assert.ok(await sheetOpen(page), 'first tap only arms it');
    assert.ok(/Tap again/.test(await page.$eval('#fixDismiss', (e) => e.textContent)));
    await page.tap('#fixDismiss');
    await page.waitForFunction(() => document.getElementById('fixModal').classList.contains('hidden'));
    assert.match(await toastText(page), /Dismissed/);
    await page.waitForFunction(() => ![...document.querySelectorAll('#unresolved li')].some((l) => l.textContent.includes('Library of Congress')));

    console.log('== the book search box still finds a fixed book and the rest of the list');
    await page.focus('.sb-input'); await page.keyboard.type('slowpoke'); await sleep(200);
    assert.deepStrictEqual(await listTexts(page), ['SLOWPOKE CHRONICLES']);
    await page.tap('#unresolved li.tap'); await page.waitForFunction(() => !document.getElementById('fixModal').classList.contains('hidden'));
    await page.tap('#fixCancel');
    await page.click('.sb-clear'); await sleep(100);

    console.log('== catalogs down');
    state.unavailable = true;
    await tapSpine(page, 'SLOWPOKE'); await waitSuggestionsSettled(page);
    assert.ok(/aren.t answering/.test(await page.$eval('#fixStatus', (e) => e.textContent)));
    await page.screenshot({ path: `${shotDir}/fix-unavailable.png` });
    await page.keyboard.press('Escape'); await sleep(100);
    assert.ok(!(await sheetOpen(page)), 'Escape closes the sheet');
    state.unavailable = false;

    console.log('== a stale tap (spine gone on the server) refreshes the list');
    state.spines = state.spines.filter((s) => s.id !== 's-slow');
    await page.evaluate(() => document.querySelector('#unresolved li.tap').click());
    await page.waitForFunction(() => document.getElementById('toast').textContent.includes('gone'), { timeout: 5000 });
    assert.ok(!(await sheetOpen(page)));

    assert.deepStrictEqual(errors, [], 'page errors');
    await page.close();
  } finally { server.close(); }
}
const shelfShot = (d) => `${d}/fix-shelf`;

// ---------------------------------------------------------------- live scenario
async function liveRun(browser, base, shelfId, fragment, query, expectedTitle, shotDir) {
  const before = await (await fetch(`${base}/api/shelves/${shelfId}`)).json();
  const unmatchedBefore = (await (await fetch(`${base}/api/shelves/${shelfId}/unmatched`)).json()).spines;
  console.log(`test shelf: ${before.books.length} books, ${unmatchedBefore.length} unmatched`);
  const { page, errors } = await openHome(browser, base, shelfId, shotDir);
  await tapSpine(page, fragment);
  console.log('read as:', await page.$eval('#fixRaw', (e) => e.textContent));
  await waitSuggestionsSettled(page);
  let titles = await sugTitles(page);
  console.log('suggested:', JSON.stringify(titles));
  await page.screenshot({ path: `${shotDir}/live-fix-sheet.png` });
  if (!titles.includes(expectedTitle)) {
    console.log('not suggested; searching for', query);
    await search(page, query);
    titles = await sugTitles(page);
    console.log('search found:', JSON.stringify(titles));
    await page.screenshot({ path: `${shotDir}/live-fix-search.png` });
  }
  assert.ok(titles.includes(expectedTitle), `${expectedTitle} not offered: ${JSON.stringify(titles)}`);
  await pickByTitle(page, expectedTitle);
  console.log('toast:', await toastText(page));
  await page.waitForFunction((t) => [...document.querySelectorAll('#books .book .t')].some((x) => x.textContent === t), { timeout: 15000 }, expectedTitle);
  await page.waitForFunction((f) => ![...document.querySelectorAll('#unresolved li')].some((l) => l.textContent.includes(f)), { timeout: 15000 }, fragment);
  await page.screenshot({ path: `${shotDir}/live-fix-done.png` });
  const after = await (await fetch(`${base}/api/shelves/${shelfId}`)).json();
  assert.strictEqual(after.books.length, before.books.length + 1);
  assert.ok(after.books.some((b) => b.title === expectedTitle && b.status === 0 || b.status === 'Private'));
  const unmatchedAfter = (await (await fetch(`${base}/api/shelves/${shelfId}/unmatched`)).json()).spines;
  assert.strictEqual(unmatchedAfter.length, unmatchedBefore.length - 1);
  assert.deepStrictEqual(errors, [], 'page errors');
  console.log('live fix ok: the book joined the shelf and the spine left the list');
  await page.close();
}

(async () => {
  const browser = await puppeteer.launch({ executablePath: process.env.CHROME || '/usr/bin/google-chrome', headless: 'new', args: ['--no-sandbox'] });
  try {
    if (MOCK) { fs.mkdirSync(args[1] || '/tmp', { recursive: true }); await mockRun(browser, args[1] || '/tmp'); console.log('\nmock ui checks ok'); }
    else {
      const [base, shelfId, fragment, query, title, dir = '/tmp'] = args;
      if (!base || !shelfId || !fragment || !query || !title) { console.error('usage: see the top of this file'); process.exit(2); }
      fs.mkdirSync(dir, { recursive: true });
      await liveRun(browser, base, shelfId, fragment, query, title, dir);
    }
  } finally { await browser.close(); }
})().catch((e) => { console.error(e); process.exit(1); });
