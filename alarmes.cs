// Alarmes fetes sonar pel servidor (que sempre es engegat): temporitzadors de cuina i despertadors que han de sonar
// en un altaveu o una tele Cast/DLNA ("cast:<id>") o al mateix PC ("pc"). Les pantalles de Hearth obertes les fan
// sonar elles mateixes; si una pantalla l'atura (desa "aturat"), aqui tambe s'atura. C# 5.
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

public static class Alarmes
{
    static Timer rellotge;
    static readonly object lk = new object();
    static readonly Dictionary<string, DateTime> fets = new Dictionary<string, DateTime>();
    class Sonant { public string Clau, Id, Disp; public DateTime Inici; }
    static readonly List<Sonant> sonant = new List<Sonant>();
    static System.Media.SoundPlayer pcSo;

    public static void Inicia()
    {
        rellotge = new Timer(delegate { try { Mira(); } catch (Exception e) { Console.WriteLine("  Alarmes: " + e.Message); } }, null, 15000, 10000);
    }

    static string Fitxer { get { return Path.Combine(CasaServidor.DirDades, "alarmes.json"); } }

    static List<Dictionary<string, object>> Llegeix()
    {
        List<Dictionary<string, object>> r = new List<Dictionary<string, object>>();
        string t;
        lock (CasaServidor.DadesLock) t = File.Exists(Fitxer) ? File.ReadAllText(Fitxer, Encoding.UTF8) : "";
        if (t.Trim() == "" || t.Trim() == "null") return r;
        object[] l = new JavaScriptSerializer().DeserializeObject(t) as object[];
        if (l != null) foreach (object o in l) { Dictionary<string, object> d = o as Dictionary<string, object>; if (d != null) r.Add(d); }
        return r;
    }

    // Canvia una alarma al fitxer (llegint-lo just abans, per no trepitjar els canvis de les pantalles)
    static void Canvia(string id, Action<Dictionary<string, object>> f)
    {
        lock (CasaServidor.DadesLock)
        {
            if (!File.Exists(Fitxer)) return;
            JavaScriptSerializer js = new JavaScriptSerializer();
            object[] l = js.DeserializeObject(File.ReadAllText(Fitxer, Encoding.UTF8)) as object[];
            if (l == null) return;
            foreach (object o in l) { Dictionary<string, object> d = o as Dictionary<string, object>; if (d != null && Str(d, "id") == id) f(d); }
            File.WriteAllText(Fitxer, js.Serialize(l), new UTF8Encoding(false));
        }
    }

    static string Str(Dictionary<string, object> d, string k)
    {
        object v;
        if (!d.TryGetValue(k, out v) || v == null) return "";
        return Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture);
    }
    static double Num(Dictionary<string, object> d, string k)
    {
        object v;
        if (!d.TryGetValue(k, out v) || v == null) return 0;
        try { return Convert.ToDouble(v, System.Globalization.CultureInfo.InvariantCulture); } catch { return 0; }
    }

    static bool EsDelServidor(string disp) { return disp.StartsWith("cast:") || disp == "pc"; }

    // La mateixa clau que fan servir les pantalles (index.html, alMira) per a cada vegada que sona
    static string Clau(Dictionary<string, object> a, DateTime ara, double ms)
    {
        string id = Str(a, "id");
        if (Str(a, "tipus") == "cuina")
        {
            double fi = Num(a, "fi");
            return fi > 0 && ms >= fi && ms - fi < 3600000 ? id + "|" + Str(a, "fi") : null;
        }
        object on;
        if (a.TryGetValue("on", out on) && on is bool && !(bool)on) return null;
        double pos = Num(a, "pospos");
        if (pos > 0 && ms >= pos && ms - pos < 3600000) return id + "|p" + Str(a, "pospos");
        string[] hm = Str(a, "hora").Split(':');
        if (hm.Length != 2) return null;
        int h, m;
        if (!int.TryParse(hm[0], out h) || !int.TryParse(hm[1], out m) || ara.Hour != h || ara.Minute != m) return null;
        object[] dies = a.ContainsKey("dies") ? a["dies"] as object[] : null;
        if (dies != null && dies.Length > 0)
        {
            bool avui = false;
            foreach (object d in dies) if (Convert.ToInt32(d) == (int)ara.DayOfWeek) avui = true;
            if (!avui) return null;
        }
        return id + "|" + ara.Year + "-" + (ara.Month - 1) + "-" + ara.Day;
    }

    static void Mira()
    {
        List<Dictionary<string, object>> l = Llegeix();
        DateTime ara = DateTime.Now;
        double ms = (DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalMilliseconds;
        // les que sonen: s'aturen si algu les ha aturades, si s'han esborrat o si fa 15 minuts que sonen
        lock (lk)
        {
            for (int i = sonant.Count - 1; i >= 0; i--)
            {
                Sonant s = sonant[i];
                Dictionary<string, object> a = l.Find(delegate (Dictionary<string, object> x) { return Str(x, "id") == s.Id; });
                bool para = a == null || Str(a, "aturat") == s.Clau || (DateTime.Now - s.Inici).TotalMinutes > 15;
                if (a != null && Str(a, "tipus") == "cuina" && !s.Clau.EndsWith("|" + Str(a, "fi"))) para = true;
                if (!para) continue;
                sonant.RemoveAt(i);
                Atura(s.Disp);
            }
            foreach (string k in new List<string>(fets.Keys)) if ((DateTime.Now - fets[k]).TotalDays > 3) fets.Remove(k);
        }
        foreach (Dictionary<string, object> a in l)
        {
            string disp = Str(a, "disp");
            if (!EsDelServidor(disp)) continue;
            string clau = Clau(a, ara, ms);
            if (clau == null || Str(a, "aturat") == clau) continue;
            lock (lk) { if (fets.ContainsKey(clau)) continue; fets[clau] = DateTime.Now; }
            Sona(a, clau, disp);
        }
    }

    static void Sona(Dictionary<string, object> a, string clau, string disp)
    {
        string nom = Str(a, "nom");
        if (nom == "") nom = Str(a, "tipus") == "cuina" ? "Temporitzador" : "Despertador";
        string u = "/api/alarma/bip.wav", k = "musica";
        string so = Str(a, "so");
        if (so.StartsWith("radio:") && disp != "pc")
        {
            // una de les radios preferides (dades/radios.json)
            try
            {
                int n = int.Parse(so.Substring(6));
                string f = Path.Combine(CasaServidor.DirDades, "radios.json");
                object[] rl = new JavaScriptSerializer().DeserializeObject(File.ReadAllText(f, Encoding.UTF8)) as object[];
                Dictionary<string, object> r = rl != null && n < rl.Length ? rl[n] as Dictionary<string, object> : null;
                if (r != null && Str(r, "u") != "") { u = "/api/radio/mp3?u=" + Uri.EscapeDataString(Str(r, "u")); k = "radio"; nom = nom + " \u00b7 " + Str(r, "t"); }
            }
            catch { }
        }
        bool ok = false;
        try
        {
            if (disp == "pc") { SonaPc(); ok = true; }
            else ok = Cast.EnviaA(disp.Substring(5), u, k, nom);
        }
        catch (Exception e) { Console.WriteLine("  Alarmes: no ha pogut sonar a " + disp + ": " + e.Message); }
        if (!ok) return;
        Console.WriteLine("  Alarmes: sona \"" + nom + "\" a " + disp);
        lock (lk) sonant.Add(new Sonant { Clau = clau, Id = Str(a, "id"), Disp = disp, Inici = DateTime.Now });
        // perque les pantalles obertes mostrin l'avis (sense so) i s'hi pugui aturar
        try { Canvia(Str(a, "id"), delegate (Dictionary<string, object> d) { d["sonant"] = clau; }); } catch { }
    }

    static void Atura(string disp)
    {
        try
        {
            if (disp == "pc") { if (pcSo != null) { pcSo.Stop(); pcSo = null; } }
            else Cast.AturaA(disp.Substring(5));
        }
        catch { }
    }

    static void SonaPc()
    {
        if (pcSo != null) try { pcSo.Stop(); } catch { }
        pcSo = new System.Media.SoundPlayer(new MemoryStream(Wav(6)));
        pcSo.PlayLooping();
    }

    // "Bip bip bip" en WAV (8 kHz, 8 bits): segons de durada
    static byte[] Wav(int segons)
    {
        int sr = 8000;
        List<byte> d = new List<byte>();
        double[][] pat = { new double[] { 880, .14 }, new double[] { 0, .1 }, new double[] { 880, .14 }, new double[] { 0, .1 }, new double[] { 880, .14 }, new double[] { 0, .7 } };
        while (d.Count < segons * sr)
            foreach (double[] p in pat)
                for (int i = 0; i < (int)(p[1] * sr); i++)
                    d.Add(p[0] > 0 ? (byte)Math.Round(128 + 90 * Math.Sin(2 * Math.PI * p[0] * i / sr)) : (byte)128);
        MemoryStream ms = new MemoryStream();
        BinaryWriter w = new BinaryWriter(ms);
        w.Write(Encoding.ASCII.GetBytes("RIFF")); w.Write(36 + d.Count); w.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
        w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(sr); w.Write(sr); w.Write((short)1); w.Write((short)8);
        w.Write(Encoding.ASCII.GetBytes("data")); w.Write(d.Count); w.Write(d.ToArray());
        w.Flush();
        return ms.ToArray();
    }

    static byte[] bipLlarg;
    // /api/alarma/bip.wav: 5 minuts de bips per als altaveus (no saben repetir un so curt)
    public static void Bip(HttpListenerContext ctx)
    {
        if (bipLlarg == null) bipLlarg = Wav(300);
        HttpListenerResponse res = ctx.Response;
        res.ContentType = "audio/wav";
        res.ContentLength64 = bipLlarg.Length;
        try { res.OutputStream.Write(bipLlarg, 0, bipLlarg.Length); res.OutputStream.Close(); } catch { }
    }
}
