// Pantalles de Hearth connectades ara mateix (iPad, m\u00f2bil, PC...): per enviar el que sona d'una a l'altra.
// Cada pantalla diu \u00abhola\u00bb cada pocs segons i, alhora, recull les ordres que li han enviat. C# 5.
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Web.Script.Serialization;

public static class Pantalles
{
    class P { public string Nom = ""; public bool Tele; public DateTime Vist; public List<object> Ordres = new List<object>(); }
    static readonly Dictionary<string, P> llista = new Dictionary<string, P>();
    static readonly object lk = new object();

    // pantalles/hola?d=&nom=  \u00b7  pantalles/llista?d=  \u00b7  pantalles/envia?a=  (POST: l'ordre en JSON)
    public static void Api(string nom, Dictionary<string, string> q, HttpListenerContext ctx)
    {
        HttpListenerResponse res = ctx.Response;
        string d = CasaServidor.Q(q, "d");
        if (nom == "hola")
        {
            if (d == "") { CasaServidor.Error(res, 400, "Falta d"); return; }
            object[] ordres;
            lock (lk)
            {
                P p;
                if (!llista.TryGetValue(d, out p)) { p = new P(); llista[d] = p; }
                p.Vist = DateTime.Now;
                string n = CasaServidor.Q(q, "nom");
                if (n != "") p.Nom = n;
                p.Tele = CasaServidor.Q(q, "tele") == "1";
                ordres = p.Ordres.ToArray();
                p.Ordres.Clear();
            }
            // l'hora del servidor serveix perque totes les pantalles comencin alhora
            if (CasaServidor.Q(q, "v") == "2") CasaServidor.Json(res, CasaServidor.Dic("o", ordres, "t", (long)(DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalMilliseconds));
            else CasaServidor.Json(res, ordres);
            return;
        }
        if (nom == "llista")
        {
            List<object> r = new List<object>();
            lock (lk)
                foreach (KeyValuePair<string, P> kv in llista)
                    if (kv.Key != d && (DateTime.Now - kv.Value.Vist).TotalSeconds < 25)
                        r.Add(CasaServidor.Dic("id", kv.Key, "nom", kv.Value.Nom, "tele", kv.Value.Tele));
            CasaServidor.Json(res, r.ToArray());
            return;
        }
        if (nom == "envia" && ctx.Request.HttpMethod == "POST")
        {
            string a = CasaServidor.Q(q, "a"), text;
            using (StreamReader sr = new StreamReader(ctx.Request.InputStream, Encoding.UTF8)) text = sr.ReadToEnd();
            JavaScriptSerializer js = new JavaScriptSerializer();
            js.MaxJsonLength = int.MaxValue;
            object o = js.DeserializeObject(text);
            lock (lk)
            {
                P p;
                if (!llista.TryGetValue(a, out p) || (DateTime.Now - p.Vist).TotalSeconds > 25) { CasaServidor.Error(res, 404, "Aquella pantalla ja no est\u00e0 connectada"); return; }
                if (p.Ordres.Count > 10) p.Ordres.RemoveAt(0);
                p.Ordres.Add(o);
            }
            CasaServidor.Json(res, CasaServidor.Dic("ok", true));
            return;
        }
        CasaServidor.Error(res, 404, "No");
    }
}
