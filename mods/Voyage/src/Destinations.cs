using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Voyage
{
    // Destinations (JD, 24/10) : circuits convertis par le convertisseur Voyage depuis l'Assetto Corsa du joueur, chacun dans
    // <jeu>\Mods\Assets\Voyage\destinations\<id>\ : destination.ini (fiche), circuit.vmesh (maillages, format du mod) et
    // textures\. Rien n'est livre avec le mod : chaque joueur convertit ses propres circuits. Lues au chargement (fiches
    // seulement) ; le circuit choisi est charge pendant le vol, a sa case hors de la carte du jeu, et decharge au retour.
    class Destination
    {
        public string Id, Name, Country, Author, Hash, Dir, Source;
        public int Price = 400;
        public Vector3 Arrival;   // (repere du circuit)
        public float Yaw, Radius = 1500f, MinY = -200f;
        public Vector3 Origin;
        public GameObject Root;

        public Vector3 ArrivalWorld { get { return Origin + Arrival + Vector3.up * 0.3f; } }
        public string Label { get { return Country.Length > 0 ? Name + " (" + Country + ")" : Name; } }
    }

    static class Destinations
    {
        public static readonly List<Destination> All = new List<Destination>();
        public static Destination Loaded;

        public static string Folder { get { return Path.Combine(Path.GetDirectoryName(Application.dataPath), Path.Combine("Mods", Path.Combine("Assets", Path.Combine("Voyage", "destinations")))); } }

        public static void Scan()
        {
            All.Clear();
            string root = Folder;
            if (!Directory.Exists(root)) { Journal.Info("destinations : aucune (" + root + " absent)"); return; }
            foreach (string dir in Directory.GetDirectories(root))
            {
                string ini = Path.Combine(dir, "destination.ini");
                if (!File.Exists(ini) || !File.Exists(Path.Combine(dir, "circuit.vmesh"))) continue;
                try
                {
                    var d = new Destination { Id = Path.GetFileName(dir), Dir = dir, Name = Path.GetFileName(dir), Country = "", Author = "", Hash = "", Source = "" };
                    foreach (string line in File.ReadAllLines(ini))
                    {
                        int eq = line.IndexOf('=');
                        if (eq <= 0 || line.StartsWith(";") || line.StartsWith("#")) continue;
                        string k = line.Substring(0, eq).Trim().ToLowerInvariant(), v = line.Substring(eq + 1).Trim();
                        var ci = System.Globalization.CultureInfo.InvariantCulture;
                        switch (k)
                        {
                            case "nom": d.Name = v; break;
                            case "pays": d.Country = v; break;
                            case "auteur": d.Author = v; break;
                            case "prix": int.TryParse(v, out d.Price); break;
                            case "empreinte": d.Hash = v; break;
                            case "source": d.Source = v; break;
                            case "cap": float.TryParse(v, System.Globalization.NumberStyles.Float, ci, out d.Yaw); break;
                            case "rayon": float.TryParse(v, System.Globalization.NumberStyles.Float, ci, out d.Radius); break;
                            case "basy": float.TryParse(v, System.Globalization.NumberStyles.Float, ci, out d.MinY); break;
                            case "arrivee":
                            {
                                string[] p = v.Split(',');
                                if (p.Length == 3) d.Arrival = new Vector3(float.Parse(p[0], ci), float.Parse(p[1], ci), float.Parse(p[2], ci));
                                break;
                            }
                        }
                    }
                    // case hors de la carte : sur un cercle de 25 km autour du jeu (16 places, ~10 km entre deux), d'apres
                    // l'identifiant -- la meme chez tous les joueurs qui ont ce circuit ; pas plus loin (precision des calculs)
                    int slot = (int)(Stable(d.Id) % 16u);
                    float ang = slot * Mathf.PI * 2f / 16f;
                    d.Origin = new Vector3(Mathf.Cos(ang) * 25000f, 0f, Mathf.Sin(ang) * 25000f);
                    All.Add(d);
                }
                catch (Exception e) { Journal.Info("destination " + dir + " : " + e.Message); }
            }
            All.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            Journal.Info("destinations : " + All.Count + (All.Count > 0 ? " (" + string.Join(", ", All.ConvertAll(d => d.Name).ToArray()) + ")" : ""));
        }

        static uint Stable(string s) { uint h = 2166136261; foreach (char c in s) { h ^= c; h *= 16777619; } return h; }

        public static Destination Find(string id) { return string.IsNullOrEmpty(id) ? null : All.Find(d => d.Id == id); }

        // charge le circuit (et son guichet du retour) ; vrai si c'est fait
        public static bool Load(Destination d)
        {
            if (d == null) return false;
            if (Loaded == d && d.Root != null) return true;
            Unload();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var root = new GameObject("VOYAGE_DESTINATION_" + d.Id);
            root.transform.position = d.Origin;
            Dictionary<string, Transform> parts;
            string dir = d.Dir;
            GameObject m = null;
            try { m = Modele.Load("circuit", root.transform, out parts, f => { string p = Path.Combine(dir, f.Replace('/', Path.DirectorySeparatorChar)); return File.Exists(p) ? File.ReadAllBytes(p) : null; }); }
            catch (Exception e) { Journal.Info("destination " + d.Id + " : " + e); }
            if (m == null) { UnityEngine.Object.Destroy(root); return false; }
            d.Root = root; Loaded = d;
            try { Lieux.ReturnDesk(d); } catch (Exception e) { Journal.Info("guichet du retour : " + e); }
            Journal.Info("destination " + d.Name + " chargee en " + sw.ElapsedMilliseconds + " ms (case " + d.Origin + ")");
            return true;
        }

        public static void Unload()
        {
            if (Loaded == null) return;
            Lieux.DropReturnDesk();
            if (Loaded.Root != null) UnityEngine.Object.Destroy(Loaded.Root);
            Journal.Info("destination " + Loaded.Name + " dechargee");
            Loaded.Root = null; Loaded = null;
            Modele.ForgetFolderTextures();
            Resources.UnloadUnusedAssets();
        }

        // le point est-il sur (ou au-dessus de) la destination d ?
        public static bool Near(Destination d, Vector3 p)
        {
            if (d == null) return false;
            Vector3 f = p - d.Origin; f.y = 0f;
            return f.magnitude < d.Radius + 400f;
        }
    }
}
