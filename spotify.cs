// Spotify via Spotify web a un Edge propi controlat pel servidor (protocol DevTools),
// i so del PC enviat a l'iPad amb ffmpeg (cable d'audio virtual). C# 5.
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;

public static class Spotify
{
    const string URL = "https://open.spotify.com/";
    static readonly object lk = new object();
    static Cdp sessio;
    static string token;
    static DateTime tokenData = DateTime.MinValue;
    static string darrerError = "";

    // ---------- Ajustos (dades/spotify.json) ----------
    static Dictionary<string, object> Ajustos()
    {
        string f = Path.Combine(CasaServidor.DirDades, "spotify.json");
        try
        {
            if (File.Exists(f))
            {
                Dictionary<string, object> d = new JavaScriptSerializer().DeserializeObject(File.ReadAllText(f, Encoding.UTF8)) as Dictionary<string, object>;
                if (d != null) return d;
            }
        }
        catch { }
        return new Dictionary<string, object>();
    }
    static string Ajust(string k, string def)
    {
        object v;
        Dictionary<string, object> a = Ajustos();
        return a.TryGetValue(k, out v) && v != null && v.ToString() != "" ? v.ToString() : def;
    }

    public static void AlArrencar()
    {
        if (Ajust("actiu", "") == "True" || Ajust("actiu", "") == "true")
            ThreadPool.QueueUserWorkItem(delegate { try { Assegura(); } catch (Exception e) { Console.WriteLine("  Spotify: " + e.Message); } });
    }

    static string targetId;
    static string TargetSpotify()
    {
        foreach (string[] p in Edge.Pagines()) if (p[1].Contains("open.spotify.com")) return p[0];
        return Edge.NovaPagina(URL);
    }

    static string Pont()
    {
        return File.ReadAllText(Path.Combine(CasaServidor.DirArrel, "spotify-pont.js"), Encoding.UTF8);
    }

    // Garanteix Edge en marxa + sessio DevTools oberta amb la pagina de Spotify
    static Cdp Assegura()
    {
        lock (lk)
        {
            if (sessio != null && sessio.Viu) return sessio;
            Edge.Assegura();
            targetId = TargetSpotify();
            Cdp c = new Cdp();
            c.Esdeveniment = EsdevenimentEdge;
            c.Connecta(Edge.WsPagina(targetId));
            c.Envia("Network.enable", null, 5000);
            c.Envia("Page.enable", null, 5000);
            try { c.Envia("Page.addScriptToEvaluateOnNewDocument", CasaServidor.Dic("source", Pont()), 5000); } catch { }
            try
            {
                Cdp nav = Edge.Navegador();
                try
                {
                    nav.Envia("Browser.grantPermissions", CasaServidor.Dic("origin", "https://open.spotify.com", "permissions", new string[] { "audioCapture" }), 5000);
                    try { nav.Envia("Browser.grantPermissions", CasaServidor.Dic("origin", "https://open.spotify.com", "permissions", new string[] { "audioCapture", "speakerSelection" }), 5000); } catch { }
                }
                finally { nav.Tanca(); }
            }
            catch { }
            string href = Edge.Avalua(c, "location.href", 5000) as string ?? "";
            if (!href.Contains("open.spotify.com")) c.Envia("Page.navigate", CasaServidor.Dic("url", URL), 10000);
            else c.Envia("Page.reload", null, 10000);
            sessio = c;
            Thread.Sleep(2500);
            ThreadPool.QueueUserWorkItem(delegate { Sortida(); });
            return c;
        }
    }

    static void EsdevenimentEdge(string metode, Dictionary<string, object> p)
    {
        if (p == null || !metode.StartsWith("Network.requestWillBeSent")) return;
        Dictionary<string, object> h = null;
        if (p.ContainsKey("request")) { Dictionary<string, object> rq = p["request"] as Dictionary<string, object>; if (rq != null && rq.ContainsKey("headers")) h = rq["headers"] as Dictionary<string, object>; }
        else if (p.ContainsKey("headers")) h = p["headers"] as Dictionary<string, object>;
        if (h == null) return;
        foreach (KeyValuePair<string, object> kv in h)
        {
            if (!kv.Key.Equals("authorization", StringComparison.OrdinalIgnoreCase)) continue;
            string v = kv.Value as string;
            if (v != null && v.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) && v.Length > 40)
            {
                token = v.Substring(7);
                tokenData = DateTime.Now;
            }
        }
    }

    // Crida una funcio del pont (window.__casa) i torna el JSON que retorna
    static string Crida(string expr, int ms)
    {
        Cdp c = Assegura();
        string pont = Pont();
        string js = "(async () => { if (!window.__casa) { " + pont + "\n } return JSON.stringify(await window.__casa." + expr + "); })()";
        Dictionary<string, object> r;
        try { r = c.Envia("Runtime.evaluate", CasaServidor.Dic("expression", js, "awaitPromise", true, "returnByValue", true), ms); }
        catch (Exception)
        {
            lock (lk) { if (sessio != null && !sessio.Viu) sessio = null; }
            throw;
        }
        object v = Edge.Valor(r);
        return v == null ? "null" : v.ToString();
    }

    static string Js(string s) { return new JavaScriptSerializer().Serialize(s ?? ""); }

    static string Token()
    {
        if (token != null && (DateTime.Now - tokenData).TotalMinutes < 45) return token;
        Cdp c = Assegura();
        if (token == null || (DateTime.Now - tokenData).TotalMinutes >= 45)
        {
            DateTime abans = tokenData;
            try { c.Envia("Page.reload", null, 10000); } catch { }
            for (int i = 0; i < 40 && tokenData == abans; i++) Thread.Sleep(250);
            Thread.Sleep(1500);
        }
        if (token == null) throw new Exception("Encara no tinc perm\u00eds de Spotify. Inicia sessi\u00f3 a Configuraci\u00f3 \u25b8 Spotify.");
        return token;
    }

    // Token sense recarregar la pagina (la cerca ja no el necessita; nomes es el pla C)
    static string TokenSiHiHa()
    {
        if (token != null && (DateTime.Now - tokenData).TotalMinutes < 50) return token;
        return "";
    }

    // Perque l'Edge deixi triar la sortida de so (CABLE Input) cal el permis del micro
    static void PermisSo()
    {
        Cdp b = null;
        try
        {
            b = Edge.Navegador();
            Dictionary<string, object> p = new Dictionary<string, object>();
            p["origin"] = "https://open.spotify.com";
            p["permissions"] = new object[] { "audioCapture", "speakerSelection" };
            try { b.Envia("Browser.grantPermissions", p, 4000); }
            catch { p["permissions"] = new object[] { "audioCapture" }; b.Envia("Browser.grantPermissions", p, 4000); }
        }
        catch (Exception e) { Console.WriteLine("  Spotify: permis de so: " + e.Message); }
        finally { try { if (b != null) b.Tanca(); } catch { } }
    }

    public static void Sortida() { try { AplicaSortida(); } catch (Exception e) { UltimaSortida = "{\"ok\":false,\"error\":" + Js(e.Message) + "}"; Console.WriteLine("  Spotify: sortida de so: " + e.Message); } }

    static void AplicaSortida()
    {
        PermisSo();
        string s = Ajust("sortida", "CABLE Input");
        if (Ajust("sonaAlPC", "") == "True" || Ajust("sonaAlPC", "") == "true") s = "";
        string r = Crida("sortida(" + Js(s) + ")", 8000);
        UltimaSortida = r;
        Console.WriteLine("  Spotify: sortida de so " + r);
    }
    public static string UltimaSortida = "";

    // ---------- Finestra ----------
    static void Finestra(bool visible)
    {
        Assegura();
        Edge.Finestra(targetId, visible);
    }

    static void Reinicia()
    {
        lock (lk)
        {
            if (sessio != null) { sessio.Tanca(); sessio = null; }
            token = null;
        }
        try { if (targetId != null) Edge.TancaPagina(targetId); } catch { }
        targetId = null;
    }

    // ---------- API ----------
    public static void Api(string nom, Dictionary<string, string> q, HttpListenerContext ctx)
    {
        HttpListenerResponse res = ctx.Response;
        // Mentre l'app de Spotify esta oberta, el so del PC ja es prepara (aixi a l'iPad comenca de seguida)
        if (nom != "so") ThreadPool.QueueUserWorkItem(delegate { SoPC.Escalfa(); });
        try
        {
            switch (nom)
            {
                case "estat":
                    {
                        string e = Crida("estat()", 8000);
                        string extra = ",\"token\":" + (token != null ? "true" : "false") + ",\"oients\":" + SoPC.Oients + "}";
                        CasaServidor.Text(res, (e.StartsWith("{") ? e.TrimEnd('}') : "{\"error\":\"pont\"") + extra, "application/json; charset=utf-8");
                        return;
                    }
                case "ordre":
                    CasaServidor.Text(res, Crida("ordre(" + Js(CasaServidor.Q(q, "o")) + ")", 8000), "application/json; charset=utf-8");
                    return;
                case "toca":
                    CasaServidor.Text(res, Crida("toca(" + Js(CasaServidor.Q(q, "uri")) + ")", 20000), "application/json; charset=utf-8");
                    return;
                case "cerca":
                    {
                        Assegura();
                        string t = TokenSiHiHa();
                        CasaServidor.Text(res, Crida("cerca(" + Js(t) + "," + Js(CasaServidor.Q(q, "q")) + ")", 25000), "application/json; charset=utf-8");
                        return;
                    }
                case "biblioteca":
                    {
                        Assegura();
                        string t = TokenSiHiHa();
                        CasaServidor.Text(res, Crida("biblioteca(" + Js(t) + ")", 20000), "application/json; charset=utf-8");
                        return;
                    }
                case "so":
                    SoPC.Serveix(ctx);
                    return;
            }
            CasaServidor.Error(res, 404, "API desconeguda");
        }
        catch (Exception e)
        {
            darrerError = e.Message;
            Console.WriteLine("  Spotify: " + e.Message);
            try { CasaServidor.Error(res, 503, e.Message); } catch { }
        }
    }

    public static void Admin(string nom, Dictionary<string, string> q, HttpListenerContext ctx)
    {
        HttpListenerResponse res = ctx.Response;
        try
        {
            switch (nom)
            {
                case "info":
                    {
                        Dictionary<string, object> r = new Dictionary<string, object>();
                        r["edge"] = Edge.Ruta() != null;
                        r["edgeEnMarxa"] = Edge.Viu();
                        r["connectat"] = sessio != null && sessio.Viu;
                        r["token"] = token != null;
                        string ff = SoPC.RutaFfmpeg();
                        r["ffmpeg"] = ff != null;
                        r["ffmpegRuta"] = ff ?? "";
                        r["dispositius"] = ff != null ? SoPC.Dispositius() : new List<string>();
                        r["ajustos"] = Ajustos();
                        r["error"] = darrerError + (SoPC.DarrerError != "" ? " " + SoPC.DarrerError : "");
                        CasaServidor.Json(res, r);
                        return;
                    }
                case "mostra": Finestra(true); CasaServidor.Text(res, "{\"ok\":true}", "application/json"); return;
                case "amaga": Finestra(false); CasaServidor.Text(res, "{\"ok\":true}", "application/json"); return;
                case "reinicia": Reinicia(); Assegura(); CasaServidor.Text(res, "{\"ok\":true}", "application/json"); return;
                case "sortida": AplicaSortida(); SoPC.Atura(); CasaServidor.Text(res, "{\"ok\":true}", "application/json"); return;
                case "dispositius-web": CasaServidor.Text(res, Crida("dispositius()", 8000), "application/json; charset=utf-8"); return;
            }
            CasaServidor.Error(res, 404, "API desconeguda");
        }
        catch (Exception e)
        {
            darrerError = e.Message;
            try { CasaServidor.Error(res, 503, e.Message); } catch { }
        }
    }

    public static string Ajuda(string k, string def) { return Ajust(k, def); }
}

// ---------- So del PC cap a l'iPad (ffmpeg, MP3 en directe) ----------
public static class SoPC
{
    static readonly object lk = new object();
    static Process ff;
    static readonly List<BlockingCollection<byte[]>> clients = new List<BlockingCollection<byte[]>>();
    static Timer aturada;
    // Els darrers segons de so: s'envien de cop a qui es connecta, perque el reproductor comenci de seguida
    static readonly LinkedList<byte[]> recent = new LinkedList<byte[]>();
    static int recentBytes = 0;
    const int Rafega = 192000 / 8 * 4;   // ~4 s a 192 kbps
    public static string DarrerError = "";
    public static int Oients { get { lock (lk) return clients.Count; } }

    public static string RutaFfmpeg()
    {
        string conf = Spotify.Ajuda("ffmpeg", "");
        if (conf != "" && File.Exists(conf)) return conf;
        string local = Path.Combine(CasaServidor.DirArrel, "ffmpeg.exe");
        if (File.Exists(local)) return local;
        local = Path.Combine(CasaServidor.DirArrel, @"eines\ffmpeg.exe");
        if (File.Exists(local)) return local;
        local = Path.Combine(CasaServidor.DirArrel, @"ffmpeg\bin\ffmpeg.exe");
        if (File.Exists(local)) return local;
        foreach (string d in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';'))
        {
            try { string f = Path.Combine(d.Trim(), "ffmpeg.exe"); if (File.Exists(f)) return f; } catch { }
        }
        return null;
    }

    public static List<string> Dispositius()
    {
        List<string> l = new List<string>();
        string exe = RutaFfmpeg();
        if (exe == null) return l;
        ProcessStartInfo psi = new ProcessStartInfo(exe, "-hide_banner -list_devices true -f dshow -i dummy");
        psi.UseShellExecute = false; psi.CreateNoWindow = true; psi.RedirectStandardError = true;
        psi.StandardErrorEncoding = Encoding.UTF8;
        using (Process p = Process.Start(psi))
        {
            string txt = p.StandardError.ReadToEnd();
            p.WaitForExit(5000);
            bool seccioAudio = false;
            foreach (string linia in txt.Split('\n'))
            {
                if (linia.Contains("DirectShow audio devices")) { seccioAudio = true; continue; }
                if (linia.Contains("DirectShow video devices")) { seccioAudio = false; continue; }
                if (linia.Contains("Alternative name")) continue;
                Match m = Regex.Match(linia, "\"([^\"]+)\"");
                if (!m.Success) continue;
                if (linia.Contains("(audio)") || (seccioAudio && !linia.Contains("(video)"))) if (!l.Contains(m.Groups[1].Value)) l.Add(m.Groups[1].Value);
            }
        }
        return l;
    }

    static void Engega()
    {
        string exe = RutaFfmpeg();
        if (exe == null) throw new Exception("No trobo ffmpeg.exe. Posa'l a la carpeta del servidor.");
        string disp = Spotify.Ajuda("dispositiu", "CABLE Output (VB-Audio Virtual Cable)");
        string args = "-hide_banner -loglevel error -f dshow -audio_buffer_size 80 -i audio=\"" + disp.Replace("\"", "") + "\" " +
            "-ac 2 -ar 44100 -c:a libmp3lame -b:a 192k -f mp3 -flush_packets 1 -";
        ProcessStartInfo psi = new ProcessStartInfo(exe, args);
        psi.UseShellExecute = false; psi.CreateNoWindow = true;
        psi.RedirectStandardOutput = true; psi.RedirectStandardError = true;
        Process p = Process.Start(psi);
        ff = p;
        DarrerError = "";
        Thread te = new Thread(delegate () { try { string l; while ((l = p.StandardError.ReadLine()) != null) { DarrerError = "ffmpeg: " + l; Console.WriteLine("  " + DarrerError); } } catch { } });
        te.IsBackground = true; te.Start();
        Thread tl = new Thread(delegate ()
        {
            byte[] buf = new byte[4096];
            Stream s = p.StandardOutput.BaseStream;
            try
            {
                int n;
                while ((n = s.Read(buf, 0, buf.Length)) > 0)
                {
                    byte[] c = new byte[n];
                    Buffer.BlockCopy(buf, 0, c, 0, n);
                    lock (lk)
                    {
                        recent.AddLast(c); recentBytes += n;
                        while (recentBytes > Rafega && recent.Count > 1) { recentBytes -= recent.First.Value.Length; recent.RemoveFirst(); }
                        foreach (BlockingCollection<byte[]> q in clients) { try { if (!q.IsAddingCompleted) q.TryAdd(c); } catch { } }
                    }
                }
            }
            catch { }
            lock (lk) { if (ff == p) { ff = null; recent.Clear(); recentBytes = 0; } foreach (BlockingCollection<byte[]> q in clients) q.CompleteAdding(); }
        });
        tl.IsBackground = true; tl.Start();
    }

    // Si ningu escolta el MP3, allibera el dispositiu de so (per a la captura a trossos)
    public static bool AlliberaSiLliure()
    {
        lock (lk)
        {
            if (clients.Count > 0) return false;
            if (aturada != null) { aturada.Dispose(); aturada = null; }
            bool havia = ff != null && !ff.HasExited;
            Atura();
            return havia;
        }
    }

    public static void Atura()
    {
        lock (lk)
        {
            try { if (ff != null && !ff.HasExited) ff.Kill(); } catch { }
            ff = null;
        }
    }

    // Engega ffmpeg abans que ningu l'escolti (quan s'obre l'app de Spotify), i l'atura si ningu l'escolta en 10 min
    public static void Escalfa()
    {
        if (Pcm.SpotifyViu()) return;   // l'iPad ja l'escolta a trossos: no capturis dues vegades el mateix dispositiu
        lock (lk)
        {
            try { if (ff == null || ff.HasExited) Engega(); } catch { return; }
            if (clients.Count == 0)
            {
                if (aturada != null) aturada.Dispose();
                aturada = new Timer(delegate { lock (lk) { if (clients.Count == 0) Atura(); } }, null, 600000, Timeout.Infinite);
            }
        }
    }

    public static void Serveix(HttpListenerContext ctx)
    {
        HttpListenerResponse res = ctx.Response;
        BlockingCollection<byte[]> q = new BlockingCollection<byte[]>(2000);
        lock (lk)
        {
            if (aturada != null) { aturada.Dispose(); aturada = null; }
            if (ff == null || ff.HasExited) Engega();
            foreach (byte[] b0 in recent) q.TryAdd(b0);
            clients.Add(q);
        }
        res.ContentType = "audio/mpeg";
        res.AddHeader("Cache-Control", "no-cache, no-store");
        res.SendChunked = true;
        try
        {
            byte[] b;
            while (q.TryTake(out b, 20000)) res.OutputStream.Write(b, 0, b.Length);
        }
        catch { }
        finally
        {
            lock (lk)
            {
                clients.Remove(q);
                if (clients.Count == 0)
                {
                    if (aturada != null) aturada.Dispose();
                    aturada = new Timer(delegate { lock (lk) { if (clients.Count == 0) Atura(); } }, null, 300000, Timeout.Infinite);
                }
            }
        }
    }
}
