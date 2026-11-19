// Captura de webs: cada web s'obre en una finestra amagada de l'Edge del servidor
// i l'iPad en rep imatges (una cada pocs segons, o seguides com si fos video).
// Tocar la imatge a l'iPad fa un clic a la web. C# 5.
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

public static class Webs
{
    class Captura
    {
        public string Id, Target, Url;
        public Cdp Pag;
        public byte[] Darrera;
        public DateTime Quan = DateTime.MinValue, Vist = DateTime.Now;
        public readonly object Lk = new object();
        public int W, H;
        public double Cx, Cy, Cw, Ch;
        public bool Retall;
    }
    static readonly Dictionary<string, Captura> obertes = new Dictionary<string, Captura>();
    static readonly object lk = new object();
    static Timer neteja;

    static List<Dictionary<string, object>> Definicions()
    {
        List<Dictionary<string, object>> l = new List<Dictionary<string, object>>();
        string f = Path.Combine(CasaServidor.DirDades, "webs.json");
        try
        {
            if (File.Exists(f))
            {
                object[] a = new JavaScriptSerializer().DeserializeObject(File.ReadAllText(f, Encoding.UTF8)) as object[];
                if (a != null) foreach (object o in a) { Dictionary<string, object> d = o as Dictionary<string, object>; if (d != null) l.Add(d); }
            }
        }
        catch { }
        return l;
    }

    static double Num(Dictionary<string, object> d, string k, double def)
    {
        object v;
        if (d == null || !d.TryGetValue(k, out v) || v == null) return def;
        try { return Convert.ToDouble(v, System.Globalization.CultureInfo.InvariantCulture); } catch { return def; }
    }

    static Captura Obre(string id)
    {
        Dictionary<string, object> def = null;
        foreach (Dictionary<string, object> d in Definicions()) if (d.ContainsKey("id") && d["id"].ToString() == id) def = d;
        if (def == null) throw new Exception("Aquesta web no existeix");
        string url = def.ContainsKey("url") ? def["url"].ToString() : "";
        int w = (int)Num(def, "w", 1280), h = (int)Num(def, "h", 800);
        Captura c;
        lock (lk)
        {
            obertes.TryGetValue(id, out c);
            if (c != null && c.Pag != null && c.Pag.Viu && c.Url == url && c.W == w && c.H == h) { Clip(c, def); c.Vist = DateTime.Now; return c; }
            if (c != null) { try { c.Pag.Tanca(); } catch { } try { Edge.TancaPagina(c.Target); } catch { } }
            c = new Captura();
            c.Id = id; c.Url = url; c.W = w; c.H = h;
            c.Target = Edge.NovaPagina(url);
            c.Pag = new Cdp();
            c.Pag.Connecta(Edge.WsPagina(c.Target));
            c.Pag.Envia("Page.enable", null, 5000);
            c.Pag.Envia("Emulation.setDeviceMetricsOverride", CasaServidor.Dic("width", w, "height", h, "deviceScaleFactor", 1, "mobile", false), 5000);
            Clip(c, def);
            obertes[id] = c;
            if (neteja == null) neteja = new Timer(Neteja, null, 60000, 60000);
        }
        Thread.Sleep(2500);
        return c;
    }

    static void Clip(Captura c, Dictionary<string, object> def)
    {
        Dictionary<string, object> cl = def.ContainsKey("clip") ? def["clip"] as Dictionary<string, object> : null;
        c.Retall = cl != null && Num(cl, "w", 0) > 10 && Num(cl, "h", 0) > 10;
        if (c.Retall) { c.Cx = Num(cl, "x", 0); c.Cy = Num(cl, "y", 0); c.Cw = Num(cl, "w", c.W); c.Ch = Num(cl, "h", c.H); }
        else { c.Cx = 0; c.Cy = 0; c.Cw = c.W; c.Ch = c.H; }
    }

    // Tanca les webs que ningu mira fa estona
    static void Neteja(object o)
    {
        List<Captura> velles = new List<Captura>();
        lock (lk)
        {
            foreach (Captura c in obertes.Values) if ((DateTime.Now - c.Vist).TotalMinutes > 15) velles.Add(c);
            foreach (Captura c in velles) obertes.Remove(c.Id);
        }
        foreach (Captura c in velles) { try { c.Pag.Tanca(); } catch { } try { Edge.TancaPagina(c.Target); } catch { } }
    }

    static byte[] Imatge(string id, bool completa, int minMs)
    {
        Captura c = Obre(id);
        lock (c.Lk)
        {
            c.Vist = DateTime.Now;
            if (!completa && c.Darrera != null && (DateTime.Now - c.Quan).TotalMilliseconds < minMs) return c.Darrera;
            Dictionary<string, object> p = CasaServidor.Dic("format", "jpeg", "quality", 78);
            if (!completa && c.Retall) p["clip"] = CasaServidor.Dic("x", c.Cx, "y", c.Cy, "width", c.Cw, "height", c.Ch, "scale", 1);
            Dictionary<string, object> r = c.Pag.Envia("Page.captureScreenshot", p, 15000);
            byte[] b = Convert.FromBase64String((string)r["data"]);
            if (!completa) { c.Darrera = b; c.Quan = DateTime.Now; }
            return b;
        }
    }

    static void Clic(string id, double fx, double fy)
    {
        Captura c = Obre(id);
        double x = c.Cx + fx * c.Cw, y = c.Cy + fy * c.Ch;
        c.Pag.Envia("Input.dispatchMouseEvent", CasaServidor.Dic("type", "mouseMoved", "x", x, "y", y), 5000);
        c.Pag.Envia("Input.dispatchMouseEvent", CasaServidor.Dic("type", "mousePressed", "x", x, "y", y, "button", "left", "clickCount", 1), 5000);
        c.Pag.Envia("Input.dispatchMouseEvent", CasaServidor.Dic("type", "mouseReleased", "x", x, "y", y, "button", "left", "clickCount", 1), 5000);
        c.Quan = DateTime.MinValue;
    }

    static void Roda(string id, int dy)
    {
        Captura c = Obre(id);
        double x = c.Cx + c.Cw / 2, y = c.Cy + c.Ch / 2;
        c.Pag.Envia("Input.dispatchMouseEvent", CasaServidor.Dic("type", "mouseWheel", "x", x, "y", y, "deltaX", 0, "deltaY", dy), 5000);
        c.Quan = DateTime.MinValue;
    }

    public static void Api(string nom, Dictionary<string, string> q, HttpListenerContext ctx)
    {
        HttpListenerResponse res = ctx.Response;
        string id = CasaServidor.Q(q, "id");
        try
        {
            switch (nom)
            {
                case "imatge":
                    {
                        int min;
                        if (!int.TryParse(CasaServidor.Q(q, "min"), out min)) min = 800;
                        byte[] b = Imatge(id, CasaServidor.Q(q, "completa") == "1", Math.Max(300, min));
                        res.ContentType = "image/jpeg";
                        res.AddHeader("Cache-Control", "no-cache, no-store");
                        res.ContentLength64 = b.Length;
                        res.OutputStream.Write(b, 0, b.Length);
                        return;
                    }
                case "clic":
                    Clic(id, double.Parse(CasaServidor.Q(q, "x"), System.Globalization.CultureInfo.InvariantCulture), double.Parse(CasaServidor.Q(q, "y"), System.Globalization.CultureInfo.InvariantCulture));
                    CasaServidor.Text(res, "{\"ok\":true}", "application/json");
                    return;
                case "roda":
                    Roda(id, int.Parse(CasaServidor.Q(q, "d")));
                    CasaServidor.Text(res, "{\"ok\":true}", "application/json");
                    return;
                case "recarrega":
                    Obre(id).Pag.Envia("Page.reload", null, 10000);
                    CasaServidor.Text(res, "{\"ok\":true}", "application/json");
                    return;
                case "mostra":
                case "amaga":
                    Edge.Finestra(Obre(id).Target, nom == "mostra");
                    CasaServidor.Text(res, "{\"ok\":true}", "application/json");
                    return;
                case "tanca":
                    {
                        Captura c;
                        lock (lk) { obertes.TryGetValue(id, out c); obertes.Remove(id); }
                        if (c != null) { try { c.Pag.Tanca(); } catch { } try { Edge.TancaPagina(c.Target); } catch { } }
                        CasaServidor.Text(res, "{\"ok\":true}", "application/json");
                        return;
                    }
            }
            CasaServidor.Error(res, 404, "API desconeguda");
        }
        catch (Exception e)
        {
            Console.WriteLine("  Webs: " + e.Message);
            try { CasaServidor.Error(res, 503, e.Message); } catch { }
        }
    }
}
