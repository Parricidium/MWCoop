using System;
using System.Collections.Generic;
using MWCoop.Net;
using UnityEngine;

namespace MWCoop
{
    /// <summary>
    /// Public API of MWCoop for other mods (MSCLoader mods or anything loaded in the game), so a mod can keep its own
    /// state in sync between the players of a co-op session. Documentation and examples: docs/API.md.
    /// <para>To avoid a hard dependency on MWCoop.dll, find the type by reflection:
    /// <c>Type.GetType("MWCoop.CoopApi, MWCoop")</c> (null when MWCoop is not installed).</para>
    /// <para>Everything runs on Unity's main thread. Messages from other players are delivered during MWCoop's
    /// update, in the order they were sent (reliable messages).</para>
    /// </summary>
    // (API publique, en anglais pour les auteurs de mods - demande de JD, 08/10. Message Msg.ModData ; l'hote relaie.)
    public static class CoopApi
    {
        /// <summary>API version: raised when something is added. Check it if you use a newer member.</summary>
        public const int Version = 5;

        /// <summary>Largest payload of <see cref="Send"/> (reliable; it is split in packets and rebuilt on the other side).</summary>
        public const int MaxReliableBytes = 64 * 1024;

        /// <summary>Largest payload of an unreliable message (one packet; the latest one wins, some may be lost).</summary>
        public const int MaxUnreliableBytes = 1000;

        /// <summary>The game MWCoop runs in: "My Winter Car" or "My Summer Car" (same API in both).</summary>
        public static string GameName { get { return Game.Name; } }

        /// <summary>True in My Summer Car, false in My Winter Car.</summary>
        public static bool IsMySummerCar { get { return Game.Msc; } }

        /// <summary>True when a co-op session is running (host or guest), false in solo.</summary>
        public static bool InSession { get { return Session.Active; } }

        /// <summary>True on the host's game. The host has authority: save, time, weather, traffic.</summary>
        public static bool IsHost { get { return Session.Active && Session.IsHost; } }

        /// <summary>True once the local player is in the game world (not at the main menu).</summary>
        public static bool InGame { get { return PlayerSync.InGame; } }

        /// <summary>Id of the local player in the session (the host is 0).</summary>
        public static int LocalPlayerId { get { return Session.LocalId; } }

        /// <summary>Ids of every player in the session, the local one included, sorted.</summary>
        public static int[] PlayerIds()
        {
            var l = new List<int>(Session.Players.Keys);
            l.Sort();
            return l.ToArray();
        }

        /// <summary>Nickname of a player ("" if unknown).</summary>
        public static string PlayerName(int playerId)
        {
            PlayerInfo pi;
            return Session.Players.TryGetValue(playerId, out pi) && pi != null ? pi.Name ?? "" : "";
        }

        /// <summary>Sends data to every other player on a channel (use your mod ID). Reliable: delivered once, in order.</summary>
        public static void Send(string channel, byte[] data) { SendImpl(channel, data, To.All, true); }

        /// <summary>Sends data to one player on a channel. Reliable: delivered once, in order.</summary>
        public static void SendTo(int playerId, string channel, byte[] data) { SendImpl(channel, data, playerId, true); }

        /// <summary>Sends small, frequent data to every other player (positions...): at most <see cref="MaxUnreliableBytes"/>,
        /// may be lost, no order guaranteed.</summary>
        public static void SendUnreliable(string channel, byte[] data) { SendImpl(channel, data, To.All, false); }

        /// <summary>Receives the data other players send on a channel: handler(sender player id, data).
        /// <para>Reliable messages that arrived before anyone listened to the channel (the other game was faster to load)
        /// are kept a while and delivered to the first handler, in order.</para></summary>
        public static void Listen(string channel, Action<int, byte[]> handler)
        {
            if (string.IsNullOrEmpty(channel) || handler == null) return;
            List<Action<int, byte[]>> l;
            if (!handlers.TryGetValue(channel, out l)) handlers[channel] = l = new List<Action<int, byte[]>>();
            if (!l.Contains(handler)) l.Add(handler);
            List<KeyValuePair<int, byte[]>> wait;
            if (!waiting.TryGetValue(channel, out wait)) return;
            waiting.Remove(channel);
            foreach (var m in wait) { waitingBytes -= m.Value.Length; Deliver(channel, m.Key, m.Value); }
        }

        /// <summary>Stops receiving a channel with this handler.</summary>
        public static void StopListening(string channel, Action<int, byte[]> handler)
        {
            List<Action<int, byte[]>> l;
            if (channel != null && handlers.TryGetValue(channel, out l)) l.Remove(handler);
        }

        /// <summary>A player joined the session (their id). Players already there when you subscribe are not announced:
        /// call <see cref="PlayerIds"/> once, then follow these events.</summary>
        public static event Action<int> PlayerJoined;

        /// <summary>A player left the session (their id).</summary>
        public static event Action<int> PlayerLeft;

        /// <summary>Same as <see cref="PlayerJoined"/>, as a method (easier through reflection).</summary>
        public static void OnPlayerJoined(Action<int> handler) { PlayerJoined += handler; }

        /// <summary>Same as <see cref="PlayerLeft"/>, as a method (easier through reflection).</summary>
        public static void OnPlayerLeft(Action<int> handler) { PlayerLeft += handler; }

        // ------------------------------------------------------------ parts shop (version 2)
        // (magasin de pieces CARDINAL VARAOSAT -- JD, 23/10 : un batiment ou les mods vendent leurs objets ; Kauppa.OsatMyynti)

        /// <summary>True when the CARDINAL VARAOSAT parts shop exists in this game (add-on on; the host's choice
        /// applies to everyone). Items can be added at any time anyway: they show up when the shop is built.</summary>
        public static bool ShopAvailable { get { return Ajouts.Osat; } }

        /// <summary>Puts an item of your mod on sale in the CARDINAL VARAOSAT parts shop (left wing). Version 2.
        /// <para><paramref name="id"/>: unique and the same on every player's game (e.g. "MyMod.Exhaust"); adding the same id
        /// again updates it. <paramref name="model"/>: shown on a shelf (only its meshes are copied, it is never touched; may be
        /// inactive, null = no model); big items go on a display stand. Unlimited stock.</para>
        /// <para><paramref name="onBought"/>(buyerId, boughtHere, position, rotation) is called on EVERY player's game, in the
        /// same order, when someone buys it (the buyer has paid): spawn your object at position/rotation (the NOUTO pick-up
        /// pallet). Spawn it on every game if your mod syncs it by itself, or only where boughtHere is true if you send it
        /// with <see cref="Send"/>. Returns false if the arguments are wrong.</para></summary>
        public static bool AddShopItem(string id, string name, float price, GameObject model, Action<int, bool, Vector3, Quaternion> onBought)
        {
            return Kauppa.OsatAddModItem(id, name, price, model, onBought);
        }

        /// <summary>Takes an item added with <see cref="AddShopItem"/> off the shelves.</summary>
        public static void RemoveShopItem(string id) { Kauppa.OsatRemoveModItem(id); }

        /// <summary>v3. Adds a place of your mod to the M map and to the /place cheat (same key again: replaced). <paramref name="key"/>
        /// and <paramref name="alias"/>: one lower-case word each (/place key or /place alias) ; <paramref name="at"/>: where the player
        /// is put (on the ground) ; <paramref name="face"/>: a point he looks at. Call it after each game load (places are kept until
        /// you remove them).</summary>
        public static void AddMapPlace(string key, string alias, string nameFr, string nameEn, Vector3 at, Vector3 face) { Cheats.AddPlace(key, alias, nameFr, nameEn, at, face); }

        /// <summary>v3. Removes a place added with <see cref="AddMapPlace"/>.</summary>
        public static void RemoveMapPlace(string key) { Cheats.RemovePlace(key); }

        // ------------------------------------------------------------ cars of the F10 menu (version 4)
        // (showroom de JD, 28/10 : un mod a part sort la voiture choisie au milieu de sa salle)

        /// <summary>v4. Cars of the F10 menu installed in this game: the VAZ-2108 and the add-on cars, as "id|name" (id: the
        /// number to give <see cref="ShowF10Car"/>). Empty when the VAZ-2108 add-on is off.</summary>
        public static string[] F10Cars()
        {
            var l = new List<string>();
            if (!Vaz.Enabled) return l.ToArray();
            for (int i = 0; i < VazCar.Models.Length; i++) if (VazCar.InF10(i)) l.Add(i + "|" + VazCar.ModelOf(i).Name);
            return l.ToArray();
        }

        /// <summary>v4. Takes the local player's F10 car (the VAZ-2108 or an add-on car, <paramref name="carId"/> from
        /// <see cref="F10Cars"/>) and puts it at <paramref name="position"/> (0.8 m above the ground), heading <paramref name="yaw"/>
        /// (degrees). The car the player already has is moved if it is the same model, else given back first. Works in solo and in
        /// co-op (a guest asks the host). False when refused (add-on off, unknown car, no free number: a message is shown).</summary>
        public static bool ShowF10Car(int carId, Vector3 position, float yaw) { return Vaz.RequestAt(carId, position, yaw); }

        /// <summary>v5. Adds a car of your own to the F10 menu, with everything the add-on cars of MWCoop have (doors, belts, gauges,
        /// lights, wipers, handbrake, seats, co-op sync). Call it while your mod loads, before the player enters the game world.
        /// <para><paramref name="key"/>: short unique name (letters and digits); <paramref name="directory"/>: folder with the car files
        /// in MWCoop's add-on format (carrosserie.p205, roue.p205, volant.p205, portiere_g/d.p205, textures, addon.ini for the name);
        /// <paramref name="definition"/>: "name=value" lines -- base (key of the MWCoop car to take the cockpit and controls from,
        /// default m3e30), nom, tete (x,y,z: driver's eyes, car frame), moteur (x,y,z), frein_pivot, frein_prise (x,y,z), frein_angle,
        /// puissance (hp), regime_puissance, couple (N.m), regime_couple, rupteur, masse (kg), avant (share of the weight on the front
        /// axle, 0..1), braquage (degrees), pont, rapports (reverse,neutral,1st,2nd,...), transmission (propulsion, traction or
        /// integrale), yeux_bas (m), couleur (r,g,b, 0..1 or 0..255: its own paint instead of the player's number colour).</para>
        /// <para>Every player of a session needs the same cars: their numbers follow the keys. A player without it sees the base car.
        /// Returns the car number (for <see cref="ShowF10Car"/>), or -1 when refused (the reason is in MWCoop's log).</para></summary>
        public static int AddCar(string key, string directory, string definition)
        {
            string why = AddCarImpl(key, directory, definition);
            if (why == null) { int id = VazCar.Models.Length - 1; foreach (VazCar.Model m in VazCar.Models) if (m != null && m.Folder == key) id = m.Id; Log.Info("api : voiture " + key + " ajoutee, n " + id + " (" + directory + ")"); Remember(key, directory, definition); return id; }
            Log.Warn("api : voiture " + key + " refusee : " + why);
            return -1;
        }

        // Voitures declarees par d'autres mods, notees dans MWCoop\voitures-externes\<cle>.txt (ligne « dossier=... » puis la definition) :
        // l'export des places tourne sans MSCLoader, leur mod ne s'y charge pas -- MWCoop les y declare lui-meme d'apres cette note, pour
        // que l'outil des places les montre (JD 10/11 : une voiture de mod absente de la liste de l'outil). Jamais en partie : la, c'est
        // le mod qui decide.
        static string KnownDir { get { return System.IO.Path.Combine(Environment.GetEnvironmentVariable("MWCOOP_DIR") ?? "MWCoop", "voitures-externes"); } }
        static void Remember(string key, string directory, string definition)
        {
            if (SeatExport.Active) return;
            try
            {
                string t = "dossier=" + directory + "\n" + (definition ?? "").Replace("\r", "");
                string f = System.IO.Path.Combine(KnownDir, key + ".txt");
                if (System.IO.File.Exists(f) && System.IO.File.ReadAllText(f) == t) return;
                System.IO.Directory.CreateDirectory(KnownDir);
                System.IO.File.WriteAllText(f, t, new System.Text.UTF8Encoding(false));
            }
            catch (Exception e) { Log.Warn("api : voiture " + key + " : note pour l'export des places (" + e.Message + ")"); }
        }
        internal static void AddRememberedForExport()
        {
            if (!SeatExport.Active || !System.IO.Directory.Exists(KnownDir)) return;
            foreach (string f in System.IO.Directory.GetFiles(KnownDir, "*.txt"))
            {
                string key = System.IO.Path.GetFileNameWithoutExtension(f);
                try
                {
                    bool have = false; foreach (VazCar.Model m in VazCar.Models) if (m != null && m.Folder == key) have = true;
                    if (have) continue;
                    string t = System.IO.File.ReadAllText(f).Replace("\r", "");
                    int nl = t.IndexOf('\n'); string first = nl < 0 ? t : t.Substring(0, nl);
                    if (!first.StartsWith("dossier=")) { Log.Warn("export des places : " + f + " illisible"); continue; }
                    string dir = first.Substring(8).Trim();
                    if (!System.IO.Directory.Exists(dir)) { Log.Info("export des places : voiture " + key + " notee, mais son dossier n'existe plus (" + dir + ")"); continue; }
                    Log.Info("export des places : voiture " + key + " d'un autre mod reprise de sa note");
                    AddCar(key, dir, nl < 0 ? "" : t.Substring(nl + 1));
                }
                catch (Exception e) { Log.Warn("export des places : voiture " + key + " (" + e.Message + ")"); }
            }
        }
        static string AddCarImpl(string key, string directory, string definition)
        {
            if (string.IsNullOrEmpty(key) || key.Length > 24) return "cle vide ou trop longue";
            foreach (char c in key) if (!char.IsLetterOrDigit(c)) return "cle : lettres et chiffres seulement";
            if (string.IsNullOrEmpty(directory) || !System.IO.File.Exists(System.IO.Path.Combine(directory, "carrosserie.p205"))) return "pas de carrosserie.p205 dans " + directory;
            var v = new Dictionary<string, string>();
            foreach (string raw in (definition ?? "").Split('\n'))
            {
                string l = raw; int c = l.IndexOf('#'); if (c >= 0) l = l.Substring(0, c);
                int eq = l.IndexOf('='); if (eq <= 0) continue;
                v[l.Substring(0, eq).Trim().ToLowerInvariant()] = l.Substring(eq + 1).Trim();
            }
            string bk; if (!v.TryGetValue("base", out bk) || bk.Length == 0) bk = "m3e30";
            VazCar.Model b = null;
            foreach (VazCar.Model x in VazCar.Models) if (x != null && x.Folder == bk && x.Standalone && x.Sp != null && (b == null || x.Id > b.Id)) b = x;
            if (b == null) return "voiture de base inconnue : " + bk;
            VazCar.Model m = b.ExternalOf(key, directory);
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            System.Func<string, float, float> F = (k, d) => { string s; float f; return v.TryGetValue(k, out s) && float.TryParse(s, System.Globalization.NumberStyles.Float, ci, out f) ? f : d; };
            System.Func<string, Vector3, Vector3> V = (k, d) =>
            {
                string s; if (!v.TryGetValue(k, out s)) return d;
                string[] w = s.Split(','); float x, y, z;
                return w.Length == 3 && float.TryParse(w[0], System.Globalization.NumberStyles.Float, ci, out x) && float.TryParse(w[1], System.Globalization.NumberStyles.Float, ci, out y)
                       && float.TryParse(w[2], System.Globalization.NumberStyles.Float, ci, out z) ? new Vector3(x, y, z) : d;
            };
            string nom; if (v.TryGetValue("nom", out nom) && nom.Length > 0) { m.Name = nom; m.Sp.Name = nom; }
            m.Head = V("tete", m.Head); m.Engine = V("moteur", m.Engine);
            m.BrakePivot = V("frein_pivot", m.BrakePivot); m.BrakeGrip = V("frein_prise", m.BrakeGrip); m.BrakeAngle = F("frein_angle", m.BrakeAngle);
            m.SoloEyeDrop = F("yeux_bas", m.SoloEyeDrop);
            VazCar.Spec s0 = m.Sp;
            s0.Power = F("puissance", s0.Power); s0.PowerRpm = F("regime_puissance", s0.PowerRpm); s0.Torque = F("couple", s0.Torque); s0.TorqueRpm = F("regime_couple", s0.TorqueRpm);
            s0.MaxRpm = F("rupteur", s0.MaxRpm); s0.Mass = F("masse", s0.Mass); s0.Com = F("avant", s0.Com); s0.Steer = F("braquage", s0.Steer); s0.Final = F("pont", s0.Final);
            string g; if (v.TryGetValue("rapports", out g))
            {
                var gl = new List<float>();
                foreach (string t in g.Split(',')) { float f; if (float.TryParse(t.Trim(), System.Globalization.NumberStyles.Float, ci, out f)) gl.Add(f); }
                if (gl.Count >= 3) s0.Gears = gl.ToArray();
            }
            Vector3 col = V("couleur", new Vector3(-1f, 0f, 0f));
            if (col.x >= 0f) { if (col.x > 1f || col.y > 1f || col.z > 1f) col /= 255f; m.HasPaint = true; m.Paint = new Color(col.x, col.y, col.z, 1f); }
            string tr; if (v.TryGetValue("transmission", out tr)) { tr = tr.ToLowerInvariant(); s0.Rwd = tr == "propulsion"; s0.Fwd = tr == "traction"; }
            return VazCar.AddExternal(m);
        }

        // ------------------------------------------------------------ dessous (MWCoop)
        static class To { public const int All = 255; }
        const int Part = 960;   // octets par paquet (avec l'en-tete, sous Transport.MaxPayload)
        static readonly Dictionary<string, List<Action<int, byte[]>>> handlers = new Dictionary<string, List<Action<int, byte[]>>>();
        static readonly Dictionary<string, List<byte[]>> partial = new Dictionary<string, List<byte[]>>();
        static readonly HashSet<int> known = new HashSet<int>();
        // messages fiables d'un canal que personne n'ecoute encore (mod pas encore charge) : gardes jusqu'a 1 Mo
        static readonly Dictionary<string, List<KeyValuePair<int, byte[]>>> waiting = new Dictionary<string, List<KeyValuePair<int, byte[]>>>();
        static int waitingBytes;
        const int MaxWaitingBytes = 1024 * 1024;
        static int nextId;

        // Message : [u8 de][u8 pour (255 : tous)][u8 fiable][texte canal][u16 numero][u8 morceau][u8 morceaux][octets]
        static void SendImpl(string channel, byte[] data, int to, bool reliable)
        {
            if (!Session.Active || string.IsNullOrEmpty(channel)) return;
            if (data == null) data = new byte[0];
            if (!reliable && data.Length > MaxUnreliableBytes) { Log.Warn("api : message non fiable trop gros (" + channel + ", " + data.Length + " octets)"); return; }
            if (data.Length > MaxReliableBytes) { Log.Warn("api : message trop gros (" + channel + ", " + data.Length + " octets)"); return; }
            int parts = Math.Max(1, (data.Length + Part - 1) / Part), id = nextId++ & 0xFFFF;
            for (int k = 0; k < parts; k++)
            {
                int off = k * Part, n = Math.Min(Part, data.Length - off);
                var w = new NetWriter(Msg.ModData).U8(Session.LocalId).U8(to).U8(reliable ? 1 : 0).Str(channel).U16(id).U8(k).U8(parts).Bytes(data, off, Math.Max(0, n));
                if (Session.IsHost)
                {
                    if (to == To.All) Session.Broadcast(w, reliable);
                    else foreach (Peer p in Session.T.Peers) if (p.Accepted && p.Id == to) { if (reliable) Session.T.SendReliable(p, w.ToArray()); else Session.T.SendUnreliable(p, w.ToArray()); }
                }
                else Session.SendToHost(w, reliable);
            }
        }

        // Recu : l'hote relaie (a tous, ou au destinataire) et garde ce qui est pour lui ; recolle les morceaux.
        internal static void OnMessage(Peer from, NetReader r)
        {
            int who = r.U8(), to = r.U8();
            bool reliable = r.U8() != 0;
            string channel = r.Str();
            int id = r.U16(), part = r.U8(), parts = r.U8();
            byte[] chunk = r.Bytes();
            if (Session.IsHost)
            {
                who = from.Id;
                var w = new NetWriter(Msg.ModData).U8(who).U8(to).U8(reliable ? 1 : 0).Str(channel).U16(id).U8(part).U8(parts).Bytes(chunk, 0, chunk.Length);
                if (to == To.All) Session.Broadcast(w, reliable, who);
                else if (to != 0) foreach (Peer p in Session.T.Peers) if (p.Accepted && p.Id == to) { if (reliable) Session.T.SendReliable(p, w.ToArray()); else Session.T.SendUnreliable(p, w.ToArray()); }
                if (to != To.All && to != 0) return;
            }
            byte[] data;
            if (parts <= 1) data = chunk;
            else
            {
                string key = who + "|" + channel + "|" + id;
                List<byte[]> got;
                if (part == 0 || !partial.TryGetValue(key, out got)) partial[key] = got = new List<byte[]>();
                got.Add(chunk);
                if (part < parts - 1) return;
                partial.Remove(key);
                int total = 0;
                foreach (byte[] c in got) total += c.Length;
                data = new byte[total];
                int at = 0;
                foreach (byte[] c in got) { Buffer.BlockCopy(c, 0, data, at, c.Length); at += c.Length; }
            }
            List<Action<int, byte[]>> l;
            if (handlers.TryGetValue(channel, out l) && l.Count > 0) Deliver(channel, who, data);
            else if (reliable && waitingBytes + data.Length <= MaxWaitingBytes)
            {
                List<KeyValuePair<int, byte[]>> wait;
                if (!waiting.TryGetValue(channel, out wait)) waiting[channel] = wait = new List<KeyValuePair<int, byte[]>>();
                wait.Add(new KeyValuePair<int, byte[]>(who, data));
                waitingBytes += data.Length;
            }
        }

        static void Deliver(string channel, int who, byte[] data)
        {
            List<Action<int, byte[]>> l;
            if (!handlers.TryGetValue(channel, out l)) return;
            foreach (Action<int, byte[]> h in l.ToArray())
            {
                try { h(who, data); }
                catch (Exception e) { Log.Error("api : erreur du mod sur le canal " + channel + " : " + e); }
            }
        }

        // Chaque image (Core) : arrivees et departs, d'apres la liste des joueurs de la session.
        internal static void Update()
        {
            if (!Session.Active)
            {
                if (known.Count > 0) { foreach (int id in new List<int>(known)) Raise(PlayerLeft, id); known.Clear(); }
                if (waiting.Count > 0 || partial.Count > 0) { waiting.Clear(); waitingBytes = 0; partial.Clear(); }
                return;
            }
            foreach (int id in Session.Players.Keys)
                if (id != Session.LocalId && known.Add(id)) Raise(PlayerJoined, id);
            // (departs : liste reutilisee, remplie seulement s'il en manque un -- avant, une liste neuve a chaque image)
            gone.Clear();
            foreach (int id in known) if (!Session.Players.ContainsKey(id)) gone.Add(id);
            foreach (int id in gone) { known.Remove(id); Raise(PlayerLeft, id); }
        }
        static readonly List<int> gone = new List<int>();

        static void Raise(Action<int> ev, int id)
        {
            if (ev == null) return;
            foreach (Delegate d in ev.GetInvocationList())
            {
                try { ((Action<int>)d)(id); }
                catch (Exception e) { Log.Error("api : erreur d'un mod (arrivee ou depart d'un joueur) : " + e); }
            }
        }
    }
}
