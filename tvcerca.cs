// Cerca de canals a tota la llista publica d'iptv-org (index.country.m3u: cada canal amb el seu pais).
// El servidor la baixa un cop al dia i la guarda, perque les cerques siguin rapides tambe a les tauletes velles. C# 5.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

public static class TvCerca
{
    const string URL = "https://iptv-org.github.io/iptv/index.country.m3u";
    class Canal { public string Nom, Pla, Logo, Id, Pais; public List<string> Urls = new List<string>(); public bool Geo, No247; }
    static List<Canal> canals;
    static DateTime quan = DateTime.MinValue;
    static readonly object lk = new object();

    static string Fitxer { get { return Path.Combine(CasaServidor.DirDades, "iptv-index.m3u"); } }

    // Sense accents, en minuscules (com la funcio pla() de les pagines)
    static string Pla(string s)
    {
        string d = (s ?? "").ToLowerInvariant().Normalize(NormalizationForm.FormD);
        StringBuilder b = new StringBuilder();
        foreach (char ch in d) if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark) b.Append(ch);
        return b.ToString();
    }

    static List<Canal> Llegeix(string txt)
    {
        List<Canal> l = new List<Canal>();
        Dictionary<string, Canal> vistos = new Dictionary<string, Canal>();
        Canal info = null;
        foreach (string l0 in txt.Split('\n'))
        {
            string s = l0.Trim();
            if (s.StartsWith("#EXTINF"))
            {
                int coma = s.LastIndexOf("\",");
                string nom = coma >= 0 ? s.Substring(coma + 2) : s.Substring(s.IndexOf(',') + 1);
                info = new Canal();
                info.Geo = nom.IndexOf("[Geo-blocked]", StringComparison.OrdinalIgnoreCase) >= 0;
                info.No247 = nom.IndexOf("[Not 24/7]", StringComparison.OrdinalIgnoreCase) >= 0;
                info.Nom = Regex.Replace(Regex.Replace(nom, @"\[[^\]]*\]", ""), @"\(\d{3,4}[pi]\)", "").Trim();
                Match m = Regex.Match(s, "tvg-logo=\"([^\"]*)\""); info.Logo = m.Success ? m.Groups[1].Value : "";
                m = Regex.Match(s, "tvg-id=\"([^\"]*)\""); info.Id = m.Success ? m.Groups[1].Value : "";
                m = Regex.Match(s, "group-title=\"([^\"]*)\""); info.Pais = m.Success ? m.Groups[1].Value : "";
            }
            else if (s != "" && s[0] != '#' && info != null)
            {
                info.Pla = Pla(info.Nom);
                string clau = info.Pla + "|" + info.Pais;
                Canal c;
                if (vistos.TryGetValue(clau, out c)) { if (!c.Urls.Contains(s)) c.Urls.Add(s); }
                else { info.Urls.Add(s); vistos[clau] = info; l.Add(info); }
                info = null;
            }
        }
        return l;
    }

    // La llista: de memoria, del fitxer desat (si es d'avui) o d'internet; si internet falla, la desada encara que sigui vella
    static List<Canal> Llista()
    {
        lock (lk)
        {
            if (canals != null && (DateTime.Now - quan).TotalHours < 24) return canals;
            string txt = null;
            bool fresc = File.Exists(Fitxer) && (DateTime.Now - File.GetLastWriteTime(Fitxer)).TotalHours < 24;
            if (!fresc)
            {
                try
                {
                    ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                    HttpWebRequest rq = (HttpWebRequest)WebRequest.Create(URL);
                    rq.UserAgent = "Hearth";
                    rq.Timeout = 30000; rq.ReadWriteTimeout = 60000;
                    rq.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
                    using (WebResponse rs = rq.GetResponse())
                    using (StreamReader sr = new StreamReader(rs.GetResponseStream(), Encoding.UTF8)) txt = sr.ReadToEnd();
                    if (txt.IndexOf("#EXTM3U", StringComparison.Ordinal) < 0) txt = null;
                    else File.WriteAllText(Fitxer, txt, new UTF8Encoding(false));
                }
                catch (Exception e) { Console.WriteLine("  TV: no s'ha pogut baixar la llista d'iptv-org: " + e.Message); }
            }
            if (txt == null && File.Exists(Fitxer)) txt = File.ReadAllText(Fitxer, Encoding.UTF8);
            if (txt == null) throw new Exception("No s'ha pogut baixar la llista de canals");
            canals = Llegeix(txt);
            quan = DateTime.Now;
            return canals;
        }
    }

    // api/tv/cerca?q=...: fins a 200 canals que tinguin totes les paraules al nom o al pais
    public static void Api(Dictionary<string, string> q, HttpListenerContext ctx)
    {
        string cerca = Pla(CasaServidor.Q(q, "q")).Trim();
        List<object> r = new List<object>();
        if (cerca.Length >= 2)
        {
            string[] paraules = cerca.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            List<Canal> l;
            try { l = Llista(); }
            catch (Exception e) { CasaServidor.Error(ctx.Response, 502, e.Message); return; }
            // primer els que comencen amb la cerca, despres la resta
            List<Canal> prim = new List<Canal>(), resta = new List<Canal>();
            foreach (Canal c in l)
            {
                string tot = c.Pla + " " + Pla(c.Pais);
                bool ok = true;
                foreach (string p in paraules) if (tot.IndexOf(p, StringComparison.Ordinal) < 0) { ok = false; break; }
                if (!ok) continue;
                if (c.Pla.StartsWith(paraules[0])) prim.Add(c); else resta.Add(c);
                if (prim.Count >= 200) break;
            }
            prim.AddRange(resta);
            foreach (Canal c in prim)
            {
                if (r.Count >= 200) break;
                Dictionary<string, object> o = new Dictionary<string, object>();
                o["id"] = "iptv-" + (c.Id != "" ? c.Id : c.Pla);
                o["nom"] = c.Nom; o["logo"] = c.Logo; o["pais"] = c.Pais; o["urls"] = c.Urls; o["geo"] = c.Geo; o["no247"] = c.No247; o["propi"] = true;
                r.Add(o);
            }
        }
        CasaServidor.Json(ctx.Response, r);
    }
}
