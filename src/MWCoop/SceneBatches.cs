using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace MWCoop
{
    // Objets regroupes par le jeu (lots statiques, « Combined Mesh » illisibles en Unity 5.0) : leurs sous-maillages relus
    // dans le fichier de la scene (SceneFile.cs). Commun a l'export de la carte (MapExport.cs) et aux cartes de l'editeur
    // (MapPacks.cs : copies d'objets du jeu). Un objet regroupe est retrouve par son nom, celui de son lot, sa 1re matiere
    // et leur nombre (deux lots ont des objets de meme nom) ; chaque objet du fichier ne sert qu'a un objet du jeu.
    public static class SceneBatches
    {
        static SceneFile scene;
        static Dictionary<string, List<SceneFile.Batched>> byKey, byName;
        static Dictionary<string, SceneFile.Batched> byPath;   // chemin unique (le plus sur : deux objets d'un lot ont souvent meme nom et memes matieres)
        static readonly Dictionary<long, SceneFile.MeshData> meshes = new Dictionary<long, SceneFile.MeshData>();
        static readonly Dictionary<int, SceneFile.Batched> found = new Dictionary<int, SceneFile.Batched>();
        static int bad;
        public static int paths;   // (objets retrouves par leur chemin : journal)
        public static bool IsOpen { get { return scene != null; } }

        // Unity 5.0 : la 1re scene est dans mainData, la scene n dans level(n-1) (GAME de MSC : scene 3, level2) ; sinon
        // le premier fichier levelN qui a des objets regroupes.
        static List<string> Tries()
        {
            var names = new List<string> { "level" + (Application.loadedLevel - 1), "level" + Application.loadedLevel };
            for (int i = 0; i < 12; i++) if (!names.Contains("level" + i)) names.Add("level" + i);
            var l = new List<string>();
            foreach (string n in names) l.Add(Path.Combine(Application.dataPath, n));
            return l;
        }

        public static bool Open(string who)
        {
            if (scene != null) return true;
            if (pre != null)   // (lue en arriere-plan : Prefetch)
            {
                pre.Join(); pre = null;
                if (preScene != null)
                {
                    Adopt(preScene, preList);
                    Log.Info(who + " : scene " + Path.GetFileName(preScene.FilePath) + " lue en arriere-plan : " + preList.Count + " objets regroupes (" + preMs + " ms)");
                    preScene = null; preList = null;
                    return true;
                }
            }
            var sw = System.Diagnostics.Stopwatch.StartNew();
            foreach (string tp in Tries())
            {
                string t = Path.GetFileName(tp);
                if (!File.Exists(tp)) continue;
                var cf = new SceneFile(tp);
                List<SceneFile.Batched> cl = null;
                try { if (cf.Open()) cl = cf.ReadBatched(); else Log.Warn(who + " : " + t + " illisible (" + cf.Error + ")"); }
                catch (Exception e) { Log.Warn(who + " : " + t + " : " + e.Message); }
                if (cl == null || cl.Count == 0) { cf.Close(); continue; }
                Adopt(cf, cl);
                Log.Info(who + " : scene " + t + " : " + cl.Count + " objets regroupes, " + cf.Count(43) + " maillages (" + sw.ElapsedMilliseconds + " ms)");
                return true;
            }
            Log.Warn(who + " : aucun fichier de scene avec des objets regroupes");
            return false;
        }

        static void Adopt(SceneFile cf, List<SceneFile.Batched> cl)
        {
            scene = cf;
            byKey = new Dictionary<string, List<SceneFile.Batched>>();
            byName = new Dictionary<string, List<SceneFile.Batched>>();
            byPath = new Dictionary<string, SceneFile.Batched>();
            foreach (SceneFile.Batched b in cl)
            {
                if (b.Path != null && !byPath.ContainsKey(b.Path)) byPath[b.Path] = b;
                Add(byKey, Key(b.Name + "|" + b.MeshName, b.Mats.Length > 0 ? b.Mats[0] : "", b.Subsets.Length), b);
                Add(byName, b.Name + "|" + b.MeshName + "|" + b.Subsets.Length, b);
            }
        }

        // Cartes de l'editeur : le fichier de la scene lu sur un autre fil des le chargement de GAME (0,2 a 0,3 s qui
        // figeaient le jeu a l'arrivee des cartes) ; Open le reprend (ou attend la fin de la lecture).
        static System.Threading.Thread pre;
        static SceneFile preScene;
        static List<SceneFile.Batched> preList;
        static long preMs;
        public static void Prefetch()
        {
            if (scene != null || pre != null) return;
            List<string> tries = Tries();
            pre = new System.Threading.Thread(() =>
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                foreach (string tp in tries)
                {
                    if (!File.Exists(tp)) continue;
                    var cf = new SceneFile(tp);
                    List<SceneFile.Batched> cl = null;
                    try { if (cf.Open()) cl = cf.ReadBatched(); } catch { }
                    if (cl == null || cl.Count == 0) { cf.Close(); continue; }
                    preScene = cf; preList = cl;
                    break;
                }
                preMs = sw.ElapsedMilliseconds;
            });
            pre.IsBackground = true;
            pre.Start();
        }

        public static void Close()
        {
            if (pre != null) { pre.Join(); pre = null; }
            if (preScene != null) { preScene.Close(); preScene = null; preList = null; }
            if (found.Count > 0) Log.Info("lots statiques : " + found.Count + " objet(s) cherche(s), " + paths + " retrouve(s) par leur chemin");
            paths = 0;
            if (scene != null) scene.Close();
            scene = null; byKey = byName = null; byPath = null;
            meshes.Clear(); found.Clear(); bad = 0;
        }

        static void Add(Dictionary<string, List<SceneFile.Batched>> d, string k, SceneFile.Batched b) { List<SceneFile.Batched> l; if (!d.TryGetValue(k, out l)) d[k] = l = new List<SceneFile.Batched>(); l.Add(b); }
        static string Key(string name, string mat, int n) { return name + "|" + mat + "|" + n; }
        static SceneFile.Batched Take(Dictionary<string, List<SceneFile.Batched>> d, string k)
        {
            List<SceneFile.Batched> l;
            if (d == null || !d.TryGetValue(k, out l)) return null;
            foreach (SceneFile.Batched b in l) if (!b.Used) { b.Used = true; return b; }
            return null;
        }

        // Sous-maillages (sommets en coordonnees monde) de l'objet regroupe r ; faux si introuvable ou si le fichier ne
        // correspond pas au jeu (nombre de sommets ou de sous-maillages du lot).
        public static bool Find(MeshRenderer r, Mesh combined, out SceneFile.MeshData md, out int[] subsets)
        {
            md = null; subsets = null;
            if (scene == null || r == null || combined == null) return false;
            SceneFile.Batched b;
            if (!found.TryGetValue(r.GetInstanceID(), out b))
            {
                Material[] rm = r.sharedMaterials;
                string m0 = rm.Length > 0 && rm[0] != null ? rm[0].name.Replace(" (Instance)", "") : "";
                SceneFile.Batched bp;
                if (byPath.TryGetValue(MapExport.UniquePath(r.transform), out bp) && !bp.Used && bp.MeshName == combined.name) { bp.Used = true; b = bp; paths++; }
                else b = Take(byKey, Key(r.name + "|" + combined.name, m0, rm.Length)) ?? Take(byName, r.name + "|" + combined.name + "|" + rm.Length);
                found[r.GetInstanceID()] = b;
            }
            if (b == null) return false;
            long mk = b.MeshPath * 64 + b.MeshFile;
            if (!meshes.TryGetValue(mk, out md))
            {
                md = scene.ReadMesh(b.MeshFile, b.MeshPath);
                if (md != null && (md.VertexCount != combined.vertexCount || md.Subs.Length != combined.subMeshCount))
                {
                    if (bad++ < 5) Log.Warn("lot " + md.Name + " du fichier : " + md.VertexCount + " sommets / " + md.Subs.Length + " sous-maillages, le jeu en a " + combined.vertexCount + " / " + combined.subMeshCount + " : ignore");
                    md = null;
                }
                else if (md == null && bad++ < 5) Log.Warn("lot " + combined.name + " : lecture du fichier impossible");
                meshes[mk] = md;
            }
            if (md == null) return false;
            subsets = b.Subsets;
            return true;
        }

        // Maillage lisible d'un objet regroupe, dans le repere de son objet (copies des cartes de l'editeur) ; null sinon.
        public static Mesh LocalMesh(MeshRenderer r, Mesh combined)
        {
            SceneFile.MeshData md; int[] subs;
            if (!Find(r, combined, out md, out subs)) return null;
            var map = new Dictionary<int, int>();
            var v = new List<Vector3>(); var n = new List<Vector3>(); var uv = new List<Vector2>();
            var tris = new List<int[]>();
            Matrix4x4 w2l = r.transform.worldToLocalMatrix;
            foreach (int s in subs)
            {
                if (s < 0 || s >= md.Subs.Length) continue;
                int[] t = md.Subs[s], o = new int[t.Length];
                for (int i = 0; i < t.Length; i++)
                {
                    int j;
                    if (!map.TryGetValue(t[i], out j))
                    {
                        j = v.Count; map[t[i]] = j;
                        v.Add(w2l.MultiplyPoint3x4(md.V[t[i]]));
                        n.Add(md.N != null ? w2l.MultiplyVector(md.N[t[i]]).normalized : Vector3.up);
                        uv.Add(md.Uv != null ? md.Uv[t[i]] : Vector2.zero);
                    }
                    o[i] = j;
                }
                tris.Add(o);
            }
            if (v.Count == 0 || v.Count > 65000) return null;
            var m = new Mesh { name = r.name + " (copie)" };
            m.vertices = v.ToArray(); m.normals = n.ToArray(); m.uv = uv.ToArray();
            m.subMeshCount = tris.Count;
            for (int i = 0; i < tris.Count; i++) m.SetTriangles(tris[i], i);
            m.RecalculateBounds();
            return m;
        }
    }
}
