// Altres missatgeries amb la seva web a l'Edge del servidor, com el WhatsApp: Google Messages (SMS) i Messenger de Facebook.
// Cada servei te un "pont" (sms-pont.js, messenger-pont.js) amb la mateixa interficie que whatsapp-pont.js. C# 5.
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

public static class Xats
{
    class Servei
    {
        public string Clau, Funcio, Url, Domini, Pont, Target;
        public Cdp Sessio;
        public string LoginTarget;
        public readonly object Lk = new object();
    }

    static readonly Dictionary<string, Servei> serveis = new Dictionary<string, Servei>();

    static Xats()
    {
        serveis["sms"] = new Servei { Clau = "sms", Funcio = "sms", Url = "https://messages.google.com/web/conversations", Domini = "messages.google.com", Pont = "sms-pont.js" };
        serveis["fb"] = new Servei { Clau = "fb", Funcio = "messenger", Url = "https://www.facebook.com/messages/t/", Domini = "facebook.com/messages", Pont = "messenger-pont.js" };
    }

    public static string Funcio(string clau) { Servei s; return serveis.TryGetValue(clau, out s) ? s.Funcio : null; }

    static string Pont(Servei s) { return File.ReadAllText(Path.Combine(CasaServidor.DirArrel, s.Pont), Encoding.UTF8); }

    static string Target(Servei s)
    {
        foreach (string[] p in Edge.Pagines()) if (p[1].Contains(s.Domini)) return p[0];
        return Edge.NovaPagina(s.Url);
    }

    static Cdp Assegura(Servei s)
    {
        lock (s.Lk)
        {
            if (s.Sessio != null && s.Sessio.Viu) return s.Sessio;
            Edge.Assegura();
            // la pestanya d'abans si encara hi es (pot ser a la pagina d'iniciar sessio, amb una altra adreca)
            string t = null;
            if (s.Target != null) foreach (string[] p in Edge.Pagines()) if (p[0] == s.Target) t = s.Target;
            if (t == null) t = Target(s);
            s.Target = t;
            Cdp c = new Cdp();
            c.Connecta(Edge.WsPagina(t));
            c.Envia("Page.enable", null, 5000);
            try { c.Envia("Page.addScriptToEvaluateOnNewDocument", CasaServidor.Dic("source", Pont(s)), 5000); } catch { }
            Edge.FesVisible(c);
            // finestra prou gran per veure la llista de xats i el xat obert alhora
            try { c.Envia("Emulation.setDeviceMetricsOverride", CasaServidor.Dic("width", 1300, "height", 900, "deviceScaleFactor", 1, "mobile", false), 5000); } catch { }
            string href = Edge.Avalua(c, "location.href", 5000) as string ?? "";
            if (!href.Contains(s.Domini.Split('/')[0])) { c.Envia("Page.navigate", CasaServidor.Dic("url", s.Url), 15000); Thread.Sleep(4000); }
            s.Sessio = c;
            return c;
        }
    }

    static string Crida(Servei s, string expr, int ms)
    {
        Cdp c = Assegura(s);
        string js = "(async () => { if (!window.__hx || window.__hx.v !== 1) { " + Pont(s) + "\n } return JSON.stringify(await window.__hx." + expr + "); })()";
        try
        {
            object v = Edge.Avalua(c, js, ms);
            return v == null ? "null" : v.ToString();
        }
        catch
        {
            lock (s.Lk) { if (s.Sessio != null && !s.Sessio.Viu) s.Sessio = null; }
            throw;
        }
    }

    static string Js(string t) { return new JavaScriptSerializer().Serialize(t ?? ""); }

    static void Clic(Cdp c, double x, double y)
    {
        c.Envia("Input.dispatchMouseEvent", CasaServidor.Dic("type", "mouseMoved", "x", x, "y", y), 3000);
        c.Envia("Input.dispatchMouseEvent", CasaServidor.Dic("type", "mousePressed", "x", x, "y", y, "button", "left", "buttons", 1, "clickCount", 1), 3000);
        c.Envia("Input.dispatchMouseEvent", CasaServidor.Dic("type", "mouseReleased", "x", x, "y", y, "button", "left", "buttons", 0, "clickCount", 1), 3000);
    }

    static void Enter(Cdp c)
    {
        c.Envia("Input.dispatchKeyEvent", CasaServidor.Dic("type", "rawKeyDown", "key", "Enter", "code", "Enter", "windowsVirtualKeyCode", 13, "nativeVirtualKeyCode", 13), 3000);
        c.Envia("Input.dispatchKeyEvent", CasaServidor.Dic("type", "char", "key", "Enter", "text", "\r", "unmodifiedText", "\r", "windowsVirtualKeyCode", 13), 3000);
        c.Envia("Input.dispatchKeyEvent", CasaServidor.Dic("type", "keyUp", "key", "Enter", "code", "Enter", "windowsVirtualKeyCode", 13, "nativeVirtualKeyCode", 13), 3000);
    }

    static bool Obert(Servei s, string nom) { try { return Crida(s, "esActual(" + Js(nom) + ")", 4000) == "true"; } catch { return false; } }

    // Obre un xat amb un clic de ratoli de debo sobre la fila de la llista
    static void ObreXat(Servei s, string nom)
    {
        if (string.IsNullOrEmpty(nom)) return;
        Cdp c = Assegura(s);
        string r = Crida(s, "rect(" + Js(nom) + ")", 10000);
        Dictionary<string, object> d = new JavaScriptSerializer().DeserializeObject(r) as Dictionary<string, object>;
        if (d == null || d.ContainsKey("ja") || !d.ContainsKey("x")) return;
        Clic(c, Convert.ToDouble(d["x"]), Convert.ToDouble(d["y"]));
        for (int i = 0; i < 12; i++) { Thread.Sleep(250); if (Obert(s, nom)) return; }
        try { Console.WriteLine("  " + s.Clau + ": no s'ha obert el xat. " + Crida(s, "diag(" + Js(nom) + ")", 4000)); } catch { }
    }

    static string FitxerActiu(Servei s) { return Path.Combine(CasaServidor.DirDades, "xat-" + s.Clau + ".json"); }

    static void Activa(Servei s, bool si)
    {
        try { File.WriteAllText(FitxerActiu(s), "{\"actiu\":" + (si ? "true" : "false") + "}", new UTF8Encoding(false)); } catch { }
    }

    // En engegar el servidor, es tornen a obrir els serveis que es feien servir (per als avisos de missatges nous)
    public static void AlArrencar()
    {
        foreach (Servei s0 in serveis.Values)
        {
            Servei s = s0;
            try
            {
                if (!CasaServidor.FuncioActiva(s.Funcio)) continue;
                string f = FitxerActiu(s);
                if (File.Exists(f) && File.ReadAllText(f).Contains("\"actiu\":true"))
                    ThreadPool.QueueUserWorkItem(delegate { try { Assegura(s); } catch (Exception e) { Console.WriteLine("  " + s.Clau + ": " + e.Message); } });
            }
            catch { }
        }
    }

    // api/admin/xat/<servei>/<nom>
    public static void Api(string clau, string nom, Dictionary<string, string> q, HttpListenerContext ctx)
    {
        HttpListenerResponse res = ctx.Response;
        string ct = "application/json; charset=utf-8";
        Servei s;
        if (!serveis.TryGetValue(clau, out s)) { CasaServidor.Error(res, 404, "Servei desconegut"); return; }
        try
        {
            switch (nom)
            {
                case "estat": Activa(s, true); CasaServidor.Text(res, Crida(s, "estat()", 8000), ct); return;
                case "qr": CasaServidor.Text(res, Crida(s, "qr()", 8000), ct); return;
                case "diagnosi": CasaServidor.Text(res, Crida(s, "diag(" + Js(CasaServidor.Q(q, "xat")) + ")", 8000), ct); return;
                case "xats": CasaServidor.Text(res, Crida(s, "xats()", 8000), ct); return;
                case "missatges":
                    try { ObreXat(s, CasaServidor.Q(q, "xat")); } catch { }
                    CasaServidor.Text(res, Crida(s, "missatges(" + Js(CasaServidor.Q(q, "xat")) + ")", 15000), ct);
                    return;
                case "nous":
                    {
                        long des; long.TryParse(CasaServidor.Q(q, "des"), out des);
                        lock (s.Lk) { if (s.Sessio == null || !s.Sessio.Viu) { CasaServidor.Text(res, "{\"llista\":[],\"total\":0}", ct); return; } }
                        CasaServidor.Text(res, Crida(s, "nous(" + des + ")", 6000), ct);
                        return;
                    }
                case "envia":
                    {
                        string text;
                        using (StreamReader sr = new StreamReader(ctx.Request.InputStream, Encoding.UTF8)) text = sr.ReadToEnd();
                        if (text.Trim() == "") { CasaServidor.Error(res, 400, "Missatge buit"); return; }
                        string xat = CasaServidor.Q(q, "xat");
                        try { ObreXat(s, xat); } catch { }
                        string r = Crida(s, "envia(" + Js(xat) + "," + Js(text) + ")", 20000);
                        // alguns editors nomes envien amb una tecla Enter de debo
                        if (r.Contains("\"enter\":true")) { Enter(Assegura(s)); Thread.Sleep(600); r = "{\"ok\":true}"; }
                        CasaServidor.Text(res, r, ct);
                        return;
                    }
                case "mostra":
                    {
                        Assegura(s);
                        if (s.Target != null) { s.LoginTarget = s.Target; Edge.Finestra(s.Target, true); }
                        CasaServidor.Text(res, "{\"ok\":true}", ct);
                        return;
                    }
                case "amaga":
                    if (s.LoginTarget != null) { try { Edge.Finestra(s.LoginTarget, false); } catch { } }
                    CasaServidor.Text(res, "{\"ok\":true}", ct);
                    return;
                case "atura":
                    Activa(s, false);
                    lock (s.Lk) { if (s.Sessio != null) { try { s.Sessio.Tanca(); } catch { } s.Sessio = null; } }
                    if (s.Target != null) { try { Edge.TancaPagina(s.Target); } catch { } s.Target = null; }
                    CasaServidor.Text(res, "{\"ok\":true}", ct);
                    return;
            }
            CasaServidor.Error(res, 404, "API desconeguda");
        }
        catch (Exception e) { try { CasaServidor.Error(res, 500, e.Message); } catch { } }
    }
}
