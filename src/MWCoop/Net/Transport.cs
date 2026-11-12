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
        public float GraceUntil;      // silence tolere jusque-la (pair en plein chargement, hote qui sauvegarde) : pas de "delai depasse" avant GraceUntil + Timeout
        internal uint Token;          // jeton de session de l'invite (T_CONNECT) : 0 pour une version sans jeton
        internal uint Instance;       // identifiant de son processus (T_CONNECT) : 0 pour une version sans
        internal ushort sendSeq, recvSeq;
        internal ushort sendBase;     // plus vieux numero fiable peut-etre pas encore acquitte (sendSeq s'il n'y en a aucun)
        internal readonly Dictionary<ushort, Pending> unacked = new Dictionary<ushort, Pending>();
        internal readonly Queue<byte[]> waiting = new Queue<byte[]>();
        internal readonly Dictionary<ushort, byte[]> early = new Dictionary<ushort, byte[]>();
        internal readonly List<ushort> acks = new List<ushort>();   // acquittements a envoyer (un paquet par image)
        internal readonly byte[] batch = new byte[3 + Transport.MaxBatch];   // messages non fiables de l'image (T_BATCH)
        internal int batchLen, batchCount;
        public int InFlight { get { return unacked.Count + waiting.Count; } }
        internal class Pending { public byte[] Packet; public float SentAt; public int Tries; }
        public override string ToString() { return "#" + Id + " " + End; }   // (SteamId.ToString : l'identifiant)
    }

    public class Transport
    {
        public const int MaxPayload = 1150;
        const byte T_UNREL = 1, T_REL = 2, T_ACK = 3, T_PING = 4, T_PONG = 5, T_CONNECT = 6, T_ACCEPT = 7, T_REJECT = 8, T_BYE = 9, T_BATCH = 10;
        // Messages non fiables d'une image mis ensemble (T_BATCH : longueur U16 + message, a la suite), un datagramme par pair
        // au lieu d'un par message (refonte du 14/10 : en-tetes UDP / Steam par paquet). Envoyes a Flush (fin d'image, Core).
        internal const int MaxBatch = 1180;
        const int Window = 192;
        // Ecart maximal entre le plus vieux message fiable non acquitte et le prochain numero. Window compte les messages en
        // attente, pas l'ecart des numeros : un vieux message perdu et renvoye en vain pendant que les suivants etaient acquittes
        // laissait l'expediteur prendre plus de 32 768 numeros d'avance (cinema, 4,75 Go a 3,3 Mo/s, essai de [Addons] 07/11).
        // Les numeros etant sur 16 bits, le recepteur prenait alors les neufs pour deja recus : acquittes puis jetes (65 364
        // messages en 8 plages), et au-dela de 65 536 un nouveau message ecrasait l'ancien dans unacked. Au-dela de MaxSpan, les
        // nouveaux attendent dans waiting. Cote expediteur seulement : le recepteur ne change pas.
        const int MaxSpan = 16384;
        public const float Timeout = 15f;

        ILink link;
        readonly byte[] buf = new byte[2048];
        readonly Dictionary<string, Peer> byEnd = new Dictionary<string, Peer>();
        public readonly List<Peer> Peers = new List<Peer>();
        public bool IsHost;
        public int NetVersion;
        public int LocalId;   // invite : numero donne par l'hote (l'hote est 0)
        int nextId = 1;
        // Numeros recycles (audit MSC 02/11 : nextId++ sans fin ; chaque reconnexion -- coupure, pair remplace, invite d'une
        // autre version exclu puis revenu toutes les 3 s sur un port neuf -- prenait un numero, et le 256e, ecrit sur un octet
        // (Roster, U8(from.Id) des modules), valait 0 : les autres le prenaient pour l'hote). Tourniquet 1..255 a partir de
        // nextId, sans un numero porte par un pair present ; aucun libre : refuse. [Test] NumerosRecycles=0 : comme avant.
        public bool RecycleIds = true;
        // T_ACCEPT perime (dossier #67, 12/11) : en Steam, l'invite qui n'a pas de reponse en 15 s (hote pas encore pret) recree son transport
        // (nouveau jeton) et redemande ; l'hote avait accepte la 1re demande (#1) puis remplace ce pair par la 2e (#2). Le T_ACCEPT de la
        // 1re, livre en retard par Steam, arrivait avant celui de la 2e : l'invite se croyait #1, l'hote le tenait pour #2 -- puis rien ne
        // se faisait (invite reste au menu, sauvegarde recue). L'hote ajoute desormais le jeton de la demande au T_ACCEPT ; l'invite ignore
        // un T_ACCEPT d'un autre jeton. Un hote d'avant (sans jeton) : comme avant. AcceptToken=false ([Test] AccepteJeton=0) : comme avant.
        public bool AcceptToken = true;
        public event Action<int> OnStaleAccept;   // invite : numero d'un T_ACCEPT d'un autre jeton, ignore
        public void FirstId(int id) { if (id > 0) nextId = id; }   // ([Test] PremierNumero : essai du passage de 255 a 1)

        int FreeId()
        {
            if (!RecycleIds) return nextId++;
            for (int k = 0; k < 255; k++)
            {
                int id = (Math.Max(nextId, 1) - 1 + k) % 255 + 1;
                bool used = false;
                foreach (Peer q in Peers) if (q.Id == id) { used = true; break; }
                if (used) continue;
                nextId = id % 255 + 1;   // (le suivant : un numero qui vient de se liberer n'est pas redonne tout de suite)
                return id;
            }
            return -1;
        }

        // Essai hors jeu (sans lien) : 'cycles' arrivees et departs au hasard, au plus 'active' pairs a la fois, a partir du
        // numero 'first'. Rend « numeros OK ... » ou la premiere faute (numero hors 1..255, doublon parmi les presents).
        public static string TestNumeros(int cycles, int active, int first)
        {
            var t = new Transport { IsHost = true };
            t.FirstId(first);
            var r = new Random(1234);
            int max = 0, refused = 0;
            for (int i = 0; i < cycles; i++)
            {
                if (t.Peers.Count >= active || (t.Peers.Count > 0 && r.Next(3) == 0)) t.Peers.RemoveAt(r.Next(t.Peers.Count));
                int id = t.FreeId();
                if (id < 0) { refused++; continue; }
                if (id < 1 || id > 255) return "numeros FAUX : " + id + " au cycle " + i;
                foreach (Peer q in t.Peers) if (q.Id == id) return "numeros FAUX : doublon " + id + " au cycle " + i;
                t.Peers.Add(new Peer { Id = id });
                if (id > max) max = id;
            }
            return "numeros OK : " + cycles + " arrivees, plus grand " + max + ", refus " + refused + ", presents " + t.Peers.Count;
        }

        public float Now;
        public long BytesIn, BytesOut;
        public long RelSent, RelResent;   // messages fiables envoyes, et renvois (perdus, ou leur accuse perdu) : journal de Core
        // Jeton de session (relecture du 15/10, #2) : un invite qui recree son transport (hote fige > 15 s, Steam) garde
        // la meme adresse, donc la meme cle ; sans jeton l'hote le prenait pour le pair existant (deja Accepted, numeros
        // gardes) et jetait son Hello (seq 0) comme « deja recu » -- invite connecte mais muet pour toujours. Tire au
        // hasard a la creation du transport (jamais 0 : 0 = version sans jeton, ancien comportement).
        static readonly Random rng = new Random();
        readonly uint token = (uint)rng.Next(1, int.MaxValue);
        // Identifiant d'instance (essais chaos, 27/10) : fixe pour tout le processus. En IP, un invite qui se reconnecte
        // (hote muet > 15 s : chargement du niveau, coupure) repart d'un port ephemere neuf, donc d'une autre adresse :
        // pour l'hote c'etait un 2e pair, et l'ancien restait 15 s (avatar fantome, sauvegarde envoyee deux fois, ses PNJ
        // a lui). Meme instance a une autre adresse : l'ancien pair est remplace tout de suite.
        static readonly uint instance = (uint)rng.Next(1, int.MaxValue);
        // Pairs exclus ou refuses (relecture du 15/10, 3.1) : adresse -> fin de l'exclusion et motif ; un T_CONNECT pendant
        // ce temps recoit un T_REJECT au lieu d'etre accepte puis re-exclu en boucle (BYE perdu, invite qui reessaie a 3 s).
        class Ban { public float Until; public string Reason; }
        readonly Dictionary<string, Ban> banned = new Dictionary<string, Ban>();
        const float BanTime = 10f;
        // Adieux en cours : le BYE d'une exclusion part 3 fois (0, +0,3 s, +0,6 s : non fiable, un seul se perdait) et le
        // lien n'oublie l'adresse (Steam : session fermee) qu'une seconde apres, le temps que le dernier BYE parte.
        class Farewell { public object End; public byte[] Packet; public float NextAt, ForgetAt; public int Left; }
        readonly List<Farewell> farewells = new List<Farewell>();

        public event Action<Peer> OnConnected;               // hote : nouveau pair ; invite : accepte par l'hote
        public event Action<Peer, string> OnDisconnected;
        public event Action<Peer, byte[], int, int> OnMessage; // pair, donnees, debut, longueur
        public event Action<string> OnRejected;
        // Message en cours de remise (OnMessage) : venu par le canal fiable ? (relais de l'hote : meme canal, Jobs.OnSpecial)
        public bool InReliable;

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
            SendRaw(p, ConnectPacket());
            return p;
        }

        // T_CONNECT : version reseau (I32), jeton de session (U32), instance (U32) ; un hote d'avant ne lit que ce qu'il connait.
        byte[] ConnectPacket()
        {
            var body = new byte[12];
            Buffer.BlockCopy(BitConverter.GetBytes(NetVersion), 0, body, 0, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(token), 0, body, 4, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(instance), 0, body, 8, 4);
            return Build(T_CONNECT, body);
        }

        // Essais (#67, [Test] AccepteFantome=N) : invite -- un T_ACCEPT perime (numero N, autre jeton) recu juste apres sa demande, comme celui
        // que Steam livrait en retard.
        public void TestStaleAccept(int id)
        {
            Peer p = Peers.Count > 0 ? Peers[0] : null;
            if (p == null) return;
            var body = new byte[8];
            Buffer.BlockCopy(BitConverter.GetBytes(id), 0, body, 0, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(token + 1), 0, body, 4, 4);
            byte[] pkt = Build(T_ACCEPT, body);
            Buffer.BlockCopy(pkt, 0, buf, 0, pkt.Length);
            Handle(p, p.End, T_ACCEPT, pkt.Length);
        }

        public void Close()
        {
            if (link == null) return;
            foreach (Peer p in Peers) SendRaw(p, Build(T_BYE, null));
            link.Close();
            link = null;
            Peers.Clear();
            byEnd.Clear();
            farewells.Clear();
            banned.Clear();
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

        void DropPeer(Peer p, string reason) { DropPeer(p, reason, true); }

        // forget faux : l'adresse reste connue du lien (pair remplace : le nouveau pair la reprend tout de suite ;
        // exclusion : oubli differe par Farewell, apres les derniers BYE).
        void DropPeer(Peer p, string reason, bool forget)
        {
            byEnd.Remove(p.Key);
            if (forget && link != null) link.Forget(p.End);
            Peers.Remove(p);
            if (p.Accepted && OnDisconnected != null) OnDisconnected(p, reason);
            else if (!IsHost && OnRejected != null) OnRejected(reason);
        }

        // Silence tolere pendant 'seconds' (Menor, 18/10 : ses invites, en plein chargement de la sauvegarde recue, etaient
        // "partis (delai depasse)" 20 s apres leur arrivee, se reconnectaient, et l'hote resauvegardait 12 s pour chacun).
        public void Grace(Peer p, float seconds) { if (p != null) p.GraceUntil = Math.Max(p.GraceUntil, Now + seconds); }

        public void Kick(Peer p, string reason)
        {
            byte[] bye = Build(T_BYE, System.Text.Encoding.UTF8.GetBytes(reason));
            SendRaw(p, bye);
            farewells.Add(new Farewell { End = p.End, Packet = bye, NextAt = Now + 0.3f, ForgetAt = Now + 1f, Left = 2 });
            if (IsHost) banned[p.Key] = new Ban { Until = Now + BanTime, Reason = reason };
            DropPeer(p, reason, false);
        }

        // Adieux : BYE renvoyes, puis adresse oubliee par le lien (Update).
        void UpdateFarewells()
        {
            for (int i = farewells.Count - 1; i >= 0; i--)
            {
                Farewell f = farewells[i];
                if (f.Left > 0 && Now >= f.NextAt) { f.Left--; f.NextAt = Now + 0.3f; SendRawTo(f.End, f.Packet); }
                if (f.Left == 0 && Now >= f.ForgetAt)
                {
                    farewells.RemoveAt(i);
                    // (pas si l'adresse a deja un nouveau pair : sa session serait fermee sous lui)
                    if (link != null && !byEnd.ContainsKey(link.KeyOf(f.End))) link.Forget(f.End);
                }
            }
        }

        static byte[] Build(byte type, byte[] body)
        {
            int n = body != null ? body.Length : 0;
            var b = new byte[3 + n];
            b[0] = (byte)'M'; b[1] = (byte)'W'; b[2] = type;
            if (n > 0) Buffer.BlockCopy(body, 0, b, 3, n);
            return b;
        }

        void SendRaw(Peer p, byte[] packet) { SendRawTo(p.End, packet); }

        void SendRawTo(object end, byte[] packet)
        {
            if (link == null) return;
            if (link.Send(end, packet, packet.Length)) BytesOut += packet.Length;
        }

        public void SendUnreliable(Peer p, byte[] msg)
        {
            if (!p.Accepted) return;
            if (msg.Length + 2 > MaxBatch) { SendRaw(p, Build(T_UNREL, msg)); return; }   // (trop gros : seul)
            if (p.batchLen + 2 + msg.Length > MaxBatch) FlushBatch(p);
            int at = 3 + p.batchLen;
            p.batch[at] = (byte)msg.Length; p.batch[at + 1] = (byte)(msg.Length >> 8);
            Buffer.BlockCopy(msg, 0, p.batch, at + 2, msg.Length);
            p.batchLen += 2 + msg.Length; p.batchCount++;
        }

        void FlushBatch(Peer p)
        {
            if (p.batchCount == 0) return;
            if (p.batchCount == 1)   // (un seul : T_UNREL, comme avant)
            {
                var one = new byte[p.batchLen - 2 + 3];
                one[0] = (byte)'M'; one[1] = (byte)'W'; one[2] = T_UNREL;
                Buffer.BlockCopy(p.batch, 5, one, 3, p.batchLen - 2);
                SendRaw(p, one);
            }
            else
            {
                var pk = new byte[3 + p.batchLen];
                Buffer.BlockCopy(p.batch, 3, pk, 3, p.batchLen);
                pk[0] = (byte)'M'; pk[1] = (byte)'W'; pk[2] = T_BATCH;
                SendRaw(p, pk);
            }
            p.batchLen = 0; p.batchCount = 0;
        }

        // Fin d'image : messages non fiables groupes et acquittements partent (Core.LateUpdate, et au debut de Update).
        public void Flush()
        {
            if (link == null) return;
            foreach (Peer p in Peers) { FlushBatch(p); FlushAcks(p); }
        }

        public void SendReliable(Peer p, byte[] msg)
        {
            if (msg.Length > MaxPayload) throw new ArgumentException("message fiable trop long : " + msg.Length);
            // (file deja non vide : derriere elle, sinon un message passerait devant les plus anciens)
            if (p.waiting.Count > 0 || p.unacked.Count >= Window || (ushort)(p.sendSeq - p.sendBase) >= MaxSpan) { p.waiting.Enqueue(msg); return; }
            // Le lot non fiable de l'image part d'abord (relecture du 15/10, 3.1) : un fiable partait tout de suite et les
            // non fiables de la meme image au Flush -- la « finale » (Knobs, @drone) etait depassee par le flux d'avant.
            FlushBatch(p);
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
            RelSent++;
            SendRaw(p, packet);
        }

        public void Update(float now)
        {
            Now = now;
            if (link == null) return;
            Flush();   // (envoyes apres la fin de l'image precedente : physique, LateUpdate)
            Receive();
            UpdateFarewells();
            foreach (Peer p in Peers.ToArray())
            {
                FlushAcks(p);
                if (Now - Math.Max(p.LastHeard, p.GraceUntil) > Timeout) { DropPeer(p, "delai depasse"); continue; }
                if (Now - p.LastPingSent > 1f)
                {
                    p.LastPingSent = Now;
                    if (p.Accepted || IsHost) SendRaw(p, Build(T_PING, BitConverter.GetBytes(Now)));
                    else SendRaw(p, ConnectPacket());
                }
                float resend = Math.Max(0.2f, p.Rtt * 1.5f);
                foreach (Peer.Pending pd in p.unacked.Values)
                    if (Now - pd.SentAt > resend) { pd.SentAt = Now; pd.Tries++; RelResent++; SendRaw(p, pd.Packet); }
                while (p.waiting.Count > 0 && p.unacked.Count < Window && (ushort)(p.sendSeq - p.sendBase) < MaxSpan) SendReliableNow(p, p.waiting.Dequeue());
            }
        }

        // Acquittements groupes : un paquet par pair et par image au lieu d'un par message fiable recu (T_ACK porte une
        // liste de numeros ; les versions d'avant n'en envoyaient qu'un, et lisaient deja la liste).
        void FlushAcks(Peer p)
        {
            for (int i = 0; i < p.acks.Count; i += 500)
            {
                int k = Math.Min(500, p.acks.Count - i);
                var b = new byte[3 + 2 * k];
                b[0] = (byte)'M'; b[1] = (byte)'W'; b[2] = T_ACK;
                for (int j = 0; j < k; j++) { ushort s = p.acks[i + j]; b[3 + 2 * j] = (byte)s; b[4 + 2 * j] = (byte)(s >> 8); }
                SendRaw(p, b);
            }
            p.acks.Clear();
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
                    SendRawTo(ep, Build(T_REJECT, System.Text.Encoding.UTF8.GetBytes(
                        "version reseau differente (hote " + NetVersion + ", invite " + ver + ")")));
                    return;
                }
                // Exclu il y a moins de 10 s : refuse (le BYE a pu se perdre, l'invite reessaie), sans l'accepter.
                Ban ban;
                string key = link.KeyOf(ep);
                if (banned.TryGetValue(key, out ban))
                {
                    if (Now < ban.Until) { SendRawTo(ep, Build(T_REJECT, System.Text.Encoding.UTF8.GetBytes(ban.Reason))); return; }
                    banned.Remove(key);
                }
                // Meme adresse, autre jeton : l'invite a recree son transport (numeros a zero) -- l'ancien pair est
                // remplace par un neuf, pour que son Hello (seq 0) soit pris (relecture du 15/10, #2).
                uint tok = n >= 11 ? BitConverter.ToUInt32(buf, 7) : 0, inst = n >= 15 ? BitConverter.ToUInt32(buf, 11) : 0;
                if (p != null && tok != p.Token) { DropPeer(p, "pair remplace", false); p = null; }
                // Meme processus a une autre adresse (reconnexion en IP, port ephemere neuf) : l'ancien pair part tout de suite.
                if (inst != 0) foreach (Peer q in Peers.ToArray()) if (q != p && q.Instance == inst) DropPeer(q, "pair remplace (nouvelle adresse)");
                if (p == null)
                {
                    int id = FreeId();
                    if (id < 0) { SendRawTo(ep, Build(T_REJECT, System.Text.Encoding.UTF8.GetBytes("partie pleine"))); return; }
                    p = AddPeer(ep); p.Id = id; p.Token = tok; p.Instance = inst;
                }
                p.LastHeard = Now;
                var acc = new byte[8];
                Buffer.BlockCopy(BitConverter.GetBytes(p.Id), 0, acc, 0, 4);
                Buffer.BlockCopy(BitConverter.GetBytes(p.Token), 0, acc, 4, 4);
                SendRaw(p, Build(T_ACCEPT, acc));
                if (!p.Accepted) { p.Accepted = true; if (OnConnected != null) OnConnected(p); }
                return;
            }
            if (p == null) return;
            p.LastHeard = Now;
            switch (type)
            {
                case T_ACCEPT:
                    if (p.Accepted) return;
                    if (AcceptToken && n >= 11 && BitConverter.ToUInt32(buf, 7) != token)
                    {
                        if (OnStaleAccept != null) OnStaleAccept(n >= 7 ? BitConverter.ToInt32(buf, 3) : -1);
                        return;
                    }
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
                    while (p.sendBase != p.sendSeq && !p.unacked.ContainsKey(p.sendBase)) p.sendBase++;
                    break;
                case T_UNREL:
                    InReliable = false;
                    if (p.Accepted && OnMessage != null) OnMessage(p, buf, 3, n - 3);
                    break;
                case T_BATCH:
                    if (!p.Accepted || OnMessage == null) break;
                    for (int i = 3; i + 2 <= n;)
                    {
                        int len = buf[i] | buf[i + 1] << 8;
                        i += 2;
                        if (len <= 0 || i + len > n) break;
                        InReliable = false;
                        OnMessage(p, buf, i, len);
                        i += len;
                    }
                    break;
                case T_REL:
                    if (n < 5 || !p.Accepted) return;
                    ushort seq = (ushort)(buf[3] | buf[4] << 8);
                    p.acks.Add(seq);
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
            InReliable = true;
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

        // Tampon de reception du socket, en Ko (0 : celui du systeme). Les datagrammes ne sont lus qu'une fois par image
        // (Transport.Receive) : un film envoye a 3,3 Mo/s arrive par rafales entre deux images et ce qui ne tenait pas dans le
        // tampon par defaut etait perdu, meme en local (~10 %, renvoye ensuite ; essai de [Addons] 07/11). Session le regle
        // ([Test] TamponUdpKo) ; RecvBuffer : la taille obtenue.
        public static int RecvBufferKo = 4096;
        public int RecvBuffer;

        public UdpLink(int port)
        {
            sock = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            sock.Blocking = false;
            // Sans cela, un ICMP « port injoignable » ferme la reception sous Windows (WSAECONNRESET).
            try { sock.IOControl(-1744830452, new byte[] { 0 }, null); } catch { }
            if (RecvBufferKo > 0) try { sock.ReceiveBufferSize = RecvBufferKo * 1024; } catch { }
            try { RecvBuffer = sock.ReceiveBufferSize; } catch { }
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
