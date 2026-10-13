using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace MWCoop
{
    // Boutique OSUUSKAUPPA s21 (demande de JD, 12/10) : le batiment, construit en code dans son propre repere (x = largeur
    // 11 m, z = profondeur 7 m, facade et porte en +z, y = 0 au plancher), a l'ouest du parking de la PSK. Habillage :
    // materiaux du jeu trouves par leur nom (aucun fichier du jeu copie) + nos textures (assets\kauppa, dans la DLL).
    public static partial class Kauppa
    {
        // place : a l'ouest du parking de la PSK, porte vers le parking (repere local +z = monde +x)
        static Vector3 Place = new Vector3(-1786.6f, 0f, 963.5f);
        static float Yaw = 90f;
        static float FloorY = 3.74f;   // hauteur du plancher, la meme chez tous (sol mesure 2,95..3,34 + 0,4)
        static bool floorAuto;
        // [Boutique] Paikka=x,z,cap[,plancher|auto] : autre emplacement (essais du coin s21 a Perajarvi)
        static void ReadPlace()
        {
            string s = Config.Get("Boutique", "Paikka", "");
            if (s.Length == 0) return;
            string[] a = s.Split(',');
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            try
            {
                Place = new Vector3(float.Parse(a[0], ci), 0f, float.Parse(a[1], ci));
                Yaw = float.Parse(a[2], ci);
                floorAuto = a.Length < 4 || a[3].Trim() == "auto";
                if (!floorAuto) FloorY = float.Parse(a[3], ci);
                L("emplacement d'essai " + Place.x + ", " + Place.z + ", cap " + Yaw + ", plancher " + (floorAuto ? "mesure" : FloorY.ToString("F2")));
            }
            catch { L("[Boutique] Paikka illisible : " + s); }
        }
        const float W2 = 5.5f, D2 = 3.5f, Wall = 0.2f, WallH = 3.4f, Ceil = 2.9f, Pitch = 25f;

        static GameObject root;
        static Transform door; static float doorAngle;
        static readonly Dictionary<string, Material> mats = new Dictionary<string, Material>();
        static readonly Dictionary<string, float> tiles = new Dictionary<string, float>();

        class Part { public readonly List<Vector3> V = new List<Vector3>(), N = new List<Vector3>(); public readonly List<Vector2> U = new List<Vector2>(); public readonly List<int> T = new List<int>(); }
        static readonly Dictionary<string, Part> parts = new Dictionary<string, Part>();
        static readonly List<Bounds> cols = new List<Bounds>();

        // ---------------------------------------------------------------- materiaux
        // (index des matieres et des objets du jeu par nom, fait une fois par construction : les parcourir a chaque
        // meuble coutait plus d'une seconde)
        static Dictionary<string, Material> gameMats;
        static Dictionary<string, List<Transform>> byName;

        static Material GameMat(string name)
        {
            if (gameMats == null)
            {
                gameMats = new Dictionary<string, Material>();
                foreach (Material m in Resources.FindObjectsOfTypeAll<Material>()) if (m != null && !gameMats.ContainsKey(m.name)) gameMats[m.name] = m;
            }
            Material r;
            return gameMats.TryGetValue(name, out r) ? r : null;
        }

        static void Mat(string key, string game, float tile, Color? tint = null)
        {
            Material src = GameMat(game);
            Material m = src != null ? new Material(src) : new Material(Shader.Find("Legacy Shaders/Diffuse") ?? Shader.Find("Diffuse"));
            if (src == null) L("materiau du jeu introuvable : " + game);
            if (tint.HasValue && m.HasProperty("_Color")) m.color = tint.Value;
            m.name = "osuus_" + key;
            mats[key] = m; tiles[key] = tile;
        }

        static void OwnMat(string key, string png, bool glow = false)
        {
            Texture2D t = Res(png, TextureFormat.RGB24);
            t.wrapMode = TextureWrapMode.Clamp; t.anisoLevel = 4;
            Material basis = GameMat("wood_panel7");
            var m = new Material(basis != null ? basis.shader : Shader.Find("Legacy Shaders/Diffuse")) { mainTexture = t, name = "osuus_" + key };
            if (m.HasProperty("_Color")) m.color = glow ? new Color(1.25f, 1.25f, 1.25f) : Color.white;
            mats[key] = m; tiles[key] = 1f;
        }

        static void Materials()
        {
            Mat("lauta", "wood_panel7", 2.2f);                 // bardage rouge a planches verticales
            Mat("lista", "wood_panel20", 1.2f);                // bordures blanches
            Mat("kivi", "rock_sokkeli", 2.0f);                 // soubassement en pierre
            Mat("katto", "roof_metal 1", 2.0f, new Color(0.62f, 0.42f, 0.38f));   // tole ondulee, brun-rouge
            Mat("lumi", "SNOW", 3f);
            Mat("parketti", "tiles5", 1.4f);
            Mat("tapetti", "wallpaper1", 1.1f);
            Mat("paneeli", "wood_panel4", 1.4f);
            Mat("sisakatto", "wood_panel2", 1.6f);
            Mat("tiski", "wood12", 1.0f);
            Mat("taso", "livingroom_table", 1.0f);
            Mat("terassi", "wood_panel19", 1.5f);
            // vitre : verre clair un peu teinte (le verre de la PSK est depoli)
            Shader gs = Shader.Find("Legacy Shaders/Transparent/Specular") ?? Shader.Find("Legacy Shaders/Transparent/Diffuse") ?? Shader.Find("Transparent/Diffuse");
            if (gs != null)
            {
                var g = new Material(gs) { name = "osuus_lasi", color = new Color(0.78f, 0.86f, 0.9f, 0.16f) };
                if (g.HasProperty("_SpecColor")) g.SetColor("_SpecColor", new Color(0.6f, 0.6f, 0.6f));
                if (g.HasProperty("_Shininess")) g.SetFloat("_Shininess", 0.9f);
                mats["lasi"] = g; tiles["lasi"] = 1f;
                L("verre : " + gs.name);
            }
            else Mat("lasi", "psk_glass", 1f);
            Mat("metalli", "metal_galvanized", 1f, new Color(0.9f, 0.9f, 0.88f));
            Mat("hylly", "store_shelfs", 1.2f);                // rayonnage blanc de la PSK
            Mat("hyllytaso", "metal_galvanized", 1f, new Color(0.93f, 0.93f, 0.92f));
            Mat("hyllyreuna", "metal_galvanized", 1f, new Color(0.55f, 0.6f, 0.65f));
            Mat("matto", "carpet1", 1f);
            Mat("ryijy", "ryijy1", 1f);
            Mat("juliste1", "home_poster1", 1f);
            Mat("juliste3", "home_poster3", 1f);
            OwnMat("kyltti", "enseigne.png");
            OwnMat("video", "affiche-video.png");
            OwnMat("horaires", "affiche-horaires.png");
            OwnMat("makuupussi", "affiche-makuupussi.png");
            OwnMat("hintalappu", "hintalappu-makuupussi.png");
            OwnMat("hlvhs", "hintalappu-vhs.png");
            OwnMat("hlnauhuri", "hintalappu-nauhuri.png");
            OwnMat("hltv", "hintalappu-tv.png");
            OwnMat("hlkamera", "hintalappu-kamera.png");
        }

        // ---------------------------------------------------------------- geometrie
        static Part P(string key) { Part p; if (!parts.TryGetValue(key, out p)) parts[key] = p = new Part(); return p; }

        // quadrilatere a (bas gauche), b (bas droite), c (haut droite), d (haut gauche) vu de face ; UV en metres / tuile
        static void Quad(string key, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector2[] uv = null)
        {
            if (key == null) return;
            Part p = P(key);
            Vector3 ud = (b - a).normalized, vd = (d - a).normalized, n = Vector3.Cross(vd, ud).normalized;
            float t = tiles.ContainsKey(key) ? tiles[key] : 1f;
            int i0 = p.V.Count;
            Vector3[] q = { a, b, c, d };
            for (int i = 0; i < 4; i++)
            {
                p.V.Add(q[i]); p.N.Add(n);
                p.U.Add(uv != null ? uv[i] : new Vector2(Vector3.Dot(q[i], ud) / t, Vector3.Dot(q[i], vd) / t));
            }
            p.T.Add(i0); p.T.Add(i0 + 3); p.T.Add(i0 + 2);
            p.T.Add(i0); p.T.Add(i0 + 2); p.T.Add(i0 + 1);
        }

        static void Tri(string key, Vector3 a, Vector3 b, Vector3 c)   // a, b, c dans le sens des aiguilles vu de face
        {
            Part p = P(key);
            Vector3 n = Vector3.Cross(b - a, c - a).normalized;
            Vector3 ud = Vector3.Cross(Vector3.up, -n).normalized; if (ud == Vector3.zero) ud = Vector3.right;
            float t = tiles.ContainsKey(key) ? tiles[key] : 1f;
            int i0 = p.V.Count;
            foreach (Vector3 v in new[] { a, b, c }) { p.V.Add(v); p.N.Add(n); p.U.Add(new Vector2(Vector3.Dot(v, ud) / t, v.y / t)); }
            p.T.Add(i0); p.T.Add(i0 + 1); p.T.Add(i0 + 2);
        }

        // boite alignee ; materiaux des faces : +x, -x, +y, -y, +z, -z (null = pas de face)
        static void Box(Vector3 mn, Vector3 mx, string px, string nx, string py, string ny, string pz, string nz, bool collide = true)
        {
            Quad(px, new Vector3(mx.x, mn.y, mn.z), new Vector3(mx.x, mn.y, mx.z), new Vector3(mx.x, mx.y, mx.z), new Vector3(mx.x, mx.y, mn.z));
            Quad(nx, new Vector3(mn.x, mn.y, mx.z), new Vector3(mn.x, mn.y, mn.z), new Vector3(mn.x, mx.y, mn.z), new Vector3(mn.x, mx.y, mx.z));
            Quad(py, new Vector3(mn.x, mx.y, mn.z), new Vector3(mx.x, mx.y, mn.z), new Vector3(mx.x, mx.y, mx.z), new Vector3(mn.x, mx.y, mx.z));
            Quad(ny, new Vector3(mn.x, mn.y, mx.z), new Vector3(mx.x, mn.y, mx.z), new Vector3(mx.x, mn.y, mn.z), new Vector3(mn.x, mn.y, mn.z));
            Quad(pz, new Vector3(mx.x, mn.y, mx.z), new Vector3(mn.x, mn.y, mx.z), new Vector3(mn.x, mx.y, mx.z), new Vector3(mx.x, mx.y, mx.z));
            Quad(nz, new Vector3(mn.x, mn.y, mn.z), new Vector3(mx.x, mn.y, mn.z), new Vector3(mx.x, mx.y, mn.z), new Vector3(mn.x, mx.y, mn.z));
            if (collide) cols.Add(new Bounds((mn + mx) / 2, mx - mn));
        }
        static void Box(Vector3 mn, Vector3 mx, string all, bool collide = true) { Box(mn, mx, all, all, all, all, all, all, collide); }

        // boite tournee (toit) : centre, rotation, taille ; faces dans le repere de la boite
        static void OBox(Vector3 c, Quaternion r, Vector3 s, string top, string bottom, string sides)
        {
            Vector3 h = s / 2;
            Func<float, float, float, Vector3> pt = (x, y, z) => c + r * new Vector3(x * h.x, y * h.y, z * h.z);
            Quad(sides, pt(1, -1, -1), pt(1, -1, 1), pt(1, 1, 1), pt(1, 1, -1));
            Quad(sides, pt(-1, -1, 1), pt(-1, -1, -1), pt(-1, 1, -1), pt(-1, 1, 1));
            Quad(top, pt(-1, 1, -1), pt(1, 1, -1), pt(1, 1, 1), pt(-1, 1, 1));
            Quad(bottom, pt(-1, -1, 1), pt(1, -1, 1), pt(1, -1, -1), pt(-1, -1, -1));
            Quad(sides, pt(1, -1, 1), pt(-1, -1, 1), pt(-1, 1, 1), pt(1, 1, 1));
            Quad(sides, pt(-1, -1, -1), pt(1, -1, -1), pt(1, 1, -1), pt(-1, 1, -1));
        }

        struct Hole { public float U0, U1, Y0, Y1; public Hole(float u0, float u1, float y0, float y1) { U0 = u0; U1 = u1; Y0 = y0; Y1 = y1; } }

        // mur droit decoupe autour des ouvertures. alongX : mur de facade/arriere (u = x), sinon pignon (u = z).
        // outer : coordonnee de la face exterieure, inward : +1/-1 vers l'interieur.
        static void WallWithHoles(bool alongX, float outer, float inward, float u0, float u1, Hole[] holes)
        {
            var cuts = new List<float> { u0, u1 };
            foreach (Hole h in holes) { cuts.Add(h.U0); cuts.Add(h.U1); }
            float[] cs = cuts.Distinct().OrderBy(v => v).ToArray();
            float inner = outer + inward * Wall;
            for (int i = 0; i + 1 < cs.Length; i++)
            {
                float a = cs[i], b = cs[i + 1], mid = (a + b) / 2;
                var spans = new List<KeyValuePair<float, float>> { new KeyValuePair<float, float>(-0.02f, WallH) };
                foreach (Hole h in holes)
                    if (mid > h.U0 && mid < h.U1)
                    {
                        var ns = new List<KeyValuePair<float, float>>();
                        foreach (var s in spans)
                        {
                            if (h.Y0 > s.Key) ns.Add(new KeyValuePair<float, float>(s.Key, Mathf.Min(s.Value, h.Y0)));
                            if (h.Y1 < s.Value) ns.Add(new KeyValuePair<float, float>(Mathf.Max(s.Key, h.Y1), s.Value));
                        }
                        spans = ns;
                    }
                foreach (var s in spans)
                {
                    if (s.Value - s.Key < 0.001f) continue;
                    float lo = Mathf.Min(outer, inner), hi = Mathf.Max(outer, inner);
                    bool outIsMax = outer > inner;
                    if (alongX)
                        Box(new Vector3(a, s.Key, lo), new Vector3(b, s.Value, hi), "lista", "lista", "lista", "lista", outIsMax ? "lauta" : "tapetti", outIsMax ? "tapetti" : "lauta");
                    else
                        Box(new Vector3(lo, s.Key, a), new Vector3(hi, s.Value, b), outIsMax ? "lauta" : "tapetti", outIsMax ? "tapetti" : "lauta", "lista", "lista", "lista", "lista");
                }
            }
            // lambris bas, plinthe et cimaise cote interieur (coupes aux portes)
            var lowCuts = new List<KeyValuePair<float, float>> { new KeyValuePair<float, float>(u0 + (alongX ? Wall : 0), u1 - (alongX ? Wall : 0)) };
            foreach (Hole h in holes)
                if (h.Y0 < 0.95f)
                {
                    var ns = new List<KeyValuePair<float, float>>();
                    foreach (var s in lowCuts)
                    {
                        if (h.U0 > s.Key) ns.Add(new KeyValuePair<float, float>(s.Key, Mathf.Min(s.Value, h.U0)));
                        if (h.U1 < s.Value) ns.Add(new KeyValuePair<float, float>(Mathf.Max(s.Key, h.U1), s.Value));
                    }
                    lowCuts = ns;
                }
            foreach (var s in lowCuts)
            {
                Lining(alongX, inner, inward, s.Key, s.Value, 0f, 0.9f, 0.015f, "paneeli");
                Lining(alongX, inner, inward, s.Key, s.Value, 0.9f, 0.95f, 0.035f, "tiski");
                Lining(alongX, inner, inward, s.Key, s.Value, 0f, 0.1f, 0.03f, "lista");
            }
        }

        static void Lining(bool alongX, float inner, float inward, float a, float b, float y0, float y1, float th, string key)
        {
            float p0 = inner, p1 = inner + inward * th, lo = Mathf.Min(p0, p1), hi = Mathf.Max(p0, p1);
            bool faceMax = inward > 0;
            if (alongX) Box(new Vector3(a, y0, lo), new Vector3(b, y1, hi), key, key, key, null, faceMax ? key : null, faceMax ? null : key, false);
            else Box(new Vector3(lo, y0, a), new Vector3(hi, y1, b), faceMax ? key : null, faceMax ? null : key, key, null, key, key, false);
        }

        // cadre, meneau et vitre d'une fenetre dans un mur de facade (alongX) ou de pignon
        static void Window(bool alongX, float outer, float inward, Hole h, int mullions)
        {
            float f = 0.06f, mid = outer + inward * (Wall * 0.45f), d0 = mid - 0.035f, d1 = mid + 0.035f;
            Action<float, float, float, float> frame = (ua, ub, ya, yb) =>
            {
                if (alongX) Box(new Vector3(ua, ya, d0), new Vector3(ub, yb, d1), "lista", false);
                else Box(new Vector3(d0, ya, ua), new Vector3(d1, yb, ub), "lista", false);
            };
            frame(h.U0, h.U1, h.Y0, h.Y0 + f); frame(h.U0, h.U1, h.Y1 - f, h.Y1);
            frame(h.U0, h.U0 + f, h.Y0, h.Y1); frame(h.U1 - f, h.U1, h.Y0, h.Y1);
            for (int i = 1; i <= mullions; i++) { float u = Mathf.Lerp(h.U0, h.U1, i / (float)(mullions + 1)); frame(u - f / 2, u + f / 2, h.Y0, h.Y1); }
            // appui exterieur
            float so = outer - inward * 0.06f;
            if (alongX) Box(new Vector3(h.U0 - 0.05f, h.Y0 - 0.05f, Mathf.Min(so, outer)), new Vector3(h.U1 + 0.05f, h.Y0, Mathf.Max(so, outer)), "lista", false);
            else Box(new Vector3(Mathf.Min(so, outer), h.Y0 - 0.05f, h.U0 - 0.05f), new Vector3(Mathf.Max(so, outer), h.Y0, h.U1 + 0.05f), "lista", false);
            // vitre (deux faces)
            if (alongX)
            {
                Quad("lasi", new Vector3(h.U0, h.Y0, mid), new Vector3(h.U1, h.Y0, mid), new Vector3(h.U1, h.Y1, mid), new Vector3(h.U0, h.Y1, mid));
                Quad("lasi", new Vector3(h.U1, h.Y0, mid), new Vector3(h.U0, h.Y0, mid), new Vector3(h.U0, h.Y1, mid), new Vector3(h.U1, h.Y1, mid));
            }
            else
            {
                Quad("lasi", new Vector3(mid, h.Y0, h.U1), new Vector3(mid, h.Y0, h.U0), new Vector3(mid, h.Y1, h.U0), new Vector3(mid, h.Y1, h.U1));
                Quad("lasi", new Vector3(mid, h.Y0, h.U0), new Vector3(mid, h.Y0, h.U1), new Vector3(mid, h.Y1, h.U1), new Vector3(mid, h.Y1, h.U0));
            }
        }

        // affiche (une face) collee contre la vitre, cote interieur, visible de l'exterieur (+z)
        static void PosterFront(string key, float x0, float x1, float y0, float y1, float z)
        {
            var uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
            Quad(key, new Vector3(x1, y0, z), new Vector3(x0, y0, z), new Vector3(x0, y1, z), new Vector3(x1, y1, z), uv);
            Quad("lista", new Vector3(x0, y0, z - 0.002f), new Vector3(x1, y0, z - 0.002f), new Vector3(x1, y1, z - 0.002f), new Vector3(x0, y1, z - 0.002f));
        }

        // ---------------------------------------------------------------- construction
        static void Build()
        {
            Materials();
            ReadPlace();
            // hauteur : le plancher 0,4 m au-dessus du point le plus haut du sol sous l'emprise
            Quaternion rot = Quaternion.Euler(0, Yaw, 0);
            float gMax = float.MinValue, gMin = float.MaxValue, gFront = 0; int nf = 0;
            for (float x = -W2; x <= W2; x += 1.1f)
                for (float z = -D2; z <= D2 + 2.5f; z += 0.7f)
                {
                    RaycastHit? h = GroundHit(Place + rot * new Vector3(x, 0, z) + Vector3.up * 200f);
                    if (!h.HasValue) continue;
                    float y = h.Value.point.y;
                    if (z <= D2) { gMax = Mathf.Max(gMax, y); gMin = Mathf.Min(gMin, y); }
                    else { gFront += y; nf++; }
                }
            if (gMax == float.MinValue) { gMax = gMin = 3.2f; }
            gFront = nf > 0 ? gFront / nf : gMax;
            // Plancher a hauteur FIXE (retour de JD, 12/10, captures : l'invite voyait l'hote enfonce a mi-jambes dans le plancher,
            // l'hote voyait l'invite flotter). Mesure chez l'un 2,95..3,34 (plancher 3,74), chez l'autre 2,95..4,06 (4,46) : un
            // objet ou une voiture sous l'emprise a son chargement. Chacun marchait sur son plancher, 72 cm d'ecart. La mesure ne
            // sert plus qu'au soubassement et aux marches.
            float floorY = floorAuto ? gMax + 0.4f : FloorY;
            L(string.Format("sol sous la boutique {0:F2}..{1:F2}, devant {2:F2} ; plancher a {3:F2}{4}", gMin, gMax, gFront, floorY, Mathf.Abs(gMax + 0.4f - floorY) > 0.05f ? " (mesure " + (gMax + 0.4f).ToString("F2") + " ignoree)" : ""));
            gMin = Mathf.Min(gMin, floorY - 0.8f);
            float down = floorY - gMin + 0.4f;          // profondeur du soubassement
            float frontDrop = floorY - gFront;           // marches a descendre devant

            root = new GameObject("OSUUSKAUPPA s21");
            root.transform.position = new Vector3(Place.x, floorY, Place.z);
            root.transform.rotation = rot;

            // soubassement en pierre et plancher
            Box(new Vector3(-W2 - 0.1f, -down, -D2 - 0.1f), new Vector3(W2 + 0.1f, -0.02f, D2 + 0.1f), "kivi", "kivi", "kivi", null, "kivi", "kivi");
            Box(new Vector3(-W2 + Wall, -0.1f, -D2 + Wall), new Vector3(W2 - Wall, 0f, D2 - Wall), null, null, "parketti", null, null, null);

            // murs : facade (+z) avec porte, deux vitrines et une petite fenetre ; pignons ; arriere
            var door0 = new Hole(-4.4f, -3.4f, 0f, 2.15f);
            var winA = new Hole(-2.7f, -0.1f, 0.9f, 2.3f);
            var winB = new Hole(0.7f, 3.3f, 0.9f, 2.3f);
            var winC = new Hole(4.0f, 4.9f, 1.2f, 2.2f);
            WallWithHoles(true, D2, -1, -W2, W2, new[] { door0, winA, winB, winC });
            var back = new Hole(-2.2f, -1.2f, 1.3f, 2.1f);
            WallWithHoles(true, -D2, 1, -W2, W2, new[] { back });
            var sideR = new Hole(-1.4f, -0.2f, 1.0f, 2.2f);
            WallWithHoles(false, W2, -1, -D2 + Wall, D2 - Wall, new[] { sideR });
            var sideL = new Hole(0.2f, 1.4f, 1.0f, 2.2f);
            WallWithHoles(false, -W2, 1, -D2 + Wall, D2 - Wall, new[] { sideL });
            Window(true, D2, -1, winA, 1); Window(true, D2, -1, winB, 1); Window(true, D2, -1, winC, 0);
            Window(true, -D2, 1, back, 0); Window(false, W2, -1, sideR, 0); Window(false, -W2, 1, sideL, 0);
            // encadrement de porte
            Box(new Vector3(door0.U0 - 0.08f, 0, D2), new Vector3(door0.U0, 2.23f, D2 + 0.03f), "lista", false);
            Box(new Vector3(door0.U1, 0, D2), new Vector3(door0.U1 + 0.08f, 2.23f, D2 + 0.03f), "lista", false);
            Box(new Vector3(door0.U0 - 0.08f, 2.15f, D2), new Vector3(door0.U1 + 0.08f, 2.23f, D2 + 0.03f), "lista", false);
            // planches d'angle
            foreach (float sx in new[] { -1f, 1f })
                foreach (float sz in new[] { -1f, 1f })
                {
                    Vector3 c = new Vector3(sx * W2, 0, sz * D2);
                    Box(new Vector3(c.x - (sx > 0 ? 0.12f : 0.03f), -0.02f, c.z - (sz > 0 ? 0.12f : 0.03f)),
                        new Vector3(c.x + (sx > 0 ? 0.03f : 0.12f), WallH, c.z + (sz > 0 ? 0.03f : 0.12f)), "lista", false);
                }
            // plafond (et, au-dessus, la zone "abritee" du jeu)
            Quad("sisakatto", new Vector3(-W2 + Wall, Ceil, D2 - Wall), new Vector3(W2 - Wall, Ceil, D2 - Wall), new Vector3(W2 - Wall, Ceil, -D2 + Wall), new Vector3(-W2 + Wall, Ceil, -D2 + Wall));

            // toit a deux pentes (faitage le long de x), debord 0,5 m, neige dessus
            float tan = Mathf.Tan(Pitch * Mathf.Deg2Rad), cos = Mathf.Cos(Pitch * Mathf.Deg2Rad);
            float ridge = WallH + D2 * tan, over = 0.5f, run = D2 + over, t = 0.12f;
            foreach (float s in new[] { 1f, -1f })
            {
                Quaternion r = Quaternion.Euler(s * Pitch, 0, 0);
                Vector3 nrm = r * Vector3.up;
                if (s < 0) nrm = new Vector3(0, nrm.y, -Mathf.Abs(nrm.z)); else nrm = new Vector3(0, nrm.y, Mathf.Abs(nrm.z));
                Vector3 under = new Vector3(0, WallH + (D2 - run / 2) * tan, s * run / 2);
                OBox(under + nrm * (t / 2), r, new Vector3(2 * W2 + 0.8f, t, run / cos), "katto", "terassi", "lista");
                OBox(under + nrm * (t + 0.05f), r, new Vector3(2 * W2 + 0.6f, 0.1f, run / cos - 0.15f), "lumi", null, "lumi");
                // planche de rive (bord du toit)
                Box(new Vector3(-W2 - 0.4f, WallH - over * tan - 0.2f, s > 0 ? D2 + over - 0.03f : -D2 - over), new Vector3(W2 + 0.4f, WallH - over * tan + 0.02f, s > 0 ? D2 + over : -D2 - over + 0.03f), "lista", false);
            }
            Box(new Vector3(-W2 - 0.4f, ridge + 0.05f, -0.15f), new Vector3(W2 + 0.4f, ridge + 0.2f, 0.15f), "lumi", false);
            // pignons (triangles au-dessus des murs lateraux)
            Tri("lauta", new Vector3(W2, WallH, -D2), new Vector3(W2, ridge, 0), new Vector3(W2, WallH, D2));
            Tri("lauta", new Vector3(-W2, WallH, D2), new Vector3(-W2, ridge, 0), new Vector3(-W2, WallH, -D2));

            // enseigne au-dessus des vitrines
            float sx0 = -2.6f, sx1 = 3.8f, sy0 = 2.42f, sy1 = 3.22f, sgz = D2 + 0.06f;
            Box(new Vector3(sx0, sy0, D2), new Vector3(sx1, sy1, sgz), "lista", "lista", "lista", "lista", null, null, false);
            Quad("kyltti", new Vector3(sx1, sy0, sgz), new Vector3(sx0, sy0, sgz), new Vector3(sx0, sy1, sgz), new Vector3(sx1, sy1, sgz),
                 new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) });

            // lampes "col de cygne" au-dessus de l'enseigne
            foreach (float x in new[] { -0.9f, 2.1f })
            {
                Box(new Vector3(x - 0.02f, 3.3f, D2 + 0.06f), new Vector3(x + 0.02f, 3.34f, D2 + 0.72f), "metalli", false);
                Box(new Vector3(x - 0.11f, 3.24f, D2 + 0.66f), new Vector3(x + 0.11f, 3.36f, D2 + 0.86f), "metalli", false);
            }
            // affiches dans les vitrines (vues de dehors)
            float pz = D2 - Wall * 0.45f - 0.04f;
            PosterFront("video", winA.U0 + 0.15f, winA.U0 + 0.15f + 0.62f, winA.Y0 + 0.08f, winA.Y0 + 0.08f + 0.93f, pz);
            PosterFront("makuupussi", winB.U1 - 0.77f, winB.U1 - 0.15f, winB.Y0 + 0.08f, winB.Y0 + 1.01f, pz);
            PosterFront("horaires", winC.U0 + 0.25f, winC.U0 + 0.55f, winC.Y0 + 0.1f, winC.Y0 + 0.55f, pz);

            // perron et marches devant la porte
            float deck0 = -5.1f, deck1 = -2.7f, dz = D2 + 1.3f;
            Box(new Vector3(deck0, -frontDrop - 1.0f, D2), new Vector3(deck1, -0.03f, dz), "terassi");
            int steps = Mathf.Max(0, Mathf.CeilToInt((frontDrop - 0.05f) / 0.19f));
            float rise = steps > 0 ? (frontDrop - 0.03f) / (steps + 0.0001f) : 0;
            for (int k = 1; k <= steps; k++)
                Box(new Vector3(deck0, -frontDrop - 1.0f, dz + (k - 1) * 0.32f), new Vector3(deck1, -0.03f - k * rise, dz + k * 0.32f), "terassi");

            // tapis, ryijy, affiches (materiaux du jeu sur des plans), puis meubles et etageres garnies
            FlatDeco();
            Furnish();
            Offers();
            Flush();
            Lights();
            // (drapeau corse en tissu physique retire, 0.83.1 : des joueurs avaient le jeu fige depuis la boutique ; JD : « on fera
            // autrement ». KauppaLippu.cs garde le code, plus appele.)
            Door(door0);
            Shelter();
            Vendor();
            L("construite en " + root.transform.position.ToString("F1"));
        }

        // plan texture entier (UV 0-1), face tournee vers n (Vector3.up pour le sol)
        static void Plane(string key, Vector3 c, float w, float h, Vector3 n)
        {
            var uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
            Vector3 up = n == Vector3.up ? Vector3.forward : Vector3.up;
            Vector3 right = n == Vector3.up ? Vector3.right : Vector3.Cross(Vector3.up, -n).normalized;   // droite vue de face
            Vector3 hr = right * (w / 2), hu = up * (h / 2);
            Quad(key, c - hr - hu, c + hr - hu, c + hr + hu, c - hr + hu, uv);
        }

        static void FlatDeco()
        {
            Plane("matto", new Vector3(-3.9f, 0.006f, 1.9f), 1.0f, 2.2f, Vector3.up);          // passage depuis la porte
            Plane("matto", new Vector3(-1.6f, 0.006f, 1.6f), 1.4f, 1.0f, Vector3.up);
            Plane("ryijy", new Vector3(W2 - Wall - 0.01f, 1.75f, 2.0f), 0.9f, 1.3f, Vector3.left); // au-dessus du coin cafe
            Plane("juliste1", new Vector3(-4.1f, 1.75f, -D2 + Wall + 0.01f), 0.5f, 0.7f, Vector3.forward);
            Plane("juliste3", new Vector3(-0.6f, 1.8f, -D2 + Wall + 0.01f), 0.5f, 0.7f, Vector3.forward);
        }

        static void Flush()
        {
            foreach (var kv in parts)
            {
                var g = new GameObject("osuus_" + kv.Key);
                g.transform.SetParent(root.transform, false);
                var m = new Mesh { name = g.name };
                m.vertices = kv.Value.V.ToArray(); m.normals = kv.Value.N.ToArray(); m.uv = kv.Value.U.ToArray(); m.triangles = kv.Value.T.ToArray();
                m.RecalculateBounds();
                g.AddComponent<MeshFilter>().sharedMesh = m;
                var r = g.AddComponent<MeshRenderer>();
                r.sharedMaterial = mats.ContainsKey(kv.Key) ? mats[kv.Key] : null;
                if (kv.Key == "lasi") r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            var cg = new GameObject("osuus_collisions");
            cg.transform.SetParent(root.transform, false);
            foreach (Bounds b in cols)
            {
                var c = new GameObject("c");
                c.transform.SetParent(cg.transform, false);
                c.transform.localPosition = b.center;
                c.AddComponent<BoxCollider>().size = b.size;
            }
            parts.Clear(); cols.Clear();
        }

        static void Lights()
        {
            var warm = new Color(1f, 0.83f, 0.62f);
            foreach (Vector3 p in new[] { new Vector3(-3f, 2.6f, 0.3f), new Vector3(0.3f, 2.6f, 0.6f), new Vector3(3f, 2.6f, -0.6f) })
            {
                var g = new GameObject("osuus_valo");
                g.transform.SetParent(root.transform, false); g.transform.localPosition = p;
                var l = g.AddComponent<Light>(); l.type = LightType.Point; l.color = warm; l.intensity = 1.5f; l.range = 7f; l.shadows = LightShadows.None;
            }
            // eclairage de l'enseigne (deux spots sous l'avant-toit)
            foreach (float x in new[] { -0.9f, 2.1f })
            {
                var sg = new GameObject("osuus_valo_kyltti");
                sg.transform.SetParent(root.transform, false);
                sg.transform.localPosition = new Vector3(x, 3.32f, D2 + 0.75f);
                sg.transform.localRotation = Quaternion.LookRotation(new Vector3(0, -0.55f, -1f));
                var sl = sg.AddComponent<Light>(); sl.type = LightType.Spot; sl.spotAngle = 100f; sl.color = new Color(1f, 0.9f, 0.75f); sl.intensity = 1.6f; sl.range = 3.5f; sl.shadows = LightShadows.None;
            }
            var e = new GameObject("osuus_valo_ulko");
            e.transform.SetParent(root.transform, false); e.transform.localPosition = new Vector3(-3.9f, 2.45f, D2 + 0.6f);
            var le = e.AddComponent<Light>(); le.type = LightType.Point; le.color = warm; le.intensity = 1.2f; le.range = 6f; le.shadows = LightShadows.None;
        }

        // porte vitree, gond a gauche, s'ouvre vers l'interieur quand un joueur approche
        static void Door(Hole h)
        {
            var pivot = new GameObject("osuus_ovi");
            pivot.transform.SetParent(root.transform, false);
            pivot.transform.localPosition = new Vector3(h.U0 + 0.02f, 0.01f, D2 - Wall * 0.5f);
            float w = h.U1 - h.U0 - 0.04f, top = h.Y1 - 0.03f, th = 0.045f;
            Box(new Vector3(0, 0, -th / 2), new Vector3(w, 1.05f, th / 2), "lista", false);
            Box(new Vector3(0, 1.05f, -th / 2), new Vector3(0.14f, top, th / 2), "lista", false);
            Box(new Vector3(w - 0.14f, 1.05f, -th / 2), new Vector3(w, top, th / 2), "lista", false);
            Box(new Vector3(0, top - 0.14f, -th / 2), new Vector3(w, top, th / 2), "lista", false);
            Quad("lasi", new Vector3(0.14f, 1.05f, 0), new Vector3(w - 0.14f, 1.05f, 0), new Vector3(w - 0.14f, top - 0.14f, 0), new Vector3(0.14f, top - 0.14f, 0));
            Quad("lasi", new Vector3(w - 0.14f, 1.05f, 0), new Vector3(0.14f, 1.05f, 0), new Vector3(0.14f, top - 0.14f, 0), new Vector3(w - 0.14f, top - 0.14f, 0));
            Box(new Vector3(w - 0.16f, 0.98f, th / 2), new Vector3(w - 0.08f, 1.02f, th / 2 + 0.06f), "metalli", false);
            Box(new Vector3(w - 0.16f, 0.98f, -th / 2 - 0.06f), new Vector3(w - 0.08f, 1.02f, -th / 2), "metalli", false);
            var keepRoot = root; root = pivot;
            Flush();
            root = keepRoot;
            door = pivot.transform;
            // collision de la porte (elle tourne avec) : fermee, elle bloque ; on clique dessus pour l'ouvrir
            var dc = new GameObject("osuus_ovi_coll");
            dc.transform.SetParent(pivot.transform, false);
            dc.transform.localPosition = new Vector3(w / 2f, top / 2f, 0f);
            var dbc = dc.AddComponent<BoxCollider>(); dbc.size = new Vector3(w, top, 0.06f);
            doorCol = dbc;
        }

        static void TickDoor()
        {
            if (door == null) return;
            Vector3 c = door.position + door.rotation * new Vector3(0.5f, 1f, 0);
            bool near = false;
            // (le joueur, les autres joueurs, la camera d'essai)
            Transform pl = Game.PlayerT;
            // (porte ouverte a la main, clic sur la porte : KauppaOsto ; les essais l'ouvrent)
            if (doorOpen || forceDoor) near = true;
            doorAngle = Mathf.MoveTowards(doorAngle, near ? 95f : 0f, Time.deltaTime * 160f);
            door.localRotation = Quaternion.Euler(0, doorAngle, 0);
        }
        static bool forceDoor;

        // zone abritee du jeu (calque 27 + automate "Data" Temp) : copie d'un NoRain du magasin de Teimo, a notre forme
        static void Shelter()
        {
            GameObject src = FindPath("STORE_AREA/Stuff/LOD/NoRain");
            if (src == null) { L("NoRain introuvable"); return; }
            var g = (GameObject)UnityEngine.Object.Instantiate(src);
            g.name = "NoRainOsuuskauppa";
            g.transform.SetParent(root.transform, false);
            g.transform.localPosition = Vector3.zero; g.transform.localRotation = Quaternion.identity; g.transform.localScale = Vector3.one;
            var mc = g.GetComponent<MeshCollider>();
            var m = new Mesh { name = "osuus_norain" };
            float y = Ceil + 0.05f;
            m.vertices = new[] { new Vector3(-W2, y, -D2), new Vector3(W2, y, -D2), new Vector3(W2, y, D2), new Vector3(-W2, y, D2) };
            m.triangles = new[] { 0, 2, 1, 0, 3, 2, 0, 1, 2, 0, 2, 3 };
            m.RecalculateBounds();
            if (mc != null) { mc.sharedMesh = null; mc.sharedMesh = m; }
            g.SetActive(true);
            L("zone abritee : calque " + g.layer);
        }

        static GameObject FindPath(string path, int index = 0)
        {
            if (byName == null)
            {
                byName = new Dictionary<string, List<Transform>>();
                foreach (Transform t in Resources.FindObjectsOfTypeAll<Transform>())
                {
                    if (t.hideFlags != HideFlags.None) continue;
                    List<Transform> l;
                    if (!byName.TryGetValue(t.name, out l)) byName[t.name] = l = new List<Transform>();
                    l.Add(t);
                }
            }
            int slash = path.LastIndexOf('/');
            List<Transform> cands;
            if (!byName.TryGetValue(slash >= 0 ? path.Substring(slash + 1) : path, out cands)) return null;
            int n = 0;
            foreach (Transform t in cands)
                if (t != null && ObjPath(t) == path && n++ == index) return t.gameObject;
            return null;
        }
    }
}
