using System.Collections.Generic;
using MWCoop.Net;
using UnityEngine;

namespace MWCoop
{
    // Marqueurs partages (validation de JD, 16/10) : chaque joueur pose UN marqueur, de sa couleur, que tous voient sur la
    // carte (M : clic droit) et dans le monde (colonne de lumiere additive, pastille sur l'interface avec la distance).
    //  - Couleurs : palette de 16 par numero de joueur (0 = hote), les 8 premieres celles du salon du lanceur
    //    (launcher.cpp, pal[]) ; ColorOf() sert aussi aux pastilles des joueurs sur la carte.
    //  - Poser : dans le monde, Keys.Marker (clic molette par defaut, [Touches] Marqueur) la ou le joueur regarde (rayon
    //    depuis la camera, 300 m, sol) ; sur la carte, clic droit. Reposer deplace ; poser sur le sien (3 m) ou tenir la
    //    touche 0,6 s le retire. Rien n'est garde entre deux parties.
    //  - Reseau : Msg.Job "@marqueur" fiable (Jobs.OnSpecial relaie chez l'hote) : U8 proprietaire, U8 drapeaux (0 retire,
    //    1 pose), Vec position. Un invite ne parle que pour lui ; l'hote (snapshot) pour tous. ScheduleSnapshot(Peer) :
    //    l'hote renvoie tous les marqueurs a un arrivant (+20 s, comme les autres modules).
    //  - [Coop] Marqueurs=0 (lanceur) : ne pas montrer ceux des autres (le sien reste).
    //  - Rendu : un objet par marqueur (deux quads croises, texture degradee, Particles/Additive teinte), s'elargit et
    //    s'efface avec la distance ; toujours au moins la pastille de l'interface (Hud.Draw -> DrawHud).
    public static class Markers
    {
        public class Marker { public int Owner; public Vector3 At; public float Since; public GameObject Go; public Material M; }
        static readonly Dictionary<int, Marker> all = new Dictionary<int, Marker>();
        static readonly List<int> gone = new List<int>();
        static readonly List<KeyValuePair<float, Peer>> snapshots = new List<KeyValuePair<float, Peer>>();
        const float BeamHeight = 40f, Reach = 300f, HoldToRemove = 0.6f, SameSpot = 3f;
        static float downAt = -1f; static bool held;

        public static IEnumerable<Marker> All { get { return all.Values; } }
        public static int Count { get { return all.Count; } }
        public static Marker Mine { get { Marker m; return all.TryGetValue(Session.LocalId, out m) ? m : null; } }
        public static Marker Of(int owner) { Marker m; return all.TryGetValue(owner, out m) ? m : null; }

        // Voir ceux des autres ([Coop] Marqueurs, 1 par defaut) ; le sien toujours.
        public static bool ShowOthers { get { return Config.GetInt("Coop", "Marqueurs", 1) != 0; } }
        public static bool Shown(Marker m) { return m.Owner == Session.LocalId || ShowOthers; }

        // ---------------------------------------------------------------- couleurs
        // 16 couleurs par numero de joueur : les 8 du salon du lanceur (launcher.cpp pal[], memes valeurs 0xRRGGBB), puis 8
        // teintes bien distinctes (vert, cyan, ambre, magenta, brun, indigo, citron, saumon). Au-dela de 16 : on repart.
        static readonly int[] Rgb = { 0x3E86D0, 0xE0702A, 0x2A9C9A, 0x8E5BD6, 0xD64545, 0xC9971A, 0xD45D9A, 0x6C7A89,
                                      0x55B04C, 0x3FBFD9, 0xE8A33C, 0xB24FA5, 0x8C6239, 0x5C6BC0, 0xA9C43A, 0xE88A7A };
        static readonly Color[] pal = BuildPalette();
        static Color[] BuildPalette()
        {
            var p = new Color[Rgb.Length];
            for (int i = 0; i < Rgb.Length; i++) p[i] = new Color(((Rgb[i] >> 16) & 255) / 255f, ((Rgb[i] >> 8) & 255) / 255f, (Rgb[i] & 255) / 255f, 1f);
            return p;
        }
        public static Color ColorOf(int playerId) { return pal[((playerId % pal.Length) + pal.Length) % pal.Length]; }
        public static string Hex(Color c) { return ((int)(c.r * 255f)).ToString("X2") + ((int)(c.g * 255f)).ToString("X2") + ((int)(c.b * 255f)).ToString("X2"); }

        public static string NameOf(int owner)
        {
            if (owner == Session.LocalId) return Lang.T("Moi", "Me");
            PlayerInfo pi;
            return Session.Players.TryGetValue(owner, out pi) ? pi.Name : "#" + owner;
        }

        // ---------------------------------------------------------------- boucle
        public static void Update()
        {
            if (!PlayerSync.InGame)
            {
                if (all.Count > 0) Clear();
                snapshots.Clear(); downAt = -1f; held = false;
                return;
            }
            float now = Time.realtimeSinceStartup;
            // (un joueur parti : son marqueur avec lui)
            if (Session.Active)
            {
                gone.Clear();
                foreach (Marker m in all.Values) if (!Session.Players.ContainsKey(m.Owner)) gone.Add(m.Owner);
                foreach (int id in gone) { Drop(id); Log.Info("marqueurs : #" + id + " parti, marqueur retire"); }
            }
            // Arrivants (hote) : tous les marqueurs connus.
            for (int i = snapshots.Count - 1; i >= 0; i--)
            {
                if (now < snapshots[i].Key) continue;
                Peer p = snapshots[i].Value; snapshots.RemoveAt(i);
                if (!p.Accepted || Session.T == null) continue;
                foreach (Marker m in all.Values) Session.T.SendReliable(p, Write(m.Owner, m.At, 1).ToArray());
                Log.Info("marqueurs : " + all.Count + " envoye(s) a " + p);
            }
            KeyTick(now);
            Visuals();
        }

        // Touche : appui court = poser la ou l'on regarde (ou retirer si c'est deja la) ; tenue 0,6 s = retirer.
        static void KeyTick(float now)
        {
            if (Menu.Open || Menu.ChatOpen || MapPanel.Open || DroneCam.Active || Kauppa.Viewfinder) { downAt = -1f; held = false; return; }
            KeyCode k = Keys.Marker;
            if (Input.GetKeyDown(k)) { downAt = now; held = false; }
            if (downAt >= 0f && !held && Input.GetKey(k) && now - downAt >= HoldToRemove) { held = true; Remove(); }
            if (Input.GetKeyUp(k))
            {
                if (downAt >= 0f && !held) PlaceFromLook();
                downAt = -1f; held = false;
            }
        }

        // ---------------------------------------------------------------- poser / retirer (le sien)
        // La ou le joueur regarde : premier solide (pas un declencheur, pas le joueur) a moins de 300 m devant la camera.
        public static bool PlaceFromLook()
        {
            Camera cam = Game.MainCam;
            Transform ct = cam != null ? cam.transform : PlayerSync.LocalCamera;
            if (ct == null) return false;
            Vector3 at;
            if (!Hit(ct.position, ct.forward, Reach, out at)) { Log.Info("marqueurs : rien a moins de " + Reach + " m dans le regard"); return false; }
            Place(at);
            return true;
        }

        // Depuis la carte (x, z) : au sol de l'endroit (le plus haut solide sous 300 m), sinon a la hauteur du joueur.
        public static void PlaceOnMap(float x, float z)
        {
            Vector3 at;
            if (!Hit(new Vector3(x, 300f, z), Vector3.down, 400f, out at, true)) at = new Vector3(x, Game.PlayerT != null ? Game.PlayerT.position.y : 0f, z);
            Place(at);
        }

        // Poser le sien ici (le deplace s'il existe) ; a 3 m de l'existant : le retire. Meme chemin pour l'autotest.
        public static void Place(Vector3 at)
        {
            Marker mine = Mine;
            if (mine != null && (mine.At - at).sqrMagnitude < SameSpot * SameSpot) { Remove(); return; }
            Set(Session.LocalId, at);
            Send(Session.LocalId, at, 1);
            Log.Info("marqueurs : pose en " + at.ToString("F1") + (mine != null ? " (deplace)" : ""));
        }

        public static void Remove()
        {
            if (Mine == null) return;
            Drop(Session.LocalId);
            Send(Session.LocalId, Vector3.zero, 0);
            Log.Info("marqueurs : retire");
        }

        // Rayon : premier solide (ou le plus haut, 'highest' : depuis le ciel) sans les declencheurs ni le joueur.
        static bool Hit(Vector3 from, Vector3 dir, float max, out Vector3 at, bool highest = false)
        {
            at = Vector3.zero;
            float best = highest ? float.NegativeInfinity : float.PositiveInfinity;
            bool found = false;
            foreach (RaycastHit h in Physics.RaycastAll(from, dir, max))
            {
                if (h.collider == null || h.collider.isTrigger || Game.RootName(h.collider.transform) == "PLAYER") continue;
                if (highest ? h.point.y > best : h.distance < best) { best = highest ? h.point.y : h.distance; at = h.point; found = true; }
            }
            return found;
        }

        // ---------------------------------------------------------------- reseau
        static NetWriter Write(int owner, Vector3 at, int flags)
        {
            return new NetWriter(Msg.Job).U8(Session.LocalId).Str("@marqueur").U8(owner).U8(flags).Vec(at);
        }

        static void Send(int owner, Vector3 at, int flags)
        {
            if (!Session.Active) return;
            Session.SendAll(Write(owner, at, flags), true);
        }

        // Jobs.OnSpecial "@marqueur" (l'hote a deja relaye) : un invite ne parle que pour lui ; l'hote (who 0) pour tous.
        public static void OnMessage(int who, NetReader r)
        {
            int owner = r.U8(), flags = r.U8();
            Vector3 at = r.Vec();
            if (owner != who && (Session.IsHost || who != 0)) { Log.Warn("marqueurs : #" + who + " parle pour #" + owner + ", ignore"); return; }
            if (owner == Session.LocalId) return;   // (echo : jamais en principe)
            if (flags == 0) { if (Drop(owner)) Log.Info("marqueurs : #" + owner + " retire le sien"); }
            else { bool had = all.ContainsKey(owner); Set(owner, at); Log.Info("marqueurs : #" + owner + (had ? " deplace" : " pose") + " en " + at.ToString("F1")); }
        }

        public static void ScheduleSnapshot(Peer p) { if (Session.IsHost) snapshots.Add(new KeyValuePair<float, Peer>(Time.realtimeSinceStartup + 20f, p)); }

        // ---------------------------------------------------------------- etat
        static void Set(int owner, Vector3 at)
        {
            Marker m;
            if (!all.TryGetValue(owner, out m)) { m = new Marker { Owner = owner }; all[owner] = m; }
            m.At = at; m.Since = Time.realtimeSinceStartup;
            if (m.Go == null) Build(m);
            if (m.Go != null) m.Go.transform.position = at;
        }

        static bool Drop(int owner)
        {
            Marker m;
            if (!all.TryGetValue(owner, out m)) return false;
            all.Remove(owner);
            if (m.Go != null) Object.Destroy(m.Go);
            if (m.M != null) Object.Destroy(m.M);
            return true;
        }

        static void Clear()
        {
            foreach (Marker m in all.Values) { if (m.Go != null) Object.Destroy(m.Go); if (m.M != null) Object.Destroy(m.M); }
            all.Clear();
        }

        // ---------------------------------------------------------------- monde : colonne de lumiere
        static Shader shader; static Texture2D tex; static Mesh mesh; static bool noShader;

        static Shader BeamShader()
        {
            if (shader != null || noShader) return shader;
            foreach (string s in new[] { "Particles/Additive", "Legacy Shaders/Particles/Additive", "Mobile/Particles/Additive", "Particles/Additive (Soft)", "Sprites/Default" })
            {
                shader = Shader.Find(s);
                if (shader != null) break;
            }
            if (shader == null) { noShader = true; Log.Warn("marqueurs : pas de shader additif, colonnes non dessinees (pastilles seules)"); }
            return shader;
        }

        // Degrade : coeur brillant au milieu (u), qui s'eteint vers le haut (v) ; pied un peu plus fort.
        static Texture2D BeamTex()
        {
            if (tex != null) return tex;
            const int W = 32, H = 64;
            tex = new Texture2D(W, H, TextureFormat.ARGB32, false) { wrapMode = TextureWrapMode.Clamp, name = "MWCoop-Marqueur" };
            var px = new Color[W * H];
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    float dx = Mathf.Abs((x + 0.5f) / W * 2f - 1f), v = (y + 0.5f) / H;
                    float a = Mathf.Pow(Mathf.Clamp01(1f - dx), 2.2f) * 0.55f + Mathf.Pow(Mathf.Clamp01(1f - dx * 3f), 2f) * 0.7f;
                    a *= Mathf.Pow(1f - v, 1.3f) * (1f + Mathf.Clamp01(1f - v * 12f) * 0.6f);
                    a = Mathf.Clamp01(a);
                    px[y * W + x] = new Color(a, a, a, a);
                }
            tex.SetPixels(px);
            tex.Apply();
            return tex;
        }

        // Deux quads croises, pied en y = 0, haut en y = 1 (hauteur par l'echelle).
        static Mesh BeamMesh()
        {
            if (mesh != null) return mesh;
            mesh = new Mesh { name = "MWCoop-Marqueur" };
            mesh.vertices = new[] { new Vector3(-0.5f, 0, 0), new Vector3(0.5f, 0, 0), new Vector3(0.5f, 1, 0), new Vector3(-0.5f, 1, 0),
                                    new Vector3(0, 0, -0.5f), new Vector3(0, 0, 0.5f), new Vector3(0, 1, 0.5f), new Vector3(0, 1, -0.5f) };
            mesh.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1), new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
            mesh.triangles = new[] { 0, 2, 1, 0, 3, 2, 4, 6, 5, 4, 7, 6 };
            mesh.RecalculateBounds();
            return mesh;
        }

        static void Build(Marker m)
        {
            if (BeamShader() == null) return;
            var go = new GameObject("MWCoop-Marqueur-" + m.Owner);
            go.AddComponent<MeshFilter>().sharedMesh = BeamMesh();
            var mr = go.AddComponent<MeshRenderer>();
            m.M = new Material(shader) { mainTexture = BeamTex(), name = "MWCoop-Marqueur-" + m.Owner };
            Tint(m.M, ColorOf(m.Owner), 1f);
            mr.sharedMaterial = m.M;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            go.transform.localScale = new Vector3(0.8f, BeamHeight, 0.8f);
            m.Go = go;
        }

        static void Tint(Material mat, Color c, float a)
        {
            c.a = a;
            if (mat.HasProperty("_TintColor")) mat.SetColor("_TintColor", c); else mat.color = c;
        }

        // Chaque image (16 objets au plus) : s'elargit avec la distance pour rester visible, s'efface au loin ; tout pres,
        // plus discret (pas d'eblouissement). Caches si [Coop] Marqueurs=0 (sauf le sien).
        static void Visuals()
        {
            Camera cam = Game.MainCam;
            if (cam == null) return;
            Vector3 cp = cam.transform.position;
            foreach (Marker m in all.Values)
            {
                if (m.Go == null) continue;
                bool show = Shown(m);
                if (m.Go.activeSelf != show) m.Go.SetActive(show);
                if (!show) continue;
                float d = Vector3.Distance(cp, m.At);
                float w = 0.8f + d * 0.006f;                                            // (2,6 m a 300 m : quelques pixels)
                float a = Mathf.Clamp01(1.15f - d / 600f) * Mathf.Clamp(d / 12f, 0.35f, 1f);   // (loin : s'efface ; pres : discret)
                a *= 0.9f + 0.1f * Mathf.Sin((Time.realtimeSinceStartup - m.Since) * 2.5f);   // (leger battement)
                m.Go.transform.localScale = new Vector3(w, BeamHeight, w);
                Tint(m.M, ColorOf(m.Owner), a);
            }
        }

        // ---------------------------------------------------------------- interface (Hud.Draw) : pastille + nom · distance
        static readonly Color dark = new Color(0.06f, 0.08f, 0.12f, 0.88f);
        public static void DrawHud()
        {
            if (all.Count == 0 || !PlayerSync.InGame) return;
            Camera cam = Game.MainCam;
            if (cam == null) return;
            Vector3 cp = cam.transform.position;
            foreach (Marker m in all.Values)
            {
                if (!Shown(m)) continue;
                Vector3 sp = cam.WorldToScreenPoint(m.At + Vector3.up * 1.6f);
                if (sp.z <= 0.5f) continue;   // (derriere : rien)
                float d = Vector3.Distance(cp, m.At);
                Color c = ColorOf(m.Owner);
                string label = NameOf(m.Owner) + " · " + Mathf.RoundToInt(d) + " m";
                float x = Mathf.Round(sp.x), y = Mathf.Round(Screen.height - sp.y);
                float r = Style.Px(6);
                Style.Alpha = 0.92f;
                Style.Round(new Rect(x - r - Style.Px(2), y - r - Style.Px(2), 2 * r + Style.Px(4), 2 * r + Style.Px(4)), r + Style.Px(2), new Color(0f, 0f, 0f, 0.7f));
                Style.Round(new Rect(x - r, y - r, 2 * r, 2 * r), r, c);
                Style.Round(new Rect(x - r * 0.4f, y - r * 0.4f, r * 0.8f, r * 0.8f), r * 0.4f, Style.White);
                float tw = Style.Width(label, 13, true) + Style.Px(16), th = Style.Px(22);
                var pill = new Rect(Mathf.Round(x - tw / 2), Mathf.Round(y - r - Style.Px(6) - th), tw, th);
                Style.Round(pill, th / 2, dark);
                Style.Round(new Rect(pill.x, pill.yMax - Style.Px(3), pill.width, Style.Px(3)), Style.Px(1.5f), c);
                Style.Text(pill, label, 13, TextAnchor.MiddleCenter, Style.White, true);
            }
            Style.Alpha = 1f;
        }

        // ---------------------------------------------------------------- essais
        // [Test] Autotest=marqueur : hote a 25 s pose 20 m devant lui (meme chemin que la touche), invite a 35 s dit ce qu'il
        // connait (proprietaire, couleur, position, objet du monde), hote a 45 s retire, invite a 55 s redit.
        static int testStep;
        public static void Test(string mode, float t)
        {
            if (mode != "marqueur") return;
            if (Session.IsHost)
            {
                if (testStep == 0 && t > 25f)
                {
                    testStep = 1;
                    Transform ct = PlayerSync.LocalCamera ?? Game.PlayerT;
                    if (ct == null) { Log.Info("autotest marqueur : pas de camera"); return; }
                    Vector3 fwd = ct.forward; fwd.y = 0f; fwd = fwd.sqrMagnitude > 0.01f ? fwd.normalized : Vector3.forward;
                    Vector3 p = ct.position + fwd * 20f, at;
                    if (!Hit(p + Vector3.up * 20f, Vector3.down, 60f, out at, true)) at = new Vector3(p.x, Game.PlayerT != null ? Game.PlayerT.position.y : p.y, p.z);
                    Place(at);
                    Log.Info("autotest marqueur : pose a " + at.ToString("F1") + ", " + Describe());
                }
                if (testStep == 1 && t > 45f) { testStep = 2; Remove(); Log.Info("autotest marqueur : retire, " + Describe()); }
            }
            else
            {
                if (testStep == 0 && t > 35f) { testStep = 1; Log.Info("autotest marqueur : " + Describe()); }
                if (testStep == 1 && t > 55f) { testStep = 2; Log.Info("autotest marqueur : apres retrait, " + Describe()); }
            }
        }

        public static string Describe()
        {
            if (all.Count == 0) return "aucun marqueur";
            var sb = new System.Text.StringBuilder(all.Count + " marqueur(s) :");
            foreach (Marker m in all.Values)
                sb.Append(" #").Append(m.Owner).Append(" (").Append(NameOf(m.Owner)).Append(", couleur ").Append(Hex(ColorOf(m.Owner))).Append(") en ").Append(m.At.ToString("F1"))
                  .Append(m.Go != null ? (m.Go.activeSelf ? ", objet present" : ", objet cache") : ", SANS objet");
            return sb.ToString();
        }
    }
}
