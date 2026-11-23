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
        public int W, H; public double Dsf = 1, Zoom = 1, Sx, Sy; public bool Mobil;
        public double Cx, Cy, Cw, Ch;
        public bool Retall;
        public string Clica = ""; public DateTime Clicat = DateTime.MinValue;
        public bool Camera, VideoTrobat, Aparcada; public DateTime Buscat = DateTime.MinValue, Entrat = DateTime.MinValue;
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

    // ---------- Navegador: una pagina de l'Edge per a cada aparell, de la mida de la seva pantalla ----------
    // L'aparell nomes rep una captura quan la demana (despres de tocar, moure o escriure), no un video.
    const string NAV_INICI = "https://www.google.com/";
    const string NAV_SCRIPT = "(function(){try{window.open=function(u){if(u)location.href=u;return window;};" +
        "document.addEventListener('click',function(e){var a=e.target;while(a&&a.tagName!='A')a=a.parentElement;if(a&&a.target&&a.target!='_self')a.target='_self';},true);}catch(e){}})();";

    // La mida de la pantalla i l'adre\u00e7a de cada aparell: si l'Edge es tanca (o es reinicia), la pagina es torna a obrir igual
    static readonly Dictionary<string, object[]> navRecord = new Dictionary<string, object[]>();
    static readonly Dictionary<string, string> navAdreca = new Dictionary<string, string>();

    static Captura ObreNav(string id, int w, int h, bool mobil, double dsf)
    {
        Captura c;
        lock (lk)
        {
            object[] rec;
            if (w > 0 && h > 0) navRecord[id] = new object[] { w, h, mobil, dsf };
            else if (navRecord.TryGetValue(id, out rec)) { w = (int)rec[0]; h = (int)rec[1]; mobil = (bool)rec[2]; dsf = (double)rec[3]; }
            obertes.TryGetValue(id, out c);
            if (c == null || c.Pag == null || !c.Pag.Viu)
            {
                if (c != null) { try { c.Pag.Tanca(); } catch { } try { Edge.TancaPagina(c.Target); } catch { } }
                c = new Captura();
                c.Id = id; c.Url = ""; c.W = w > 0 ? w : 1024; c.H = h > 0 ? h : 700;
                c.Target = Edge.NovaPagina("about:blank");
                c.Pag = new Cdp();
                c.Pag.Connecta(Edge.WsPagina(c.Target));
                c.Pag.Envia("Page.enable", null, 5000);
                c.Pag.Envia("Page.addScriptToEvaluateOnNewDocument", CasaServidor.Dic("source", NAV_SCRIPT), 5000);
                Edge.FesVisible(c.Pag);
                c.Dsf = dsf > 0 ? dsf : 1; c.Mobil = mobil;
                c.Pag.Envia("Emulation.setDeviceMetricsOverride", CasaServidor.Dic("width", c.W, "height", c.H, "deviceScaleFactor", c.Dsf, "mobile", mobil), 5000);
                if (mobil) try { c.Pag.Envia("Emulation.setTouchEmulationEnabled", CasaServidor.Dic("enabled", true, "maxTouchPoints", 1), 5000); } catch { }
                string tornar;
                if (!navAdreca.TryGetValue(id, out tornar) || !tornar.StartsWith("http")) tornar = NAV_INICI;
                c.Pag.Envia("Page.navigate", CasaServidor.Dic("url", tornar), 10000);
                c.Cx = 0; c.Cy = 0; c.Cw = c.W; c.Ch = c.H; c.Retall = false;
                obertes[id] = c;
                if (neteja == null) neteja = new Timer(Neteja, null, 60000, 60000);
            }
            else if (w > 0 && h > 0 && (w != c.W || h != c.H || (dsf > 0 && Math.Abs(dsf - c.Dsf) > 0.01) || mobil != c.Mobil))
            {
                c.W = w; c.H = h; c.Cw = w; c.Ch = h; c.Quan = DateTime.MinValue;
                if (dsf > 0) c.Dsf = dsf;
                c.Mobil = mobil;
                c.Pag.Envia("Emulation.setDeviceMetricsOverride", CasaServidor.Dic("width", w, "height", h, "deviceScaleFactor", c.Dsf, "mobile", mobil), 5000);
                try { c.Pag.Envia("Emulation.setTouchEmulationEnabled", CasaServidor.Dic("enabled", mobil, "maxTouchPoints", 1), 5000); } catch { }
            }
            c.Vist = DateTime.Now;
        }
        return c;
    }

    static string NavUrl(string u)
    {
        u = (u ?? "").Trim();
        if (u == "") return NAV_INICI;
        if (System.Text.RegularExpressions.Regex.IsMatch(u, @"^https?://", System.Text.RegularExpressions.RegexOptions.IgnoreCase)) return u;
        if (!u.Contains(" ") && System.Text.RegularExpressions.Regex.IsMatch(u, @"^[\w-]+(\.[\w-]+)+(:\d+)?(/.*)?$")) return "https://" + u;
        return "https://www.google.com/search?q=" + Uri.EscapeDataString(u);
    }

    static void NavTecla(Cdp p, string tecla)
    {
        int codi = tecla == "Enter" ? 13 : tecla == "Backspace" ? 8 : tecla == "Tab" ? 9 : tecla == "Escape" ? 27 : tecla == "End" ? 35 : 0;
        if (codi == 0) return;
        Dictionary<string, object> d = CasaServidor.Dic("type", "rawKeyDown", "key", tecla, "code", tecla, "windowsVirtualKeyCode", codi, "nativeVirtualKeyCode", codi);
        p.Envia("Input.dispatchKeyEvent", d, 5000);
        if (tecla == "Enter") p.Envia("Input.dispatchKeyEvent", CasaServidor.Dic("type", "char", "key", "Enter", "text", "\r", "unmodifiedText", "\r", "windowsVirtualKeyCode", 13), 5000);
        d = CasaServidor.Dic("type", "keyUp", "key", tecla, "code", tecla, "windowsVirtualKeyCode", codi, "nativeVirtualKeyCode", codi);
        p.Envia("Input.dispatchKeyEvent", d, 5000);
    }

    const string NAV_INFO = "(function(){var e=document.activeElement,ed=false,v='',t='';" +
        "if(e){t=(e.tagName||'').toLowerCase();var ty=(e.type||'').toLowerCase();" +
        "ed=(t=='textarea')||(t=='input'&&!/^(button|submit|checkbox|radio|range|color|file|image|reset|hidden)$/.test(ty))||e.isContentEditable===true;" +
        "if(ed)v=e.isContentEditable?(e.innerText||''):(e.value||'');}" +
        "return {url:location.href,titol:document.title,carregant:document.readyState!='complete',sx:window.scrollX,sy:window.scrollY,zoom:parseFloat(document.documentElement.style.zoom)||1,edita:ed,valor:v.substring(0,2000),contrasenya:!!(e&&e.type=='password'),cercador:!!(e&&(e.type=='search'||e.getAttribute&&e.getAttribute('role')=='combobox'))};})()";

    static Captura Obre(string id)
    {
        if (id.StartsWith("nav-")) return ObreNav(id, 0, 0, false, 0);
        Dictionary<string, object> def = null;
        foreach (Dictionary<string, object> d in Definicions()) if (d.ContainsKey("id") && d["id"].ToString() == id) def = d;
        if (def == null) throw new Exception("Aquesta web no existeix");
        string url = def.ContainsKey("url") ? def["url"].ToString() : "";
        int w = (int)Num(def, "w", 1280), h = (int)Num(def, "h", 800);
        // una camera s'obre sempre a la mateixa mida gran (el retall segueix el video), sigui quin sigui l'aparell on es va retallar
        bool esCam = def.ContainsKey("lloc") && Convert.ToString(def["lloc"]) == "casa";
        if (esCam) { w = 1600; h = 900; }
        Captura c;
        lock (lk)
        {
            obertes.TryGetValue(id, out c);
            if (c != null && c.Pag != null && c.Pag.Viu && c.Url == url && c.W == w && c.H == h) { Clip(c, def); c.Vist = DateTime.Now; return c; }
            if (c != null) { try { c.Pag.Tanca(); } catch { } try { Edge.TancaPagina(c.Target); } catch { } }
            c = new Captura();
            c.Id = id; c.Url = url; c.W = w; c.H = h;
            // s'obre en blanc per preparar-la abans que carregui la web
            c.Target = Edge.NovaPagina("about:blank");
            c.Pag = new Cdp();
            c.Pag.Connecta(Edge.WsPagina(c.Target));
            c.Pag.Envia("Page.enable", null, 5000);
            Edge.FesVisible(c.Pag);
            bool mob = !esCam && def.ContainsKey("mobil") && def["mobil"] is bool && (bool)def["mobil"];
            c.Mobil = mob;
            c.Pag.Envia("Emulation.setDeviceMetricsOverride", CasaServidor.Dic("width", w, "height", h, "deviceScaleFactor", 1, "mobile", mob), 5000);
            if (mob) try { c.Pag.Envia("Emulation.setTouchEmulationEnabled", CasaServidor.Dic("enabled", true, "maxTouchPoints", 1), 5000); } catch { }
            Clip(c, def);
            c.Pag.Envia("Page.navigate", CasaServidor.Dic("url", url), 10000);
            obertes[id] = c;
            if (neteja == null) neteja = new Timer(Neteja, null, 60000, 60000);
        }
        Thread.Sleep(2500);
        return c;
    }

    static void Clip(Captura c, Dictionary<string, object> def)
    {
        // un widget retallat des del navegador: la pagina desplacada i ampliada com estava quan es va retallar
        c.Sx = Num(def, "sx", 0); c.Sy = Num(def, "sy", 0); c.Zoom = Num(def, "zoom", 1);
        Dictionary<string, object> cl = def.ContainsKey("clip") ? def["clip"] as Dictionary<string, object> : null;
        c.Camera = def.ContainsKey("lloc") && Convert.ToString(def["lloc"]) == "casa";
        if (!c.Camera) c.VideoTrobat = false;
        if (!c.VideoTrobat)
        {
            c.Retall = !c.Camera && cl != null && Num(cl, "w", 0) > 10 && Num(cl, "h", 0) > 10;
            if (c.Retall) { c.Cx = Num(cl, "x", 0); c.Cy = Num(cl, "y", 0); c.Cw = Num(cl, "w", c.W); c.Ch = Num(cl, "h", c.H); }
            else { c.Cx = 0; c.Cy = 0; c.Cw = c.W; c.Ch = c.H; }
        }
        c.Clica = def.ContainsKey("clica") && def["clica"] != null ? def["clica"].ToString().Trim() : "";
    }

    // Tanca les webs que ningu mira fa estona
    static void Neteja(object o)
    {
        List<Captura> velles = new List<Captura>();
        lock (lk)
        {
            // les cameres es queden obertes (aixi no cal tornar a iniciar la sessio), tret que ningu les miri en un dia
            foreach (Captura c in obertes.Values) if ((DateTime.Now - c.Vist).TotalMinutes > (c.Camera ? 1440 : 15)) velles.Add(c);
            foreach (Captura c in velles) obertes.Remove(c.Id);
        }
        foreach (Captura c in velles) { try { c.Pag.Tanca(); } catch { } try { Edge.TancaPagina(c.Target); } catch { } }
        // una camera que fa un minut que ningu mira: s'atura el directe (les cameres solars gasten bateria mentre emeten)
        // tornant la pestanya a la web d'inici; la sessio es mante perque la pestanya no es tanca
        List<Captura> aparca = new List<Captura>();
        lock (lk) foreach (Captura c in obertes.Values) if (c.Camera && !c.Aparcada && (DateTime.Now - c.Vist).TotalSeconds > 50) aparca.Add(c);
        foreach (Captura c in aparca)
        {
            lock (c.Lk) { c.Aparcada = true; c.VideoTrobat = false; c.Retall = false; c.Cx = 0; c.Cy = 0; c.Cw = c.W; c.Ch = c.H; c.Darrera = null; }
            try { c.Pag.Envia("Page.navigate", CasaServidor.Dic("url", c.Url), 10000); Registre(c, "ningu la mira: s'atura el directe"); } catch { }
        }
    }

    static byte[] Imatge(string id, bool completa, int minMs)
    {
        Captura c = Obre(id);
        // el zoom del pessic es mant\u00e9 en canviar de pagina
        if (id.StartsWith("nav-") && Math.Abs(c.Zoom - 1) > 0.01)
            try { Edge.Avalua(c.Pag, string.Format(System.Globalization.CultureInfo.InvariantCulture, "(function(){{var d=document.documentElement;if(d&&d.style.zoom!='{0}')d.style.zoom='{0}';return 1;}})()", c.Zoom), 3000); } catch { }
        // un widget: cada cop, la pagina a la posicio i l'ampliacio de quan es va retallar (si s'ha recarregat, tornaria a dalt)
        if (!id.StartsWith("nav-") && (c.Sx > 0 || c.Sy > 0 || Math.Abs(c.Zoom - 1) > 0.01))
            try { Edge.Avalua(c.Pag, string.Format(System.Globalization.CultureInfo.InvariantCulture, "(function(){{var d=document.documentElement;if(d&&{2}!=1&&d.style.zoom!='{2}')d.style.zoom='{2}';if(Math.abs(window.scrollX-{0})>2||Math.abs(window.scrollY-{1})>2)window.scrollTo({0},{1});return 1;}})()", c.Sx, c.Sy, c.Zoom), 3000); } catch { }
        if (!id.StartsWith("nav-") && (DateTime.Now - c.Clicat).TotalSeconds > 10) EngegaDirecte(c);
        if (c.Camera && (DateTime.Now - c.Buscat).TotalSeconds > 5) BuscaVideo(c);
        c.Aparcada = false;
        lock (c.Lk)
        {
            c.Vist = DateTime.Now;
            if (!completa && c.Darrera != null && (DateTime.Now - c.Quan).TotalMilliseconds < minMs) return c.Darrera;
            // a pantalles de retina la captura ja ve al doble: una mica mes comprimida perque l'iPad antic la rebi de pressa
            Dictionary<string, object> p = CasaServidor.Dic("format", "jpeg", "quality", c.Dsf > 1.2 ? 68 : 78);
            if (!completa && c.Retall) p["clip"] = CasaServidor.Dic("x", c.Cx, "y", c.Cy, "width", c.Cw, "height", c.Ch, "scale", 1);
            Dictionary<string, object> r = c.Pag.Envia("Page.captureScreenshot", p, 15000);
            byte[] b = Convert.FromBase64String((string)r["data"]);
            if (!completa) { c.Darrera = b; c.Quan = DateTime.Now; }
            return b;
        }
    }

    // Les cameres del nuvol (CloudEdge...): la web s'obre amb la llista d'aparells i cal tocar la camera per veure el directe.
    // Si a la pagina no hi ha cap video o canvas gran en marxa, es toca l'element que te exactament aquest text.
    // Les passes per arribar al directe, escrites a la camera: els textos que cal tocar, per ordre, separats per comes
    // (p. ex. "Entrar, Jard\u00ed"). Cada volta, si no hi ha video en marxa, es toca el darrer text que es vegi a la pagina
    // (aixi, si ja s'ha entrat al compte, es toca directament la camera). Si un formulari te contrasenya, primer un clic
    // al camp perque l'Edge deixi fer servir la contrasenya desada.
    static void EngegaDirecte(Captura c)
    {
        c.Clicat = DateTime.Now;
        try
        {
            List<string> passes = new List<string>();
            foreach (string t in c.Clica.Split(new char[] { ',', '>', '\u203a', ';' })) if (t.Trim() != "") passes.Add(t.Trim());
            if (passes.Count > 0)
            {
                string js = "(function(ts){function vis(e){var r=e.getBoundingClientRect();return r.width>4&&r.height>4&&getComputedStyle(e).visibility!='hidden';}" +
                    "function mig(e){e.scrollIntoView({block:'center'});var r=e.getBoundingClientRect();return Math.round(r.left+r.width/2)+','+Math.round(r.top+r.height/2);}" +
                    "function net(s){return (s||'').replace(/\\s+/g,' ').replace(/^ | $/g,'').toLowerCase();}" +
                    "var v=document.querySelectorAll('video,canvas'),i,j,r;" +
                    "for(i=0;i<v.length;i++){r=v[i].getBoundingClientRect();if(r.width>200&&r.height>100&&(v[i].tagName!='VIDEO'||(!v[i].paused&&v[i].readyState>1)))return '';}" +
                    "var e=document.querySelectorAll('body *,input[type=submit],input[type=button]');" +
                    "for(j=ts.length-1;j>=0;j--){var t=net(ts[j]),b=null;for(i=0;i<e.length;i++){var x=e[i],tx=x.tagName=='INPUT'?x.value:x.textContent;if(net(tx)==t&&vis(x))b=x;}" +
                    "if(b){var p=null,ps=document.querySelectorAll('input[type=password]');for(i=0;i<ps.length;i++)if(vis(ps[i])){p=ps[i];break;}" +
                    "return (p?mig(p):'')+'|'+mig(b)+'|'+j;}}return '';})(" + new JavaScriptSerializer().Serialize(passes) + ")";
                string r = Convert.ToString(Edge.Avalua(c.Pag, js, 4000));
                if (!string.IsNullOrEmpty(r) && r.IndexOf('|') >= 0)
                {
                    string[] pp = r.Split('|');
                    if (pp[0] != "") Desperta(c, pp[0]);
                    ClicReal(c, pp[1]);
                    Registre(c, "toca \"" + passes[int.Parse(pp[2])] + "\"");
                    Thread.Sleep(2500);
                    // despres de tocar, la pagina torna a la posicio del retall
                    if (c.Sx > 0 || c.Sy > 0) Edge.Avalua(c.Pag, string.Format(System.Globalization.CultureInfo.InvariantCulture, "(function(){{window.scrollTo({0},{1});return 1;}})()", c.Sx, c.Sy), 3000);
                    return;
                }
            }
        }
        catch { }
        // cap text de les passes a la vista: si la web demana iniciar sessio i l'Edge ja hi ha posat les dades, s'entra sol
        try { EntraSol(c); } catch { }
    }

    // Formulari d'inici de sessio emplenat per l'Edge (contrasenyes desades): un clic de debo al camp (perque l'Edge
    // deixi llegir el que ha emplenat) i un altre al boto d'entrar
    const string LOGIN_JS = "(function(){function vis(e){var r=e.getBoundingClientRect();return r.width>20&&r.height>8&&getComputedStyle(e).visibility!='hidden';}" +
        "function mig(e){e.scrollIntoView({block:'center'});var r=e.getBoundingClientRect();return Math.round(r.left+r.width/2)+','+Math.round(r.top+r.height/2);}" +
        "var ps=document.querySelectorAll('input[type=password]'),p=null,i;for(i=0;i<ps.length;i++)if(vis(ps[i])){p=ps[i];break;}if(!p)return '';" +
        "var f=p.form||p.closest('form')||p.parentElement;for(i=0;i<6&&f&&f.parentElement&&!f.querySelector('button,input[type=submit],[role=button],a');i++)f=f.parentElement;" +
        "var re=/^(log ?in|sign ?in|entrar|iniciar|acceder|login|connexi|connecter|se connecter|anmelden|aceder|inicio)/i,bs=(f||document).querySelectorAll('button,input[type=submit],[role=button],a,div,span'),b=null;" +
        "for(i=0;i<bs.length;i++){var t=(bs[i].innerText||bs[i].value||'').replace(/\\s+/g,' ').replace(/^ | $/g,'');if(t.length<30&&re.test(t)&&vis(bs[i])&&(!b||b.contains(bs[i])))b=bs[i];}" +
        "if(!b){b=(f||document).querySelector('button[type=submit],input[type=submit]')||(f||document).querySelector('button');}" +
        "return mig(p)+'|'+(b&&vis(b)?mig(b):'');})()";

    // Una camera: el retall segueix el video (el mes gran de la pagina), sigui on sigui, encara que la web canvi\u00ef de disseny
    static void BuscaVideo(Captura c)
    {
        c.Buscat = DateTime.Now;
        try
        {
            string r = Convert.ToString(Edge.Avalua(c.Pag, "(function(){var v=document.querySelectorAll('video,canvas'),b=null,a=0,i,r;" +
                "for(i=0;i<v.length;i++){r=v[i].getBoundingClientRect();if(r.width>160&&r.height>90&&r.width*r.height>a){a=r.width*r.height;b=r;}}" +
                "if(!b)return '';var x=Math.max(0,b.left),y=Math.max(0,b.top),w=Math.min(innerWidth,b.right)-x,h=Math.min(innerHeight,b.bottom)-y;" +
                "if(w<160||h<90)return '';return [x,y,w,h].map(Math.round).join(',');})()", 3000));
            if (string.IsNullOrEmpty(r)) return;
            string[] a = r.Split(',');
            lock (c.Lk)
            {
                c.Cx = double.Parse(a[0], System.Globalization.CultureInfo.InvariantCulture); c.Cy = double.Parse(a[1], System.Globalization.CultureInfo.InvariantCulture);
                c.Cw = double.Parse(a[2], System.Globalization.CultureInfo.InvariantCulture); c.Ch = double.Parse(a[3], System.Globalization.CultureInfo.InvariantCulture);
                c.Retall = true; c.VideoTrobat = true;
            }
        }
        catch { }
    }

    static void ClicReal(Captura c, string xy)
    {
        string[] a = xy.Split(',');
        double x = double.Parse(a[0], System.Globalization.CultureInfo.InvariantCulture), y = double.Parse(a[1], System.Globalization.CultureInfo.InvariantCulture);
        c.Pag.Envia("Input.dispatchMouseEvent", CasaServidor.Dic("type", "mouseMoved", "x", x, "y", y), 5000);
        c.Pag.Envia("Input.dispatchMouseEvent", CasaServidor.Dic("type", "mousePressed", "x", x, "y", y, "button", "left", "buttons", 1, "clickCount", 1), 5000);
        c.Pag.Envia("Input.dispatchMouseEvent", CasaServidor.Dic("type", "mouseReleased", "x", x, "y", y, "button", "left", "buttons", 0, "clickCount", 1), 5000);
    }

    // Que ha fet el servidor amb les cameres (dades/cameres-diagnostic.txt, les darreres linies)
    static void Registre(Captura c, string txt)
    {
        try
        {
            string f = Path.Combine(CasaServidor.DirDades, "cameres-diagnostic.txt");
            string l = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " " + c.Id + " " + txt + Environment.NewLine;
            if (File.Exists(f) && new FileInfo(f).Length > 60000) File.WriteAllText(f, "");
            File.AppendAllText(f, l);
            Console.WriteLine("  Cameres: " + c.Id + " " + txt);
        }
        catch { }
    }

    // L'Edge omple l'usuari i la contrasenya desats, pero la web (Vue, React...) no se n'assabenta fins que algu escriu:
    // un clic de debo al camp, un espai escrit i esborrat (esdeveniments d'entrada de debo amb tot el valor) i, per si de cas,
    // els esdeveniments input/change a tots els camps plens. Despres, Escape tanca el desplegable de contrasenyes.
    static void Desperta(Captura c, string xy)
    {
        ClicReal(c, xy);
        Thread.Sleep(500);
        try { NavTecla(c.Pag, "End"); } catch { }
        try { c.Pag.Envia("Input.insertText", CasaServidor.Dic("text", " "), 3000); NavTecla(c.Pag, "Backspace"); } catch { }
        try { Edge.Avalua(c.Pag, "(function(){var l=document.querySelectorAll('input'),i,n=0;for(i=0;i<l.length;i++)if(l[i].value){l[i].dispatchEvent(new Event('input',{bubbles:true}));l[i].dispatchEvent(new Event('change',{bubbles:true}));n++;}return n;})()", 3000); } catch { }
        try { NavTecla(c.Pag, "Escape"); } catch { }
        Thread.Sleep(250);
    }

    static bool EntraSol(Captura c)
    {
        string r = Convert.ToString(Edge.Avalua(c.Pag, LOGIN_JS, 4000));
        if (string.IsNullOrEmpty(r) || r.IndexOf('|') < 0) return false;
        string[] pb = r.Split('|');
        // com a molt un intent cada 30 s, per no bloquejar el compte
        if ((DateTime.Now - c.Entrat).TotalSeconds < 30) return true;
        c.Entrat = DateTime.Now;
        // l'Edge nomes deixa fer servir la contrasenya emplenada despres d'un gest de debo: primer un clic al camp
        Desperta(c, pb[0]);
        if (pb[1] != "") ClicReal(c, pb[1]);
        else { ClicReal(c, pb[0]); NavTecla(c.Pag, "Enter"); }
        Registre(c, "inici de sessio: camp " + pb[0] + ", boto " + (pb[1] != "" ? pb[1] : "(Enter)"));
        Thread.Sleep(4000);
        if (c.Sx > 0 || c.Sy > 0) Edge.Avalua(c.Pag, string.Format(System.Globalization.CultureInfo.InvariantCulture, "(function(){{window.scrollTo({0},{1});return 1;}})()", c.Sx, c.Sy), 3000);
        return true;
    }

    static void Clic(string id, double fx, double fy)
    {
        Captura c = Obre(id);
        double x = c.Cx + fx * c.Cw, y = c.Cy + fy * c.Ch;
        if (c.Mobil)
        {
            // webs de mobil: un toc de debo (moltes nomes escolten tocs)
            object[] pt = new object[] { CasaServidor.Dic("x", x, "y", y) };
            c.Pag.Envia("Input.dispatchTouchEvent", CasaServidor.Dic("type", "touchStart", "touchPoints", pt), 5000);
            c.Pag.Envia("Input.dispatchTouchEvent", CasaServidor.Dic("type", "touchEnd", "touchPoints", new object[0]), 5000);
        }
        else
        {
            c.Pag.Envia("Input.dispatchMouseEvent", CasaServidor.Dic("type", "mouseMoved", "x", x, "y", y), 5000);
            Thread.Sleep(40);
            c.Pag.Envia("Input.dispatchMouseEvent", CasaServidor.Dic("type", "mousePressed", "x", x, "y", y, "button", "left", "buttons", 1, "clickCount", 1), 5000);
            c.Pag.Envia("Input.dispatchMouseEvent", CasaServidor.Dic("type", "mouseReleased", "x", x, "y", y, "button", "left", "buttons", 0, "clickCount", 1), 5000);
        }
        c.Quan = DateTime.MinValue;
    }

    static void Roda(string id, int dy, int dx, double fx, double fy)
    {
        Captura c = Obre(id);
        // on es mou importa: una llista o un mapa dins la pagina es mou ella sola
        double x = c.Cx + (fx >= 0 ? fx : 0.5) * c.Cw, y = c.Cy + (fy >= 0 ? fy : 0.5) * c.Ch;
        c.Pag.Envia("Input.dispatchMouseEvent", CasaServidor.Dic("type", "mouseWheel", "x", x, "y", y, "deltaX", dx, "deltaY", dy), 5000);
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
                        byte[] b = Imatge(id, CasaServidor.Q(q, "completa") == "1", Math.Max(id.StartsWith("nav-") ? 80 : 300, min));
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
                    {
                        int dx; int.TryParse(CasaServidor.Q(q, "dx"), out dx);
                        double fx, fy;
                        if (!double.TryParse(CasaServidor.Q(q, "x"), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out fx)) fx = -1;
                        if (!double.TryParse(CasaServidor.Q(q, "y"), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out fy)) fy = -1;
                        Roda(id, int.Parse(CasaServidor.Q(q, "d")), dx, fx, fy);
                    }
                    CasaServidor.Text(res, "{\"ok\":true}", "application/json");
                    return;
                case "nav-obre":
                    {
                        int w, h; int.TryParse(CasaServidor.Q(q, "w"), out w); int.TryParse(CasaServidor.Q(q, "h"), out h);
                        if (!id.StartsWith("nav-")) throw new Exception("id");
                        w = Math.Max(240, Math.Min(2000, w)); h = Math.Max(240, Math.Min(2000, h));
                        double dsf;
                        if (!double.TryParse(CasaServidor.Q(q, "dsf"), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out dsf)) dsf = 1;
                        dsf = Math.Max(1, Math.Min(2, dsf));
                        ObreNav(id, w, h, CasaServidor.Q(q, "mobil") == "1", dsf);
                        CasaServidor.Text(res, "{\"ok\":true}", "application/json");
                        return;
                    }
                case "nav-ves":
                    {
                        Captura c = Obre(id);
                        c.Zoom = 1;
                        c.Pag.Envia("Page.navigate", CasaServidor.Dic("url", NavUrl(CasaServidor.Q(q, "u"))), 15000);
                        c.Quan = DateTime.MinValue;
                        CasaServidor.Text(res, "{\"ok\":true}", "application/json");
                        return;
                    }
                case "nav-enrere":
                case "nav-endavant":
                    {
                        Captura c = Obre(id);
                        Edge.Avalua(c.Pag, nom == "nav-enrere" ? "history.back(),1" : "history.forward(),1", 5000);
                        c.Quan = DateTime.MinValue;
                        CasaServidor.Text(res, "{\"ok\":true}", "application/json");
                        return;
                    }
                case "nav-escriu":
                    {
                        // substitueix el que hi ha al camp actiu pel text (i, si cal, prem Enter)
                        Captura c = Obre(id);
                        Edge.Avalua(c.Pag, "(function(){var e=document.activeElement;if(!e)return 0;if(e.isContentEditable){document.execCommand('selectAll');}else if(e.select){e.select();}return 1;})()", 5000);
                        string t = CasaServidor.Q(q, "t");
                        if (t != "") c.Pag.Envia("Input.insertText", CasaServidor.Dic("text", t), 5000);
                        else NavTecla(c.Pag, "Backspace");
                        if (CasaServidor.Q(q, "enter") == "1") NavTecla(c.Pag, "Enter");
                        c.Quan = DateTime.MinValue;
                        CasaServidor.Text(res, "{\"ok\":true}", "application/json");
                        return;
                    }
                case "nav-recarrega":
                    {
                        Captura c = Obre(id);
                        c.Pag.Envia("Page.reload", null, 10000);
                        c.Quan = DateTime.MinValue;
                        CasaServidor.Text(res, "{\"ok\":true}", "application/json");
                        return;
                    }
                case "nav-pessic":
                    {
                        // ampliar o reduir la web (pessigar a la pantalla): zoom CSS de la pagina, mantenint el punt pessigat al seu lloc
                        Captura c = Obre(id);
                        double fx, fy, sc;
                        double.TryParse(CasaServidor.Q(q, "x"), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out fx);
                        double.TryParse(CasaServidor.Q(q, "y"), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out fy);
                        if (!double.TryParse(CasaServidor.Q(q, "s"), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out sc)) sc = 1;
                        double nou = Math.Max(0.5, Math.Min(4, c.Zoom * sc));
                        double px = fx * c.W, py = fy * c.H, f = nou / c.Zoom;
                        c.Zoom = nou;
                        Edge.Avalua(c.Pag, string.Format(System.Globalization.CultureInfo.InvariantCulture,
                            "(function(){{var d=document.documentElement,x=window.scrollX,y=window.scrollY;d.style.zoom={0};window.scrollTo((x+{1})*{3}-{1},(y+{2})*{3}-{2});return 1;}})()", nou, px, py, f), 5000);
                        c.Quan = DateTime.MinValue;
                        CasaServidor.Text(res, "{\"ok\":true}", "application/json");
                        return;
                    }
                case "nav-tecla":
                    {
                        Captura c = Obre(id);
                        NavTecla(c.Pag, CasaServidor.Q(q, "k"));
                        c.Quan = DateTime.MinValue;
                        CasaServidor.Text(res, "{\"ok\":true}", "application/json");
                        return;
                    }
                case "nav-que":
                    {
                        // que hi ha en aquest punt de la pagina: si es un camp per escriure, l'aparell obre el teclat de seguida
                        Captura c = Obre(id);
                        double fx, fy;
                        double.TryParse(CasaServidor.Q(q, "x"), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out fx);
                        double.TryParse(CasaServidor.Q(q, "y"), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out fy);
                        string js = string.Format(System.Globalization.CultureInfo.InvariantCulture,
                            "(function(){{var e=document.elementFromPoint({0},{1});if(!e)return {{edita:false}};var t=(e.tagName||'').toLowerCase(),ty=(e.type||'').toLowerCase();" +
                            "var ed=t=='textarea'||(t=='input'&&!/^(button|submit|checkbox|radio|range|color|file|image|reset|hidden)$/.test(ty))||e.isContentEditable===true;" +
                            "return {{edita:ed,valor:ed?(e.isContentEditable?(e.innerText||''):(e.value||'')).substring(0,2000):'',contrasenya:ty=='password'}};}})()",
                            fx * c.Cw, fy * c.Ch);
                        object v = Edge.Avalua(c.Pag, js, 3000);
                        CasaServidor.Text(res, new JavaScriptSerializer().Serialize(v), "application/json");
                        return;
                    }
                case "nav-info":
                    {
                        Captura c = Obre(id);
                        object v = Edge.Avalua(c.Pag, NAV_INFO, 5000);
                        Dictionary<string, object> vi = v as Dictionary<string, object>;
                        if (vi != null && vi.ContainsKey("url") && vi["url"] != null) lock (lk) navAdreca[id] = vi["url"].ToString();
                        CasaServidor.Text(res, new JavaScriptSerializer().Serialize(v), "application/json");
                        return;
                    }
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
