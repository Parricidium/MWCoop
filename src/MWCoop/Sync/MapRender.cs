using System.Collections.Generic;
using UnityEngine;

namespace MWCoop
{
    // Carte du monde en vectoriel (demande de JD, 10/10 : « comme un GPS, avec les joueurs en direct » ; puis « du genre
    // SVG, pas pixelisee » et « les reliefs ne sont pas importants »). Le mod reprend la geometrie du jeu -- les maillages
    // de collision du sol (MAP/MESH/TERRAIN_OBJ), des routes, de la voie ferree et des batiments -- vue du dessus, en aplats
    // de couleur (neige, lacs geles, trottoirs, chemins, routes goudronnees avec bordure, voie ferree, batiments), dans des
    // maillages 2D places loin sous le monde (couche 31). Carte ouverte : une camera orthographique les dessine a la
    // resolution de l'ecran, a chaque zoom (net a toutes les echelles). Construite a petits pas apres le chargement.
    // (Avant, meme jour : une image de 4096 px faite par rayons -- pixelisee au zoom.)
    public static class MapRender
    {
        public static float MinX, MinZ, Size;   // carre du sol du jeu (metres)
        public static bool Ready;
        public static float Progress = -1f;
        const float Depth = -80000f;            // (sous le monde : hors de portee des cameras du jeu)
        const int MapLayer = 31;

        static GameObject root;
        static Material mat;
        static Camera cam;
        static RenderTexture rt;
        static List<Collider> todo;
        static int todoAt;
        static float startAt = -1;

        // une couleur et une hauteur (ordre de dessin : plus haut = par-dessus) par sorte
        struct Kind { public Color32 C; public float Y; public Kind(byte r, byte g, byte b, float y) { C = new Color32(r, g, b, 255); Y = y; } }
        static readonly Kind ForestK = new Kind(74, 104, 84, -1f);
        static readonly Kind Snow = new Kind(238, 242, 246, 0f), Lake = new Kind(170, 205, 235, 0.5f), Pavement = new Kind(196, 200, 208, 1f), Dirt = new Kind(214, 184, 140, 2f),
            IceRoad = new Kind(120, 166, 214, 2.2f), AsphaltEdge = new Kind(170, 120, 40, 3f), Asphalt = new Kind(248, 200, 84, 3.5f), Rail = new Kind(84, 84, 92, 4f),
            Structure = new Kind(170, 172, 180, 4.5f), Building = new Kind(200, 112, 86, 5f);

        // maillages en cours : sommets, couleurs, triangles par sorte, decoupes a 60000 sommets (indices 16 bits)
        class Buf { public List<Vector3> V = new List<Vector3>(); public List<Color32> C = new List<Color32>(); public List<int> T = new List<int>(); }
        static Buf buf;
        static int meshCount, triCount;

        public static void OnLevelLoaded()
        {
            Clear();
            startAt = Application.loadedLevelName == "GAME" ? Time.realtimeSinceStartup + Config.GetFloat("Test", "CarteDelai", 20f) : -1;
        }

        static void Clear()
        {
            if (root != null) Object.Destroy(root);
            root = null; Ready = false; Progress = -1f; todo = null; buf = null;
            if (rt != null) { rt.Release(); Object.Destroy(rt); rt = null; }
        }

        public static void Update()
        {
            if (startAt > 0 && Time.realtimeSinceStartup >= startAt) { startAt = -1; Begin(); }
            if (todo == null) return;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (todoAt < todo.Count && sw.Elapsed.TotalMilliseconds < 4f) { Add(todo[todoAt]); todoAt++; }
            Progress = todo.Count > 0 ? (float)todoAt / todo.Count : 1f;
            if (todoAt >= todo.Count) Finish();
        }

        static void Begin()
        {
            GameObject ground = Game.FindAny("MAP/MESH/TERRAIN_OBJ");
            if (ground == null) { Log.Warn("carte : sol du jeu introuvable (MAP/MESH/TERRAIN_OBJ)"); return; }
            bool any = false; Bounds b = new Bounds();
            foreach (Collider c in ground.GetComponentsInChildren<Collider>(true)) { if (c.isTrigger) continue; if (!any) { b = c.bounds; any = true; } else b.Encapsulate(c.bounds); }
            if (!any) return;
            Size = Mathf.Ceil(Mathf.Max(b.size.x, b.size.z) * 1.02f);
            MinX = b.center.x - Size / 2f; MinZ = b.center.z - Size / 2f;
            root = new GameObject("MWCoop-Carte");
            Object.DontDestroyOnLoad(root);
            Shader sh = Shader.Find("Sprites/Default") ?? Shader.Find("GUI/Text Shader") ?? Shader.Find("Particles/Alpha Blended");
            mat = new Material(sh);
            // a traiter : collisionneurs du sol et des routes, des batiments et du decor (fixes)
            todo = new List<Collider>();
            foreach (Collider c in Object.FindObjectsOfType<Collider>())
            {
                if (c.isTrigger || c.attachedRigidbody != null) continue;
                int l = c.gameObject.layer;
                if (l != 0 && l != 10) continue;
                if (!(c is MeshCollider) && !(c is BoxCollider)) continue;
                todo.Add(c);
            }
            todoAt = 0; buf = new Buf(); meshCount = 0; triCount = 0;
            covered = new bool[G * G];
            Progress = 0f;
            Log.Info("carte : construction (" + todo.Count + " collisionneurs), sol " + b.min.ToString("F0") + " - " + b.max.ToString("F0") + ", shader " + (sh != null ? sh.name : "?"));
        }

        static bool KindOf(Collider c, out Kind k, out bool lakeCheck)
        {
            k = Snow; lakeCheck = false;
            string n = c.name, r = Game.RootName(c.transform);
            if (r == "PLAYER" || r.StartsWith("MWCoop")) return false;
            if (c.gameObject.layer == 10)
            {
                if (n == "AsphaltRoad" || n == "Road2") { k = Asphalt; return true; }
                if (n == "Road") { k = Dirt; return true; }
                if (n == "Pavement") { k = Pavement; return true; }
                if (n == "IceRoad") { k = IceRoad; return true; }
                if (n == "RAILROAD" || n == "RailCol") { k = Rail; return true; }
                if (n.Contains("COLL") || n.Contains("TREE")) return false;
                lakeCheck = true; k = Snow; return true;   // Grass, Skihill, Graveyard, ROCKS... (lacs : bas et plats)
            }
            if (r == "MAP")
            {
                if (n.StartsWith("RAILROAD") || n == "RailCol" || n == "PLANKS") { k = Rail; return true; }
                if (n.StartsWith("BRIDGE")) { k = Asphalt; return true; }
                if (n.Contains("TREE") || n.Contains("ELEC") || n.Contains("FOLIAGE") || n == "Bottom" || n.Contains("ROCK")) return false;
                k = Structure; return true;
            }
            // batiments : pas les petits objets (meubles, panneaux)
            Vector3 s = c.bounds.size;
            if (Mathf.Max(s.x, s.z) < 2.5f) return false;
            k = Building; return true;
        }

        static void Add(Collider c)
        {
            Kind k; bool lakeCheck;
            if (c == null || !KindOf(c, out k, out lakeCheck)) return;
            Transform t = c.transform;
            if (c is BoxCollider)
            {
                var bc = (BoxCollider)c;
                Vector3 h = bc.size / 2f;
                Vector3 p0 = t.TransformPoint(bc.center + new Vector3(-h.x, 0, -h.z)), p1 = t.TransformPoint(bc.center + new Vector3(h.x, 0, -h.z)),
                        p2 = t.TransformPoint(bc.center + new Vector3(h.x, 0, h.z)), p3 = t.TransformPoint(bc.center + new Vector3(-h.x, 0, h.z));
                Tri(p0, p1, p2, k); Tri(p0, p2, p3, k);
                return;
            }
            Mesh m = ((MeshCollider)c).sharedMesh;
            if (m == null) return;
            Vector3[] v; int[] tr;
            try { v = m.vertices; tr = m.triangles; } catch { return; }
            if (v == null || tr == null || v.Length == 0) return;
            var w = new Vector3[v.Length];
            for (int i = 0; i < v.Length; i++) w[i] = t.TransformPoint(v[i]);
            // routes goudronnees : bordure foncee (aretes de bord du maillage, en bandes de 1,4 m), sous le jaune
            if (k.Y == Asphalt.Y) Edges(w, tr, AsphaltEdge, 1.4f);
            for (int i = 0; i + 2 < tr.Length; i += 3)
            {
                Vector3 a = w[tr[i]], b = w[tr[i + 1]], d = w[tr[i + 2]];
                Kind kk = k;
                if (lakeCheck && a.y < -3.9f && b.y < -3.9f && d.y < -3.9f)
                {
                    Vector3 nrm = Vector3.Cross(b - a, d - a);
                    if (nrm.sqrMagnitude > 1e-6f && Mathf.Abs(nrm.normalized.y) > 0.995f) kk = Lake;
                }
                Tri(a, b, d, kk);
            }
        }

        // Aretes de bord (une seule face) : bandes de largeur w autour.
        static void Edges(Vector3[] w, int[] tr, Kind k, float width)
        {
            var count = new Dictionary<long, int>();
            for (int i = 0; i + 2 < tr.Length; i += 3)
                for (int e = 0; e < 3; e++)
                {
                    int a = tr[i + e], b = tr[i + (e + 1) % 3];
                    long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
                    int n; count.TryGetValue(key, out n); count[key] = n + 1;
                }
            foreach (var kv in count)
            {
                if (kv.Value != 1) continue;
                Vector3 a = w[(int)(kv.Key >> 32)], b = w[(int)(kv.Key & 0xffffffff)];
                Vector3 d = new Vector3(b.x - a.x, 0f, b.z - a.z);
                if (d.sqrMagnitude < 1e-6f) continue;
                Vector3 side = new Vector3(-d.z, 0f, d.x).normalized * (width / 2f);
                Tri(a - side, a + side, b + side, k); Tri(a - side, b + side, b - side, k);
            }
        }

        // Forets : les trous du sol enfermes par lui (autour des collines, ou rien n'est praticable) ; le reste du vide est
        // hors de la carte (demande de JD, 10/10 : fond du panneau, seules les forets en vert). Grille de couverture du sol
        // (cellules de ~3 m) : cases vides reliees au bord = dehors (remplissage), les autres = foret.
        const int G = 2048;   // (cases de ~3 m : bords des forets fins)
        static bool[] covered;
        static void Cover(Vector3 a, Vector3 b, Vector3 c)
        {
            float cell = Size / G;
            float ax = (a.x - MinX) / cell, az = (a.z - MinZ) / cell, bx = (b.x - MinX) / cell, bz = (b.z - MinZ) / cell, cx = (c.x - MinX) / cell, cz = (c.z - MinZ) / cell;
            int x0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(ax, Mathf.Min(bx, cx)))), x1 = Mathf.Min(G - 1, Mathf.CeilToInt(Mathf.Max(ax, Mathf.Max(bx, cx))));
            int z0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(az, Mathf.Min(bz, cz)))), z1 = Mathf.Min(G - 1, Mathf.CeilToInt(Mathf.Max(az, Mathf.Max(bz, cz))));
            float d = (bz - cz) * (ax - cx) + (cx - bx) * (az - cz);
            if (Mathf.Abs(d) < 1e-6f) return;
            for (int z = z0; z <= z1; z++)
                for (int x = x0; x <= x1; x++)
                {
                    float px = x + 0.5f, pz = z + 0.5f;
                    float l1 = ((bz - cz) * (px - cx) + (cx - bx) * (pz - cz)) / d, l2 = ((cz - az) * (px - cx) + (ax - cx) * (pz - cz)) / d;
                    if (l1 >= -0.02f && l2 >= -0.02f && l1 + l2 <= 1.02f) covered[z * G + x] = true;
                }
            covered[Mathf.Clamp((int)az, 0, G - 1) * G + Mathf.Clamp((int)ax, 0, G - 1)] = true;
        }

        static void Forests()
        {
            var outside = new bool[G * G];
            var q = new Queue<int>();
            for (int i = 0; i < G; i++)
                foreach (int k in new[] { i, (G - 1) * G + i, i * G, i * G + G - 1 })
                    if (!covered[k] && !outside[k]) { outside[k] = true; q.Enqueue(k); }
            while (q.Count > 0)
            {
                int k = q.Dequeue(), x = k % G, z = k / G;
                if (x > 0) Visit(k - 1, outside, q); if (x < G - 1) Visit(k + 1, outside, q);
                if (z > 0) Visit(k - G, outside, q); if (z < G - 1) Visit(k + G, outside, q);
            }
            // foret = vide pas dehors ; elargie d'une case sous le sol (bords sans jour), pas sur le dehors
            var forest = new bool[G * G];
            int cells = 0;
            for (int z = 0; z < G; z++)
                for (int x = 0; x < G; x++)
                {
                    int k = z * G + x;
                    if (outside[k]) continue;
                    bool f = !covered[k];
                    if (!f)
                        for (int dz = -1; dz <= 1 && !f; dz++)
                            for (int dx = -1; dx <= 1 && !f; dx++)
                            {
                                int nx = x + dx, nz = z + dz;
                                if (nx >= 0 && nz >= 0 && nx < G && nz < G && !covered[nz * G + nx] && !outside[nz * G + nx]) f = true;
                            }
                    if (f) { forest[k] = true; cells++; }
                }
            // contour lisse (marching squares sur les centres des cases) ; l'interieur plein en bandes par ligne
            float cell = Size / G;
            noCover = true;
            for (int zz = 0; zz < G - 1; zz++)
            {
                int runStart = -1;
                for (int xx = 0; xx < G - 1; xx++)
                {
                    int c0 = forest[zz * G + xx] ? 1 : 0, c1 = forest[zz * G + xx + 1] ? 2 : 0, c2 = forest[(zz + 1) * G + xx + 1] ? 4 : 0, c3 = forest[(zz + 1) * G + xx] ? 8 : 0;
                    int cs = c0 | c1 | c2 | c3;
                    if (cs == 15) { if (runStart < 0) runStart = xx; continue; }
                    if (runStart >= 0) { Quad(runStart, xx, zz, cell); runStart = -1; }
                    if (cs != 0) Marching(cs, xx, zz, cell);
                }
                if (runStart >= 0) Quad(runStart, G - 1, zz, cell);
            }
            noCover = false;
            Log.Info("carte : forets " + cells + " cases de " + cell.ToString("F1") + " m");
        }
        // (points : centres des cases)
        static Vector3 P(float x, float z, float cell) { return new Vector3(MinX + (x + 0.5f) * cell, 0f, MinZ + (z + 0.5f) * cell); }
        static void Quad(int x0, int x1, int z, float cell)
        {
            Vector3 a = P(x0, z, cell), b = P(x1, z, cell), c = P(x1, z + 1, cell), d = P(x0, z + 1, cell);
            Tri(a, b, c, ForestK); Tri(a, c, d, ForestK);
        }
        // coins : 1 bas-gauche, 2 bas-droite, 4 haut-droite, 8 haut-gauche ; milieux : b(as), d(roite), h(aut), g(auche)
        static readonly string[] Cases = { "", "0bg", "b1d", "01dg", "d2h", "0bd2hg", "b12h", "012hg", "h3g", "0bh3", "b1dh3g", "01dh3", "d23g", "0bd23", "b123g", "0123" };
        static void Marching(int cs, int x, int z, float cell)
        {
            string poly = Cases[cs];
            var pts = new Vector3[poly.Length];
            for (int i = 0; i < poly.Length; i++)
            {
                switch (poly[i])
                {
                    case '0': pts[i] = P(x, z, cell); break;
                    case '1': pts[i] = P(x + 1, z, cell); break;
                    case '2': pts[i] = P(x + 1, z + 1, cell); break;
                    case '3': pts[i] = P(x, z + 1, cell); break;
                    case 'b': pts[i] = P(x + 0.5f, z, cell); break;
                    case 'd': pts[i] = P(x + 1, z + 0.5f, cell); break;
                    case 'h': pts[i] = P(x + 0.5f, z + 1, cell); break;
                    default: pts[i] = P(x, z + 0.5f, cell); break;
                }
            }
            for (int i = 1; i + 1 < pts.Length; i++) Tri(pts[0], pts[i], pts[i + 1], ForestK);
        }

        static void Visit(int k, bool[] outside, Queue<int> q) { if (!covered[k] && !outside[k]) { outside[k] = true; q.Enqueue(k); } }
        static bool noCover;

        static void Tri(Vector3 a, Vector3 b, Vector3 c, Kind k)
        {
            // (triangles immenses et fins du bord du sol : des traits en travers de la carte)
            if (!noCover && (Flat(a, b) > 1000f || Flat(b, c) > 1000f || Flat(c, a) > 1000f)) return;
            if (!noCover && covered != null && k.Y <= Asphalt.Y) Cover(a, b, c);   // (sol, routes : pas les batiments)
            if (buf.V.Count > 59990) Flush();
            int n = buf.V.Count;
            buf.V.Add(new Vector3(a.x, Depth + k.Y, a.z)); buf.V.Add(new Vector3(b.x, Depth + k.Y, b.z)); buf.V.Add(new Vector3(c.x, Depth + k.Y, c.z));
            buf.C.Add(k.C); buf.C.Add(k.C); buf.C.Add(k.C);
            // (vu d'en haut : sens des aiguilles d'une montre, sinon la face est tournee)
            float cross = (b.x - a.x) * (c.z - a.z) - (b.z - a.z) * (c.x - a.x);
            if (cross > 0f) { buf.T.Add(n); buf.T.Add(n + 2); buf.T.Add(n + 1); } else { buf.T.Add(n); buf.T.Add(n + 1); buf.T.Add(n + 2); }
            triCount++;
        }

        static float Flat(Vector3 a, Vector3 b) { float dx = a.x - b.x, dz = a.z - b.z; return Mathf.Sqrt(dx * dx + dz * dz); }

        static void Flush()
        {
            if (buf == null || buf.V.Count == 0) return;
            var go = new GameObject("carte" + meshCount++);
            go.layer = MapLayer;
            go.transform.parent = root.transform;
            var m = new Mesh();
            m.vertices = buf.V.ToArray(); m.colors32 = buf.C.ToArray(); m.triangles = buf.T.ToArray();
            m.RecalculateBounds();
            go.AddComponent<MeshFilter>().sharedMesh = m;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.castShadows = false; r.receiveShadows = false;
            buf = new Buf();
        }

        static void Finish()
        {
            Forests();
            covered = null;
            Flush();
            todo = null; buf = null;
            Ready = true; Progress = -1f;
            Log.Info("carte : construite, " + triCount + " triangles en " + meshCount + " maillages");
        }

        // Dessine la vue (centre monde, metres par pixel) dans une texture de la taille demandee.
        public static RenderTexture View(int w, int h, Vector2 center, float metersPerPixel, Color32 background)
        {
            if (!Ready || w < 8 || h < 8) return null;
            if (rt == null || rt.width != w || rt.height != h)
            {
                if (rt != null) { rt.Release(); Object.Destroy(rt); }
                rt = new RenderTexture(w, h, 16, RenderTextureFormat.ARGB32);   // (fond transparent : celui du panneau)
                rt.antiAliasing = Mathf.Clamp(Config.GetInt("Test", "CarteAA", 4), 1, 8);
            }
            if (cam == null)
            {
                var go = new GameObject("MWCoop-CarteCamera");
                Object.DontDestroyOnLoad(go);
                cam = go.AddComponent<Camera>();
                cam.enabled = false;
                cam.orthographic = true;
                cam.cullingMask = 1 << MapLayer;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.transform.rotation = Quaternion.Euler(90f, 0f, 0f);   // vers le bas, le nord (+z) en haut
                cam.nearClipPlane = 1f; cam.farClipPlane = 100f;
            }
            cam.backgroundColor = new Color32(background.r, background.g, background.b, 0);
            cam.orthographicSize = h * metersPerPixel / 2f;
            cam.aspect = (float)w / h;
            cam.transform.position = new Vector3(center.x, Depth + 50f, center.y);
            cam.targetTexture = rt;
            bool fog = RenderSettings.fog;
            RenderSettings.fog = false;
            cam.Render();
            RenderSettings.fog = fog;
            cam.targetTexture = null;
            return rt;
        }
    }
}
