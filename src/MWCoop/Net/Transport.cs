using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;

namespace MWCoop.Net
{
    // Sans fil d'execution (sonde a chaque image depuis Core), sur un lien de datagrammes : UDP (adresse IP) ou
    // Steam (pair-a-pair par les relais de Valve, SteamLink). Deux canaux par pair :
    //  - non fiable : etats frequents (positions), le plus recent gagne ;
    //  - fiable et ordonne : evenements, numerotes, acquittes, renvoyes toutes les 200 ms.
    // En-tete : 'M' 'W' type. Un message applicatif tient dans un paquet (<= MaxPayload) ;
    // les gros transferts (sauvegarde) sont decoupes par l'appelant.
    public class Peer
    {
        public int Id;
        public object End;            // adresse sur le lien (IPEndPoint, ou identifiant Steam)
        internal string Key;          // cle de l'adresse (table des pairs)
        public bool Accepted;
        public float LastHeard, LastPingSent, Rtt = 0.1f;
        internal ushort sendSeq, recvSeq;
        internal readonly Dictionary<ushort, Pending> unacked = new Dictionary<ushort, Pending>();
        internal readonly Queue<byte[]> waiting = new Queue<byte[]>();
        internal readonly Dictionary<ushort, byte[]> early = new Dictionary<ushort, byte[]>();
        public int InFlight { get { return unacked.Count + waiting.Count; } }
        internal class Pending { public byte[] Packet; public float SentAt; public int Tries; }
        public override string ToString() { return "#" + Id + " " + End; }   // (SteamId.ToString : l'identifiant)
    }

    public class Transport
    {
        public const int MaxPayload = 1150;
        const byte T_UNREL = 1, T_REL = 2, T_ACK = 3, T_PING = 4, T_PONG = 5, T_CONNECT = 6, T_ACCEPT = 7, T_REJECT = 8, T_BYE = 9;
        const int Window = 192;
        public const float Timeout = 15f;

        ILink link;
        readonly byte[] buf = new byte[2048];
        readonly Dictionary<string, Peer> byEnd = new Dictionary<string, Peer>();
        public readonly List<Peer> Peers = new List<Peer>();
        public bool IsHost;
        public int NetVersion;
        public int LocalId;   // invite : numero donne par l'hote (l'hote est 0)
        int nextId = 1;
        public float Now;
        public long BytesIn, BytesOut;

        public event Action<Peer> OnConnected;               // hote : nouveau pair ; invite : accepte par l'hote
        public event Action<Peer, string> OnDisconnected;
        public event Action<Peer, byte[], int, int> OnMessage; // pair, donnees, debut, longueur
        public event Action<string> OnRejected;

        public void Host(int port) { Host(new UdpLink(port)); }

        public void Host(ILink l)
        {
            IsHost = true;
            link = l;
        }

        public Peer Connect(string address, int port)
        {
            IPAddress ip;
            if (!IPAddress.TryParse(address, out ip))
            {
                ip = null;
                foreach (IPAddress a in Dns.GetHostAddresses(address))
                    if (a.AddressFamily == AddressFamily.InterNetwork) { ip = a; break; }
                if (ip == null) throw new Exception("adresse introuvable : " + address);
            }
            return Connect(new UdpLink(0), new IPEndPoint(ip, port));
        }

        public Peer Connect(ILink l, object hostAddr)
        {
            IsHost = false;
            link = l;
            Peer p = AddPeer(hostAddr);
            p.Id = 0;   // l'hote est toujours le joueur 0
            SendRaw(p, Build(T_CONNECT, BitConverter.GetBytes(NetVersion)));
            return p;
        }

        public void Close()
        {
            if (link == null) return;
            foreach (Peer p in Peers) SendRaw(p, Build(T_BYE, null));
            link.Close();
            link = null;
            Peers.Clear();
            byEnd.Clear();
        }

        public bool IsOpen { get { return link != null; } }
        public ILink Link { get { return link; } }

        Peer AddPeer(object ep)
        {
            var p = new Peer { End = ep, Key = link.KeyOf(ep), LastHeard = Now };
            byEnd[p.Key] = p;
            Peers.Add(p);
            return p;
        }

        void DropPeer(Peer p, string reason)
        {
            byEnd.Remove(p.Key);
            if (link != null) link.Forget(p.End);
            Peers.Remove(p);
            if (p.Accepted && OnDisconnected != null) OnDisconnected(p, reason);
            else if (!IsHost && OnRejected != null) OnRejected(reason);
        }

        public void Kick(Peer p, string reason)
        {
            SendRaw(p, Build(T_BYE, System.Text.Encoding.UTF8.GetBytes(reason)));
            DropPeer(p, reason);
        }

        static byte[] Build(byte type, byte[] body)
        {
            int n = body != null ? body.Length : 0;
            var b = new byte[3 + n];
            b[0] = (byte)'M'; b[1] = (byte)'W'; b[2] = type;
            if (n > 0) Buffer.BlockCopy(body, 0, b, 3, n);
            return b;
        }

        void SendRaw(Peer p, byte[] packet)
        {
            if (link == null) return;
            if (link.Send(p.End, packet, packet.Length)) BytesOut += packet.Length;
        }

        public void SendUnreliable(Peer p, byte[] msg)
        {
            if (p.Accepted) SendRaw(p, Build(T_UNREL, msg));
        }

        public void SendReliable(Peer p, byte[] msg)
        {
            if (msg.Length > MaxPayload) throw new ArgumentException("message fiable trop long : " + msg.Length);
            if (p.unacked.Count >= Window) { p.waiting.Enqueue(msg); return; }
            SendReliableNow(p, msg);
        }

        void SendReliableNow(Peer p, byte[] msg)
        {
            ushort seq = p.sendSeq++;
            var packet = new byte[5 + msg.Length];
            packet[0] = (byte)'M'; packet[1] = (byte)'W'; packet[2] = T_REL;
            packet[3] = (byte)seq; packet[4] = (byte)(seq >> 8);
            Buffer.BlockCopy(msg, 0, packet, 5, msg.Length);
            p.unacked[seq] = new Peer.Pending { Packet = packet, SentAt = Now };
            SendRaw(p, packet);
        }

        public void Update(float now)
        {
            Now = now;
            if (link == null) return;
            Receive();
            foreach (Peer p in Peers.ToArray())
            {
                if (Now - p.LastHeard > Timeout) { DropPeer(p, "delai depasse"); continue; }
                if (Now - p.LastPingSent > 1f)
                {
                    p.LastPingSent = Now;
                    SendRaw(p, Build(p.Accepted || IsHost ? T_PING : T_CONNECT,
                        p.Accepted || IsHost ? BitConverter.GetBytes(Now) : BitConverter.GetBytes(NetVersion)));
                }
                float resend = Math.Max(0.2f, p.Rtt * 1.5f);
                foreach (Peer.Pending pd in p.unacked.Values)
                    if (Now - pd.SentAt > resend) { pd.SentAt = Now; pd.Tries++; SendRaw(p, pd.Packet); }
                while (p.waiting.Count > 0 && p.unacked.Count < Window) SendReliableNow(p, p.waiting.Dequeue());
            }
        }

        void Receive()
        {
            object ep;
            int n;
            for (int guard = 0; link != null && guard < 4096 && link.Receive(buf, out n, out ep); guard++)
            {
                BytesIn += n;
                if (n < 3 || buf[0] != 'M' || buf[1] != 'W') continue;
                Peer p;
                byEnd.TryGetValue(link.KeyOf(ep), out p);
                Handle(p, ep, buf[2], n);
            }
        }

        void Handle(Peer p, object ep, byte type, int n)
        {
            if (type == T_CONNECT)
            {
                if (!IsHost) return;
                int ver = n >= 7 ? BitConverter.ToInt32(buf, 3) : -1;
                if (ver != NetVersion)
                {
                    var tmp = new Peer { End = ep };
                    SendRaw(tmp, Build(T_REJECT, System.Text.Encoding.UTF8.GetBytes(
                        "version reseau differente (hote " + NetVersion + ", invite " + ver + ")")));
                    return;
                }
                if (p == null) { p = AddPeer(ep); p.Id = nextId++; }
                p.LastHeard = Now;
                SendRaw(p, Build(T_ACCEPT, BitConverter.GetBytes(p.Id)));
                if (!p.Accepted) { p.Accepted = true; if (OnConnected != null) OnConnected(p); }
                return;
            }
            if (p == null) return;
            p.LastHeard = Now;
            switch (type)
            {
                case T_ACCEPT:
                    if (p.Accepted) return;
                    p.Accepted = true;
                    LocalId = n >= 7 ? BitConverter.ToInt32(buf, 3) : -1;
                    if (OnConnected != null) OnConnected(p);
                    break;
                case T_REJECT:
                case T_BYE:
                    DropPeer(p, n > 3 ? System.Text.Encoding.UTF8.GetString(buf, 3, n - 3) : "deconnecte");
                    break;
                case T_PING:
                    var pong = new byte[n]; Buffer.BlockCopy(buf, 0, pong, 0, n); pong[2] = T_PONG;
                    SendRaw(p, pong);
                    break;
                case T_PONG:
                    if (n >= 7) p.Rtt = p.Rtt * 0.8f + (Now - BitConverter.ToSingle(buf, 3)) * 0.2f;
                    break;
                case T_ACK:
                    for (int i = 3; i + 1 < n; i += 2) p.unacked.Remove((ushort)(buf[i] | buf[i + 1] << 8));
                    break;
                case T_UNREL:
                    if (p.Accepted && OnMessage != null) OnMessage(p, buf, 3, n - 3);
                    break;
                case T_REL:
                    if (n < 5 || !p.Accepted) return;
                    ushort seq = (ushort)(buf[3] | buf[4] << 8);
                    SendRaw(p, Build(T_ACK, new[] { buf[3], buf[4] }));
                    short ahead = (short)(seq - p.recvSeq);
                    if (ahead < 0) return;                       // deja recu
                    var copy = new byte[n - 5];
                    Buffer.BlockCopy(buf, 5, copy, 0, copy.Length);
                    if (ahead > 0) { p.early[seq] = copy; return; }
                    Deliver(p, copy);
                    byte[] next;
                    while (p.early.TryGetValue(p.recvSeq, out next)) { p.early.Remove(p.recvSeq); Deliver(p, next); }
                    break;
            }
        }

        void Deliver(Peer p, byte[] msg)
        {
            p.recvSeq++;
            if (OnMessage != null) OnMessage(p, msg, 0, msg.Length);
        }
    }
}

namespace MWCoop.Net
{
    // Lien de datagrammes sous le transport : envoi a une adresse, reception sans attente.
    public interface ILink
    {
        bool Send(object to, byte[] data, int len);
        bool Receive(byte[] buf, out int len, out object from);   // faux : plus rien a lire
        string KeyOf(object addr);
        void Forget(object addr);                                  // pair retire (Steam : session fermee)
        void Close();
        string Describe(object addr);
    }

    public class UdpLink : ILink
    {
        Socket sock;

        public UdpLink(int port)
        {
            sock = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            sock.Blocking = false;
            // Sans cela, un ICMP « port injoignable » ferme la reception sous Windows (WSAECONNRESET).
            try { sock.IOControl(-1744830452, new byte[] { 0 }, null); } catch { }
            sock.Bind(new IPEndPoint(IPAddress.Any, port));
        }

        public bool Send(object to, byte[] data, int len)
        {
            if (sock == null) return false;
            try { sock.SendTo(data, len, SocketFlags.None, (EndPoint)to); return true; } catch (SocketException) { return false; }
        }

        public bool Receive(byte[] buf, out int len, out object from)
        {
            len = 0; from = null;
            while (sock != null && sock.Available > 0)
            {
                EndPoint ep = new IPEndPoint(IPAddress.Any, 0);
                try { len = sock.ReceiveFrom(buf, ref ep); }
                catch (SocketException) { continue; }
                from = ep;
                return true;
            }
            return false;
        }

        public string KeyOf(object addr) { return addr != null ? addr.ToString() : ""; }
        public void Forget(object addr) { }
        public string Describe(object addr) { return KeyOf(addr); }
        public void Close() { if (sock != null) { sock.Close(); sock = null; } }
    }
}
