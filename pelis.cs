// Biblioteca de pel.licules: mira cada carpeta, tria el fitxer de la pel.licula (el video gran)
// i ignora mostres, trailers, extres i fitxers petits. C# 5.
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text.RegularExpressions;

public static class Pelis
{
    static readonly string[] ExtVideo = { ".mp4", ".m4v", ".mov", ".mkv", ".avi", ".wmv", ".ts", ".m2ts", ".webm", ".mpg", ".mpeg", ".divx", ".flv", ".3gp" };
    static readonly string[] ExtSubs = { ".srt", ".vtt" };
    static readonly Regex Brossa = new Regex(@"(^|[\s._\-\[\(])(sample|trailer|teaser|extras?|featurettes?|behind[\s._-]?the[\s._-]?scenes|deleted[\s._-]?scenes|interviews?|making[\s._-]?of|bonus|preview|promo|rarbg|etrg|yts)([\s._\-\]\)]|$)", RegexOptions.IgnoreCase);
    static readonly Regex CarpetaBrossa = new Regex(@"^(subs?|subtitles?|sample|samples|extras?|featurettes?|trailers?|screens?|screenshots?|artwork|covers?|other|bonus|behind the scenes|deleted scenes|interviews)$", RegexOptions.IgnoreCase);
    const long Gran = 300L * 1024 * 1024, Petit = 30L * 1024 * 1024;

    public static bool EsVideo(string nom) { return Array.IndexOf(ExtVideo, Path.GetExtension(nom).ToLowerInvariant()) >= 0; }
    static bool Amagat(FileSystemInfo f) { return (f.Attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0 || f.Name.StartsWith(".") || f.Name.StartsWith("@") || f.Name.StartsWith("$"); }

    // Videos que podrien ser la pel.licula (fins a un nivell de subcarpetes: CD1, Movie/...)
    static List<FileInfo> Videos(DirectoryInfo d, int nivell)
    {
        List<FileInfo> r = new List<FileInfo>();
        try
        {
            foreach (FileInfo f in d.GetFiles())
                if (!Amagat(f) && EsVideo(f.Name) && !Brossa.IsMatch(Path.GetFileNameWithoutExtension(f.Name))) r.Add(f);
            if (nivell > 0)
                foreach (DirectoryInfo s in d.GetDirectories())
                    if (!Amagat(s) && !CarpetaBrossa.IsMatch(s.Name) && !Brossa.IsMatch(s.Name)) r.AddRange(Videos(s, nivell - 1));
        }
        catch { }
        r.Sort(delegate (FileInfo a, FileInfo b) { return b.Length.CompareTo(a.Length); });
        return r;
    }

    // Torna el fitxer principal si la carpeta es UNA pel.licula; null si es una col.leccio o no hi ha res
    public static FileInfo Principal(DirectoryInfo d, out int quantes)
    {
        List<FileInfo> v = Videos(d, 1);
        quantes = 0;
        if (v.Count == 0) return null;
        long llindar = v[0].Length >= Gran ? Gran : Petit;
        List<FileInfo> grans = v.FindAll(delegate (FileInfo f) { return f.Length >= llindar; });
        if (grans.Count == 0) grans.Add(v[0]);
        quantes = grans.Count;
        if (grans.Count == 1) return grans[0];
        // Dues parts (CD1/CD2, part1/part2): la primera
        if (grans.Count == 2 && Regex.IsMatch(grans[0].Name + grans[1].Name, @"(cd|part|disc|disk)[\s._-]?[12]", RegexOptions.IgnoreCase))
        {
            quantes = 1;
            return string.Compare(grans[0].Name, grans[1].Name, StringComparison.OrdinalIgnoreCase) < 0 ? grans[0] : grans[1];
        }
        // Un video molt mes gran que la resta (la resta son extres)
        if (grans[1].Length < grans[0].Length * 0.35) { quantes = 1; return grans[0]; }
        return null;
    }

    static string Rel(string arrel, string full)
    {
        string r = full.Substring(arrel.TrimEnd('\\', '/').Length).TrimStart('\\', '/');
        return r.Replace('\\', '/');
    }

    static List<object> Subtitols(string arrel, FileInfo video)
    {
        List<object> r = new List<object>();
        string b = Path.GetFileNameWithoutExtension(video.Name).ToLowerInvariant();
        List<DirectoryInfo> dirs = new List<DirectoryInfo>();
        dirs.Add(video.Directory);
        try { foreach (DirectoryInfo s in video.Directory.GetDirectories()) if (Regex.IsMatch(s.Name, "^(subs?|subtitles?)$", RegexOptions.IgnoreCase)) dirs.Add(s); } catch { }
        foreach (DirectoryInfo d in dirs)
        {
            FileInfo[] fs;
            try { fs = d.GetFiles(); } catch { continue; }
            foreach (FileInfo f in fs)
            {
                if (Array.IndexOf(ExtSubs, f.Extension.ToLowerInvariant()) < 0) continue;
                string n = Path.GetFileNameWithoutExtension(f.Name);
                string resta = n.ToLowerInvariant().StartsWith(b) ? n.Substring(b.Length) : n;
                resta = resta.Trim(' ', '.', '_', '-');
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

    static long Ms(DateTime t) { return (long)(t.ToUniversalTime() - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds; }

    static Dictionary<string, object> ElementPeli(string arrel, string identitat, string nom, FileInfo v)
    {
        Dictionary<string, object> e = new Dictionary<string, object>();
        e["tipus"] = "peli";
        e["n"] = nom;
        e["p"] = identitat;
        e["v"] = Rel(arrel, v.FullName);
        e["ext"] = v.Extension.TrimStart('.').ToLowerInvariant();
        e["mida"] = v.Length;
        e["t"] = Ms(v.LastWriteTimeUtc);
        e["subs"] = Subtitols(arrel, v);
        return e;
    }

    public static void Api(Dictionary<string, string> q, HttpListenerContext ctx)
    {
        string arrel = CasaServidor.RutaArrel("pelis");
        string rel = CasaServidor.Q(q, "p");
        string dir = CasaServidor.Ruta("pelis", rel);
        if (arrel == null || dir == null || !Directory.Exists(dir)) { CasaServidor.Error(ctx.Response, 404, "Carpeta no trobada"); return; }
        DirectoryInfo di = new DirectoryInfo(dir);
        List<object> els = new List<object>();
        try
        {
            foreach (DirectoryInfo d in di.GetDirectories())
            {
                if (Amagat(d) || CarpetaBrossa.IsMatch(d.Name)) continue;
                int n;
                FileInfo p = Principal(d, out n);
                string r = Rel(arrel, d.FullName);
                if (p != null) { els.Add(ElementPeli(arrel, r, d.Name, p)); continue; }
                // Col.leccio o serie: nomes si hi ha algun video a dins (fins a 3 nivells)
                if (n == 0 && Videos(d, 3).Count == 0) continue;
                Dictionary<string, object> c = new Dictionary<string, object>();
                c["tipus"] = "carpeta"; c["n"] = d.Name; c["p"] = r; c["num"] = n; c["t"] = Ms(d.LastWriteTimeUtc);
                els.Add(c);
            }
            foreach (FileInfo f in Videos(di, 0))
            {
                if (f.Length < Petit && Videos(di, 0).Count > 1) continue;
                els.Add(ElementPeli(arrel, Rel(arrel, f.FullName), Path.GetFileNameWithoutExtension(f.Name), f));
            }
        }
        catch (Exception e) { CasaServidor.Error(ctx.Response, 500, e.Message); return; }
        Dictionary<string, object> o = new Dictionary<string, object>();
        o["cami"] = rel;
        o["elements"] = els;
        CasaServidor.Json(ctx.Response, o);
    }
}
