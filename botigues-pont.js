// Hearth · lector de catàlegs de supermercat (s'executa dins l'Edge amagat, a la pàgina de la botiga).
// No depèn de classes concretes de cap web: busca enllaços a fitxes de producte i, al voltant, el preu.
(function () {
  if (window.__bt && window.__bt.v === 7) return;
  const espera = (ms) => new Promise((r) => setTimeout(r, ms));
  // Fitxa de producte: /produto/…, /product/…, /p/… o adreces que acaben en un codi (…-544184.html).
  // "/produtos/…" (plural) sol ser una categoria, no un producte.
  const RX_PROD = /(\/(produto|product|p)\/[^?#]*[a-z0-9]|[-_\/]\d{4,}(\.html?)?$)/i;
  const RX_PREU = /(\d{1,4}[.,]\d{1,3})\s*€|€\s*(\d{1,4}[.,]\d{1,3})/g;
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
      const v = num(m[1] || m[2]), seg = s.substr(m.index + m[0].length, 4), ant = s.substr(Math.max(0, m.index - 22), 22).split('€').pop();
      // depòsit o taxa d'envàs («+0,10 € Depósito», «Valor depósito 0,10 €»): no és el preu
      const desp = s.substr(m.index + m[0].length, 30), abansT = s.substr(Math.max(0, m.index - 30), 30);
      if (/^\s*(\+\s*)?(dep[oó]sito|tara|taxa|vasilhame|retorn|eco|embalagem)/i.test(desp) || /(dep[oó]sito|tara|taxa|vasilhame|retorn[aá]vel|\+)\s*:?\s*$/i.test(abansT)) continue;
      r.push({ v: v, unit: /^\s*\/\s*(?!un|ud\b|u\b)[a-z]/i.test(s.substr(m.index + m[0].length, 8)), antic: /pvpr|antes|era|from|de\s*$|desde|recomendado|reduced|riduzione|avant/i.test(ant) });
    }
    const unit = r.find((x) => x.unit);
    let nets = r.filter((x) => !x.unit);
    // un import molt petit al costat d'un preu de debò (p. ex. 0,10 € de l'envàs) no és el preu
    if (nets.length > 1) { const mx = Math.max.apply(null, nets.map((x) => x.v)); if (mx > 0.6) nets = nets.filter((x) => !(x.v <= 0.25 && x.v * 4 < mx)); }
    const mu = unit ? new RegExp(String(unit.v).replace('.', '[.,]') + '0?' + '\\s*€\\s*\\/\\s*(kg|lt|ml|cl|gr|dos|dz|un|l|g|m)', 'i').exec(s) : null;
    // Productes a granel: només hi ha preu per kg ("6,19 €/Kg abans 6,49 €/Kg")
    if (!nets.length) {
      const us = r.filter((x) => x.unit);
      if (!us.length) return null;
      const nous = us.filter((x) => !x.antic), vells = us.filter((x) => x.antic);
      const pr = nous.length ? Math.min.apply(null, nous.map((x) => x.v)) : us[0].v;
      const ab = vells.length ? vells[0].v : (us.length > 1 ? Math.max.apply(null, us.map((x) => x.v)) : 0);
      return { p: pr, pv: ab > pr ? ab : 0, pu: pr, uu: mu ? mu[1] : 'kg', granel: 1 };
    }
    // El preu és el més baix dels que no són «abans»; l'antic, el marcat o el més alt
    const nous = nets.filter((x) => !x.antic);
    const preu = nous.length ? Math.min.apply(null, nous.map((x) => x.v)) : nets[0].v;
    let abans = nets.find((x) => x.antic);
    abans = abans ? abans.v : (nets.length > 1 ? Math.max.apply(null, nets.map((x) => x.v)) : 0);
    return { p: preu, pv: abans > preu ? abans : 0, pu: unit ? unit.v : 0, uu: mu ? mu[1] : '' };
  }

  const RX_AFEGIR = /^\s*(adicionar|adicionar ao carrinho|add|add to (cart|basket|trolley)|añadir|anadir|afegir|ajouter|aggiungi|comprar|in den warenkorb|\+)\s*$/i;
  // Fitxa per botó «Adicionar»: l'ancestre més petit que té el botó, un enllaç i un preu (i només un botó)
  function fitxaBoto(bt) {
    let t = bt.parentElement;
    for (let k = 0; k < 9 && t && t !== document.body; k++) {
      const s = txt(t);
      if (/\d[.,]\d{2}\s*€|€\s*\d/.test(s) && t.querySelector('a[href]')) {
        const n = Array.from(t.querySelectorAll('button, a, [role="button"]')).filter((x) => RX_AFEGIR.test(txt(x))).length;
        return n <= 1 ? t : null;
      }
      t = t.parentElement;
    }
    return null;
  }
  function candidats() {
    const r = [], vist = new Set();
    for (const a of document.querySelectorAll('a[href]')) {
      const h = a.href.split(/[?#]/)[0];
      if (!RX_PROD.test(h) || vist.has(h)) continue;
      const t = fitxa(a); if (t) { vist.add(h); r.push([a, t, h]); }
    }
    if (r.length >= 3) return r;
    // Webs on les fitxes no tenen una adreça reconeixible: es busquen pels botons d'afegir al carro
    const r2 = [], vist2 = new Set();
    for (const bt of document.querySelectorAll('button, a, [role="button"]')) {
      if (!RX_AFEGIR.test(txt(bt))) continue;
      const t = fitxaBoto(bt); if (!t || vist2.has(t)) continue;
      const a = Array.from(t.querySelectorAll('a[href]')).find((x) => !/^(javascript|#)/.test(x.getAttribute('href') || '#') && txt(x).length > 2) || t.querySelector('a[href]');
      if (!a) continue;
      const h = a.href.split(/[?#]/)[0];
      if (vist.has(h)) continue;
      vist.add(h); vist2.add(t); r2.push([a, t, h]);
    }
    return r2.length > r.length ? r2 : r;
  }

  function productes() {
    const out = [];
    for (const [a, t, h] of candidats()) {
      const s = txt(t), pr = preus(s);
      if (!pr) continue;
      const img = t.querySelector('img');
      let nom = '';
      const cap = t.querySelector('h2, h3, h4, [class*="name" i], [class*="title" i], [class*="nome" i]');
      if (cap) nom = txt(cap);
      if (!nom || nom.length < 3) nom = txt(a);
      if ((!nom || nom.length < 3) && img) nom = img.alt || '';
      nom = nom.split('\n')[0].replace(/(poupe|apenas|leve \d|desconto|oferta|promo[cç][aã]o|novidade|ahorra|save)\b.*$/i, '').trim();
      if (!nom) continue;
      const qm = /(?:emb\.?|embalagem)\s*([^\n€]{1,30})/i.exec(s) || /\b(\d+\s*x\s*\d+[.,]?\d*\s*(?:g|gr|kg|ml|cl|l|lt)|\d+[.,]?\d*\s*(?:g|gr|kg|ml|cl|l|lt|un|unid)\.?)(?=\s|$)/i.exec(s);
      const marca = t.querySelector('[class*="brand" i], [class*="marca" i]');
      let im = img ? (img.currentSrc || img.src || img.getAttribute('data-src') || '') : '';
      if (/^data:/.test(im)) im = img.getAttribute('data-src') || '';
      out.push({
        n: nom, u: h, img: im, p: pr.p, pv: pr.pv, pu: pr.pu, uu: pr.uu,
        q: qm ? qm[1].trim() : '', m: marca ? txt(marca) : '',
        g: pr.granel || (pr.pu && pr.p == pr.pu && /^(kg|kilo)/i.test(pr.uu) && /granel|\/\s*kg\b|€\s*\/\s*kg|kg\s*$/i.test(s.split('\n').slice(0, 6).join(' ')) && !/emb\.|\d+\s*(g|gr)\b/i.test(s)) ? 1 : 0,
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
      if (d < 1 || d > 6) continue;
      vist.add(p);
      out.push({ u: u.origin + p, t: (txt(a) || a.title || '').split('\n')[0].slice(0, 60), d: d });
    }
    return out;
  }

  // Prem "Ver mais" / baixa fins al final per carregar més productes. Torna true si n'han aparegut més.
  async function mes() {
    const abans = document.querySelectorAll('a[href]').length;
    const cand = Array.from(document.querySelectorAll('button, a')).filter((x) => /^(ver mais|carregar mais|mostrar mais|mais produtos|ver m[aá]s|cargar m[aá]s|load more|show more|voir plus|afficher plus)/i.test(txt(x)) && x.offsetParent !== null && !x.closest('nav, header, footer, aside, [class*="menu" i], [class*="filter" i], [class*="filtro" i]'));
    // el que queda més avall de la pàgina (sota els productes), i millor si parla de productes
    cand.sort((x, y) => (/produt|product|result/i.test(txt(y)) - /produt|product|result/i.test(txt(x))) || (y.getBoundingClientRect().top - x.getBoundingClientRect().top));
    const b = cand[0];
    if (b) { try { b.scrollIntoView(); b.click(); } catch (e) { } }
    else window.scrollTo(0, document.body.scrollHeight);
    for (let i = 0; i < 16; i++) { await espera(400); if (document.querySelectorAll('a[href]').length > abans + 2) return true; }
    return false;
  }

  // A la fitxa d'un producte: prem «Adicionar» (el del producte principal) i puja la quantitat fins a q
  const visible = (x) => { const b = x.getBoundingClientRect(); return b.width > 0 && b.height > 0 && getComputedStyle(x).visibility != 'hidden'; };
  async function afegeix(q) {
    rebutjaGaletes();
    let bt = null;
    for (let i = 0; i < 25 && !bt; i++) {
      const l = Array.from(document.querySelectorAll('button, a, [role="button"], input[type="submit"]')).filter((x) => (RX_AFEGIR.test(txt(x)) || RX_AFEGIR.test(x.value || '') || /add.?to.?cart|adicionar/i.test(x.getAttribute('aria-label') || '')) && visible(x) && !x.disabled);
      // el del producte principal: fora de carrusels de «també et pot interessar», i el més amunt
      const pri = l.filter((x) => !x.closest('[class*="carousel" i], [class*="recommend" i], [class*="slider" i], [class*="related" i], footer'));
      bt = (pri.length ? pri : l).sort((a, b) => a.getBoundingClientRect().top - b.getBoundingClientRect().top)[0] || null;
      if (!bt) await espera(400);
    }
    if (!bt) return { ok: false, error: 'No trobo el botó d\'afegir' };
    bt.scrollIntoView({ block: 'center' }); bt.click();
    await espera(1500);
    let fets = 1;
    for (let k = 1; k < (q || 1); k++) {
      // botó «+» al costat (el que ha aparegut en lloc d'«Adicionar») o, si no n'hi ha, torna a prémer «Adicionar»
      const cont = bt.closest('form, [class*="product" i], [class*="add" i], div') || document;
      const mes = Array.from(document.querySelectorAll('button, [role="button"]')).filter((x) => visible(x) && (/^\+$/.test(txt(x)) || /increase|aumentar|mais|incrementar|plus/i.test((x.getAttribute('aria-label') || '') + ' ' + (x.className || '')))).sort((a, b) => Math.abs(a.getBoundingClientRect().top - bt.getBoundingClientRect().top) - Math.abs(b.getBoundingClientRect().top - bt.getBoundingClientRect().top))[0];
      const b2 = mes || (visible(bt) ? bt : null);
      if (!b2) break;
      b2.click(); fets++;
      await espera(1100);
    }
    return { ok: true, q: fets };
  }
  // Adreça del carro de la botiga
  function cistell() {
    const a = Array.from(document.querySelectorAll('a[href]')).find((x) => /cart|carrinho|carrito|cistella|panier|basket|trolley|carrello/i.test(x.getAttribute('href')));
    return a ? a.href : '';
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

  window.__bt = { v: 7, afegeix: afegeix, cistell: cistell, sfcc: () => !!document.querySelector('script[src*="demandware"]'), productes: productes, enllacos: enllacos, mes: mes, llest: llest };
})();
