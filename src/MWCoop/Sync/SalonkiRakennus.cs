using System;
using System.Collections.Generic;
using System.IO;
using HutongGames.PlayMaker;
using UnityEngine;

namespace MWCoop
{
    // AUTOSALONKI -- location des voitures du F10 (demande de JD, 29/10 ; prix d'apres les performances ; le menu de location remplace
    // la prise au F10). 3e version (JD 30/10, photo d'un vieux garage d'atelier : « plutot un garage, un peu comme le controle
    // technique ... les voitures disposees un peu n'importe comment, certaines sur des ponts elevateurs, on pourrait juste les voir ;
    // la location dans une salle vitree qui donne sur le garage, un PNJ ; une fois louee, elle apparait devant le garage sur le
    // parking ») : halle de briques claires (fenetres cintrees a petits carreaux, plafond de briques entre poutrelles d'acier,
    // suspensions industrielles, sol de beton), voitures du catalogue posees en vrac ou levees sur des ponts a deux colonnes,
    // pneus, servantes, etabli ; bureau vitre dans l'angle avant droit (porte sur la rue et porte sur l'atelier), vendeur derriere
    // son comptoir (visee + clic : menu de location, SalonkiMenu) ; parking de livraison devant les portes.
    // Style (JD 31/10 : « en prenant exemple sur les textures et le design de l'inspection du jeu ») : les matieres du batiment
    // INSPECTION de My Summer Car (releve SalonkiInspection) -- murs de dalles de beton (concrete_slabs), bandeau de toit en tole
    // ondulee (wall_metal1), toit et plafond de bac acier (wall_metal2), sol (tiles4), brique (wall_brick2) pour le pylone de
    // l'enseigne et les murets ; fenetres rectangulaires a menuiseries de metal clair, bandeaux vitres hauts, tubes fluorescents.
    // My Summer Car d'abord. A l'est du cinema KINO TAHTI, meme route, meme alignement : la prairie au nord de la route y est plate
    // (releve du 29/10 : 1,1 m de denivele sur 120 x 80 m, aucun objet, 200 m libres vers le nord).
    // Repere propre : x vers l'est (le long de la route), z vers le nord (le fond), y = 0 au sol de l'atelier ; facade a z = 0 sur
    // l'alignement de celle du cinema, parking devant (z < 0). Places de l'atelier fixes (SaSlots) : au-dela, les voitures restent au
    // menu seulement.
    // [Salonki] Paikka=x,z,cap : place de l'angle sud-ouest (facade, cote cinema) et cap ; Active=0 : pas de garage.
    public static partial class Kauppa
    {
        const float SaW = 24f, SaD = 22f, SaH = 6f, SaT = 0.35f, SaPark = 13f, SaLiftH = 1.75f;
        // bureau vitre (angle avant droit) et son comptoir
        const float SaOx = 17.2f, SaOz = 8.5f, SaOh = 2.9f, SaDeskZ0 = 4.5f, SaDeskZ1 = 5.1f;
        static readonly Vector3 SaVendorSpot = new Vector3(20.9f, 0f, 6.0f);

        public static bool SalonkiOn { get { return Game.Msc && Ajouts.Salonki && Config.GetInt("Salonki", "Active", 1) != 0; } }

        class Stand
        {
            public int Model, Number; public string Name, Group, Folder; public int Price;
            public Texture2D Poster;
            public VazCar.Built Hold;
        }
        // places de l'atelier : x, z, cap (nez de la voiture), sur un pont
        struct Slot { public float X, Z, Yaw; public bool Lift; public Slot(float x, float z, float yaw, bool lift) { X = x; Z = z; Yaw = yaw; Lift = lift; } }
        static readonly Slot[] SaSlots = {
            new Slot(3.7f, 14.2f, 90f, true),     // deux ponts contre le mur gauche, de profil (photo)
            new Slot(3.7f, 8.7f, 96f, true),
            new Slot(11.2f, 18.9f, 180f, true),   // pont du fond, sous la fenetre
            new Slot(10.6f, 11.6f, 171f, false),  // au milieu, le nez vers les portes
            new Slot(2.9f, 3.4f, 152f, false),    // devant a gauche, de biais
            new Slot(15.2f, 13.0f, 204f, false),  // a droite, contre le bureau
            new Slot(6.4f, 19.6f, 172f, false),
            new Slot(14.6f, 4.0f, 196f, false),
            new Slot(8.0f, 5.6f, 188f, false),
        };
        static GameObject salonki;
        static readonly List<Stand> stands = new List<Stand>();
        static bool saLooked;
        static Font saFont;
        static Avatar saVendor;
        static Collider saVendorCol;
        static readonly List<UnityEngine.Object> saBaked = new List<UnityEngine.Object>();

        static void ResetSalonki()
        {
            foreach (Stand s in stands) { if (s.Hold != null) VazCar.DestroyPreview(s.Hold); if (s.Poster != null) UnityEngine.Object.Destroy(s.Poster); }
            foreach (UnityEngine.Object o in saBaked) if (o != null) UnityEngine.Object.Destroy(o);
            saBaked.Clear();
            stands.Clear(); salonki = null; saLooked = false; SalonkiOpen = false; saMenu = null; saVendor = null; saVendorCol = null;
        }

        // angle sud-ouest du garage (facade, cote cinema) et cap du repere (x vers l'est)
        public static void SalonkiFrame(out Vector3 o, out float yaw)
        {
            string s = Config.Get("Salonki", "Paikka", "");
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            if (s.Length > 0)
                try { string[] a = s.Split(','); o = new Vector3(float.Parse(a[0], ci), 0f, float.Parse(a[1], ci)); yaw = float.Parse(a[2], ci); return; }
                catch { L("[Salonki] Paikka illisible : " + s); }
            // (colle au cinema, JD 30/10 : juste a l'est du muret est de son parking, sur l'alignement de sa facade ; repere du cinema :
            // x vers l'ouest, z vers la route)
            Vector3 c; float ky; string fl;
            KinoFrame(out c, out ky, out fl);
            o = c + Quaternion.Euler(0, ky, 0) * new Vector3(-(KL + 2.6f), 0f, KD);
            yaw = ky + 180f;
        }

        // prix de location : d'apres le rapport puissance / poids (mk, arrondi a 10) ; la VAZ-2108 60 mk ; [Salonki] Hinta_<dossier>=n
        static int SalonkiPrice(VazCar.Model m)
        {
            int over = Config.GetInt("Salonki", "Hinta_" + m.Folder, 0);
            if (over > 0) return over;
            if (m.Folder == "vaz" || m.Sp == null || m.Sp.Mass <= 0f) return 60;
            float chT = m.Sp.Power / (m.Sp.Mass / 1000f);
            return Mathf.Max(60, Mathf.RoundToInt(chT / 10f) * 10);
        }

        // catalogue : modeles installes, par categorie (MWCoop d'abord, puis par nom), puis par nom
        static List<Stand> SalonkiCatalog()
        {
            var l = new List<Stand>();
            for (int i = 0; i < VazCar.Models.Length; i++)
            {
                if (!VazCar.Has(i)) continue;
                VazCar.Model m = VazCar.ModelOf(i);
                l.Add(new Stand { Model = i, Name = m.Name, Group = m.Group, Folder = m.Folder, Price = SalonkiPrice(m) });
            }
            l.Sort((a, b) =>
            {
                if (a.Group != b.Group) return a.Group == "MWCoop" ? -1 : b.Group == "MWCoop" ? 1 : string.CompareOrdinal(a.Group, b.Group);
                return string.CompareOrdinal(a.Name, b.Name);
            });
            return l;
        }

        static void SalonkiMaterials()
        {
            Mat("sa_tiili", "wall_brick2", 2.4f, new Color(1.0f, 0.92f, 0.92f));       // brique (teinte de l'inspection)
            Mat("sa_laatat", "concrete_slabs", 3.6f, new Color(0.86f, 0.86f, 0.86f));   // dalles de beton des murs de l'inspection
            Mat("sa_pelti", "wall_metal1", 2.0f, new Color(1.08f, 1.06f, 0.98f));       // tole ondulee du bandeau de toit
            Mat("sa_katto", "wall_metal2", 2.5f, new Color(0.85f, 0.85f, 0.85f));       // bac acier (toit, plafond)
            Mat("sa_kehys", "wall_metal2", 1.0f, new Color(0.80f, 0.81f, 0.82f));       // menuiseries de metal clair
            Mat("sa_lattia", "paint_black_matte", 1f, new Color(0.50f, 0.50f, 0.50f));   // beton gris lisse (le carrelage tiles4 de l'inspection ressortait beige)
            Mat("sa_valkoinen", "paint_black_matte", 1f, new Color(0.93f, 0.93f, 0.91f));
            Mat("sa_tumma", "paint_black_matte", 1f, new Color(0.12f, 0.12f, 0.14f));   // menuiseries, bandeaux
            Mat("sa_teras", "paint_black_matte", 1f, new Color(0.24f, 0.25f, 0.27f));   // poutrelles
            Mat("sa_sininen", "paint_black_matte", 1f, new Color(0.10f, 0.22f, 0.58f)); // colonnes des ponts
            Mat("sa_punainen", "paint_black_matte", 1f, new Color(0.72f, 0.09f, 0.06f));
            Mat("sa_keltainen", "paint_black_matte", 1f, new Color(0.95f, 0.72f, 0.10f));
            Mat("sa_kumi", "paint_black_matte", 1f, new Color(0.06f, 0.06f, 0.065f));
            Mat("sa_tiski", "wood12", 1f);
            Mat("sa_asfaltti", "mwc_asphalt", 7f);
            Mat("sa_viiva", "paint_black_matte", 1f, new Color(0.95f, 0.95f, 0.92f));
            Mat("sa_betoni", "paint_black_matte", 1f, new Color(0.62f, 0.62f, 0.60f));
            if (!mats.ContainsKey("lasi")) Mat("lasi", "psk_glass", 1f);
            // verriere du toit : blanc non eclaire (lumiere du jour)
            // (parking : memes matieres que celui du cinema ; creees ici si le cinema est coupe)
            if (!mats.ContainsKey("ki_asfaltti")) Mat("ki_asfaltti", "mwc_asphalt", 7f);
            if (!mats.ContainsKey("ki_betoni")) Mat("ki_betoni", "paint_black_matte", 1f, new Color(0.66f, 0.66f, 0.64f));
            if (!mats.ContainsKey("ki_viiva")) Mat("ki_viiva", "paint_black_matte", 1f, new Color(0.95f, 0.78f, 0.2f));
            Shader u = Shader.Find("Unlit/Color");
            if (u != null) { var m = new Material(u) { name = "osuus_sa_valo", color = new Color(0.90f, 0.93f, 0.97f) }; mats["sa_valo"] = m; tiles["sa_valo"] = 1f; }
            else Mat("sa_valo", "paint_black_matte", 1f, Color.white);
        }

        static void BuildSalonki()
        {
            Vector3 o; float yaw;
            SalonkiFrame(out o, out yaw);
            Quaternion rot = Quaternion.Euler(0, yaw, 0);
            stands.Clear();
            stands.AddRange(SalonkiCatalog());
            if (stands.Count == 0) { L("salon : aucune voiture au catalogue"); return; }
            SalonkiMaterials();
            // sol au-dessus du terrain sous le batiment (le parking suit le sol, SalonkiParking)
            float gMax = float.MinValue, gMin = float.MaxValue;
            for (float x = 0f; x <= SaW; x += 3f)
                for (float z = 0f; z <= SaD; z += 2.75f)
                {
                    RaycastHit? h = GroundHit(o + rot * new Vector3(x, 0, z) + Vector3.up * 200f);
                    if (!h.HasValue || kino != null && h.Value.collider.transform.IsChildOf(kino.transform)) continue;
                    gMax = Mathf.Max(gMax, h.Value.point.y); gMin = Mathf.Min(gMin, h.Value.point.y);
                }
            if (gMax == float.MinValue) gMax = gMin = 3f;
            float floorY = gMax + 0.2f, down = floorY - gMin + 0.6f;
            L(string.Format("salon : {0} voiture(s), garage {1:F0} x {2:F0} m, sol {3:F2}..{4:F2}, plancher a {5:F2}", stands.Count, SaW, SaD, gMin, gMax, floorY));

            salonki = new GameObject("MWCoop AUTOSALONKI");
            salonki.transform.position = new Vector3(o.x, floorY, o.z);
            salonki.transform.rotation = rot;
            var keepRoot = root; root = salonki;
            try
            {
                SalonkiShell(down);
                SalonkiParking();
                SalonkiOffice();
                SalonkiClutter();
                Flush();
                SalonkiFittings();
                SalonkiSigns();
                SalonkiCars();
                SalonkiVendor();
                SalonkiLights();
            }
            finally { root = keepRoot; }
            L("salon construit en " + salonki.transform.position.ToString("F1") + ", cap " + yaw.ToString("F1"));
        }

        // ---------------------------------------------------------------- murs
        // une ouverture d'un mur : de a a b (le long du mur), allege 'sill', haut du rectangle 'top' ; cintree (demi-cercle au-dessus de
        // top) ; vitree (carreaux a petits bois) ou non (porte)
        struct SaHole { public float A, B, Sill, Top; public bool Arch, Glass; public SaHole(float a, float b, float sill, float top, bool arch, bool glass) { A = a; B = b; Sill = sill; Top = top; Arch = arch; Glass = glass; } }

        // boite d'un mur : alongX -> u = x, epaisseur en z (p0..p1) ; sinon u = z, epaisseur en x
        static void WBox(bool alongX, float p0, float p1, float u0, float u1, float v0, float v1, string mat, bool collide = true)
        {
            if (u1 - u0 < 0.001f || v1 - v0 < 0.001f) return;
            if (alongX) Box(new Vector3(u0, v0, p0), new Vector3(u1, v1, p1), mat, collide);
            else Box(new Vector3(p0, v0, u0), new Vector3(p1, v1, u1), mat, collide);
        }

        static float ArchY(SaHole h, float u)
        {
            float r = (h.B - h.A) / 2f, c = (h.A + h.B) / 2f, d = u - c;
            return h.Top + Mathf.Sqrt(Mathf.Max(0f, r * r - d * d));
        }

        static void SaWall(bool alongX, float p0, float p1, float u0, float u1, float v1, string mat, params SaHole[] holes)
        {
            float cur = u0;
            foreach (SaHole h in holes)
            {
                WBox(alongX, p0, p1, cur, h.A, 0f, v1, mat);
                WBox(alongX, p0, p1, h.A, h.B, 0f, h.Sill, mat);
                if (!h.Arch) WBox(alongX, p0, p1, h.A, h.B, h.Top, v1, mat, h.Top < 3f);
                else
                {
                    // cintre : bandes de 12 cm, mur au-dessus de l'arc, vitre dessous
                    int n = Mathf.Max(6, Mathf.RoundToInt((h.B - h.A) / 0.12f));
                    for (int k = 0; k < n; k++)
                    {
                        float a = h.A + (h.B - h.A) * k / n, b = h.A + (h.B - h.A) * (k + 1) / n, y = ArchY(h, (a + b) / 2f);
                        WBox(alongX, p0, p1, a, b, y, v1, mat, false);
                        if (h.Glass) WBox(alongX, (p0 + p1) / 2f - 0.01f, (p0 + p1) / 2f + 0.01f, a, b, h.Top, y, "lasi", false);
                    }
                }
                if (h.Glass) SaWindow(alongX, (p0 + p1) / 2f, h);
                cur = h.B;
            }
            WBox(alongX, p0, p1, cur, u1, 0f, v1, mat);
        }

        // vitre et petits bois d'une fenetre (dans le plan du mur)
        static void SaWindow(bool alongX, float p, SaHole h)
        {
            float q0 = p - 0.04f, q1 = p + 0.04f;
            WBox(alongX, p - 0.01f, p + 0.01f, h.A, h.B, h.Sill, h.Top, "lasi");
            // cadre, montants (tous les ~45 cm), traverses (tous les ~55 cm), arretes par l'arc
            WBox(alongX, q0, q1, h.A, h.B, h.Sill, h.Sill + 0.06f, "sa_kehys", false);
            int nv = Mathf.Max(2, Mathf.RoundToInt((h.B - h.A) / 0.45f));
            for (int k = 0; k <= nv; k++)
            {
                float u = h.A + (h.B - h.A) * k / nv, top = h.Arch ? ArchY(h, Mathf.Clamp(u, h.A + 0.03f, h.B - 0.03f)) : h.Top;
                WBox(alongX, q0, q1, u - 0.025f, u + 0.025f, h.Sill, top, "sa_kehys", false);
            }
            for (float y = h.Sill + 0.55f; y < (h.Arch ? ArchY(h, (h.A + h.B) / 2f) - 0.1f : h.Top); y += 0.55f)
            {
                float a = h.A, b = h.B;
                if (h.Arch && y > h.Top)
                {
                    float r = (h.B - h.A) / 2f, c = (h.A + h.B) / 2f, dy = y - h.Top, half = Mathf.Sqrt(Mathf.Max(0f, r * r - dy * dy));
                    a = c - half; b = c + half;
                }
                WBox(alongX, q0, q1, a, b, y - 0.025f, y + 0.025f, "sa_kehys", false);
            }
            if (!h.Arch) WBox(alongX, q0, q1, h.A, h.B, h.Top - 0.06f, h.Top, "sa_kehys", false);
        }

        // coque : socle, sol, murs de dalles de beton (portes, fenetres, bandeaux vitres hauts), plafond de bac acier, toit, poutrelles
        static void SalonkiShell(float down)
        {
            float W = SaW, D = SaD, H = SaH, T = SaT, top = H + 0.6f;
            Box(new Vector3(-T, -down, -0.4f), new Vector3(W + T, -0.02f, D + T), "sa_betoni");
            Box(new Vector3(0f, -0.02f, 0f), new Vector3(W, 0f, D), null, null, "sa_lattia", null, null, null);
            // facade : deux grandes portes d'atelier ouvertes, une fenetre cintree, porte et vitrine du bureau
            SaWall(true, -T, 0f, -T, W + T, top, "sa_laatat",
                new SaHole(2.4f, 7.0f, 0f, 4.4f, false, false), new SaHole(8.4f, 13.0f, 0f, 4.4f, false, false),
                new SaHole(14.0f, 16.6f, 1.0f, 3.6f, false, true),
                new SaHole(19.6f, 21.0f, 0f, 2.3f, false, false), new SaHole(21.6f, 23.4f, 0.9f, 2.4f, false, true));
            // fond : trois grandes fenetres
            SaWall(true, D, D + T, -T, W + T, top, "sa_laatat",
                new SaHole(3.0f, 6.4f, 1.0f, 3.8f, false, true), new SaHole(9.5f, 12.9f, 1.0f, 3.8f, false, true), new SaHole(16.0f, 19.4f, 1.0f, 3.8f, false, true));
            // pignons : bandeaux vitres hauts (comme l'atelier de l'inspection), deux fenetres derriere le bureau a droite
            SaWall(false, W, W + T, 0f, D, top, "sa_laatat", new SaHole(9.6f, 14.4f, 1.0f, 3.6f, false, true), new SaHole(15.6f, 20.4f, 4.2f, 5.0f, false, true));
            SaWall(false, -T, 0f, 0f, D, top, "sa_laatat", new SaHole(1.5f, 6.5f, 4.2f, 5.0f, false, true), new SaHole(8.5f, 13.5f, 4.2f, 5.0f, false, true), new SaHole(15.5f, 20.5f, 4.2f, 5.0f, false, true));
            // plafond de bac acier, toit blanc, bandeau de tole ondulee tout autour
            Box(new Vector3(0f, H, 0f), new Vector3(W, H + 0.2f, D), "sa_tumma", "sa_tumma", "sa_tumma", "sa_katto", "sa_tumma", "sa_tumma", false);
            Box(new Vector3(-T - 0.25f, top, -T - 0.25f), new Vector3(W + T + 0.25f, top + 0.2f, D + T + 0.25f), "sa_pelti", "sa_pelti", "sa_katto", "sa_tumma", "sa_pelti", "sa_pelti");
            Box(new Vector3(-T - 0.12f, top - 0.9f, -T - 0.12f), new Vector3(W + T + 0.12f, top, -T), "sa_pelti", false);
            Box(new Vector3(-T - 0.12f, top - 0.9f, D + T), new Vector3(W + T + 0.12f, top, D + T + 0.12f), "sa_pelti", false);
            Box(new Vector3(W + T, top - 0.9f, -T), new Vector3(W + T + 0.12f, top, D + T), "sa_pelti", false);
            Box(new Vector3(-T - 0.12f, top - 0.9f, -T), new Vector3(-T, top, D + T), "sa_pelti", false);
            // poutrelles d'acier en travers, longrine de la verriere
            for (float z = 2.2f; z < D; z += 4.4f)
            {
                Box(new Vector3(0f, H - 0.42f, z - 0.06f), new Vector3(W, H, z + 0.06f), "sa_teras", false);
                Box(new Vector3(0f, H - 0.46f, z - 0.15f), new Vector3(W, H - 0.42f, z + 0.15f), "sa_teras", false);
            }
            foreach (float x in new[] { 9.3f, 14.7f }) Box(new Vector3(x - 0.08f, H - 0.3f, 1.5f), new Vector3(x + 0.08f, H, D - 1.5f), "sa_teras", false);
            // dehors (JD 30/10 : « bien structure a l'exterieur ») : soubassement de beton ; encadrements de metal des grandes portes
            float bandY = 0.55f, pr = 0.07f;
            foreach (float[] s in new[] { new[] { -T, 2.4f }, new[] { 7.0f, 8.4f }, new[] { 13.0f, 19.6f }, new[] { 21.0f, W + T } })
                Box(new Vector3(s[0], -down, -T - pr), new Vector3(s[1], bandY, -T), "sa_betoni");
            Box(new Vector3(-T - pr, -down, D + T), new Vector3(W + T + pr, bandY, D + T + pr), "sa_betoni");
            Box(new Vector3(W + T, -down, -T - pr), new Vector3(W + T + pr, bandY, D + T + pr), "sa_betoni");
            foreach (float[] dd in new[] { new[] { 2.4f, 7.0f }, new[] { 8.4f, 13.0f } })
            {
                Box(new Vector3(dd[0] - 0.14f, 0f, -T - 0.1f), new Vector3(dd[0], 4.54f, 0.02f), "sa_kehys", false);
                Box(new Vector3(dd[1], 0f, -T - 0.1f), new Vector3(dd[1] + 0.14f, 4.54f, 0.02f), "sa_kehys", false);
                Box(new Vector3(dd[0] - 0.14f, 4.4f, -T - 0.1f), new Vector3(dd[1] + 0.14f, 4.54f, 0.02f), "sa_kehys", false);
            }
            // caissons des portes relevees (dedans, au-dessus des ouvertures)
            foreach (float[] dd in new[] { new[] { 2.4f, 7.0f }, new[] { 8.4f, 13.0f } })
                Box(new Vector3(dd[0] - 0.2f, 4.45f, 0.02f), new Vector3(dd[1] + 0.2f, 5.0f, 0.6f), "sa_teras", false);
        }
        const int SaBays = 5; const float SaBay0 = 2.5f, SaBayW = 3.8f;

        // bureau vitre (angle avant droit) : soubassement, vitres, montants, plafond ; porte sur l'atelier ; comptoir, etageres, chaise
        static void SalonkiOffice()
        {
            float x0 = SaOx, x1 = SaW, z1 = SaOz, H = SaOh;
            // cote atelier (ouest) : porte de z 1,3 a 2,4
            OfficeGlass(false, x0, 0f, 1.3f);
            OfficeGlass(false, x0, 2.4f, z1);
            Box(new Vector3(x0 - 0.06f, 2.3f, 1.3f), new Vector3(x0 + 0.06f, H, 2.4f), "sa_tumma");
            // cote atelier (nord)
            OfficeGlass(true, z1, x0, x1);
            // plafond du bureau (dessus : enseignes, SalonkiSigns)
            Box(new Vector3(x0 - 0.08f, H, 0f), new Vector3(x1, H + 0.18f, z1 + 0.08f), "sa_valkoinen", "sa_tumma", "sa_tumma", "sa_valkoinen", "sa_tumma", "sa_tumma");
            // sol du bureau un peu plus clair
            Box(new Vector3(x0, 0f, 0f), new Vector3(x1, 0.01f, z1), null, null, "sa_betoni", null, null, null, false);
            // comptoir en travers, passage cote mur (x > 22,9) pour le vendeur
            Box(new Vector3(18.4f, 0f, SaDeskZ0), new Vector3(22.9f, 1.02f, SaDeskZ1), "sa_tiski");
            Box(new Vector3(18.3f, 1.02f, SaDeskZ0 - 0.1f), new Vector3(23.0f, 1.08f, SaDeskZ1 + 0.05f), "sa_tumma");
            // etagere de classeurs contre le mur de briques, derriere le vendeur (la vitre sur l'atelier reste degagee), chaise
            Box(new Vector3(x1 - 0.42f, 0f, 5.9f), new Vector3(x1 - 0.02f, 1.9f, 8.3f), "sa_tiski");
            for (int k = 0; k < 4; k++)
                for (int j = 0; j < 7; j++)
                {
                    float bz = 6.0f + j * 0.32f;
                    Box(new Vector3(x1 - 0.44f, 0.1f + k * 0.45f, bz), new Vector3(x1 - 0.12f, 0.42f + k * 0.45f, bz + 0.26f), (j + k) % 3 == 0 ? "sa_punainen" : (j + k) % 3 == 1 ? "sa_sininen" : "sa_tumma", false);
                }
            Box(new Vector3(21.6f, 0.45f, 6.6f), new Vector3(22.1f, 0.52f, 7.1f), "sa_tumma", false);
            Box(new Vector3(21.6f, 0.52f, 7.05f), new Vector3(22.1f, 1.0f, 7.12f), "sa_tumma", false);
            Box(new Vector3(21.82f, 0f, 6.82f), new Vector3(21.88f, 0.45f, 6.88f), "sa_teras", false);
        }

        // paroi vitree du bureau : soubassement plein (0,9 m), vitre jusqu'a 2,6 m, imposte ; montants tous les ~1,4 m
        static void OfficeGlass(bool alongX, float p, float u0, float u1)
        {
            if (u1 - u0 < 0.05f) return;
            WBox(alongX, p - 0.06f, p + 0.06f, u0, u1, 0f, 0.9f, "sa_valkoinen");
            WBox(alongX, p - 0.01f, p + 0.01f, u0, u1, 0.9f, 2.6f, "lasi");
            WBox(alongX, p - 0.06f, p + 0.06f, u0, u1, 2.6f, SaOh, "sa_tumma");
            int n = Mathf.Max(1, Mathf.RoundToInt((u1 - u0) / 1.4f));
            for (int k = 0; k <= n; k++) { float u = u0 + (u1 - u0) * k / n; WBox(alongX, p - 0.05f, p + 0.05f, u - 0.04f, u + 0.04f, 0.9f, 2.6f, "sa_tumma", false); }
            WBox(alongX, p - 0.07f, p + 0.07f, u0, u1, 0.88f, 0.94f, "sa_tumma", false);
        }

        // atelier : piles de pneus, servantes rouges, etabli, futs, voiture baches
        static void SalonkiClutter()
        {
            // servantes contre le mur gauche, au fond
            foreach (float z in new[] { 17.6f, 18.4f })
            {
                Box(new Vector3(0.05f, 0.08f, z), new Vector3(0.7f, 1.05f, z + 0.72f), "sa_punainen");
                for (float y = 0.25f; y < 1.0f; y += 0.16f) Box(new Vector3(0.7f, y, z + 0.06f), new Vector3(0.71f, y + 0.012f, z + 0.66f), "sa_teras", false);
            }
            // etabli au fond, a gauche
            Box(new Vector3(0.6f, 0.86f, D0(0.75f)), new Vector3(3.0f, 0.92f, SaD - 0.05f), "sa_tiski");
            foreach (float x in new[] { 0.7f, 2.85f }) Box(new Vector3(x, 0f, D0(0.7f)), new Vector3(x + 0.06f, 0.86f, SaD - 0.1f), "sa_teras", false);
            Box(new Vector3(0.6f, 0.92f, SaD - 0.12f), new Vector3(3.0f, 2.0f, SaD - 0.05f), "sa_teras", false);   // panneau d'outils
            // futs d'huile, au fond a droite
            Box(new Vector3(20.3f, 0f, 20.6f), new Vector3(21.9f, 0.12f, 21.9f), "sa_tiski", false);
        }
        static float D0(float d) { return SaD - d; }

        // objets ronds (primitives) : pneus, futs, suspensions, tubes, bache
        static void SalonkiFittings()
        {
            Transform t = salonki.transform;
            Material rub = mats["sa_kumi"];
            foreach (Vector3 p in new[] { new Vector3(1.0f, 0f, 11.4f), new Vector3(1.0f, 0f, 12.2f), new Vector3(16.4f, 0f, 9.4f), new Vector3(1.0f, 0f, 6.4f) })
            {
                int n = 3 + Mathf.Abs((int)(p.z * 7f)) % 3;
                for (int k = 0; k < n; k++)
                {
                    // pneu (flanc arrondi par deux cylindres) et trou de jante gris au milieu
                    Vector3 tc = p + new Vector3(0.025f * (k % 2), 0.11f + k * 0.22f, 0.015f * ((k + 1) % 2));
                    SaCyl(t, tc, 0.64f, 0.15f, rub); SaCyl(t, tc, 0.58f, 0.2f, rub);
                    SaCyl(t, tc, 0.36f, 0.205f, mats["sa_teras"]);
                }
                var c = new GameObject("sa_renkaat_c"); c.transform.SetParent(t, false); c.transform.localPosition = p + new Vector3(0, n * 0.11f, 0);
                c.AddComponent<BoxCollider>().size = new Vector3(0.62f, n * 0.22f, 0.62f);
            }
            foreach (Vector3 p in new[] { new Vector3(20.75f, 0.12f, 21.0f), new Vector3(21.45f, 0.12f, 21.1f), new Vector3(21.1f, 0.12f, 21.55f) })
                SaCyl(t, p + new Vector3(0, 0.44f, 0), 0.58f, 0.88f, p.x > 21.3f ? mats["sa_punainen"] : mats["sa_sininen"]);
            // tuyaux blancs le long du plafond
            foreach (float z in new[] { 3.4f, 13.4f }) SaTube(t, new Vector3(0.2f, SaH - 1.0f, z), new Vector3(SaW - 0.2f, SaH - 1.0f, z), 0.16f, mats["sa_valkoinen"]);
            SaTube(t, new Vector3(0.6f, SaH - 1.0f, 3.4f), new Vector3(0.6f, SaH - 1.0f, 13.4f), 0.16f, mats["sa_valkoinen"]);
            // voiture sous sa bache rouge, dans le coin du fond (photo)
            var cover = new GameObject("sa_peite"); cover.transform.SetParent(t, false);
            cover.transform.localPosition = new Vector3(16.4f, 0f, 19.7f); cover.transform.localRotation = Quaternion.Euler(0, 100f, 0);
            Material red = mats["sa_punainen"];
            SaPrim(cover.transform, PrimitiveType.Sphere, new Vector3(0, 0.55f, 0), new Vector3(1.75f, 1.0f, 4.1f), red);
            SaPrim(cover.transform, PrimitiveType.Sphere, new Vector3(0, 0.95f, -0.2f), new Vector3(1.35f, 0.75f, 2.1f), red);
            SaPrim(cover.transform, PrimitiveType.Cube, new Vector3(0, 0.25f, 0), new Vector3(1.7f, 0.5f, 3.9f), red);
            cover.AddComponent<BoxCollider>().center = new Vector3(0, 0.65f, 0);
            cover.GetComponent<BoxCollider>().size = new Vector3(1.75f, 1.3f, 4.1f);
        }

        static GameObject SaPrim(Transform parent, PrimitiveType type, Vector3 pos, Vector3 scale, Material m, Quaternion? rot = null, bool keepCollider = false)
        {
            var g = GameObject.CreatePrimitive(type);
            if (!keepCollider) UnityEngine.Object.Destroy(g.GetComponent<Collider>());
            g.transform.SetParent(parent, false); g.transform.localPosition = pos; g.transform.localScale = scale;
            if (rot.HasValue) g.transform.localRotation = rot.Value;
            g.GetComponent<Renderer>().sharedMaterial = m;
            return g;
        }

        static TextMesh SaText(Transform parent, string s, Vector3 pos, Quaternion rot, float size, Color col, TextAnchor anchor, FontStyle st = FontStyle.Bold)
        {
            if (saFont == null) saFont = Resources.GetBuiltinResource(typeof(Font), "Arial.ttf") as Font;
            var g = new GameObject("sa_teksti");
            g.transform.SetParent(parent, false); g.transform.localPosition = pos; g.transform.localRotation = rot;
            var tm = g.AddComponent<TextMesh>();
            tm.font = saFont; tm.fontSize = 96; tm.characterSize = size; tm.anchor = anchor; tm.fontStyle = st; tm.color = col;
            tm.alignment = anchor == TextAnchor.MiddleLeft || anchor == TextAnchor.UpperLeft || anchor == TextAnchor.LowerLeft ? TextAlignment.Left : TextAlignment.Center;
            tm.text = s;
            g.GetComponent<MeshRenderer>().sharedMaterial = saFont.material;
            return tm;
        }

        // Panneau de texte cuit : les TextMesh (fill, en metres autour du centre) rendus une fois dans une image (camera orthographique,
        // calque de l'apercu), posee sur un quad (Unlit/Texture, d'un seul cote : visible du cote -z du panneau, cache par les murs) --
        // le texte 3D de Unity (GUI/Text Shader) se voyait a travers les panneaux et a l'envers depuis l'interieur (essai du 30/10).
        static GameObject SaPanel(Transform parent, Vector3 pos, Quaternion rot, float w, float h, Color bg, Action<Transform> fill, float ppm)
        {
            int pw = Mathf.Clamp(Mathf.RoundToInt(w * ppm), 16, 2048), ph = Mathf.Clamp(Mathf.RoundToInt(h * ppm), 16, 2048);
            int layer = Studio.PreviewLayer;
            var holder = new GameObject("sa_paistaa");
            holder.transform.position = new Vector3(0f, -900f, 0f);
            var cg = new GameObject("sa_paistaa_kamera");
            Texture2D tex = null;
            try
            {
                fill(holder.transform);
                foreach (Transform c in holder.GetComponentsInChildren<Transform>(true)) c.gameObject.layer = layer;
                Camera cam = cg.AddComponent<Camera>();
                cam.enabled = false; cam.orthographic = true; cam.orthographicSize = h / 2f; cam.aspect = w / h;
                cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = bg; cam.cullingMask = 1 << layer;
                cam.nearClipPlane = 0.1f; cam.farClipPlane = 20f;
                cg.transform.position = holder.transform.position + new Vector3(0, 0, -5f); cg.transform.rotation = Quaternion.identity;
                var rt = new RenderTexture(pw, ph, 24);
                cam.targetTexture = rt; cam.Render();
                RenderTexture.active = rt;
                // (sans mipmaps : les bannieres, tres allongees, restaient noires vues de biais -- essai du 30/10)
                tex = new Texture2D(pw, ph, TextureFormat.RGB24, false) { name = "sa_paneeli" };
                tex.ReadPixels(new Rect(0, 0, pw, ph), 0, 0); tex.Apply(false);
                tex.wrapMode = TextureWrapMode.Clamp; tex.anisoLevel = 4;
                RenderTexture.active = null; cam.targetTexture = null; rt.Release(); UnityEngine.Object.Destroy(rt);
            }
            // (tout de suite : les panneaux suivants se cuisent dans la meme image, au meme endroit -- avec Destroy, leurs textes s'y
            // superposaient, essai du 30/10)
            finally { UnityEngine.Object.DestroyImmediate(holder); UnityEngine.Object.DestroyImmediate(cg); }
            return SaQuad(parent, pos, rot, w, h, tex, "sa_paneeli");
        }

        static GameObject SaQuad(Transform parent, Vector3 pos, Quaternion rot, float w, float h, Texture2D tex, string name)
        {
            var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
            UnityEngine.Object.Destroy(q.GetComponent<Collider>());
            q.name = name; q.transform.SetParent(parent, false);
            q.transform.localPosition = pos; q.transform.localRotation = rot; q.transform.localScale = new Vector3(w, h, 1f);
            var m = new Material(Shader.Find("Unlit/Texture") ?? Shader.Find("Legacy Shaders/Diffuse")) { mainTexture = tex, name = name };
            q.GetComponent<Renderer>().sharedMaterial = m;
            if (tex != null && tex.name == "sa_paneeli") saBaked.Add(tex);
            saBaked.Add(m);
            return q;
        }

        // enseignes : facade (rue), dessus du bureau (vers l'atelier), affiches du mur gauche, cadres du bureau (images des voitures)
        static void SalonkiSigns()
        {
            Transform t = salonki.transform;
            Color gold = new Color(1f, 0.82f, 0.3f), dark = new Color(0.10f, 0.10f, 0.12f), red = new Color(0.68f, 0.07f, 0.05f), yellow = new Color(0.98f, 0.78f, 0.12f);
            SaPanel(t, new Vector3(7.7f, 5.05f, -SaT - 0.16f), Quaternion.identity, 11.4f, 1.1f, dark, h =>
            {
                SaText(h, "AUTOSALONKI", new Vector3(0, 0.17f, 0), Quaternion.identity, 0.08f, gold, TextAnchor.MiddleCenter);
                SaText(h, "KORJAAMO · VUOKRAUS · LOCATION · RENTAL", new Vector3(0, -0.36f, 0), Quaternion.identity, 0.026f, Color.white, TextAnchor.MiddleCenter, FontStyle.Normal);
            }, 110f);
            SaPanel(t, saSignAt, Quaternion.identity, 2.7f, 0.9f, dark, h =>
            {
                SaText(h, "AUTOSALONKI", new Vector3(0, 0.12f, 0), Quaternion.identity, 0.04f, gold, TextAnchor.MiddleCenter);
                SaText(h, "KORJAAMO · VUOKRAUS", new Vector3(0, -0.24f, 0), Quaternion.identity, 0.018f, Color.white, TextAnchor.MiddleCenter, FontStyle.Normal);
            }, 200f);
            SaPanel(t, new Vector3(20.3f, 2.75f, -SaT - 0.06f), Quaternion.identity, 1.9f, 0.5f, red, h =>
                SaText(h, "VUOKRAUS", Vector3.zero, Quaternion.identity, 0.03f, Color.white, TextAnchor.MiddleCenter), 300f);
            // dessus du bureau, vers l'atelier : face ouest et face nord
            SaPanel(t, new Vector3(SaOx - 0.12f, SaOh + 0.75f, 4.3f), Quaternion.Euler(0, 90f, 0), 3.6f, 1.0f, red, h =>
            {
                SaText(h, "KORJAAMO", new Vector3(0, 0.1f, 0), Quaternion.identity, 0.055f, Color.white, TextAnchor.MiddleCenter);
                SaText(h, "HUOLTO · ÖLJYNVAIHTO", new Vector3(0, -0.28f, 0), Quaternion.identity, 0.022f, Color.white, TextAnchor.MiddleCenter, FontStyle.Normal);
            }, 160f);
            SaPanel(t, new Vector3(20.6f, SaOh + 0.7f, SaOz + 0.14f), Quaternion.Euler(0, 180f, 0), 4.6f, 0.9f, yellow, h =>
            {
                SaText(h, Lang.T("LOCATION DE VOITURES", "CAR RENTAL"), new Vector3(0, 0.1f, 0), Quaternion.identity, 0.036f, dark, TextAnchor.MiddleCenter);
                SaText(h, Lang.T("au bureau →", "at the office →"), new Vector3(0, -0.25f, 0), Quaternion.identity, 0.02f, dark, TextAnchor.MiddleCenter, FontStyle.Normal);
            }, 160f);
            SaPanel(t, new Vector3(SaOx - 0.08f, 2.3f, 0.75f), Quaternion.Euler(0, 90f, 0), 1.1f, 0.38f, yellow, h =>
                SaText(h, "TOIMISTO", Vector3.zero, Quaternion.identity, 0.022f, dark, TextAnchor.MiddleCenter), 300f);
            // mur gauche : affiches d'atelier
            string[][] posters = { new[] { "MOOTTORIÖLJY", "SAE 20W-50" }, new[] { "RENKAAT", "KESÄ · TALVI" }, new[] { "ALL. KATSASTUS", "MA-PE 8-16" } };
            Color[] bgs = { red, new Color(0.08f, 0.22f, 0.5f), yellow };
            for (int i = 0; i < posters.Length; i++)
            {
                string[] pp = posters[i]; Color ink = i == 2 ? dark : Color.white;
                SaPanel(t, new Vector3(0.05f, 2.6f + (i % 2) * 0.25f, 3.5f + i * 3.6f), Quaternion.Euler(0, -90f, 0), 1.5f, 0.9f, bgs[i], h =>
                {
                    SaText(h, pp[0], new Vector3(0, 0.12f, 0), Quaternion.identity, 0.026f, ink, TextAnchor.MiddleCenter);
                    SaText(h, pp[1], new Vector3(0, -0.2f, 0), Quaternion.identity, 0.017f, ink, TextAnchor.MiddleCenter, FontStyle.Normal);
                }, 220f);
            }
            // bureau : les voitures du catalogue encadrees au mur du fond (face au client), comme les photos du garage
            int n = 0;
            foreach (Stand s in stands)
            {
                s.Poster = SalonkiPoster(s);
                if (s.Poster == null || n >= 8) continue;
                float x = SaW - 0.03f, z = 1.0f + (n % 4) * 0.95f, y = 1.55f + (n / 4) * 0.62f;
                Box(new Vector3(x - 0.02f, y - 0.25f, z - 0.42f), new Vector3(x + 0.02f, y + 0.25f, z + 0.42f), "sa_tumma", false);
                SaQuad(t, new Vector3(x - 0.025f, y, z), Quaternion.Euler(0, 90f, 0), 0.76f, 0.43f, s.Poster, "sa_kuva");
                n++;
            }
            Flush();
        }

        // ---------------------------------------------------------------- voitures de l'atelier
        static readonly int[] SaPaint = { 5, 1, 2, 3, 9, 6, 7, 14, 13, 4, 8, 10, 11, 12, 15, 16 };

        static void SalonkiCars()
        {
            Transform t = salonki.transform;
            for (int i = 0; i < stands.Count && i < SaSlots.Length; i++)
            {
                Stand s = stands[i]; Slot sl = SaSlots[i];
                var pivot = new GameObject("sa_paikka_" + s.Folder).transform;
                pivot.SetParent(t, false);
                pivot.localPosition = new Vector3(sl.X, sl.Lift ? SaLiftH : 0.0f, sl.Z);
                pivot.localRotation = Quaternion.Euler(0, sl.Yaw, 0);
                if (sl.Lift) SaLift(t, new Vector3(sl.X, 0f, sl.Z), sl.Yaw);
                try
                {
                    s.Number = SaPaint[i % SaPaint.Length];
                    GameObject car = VazCar.BuildPreview(s.Number, Vaz.ColorOf(s.Number), 0, out s.Hold, VazCar.ModelOf(s.Model));
                    if (car == null) { L("salon : apercu impossible pour " + s.Name); continue; }
                    car.transform.SetParent(pivot, false);
                    car.transform.localPosition = Vector3.zero; car.transform.localRotation = Quaternion.identity;
                    // (encombrement des pieces a moins de 8 m de la place : les pieces vides d'un addon -- matieres dont tous les
                    // triangles sont partis dans un autre fichier -- ont des bornes a l'origine du monde, essai du 30/10)
                    Bounds bb = new Bounds(); bool any = false;
                    foreach (Renderer r in car.GetComponentsInChildren<Renderer>(true))
                    {
                        if ((r.bounds.center - pivot.position).sqrMagnitude > 64f) continue;
                        if (!any) { bb = r.bounds; any = true; } else bb.Encapsulate(r.bounds);
                    }
                    if (!any) continue;
                    Vector3 lc = pivot.InverseTransformPoint(bb.center), lmin = pivot.InverseTransformPoint(bb.min);
                    car.transform.localPosition = new Vector3(-lc.x, -lmin.y, -lc.z);
                    // on ne la traverse pas (l'apercu n'a pas de collisions) : une boite a ses dimensions
                    Vector3 ext = pivot.InverseTransformVector(bb.size);
                    var bc = pivot.gameObject.AddComponent<BoxCollider>();
                    bc.size = new Vector3(Mathf.Abs(ext.x) * 0.92f, Mathf.Abs(ext.y) * 0.9f, Mathf.Abs(ext.z) * 0.95f);
                    bc.center = new Vector3(0f, bc.size.y / 2f + 0.05f, 0f);
                }
                catch (Exception e) { L("salon : voiture " + s.Name + " : " + e.Message); }
            }
            if (stands.Count > SaSlots.Length) L("salon : " + (stands.Count - SaSlots.Length) + " voiture(s) au menu seulement (atelier plein)");
        }

        // pont a deux colonnes (bleu) autour de la place : colonnes de chaque cote de la voiture, traverse haute, chariots et bras sous
        // les bas de caisse a SaLiftH
        static void SaLift(Transform t, Vector3 at, float yaw)
        {
            var g = new GameObject("sa_nostin"); g.transform.SetParent(t, false);
            g.transform.localPosition = at; g.transform.localRotation = Quaternion.Euler(0, yaw, 0);
            Transform l = g.transform;
            Material blue = mats["sa_sininen"], steel = mats["sa_teras"], dark = mats["sa_tumma"];
            foreach (float s in new[] { -1f, 1f })
            {
                float x = s * 1.5f;
                SaPrim(l, PrimitiveType.Cube, new Vector3(x, 1.95f, 0f), new Vector3(0.28f, 3.9f, 0.34f), blue, null, true);
                SaPrim(l, PrimitiveType.Cube, new Vector3(x, 0.01f, 0f), new Vector3(0.6f, 0.02f, 0.62f), steel);
                SaPrim(l, PrimitiveType.Cube, new Vector3(x - s * 0.2f, SaLiftH - 0.22f, 0f), new Vector3(0.16f, 0.44f, 0.3f), dark);
                foreach (float f in new[] { -1f, 1f })
                {
                    Vector3 a = new Vector3(x - s * 0.24f, SaLiftH - 0.07f, f * 0.1f), b = new Vector3(s * 0.62f, SaLiftH - 0.07f, f * 1.15f);
                    SaPrim(l, PrimitiveType.Cube, (a + b) / 2f, new Vector3(0.09f, 0.07f, (b - a).magnitude), steel, Quaternion.LookRotation(b - a));
                    SaPrim(l, PrimitiveType.Cylinder, b + new Vector3(0, 0.03f, 0), new Vector3(0.14f, 0.03f, 0.14f), dark);
                }
                // boitier du moteur sur la colonne (cote exterieur)
                SaPrim(l, PrimitiveType.Cube, new Vector3(x + s * 0.24f, 1.3f, 0.12f), new Vector3(0.2f, 0.4f, 0.24f), steel);
            }
            SaPrim(l, PrimitiveType.Cube, new Vector3(0f, 3.98f, 0f), new Vector3(3.3f, 0.16f, 0.22f), blue);
        }

        static void SaCyl(Transform t, Vector3 c, float diam, float h, Material m)
        {
            SaPrim(t, PrimitiveType.Cylinder, c, new Vector3(diam, h / 2f, diam), m);
        }

        static void SaTube(Transform t, Vector3 a, Vector3 b, float diam, Material m)
        {
            SaPrim(t, PrimitiveType.Cylinder, (a + b) / 2f, new Vector3(diam, (b - a).magnitude / 2f, diam), m, Quaternion.FromToRotation(Vector3.up, b - a));
        }

        // ---------------------------------------------------------------- vendeur
        static void SalonkiVendor()
        {
            saVendor = Avatar.CreatePreview("SALONKI-myyja", 0);
            if (saVendor == null) { L("salon : vendeur indisponible (corps de PNJ)"); }
            else
            {
                Transform r = saVendor.Root.transform;
                r.SetParent(salonki.transform, false);
                r.localPosition = SaVendorSpot;
                r.localRotation = Quaternion.Euler(0, 180f, 0);   // (face au client, vers la porte de la rue)
            }
            var cg = new GameObject("sa_myyja_coll");
            cg.transform.SetParent(salonki.transform, false);
            cg.transform.localPosition = SaVendorSpot + new Vector3(0f, 0.9f, 0f);
            var cc = cg.AddComponent<CapsuleCollider>(); cc.height = 1.8f; cc.radius = 0.3f;
            saVendorCol = cc;
            L("salon : vendeur " + (saVendor != null ? "en place" : "absent (collision seule)"));
        }

        // image du menu et des cadres : affiche.png du dossier de l'addon (JD), sinon son apercu (My Summer Car d'abord)
        static Texture2D SalonkiPoster(Stand s)
        {
            if (s.Poster != null) return s.Poster;
            VazCar.Model m = VazCar.ModelOf(s.Model);
            foreach (string f in new[] { "affiche.png", "apercu-msc.png", "apercu.png" })
            {
                string p = Path.Combine(m.Dir, f);
                if (!File.Exists(p)) continue;
                try
                {
                    var tex = new Texture2D(2, 2, TextureFormat.RGB24, true) { name = "sa_" + s.Folder + "_" + f };
                    tex.LoadImage(File.ReadAllBytes(p)); tex.wrapMode = TextureWrapMode.Clamp; tex.anisoLevel = 4;
                    return tex;
                }
                catch (Exception e) { L("salon : image " + p + " : " + e.Message); }
            }
            return null;
        }

        // reglettes de tubes fluorescents sous les poutrelles (comme l'inspection), plafonnier du bureau
        static void SalonkiLights()
        {
            Transform t = salonki.transform;
            Material white = mats["sa_valkoinen"], dark = mats["sa_tumma"];
            foreach (Vector3 p in new[] { new Vector3(4.5f, 0, 4.6f), new Vector3(11.5f, 0, 4.6f), new Vector3(4.5f, 0, 11.4f), new Vector3(11.5f, 0, 11.4f),
                                          new Vector3(4.5f, 0, 17.6f), new Vector3(11.5f, 0, 17.6f), new Vector3(19.5f, 0, 12.4f), new Vector3(19.5f, 0, 18.2f) })
            {
                float y = SaH - 0.62f;
                foreach (float dx in new[] { -0.7f, 0.7f }) SaTube(t, new Vector3(p.x + dx, SaH - 0.46f, p.z), new Vector3(p.x + dx, y, p.z), 0.015f, dark);
                SaPrim(t, PrimitiveType.Cube, new Vector3(p.x, y, p.z), new Vector3(1.8f, 0.07f, 0.2f), mats["sa_kehys"]);
                SaPrim(t, PrimitiveType.Cube, new Vector3(p.x, y - 0.05f, p.z), new Vector3(1.7f, 0.045f, 0.08f), mats["sa_valo"]);
                var g = new GameObject("sa_valo"); g.transform.SetParent(t, false);
                g.transform.localPosition = new Vector3(p.x, y - 0.4f, p.z);
                var l = g.AddComponent<Light>(); l.type = LightType.Point; l.range = 11f; l.intensity = 1.15f; l.color = new Color(1f, 0.95f, 0.86f); l.shadows = LightShadows.None;
            }
            foreach (Vector3 p in saLampSpots)
            {
                var g = new GameObject("sa_pihavalo"); g.transform.SetParent(t, false); g.transform.localPosition = p;
                var l = g.AddComponent<Light>(); l.type = LightType.Point; l.range = 16f; l.intensity = 0.9f; l.color = new Color(1f, 0.92f, 0.78f); l.shadows = LightShadows.None;
            }
            var og = new GameObject("sa_toimisto_valo"); og.transform.SetParent(t, false);
            og.transform.localPosition = new Vector3((SaOx + SaW) / 2f, SaOh - 0.3f, 3.6f);
            var ol = og.AddComponent<Light>(); ol.type = LightType.Point; ol.range = 7f; ol.intensity = 1.0f; ol.color = new Color(1f, 0.97f, 0.92f); ol.shadows = LightShadows.None;
            SaPrim(t, PrimitiveType.Cube, new Vector3((SaOx + SaW) / 2f, SaOh - 0.02f, 3.6f), new Vector3(1.2f, 0.04f, 0.3f), mats["sa_valo"]);
        }

        // place de livraison libre devant les portes (monde) ; cap : nez vers la route
        static bool SalonkiBay(out Vector3 pos, out float yaw)
        {
            Transform t = salonki.transform;
            yaw = t.eulerAngles.y + 180f;
            for (int k = 0; k < SaBays; k++)
            {
                Vector3 p = SaGround(t.TransformPoint(new Vector3(SaBay0 + (k + 0.5f) * SaBayW, 0.4f, -7.5f)));
                bool free = true;
                foreach (Collider c in Physics.OverlapSphere(p + Vector3.up * 0.8f, 1.6f))
                    if (!c.isTrigger && c.attachedRigidbody != null) { free = false; break; }
                if (free) { pos = p; return true; }
            }
            pos = SaGround(t.TransformPoint(new Vector3(SaBay0 + 0.5f * SaBayW, 0.4f, -7.5f)));
            return false;
        }
        static Vector3 SaGround(Vector3 p)
        {
            RaycastHit? g = GroundHit(p + Vector3.up * 6f);
            if (g.HasValue) p.y = g.Value.point.y + 0.5f;
            return p;
        }

        // ---------------------------------------------------------------- parking (JD 30/10 : murets, entree collee a la route, plus en l'air)
        // asphalte qui suit le sol (grille de 1 m, comme celui du cinema), rampe jusqu'au seuil des portes, places marquees ; muret de brique
        // cote route (ouverture de 7,8 m, piliers) et cote est ; a l'ouest, le muret du parking du cinema ; allee de l'ouverture jusqu'au
        // bord de la route la plus proche (sondee : Clearing.IsRoad) ; panneau et deux lampadaires.
        static void SalonkiParking()
        {
            Transform tr = salonki.transform;
            Func<float, float, float> gy = (x, z) =>
            {
                RaycastHit? h = GroundHit(tr.TransformPoint(new Vector3(x, 50f, z)));
                return h.HasValue ? tr.InverseTransformPoint(h.Value.point).y : -0.3f;
            };
            float mz = -(SaPark + 0.3f), ex = SaW + 1.8f, gate0 = 8.0f, gate1 = 15.8f;
            HeightGrid("ki_asfaltti", -0.3f, ex - 0.15f, mz + 0.15f, -1.6f, 1f, gy, 0.1f);
            // rampe : du parking au seuil (y 0) sur 1,2 m, toute la largeur
            for (float x = -0.3f; x < ex - 0.16f; x += 1f)
            {
                float xb = Mathf.Min(x + 1f, ex - 0.15f);
                Quad("ki_asfaltti", new Vector3(x, gy(x, -1.6f) + 0.1f, -1.6f), new Vector3(xb, gy(xb, -1.6f) + 0.1f, -1.6f), new Vector3(xb, -0.02f, -0.4f), new Vector3(x, -0.02f, -0.4f));
            }
            for (int k = 0; k <= SaBays; k++) { float x = SaBay0 + k * SaBayW; Line(x, mz + 1.2f, x, -4.5f, gy); }
            SaWallRun(-0.3f, mz, gate0, mz, gy); SaWallRun(gate1, mz, ex, mz, gy); SaWallRun(ex, mz, ex, -0.4f, gy);
            foreach (Vector2 p in new[] { new Vector2(gate0, mz), new Vector2(gate1, mz), new Vector2(ex, mz), new Vector2(ex, -0.4f) }) SaPillar(p.x, p.y, gy);
            // allee jusqu'a la route
            float cx = (gate0 + gate1) / 2f;
            Vector3 from = new Vector3(cx, 0f, mz + 0.4f), best = new Vector3(cx, 0f, mz - 8f);
            float bd = float.MaxValue;
            for (float dz = 1f; dz <= 26f; dz += 1f)
                for (float dx = -14f; dx <= 14f; dx += 1f)
                {
                    Vector3 p = new Vector3(cx + dx, 0f, mz - dz), w = tr.TransformPoint(p);
                    if (!Clearing.IsRoad(w.x, w.z)) continue;
                    float d = (p - from).sqrMagnitude;
                    if (d < bd) { bd = d; best = p; }
                }
            KinoDriveway(new[] { from, best }, gy);
            L("salon : allee vers la route " + (bd < float.MaxValue ? "a " + Mathf.Sqrt(bd).ToString("F1") + " m" : ": pas de route trouvee"));
            // pylone de brique de l'enseigne a l'entree (comme la haute tour de brique de l'inspection), panneau entre ses deux piles
            float sg = gy(gate1 + 2.2f, mz - 0.8f);
            foreach (float x in new[] { gate1 + 0.6f, gate1 + 3.8f }) Box(new Vector3(x - 0.25f, sg - 0.4f, mz - 1.05f), new Vector3(x + 0.25f, sg + 5.4f, mz - 0.55f), "sa_tiili");
            Box(new Vector3(gate1 + 0.3f, sg + 5.4f, mz - 1.1f), new Vector3(gate1 + 4.1f, sg + 5.7f, mz - 0.5f), "sa_tiili");
            Box(new Vector3(gate1 + 0.25f, sg + 5.7f, mz - 1.15f), new Vector3(gate1 + 4.15f, sg + 5.8f, mz - 0.45f), "sa_betoni", false);
            saSignAt = new Vector3(gate1 + 2.2f, sg + 3.6f, mz - 1.08f);
            // lampadaires le long du muret, bras vers le garage
            saLampSpots.Clear();
            foreach (float x in new[] { 3.5f, 20.5f })
            {
                Vector3 f = new Vector3(x, gy(x, mz + 0.6f), mz + 0.6f);
                Box(new Vector3(f.x - 0.08f, f.y - 0.3f, f.z - 0.08f), new Vector3(f.x + 0.08f, f.y + 5.0f, f.z + 0.08f), "sa_tumma");
                Box(new Vector3(f.x - 0.08f, f.y + 4.9f, f.z), new Vector3(f.x + 0.08f, f.y + 5.0f, f.z + 1.4f), "sa_tumma", false);
                Box(new Vector3(f.x - 0.2f, f.y + 4.78f, f.z + 1.1f), new Vector3(f.x + 0.2f, f.y + 4.92f, f.z + 1.6f), "sa_valkoinen", false);
                saLampSpots.Add(new Vector3(f.x, f.y + 4.6f, f.z + 1.35f));
            }
        }
        static Vector3 saSignAt;
        static readonly List<Vector3> saLampSpots = new List<Vector3>();

        // muret de brique (0,9 m, chaperon de beton) par troncons de 2 m poses sur le sol
        static void SaWallRun(float xa, float za, float xb, float zb, Func<float, float, float> gy)
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
                Box(mn, mx, "sa_tiili");
                Box(new Vector3(mn.x - (alongX ? 0f : 0.04f), mx.y, mn.z - (alongX ? 0.04f : 0f)), new Vector3(mx.x + (alongX ? 0f : 0.04f), mx.y + 0.08f, mx.z + (alongX ? 0.04f : 0f)), "sa_betoni", false);
            }
        }

        static void SaPillar(float x, float z, Func<float, float, float> gy)
        {
            float g = gy(x, z);
            Box(new Vector3(x - 0.25f, g - 0.5f, z - 0.25f), new Vector3(x + 0.25f, g + 1.3f, z + 0.25f), "sa_tiili");
            Box(new Vector3(x - 0.3f, g + 1.3f, z - 0.3f), new Vector3(x + 0.3f, g + 1.4f, z + 0.3f), "sa_betoni", false);
        }

        // ---------------------------------------------------------------- chaque image : vendeur vise
        static void TickSalonki(float now)
        {
            if (salonki == null) return;
            Camera cam = Game.MainCam;
            bool near = cam != null && (cam.transform.position - salonki.transform.position).sqrMagnitude < (SaD + 40f) * (SaD + 40f);
            if (guiText == null)
            {
                var gv = FsmVariables.GlobalVariables;
                guiUse = gv.FindFsmBool("GUIuse"); guiBuy = gv.FindFsmBool("GUIbuy"); guiText = gv.FindFsmString("GUIinteraction");
                money = gv.FindFsmFloat("PlayerMoney"); guiSub = gv.FindFsmString("GUIsubtitle");
            }
            bool hit = false;
            if (near && !SalonkiOpen && saVendorCol != null && !Game.GlobalBool("PlayerSleeps"))
            {
                RaycastHit h;
                hit = saVendorCol.Raycast(new Ray(cam.transform.position, cam.transform.forward), out h, 2.8f);
            }
            if (hit != saLooked) { if (saLooked) ClearGui(); saLooked = hit; }
            if (!hit) return;
            guiUse.Value = true;
            guiText.Value = Lang.T("LOCATION DE VOITURES", "CAR RENTAL");
            if (Input.GetMouseButtonDown(0)) { ClearGui(); saLooked = false; OpenSalonki(saMenu ?? stands[0]); }
        }
    }
}
