using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Steamworks;

namespace MWCoop.Net
{
    // Mods de l'hote pour les invites Steam qui arrivent pendant la partie (demande de JD, 13/10 : « ceux qui rejoignent
    // pendant que la partie est lancee : les mods, puis ils entrent »). Le lanceur de l'hote a rendu Steam au jeu en le
    // lancant ; c'est donc le jeu qui repond, sur le canal des mods du lanceur (5), dans le meme format (modsync.inc) :
    //  - M_MODSREQ (23) : la liste telle que le lanceur l'avait faite (messages M_MODS / M_MODSX), sinon M_LATE (26) 0 :
    //    rien a envoyer -- l'invite rejoint en jeu sans attendre ;
    //  - M_MODGET (21 : u32 + u32 empreinte, u16 fichier, u32 position) -> M_MODDATA (22 : u16 fichier, u32 position,
    //    48 Ko au plus).
    // La liste : MWCoop\offre-mods.bin, ecrit par le lanceur au lancement de l'hote (SyncWriteLateOffer).
    public static class ModServe
    {
        const int Channel = 5, Chunk = 48 * 1024;
        const byte M_MODGET = 21, M_MODDATA = 22, M_MODSREQ = 23, M_LATE = 26;
        static bool loaded;
        static readonly List<byte[]> blobs = new List<byte[]>();
        static ulong key;
        static readonly List<KeyValuePair<string, uint>> files = new List<KeyValuePair<string, uint>>();
        static byte[] buf = new byte[4096];
        static int served, asked;

        static void Load()
        {
            loaded = true;
            blobs.Clear(); files.Clear(); key = 0;
            string f = Path.Combine(Path.GetDirectoryName(Config.IniPath) ?? "", "offre-mods.bin");
            try
            {
                if (!File.Exists(f)) { Log.Info("mods : pas de liste pour les invites en retard (" + f + ")"); return; }
                byte[] d = File.ReadAllBytes(f);
                if (d.Length < 8 || Encoding.ASCII.GetString(d, 0, 4) != "MWOF") return;
                int p = 4;
                uint n = BitConverter.ToUInt32(d, p); p += 4;
                for (uint i = 0; i < n; i++)
                {
                    int len = (int)BitConverter.ToUInt32(d, p); p += 4;
                    var b = new byte[len]; Buffer.BlockCopy(d, p, b, 0, len); p += len;
                    blobs.Add(b);
                }
                key = BitConverter.ToUInt32(d, p) | ((ulong)BitConverter.ToUInt32(d, p + 4) << 32); p += 8;
                int nf = BitConverter.ToUInt16(d, p); p += 2;
                for (int i = 0; i < nf; i++)
                {
                    int len = BitConverter.ToUInt16(d, p); p += 2;
                    string path = Encoding.UTF8.GetString(d, p, len); p += len;
                    uint size = BitConverter.ToUInt32(d, p); p += 4;
                    files.Add(new KeyValuePair<string, uint>(path, size));
                }
                Log.Info("mods : liste pour les invites en retard chargee (" + files.Count + " fichiers)");
            }
            catch (Exception e) { blobs.Clear(); files.Clear(); Log.Warn("mods : liste pour les invites en retard illisible : " + e.Message); }
        }

        // SteamNet.Update, hote Steam seulement
        public static void Update()
        {
            uint size;
            for (int k = 0; k < 32 && SteamNetworking.IsP2PPacketAvailable(out size, Channel); k++)
            {
                if (size > buf.Length) buf = new byte[size];
                uint got; CSteamID who;
                if (!SteamNetworking.ReadP2PPacket(buf, size, out got, out who, Channel) || got == 0) continue;
                if (!loaded) Load();
                if (buf[0] == M_MODSREQ)
                {
                    if (asked++ < 20) Log.Info("mods : " + SteamNet.Name(who.m_SteamID) + " demande la liste (partie en cours) : " + (blobs.Count > 0 ? files.Count + " fichiers" : "rien"));
                    if (blobs.Count == 0) Send(who, new byte[] { M_LATE, 0 }, 2);
                    else foreach (byte[] b in blobs) Send(who, b, b.Length);
                }
                else if (buf[0] == M_MODGET && got >= 15) Serve(who);
            }
        }

        static void Serve(CSteamID who)
        {
            ulong k = BitConverter.ToUInt32(buf, 1) | ((ulong)BitConverter.ToUInt32(buf, 5) << 32);
            int idx = BitConverter.ToUInt16(buf, 9);
            uint off = BitConverter.ToUInt32(buf, 11);
            if (k != key || idx >= files.Count || off > files[idx].Value) return;
            int n = (int)Math.Min((uint)Chunk, files[idx].Value - off);
            var o = new byte[7 + n];
            o[0] = M_MODDATA;
            BitConverter.GetBytes((ushort)idx).CopyTo(o, 1);
            BitConverter.GetBytes(off).CopyTo(o, 3);
            if (n > 0)
                try
                {
                    using (var fs = new FileStream(files[idx].Key, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                    {
                        fs.Seek(off, SeekOrigin.Begin);
                        int at = 0;
                        while (at < n) { int r = fs.Read(o, 7 + at, n - at); if (r <= 0) break; at += r; }
                        if (at < n) return;
                    }
                }
                catch (Exception e) { Log.Warn("mods : " + files[idx].Key + " illisible : " + e.Message); return; }
            Send(who, o, o.Length);
            if (served++ == 0) Log.Info("mods : premier morceau envoye a " + SteamNet.Name(who.m_SteamID) + " (partie en cours)");
        }

        // [Test] Autotest=modserve : la liste lue, et le premier morceau de chaque fichier compare au fichier lui-meme.
        static bool tested;
        public static void Test(string mode, float t)
        {
            if (mode != "modserve" || tested || t < 10f) return;
            tested = true;
            Load();
            int ok = 0, bad = 0;
            for (int i = 0; i < files.Count; i++)
            {
                buf = new byte[15]; buf[0] = M_MODGET;
                BitConverter.GetBytes((uint)key).CopyTo(buf, 1); BitConverter.GetBytes((uint)(key >> 32)).CopyTo(buf, 5);
                BitConverter.GetBytes((ushort)i).CopyTo(buf, 9); BitConverter.GetBytes(0u).CopyTo(buf, 11);
                lastOut = null;
                Serve(default(CSteamID));
                byte[] want = File.Exists(files[i].Key) ? File.ReadAllBytes(files[i].Key) : null;
                int n = want == null ? -1 : Math.Min(Chunk, want.Length);
                bool same = lastOut != null && want != null && lastOut.Length == 7 + n;
                for (int k = 0; same && k < n; k++) same = lastOut[7 + k] == want[k];
                if (same) ok++; else { bad++; Log.Info("autotest : modserve, fichier " + i + " different : " + files[i].Key); }
            }
            Log.Info("autotest : modserve, " + blobs.Count + " message(s) de liste, " + files.Count + " fichiers, morceaux identiques " + ok + ", differents " + bad);
        }
        static byte[] lastOut;

        static void Send(CSteamID to, byte[] d, int len)
        {
            lastOut = d;
            if (to.m_SteamID == 0) return;   // (essai)
            try { SteamNetworking.SendP2PPacket(to, d, (uint)len, EP2PSend.k_EP2PSendReliable, Channel); } catch { }
        }
    }
}
