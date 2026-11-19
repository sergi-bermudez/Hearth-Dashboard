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

    // Executa yt-dlp i torna la sortida
    static string Executa(string args, int ms)
    {
        string exe = Ruta();
        if (exe == null) throw new Exception("Falta yt-dlp.exe (Configuraci\u00f3 \u25b8 YouTube \u25b8 Descarregar)");
        ProcessStartInfo si = new ProcessStartInfo(exe, args);
        si.UseShellExecute = false; si.CreateNoWindow = true;
        si.RedirectStandardOutput = true; si.RedirectStandardError = true;
        si.StandardOutputEncoding = Encoding.UTF8;
        string ff = SoPC.RutaFfmpeg();
        if (ff != null) si.EnvironmentVariables["PATH"] = Path.GetDirectoryName(ff) + ";" + Environment.GetEnvironmentVariable("PATH");
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
                throw new Exception(m.Success ? m.Value : "yt-dlp ha fallat");
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
                        o["ytdlp"] = Ruta() != null; o["ffmpeg"] = SoPC.RutaFfmpeg() != null;
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
                        CasaServidor.Text(res, "{\"ok\":true}", "application/json; charset=utf-8");
                        return;
                    }
            }
            CasaServidor.Error(res, 404, "API desconeguda");
        }
        catch (Exception e) { try { CasaServidor.Error(res, 500, e.Message); } catch { } }
    }
}
