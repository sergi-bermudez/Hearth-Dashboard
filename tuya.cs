// Tuya / Smart Life: control dels aparells de casa amb l'API oberta de Tuya (iot.tuya.com).
// Calen l'Access ID i l'Access Secret d'un projecte "Cloud" amb el compte de l'app Smart Life vinculat. C# 5.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

public static class Tuya
{
    static readonly object lk = new object();
    static string token, tokenId;
    static DateTime tokenFins = DateTime.MinValue;
    static string cauDisp;
    static DateTime cauData = DateTime.MinValue;
    static string uid;

    static string Fitxer { get { return Path.Combine(CasaServidor.DirDades, "tuya.json"); } }

    static Dictionary<string, object> Ajustos()
    {
        try
        {
            if (File.Exists(Fitxer))
            {
                Dictionary<string, object> d = new JavaScriptSerializer().DeserializeObject(File.ReadAllText(Fitxer, Encoding.UTF8)) as Dictionary<string, object>;
                if (d != null) return d;
            }
        }
        catch { }
        return new Dictionary<string, object>();
    }
    static string S(Dictionary<string, object> d, string k) { object v; return d != null && d.TryGetValue(k, out v) && v != null ? v.ToString().Trim() : ""; }

    static string Base()
    {
        string r = S(Ajustos(), "regio").ToLowerInvariant();
        switch (r)
        {
            case "us": return "https://openapi.tuyaus.com";
            case "usw": return "https://openapi-ueaz.tuyaus.com";
            case "cn": return "https://openapi.tuyacn.com";
            case "in": return "https://openapi.tuyain.com";
            case "weu": return "https://openapi-weaz.tuyaeu.com";
            default: return "https://openapi.tuyaeu.com";
        }
    }

    static string Hex(byte[] b) { StringBuilder sb = new StringBuilder(); foreach (byte x in b) sb.Append(x.ToString("x2")); return sb.ToString(); }
    static string Sha256(string s) { using (SHA256 h = SHA256.Create()) return Hex(h.ComputeHash(Encoding.UTF8.GetBytes(s ?? ""))); }
    static string Hmac(string clau, string s) { using (HMACSHA256 h = new HMACSHA256(Encoding.UTF8.GetBytes(clau))) return Hex(h.ComputeHash(Encoding.UTF8.GetBytes(s))).ToUpperInvariant(); }

    // Ordena els parametres de la consulta, com demana la signatura de Tuya
    static string CamiOrdenat(string cami)
    {
        int i = cami.IndexOf('?');
        if (i < 0) return cami;
        List<string> ps = new List<string>(cami.Substring(i + 1).Split('&'));
        ps.Sort(StringComparer.Ordinal);
        return cami.Substring(0, i) + "?" + string.Join("&", ps.ToArray());
    }

    static Dictionary<string, object> Crida(string metode, string cami, string cos, bool ambToken)
    {
        Dictionary<string, object> a = Ajustos();
        string id = S(a, "id"), secret = S(a, "secret");
        if (id == "" || secret == "") throw new Exception("Falta configurar Tuya (Configuraci\u00f3 \u25b8 Casa)");
        string tk = ambToken ? Token() : "";
        cami = CamiOrdenat(cami);
        string t = ((long)(DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalMilliseconds).ToString(CultureInfo.InvariantCulture);
        string nonce = Guid.NewGuid().ToString("N");
        string aSignar = metode + "\n" + Sha256(cos ?? "") + "\n\n" + cami;
        string sign = Hmac(secret, id + tk + t + nonce + aSignar);
        HttpWebRequest rq = (HttpWebRequest)WebRequest.Create(Base() + cami);
        rq.Method = metode;
        rq.Timeout = 12000; rq.ReadWriteTimeout = 12000;
        rq.Headers["client_id"] = id;
        rq.Headers["sign"] = sign;
        rq.Headers["t"] = t;
        rq.Headers["nonce"] = nonce;
        rq.Headers["sign_method"] = "HMAC-SHA256";
        if (tk != "") rq.Headers["access_token"] = tk;
        if (cos != null)
        {
            byte[] b = Encoding.UTF8.GetBytes(cos);
            rq.ContentType = "application/json";
            rq.ContentLength = b.Length;
            using (Stream st = rq.GetRequestStream()) st.Write(b, 0, b.Length);
        }
        string txt;
        try
        {
            using (WebResponse rs = rq.GetResponse())
            using (StreamReader sr = new StreamReader(rs.GetResponseStream(), Encoding.UTF8)) txt = sr.ReadToEnd();
        }
        catch (WebException e)
        {
            if (e.Response == null) throw;
            using (StreamReader sr = new StreamReader(e.Response.GetResponseStream(), Encoding.UTF8)) txt = sr.ReadToEnd();
        }
        JavaScriptSerializer js = new JavaScriptSerializer(); js.MaxJsonLength = int.MaxValue;
        Dictionary<string, object> d = js.DeserializeObject(txt) as Dictionary<string, object>;
        if (d == null) throw new Exception("Resposta estranya de Tuya");
        if (!(d.ContainsKey("success") && d["success"] is bool && (bool)d["success"]))
        {
            string codi = S(d, "code"), msg = S(d, "msg");
            if (codi == "1010" || codi == "1011") { lock (lk) { token = null; } }
            throw new Exception("Tuya: " + msg + (codi != "" ? " (" + codi + ")" : ""));
        }
        return d;
    }

    static string Token()
    {
        lock (lk)
        {
            if (token != null && DateTime.Now < tokenFins) return token;
        }
        Dictionary<string, object> d = Crida("GET", "/v1.0/token?grant_type=1", null, false);
        Dictionary<string, object> r = d["result"] as Dictionary<string, object>;
        lock (lk)
        {
            token = S(r, "access_token");
            tokenId = S(r, "uid");
            int exp; int.TryParse(S(r, "expire_time"), out exp);
            tokenFins = DateTime.Now.AddSeconds(Math.Max(300, exp - 120));
            return token;
        }
    }

    static object Resultat(Dictionary<string, object> d) { object r; return d.TryGetValue("result", out r) ? r : null; }

    // Llista d'aparells (amb el seu estat). Prova les maneres que ofereix l'API segons el tipus de projecte
    static List<object> Llista()
    {
        List<object> l = new List<object>();
        string err = null;
        try
        {
            string seguent = "";
            for (int pag = 0; pag < 10; pag++)
            {
                Dictionary<string, object> r = Resultat(Crida("GET", "/v1.0/iot-01/associated-users/devices?size=100" + (seguent != "" ? "&last_row_key=" + Uri.EscapeDataString(seguent) : ""), null, true)) as Dictionary<string, object>;
                object[] ds = r != null && r.ContainsKey("devices") ? r["devices"] as object[] : null;
                if (ds != null) l.AddRange(ds);
                bool mes = r != null && r.ContainsKey("has_more") && r["has_more"] is bool && (bool)r["has_more"];
                seguent = S(r, "last_row_key");
                if (!mes || seguent == "") break;
            }
        }
        catch (Exception e) { err = e.Message; }
        if (l.Count == 0)
        {
            string u = S(Ajustos(), "uid");
            if (u == "") u = uid ?? "";
            if (u != "")
            {
                try { object[] ds = Resultat(Crida("GET", "/v1.0/users/" + u + "/devices", null, true)) as object[]; if (ds != null) l.AddRange(ds); }
                catch (Exception e) { err = e.Message; }
            }
        }
        if (l.Count == 0 && err != null) throw new Exception(err);
        foreach (object o in l) { Dictionary<string, object> x = o as Dictionary<string, object>; if (x != null && S(x, "uid") != "") { uid = S(x, "uid"); break; } }
        return l;
    }

    static readonly string[] Interruptors = { "switch_led", "switch", "switch_1", "switch_2", "switch_3", "switch_4", "switch_usb1", "led_switch", "start" };

    static Dictionary<string, object> specs = new Dictionary<string, object>();

    static string Compacta(List<object> l)
    {
        List<object> r = new List<object>();
        foreach (object o in l)
        {
            Dictionary<string, object> d = o as Dictionary<string, object>;
            if (d == null) continue;
            Dictionary<string, object> x = new Dictionary<string, object>();
            x["id"] = S(d, "id");
            x["nom"] = S(d, "name");
            x["cat"] = S(d, "category");
            x["online"] = d.ContainsKey("online") && d["online"] is bool && (bool)d["online"];
            string ic = S(d, "icon");
            x["icona"] = ic == "" ? "" : (ic.StartsWith("http") ? ic : "https://images.tuyaeu.com/" + ic);
            Dictionary<string, object> st = new Dictionary<string, object>();
            object[] ss = d.ContainsKey("status") ? d["status"] as object[] : null;
            if (ss != null) foreach (object s in ss) { Dictionary<string, object> c = s as Dictionary<string, object>; if (c != null && S(c, "code") != "") st[S(c, "code")] = c.ContainsKey("value") ? c["value"] : null; }
            x["estat"] = st;
            r.Add(x);
        }
        return new JavaScriptSerializer().Serialize(r);
    }

    static Dictionary<string, object> Escenes()
    {
        Dictionary<string, object> o = new Dictionary<string, object>();
        List<object> l = new List<object>();
        if (uid == null) Llista();
        object[] cases = Resultat(Crida("GET", "/v1.0/users/" + uid + "/homes", null, true)) as object[];
        if (cases != null)
            foreach (object c0 in cases)
            {
                Dictionary<string, object> c = c0 as Dictionary<string, object>;
                string home = S(c, "home_id");
                if (home == "") continue;
                object[] es = null;
                try { es = Resultat(Crida("GET", "/v1.1/homes/" + home + "/scenes", null, true)) as object[]; } catch { }
                if (es == null) continue;
                foreach (object e0 in es)
                {
                    Dictionary<string, object> e = e0 as Dictionary<string, object>;
                    Dictionary<string, object> x = new Dictionary<string, object>();
                    x["id"] = S(e, "scene_id"); x["nom"] = S(e, "name"); x["casa"] = home;
                    x["img"] = S(e, "background");
                    l.Add(x);
                }
            }
        o["escenes"] = l;
        return o;
    }

    // Configuracio (nomes des de l'administracio): el secret no es torna mai sencer
    public static void Ajusta(HttpListenerContext ctx)
    {
        HttpListenerResponse res = ctx.Response;
        Dictionary<string, object> a = Ajustos();
        if (ctx.Request.HttpMethod == "POST")
        {
            string cos;
            using (StreamReader sr = new StreamReader(ctx.Request.InputStream, Encoding.UTF8)) cos = sr.ReadToEnd();
            Dictionary<string, object> d = new JavaScriptSerializer().DeserializeObject(cos) as Dictionary<string, object>;
            if (d == null) { CasaServidor.Error(res, 400, "JSON"); return; }
            foreach (string k in new string[] { "id", "regio", "uid" }) if (d.ContainsKey(k)) a[k] = S(d, k);
            if (S(d, "secret") != "") a["secret"] = S(d, "secret");
            File.WriteAllText(Fitxer, new JavaScriptSerializer().Serialize(a), new UTF8Encoding(false));
            lock (lk) { token = null; cauData = DateTime.MinValue; }
        }
        Dictionary<string, object> o = new Dictionary<string, object>();
        o["id"] = S(a, "id"); o["regio"] = S(a, "regio") == "" ? "eu" : S(a, "regio"); o["uid"] = S(a, "uid");
        o["teSecret"] = S(a, "secret") != "";
        CasaServidor.Json(res, o);
    }

    public static void Api(string nom, Dictionary<string, string> q, HttpListenerContext ctx)
    {
        HttpListenerResponse res = ctx.Response;
        string ct = "application/json; charset=utf-8";
        try
        {
            switch (nom)
            {
                case "dispositius":
                    {
                        string r;
                        lock (lk) { r = (DateTime.Now - cauData).TotalSeconds < 8 && CasaServidor.Q(q, "fresc") != "1" ? cauDisp : null; }
                        if (r == null)
                        {
                            r = Compacta(Llista());
                            lock (lk) { cauDisp = r; cauData = DateTime.Now; }
                        }
                        CasaServidor.Text(res, r, ct);
                        return;
                    }
                case "ordre":
                    {
                        // Cos: {"id":"...","codi":"switch_led","valor":true}
                        string cos;
                        using (StreamReader sr = new StreamReader(ctx.Request.InputStream, Encoding.UTF8)) cos = sr.ReadToEnd();
                        Dictionary<string, object> d = new JavaScriptSerializer().DeserializeObject(cos) as Dictionary<string, object>;
                        string id = S(d, "id"), codi = S(d, "codi");
                        if (id == "" || codi == "") { CasaServidor.Error(res, 400, "Falta l'aparell"); return; }
                        Dictionary<string, object> c = new Dictionary<string, object>();
                        c["code"] = codi; c["value"] = d.ContainsKey("valor") ? d["valor"] : null;
                        Dictionary<string, object> body = new Dictionary<string, object>();
                        body["commands"] = new object[] { c };
                        Crida("POST", "/v1.0/devices/" + id + "/commands", new JavaScriptSerializer().Serialize(body), true);
                        lock (lk) { cauData = DateTime.MinValue; }
                        CasaServidor.Text(res, "{\"ok\":true}", ct);
                        return;
                    }
                case "funcions":
                    {
                        string id = CasaServidor.Q(q, "id");
                        CasaServidor.Json(res, Resultat(Crida("GET", "/v1.0/devices/" + Uri.EscapeDataString(id) + "/functions", null, true)));
                        return;
                    }
                case "especificacions":
                    {
                        string id = CasaServidor.Q(q, "id");
                        object sp;
                        lock (specs) specs.TryGetValue(id, out sp);
                        if (sp == null)
                        {
                            sp = Resultat(Crida("GET", "/v1.0/devices/" + Uri.EscapeDataString(id) + "/specifications", null, true));
                            lock (specs) specs[id] = sp;
                        }
                        CasaServidor.Json(res, sp);
                        return;
                    }
                case "historial":
                    {
                        string id = CasaServidor.Q(q, "id"), codi = CasaServidor.Q(q, "codi");
                        long ara = (long)(DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalMilliseconds, des = ara - 24L * 3600 * 1000;
                        List<object> punts = new List<object>();
                        string fila = "";
                        for (int pag = 0; pag < 6; pag++)
                        {
                            Dictionary<string, object> r = Resultat(Crida("GET", "/v1.0/devices/" + Uri.EscapeDataString(id) + "/logs?type=7&size=100&codes=" + Uri.EscapeDataString(codi) + "&start_time=" + des + "&end_time=" + ara + (fila != "" ? "&start_row_key=" + Uri.EscapeDataString(fila) : ""), null, true)) as Dictionary<string, object>;
                            if (r == null) break;
                            object[] logs = r.ContainsKey("logs") ? r["logs"] as object[] : null;
                            if (logs != null) foreach (object o in logs)
                                {
                                    Dictionary<string, object> e = o as Dictionary<string, object>;
                                    if (e == null || !e.ContainsKey("value") || !e.ContainsKey("event_time")) continue;
                                    punts.Add(new object[] { e["event_time"], e["value"] });
                                }
                            bool mes = r.ContainsKey("has_next") && r["has_next"] is bool && (bool)r["has_next"];
                            fila = r.ContainsKey("next_row_key") && r["next_row_key"] != null ? r["next_row_key"].ToString() : "";
                            if (!mes || fila == "") break;
                        }
                        Dictionary<string, object> h = new Dictionary<string, object>();
                        h["punts"] = punts;
                        CasaServidor.Json(res, h);
                        return;
                    }
                case "escenes": CasaServidor.Json(res, Escenes()); return;
                case "escena":
                    Crida("POST", "/v1.0/homes/" + Uri.EscapeDataString(CasaServidor.Q(q, "casa")) + "/scenes/" + Uri.EscapeDataString(CasaServidor.Q(q, "id")) + "/trigger", "", true);
                    CasaServidor.Text(res, "{\"ok\":true}", ct);
                    return;
                case "prova":
                    {
                        lock (lk) { token = null; }
                        List<object> l = Llista();
                        Dictionary<string, object> o = new Dictionary<string, object>();
                        o["ok"] = true; o["aparells"] = l.Count;
                        CasaServidor.Json(res, o);
                        return;
                    }
            }
            CasaServidor.Error(res, 404, "API desconeguda");
        }
        catch (Exception e) { try { CasaServidor.Error(res, 502, e.Message); } catch { } }
    }
}
