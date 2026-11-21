// Hearth · lector de catàlegs de supermercat (s'executa dins l'Edge amagat, a la pàgina de la botiga).
// No depèn de classes concretes de cap web: busca enllaços a fitxes de producte i, al voltant, el preu.
(function () {
  if (window.__bt && window.__bt.v === 1) return;
  const espera = (ms) => new Promise((r) => setTimeout(r, ms));
  const RX_PROD = /\/(produto|produtos|product|products|p)\/[^?#]*[a-z0-9]/i;
  const RX_PREU = /(\d{1,4}[.,]\d{2})\s*€|€\s*(\d{1,4}[.,]\d{2})/g;
  const num = (s) => parseFloat(String(s).replace(',', '.'));
  const txt = (el) => (el && (el.innerText || el.textContent) || '').replace(/[ \t]+/g, ' ').trim();

  function rebutjaGaletes() {
    // Opció més privada: rebutjar o només les necessàries (mai "acceptar totes")
    const bs = Array.from(document.querySelectorAll('button, a[role="button"], [role="button"]'));
    const b = bs.find((x) => /^(rejeitar|recusar|rejeitar tudo|recusar tudo|apenas (os )?necess|só necess|continuar sem aceitar|reject)/i.test(txt(x)));
    if (b) { try { b.click(); } catch (e) { } }
  }

  // Fitxa = l'ancestre més proper de l'enllaç que conté un preu i només un producte
  function fitxa(a) {
    let t = a;
    for (let k = 0; k < 9 && t && t !== document.body; k++) {
      const s = txt(t);
      if (/\d[.,]\d{2}\s*€|€\s*\d/.test(s)) {
        const n = new Set(Array.from(t.querySelectorAll('a[href]')).map((x) => x.href.split(/[?#]/)[0]).filter((h) => RX_PROD.test(h))).size;
        return n <= 1 ? t : null;
      }
      t = t.parentElement;
    }
    return null;
  }

  function preus(s) {
    // "PVPR 6,00€ 5,34€ 0,89€/lt" → preu 5,34 · abans 6,00 · unitari 0,89/lt
    const r = []; let m; RX_PREU.lastIndex = 0;
    while ((m = RX_PREU.exec(s))) {
      const v = num(m[1] || m[2]), seg = s.substr(m.index + m[0].length, 4), ant = s.substr(Math.max(0, m.index - 14), 14);
      r.push({ v: v, unit: /^\s*\//.test(seg), antic: /pvpr|antes|era|de\s*$|recomendado/i.test(ant) });
    }
    const unit = r.find((x) => x.unit);
    const nets = r.filter((x) => !x.unit);
    if (!nets.length) return null;
    let preu = nets.filter((x) => !x.antic).pop() || nets[nets.length - 1];
    let abans = nets.find((x) => x.antic) || null;
    if (!abans && nets.length > 1) { const mx = Math.max.apply(null, nets.map((x) => x.v)); if (mx > preu.v) abans = { v: mx }; }
    const mu = unit ? new RegExp(String(unit.v.toFixed(2)).replace('.', '[.,]') + '\\s*€\\s*\\/\\s*([a-zA-Z]+)').exec(s) : null;
    return { p: preu.v, pv: abans && abans.v > preu.v ? abans.v : 0, pu: unit ? unit.v : 0, uu: mu ? mu[1] : '' };
  }

  function productes() {
    const vist = new Set(), out = [];
    for (const a of document.querySelectorAll('a[href]')) {
      const h = a.href.split(/[?#]/)[0];
      if (!RX_PROD.test(h) || vist.has(h)) continue;
      const t = fitxa(a);
      if (!t) continue;
      vist.add(h);
      const s = txt(t), pr = preus(s);
      if (!pr) continue;
      const img = t.querySelector('img');
      let nom = '';
      const cap = t.querySelector('h2, h3, h4, [class*="name" i], [class*="title" i], [class*="nome" i]');
      if (cap) nom = txt(cap);
      if (!nom || nom.length < 3) nom = txt(a);
      if ((!nom || nom.length < 3) && img) nom = img.alt || '';
      nom = nom.split('\n')[0].trim();
      if (!nom) continue;
      const qm = /(?:emb\.?|embalagem)\s*([^\n€]{1,30})/i.exec(s) || /\b(\d+\s*x\s*\d+[.,]?\d*\s*(?:g|gr|kg|ml|cl|l|lt)|\d+[.,]?\d*\s*(?:g|gr|kg|ml|cl|l|lt|un|unid)\.?)(?=\s|$)/i.exec(s);
      const marca = t.querySelector('[class*="brand" i], [class*="marca" i]');
      let im = img ? (img.currentSrc || img.src || img.getAttribute('data-src') || '') : '';
      if (/^data:/.test(im)) im = img.getAttribute('data-src') || '';
      out.push({
        n: nom, u: h, img: im, p: pr.p, pv: pr.pv, pu: pr.pu, uu: pr.uu,
        q: qm ? qm[1].trim() : '', m: marca ? txt(marca) : '',
        esg: /indispon[ií]vel|esgotado|sem stock/i.test(s) ? 1 : 0,
        promo: pr.pv > 0 || /promo|desconto|poupe|leve \d|oferta|\-\d+\s*%/i.test(s) ? 1 : 0
      });
    }
    return out;
  }

  // Enllaços del menú per descobrir les categories
  function enllacos() {
    const out = [], vist = new Set(), host = location.host.replace(/^www\./, '');
    for (const a of document.querySelectorAll('a[href]')) {
      let u; try { u = new URL(a.href); } catch (e) { continue; }
      if (u.host.replace(/^www\./, '') !== host) continue;
      const p = u.pathname.replace(/\/+$/, '/');
      if (RX_PROD.test(p) || vist.has(p) || p === '/' || /conta|login|ajuda|contact|carrinho|cart|checkout|termos|privacidade|cookies|lojas|receitas|folheto|blog|noticias|empresa|institucional/i.test(p)) continue;
      const d = p.split('/').filter(Boolean).length;
      if (d < 1 || d > 4) continue;
      vist.add(p);
      out.push({ u: u.origin + p, t: (txt(a) || a.title || '').split('\n')[0].slice(0, 60), d: d });
    }
    return out;
  }

  // Prem "Ver mais" / baixa fins al final per carregar més productes. Torna true si n'han aparegut més.
  async function mes() {
    const abans = document.querySelectorAll('a[href]').length;
    const b = Array.from(document.querySelectorAll('button, a')).find((x) => /^(ver mais|ver mais produtos|carregar mais|mostrar mais|mais produtos|load more|show more)/i.test(txt(x)) && x.offsetParent !== null);
    if (b) { try { b.scrollIntoView(); b.click(); } catch (e) { } }
    else window.scrollTo(0, document.body.scrollHeight);
    for (let i = 0; i < 16; i++) { await espera(400); if (document.querySelectorAll('a[href]').length > abans + 2) return true; }
    return false;
  }

  async function llest() {
    rebutjaGaletes();
    for (let i = 0; i < 30; i++) {
      if (productes().length >= 3) break;
      await espera(400);
      if (i == 5) rebutjaGaletes();
    }
    return { url: location.href, titol: document.title, n: productes().length };
  }

  window.__bt = { v: 1, productes: productes, enllacos: enllacos, mes: mes, llest: llest };
})();
