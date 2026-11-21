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
        string[] x = Romans(Pla(a)).Split(' '), y = Romans(Pla(b)).Split(' ');
        if (x.Length == 0 || y.Length == 0) return 0;
        int comuns = 0;
        foreach (string w in x) if (w != "" && Array.IndexOf(y, w) >= 0) comuns++;
        return 2.0 * comuns / (x.Length + y.Length);
    }

    static string Romans(string s) { return Regex.Replace(" " + s + " ", @" (ii|iii|iv) ", delegate (Match m) { string v = m.Groups[1].Value; return v == "ii" ? " 2 " : v == "iii" ? " 3 " : " 4 "; }).Trim(); }

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
                        double pm = -1;
                        for (int ri = 0; ri < res.Length && ri < 6; ri++)
                        {
                            Dictionary<string, object> pr = res[ri] as Dictionary<string, object>;
                            double sc = Math.Max(Semblanca(titol, S(pr, "title")), Semblanca(titol, S(pr, "original_title"))) - ri * 0.03;
                            if (any != "" && S(pr, "release_date").StartsWith(any)) sc += 0.3;
                            if (sc > pm) { pm = sc; p = pr; }
                        }
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

    // ---------- Reconeixement: confianca, opcions i triar a ma ----------
    static readonly Dictionary<string, string> GenTmdb = new Dictionary<string, string> {
        {"28","Action"},{"12","Adventure"},{"16","Animation"},{"35","Comedy"},{"80","Crime"},{"99","Documentary"},{"18","Drama"},
        {"10751","Family"},{"14","Fantasy"},{"36","History"},{"27","Horror"},{"10402","Music"},{"9648","Mystery"},{"10749","Romance"},
        {"878","Science Fiction"},{"10770","TV Movie"},{"53","Thriller"},{"10752","War"},{"37","Western"} };

    // 0..1: com s'assembla la fitxa trobada al nom del fitxer o carpeta
    internal static double Confianca(List<string[]> cands, Dictionary<string, object> m)
    {
        if (m == null || !m.ContainsKey("fontNom")) return 0;
        if (m.ContainsKey("confirmada")) return 1;
        double millor = 0;
        foreach (string[] c in cands)
        {
            double s = Math.Max(Semblanca(c[0], S(m, "titol")), S(m, "original") != "" ? Semblanca(c[0], S(m, "original")) : 0);
            int a1, a2;
            if (c[1] != "" && int.TryParse(c[1], out a1) && int.TryParse(S(m, "any"), out a2))
            {
                if (a1 == a2) s += 0.25; else if (Math.Abs(a1 - a2) <= 1) s += 0.1; else s -= 0.3;
            }
            if (s > millor) millor = s;
        }
        return Math.Max(0, Math.Min(1, millor));
    }

    // Per a la llista: la confianca d'una fitxa ja desada (es calcula un cop i es guarda)
    internal static double ConfiancaEnCau(string rel, Dictionary<string, object> m)
    {
        double c;
        if (m.ContainsKey("conf") && double.TryParse(S(m, "conf"), NumberStyles.Any, CultureInfo.InvariantCulture, out c)) return c;
        if (!m.ContainsKey("fontNom")) return 0;
        string full = CasaServidor.Ruta("pelis", rel);
        if (full == null) return 1;
        c = Confianca(CandidatsAmbTitol(rel, full), m);
        lock (lk) { m["conf"] = Math.Round(c, 2).ToString(CultureInfo.InvariantCulture); }
        return c;
    }

    static List<string[]> CandidatsAmbTitol(string rel, string full)
    {
        List<string[]> cands = Candidats(full);
        object tc;
        lock (lk) { Titols().TryGetValue(rel, out tc); }
        Dictionary<string, object> tcd = tc as Dictionary<string, object>;
        if (tcd != null && S(tcd, "t") != "") cands.Insert(0, new string[] { S(tcd, "t"), S(tcd, "a") });
        return cands;
    }

    static readonly Dictionary<string, Dictionary<string, object>> opcionsCau = new Dictionary<string, Dictionary<string, object>>();

    static void AfegeixOpcio(List<Dictionary<string, object>> l, Dictionary<string, object> o, string titol, string any)
    {
        if (S(o, "titol") == "") return;
        double s = Math.Max(Semblanca(titol, S(o, "titol")), S(o, "original") != "" ? Semblanca(titol, S(o, "original")) : 0);
        int a1, a2;
        if (any != "" && int.TryParse(any, out a1) && int.TryParse(S(o, "any"), out a2)) { if (a1 == a2) s += 0.3; else if (Math.Abs(a1 - a2) <= 1) s += 0.15; }
        if (S(o, "poster") != "") s += 0.05;
        string clau = Pla(S(o, "titol")) + "|" + S(o, "any");
        foreach (Dictionary<string, object> x in l)
            if (Pla(S(x, "titol")) + "|" + S(x, "any") == clau)
            {
                // la mateixa pel.licula des d'una altra font: completa el que falti
                foreach (string k in new string[] { "poster", "fons", "sinopsi", "nota", "genere", "minuts", "edat", "original" }) if (S(x, k) == "" && S(o, k) != "") x[k] = o[k];
                return;
            }
        o["punts"] = Math.Round(s, 3);
        l.Add(o);
        lock (opcionsCau) opcionsCau[S(o, "id")] = o;
    }

    // Variants d'un titol per buscar mes: "El rei lleo 2" -> tambe "El rei lleo II" i "El rei lleo"
    static List<string> Variants(string t)
    {
        List<string> l = new List<string>(); l.Add(t);
        string[] ar = { "1", "2", "3", "4", "5", "6" }, ro = { "I", "II", "III", "IV", "V", "VI" };
        Match m = Regex.Match(t, @"^(.*\S)\s+(\d|I{1,3}|IV|V|VI)$", RegexOptions.IgnoreCase);
        if (m.Success)
        {
            string b = m.Groups[1].Value, n = m.Groups[2].Value.ToUpperInvariant();
            int ia = Array.IndexOf(ar, n), ir = Array.IndexOf(ro, n);
            if (ia >= 0) l.Add(b + " " + ro[ia]); else if (ir >= 0) l.Add(b + " " + ar[ir]);
            l.Add(b);
        }
        return l;
    }

    static void OpTmdb(List<Dictionary<string, object>> l, string q, string titol, string any, string llengua)
    {
        string tmdb = A("tmdb", "");
        if (tmdb == "") return;
        Dictionary<string, object> r = JsonDe("https://api.themoviedb.org/3/search/movie?api_key=" + Uri.EscapeDataString(tmdb) + "&language=" + llengua + "&query=" + Uri.EscapeDataString(q), 8000) as Dictionary<string, object>;
        object[] res = r != null && r.ContainsKey("results") ? r["results"] as object[] : null;
        if (res == null) return;
        for (int i = 0; i < res.Length && i < 10; i++)
        {
            Dictionary<string, object> p = res[i] as Dictionary<string, object>, o = new Dictionary<string, object>();
            o["id"] = "tmdb:" + S(p, "id"); o["fontNom"] = "TMDB";
            o["titol"] = S(p, "title"); o["original"] = S(p, "original_title");
            if (S(p, "release_date").Length >= 4) o["any"] = S(p, "release_date").Substring(0, 4);
            if (S(p, "overview") != "") o["sinopsi"] = S(p, "overview");
            if (S(p, "poster_path") != "") o["poster"] = "https://image.tmdb.org/t/p/w500" + S(p, "poster_path");
            if (S(p, "backdrop_path") != "") o["fons"] = "https://image.tmdb.org/t/p/w1280" + S(p, "backdrop_path");
            double nota; if (double.TryParse(S(p, "vote_average"), NumberStyles.Any, CultureInfo.InvariantCulture, out nota) && nota > 0) o["nota"] = Math.Round(nota, 1).ToString(CultureInfo.InvariantCulture);
            object[] gs = p.ContainsKey("genre_ids") ? p["genre_ids"] as object[] : null;
            if (gs != null) { List<string> g = new List<string>(); foreach (object x in gs) { string gn; if (x != null && GenTmdb.TryGetValue(x.ToString(), out gn)) g.Add(gn); } o["genere"] = string.Join(", ", g.ToArray()); }
            lock (l) AfegeixOpcio(l, o, titol, any);
        }
    }

    static Dictionary<string, object> ItunesHit(Dictionary<string, object> p)
    {
        Dictionary<string, object> o = new Dictionary<string, object>();
        o["id"] = "itunes:" + S(p, "trackId"); o["fontNom"] = "iTunes";
        o["titol"] = S(p, "trackName");
        if (S(p, "releaseDate").Length >= 4) o["any"] = S(p, "releaseDate").Substring(0, 4);
        if (S(p, "longDescription") != "") o["sinopsi"] = S(p, "longDescription");
        if (S(p, "artworkUrl100") != "") o["poster"] = S(p, "artworkUrl100").Replace("100x100bb", "600x600bb");
        o["genere"] = S(p, "primaryGenreName"); o["edat"] = S(p, "contentAdvisoryRating");
        long ms; if (long.TryParse(S(p, "trackTimeMillis"), out ms)) o["minuts"] = (ms / 60000).ToString();
        return o;
    }

    static object[] ItunesCerca(string q, string pais, int limit)
    {
        Dictionary<string, object> r = JsonDe("https://itunes.apple.com/search?media=movie&entity=movie&limit=" + limit + "&country=" + pais + "&term=" + Uri.EscapeDataString(q), 8000) as Dictionary<string, object>;
        return r != null && r.ContainsKey("results") ? r["results"] as object[] : null;
    }

    static void OpItunes(List<Dictionary<string, object>> l, string q, string titol, string any, string pais)
    {
        object[] res = ItunesCerca(q, pais, 10);
        if (res == null) return;
        foreach (object x in res) { Dictionary<string, object> o = ItunesHit(x as Dictionary<string, object>); lock (l) AfegeixOpcio(l, o, titol, any); }
    }

    static void OpWikipedia(List<Dictionary<string, object>> l, string q, string titol, string any, string wl)
    {
        string paraula = wl == "ca" ? "pel\u00b7l\u00edcula" : wl == "es" ? "pel\u00edcula" : wl == "pt" ? "filme" : "film";
        Dictionary<string, object> r = JsonDe("https://" + wl + ".wikipedia.org/w/api.php?action=query&list=search&format=json&srlimit=5&srsearch=" + Uri.EscapeDataString(q + " " + paraula), 8000) as Dictionary<string, object>;
        Dictionary<string, object> qd = r != null && r.ContainsKey("query") ? r["query"] as Dictionary<string, object> : null;
        object[] res = qd != null && qd.ContainsKey("search") ? qd["search"] as object[] : null;
        if (res == null) return;
        foreach (object x in res)
        {
            string t = S(x as Dictionary<string, object>, "title");
            if (!Regex.IsMatch(t + " " + S(x as Dictionary<string, object>, "snippet"), @"(film|pel|filme|movie)", RegexOptions.IgnoreCase) && Semblanca(titol, t) < 0.5) continue;
            Dictionary<string, object> d = JsonDe("https://" + wl + ".wikipedia.org/api/rest_v1/page/summary/" + Uri.EscapeDataString(t.Replace(' ', '_')), 8000) as Dictionary<string, object>;
            if (d == null || S(d, "type") == "disambiguation") continue;
            Dictionary<string, object> o = new Dictionary<string, object>();
            o["id"] = "wiki:" + wl + ":" + t; o["fontNom"] = "Viquip\u00e8dia";
            o["titol"] = Regex.Replace(t, @"\s*\([^)]*\)\s*$", "");
            Match ma = Regex.Match(t + " " + S(d, "description") + " " + S(d, "extract"), @"(19[2-9]\d|20[0-4]\d)"); if (ma.Success) o["any"] = ma.Value;
            if (S(d, "extract") != "") o["sinopsi"] = S(d, "extract");
            Dictionary<string, object> img = d.ContainsKey("originalimage") ? d["originalimage"] as Dictionary<string, object> : (d.ContainsKey("thumbnail") ? d["thumbnail"] as Dictionary<string, object> : null);
            if (S(img, "source") != "") o["poster"] = S(img, "source");
            lock (l) AfegeixOpcio(l, o, titol, any);
        }
    }

    static readonly string[] ClassesFilm = { "Q11424", "Q202866", "Q24869", "Q506240", "Q1257444", "Q29168811", "Q17517379", "Q229390", "Q24862", "Q1261214" };

    // Wikidata: totes les pel.licules amb aquest nom en qualsevol idioma; dona el titol en angles per buscar la caratula a iTunes
    static void OpWikidata(List<Dictionary<string, object>> l, string q, string titol, string any, string ll)
    {
        Dictionary<string, object> r = JsonDe("https://www.wikidata.org/w/api.php?action=wbsearchentities&format=json&type=item&limit=12&language=" + ll + "&uselang=" + ll + "&search=" + Uri.EscapeDataString(q), 8000) as Dictionary<string, object>;
        object[] res = r != null && r.ContainsKey("search") ? r["search"] as object[] : null;
        if (res == null || res.Length == 0) return;
        List<string> ids = new List<string>();
        foreach (object x in res) ids.Add(S(x as Dictionary<string, object>, "id"));
        Dictionary<string, object> e = JsonDe("https://www.wikidata.org/w/api.php?action=wbgetentities&format=json&props=labels|claims&languages=" + ll + "|en|es|ca|pt|fr&ids=" + string.Join("|", ids.ToArray()), 10000) as Dictionary<string, object>;
        Dictionary<string, object> ents = e != null && e.ContainsKey("entities") ? e["entities"] as Dictionary<string, object> : null;
        if (ents == null) return;
        int n = 0;
        foreach (string id in ids)
        {
            Dictionary<string, object> it = ents.ContainsKey(id) ? ents[id] as Dictionary<string, object> : null;
            if (it == null) continue;
            Dictionary<string, object> claims = it.ContainsKey("claims") ? it["claims"] as Dictionary<string, object> : null;
            if (claims == null || !claims.ContainsKey("P31")) continue;
            bool film = false;
            foreach (object c in (object[])claims["P31"])
            {
                try { string cl = S(((((Dictionary<string, object>)c)["mainsnak"] as Dictionary<string, object>)["datavalue"] as Dictionary<string, object>)["value"] as Dictionary<string, object>, "id"); if (Array.IndexOf(ClassesFilm, cl) >= 0) film = true; } catch { }
            }
            if (!film) continue;
            string anyW = "";
            if (claims.ContainsKey("P577"))
                foreach (object c in (object[])claims["P577"])
                {
                    try
                    {
                        Dictionary<string, object> v = (((Dictionary<string, object>)c)["mainsnak"] as Dictionary<string, object>)["datavalue"] as Dictionary<string, object>;
                        Match ma = Regex.Match(S(v["value"] as Dictionary<string, object>, "time"), @"(\d{4})");
                        if (ma.Success && (anyW == "" || string.CompareOrdinal(ma.Value, anyW) < 0)) anyW = ma.Value;
                    }
                    catch { }
                }
            Dictionary<string, object> labels = it.ContainsKey("labels") ? it["labels"] as Dictionary<string, object> : null;
            Func<string, string> lab = delegate (string lg) { return labels != null && labels.ContainsKey(lg) ? S(labels[lg] as Dictionary<string, object>, "value") : ""; };
            string local = lab(ll); if (local == "") local = lab("es"); if (local == "") local = lab("en");
            string en = lab("en");
            if (local == "") continue;
            Dictionary<string, object> o = new Dictionary<string, object>();
            o["id"] = "wd:" + id; o["fontNom"] = "Wikidata";
            o["titol"] = local; if (en != "" && en != local) o["original"] = en;
            if (anyW != "") o["any"] = anyW;
            // la caratula i la sinopsi, d'iTunes amb el titol angles (o el local)
            if (n++ < 5)
                try
                {
                    string qq = en != "" ? en : local;
                    object[] ir = ItunesCerca(qq, en != "" ? "us" : A("paisItunes", "es"), 8);
                    Dictionary<string, object> millor = null; double pm = 0.5;
                    if (ir != null)
                        foreach (object x in ir)
                        {
                            Dictionary<string, object> p = x as Dictionary<string, object>;
                            double sc = Semblanca(qq, S(p, "trackName"));
                            if (anyW != "" && S(p, "releaseDate").StartsWith(anyW)) sc += 0.4;
                            if (sc > pm) { pm = sc; millor = p; }
                        }
                    if (millor != null)
                    {
                        Dictionary<string, object> h = ItunesHit(millor);
                        foreach (string k in new string[] { "poster", "sinopsi", "genere", "edat", "minuts" }) if (S(h, k) != "") o[k] = h[k];
                        o["fontNom"] = "Wikidata + iTunes";
                    }
                }
                catch { }
            lock (l) AfegeixOpcio(l, o, titol, any);
        }
    }

    // Les pel.licules que s'assemblen a un titol, de moltes fonts i idiomes alhora, per triar-ne una
    internal static List<Dictionary<string, object>> Opcions(List<string[]> cands, bool complet)
    {
        List<Dictionary<string, object>> l = new List<Dictionary<string, object>>();
        if (!Internet()) return l;
        string ll = Llengua(), pais = A("paisItunes", "es");
        List<string> llengues = new List<string>(); foreach (string x in (complet ? new string[] { ll, "es", "en", "pt" } : new string[] { ll })) if (!llengues.Contains(x)) llengues.Add(x);
        List<string> paisos = new List<string>(); foreach (string x in (complet ? new string[] { pais, "us", "pt", "es" } : new string[] { pais, "us" })) if (!paisos.Contains(x)) paisos.Add(x);
        List<Action> feina = new List<Action>();
        int n = 0;
        foreach (string[] c in cands)
        {
            if (++n > 2) break;
            string titol = c[0], any = c[1];
            if (titol.Length < 2) continue;
            List<string> vs = Variants(titol);
            for (int vi = 0; vi < vs.Count; vi++)
            {
                string q = vs[vi];
                bool principal = vi == 0;
                foreach (string lg in llengues) { string lg2 = lg; if (principal || lg2 == ll) feina.Add(delegate { OpTmdb(l, q, titol, any, lg2 + "-" + lg2.ToUpperInvariant()); }); }
                foreach (string ps in paisos) { string ps2 = ps; if (principal || ps2 == pais) feina.Add(delegate { OpItunes(l, q, titol, any, ps2); }); }
                foreach (string lg in (complet ? new string[] { ll, "en" } : new string[] { ll })) { string lg2 = lg; feina.Add(delegate { OpWikidata(l, q, titol, any, lg2); }); }
                if (principal) foreach (string lg in llengues) { string lg2 = lg; if (lg2 != "pt") feina.Add(delegate { OpWikipedia(l, q, titol, any, lg2); }); }
            }
        }
        // totes alhora (el limit de connexions simultanies el posa Baixa)
        // (no es fa Dispose: alguna feina lenta podria acabar despres d'haver deixat d'esperar)
        CountdownEvent fet = new CountdownEvent(feina.Count);
        foreach (Action a in feina)
        {
            Action a2 = a;
            ThreadPool.QueueUserWorkItem(delegate { try { a2(); } catch { } try { fet.Signal(); } catch { } });
        }
        fet.Wait(complet ? 40000 : 20000);
        List<Dictionary<string, object>> r;
        lock (l)
        {
            r = new List<Dictionary<string, object>>(l);
        }
        r.Sort(delegate (Dictionary<string, object> a, Dictionary<string, object> b) { return Convert.ToDouble(b["punts"]).CompareTo(Convert.ToDouble(a["punts"])); });
        if (r.Count > 24) r.RemoveRange(24, r.Count - 24);
        return r;
    }

    // Desa l'opcio triada a ma: d'ara endavant aquesta pel.licula fa servir aquesta fitxa
    internal static Dictionary<string, object> Tria(string rel, string id)
    {
        Dictionary<string, object> o;
        if (id == "actual")
        {
            // "si, es aquesta": la fitxa que ja tenia queda confirmada
            o = Fitxa(rel);
            if (o == null || !o.ContainsKey("fontNom")) return null;
            o = new Dictionary<string, object>(o);
            foreach (string k in new string[] { "font", "conf", "confirmada", "avis", "error", "imdb", "rotten", "director" }) o.Remove(k);
        }
        else lock (opcionsCau) opcionsCau.TryGetValue(id, out o);
        if (o == null) return null;
        Dictionary<string, object> f = new Dictionary<string, object>();
        foreach (KeyValuePair<string, object> kv in o) if (kv.Key != "punts" && kv.Key != "id") f[kv.Key] = kv.Value;
        lock (lk)
        {
            Dictionary<string, object> t = Titols();
            Dictionary<string, object> x = new Dictionary<string, object>();
            x["t"] = S(o, "titol"); x["a"] = S(o, "any"); x["tria"] = f;
            // el que hi havia abans, per poder desfer-ho
            object ab; t.TryGetValue(rel, out ab);
            Dictionary<string, object> abd = ab as Dictionary<string, object>;
            if (abd != null) { abd = new Dictionary<string, object>(abd); abd.Remove("abans"); x["abans"] = abd; } else x["abans"] = "";
            t[rel] = x;
            File.WriteAllText(FitxerTitols, new JavaScriptSerializer().Serialize(t), new UTF8Encoding(false));
            CarregaMetes();
            metes.Remove(rel);
            noTrobades.Clear();
            DesaMetes();
        }
        return Fitxa(rel);
    }

    // Torna a com estava abans de la darrera tria
    internal static Dictionary<string, object> Desfes(string rel)
    {
        lock (lk)
        {
            Dictionary<string, object> t = Titols();
            object v; t.TryGetValue(rel, out v);
            Dictionary<string, object> x = v as Dictionary<string, object>;
            if (x == null) return null;
            Dictionary<string, object> ab = x.ContainsKey("abans") ? x["abans"] as Dictionary<string, object> : null;
            if (ab != null) t[rel] = ab; else t.Remove(rel);
            File.WriteAllText(FitxerTitols, new JavaScriptSerializer().Serialize(t), new UTF8Encoding(false));
            CarregaMetes();
            metes.Remove(rel);
            noTrobades.Clear();
            DesaMetes();
        }
        return Fitxa(rel);
    }

    // Afegeix la nota d'IMDb i el director (si hi ha clau d'OMDb)
    static void DeOmdb(Dictionary<string, object> m)
    {
        try
        {
            string omdb = A("omdb", "");
            if (omdb == "" || !m.ContainsKey("fontNom") || !Internet()) return;
            string t = m.ContainsKey("original") && S(m, "original") != "" ? S(m, "original") : S(m, "titol");
            Dictionary<string, object> o = JsonDe("https://www.omdbapi.com/?apikey=" + Uri.EscapeDataString(omdb) + "&t=" + Uri.EscapeDataString(t) + (S(m, "any") != "" ? "&y=" + S(m, "any") : ""), 8000) as Dictionary<string, object>;
            if (o == null || S(o, "Response") != "True") return;
            if (S(o, "imdbRating") != "N/A") m["imdb"] = S(o, "imdbRating");
            object[] rs = o.ContainsKey("Ratings") ? o["Ratings"] as object[] : null;
            if (rs != null) foreach (object x in rs) { Dictionary<string, object> rr = x as Dictionary<string, object>; if (S(rr, "Source") == "Rotten Tomatoes") m["rotten"] = S(rr, "Value"); }
            if (!m.ContainsKey("poster") && S(o, "Poster").StartsWith("http")) m["poster"] = S(o, "Poster");
            if (!m.ContainsKey("genere") || S(m, "genere") == "") m["genere"] = S(o, "Genre");
            if (!m.ContainsKey("minuts") || S(m, "minuts") == "") m["minuts"] = S(o, "Runtime").Replace(" min", "");
            m["director"] = S(o, "Director");
        }
        catch { }
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
        Dictionary<string, object> m;
        Dictionary<string, object> triada = tcd != null && tcd.ContainsKey("tria") ? tcd["tria"] as Dictionary<string, object> : null;
        if (triada != null)
        {
            // triada a ma entre les opcions: es fa servir tal qual
            m = new Dictionary<string, object>(triada);
            m["confirmada"] = true;
            m["triada"] = true;
            DeOmdb(m);
        }
        else
        {
            if (tcd != null) cands.Insert(0, new string[] { S(tcd, "t"), S(tcd, "a") });
            m = CercaPeli(cands);
            // un titol escrit a ma i trobat compta com a confirmat
            if (tcd != null && m.ContainsKey("fontNom") && Math.Max(Semblanca(S(tcd, "t"), S(m, "titol")), Semblanca(S(tcd, "t"), S(m, "original"))) >= 0.6) m["confirmada"] = true;
        }
        m["conf"] = Math.Round(Confianca(cands, m), 2).ToString(CultureInfo.InvariantCulture);
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
                case "peli-opcions":
                    {
                        string rel = CasaServidor.Q(q, "p"), full = CasaServidor.Ruta("pelis", rel), txt = CasaServidor.Q(q, "q").Trim();
                        if (full == null) { CasaServidor.Error(res, 404, "No trobat"); return; }
                        List<string[]> cands;
                        if (txt != "")
                        {
                            Match ma = Regex.Match(txt, @"^(.*?)\s*\(?((?:19|20)\d\d)\)?$");
                            cands = new List<string[]>();
                            cands.Add(ma.Success && ma.Groups[1].Value.Trim() != "" ? new string[] { ma.Groups[1].Value.Trim(), ma.Groups[2].Value } : new string[] { txt, "" });
                        }
                        else cands = CandidatsAmbTitol(rel, full);
                        Dictionary<string, object> o = new Dictionary<string, object>();
                        List<object> cs = new List<object>(); foreach (string[] c in cands) cs.Add(c[0] + (c[1] != "" ? " " + c[1] : ""));
                        o["cerca"] = cs; o["opcions"] = Opcions(cands, CasaServidor.Q(q, "rapid") != "1"); o["fitxer"] = Path.GetFileName(full.TrimEnd('\\', '/'));
                        CasaServidor.Json(res, o);
                        return;
                    }
                case "peli-desfes":
                    {
                        Dictionary<string, object> fx = Desfes(CasaServidor.Q(q, "p"));
                        if (fx == null) { CasaServidor.Error(res, 404, "No hi ha res per desfer"); return; }
                        CasaServidor.Json(res, fx);
                        return;
                    }
                case "peli-tria":
                    {
                        Dictionary<string, object> fx = Tria(CasaServidor.Q(q, "p"), CasaServidor.Q(q, "id"));
                        if (fx == null) { CasaServidor.Error(res, 410, "Torna a buscar"); return; }
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
