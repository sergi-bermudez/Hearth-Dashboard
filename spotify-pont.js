// Pont entre el Servidor Casa i la web de Spotify (s'executa dins l'Edge del servidor).
// Si Spotify canvia la web i alguna cosa deixa de funcionar, normalment n'hi ha prou
// d'ajustar els selectors "data-testid" d'aquest fitxer (no cal tocar res més).
if (!window.__casa) (function () {
  const media = new Set();
  let sinkId = null;
  let darrerError = '';
  const aplica = (el) => { if (sinkId !== null && el.setSinkId && el.sinkId !== sinkId) el.setSinkId(sinkId).catch((e) => { darrerError = String(e && e.message || e); }); };
  const playOriginal = HTMLMediaElement.prototype.play;
  HTMLMediaElement.prototype.play = function () { media.add(this); aplica(this); return playOriginal.apply(this, arguments); };
  // L'Spotify també pot sonar amb Web Audio: cal desviar també els AudioContext
  const ctxs = new Set();
  const aplicaCtx = (c) => { if (sinkId !== null && c.setSinkId && c.sinkId !== sinkId) c.setSinkId(sinkId).catch(() => {}); };
  ['AudioContext', 'webkitAudioContext'].forEach((n) => {
    const Orig = window[n];
    if (!Orig) return;
    const Nou = function (...a) { const c = new Orig(...a); ctxs.add(c); aplicaCtx(c); return c; };
    Nou.prototype = Orig.prototype;
    try { window[n] = Nou; } catch (e) {}
  });
  const totsMedia = () => { document.querySelectorAll('audio,video').forEach((e) => media.add(e)); return media; };

  const q = (s) => document.querySelector(s);
  const txt = (s) => { const e = q(s); return e ? e.textContent.trim() : ''; };
  const segons = (t) => { if (!t) return 0; return t.split(':').map(Number).reduce((a, b) => a * 60 + b, 0); };
  const espera = (ms) => new Promise((r) => setTimeout(r, ms));
  const img = (imgs) => {
    if (!imgs || !imgs.length) return '';
    let tria = imgs[0];
    for (const i of imgs) if (i && i.width && i.width >= 200 && i.width <= 400) tria = i;
    return tria.url;
  };

  window.__casa = {
    estat() {
      const ms = navigator.mediaSession, m = ms && ms.metadata;
      const w = q('[data-testid="now-playing-widget"]');
      let art = '';
      if (m && m.artwork && m.artwork.length) {
        art = m.artwork[0].src;
        for (const a of m.artwork) if (a.sizes && /^(2|3)\d\dx/.test(a.sizes)) art = a.src;
      } else if (w) { const i = w.querySelector('img'); if (i) art = i.src; }
      const pp = q('[data-testid="control-button-playpause"]');
      let sona = ms ? ms.playbackState === 'playing' : false;
      if ((!ms || ms.playbackState === 'none') && pp) sona = /pause|pausa|atur/i.test(pp.getAttribute('aria-label') || '');
      return {
        sessio: !q('[data-testid="login-button"]'),
        titol: (m && m.title) || txt('[data-testid="context-item-info-title"]') || txt('[data-testid="context-item-link"]'),
        artista: (m && m.artist) || txt('[data-testid="context-item-info-artist"]'),
        album: (m && m.album) || '',
        art: art,
        sona: sona,
        pos: segons(txt('[data-testid="playback-position"]')),
        dur: segons(txt('[data-testid="playback-duration"]')),
        aleatori: (q('[data-testid="control-button-shuffle"]') || {}).getAttribute ? q('[data-testid="control-button-shuffle"]').getAttribute('aria-checked') === 'true' : false,
        anunci: !!q('[data-testid="context-item-info-ad-subtitle"]') || /anunci|advert/i.test(txt('[data-testid="now-playing-widget"]')),
        reproductor: !!pp
      };
    },

    ordre(o) {
      const mapa = { playpausa: 'control-button-playpause', seg: 'control-button-skip-forward', ant: 'control-button-skip-back',
        aleatori: 'control-button-shuffle', repeteix: 'control-button-repeat' };
      if (o === 'pausa' || o === 'play') {
        const sona = navigator.mediaSession && navigator.mediaSession.playbackState === 'playing';
        if ((o === 'pausa') !== sona) return { ok: true };
        o = 'playpausa';
      }
      const b = q('[data-testid="' + mapa[o] + '"]');
      if (!b) return { ok: false, error: 'No trobo el botó' };
      b.click();
      return { ok: true };
    },

    // L'API pública (api.spotify.com) limita molt els tokens del reproductor web (error 429).
    // Per això la cerca i la biblioteca es llegeixen de les respostes que la mateixa web rep
    // (fetch interceptat) o, si no hi són, de la pàgina. L'API només és l'últim recurs.
    async api(token, cami) {
      if (Date.now() < (window.__casaEspera || 0)) throw new Error('Spotify demana esperar una estona');
      const r = await fetch('https://api.spotify.com/v1/' + cami, { headers: { authorization: 'Bearer ' + token } });
      if (r.status === 429) {
        const s = parseInt(r.headers.get('retry-after') || '60', 10);
        window.__casaEspera = Date.now() + Math.min(Math.max(s, 30), 3600) * 1000;
        throw new Error('Spotify demana esperar ' + Math.round(s / 60) + ' min');
      }
      if (!r.ok) throw new Error('Spotify ha respost ' + r.status);
      return r.json();
    },

    // Recorre qualsevol JSON i en treu cançons, àlbums, llistes i artistes (no depèn de l'estructura exacta)
    extreu(json, o) {
      o = o || { cancons: [], albums: [], llistes: [], artistes: [] };
      const vist = new Set([].concat(o.cancons, o.albums, o.llistes, o.artistes).map((x) => x.uri));
      const fonts = (n) => {
        let best = '';
        const mira = (x, d) => {
          if (!x || typeof x !== 'object' || d > 4) return;
          if (Array.isArray(x.sources) && x.sources.length) {
            let t = x.sources[0];
            for (const s of x.sources) if (s && s.width && s.width >= 200 && s.width <= 400) t = s;
            if (t && t.url && !best) best = t.url;
            return;
          }
          if (Array.isArray(x.images) && x.images.length && x.images[0].url) { if (!best) best = img(x.images); return; }
          for (const k in x) if (k !== 'tracks' && k !== 'items' || d < 1) mira(x[k], d + 1);
        };
        mira(n, 0);
        return best;
      };
      const noms = (n) => {
        const a = n.artists && (n.artists.items || n.artists);
        if (Array.isArray(a)) return a.map((x) => (x.profile && x.profile.name) || x.name || '').filter(Boolean).join(', ');
        if (n.ownerV2 && n.ownerV2.data) return n.ownerV2.data.name || '';
        if (n.owner) return n.owner.display_name || n.owner.name || '';
        return '';
      };
      const camina = (n, d) => {
        if (!n || typeof n !== 'object' || d > 14) return;
        if (Array.isArray(n)) { for (const x of n) camina(x, d + 1); return; }
        const uri = typeof n.uri === 'string' ? n.uri : '';
        const m = /^spotify:(track|album|playlist|artist)/.exec(uri) || (/^spotify:user:[^:]+:playlist:/.test(uri) ? [0, 'playlist'] : null);
        const nom = n.name || (n.profile && n.profile.name) || '';
        if (m && nom && !vist.has(uri)) {
          vist.add(uri);
          const e = { t: nom, a: noms(n), img: fonts(n.albumOfTrack || n.album || n.visuals || n.coverArt || n.images || n), uri: uri };
          if (m[1] === 'track') { const ms = (n.duration && (n.duration.totalMilliseconds || n.duration)) || n.duration_ms || 0; e.dur = Math.round(ms / 1000); o.cancons.push(e); }
          else if (m[1] === 'album') o.albums.push(e);
          else if (m[1] === 'playlist') o.llistes.push(e);
          else o.artistes.push(e);
        }
        for (const k in n) if (n[k] && typeof n[k] === 'object') camina(n[k], d + 1);
      };
      camina(json, 0);
      return o;
    },

    // Llegeix de la pàgina els enllaços a cançons, àlbums, llistes i artistes
    dePagina(arrel) {
      const o = { cancons: [], albums: [], llistes: [], artistes: [] }, vist = new Set();
      for (const a of (arrel || document).querySelectorAll('a[href]')) {
        const m = /^\/(track|album|playlist|artist)\/([A-Za-z0-9]+)/.exec(a.getAttribute('href') || '');
        if (!m) continue;
        const uri = 'spotify:' + m[1] + ':' + m[2];
        const t = (a.textContent || '').trim();
        if (!t || vist.has(uri)) continue;
        vist.add(uri);
        let cont = a, im = null;
        for (let i = 0; i < 6 && cont && !im; i++) { cont = cont.parentElement; im = cont && cont.querySelector('img'); }
        let sub = '';
        if (cont) { const s = cont.querySelector('a[href^="/artist/"]'); if (s && s !== a) sub = s.textContent.trim(); }
        const e = { t: t, a: sub, img: im ? im.src : '', uri: uri };
        (m[1] === 'track' ? o.cancons : m[1] === 'album' ? o.albums : m[1] === 'playlist' ? o.llistes : o.artistes).push(e);
      }
      return o;
    },

    intercepta() {
      if (window.__casaFetch) return;
      window.__casaFetch = true;
      window.__casaResp = [];
      const f = window.fetch;
      window.fetch = function (input, init) {
        const url = typeof input === 'string' ? input : (input && input.url) || '';
        const p = f.apply(this, arguments);
        if (/pathfinder|api-partner|spclient/.test(url)) {
          p.then((r) => r.clone().json().then((j) => {
            const l = window.__casaResp;
            let op = '';
            try { op = (/operationName=([^&]+)/.exec(url) || [])[1] || (init && init.body && JSON.parse(init.body).operationName) || ''; } catch (e) {}
            l.push({ t: Date.now(), url: url, op: op, j: j });
            if (l.length > 30) l.shift();
          }).catch(() => {})).catch(() => {});
        }
        return p;
      };
    },

    async navega(cami) {
      if (location.pathname !== cami) {
        history.pushState({}, '', cami);
        dispatchEvent(new PopStateEvent('popstate', { state: {} }));
      }
    },

    async cerca(token, text) {
      this.intercepta();
      const inici = Date.now();
      await this.navega('/search/' + encodeURIComponent(text));
      let o = null;
      for (let i = 0; i < 40; i++) {
        await espera(250);
        for (const r of window.__casaResp) {
          if (r.t < inici) continue;
          if (/search/i.test(r.op + r.url) || (r.j && r.j.data && r.j.data.searchV2)) {
            o = this.extreu(r.j);
            if (o.cancons.length + o.albums.length + o.llistes.length > 0) break;
          }
        }
        if (o && o.cancons.length + o.albums.length + o.llistes.length > 0) break;
      }
      if (!o || o.cancons.length + o.albums.length + o.llistes.length === 0) {
        await espera(600);
        o = this.dePagina(q('main') || document);
      }
      if (o.cancons.length + o.albums.length + o.llistes.length + o.artistes.length === 0 && token) {
        try {
          const d = await this.api(token, 'search?type=track,album,playlist,artist&limit=12&q=' + encodeURIComponent(text));
          o = this.extreu(d);
        } catch (e) { return { error: e.message, cancons: [], albums: [], llistes: [], artistes: [] }; }
      }
      for (const k of ['cancons', 'albums', 'llistes', 'artistes']) o[k] = o[k].slice(0, 20);
      return o;
    },

    async biblioteca(token) {
      this.intercepta();
      const l = [{ t: 'Cançons que m\'agraden', a: '', img: '', uri: 'spotify:collection:tracks' }];
      // 1) Respostes de la web amb la biblioteca (es carreguen en obrir la barra lateral)
      let o = { cancons: [], albums: [], llistes: [], artistes: [] };
      for (const r of window.__casaResp) if (/library/i.test(r.op + r.url)) o = this.extreu(r.j, o);
      // 2) La barra lateral de la pàgina
      if (!o.llistes.length) {
        const b = q('nav[aria-label] [role="grid"]') || q('[data-testid="rootlist"]') || q('nav') || document;
        const d = this.dePagina(b);
        o.llistes = d.llistes; o.albums = o.albums.concat(d.albums); o.artistes = o.artistes.concat(d.artistes);
      }
      // 3) Últim recurs: l'API (només si no hem rebut cap 429 fa poc)
      if (!o.llistes.length && token) {
        try { const d = await this.api(token, 'me/playlists?limit=50'); o = this.extreu(d, o); } catch (e) { if (!o.albums.length) return { error: e.message, llistes: l }; }
      }
      return { llistes: l.concat(o.llistes, o.albums, o.artistes) };
    },

    cami(uri) {
      const p = uri.split(':');
      if (p[1] === 'collection') return '/collection/tracks';
      if (p[1] === 'user' && p[3] === 'collection') return '/collection/tracks';
      if (p[1] === 'user' && p[3] === 'playlist') return '/playlist/' + p[4];
      return '/' + p[1] + '/' + p[2];
    },

    async toca(uri) {
      const cami = this.cami(uri);
      if (location.pathname !== cami) {
        history.pushState({}, '', cami);
        dispatchEvent(new PopStateEvent('popstate', { state: {} }));
      }
      for (let i = 0; i < 48; i++) {
        await espera(250);
        if (location.pathname !== cami) continue;
        const b = q('main [data-testid="action-bar-row"] [data-testid="play-button"]') || q('[data-testid="action-bar-row"] [data-testid="play-button"]');
        if (b) {
          await espera(400);
          const etiqueta = (b.getAttribute('aria-label') || '').toLowerCase();
          if (!/pause|pausa|atur/.test(etiqueta)) b.click();
          return { ok: true };
        }
      }
      return { ok: false, error: 'No he trobat el botó de reproduir' };
    },

    async dispositius() {
      const l = await navigator.mediaDevices.enumerateDevices();
      return l.filter((d) => d.kind === 'audiooutput').map((d) => d.label || d.deviceId);
    },

    async sortida(nom) {
      try { localStorage.setItem('__casaSink', nom || '-'); } catch (e) {}
      if (!nom) { sinkId = ''; totsMedia().forEach(aplica); ctxs.forEach(aplicaCtx); return { ok: true, sortida: 'PC' }; }
      let l = await navigator.mediaDevices.enumerateDevices();
      // Sense permís, els noms dels aparells surten buits: el demanem un moment
      if (!l.some((x) => x.kind === 'audiooutput' && x.label)) {
        try { const st = await Promise.race([navigator.mediaDevices.getUserMedia({ audio: true }), espera(3000).then(() => null)]); if (st) st.getTracks().forEach((t) => t.stop()); } catch (e) {}
        l = await navigator.mediaDevices.enumerateDevices();
      }
      const d = l.find((x) => x.kind === 'audiooutput' && x.label.toLowerCase().indexOf(nom.toLowerCase()) >= 0);
      if (!d) return { ok: false, error: 'No trobo la sortida «' + nom + '». Has instal·lat el cable d\'àudio virtual?', aparells: l.filter((x) => x.kind === 'audiooutput').map((x) => x.label || '(sense nom)') };
      sinkId = d.deviceId;
      totsMedia().forEach(aplica); ctxs.forEach(aplicaCtx);
      await espera(600);
      const fet = [...media].filter((e) => e.sinkId === sinkId).length;
      return { ok: !darrerError, sortida: d.label, media: media.size, desviats: fet, contexts: ctxs.size, error: darrerError };
    }
  };
  try {
    const desat = localStorage.getItem('__casaSink');
    if (desat) setTimeout(() => window.__casa.sortida(desat === '-' ? '' : desat).catch(() => {}), 1500);
  } catch (e) {}
})();
