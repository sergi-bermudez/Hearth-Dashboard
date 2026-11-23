// Servidor Casa - servidor HTTP lleuger per al dashboard de l'iPad.
// Es compila sol amb Add-Type des de servidor.ps1 (.NET Framework, C# 5).
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;

public static class CasaServidor
{
    static volatile Dictionary<string, string> Arrels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    static string Www, Dades, Minis, ConfigRuta, Clau = "";
    internal static string DirDades { get { return Dades; } }
    internal static string RutaArrel(string c) { string r; return Arrels.TryGetValue(c, out r) ? r : null; }
    static int fotoFeina = 0;
    static volatile bool FotoIndexant = false;
    internal static void RefrescaFotos()
    {
        if (Interlocked.CompareExchange(ref fotoFeina, 1, 0) != 0) return;
        ThreadPool.QueueUserWorkItem(delegate
        {
            FotoIndexant = true;
            try { IndexFotos(true); } catch (Exception e) { Console.WriteLine("  Fotos: " + e.Message); }
            finally { FotoIndexant = false; fotoFeina = 0; }
        });
    }
    // Llista instantania a partir de l'index desat (sense tocar el disc de les fotos)
    static List<object[]> FotosDeLaCache()
    {
        lock (FotoLock)
        {
            if (FotoLlista != null) return FotoLlista;
            List<object[]> l = new List<object[]>();
            try
            {
                string f = Path.Combine(Dades, "index-fotos.json");
                if (!File.Exists(f)) { FotoLlista = l; return l; }
                JavaScriptSerializer js = new JavaScriptSerializer(); js.MaxJsonLength = int.MaxValue;
                Dictionary<string, string> c = js.Deserialize<Dictionary<string, string>>(File.ReadAllText(f, Encoding.UTF8));
                foreach (KeyValuePair<string, string> kv in c)
                {
                    string[] p = kv.Value.Split('|');
                    long d; if (p.Length < 3 || !long.TryParse(p[2], out d)) continue;
                    bool v = Array.IndexOf(ExtVid, Path.GetExtension(kv.Key).ToLowerInvariant()) >= 0;
                    l.Add(v ? new object[] { kv.Key, d, 1 } : new object[] { kv.Key, d });
                }
                l.Sort(delegate (object[] a, object[] b) { return ((long)b[1]).CompareTo((long)a[1]); });
                FotoLlista = l;
            }
            catch { }
            return l;
        }
    }
    internal static string DirArrel { get { return Path.GetDirectoryName(Www.TrimEnd('\\')); } }
    static int Port = 8080;
    static DateTime Arrencada = DateTime.Now;
    static readonly string[] Categories = { "pelis", "musica", "fotos", "documents" };
    static readonly SemaphoreSlim Sem = new SemaphoreSlim(2);
    static readonly object FotoLock = new object();
    static readonly object DadesLock = new object();
    static Dictionary<string, string> FotoCache;
    static List<object[]> FotoLlista;
    static DateTime FotoEscaneig = DateTime.MinValue;
    static readonly DateTime Epoca = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    static readonly string[] ExtImg = { ".jpg", ".jpeg", ".png", ".gif", ".bmp" };
    static readonly string[] ExtVid = { ".mp4", ".m4v", ".mov" };
    static readonly string[] Portades = { "cover.jpg", "folder.jpg", "poster.jpg", "front.jpg", "cover.png", "folder.png", "poster.png", "portada.jpg" };

    // ---------- Configuracio ----------
    static void CarregaConfig()
    {
        Dictionary<string, object> d = null;
        try
        {
            if (File.Exists(ConfigRuta))
                d = new JavaScriptSerializer().DeserializeObject(File.ReadAllText(ConfigRuta, Encoding.UTF8).TrimStart('\uFEFF')) as Dictionary<string, object>;
        }
        catch (Exception e) { Console.WriteLine("  AVIS: config.json no es valid (" + e.Message + "). Faig servir els valors per defecte."); }
        Dictionary<string, string> noves = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (d == null)
        {
            noves["pelis"] = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
            noves["musica"] = Environment.GetFolderPath(Environment.SpecialFolder.MyMusic);
            noves["fotos"] = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
            noves["documents"] = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Casa");
            try { Directory.CreateDirectory(noves["documents"]); } catch { }
            Arrels = noves;
            DesaConfig();
            return;
        }
        object v;
        if (d.TryGetValue("port", out v) && v != null) { try { Port = Convert.ToInt32(v); } catch { } }
        if (d.TryGetValue("clau", out v) && v != null) Clau = v.ToString();
        if (d.TryGetValue("carpetes", out v) && v is Dictionary<string, object>)
            foreach (KeyValuePair<string, object> kv in (Dictionary<string, object>)v)
                if (kv.Value != null && kv.Value.ToString().Trim() != "") noves[kv.Key] = kv.Value.ToString().Trim();
        Arrels = noves;
    }

    static void DesaConfig()
    {
        Dictionary<string, object> c = new Dictionary<string, object>();
        foreach (KeyValuePair<string, string> kv in Arrels) c[kv.Key] = kv.Value;
        Dictionary<string, object> d = new Dictionary<string, object>();
        d["port"] = Port; d["clau"] = Clau; d["carpetes"] = c;
        string json = new JavaScriptSerializer().Serialize(d).Replace(",\"", ",\n  \"").Replace("{\"port", "{\n  \"port");
        File.WriteAllText(ConfigRuta, json + "\n", new UTF8Encoding(false));
    }

    public static void Iniciar(string config, string www, string dades)
    {
        ConfigRuta = config; Www = www; Dades = dades; Minis = Path.Combine(dades, "miniatures");
        Directory.CreateDirectory(Dades); Directory.CreateDirectory(Minis);
        CarregaConfig();
        int port = Port;
        Console.WriteLine("");
        Console.WriteLine("  SERVIDOR CASA");
        Console.WriteLine("  Carpetes:");
        foreach (KeyValuePair<string, string> kv in Arrels)
            Console.WriteLine("    " + kv.Key.PadRight(10) + " " + kv.Value + (Directory.Exists(kv.Value) ? "" : "   (NO EXISTEIX)"));
        Console.WriteLine("");
        Console.WriteLine("  Obre a l'iPad (Safari):");
        foreach (string ip in Ips()) Console.WriteLine("    http://" + ip + ":" + port + "/");
        Console.WriteLine("  Configuracio: http://localhost:" + port + "/config.html");
        Console.WriteLine("");
        Console.WriteLine("  Tanca aquesta finestra per aturar el servidor.");
        Console.WriteLine("");
        try { ServicePointManager.SecurityProtocol = (SecurityProtocolType)(3072 | 768 | 192); } catch { }
        ServicePointManager.DefaultConnectionLimit = 64;
        ServicePointManager.Expect100Continue = false;
        CapcaleresPermissives();
        ThreadPool.SetMinThreads(32, 32);

        HttpListener l = new HttpListener();
        l.Prefixes.Add("http://+:" + port + "/");
        try { l.Start(); }
        catch (HttpListenerException)
        {
            Console.WriteLine("No puc obrir el port " + port + " per a la xarxa.");
            Console.WriteLine("Executa \"Instal-la Hearth.bat\" (nomes un cop).");
            Console.WriteLine("Mentrestant nomes funciona en aquest PC: http://localhost:" + port + "/");
            l = new HttpListener();
            l.Prefixes.Add("http://localhost:" + port + "/");
            l.Start();
        }
        FotosDeLaCache();
        RefrescaFotos();
        Musica.Indexa();
        Pelis.Indexa();
        Mdns.Inicia("hearth");
        if (FuncioActiva("spotify")) Spotify.AlArrencar();
        if (FuncioActiva("whatsapp")) WhatsApp.AlArrencar();
        GFotos.AlArrencar();
        Actualitza.Inicia();
        Botigues.Inicia();
        Webs.Inicia();
        while (true)
        {
            HttpListenerContext ctx;
            try { ctx = l.GetContext(); } catch (Exception e) { Console.WriteLine(e.Message); continue; }
            ThreadPool.QueueUserWorkItem(Atendre, ctx);
        }
    }

    // ---------- Peticions ----------
    static void Atendre(object o)
    {
        HttpListenerContext ctx = (HttpListenerContext)o;
        HttpListenerResponse res = ctx.Response;
        try
        {
            string raw = ctx.Request.RawUrl, cami = raw, qs = "";
            int qi = raw.IndexOf('?');
            if (qi >= 0) { cami = raw.Substring(0, qi); qs = raw.Substring(qi + 1); }
            Dictionary<string, string> q = Consulta(qs);
            cami = Uri.UnescapeDataString(cami);
            // El Chromecast necessita CORS per reproduir HLS; mai per a l'administracio
            if (!cami.StartsWith("/api/admin")) { try { res.AddHeader("Access-Control-Allow-Origin", "*"); } catch { } }
            if (cami.StartsWith("/api/"))
            {
                if (!cami.StartsWith("/api/mini") && !cami.StartsWith("/api/x"))
                    Console.WriteLine(DateTime.Now.ToString("HH:mm:ss") + " " + ctx.Request.HttpMethod + " " + cami);
                Api(cami.Substring(5), q, ctx);
            }
            else if (cami.StartsWith("/f/"))
            {
                int s = cami.IndexOf('/', 3);
                string c = s < 0 ? cami.Substring(3) : cami.Substring(3, s - 3);
                string rel = s < 0 ? "" : cami.Substring(s + 1);
                string full = Ruta(c, rel);
                if (full == null || !File.Exists(full)) Error(res, 404, "No trobat");
                else EnviarFitxer(ctx, full);
            }
            else
            {
                string rel = cami == "/" ? "index.html" : cami.TrimStart('/');
                string full = Segur(Www, rel);
                if (full == null || !File.Exists(full)) Error(res, 404, "No trobat");
                else EnviarFitxer(ctx, full);
            }
        }
        catch (HttpListenerException) { }
        catch (IOException) { }
        catch (Exception e)
        {
            Console.WriteLine("  Error: " + e.Message);
            try { Error(res, 500, e.Message); } catch { }
        }
        finally { try { res.Close(); } catch { } }
    }

    static void Api(string nom, Dictionary<string, string> q, HttpListenerContext ctx)
    {
        HttpListenerResponse res = ctx.Response;
        bool post = ctx.Request.HttpMethod == "POST";
        string c = Q(q, "c"), p = Q(q, "p");
        switch (nom)
        {
            case "config":
                {
                    Dictionary<string, object> d = new Dictionary<string, object>();
                    d["carpetes"] = new List<string>(Arrels.Keys);
                    d["versio"] = "1.0";
                    Json(res, d);
                    return;
                }
            case "llista": Llista(res, c, p); return;
            case "fotos":
                {
                    List<object[]> l = FotoLlista;
                    if (l == null) l = FotosDeLaCache();
                    if (Q(q, "nou") == "1" || (DateTime.Now - FotoEscaneig).TotalSeconds > 60) RefrescaFotos();
                    if (FotoIndexant) res.AddHeader("X-Indexant", "1");
                    Json(res, l ?? new List<object[]>());
                    return;
                }
            case "foto-info":
                {
                    // On es va fer la foto (les coordenades GPS de la foto, tradu\u00efdes a un nom de lloc)
                    string full = Ruta("fotos", p);
                    if (full == null || !File.Exists(full)) { Error(res, 404, "No trobat"); return; }
                    string lloc = LlocDeFoto(full, Q(q, "ll"));
                    Json(res, Dic("lloc", lloc ?? "", "pendent", lloc == null));
                    return;
                }
            case "mini":
                {
                    string full = Ruta(c, p);
                    if (full == null || !File.Exists(full)) { Error(res, 404, "No trobat"); return; }
                    int m;
                    if (!int.TryParse(Q(q, "m"), out m)) m = 320;
                    m = Math.Max(64, Math.Min(2048, m));
                    string mini = Miniatura(full, m);
                    if (mini == null) { Error(res, 415, "Format no compatible"); return; }
                    EnviarFitxer(ctx, mini);
                    return;
                }
            case "pujar":
                {
                    if (!post) { Error(res, 405, "POST"); return; }
                    string dir = Ruta(c, p);
                    if (dir == null) { Error(res, 400, "Ruta"); return; }
                    Directory.CreateDirectory(dir);
                    string nomf = NomNet(Q(q, "nom"));
                    if (nomf == "") nomf = "fitxer_" + DateTime.Now.ToString("yyyyMMdd_HHmmss");
                    string desti = NoRepetit(Path.Combine(dir, nomf));
                    using (FileStream fs = new FileStream(desti, FileMode.CreateNew))
                        ctx.Request.InputStream.CopyTo(fs, 65536);
                    if (string.Equals(c, "fotos", StringComparison.OrdinalIgnoreCase)) FotoEscaneig = DateTime.MinValue;
                    Dictionary<string, object> d = new Dictionary<string, object>();
                    d["ok"] = true; d["nom"] = Path.GetFileName(desti);
                    Json(res, d);
                    return;
                }
            case "desar":
                {
                    if (!post) { Error(res, 405, "POST"); return; }
                    if (!string.Equals(c, "documents", StringComparison.OrdinalIgnoreCase)) { Error(res, 403, "Nomes a documents"); return; }
                    string full = Ruta(c, p);
                    string ext = full == null ? "" : Path.GetExtension(full).ToLowerInvariant();
                    if (full == null || (ext != ".html" && ext != ".htm" && ext != ".txt" && ext != ".doc")) { Error(res, 400, "Nom no valid"); return; }
                    Directory.CreateDirectory(Path.GetDirectoryName(full));
                    string text;
                    using (StreamReader sr = new StreamReader(ctx.Request.InputStream, Encoding.UTF8)) text = sr.ReadToEnd();
                    File.WriteAllText(full, text, new UTF8Encoding(ext == ".doc"));
                    Dictionary<string, object> d = new Dictionary<string, object>();
                    d["ok"] = true; d["t"] = Ms(File.GetLastWriteTimeUtc(full));
                    Json(res, d);
                    return;
                }
            case "esborrar":
                {
                    if (!post) { Error(res, 405, "POST"); return; }
                    if (!string.Equals(c, "documents", StringComparison.OrdinalIgnoreCase)) { Error(res, 403, "Nomes a documents"); return; }
                    string full = Ruta(c, p);
                    if (full == null || !File.Exists(full)) { Error(res, 404, "No trobat"); return; }
                    try
                    {
                        Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(full,
                            Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                            Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
                    }
                    catch { File.Delete(full); }
                    Dictionary<string, object> d = new Dictionary<string, object>();
                    d["ok"] = true;
                    Json(res, d);
                    return;
                }
            case "dades":
                {
                    string n = Q(q, "n");
                    if (!Regex.IsMatch(n, "^[a-z0-9_]{1,40}$") || n == "tuya") { Error(res, 400, "Nom"); return; }
                    string f = Path.Combine(Dades, n + ".json");
                    if (post)
                    {
                        string text;
                        using (StreamReader sr = new StreamReader(ctx.Request.InputStream, Encoding.UTF8)) text = sr.ReadToEnd();
                        lock (DadesLock)
                        {
                            // c\u00f2pia de l'anterior, per si de cas (p. ex. una llista de la compra trepitjada)
                            try { if (File.Exists(f)) File.Copy(f, Path.Combine(Dades, n + ".anterior.json"), true); } catch { }
                            File.WriteAllText(f, text, new UTF8Encoding(false));
                        }
                        Text(res, "{\"ok\":true}", "application/json; charset=utf-8");
                    }
                    else
                    {
                        string text;
                        lock (DadesLock) text = File.Exists(f) ? File.ReadAllText(f, Encoding.UTF8) : "null";
                        Text(res, text, "application/json; charset=utf-8");
                    }
                    return;
                }
            case "x": Intermediari(ctx, Q(q, "u"), Q(q, "directe") == "1", Q(q, "directe") == "1" && Q(q, "tal") != "1"); return;
            case "hls": Hls(ctx, Q(q, "u")); return;
            case "vtt":
                {
                    string full = Ruta(c, p);
                    if (full == null || !File.Exists(full)) { Error(res, 404, "No trobat"); return; }
                    Text(res, SrtAVtt(LlegirText(full)), "text/vtt; charset=utf-8");
                    return;
                }
        }
        if (nom.StartsWith("spotify/")) { if (!FuncioActiva("spotify")) { Error(res, 403, "Spotify est\u00e0 desactivat (Configuraci\u00f3 \u25b8 Funcions opcionals)"); return; } Spotify.Api(nom.Substring(8), q, ctx); return; }
        if (nom.StartsWith("web/")) { Webs.Api(nom.Substring(4), q, ctx); return; }
        if (nom == "jo") { Text(res, "{\"ip\":\"" + ((IPEndPoint)ctx.Request.RemoteEndPoint).Address + "\"}", "application/json; charset=utf-8"); return; }
        if (nom == "ip") { Text(res, "{\"ip\":\"" + ((IPEndPoint)ctx.Request.LocalEndPoint).Address + "\"}", "application/json; charset=utf-8"); return; }
        if (nom == "radios") { Radios.Api(q, ctx); return; }
        if (nom.StartsWith("pcm/")) { Pcm.Api(nom.Substring(4), q, ctx); return; }
        if (nom.StartsWith("cast/")) { Cast.Api(nom, q, ctx, Port); return; }
        if (nom.StartsWith("compra/")) { Botigues.Api(nom.Substring(7), q, ctx, false); return; }
        if (nom.StartsWith("pantalles/")) { Pantalles.Api(nom.Substring(10), q, ctx); return; }
        if (nom.StartsWith("futbol/")) { Futbol.Api(nom.Substring(7), q, ctx); return; }
        if (nom.StartsWith("radio/")) { RadioHls.Api(nom.Substring(6), q, ctx); return; }
        if (nom.StartsWith("tuya/")) { Tuya.Api(nom.Substring(5), q, ctx); return; }
        if (nom == "agenda") { Agenda.Api(q, ctx); return; }
        if (nom.StartsWith("pelis/")) { Pelis.Api(nom, q, ctx); return; }
        if (nom == "musica") { Musica.Api(q, ctx); return; }
        if (nom.StartsWith("youtube/") && nom != "youtube/instal-la") { YouTube.Api(nom.Substring(8), q, ctx); return; }
        if (nom == "agenda/calendaris") { Agenda.Llista(q, ctx); return; }
        if (nom == "imgx" || nom == "portada" || nom == "caratula" || nom == "peli" || nom == "peli-titol" || nom == "peli-opcions" || nom == "peli-tria" || nom == "peli-desfes") { Media.Api(nom, q, ctx); return; }
        if (nom.StartsWith("admin/")) { Admin(nom.Substring(6), q, ctx); return; }
        Error(res, 404, "API desconeguda");
    }

    // ---------- Administracio (pagina de configuracio) ----------
    static void Admin(string nom, Dictionary<string, string> q, HttpListenerContext ctx)
    {
        HttpListenerResponse res = ctx.Response;
        if (!string.IsNullOrEmpty(Clau) && !ctx.Request.IsLocal && ctx.Request.Headers["X-Clau"] != Clau) { Error(res, 401, "Cal la contrasenya"); return; }
        bool post = ctx.Request.HttpMethod == "POST";
        if (nom.StartsWith("spotify/")) { Spotify.Admin(nom.Substring(8), q, ctx); return; }
        if (nom.StartsWith("gfotos/")) { GFotos.Admin(nom.Substring(7), q, ctx); return; }
        if (nom == "youtube/instal-la") { YouTube.Api("instal-la", q, ctx); return; }
        if (nom == "tuya/ajustos") { Tuya.Ajusta(ctx); return; }
        if (nom.StartsWith("actualitza/")) { Actualitza.Api(nom, q, ctx); return; }
        if (nom == "diagnosi") { Diagnosi.Api(ctx); return; }
        if (nom == "tele/obre" || nom == "tele/atura" || nom == "tele/llista")
        {
            // Un sol boto: obre Hearth en mode tele a pantalla completa al PC i el projecta a la tele (Miracast),
            // o ho desfa. La projeccio la fa eines/tele.ps1 amb el panell Connecta de Windows.
            try
            {
                if (nom == "tele/obre")
                {
                    string perfil = Path.Combine(Dades, "edge-tele");
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Edge.Ruta(),
                        "--kiosk \"http://localhost:" + Port + "/?tele=1\" --edge-kiosk-type=fullscreen --no-first-run --autoplay-policy=no-user-gesture-required --user-data-dir=\"" + perfil + "\"") { UseShellExecute = false });
                    System.Threading.Thread.Sleep(3000);
                }
                string tele = Q(q, "nom");
                System.Diagnostics.ProcessStartInfo pi = new System.Diagnostics.ProcessStartInfo("powershell.exe",
                    "-NoProfile -ExecutionPolicy Bypass -File \"" + Path.Combine(DirArrel, "eines", "tele.ps1") + "\" " + (nom == "tele/atura" ? "-Atura" : nom == "tele/llista" ? "-Llista" : Q(q, "manual") == "1" ? "-Manual" : "-Nom \"" + tele.Replace("\"", "") + "\""));
                pi.UseShellExecute = false; pi.RedirectStandardOutput = true; pi.CreateNoWindow = true;
                System.Diagnostics.Process pr = System.Diagnostics.Process.Start(pi);
                string sortida = pr.StandardOutput.ReadToEnd();
                pr.WaitForExit(40000);
                sortida = sortida.Trim();
                if (nom == "tele/llista") { Text(res, sortida.StartsWith("[") ? sortida : "[]", "application/json; charset=utf-8"); return; }
                Json(res, Dic("ok", sortida.StartsWith("ok") || sortida == "no-projectava" || sortida == "manual", "manual", sortida == "manual" || sortida == "ok:manual", "resultat", sortida));
            }
            catch (Exception ex) { Error(res, 500, ex.Message); }
            return;
        }
        if (nom.StartsWith("compra/")) { Botigues.Api(nom.Substring(7), q, ctx, true); return; }
        if (nom.StartsWith("wa/")) { if (nom != "wa/atura" && !FuncioActiva("whatsapp")) { Error(ctx.Response, 403, "WhatsApp est\u00e0 desactivat (Configuraci\u00f3 \u25b8 Funcions opcionals)"); return; } WhatsApp.Api(nom.Substring(3), q, ctx); return; }
        switch (nom)
        {
            case "config":
                {
                    if (post)
                    {
                        string text;
                        using (StreamReader sr = new StreamReader(ctx.Request.InputStream, Encoding.UTF8)) text = sr.ReadToEnd();
                        Dictionary<string, object> d = new JavaScriptSerializer().DeserializeObject(text) as Dictionary<string, object>;
                        if (d == null) { Error(res, 400, "JSON"); return; }
                        object v;
                        if (d.TryGetValue("carpetes", out v) && v is Dictionary<string, object>)
                        {
                            Dictionary<string, string> noves = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                            foreach (KeyValuePair<string, object> kv in (Dictionary<string, object>)v)
                                if (kv.Value != null && kv.Value.ToString().Trim() != "" && Regex.IsMatch(kv.Key, "^[a-z0-9_]{1,20}$"))
                                    noves[kv.Key] = kv.Value.ToString().Trim();
                            string fotosAbans, fotosAra;
                            Arrels.TryGetValue("fotos", out fotosAbans);
                            noves.TryGetValue("fotos", out fotosAra);
                            Arrels = noves;
                            if (fotosAbans != fotosAra)
                            {
                                FotoEscaneig = DateTime.MinValue;
                                ThreadPool.QueueUserWorkItem(delegate { try { IndexFotos(true); } catch { } });
                Musica.Indexa();
                            }
                        }
                        if (d.TryGetValue("clau", out v) && v != null) Clau = v.ToString();
                        DesaConfig();
                        Console.WriteLine("  Configuracio desada.");
                    }
                    Dictionary<string, object> r = new Dictionary<string, object>();
                    List<object> cs = new List<object>();
                    List<string> claus = new List<string>(Categories);
                    foreach (string k in Arrels.Keys) if (!claus.Contains(k)) claus.Add(k);
                    foreach (string k in claus)
                    {
                        string ruta;
                        Arrels.TryGetValue(k, out ruta);
                        Dictionary<string, object> e = new Dictionary<string, object>();
                        e["c"] = k; e["ruta"] = ruta ?? ""; e["existeix"] = ruta != null && Directory.Exists(ruta);
                        cs.Add(e);
                    }
                    r["carpetes"] = cs; r["port"] = Port; r["clau"] = !string.IsNullOrEmpty(Clau);
                    r["local"] = ctx.Request.IsLocal; r["ips"] = Ips(); r["versio"] = Actualitza.Local(); r["nomXarxa"] = Mdns.Nom + ".local";
                    Json(res, r);
                    return;
                }
            case "explora": Explora(res, Q(q, "r")); return;
            case "estat":
                {
                    Dictionary<string, object> r = new Dictionary<string, object>();
                    List<object[]> fl = FotoLlista;
                    r["fotos"] = fl == null ? -1 : fl.Count;
                    long mida = 0; int n = 0;
                    try { foreach (string f in Directory.GetFiles(Minis, "*.jpg", SearchOption.AllDirectories)) { mida += new FileInfo(f).Length; n++; } } catch { }
                    r["miniatures"] = n; r["miniaturesMida"] = mida;
                    r["minuts"] = (int)(DateTime.Now - Arrencada).TotalMinutes;
                    Json(res, r);
                    return;
                }
            case "reindexa":
                ThreadPool.QueueUserWorkItem(delegate { try { IndexFotos(true); } catch { } });
                Musica.Indexa();
                Text(res, "{\"ok\":true}", "application/json; charset=utf-8");
                return;
            case "esborra-miniatures":
                try { Directory.Delete(Minis, true); } catch { }
                Directory.CreateDirectory(Minis);
                Text(res, "{\"ok\":true}", "application/json; charset=utf-8");
                return;
        }
        Error(res, 404, "API desconeguda");
    }

    static void Explora(HttpListenerResponse res, string r)
    {
        List<Dictionary<string, object>> carps = new List<Dictionary<string, object>>();
        string pare = null;
        if (string.IsNullOrEmpty(r))
        {
            string[][] esp = {
                new string[] { "V\u00eddeos", Environment.GetFolderPath(Environment.SpecialFolder.MyVideos) },
                new string[] { "M\u00fasica", Environment.GetFolderPath(Environment.SpecialFolder.MyMusic) },
                new string[] { "Imatges", Environment.GetFolderPath(Environment.SpecialFolder.MyPictures) },
                new string[] { "Documents", Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments) },
                new string[] { "Escriptori", Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory) } };
            foreach (string[] e in esp)
                if (!string.IsNullOrEmpty(e[1]) && Directory.Exists(e[1])) carps.Add(Dic("n", e[0], "r", e[1], "tipus", "especial"));
            foreach (DriveInfo di in DriveInfo.GetDrives())
            {
                if (!di.IsReady) continue;
                string et = "";
                try { et = di.VolumeLabel; } catch { }
                carps.Add(Dic("n", di.Name.TrimEnd('\\') + (et != "" ? " (" + et + ")" : ""), "r", di.Name, "tipus", "unitat"));
            }
        }
        else
        {
            DirectoryInfo dir = new DirectoryInfo(r);
            if (!dir.Exists) { Error(res, 404, "No existeix"); return; }
            pare = dir.Parent == null ? "" : dir.Parent.FullName;
            DirectoryInfo[] subs;
            try { subs = dir.GetDirectories(); } catch { subs = new DirectoryInfo[0]; }
            foreach (DirectoryInfo s in subs)
                if (!Amagat(s.Attributes, s.Name)) carps.Add(Dic("n", s.Name, "r", s.FullName));
            carps.Sort(delegate (Dictionary<string, object> a, Dictionary<string, object> b) { return string.Compare((string)a["n"], (string)b["n"], StringComparison.CurrentCultureIgnoreCase); });
            r = dir.FullName;
        }
        Dictionary<string, object> o = new Dictionary<string, object>();
        o["ruta"] = r ?? ""; o["pare"] = pare; o["carpetes"] = carps;
        Json(res, o);
    }

    internal static Dictionary<string, object> Dic(params object[] kv)
    {
        Dictionary<string, object> d = new Dictionary<string, object>();
        for (int i = 0; i + 1 < kv.Length; i += 2) d[(string)kv[i]] = kv[i + 1];
        return d;
    }

    static List<string> Ips()
    {
        List<string> l = new List<string>();
        try
        {
            foreach (IPAddress a in Dns.GetHostAddresses(Dns.GetHostName()))
                if (a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork && !a.ToString().StartsWith("169.254") && !a.ToString().StartsWith("127.")) l.Add(a.ToString());
        }
        catch { }
        return l;
    }

    // ---------- HLS (TV en directe) a traves del servidor ----------
    static void Hls(HttpListenerContext ctx, string u)
    {
        HttpListenerResponse res = ctx.Response;
        if (string.IsNullOrEmpty(u) || !(u.StartsWith("http://") || u.StartsWith("https://"))) { Error(res, 400, "URL no valida"); return; }
        HttpWebRequest rq = (HttpWebRequest)WebRequest.Create(u);
        rq.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0 Safari/537.36";
        rq.Timeout = 15000;
        string text; Uri baseUri;
        try
        {
            using (HttpWebResponse rs = (HttpWebResponse)rq.GetResponse())
            using (StreamReader sr = new StreamReader(rs.GetResponseStream(), Encoding.UTF8))
            { text = sr.ReadToEnd(); baseUri = rs.ResponseUri; }
        }
        catch (WebException we)
        {
            HttpWebResponse er = we.Response as HttpWebResponse;
            Error(res, er != null ? (int)er.StatusCode : 502, we.Message);
            return;
        }
        StringBuilder sb = new StringBuilder();
        foreach (string linia in text.Replace("\r\n", "\n").Split('\n'))
        {
            string l = linia.Trim();
            if (l.Length == 0) { sb.Append('\n'); continue; }
            if (l.StartsWith("#"))
            {
                Uri b = baseUri;
                sb.Append(Regex.Replace(l, "URI=\"([^\"]+)\"", delegate (Match m) { return "URI=\"" + HlsEnllac(b, m.Groups[1].Value) + "\""; }));
            }
            else sb.Append(HlsEnllac(baseUri, l));
            sb.Append('\n');
        }
        Text(res, sb.ToString(), "application/vnd.apple.mpegurl");
    }

    static string HlsEnllac(Uri b, string rel)
    {
        string abs = new Uri(b, rel).ToString();
        string cami = abs.Split('?')[0].ToLowerInvariant();
        return (cami.EndsWith(".m3u8") ? "/api/hls?u=" : "/api/x?u=") + Uri.EscapeDataString(abs);
    }

    // ---------- Carpetes ----------
    static void Llista(HttpListenerResponse res, string c, string p)
    {
        string dir = Ruta(c, p);
        if (dir == null || !Directory.Exists(dir)) { Error(res, 404, "Carpeta no trobada"); return; }
        DirectoryInfo di = new DirectoryInfo(dir);
        List<object> carpetes = new List<object>(), fitxers = new List<object>();
        foreach (DirectoryInfo d in di.GetDirectories())
        {
            if (Amagat(d.Attributes, d.Name)) continue;
            Dictionary<string, object> e = new Dictionary<string, object>();
            e["n"] = d.Name; e["t"] = Ms(d.LastWriteTimeUtc);
            carpetes.Add(e);
        }
        foreach (FileInfo f in di.GetFiles())
        {
            if (Amagat(f.Attributes, f.Name)) continue;
            Dictionary<string, object> e = new Dictionary<string, object>();
            e["n"] = f.Name; e["m"] = f.Length; e["t"] = Ms(f.LastWriteTimeUtc);
            fitxers.Add(e);
        }
        Dictionary<string, object> r = new Dictionary<string, object>();
        r["carpetes"] = carpetes; r["fitxers"] = fitxers;
        Json(res, r);
    }

    static bool Amagat(FileAttributes a, string nom)
    {
        if ((a & FileAttributes.Hidden) != 0 || (a & FileAttributes.System) != 0) return true;
        return nom.StartsWith(".") || nom.StartsWith("@") || nom.StartsWith("$") || nom.Equals("Thumbs.db", StringComparison.OrdinalIgnoreCase) || nom.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase);
    }

    // ---------- Fotos ----------
    static List<object[]> IndexFotos(bool forcar)
    {
        lock (FotoLock)
        {
            if (!forcar && FotoLlista != null && (DateTime.Now - FotoEscaneig).TotalSeconds < 60) return FotoLlista;
            string fitxerCache = Path.Combine(Dades, "index-fotos.json");
            if (FotoCache == null)
            {
                FotoCache = new Dictionary<string, string>();
                try
                {
                    if (File.Exists(fitxerCache))
                    {
                        JavaScriptSerializer js = new JavaScriptSerializer(); js.MaxJsonLength = int.MaxValue;
                        FotoCache = js.Deserialize<Dictionary<string, string>>(File.ReadAllText(fitxerCache, Encoding.UTF8));
                    }
                }
                catch { FotoCache = new Dictionary<string, string>(); }
            }
            List<object[]> llista = new List<object[]>();
            Dictionary<string, string> nou = new Dictionary<string, string>();
            string arrel;
            if (Arrels.TryGetValue("fotos", out arrel) && Directory.Exists(arrel))
            {
                bool canvis = false;
                int comptador = 0;
                Recorre(new DirectoryInfo(arrel), "", llista, nou, ref canvis, ref comptador);
                try { llista.AddRange(GFotos.Virtuals()); } catch { }
                if (canvis || nou.Count != FotoCache.Count)
                {
                    try
                    {
                        JavaScriptSerializer js = new JavaScriptSerializer(); js.MaxJsonLength = int.MaxValue;
                        File.WriteAllText(fitxerCache, js.Serialize(nou), new UTF8Encoding(false));
                    }
                    catch { }
                }
                FotoCache = nou;
            }
            llista.Sort(delegate (object[] a, object[] b) { return ((long)b[1]).CompareTo((long)a[1]); });
            FotoLlista = llista;
            FotoEscaneig = DateTime.Now;
            return llista;
        }
    }

    static void Recorre(DirectoryInfo dir, string rel, List<object[]> llista, Dictionary<string, string> nou, ref bool canvis, ref int comptador)
    {
        FileInfo[] fitxers;
        DirectoryInfo[] subs;
        try { fitxers = dir.GetFiles(); subs = dir.GetDirectories(); } catch { return; }
        foreach (FileInfo f in fitxers)
        {
            if (Amagat(f.Attributes, f.Name)) continue;
            string ext = f.Extension.ToLowerInvariant();
            bool video = Array.IndexOf(ExtVid, ext) >= 0;
            if (!video && Array.IndexOf(ExtImg, ext) < 0) continue;
            string clau = rel + f.Name;
            string segell = f.Length + "|" + f.LastWriteTimeUtc.Ticks;
            string guardat;
            long data;
            if (FotoCache.TryGetValue(clau, out guardat) && guardat.StartsWith(segell + "|"))
                data = long.Parse(guardat.Substring(segell.Length + 1));
            else
            {
                DateTime? dt = video ? null : DataExif(f.FullName);
                data = Ms(dt.HasValue ? dt.Value.ToUniversalTime() : f.LastWriteTimeUtc);
                canvis = true;
                comptador++;
                if (comptador % 500 == 0) Console.WriteLine("  Indexant fotos: " + comptador + "...");
            }
            nou[clau] = segell + "|" + data;
            llista.Add(video ? new object[] { clau, data, 1 } : new object[] { clau, data });
        }
        foreach (DirectoryInfo d in subs)
            if (!Amagat(d.Attributes, d.Name)) Recorre(d, rel + d.Name + "/", llista, nou, ref canvis, ref comptador);
    }

    // ---------- Lloc de les fotos: GPS de l'EXIF + nom del lloc (OpenStreetMap, guardat a dades\llocs.json) ----------
    static readonly object llocsLk = new object();
    static Dictionary<string, object> llocs;
    static DateTime darreraNominatim = DateTime.MinValue;

    static double Graus(byte[] v)
    {
        // tres racionals: graus, minuts, segons
        double r = 0, div = 1;
        for (int i = 0; i < 3 && v.Length >= i * 8 + 8; i++)
        {
            uint n = BitConverter.ToUInt32(v, i * 8), d = BitConverter.ToUInt32(v, i * 8 + 4);
            if (d != 0) r += (double)n / d / div;
            div *= 60;
        }
        return r;
    }

    static bool GpsDeFoto(string f, out double lat, out double lon)
    {
        lat = lon = 0;
        try
        {
            using (FileStream fs = new FileStream(f, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (Image img = Image.FromStream(fs, false, false))
            {
                int[] ids = img.PropertyIdList;
                if (Array.IndexOf(ids, 2) < 0 || Array.IndexOf(ids, 4) < 0) return false;
                lat = Graus(img.GetPropertyItem(2).Value); lon = Graus(img.GetPropertyItem(4).Value);
                if (Array.IndexOf(ids, 1) >= 0 && Encoding.ASCII.GetString(img.GetPropertyItem(1).Value).StartsWith("S")) lat = -lat;
                if (Array.IndexOf(ids, 3) >= 0 && Encoding.ASCII.GetString(img.GetPropertyItem(3).Value).StartsWith("W")) lon = -lon;
                return !(lat == 0 && lon == 0);
            }
        }
        catch { return false; }
    }

    static string LlocDeFoto(string f, string llengua)
    {
        double lat, lon;
        if (!GpsDeFoto(f, out lat, out lon)) return "";
        if (llengua == "" || llengua.Length > 5) llengua = "ca";
        string clau = lat.ToString("0.00", CultureInfo.InvariantCulture) + "," + lon.ToString("0.00", CultureInfo.InvariantCulture) + "," + llengua;
        string fit = Path.Combine(DirDades, "llocs.json");
        lock (llocsLk)
        {
            if (llocs == null)
            {
                try { llocs = File.Exists(fit) ? new JavaScriptSerializer().DeserializeObject(File.ReadAllText(fit, Encoding.UTF8)) as Dictionary<string, object> : null; } catch { }
                if (llocs == null) llocs = new Dictionary<string, object>();
            }
            object v;
            if (llocs.TryGetValue(clau, out v)) return Convert.ToString(v);
            // No es fa esperar ning\u00fa: el nom es busca en segon pla (la connexi\u00f3 de l'aparell queda lliure per a la m\u00fasica)
            if (!pendentsLloc.Contains(clau))
            {
                pendentsLloc.Add(clau);
                double la = lat, lo = lon; string ll2 = llengua, cl = clau;
                ThreadPool.QueueUserWorkItem(delegate { try { BuscaLloc(la, lo, ll2, cl); } catch { } lock (llocsLk) pendentsLloc.Remove(cl); });
            }
            return null;
        }
    }
    static readonly HashSet<string> pendentsLloc = new HashSet<string>();
    static readonly object nominatimLk = new object();
    static void BuscaLloc(double lat, double lon, string llengua, string clau)
    {
        string fit = Path.Combine(DirDades, "llocs.json");
        lock (nominatimLk)
        {
            // OpenStreetMap demana com a molt una consulta per segon
            double esp = 1100 - (DateTime.Now - darreraNominatim).TotalMilliseconds;
            if (esp > 0) Thread.Sleep((int)esp);
            darreraNominatim = DateTime.Now;
            string nom = "";
            try
            {
                HttpWebRequest rq = (HttpWebRequest)WebRequest.Create("https://nominatim.openstreetmap.org/reverse?format=json&zoom=10&lat=" + lat.ToString(CultureInfo.InvariantCulture) + "&lon=" + lon.ToString(CultureInfo.InvariantCulture) + "&accept-language=" + Uri.EscapeDataString(llengua));
                rq.UserAgent = "Hearth/4 (home photo frame; https://github.com/sergi-bermudez/Hearth-Dashboard)";
                rq.Timeout = 8000;
                string txt;
                using (WebResponse rs = rq.GetResponse())
                using (StreamReader sr = new StreamReader(rs.GetResponseStream(), Encoding.UTF8)) txt = sr.ReadToEnd();
                Dictionary<string, object> d = new JavaScriptSerializer().DeserializeObject(txt) as Dictionary<string, object>;
                Dictionary<string, object> a = d != null && d.ContainsKey("address") ? d["address"] as Dictionary<string, object> : null;
                if (a != null)
                {
                    string poble = "";
                    foreach (string k in new string[] { "city", "town", "village", "municipality", "hamlet", "county" }) if (a.ContainsKey(k) && Convert.ToString(a[k]) != "") { poble = Convert.ToString(a[k]); break; }
                    string zona = "";
                    foreach (string k in new string[] { "island", "state", "region", "country" }) if (a.ContainsKey(k) && Convert.ToString(a[k]) != "" && Convert.ToString(a[k]) != poble) { zona = Convert.ToString(a[k]); break; }
                    nom = poble != "" && zona != "" ? poble + ", " + zona : poble + zona;
                }
            }
            catch { return; }
            lock (llocsLk)
            {
                llocs[clau] = nom;
                try { File.WriteAllText(fit, new JavaScriptSerializer().Serialize(llocs), new UTF8Encoding(false)); } catch { }
            }
        }
    }

    static DateTime? DataExif(string f)
    {
        try
        {
            using (FileStream fs = new FileStream(f, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (Image img = Image.FromStream(fs, false, false))
            {
                foreach (int id in new int[] { 0x9003, 0x0132 })
                {
                    if (Array.IndexOf(img.PropertyIdList, id) < 0) continue;
                    string s = Encoding.ASCII.GetString(img.GetPropertyItem(id).Value).Trim('\0', ' ');
                    DateTime dt;
                    if (DateTime.TryParseExact(s, "yyyy:MM:dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out dt) && dt.Year > 1970)
                        return dt;
                }
            }
        }
        catch { }
        return null;
    }

    internal static string Miniatura(string full, int m) { return Miniatura(full, m, false); }
    internal static string Miniatura(string full, int m, bool qualsevol)
    {
        FileInfo fi = new FileInfo(full);
        string ext = fi.Extension.ToLowerInvariant();
        if (!qualsevol && Array.IndexOf(ExtImg, ext) < 0) return null;
        // Les fotos de Google Fotos ja venen dretes (Google les gira en fer-les petites); si encara porten l'etiqueta
        // de rotacio, girar-les un altre cop les deixa de costat. Aquestes no es giren.
        bool jaDreta = full.Replace('/', '\\').IndexOf("\\Google Fotos", StringComparison.OrdinalIgnoreCase) >= 0 || full.IndexOf("gfotos-cau", StringComparison.OrdinalIgnoreCase) >= 0;
        string clau = Hash(full.ToLowerInvariant() + "|" + fi.Length + "|" + fi.LastWriteTimeUtc.Ticks + "|" + m + (jaDreta ? "|dreta" : "") + "|r2");
        string desti = Path.Combine(Minis, clau.Substring(0, 2), clau + ".jpg");
        if (File.Exists(desti)) return desti;
        Sem.Wait();
        try
        {
            if (File.Exists(desti)) return desti;
            Directory.CreateDirectory(Path.GetDirectoryName(desti));
            using (FileStream fs = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (Image img = Image.FromStream(fs, true, false))
            {
                int ori = 1;
                if (!jaDreta && Array.IndexOf(img.PropertyIdList, 0x0112) >= 0)
                {
                    try { ori = BitConverter.ToUInt16(img.GetPropertyItem(0x0112).Value, 0); } catch { }
                }
                int w = img.Width, h = img.Height;
                double s = Math.Min(1.0, (double)m / Math.Max(w, h));
                int nw = Math.Max(1, (int)Math.Round(w * s)), nh = Math.Max(1, (int)Math.Round(h * s));
                using (Bitmap bmp = new Bitmap(nw, nh, PixelFormat.Format24bppRgb))
                {
                    using (Graphics g = Graphics.FromImage(bmp))
                    using (ImageAttributes ia = new ImageAttributes())
                    {
                        g.Clear(Color.White);
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                        g.CompositingQuality = CompositingQuality.HighQuality;
                        ia.SetWrapMode(WrapMode.TileFlipXY);
                        g.DrawImage(img, new Rectangle(0, 0, nw, nh), 0, 0, w, h, GraphicsUnit.Pixel, ia);
                    }
                    RotateFlipType rf = Rotacio(ori);
                    if (rf != RotateFlipType.RotateNoneFlipNone) bmp.RotateFlip(rf);
                    string tmp = desti + "." + Thread.CurrentThread.ManagedThreadId + ".tmp";
                    EncoderParameters ep = new EncoderParameters(1);
                    ep.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 82L);
                    bmp.Save(tmp, CodecJpeg(), ep);
                    if (File.Exists(desti)) File.Delete(tmp); else File.Move(tmp, desti);
                }
            }
        }
        catch (Exception e)
        {
            Console.WriteLine("  Miniatura: " + Path.GetFileName(full) + ": " + e.Message);
            return null;
        }
        finally { Sem.Release(); }
        return desti;
    }

    static ImageCodecInfo jpeg;
    static ImageCodecInfo CodecJpeg()
    {
        if (jpeg == null)
            foreach (ImageCodecInfo ci in ImageCodecInfo.GetImageEncoders())
                if (ci.MimeType == "image/jpeg") jpeg = ci;
        return jpeg;
    }

    static RotateFlipType Rotacio(int o)
    {
        switch (o)
        {
            case 2: return RotateFlipType.RotateNoneFlipX;
            case 3: return RotateFlipType.Rotate180FlipNone;
            case 4: return RotateFlipType.RotateNoneFlipY;
            case 5: return RotateFlipType.Rotate90FlipX;
            case 6: return RotateFlipType.Rotate90FlipNone;
            case 7: return RotateFlipType.Rotate270FlipX;
            case 8: return RotateFlipType.Rotate270FlipNone;
        }
        return RotateFlipType.RotateNoneFlipNone;
    }

    // ---------- Intermediari (radio, podcasts, temps) ----------
    internal static void Intermediari(HttpListenerContext ctx, string u, bool directe, bool continu = false)
    {
        HttpListenerResponse res = ctx.Response;
        if (string.IsNullOrEmpty(u) || !(u.StartsWith("http://") || u.StartsWith("https://"))) { Error(res, 400, "URL no valida"); return; }
        HttpWebRequest rq = (HttpWebRequest)WebRequest.Create(u);
        rq.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0 Safari/537.36";
        rq.AllowAutoRedirect = true;
        rq.MaximumAutomaticRedirections = 10;
        rq.Timeout = 20000;
        rq.ReadWriteTimeout = 60000;
        rq.Accept = "*/*";
        string rg = directe ? null : ctx.Request.Headers["Range"];
        if (!string.IsNullOrEmpty(rg) && rg.StartsWith("bytes="))
        {
            string[] pr = rg.Substring(6).Split(',')[0].Split('-');
            long a, b;
            if (long.TryParse(pr[0], out a))
            {
                if (pr.Length > 1 && long.TryParse(pr[1], out b)) rq.AddRange(a, b); else rq.AddRange(a);
            }
        }
        if (directe) { rq.ReadWriteTimeout = 30000; rq.KeepAlive = false; }
        HttpWebResponse rs;
        try { rs = (HttpWebResponse)rq.GetResponse(); }
        catch (WebException we)
        {
            rs = we.Response as HttpWebResponse;
            if (rs == null) { Error(res, 502, we.Message); return; }
        }
        using (rs)
        {
            res.StatusCode = (int)rs.StatusCode;
            string ct = rs.ContentType;
            if (string.IsNullOrEmpty(ct) || ct.Contains("octet-stream")) ct = TipusPerUrl(u, ct);
            if (ct != null && (ct.Contains("aacp") || ct.Contains("audio/x-aac"))) ct = "audio/aac";
            if (ct != null && ct.Contains("audio/x-mpeg")) ct = "audio/mpeg";
            if (directe && (res.StatusCode == 206 || res.StatusCode == 200)) res.StatusCode = 200;
            if (!string.IsNullOrEmpty(ct)) res.ContentType = ct;
            string cr = rs.Headers["Content-Range"];
            if (cr != null && !directe) res.AddHeader("Content-Range", cr);
            if (directe) res.AddHeader("Cache-Control", "no-cache, no-store");
            string ar = rs.Headers["Accept-Ranges"];
            if (ar != null && !continu) res.AddHeader("Accept-Ranges", ar);
            string ce = rs.Headers["Content-Encoding"];
            if (ce != null) res.AddHeader("Content-Encoding", ce);
            long len = rs.ContentLength;
            // una emissora en directe no te final: algunes (p. ex. Los 40 Classic) diuen una mida, el navegador la pren per
            // un fitxer, el reprodueix fins al final i torna a comencar. Aqui no se'n diu la mida i, si l'emissora talla,
            // el servidor s'hi torna a connectar sense que el reproductor se n'adoni.
            // (les teles fan servir la via d'abans, tal com ve: "tal=1")
            if (continu) res.SendChunked = true;
            else if (len >= 0) res.ContentLength64 = len; else res.SendChunked = true;
            if (ctx.Request.HttpMethod == "HEAD") return;
            if (continu && rs.StatusCode == HttpStatusCode.OK) { CopiaEnDirecte(rs.GetResponseStream(), res.OutputStream, u); return; }
            using (Stream s = rs.GetResponseStream()) Copiar(s, res.OutputStream, long.MaxValue);
        }
    }

    static void CopiaEnDirecte(Stream de, Stream a, string u)
    {
        byte[] buf = new byte[16384];
        int seguides = 0;
        DateTime darrera = DateTime.Now;
        HttpWebResponse act = null;
        try
        {
            while (true)
            {
                int r;
                try { r = de.Read(buf, 0, buf.Length); } catch { r = -1; }
                if (r > 0) { a.Write(buf, 0, r); continue; }   // si el reproductor ja no hi es, Write falla i s'acaba
                try { de.Close(); } catch { }
                try { if (act != null) act.Close(); } catch { }
                act = null;
                if ((DateTime.Now - darrera).TotalSeconds > 60) seguides = 0;
                darrera = DateTime.Now;
                while (act == null)
                {
                    if (++seguides > 8) return;
                    Thread.Sleep(seguides == 1 ? 200 : 1500);
                    try
                    {
                        HttpWebRequest rq = (HttpWebRequest)WebRequest.Create(u);
                        rq.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0 Safari/537.36";
                        rq.AllowAutoRedirect = true; rq.MaximumAutomaticRedirections = 10;
                        rq.Timeout = 15000; rq.ReadWriteTimeout = 30000; rq.KeepAlive = false; rq.Accept = "*/*";
                        act = (HttpWebResponse)rq.GetResponse();
                        if (act.StatusCode != HttpStatusCode.OK) { act.Close(); act = null; continue; }
                        de = act.GetResponseStream();
                    }
                    catch { act = null; }
                }
            }
        }
        finally { try { de.Close(); } catch { } try { if (act != null) act.Close(); } catch { } }
    }

    static string TipusPerUrl(string u, string per)
    {
        string cami2 = u.Split('?')[0].ToLowerInvariant();
        string ext = Path.GetExtension(cami2);
        string t = Tipus(ext);
        if (t == "application/octet-stream") return string.IsNullOrEmpty(per) ? t : per;
        return t;
    }

    // Permet emissores antigues (Shoutcast "ICY 200 OK")
    static void CapcaleresPermissives()
    {
        try
        {
            Assembly asm = typeof(HttpWebRequest).Assembly;
            Type t = asm.GetType("System.Net.Configuration.SettingsSectionInternal");
            object inst = t.GetProperty("Section", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null, new object[0]);
            FieldInfo f = t.GetField("useUnsafeHeaderParsing", BindingFlags.NonPublic | BindingFlags.Instance);
            f.SetValue(inst, true);
        }
        catch { }
    }

    // ---------- Fitxers ----------
    internal static void EnviarFitxer(HttpListenerContext ctx, string full)
    {
        HttpListenerResponse res = ctx.Response;
        FileInfo fi = new FileInfo(full);
        if (fi.Extension.ToLowerInvariant() == ".html" && fi.Length < 4000000)
        {
            // Els .js i .css de la pagina porten la versio a l'adreca: despres d'una actualitzacio cap aparell
            // no pot barrejar un index.html nou amb un comu.js vell que tenia guardat (la pantalla quedava buida).
            string html = File.ReadAllText(full, Encoding.UTF8);
            string v = Uri.EscapeDataString(Actualitza.Local());
            html = Regex.Replace(html, "(<script[^>]+src=\"|<link[^>]+href=\")([^\"?:#]+\\.(?:js|css))\"", "$1$2?v=" + v + "\"");
            Text(res, html, "text/html; charset=utf-8");
            return;
        }
        long len = fi.Length;
        string ct = Tipus(fi.Extension);
        res.ContentType = ct;
        res.AddHeader("Accept-Ranges", "bytes");
        res.AddHeader("Cache-Control", ct.StartsWith("text/") || ct.StartsWith("application/json") || ct.StartsWith("application/javascript") || ct.Contains("manifest") ? "no-cache" : "max-age=86400");
        long ini = 0, fi2 = len - 1;
        string rg = ctx.Request.Headers["Range"];
        if (!string.IsNullOrEmpty(rg) && rg.StartsWith("bytes=") && len > 0)
        {
            string[] pr = rg.Substring(6).Split(',')[0].Split('-');
            long a, b;
            if (pr[0] == "" && pr.Length > 1 && long.TryParse(pr[1], out b)) ini = Math.Max(0, len - b);
            else if (long.TryParse(pr[0], out a))
            {
                ini = a;
                if (pr.Length > 1 && long.TryParse(pr[1], out b)) fi2 = Math.Min(b, len - 1);
            }
            if (ini >= len || ini > fi2)
            {
                res.StatusCode = 416;
                res.AddHeader("Content-Range", "bytes */" + len);
                res.ContentLength64 = 0;
                return;
            }
            res.StatusCode = 206;
            res.AddHeader("Content-Range", "bytes " + ini + "-" + fi2 + "/" + len);
        }
        long n = len == 0 ? 0 : fi2 - ini + 1;
        res.ContentLength64 = n;
        if (ctx.Request.HttpMethod == "HEAD" || n == 0) return;
        using (FileStream fs = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 65536))
        {
            fs.Seek(ini, SeekOrigin.Begin);
            Copiar(fs, res.OutputStream, n);
        }
    }

    static void Copiar(Stream de, Stream a, long n)
    {
        byte[] buf = new byte[65536];
        while (n > 0)
        {
            int r = de.Read(buf, 0, (int)Math.Min(buf.Length, n));
            if (r <= 0) break;
            a.Write(buf, 0, r);
            n -= r;
        }
    }

    internal static string Tipus(string ext)
    {
        switch ((ext ?? "").ToLowerInvariant())
        {
            case ".html": case ".htm": return "text/html; charset=utf-8";
            case ".txt": return "text/plain; charset=utf-8";
            case ".css": return "text/css; charset=utf-8";
            case ".js": return "application/javascript; charset=utf-8";
            case ".json": return "application/json; charset=utf-8";
            case ".webmanifest": return "application/manifest+json; charset=utf-8";
            case ".xml": case ".rss": return "text/xml; charset=utf-8";
            case ".vtt": return "text/vtt; charset=utf-8";
            case ".srt": return "text/plain; charset=utf-8";
            case ".png": return "image/png";
            case ".jpg": case ".jpeg": return "image/jpeg";
            case ".gif": return "image/gif";
            case ".bmp": return "image/bmp";
            case ".svg": return "image/svg+xml";
            case ".ico": return "image/x-icon";
            case ".mp4": return "video/mp4";
            case ".m4v": return "video/x-m4v";
            case ".mov": return "video/quicktime";
            case ".mkv": return "video/x-matroska";
            case ".avi": return "video/x-msvideo";
            case ".mp3": return "audio/mpeg";
            case ".m4a": case ".m4b": return "audio/mp4";
            case ".aac": return "audio/aac";
            case ".wav": return "audio/wav";
            case ".aif": case ".aiff": return "audio/aiff";
            case ".flac": return "audio/flac";
            case ".ogg": case ".opus": return "audio/ogg";
            case ".m3u8": return "application/vnd.apple.mpegurl";
            case ".doc": return "application/msword";
            case ".docx": return "application/vnd.openxmlformats-officedocument.wordprocessingml.document";
            case ".pdf": return "application/pdf";
        }
        return "application/octet-stream";
    }

    // ---------- Utilitats ----------
    internal static string Ruta(string c, string rel)
    {
        string arrel;
        // Google Fotos sota demanda: la foto es baixa ara (i es guarda) quan algu la vol veure
        if (c == "fotos" && rel != null && rel.Replace('\\', '/').TrimStart('/').StartsWith(GFotos.PrefixNuvol + "/")) return GFotos.Local(rel.Replace('\\', '/').TrimStart('/'));
        if (c == null || !Arrels.TryGetValue(c, out arrel)) return null;
        return Segur(arrel, rel);
    }

    static string Segur(string arrel, string rel)
    {
        try
        {
            rel = (rel ?? "").Replace('/', '\\').TrimStart('\\');
            if (rel.Contains(":")) return null;
            string a = Path.GetFullPath(arrel).TrimEnd('\\') + "\\";
            string full = Path.GetFullPath(Path.Combine(a, rel));
            if (!(full.TrimEnd('\\') + "\\").StartsWith(a, StringComparison.OrdinalIgnoreCase)) return null;
            return full;
        }
        catch { return null; }
    }

    static Dictionary<string, string> Consulta(string qs)
    {
        Dictionary<string, string> d = new Dictionary<string, string>();
        foreach (string part in qs.Split('&'))
        {
            if (part.Length == 0) continue;
            int i = part.IndexOf('=');
            string k = i < 0 ? part : part.Substring(0, i);
            string v = i < 0 ? "" : part.Substring(i + 1);
            try { d[Uri.UnescapeDataString(k.Replace('+', ' '))] = Uri.UnescapeDataString(v.Replace('+', ' ')); } catch { }
        }
        return d;
    }

    // Funcions opcionals (Spotify, WhatsApp): desactivades fins que s'activen a la configuracio
    internal static bool FuncioActiva(string nom)
    {
        try
        {
            string f = Path.Combine(Dades, "ajustos.json");
            if (!File.Exists(f)) return false;
            Dictionary<string, object> d = new JavaScriptSerializer().DeserializeObject(File.ReadAllText(f, Encoding.UTF8)) as Dictionary<string, object>;
            object v;
            return d != null && d.TryGetValue(nom, out v) && v is bool && (bool)v;
        }
        catch { return false; }
    }

    internal static string Q(Dictionary<string, string> q, string k)
    {
        string v;
        return q.TryGetValue(k, out v) ? v : "";
    }

    static string NomNet(string n)
    {
        n = Path.GetFileName((n ?? "").Replace('/', '\\'));
        foreach (char ch in Path.GetInvalidFileNameChars()) n = n.Replace(ch.ToString(), "");
        return n.Trim();
    }

    static string NoRepetit(string f)
    {
        if (!File.Exists(f)) return f;
        string dir = Path.GetDirectoryName(f), b = Path.GetFileNameWithoutExtension(f), e = Path.GetExtension(f);
        for (int i = 1; ; i++)
        {
            string n = Path.Combine(dir, b + " (" + i + ")" + e);
            if (!File.Exists(n)) return n;
        }
    }

    static long Ms(DateTime utc) { return (long)(utc.ToUniversalTime() - Epoca).TotalMilliseconds; }

    internal static string Hash(string s)
    {
        using (SHA1 sha = SHA1.Create())
        {
            byte[] h = sha.ComputeHash(Encoding.UTF8.GetBytes(s));
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < 10; i++) sb.Append(h[i].ToString("x2"));
            return sb.ToString();
        }
    }

    static string LlegirText(string f)
    {
        byte[] b = File.ReadAllBytes(f);
        try { return new UTF8Encoding(false, true).GetString(b).TrimStart('\uFEFF'); }
        catch { return Encoding.GetEncoding(1252).GetString(b); }
    }

    static string SrtAVtt(string s)
    {
        if (s.StartsWith("WEBVTT")) return s;
        s = s.Replace("\r\n", "\n").Replace("\r", "\n");
        s = Regex.Replace(s, @"(\d\d:\d\d:\d\d),(\d\d\d)", "$1.$2");
        return "WEBVTT\n\n" + s;
    }

    internal static string ToJson(object o)
    {
        JavaScriptSerializer js = new JavaScriptSerializer();
        js.MaxJsonLength = int.MaxValue;
        return js.Serialize(o);
    }

    internal static void Json(HttpListenerResponse r, object o) { Text(r, ToJson(o), "application/json; charset=utf-8"); }

    internal static void Text(HttpListenerResponse r, string text, string ct)
    {
        byte[] b = Encoding.UTF8.GetBytes(text);
        r.ContentType = ct;
        r.AddHeader("Cache-Control", "no-cache");
        r.ContentLength64 = b.Length;
        r.OutputStream.Write(b, 0, b.Length);
    }

    internal static void Error(HttpListenerResponse r, int codi, string msg)
    {
        r.StatusCode = codi;
        Dictionary<string, object> d = new Dictionary<string, object>();
        d["error"] = msg;
        Json(r, d);
    }
}
