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
    }

    static readonly Dictionary<string, Font> fonts = new Dictionary<string, Font>();
    static readonly object lk = new object();
    static Timer neteja;

    static string Args(string font, string u)
    {
        string sortida = " -vn -ac " + Canals + " -ar " + Rate + " -f s16le pipe:1";
        if (font == "spotify")
        {
            string disp = Spotify.Ajuda("dispositiu", "CABLE Output (VB-Audio Virtual Cable)");
            return "-hide_banner -loglevel error -nostdin -f dshow -audio_buffer_size 50 -i audio=\"" + disp.Replace("\"", "") + "\"" + sortida;
        }
        string extra = u.StartsWith("http") ? "-reconnect 1 -reconnect_streamed 1 -reconnect_delay_max 5 -rw_timeout 15000000 -user_agent \"Mozilla/5.0 (Windows NT 10.0; Win64; x64) Hearth\" " : "";
        return "-hide_banner -loglevel error -nostdin " + extra + "-i \"" + u.Replace("\"", "%22") + "\"" + sortida;
    }

    static Font Obre(string font, string u)
    {
        string id = CasaServidor.Hash(font + "|" + u).Substring(0, 12);
        lock (lk)
        {
            Font f;
            if (fonts.TryGetValue(id, out f) && f.Viu && f.P != null && !f.P.HasExited) { f.Vist = DateTime.Now; return f; }
            string exe = SoPC.RutaFfmpeg();
            if (exe == null) throw new Exception("Falta ffmpeg");
            f = new Font(); f.Id = id;
            ProcessStartInfo si = new ProcessStartInfo(exe, Args(font, u));
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
                        while (fet < Tros) { int n = s.Read(b, fet, Tros - fet); if (n <= 0) throw new EndOfStreamException(); fet += n; }
                        lock (ff.Lk) { ff.Seq++; ff.Trossos[ff.Seq % Anell] = b; Monitor.PulseAll(ff.Lk); }
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
            foreach (Font f in fonts.Values) if (!f.Viu || (DateTime.Now - f.Vist).TotalSeconds > 20) fora.Add(f);
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
                if (font != "spotify" && !(u.StartsWith("http://") || u.StartsWith("https://"))) { CasaServidor.Error(res, 400, "Font"); return; }
                if (font == "spotify" && !CasaServidor.FuncioActiva("spotify")) { CasaServidor.Error(res, 403, "Spotify desactivat"); return; }
                Font f = Obre(font, u);
                // Espera el primer tros (fins a 15 s: algunes emissores triguen a respondre)
                lock (f.Lk) { DateTime fi = DateTime.Now.AddSeconds(15); while (f.Seq < 0 && f.Viu && DateTime.Now < fi) Monitor.Wait(f.Lk, 500); }
                if (f.Seq < 0) { CasaServidor.Error(res, 502, f.Error != "" ? f.Error : "L'emissora no respon"); return; }
                CasaServidor.Text(res, "{\"id\":\"" + f.Id + "\",\"seq\":" + f.Seq + "}", "application/json; charset=utf-8");
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
                    if (f.Seq < n) { CasaServidor.Error(res, f.Viu ? 504 : 410, f.Viu ? "Encara no" : "Ja no sona"); return; }
                    if (n <= f.Seq - Anell + 1) n = f.Seq - 1;   // massa enrere: salta al directe
                    b = f.Trossos[n % Anell];
                }
                byte[] w = Wav(b);
                res.AddHeader("X-Seq", n.ToString());
                res.AddHeader("Access-Control-Expose-Headers", "X-Seq");
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
