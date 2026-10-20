using System.Collections.Generic;
using System.IO;
using MWCoop.Net;
using UnityEngine;

namespace MWCoop
{
    // Livree de l'Evo III (extension privee, JD 20/10) : sans livree, l'Evo est comme la VAZ (couleur du joueur + numero sur
    // les portieres) ; le joueur peut choisir sa propre image au lanceur (section Voitures : peinte sur le gabarit de
    // l'extension, MWCoop/evo3/gabarit-livree.png ; numero hachure en rouge). Le lanceur la range dans
    // %LOCALAPPDATA%\MWCoop\evo3-livree.jpg et met [Coop] EvoLivree=perso. Son empreinte part avec la table des voitures (Vaz,
    // V_SET / V_LIV) ; chez les autres, l'image est demandee (@livree?) a qui l'a, recue par morceaux (@livreep, fiables, 900
    // octets, comme les visages perso : CustomFace), gardee en cache (%LOCALAPPDATA%\MWCoop\livrees\<empreinte>.jpg) et posee
    // sur les matieres de peinture de la voiture (VazCar.SetLivery). [Coop] VoirLivrees=0 : celles des autres non.
    public static class EvoLivery
    {
        const int Chunk = 900;
        static readonly Dictionary<string, Texture2D> loaded = new Dictionary<string, Texture2D>();
        static readonly Dictionary<string, float> asked = new Dictionary<string, float>(), sentAt = new Dictionary<string, float>();
        class Parts { public byte[][] P; public int Got; }
        static readonly Dictionary<string, Parts> incoming = new Dictionary<string, Parts>();
        static string myHash = "";
        static System.DateTime myTime;
        static bool myChosen;
        static float nextPoll;
        public static int Generation;   // change quand une image arrive (Vaz repose les livrees)

        // ([Test] RacineVisages : un dossier par instance d'essai, comme les visages perso)
        static string Root { get { string o = Config.Get("Test", "RacineVisages", ""); return o.Length > 0 ? o : Path.Combine(System.Environment.GetEnvironmentVariable("LOCALAPPDATA") ?? ".", "MWCoop"); } }
        public static string MyFile { get { return Path.Combine(Root, "evo3-livree.jpg"); } }
        static string CacheDir { get { return Path.Combine(Root, "livrees"); } }
        static string CacheFile(string h) { return Path.Combine(CacheDir, h + ".jpg"); }

        static string Hash(byte[] b)
        {
            using (var md5 = System.Security.Cryptography.MD5.Create())
            {
                byte[] h = md5.ComputeHash(b);
                var sb = new System.Text.StringBuilder();
                for (int i = 0; i < 6; i++) sb.Append(h[i].ToString("x2"));
                return sb.ToString();
            }
        }

        // Empreinte de la livree du joueur d'ici ("" : couleur du joueur). Relue quand le fichier ou le choix changent.
        public static string MyHash { get { Poll(); return myHash; } }

        // Au plus toutes les 2 s : choix ([Coop] EvoLivree) et date du fichier ; vrai si l'empreinte a change.
        public static bool Poll()
        {
            float now = Time.realtimeSinceStartup;
            if (now < nextPoll) return false;
            nextPoll = now + 2f;
            bool chosen = Config.Get("Coop", "EvoLivree", "") == "perso";
            System.DateTime t = System.DateTime.MinValue;
            try { if (chosen && File.Exists(MyFile)) t = File.GetLastWriteTimeUtc(MyFile); } catch { }
            if (chosen == myChosen && t == myTime) return false;
            myChosen = chosen; myTime = t;
            string was = myHash;
            myHash = "";
            if (chosen && t != System.DateTime.MinValue)
                try
                {
                    byte[] b = File.ReadAllBytes(MyFile);
                    if (b.Length < 100 || b.Length > 2200000) Log.Warn("livree evo : image refusee (" + b.Length + " octets)");
                    else
                    {
                        string h = Hash(b);
                        Directory.CreateDirectory(CacheDir);
                        if (!File.Exists(CacheFile(h))) File.WriteAllBytes(CacheFile(h), b);
                        myHash = h;
                    }
                }
                catch (System.Exception e) { Log.Warn("livree evo : " + e.Message); }
            if (myHash != was) Log.Info("livree evo : " + (myHash.Length > 0 ? myHash : "aucune (couleur du joueur)"));
            return myHash != was;
        }

        // Texture de la livree h, ou null (pas encore recue : demandee ; ou celles des autres coupees).
        public static Texture2D Get(string h)
        {
            if (string.IsNullOrEmpty(h)) return null;
            Texture2D t;
            if (loaded.TryGetValue(h, out t) && t != null) return t;
            if (Config.GetInt("Coop", "VoirLivrees", 1) == 0 && h != myHash) return null;
            string f = CacheFile(h);
            if (!File.Exists(f)) { Ask(h); return null; }
            try
            {
                t = new Texture2D(2, 2, TextureFormat.RGB24, true) { name = "livree_" + h, hideFlags = HideFlags.HideAndDontSave };
                if (!t.LoadImage(File.ReadAllBytes(f))) { Object.Destroy(t); Log.Warn("livree evo : " + h + " illisible"); return null; }
                t.wrapMode = TextureWrapMode.Repeat; t.anisoLevel = 4;
                loaded[h] = t;
                Log.Info("livree evo : " + h + " chargee (" + t.width + "x" + t.height + ")");
            }
            catch (System.Exception e) { Log.Warn("livree evo : " + e.Message); t = null; }
            return t;
        }

        static void Ask(string h)
        {
            float t;
            if (!Session.Active || (asked.TryGetValue(h, out t) && Time.realtimeSinceStartup - t < 30f)) return;
            asked[h] = Time.realtimeSinceStartup;
            Session.SendAll(new NetWriter(Msg.Job).U8(Session.LocalId).Str("@livree?").Str(h), true);
            Log.Info("livree evo : " + h + " demandee");
        }

        public static void OnAsk(int who, string h)
        {
            float t;
            string f = CacheFile(h);
            if (!File.Exists(f) || (sentAt.TryGetValue(h, out t) && Time.realtimeSinceStartup - t < 20f)) return;
            if (!Session.IsHost && h != myHash) return;   // (celui qui l'a choisie repond, et l'hote qui l'a en cache)
            sentAt[h] = Time.realtimeSinceStartup;
            byte[] b = File.ReadAllBytes(f);
            int n = (b.Length + Chunk - 1) / Chunk;
            for (int i = 0; i < n; i++)
                Session.SendAll(new NetWriter(Msg.Job).U8(Session.LocalId).Str("@livreep").Str(h).U16(i).U16(n).Bytes(b, i * Chunk, Mathf.Min(Chunk, b.Length - i * Chunk)), true);
            Log.Info("livree evo : " + h + " envoyee a la demande de #" + who + " (" + n + " morceaux)");
        }

        public static void OnPart(int who, string h, int i, int n, byte[] data)
        {
            if (File.Exists(CacheFile(h)) || n <= 0 || n > 2500 || i >= n) return;
            Parts p;
            if (!incoming.TryGetValue(h, out p) || p.P.Length != n) incoming[h] = p = new Parts { P = new byte[n][] };
            if (p.P[i] != null) return;
            p.P[i] = data; p.Got++;
            if (p.Got < n) return;
            incoming.Remove(h);
            var all = new List<byte>();
            foreach (byte[] x in p.P) all.AddRange(x);
            byte[] b = all.ToArray();
            if (Hash(b) != h) { Log.Warn("livree evo : " + h + " recue abimee"); return; }
            try { Directory.CreateDirectory(CacheDir); File.WriteAllBytes(CacheFile(h), b); } catch (System.Exception e) { Log.Warn("livree evo : " + e.Message); return; }
            Generation++;
            Log.Info("livree evo : " + h + " recue de #" + who + " (" + b.Length + " octets)");
        }
    }
}
