using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace MWCoop.Net
{
    // L'hote envoie sa sauvegarde (fichiers .txt du dossier du jeu) a chaque invite qui arrive.
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

        public static string SaveDir { get { return Application.persistentDataPath; } }

        public static bool IsolatedProfile
        {
            get { return SaveDir.Replace('\\', '/').Contains("/MWCoop/profils/"); }
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
