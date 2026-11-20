// So "a trossos" per a l'iPad: el PC converteix la radio o el so de l'Spotify a trossos
// d'1 segon en WAV, i la tauleta els va demanant i els reprodueix amb Web Audio.
// Aixi no depenem de com Safari tracta els streams en directe (i el volum es pot canviar). C# 5.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Threading;

public static class Pcm
{
    const int Rate = 32000, Canals = 2, Tros = Rate * Canals * 2;   // 1 s de so
    const int Anell = 20;

    class Font
    {
        public string Id;
        public Process P;
        public readonly byte[][] Trossos = new byte[Anell][];
        public long Seq = -1;          // darrer tros complet
        public DateTime Vist = DateTime.Now;
        public string Error = "";
        public readonly object Lk = new object();
        public bool Viu = true;
        public bool Spotify;
        public bool Fitxer;           // fitxer o podcast: s'acaba (no es directe)
        public double Durada;
        public int Nivell;            // pic del darrer tros (0-100), per saber si arriba silenci
    }

    static readonly Dictionary<string, Font> fonts = new Dictionary<string, Font>();
    static readonly object lk = new object();
    static Timer neteja;

    static string Args(string font, string u, double ss)
    {
        string sortida = " -vn -ac " + Canals + " -ar " + Rate + " -f s16le pipe:1";
        if (font == "spotify")
        {
            string disp = Spotify.Ajuda("dispositiu", "CABLE Output (VB-Audio Virtual Cable)");
            return "-hide_banner -loglevel error -nostdin -f dshow -audio_buffer_size 50 -i audio=\"" + disp.Replace("\"", "") + "\"" + sortida;
        }
        if (font == "fitxer")
        {
            string ini = ss > 0 ? "-ss " + ss.ToString(System.Globalization.CultureInfo.InvariantCulture) + " " : "";
            string xa = u.StartsWith("http") ? "-reconnect 1 -reconnect_streamed 1 -reconnect_delay_max 5 -user_agent \"Mozilla/5.0 Hearth\" " : "";
            return "-hide_banner -loglevel error -nostdin -re " + xa + ini + "-i \"" + u.Replace("\"", "%22") + "\"" + sortida;
        }
        string extra = u.StartsWith("http") ? "-reconnect 1 -reconnect_streamed 1 -reconnect_delay_max 5 -rw_timeout 15000000 -user_agent \"Mozilla/5.0 (Windows NT 10.0; Win64; x64) Hearth\" " : "";
        return "-hide_banner -loglevel error -nostdin " + extra + "-i \"" + u.Replace("\"", "%22") + "\"" + sortida;
    }

    static double Durada(string full)
    {
        try
        {
            ProcessStartInfo si = new ProcessStartInfo(SoPC.RutaFfmpeg(), "-hide_banner -nostdin -i \"" + full + "\"");
            si.UseShellExecute = false; si.CreateNoWindow = true; si.RedirectStandardError = true;
            using (Process p = Process.Start(si))
            {
                string e = p.StandardError.ReadToEnd(); p.WaitForExit(5000);
                System.Text.RegularExpressions.Match m = System.Text.RegularExpressions.Regex.Match(e, @"Duration:\s*(\d+):(\d+):(\d+(?:\.\d+)?)");
                if (m.Success) return int.Parse(m.Groups[1].Value) * 3600 + int.Parse(m.Groups[2].Value) * 60 + double.Parse(m.Groups[3].Value, System.Globalization.CultureInfo.InvariantCulture);
            }
        }
        catch { }
        return 0;
    }

    // Pic del so de l'Spotify que arriba (-1 si no s'escolta)
    public static int NivellSpotify()
    {
        lock (lk) foreach (Font f in fonts.Values) if (f.Spotify && f.Viu) return f.Nivell;
        return -1;
    }

    public static bool SpotifyViu()
    {
        lock (lk) foreach (Font f in fonts.Values) if (f.Spotify && f.Viu) return true;
        return false;
    }

    static Font Obre(string font, string u, double ss)
    {
        string id = CasaServidor.Hash(font + "|" + u + "|" + ss).Substring(0, 12);
        if (font == "spotify")
        {
            bool viu;
            lock (lk) { Font x; viu = fonts.TryGetValue(id, out x) && x.Viu && x.P != null && !x.P.HasExited; }
            if (!viu && SoPC.AlliberaSiLliure()) Thread.Sleep(800);   // el dispositiu de so nomes el pot fer servir un ffmpeg
            if (!viu) ThreadPool.QueueUserWorkItem(delegate { Spotify.Sortida(); });   // l'Edge ha de sonar pel cable
        }
        lock (lk)
        {
            Font f;
            if (fonts.TryGetValue(id, out f) && f.Viu && f.P != null && !f.P.HasExited) { f.Vist = DateTime.Now; return f; }
            string exe = SoPC.RutaFfmpeg();
            if (exe == null) throw new Exception("Falta ffmpeg");
            f = new Font(); f.Id = id; f.Spotify = font == "spotify"; f.Fitxer = font == "fitxer";
            ProcessStartInfo si = new ProcessStartInfo(exe, Args(font, u, ss));
            si.UseShellExecute = false; si.CreateNoWindow = true; si.RedirectStandardOutput = true; si.RedirectStandardError = true;
            f.P = Process.Start(si);
            fonts[id] = f;
            Font ff = f;
            Thread te = new Thread(delegate () { try { string l; while ((l = ff.P.StandardError.ReadLine()) != null) { ff.Error = l; Console.WriteLine("  So (" + font + "): " + l); } } catch { } });
            te.IsBackground = true; te.Start();
            Thread tl = new Thread(delegate ()
            {
                Stream s = ff.P.StandardOutput.BaseStream;
                try
                {
                    while (ff.Viu)
                    {
                        byte[] b = new byte[Tros];
                        int fet = 0;
                        while (fet < Tros) { int n = s.Read(b, fet, Tros - fet); if (n <= 0) break; fet += n; }
                        if (fet < 400) break;
                        if (fet < Tros) { fet -= fet % 4; byte[] c = new byte[fet]; Buffer.BlockCopy(b, 0, c, 0, fet); b = c; }
                        int pic = 0;
                        for (int i = 0; i + 1 < b.Length; i += 32) { int v = (short)(b[i] | (b[i + 1] << 8)); if (v < 0) v = -v; if (v > pic) pic = v; }
                        ff.Nivell = pic * 100 / 32768;
                        lock (ff.Lk) { ff.Seq++; ff.Trossos[ff.Seq % Anell] = b; Monitor.PulseAll(ff.Lk); }
                        if (fet < Tros) break;
                    }
                }
                catch { }
                ff.Viu = false;
                lock (ff.Lk) Monitor.PulseAll(ff.Lk);
            });
            tl.IsBackground = true; tl.Start();
            if (neteja == null) neteja = new Timer(delegate { Neteja(); }, null, 10000, 10000);
            return f;
        }
    }

    // Atura les fonts que ningu escolta fa 20 s
    static void Neteja()
    {
        List<Font> fora = new List<Font>();
        lock (lk)
        {
            foreach (Font f in fonts.Values) if ((DateTime.Now - f.Vist).TotalSeconds > 20 || (!f.Viu && !f.Fitxer)) fora.Add(f);
            foreach (Font f in fora) fonts.Remove(f.Id);
        }
        foreach (Font f in fora) { f.Viu = false; try { if (!f.P.HasExited) f.P.Kill(); } catch { } }
    }

    static byte[] Wav(byte[] pcm)
    {
        MemoryStream m = new MemoryStream(44 + pcm.Length);
        BinaryWriter w = new BinaryWriter(m);
        w.Write(new char[] { 'R', 'I', 'F', 'F' }); w.Write(36 + pcm.Length); w.Write(new char[] { 'W', 'A', 'V', 'E' });
        w.Write(new char[] { 'f', 'm', 't', ' ' }); w.Write(16); w.Write((short)1); w.Write((short)Canals); w.Write(Rate);
        w.Write(Rate * Canals * 2); w.Write((short)(Canals * 2)); w.Write((short)16);
        w.Write(new char[] { 'd', 'a', 't', 'a' }); w.Write(pcm.Length); w.Write(pcm);
        w.Flush();
        return m.ToArray();
    }

    public static void Api(string nom, Dictionary<string, string> q, HttpListenerContext ctx)
    {
        HttpListenerResponse res = ctx.Response;
        try
        {
            if (nom == "obre")
            {
                string font = CasaServidor.Q(q, "font"), u = CasaServidor.Q(q, "u");
                double ss = 0; double.TryParse(CasaServidor.Q(q, "ss"), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out ss);
                double durada = 0;
                if (font == "fitxer" && u.StartsWith("/f/"))
                {
                    // /f/<carpeta>/<cami> -> fitxer del PC
                    string[] parts = u.Substring(3).Split(new char[] { '/' }, 2);
                    string arrel = parts.Length == 2 ? CasaServidor.RutaArrel(Uri.UnescapeDataString(parts[0])) : null;
                    if (arrel == null) { CasaServidor.Error(res, 404, "Carpeta"); return; }
                    string rel = Uri.UnescapeDataString(parts[1].Split('?')[0]).Replace('/', '\\');
                    string full = Path.GetFullPath(Path.Combine(arrel, rel));
                    if (!full.StartsWith(Path.GetFullPath(arrel), StringComparison.OrdinalIgnoreCase) || !File.Exists(full)) { CasaServidor.Error(res, 404, "Fitxer"); return; }
                    u = full;
                    durada = Durada(full);
                }
                else if (!(font == "spotify" || u.StartsWith("http://") || u.StartsWith("https://"))) { CasaServidor.Error(res, 400, "Font"); return; }
                if (font == "spotify" && !CasaServidor.FuncioActiva("spotify")) { CasaServidor.Error(res, 403, "Spotify desactivat"); return; }
                Font f = Obre(font, u, ss);
                if (durada > 0) f.Durada = durada;
                // Espera el primer tros (fins a 15 s: algunes emissores triguen a respondre)
                lock (f.Lk) { DateTime fi = DateTime.Now.AddSeconds(15); while (f.Seq < 0 && f.Viu && DateTime.Now < fi) Monitor.Wait(f.Lk, 500); }
                if (f.Fitxer && f.Seq >= 0) { CasaServidor.Text(res, "{\"id\":\"" + f.Id + "\",\"seq\":0,\"fitxer\":true,\"durada\":" + f.Durada.ToString(System.Globalization.CultureInfo.InvariantCulture) + "}", "application/json; charset=utf-8"); return; }
                if (f.Seq < 0) { CasaServidor.Error(res, 502, f.Error != "" ? f.Error : "L'emissora no respon"); return; }
                CasaServidor.Text(res, "{\"id\":\"" + f.Id + "\",\"seq\":" + f.Seq + ",\"fitxer\":" + (f.Fitxer ? "true" : "false") + ",\"durada\":" + f.Durada.ToString(System.Globalization.CultureInfo.InvariantCulture) + "}", "application/json; charset=utf-8");
                return;
            }
            if (nom == "tros")
            {
                Font f;
                lock (lk) fonts.TryGetValue(CasaServidor.Q(q, "id"), out f);
                if (f == null) { CasaServidor.Error(res, 410, "Ja no sona"); return; }
                f.Vist = DateTime.Now;
                long n; long.TryParse(CasaServidor.Q(q, "n"), out n);
                byte[] b = null;
                lock (f.Lk)
                {
                    DateTime fi = DateTime.Now.AddSeconds(5);
                    while (f.Seq < n && f.Viu && DateTime.Now < fi) Monitor.Wait(f.Lk, 500);
                    if (f.Seq < n) { CasaServidor.Error(res, f.Viu ? 504 : 410, f.Viu ? "Encara no" : (f.Fitxer ? "fi" : "Ja no sona")); return; }
                    if (n <= f.Seq - Anell + 1) n = f.Fitxer ? f.Seq - Anell + 2 : f.Seq - 1;   // massa enrere: salta al directe
                    b = f.Trossos[n % Anell];
                }
                byte[] w = Wav(b);
                res.AddHeader("X-Seq", n.ToString());
                res.AddHeader("X-Nivell", f.Nivell.ToString());
                res.AddHeader("Access-Control-Expose-Headers", "X-Seq, X-Nivell");
                res.AddHeader("Cache-Control", "no-store");
                res.ContentType = "audio/wav";
                res.ContentLength64 = w.Length;
                res.OutputStream.Write(w, 0, w.Length);
                res.OutputStream.Close();
                return;
            }
            if (nom == "atura")
            {
                Font f;
                lock (lk) { if (fonts.TryGetValue(CasaServidor.Q(q, "id"), out f)) fonts.Remove(f.Id); }
                if (f != null) { f.Viu = false; try { if (!f.P.HasExited) f.P.Kill(); } catch { } }
                CasaServidor.Text(res, "{\"ok\":true}", "application/json; charset=utf-8");
                return;
            }
            CasaServidor.Error(res, 404, "API");
        }
        catch (Exception e) { try { CasaServidor.Error(res, 500, e.Message); } catch { } }
    }
}
