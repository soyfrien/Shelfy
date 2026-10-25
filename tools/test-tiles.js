// Sanity checks for the tiling plan: node tools/test-tiles.js
const { planTiles } = require('../src/Shelfy.Api/wwwroot/tiles.js');
const assert = require('assert');
function check(w, h, expectTiled, minN, maxN) {
  const p = planTiles(w, h);
  assert.strictEqual(p.tiled, expectTiled, `${w}x${h} tiled=${p.tiled}`);
  if (!expectTiled) { assert.strictEqual(p.tiles.length, 1); return p; }
  assert.ok(p.tiles.length >= minN && p.tiles.length <= maxN, `${w}x${h}: ${p.tiles.length} tiles`);
  const horiz = w >= h, L = horiz ? w : h;
  let covered = 0;
  p.tiles.forEach((t, i) => {
    const pos = horiz ? t.x : t.y, len = horiz ? t.w : t.h;
    assert.ok(pos >= 0 && pos + len <= L, 'tile inside photo');
    assert.ok(Math.max(t.outW, t.outH) <= 2400, 'output cap');
    assert.ok(pos <= covered, `gap before tile ${i}: ${pos} > ${covered}`);          // no gaps
    if (i > 0) { const prev = p.tiles[i - 1], pp = horiz ? prev.x : prev.y, pl = horiz ? prev.w : prev.h;
      const ov = (pp + pl - pos) / len; assert.ok(ov >= 0.15 && ov <= 0.4, `overlap ${ov.toFixed(2)}`); }
    covered = Math.max(covered, pos + len);
  });
  assert.strictEqual(covered, L, 'covers the whole photo');
  return p;
}
check(4032, 3024, false);            // normal 4:3 phone photo: single image
check(3000, 2000, false);            // 3:2: single
check(1920, 1080, false);            // small 16:9: single
check(3200, 1800, true, 2, 2);       // 16:9 at full size: two tiles
check(5000, 2500, true, 2, 3);       // ratio 2
check(8000, 2000, true, 3, 5);       // wide shelf
check(12000, 3000, true, 5, 8);      // panorama
check(2000, 5000, true, 2, 4);       // tall (portrait) bookcase: split top to bottom
check(1300, 2200, true, 2, 3);       // ratio 1.7
check(30000, 1000, true, 12, 12);    // tile cap
const p = planTiles(8000, 2000); console.log('8000x2000 ->', JSON.stringify(p.tiles.map(t => [t.x, t.w, t.outW + 'x' + t.outH])));
console.log('tiles ok');
