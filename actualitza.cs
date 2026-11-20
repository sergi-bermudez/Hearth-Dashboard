// Actualitzacions automatiques des de GitHub (sergi-bermudez/Hearth-Dashboard).
// Mira el fitxer VERSION del repositori; si n'hi ha una de nova, llanca eines\actualitza.ps1,
// que baixa el codi, comprova que compila, atura el servidor, copia els fitxers (sense tocar
// dades ni config.json) i el torna a engegar. Si no compila, no es toca res. C# 5.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

public static class Actualitza
{
    const string Repo = "sergi-bermudez/Hearth-Dashboard";
    static string remota = "";
    static DateTime mirat = DateTime.MinValue;
    static string error = "";
    static int aplicant = 0;
    static DateTime inici = DateTime.MinValue;
    static Timer rellotge;

    public static string Local()
    {
        try
        {
            string f = Path.Combine(CasaServidor.DirArrel, "VERSION");
            if (File.Exists(f)) return File.ReadAllText(f).Trim();
        }
        catch { }
        return "0";
    }

    // "3.4.10" > "3.4.9"
    static int Compara(string a, string b)
    {
        string[] x = (a ?? "0").Split('.'), y = (b ?? "0").Split('.');
        for (int i = 0; i < Math.Max(x.Length, y.Length); i++)
        {
            int p = 0, q = 0;
            if (i < x.Length) int.TryParse(x[i], out p);
            if (i < y.Length) int.TryParse(y[i], out q);
            if (p != q) return p < q ? -1 : 1;
        }
        return 0;
    }

    static bool Auto()
    {
        try
        {
            string f = Path.Combine(CasaServidor.DirDades, "ajustos.json");
            if (!File.Exists(f)) return true;
            Dictionary<string, object> d = new JavaScriptSerializer().DeserializeObject(File.ReadAllText(f, Encoding.UTF8)) as Dictionary<string, object>;
            object v;
            if (d != null && d.TryGetValue("actualitzaAuto", out v) && v is bool) return (bool)v;
        }
        catch { }
        return true;
    }

    static string Mira(bool forca)
    {
        if (!forca && (DateTime.Now - mirat).TotalMinutes < 60 && remota != "") return remota;
        try
        {
            HttpWebRequest rq = (HttpWebRequest)WebRequest.Create("https://raw.githubusercontent.com/" + Repo + "/main/VERSION?t=" + DateTime.Now.Ticks);
            rq.UserAgent = "Hearth/" + Local();
            rq.Timeout = 15000; rq.ReadWriteTimeout = 15000;
            rq.CachePolicy = new System.Net.Cache.RequestCachePolicy(System.Net.Cache.RequestCacheLevel.NoCacheNoStore);
            using (WebResponse rs = rq.GetResponse())
            using (StreamReader sr = new StreamReader(rs.GetResponseStream(), Encoding.UTF8)) remota = sr.ReadToEnd().Trim();
            mirat = DateTime.Now;
            error = "";
        }
        catch (Exception e) { error = "No s'ha pogut consultar GitHub: " + e.Message; }
        return remota;
    }

    static string Registre { get { return Path.Combine(CasaServidor.DirDades, "actualitzacio.txt"); } }

    static string DarrerRegistre()
    {
        try
        {
            if (!File.Exists(Registre)) return "";
            string[] l = File.ReadAllLines(Registre, Encoding.UTF8);
            return l.Length > 0 ? l[l.Length - 1] : "";
        }
        catch { return ""; }
    }

    // Llanca l'script que fa la feina (continua encara que aquest proces s'aturi)
    static bool Aplica(out string msg)
    {
        msg = "";
        if (Interlocked.CompareExchange(ref aplicant, 1, 0) != 0) { msg = "Ja s'est\u00e0 actualitzant"; return false; }
        try
        {
            string ps = Path.Combine(CasaServidor.DirArrel, "eines", "actualitza.ps1");
            if (!File.Exists(ps)) { msg = "Falta eines\\actualitza.ps1"; aplicant = 0; return false; }
            ProcessStartInfo si = new ProcessStartInfo("powershell.exe",
                "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"" + ps + "\" -ServidorPid " + Process.GetCurrentProcess().Id +
                " -Dir \"" + CasaServidor.DirArrel + "\" -Repo \"" + Repo + "\"");
            si.UseShellExecute = false; si.CreateNoWindow = true;
            inici = DateTime.Now;
            Process.Start(si);
            Console.WriteLine("  Actualitzaci\u00f3: comen\u00e7ada (" + Local() + " -> " + remota + ")");
            return true;
        }
        catch (Exception e) { msg = e.Message; aplicant = 0; return false; }
    }

    // Cada hora mira si hi ha versio nova; l'aplica de matinada (entre les 3 i les 5) si l'automatic esta activat
    public static void Inicia()
    {
        rellotge = new Timer(delegate
        {
            try
            {
                if (!Auto()) return;
                int h = DateTime.Now.Hour;
                if (h < 3 || h >= 5) return;
                string r = Mira(true);
                string m;
                if (r != "" && Compara(Local(), r) < 0) Aplica(out m);
            }
            catch { }
        }, null, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(60));
    }

    public static void Api(string nom, Dictionary<string, string> q, HttpListenerContext ctx)
    {
        HttpListenerResponse res = ctx.Response;
        Dictionary<string, object> o = new Dictionary<string, object>();
        if (nom == "actualitza/aplica")
        {
            Mira(true);
            string m;
            if (remota == "") { CasaServidor.Error(res, 502, error != "" ? error : "No s'ha pogut consultar GitHub"); return; }
            if (Compara(Local(), remota) >= 0 && CasaServidor.Q(q, "forca") != "1") { o["ok"] = false; o["missatge"] = "Ja tens la darrera versi\u00f3"; CasaServidor.Json(res, o); return; }
            bool ok = Aplica(out m);
            o["ok"] = ok; o["missatge"] = ok ? "Actualitzant: Hearth es reiniciar\u00e0 en un minut" : m;
            CasaServidor.Json(res, o);
            return;
        }
        // actualitza/estat
        if (aplicant == 1)
        {
            // Si l'script ha acabat amb error (o fa massa estona), es pot tornar a provar
            bool err = false;
            try { err = File.Exists(Registre) && File.GetLastWriteTime(Registre) > inici && DarrerRegistre().Contains("ERROR"); } catch { }
            if (err || (DateTime.Now - inici).TotalMinutes > 10) aplicant = 0;
        }
        Mira(CasaServidor.Q(q, "ara") == "1");
        o["local"] = Local();
        o["remota"] = remota;
        o["hiHa"] = remota != "" && Compara(Local(), remota) < 0;
        o["auto"] = Auto();
        o["error"] = error;
        o["aplicant"] = aplicant == 1;
        o["darrer"] = DarrerRegistre();
        CasaServidor.Json(res, o);
    }
}
