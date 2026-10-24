using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace MWCoop
{
    // Musee de la Corse (demande de JD, 13/10), a cote de la boutique au coin s21 de Perajarvi. Construit en code comme la
    // boutique (memes outils : Quad, Box, OBox, Flush), dans son propre repere : x = largeur (ailes de -10 a +10 m), z =
    // profondeur (arriere -5, facade des ailes +5, avant du hall +7), y = 0 au plancher.
    // Silhouette : un hall central haut (pignon vitre sur la route, entree a portes coulissantes automatiques) entre deux
    // ailes basses a deux pentes ; bardage ocre, soubassement en granit, volets gris-bleu, menuiseries anthracite.
    // Decor local a chaque joueur ; les portes s'ouvrent devant n'importe quel joueur (le local ou un avatar).
    // Jamais de physique de tissu sur les drapeaux (Cloth : jeu fige chez des joueurs) : vagues calculees.
    public static partial class Kauppa
    {
        const float MuW = 10f, MuBack = -5f, MuWing = 5f, MuFront = 7f, MuHall = 3.2f;
        const float MuWingH = 4.2f, MuWingPitch = 22f, MuHallH = 6.4f, MuHallPitch = 30f, MuCeil = 3.6f;

        static GameObject museo;
        static Transform[] museoLeaves;
        static float museoOpen, museoNear;
        static bool forceMuseoDoor;
        const float MuDoorHalf = 1.25f, MuDoorSlide = 1.18f;

        class Flag { public Transform T; public Mesh Front, Back; public Vector3[] Rest, Pos, Nrm; public Vector3 Side, Fly; public int U, V; public float Phase; }
        static readonly List<Flag> flags = new List<Flag>();

        static void ResetMuseo() { museo = null; museoLeaves = null; museoOpen = 0f; museoNear = -10f; forceMuseoDoor = false; flags.Clear(); }

        // [Boutique] Museo=x,z,cap[,plancher|auto] (vide : pas de musee)
        static void BuildMuseo()
        {
            // (plancher fixe, le meme chez tous : sol nivele 2,23..3,16 + 0,35 ; My Summer Car : 2,44..3,52, plancher 3,62)
            string s = Config.Get("Boutique", "Museo", Game.Msc ? "-1574.6,1085.4,22,3.62" : "-1574.6,1085.4,22,3.51");
            if (s.Length == 0) return;
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            string[] a = s.Split(',');
            Vector3 place = new Vector3(float.Parse(a[0], ci), 0f, float.Parse(a[1], ci));
            float yaw = float.Parse(a[2], ci);
            Quaternion rot = Quaternion.Euler(0, yaw, 0);
            MuseoMaterials();

            // sol : sous l'emprise (ailes + hall) et devant les marches
            float gMax = float.MinValue, gMin = float.MaxValue, gFront = 0; int nf = 0;
            for (float x = -MuW; x <= MuW; x += 1f)
                for (float z = MuBack; z <= MuFront + 3.5f; z += 1f)
                {
                    bool hall = Mathf.Abs(x) <= MuHall;
                    bool under = z <= MuWing || (hall && z <= MuFront);
                    bool front = hall && z > MuFront + 2f;
                    if (!under && !front) continue;
                    RaycastHit? h = GroundHit(place + rot * new Vector3(x, 0, z) + Vector3.up * 200f);
                    if (!h.HasValue) continue;
                    float y = h.Value.point.y;
                    if (under) { gMax = Mathf.Max(gMax, y); gMin = Mathf.Min(gMin, y); }
                    else { gFront += y; nf++; }
                }
            if (gMax == float.MinValue) gMax = gMin = 3f;
            gFront = nf > 0 ? gFront / nf : gMin;
            float floorY = a.Length > 3 && a[3].Trim() != "auto" ? float.Parse(a[3], ci) : gMax + 0.35f;
            L(string.Format("musee : sol {0:F2}..{1:F2}, devant {2:F2} ; plancher a {3:F2}", gMin, gMax, gFront, floorY));
            float down = floorY - Mathf.Min(gMin, floorY - 0.6f) + 0.4f;
            float frontDrop = Mathf.Max(0.05f, floorY - gFront);

            museo = new GameObject("MWCoop Korsikan museo");
            museo.transform.position = new Vector3(place.x, floorY, place.z);
            museo.transform.rotation = rot;
            var keepRoot = root; root = museo;
            try
            {
                MuseoShell(down);
                MuseoFront(frontDrop);
                MuseoRoofs();
                MuseoInterior();
                MuseoFurniture();
                MuseoExhibits();
                Flush();
                MuseoDoors();
                MuseoLights();
                foreach (float fx in new[] { 5.4f, 8.0f }) MuseoFlagpole(new Vector3(fx, 0, MuFront + 1.6f), fx < 7f);
                MuseoShelter();
            }
            finally { root = keepRoot; }
            L("musee construit en " + museo.transform.position.ToString("F1"));
        }

        static void MuseoMaterials()
        {
            Mat("mu_seina", "wood_panel20", 1.6f, new Color(1f, 0.79f, 0.5f));     // bardage ocre (les facades d'Ajaccio)
            Mat("mu_valko", "wood_panel20", 1.6f, new Color(0.97f, 0.96f, 0.93f)); // murs interieurs blancs
            Mat("mu_kivi", "rock_sokkeli", 1.6f, new Color(0.95f, 0.9f, 0.86f));  // granit
            Mat("mu_katto", "roof_metal 1", 2.0f, new Color(0.7f, 0.36f, 0.26f));  // tole couleur tuile
            Mat("mu_alu", "metal_galvanized", 1f, new Color(0.2f, 0.21f, 0.23f));  // menuiseries anthracite
            Mat("mu_luukku", "wood_panel20", 0.8f, new Color(0.5f, 0.6f, 0.66f));  // volets gris-bleu
            Mat("mu_lattia", "tiles5", 1.4f);
            Mat("mu_puukatto", "wood_panel2", 1.6f);
            Mat("mu_lauta", "wood_panel19", 1.5f);
            if (!mats.ContainsKey("lasi")) Mat("lasi", "psk_glass", 1f);
            if (!mats.ContainsKey("lumi")) Mat("lumi", "SNOW", 3f);
            if (!mats.ContainsKey("lista")) Mat("lista", "wood_panel20", 1.2f);
            OwnMat("mu_kyltti", "museo-kyltti.png");
        }

        // mur droit decoupe autour des ouvertures, de y0 a y1 ; alongX : u = x (mur face a +-z), sinon u = z
        static void MWall(bool alongX, float outer, float inward, float u0, float u1, float y0, float y1, Hole[] holes, string outKey, string inKey)
        {
            var cuts = new List<float> { u0, u1 };
            foreach (Hole h in holes) { cuts.Add(h.U0); cuts.Add(h.U1); }
            float[] cs = cuts.Distinct().OrderBy(v => v).ToArray();
            float inner = outer + inward * Wall;
            for (int i = 0; i + 1 < cs.Length; i++)
            {
                float a = cs[i], b = cs[i + 1], mid = (a + b) / 2;
                var spans = new List<KeyValuePair<float, float>> { new KeyValuePair<float, float>(y0, y1) };
                foreach (Hole h in holes)
                    if (mid > h.U0 && mid < h.U1)
                    {
                        var ns = new List<KeyValuePair<float, float>>();
                        foreach (var sp in spans)
                        {
                            if (h.Y0 > sp.Key) ns.Add(new KeyValuePair<float, float>(sp.Key, Mathf.Min(sp.Value, h.Y0)));
                            if (h.Y1 < sp.Value) ns.Add(new KeyValuePair<float, float>(Mathf.Max(sp.Key, h.Y1), sp.Value));
                        }
                        spans = ns;
                    }
                foreach (var sp in spans)
                {
                    if (sp.Value - sp.Key < 0.001f) continue;
                    float lo = Mathf.Min(outer, inner), hi = Mathf.Max(outer, inner);
                    bool outIsMax = outer > inner;
                    if (alongX)
                        Box(new Vector3(a, sp.Key, lo), new Vector3(b, sp.Value, hi), "lista", "lista", "lista", "lista", outIsMax ? outKey : inKey, outIsMax ? inKey : outKey);
                    else
                        Box(new Vector3(lo, sp.Key, a), new Vector3(hi, sp.Value, b), outIsMax ? outKey : inKey, outIsMax ? inKey : outKey, "lista", "lista", "lista", "lista");
                }
            }
        }

        // poutre de a vers b (section w x h)
        static void Beam(Vector3 a, Vector3 b, float w, float h, string key)
        {
            Vector3 d = b - a;
            Quaternion r = Mathf.Abs(Vector3.Dot(d.normalized, Vector3.up)) > 0.99f ? Quaternion.LookRotation(d, Vector3.forward) : Quaternion.LookRotation(d, Vector3.up);
            OBox((a + b) / 2, r, new Vector3(w, h, d.magnitude), key, key, key);
        }

        // volets ouverts de part et d'autre d'une fenetre de facade (alongX) ou de pignon ; out = cote exterieur (+1/-1)
        static void Shutters(bool alongX, float outer, float outSign, Hole h)
        {
            float sw = (h.U1 - h.U0) / 2f, th = 0.04f, o0 = outer + outSign * 0.01f, o1 = outer + outSign * (0.01f + th);
            float lo = Mathf.Min(o0, o1), hi = Mathf.Max(o0, o1);
            foreach (var span in new[] { new[] { h.U0 - sw - 0.04f, h.U0 - 0.04f }, new[] { h.U1 + 0.04f, h.U1 + sw + 0.04f } })
            {
                float u0 = span[0], u1 = span[1];
                if (alongX) Box(new Vector3(u0, h.Y0, lo), new Vector3(u1, h.Y1, hi), "mu_luukku", false);
                else Box(new Vector3(lo, h.Y0, u0), new Vector3(hi, h.Y1, u1), "mu_luukku", false);
                // traverses (persiennes suggerees)
                for (float y = h.Y0 + 0.25f; y < h.Y1 - 0.15f; y += 0.36f)
                {
                    float p0 = outer + outSign * (0.01f + th), p1 = p0 + outSign * 0.02f, l2 = Mathf.Min(p0, p1), h2 = Mathf.Max(p0, p1);
                    if (alongX) Box(new Vector3(u0 + 0.03f, y, l2), new Vector3(u1 - 0.03f, y + 0.05f, h2), "mu_luukku", false);
                    else Box(new Vector3(l2, y, u0 + 0.03f), new Vector3(h2, y + 0.05f, u1 - 0.03f), "mu_luukku", false);
                }
            }
        }

        static void MuseoShell(float down)
        {
            // soubassement en granit (ailes + hall avance) et plancher
            Box(new Vector3(-MuW - 0.1f, -down, MuBack - 0.1f), new Vector3(MuW + 0.1f, -0.02f, MuWing + 0.1f), "mu_kivi", "mu_kivi", "mu_kivi", null, "mu_kivi", "mu_kivi");
            Box(new Vector3(-MuHall - 0.1f, -down, MuWing), new Vector3(MuHall + 0.1f, -0.02f, MuFront + 0.1f), "mu_kivi", "mu_kivi", "mu_kivi", null, "mu_kivi", null);
            Box(new Vector3(-MuW + Wall, -0.1f, MuBack + Wall), new Vector3(MuW - Wall, 0f, MuWing - Wall), null, null, "mu_lattia", null, null, null);
            Box(new Vector3(-MuHall + Wall, -0.1f, MuWing - Wall), new Vector3(MuHall - Wall, 0f, MuFront - Wall), null, null, "mu_lattia", null, null, null);
            // bande de granit au pied des murs (0,45 m), comme une maison corse
            float b = 0.45f, t = 0.05f;
            Box(new Vector3(-MuW - t, 0f, MuBack - t), new Vector3(MuW + t, b, MuBack), "mu_kivi", "mu_kivi", "mu_kivi", null, null, "mu_kivi", false);
            Box(new Vector3(MuW, 0f, MuBack - t), new Vector3(MuW + t, b, MuWing + t), "mu_kivi", null, "mu_kivi", null, "mu_kivi", "mu_kivi", false);
            Box(new Vector3(-MuW - t, 0f, MuBack - t), new Vector3(-MuW, b, MuWing + t), null, "mu_kivi", "mu_kivi", null, "mu_kivi", "mu_kivi", false);
            foreach (float sx in new[] { -1f, 1f })
            {
                float x0 = sx > 0 ? MuHall : -MuW - t, x1 = sx > 0 ? MuW + t : -MuHall;
                Box(new Vector3(x0, 0f, MuWing), new Vector3(x1, b, MuWing + t), null, null, "mu_kivi", null, "mu_kivi", null, false);
                float hx = sx * (MuHall + t / 2);
                Box(new Vector3(hx - t / 2, 0f, MuWing), new Vector3(hx + t / 2, b, MuFront + t), "mu_kivi", "mu_kivi", "mu_kivi", null, "mu_kivi", null, false);
            }

            // ailes : facade (z = +5) en deux morceaux, arriere, pignons ; hautes fenetres a volets
            float[] fx = { 4.7f, 6.5f, 8.3f };
            foreach (float sx in new[] { -1f, 1f })
            {
                var holes = fx.Select(x => new Hole(sx * x - 0.5f, sx * x + 0.5f, 0.95f, 3.05f)).ToArray();
                float u0 = sx > 0 ? MuHall : -MuW, u1 = sx > 0 ? MuW : -MuHall;
                MWall(true, MuWing, -1, u0, u1, 0f, MuWingH, holes, "mu_seina", "mu_valko");
                foreach (Hole h in holes) { Window(true, MuWing, -1, h, 1); Shutters(true, MuWing, 1, h); }
                var back = new[] { 5.5f, 8.2f }.Select(x => new Hole(sx * x - 0.5f, sx * x + 0.5f, 1.2f, 2.9f)).ToArray();
                MWall(true, MuBack, 1, u0, u1, 0f, MuWingH, back, "mu_seina", "mu_valko");
                foreach (Hole h in back) { Window(true, MuBack, 1, h, 1); Shutters(true, MuBack, -1, h); }
                var side = new[] { new Hole(-2.6f, -1.6f, 0.95f, 3.05f), new Hole(1.6f, 2.6f, 0.95f, 3.05f) };
                MWall(false, sx * MuW, -sx, MuBack + Wall, MuWing - Wall, 0f, MuWingH, side, "mu_seina", "mu_valko");
                foreach (Hole h in side) { Window(false, sx * MuW, -sx, h, 1); Shutters(false, sx * MuW, sx, h); }
                // pignon des ailes
                float ridge = MuWingH + (MuWing - MuBack) / 2f * Mathf.Tan(MuWingPitch * Mathf.Deg2Rad), zc = (MuWing + MuBack) / 2f;
                if (sx > 0) Tri("mu_seina", new Vector3(MuW, MuWingH, MuBack), new Vector3(MuW, ridge, zc), new Vector3(MuW, MuWingH, MuWing));
                else Tri("mu_seina", new Vector3(-MuW, MuWingH, MuWing), new Vector3(-MuW, ridge, zc), new Vector3(-MuW, MuWingH, MuBack));
                // planches d'angle blanches
                foreach (float cz in new[] { MuBack, MuWing })
                {
                    float cx = sx * MuW;
                    Box(new Vector3(cx - (sx > 0 ? 0.12f : 0.03f), 0.45f, cz - (cz > 0 ? 0.12f : 0.03f)), new Vector3(cx + (sx > 0 ? 0.03f : 0.12f), MuWingH, cz + (cz > 0 ? 0.03f : 0.12f)), "lista", false);
                }
                // hall : murs lateraux (au-dessus du plafond des ailes, puis pleine hauteur sur l'avancee)
                MWall(false, sx * MuHall, -sx, MuBack, MuWing, MuCeil, MuHallH, new Hole[0], "mu_seina", "mu_valko");
                MWall(false, sx * MuHall, -sx, MuWing, MuFront - Wall, 0f, MuHallH, new[] { new Hole(5.6f, 6.4f, 0.9f, 3.2f) }, "mu_seina", "mu_valko");
                Window(false, sx * MuHall, -sx, new Hole(5.6f, 6.4f, 0.9f, 3.2f), 0);
                // angles du hall (anthracite, en saillie)
                Box(new Vector3(sx * MuHall - 0.09f, 0.45f, MuFront - 0.09f), new Vector3(sx * MuHall + 0.09f, MuHallH, MuFront + 0.06f), "mu_alu", false);
            }
            // hall : arriere (pignon plein, pour un grand tableau dedans)
            MWall(true, MuBack, 1, -MuHall, MuHall, 0f, MuHallH, new Hole[0], "mu_seina", "mu_valko");
            float hr = MuHallH + MuHall * Mathf.Tan(MuHallPitch * Mathf.Deg2Rad);
            Tri("mu_seina", new Vector3(-MuHall, MuHallH, MuBack), new Vector3(0, hr, MuBack), new Vector3(MuHall, MuHallH, MuBack));
            Tri("mu_valko", new Vector3(MuHall, MuHallH, MuBack + Wall), new Vector3(0, hr, MuBack + Wall), new Vector3(-MuHall, MuHallH, MuBack + Wall));
        }

        static void MuseoFront(float frontDrop)
        {
            float z = MuFront, zg = MuFront - 0.08f;
            // poteaux et traverses anthracite (le vitrage est en retrait de 8 cm)
            foreach (float x in new[] { -MuHall + 0.06f, -MuDoorHalf - 0.05f, MuDoorHalf + 0.05f, MuHall - 0.06f })
                Box(new Vector3(x - 0.06f, 0f, zg - 0.07f), new Vector3(x + 0.06f, 4.0f, z), "mu_alu", false);
            Box(new Vector3(-MuHall, 2.48f, zg - 0.07f), new Vector3(MuHall, 2.62f, z), "mu_alu", false);   // linteau des portes
            Box(new Vector3(-MuHall, 3.94f, zg - 0.07f), new Vector3(MuHall, 4.06f, z), "mu_alu", false);
            Box(new Vector3(-0.05f, 2.62f, zg - 0.07f), new Vector3(0.05f, 3.94f, z), "mu_alu", false);
            foreach (float sx in new[] { -1f, 1f })
            {
                float x0 = sx > 0 ? MuDoorHalf + 0.11f : -MuHall + 0.12f, x1 = sx > 0 ? MuHall - 0.12f : -MuDoorHalf - 0.11f;
                Box(new Vector3(x0, 0f, zg - 0.06f), new Vector3(x1, 0.12f, z - 0.01f), "mu_alu", false);   // seuil des vitres fixes
                GlassX(x0, x1, 0.12f, 2.48f, zg);
                cols.Add(new Bounds(new Vector3((x0 + x1) / 2, 1.3f, zg), new Vector3(x1 - x0, 2.6f, 0.16f)));
                GlassX(sx > 0 ? 0.05f : -MuHall + 0.12f, sx > 0 ? MuHall - 0.12f : -0.05f, 2.62f, 3.94f, zg);
            }
            cols.Add(new Bounds(new Vector3(0, 3.3f, zg), new Vector3(2 * MuHall, 1.6f, 0.16f)));
            // au-dessus : bardage, enseigne
            MWall(true, z, -1, -MuHall, MuHall, 4.06f, MuHallH, new Hole[0], "mu_seina", "mu_valko");
            float kw = 5.5f, kh = kw * 384f / 2048f, ky0 = 4.45f, kz = z + 0.07f;
            Box(new Vector3(-kw / 2 - 0.06f, ky0 - 0.06f, z), new Vector3(kw / 2 + 0.06f, ky0 + kh + 0.06f, kz), "mu_alu", false);
            Quad("mu_kyltti", new Vector3(kw / 2, ky0, kz + 0.002f), new Vector3(-kw / 2, ky0, kz + 0.002f), new Vector3(-kw / 2, ky0 + kh, kz + 0.002f), new Vector3(kw / 2, ky0 + kh, kz + 0.002f),
                 new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) });
            // pignon vitre (le hall est ouvert jusqu'au faitage), cadre anthracite
            float hr = MuHallH + MuHall * Mathf.Tan(MuHallPitch * Mathf.Deg2Rad);
            Vector3 l = new Vector3(MuHall, MuHallH, zg), top = new Vector3(0, hr, zg), r = new Vector3(-MuHall, MuHallH, zg);
            Tri("lasi", l, top, r); Tri("lasi", r, top, l);
            Box(new Vector3(-MuHall, MuHallH - 0.06f, zg - 0.07f), new Vector3(MuHall, MuHallH + 0.06f, z), "mu_alu", false);
            Beam(new Vector3(MuHall, MuHallH, zg - 0.01f), new Vector3(0, hr, zg - 0.01f), 0.12f, 0.12f, "mu_alu");
            Beam(new Vector3(-MuHall, MuHallH, zg - 0.01f), new Vector3(0, hr, zg - 0.01f), 0.12f, 0.12f, "mu_alu");
            Box(new Vector3(-0.05f, MuHallH, zg - 0.07f), new Vector3(0.05f, hr - 0.05f, z), "mu_alu", false);
            foreach (float sx in new[] { -1f, 1f })
            {
                float x = sx * MuHall * 0.5f, yt = MuHallH + (MuHall - Mathf.Abs(x)) * Mathf.Tan(MuHallPitch * Mathf.Deg2Rad);
                Box(new Vector3(x - 0.04f, MuHallH, zg - 0.06f), new Vector3(x + 0.04f, yt - 0.04f, z - 0.01f), "mu_alu", false);
            }

            // auvent plat sur deux poteaux, neige dessus
            float cz0 = z, cz1 = z + 2.0f, cy = 3.0f;
            Box(new Vector3(-2.7f, cy, cz0), new Vector3(2.7f, cy + 0.22f, cz1), "mu_alu", "mu_alu", "mu_alu", "mu_lauta", "mu_alu", null, false);
            Box(new Vector3(-2.65f, cy + 0.22f, cz0 + 0.02f), new Vector3(2.65f, cy + 0.3f, cz1 - 0.04f), "lumi", false);
            foreach (float sx in new[] { -1f, 1f })
                Box(new Vector3(sx * 2.45f - 0.07f, -0.02f, cz1 - 0.3f), new Vector3(sx * 2.45f + 0.07f, cy, cz1 - 0.16f), "mu_alu");

            // parvis en granit et marches jusqu'au sol
            float pz1 = z + 2.4f;
            Box(new Vector3(-3.1f, -frontDrop - 1.0f, z), new Vector3(3.1f, -0.02f, pz1), "mu_kivi");
            int steps = Mathf.Max(0, Mathf.CeilToInt((frontDrop - 0.05f) / 0.17f));
            float rise = steps > 0 ? (frontDrop - 0.02f) / (steps + 0.0001f) : 0;
            for (int k = 1; k <= steps; k++)
                Box(new Vector3(-3.1f, -frontDrop - 1.0f, pz1 + (k - 1) * 0.34f), new Vector3(3.1f, -0.02f - k * rise, pz1 + k * 0.34f), "mu_kivi");
            // paillasson
            Plane("mu_alu", new Vector3(0, 0.004f, z - 0.9f), 2.0f, 1.2f, Vector3.up);
        }

        // vitre plane (deux faces) dans le plan z, de x0 a x1
        static void GlassX(float x0, float x1, float y0, float y1, float z)
        {
            Quad("lasi", new Vector3(x0, y0, z), new Vector3(x1, y0, z), new Vector3(x1, y1, z), new Vector3(x0, y1, z));
            Quad("lasi", new Vector3(x1, y0, z), new Vector3(x0, y0, z), new Vector3(x0, y1, z), new Vector3(x1, y1, z));
        }

        // toit a deux pentes : alongX = faitage le long de x ; c0..c1 : etendue le long du faitage ; S : demi-portee ;
        // mid : position du faitage en travers ; he : hauteur a l'aplomb des murs
        static void GableRoof(bool alongX, float c0, float c1, float mid, float S, float he, float pitch, float over, string bottom)
        {
            float tan = Mathf.Tan(pitch * Mathf.Deg2Rad), cos = Mathf.Cos(pitch * Mathf.Deg2Rad), run = S + over, t = 0.14f, len = c1 - c0, cm = (c0 + c1) / 2;
            foreach (float s in new[] { 1f, -1f })
            {
                Quaternion r = alongX ? Quaternion.Euler(s * pitch, 0, 0) : Quaternion.Euler(0, 0, -s * pitch);
                Vector3 nrm = r * Vector3.up;
                float y = he + (S - run / 2) * tan;
                Vector3 under = alongX ? new Vector3(cm, y, mid + s * run / 2) : new Vector3(mid + s * run / 2, y, cm);
                Vector3 size = alongX ? new Vector3(len, t, run / cos) : new Vector3(run / cos, t, len);
                OBox(under + nrm * (t / 2), r, size, "mu_katto", bottom, "lista");
                Vector3 snow = alongX ? new Vector3(len - 0.2f, 0.1f, run / cos - 0.15f) : new Vector3(run / cos - 0.15f, 0.1f, len - 0.2f);
                OBox(under + nrm * (t + 0.05f) - (alongX ? new Vector3(0, 0, s * 0.07f) : new Vector3(s * 0.07f, 0, 0)) * cos, r, snow, "lumi", null, "lumi");
            }
            float ridge = he + S * tan;
            if (alongX) Box(new Vector3(c0 + 0.1f, ridge + 0.08f, mid - 0.15f), new Vector3(c1 - 0.1f, ridge + 0.24f, mid + 0.15f), "lumi", false);
            else Box(new Vector3(mid - 0.15f, ridge + 0.08f, c0 + 0.1f), new Vector3(mid + 0.15f, ridge + 0.24f, c1 - 0.1f), "lumi", false);
        }

        static void MuseoRoofs()
        {
            float zc = (MuWing + MuBack) / 2f, S = (MuWing - MuBack) / 2f;
            GableRoof(true, MuHall, MuW + 0.45f, zc, S, MuWingH, MuWingPitch, 0.55f, "mu_lauta");
            GableRoof(true, -MuW - 0.45f, -MuHall, zc, S, MuWingH, MuWingPitch, 0.55f, "mu_lauta");
            // hall : faitage le long de z, de l'arriere a l'avant (+ debord), sous-face en bois visible de l'interieur
            GableRoof(false, MuBack - 0.5f, MuFront + 0.7f, 0f, MuHall, MuHallH, MuHallPitch, 0.45f, "mu_puukatto");
        }

        static void MuseoInterior()
        {
            // plafond plat des ailes (le hall est ouvert jusqu'au toit)
            foreach (float sx in new[] { -1f, 1f })
            {
                float x0 = sx > 0 ? MuHall : -MuW + Wall, x1 = sx > 0 ? MuW - Wall : -MuHall;
                Quad("mu_puukatto", new Vector3(x0, MuCeil, MuWing - Wall), new Vector3(x1, MuCeil, MuWing - Wall), new Vector3(x1, MuCeil, MuBack + Wall), new Vector3(x0, MuCeil, MuBack + Wall));
            }
            // poutre blanche le long des ouvertures hall / ailes
            foreach (float sx in new[] { -1f, 1f })
                Box(new Vector3(sx * MuHall - 0.15f, MuCeil - 0.35f, MuBack + Wall), new Vector3(sx * MuHall + 0.15f, MuCeil + 0.01f, MuWing - Wall), "mu_valko", false);
        }

        // ---------------------------------------------------------------- portes coulissantes
        static void MuseoDoors()
        {
            museoLeaves = new Transform[2];
            float zi = MuFront - 0.24f, th = 0.045f, top = 2.46f, f = 0.07f;
            for (int k = 0; k < 2; k++)
            {
                float sx = k == 0 ? -1f : 1f;
                var g = new GameObject("museo_ovi" + k);
                g.transform.SetParent(museo.transform, false);
                g.transform.localPosition = new Vector3(0, 0.01f, zi);
                // vantail de x = 0 (milieu) a sx * MuDoorHalf, cadre anthracite, grande vitre, poignee verticale
                float a = sx > 0 ? 0f : -MuDoorHalf, b = sx > 0 ? MuDoorHalf : 0f;
                Box(new Vector3(a, 0, -th / 2), new Vector3(b, f, th / 2), "mu_alu", false);
                Box(new Vector3(a, top - f, -th / 2), new Vector3(b, top, th / 2), "mu_alu", false);
                Box(new Vector3(a, 0, -th / 2), new Vector3(a + f, top, th / 2), "mu_alu", false);
                Box(new Vector3(b - f, 0, -th / 2), new Vector3(b, top, th / 2), "mu_alu", false);
                GlassX(a + f, b - f, f, top - f, 0f);
                float hx = sx > 0 ? a + 0.16f : b - 0.16f;
                Box(new Vector3(hx - 0.015f, 0.8f, th / 2), new Vector3(hx + 0.015f, 1.5f, th / 2 + 0.04f), "metalli", false);
                Box(new Vector3(hx - 0.015f, 0.8f, -th / 2 - 0.04f), new Vector3(hx + 0.015f, 1.5f, -th / 2), "metalli", false);
                var keep = root; root = g;
                Flush();
                root = keep;
                var cg = new GameObject("museo_ovi_coll");
                cg.transform.SetParent(g.transform, false);
                cg.transform.localPosition = new Vector3((a + b) / 2, top / 2, 0);
                cg.AddComponent<BoxCollider>().size = new Vector3(b - a, top, 0.08f);
                museoLeaves[k] = g.transform;
            }
            // caisson du mecanisme au-dessus, cote interieur
            Box(new Vector3(-2.6f, 2.48f, zi - 0.16f), new Vector3(2.6f, 2.72f, zi + 0.03f), "mu_alu", false);
            Flush();
        }

        static void TickMuseo()
        {
            if (museo == null) return;
            if (museoLeaves != null)
            {
                float now = Time.time;
                Vector3 c = museo.transform.TransformPoint(new Vector3(0, 1f, MuFront - 0.2f));
                if (forceMuseoDoor || Near(Game.PlayerT, c)) museoNear = now;
                else foreach (Avatar av in PlayerSync.Avatars) if (av != null && av.Root != null && av.Root.activeInHierarchy && Near(av.Root.transform, c)) { museoNear = now; break; }
                float target = now - museoNear < 1.2f ? 1f : 0f;
                if (museoOpen != target)
                {
                    museoOpen = Mathf.MoveTowards(museoOpen, target, Time.deltaTime * 1.6f);
                    float e = museoOpen * museoOpen * (3f - 2f * museoOpen);
                    museoLeaves[0].localPosition = new Vector3(-e * MuDoorSlide, 0.01f, MuFront - 0.24f);
                    museoLeaves[1].localPosition = new Vector3(e * MuDoorSlide, 0.01f, MuFront - 0.24f);
                }
            }
            TickFlags();
        }

        // devant ou derriere les portes, a moins de 3,2 m (a plat) et pas a un autre etage
        static bool Near(Transform t, Vector3 c)
        {
            if (t == null) return false;
            Vector3 d = t.position - c;
            return Mathf.Abs(d.y) < 2.6f && d.x * d.x + d.z * d.z < 3.2f * 3.2f;
        }

        static void MuseoLights()
        {
            var warm = new Color(1f, 0.86f, 0.68f);
            Action<Vector3, float, float> point = (p, i, r) =>
            {
                var g = new GameObject("museo_valo");
                g.transform.SetParent(museo.transform, false); g.transform.localPosition = p;
                var l = g.AddComponent<Light>(); l.type = LightType.Point; l.color = warm; l.intensity = i; l.range = r; l.shadows = LightShadows.None;
            };
            point(new Vector3(0, 5.2f, 0.5f), 2.0f, 11f);
            point(new Vector3(-6.6f, 3.3f, -1.5f), 2.0f, 9f);
            point(new Vector3(-6.6f, 3.3f, 2.5f), 1.6f, 7f);
            point(new Vector3(6.6f, 3.3f, -1.5f), 2.0f, 9f);
            point(new Vector3(6.6f, 3.3f, 2.5f), 1.6f, 7f);
            point(new Vector3(0, 2.75f, MuFront + 1.2f), 1.3f, 6f);   // sous l'auvent
            // enseigne : deux projecteurs sur l'auvent
            foreach (float x in new[] { -1.6f, 1.6f })
            {
                var g = new GameObject("museo_valo_kyltti");
                g.transform.SetParent(museo.transform, false);
                g.transform.localPosition = new Vector3(x, 3.4f, MuFront + 1.0f);
                g.transform.localRotation = Quaternion.LookRotation(new Vector3(0, 0.9f, -0.75f));
                var l = g.AddComponent<Light>(); l.type = LightType.Spot; l.spotAngle = 70f; l.color = new Color(1f, 0.92f, 0.78f); l.intensity = 1.8f; l.range = 4.5f; l.shadows = LightShadows.None;
                var lamp = new GameObject("museo_spotti");
                lamp.transform.SetParent(museo.transform, false);
                lamp.transform.localPosition = g.transform.localPosition;
                lamp.transform.localRotation = g.transform.localRotation;
                parts.Clear(); cols.Clear();
                var keep = root; root = lamp;
                Box(new Vector3(-0.07f, -0.07f, -0.1f), new Vector3(0.07f, 0.07f, 0.12f), "mu_alu", false);
                Flush();
                root = keep;
            }
        }

        // zone abritee du jeu (pas de pluie ni de neige dedans) : copie du NoRain du magasin de Teimo a notre forme
        static void MuseoShelter()
        {
            GameObject src = FindPath("STORE_AREA/Stuff/LOD/NoRain");
            if (src == null) { L("musee : NoRain introuvable"); return; }
            var g = (GameObject)UnityEngine.Object.Instantiate(src);
            g.name = "NoRainMuseo";
            g.transform.SetParent(museo.transform, false);
            g.transform.localPosition = Vector3.zero; g.transform.localRotation = Quaternion.identity; g.transform.localScale = Vector3.one;
            var mc = g.GetComponent<MeshCollider>();
            float y = MuCeil + 0.05f;
            var m = new Mesh { name = "museo_norain" };
            m.vertices = new[] { new Vector3(-MuW, y, MuBack), new Vector3(MuW, y, MuBack), new Vector3(MuW, y, MuWing), new Vector3(-MuW, y, MuWing),
                                 new Vector3(-MuHall, y, MuWing), new Vector3(MuHall, y, MuWing), new Vector3(MuHall, y, MuFront + 2f), new Vector3(-MuHall, y, MuFront + 2f) };
            m.triangles = new[] { 0, 2, 1, 0, 3, 2, 0, 1, 2, 0, 2, 3, 4, 6, 5, 4, 7, 6, 4, 5, 6, 4, 6, 7 };
            m.RecalculateBounds();
            if (mc != null) { mc.sharedMesh = null; mc.sharedMesh = m; }
            g.SetActive(true);
        }

        // ---------------------------------------------------------------- drapeaux (sans physique)
        // mat vertical pose au sol ; corsica : tete de Maure, sinon drapeau finlandais (dessine ici)
        static void MuseoFlagpole(Vector3 local, bool corsica)
        {
            Vector3 w = museo.transform.TransformPoint(local);
            RaycastHit? h = GroundHit(new Vector3(w.x, w.y + 50f, w.z));
            float gy = h.HasValue ? museo.transform.InverseTransformPoint(h.Value.point).y : -0.5f;
            float height = 7.5f;
            Vector3 foot = new Vector3(local.x, gy - 0.3f, local.z), tip = new Vector3(local.x, gy + height, local.z);
            var pole = new GameObject("museo_lipputanko");
            pole.transform.SetParent(museo.transform, false);
            pole.AddComponent<MeshFilter>().sharedMesh = Cylinder(foot, tip, 0.045f, 10);
            pole.AddComponent<MeshRenderer>().sharedMaterial = mats["lista"];
            var knob = new GameObject("museo_lipputanko_nuppi");
            knob.transform.SetParent(museo.transform, false);
            knob.AddComponent<MeshFilter>().sharedMesh = Cylinder(tip, tip + Vector3.up * 0.12f, 0.07f, 10);
            knob.AddComponent<MeshRenderer>().sharedMaterial = mats["metalli"];
            // pied en granit
            parts.Clear(); cols.Clear();
            var keep = root; root = museo;
            Box(new Vector3(local.x - 0.3f, gy - 0.4f, local.z - 0.3f), new Vector3(local.x + 0.3f, gy + 0.12f, local.z + 0.3f), "mu_kivi");
            Flush();
            root = keep;

            Texture2D tex;
            float fw, fh;
            if (corsica) { tex = Res("lippu-korsika.png", TextureFormat.RGB24); fw = 1.8f; fh = 1.08f; }
            else
            {
                // drapeau finlandais 18:11, croix bleue
                tex = new Texture2D(180, 110, TextureFormat.RGB24, true) { name = "museo_suomi" };
                var blue = new Color(0f, 0.18f, 0.42f);
                var px = new Color[180 * 110];
                for (int y = 0; y < 110; y++)
                    for (int x = 0; x < 180; x++)
                        px[y * 180 + x] = (x >= 50 && x < 80) || (y >= 40 && y < 70) ? blue : Color.white;
                tex.SetPixels(px); tex.Apply();
                fw = 1.8f; fh = 1.1f;
            }
            tex.wrapMode = TextureWrapMode.Clamp;
            Material basis = GameMat("wood_panel7");
            var mat = new Material(basis != null ? basis.shader : Shader.Find("Legacy Shaders/Diffuse")) { name = "museo_lippu", mainTexture = tex, color = Color.white };

            // grille : i le long du vol (0 au mat), j vers le bas ; le drapeau flotte vers +x (le vent vient de la route)
            var fl = new Flag { U = 18, V = 10, Phase = corsica ? 0f : 1.7f };
            Vector3 top = new Vector3(local.x + 0.05f, gy + height - 0.1f, local.z);
            fl.Fly = Vector3.right; fl.Side = Vector3.forward;
            var v = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>();
            for (int j = 0; j <= fl.V; j++)
                for (int i = 0; i <= fl.U; i++)
                {
                    v.Add(top + fl.Fly * (fw * i / fl.U) + Vector3.down * (fh * j / fl.V));
                    uv.Add(new Vector2(i / (float)fl.U, 1f - j / (float)fl.V));
                }
            for (int j = 0; j < fl.V; j++)
                for (int i = 0; i < fl.U; i++)
                {
                    int a = j * (fl.U + 1) + i, b = a + 1, c = a + fl.U + 1, d = c + 1;
                    t.Add(a); t.Add(b); t.Add(c); t.Add(b); t.Add(d); t.Add(c);
                }
            fl.Rest = v.ToArray(); fl.Pos = new Vector3[fl.Rest.Length]; fl.Nrm = new Vector3[fl.Rest.Length];
            var go = new GameObject("museo_lippu");
            go.transform.SetParent(museo.transform, false);
            fl.Front = new Mesh { name = "museo_lippu" };
            fl.Front.vertices = fl.Rest; fl.Front.uv = uv.ToArray(); fl.Front.triangles = t.ToArray();
            fl.Front.RecalculateNormals(); fl.Front.RecalculateBounds(); fl.Front.MarkDynamic();
            go.AddComponent<MeshFilter>().sharedMesh = fl.Front;
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
            var back = new GameObject("museo_lippu_taka");
            back.transform.SetParent(go.transform, false);
            fl.Back = new Mesh { name = "museo_lippu_taka" };
            fl.Back.vertices = fl.Rest; fl.Back.uv = uv.ToArray();
            var bt = new int[t.Count];
            for (int k = 0; k < t.Count; k += 3) { bt[k] = t[k]; bt[k + 1] = t[k + 2]; bt[k + 2] = t[k + 1]; }
            fl.Back.triangles = bt;
            fl.Back.RecalculateNormals(); fl.Back.RecalculateBounds(); fl.Back.MarkDynamic();
            back.AddComponent<MeshFilter>().sharedMesh = fl.Back;
            back.AddComponent<MeshRenderer>().sharedMaterial = mat;
            fl.T = go.transform;
            flags.Add(fl);
        }

        // un drapeau qui flotte au vent : ondes qui courent du mat vers le bout, le bout libre bat plus fort et tombe un peu
        static void TickFlags()
        {
            if (flags.Count == 0) return;
            Camera cam = Game.MainCam;
            float time = Time.time;
            foreach (Flag fl in flags)
            {
                if (fl.T == null) continue;
                if (cam != null && (cam.transform.position - fl.T.position).sqrMagnitude > 150f * 150f) continue;
                float tt = time + fl.Phase, gust = 0.6f + 0.4f * Mathf.Sin(tt * 0.31f) * Mathf.Sin(tt * 0.17f + 0.8f);
                for (int j = 0; j <= fl.V; j++)
                {
                    float f = j / (float)fl.V;
                    for (int i = 0; i <= fl.U; i++)
                    {
                        float g = i / (float)fl.U;
                        float wave = Mathf.Sin(tt * 5.2f - g * 7.5f + f * 0.6f) * 0.11f + Mathf.Sin(tt * 8.3f - g * 13f + f * 1.3f) * 0.03f;
                        Vector3 off = fl.Side * (wave * g * (0.5f + 0.7f * gust))
                                    + Vector3.down * ((1.2f - gust) * 0.35f * g * g)
                                    - fl.Fly * (0.06f * g * Mathf.Abs(wave) * 4f);
                        int k = j * (fl.U + 1) + i;
                        fl.Pos[k] = fl.Rest[k] + off;
                    }
                }
                fl.Front.vertices = fl.Pos;
                fl.Front.RecalculateNormals();
                fl.Front.RecalculateBounds();
                Vector3[] ns = fl.Front.normals;
                for (int k = 0; k < ns.Length; k++) fl.Nrm[k] = -ns[k];
                fl.Back.vertices = fl.Pos;
                fl.Back.normals = fl.Nrm;
                fl.Back.RecalculateBounds();
            }
        }
    }
}
