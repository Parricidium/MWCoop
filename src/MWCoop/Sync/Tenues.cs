using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace MWCoop
{
    // Tenues offertes par des joueurs (credites dans l'onglet CREDITS du lanceur). Pack de Dom (08/10/2026) :
    // MWCoop\tenues\dom\dom_haut_NN.jpg, dom_pantalon_NN.jpg, dom_visage_NN.jpg, faites sur le patron des vetements des
    // PNJ (512x512, visages 512x256 ; matieres Legacy Shaders/Diffuse, opaques). Telechargees une fois par le lanceur
    // (assets/tenues du depot), a part des mises a jour du mod.
    // Jamais a la place des textures du jeu : chaque tenue est une copie de la matiere d'un vetement du jeu, sous son propre
    // nom, creee seulement quand quelqu'un la porte ou la regarde (pas 157 textures en memoire d'un coup). Absente chez un
    // joueur (pack pas encore recu) : la tenue d'origine, comme pour un nom inconnu.
    public static class Tenues
    {
        public const int Shirt = 0, Pants = 1, Face = 2;
        static Dictionary<string, string> files;
        static readonly Dictionary<string, Material> made = new Dictionary<string, Material>();

        static string Dir { get { return Path.Combine(System.Environment.GetEnvironmentVariable("MWCOOP_DIR") ?? "MWCoop", "tenues"); } }

        static void Load()
        {
            if (files != null) return;
            files = new Dictionary<string, string>();
            try
            {
                if (Directory.Exists(Dir))
                    foreach (string d in Directory.GetDirectories(Dir))
                        foreach (string f in Directory.GetFiles(d))
                        {
                            string ext = Path.GetExtension(f).ToLowerInvariant();
                            if (ext != ".jpg" && ext != ".png") continue;
                            string n = Path.GetFileNameWithoutExtension(f);
                            if (KindOf(n) >= 0 && !files.ContainsKey(n)) files[n] = f;
                        }
            }
            catch (System.Exception e) { Log.Warn("tenues offertes : " + e.Message); }
            Log.Info("tenues offertes : " + List(Shirt).Count + " hauts, " + List(Pants).Count + " pantalons, " + List(Face).Count + " visages (" + Dir + ")");
        }

        public static int KindOf(string n)
        {
            if (string.IsNullOrEmpty(n)) return -1;
            if (n.Contains("_haut_")) return Shirt;
            if (n.Contains("_pantalon_")) return Pants;
            if (n.Contains("_visage_")) return Face;
            return -1;
        }

        public static bool Has(string n) { Load(); return !string.IsNullOrEmpty(n) && files.ContainsKey(n); }

        public static List<string> List(int kind)
        {
            Load();
            var l = new List<string>();
            foreach (string n in files.Keys) if (KindOf(n) == kind) l.Add(n);
            l.Sort(System.StringComparer.Ordinal);
            return l;
        }

        // "dom_haut_05" -> "Dom 5" (l'auteur, puis le numero)
        public static string Label(string n)
        {
            int a = n.IndexOf('_'), b = n.LastIndexOf('_');
            if (a <= 0 || b <= a) return n;
            string who = n.Substring(0, a);
            return char.ToUpperInvariant(who[0]) + who.Substring(1) + " " + n.Substring(b + 1).TrimStart('0');
        }

        // Matiere de la tenue 'n' (null : pas une tenue offerte, ou fichier illisible).
        public static Material Make(string n)
        {
            Material m;
            if (made.TryGetValue(n, out m)) return m;
            if (!Has(n)) return null;
            int k = KindOf(n);
            Material tpl = Avatar.FindMaterial(k == Shirt ? "char_shirt01" : k == Pants ? "char_pants01" : "char_face01");
            if (tpl == null) return null;   // (le jeu pas encore charge : on reessaiera)
            try
            {
                var tex = new Texture2D(2, 2, TextureFormat.RGB24, true);
                if (tex.LoadImage(File.ReadAllBytes(files[n])))
                {
                    tex.name = n;
                    m = new Material(tpl);
                    m.name = n;
                    m.mainTexture = tex;
                }
                else Object.Destroy(tex);
            }
            catch (System.Exception e) { Log.Warn("tenues offertes : " + n + " : " + e.Message); }
            made[n] = m;
            if (m != null && made.Count % 20 == 1) Log.Info("tenues offertes : " + made.Count + " matiere(s) creee(s) (" + n + ")");
            return m;
        }
    }
}
