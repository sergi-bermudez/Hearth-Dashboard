// Pont entre Hearth i els missatges de Facebook (facebook.com/messages, el que queda de messenger.com).
// Mateixa interficie que whatsapp-pont.js. Facebook canvia les classes sovint: aqui nomes es fan servir rols i adreces.
if (!window.__hx || window.__hx.v !== 1) (function () {
  const q = (s, a) => (a || document).querySelector(s);
  const qa = (s, a) => Array.from((a || document).querySelectorAll(s));
  const espera = (ms) => new Promise((r) => setTimeout(r, ms));
  const txt = (e) => (e ? (e.innerText || e.textContent || '').trim() : '');
  const norm = (t) => (t || '').replace(/[​-‏‪-‮⁦-⁩﻿]/g, '').replace(/\s+/g, ' ').trim().toLowerCase();
  const vis = (e) => { if (!e) return false; const r = e.getBoundingClientRect(); return r.width > 2 && r.height > 2; };
  const main = () => q('[role="main"]');

  // Les files de la llista: enllacos a /messages/t/... que no son dins del xat obert
  const files = () => {
    const m = main(), vist = {};
    return qa('a[href*="/messages/t/"]').filter((a) => {
      if (!vis(a) || (m && m.contains(a))) return false;
      const h = a.getAttribute('href');
      if (vist[h]) return false;
      vist[h] = 1;
      return txt(a).length > 0;
    });
  };
  const linies = (a) => txt(a).split('\n').map((l) => l.trim()).filter((l) => l && !/^(unread message|mensagem não lida|mensaje no leído|missatge no llegit|message non lu)/i.test(l));
  const nomFila = (a) => linies(a)[0] || '';
  const noLlegits = (a) => {
    if (qa('[aria-label]', a).some((e) => /unread|não lida|no leído|no llegit|non lu/i.test(e.getAttribute('aria-label')))) return 1;
    if (/unread message|mensagem não lida|mensaje no leído|missatge no llegit|message non lu/i.test(txt(a))) return 1;
    // el darrer missatge en negreta
    const sp = qa('span[dir="auto"]', a);
    const s = sp[1];
    if (s && parseInt(getComputedStyle(s).fontWeight, 10) >= 600) return 1;
    return 0;
  };
  const idActual = () => { const m = /\/messages\/t\/([^/?#]+)/.exec(location.pathname); return m ? m[1] : ''; };
  const filaActual = () => { const id = idActual(); if (!id) return null; return files().find((a) => (a.getAttribute('href') || '').indexOf('/messages/t/' + id) >= 0) || null; };
  const esActual = (nom) => { const f = filaActual(); return !!f && norm(nomFila(f)) === norm(nom); };
  const login = () => !!(q('input[name="pass"]') || q('input[type="password"]'));
  const pin = () => !files().length && /\bPIN\b/.test(txt(document.body)) && !!q('input[maxlength="6"], input[inputmode="numeric"]');

  const vistos = {};
  const nous = [];
  let primer = true;

  window.__hx = {
    v: 1,
    estat() {
      const s = files().length > 0 || !!main() && /\/messages\//.test(location.pathname) && !login();
      return { sessio: s && !pin(), login: login(), pin: pin(), carregant: !s && !login() && !pin() };
    },

    qr() { return ''; },

    xats() {
      const r = [];
      for (const a of files()) {
        const l = linies(a);
        if (!l.length) continue;
        let darrer = l.slice(1).join(' '), hora = '';
        const m = /^(.*)\s·\s([^·]+)$/.exec(darrer);
        if (m) { darrer = m[1]; hora = m[2]; }
        let img = '';
        const i = q('img', a);
        if (i && /^https/.test(i.src)) img = i.src;
        else { const im = q('svg image', a); if (im) img = im.getAttribute('xlink:href') || im.getAttribute('href') || ''; }
        r.push({ nom: l[0], darrer: darrer, noLlegits: noLlegits(a), img: img, hora: hora });
      }
      return r;
    },

    vigila() {
      for (const x of this.xats()) {
        const clau = x.nom + '|' + x.darrer;
        if (x.noLlegits > 0 && !vistos[clau]) {
          vistos[clau] = 1;
          if (!primer) nous.push({ nom: x.nom, text: x.darrer, img: x.img, ts: Date.now() });
          if (nous.length > 30) nous.shift();
        }
      }
      if (files().length) primer = false;
      return true;
    },
    nous(des) {
      this.vigila();
      let total = 0;
      for (const a of files()) total += noLlegits(a);
      return { llista: nous.filter((n) => n.ts > (des || 0)), total: total };
    },

    async rect(nom) {
      if (esActual(nom)) return { ja: true };
      const a = files().find((x) => norm(nomFila(x)) === norm(nom));
      if (!a) return { error: 'No trobo el xat' };
      a.scrollIntoView({ block: 'center' });
      await espera(200);
      const r = a.getBoundingClientRect();
      return { x: Math.round(r.left + Math.min(r.width / 2, 90)), y: Math.round(r.top + r.height / 2) };
    },

    esActual(nom) { return esActual(nom); },

    diag(nom) {
      const m = main();
      return { url: location.href, files: files().length, noms: files().slice(0, 5).map(nomFila), actual: idActual(), demanat: nom || '',
        main: !!m, rows: m ? qa('[role="row"]', m).length : 0, caixa: !!this.caixa(), login: login(), pin: pin() };
    },

    caixa() { const m = main() || document; return q('div[role="textbox"][contenteditable="true"]', m) || q('[contenteditable="true"][aria-label]', m); },

    async missatges(nom) {
      if (nom && !esActual(nom)) {
        const a = files().find((x) => norm(nomFila(x)) === norm(nom));
        if (!a) return { error: 'No trobo el xat', diag: this.diag(nom) };
        a.click();
        for (let i = 0; i < 30 && !esActual(nom); i++) await espera(150);
        if (!esActual(nom)) return { error: 'No s\'ha obert el xat', diag: this.diag(nom) };
      }
      await espera(400);
      const m = main();
      if (!m) return { error: 'No hi ha cap xat obert' };
      const mr = m.getBoundingClientRect();
      const r = [];
      for (const row of qa('[role="row"]', m).slice(-70)) {
        const parts = qa('div[dir="auto"]', row).filter((d) => !d.querySelector('div[dir="auto"]'));
        let text = parts.map(txt).filter((t) => t).join('\n');
        let media = '';
        const imgs = qa('img', row).filter((i) => i.getBoundingClientRect().width > 80);
        if (imgs.length) media = 'foto';
        if (q('video', row)) media = 'vídeo';
        if (q('audio, [aria-label*="audio" i], [aria-label*="áudio" i]', row)) media = 'àudio';
        if (!text && !media) {
          // separadors de data/hora, avisos del xat
          const s = txt(row);
          if (s && s.length < 80) r.push({ tipus: 'sistema', text: s.replace(/\n/g, ' ') });
          continue;
        }
        // enviat o rebut per la posicio de la bombolla
        const b = (parts[0] || imgs[0] || row).getBoundingClientRect();
        const surt = b.width > 0 && (b.left + b.right) / 2 > (mr.left + mr.right) / 2 + 10;
        r.push({ tipus: surt ? 'surt' : 'entra', text: text, media: media, qui: '', hora: '' });
      }
      const f = filaActual();
      return { nom: f ? nomFila(f) : nom, missatges: r };
    },

    async envia(nom, text) {
      if (!esActual(nom)) return { ok: false, error: 'No trobo el xat' };
      const c = this.caixa();
      if (!c) return { ok: false, error: 'No trobo on escriure' };
      c.focus();
      document.execCommand('insertText', false, text);
      await espera(300);
      // l'editor de Facebook envia amb una tecla Enter de debo (la fa el servidor)
      return { ok: true, enter: true };
    }
  };
  if (!window.__hxT) window.__hxT = setInterval(() => { try { window.__hx.vigila(); } catch (e) {} }, 5000);
})();
