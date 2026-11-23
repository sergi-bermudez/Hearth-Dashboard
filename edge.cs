// Edge propi del servidor, controlat pel protocol DevTools (Spotify, webs, Google Fotos). C# 5.
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;

public class Cdp
{
    ClientWebSocket ws;
    int seguent;
    readonly object lk = new object(), lkEnvia = new object();
    readonly Dictionary<int, Pendent> pendents = new Dictionary<int, Pendent>();
    public Action<string, Dictionary<string, object>> Esdeveniment;

    class Pendent { public ManualResetEvent Fet = new ManualResetEvent(false); public Dictionary<string, object> Dades; }

    public bool Viu { get { return ws != null && ws.State == WebSocketState.Open; } }

    public void Connecta(string url)
    {
        ws = new ClientWebSocket();
        ws.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
        if (!ws.ConnectAsync(new Uri(url), CancellationToken.None).Wait(8000)) throw new Exception("No puc connectar amb l'Edge");
        Thread t = new Thread(Llegeix);
        t.IsBackground = true;
        t.Start();
    }

    public void Tanca()
    {
        try { if (ws != null) ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None).Wait(2000); } catch { }
    }

    void Llegeix()
    {
        byte[] buf = new byte[65536];
        MemoryStream ms = new MemoryStream();
        JavaScriptSerializer js = new JavaScriptSerializer();
        js.MaxJsonLength = int.MaxValue;
        try
        {
            while (ws.State == WebSocketState.Open)
            {
                WebSocketReceiveResult r = ws.ReceiveAsync(new ArraySegment<byte>(buf), CancellationToken.None).Result;
                if (r.MessageType == WebSocketMessageType.Close) break;
                ms.Write(buf, 0, r.Count);
                if (!r.EndOfMessage) continue;
                string txt = Encoding.UTF8.GetString(ms.ToArray());
                ms.SetLength(0);
                Dictionary<string, object> d;
                try { d = js.DeserializeObject(txt) as Dictionary<string, object>; } catch { continue; }
                if (d == null) continue;
                if (d.ContainsKey("id"))
                {
                    int id = Convert.ToInt32(d["id"]);
                    Pendent p = null;
                    lock (lk) { if (pendents.TryGetValue(id, out p)) pendents.Remove(id); }
                    if (p != null) { p.Dades = d; p.Fet.Set(); }
                }
                else if (d.ContainsKey("method") && Esdeveniment != null)
                {
                    try { Esdeveniment((string)d["method"], d.ContainsKey("params") ? d["params"] as Dictionary<string, object> : null); } catch { }
                }
            }
        }
        catch { }
        lock (lk)
        {
            foreach (Pendent p in pendents.Values) p.Fet.Set();
            pendents.Clear();
        }
    }

    public Dictionary<string, object> Envia(string metode, Dictionary<string, object> prm, int ms)
    {
        if (!Viu) throw new Exception("Connexio amb l'Edge tancada");
        int id = Interlocked.Increment(ref seguent);
        Pendent p = new Pendent();
        lock (lk) pendents[id] = p;
        Dictionary<string, object> m = new Dictionary<string, object>();
        m["id"] = id; m["method"] = metode; m["params"] = prm ?? new Dictionary<string, object>();
        JavaScriptSerializer js = new JavaScriptSerializer();
        js.MaxJsonLength = int.MaxValue;
        byte[] b = Encoding.UTF8.GetBytes(js.Serialize(m));
        lock (lkEnvia) ws.SendAsync(new ArraySegment<byte>(b), WebSocketMessageType.Text, true, CancellationToken.None).Wait(5000);
        if (!p.Fet.WaitOne(ms)) { lock (lk) pendents.Remove(id); throw new TimeoutException(metode + ": no respon"); }
        if (p.Dades == null) throw new Exception("Connexio amb l'Edge tancada");
        if (p.Dades.ContainsKey("error"))
        {
            Dictionary<string, object> e = p.Dades["error"] as Dictionary<string, object>;
            throw new Exception(e != null && e.ContainsKey("message") ? e["message"].ToString() : "Error de l'Edge");
        }
        return p.Dades.ContainsKey("result") ? p.Dades["result"] as Dictionary<string, object> : new Dictionary<string, object>();
    }
}


public static class Edge
{
    public const int PORT = 9333;
    static Process proc;
    static readonly object lk = new object();

    public static string Ruta()
    {
        string[] llocs = {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), @"Microsoft\Edge\Application\msedge.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"Microsoft\Edge\Application\msedge.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Microsoft\Edge\Application\msedge.exe") };
        foreach (string l in llocs) if (File.Exists(l)) return l;
        return null;
    }

    public static string Http(string url, string metode)
    {
        HttpWebRequest rq = (HttpWebRequest)WebRequest.Create(url);
        rq.Method = metode; rq.Timeout = 4000; rq.Proxy = null;
        using (WebResponse rs = rq.GetResponse())
        using (StreamReader sr = new StreamReader(rs.GetResponseStream(), Encoding.UTF8)) return sr.ReadToEnd();
    }

    public static object Json(string url, string metode)
    {
        JavaScriptSerializer js = new JavaScriptSerializer();
        js.MaxJsonLength = int.MaxValue;
        return js.DeserializeObject(Http(url, metode));
    }

    // canvia-ho quan canvien les opcions d'engegada, perque l'Edge que ja corre es reinici\u00ef
    const string ARGS_VERSIO = "2";

    // Les pagines del servidor van en finestres amagades: algunes webs (cameres, videos) s'aturen si creuen que no es veuen.
    // Se'ls fa creure que la finestra es visible i te el focus.
    public const string VISIBLE_SCRIPT = "(function(){try{Object.defineProperty(document,'visibilityState',{get:function(){return 'visible';},configurable:true});" +
        "Object.defineProperty(document,'hidden',{get:function(){return false;},configurable:true});" +
        "Object.defineProperty(document,'webkitVisibilityState',{get:function(){return 'visible';},configurable:true});" +
        "document.hasFocus=function(){return true;};" +
        "var st=function(e){e.stopImmediatePropagation();};document.addEventListener('visibilitychange',st,true);document.addEventListener('webkitvisibilitychange',st,true);" +
        "window.addEventListener('blur',st,true);window.addEventListener('pagehide',st,true);}catch(e){}})();";

    public static void FesVisible(Cdp p)
    {
        try { p.Envia("Page.addScriptToEvaluateOnNewDocument", CasaServidor.Dic("source", VISIBLE_SCRIPT), 5000); } catch { }
        try { p.Envia("Emulation.setFocusEmulationEnabled", CasaServidor.Dic("enabled", true), 5000); } catch { }
        try { p.Envia("Page.setWebLifecycleState", CasaServidor.Dic("state", "active"), 5000); } catch { }
    }

    public static bool Viu()
    {
        try { Http("http://127.0.0.1:" + PORT + "/json/version", "GET"); return true; } catch { return false; }
    }

    // Engega l'Edge del servidor (amagat) si no ho est\u00e0
    public static void Assegura()
    {
        lock (lk)
        {
            string perfil0 = Path.Combine(CasaServidor.DirDades, "edge-perfil");
            string marca = Path.Combine(perfil0, "hearth-args.txt");
            if (Viu())
            {
                // un Edge engegat per una versio anterior (amb altres opcions): es reinicia un cop
                string ant = ""; try { ant = File.ReadAllText(marca); } catch { }
                if (ant == ARGS_VERSIO) return;
                try { Cdp b0 = new Cdp(); Dictionary<string, object> v0 = Json("http://127.0.0.1:" + PORT + "/json/version", "GET") as Dictionary<string, object>; b0.Connecta((string)v0["webSocketDebuggerUrl"]); try { b0.Envia("Browser.close", null, 3000); } catch { } b0.Tanca(); } catch { }
                for (int i = 0; i < 40 && Viu(); i++) Thread.Sleep(250);
                if (Viu()) return;
            }
            string exe = Ruta();
            if (exe == null) throw new Exception("No trobo el Microsoft Edge en aquest PC");
            string perfil = Path.Combine(CasaServidor.DirDades, "edge-perfil");
            Directory.CreateDirectory(perfil);
            string args = "--remote-debugging-port=" + PORT + " --remote-allow-origins=* --user-data-dir=\"" + perfil + "\" " +
                "--no-first-run --no-default-browser-check --hide-crash-restore-bubble --autoplay-policy=no-user-gesture-required " +
                "--disable-background-timer-throttling --disable-renderer-backgrounding --disable-backgrounding-occluded-windows " +
                "--disable-features=CalculateNativeWinOcclusion,IntensiveWakeUpThrottling --disable-background-media-suspend " +
                "--window-size=1150,820 --window-position=-12000,-12000 about:blank";
            ProcessStartInfo psi = new ProcessStartInfo(exe, args);
            psi.UseShellExecute = false;
            proc = Process.Start(psi);
            for (int i = 0; i < 60 && !Viu(); i++) Thread.Sleep(250);
            if (!Viu()) throw new Exception("L'Edge no respon");
            try { File.WriteAllText(marca, ARGS_VERSIO); } catch { }
        }
    }

    public static void Reinicia()
    {
        lock (lk)
        {
            try { Cdp b = Navegador(); try { b.Envia("Browser.close", null, 3000); } catch { } b.Tanca(); } catch { }
            Thread.Sleep(1000);
            try { if (proc != null && !proc.HasExited) proc.Kill(); } catch { }
            proc = null;
        }
        for (int i = 0; i < 20 && Viu(); i++) Thread.Sleep(250);
    }

    // Connexio al navegador (no a una pagina)
    public static Cdp Navegador()
    {
        Assegura();
        Dictionary<string, object> v = Json("http://127.0.0.1:" + PORT + "/json/version", "GET") as Dictionary<string, object>;
        Cdp b = new Cdp();
        b.Connecta((string)v["webSocketDebuggerUrl"]);
        return b;
    }

    public static string WsPagina(string targetId) { return "ws://127.0.0.1:" + PORT + "/devtools/page/" + targetId; }

    // Llista de pestanyes: [targetId, url]
    public static List<string[]> Pagines()
    {
        Assegura();
        List<string[]> l = new List<string[]>();
        object[] llista = Json("http://127.0.0.1:" + PORT + "/json/list", "GET") as object[];
        if (llista != null)
            foreach (object o in llista)
            {
                Dictionary<string, object> t = o as Dictionary<string, object>;
                if (t != null && (string)t["type"] == "page") l.Add(new string[] { (string)t["id"], t.ContainsKey("url") ? (string)t["url"] : "" });
            }
        return l;
    }

    // Obre una finestra nova (amagada) amb una adre\u00e7a i en torna l'identificador
    public static string NovaPagina(string url)
    {
        Cdp b = Navegador();
        try
        {
            string id = (string)b.Envia("Target.createTarget", CasaServidor.Dic("url", url, "newWindow", true), 8000)["targetId"];
            try { MouFinestra(b, id, false); } catch { }
            return id;
        }
        finally { b.Tanca(); }
    }

    public static void TancaPagina(string id)
    {
        Cdp b = Navegador();
        try { b.Envia("Target.closeTarget", CasaServidor.Dic("targetId", id), 5000); } catch { }
        finally { b.Tanca(); }
    }

    public static void Finestra(string targetId, bool visible)
    {
        Cdp b = Navegador();
        try { MouFinestra(b, targetId, visible); }
        finally { b.Tanca(); }
    }

    static void MouFinestra(Cdp b, string id, bool visible)
    {
        object wid = b.Envia("Browser.getWindowForTarget", CasaServidor.Dic("targetId", id), 5000)["windowId"];
        b.Envia("Browser.setWindowBounds", CasaServidor.Dic("windowId", wid, "bounds", CasaServidor.Dic("windowState", "normal")), 5000);
        b.Envia("Browser.setWindowBounds", CasaServidor.Dic("windowId", wid, "bounds",
            visible ? CasaServidor.Dic("left", 80, "top", 60, "width", 1150, "height", 820) : CasaServidor.Dic("left", -12000, "top", -12000, "width", 1150, "height", 820)), 5000);
        if (visible)
        {
            // maximitzada i al davant de tot, perqu\u00e8 es vegi a la pantalla del PC
            try { b.Envia("Browser.setWindowBounds", CasaServidor.Dic("windowId", wid, "bounds", CasaServidor.Dic("windowState", "maximized")), 5000); } catch { }
            try { b.Envia("Target.activateTarget", CasaServidor.Dic("targetId", id), 5000); } catch { }
            try { Cdp p = new Cdp(); p.Connecta(WsPagina(id)); p.Envia("Page.bringToFront", null, 5000); p.Tanca(); } catch { }
        }
    }

    // Valor d'un Runtime.evaluate (llan\u00e7a l'excepci\u00f3 de la pagina si n'hi ha)
    public static object Valor(Dictionary<string, object> r)
    {
        if (r == null) return null;
        if (r.ContainsKey("exceptionDetails"))
        {
            Dictionary<string, object> ex = r["exceptionDetails"] as Dictionary<string, object>;
            string msg = "Error a la pagina";
            if (ex != null && ex.ContainsKey("exception"))
            {
                Dictionary<string, object> e2 = ex["exception"] as Dictionary<string, object>;
                if (e2 != null && e2.ContainsKey("description")) msg = e2["description"].ToString().Split('\n')[0];
            }
            throw new Exception(msg);
        }
        Dictionary<string, object> res = r.ContainsKey("result") ? r["result"] as Dictionary<string, object> : null;
        return res != null && res.ContainsKey("value") ? res["value"] : null;
    }

    public static object Avalua(Cdp c, string expr, int ms)
    {
        return Valor(c.Envia("Runtime.evaluate", CasaServidor.Dic("expression", expr, "awaitPromise", true, "returnByValue", true), ms));
    }
}
