/* Hearth: service worker mínim perquè es pugui instal·lar com a app (Chrome/Edge/Android amb HTTPS o localhost).
   Sempre va primer a la xarxa; la memòria cau només serveix per obrir l'app si el servidor no respon. */
var CAU = 'hearth-v1', BASE = ['/', '/index.html', '/comu.css', '/comu.js', '/i18n.js', '/icona.png'];
self.addEventListener('install', function (e) { e.waitUntil(caches.open(CAU).then(function (c) { return c.addAll(BASE); }).catch(function () { })); self.skipWaiting(); });
self.addEventListener('activate', function (e) { e.waitUntil(caches.keys().then(function (k) { return Promise.all(k.filter(function (n) { return n != CAU; }).map(function (n) { return caches.delete(n); })); })); self.clients.claim(); });
self.addEventListener('fetch', function (e) {
  var r = e.request;
  if (r.method != 'GET' || r.url.indexOf('/api/') >= 0) return;
  e.respondWith(fetch(r).then(function (res) {
    if (res.ok && BASE.indexOf(new URL(r.url).pathname) >= 0) { var cp = res.clone(); caches.open(CAU).then(function (c) { c.put(r, cp); }); }
    return res;
  }).catch(function () { return caches.match(r).then(function (m) { return m || caches.match('/'); }); }));
});
