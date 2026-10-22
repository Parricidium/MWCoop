using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace MWCoop
{
    // Cinema KINO TAHTI (demande de JD, 22/10 ; plan de JD sur une vue de dessus) : a l'est du musee, entre la route du musee
    // et le fosse, avant la route de la course sur glace. Un cinema de petite ville finlandaise des annees 80-90 : salles en
    // brique, hall vitre au milieu sous un auvent lumineux, enseigne sur le toit, parking devant entoure d'un muret.
    // Construit en code comme le musee (memes outils : Box, Quad, MWall, Flush), dans son propre repere : x = longueur (-26 a
    // +26 m), z = profondeur (arriere -9, facade +9, parking jusqu'a +22), y = 0 au plancher du hall.
    //  - salle 1 a l'ouest (x < -11), salle 2 a l'est (x > 11) : ecran sur le pignon, 7 rangs de 18 fauteuils en gradins qui
    //    descendent vers l'ecran, entree par le fond du hall (portes au fond des cloisons, comme sur le plan de JD) ;
    //  - hall (-11 < x < 11) : comptoir billets et confiserie au fond (vendeur : KinoKassa), cabines de projection contre les
    //    cloisons (KinoSali) ;
    //  - parking : places marquees le long de la facade, lampadaires, muret avec entree (ouest, route du musee) et sortie (est,
    //    route de la course sur glace).
    // [Kino] Paikka=x,z,cap[,plancher|auto] : place, cap et hauteur du plancher (les memes chez tous).
    public static partial class Kauppa
    {
        const float KL = 26f, KD = 9f, KPart = 11f, KLobH = 4.4f, KLobCeil = 3.9f, KSalH = 7.0f, KSalCeil = 6.6f, KFront = 0f, KBack = 0.9f, KPark = 22f;
        const int KRows = 7, KSeats = 18;

        static GameObject kino;
        static float kinoDrop; static int kinoSteps;   // (marches du trottoir vers le parking)
        static Transform[] kinoLeaves;
        static float kinoOpen, kinoNear;
        static readonly List<Light> kinoSalLights = new List<Light>();

        public static bool KinoOn { get { return Ajouts.Kino && Config.GetInt("Kino", "Active", 1) != 0; } }

        static void ResetKino() { OnLevelLoadedKino(); kino = null; kinoDrives = null; kinoLeaves = null; kinoOpen = 0f; kinoNear = -10f; kinoSalLights.Clear(); ResetKinoKassa(); ResetKinoSali(); }

        // place et cap du cinema (aussi pour le defrichage, Clearing)
        public static void KinoFrame(out Vector3 c, out float yaw, out string floor)
        {
            string s = Config.Get("Kino", "Paikka", "-1528.2,1053.4,24.3,2.25");   // (plancher fixe, le meme chez tous : sol 0,76..2,08 sous l'emprise)
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            string[] a = s.Split(',');
            c = new Vector3(float.Parse(a[0], ci), 0f, float.Parse(a[1], ci));
            yaw = float.Parse(a[2], ci);
            floor = a.Length > 3 ? a[3].Trim() : "auto";
        }

        // terrain a defricher : le batiment, le parking et ses deux entrees (rectangle du repere, monde x, z)
        public static Vector2[] KinoPlot()
        {
            Vector3 c; float yaw; string fl;
            KinoFrame(out c, out yaw, out fl);
            Quaternion r = Quaternion.Euler(0, yaw, 0);
            var pts = new[] { new Vector3(-KL - 3f, 0, -KD - 5f), new Vector3(KL + 3f, 0, -KD - 5f),   // (au sud : les arbres du fosse dont le feuillage entrait dans les salles)
                               new Vector3(KL + 3f, 0, KPark + 15f), new Vector3(-KL - 3f, 0, KPark + 15f) };   // (au nord : jusqu'aux routes)
            return pts.Select(p => { Vector3 w = c + r * p; return new Vector2(w.x, w.z); }).ToArray();
        }

        // plateforme de terrassement (format de [Boutique] Tasanne) : batiment + parking
        public static string KinoPad()
        {
            Vector3 c; float yaw; string fl;
            KinoFrame(out c, out yaw, out fl);
            Vector3 m = c + Quaternion.Euler(0, yaw, 0) * new Vector3(0, 0, (KPark + 0.5f - KD) / 2f);
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            return string.Format(ci, "{0:F1},{1:F1},{2:F1},{3:F1},{4:F1}", m.x, m.z, yaw, KL + 1.5f, (KPark + 0.5f + KD) / 2f);
        }

        static void BuildKino()
        {
            Vector3 place; float yaw; string fl;
            KinoFrame(out place, out yaw, out fl);
            Quaternion rot = Quaternion.Euler(0, yaw, 0);
            KinoMaterials();
            // sol sous le batiment (le plancher : plateforme du defrichage + 0,2, sinon le plus haut sous l'emprise + 0,3)
            float gMax = float.MinValue, gMin = float.MaxValue;
            for (float x = -KL; x <= KL; x += 2f)
                for (float z = -KD; z <= KD; z += 2f)
                {
                    RaycastHit? h = GroundHit(place + rot * new Vector3(x, 0, z) + Vector3.up * 200f);
                    if (!h.HasValue) continue;
                    gMax = Mathf.Max(gMax, h.Value.point.y); gMin = Mathf.Min(gMin, h.Value.point.y);
                }
            if (gMax == float.MinValue) gMax = gMin = 3f;
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            // (le plancher au-dessus de tout le sol sous l'emprise : sinon la neige du terrain apparait dans les salles)
            float padY = !float.IsNaN(Clearing.KinoPadY) ? Clearing.KinoPadY : gMin;
            float floorY = fl != "auto" ? float.Parse(fl, ci) : Mathf.Max(padY + 0.2f, gMax + 0.15f);
            kinoDrop = Mathf.Max(0.06f, floorY - padY);
            kinoSteps = Mathf.Max(0, Mathf.CeilToInt((kinoDrop - 0.25f) / 0.17f));
            L(string.Format("cinema : sol {0:F2}..{1:F2}, plateforme {2}, plancher a {3:F2}", gMin, gMax, float.IsNaN(Clearing.KinoPadY) ? "-" : Clearing.KinoPadY.ToString("F2"), floorY));
            float down = floorY - Mathf.Min(gMin, floorY - 1.6f) + 0.5f;

            kino = new GameObject("MWCoop KINO TAHTI");
            kino.transform.position = new Vector3(place.x, floorY, place.z);
            kino.transform.rotation = rot;
            var keepRoot = root; root = kino;
            try
            {
                KinoParking();   // (avant les collisions du batiment : le parking suit le sol)
                Flush();
                KinoShell(down);
                KinoFront();
                KinoRoofs();
                foreach (float s in new[] { -1f, 1f }) KinoSalle(s);
                KinoLobby();
                Flush();
                KinoDoors();
                KinoLights();
                KinoShelter();
                KinoVendor();
                KinoSaliSetup();
            }
            finally { root = keepRoot; }
            L("cinema construit en " + kino.transform.position.ToString("F1"));
        }

        static void KinoMaterials()
        {
            if (!mats.ContainsKey("lista")) Mat("lista", "wood_panel20", 1.2f);
            if (!mats.ContainsKey("lumi")) Mat("lumi", "SNOW", 3f);
            if (!mats.ContainsKey("metalli")) Mat("metalli", "metal_galvanized", 1f, new Color(0.9f, 0.9f, 0.88f));
            if (!mats.ContainsKey("lasi")) Mat("lasi", "psk_glass", 1f);
            if (!mats.ContainsKey("tiski")) Mat("tiski", "wood12", 1.0f);
            Mat("ki_tiili", "wall_brick2", 2.4f, new Color(0.95f, 0.78f, 0.7f));     // brique rouge-brun
            Mat("ki_rappaus", "plaster", 2.0f, new Color(0.96f, 0.95f, 0.92f));      // enduit blanc
            Mat("ki_aulaseina", "paint_black_matte", 1f, new Color(0.93f, 0.86f, 0.74f));   // murs du hall : peinture creme, unie
            Mat("ki_paneeli", "wood_panel4", 1.4f, new Color(0.85f, 0.7f, 0.55f));    // lambris du hall
            Mat("ki_kasvi", "plaster", 0.5f, new Color(0.18f, 0.42f, 0.16f));        // plantes vertes
            Mat("ki_punainen", "plaster", 2.0f, new Color(0.72f, 0.12f, 0.12f));     // bandeau rouge
            Mat("ki_betoni", "concrete_slabs", 3.0f, new Color(0.85f, 0.85f, 0.83f));
            // (couleurs unies, sans texture : les textures du jeu se repetaient partout -- JD, 22/10)
            Mat("ki_asfaltti", "paint_black_matte", 1f, new Color(0.2f, 0.2f, 0.21f));   // asphalte
            Mat("ki_viiva", "paint_black_matte", 1f, new Color(0.92f, 0.92f, 0.88f));    // lignes du parking
            Mat("ki_katto", "concrete_slabs", 3.0f, new Color(0.45f, 0.45f, 0.47f));
            Mat("ki_alu", "metal_galvanized", 1f, new Color(0.18f, 0.18f, 0.2f));    // menuiseries noires
            Mat("ki_kulta", "metal_galvanized", 1f, new Color(0.95f, 0.75f, 0.35f)); // laiton
            Mat("ki_matto", "paint_black_matte", 1f, new Color(0.42f, 0.05f, 0.08f));   // moquette bordeaux (unie)
            Mat("ki_aula", "paint_black_matte", 1f, new Color(0.13f, 0.17f, 0.36f));    // moquette du hall, bleue (unie)
            Mat("ki_kyltti_in", "paint_black_matte", 1f, Color.white);
            OwnMat("ki_sisaan", "kino-sisaan.png", true);
            OwnMat("ki_ulosajo", "kino-ulosajo.png", true);
            Mat("ki_seina", "fabric_cotton", 1.2f, new Color(0.16f, 0.14f, 0.22f));   // tenture murale sombre
            Mat("ki_katossa", "plaster", 2.0f, new Color(0.12f, 0.12f, 0.14f));       // plafond des salles
            Mat("ki_sisakatto", "tiles4", 1.2f, new Color(0.95f, 0.95f, 0.95f));     // faux plafond du hall
            Mat("ki_istuin", "fabric_cotton", 0.5f, new Color(0.62f, 0.06f, 0.08f));  // velours rouge des fauteuils
            Mat("ki_musta", "paint_black_matte", 1f, new Color(0.07f, 0.07f, 0.08f));
            Mat("ki_verho", "fabric_cotton", 0.8f, new Color(0.5f, 0.04f, 0.06f));         // rideaux de scene
            Mat("ki_valkoinen", "plaster", 1f, new Color(1f, 1f, 1f));
            Mat("ki_popcorn", "plaster", 0.5f, new Color(1f, 0.86f, 0.42f));
            Mat("ki_sininen", "plaster", 1f, new Color(0.15f, 0.3f, 0.7f));
            Mat("ki_projektori", "metal_galvanized", 1f, new Color(0.22f, 0.3f, 0.26f));   // vert machine
            OwnMat("ki_kyltti", "kino-kyltti.png", true);
            OwnMat("ki_valkokangas", "kino-valkokangas.png", true);
            OwnMat("ki_hinnasto", "kino-hinnasto.png", true);
            OwnMat("ki_sali1", "kino-sali1.png", true);
            OwnMat("ki_sali2", "kino-sali2.png", true);
            OwnMat("ki_konehuone", "kino-konehuone.png");
            OwnMat("ki_ulos", "kino-ulos.png", true);
            for (int i = 1; i <= 6; i++) OwnMat("ki_juliste" + i, "kino-juliste" + i + ".png", true);
        }

        // ---------------------------------------------------------------- coque
        static void KinoShell(float down)
        {
            // soubassement (le batiment entier) et dalle du hall
            Box(new Vector3(-KL - 0.1f, -down, -KD - 0.1f), new Vector3(KL + 0.1f, -0.02f, KD + 0.1f), "ki_betoni", "ki_betoni", null, null, "ki_betoni", "ki_betoni", false);   // (sans collision : les gradins descendent dedans)
            Box(new Vector3(-KPart + Wall / 2, -0.15f, -KD + Wall), new Vector3(KPart - Wall / 2, 0f, KD - Wall), null, null, "ki_aula", null, null, null);
            // bandeau de beton au pied des murs
            Box(new Vector3(-KL - 0.05f, -0.4f, KD), new Vector3(KL + 0.05f, 0.35f, KD + 0.05f), "ki_betoni", "ki_betoni", "ki_betoni", null, "ki_betoni", null, false);
            Box(new Vector3(-KL - 0.05f, -0.4f, -KD - 0.05f), new Vector3(KL + 0.05f, 0.35f, -KD), "ki_betoni", "ki_betoni", "ki_betoni", null, null, "ki_betoni", false);
            foreach (float s in new[] { -1f, 1f })
                Box(new Vector3(s > 0 ? KL : -KL - 0.05f, -0.4f, -KD), new Vector3(s > 0 ? KL + 0.05f : -KL, 0.35f, KD), "ki_betoni", "ki_betoni", "ki_betoni", null, null, null, false);

            foreach (float s in new[] { -1f, 1f })
            {
                float u0 = s > 0 ? KPart : -KL, u1 = s > 0 ? KL : -KPart;
                // salles : murs de facade et arriere (brique dehors, tenture dedans), du bas des gradins au toit ; une issue de
                // secours sur la facade, pres de l'ecran
                var exit = new Hole(s * 23.6f - 0.6f, s * 23.6f + 0.6f, KFront, KFront + 2.1f);
                MWall(true, KD, -1, u0, u1, KFront - 0.1f, KSalH, new[] { exit }, "ki_tiili", "ki_seina");
                MWall(true, -KD, 1, u0, u1, KFront - 0.1f, KSalH, new Hole[0], "ki_tiili", "ki_seina");
                // pignon (derriere l'ecran)
                MWall(false, s * KL, -s, -KD + Wall, KD - Wall, KFront - 0.1f, KSalH, new Hole[0], "ki_tiili", "ki_seina");
                // issue de secours : porte metallique fermee, enseigne verte au-dessus
                float ez = KD - Wall * 0.5f;
                Box(new Vector3(exit.U0 + 0.04f, KFront, ez - 0.03f), new Vector3(exit.U1 - 0.04f, KFront + 2.06f, ez + 0.03f), "ki_alu");
                Box(new Vector3(exit.U1 - 0.2f, KFront + 0.95f, ez + 0.03f), new Vector3(exit.U1 - 0.08f, KFront + 1.0f, ez + 0.09f), "metalli", false);
                Plane("ki_ulos", new Vector3(s * 23.6f, KFront + 2.35f, KD - Wall - 0.01f), 0.5f, 0.2f, Vector3.back);
                // escalier exterieur de l'issue (le sol dehors est au niveau du parking)
                // cloison hall / salle : porte au fond (plan de JD), fenetre de projection au milieu ; au-dessus du toit du hall,
                // mur exterieur en brique
                var door = new Hole(-KD + 0.35f, -KD + 1.85f, KBack, KBack + 2.2f);   // (en haut des marches du hall)
                var port = new Hole(-0.35f, 0.35f, 2.2f, 2.6f);   // (objectif du projecteur a 2,4 m : KinoSali)
                // porte de la cabine de projection, cote salle (JD, 22/10 : depuis le hall, n'importe qui changeait le film) ;
                // seuil au niveau de l'allee du fond de la salle
                var booth = new Hole(0.75f, 1.65f, KBack, KBack + 2.1f);
                float lobbyFace = s * (KPart - Wall / 2);
                MWall(false, lobbyFace, s, -KD + Wall, KD - Wall, 0f, KLobH, new[] { door, port, booth }, "ki_aulaseina", "ki_seina");
                Plane("ki_konehuone", new Vector3(s * (KPart + Wall / 2 + 0.012f), KBack + 2.35f, (booth.U0 + booth.U1) / 2f), 1.0f, 0.25f, s > 0 ? Vector3.right : Vector3.left);
                MWall(false, lobbyFace, s, -KD + Wall, KD - Wall, KLobH, KSalH, new Hole[0], "ki_tiili", "ki_seina");
                GlassZ(s * KPart, port.U0, port.U1, port.Y0, port.Y1);
                // linteau de la porte de salle : enseigne SALI 1 / SALI 2
                Plane(s < 0 ? "ki_sali1" : "ki_sali2", new Vector3(lobbyFace - s * 0.012f, KBack + 2.5f, (door.U0 + door.U1) / 2f), 1.2f, 0.4f, s > 0 ? Vector3.left : Vector3.right);
                Box(new Vector3(Mathf.Min(lobbyFace, lobbyFace - s * 0.08f), KBack + 2.2f, door.U0 - 0.06f), new Vector3(Mathf.Max(lobbyFace, lobbyFace - s * 0.08f), KBack + 2.28f, door.U1 + 0.06f), "ki_kulta", false);
                // palier et marches dans le hall jusqu'a la porte (le fond de la salle est 0,9 m plus haut que le hall)
                float lx = s * 9.3f;
                Box(new Vector3(Mathf.Min(lx, lobbyFace), 0f, -KD + Wall), new Vector3(Mathf.Max(lx, lobbyFace), KBack, -KD + 2.2f), "ki_betoni", "ki_betoni", "ki_matto", null, "ki_betoni", null);
                for (int k = 1; k <= 4; k++)
                {
                    float xa = lx - s * 0.3f * k, xb = lx - s * 0.3f * (k - 1), top = KBack - 0.18f * k;
                    Box(new Vector3(Mathf.Min(xa, xb), 0f, -KD + Wall), new Vector3(Mathf.Max(xa, xb), top, -KD + 2.2f), "ki_betoni", "ki_betoni", "ki_matto", null, "ki_betoni", null);
                    Box(new Vector3(Mathf.Min(xa, xb), top - 0.02f, -KD + Wall), new Vector3(Mathf.Min(xa, xb) + 0.04f, top + 0.005f, -KD + 2.2f), "ki_kulta", false);
                }
                // garde-corps du palier (cote hall), main courante en laiton
                Box(new Vector3(Mathf.Min(lx, lobbyFace), KBack, -KD + 2.15f), new Vector3(Mathf.Max(lx, lobbyFace), KBack + 0.95f, -KD + 2.2f), "ki_alu", false);
                Box(new Vector3(Mathf.Min(lx, lobbyFace), KBack + 0.95f, -KD + 2.12f), new Vector3(Mathf.Max(lx, lobbyFace), KBack + 1.0f, -KD + 2.23f), "ki_kulta", false);
            }
            // hall : facade vitree (z = +9) et mur du fond
            MWall(true, KD, -1, -KPart, KPart, 0f, KLobH, new[] { new Hole(-9f, 9f, 0f, 3.3f) }, "ki_rappaus", "ki_aulaseina");
            MWall(true, -KD, 1, -KPart, KPart, 0f, KLobH, new Hole[0], "ki_tiili", "ki_aulaseina");
            // bandeau rouge sur l'enduit de la facade du hall
            Box(new Vector3(-KPart, 3.55f, KD), new Vector3(KPart, 3.75f, KD + 0.03f), "ki_punainen", false);
            // corniche blanche en haut des murs de brique
            foreach (float s in new[] { -1f, 1f })
            {
                float a = s > 0 ? KPart : -KL - 0.06f, b = s > 0 ? KL + 0.06f : -KPart;
                Box(new Vector3(a, KSalH - 0.35f, KD), new Vector3(b, KSalH, KD + 0.06f), "ki_rappaus", false);
                Box(new Vector3(a, KSalH - 0.35f, -KD - 0.06f), new Vector3(b, KSalH, -KD), "ki_rappaus", false);
                float px = s * (KL + 0.03f);
                Box(new Vector3(px - 0.03f, KSalH - 0.35f, -KD), new Vector3(px + 0.03f, KSalH, KD), "ki_rappaus", false);
            }
        }

        // vitre plane (deux faces) dans le plan x
        static void GlassZ(float x, float z0, float z1, float y0, float y1)
        {
            Quad("lasi", new Vector3(x, y0, z1), new Vector3(x, y0, z0), new Vector3(x, y1, z0), new Vector3(x, y1, z1));
            Quad("lasi", new Vector3(x, y0, z0), new Vector3(x, y0, z1), new Vector3(x, y1, z1), new Vector3(x, y1, z0));
        }

        // ---------------------------------------------------------------- facade, auvent, enseigne
        static void KinoFront()
        {
            float z = KD, zg = KD - 0.1f;
            // mur-rideau du hall : montants noirs tous les 1,5 m, traverse a 2,4 m ; portes coulissantes au milieu (-1,25..1,25)
            for (float x = -9f; x <= 9.01f; x += 1.5f)
            {
                if (Mathf.Abs(x) < 1.3f) continue;
                Box(new Vector3(x - 0.05f, 0f, zg - 0.07f), new Vector3(x + 0.05f, 3.3f, z), "ki_alu", false);
            }
            Box(new Vector3(-9f, 2.42f, zg - 0.07f), new Vector3(9f, 2.52f, z), "ki_alu", false);
            Box(new Vector3(-9f, 3.2f, zg - 0.07f), new Vector3(9f, 3.3f, z), "ki_alu", false);
            Box(new Vector3(-9f, 0f, zg - 0.07f), new Vector3(9f, 0.1f, z), "ki_alu", false);
            foreach (float sx in new[] { -1f, 1f })
            {
                float x0 = sx > 0 ? 1.3f : -9f, x1 = sx > 0 ? 9f : -1.3f;
                GlassX(x0, x1, 0.1f, 2.42f, zg);
                cols.Add(new Bounds(new Vector3((x0 + x1) / 2, 1.3f, zg), new Vector3(x1 - x0, 2.6f, 0.16f)));
                Box(new Vector3(sx * 1.25f - 0.05f, 0f, zg - 0.07f), new Vector3(sx * 1.25f + 0.05f, 3.3f, z), "ki_alu", false);
            }
            GlassX(-9f, 9f, 2.52f, 3.2f, zg);
            cols.Add(new Bounds(new Vector3(0, 2.85f, zg), new Vector3(18f, 0.7f, 0.16f)));

            // auvent (marquise) : dalle blanche, chant rouge, ampoules dessous, neige dessus
            float cz0 = z, cz1 = z + 3.6f, cy = 3.35f;
            Box(new Vector3(-7f, cy, cz0), new Vector3(7f, cy + 0.35f, cz1), "ki_punainen", "ki_punainen", null, "ki_rappaus", "ki_punainen", null, false);
            Box(new Vector3(-6.95f, cy + 0.35f, cz0), new Vector3(6.95f, cy + 0.45f, cz1 - 0.05f), "lumi", false);
            for (float x = -6.4f; x <= 6.41f; x += 0.8f)
                for (float zz = cz0 + 0.5f; zz < cz1 - 0.2f; zz += 0.8f)
                    Box(new Vector3(x - 0.06f, cy - 0.04f, zz - 0.06f), new Vector3(x + 0.06f, cy, zz + 0.06f), "ki_valkoinen", false);
            // tirants vers la facade
            foreach (float x in new[] { -5.5f, 5.5f })
                Beam(new Vector3(x, cy + 0.4f, cz1 - 0.3f), new Vector3(x, KLobH - 0.2f, z + 0.05f), 0.06f, 0.06f, "ki_alu");

            // enseigne sur le toit du hall : KINO TAHTI (texture lumineuse), cadre noir, pieds
            float sw = 11f, sh = sw * 512f / 2048f, sy0 = KLobH + 0.6f, sz = KD - 0.6f;
            Box(new Vector3(-sw / 2 - 0.1f, sy0 - 0.1f, sz - 0.15f), new Vector3(sw / 2 + 0.1f, sy0 + sh + 0.1f, sz), "ki_alu", false);
            Quad("ki_kyltti", new Vector3(sw / 2, sy0, sz + 0.003f), new Vector3(-sw / 2, sy0, sz + 0.003f), new Vector3(-sw / 2, sy0 + sh, sz + 0.003f), new Vector3(sw / 2, sy0 + sh, sz + 0.003f),
                 new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) });
            foreach (float x in new[] { -4.5f, -1.5f, 1.5f, 4.5f })
                Box(new Vector3(x - 0.06f, KLobH + 0.2f, sz - 0.4f), new Vector3(x + 0.06f, sy0, sz - 0.28f), "ki_alu", false);

            // vitrines d'affiches sur la facade des salles (3 par salle), eclairees
            int n = 1;
            foreach (float s in new[] { -1f, 1f })
                foreach (float x in new[] { 13.6f, 17.2f, 20.8f })
                {
                    float cx = s * x, w = 1.1f, h = 1.55f, y0 = 0.85f, fz = KD + 0.12f;
                    Box(new Vector3(cx - w / 2 - 0.08f, y0 - 0.08f, KD), new Vector3(cx + w / 2 + 0.08f, y0 + h + 0.08f, fz), "ki_alu", false);
                    Quad("ki_juliste" + n, new Vector3(cx + w / 2, y0, fz + 0.002f), new Vector3(cx - w / 2, y0, fz + 0.002f), new Vector3(cx - w / 2, y0 + h, fz + 0.002f), new Vector3(cx + w / 2, y0 + h, fz + 0.002f),
                         new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) });
                    GlassX(cx - w / 2, cx + w / 2, y0, y0 + h, fz + 0.02f);
                    n++;
                }

            // trottoir le long de la facade
            Box(new Vector3(-KL - 0.5f, -kinoDrop - 0.5f, KD), new Vector3(KL + 0.5f, -0.06f, KD + 1.8f), "ki_betoni");
            // marches jusqu'au parking (sur toute la facade)
            float rise = kinoSteps > 0 ? (kinoDrop - 0.06f) / (kinoSteps + 1f) : 0f;
            for (int k = 1; k <= kinoSteps; k++)
                Box(new Vector3(-KL - 0.5f, -kinoDrop - 0.5f, KD + 1.8f + (k - 1) * 0.32f), new Vector3(KL + 0.5f, -0.06f - k * rise, KD + 1.8f + k * 0.32f), "ki_betoni");
        }

        static void KinoRoofs()
        {
            // hall : dalle, acrotere, neige
            Box(new Vector3(-KPart, KLobH, -KD), new Vector3(KPart, KLobH + 0.25f, KD), "ki_rappaus", "ki_rappaus", "ki_katto", "ki_sisakatto", "ki_rappaus", "ki_rappaus");
            Box(new Vector3(-KPart + 0.3f, KLobH + 0.25f, -KD + 0.3f), new Vector3(KPart - 0.3f, KLobH + 0.33f, KD - 0.3f), "lumi", false);
            foreach (float s in new[] { -1f, 1f })
            {
                float a = s > 0 ? KPart : -KL, b = s > 0 ? KL : -KPart;
                Box(new Vector3(a, KSalH, -KD), new Vector3(b, KSalH + 0.3f, KD), "ki_rappaus", "ki_rappaus", "ki_katto", null, "ki_rappaus", "ki_rappaus");
                Box(new Vector3(a + 0.3f, KSalH + 0.3f, -KD + 0.3f), new Vector3(b - 0.3f, KSalH + 0.4f, KD - 0.3f), "lumi", false);
            }
            // faux plafond du hall
            Quad("ki_sisakatto", new Vector3(-KPart + Wall / 2, KLobCeil, KD - Wall), new Vector3(KPart - Wall / 2, KLobCeil, KD - Wall), new Vector3(KPart - Wall / 2, KLobCeil, -KD + Wall), new Vector3(-KPart + Wall / 2, KLobCeil, -KD + Wall));
        }

        // ---------------------------------------------------------------- salle (s = -1 : salle 1, ouest ; +1 : salle 2, est)
        // gradins : rang k (0 = le plus pres de l'ecran) de x = 20,6 - (k+1)*1,15 a 20,6 - k*1,15 (en |x|), plancher a
        // (k+1)*0,9/7 : le premier rang juste au-dessus du sol devant l'ecran (niveau du hall), le dernier au niveau de l'allee
        // du fond et des portes (0,9 m).
        static float RowFloor(int k) { return KFront + (k + 1) * (KBack - KFront) / KRows; }
        static float RowNear(int k) { return 20.6f - k * 1.15f; }
        static float RowFar(int k) { return 20.6f - (k + 1) * 1.15f; }

        static void KinoSalle(float s)
        {
            float zi0 = -KD + Wall, zi1 = KD - Wall, xin = KPart + Wall / 2, xout = KL - Wall;
            Func<float, float, float, float, Vector3[]> span = (a, b, y0, y1) => new[] { new Vector3(Mathf.Min(s * a, s * b), y0, zi0), new Vector3(Mathf.Max(s * a, s * b), y1, zi1) };
            // devant l'ecran : plancher bas
            Vector3[] f = span(RowNear(0), xout, KFront - 0.2f, KFront);
            Box(f[0], f[1], null, null, "ki_matto", null, null, null);
            // gradins
            for (int k = 0; k < KRows; k++)
            {
                Vector3[] g = span(RowFar(k), RowNear(k), KFront - 0.2f, RowFloor(k));
                // contremarche (vers l'ecran) noire, dessus en moquette
                Box(g[0], g[1], s > 0 ? "ki_musta" : null, s > 0 ? null : "ki_musta", "ki_matto", null, null, null);
            }
            // allee du fond, au niveau du hall
            Vector3[] back = span(xin, RowFar(KRows - 1), -0.2f, KBack);
            Box(back[0], back[1], null, null, "ki_matto", null, null, null);
            // petite scene devant l'ecran
            Vector3[] st = span(24.4f, xout, KFront, KFront + 0.35f);
            Box(st[0], st[1], s > 0 ? "ki_musta" : "ki_matto", s > 0 ? "ki_matto" : "ki_musta", "ki_matto", null, "ki_musta", "ki_musta");
            // plafond sombre
            Quad("ki_katossa", new Vector3(s * xin, KSalCeil, s > 0 ? zi1 : zi0), new Vector3(s * xout, KSalCeil, s > 0 ? zi1 : zi0), new Vector3(s * xout, KSalCeil, s > 0 ? zi0 : zi1), new Vector3(s * xin, KSalCeil, s > 0 ? zi0 : zi1));

            // ecran : cache noir, toile blanche (la projection viendra dessus : KinoSali), rideaux rouges plisses
            float sx = s * (xout - 0.02f), sz0 = -4.6f, sz1 = 4.6f, sy0 = 1.3f, sy1 = 5.3f;
            Vector3 nrm = s > 0 ? Vector3.left : Vector3.right;
            Plane("ki_musta", new Vector3(sx, (sy0 + sy1) / 2f, 0f), 10.4f, sy1 - sy0 + 0.8f, nrm);
            // (la toile elle-meme : KinoSali, une surface a part ou passe le film)
            foreach (float side in new[] { -1f, 1f })
                for (int i = 0; i < 9; i++)
                {
                    float zc = side * (5.25f + i * 0.17f), d = (i % 2 == 0) ? 0.12f : 0.05f;
                    float x0 = sx - s * (0.06f + d), x1 = sx - s * 0.04f;
                    Box(new Vector3(Mathf.Min(x0, x1), KFront + 0.35f, zc - 0.09f), new Vector3(Mathf.Max(x0, x1), KSalCeil - 0.05f, zc + 0.09f), "ki_verho", false);
                }
            // lambrequin au-dessus de l'ecran
            Box(new Vector3(Mathf.Min(sx, sx - s * 0.25f), sy1 + 0.4f, -6.8f), new Vector3(Mathf.Max(sx, sx - s * 0.25f), KSalCeil, 6.8f), "ki_verho", false);

            // fauteuils : 7 rangs de 18, face a l'ecran ; colliders de place (KinoSali)
            for (int k = 0; k < KRows; k++)
            {
                float y = RowFloor(k), a = RowFar(k);   // (dos du fauteuil cote a, l'ecran cote RowNear)
                for (int i = 0; i < KSeats; i++)
                {
                    float zc = (i - (KSeats - 1) / 2f) * 0.6f;
                    KinoSeat(s, a, y, zc);
                    kinoSeatSpots.Add(new SeatSpot { Salle = s < 0 ? 1 : 2, Row = k, Index = i, Pos = new Vector3(s * (a + 0.5f), y + 0.46f, zc), Yaw = s > 0 ? 90f : -90f });
                }
                // accoudoir de bout de rang
                foreach (float e in new[] { -1f, 1f })
                {
                    float ze = e * (KSeats / 2f * 0.6f + 0.03f);
                    Box(new Vector3(Mathf.Min(s * (a + 0.15f), s * (a + 0.7f)), y, ze - 0.03f), new Vector3(Mathf.Max(s * (a + 0.15f), s * (a + 0.7f)), y + 0.66f, ze + 0.03f), "ki_musta", false);
                }
            }

            // enceintes du son d'ambiance sur les murs lateraux et au fond (decor : le son, KinoSali)
            foreach (float wz in new[] { zi0 + 0.1f, zi1 - 0.1f })
                foreach (float x in new[] { 15.5f, 20f, 24.2f })
                {
                    float d = wz < 0 ? 0.25f : -0.25f;
                    Box(new Vector3(s * x - 0.28f, 4.0f, Mathf.Min(wz, wz + d)), new Vector3(s * x + 0.28f, 4.75f, Mathf.Max(wz, wz + d)), "ki_musta", false);
                }
            foreach (float z in new[] { -4f, 0f, 4f })
                Box(new Vector3(Mathf.Min(s * (xin + 0.05f), s * (xin + 0.3f)), 4.6f, z - 0.28f), new Vector3(Mathf.Max(s * (xin + 0.05f), s * (xin + 0.3f)), 5.35f, z + 0.28f), "ki_musta", false);
            // appliques murales (la lumiere : KinoLights), bande lumineuse le long des marches
            foreach (float wz in new[] { zi0 + 0.06f, zi1 - 0.06f })
                foreach (float x in new[] { 14f, 18f, 22f })
                    Box(new Vector3(s * x - 0.2f, 3.1f, wz - 0.06f), new Vector3(s * x + 0.2f, 3.4f, wz + 0.06f), "ki_kulta", false);
            // enseigne de sortie au-dessus de la porte, cote salle
            Plane("ki_ulos", new Vector3(s * (xin + 0.012f), KBack + 2.45f, -KD + 1.1f), 0.5f, 0.2f, s > 0 ? Vector3.right : Vector3.left);
        }

        // un fauteuil de cinema : pied, assise relevable, dossier, accoudoir a droite ; dos vers a (|x|), face a l'ecran
        static void KinoSeat(float s, float a, float y, float zc)
        {
            Func<float, float, float> X0 = (u, v) => Mathf.Min(s * u, s * v), X1 = (u, v) => Mathf.Max(s * u, s * v);
            const float hw = 0.26f;
            // dossier (rembourre, un peu incline : deux boites)
            Box(new Vector3(X0(a + 0.1f, a + 0.24f), y + 0.42f, zc - hw), new Vector3(X1(a + 0.1f, a + 0.24f), y + 0.78f, zc + hw), "ki_istuin", false);
            Box(new Vector3(X0(a + 0.06f, a + 0.2f), y + 0.78f, zc - hw), new Vector3(X1(a + 0.06f, a + 0.2f), y + 1.02f, zc + hw), "ki_istuin", false);
            // coque arriere noire
            Box(new Vector3(X0(a + 0.04f, a + 0.07f), y + 0.4f, zc - hw), new Vector3(X1(a + 0.04f, a + 0.07f), y + 1.0f, zc + hw), "ki_musta", false);
            // assise
            Box(new Vector3(X0(a + 0.22f, a + 0.68f), y + 0.36f, zc - hw + 0.02f), new Vector3(X1(a + 0.22f, a + 0.68f), y + 0.47f, zc + hw - 0.02f), "ki_istuin", false);
            // pied central
            Box(new Vector3(X0(a + 0.3f, a + 0.42f), y, zc - 0.05f), new Vector3(X1(a + 0.3f, a + 0.42f), y + 0.36f, zc + 0.05f), "ki_musta", false);
            // accoudoir (cote +z) : flanc noir, dessus en laiton... non, en bois
            Box(new Vector3(X0(a + 0.15f, a + 0.7f), y + 0.2f, zc + 0.27f), new Vector3(X1(a + 0.15f, a + 0.7f), y + 0.6f, zc + 0.33f), "ki_musta", false);
            Box(new Vector3(X0(a + 0.12f, a + 0.72f), y + 0.6f, zc + 0.26f), new Vector3(X1(a + 0.12f, a + 0.72f), y + 0.66f, zc + 0.34f), "tiski", false);
        }

        // ---------------------------------------------------------------- hall
        static void KinoLobby()
        {
            // comptoir billets + confiserie au fond (le vendeur derriere, KinoKassa) : facade en bois, dessus noir, bandeau rouge
            float c0 = -4.6f, c1 = 4.6f, cz0 = -5.6f, cz1 = -4.8f, ch = 1.05f;
            Box(new Vector3(c0, 0f, cz0), new Vector3(c1, ch - 0.05f, cz1), "tiski", "tiski", null, null, "tiski", "ki_musta");
            Box(new Vector3(c0 - 0.05f, ch - 0.05f, cz0 - 0.05f), new Vector3(c1 + 0.05f, ch, cz1 + 0.12f), "ki_musta");
            Box(new Vector3(c0, 0.75f, cz1), new Vector3(c1, 0.85f, cz1 + 0.02f), "ki_punainen", false);
            // tableau des prix au-dessus du vendeur, contre le mur du fond
            Plane("ki_hinnasto", new Vector3(0f, 2.75f, -KD + Wall + 0.015f), 3.6f, 1.8f, Vector3.forward);
            Box(new Vector3(-1.9f, 1.8f, -KD + Wall), new Vector3(1.9f, 3.7f, -KD + Wall + 0.012f), "ki_alu", false);
            // etageres a bonbons (irtokarkit) contre le mur du fond, a gauche du tableau
            for (int lvl = 0; lvl < 3; lvl++)
            {
                float y = 0.9f + lvl * 0.45f;
                Box(new Vector3(-4.4f, y, -KD + Wall), new Vector3(-2.2f, y + 0.04f, -KD + Wall + 0.45f), "ki_valkoinen", false);
            }
            // armoire a boissons (vitree, eclairee) a droite
            Box(new Vector3(2.4f, 0f, -KD + Wall), new Vector3(3.6f, 2.0f, -KD + Wall + 0.7f), "ki_valkoinen", "ki_valkoinen", "ki_valkoinen", null, null, null);
            GlassX(2.45f, 3.55f, 0.1f, 1.9f, -KD + Wall + 0.71f);
            // banquettes rouges devant la vitrine
            foreach (float sx in new[] { -1f, 1f })
            {
                float x0 = sx * 3.2f, x1 = sx * 7.4f;
                Box(new Vector3(Mathf.Min(x0, x1), 0f, 7.4f), new Vector3(Mathf.Max(x0, x1), 0.42f, 8.2f), "ki_istuin");
                Box(new Vector3(Mathf.Min(x0, x1), 0.42f, 8.05f), new Vector3(Mathf.Max(x0, x1), 0.95f, 8.3f), "ki_istuin", false);
            }
            // tapis d'entree, tapis rouge jusqu'au comptoir
            Plane("ki_musta", new Vector3(0f, 0.006f, KD - 1.6f), 3.0f, 2.0f, Vector3.up);
            Plane("ki_matto", new Vector3(0f, 0.005f, 1.8f), 2.0f, 11.6f, Vector3.up);
            // lambris de bois sur les murs du hall (bas, avec une cimaise en laiton)
            foreach (float s in new[] { -1f, 1f })
            {
                float xf = s * (KPart - Wall / 2 - 0.012f);
                foreach (var seg in new[] { new[] { -KD + 2.25f, -0.3f }, new[] { 0.3f, KD - Wall } })
                {
                    Box(new Vector3(Mathf.Min(xf, xf - s * 0.02f), 0f, seg[0]), new Vector3(Mathf.Max(xf, xf - s * 0.02f), 1.0f, seg[1]), "ki_paneeli", false);
                    Box(new Vector3(Mathf.Min(xf, xf - s * 0.035f), 1.0f, seg[0]), new Vector3(Mathf.Max(xf, xf - s * 0.035f), 1.04f, seg[1]), "ki_kulta", false);
                }
            }
            Box(new Vector3(-KPart + Wall / 2, 0f, -KD + Wall), new Vector3(KPart - Wall / 2, 1.0f, -KD + Wall + 0.02f), "ki_paneeli", false);
            // poteaux a cordons rouges devant le comptoir (file d'attente)
            foreach (float x in new[] { -4.2f, -2.4f, -0.6f, 1.2f, 3.0f, 4.6f })
            {
                Box(new Vector3(x - 0.15f, 0f, -3.2f - 0.15f), new Vector3(x + 0.15f, 0.04f, -3.2f + 0.15f), "ki_kulta", false);
                Box(new Vector3(x - 0.025f, 0.04f, -3.2f - 0.025f), new Vector3(x + 0.025f, 0.95f, -3.2f + 0.025f), "ki_kulta", false);
            }
            foreach (float x in new[] { -4.2f, -2.4f, 1.2f, 3.0f })
                Box(new Vector3(x, 0.8f, -3.22f), new Vector3(x + 1.8f, 0.85f, -3.18f), "ki_punainen", false);
            // plantes en pot dans les coins de la facade
            foreach (float x in new[] { -10.2f, 10.2f })
            {
                Box(new Vector3(x - 0.3f, 0f, KD - 0.95f), new Vector3(x + 0.3f, 0.55f, KD - 0.35f), "ki_musta");
                for (int k = 0; k < 7; k++)
                {
                    float a = k * 0.9f, rr = 0.12f + (k % 3) * 0.06f, hh = 0.9f + (k % 4) * 0.25f;
                    Box(new Vector3(x + Mathf.Cos(a) * rr - 0.12f, 0.55f, KD - 0.65f + Mathf.Sin(a) * rr - 0.12f), new Vector3(x + Mathf.Cos(a) * rr + 0.12f, 0.55f + hh, KD - 0.65f + Mathf.Sin(a) * rr + 0.12f), "ki_kasvi", false);
                }
            }
            // affiches encadrees sur les cloisons (cote hall)
            int n = 1;
            foreach (float s in new[] { -1f, 1f })
                foreach (float z in new[] { 5.5f, 3.2f })
                {
                    float x = s * (KPart - Wall / 2 - 0.02f), w = 1.0f, h = 1.4f;
                    Vector3 nn = s > 0 ? Vector3.left : Vector3.right;
                    Box(new Vector3(Mathf.Min(x, x - s * 0.05f), 1.0f - 0.06f, z - w / 2 - 0.06f), new Vector3(Mathf.Max(x, x - s * 0.05f), 1.0f + h + 0.06f, z + w / 2 + 0.06f), "ki_kulta", false);
                    Plane("ki_juliste" + n, new Vector3(x - s * 0.055f, 1.0f + h / 2, z), w, h, nn);
                    n++;
                }
            // cabines de projection contre les cloisons (le projecteur et sa bobine : KinoSali)
            foreach (float s in new[] { -1f, 1f }) KinoBooth(s);
        }

        // cabine : murs minces x = s*8,9 (cote hall, porte), z = +-1,9 ; plafond a 3,3 ; panneau KONEHUONE
        static void KinoBooth(float s)
        {
            float xo = s * 8.9f, xi = s * (KPart - Wall / 2), z0 = -1.9f, z1 = 1.9f, top = 3.5f, t = 0.12f;
            // mur cote hall, plein (on entre par la salle : porte dans la cloison, KinoShell)
            Box(new Vector3(Mathf.Min(xo, xo + s * t), 0f, z0), new Vector3(Mathf.Max(xo, xo + s * t), top, z1), "ki_aulaseina");
            // plancher de la cabine au niveau du fond de la salle
            Box(new Vector3(Mathf.Min(xo, xi), 0f, z0), new Vector3(Mathf.Max(xo, xi), KBack, z1), null, null, "ki_betoni", null, null, null);
            // murs lateraux
            foreach (float z in new[] { z0, z1 - t })
                Box(new Vector3(Mathf.Min(xo, xi), 0f, z), new Vector3(Mathf.Max(xo, xi), top, z + t), "ki_aulaseina");
            // plafond
            Box(new Vector3(Mathf.Min(xo, xi), top, z0), new Vector3(Mathf.Max(xo, xi), top + 0.08f, z1), "ki_aulaseina", false);
        }

        // ---------------------------------------------------------------- parking
        // asphalte en grille de 1 m qui suit le sol, places marquees le long de la facade (perpendiculaires), allee devant ;
        // muret tout autour (entree a l'ouest, sortie a l'est, cote route), lampadaires.
        static void KinoParking()
        {
            float x0 = -KL - 1.5f, x1 = KL + 1.5f, z0 = KD + 1.8f + kinoSteps * 0.32f, z1 = KPark;
            Func<float, float, float> gy = (x, z) =>
            {
                RaycastHit? h = GroundHit(kino.transform.TransformPoint(new Vector3(x, 50f, z)));
                return h.HasValue ? kino.transform.InverseTransformPoint(h.Value.point).y : -0.2f;
            };
            // (maille de 1 m, 10 cm au-dessus du sol : avec 1,5 m et 4 cm, des triangles de neige du terrain passaient au travers)
            HeightGrid("ki_asfaltti", x0, x1, z0, z1 + 0.6f, 1f, gy, 0.1f);
            // places : 2,6 m de large, 5 m de profondeur depuis le trottoir, ligne du fond ; pas devant l'entree du hall
            for (float x = -25f; x <= 25.01f; x += 2.6f)
            {
                if (Mathf.Abs(x) < 3.0f) continue;
                Line(x, z0 + 0.2f, x, z0 + 5.2f, gy);
            }
            Line(-25f, z0 + 5.2f, -3.0f, z0 + 5.2f, gy); Line(3.0f, z0 + 5.2f, 25f, z0 + 5.2f, gy);
            // passage pieton devant les portes du hall (bandes paralleles a la facade, comme un vrai passage)
            for (float z = z0 + 0.5f; z <= z0 + 4.6f; z += 0.7f) Line(-2.4f, z, 2.4f, z, gy, 0.35f);
            // muret : 0,9 m, brique et chaperon en beton ; au nord, entree (ouest) et sortie (est), larges de 7,8 m, piliers
            float mz = z1 + 0.3f, gx = KL + 1.8f;
            WallRun(-20f, mz, 20f, mz, gy);
            foreach (float s in new[] { -1f, 1f })
            {
                WallRun(s * gx, -KD + 0.2f, s * gx, mz, gy);
                foreach (float px in new[] { s * 20f, s * gx }) KinoPillar(px, mz, gy);
                // panneau sur le pilier du cote du muret, fleche peinte au sol (entree : vers le parking ; sortie : vers la route)
                float cx = s * (20f + gx) / 2f, g = gy(s * 20f, mz);
                Plane(s < 0 ? "ki_sisaan" : "ki_ulosajo", new Vector3(s * 20f, g + 1.55f, mz + 0.27f), 0.48f, 0.6f, Vector3.forward);
                GroundArrow(cx, mz - 2.5f, s < 0 ? -1f : 1f, gy);
                KinoDriveway(KinoDrives()[s < 0 ? 0 : 1], gy);
            }
            // lampadaires (lumiere : KinoLights)
            foreach (float x in new[] { -19f, -7f, 7f, 19f }) KinoLamp(new Vector3(x, gy(x, z1 - 0.6f), z1 - 0.6f));
        }

        // pilier de brique (0,5 x 1,3 m, chaperon en beton) au bout d'un muret
        static void KinoPillar(float x, float z, Func<float, float, float> gy)
        {
            float g = gy(x, z);
            Box(new Vector3(x - 0.25f, g - 0.5f, z - 0.25f), new Vector3(x + 0.25f, g + 1.3f, z + 0.25f), "ki_tiili");
            Box(new Vector3(x - 0.3f, g + 1.3f, z - 0.3f), new Vector3(x + 0.3f, g + 1.4f, z + 0.3f), "ki_betoni", false);
        }

        // fleche blanche peinte au sol, pointe vers +z (dir 1) ou -z (dir -1)
        static void GroundArrow(float x, float z, float dir, Func<float, float, float> gy)
        {
            Line(x, z - dir * 1.4f, x, z + dir * 0.4f, gy, 0.35f);
            float y = gy(x, z + dir * 0.6f) + 0.13f;
            Vector3 a = new Vector3(x - 0.6f, y, z + dir * 0.35f), b = new Vector3(x + 0.6f, y, z + dir * 0.35f), c = new Vector3(x, y, z + dir * 1.3f);
            if (dir > 0) Tri("ki_viiva", a, c, b); else Tri("ki_viiva", b, c, a);
        }

        // allee d'asphalte de l'ouverture du muret jusqu'a la route la plus proche (sondee : Clearing.IsRoad), 6,5 m de
        // large, en troncons de 1 m qui suivent le sol (et le bord de la route a l'arrivee)
        // Allees d'entree et de sortie : de l'ouverture du muret jusqu'au bord de la route la plus proche (sondee :
        // Clearing.IsRoad), calculees une fois par chargement (le defrichage en a besoin avant la construction : bourrelets de
        // neige coupes dessous). Repere du cinema : [depart, arrivee sur la route].
        static List<Vector3[]> kinoDrives;
        public static List<Vector3[]> KinoDrives()
        {
            if (kinoDrives != null) return kinoDrives;
            kinoDrives = new List<Vector3[]>();
            Vector3 c; float yaw; string fl;
            KinoFrame(out c, out yaw, out fl);
            Quaternion r = Quaternion.Euler(0, yaw, 0);
            float mz = KPark + 0.3f, gx = KL + 1.8f;
            foreach (float s in new[] { -1f, 1f })
            {
                float cx = s * (20f + gx) / 2f;
                Vector3 from = new Vector3(cx, 0f, mz - 0.4f), best = new Vector3(cx, 0f, mz + 8f);
                float bd = float.MaxValue;
                for (float dz = 1f; dz <= 26f; dz += 1f)
                    for (float dx = -14f; dx <= 14f; dx += 1f)
                    {
                        Vector3 p = new Vector3(cx + dx, 0f, mz + dz), w = c + r * p;
                        if (!Clearing.IsRoad(w.x, w.z)) continue;
                        float d = (p - from).sqrMagnitude;
                        if (d < bd) { bd = d; best = p; }
                    }
                kinoDrives.Add(new[] { from, best });
                L("cinema : allee de l'ouverture x " + cx.ToString("F0") + (bd < float.MaxValue ? " vers la route a " + Mathf.Sqrt(bd).ToString("F1") + " m" : " : pas de route trouvee"));
            }
            return kinoDrives;
        }

        // le point (x, z du monde) est-il sous une allee (bourrelets de neige de la route a couper : Clearing)
        public static bool OnKinoDrive(float x, float z)
        {
            if (kinoDrives == null) return false;
            Vector3 c; float yaw; string fl;
            KinoFrame(out c, out yaw, out fl);
            Vector3 p = Quaternion.Inverse(Quaternion.Euler(0, yaw, 0)) * new Vector3(x - c.x, 0f, z - c.z);
            foreach (Vector3[] d in kinoDrives)
            {
                Vector3 dir = d[1] - d[0]; float len = dir.magnitude; if (len < 0.1f) continue; dir /= len;
                float along = Vector3.Dot(p - d[0], dir), across = Mathf.Abs(Vector3.Dot(p - d[0], new Vector3(dir.z, 0f, -dir.x)));
                if (along > -1f && along < len + 3f && across < 4.2f) return true;
            }
            return false;
        }

        // allee : asphalte de 6,5 m en troncons de 50 cm qui suivent le sol, bordures de beton qui descendent dans la neige ;
        // elle s'arrete au bord de la route (son accotement enneige fait partie du maillage de la route, intouchable)
        static void KinoDriveway(Vector3[] d, Func<float, float, float> gy)
        {
            Vector3 from = d[0], dir = d[1] - d[0];
            float len = Mathf.Max(1f, dir.magnitude - 1.2f);
            dir.y = 0f; dir.Normalize();
            Vector3 side = new Vector3(dir.z, 0f, -dir.x) * 3.25f;
            int n = Mathf.Max(1, Mathf.CeilToInt(len / 0.5f));
            for (int i = 0; i < n; i++)
            {
                Vector3 p0 = from + dir * (len * i / n), p1 = from + dir * (len * (i + 1) / n);
                Vector3[] q = { p0 - side, p0 + side, p1 + side, p1 - side };
                for (int k = 0; k < 4; k++) q[k].y = gy(q[k].x, q[k].z) + 0.08f;
                if (Vector3.Cross(q[3] - q[0], q[1] - q[0]).y > 0f) Quad("ki_asfaltti", q[0], q[1], q[2], q[3]);
                else Quad("ki_asfaltti", q[1], q[0], q[3], q[2]);
                // bordures : face exterieure en beton, du bord de l'asphalte a 25 cm sous le sol
                foreach (int e in new[] { 0, 1 })
                {
                    Vector3 a = e == 0 ? q[0] : q[1], b = e == 0 ? q[3] : q[2];
                    Vector3 out0 = (e == 0 ? -side : side).normalized * 0.12f;
                    Vector3 a2 = a + out0, b2 = b + out0;
                    a2.y = gy(a2.x, a2.z) - 0.25f; b2.y = gy(b2.x, b2.z) - 0.25f;
                    Vector3 at = a + out0 + Vector3.up * 0.02f, bt = b + out0 + Vector3.up * 0.02f;
                    // dessus de la bordure et sa face exterieure (deux faces : vue des deux cotes)
                    Quad("ki_betoni", a, b, bt, at); Quad("ki_betoni", b, a, at, bt);
                    Quad("ki_betoni", at, bt, b2, a2); Quad("ki_betoni", bt, at, a2, b2);
                }
            }
            // fin de l'allee : petite rampe jusqu'au sol (bord vers la route)
            Vector3 e0 = from + dir * len - side, e1 = from + dir * len + side;
            Vector3 f0 = e0 + dir * 0.6f, f1 = e1 + dir * 0.6f;
            e0.y = gy(e0.x, e0.z) + 0.08f; e1.y = gy(e1.x, e1.z) + 0.08f; f0.y = gy(f0.x, f0.z) - 0.05f; f1.y = gy(f1.x, f1.z) - 0.05f;
            if (Vector3.Cross(f0 - e0, e1 - e0).y > 0f) Quad("ki_asfaltti", e0, e1, f1, f0); else Quad("ki_asfaltti", e1, e0, f0, f1);
        }

        // grille de quads (cellule c) a la hauteur du sol + lift
        static void HeightGrid(string key, float x0, float x1, float z0, float z1, float c, Func<float, float, float> gy, float lift)
        {
            int nx = Mathf.Max(1, Mathf.CeilToInt((x1 - x0) / c)), nz = Mathf.Max(1, Mathf.CeilToInt((z1 - z0) / c));
            var h = new float[nx + 1, nz + 1];
            for (int i = 0; i <= nx; i++) for (int j = 0; j <= nz; j++) h[i, j] = gy(x0 + (x1 - x0) * i / nx, z0 + (z1 - z0) * j / nz) + lift;
            for (int i = 0; i < nx; i++)
                for (int j = 0; j < nz; j++)
                {
                    float xa = x0 + (x1 - x0) * i / nx, xb = x0 + (x1 - x0) * (i + 1) / nx, za = z0 + (z1 - z0) * j / nz, zb = z0 + (z1 - z0) * (j + 1) / nz;
                    Quad(key, new Vector3(xa, h[i, j], za), new Vector3(xb, h[i + 1, j], za), new Vector3(xb, h[i + 1, j + 1], zb), new Vector3(xa, h[i, j + 1], zb));
                }
        }

        // ligne blanche au sol de (xa, za) a (xb, zb), 12 cm de large (w)
        static void Line(float xa, float za, float xb, float zb, Func<float, float, float> gy, float w = 0.12f)
        {
            Vector3 d = new Vector3(xb - xa, 0, zb - za);
            Vector3 n = Vector3.Cross(Vector3.up, d.normalized) * (w / 2);
            int steps = Mathf.Max(1, Mathf.CeilToInt(d.magnitude / 1.5f));
            for (int i = 0; i < steps; i++)
            {
                Vector3 p0 = new Vector3(xa, 0, za) + d * (i / (float)steps), p1 = new Vector3(xa, 0, za) + d * ((i + 1) / (float)steps);
                p0.y = gy(p0.x, p0.z) + 0.12f; p1.y = gy(p1.x, p1.z) + 0.12f;   // (sur l'asphalte, a 10 cm du sol)
                Quad("ki_viiva", p0 - n, p0 + n, p1 + n, p1 - n);
            }
        }

        // muret de (xa, za) a (xb, zb) par troncons de 2 m poses sur le sol
        static void WallRun(float xa, float za, float xb, float zb, Func<float, float, float> gy)
        {
            Vector3 a = new Vector3(xa, 0, za), d = new Vector3(xb - xa, 0, zb - za);
            int n = Mathf.Max(1, Mathf.CeilToInt(d.magnitude / 2f));
            bool alongX = Mathf.Abs(d.x) > Mathf.Abs(d.z);
            for (int i = 0; i < n; i++)
            {
                Vector3 p0 = a + d * (i / (float)n), p1 = a + d * ((i + 1) / (float)n), m = (p0 + p1) / 2;
                float g = Mathf.Min(gy(p0.x, p0.z), gy(p1.x, p1.z));
                Vector3 mn = new Vector3(Mathf.Min(p0.x, p1.x), g - 0.5f, Mathf.Min(p0.z, p1.z)), mx = new Vector3(Mathf.Max(p0.x, p1.x), g + 0.9f, Mathf.Max(p0.z, p1.z));
                if (alongX) { mn.z = m.z - 0.15f; mx.z = m.z + 0.15f; } else { mn.x = m.x - 0.15f; mx.x = m.x + 0.15f; }
                Box(mn, mx, "ki_tiili");
                Box(new Vector3(mn.x - (alongX ? 0f : 0.04f), mx.y, mn.z - (alongX ? 0.04f : 0f)), new Vector3(mx.x + (alongX ? 0f : 0.04f), mx.y + 0.08f, mx.z + (alongX ? 0.04f : 0f)), "ki_betoni", false);
            }
        }

        static readonly List<Vector3> kinoLampSpots = new List<Vector3>();
        static void KinoLamp(Vector3 foot)
        {
            Box(new Vector3(foot.x - 0.08f, foot.y - 0.3f, foot.z - 0.08f), new Vector3(foot.x + 0.08f, foot.y + 5.0f, foot.z + 0.08f), "ki_alu");
            Box(new Vector3(foot.x - 0.08f, foot.y + 4.9f, foot.z - 1.4f), new Vector3(foot.x + 0.08f, foot.y + 5.0f, foot.z), "ki_alu", false);
            Box(new Vector3(foot.x - 0.2f, foot.y + 4.78f, foot.z - 1.6f), new Vector3(foot.x + 0.2f, foot.y + 4.92f, foot.z - 1.1f), "ki_valkoinen", false);
            kinoLampSpots.Add(new Vector3(foot.x, foot.y + 4.6f, foot.z - 1.35f));
        }

        // ---------------------------------------------------------------- portes coulissantes du hall (comme au musee)
        static void KinoDoors()
        {
            kinoLeaves = new Transform[2];
            float zi = KD - 0.3f, th = 0.045f, top = 2.4f, f = 0.07f;
            for (int k = 0; k < 2; k++)
            {
                float sx = k == 0 ? -1f : 1f;
                var g = new GameObject("kino_ovi" + k);
                g.transform.SetParent(kino.transform, false);
                g.transform.localPosition = new Vector3(0, 0.01f, zi);
                float a = sx > 0 ? 0f : -1.2f, b = sx > 0 ? 1.2f : 0f;
                Box(new Vector3(a, 0, -th / 2), new Vector3(b, f, th / 2), "ki_alu", false);
                Box(new Vector3(a, top - f, -th / 2), new Vector3(b, top, th / 2), "ki_alu", false);
                Box(new Vector3(a, 0, -th / 2), new Vector3(a + f, top, th / 2), "ki_alu", false);
                Box(new Vector3(b - f, 0, -th / 2), new Vector3(b, top, th / 2), "ki_alu", false);
                GlassX(a + f, b - f, f, top - f, 0f);
                float hx = sx > 0 ? a + 0.16f : b - 0.16f;
                Box(new Vector3(hx - 0.015f, 0.8f, th / 2), new Vector3(hx + 0.015f, 1.5f, th / 2 + 0.04f), "ki_kulta", false);
                Box(new Vector3(hx - 0.015f, 0.8f, -th / 2 - 0.04f), new Vector3(hx + 0.015f, 1.5f, -th / 2), "ki_kulta", false);
                var keep = root; root = g;
                Flush();
                root = keep;
                var cg = new GameObject("kino_ovi_coll");
                cg.transform.SetParent(g.transform, false);
                cg.transform.localPosition = new Vector3((a + b) / 2, top / 2, 0);
                cg.AddComponent<BoxCollider>().size = new Vector3(b - a, top, 0.08f);
                kinoLeaves[k] = g.transform;
            }
            Box(new Vector3(-2.5f, 2.4f, zi - 0.16f), new Vector3(2.5f, 2.62f, zi + 0.03f), "ki_alu", false);
            Flush();
        }

        static void TickKino(float now)
        {
            if (kino == null) return;
            if (kinoLeaves != null)
            {
                float t = Time.time;
                Vector3 c = kino.transform.TransformPoint(new Vector3(0, 1f, KD - 0.3f));
                if (forceMuseoDoor || Near(Game.PlayerT, c)) kinoNear = t;
                else foreach (Avatar av in PlayerSync.Avatars) if (av != null && av.Root != null && av.Root.activeInHierarchy && Near(av.Root.transform, c)) { kinoNear = t; break; }
                float target = t - kinoNear < 1.2f ? 1f : 0f;
                if (kinoOpen != target)
                {
                    kinoOpen = Mathf.MoveTowards(kinoOpen, target, Time.deltaTime * 1.6f);
                    float e = kinoOpen * kinoOpen * (3f - 2f * kinoOpen);
                    kinoLeaves[0].localPosition = new Vector3(-e * 1.15f, 0.01f, KD - 0.3f);
                    kinoLeaves[1].localPosition = new Vector3(e * 1.15f, 0.01f, KD - 0.3f);
                }
            }
            TickKinoKassa(now);
            TickKinoSali(now);
        }

        // ---------------------------------------------------------------- lumieres
        static void KinoLights()
        {
            var warm = new Color(1f, 0.86f, 0.68f);
            Func<Vector3, float, float, Color, Light> point = (p, i, r, col) =>
            {
                var g = new GameObject("kino_valo");
                g.transform.SetParent(kino.transform, false); g.transform.localPosition = p;
                var l = g.AddComponent<Light>(); l.type = LightType.Point; l.color = col; l.intensity = i; l.range = r; l.shadows = LightShadows.None;
                return l;
            };
            // hall
            foreach (float x in new[] { -6f, 0f, 6f })
                foreach (float z in new[] { -3.5f, 3.5f }) point(new Vector3(x, 3.6f, z), 1.4f, 8f, warm);
            // sous l'auvent (ampoules) et l'enseigne (lumiere rouge sur la facade)
            point(new Vector3(-3.5f, 3.1f, KD + 1.8f), 1.6f, 7f, warm);
            point(new Vector3(3.5f, 3.1f, KD + 1.8f), 1.6f, 7f, warm);
            point(new Vector3(0f, KLobH + 1.9f, KD + 0.8f), 1.5f, 9f, new Color(1f, 0.55f, 0.45f));
            // vitrines d'affiches
            foreach (float s in new[] { -1f, 1f }) foreach (float x in new[] { 13.6f, 17.2f, 20.8f }) point(new Vector3(s * x, 2.7f, KD + 0.6f), 0.9f, 2.6f, warm);
            // salles : appliques (baissees pendant la seance : KinoSali)
            foreach (float s in new[] { -1f, 1f })
                foreach (float x in new[] { 14f, 18f, 22f })
                    foreach (float z in new[] { -KD + 0.6f, KD - 0.6f })
                        kinoSalLights.Add(point(new Vector3(s * x, 3.0f, z), 1.0f, 6f, warm));
            // lampadaires du parking
            foreach (Vector3 p in kinoLampSpots) point(p, 1.6f, 14f, new Color(1f, 0.82f, 0.55f));
            kinoLampSpots.Clear();
        }

        // zone abritee du jeu (pas de pluie ni de neige dedans) : copie du NoRain du magasin de Teimo
        static void KinoShelter()
        {
            GameObject src = FindPath("STORE_AREA/Stuff/LOD/NoRain");
            if (src == null) { L("cinema : NoRain introuvable"); return; }
            var g = (GameObject)UnityEngine.Object.Instantiate(src);
            g.name = "NoRainKino";
            g.transform.SetParent(kino.transform, false);
            g.transform.localPosition = Vector3.zero; g.transform.localRotation = Quaternion.identity; g.transform.localScale = Vector3.one;
            var mc = g.GetComponent<MeshCollider>();
            float y = KLobCeil - 0.05f;
            var m = new Mesh { name = "kino_norain" };
            m.vertices = new[] { new Vector3(-KL, y, -KD), new Vector3(KL, y, -KD), new Vector3(KL, y, KD + 3.6f), new Vector3(-KL, y, KD + 3.6f) };
            m.triangles = new[] { 0, 2, 1, 0, 3, 2, 0, 1, 2, 0, 2, 3 };
            m.RecalculateBounds();
            if (mc != null) { mc.sharedMesh = null; mc.sharedMesh = m; }
            // temperature dedans (automate « Data » de la zone abritee, variable Temp : 17 ou 21 chez Teimo et a l'inspection) :
            // un cinema chauffe ([Kino] Lampo, 21 par defaut ; JD, 22/10)
            float temp = Config.GetFloat("Kino", "Lampo", 21f);
            foreach (PlayMakerFSM f in g.GetComponents<PlayMakerFSM>())
            {
                if (f.FsmName != "Data") continue;
                HutongGames.PlayMaker.FsmFloat tv = f.FsmVariables.FindFsmFloat("Temp");
                if (tv != null) { L("cinema : temperature dedans " + tv.Value.ToString("F0") + " -> " + temp.ToString("F0")); tv.Value = temp; }
            }
            g.SetActive(true);
        }

        // ---------------------------------------------------------------- essais
        static void KinoEssai(string[] a, float now)
        {
            switch (a[1])
            {
                case "matieres":
                {
                    // matieres <motif> : noms des matieres du jeu qui contiennent le motif, texture et taille
                    string q = a.Length > 2 ? a[2].ToLowerInvariant() : "";
                    var seen = new HashSet<string>();
                    foreach (Material m in Resources.FindObjectsOfTypeAll<Material>())
                    {
                        if (m == null || !seen.Add(m.name) || m.name.ToLowerInvariant().IndexOf(q, StringComparison.Ordinal) < 0) continue;
                        Texture t = m.HasProperty("_MainTex") ? m.mainTexture : null;
                        L("essai : matiere '" + m.name + "' shader " + (m.shader != null ? m.shader.name : "?") + " texture " + (t != null ? t.name + " " + t.width + "x" + t.height : "-"));
                    }
                    break;
                }
                case "kino":
                {
                    // kino x y z tx ty tz nom [attente] [fov] : comme "lieu", dans le repere du cinema
                    if (kino == null) { L("essai : pas de cinema"); break; }
                    Vector3 at = kino.transform.TransformPoint(new Vector3(F(a[2]), F(a[3]), F(a[4]))), tg = kino.transform.TransformPoint(new Vector3(F(a[5]), F(a[6]), F(a[7])));
                    var b = new string[a.Length];
                    b[0] = a[0]; b[1] = "lieu";
                    var ci = System.Globalization.CultureInfo.InvariantCulture;
                    b[2] = at.x.ToString(ci); b[3] = at.y.ToString(ci); b[4] = at.z.ToString(ci); b[5] = tg.x.ToString(ci); b[6] = tg.y.ToString(ci); b[7] = tg.z.ToString(ci);
                    for (int i = 8; i < a.Length; i++) b[i] = a[i];
                    script.Insert(scriptAt, b);
                    break;
                }
                default: KinoEssai2(a, now); break;
            }
        }
    }
}
