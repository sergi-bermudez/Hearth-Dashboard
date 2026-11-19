// Pont entre Hearth i WhatsApp Web (s'executa dins l'Edge del servidor).
// Si WhatsApp canvia la web, normalment n'hi ha prou d'ajustar els selectors d'aquest fitxer.
if (!window.__wa) (function () {
  const q = (s, a) => (a || document).querySelector(s);
  const qa = (s, a) => Array.from((a || document).querySelectorAll(s));
  const espera = (ms) => new Promise((r) => setTimeout(r, ms));
  const txt = (e) => (e ? (e.innerText || e.textContent || '').trim() : '');

  const panell = () => q('#pane-side');
  const files = () => {
    const p = panell();
    if (!p) return [];
    let l = qa('[role="listitem"]', p);
    if (!l.length) l = qa('[role="row"]', p);
    return l;
  };
  // Nom del xat dins d'una fila de la llista
  const nomFila = (f) => {
    const t = q('span[title][dir="auto"]', f) || q('span[title]', f);
    return t ? t.getAttribute('title') : '';
  };
  const noLlegits = (f) => {
    const b = qa('span[aria-label]', f).find((s) => /unread|no le[ií]d|no llegi|não lid|non lu/i.test(s.getAttribute('aria-label') || ''));
    if (!b) return 0;
    const n = parseInt(txt(b), 10);
    return isNaN(n) ? 1 : n;
  };

  const vistos = {};
  const nous = [];
  let primer = true;

  window.__wa = {
    estat() {
      const qr = q('canvas[aria-label]') || q('div[data-ref] canvas') || q('[data-testid="qrcode"] canvas');
      return { sessio: !!panell(), qr: !panell() && !!qr, carregant: !panell() && !qr };
    },

    qr() {
      const c = q('canvas[aria-label]') || q('div[data-ref] canvas') || q('[data-testid="qrcode"] canvas');
      return c ? c.toDataURL('image/png') : '';
    },

    xats() {
      const r = [];
      for (const f of files()) {
        const nom = nomFila(f);
        if (!nom) continue;
        const spans = qa('span[title]', f).map((s) => s.getAttribute('title')).filter((t) => t && t !== nom);
        const img = q('img', f);
        const hora = qa('div', f).map((d) => d.childElementCount === 0 ? txt(d) : '').find((t) => /^\d{1,2}:\d\d$|^[A-Za-zÀ-ú]{2,}\.?$|^\d{1,2}\/\d{1,2}\/\d{2,4}$/.test(t)) || '';
        r.push({ nom: nom, darrer: spans.length ? spans[spans.length - 1] : '', noLlegits: noLlegits(f), img: img ? img.src : '', hora: hora });
      }
      return r;
    },

    // Revisa la llista i apunta els missatges nous (per als avisos del dashboard)
    vigila() {
      for (const x of this.xats()) {
        const clau = x.nom + '|' + x.darrer;
        if (x.noLlegits > 0 && !vistos[clau]) {
          vistos[clau] = 1;
          if (!primer) nous.push({ nom: x.nom, text: x.darrer, img: x.img, ts: Date.now() });
          if (nous.length > 30) nous.shift();
        }
      }
      if (panell()) primer = false;
      return true;
    },
    nous(des) { this.vigila(); return nous.filter((n) => n.ts > (des || 0)); },

    async obre(nom) {
      if (this.actual() === nom) return true;
      let f = files().find((x) => nomFila(x) === nom);
      if (!f) {
        // Cerca el xat pel nom
        const caixa = q('#side div[contenteditable="true"]') || q('div[contenteditable="true"][data-tab="3"]');
        if (caixa) {
          caixa.focus();
          document.execCommand('selectAll', false, null);
          document.execCommand('insertText', false, nom);
          for (let i = 0; i < 20 && !f; i++) { await espera(150); f = files().find((x) => nomFila(x) === nom); }
        }
      }
      if (!f) return false;
      const clic = q('[role="gridcell"]', f) || q('div[tabindex]', f) || f;
      for (const t of ['mousedown', 'mouseup', 'click']) clic.dispatchEvent(new MouseEvent(t, { bubbles: true, cancelable: true, view: window }));
      for (let i = 0; i < 30; i++) { await espera(150); if (this.actual() === nom) break; }
      return this.actual() === nom;
    },

    actual() {
      const h = q('#main header');
      if (!h) return '';
      const t = q('span[title]', h) || q('span[dir="auto"]', h);
      return t ? (t.getAttribute('title') || txt(t)) : '';
    },

    async missatges(nom) {
      if (nom && !(await this.obre(nom))) return { error: 'No trobo el xat' };
      await espera(400);
      const m = q('#main');
      if (!m) return { error: 'No hi ha cap xat obert' };
      const r = [];
      let rows = qa('[role="row"]', m);
      if (!rows.length) rows = qa('.message-in, .message-out', m);
      for (const row of rows.slice(-60)) {
        const surt = !!(q('.message-out', row) || row.classList.contains('message-out'));
        const entra = !!(q('.message-in', row) || row.classList.contains('message-in'));
        if (!surt && !entra) {
          const s = txt(row);
          if (s && s.length < 60) r.push({ tipus: 'sistema', text: s });
          continue;
        }
        const ct = q('[data-pre-plain-text]', row);
        const pre = ct ? ct.getAttribute('data-pre-plain-text') || '' : '';
        const mm = /\[([^\]]+)\]\s*([^:]*):/.exec(pre);
        const cos = q('span.selectable-text', row) || q('.copyable-text span', row);
        let text = cos ? txt(cos) : '';
        let media = '';
        if (q('img[src^="blob:"]', row) && !text) media = 'foto';
        else if (q('img[src^="blob:"]', row)) media = 'foto';
        if (q('[data-icon="audio-play"], [data-icon="ptt-play"], audio', row)) media = 'àudio';
        if (q('video', row)) media = 'vídeo';
        if (q('[data-icon="document"], [data-icon*="doc"]', row) && !media) media = 'document';
        if (!text && !media) { const s = txt(row); text = s.replace(/\d{1,2}:\d\d\s*$/, '').trim(); }
        r.push({ tipus: surt ? 'surt' : 'entra', text: text, media: media, qui: mm ? mm[2].trim() : '', hora: mm ? mm[1].split(',')[0].trim() : '' });
      }
      return { nom: this.actual(), missatges: r };
    },

    async envia(nom, text) {
      if (!(await this.obre(nom))) return { ok: false, error: 'No trobo el xat' };
      const caixa = q('footer div[contenteditable="true"]') || q('#main div[contenteditable="true"][data-tab="10"]') || q('#main div[contenteditable="true"]');
      if (!caixa) return { ok: false, error: 'No trobo on escriure' };
      caixa.focus();
      document.execCommand('insertText', false, text);
      await espera(250);
      const b = q('footer button[aria-label="Send"]') || q('footer button[aria-label="Enviar"]') || q('footer button[aria-label="Envia"]') || (q('footer span[data-icon="send"]') || {}).parentElement || (q('footer span[data-icon="wds-ic-send-filled"]') || {}).parentElement;
      if (b && b.click) b.click();
      else caixa.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', code: 'Enter', keyCode: 13, which: 13, bubbles: true }));
      await espera(500);
      return { ok: true };
    },

    llegit() { return true; }
  };
  setInterval(() => { try { window.__wa.vigila(); } catch (e) {} }, 5000);
})();
