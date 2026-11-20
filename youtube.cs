// YouTube amb yt-dlp (programa gratuit): cerca i reproduccio a l'iPad sense anuncis de la web.
// 360p es immediat (un sol fitxer mp4); 720p es baixa i s'ajunta amb ffmpeg abans de mirar-lo. C# 5.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;

public static class YouTube
{
    static readonly object lk = new object();
    static readonly Dictionary<string, KeyValuePair<DateTime, string>> cau = new Dictionary<string, KeyValuePair<DateTime, string>>();
    static readonly Dictionary<string, string> baixades = new Dictionary<string, string>();   // id -> estat ("12%", "fet", "error: ...")
    static DateTime actualitzat = DateTime.MinValue;

    static string Dir() { string d = Path.Combine(CasaServidor.DirDades, "youtube"); Directory.CreateDirectory(d); return d; }

    public static string Ruta()
    {
        foreach (string f in new string[] { Path.Combine(CasaServidor.DirArrel, "yt-dlp.exe"), Path.Combine(CasaServidor.DirArrel, @"eines\yt-dlp.exe"), Path.Combine(CasaServidor.DirDades, "yt-dlp.exe") })
            if (File.Exists(f)) return f;
        foreach (string d in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';'))
        {
            try { string f = Path.Combine(d.Trim(), "yt-dlp.exe"); if (File.Exists(f)) return f; } catch { }
        }
        return null;
    }

    // yt-dlp necessita Deno (un motor de JavaScript) per resoldre els reptes de YouTube des del nov. 2025
    public static string RutaDeno()
    {
        foreach (string f in new string[] { Path.Combine(CasaServidor.DirArrel, @"eines\deno.exe"), Path.Combine(CasaServidor.DirArrel, "deno.exe"), Path.Combine(CasaServidor.DirDades, "deno.exe") })
            if (File.Exists(f)) return f;
        foreach (string d in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';'))
        {
            try { string f = Path.Combine(d.Trim(), "deno.exe"); if (File.Exists(f)) return f; } catch { }
        }
        string u = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), @".deno\bin\deno.exe");
        return File.Exists(u) ? u : null;
    }
    static int instalantDeno = 0;
    static string errorDeno = "";
    static void InstallaDeno()
    {
        string eines = Path.Combine(CasaServidor.DirArrel, "eines");
        Directory.CreateDirectory(eines);
        string zip = Path.Combine(eines, "deno.zip");
        using (WebClient wc = new WebClient()) { wc.Headers["User-Agent"] = "Hearth"; wc.DownloadFile("https://github.com/denoland/deno/releases/latest/download/deno-x86_64-pc-windows-msvc.zip", zip); }
        ProcessStartInfo si = new ProcessStartInfo("powershell.exe", "-NoProfile -ExecutionPolicy Bypass -Command \"Expand-Archive -LiteralPath '" + zip + "' -DestinationPath '" + eines + "' -Force\"");
        si.UseShellExecute = false; si.CreateNoWindow = true;
        using (Process p = Process.Start(si)) p.WaitForExit(120000);
        try { File.Delete(zip); } catch { }
        if (!File.Exists(Path.Combine(eines, "deno.exe"))) throw new Exception("No s'ha pogut descomprimir deno.exe");
    }
    // Si falta Deno, l'instal-la en segon pla (una vegada)
    static void AsseguraDeno()
    {
        if (RutaDeno() != null) return;
        if (Interlocked.CompareExchange(ref instalantDeno, 1, 0) != 0) return;
        ThreadPool.QueueUserWorkItem(delegate
        {
            try { Console.WriteLine("  YouTube: instal-lant Deno..."); InstallaDeno(); errorDeno = ""; Console.WriteLine("  YouTube: Deno instal-lat."); }
            catch (Exception e) { errorDeno = e.Message; Console.WriteLine("  YouTube: no s'ha pogut instal-lar Deno: " + e.Message); }
            finally { instalantDeno = 0; }
        });
    }

    // Executa yt-dlp i torna la sortida
    static string Executa(string args, int ms)
    {
        string exe = Ruta();
        if (exe == null) throw new Exception("Falta yt-dlp.exe (Configuraci\u00f3 \u25b8 YouTube \u25b8 Descarregar)");
        ProcessStartInfo si = new ProcessStartInfo(exe, args);
        si.UseShellExecute = false; si.CreateNoWindow = true;
        si.RedirectStandardOutput = true; si.RedirectStandardError = true;
        si.StandardOutputEncoding = Encoding.UTF8;
        string ff = SoPC.RutaFfmpeg(), deno = RutaDeno(), cami = Environment.GetEnvironmentVariable("PATH");
        if (ff != null) cami = Path.GetDirectoryName(ff) + ";" + cami;
        if (deno != null) cami = Path.GetDirectoryName(deno) + ";" + cami;
        si.EnvironmentVariables["PATH"] = cami;
        using (Process p = Process.Start(si))
        {
            StringBuilder err = new StringBuilder();
            p.ErrorDataReceived += delegate (object s, DataReceivedEventArgs e) { if (e.Data != null) lock (err) err.AppendLine(e.Data); };
            p.BeginErrorReadLine();
            string sortida = p.StandardOutput.ReadToEnd();
            if (!p.WaitForExit(ms)) { try { p.Kill(); } catch { } throw new Exception("yt-dlp no respon"); }
            if (p.ExitCode != 0 && sortida.Trim() == "")
            {
                string e = err.ToString();
                Match m = Regex.Match(e, "ERROR:[^\r\n]*");
                string pista = RutaDeno() == null ? " (falta Deno: Configuraci\u00f3 \u25b8 YouTube \u25b8 Descarregar)" : "";
                throw new Exception((m.Success ? m.Value : "yt-dlp ha fallat") + pista);
            }
            return sortida;
        }
    }

    static string AmbCau(string clau, int minuts, Func<string> fes)
    {
        lock (lk)
        {
            KeyValuePair<DateTime, string> c;
            if (cau.TryGetValue(clau, out c) && (DateTime.Now - c.Key).TotalMinutes < minuts) return c.Value;
        }
        string r = fes();
        lock (lk) cau[clau] = new KeyValuePair<DateTime, string>(DateTime.Now, r);
        return r;
    }

    static string S(Dictionary<string, object> d, string k) { object v; return d != null && d.TryGetValue(k, out v) && v != null ? v.ToString() : ""; }

    // Llista plana (cerca, canal, llista de reproduccio)
    static string Llista(string objectiu, int n)
    {
        string json = Executa("--flat-playlist -J --no-warnings --playlist-end " + n + " " + Cometes(objectiu), 60000);
        JavaScriptSerializer js = new JavaScriptSerializer(); js.MaxJsonLength = int.MaxValue;
        Dictionary<string, object> d = js.DeserializeObject(json) as Dictionary<string, object>;
        List<object> r = new List<object>();
        object[] es = d != null && d.ContainsKey("entries") ? d["entries"] as object[] : null;
        if (es != null)
            foreach (object o in es)
            {
                Dictionary<string, object> e = o as Dictionary<string, object>;
                string id = S(e, "id");
                if (id == "" || id.Length != 11) continue;
                Dictionary<string, object> x = new Dictionary<string, object>();
                x["id"] = id;
                x["t"] = S(e, "title");
                x["c"] = S(e, "channel") != "" ? S(e, "channel") : S(e, "uploader");
                double dur; double.TryParse(S(e, "duration"), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out dur);
                x["d"] = (int)dur;
                x["v"] = S(e, "view_count");
                x["img"] = "https://i.ytimg.com/vi/" + id + "/mqdefault.jpg";
                x["directe"] = S(e, "live_status") == "is_live";
                r.Add(x);
            }
        return js.Serialize(r);
    }

    static string Cometes(string s) { return "\"" + s.Replace("\"", "\\\"") + "\""; }
    static bool IdValid(string id) { return id != null && Regex.IsMatch(id, "^[A-Za-z0-9_-]{11}$"); }

    // URL directa d'un mp4 que l'iPad pot reproduir (360p, video i so junts)
    static string UrlDirecta(string id)
    {
        return AmbCau("u:" + id, 120, delegate
        {
            string s = Executa("-f \"18/best[ext=mp4][vcodec^=avc1][acodec^=mp4a]/best[ext=mp4]\" -g --no-warnings " + Cometes("https://www.youtube.com/watch?v=" + id), 60000);
            string u = s.Trim().Split('\n')[0].Trim();
            if (!u.StartsWith("http")) throw new Exception("No he trobat cap format compatible");
            return u;
        });
    }

    static string FitxerHd(string id) { return Path.Combine(Dir(), id + "-720.mp4"); }

    // Baixa 720p (H.264 + AAC, el que l'iPad pot reproduir) i ho ajunta amb ffmpeg
    static void BaixaHd(string id)
    {
        lock (lk) { string e; if (baixades.TryGetValue(id, out e) && (e.EndsWith("%") || e == "fet")) return; baixades[id] = "0%"; }
        ThreadPool.QueueUserWorkItem(delegate
        {
            try
            {
                Neteja();
                string exe = Ruta();
                if (exe == null) throw new Exception("Falta yt-dlp.exe");
                if (SoPC.RutaFfmpeg() == null) throw new Exception("Falta ffmpeg.exe per ajuntar el video i el so");
                string sortida = FitxerHd(id);
                ProcessStartInfo si = new ProcessStartInfo(exe, "-f \"bv*[height<=720][vcodec^=avc1]+ba[ext=m4a]/b[height<=720][ext=mp4]/18\" --merge-output-format mp4 --newline --no-warnings --no-part -o " + Cometes(sortida + ".tmp.mp4") + " --ffmpeg-location " + Cometes(SoPC.RutaFfmpeg()) + " " + Cometes("https://www.youtube.com/watch?v=" + id));
                si.UseShellExecute = false; si.CreateNoWindow = true; si.RedirectStandardOutput = true; si.RedirectStandardError = true;
                using (Process p = Process.Start(si))
                {
                    p.ErrorDataReceived += delegate { };
                    p.BeginErrorReadLine();
                    string l;
                    int part = 0; double darrer = 0;
                    while ((l = p.StandardOutput.ReadLine()) != null)
                    {
                        Match m = Regex.Match(l, @"\[download\]\s+([\d.]+)%");
                        if (m.Success)
                        {
                            double pc = double.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
                            if (pc < darrer - 50) part++;
                            darrer = pc;
                            int total = (int)(part == 0 ? pc * 0.85 : 85 + pc * 0.13);
                            lock (lk) baixades[id] = Math.Min(98, total) + "%";
                        }
                        else if (l.Contains("[Merger]")) lock (lk) baixades[id] = "99%";
                    }
                    p.WaitForExit();
                }
                // Mou el moov al principi (perque l'iPad comenci a reproduir de seguida)
                string tmp = sortida + ".tmp.mp4";
                if (!File.Exists(tmp)) throw new Exception("La baixada no ha acabat be");
                string ff = SoPC.RutaFfmpeg();
                ProcessStartInfo fs = new ProcessStartInfo(ff, "-y -v error -i " + Cometes(tmp) + " -c copy -movflags +faststart " + Cometes(sortida));
                fs.UseShellExecute = false; fs.CreateNoWindow = true;
                using (Process p = Process.Start(fs)) p.WaitForExit(300000);
                if (File.Exists(sortida) && new FileInfo(sortida).Length > 0) File.Delete(tmp); else File.Move(tmp, sortida);
                lock (lk) baixades[id] = "fet";
            }
            catch (Exception e) { lock (lk) baixades[id] = "error: " + e.Message; }
        });
    }

    // Esborra les baixades velles (mes de 3 dies) perque no omplin el disc
    static void Neteja()
    {
        try { foreach (FileInfo f in new DirectoryInfo(Dir()).GetFiles("*.mp4")) if ((DateTime.Now - f.LastAccessTime).TotalDays > 3 && (DateTime.Now - f.LastWriteTime).TotalDays > 3) f.Delete(); } catch { }
    }

    static void ActualitzaYtDlp()
    {
        if ((DateTime.Now - actualitzat).TotalDays < 3) return;
        actualitzat = DateTime.Now;
        ThreadPool.QueueUserWorkItem(delegate { try { Executa("-U", 120000); } catch { } });
    }

    // ---------- Reproduccio en HLS: yt-dlp troba els fluxos i ffmpeg els ajunta en trossos .ts ----------
    // Es el format que l'iPad (fins i tot amb iOS 9) reprodueix millor, comenca en pocs segons i permet 720p.
    class Sessio { public Process P; public string Dir; public DateTime Darrer; public string Error; }
    static readonly Dictionary<string, Sessio> sessions = new Dictionary<string, Sessio>();
    static System.Threading.Timer netejaHls;

    // googlevideo vol el mateix User-Agent que ha fet servir yt-dlp
    static string Capcaleres(Dictionary<string, object> f)
    {
        Dictionary<string, object> h = f != null && f.ContainsKey("http_headers") ? f["http_headers"] as Dictionary<string, object> : null;
        string ua = h != null ? S(h, "User-Agent") : "";
        return ua != "" ? "-user_agent " + Cometes(ua) + " " : "";
    }

    static Sessio IniciaHls(string id, string qualitat)
    {
        string clau = id + "-" + qualitat;
        lock (lk)
        {
            if (netejaHls == null) netejaHls = new System.Threading.Timer(delegate { NetejaHls(); }, null, 60000, 60000);
            Sessio s;
            if (sessions.TryGetValue(clau, out s) && (s.Error == null)) { s.Darrer = DateTime.Now; return s; }
            s = new Sessio { Dir = Path.Combine(Path.Combine(Dir(), "hls"), clau), Darrer = DateTime.Now };
            sessions[clau] = s;
            try { if (Directory.Exists(s.Dir)) Directory.Delete(s.Dir, true); } catch { }
            Directory.CreateDirectory(s.Dir);
            Sessio ses = s;
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    string ff = SoPC.RutaFfmpeg();
                    if (ff == null) throw new Exception("Falta ffmpeg.exe");
                    string h = qualitat == "360" ? "360" : "720";
                    string json = Executa("-j --no-warnings --no-playlist -f \"bv*[height<=" + h + "][vcodec^=avc1]+ba[ext=m4a]/b[height<=" + h + "][ext=mp4]/18/b\" " + Cometes("https://www.youtube.com/watch?v=" + id), 90000);
                    JavaScriptSerializer js = new JavaScriptSerializer(); js.MaxJsonLength = int.MaxValue;
                    Dictionary<string, object> d = js.DeserializeObject(json.Trim().Split('\n')[0]) as Dictionary<string, object>;
                    if (d == null) throw new Exception("yt-dlp no ha tornat res");
                    List<Dictionary<string, object>> fs = new List<Dictionary<string, object>>();
                    object[] rf = d.ContainsKey("requested_formats") ? d["requested_formats"] as object[] : null;
                    if (rf != null) foreach (object o in rf) fs.Add(o as Dictionary<string, object>);
                    else fs.Add(d);
                    StringBuilder a = new StringBuilder("-nostdin -loglevel error ");
                    foreach (Dictionary<string, object> f in fs) a.Append(Capcaleres(f)).Append("-i ").Append(Cometes(S(f, "url"))).Append(" ");
                    if (fs.Count > 1) a.Append("-map 0:v:0 -map 1:a:0 ");
                    a.Append("-c:v copy -c:a aac -b:a 128k -ac 2 -f hls -hls_time 4 -hls_list_size 0 -hls_playlist_type event ");
                    a.Append("-hls_segment_filename ").Append(Cometes(Path.Combine(ses.Dir, "s%05d.ts"))).Append(" ").Append(Cometes(Path.Combine(ses.Dir, "index.m3u8")));
                    ProcessStartInfo si = new ProcessStartInfo(ff, a.ToString());
                    si.UseShellExecute = false; si.CreateNoWindow = true; si.RedirectStandardError = true;
                    Process p = Process.Start(si);
                    StringBuilder err = new StringBuilder();
                    p.ErrorDataReceived += delegate (object o2, DataReceivedEventArgs e2) { if (e2.Data != null) lock (err) { if (err.Length < 2000) err.AppendLine(e2.Data); } };
                    p.BeginErrorReadLine();
                    ses.P = p;
                    p.WaitForExit();
                    if (p.ExitCode != 0 && !File.Exists(Path.Combine(ses.Dir, "s00001.ts"))) ses.Error = "ffmpeg: " + err.ToString().Trim();
                }
                catch (Exception e) { ses.Error = e.Message; }
            });
            return s;
        }
    }

    static void NetejaHls()
    {
        List<Sessio> velles = new List<Sessio>();
        lock (lk)
        {
            List<string> treure = new List<string>();
            foreach (KeyValuePair<string, Sessio> kv in sessions) if ((DateTime.Now - kv.Value.Darrer).TotalMinutes > 15) { treure.Add(kv.Key); velles.Add(kv.Value); }
            foreach (string k in treure) sessions.Remove(k);
        }
        foreach (Sessio s in velles)
        {
            try { if (s.P != null && !s.P.HasExited) s.P.Kill(); } catch { }
            try { if (s.P != null) s.P.WaitForExit(3000); Directory.Delete(s.Dir, true); } catch { }
        }
    }

    public static void Api(string nom, Dictionary<string, string> q, HttpListenerContext ctx)
    {
        HttpListenerResponse res = ctx.Response;
        try
        {
            switch (nom)
            {
                case "cerca":
                    {
                        string t = CasaServidor.Q(q, "q").Trim();
                        if (t == "") { CasaServidor.Text(res, "[]", "application/json; charset=utf-8"); return; }
                        ActualitzaYtDlp();
                        AsseguraDeno();
                        CasaServidor.Text(res, AmbCau("c:" + t.ToLowerInvariant(), 60, delegate { return Llista("ytsearch24:" + t, 24); }), "application/json; charset=utf-8");
                        return;
                    }
                case "canal":
                    {
                        string u = CasaServidor.Q(q, "u");
                        if (!Regex.IsMatch(u, @"^https://(www\.|m\.)?youtube\.com/")) { CasaServidor.Error(res, 400, "Enlla\u00e7 no v\u00e0lid"); return; }
                        if (!u.Contains("/videos") && Regex.IsMatch(u, @"/(@[^/]+|channel/[^/]+|c/[^/]+)/?$")) u = u.TrimEnd('/') + "/videos";
                        CasaServidor.Text(res, AmbCau("l:" + u, 30, delegate { return Llista(u, 30); }), "application/json; charset=utf-8");
                        return;
                    }
                case "hls":
                    {
                        string id = CasaServidor.Q(q, "id"), qu = CasaServidor.Q(q, "q") == "360" ? "360" : "720";
                        if (!IdValid(id)) { CasaServidor.Error(res, 400, "Id"); return; }
                        AsseguraDeno();
                        Sessio s = IniciaHls(id, qu);
                        string llista = Path.Combine(s.Dir, "index.m3u8");
                        for (int i = 0; i < 240; i++)
                        {
                            if (s.Error != null) { string e = s.Error; lock (lk) sessions.Remove(id + "-" + qu); CasaServidor.Error(res, 502, e); return; }
                            if (File.Exists(llista)) { string t0 = ""; try { t0 = File.ReadAllText(llista); } catch { } if (Regex.Matches(t0, @"\.ts").Count >= 2 || t0.Contains("#EXT-X-ENDLIST")) break; }
                            Thread.Sleep(250);
                        }
                        string t;
                        try { t = File.ReadAllText(llista); } catch { CasaServidor.Error(res, 504, "El v\u00eddeo triga massa a comen\u00e7ar"); return; }
                        string k = id + "-" + qu;
                        t = Regex.Replace(t, @"^(s\d+\.ts)\s*$", delegate (Match m) { return "/api/youtube/seg?k=" + k + "&f=" + m.Groups[1].Value; }, RegexOptions.Multiline);
                        res.AddHeader("Cache-Control", "no-cache");
                        CasaServidor.Text(res, t, "application/vnd.apple.mpegurl");
                        return;
                    }
                case "seg":
                    {
                        string k = CasaServidor.Q(q, "k"), f = CasaServidor.Q(q, "f");
                        if (!Regex.IsMatch(k, "^[A-Za-z0-9_-]{11}-(360|720)$") || !Regex.IsMatch(f, @"^s\d+\.ts$")) { CasaServidor.Error(res, 400, "Nom"); return; }
                        lock (lk) { Sessio s; if (sessions.TryGetValue(k, out s)) s.Darrer = DateTime.Now; }
                        string ruta = Path.Combine(Path.Combine(Path.Combine(Dir(), "hls"), k), f);
                        if (!File.Exists(ruta)) { CasaServidor.Error(res, 404, "Tros"); return; }
                        byte[] b;
                        try { b = File.ReadAllBytes(ruta); } catch { CasaServidor.Error(res, 404, "Tros"); return; }
                        res.ContentType = "video/mp2t";
                        res.ContentLength64 = b.Length;
                        res.OutputStream.Write(b, 0, b.Length);
                        res.Close();
                        return;
                    }
                case "video":
                    {
                        string id = CasaServidor.Q(q, "id");
                        if (!IdValid(id)) { CasaServidor.Error(res, 400, "Id"); return; }
                        string hd = FitxerHd(id);
                        if (CasaServidor.Q(q, "q") == "720" && File.Exists(hd)) { File.SetLastAccessTime(hd, DateTime.Now); CasaServidor.EnviarFitxer(ctx, hd); return; }
                        string u;
                        try { u = UrlDirecta(id); }
                        catch { lock (lk) cau.Remove("u:" + id); u = UrlDirecta(id); }
                        CasaServidor.Intermediari(ctx, u, false);
                        return;
                    }
                case "hd":
                    {
                        string id = CasaServidor.Q(q, "id");
                        if (!IdValid(id)) { CasaServidor.Error(res, 400, "Id"); return; }
                        if (File.Exists(FitxerHd(id))) lock (lk) baixades[id] = "fet";
                        else BaixaHd(id);
                        string e; lock (lk) baixades.TryGetValue(id, out e);
                        Dictionary<string, object> o = new Dictionary<string, object>();
                        o["estat"] = e ?? "";
                        CasaServidor.Json(res, o);
                        return;
                    }
                case "info":
                    {
                        Dictionary<string, object> o = new Dictionary<string, object>();
                        o["ytdlp"] = Ruta() != null; o["ffmpeg"] = SoPC.RutaFfmpeg() != null; o["deno"] = RutaDeno() != null; o["instalantDeno"] = instalantDeno == 1; o["errorDeno"] = errorDeno;
                        try { o["versio"] = Ruta() != null ? Executa("--version", 20000).Trim() : ""; } catch { o["versio"] = ""; }
                        CasaServidor.Json(res, o);
                        return;
                    }
                case "instal-la":
                    {
                        // Baixa yt-dlp.exe de GitHub a la carpeta del servidor
                        string desti = Path.Combine(CasaServidor.DirArrel, "yt-dlp.exe");
                        using (WebClient wc = new WebClient()) { wc.Headers["User-Agent"] = "Hearth"; wc.DownloadFile("https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp.exe", desti + ".tmp"); }
                        if (File.Exists(desti)) File.Delete(desti);
                        File.Move(desti + ".tmp", desti);
                        lock (lk) cau.Clear();
                        if (RutaDeno() == null) InstallaDeno();
                        CasaServidor.Text(res, "{\"ok\":true}", "application/json; charset=utf-8");
                        return;
                    }
            }
            CasaServidor.Error(res, 404, "API desconeguda");
        }
        catch (Exception e) { try { CasaServidor.Error(res, 500, e.Message); } catch { } }
    }
}
