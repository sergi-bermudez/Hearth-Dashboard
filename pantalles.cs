// Pantalles de Hearth connectades ara mateix (iPad, m\u00f2bil, PC...): per enviar el que sona d'una a l'altra.
// Cada pantalla diu \u00abhola\u00bb cada pocs segons i, alhora, recull les ordres que li han enviat. C# 5.
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Runtime.InteropServices;
using System.Threading;
using System.Web.Script.Serialization;

public static class Pantalles
{
    class P { public string Nom = ""; public string Sona = ""; public bool Pausa; public bool Tele; public DateTime Vist; public List<object> Ordres = new List<object>(); public string Registre = ""; public DateTime RegQuan; }
    static readonly Dictionary<string, P> llista = new Dictionary<string, P>();
    static readonly object lk = new object();

    // ---------- Que el PC no s'adormi mentre hi hagi alguna pantalla connectada ----------
    [DllImport("kernel32.dll")] static extern uint SetThreadExecutionState(uint esFlags);
    const uint ES_CONTINUOUS = 0x80000000, ES_SYSTEM_REQUIRED = 0x00000001;
    static Thread vetlla;
    static void Vetlla()
    {
        bool abans = false;
        while (true)
        {
            bool algu = false;
            lock (lk) foreach (P p in llista.Values) if ((DateTime.Now - p.Vist).TotalSeconds < 90) { algu = true; break; }
            if (algu != abans)
            {
                // l'estat val per a aquest fil, que no s'acaba mai
                try { SetThreadExecutionState(algu ? ES_CONTINUOUS | ES_SYSTEM_REQUIRED : ES_CONTINUOUS); } catch { }
                abans = algu;
            }
            Thread.Sleep(30000);
        }
    }
    static void EngegaVetlla()
    {
        lock (lk)
        {
            if (vetlla != null) return;
            vetlla = new Thread(Vetlla); vetlla.IsBackground = true; vetlla.Start();
        }
    }

    // pantalles/hola?d=&nom=  \u00b7  pantalles/llista?d=  \u00b7  pantalles/envia?a=  (POST: l'ordre en JSON)
    public static void Api(string nom, Dictionary<string, string> q, HttpListenerContext ctx)
    {
        HttpListenerResponse res = ctx.Response;
        string d = CasaServidor.Q(q, "d");
        if (nom == "hola")
        {
            EngegaVetlla();
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
                // el que sona en aquella pantalla, per poder-la controlar des d'una altra
                p.Sona = CasaServidor.Q(q, "sona"); p.Pausa = CasaServidor.Q(q, "pausa") == "1";
                ordres = p.Ordres.ToArray();
                p.Ordres.Clear();
            }
            // l'hora del servidor serveix perque totes les pantalles comencin alhora
            if (CasaServidor.Q(q, "v") == "2") {
                int altres = 0;
                lock (lk) foreach (KeyValuePair<string, P> kv in llista) if (kv.Key != d && (DateTime.Now - kv.Value.Vist).TotalSeconds < 25) altres++;
                CasaServidor.Json(res, CasaServidor.Dic("o", ordres, "t", (long)(DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalMilliseconds, "n", altres, "ver", Actualitza.Local()));
            }
            else CasaServidor.Json(res, ordres);
            return;
        }
        if (nom == "llista")
        {
            List<object> r = new List<object>();
            lock (lk)
                foreach (KeyValuePair<string, P> kv in llista)
                    if (kv.Key != d && (DateTime.Now - kv.Value.Vist).TotalSeconds < 25)
                        r.Add(CasaServidor.Dic("id", kv.Key, "nom", kv.Value.Nom, "tele", kv.Value.Tele, "sona", kv.Value.Sona, "pausa", kv.Value.Pausa));
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
        // el registre del so de cada pantalla, per veure'l des d'una altra (p. ex. el de la tele des de l'iPad)
        if (nom == "registre" && ctx.Request.HttpMethod == "POST")
        {
            string text;
            using (StreamReader sr = new StreamReader(ctx.Request.InputStream, Encoding.UTF8)) text = sr.ReadToEnd();
            if (text.Length > 20000) text = text.Substring(text.Length - 20000);
            lock (lk)
            {
                P p;
                if (d == "" || !llista.TryGetValue(d, out p)) { CasaServidor.Error(res, 404, "No"); return; }
                p.Registre = text; p.RegQuan = DateTime.Now;
            }
            CasaServidor.Json(res, CasaServidor.Dic("ok", true));
            return;
        }
        if (nom == "registres")
        {
            List<object> r = new List<object>();
            lock (lk)
                foreach (KeyValuePair<string, P> kv in llista)
                    if (kv.Value.Registre != "")
                        r.Add(CasaServidor.Dic("id", kv.Key, "nom", kv.Value.Nom, "quan", kv.Value.RegQuan.ToString("HH:mm:ss"), "text", kv.Value.Registre));
            CasaServidor.Json(res, r.ToArray());
            return;
        }
        CasaServidor.Error(res, 404, "No");
    }
}
