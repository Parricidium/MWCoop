using System.Collections.Generic;
using UnityEngine;

namespace Voyage
{
    // Agence de voyage (MATKATOIMISTO) a Perajarvi et aerodrome (LENTOKENTTA) dans le champ au nord du cinema.
    // Places choisies sur releve (Essai : sonde, alentours, chercher ; 23/10) :
    //  - agence : a l'est du musee de la Corse (MWCoop), au sud de la petite route ; boulot le plus proche, le bois de
    //    HouseWood4, a 42 m ; arret de bus a 76 m ;
    //  - aerodrome : champ plat (2,4..4,4 m) entre le cinema et le radar de police du bord de route (Ratsia3Fields, -268,1578) :
    //    aucun declencheur ni automate de boulot, de course ou d'evenement a moins de 300 m, hors ce radar (sur la route,
    //    l'allee d'acces passe a 110 m). La course sur glace et le rallye sont ailleurs.
    static partial class Lieux
    {
        static readonly Vector3 AgencyAt = new Vector3(-1535f, 0f, 1080f);
        const float AgencyYaw = 0f;            // (facade au nord, vers la petite route)
        static Vector3 TerminalAt = new Vector3(-380f, 0f, 1668f);
        static float TerminalYaw = 180f;       // (facade au sud, vers la route ; piste au nord)
        public const float RunwayZ = 1745f;
        // repere de la piste (cinematiques de l'avion) : origine, sens du decollage, abscisses du depart et de l'approche
        public static Vector3 RunO = new Vector3(0f, 0f, RunwayZ), RunDir = Vector3.right;
        public static float StartU = -470f, ApproachU = -760f;
        const float RunwayX0 = -530f, RunwayX1 = -230f, RunwayHalfW = 10f;
        public static Vector3 CineDepart, CineArrivee;   // (cameras de cinema, bord sud de la piste)

        // ---------------------------------------------------------------- sol
        // hauteur du sol (premier obstacle solide sous le ciel, hors nos propres constructions)
        public static float GroundY(float x, float z, float fallback = 3f)
        {
            RaycastHit[] hs = Physics.RaycastAll(new Vector3(x, 600f, z), Vector3.down, 1200f);
            float best = float.MaxValue, y = fallback;
            foreach (RaycastHit h in hs)
            {
                string root = h.collider.transform.root.name;
                if (h.collider.isTrigger || root.StartsWith("VOYAGE") && !root.StartsWith("VOYAGE_DESTINATION")) continue;   // (nos constructions, sauf le sol d'un circuit)
                if (h.distance < best) { best = h.distance; y = h.point.y; }
            }
            return y;
        }

        // sol sous l'emprise d'un batiment (centre, cap, demi-largeur, demi-profondeur) : bas et haut
        static void Footprint(Vector3 c, float yaw, float hw, float hd, out float lo, out float hi)
        {
            lo = float.MaxValue; hi = float.MinValue;
            Quaternion r = Quaternion.Euler(0f, yaw, 0f);
            for (int i = -2; i <= 2; i++)
                for (int j = -2; j <= 2; j++)
                {
                    Vector3 p = c + r * new Vector3(hw * i / 2f, 0f, hd * j / 2f);
                    float y = GroundY(p.x, p.z);
                    lo = Mathf.Min(lo, y); hi = Mathf.Max(hi, y);
                }
        }

        // surface posee sur le sol (allee, parking, aire) : rectangle monde x0..x1, z0..z1, quadrillage de 'step' m
        static void Drape(Construction c, string key, float x0, float z0, float x1, float z1, float step, float lift = 0.1f, bool alongZ = false)
        {
            int nx = Mathf.Max(1, Mathf.CeilToInt((x1 - x0) / step)), nz = Mathf.Max(1, Mathf.CeilToInt((z1 - z0) / step));
            var h = new float[nx + 1, nz + 1];
            for (int i = 0; i <= nx; i++) for (int j = 0; j <= nz; j++) h[i, j] = GroundY(x0 + (x1 - x0) * i / nx, z0 + (z1 - z0) * j / nz) + lift;
            Vector3 o = c.Root.transform.position;
            for (int i = 0; i < nx; i++)
                for (int j = 0; j < nz; j++)
                {
                    float xa = x0 + (x1 - x0) * i / nx, xb = x0 + (x1 - x0) * (i + 1) / nx, za = z0 + (z1 - z0) * j / nz, zb = z0 + (z1 - z0) * (j + 1) / nz;
                    Vector3 p00 = new Vector3(xa, h[i, j], za) - o, p10 = new Vector3(xb, h[i + 1, j], za) - o, p11 = new Vector3(xb, h[i + 1, j + 1], zb) - o, p01 = new Vector3(xa, h[i, j + 1], zb) - o;
                    // (premier cote = sens des traces de la texture de route : le long de x, ou de z pour une allee nord-sud)
                    if (alongZ) c.Quad(key, p10, p11, p01, p00); else c.Quad(key, p00, p10, p11, p01);
                }
        }

        // ---------------------------------------------------------------- murs
        struct Hole { public float U0, U1, Y0, Y1; public Hole(float u0, float u1, float y0, float y1) { U0 = u0; U1 = u1; Y0 = y0; Y1 = y1; } }

        // mur le long de x (face exterieure en z = outer, l'interieur vers -sign(outer)), decoupe autour des ouvertures
        static void WallX(Construction c, float x0, float x1, float outer, float t, float h, string outside, string inside, params Hole[] holes)
        {
            float inner = outer - Mathf.Sign(outer) * t;
            float zmin = Mathf.Min(outer, inner), zmax = Mathf.Max(outer, inner);
            var cuts = new List<float> { x0, x1 };
            foreach (Hole o in holes) { cuts.Add(o.U0); cuts.Add(o.U1); }
            cuts.Sort();
            for (int i = 0; i + 1 < cuts.Count; i++)
            {
                float a = cuts[i], b = cuts[i + 1];
                if (b - a < 0.01f) continue;
                float m = (a + b) / 2f;
                var spans = new List<float> { 0f, h };
                foreach (Hole o in holes) if (m > o.U0 && m < o.U1) { spans.Add(o.Y0); spans.Add(o.Y1); }
                spans.Sort();
                for (int k = 0; k + 1 < spans.Count; k += 2)
                {
                    if (spans[k + 1] - spans[k] < 0.01f) continue;
                    c.Box(new Vector3(a, spans[k], zmin), new Vector3(b, spans[k + 1], zmax),
                          null, null, null, null, outer > 0 ? outside : inside, outer > 0 ? inside : outside);
                }
            }
            // embrasures (tableaux des ouvertures)
            foreach (Hole o in holes)
            {
                c.Box(new Vector3(o.U0 - 0.02f, o.Y0, zmin), new Vector3(o.U0, o.Y1, zmax), "lista", false);
                c.Box(new Vector3(o.U1, o.Y0, zmin), new Vector3(o.U1 + 0.02f, o.Y1, zmax), "lista", false);
                if (o.Y0 > 0.01f) c.Box(new Vector3(o.U0, o.Y0 - 0.02f, zmin - 0.05f), new Vector3(o.U1, o.Y0, zmax + 0.05f), "lista", false);
                c.Box(new Vector3(o.U0, o.Y1, zmin), new Vector3(o.U1, o.Y1 + 0.02f, zmax), "lista", false);
            }
        }

        // mur le long de z (pignon) plein
        static void WallZ(Construction c, float z0, float z1, float outer, float t, float h, string outside, string inside)
        {
            float inner = outer - Mathf.Sign(outer) * t;
            c.Box(new Vector3(Mathf.Min(outer, inner), 0f, z0), new Vector3(Mathf.Max(outer, inner), h, z1), outer > 0 ? outside : inside, outer > 0 ? inside : outside, null, null, null, null);
        }

        static void Pane(Construction c, float x0, float x1, float y0, float y1, float z) { c.Quad("lasi", new Vector3(x1, y0, z), new Vector3(x0, y0, z), new Vector3(x0, y1, z), new Vector3(x1, y1, z)); c.Quad("lasi", new Vector3(x0, y0, z), new Vector3(x1, y0, z), new Vector3(x1, y1, z), new Vector3(x0, y1, z)); }

        // toit a deux pans (faitage le long de x), neige dessus
        static void GableRoof(Construction c, float hw, float hd, float eave, float rise, float over)
        {
            float slope = Mathf.Atan2(rise, hd) * Mathf.Rad2Deg, len = Mathf.Sqrt(rise * rise + hd * hd) + over;
            foreach (float s in new[] { 1f, -1f })
            {
                Quaternion q = Quaternion.Euler(s * slope, 0f, 0f);
                Vector3 mid = new Vector3(0f, eave + rise / 2f, s * hd / 2f) + q * new Vector3(0f, 0.08f, s * over / 2f);
                c.OBox(mid, q, new Vector3(2f * hw + 2f * over, 0.12f, len), "lumi", "sisakatto", "lista");
            }
            // pignons triangulaires
            foreach (float x in new[] { -hw, hw })
            {
                float sx = Mathf.Sign(x);
                Vector3 a = new Vector3(x, eave, -hd), b = new Vector3(x, eave, hd), t = new Vector3(x, eave + rise, 0f);
                if (sx > 0) { c.Tri("lauta", a, t, b); c.Tri("sisaseina", b, t, a); } else { c.Tri("lauta", b, t, a); c.Tri("sisaseina", a, t, b); }
            }
        }

        static void CommonMats(Construction c)
        {
            c.Mat("lauta", "wood_panel7", 2.2f);
            c.Mat("lista", "wood_panel20", 1.2f);
            c.Mat("kivi", "rock_sokkeli", 2.0f);
            c.Mat("lumi", "SNOW", 3f);
            c.Mat("lattia", "wood_panel19", 1.2f, new Color(0.85f, 0.72f, 0.58f));
            c.Mat("sisaseina", "wallpaper1", 1.1f);
            c.Mat("sisakatto", "wood_panel2", 1.6f);
            c.Mat("tiski", "wood12", 1.0f);
            c.Mat("metalli", "metal_galvanized", 1f, new Color(0.9f, 0.9f, 0.88f));
            c.Mat("matto", "carpet1", 1f);
            c.Glass("lasi", new Color(0.78f, 0.86f, 0.9f, 0.18f));
            c.Color("kyltti", new Color(0.12f, 0.27f, 0.55f));
            c.Color("valkoinen", new Color(0.93f, 0.93f, 0.9f));
            c.Color("keltainen", new Color(0.95f, 0.78f, 0.15f));
            c.Color("musta", new Color(0.08f, 0.08f, 0.09f));
            c.Color("punainen", new Color(0.75f, 0.1f, 0.08f));
            c.Color("pnj_corps", new Color(0.2f, 0.3f, 0.55f));
            c.Color("pnj_tete", new Color(0.86f, 0.7f, 0.58f));
        }

        // ---------------------------------------------------------------- agence de voyage (Perajarvi)
        static void Agency()
        {
            const float hw = 3.5f, hd = 2.75f, h = 3.0f, t = 0.15f;
            float lo, hi;
            Footprint(AgencyAt, AgencyYaw, hw + 0.5f, hd + 0.5f, out lo, out hi);
            float floor = hi + 0.25f;
            var c = new Construction("VOYAGE_AGENCE", new Vector3(AgencyAt.x, floor, AgencyAt.z), AgencyYaw);
            Agence = c.Root;
            CommonMats(c);
            // soubassement, plancher, perron
            c.Box(new Vector3(-hw - 0.1f, lo - floor - 0.3f, -hd - 0.1f), new Vector3(hw + 0.1f, -0.12f, hd + 0.1f), "kivi");
            c.Box(new Vector3(-hw, -0.12f, -hd), new Vector3(hw, 0f, hd), "lattia");
            for (int s = 0; s < 3; s++)
            {
                float y = -0.12f - s * 0.15f - (floor - hi) * s / 3f;
                c.Box(new Vector3(-1.0f, lo - floor - 0.3f, hd + 0.1f + s * 0.3f), new Vector3(1.0f, y, hd + 0.4f + s * 0.3f), "kivi");
            }
            // murs : facade (porte, deux vitrines), arriere (une fenetre), pignons
            WallX(c, -hw, hw, hd, t, h, "lauta", "sisaseina", new Hole(-0.6f, 0.6f, 0f, 2.15f), new Hole(-3.0f, -1.2f, 0.85f, 2.2f), new Hole(1.2f, 3.0f, 0.85f, 2.2f));
            Pane(c, -3.0f, -1.2f, 0.85f, 2.2f, hd - t / 2f);
            Pane(c, 1.2f, 3.0f, 0.85f, 2.2f, hd - t / 2f);
            WallX(c, -hw, hw, -hd, t, h, "lauta", "sisaseina", new Hole(2.0f, 3.0f, 1.0f, 2.1f));
            Pane(c, 2.0f, 3.0f, 1.0f, 2.1f, -hd + t / 2f);
            WallZ(c, -hd, hd, hw, t, h, "lauta", "sisaseina");
            WallZ(c, -hd, hd, -hw, t, h, "lauta", "sisaseina");
            c.Box(new Vector3(-hw, h - 0.02f, -hd), new Vector3(hw, h, hd), null, null, null, "sisakatto", null, null, false);
            GableRoof(c, hw, hd, h, 1.6f, 0.45f);
            // enseigne au-dessus de la porte
            c.Box(new Vector3(-2.4f, 2.3f, hd), new Vector3(2.4f, 2.85f, hd + 0.06f), "kyltti", false);
            c.Text("MATKATOIMISTO · VOYAGES", new Vector3(0f, 2.575f, hd + 0.07f), 180f, 0.11f, new Color(1f, 0.9f, 0.35f));
            // interieur : comptoir en travers, guichetiere derriere, affiches
            c.Box(new Vector3(-2.2f, 0f, -0.55f), new Vector3(2.2f, 1.05f, -0.05f), "tiski");
            c.Box(new Vector3(-2.3f, 1.05f, -0.6f), new Vector3(2.3f, 1.09f, 0.05f), "musta");
            c.Box(new Vector3(-2.0f, 0.01f, 0.4f), new Vector3(2.0f, 0.02f, 2.2f), "matto", false);
            Npc(c, "VOYAGE-agence", new Vector3(0.3f, 0f, -1.4f), 0f);
            AddSpot(Kind.Counter, c.Root.transform, new Vector3(0f, 1.25f, -0.25f), new Vector3(4.4f, 0.8f, 0.8f));
            Board(c, hw, hd, t);
            c.Lamp(new Vector3(0f, 2.6f, 0.5f), new Color(1f, 0.82f, 0.6f), 1.2f, 6f);
            c.Lamp(new Vector3(0f, 2.6f, hd + 0.9f), new Color(1f, 0.85f, 0.65f), 0.8f, 4f);
            c.Flush();
            Journal.Info(string.Format(System.Globalization.CultureInfo.InvariantCulture, "agence construite en {0:F1},{1:F2},{2:F1} (sol {3:F2}..{4:F2})", AgencyAt.x, floor, AgencyAt.z, lo, hi));
        }

        // ---------------------------------------------------------------- tableau des destinations de l'agence
        // Une affiche par circuit converti (8 au plus) : 4 au mur du fond (derriere le comptoir), 2 sur chaque mur de cote
        // (cote clients) ; viser une affiche et cliquer achete le billet. Sans circuit : une affiche qui explique quoi faire.
        static readonly Color[] Teintes = { new Color(0.1f, 0.45f, 0.7f), new Color(0.8f, 0.4f, 0.12f), new Color(0.18f, 0.5f, 0.28f), new Color(0.6f, 0.15f, 0.2f),
                                            new Color(0.35f, 0.25f, 0.6f), new Color(0.12f, 0.5f, 0.5f), new Color(0.7f, 0.55f, 0.1f), new Color(0.3f, 0.3f, 0.35f) };

        static void Board(Construction c, float hw, float hd, float t)
        {
            var places = new List<KeyValuePair<Vector3, float>>();
            foreach (float x in new[] { -2.6f, -1.4f, -0.2f, 1.0f }) places.Add(new KeyValuePair<Vector3, float>(new Vector3(x, 1.75f, -hd + t + 0.02f), 0f));
            foreach (float z in new[] { 0.7f, 1.9f }) { places.Add(new KeyValuePair<Vector3, float>(new Vector3(-hw + t + 0.02f, 1.75f, z), 90f)); places.Add(new KeyValuePair<Vector3, float>(new Vector3(hw - t - 0.02f, 1.75f, z), -90f)); }
            var all = Destinations.All;
            if (all.Count == 0)
            {
                Poster(c, places[1].Key + new Vector3(0.6f, 0f, 0f), 0f, Jeu.T("AUCUNE DESTINATION", "NO DESTINATION", "НЕТ НАПРАВЛЕНИЙ"), Jeu.T("Convertis un circuit", "Convert a track", "Конвертируй трассу"),
                       Jeu.T("avec le convertisseur Voyage", "with the Voyage converter", "конвертером Voyage"), new Color(0.3f, 0.3f, 0.33f), null, 2.2f);
                return;
            }
            for (int k = 0; k < all.Count && k < places.Count; k++)
            {
                Destination d = all[k];
                bool more = k == places.Count - 1 && all.Count > places.Count;
                if (more) { Poster(c, places[k].Key, places[k].Value, "+" + (all.Count - k), Jeu.T("autres destinations", "more destinations", "направлений ещё"), Jeu.T("au comptoir", "at the counter", "у стойки"), Teintes[k % Teintes.Length], null, 1.1f); break; }
                Poster(c, places[k].Key, places[k].Value, d.Name.ToUpperInvariant(), d.Country, d.Price + " MK", Teintes[k % Teintes.Length], d, 1.1f);
            }
        }

        // affiche tournee (yaw local : 0 = regarde +z), son texte et sa visee
        static void Poster(Construction c, Vector3 at, float yaw, string line1, string line2, string line3, Color bg, Destination d, float w)
        {
            var p = new Construction("voyage_affiche", c.Root.transform.TransformPoint(at), c.Root.transform.eulerAngles.y + yaw, c.Root.transform);
            p.Color("fond", bg);
            p.Color("cadre", new Color(0.92f, 0.9f, 0.85f));
            p.Box(new Vector3(-w / 2 - 0.04f, -0.79f, -0.01f), new Vector3(w / 2 + 0.04f, 0.79f, 0.0f), "cadre", false);
            p.Box(new Vector3(-w / 2, -0.75f, 0f), new Vector3(w / 2, 0.75f, 0.02f), "fond", false);
            float size = Mathf.Clamp(0.095f * 10f / Mathf.Max(10, line1.Length), 0.045f, 0.095f) * (w / 1.1f);
            p.Text(line1, new Vector3(0f, 0.42f, 0.03f), 180f, size, Color.white);
            if (!string.IsNullOrEmpty(line2)) p.Text(line2, new Vector3(0f, 0.05f, 0.03f), 180f, 0.06f, new Color(1f, 0.95f, 0.8f));
            if (!string.IsNullOrEmpty(line3)) p.Text(line3, new Vector3(0f, -0.42f, 0.03f), 180f, 0.075f, new Color(1f, 0.9f, 0.45f));
            p.Flush();
            if (d != null) AddSpot(Kind.Counter, p.Root.transform, new Vector3(0f, 0f, 0.15f), new Vector3(w + 0.1f, 1.5f, 0.3f), d, 4.6f);
        }

        // affiche : panneau de couleur, deux lignes de texte (regarde +z local)
        static void Poster(Construction c, Vector3 at, string line1, string line2, Color bg)
        {
            string key = "affiche_" + line1.GetHashCode();
            c.Color(key, bg);
            c.Box(at + new Vector3(-0.55f, -0.75f, 0f), at + new Vector3(0.55f, 0.75f, 0.02f), key, false);
            c.Text(line1, at + new Vector3(0f, 0.3f, 0.03f), 180f, 0.09f, Color.white);
            c.Text(line2, at + new Vector3(0f, -0.25f, 0.03f), 180f, 0.07f, new Color(1f, 0.95f, 0.7f));
        }

        // ---------------------------------------------------------------- aerodrome (champ du cinema)
        static void Airfield()
        {
            var root = new GameObject("VOYAGE_AERODROME");
            Aerodrome = root;
            // surfaces au sol, coordonnees monde (racine a l'origine)
            var g = new Construction("VOYAGE_AERODROME_SOL", Vector3.zero, 0f, root.transform);
            // route enneigee du jeu, comme les allees du cinema de MWCoop (traces dans le sens de circulation ; JD, 23/10)
            g.Mat("asfaltti", "mwc_asphalt", 7f);
            g.Mat("betoni", "mwc_asphalt", 7f);
            g.Color("viiva", new Color(0.92f, 0.92f, 0.9f));
            g.Color("keltainen", new Color(0.95f, 0.78f, 0.15f));
            Drape(g, "asfaltti", RunwayX0, RunwayZ - RunwayHalfW, RunwayX1, RunwayZ + RunwayHalfW, 5f, 0.12f);
            Drape(g, "betoni", -412f, 1673f, -348f, RunwayZ - RunwayHalfW + 0.5f, 4f, 0.11f, true);       // aire de stationnement jusqu'a la piste
            Drape(g, "asfaltti", -384f, 1594f, -376f, 1664f, 4f, 0.1f, true);                           // allee depuis la route
            Drape(g, "asfaltti", -410f, 1628f, -384f, 1652f, 4f, 0.1f, true);                           // parking
            // marquages de la piste : axe en tirets, seuils, numeros
            for (float x = RunwayX0 + 30f; x < RunwayX1 - 30f; x += 18f) Mark(g, x, RunwayZ, 9f, 0.45f);
            foreach (float xe in new[] { RunwayX0 + 6f, RunwayX1 - 6f })
                for (int k = -3; k <= 3; k++) if (k != 0) Mark(g, xe, RunwayZ + k * 2.4f, 10f, 1.2f, true);
            for (float x = -410f; x <= -386f; x += 3f) Mark(g, x, 1640f, 0.12f, 5f);              // places de parking
            // feux de bord de piste
            for (float x = RunwayX0; x <= RunwayX1 + 0.1f; x += 30f)
                foreach (float z in new[] { RunwayZ - RunwayHalfW - 0.6f, RunwayZ + RunwayHalfW + 0.6f })
                {
                    float y = GroundY(x, z);
                    g.Box(new Vector3(x - 0.12f, y, z - 0.12f), new Vector3(x + 0.12f, y + 0.35f, z + 0.12f), "keltainen", false);
                }
            // grillages (JD, 23/10) : piste et manche a air enclos, ouverts sur l'aire ; aire fermee de chaque cote, le terminal
            // faisant le passage entre le cote route et le cote piste ; parking sur trois cotes, ouvert sur l'allee
            g.ChainLink("verkko", 0.5f);
            g.Mat("tolppa", "metal_galvanized", 1f, new Color(0.82f, 0.83f, 0.85f));
            const float fx0 = -545f, fx1 = -215f, fz0 = 1722f, fz1 = 1778f;
            Fence(g, fx0, fz0, -412f, fz0); Fence(g, -348f, fz0, fx1, fz0);   // (sud, ouverture de l'aire)
            Fence(g, fx1, fz0, fx1, fz1); Fence(g, fx1, fz1, fx0, fz1); Fence(g, fx0, fz1, fx0, fz0);
            Fence(g, -416f, 1668f, -416f, fz0); Fence(g, -344f, 1668f, -344f, fz0);   // (cotes de l'aire)
            Fence(g, -416f, 1668f, -386.2f, 1668f); Fence(g, -373.8f, 1668f, -344f, 1668f);   // (jusqu'aux pignons du terminal)
            Fence(g, -383f, 1626f, -412f, 1626f); Fence(g, -412f, 1626f, -412f, 1654f); Fence(g, -412f, 1654f, -386f, 1654f);   // (parking)
            Fence(g, -416f, 1722f, -412f, 1722f); Fence(g, -348f, 1722f, -344f, 1722f);   // (raccords de l'aire a la cloture de la piste)
            g.Flush();
            RunwayNumber(root, "09", new Vector3(RunwayX0 + 20f, 0f, RunwayZ), 90f);
            RunwayNumber(root, "27", new Vector3(RunwayX1 - 20f, 0f, RunwayZ), -90f);
            Terminal(root.transform);
            Cessna(root.transform, new Vector3(-390f, 0f, 1712f), 90f);
            CineDepart = new Vector3(-300f, GroundY(-300f, RunwayZ - 16f) + 2.2f, RunwayZ - 16f);
            CineArrivee = new Vector3(-430f, GroundY(-430f, RunwayZ - 16f) + 2.2f, RunwayZ - 16f);
            Windsock(root.transform, new Vector3(-250f, 0f, 1770f));
            RoadSign(root.transform, new Vector3(-371f, 0f, 1598f));
            Journal.Info("aerodrome construit (piste " + RunwayX0 + ".." + RunwayX1 + " a z " + RunwayZ + ")");
        }

        // ---------------------------------------------------------------- My Summer Car : l'ancien aeroport du jeu (JD, 24/10)
        // MAP/MESH/AIRPORT : piste de 26,5 m de large au cap 278 degres (axe mesure dans le maillage), bout est vers (-277, -1080) ;
        // les dragsters du vendredi n'utilisent que l'ouest (depart x -1278, arrivee x -880, tentes x -1304 et -739). Ici, rien
        // a dessiner au sol : le terminal et le Cessna dans la boucle du taxiway au nord du bout est (epave remorquable
        // Spawn/JunkCar1 a 150 m, tente des resultats a 260 m). Repere de piste : u le long de l'axe vers l'ouest (0 a 28 m
        // du bout est), v vers le nord (cote taxiway).
        static Vector3 Rw(float u, float v) { Vector3 n = new Vector3(-RunDir.z, 0f, RunDir.x); return RunO + RunDir * u - n * v; }

        static void AirfieldMsc()
        {
            var root = new GameObject("VOYAGE_AERODROME");
            Aerodrome = root;
            RunO = new Vector3(-304.2f, 0f, -1075.5f); RunDir = new Vector3(-0.9912f, 0f, 0.1323f).normalized;
            StartU = 10f; ApproachU = -240f;   // (toucher vers u 45, arret vers u 120)
            float north = Mathf.Atan2(RunDir.z, -RunDir.x) * Mathf.Rad2Deg;   // (cap de la normale cote nord : facade vers le taxiway)
            TerminalAt = Rw(70f, 70f); TerminalYaw = north;
            Terminal(root.transform);
            Cessna(root.transform, Rw(70f, 35f), Mathf.Atan2(RunDir.x, RunDir.z) * Mathf.Rad2Deg);
            Vector3 cd = Rw(170f, -22f), ca = Rw(25f, -22f);
            CineDepart = new Vector3(cd.x, GroundY(cd.x, cd.z) + 2.2f, cd.z);
            CineArrivee = new Vector3(ca.x, GroundY(ca.x, ca.z) + 2.2f, ca.z);
            Windsock(root.transform, Rw(-10f, -32f));
            RoadSign(root.transform, Rw(70f, 98f));
            Journal.Info("aerodrome construit (ancien aeroport de My Summer Car, terminal en " + TerminalAt + ")");
        }

        // grillage de (x0, z0) a (x1, z1), monde : poteaux tous les 3 m qui suivent le sol, panneaux de maille des deux
        // cotes, lisse en haut, collision par panneau (les clotures sont alignees sur x ou z)
        const float FenceH = 2.0f, PostStep = 3f, Mesh = 0.12f;   // (Mesh : une maille de 12 cm ; 25 cm trop grande, JD 23/10)
        static void Fence(Construction g, float x0, float z0, float x1, float z1)
        {
            Vector3 a = new Vector3(x0, 0f, z0), b = new Vector3(x1, 0f, z1);
            float len = Vector3.Distance(a, b);
            if (len < 0.1f) return;
            int n = Mathf.Max(1, Mathf.RoundToInt(len / PostStep));
            Vector3 dir = (b - a) / len;
            var foot = new Vector3[n + 1];
            for (int i = 0; i <= n; i++) { Vector3 p = Vector3.Lerp(a, b, i / (float)n); p.y = GroundY(p.x, p.z) - 0.05f; foot[i] = p; }
            for (int i = 0; i <= n; i++)
            {
                Vector3 f = foot[i];
                g.Box(f + new Vector3(-0.035f, 0f, -0.035f), f + new Vector3(0.035f, FenceH + 0.1f, 0.035f), "tolppa", false);
            }
            for (int i = 0; i < n; i++)
            {
                Vector3 p = foot[i], q = foot[i + 1], up = Vector3.up * FenceH;
                // (UV le long de la cloture et en hauteur : mailles a l'endroit, a cheval sur les poteaux)
                float u0 = Vector3.Distance(a, p) / Mesh, u1 = Vector3.Distance(a, q) / Mesh, v = FenceH / Mesh;
                Vector2[] uvA = { new Vector2(u0, 0f), new Vector2(u1, 0f), new Vector2(u1, v), new Vector2(u0, v) };
                Vector2[] uvB = { new Vector2(u1, 0f), new Vector2(u0, 0f), new Vector2(u0, v), new Vector2(u1, v) };
                Vector3 lift = Vector3.up * 0.06f;
                g.Quad("verkko", p + lift, q + lift, q + up, p + up, uvA);
                g.Quad("verkko", q + lift, p + lift, p + up, q + up, uvB);
                // lisse du haut
                Vector3 s = Vector3.Cross(Vector3.up, dir) * 0.025f;
                g.Quad("tolppa", p + up + s, q + up + s, q + up - s, p + up - s);
                g.Quad("tolppa", p + up - s + Vector3.up * 0.05f, q + up - s + Vector3.up * 0.05f, q + up + s + Vector3.up * 0.05f, p + up + s + Vector3.up * 0.05f);
                // collision : boite fine le long du panneau
                Vector3 mn = Vector3.Min(p, q) - new Vector3(Mathf.Abs(dir.z) * 0.06f, 0f, Mathf.Abs(dir.x) * 0.06f);
                Vector3 mx = Vector3.Max(p, q) + new Vector3(Mathf.Abs(dir.z) * 0.06f, 0f, Mathf.Abs(dir.x) * 0.06f);
                mn.y = Mathf.Min(p.y, q.y) - 0.3f; mx.y = Mathf.Max(p.y, q.y) + FenceH;
                g.Collider(mn, mx);
            }
        }

        static void Mark(Construction g, float x, float z, float lx, float lz, bool alongX = true)
        {
            float y = GroundY(x, z) + 0.16f;
            float hx = lx / 2f, hz = lz / 2f;
            g.Quad("viiva", new Vector3(x - hx, y, z - hz), new Vector3(x + hx, y, z - hz), new Vector3(x + hx, y, z + hz), new Vector3(x - hx, y, z + hz));
        }

        static void RunwayNumber(GameObject root, string text, Vector3 at, float yaw)
        {
            at.y = GroundY(at.x, at.z) + 0.17f;
            var c = new Construction("VOYAGE_NUMERO_" + text, at, 0f, root.transform);
            var holder = new GameObject("n");
            holder.transform.SetParent(c.Root.transform, false);
            c.Text(text, Vector3.zero, 0f, 1.6f, new Color(0.93f, 0.93f, 0.9f));
            Transform tx = c.Root.transform.GetChild(c.Root.transform.childCount - 1);
            tx.localRotation = Quaternion.Euler(90f, yaw, 0f);
        }

        // terminal : hall vitre cote route, guichet d'enregistrement, banc, tableau des departs, petite tour de controle
        static void Terminal(Transform parent)
        {
            const float hw = 6f, hd = 4f, h = 3.2f, t = 0.18f;
            float lo, hi;
            Footprint(TerminalAt, TerminalYaw, hw + 0.5f, hd + 0.5f, out lo, out hi);
            float floor = hi + 0.2f;
            var c = new Construction("VOYAGE_TERMINAL", new Vector3(TerminalAt.x, floor, TerminalAt.z), TerminalYaw, parent);
            TerminalGo = c.Root;
            CommonMats(c);
            c.Mat("lauta", "wood_panel20", 2.2f);   // (terminal : bardage blanc)
            c.Box(new Vector3(-hw - 0.1f, lo - floor - 0.3f, -hd - 0.1f), new Vector3(hw + 0.1f, -0.1f, hd + 0.1f), "kivi");
            c.Box(new Vector3(-hw, -0.1f, -hd), new Vector3(hw, 0f, hd), "lattia");
            c.Box(new Vector3(-2f, lo - floor - 0.3f, hd + 0.1f), new Vector3(2f, -0.1f, hd + 1.6f), "kivi");   // (rampe d'entree)
            c.Box(new Vector3(-2f, lo - floor - 0.3f, -hd - 1.6f), new Vector3(2f, -0.1f, -hd - 0.1f), "kivi");
            // facade cote route : portes vitrees et grandes baies
            WallX(c, -hw, hw, hd, t, h, "lauta", "sisaseina", new Hole(-1.0f, 1.0f, 0f, 2.3f), new Hole(-5.4f, -1.6f, 0.7f, 2.6f), new Hole(1.6f, 5.4f, 0.7f, 2.6f));
            Pane(c, -5.4f, -1.6f, 0.7f, 2.6f, hd - t / 2f);
            Pane(c, 1.6f, 5.4f, 0.7f, 2.6f, hd - t / 2f);
            // cote piste : porte vers l'avion, baie
            WallX(c, -hw, hw, -hd, t, h, "lauta", "sisaseina", new Hole(-1.0f, 1.0f, 0f, 2.3f), new Hole(1.8f, 5.2f, 0.9f, 2.5f));
            Pane(c, 1.8f, 5.2f, 0.9f, 2.5f, -hd + t / 2f);
            WallZ(c, -hd, hd, hw, t, h, "lauta", "sisaseina");
            WallZ(c, -hd, hd, -hw, t, h, "lauta", "sisaseina");
            c.Box(new Vector3(-hw, h - 0.02f, -hd), new Vector3(hw, h, hd), null, null, null, "sisakatto", null, null, false);
            // toit plat enneige, debord
            c.Box(new Vector3(-hw - 0.4f, h, -hd - 0.4f), new Vector3(hw + 0.4f, h + 0.25f, hd + 0.4f), "lista", "lista", "lumi", "lista", "lista", "lista");
            // enseigne
            c.Box(new Vector3(-3.6f, h + 0.25f, hd + 0.2f), new Vector3(3.6f, h + 1.0f, hd + 0.3f), "kyltti", false);
            c.Text("LENTOKENTTÄ · AÉRODROME", new Vector3(0f, h + 0.62f, hd + 0.31f), 180f, 0.16f, new Color(1f, 0.9f, 0.35f));
            // tour de controle sur le coin ouest
            float tx = -hw + 1.3f, tz = -hd + 1.3f;
            c.Box(new Vector3(tx - 1.1f, h + 0.25f, tz - 1.1f), new Vector3(tx + 1.1f, h + 2.2f, tz + 1.1f), "lauta");
            c.Box(new Vector3(tx - 1.3f, h + 2.2f, tz - 1.3f), new Vector3(tx + 1.3f, h + 2.3f, tz + 1.3f), "lista");
            foreach (float s in new[] { -1f, 1f }) { Pane(c, tx - 1.25f, tx + 1.25f, h + 2.3f, h + 3.3f, tz + s * 1.25f); }
            c.Quad("lasi", new Vector3(tx + 1.25f, h + 2.3f, tz - 1.25f), new Vector3(tx + 1.25f, h + 2.3f, tz + 1.25f), new Vector3(tx + 1.25f, h + 3.3f, tz + 1.25f), new Vector3(tx + 1.25f, h + 3.3f, tz - 1.25f));
            c.Quad("lasi", new Vector3(tx - 1.25f, h + 2.3f, tz + 1.25f), new Vector3(tx - 1.25f, h + 2.3f, tz - 1.25f), new Vector3(tx - 1.25f, h + 3.3f, tz - 1.25f), new Vector3(tx - 1.25f, h + 3.3f, tz + 1.25f));
            c.Box(new Vector3(tx - 1.45f, h + 3.3f, tz - 1.45f), new Vector3(tx + 1.45f, h + 3.5f, tz + 1.45f), "lista", "lista", "lumi", "lista", "lista", "lista");
            c.Cylinder("metalli", new Vector3(tx, h + 3.5f, tz), 0.03f, 0.02f, 2.2f, 6);
            // guichet d'enregistrement (cote est), guichetier derriere, tapis bagages
            c.Box(new Vector3(1.4f, 0f, -1.4f), new Vector3(4.6f, 1.05f, -0.9f), "tiski");
            c.Box(new Vector3(1.3f, 1.05f, -1.45f), new Vector3(4.7f, 1.09f, -0.85f), "musta");
            c.Box(new Vector3(4.6f, 0f, -1.4f), new Vector3(5.6f, 0.45f, 0.4f), "metalli");
            c.Box(new Vector3(1.6f, 2.2f, -1.2f), new Vector3(4.4f, 2.7f, -1.15f), "kyltti", false);
            c.Text("CHECK-IN · LÄHTÖSELVITYS", new Vector3(3.0f, 2.45f, -1.1f), 180f, 0.07f, Color.white);
            Npc(c, "VOYAGE-aerodrome", new Vector3(3.0f, 0f, -2.2f), 0f);
            AddSpot(Kind.Airfield, c.Root.transform, new Vector3(3.0f, 1.2f, -1.6f), new Vector3(1.4f, 1.9f, 1.4f));
            // tableau des departs, bancs, tapis
            c.Box(new Vector3(-4.6f, 1.7f, -hd + t), new Vector3(-1.6f, 2.6f, -hd + t + 0.06f), "musta", false);
            c.Text(Jeu.T("DÉPARTS · LÄHTEVÄT", "DEPARTURES · LÄHTEVÄT", "ВЫЛЕТЫ · LÄHTEVÄT"), new Vector3(-3.1f, 2.42f, -hd + t + 0.07f), 180f, 0.1f, new Color(1f, 0.8f, 0.2f));
            c.Text(Jeu.T("Cessna 172 : modèle 3D de osmosikum (Sketchfab), CC BY 4.0", "Cessna 172: 3D model by osmosikum (Sketchfab), CC BY 4.0", "Cessna 172: 3D-модель osmosikum (Sketchfab), CC BY 4.0"), new Vector3(-3.1f, 1.5f, -hd + t + 0.02f), 180f, 0.05f, new Color(0.25f, 0.22f, 0.2f));
            for (int k = 0; k < 3; k++)   // (trois premieres destinations ; sans circuit converti, des tirets)
            {
                string l = k < Destinations.All.Count ? Destinations.All[k].Name.ToUpperInvariant() : "---";
                if (l.Length > 16) l = l.Substring(0, 15) + ".";
                c.Text(l.PadRight(17) + (k < Destinations.All.Count ? Jeu.T("À L'HEURE", "ON TIME", "ПО ПЛАНУ") : ""), new Vector3(-3.1f, 2.14f - k * 0.2f, -hd + t + 0.07f), 180f, 0.065f, new Color(1f, 0.8f, 0.2f));
            }
            foreach (float bx in new[] { -4.2f, -2.2f })
            {
                c.Box(new Vector3(bx - 0.8f, 0.42f, 1.6f), new Vector3(bx + 0.8f, 0.48f, 2.1f), "tiski");
                c.Box(new Vector3(bx - 0.75f, 0f, 1.65f), new Vector3(bx - 0.68f, 0.42f, 2.05f), "metalli");
                c.Box(new Vector3(bx + 0.68f, 0f, 1.65f), new Vector3(bx + 0.75f, 0.42f, 2.05f), "metalli");
            }
            c.Box(new Vector3(-1.5f, 0.01f, -0.5f), new Vector3(1.5f, 0.02f, 3.4f), "matto", false);
            foreach (Vector3 p in new[] { new Vector3(-3f, 2.9f, 0f), new Vector3(3f, 2.9f, 0f), new Vector3(0f, 2.9f, 2.5f) })
                c.Lamp(p, new Color(1f, 0.9f, 0.75f), 1.1f, 6f);
            c.Lamp(new Vector3(0f, 2.8f, hd + 1.0f), new Color(1f, 0.85f, 0.65f), 0.9f, 5f);
            c.Flush();
            // arrivee au retour : devant le guichet, face a la sortie (cote route)
            AirfieldArrival = c.Root.transform.TransformPoint(new Vector3(2.4f, 0.05f, 0.6f));
            AirfieldArrivalYaw = TerminalYaw;
            Journal.Info(string.Format(System.Globalization.CultureInfo.InvariantCulture, "terminal construit (plancher {0:F2}, sol {1:F2}..{2:F2})", floor, lo, hi));
        }

        // Cessna 172 (modele de osmosikum, CC BY 4.0, embarque dans le mod) ; a defaut, l'avion en boites
        static void Cessna(Transform parent, Vector3 at, float yaw)
        {
            Dictionary<string, Transform> parts;
            var holder = new GameObject("VOYAGE_CESSNA");
            holder.transform.SetParent(parent, false);
            at.y = GroundY(at.x, at.z) - 0.09f;   // (roues a 11 cm sous l'origine du modele)
            holder.transform.position = at;
            holder.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            GameObject m = Modele.Load("cessna", holder.transform, out parts);
            if (m == null) { Object.Destroy(holder); Plane(parent, at, yaw); return; }
            Transform prop;
            Avion.Setup(holder.transform, parts.TryGetValue("propelting", out prop) ? prop : null);
            // collisions : fuselage, aile haute, empennage (boites)
            foreach (var b in new[] { new Bounds(new Vector3(0f, 1.25f, 0.3f), new Vector3(1.2f, 1.6f, 7.8f)),
                                      new Bounds(new Vector3(0f, 2.8f, 1.0f), new Vector3(12.6f, 0.25f, 1.6f)),
                                      new Bounds(new Vector3(0f, 1.6f, -4.2f), new Vector3(3.4f, 1.6f, 1.0f)) })
            {
                var c = new GameObject("c");
                c.transform.SetParent(holder.transform, false);
                c.transform.localPosition = b.center;
                c.AddComponent<BoxCollider>().size = b.size;
            }
        }

        // petit avion a aile haute (facon Cessna) : fuselage, aile, empennage, train ; nez vers +z local
        static void Plane(Transform parent, Vector3 at, float yaw)
        {
            at.y = GroundY(at.x, at.z) + 0.11f;
            var c = new Construction("VOYAGE_AVION", at, yaw, parent);
            c.Color("runko", new Color(0.94f, 0.94f, 0.92f));
            c.Color("raita", new Color(0.72f, 0.1f, 0.1f));
            c.Color("ikkuna", new Color(0.15f, 0.2f, 0.25f));
            c.Color("musta", new Color(0.06f, 0.06f, 0.06f));
            c.Color("metalli", new Color(0.6f, 0.6f, 0.62f));
            // fuselage (plus fin vers la queue)
            c.Box(new Vector3(-0.55f, 0.75f, -1.2f), new Vector3(0.55f, 1.95f, 1.8f), "runko");
            c.OBox(new Vector3(0f, 1.45f, -3.2f), Quaternion.Euler(-6f, 0f, 0f), new Vector3(0.7f, 0.8f, 4.2f), "runko", "runko", "runko");
            c.Box(new Vector3(-0.45f, 0.85f, 1.8f), new Vector3(0.45f, 1.7f, 2.6f), "runko");
            c.Box(new Vector3(-0.56f, 1.15f, -1.0f), new Vector3(0.56f, 1.25f, 1.7f), "raita", false);
            c.Box(new Vector3(-0.57f, 1.35f, 0.0f), new Vector3(0.57f, 1.8f, 1.2f), "ikkuna", false);
            c.OBox(new Vector3(0f, 1.75f, 1.55f), Quaternion.Euler(-35f, 0f, 0f), new Vector3(1.0f, 0.05f, 0.6f), "ikkuna", "ikkuna", "ikkuna");
            // aile haute et haubans
            c.Box(new Vector3(-5.5f, 1.95f, -0.2f), new Vector3(5.5f, 2.08f, 1.3f), "runko");
            c.Box(new Vector3(-5.5f, 1.94f, -0.2f), new Vector3(-4.6f, 2.09f, 1.3f), "raita", false);
            c.Box(new Vector3(4.6f, 1.94f, -0.2f), new Vector3(5.5f, 2.09f, 1.3f), "raita", false);
            foreach (float s in new[] { -1f, 1f })
                c.OBox(new Vector3(s * 1.6f, 1.45f, 0.5f), Quaternion.Euler(0f, 0f, s * 33f), new Vector3(0.06f, 1.4f, 0.1f), "metalli", "metalli", "metalli");
            // empennage
            c.Box(new Vector3(-1.7f, 1.55f, -5.3f), new Vector3(1.7f, 1.62f, -4.5f), "runko");
            c.OBox(new Vector3(0f, 2.3f, -5.0f), Quaternion.Euler(-12f, 0f, 0f), new Vector3(0.06f, 1.5f, 0.9f), "raita", "raita", "raita");
            // moteur, helice, train
            c.Box(new Vector3(-0.08f, 0.7f, 2.62f), new Vector3(0.08f, 1.95f, 2.68f), "musta", false);
            c.Box(new Vector3(-0.08f, 1.24f, 2.6f), new Vector3(0.08f, 1.4f, 2.75f), "metalli", false);
            foreach (Vector3 w in new[] { new Vector3(-1.1f, 0f, 0.2f), new Vector3(1.1f, 0f, 0.2f), new Vector3(0f, 0f, 2.3f) })
            {
                c.Box(w + new Vector3(-0.08f, 0f, -0.22f), w + new Vector3(0.08f, 0.44f, 0.22f), "musta");
                c.Box(w + new Vector3(-0.03f, 0.3f, -0.03f), w + new Vector3(0.03f, 0.85f, 0.03f), "metalli", false);
            }
            c.Text("OH-SAA", new Vector3(-0.58f, 1.5f, -2.6f), 90f, 0.18f, new Color(0.15f, 0.15f, 0.2f));
            c.Text("OH-SAA", new Vector3(0.58f, 1.5f, -2.6f), -90f, 0.18f, new Color(0.15f, 0.15f, 0.2f));
            c.Flush();
            Avion.Setup(c.Root.transform, null);
        }

        // manche a air : mat, cone rayé orange et blanc
        static void Windsock(Transform parent, Vector3 at)
        {
            at.y = GroundY(at.x, at.z);
            var c = new Construction("VOYAGE_MANCHE", at, 200f, parent);
            c.Color("metalli", new Color(0.7f, 0.7f, 0.72f));
            c.Color("oranssi", new Color(0.95f, 0.45f, 0.1f));
            c.Color("valkoinen", new Color(0.95f, 0.95f, 0.93f));
            c.Cylinder("metalli", Vector3.zero, 0.06f, 0.04f, 6f, 8, true);
            for (int i = 0; i < 4; i++)
            {
                float z0 = 0.1f + i * 0.6f, r0 = 0.32f - i * 0.05f, r1 = r0 - 0.05f;
                string k = i % 2 == 0 ? "oranssi" : "valkoinen";
                for (int s = 0; s < 8; s++)
                {
                    float a0 = s * Mathf.PI / 4f, a1 = (s + 1) * Mathf.PI / 4f;
                    Vector3 d0 = new Vector3(Mathf.Cos(a0), Mathf.Sin(a0), 0f), d1 = new Vector3(Mathf.Cos(a1), Mathf.Sin(a1), 0f);
                    Vector3 o0 = new Vector3(0f, 5.7f - i * 0.12f, z0), o1 = new Vector3(0f, 5.7f - (i + 1) * 0.12f, z0 + 0.6f);
                    c.Quad(k, o0 + d1 * r0, o0 + d0 * r0, o1 + d0 * r1, o1 + d1 * r1);
                    c.Quad(k, o0 + d0 * r0, o0 + d1 * r0, o1 + d1 * r1, o1 + d0 * r1);
                }
            }
            c.Flush();
        }

        // panneau au bord de la route : LENTOKENTTÄ / AÉRODROME
        static void RoadSign(Transform parent, Vector3 at)
        {
            at.y = GroundY(at.x, at.z);
            var c = new Construction("VOYAGE_PANNEAU", at, 180f, parent);
            c.Color("metalli", new Color(0.7f, 0.7f, 0.72f));
            c.Color("kyltti", new Color(0.1f, 0.3f, 0.65f));
            c.Cylinder("metalli", new Vector3(-0.9f, 0f, 0f), 0.04f, 0.04f, 2.6f, 6, true);
            c.Cylinder("metalli", new Vector3(0.9f, 0f, 0f), 0.04f, 0.04f, 2.6f, 6, true);
            c.Box(new Vector3(-1.2f, 1.7f, -0.03f), new Vector3(1.2f, 2.6f, 0.03f), "kyltti", false);
            c.Text("LENTOKENTTÄ", new Vector3(0f, 2.3f, 0.04f), 180f, 0.13f, Color.white);
            c.Text(Jeu.T("AÉRODROME  »", "AIRFIELD  »", "АЭРОДРОМ  »"), new Vector3(0f, 1.95f, 0.04f), 180f, 0.09f, Color.white);
            c.Text("LENTOKENTTÄ", new Vector3(0f, 2.3f, -0.04f), 0f, 0.13f, Color.white);
            c.Text(Jeu.T("«  AÉRODROME", "«  AIRFIELD", "«  АЭРОДРОМ"), new Vector3(0f, 1.95f, -0.04f), 0f, 0.09f, Color.white);
            c.Flush();
        }
    }
}
