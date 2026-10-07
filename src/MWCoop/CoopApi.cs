using System;
using System.Collections.Generic;
using MWCoop.Net;

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
        public const int Version = 1;

        /// <summary>Largest payload of <see cref="Send"/> (reliable; it is split in packets and rebuilt on the other side).</summary>
        public const int MaxReliableBytes = 64 * 1024;

        /// <summary>Largest payload of an unreliable message (one packet; the latest one wins, some may be lost).</summary>
        public const int MaxUnreliableBytes = 1000;

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
            foreach (int id in new List<int>(known))
                if (!Session.Players.ContainsKey(id)) { known.Remove(id); Raise(PlayerLeft, id); }
        }

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
