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
    // on y peint seulement les places). Construit en code dans son repere : x = longueur (le long de la piste), z = profondeur
    // (vitrine et porte en +z vers la piste), y = 0 au plancher.
    // Refait le 02/11 (JD : « un petit batiment, un peu dans le style de la boutique s21, pour garder l'identite du jeu et son
    // atmosphere », photo d'un magasin de pieces auto) : maison de bois de village comme la s21 -- bardage rouge a planches, rives
    // et cadres blancs, soubassement en pierre, toit de tole a deux pans enneige, enseigne au-dessus de la porte, perron en bois ;
    // dedans, murs clairs, sol carrele gris, comptoir de bois au fond (caisse, vendeur), etageres blanches le long des murs,
    // presentoir d'essuie-glaces bleu, gondole de bidons d'huile, batteries -- aucune marque reelle.
    //  - aile ouest (x < 0) : CARDINAL, pieces des mods (CoopApi.AddShopItem) ; aile est (x > 0) : KAYTETYT OSAT, pieces
    //    d'occasion de la Rivett renouvelees tous les 3 jours (OsatMyynti) ;
    //  - par aile : une etagere au fond et deux contre le pignon (4 niveaux de 3 cases), une estrade pour les grosses pieces
    //    pres de la vitrine, une palette NOUTO (retrait) au bout du comptoir : les achats y apparaissent.
    // [Osat] Paikka=x,z,cap[,plancher|auto] : place, cap et hauteur du plancher (les memes chez tous).
    public static partial class Kauppa
    {
        const float OL = 5.2f, OD = 3.6f, OH = 3.6f, OCeil = 2.95f, OPitch = 25f;   // (murs de 3,6 m : la rive du toit au-dessus de l'enseigne, pas devant)
        // comptoir (repere du magasin) : le vendeur derriere, la caisse dessus a droite
        const float OCounterX = 1.5f, OCounterZ0 = -2.45f, OCounterZ1 = -1.8f, OCounterTop = 0.98f;
        static readonly Vector3 OsatVendorAt = new Vector3(0f, 0f, -2.95f), OsatRegisterAt = new Vector3(1.0f, OCounterTop + 0.05f, -2.12f);
        // (facade de l'ancien hangar a z = +5 : parvis jusqu'au meme bord de piste)
        const float OFrontEdge = 6.6f;

        static GameObject osat;
        static Transform[] osatLeaves;
        static float osatOpen, osatNear = -10f, osatDrop;

        public static bool OsatOn { get { return Ajouts.Osat && Config.GetInt("Osat", "Active", 1) != 0; } }

        static void ResetOsat() { osat = null; osatLeaves = null; osatOpen = 0f; osatNear = -10f; ResetOsatMyynti(); }

        // place et cap du magasin (aussi pour le defrichage, Clearing). Releve du 23/10 : piste (Road2) a x -1748 environ, barriere
        // de la piste de ski a -1760 (z -115) .. -1772 (z -150).
        public static void OsatFrame(out Vector3 c, out float yaw, out string floor)
        {
            string s = Config.Get("Osat", "Paikka", Ville.On ? Ville.Osat : "-1755.8,-136.7,85.7,auto");
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            string[] a = s.Split(',');
            c = new Vector3(float.Parse(a[0], ci), 0f, float.Parse(a[1], ci));
            yaw = float.Parse(a[2], ci);
            floor = a.Length > 3 ? a[3].Trim() : "auto";
        }

        // terrain du magasin (defrichage) : le batiment, son perron et son parvis, pas la piste
        public static Vector2[] OsatPlot()
        {
            Vector3 c; float yaw; string fl;
            OsatFrame(out c, out yaw, out fl);
            Quaternion r = Quaternion.Euler(0, yaw, 0);
            var pts = new[] { new Vector3(-OL - 2f, 0, -OD - 2f), new Vector3(OL + 2f, 0, -OD - 2f), new Vector3(OL + 2f, 0, OFrontEdge + 0.6f), new Vector3(-OL - 2f, 0, OFrontEdge + 0.6f) };
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
            KinoMaterials();   // (beton, alu, verre, lignes jaunes, neige... : les memes que le cinema)
            // (maison de bois de la s21 : meme bardage, memes rives, meme pierre, meme tole -- deja la si la boutique est construite)
            if (!mats.ContainsKey("lauta")) Mat("lauta", "wood_panel7", 2.2f);
            if (!mats.ContainsKey("lista")) Mat("lista", "wood_panel20", 1.2f);
            if (!mats.ContainsKey("kivi")) Mat("kivi", "rock_sokkeli", 2.0f);
            if (!mats.ContainsKey("katto")) Mat("katto", "roof_metal 1", 2.0f, new Color(0.62f, 0.42f, 0.38f));
            if (!mats.ContainsKey("lumi")) Mat("lumi", "SNOW", 3f);
            if (!mats.ContainsKey("terassi")) Mat("terassi", "wood_panel19", 1.5f);
            if (!mats.ContainsKey("tiski")) Mat("tiski", "wood12", 1.0f);
            if (!mats.ContainsKey("metalli")) Mat("metalli", "metal_galvanized", 1f, new Color(0.9f, 0.9f, 0.88f));
            // (peintures unies : JD ne veut pas de textures du jeu qui se repetent ou qui font sale)
            // dedans : les matieres du magasin de Teimo (JD 02/11 : « utilise des textures pour l'interieur », « les presentoirs et les
            // etageres du Teimo shop ») -- carrelage tiles4, murs wall_paint7, gondoles blanches store_shelfs (les meubles du jeu sont
            // regroupes en lots statiques : formes refaites en code avec leurs matieres, comme la gondole de la s21)
            Mat("os_sisa", "wall_paint7", 1.5f);
            Mat("os_lattia", "tiles4", 1.0f);
            Mat("os_katto", "paint_black_matte", 1f, new Color(0.96f, 0.96f, 0.95f));    // plafond
            Mat("os_pysty", "store_shelfs", 1.2f);                                       // montants et fonds des etageres
            Mat("os_palkki", "metal_galvanized", 1f, new Color(0.55f, 0.6f, 0.65f));    // bords des tablettes (etiquettes de prix)
            Mat("os_taso", "metal_galvanized", 1f, new Color(0.93f, 0.93f, 0.92f));     // tablettes
            Mat("os_lava", "paint_black_matte", 1f, new Color(0.72f, 0.58f, 0.38f));     // palettes en bois
            Mat("os_musta", "paint_black_matte", 1f, new Color(0.08f, 0.08f, 0.09f));
            Mat("os_sininen", "paint_black_matte", 1f, new Color(0.11f, 0.27f, 0.6f));   // presentoir des essuie-glaces, batteries
            Mat("os_keltainen", "paint_black_matte", 1f, new Color(0.96f, 0.78f, 0.1f)); // etiquettes jaunes, bidons
            Mat("os_puna", "paint_black_matte", 1f, new Color(0.75f, 0.1f, 0.09f));
            Mat("os_harmaa", "paint_black_matte", 1f, new Color(0.55f, 0.57f, 0.6f));
            Mat("os_valko", "paint_black_matte", 1f, new Color(0.95f, 0.95f, 0.94f));
            OwnMat("os_kyltti", "osat-kyltti.png");
            OwnMat("os_cardinal", "osat-cardinal.png", true);
            OwnMat("os_kaytetyt", Game.Msc ? "osat-satsuma.png" : "osat-kaytetyt.png", true);
            OwnMat("os_nouto", "osat-nouto.png", true);
        }

        static void BuildOsat()
        {
            Vector3 place; float yaw; string fl;
            OsatFrame(out place, out yaw, out fl);
            Quaternion rot = Quaternion.Euler(0, yaw, 0);
            OsatMaterials();
            float gMax = float.MinValue, gMin = float.MaxValue;
            for (float x = -OL; x <= OL + 0.01f; x += 1.3f)
                for (float z = -OD; z <= OD + 0.01f; z += 1.2f)
                {
                    RaycastHit? h = GroundHit(place + rot * new Vector3(x, 0, z) + Vector3.up * 200f);
                    if (!h.HasValue) continue;
                    gMax = Mathf.Max(gMax, h.Value.point.y); gMin = Mathf.Min(gMin, h.Value.point.y);
                }
            if (gMax == float.MinValue) gMax = gMin = 7f;
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            float padY = !float.IsNaN(Clearing.OsatPadY) ? Clearing.OsatPadY : gMin;
            // (une marche au-dessus du sol, comme la s21 : perron en bois devant la porte)
            float floorY = fl != "auto" ? float.Parse(fl, ci) : Mathf.Max(padY + 0.35f, gMax + 0.3f);
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
                OsatRoof();
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

        // ---------------------------------------------------------------- coque (maison de bois de la s21)
        static readonly Hole OsatDoorHole = new Hole(-0.7f, 0.7f, 0f, 2.15f);
        static readonly Hole[] OsatWindows = { new Hole(-4.5f, -1.3f, 0.8f, 2.25f), new Hole(1.3f, 4.5f, 0.8f, 2.25f) };

        static void OsatShell(float down)
        {
            // soubassement en pierre et plancher carrele (carrelage du magasin de Teimo)
            Box(new Vector3(-OL - 0.1f, -down, -OD - 0.1f), new Vector3(OL + 0.1f, -0.02f, OD + 0.1f), "kivi", "kivi", "kivi", null, "kivi", "kivi");
            Box(new Vector3(-OL + Wall, -0.1f, -OD + Wall), new Vector3(OL - Wall, 0f, OD - Wall), null, null, "os_lattia", null, null, null);
            // murs : facade (+z) avec la porte au milieu et deux vitrines ; fond et pignons pleins ; bardage rouge dehors, creme dedans
            var front = new List<Hole>(OsatWindows) { OsatDoorHole };
            MWall(true, OD, -1, -OL, OL, -0.02f, OH, front.ToArray(), "lauta", "os_sisa");
            MWall(true, -OD, 1, -OL, OL, -0.02f, OH, new Hole[0], "lauta", "os_sisa");
            foreach (float s in new[] { -1f, 1f }) MWall(false, s * OL, -s, -OD + Wall, OD - Wall, -0.02f, OH, new Hole[0], "lauta", "os_sisa");
            foreach (Hole h in OsatWindows) { Window(true, OD, -1, h, 2); cols.Add(new Bounds(new Vector3((h.U0 + h.U1) / 2, (h.Y0 + h.Y1) / 2, OD - Wall / 2), new Vector3(h.U1 - h.U0, h.Y1 - h.Y0, Wall))); }
            // encadrement de porte, planches d'angle (blanches)
            Box(new Vector3(OsatDoorHole.U0 - 0.08f, 0, OD), new Vector3(OsatDoorHole.U0, 2.23f, OD + 0.03f), "lista", false);
            Box(new Vector3(OsatDoorHole.U1, 0, OD), new Vector3(OsatDoorHole.U1 + 0.08f, 2.23f, OD + 0.03f), "lista", false);
            Box(new Vector3(OsatDoorHole.U0 - 0.08f, 2.15f, OD), new Vector3(OsatDoorHole.U1 + 0.08f, 2.23f, OD + 0.03f), "lista", false);
            foreach (float sx in new[] { -1f, 1f })
                foreach (float sz in new[] { -1f, 1f })
                {
                    Vector3 c = new Vector3(sx * OL, 0, sz * OD);
                    Box(new Vector3(c.x - (sx > 0 ? 0.12f : 0.03f), -0.02f, c.z - (sz > 0 ? 0.12f : 0.03f)),
                        new Vector3(c.x + (sx > 0 ? 0.03f : 0.12f), OH, c.z + (sz > 0 ? 0.03f : 0.12f)), "lista", false);
                }
            // plinthes dedans (bois, comme la s21), plafond
            foreach (float sz in new[] { -1f, 1f })
                Box(new Vector3(-OL + Wall, 0f, sz > 0 ? OD - Wall - 0.02f : -OD + Wall), new Vector3(OL - Wall, 0.1f, sz > 0 ? OD - Wall : -OD + Wall + 0.02f), "tiski", false);
            foreach (float sx in new[] { -1f, 1f })
                Box(new Vector3(sx > 0 ? OL - Wall - 0.02f : -OL + Wall, 0f, -OD + Wall), new Vector3(sx > 0 ? OL - Wall : -OL + Wall + 0.02f, 0.1f, OD - Wall), "tiski", false);
            Quad("os_katto", new Vector3(-OL + Wall, OCeil, OD - Wall), new Vector3(OL - Wall, OCeil, OD - Wall), new Vector3(OL - Wall, OCeil, -OD + Wall), new Vector3(-OL + Wall, OCeil, -OD + Wall));
            // perron en bois sur toute la facade, marches, puis parvis de beton jusqu'au bord de la piste
            float deck = OD + 1.4f;
            Box(new Vector3(-OL - 0.3f, -osatDrop - 0.5f, OD), new Vector3(OL + 0.3f, -0.03f, deck), "terassi");
            int steps = Mathf.Max(0, Mathf.CeilToInt((osatDrop - 0.08f) / 0.19f));
            float rise = steps > 0 ? (osatDrop - 0.03f) / (steps + 0.0001f) : 0f;
            for (int k = 1; k <= steps; k++)
                Box(new Vector3(-1.4f, -osatDrop - 0.5f, deck + (k - 1) * 0.32f), new Vector3(1.4f, -0.03f - k * rise, deck + k * 0.32f), "terassi");
            float pz0 = deck + steps * 0.32f;
            if (pz0 < OFrontEdge) Box(new Vector3(-OL - 0.5f, -osatDrop - 0.5f, pz0), new Vector3(OL + 0.5f, -osatDrop + 0.04f, OFrontEdge), "ki_betoni");
        }

        // toit a deux pentes (faitage le long de x), debord 0,5 m, tole brun-rouge et neige ; pignons en planches
        static void OsatRoof()
        {
            float tan = Mathf.Tan(OPitch * Mathf.Deg2Rad), cos = Mathf.Cos(OPitch * Mathf.Deg2Rad);
            float ridge = OH + OD * tan, over = 0.5f, run = OD + over, t = 0.12f;
            foreach (float s in new[] { 1f, -1f })
            {
                Quaternion r = Quaternion.Euler(s * OPitch, 0, 0);
                Vector3 nrm = r * Vector3.up;
                nrm = new Vector3(0, nrm.y, s * Mathf.Abs(nrm.z));
                Vector3 under = new Vector3(0, OH + (OD - run / 2) * tan, s * run / 2);
                OBox(under + nrm * (t / 2), r, new Vector3(2 * OL + 0.8f, t, run / cos), "katto", "terassi", "lista");
                OBox(under + nrm * (t + 0.05f), r, new Vector3(2 * OL + 0.6f, 0.1f, run / cos - 0.15f), "lumi", null, "lumi");
                Box(new Vector3(-OL - 0.4f, OH - over * tan - 0.2f, s > 0 ? OD + over - 0.03f : -OD - over), new Vector3(OL + 0.4f, OH - over * tan + 0.02f, s > 0 ? OD + over : -OD - over + 0.03f), "lista", false);
            }
            Box(new Vector3(-OL - 0.4f, ridge + 0.05f, -0.15f), new Vector3(OL + 0.4f, ridge + 0.2f, 0.15f), "lumi", false);
            Tri("lauta", new Vector3(OL, OH, -OD), new Vector3(OL, ridge, 0), new Vector3(OL, OH, OD));
            Tri("lauta", new Vector3(-OL, OH, OD), new Vector3(-OL, ridge, 0), new Vector3(-OL, OH, -OD));
        }

        static void OsatFront()
        {
            // enseigne au-dessus de la porte : CARDINAL VARAOSAT peint sur tole (4 x 1, comme les enseignes de Teimo et de Fleetari,
            // store_banners), sur un chassis de tole galvanisee
            float sw = 3.6f, sh = sw * 512f / 2048f, sy0 = 2.32f, sz = OD + 0.06f;
            Box(new Vector3(-sw / 2 - 0.025f, sy0 - 0.025f, OD), new Vector3(sw / 2 + 0.025f, sy0 + sh + 0.025f, sz), "metalli", "metalli", "metalli", "metalli", null, null, false);
            Quad("os_kyltti", new Vector3(sw / 2, sy0, sz + 0.003f), new Vector3(-sw / 2, sy0, sz + 0.003f), new Vector3(-sw / 2, sy0 + sh, sz + 0.003f), new Vector3(sw / 2, sy0 + sh, sz + 0.003f),
                 new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) });
            // lampes « col de cygne » au-dessus de l'enseigne (comme la s21)
            foreach (float x in new[] { -1.2f, 1.2f })
            {
                Box(new Vector3(x - 0.02f, sy0 + sh + 0.12f, OD + 0.06f), new Vector3(x + 0.02f, sy0 + sh + 0.16f, OD + 0.72f), "metalli", false);
                Box(new Vector3(x - 0.11f, sy0 + sh + 0.06f, OD + 0.66f), new Vector3(x + 0.11f, sy0 + sh + 0.18f, OD + 0.86f), "metalli", false);
            }
            // panneaux des ailes derriere les vitrines (vus de dehors) : CARDINAL a gauche, KAYTETYT OSAT a droite
            float pz = OD - Wall * 0.45f - 0.05f;
            PosterFront("os_cardinal", -3.95f, -1.85f, 1.75f, 2.17f, pz);
            PosterFront("os_kaytetyt", 1.85f, 3.95f, 1.75f, 2.17f, pz);
        }

        // ---------------------------------------------------------------- interieur
        // Case d'etagere ou d'estrade (repere du magasin) : bas au centre, sens de la face vers l'allee, taille utile.
        class OsatSlot { public int Wing; public Vector3 Pos, Face, Size; public bool Big; public object Item; }
        static readonly List<OsatSlot> osatSlots = new List<OsatSlot>();
        static Vector3 OsatPickup(int wing) { return new Vector3(wing == 0 ? -2.45f : 2.45f, 0.16f, -1.15f); }

        static void OsatInterior()
        {
            osatSlots.Clear();
            // comptoir de bois (le vendeur derriere, au fond), plateau noir, bandeau rouge
            Box(new Vector3(-OCounterX, 0f, OCounterZ0), new Vector3(OCounterX, OCounterTop, OCounterZ1), "tiski");
            Box(new Vector3(-OCounterX - 0.05f, OCounterTop, OCounterZ0 - 0.05f), new Vector3(OCounterX + 0.05f, OCounterTop + 0.05f, OCounterZ1 + 0.05f), "os_musta");
            Box(new Vector3(-OCounterX, 0.08f, OCounterZ1), new Vector3(OCounterX, 0.2f, OCounterZ1 + 0.04f), "os_puna", false);
            try { OsatRegister(); } catch (Exception e) { L("magasin de pieces : caisse " + e.Message); }
            foreach (int wing in new[] { 0, 1 })
            {
                float s = wing == 0 ? -1f : 1f;
                // etagere du fond, deux contre le pignon
                OsatShelf(new Vector3(s * 3.3f, 0f, -OD + Wall + 0.27f), Vector3.forward, wing);
                foreach (float z in new[] { -2.0f, 0.2f }) OsatShelf(new Vector3(s * (OL - Wall - 0.27f), 0f, z), s < 0 ? Vector3.right : Vector3.left, wing);
                // estrade des grosses pieces, pres de la vitrine
                Vector3 c = new Vector3(s * 3.1f, 0f, 2.3f);
                Box(new Vector3(c.x - 1.0f, 0f, c.z - 0.65f), new Vector3(c.x + 1.0f, 0.22f, c.z + 0.65f), "os_taso");
                Box(new Vector3(c.x - 1.02f, 0.18f, c.z - 0.67f), new Vector3(c.x + 1.02f, 0.24f, c.z + 0.67f), "os_harmaa", false);
                osatSlots.Add(new OsatSlot { Wing = wing, Pos = c + new Vector3(0f, 0.24f, 0f), Face = Vector3.back, Size = new Vector3(1.8f, 1.6f, 1.1f), Big = true });
                // palette de retrait (NOUTO) et son panneau
                Vector3 p = OsatPickup(wing);
                Box(new Vector3(p.x - 0.55f, 0f, p.z - 0.38f), new Vector3(p.x + 0.55f, 0.14f, p.z + 0.38f), "os_lava");
                Box(new Vector3(p.x - 0.03f, 0f, p.z - 0.48f), new Vector3(p.x + 0.03f, 1.75f, p.z - 0.42f), "ki_alu", false);
                Plane("os_nouto", new Vector3(p.x, 1.55f, p.z - 0.41f), 0.7f, 0.35f, Vector3.forward);
                // panneau suspendu de l'aile (deux faces)
                Vector3 h = new Vector3(s * 3.3f, 2.5f, -0.6f);
                string key = wing == 0 ? "os_cardinal" : "os_kaytetyt";
                Plane(key, h + new Vector3(0, 0, 0.012f), 2.0f, 0.5f, Vector3.forward);
                Plane(key, h - new Vector3(0, 0, 0.012f), 2.0f, 0.5f, Vector3.back);
                Box(new Vector3(h.x - 1.02f, h.y - 0.27f, h.z - 0.01f), new Vector3(h.x + 1.02f, h.y + 0.27f, h.z + 0.01f), "os_musta", false);
                foreach (float dx in new[] { -0.85f, 0.85f }) Box(new Vector3(h.x + dx - 0.01f, h.y + 0.25f, h.z - 0.01f), new Vector3(h.x + dx + 0.01f, OCeil, h.z + 0.01f), "ki_alu", false);
            }
            OsatBelts(new Vector3(-1.75f, 0f, 0.55f));
            OsatOils(new Vector3(1.75f, 0f, 0.55f));
            OsatBatteries(new Vector3(-0.9f, 0f, -1.35f));
            OsatKyparaTorni();   // (presentoir des casques personnalises, aile CARDINAL -- JD 07/11)
            // boites de pieces du jeu sur le dessus des etageres murales (decor)
            foreach (float s in new[] { -1f, 1f })
                for (int k = 0; k < 5; k++)
                    OsatProp(k % 2 == 0 ? "sparkplugbox0" : "boxoilpump0", new Vector3(s * (3.3f - 0.85f + k * 0.42f), 1.96f, -OD + Wall + 0.27f), 180f + k * 7f, new Vector3(0, -1, 0));
            // affiches de rallye du garage de Fleetari, sur le mur du fond derriere le comptoir
            try
            {
                Clone("REPAIRSHOP/LOD/Office/rally_poster", 0, new Vector3(-1.0f, 1.85f, -OD + Wall + 0.01f), 0, new Vector3(0, 0, -1), false, false);
                Clone("REPAIRSHOP/LOD/Office/drag_poster", 0, new Vector3(1.0f, 1.85f, -OD + Wall + 0.01f), 0, new Vector3(0, 0, -1), false, false);
            }
            catch (Exception e) { L("magasin de pieces : affiches " + e.Message); }
        }

        // etagere metallique blanche 2,1 x 0,5 m, fond plein, 5 tablettes ; les 4 du bas ont chacune 3 cases.
        // c : centre au sol, f : face (vers l'allee).
        static void OsatShelf(Vector3 c, Vector3 f, int wing)
        {
            Vector3 a = Vector3.Cross(Vector3.up, f);   // (le long de l'etagere)
            Func<float, float, float, Vector3> P = (u, v, y) => c + a * u + f * v + Vector3.up * y;
            Action<Vector3, Vector3, string, bool> B = (p, q, key, col) => Box(Vector3.Min(p, q), Vector3.Max(p, q), key, col);
            foreach (float u in new[] { -1.05f, 1.01f })
                foreach (float v in new[] { -0.25f, 0.21f })
                    B(P(u, v, 0f), P(u + 0.04f, v + 0.04f, 2.0f), "os_pysty", false);
            B(P(-1.05f, -0.26f, 0.1f), P(1.05f, -0.24f, 2.0f), "os_pysty", false);   // (fond plein, comme les gondoles de la photo)
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

        // ---------------------------------------------------------------- objets du jeu (decor)
        // Modeles d'origine des objets vendus par Teimo (prefabs : motormoil1, coolant0, brakefluid0, atfoil0, FANBELT0, VIN107,
        // r20battery0...) : leurs maillages et matieres seulement, poses en decor (ni physique, ni automate, ni sauvegarde).
        static Dictionary<string, GameObject> osatPrefabs;
        static readonly HashSet<string> osatMissing = new HashSet<string>();
        // (My Summer Car : memes rayons avec ses produits, modeles d'origine des objets du magasin de Teimo et des pieces -- petite ville de JD, 04/11)
        static readonly Dictionary<string, string> OsatPrefabMsc = new Dictionary<string, string> {
            { "motormoil1", "motoroil0" }, { "atfoil0", "motoroil0" }, { "boxtimingbelt", "lightbulbbox0" }, { "boxoilpump0", "lightbulbbox0" },
            { "CARBATTERY0", "battery" } };
        static GameObject OsatPrefab(string name)
        {
            if (osatPrefabs == null)
            {
                osatPrefabs = new Dictionary<string, GameObject>();
                foreach (GameObject g in Resources.FindObjectsOfTypeAll<GameObject>())
                    if (g != null && g.transform.parent == null && !osatPrefabs.ContainsKey(g.name)) osatPrefabs[g.name] = g;
            }
            GameObject r;
            string msc;
            if (Game.Msc && OsatPrefabMsc.TryGetValue(name, out msc)) name = msc;
            return osatPrefabs.TryGetValue(name, out r) ? r : null;
        }
        // copie posee : at (repere du magasin) au point d'ancrage de sa boite (anchor -1/0/1 par axe), tournee de yaw, couchee de tilt
        // autour de x ; renvoie la copie (null sans modele)
        static GameObject OsatProp(string name, Vector3 at, float yaw, Vector3 anchor, float tilt = 0f)
        {
            GameObject src = name.Contains("/") ? FindPath(name) : OsatPrefab(name);   // (chemin : objet de la scene ; sinon modele d'origine)
            if (src == null) { if (osatMissing.Add(name)) L("magasin de pieces : objet du jeu " + name + " introuvable"); return null; }
            var g = new GameObject("osat_" + name);
            g.transform.SetParent(osat.transform, false);
            int n = 0;
            foreach (MeshRenderer r in src.GetComponentsInChildren<MeshRenderer>(true))
            {
                MeshFilter mf = r.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null || r.GetComponent<TextMesh>() != null || mf.sharedMesh.name.StartsWith("Combined Mesh")) continue;
                var c = new GameObject(r.name);
                c.transform.SetParent(g.transform, false);
                c.transform.localPosition = src.transform.InverseTransformPoint(r.transform.position);
                c.transform.localRotation = Quaternion.Inverse(src.transform.rotation) * r.transform.rotation;
                Vector3 ls = r.transform.lossyScale, ss = src.transform.lossyScale;
                c.transform.localScale = new Vector3(Div(ls.x, ss.x), Div(ls.y, ss.y), Div(ls.z, ss.z));
                c.AddComponent<MeshFilter>().sharedMesh = mf.sharedMesh;
                c.AddComponent<MeshRenderer>().sharedMaterials = r.sharedMaterials;
                n++;
            }
            if (n == 0) { UnityEngine.Object.Destroy(g); if (osatMissing.Add(name)) L("magasin de pieces : objet du jeu " + name + " sans maillage"); return null; }
            g.transform.localScale = Ok(src.transform.lossyScale) && src.transform.lossyScale.sqrMagnitude > 1e-6f ? src.transform.lossyScale : Vector3.one;
            g.transform.localRotation = Quaternion.Euler(0f, yaw, 0f) * Quaternion.Euler(tilt, 0f, 0f);
            Bounds b = LocalBounds(g);
            g.transform.localPosition = at - new Vector3(Pick(b, 0, anchor.x), Pick(b, 1, anchor.y), Pick(b, 2, anchor.z));
            return g;
        }
        static float OsatPropWidth(string name, float yaw)
        {
            GameObject g = OsatProp(name, new Vector3(0f, -50f, 0f), yaw, Vector3.zero);
            if (g == null) return 0f;
            float w = LocalBounds(g).size.z;
            UnityEngine.Object.Destroy(g);
            return w;
        }

        // gondole blanche (matiere store_shelfs, comme celles de Teimo) : socle, panneau central, joues, tablettes des deux cotes ;
        // le long de z, centre c, longueur len, profondeur dep ; renvoie le dessus des tablettes
        static float[] OsatGondola(Vector3 c, float len, float dep, float h, int levels)
        {
            float z0 = c.z - len / 2, z1 = c.z + len / 2, x0 = c.x - dep / 2, x1 = c.x + dep / 2, t = 0.03f;
            Box(new Vector3(x0 + 0.04f, 0, z0), new Vector3(x1 - 0.04f, 0.14f, z1), "os_taso", false);
            Box(new Vector3(c.x - 0.02f, 0.14f, z0 + t), new Vector3(c.x + 0.02f, h, z1 - t), "os_pysty", false);
            Box(new Vector3(x0, 0, z0), new Vector3(x1, h, z0 + t), "os_pysty", false);
            Box(new Vector3(x0, 0, z1 - t), new Vector3(x1, h, z1), "os_pysty", false);
            var tops = new float[levels];
            for (int k = 0; k < levels; k++)
            {
                float y = k == 0 ? 0.14f : 0.14f + k * (h - 0.25f) / levels;
                tops[k] = y + (k == 0 ? 0f : 0.025f);
                if (k == 0) continue;
                foreach (float s in new[] { -1f, 1f })
                {
                    float a = s < 0 ? x0 + 0.02f : c.x + 0.02f, b2 = s < 0 ? c.x - 0.02f : x1 - 0.02f;
                    Box(new Vector3(a, y, z0 + t), new Vector3(b2, y + 0.025f, z1 - t), "os_taso", false);
                    float e = s < 0 ? x0 + 0.02f : x1 - 0.035f;
                    Box(new Vector3(e, y - 0.03f, z0 + t), new Vector3(e + 0.015f, y + 0.035f, z1 - t), "os_palkki", false);
                }
            }
            cols.Add(new Bounds(new Vector3(c.x, h / 2, c.z), new Vector3(dep, h, len)));
            return tops;
        }

        // gondole des liquides : de vrais bidons du jeu en rangees des deux cotes (liquide de refroidissement, huile moteur, liquide de
        // frein, huile de boite ATF ; en haut, boites de courroies de distribution)
        static void OsatOils(Vector3 c)
        {
            float len = 1.4f;
            float[] tops = OsatGondola(c, len, 0.66f, 1.5f, 4);
            string[] rows = { "coolant0", "motormoil1", "brakefluid0", "boxtimingbelt" };
            for (int k = 0; k < tops.Length; k++)
                foreach (float s in new[] { -1f, 1f })
                {
                    string item = k == 1 && s > 0 ? "atfoil0" : rows[k];
                    float yaw = s < 0 ? -90f : 90f, w = Mathf.Max(0.12f, OsatPropWidth(item, yaw) + 0.03f);
                    int n = Mathf.Max(1, Mathf.FloorToInt((len - 0.1f) / w));
                    for (int i = 0; i < n; i++)
                        OsatProp(item, new Vector3(c.x + s * 0.17f, tops[k], c.z - (n - 1) * w / 2f + i * w), yaw, new Vector3(0, -1, 0));
                }
        }

        // presentoir des courroies (JD 02/11 : « chaine de courroie suspendue ») : panneau perfore bleu a crochets, courroies
        // d'alternateur (FANBELT0) et de distribution (VIN107) du jeu pendues des deux cotes, bandeau blanc sans marque
        static void OsatBelts(Vector3 c)
        {
            Box(new Vector3(c.x - 0.35f, 0f, c.z - 0.6f), new Vector3(c.x + 0.35f, 0.1f, c.z + 0.6f), "os_musta");
            Box(new Vector3(c.x - 0.03f, 0.1f, c.z - 0.6f), new Vector3(c.x + 0.03f, 1.95f, c.z + 0.6f), "os_sininen");
            Box(new Vector3(c.x - 0.06f, 1.95f, c.z - 0.62f), new Vector3(c.x + 0.06f, 2.2f, c.z + 0.62f), "os_valko", false);
            foreach (float s in new[] { -1f, 1f })
                for (int row = 0; row < 3; row++)
                    for (int k = 0; k < 3; k++)
                    {
                        float z = c.z - 0.38f + k * 0.38f, y = 1.82f - row * 0.58f, x = c.x + s * 0.03f;
                        Box(new Vector3(Mathf.Min(x, x + s * 0.12f), y - 0.006f, z - 0.006f), new Vector3(Mathf.Max(x, x + s * 0.12f), y + 0.006f, z + 0.006f), "metalli", false);   // (crochet)
                        // (courroie pendue au crochet par son haut, a plat contre le panneau)
                        OsatProp(row == 1 ? "VIN107" : "FANBELT0", new Vector3(x + s * 0.06f, y + 0.01f, z), s < 0 ? -90f : 90f, new Vector3(0, 1, 0), 90f);
                    }
            cols.Add(new Bounds(c + new Vector3(0f, 1.0f, 0f), new Vector3(0.7f, 2.0f, 1.2f)));
        }

        // batteries de voiture du jeu (CARBATTERY0, celle des pieces de la Rivett) sur un socle, a cote du comptoir
        static void OsatBatteries(Vector3 c)
        {
            Box(new Vector3(c.x - 0.4f, 0f, c.z - 0.25f), new Vector3(c.x + 0.4f, 0.1f, c.z + 0.25f), "os_harmaa", false);
            for (int k = 0; k < 2; k++)
                for (int n = 0; n < 2; n++) OsatProp("CARBATTERY0", new Vector3(c.x - 0.18f + k * 0.36f, 0.1f + n * 0.21f, c.z), 0f, new Vector3(0, -1, 0));
            cols.Add(new Bounds(c + new Vector3(0f, 0.3f, 0f), new Vector3(0.8f, 0.6f, 0.5f)));
        }

        // ---------------------------------------------------------------- parking (de l'autre cote de la piste, sur la place)
        static void OsatParking()
        {
            Func<float, float, float> gy = (x, z) =>
            {
                RaycastHit? h = GroundHit(osat.transform.TransformPoint(new Vector3(x, 50f, z)));
                return h.HasValue ? osat.transform.InverseTransformPoint(h.Value.point).y : -0.2f;
            };
            // ([Osat] Parking=droite -- petite ville de JD, 04/11 : devant, les places tombaient de l'autre cote de la rue) : a droite du magasin vu de la rue,
            // colle a la rue, asphalte et sol solide, 6 places, muret sur les cotes et derriere les lampadaires
            if (Config.Get("Osat", "Parking", Ville.On ? "droite" : "") == "droite")
            {
                const float xa = -OL - 2f, xb = xa - 15.6f, za = 3.8f, zb = 8.8f, zf = 9.2f, zm = 1.6f, m = 0.7f;   // (zf : bord de la chaussee ; zm : muret du fond)
                HeightGrid("ki_asfaltti", xb - m, xa + m, zm, zf, 1f, gy, 0.1f);
                for (float x = xa; x >= xb - 0.01f; x -= 2.6f) Line(x, za, x, zb, gy);
                Line(xb, za, xa, za, gy);
                foreach (float x in new[] { xa - 3.9f, xb + 3.9f }) KinoLamp(new Vector3(x, gy(x, za - 1.1f), za - 1.1f));
                WallRun(xb - m, zm, xa + m, zm, gy);
                WallRun(xb - m, zm, xb - m, zf - 1.2f, gy);
                WallRun(xa + m, zm, xa + m, zf - 1.2f, gy);
                SolDur(osat.transform, xb - m, xa + m, zm, zf, 1f, gy, 0.1f);
                return;
            }
            // places perpendiculaires a la piste, 2,6 m, 5 m de profondeur, ligne du fond ; peinture seulement (place du rallye)
            float z0 = 14.5f, z1 = 19.5f;
            for (float x = -9.1f; x <= 9.11f; x += 2.6f) Line(x, z0, x, z1, gy);
            Line(-9.1f, z1, 9.1f, z1, gy);
            foreach (float x in new[] { -6f, 6f }) KinoLamp(new Vector3(x, gy(x, z1 + 1.2f), z1 + 1.2f));
        }

        // ---------------------------------------------------------------- porte vitree a deux battants coulissants, cadre blanc
        static void OsatDoors()
        {
            osatLeaves = new Transform[2];
            float zi = OD - Wall / 2f, th = 0.045f, top = 2.13f, fr = 0.07f, half = (OsatDoorHole.U1 - OsatDoorHole.U0) / 2f;
            for (int k = 0; k < 2; k++)
            {
                float sx = k == 0 ? -1f : 1f;
                var g = new GameObject("osat_ovi" + k);
                g.transform.SetParent(osat.transform, false);
                g.transform.localPosition = new Vector3(0, 0.01f, zi);
                float a = sx > 0 ? 0f : -half, b = sx > 0 ? half : 0f;
                Box(new Vector3(a, 0, -th / 2), new Vector3(b, fr, th / 2), "lista", false);
                Box(new Vector3(a, top - fr, -th / 2), new Vector3(b, top, th / 2), "lista", false);
                Box(new Vector3(a, 0, -th / 2), new Vector3(a + fr, top, th / 2), "lista", false);
                Box(new Vector3(b - fr, 0, -th / 2), new Vector3(b, top, th / 2), "lista", false);
                Box(new Vector3(a, 0.95f, -th / 2), new Vector3(b, 1.0f, th / 2), "lista", false);   // (traverse a hauteur de poignee)
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
            Vector3 c = osat.transform.TransformPoint(new Vector3(0, 1f, OD - Wall / 2f));
            if (Near(Game.PlayerT, c)) osatNear = t;
            else foreach (Avatar av in PlayerSync.Avatars) if (av != null && av.Root != null && av.Root.activeInHierarchy && Near(av.Root.transform, c)) { osatNear = t; break; }
            float target = t - osatNear < 1.2f ? 1f : 0f;
            if (osatOpen == target) return;
            osatOpen = Mathf.MoveTowards(osatOpen, target, Time.deltaTime * 1.6f);
            float e = osatOpen * osatOpen * (3f - 2f * osatOpen), slide = (OsatDoorHole.U1 - OsatDoorHole.U0) / 2f - 0.05f;
            osatLeaves[0].localPosition = new Vector3(-e * slide, 0.01f, OD - Wall / 2f);
            osatLeaves[1].localPosition = new Vector3(e * slide, 0.01f, OD - Wall / 2f);
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
            foreach (float x in new[] { -3.2f, 0f, 3.2f })
                foreach (float z in new[] { -1.6f, 1.6f }) point(new Vector3(x, OCeil - 0.2f, z), 1.3f, 6f, shop);
            point(new Vector3(0f, 2.9f, OD + 0.9f), 1.4f, 7f, new Color(1f, 0.85f, 0.65f));                    // (enseigne, perron)
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

        // ([Test] OsatKuvat=1 : 6 s apres la construction, le magasin en photo -- dehors de face et de biais, dedans depuis la porte et
        // depuis chaque aile ; dumps\photo-osat-*.png)
        static float osatShotsAt = -1f;
        static void OsatShots(float now)
        {
            if (Config.GetInt("Test", "OsatKuvat", 0) == 0 || osatShotsAt == float.MaxValue) return;
            if (osatShotsAt < 0f) { osatShotsAt = now + 6f; return; }
            if (now < osatShotsAt) return;
            osatShotsAt = float.MaxValue;
            if (Config.GetInt("Test", "OsatSonde", 0) != 0) OsatProbe();
            if (Config.GetInt("Test", "OsatTeimo", 0) != 0) OsatSignProbe();
            OsatShot("ulko", new Vector3(2.5f, 2.0f, 13f), new Vector3(0f, 2.0f, 0f), 55f);
            OsatShot("ulko2", new Vector3(-11f, 3.5f, 10f), new Vector3(0f, 1.8f, 0f), 55f);
            OsatShot("kyltti", new Vector3(0.6f, 1.8f, 8.5f), new Vector3(0f, 2.55f, OD), 40f);
            OsatShot("sisa", new Vector3(0f, 1.65f, OD - 0.4f), new Vector3(0f, 1.1f, -OD), 70f);
            OsatShot("sisa2", new Vector3(OL - 0.8f, 1.65f, -1.0f), new Vector3(-OL, 1.0f, 1.8f), 70f);
            OsatShot("sisa3", new Vector3(-OL + 0.8f, 1.65f, -1.0f), new Vector3(OL, 1.0f, 1.8f), 70f);
        }
        // ([Test] OsatSonde=1 : meubles et produits du jeu candidats -- copies posees derriere le magasin, taille, matieres, niveaux des
        // faces horizontales (tablettes) dans le repere du magasin)
        // ([Test] OsatTeimo=1 : matieres des enseignes des magasins du jeu -- liste au journal, textures dans dumps\tex-*.png)
        static void OsatSignProbe()
        {
            foreach (string rootName in new[] { "PERAPORTTI", "STORE_AREA" })
            {
                GameObject src = FindPath(rootName);
                if (src == null) { L("sonde enseigne : " + rootName + " introuvable"); continue; }
                var seen = new HashSet<string>();
                Bounds wb = new Bounds(src.transform.position, Vector3.zero);
                foreach (MeshRenderer r in src.GetComponentsInChildren<MeshRenderer>(true))
                {
                    if (r.bounds.size.magnitude < 60f) wb.Encapsulate(r.bounds);
                    foreach (Material m in r.sharedMaterials)
                    {
                        if (m == null || !seen.Add(m.name)) continue;
                        L("sonde enseigne : " + rootName + " " + r.name + " mat " + m.name + " tex " + (m.mainTexture != null ? m.mainTexture.name + " " + m.mainTexture.width + "x" + m.mainTexture.height : "-")
                          + " y " + (r.bounds.center.y - src.transform.position.y).ToString("0.0"));
                    }
                }
                L("sonde enseigne : " + rootName + " boite " + wb.center + " " + wb.size);
                int k = 0;
                foreach (Vector3 dir in new[] { Vector3.forward, Vector3.back, Vector3.left, Vector3.right })
                {
                    Vector3 from = wb.center + dir * (Mathf.Max(wb.extents.x, wb.extents.z) + 12f); from.y = wb.min.y + 3f;
                    WorldShot("teimo-" + rootName + "-" + (k++), from, wb.center, 60f);
                }
            }
            foreach (string t in Config.Get("Test", "OsatTex", "").Split(','))
                if (t.Trim().Length > 0) DumpTex(t.Trim());
        }

        static void OsatProbe()
        {
            string[] paths = { "PERAPORTTI/Building/LOD100/Store/GFX/store_shelf", "PERAPORTTI/Building/LOD100/Store/GFX/store_shelf_corner",
                               "PERAPORTTI/Building/LOD100/Store/GFX/PRODUCTS/Suomi/cardboard_stand", "PERAPORTTI/Building/LOD100/Store/GFX/magazine_stand",
                               "REPAIRSHOP/LOD/Store/Shelfs/garage_shelf_bars", "INSPECTION/Garage/ShelfTirerack/garage_shelf_bars",
                               "STORE_AREA/Stuff/LOD/GFX_Pub/store_floor", "STORE_AREA/Stuff/LOD/GFX_Pub/store_inside", "STORE_AREA/Stuff/LOD/GFX_Pub/store_woodfloor",
                               "STORE_AREA/Stuff/LOD/GFX_Pub/store_furniture", "PERAPORTTI/Building/LOD100/Store/GFX",
                               "motormoil", "coolant", "brakefluid", "atfoil", "boxtimingbelt", "boxoilpump", "sparkplugbox", "CARBATTERY", "r20battery", "Timing Belt(VINXX)" };
            float x = -8f;
            var keep = root; root = osat;
            try
            {
                foreach (string pth in paths)
                {
                    GameObject src = FindPath(pth);
                    if (src == null) { L("sonde : " + pth + " introuvable"); continue; }
                    var sb = new System.Text.StringBuilder();
                    foreach (MeshRenderer r in src.GetComponentsInChildren<MeshRenderer>(true))
                    {
                        MeshFilter mf = r.GetComponent<MeshFilter>();
                        sb.Append(" [").Append(r.name).Append(" ").Append(mf != null && mf.sharedMesh != null ? mf.sharedMesh.name : "-").Append(" :");
                        foreach (Material m in r.sharedMaterials) sb.Append(" ").Append(m != null ? m.name + "/" + (m.mainTexture != null ? m.mainTexture.name : "-") : "-");
                        sb.Append("]");
                        if (sb.Length > 900) break;
                    }
                    GameObject g = pth.Contains("store_floor") || pth.Contains("store_inside") || pth.Contains("woodfloor") || pth.EndsWith("store_furniture") || pth.EndsWith("/GFX") ? null
                                 : Clone(pth, 0, new Vector3(x, 0f, -OD - 6f), 0f, new Vector3(0, -1, 0), false, false);
                    string tiers = "";
                    if (g != null)
                    {
                        Bounds b = LocalBounds(g);
                        var lv = new Dictionary<int, float>();
                        foreach (MeshFilter mf in g.GetComponentsInChildren<MeshFilter>())
                        {
                            Mesh m = mf.sharedMesh; if (m == null || !m.isReadable) continue;
                            Vector3[] v = m.vertices; int[] tr = m.triangles;
                            for (int i = 0; i + 2 < tr.Length; i += 3)
                            {
                                Vector3 a = osat.transform.InverseTransformPoint(mf.transform.TransformPoint(v[tr[i]])), bb = osat.transform.InverseTransformPoint(mf.transform.TransformPoint(v[tr[i + 1]])), c = osat.transform.InverseTransformPoint(mf.transform.TransformPoint(v[tr[i + 2]]));
                                Vector3 nn = Vector3.Cross(bb - a, c - a); float ar = nn.magnitude / 2f; if (ar < 1e-5f) continue;
                                if (nn.normalized.y < 0.9f) continue;
                                int k = Mathf.RoundToInt((a.y + bb.y + c.y) / 3f / 0.02f); float o; lv.TryGetValue(k, out o); lv[k] = o + ar;
                            }
                        }
                        tiers = string.Join(" ", lv.Where(kv => kv.Value > 0.05f).OrderBy(kv => kv.Key).Select(kv => (kv.Key * 0.02f).ToString("F2") + "(" + kv.Value.ToString("F2") + ")").ToArray());
                        L("sonde : " + pth + " taille " + b.size.ToString("F2") + " centre " + b.center.ToString("F2") + " ; tablettes " + tiers);
                        x += Mathf.Max(1.5f, b.size.x + 0.5f);
                    }
                    L("sonde : " + pth + " :" + sb);
                }
                Flush();
            }
            finally { root = keep; }
        }

        static void OsatShot(string name, Vector3 from, Vector3 at, float fov)
        {
            WorldShot(name, osat.transform.TransformPoint(from), osat.transform.TransformPoint(at), fov);
        }
        static void WorldShot(string name, Vector3 from, Vector3 at, float fov)
        {
            var go = new GameObject("osat_kuva");
            try
            {
                var cam = go.AddComponent<Camera>(); cam.enabled = false;
                if (Game.MainCam != null) cam.CopyFrom(Game.MainCam);
                cam.fieldOfView = fov; cam.nearClipPlane = 0.05f; cam.farClipPlane = 800f;
                go.transform.position = from; go.transform.LookAt(at);
                var rt = new RenderTexture(1280, 720, 24); cam.targetTexture = rt; cam.Render();
                RenderTexture.active = rt; var tex = new Texture2D(1280, 720, TextureFormat.RGB24, false); tex.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); tex.Apply();
                RenderTexture.active = null; cam.targetTexture = null;
                string png = System.IO.Path.Combine(System.IO.Path.Combine(Log.DataDir, "dumps"), "photo-osat-" + name + ".png");
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(png));
                System.IO.File.WriteAllBytes(png, tex.EncodeToPNG());
                UnityEngine.Object.Destroy(tex); rt.Release(); UnityEngine.Object.Destroy(rt);
                L("essai : photo " + png);
            }
            catch (Exception e) { L("essai : photo " + name + " : " + e.Message); }
            UnityEngine.Object.Destroy(go);
        }

        static bool InOsat(Transform t)
        {
            if (osat == null || t == null) return false;
            Vector3 lp = osat.transform.InverseTransformPoint(t.position);
            return Mathf.Abs(lp.x) < OL - 0.2f && Mathf.Abs(lp.z) < OD - 0.2f && lp.y > -0.6f && lp.y < OCeil;
        }
    }
}
