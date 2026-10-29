using System;
using System.Collections.Generic;
using System.IO;
using HutongGames.PlayMaker;
using UnityEngine;

namespace MWCoop
{
    // AUTOSALONKI -- salle d'exposition et de location des voitures du F10 (demande de JD, 29/10 ; « loue » a la pancarte, prix d'apres
    // les performances ; le menu de location remplace la prise au F10). 2e version (JD 30/10, photo d'une exposition de voitures de
    // rallye : « un batiment plus petit, mais avec ce style de position » ; plus de plateaux tournants ni d'affiches) : les voitures
    // garees cote a cote en epi sur un grand tapis sombre, deux rangees qui s'ouvrent vers l'entree, potelets dores et cordons rouges
    // tout autour du tapis, une pancarte devant chaque voiture dans l'allee (visee + clic : le menu de location, SalonkiMenu).
    // My Summer Car d'abord. A l'est du cinema KINO TAHTI, meme route, meme alignement : la prairie au nord de la route y est plate
    // (releve du 29/10 : 1,1 m de denivele sur 120 x 80 m, aucun objet, 200 m libres vers le nord).
    // Repere propre : x vers l'est (le long de la route), z vers le nord (le fond), y = 0 au plancher ; facade vitree a z = 0 sur
    // l'alignement de celle du cinema, parking de livraison devant (z < 0). Largeur fixe ; profondeur d'apres le catalogue, range par
    // categorie de l'entree vers le fond (MWCoop, puis Gr. A, Gr. B...), une banniere suspendue au-dessus du tapis au debut de chaque
    // categorie : une voiture de plus au catalogue = une place de plus, la salle s'allonge vers le nord.
    // [Salonki] Paikka=x,z,cap : place de l'angle sud-ouest (facade, cote cinema) et cap ; Active=0 : pas de salle.
    public static partial class Kauppa
    {
        const float SaW = 18f, SaH = 5.5f, SaFront = 6.5f, SaBack = 3f, SaPark = 13f;
        const float SaHalf = 5f, SaRowX = 2.35f, SaPitch = 3.3f, SaGroupGap = 1.2f, SaAngle = 40f;

        public static bool SalonkiOn { get { return Game.Msc && Ajouts.Salonki && Config.GetInt("Salonki", "Active", 1) != 0; } }

        class Stand
        {
            public int Model, Number; public string Name, Group, Folder; public int Price;
            public Collider Sign; public Texture2D Poster; public float Z; public bool Right;
            public VazCar.Built Hold;
        }
        static GameObject salonki;
        static readonly List<Stand> stands = new List<Stand>();
        static float saDepth, saMat0, saMat1;
        static Stand saLooked;
        static Font saFont;
        static readonly List<UnityEngine.Object> saBaked = new List<UnityEngine.Object>();

        static void ResetSalonki()
        {
            foreach (Stand s in stands) { if (s.Hold != null) VazCar.DestroyPreview(s.Hold); if (s.Poster != null) UnityEngine.Object.Destroy(s.Poster); }
            foreach (UnityEngine.Object o in saBaked) if (o != null) UnityEngine.Object.Destroy(o);
            saBaked.Clear();
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
            Mat("sa_lattia", "paint_black_matte", 1f, new Color(0.72f, 0.42f, 0.28f));   // carrelage terre cuite
            Mat("sa_matto", "paint_black_matte", 1f, new Color(0.17f, 0.19f, 0.23f));    // tapis d'exposition, gris-bleu sombre
            Mat("sa_seina", "paint_black_matte", 1f, new Color(0.94f, 0.92f, 0.87f));    // murs creme
            Mat("sa_tumma", "paint_black_matte", 1f, new Color(0.12f, 0.12f, 0.14f));    // bandeaux, menuiseries, pancartes
            Mat("sa_katto", "paint_black_matte", 1f, new Color(0.36f, 0.36f, 0.38f));
            Mat("sa_kulta", "paint_black_matte", 1f, new Color(0.86f, 0.68f, 0.28f));    // laiton des potelets
            Mat("sa_naru", "paint_black_matte", 1f, new Color(0.62f, 0.05f, 0.08f));     // cordons rouges
            Mat("sa_tiski", "wood12", 1f);
            Mat("sa_asfaltti", "mwc_asphalt", 7f);
            Mat("sa_viiva", "paint_black_matte", 1f, new Color(0.95f, 0.95f, 0.92f));
            Mat("sa_betoni", "paint_black_matte", 1f, new Color(0.66f, 0.66f, 0.64f));
            if (!mats.ContainsKey("lasi")) Mat("lasi", "psk_glass", 1f);
        }

        // places : deux rangees en epi (gauche, droite), par categorie de l'entree vers le fond ; une categorie commence une rangee neuve
        static void SalonkiLayout()
        {
            float z = SaFront + 1.6f; int col = 0; string g = null;
            foreach (Stand s in stands)
            {
                if (g != null && s.Group != g) { if (col == 1) z += SaPitch; col = 0; z += SaGroupGap; }
                g = s.Group;
                s.Right = col == 1; s.Z = z;
                if (col == 1) z += SaPitch;
                col = 1 - col;
            }
            float last = col == 1 ? z : z - SaPitch;
            saMat0 = SaFront; saMat1 = last + 2.6f;
            saDepth = saMat1 + SaBack;
        }

        static void BuildSalonki()
        {
            Vector3 o; float yaw;
            SalonkiFrame(out o, out yaw);
            Quaternion rot = Quaternion.Euler(0, yaw, 0);
            stands.Clear();
            stands.AddRange(SalonkiCatalog());
            if (stands.Count == 0) { L("salon : aucune voiture au catalogue"); return; }
            SalonkiLayout();
            SalonkiMaterials();
            // plancher au-dessus de tout le sol sous l'emprise (parking compris)
            float gMax = float.MinValue, gMin = float.MaxValue;
            for (float x = 0f; x <= SaW; x += 3f)
                for (float z = -SaPark; z <= saDepth; z += 3f)
                {
                    RaycastHit? h = GroundHit(o + rot * new Vector3(x, 0, z) + Vector3.up * 200f);
                    if (!h.HasValue || kino != null && h.Value.collider.transform.IsChildOf(kino.transform)) continue;
                    gMax = Mathf.Max(gMax, h.Value.point.y); gMin = Mathf.Min(gMin, h.Value.point.y);
                }
            if (gMax == float.MinValue) gMax = gMin = 3f;
            float floorY = gMax + 0.2f, down = floorY - gMin + 0.6f;
            L(string.Format("salon : {0} voiture(s), {1:F0} x {2:F0} m, sol {3:F2}..{4:F2}, plancher a {5:F2}", stands.Count, SaW, saDepth, gMin, gMax, floorY));

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

        // coque : socle, plancher, tapis, murs, facade vitree (porte au milieu), toit, bandeau, comptoir, parking de livraison
        static void SalonkiShell(float down)
        {
            float W = SaW, D = saDepth, H = SaH, w = 0.3f;
            Box(new Vector3(-w, -down, -0.4f), new Vector3(W + w, -0.02f, D + w), "sa_betoni");
            Box(new Vector3(0f, -0.02f, 0f), new Vector3(W, 0f, D), null, null, "sa_lattia", null, null, null);
            Box(new Vector3(W / 2 - SaHalf, 0f, saMat0), new Vector3(W / 2 + SaHalf, 0.06f, saMat1), "sa_matto", "sa_matto", "sa_matto", null, "sa_matto", "sa_matto", false);
            Box(new Vector3(-w, 0f, D), new Vector3(W + w, H, D + w), "sa_seina");     // fond
            Box(new Vector3(-w, 0f, 0f), new Vector3(0f, H, D), "sa_seina");           // pignon ouest
            Box(new Vector3(W, 0f, 0f), new Vector3(W + w, H, D), "sa_seina");          // pignon est
            Box(new Vector3(-w, H, -0.6f), new Vector3(W + w, H + 0.45f, D + w), "sa_katto");   // toit, debord sur la facade
            Box(new Vector3(-w, H - 1.3f, -0.12f), new Vector3(W + w, H, 0.05f), "sa_tumma");  // bandeau de l'enseigne
            // facade vitree : montants tous les 3 m, porte de 3 m au milieu (x 7,5 a 10,5), soubassement sombre
            float doorA = W / 2 - 1.5f, doorB = W / 2 + 1.5f;
            foreach (float x in new[] { 0f, 3f, doorA, doorB, 15f, W })
                Box(new Vector3(x - 0.08f, 0f, -0.08f), new Vector3(x + 0.08f, H - 1.3f, 0.08f), "sa_tumma");
            foreach (float[] seg in new[] { new[] { 0f, doorA }, new[] { doorB, W } })
            {
                Box(new Vector3(seg[0], 0f, -0.05f), new Vector3(seg[1], 0.4f, 0.05f), "sa_tumma");
                Box(new Vector3(seg[0], 0.4f, -0.02f), new Vector3(seg[1], H - 1.3f, 0.02f), "lasi");
            }
            Box(new Vector3(doorA, 3.0f, -0.05f), new Vector3(doorB, H - 1.3f, 0.05f), "lasi");   // imposte au-dessus de la porte
            // parking de livraison devant la facade, marche devant la porte
            Box(new Vector3(-2f, -down, -SaPark), new Vector3(W + 2f, -0.25f, -0.4f), "sa_betoni", "sa_betoni", "sa_asfaltti", null, "sa_betoni", null);
            Box(new Vector3(doorA - 1f, -0.25f, -1.2f), new Vector3(doorB + 1f, -0.02f, -0.4f), "sa_betoni");
            for (int k = 0; k <= SaBays; k++)
            {
                float x = SaBay0 + k * SaBayW;
                Box(new Vector3(x - 0.06f, -0.249f, -SaPark + 1f), new Vector3(x + 0.06f, -0.245f, -2f), "sa_viiva", false);
            }
            // comptoir de location dans l'entree, a gauche
            Box(new Vector3(0.8f, 0f, 2.2f), new Vector3(1.7f, 1.05f, 5.4f), "sa_tiski");
            Box(new Vector3(0.7f, 1.05f, 2.1f), new Vector3(1.8f, 1.12f, 5.5f), "sa_tumma");
        }
        const int SaBays = 4; const float SaBay0 = 1f, SaBayW = 4f;

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
        // calque de l'apercu), posee sur un quad (Unlit/Texture, d'un seul cote, cache par les murs) -- le texte 3D de Unity (GUI/Text
        // Shader) se voyait a travers les panneaux et a l'envers depuis l'interieur (essai du 30/10).
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
                if (Config.GetInt("Test", "SalonkiEssai", 0) != 0) File.WriteAllBytes(Path.Combine(Path.Combine(Log.DataDir, "dumps"), "salonki-paneeli-" + saBaked.Count + ".png"), tex.EncodeToPNG());
            }
            // (tout de suite : les panneaux suivants se cuisent dans la meme image, au meme endroit -- avec Destroy, leurs textes s'y
            // superposaient, essai du 30/10)
            finally { UnityEngine.Object.DestroyImmediate(holder); UnityEngine.Object.DestroyImmediate(cg); }
            var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
            UnityEngine.Object.Destroy(q.GetComponent<Collider>());
            q.name = "sa_paneeli"; q.transform.SetParent(parent, false);
            q.transform.localPosition = pos; q.transform.localRotation = rot; q.transform.localScale = new Vector3(w, h, 1f);
            var m = new Material(Shader.Find("Unlit/Texture") ?? Shader.Find("Legacy Shaders/Diffuse")) { mainTexture = tex, name = "sa_paneeli" };
            q.GetComponent<Renderer>().sharedMaterial = m;
            saBaked.Add(tex); saBaked.Add(m);
            if (Config.GetInt("Test", "SalonkiEssai", 0) != 0) L("salonki essai : panneau " + (saBaked.Count / 2 - 1) + " en " + q.transform.position.ToString("F2") + ", vers " + (-q.transform.forward).ToString("F2") + ", parent " + parent.name);
            return q;
        }

        // enseigne de la facade, comptoir, bannieres des categories suspendues au-dessus du tapis (lues depuis l'entree)
        static void SalonkiSigns()
        {
            Transform t = salonki.transform;
            Color gold = new Color(1f, 0.82f, 0.3f);
            Color dark = new Color(0.10f, 0.10f, 0.12f);
            SaPanel(t, new Vector3(SaW / 2f, SaH - 0.65f, -0.2f), Quaternion.identity, SaW, 1.3f, dark, h =>
            {
                SaText(h, "AUTOSALONKI", new Vector3(0, 0.15f, 0), Quaternion.identity, 0.08f, gold, TextAnchor.MiddleCenter);
                SaText(h, "VUOKRAUS · LOCATION · RENTAL", new Vector3(0, -0.35f, 0), Quaternion.identity, 0.028f, Color.white, TextAnchor.MiddleCenter, FontStyle.Normal);
            }, 110f);
            SaPanel(t, new Vector3(0.62f, 0.7f, 3.8f), Quaternion.Euler(0, 90f, 0), 2.6f, 0.5f, dark, h =>
                SaText(h, "VUOKRAUS", Vector3.zero, Quaternion.identity, 0.03f, gold, TextAnchor.MiddleCenter), 300f);
            string g = null;
            foreach (Stand s in stands)
            {
                if (s.Group == g) continue;
                g = s.Group;
                var band = GameObject.CreatePrimitive(PrimitiveType.Cube);
                UnityEngine.Object.Destroy(band.GetComponent<Collider>());
                band.name = "sa_kategoria"; band.transform.SetParent(t, false);
                Vector3 at = new Vector3(SaW / 2f, SaH - 1.2f, s.Z - 1.4f);
                band.transform.localPosition = at; band.transform.localScale = new Vector3(7.5f, 0.75f, 0.05f);
                band.GetComponent<Renderer>().sharedMaterial = mats["sa_tumma"];
                foreach (float x in new[] { -3.4f, 3.4f }) SaRope(t, at + new Vector3(x, 0.37f, 0), new Vector3(at.x + x, SaH, at.z), mats["sa_tumma"]);
                string title = CategoryTitle(g);
                // (10 cm devant : a 3 mm, le texte disparaissait dans la planche en jeu -- profondeur, essai du 30/10)
                SaPanel(t, at + new Vector3(0, 0, -0.1f), Quaternion.identity, 7.4f, 0.7f, dark, h => SaText(h, title, Vector3.zero, Quaternion.identity, 0.05f, gold, TextAnchor.MiddleCenter), 180f);
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
            Material gold = mats["sa_kulta"], rope = mats["sa_naru"];
            // potelets et cordons tout autour du tapis (un potelet tous les 2 m environ ; collisions le long de chaque cote)
            float x0 = SaW / 2 - SaHalf - 0.35f, x1 = SaW / 2 + SaHalf + 0.35f, z0 = saMat0 - 0.35f, z1 = saMat1 + 0.35f;
            var corners = new[] { new Vector3(x0, 0, z0), new Vector3(x1, 0, z0), new Vector3(x1, 0, z1), new Vector3(x0, 0, z1) };
            for (int c = 0; c < 4; c++)
            {
                Vector3 a = corners[c], b = corners[(c + 1) % 4];
                int n = Mathf.Max(1, Mathf.RoundToInt((b - a).magnitude / 2.1f));
                for (int k = 0; k < n; k++)
                {
                    Vector3 p0 = Vector3.Lerp(a, b, (float)k / n), p1 = Vector3.Lerp(a, b, (float)(k + 1) / n);
                    SaPost(t, p0, gold);
                    Vector3 ra = p0 + new Vector3(0, 0.92f, 0), rb = p1 + new Vector3(0, 0.92f, 0), m = (ra + rb) / 2f - new Vector3(0, 0.14f, 0);
                    SaRope(t, ra, m, rope); SaRope(t, m, rb, rope);
                }
                var col = new GameObject("sa_naru_c"); col.transform.SetParent(t, false);
                col.transform.localPosition = (a + b) / 2f + new Vector3(0, 0.55f, 0);
                col.transform.localRotation = Quaternion.LookRotation(b - a);
                col.AddComponent<BoxCollider>().size = new Vector3(0.1f, 1.1f, (b - a).magnitude);
            }
            for (int i = 0; i < stands.Count; i++)
            {
                Stand s = stands[i];
                float side = s.Right ? 1f : -1f;
                // pancarte dans l'allee, en face de la voiture, tournee vers l'allee
                SalonkiSign(s, new Vector3(SaW / 2f + side * (SaHalf + 1.2f), 0f, s.Z), s.Right ? -90f : 90f);
                s.Poster = SalonkiPoster(s);   // (image du menu seulement)
                // la voiture, en apercu (sans physique), en epi : le nez vers l'allee de son cote et vers l'entree
                var pivot = new GameObject("sa_paikka_" + s.Folder).transform;
                pivot.SetParent(t, false);
                pivot.localPosition = new Vector3(SaW / 2f + side * SaRowX, 0.06f, s.Z);
                pivot.localRotation = Quaternion.Euler(0, 180f - side * SaAngle, 0);
                try
                {
                    s.Number = SaPaint[i % SaPaint.Length];
                    GameObject car = VazCar.BuildPreview(s.Number, Vaz.ColorOf(s.Number), 0, out s.Hold, VazCar.ModelOf(s.Model));
                    if (car != null)
                    {
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
                        if (any)
                        {
                            Vector3 lc = pivot.InverseTransformPoint(bb.center), lmin = pivot.InverseTransformPoint(bb.min);
                            car.transform.localPosition = new Vector3(-lc.x, -lmin.y, -lc.z);
                        }
                    }
                    else L("salon : apercu impossible pour " + s.Name);
                }
                catch (Exception e) { L("salon : voiture " + s.Name + " : " + e.Message); }
            }
        }

        static void SaPost(Transform t, Vector3 p, Material gold)
        {
            SaCyl(t, p + new Vector3(0, 0.5f, 0), 0.05f, 1.0f, gold);
            SaCyl(t, p + new Vector3(0, 0.02f, 0), 0.32f, 0.04f, gold);
            var top = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            UnityEngine.Object.Destroy(top.GetComponent<Collider>());
            top.transform.SetParent(t, false); top.transform.localPosition = p + new Vector3(0, 1.03f, 0); top.transform.localScale = Vector3.one * 0.1f;
            top.GetComponent<Renderer>().sharedMaterial = gold;
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

        // pancarte : pied, plaque inclinee vers l'allee (cap), texte ; son collider sert a la viser
        static void SalonkiSign(Stand s, Vector3 at, float yaw)
        {
            Transform t = salonki.transform;
            SaCyl(t, at + new Vector3(0, 0.45f, 0), 0.06f, 0.9f, mats["sa_tumma"]);
            var plate = GameObject.CreatePrimitive(PrimitiveType.Cube);
            plate.name = "sa_kyltti_" + s.Folder; plate.transform.SetParent(t, false);
            plate.transform.localPosition = at + new Vector3(0, 1.0f, 0); plate.transform.localRotation = Quaternion.Euler(35f, yaw, 0);
            plate.transform.localScale = new Vector3(0.9f, 0.6f, 0.03f);
            plate.GetComponent<Renderer>().sharedMaterial = mats["sa_tumma"];
            s.Sign = plate.GetComponent<Collider>();
            VazCar.Model m = VazCar.ModelOf(s.Model);
            Color gold = new Color(1f, 0.82f, 0.3f), soft = new Color(0.86f, 0.86f, 0.86f);
            string desc = m.Desc, cat = CategoryTitle(s.Group), name = s.Name, price = Lang.T("LOCATION  ", "RENTAL  ") + s.Price + " MK", hint = Lang.T("Clic : lire et louer", "Click: read and rent");
            SaPanel(plate.transform, new Vector3(0, 0, -1.6f), Quaternion.identity, 1f, 1f, new Color(0.10f, 0.10f, 0.12f), h =>
            {
                // (dans le repere de la plaque, 0,9 x 0,6 m : le quad est mis a l'echelle 1 x 1 de la plaque)
                h.localScale = new Vector3(1f / 0.9f, 1f / 0.6f, 1f);
                SaText(h, name, new Vector3(-0.4f, 0.2f, 0), Quaternion.identity, 0.0042f, Color.white, TextAnchor.UpperLeft);
                SaText(h, cat, new Vector3(-0.4f, 0.09f, 0), Quaternion.identity, 0.0026f, gold, TextAnchor.UpperLeft);
                if (desc.Length > 0) SaText(h, desc, new Vector3(-0.4f, 0.025f, 0), Quaternion.identity, 0.0022f, soft, TextAnchor.UpperLeft, FontStyle.Normal);
                SaText(h, price, new Vector3(-0.4f, -0.12f, 0), Quaternion.identity, 0.0036f, gold, TextAnchor.UpperLeft);
                SaText(h, hint, new Vector3(-0.4f, -0.22f, 0), Quaternion.identity, 0.0019f, soft, TextAnchor.UpperLeft, FontStyle.Normal);
            }, 600f);
        }

        // image du menu : affiche.png du dossier de l'addon (JD), sinon son apercu (My Summer Car d'abord)
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
                catch (Exception e) { L("salon : image " + p + " : " + e.Message); }
            }
            return null;
        }

        static void SalonkiLights()
        {
            Transform t = salonki.transform;
            foreach (Stand s in stands)
            {
                float side = s.Right ? 1f : -1f;
                var g = new GameObject("sa_spotti"); g.transform.SetParent(t, false);
                g.transform.localPosition = new Vector3(SaW / 2f + side * (SaRowX + 1.5f), SaH - 0.3f, s.Z - 1f);
                g.transform.localRotation = Quaternion.LookRotation(new Vector3(-side * 0.35f, -1f, 0.2f));
                var l = g.AddComponent<Light>(); l.type = LightType.Spot; l.spotAngle = 75f; l.range = 9f; l.intensity = 1.9f; l.color = new Color(1f, 0.96f, 0.9f); l.shadows = LightShadows.None;
            }
            for (float z = 3f; z < saDepth; z += 7f)
                foreach (float x in new[] { 3.5f, SaW - 3.5f })
                {
                    var g = new GameObject("sa_valo"); g.transform.SetParent(t, false);
                    g.transform.localPosition = new Vector3(x, SaH - 0.4f, z);
                    var l = g.AddComponent<Light>(); l.type = LightType.Point; l.range = 10f; l.intensity = 1.0f; l.color = new Color(1f, 0.95f, 0.88f); l.shadows = LightShadows.None;
                }
        }

        // place de livraison libre devant la facade (monde) ; cap : nez vers la route
        static bool SalonkiBay(out Vector3 pos, out float yaw)
        {
            Transform t = salonki.transform;
            yaw = t.eulerAngles.y + 180f;
            for (int k = 0; k < SaBays; k++)
            {
                Vector3 p = t.TransformPoint(new Vector3(SaBay0 + (k + 0.5f) * SaBayW, 0.4f, -7f));
                bool free = true;
                foreach (Collider c in Physics.OverlapSphere(p + Vector3.up * 0.8f, 1.6f))
                    if (!c.isTrigger && c.attachedRigidbody != null) { free = false; break; }
                if (free) { pos = p; return true; }
            }
            pos = t.TransformPoint(new Vector3(SaBay0 + 0.5f * SaBayW, 0.4f, -7f));
            return false;
        }

        // ---------------------------------------------------------------- chaque image : pancartes visees
        static void TickSalonki(float now)
        {
            if (salonki == null) return;
            Camera cam = Game.MainCam;
            bool near = cam != null && (cam.transform.position - salonki.transform.position).sqrMagnitude < (saDepth + 40f) * (saDepth + 40f);
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
