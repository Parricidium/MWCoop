using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace MWCoop
{
    // Cartes de l'editeur (V1, 20/10) : paquets .mwcmap (un zip) de <MWCoop>\cartes, ajoutes a la carte du jeu au chargement
    // de GAME. Un paquet ne contient que ce que le joueur a fait (ses modeles 3D, leurs textures) ; les copies d'objets du jeu
    // ne sont que des chemins (MapExport.UniquePath), recrees ici depuis le jeu de chacun -- objets regroupes compris
    // (SceneBatches.cs). carte.txt (ecrit par l'editeur, ui/edition.js), une ligne par element, champs separes par des
    // tabulations, nombres a point :
    //   MWCMAP 1 | jeu msc|mwc | nom .. | auteur .. | version ..
    //   modele <id> <modeles/id.bin> | matiere <id> <i> <r g b a> <textures/x.png|-> <decoupe 0|1>
    //   objet jeu <chemin> <px py pz> <qx qy qz qw> <sx sy sz> <aucune|boite|forme> <nom> [<cx cy cz>]   (c : centre d'un objet
    //     regroupe dans le repere de son objet -- souvent le centre de la carte --, retire de ses sommets comme dans l'editeur)
    //   objet modele <id,id,...> <pos> <rot> <ech> <collisions> <nom>   (un modele de plus de 60000 sommets en plusieurs)
    //   masque <chemin>
    //   (MWCMAP 2, 20/10) habit <sous-maillage|*> <texture> [<ru rv>] : texture d'une copie (ligne objet juste avant) ; texture
    //     d'un habit ou d'une matiere : image du paquet, tex:<nom> (texture du jeu), mat:<nom> (matiere du jeu : routes, eau),
    //     eau: (eau simple). Routes et eau de l'editeur : des modeles comme les autres (maillages cuits par l'editeur).
    // Maillages des modeles : comme maillages.bin de l'export (sommets, drapeaux, sous-maillages, positions, normales, uv,
    // indices), deja dans le repere d'Unity.
    public static class MapPacks
    {
        static GameObject root;
        static float loadAt = -1;
        public static int Objects, Hidden, Packs;
        public static string Dir { get { return Path.Combine(Environment.GetEnvironmentVariable("MWCOOP_DIR") ?? "MWCoop", "cartes"); } }

        // Dossier lu : un invite ne charge que les cartes recues de l'hote (lanceur : categorie « Cartes de l'editeur » du
        // contenu envoye, ContenuDossier\cartes), jamais les siennes -- meme carte et memes collisions pour tout le monde ;
        // rien si l'hote n'en envoie pas.
        static string LoadDir(bool log)
        {
            if (Config.Get("Coop", "Mode", "solo").ToLowerInvariant() != "invite") return Dir;
            string recu = Config.Get("Coop", "ContenuDossier", "");
            bool sent = false;
            foreach (string r in Config.Get("Coop", "ContenuRecu", "").Split(',')) if (r.Trim().TrimEnd('/', '\\').Equals("cartes", StringComparison.OrdinalIgnoreCase)) sent = true;
            if (!sent || recu.Length == 0) { if (log) Log.Info("cartes : invite, aucune carte de l'hote"); return null; }
            return Path.Combine(recu, "cartes");
        }

        static IEnumerator<bool> job;

        public static void OnLevelLoaded()
        {
            root = null; Objects = Hidden = Packs = 0; job = null; lamps.Clear(); suns = null; hiddenUnits.Clear(); waters.Clear();
            loadAt = Game.LevelName == "GAME" && !MapExport.Active ? Time.realtimeSinceStartup + Config.GetFloat("Test", "CartesDelai", 6f) : -1;
            if (loadAt < 0) return;
            string d = LoadDir(false);
            if (d != null && Directory.Exists(d) && Directory.GetFiles(d, "*.mwcmap").Length > 0) SceneBatches.Prefetch();   // (copies d'objets regroupes)
        }

        // Chargement reparti sur plusieurs images (CartesBudget ms par image, 4 par defaut) : plus d'a-coup a l'arrivee des
        // cartes (0,36 s d'un bloc avant le 20/10).
        public static void Update()
        {
            NightLamps();
            WaterColors();
            KeepHidden();
            if (job != null)
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                float budget = Config.GetFloat("Test", "CartesBudget", 4f);
                try { while (sw.Elapsed.TotalMilliseconds < budget) if (!job.MoveNext()) { job = null; break; } }
                catch (Exception e) { Log.Warn("cartes : " + e.Message); job = null; }
                return;
            }
            if (loadAt < 0 || Time.realtimeSinceStartup < loadAt) return;
            loadAt = -1;
            job = LoadAll();
        }

        static IEnumerator<bool> LoadAll()
        {
            string dir = LoadDir(true);
            if (dir == null || !Directory.Exists(dir)) { SceneBatches.Close(); yield break; }
            string[] files = Directory.GetFiles(dir, "*.mwcmap");
            if (files.Length == 0) { SceneBatches.Close(); yield break; }
            var sw = System.Diagnostics.Stopwatch.StartNew();
            int frames = 0;
            root = new GameObject("MWCoop-Cartes");
            roots = null; meshCache.Clear(); plantCache.Clear(); gameTex = null; gameMat = null; allTex = false; packTex.Clear();
            Array.Sort(files, StringComparer.OrdinalIgnoreCase);
            foreach (string f in files)
            {
                Pack p = Begin(f);
                if (p == null) continue;
                foreach (string l in p.Lines) { Line(p, l); frames++; yield return true; }
                foreach (string c in p.ReliefTargets) { ApplyRelief(p, c); frames++; yield return true; }
                End(p);
                Packs++;
            }
            SceneBatches.Close();
            if (Config.Get("Test", "CartesPhoto", "").Length > 0)   // (photo d'essai : les scripts ajoutes -- eau du lac -- demarrent a l'image suivante)
            {
                float t = Time.realtimeSinceStartup + Config.GetFloat("Test", "CartesPhotoDelai", 2f);
                while (Time.realtimeSinceStartup < t) yield return true;
            }
            TestPhoto();
            ProbeWater();
            ProbeUnits();
            ProbeHeights();
            Log.Info("cartes : " + Packs + "/" + files.Length + " carte(s), " + Objects + " objet(s) ajoute(s), " + Hidden + " objet(s) du jeu masque(s) en " + sw.ElapsedMilliseconds + " ms (" + frames + " etapes)");
        }

        // ---------------------------------------------------------------- relief sculpte
        // relief-cible <chemin> (un objet du sol touche) ; relief <monter|creuser|aplanir|adoucir> <x> <z> <rayon> <force> <y> :
        // les traits du pinceau de l'editeur, rejoues dans l'ordre sur une copie du maillage du sol (affichage et collision) --
        // meme calcul que l'editeur (edition.js reliefStroke).
        static void ApplyRelief(Pack p, string path)
        {
            try
            {
                Transform t = Resolve(path);
                MeshFilter mf = t != null ? t.GetComponent<MeshFilter>() : null;
                if (mf == null || mf.sharedMesh == null) { if (p.Missing++ < 10) Log.Warn("cartes : " + p.Name + " : sol « " + path + " » introuvable"); return; }
                Mesh src = mf.sharedMesh, m = Sculpt(src, t, p.Strokes);
                if (m == null) return;
                mf.sharedMesh = m;
                MeshCollider mc = t.GetComponent<MeshCollider>();
                if (mc != null && mc.sharedMesh != null)
                {
                    Mesh cm = mc.sharedMesh == src ? m : Sculpt(mc.sharedMesh, t, p.Strokes);
                    if (cm != null) { mc.sharedMesh = null; mc.sharedMesh = cm; }
                }
                if (Config.Testing) Log.Info("cartes : " + p.Name + " : sol " + path + " sculpte (" + p.Strokes.Count + " trait(s), " + m.vertexCount + " sommets)");
            }
            catch (Exception e) { Log.Warn("cartes : " + p.Name + " : relief " + path + " : " + e.Message); }
        }
        static Mesh Sculpt(Mesh src, Transform t, List<string[]> strokes)
        {
            Vector3[] v = src.vertices;
            if (v == null || v.Length == 0) return null;
            int n = v.Length;
            var xs = new double[n]; var ys = new double[n]; var zs = new double[n];
            for (int i = 0; i < n; i++) { Vector3 w = t.TransformPoint(v[i]); xs[i] = w.x; ys[i] = w.y; zs[i] = w.z; }
            foreach (string[] f in strokes)
                Stroke(xs, ys, zs, f[1], double.Parse(f[2], CultureInfo.InvariantCulture), double.Parse(f[3], CultureInfo.InvariantCulture),
                       double.Parse(f[4], CultureInfo.InvariantCulture), double.Parse(f[5], CultureInfo.InvariantCulture), double.Parse(f[6], CultureInfo.InvariantCulture));
            var nv = new Vector3[n];
            for (int i = 0; i < n; i++) nv[i] = t.InverseTransformPoint(new Vector3((float)xs[i], (float)ys[i], (float)zs[i]));
            Mesh m = UnityEngine.Object.Instantiate(src);
            m.name = src.name + " (sculpte)";
            m.vertices = nv;
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }
        static void Stroke(double[] xs, double[] ys, double[] zs, string mode, double x, double z, double r, double f, double y)
        {
            double r2 = r * r, k = Math.Min(1.0, f / 2.0), mean = 0, ws = 0;
            if (mode == "adoucir")
                for (int i = 0; i < xs.Length; i++) { double d2 = (xs[i] - x) * (xs[i] - x) + (zs[i] - z) * (zs[i] - z); if (d2 < r2) { double w = (1 - d2 / r2) * (1 - d2 / r2); mean += w * ys[i]; ws += w; } }
            if (ws > 0) mean /= ws;
            for (int i = 0; i < xs.Length; i++)
            {
                double d2 = (xs[i] - x) * (xs[i] - x) + (zs[i] - z) * (zs[i] - z);
                if (d2 >= r2) continue;
                double w = (1 - d2 / r2) * (1 - d2 / r2);
                if (mode == "monter") ys[i] += f * w;
                else if (mode == "creuser") ys[i] -= f * w;
                else if (mode == "aplanir") ys[i] += (y - ys[i]) * k * w;
                else if (mode == "adoucir" && ws > 0) ys[i] += (mean - ys[i]) * k * w;
            }
        }

        // ---------------------------------------------------------------- ensembles qui fonctionnent (format 4 de l'editeur)
        // objet unite <chemin> ... : copie entiere (Instantiate : automates, declencheurs, collisions compris) ; les morceaux
        // regroupes par le jeu (Combined Mesh) y sont refaits dans leur repere, sans LODGroup. Pas synchronise en co-op : chacun
        // a sa copie qui marche pour lui. masque-unite <chemin> : l'ensemble eteint (re-eteint si le jeu le rallume : LOD).
        static readonly List<GameObject> hiddenUnits = new List<GameObject>();
        static float hideCheck;
        static void KeepHidden()
        {
            if (hiddenUnits.Count == 0 || Time.realtimeSinceStartup < hideCheck) return;
            hideCheck = Time.realtimeSinceStartup + 2f;
            foreach (GameObject g in hiddenUnits) if (g != null && g.activeSelf) g.SetActive(false);
        }
        static GameObject UnitCopy(string path)
        {
            Transform t = Resolve(path);
            if (t == null) return null;
            var clone = (GameObject)UnityEngine.Object.Instantiate(t.gameObject);
            clone.name = t.name;
            clone.SetActive(true);
            foreach (LODGroup lod in clone.GetComponentsInChildren<LODGroup>(true)) UnityEngine.Object.DestroyImmediate(lod);
            foreach (MeshRenderer cr in clone.GetComponentsInChildren<MeshRenderer>(true))
            {
                MeshFilter cf = cr.GetComponent<MeshFilter>();
                if (cf == null || cf.sharedMesh == null || !cf.sharedMesh.name.StartsWith("Combined Mesh")) continue;
                string rel = "";
                for (Transform x = cr.transform; x != clone.transform; x = x.parent) rel = rel.Length == 0 ? x.name : x.name + "/" + rel;
                Transform orig = rel.Length == 0 ? t : t.Find(rel);
                MeshRenderer or = orig != null ? orig.GetComponent<MeshRenderer>() : null;
                SceneBatches.Open("cartes");
                Mesh m = or != null ? SceneBatches.LocalMesh(or, cf.sharedMesh, true) : null;
                Material[] mats = cr.sharedMaterials;
                GameObject g = cr.gameObject;
                UnityEngine.Object.DestroyImmediate(cr); UnityEngine.Object.DestroyImmediate(cf);
                if (m == null) continue;
                g.AddComponent<MeshFilter>().sharedMesh = m;
                g.AddComponent<MeshRenderer>().sharedMaterials = mats;
            }
            return clone;
        }

        // eau « du lac » (MSC : « eau:lac » ou une matiere Water4 du jeu, « mat:Water4Adv_Lake »...). Posee telle quelle, la
        // matiere du lac sortait grise (son _ReflectionTex n'est rendu que par le PlanarReflection du lac, a la hauteur du lac) ou
        // invisible (fondu de rive du nuanceur : alpha = _InvFadeParemeter.x (0,2) x profondeur, il faut 5 m de fond). Ici,
        // chaque plan d'eau a sa copie de la matiere du lac affiche (LakeNice, ou LakeSimple selon les reglages graphiques),
        // un fondu plus court et ses propres WaterBase + PlanarReflection + SpecularLighting + WaterTile (reflet a sa hauteur),
        // sur le calque Water comme le lac ; couleurs recopiees du lac toutes les 2 s (le jeu les change avec l'heure).
        class WaterCopy { public Material Mine; public Transform Lake; }
        static readonly List<WaterCopy> waters = new List<WaterCopy>();
        static float waterCheck;
        static void LakeWater(GameObject go)
        {
            foreach (MeshRenderer mr in go.GetComponentsInChildren<MeshRenderer>())
            {
                Material m = mr.sharedMaterial;
                if (m == null || !(m.name == "MWCoop eau lac" || (m.shader != null && m.shader.name.Contains("Water4")))) continue;
                Transform lake = LiveLake();
                Renderer tile = LakeTile(lake);
                if (tile == null || tile.sharedMaterial == null) continue;   // (pas de lac, MWC : l'eau simple reste)
                var w = new WaterCopy { Mine = new Material(tile.sharedMaterial), Lake = lake };
                w.Mine.name = "MWCoop eau lac (copie)";
                Shorter(w.Mine);
                mr.sharedMaterial = w.Mine;
                mr.gameObject.layer = tile.gameObject.layer;
                Component wb = AddCopy(mr.gameObject, lake.GetComponent("WaterBase"));
                if (wb == null) continue;
                SetField(wb, "sharedMaterial", w.Mine);
                Component pr = AddCopy(mr.gameObject, lake.GetComponent("PlanarReflection"));
                AddCopy(mr.gameObject, lake.GetComponent("SpecularLighting"));
                Component wt = AddCopy(mr.gameObject, tile.GetComponent("WaterTile"));
                if (wt != null) { SetField(wt, "reflection", pr); SetField(wt, "waterBase", wb); }
                waters.Add(w);
            }
        }
        // lac affiche : LakeNice/Lake (eau soignee) ou LakeSimple (reglages graphiques bas)
        static Transform LiveLake()
        {
            Transform nice = Resolve("MAP/LakeNice/Lake"), simple = Resolve("MAP/LakeSimple");
            if (nice != null && nice.gameObject.activeInHierarchy) return nice;
            if (simple != null && simple.gameObject.activeInHierarchy) return simple;
            return nice ?? simple;
        }
        static Renderer LakeTile(Transform lake)
        {
            MeshRenderer[] t = lake != null ? lake.GetComponentsInChildren<MeshRenderer>(true) : null;
            return t != null && t.Length > 0 ? t[0] : null;
        }
        // reglages pour un etang : fondu de rive plein a ~0,7 m de fond (le lac : 5 m), ecume sur ~0,3 m (le lac : ~7 m, tout un
        // etang en etait couvert), couleur de l'eau plus couvrante (le lac laisse voir son fond sombre a 71 % ; un etang creuse
        // dans l'herbe montrait l'herbe)
        static void Shorter(Material m)
        {
            if (m.HasProperty("_InvFadeParemeter"))
            {
                Vector4 f = m.GetVector("_InvFadeParemeter");
                f.x = Mathf.Max(f.x, Config.GetFloat("Test", "EauFondu", 1.5f));
                f.y = Mathf.Max(f.y, Config.GetFloat("Test", "EauEcume", 3f));
                m.SetVector("_InvFadeParemeter", f);
            }
            if (m.HasProperty("_BaseColor"))
            {
                Color c = m.GetColor("_BaseColor");
                c.a = Mathf.Max(c.a, Config.GetFloat("Test", "EauOpacite", 0.75f));
                m.SetColor("_BaseColor", c);
            }
            // reflet : le lac le remplace par une couleur unie (_ReflectionColor.a = 1) et ne le montre qu'en rasant (Fresnel
            // puissance 11, biais -0,5) -- d'ou la dalle grise ; ici le vrai reflet (ciel, sol) des qu'on regarde en biais
            if (m.HasProperty("_ReflectionColor"))
            {
                Color r = m.GetColor("_ReflectionColor");
                r.a = Mathf.Min(r.a, Config.GetFloat("Test", "EauRefletUni", 0.25f));
                m.SetColor("_ReflectionColor", r);
            }
            if (m.HasProperty("_DistortParams"))
            {
                Vector4 d = m.GetVector("_DistortParams");
                d.z = Mathf.Min(d.z, Config.GetFloat("Test", "EauFresnel", 4f));
                d.w = Mathf.Max(d.w, Config.GetFloat("Test", "EauFresnelBiais", 0.1f));
                m.SetVector("_DistortParams", d);
            }
        }
        static void WaterColors()
        {
            if (waters.Count == 0 || Time.realtimeSinceStartup < waterCheck) return;
            waterCheck = Time.realtimeSinceStartup + 2f;
            Transform lake = LiveLake();
            Renderer tile = LakeTile(lake);
            if (tile == null || tile.sharedMaterial == null) return;
            Material src = tile.sharedMaterial;
            foreach (WaterCopy w in waters)
            {
                if (w.Mine == null) continue;
                Texture refl = w.Mine.HasProperty("_ReflectionTex") ? w.Mine.GetTexture("_ReflectionTex") : null;   // (le notre, pas celui du lac)
                if (w.Mine.shader != src.shader) w.Mine.shader = src.shader;
                w.Mine.CopyPropertiesFromMaterial(src);
                w.Mine.shaderKeywords = src.shaderKeywords;
                if (refl != null) w.Mine.SetTexture("_ReflectionTex", refl);
                Shorter(w.Mine);
            }
        }
        // composant du jeu recopie (champs publics) sur un autre objet ; types pris sur le lac (scripts d'eau de MSC)
        static Component AddCopy(GameObject go, Component from)
        {
            if (from == null) return null;
            Component c = go.AddComponent(from.GetType());
            foreach (System.Reflection.FieldInfo fi in from.GetType().GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)) fi.SetValue(c, fi.GetValue(from));
            return c;
        }
        static void SetField(Component c, string name, object v)
        {
            System.Reflection.FieldInfo fi = c != null ? c.GetType().GetField(name, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance) : null;
            if (fi != null) fi.SetValue(c, v);
        }

        // ---------------------------------------------------------------- lumieres
        // lumiere <pos> <r g b> <intensite> <portee> <nuit 0|1> <ombres 0|1> [<nom>] : lumiere ponctuelle ; « nuit » : allumee
        // quand le soleil du jeu (lumiere directionnelle) est sous l'horizon ou presque eteint
        static readonly List<Light> lamps = new List<Light>();
        static Light[] suns;
        static float lampCheck;
        static void Lamp(Pack p, string[] f)
        {
            var go = new GameObject(f.Length > 7 ? f[7] : "lumiere");
            go.transform.parent = p.Group.transform;
            go.transform.position = V3(f[1]);
            var l = go.AddComponent<Light>();
            l.type = LightType.Point;
            string[] c = f[2].Split(' ');
            l.color = new Color(Fl(c[0]), Fl(c[1]), Fl(c[2]));
            l.intensity = Fl(f[3]);
            l.range = Fl(f[4]);
            l.shadows = f[6] == "1" ? LightShadows.Soft : LightShadows.None;
            l.renderMode = LightRenderMode.ForcePixel;
            if (f[5] == "1") { lamps.Add(l); l.enabled = IsNight(); }
        }
        static bool IsNight()
        {
            if (suns == null)
            {
                var l = new List<Light>();
                foreach (Light x in UnityEngine.Object.FindObjectsOfType<Light>()) if (x.type == LightType.Directional && (root == null || !x.transform.IsChildOf(root.transform))) l.Add(x);
                suns = l.ToArray();
            }
            float best = 0f; bool up = false;
            foreach (Light x in suns)
            {
                if (x == null || !x.enabled || !x.gameObject.activeInHierarchy) continue;
                best = Mathf.Max(best, x.intensity);
                if (x.transform.forward.y < -0.08f && x.intensity > 0.25f) up = true;
            }
            return !up || best < 0.25f;
        }
        static void NightLamps()
        {
            if (lamps.Count == 0 || Time.realtimeSinceStartup < lampCheck) return;
            lampCheck = Time.realtimeSinceStartup + 2f;
            bool night = IsNight();
            foreach (Light l in lamps) if (l != null) l.enabled = night;
        }

        // ---------------------------------------------------------------- un paquet
        class Part { public Mesh Mesh; public Material[] Mats; }
        class Pack
        {
            public string Name, File; public Dictionary<string, byte[]> Z; public string[] Lines;
            public Dictionary<string, Part> Parts = new Dictionary<string, Part>(); public List<string[]> MatLines = new List<string[]>();
            public GameObject Group, Last; public int Objs, Missing, Hid;
            public List<string> ReliefTargets = new List<string>(); public List<string[]> Strokes = new List<string[]>();
        }

        static Pack Begin(string file)
        {
            try
            {
                var p = new Pack { File = file, Z = ReadZip(file), Name = Path.GetFileNameWithoutExtension(file) };
                byte[] txt;
                if (!p.Z.TryGetValue("carte.txt", out txt)) { Log.Warn("cartes : " + Path.GetFileName(file) + " : carte.txt absent"); return null; }
                p.Lines = Encoding.UTF8.GetString(txt).Replace("\r", "").Split('\n');
                if (p.Lines.Length == 0 || !p.Lines[0].StartsWith("MWCMAP\t")) { Log.Warn("cartes : " + Path.GetFileName(file) + " : format inconnu"); return null; }
                string game = "";
                foreach (string l in p.Lines)
                {
                    string[] f = l.Split('\t');
                    if (f[0] == "jeu" && f.Length > 1) game = f[1];
                    else if (f[0] == "nom" && f.Length > 1) p.Name = f[1];
                    else if (f[0] == "matiere") p.MatLines.Add(f);
                }
                if (game != (Game.Msc ? "msc" : "mwc")) { Log.Info("cartes : " + p.Name + " : pour " + game + ", ignoree ici"); return null; }
                p.Group = new GameObject("carte " + p.Name);
                p.Group.transform.parent = root.transform;
                return p;
            }
            catch (Exception e) { Log.Warn("cartes : " + Path.GetFileName(file) + " : " + e.Message); return null; }
        }

        static void Line(Pack p, string l)
        {
            string[] f = l.Split('\t');
            try
            {
                if (f[0] == "modele" && f.Length >= 3)
                {
                    byte[] b;
                    if (p.Z.TryGetValue(f[2], out b)) p.Parts[f[1]] = new Part { Mesh = ReadMesh(b, f[1]), Mats = Materials(f[1], p) };
                }
                else if (f[0] == "objet" && f.Length >= 7)
                {
                    p.Last = null;
                    string plant = f.Length > 9 && f[9].StartsWith("plante ") ? f[9].Substring(7) : null;
                    GameObject go = f[1] == "jeu" ? GameCopy(f[2], f.Length > 8 && f[8] != "-" ? f[8] : null, plant) : f[1] == "unite" ? UnitCopy(f[2]) : ModelObject(f[2], p.Parts);
                    if (go == null) { if (p.Missing++ < 10) Log.Warn("cartes : " + p.Name + " : " + f[2] + " introuvable ici"); return; }
                    go.name = f.Length > 7 ? f[7] : go.name;
                    go.transform.parent = p.Group.transform;
                    go.transform.position = V3(f[3]);
                    go.transform.rotation = Q(f[4]);
                    go.transform.localScale = V3(f[5]);
                    Collide(go, f[6]);
                    if (f[1] == "modele") LakeWater(go);
                    p.Objs++;
                    p.Last = go;
                    if (Config.Testing)
                    {
                        Renderer rr = go.GetComponentInChildren<Renderer>();
                        MeshFilter mm = go.GetComponentInChildren<MeshFilter>();
                        Log.Info("cartes : " + p.Name + " : " + go.name + " en " + go.transform.position.ToString("F1") + (rr != null ? ", boite " + rr.bounds.center.ToString("F1") + " / " + rr.bounds.size.ToString("F1") : "")
                                 + (mm != null && mm.sharedMesh != null ? ", " + mm.sharedMesh.vertexCount + " sommets" : "") + ", calque " + go.layer);
                    }
                }
                else if (f[0] == "habit" && f.Length >= 3 && p.Last != null) Habit(p, p.Last, f[1], f[2], f.Length > 3 ? f[3] : null);
                else if (f[0] == "lumiere" && f.Length >= 7) { Lamp(p, f); p.Objs++; }
                else if (f[0] == "masque" && f.Length >= 2) { if (Hide(f[1])) p.Hid++; }
                else if (f[0] == "masque-unite" && f.Length >= 2) { Transform u = Resolve(f[1]); if (u != null) { u.gameObject.SetActive(false); hiddenUnits.Add(u.gameObject); p.Hid++; } }
                else if (f[0] == "relief-cible" && f.Length >= 2) p.ReliefTargets.Add(f[1]);
                else if (f[0] == "relief" && f.Length >= 7) p.Strokes.Add(f);
            }
            catch (Exception e) { if (p.Missing++ < 10) Log.Warn("cartes : " + p.Name + " : ligne « " + f[0] + " » : " + e.Message); }
        }

        static void End(Pack p)
        {
            Objects += p.Objs; Hidden += p.Hid;
            Log.Info("cartes : " + p.Name + " : " + p.Objs + " objet(s), " + p.Hid + " masque(s)" + (p.Missing > 0 ? ", " + p.Missing + " introuvable(s)" : ""));
        }

        // [Test] SondeEau=1 : objets d'eau du jeu (noms, composants, declencheurs, automates) dans le journal -- pour l'eau vraie
        // de l'editeur (nager, couler)
        static void ProbeWater()
        {
            if (Config.GetInt("Test", "SondeEau", 0) == 0) return;
            int n = 0;
            foreach (Transform t in Game.RootSet(true))
                foreach (Transform c in t.GetComponentsInChildren<Transform>(true))
                {
                    string nm = c.name.ToLowerInvariant();
                    if (!(nm.Contains("water") || nm.Contains("swim") || nm.Contains("lake") || nm.Contains("drown") || nm.Contains("vesi") || nm.Contains("uima") || nm.Contains("river") || nm.Contains("ice"))) continue;
                    var comps = new List<string>();
                    foreach (Component k in c.GetComponents<Component>())
                    {
                        if (k == null || k is Transform) continue;
                        string d = k.GetType().Name;
                        Collider col = k as Collider;
                        if (col != null) d += (col.isTrigger ? "(trigger " : "(") + col.bounds.size.ToString("F0") + ")";
                        PlayMakerFSM fsm = k as PlayMakerFSM;
                        if (fsm != null) d += "(" + fsm.FsmName + ")";
                        comps.Add(d);
                    }
                    if (comps.Count == 0) continue;
                    Log.Info("sonde eau : " + Recon.Path(c) + " [" + (c.gameObject.activeInHierarchy ? "actif" : "eteint") + ", calque " + c.gameObject.layer + ", " + c.position.ToString("F0") + "] " + string.Join(", ", comps.ToArray()));
                    if (nm.Contains("water") || nm.Contains("lake") || nm.Contains("vesi")) ProbeWaterDetail(c);
                    if (++n >= 200) return;
                }
        }

        // detail d'un objet d'eau : matieres (nuanceur, mots-cles, proprietes Water4 connues) et champs des scripts d'eau
        static readonly string[] waterProps = { "_ReflectionTex", "_RefractionTex", "_BumpMap", "_ShoreTex", "_Fresnel", "_MainTex", "_InvFadeParemeter",
            "_DistortParams", "_BaseColor", "_ReflectionColor", "_SpecularColor", "_Shininess", "_GerstnerIntensity", "_GAmplitude", "_GFrequency",
            "_GSteepness", "_GSpeed", "_GDirectionAB", "_GDirectionCD", "_BumpTiling", "_BumpDirection", "_Foam", "_WorldLightDir", "_Color", "_WaveScale", "_HorizonColor" };
        static void ProbeWaterDetail(Transform c)
        {
            Renderer r = c.GetComponent<Renderer>();
            if (r != null)
                foreach (Material m in r.sharedMaterials)
                {
                    if (m == null) continue;
                    var props = new List<string>();
                    foreach (string pn in waterProps)
                    {
                        if (!m.HasProperty(pn)) continue;
                        string v;
                        Texture t = null;
                        try { t = m.GetTexture(pn); } catch { }
                        if (t != null) v = "tex " + t.name + " " + t.width + "x" + t.height;
                        else { Vector4 x = m.GetVector(pn); v = x.ToString("F3"); }
                        props.Add(pn + "=" + v);
                    }
                    Log.Info("sonde eau :   matiere « " + m.name + " » nuanceur « " + (m.shader != null ? m.shader.name : "?") + " » file " + m.renderQueue
                             + " mots-cles [" + string.Join(" ", m.shaderKeywords ?? new string[0]) + "] " + string.Join(" ; ", props.ToArray()));
                }
            MeshFilter mf = c.GetComponent<MeshFilter>();
            if (mf != null && mf.sharedMesh != null)
                Log.Info("sonde eau :   maillage « " + mf.sharedMesh.name + " » " + mf.sharedMesh.vertexCount + " sommets, couleurs " + mf.sharedMesh.colors.Length + ", boite " + mf.sharedMesh.bounds.size.ToString("F1") + ", echelle " + c.lossyScale.ToString("F2"));
            foreach (Component k in c.GetComponents<Component>())
            {
                if (k == null || k is Transform || k is Renderer || k is MeshFilter || k is Collider || k is PlayMakerFSM) continue;
                var fs = new List<string>();
                foreach (System.Reflection.FieldInfo fi in k.GetType().GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance))
                {
                    object v = fi.GetValue(k);
                    UnityEngine.Object uo = v as UnityEngine.Object;
                    fs.Add(fi.Name + "=" + (uo != null ? "<" + uo.name + ">" : v is Component || v == null ? "null" : v.ToString()));
                }
                Log.Info("sonde eau :   " + k.GetType().FullName + " " + string.Join(" ; ", fs.ToArray()));
            }
            if (c.GetComponent("WaterBase") != null)   // (dalles du lac : noms sans « water »)
            {
                int n = 0;
                foreach (Transform ch in c.GetComponentsInChildren<Transform>(true))
                {
                    if (ch == c) continue;
                    Log.Info("sonde eau : dalle " + Recon.Path(ch) + " [" + (ch.gameObject.activeInHierarchy ? "actif" : "eteint") + ", calque " + ch.gameObject.layer + ", " + ch.position.ToString("F1") + "]");
                    ProbeWaterDetail(ch);
                    if (++n >= 6) break;
                }
            }
        }

        // [Test] SondeHauteur=x,z;x,z... : hauteur du sol (collision) sous ces points -- relief sculpte
        static void ProbeHeights()
        {
            string o = Config.Get("Test", "SondeHauteur", "");
            if (o.Length == 0) return;
            foreach (string pt in o.Split(';'))
            {
                string[] xz = pt.Split(',');
                if (xz.Length < 2) continue;
                var from = new Vector3(Fl(xz[0]), 200f, Fl(xz[1]));
                RaycastHit[] hs = Physics.RaycastAll(from, Vector3.down, 400f);
                Array.Sort(hs, (x, y) => x.distance.CompareTo(y.distance));
                var l = new List<string>();
                foreach (RaycastHit h in hs) if (!h.collider.isTrigger && l.Count < 3) l.Add(h.point.y.ToString("F2") + " " + h.collider.name);
                Log.Info("sonde hauteur : (" + xz[0] + ", " + xz[1] + ") " + (l.Count > 0 ? string.Join(" ; ", l.ToArray()) : "rien"));
            }
        }

        // [Test] SondeUnite=<chemin>;<chemin>... : parents d'un objet, rendus et automates de chacun (copies fonctionnelles)
        static void ProbeUnits()
        {
            string o = Config.Get("Test", "SondeUnite", "");
            if (o.Length == 0) return;
            foreach (string path in o.Split(';'))
            {
                Transform t = Resolve(path.Trim());
                if (t == null) { Log.Info("sonde unite : " + path + " introuvable"); continue; }
                for (Transform a = t; a != null; a = a.parent)
                {
                    var fsms = new List<string>();
                    foreach (PlayMakerFSM f in a.GetComponentsInChildren<PlayMakerFSM>(true)) if (fsms.Count < 10) fsms.Add(f.gameObject.name + ":" + f.FsmName);
                    Log.Info("sonde unite : " + Recon.Path(a) + " -- " + a.GetComponentsInChildren<Renderer>(true).Length + " rendu(s), " + a.GetComponentsInChildren<Collider>(true).Length + " collision(s), automates " + string.Join(" ", fsms.ToArray()));
                }
            }
        }

        // [Test] CartesPhoto=x,y,z,cx,cy,cz : photo d'essai (cartes\photo.png) d'une camera en (x, y, z) qui regarde (cx, cy, cz)
        static void TestPhoto()
        {
            string o = Config.Get("Test", "CartesPhoto", "");
            if (o.Length == 0) return;
            try
            {
                string[] p = o.Split(',');
                var go = new GameObject("photo cartes");
                var cam = go.AddComponent<Camera>();
                var lg = new GameObject("photo cartes (lumiere)");   // (la nouvelle partie commence la nuit sur MWC)
                var li = lg.AddComponent<Light>(); li.type = LightType.Directional; li.intensity = 1.1f; lg.transform.rotation = Quaternion.Euler(50, 30, 0);
                Color amb = RenderSettings.ambientLight; RenderSettings.ambientLight = new Color(0.55f, 0.58f, 0.62f);
                go.transform.position = new Vector3(Fl(p[0]), Fl(p[1]), Fl(p[2]));
                go.transform.LookAt(new Vector3(Fl(p[3]), Fl(p[4]), Fl(p[5])));
                cam.fieldOfView = 60; cam.farClipPlane = 3000;
                cam.depthTextureMode = DepthTextureMode.Depth;   // (comme la camera du joueur : fondu de rive de l'eau Water4)
                var rt = new RenderTexture(1280, 720, 24);
                cam.targetTexture = rt; cam.Render();
                RenderTexture.active = rt;
                var tex = new Texture2D(1280, 720, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); tex.Apply();
                RenderTexture.active = null; cam.targetTexture = null;
                File.WriteAllBytes(Path.Combine(Dir, "photo.png"), tex.EncodeToPNG());
                RenderSettings.ambientLight = amb;
                UnityEngine.Object.Destroy(lg); UnityEngine.Object.Destroy(go); UnityEngine.Object.Destroy(rt); UnityEngine.Object.Destroy(tex);
                Log.Info("cartes : photo d'essai ecrite");
                foreach (WaterCopy w in waters)   // (eau du lac : reflet bien rendu ?)
                {
                    Texture rt0 = w.Mine != null && w.Mine.HasProperty("_ReflectionTex") ? w.Mine.GetTexture("_ReflectionTex") : null;
                    string pr = "";
                    foreach (MeshRenderer mr in root.GetComponentsInChildren<MeshRenderer>())
                    {
                        if (mr.sharedMaterial != w.Mine) continue;
                        Component c = mr.GetComponent("PlanarReflection");
                        if (c == null) { pr = "pas de PlanarReflection"; break; }
                        foreach (string fn in new[] { "m_ReflectionCamera", "m_SharedMaterial", "reflectionMask" })
                        {
                            System.Reflection.FieldInfo fi = c.GetType().GetField(fn, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                            object v = fi != null ? fi.GetValue(c) : null;
                            UnityEngine.Object uo = v as UnityEngine.Object;
                            pr += fn + "=" + (uo != null ? uo.name : v is LayerMask ? ((LayerMask)v).value.ToString() : "null") + " ";
                        }
                        break;
                    }
                    Log.Info("cartes : eau du lac : _ReflectionTex " + (rt0 != null ? rt0.name + " " + rt0.width + "x" + rt0.height : "aucune") + ", mots-cles globaux WATER_REFLECTIVE " + Shader.IsKeywordEnabled("WATER_REFLECTIVE")
                             + " WATER_EDGEBLEND_ON " + Shader.IsKeywordEnabled("WATER_EDGEBLEND_ON") + " WATER_SIMPLE " + Shader.IsKeywordEnabled("WATER_SIMPLE") + ", maximumLOD " + Shader.globalMaximumLOD + "/" + (w.Mine != null ? w.Mine.shader.maximumLOD : 0) + ", " + pr);
                }
            }
            catch (Exception e) { Log.Warn("cartes : photo d'essai : " + e.Message); }
        }

        static float Fl(string s) { return float.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture); }
        static Vector3 V3(string s) { string[] p = s.Split(' '); return new Vector3(Fl(p[0]), Fl(p[1]), Fl(p[2])); }
        static Quaternion Q(string s) { string[] p = s.Split(' '); return new Quaternion(Fl(p[0]), Fl(p[1]), Fl(p[2]), Fl(p[3])); }

        // ---------------------------------------------------------------- objets du jeu (par leur chemin unique)
        static Dictionary<string, Transform> roots;
        static readonly Dictionary<string, Mesh> meshCache = new Dictionary<string, Mesh>();

        public static Transform Resolve(string path)
        {
            if (roots == null)
            {
                roots = new Dictionary<string, Transform>();
                foreach (Transform t in Game.RootSet(true)) if (!roots.ContainsKey(t.name)) roots[t.name] = t;
            }
            string[] segs = path.Split('/');
            Transform cur;
            if (!roots.TryGetValue(segs[0], out cur)) return null;
            for (int i = 1; i < segs.Length && cur != null; i++)
            {
                string n = segs[i]; int want = 0, h = n.LastIndexOf('#');
                if (h > 0 && int.TryParse(n.Substring(h + 1), out want)) n = n.Substring(0, h); else want = 0;
                Transform next = null; int seen = 0;
                for (int c = 0; c < cur.childCount; c++)
                {
                    Transform ch = cur.GetChild(c);
                    if (ch.name != n) continue;
                    if (seen++ == want) { next = ch; break; }
                }
                cur = next;
            }
            return cur;
        }

        static GameObject GameCopy(string path, string center, string plant)
        {
            Transform t = Resolve(path);
            MeshRenderer r = t != null ? t.GetComponent<MeshRenderer>() : null;
            MeshFilter mf = t != null ? t.GetComponent<MeshFilter>() : null;
            if (r == null || mf == null || mf.sharedMesh == null) return null;
            Mesh mesh;
            if (plant != null)   // (plante d'une foret : pinceau de vegetation de l'editeur)
            {
                if (!meshCache.TryGetValue(path + "|plante " + plant, out mesh)) meshCache[path + "|plante " + plant] = mesh = PlantMesh(mf.sharedMesh, t, V3(plant));
            }
            else if (!meshCache.TryGetValue(path + "|" + center, out mesh))
            {
                mesh = mf.sharedMesh;
                if (mesh.name.StartsWith("Combined Mesh"))   // (lot : relu dans le fichier de la scene, ramene dans le repere de l'objet)
                {
                    SceneBatches.Open("cartes");
                    mesh = SceneBatches.LocalMesh(r, mesh);
                    if (mesh != null && !string.IsNullOrEmpty(center))
                    {
                        // (recentre sur sa propre boite, comme l'editeur : le centre de la carte n'est qu'un repere -- meme
                        // resultat, et juste meme si la geometrie du jeu a change depuis la preparation)
                        Vector3 c = mesh.bounds.center;
                        Vector3[] v = mesh.vertices;
                        for (int i = 0; i < v.Length; i++) v[i] -= c;
                        mesh.vertices = v;
                        mesh.RecalculateBounds();
                    }
                }
                meshCache[path + "|" + center] = mesh;
            }
            if (mesh == null) return null;
            var go = new GameObject(t.name);
            go.layer = t.gameObject.layer;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterials = r.sharedMaterials;
            MeshCollider src = t.GetComponent<MeshCollider>();
            colliderOf[go] = src != null ? src.sharedMesh : null;   // (forme : celle de la collision de l'objet du jeu, lisible)
            return go;
        }
        static readonly Dictionary<GameObject, Mesh> colliderOf = new Dictionary<GameObject, Mesh>();

        // Plantes d'une foret du jeu (un maillage de quads croises, deux ou trois par arbre) : composantes reliees par leurs
        // sommets, regroupees quand leurs centres sont a moins de 35 cm -- meme calcul, meme ordre que l'editeur
        // (edition.js plantClusters) ; la plante la plus proche de « key » (repere du maillage), posee par son pied.
        class Cluster { public Vector3 C; public List<int> Tris = new List<int>(); }
        static readonly Dictionary<Mesh, List<Cluster>> plantCache = new Dictionary<Mesh, List<Cluster>>();
        static long CellKey(int x, int y, int z) { return ((long)(x & 0x1FFFFF) << 42) | ((long)(y & 0x1FFFFF) << 21) | (long)(z & 0x1FFFFF); }
        static Mesh PlantMesh(Mesh src, Transform t, Vector3 key)
        {
            if (src.name.StartsWith("Combined Mesh")) return null;
            Vector3[] v = src.vertices;
            var subOf = new List<int>(); var tri = new List<int>();
            for (int sm = 0; sm < src.subMeshCount; sm++) { int[] tt = src.GetTriangles(sm); for (int i = 0; i + 2 < tt.Length; i += 3) { tri.Add(tt[i]); tri.Add(tt[i + 1]); tri.Add(tt[i + 2]); subOf.Add(sm); } }
            int nt = subOf.Count;
            List<Cluster> clusters;
            if (!plantCache.TryGetValue(src, out clusters))
            {
                var par = new int[v.Length];
                for (int i = 0; i < par.Length; i++) par[i] = i;
                Func<int, int> find = x => { while (par[x] != x) { par[x] = par[par[x]]; x = par[x]; } return x; };
                for (int k = 0; k < nt; k++) { int a = find(tri[k * 3]); par[find(tri[k * 3 + 1])] = a; par[find(tri[k * 3 + 2])] = a; }
                var comps = new Dictionary<int, int>(); var mn = new List<Vector3>(); var mx = new List<Vector3>(); var ctris = new List<List<int>>();
                for (int k = 0; k < nt; k++)
                {
                    int root = find(tri[k * 3]), ci;
                    if (!comps.TryGetValue(root, out ci)) { ci = mn.Count; comps[root] = ci; mn.Add(new Vector3(1e9f, 1e9f, 1e9f)); mx.Add(new Vector3(-1e9f, -1e9f, -1e9f)); ctris.Add(new List<int>()); }
                    ctris[ci].Add(k);
                    for (int c = 0; c < 3; c++) { Vector3 p = v[tri[k * 3 + c]]; mn[ci] = Vector3.Min(mn[ci], p); mx[ci] = Vector3.Max(mx[ci], p); }
                }
                clusters = new List<Cluster>();
                var cells = new Dictionary<long, List<Cluster>>();
                for (int ci = 0; ci < mn.Count; ci++)
                {
                    // (centres dans le repere de l'editeur : z inverse, comme ses calculs)
                    Vector3 ce = new Vector3((mn[ci].x + mx[ci].x) / 2f, (mn[ci].y + mx[ci].y) / 2f, -(mn[ci].z + mx[ci].z) / 2f);
                    int kx = Mathf.FloorToInt(ce.x / 0.5f), ky = Mathf.FloorToInt(ce.y / 0.5f), kz = Mathf.FloorToInt(ce.z / 0.5f);
                    Cluster hit = null;
                    for (int dx = -1; dx <= 1 && hit == null; dx++) for (int dy = -1; dy <= 1 && hit == null; dy++) for (int dz = -1; dz <= 1 && hit == null; dz++)
                    {
                        List<Cluster> l;
                        if (!cells.TryGetValue(CellKey(kx + dx, ky + dy, kz + dz), out l)) continue;
                        foreach (Cluster cl in l) if ((cl.C - ce).magnitude < 0.35f) { hit = cl; break; }
                    }
                    if (hit == null)
                    {
                        hit = new Cluster { C = ce }; clusters.Add(hit);
                        long ck = CellKey(kx, ky, kz); List<Cluster> l;
                        if (!cells.TryGetValue(ck, out l)) cells[ck] = l = new List<Cluster>();
                        l.Add(hit);
                    }
                    hit.Tris.AddRange(ctris[ci]);
                }
                plantCache[src] = clusters;
            }
            Vector3 want = new Vector3(key.x, key.y, -key.z);
            Cluster best = null; float bd = float.MaxValue;
            foreach (Cluster cl in clusters) { float d = (cl.C - want).sqrMagnitude; if (d < bd) { bd = d; best = cl; } }
            if (best == null) return null;
            // pied : l'axe du maillage qui monte dans le monde (ligne y de la matrice), au plus bas ; centre de la boite ailleurs
            Vector4 row = t.localToWorldMatrix.GetRow(1);
            int up = Mathf.Abs(row.y) > Mathf.Abs(row.x) ? 1 : 0; if (Mathf.Abs(row.z) > Mathf.Abs(row[up])) up = 2;
            Vector3 bmn = new Vector3(1e9f, 1e9f, 1e9f), bmx = new Vector3(-1e9f, -1e9f, -1e9f);
            foreach (int k in best.Tris) for (int c = 0; c < 3; c++) { Vector3 p = v[tri[k * 3 + c]]; bmn = Vector3.Min(bmn, p); bmx = Vector3.Max(bmx, p); }
            Vector3 o = (bmn + bmx) / 2f; o[up] = row[up] >= 0 ? bmn[up] : bmx[up];
            Vector3[] n0 = src.normals; Vector2[] uv0 = src.uv, uv20 = src.uv2; Color[] c0 = src.colors; Vector4[] t0 = src.tangents;   // (couleurs des sommets : l'ombrage du feuillage)
            bool hasC = c0 != null && c0.Length == v.Length, hasU2 = uv20 != null && uv20.Length == v.Length, hasT = t0 != null && t0.Length == v.Length;
            var map = new Dictionary<int, int>(); var nv = new List<Vector3>(); var nn = new List<Vector3>(); var nuv = new List<Vector2>();
            var nc = new List<Color>(); var nuv2 = new List<Vector2>(); var ntg = new List<Vector4>();
            var subs = new List<int>[src.subMeshCount];
            for (int sm = 0; sm < subs.Length; sm++) subs[sm] = new List<int>();
            foreach (int k in best.Tris)
                for (int c = 0; c < 3; c++)
                {
                    int vi = tri[k * 3 + c], j;
                    if (!map.TryGetValue(vi, out j))
                    {
                        j = nv.Count; map[vi] = j; nv.Add(v[vi] - o); nn.Add(n0 != null && n0.Length == v.Length ? n0[vi] : Vector3.up); nuv.Add(uv0 != null && uv0.Length == v.Length ? uv0[vi] : Vector2.zero);
                        if (hasC) nc.Add(c0[vi]); if (hasU2) nuv2.Add(uv20[vi]); if (hasT) ntg.Add(t0[vi]);
                    }
                    subs[subOf[k]].Add(j);
                }
            var m = new Mesh { name = src.name + " (plante)" };
            m.vertices = nv.ToArray(); m.normals = nn.ToArray(); m.uv = nuv.ToArray();
            if (hasC) m.colors = nc.ToArray(); if (hasU2) m.uv2 = nuv2.ToArray(); if (hasT) m.tangents = ntg.ToArray();
            m.subMeshCount = subs.Length;
            for (int sm = 0; sm < subs.Length; sm++) m.SetTriangles(subs[sm].ToArray(), sm);
            m.RecalculateBounds();
            return m;
        }

        // aucune | boite (la boite du maillage) | forme (le maillage s'il est lisible, sinon celui de la collision de l'objet du
        // jeu, sinon la boite)
        static void Collide(GameObject go, string mode)
        {
            if (mode == "aucune") return;
            MeshFilter mf = go.GetComponent<MeshFilter>();
            Mesh m = mf != null ? mf.sharedMesh : null;
            Mesh col;
            colliderOf.TryGetValue(go, out col);
            colliderOf.Remove(go);
            if (mode == "forme")
            {
                Mesh shape = m != null && m.isReadable ? m : col;
                if (shape != null) { go.AddComponent<MeshCollider>().sharedMesh = shape; return; }
            }
            var bc = go.AddComponent<BoxCollider>();
            if (m != null) { bc.center = m.bounds.center; bc.size = m.bounds.size; }
        }

        static bool Hide(string path)
        {
            Transform t = Resolve(path);
            if (t == null) return false;
            foreach (Renderer r in t.GetComponents<Renderer>()) r.enabled = false;
            foreach (Collider c in t.GetComponents<Collider>()) c.enabled = false;
            return true;
        }

        // ---------------------------------------------------------------- modeles importes
        static GameObject ModelObject(string ids, Dictionary<string, Part> parts)
        {
            var go = new GameObject("modele");
            Part first = null;
            foreach (string id in ids.Split(','))
            {
                Part p;
                if (!parts.TryGetValue(id, out p) || p.Mesh == null) continue;
                var c = first == null ? go : new GameObject("partie " + id);
                if (c != go) c.transform.parent = go.transform;
                c.AddComponent<MeshFilter>().sharedMesh = p.Mesh;
                c.AddComponent<MeshRenderer>().sharedMaterials = p.Mats;
                if (first == null) first = p;
            }
            if (first == null) { UnityEngine.Object.Destroy(go); return null; }
            return go;
        }

        // matiere <id> <i> <r g b a> <texture> <decoupe 0|1> [<ru rv>] ; texture : image du paquet, « tex:<nom> » (texture du jeu,
        // retrouvee par son nom : aucune image du jeu dans le paquet), « mat:<nom> » (matiere du jeu entiere : routes, eau),
        // « eau: » (eau simple, transparente), « - »
        static Material[] Materials(string id, Pack p)
        {
            var mats = new List<Material>();
            Shader diffuse = Diffuse(), cut = Shader.Find("Legacy Shaders/Transparent/Cutout/Diffuse") ?? Shader.Find("Transparent/Cutout/Diffuse") ?? diffuse;
            foreach (string[] f in p.MatLines)
            {
                if (f.Length < 6 || f[1] != id) continue;
                string[] c = f[3].Split(' ');
                var m = new Material(f[5] == "1" ? cut : diffuse);
                m.color = new Color(Fl(c[0]), Fl(c[1]), Fl(c[2]), Fl(c[3]));
                mats.Add(Dress(m, f[4], f.Length > 6 ? f[6] : null, p));
            }
            if (mats.Count == 0) mats.Add(new Material(diffuse));
            return mats.ToArray();
        }
        static Shader Diffuse() { return Shader.Find("Legacy Shaders/Diffuse") ?? Shader.Find("Diffuse") ?? Shader.Find("Standard"); }

        // tenue d'une matiere (base : celle d'origine, gardee si la reference est introuvable chez ce joueur)
        static Material Dress(Material base0, string tex, string rep, Pack p)
        {
            Vector2 sc = Vector2.one;
            if (!string.IsNullOrEmpty(rep)) { string[] r = rep.Split(' '); if (r.Length >= 2) sc = new Vector2(Fl(r[0]), Fl(r[1])); }
            if (string.IsNullOrEmpty(tex) || tex == "-") return base0;
            if (tex.StartsWith("mat:"))
            {
                Material gm = GameMat(tex.Substring(4));
                if (gm == null) { Log.Warn("cartes : " + p.Name + " : matiere du jeu « " + tex.Substring(4) + " » introuvable"); return base0; }
                if (sc == Vector2.one) return gm;
                var m = new Material(gm); m.mainTextureScale = Vector2.Scale(gm.mainTextureScale, sc);
                return m;
            }
            if (tex == "eau:lac") { var lw = new Material(Shader.Find("Legacy Shaders/Transparent/Diffuse") ?? Diffuse()); lw.name = "MWCoop eau lac"; lw.color = new Color(0.1f, 0.2f, 0.27f, 0.78f); return lw; }
            if (tex.StartsWith("eau:"))
            {
                var w = new Material(Shader.Find("Legacy Shaders/Transparent/Diffuse") ?? Shader.Find("Transparent/Diffuse") ?? Diffuse());
                w.color = Game.Msc ? new Color(0.1f, 0.2f, 0.27f, 0.78f) : new Color(0.3f, 0.4f, 0.48f, 0.78f);   // (eclairee par le soleil du jeu : sombre)
                return w;
            }
            Texture t = tex.StartsWith("tex:") ? GameTex(tex.Substring(4)) : PackTex(p, tex);
            if (t == null) { Log.Warn("cartes : " + p.Name + " : texture « " + tex + " » introuvable"); return base0; }
            var d = base0 != null ? new Material(base0) : new Material(Diffuse());
            d.mainTexture = t; d.mainTextureScale = sc; d.mainTextureOffset = Vector2.zero;
            return d;
        }

        // habit <sous-maillage|*> <texture> [<ru rv>] : tenue d'une copie d'objet du jeu (ligne objet juste avant)
        static void Habit(Pack p, GameObject go, string sub, string tex, string rep)
        {
            MeshRenderer mr = go.GetComponent<MeshRenderer>();
            if (mr == null) return;
            Material[] ms = mr.sharedMaterials;
            int only;
            bool all = sub == "*";
            if (!all && (!int.TryParse(sub, out only) || only < 0 || only >= ms.Length)) return;
            for (int i = 0; i < ms.Length; i++) if (all || i.ToString() == sub) ms[i] = Dress(ms[i], tex, rep, p);
            mr.sharedMaterials = ms;
        }

        static Dictionary<string, Texture2D> gameTex;
        static Dictionary<string, Material> gameMat;
        static readonly Dictionary<string, Texture2D> packTex = new Dictionary<string, Texture2D>();
        // Textures du jeu par leur nom : d'abord celles des matieres (quelques centaines d'objets ; ce sont elles que l'editeur
        // propose), toutes les textures chargees seulement en dernier recours -- FindObjectsOfTypeAll<Texture2D> prenait 93 ms
        // d'un bloc au chargement des cartes (1.4.0).
        static bool allTex;
        static Texture2D GameTex(string name)
        {
            if (gameTex == null)
            {
                gameTex = new Dictionary<string, Texture2D>();
                GameMat("");
                foreach (Material m in gameMat.Values)
                    foreach (string prop in new[] { "_MainTex", "_Detail" })
                    {
                        Texture2D t = m.HasProperty(prop) ? m.GetTexture(prop) as Texture2D : null;
                        if (t != null && !gameTex.ContainsKey(t.name)) gameTex[t.name] = t;
                    }
            }
            Texture2D r;
            if (gameTex.TryGetValue(name, out r)) return r;
            if (!allTex)
            {
                allTex = true;
                foreach (Texture2D t in Resources.FindObjectsOfTypeAll<Texture2D>()) if (t != null && !gameTex.ContainsKey(t.name)) gameTex[t.name] = t;
            }
            return gameTex.TryGetValue(name, out r) ? r : null;
        }
        static Material GameMat(string name)
        {
            if (gameMat == null)
            {
                gameMat = new Dictionary<string, Material>();
                foreach (Material m in Resources.FindObjectsOfTypeAll<Material>())
                {
                    if (m == null) continue;
                    string n = m.name.Replace(" (Instance)", "");
                    if (!gameMat.ContainsKey(n) || m.name == n) gameMat[n] = m;   // (l'originale avant ses copies)
                }
            }
            Material r; return gameMat.TryGetValue(name.Replace(" (Instance)", ""), out r) ? r : null;
        }
        static Texture2D PackTex(Pack p, string path)
        {
            Texture2D t;
            string key = p.File + "|" + path;
            if (packTex.TryGetValue(key, out t)) return t;
            byte[] png;
            if (!p.Z.TryGetValue(path, out png)) return null;
            t = new Texture2D(2, 2, TextureFormat.ARGB32, true);
            if (!t.LoadImage(png)) return null;
            t.wrapMode = TextureWrapMode.Repeat;
            packTex[key] = t;
            return t;
        }

        // n, drapeaux (1 normales, 2 uv), k sous-maillages, k tailles, positions, normales, uv, indices (u32)
        static Mesh ReadMesh(byte[] b, string id)
        {
            using (var r = new BinaryReader(new MemoryStream(b)))
            {
                int n = r.ReadInt32(), fl = r.ReadInt32(), k = r.ReadInt32();
                if (n <= 0 || n > 65000 || k <= 0 || k > 64) throw new Exception("modele " + id + " : " + n + " sommets, " + k + " sous-maillages");
                var sizes = new int[k];
                for (int i = 0; i < k; i++) sizes[i] = r.ReadInt32();
                var v = new Vector3[n];
                for (int i = 0; i < n; i++) v[i] = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
                var m = new Mesh { name = "modele " + id };
                m.vertices = v;
                if ((fl & 1) != 0) { var nr = new Vector3[n]; for (int i = 0; i < n; i++) nr[i] = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle()); m.normals = nr; }
                if ((fl & 2) != 0) { var uv = new Vector2[n]; for (int i = 0; i < n; i++) uv[i] = new Vector2(r.ReadSingle(), r.ReadSingle()); m.uv = uv; }
                m.subMeshCount = k;
                for (int s = 0; s < k; s++)
                {
                    var t = new int[sizes[s]];
                    for (int i = 0; i < t.Length; i++) t[i] = r.ReadInt32();
                    m.SetTriangles(t, s);
                }
                if ((fl & 1) == 0) m.RecalculateNormals();
                m.RecalculateBounds();
                return m;
            }
        }

        // ---------------------------------------------------------------- zip (sans compression ou deflate : Inflate.cs)
        public static Dictionary<string, byte[]> ReadZip(string path)
        {
            byte[] z = File.ReadAllBytes(path);
            var res = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            int eocd = -1;
            for (int o = z.Length - 22; o >= 0 && o >= z.Length - 22 - 65535; o--)
                if (BitConverter.ToUInt32(z, o) == 0x06054b50) { eocd = o; break; }
            if (eocd < 0) throw new Exception("zip illisible");
            int count = BitConverter.ToUInt16(z, eocd + 10), at = (int)BitConverter.ToUInt32(z, eocd + 16);
            for (int i = 0; i < count; i++)
            {
                if (BitConverter.ToUInt32(z, at) != 0x02014b50) throw new Exception("zip : repertoire abime");
                int method = BitConverter.ToUInt16(z, at + 10), csize = (int)BitConverter.ToUInt32(z, at + 20), usize = (int)BitConverter.ToUInt32(z, at + 24);
                int nl = BitConverter.ToUInt16(z, at + 28), el = BitConverter.ToUInt16(z, at + 30), cl = BitConverter.ToUInt16(z, at + 32);
                int local = (int)BitConverter.ToUInt32(z, at + 42);
                string name = Encoding.UTF8.GetString(z, at + 46, nl);
                at += 46 + nl + el + cl;
                if (name.EndsWith("/")) continue;
                int data = local + 30 + BitConverter.ToUInt16(z, local + 26) + BitConverter.ToUInt16(z, local + 28);
                byte[] outb = new byte[usize];
                if (method == 0) Buffer.BlockCopy(z, data, outb, 0, usize);
                else if (method == 8) outb = Inflate.Run(z, data, csize, usize);   // (pas DeflateStream : le Mono d'Unity 5.0 n'a pas zlib)
                else continue;
                res[name] = outb;
            }
            return res;
        }
    }
}
