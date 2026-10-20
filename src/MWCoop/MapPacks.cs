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
            root = null; Objects = Hidden = Packs = 0; job = null;
            loadAt = Game.LevelName == "GAME" && !MapExport.Active ? Time.realtimeSinceStartup + Config.GetFloat("Test", "CartesDelai", 6f) : -1;
            if (loadAt < 0) return;
            string d = LoadDir(false);
            if (d != null && Directory.Exists(d) && Directory.GetFiles(d, "*.mwcmap").Length > 0) SceneBatches.Prefetch();   // (copies d'objets regroupes)
        }

        // Chargement reparti sur plusieurs images (CartesBudget ms par image, 4 par defaut) : plus d'a-coup a l'arrivee des
        // cartes (0,36 s d'un bloc avant le 20/10).
        public static void Update()
        {
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
            roots = null; meshCache.Clear(); gameTex = null; gameMat = null; packTex.Clear();
            Array.Sort(files, StringComparer.OrdinalIgnoreCase);
            foreach (string f in files)
            {
                Pack p = Begin(f);
                if (p == null) continue;
                foreach (string l in p.Lines) { Line(p, l); frames++; yield return true; }
                End(p);
                Packs++;
            }
            SceneBatches.Close();
            TestPhoto();
            Log.Info("cartes : " + Packs + "/" + files.Length + " carte(s), " + Objects + " objet(s) ajoute(s), " + Hidden + " objet(s) du jeu masque(s) en " + sw.ElapsedMilliseconds + " ms (" + frames + " etapes)");
        }

        // ---------------------------------------------------------------- un paquet
        class Part { public Mesh Mesh; public Material[] Mats; }
        class Pack
        {
            public string Name, File; public Dictionary<string, byte[]> Z; public string[] Lines;
            public Dictionary<string, Part> Parts = new Dictionary<string, Part>(); public List<string[]> MatLines = new List<string[]>();
            public GameObject Group, Last; public int Objs, Missing, Hid;
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
                    GameObject go = f[1] == "jeu" ? GameCopy(f[2], f.Length > 8 ? f[8] : null) : ModelObject(f[2], p.Parts);
                    if (go == null) { if (p.Missing++ < 10) Log.Warn("cartes : " + p.Name + " : " + f[2] + " introuvable ici"); return; }
                    go.name = f.Length > 7 ? f[7] : go.name;
                    go.transform.parent = p.Group.transform;
                    go.transform.position = V3(f[3]);
                    go.transform.rotation = Q(f[4]);
                    go.transform.localScale = V3(f[5]);
                    Collide(go, f[6]);
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
                else if (f[0] == "masque" && f.Length >= 2) { if (Hide(f[1])) p.Hid++; }
            }
            catch (Exception e) { if (p.Missing++ < 10) Log.Warn("cartes : " + p.Name + " : ligne « " + f[0] + " » : " + e.Message); }
        }

        static void End(Pack p)
        {
            Objects += p.Objs; Hidden += p.Hid;
            Log.Info("cartes : " + p.Name + " : " + p.Objs + " objet(s), " + p.Hid + " masque(s)" + (p.Missing > 0 ? ", " + p.Missing + " introuvable(s)" : ""));
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

        static GameObject GameCopy(string path, string center)
        {
            Transform t = Resolve(path);
            MeshRenderer r = t != null ? t.GetComponent<MeshRenderer>() : null;
            MeshFilter mf = t != null ? t.GetComponent<MeshFilter>() : null;
            if (r == null || mf == null || mf.sharedMesh == null) return null;
            Mesh mesh;
            if (!meshCache.TryGetValue(path + "|" + center, out mesh))
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
        static Texture2D GameTex(string name)
        {
            if (gameTex == null)
            {
                gameTex = new Dictionary<string, Texture2D>();
                foreach (Texture2D t in Resources.FindObjectsOfTypeAll<Texture2D>()) if (t != null && !gameTex.ContainsKey(t.name)) gameTex[t.name] = t;
            }
            Texture2D r; return gameTex.TryGetValue(name, out r) ? r : null;
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
