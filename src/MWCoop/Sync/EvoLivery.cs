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
    // Chaque addon a la sienne (JD 21/10, 205 T16) : %LOCALAPPDATA%\MWCoop\<dossier>-livree.jpg, [Coop] Livree_<dossier>=perso
    // (EvoLivree pour l'Evo III, comme avant) -- les memes noms que le lanceur (voiture.inc, AddonCfgKey).
    // Vitres (JD 04/11, voitures Rallyworld) : une seconde image, %LOCALAPPDATA%\MWCoop\<dossier>-vitres.png (PNG avec alpha,
    // [Coop] Vitres_<dossier>=perso), posee sur la couche de details des vitres (VazCar.SetWindows) ; son empreinte commence par
    // « v » (cache livrees<empreinte>.png) et suit celle de la livree dans la table des voitures : « <livree>|<vitres> ».
    public static class EvoLivery
    {
        const int Chunk = 900;
        static readonly Dictionary<string, Texture2D> loaded = new Dictionary<string, Texture2D>();
        static readonly Dictionary<string, float> asked = new Dictionary<string, float>(), sentAt = new Dictionary<string, float>();
        class Parts { public byte[][] P; public int Got; }
        static readonly Dictionary<string, Parts> incoming = new Dictionary<string, Parts>();
        class Mine { public string Hash = "", VHash = ""; public System.DateTime Time, VTime; public bool Chosen, VChosen; public float Next; }
        static readonly Dictionary<string, Mine> mine = new Dictionary<string, Mine>();
        static Mine Of(VazCar.Model m) { Mine x; if (!mine.TryGetValue(m.Folder, out x)) mine[m.Folder] = x = new Mine(); return x; }
        static bool IsMine(string h) { foreach (Mine x in mine.Values) if (x.Hash == h || x.VHash == h) return true; return false; }
        public static int Generation;   // change quand une image arrive (Vaz repose les livrees)

        // ([Test] RacineVisages : un dossier par instance d'essai, comme les visages perso)
        static string Root { get { string o = Config.Get("Test", "RacineVisages", ""); return o.Length > 0 ? o : Path.Combine(System.Environment.GetEnvironmentVariable("LOCALAPPDATA") ?? ".", "MWCoop"); } }
        static string MyFile(VazCar.Model m) { return Path.Combine(Root, m.Folder + "-livree.jpg"); }
        static string CfgKey(VazCar.Model m) { return m.Folder == "evo3" ? "EvoLivree" : "Livree_" + m.Folder; }
        static string CacheDir { get { return Path.Combine(Root, "livrees"); } }
        static string CacheFile(string h) { return Path.Combine(CacheDir, h + (IsWin(h) ? ".png" : ".jpg")); }
        static string MyWinFile(VazCar.Model m) { return Path.Combine(Root, m.Folder + "-vitres.png"); }
        static bool IsWin(string h) { return h.Length > 0 && h[0] == 'v'; }

        // « <livree>|<vitres> » (table des voitures) -> les deux empreintes ; sans « | » : la livree seule (comme avant le 04/11)
        public static void Split(string s, out string liv, out string win)
        {
            s = s ?? ""; int i = s.IndexOf('|');
            liv = i < 0 ? s : s.Substring(0, i); win = i < 0 ? "" : s.Substring(i + 1);
        }
        static string Join(string liv, string win) { return win.Length > 0 ? liv + "|" + win : liv; }

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

        // Empreinte de la livree du joueur d'ici pour ce modele ("" : couleur du joueur ; la VAZ n'en a pas). Relue quand le
        // fichier ou le choix changent.
        public static string MyHash(int model)
        {
            VazCar.Model m = VazCar.ModelOf(model);   // (la VAZ aussi, depuis le 21/10 : Livree_vaz, vaz-livree.jpg)
            Poll(m); return Join(Of(m).Hash, Of(m).VHash);
        }

        // Au plus toutes les 2 s : choix ([Coop] EvoLivree / Livree_<dossier>) et date du fichier ; vrai si l'empreinte a change.
        public static bool Poll(int model) { return Poll(VazCar.ModelOf(model)); }
        static bool Poll(VazCar.Model m)
        {
            Mine me = Of(m);
            float now = Time.realtimeSinceStartup;
            if (now < me.Next) return false;
            me.Next = now + 2f;
            bool chosen = Config.Get("Coop", CfgKey(m), "") == "perso", vchosen = Config.Get("Coop", "Vitres_" + m.Folder, "") == "perso";
            System.DateTime t = Stamp(chosen, MyFile(m)), vt = Stamp(vchosen, MyWinFile(m));
            if (chosen == me.Chosen && t == me.Time && vchosen == me.VChosen && vt == me.VTime) return false;
            me.Chosen = chosen; me.Time = t; me.VChosen = vchosen; me.VTime = vt;
            string was = me.Hash, vwas = me.VHash;
            me.Hash = chosen && t != System.DateTime.MinValue ? Store(MyFile(m), "", 2200000) : "";
            me.VHash = vchosen && vt != System.DateTime.MinValue ? Store(MyWinFile(m), "v", 1200000) : "";
            if (me.Hash != was) Log.Info("livree " + m.Folder + " : " + (me.Hash.Length > 0 ? me.Hash : "aucune (couleur du joueur)"));
            if (me.VHash != vwas) Log.Info("vitres " + m.Folder + " : " + (me.VHash.Length > 0 ? me.VHash : "d'origine"));
            return me.Hash != was || me.VHash != vwas;
        }
        static System.DateTime Stamp(bool chosen, string file)
        {
            try { if (chosen && File.Exists(file)) return File.GetLastWriteTimeUtc(file); } catch { }
            return System.DateTime.MinValue;
        }
        // image du joueur d'ici -> cache (livrees\<empreinte>) ; empreinte, ou "" si refusee
        static string Store(string file, string prefix, int max)
        {
            try
            {
                byte[] b = File.ReadAllBytes(file);
                if (b.Length < 100 || b.Length > max) { Log.Warn("livree evo : image refusee (" + Path.GetFileName(file) + ", " + b.Length + " octets)"); return ""; }
                string h = prefix + Hash(b);
                Directory.CreateDirectory(CacheDir);
                if (!File.Exists(CacheFile(h))) File.WriteAllBytes(CacheFile(h), b);
                return h;
            }
            catch (System.Exception e) { Log.Warn("livree evo : " + e.Message); return ""; }
        }

        // Texture de la livree h, ou null (pas encore recue : demandee ; ou celles des autres coupees).
        public static Texture2D Get(string h)
        {
            if (string.IsNullOrEmpty(h)) return null;
            Texture2D t;
            if (loaded.TryGetValue(h, out t) && t != null) return t;
            if (Config.GetInt("Coop", "VoirLivrees", 1) == 0 && !IsMine(h)) return null;
            string f = CacheFile(h);
            if (!File.Exists(f)) { Ask(h); return null; }
            try
            {
                t = new Texture2D(2, 2, IsWin(h) ? TextureFormat.RGBA32 : TextureFormat.RGB24, true) { name = "livree_" + h, hideFlags = HideFlags.HideAndDontSave };
                if (!t.LoadImage(File.ReadAllBytes(f))) { Object.Destroy(t); Log.Warn("livree evo : " + h + " illisible"); return null; }
                if (IsWin(h)) DetailsOnly(t);
                t.wrapMode = TextureWrapMode.Repeat; t.anisoLevel = 4;
                loaded[h] = t;
                Log.Info("livree evo : " + h + " chargee (" + t.width + "x" + t.height + ")");
            }
            catch (System.Exception e) { Log.Warn("livree evo : " + e.Message); t = null; }
            return t;
        }

        // (vitres : couche de details seule, comme le convertisseur -- le verre teinte est dessous, pour la pluie ; une vitre de skin
        // importee telle quelle, teinte comprise, doublerait la teinte : alpha sous 25 % mis a 0)
        static void DetailsOnly(Texture2D t)
        {
            Color32[] px = t.GetPixels32();
            for (int i = 0; i < px.Length; i++) if (px[i].a < 64) px[i].a = 0;
            t.SetPixels32(px); t.Apply(true);
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
            if (!Session.IsHost && !IsMine(h)) return;   // (celui qui l'a choisie repond, et l'hote qui l'a en cache)
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
            if ((IsWin(h) ? "v" : "") + Hash(b) != h) { Log.Warn("livree evo : " + h + " recue abimee"); return; }
            try { Directory.CreateDirectory(CacheDir); File.WriteAllBytes(CacheFile(h), b); } catch (System.Exception e) { Log.Warn("livree evo : " + e.Message); return; }
            Generation++;
            Log.Info("livree evo : " + h + " recue de #" + who + " (" + b.Length + " octets)");
        }
    }
}
