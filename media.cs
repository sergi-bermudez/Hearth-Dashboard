// Imatges i fitxes: imatges externes amb memoria cau, portades de musica i
// caratules i informacio de pel.licules (iTunes; TMDB i OMDb si poses la clau). C# 5.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;

public static class Media
{
    static readonly object lk = new object();
    static Dictionary<string, string> portades;
    static Dictionary<string, object> metes;
    static readonly string[] NomsPortada = { "cover", "folder", "front", "portada", "album", "albumart", "poster", "caratula" };
    static readonly string[] ExtPortada = { ".jpg", ".jpeg", ".png" };
    static readonly SemaphoreSlim xarxa = new SemaphoreSlim(4);

    static string Dir(string sub) { string d = Path.Combine(CasaServidor.DirDades, sub); Directory.CreateDirectory(d); return d; }

    static Dictionary<string, object> Ajustos()
    {
        string f = Path.Combine(CasaServidor.DirDades, "media.json");
        try { if (File.Exists(f)) { Dictionary<string, object> d = new JavaScriptSerializer().DeserializeObject(File.ReadAllText(f, Encoding.UTF8)) as Dictionary<string, object>; if (d != null) return d; } } catch { }
        return new Dictionary<string, object>();
    }
    static string A(string k, string def) { object v; Dictionary<string, object> a = Ajustos(); return a.TryGetValue(k, out v) && v != null && v.ToString() != "" ? v.ToString() : def; }
    static bool Internet() { return A("internet", "true").ToLowerInvariant() != "false"; }

    static string Baixa(string url, int ms)
    {
        xarxa.Wait();
        try
        {
            HttpWebRequest rq = (HttpWebRequest)WebRequest.Create(url);
            rq.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) CasaServidor/2.4";
            rq.Timeout = ms; rq.ReadWriteTimeout = ms;
            using (WebResponse rs = rq.GetResponse())
            using (StreamReader sr = new StreamReader(rs.GetResponseStream(), Encoding.UTF8)) return sr.ReadToEnd();
        }
        finally { xarxa.Release(); }
    }

    static object JsonDe(string url, int ms)
    {
        JavaScriptSerializer js = new JavaScriptSerializer();
        js.MaxJsonLength = int.MaxValue;
        return js.DeserializeObject(Baixa(url, ms));
    }

    // ---------- Imatges externes (logos de radio, caratules...) amb memoria cau ----------
    public static string ImatgeExterna(string url, int ms)
    {
        if (string.IsNullOrEmpty(url) || !(url.StartsWith("http://") || url.StartsWith("https://"))) return null;
        string dir = Dir("imgx"), h = CasaServidor.Hash(url);
        foreach (string e in new string[] { ".jpg", ".png", ".gif", ".ico", ".svg", ".webp" })
        {
            string f0 = Path.Combine(dir, h + e);
            if (File.Exists(f0)) return f0;
        }
        string err = Path.Combine(dir, h + ".err");
        if (File.Exists(err) && (DateTime.Now - File.GetLastWriteTime(err)).TotalHours < 12) return null;
        string tmp = Path.Combine(dir, h + ".part" + Thread.CurrentThread.ManagedThreadId);
        xarxa.Wait();
        try
        {
            HttpWebRequest rq = (HttpWebRequest)WebRequest.Create(url);
            rq.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0 Safari/537.36";
            rq.Timeout = ms; rq.ReadWriteTimeout = ms;
            string ct;
            using (WebResponse rs = rq.GetResponse())
            {
                ct = (rs.ContentType ?? "").ToLowerInvariant();
                using (Stream s = rs.GetResponseStream())
                using (FileStream fs = new FileStream(tmp, FileMode.Create)) s.CopyTo(fs);
            }
            if (new FileInfo(tmp).Length < 60) throw new Exception("buida");
            string ext = ct.Contains("png") ? ".png" : ct.Contains("gif") ? ".gif" : ct.Contains("icon") || url.ToLowerInvariant().EndsWith(".ico") ? ".ico" :
                ct.Contains("svg") ? ".svg" : ct.Contains("webp") ? ".webp" : ".jpg";
            string f = Path.Combine(dir, h + ext);
            if (File.Exists(f)) File.Delete(tmp); else File.Move(tmp, f);
            return f;
        }
        catch
        {
            try { File.Delete(tmp); } catch { }
            try { File.WriteAllText(err, url); } catch { }
            return null;
        }
        finally { xarxa.Release(); }
    }

    static void EnviaImatge(HttpListenerContext ctx, string f, int m)
    {
        if (f == null) { CasaServidor.Error(ctx.Response, 404, "Sense imatge"); return; }
        string ext = Path.GetExtension(f).ToLowerInvariant();
        string mini = (ext == ".svg" || ext == ".webp") ? null : CasaServidor.Miniatura(f, m, true);
        CasaServidor.EnviarFitxer(ctx, mini ?? f);
    }

    // ---------- Portades de musica ----------
    static void CarregaPortades()
    {
        if (portades != null) return;
        portades = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            string f = Path.Combine(CasaServidor.DirDades, "portades.json");
            if (File.Exists(f)) portades = new JavaScriptSerializer().Deserialize<Dictionary<string, string>>(File.ReadAllText(f, Encoding.UTF8));
            portades = new Dictionary<string, string>(portades, StringComparer.OrdinalIgnoreCase);
        }
        catch { }
    }
    static void DesaPortades()
    {
        try { File.WriteAllText(Path.Combine(CasaServidor.DirDades, "portades.json"), new JavaScriptSerializer().Serialize(portades), new UTF8Encoding(false)); } catch { }
    }

    static string ImatgeDeCarpeta(DirectoryInfo d)
    {
        FileInfo millor = null;
        int puntsMillor = -1;
        FileInfo[] fs;
        try { fs = d.GetFiles(); } catch { return null; }
        foreach (FileInfo f in fs)
        {
            string e = f.Extension.ToLowerInvariant();
            if (Array.IndexOf(ExtPortada, e) < 0 || f.Length < 2000) continue;
            string n = Path.GetFileNameWithoutExtension(f.Name).ToLowerInvariant();
            int punts = 1;
            for (int i = 0; i < NomsPortada.Length; i++) if (n.Contains(NomsPortada[i])) { punts = 10 - i; break; }
            if (n.Contains("back") || n.Contains("cd")) punts = 0;
            if (punts > puntsMillor) { puntsMillor = punts; millor = f; }
        }
        return millor == null ? null : millor.FullName;
    }

    // Caratula incrustada en un MP3 (ID3v2, marc APIC)
    static string CaratulaMp3(string mp3)
    {
        string desti = Path.Combine(Dir("portades"), CasaServidor.Hash(mp3.ToLowerInvariant()) + ".jpg");
        if (File.Exists(desti)) return desti;
        try
        {
            using (FileStream fs = new FileStream(mp3, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                byte[] cap = new byte[10];
                if (fs.Read(cap, 0, 10) < 10 || cap[0] != 'I' || cap[1] != 'D' || cap[2] != '3') return null;
                int ver = cap[3];
                int mida = (cap[6] << 21) | (cap[7] << 14) | (cap[8] << 7) | cap[9];
                if (mida <= 0 || mida > 20 * 1024 * 1024) return null;
                byte[] tag = new byte[mida];
                int llegit = 0;
                while (llegit < mida) { int n = fs.Read(tag, llegit, mida - llegit); if (n <= 0) break; llegit += n; }
                int p = 0;
                if ((cap[5] & 0x40) != 0 && ver >= 3)
                {
                    int ext = ver == 4 ? ((tag[0] << 21) | (tag[1] << 14) | (tag[2] << 7) | tag[3]) : ((tag[0] << 24) | (tag[1] << 16) | (tag[2] << 8) | tag[3]) + 4;
                    p += ext;
                }
                while (p + (ver == 2 ? 6 : 10) < llegit)
                {
                    string id; int mf, capf;
                    if (ver == 2) { id = Encoding.ASCII.GetString(tag, p, 3); mf = (tag[p + 3] << 16) | (tag[p + 4] << 8) | tag[p + 5]; capf = 6; }
                    else
                    {
                        id = Encoding.ASCII.GetString(tag, p, 4);
                        mf = ver == 4 ? ((tag[p + 4] << 21) | (tag[p + 5] << 14) | (tag[p + 6] << 7) | tag[p + 7]) : ((tag[p + 4] << 24) | (tag[p + 5] << 16) | (tag[p + 6] << 8) | tag[p + 7]);
                        capf = 10;
                    }
                    if (id[0] == 0 || mf <= 0 || p + capf + mf > llegit) break;
                    if (id == "APIC" || id == "PIC")
                    {
                        int q = p + capf, fi = q + mf;
                        int encd = tag[q++];
                        if (id == "PIC") q += 3; else { while (q < fi && tag[q] != 0) q++; q++; }
                        q++;
                        if (encd == 1 || encd == 2) { while (q + 1 < fi && !(tag[q] == 0 && tag[q + 1] == 0)) q += 2; q += 2; }
                        else { while (q < fi && tag[q] != 0) q++; q++; }
                        if (fi - q > 1000)
                        {
                            using (FileStream o = new FileStream(desti, FileMode.Create)) o.Write(tag, q, fi - q);
                            return desti;
                        }
                    }
                    p += capf + mf;
                }
            }
        }
        catch { }
        return null;
    }

    static string PortadaLocal(DirectoryInfo d, int nivell)
    {
        string img = ImatgeDeCarpeta(d);
        if (img != null) return img;
        FileInfo[] fs;
        try { fs = d.GetFiles("*.mp3"); } catch { fs = new FileInfo[0]; }
        for (int i = 0; i < fs.Length && i < 3; i++) { string c = CaratulaMp3(fs[i].FullName); if (c != null) return c; }
        if (nivell < 2)
        {
            DirectoryInfo[] subs;
            try { subs = d.GetDirectories(); } catch { subs = new DirectoryInfo[0]; }
            Array.Sort(subs, delegate (DirectoryInfo a, DirectoryInfo b) { return string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase); });
            for (int i = 0; i < subs.Length && i < 12; i++)
            {
                if ((subs[i].Attributes & FileAttributes.Hidden) != 0) continue;
                string c = PortadaLocal(subs[i], nivell + 1);
                if (c != null) return c;
            }
        }
        return null;
    }

    // Cerca a internet: artista (Deezer) o disc (iTunes)
    static string PortadaInternet(string arrel, DirectoryInfo d)
    {
        if (!Internet()) return null;
        string rel = d.FullName.Substring(Path.GetFullPath(arrel).TrimEnd('\\').Length).Trim('\\');
        string[] parts = rel.Split('\\');
        try
        {
            if (parts.Length == 1)
            {
                Dictionary<string, object> r = JsonDe("https://api.deezer.com/search/artist?limit=1&q=" + Uri.EscapeDataString(Neteja(parts[0])), 6000) as Dictionary<string, object>;
                object[] data = r != null && r.ContainsKey("data") ? r["data"] as object[] : null;
                if (data != null && data.Length > 0)
                {
                    Dictionary<string, object> a = data[0] as Dictionary<string, object>;
                    string u = a.ContainsKey("picture_xl") ? a["picture_xl"] as string : null;
                    if (u != null && !u.Contains("/artist//")) return ImatgeExterna(u, 8000);
                }
            }
            else
            {
                string terme = Neteja(parts[parts.Length - 2]) + " " + Neteja(parts[parts.Length - 1]);
                Dictionary<string, object> r = JsonDe("https://itunes.apple.com/search?entity=album&limit=1&term=" + Uri.EscapeDataString(terme), 6000) as Dictionary<string, object>;
                object[] res = r != null && r.ContainsKey("results") ? r["results"] as object[] : null;
                if (res != null && res.Length > 0)
                {
                    string u = ((Dictionary<string, object>)res[0])["artworkUrl100"] as string;
                    if (u != null) return ImatgeExterna(u.Replace("100x100bb", "600x600bb"), 8000);
                }
            }
        }
        catch { }
        return null;
    }

    static string Neteja(string n)
    {
        n = Regex.Replace(n, @"[\[(][^\])]*[\])]", " ");
        n = Regex.Replace(n, @"^\d{4}\s*-\s*", "");
        n = n.Replace('_', ' ').Replace('.', ' ');
        return Regex.Replace(n, @"\s+", " ").Trim();
    }

    static string Portada(string c, string rel)
    {
        string dir = CasaServidor.Ruta(c, rel);
        if (dir == null || !Directory.Exists(dir)) return null;
        DirectoryInfo d = new DirectoryInfo(dir);
        string clau = c + "|" + dir;
        lock (lk)
        {
            CarregaPortades();
            string v;
            if (portades.TryGetValue(clau, out v))
            {
                if (v == "") return null;
                if (File.Exists(v)) return v;
            }
        }
        string trobat = PortadaLocal(d, 0);
        if (trobat == null && c == "musica") trobat = PortadaInternet(CasaServidor.RutaArrel(c), d);
        lock (lk)
        {
            portades[clau] = trobat ?? "";
            DesaPortades();
        }
        return trobat;
    }

    // ---------- Pel.licules ----------
    static void CarregaMetes()
    {
        if (metes != null) return;
        metes = new Dictionary<string, object>();
        try
        {
            string f = Path.Combine(CasaServidor.DirDades, "pelis-meta.json");
            if (File.Exists(f)) metes = new JavaScriptSerializer().DeserializeObject(File.ReadAllText(f, Encoding.UTF8)) as Dictionary<string, object> ?? new Dictionary<string, object>();
        }
        catch { }
    }
    static void DesaMetes()
    {
        try
        {
            JavaScriptSerializer js = new JavaScriptSerializer(); js.MaxJsonLength = int.MaxValue;
            File.WriteAllText(Path.Combine(CasaServidor.DirDades, "pelis-meta.json"), js.Serialize(metes), new UTF8Encoding(false));
        }
        catch { }
    }

    static string S(Dictionary<string, object> d, string k) { object v; return d != null && d.TryGetValue(k, out v) && v != null ? v.ToString() : ""; }

    // Neteja el nom d'un fitxer o carpeta: "The.Matrix.1999.1080p.BluRay.x264-GRP" -> { "The Matrix", "1999" }
    static string[] TitolAny(string nom)
    {
        string t = nom;
        string e = Path.GetExtension(t);
        if (e.Length > 1 && e.Length <= 5 && !Regex.IsMatch(e, @"^\.\d+$")) t = Path.GetFileNameWithoutExtension(t);
        t = Regex.Replace(t, @"^\s*(www\.)?[a-z0-9-]+\.(com|org|net|to|me|cc|tv)\s*[-_ ]+", "", RegexOptions.IgnoreCase);
        t = Regex.Replace(t, @"^\s*\[[^\]]*\]\s*", "");
        t = t.Replace('.', ' ').Replace('_', ' ');
        string any = "";
        string orig = t;
        MatchCollection ms = Regex.Matches(orig, @"(?<![0-9])(19[2-9]\d|20[0-4]\d)(?![0-9])");
        foreach (Match m in ms)
        {
            // L'any es el darrer que no es al principi (p. ex. "2001 A Space Odyssey 1968")
            if (m.Index == 0) continue;
            any = m.Value;
            t = orig.Substring(0, m.Index);
        }
        t = Regex.Replace(t, @"\b(2160p|1080p|1080i|720p|576p|480p|4k|8k|uhd|hdr10?|hdr|dv|dolby ?vision|10 ?bit|8 ?bit|x264|x265|h ?264|h ?265|hevc|avc|xvid|divx|bluray|blu ray|blu-ray|bdrip|brrip|bdremux|remux|webrip|web ?dl|web|dvdrip|dvdscr|dvd|hdrip|hdtv|tvrip|cam|ts|aac\d?|ac3|eac3|ddp?\d?|dts(-hd)?|truehd|atmos|flac|mp3|5 1|7 1|2 0|multi|dual|dubbed|subbed|subs?|castellano|catala|catalan|espanol|spanish|english|ingles|vose|vos|latino|extended|unrated|remastered|directors? cut|imax|proper|repack|limited|complete|nf|amzn|dsnp|hmax|atvp)\b.*$", "", RegexOptions.IgnoreCase);
        t = Regex.Replace(t, @"[\[\(\{].*$", "");
        t = Regex.Replace(t, @"\s+-\s*[A-Za-z0-9]+\s*$", "");
        t = Regex.Replace(t, @"[-\s]+$", "");
        return new string[] { Regex.Replace(t, @"\s+", " ").Trim(), any };
    }

    // Noms possibles per a una pel.licula: la carpeta i el fitxer principal (sense repetir)
    static List<string[]> Candidats(string full)
    {
        List<string[]> r = new List<string[]>();
        List<string> noms = new List<string>();
        string arrel = (CasaServidor.RutaArrel("pelis") ?? "").TrimEnd('\\', '/');
        if (Directory.Exists(full))
        {
            noms.Add(Path.GetFileName(full.TrimEnd('\\', '/')));
            int n;
            FileInfo p = Pelis.Principal(new DirectoryInfo(full), out n);
            if (p != null) { noms.Add(p.Name); if (p.Directory.FullName.TrimEnd('\\') != full.TrimEnd('\\')) noms.Add(p.Directory.Name); }
        }
        else
        {
            noms.Add(Path.GetFileName(full));
            string pare = Path.GetDirectoryName(full);
            if (pare != null && pare.TrimEnd('\\', '/') != arrel) noms.Add(Path.GetFileName(pare));
        }
        string anyComu = "";
        foreach (string n in noms) { string[] ta = TitolAny(n); if (ta[1] != "") { anyComu = ta[1]; break; } }
        foreach (string n in noms)
        {
            string[] ta = TitolAny(n);
            if (ta[0].Length < 2 || Regex.IsMatch(ta[0], @"^(cd|disc|part)\s*\d$|^\d+$", RegexOptions.IgnoreCase)) continue;
            if (ta[1] == "") ta[1] = anyComu;
            bool hi = false;
            foreach (string[] x in r) if (string.Equals(x[0], ta[0], StringComparison.OrdinalIgnoreCase)) hi = true;
            if (!hi) r.Add(ta);
        }
        return r;
    }

    static string Pla(string s)
    {
        s = (s ?? "").ToLowerInvariant().Normalize(NormalizationForm.FormD);
        StringBuilder sb = new StringBuilder();
        foreach (char c in s) if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(char.IsLetterOrDigit(c) ? c : ' ');
        return Regex.Replace(sb.ToString(), @"\s+", " ").Trim();
    }
    // Semblanca entre dos titols (0..1) per paraules
    static double Semblanca(string a, string b)
    {
        string[] x = Pla(a).Split(' '), y = Pla(b).Split(' ');
        if (x.Length == 0 || y.Length == 0) return 0;
        int comuns = 0;
        foreach (string w in x) if (w != "" && Array.IndexOf(y, w) >= 0) comuns++;
        return 2.0 * comuns / (x.Length + y.Length);
    }

    static string Llengua() { return A("llengua", "ca"); }

    static void DeItunes(Dictionary<string, object> m, string titol, string any)
    {
        foreach (string pais in new string[] { A("paisItunes", "es"), "us" })
        {
            Dictionary<string, object> r = JsonDe("https://itunes.apple.com/search?media=movie&entity=movie&limit=15&country=" + pais + "&term=" + Uri.EscapeDataString(titol), 8000) as Dictionary<string, object>;
            object[] res = r != null && r.ContainsKey("results") ? r["results"] as object[] : null;
            Dictionary<string, object> tria = null;
            double millor = 0.45;
            if (res != null)
                foreach (object o in res)
                {
                    Dictionary<string, object> p = o as Dictionary<string, object>;
                    double s = Semblanca(titol, S(p, "trackName"));
                    if (any != "" && S(p, "releaseDate").StartsWith(any)) s += 0.4;
                    else if (any != "" && S(p, "releaseDate").Length >= 4) { int a1, a2; if (int.TryParse(any, out a1) && int.TryParse(S(p, "releaseDate").Substring(0, 4), out a2) && Math.Abs(a1 - a2) <= 1) s += 0.25; }
                    if (s > millor) { millor = s; tria = p; }
                }
            if (tria == null) continue;
            if (!m.ContainsKey("original")) m["titol"] = S(tria, "trackName");
            if (S(tria, "releaseDate").Length >= 4) m["any"] = S(tria, "releaseDate").Substring(0, 4);
            if (!m.ContainsKey("sinopsi") && S(tria, "longDescription") != "") m["sinopsi"] = S(tria, "longDescription");
            m["genere"] = S(tria, "primaryGenreName");
            m["edat"] = S(tria, "contentAdvisoryRating");
            long ms; if (long.TryParse(S(tria, "trackTimeMillis"), out ms)) m["minuts"] = (ms / 60000).ToString();
            if (S(tria, "artworkUrl100") != "") m["poster"] = S(tria, "artworkUrl100").Replace("100x100bb", "600x600bb");
            m["fontNom"] = "iTunes";
            return;
        }
    }

    // Viquipedia: sense clau. Cerca "titol any film" i agafa el resum i la imatge
    static void DeWikipedia(Dictionary<string, object> m, string titol, string any)
    {
        string[] llengues = Llengua() == "en" ? new string[] { "en" } : new string[] { Llengua(), "en" };
        foreach (string ll in llengues)
        {
            string paraula = ll == "ca" ? "pel\u00b7l\u00edcula" : ll == "es" ? "pel\u00edcula" : ll == "pt" ? "filme" : ll == "fr" ? "film" : "film";
            Dictionary<string, object> r = JsonDe("https://" + ll + ".wikipedia.org/w/api.php?action=query&list=search&format=json&srlimit=5&srsearch=" + Uri.EscapeDataString(titol + " " + any + " " + paraula), 8000) as Dictionary<string, object>;
            Dictionary<string, object> qd = r != null && r.ContainsKey("query") ? r["query"] as Dictionary<string, object> : null;
            object[] res = qd != null && qd.ContainsKey("search") ? qd["search"] as object[] : null;
            if (res == null) continue;
            string pagina = null;
            double millor = 0.4;
            foreach (object o in res)
            {
                Dictionary<string, object> p = o as Dictionary<string, object>;
                string t = S(p, "title"), net = Regex.Replace(t, @"\s*\([^)]*\)\s*$", "");
                double s = Semblanca(titol, net);
                if (Regex.IsMatch(t, @"\((film|pel|filme|movie)", RegexOptions.IgnoreCase)) s += 0.2;
                if (any != "" && (t.Contains(any) || S(p, "snippet").Contains(any))) s += 0.2;
                if (s > millor) { millor = s; pagina = t; }
            }
            if (pagina == null) continue;
            Dictionary<string, object> d = JsonDe("https://" + ll + ".wikipedia.org/api/rest_v1/page/summary/" + Uri.EscapeDataString(pagina.Replace(' ', '_')), 8000) as Dictionary<string, object>;
            if (d == null || S(d, "type") == "disambiguation") continue;
            if (!m.ContainsKey("sinopsi") && S(d, "extract") != "") m["sinopsi"] = S(d, "extract");
            Dictionary<string, object> img = d.ContainsKey("originalimage") ? d["originalimage"] as Dictionary<string, object> : (d.ContainsKey("thumbnail") ? d["thumbnail"] as Dictionary<string, object> : null);
            if (!m.ContainsKey("poster") && S(img, "source") != "") m["poster"] = S(img, "source");
            if (!m.ContainsKey("fontNom")) { m["titol"] = Regex.Replace(S(d, "title"), @"\s*\([^)]*\)\s*$", ""); m["fontNom"] = "Viquip\u00e8dia"; }
            if (m.ContainsKey("poster") && m.ContainsKey("sinopsi")) return;
        }
    }

    static Dictionary<string, object> CercaPeli(List<string[]> cands)
    {
        Dictionary<string, object> m = new Dictionary<string, object>();
        string titol = cands.Count > 0 ? cands[0][0] : "", any = cands.Count > 0 ? cands[0][1] : "";
        m["titol"] = titol; m["any"] = any;
        if (!Internet() || titol == "") return m;
        string tmdb = A("tmdb", "");
        List<string> errors = new List<string>();
        foreach (string[] c in cands)
        {
            titol = c[0]; any = c[1];
            try
            {
                if (tmdb != "")
                {
                    string ll = Llengua();
                    foreach (string llengua in new string[] { ll + "-" + ll.ToUpperInvariant(), "es-ES", "en-US" })
                    {
                        Dictionary<string, object> r = JsonDe("https://api.themoviedb.org/3/search/movie?api_key=" + Uri.EscapeDataString(tmdb) + "&language=" + llengua + "&query=" + Uri.EscapeDataString(titol) + (any != "" ? "&year=" + any : ""), 8000) as Dictionary<string, object>;
                        object[] res = r != null && r.ContainsKey("results") ? r["results"] as object[] : null;
                        if ((res == null || res.Length == 0) && any != "") { r = JsonDe("https://api.themoviedb.org/3/search/movie?api_key=" + Uri.EscapeDataString(tmdb) + "&language=" + llengua + "&query=" + Uri.EscapeDataString(titol), 8000) as Dictionary<string, object>; res = r != null && r.ContainsKey("results") ? r["results"] as object[] : null; }
                        if (res == null || res.Length == 0) break;
                        Dictionary<string, object> p = res[0] as Dictionary<string, object>;
                        m["titol"] = S(p, "title"); m["original"] = S(p, "original_title"); m["fontNom"] = "TMDB";
                        string data = S(p, "release_date"); if (data.Length >= 4) m["any"] = data.Substring(0, 4);
                        if (S(p, "overview") != "" && !m.ContainsKey("sinopsi")) m["sinopsi"] = S(p, "overview");
                        if (S(p, "poster_path") != "") m["poster"] = "https://image.tmdb.org/t/p/w500" + S(p, "poster_path");
                        if (S(p, "backdrop_path") != "") m["fons"] = "https://image.tmdb.org/t/p/w1280" + S(p, "backdrop_path");
                        double nota; if (double.TryParse(S(p, "vote_average"), NumberStyles.Any, CultureInfo.InvariantCulture, out nota) && nota > 0) m["nota"] = Math.Round(nota, 1).ToString(CultureInfo.InvariantCulture);
                        if (m.ContainsKey("sinopsi")) break;
                    }
                }
                if (!m.ContainsKey("poster")) DeItunes(m, titol, any);
                if (!m.ContainsKey("poster") || !m.ContainsKey("sinopsi")) DeWikipedia(m, titol, any);
            }
            catch (Exception e) { errors.Add(e.Message); }
            if (m.ContainsKey("poster")) break;
        }
        try
        {
            string omdb = A("omdb", "");
            if (omdb != "" && m.ContainsKey("fontNom"))
            {
                string t = m.ContainsKey("original") ? S(m, "original") : S(m, "titol");
                Dictionary<string, object> o = JsonDe("https://www.omdbapi.com/?apikey=" + Uri.EscapeDataString(omdb) + "&t=" + Uri.EscapeDataString(t) + (S(m, "any") != "" ? "&y=" + S(m, "any") : ""), 8000) as Dictionary<string, object>;
                if (o != null && S(o, "Response") == "True")
                {
                    if (S(o, "imdbRating") != "N/A") m["imdb"] = S(o, "imdbRating");
                    object[] rs = o.ContainsKey("Ratings") ? o["Ratings"] as object[] : null;
                    if (rs != null) foreach (object x in rs) { Dictionary<string, object> rr = x as Dictionary<string, object>; if (S(rr, "Source") == "Rotten Tomatoes") m["rotten"] = S(rr, "Value"); }
                    if (!m.ContainsKey("poster") && S(o, "Poster").StartsWith("http")) m["poster"] = S(o, "Poster");
                    if (!m.ContainsKey("genere") || S(m, "genere") == "") m["genere"] = S(o, "Genre");
                    if (!m.ContainsKey("minuts")) m["minuts"] = S(o, "Runtime").Replace(" min", "");
                    m["director"] = S(o, "Director");
                }
            }
        }
        catch (Exception e) { errors.Add(e.Message); }
        if (!m.ContainsKey("fontNom"))
        {
            List<string> provats = new List<string>();
            foreach (string[] c in cands) provats.Add("\u00ab" + c[0] + "\u00bb" + (c[1] != "" ? " (" + c[1] + ")" : ""));
            m["avis"] = "No l'he trobada. He cercat " + string.Join(", ", provats.ToArray()) + (errors.Count > 0 ? ". Error: " + errors[0] : "");
            if (errors.Count > 0) m["error"] = errors[0];
            if (cands.Count > 0) { m["titol"] = cands[0][0]; m["any"] = cands[0][1]; }
        }
        return m;
    }

    static readonly Dictionary<string, DateTime> noTrobades = new Dictionary<string, DateTime>();

    static string FontActual() { return "4" + (A("tmdb", "") != "" ? "t" : "") + (A("omdb", "") != "" ? "o" : "") + (Internet() ? "i" : "") + Llengua(); }

    // Fitxa ja desada (sense anar a internet); null si encara no se n'ha buscat cap
    internal static Dictionary<string, object> MetaEnCau(string rel)
    {
        string font = FontActual();
        lock (lk)
        {
            CarregaMetes();
            object v;
            if (metes.TryGetValue(rel, out v) && v is Dictionary<string, object> && S((Dictionary<string, object>)v, "font") == font) return (Dictionary<string, object>)v;
        }
        return null;
    }

    // Titols corregits a ma (des de la fitxa de la pel.licula)
    static Dictionary<string, object> titols;
    static string FitxerTitols { get { return Path.Combine(CasaServidor.DirDades, "pelis-titols.json"); } }
    static Dictionary<string, object> Titols()
    {
        if (titols == null)
        {
            try { titols = File.Exists(FitxerTitols) ? new JavaScriptSerializer().DeserializeObject(File.ReadAllText(FitxerTitols, Encoding.UTF8)) as Dictionary<string, object> : null; } catch { }
            if (titols == null) titols = new Dictionary<string, object>();
        }
        return titols;
    }
    internal static void CorregeixTitol(string rel, string titol, string any)
    {
        lock (lk)
        {
            Dictionary<string, object> t = Titols();
            if (string.IsNullOrEmpty(titol)) t.Remove(rel);
            else { Dictionary<string, object> x = new Dictionary<string, object>(); x["t"] = titol; x["a"] = any ?? ""; t[rel] = x; }
            File.WriteAllText(FitxerTitols, new JavaScriptSerializer().Serialize(t), new UTF8Encoding(false));
            CarregaMetes();
            metes.Remove(rel);
            noTrobades.Clear();
            DesaMetes();
        }
    }

    internal static Dictionary<string, object> Fitxa(string rel)
    {
        string full = CasaServidor.Ruta("pelis", rel);
        if (full == null || (!File.Exists(full) && !Directory.Exists(full))) return null;
        string font = FontActual();
        lock (lk)
        {
            CarregaMetes();
            object v;
            if (metes.TryGetValue(rel, out v) && v is Dictionary<string, object> && S((Dictionary<string, object>)v, "font") == font) return (Dictionary<string, object>)v;
            DateTime quan;
            if (noTrobades.TryGetValue(rel + "|" + font, out quan) && (DateTime.Now - quan).TotalMinutes < 30 && metes.TryGetValue(rel + "|no", out v)) return (Dictionary<string, object>)v;
        }
        List<string[]> cands = Candidats(full);
        object tc;
        lock (lk) { Titols().TryGetValue(rel, out tc); }
        Dictionary<string, object> tcd = tc as Dictionary<string, object>;
        if (tcd != null) cands.Insert(0, new string[] { S(tcd, "t"), S(tcd, "a") });
        Dictionary<string, object> m = CercaPeli(cands);
        m["font"] = font;
        lock (lk)
        {
            if (!m.ContainsKey("error") && m.ContainsKey("fontNom")) { metes[rel] = m; DesaMetes(); }
            else if (!m.ContainsKey("error")) { metes[rel] = m; DesaMetes(); }
            else { noTrobades[rel + "|" + font] = DateTime.Now; metes[rel + "|no"] = m; }
        }
        return m;
    }

    static string PosterLocal(string rel)
    {
        string full = CasaServidor.Ruta("pelis", rel);
        if (full == null) return null;
        if (Directory.Exists(full))
        {
            string ic = ImatgeDeCarpeta(new DirectoryInfo(full));
            if (ic != null) return ic;
            int n;
            FileInfo p = Pelis.Principal(new DirectoryInfo(full), out n);
            if (p == null) return null;
            full = p.FullName;
        }
        string dir = Path.GetDirectoryName(full), b = Path.GetFileNameWithoutExtension(full);
        foreach (string suf in new string[] { "", "-poster", ".poster", "-cover" })
            foreach (string e in ExtPortada) { string f = Path.Combine(dir, b + suf + e); if (File.Exists(f)) return f; }
        return null;
    }

    // Un fotograma del video (al 12% de la durada) per a les pel.licules sense caratula
    static string Fotograma(string rel)
    {
        string full = CasaServidor.Ruta("pelis", rel);
        if (full == null || !File.Exists(full)) return null;
        string dir = Path.Combine(CasaServidor.DirDades, "fotogrames");
        Directory.CreateDirectory(dir);
        string f = Path.Combine(dir, CasaServidor.Hash(rel) + ".jpg");
        if (File.Exists(f)) return f;
        string ff = SoPC.RutaFfmpeg();
        if (ff == null) return null;
        int d = Pelis.Durada(rel);
        int seg = d > 0 ? Math.Max(5, d * 12 / 100) : 60;
        try
        {
            System.Diagnostics.ProcessStartInfo si = new System.Diagnostics.ProcessStartInfo(ff, "-nostdin -loglevel error -ss " + seg + " -i \"" + full + "\" -frames:v 1 -vf scale=480:-2 -q:v 4 -y \"" + f + "\"");
            si.UseShellExecute = false; si.CreateNoWindow = true;
            using (System.Diagnostics.Process p = System.Diagnostics.Process.Start(si)) { if (!p.WaitForExit(20000)) { try { p.Kill(); } catch { } } }
        }
        catch { }
        return File.Exists(f) ? f : null;
    }

    public static void Api(string nom, Dictionary<string, string> q, HttpListenerContext ctx)
    {
        HttpListenerResponse res = ctx.Response;
        int m;
        if (!int.TryParse(CasaServidor.Q(q, "m"), out m)) m = 300;
        m = Math.Max(40, Math.Min(1600, m));
        try
        {
            switch (nom)
            {
                case "imgx": EnviaImatge(ctx, ImatgeExterna(CasaServidor.Q(q, "u"), 3500), m); return;
                case "portada": EnviaImatge(ctx, Portada(CasaServidor.Q(q, "c"), CasaServidor.Q(q, "p")), m); return;
                case "caratula":
                    {
                        // Nomes la caratula de la fitxa (no les imatges que hi hagi a la carpeta); si no n'hi ha, un fotograma del video
                        string rel = CasaServidor.Q(q, "p"), f = null;
                        Dictionary<string, object> fx = CasaServidor.Q(q, "cau") == "1" ? MetaEnCau(rel) : Fitxa(rel);
                        string camp = CasaServidor.Q(q, "fons") == "1" ? "fons" : "poster";
                        if (fx != null && fx.ContainsKey(camp)) f = ImatgeExterna(fx[camp].ToString(), 8000);
                        if (f == null && camp == "poster") f = Fotograma(rel);
                        EnviaImatge(ctx, f, m);
                        return;
                    }
                case "peli-titol":
                    {
                        CorregeixTitol(CasaServidor.Q(q, "p"), CasaServidor.Q(q, "t").Trim(), CasaServidor.Q(q, "a").Trim());
                        Dictionary<string, object> fx = Fitxa(CasaServidor.Q(q, "p"));
                        if (fx == null) { CasaServidor.Error(res, 404, "No trobat"); return; }
                        CasaServidor.Json(res, fx);
                        return;
                    }
                case "peli":
                    {
                        Dictionary<string, object> fx = Fitxa(CasaServidor.Q(q, "p"));
                        if (fx == null) { CasaServidor.Error(res, 404, "No trobat"); return; }
                        CasaServidor.Json(res, fx);
                        return;
                    }
            }
            CasaServidor.Error(res, 404, "API desconeguda");
        }
        catch (Exception e)
        {
            try { CasaServidor.Error(res, 500, e.Message); } catch { }
        }
    }
}
