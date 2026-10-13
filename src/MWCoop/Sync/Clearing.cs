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
        public static readonly Vector2[] Plot = {
            new Vector2(-1612f, 1103f), new Vector2(-1613f, 1075f), new Vector2(-1582f, 1059f), new Vector2(-1537f, 1050f),
            new Vector2(-1514f, 1081f), new Vector2(-1550f, 1087f), new Vector2(-1604f, 1104f) };
        static readonly string[] Visual = { "TREES1", "TREES2", "TREES_MEDIUM1", "TREES_MEDIUM2", "TREES_MEDIUM3", "TREES_SMALL1", "TREES_SMALL2", "TREES_SMALL3", "TREES_SMALL4" };
        static readonly string[] Colls = { "TREES1_COLL", "TREES2_COLL", "TREES3_COLL" };
        static bool done;
        static float at = -1f;

        public static void OnLevelLoaded() { done = false; at = PlayerSync.InGame ? Time.realtimeSinceStartup + 1f : -1f; }

        public static void Update()
        {
            if (done || at < 0f || Time.realtimeSinceStartup < at) return;
            done = true;
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
            MuseumMockup();
            Parking();
            Probe();
        }

        // Bord sud de la petite route (Road2) le long du terrain, releve a la sonde (x, z du monde), d'ouest en est.
        static readonly Vector2[] RoadEdge = { new Vector2(-1600f, 1112f), new Vector2(-1590f, 1107f), new Vector2(-1580f, 1102f), new Vector2(-1570f, 1098f),
            new Vector2(-1560f, 1093f), new Vector2(-1550f, 1090f), new Vector2(-1540f, 1090f), new Vector2(-1530f, 1092f) };

        static Vector2 EdgeAt(float x)
        {
            for (int i = 0; i + 1 < RoadEdge.Length; i++)
                if (x <= RoadEdge[i + 1].x) { float t = Mathf.InverseLerp(RoadEdge[i].x, RoadEdge[i + 1].x, x); return Vector2.Lerp(RoadEdge[i], RoadEdge[i + 1], t); }
            return RoadEdge[RoadEdge.Length - 1];
        }

        // [Boutique] Parkki=x0,x1,ecart,profondeur : parking goudronne entre la route et les batiments, de x0 a x1, a 'ecart' m du
        // bord de la route et 'profondeur' m de large ; il suit le sol (releve tous les metres) ; collision (on s'y gare) ;
        // places marquees en blanc. Materiau : celui de la route du jeu (AsphaltRoad).
        static void Parking()
        {
            string s = Config.Get("Boutique", "Parkki", "");
            if (s.Length == 0) return;
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            string[] a = s.Split(',');
            float xa = float.Parse(a[0], ci), xb = float.Parse(a[1], ci), gap = float.Parse(a[2], ci), depth = float.Parse(a[3], ci);
            var vs = new List<Vector3>(); var uv = new List<Vector2>(); var tri = new List<int>();
            var lineV = new List<Vector3>(); var lineT = new List<int>();
            int cols = 0, rows = Mathf.CeilToInt(depth) + 1;
            float along = 0f; Vector2 prev = Vector2.zero; bool first = true;
            for (float x = xa; x <= xb + 0.01f; x += 1f, cols++)
            {
                Vector2 e = EdgeAt(x), e2 = EdgeAt(x + 0.5f), e1 = EdgeAt(x - 0.5f);
                Vector2 dir = (e2 - e1).normalized, nrm = new Vector2(dir.y, -dir.x);   // (vers le sud : la route est au nord)
                if (!first) along += (e - prev).magnitude; prev = e; first = false;
                for (int r = 0; r < rows; r++)
                {
                    float dd = gap + depth * r / (rows - 1);
                    Vector2 p = e + nrm * dd;
                    vs.Add(new Vector3(p.x, Ground(p.x, p.y) + 0.05f, p.y));
                    uv.Add(new Vector2(along / 4f, dd / 4f));
                }
            }
            for (int c = 0; c + 1 < cols; c++)
                for (int r = 0; r + 1 < rows; r++)
                {
                    int i0 = c * rows + r, i1 = (c + 1) * rows + r;
                    tri.AddRange(new[] { i0, i0 + 1, i1, i1, i0 + 1, i1 + 1 });
                }
            var m = new Mesh { name = "MWCoop parkki" };
            m.vertices = vs.ToArray(); m.uv = uv.ToArray(); m.triangles = tri.ToArray(); m.RecalculateNormals(); m.RecalculateBounds();
            var go = new GameObject("MWCoop parkki");
            go.AddComponent<MeshFilter>().sharedMesh = m;
            var mr = go.AddComponent<MeshRenderer>();
            GameObject road = GameObject.Find("MAP/MESH/TERRAIN_OBJ/AsphaltRoad");
            Renderer rr = road != null ? road.GetComponent<Renderer>() : null;
            Shader sh = Shader.Find("Legacy Shaders/Diffuse") ?? Shader.Find("Diffuse");
            mr.sharedMaterial = rr != null ? rr.sharedMaterial : new Material(sh) { color = new Color(0.3f, 0.3f, 0.32f) };
            go.AddComponent<MeshCollider>().sharedMesh = m;
            // places : traits blancs perpendiculaires a la route, tous les 2,7 m, sur les 5 m du fond
            var wm = new Material(sh) { color = new Color(0.92f, 0.92f, 0.9f) };
            int n = 0;
            for (float x = xa + 1.5f; x <= xb - 1.5f; x += 2.7f, n++)
            {
                Vector2 e = EdgeAt(x), dir = (EdgeAt(x + 0.5f) - EdgeAt(x - 0.5f)).normalized, nrm = new Vector2(dir.y, -dir.x);
                Vector2 p0 = e + nrm * (gap + depth - 5.2f), p1 = e + nrm * (gap + depth - 0.2f);
                var l = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Object.Destroy(l.GetComponent<Collider>());
                Vector2 c = (p0 + p1) / 2f;
                l.transform.position = new Vector3(c.x, Mathf.Max(Ground(p0.x, p0.y), Ground(p1.x, p1.y)) + 0.075f, c.y);
                l.transform.rotation = Quaternion.LookRotation(new Vector3(nrm.x, 0, nrm.y));
                l.transform.localScale = new Vector3(0.12f, 0.02f, 5f);
                l.GetComponent<Renderer>().sharedMaterial = wm;
                l.transform.SetParent(go.transform, true);
            }
            Log.Info("defrichage : parking de " + (xb - xa) + " x " + depth + " m, " + n + " traits, materiau " + mr.sharedMaterial.name);
        }

        static float Ground(float x, float z)
        {
            float best = float.MinValue;
            foreach (RaycastHit h in Physics.RaycastAll(new Vector3(x, 300f, z), Vector3.down, 400f))
                if (!h.collider.isTrigger && h.collider.name != "MWCoop parkki" && h.point.y > best && h.collider.GetComponentInParent<Rigidbody>() == null) best = h.point.y;
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

        // [Boutique] MuseoEssai=x,z,cap,largeur,profondeur,hauteur : volume d'essai du musee (rouge de Falun, toit gris), pour juger
        // l'emplacement avant le dessin du batiment.
        static void MuseumMockup()
        {
            string s = Config.Get("Boutique", "MuseoEssai", "");
            if (s.Length == 0) return;
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            string[] a = s.Split(',');
            float x = float.Parse(a[0], ci), z = float.Parse(a[1], ci), yaw = float.Parse(a[2], ci), w = float.Parse(a[3], ci), d = float.Parse(a[4], ci), h = float.Parse(a[5], ci);
            Quaternion rot = Quaternion.Euler(0, yaw, 0);
            float g = float.MinValue;
            for (float u = -w / 2; u <= w / 2; u += 1f)
                for (float v = -d / 2; v <= d / 2; v += 1f)
                {
                    RaycastHit hit;
                    if (Physics.Raycast(new Vector3(x, 300f, z) + rot * new Vector3(u, 0, v), Vector3.down, out hit, 400f)) g = Mathf.Max(g, hit.point.y);
                }
            if (g == float.MinValue) g = 3.5f;
            var root = new GameObject("MWCoop museo essai");
            root.transform.position = new Vector3(x, g, z); root.transform.rotation = rot;
            Shader sh = Shader.Find("Legacy Shaders/Diffuse") ?? Shader.Find("Diffuse");
            GameObject body = GameObject.CreatePrimitive(PrimitiveType.Cube);
            body.transform.SetParent(root.transform, false);
            body.transform.localPosition = new Vector3(0, h / 2 - 0.6f, 0); body.transform.localScale = new Vector3(w, h + 1.2f, d);
            body.GetComponent<Renderer>().material = new Material(sh) { color = new Color(0.55f, 0.16f, 0.1f) };
            GameObject roof = GameObject.CreatePrimitive(PrimitiveType.Cube);
            roof.transform.SetParent(root.transform, false);
            roof.transform.localPosition = new Vector3(0, h + 0.9f, 0); roof.transform.localScale = new Vector3(w + 0.6f, 1.8f, d + 0.6f);
            roof.GetComponent<Renderer>().material = new Material(sh) { color = new Color(0.32f, 0.33f, 0.35f) };
            Log.Info("defrichage : volume d'essai du musee en " + x + ", " + g.ToString("F2") + ", " + z + " (" + w + " x " + d + " m)");
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
