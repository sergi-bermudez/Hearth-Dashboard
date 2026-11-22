/* Casa · utilitats comunes (ES5, compatible amb Safari 9 / iPad 3) */
var EXT_VID = ['mp4', 'm4v', 'mov'];
var EXT_VLC = ['mkv', 'avi', 'wmv', 'mpg', 'mpeg', 'ts', 'webm', 'flv', 'm2ts', 'vob'];
var EXT_AUD = ['mp3', 'm4a', 'm4b', 'aac', 'wav', 'aif', 'aiff'];
var EXT_AUD_NO = ['flac', 'ogg', 'opus', 'wma', 'ape'];
var EXT_IMG = ['jpg', 'jpeg', 'png', 'gif', 'bmp'];

/* Canals de TV per defecte (3Cat). Cada canal prova les adreces en ordre. */
var CANALS_DEF = [
  { id: 'tv3', nom: 'TV3 Internacional', color: '#1f6fd1', urls: [
    'https://directes-tv-int.3catdirectes.cat/live-origin/tvc-hls/master.m3u8',
    'https://directes-tv-int.3catdirectes.cat/live-content/tvc-hls/master.m3u8',
    'https://directes3-tv-es.3catdirectes.cat/live-content/tvc-hls/master.m3u8',
    'https://directes-tv-es.3catdirectes.cat/live-origin/tvc-hls/master.m3u8'] },
  { id: '324', nom: '3CatInfo', color: '#d6213a', urls: [
    'https://directes-tv-int.3catdirectes.cat/live-origin/canal324-hls/master.m3u8'] },
  { id: '33', nom: 'El 33', color: '#7a3fd0', urls: [
    'https://directes-tv-cat.3catdirectes.cat/live-content/c33-super3-hls/master.m3u8',
    'https://directes-tv-cat.3catdirectes.cat/live-origin/c33-super3-hls/master.m3u8',
    'https://directes-tv-int.3catdirectes.cat/live-origin/c33-super3-hls/master.m3u8'] },
  { id: 'sx3', nom: 'SX3', color: '#f29a10', urls: [
    'https://directes-tv-cat.3catdirectes.cat/live-content/super3-hls/master.m3u8',
    'https://directes-tv-cat.3catdirectes.cat/live-origin/super3-hls/master.m3u8'] },
  { id: 'esport3', nom: 'Esport3', color: '#14a35a', urls: [
    'https://directes-tv-cat.3catdirectes.cat/live-content/esport3-hls/master.m3u8',
    'https://directes-tv-cat.3catdirectes.cat/live-origin/esport3-hls/master.m3u8',
    'https://directes-tv-int.3catdirectes.cat/live-origin/esport3-hls/master.m3u8'] },
  { id: 'bondia', nom: 'Bon Dia TV', color: '#e8542b', urls: [
    'https://directes-tv-int.3catdirectes.cat/live-origin/bondia-hls/master.m3u8',
    'https://directes-tv-int.3catdirectes.cat/live-content/bondia-hls/master.m3u8'] },
  { id: 'temps', nom: 'Càmeres del temps', color: '#2a9fb8', urls: [
    'https://directes-tv-int.3catdirectes.cat/live-content/beauties-hls/master.m3u8'] },
  { id: 'platsbruts', nom: 'Plats bruts', color: '#b0386e', urls: [
    'https://fast-tailor.3catdirectes.cat/v1/channel/ccma-channel2/hls.m3u8'] },
  { id: 'oca1', nom: '3Cat Exclusiu 1', color: '#555d6b', urls: [
    'https://directes-tv-cat.3catdirectes.cat/live-content/oca1-hls/master.m3u8',
    'https://directes-tv-cat.3catdirectes.cat/live-origin/oca1-hls/master.m3u8',
    'https://directes-tv-es.3catdirectes.cat/live-origin/oca1-hls/master.m3u8'] },
  { id: 'oca2', nom: '3Cat Exclusiu 2', color: '#555d6b', urls: [
    'https://directes-tv-cat.3catdirectes.cat/live-content/oca2-hls/master.m3u8',
    'https://directes-tv-cat.3catdirectes.cat/live-origin/oca2-hls/master.m3u8',
    'https://directes-tv-es.3catdirectes.cat/live-origin/oca2-hls/master.m3u8'] },
  { id: 'oca3', nom: '3Cat Exclusiu 3', color: '#555d6b', urls: [
    'https://directes-tv-cat.3catdirectes.cat/live-content/oca3-hls/master.m3u8',
    'https://directes-tv-cat.3catdirectes.cat/live-origin/oca3-hls/master.m3u8',
    'https://directes-tv-es.3catdirectes.cat/live-origin/oca3-hls/master.m3u8'] }
];
/* Els canals per defecte són de Catalunya: només si Hearth és en català */
if (typeof LANG != 'undefined' && LANG != 'ca') CANALS_DEF = [];

/* Pont amb la pàgina principal (dashboard), que té el reproductor */
/* Els navegadors nous retornen una promesa de play() que pot fallar; l'ignorem (el Safari 9 no en retorna cap) */
(function () {
  try {
    var P = HTMLMediaElement.prototype, o = P.play;
    P.play = function () { var r = o.apply(this, arguments); if (r && r.then) r.then(null, function () { }); return r; };
  } catch (e) { }
})();
var Casa = null;
try { if (window.parent !== window && window.parent.Casa) Casa = window.parent.Casa; } catch (e) { Casa = null; }

/* ---------- Utilitats ---------- */
function $(id) { return document.getElementById(id); }
function esc(s) {
  return String(s == null ? '' : s).replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;').replace(/'/g, '&#39;');
}
function js(v) { return esc(JSON.stringify(v)); }
function enc(s) { return encodeURIComponent(s); }
function qs(o) {
  var r = [];
  for (var k in o) if (o.hasOwnProperty(k)) r.push(enc(k) + '=' + enc(o[k]));
  return r.join('&');
}
function api(nom, params) { return '/api/' + nom + (params ? '?' + qs(params) : ''); }
function fUrl(c, p) {
  var parts = p.split('/');
  for (var i = 0; i < parts.length; i++) parts[i] = enc(parts[i]);
  return '/f/' + enc(c) + '/' + parts.join('/');
}
function miniUrl(c, p, m) { return api('mini', { c: c, p: p, m: m || 320 }); }
function viaX(u) { return u ? api('x', { u: u }) : ''; }
function imgx(u, m) { return u ? api('imgx', { u: u, m: m || 200 }) : ''; }
function absolut(u) { return location.protocol + '//' + location.host + u; }
function bg(u) { return u ? "url('" + String(u).replace(/'/g, '%27').replace(/"/g, '%22') + "')" : 'none'; }
function ext(n) { var i = n.lastIndexOf('.'); return i < 0 ? '' : n.substring(i + 1).toLowerCase(); }
function senseExt(n) { var i = n.lastIndexOf('.'); return i <= 0 ? n : n.substring(0, i); }
function dins(llista, x) { return llista.indexOf(x) >= 0; }
function uneix(a, b) { return a ? a + '/' + b : b; }
function retalla(s) { return String(s || '').replace(/^\s+|\s+$/g, ''); }
function pla(s) {
  return s.toLowerCase().replace(/[àáâä]/g, 'a').replace(/[èéêë]/g, 'e').replace(/[ìíîï]/g, 'i')
    .replace(/[òóôö]/g, 'o').replace(/[ùúûü]/g, 'u').replace(/ç/g, 'c').replace(/ñ/g, 'n').replace(/·/g, '');
}
function natural(a, b) {
  var ra = pla(a).match(/(\d+|\D+)/g) || [], rb = pla(b).match(/(\d+|\D+)/g) || [];
  for (var i = 0; i < Math.min(ra.length, rb.length); i++) {
    var x = ra[i], y = rb[i];
    if (x === y) continue;
    if (/^\d/.test(x) && /^\d/.test(y)) return parseInt(x, 10) - parseInt(y, 10);
    return x < y ? -1 : 1;
  }
  return ra.length - rb.length;
}
function dos(n) { return n < 10 ? '0' + n : '' + n; }
function durada(s) {
  if (!isFinite(s) || s < 0) return '';
  s = Math.floor(s);
  var h = Math.floor(s / 3600), m = Math.floor(s % 3600 / 60), x = s % 60;
  return (h ? h + ':' + dos(m) : m) + ':' + dos(x);
}
function mida(b) {
  if (b < 1024 * 1024) return Math.round(b / 1024) + ' kB';
  if (b < 1024 * 1024 * 1024) return (b / 1048576).toFixed(1) + ' MB';
  return (b / 1073741824).toFixed(2) + ' GB';
}
function barreja(a) {
  for (var i = a.length - 1; i > 0; i--) { var j = Math.floor(Math.random() * (i + 1)); var t = a[i]; a[i] = a[j]; a[j] = t; }
  return a;
}
function ls(k, v) {
  try {
    if (arguments.length > 1) { localStorage.setItem('casa.' + k, JSON.stringify(v)); return v; }
    var r = localStorage.getItem('casa.' + k);
    return r ? JSON.parse(r) : null;
  } catch (e) { return null; }
}
var avisT = null;
function avis(t, ms) {
  var a = $('avis');
  if (!a) { a = document.createElement('div'); a.id = 'avis'; document.body.appendChild(a); }
  a.textContent = t; a.style.display = 'block';
  clearTimeout(avisT);
  avisT = setTimeout(function () { a.style.display = 'none'; }, ms || 3000);
}

/* ---------- Xarxa ---------- */
function peticio(metode, url, cos, ok, ko, prog) {
  var x = new XMLHttpRequest();
  x.open(metode, url, true);
  if (url.indexOf('/api/admin/') === 0) { var c = ls('clau'); if (c) x.setRequestHeader('X-Clau', c); }
  if (prog && x.upload) x.upload.onprogress = function (e) { if (e.lengthComputable) prog(e.loaded / e.total); };
  x.onload = function () {
    if (x.status >= 200 && x.status < 300) { if (ok) ok(x.responseText); }
    else if (ko) ko(x.status, x.responseText);
  };
  x.onerror = function () { if (ko) ko(0, ''); };
  x.send(cos == null ? null : cos);
  return x;
}
function getJson(url, ok, ko) {
  return peticio('GET', url, null, function (t) {
    var d;
    try { d = JSON.parse(t); } catch (e) { if (ko) ko(-1, t); return; }
    ok(d);
  }, ko);
}
function getText(url, ok, ko) { return peticio('GET', url, null, ok, ko); }
function post(url, cos, ok, ko, prog) { return peticio('POST', url, cos, ok, ko, prog); }
function postJson(url, obj, ok, ko) {
  return post(url, JSON.stringify(obj), function (t) { var d = null; try { d = JSON.parse(t); } catch (e) { } if (ok) ok(d); }, ko);
}
function errXarxa(que) {
  return function (st) { avis('No s\'ha pogut ' + que + (st > 0 ? ' (error ' + st + ')' : st === 0 ? ' (sense connexió amb el servidor)' : '')); };
}
function llistaCarpeta(c, p, ok) {
  getJson(api('llista', { c: c, p: p }), function (d) {
    d.carpetes.sort(function (a, b) { return natural(a.n, b.n); });
    d.fitxers.sort(function (a, b) { return natural(a.n, b.n); });
    ok(d);
  }, function (st) {
    if (st == 404 && !p) ok({ carpetes: [], fitxers: [], noExisteix: true });
    else errXarxa('obrir la carpeta')(st);
  });
}
function dades(n, ok) { getJson(api('dades', { n: n }), function (d) { ok(d); }, function () { ok(null); }); }
function desaDades(n, v, ok) { post(api('dades', { n: n }), JSON.stringify(v), ok || null, errXarxa('desar')); }
function canals(ok) {
  dades('canals', function (d) {
    if (!d || !d.length) { ok(CANALS_DEF.slice()); return; }
    /* Els canals de 3Cat desats es posen al dia amb les adreces noves */
    for (var i = 0; i < d.length; i++) {
      if (d[i].propi) continue;
      for (var j = 0; j < CANALS_DEF.length; j++) if (CANALS_DEF[j].id == d[i].id) { d[i].urls = CANALS_DEF[j].urls; d[i].nom = CANALS_DEF[j].nom; }
    }
    ok(d);
  });
}
function camiHtml(arrel, cami, fn) {
  var h = '<span onclick="' + fn + '(\'\')">' + esc(arrel) + '</span>';
  if (cami) {
    var parts = cami.split('/'), acc = '';
    for (var i = 0; i < parts.length; i++) {
      acc = uneix(acc, parts[i]);
      h += ' › ' + (i == parts.length - 1 ? '<b>' + esc(parts[i]) + '</b>' : '<span onclick="' + fn + '(' + js(acc) + ')">' + esc(parts[i]) + '</span>');
    }
  }
  return h;
}

/* ---------- Icones (SVG) ---------- */
function svg(d, cls) { return '<svg class="' + (cls || 'ic') + '" viewBox="0 0 24 24"><path d="' + d + '"/></svg>'; }
var IC = {
  play: 'M7 4.5v15a1 1 0 0 0 1.5.86l12.4-7.5a1 1 0 0 0 0-1.72L8.5 3.64A1 1 0 0 0 7 4.5z',
  pausa: 'M6 4h4.2v16H6zM13.8 4H18v16h-4.2z',
  ant: 'M6 5h2.2v14H6zM9.6 12L19 5.3v13.4z',
  seg: 'M15.8 5H18v14h-2.2zM5 5.3l9.4 6.7L5 18.7z',
  enrere: 'M12.5 3a9 9 0 1 1-8.95 10h2.02A7 7 0 1 0 12.5 5v3l-5-4 5-4z',
  endavant: 'M11.5 3a9 9 0 1 0 8.95 10h-2.02A7 7 0 1 1 11.5 5v3l5-4-5-4z',
  tanca: 'M19 6.41L17.59 5 12 10.59 6.41 5 5 6.41 10.59 12 5 17.59 6.41 19 12 13.41 17.59 19 19 17.59 13.41 12z',
  casa: 'M10 20v-6h4v6h5v-8h3L12 3 2 12h3v8z',
  pelis: 'M18 4l2 4h-3l-2-4h-2l2 4h-3l-2-4H8l2 4H7L5 4H4c-1.1 0-1.99.9-1.99 2L2 18c0 1.1.9 2 2 2h16c1.1 0 2-.9 2-2V4h-4z',
  tv: 'M21 3H3c-1.1 0-2 .9-2 2v12c0 1.1.9 2 2 2h5v2h8v-2h5c1.1 0 1.99-.9 1.99-2L23 5c0-1.1-.9-2-2-2zm0 14H3V5h18v12z',
  musica: 'M12 3v10.55c-.59-.34-1.27-.55-2-.55-2.21 0-4 1.79-4 4s1.79 4 4 4 4-1.79 4-4V7h4V3h-6z',
  radio: 'M3.24 6.15C2.51 6.43 2 7.17 2 8v12c0 1.1.89 2 2 2h16c1.11 0 2-.9 2-2V8c0-1.11-.89-2-2-2H8.3l8.26-3.34L15.88 1 3.24 6.15zM7 20c-1.66 0-3-1.34-3-3s1.34-3 3-3 3 1.34 3 3-1.34 3-3 3zm13-8h-2v-2h-2v2H4V8h16v4z',
  podcasts: 'M12 14c1.66 0 2.99-1.34 2.99-3L15 5c0-1.66-1.34-3-3-3S9 3.34 9 5v6c0 1.66 1.34 3 3 3zm5.3-3c0 3-2.54 5.1-5.3 5.1S6.7 14 6.7 11H5c0 3.41 2.72 6.23 6 6.72V21h2v-3.28c3.28-.48 6-3.3 6-6.72h-1.7z',
  fotos: 'M21 19V5c0-1.1-.9-2-2-2H5c-1.1 0-2 .9-2 2v14c0 1.1.9 2 2 2h14c1.1 0 2-.9 2-2zM8.5 13.5l2.5 3.01L14.5 12l4.5 6H5l3.5-4.5z',
  docs: 'M14 2H6c-1.1 0-1.99.9-1.99 2L4 20c0 1.1.89 2 1.99 2H18c1.1 0 2-.9 2-2V8l-6-6zm2 16H8v-2h8v2zm0-4H8v-2h8v2zm-3-5V3.5L18.5 9H13z',
  config: 'M19.14 12.94c.04-.3.06-.61.06-.94 0-.32-.02-.64-.07-.94l2.03-1.58a.49.49 0 0 0 .12-.61l-1.92-3.32a.49.49 0 0 0-.59-.22l-2.39.96c-.5-.38-1.03-.7-1.62-.94l-.36-2.54a.48.48 0 0 0-.48-.41h-3.84c-.24 0-.43.17-.47.41l-.36 2.54c-.59.24-1.13.57-1.62.94l-2.39-.96c-.22-.08-.47 0-.59.22L2.74 8.87c-.12.21-.08.47.12.61l2.03 1.58c-.05.3-.09.63-.09.94s.02.64.07.94l-2.03 1.58a.49.49 0 0 0-.12.61l1.92 3.32c.12.22.37.29.59.22l2.39-.96c.5.38 1.03.7 1.62.94l.36 2.54c.05.24.24.41.48.41h3.84c.24 0 .44-.17.47-.41l.36-2.54c.59-.24 1.13-.56 1.62-.94l2.39.96c.22.08.47 0 .59-.22l1.92-3.32c.12-.22.07-.47-.12-.61l-2.01-1.58zM12 15.6c-1.98 0-3.6-1.62-3.6-3.6s1.62-3.6 3.6-3.6 3.6 1.62 3.6 3.6-1.62 3.6-3.6 3.6z',
  marc: 'M4 4h7V2H4c-1.1 0-2 .9-2 2v7h2V4zm6 9l-4 5h12l-3-4-2.03 2.71L10 13zm7-4.5c0-.83-.67-1.5-1.5-1.5S14 7.67 14 8.5s.67 1.5 1.5 1.5S17 9.33 17 8.5zM20 2h-7v2h7v7h2V4c0-1.1-.9-2-2-2zm0 18h-7v2h7c1.1 0 2-.9 2-2v-7h-2v7zM4 13H2v7c0 1.1.9 2 2 2h7v-2H4v-7z',
  gran: 'M7 14H5v5h5v-2H7v-3zm-2-4h2V7h3V5H5v5zm12 7h-3v2h5v-5h-2v3zM14 5v2h3v3h2V5h-5z',
  petit: 'M5 16h3v3h2v-5H5v2zm3-8H5v2h5V5H8v3zm6 11h2v-3h3v-2h-5v5zm2-11V5h-2v5h5V8h-3z',
  carpeta: 'M10 4H4c-1.1 0-1.99.9-1.99 2L2 18c0 1.1.9 2 2 2h16c1.1 0 2-.9 2-2V8c0-1.1-.9-2-2-2h-8l-2-2z',
  disc: 'M6 2h12a2 2 0 0 1 2 2v16a2 2 0 0 1-2 2H6a2 2 0 0 1-2-2V4a2 2 0 0 1 2-2zm6 4a5 5 0 1 0 0 10 5 5 0 0 0 0-10zm0 3.5a1.5 1.5 0 1 1 0 3 1.5 1.5 0 0 1 0-3z',
  estrella: 'M12 17.27L18.18 21l-1.64-7.03L22 9.24l-7.19-.61L12 2 9.19 8.63 2 9.24l5.46 4.73L5.82 21z',
  estrellaBuida: 'M22 9.24l-7.19-.62L12 2 9.19 8.63 2 9.24l5.46 4.73L5.82 21 12 17.27 18.18 21l-1.63-7.03L22 9.24zM12 15.4l-3.76 2.27 1-4.28-3.32-2.88 4.38-.38L12 6.1l1.71 4.04 4.38.38-3.32 2.88 1 4.28L12 15.4z',
  puja: 'M9 16h6v-6h4l-7-7-7 7h4zm-4 2h14v2H5z',
  mes: 'M19 13h-6v6h-2v-6H5v-2h6V5h2v6h6v2z',
  paperera: 'M6 19c0 1.1.9 2 2 2h8c1.1 0 2-.9 2-2V7H6v12zM19 4h-3.5l-1-1h-5l-1 1H5v2h14V4z',
  spotify: 'M20 2H8c-1.1 0-2 .9-2 2v12c0 1.1.9 2 2 2h12c1.1 0 2-.9 2-2V4c0-1.1-.9-2-2-2zm-2 5h-3v5.5c0 1.38-1.12 2.5-2.5 2.5S10 13.88 10 12.5s1.12-2.5 2.5-2.5c.57 0 1.08.19 1.5.51V5h4v2zM4 6H2v14c0 1.1.9 2 2 2h14v-2H4V6z',
  cerca: 'M15.5 14h-.79l-.28-.27A6.47 6.47 0 0 0 16 9.5 6.5 6.5 0 1 0 9.5 16c1.61 0 3.09-.59 4.23-1.57l.27.28v.79l5 4.99L20.49 19l-4.99-5zm-6 0C7.01 14 5 11.99 5 9.5S7.01 5 9.5 5 14 7.01 14 9.5 11.99 14 9.5 14z',
  video: 'M21 3H3c-1.11 0-2 .89-2 2v12c0 1.1.89 2 2 2h5v2h8v-2h5c1.1 0 1.99-.9 1.99-2L23 5c0-1.11-.9-2-2-2zm0 14H3V5h18v12zM16 11l-7 4V7z',
  missatges: 'M20 2H4c-1.1 0-2 .9-2 2v18l4-4h14c1.1 0 2-.9 2-2V4c0-1.1-.9-2-2-2zM7 9h10v2H7zm0-3h10v2H7zm0 6h7v2H7z',
  web: 'M11.99 2C6.47 2 2 6.48 2 12s4.47 10 9.99 10C17.52 22 22 17.52 22 12S17.52 2 11.99 2zm6.93 6h-2.95a15.65 15.65 0 0 0-1.38-3.56A8.03 8.03 0 0 1 18.92 8zM12 4.04c.83 1.2 1.48 2.53 1.91 3.96h-3.82c.43-1.43 1.08-2.76 1.91-3.96zM4.26 14C4.1 13.36 4 12.69 4 12s.1-1.36.26-2h3.38c-.08.66-.14 1.32-.14 2 0 .68.06 1.34.14 2H4.26zm.82 2h2.95c.32 1.25.78 2.45 1.38 3.56A7.99 7.99 0 0 1 5.08 16zm2.95-8H5.08a7.99 7.99 0 0 1 4.33-3.56A15.65 15.65 0 0 0 8.03 8zM12 19.96c-.83-1.2-1.48-2.53-1.91-3.96h3.82c-.43 1.43-1.08 2.76-1.91 3.96zM14.34 14H9.66c-.09-.66-.16-1.32-.16-2 0-.68.07-1.35.16-2h4.68c.09.65.16 1.32.16 2 0 .68-.07 1.34-.16 2zm.25 5.56c.6-1.11 1.06-2.31 1.38-3.56h2.95a8.03 8.03 0 0 1-4.33 3.56zM16.36 14c.08-.66.14-1.32.14-2 0-.68-.06-1.34-.14-2h3.38c.16.64.26 1.31.26 2s-.1 1.36-.26 2h-3.38z',
  aleatori: 'M10.59 9.17L5.41 4 4 5.41l5.17 5.17 1.42-1.41zM14.5 4l2.04 2.04L4 18.59 5.41 20 17.96 7.46 20 9.5V4h-5.5zm.33 9.41l-1.41 1.41 3.13 3.13L14.5 20H20v-5.5l-2.04 2.04-3.13-3.13z'
};

/* ---------- Comú a totes les apps ---------- */
function dinsCasa(app) {
  /* Si s'obre una app sola (sense el dashboard), torna al dashboard amb l'app oberta */
  if (!Casa) { location.replace('/#' + app); return false; }
  return true;
}
document.addEventListener('touchstart', function () { if (Casa) Casa.toc(); }, true);
/* Safari 9: les zones amb desplaçament tàctil a vegades queden «mortes»; tornar-les a crear ho arregla */
window.casaDesperta = function () {
  var ids = ['pag', 'area', 'esq', 'msgs'];
  for (var i = 0; i < ids.length; i++) (function (p) {
    if (!p) return;
    var y = p.scrollTop;
    p.style.webkitOverflowScrolling = 'auto';
    p.style.overflowY = 'hidden';
    void p.offsetHeight;
    setTimeout(function () { p.style.overflowY = ''; p.style.webkitOverflowScrolling = ''; p.scrollTop = y; }, 30);
  })($(ids[i]));
};
window.addEventListener('orientationchange', function () { setTimeout(window.casaDesperta, 300); });
document.addEventListener('mousedown', function () { if (Casa) Casa.toc(); }, true);

/* ---------- Imatges mandroses: només es carreguen les visibles, 4 alhora ---------- */
var MAND = { cua: [], actius: 0, t: null };
function mandros() {
  var sc = $('pag');
  if (!sc) return;
  var h = sc.clientHeight || window.innerHeight, w = window.innerWidth || 1024, els = sc.querySelectorAll('[data-bg]');
  for (var i = 0; i < els.length; i++) {
    var e = els[i];
    if (e.getAttribute('data-estat')) continue;
    var r = e.getBoundingClientRect();
    if (r.bottom > -300 && r.top < h + 500 && r.width > 0 && r.right > -300 && r.left < w + 600) { e.setAttribute('data-estat', 'cua'); MAND.cua.push(e); }
  }
  mandSeg();
}
function mandSeg() {
  while (MAND.actius < 4 && MAND.cua.length) {
    (function (e) {
      MAND.actius++;
      var u = e.getAttribute('data-bg'), im = new Image();
      im.onload = function () {
        MAND.actius--;
        e.style.backgroundImage = bg(u);
        e.setAttribute('data-estat', 'ok');
        var l = e.getElementsByClassName('lletra')[0];
        if (l) l.style.display = 'none';
        mandSeg();
      };
      im.onerror = function () {
        MAND.actius--;
        /* el servidor pot estar ocupat buscant (o el lloc ens demana calma): es torna a provar un parell de cops */
        var n = +(e.getAttribute('data-intents') || 0);
        if (n < 2) {
          e.setAttribute('data-intents', n + 1);
          e.setAttribute('data-bg', u.replace(/([?&])r=\d+&?/, '$1').replace(/[?&]$/, '') + (u.indexOf('?') >= 0 ? '&' : '?') + 'r=' + (n + 1));
          setTimeout(function () { if (e.parentNode) { MAND.cua.push(e); mandSeg(); } }, 5000 + n * 5000);
        } else e.setAttribute('data-estat', 'err');
        mandSeg();
      };
      im.src = u;
    })(MAND.cua.shift());
  }
}
function iniciaMandros() {
  var sc = $('pag');
  if (!sc) return;
  sc.addEventListener('scroll', function () {
    if (MAND.t) return;
    MAND.t = setTimeout(function () { MAND.t = null; mandros(); }, 150);
  }, false);
  window.addEventListener('resize', function () { setTimeout(mandros, 100); }, false);
}

/* ---------- Dispositiu (cada tauleta pot tenir la seva configuració) ---------- */
function galeta(n) { var m = new RegExp('(?:^|; )' + n + '=([^;]*)').exec(document.cookie); return m ? decodeURIComponent(m[1]) : ''; }
function desaGaleta(n, v) { try { document.cookie = n + '=' + encodeURIComponent(v) + '; expires=Fri, 31 Dec 2037 23:59:59 GMT; path=/'; } catch (e) { } }
var DISP = (function () {
  /* l'identificador es guarda a dos llocs (emmagatzematge local i una galeta), per si se n'esborra un */
  var id = ls('disp') || galeta('hdisp');
  if (!id || !/^[a-z0-9]{6,16}$/.test(id)) { id = ''; for (var i = 0; i < 10; i++) id += 'abcdefghijklmnopqrstuvwxyz0123456789'.charAt(Math.floor(Math.random() * 36)); ls('disp', id); }
  desaGaleta('hdisp', id); if (ls('disp') != id) ls('disp', id);
  return { id: id };
})();
function nomDispositiuDefecte() {
  var u = navigator.userAgent, m;
  if (/iPad/.test(u)) { m = u.match(/OS (\d+)_/); return 'iPad' + (m ? ' (iOS ' + m[1] + ')' : ''); }
  if (/iPhone/.test(u)) return 'iPhone';
  if (/Macintosh/.test(u) && navigator.maxTouchPoints > 1) return 'iPad';
  if (/Android/.test(u)) return /Mobile/.test(u) ? 'Android (mòbil)' : 'Android (tauleta)';
  if (/Windows/.test(u)) return 'PC';
  if (/Macintosh/.test(u)) return 'Mac';
  return 'Navegador';
}
/* Ajustos que poden ser propis de cada dispositiu */
var AJ_DISP = ['estil', 'foscor', 'fonsSeg', 'marcSeg', 'marcMin', 'targeta', 'nom', 'passi', 'nit', 'nitDes', 'nitFins', 'lleuger', 'fonsPassi', 'tele', 'retard', 'despert', 'moll', 'fotosAmaga'];
/* Llegeix els ajustos generals i hi aplica els d'aquest dispositiu (si en té) */
function ajustosDispositiu(ok) {
  dades('ajustos', function (g) {
    dades('disp_' + DISP.id, function (p) {
      var a = {}, k;
      if (g) for (k in g) if (g.hasOwnProperty(k)) a[k] = g[k];
      if (p && p.propi && p.ajustos) for (k in p.ajustos) if (p.ajustos.hasOwnProperty(k)) a[k] = p.ajustos[k];
      /* el mode tele és sempre només d'aquest aparell (mai dels ajustos generals) */
      a.tele = !!(p && p.tele === true);
      /* i el que és sempre d'aquest aparell */
      var nomes = ['retard', 'despert', 'moll', 'fotosAmaga'];
      for (var n = 0; n < nomes.length; n++) { delete a[nomes[n]]; if (p && p[nomes[n]] !== undefined) a[nomes[n]] = p[nomes[n]]; }
      ok(a, g, p);
    });
  });
}
/* Registra el dispositiu (nom i darrera connexió) perquè surti a Configuració.
   Un mateix aparell es reconeix per la seva adreça a la xarxa i el seu navegador: si s'obre Hearth amb una
   altra adreça (IP o hearth.local) o se n'esborren les dades, no surt duplicat, sinó que torna a ser el mateix.
   ok(d, canviat): canviat = true si aquest aparell ha recuperat el seu identificador d'abans */
function registraDispositiu(ok) {
  getJson(api('jo', { _: Date.now() }), function (jo) { registraAmb(jo && jo.ip, ok); }, function () { registraAmb('', ok); });
}
function registraAmb(ip, ok) {
  dades('dispositius', function (d) {
    d = d || {};
    var ua = navigator.userAgent.substring(0, 160), k, canviat = false;
    /* el mateix aparell amb un altre identificador: es queda el més antic (és el que té els ajustos) */
    if (ip) {
      var millor = d[DISP.id] && d[DISP.id].ip == ip ? DISP.id : '', dups = [];
      for (k in d) if (d.hasOwnProperty(k) && k != DISP.id && d[k].ua == ua && (d[k].ip == ip || !d[k].ip)) dups.push(k);
      if (dups.length) {
        dups.push(DISP.id);
        dups.sort(function (a, b) { return (d[a] ? d[a].des || d[a].vist || 0 : 9e15) - (d[b] ? d[b].des || d[b].vist || 0 : 9e15); });
        millor = dups[0];
        for (var i = 1; i < dups.length; i++) {
          var x = d[dups[i]];
          if (x && x.nom && d[millor] && !d[millor].nomPropi && x.nomPropi) { d[millor].nom = x.nom; d[millor].nomPropi = 1; }
          delete d[dups[i]];
        }
        if (millor != DISP.id) { DISP.id = millor; ls('disp', millor); desaGaleta('hdisp', millor); canviat = true; }
      }
    }
    var e = d[DISP.id] || {};
    e.nom = e.nom || nomDispositiuDefecte();
    if (!e.des) e.des = new Date().getTime();
    e.vist = new Date().getTime();
    e.ua = ua;
    if (ip) e.ip = ip;
    d[DISP.id] = e;
    DISP.nom = e.nom;
    post(api('dades', { n: 'dispositius' }), JSON.stringify(d), function () { if (ok) ok(d, canviat); }, function () { });
  });
}
/* El mode infantil és de cada dispositiu (si encara no en té, es fa servir el general d'abans) */
function dadesInf(ok) { dades('inf_' + DISP.id, function (d) { if (d) ok(d); else dades('infantil', ok); }); }
function desaInf(v, ok) { desaDades('inf_' + DISP.id, v, ok); }
/* A l'ordinador, la roda del ratolí desplaça de costat les files de pestanyes */
(function () {
  if (!document.addEventListener) return;
  document.addEventListener('wheel', function (e) {
    if (!e.deltaY || e.deltaX) return;
    var el = e.target;
    while (el && el.nodeType == 1) {
      if (el.scrollWidth > el.clientWidth + 4 && el.scrollHeight <= el.clientHeight + 4) {
        var s = getComputedStyle(el).overflowX;
        if (s == 'auto' || s == 'scroll') { el.scrollLeft += e.deltaY; e.preventDefault(); return; }
      }
      el = el.parentNode;
    }
  }, { passive: false });
})();

/* ---------- Mode tele: navegar amb les fletxes del comandament (i l'OK / enrere) ----------
   Per a la tele (Hearth al PC projectat amb Miracast, o al navegador de la tele). Funciona a la pantalla
   principal i a dins de cada app; quan no hi ha res més en una direcció, salta d'una a l'altra. */
var TELE = (function () {
  var actiu = false, perUrl = false, cur = null;
  try { perUrl = /[?&]tele=1/.test(location.search) || /[?&]tele=1/.test(window.top.location.search); } catch (e) { }
  actiu = perUrl || ls('modeTele') === true;
  function visible(el) {
    if (!el || !el.getBoundingClientRect) return false;
    var r = el.getBoundingClientRect();
    if (r.width < 4 || r.height < 4) return false;
    if (r.bottom < 0 || r.right < 0 || r.top > (window.innerHeight || 0) + 400 || r.left > (window.innerWidth || 0)) return false;
    var s = window.getComputedStyle(el);
    return s.visibility != 'hidden' && s.display != 'none' && el.offsetParent !== null && !el.disabled;
  }
  function candidats() {
    var l = document.querySelectorAll('button, a[href], input, select, textarea, [onclick], [tabindex]'), r = [];
    /* si hi ha una finestra emergent oberta, només el que hi ha a dins */
    var modal = null, ms = document.querySelectorAll('#tria, #cast, #pin, .modal');
    for (var j = 0; j < ms.length; j++) if (visible(ms[j]) || (ms[j].style.display == 'block')) modal = ms[j];
    for (var i = 0; i < l.length; i++) if (visible(l[i]) && (!modal || modal.contains(l[i]))) r.push(l[i]);
    return r;
  }
  function centre(el) { var b = el.getBoundingClientRect(); return { x: b.left + b.width / 2, y: b.top + b.height / 2, b: b }; }
  function marca(el) {
    if (cur && cur.classList) cur.classList.remove('tele-focus');
    cur = el;
    if (!el) return;
    el.classList.add('tele-focus');
    try { el.scrollIntoView({ block: 'nearest', inline: 'nearest' }); } catch (e) { try { el.scrollIntoView(false); } catch (x) { } }
    if (/^(INPUT|SELECT|TEXTAREA)$/.test(el.tagName)) { try { el.focus(); } catch (e) { } }
  }
  function mou(dx, dy, des) {
    var l = candidats();
    if (!l.length) return false;
    var o = des ? null : (cur && document.body.contains(cur) && visible(cur) ? centre(cur) : null), millor = null, mp = 1e9;
    if (!o) {
      /* entrant des d'una altra part (o el primer cop): el més proper a la vora per on s'entra */
      var W = window.innerWidth, H = window.innerHeight;
      o = { x: des && des.x != null ? des.x : (dx > 0 ? -1 : dx < 0 ? W + 1 : W / 2), y: dy > 0 ? -1 : dy < 0 ? H + 1 : (des && des.y != null ? des.y : H / 2) };
    }
    for (var i = 0; i < l.length; i++) {
      if (l[i] == cur) continue;
      var c = centre(l[i]), px = (c.x - o.x) * dx + (c.y - o.y) * dy, sx = Math.abs((c.x - o.x) * dy) + Math.abs((c.y - o.y) * dx);
      if (px <= 2) continue;
      var p = px + sx * 2.2;
      if (p < mp) { mp = p; millor = l[i]; }
    }
    if (millor) { marca(millor); return true; }
    return false;
  }
  function frameActiu() {
    var fs = document.querySelectorAll('iframe');
    for (var i = 0; i < fs.length; i++) if (visible(fs[i])) return fs[i];
    return null;
  }
  function entra(dx, dy, x, y) { if (cur) { cur.classList.remove('tele-focus'); cur = null; } return mou(dx, dy, { x: x, y: y }); }
  function tecla(e) {
    if (!actiu) return;
    var k = e.keyCode || e.which, dx = 0, dy = 0;
    if (k == 37) dx = -1; else if (k == 39) dx = 1; else if (k == 38) dy = -1; else if (k == 40) dy = 1;
    var t = e.target, escrivint = t && (/^(INPUT|TEXTAREA)$/.test(t.tagName) && !/^(checkbox|radio|range|button)$/.test(t.type));
    if (dx || dy) {
      if (escrivint && dx) return;
      e.preventDefault();
      if (mou(dx, dy)) return;
      /* res més en aquesta direcció: passa a la pantalla de fora (si som una app) o a l'app oberta */
      var o = cur ? centre(cur) : { x: 0, y: 0 };
      try {
        if (window.parent !== window && window.parent.TELE) {
          var fr = window.frameElement.getBoundingClientRect();
          if (cur) cur.classList.remove('tele-focus'), cur = null;
          window.parent.TELE.entra(dx, dy, fr.left + o.x, fr.top + o.y);
          window.parent.focus();
        } else {
          var f = frameActiu();
          if (f && f.contentWindow.TELE) { var fb = f.getBoundingClientRect(); if (f.contentWindow.TELE.entra(dx, dy, o.x - fb.left, o.y - fb.top)) { if (cur) cur.classList.remove('tele-focus'), cur = null; f.contentWindow.focus(); } }
        }
      } catch (x) { }
      return;
    }
    if ((k == 13 || k == 32) && cur && !escrivint) { e.preventDefault(); if (/^(SELECT)$/.test(cur.tagName)) { try { cur.focus(); } catch (x) { } } else cur.click(); return; }
    /* Enrere: Esc, Retrocés (si no s'escriu), o la tecla enrere del navegador/comandament */
    if (k == 27 || (k == 8 && !escrivint) || k == 166 || k == 461 || k == 10009) {
      e.preventDefault();
      var ms = document.querySelectorAll('#tria, #cast');
      for (var i = 0; i < ms.length; i++) if (ms[i].style.display == 'block') { ms[i].style.display = 'none'; return; }
      try {
        var top = window.top;
        if (top.navInici && top.A && top.A.vista != 'dash') { top.navInici(); top.focus(); if (top.TELE) top.TELE.entra(0, 1, top.innerWidth / 2, 0); }
      } catch (x) { }
    }
  }
  if (actiu) {
    var st = document.createElement('style');
    st.textContent = '.tele-focus{outline:4px solid #0a84ff !important;outline-offset:2px;box-shadow:0 0 0 8px rgba(10,132,255,.35) !important;border-radius:10px;position:relative;z-index:5}';
    (document.head || document.documentElement).appendChild(st);
    document.addEventListener('keydown', tecla, true);
  }
  return { actiu: actiu, perUrl: perUrl, entra: entra, mou: mou };
})();
