// Cat\u00e0leg de supermercats per a l'app Compra. Gen\u00e8ric: cada botiga \u00e9s nom\u00e9s una adre\u00e7a web
// (de qualsevol pa\u00eds). Cada nit l'Edge amagat en recorre les categories, en llegeix els productes,
// els preus i les promocions, i ho desa a dades/botigues_cat.json. C# 5.
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

public static class Botigues
{
    static readonly object lk = new object();
    static Timer rellotge;
    static volatile bool enMarxa;
    static string estatText = "";
    static volatile string llegintB = "";
    static Dictionary<string, object> cataleg; // en mem\u00f2ria

    // Categories comunes per a totes les botigues, amb paraules clau en diversos idiomes
    static readonly string[][] CATS = new string[][] {
        new string[] { "fruita", "frut", "legum", "verdur", "hortal", "fruit", "veg", "obst", "gemuse", "gem\u00fcse", "primeur" },
        new string[] { "carn", "talho", "carne", "carn", "meat", "viande", "boucher", "fleisch", "carni", "aves", "frango", "pollo" },
        new string[] { "peix", "peix", "pescad", "fish", "poisson", "fisch", "pesce", "marisc", "seafood" },
        new string[] { "pa", "padaria", "pastelaria", "p\u00e3o", "pao", "panader", "forn", "bread", "bakery", "boulang", "brot", "pane" },
        new string[] { "xarcuteria", "charcut", "queijo", "formatg", "queso", "cheese", "fromage", "k\u00e4se", "kase", "embutid", "fiambre", "deli" },
        new string[] { "lactis", "latic", "lactic", "l\u00e0ctic", "leite", "ovos", "dairy", "milk", "iogurt", "yogur", "yaourt", "egg", "oeuf", "huevo", "milch", "latte" },
        new string[] { "begudes", "bebida", "beguda", "drink", "boisson", "getr\u00e4nk", "getrank", "bevand", "agua", "\u00e1gua", "vinho", "vino", "vin", "cervej", "cervez", "beer" },
        new string[] { "congelats", "congel", "frozen", "surgel", "tiefk", "gelad" },
        new string[] { "neteja", "limpeza", "neteja", "limpieza", "cleaning", "household", "entretien", "drogeri", "detergen", "lar-e" },
        new string[] { "higiene", "higiene", "higien", "beleza", "belleza", "bellesa", "beauty", "hygi", "perfum", "cuidado-pessoal", "cuidado pessoal", "parafarm" },
        new string[] { "bebe", "beb\u00e9", "bebe", "baby", "nad\u00f3", "b\u00e9b\u00e9", "infantil" },
        new string[] { "animals", "animais", "animal", "mascot", "pets", "petshop", "animaux" },
        new string[] { "rebost", "mercearia", "despensa", "alimentaci", "pantry", "grocer", "\u00e9picerie", "epicerie", "rebost", "conserv", "arroz", "massa", "azeite", "aceite", "cereais", "cereal", "snack", "bolacha", "galleta" }
    };

    static string Ruta(string n) { return Path.Combine(CasaServidor.DirDades, n); }
    static JavaScriptSerializer Js() { JavaScriptSerializer j = new JavaScriptSerializer(); j.MaxJsonLength = int.MaxValue; return j; }

    // ---------- Configuraci\u00f3: llista de botigues ----------
    public static List<object> Config()
    {
        try
        {
            string f = Ruta("botigues_cfg.json");
            if (File.Exists(f))
            {
                Dictionary<string, object> d = Js().DeserializeObject(File.ReadAllText(f, Encoding.UTF8)) as Dictionary<string, object>;
                if (d != null && d.ContainsKey("botigues") && d["botigues"] is object[]) return new List<object>((object[])d["botigues"]);
            }
        }
        catch { }
        return new List<object>();
    }

    static string S(Dictionary<string, object> d, string k) { return d != null && d.ContainsKey(k) && d[k] != null ? d[k].ToString() : ""; }

    public static string Categoria(string text)
    {
        string t = (text ?? "").ToLowerInvariant();
        foreach (string[] c in CATS)
            for (int i = 1; i < c.Length; i++)
                if (t.Contains(c[i])) return c[0];
        return "";
    }

    // ---------- Recorregut ----------
    public static void Inicia()
    {
        rellotge = new Timer(delegate
        {
            try
            {
                if (DateTime.Now.Hour != 6 || enMarxa) return;
                if (Config().Count == 0) return;
                Dictionary<string, object> c = Cataleg();
                double t = c != null && c.ContainsKey("t") ? Convert.ToDouble(c["t"]) : 0;
                if ((DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalMilliseconds - t < 20 * 3600 * 1000) return;
                Recorre("");
            }
            catch { }
        }, null, TimeSpan.FromMinutes(7), TimeSpan.FromMinutes(30));
    }

    static string Pont() { return File.ReadAllText(Path.Combine(CasaServidor.DirArrel, "botigues-pont.js"), Encoding.UTF8); }

    static object Crida(Cdp c, string expr, int ms)
    {
        string js = "(async () => { " + Pont() + "\n return JSON.stringify(await window.__bt." + expr + "); })()";
        object v = Edge.Avalua(c, js, ms);
        return v == null ? null : Js().DeserializeObject(v.ToString());
    }

    static void Navega(Cdp c, string url)
    {
        c.Envia("Page.navigate", CasaServidor.Dic("url", url), 20000);
        Thread.Sleep(2500);
        try { Crida(c, "llest()", 20000); } catch { Thread.Sleep(2000); }
    }

    // nomesB: llegeix nom\u00e9s aquesta botiga (les altres es queden com estaven); "" = totes
    public static void Recorre(string nomesB)
    {
        lock (lk) { if (enMarxa) return; enMarxa = true; llegintB = nomesB; }
        Thread th = new Thread(delegate ()
        {
            string pagina = null;
            try
            {
                Dictionary<string, object> vell = Cataleg();
                Dictionary<string, Dictionary<string, object>> anteriors = new Dictionary<string, Dictionary<string, object>>();
                if (vell != null && vell.ContainsKey("prod") && vell["prod"] is object[])
                    foreach (object o in (object[])vell["prod"]) { Dictionary<string, object> p = o as Dictionary<string, object>; if (p != null) anteriors[S(p, "u")] = p; }

                List<object> prods = new List<object>();
                Dictionary<string, object> estats = new Dictionary<string, object>();
                Dictionary<string, bool> llegides = new Dictionary<string, bool>();
                // L'estat de les botigues que no es llegeixen ara es conserva
                if (vell != null && vell.ContainsKey("botigues") && vell["botigues"] is Dictionary<string, object>)
                    foreach (KeyValuePair<string, object> kv in (Dictionary<string, object>)vell["botigues"]) estats[kv.Key] = kv.Value;
                Edge.Assegura();
                pagina = Edge.NovaPagina("about:blank");
                Cdp c = new Cdp();
                c.Connecta(Edge.WsPagina(pagina));
                c.Envia("Page.enable", null, 5000);
                try { c.Envia("Emulation.setDeviceMetricsOverride", CasaServidor.Dic("width", 1300, "height", 1000, "deviceScaleFactor", 1, "mobile", false), 5000); } catch { }

                foreach (object ob in Config())
                {
                    Dictionary<string, object> b = ob as Dictionary<string, object>;
                    if (b == null || S(b, "url") == "" || S(b, "off") == "True") continue;
                    string id = S(b, "id"), nom = S(b, "nom");
                    if (nomesB != "" && id != nomesB) continue;
                    llegides[id] = true;
                    Dictionary<string, object> est = new Dictionary<string, object>();
                    estats[id] = est;
                    int n0 = prods.Count;
                    try
                    {
                        estatText = nom + ": " + "categories";
                        Navega(c, S(b, "url"));
                        // Categories: les que s'han posat a m\u00e0 o les del men\u00fa de la web
                        List<string[]> cats = new List<string[]>();
                        if (b.ContainsKey("cats") && b["cats"] is object[])
                            foreach (object u in (object[])b["cats"]) { string us = u.ToString(); cats.Add(new string[] { us, Categoria(us) }); }
                        if (cats.Count == 0)
                        {
                            object[] ls = Crida(c, "enllacos()", 15000) as object[];
                            Dictionary<string, int> perCat = new Dictionary<string, int>();
                            if (ls != null)
                            {
                                List<Dictionary<string, object>> l = new List<Dictionary<string, object>>();
                                foreach (object o in ls) { Dictionary<string, object> e = o as Dictionary<string, object>; if (e != null) l.Add(e); }
                                l.Sort(delegate (Dictionary<string, object> x, Dictionary<string, object> y) { return Convert.ToInt32(x["d"]).CompareTo(Convert.ToInt32(y["d"])); });
                                foreach (Dictionary<string, object> e in l)
                                {
                                    string cat = Categoria(S(e, "t"));
                                    if (cat == "") cat = Categoria(S(e, "u"));
                                    if (cat == "") continue;
                                    int k; perCat.TryGetValue(cat, out k);
                                    if (k >= 3) continue;
                                    perCat[cat] = k + 1;
                                    cats.Add(new string[] { S(e, "u"), cat });
                                }
                            }
                        }
                        est["cats"] = cats.Count;
                        Dictionary<string, bool> vistos = new Dictionary<string, bool>();
                        int maxPag = 12, maxProd = 5000;
                        foreach (string[] cu in cats)
                        {
                            if (prods.Count - n0 >= maxProd) break;
                            estatText = nom + ": " + cu[1] + " (" + (prods.Count - n0) + ")";
                            try
                            {
                                Navega(c, cu[0]);
                                for (int pg = 0; pg < maxPag; pg++)
                                {
                                    object r = Crida(c, "mes()", 12000);
                                    if (!(r is bool) || !(bool)r) break;
                                    Thread.Sleep(600);
                                }
                                object[] ps = Crida(c, "productes()", 20000) as object[];
                                if (ps == null) continue;
                                foreach (object o in ps)
                                {
                                    Dictionary<string, object> p = o as Dictionary<string, object>;
                                    if (p == null) continue;
                                    string u = S(p, "u");
                                    if (u == "" || vistos.ContainsKey(u)) continue;
                                    vistos[u] = true;
                                    p["b"] = id;
                                    p["c"] = cu[1];
                                    double preu = Convert.ToDouble(p["p"]);
                                    double min = preu;
                                    Dictionary<string, object> ant;
                                    if (anteriors.TryGetValue(u, out ant) && ant.ContainsKey("min")) min = Math.Min(min, Convert.ToDouble(ant["min"]));
                                    p["min"] = min;
                                    prods.Add(p);
                                }
                            }
                            catch { }
                            Thread.Sleep(1200); // amb calma: no carregar la web de la botiga
                        }
                        est["n"] = prods.Count - n0;
                        if (prods.Count == n0) est["error"] = "No he trobat productes";
                    }
                    catch (Exception ex) { est["error"] = ex.Message; }
                    // Si una botiga ha fallat, conserva els productes d'abans
                    if (prods.Count == n0)
                        foreach (Dictionary<string, object> p in anteriors.Values) if (S(p, "b") == id) prods.Add(p);
                }
                try { c.Tanca(); } catch { }
                // Els productes de les botigues que no s'han llegit ara (i que encara s\u00f3n a la configuraci\u00f3) es conserven
                Dictionary<string, bool> actives = new Dictionary<string, bool>();
                foreach (object ob2 in Config()) { Dictionary<string, object> b2 = ob2 as Dictionary<string, object>; if (b2 != null) actives[S(b2, "id")] = true; }
                foreach (Dictionary<string, object> pa in anteriors.Values) { string bb = S(pa, "b"); if (!llegides.ContainsKey(bb) && actives.ContainsKey(bb)) prods.Add(pa); }
                List<string> fora = new List<string>();
                foreach (string k in estats.Keys) if (!actives.ContainsKey(k)) fora.Add(k);
                foreach (string k in fora) estats.Remove(k);
                Dictionary<string, object> nou = new Dictionary<string, object>();
                nou["t"] = (DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalMilliseconds;
                nou["botigues"] = estats;
                nou["prod"] = prods.ToArray();
                File.WriteAllText(Ruta("botigues_cat.json"), Js().Serialize(nou), new UTF8Encoding(false));
                lock (lk) cataleg = nou;
                estatText = "";
            }
            catch (Exception ex) { estatText = "Error: " + ex.Message; }
            finally
            {
                if (pagina != null) { try { Edge.TancaPagina(pagina); } catch { } }
                enMarxa = false; llegintB = "";
            }
        });
        th.IsBackground = true;
        th.Start();
    }

    static Dictionary<string, object> Cataleg()
    {
        lock (lk)
        {
            if (cataleg != null) return cataleg;
            try
            {
                string f = Ruta("botigues_cat.json");
                if (File.Exists(f)) cataleg = Js().DeserializeObject(File.ReadAllText(f, Encoding.UTF8)) as Dictionary<string, object>;
            }
            catch { }
            return cataleg;
        }
    }

    static string Pla(string s)
    {
        string d = (s ?? "").ToLowerInvariant().Normalize(NormalizationForm.FormD);
        StringBuilder sb = new StringBuilder();
        foreach (char ch in d) if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(ch) != System.Globalization.UnicodeCategory.NonSpacingMark) sb.Append(ch);
        return sb.ToString();
    }

    // ---------- API ----------
    // compra/estat \u00b7 compra/productes?cat=&q=&b=&promo=1&max=60
    // admin/compra/botigues (GET/POST) \u00b7 admin/compra/actualitza
    public static void Api(string nom, Dictionary<string, string> q, HttpListenerContext ctx, bool admin)
    {
        HttpListenerResponse res = ctx.Response;
        if (nom == "estat")
        {
            Dictionary<string, object> c = Cataleg();
            Dictionary<string, object> d = new Dictionary<string, object>();
            d["botigues"] = Config().ToArray();
            d["actualitzat"] = c != null && c.ContainsKey("t") ? c["t"] : 0;
            d["estat"] = c != null && c.ContainsKey("botigues") ? c["botigues"] : null;
            d["n"] = c != null && c.ContainsKey("prod") && c["prod"] is object[] ? ((object[])c["prod"]).Length : 0;
            d["enMarxa"] = enMarxa;
            d["fent"] = estatText;
            d["llegint"] = llegintB;
            Dictionary<string, int> cats = new Dictionary<string, int>();
            int promos = 0;
            if (c != null && c["prod"] is object[])
                foreach (object o in (object[])c["prod"])
                {
                    Dictionary<string, object> p = o as Dictionary<string, object>;
                    if (p == null) continue;
                    string k = S(p, "c"); int n; cats.TryGetValue(k, out n); cats[k] = n + 1;
                    if (Convert.ToInt32(p.ContainsKey("promo") ? p["promo"] : 0) == 1) promos++;
                }
            d["cats"] = cats;
            d["promos"] = promos;
            CasaServidor.Json(res, d);
            return;
        }
        if (nom == "productes")
        {
            Dictionary<string, object> c = Cataleg();
            List<Dictionary<string, object>> r = new List<Dictionary<string, object>>();
            string cat = CasaServidor.Q(q, "cat"), b = CasaServidor.Q(q, "b"), text = Pla(CasaServidor.Q(q, "q"));
            bool promo = CasaServidor.Q(q, "promo") == "1";
            int max = 60; int.TryParse(CasaServidor.Q(q, "max"), out max); if (max <= 0 || max > 300) max = 60;
            string[] paraules = text.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (c != null && c["prod"] is object[])
                foreach (object o in (object[])c["prod"])
                {
                    Dictionary<string, object> p = o as Dictionary<string, object>;
                    if (p == null) continue;
                    if (cat != "" && S(p, "c") != cat) continue;
                    if (b != "" && S(p, "b") != b) continue;
                    if (promo && S(p, "promo") != "1") continue;
                    if (paraules.Length > 0)
                    {
                        string nn = Pla(S(p, "n") + " " + S(p, "m"));
                        bool ok = true;
                        foreach (string w in paraules) if (!nn.Contains(w)) { ok = false; break; }
                        if (!ok) continue;
                    }
                    r.Add(p);
                }
            // Promocions: primer el descompte m\u00e9s gran; cerca: primer els noms m\u00e9s curts (m\u00e9s gen\u00e8rics) i m\u00e9s barats
            if (promo)
                r.Sort(delegate (Dictionary<string, object> x, Dictionary<string, object> y) { return Desc(y).CompareTo(Desc(x)); });
            else if (paraules.Length > 0)
                r.Sort(delegate (Dictionary<string, object> x, Dictionary<string, object> y) { int a = S(x, "n").Length.CompareTo(S(y, "n").Length); return a != 0 ? a : Convert.ToDouble(x["p"]).CompareTo(Convert.ToDouble(y["p"])); });
            if (r.Count > max) r = r.GetRange(0, max);
            CasaServidor.Json(res, r.ToArray());
            return;
        }
        if (nom == "millors")
        {
            // Rep una llista de conceptes ("leite meio gordo -chocolate") i, per a cada un,
            // torna les opcions m\u00e9s barates (per preu unitari) i la millor de cada botiga
            string text;
            using (StreamReader sr = new StreamReader(ctx.Request.InputStream, Encoding.UTF8)) text = sr.ReadToEnd();
            object[] qs = Js().DeserializeObject(text) as object[];
            Dictionary<string, object> c = Cataleg();
            object[] prods = c != null && c.ContainsKey("prod") ? c["prod"] as object[] : null;
            List<object> sortida = new List<object>();
            if (qs != null)
                foreach (object qo in qs)
                {
                    // "tomate cherry|tomate mini -polpa @fruita": alternatives separades per |,
                    // paraules a excloure amb -, i categoria obligat\u00f2ria amb @
                    List<List<string>> alts = new List<List<string>>();
                    List<string> no = new List<string>();
                    string catQ = "";
                    foreach (string alt in Pla(qo == null ? "" : qo.ToString()).Split('|'))
                    {
                        List<string> si = new List<string>();
                        foreach (string w in alt.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
                            if (w.StartsWith("-")) { if (w.Length > 1) no.Add(w.Substring(1)); }
                            else if (w.StartsWith("@")) catQ = w.Substring(1);
                            else si.Add(w);
                        if (si.Count > 0) alts.Add(si);
                    }
                    List<Dictionary<string, object>> r = new List<Dictionary<string, object>>(), rCat = new List<Dictionary<string, object>>();
                    if (prods != null && alts.Count > 0)
                        foreach (object o in prods)
                        {
                            Dictionary<string, object> p = o as Dictionary<string, object>;
                            if (p == null || S(p, "esg") == "1") continue;
                            string nn = " " + Pla(S(p, "n") + " " + S(p, "m")) + " ";
                            bool ok = false;
                            foreach (List<string> si in alts)
                            {
                                bool totes = true;
                                foreach (string w in si) if (!Paraula(nn, w)) { totes = false; break; }
                                if (totes) { ok = true; break; }
                            }
                            if (ok) foreach (string w in no) if (nn.Contains(w)) { ok = false; break; }
                            if (ok) { r.Add(p); if (S(p, "c") == catQ) rCat.Add(p); }
                        }
                    // Si la botiga classifica b\u00e9 la secci\u00f3, nom\u00e9s d'aquella categoria; si no, de totes
                    if (catQ != "" && rCat.Count > 0) r = rCat;
                    r.Sort(delegate (Dictionary<string, object> x, Dictionary<string, object> y) { return PreuRef(x).CompareTo(PreuRef(y)); });
                    Dictionary<string, object> perB = new Dictionary<string, object>();
                    foreach (Dictionary<string, object> p in r) { string b = S(p, "b"); if (!perB.ContainsKey(b)) perB[b] = p; }
                    Dictionary<string, object> e = new Dictionary<string, object>();
                    e["n"] = r.Count;
                    e["top"] = (r.Count > 8 ? r.GetRange(0, 8) : r).ToArray();
                    e["perBotiga"] = perB;
                    sortida.Add(e);
                }
            CasaServidor.Json(res, sortida.ToArray());
            return;
        }
        if (!admin) { CasaServidor.Error(res, 404, "No"); return; }
        if (nom == "botigues" && ctx.Request.HttpMethod == "POST")
        {
            string text;
            using (StreamReader sr = new StreamReader(ctx.Request.InputStream, Encoding.UTF8)) text = sr.ReadToEnd();
            object d = Js().DeserializeObject(text);
            File.WriteAllText(Ruta("botigues_cfg.json"), Js().Serialize(d), new UTF8Encoding(false));
            CasaServidor.Json(res, CasaServidor.Dic("ok", true));
            return;
        }
        if (nom == "actualitza")
        {
            if (Config().Count == 0) { CasaServidor.Error(res, 400, "No hi ha cap botiga configurada"); return; }
            if (enMarxa) { CasaServidor.Error(res, 409, "Ja s'esta llegint una botiga; espera que acabi"); return; }
            Recorre(CasaServidor.Q(q, "b"));
            CasaServidor.Json(res, CasaServidor.Dic("ok", true));
            return;
        }
        CasaServidor.Error(res, 404, "No");
    }

    // Preu de refer\u00e8ncia: el preu per kg/litre/unitat si la web el dona; si no, el preu
    static double PreuRef(Dictionary<string, object> p)
    {
        double pu = p.ContainsKey("pu") && p["pu"] != null ? Convert.ToDouble(p["pu"]) : 0;
        return pu > 0 ? pu : Convert.ToDouble(p["p"]);
    }

    // La paraula ha de comen\u00e7ar una paraula del nom ("ovo" troba "ovos" per\u00f2 no "novo")
    static bool Paraula(string nom, string w)
    {
        int i = nom.IndexOf(w, StringComparison.Ordinal);
        while (i >= 0)
        {
            if (i == 0 || !char.IsLetterOrDigit(nom[i - 1])) return true;
            i = nom.IndexOf(w, i + 1, StringComparison.Ordinal);
        }
        return false;
    }

    static double Desc(Dictionary<string, object> p)
    {
        double pv = p.ContainsKey("pv") ? Convert.ToDouble(p["pv"]) : 0, pr = Convert.ToDouble(p["p"]);
        return pv > pr ? (pv - pr) / pv : 0.001;
    }
}
