using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace MWCoop
{
    // Magasin de pieces CARDINAL VARAOSAT (demande de JD, 23/10, pour le moddeur Cardinal qui fait des vehicules pour le coop :
    // un batiment ou ses mods vendent leurs pieces). Pres du camping de la station de ski, dans la bande libre entre la piste
    // nord-sud et la barriere bleue de la piste de ski (JD : « ouest de la piste » ; la place pavee a l'est est le depart de la
    // speciale SS2 du rallye du samedi -- route des voitures et spectateurs -- d'apres le vidage du jeu : on n'y construit pas,
    // on y peint seulement les places). Construit en code comme le cinema (memes outils), dans son repere : x = longueur
    // (-10 a +10, le long de la piste), z = profondeur (arriere -5, vitrine +5 vers la piste), y = 0 au plancher.
    //  - aile ouest (x < 0) : CARDINAL, pieces des mods (CoopApi.AddShopItem) ; aile est (x > 0) : KAYTETYT OSAT, pieces
    //    d'occasion de la Rivett renouvelees tous les 3 jours (OsatMyynti) ; comptoir et vendeur au fond, au milieu ;
    //  - etageres (4 niveaux de 3 cases) le long du fond et du pignon de chaque aile, deux estrades pour les grosses pieces ;
    //    palettes NOUTO (retrait) devant le comptoir : les achats y apparaissent.
    // [Osat] Paikka=x,z,cap[,plancher|auto] : place, cap et hauteur du plancher (les memes chez tous).
    public static partial class Kauppa
    {
        const float OL = 10f, OD = 5f, OH = 3.6f, OCeil = 3.2f;

        static GameObject osat;
        static Transform[] osatLeaves;
        static float osatOpen, osatNear = -10f, osatDrop;

        public static bool OsatOn { get { return Ajouts.Osat && Config.GetInt("Osat", "Active", 1) != 0; } }

        static void ResetOsat() { osat = null; osatLeaves = null; osatOpen = 0f; osatNear = -10f; ResetOsatMyynti(); }

        // place et cap du magasin (aussi pour le defrichage, Clearing). Releve du 23/10 : piste (Road2) a x -1748 environ, barriere
        // de la piste de ski a -1760 (z -115) .. -1772 (z -150) : le batiment (20 x 10) entre z -146 et -127, vitrine a 2,8 m de la piste.
        public static void OsatFrame(out Vector3 c, out float yaw, out string floor)
        {
            string s = Config.Get("Osat", "Paikka", "-1755.8,-136.7,85.7,auto");
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            string[] a = s.Split(',');
            c = new Vector3(float.Parse(a[0], ci), 0f, float.Parse(a[1], ci));
            yaw = float.Parse(a[2], ci);
            floor = a.Length > 3 ? a[3].Trim() : "auto";
        }

        // terrain du magasin (defrichage) : le batiment et son parvis, pas la piste
        public static Vector2[] OsatPlot()
        {
            Vector3 c; float yaw; string fl;
            OsatFrame(out c, out yaw, out fl);
            Quaternion r = Quaternion.Euler(0, yaw, 0);
            var pts = new[] { new Vector3(-OL - 2f, 0, -OD - 2f), new Vector3(OL + 2f, 0, -OD - 2f), new Vector3(OL + 2f, 0, OD + 2.2f), new Vector3(-OL - 2f, 0, OD + 2.2f) };
            return pts.Select(p => { Vector3 w = c + r * p; return new Vector2(w.x, w.z); }).ToArray();
        }

        // plateforme de terrassement (format de [Boutique] Tasanne) : le batiment
        public static string OsatPad()
        {
            Vector3 c; float yaw; string fl;
            OsatFrame(out c, out yaw, out fl);
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            return string.Format(ci, "{0:F1},{1:F1},{2:F1},{3:F1},{4:F1}", c.x, c.z, yaw, OL + 0.5f, OD + 0.5f);
        }

        static void OsatMaterials()
        {
            KinoMaterials();   // (brique, beton, alu, verre, lignes jaunes, neige... : les memes que le cinema)
            // (peintures unies : JD ne veut pas de textures du jeu qui se repetent ou qui font sale)
            Mat("os_ulko", "paint_black_matte", 1f, new Color(0.78f, 0.8f, 0.83f));      // bardage gris clair
            Mat("os_puna", "paint_black_matte", 1f, new Color(0.75f, 0.1f, 0.09f));      // bandeau rouge
            Mat("os_sisa", "paint_black_matte", 1f, new Color(0.92f, 0.91f, 0.88f));     // murs dedans
            Mat("os_lattia", "paint_black_matte", 1f, new Color(0.42f, 0.44f, 0.46f));   // sol beton peint
            Mat("os_katto", "paint_black_matte", 1f, new Color(0.95f, 0.95f, 0.94f));    // plafond
            Mat("os_pysty", "paint_black_matte", 1f, new Color(0.12f, 0.28f, 0.62f));    // montants des etageres, bleus
            Mat("os_palkki", "paint_black_matte", 1f, new Color(0.95f, 0.52f, 0.08f));   // lisses orange
            Mat("os_taso", "paint_black_matte", 1f, new Color(0.62f, 0.64f, 0.66f));     // tablettes galvanisees
            Mat("os_lava", "paint_black_matte", 1f, new Color(0.72f, 0.58f, 0.38f));     // palettes en bois
            Mat("os_musta", "paint_black_matte", 1f, new Color(0.08f, 0.08f, 0.09f));
            OwnMat("os_kyltti", "osat-kyltti.png", true);
            OwnMat("os_cardinal", "osat-cardinal.png", true);
            OwnMat("os_kaytetyt", "osat-kaytetyt.png", true);
            OwnMat("os_nouto", "osat-nouto.png", true);
        }

        static void BuildOsat()
        {
            Vector3 place; float yaw; string fl;
            OsatFrame(out place, out yaw, out fl);
            Quaternion rot = Quaternion.Euler(0, yaw, 0);
            OsatMaterials();
            float gMax = float.MinValue, gMin = float.MaxValue;
            for (float x = -OL; x <= OL; x += 2f)
                for (float z = -OD; z <= OD; z += 2f)
                {
                    RaycastHit? h = GroundHit(place + rot * new Vector3(x, 0, z) + Vector3.up * 200f);
                    if (!h.HasValue) continue;
                    gMax = Mathf.Max(gMax, h.Value.point.y); gMin = Mathf.Min(gMin, h.Value.point.y);
                }
            if (gMax == float.MinValue) gMax = gMin = 7f;
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            float padY = !float.IsNaN(Clearing.OsatPadY) ? Clearing.OsatPadY : gMin;
            float floorY = fl != "auto" ? float.Parse(fl, ci) : Mathf.Max(padY + 0.2f, gMax + 0.15f);
            osatDrop = Mathf.Max(0.06f, floorY - gMin);
            L(string.Format("magasin de pieces : sol {0:F2}..{1:F2}, plateforme {2}, plancher a {3:F2}", gMin, gMax, float.IsNaN(Clearing.OsatPadY) ? "-" : Clearing.OsatPadY.ToString("F2"), floorY));
            float down = floorY - gMin + 0.6f;

            osat = new GameObject("MWCoop CARDINAL VARAOSAT");
            osat.transform.position = new Vector3(place.x, floorY, place.z);
            osat.transform.rotation = rot;
            var keepRoot = root; root = osat;
            try
            {
                OsatParking();
                Flush();
                OsatShell(down);
                OsatFront();
                OsatInterior();
                Flush();
                OsatDoors();
                OsatLights();
                OsatShelter();
                OsatVendor();
                Flush();
            }
            finally { root = keepRoot; }
            L("magasin de pieces construit en " + osat.transform.position.ToString("F1"));
        }

        // ---------------------------------------------------------------- coque
        static void OsatShell(float down)
        {
            // soubassement et dalle
            Box(new Vector3(-OL - 0.1f, -down, -OD - 0.1f), new Vector3(OL + 0.1f, 0f, OD + 0.1f), "ki_betoni", "ki_betoni", "os_lattia", null, "ki_betoni", "ki_betoni");
            // murs : facade vitree (z = +5 : deux vitrines et la porte), fond et pignons pleins ; plinthe de brique dehors
            var holes = new[] { new Hole(-9f, -1.7f, 0.5f, 2.6f), new Hole(-1.3f, 1.3f, 0f, 2.4f), new Hole(1.7f, 9f, 0.5f, 2.6f) };
            MWall(true, OD, -1, -OL, OL, 0f, OH, holes, "os_ulko", "os_sisa");
            MWall(true, -OD, 1, -OL, OL, 0f, OH, new Hole[0], "os_ulko", "os_sisa");
            foreach (float s in new[] { -1f, 1f }) MWall(false, s * OL, -s, -OD + Wall, OD - Wall, 0f, OH, new Hole[0], "os_ulko", "os_sisa");
            Box(new Vector3(-OL - 0.04f, -0.4f, OD), new Vector3(OL + 0.04f, 0.45f, OD + 0.04f), "ki_tiili", false);
            Box(new Vector3(-OL - 0.04f, -0.4f, -OD - 0.04f), new Vector3(OL + 0.04f, 0.45f, -OD), "ki_tiili", false);
            foreach (float s in new[] { -1f, 1f })
                Box(new Vector3(s > 0 ? OL : -OL - 0.04f, -0.4f, -OD), new Vector3(s > 0 ? OL + 0.04f : -OL, 0.45f, OD), "ki_tiili", false);
            // bandeau rouge en haut des murs (tout le tour)
            Box(new Vector3(-OL - 0.03f, OH - 0.45f, OD), new Vector3(OL + 0.03f, OH - 0.2f, OD + 0.03f), "os_puna", false);
            Box(new Vector3(-OL - 0.03f, OH - 0.45f, -OD - 0.03f), new Vector3(OL + 0.03f, OH - 0.2f, -OD), "os_puna", false);
            foreach (float s in new[] { -1f, 1f })
                Box(new Vector3(s > 0 ? OL : -OL - 0.03f, OH - 0.45f, -OD), new Vector3(s > 0 ? OL + 0.03f : -OL, OH - 0.2f, OD), "os_puna", false);
            // toit plat : dalle, acrotere, neige ; plafond dedans
            Box(new Vector3(-OL - 0.1f, OH, -OD - 0.1f), new Vector3(OL + 0.1f, OH + 0.25f, OD + 0.1f), "ki_betoni", "ki_betoni", "ki_katto", null, "ki_betoni", "ki_betoni");
            Box(new Vector3(-OL + 0.2f, OH + 0.25f, -OD + 0.2f), new Vector3(OL - 0.2f, OH + 0.33f, OD - 0.2f), "lumi", false);
            Quad("os_katto", new Vector3(-OL + Wall, OCeil, OD - Wall), new Vector3(OL - Wall, OCeil, OD - Wall), new Vector3(OL - Wall, OCeil, -OD + Wall), new Vector3(-OL + Wall, OCeil, -OD + Wall));
            // parvis de beton devant la vitrine, jusqu'au bord de la piste (marches si le plancher est haut)
            Box(new Vector3(-OL - 0.5f, -osatDrop - 0.5f, OD), new Vector3(OL + 0.5f, -0.06f, OD + 1.6f), "ki_betoni");
            if (osatDrop > 0.3f) Box(new Vector3(-OL - 0.5f, -osatDrop - 0.5f, OD + 1.6f), new Vector3(OL + 0.5f, -osatDrop / 2f, OD + 1.95f), "ki_betoni");
        }

        static void OsatFront()
        {
            float zg = OD - 0.1f;
            // vitrines : montants noirs tous les 1,5 m, verre, appuis
            foreach (float s in new[] { -1f, 1f })
            {
                float x0 = s > 0 ? 1.7f : -9f, x1 = s > 0 ? 9f : -1.7f;
                for (float x = x0; x <= x1 + 0.01f; x += (x1 - x0) / 5f)
                    Box(new Vector3(x - 0.04f, 0.5f, zg - 0.06f), new Vector3(x + 0.04f, 2.6f, zg + 0.06f), "ki_alu", false);
                GlassX(x0, x1, 0.5f, 2.6f, zg);
                cols.Add(new Bounds(new Vector3((x0 + x1) / 2, 1.55f, zg), new Vector3(x1 - x0, 2.1f, 0.16f)));
                Box(new Vector3(x0, 0.42f, zg - 0.12f), new Vector3(x1, 0.5f, zg + 0.12f), "os_musta", false);
            }
            // cadre de la porte
            Box(new Vector3(-1.4f, 2.4f, zg - 0.08f), new Vector3(1.4f, 2.52f, zg + 0.08f), "ki_alu", false);
            // enseigne sur l'acrotere : CARDINAL VARAOSAT (texture lumineuse), cadre et pieds
            float sw = 9f, sh = sw * 512f / 2048f, sy0 = OH + 0.45f, sz = OD - 0.5f;
            Box(new Vector3(-sw / 2 - 0.1f, sy0 - 0.1f, sz - 0.15f), new Vector3(sw / 2 + 0.1f, sy0 + sh + 0.1f, sz), "ki_alu", false);
            Quad("os_kyltti", new Vector3(sw / 2, sy0, sz + 0.003f), new Vector3(-sw / 2, sy0, sz + 0.003f), new Vector3(-sw / 2, sy0 + sh, sz + 0.003f), new Vector3(sw / 2, sy0 + sh, sz + 0.003f),
                 new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) });
            foreach (float x in new[] { -3.5f, -1.2f, 1.2f, 3.5f })
                Box(new Vector3(x - 0.06f, OH + 0.25f, sz - 0.4f), new Vector3(x + 0.06f, sy0, sz - 0.28f), "ki_alu", false);
            // projecteurs des vitrines (bras sous l'acrotere)
            foreach (float x in new[] { -6f, 6f })
            {
                Box(new Vector3(x - 0.05f, OH - 0.15f, OD), new Vector3(x + 0.05f, OH - 0.05f, OD + 0.7f), "ki_alu", false);
                Box(new Vector3(x - 0.18f, OH - 0.3f, OD + 0.55f), new Vector3(x + 0.18f, OH - 0.1f, OD + 0.85f), "ki_valkoinen", false);
            }
        }

        // ---------------------------------------------------------------- interieur
        // Case d'etagere ou d'estrade (repere du magasin) : bas au centre, sens de la face vers l'allee, taille utile.
        class OsatSlot { public int Wing; public Vector3 Pos, Face, Size; public bool Big; public object Item; }
        static readonly List<OsatSlot> osatSlots = new List<OsatSlot>();
        static Vector3 OsatPickup(int wing) { return new Vector3(wing == 0 ? -3.4f : 3.4f, 0.16f, -0.9f); }

        static void OsatInterior()
        {
            osatSlots.Clear();
            // comptoir (le vendeur derriere, au fond), caisse
            Box(new Vector3(-2.2f, 0f, -2.6f), new Vector3(2.2f, 0.98f, -1.9f), "tiski");
            Box(new Vector3(-2.25f, 0.98f, -2.65f), new Vector3(2.25f, 1.03f, -1.85f), "os_musta");
            Box(new Vector3(-2.2f, 0.08f, -1.9f), new Vector3(2.2f, 0.22f, -1.86f), "os_puna", false);
            try { Clone("INSPECTION/LOD/inspection_desk/CashRegister/InspectionCashRegister", 0, new Vector3(1.4f, 1.03f, -2.3f), 180, new Vector3(0, -1, 0), false); } catch (Exception e) { L("magasin de pieces : caisse " + e.Message); }
            foreach (int wing in new[] { 0, 1 })
            {
                float s = wing == 0 ? -1f : 1f;
                // etageres du fond (3) et du pignon (2)
                foreach (float x in new[] { 8.65f, 6.45f, 4.25f }) OsatShelf(new Vector3(s * x, 0f, -OD + Wall + 0.27f), Vector3.forward, wing);
                foreach (float z in new[] { -2.9f, -0.7f }) OsatShelf(new Vector3(s * (OL - Wall - 0.27f), 0f, z), s < 0 ? Vector3.right : Vector3.left, wing);
                // estrades des grosses pieces, pres des vitrines
                foreach (float x in new[] { 7.2f, 4.4f })
                {
                    Vector3 c = new Vector3(s * x, 0f, 2.4f);
                    Box(new Vector3(c.x - 1.0f, 0f, c.z - 0.7f), new Vector3(c.x + 1.0f, 0.22f, c.z + 0.7f), "os_taso");
                    Box(new Vector3(c.x - 1.02f, 0.18f, c.z - 0.72f), new Vector3(c.x + 1.02f, 0.24f, c.z + 0.72f), "os_palkki", false);
                    osatSlots.Add(new OsatSlot { Wing = wing, Pos = c + new Vector3(0f, 0.24f, 0f), Face = Vector3.back, Size = new Vector3(1.8f, 1.6f, 1.2f), Big = true });
                }
                // palette de retrait (NOUTO) et son panneau
                Vector3 p = OsatPickup(wing);
                Box(new Vector3(p.x - 0.6f, 0f, p.z - 0.4f), new Vector3(p.x + 0.6f, 0.14f, p.z + 0.4f), "os_lava");
                Box(new Vector3(p.x - 0.03f, 0f, p.z - 0.5f), new Vector3(p.x + 0.03f, 1.75f, p.z - 0.44f), "ki_alu", false);
                Plane("os_nouto", new Vector3(p.x, 1.55f, p.z - 0.43f), 0.7f, 0.35f, Vector3.forward);
                // panneau suspendu de l'aile (deux faces)
                Vector3 h = new Vector3(s * 6.3f, 2.55f, 0.2f);
                string key = wing == 0 ? "os_cardinal" : "os_kaytetyt";
                Plane(key, h + new Vector3(0, 0, 0.012f), 2.4f, 0.6f, Vector3.forward);
                Plane(key, h - new Vector3(0, 0, 0.012f), 2.4f, 0.6f, Vector3.back);
                Box(new Vector3(h.x - 1.22f, h.y - 0.32f, h.z - 0.01f), new Vector3(h.x + 1.22f, h.y + 0.32f, h.z + 0.01f), "os_musta", false);
                foreach (float dx in new[] { -1f, 1f }) Box(new Vector3(h.x + dx - 0.01f, h.y + 0.3f, h.z - 0.01f), new Vector3(h.x + dx + 0.01f, OCeil, h.z + 0.01f), "ki_alu", false);
            }
            // affiches de rallye du garage de Fleetari, sur le mur du fond derriere le comptoir
            try
            {
                Clone("REPAIRSHOP/LOD/Office/rally_poster", 0, new Vector3(-1.2f, 1.9f, -OD + Wall + 0.01f), 0, new Vector3(0, 0, -1), false, false);
                Clone("REPAIRSHOP/LOD/Office/drag_poster", 0, new Vector3(1.2f, 1.9f, -OD + Wall + 0.01f), 0, new Vector3(0, 0, -1), false, false);
            }
            catch (Exception e) { L("magasin de pieces : affiches " + e.Message); }
        }

        // etagere metallique 2,1 x 0,5 m, montants bleus, lisses orange, 5 tablettes ; les 4 du bas ont chacune 3 cases.
        // c : centre au sol, f : face (vers l'allee).
        static void OsatShelf(Vector3 c, Vector3 f, int wing)
        {
            Vector3 a = Vector3.Cross(Vector3.up, f);   // (le long de l'etagere)
            Func<float, float, float, Vector3> P = (u, v, y) => c + a * u + f * v + Vector3.up * y;
            Action<Vector3, Vector3, string, bool> B = (p, q, key, col) => Box(Vector3.Min(p, q), Vector3.Max(p, q), key, col);
            foreach (float u in new[] { -1.05f, 1.01f })
                foreach (float v in new[] { -0.25f, 0.21f })
                    B(P(u, v, 0f), P(u + 0.04f, v + 0.04f, 2.0f), "os_pysty", false);
            float[] ys = { 0.12f, 0.57f, 1.02f, 1.47f, 1.92f };
            for (int i = 0; i < ys.Length; i++)
            {
                B(P(-1.03f, -0.24f, ys[i]), P(1.03f, 0.24f, ys[i] + 0.04f), "os_taso", true);
                B(P(-1.05f, 0.21f, ys[i] - 0.06f), P(1.05f, 0.25f, ys[i] + 0.04f), "os_palkki", false);
                if (i == ys.Length - 1) break;
                foreach (float u in new[] { -0.7f, 0f, 0.7f })
                    osatSlots.Add(new OsatSlot { Wing = wing, Pos = P(u, 0f, ys[i] + 0.04f), Face = f, Size = new Vector3(0.64f, 0.38f, 0.44f) });
            }
        }

        // ---------------------------------------------------------------- parking (de l'autre cote de la piste, sur la place)
        static void OsatParking()
        {
            Func<float, float, float> gy = (x, z) =>
            {
                RaycastHit? h = GroundHit(osat.transform.TransformPoint(new Vector3(x, 50f, z)));
                return h.HasValue ? osat.transform.InverseTransformPoint(h.Value.point).y : -0.2f;
            };
            // places perpendiculaires a la piste, 2,6 m, 5 m de profondeur, ligne du fond ; peinture seulement (place du rallye)
            float z0 = 14.5f, z1 = 19.5f;
            for (float x = -9.1f; x <= 9.11f; x += 2.6f) Line(x, z0, x, z1, gy);
            Line(-9.1f, z1, 9.1f, z1, gy);
            foreach (float x in new[] { -6f, 6f }) KinoLamp(new Vector3(x, gy(x, z1 + 1.2f), z1 + 1.2f));
        }

        // ---------------------------------------------------------------- portes coulissantes (comme au cinema)
        static void OsatDoors()
        {
            osatLeaves = new Transform[2];
            float zi = OD - 0.1f, th = 0.045f, top = 2.4f, fr = 0.07f;
            for (int k = 0; k < 2; k++)
            {
                float sx = k == 0 ? -1f : 1f;
                var g = new GameObject("osat_ovi" + k);
                g.transform.SetParent(osat.transform, false);
                g.transform.localPosition = new Vector3(0, 0.01f, zi);
                float a = sx > 0 ? 0f : -1.3f, b = sx > 0 ? 1.3f : 0f;
                Box(new Vector3(a, 0, -th / 2), new Vector3(b, fr, th / 2), "ki_alu", false);
                Box(new Vector3(a, top - fr, -th / 2), new Vector3(b, top, th / 2), "ki_alu", false);
                Box(new Vector3(a, 0, -th / 2), new Vector3(a + fr, top, th / 2), "ki_alu", false);
                Box(new Vector3(b - fr, 0, -th / 2), new Vector3(b, top, th / 2), "ki_alu", false);
                GlassX(a + fr, b - fr, fr, top - fr, 0f);
                var keep = root; root = g;
                Flush();
                root = keep;
                var cg = new GameObject("osat_ovi_coll");
                cg.transform.SetParent(g.transform, false);
                cg.transform.localPosition = new Vector3((a + b) / 2, top / 2, 0);
                cg.AddComponent<BoxCollider>().size = new Vector3(b - a, top, 0.08f);
                osatLeaves[k] = g.transform;
            }
        }

        static void TickOsatDoors()
        {
            if (osatLeaves == null) return;
            float t = Time.time;
            Vector3 c = osat.transform.TransformPoint(new Vector3(0, 1f, OD - 0.1f));
            if (Near(Game.PlayerT, c)) osatNear = t;
            else foreach (Avatar av in PlayerSync.Avatars) if (av != null && av.Root != null && av.Root.activeInHierarchy && Near(av.Root.transform, c)) { osatNear = t; break; }
            float target = t - osatNear < 1.2f ? 1f : 0f;
            if (osatOpen == target) return;
            osatOpen = Mathf.MoveTowards(osatOpen, target, Time.deltaTime * 1.6f);
            float e = osatOpen * osatOpen * (3f - 2f * osatOpen);
            osatLeaves[0].localPosition = new Vector3(-e * 1.25f, 0.01f, OD - 0.1f);
            osatLeaves[1].localPosition = new Vector3(e * 1.25f, 0.01f, OD - 0.1f);
        }

        // ---------------------------------------------------------------- lumieres, abri
        static void OsatLights()
        {
            var shop = new Color(1f, 0.95f, 0.86f);
            Func<Vector3, float, float, Color, Light> point = (p, i, r, col) =>
            {
                var g = new GameObject("osat_valo");
                g.transform.SetParent(osat.transform, false); g.transform.localPosition = p;
                var l = g.AddComponent<Light>(); l.type = LightType.Point; l.color = col; l.intensity = i; l.range = r; l.shadows = LightShadows.None;
                return l;
            };
            foreach (float x in new[] { -6.5f, 0f, 6.5f })
                foreach (float z in new[] { -2.5f, 2f }) point(new Vector3(x, 2.9f, z), 1.4f, 8f, shop);
            foreach (float x in new[] { -6f, 6f }) point(new Vector3(x, OH - 0.5f, OD + 1.4f), 1.5f, 8f, shop);   // (vitrines, parvis)
            point(new Vector3(0f, OH + 1.6f, OD + 0.9f), 1.4f, 8f, new Color(1f, 0.55f, 0.45f));                  // (enseigne)
            foreach (Vector3 p in kinoLampSpots) point(p, 1.6f, 14f, new Color(1f, 0.82f, 0.55f));               // (lampadaires du parking)
            kinoLampSpots.Clear();
        }

        // zone abritee du jeu (pas de neige dedans, temperature d'un magasin chauffe) : comme le cinema
        static void OsatShelter()
        {
            GameObject src = FindPath("STORE_AREA/Stuff/LOD/NoRain");
            if (src == null) { L("magasin de pieces : NoRain introuvable"); return; }
            var g = (GameObject)UnityEngine.Object.Instantiate(src);
            g.name = "NoRainOsat";
            g.transform.SetParent(osat.transform, false);
            g.transform.localPosition = Vector3.zero; g.transform.localRotation = Quaternion.identity; g.transform.localScale = Vector3.one;
            var mc = g.GetComponent<MeshCollider>();
            float y = OCeil - 0.05f;
            var m = new Mesh { name = "osat_norain" };
            m.vertices = new[] { new Vector3(-OL, y, -OD), new Vector3(OL, y, -OD), new Vector3(OL, y, OD), new Vector3(-OL, y, OD) };
            m.triangles = new[] { 0, 2, 1, 0, 3, 2, 0, 1, 2, 0, 2, 3 };
            m.RecalculateBounds();
            if (mc != null) { mc.sharedMesh = null; mc.sharedMesh = m; }
            float temp = Config.GetFloat("Osat", "Lampo", 19f);
            foreach (PlayMakerFSM f in g.GetComponents<PlayMakerFSM>())
            {
                if (f.FsmName != "Data") continue;
                HutongGames.PlayMaker.FsmFloat tv = f.FsmVariables.FindFsmFloat("Temp");
                if (tv != null) tv.Value = temp;
            }
            g.SetActive(true);
        }

        static bool InOsat(Transform t)
        {
            if (osat == null || t == null) return false;
            Vector3 lp = osat.transform.InverseTransformPoint(t.position);
            return Mathf.Abs(lp.x) < OL - 0.2f && Mathf.Abs(lp.z) < OD - 0.2f && lp.y > -0.6f && lp.y < OCeil;
        }
    }
}
