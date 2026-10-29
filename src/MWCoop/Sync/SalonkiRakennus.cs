using System;
using System.Collections.Generic;
using System.IO;
using HutongGames.PlayMaker;
using UnityEngine;

namespace MWCoop
{
    // AUTOSALONKI -- salle d'exposition et de location des voitures du F10 (demande de JD, 29/10 : « comme dans un musee », chaque
    // voiture sur un plateau tournant, entouree de potelets dores et de cordons rouges, une pancarte fixe devant, une grande affiche
    // derriere facon salon de l'auto de Paris ; « loue » a la pancarte, prix d'apres les performances ; le menu de location remplace
    // la prise au F10). My Summer Car d'abord. A l'est du cinema KINO TAHTI, meme route, meme alignement : la prairie au nord de la
    // route y est plate (releve du 29/10 : 1,1 m de denivele sur 120 x 80 m, aucun objet, plus de 100 m libres vers l'est et 200 m
    // vers le nord).
    // Repere propre : x vers l'est (le long de la route), z vers le nord (le fond), y = 0 au plancher ; facade vitree a z = 0 sur
    // l'alignement de celle du cinema, parking de livraison devant (z < 0). Longueur d'apres le catalogue, rangee par categorie
    // (MWCoop, puis Gr. A, Gr. B...) : hall d'entree et comptoir a l'ouest, puis chaque categorie, son titre sur le mur du fond,
    // un stand de 11 m par voiture. Une voiture de plus au catalogue = un stand de plus, le batiment s'allonge vers l'est.
    // Stand : plateau tournant (rayon 2,8 m), potelets dores et cordons rouges (anneau de 3,4 m, collisions), pancarte inclinee devant
    // (visee + clic : le menu de location, SalonkiMenu), affiche derriere sur le mur du fond (affiche.png du dossier de l'addon, sinon
    // son apercu-msc.png / apercu.png ; 16:9, JD les fera), spot au plafond.
    // [Salonki] Paikka=x,z,cap : place de l'angle sud-ouest (facade, cote cinema) et cap ; Active=0 : pas de salle.
    public static partial class Kauppa
    {
        const float SaDepth = 18f, SaH = 7f, SaLobby = 12f, SaStand = 11f, SaGap = 3f, SaEnd = 3f, SaPark = 14f;
        const float SaDiscR = 2.8f, SaRopeR = 3.4f, SaDiscZ = 9f, SaSignZ = 4.4f;

        public static bool SalonkiOn { get { return Game.Msc && Ajouts.Salonki && Config.GetInt("Salonki", "Active", 1) != 0; } }

        class Stand
        {
            public int Model, Number; public string Name, Group, Folder; public int Price;
            public Transform Disc; public Collider Sign; public Texture2D Poster; public float U;
            public VazCar.Built Hold;
        }
        static GameObject salonki;
        static readonly List<Stand> stands = new List<Stand>();
        static float saLen;
        static Stand saLooked;
        static Font saFont;

        static void ResetSalonki()
        {
            foreach (Stand s in stands) { if (s.Hold != null) VazCar.DestroyPreview(s.Hold); if (s.Poster != null) UnityEngine.Object.Destroy(s.Poster); }
            stands.Clear(); salonki = null; saLooked = null; SalonkiOpen = false; saMenu = null;
        }

        // angle sud-ouest de la salle (facade, cote cinema) et cap du repere (x vers l'est)
        public static void SalonkiFrame(out Vector3 o, out float yaw)
        {
            string s = Config.Get("Salonki", "Paikka", "");
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            if (s.Length > 0)
                try { string[] a = s.Split(','); o = new Vector3(float.Parse(a[0], ci), 0f, float.Parse(a[1], ci)); yaw = float.Parse(a[2], ci); return; }
                catch { L("[Salonki] Paikka illisible : " + s); }
            // (a 18 m a l'est du pignon est du cinema, sur l'alignement de sa facade ; son repere a x vers l'ouest et z vers la route)
            Vector3 c; float ky; string fl;
            KinoFrame(out c, out ky, out fl);
            o = c + Quaternion.Euler(0, ky, 0) * new Vector3(-(KL + 18f), 0f, KD);
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
            Mat("sa_lattia", "paint_black_matte", 1f, new Color(0.80f, 0.80f, 0.78f));   // dalles claires
            Mat("sa_seina", "paint_black_matte", 1f, new Color(0.93f, 0.93f, 0.91f));    // murs blancs
            Mat("sa_tumma", "paint_black_matte", 1f, new Color(0.12f, 0.12f, 0.14f));    // bandeaux, plateaux, menuiseries
            Mat("sa_katto", "paint_black_matte", 1f, new Color(0.36f, 0.36f, 0.38f));
            Mat("sa_kulta", "paint_black_matte", 1f, new Color(0.86f, 0.68f, 0.28f));    // laiton des potelets
            Mat("sa_naru", "paint_black_matte", 1f, new Color(0.62f, 0.05f, 0.08f));     // cordons rouges
            Mat("sa_tiski", "wood12", 1f);
            Mat("sa_asfaltti", "mwc_asphalt", 7f);
            Mat("sa_viiva", "paint_black_matte", 1f, new Color(0.95f, 0.95f, 0.92f));
            Mat("sa_betoni", "paint_black_matte", 1f, new Color(0.66f, 0.66f, 0.64f));
            if (!mats.ContainsKey("lasi")) Mat("lasi", "psk_glass", 1f);
        }

        static void BuildSalonki()
        {
            Vector3 o; float yaw;
            SalonkiFrame(out o, out yaw);
            Quaternion rot = Quaternion.Euler(0, yaw, 0);
            stands.Clear();
            stands.AddRange(SalonkiCatalog());
            if (stands.Count == 0) { L("salon : aucune voiture au catalogue"); return; }
            // longueur : hall, puis les categories (un stand par voiture, un passage entre deux categories)
            float u = SaLobby; string g = null;
            foreach (Stand s in stands)
            {
                if (g != null && s.Group != g) u += SaGap;
                g = s.Group; s.U = u + SaStand / 2f; u += SaStand;
            }
            saLen = u + SaEnd;
            SalonkiMaterials();
            // plancher au-dessus de tout le sol sous l'emprise (parking compris)
            float gMax = float.MinValue, gMin = float.MaxValue;
            for (float x = 0f; x <= saLen; x += 3f)
                for (float z = -SaPark; z <= SaDepth; z += 3f)
                {
                    RaycastHit? h = GroundHit(o + rot * new Vector3(x, 0, z) + Vector3.up * 200f);
                    if (!h.HasValue || kino != null && h.Value.collider.transform.IsChildOf(kino.transform)) continue;
                    gMax = Mathf.Max(gMax, h.Value.point.y); gMin = Mathf.Min(gMin, h.Value.point.y);
                }
            if (gMax == float.MinValue) gMax = gMin = 3f;
            float floorY = gMax + 0.2f, down = floorY - gMin + 0.6f;
            L(string.Format("salon : {0} voiture(s), {1:F0} m de long, sol {2:F2}..{3:F2}, plancher a {4:F2}", stands.Count, saLen, gMin, gMax, floorY));

            salonki = new GameObject("MWCoop AUTOSALONKI");
            salonki.transform.position = new Vector3(o.x, floorY, o.z);
            salonki.transform.rotation = rot;
            var keepRoot = root; root = salonki;
            try
            {
                SalonkiShell(down);
                Flush();
                SalonkiSigns();
                SalonkiStands();
                SalonkiLights();
            }
            finally { root = keepRoot; }
            L("salon construit en " + salonki.transform.position.ToString("F1") + ", cap " + yaw.ToString("F1"));
        }

        // coque : socle, plancher, murs, facade vitree (porte ouverte dans le hall), toit, bandeau, parking de livraison
        static void SalonkiShell(float down)
        {
            float L = saLen, D = SaDepth, H = SaH, w = 0.3f;
            Box(new Vector3(-w, -down, -0.4f), new Vector3(L + w, -0.02f, D + w), "sa_betoni");
            Box(new Vector3(0f, -0.02f, 0f), new Vector3(L, 0f, D), null, null, "sa_lattia", null, null, null);
            Box(new Vector3(-w, 0f, D), new Vector3(L + w, H, D + w), "sa_seina");     // fond (affiches)
            Box(new Vector3(-w, 0f, 0f), new Vector3(0f, H, D), "sa_seina");           // pignon ouest
            Box(new Vector3(L, 0f, 0f), new Vector3(L + w, H, D), "sa_seina");          // pignon est
            Box(new Vector3(-w, H, -0.6f), new Vector3(L + w, H + 0.5f, D + w), "sa_katto");   // toit, debord sur la facade
            Box(new Vector3(-w, H - 1.6f, -0.12f), new Vector3(L + w, H, 0.05f), "sa_tumma");  // bandeau de l'enseigne
            // facade vitree : montants tous les 3 m, porte de 3 m dans le hall (x 4,5 a 7,5), soubassement sombre
            float doorA = 4.5f, doorB = 7.5f;
            for (float x = 0f; x <= L + 0.01f; x += 3f)
                if (x < doorA - 0.01f || x > doorB + 0.01f || Mathf.Abs(x - doorA) < 0.01f || Mathf.Abs(x - doorB) < 0.01f)
                    Box(new Vector3(x - 0.08f, 0f, -0.08f), new Vector3(x + 0.08f, H - 1.6f, 0.08f), "sa_tumma");
            foreach (float[] seg in new[] { new[] { 0f, doorA }, new[] { doorB, L } })
            {
                if (seg[1] - seg[0] < 0.1f) continue;
                Box(new Vector3(seg[0], 0f, -0.05f), new Vector3(seg[1], 0.4f, 0.05f), "sa_tumma");
                Box(new Vector3(seg[0], 0.4f, -0.02f), new Vector3(seg[1], H - 1.6f, 0.02f), "lasi");
            }
            Box(new Vector3(doorA, 3.2f, -0.05f), new Vector3(doorB, H - 1.6f, 0.05f), "lasi");   // imposte au-dessus de la porte
            // parking de livraison devant la facade, marches
            Box(new Vector3(-2f, -down, -SaPark), new Vector3(L + 2f, -0.25f, -0.4f), "sa_betoni", "sa_betoni", "sa_asfaltti", null, "sa_betoni", null);
            Box(new Vector3(doorA - 1f, -0.25f, -1.2f), new Vector3(doorB + 1f, -0.02f, -0.4f), "sa_betoni");
            for (int k = 0; k < SaBays; k++)
            {
                float x = SaBay0 + k * SaBayW;
                Box(new Vector3(x - 0.06f, -0.249f, -SaPark + 1f), new Vector3(x + 0.06f, -0.245f, -2f), "sa_viiva", false);
            }
            // comptoir de location dans le hall
            Box(new Vector3(1.5f, 0f, D - 5.5f), new Vector3(9.5f, 1.05f, D - 4.6f), "sa_tiski");
            Box(new Vector3(1.4f, 1.05f, D - 5.6f), new Vector3(9.6f, 1.12f, D - 4.5f), "sa_tumma");
        }
        const int SaBays = 6; const float SaBay0 = 14f, SaBayW = 4f;

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

        // enseigne de la facade, titres des categories sur le mur du fond
        static void SalonkiSigns()
        {
            Transform t = salonki.transform;
            SaText(t, "AUTOSALONKI", new Vector3(saLen / 2f, SaH - 0.85f, -0.14f), Quaternion.identity, 0.11f, new Color(1f, 0.82f, 0.3f), TextAnchor.MiddleCenter);
            SaText(t, "VUOKRAUS · LOCATION · RENTAL", new Vector3(saLen / 2f, SaH - 1.38f, -0.14f), Quaternion.identity, 0.035f, Color.white, TextAnchor.MiddleCenter, FontStyle.Normal);
            SaText(t, "VUOKRAUS", new Vector3(5.5f, 2.6f, SaDepth - 0.05f - 0.0f), Quaternion.identity, 0.05f, new Color(0.15f, 0.15f, 0.17f), TextAnchor.MiddleCenter);
            string g = null; float start = 0f;
            for (int i = 0; i <= stands.Count; i++)
            {
                Stand s = i < stands.Count ? stands[i] : null;
                if (s != null && s.Group == g) continue;
                if (g != null)
                {
                    float a = start - SaStand / 2f + 0.5f, b = stands[i - 1].U + SaStand / 2f - 0.5f;
                    var band = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    UnityEngine.Object.Destroy(band.GetComponent<Collider>());
                    band.name = "sa_kategoria"; band.transform.SetParent(t, false);
                    band.transform.localPosition = new Vector3((a + b) / 2f, SaH - 0.55f, SaDepth - 0.06f); band.transform.localScale = new Vector3(b - a, 0.7f, 0.04f);
                    band.GetComponent<Renderer>().sharedMaterial = mats["sa_tumma"];
                    SaText(t, CategoryTitle(g), new Vector3((a + b) / 2f, SaH - 0.55f, SaDepth - 0.09f), Quaternion.identity, 0.045f, new Color(1f, 0.82f, 0.3f), TextAnchor.MiddleCenter);
                }
                if (s != null) { g = s.Group; start = s.U; }
            }
        }

        static string CategoryTitle(string g)
        {
            if (g == "MWCoop") return Lang.T("VOITURES DE SÉRIE", "PRODUCTION CARS");
            if (g.StartsWith("Gr. ")) return Lang.T("GROUPE ", "GROUP ") + g.Substring(4).ToUpperInvariant();
            return g.ToUpperInvariant();
        }

        static readonly int[] SaPaint = { 5, 1, 2, 3, 9, 6, 7, 14, 13, 4, 8, 10, 11, 12, 15, 16 };

        static void SalonkiStands()
        {
            Transform t = salonki.transform;
            Material disc = mats["sa_tumma"], gold = mats["sa_kulta"], rope = mats["sa_naru"];
            for (int i = 0; i < stands.Count; i++)
            {
                Stand s = stands[i];
                Vector3 c = new Vector3(s.U, 0f, SaDiscZ);
                // plateau tournant
                var d = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                UnityEngine.Object.Destroy(d.GetComponent<Collider>());
                d.name = "sa_levy_" + s.Folder; d.transform.SetParent(t, false);
                d.transform.localPosition = c + new Vector3(0, 0.11f, 0); d.transform.localScale = new Vector3(SaDiscR * 2f, 0.11f, SaDiscR * 2f);
                d.GetComponent<Renderer>().sharedMaterial = disc;
                var pivot = new GameObject("sa_pyorija").transform;
                pivot.SetParent(t, false); pivot.localPosition = c + new Vector3(0, 0.22f, 0);
                s.Disc = pivot;
                // potelets dores et cordons rouges (10 potelets ; collisions le long des cordons)
                const int Posts = 10;
                for (int k = 0; k < Posts; k++)
                {
                    float a0 = k * Mathf.PI * 2f / Posts, a1 = (k + 1) * Mathf.PI * 2f / Posts;
                    Vector3 p0 = c + new Vector3(Mathf.Cos(a0), 0, Mathf.Sin(a0)) * SaRopeR, p1 = c + new Vector3(Mathf.Cos(a1), 0, Mathf.Sin(a1)) * SaRopeR;
                    SaCyl(t, p0 + new Vector3(0, 0.5f, 0), 0.05f, 1.0f, gold);
                    SaCyl(t, p0 + new Vector3(0, 0.02f, 0), 0.32f, 0.04f, gold);
                    var top = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    UnityEngine.Object.Destroy(top.GetComponent<Collider>());
                    top.transform.SetParent(t, false); top.transform.localPosition = p0 + new Vector3(0, 1.03f, 0); top.transform.localScale = Vector3.one * 0.1f;
                    top.GetComponent<Renderer>().sharedMaterial = gold;
                    // cordon : deux troncons qui pendent de 14 cm au milieu
                    Vector3 a = p0 + new Vector3(0, 0.92f, 0), b = p1 + new Vector3(0, 0.92f, 0), m = (a + b) / 2f - new Vector3(0, 0.14f, 0);
                    SaRope(t, a, m, rope); SaRope(t, m, b, rope);
                    var col = new GameObject("sa_naru_c"); col.transform.SetParent(t, false);
                    col.transform.localPosition = (p0 + p1) / 2f + new Vector3(0, 0.55f, 0);
                    col.transform.localRotation = Quaternion.LookRotation(p1 - p0);
                    col.AddComponent<BoxCollider>().size = new Vector3(0.1f, 1.1f, (p1 - p0).magnitude);
                }
                // pancarte inclinee devant l'anneau, fixe
                SalonkiSign(s, new Vector3(s.U, 0f, SaSignZ));
                // affiche sur le mur du fond
                s.Poster = SalonkiPoster(s);
                var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
                UnityEngine.Object.Destroy(q.GetComponent<Collider>());
                q.name = "sa_juliste_" + s.Folder; q.transform.SetParent(t, false);
                q.transform.localPosition = new Vector3(s.U, 3.3f, SaDepth - 0.04f); q.transform.localScale = new Vector3(7.2f, 4.05f, 1f);
                var pm = new Material(Shader.Find("Unlit/Texture") ?? Shader.Find("Legacy Shaders/Diffuse"));
                if (s.Poster != null) pm.mainTexture = s.Poster; else if (pm.HasProperty("_Color")) pm.color = new Color(0.2f, 0.2f, 0.22f);
                q.GetComponent<Renderer>().sharedMaterial = pm;
                if (s.Poster == null) SaText(t, s.Name.ToUpperInvariant(), new Vector3(s.U, 3.3f, SaDepth - 0.06f), Quaternion.identity, 0.06f, Color.white, TextAnchor.MiddleCenter);
                // la voiture, en apercu (sans physique) sur le plateau, de face au depart
                try
                {
                    s.Number = SaPaint[i % SaPaint.Length];
                    GameObject car = VazCar.BuildPreview(s.Number, Vaz.ColorOf(s.Number), 0, out s.Hold, VazCar.ModelOf(s.Model));
                    if (car != null)
                    {
                        car.transform.SetParent(pivot, false);
                        car.transform.localPosition = Vector3.zero; car.transform.localRotation = Quaternion.Euler(0, 180f, 0);
                        // (encombrement des pieces a moins de 8 m du plateau : une piece d'un addon donnait des bornes a l'origine du monde,
                        // la voiture partait a 400 m -- essai du 30/10)
                        Bounds bb = new Bounds(); bool any = false; string far = "";
                        foreach (Renderer r in car.GetComponentsInChildren<Renderer>(true))
                        {
                            if ((r.bounds.center - pivot.position).sqrMagnitude > 64f) { if (far.Length < 200) far += " " + r.name + r.bounds.center.ToString("F0"); continue; }
                            if (!any) { bb = r.bounds; any = true; } else bb.Encapsulate(r.bounds);
                        }
                        if (far.Length > 0) L("salon : " + s.Name + " : pieces loin du plateau ignorees :" + far);
                        if (any)
                        {
                            Vector3 lc = pivot.InverseTransformPoint(bb.center), lmin = pivot.InverseTransformPoint(bb.min);
                            car.transform.localPosition = new Vector3(-lc.x, -lmin.y, -lc.z);
                        }
                        int nr = car.GetComponentsInChildren<Renderer>(true).Length, on = 0;
                        foreach (Renderer r in car.GetComponentsInChildren<Renderer>(true)) if (r.enabled && r.gameObject.activeInHierarchy) on++;
                        L("salon : " + s.Name + " exposee : " + nr + " piece(s) (" + on + " visibles), taille " + bb.size.ToString("F1") + ", en " + car.transform.position.ToString("F1") + ", actif " + car.activeInHierarchy);
                    }
                    else L("salon : apercu impossible pour " + s.Name);
                }
                catch (Exception e) { L("salon : voiture " + s.Name + " : " + e.Message); }
            }
        }

        static void SaCyl(Transform t, Vector3 c, float diam, float h, Material m)
        {
            var g = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            UnityEngine.Object.Destroy(g.GetComponent<Collider>());
            g.transform.SetParent(t, false); g.transform.localPosition = c; g.transform.localScale = new Vector3(diam, h / 2f, diam);
            g.GetComponent<Renderer>().sharedMaterial = m;
        }

        static void SaRope(Transform t, Vector3 a, Vector3 b, Material m)
        {
            var g = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            UnityEngine.Object.Destroy(g.GetComponent<Collider>());
            g.transform.SetParent(t, false); g.transform.localPosition = (a + b) / 2f;
            g.transform.localRotation = Quaternion.FromToRotation(Vector3.up, b - a);
            g.transform.localScale = new Vector3(0.035f, (b - a).magnitude / 2f, 0.035f);
            g.GetComponent<Renderer>().sharedMaterial = m;
        }

        // pancarte : pied, plaque inclinee vers l'allee, texte ; son collider sert a la viser
        static void SalonkiSign(Stand s, Vector3 at)
        {
            Transform t = salonki.transform;
            SaCyl(t, at + new Vector3(0, 0.45f, 0), 0.06f, 0.9f, mats["sa_tumma"]);
            var plate = GameObject.CreatePrimitive(PrimitiveType.Cube);
            plate.name = "sa_kyltti_" + s.Folder; plate.transform.SetParent(t, false);
            plate.transform.localPosition = at + new Vector3(0, 1.0f, 0); plate.transform.localRotation = Quaternion.Euler(35f, 0, 0);
            plate.transform.localScale = new Vector3(0.9f, 0.6f, 0.03f);
            plate.GetComponent<Renderer>().sharedMaterial = mats["sa_tumma"];
            s.Sign = plate.GetComponent<Collider>();
            Transform face = new GameObject("sa_kyltti_teksti").transform;
            face.SetParent(plate.transform, false); face.localPosition = new Vector3(0, 0, -0.55f); face.localScale = new Vector3(1f / 0.9f, 1f / 0.6f, 1f / 0.03f);
            VazCar.Model m = VazCar.ModelOf(s.Model);
            Color gold = new Color(1f, 0.82f, 0.3f), soft = new Color(0.86f, 0.86f, 0.86f);
            SaText(face, s.Name, new Vector3(-0.4f, 0.2f, 0), Quaternion.identity, 0.0042f, Color.white, TextAnchor.UpperLeft);
            SaText(face, CategoryTitle(s.Group), new Vector3(-0.4f, 0.09f, 0), Quaternion.identity, 0.0026f, gold, TextAnchor.UpperLeft);
            string desc = m.Desc;
            if (desc.Length > 0) SaText(face, desc, new Vector3(-0.4f, 0.025f, 0), Quaternion.identity, 0.0022f, soft, TextAnchor.UpperLeft, FontStyle.Normal);
            SaText(face, Lang.T("LOCATION  ", "RENTAL  ") + s.Price + " MK", new Vector3(-0.4f, -0.12f, 0), Quaternion.identity, 0.0036f, gold, TextAnchor.UpperLeft);
            SaText(face, Lang.T("Clic : lire et louer", "Click: read and rent"), new Vector3(-0.4f, -0.22f, 0), Quaternion.identity, 0.0019f, soft, TextAnchor.UpperLeft, FontStyle.Normal);
        }

        // affiche : affiche.png du dossier de l'addon (JD), sinon son apercu (My Summer Car d'abord)
        static Texture2D SalonkiPoster(Stand s)
        {
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
                catch (Exception e) { L("salon : affiche " + p + " : " + e.Message); }
            }
            return null;
        }

        static void SalonkiLights()
        {
            Transform t = salonki.transform;
            foreach (Stand s in stands)
            {
                var g = new GameObject("sa_spotti"); g.transform.SetParent(t, false);
                g.transform.localPosition = new Vector3(s.U, SaH - 0.3f, SaDiscZ - 1.5f);
                g.transform.localRotation = Quaternion.LookRotation(new Vector3(0, -1f, 0.25f));
                var l = g.AddComponent<Light>(); l.type = LightType.Spot; l.spotAngle = 70f; l.range = 11f; l.intensity = 2.2f; l.color = new Color(1f, 0.96f, 0.9f); l.shadows = LightShadows.None;
                var pg = new GameObject("sa_juliste_valo"); pg.transform.SetParent(t, false);
                pg.transform.localPosition = new Vector3(s.U, 1.0f, SaDepth - 2.5f);
                var pl = pg.AddComponent<Light>(); pl.type = LightType.Point; pl.range = 6f; pl.intensity = 0.8f; pl.color = new Color(1f, 0.95f, 0.85f); pl.shadows = LightShadows.None;
            }
            for (float x = 4f; x < saLen; x += 10f)
            {
                var g = new GameObject("sa_valo"); g.transform.SetParent(t, false);
                g.transform.localPosition = new Vector3(x, SaH - 0.5f, 3.5f);
                var l = g.AddComponent<Light>(); l.type = LightType.Point; l.range = 12f; l.intensity = 1.1f; l.color = new Color(1f, 0.97f, 0.92f); l.shadows = LightShadows.None;
            }
        }

        // place de livraison libre devant la facade (monde) ; cap : nez vers la route
        static bool SalonkiBay(out Vector3 pos, out float yaw)
        {
            Transform t = salonki.transform;
            yaw = t.eulerAngles.y + 180f;
            for (int k = 0; k < SaBays; k++)
            {
                Vector3 p = t.TransformPoint(new Vector3(SaBay0 + (k + 0.5f) * SaBayW, 0.4f, -7.5f));
                bool free = true;
                foreach (Collider c in Physics.OverlapSphere(p + Vector3.up * 0.8f, 1.6f))
                    if (!c.isTrigger && c.attachedRigidbody != null) { free = false; break; }
                if (free) { pos = p; return true; }
            }
            pos = t.TransformPoint(new Vector3(SaBay0 + 0.5f * SaBayW, 0.4f, -7.5f));
            return false;
        }

        // ---------------------------------------------------------------- chaque image : plateaux, pancartes visees
        static void TickSalonki(float now)
        {
            if (salonki == null) return;
            float dt = Time.deltaTime;
            Camera cam = Game.MainCam;
            bool near = cam != null && (cam.transform.position - salonki.transform.position).sqrMagnitude < (saLen + 40f) * (saLen + 40f);
            if (near) foreach (Stand s in stands) if (s.Disc != null) s.Disc.Rotate(0f, 9f * dt, 0f, Space.Self);
            if (guiText == null)
            {
                var gv = FsmVariables.GlobalVariables;
                guiUse = gv.FindFsmBool("GUIuse"); guiBuy = gv.FindFsmBool("GUIbuy"); guiText = gv.FindFsmString("GUIinteraction");
                money = gv.FindFsmFloat("PlayerMoney"); guiSub = gv.FindFsmString("GUIsubtitle");
            }
            Stand hit = null;
            if (near && !SalonkiOpen && !Game.GlobalBool("PlayerSleeps"))
            {
                var ray = new Ray(cam.transform.position, cam.transform.forward);
                float best = 2.4f; RaycastHit h;
                foreach (Stand s in stands) if (s.Sign != null && s.Sign.Raycast(ray, out h, best)) { best = h.distance; hit = s; }
            }
            if (hit != saLooked) { if (saLooked != null) ClearGui(); saLooked = hit; }
            if (hit == null) return;
            guiUse.Value = true;
            guiText.Value = hit.Name.ToUpperInvariant() + " · " + Lang.T("LOCATION ", "RENTAL ") + hit.Price + " MK";
            if (Input.GetMouseButtonDown(0)) { ClearGui(); saLooked = null; OpenSalonki(hit); }
        }
    }
}
