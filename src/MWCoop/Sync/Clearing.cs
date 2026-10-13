using System.Collections.Generic;
using UnityEngine;

namespace MWCoop
{
    // Terrain du « coin s21 » a Perajarvi (demande de JD, 13/10 : la boutique et le musee de la Corse, au sud-ouest du
    // carrefour de Teimo) : les arbres et buissons de ce terrain sont retires au chargement. Le jeu ne les a pas en objets :
    // ils sont fusionnes dans quelques tres gros maillages pour toute la carte (MAP/MESH/FOLIAGE/TREES1, TREES2,
    // TREES_MEDIUM1..3, TREES_SMALL1..4) et leurs troncs dans des maillages de collision (MAP/MESH/TREES1_COLL..3). Chaque
    // arbre y est un ilot de triangles (sommets non partages avec les voisins) : on regroupe les triangles proches du terrain
    // par sommets communs, et on retire les ilots dont le pied (sommet le plus bas) est dans le terrain -- l'arbre entier, pas
    // un morceau. En memoire seulement, a chaque chargement, pareil chez tous (meme calcul) ; les fichiers du jeu ne changent
    // pas, sans MWCoop la foret est intacte.
    public static class Clearing
    {
        // terrain (x, z du monde), trace par JD sur la vue du carrefour, un peu elargi
        // (au nord : jusqu'au bord de la petite route, pour qu'aucun arbre ne reste devant les facades)
        public static readonly Vector2[] Plot = {
            new Vector2(-1613f, 1075f), new Vector2(-1582f, 1059f), new Vector2(-1537f, 1050f), new Vector2(-1514f, 1081f),
            new Vector2(-1530f, 1091.5f), new Vector2(-1540f, 1089.5f), new Vector2(-1550f, 1089.5f), new Vector2(-1560f, 1092.5f),
            new Vector2(-1570f, 1097.5f), new Vector2(-1580f, 1101.5f), new Vector2(-1590f, 1106.5f), new Vector2(-1600f, 1111.5f), new Vector2(-1612f, 1108f) };
        static readonly string[] Visual = { "TREES1", "TREES2", "TREES_MEDIUM1", "TREES_MEDIUM2", "TREES_MEDIUM3", "TREES_SMALL1", "TREES_SMALL2", "TREES_SMALL3", "TREES_SMALL4" };
        static readonly string[] Colls = { "TREES1_COLL", "TREES2_COLL", "TREES3_COLL" };
        static bool done;
        static float at = -1f;

        public static void OnLevelLoaded() { done = false; at = PlayerSync.InGame ? Time.realtimeSinceStartup + 1f : -1f; }

        public static void Update()
        {
            if (done || at < 0f || Time.realtimeSinceStartup < at || !Ajouts.Ready) return;   // (invite : les choix de l'hote d'abord)
            done = true;
            if (!Kauppa.Active) return;   // (sans les ajouts COOP ni la boutique : la foret reste)
            if (Config.GetInt("Boutique", "Defrichage", 1) == 0) { Log.Info("defrichage : coupe ([Boutique] Defrichage=0)"); return; }
            GameObject mesh = GameObject.Find("MAP/MESH");
            if (mesh == null) { Log.Info("defrichage : MAP/MESH introuvable"); return; }
            var sw = System.Diagnostics.Stopwatch.StartNew();
            int trees = 0, colls = 0;
            Transform fol = mesh.transform.Find("FOLIAGE");
            if (fol != null)
                foreach (string n in Visual)
                {
                    Transform t = fol.Find(n);
                    MeshFilter mf = t != null ? t.GetComponent<MeshFilter>() : null;
                    if (mf != null && mf.sharedMesh != null) trees += Clear(mf.sharedMesh, t, n);
                }
            foreach (string n in Colls)
            {
                Transform t = mesh.transform.Find(n);
                MeshCollider mc = t != null ? t.GetComponent<MeshCollider>() : null;
                if (mc == null || mc.sharedMesh == null) continue;
                Mesh m = mc.sharedMesh;
                int k = Clear(m, t, n);
                if (k > 0) { mc.sharedMesh = null; mc.sharedMesh = m; }   // (collision refaite)
                colls += k;
            }
            Log.Info("defrichage : " + trees + " arbre(s) et buisson(s), " + colls + " tronc(s) retires du terrain du coin s21 (" + sw.ElapsedMilliseconds + " ms)");
            Terrace();
            Probe();
        }

        // Bord sud de la petite route (Road2) le long du terrain, releve a la sonde (x, z du monde), d'ouest en est.
        static readonly Vector2[] RoadEdge = { new Vector2(-1600f, 1112f), new Vector2(-1590f, 1107f), new Vector2(-1580f, 1102f), new Vector2(-1570f, 1098f),
            new Vector2(-1560f, 1093f), new Vector2(-1550f, 1090f), new Vector2(-1540f, 1090f), new Vector2(-1530f, 1092f) };

        // Bord est de la grande route (AsphaltRoad) le long du terrain, releve a la sonde.
        static readonly Vector2[] MainRoadEdge = { new Vector2(-1624f, 1066f), new Vector2(-1619f, 1084f), new Vector2(-1615f, 1100f), new Vector2(-1609f, 1112f), new Vector2(-1600f, 1122f) };

        static float DistToLine(Vector2 p, Vector2[] line)
        {
            float best = float.MaxValue;
            for (int i = 0; i + 1 < line.Length; i++)
            {
                Vector2 a = line[i], b = line[i + 1], ab = b - a;
                float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
                best = Mathf.Min(best, (a + ab * t - p).magnitude);
            }
            return best;
        }

        static Vector2 EdgeAt(float x)
        {
            for (int i = 0; i + 1 < RoadEdge.Length; i++)
                if (x <= RoadEdge[i + 1].x) { float t = Mathf.InverseLerp(RoadEdge[i].x, RoadEdge[i + 1].x, x); return Vector2.Lerp(RoadEdge[i], RoadEdge[i + 1], t); }
            return RoadEdge[RoadEdge.Length - 1];
        }

        static float Ground(float x, float z)
        {
            float best = float.MinValue;
            foreach (RaycastHit h in Physics.RaycastAll(new Vector3(x, 300f, z), Vector3.down, 400f))
                if (!h.collider.isTrigger && h.point.y > best && h.collider.GetComponentInParent<Rigidbody>() == null) best = h.point.y;
            return best == float.MinValue ? 2f : best;
        }

        // [Boutique] Sonde=1 : le sol du terrain releve en lignes nord-sud (tous les 10 m en x, 1 m en z) -- nom du collider
        // touche et hauteur, aux changements (essais : ou passe la route, ou monte le talus).
        static void Probe()
        {
            if (Config.GetInt("Boutique", "Sonde", 0) == 0) return;
            for (float x = -1620f; x <= -1510f; x += 10f)
            {
                var sb = new System.Text.StringBuilder("sonde x " + x + " :");
                string last = null;
                for (float z = 1040f; z <= 1140f; z += 1f)
                {
                    RaycastHit best = new RaycastHit(); bool got = false;
                    foreach (RaycastHit h in Physics.RaycastAll(new Vector3(x, 300f, z), Vector3.down, 400f))
                        if (!h.collider.isTrigger && (!got || h.distance < best.distance)) { best = h; got = true; }
                    string n = got ? best.collider.name : "-";
                    if (n != last) { sb.Append(" | z" + z + " " + n + " " + (got ? best.point.y.ToString("F1") : "")); last = n; }
                    else if (((int)z) % 10 == 0) sb.Append(" .z" + z + " " + (got ? best.point.y.ToString("F1") : ""));
                }
                Log.Info(sb.ToString());
            }
        }

        // [Boutique] Tasanne=x,z,cap,demi-largeur,demi-profondeur;... : plateformes de terrassement. Le sol du jeu (maillages
        // MAP/MESH/TERRAIN_OBJ/Grass, rendu et collision) est mis a plat sous chaque emprise, a la hauteur du sol au milieu de la
        // facade (cote route, +z local), plat 4 m autour, puis raccord en pente douce sur 12 m. Le talus de la grande route montait de
        // 2,5 m sous la boutique : plancher perche, grand escalier.
        // boutique puis musee (memes centres et caps que KauppaRakennus / MuseoRakennus ; le musee : sa facade et son parvis)
        const string DefaultPads = "-1591.1,1098.5,25,5.6,3.6;-1573.8,1087.4,22,10.6,7.9";
        static void Terrace()
        {
            string s = Config.Get("Boutique", "Tasanne", DefaultPads);
            if (s.Length == 0) return;
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            var pads = new List<float[]>();
            foreach (string one in s.Split(';'))
            {
                string[] a = one.Split(',');
                if (a.Length < 5) continue;
                float x = float.Parse(a[0], ci), z = float.Parse(a[1], ci), yaw = float.Parse(a[2], ci), hw = float.Parse(a[3], ci), hd = float.Parse(a[4], ci);
                Quaternion r = Quaternion.Euler(0, yaw, 0);
                Vector3 front = new Vector3(x, 0, z) + r * new Vector3(0, 0, hd + 1f);
                float h = Ground(front.x, front.z);
                pads.Add(new[] { x, z, yaw, hw, hd, h });
                Log.Info("defrichage : plateforme en " + x + ", " + z + " a " + h.ToString("F2") + " m");
            }
            GameObject terr = GameObject.Find("MAP/MESH/TERRAIN_OBJ");
            if (terr == null || pads.Count == 0) return;
            const float Blend = 12f, Flat = 4f;   // (sol du jeu grossier : un sommet tous les 5 a 10 m)
            System.Func<Vector3, Vector2> weight = w =>
            {
                float best = 0f, target = 0f;
                foreach (float[] p in pads)
                {
                    Vector3 loc = Quaternion.Inverse(Quaternion.Euler(0, p[2], 0)) * new Vector3(w.x - p[0], 0, w.z - p[1]);
                    float dx = Mathf.Max(0f, Mathf.Abs(loc.x) - p[3] - Flat), dz = Mathf.Max(0f, Mathf.Abs(loc.z) - p[4] - Flat);
                    float d = Mathf.Sqrt(dx * dx + dz * dz);
                    if (d >= Blend) continue;
                    float k = 1f - Mathf.SmoothStep(0f, 1f, d / Blend);
                    if (k > best) { best = k; target = p[5]; }
                }
                // Pres des routes, le sol ne bouge pas : leurs bords enneiges (le bourrelet le long de la route) font partie du
                // maillage de la route, fusionne par le jeu (statique, illisible) -- abaisse dessous, le sol laissait le bourrelet
                // en l'air, ombres de l'autre cote visibles au travers (retour de JD, 13/10). Rien a moins de 2 m, raccord jusqu'a 8 m.
                float dr = Mathf.Min(DistToLine(new Vector2(w.x, w.z), RoadEdge), DistToLine(new Vector2(w.x, w.z), MainRoadEdge));
                best *= Mathf.SmoothStep(0f, 1f, (dr - 2f) / 6f);
                return new Vector2(best, target);
            };
            // Couches posees sur le sol (bourrelets de neige des routes -- Snowbanks, sans collision --, routes, trottoirs) : le meme
            // deplacement que le sol sous elles, mesure sur le sol AVANT de le toucher. Sinon elles flottaient au-dessus du sol abaisse
            // (plaque blanche, liseré sous les murs : retour de JD, 13/10).
            var grassCols = new List<MeshCollider>();
            foreach (Transform t in terr.transform) if (t.name == "Grass" && t.GetComponent<MeshCollider>() != null) grassCols.Add(t.GetComponent<MeshCollider>());
            System.Func<float, float, float> grassY = (x, z) =>
            {
                RaycastHit h; var ray = new Ray(new Vector3(x, 300f, z), Vector3.down);
                foreach (MeshCollider c in grassCols) if (c.Raycast(ray, out h, 500f)) return h.point.y;
                return float.NaN;
            };
            int layers = 0;
            foreach (Transform t in terr.transform)
            {
                if (t.name == "Grass") continue;
                MeshFilter mf = t.GetComponent<MeshFilter>(); MeshCollider mc = t.GetComponent<MeshCollider>();
                Mesh m = mf != null ? mf.sharedMesh : null;
                if (m == null) continue;
                Vector3[] v;
                try { v = m.vertices; } catch { Log.Info("defrichage : couche " + t.name + " illisible"); continue; }
                Matrix4x4 l2w = t.localToWorldMatrix, w2l = t.worldToLocalMatrix;
                int n = 0, inside = 0, nan = 0;
                for (int i = 0; i < v.Length; i++)
                {
                    Vector3 w = l2w.MultiplyPoint3x4(v[i]);
                    Vector2 bt = weight(w);
                    if (bt.x <= 0f) continue;
                    inside++;
                    float g = grassY(w.x, w.z);
                    if (float.IsNaN(g)) { nan++; continue; }
                    w.y += Mathf.Lerp(g, bt.y, bt.x) - g;
                    v[i] = w2l.MultiplyPoint3x4(w); n++;
                }
                if (inside > 0 || v.Length == 0) Log.Info("defrichage : couche " + t.name + " : " + v.Length + " sommets, " + inside + " dans la zone, " + nan + " sans sol dessous, " + grassCols.Count + " sols");
                if (n == 0) continue;
                m.vertices = v; m.RecalculateBounds();
                if (mc != null && mc.sharedMesh == m) { mc.sharedMesh = null; mc.sharedMesh = m; }
                layers += n;
                Log.Info("defrichage : couche " + t.name + " : " + n + " sommet(s) suivent le sol");
            }
            int moved = 0;
            foreach (Transform t in terr.transform)
            {
                if (t.name != "Grass") continue;
                MeshFilter mf = t.GetComponent<MeshFilter>(); MeshCollider mc = t.GetComponent<MeshCollider>();
                Mesh m = mf != null ? mf.sharedMesh : null;
                if (m == null) continue;
                Vector3[] v;
                try { v = m.vertices; } catch { continue; }
                Matrix4x4 l2w = t.localToWorldMatrix, w2l = t.worldToLocalMatrix;
                Vector3[] nr = m.normals;
                Vector3 up = t.InverseTransformDirection(Vector3.up).normalized;
                int n = 0;
                for (int i = 0; i < v.Length; i++)
                {
                    Vector3 w = l2w.MultiplyPoint3x4(v[i]);
                    Vector2 bt = weight(w);
                    float best = bt.x, target = bt.y;
                    if (best <= 0f) continue;
                    w.y = Mathf.Lerp(w.y, target, best);
                    v[i] = w2l.MultiplyPoint3x4(w); n++;
                    if (nr != null && i < nr.Length) nr[i] = Vector3.Slerp(nr[i], up, best);   // (normales d'origine ailleurs : rien ne change sur le reste de la carte)
                }
                if (n == 0) continue;
                m.vertices = v; if (nr != null && nr.Length == v.Length) m.normals = nr; m.RecalculateBounds();
                if (mc != null) { mc.sharedMesh = null; mc.sharedMesh = m; }
                moved += n;
                Log.Info("defrichage : sol " + t.name + " (" + v.Length + " sommets) : " + n + " sommet(s) nivele(s)");
            }
            Log.Info("defrichage : terrassement, " + moved + " sommet(s) du sol et " + layers + " des couches posees dessus deplaces");
        }

        static bool Inside(float x, float z)
        {
            bool c = false;
            for (int i = 0, j = Plot.Length - 1; i < Plot.Length; j = i++)
            {
                Vector2 a = Plot[i], b = Plot[j];
                if ((a.y > z) != (b.y > z) && x < (b.x - a.x) * (z - a.y) / (b.y - a.y) + a.x) c = !c;
            }
            return c;
        }

        static int Find(int[] p, int i) { while (p[i] != i) { p[i] = p[p[i]]; i = p[i]; } return i; }

        // Retire de 'm' les ilots dont le pied est dans le terrain ; rend leur nombre.
        static int Clear(Mesh m, Transform t, string name)
        {
            Vector3[] v;
            try { v = m.vertices; } catch { Log.Info("defrichage : " + name + " illisible"); return 0; }
            if (v == null || v.Length == 0) { Log.Info("defrichage : " + name + " illisible ou vide"); return 0; }
            // boite du terrain + 15 m (rayon d'un grand arbre), dans le repere du maillage
            float x0 = float.MaxValue, x1 = float.MinValue, z0 = float.MaxValue, z1 = float.MinValue;
            foreach (Vector2 p in Plot) { x0 = Mathf.Min(x0, p.x); x1 = Mathf.Max(x1, p.x); z0 = Mathf.Min(z0, p.y); z1 = Mathf.Max(z1, p.y); }
            x0 -= 15f; x1 += 15f; z0 -= 15f; z1 += 15f;
            var near = new bool[v.Length];
            var world = new Vector3[v.Length];
            Matrix4x4 l2w = t.localToWorldMatrix;
            bool any = false;
            for (int i = 0; i < v.Length; i++)
            {
                Vector3 w = l2w.MultiplyPoint3x4(v[i]);
                if (w.x >= x0 && w.x <= x1 && w.z >= z0 && w.z <= z1) { near[i] = true; world[i] = w; any = true; }
            }
            if (!any) return 0;
            var parent = new int[v.Length];
            for (int i = 0; i < parent.Length; i++) parent[i] = i;
            var tris = new int[m.subMeshCount][];
            for (int s = 0; s < m.subMeshCount; s++)
            {
                int[] tr = m.GetTriangles(s); tris[s] = tr;
                for (int i = 0; i + 2 < tr.Length; i += 3)
                {
                    int a = tr[i], b = tr[i + 1], c = tr[i + 2];
                    if (!near[a] && !near[b] && !near[c]) continue;
                    int ra = Find(parent, a), rb = Find(parent, b), rc = Find(parent, c);
                    if (ra != rb) parent[rb] = ra;
                    if (ra != rc) parent[Find(parent, rc)] = Find(parent, ra);
                }
            }
            // pied de chaque ilot proche : sommet le plus bas
            var foot = new Dictionary<int, Vector3>();
            for (int i = 0; i < v.Length; i++)
            {
                if (!near[i]) continue;
                int r = Find(parent, i);
                Vector3 f;
                if (!foot.TryGetValue(r, out f) || world[i].y < f.y) foot[r] = world[i];
            }
            var gone = new HashSet<int>();
            foreach (KeyValuePair<int, Vector3> kv in foot) if (Inside(kv.Value.x, kv.Value.z)) gone.Add(kv.Key);
            if (gone.Count == 0) return 0;
            for (int s = 0; s < tris.Length; s++)
            {
                int[] tr = tris[s];
                var keep = new List<int>(tr.Length);
                for (int i = 0; i + 2 < tr.Length; i += 3)
                {
                    int a = tr[i];
                    if (near[a] && gone.Contains(Find(parent, a))) continue;
                    keep.Add(a); keep.Add(tr[i + 1]); keep.Add(tr[i + 2]);
                }
                if (keep.Count != tr.Length) m.SetTriangles(keep.ToArray(), s);
            }
            Log.Info("defrichage : " + name + " : " + gone.Count + " ilot(s) retire(s)");
            return gone.Count;
        }
    }
}
