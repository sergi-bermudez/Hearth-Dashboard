// Biblioteca de musica: llegeix les etiquetes (ID3 dels MP3, i les dels M4A/AAC) per poder
// filtrar per artista, album, genere o any, com un reproductor professional. C# 5.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

public static class Musica
{
    static readonly string[] Ext = { ".mp3", ".m4a", ".aac", ".mp4", ".m4b", ".wav" };
    static readonly object lk = new object();
    static Dictionary<string, object[]> index;     // rel -> { mida, data, titol, artista, artistaAlbum, album, genere, any, pista, durada, disc }
    static int indexant = 0;
    static DateTime darrer = DateTime.MinValue;
    static string estat = "";

    static readonly string[] Generes = { "Blues", "Classic Rock", "Country", "Dance", "Disco", "Funk", "Grunge", "Hip-Hop", "Jazz", "Metal", "New Age", "Oldies", "Other", "Pop", "R&B", "Rap", "Reggae", "Rock", "Techno", "Industrial", "Alternative", "Ska", "Death Metal", "Pranks", "Soundtrack", "Euro-Techno", "Ambient", "Trip-Hop", "Vocal", "Jazz+Funk", "Fusion", "Trance", "Classical", "Instrumental", "Acid", "House", "Game", "Sound Clip", "Gospel", "Noise", "AlternRock", "Bass", "Soul", "Punk", "Space", "Meditative", "Instrumental Pop", "Instrumental Rock", "Ethnic", "Gothic", "Darkwave", "Techno-Industrial", "Electronic", "Pop-Folk", "Eurodance", "Dream", "Southern Rock", "Comedy", "Cult", "Gangsta", "Top 40", "Christian Rap", "Pop/Funk", "Jungle", "Native American", "Cabaret", "New Wave", "Psychedelic", "Rave", "Showtunes", "Trailer", "Lo-Fi", "Tribal", "Acid Punk", "Acid Jazz", "Polka", "Retro", "Musical", "Rock & Roll", "Hard Rock", "Folk", "Folk-Rock", "National Folk", "Swing", "Fast Fusion", "Bebop", "Latin", "Revival", "Celtic", "Bluegrass", "Avantgarde", "Gothic Rock", "Progressive Rock", "Psychedelic Rock", "Symphonic Rock", "Slow Rock", "Big Band", "Chorus", "Easy Listening", "Acoustic", "Humour", "Speech", "Chanson", "Opera", "Chamber Music", "Sonata", "Symphony", "Booty Bass", "Primus", "Porn Groove", "Satire", "Slow Jam", "Club", "Tango", "Samba", "Folklore", "Ballad", "Power Ballad", "Rhythmic Soul", "Freestyle", "Duet", "Punk Rock", "Drum Solo", "A capella", "Euro-House", "Dance Hall" };

    static string FitxerIndex { get { return Path.Combine(CasaServidor.DirDades, "musica-index.json"); } }

    // ---------- Lectura d'etiquetes ----------
    static int Synchsafe(byte[] b, int o) { return (b[o] & 0x7f) << 21 | (b[o + 1] & 0x7f) << 14 | (b[o + 2] & 0x7f) << 7 | (b[o + 3] & 0x7f); }
    static int Be32(byte[] b, int o) { return b[o] << 24 | b[o + 1] << 16 | b[o + 2] << 8 | b[o + 3]; }

    static string TextId3(byte[] b, int o, int n)
    {
        if (n <= 1) return "";
        int enc = b[o];
        string s;
        try
        {
            if (enc == 1) s = (n >= 3 && b[o + 1] == 0xFE && b[o + 2] == 0xFF) ? Encoding.BigEndianUnicode.GetString(b, o + 3, n - 3) : Encoding.Unicode.GetString(b, o + (n >= 3 && b[o + 1] == 0xFF ? 3 : 1), n - (n >= 3 && b[o + 1] == 0xFF ? 3 : 1));
            else if (enc == 2) s = Encoding.BigEndianUnicode.GetString(b, o + 1, n - 1);
            else if (enc == 3) s = Encoding.UTF8.GetString(b, o + 1, n - 1);
            else s = Encoding.GetEncoding(28591).GetString(b, o + 1, n - 1);
        }
        catch { return ""; }
        int z = s.IndexOf('\0');
        if (z >= 0) s = s.Substring(0, z);
        return s.Trim();
    }

    static string Genere(string g)
    {
        g = (g ?? "").Trim();
        // "(13)", "(13)Pop", "13"
        System.Text.RegularExpressions.Match m = System.Text.RegularExpressions.Regex.Match(g, @"^\(?(\d{1,3})\)?(.*)$");
        if (m.Success)
        {
            if (m.Groups[2].Value.Trim() != "") return m.Groups[2].Value.Trim();
            int i = int.Parse(m.Groups[1].Value);
            return i < Generes.Length ? Generes[i] : "";
        }
        return g;
    }

    static int Num(string s) { int n = 0; if (s == null) return 0; int i = s.IndexOf('/'); if (i >= 0) s = s.Substring(0, i); int.TryParse(s.Trim(), out n); return n; }

    static readonly int[,] Bitrates = { { 0, 32, 64, 96, 128, 160, 192, 224, 256, 288, 320, 352, 384, 416, 448 }, { 0, 32, 48, 56, 64, 80, 96, 112, 128, 160, 192, 224, 256, 320, 384 }, { 0, 32, 40, 48, 56, 64, 80, 96, 112, 128, 160, 192, 224, 256, 320 }, { 0, 32, 48, 56, 64, 80, 96, 112, 128, 144, 160, 176, 192, 224, 256 }, { 0, 8, 16, 24, 32, 40, 48, 56, 64, 80, 96, 112, 128, 144, 160 } };

    // Durada d'un MP3: capcalera Xing/Info o, si no n'hi ha, a partir del bitrate
    static int DuradaMp3(FileStream fs, long inici, long mida)
    {
        try
        {
            byte[] b = new byte[16384];
            fs.Seek(inici, SeekOrigin.Begin);
            int n = fs.Read(b, 0, b.Length);
            for (int i = 0; i + 4 < n; i++)
            {
                if (b[i] != 0xFF || (b[i + 1] & 0xE0) != 0xE0) continue;
                int ver = (b[i + 1] >> 3) & 3, capa = (b[i + 1] >> 1) & 3, bri = (b[i + 2] >> 4) & 15, sri = (b[i + 2] >> 2) & 3, canals = (b[i + 3] >> 6) & 3;
                if (ver == 1 || capa != 1 || bri == 0 || bri == 15 || sri == 3) continue;
                int[] srs = ver == 3 ? new int[] { 44100, 48000, 32000 } : ver == 2 ? new int[] { 22050, 24000, 16000 } : new int[] { 11025, 12000, 8000 };
                int sr = srs[sri];
                int br = Bitrates[ver == 3 ? 2 : 4, bri] * 1000;
                int mostres = ver == 3 ? 1152 : 576;
                int xo = i + 4 + (ver == 3 ? (canals == 3 ? 17 : 32) : (canals == 3 ? 9 : 17));
                if (xo + 12 < n)
                {
                    string tag = Encoding.ASCII.GetString(b, xo, 4);
                    if ((tag == "Xing" || tag == "Info") && (b[xo + 7] & 1) != 0) { int frames = Be32(b, xo + 8); if (frames > 0) return (int)((long)frames * mostres / sr); }
                    if (Encoding.ASCII.GetString(b, i + 36, 4) == "VBRI" && i + 50 < n) { int frames = Be32(b, i + 36 + 14); if (frames > 0) return (int)((long)frames * mostres / sr); }
                }
                return br > 0 ? (int)((mida - (inici + i)) * 8 / br) : 0;
            }
        }
        catch { }
        return 0;
    }

    static void LlegeixMp3(string f, object[] r)
    {
        using (FileStream fs = new FileStream(f, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        {
            long mida = fs.Length, inici = 0;
            byte[] h = new byte[10];
            if (fs.Read(h, 0, 10) == 10 && h[0] == 'I' && h[1] == 'D' && h[2] == '3')
            {
                int ver = h[3], tam = Synchsafe(h, 6);
                inici = 10 + tam + ((h[5] & 0x10) != 0 ? 10 : 0);
                if (tam > 0 && tam < 64 * 1024 * 1024)
                {
                    byte[] b = new byte[tam];
                    int llegits = fs.Read(b, 0, tam);
                    int o = 0;
                    if ((h[5] & 0x40) != 0 && ver >= 3 && llegits > 4) o = ver == 4 ? Synchsafe(b, 0) : Be32(b, 0) + 4;
                    while (o + (ver == 2 ? 6 : 10) < llegits)
                    {
                        string id; int n;
                        if (ver == 2) { id = Encoding.ASCII.GetString(b, o, 3); n = b[o + 3] << 16 | b[o + 4] << 8 | b[o + 5]; o += 6; }
                        else { id = Encoding.ASCII.GetString(b, o, 4); n = ver == 4 ? Synchsafe(b, o + 4) : Be32(b, o + 4); o += 10; }
                        if (id[0] == '\0' || n <= 0 || o + n > llegits) break;
                        switch (id)
                        {
                            case "TIT2": case "TT2": r[2] = TextId3(b, o, n); break;
                            case "TPE1": case "TP1": r[3] = TextId3(b, o, n); break;
                            case "TPE2": case "TP2": r[4] = TextId3(b, o, n); break;
                            case "TALB": case "TAL": r[5] = TextId3(b, o, n); break;
                            case "TCON": case "TCO": r[6] = Genere(TextId3(b, o, n)); break;
                            case "TYER": case "TYE": case "TDRC": { string y = TextId3(b, o, n); if (y.Length >= 4) r[7] = Num(y.Substring(0, 4)); break; }
                            case "TRCK": case "TRK": r[8] = Num(TextId3(b, o, n)); break;
                            case "TPOS": case "TPA": r[10] = Num(TextId3(b, o, n)); break;
                            case "TLEN": case "TLE": { int ms = Num(TextId3(b, o, n)); if (ms > 1000) r[9] = ms / 1000; break; }
                        }
                        o += n;
                    }
                }
            }
            // ID3v1 al final, si falta alguna cosa
            if ((string)r[2] == "" && mida > 128)
            {
                byte[] t = new byte[128];
                fs.Seek(mida - 128, SeekOrigin.Begin);
                if (fs.Read(t, 0, 128) == 128 && t[0] == 'T' && t[1] == 'A' && t[2] == 'G')
                {
                    Encoding l1 = Encoding.GetEncoding(28591);
                    Func<int, int, string> tx = delegate (int o, int n) { return l1.GetString(t, o, n).Split('\0')[0].Trim(); };
                    r[2] = tx(3, 30); r[3] = tx(33, 30); r[5] = tx(63, 30);
                    r[7] = Num(tx(93, 4));
                    if (t[125] == 0 && t[126] != 0) r[8] = (int)t[126];
                    if (t[127] < Generes.Length) r[6] = Generes[t[127]];
                }
            }
            if ((int)r[9] == 0) r[9] = DuradaMp3(fs, inici, mida);
        }
    }

    // MP4/M4A: atoms moov > udta > meta > ilst
    static void LlegeixMp4(string f, object[] r)
    {
        using (FileStream fs = new FileStream(f, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        {
            long pos = 0, mida = fs.Length;
            byte[] h = new byte[16];
            while (pos + 8 <= mida)
            {
                fs.Seek(pos, SeekOrigin.Begin);
                if (fs.Read(h, 0, 8) < 8) break;
                long tam = (uint)Be32(h, 0);
                string tipus = Encoding.ASCII.GetString(h, 4, 4);
                if (tam == 1) { fs.Read(h, 8, 8); tam = (long)((ulong)(uint)Be32(h, 8) << 32 | (uint)Be32(h, 12)); }
                if (tam == 0) tam = mida - pos;
                if (tam < 8) break;
                if (tipus == "moov" && tam < 64 * 1024 * 1024)
                {
                    byte[] b = new byte[tam];
                    fs.Seek(pos, SeekOrigin.Begin);
                    int n = fs.Read(b, 0, (int)tam);
                    Atoms(b, 8, n, r, "moov");
                    return;
                }
                pos += tam;
            }
        }
    }
    static void Atoms(byte[] b, int ini, int fi, object[] r, string pare)
    {
        int o = ini;
        while (o + 8 <= fi)
        {
            int tam = Be32(b, o);
            if (tam < 8 || o + tam > fi) break;
            string t = Encoding.GetEncoding(28591).GetString(b, o + 4, 4);
            if (t == "udta" || t == "trak" || t == "mdia") Atoms(b, o + 8, o + tam, r, t);
            else if (t == "meta") Atoms(b, o + 12, o + tam, r, t);
            else if (t == "ilst") Atoms(b, o + 8, o + tam, r, t);
            else if (t == "mvhd")
            {
                int v = b[o + 8];
                long escala = v == 1 ? (uint)Be32(b, o + 28) : (uint)Be32(b, o + 20);
                long dur = v == 1 ? ((long)(uint)Be32(b, o + 32) << 32 | (uint)Be32(b, o + 36)) : (uint)Be32(b, o + 24);
                if (escala > 0) r[9] = (int)(dur / escala);
            }
            else if (pare == "ilst")
            {
                // item > data (8 capcalera + 4 tipus + 4 locale + valor)
                int d = o + 8;
                if (d + 16 <= o + tam && Encoding.ASCII.GetString(b, d + 4, 4) == "data")
                {
                    int dl = Be32(b, d), vo = d + 16, vn = dl - 16;
                    if (vn > 0 && vo + vn <= fi)
                    {
                        string s = Encoding.UTF8.GetString(b, vo, vn).Trim('\0', ' ');
                        switch (t)
                        {
                            case "\u00a9nam": r[2] = s; break;
                            case "\u00a9ART": r[3] = s; break;
                            case "aART": r[4] = s; break;
                            case "\u00a9alb": r[5] = s; break;
                            case "\u00a9gen": r[6] = s; break;
                            case "gnre": if (vn >= 2) { int g = b[vo] << 8 | b[vo + 1]; if (g > 0 && g <= Generes.Length) r[6] = Generes[g - 1]; } break;
                            case "\u00a9day": if (s.Length >= 4) r[7] = Num(s.Substring(0, 4)); break;
                            case "trkn": if (vn >= 4) r[8] = b[vo + 2] << 8 | b[vo + 3]; break;
                            case "disk": if (vn >= 4) r[10] = b[vo + 2] << 8 | b[vo + 3]; break;
                        }
                    }
                }
            }
            o += tam;
        }
    }

    static object[] Etiquetes(FileInfo f, string rel)
    {
        object[] r = { f.Length, f.LastWriteTimeUtc.Ticks, "", "", "", "", "", 0, 0, 0, 0 };
        string e = f.Extension.ToLowerInvariant();
        try
        {
            if (e == ".mp3") LlegeixMp3(f.FullName, r);
            else if (e == ".m4a" || e == ".mp4" || e == ".m4b" || e == ".aac") LlegeixMp4(f.FullName, r);
        }
        catch { }
        // Si no hi ha etiquetes: "Artista - Titol" o la carpeta
        string nom = Path.GetFileNameWithoutExtension(f.Name);
        if ((string)r[2] == "")
        {
            string t = System.Text.RegularExpressions.Regex.Replace(nom, @"^\d{1,3}[\s._-]+", "");
            int g = t.IndexOf(" - ");
            if (g > 0 && (string)r[3] == "") { r[3] = t.Substring(0, g).Trim(); t = t.Substring(g + 3); }
            r[2] = t.Trim();
            if ((int)r[8] == 0) { System.Text.RegularExpressions.Match m = System.Text.RegularExpressions.Regex.Match(nom, @"^(\d{1,3})"); if (m.Success) r[8] = int.Parse(m.Groups[1].Value); }
        }
        string[] parts = rel.Split('/');
        if ((string)r[5] == "" && parts.Length >= 2) r[5] = parts[parts.Length - 2];
        if ((string)r[3] == "" && parts.Length >= 3) r[3] = parts[parts.Length - 3];
        return r;
    }

    // ---------- Index ----------
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
                if (d != null)
                    foreach (KeyValuePair<string, object> kv in d)
                    {
                        object[] a = kv.Value as object[];
                        if (a == null || a.Length < 11) continue;
                        object[] r = { Convert.ToInt64(a[0]), Convert.ToInt64(a[1]), a[2] ?? "", a[3] ?? "", a[4] ?? "", a[5] ?? "", a[6] ?? "", Convert.ToInt32(a[7]), Convert.ToInt32(a[8]), Convert.ToInt32(a[9]), Convert.ToInt32(a[10]) };
                        index[kv.Key] = r;
                    }
            }
        }
        catch { }
    }

    // Recorre les carpetes sense aturar-se si alguna no es pot llegir
    static IEnumerable<string> Fitxers(string arrel)
    {
        Stack<string> pila = new Stack<string>();
        pila.Push(arrel);
        while (pila.Count > 0)
        {
            string d = pila.Pop();
            string[] fs = null, ds = null;
            try { fs = Directory.GetFiles(d); ds = Directory.GetDirectories(d); } catch { }
            if (fs != null) foreach (string f in fs) yield return f;
            if (ds != null) foreach (string x in ds) { string n = Path.GetFileName(x); if (!n.StartsWith(".") && !n.StartsWith("$")) pila.Push(x); }
        }
    }

    public static void Indexa()
    {
        if (Interlocked.CompareExchange(ref indexant, 1, 0) != 0) return;
        ThreadPool.QueueUserWorkItem(delegate
        {
            try
            {
                string arrel = CasaServidor.RutaArrel("musica");
                if (arrel == null || !Directory.Exists(arrel)) { estat = "No hi ha carpeta de m\u00fasica"; return; }
                lock (lk) Carrega();
                Dictionary<string, object[]> nou = new Dictionary<string, object[]>();
                int fets = 0, nous = 0;
                foreach (string f in Fitxers(arrel))
                {
                    if (Array.IndexOf(Ext, Path.GetExtension(f).ToLowerInvariant()) < 0) continue;
                    FileInfo fi = new FileInfo(f);
                    if ((fi.Attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0) continue;
                    string rel = f.Substring(arrel.TrimEnd('\\').Length).TrimStart('\\').Replace('\\', '/');
                    object[] vell;
                    lock (lk) index.TryGetValue(rel, out vell);
                    if (vell != null && (long)vell[0] == fi.Length && (long)vell[1] == fi.LastWriteTimeUtc.Ticks) nou[rel] = vell;
                    else { nou[rel] = Etiquetes(fi, rel); nous++; }
                    fets++;
                    if (fets % 200 == 0) estat = "Llegint etiquetes: " + fets + " can\u00e7ons";
                    if (nous > 0 && nous % 500 == 0) lock (lk) { foreach (KeyValuePair<string, object[]> kv in nou) index[kv.Key] = kv.Value; }
                }
                lock (lk) { index = nou; darrer = DateTime.Now; }
                JavaScriptSerializer js = new JavaScriptSerializer(); js.MaxJsonLength = int.MaxValue;
                File.WriteAllText(FitxerIndex, js.Serialize(nou), new UTF8Encoding(false));
                estat = fets + " can\u00e7ons";
            }
            catch (Exception e) { estat = "Error: " + e.Message; }
            finally { indexant = 0; }
        });
    }

    // ---------- API ----------
    static string Clau(string s) { return (s ?? "").Trim().ToLowerInvariant(); }
    static string ArtistaAlbum(object[] r) { string a = (string)r[4]; return a != "" ? a : (string)r[3]; }

    static Dictionary<string, object> Canco(string rel, object[] r)
    {
        Dictionary<string, object> x = new Dictionary<string, object>();
        x["p"] = rel; x["t"] = r[2]; x["a"] = r[3]; x["al"] = r[5]; x["g"] = r[6]; x["y"] = r[7]; x["n"] = r[8]; x["d"] = r[9]; x["disc"] = r[10];
        return x;
    }

    public static void Api(Dictionary<string, string> q, HttpListenerContext ctx)
    {
        if (index == null || (DateTime.Now - darrer).TotalMinutes > 30) Indexa();
        string vista = CasaServidor.Q(q, "vista"), fa = Clau(CasaServidor.Q(q, "artista")), fal = Clau(CasaServidor.Q(q, "album")), fg = Clau(CasaServidor.Q(q, "genere")), fy = CasaServidor.Q(q, "any"), cerca = Clau(CasaServidor.Q(q, "q"));
        List<KeyValuePair<string, object[]>> l = new List<KeyValuePair<string, object[]>>();
        lock (lk)
        {
            Carrega();
            foreach (KeyValuePair<string, object[]> kv in index)
            {
                object[] r = kv.Value;
                if (fa != "" && Clau(ArtistaAlbum(r)) != fa && Clau((string)r[3]) != fa) continue;
                if (fal != "" && Clau((string)r[5]) != fal) continue;
                if (fg != "" && Clau((string)r[6]) != fg) continue;
                if (fy != "" && r[7].ToString() != fy && !(fy.EndsWith("s") && fy.Length == 5 && ((int)r[7]) / 10 * 10 == Num(fy.Substring(0, 4)))) continue;
                if (cerca != "" && (Clau((string)r[2]) + " " + Clau((string)r[3]) + " " + Clau((string)r[5]) + " " + Clau((string)r[6])).IndexOf(cerca) < 0) continue;
                l.Add(kv);
            }
        }
        Dictionary<string, object> o = new Dictionary<string, object>();
        o["indexant"] = indexant == 1;
        o["estat"] = estat;
        o["total"] = index.Count;
        if (vista == "artistes" || vista == "generes" || vista == "anys" || vista == "albums")
        {
            Dictionary<string, Dictionary<string, object>> g = new Dictionary<string, Dictionary<string, object>>();
            foreach (KeyValuePair<string, object[]> kv in l)
            {
                object[] r = kv.Value;
                string nom = vista == "artistes" ? ArtistaAlbum(r) : vista == "generes" ? (string)r[6] : vista == "anys" ? ((int)r[7] > 0 ? ((int)r[7] / 10 * 10) + "s" : "") : (string)r[5];
                if (nom == "") continue;
                string k = Clau(nom) + (vista == "albums" ? "|" + Clau(ArtistaAlbum(r)) : "");
                Dictionary<string, object> e;
                if (!g.TryGetValue(k, out e))
                {
                    e = new Dictionary<string, object>();
                    e["nom"] = nom; e["n"] = 0; e["p"] = kv.Key; e["albums"] = new HashSet<string>();
                    if (vista == "albums") { e["a"] = ArtistaAlbum(r); e["y"] = r[7]; }
                    g[k] = e;
                }
                e["n"] = (int)e["n"] + 1;
                ((HashSet<string>)e["albums"]).Add(Clau((string)r[5]));
                // Per a la portada: una canco de l'album amb el numero de pista mes baix
                if ((int)r[8] == 1) e["p"] = kv.Key;
            }
            List<Dictionary<string, object>> res = new List<Dictionary<string, object>>(g.Values);
            foreach (Dictionary<string, object> e in res) { e["na"] = ((HashSet<string>)e["albums"]).Count; e.Remove("albums"); }
            if (vista == "anys") res.Sort(delegate (Dictionary<string, object> a, Dictionary<string, object> b) { return string.CompareOrdinal((string)b["nom"], (string)a["nom"]); });
            else if (vista == "albums" && fa != "") res.Sort(delegate (Dictionary<string, object> a, Dictionary<string, object> b) { return ((int)a["y"]).CompareTo((int)b["y"]); });
            else res.Sort(delegate (Dictionary<string, object> a, Dictionary<string, object> b) { return string.Compare((string)a["nom"], (string)b["nom"], StringComparison.CurrentCultureIgnoreCase); });
            o["grups"] = res;
        }
        else
        {
            // Cancons: ordenades per album (any) i pista si hi ha filtre; si no, per titol
            bool perAlbum = fa != "" || fal != "";
            l.Sort(delegate (KeyValuePair<string, object[]> x, KeyValuePair<string, object[]> y)
            {
                object[] a = x.Value, b = y.Value;
                if (perAlbum)
                {
                    int c = ((int)a[7]).CompareTo((int)b[7]); if (c != 0) return c;
                    c = string.Compare((string)a[5], (string)b[5], StringComparison.CurrentCultureIgnoreCase); if (c != 0) return c;
                    c = ((int)a[10]).CompareTo((int)b[10]); if (c != 0) return c;
                    c = ((int)a[8]).CompareTo((int)b[8]); if (c != 0) return c;
                }
                return string.Compare((string)a[2], (string)b[2], StringComparison.CurrentCultureIgnoreCase);
            });
            int lim;
            if (!int.TryParse(CasaServidor.Q(q, "limit"), out lim) || lim <= 0) lim = 3000;
            List<object> res = new List<object>();
            for (int i = 0; i < l.Count && i < lim; i++) res.Add(Canco(l[i].Key, l[i].Value));
            o["cancons"] = res;
        }
        CasaServidor.Json(ctx.Response, o);
    }
}
