// Pont entre Hearth i Google Messages per a la web (s'executa dins l'Edge del servidor).
// Mateixa interficie que whatsapp-pont.js. Si Google canvia la web, normalment n'hi ha prou d'ajustar els selectors d'aqui.
if (!window.__hx || window.__hx.v !== 1) (function () {
  const q = (s, a) => (a || document).querySelector(s);
  const qa = (s, a) => Array.from((a || document).querySelectorAll(s));
  const espera = (ms) => new Promise((r) => setTimeout(r, ms));
  const txt = (e) => (e ? (e.innerText || e.textContent || '').trim() : '');
  const norm = (t) => (t || '').replace(/[​-‏‪-‮⁦-⁩﻿]/g, '').replace(/\s+/g, ' ').trim().toLowerCase();
  const vis = (e) => { if (!e) return false; const r = e.getBoundingClientRect(); return r.width > 2 && r.height > 2; };

  // Si la web diu que ja esta oberta en una altra pestanya, es fa servir aqui
  const usaAqui = () => {
    for (const b of qa('button')) if (/^(use here|usar aqu[ií]|utilizar aqui|utilitza aqu[ií]|utiliser ici)$/i.test(txt(b))) { b.click(); return true; }
    return false;
  };
  const llista = () => q('mws-conversations-list') || q('nav mws-conversation-list-item') && q('nav');
  const files = () => qa('mws-conversation-list-item').filter(vis);
  const enllac = (f) => q('a[href*="/conversations/"]', f) || f;
  const nomFila = (f) => txt(q('[data-e2e-conversation-name]', f) || q('.name', f) || q('h3', f) || q('span', f)).split('\n')[0];
  const noLlegits = (f) => {
    const a = enllac(f);
    if ((a.className || '').toString().indexOf('unread') >= 0 || f.getAttribute('data-e2e-is-unread') === 'true' || q('[data-e2e-is-unread="true"]', f)) return 1;
    return 0;
  };
  // El xat obert: la fila de la llista que te l'adreca actual
  const idActual = () => { const m = /\/conversations\/([^/?#]+)/.exec(location.pathname); return m && m[1] !== 'new' ? m[1] : ''; };
  const filaActual = () => { const id = idActual(); if (!id) return null; return files().find((f) => (enllac(f).getAttribute('href') || '').indexOf('/conversations/' + id) >= 0) || null; };
  const esActual = (nom) => { const f = filaActual(); return !!f && norm(nomFila(f)) === norm(nom); };

  const vistos = {};
  const nous = [];
  let primer = true;

  window.__hx = {
    v: 1,
    estat() {
      usaAqui();
      const qr = q('mw-qr-code img') || q('mw-qr-code canvas') || q('img[alt*="QR" i]');
      const s = !!llista() || files().length > 0;
      return { sessio: s, qr: !s && !!qr, carregant: !s && !qr };
    },

    qr() {
      const i = q('mw-qr-code img') || q('img[alt*="QR" i]');
      if (i && i.src) return i.src;
      const c = q('mw-qr-code canvas');
      return c ? c.toDataURL('image/png') : '';
    },

    xats() {
      usaAqui();
      const r = [];
      for (const f of files()) {
        const nom = nomFila(f);
        if (!nom) continue;
        const img = q('img', f);
        r.push({ nom: nom, darrer: txt(q('mws-conversation-snippet', f) || q('.snippet-text', f)).replace(/\n/g, ' '), noLlegits: noLlegits(f),
          img: img && /^https/.test(img.src) ? img.src : '', hora: txt(q('mws-relative-timestamp', f) || q('.timestamp', f)) });
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
      for (const f of files()) total += noLlegits(f);
      return { llista: nous.filter((n) => n.ts > (des || 0)), total: total };
    },

    // On es el xat a la llista (el servidor hi fa un clic de ratoli de debo)
    async rect(nom) {
      if (esActual(nom)) return { ja: true };
      const f = files().find((x) => norm(nomFila(x)) === norm(nom));
      if (!f) return { error: 'No trobo el xat' };
      f.scrollIntoView({ block: 'center' });
      await espera(200);
      const r = enllac(f).getBoundingClientRect();
      return { x: Math.round(r.left + Math.min(r.width / 2, 80)), y: Math.round(r.top + r.height / 2) };
    },

    esActual(nom) { return esActual(nom); },

    diag(nom) {
      return { url: location.href, files: files().length, noms: files().slice(0, 5).map(nomFila), actual: idActual(), demanat: nom || '',
        missatges: qa('mws-message-wrapper').length, caixa: !!this.caixa() };
    },

    caixa() { return q('mws-message-compose textarea') || q('textarea[data-e2e-message-input-box]') || q('mws-autosize-textarea textarea') || q('main textarea'); },

    async missatges(nom) {
      if (nom && !esActual(nom)) {
        // el servidor ja ha fet el clic; si encara no hi es, s'obre per l'adreca de la fila
        const f = files().find((x) => norm(nomFila(x)) === norm(nom));
        if (!f) return { error: 'No trobo el xat', diag: this.diag(nom) };
        enllac(f).click();
        for (let i = 0; i < 30 && !esActual(nom); i++) await espera(150);
        if (!esActual(nom)) return { error: 'No s\'ha obert el xat', diag: this.diag(nom) };
      }
      await espera(300);
      const r = [];
      const els = qa('mws-message-wrapper, mws-tombstone-message-wrapper, mws-message-list-item .date-divider, .tombstone-timestamp');
      for (const el of els.slice(-70)) {
        if (el.tagName !== 'MWS-MESSAGE-WRAPPER') { const s = txt(el); if (s && s.length < 60) r.push({ tipus: 'sistema', text: s }); continue; }
        let surt = el.getAttribute('is-outgoing') === 'true' || /outgoing/.test(el.className || '') || !!q('.outgoing', el);
        if (!el.hasAttribute('is-outgoing') && !/outgoing|incoming/.test(el.className || '') && !q('.outgoing, .incoming', el)) {
          // per la posicio de la bombolla
          const b = (q('.text-msg-container, mws-text-message-part, .msg-content', el) || el).getBoundingClientRect(), m = (el.parentElement || el).getBoundingClientRect();
          surt = b.width > 0 && (b.left + b.right) / 2 > (m.left + m.right) / 2 + 10;
        }
        const cos = q('.text-msg-content', el) || q('mws-text-message-part', el) || q('.text-msg', el);
        let text = cos ? txt(cos) : '';
        let media = '';
        if (q('mws-image-message-part, img.image-msg, mws-image-message-part img', el)) media = 'foto';
        if (q('mws-video-message-part, video', el)) media = 'vídeo';
        if (q('mws-audio-message-part, audio', el)) media = 'àudio';
        if (!text && !media) text = txt(el);
        const h = q('mws-relative-timestamp', el) || q('.timestamp', el);
        r.push({ tipus: surt ? 'surt' : 'entra', text: text, media: media, qui: '', hora: h ? txt(h) : '' });
      }
      const f = filaActual();
      return { nom: f ? nomFila(f) : nom, missatges: r };
    },

    async envia(nom, text) {
      if (!esActual(nom)) return { ok: false, error: 'No trobo el xat' };
      const c = this.caixa();
      if (!c) return { ok: false, error: 'No trobo on escriure' };
      c.focus();
      const set = Object.getOwnPropertyDescriptor(HTMLTextAreaElement.prototype, 'value').set;
      set.call(c, text);
      c.dispatchEvent(new Event('input', { bubbles: true }));
      await espera(300);
      const b = q('mws-message-send-button button') || q('button[data-e2e-send-text-button]') || q('button[aria-label*="Send" i]') || q('button[aria-label*="Enviar" i]');
      if (b && !b.disabled) { b.click(); await espera(500); return { ok: true }; }
      return { ok: true, enter: true };
    }
  };
  if (!window.__hxT) window.__hxT = setInterval(() => { try { window.__hx.vigila(); } catch (e) {} }, 5000);
})();
