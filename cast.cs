// Enviar el que sona a Hearth a un Chromecast / Google Nest / Google TV (protocol Cast)
// o a una tele que entengui DLNA (LG, Samsung, Sony...). L'iPad fa de comandament. C# 5.
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;

public static class Cast
{
    public class Disp
    {
        public string Id, Nom, Tipus, Model, Ip, Control, Volum;   // Tipus: "cast" o "dlna"
        public int Port;
        public DateTime Vist;
    }

    static readonly object lk = new object();
    static readonly Dictionary<string, Disp> disps = new Dictionary<string, Disp>();
    static DateTime cercat = DateTime.MinValue;
    static int cercant = 0;
    static readonly Dictionary<string, Sessio> sessions = new Dictionary<string, Sessio>();
    public static int Coneguts() { lock (lk) return disps.Count; }

    // ---------------------------------------------------------------- Descobriment
    static List<IPAddress> LocalsIp()
    {
        List<IPAddress> l = new List<IPAddress>();
        try
        {
            foreach (System.Net.NetworkInformation.NetworkInterface ni in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up || ni.NetworkInterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.Loopback) continue;
                foreach (System.Net.NetworkInformation.UnicastIPAddressInformation a in ni.GetIPProperties().UnicastAddresses)
                    if (a.Address.AddressFamily == AddressFamily.InterNetwork && !a.Address.ToString().StartsWith("169.254")) l.Add(a.Address);
            }
        }
        catch { }
        return l;
    }

    static void Cerca()
    {
        if (Interlocked.CompareExchange(ref cercant, 1, 0) != 0) return;
        try
        {
            Thread a = new Thread(CercaCast), b = new Thread(CercaDlna);
            a.IsBackground = true; b.IsBackground = true;
            a.Start(); b.Start();
            a.Join(6000); b.Join(8000);
            cercat = DateTime.Now;
        }
        finally { cercant = 0; }
    }

    // mDNS: pregunta per _googlecast._tcp.local (els aparells responen directament a aquest port)
    static void CercaCast()
    {
        byte[] q = PreguntaDns("_googlecast._tcp.local");
        List<UdpClient> us = new List<UdpClient>();
        foreach (IPAddress ip in LocalsIp())
        {
            try
            {
                UdpClient u = new UdpClient(new IPEndPoint(ip, 0));
                u.Client.ReceiveTimeout = 400;
                u.Send(q, q.Length, new IPEndPoint(IPAddress.Parse("224.0.0.251"), 5353));
                us.Add(u);
            }
            catch { }
        }
        DateTime fi = DateTime.Now.AddMilliseconds(2500);
        int envis = 1;
        while (DateTime.Now < fi && us.Count > 0)
        {
            foreach (UdpClient u in us)
            {
                try
                {
                    IPEndPoint de = new IPEndPoint(IPAddress.Any, 0);
                    byte[] r = u.Receive(ref de);
                    LlegeixDns(r, de.Address.ToString());
                }
                catch { }
            }
            if (envis < 2 && DateTime.Now > fi.AddMilliseconds(-1500)) { envis++; foreach (UdpClient u in us) { try { u.Send(q, q.Length, new IPEndPoint(IPAddress.Parse("224.0.0.251"), 5353)); } catch { } } }
        }
        foreach (UdpClient u in us) { try { u.Close(); } catch { } }
    }

    static byte[] PreguntaDns(string nom)
    {
        List<byte> b = new List<byte>(new byte[] { 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0 });
        foreach (string p in nom.Split('.')) { byte[] x = Encoding.ASCII.GetBytes(p); b.Add((byte)x.Length); b.AddRange(x); }
        b.Add(0);
        b.AddRange(new byte[] { 0, 12, 0, 1 });   // PTR, IN
        return b.ToArray();
    }

    static string NomDns(byte[] d, ref int p)
    {
        StringBuilder s = new StringBuilder();
        int salts = 0, tornada = -1;
        while (p < d.Length && salts < 20)
        {
            int n = d[p];
            if (n == 0) { p++; break; }
            if ((n & 0xC0) == 0xC0)
            {
                int o = ((n & 0x3F) << 8) | d[p + 1];
                if (tornada < 0) tornada = p + 2;
                p = o; salts++;
                continue;
            }
            p++;
            if (s.Length > 0) s.Append('.');
            s.Append(Encoding.UTF8.GetString(d, p, Math.Min(n, d.Length - p)));
            p += n;
        }
        if (tornada >= 0) p = tornada;
        return s.ToString();
    }

    static void LlegeixDns(byte[] d, string ip)
    {
        if (d.Length < 12) return;
        int qd = (d[4] << 8) | d[5], an = ((d[6] << 8) | d[7]) + ((d[8] << 8) | d[9]) + ((d[10] << 8) | d[11]);
        int p = 12;
        for (int i = 0; i < qd; i++) { NomDns(d, ref p); p += 4; }
        Dictionary<string, string> txt = new Dictionary<string, string>();
        int port = 8009;
        bool esCast = false;
        for (int i = 0; i < an && p + 10 <= d.Length; i++)
        {
            string nom = NomDns(d, ref p);
            if (p + 10 > d.Length) break;
            int tipus = (d[p] << 8) | d[p + 1];
            int len = (d[p + 8] << 8) | d[p + 9];
            p += 10;
            int ini = p;
            if (nom.ToLowerInvariant().Contains("_googlecast")) esCast = true;
            if (tipus == 16)   // TXT
            {
                int q = ini;
                while (q < ini + len && q < d.Length)
                {
                    int n = d[q++];
                    if (q + n > d.Length) break;
                    string kv = Encoding.UTF8.GetString(d, q, n);
                    q += n;
                    int e = kv.IndexOf('=');
                    if (e > 0) txt[kv.Substring(0, e)] = kv.Substring(e + 1);
                }
            }
            else if (tipus == 33 && len >= 6) port = (d[ini + 4] << 8) | d[ini + 5];   // SRV
            p = ini + len;
        }
        if (!esCast || !txt.ContainsKey("fn")) return;
        string id = "c-" + (txt.ContainsKey("id") ? txt["id"] : ip);
        lock (lk)
        {
            Disp x;
            if (!disps.TryGetValue(id, out x)) { x = new Disp(); disps[id] = x; }
            x.Id = id; x.Nom = txt["fn"]; x.Tipus = "cast"; x.Model = txt.ContainsKey("md") ? txt["md"] : "Chromecast";
            x.Ip = ip; x.Port = port; x.Vist = DateTime.Now;
        }
    }

    // SSDP: televisors DLNA (MediaRenderer)
    static void CercaDlna()
    {
        string msg = "M-SEARCH * HTTP/1.1\r\nHOST: 239.255.255.250:1900\r\nMAN: \"ssdp:discover\"\r\nMX: 2\r\nST: urn:schemas-upnp-org:device:MediaRenderer:1\r\n\r\n";
        byte[] b = Encoding.ASCII.GetBytes(msg);
        List<string> llocs = new List<string>();
        List<UdpClient> us = new List<UdpClient>();
        foreach (IPAddress ip in LocalsIp())
        {
            try
            {
                UdpClient u = new UdpClient(new IPEndPoint(ip, 0));
                u.Client.ReceiveTimeout = 400;
                u.Send(b, b.Length, new IPEndPoint(IPAddress.Parse("239.255.255.250"), 1900));
                us.Add(u);
            }
            catch { }
        }
        DateTime fi = DateTime.Now.AddMilliseconds(3000);
        while (DateTime.Now < fi && us.Count > 0)
            foreach (UdpClient u in us)
            {
                try
                {
                    IPEndPoint de = new IPEndPoint(IPAddress.Any, 0);
                    string r = Encoding.ASCII.GetString(u.Receive(ref de));
                    Match m = Regex.Match(r, @"(?im)^location:\s*(\S+)");
                    if (m.Success && !llocs.Contains(m.Groups[1].Value)) llocs.Add(m.Groups[1].Value);
                }
                catch { }
            }
        foreach (UdpClient u in us) { try { u.Close(); } catch { } }
        foreach (string loc in llocs)
        {
            try
            {
                string xml;
                using (WebClient wc = new WebClient()) { wc.Encoding = Encoding.UTF8; xml = wc.DownloadString(loc); }
                string nom = Etiqueta(xml, "friendlyName"), model = Etiqueta(xml, "modelName"), udn = Etiqueta(xml, "UDN");
                // Servei AVTransport i RenderingControl
                string ctrl = "", vol = "";
                foreach (Match s in Regex.Matches(xml, @"<service>([\s\S]*?)</service>", RegexOptions.IgnoreCase))
                {
                    string t = Etiqueta(s.Groups[1].Value, "serviceType"), c = Etiqueta(s.Groups[1].Value, "controlURL");
                    if (t.Contains("AVTransport")) ctrl = new Uri(new Uri(loc), c).ToString();
                    else if (t.Contains("RenderingControl")) vol = new Uri(new Uri(loc), c).ToString();
                }
                if (ctrl == "") continue;
                string id = "d-" + (udn != "" ? Regex.Replace(udn, "[^A-Za-z0-9-]", "") : new Uri(loc).Host);
                lock (lk)
                {
                    Disp x;
                    if (!disps.TryGetValue(id, out x)) { x = new Disp(); disps[id] = x; }
                    x.Id = id; x.Nom = nom != "" ? nom : "TV"; x.Model = model; x.Tipus = "dlna"; x.Ip = new Uri(loc).Host;
                    x.Control = ctrl; x.Volum = vol; x.Vist = DateTime.Now;
                }
            }
            catch { }
        }
    }

    static string Etiqueta(string xml, string nom)
    {
        Match m = Regex.Match(xml, "<(?:[A-Za-z0-9]+:)?" + nom + @"[^>]*>([\s\S]*?)</(?:[A-Za-z0-9]+:)?" + nom + ">", RegexOptions.IgnoreCase);
        return m.Success ? WebUtility.HtmlDecode(m.Groups[1].Value.Trim()) : "";
    }

    // ---------------------------------------------------------------- Chromecast (protocol Cast v2)
    class Sessio
    {
        public TcpClient Tcp;
        public SslStream S;
        public readonly object Esc = new object();
        public string Transport = "", SessioApp = "";
        public long MediaSessio = 0;
        public string Estat = "", Titol = "";
        public double Volum = -1;
        public bool Viu = true;
        public int ReqId = 10;
        public DateTime Darrer = DateTime.Now;
    }

    const string NsConn = "urn:x-cast:com.google.cast.tp.connection", NsBeat = "urn:x-cast:com.google.cast.tp.heartbeat",
        NsRec = "urn:x-cast:com.google.cast.receiver", NsMedia = "urn:x-cast:com.google.cast.media";

    static void Varint(MemoryStream m, long v) { while (v >= 0x80) { m.WriteByte((byte)(v | 0x80)); v >>= 7; } m.WriteByte((byte)v); }
    static void Camp(MemoryStream m, int n, string s) { byte[] b = Encoding.UTF8.GetBytes(s); m.WriteByte((byte)((n << 3) | 2)); Varint(m, b.Length); m.Write(b, 0, b.Length); }

    static void Envia(Sessio s, string desti, string ns, object cos)
    {
        string json = cos as string ?? new JavaScriptSerializer().Serialize(cos);
        MemoryStream m = new MemoryStream();
        m.WriteByte(0x08); Varint(m, 0);            // protocol_version = CASTV2_1_0
        Camp(m, 2, "sender-hearth");
        Camp(m, 3, desti);
        Camp(m, 4, ns);
        m.WriteByte(0x28); Varint(m, 0);            // payload_type = STRING
        Camp(m, 6, json);
        byte[] b = m.ToArray();
        byte[] mida = new byte[] { (byte)(b.Length >> 24), (byte)(b.Length >> 16), (byte)(b.Length >> 8), (byte)b.Length };
        lock (s.Esc) { s.S.Write(mida, 0, 4); s.S.Write(b, 0, b.Length); s.S.Flush(); }
    }

    static bool LlegeixTot(Stream st, byte[] b, int n)
    {
        int f = 0;
        while (f < n) { int r = st.Read(b, f, n - f); if (r <= 0) return false; f += r; }
        return true;
    }

    // Llegeix missatges fins que es tanca la connexio; respon als PING i apunta l'estat
    static void Lector(object o)
    {
        Sessio s = (Sessio)o;
        JavaScriptSerializer js = new JavaScriptSerializer();
        try
        {
            byte[] mida = new byte[4];
            while (s.Viu && LlegeixTot(s.S, mida, 4))
            {
                int n = (mida[0] << 24) | (mida[1] << 16) | (mida[2] << 8) | mida[3];
                if (n <= 0 || n > 1 << 20) break;
                byte[] b = new byte[n];
                if (!LlegeixTot(s.S, b, n)) break;
                string ns = "", cos = "", font = "";
                int p = 0;
                while (p < n)
                {
                    int tag = b[p++], camp = tag >> 3, tipus = tag & 7;
                    long v = 0; int sh = 0;
                    if (tipus == 0) { while (p < n) { byte x = b[p++]; v |= (long)(x & 0x7F) << sh; sh += 7; if ((x & 0x80) == 0) break; } continue; }
                    if (tipus != 2) break;
                    while (p < n) { byte x = b[p++]; v |= (long)(x & 0x7F) << sh; sh += 7; if ((x & 0x80) == 0) break; }
                    string t = Encoding.UTF8.GetString(b, p, (int)Math.Min(v, n - p));
                    p += (int)v;
                    if (camp == 2) font = t; else if (camp == 4) ns = t; else if (camp == 6) cos = t;
                }
                s.Darrer = DateTime.Now;
                Dictionary<string, object> d = null;
                try { d = js.DeserializeObject(cos) as Dictionary<string, object>; } catch { }
                if (d == null) continue;
                string ty = d.ContainsKey("type") ? d["type"] as string : "";
                if (ns == NsBeat && ty == "PING") { try { Envia(s, font, NsBeat, "{\"type\":\"PONG\"}"); } catch { } continue; }
                if (ty == "CLOSE" && font != "receiver-0") { s.Estat = "IDLE"; continue; }
                if (ty == "RECEIVER_STATUS" && d.ContainsKey("status"))
                {
                    Dictionary<string, object> st = d["status"] as Dictionary<string, object>;
                    if (st == null) continue;
                    if (st.ContainsKey("volume")) { Dictionary<string, object> vo = st["volume"] as Dictionary<string, object>; if (vo != null && vo.ContainsKey("level")) s.Volum = Convert.ToDouble(vo["level"]); }
                    object[] apps = st.ContainsKey("applications") ? st["applications"] as object[] : null;
                    if (apps != null && apps.Length > 0)
                    {
                        Dictionary<string, object> a = apps[0] as Dictionary<string, object>;
                        if (a != null) { s.Transport = a.ContainsKey("transportId") ? a["transportId"] as string : ""; s.SessioApp = a.ContainsKey("sessionId") ? a["sessionId"] as string : ""; }
                    }
                    else { s.Transport = ""; s.Estat = "IDLE"; }
                }
                if (ty == "MEDIA_STATUS" && d.ContainsKey("status"))
                {
                    object[] l = d["status"] as object[];
                    if (l != null && l.Length > 0)
                    {
                        Dictionary<string, object> m = l[0] as Dictionary<string, object>;
                        if (m != null)
                        {
                            if (m.ContainsKey("mediaSessionId")) s.MediaSessio = Convert.ToInt64(m["mediaSessionId"]);
                            if (m.ContainsKey("playerState")) s.Estat = m["playerState"] as string;
                        }
                    }
                }
            }
        }
        catch { }
        s.Viu = false;
        try { s.Tcp.Close(); } catch { }
    }

    static Sessio Connecta(Disp x)
    {
        lock (lk)
        {
            Sessio s;
            if (sessions.TryGetValue(x.Id, out s) && s.Viu) return s;
            s = new Sessio();
            s.Tcp = new TcpClient();
            IAsyncResult ar = s.Tcp.BeginConnect(x.Ip, x.Port, null, null);
            if (!ar.AsyncWaitHandle.WaitOne(5000)) { try { s.Tcp.Close(); } catch { } throw new Exception("No es pot connectar amb " + x.Nom); }
            s.Tcp.EndConnect(ar);
            s.S = new SslStream(s.Tcp.GetStream(), false, delegate (object a, X509Certificate c, X509Chain ch, SslPolicyErrors e) { return true; });
            s.S.AuthenticateAsClient(x.Ip);
            Envia(s, "receiver-0", NsConn, "{\"type\":\"CONNECT\"}");
            Thread t = new Thread(Lector); t.IsBackground = true; t.Start(s);
            // Batec cada 5 s perque no es tanqui la connexio
            Sessio ses = s;
            Thread bt = new Thread(delegate () { while (ses.Viu) { Thread.Sleep(5000); try { Envia(ses, "receiver-0", NsBeat, "{\"type\":\"PING\"}"); } catch { ses.Viu = false; } } });
            bt.IsBackground = true; bt.Start();
            sessions[x.Id] = s;
            return s;
        }
    }

    static void CastCarrega(Disp x, string url, string ct, bool directe, string titol, string img)
    {
        Sessio s = Connecta(x);
        s.Titol = titol;
        // Obre el reproductor per defecte de Google
        Envia(s, "receiver-0", NsRec, "{\"type\":\"LAUNCH\",\"appId\":\"CC1AD845\",\"requestId\":" + (++s.ReqId) + "}");
        for (int i = 0; i < 60 && (s.Transport == "" || s.Transport == null); i++) Thread.Sleep(150);
        if (string.IsNullOrEmpty(s.Transport)) throw new Exception(x.Nom + " no ha obert el reproductor");
        Envia(s, s.Transport, NsConn, "{\"type\":\"CONNECT\"}");
        Dictionary<string, object> meta = new Dictionary<string, object>();
        meta["metadataType"] = 0; meta["title"] = titol;
        if (!string.IsNullOrEmpty(img)) { Dictionary<string, object> im = new Dictionary<string, object>(); im["url"] = img; meta["images"] = new object[] { im }; }
        Dictionary<string, object> media = new Dictionary<string, object>();
        media["contentId"] = url; media["contentType"] = ct; media["streamType"] = directe ? "LIVE" : "BUFFERED"; media["metadata"] = meta;
        Dictionary<string, object> load = new Dictionary<string, object>();
        load["type"] = "LOAD"; load["requestId"] = ++s.ReqId; load["media"] = media; load["autoplay"] = true; load["currentTime"] = 0;
        s.MediaSessio = 0;
        Envia(s, s.Transport, NsMedia, load);
        for (int i = 0; i < 50 && s.MediaSessio == 0; i++) Thread.Sleep(200);
    }

    static void CastOrdre(Disp x, string o, double v)
    {
        Sessio s = Connecta(x);
        if (o == "vol")
        {
            double nou = Math.Max(0, Math.Min(1, (s.Volum < 0 ? 0.5 : s.Volum) + v));
            Envia(s, "receiver-0", NsRec, "{\"type\":\"SET_VOLUME\",\"volume\":{\"level\":" + nou.ToString(System.Globalization.CultureInfo.InvariantCulture) + "},\"requestId\":" + (++s.ReqId) + "}");
            s.Volum = nou;
            return;
        }
        if (o == "atura")
        {
            if (s.SessioApp != "") Envia(s, "receiver-0", NsRec, "{\"type\":\"STOP\",\"sessionId\":\"" + s.SessioApp + "\",\"requestId\":" + (++s.ReqId) + "}");
            s.Estat = "IDLE";
            return;
        }
        if (s.Transport == "" || s.MediaSessio == 0) return;
        string ty = o == "pausa" ? "PAUSE" : "PLAY";
        Envia(s, s.Transport, NsMedia, "{\"type\":\"" + ty + "\",\"mediaSessionId\":" + s.MediaSessio + ",\"requestId\":" + (++s.ReqId) + "}");
    }

    // ---------------------------------------------------------------- DLNA (LG, Samsung...)
    static string Soap(string url, string servei, string accio, string cos)
    {
        string xml = "<?xml version=\"1.0\" encoding=\"utf-8\"?><s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\" s:encodingStyle=\"http://schemas.xmlsoap.org/soap/encoding/\"><s:Body><u:" + accio + " xmlns:u=\"" + servei + "\">" + cos + "</u:" + accio + "></s:Body></s:Envelope>";
        HttpWebRequest rq = (HttpWebRequest)WebRequest.Create(url);
        rq.Method = "POST"; rq.ContentType = "text/xml; charset=\"utf-8\""; rq.Timeout = 8000;
        rq.Headers["SOAPACTION"] = "\"" + servei + "#" + accio + "\"";
        byte[] b = Encoding.UTF8.GetBytes(xml);
        rq.ContentLength = b.Length;
        using (Stream st = rq.GetRequestStream()) st.Write(b, 0, b.Length);
        try
        {
            using (WebResponse rs = rq.GetResponse())
            using (StreamReader sr = new StreamReader(rs.GetResponseStream())) return sr.ReadToEnd();
        }
        catch (WebException e)
        {
            string t = "";
            try { using (StreamReader sr = new StreamReader(e.Response.GetResponseStream())) t = sr.ReadToEnd(); } catch { }
            string err = Etiqueta(t, "errorDescription");
            throw new Exception("La tele ha dit: " + (err != "" ? err : e.Message));
        }
    }

    const string AvT = "urn:schemas-upnp-org:service:AVTransport:1", Rc = "urn:schemas-upnp-org:service:RenderingControl:1";

    static string X(string s) { return WebUtility.HtmlEncode(s ?? ""); }

    static void DlnaCarrega(Disp x, string url, string ct, string titol, bool video)
    {
        string didl = "<DIDL-Lite xmlns=\"urn:schemas-upnp-org:metadata-1-0/DIDL-Lite/\" xmlns:dc=\"http://purl.org/dc/elements/1.1/\" xmlns:upnp=\"urn:schemas-upnp-org:metadata-1-0/upnp/\">" +
            "<item id=\"1\" parentID=\"0\" restricted=\"1\"><dc:title>" + X(titol) + "</dc:title><upnp:class>" + (video ? "object.item.videoItem" : "object.item.audioItem.musicTrack") + "</upnp:class>" +
            "<res protocolInfo=\"http-get:*:" + ct + ":*\">" + X(url) + "</res></item></DIDL-Lite>";
        try { Soap(x.Control, AvT, "Stop", "<InstanceID>0</InstanceID>"); } catch { }
        Soap(x.Control, AvT, "SetAVTransportURI", "<InstanceID>0</InstanceID><CurrentURI>" + X(url) + "</CurrentURI><CurrentURIMetaData>" + X(didl) + "</CurrentURIMetaData>");
        Thread.Sleep(400);
        Soap(x.Control, AvT, "Play", "<InstanceID>0</InstanceID><Speed>1</Speed>");
    }

    static void DlnaOrdre(Disp x, string o, double v)
    {
        if (o == "pausa") Soap(x.Control, AvT, "Pause", "<InstanceID>0</InstanceID>");
        else if (o == "play") Soap(x.Control, AvT, "Play", "<InstanceID>0</InstanceID><Speed>1</Speed>");
        else if (o == "atura") Soap(x.Control, AvT, "Stop", "<InstanceID>0</InstanceID>");
        else if (o == "vol" && !string.IsNullOrEmpty(x.Volum))
        {
            int ara = 20;
            try { int.TryParse(Etiqueta(Soap(x.Volum, Rc, "GetVolume", "<InstanceID>0</InstanceID><Channel>Master</Channel>"), "CurrentVolume"), out ara); } catch { }
            int nou = Math.Max(0, Math.Min(100, ara + (int)Math.Round(v * 100)));
            Soap(x.Volum, Rc, "SetVolume", "<InstanceID>0</InstanceID><Channel>Master</Channel><DesiredVolume>" + nou + "</DesiredVolume>");
        }
    }

    // ---------------------------------------------------------------- API
    // L'adreca del PC que veu l'aparell (la de la mateixa xarxa)
    static string Base(Disp x, int port)
    {
        string ip = null;
        string[] p = (x.Ip ?? "").Split('.');
        foreach (IPAddress a in LocalsIp())
        {
            string[] q = a.ToString().Split('.');
            if (ip == null) ip = a.ToString();
            if (p.Length == 4 && q[0] == p[0] && q[1] == p[1] && q[2] == p[2]) { ip = a.ToString(); break; }
        }
        return "http://" + (ip ?? "127.0.0.1") + ":" + port;
    }

    static string Tipus(string u, string k)
    {
        string l = u.ToLowerInvariant();
        if (l.Contains(".m3u8") || l.Contains("/api/hls") || l.Contains("youtube/hls")) return "application/x-mpegURL";
        if (k == "radio") return l.Contains(".aac") ? "audio/aac" : l.Contains(".ogg") ? "audio/ogg" : "audio/mpeg";
        Match m = Regex.Match(l, @"\.(mp4|m4v|mov|mkv|webm|avi|mp3|m4a|aac|flac|ogg|opus|wav)(\?|&|$)");
        string e = m.Success ? m.Groups[1].Value : "";
        switch (e)
        {
            case "mp4": case "m4v": case "mov": return "video/mp4";
            case "mkv": return "video/x-matroska";
            case "webm": return "video/webm";
            case "avi": return "video/x-msvideo";
            case "mp3": return "audio/mpeg";
            case "m4a": case "aac": return "audio/mp4";
            case "flac": return "audio/flac";
            case "ogg": case "opus": return "audio/ogg";
            case "wav": return "audio/wav";
        }
        return k == "musica" || k == "podcast" ? "audio/mpeg" : "video/mp4";
    }

    public static void Api(string nom, Dictionary<string, string> q, HttpListenerContext ctx, int port)
    {
        HttpListenerResponse res = ctx.Response;
        try
        {
            if (nom == "cast/dispositius")
            {
                if (CasaServidor.Q(q, "ara") == "1" || (DateTime.Now - cercat).TotalMinutes > 2) Cerca();
                List<object> l = new List<object>();
                lock (lk)
                    foreach (Disp x in disps.Values)
                    {
                        if ((DateTime.Now - x.Vist).TotalMinutes > 30) continue;
                        Dictionary<string, object> o = new Dictionary<string, object>();
                        o["id"] = x.Id; o["nom"] = x.Nom; o["tipus"] = x.Tipus; o["model"] = x.Model ?? "";
                        l.Add(o);
                    }
                l.Sort(delegate (object a, object b) { return string.Compare((string)((Dictionary<string, object>)a)["nom"], (string)((Dictionary<string, object>)b)["nom"], StringComparison.OrdinalIgnoreCase); });
                CasaServidor.Json(res, l);
                return;
            }
            Disp d;
            lock (lk) disps.TryGetValue(CasaServidor.Q(q, "id"), out d);
            if (d == null) { CasaServidor.Error(res, 404, "No trobo l'aparell; torna a buscar-lo"); return; }
            if (nom == "cast/envia")
            {
                string u = CasaServidor.Q(q, "u"), k = CasaServidor.Q(q, "k"), titol = CasaServidor.Q(q, "t"), img = CasaServidor.Q(q, "img");
                if (u == "") { CasaServidor.Error(res, 400, "Res a enviar"); return; }
                string b = Base(d, port);
                bool directe = k == "radio" || k == "tv";
                // Les teles DLNA no saben https ni cap\u00e7aleres especials: tot el que ve de fora passa pel PC
                if (d.Tipus == "dlna" && (u.StartsWith("http://") || u.StartsWith("https://")) && !u.ToLowerInvariant().Contains(".m3u8")) u = "/api/x?directe=1&u=" + Uri.EscapeDataString(u);
                if (u.StartsWith("http") && u.ToLowerInvariant().Contains(".m3u8")) u = "/api/hls?u=" + Uri.EscapeDataString(u);
                if (u.StartsWith("/")) u = b + u;
                if (img.StartsWith("/")) img = b + img;
                string ct = Tipus(u, k);
                if (d.Tipus == "cast") CastCarrega(d, u, ct, directe, titol, img);
                else DlnaCarrega(d, u, ct, titol, ct.StartsWith("video") || ct.Contains("mpegURL"));
                Dictionary<string, object> o = new Dictionary<string, object>();
                o["ok"] = true; o["nom"] = d.Nom;
                CasaServidor.Json(res, o);
                return;
            }
            if (nom == "cast/ordre")
            {
                string o = CasaServidor.Q(q, "o");
                double v = 0; double.TryParse(CasaServidor.Q(q, "v"), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out v);
                if (d.Tipus == "cast") CastOrdre(d, o, v); else DlnaOrdre(d, o, v);
                CasaServidor.Text(res, "{\"ok\":true}", "application/json; charset=utf-8");
                return;
            }
            if (nom == "cast/estat")
            {
                Dictionary<string, object> o = new Dictionary<string, object>();
                Sessio s;
                lock (lk) sessions.TryGetValue(d.Id, out s);
                o["estat"] = s != null && s.Viu ? s.Estat : "";
                o["volum"] = s != null ? s.Volum : -1;
                CasaServidor.Json(res, o);
                return;
            }
            CasaServidor.Error(res, 404, "API desconeguda");
        }
        catch (Exception e) { try { CasaServidor.Error(res, 500, e.Message); } catch { } }
    }
}
