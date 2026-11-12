using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace EditeurCarte
{
    // Mode navigateur (Linux / Proton : pas de WebView2 sous Wine -- demande d'un joueur, JD 12/11) : l'interface est servie sur
    // 127.0.0.1, port libre, sous un jeton aleatoire (http://127.0.0.1:<port>/<jeton>/index.html), et s'ouvre dans le navigateur du
    // joueur. Memes messages qu'avec WebView2 : page -> programme POST /<jeton>/api (JSON), programme -> page GET /<jeton>/evenements
    // (text/event-stream). Fichiers : /<jeton>/carte/... (cartes preparees), /<jeton>/projets/... (modeles et images des projets),
    // le reste dans ui\. Hote 127.0.0.1:<port> ou localhost:<port> seulement (pas de rebinding DNS), POST de notre origine seulement,
    // jamais un chemin hors de ces trois dossiers. Un seul fil par connexion, « Connection: close » (sauf le flux d'evenements).
    public class LocalServer
    {
        readonly TcpListener listener;
        public readonly int Port;
        public readonly string Token;
        readonly string ui, carte, projets;
        readonly Action<string> onMessage;   // (fil du serveur : MainForm repasse sur le fil de la fenetre)
        readonly List<Stream> streams = new List<Stream>();
        readonly List<string> pending = new List<string>();   // (messages envoyes avant que la page n'ecoute)
        readonly object gate = new object();
        public int Clients { get { lock (gate) return streams.Count; } }

        public string Url { get { return "http://127.0.0.1:" + Port + "/" + Token + "/index.html"; } }

        public LocalServer(string ui, string carte, string projets, Action<string> onMessage)
        {
            this.ui = Full(ui); this.carte = Full(carte); this.projets = Full(projets); this.onMessage = onMessage;
            var b = new byte[16];
            using (var r = System.Security.Cryptography.RandomNumberGenerator.Create()) r.GetBytes(b);
            Token = BitConverter.ToString(b).Replace("-", "").ToLowerInvariant();
            listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            Port = ((IPEndPoint)listener.LocalEndpoint).Port;
            new Thread(Accept) { IsBackground = true, Name = "serveur local" }.Start();
        }

        static string Full(string d) { return Path.GetFullPath(d).TrimEnd('\\', '/'); }

        public void Stop() { try { listener.Stop(); } catch { } lock (gate) { foreach (Stream s in streams) try { s.Close(); } catch { } streams.Clear(); } }

        // programme -> page
        public void Send(string json)
        {
            byte[] data = Encoding.UTF8.GetBytes("data: " + json + "\n\n");
            lock (gate)
            {
                if (streams.Count == 0) { if (pending.Count < 200) pending.Add(json); return; }
                Write(data);
            }
        }
        void Write(byte[] data)   // (sous gate)
        {
            for (int i = streams.Count - 1; i >= 0; i--)
                try { streams[i].Write(data, 0, data.Length); streams[i].Flush(); }
                catch { try { streams[i].Close(); } catch { } streams.RemoveAt(i); }
        }

        void Accept()
        {
            while (true)
            {
                TcpClient c;
                try { c = listener.AcceptTcpClient(); } catch { return; }
                ThreadPool.QueueUserWorkItem(_ => { try { Serve(c); } catch { } finally { try { c.Close(); } catch { } } });
            }
        }

        void Serve(TcpClient c)
        {
            c.NoDelay = true;
            NetworkStream ns = c.GetStream();
            ns.ReadTimeout = 15000;
            string line = ReadLine(ns);
            if (string.IsNullOrEmpty(line)) return;
            string[] rq = line.Split(' ');
            if (rq.Length < 3) { Reply(ns, 400, "text/plain", "requete"); return; }
            string method = rq[0], target = rq[1];
            var h = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (string l = ReadLine(ns); !string.IsNullOrEmpty(l); l = ReadLine(ns))
            {
                int k = l.IndexOf(':');
                if (k > 0) h[l.Substring(0, k).Trim()] = l.Substring(k + 1).Trim();
                if (h.Count > 100) return;
            }
            string host; h.TryGetValue("Host", out host);
            if (host != "127.0.0.1:" + Port && host != "localhost:" + Port) { Reply(ns, 421, "text/plain", "hote"); return; }
            int q = target.IndexOf('?'); if (q >= 0) target = target.Substring(0, q);
            string prefix = "/" + Token + "/";
            if (!target.StartsWith(prefix, StringComparison.Ordinal)) { Reply(ns, 404, "text/plain", "introuvable"); return; }
            string rest;
            try { rest = Uri.UnescapeDataString(target.Substring(prefix.Length)); } catch { Reply(ns, 400, "text/plain", "chemin"); return; }

            if (rest == "api")
            {
                string origin; h.TryGetValue("Origin", out origin);
                if (method != "POST" || origin != null && origin != "http://127.0.0.1:" + Port && origin != "http://localhost:" + Port) { Reply(ns, 403, "text/plain", "refuse"); return; }
                string len; long n;
                if (!h.TryGetValue("Content-Length", out len) || !long.TryParse(len, out n) || n < 0 || n > 1L << 30) { Reply(ns, 411, "text/plain", "longueur"); return; }
                byte[] body = new byte[n];
                ns.ReadTimeout = 120000;
                for (int got = 0; got < n;) { int r = ns.Read(body, got, (int)n - got); if (r <= 0) return; got += r; }
                Reply(ns, 204, null, null);
                onMessage(Encoding.UTF8.GetString(body));
                return;
            }
            if (method != "GET" && method != "HEAD") { Reply(ns, 405, "text/plain", "methode"); return; }
            if (rest == "evenements") { Events(ns); return; }

            string root = ui;
            if (rest.StartsWith("carte/", StringComparison.Ordinal)) { root = carte; rest = rest.Substring(6); }
            else if (rest.StartsWith("projets/", StringComparison.Ordinal)) { root = projets; rest = rest.Substring(8); }
            if (rest.Length == 0) rest = "index.html";
            string path;
            try { path = Path.GetFullPath(Path.Combine(root, rest.Replace('/', '\\'))); } catch { Reply(ns, 400, "text/plain", "chemin"); return; }
            if (!path.StartsWith(root + "\\", StringComparison.OrdinalIgnoreCase) || !File.Exists(path)) { Reply(ns, 404, "text/plain", "introuvable"); return; }
            using (var f = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 16))
            {
                Head(ns, 200, Type(path), f.Length, "Cache-Control: no-store\r\n");
                if (method == "GET") f.CopyTo(ns, 1 << 20);
            }
        }

        // flux d'evenements : la page y recoit les messages ; une ligne de commentaire toutes les 15 s detecte un onglet ferme
        void Events(NetworkStream ns)
        {
            byte[] head = Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Type: text/event-stream; charset=utf-8\r\nCache-Control: no-store\r\nConnection: keep-alive\r\n\r\n: ok\n\n");
            ns.Write(head, 0, head.Length);
            lock (gate)
            {
                streams.Add(ns);
                if (pending.Count > 0) { foreach (string p in pending) Write(Encoding.UTF8.GetBytes("data: " + p + "\n\n")); pending.Clear(); }
            }
            byte[] ping = Encoding.ASCII.GetBytes(": ping\n\n");
            while (true)
            {
                Thread.Sleep(15000);
                lock (gate)
                {
                    if (!streams.Contains(ns)) return;
                    try { ns.Write(ping, 0, ping.Length); ns.Flush(); } catch { streams.Remove(ns); return; }
                }
            }
        }

        static void Head(Stream s, int code, string type, long length, string extra)
        {
            var sb = new StringBuilder();
            sb.Append("HTTP/1.1 ").Append(code).Append(' ').Append(code == 200 ? "OK" : code == 204 ? "No Content" : "Erreur").Append("\r\n");
            if (type != null) sb.Append("Content-Type: ").Append(type).Append("\r\n");
            sb.Append("Content-Length: ").Append(length).Append("\r\n");
            sb.Append("X-Content-Type-Options: nosniff\r\nConnection: close\r\n").Append(extra ?? "").Append("\r\n");
            byte[] b = Encoding.ASCII.GetBytes(sb.ToString());
            s.Write(b, 0, b.Length);
        }
        static void Reply(Stream s, int code, string type, string text)
        {
            byte[] b = text == null ? new byte[0] : Encoding.UTF8.GetBytes(text);
            Head(s, code, type, b.Length, null);
            if (b.Length > 0) s.Write(b, 0, b.Length);
        }

        static string ReadLine(Stream s)
        {
            var b = new List<byte>(128);
            while (true)
            {
                int x = s.ReadByte();
                if (x < 0) return b.Count > 0 ? Encoding.ASCII.GetString(b.ToArray()) : null;
                if (x == '\n') break;
                if (x != '\r') b.Add((byte)x);
                if (b.Count > 16384) return null;
            }
            return Encoding.ASCII.GetString(b.ToArray());
        }

        static string Type(string p)
        {
            switch (Path.GetExtension(p).ToLowerInvariant())
            {
                case ".html": return "text/html; charset=utf-8";
                case ".js": case ".mjs": return "text/javascript; charset=utf-8";
                case ".css": return "text/css; charset=utf-8";
                case ".json": return "application/json; charset=utf-8";
                case ".txt": return "text/plain; charset=utf-8";
                case ".png": return "image/png";
                case ".jpg": case ".jpeg": return "image/jpeg";
                case ".webp": return "image/webp";
                case ".bmp": return "image/bmp";
                case ".svg": return "image/svg+xml";
                case ".ttf": return "font/ttf";
                case ".woff2": return "font/woff2";
                case ".glb": return "model/gltf-binary";
                case ".gltf": return "model/gltf+json";
                default: return "application/octet-stream";
            }
        }
    }
}
