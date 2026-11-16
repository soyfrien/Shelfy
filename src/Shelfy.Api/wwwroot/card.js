// Download the signed-in member's library card as SVG, or as a 2× PNG drawn on a
// same-origin canvas (so it isn't tainted). Used on My loans and the post-sign-up card.
(function () {
  'use strict';
  var SVG_URL = '/api/account/card.svg';
  var WIDTH = 856;
  var HEIGHT = 540;
  var SCALE = 2; // 1712×1080, sharp enough to print or to share in apps that still skip SVG
  var SANS = "'Segoe UI Variable','Segoe UI',Inter,'Liberation Sans','Noto Sans','DejaVu Sans',Helvetica,Arial,sans-serif";
  var MONO = "ui-monospace,'Cascadia Mono',Consolas,Menlo,'Liberation Mono','DejaVu Sans Mono',monospace";

  function memberNoFromSvg(svg) {
    var m = svg.match(/Member No\.\s*([0-9A-HJKMNP-TV-Z]{4}-[0-9A-HJKMNP-TV-Z]{4})/);
    return m ? m[1] : null;
  }

  function fileBase(svg) {
    var n = memberNoFromSvg(svg);
    return n ? 'shelfy-library-card-' + n : 'shelfy-library-card';
  }

  // Extra system fonts so the canvas text stays readable on machines without Segoe or Inter.
  function withFontFallback(svg) {
    return svg
      .replace(/font-family="'Segoe UI Variable','Segoe UI',Inter,Helvetica,Arial,sans-serif"/g, 'font-family="' + SANS + '"')
      .replace(/font-family="ui-monospace,'Cascadia Mono',Consolas,Menlo,monospace"/g, 'font-family="' + MONO + '"');
  }

  function saveBlob(blob, name) {
    var url = URL.createObjectURL(blob);
    var a = document.createElement('a');
    a.href = url;
    a.download = name;
    document.body.appendChild(a);
    a.click();
    a.remove();
    setTimeout(function () { URL.revokeObjectURL(url); }, 2000);
  }

  function fetchSvg() {
    return fetch(SVG_URL, { credentials: 'same-origin', headers: { Accept: 'image/svg+xml' } })
      .then(function (r) {
        if (!r.ok) throw new Error(r.status === 401 ? 'Sign in first.' : 'Could not load the card.');
        return r.text();
      });
  }

  function pngBlobFromSvg(svg) {
    return new Promise(function (resolve, reject) {
      var blob = new Blob([withFontFallback(svg)], { type: 'image/svg+xml;charset=utf-8' });
      var url = URL.createObjectURL(blob);
      var img = new Image();
      img.onload = function () {
        try {
          var canvas = document.createElement('canvas');
          canvas.width = WIDTH * SCALE;
          canvas.height = HEIGHT * SCALE;
          var ctx = canvas.getContext('2d');
          if (!ctx) { URL.revokeObjectURL(url); reject(new Error('Could not make a PNG.')); return; }
          ctx.imageSmoothingEnabled = true;
          ctx.imageSmoothingQuality = 'high';
          ctx.drawImage(img, 0, 0, canvas.width, canvas.height);
          URL.revokeObjectURL(url);
          canvas.toBlob(function (png) {
            if (!png) reject(new Error('Could not make a PNG.'));
            else resolve(png);
          }, 'image/png');
        } catch (e) {
          URL.revokeObjectURL(url);
          reject(e);
        }
      };
      img.onerror = function () {
        URL.revokeObjectURL(url);
        reject(new Error('Could not draw the card.'));
      };
      img.src = url;
    });
  }

  function downloadSvg() {
    return fetchSvg().then(function (svg) {
      saveBlob(new Blob([svg], { type: 'image/svg+xml;charset=utf-8' }), fileBase(svg) + '.svg');
    });
  }

  function downloadPng() {
    return fetchSvg().then(function (svg) {
      return pngBlobFromSvg(svg).then(function (png) { saveBlob(png, fileBase(svg) + '.png'); });
    });
  }

  function showErr(msg) {
    var err = document.getElementById('cardDlErr');
    if (!err) return;
    err.textContent = msg;
    err.classList.remove('hidden');
  }

  function hideErr() {
    var err = document.getElementById('cardDlErr');
    if (!err) return;
    err.textContent = '';
    err.classList.add('hidden');
  }

  function onClick(el, fn) {
    el.addEventListener('click', function (e) {
      e.preventDefault();
      if (el.getAttribute('aria-busy') === 'true') return;
      hideErr();
      el.setAttribute('aria-busy', 'true');
      var prev = el.textContent;
      el.textContent = 'Preparing…';
      fn().then(function () {
        el.textContent = prev;
        el.removeAttribute('aria-busy');
      }, function (err) {
        el.textContent = prev;
        el.removeAttribute('aria-busy');
        showErr((err && err.message) || 'Could not download the card.');
      });
    });
  }

  function wire() {
    var svgA = document.getElementById('cardDownloadSvg');
    var pngA = document.getElementById('cardDownloadPng');
    if (svgA) onClick(svgA, downloadSvg);
    if (pngA) onClick(pngA, downloadPng);
  }

  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', wire);
  else wire();

  window.ShelfyCard = {
    downloadSvg: downloadSvg,
    downloadPng: downloadPng,
    pngBlobFromSvg: pngBlobFromSvg,
    memberNoFromSvg: memberNoFromSvg
  };
})();
