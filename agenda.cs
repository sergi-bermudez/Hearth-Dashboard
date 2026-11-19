// Agenda familiar: llegeix calendaris (iCal / Google Calendar) i torna els propers
// esdeveniments. Accepta enllacos "embed" de Google (calendaris publics) i adreces iCal. C# 5.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;

public static class Agenda
{
    static readonly object lk = new object();
    static readonly Dictionary<string, KeyValuePair<DateTime, string>> cau = new Dictionary<string, KeyValuePair<DateTime, string>>();
    static readonly string[] ColorsDef = { "#4f8cff", "#ff6b6b", "#3fd18b", "#ffab3d", "#b16cff", "#22c4c4", "#ff5fa2", "#c0ca33" };

    class Esdev { public string Uid, Titol, Lloc, Rrule; public DateTime Ini, Fi; public bool TotDia, Cancel; public DateTime? RecId; public List<DateTime> Exdates = new List<DateTime>(); }

    static string FitxerAjustos { get { return Path.Combine(CasaServidor.DirDades, "agenda.json"); } }

    // agenda.json: { google: true, cals: { "<id>": { mostra: true, color: "#..." } }, enllacos: [ { nom, url, color } ] }
    static Dictionary<string, object> Ajustos()
    {
        try
        {
            if (File.Exists(FitxerAjustos))
            {
                object o = new JavaScriptSerializer().DeserializeObject(File.ReadAllText(FitxerAjustos, Encoding.UTF8));
                Dictionary<string, object> d = o as Dictionary<string, object>;
                if (d != null) return d;
                if (o is object[]) { d = new Dictionary<string, object>(); d["enllacos"] = o; return d; }
            }
        }
        catch { }
        return new Dictionary<string, object>();
    }

    static List<Dictionary<string, object>> Calendaris()
    {
        List<Dictionary<string, object>> r = new List<Dictionary<string, object>>();
        object l0;
        object[] l = Ajustos().TryGetValue("enllacos", out l0) ? l0 as object[] : null;
        if (l != null) foreach (object o in l) { Dictionary<string, object> d = o as Dictionary<string, object>; if (d != null && S(d, "url") != "") r.Add(d); }
        return r;
    }

    // ---------- Google Calendar amb la sessio de l'Edge del servidor ----------
    static List<string[]> google;           // { id, nom, ics }
    static DateTime googleData = DateTime.MinValue;
    static string googleError = "";
    static readonly object lkG = new object();

    const string JsExporta = @"(async () => {
  const r = await fetch('https://calendar.google.com/calendar/exporticalzip', { credentials: 'include' });
  const b = new Uint8Array(await r.arrayBuffer());
  if (b.length < 22 || b[0] !== 0x50 || b[1] !== 0x4b) return JSON.stringify({ sessio: false });
  const dv = new DataView(b.buffer);
  let e = b.length - 22;
  while (e >= 0 && dv.getUint32(e, true) !== 0x06054b50) e--;
  if (e < 0) return JSON.stringify({ sessio: false });
  const n = dv.getUint16(e + 10, true);
  let p = dv.getUint32(e + 16, true);
  const avui = new Date(Date.now() - 3 * 864e5), tall = '' + avui.getFullYear() + String(avui.getMonth() + 1).padStart(2, '0') + String(avui.getDate()).padStart(2, '0');
  const out = [];
  for (let i = 0; i < n; i++) {
    const met = dv.getUint16(p + 10, true), csz = dv.getUint32(p + 20, true), nl = dv.getUint16(p + 28, true), el = dv.getUint16(p + 30, true), cl = dv.getUint16(p + 32, true), off = dv.getUint32(p + 42, true);
    const nom = new TextDecoder().decode(b.subarray(p + 46, p + 46 + nl));
    p += 46 + nl + el + cl;
    const lnl = dv.getUint16(off + 26, true), lel = dv.getUint16(off + 28, true), ini = off + 30 + lnl + lel;
    const dades = b.subarray(ini, ini + csz);
    let txt;
    if (met === 0) txt = new TextDecoder().decode(dades);
    else txt = await new Response(new Blob([dades]).stream().pipeThrough(new DecompressionStream('deflate-raw'))).text();
    // Nomes els esdeveniments que es repeteixen o que son d'ara endavant (molt menys text)
    const parts = txt.replace(/\r\n[ \t]/g, '').split('BEGIN:VEVENT');
    let ics = parts[0];
    for (let k = 1; k < parts.length; k++) {
      const m = /\nDTSTART[^:]*:(\d{8})/.exec('\n' + parts[k]);
      if (/\nRRULE:/.test('\n' + parts[k]) || /\nRECURRENCE-ID/.test('\n' + parts[k]) || !m || m[1] >= tall) ics += 'BEGIN:VEVENT' + parts[k];
    }
    if (ics.indexOf('END:VCALENDAR') < 0) ics += '\r\nEND:VCALENDAR';
    out.push({ nom: nom, ics: ics });
  }
  return JSON.stringify({ sessio: true, cals: out });
})()";

    static List<string[]> Google(bool forca)
    {
        lock (lkG)
        {
            if (!forca && google != null && (DateTime.Now - googleData).TotalMinutes < 15) return google;
            if (!forca && google == null && googleError != "" && (DateTime.Now - googleData).TotalMinutes < 5) return null;
            string target = null;
            Cdp c = new Cdp();
            try
            {
                Edge.Assegura();
                target = Edge.NovaPagina("about:blank");
                c.Connecta(Edge.WsPagina(target));
                c.Envia("Page.enable", null, 5000);
                // Una pagina petita del mateix domini perque el fetch porti les galetes de la sessio
                c.Envia("Page.navigate", CasaServidor.Dic("url", "https://calendar.google.com/robots.txt"), 15000);
                for (int i = 0; i < 40; i++) { Thread.Sleep(250); object st = Edge.Avalua(c, "document.readyState", 3000); if (st != null && st.ToString() == "complete") break; }
                object v = Edge.Avalua(c, JsExporta, 60000);
                JavaScriptSerializer js = new JavaScriptSerializer();
                js.MaxJsonLength = int.MaxValue;
                Dictionary<string, object> d = v == null ? null : js.DeserializeObject(v.ToString()) as Dictionary<string, object>;
                if (d == null || !(d["sessio"] is bool) || !(bool)d["sessio"]) throw new Exception("Cal iniciar sessio a Google (Configuracio > Google Fotos > Inicia sessio)");
                List<string[]> r = new List<string[]>();
                object[] cals = d["cals"] as object[];
                if (cals != null)
                    foreach (object o in cals)
                    {
                        Dictionary<string, object> x = o as Dictionary<string, object>;
                        if (x == null) continue;
                        string fitxer = S(x, "nom"), ics = x["ics"] as string ?? "";
                        string id = fitxer.EndsWith(".ics", StringComparison.OrdinalIgnoreCase) ? fitxer.Substring(0, fitxer.Length - 4) : fitxer;
                        int sl = id.LastIndexOf('/'); if (sl >= 0) id = id.Substring(sl + 1);
                        Match m = Regex.Match(ics, @"X-WR-CALNAME:([^\r\n]*)");
                        string nom = m.Success ? Desescapa(m.Groups[1].Value) : id;
                        r.Add(new string[] { id, nom, ics });
                    }
                google = r; googleData = DateTime.Now; googleError = "";
                return r;
            }
            catch (Exception e) { googleError = e.Message; googleData = DateTime.Now; return google; }
            finally { c.Tanca(); if (target != null) { try { Edge.TancaPagina(target); } catch { } } }
        }
    }

    static Dictionary<string, object> AjustCal(string id)
    {
        object c0, x;
        Dictionary<string, object> cals = Ajustos().TryGetValue("cals", out c0) ? c0 as Dictionary<string, object> : null;
        return cals != null && cals.TryGetValue(id, out x) ? x as Dictionary<string, object> : null;
    }
    static bool GoogleActiu() { object g; Dictionary<string, object> a = Ajustos(); return !a.TryGetValue("google", out g) || g == null || g.ToString().ToLowerInvariant() != "false"; }

    static string S(Dictionary<string, object> d, string k) { object v; return d != null && d.TryGetValue(k, out v) && v != null ? v.ToString().Trim() : ""; }

    // Un enllac pot contenir diversos calendaris (embed amb diversos src=). Torna parells (url ics, color)
    static List<string[]> Fonts(string url, string color)
    {
        List<string[]> r = new List<string[]>();
        url = (url ?? "").Trim();
        if (url.StartsWith("webcal://", StringComparison.OrdinalIgnoreCase)) url = "https://" + url.Substring(9);
        if (url.IndexOf("calendar.google.com", StringComparison.OrdinalIgnoreCase) >= 0 && url.IndexOf("/ical/", StringComparison.OrdinalIgnoreCase) < 0)
        {
            int qi = url.IndexOf('?');
            string qs = qi >= 0 ? url.Substring(qi + 1) : "";
            List<string> srcs = new List<string>(), cols = new List<string>();
            foreach (string part in qs.Split('&'))
            {
                int e = part.IndexOf('=');
                if (e < 0) continue;
                string k = part.Substring(0, e).ToLowerInvariant(), v = Uri.UnescapeDataString(part.Substring(e + 1).Replace("+", " "));
                if (k == "src" || k == "cid") srcs.Add(v);
                else if (k == "color") cols.Add(v);
            }
            for (int i = 0; i < srcs.Count; i++)
            {
                string id = srcs[i];
                if (!id.Contains("@") && k64(id)) { try { id = Encoding.UTF8.GetString(Convert.FromBase64String(Pad(id))); } catch { } }
                string c = color != "" ? color : (i < cols.Count ? cols[i] : "");
                r.Add(new string[] { "https://calendar.google.com/calendar/ical/" + Uri.EscapeDataString(id) + "/public/basic.ics", c });
            }
            return r;
        }
        r.Add(new string[] { url, color });
        return r;
    }
    static bool k64(string s) { return Regex.IsMatch(s, "^[A-Za-z0-9+/=_-]+$") && s.Length % 4 != 1; }
    static string Pad(string s) { s = s.Replace('-', '+').Replace('_', '/'); while (s.Length % 4 != 0) s += "="; return s; }

    static string Baixa(string url)
    {
        lock (lk)
        {
            KeyValuePair<DateTime, string> c;
            if (cau.TryGetValue(url, out c) && (DateTime.Now - c.Key).TotalMinutes < 10) return c.Value;
        }
        HttpWebRequest rq = (HttpWebRequest)WebRequest.Create(url);
        rq.UserAgent = "Hearth/3.0";
        rq.Timeout = 15000; rq.ReadWriteTimeout = 15000;
        rq.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
        string t;
        using (WebResponse rs = rq.GetResponse())
        using (StreamReader sr = new StreamReader(rs.GetResponseStream(), Encoding.UTF8)) t = sr.ReadToEnd();
        if (t.IndexOf("BEGIN:VCALENDAR", StringComparison.Ordinal) < 0) throw new Exception("No es un calendari (potser no es public?)");
        lock (lk) cau[url] = new KeyValuePair<DateTime, string>(DateTime.Now, t);
        return t;
    }

    // ---------- Zones horaries (Windows no coneix els noms "Europe/Madrid") ----------
    static readonly Dictionary<string, string> Zones = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
        { "Europe/Madrid", "Romance Standard Time" }, { "Europe/Paris", "Romance Standard Time" }, { "Europe/Brussels", "Romance Standard Time" },
        { "Europe/Andorra", "W. Europe Standard Time" }, { "Europe/Berlin", "W. Europe Standard Time" }, { "Europe/Rome", "W. Europe Standard Time" },
        { "Europe/Amsterdam", "W. Europe Standard Time" }, { "Europe/Zurich", "W. Europe Standard Time" }, { "Europe/Vienna", "W. Europe Standard Time" },
        { "Europe/Lisbon", "GMT Standard Time" }, { "Europe/London", "GMT Standard Time" }, { "Europe/Dublin", "GMT Standard Time" },
        { "Atlantic/Canary", "GMT Standard Time" }, { "Atlantic/Madeira", "GMT Standard Time" }, { "Atlantic/Azores", "Azores Standard Time" },
        { "Europe/Athens", "GTB Standard Time" }, { "Europe/Helsinki", "FLE Standard Time" }, { "Europe/Warsaw", "Central European Standard Time" },
        { "America/New_York", "Eastern Standard Time" }, { "America/Chicago", "Central Standard Time" }, { "America/Denver", "Mountain Standard Time" },
        { "America/Los_Angeles", "Pacific Standard Time" }, { "America/Sao_Paulo", "E. South America Standard Time" }, { "America/Mexico_City", "Central Standard Time (Mexico)" },
        { "America/Argentina/Buenos_Aires", "Argentina Standard Time" }, { "America/Bogota", "SA Pacific Standard Time" }, { "UTC", "UTC" }, { "Etc/UTC", "UTC" }, { "GMT", "UTC" }
    };
    static DateTime ALocal(DateTime t, string tzid)
    {
        if (string.IsNullOrEmpty(tzid)) return t;
        string win;
        TimeZoneInfo z = null;
        try { z = TimeZoneInfo.FindSystemTimeZoneById(Zones.TryGetValue(tzid, out win) ? win : tzid); } catch { }
        if (z == null) return t;
        try { return TimeZoneInfo.ConvertTime(DateTime.SpecifyKind(t, DateTimeKind.Unspecified), z, TimeZoneInfo.Local); } catch { return t; }
    }

    // Llegeix un valor de data: torna (data local, es tot el dia)
    static DateTime Data(string param, string valor, out bool totDia)
    {
        totDia = false;
        string tz = "";
        Match m = Regex.Match(param, "TZID=\"?([^;:\"]+)", RegexOptions.IgnoreCase);
        if (m.Success) tz = m.Groups[1].Value;
        valor = valor.Trim();
        if (valor.Length == 8 || param.IndexOf("VALUE=DATE", StringComparison.OrdinalIgnoreCase) >= 0 && valor.Length == 8)
        {
            totDia = true;
            return DateTime.ParseExact(valor, "yyyyMMdd", CultureInfo.InvariantCulture);
        }
        bool utc = valor.EndsWith("Z");
        DateTime t = DateTime.ParseExact(valor.TrimEnd('Z').Substring(0, 15), "yyyyMMdd'T'HHmmss", CultureInfo.InvariantCulture);
        if (utc) return DateTime.SpecifyKind(t, DateTimeKind.Utc).ToLocalTime();
        return ALocal(t, tz);
    }

    static string Desescapa(string s) { return s.Replace("\\n", " ").Replace("\\N", " ").Replace("\\,", ",").Replace("\\;", ";").Replace("\\\\", "\\").Trim(); }

    static TimeSpan Durada(string d)
    {
        Match m = Regex.Match(d, @"P(?:(\d+)W)?(?:(\d+)D)?(?:T(?:(\d+)H)?(?:(\d+)M)?(?:(\d+)S)?)?");
        if (!m.Success) return TimeSpan.Zero;
        Func<int, int> g = delegate (int i) { return m.Groups[i].Success ? int.Parse(m.Groups[i].Value) : 0; };
        return new TimeSpan(g(1) * 7 + g(2), g(3), g(4), g(5));
    }

    static List<Esdev> Llegeix(string ics)
    {
        List<Esdev> r = new List<Esdev>();
        string[] linies = Regex.Replace(ics, "\r?\n[ \t]", "").Split(new char[] { '\n' });
        Esdev e = null;
        TimeSpan? dur = null;
        foreach (string l0 in linies)
        {
            string l = l0.TrimEnd('\r');
            if (l == "BEGIN:VEVENT") { e = new Esdev(); dur = null; continue; }
            if (l == "END:VEVENT")
            {
                if (e != null && e.Ini != DateTime.MinValue)
                {
                    if (e.Fi == DateTime.MinValue) e.Fi = dur.HasValue ? e.Ini + dur.Value : (e.TotDia ? e.Ini.AddDays(1) : e.Ini.AddHours(1));
                    r.Add(e);
                }
                e = null; continue;
            }
            if (e == null) continue;
            int c = l.IndexOf(':');
            if (c < 0) continue;
            string clau = l.Substring(0, c), val = l.Substring(c + 1);
            int pc = clau.IndexOf(';');
            string nom = (pc >= 0 ? clau.Substring(0, pc) : clau).ToUpperInvariant(), param = pc >= 0 ? clau.Substring(pc + 1) : "";
            bool td;
            try
            {
                switch (nom)
                {
                    case "UID": e.Uid = val; break;
                    case "SUMMARY": e.Titol = Desescapa(val); break;
                    case "LOCATION": e.Lloc = Desescapa(val); break;
                    case "STATUS": e.Cancel = val.Trim().ToUpperInvariant() == "CANCELLED"; break;
                    case "DTSTART": e.Ini = Data(param, val, out td); e.TotDia = td; break;
                    case "DTEND": e.Fi = Data(param, val, out td); break;
                    case "DURATION": dur = Durada(val); break;
                    case "RRULE": e.Rrule = val; break;
                    case "RECURRENCE-ID": e.RecId = Data(param, val, out td); break;
                    case "EXDATE": foreach (string x in val.Split(',')) e.Exdates.Add(Data(param, x, out td)); break;
                }
            }
            catch { }
        }
        return r;
    }

    static readonly string[] Dies = { "SU", "MO", "TU", "WE", "TH", "FR", "SA" };

    // Totes les ocurrencies d'un esdeveniment que comencen abans de "fins" (n'hi ha prou per a l'agenda)
    static List<DateTime> Ocurrencies(Esdev e, DateTime des, DateTime fins)
    {
        List<DateTime> r = new List<DateTime>();
        TimeSpan llarg = e.Fi - e.Ini;
        if (string.IsNullOrEmpty(e.Rrule)) { if (e.Ini < fins && e.Ini + llarg > des) r.Add(e.Ini); return r; }
        Dictionary<string, string> p = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string x in e.Rrule.Split(';')) { int i = x.IndexOf('='); if (i > 0) p[x.Substring(0, i)] = x.Substring(i + 1); }
        string freq; p.TryGetValue("FREQ", out freq);
        int interval = 1, count = int.MaxValue;
        string s;
        if (p.TryGetValue("INTERVAL", out s)) int.TryParse(s, out interval);
        if (interval < 1) interval = 1;
        if (p.TryGetValue("COUNT", out s)) int.TryParse(s, out count);
        DateTime until = DateTime.MaxValue;
        bool td;
        if (p.TryGetValue("UNTIL", out s)) { try { until = Data("", s, out td); if (td) until = until.AddDays(1).AddSeconds(-1); } catch { } }
        List<string> byday = new List<string>();
        if (p.TryGetValue("BYDAY", out s)) byday.AddRange(s.Split(','));
        int n = 0;
        DateTime limit = fins < until ? fins : until;
        Func<DateTime, bool> afegeix = delegate (DateTime t)
        {
            if (t < e.Ini) return true;
            if (t > limit || n >= count) return false;
            n++;
            bool exclosa = false;
            foreach (DateTime x in e.Exdates) if (Math.Abs((x - t).TotalMinutes) < 1) exclosa = true;
            if (!exclosa && t + llarg > des && t < fins) r.Add(t);
            return true;
        };
        if (freq == "DAILY")
        {
            for (DateTime t = e.Ini; t <= limit && n < count; t = t.AddDays(interval)) if (!afegeix(t)) break;
        }
        else if (freq == "WEEKLY")
        {
            List<int> dies = new List<int>();
            foreach (string d in byday) { int k = Array.IndexOf(Dies, d.Trim().Substring(Math.Max(0, d.Trim().Length - 2))); if (k >= 0) dies.Add(k); }
            if (dies.Count == 0) dies.Add((int)e.Ini.DayOfWeek);
            dies.Sort();
            DateTime setmana = e.Ini.Date.AddDays(-(((int)e.Ini.DayOfWeek + 6) % 7));
            for (int guard = 0; guard < 2000 && setmana <= limit && n < count; guard++, setmana = setmana.AddDays(7 * interval))
            {
                bool seguir = true;
                foreach (int d in new int[] { 1, 2, 3, 4, 5, 6, 0 })
                {
                    if (!dies.Contains(d)) continue;
                    DateTime t = setmana.AddDays((d + 6) % 7) + e.Ini.TimeOfDay;
                    if (!afegeix(t)) { seguir = false; break; }
                }
                if (!seguir) break;
            }
        }
        else if (freq == "MONTHLY")
        {
            for (int k = 0; k < 1200 && n < count; k += interval)
            {
                DateTime mes = new DateTime(e.Ini.Year, e.Ini.Month, 1).AddMonths(k);
                if (mes > limit) break;
                List<DateTime> cands = new List<DateTime>();
                if (byday.Count > 0)
                    foreach (string d0 in byday)
                    {
                        string d = d0.Trim();
                        int dw = Array.IndexOf(Dies, d.Substring(Math.Max(0, d.Length - 2)));
                        int ord; int.TryParse(d.Substring(0, Math.Max(0, d.Length - 2)), out ord);
                        if (dw < 0) continue;
                        List<DateTime> llista = new List<DateTime>();
                        for (DateTime x = mes; x.Month == mes.Month; x = x.AddDays(1)) if ((int)x.DayOfWeek == dw) llista.Add(x);
                        if (ord > 0 && ord <= llista.Count) cands.Add(llista[ord - 1]);
                        else if (ord < 0 && -ord <= llista.Count) cands.Add(llista[llista.Count + ord]);
                        else if (ord == 0) cands.AddRange(llista);
                    }
                else if (e.Ini.Day <= DateTime.DaysInMonth(mes.Year, mes.Month)) cands.Add(new DateTime(mes.Year, mes.Month, e.Ini.Day));
                cands.Sort();
                bool seguir = true;
                foreach (DateTime c in cands) if (!afegeix(c.Date + e.Ini.TimeOfDay)) { seguir = false; break; }
                if (!seguir) break;
            }
        }
        else if (freq == "YEARLY")
        {
            for (int k = 0; k < 200 && n < count; k += interval)
            {
                int any = e.Ini.Year + k;
                if (e.Ini.Month == 2 && e.Ini.Day == 29 && !DateTime.IsLeapYear(any)) continue;
                DateTime t = new DateTime(any, e.Ini.Month, e.Ini.Day) + e.Ini.TimeOfDay;
                if (!afegeix(t)) break;
            }
        }
        else if (e.Ini < fins && e.Fi > des) r.Add(e.Ini);
        return r;
    }

    static string Hora(DateTime t) { return t.ToString("yyyy-MM-dd'T'HH:mm", CultureInfo.InvariantCulture); }

    public static object Propers(int dies, List<string> errors)
    {
        DateTime des = DateTime.Today, fins = DateTime.Today.AddDays(dies);
        List<Dictionary<string, object>> r = new List<Dictionary<string, object>>();
        // Fonts: { nom, color, url o null, ics o null }
        List<string[]> fonts = new List<string[]>();
        int ci = 0;
        if (GoogleActiu())
        {
            List<string[]> g = Google(false);
            if (g == null) { if (googleError != "") errors.Add("Google: " + googleError); }
            else
                foreach (string[] gc in g)
                {
                    Dictionary<string, object> aj = AjustCal(gc[0]);
                    string col = S(aj, "color");
                    if (col == "") col = ColorsDef[ci % ColorsDef.Length];
                    ci++;
                    if (aj != null && S(aj, "mostra").ToLowerInvariant() == "false") continue;
                    if (aj == null && Regex.IsMatch(gc[0], "(holiday|#contacts|#weeknum)", RegexOptions.IgnoreCase)) continue;
                    fonts.Add(new string[] { gc[1], col, null, gc[2] });
                }
        }
        foreach (Dictionary<string, object> cal in Calendaris())
            foreach (string[] f in Fonts(S(cal, "url"), S(cal, "color")))
            {
                fonts.Add(new string[] { S(cal, "nom"), f[1] != "" ? f[1] : ColorsDef[ci % ColorsDef.Length], f[0], null });
                ci++;
            }
        foreach (string[] font in fonts)
        {
            string nomCal = font[0], color = font[1];
            {
                List<Esdev> l;
                try { l = Llegeix(font[3] ?? Baixa(font[2])); }
                catch (Exception ex) { errors.Add((nomCal != "" ? nomCal : font[2]) + ": " + ex.Message); continue; }
                // Instancies modificades (RECURRENCE-ID) substitueixen l'original
                Dictionary<string, List<DateTime>> canviades = new Dictionary<string, List<DateTime>>();
                foreach (Esdev e in l) if (e.RecId.HasValue && e.Uid != null) { if (!canviades.ContainsKey(e.Uid)) canviades[e.Uid] = new List<DateTime>(); canviades[e.Uid].Add(e.RecId.Value); }
                foreach (Esdev e in l)
                {
                    if (e.Cancel && !e.RecId.HasValue) continue;
                    if (!e.RecId.HasValue && e.Uid != null && canviades.ContainsKey(e.Uid)) e.Exdates.AddRange(canviades[e.Uid]);
                    if (e.Cancel) continue;
                    foreach (DateTime t in Ocurrencies(e, des, fins))
                    {
                        Dictionary<string, object> x = new Dictionary<string, object>();
                        x["t"] = string.IsNullOrEmpty(e.Titol) ? "(sense titol)" : e.Titol;
                        x["ini"] = Hora(t);
                        x["fi"] = Hora(t + (e.Fi - e.Ini));
                        x["dia"] = e.TotDia;
                        x["lloc"] = e.Lloc ?? "";
                        x["color"] = color;
                        x["cal"] = nomCal;
                        r.Add(x);
                    }
                }
            }
        }
        r.Sort(delegate (Dictionary<string, object> a, Dictionary<string, object> b)
        {
            int c = string.CompareOrdinal((string)a["ini"], (string)b["ini"]);
            if (c != 0) return c;
            return ((bool)b["dia"]).CompareTo((bool)a["dia"]);
        });
        if (r.Count > 200) r.RemoveRange(200, r.Count - 200);
        return r;
    }

    public static void Api(Dictionary<string, string> q, HttpListenerContext ctx)
    {
        int dies;
        if (!int.TryParse(CasaServidor.Q(q, "dies"), out dies) || dies < 1 || dies > 60) dies = 7;
        List<string> errors = new List<string>();
        Dictionary<string, object> o = new Dictionary<string, object>();
        o["esdeveniments"] = Propers(dies, errors);
        o["errors"] = errors;
        o["calendaris"] = Calendaris().Count + (google != null ? google.Count : 0);
        CasaServidor.Json(ctx.Response, o);
    }

    // Llista dels calendaris de Google (per a la configuracio)
    public static void Llista(Dictionary<string, string> q, HttpListenerContext ctx)
    {
        List<string[]> g = Google(CasaServidor.Q(q, "refresca") == "1");
        List<object> l = new List<object>();
        int ci = 0;
        if (g != null)
            foreach (string[] gc in g)
            {
                Dictionary<string, object> aj = AjustCal(gc[0]), x = new Dictionary<string, object>();
                string col = S(aj, "color");
                x["id"] = gc[0]; x["nom"] = gc[1];
                x["color"] = col != "" ? col : ColorsDef[ci % ColorsDef.Length];
                x["mostra"] = aj != null ? S(aj, "mostra").ToLowerInvariant() != "false" : !Regex.IsMatch(gc[0], "(holiday|#contacts|#weeknum)", RegexOptions.IgnoreCase);
                ci++;
                l.Add(x);
            }
        Dictionary<string, object> o = new Dictionary<string, object>();
        o["calendaris"] = l;
        o["error"] = googleError;
        o["actiu"] = GoogleActiu();
        CasaServidor.Json(ctx.Response, o);
    }
}
