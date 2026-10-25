// Tiling plan for wide / large shelf photos. Pure arithmetic (no DOM), so it can be unit-tested with node:
//   node tools/test-tiles.js
// A photo that is much wider (or taller) than a normal one is cut along its LONG axis into overlapping tiles. The
// overlap makes sure every spine is whole in at least one tile; the server removes the duplicates it creates.
(function (root) {
  'use strict';
  var CFG = {
    SINGLE_SMALL_LONG: 2000, // a photo this small (long side) is never tiled
    SINGLE_MAX_RATIO: 1.6,   // long/short side above this -> tile
    SINGLE_MAX_LONG: 4096,   // long side above this (in photo pixels) -> tile
    SINGLE_OUT_LONG: 3200,   // a photo that is not tiled is downscaled to this long side (as before)
    TILE_TARGET_RATIO: 1.5,  // aim for tiles about 1.5 x as long as the short side of the photo ...
    TILE_MAX_LONG: 2400,     // ... and at most this long; a tile's longest side is capped at this when encoded
    OVERLAP: 0.22,           // neighbouring tiles share 22% of a tile's length
    MAX_TILES: 12
  };

  // Returns { tiles: [{x, y, w, h, outW, outH}], vertical: bool }. A single image is one "tile" covering everything.
  function planTiles(w, h, cfg) {
    cfg = cfg || CFG;
    var long = Math.max(w, h), short = Math.min(w, h), horizontal = w >= h;
    var ratio = long / short;
    var tiles = [];
    function add(x, y, tw, th, cap) {
      var s = Math.min(1, cap / Math.max(tw, th));
      tiles.push({ x: x, y: y, w: tw, h: th, outW: Math.max(1, Math.round(tw * s)), outH: Math.max(1, Math.round(th * s)) });
    }
    if (long <= cfg.SINGLE_SMALL_LONG || (ratio <= cfg.SINGLE_MAX_RATIO && long <= cfg.SINGLE_MAX_LONG)) {
      add(0, 0, w, h, cfg.SINGLE_OUT_LONG);
      return { tiles: tiles, vertical: !horizontal, tiled: false };
    }
    var target = Math.min(short * cfg.TILE_TARGET_RATIO, cfg.TILE_MAX_LONG);
    var step = 1 - cfg.OVERLAP;
    var n = Math.max(2, Math.ceil((long - target) / (target * step)) + 1);
    n = Math.min(n, cfg.MAX_TILES);
    var len = long / (1 + (n - 1) * step);              // n tiles of this length, overlapping by OVERLAP, cover the photo exactly
    var tl = Math.min(long, Math.round(len));
    for (var i = 0; i < n; i++) {
      var pos = i === n - 1 ? long - tl : Math.round(i * len * step);
      if (horizontal) add(pos, 0, tl, h, cfg.TILE_MAX_LONG); else add(0, pos, w, tl, cfg.TILE_MAX_LONG);
    }
    return { tiles: tiles, vertical: !horizontal, tiled: true };
  }

  var api = { planTiles: planTiles, CONFIG: CFG };
  if (typeof module !== 'undefined' && module.exports) module.exports = api; else root.ShelfyTiles = api;
})(typeof window !== 'undefined' ? window : this);
