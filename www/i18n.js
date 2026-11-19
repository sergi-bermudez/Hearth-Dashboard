/* Hearth · idiomes. El text font és en català; els altres idiomes són a lang/<codi>.js
   Per afegir un idioma: copia lang/en.js, tradueix-lo i afegeix-lo a LLENGUES. */
var LLENGUES = [['ca', 'Català'], ['es', 'Español'], ['en', 'English'], ['pt', 'Português'], ['fr', 'Français']];
var TR = {};
var LANG = (function () {
  var l = null;
  try { l = JSON.parse(localStorage.getItem('casa.llengua') || 'null'); } catch (e) { }
  if (!l) {
    var n = (navigator.language || navigator.userLanguage || 'en').toLowerCase().substring(0, 2);
    l = 'en';
    for (var i = 0; i < LLENGUES.length; i++) if (LLENGUES[i][0] == n) l = n;
  }
  return l;
})();
if (LANG != 'ca') document.write('<script src="lang/' + LANG + '.js"><\/script>');

function T(s) { var d = TR[LANG]; return d && d[s] ? d[s] : s; }
/* T amb valors: Tf('Pujades {1} de {2}', a, b) */
function Tf(s) {
  var r = T(s);
  for (var i = 1; i < arguments.length; i++) r = r.split('{' + i + '}').join(arguments[i]);
  return r;
}
function canviaLlengua(l) {
  try { localStorage.setItem('casa.llengua', JSON.stringify(l)); } catch (e) { }
}
/* Tradueix el text fix de la pàgina (nodes de text, placeholder i title) */
/* Les apps creen text amb JavaScript: es tradueix també el que s'afegeix després */
function vigilaTraduccio() {
  if (LANG == 'ca' || !window.MutationObserver || !document.body) return;
  new MutationObserver(function (ms) {
    for (var i = 0; i < ms.length; i++) {
      var a = ms[i].addedNodes;
      for (var j = 0; j < a.length; j++) if (a[j].nodeType == 1 || a[j].nodeType == 3) traduirDOM(a[j]);
    }
  }).observe(document.body, { childList: true, subtree: true });
}
function traduirDOM(arrel) {
  var d = TR[LANG];
  if (!d) return;
  arrel = arrel || document.body;
  var pila = [arrel];
  while (pila.length) {
    var n = pila.pop();
    if (n.nodeType == 3) {
      var t = n.nodeValue, net = t.replace(/^\s+|\s+$/g, '');
      if (net && !/^[\d\s:.,·\/%-]*$/.test(net)) {
        if (d[net]) n.nodeValue = t.replace(net, d[net]);
        else {
          /* Amb signes al principi o al final: "★ Les meves", "Direcció:" */
          var m = /^([★▸›‹•·\s]*)(.*?)([\s:…?!.]*)$/.exec(net);
          if (m && m[2] && d[m[2]]) n.nodeValue = t.replace(net, m[1] + d[m[2]] + m[3]);
        }
      }
      continue;
    }
    if (n.nodeType != 1 || n.tagName == 'SCRIPT' || n.tagName == 'STYLE') continue;
    var ph = n.getAttribute('placeholder'), ti = n.getAttribute('title');
    if (ph && d[ph]) n.setAttribute('placeholder', d[ph]);
    if (ti && d[ti]) n.setAttribute('title', d[ti]);
    for (var c = n.firstChild; c; c = c.nextSibling) pila.push(c);
  }
}

/* ---------- Dates ---------- */
var DATES = {
  ca: { dies: ['diumenge', 'dilluns', 'dimarts', 'dimecres', 'dijous', 'divendres', 'dissabte'], diesc: ['dg', 'dl', 'dt', 'dc', 'dj', 'dv', 'ds'],
    mesos: ['gener', 'febrer', 'març', 'abril', 'maig', 'juny', 'juliol', 'agost', 'setembre', 'octubre', 'novembre', 'desembre'] },
  es: { dies: ['domingo', 'lunes', 'martes', 'miércoles', 'jueves', 'viernes', 'sábado'], diesc: ['do', 'lu', 'ma', 'mi', 'ju', 'vi', 'sá'],
    mesos: ['enero', 'febrero', 'marzo', 'abril', 'mayo', 'junio', 'julio', 'agosto', 'septiembre', 'octubre', 'noviembre', 'diciembre'] },
  en: { dies: ['Sunday', 'Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday'], diesc: ['Sun', 'Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat'],
    mesos: ['January', 'February', 'March', 'April', 'May', 'June', 'July', 'August', 'September', 'October', 'November', 'December'] },
  pt: { dies: ['domingo', 'segunda-feira', 'terça-feira', 'quarta-feira', 'quinta-feira', 'sexta-feira', 'sábado'], diesc: ['dom', 'seg', 'ter', 'qua', 'qui', 'sex', 'sáb'],
    mesos: ['janeiro', 'fevereiro', 'março', 'abril', 'maio', 'junho', 'julho', 'agosto', 'setembro', 'outubro', 'novembro', 'dezembro'] },
  fr: { dies: ['dimanche', 'lundi', 'mardi', 'mercredi', 'jeudi', 'vendredi', 'samedi'], diesc: ['dim', 'lun', 'mar', 'mer', 'jeu', 'ven', 'sam'],
    mesos: ['janvier', 'février', 'mars', 'avril', 'mai', 'juin', 'juillet', 'août', 'septembre', 'octobre', 'novembre', 'décembre'] }
};
var DL = DATES[LANG] || DATES.en;
var DIES = DL.dies, DIESC = DL.diesc, MESOS = DL.mesos;
function majuscula(s) { return s.charAt(0).toUpperCase() + s.substring(1); }
function deMes(i) { var m = MESOS[i]; return LANG == 'ca' ? ((/^[aeiou]/.test(m) ? "d'" : 'de ') + m) : (LANG == 'es' || LANG == 'pt') ? 'de ' + m : m; }
function dataLlarga(d) {
  if (LANG == 'en') return DIES[d.getDay()] + ', ' + d.getDate() + ' ' + MESOS[d.getMonth()];
  if (LANG == 'fr') return DIES[d.getDay()] + ' ' + d.getDate() + ' ' + MESOS[d.getMonth()];
  return DIES[d.getDay()] + ', ' + d.getDate() + ' ' + deMes(d.getMonth());
}
function dataCurta(d) {
  if (LANG == 'en' || LANG == 'fr') return d.getDate() + ' ' + MESOS[d.getMonth()] + ' ' + d.getFullYear();
  return d.getDate() + ' ' + deMes(d.getMonth()) + ' de ' + d.getFullYear();
}
function mesAny(m, a) {
  var n = majuscula(MESOS[m]);
  return (LANG == 'en' || LANG == 'fr') ? n + ' ' + a : n + ' de ' + a;
}
function haFet(ms) {
  var s = (Date.now() - ms) / 1000;
  if (s < 60) return T('ara mateix');
  if (s < 3600) return Tf('fa {1} min', Math.floor(s / 60));
  if (s < 86400) return Tf('fa {1} h', Math.floor(s / 3600));
  if (s < 86400 * 7) { var d = Math.floor(s / 86400); return d == 1 ? T('ahir') : Tf('fa {1} dies', d); }
  return dataCurta(new Date(ms));
}
if (document.addEventListener) document.addEventListener('DOMContentLoaded', function () { traduirDOM(); vigilaTraduccio(); }, false);

/* ---------- País (per a ràdios i televisions) ---------- */
var LLENGUA_INFO = {
  ca: { radio: 'catalan', iso3: 'cat', pais: 'ES' }, es: { radio: 'spanish', iso3: 'spa', pais: 'ES' },
  en: { radio: 'english', iso3: 'eng', pais: 'GB' }, pt: { radio: 'portuguese', iso3: 'por', pais: 'PT' },
  fr: { radio: 'french', iso3: 'fra', pais: 'FR' }
};
var PAISOS = [['AD', 'Andorra'], ['AR', 'Argentina'], ['AT', 'Àustria'], ['AU', 'Austràlia'], ['BE', 'Bèlgica'], ['BR', 'Brasil'],
  ['CA', 'Canadà'], ['CH', 'Suïssa'], ['CL', 'Xile'], ['CO', 'Colòmbia'], ['CU', 'Cuba'], ['CZ', 'Txèquia'], ['DE', 'Alemanya'], ['DK', 'Dinamarca'],
  ['EC', 'Equador'], ['ES', 'Espanya'], ['FI', 'Finlàndia'], ['FR', 'França'], ['GB', 'Regne Unit'], ['GR', 'Grècia'], ['IE', 'Irlanda'],
  ['IN', 'Índia'], ['IT', 'Itàlia'], ['JP', 'Japó'], ['MA', 'Marroc'], ['MX', 'Mèxic'], ['NL', 'Països Baixos'], ['NO', 'Noruega'],
  ['PE', 'Perú'], ['PL', 'Polònia'], ['PT', 'Portugal'], ['RO', 'Romania'], ['SE', 'Suècia'], ['TR', 'Turquia'], ['UA', 'Ucraïna'],
  ['US', 'Estats Units'], ['UY', 'Uruguai'], ['VE', 'Veneçuela'], ['AO', 'Angola'], ['MZ', 'Moçambic'], ['CV', 'Cap Verd']];
var PAIS = (function () {
  var p = null;
  try { p = JSON.parse(localStorage.getItem('casa.pais') || 'null'); } catch (e) { }
  if (!p) {
    var m = /[-_]([a-z]{2})$/i.exec(navigator.language || '');
    p = m ? m[1].toUpperCase() : (LLENGUA_INFO[LANG] || LLENGUA_INFO.en).pais;
  }
  return p;
})();
function canviaPais(p) { try { localStorage.setItem('casa.pais', JSON.stringify(p)); } catch (e) { } }
function nomPais(c) { for (var i = 0; i < PAISOS.length; i++) if (PAISOS[i][0] == c) return T(PAISOS[i][1]); return c; }
function paisosOrdenats() {
  var l = PAISOS.slice();
  l.sort(function (a, b) { return T(a[1]).localeCompare(T(b[1])); });
  return l;
}
