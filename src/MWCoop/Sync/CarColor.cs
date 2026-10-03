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
            foreach (MeshFilter mf in car.GetComponentsInChildren<MeshFilter>())
            {
                Renderer r = mf.GetComponent<Renderer>();
                if (r != null && r.enabled && mf.sharedMesh != null)
                    parts.Add(new Piece { R = r, M = mf.sharedMesh, ToCar = root.worldToLocalMatrix * r.transform.localToWorldMatrix, Paint = IsPaintable(r.transform, root) });
            }
            foreach (SkinnedMeshRenderer s in car.GetComponentsInChildren<SkinnedMeshRenderer>())
                if (s.enabled && s.sharedMesh != null)
                    parts.Add(new Piece { R = s, M = s.sharedMesh, ToCar = root.worldToLocalMatrix * s.transform.localToWorldMatrix, Paint = IsPaintable(s.transform, root) });
            var points = AddLooseParts(root, parts);
            var extra = new List<Piece>();
            AddPointMeshes(root, points, extra);
            AddWheels(car, root, extra);

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
