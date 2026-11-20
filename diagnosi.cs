// Diagnostic: un semafor per a cada peca de Hearth i els darrers errors del registre,
// perque sigui facil saber que falla (i enviar-ho a qui t'ajudi). C# 5.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

public static class Diagnosi
{
    static readonly DateTime Inici = DateTime.Now;

    static void Afegeix(List<object> l, string grup, string nom, string estat, string detall)
    {
        Dictionary<string, object> o = new Dictionary<string, object>();
        o["grup"] = grup; o["nom"] = nom; o["estat"] = estat; o["detall"] = detall ?? "";
        l.Add(o);
    }

    static bool Activa(string k) { return CasaServidor.FuncioActiva(k); }

    static string Versio(string exe, string args)
    {
        try
        {
            ProcessStartInfo si = new ProcessStartInfo(exe, args);
            si.UseShellExecute = false; si.CreateNoWindow = true; si.RedirectStandardOutput = true; si.RedirectStandardError = true;
            using (Process p = Process.Start(si))
            {
                string o = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
                p.WaitForExit(8000);
                string l = o.Trim().Split('\n')[0].Trim();
                return l.Length > 80 ? l.Substring(0, 80) : l;
            }
        }
        catch (Exception e) { return "error: " + e.Message; }
    }

    static List<string> Errors()
    {
        List<string> r = new List<string>();
        try
        {
            string f = Path.Combine(CasaServidor.DirDades, "registre.txt");
            if (!File.Exists(f)) return r;
            string text;
            using (FileStream fs = new FileStream(f, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                long ini = Math.Max(0, fs.Length - 300000);
                fs.Seek(ini, SeekOrigin.Begin);
                using (StreamReader sr = new StreamReader(fs, Encoding.UTF8)) text = sr.ReadToEnd();
            }
            foreach (string l in text.Split('\n'))
            {
                string t = l.Trim();
                if (t == "") continue;
                if (Regex.IsMatch(t, @"error|ERROR|AVIS|no s'ha|No s'ha|no respon|Exception|ha fallat|failed", RegexOptions.None)) r.Add(t.Length > 220 ? t.Substring(0, 220) : t);
            }
            if (r.Count > 25) r = r.GetRange(r.Count - 25, 25);
        }
        catch { }
        return r;
    }

    public static void Api(HttpListenerContext ctx)
    {
        List<object> l = new List<object>();
        string ok = "ok", avis = "avis", err = "error", off = "off";

        // Servidor
        TimeSpan up = DateTime.Now - Inici;
        Afegeix(l, "Servidor", "Hearth", ok, "Versi\u00f3 " + Actualitza.Local() + " \u00b7 en marxa des de fa " + (up.TotalHours >= 1 ? (int)up.TotalHours + " h " : "") + up.Minutes + " min");
        Afegeix(l, "Servidor", "Windows", ok, Environment.OSVersion.VersionString + " \u00b7 .NET " + Environment.Version);
        try
        {
            DriveInfo di = new DriveInfo(Path.GetPathRoot(CasaServidor.DirDades));
            double gb = di.AvailableFreeSpace / 1073741824.0;
            Afegeix(l, "Servidor", "Espai lliure al disc", gb < 2 ? err : gb < 10 ? avis : ok, gb.ToString("0.0") + " GB a " + di.Name);
        }
        catch { }
        string[][] cs = new string[][] { new string[] { "fotos", "Fotos" }, new string[] { "musica", "M\u00fasica" }, new string[] { "pelis", "Pel\u00b7l\u00edcules" }, new string[] { "documents", "Documents" } };
        foreach (string[] c in cs)
        {
            string r = CasaServidor.RutaArrel(c[0]);
            if (r == null) Afegeix(l, "Carpetes", c[1], off, "No configurada");
            else Afegeix(l, "Carpetes", c[1], Directory.Exists(r) ? ok : err, r + (Directory.Exists(r) ? "" : " (no existeix)"));
        }

        // Eines
        string ff = SoPC.RutaFfmpeg();
        Afegeix(l, "Eines", "ffmpeg", ff != null ? ok : err, ff != null ? Versio(ff, "-hide_banner -version") : "Falta: sense ffmpeg no van la r\u00e0dio a iOS 9, YouTube ni Spotify");
        string yt = YouTube.Ruta();
        Afegeix(l, "Eines", "yt-dlp", yt != null ? ok : err, yt != null ? Versio(yt, "--version") : "Falta: Configuraci\u00f3 \u25b8 YouTube \u25b8 Descarregar");
        string dn = YouTube.RutaDeno();
        Afegeix(l, "Eines", "Deno", dn != null ? ok : err, dn != null ? Versio(dn, "--version") : "Falta: YouTube no reprodueix sense Deno");
        string ed = Edge.Ruta();
        Afegeix(l, "Eines", "Microsoft Edge", ed == null ? err : Edge.Viu() ? ok : avis, ed == null ? "No trobat" : Edge.Viu() ? "En marxa (amagat)" : "Instal\u00b7lat; s'engega quan cal");

        // Serveis
        Afegeix(l, "Serveis", "Actualitzacions", ok, "Instal\u00b7lada " + Actualitza.Local());
        if (Activa("spotify"))
        {
            string disp = Spotify.Ajuda("dispositiu", "CABLE Output (VB-Audio Virtual Cable)");
            bool hi = false;
            try { hi = SoPC.Dispositius().Contains(disp); } catch { }
            Afegeix(l, "Serveis", "Spotify", hi ? ok : err, hi ? "So des de \u00ab" + disp + "\u00bb" + (SoPC.DarrerError != "" ? " \u00b7 " + SoPC.DarrerError : "") : "No trobo el dispositiu de so \u00ab" + disp + "\u00bb (cal VB-Cable)");
        }
        else Afegeix(l, "Serveis", "Spotify", off, "Desactivat");
        if (Activa("spotify"))
        {
            Spotify.Sortida();   // ho torna a aplicar ara, per veure el resultat
            string so = Spotify.UltimaSortida;
            Afegeix(l, "Serveis", "Sortida de l'Edge", so.Contains("\"ok\":true") ? ok : err, so != "" ? so : "No s'ha pogut aplicar");
        }
        int niv = Pcm.NivellSpotify();
        if (niv >= 0) Afegeix(l, "Serveis", "So de l'Spotify", niv > 0 ? ok : avis, niv > 0 ? "Arriba so del cable (" + niv + "%)" : "Arriba silenci: l'Spotify no sona pel cable, est\u00e0 en pausa");
        Afegeix(l, "Serveis", "WhatsApp", Activa("whatsapp") ? ok : off, Activa("whatsapp") ? "Activat" : "Desactivat");
        bool ty = File.Exists(Path.Combine(CasaServidor.DirDades, "tuya.json"));
        Afegeix(l, "Serveis", "Casa (Tuya)", ty ? ok : off, ty ? "Configurat" : "No configurat");
        int nc = Cast.Coneguts();
        Afegeix(l, "Serveis", "Enviar a la tele", nc > 0 ? ok : off, nc > 0 ? nc + " aparells trobats" : "Encara no s'ha buscat cap aparell");

        Dictionary<string, object> o = new Dictionary<string, object>();
        o["comprovacions"] = l;
        o["errors"] = Errors();
        o["hora"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        CasaServidor.Json(ctx.Response, o);
    }
}
