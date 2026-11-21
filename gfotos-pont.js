// Pont amb la web de Google Fotos (s'executa dins l'Edge del servidor).
// Si Google canvia la web i la sincronització deixa de trobar fotos, normalment
// n'hi ha prou d'ajustar els selectors d'aquest fitxer.
if (!window.__gf) (function () {
  const espera = (ms) => new Promise((r) => setTimeout(r, ms));
  const MESOS = {
    jan: 0, feb: 1, mar: 2, apr: 3, may: 4, jun: 5, jul: 6, aug: 7, sep: 8, oct: 9, nov: 10, dec: 11,
    gen: 0, febr: 1, 'març': 2, abr: 3, maig: 4, juny: 5, jul: 6, ag: 7, set: 8, oct: 9, nov: 10, des: 11,
    ene: 0, feb: 1, mar: 2, abr: 3, may: 4, jun: 5, jul: 6, ago: 7, sept: 8, sep: 8, oct: 9, nov: 10, dic: 11,
    fev: 1, mai: 4, out: 9, dez: 11
  };
  function data(label) {
    // Exemples: "Photo - Portrait - Nov 19, 2026, 10:23:45 AM", "Foto - 19 de nov. 2026, 10:23:45"
    const t = label.toLowerCase();
    let any = (t.match(/\b(19|20)\d\d\b/) || [])[0];
    if (!any) return 0;
    let mes = -1, dia = 0;
    const paraules = t.replace(/[.,]/g, ' ').split(/\s+/);
    for (let i = 0; i < paraules.length && mes < 0; i++) {
      const p = paraules[i];
      for (const k in MESOS) if (p.indexOf(k) === 0 && p.length <= k.length + 6) { mes = MESOS[k]; break; }
      if (mes >= 0) {
        const abans = parseInt(paraules[i - 1], 10), despres = parseInt(paraules[i + 1], 10);
        if (abans >= 1 && abans <= 31) dia = abans;
        else if (paraules[i - 1] === 'de' && parseInt(paraules[i - 2], 10) >= 1) dia = parseInt(paraules[i - 2], 10);
        else if (despres >= 1 && despres <= 31) dia = despres;
      }
    }
    if (mes < 0 || !dia) return 0;
    const h = t.match(/(\d{1,2}):(\d\d)(?::(\d\d))?\s*(am|pm|a\.?\s?m\.?|p\.?\s?m\.?)?/);
    let hora = 12, min = 0, seg = 0;
    if (h) {
      hora = +h[1]; min = +h[2]; seg = +(h[3] || 0);
      if (h[4] && /p/.test(h[4]) && hora < 12) hora += 12;
      if (h[4] && /a/.test(h[4]) && hora === 12) hora = 0;
    }
    return new Date(+any, mes, dia, hora, min, seg).getTime();
  }
  function desplacable() {
    let millor = document.scrollingElement, alt = 0;
    document.querySelectorAll('div, c-wiz, main').forEach((e) => {
      if (e.scrollHeight > e.clientHeight + 200 && e.clientHeight > alt) {
        const o = getComputedStyle(e).overflowY;
        if (o === 'auto' || o === 'scroll') { millor = e; alt = e.clientHeight; }
      }
    });
    return millor;
  }
  // Captura les respostes internes de Google Fotos (batchexecute): hi ha totes les fotos que es carreguen
  const capturades = [];
  function guarda(url, text) {
    if (!/batchexecute|PhotosUi/.test(url || '') || !text) return;
    capturades.push(text);
    if (capturades.length > 400) capturades.shift();
  }
  try {
    const open0 = XMLHttpRequest.prototype.open, send0 = XMLHttpRequest.prototype.send;
    XMLHttpRequest.prototype.open = function (m, u) { this.__gfu = u; return open0.apply(this, arguments); };
    XMLHttpRequest.prototype.send = function () {
      this.addEventListener('load', function () { try { guarda(this.__gfu, this.responseText); } catch (e) {} });
      return send0.apply(this, arguments);
    };
    const f0 = window.fetch;
    window.fetch = function (input) {
      const u = typeof input === 'string' ? input : (input && input.url) || '';
      const p = f0.apply(this, arguments);
      if (/batchexecute|PhotosUi/.test(u)) p.then((r) => r.clone().text().then((t) => guarda(u, t))).catch(() => {});
      return p;
    };
  } catch (e) {}
  // Adreces d'imatge: abans lh3.googleusercontent.com, ara també photos.fife.usercontent.google.com
  const RE_FOTO = /\["([A-Za-z0-9_-]{25,})",\["(https:\/\/[a-z0-9.-]*(?:googleusercontent\.com|usercontent\.google\.com)\/[^"\\]+)[^"]*",\d+,\d+[^"]{0,300}?\],(1\d{12})/g;
  // Més tolerant: un identificador de Google Fotos (AF1Qip…) seguit d'una adreça d'imatge i, a prop, una data (ms)
  const RE_FOTO2 = /"(AF1Qip[A-Za-z0-9_-]{20,})",\[?"(https:\/\/[a-z0-9.-]*(?:googleusercontent\.com|usercontent\.google\.com)\/[^"\\]+)/g;
  function deText(txt, l, ids) {
    // El text pot venir escapat una o dues vegades (JSON dins de JSON)
    let t = txt;
    for (let k = 0; k < 3; k++) {
      let m;
      RE_FOTO.lastIndex = 0;
      while ((m = RE_FOTO.exec(t))) if (!ids.has(m[1])) { ids.add(m[1]); l.push({ id: m[1], url: m[2].split('=')[0], data: +m[3], label: '' }); }
      RE_FOTO2.lastIndex = 0;
      while ((m = RE_FOTO2.exec(t))) {
        if (ids.has(m[1])) continue;
        const resta = t.substring(m.index + m[0].length, m.index + m[0].length + 400);
        const d = (resta.match(/[,\[](1[4-9]\d{11})[,\]]/) || [])[1];
        ids.add(m[1]); l.push({ id: m[1], url: m[2].split('=')[0], data: d ? +d : 0, label: '' });
      }
      if (t.indexOf('\\"') < 0 && t.indexOf('\\u003d') < 0) break;
      t = t.replace(/\\u003d/g, '=').replace(/\\u0026/g, '&').replace(/\\\\/g, '\\').replace(/\\"/g, '"');
    }
  }

  window.__gf = {
    deXarxa() {
      const l = [], ids = new Set();
      for (const t of capturades) deText(t, l, ids);
      return l;
    },
    sessio() { return !/accounts\.google\.com|signin/.test(location.href); },
    dadesInicials() {
      // Les fotos que Google ja posa dins la pàgina en carregar-la
      const l = [], ids = new Set();
      deText(Array.from(document.scripts).map((x) => x.textContent || '').join('\n'), l, ids);
      return l;
    },
    async recull(max, coneguts) {
      const r = await this.recullDom(max, coneguts);
      const ini = this.dadesInicials(), xarxa = this.deXarxa();
      const ids = new Set(r.map((x) => x.id));
      // Les dades de xarxa porten la data exacta: completen o corregeixen les del DOM
      const perId = new Map(r.map((x) => [x.id, x]));
      for (const x of ini.concat(xarxa)) {
        const ja = perId.get(x.id);
        if (ja) { if (!ja.data && x.data) ja.data = x.data; continue; }
        if (r.length < max) { r.push(x); perId.set(x.id, x); ids.add(x.id); }
      }
      r.sort((a, b) => (b.data || 0) - (a.data || 0));
      const enllacos = document.querySelectorAll('a[href*="/photo/"]');
      const tots = Array.from(document.scripts).map((x) => x.textContent || '').join('\n') + capturades.join('\n');
      const ai = tots.indexOf('AF1Qip');
      const diag = { url: location.href, titol: document.title, enllacos: enllacos.length, inicials: ini.length, xarxa: xarxa.length, respostes: capturades.length, trobades: r.length,
        imatges: document.querySelectorAll('img[src*="usercontent"], [style*="usercontent"]').length,
        ids: (tots.match(/AF1Qip[A-Za-z0-9_-]{20,}/g) || []).length, prop: ai >= 0 ? tots.substring(ai - 40, ai + 400) : '',
        mostra: enllacos[0] ? enllacos[0].outerHTML.substring(0, 600) : (document.body ? document.body.innerHTML.substring(0, 1500) : '') };
      return { items: r, diag: diag };
    },
    async recullDom(max, coneguts) {
      const vistos = new Map(), sap = new Set(coneguts || []);
      const sc = desplacable();
      let quiet = 0, trobatsConeguts = 0;
      await espera(1500);
      while (vistos.size < max && quiet < 8) {
        const abans = vistos.size;
        document.querySelectorAll('a[href*="/photo/"]').forEach((a) => {
          const id = (a.getAttribute('href').match(/photo\/([^?/#]+)/) || [])[1];
          if (!id || vistos.has(id)) return;
          let url = '';
          const cands = [a].concat(Array.from(a.querySelectorAll('*')));
          if (a.parentElement) cands.push(a.parentElement, ...a.parentElement.querySelectorAll('*'));
          for (const el of cands) {
            const bgl = el.getAttribute && (el.getAttribute('data-latest-bg') || '');
            const st = el.style && el.style.backgroundImage;
            const m = st && st.match(/url\(["']?([^"')]+)["']?\)/);
            const src = el.tagName === 'IMG' ? el.src : '';
            const u = bgl || (m && m[1]) || src;
            if (u && /googleusercontent\.com|usercontent\.google\.com/.test(u)) { url = u; break; }
          }
          if (!url) return;
          const label = a.getAttribute('aria-label') || '';
          if (/v[ií]deo/i.test(label)) return;
          if (sap.has(id)) trobatsConeguts++;
          vistos.set(id, { id: id, url: url.split('=')[0], data: data(label), label: label });
        });
        if (vistos.size === abans) quiet++; else quiet = 0;
        if (trobatsConeguts > 60) break;
        sc.scrollBy(0, Math.round(sc.clientHeight * 0.85));
        await espera(900);
      }
      return Array.from(vistos.values()).slice(0, max);
    },
    async albums() {
      await espera(2500);
      const vistos = new Map(), sc = desplacable();
      let quiet = 0;
      while (quiet < 6 && vistos.size < 2000) {
        const abans = vistos.size;
        document.querySelectorAll('a[href*="/album/"], a[href*="/share/"]').forEach((a) => {
          const url = a.href.split('?')[0];
          if (vistos.has(url)) return;
          let nom = (a.getAttribute('aria-label') || a.innerText || '').trim().split('\n')[0];
          if (!nom) { const t = a.querySelector('[title]'); nom = t ? t.getAttribute('title') : ''; }
          if (!nom) return;
          vistos.set(url, { nom: nom.substring(0, 80), url: url });
        });
        if (vistos.size === abans) quiet++; else quiet = 0;
        sc.scrollBy(0, Math.round(sc.clientHeight * 0.9));
        await espera(800);
      }
      return Array.from(vistos.values());
    }
  };
})();
