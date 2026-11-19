// Cerca d'emissores (radio-browser.info) feta pel servidor: prova diversos servidors
// alhora, es queda el primer que respon i desa el resultat 24 h al disc. C# 5.
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

public static class Radios
{
    static readonly string[] Miralls = { "https://de1.api.radio-browser.info", "https://nl1.api.radio-browser.info", "https://at1.api.radio-browser.info", "https://de2.api.radio-browser.info", "https://fi1.api.radio-browser.info" };
    static readonly Dictionary<string, string> memoria = new Dictionary<string, string>();
    static readonly Dictionary<string, bool> refrescant = new Dictionary<string, bool>();
    static readonly object lk = new object();
    static readonly string[] Permesos = { "countrycode", "language", "name", "tag", "limit", "order", "state" };

    static string Dir() { string d = Path.Combine(CasaServidor.DirDades, "radios-cache"); Directory.CreateDirectory(d); return d; }

    static string Md5(string s)
    {
        using (MD5 m = MD5.Create())
        {
            byte[] h = m.ComputeHash(Encoding.UTF8.GetBytes(s));
            StringBuilder sb = new StringBuilder();
            foreach (byte b in h) sb.Append(b.ToString("x2"));
            return sb.ToString();
        }
    }

    static string Baixa(string url, int ms, CancellationToken ct)
    {
        HttpWebRequest rq = (HttpWebRequest)WebRequest.Create(url);
        rq.UserAgent = "Hearth/3.0 (home dashboard)";
        rq.Timeout = ms; rq.ReadWriteTimeout = ms;
        rq.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
        using (ct.Register(delegate { try { rq.Abort(); } catch { } }))
        using (WebResponse rs = rq.GetResponse())
        using (StreamReader sr = new StreamReader(rs.GetResponseStream(), Encoding.UTF8)) return sr.ReadToEnd();
    }

    // Llan\u00e7a la consulta a tots els servidors i torna la primera resposta bona
    static string Consulta(string cami)
    {
        CancellationTokenSource cts = new CancellationTokenSource();
        List<Task<string>> tasques = new List<Task<string>>();
        foreach (string m in Miralls)
        {
            string url = m + cami;
            tasques.Add(Task.Run(delegate { return Baixa(url, 9000, cts.Token); }));
        }
        DateTime limit = DateTime.Now.AddSeconds(10);
        while (tasques.Count > 0 && DateTime.Now < limit)
        {
            int i = Task.WaitAny(tasques.ToArray(), 10500);
            if (i < 0) break;
            Task<string> t = tasques[i];
            tasques.RemoveAt(i);
            if (t.Status == TaskStatus.RanToCompletion && t.Result != null && t.Result.TrimStart().StartsWith("["))
            {
                cts.Cancel();
                return t.Result;
            }
        }
        cts.Cancel();
        return null;
    }

    static string S(Dictionary<string, object> d, string k) { object v; return d.TryGetValue(k, out v) && v != null ? v.ToString().Trim() : ""; }

    // Redueix la resposta a l'essencial: menys bytes cap a l'iPad
    static string Compacta(string json)
    {
        JavaScriptSerializer js = new JavaScriptSerializer();
        js.MaxJsonLength = int.MaxValue;
        object[] l = js.DeserializeObject(json) as object[];
        List<object> r = new List<object>();
        HashSet<string> vistes = new HashSet<string>();
        if (l != null)
            foreach (object o in l)
            {
                Dictionary<string, object> e = o as Dictionary<string, object>;
                if (e == null) continue;
                string u = S(e, "url_resolved");
                if (u == "") u = S(e, "url");
                string nom = S(e, "name");
                if (u == "" || nom == "") continue;
                string clau = nom.ToLowerInvariant();
                if (vistes.Contains(clau)) continue;
                vistes.Add(clau);
                string tags = S(e, "tags");
                string[] tp = tags.Split(',');
                string etiq = string.Join(", ", tp, 0, Math.Min(3, tp.Length)).Trim(' ', ',');
                string lloc = S(e, "state");
                if (lloc == "") lloc = S(e, "country");
                Dictionary<string, object> x = new Dictionary<string, object>();
                x["t"] = nom;
                x["s"] = (lloc + (lloc != "" && etiq != "" ? " \u00b7 " : "") + etiq);
                x["u"] = u;
                x["img"] = S(e, "favicon");
                x["codec"] = S(e, "codec");
                x["br"] = S(e, "bitrate");
                x["cc"] = S(e, "countrycode");
                r.Add(x);
            }
        return js.Serialize(r);
    }

    static string Cami(Dictionary<string, string> q)
    {
        StringBuilder sb = new StringBuilder("/json/stations/search?hidebroken=true");
        string order = CasaServidor.Q(q, "order");
        sb.Append("&order=").Append(order == "votes" || order == "name" ? order : "clickcount");
        if (order != "name") sb.Append("&reverse=true");
        int lim;
        if (!int.TryParse(CasaServidor.Q(q, "limit"), out lim) || lim <= 0 || lim > 300) lim = 150;
        sb.Append("&limit=").Append(lim);
        foreach (string k in Permesos)
        {
            if (k == "limit" || k == "order") continue;
            string v = CasaServidor.Q(q, k);
            if (!string.IsNullOrEmpty(v)) sb.Append("&").Append(k).Append("=").Append(Uri.EscapeDataString(v));
        }
        if (!string.IsNullOrEmpty(CasaServidor.Q(q, "tag"))) sb.Append("&tagExact=false");
        return sb.ToString();
    }

    static string Obte(string cami, bool forca)
    {
        string clau = Md5(cami), f = Path.Combine(Dir(), clau + ".json");
        string r;
        lock (lk) { if (!forca && memoria.TryGetValue(clau, out r)) { if (File.Exists(f) && (DateTime.Now - File.GetLastWriteTime(f)).TotalHours < 24) return r; } }
        if (!forca && File.Exists(f))
        {
            r = File.ReadAllText(f, Encoding.UTF8);
            lock (lk) memoria[clau] = r;
            if ((DateTime.Now - File.GetLastWriteTime(f)).TotalHours >= 24) Refresca(cami, clau);
            return r;
        }
        string cru = Consulta(cami);
        if (cru == null) return null;
        r = Compacta(cru);
        try { File.WriteAllText(f, r, new UTF8Encoding(false)); } catch { }
        lock (lk) memoria[clau] = r;
        return r;
    }

    // Respon de seguida amb la llista vella i l'actualitza en segon pla
    static void Refresca(string cami, string clau)
    {
        lock (lk) { if (refrescant.ContainsKey(clau)) return; refrescant[clau] = true; }
        ThreadPool.QueueUserWorkItem(delegate
        {
            try { Obte(cami, true); } catch { }
            lock (lk) refrescant.Remove(clau);
        });
    }

    public static void Api(Dictionary<string, string> q, HttpListenerContext ctx)
    {
        try
        {
            string r = Obte(Cami(q), false);
            if (r == null) { CasaServidor.Error(ctx.Response, 502, "No s'ha pogut connectar amb radio-browser.info"); return; }
            CasaServidor.Text(ctx.Response, r, "application/json; charset=utf-8");
        }
        catch (Exception e) { CasaServidor.Error(ctx.Response, 500, e.Message); }
    }
}
