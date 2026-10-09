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
        public ulong SteamId;          // compte Steam (0 : inconnu) : avatar a cote du pseudo
        public float StateTime;
    }

    public struct PlayerState
    {
        public Vector3 Feet;           // bas du personnage (sol)
        public Vector3 Head;           // camera (yeux) : place l'avatar assis dans un vehicule
        public float Yaw, Pitch, Height, Speed;
        public int Flags;              // PlayerSync.F_* : accroupi, assis, fume, boit, porte, salue, dort
        public int Drink;              // ce qu'il a en main en buvant : rang dans Drinks.Names (0 : rien)
    }

    // Session coop : l'hote fait autorite et relaie tout. [Coop] Mode=solo|hote|invite.
    public static class Session
    {
        public const int NetVersion = 41;   // 38 : moteur en marche (bit 15 du masque des sons d'une voiture) ; 39 : klaxon (bit 14) ; 40 : pose d'un PNJ dans sa voiture (Npcs, drapeau 4) ; 41 : objet range (Props, etat 4)
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

        // Reseau choisi dans le lanceur ([Coop] Reseau) : "ip" (adresse:port, UDP) ou "steam" (salon et pair-a-pair Steam).
        public static bool Steam;
        // Invite parti du salon Steam du lanceur ([Coop] HoteSteam, lancement.ini) : compte de l'hote, joint directement.
        static ulong hostSteam;

        public static void Start()
        {
            string mode = Config.Get("Coop", "Mode", "solo").ToLowerInvariant();
            Steam = Config.Get("Coop", "Reseau", "ip").ToLowerInvariant() == "steam";
            if (!ulong.TryParse(Config.Get("Coop", "HoteSteam", ""), out hostSteam)) hostSteam = 0;
            SteamNet.DirectHost = hostSteam;
            port = Config.GetInt("Coop", "Port", 7870);
            Me = new PlayerInfo { Local = true, Name = Config.Get("Coop", "Pseudo", Environment.UserName),
                                  Skin = Looks.FromConfig() };
            // Essais : [Test] HoteRetard=s -- l'hote n'ecoute qu'apres s secondes (invite arrive avant lui).
            int late = Config.GetInt("Test", "HoteRetard", 0);
            if ((mode == "hote" || mode == "host") && late > 0) { hostLateAt = Time.realtimeSinceStartup + late; Log.Info("essai : hote dans " + late + " s"); }
            else if (mode == "hote" || mode == "host") StartHost();
            else if (mode == "invite" || mode == "client")
            {
                if (Steam && hostSteam != 0) { Active = true; IsHost = false; Status = "connexion a l'hote Steam"; Log.Info("invite par Steam : hote " + hostSteam + " (salon du lanceur)"); }
                else if (Steam) { Active = true; IsHost = false; Status = "attente d'une invitation Steam"; Log.Info("invite par Steam : attente d'une invitation ou du salon d'un ami"); }
                else { address = Config.Get("Coop", "Adresse", "127.0.0.1"); StartClient(); }
            }
        }

        static Transport NewTransport()
        {
            // Horloge du transport a l'heure du jeu des sa creation : sinon le pair ajoute par Connect (LastHeard = 0) etait
            // juge muet depuis plus de 15 s au premier Update, et chaque nouvel essai de l'invite abandonne en quelques
            // millisecondes -- un invite dont le 1er essai echouait (hote pas encore pret) ne se connectait plus jamais.
            var t = new Transport { NetVersion = NetVersion, Now = Time.realtimeSinceStartup };
            t.OnConnected += Connected;
            t.OnDisconnected += Disconnected;
            t.OnMessage += Message;
            t.OnRejected += r =>
            {
                // "delai depasse" avant toute reponse : l'hote n'a rien recu (adresse, pare-feu de son jeu, box), ce
                // n'est pas un refus. On reessaie tout de suite.
                bool silent = r == "delai depasse";
                string where = Steam ? address : address + ":" + port;
                Status = silent ? "sans reponse de " + where : "refuse : " + r;
                Log.Warn(silent ? "aucune reponse de l'hote " + where + (Steam ? " (Steam)" : " (pare-feu du jeu de l'hote ? adresse ?)") : "connexion refusee : " + r);
                hostPeer = null;
                retryAt = Time.realtimeSinceStartup + (silent ? 0.5f : 5f);
            };
            return t;
        }

        static void StartHost()
        {
            try
            {
                T = NewTransport();
                if (Steam) { T.Host(new SteamLink(true)); SteamNet.Host(); }
                else T.Host(port);
                Active = IsHost = true;
                LocalId = Me.Id = 0;
                Players[0] = Me;
                Status = Steam ? "hote Steam" : "hote, port " + port;
                Log.Info(Steam ? "hote par Steam (salon et pair-a-pair)" : "hote sur le port UDP " + port);
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

        static float hostLateAt = -1;

        public static void Update()
        {
            if (hostLateAt > 0 && Time.realtimeSinceStartup >= hostLateAt) { hostLateAt = -1; StartHost(); }
            if (!Active) return;
            float now = Time.realtimeSinceStartup;
            if (Me != null && Me.SteamId == 0) { Me.SteamId = SteamNet.MyId; if (Me.SteamId != 0) RosterIfSteamKnown(); }
            if (Steam)
            {
                SteamNet.Update();
                // Invite : connexion au proprietaire du salon Steam rejoint (l'hote).
                if (!IsHost && hostPeer == null && now >= retryAt)
                {
                    ulong owner = SteamNet.LobbyOwner != 0 ? SteamNet.LobbyOwner : hostSteam;
                    if (owner != 0 && SteamNet.Ready) StartClientSteam(owner);
                    else if (!Status.StartsWith("deconnecte") && !Status.StartsWith("refuse")) Status = "attente d'une invitation Steam";
                }
            }
            else if (!IsHost && hostPeer == null && now >= retryAt) StartClient();
            if (T != null) T.Update(now);
        }

        static void StartClientSteam(ulong owner)
        {
            try
            {
                if (T != null) T.Close();
                T = NewTransport();
                address = SteamNet.Name(owner);
                hostPeer = T.Connect(new SteamLink(false), (object)owner);
                Status = "connexion a " + address + " (Steam)...";
                Log.Info("connexion par Steam a " + address + " (" + owner + ")");
            }
            catch (Exception e) { Status = "erreur : " + e.Message; Log.Error("invite Steam : " + e); retryAt = Time.realtimeSinceStartup + 5f; }
        }

        public static void Stop()
        {
            if (Steam) SteamNet.Leave();
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
            SendToHost(new NetWriter(Msg.Hello).Str(Me.Name).Str(Me.Skin).Str(Version.Text).Str(SteamNet.MyId.ToString()), true);
        }

        static void Disconnected(Peer p, string reason)
        {
            if (IsHost)
            {
                PlayerInfo pi;
                if (Players.TryGetValue(p.Id, out pi))
                {
                    Log.Info(pi.Name + " est parti (" + reason + ")");
                    Hud.Toast(pi.Name + Lang.T(" a quitt\u00E9 la partie", " left the game"));
                    Players.Remove(p.Id);
                    if (PlayerLeft != null) PlayerLeft(pi);
                    SendRoster();
                }
                return;
            }
            if (reason.StartsWith("exclu"))   // exclu par l'hote (Admin.Kick) : pas de nouvel essai tout seul
            {
                Log.Warn("exclu par l'hote");
                Hud.Toast(Lang.T("Vous avez \u00E9t\u00E9 exclu de la partie par l'h\u00F4te", "You were kicked from the game by the host"));
                foreach (PlayerInfo pk in new List<PlayerInfo>(Players.Values))
                    if (!pk.Local && PlayerLeft != null) PlayerLeft(pk);
                Players.Clear();
                Status = "exclu par l'hote";
                hostPeer = null;
                retryAt = float.MaxValue;
                return;
            }
            Log.Warn("deconnecte de l'hote : " + reason);
            Hud.Toast(Lang.T("D\u00E9connect\u00E9 de l'h\u00F4te : ", "Disconnected from the host: ") + reason);
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
            foreach (PlayerInfo pi in Players.Values) w.U8(pi.Id).Str(pi.Name).Str(pi.Skin).U8(pi.Level).Str(pi.SteamId.ToString());
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
                    case Msg.Payout: Wallet.OnPayout(from, r); break;
                    case Msg.Race: Races.OnMessage(from, r); break;
                    case Msg.PushDoor: PushDoors.OnMessage(from, r); break;
                    case Msg.Summon: Admin.OnSummon(from, r); break;
                    case Msg.Radio: Radio.OnMessage(from, r); break;
                    case Msg.ModData: CoopApi.OnMessage(from, r); break;
                    case Msg.Knob: Knobs.OnMessage(from, r); break;
                    case Msg.Event: Events.OnMessage(from, r); break;
                case Msg.Talk: Voice.OnMessage(from, r); break;
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
                    case Msg.MachineLock: Machines.OnLock(from, r); break;
                    case Msg.Frost: Frost.OnMessage(from, r); break;
                    case Msg.Tow: Tow.OnMessage(from, r); break;
                    case Msg.Call: Calls.OnMessage(from, r); break;
                    case Msg.Wear: Wear.OnMessage(from, r); break;
                    case Msg.Cook: Cooking.OnMessage(from, r); break;
                    case Msg.Fire: Fires.OnMessage(from, r); break;
                    case Msg.Garage: Garage.OnMessage(from, r); break;
                    case Msg.Home: Home.OnMessage(from, r); break;
                    case Msg.Gesture: Gestures.OnMessage(from, r); break;
                    case Msg.Thrown: Drinks.OnMessage(from, r); break;
                    case Msg.Stock: Stock.OnMessage(from, r); break;
                    case Msg.Npc: Npcs.OnMessage(from, r); break;
                    case Msg.Parked: Parked.OnMessage(from, r); break;
                    case Msg.Audit: Audit.OnMessage(from, r); break;
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
            pi.Skin = Clean(r.Str(), 200);
            string ver = r.Str();
            ulong sid;
            if (r.More && ulong.TryParse(r.Str(), out sid)) pi.SteamId = sid;
            if (from.End is ulong) pi.SteamId = (ulong)from.End;   // (Steam : l'adresse meme du pair)
            Log.Info("bonjour de " + pi.Name + " (#" + pi.Id + ", MWCoop " + ver + ", apparence " + pi.Skin + ")");
            if (ver != Version.Text)
            {
                T.Kick(from, "version du mod differente (hote " + Version.Text + ", vous " + ver + ")");
                Players.Remove(from.Id);
                return;
            }
            Hud.Toast(pi.Name + Lang.T(" a rejoint la partie", " joined the game"));
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
            pi.Skin = Clean(r.Str(), 200);
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
                ulong sid; ulong.TryParse(r.Str(), out sid);
                seen.Add(id);
                PlayerInfo pi;
                if (!Players.TryGetValue(id, out pi))
                {
                    pi = new PlayerInfo { Id = id };
                    Players[id] = pi;
                    if (id != LocalId) Hud.Toast(name + Lang.T(" est dans la partie", " is in the game"));
                }
                if (pi.Local) continue;
                pi.Name = name; pi.Skin = skin; pi.Level = level; pi.SteamId = sid;
            }
            foreach (PlayerInfo pi in new List<PlayerInfo>(Players.Values))
                if (!pi.Local && !seen.Contains(pi.Id))
                {
                    Players.Remove(pi.Id);
                    Hud.Toast(pi.Name + Lang.T(" a quitt\u00E9 la partie", " left the game"));
                    if (PlayerLeft != null) PlayerLeft(pi);
                }
        }

        // Hote : son identifiant Steam (pas connu au lancement) dans la liste des qu'il l'est.
        static bool steamIdSent;
        public static void RosterIfSteamKnown()
        {
            if (!IsHost || steamIdSent || Me == null || Me.SteamId == 0) return;
            steamIdSent = true;
            SendRoster();
        }

        public static string Clean(string s, int max)
        {
            if (s == null) return "";
            s = s.Replace("\n", " ").Replace("\r", " ").Trim();
            return s.Length > max ? s.Substring(0, max) : s;
        }

        public static PlayerInfo Host { get { PlayerInfo h; Players.TryGetValue(0, out h); return h; } }

        // Invite : accepte par l'hote (connexion etablie).
        public static bool HostConnected { get { return !IsHost && hostPeer != null && hostPeer.Accepted; } }
        // Invite : aller-retour avec l'hote, en ms (-1 : pas connecte).
        public static int HostPing { get { return HostConnected ? Mathf.RoundToInt(hostPeer.Rtt * 1000) : -1; } }
    }
}
