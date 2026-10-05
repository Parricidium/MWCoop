using System.Collections.Generic;
using System.IO;
using HutongGames.PlayMaker;
using MWCoop.Net;
using UnityEngine;

namespace MWCoop
{
    // Couleur de la CORRIS choisie dans le lanceur (demande de JD) et apercu 3D du lanceur.
    //  - Export : a chaque partie (20 s apres le chargement), le maillage de la CORRIS est ecrit dans
    //    MWCoop\cache\corris.mesh depuis le jeu du joueur (aucune donnee du jeu dans le depot ni dans
    //    le zip) : carrosserie, pieces montees, et pieces pas encore montees (portes, capot, roues...)
    //    posees sur leur point de montage, pour que l'apercu montre la voiture entiere. Format :
    //      "MWCM", u32 version=1, u32 n, puis n fois :
    //        u16 longueur + nom UTF-8, u8 peignable (1 = prend la couleur choisie),
    //        3 x f32 couleur de base (0..1), u32 nb sommets + nb x (3 x f32) en repere de la voiture
    //        (x droite, y haut, z avant, metres), u32 nb indices + nb x u32 (triangles).
    //  - Nouvelle partie : la carrosserie tire sa couleur au hasard des le chargement. Au menu, le
    //    passage de Licence/Buttons/ButtonBegin (Commencer) par 'Generate ID' marque une nouvelle
    //    partie ; une fois en jeu (12 s, apres l'injection des actions de Paint), chez l'hote ou en
    //    solo, si [Coop] CouleurVoiture=RRGGBB est fixee, la carrosserie est repeinte de cette couleur
    //    comme d'un coup de bombe (REPAINT) : la synchro de peinture la donne aux invites.
    public static class CarColor
    {
        static float exportAt = -1, applyAt = -1;
        static bool exported, applied;
        public static bool NewGame;      // une nouvelle partie vient d'etre lancee depuis le menu

        public static string MeshPath
        {
            get { return Path.Combine(Path.Combine(System.Environment.GetEnvironmentVariable("MWCOOP_DIR") ?? "MWCoop", "cache"), "corris.mesh"); }
        }

        public static void OnLevelLoaded()
        {
            exported = applied = false;
            exportAt = PlayerSync.InGame ? Time.realtimeSinceStartup + 20f : -1;
            applyAt = PlayerSync.InGame && NewGame ? Time.realtimeSinceStartup + 12f : -1;
            if (PlayerSync.InGame) NewGame = false;
        }

        static PlayMakerFSM begin;

        public static void Update()
        {
            if (Application.loadedLevelName == "MainMenu")
            {
                // Bouton Commencer de la carte du permis : 'Generate ID' = nouvelle partie.
                if (begin == null) { GameObject b = Game.FindAny("Licence/Buttons/ButtonBegin"); if (b != null) begin = Game.FsmOn(b, "SetSize"); }
                if (begin != null && !NewGame && (begin.ActiveStateName == "Generate ID" || begin.ActiveStateName == "State 3" || begin.ActiveStateName == "Load"))
                {
                    NewGame = true;
                    Log.Info("voiture : nouvelle partie lancee");
                }
                return;
            }
            if (applyAt > 0 && Time.realtimeSinceStartup >= applyAt) { applyAt = -1; ApplyChosen(); }
            if (exportAt < 0 || Time.realtimeSinceStartup < exportAt) return;
            if (!exported)
            {
                exported = true;
                try { Log.Info("voiture : apercu exporte, " + Export()); } catch (System.Exception e) { Log.Warn("voiture : export " + e.Message); }
            }
        }

        static bool IsPaintable(Transform t, Transform root)
        {
            for (; t != null && t != root.parent; t = t.parent)
                if (Game.FsmOn(t.gameObject, "Paint") != null) return true;
            return false;
        }

        static string Export()
        {
            GameObject car = GameObject.Find("CORRIS");
            if (car == null) return "CORRIS introuvable";
            Transform root = car.transform;
            var parts = new List<Piece>();
            var extra = new List<Piece>();
            // Voiture COMPLETE d'origine (demande de JD : le jeu tire l'etat de la voiture au hasard, l'apercu ne doit
            // pas montrer la sauvegarde) : chaque point de montage recoit la piece de serie que son createur y pose
            // (automates 'Spawn' : Prefab -> VINP), les roues sont les jantes acier 13". Ce qui est monte sur ces
            // points, le tuning et les pieces qui trainent sont ignores. [Coop] ApercuComplet=0 : l'etat de la partie.
            bool stockOnly = Config.GetInt("Coop", "ApercuComplet", 1) != 0;
            Dictionary<Transform, GameObject> stock = stockOnly ? StockParts(root) : new Dictionary<Transform, GameObject>();
            foreach (MeshFilter mf in car.GetComponentsInChildren<MeshFilter>())
            {
                Renderer r = mf.GetComponent<Renderer>();
                if (r != null && r.enabled && mf.sharedMesh != null && !Replaced(r.transform, root, stock, stockOnly))
                    parts.Add(new Piece { R = r, M = mf.sharedMesh, ToCar = root.worldToLocalMatrix * r.transform.localToWorldMatrix, Paint = IsPaintable(r.transform, root) });
            }
            foreach (SkinnedMeshRenderer s in car.GetComponentsInChildren<SkinnedMeshRenderer>())
                if (s.enabled && s.sharedMesh != null && !Replaced(s.transform, root, stock, stockOnly))
                    parts.Add(new Piece { R = s, M = s.sharedMesh, ToCar = root.worldToLocalMatrix * s.transform.localToWorldMatrix, Paint = IsPaintable(s.transform, root) });
            HashSet<Transform> points;
            if (stockOnly)
            {
                points = new HashSet<Transform>(stock.Keys);
                foreach (KeyValuePair<Transform, GameObject> kv in stock) AddPrefab(root, kv.Key, kv.Value, parts);
            }
            else points = AddLooseParts(root, parts);
            AddPointMeshes(root, points, extra);
            if (!stockOnly || !AddStockWheels(root, parts)) AddWheels(car, root, extra);

            Directory.CreateDirectory(Path.GetDirectoryName(MeshPath));
            int written = 0, tris = 0;
            using (var w = new BinaryWriter(File.Create(MeshPath)))
            {
                w.Write(new[] { (byte)'M', (byte)'W', (byte)'C', (byte)'M' });
                w.Write(1);
                long countPos = w.BaseStream.Position;
                w.Write(0);
                parts.AddRange(extra);
                foreach (Piece pc in parts)
                {
                    Renderer r = pc.R;
                    Vector3[] v;
                    int[] idx;
                    Color c;
                    string pieceName;
                    if (r == null)
                    {
                        // Piece fabriquee (roue) : sommets deja dans le repere de la voiture.
                        v = pc.V; idx = pc.T; c = pc.C; pieceName = pc.Name;
                    }
                    else
                    {
                        Mesh m = pc.M;
                        Material mat = r.sharedMaterial;
                        string mn = mat != null ? mat.name.ToLowerInvariant() : "";
                        if (mn.Contains("shadow") || mn.Contains("alpha")) continue;
                        // Calques de givre/buee des vitres (doorwindow_frozen, sidewindow_frozen...) : vitres blanches sinon.
                        string rn = r.name.ToLowerInvariant();
                        if (rn.Contains("frozen") || rn.Contains("frost") || mn.Contains("frozen") || mn.Contains("frost")) continue;
                        bool glass = mn.Contains("glass") || mn.Contains("window");
                        if (!m.isReadable) continue;
                        v = m.vertices;
                        idx = m.triangles;
                        c = glass ? new Color(0.13f, 0.17f, 0.21f) : mat != null && mat.HasProperty("_Color") ? mat.color : Color.gray;
                        pieceName = r.name;
                        if (glass) pc.Paint = false;
                    }
                    if (v.Length == 0 || idx.Length == 0 || v.Length > 60000) continue;
                    byte[] name = System.Text.Encoding.UTF8.GetBytes(pieceName);
                    w.Write((ushort)name.Length); w.Write(name);
                    w.Write((byte)(pc.Paint ? 1 : 0));
                    w.Write(c.r); w.Write(c.g); w.Write(c.b);
                    w.Write(v.Length);
                    Matrix4x4 toCar = r == null ? Matrix4x4.identity : pc.ToCar;
                    foreach (Vector3 p in v)
                    {
                        Vector3 q = toCar.MultiplyPoint3x4(p);
                        w.Write(q.x); w.Write(q.y); w.Write(q.z);
                    }
                    w.Write(idx.Length);
                    foreach (int i in idx) w.Write(i);
                    written++;
                    tris += idx.Length / 3;
                }
                w.BaseStream.Position = countPos;
                w.Write(written);
            }
            return written + " morceaux, " + tris + " triangles, " + MeshPath;
        }

        // Points de montage de la CORRIS (hors tuning) -> piece de serie (prefab VIN..., premier par nom).
        static Dictionary<Transform, GameObject> StockParts(Transform root)
        {
            var map = new Dictionary<Transform, GameObject>();
            foreach (Object o in Resources.FindObjectsOfTypeAll(typeof(PlayMakerFSM)))
            {
                var f = (PlayMakerFSM)o;
                if (f.FsmName != "Spawn" || f.hideFlags != HideFlags.None) continue;
                FsmGameObject pre = f.FsmVariables.FindFsmGameObject("Prefab"), vinp = f.FsmVariables.FindFsmGameObject("VINP");
                if (pre == null || vinp == null || pre.Value == null || vinp.Value == null || !pre.Value.name.StartsWith("VIN")) continue;
                Transform pt = vinp.Value.transform;
                if (!pt.IsChildOf(root) || Recon.Path(pt).Contains("/AssembliesTuning/")) continue;
                GameObject had;
                if (!map.TryGetValue(pt, out had) || string.CompareOrdinal(pre.Value.name, had.name) < 0) map[pt] = pre.Value;
            }
            Log.Info("voiture : " + map.Count + " pieces de serie trouvees pour l'apercu");
            return map;
        }

        // Rendu de la voiture remplace par une piece de serie (sous un de ces points), ou tuning monte.
        static bool Replaced(Transform t, Transform root, Dictionary<Transform, GameObject> stock, bool stockOnly)
        {
            if (!stockOnly) return false;
            for (Transform p = t; p != null && p != root; p = p.parent)
            {
                if (stock.ContainsKey(p) || p.name == "AssembliesTuning") return true;
                if (p.name.StartsWith("VINP_Wheel") || p.name.StartsWith("VINP_Hubcap")) return true;   // roues de serie a la place
            }
            return false;
        }

        // Rendus d'un prefab (actifs dans le prefab : un prefab n'est jamais actif dans la scene), poses sur le point.
        static void AddPrefab(Transform root, Transform point, GameObject prefab, List<Piece> parts)
        {
            AddPrefab(root, point, prefab, parts, Matrix4x4.identity);
        }

        static void AddPrefab(Transform root, Transform point, GameObject prefab, List<Piece> parts, Matrix4x4 scale)
        {
            Matrix4x4 place = root.worldToLocalMatrix * point.localToWorldMatrix * scale * prefab.transform.worldToLocalMatrix;
            foreach (MeshFilter mf in prefab.GetComponentsInChildren<MeshFilter>(true))
            {
                Renderer r = mf.GetComponent<Renderer>();
                if (r == null || !r.enabled || mf.sharedMesh == null || !ActiveIn(mf.transform, prefab.transform)) continue;
                if (mf.transform != prefab.transform && mf.transform.parent != null && mf.transform.parent.name == "Bolts") continue;
                parts.Add(new Piece { R = r, M = mf.sharedMesh, ToCar = place * r.transform.localToWorldMatrix, Paint = IsPaintable(r.transform, prefab.transform) });
            }
        }

        static bool ActiveIn(Transform t, Transform top)
        {
            for (; t != null; t = t.parent) { if (!t.gameObject.activeSelf) return false; if (t == top) return true; }
            return true;
        }

        // Jantes acier 13" (RIM13STEELa0, celles des createurs de roues) sur les quatre points VINP_Wheel*.
        static bool AddStockWheels(Transform root, List<Piece> parts)
        {
            GameObject rim = null;
            foreach (Object o in Resources.FindObjectsOfTypeAll(typeof(GameObject)))
                if (o.name == "RIM13STEELa0" && ((GameObject)o).transform.parent == null) { rim = (GameObject)o; break; }
            if (rim == null) return false;
            var points = new List<Transform>();
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                if (t.name.StartsWith("VINP_Wheel") && t.name.Length == "VINP_WheelFL".Length) points.Add(t);
            if (points.Count == 0) return false;
            // Roue de serie : jante 13" (rayon 16,5 cm ; le prefab est mis a l'echelle du pneu a l'execution, ScalePivot)
            // dans un pneu 155R13 (rayon 29 cm). Le pneu, le jeu le cree a l'execution (Use 'GetTire') : fabrique ici.
            // Pas le rayon de la roue physique : sans pneu monte, elle garde 13,5 cm (roues minuscules).
            const float RimR = 0.165f, TireR = 0.29f, TireW = 0.16f;
            Transform meshT = rim.transform.Find("ScalePivot/meshrim");
            MeshFilter rmf = meshT != null ? meshT.GetComponent<MeshFilter>() : null;
            Vector3 ext = rmf != null && rmf.sharedMesh != null ? Vector3.Scale(rmf.sharedMesh.bounds.extents, meshT.lossyScale) : Vector3.zero;
            float rimR = Mathf.Max(ext.x, Mathf.Max(ext.y, ext.z));
            foreach (Wheel wh in root.GetComponentsInChildren<Wheel>(true))
            {
                Transform t = wh.transform, best = null;
                foreach (Transform pt in points) if (best == null || (pt.position - t.position).sqrMagnitude < (best.position - t.position).sqrMagnitude) best = pt;
                // Hauteur de repos : la voiture de l'apercu ne pose pas sur ses roues (suspension en detente, roue 8,6 cm
                // sous son point de fixation WHEELc_*, sous les ailes) ; chargee, la roue remonte a ce point.
                Vector3 center = root.InverseTransformPoint(best != null ? best.position : t.position);
                Vector3 lift = new Vector3(0f, root.InverseTransformPoint(t.position).y - center.y, 0f);
                if (best != null)
                {
                    float k = rimR > 0.05f ? RimR / rimR : 1f;
                    int from = parts.Count;
                    AddPrefab(root, best, rim, parts, Matrix4x4.Scale(new Vector3(k, k, k)));
                    for (int i = from; i < parts.Count; i++)
                    {
                        parts[i].Paint = false;   // acier, pas la couleur de la carrosserie
                        parts[i].ToCar = Matrix4x4.TRS(lift, Quaternion.identity, Vector3.one) * parts[i].ToCar;
                    }
                }
                parts.Add(Ring(wh.name + " pneu", center + lift, root.InverseTransformDirection(t.right).normalized,
                               TireR, RimR * 0.97f, TireW, new Color(0.07f, 0.07f, 0.075f)));
            }
            return true;
        }

        class Piece
        {
            public Renderer R; public Mesh M; public Matrix4x4 ToCar; public bool Paint;
            public string Name; public Vector3[] V; public int[] T; public Color C;   // piece fabriquee
        }

        // Roues : une partie neuve n'en a pas encore de montees. A chaque roue physique (Wheel) sans
        // modele visible, un pneu et une jante simples a sa place, de son rayon et de sa largeur.
        static void AddWheels(GameObject car, Transform root, List<Piece> parts)
        {
            foreach (Wheel wh in car.GetComponentsInChildren<Wheel>(true))
            {
                bool shown = false;
                if (wh.model != null)
                    foreach (Renderer r in wh.model.GetComponentsInChildren<Renderer>())
                        if (r.enabled) { shown = true; break; }
                if (shown) continue;
                Transform t = wh.transform;
                Vector3 c = root.InverseTransformPoint(t.position);
                Vector3 axis = root.InverseTransformDirection(t.right).normalized;
                float rad = wh.radius > 0.1f ? wh.radius : 0.3f, width = wh.width > 0.05f ? wh.width : 0.18f;
                parts.Add(Ring(wh.name + " pneu", c, axis, rad, rad * 0.64f, width, new Color(0.07f, 0.07f, 0.075f)));
                parts.Add(Ring(wh.name + " jante", c, axis, rad * 0.64f, 0.02f, width * 0.8f, new Color(0.62f, 0.63f, 0.65f)));
            }
        }

        // Anneau epais (cylindre creux ferme) : 'outer' / 'inner' rayons, 'width' le long de 'axis'.
        static Piece Ring(string name, Vector3 c, Vector3 axis, float outer, float inner, float width, Color col)
        {
            const int N = 28;
            Vector3 u = Vector3.Cross(axis, Vector3.up);
            if (u.sqrMagnitude < 1e-4f) u = Vector3.Cross(axis, Vector3.forward);
            u.Normalize();
            Vector3 vv = Vector3.Cross(axis, u).normalized;
            Vector3 h = axis * (width * 0.5f);
            var verts = new List<Vector3>();
            for (int i = 0; i < N; i++)
            {
                float a = i * Mathf.PI * 2f / N;
                Vector3 d = u * Mathf.Cos(a) + vv * Mathf.Sin(a);
                verts.Add(c + d * outer + h); verts.Add(c + d * outer - h);
                verts.Add(c + d * inner + h); verts.Add(c + d * inner - h);
            }
            var tris = new List<int>();
            for (int i = 0; i < N; i++)
            {
                int a = i * 4, b = ((i + 1) % N) * 4;
                // exterieur, interieur, flanc +, flanc - (les deux faces : le rendu ne trie pas l'orientation)
                Quad(tris, a, b, b + 1, a + 1);
                Quad(tris, a + 2, a + 3, b + 3, b + 2);
                Quad(tris, a, a + 2, b + 2, b);
                Quad(tris, a + 1, b + 1, b + 3, a + 3);
            }
            return new Piece { Name = name, V = verts.ToArray(), T = tris.ToArray(), C = col };
        }

        static void Quad(List<int> t, int a, int b, int c, int d)
        {
            t.Add(a); t.Add(b); t.Add(c); t.Add(a); t.Add(c); t.Add(d);
            t.Add(a); t.Add(c); t.Add(b); t.Add(a); t.Add(d); t.Add(c);
        }

        // Pieces pas encore montees dont le point de montage est sur la CORRIS (portes, capot, ailes,
        // pare-chocs, roues...) : posees la ou le jeu les fixerait (SetParent sur le point, position
        // locale nulle). Une seule par point de montage.
        static HashSet<Transform> AddLooseParts(Transform root, List<Piece> parts)
        {
            var points = new HashSet<Transform>();
            var diag = new System.Text.StringBuilder();
            foreach (Object o in Resources.FindObjectsOfTypeAll(typeof(PlayMakerFSM)))
            {
                var f = (PlayMakerFSM)o;
                if (f.hideFlags != HideFlags.None || f.FsmName != "Data") continue;
                FsmGameObject ip = f.FsmVariables.FindFsmGameObject("InstallPoint");
                FsmString id = f.FsmVariables.FindFsmString("ID");
                if (Config.GetInt("Test", "JournalApercu", 0) != 0 && f.name.Contains("(VIN"))
                    diag.Append(f.name).Append(ip == null ? "[pas de var]" : ip.Value == null ? "[point nul]" : "[" + Recon.Path(ip.Value.transform) + "]")
                        .Append(id == null ? "" : " id=" + id.Value).Append(f.transform.IsChildOf(root) ? " monte" : "").Append("; ");
                if (ip == null || ip.Value == null || id == null || id.Value.Length == 0) continue;
                Transform point = ip.Value.transform, part = f.transform;
                if (!point.IsChildOf(root) || part.IsChildOf(root) || points.Contains(point)) continue;
                points.Add(point);
                Matrix4x4 place = root.worldToLocalMatrix * point.localToWorldMatrix * part.worldToLocalMatrix;
                foreach (MeshFilter mf in part.GetComponentsInChildren<MeshFilter>())
                {
                    Renderer r = mf.GetComponent<Renderer>();
                    if (r == null || !r.enabled || mf.sharedMesh == null) continue;
                    if (mf.transform != part && mf.transform.parent != null && mf.transform.parent.name == "Bolts") continue;
                    parts.Add(new Piece { R = r, M = mf.sharedMesh, ToCar = place * r.transform.localToWorldMatrix, Paint = IsPaintable(r.transform, part) });
                }
            }
            if (diag.Length > 0) Log.Info("voiture : pieces " + diag);
            return points;
        }

        // Pieces qui n'existent pas encore dans une partie neuve (capot, pare-chocs, phares...) : chaque
        // point de montage VINP_* vide de la CORRIS garde le maillage de sa piece (Data, OriginalMesh),
        // pose a la place du point. Les variantes de tuning (AssembliesTuning) sont laissees de cote.
        static void AddPointMeshes(Transform root, HashSet<Transform> taken, List<Piece> parts)
        {
            int n = 0;
            if (Config.GetInt("Test", "JournalApercu", 0) != 0)
            {
                var sb = new System.Text.StringBuilder();
                foreach (PlayMakerFSM f in Resources.FindObjectsOfTypeAll(typeof(PlayMakerFSM)))
                {
                    if (f.hideFlags != HideFlags.None || f.FsmName != "Data" || !f.name.StartsWith("VINP_")) continue;
                    FsmBool inst = f.FsmVariables.FindFsmBool("Installed");
                    FsmGameObject active = f.FsmVariables.FindFsmGameObject("ActivePart");
                    FsmObject om = f.FsmVariables.FindFsmObject("OriginalMesh");
                    var mesh = om != null ? om.Value as Mesh : null;
                    sb.Append(Recon.Path(f.transform)).Append(inst != null && inst.Value ? " monte" : "").Append(active != null && active.Value != null ? " actif=" + active.Value.name : "")
                      .Append(mesh != null ? " maillage=" + mesh.name + (mesh.isReadable ? "" : "(illisible)") : " sans maillage").Append(" | ");
                }
                Log.Info("voiture : points " + sb);
                sb.Length = 0;
                foreach (PlayMakerFSM f in Resources.FindObjectsOfTypeAll(typeof(PlayMakerFSM)))
                {
                    if (f.FsmName != "Data" || f.gameObject.activeInHierarchy || f.name.StartsWith("VINP_")) continue;
                    FsmString id = f.FsmVariables.FindFsmString("ID");
                    if (id == null || id.Value.Length > 0) continue;
                    sb.Append(f.name).Append(" [hf ").Append((int)f.hideFlags).Append(", parent ").Append(f.transform.parent != null ? f.transform.parent.name : "-").Append("]");
                    foreach (FsmString s in f.FsmVariables.StringVariables) if (s.Value.Length > 0 && s.Value.Length < 40) sb.Append(' ').Append(s.Name).Append('=').Append(s.Value);
                    foreach (FsmGameObject g in f.FsmVariables.GameObjectVariables) if (g.Value != null) sb.Append(' ').Append(g.Name).Append('=').Append(g.Value.name);
                    foreach (FsmObject o in f.FsmVariables.ObjectVariables) if (o.Value != null) sb.Append(' ').Append(o.Name).Append('=').Append(o.Value.name);
                    sb.Append(" | ");
                }
                Log.Info("voiture : modeles " + sb);
                sb.Length = 0;
                foreach (Wheel wh in root.GetComponentsInChildren<Wheel>(true))
                {
                    sb.Append(wh.name).Append(" pos ").Append(root.InverseTransformPoint(wh.transform.position).ToString("F3"));
                    foreach (System.Reflection.FieldInfo fi in typeof(Wheel).GetFields())
                        if (fi.FieldType == typeof(float) || fi.FieldType == typeof(bool)) sb.Append(' ').Append(fi.Name).Append('=').Append(fi.GetValue(wh));
                    if (wh.model != null) sb.Append(" model ").Append(wh.model.name).Append(' ').Append(root.InverseTransformPoint(wh.model.transform.position).ToString("F3"));
                    sb.Append(" | ");
                }
                Log.Info("voiture : roues " + sb);
            }
            foreach (PlayMakerFSM f in root.GetComponentsInChildren<PlayMakerFSM>(true))
            {
                if (f.FsmName != "Data" || !f.name.StartsWith("VINP_") || taken.Contains(f.transform)) continue;
                if (Recon.Path(f.transform).Contains("/AssembliesTuning/")) continue;
                FsmBool inst = f.FsmVariables.FindFsmBool("Installed");
                FsmGameObject active = f.FsmVariables.FindFsmGameObject("ActivePart");
                if (inst != null && inst.Value || active != null && active.Value != null) continue;
                FsmObject om = f.FsmVariables.FindFsmObject("OriginalMesh");
                var mesh = om != null ? om.Value as Mesh : null;
                if (mesh == null || !mesh.isReadable) continue;
                string mn = mesh.name.ToLowerInvariant();
                bool paint = mn.StartsWith("body_") || mn.Contains("hood") || mn.Contains("fender") || mn.Contains("door") || mn.Contains("bootlid");
                bool glass = mn.Contains("glass") || mn.Contains("window");
                parts.Add(new Piece
                {
                    Name = f.name + " " + mesh.name, Paint = paint && !glass,
                    V = Transformed(mesh.vertices, root.worldToLocalMatrix * f.transform.localToWorldMatrix), T = mesh.triangles,
                    C = glass ? new Color(0.13f, 0.17f, 0.21f) : new Color(0.32f, 0.32f, 0.34f)
                });
                n++;
            }
            Log.Info("voiture : " + n + " pieces absentes posees sur leur point de montage");
        }

        static Vector3[] Transformed(Vector3[] v, Matrix4x4 m)
        {
            var r = new Vector3[v.Length];
            for (int i = 0; i < v.Length; i++) r[i] = m.MultiplyPoint3x4(v[i]);
            return r;
        }

        static void ApplyChosen()
        {
            if (applied || (Session.Active && !Session.IsHost)) return;   // invite : la couleur vient de l'hote
            GameObject body = Game.FindAny("CORRIS/BODY/car body(xxxxx)");
            PlayMakerFSM paint = body != null ? Game.FsmOn(body, "Paint") : null;
            if (paint == null) { Log.Warn("voiture : carrosserie introuvable"); return; }
            TryOverride(paint);
        }

        // Repeint la carrosserie de la couleur choisie dans le lanceur (une fois par partie).
        public static bool TryOverride(PlayMakerFSM paint)
        {
            if (applied || (Session.Active && !Session.IsHost)) return false;
            if (paint.transform.root.name != "CORRIS" || !paint.name.StartsWith("car body")) return false;
            Color c;
            if (!Parse(Config.Get("Coop", "CouleurVoiture", ""), out c)) return false;
            applied = true;
            FsmColor col = paint.FsmVariables.FindFsmColor("Color");
            if (col == null) return false;
            col.Value = c;
            FsmInt type = paint.FsmVariables.FindFsmInt("PaintType");
            if (type != null && type.Value > 2) type.Value = 1;   // couleur speciale tiree : brillant
            Log.Info("voiture : couleur choisie " + c + " appliquee a la nouvelle CORRIS");
            paint.SendEvent("REPAINT");
            return true;
        }

        public static bool Parse(string hex, out Color c)
        {
            c = Color.white;
            if (hex == null || hex.Length != 6) return false;
            int v;
            if (!int.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out v)) return false;
            c = new Color(((v >> 16) & 255) / 255f, ((v >> 8) & 255) / 255f, (v & 255) / 255f, 1f);
            return true;
        }
    }
}
