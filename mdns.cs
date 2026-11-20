// Anuncia el servidor a la xarxa de casa amb un nom fix (hearth.local) per mDNS/Bonjour,
// perque l'iPad el trobi encara que canvii la IP del PC. C# 5.
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;

public static class Mdns
{
    static UdpClient udp;
    static Timer rejunta;
    public static string Nom = "hearth";
    static readonly IPAddress Grup = IPAddress.Parse("224.0.0.251");

    static List<IPAddress> Adreces() { return Adreces(false); }
    // totes=true inclou adaptadors virtuals (p. ex. el punt d'acces Wi-Fi del PC, 192.168.137.1)
    static List<IPAddress> Adreces(bool totes)
    {
        List<IPAddress> l = new List<IPAddress>();
        try
        {
            foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up || ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                string d = (ni.Description + " " + ni.Name).ToLowerInvariant();
                if (!totes && (d.Contains("virtual") || d.Contains("vmware") || d.Contains("hyper-v") || d.Contains("vpn") || d.Contains("tailscale") || d.Contains("bluetooth"))) continue;
                foreach (UnicastIPAddressInformation a in ni.GetIPProperties().UnicastAddresses)
                    if (a.Address.AddressFamily == AddressFamily.InterNetwork && !a.Address.ToString().StartsWith("169.254")) l.Add(a.Address);
            }
        }
        catch { }
        return l;
    }

    public static void Inicia(string nom)
    {
        if (!string.IsNullOrEmpty(nom)) Nom = nom.ToLowerInvariant();
        Thread t = new Thread(Escolta);
        t.IsBackground = true;
        t.Start();
    }

    static void Escolta()
    {
        for (int intent = 0; intent < 1000; intent++)
        {
            try
            {
                udp = new UdpClient();
                udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                udp.Client.Bind(new IPEndPoint(IPAddress.Any, 5353));
                foreach (IPAddress a in Adreces(true)) { try { udp.JoinMulticastGroup(Grup, a); } catch { } }
                try { udp.JoinMulticastGroup(Grup); } catch { }
                Anuncia();
                // Si despres s'activa el punt d'acces o canvia la xarxa, s'hi torna a apuntar
                rejunta = new Timer(delegate { foreach (IPAddress a in Adreces(true)) { try { udp.JoinMulticastGroup(Grup, a); } catch { } } }, null, 60000, 60000);
                while (true)
                {
                    IPEndPoint de = new IPEndPoint(IPAddress.Any, 0);
                    byte[] b = udp.Receive(ref de);
                    try { Respon(b, de); } catch { }
                }
            }
            catch (Exception e)
            {
                Console.WriteLine("  mDNS (" + Nom + ".local): " + e.Message);
                try { udp.Close(); } catch { }
                Thread.Sleep(30000);
            }
        }
    }

    // Llegeix un nom DNS (amb punters de compressio)
    static string LlegeixNom(byte[] b, ref int o)
    {
        StringBuilder sb = new StringBuilder();
        int salts = 0, pos = o;
        bool saltat = false;
        while (pos < b.Length && salts < 20)
        {
            int n = b[pos];
            if (n == 0) { pos++; break; }
            if ((n & 0xC0) == 0xC0)
            {
                int p = (n & 0x3F) << 8 | b[pos + 1];
                if (!saltat) o = pos + 2;
                saltat = true; pos = p; salts++;
                continue;
            }
            if (sb.Length > 0) sb.Append('.');
            sb.Append(Encoding.UTF8.GetString(b, pos + 1, n));
            pos += n + 1;
        }
        if (!saltat) o = pos;
        return sb.ToString();
    }

    static void Respon(byte[] b, IPEndPoint de)
    {
        if (b.Length < 12) return;
        int flags = b[2] << 8 | b[3];
        if ((flags & 0x8000) != 0) return;            // es una resposta, no una pregunta
        int qd = b[4] << 8 | b[5], o = 12;
        string jo = Nom + ".local";
        for (int i = 0; i < qd && o < b.Length; i++)
        {
            string n = LlegeixNom(b, ref o);
            if (o + 4 > b.Length) return;
            int tipus = b[o] << 8 | b[o + 1], classe = (b[o + 2] << 8 | b[o + 3]) & 0x7FFF;
            bool unicast = (b[o + 2] & 0x80) != 0;
            o += 4;
            if (!string.Equals(n, jo, StringComparison.OrdinalIgnoreCase) || (tipus != 1 && tipus != 255) || classe != 1) continue;
            byte[] r = Resposta(b[0], b[1], de.Address);
            if (r == null) return;
            if (unicast || de.Port != 5353) udp.Send(r, r.Length, de);
            else udp.Send(r, r.Length, new IPEndPoint(Grup, 5353));
            return;
        }
    }

    // Resposta amb un registre A: la IP de la xarxa d'on ve la pregunta (si n'hi ha diverses)
    static byte[] Resposta(byte id0, byte id1, IPAddress qui)
    {
        List<IPAddress> l = Adreces();
        IPAddress tria = l.Count > 0 ? l[0] : null;
        if (qui != null)
        {
            // La IP de la mateixa xarxa que l'aparell que pregunta (inclou el punt d'acces del PC)
            byte[] q = qui.GetAddressBytes();
            foreach (IPAddress a in Adreces(true)) { byte[] x = a.GetAddressBytes(); if (x[0] == q[0] && x[1] == q[1] && x[2] == q[2]) { tria = a; break; } }
        }
        if (tria == null) return null;
        List<byte> r = new List<byte>();
        r.AddRange(new byte[] { 0, 0, 0x84, 0x00, 0, 0, 0, 1, 0, 0, 0, 0 });
        foreach (string part in (Nom + ".local").Split('.')) { byte[] p = Encoding.UTF8.GetBytes(part); r.Add((byte)p.Length); r.AddRange(p); }
        r.Add(0);
        r.AddRange(new byte[] { 0, 1, 0x80, 1, 0, 0, 0, 120, 0, 4 });   // A, IN (cache flush), TTL 120 s, 4 bytes
        r.AddRange(tria.GetAddressBytes());
        return r.ToArray();
    }

    static void Anuncia()
    {
        try
        {
            byte[] r = Resposta(0, 0, null);
            if (r != null) for (int i = 0; i < 2; i++) { udp.Send(r, r.Length, new IPEndPoint(Grup, 5353)); Thread.Sleep(1000); }
        }
        catch { }
    }
}
