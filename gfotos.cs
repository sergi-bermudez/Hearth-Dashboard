// Google Fotos: sincronitza les fotos recents i els albums triats cap a una carpeta
// local (dins de la carpeta de Fotos), fent servir la web de Google Fotos a l'Edge del servidor. C# 5.
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

public static class GFotos
{
    static readonly object lk = new object();
    static bool enMarxa;
    static string estat = "";
    static int fetes, total;
    static DateTime darrera = DateTime.MinValue, darreraAuto = DateTime.MinValue;
    static Timer rellotge;
    static string loginTarget;

    static Dictionary<string, object> Ajustos()
    {
        string f = Path.Combine(CasaServidor.DirDades, "gfotos.json");
        try { if (File.Exists(f)) { Dictionary<string, object> d = new JavaScriptSerializer().DeserializeObject(File.ReadAllText(f, Encoding.UTF8)) as Dictionary<string, object>; if (d != null) return d; } }
        catch { }
        return new Dictionary<string, object>();
    }
    static string A(Dictionary<string, object> d, string k, string def)
    {
        object v;
        return d.TryGetValue(k, out v) && v != null && v.ToString() != "" ? v.ToString() : def;
    }

    static string FitxerDarrera { get { return Path.Combine(CasaServidor.DirDades, "gfotos-darrera.txt"); } }
    static DateTime DarreraDesada()
    {
        try { if (File.Exists(FitxerDarrera)) return File.GetLastWriteTime(FitxerDarrera); } catch { }
        return DateTime.MinValue;
    }

    public static void AlArrencar()
    {
        DateTime arrencada = DateTime.Now;
        rellotge = new Timer(delegate
        {
            try
            {
                Dictionary<string, object> a = Ajustos();
                if (A(a, "actiu", "").ToLowerInvariant() != "true") return;
                string[] h = A(a, "hora", "03:30").Split(':');
                DateTime ara = DateTime.Now;
                // A l'hora triada; o, si el PC estava apagat a aquella hora i fa m\u00e9s d'un dia, ara (uns minuts despr\u00e9s d'arrencar)
                bool aHora = ara.Hour == int.Parse(h[0]) && ara.Minute == int.Parse(h[1]);
                bool endarrerit = (ara - DarreraDesada()).TotalHours > 26 && (ara - arrencada).TotalMinutes > 3;
                if ((aHora || endarrerit) && (ara - darreraAuto).TotalHours > 2)
                {
                    darreraAuto = ara;
                    Comenca();
                }
            }
            catch { }
        }, null, 30000, 30000);
    }

    static Dictionary<string, string> Index()
    {
        string f = Path.Combine(CasaServidor.DirDades, "gfotos-index.json");
        try { if (File.Exists(f)) return new JavaScriptSerializer().Deserialize<Dictionary<string, string>>(File.ReadAllText(f, Encoding.UTF8)); } catch { }
        return new Dictionary<string, string>();
    }
    static void DesaIndex(Dictionary<string, string> ix)
    {
        JavaScriptSerializer js = new JavaScriptSerializer();
        js.MaxJsonLength = int.MaxValue;
        File.WriteAllText(Path.Combine(CasaServidor.DirDades, "gfotos-index.json"), js.Serialize(ix), new UTF8Encoding(false));
    }

    static string Pont() { return File.ReadAllText(Path.Combine(CasaServidor.DirArrel, "gfotos-pont.js"), Encoding.UTF8); }

    static object Crida(Cdp c, string expr, int ms)
    {
        string js = "(async () => { if (!window.__gf) { " + Pont() + "\n } return JSON.stringify(await window.__gf." + expr + "); })()";
        object v = Edge.Avalua(c, js, ms);
        if (v == null) return null;
        JavaScriptSerializer s = new JavaScriptSerializer();
        s.MaxJsonLength = int.MaxValue;
        return s.DeserializeObject(v.ToString());
    }

    static void Navega(Cdp c, string url)
    {
        c.Envia("Page.navigate", CasaServidor.Dic("url", url), 15000);
        Thread.Sleep(5000);
    }

    static string NomNet(string n)
    {
        foreach (char ch in Path.GetInvalidFileNameChars()) n = n.Replace(ch.ToString(), "");
        n = n.Trim().TrimEnd('.');
        return n == "" ? "Album" : n;
    }

    public static void Comenca()
    {
        lock (lk) { if (enMarxa) return; enMarxa = true; }
        ThreadPool.QueueUserWorkItem(delegate
        {
            try { Sincronitza(); }
            catch (Exception e) { estat = "Error: " + e.Message; Console.WriteLine("  Google Fotos: " + e.Message); }
            finally { enMarxa = false; darrera = DateTime.Now; try { File.WriteAllText(FitxerDarrera, darrera.ToString("o")); } catch { } }
        });
    }

    static void Sincronitza()
    {
        string arrel;
        string fotos = CasaServidor.RutaArrel("fotos");
        if (fotos == null) throw new Exception("No hi ha carpeta de fotos configurada");
        arrel = Path.Combine(fotos, "Google Fotos");
        Dictionary<string, object> a = Ajustos();
        int recents = 300;
        int.TryParse(A(a, "recents", "300"), out recents);
        List<string[]> llocs = new List<string[]>();
        if (recents > 0) llocs.Add(new string[] { "https://photos.google.com/", "Recents", recents.ToString() });
        object[] albums = a.ContainsKey("albums") ? a["albums"] as object[] : null;
        if (albums != null)
            foreach (object o in albums)
            {
                Dictionary<string, object> al = o as Dictionary<string, object>;
                if (al != null && al.ContainsKey("url")) llocs.Add(new string[] { al["url"].ToString(), NomNet(A(al, "nom", "Album")), "2000" });
            }
        if (llocs.Count == 0) { estat = "No hi ha res triat per sincronitzar"; return; }

        Dictionary<string, string> ix = Index();
        fetes = 0; total = 0;
        estat = "Obrint Google Fotos\u2026";
        string target = Edge.NovaPagina("about:blank");
        Cdp c = new Cdp();
        try
        {
            c.Connecta(Edge.WsPagina(target));
            c.Envia("Page.enable", null, 5000);
            c.Envia("Emulation.setDeviceMetricsOverride", CasaServidor.Dic("width", 1400, "height", 1000, "deviceScaleFactor", 1, "mobile", false), 5000);
            // El pont s'injecta abans que la pagina carregui, per capturar les respostes internes amb les fotos
            try { c.Envia("Page.addScriptToEvaluateOnNewDocument", CasaServidor.Dic("source", Pont()), 5000); } catch { }
            int noves = 0, fallades = 0; string darrerError = "";
            foreach (string[] lloc in llocs)
            {
                estat = "Llegint \u00ab" + lloc[1] + "\u00bb\u2026";
                Navega(c, lloc[0]);
                if (!(bool)Crida(c, "sessio()", 10000)) throw new Exception("Cal iniciar sessi\u00f3 a Google Fotos (Configuraci\u00f3 \u25b8 Google Fotos)");
                List<string> coneguts = new List<string>(ix.Keys);
                Dictionary<string, object> rr = Crida(c, "recull(" + lloc[2] + "," + new JavaScriptSerializer().Serialize(coneguts) + ")", 600000) as Dictionary<string, object>;
                object[] items = rr != null && rr.ContainsKey("items") ? rr["items"] as object[] : null;
                try
                {
                    File.AppendAllText(Path.Combine(CasaServidor.DirDades, "gfotos-diagnostic.txt"),
                        "=== " + DateTime.Now + " " + lloc[1] + "\r\n" + new JavaScriptSerializer().Serialize(rr != null && rr.ContainsKey("diag") ? rr["diag"] : null) + "\r\n", new UTF8Encoding(false));
                }
                catch { }
                if (items == null || items.Length == 0) { estat = "No he trobat fotos a \u00ab" + lloc[1] + "\u00bb. Envia'm el fitxer dades\\gfotos-diagnostic.txt"; continue; }
                string dir = Path.Combine(arrel, lloc[1]);
                Directory.CreateDirectory(dir);
                total += items.Length;
                foreach (object o in items)
                {
                    Dictionary<string, object> it = o as Dictionary<string, object>;
                    if (it == null) continue;
                    string id = it["id"].ToString();
                    string clau = lloc[1] + "/" + id;
                    fetes++;
                    if (ix.ContainsKey(clau) && File.Exists(Path.Combine(fotos, ix[clau])))
                    {
                        // les d'abans que en realitat eren una pagina d'error es tornen a baixar
                        if (EsFitxerImatge(Path.Combine(fotos, ix[clau]))) continue;
                        try { File.Delete(Path.Combine(fotos, ix[clau])); } catch { }
                    }
                    long ms = 0;
                    try { ms = Convert.ToInt64(it["data"]); } catch { }
                    DateTime data = ms > 0 ? new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMilliseconds(ms).ToLocalTime() : DateTime.Now;
                    string nom = data.ToString("yyyy-MM-dd_HHmmss") + "_" + (id.Length > 8 ? id.Substring(id.Length - 8) : id) + ".jpg";
                    string desti = Path.Combine(dir, NomNet(nom));
                    estat = "Baixant \u00ab" + lloc[1] + "\u00bb (" + fetes + " de " + total + ")";
                    try
                    {
                        Baixa(c, it["url"].ToString() + "=w2560-h2560-rj", desti);
                        File.SetLastWriteTime(desti, data);
                        ix[clau] = "Google Fotos/" + lloc[1] + "/" + Path.GetFileName(desti);
                        noves++;
                        if (noves % 25 == 0) DesaIndex(ix);
                    }
                    catch (Exception e) { Console.WriteLine("  Google Fotos: " + e.Message); fallades++; darrerError = e.Message; }
                }
                DesaIndex(ix);
            }
            if (total == 0) return;
            estat = "Fet: " + noves + " fotos noves" + (fallades > 0 ? ", " + fallades + " no s'han pogut baixar (" + darrerError + ")" : "") + " (" + DateTime.Now.ToString("dd/MM HH:mm") + ")";
            CasaServidor.RefrescaFotos();
        }
        finally
        {
            c.Tanca();
            try { Edge.TancaPagina(target); } catch { }
        }
    }

    static void Baixa(Cdp c, string url, string desti)
    {
        try { Descarrega(url, desti, null); return; }
        catch (Exception) { }
        try { BaixaAmbGaletes(c, url, desti); return; }
        catch (Exception) { }
        // Darrer recurs: la baixa el mateix Edge, que ja te la sessio de Google oberta
        string b64 = Edge.Avalua(c, "(async()=>{const r=await fetch(" + new JavaScriptSerializer().Serialize(url) + ",{credentials:'include'});if(!r.ok)throw new Error('HTTP '+r.status);const a=new Uint8Array(await r.arrayBuffer());let s='';for(let i=0;i<a.length;i+=32768)s+=String.fromCharCode.apply(null,a.subarray(i,i+32768));return btoa(s);})()", 60000) as string;
        if (string.IsNullOrEmpty(b64)) throw new Exception("La pagina no ha pogut baixar la foto");
        byte[] bytes = Convert.FromBase64String(b64);
        if (!EsImatge(bytes)) throw new Exception("No es una imatge");
        File.WriteAllBytes(desti, bytes);
    }

    // JPEG, PNG, GIF o WebP (no una pagina d'error o d'inici de sessio)
    static bool EsImatge(byte[] b)
    {
        if (b == null || b.Length < 1000) return false;
        if (b[0] == 0xFF && b[1] == 0xD8) return true;
        if (b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47) return true;
        if (b[0] == 0x47 && b[1] == 0x49 && b[2] == 0x46) return true;
        if (b[0] == 0x52 && b[1] == 0x49 && b[2] == 0x46 && b[3] == 0x46) return true;
        return false;
    }

    static bool EsFitxerImatge(string f)
    {
        try
        {
            byte[] b = new byte[1000];
            using (FileStream fr = new FileStream(f, FileMode.Open, FileAccess.Read)) { if (fr.Length < 1000) return false; fr.Read(b, 0, 1000); }
            return EsImatge(b);
        }
        catch { return true; }
    }

    static void BaixaAmbGaletes(Cdp c, string url, string desti)
    {
        // Si cal, amb les galetes de la sessio
        object[] galetes = c.Envia("Network.getCookies", CasaServidor.Dic("urls", new string[] { url, "https://photos.google.com/" }), 5000)["cookies"] as object[];
        StringBuilder sb = new StringBuilder();
        if (galetes != null)
            foreach (object o in galetes)
            {
                Dictionary<string, object> g = o as Dictionary<string, object>;
                if (g != null) sb.Append(g["name"]).Append('=').Append(g["value"]).Append("; ");
            }
        Descarrega(url, desti, sb.ToString());
    }

    static void Descarrega(string url, string desti, string galetes)
    {
        HttpWebRequest rq = (HttpWebRequest)WebRequest.Create(url);
        rq.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0 Safari/537.36 Edg/120.0";
        rq.Timeout = 30000;
        rq.Accept = "image/jpeg,image/*;q=0.8";
        rq.Referer = "https://photos.google.com/";
        if (galetes != null) rq.Headers["Cookie"] = galetes;
        string tmp = desti + ".part";
        using (WebResponse rs = rq.GetResponse())
        using (Stream s = rs.GetResponseStream())
        using (FileStream fs = new FileStream(tmp, FileMode.Create)) s.CopyTo(fs);
        byte[] cap = new byte[8];
        using (FileStream fr = new FileStream(tmp, FileMode.Open, FileAccess.Read)) fr.Read(cap, 0, 8);
        byte[] prova = new byte[1000]; Array.Copy(cap, prova, 8);
        if (new FileInfo(tmp).Length < 1000 || !EsImatge(prova)) { File.Delete(tmp); throw new WebException("No ha tornat una imatge"); }
        if (File.Exists(desti)) File.Delete(desti);
        File.Move(tmp, desti);
    }

    // ---------- API d'administracio ----------
    public static void Admin(string nom, Dictionary<string, string> q, HttpListenerContext ctx)
    {
        HttpListenerResponse res = ctx.Response;
        try
        {
            switch (nom)
            {
                case "estat":
                    {
                        Dictionary<string, object> r = new Dictionary<string, object>();
                        r["enMarxa"] = enMarxa; r["estat"] = estat; r["fetes"] = fetes; r["total"] = total;
                        r["darrera"] = darrera == DateTime.MinValue ? "" : darrera.ToString("dd/MM/yyyy HH:mm");
                        r["ajustos"] = Ajustos(); r["edge"] = Edge.Ruta() != null;
                        try
                        {
                            // el darrer diagnostic, per poder-lo ensenyar a Configuracio
                            string fd = Path.Combine(CasaServidor.DirDades, "gfotos-diagnostic.txt");
                            if (File.Exists(fd)) { string t = File.ReadAllText(fd, Encoding.UTF8); int i = t.LastIndexOf("=== "); r["diag"] = (i >= 0 ? t.Substring(i) : t); if (r["diag"].ToString().Length > 3000) r["diag"] = r["diag"].ToString().Substring(0, 3000); }
                        }
                        catch { }
                        CasaServidor.Json(res, r);
                        return;
                    }
                case "sincronitza": Comenca(); CasaServidor.Text(res, "{\"ok\":true}", "application/json"); return;
                case "login":
                    loginTarget = Edge.NovaPagina("https://photos.google.com/login");
                    Edge.Finestra(loginTarget, true);
                    CasaServidor.Text(res, "{\"ok\":true}", "application/json");
                    return;
                case "amaga":
                    if (loginTarget != null) { try { Edge.TancaPagina(loginTarget); } catch { } loginTarget = null; }
                    CasaServidor.Text(res, "{\"ok\":true}", "application/json");
                    return;
                case "albums":
                    {
                        string target = Edge.NovaPagina("about:blank");
                        Cdp c = new Cdp();
                        try
                        {
                            c.Connecta(Edge.WsPagina(target));
                            c.Envia("Page.enable", null, 5000);
                            c.Envia("Emulation.setDeviceMetricsOverride", CasaServidor.Dic("width", 1400, "height", 1000, "deviceScaleFactor", 1, "mobile", false), 5000);
                            Navega(c, "https://photos.google.com/albums");
                            if (!(bool)Crida(c, "sessio()", 10000)) throw new Exception("Cal iniciar sessi\u00f3 a Google Fotos");
                            object l = Crida(c, "albums()", 180000);
                            CasaServidor.Json(res, l ?? new object[0]);
                        }
                        finally { c.Tanca(); try { Edge.TancaPagina(target); } catch { } }
                        return;
                    }
            }
            CasaServidor.Error(res, 404, "API desconeguda");
        }
        catch (Exception e)
        {
            try { CasaServidor.Error(res, 503, e.Message); } catch { }
        }
    }
}
