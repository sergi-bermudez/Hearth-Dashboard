/* Generador de codis QR petit (ES5): mode byte, correcció M, versions 1-6 (fins a 106 caràcters).
   qrSvg(text, mida) torna un <svg> amb el codi. */
var QR = (function () {
  var BLOCS = [null, [1, 16, 10], [1, 28, 16], [1, 44, 26], [2, 32, 18], [2, 43, 24], [4, 27, 16]]; /* [blocs, dades per bloc, correcció per bloc] */
  var ALIN = [null, [], [6, 18], [6, 22], [6, 26], [6, 30], [6, 34]];
  var EXP = [], LOG = [];
  (function () { var x = 1; for (var i = 0; i < 255; i++) { EXP[i] = x; LOG[x] = i; x <<= 1; if (x & 256) x ^= 0x11d; } for (i = 255; i < 512; i++) EXP[i] = EXP[i - 255]; })();
  function mul(a, b) { return a && b ? EXP[LOG[a] + LOG[b]] : 0; }
  function generador(n) {
    var g = [1];
    for (var i = 0; i < n; i++) {
      var r = [];
      for (var j = 0; j <= g.length; j++) r[j] = (j < g.length ? g[j] : 0) ^ (j > 0 ? mul(g[j - 1], EXP[i]) : 0);
      g = r;
    }
    return g;
  }
  function ecc(dades, n) {
    var g = generador(n), r = dades.concat(new Array(n + 1).join('0').split('').map(Number));
    for (var i = 0; i < dades.length; i++) {
      var c = r[i];
      if (c) for (var j = 0; j < g.length; j++) r[i + j] ^= mul(g[j], c);
    }
    return r.slice(dades.length);
  }
  function utf8(s) {
    var b = [], e = unescape(encodeURIComponent(s));
    for (var i = 0; i < e.length; i++) b.push(e.charCodeAt(i));
    return b;
  }
  function codifica(text) {
    var bytes = utf8(text), v;
    for (v = 1; v <= 6; v++) if (bytes.length <= BLOCS[v][0] * BLOCS[v][1] - 2) break;
    if (v > 6) return null;
    var nb = BLOCS[v][0], nd = BLOCS[v][1], ne = BLOCS[v][2], total = nb * nd;
    var bits = [];
    function posa(val, n) { for (var i = n - 1; i >= 0; i--) bits.push((val >> i) & 1); }
    posa(4, 4); posa(bytes.length, 8);
    for (var i = 0; i < bytes.length; i++) posa(bytes[i], 8);
    posa(0, Math.min(4, total * 8 - bits.length));
    while (bits.length % 8) bits.push(0);
    var cw = [];
    for (i = 0; i < bits.length; i += 8) { var x = 0; for (var k = 0; k < 8; k++) x = x << 1 | bits[i + k]; cw.push(x); }
    for (var p = 0; cw.length < total; p++) cw.push(p % 2 ? 0x11 : 0xEC);
    var blocs = [], eccs = [];
    for (i = 0; i < nb; i++) { blocs.push(cw.slice(i * nd, (i + 1) * nd)); eccs.push(ecc(blocs[i], ne)); }
    var fi = [];
    for (i = 0; i < nd; i++) for (k = 0; k < nb; k++) fi.push(blocs[k][i]);
    for (i = 0; i < ne; i++) for (k = 0; k < nb; k++) fi.push(eccs[k][i]);
    return { v: v, cw: fi };
  }
  function matriu(text) {
    var c = codifica(text);
    if (!c) return null;
    var n = 17 + 4 * c.v, m = [], res = [], i, j, r;
    for (i = 0; i < n; i++) { m.push([]); res.push([]); for (j = 0; j < n; j++) { m[i].push(0); res[i].push(false); } }
    function fixa(y, x, val) { if (y >= 0 && y < n && x >= 0 && x < n) { m[y][x] = val ? 1 : 0; res[y][x] = true; } }
    function cercador(y, x) {
      for (var dy = -1; dy <= 7; dy++) for (var dx = -1; dx <= 7; dx++) {
        var dins = dy >= 0 && dy <= 6 && dx >= 0 && dx <= 6;
        var negre = dins && (dy == 0 || dy == 6 || dx == 0 || dx == 6 || (dy >= 2 && dy <= 4 && dx >= 2 && dx <= 4));
        fixa(y + dy, x + dx, negre);
      }
    }
    cercador(0, 0); cercador(0, n - 7); cercador(n - 7, 0);
    for (i = 8; i < n - 8; i++) { fixa(6, i, i % 2 == 0); fixa(i, 6, i % 2 == 0); }
    var al = ALIN[c.v];
    for (i = 0; i < al.length; i++) for (j = 0; j < al.length; j++) {
      var ay = al[i], ax = al[j];
      if ((ay == 6 && ax == 6) || (ay == 6 && ax == n - 7) || (ay == n - 7 && ax == 6)) continue;
      for (var dy = -2; dy <= 2; dy++) for (var dx = -2; dx <= 2; dx++) fixa(ay + dy, ax + dx, Math.max(Math.abs(dy), Math.abs(dx)) != 1);
    }
    fixa(n - 8, 8, true);
    /* Reserva el lloc de la informació de format */
    for (i = 0; i < 9; i++) { if (!res[8][i]) res[8][i] = true; if (!res[i][8]) res[i][8] = true; }
    for (i = 0; i < 8; i++) { res[8][n - 1 - i] = true; res[n - 1 - i][8] = true; }
    /* Dades en zig-zag */
    var bits = [];
    for (i = 0; i < c.cw.length; i++) for (var b = 7; b >= 0; b--) bits.push((c.cw[i] >> b) & 1);
    var bi = 0, puja = true;
    for (var col = n - 1; col > 0; col -= 2) {
      if (col == 6) col--;
      for (var k = 0; k < n; k++) {
        var y = puja ? n - 1 - k : k;
        for (var t = 0; t < 2; t++) {
          var xx = col - t;
          if (res[y][xx]) continue;
          m[y][xx] = bi < bits.length ? bits[bi] : 0;
          bi++;
        }
      }
      puja = !puja;
    }
    /* Màscara 0 ((x+y)%2) i informació de format (correcció M = 00) */
    var mascara = 0;
    for (y = 0; y < n; y++) for (var x = 0; x < n; x++) if (!res[y][x] && (x + y) % 2 == 0) m[y][x] ^= 1;
    var dades = (0 << 3) | mascara, rem = dades;
    for (i = 0; i < 10; i++) rem = (rem << 1) ^ ((rem >> 9) * 0x537);
    var fmt = ((dades << 10) | rem) ^ 0x5412;
    function fb(k) { return (fmt >> k) & 1; }
    /* m[y][x] */
    for (i = 0; i <= 5; i++) m[i][8] = fb(i);
    m[7][8] = fb(6); m[8][8] = fb(7); m[8][7] = fb(8);
    for (i = 9; i < 15; i++) m[8][14 - i] = fb(i);
    for (i = 0; i < 8; i++) m[8][n - 1 - i] = fb(i);
    for (i = 8; i < 15; i++) m[n - 15 + i][8] = fb(i);
    m[n - 8][8] = 1;
    return m;
  }
  function svg(text, mida) {
    var m = matriu(text);
    if (!m) return '';
    var n = m.length, q = 4, d = '';
    for (var y = 0; y < n; y++) for (var x = 0; x < n; x++) if (m[y][x]) d += 'M' + (x + q) + ' ' + (y + q) + 'h1v1h-1z';
    return '<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 ' + (n + 2 * q) + ' ' + (n + 2 * q) + '" width="' + (mida || 220) + '" height="' + (mida || 220) + '" shape-rendering="crispEdges"><rect width="100%" height="100%" fill="#fff"/><path d="' + d + '" fill="#000"/></svg>';
  }
  return { matriu: matriu, svg: svg };
})();
function qrSvg(text, mida) { return QR.svg(text, mida); }
