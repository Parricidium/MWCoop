using System.Collections.Generic;
using MWCoop.Net;
using Steamworks;
using UnityEngine;

namespace MWCoop
{
    // Vocal de proximite (demande de JD, 10/10). Capture et compression par la voix de Steam (SteamUser : le jeu embarque
    // Steamworks.NET et l'initialise, voir SteamNet) : tant que la touche Keys.Voice (V, reglable dans le lanceur) est tenue,
    // les paquets compresses partent aux autres (Msg.Talk, non fiable ; l'hote relaie). Chez chacun, une source audio 3D
    // par joueur, posee a la tete de son avatar (PlayerSync.HeadFrame) : la voix baisse avec la distance (lineaire, muette
    // au-dela de [Coop] VocalPortee m, 35 par defaut) et vient de la bonne direction. Decompression a 24 kHz, file de
    // 2 s lue par l'AudioClip en flux (fil audio). [Coop] Vocal=0 : coupe (ni envoi ni ecoute).
    public static class Voice
    {
        const int Rate = 24000;
        class Speaker
        {
            public int Id; public GameObject Go; public AudioSource Src;
            public readonly float[] Ring = new float[Rate * 2];
            public int Write, Read, Count;
            public float LastHeard = -100f;
            public void Push(byte[] pcm, int bytes)
            {
                lock (Ring)
                    for (int i = 0; i + 1 < bytes; i += 2)
                    {
                        short s = (short)(pcm[i] | pcm[i + 1] << 8);
                        if (Count == Ring.Length) { Read = (Read + 1) % Ring.Length; Count--; }   // (trop en retard : on jette le plus vieux)
                        Ring[Write] = s / 32768f; Write = (Write + 1) % Ring.Length; Count++;
                    }
            }
            public void Fill(float[] data)
            {
                lock (Ring)
                    for (int i = 0; i < data.Length; i++)
                    {
                        if (Count > 0) { data[i] = Ring[Read]; Read = (Read + 1) % Ring.Length; Count--; }
                        else data[i] = 0f;
                    }
            }
        }
        static readonly Dictionary<int, Speaker> speakers = new Dictionary<int, Speaker>();
        static bool recording, failed;
        static readonly byte[] capture = new byte[8192], pcm = new byte[Rate * 2];
        static float lastSent, nextLog;
        static int sentLogs, heardLogs, avLogs;
        static float nextAvLog;
        public static bool Talking { get { return recording; } }
        public static bool Enabled { get { return Config.GetInt("Coop", "Vocal", 1) != 0; } }

        public static void OnLevelLoaded()
        {
            foreach (Speaker s in speakers.Values) if (s.Go != null) Object.Destroy(s.Go);
            speakers.Clear();
            Stop();
        }

        static void Stop()
        {
            if (!recording) return;
            recording = false;
            try { SteamUser.StopVoiceRecording(); } catch { }
        }

        public static void Update()
        {
            if (!PlayerSync.InGame || !Session.Active || !Enabled || failed || !SteamNet.Ready) { Stop(); return; }
            bool want = Input.GetKey(Keys.Voice) && !Menu.Open && !Menu.ChatOpen;
            // [Test] VocalEssai=1 : micro ouvert de 30 a 40 s de partie (sans la touche) ; journal des paquets.
            if (Config.GetInt("Test", "VocalEssai", 0) != 0 && Time.timeSinceLevelLoad > 30f && Time.timeSinceLevelLoad < 40f) want = true;
            try
            {
                if (want && !recording) { SteamUser.StartVoiceRecording(); recording = true; Log.Info("vocal : micro ouvert"); }
                else if (!want && recording) { SteamUser.StopVoiceRecording(); recording = false; }
                // (apres l'arret, Steam rend encore la fin de la phrase : lue jusqu'a ce qu'il n'y ait plus rien)
                uint compressed, raw;
                EVoiceResult av = SteamUser.GetAvailableVoice(out compressed, out raw, 0);
                if (recording && Time.realtimeSinceStartup >= nextAvLog) { nextAvLog = Time.realtimeSinceStartup + 2f; if (avLogs++ < 6) Log.Info("vocal : Steam " + av + ", " + compressed + " octets prets"); }
                if (av == EVoiceResult.k_EVoiceResultOK && compressed > 0)
                {
                    uint got, gotRaw;
                    EVoiceResult r = SteamUser.GetVoice(true, capture, (uint)capture.Length, out got, false, null, 0, out gotRaw, 0);
                    if (r == EVoiceResult.k_EVoiceResultOK && got > 0 && got < 1100)
                    {
                        Session.SendAll(new NetWriter(Msg.Talk).U8(Session.LocalId).Bytes(capture, 0, (int)got), false);
                        lastSent = Time.realtimeSinceStartup;
                        if (sentLogs++ < 5 || sentLogs % 50 == 0) Log.Info("vocal : paquet de " + got + " octets envoye (" + sentLogs + ")");
                    }
                }
                else if (av == EVoiceResult.k_EVoiceResultNotInitialized || av == EVoiceResult.k_EVoiceResultNotRecording) { }
            }
            catch (System.Exception e) { failed = true; recording = false; Log.Warn("vocal : voix de Steam indisponible (" + e.GetType().Name + " : " + e.Message + ")"); }
            // sources a la tete des avatars
            foreach (Speaker s in speakers.Values)
            {
                if (s.Go == null) continue;
                Vector3 p; Quaternion q;
                if (PlayerSync.HeadFrame(s.Id, true, out p, out q)) s.Go.transform.position = p;
                float range = Config.GetFloat("Coop", "VocalPortee", 35f);
                if (s.Src.maxDistance != range) s.Src.maxDistance = range;
            }
        }

        public static void OnMessage(Peer from, NetReader r)
        {
            int id = r.U8();
            if (Session.IsHost) id = from.Id;
            byte[] data = r.Bytes();
            if (Session.IsHost) Session.Broadcast(new NetWriter(Msg.Talk).U8(id).Bytes(data, 0, data.Length), false, id);
            if (!Enabled || id == Session.LocalId || !PlayerSync.InGame) return;
            Speaker s = Get(id);
            if (s == null) return;
            try
            {
                uint got;
                EVoiceResult res = SteamUser.DecompressVoice(data, (uint)data.Length, pcm, (uint)pcm.Length, out got, Rate);
                if (res == EVoiceResult.k_EVoiceResultOK && got > 0) { s.Push(pcm, (int)got); s.LastHeard = Time.realtimeSinceStartup; if (heardLogs++ < 5 || heardLogs % 50 == 0) Log.Info("vocal : " + got + " octets de voix de #" + id + " (" + heardLogs + "), source a " + s.Go.transform.position.ToString("F1")); }
                else if (heardLogs++ < 5) Log.Info("vocal : decompression " + res);
            }
            catch (System.Exception e) { if (Time.realtimeSinceStartup >= nextLog) { nextLog = Time.realtimeSinceStartup + 30f; Log.Warn("vocal : decompression impossible (" + e.Message + ")"); } }
        }

        static Speaker Get(int id)
        {
            Speaker s;
            if (speakers.TryGetValue(id, out s) && s.Go != null) return s;
            s = new Speaker { Id = id };
            s.Go = new GameObject("MWCoop-Voix-" + id);
            Object.DontDestroyOnLoad(s.Go);
            s.Src = s.Go.AddComponent<AudioSource>();
            Speaker sp = s;
            s.Src.clip = AudioClip.Create("voix-" + id, Rate, 1, Rate, true, delegate (float[] d) { sp.Fill(d); });
            s.Src.loop = true;
            s.Src.spatialBlend = 1f;
            s.Src.rolloffMode = AudioRolloffMode.Linear;
            s.Src.minDistance = 1.5f;
            s.Src.maxDistance = Config.GetFloat("Coop", "VocalPortee", 35f);
            s.Src.dopplerLevel = 0f;
            s.Src.volume = Mathf.Clamp01(Config.GetFloat("Coop", "VocalVolume", 1f));
            s.Src.Play();
            speakers[id] = s;
            Log.Info("vocal : voix de #" + id + " ecoutee (portee " + s.Src.maxDistance + " m)");
            return s;
        }

        // Parle en ce moment (pour le pseudo) : voix recue il y a moins de 0,3 s.
        public static bool Speaking(int id)
        {
            Speaker s;
            return speakers.TryGetValue(id, out s) && Time.realtimeSinceStartup - s.LastHeard < 0.3f;
        }

        public static void PlayerLeft(int id)
        {
            Speaker s;
            if (speakers.TryGetValue(id, out s)) { if (s.Go != null) Object.Destroy(s.Go); speakers.Remove(id); }
        }
    }
}
