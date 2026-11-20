// WhatsApp via WhatsApp Web a l'Edge del servidor (protocol DevTools). Llegir xats i escriure. C# 5.
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

public static class WhatsApp
{
    const string URL = "https://web.whatsapp.com/";
    static readonly object lk = new object();
    static Cdp sessio;
    static string loginTarget;

    static string Pont() { return File.ReadAllText(Path.Combine(CasaServidor.DirArrel, "whatsapp-pont.js"), Encoding.UTF8); }

    static string Target()
    {
        foreach (string[] p in Edge.Pagines()) if (p[1].Contains("web.whatsapp.com")) return p[0];
        return Edge.NovaPagina(URL);
    }

    static Cdp Assegura()
    {
        lock (lk)
        {
            if (sessio != null && sessio.Viu) return sessio;
            Edge.Assegura();
            string t = Target();
            Cdp c = new Cdp();
            c.Connecta(Edge.WsPagina(t));
            c.Envia("Page.enable", null, 5000);
            try { c.Envia("Page.addScriptToEvaluateOnNewDocument", CasaServidor.Dic("source", Pont()), 5000); } catch { }
            // WhatsApp necessita una finestra prou gran per mostrar la llista i el xat alhora
            try { c.Envia("Emulation.setDeviceMetricsOverride", CasaServidor.Dic("width", 1300, "height", 900, "deviceScaleFactor", 1, "mobile", false), 5000); } catch { }
            string href = Edge.Avalua(c, "location.href", 5000) as string ?? "";
            if (!href.Contains("web.whatsapp.com")) { c.Envia("Page.navigate", CasaServidor.Dic("url", URL), 15000); Thread.Sleep(4000); }
            sessio = c;
            return c;
        }
    }

    static string Crida(string expr, int ms)
    {
        Cdp c = Assegura();
        string js = "(async () => { if (!window.__wa || window.__wa.v !== 5) { " + Pont() + "\n } return JSON.stringify(await window.__wa." + expr + "); })()";
        try
        {
            object v = Edge.Avalua(c, js, ms);
            return v == null ? "null" : v.ToString();
        }
        catch
        {
            lock (lk) { if (sessio != null && !sessio.Viu) sessio = null; }
            throw;
        }
    }

    static string Js(string s) { return new JavaScriptSerializer().Serialize(s ?? ""); }

    // Obre un xat amb un clic de ratoli real (WhatsApp ignora sovint els clics fets amb JavaScript).
    // Si no s'obre, el busca al cercador i prem Avall + Retorn amb el teclat "de veritat".
    static void Clic(Cdp c, double x, double y)
    {
        c.Envia("Input.dispatchMouseEvent", CasaServidor.Dic("type", "mouseMoved", "x", x, "y", y), 3000);
        c.Envia("Input.dispatchMouseEvent", CasaServidor.Dic("type", "mousePressed", "x", x, "y", y, "button", "left", "buttons", 1, "clickCount", 1), 3000);
        c.Envia("Input.dispatchMouseEvent", CasaServidor.Dic("type", "mouseReleased", "x", x, "y", y, "button", "left", "buttons", 0, "clickCount", 1), 3000);
    }
    static void Tecla(Cdp c, string key, int codi)
    {
        c.Envia("Input.dispatchKeyEvent", CasaServidor.Dic("type", "rawKeyDown", "key", key, "code", key, "windowsVirtualKeyCode", codi, "nativeVirtualKeyCode", codi), 3000);
        c.Envia("Input.dispatchKeyEvent", CasaServidor.Dic("type", "keyUp", "key", key, "code", key, "windowsVirtualKeyCode", codi, "nativeVirtualKeyCode", codi), 3000);
    }
    static bool Obert(string nom) { try { return Crida("esActual(" + Js(nom) + ")", 4000) == "true"; } catch { return false; } }

    static void ObreXat(string nom)
    {
        if (string.IsNullOrEmpty(nom)) return;
        Cdp c = Assegura();
        try { c.Envia("Page.bringToFront", null, 3000); } catch { }
        string r = Crida("rect(" + Js(nom) + ")", 10000);
        Dictionary<string, object> d = new JavaScriptSerializer().DeserializeObject(r) as Dictionary<string, object>;
        if (d != null && d.ContainsKey("ja")) return;
        if (d != null && d.ContainsKey("x"))
        {
            Clic(c, Convert.ToDouble(d["x"]), Convert.ToDouble(d["y"]));
            for (int i = 0; i < 8; i++) { Thread.Sleep(250); if (Obert(nom)) return; }
        }
        // Pla B: cercador + teclat
        try
        {
            Crida("cerca(" + Js(nom) + ")", 8000);
            Tecla(c, "ArrowDown", 40); Thread.Sleep(200);
            Tecla(c, "Enter", 13);
            for (int i = 0; i < 8; i++) { Thread.Sleep(250); if (Obert(nom)) return; }
        }
        catch { }
        try { Console.WriteLine("  WhatsApp: no s'ha obert el xat. " + Crida("diag(" + Js(nom) + ")", 4000)); } catch { }
    }

    public static void AlArrencar()
    {
        try
        {
            string f = Path.Combine(CasaServidor.DirDades, "whatsapp.json");
            if (File.Exists(f) && File.ReadAllText(f).Contains("\"actiu\":true"))
                ThreadPool.QueueUserWorkItem(delegate { try { Assegura(); } catch (Exception e) { Console.WriteLine("  WhatsApp: " + e.Message); } });
        }
        catch { }
    }

    static void Activa(bool si)
    {
        try { File.WriteAllText(Path.Combine(CasaServidor.DirDades, "whatsapp.json"), "{\"actiu\":" + (si ? "true" : "false") + "}", new UTF8Encoding(false)); } catch { }
    }

    // API per a l'app (protegida amb la contrasenya del servidor, com la configuracio)
    public static void Api(string nom, Dictionary<string, string> q, HttpListenerContext ctx)
    {
        HttpListenerResponse res = ctx.Response;
        string ct = "application/json; charset=utf-8";
        try
        {
            switch (nom)
            {
                case "estat": Activa(true); CasaServidor.Text(res, Crida("estat()", 8000), ct); return;
                case "qr": CasaServidor.Text(res, Crida("qr()", 8000), ct); return;
                case "diagnosi": CasaServidor.Text(res, Crida("diag(" + Js(CasaServidor.Q(q, "xat")) + ")", 8000), ct); return;
                case "xats": CasaServidor.Text(res, Crida("xats()", 8000), ct); return;
                case "missatges":
                    try { ObreXat(CasaServidor.Q(q, "xat")); } catch { }
                    CasaServidor.Text(res, Crida("missatges(" + Js(CasaServidor.Q(q, "xat")) + ")", 15000), ct);
                    return;
                case "nous":
                    {
                        long des; long.TryParse(CasaServidor.Q(q, "des"), out des);
                        lock (lk) { if (sessio == null || !sessio.Viu) { CasaServidor.Text(res, "{\"llista\":[],\"total\":0}", ct); return; } }
                        CasaServidor.Text(res, Crida("nous(" + des + ")", 6000), ct);
                        return;
                    }
                case "envia":
                    {
                        string text;
                        using (StreamReader sr = new StreamReader(ctx.Request.InputStream, Encoding.UTF8)) text = sr.ReadToEnd();
                        if (text.Trim() == "") { CasaServidor.Error(res, 400, "Missatge buit"); return; }
                        try { ObreXat(CasaServidor.Q(q, "xat")); } catch { }
                        CasaServidor.Text(res, Crida("envia(" + Js(CasaServidor.Q(q, "xat")) + "," + Js(text) + ")", 20000), ct);
                        return;
                    }
                case "mostra":
                    {
                        Assegura();
                        foreach (string[] p in Edge.Pagines()) if (p[1].Contains("web.whatsapp.com")) { loginTarget = p[0]; Edge.Finestra(p[0], true); }
                        CasaServidor.Text(res, "{\"ok\":true}", ct);
                        return;
                    }
                case "amaga":
                    if (loginTarget != null) { try { Edge.Finestra(loginTarget, false); } catch { } }
                    CasaServidor.Text(res, "{\"ok\":true}", ct);
                    return;
                case "atura":
                    Activa(false);
                    lock (lk) { if (sessio != null) { try { sessio.Tanca(); } catch { } sessio = null; } }
                    foreach (string[] p in Edge.Pagines()) if (p[1].Contains("web.whatsapp.com")) { try { Edge.TancaPagina(p[0]); } catch { } }
                    CasaServidor.Text(res, "{\"ok\":true}", ct);
                    return;
            }
            CasaServidor.Error(res, 404, "API desconeguda");
        }
        catch (Exception e) { try { CasaServidor.Error(res, 500, e.Message); } catch { } }
    }
}
