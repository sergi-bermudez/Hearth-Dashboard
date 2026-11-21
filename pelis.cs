// Biblioteca de pel.licules: indexa TOTS els videos de totes les carpetes (pot haver-n'hi
// diversos a cada carpeta), en mesura la durada amb ffmpeg i descarta els curts (mostres,
// extres...). En segon pla busca la fitxa de cada una (caratula, genere, any, nota). C# 5.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;

public static class Pelis
{
    static readonly string[] ExtVideo = { ".mp4", ".m4v", ".mov", ".mkv", ".avi", ".wmv", ".ts", ".m2ts", ".webm", ".mpg", ".mpeg", ".divx", ".flv", ".3gp" };
    static readonly string[] ExtSubs = { ".srt", ".vtt" };
    static readonly Regex Brossa = new Regex(@"(^|[\s._\-\[\(])(sample|trailer|teaser|featurettes?|behind[\s._-]?the[\s._-]?scenes|deleted[\s._-]?scenes|making[\s._-]?of|rarbg|etrg)([\s._\-\]\)]|$)", RegexOptions.IgnoreCase);
    static readonly Regex CarpetaBrossa = new Regex(@"^(subs?|subtitles?|sample|samples|extras?|featurettes?|trailers?|screens?|screenshots?|artwork|covers?)$", RegexOptions.IgnoreCase);

    static readonly object lk = new object();
    static Dictionary<string, object[]> index;      // rel -> { mida, data (ticks), durada (s) }
    static int indexant = 0, buscant = 0;
    static string estat = "";
    static DateTime darrer = DateTime.MinValue;

    static string FitxerIndex { get { return Path.Combine(CasaServidor.DirDades, "pelis-index.json"); } }

    public static bool EsVideo(string nom) { return Array.IndexOf(ExtVideo, Path.GetExtension(nom).ToLowerInvariant()) >= 0; }
    static bool Amagat(FileSystemInfo f) { return (f.Attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0 || f.Name.StartsWith(".") || f.Name.StartsWith("@") || f.Name.StartsWith("$"); }

    // Durada minima per considerar que un video es una pel.licula (minuts, Configuracio > Pel.licules)
    static int MinutsMinims()
    {
        try
        {
            string f = Path.Combine(CasaServidor.DirDades, "media.json");
            if (File.Exists(f))
            {
                Dictionary<string, object> d = new JavaScriptSerializer().DeserializeObject(File.ReadAllText(f, Encoding.UTF8)) as Dictionary<string, object>;
                object v; int n;
                if (d != null && d.TryGetValue("pelisMin", out v) && v != null && int.TryParse(v.ToString(), out n)) return Math.Max(0, n);
            }
        }
        catch { }
        return 40;
    }

    static string Rel(string arrel, string full) { return full.Substring(arrel.TrimEnd('\\', '/').Length).TrimStart('\\', '/').Replace('\\', '/'); }
    static long Ms(DateTime t) { return (long)(t.ToUniversalTime() - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds; }

    // Durada amb ffmpeg ("Duration: 01:52:03.20"); 0 si no es pot saber
    static int MesuraDurada(string full)
    {
        string ff = SoPC.RutaFfmpeg();
        if (ff == null) return 0;
        try
        {
            ProcessStartInfo si = new ProcessStartInfo(ff, "-hide_banner -nostdin -i \"" + full + "\"");
            si.UseShellExecute = false; si.CreateNoWindow = true; si.RedirectStandardError = true;
            using (Process p = Process.Start(si))
            {
                string err = p.StandardError.ReadToEnd();
                if (!p.WaitForExit(15000)) { try { p.Kill(); } catch { } }
                Match m = Regex.Match(err, @"Duration:\s*(\d+):(\d+):(\d+)");
                if (m.Success) return int.Parse(m.Groups[1].Value) * 3600 + int.Parse(m.Groups[2].Value) * 60 + int.Parse(m.Groups[3].Value);
            }
        }
        catch { }
        return 0;
    }

    public static int Durada(string rel)
    {
        lock (lk) { Carrega(); object[] r; if (index.TryGetValue(rel, out r)) return Convert.ToInt32(r[2]); }
        return 0;
    }

    static void Carrega()
    {
        if (index != null) return;
        index = new Dictionary<string, object[]>();
        try
        {
            if (File.Exists(FitxerIndex))
            {
                JavaScriptSerializer js = new JavaScriptSerializer(); js.MaxJsonLength = int.MaxValue;
                Dictionary<string, object> d = js.DeserializeObject(File.ReadAllText(FitxerIndex, Encoding.UTF8)) as Dictionary<string, object>;
                if (d != null) foreach (KeyValuePair<string, object> kv in d) { object[] a = kv.Value as object[]; if (a != null && a.Length >= 3) index[kv.Key] = new object[] { Convert.ToInt64(a[0]), Convert.ToInt64(a[1]), Convert.ToInt32(a[2]) }; }
            }
        }
        catch { }
    }

    static IEnumerable<string> Fitxers(string arrel)
    {
        Stack<string> pila = new Stack<string>();
        pila.Push(arrel);
        while (pila.Count > 0)
        {
            string d = pila.Pop();
            string[] fs = null, ds = null;
            try { fs = Directory.GetFiles(d); ds = Directory.GetDirectories(d); } catch { }
            if (fs != null) foreach (string f in fs) if (EsVideo(f)) yield return f;
            if (ds != null) foreach (string x in ds) { string n = Path.GetFileName(x); if (!n.StartsWith(".") && !n.StartsWith("$") && !n.StartsWith("@")) pila.Push(x); }
        }
    }

    public static void Indexa()
    {
        if (Interlocked.CompareExchange(ref indexant, 1, 0) != 0) return;
        ThreadPool.QueueUserWorkItem(delegate
        {
            try
            {
                string arrel = CasaServidor.RutaArrel("pelis");
                if (arrel == null || !Directory.Exists(arrel)) { estat = "No hi ha carpeta de pel\u00b7l\u00edcules"; return; }
                lock (lk) Carrega();
                Dictionary<string, object[]> nou = new Dictionary<string, object[]>();
                int fets = 0;
                foreach (string f in Fitxers(arrel))
                {
                    FileInfo fi = new FileInfo(f);
                    if (Amagat(fi)) continue;
                    string rel = Rel(arrel, f);
                    object[] vell;
                    lock (lk) index.TryGetValue(rel, out vell);
                    if (vell != null && (long)vell[0] == fi.Length && (long)vell[1] == fi.LastWriteTimeUtc.Ticks && (int)vell[2] > 0) nou[rel] = vell;
                    else
                    {
                        nou[rel] = new object[] { fi.Length, fi.LastWriteTimeUtc.Ticks, MesuraDurada(f) };
                        lock (lk) index[rel] = nou[rel];
                    }
                    fets++;
                    if (fets % 20 == 0) estat = "Mirant v\u00eddeos: " + fets;
                }
                lock (lk) { index = nou; darrer = DateTime.Now; }
                JavaScriptSerializer js = new JavaScriptSerializer(); js.MaxJsonLength = int.MaxValue;
                File.WriteAllText(FitxerIndex, js.Serialize(nou), new UTF8Encoding(false));
                estat = "";
            }
            catch (Exception e) { estat = "Error: " + e.Message; }
            finally { indexant = 0; }
            BuscaFitxes();
        });
    }

    static bool EsPeli(string rel, object[] r)
    {
        if (Brossa.IsMatch(Path.GetFileNameWithoutExtension(rel))) return false;
        foreach (string part in rel.Split('/')) if (CarpetaBrossa.IsMatch(part)) return false;
        int d = Convert.ToInt32(r[2]);
        if (d > 0) return d >= MinutsMinims() * 60;
        return Convert.ToInt64(r[0]) >= 300L * 1024 * 1024;      // sense ffmpeg: per mida
    }

    // Busca la fitxa (internet) de les pel.licules que encara no en tenen, d'una en una
    static void BuscaFitxes()
    {
        if (Interlocked.CompareExchange(ref buscant, 1, 0) != 0) return;
        ThreadPool.QueueUserWorkItem(delegate
        {
            try
            {
                List<string> falten = new List<string>();
                lock (lk) foreach (KeyValuePair<string, object[]> kv in index) if (EsPeli(kv.Key, kv.Value) && Media.MetaEnCau(kv.Key) == null) falten.Add(kv.Key);
                int n = 0;
                foreach (string rel in falten)
                {
                    estat = "Buscant fitxes: " + (++n) + " de " + falten.Count;
                    try { Media.Fitxa(rel); } catch { }
                    Thread.Sleep(700);
                }
                estat = "";
            }
            finally { buscant = 0; }
        });
    }

    static List<object> Subtitols(string arrel, FileInfo video)
    {
        List<object> r = new List<object>();
        string b = Path.GetFileNameWithoutExtension(video.Name).ToLowerInvariant();
        List<DirectoryInfo> dirs = new List<DirectoryInfo>();
        dirs.Add(video.Directory);
        try { foreach (DirectoryInfo s in video.Directory.GetDirectories()) if (Regex.IsMatch(s.Name, "^(subs?|subtitles?)$", RegexOptions.IgnoreCase)) dirs.Add(s); } catch { }
        // Si a la carpeta hi ha diversos videos, nomes els subtitols que comencen pel nom d'aquest
        int videos = 0;
        try { foreach (FileInfo f in video.Directory.GetFiles()) if (EsVideo(f.Name)) videos++; } catch { }
        foreach (DirectoryInfo d in dirs)
        {
            FileInfo[] fs;
            try { fs = d.GetFiles(); } catch { continue; }
            foreach (FileInfo f in fs)
            {
                if (Array.IndexOf(ExtSubs, f.Extension.ToLowerInvariant()) < 0) continue;
                string n = Path.GetFileNameWithoutExtension(f.Name);
                bool meu = n.ToLowerInvariant().StartsWith(b);
                if (videos > 1 && !meu) continue;
                string resta = (meu ? n.Substring(b.Length) : n).Trim(' ', '.', '_', '-');
                Match m = Regex.Match(resta, @"(^|[\s._-])([a-z]{2,3})($|[\s._-])", RegexOptions.IgnoreCase);
                Dictionary<string, object> x = new Dictionary<string, object>();
                x["p"] = Rel(arrel, f.FullName);
                x["label"] = resta != "" ? resta : "Subt\u00edtols";
                x["lang"] = m.Success ? m.Groups[2].Value.ToLowerInvariant().Substring(0, 2) : "";
                r.Add(x);
            }
        }
        return r;
    }

    static Dictionary<string, object> Element(string rel, object[] r, bool ambSubs, string arrel)
    {
        Dictionary<string, object> e = new Dictionary<string, object>();
        e["p"] = rel;
        e["n"] = Path.GetFileNameWithoutExtension(rel);
        e["carpeta"] = rel.Contains("/") ? rel.Substring(0, rel.LastIndexOf('/')) : "";
        e["ext"] = Path.GetExtension(rel).TrimStart('.').ToLowerInvariant();
        e["mida"] = r[0];
        e["t"] = Ms(new DateTime((long)r[1], DateTimeKind.Utc));
        e["d"] = r[2];
        Dictionary<string, object> m = Media.MetaEnCau(rel);
        if (m != null)
        {
            foreach (string k in new string[] { "titol", "any", "genere", "nota", "imdb", "fontNom" }) { object v; if (m.TryGetValue(k, out v) && v != null && v.ToString() != "") e[k] = v; }
            e["poster"] = m.ContainsKey("poster");
            e["trobada"] = m.ContainsKey("fontNom");
            if (m.ContainsKey("confirmada")) e["ok"] = true;
            else if (m.ContainsKey("fontNom") && Media.ConfiancaEnCau(rel, m) < 0.75) e["dubte"] = true;
            if (m.ContainsKey("fons")) e["fons"] = true;
        }
        if (ambSubs) { try { e["subs"] = Subtitols(arrel, new FileInfo(CasaServidor.Ruta("pelis", rel))); } catch { } }
        return e;
    }

    public static void Api(string nom, Dictionary<string, string> q, HttpListenerContext ctx)
    {
        HttpListenerResponse res = ctx.Response;
        string arrel = CasaServidor.RutaArrel("pelis");
        if (arrel == null || !Directory.Exists(arrel)) { CasaServidor.Error(res, 404, "Carpeta no trobada"); return; }
        if (index == null || (DateTime.Now - darrer).TotalMinutes > 20) Indexa();
        try
        {
            if (nom == "pelis/llista")
            {
                // Totes les pel.licules (videos prou llargs), amb la fitxa si ja se sap
                List<object> l = new List<object>();
                lock (lk) { Carrega(); foreach (KeyValuePair<string, object[]> kv in index) if (EsPeli(kv.Key, kv.Value)) l.Add(Element(kv.Key, kv.Value, false, arrel)); }
                Dictionary<string, object> o = new Dictionary<string, object>();
                o["pelis"] = l; o["estat"] = estat; o["indexant"] = indexant == 1; o["min"] = MinutsMinims();
                CasaServidor.Json(res, o);
                return;
            }
            if (nom == "pelis/fitxers")
            {
                // Mode fitxers: carpetes i TOTS els videos d'una carpeta (tambe els curts o no reconeguts)
                string rel = CasaServidor.Q(q, "p"), dir = CasaServidor.Ruta("pelis", rel);
                if (dir == null || !Directory.Exists(dir)) { CasaServidor.Error(res, 404, "Carpeta no trobada"); return; }
                List<object> carpetes = new List<object>(), videos = new List<object>();
                foreach (DirectoryInfo d in new DirectoryInfo(dir).GetDirectories()) if (!Amagat(d)) carpetes.Add(d.Name);
                foreach (FileInfo f in new DirectoryInfo(dir).GetFiles())
                {
                    if (Amagat(f) || !EsVideo(f.Name)) continue;
                    string r = Rel(arrel, f.FullName);
                    object[] x;
                    lock (lk) { Carrega(); index.TryGetValue(r, out x); }
                    if (x == null) x = new object[] { f.Length, f.LastWriteTimeUtc.Ticks, 0 };
                    Dictionary<string, object> e = Element(r, x, false, arrel);
                    e["esPeli"] = EsPeli(r, x);
                    videos.Add(e);
                }
                Dictionary<string, object> o = new Dictionary<string, object>();
                o["cami"] = rel; o["carpetes"] = carpetes; o["videos"] = videos;
                CasaServidor.Json(res, o);
                return;
            }
            if (nom == "pelis/video")
            {
                string rel = CasaServidor.Q(q, "p"), full = CasaServidor.Ruta("pelis", rel);
                if (full == null || !File.Exists(full)) { CasaServidor.Error(res, 404, "No trobat"); return; }
                object[] x;
                lock (lk) { Carrega(); index.TryGetValue(rel, out x); }
                if (x == null) { FileInfo fi = new FileInfo(full); x = new object[] { fi.Length, fi.LastWriteTimeUtc.Ticks, 0 }; }
                CasaServidor.Json(res, Element(rel, x, true, arrel));
                return;
            }
            if (nom == "pelis/reindexa") { lock (lk) { darrer = DateTime.MinValue; } Indexa(); CasaServidor.Text(res, "{\"ok\":true}", "application/json; charset=utf-8"); return; }
            CasaServidor.Error(res, 404, "API desconeguda");
        }
        catch (Exception e) { CasaServidor.Error(res, 500, e.Message); }
    }

    // Per al codi de caratules (Media): el video d'una carpeta si n'hi ha un de sol
    public static FileInfo Principal(DirectoryInfo d, out int quantes)
    {
        quantes = 0;
        FileInfo millor = null;
        try { foreach (FileInfo f in d.GetFiles()) if (EsVideo(f.Name) && !Brossa.IsMatch(f.Name)) { quantes++; if (millor == null || f.Length > millor.Length) millor = f; } } catch { }
        return quantes == 1 ? millor : null;
    }
}
