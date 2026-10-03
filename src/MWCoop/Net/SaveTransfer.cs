using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace MWCoop.Net
{
    // L'hote envoie sa sauvegarde (fichiers .txt du dossier du jeu) a chaque invite qui arrive,
    // une fois en jeu depuis 15 s (lancement groupe : pieces posees, couleur de voiture appliquee).
    // Nouvelle partie : le jeu n'ecrit en la commencant qu'une sauvegarde partielle (700 octets,
    // sans les pieces) ; l'hote sauvegarde alors pour de bon (SAVEGAME, comme en quittant) avant
    // le premier envoi, pour que l'invite ait le meme monde.
    // L'invite ne l'ecrit QUE dans un profil isole (MWCoop\profils\<profil>, cf. le chargeur) :
    // sa propre sauvegarde n'est jamais touchee.
    public static class SaveTransfer
    {
        const int Chunk = 1000;
        static readonly string[] Skip = { "options.txt", "steam_autocloud.vdf", "Mods.txt" };

        public static bool Done;              // invite : transfert termine (meme vide ou refuse)
        public static bool Received;          // invite : sauvegarde de l'hote recue et ecrite
        public static bool HostHasSave;       // invite : l'hote avait une sauvegarde a envoyer
        public static float Progress;         // invite : 0..1 pendant la reception
        static List<string> names;
        static byte[][] files;
        static int expected, got;
        static readonly List<Peer> waiting = new List<Peer>();
        static float inGameSince = -1, sendAt = -1;
        static bool mustSave;            // nouvelle partie lancee : pas encore de vraie sauvegarde

        // Hote : un invite vient d'arriver ; sa sauvegarde partira des que possible.
        public static void Queue(Peer p)
        {
            if (!waiting.Contains(p)) waiting.Add(p);
            if (!PlayerSync.InGame) Log.Info("sauvegarde : " + p + " attend que l'hote soit en jeu");
        }

        public static void Update()
        {
            if (!Session.Active || !Session.IsHost) return;
            float now = Time.realtimeSinceStartup;
            if (!PlayerSync.InGame) { inGameSince = sendAt = -1; if (CarColor.NewGame) mustSave = true; return; }
            if (inGameSince < 0) inGameSince = now;
            if (waiting.Count == 0 || now - inGameSince < 15f) return;
            if (sendAt < 0)
            {
                if (mustSave || !File.Exists(Path.Combine(SaveDir, "savefile.txt")))
                {
                    mustSave = false;
                    Log.Info("sauvegarde : nouvelle partie pas encore sauvegardee, l'hote sauvegarde avant d'envoyer");
                    PlayMakerFSM.BroadcastEvent("SAVEGAME");
                    sendAt = now + 3f;
                    return;
                }
                sendAt = now;
            }
            if (now < sendAt) return;
            // Le jeu ecrit ses fichiers sur plusieurs images (jusqu'a 5 s) : on attend qu'ils ne bougent plus.
            DateTime newest = DateTime.MinValue;
            foreach (string f in SaveFiles()) { DateTime m = File.GetLastWriteTime(f); if (m > newest) newest = m; }
            if ((DateTime.Now - newest).TotalSeconds < 2) { sendAt = now + 0.5f; return; }
            sendAt = -1;
            foreach (Peer p in waiting) if (p.Accepted) SendTo(p);
            waiting.Clear();
        }

        public static string SaveDir { get { return Application.persistentDataPath; } }

        public static bool IsolatedProfile
        {
            get { return SaveDir.Replace('\\', '/').Contains("/MWCoop/profils/"); }
        }

        // Profil isole tout neuf : reprend les options du joueur (graphismes, sensibilite...) depuis
        // son vrai dossier de sauvegardes, en lecture seule.
        public static void CopyPlayerOptions()
        {
            if (!IsolatedProfile) return;
            string mine = Path.Combine(SaveDir, "options.txt");
            string real = Path.Combine(Path.Combine(Path.Combine(Environment.GetEnvironmentVariable("USERPROFILE") ?? "",
                @"AppData\LocalLow"), "Amistech"), @"My Winter Car\options.txt");
            try
            {
                if (File.Exists(mine) || !File.Exists(real)) return;
                Directory.CreateDirectory(SaveDir);
                File.Copy(real, mine);
                Log.Info("options du joueur reprises dans le profil");
            }
            catch (Exception e) { Log.Warn("options du joueur : " + e.Message); }
        }

        static List<string> SaveFiles()
        {
            var list = new List<string>();
            if (!Directory.Exists(SaveDir)) return list;
            foreach (string f in Directory.GetFiles(SaveDir, "*.txt"))
                if (Array.IndexOf(Skip, Path.GetFileName(f)) < 0) list.Add(f);
            return list;
        }

        public static void SendTo(Peer p)
        {
            List<string> list = SaveFiles();
            var data = new List<byte[]>();
            long total = 0;
            foreach (string f in list) { byte[] b = File.ReadAllBytes(f); data.Add(b); total += b.Length; }
            var w = new NetWriter(Msg.SaveBegin).U8(list.Count);
            for (int i = 0; i < list.Count; i++) w.Str(Path.GetFileName(list[i])).I32(data[i].Length);
            Session.T.SendReliable(p, w.ToArray());
            for (int i = 0; i < data.Count; i++)
                for (int off = 0; off < data[i].Length; off += Chunk)
                {
                    int n = Math.Min(Chunk, data[i].Length - off);
                    Session.T.SendReliable(p, new NetWriter(Msg.SaveChunk).U8(i).I32(off).Bytes(data[i], off, n).ToArray());
                }
            Session.T.SendReliable(p, new NetWriter(Msg.SaveEnd).U8(list.Count).ToArray());
            Log.Info("sauvegarde envoyee a " + p + " : " + list.Count + " fichiers, " + total / 1024 + " Ko");
        }

        public static void OnMessage(Msg type, NetReader r)
        {
            if (Session.IsHost) return;
            switch (type)
            {
                case Msg.SaveBegin:
                    int n = r.U8();
                    names = new List<string>();
                    files = new byte[n][];
                    expected = got = 0;
                    for (int i = 0; i < n; i++)
                    {
                        string name = Path.GetFileName(r.Str());
                        int size = r.I32();
                        names.Add(name);
                        files[i] = new byte[size];
                        expected += size;
                    }
                    Received = Done = false;
                    HostHasSave = n > 0;
                    Progress = 0;
                    Log.Info("reception de la sauvegarde de l'hote : " + n + " fichiers, " + expected / 1024 + " Ko");
                    break;
                case Msg.SaveChunk:
                    if (files == null) return;
                    int idx = r.U8(), off = r.I32();
                    byte[] part = r.Bytes();
                    Buffer.BlockCopy(part, 0, files[idx], off, part.Length);
                    got += part.Length;
                    Progress = expected > 0 ? (float)got / expected : 1f;
                    break;
                case Msg.SaveEnd:
                    if (files != null) Write();
                    Done = true;
                    break;
            }
        }

        static void Write()
        {
            if (!IsolatedProfile)
            {
                Log.Error("sauvegarde de l'hote NON ecrite : le jeu n'est pas lance dans un profil MWCoop (" + SaveDir + ")");
                Hud.Toast("Sauvegarde de l'hote ignoree : lancez le jeu depuis MWCoop.exe");
                files = null;
                return;
            }
            Directory.CreateDirectory(SaveDir);
            foreach (string f in SaveFiles()) File.Delete(f);
            for (int i = 0; i < names.Count; i++) File.WriteAllBytes(Path.Combine(SaveDir, names[i]), files[i]);
            Log.Info("sauvegarde de l'hote ecrite dans " + SaveDir);
            Hud.Toast("Sauvegarde de l'hote recue");
            files = null;
            Received = true;
            Progress = 1;
        }
    }
}
