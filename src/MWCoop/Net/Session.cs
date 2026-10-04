using System;
using System.Collections.Generic;
using UnityEngine;

namespace MWCoop.Net
{
    public class PlayerInfo
    {
        public int Id;
        public string Name = "?", Skin = "";
        public Peer Peer;              // hote : le pair de cet invite
        public bool Local;
        public int Level;              // 0 menu, 1 en partie
        public PlayerState State;      // dernier etat recu
        public float StateTime;
    }

    public struct PlayerState
    {
        public Vector3 Feet;           // bas du personnage (sol)
        public Vector3 Head;           // camera (yeux) : place l'avatar assis dans un vehicule
        public float Yaw, Pitch, Height, Speed;
        public int Flags;              // PlayerSync.F_* : accroupi, assis, fume, boit, porte, salue, dort
    }

    // Session coop : l'hote fait autorite et relaie tout. [Coop] Mode=solo|hote|invite.
    public static class Session
    {
        public const int NetVersion = 18;
        public static Transport T;
        public static bool Active, IsHost;
        public static int LocalId;
        public static string Status = "solo";
        public static readonly Dictionary<int, PlayerInfo> Players = new Dictionary<int, PlayerInfo>();
        public static PlayerInfo Me;
        static Peer hostPeer;
        static string address;
        static int port;
        static float retryAt;

        public static event Action<PlayerInfo> PlayerLeft;

        public static void Start()
        {
            string mode = Config.Get("Coop", "Mode", "solo").ToLowerInvariant();
            port = Config.GetInt("Coop", "Port", 7870);
            Me = new PlayerInfo { Local = true, Name = Config.Get("Coop", "Pseudo", Environment.UserName),
                                  Skin = Config.Get("Coop", "Apparence", "char_shirt21") };
            if (mode == "hote" || mode == "host") StartHost();
            else if (mode == "invite" || mode == "client") { address = Config.Get("Coop", "Adresse", "127.0.0.1"); StartClient(); }
        }

        static Transport NewTransport()
        {
            var t = new Transport { NetVersion = NetVersion };
            t.OnConnected += Connected;
            t.OnDisconnected += Disconnected;
            t.OnMessage += Message;
            t.OnRejected += r => { Status = "refuse : " + r; Log.Warn("connexion refusee : " + r); hostPeer = null; retryAt = Time.realtimeSinceStartup + 5f; };
            return t;
        }

        static void StartHost()
        {
            try
            {
                T = NewTransport();
                T.Host(port);
                Active = IsHost = true;
                LocalId = Me.Id = 0;
                Players[0] = Me;
                Status = "hote, port " + port;
                Log.Info("hote sur le port UDP " + port);
            }
            catch (Exception e) { Status = "erreur : " + e.Message; Log.Error("hote : " + e); }
        }

        static void StartClient()
        {
            try
            {
                if (T != null) T.Close();
                T = NewTransport();
                hostPeer = T.Connect(address, port);
                Active = true;
                IsHost = false;
                Status = "connexion a " + address + ":" + port + "...";
                Log.Info("connexion a " + address + ":" + port);
            }
            catch (Exception e) { Status = "erreur : " + e.Message; Log.Error("invite : " + e); retryAt = Time.realtimeSinceStartup + 5f; }
        }

        public static void Update()
        {
            if (!Active) return;
            float now = Time.realtimeSinceStartup;
            if (!IsHost && hostPeer == null && now >= retryAt) StartClient();
            if (T != null) T.Update(now);
        }

        public static void Stop()
        {
            if (T != null) T.Close();
            T = null;
            Active = false;
        }

        // ------------------------------------------------------------ envoi
        public static void SendToHost(NetWriter w, bool reliable)
        {
            if (hostPeer == null || !hostPeer.Accepted) return;
            if (reliable) T.SendReliable(hostPeer, w.ToArray()); else T.SendUnreliable(hostPeer, w.ToArray());
        }

        // Hote : a tous les invites (sauf 'except').
        public static void Broadcast(NetWriter w, bool reliable, int except = -1)
        {
            if (T == null) return;
            byte[] b = w.ToArray();
            foreach (Peer p in T.Peers)
            {
                if (!p.Accepted || p.Id == except) continue;
                if (reliable) T.SendReliable(p, b); else T.SendUnreliable(p, b);
            }
        }

        // Hote : diffuse ; invite : envoie a l'hote qui relaiera.
        public static void SendAll(NetWriter w, bool reliable)
        {
            if (IsHost) Broadcast(w, reliable); else SendToHost(w, reliable);
        }

        public static int RemoteCount { get { return Players.Count - (Players.ContainsKey(LocalId) ? 1 : 0); } }

        // ------------------------------------------------------------ evenements
        static void Connected(Peer p)
        {
            if (IsHost)
            {
                Log.Info("nouveau pair " + p);
                Players[p.Id] = new PlayerInfo { Id = p.Id, Peer = p, Name = "joueur " + p.Id };
                return;
            }
            LocalId = Me.Id = T.LocalId;
            Players.Clear();
            Players[LocalId] = Me;
            Status = "connecte a " + address;
            Log.Info("accepte par l'hote, numero " + LocalId);
            SendToHost(new NetWriter(Msg.Hello).Str(Me.Name).Str(Me.Skin).Str(Version.Text), true);
        }

        static void Disconnected(Peer p, string reason)
        {
            if (IsHost)
            {
                PlayerInfo pi;
                if (Players.TryGetValue(p.Id, out pi))
                {
                    Log.Info(pi.Name + " est parti (" + reason + ")");
                    Hud.Toast(pi.Name + " a quitte la partie");
                    Players.Remove(p.Id);
                    if (PlayerLeft != null) PlayerLeft(pi);
                    SendRoster();
                }
                return;
            }
            Log.Warn("deconnecte de l'hote : " + reason);
            Hud.Toast("Deconnecte de l'hote : " + reason);
            foreach (PlayerInfo pi in new List<PlayerInfo>(Players.Values))
                if (!pi.Local && PlayerLeft != null) PlayerLeft(pi);
            Players.Clear();
            Status = "deconnecte (" + reason + "), nouvel essai...";
            hostPeer = null;
            retryAt = Time.realtimeSinceStartup + 3f;
        }

        public static void SendRoster()
        {
            var w = new NetWriter(Msg.Roster).U8(Players.Count);
            foreach (PlayerInfo pi in Players.Values) w.U8(pi.Id).Str(pi.Name).Str(pi.Skin).U8(pi.Level);
            Broadcast(w, true);
        }

        static void Message(Peer from, byte[] data, int off, int len)
        {
            var r = new NetReader(data, off, len);
            Msg type = (Msg)r.U8();
            try
            {
                switch (type)
                {
                    case Msg.Hello: OnHello(from, r); break;
                    case Msg.Profile: OnProfile(from, r); break;
                    case Msg.Interact: Interactions.OnMessage(from, r); break;
                    case Msg.Vehicle: VehicleSync.OnMessage(from, r); break;
                    case Msg.Part: Parts.OnMessage(from, r); break;
                    case Msg.Bolt: Parts.OnBolt(from, r); break;
                    case Msg.Prop: Props.OnMessage(from, r); break;
                    case Msg.Income: Wallet.OnMessage(from, r); break;
                    case Msg.Purchase: Shop.OnMessage(from, r); break;
                    case Msg.Paint: Paint.OnMessage(from, r); break;
                    case Msg.Setting: Settings.OnMessage(from, r); break;
                    case Msg.Fluid: Fluids.OnMessage(from, r); break;
                    case Msg.Consume: Consume.OnMessage(from, r); break;
                    case Msg.Voice: Voices.OnMessage(from, r); break;
                    case Msg.CarDoor: CarDoors.OnMessage(from, r); break;
                    case Msg.CarVisual: CarVisuals.OnMessage(from, r); break;
                    case Msg.Seat: Seats.OnMessage(from, r); break;
                    case Msg.Machine: Machines.OnMessage(from, r); break;
                    case Msg.Stock: Stock.OnMessage(from, r); break;
                    case Msg.Npc: Npcs.OnMessage(from, r); break;
                    case Msg.Parked: Parked.OnMessage(from, r); break;
                    case Msg.WorldFsm: WorldFsms.OnMessage(from, r); break;
                    case Msg.WorldVars: WorldFsms.OnVars(from, r); break;
                    case Msg.Traffic: Traffic.OnMessage(from, r); break;
                    case Msg.Job: Jobs.OnMessage(from, r); break;
                    case Msg.Roster: OnRoster(r); break;
                    case Msg.PlayerState: PlayerSync.OnState(from, r, data, off, len); break;
                    case Msg.Chat: Chat.OnMessage(from, r); break;
                    case Msg.SaveBegin: case Msg.SaveChunk: case Msg.SaveEnd: SaveTransfer.OnMessage(type, r); break;
                    default: World.OnMessage(type, from, r); break;
                }
            }
            catch (Exception e) { Log.Error("message " + type + " de " + from + " : " + e); }
        }

        static void OnHello(Peer from, NetReader r)
        {
            if (!IsHost) return;
            PlayerInfo pi;
            if (!Players.TryGetValue(from.Id, out pi)) return;
            pi.Name = Clean(r.Str(), 24);
            pi.Skin = Clean(r.Str(), 40);
            string ver = r.Str();
            Log.Info("bonjour de " + pi.Name + " (#" + pi.Id + ", MWCoop " + ver + ", apparence " + pi.Skin + ")");
            if (ver != Version.Text)
            {
                T.Kick(from, "version du mod differente (hote " + Version.Text + ", vous " + ver + ")");
                Players.Remove(from.Id);
                return;
            }
            Hud.Toast(pi.Name + " a rejoint la partie");
            SendRoster();
            SaveTransfer.Queue(from);
        }

        // Pseudo/apparence changes en cours de partie : l'invite previent l'hote, qui renvoie la liste.
        public static void SendProfile()
        {
            if (!Active) return;
            if (IsHost) SendRoster();
            else SendToHost(new NetWriter(Msg.Profile).Str(Me.Name).Str(Me.Skin), true);
        }

        static void OnProfile(Peer from, NetReader r)
        {
            PlayerInfo pi;
            if (!IsHost || !Players.TryGetValue(from.Id, out pi)) return;
            pi.Name = Clean(r.Str(), 24);
            pi.Skin = Clean(r.Str(), 40);
            SendRoster();
        }

        static void OnRoster(NetReader r)
        {
            if (IsHost) return;
            var seen = new HashSet<int>();
            int n = r.U8();
            for (int i = 0; i < n; i++)
            {
                int id = r.U8();
                string name = r.Str(), skin = r.Str();
                int level = r.U8();
                seen.Add(id);
                PlayerInfo pi;
                if (!Players.TryGetValue(id, out pi))
                {
                    pi = new PlayerInfo { Id = id };
                    Players[id] = pi;
                    if (id != LocalId) Hud.Toast(name + " est dans la partie");
                }
                if (pi.Local) continue;
                pi.Name = name; pi.Skin = skin; pi.Level = level;
            }
            foreach (PlayerInfo pi in new List<PlayerInfo>(Players.Values))
                if (!pi.Local && !seen.Contains(pi.Id))
                {
                    Players.Remove(pi.Id);
                    Hud.Toast(pi.Name + " a quitte la partie");
                    if (PlayerLeft != null) PlayerLeft(pi);
                }
        }

        public static string Clean(string s, int max)
        {
            if (s == null) return "";
            s = s.Replace("\n", " ").Replace("\r", " ").Trim();
            return s.Length > max ? s.Substring(0, max) : s;
        }

        public static PlayerInfo Host { get { PlayerInfo h; Players.TryGetValue(0, out h); return h; } }
    }
}
