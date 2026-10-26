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
        // (My Summer Car, 19/10 : l'ete -- herbe au lieu de neige, lacs en eau libre a -4,5 m, comme la flottaison du bateau)
        static readonly Kind Snow = Game.Msc ? new Kind(184, 206, 150, 0f) : new Kind(238, 242, 246, 0f), Lake = Game.Msc ? new Kind(96, 150, 206, 0.5f) : new Kind(170, 205, 235, 0.5f), Pavement = new Kind(196, 200, 208, 1f), Dirt = new Kind(214, 184, 140, 2f),
            IceRoad = new Kind(120, 166, 214, 2.2f), AsphaltEdge = new Kind(170, 120, 40, 3f), Asphalt = new Kind(248, 200, 84, 3.5f), Rail = new Kind(84, 84, 92, 4f),
            Structure = new Kind(170, 172, 180, 4.5f), Building = new Kind(200, 112, 86, 5f);
        // My Summer Car (carte refaite, JD 19/10) : champs, rochers, decharge, marais ; bas-cotes de la grande route sous sa bordure ;
        // chemins plus fonces que sur la neige (sur l'herbe, la couleur de MWC ne se voyait pas)
        static readonly Kind Field = new Kind(204, 210, 146, -0.5f), Rock = new Kind(172, 174, 162, 0.3f), Landfill = new Kind(176, 164, 140, 0.35f),
            Swamp = new Kind(146, 168, 118, 0.4f), LakeBed = new Kind(96, 150, 206, -0.6f), Shoulder = new Kind(204, 190, 156, 1.5f), DirtMsc = new Kind(196, 156, 104, 2f);

        // maillages en cours, un par sorte (et par niveau de zoom des routes elargies) : sommets, couleurs, triangles, decoupes
        // a 60000 sommets (indices 16 bits). Chaque sorte a son materiau et sa file de rendu (Sprites/Default n'ecrit pas la
        // profondeur : l'ordre de dessin fait tout -- avant, un seul tampon dans l'ordre des collisionneurs, et sur MSC l'herbe
        // passait sur les routes).
        class Buf { public List<Vector3> V = new List<Vector3>(); public List<Color32> C = new List<Color32>(); public List<int> T = new List<int>(); }
        static Dictionary<int, Buf> bufs;
        static readonly Dictionary<int, Material> mats = new Dictionary<int, Material>();
        static int meshCount, triCount;
        // Routes elargies (MSC : la carte fait 6 km, une route de 8 m disparaissait en vue d'ensemble) : bandes de part et
        // d'autre des bords, montrees seulement de loin -- niveau 1 au-dela de MidMpp metres par pixel, niveau 2 de FarMpp.
        const float MidMpp = 2.5f, FarMpp = 6f;
        static GameObject midRoot, farRoot;
        static int level;   // niveau des triangles ajoutes (0 : la carte ; 1, 2 : routes elargies)
        static int Key(Kind k) { return level * 1000 + Mathf.RoundToInt((k.Y + 2f) * 20f); }

        public static void OnLevelLoaded()
        {
            Clear();
            startAt = Game.LevelName == "GAME" ? Time.realtimeSinceStartup + Config.GetFloat("Test", "CarteDelai", 20f) : -1;
        }

        static void Clear()
        {
            forestJob = null;   // (un calcul en cours finit dans le vide : plus lu)
            if (root != null) Object.Destroy(root);
            root = null; midRoot = farRoot = null; Ready = false; Progress = -1f; todo = null; bufs = null; level = 0;
            if (rt != null) { rt.Release(); Object.Destroy(rt); rt = null; }
        }

        public static void Update()
        {
            if (startAt > 0 && Time.realtimeSinceStartup >= startAt) { startAt = -1; Begin(); }
            if (forestJob != null) { DrainForests(); return; }
            if (todo == null) return;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            // (un collisionneur a la fois, par morceaux : les routes goudronnees ou l'herbe d'un seul tenant prenaient 120 a
            // 400 ms d'un coup)
            while (sw.Elapsed.TotalMilliseconds < 4f)
            {
                if (cur == null) { if (todoAt >= todo.Count) break; cur = Add(todo[todoAt++]); }
                if (!cur.MoveNext()) cur = null;
            }
            Progress = todo.Count > 0 ? (float)todoAt / todo.Count : 1f;
            if (todoAt >= todo.Count && cur == null) Finish();
        }

        static void Begin()
        {
            GameObject ground = Game.FindAny("MAP/MESH/TERRAIN_OBJ");
            if (ground == null) { Log.Warn("carte : sol du jeu introuvable (MAP/MESH/TERRAIN_OBJ)"); return; }
            bool any = false; Bounds b = new Bounds();
            foreach (Collider c in ground.GetComponentsInChildren<Collider>(true)) { if (c.isTrigger) continue; if (!any) { b = c.bounds; any = true; } else b.Encapsulate(c.bounds); }
            if (!any) return;
            Log.Info("carte : sol du jeu de " + b.min.ToString("F0") + " a " + b.max.ToString("F0"));   // (bornes du monde : « Verifier la sauvegarde » du lanceur, 26/10)
            Size = Mathf.Ceil(Mathf.Max(b.size.x, b.size.z) * 1.02f);
            MinX = b.center.x - Size / 2f; MinZ = b.center.z - Size / 2f;
            root = new GameObject("MWCoop-Carte");
            Object.DontDestroyOnLoad(root);
            midRoot = new GameObject("routes-moyen"); midRoot.transform.parent = root.transform;
            farRoot = new GameObject("routes-loin"); farRoot.transform.parent = root.transform;
            Shader sh = Shader.Find("Sprites/Default") ?? Shader.Find("GUI/Text Shader") ?? Shader.Find("Particles/Alpha Blended");
            mat = new Material(sh);
            mats.Clear();
            // a traiter : collisionneurs du sol et des routes, des batiments et du decor (fixes)
            todo = new List<Collider>();
            foreach (Collider c in Object.FindObjectsOfType<Collider>())
            {
                if (c.isTrigger || c.attachedRigidbody != null) continue;
                int l = c.gameObject.layer;
                if (l != 0 && l != 10 && !(Game.Msc && (c.name == "Asphalt" || c.name == "Road" || c.name == "DirtRoad" || c.name == "Gravel" || c.name == "AIRPORT"))) continue;
                if (!(c is MeshCollider) && !(c is BoxCollider)) continue;
                todo.Add(c);
            }
            // MSC : niveau de l'eau = la surface du lac du jeu (MAP/LakeNice/Lake/Tile), -4,5 m sinon (flottaison du bateau)
            GameObject tile = Game.Msc ? Game.FindAny("MAP/LakeNice/Lake/Tile") : null;
            waterY = tile != null ? tile.transform.position.y : -4.5f;
            if (Game.Msc) Log.Info("carte : niveau de l'eau " + waterY.ToString("F2") + (tile != null ? " (surface du lac)" : " (par defaut)"));
            todoAt = 0; cur = null; bufs = new Dictionary<int, Buf>(); level = 0; meshCount = 0; triCount = 0;
            covered = new bool[G * G]; lakeCov = new bool[G * G];
            Progress = 0f;
            Log.Info("carte : construction (" + todo.Count + " collisionneurs), sol " + b.min.ToString("F0") + " - " + b.max.ToString("F0") + ", shader " + (sh != null ? sh.name : "?"));
        }

        static bool KindOf(Collider c, out Kind k, out bool lakeCheck)
        {
            k = Snow; lakeCheck = false;
            string n = c.name, r = Game.RootName(c.transform);
            if (r == "PLAYER" || r.StartsWith("MWCoop")) return false;
            if (Game.Msc) return KindOfMsc(c, n, r, out k, out lakeCheck);
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

        // My Summer Car (releve du 19/10, Autotest=msccarte) : le sol est MAP/MESH/TERRAIN_OBJ -- 4tie (la grande route) et
        // Asphalt goudronnes, Roadside (bas-cotes), Road / DirtRoad / Gravel (chemins), Pavement, Grass1/2, Fields, RockPale /
        // Rockwall, Landfill (decharge) ; MAP/MESH : LAKEBED (fond des lacs), SWAMP, AIRPORT, TRACKFIELD, RAILROAD, ponts. Sous
        // MAP aussi les murs d'arbres et poteaux (treewallcoll, TREES*_COLL, ELEC_POLES_COLL : en minuscules ou majuscules,
        // ecartes) et les granges (MAP/Buildings). Ailleurs (YARD, PERAJARVI, JOBS, REPAIRSHOP...) : des batiments.
        static bool KindOfMsc(Collider c, string n, string r, out Kind k, out bool lakeCheck)
        {
            k = Snow; lakeCheck = false;
            string ln = n.ToLowerInvariant(), parent = c.transform.parent != null ? c.transform.parent.name : "";
            if (ln.Contains("tree") || ln.Contains("elec") || ln.Contains("foliage") || ln.Contains("norain") || ln.Contains("sign")
                || parent.ToLowerInvariant().Contains("tree") || parent == "RAILROAD_TUNNEL" || n == "RAILROAD_TUNNEL") return false;
            if (r == "MAP")
            {
                if (parent == "TERRAIN_OBJ")
                    switch (n)
                    {
                        case "4tie": case "Asphalt": k = Asphalt; return true;
                        case "Roadside": k = Shoulder; return true;
                        case "Road": case "DirtRoad": case "Gravel": k = DirtMsc; return true;
                        case "Pavement": k = Pavement; return true;
                        case "Fields": k = Field; return true;   // (maillage grossier sous tout le reste : sous l'herbe, jamais de l'eau)
                        case "RockPale": case "Rockwall": k = Rock; return true;
                        case "Landfill": k = Landfill; return true;
                        default: lakeCheck = true; return true;   // Grass1, Grass2... (lacs : sous la surface)
                    }
                switch (n)
                {
                    case "LAKEBED": k = LakeBed; return true;   // (grossier, deborde sous les rives : sous l'herbe, qui a ses lacs)
                    case "SWAMP": case "SwampCollider2": k = Swamp; return true;
                    case "AIRPORT": k = Pavement; return true;
                    case "TRACKFIELD": k = DirtMsc; return true;
                    case "ROCKS": k = Rock; return true;
                    case "RAILROAD": case "RailCol": case "PLANKS": k = Rail; return true;
                    case "BRIDGE_highway": k = Asphalt; return true;
                    case "BRIDGE_dirt": k = DirtMsc; return true;
                }
                if (Recon.Path(c.transform).Contains("/Buildings/") || parent.StartsWith("Barn")) { k = Building; return BigEnough(c); }
                if (c.gameObject.layer == 10) return false;   // (autres surfaces du sol : deja dans TERRAIN_OBJ)
                k = Structure; return BigEnough(c);
            }
            k = Building; return BigEnough(c);
        }

        // batiments : pas les petits objets (meubles, panneaux)
        static bool BigEnough(Collider c) { Vector3 s = c.bounds.size; return Mathf.Max(s.x, s.z) >= 2.5f; }

        static System.Collections.IEnumerator cur;
        static float waterY = -4.5f;
        const int Step = 4000;   // (triangles ou sommets par morceau)
        static System.Collections.IEnumerator Add(Collider c)
        {
            Kind k; bool lakeCheck;
            if (c == null || !KindOf(c, out k, out lakeCheck)) yield break;
            Transform t = c.transform;
            if (c is BoxCollider)
            {
                var bc = (BoxCollider)c;
                Vector3 h = bc.size / 2f;
                Vector3 p0 = t.TransformPoint(bc.center + new Vector3(-h.x, 0, -h.z)), p1 = t.TransformPoint(bc.center + new Vector3(h.x, 0, -h.z)),
                        p2 = t.TransformPoint(bc.center + new Vector3(h.x, 0, h.z)), p3 = t.TransformPoint(bc.center + new Vector3(-h.x, 0, h.z));
                Tri(p0, p1, p2, k); Tri(p0, p2, p3, k);
                yield break;
            }
            Mesh m = ((MeshCollider)c).sharedMesh;
            if (m == null) yield break;
            Vector3[] v = null; int[] tr = null;
            try { v = m.vertices; tr = m.triangles; } catch { }
            if (v == null || tr == null || v.Length == 0) yield break;
            yield return null;
            var w = new Vector3[v.Length];
            Matrix4x4 mx = t.localToWorldMatrix;
            for (int i = 0; i < v.Length; i++) { w[i] = mx.MultiplyPoint3x4(v[i]); if (i % (Step * 4) == Step * 4 - 1) yield return null; }
            // routes goudronnees : bordure foncee (aretes de bord du maillage, en bandes de 1,4 m), sous le jaune
            if (k.Y == Asphalt.Y) { var ed = Edges(w, tr, AsphaltEdge, 1.4f); while (ed.MoveNext()) yield return null; }
            for (int i = 0; i + 2 < tr.Length; i += 3)
            {
                if (i % (Step * 3) == Step * 3 - 3) yield return null;
                Vector3 a = w[tr[i]], b = w[tr[i + 1]], d = w[tr[i + 2]];
                Kind kk = k;
                if (lakeCheck && Game.Msc) { if (a.y < waterY && b.y < waterY && d.y < waterY) kk = Lake; }   // (eau libre : le fond, sous la surface)
                else if (lakeCheck && a.y < -3.9f && b.y < -3.9f && d.y < -3.9f)
                {
                    Vector3 nrm = Vector3.Cross(b - a, d - a);
                    if (nrm.sqrMagnitude > 1e-6f && Mathf.Abs(nrm.normalized.y) > 0.995f) kk = Lake;
                }
                Tri(a, b, d, kk);
            }
            // MSC : routes elargies pour les vues de loin (bordure foncee sous le jaune pour le goudron)
            if (Game.Msc && (k.Y == Asphalt.Y || k.Y == DirtMsc.Y || k.Y == Rail.Y))
            {
                bool tar = k.Y == Asphalt.Y;
                noCover = true;
                for (level = 1; level <= 2; level++)
                {
                    float wd = (tar ? 7f : k.Y == Rail.Y ? 3f : 4.5f) * (level == 1 ? 1f : 3f);
                    if (tar) { var eb = Edges(w, tr, AsphaltEdge, wd * 1.5f); while (eb.MoveNext()) yield return null; }
                    var ed = Edges(w, tr, k, wd); while (ed.MoveNext()) yield return null;
                }
                level = 0; noCover = false;
            }
        }

        // Aretes de bord (une seule face) : bandes de largeur w autour. Sommets reperes par leur position (au centimetre) :
        // les maillages de MSC ont des sommets en double le long des coutures, qui faisaient des bords partout.
        static System.Collections.IEnumerator Edges(Vector3[] w, int[] tr, Kind k, float width)
        {
            var ids = new Dictionary<long, int>();
            var vid = new int[w.Length];
            for (int i = 0; i < w.Length; i++)
            {
                long q = ((long)Mathf.RoundToInt(w[i].x * 50f) << 32) ^ (uint)Mathf.RoundToInt(w[i].z * 50f);
                int id; if (!ids.TryGetValue(q, out id)) { id = i; ids[q] = id; }
                vid[i] = id;
                if (i % (Step * 4) == Step * 4 - 1) yield return null;
            }
            var count = new Dictionary<long, int>();
            for (int i = 0; i + 2 < tr.Length; i += 3)
            {
                if (i % (Step * 3) == Step * 3 - 3) yield return null;
                for (int e = 0; e < 3; e++)
                {
                    int a = vid[tr[i + e]], b = vid[tr[i + (e + 1) % 3]];
                    if (a == b) continue;
                    long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
                    int n; count.TryGetValue(key, out n); count[key] = n + 1;
                }
            }
            int done = 0;
            foreach (var kv in count)
            {
                if (++done % (Step * 2) == 0) yield return null;
                if (kv.Value != 1) continue;
                Vector3 a = w[(int)(kv.Key >> 32)], b = w[(int)(kv.Key & 0xffffffff)];
                Vector3 d = new Vector3(b.x - a.x, 0f, b.z - a.z);
                if (d.sqrMagnitude < 1e-6f) continue;
                Vector3 side = new Vector3(-d.z, 0f, d.x).normalized * (width / 2f);
                // (prolongee d'une demi-largeur aux deux bouts : pas de trous aux angles)
                Vector3 ext = d.normalized * (width / 2f);
                Vector3 a2 = a - ext, b2 = b + ext;
                Tri(a2 - side, a2 + side, b2 + side, k); Tri(a2 - side, b2 + side, b2 - side, k);
            }
        }

        // Forets : les trous du sol enfermes par lui (autour des collines, ou rien n'est praticable) ; le reste du vide est
        // hors de la carte (demande de JD, 10/10 : fond du panneau, seules les forets en vert). Grille de couverture du sol
        // (cellules de ~3 m) : cases vides reliees au bord = dehors (remplissage), les autres = foret.
        const int G = 2048;   // (cases de ~3 m : bords des forets fins)
        static bool[] covered, lakeCov;
        static void Cover(Vector3 a, Vector3 b, Vector3 c, bool water)
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
                    if (l1 >= -0.02f && l2 >= -0.02f && l1 + l2 <= 1.02f) { covered[z * G + x] = true; if (water) lakeCov[z * G + x] = true; }
                }
            int k0 = Mathf.Clamp((int)az, 0, G - 1) * G + Mathf.Clamp((int)ax, 0, G - 1);
            covered[k0] = true; if (water) lakeCov[k0] = true;
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
            // Trous enfermes, un par un : entoures surtout d'eau (lakeCov), c'est un lac -- le fond d'un lac de MSC a des
            // trous, qui faisaient des bois au milieu de l'eau (JD, 19/10) ; sinon une foret.
            var label = new int[G * G];
            var isLake = new List<bool> { false };
            for (int s0 = 0; s0 < G * G; s0++)
            {
                if (covered[s0] || outside[s0] || label[s0] != 0) continue;
                int id = isLake.Count, water = 0, land = 0;
                label[s0] = id; q.Enqueue(s0);
                while (q.Count > 0)
                {
                    int k = q.Dequeue(), x = k % G, z = k / G;
                    for (int d = 0; d < 4; d++)
                    {
                        int nx = x + (d == 0 ? -1 : d == 1 ? 1 : 0), nz = z + (d == 2 ? -1 : d == 3 ? 1 : 0);
                        if (nx < 0 || nz < 0 || nx >= G || nz >= G) continue;
                        int n = nz * G + nx;
                        if (covered[n]) { if (lakeCov[n]) water++; else land++; }
                        else if (!outside[n] && label[n] == 0) { label[n] = id; q.Enqueue(n); }
                    }
                }
                isLake.Add(water > 3 * land);   // (presque tout autour : une ile boisee garde ses arbres)
            }
            // foret (ou lac) = trou de cette sorte ; elargi d'une case sous le sol (bords sans jour), pas sur le dehors
            var forest = new bool[G * G];
            var lake = new bool[G * G];
            int cells = 0, lakeCells = 0;
            for (int z = 0; z < G; z++)
                for (int x = 0; x < G; x++)
                {
                    int k = z * G + x;
                    if (outside[k]) continue;
                    bool f = false, l = false;
                    for (int dz = -1; dz <= 1; dz++)
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            int nx = x + dx, nz = z + dz;
                            if (nx < 0 || nz < 0 || nx >= G || nz >= G) continue;
                            int n = nz * G + nx;
                            if (covered[n] || outside[n]) continue;
                            if (isLake[label[n]]) l = true; else f = true;
                        }
                    if (f) { forest[k] = true; cells++; }
                    else if (l) { lake[k] = true; lakeCells++; }
                }
            float cell = Size / G;
            into = fout; Contour(forest, cell);
            into = lout; Contour(lake, cell);
            forestCells = cells; lakeHoleCells = lakeCells;
        }

        // contour lisse (marching squares sur les centres des cases) ; l'interieur plein en bandes par ligne
        static void Contour(bool[] grid, float cell)
        {
            for (int zz = 0; zz < G - 1; zz++)
            {
                int runStart = -1;
                for (int xx = 0; xx < G - 1; xx++)
                {
                    int c0 = grid[zz * G + xx] ? 1 : 0, c1 = grid[zz * G + xx + 1] ? 2 : 0, c2 = grid[(zz + 1) * G + xx + 1] ? 4 : 0, c3 = grid[(zz + 1) * G + xx] ? 8 : 0;
                    int cs = c0 | c1 | c2 | c3;
                    if (cs == 15) { if (runStart < 0) runStart = xx; continue; }
                    if (runStart >= 0) { Quad(runStart, xx, zz, cell); runStart = -1; }
                    if (cs != 0) Marching(cs, xx, zz, cell);
                }
                if (runStart >= 0) Quad(runStart, G - 1, zz, cell);
            }
        }
        static List<Vector3> into;
        // (points : centres des cases)
        static Vector3 P(float x, float z, float cell) { return new Vector3(MinX + (x + 0.5f) * cell, 0f, MinZ + (z + 0.5f) * cell); }
        static void Quad(int x0, int x1, int z, float cell)
        {
            Vector3 a = P(x0, z, cell), b = P(x1, z, cell), c = P(x1, z + 1, cell), d = P(x0, z + 1, cell);
            into.Add(a); into.Add(b); into.Add(c); into.Add(a); into.Add(c); into.Add(d);
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
            for (int i = 1; i + 1 < pts.Length; i++) { into.Add(pts[0]); into.Add(pts[i]); into.Add(pts[i + 1]); }
        }

        static void Visit(int k, bool[] outside, Queue<int> q) { if (!covered[k] && !outside[k]) { outside[k] = true; q.Enqueue(k); } }
        static bool noCover;

        static void Tri(Vector3 a, Vector3 b, Vector3 c, Kind k)
        {
            // (triangles immenses et fins du bord du sol : des traits en travers de la carte)
            if (!noCover && (Flat(a, b) > 1000f || Flat(b, c) > 1000f || Flat(c, a) > 1000f)) return;
            if (!noCover && covered != null && k.Y <= Asphalt.Y) Cover(a, b, c, k.Y == Lake.Y || k.Y == LakeBed.Y);   // (sol, routes : pas les batiments)
            int key = Key(k);
            Buf buf;
            if (!bufs.TryGetValue(key, out buf)) bufs[key] = buf = new Buf();
            if (buf.V.Count > 59990) { Flush(key, buf); bufs[key] = buf = new Buf(); }
            int n = buf.V.Count;
            buf.V.Add(new Vector3(a.x, Depth + k.Y, a.z)); buf.V.Add(new Vector3(b.x, Depth + k.Y, b.z)); buf.V.Add(new Vector3(c.x, Depth + k.Y, c.z));
            buf.C.Add(k.C); buf.C.Add(k.C); buf.C.Add(k.C);
            // (vu d'en haut : sens des aiguilles d'une montre, sinon la face est tournee)
            float cross = (b.x - a.x) * (c.z - a.z) - (b.z - a.z) * (c.x - a.x);
            if (cross > 0f) { buf.T.Add(n); buf.T.Add(n + 2); buf.T.Add(n + 1); } else { buf.T.Add(n); buf.T.Add(n + 1); buf.T.Add(n + 2); }
            triCount++;
        }

        static float Flat(Vector3 a, Vector3 b) { float dx = a.x - b.x, dz = a.z - b.z; return Mathf.Sqrt(dx * dx + dz * dz); }

        // Un maillage de la sorte 'key' (niveau = key / 1000 : carte, routes-moyen, routes-loin), file de rendu de sa sorte.
        static void Flush(int key, Buf buf)
        {
            if (buf == null || buf.V.Count == 0) return;
            int lv = key / 1000, rank = key % 1000;
            var go = new GameObject("carte" + meshCount++ + "-" + rank);
            go.layer = MapLayer;
            go.transform.parent = lv == 1 ? midRoot.transform : lv == 2 ? farRoot.transform : root.transform;
            var m = new Mesh();
            m.vertices = buf.V.ToArray(); m.colors32 = buf.C.ToArray(); m.triangles = buf.T.ToArray();
            m.RecalculateBounds();
            go.AddComponent<MeshFilter>().sharedMesh = m;
            var r = go.AddComponent<MeshRenderer>();
            Material km;
            if (!mats.TryGetValue(rank, out km)) { km = new Material(mat); km.renderQueue = 3000 + rank; mats[rank] = km; }
            r.sharedMaterial = km;
            r.castShadows = false; r.receiveShadows = false;
        }

        static void FlushAll()
        {
            if (bufs == null) return;
            foreach (var kv in bufs) Flush(kv.Key, kv.Value);
            bufs.Clear();
        }

        // Forets : calculees a part (remplissage et contours de 4 millions de cases : ~400 ms d'un coup, a-coup de la carte
        // 20 s apres l'arrivee), puis leurs triangles ajoutes par paquets de 4 ms.
        static System.Threading.Thread forestJob;
        static volatile bool forestDone;
        static List<Vector3> fout, lout;
        static int foutAt, loutAt, forestCells, lakeHoleCells;
        static void Finish()
        {
            todo = null;
            fout = new List<Vector3>(); lout = new List<Vector3>();
            foutAt = 0; loutAt = 0; forestDone = false;
            forestJob = new System.Threading.Thread(() => { try { Forests(); } catch (System.Exception e) { forestError = e.Message; } forestDone = true; });
            forestJob.IsBackground = true;
            forestJob.Start();
        }
        static string forestError;
        static void DrainForests()
        {
            if (!forestDone) return;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            noCover = true;
            if (forestError != null) { foutAt = fout.Count; loutAt = lout.Count; }
            while (foutAt + 2 < fout.Count && sw.Elapsed.TotalMilliseconds < 4f)
            {
                for (int i = 0; i < 300 && foutAt + 2 < fout.Count; i++, foutAt += 3) Tri(fout[foutAt], fout[foutAt + 1], fout[foutAt + 2], ForestK);
            }
            while (foutAt + 2 >= fout.Count && loutAt + 2 < lout.Count && sw.Elapsed.TotalMilliseconds < 4f)
            {
                for (int i = 0; i < 300 && loutAt + 2 < lout.Count; i++, loutAt += 3) Tri(lout[loutAt], lout[loutAt + 1], lout[loutAt + 2], Lake);
            }
            noCover = false;
            if (foutAt + 2 < fout.Count || loutAt + 2 < lout.Count) return;
            forestJob = null; fout = null; lout = null; covered = null; lakeCov = null;
            if (forestError != null) { Log.Warn("carte : forets : " + forestError); forestError = null; }
            else Log.Info("carte : forets " + forestCells + " cases de " + (Size / G).ToString("F1") + " m, trous dans l'eau (lacs) " + lakeHoleCells);
            FlushAll();
            bufs = null;
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
            if (midRoot != null) midRoot.SetActive(metersPerPixel > MidMpp && metersPerPixel <= FarMpp);
            if (farRoot != null) farRoot.SetActive(metersPerPixel > FarMpp);
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
