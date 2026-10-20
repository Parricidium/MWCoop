using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace MWCoop
{
    // Export de la carte pour l'editeur de carte (demande de JD, 19/10 : « un programme hors du jeu, la carte importee selon
    // MSC ou MWC »). L'editeur demande au lanceur un lancement solo dans le profil isole « carte-export » (nouvelle partie,
    // jamais les sauvegardes du joueur), en arriere-plan, avec [Lancement] ExportCarte=<dossier>. Une fois GAME charge et
    // pose (ExportDelai, 20 s), le mod releve les maillages visibles et fixes, leurs matieres et leurs textures, ecrit le cache
    // puis ferme le jeu. Rien du jeu ne quitte le PC du joueur : le cache reste dans ses Documents.
    //
    // Cache (lu par l'editeur, editor/ui/carte.js) :
    //  - carte.json : jeu, version, sol, matieres (couleur, texture, decoupe, transparence), maillages (debut dans
    //    maillages.bin), objets (maillage, matrice 3x4 en lignes, matieres par sous-maillage, sorte : 0 sol, 1 routes,
    //    2 batiments et objets, 3 vegetation) ;
    //  - maillages.bin : par maillage, nombres de 4 octets : sommets n, drapeaux (1 normales, 2 uv), sous-maillages k, k
    //    tailles d'indices, puis positions (3n), normales (3n), uv (2n), indices (u32) ;
    //  - textures/<i>.png (au plus ExportTexture px, 512) ; etat.txt : avancement pour l'editeur (etape, avance, texte, fini).
    // Objets regroupes par le jeu (static batching, « Combined Mesh » en coordonnees monde) : seulement les sous-maillages de
    // l'objet (index interne staticBatchIndex, sinon ceux qui tiennent dans sa boite), matrice identite, chacun une fois.
    public static class MapExport
    {
        static string dir;
        static bool? on;
        public static bool Active
        {
            get
            {
                if (on == null)
                {
                    dir = Config.Get("Coop", "ExportCarte", Config.Get("Test", "ExportCarte", ""));
                    on = dir.Length > 0;
                    if (on.Value) Log.Info("export de la carte demande : " + dir);
                }
                return on.Value;
            }
        }

        static IEnumerator<bool> job;
        static float startAt = -1, quitAt = -1;
        static string lastStage;

        public static void Update()
        {
            if (!Active) return;
            AudioListener.volume = 0f;
            if (quitAt > 0)
            {
                if (Time.realtimeSinceStartup >= quitAt) { Log.Info("export de la carte : fermeture du jeu forcee"); System.Diagnostics.Process.GetCurrentProcess().Kill(); }
                return;
            }
            if (Game.LevelName != "GAME")
            {
                if (lastStage == null) State("chargement", 0f, "Chargement du jeu", false, null);
                return;
            }
            if (startAt < 0)
            {
                startAt = Time.realtimeSinceStartup + Config.GetFloat("Test", "ExportDelai", 20f);
                State("attente", 0f, "Monde en place, export dans quelques secondes", false, null);
            }
            if (job == null && Time.realtimeSinceStartup >= startAt) job = Run();
            if (job == null) return;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                while (sw.ElapsedMilliseconds < 250)
                    if (!job.MoveNext()) { job = null; Finish(null); break; }
            }
            catch (Exception e)
            {
                job = null;
                Log.Error("export de la carte : " + e);
                Finish(e.Message);
            }
        }

        static void Finish(string error)
        {
            State(error == null ? "fini" : "erreur", 1f, error ?? "Carte exportee", true, error);
            Log.Info("export de la carte : " + (error ?? "fini") + ", fermeture du jeu");
            // (fermeture directe : Application.Quit fait planter My Summer Car a la sortie, et Windows ajoute alors des
            // correctifs de compatibilite a l'exe -- le cache est deja ecrit et ferme)
            quitAt = Time.realtimeSinceStartup + 1f;
        }

        static void State(string stage, float progress, string text, bool done, string error)
        {
            lastStage = stage;
            try
            {
                Directory.CreateDirectory(dir);
                string p = Path.Combine(dir, "etat.txt"), tmp = p + ".tmp";
                File.WriteAllText(tmp, "etape=" + stage + "\navance=" + progress.ToString("0.000", CultureInfo.InvariantCulture) + "\ntexte=" + text
                                       + "\nfini=" + (done ? 1 : 0) + (error != null ? "\nerreur=" + error.Replace('\n', ' ') : "") + "\n", Encoding.UTF8);
                if (File.Exists(p)) File.Delete(p);
                File.Move(tmp, p);
            }
            catch (Exception e) { Log.Warn("export de la carte : etat.txt (" + e.Message + ")"); }
        }

        // ---------------------------------------------------------------- releve
        const int KGround = 0, KRoad = 1, KBuilding = 2, KPlant = 3;
        static readonly string[] KindStage = { "sol", "routes", "batiments", "vegetation" };
        static readonly string[] KindText = { "Sol et relief", "Routes et chemins", "Batiments et objets", "Arbres et vegetation" };

        struct Item { public MeshRenderer R; public Mesh M; public int Kind; }
        class Inst { public int Mesh; public Matrix4x4 T; public int[] Mats; public int Kind; public string Name; }

        static readonly Dictionary<int, int> meshIndex = new Dictionary<int, int>();       // maillage du jeu -> numero
        static readonly List<long> meshOffsets = new List<long>();
        static readonly List<string> meshNames = new List<string>();
        static readonly Dictionary<Material, int> matIndex = new Dictionary<Material, int>();
        static readonly List<Material> mats = new List<Material>();
        static readonly Dictionary<Texture, int> texIndex = new Dictionary<Texture, int>();
        static readonly List<Texture> texs = new List<Texture>();
        static readonly List<Inst> insts = new List<Inst>();
        static int unreadable, batchedMiss, batchedCollider;
        static readonly Dictionary<string, int> missNames = new Dictionary<string, int>(), dynNames = new Dictionary<string, int>();

        static string Top(Dictionary<string, int> d, int n)
        {
            var top = new List<KeyValuePair<string, int>>(d);
            top.Sort((a, b) => b.Value.CompareTo(a.Value));
            var sb = new StringBuilder();
            for (int i = 0; i < top.Count && i < n; i++) sb.Append(i > 0 ? ", " : "").Append(top[i].Key).Append(" x").Append(top[i].Value);
            return sb.ToString();
        }
        static BinaryWriter bin;

        static IEnumerator<bool> Run()
        {
            var total = System.Diagnostics.Stopwatch.StartNew();
            State("releve", 0f, "Releve des objets", false, null);
            yield return true;

            // LOD : seulement le niveau le plus detaille (GetLODs n'existe pas dans Unity 5.0 : par reflexion si present, sinon
            // les noms en ..LOD1, ..LOD2)
            var lodSkip = new HashSet<Renderer>();
            MethodInfo getLods = typeof(LODGroup).GetMethod("GetLODs", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            int groups = 0;
            // Objets de la scene, allumes ou non : My Summer Car eteint la plus grande partie de la carte loin du joueur (releve du
            // 19/10 : YARD, STORE, JOBS, PERAJARVI... plus de 8000 objets eteints, 1449 allumes).
            var roots = Game.RootSet(true);
            foreach (UnityEngine.Object o in Resources.FindObjectsOfTypeAll(typeof(LODGroup)))
            {
                var g = (LODGroup)o;
                if (g.hideFlags != HideFlags.None || !roots.Contains(g.transform.root)) continue;
                groups++;
                LOD[] lods = null;
                try { if (getLods != null) lods = (LOD[])getLods.Invoke(g, null); } catch { }
                if (lods != null) { for (int i = 1; i < lods.Length; i++) foreach (Renderer r in lods[i].renderers) if (r != null) lodSkip.Add(r); }
                else foreach (Renderer r in g.GetComponentsInChildren<Renderer>(true)) if (LodName(r.name) > 0) lodSkip.Add(r);
            }
            Log.Info("export de la carte : " + groups + " groupes LOD (" + (getLods != null ? "par l'API" : "par les noms") + "), " + lodSkip.Count + " niveaux ecartes");
            var items = new List<Item>();
            int skipped = 0, dynamic = 0, off = 0;
            var kindNames = new Dictionary<string, int>[4];
            for (int k = 0; k < 4; k++) kindNames[k] = new Dictionary<string, int>();
            var rootSkip = new Dictionary<string, int>();
            foreach (UnityEngine.Object o in Resources.FindObjectsOfTypeAll(typeof(MeshRenderer)))
            {
                var r = (MeshRenderer)o;
                if (r.hideFlags != HideFlags.None || !roots.Contains(r.transform.root)) continue;
                if (!r.enabled || lodSkip.Contains(r) || r.gameObject.layer == 31) { skipped++; continue; }
                string root = Game.RootName(r.transform);
                if (SkipRoot(root)) { int rc; rootSkip.TryGetValue(root, out rc); rootSkip[root] = rc + 1; continue; }
                Rigidbody rb = null;   // (GetComponentInParent ne voit pas les objets eteints)
                for (Transform t = r.transform; t != null && rb == null; t = t.parent) rb = t.GetComponent<Rigidbody>();
                if (rb != null) { dynamic++; int dc; dynNames.TryGetValue(rb.name, out dc); dynNames[rb.name] = dc + 1; continue; }   // (voitures, objets, portes : bougent)
                MeshFilter mf = r.GetComponent<MeshFilter>();
                Mesh m = mf != null ? mf.sharedMesh : null;
                if (m == null) { skipped++; continue; }
                if (!m.name.StartsWith("Combined Mesh"))
                {
                    Vector3 s = Vector3.Scale(m.bounds.size, r.transform.lossyScale);
                    if (Mathf.Max(Mathf.Abs(s.x), Mathf.Max(Mathf.Abs(s.y), Mathf.Abs(s.z))) > 12000f) { Log.Info("export de la carte : ecarte (trop grand) " + Recon.Path(r.transform) + " " + s.ToString("F0")); skipped++; continue; }
                }
                if (!r.gameObject.activeInHierarchy) off++;
                int kind = KindOf(r, m);
                items.Add(new Item { R = r, M = m, Kind = kind });
                string n = r.name; int c; kindNames[kind].TryGetValue(n, out c); kindNames[kind][n] = c + 1;
            }
            items.Sort((a, b) => a.Kind.CompareTo(b.Kind));
            Log.Info("export de la carte : mobiles ecartes : " + Top(dynNames, 30));
            Log.Info("export de la carte : racines ecartees : " + Top(rootSkip, 30) + " ; objets eteints gardes : " + off);
            int[] counts = new int[4];
            foreach (Item it in items) counts[it.Kind]++;
            Log.Info("export de la carte : " + items.Count + " objets (sol " + counts[0] + ", routes " + counts[1] + ", batiments " + counts[2] + ", vegetation " + counts[3]
                     + "), ecartes " + skipped + ", mobiles " + dynamic);
            for (int k = 0; k < 4; k++)
            {
                var top = new List<KeyValuePair<string, int>>(kindNames[k]);
                top.Sort((a, b) => b.Value.CompareTo(a.Value));
                var sb = new StringBuilder();
                for (int i = 0; i < top.Count && i < 25; i++) sb.Append(i > 0 ? ", " : "").Append(top[i].Key).Append(" x").Append(top[i].Value);
                Log.Info("export de la carte : " + KindStage[k] + " : " + sb);
            }
            yield return true;

            Directory.CreateDirectory(dir);
            string binPath = Path.Combine(dir, "maillages.bin");
            OpenScene();
            using (bin = new BinaryWriter(File.Create(binPath)))
            {
                for (int i = 0; i < items.Count; i++)
                {
                    Item it = items[i];
                    if (i == 0 || items[i - 1].Kind != it.Kind || i % 50 == 0)
                        State(KindStage[it.Kind], 0.05f + 0.6f * i / Mathf.Max(1, items.Count), KindText[it.Kind], false, null);
                    if (it.R == null) continue;
                    try { AddRenderer(it); } catch (Exception e) { if (unreadable++ < 20) Log.Warn("export de la carte : " + Recon.Path(it.R.transform) + " : " + e.Message); }
                    yield return true;
                }
                // objets regroupes par le jeu : lus dans le fichier de la scene, sinon leur maillage de collision
                for (int i = 0; i < pending.Count; i++)
                {
                    Item it = pending[i];
                    if (i % 50 == 0) State(KindStage[it.Kind], 0.65f + 0.15f * i / Mathf.Max(1, pending.Count), KindText[it.Kind], false, null);
                    if (it.R == null) continue;
                    try { AddBatched(it); } catch (Exception e) { if (unreadable++ < 20) Log.Warn("export de la carte : " + Recon.Path(it.R.transform) + " (lot) : " + e.Message); }
                    yield return true;
                }
            }
            bin = null;
            if (scene != null) { scene.Close(); scene = null; }
            Log.Info("export de la carte : " + insts.Count + " objets, " + meshOffsets.Count + " maillages, " + mats.Count + " matieres, " + texs.Count + " textures ; regroupes : "
                     + batchedFile + " lus dans la scene, " + batchedCollider + " par la collision, " + batchedMiss + " introuvables ; illisibles " + unreadable);
            Log.Info("export de la carte : regroupes introuvables : " + Top(missNames, 40));

            // textures
            string texDir = Path.Combine(dir, "textures");
            Directory.CreateDirectory(texDir);
            int max = Config.GetInt("Test", "ExportTexture", 512), texOk = 0;
            for (int i = 0; i < texs.Count; i++)
            {
                if (i % 10 == 0) State("textures", 0.8f + 0.15f * i / Mathf.Max(1, texs.Count), "Textures et materiaux", false, null);
                try { if (SaveTexture(texs[i], Path.Combine(texDir, i + ".png"), max)) texOk++; }
                catch (Exception e) { Log.Warn("export de la carte : texture " + texs[i].name + " : " + e.Message); }
                yield return true;
            }
            Log.Info("export de la carte : " + texOk + "/" + texs.Count + " textures ecrites");

            State("cache", 0.96f, "Mise en cache", false, null);
            yield return true;
            WriteJson(Path.Combine(dir, "carte.json"));
            Log.Info("export de la carte : ecrit en " + (total.ElapsedMilliseconds / 1000f).ToString("F1") + " s, maillages.bin " + (new FileInfo(binPath).Length / 1048576) + " Mo");
        }

        // ..._LOD2 -> 2 ; 0 sinon
        static int LodName(string n)
        {
            int i = n.LastIndexOf("LOD", StringComparison.OrdinalIgnoreCase);
            if (i < 0 || i + 3 >= n.Length) return 0;
            int v;
            return int.TryParse(n.Substring(i + 3), out v) ? v : 0;
        }

        // Racines sans decor : joueur, interface, systemes, circulation, PNJ, objets et pieces libres, le mod.
        static readonly HashSet<string> SkipRoots = new HashSet<string> { "PLAYER", "GUI", "Systems", "Sheets", "TRAFFIC", "NPC_CARS", "ITEMS", "CARPARTS", "COPS", "Clouds" };
        static bool SkipRoot(string root) { return SkipRoots.Contains(root) || root.StartsWith("MWCoop") || root.StartsWith("Sky"); }

        static int KindOf(MeshRenderer r, Mesh mesh)
        {
            string path = Recon.Path(r.transform), low = path.ToLowerInvariant(), n = r.name.ToLowerInvariant();
            Material m = r.sharedMaterial;
            string sh = m != null && m.shader != null ? m.shader.name.ToLowerInvariant() : "";
            if (low.Contains("terrain") || n.Contains("lake") || n.Contains("water") || n.Contains("lakebed") || n.Contains("swamp") || n.StartsWith("ground")) return KGround;
            if (n.Contains("road") || n.Contains("asphalt") || n.Contains("4tie") || n.Contains("gravel") || n.Contains("highway") || n.Contains("bridge") || n.Contains("pavement")
                || n.Contains("rail") || n.Contains("roadside") || n.Contains("airport") || n.Contains("trackfield")) return KRoad;
            if (sh.Contains("leaves") || sh.Contains("nature") || sh.Contains("tree") || n.Contains("tree") || n.Contains("spruce") || n.Contains("pine") || n.Contains("birch")
                || n.Contains("bush") || n.Contains("foliage") || n.Contains("forest") || n.Contains("plant") || n.Contains("reed") || n.Contains("leaf") || n.Contains("grass")) return KPlant;
            if (!mesh.name.StartsWith("Combined Mesh"))   // (grandes etendues plates : le sol ; boite du maillage, celle de l'objet est vide s'il est eteint)
            {
                Vector3 s = Vector3.Scale(mesh.bounds.size, r.transform.lossyScale);
                s = new Vector3(Mathf.Abs(s.x), Mathf.Abs(s.y), Mathf.Abs(s.z));
                if (Mathf.Max(s.x, s.z) > 250f && s.y < Mathf.Max(s.x, s.z) * 0.4f) return KGround;
            }
            return KBuilding;
        }

        static int MatOf(Material m)
        {
            if (m == null) return -1;
            int i;
            if (matIndex.TryGetValue(m, out i)) return i;
            i = mats.Count; mats.Add(m); matIndex[m] = i;
            Texture t = m.HasProperty("_MainTex") ? m.mainTexture : null;
            if (t is Texture2D && !texIndex.ContainsKey(t)) { texIndex[t] = texs.Count; texs.Add(t); }
            return i;
        }

        static void AddRenderer(Item it)
        {
            MeshRenderer r = it.R;
            Mesh mesh = it.M;
            Material[] rm = r.sharedMaterials;
            int[] ml = new int[rm.Length];
            for (int i = 0; i < rm.Length; i++) ml[i] = MatOf(rm[i]);
            if (mesh.name.StartsWith("Combined Mesh")) { pending.Add(it); return; }
            int id = mesh.GetInstanceID(), mi;
            if (!meshIndex.TryGetValue(id, out mi))
            {
                var all = new List<int>();
                for (int i = 0; i < mesh.subMeshCount; i++) all.Add(i);
                mi = WriteMesh(mesh, all, mesh.name);
                meshIndex[id] = mi;
            }
            if (mi < 0) return;
            insts.Add(new Inst { Mesh = mi, T = r.localToWorldMatrix, Mats = ml, Kind = it.Kind, Name = r.name });
        }

        // ---------------------------------------------------------------- objets regroupes (lots statiques)
        static readonly List<Item> pending = new List<Item>();
        static SceneFile scene;
        static Dictionary<string, List<SceneFile.Batched>> sceneByKey, sceneByName;
        static readonly Dictionary<long, SceneFile.MeshData> sceneMeshes = new Dictionary<long, SceneFile.MeshData>();
        static int batchedFile, sceneBad;

        static void OpenScene()
        {
            // Unity 5.0 : la 1re scene est dans mainData, la scene n dans level(n-1) (GAME de MSC : scene 3, level2) ; sinon
            // le premier fichier levelN qui a des objets regroupes
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var tries = new List<string> { "level" + (Application.loadedLevel - 1), "level" + Application.loadedLevel };
            for (int i = 0; i < 12; i++) if (!tries.Contains("level" + i)) tries.Add("level" + i);
            SceneFile f = null; List<SceneFile.Batched> list = null; string p = null;
            foreach (string t in tries)
            {
                string tp = Path.Combine(Application.dataPath, t);
                if (!File.Exists(tp)) continue;
                var cf = new SceneFile(tp);
                List<SceneFile.Batched> cl = null;
                try { if (cf.Open()) cl = cf.ReadBatched(); else Log.Warn("export de la carte : " + t + " illisible (" + cf.Error + ")"); }
                catch (Exception e) { Log.Warn("export de la carte : " + t + " : " + e.Message); }
                if (cl != null && cl.Count > 0) { f = cf; list = cl; p = tp; break; }
                cf.Close();
            }
            if (f == null) { Log.Warn("export de la carte : aucun fichier de scene avec des objets regroupes : lots par la collision seulement"); return; }
            scene = f;
            sceneByKey = new Dictionary<string, List<SceneFile.Batched>>();
            sceneByName = new Dictionary<string, List<SceneFile.Batched>>();
            foreach (SceneFile.Batched b in list)
            {
                Add(sceneByKey, BatchKey(b.Name + "|" + b.MeshName, b.Mats.Length > 0 ? b.Mats[0] : "", b.Subsets.Length), b);
                Add(sceneByName, b.Name + "|" + b.MeshName + "|" + b.Subsets.Length, b);
            }
            Log.Info("export de la carte : scene " + Path.GetFileName(p) + " : " + list.Count + " objets regroupes, " + f.Count(43) + " maillages (" + sw.ElapsedMilliseconds + " ms)");
        }

        static void Add(Dictionary<string, List<SceneFile.Batched>> d, string k, SceneFile.Batched b) { List<SceneFile.Batched> l; if (!d.TryGetValue(k, out l)) d[k] = l = new List<SceneFile.Batched>(); l.Add(b); }
        static string BatchKey(string name, string mat, int n) { return name + "|" + mat + "|" + n; }
        static SceneFile.Batched Take(Dictionary<string, List<SceneFile.Batched>> d, string k)
        {
            List<SceneFile.Batched> l;
            if (d == null || !d.TryGetValue(k, out l)) return null;
            foreach (SceneFile.Batched b in l) if (!b.Used) { b.Used = true; return b; }
            return null;
        }

        static void AddBatched(Item it)
        {
            MeshRenderer r = it.R;
            Material[] rm = r.sharedMaterials;
            int[] ml = new int[rm.Length];
            for (int i = 0; i < rm.Length; i++) ml[i] = MatOf(rm[i]);
            if (scene != null)
            {
                string m0 = rm.Length > 0 && rm[0] != null ? rm[0].name.Replace(" (Instance)", "") : "";
                // (meme objet : son nom, celui du lot -- deux lots ont des objets de meme nom --, sa 1re matiere, leur nombre)
                SceneFile.Batched b = Take(sceneByKey, BatchKey(r.name + "|" + it.M.name, m0, rm.Length)) ?? Take(sceneByName, r.name + "|" + it.M.name + "|" + rm.Length);
                if (b != null)
                {
                    SceneFile.MeshData md;
                    if (!sceneMeshes.TryGetValue(b.MeshPath * 64 + b.MeshFile, out md))
                    {
                        md = scene.ReadMesh(b.MeshFile, b.MeshPath);
                        if (md != null && (md.VertexCount != it.M.vertexCount || md.Subs.Length != it.M.subMeshCount))
                        {
                            if (sceneBad++ < 5) Log.Warn("export de la carte : lot " + md.Name + " du fichier : " + md.VertexCount + " sommets / " + md.Subs.Length + " sous-maillages, le jeu en a " + it.M.vertexCount + " / " + it.M.subMeshCount + " : ignore");
                            md = null;
                        }
                        else if (md == null && sceneBad++ < 5) Log.Warn("export de la carte : lot " + it.M.name + " : lecture du fichier impossible");
                        sceneMeshes[b.MeshPath * 64 + b.MeshFile] = md;
                    }
                    if (md != null)
                    {
                        var tris = new List<int[]>();
                        foreach (int s in b.Subsets) if (s >= 0 && s < md.Subs.Length) tris.Add(md.Subs[s]);
                        int idx = WriteArrays(md.V, md.N, md.Uv, tris, r.name);
                        if (idx >= 0)
                        {
                            batchedFile++;
                            insts.Add(new Inst { Mesh = idx, T = Matrix4x4.identity, Mats = ml, Kind = it.Kind, Name = r.name });
                            return;
                        }
                    }
                }
            }

            // Lot illisible (Unity 5.0 : aucun acces aux tampons de la carte graphique) : le maillage de collision du meme
            // objet est souvent le maillage d'origine, lisible (la physique en garde une copie) -- uv et sous-maillages compris.
            MeshCollider mc = r.GetComponent<MeshCollider>();
            Mesh cm = mc != null ? mc.sharedMesh : null;
            if (cm != null && !cm.name.StartsWith("Combined Mesh"))
            {
                int cid = cm.GetInstanceID(), ci;
                if (!meshIndex.TryGetValue(cid, out ci))
                {
                    var all = new List<int>();
                    for (int i = 0; i < cm.subMeshCount; i++) all.Add(i);
                    ci = WriteMesh(cm, all, cm.name);
                    meshIndex[cid] = ci;
                }
                if (ci >= 0)
                {
                    batchedCollider++;
                    insts.Add(new Inst { Mesh = ci, T = r.localToWorldMatrix, Mats = ml, Kind = it.Kind, Name = r.name });
                    return;
                }
            }
            batchedMiss++;
            int c; missNames.TryGetValue(r.name, out c); missNames[r.name] = c + 1;
        }

        // Ecrit les sous-maillages 'subs' (sommets renumerotes) dans maillages.bin ; numero du maillage, -1 si illisible.
        static int WriteMesh(Mesh mesh, List<int> subs, string name)
        {
            Vector3[] v; Vector3[] nr; Vector2[] uv;
            try { v = mesh.vertices; nr = mesh.normals; uv = mesh.uv; }
            catch { unreadable++; return -1; }
            if (v == null || v.Length == 0) { unreadable++; return -1; }
            var all = new List<int[]>();
            foreach (int s in subs) all.Add(mesh.GetTriangles(s));
            return WriteArrays(v, nr, uv, all, name);
        }

        static int WriteArrays(Vector3[] v, Vector3[] nr, Vector2[] uv, List<int[]> subs, string name)
        {
            bool hasN = nr != null && nr.Length == v.Length, hasUv = uv != null && uv.Length == v.Length;
            var map = new Dictionary<int, int>();
            var order = new List<int>();
            var tris = new List<int[]>();
            foreach (int[] t in subs)
            {
                var o = new int[t.Length];
                for (int i = 0; i < t.Length; i++)
                {
                    int j;
                    if (!map.TryGetValue(t[i], out j)) { j = order.Count; map[t[i]] = j; order.Add(t[i]); }
                    o[i] = j;
                }
                tris.Add(o);
            }
            if (order.Count == 0) return -1;
            int idx = meshOffsets.Count;
            meshOffsets.Add(bin.BaseStream.Position);
            meshNames.Add(name);
            bin.Write(order.Count);
            bin.Write((hasN ? 1 : 0) | (hasUv ? 2 : 0));
            bin.Write(tris.Count);
            foreach (int[] t in tris) bin.Write(t.Length);
            foreach (int k in order) { Vector3 p = v[k]; bin.Write(p.x); bin.Write(p.y); bin.Write(p.z); }
            if (hasN) foreach (int k in order) { Vector3 p = nr[k]; bin.Write(p.x); bin.Write(p.y); bin.Write(p.z); }
            if (hasUv) foreach (int k in order) { Vector2 p = uv[k]; bin.Write(p.x); bin.Write(p.y); }
            foreach (int[] t in tris) foreach (int k in t) bin.Write(k);
            return idx;
        }

        // Texture du jeu (illisible directement) : copiee sur une texture de rendu reduite, relue, PNG.
        static bool SaveTexture(Texture t, string path, int max)
        {
            if (t == null) return false;
            int w = t.width, h = t.height;
            while (w > max || h > max) { w = Mathf.Max(1, w / 2); h = Mathf.Max(1, h / 2); }
            var rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32);
            RenderTexture prev = RenderTexture.active;
            Graphics.Blit(t, rt);
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.ARGB32, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            File.WriteAllBytes(path, tex.EncodeToPNG());
            UnityEngine.Object.Destroy(tex);
            return true;
        }

        // ---------------------------------------------------------------- carte.json
        static string F(float f) { return float.IsNaN(f) || float.IsInfinity(f) ? "0" : f.ToString("R", CultureInfo.InvariantCulture); }
        static string S(string s)
        {
            var sb = new StringBuilder("\"");
            foreach (char c in s ?? "")
            {
                if (c == '"' || c == '\\') sb.Append('\\').Append(c);
                else if (c < 32) sb.Append("\\u").Append(((int)c).ToString("x4"));
                else sb.Append(c);
            }
            return sb.Append('"').ToString();
        }

        static void WriteJson(string path)
        {
            var sb = new StringBuilder(1 << 20);
            sb.Append("{\"format\":1,\"jeu\":").Append(S(Game.Msc ? "msc" : "mwc")).Append(",\"nom\":").Append(S(Game.Name));
            sb.Append(",\"mod\":").Append(S(Version.Text)).Append(",\"date\":").Append(S(DateTime.Now.ToString("yyyy-MM-dd HH:mm")));
            Transform pl = Game.PlayerT;
            if (pl != null) sb.Append(",\"joueur\":[").Append(F(pl.position.x)).Append(',').Append(F(pl.position.y)).Append(',').Append(F(pl.position.z)).Append(']');
            Bounds all = new Bounds(); bool any = false;
            foreach (Inst i in insts) { Vector3 p = i.T.GetColumn(3); if (!any) { all = new Bounds(p, Vector3.zero); any = true; } else all.Encapsulate(p); }
            sb.Append(",\"bornes\":[").Append(F(all.min.x)).Append(',').Append(F(all.min.y)).Append(',').Append(F(all.min.z)).Append(',')
              .Append(F(all.max.x)).Append(',').Append(F(all.max.y)).Append(',').Append(F(all.max.z)).Append(']');
            sb.Append(",\"textures\":").Append(texs.Count);
            sb.Append(",\"matieres\":[");
            for (int i = 0; i < mats.Count; i++)
            {
                Material m = mats[i];
                Color c = m.HasProperty("_Color") ? m.color : Color.white;
                Texture t = m.HasProperty("_MainTex") ? m.mainTexture : null;
                int ti; if (t == null || !texIndex.TryGetValue(t, out ti)) ti = -1;
                string sh = m.shader != null ? m.shader.name : "";
                string shl = sh.ToLowerInvariant();
                bool cut = (m.HasProperty("_Cutoff") && (shl.Contains("cutout") || shl.Contains("leaves") || shl.Contains("tree"))) || m.IsKeywordEnabled("_ALPHATEST_ON");
                bool transp = !cut && (shl.Contains("transparent") || m.renderQueue >= 3000);
                Vector2 sc = t != null ? m.mainTextureScale : Vector2.one, of = t != null ? m.mainTextureOffset : Vector2.zero;
                if (i > 0) sb.Append(',');
                sb.Append("{\"n\":").Append(S(m.name)).Append(",\"s\":").Append(S(sh)).Append(",\"c\":[").Append(F(c.r)).Append(',').Append(F(c.g)).Append(',').Append(F(c.b)).Append(',').Append(F(c.a)).Append(']');
                if (ti >= 0) sb.Append(",\"t\":").Append(ti).Append(",\"r\":[").Append(F(sc.x)).Append(',').Append(F(sc.y)).Append(',').Append(F(of.x)).Append(',').Append(F(of.y)).Append(']');
                if (cut) sb.Append(",\"d\":").Append(F(m.HasProperty("_Cutoff") ? m.GetFloat("_Cutoff") : 0.5f));
                if (transp) sb.Append(",\"a\":1");
                sb.Append('}');
            }
            sb.Append("],\"maillages\":[");
            for (int i = 0; i < meshOffsets.Count; i++) sb.Append(i > 0 ? "," : "").Append(meshOffsets[i]);
            sb.Append("],\"objets\":[");
            for (int i = 0; i < insts.Count; i++)
            {
                Inst o = insts[i];
                if (i > 0) sb.Append(',');
                sb.Append("{\"m\":").Append(o.Mesh).Append(",\"k\":").Append(o.Kind).Append(",\"n\":").Append(S(o.Name)).Append(",\"t\":[");
                for (int rr = 0; rr < 3; rr++) for (int cc = 0; cc < 4; cc++) sb.Append(rr + cc > 0 ? "," : "").Append(F(o.T[rr, cc]));
                sb.Append("],\"x\":[");
                for (int k = 0; k < o.Mats.Length; k++) sb.Append(k > 0 ? "," : "").Append(o.Mats[k]);
                sb.Append("]}");
            }
            sb.Append("]}");
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, sb.ToString(), new UTF8Encoding(false));
            if (File.Exists(path)) File.Delete(path);
            File.Move(tmp, path);
        }
    }
}
