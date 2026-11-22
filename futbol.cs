// Futbol: partits, equips i calendari de cada equip (dades d'ESPN), demanats pel servidor.
// El servidor ho baixa, ho guarda una estona i n'envia nomes el que cal: a l'iPad li arriba poc i rapid.
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

static class Futbol
{
    static readonly object lk = new object();
    static readonly Dictionary<string, KeyValuePair<DateTime, object>> cau = new Dictionary<string, KeyValuePair<DateTime, object>>();
    const string Base = "https://site.api.espn.com/apis/site/v2/sports/soccer/";

    static object Json(string url, int segons)
    {
        lock (lk)
        {
            KeyValuePair<DateTime, object> kv;
            if (cau.TryGetValue(url, out kv) && kv.Key > DateTime.Now) return kv.Value;
        }
        HttpWebRequest rq = (HttpWebRequest)WebRequest.Create(url);
        rq.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0 Safari/537.36";
        rq.Accept = "application/json";
        rq.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
        rq.Timeout = 15000; rq.ReadWriteTimeout = 20000;
        string txt;
        using (WebResponse rs = rq.GetResponse())
        using (StreamReader sr = new StreamReader(rs.GetResponseStream(), Encoding.UTF8)) txt = sr.ReadToEnd();
        JavaScriptSerializer js = new JavaScriptSerializer(); js.MaxJsonLength = int.MaxValue;
        object o = js.DeserializeObject(txt);
        lock (lk)
        {
            if (cau.Count > 500) cau.Clear();
            cau[url] = new KeyValuePair<DateTime, object>(DateTime.Now.AddSeconds(segons), o);
        }
        return o;
    }

    static string S(object d, string k)
    {
        Dictionary<string, object> x = d as Dictionary<string, object>;
        return x != null && x.ContainsKey(k) && x[k] != null ? Convert.ToString(x[k], System.Globalization.CultureInfo.InvariantCulture) : "";
    }
    static Dictionary<string, object> D(object d, string k) { Dictionary<string, object> x = d as Dictionary<string, object>; return x != null && x.ContainsKey(k) ? x[k] as Dictionary<string, object> : null; }
    static object[] L(object d, string k) { Dictionary<string, object> x = d as Dictionary<string, object>; return x != null && x.ContainsKey(k) ? x[k] as object[] : null; }

    // Un partit d'ESPN, reduit al que pinta la pagina
    static Dictionary<string, object> Partit(object e, string lg)
    {
        object[] comps = L(e, "competitions");
        object c = comps != null && comps.Length > 0 ? comps[0] : null;
        object[] cs = L(c, "competitors");
        if (cs == null || cs.Length < 2) return null;
        Dictionary<string, object> st = D(D(e, "status"), "type") ?? D(D(c, "status"), "type");
        Dictionary<string, object> p = new Dictionary<string, object>();
        p["id"] = S(e, "id"); p["lg"] = lg; p["data"] = S(e, "date");
        p["estat"] = st != null && S(st, "state") != "" ? S(st, "state") : "pre";
        p["detall"] = S(st, "shortDetail");
        p["rellotge"] = S(D(e, "status") ?? D(c, "status"), "displayClock");
        List<object> eqs = new List<object>();
        foreach (object x in cs)
        {
            Dictionary<string, object> t = D(x, "team");
            Dictionary<string, object> o = new Dictionary<string, object>();
            o["id"] = S(t, "id");
            o["nom"] = S(t, "shortDisplayName") != "" ? S(t, "shortDisplayName") : S(t, "displayName");
            o["llarg"] = S(t, "displayName");
            string logo = S(t, "logo");
            if (logo == "") { object[] ls = L(t, "logos"); if (ls != null && ls.Length > 0) logo = S(ls[0], "href"); }
            o["logo"] = logo;
            // al calendari d'un equip el marcador ve com a objecte
            string gols = S(x, "score");
            Dictionary<string, object> sc = D(x, "score");
            if (sc != null) gols = S(sc, "displayValue");
            o["gols"] = gols;
            o["casa"] = S(x, "homeAway") == "home";
            o["guanya"] = S(x, "winner").ToLowerInvariant() == "true";
            if ((bool)o["casa"]) eqs.Insert(0, o); else eqs.Add(o);
        }
        p["eq"] = eqs;
        List<object> tv = new List<object>();
        object[] bs = L(c, "broadcasts");
        if (bs != null) foreach (object b in bs) { object[] ns = L(b, "names"); if (ns != null) foreach (object n in ns) if (!tv.Contains(n)) tv.Add(n); }
        object[] gb = L(c, "geoBroadcasts");
        if (gb != null) foreach (object b in gb) { string n = S(D(b, "media"), "shortName"); if (n != "" && !tv.Contains(n)) tv.Add(n); }
        p["tv"] = tv;
        return p;
    }

    static bool Es400(Exception e)
    {
        WebException we = e as WebException;
        HttpWebResponse hr = we != null ? we.Response as HttpWebResponse : null;
        return hr != null && (int)hr.StatusCode == 400;
    }

    // ESPN ja no sempre accepta un interval de dates per al futbol (respon 400): llavors es demana dia per dia
    static List<object> Scoreboard(string lg, string d1, string d2)
    {
        List<object> r = new List<object>();
        string b = Base + Uri.EscapeDataString(lg) + "/scoreboard";
        try
        {
            object[] ev = L(Json(b + "?limit=300&dates=" + d1 + "-" + d2, 60), "events");
            if (ev != null) r.AddRange(ev);
            return r;
        }
        catch (Exception e) { if (!Es400(e)) throw; }
        DateTime a = DateTime.ParseExact(d1, "yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture);
        DateTime z = DateTime.ParseExact(d2, "yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture);
        HashSet<string> vist = new HashSet<string>();
        Exception darrer = null; int bons = 0;
        for (DateTime d = a; d <= z && d < a.AddDays(20); d = d.AddDays(1))
        {
            try
            {
                object[] ev = L(Json(b + "?dates=" + d.ToString("yyyyMMdd"), d.Date == DateTime.Today ? 60 : 1800), "events");
                bons++;
                if (ev != null) foreach (object e in ev) if (vist.Add(S(e, "id"))) r.Add(e);
            }
            catch (Exception e) { darrer = e; }
        }
        if (bons == 0)
        {
            // ni dia per dia: com a minim, el d'avui (sense data)
            object[] ev = L(Json(b, 60), "events");
            if (ev != null) r.AddRange(ev);
            else if (darrer != null) throw darrer;
        }
        return r;
    }

    public static void Api(string nom, Dictionary<string, string> q, HttpListenerContext ctx)
    {
        HttpListenerResponse res = ctx.Response;
        try
        {
            switch (nom)
            {
                case "partits":
                    {
                        // ?lg=por.1,esp.1&d1=20261120&d2=20261130  -> partits de totes, i els errors per lliga
                        string[] lgs = CasaServidor.Q(q, "lg").Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                        string d1 = CasaServidor.Q(q, "d1"), d2 = CasaServidor.Q(q, "d2");
                        List<object> tots = new List<object>();
                        Dictionary<string, object> errors = new Dictionary<string, object>();
                        object bloc = new object();
                        CountdownEvent fet = new CountdownEvent(lgs.Length);
                        foreach (string lg0 in lgs)
                        {
                            string lg = lg0;
                            ThreadPool.QueueUserWorkItem(delegate
                            {
                                try
                                {
                                    foreach (object e in Scoreboard(lg, d1, d2)) { Dictionary<string, object> p = Partit(e, lg); if (p != null) lock (bloc) tots.Add(p); }
                                }
                                catch (Exception ex) { lock (bloc) errors[lg] = ex.Message; }
                                try { fet.Signal(); } catch { }
                            });
                        }
                        fet.Wait(25000);
                        Dictionary<string, object> r = new Dictionary<string, object>();
                        lock (bloc) { r["partits"] = new List<object>(tots); r["errors"] = new Dictionary<string, object>(errors); }
                        CasaServidor.Json(res, r);
                        return;
                    }
                case "equips":
                    {
                        // Tots els equips de les lligues conegudes (per buscar-ne un, encara que no jugui aquests dies)
                        string[] lgs = CasaServidor.Q(q, "lg").Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                        List<object> l = new List<object>();
                        HashSet<string> vist = new HashSet<string>();
                        object bloc = new object();
                        CountdownEvent fet = new CountdownEvent(lgs.Length);
                        foreach (string lg0 in lgs)
                        {
                            string lg = lg0;
                            ThreadPool.QueueUserWorkItem(delegate
                            {
                                try
                                {
                                    object o = Json(Base + Uri.EscapeDataString(lg) + "/teams?limit=500", 7 * 86400);
                                    object[] sp = L(o, "sports");
                                    object[] lgl = sp != null && sp.Length > 0 ? L(sp[0], "leagues") : null;
                                    object[] tl = lgl != null && lgl.Length > 0 ? L(lgl[0], "teams") : null;
                                    if (tl != null)
                                        foreach (object x in tl)
                                        {
                                            Dictionary<string, object> t = D(x, "team");
                                            string id = S(t, "id");
                                            if (id == "") continue;
                                            Dictionary<string, object> e = new Dictionary<string, object>();
                                            e["id"] = id; e["nom"] = S(t, "displayName"); e["curt"] = S(t, "shortDisplayName"); e["lg"] = lg;
                                            object[] ls = L(t, "logos"); e["logo"] = ls != null && ls.Length > 0 ? S(ls[0], "href") : "";
                                            lock (bloc) { if (vist.Add(id)) l.Add(e); }
                                        }
                                }
                                catch { }
                                try { fet.Signal(); } catch { }
                            });
                        }
                        fet.Wait(25000);
                        lock (bloc) CasaServidor.Json(res, new List<object>(l));
                        return;
                    }
                case "equip":
                    {
                        // Calendari i resultats d'un equip (tots els seus partits, de qualsevol competicio que ESPN tingui)
                        string lg = CasaServidor.Q(q, "lg"), id = CasaServidor.Q(q, "id");
                        List<object> l = new List<object>();
                        foreach (string tipus in new string[] { "", "&fixture=true" })
                        {
                            try
                            {
                                object o = Json(Base + Uri.EscapeDataString(lg) + "/teams/" + Uri.EscapeDataString(id) + "/schedule?x=1" + tipus, 600);
                                object[] ev = L(o, "events");
                                if (ev != null) foreach (object e in ev) { Dictionary<string, object> p = Partit(e, lg); if (p != null) { bool ja = false; foreach (object y in l) if (S(y, "id") == S(p, "id")) ja = true; if (!ja) l.Add(p); } }
                            }
                            catch { }
                        }
                        CasaServidor.Json(res, l);
                        return;
                    }
            }
            CasaServidor.Error(res, 404, "No trobat");
        }
        catch (Exception e) { CasaServidor.Error(res, 500, e.Message); }
    }
}
