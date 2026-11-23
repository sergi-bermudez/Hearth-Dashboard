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
using System.Diagnostics;
using System.Text.RegularExpressions;

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

// R\u00e0dio en HLS per als iPad antics: ffmpeg llegeix l'emissora i en fa trossos AAC de 4 s,
// el mateix format que fa servir la TV (que a iOS 9 funciona be). Una sessio per emissora;
// s'atura sola quan ningu l'escolta.
public static class RadioHls
{
    class Sessio { public Process P; public string Dir; public DateTime Darrer; public string Url; }
    static readonly Dictionary<string, Sessio> sessions = new Dictionary<string, Sessio>();
    static readonly object lk = new object();
    static Timer neteja;

    static string Id(string u)
    {
        using (MD5 m = MD5.Create())
        {
            byte[] h = m.ComputeHash(Encoding.UTF8.GetBytes(u));
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < 8; i++) sb.Append(h[i].ToString("x2"));
            return sb.ToString();
        }
    }

    static string Cometes(string s) { return "\"" + s.Replace("\"", "") + "\""; }

    static Sessio Assegura(string url)
    {
        string id = Id(url);
        lock (lk)
        {
            if (neteja == null) neteja = new Timer(delegate { Neteja(); }, null, 15000, 15000);
            Sessio s;
            if (sessions.TryGetValue(id, out s) && s.P != null && !s.P.HasExited) { s.Darrer = DateTime.Now; return s; }
            string ff = SoPC.RutaFfmpeg();
            if (ff == null) throw new Exception("Falta ffmpeg.exe");
            string dir = Path.Combine(Path.Combine(CasaServidor.DirDades, "radio-hls"), id);
            try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch { }
            Directory.CreateDirectory(dir);
            string args = "-nostdin -loglevel error -user_agent \"Mozilla/5.0 Hearth\" " +
                (url.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? "-reconnect 1 -reconnect_streamed 1 -reconnect_delay_max 5 " : "") +
                "-i " + Cometes(url) + " -vn -c:a aac -b:a 96k -ac 2 -ar 44100 -f hls -hls_time 4 -hls_list_size 6 " +
                "-hls_flags delete_segments+omit_endlist -hls_segment_filename " + Cometes(Path.Combine(dir, "s%05d.ts")) + " " + Cometes(Path.Combine(dir, "index.m3u8"));
            ProcessStartInfo si = new ProcessStartInfo(ff, args);
            si.UseShellExecute = false; si.CreateNoWindow = true; si.RedirectStandardError = true;
            Process p = Process.Start(si);
            p.ErrorDataReceived += delegate { };
            p.BeginErrorReadLine();
            s = new Sessio { P = p, Dir = dir, Darrer = DateTime.Now, Url = url };
            sessions[id] = s;
            return s;
        }
    }

    static void Neteja()
    {
        List<Sessio> mortes = new List<Sessio>();
        lock (lk)
        {
            List<string> treure = new List<string>();
            foreach (KeyValuePair<string, Sessio> kv in sessions)
                if ((DateTime.Now - kv.Value.Darrer).TotalSeconds > 45 || kv.Value.P.HasExited) { treure.Add(kv.Key); mortes.Add(kv.Value); }
            foreach (string k in treure) sessions.Remove(k);
        }
        foreach (Sessio s in mortes)
        {
            try { if (!s.P.HasExited) s.P.Kill(); } catch { }
            try { s.P.WaitForExit(3000); Directory.Delete(s.Dir, true); } catch { }
        }
    }

    class Conversio { public Process P; public string Dir, Error; public DateTime Vist = DateTime.Now; }
    static readonly Dictionary<string, Conversio> conversions = new Dictionary<string, Conversio>();

    static Conversio Converteix(string full)
    {
        string id = CasaServidor.Hash("v2|" + full + "|" + File.GetLastWriteTimeUtc(full).Ticks).Substring(0, 16);
        lock (conversions)
        {
            Conversio c;
            if (conversions.TryGetValue(id, out c)) { c.Vist = DateTime.Now; return c; }
            string arrel = Path.Combine(CasaServidor.DirDades, "conv-vells");
            Directory.CreateDirectory(arrel);
            // nomes es guarden les ultimes conversions (ocupen molt)
            try
            {
                DirectoryInfo[] ds = new DirectoryInfo(arrel).GetDirectories();
                Array.Sort(ds, delegate (DirectoryInfo a, DirectoryInfo b) { return b.LastWriteTimeUtc.CompareTo(a.LastWriteTimeUtc); });
                for (int i = 2; i < ds.Length; i++) if (ds[i].Name != id) try { ds[i].Delete(true); } catch { }
            }
            catch { }
            c = new Conversio { Dir = Path.Combine(arrel, id) };
            bool fet = File.Exists(Path.Combine(c.Dir, "fet.txt"));
            Directory.CreateDirectory(c.Dir);
            if (!fet)
            {
                foreach (string f in Directory.GetFiles(c.Dir)) try { File.Delete(f); } catch { }
                string ff = SoPC.RutaFfmpeg();
                if (ff == null) throw new Exception("Falta ffmpeg.exe");
                // el primer tros, de 2 minuts (per comen\u00e7ar aviat); la resta, de 20 (menys talls)
                List<string> talls = new List<string>();
                for (int k = 0; k < 30; k++) talls.Add((120 + k * 1200).ToString());
                string llista = string.Join(",", talls.ToArray());
                // aresample async: el so no es va endarrerint respecte la imatge a cada tros
                string args = "-nostdin -loglevel error -y -i " + Cometes(full) +
                    " -map 0:v:0 -map 0:a:0? -c:v libx264 -preset veryfast -profile:v main -level 3.1 -pix_fmt yuv420p -vf \"scale=-2:'min(720,ih)'\" -b:v 2500k -maxrate 3000k -bufsize 6000k -force_key_frames " + llista +
                    " -c:a aac -b:a 160k -ac 2 -ar 44100 -af aresample=async=1:first_pts=0 -avoid_negative_ts make_zero" +
                    " -f segment -segment_times " + llista + " -reset_timestamps 1 -segment_format mp4 -segment_format_options movflags=+faststart " + Cometes(Path.Combine(c.Dir, "t%03d.mp4"));
                ProcessStartInfo si = new ProcessStartInfo(ff, args);
                si.UseShellExecute = false; si.CreateNoWindow = true; si.RedirectStandardError = true;
                Process p = Process.Start(si);
                Conversio cc = c;
                p.ErrorDataReceived += delegate (object o, DataReceivedEventArgs e) { if (!string.IsNullOrEmpty(e.Data)) cc.Error = e.Data; };
                p.BeginErrorReadLine();
                p.EnableRaisingEvents = true;
                p.Exited += delegate { try { if (p.ExitCode == 0) File.WriteAllText(Path.Combine(cc.Dir, "fet.txt"), "1"); } catch { } };
                c.P = p;
                // si ningu la mira, s'atura
                Timer t = null;
                t = new Timer(delegate { try { if ((DateTime.Now - cc.Vist).TotalMinutes > 10 && cc.P != null && !cc.P.HasExited) { cc.P.Kill(); lock (conversions) conversions.Remove(id); t.Dispose(); } else if (cc.P == null || cc.P.HasExited) t.Dispose(); } catch { } }, null, 60000, 60000);
            }
            conversions[id] = c;
            return c;
        }
    }

    // un tros es pot enviar quan ja n'hi ha un altre de despres, o quan la conversio ha acabat
    static bool TrosAcabat(Conversio c, int n)
    {
        if (!File.Exists(Path.Combine(c.Dir, "t" + n.ToString("000") + ".mp4"))) return false;
        if (File.Exists(Path.Combine(c.Dir, "t" + (n + 1).ToString("000") + ".mp4"))) return true;
        return c.P == null || c.P.HasExited;
    }

    public static void Api(string nom, Dictionary<string, string> q, HttpListenerContext ctx)
    {
        HttpListenerResponse res = ctx.Response;
        try
        {
            if (nom == "video")
            {
                // Una pel.licula per a navegadors vells (Xbox 360): no entenen el MP4 fet al vol (error 3), nomes fitxers
                // MP4 acabats. ffmpeg la converteix a trossos de 5 minuts (H.264 720p, AAC estereo, cada tros un MP4
                // complet) i el navegador els va reproduint un darrere l'altre mentre es fan els seguents.
                string rel = CasaServidor.Q(q, "p"), full = CasaServidor.Ruta("pelis", rel);
                if (full == null || !File.Exists(full)) { CasaServidor.Error(res, 404, "No"); return; }
                Conversio c = Converteix(full);
                string tros = CasaServidor.Q(q, "i");
                if (tros != "")
                {
                    int n; int.TryParse(tros, out n);
                    string f = Path.Combine(c.Dir, "t" + n.ToString("000") + ".mp4");
                    if (!File.Exists(f) || !TrosAcabat(c, n)) { CasaServidor.Error(res, 404, "Encara no"); return; }
                    c.Vist = DateTime.Now;
                    CasaServidor.EnviarFitxer(ctx, f);
                    return;
                }
                int fets = 0;
                while (TrosAcabat(c, fets)) fets++;
                CasaServidor.Json(res, CasaServidor.Dic("trossos", fets, "acabat", c.P == null || c.P.HasExited, "error", c.Error ?? ""));
                return;
            }
            if (nom == "mp3")
            {
                // La radio com un MP3 que no s'acaba mai: ffmpeg llegeix l'emissora (i s'hi torna a connectar si talla
                // o si diu que "s'ha acabat") i en fa un MP3 continu. Una connexio per reproductor.
                string u = CasaServidor.Q(q, "u");
                if (!Regex.IsMatch(u, "^https?://", RegexOptions.IgnoreCase)) { CasaServidor.Error(res, 400, "URL"); return; }
                string ff = SoPC.RutaFfmpeg();
                if (ff == null) { CasaServidor.Error(res, 503, "Falta ffmpeg.exe"); return; }
                string args = "-nostdin -loglevel error -user_agent \"Mozilla/5.0 (Windows NT 10.0; Win64; x64) Hearth\" " +
                    "-reconnect 1 -reconnect_streamed 1 -reconnect_at_eof 1 -reconnect_delay_max 5 -rw_timeout 15000000 " +
                    "-i " + Cometes(u) + " -vn -c:a libmp3lame -b:a 128k -ac 2 -ar 44100 -f mp3 pipe:1";
                ProcessStartInfo si = new ProcessStartInfo(ff, args);
                si.UseShellExecute = false; si.CreateNoWindow = true; si.RedirectStandardOutput = true; si.RedirectStandardError = true;
                Process p = Process.Start(si);
                p.ErrorDataReceived += delegate { };
                p.BeginErrorReadLine();
                try
                {
                    res.ContentType = "audio/mpeg";
                    res.AddHeader("Cache-Control", "no-cache, no-store");
                    // Els navegadors vells (Internet Explorer 9 de l'Xbox 360) no fan sonar un so sense mida: se'ls en diu una de
                    // molt gran (unes 70 hores a 128 kbps) i se'ls envia fins que tanquen
                    if (CasaServidor.Q(q, "mida") == "1") { res.SendChunked = false; res.ContentLength64 = 4000000000L; res.KeepAlive = false; }
                    else res.SendChunked = true;
                    if (ctx.Request.HttpMethod == "HEAD") return;
                    Stream o = p.StandardOutput.BaseStream;
                    byte[] buf = new byte[8192];
                    int n;
                    while ((n = o.Read(buf, 0, buf.Length)) > 0) res.OutputStream.Write(buf, 0, n);   // si el reproductor marxa, Write falla
                }
                finally { try { if (!p.HasExited) p.Kill(); } catch { } try { p.Dispose(); } catch { } }
                return;
            }
            if (nom == "hls")
            {
                string u = CasaServidor.Q(q, "u");
                if (!Regex.IsMatch(u, "^https?://", RegexOptions.IgnoreCase)) { CasaServidor.Error(res, 400, "URL"); return; }
                Sessio s = Assegura(u);
                string llista = Path.Combine(s.Dir, "index.m3u8");
                // La primera vegada cal esperar que hi hagi un parell de trossos
                for (int i = 0; i < 80; i++)
                {
                    if (File.Exists(llista))
                    {
                        string t0 = "";
                        try { t0 = File.ReadAllText(llista); } catch { }
                        if (Regex.Matches(t0, @"\.ts").Count >= 2) break;
                    }
                    if (s.P.HasExited) { CasaServidor.Error(res, 502, "L'emissora no respon"); return; }
                    Thread.Sleep(250);
                }
                string t;
                try { t = File.ReadAllText(llista); } catch { CasaServidor.Error(res, 504, "L'emissora triga massa"); return; }
                string id = Path.GetFileName(s.Dir);
                t = Regex.Replace(t, @"^(s\d+\.ts)\s*$", delegate (Match m) { return "/api/radio/seg?id=" + id + "&f=" + m.Groups[1].Value; }, RegexOptions.Multiline);
                res.AddHeader("Cache-Control", "no-cache");
                CasaServidor.Text(res, t, "application/vnd.apple.mpegurl");
                return;
            }
            if (nom == "seg")
            {
                string id = CasaServidor.Q(q, "id"), f = CasaServidor.Q(q, "f");
                if (!Regex.IsMatch(id, "^[0-9a-f]{16}$") || !Regex.IsMatch(f, @"^s\d+\.ts$")) { CasaServidor.Error(res, 400, "Nom"); return; }
                lock (lk) { Sessio s; if (sessions.TryGetValue(id, out s)) s.Darrer = DateTime.Now; }
                string ruta = Path.Combine(Path.Combine(Path.Combine(CasaServidor.DirDades, "radio-hls"), id), f);
                if (!File.Exists(ruta)) { CasaServidor.Error(res, 404, "Tros"); return; }
                byte[] b;
                try { b = File.ReadAllBytes(ruta); } catch { CasaServidor.Error(res, 404, "Tros"); return; }
                res.ContentType = "video/mp2t";
                res.ContentLength64 = b.Length;
                res.OutputStream.Write(b, 0, b.Length);
                res.Close();
                return;
            }
            CasaServidor.Error(res, 404, "API");
        }
        catch (Exception e) { try { CasaServidor.Error(res, 500, e.Message); } catch { } }
    }
}
