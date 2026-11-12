using System;
using System.Collections.Generic;
using System.IO;
using HutongGames.PlayMaker;
using UnityEngine;

namespace MWCoop
{
    // RALLI-VUOKRAAMO -- location des voitures de rallye (JD 06/11 : « le meme style de garage [que Fleetari], a gauche du Cardinal », « prend
    // exactement le meme mais d'une autre couleur et avec une nouvelle enseigne »). Le menu et le vendeur viennent de l'AUTOSALONKI (branche salonki).
    // Le garage de Fleetari ne se copie pas tel quel : ses murs, son toit, son enseigne et son sol sont fusionnes par le jeu (static batching, maillages
    // illisibles). Lus au chargement, sur les deux jeux (REPAIRSHOP/Building) : la collision du batiment (forme exacte, 'coll', 'repair_shop_wall_base'),
    // les murs interieurs, le toit du bureau, les portes ; la coque est dessinee sur la collision avec les matieres du jeu (tole ondulee, toit, bois),
    // teintes. Rien du jeu n'est livre : tout est relu dans la partie du joueur.
    // Repere : le meme que REPAIRSHOP/Building sous 'vuokra' (une piece du jeu garde sa pose relative). Repere de la halle (HallP) : x le long de la
    // facade depuis le bureau, z vers le fond, y depuis le sol.
    public static partial class Kauppa
    {
        // (mesures sur la sonde du 06/11, en coordonnees du jeu : centre de la halle, normale sortante de la facade, sol)
        static readonly Vector3 SrcHall = new Vector3(1562.8f, 4.65f, 733.3f), SrcOut = new Vector3(-0.909f, 0f, -0.416f).normalized;
        static readonly Vector3 SrcAlong = new Vector3(0.416f, 0f, -0.909f).normalized;
        const float VaW = 21.6f, VaD = 15.6f, VaBandY = 3.4f, VaOfficeX = 7.3f, VaOfficeZ = 6.4f, VaApron = 11f;
        // (place dans le repere du Cardinal : a sa gauche vu de face, facade sur la meme ligne)
        const float VaAtX = 24f, VaAtZ = -9f;
        // (comme Fleetari : dans la piece de gauche, a sa place derriere le bureau -- JD 06/11 ; sa chaise, chair_pub 6, en coordonnees du jeu)
        static readonly Vector3 SrcVendor = new Vector3(1555.086f, 4.65f, 739.041f);
        // Plan releve sur la collision du jeu (x, z de la halle) : halle principale x 0,5 a 13 (z 5,5 a 14,5 ; devant, x 6 a 13), deuxieme halle x 14 a 18
        // (z 3,5 a 14,5), reduit de droite x 19 a 21, bureau x 0,5 a 5,5 (z 0 a 5). Aucune voiture sur un mur (VaCheck, essai).
        static readonly Slot[] VaSlots = {
            new Slot(VaLiftX, VaLiftZ, 180f, true), new Slot(16f, 11.4f, 180f, true), new Slot(3.4f, 12.2f, 184f, false), new Slot(16f, 6.2f, 178f, false),
            new Slot(11.55f, 10.4f, 180f, false), new Slot(3.0f, 8.1f, 90f, false), new Slot(10.4f, 3.6f, 176f, false), new Slot(20f, 9f, 180f, false),
        };
        // (le pont du jeu, a sa vraie place : le cadre jaune du sol)
        const float VaLiftX = 7.73f, VaLiftZ = 10.13f;

        public static bool VuokraOn { get { return Ajouts.Osat && Ville.On && Vaz.Enabled && Config.GetInt("Vuokraamo", "Active", 1) != 0 && Config.GetInt("Test", "VuokraCoupe", 0) == 0; } }
        // (voiture louee reprise : le garage est construit ; sa premiere place libre)
        public static bool VuokraReady { get { return vuokra != null; } }
        public static bool VuokraSpot(out Vector3 pos, out float yaw) { pos = Vector3.zero; yaw = 0f; return vuokra != null && VuokraBay(out pos, out yaw); }
        static Texture2D vaPicture;
        public static Texture2D VuokraPicture
        {
            get
            {
                if (vaPicture == null) { vaPicture = Res(Game.Msc ? "vuokra-msc.jpg" : "vuokra-mwc.jpg", TextureFormat.RGB24); vaPicture.hideFlags = HideFlags.HideAndDontSave; }
                return vaPicture;
            }
        }

        class Stand
        {
            public int Model, Number; public string Name, Group, Folder; public int Price;
            public Texture2D Poster;
            public VazCar.Built Hold;
        }
        struct Slot { public float X, Z, Yaw; public bool Lift; public Slot(float x, float z, float yaw, bool lift) { X = x; Z = z; Yaw = yaw; Lift = lift; } }

        static GameObject vuokra;
        static Transform vaSource;
        static readonly List<Stand> stands = new List<Stand>();
        static bool vaLooked;
        static Avatar vaVendor;
        static Collider vaVendorCol;
        static Font vaFont;
        static float vaFloor, vaLiftTop, vaLift2Top;
        static MeshCollider vaBaseColl;
        static MeshFilter vaInner, vaDyno;
        static Transform vaHall;
        static bool vaLampDone; static float vaLampUntil;
        static readonly List<UnityEngine.Object> vaBaked = new List<UnityEngine.Object>();
        static readonly int[] VaPaint = { 2, 1, 3, 13, 5, 8, 6, 14 };

        static void ResetVuokra()
        {
            foreach (Stand s in stands) { if (s.Hold != null) VazCar.DestroyPreview(s.Hold); if (s.Poster != null) UnityEngine.Object.Destroy(s.Poster); }
            foreach (UnityEngine.Object o in vaBaked) if (o != null) UnityEngine.Object.Destroy(o);
            vaBaked.Clear();
            stands.Clear(); vuokra = null; vaSource = null; vaLooked = false; VuokraOpen = false; saMenu = null; vaVendor = null; vaVendorCol = null; vaTest = 0; vaClick = 0; vaAim = false; vaObjDone = false; vaMeshes = null; vaDeskColl = null; vaWasOpen = false; vaShutAt = -10f; vaColl = null; vaInner = null; vaDyno = null; vaBaseColl = null; vaRent = 0; vaSaved = vaGiven = vaMineLogged = false; vaHall = null; vaLampDone = false; vaLampUntil = 0f;
            vaSteps.Clear(); vaStep = 0; vaTimes.Length = 0;
        }

        // prix de location : d'apres le rapport puissance / poids (mk, arrondi a 10) ; [Vuokraamo] Hinta_<dossier>=n
        static int VuokraPrice(VazCar.Model m)
        {
            int over = Config.GetInt("Vuokraamo", "Hinta_" + m.Folder, 0);
            if (over > 0) return over;
            if (m.Sp == null || m.Sp.Mass <= 0f) return 60;
            float chT = m.Sp.Power / (m.Sp.Mass / 1000f);
            return Mathf.Max(60, Mathf.RoundToInt(chT / 10f) * 10);
        }

        // voitures de rallye du F10 (pas celles de serie), par categorie puis par nom
        static List<Stand> VuokraCatalog()
        {
            var l = new List<Stand>();
            for (int i = 0; i < VazCar.Models.Length; i++)
            {
                if (!VazCar.InF10(i)) continue;
                VazCar.Model m = VazCar.ModelOf(i);
                if (m.QuestOnly || m.Group == "MWCoop") continue;
                l.Add(new Stand { Model = i, Name = m.Name, Group = m.Group, Folder = m.Folder, Price = VuokraPrice(m) });
            }
            l.Sort((a, b) => a.Group != b.Group ? string.CompareOrdinal(a.Group, b.Group) : string.CompareOrdinal(a.Name, b.Name));
            return l;
        }

        // ---------------------------------------------------------------- reperes
        // point de la halle (x le long de la facade, y depuis le sol, z vers le fond) -> repere de 'vuokra'
        static Vector3 HallP(float x, float y, float z)
        {
            Vector3 frontLeft = SrcHall + SrcOut * (VaD / 2f) - SrcAlong * (VaW / 2f);
            return vaSource.InverseTransformPoint(frontLeft + SrcAlong * x - SrcOut * z + Vector3.up * y);
        }
        static Quaternion HallR(float yaw) { return Quaternion.Inverse(vaSource.rotation) * Quaternion.LookRotation(-SrcOut, Vector3.up) * Quaternion.Euler(0f, yaw, 0f); }

        static Texture2D GrayCopy(Texture src, int size)
        {
            RenderTexture rt = RenderTexture.GetTemporary(size, size, 0);
            RenderTexture prev = RenderTexture.active;
            Graphics.Blit(src, rt);
            RenderTexture.active = rt;
            var t = new Texture2D(size, size, TextureFormat.RGB24, true) { name = "va_harmaa" };
            t.ReadPixels(new Rect(0, 0, size, size), 0, 0);
            Color[] px = t.GetPixels();
            float mean = 0f;
            for (int i = 0; i < px.Length; i++) mean += px[i].grayscale;
            mean = Mathf.Max(0.05f, mean / px.Length);
            for (int i = 0; i < px.Length; i++) { float g = Mathf.Clamp01(px[i].grayscale * 0.82f / mean); px[i] = new Color(g, g, g); }
            t.SetPixels(px); t.Apply(true);
            RenderTexture.active = prev; RenderTexture.ReleaseTemporary(rt);
            vaBaked.Add(t);
            return t;
        }

        // Tole des murs : la vraie ondulation du jeu, en gris, sur 12 m x 3,4 m, salie a la main : taches, rouille qui coule, bas sali, ombre sous le toit (memes
        // valeurs chez tous). Sans cela la meme image se repetait partout (JD 06/11 : « on dirait que le batiment n'a pas de vie »).
        static Texture2D BakeWall(Texture src, Color tint)
        {
            RenderTexture rt = RenderTexture.GetTemporary(512, 512, 0);
            RenderTexture prev = RenderTexture.active;
            Graphics.Blit(src, rt); RenderTexture.active = rt;
            var read = new Texture2D(512, 512, TextureFormat.RGB24, false);
            read.ReadPixels(new Rect(0, 0, 512, 512), 0, 0); read.Apply(false);
            RenderTexture.active = prev; RenderTexture.ReleaseTemporary(rt);
            Color[] sp = read.GetPixels();
            UnityEngine.Object.Destroy(read);
            float mean = 0f; foreach (Color c in sp) mean += c.grayscale; mean = Mathf.Max(0.05f, mean / sp.Length);
            const int W = 1536, H = 512;
            var rust = new float[W];
            var rnd = new System.Random(7);
            for (int s = 0; s < 34; s++)
            {
                int c0 = rnd.Next(W), w0 = 4 + rnd.Next(26); float a = 0.25f + (float)rnd.NextDouble() * 0.6f;
                for (int x = c0; x < c0 + w0; x++) rust[x % W] = Mathf.Max(rust[x % W], a * (1f - Mathf.Abs(x - c0 - w0 / 2f) / (w0 / 2f + 1f)));
            }
            var px = new Color[W * H];
            Color rc = new Color(0.34f, 0.19f, 0.10f), mud = new Color(0.20f, 0.17f, 0.13f);
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    float g = sp[((y * 512 / 300) % 512) * 512 + (x * 2) % 512].grayscale * 0.82f / mean;
                    float large = Mathf.PerlinNoise(x * 0.0065f + 3f, y * 0.012f + 7f), fine = Mathf.PerlinNoise(x * 0.05f, y * 0.07f + 11f);
                    Color col = tint * g * (0.86f + 0.26f * large);
                    float top = 1f - y / (float)H, bottom = y / (float)H;
                    float streak = rust[x] * (Mathf.Clamp01(1f - top * 1.6f) * 0.8f + Mathf.Clamp01(1f - bottom * 3.5f)) * (0.55f + 0.6f * fine);
                    col = Color.Lerp(col, rc * (0.6f + g * 0.6f), Mathf.Clamp01(streak));
                    col = Color.Lerp(col, mud, Mathf.Clamp01((0.16f - bottom) * 3.4f) * (0.55f + 0.4f * fine));
                    col *= 1f - Mathf.Clamp01((y - (H - 34)) / 34f) * 0.45f;
                    col.a = 1f; px[y * W + x] = col;
                }
            var t = new Texture2D(W, H, TextureFormat.RGB24, true) { name = "va_seina" };
            t.SetPixels(px); t.Apply(true); t.wrapMode = TextureWrapMode.Repeat; t.anisoLevel = 4;
            vaBaked.Add(t);
            return t;
        }

        static void VuokraMaterials()
        {
            Mat("va_pelti", "wall_metal1", 2f, Color.white);
            Mat("va_katto", "wall_metal2", 2.5f, new Color(0.46f, 0.31f, 0.25f));
            Mat("va_sisakatto", "wall_metal2", 2.5f);
            Mat("va_lauta", "wood_panel1", 2f, new Color(0.96f, 0.93f, 0.88f));
            Mat("va_sokkeli", "paint_black_matte", 1f, new Color(0.46f, 0.46f, 0.44f));
            Mat("va_sininen", "paint_black_matte", 1f, new Color(0.10f, 0.22f, 0.58f));
            Mat("va_teras", "paint_black_matte", 1f, new Color(0.24f, 0.25f, 0.27f));
            Mat("va_tumma", "paint_black_matte", 1f, new Color(0.12f, 0.12f, 0.14f));
            Mat("va_kehys", "paint_black_matte", 1f, new Color(0.72f, 0.71f, 0.66f));
            Mat("va_tiski", "wood12", 1f);
            Mat("va_kaakeli", "tiles4", 1f);
            if (!mats.ContainsKey("ki_asfaltti")) Mat("ki_asfaltti", "mwc_asphalt", 7f);
            try
            {
                Material w = mats["va_pelti"];
                Material tile; if (TryGameMaterial("tiles4", out tile)) mats["va_kaakeli"] = tile;
                if (w != null && w.mainTexture != null) { w.mainTexture = BakeWall(w.mainTexture, new Color(0.40f, 0.62f, 0.90f)); w.color = Color.white; }
                // (My Winter Car : toit sous la neige, comme les autres)
                if (!Game.Msc) { Material n; if (TryGameMaterial("SNOW", out n)) mats["va_katto"] = n; }
            }
            catch (Exception e) { L("garage : matieres " + e.Message); }
        }
        static bool TryGameMaterial(string name, out Material m)
        {
            foreach (Renderer r in vaSource.GetComponentsInChildren<Renderer>(true))
                foreach (Material x in r.sharedMaterials)
                    if (x != null && x.name == name) { m = x; return true; }
            m = null; return false;
        }

        // ---------------------------------------------------------------- construction
        // Une etape par image (Menos, Discord 12/11 : « Remove lag when game enters » ; 3,2 s de gel quand tout le garage, ses 8 voitures
        // et la tole des murs se faisaient dans la meme image) : BuildVuokra prepare la liste, Kauppa.Update en joue une par image
        // (VuokraStep). [Test] BoutiqueEtapes=0 : tout d'un coup comme avant.
        static readonly List<KeyValuePair<string, Action>> vaSteps = new List<KeyValuePair<string, Action>>();
        static int vaStep;
        static readonly System.Text.StringBuilder vaTimes = new System.Text.StringBuilder();
        static void BuildVuokra()
        {
            vaSteps.Clear(); vaStep = 0; vaTimes.Length = 0;
            GameObject bg = Game.FindAny("REPAIRSHOP/Building");
            if (bg == null) { L("garage : REPAIRSHOP/Building absent"); return; }
            vaSource = bg.transform;
            stands.Clear(); stands.AddRange(VuokraCatalog());
            if (stands.Count == 0) { L("garage : aucune voiture de rallye"); return; }
            Step("matieres", VuokraMaterials);
            Step("halle", VuokraFrame);
            Step("coque", VuokraShell);
            Step("portes", VuokraDoors);
            Step("sol", VuokraFloor);
            Step("enseigne", VuokraSign);
            Step("parvis", () => VuokraApron(Quaternion.Euler(0f, vaYaw, 0f)));
            Step("details", VuokraDetails);
            Step("bureau", VuokraOffice);
            Step("banc", VuokraDyno);
            Step("ponts", VuokraLift);
            for (int i = 0; i < stands.Count && i < VaSlots.Length; i++) { int k = i; Step("voiture " + i, () => VuokraCar(k)); }
            Step("vendeur", () => { if (stands.Count > VaSlots.Length) L("garage : " + (stands.Count - VaSlots.Length) + " voiture(s) au menu seulement"); VuokraVendor(); });
            Step("decor", VuokraRealDecor);
            Step("lumieres", VuokraLights);
            Step("fin", () => L("garage construit en " + vuokra.transform.position.ToString("F1") + ", plancher " + vaFloor.ToString("F2") + ", " + stands.Count + " voitures, etapes (ms) :" + vaTimes));
        }
        static void Step(string name, Action a) { vaSteps.Add(new KeyValuePair<string, Action>(name, a)); }
        // (une etape ; vrai quand le garage est fini, ou rien a faire)
        static bool VuokraStep()
        {
            if (vaStep >= vaSteps.Count) return true;
            KeyValuePair<string, Action> st = vaSteps[vaStep++];
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try { st.Value(); }
            catch (Exception e)
            {
                L("garage : " + st.Key + " : " + e);
                // (sans halle, rien d'autre ne tient)
                if (vuokra == null) { vaStep = vaSteps.Count; return true; }
            }
            vaTimes.Append(" ").Append(st.Key).Append(" ").Append(sw.Elapsed.TotalMilliseconds.ToString("F0"));
            return vaStep >= vaSteps.Count;
        }
        static float vaYaw;
        static void VuokraFrame()
        {
            Vector3 c; float oy; string fl;
            OsatFrame(out c, out oy, out fl);
            vaYaw = oy;
            Quaternion q = Quaternion.Euler(0f, oy, 0f);
            Vector3 centre = c + q * new Vector3(Config.GetFloat("Vuokraamo", "X", VaAtX), 0f, Config.GetFloat("Vuokraamo", "Z", VaAtZ));
            Quaternion turn = Quaternion.FromToRotation(SrcOut, q * Vector3.forward);
            // sol : au-dessus du terrain sous le batiment
            float gMax = float.MinValue;
            for (float x = -VaW / 2f; x <= VaW / 2f; x += 3f)
                for (float z = -VaD / 2f; z <= VaD / 2f; z += 3f)
                {
                    RaycastHit? h = GroundHit(centre + q * new Vector3(x, 0f, z) + Vector3.up * 200f);
                    if (h.HasValue) gMax = Mathf.Max(gMax, h.Value.point.y);
                }
            if (gMax == float.MinValue) gMax = 3.5f;
            vaFloor = gMax + 0.12f;
            vuokra = new GameObject("MWCoop RALLI-VUOKRAAMO");
            vuokra.transform.rotation = turn * vaSource.rotation;
            vuokra.transform.localScale = vaSource.lossyScale;
            vuokra.transform.position = new Vector3(centre.x, vaFloor, centre.z) + turn * (vaSource.position - SrcHall);
            var hg = new GameObject("va_halli"); hg.transform.SetParent(vuokra.transform, false);
            hg.transform.localPosition = HallP(0f, 0f, 0f); hg.transform.localRotation = HallR(0f);
            vaHall = hg.transform;
        }

        static GameObject VaPart(string name, Mesh mesh, Material[] ms, Vector3 pos, Quaternion rot, Vector3 scale)
        {
            var g = new GameObject(name);
            g.transform.SetParent(vuokra.transform, false);
            g.transform.localPosition = pos; g.transform.localRotation = rot; g.transform.localScale = scale;
            g.AddComponent<MeshFilter>().sharedMesh = mesh;
            g.AddComponent<MeshRenderer>().sharedMaterials = ms;
            return g;
        }
        // piece du jeu lisible : meme pose relative au batiment
        static GameObject VaCopy(Transform src, Transform frame)
        {
            MeshFilter f = src.GetComponent<MeshFilter>(); Renderer r = src.GetComponent<Renderer>();
            if (f == null || r == null || f.sharedMesh == null || r.isPartOfStaticBatch) return null;
            return VaPart("va_" + src.name, f.sharedMesh, r.sharedMaterials, frame.InverseTransformPoint(src.position), Quaternion.Inverse(frame.rotation) * src.rotation, Vector3.Scale(src.lossyScale, new Vector3(1f / frame.lossyScale.x, 1f / frame.lossyScale.y, 1f / frame.lossyScale.z)));
        }

        const float Door1X0 = 8f, Door1X1 = 11.2f, Door2X0 = 14.35f, Door2X1 = 17.85f, Door2Top = 3.1f;
        static MeshCollider vaColl;
        // partie d'un polygone ou f <= 0
        static List<Vector3> VaClip(List<Vector3> poly, Func<Vector3, float> f)
        {
            var o = new List<Vector3>();
            for (int i = 0; i < poly.Count; i++)
            {
                Vector3 s = poly[i], e = poly[(i + 1) % poly.Count];
                float fs = f(s), fe = f(e);
                if (fs <= 0f) o.Add(s);
                if ((fs <= 0f) != (fe <= 0f)) o.Add(Vector3.Lerp(s, e, fs / (fs - fe)));
            }
            return o;
        }
        // z de la halle du mur de facade (la collision du jeu, vue de dehors), a x et a la hauteur y ; NaN : pas de mur (ouverture)
        static float VaWallZ(float x, float y)
        {
            if (vaColl == null) return float.NaN;
            Vector3 fl = SrcHall + SrcOut * (VaD / 2f) - SrcAlong * (VaW / 2f);
            Vector3 p = fl + SrcAlong * x + Vector3.up * y + SrcOut * 3f;
            RaycastHit h;
            return vaColl.Raycast(new Ray(p, -SrcOut), out h, 6f) ? Vector3.Dot(h.point - fl, -SrcOut) : float.NaN;
        }

        // Coque : les triangles de la collision du jeu, coupes a la hauteur du bandeau de bois, avec une image a l'echelle. Deux faces.
        static void VuokraShell()
        {
            MeshCollider coll = null, baseColl = null;
            foreach (MeshCollider mc in vaSource.GetComponentsInChildren<MeshCollider>(true))
            {
                if (mc.sharedMesh == null) continue;
                if (mc.name == "coll") coll = mc;
                else if (mc.name == "repair_shop_wall_base") baseColl = mc;
            }
            if (coll == null || !coll.sharedMesh.isReadable) { L("garage : collision du batiment illisible"); return; }
            vaColl = coll;
            // Murs : seulement les faces du tour du batiment, vers l'exterieur ; a l'interieur, les vrais murs du jeu (repair_shop_wall_inner).
            var walls = new VaBuild { U = 12f, V = VaBandY, V0 = SrcHall.y, TwoSided = false }; var band = new VaBuild { TwoSided = false }; var roof = new VaBuild { TwoSided = false }; var ceil = new VaBuild { TwoSided = false }; var porch = new VaBuild();
            Vector3[] vs = coll.sharedMesh.vertices; int[] ts = coll.sharedMesh.triangles;
            float bandWorld = SrcHall.y + VaBandY;
            Vector3 fl = SrcHall + SrcOut * (VaD / 2f) - SrcAlong * (VaW / 2f);
            for (int i = 0; i + 2 < ts.Length; i += 3)
            {
                Vector3 a = coll.transform.TransformPoint(vs[ts[i]]), b = coll.transform.TransformPoint(vs[ts[i + 1]]), d = coll.transform.TransformPoint(vs[ts[i + 2]]);
                Vector3 n = Vector3.Cross(b - a, d - a);
                if (n.sqrMagnitude < 1e-8f) continue;
                n.Normalize();
                Vector3 mid = (a + b + d) / 3f - fl;
                float hx = Vector3.Dot(mid, SrcAlong), hz = Vector3.Dot(mid, -SrcOut);
                // (porche : devant la facade, bois)
                if (hz < -0.75f) { porch.Tri(a, b, d, n); continue; }
                if (Mathf.Abs(n.y) > 0.3f)
                {
                    // (jamais les planchers ni le bas des fenetres de la collision : ils couvraient le vrai sol et le pont ; dessous du toit : le metal du jeu, dehors son toit)
                    if ((a.y + b.y + d.y) / 3f - SrcHall.y < 2.5f) continue;
                    if (n.y < 0f) { Vector3 up = b; b = d; d = up; n = -n; }
                    roof.Tri(a, b, d, n); ceil.Tri(a, d, b, -n);
                    continue;
                }
                Vector3 outward = hz <= 0.05f ? SrcOut : hz >= 15f ? -SrcOut : hx <= 0.55f ? -SrcAlong : hx >= 21f ? SrcAlong : Vector3.zero;
                if (outward == Vector3.zero) continue;
                if (Vector3.Dot(n, outward) < 0f) { Vector3 tmp = b; b = d; d = tmp; n = -n; }
                // (mur de facade : le trou de la deuxieme porte, que la collision du jeu bouche ; dedans on voit dehors, de dehors on doit voir dedans -- JD 06/11)
                if (outward != SrcOut) { VaBuild.Split(a, b, d, n, bandWorld, walls, band); continue; }
                Func<Vector3, float> hxf = q => Vector3.Dot(q - fl, SrcAlong);
                var tri = new List<Vector3> { a, b, d };
                var pieces = new List<List<Vector3>>
                {
                    VaClip(tri, q => hxf(q) - Door2X0),
                    VaClip(tri, q => Door2X1 - hxf(q)),
                    VaClip(VaClip(VaClip(tri, q => Door2X0 - hxf(q)), q => hxf(q) - Door2X1), q => SrcHall.y + Door2Top - q.y)
                };
                foreach (List<Vector3> pc in pieces)
                    for (int k = 1; k + 1 < pc.Count; k++) VaBuild.Split(pc[0], pc[k], pc[k + 1], n, bandWorld, walls, band);
            }
            VaPart("va_seinat", walls.Mesh(), new[] { mats["va_pelti"] }, Vector3.zero, Quaternion.identity, Vector3.one);
            VaPart("va_lauta", band.Mesh(), new[] { mats["va_lauta"] }, Vector3.zero, Quaternion.identity, Vector3.one);
            VaPart("va_katto", roof.Mesh(), new[] { mats["va_katto"] }, Vector3.zero, Quaternion.identity, Vector3.one);
            VaPart("va_sisakatto", ceil.Mesh(), new[] { mats["va_sisakatto"] }, Vector3.zero, Quaternion.identity, Vector3.one);
            VaPart("va_kuisti", porch.Mesh(), new[] { mats["va_lauta"] }, Vector3.zero, Quaternion.identity, Vector3.one);
            L(string.Format("garage : coque de {0} triangles (murs {1}, bois {2}, toit {3})", ts.Length / 3, walls.Count, band.Count, roof.Count));
            var cg = new GameObject("va_collision"); cg.transform.SetParent(vuokra.transform, false);
            cg.transform.localPosition = vaSource.InverseTransformPoint(coll.transform.position);
            cg.transform.localRotation = Quaternion.Inverse(vaSource.rotation) * coll.transform.rotation;
            cg.AddComponent<MeshCollider>().sharedMesh = coll.sharedMesh;
            // Sol et socle : le vrai maillage du jeu (celui de sa collision, lisible, avec ses coordonnees d'image), et sa matiere : dalles, cadre jaune du pont...
            vaBaseColl = baseColl;
            if (baseColl != null && baseColl.sharedMesh.isReadable)
            {
                Renderer br = baseColl.GetComponent<Renderer>();
                Material[] bm = br != null && br.sharedMaterials.Length > 0 ? br.sharedMaterials : new[] { mats["va_sokkeli"] };
                GameObject bg = VaPart("va_sokkeli", baseColl.sharedMesh, bm, vaSource.InverseTransformPoint(baseColl.transform.position), Quaternion.Inverse(vaSource.rotation) * baseColl.transform.rotation, Vector3.one);
                bg.AddComponent<MeshCollider>().sharedMesh = baseColl.sharedMesh;
            }
            // murs interieurs, toit du bureau, lampes : les pieces du jeu
            foreach (Renderer r in vaSource.GetComponentsInChildren<Renderer>(true))
                if (r.name == "repair_shop_wall_inner" || r.name == "repair_shop_office_roof")
                {
                    VaCopy(r.transform, vaSource);
                    MeshFilter imf = r.GetComponent<MeshFilter>();
                    if (r.name == "repair_shop_wall_inner" && imf != null && imf.sharedMesh != null && imf.sharedMesh.isReadable) vaInner = imf;
                }
            GameObject li = Game.FindAny("REPAIRSHOP/LOD/lights");
            if (li != null) VaCopy(li.transform, vaSource);
        }

        class VaBuild
        {
            readonly List<Vector3> v = new List<Vector3>(); readonly List<Vector2> u = new List<Vector2>(); readonly List<int> t = new List<int>();
            public float U = 2f, V = 2f, V0 = 0f; public bool TwoSided = true;
            public int Count { get { return t.Count / (TwoSided ? 6 : 3); } }
            public void Tri(Vector3 a, Vector3 b, Vector3 c, Vector3 n)
            {
                // (sur les axes du batiment : des normales a peine differentes decalaient les ondes de la tole d'un triangle a l'autre)
                Vector3 along = Mathf.Abs(n.y) > 0.3f || Mathf.Abs(Vector3.Dot(n, SrcOut)) > 0.7f ? SrcAlong : SrcOut;
                Vector3 up = Mathf.Abs(n.y) > 0.3f ? SrcOut : Vector3.up;
                foreach (bool back in new[] { false, true })
                {
                    if (back && !TwoSided) break;
                    int k = v.Count;
                    foreach (Vector3 p in new[] { a, b, c })
                    {
                        v.Add(vaSource.InverseTransformPoint(p));
                        u.Add(new Vector2(Vector3.Dot(p - SrcHall, along) / U, (Vector3.Dot(p, up) - V0) / V));
                    }
                    if (back) { t.Add(k); t.Add(k + 2); t.Add(k + 1); } else { t.Add(k); t.Add(k + 1); t.Add(k + 2); }
                }
            }
            public static void Split(Vector3 a, Vector3 b, Vector3 c, Vector3 n, float y, VaBuild low, VaBuild high)
            {
                var below = new List<Vector3>(); var above = new List<Vector3>();
                Vector3[] p = { a, b, c };
                for (int i = 0; i < 3; i++)
                {
                    Vector3 s = p[i], e = p[(i + 1) % 3];
                    (s.y <= y ? below : above).Add(s);
                    if ((s.y <= y) != (e.y <= y)) { Vector3 m = Vector3.Lerp(s, e, (y - s.y) / (e.y - s.y)); below.Add(m); above.Add(m); }
                }
                for (int i = 1; i + 1 < below.Count; i++) low.Tri(below[0], below[i], below[i + 1], n);
                for (int i = 1; i + 1 < above.Count; i++) high.Tri(above[0], above[i], above[i + 1], n);
            }
            public Mesh Mesh()
            {
                var m = new Mesh { name = "va_coque" };
                m.vertices = v.ToArray(); m.uv = u.ToArray(); m.triangles = t.ToArray();
                m.RecalculateNormals(); m.RecalculateBounds();
                vaBaked.Add(m);
                return m;
            }
        }

        // Portes : celle de gauche ouverte, l'autre fermee, comme sur la photo de JD ; le maillage du jeu (lisible sur une seule pose selon le jeu)
        static void VuokraDoors()
        {
            Transform door = null; bool open = false;
            foreach (MeshFilter f in vaSource.GetComponentsInChildren<MeshFilter>(true))
            {
                if (f.sharedMesh == null || !f.sharedMesh.name.StartsWith("garage_door") || f.sharedMesh.name.Contains("rail")) continue;
                Renderer r = f.GetComponent<Renderer>();
                if (r == null || r.isPartOfStaticBatch) continue;
                door = f.transform; open = r.bounds.size.y < 1f; break;
            }
            if (door == null) { L("garage : pas de porte lisible"); return; }
            Vector3 openC = new Vector3(1555.6f, 7.6f, 731.4f), shutC = new Vector3(1557.73f, 6.08f, 725.17f);
            Quaternion toOpen = open ? door.rotation : Quaternion.AngleAxis(Config.GetFloat("Vuokraamo", "PorteSens", 90f), SrcAlong) * door.rotation;
            Quaternion toShut = open ? Quaternion.AngleAxis(-Config.GetFloat("Vuokraamo", "PorteSens", 90f), SrcAlong) * door.rotation : door.rotation;
            Renderer dr = door.GetComponent<Renderer>();
            Vector3 meshC = dr.bounds.center - door.position;
            foreach (bool up in new[] { true, false })
            {
                Quaternion rot = up ? toOpen : toShut;
                Vector3 offset = rot * Quaternion.Inverse(door.rotation) * meshC;
                Vector3 pos = (up ? openC : shutC) - offset;
                GameObject g = VaPart(up ? "va_ovi_auki" : "va_ovi_kiinni", door.GetComponent<MeshFilter>().sharedMesh, dr.sharedMaterials, vaSource.InverseTransformPoint(pos), Quaternion.Inverse(vaSource.rotation) * rot, door.lossyScale);
                foreach (Transform k in door)
                {
                    MeshFilter kf = k.GetComponent<MeshFilter>(); Renderer kr = k.GetComponent<Renderer>();
                    if (kf == null || kr == null) continue;
                    var kg = new GameObject("va_ovi_lasi"); kg.transform.SetParent(g.transform, false);
                    kg.transform.localPosition = k.localPosition; kg.transform.localRotation = k.localRotation; kg.transform.localScale = k.localScale;
                    kg.AddComponent<MeshFilter>().sharedMesh = kf.sharedMesh; kg.AddComponent<MeshRenderer>().sharedMaterials = kr.sharedMaterials;
                }
                // (porte fermee : on ne la traverse pas)
                if (!up) { var bc = new GameObject("va_ovi_coll"); bc.transform.SetParent(vuokra.transform, false); bc.transform.localPosition = HallP(16.1f, 1.5f, -0.2f); bc.transform.localRotation = HallR(0f); bc.AddComponent<BoxCollider>().size = new Vector3(3.6f, 3.2f, 0.2f); }
            }
        }

        static void VuokraFloor()
        {
            GameObject f = SaPrim(vuokra.transform, PrimitiveType.Cube, HallP(VaW / 2f, -0.12f, VaD / 2f), new Vector3(VaW + 1f, 0.2f, VaD + 1f), mats["va_kaakeli"], HallR(0f), true);
            f.name = "va_lattia_coll";
            f.GetComponent<Renderer>().enabled = false;
            // bureau : le carrelage du jeu, a la hauteur et aux dimensions du sien (repair_shop_office_floor, illisible : 5,5 x 5,3 m a +5 cm ; dalles de 40 cm, 6 x 6 par image)
            const float FY = 0.055f, X0 = 0.13f, X1 = 5.63f, Z0 = -0.14f, Z1 = 5.16f;
            var m = new Mesh { name = "va_kaakeli" };
            m.vertices = new[] { new Vector3(X0, FY, Z0), new Vector3(X1, FY, Z0), new Vector3(X1, FY, Z1), new Vector3(X0, FY, Z1) };
            m.uv = new[] { new Vector2(0f, 0f), new Vector2((X1 - X0) / 2.4f, 0f), new Vector2((X1 - X0) / 2.4f, (Z1 - Z0) / 2.4f), new Vector2(0f, (Z1 - Z0) / 2.4f) };
            m.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            m.RecalculateNormals(); vaBaked.Add(m);
            var g = new GameObject("va_kaakeli"); g.transform.SetParent(vaHall, false);
            g.AddComponent<MeshFilter>().sharedMesh = m; g.AddComponent<MeshRenderer>().sharedMaterial = mats["va_kaakeli"];
        }

        // Enseigne : a la place de celle du jeu (sa taille), peinte sur de la tole claire
        static void VuokraSign()
        {
            Vector3 at = new Vector3(1556.1f, 8.8f, 727.5f) + SrcOut * 0.28f;
            Vector3 pos = vaSource.InverseTransformPoint(at);
            Quaternion rot = Quaternion.Inverse(vaSource.rotation) * Quaternion.LookRotation(-SrcOut, Vector3.up);
            SaPanel(vuokra.transform, pos, rot, 4.7f, 1.25f, new Color(0.93f, 0.89f, 0.76f), h =>
            {
                SaText(h, "RALLI-VUOKRAAMO", new Vector3(0f, 0.20f, 0f), Quaternion.identity, 0.0415f, new Color(0.11f, 0.30f, 0.52f), TextAnchor.MiddleCenter);
                SaText(h, "AUTOVUOKRAUS & KILPA-AUTOT", new Vector3(0f, -0.20f, 0f), Quaternion.identity, 0.0195f, new Color(0.16f, 0.36f, 0.50f), TextAnchor.MiddleCenter);
                SaText(h, "puh. 987 561 33", new Vector3(0f, -0.43f, 0f), Quaternion.identity, 0.019f, new Color(0.30f, 0.35f, 0.15f), TextAnchor.MiddleCenter);
            }, 220f);
            // deux projecteurs au-dessus (JD 06/11 : « comme pour la boutique s21 »), l'enseigne n'emet plus sa lumiere
            foreach (float s in new[] { -1.55f, 1.55f })
            {
                Vector3 lamp = at + SrcAlong * s + SrcOut * 0.95f + Vector3.up * 0.95f;
                Quaternion lr = Quaternion.Inverse(vaSource.rotation) * Quaternion.LookRotation(-SrcOut, Vector3.up);
                SaPrim(vuokra.transform, PrimitiveType.Cube, vaSource.InverseTransformPoint(at + SrcAlong * s + SrcOut * 0.47f + Vector3.up * 0.95f), new Vector3(0.04f, 0.04f, 0.95f), mats["va_teras"], lr);
                SaPrim(vuokra.transform, PrimitiveType.Cube, vaSource.InverseTransformPoint(lamp), new Vector3(0.26f, 0.12f, 0.2f), mats["va_tumma"], lr);
                var sg = new GameObject("va_kyltti_valo"); sg.transform.SetParent(vuokra.transform, false);
                sg.transform.localPosition = vaSource.InverseTransformPoint(lamp + Vector3.down * 0.05f);
                sg.transform.localRotation = Quaternion.Inverse(vaSource.rotation) * Quaternion.LookRotation(at + SrcAlong * s * 0.85f - lamp, Vector3.up);
                var sl = sg.AddComponent<Light>(); sl.type = LightType.Spot; sl.spotAngle = 140f; sl.color = new Color(1f, 0.9f, 0.75f); sl.intensity = 2f; sl.range = 4.2f; sl.shadows = LightShadows.None; sl.renderMode = LightRenderMode.ForcePixel;
            }
        }

        // Devant les portes : asphalte qui suit le sol jusqu'au seuil, avec collision
        static void VuokraApron(Quaternion q)
        {
            int nx = Mathf.CeilToInt(VaW), nz = Mathf.CeilToInt(VaApron);
            var vs = new Vector3[(nx + 1) * (nz + 1)]; var us = new Vector2[vs.Length]; var ts = new int[nx * nz * 6];
            int ti = 0;
            for (int j = 0; j <= nz; j++)
                for (int i = 0; i <= nx; i++)
                {
                    Vector3 local = HallP(VaW * i / nx, 0f, -VaApron * j / nz + 0.3f);
                    Vector3 w = vuokra.transform.TransformPoint(local);
                    RaycastHit? h = GroundHit(new Vector3(w.x, w.y + 40f, w.z));
                    float ground = h.HasValue && !h.Value.collider.transform.IsChildOf(vuokra.transform) ? h.Value.point.y : vaFloor;
                    // (pas le dessus d'un objet : un point du parvis tombe sur le grillage de Voyage montait en pointe, le cone de la capture de JD 06/11)
                    if (ground > vaFloor + 1f) ground = vaFloor;
                    float k = Mathf.Clamp01(j / 3f);   // (rampe sur 3 m depuis le seuil)
                    w.y = Mathf.Lerp(vaFloor + 0.01f, ground + 0.04f, k);
                    int id = j * (nx + 1) + i;
                    vs[id] = vuokra.transform.InverseTransformPoint(w);
                    us[id] = new Vector2(i / 7f, j / 7f);
                    if (i < nx && j < nz) { int a = id, b = id + 1, c = id + nx + 1, d = id + nx + 2; ts[ti++] = a; ts[ti++] = b; ts[ti++] = c; ts[ti++] = b; ts[ti++] = d; ts[ti++] = c; }
                }
            var m = new Mesh { name = "va_parvis", vertices = vs, uv = us, triangles = ts };
            m.RecalculateNormals(); m.RecalculateBounds();
            if (m.normals.Length > 0 && Vector3.Dot(vuokra.transform.TransformDirection(m.normals[0]), Vector3.up) < 0f) { Array.Reverse(ts); m.triangles = ts; m.RecalculateNormals(); }
            vaBaked.Add(m);
            GameObject g = VaPart("va_parvis", m, new[] { mats["ki_asfaltti"] }, Vector3.zero, Quaternion.identity, Vector3.one);
            g.AddComponent<MeshCollider>().sharedMesh = m;
        }

        // ---------------------------------------------------------------- voitures
        static GameObject SaPrim(Transform parent, PrimitiveType type, Vector3 pos, Vector3 scale, Material m, Quaternion? rot = null, bool keepCollider = false)
        {
            var g = GameObject.CreatePrimitive(type);
            if (!keepCollider) UnityEngine.Object.Destroy(g.GetComponent<Collider>());
            g.transform.SetParent(parent, false); g.transform.localPosition = pos; g.transform.localScale = scale;
            if (rot.HasValue) g.transform.localRotation = rot.Value;
            g.GetComponent<Renderer>().sharedMaterial = m;
            return g;
        }

        // Sol reel du garage du jeu sous un point de la halle, depuis le niveau de reference (0 si rien dessous)
        static float RealFloor(float x, float z)
        {
            Vector3 fl = SrcHall + SrcOut * (VaD / 2f) - SrcAlong * (VaW / 2f);
            Vector3 p = fl + SrcAlong * x - SrcOut * z + Vector3.up * 3f;
            float best = float.MinValue;
            RaycastHit h;
            if (vaBaseColl != null && vaBaseColl.Raycast(new Ray(p, Vector3.down), out h, 8f)) best = h.point.y;
            // (la deuxieme halle a sa dalle dans les murs interieurs du jeu, plus haut que le socle)
            foreach (MeshFilter surf in new[] { vaInner, vaDyno })
            {
                if (surf == null || !surf.sharedMesh.isReadable) continue;
                Vector3[] vs = surf.sharedMesh.vertices; int[] ts = surf.sharedMesh.triangles;
                Transform t = surf.transform;
                for (int i = 0; i + 2 < ts.Length; i += 3)
                {
                    Vector3 a = t.TransformPoint(vs[ts[i]]), b = t.TransformPoint(vs[ts[i + 1]]), c = t.TransformPoint(vs[ts[i + 2]]);
                    float d = (b.z - c.z) * (a.x - c.x) + (c.x - b.x) * (a.z - c.z);
                    if (Mathf.Abs(d) < 1e-5f || Mathf.Abs(Vector3.Cross(b - a, c - a).normalized.y) < 0.9f) continue;
                    float l1 = ((b.z - c.z) * (p.x - c.x) + (c.x - b.x) * (p.z - c.z)) / d, l2 = ((c.z - a.z) * (p.x - c.x) + (a.x - c.x) * (p.z - c.z)) / d, l3 = 1f - l1 - l2;
                    if (l1 < 0f || l2 < 0f || l3 < 0f) continue;
                    float y = l1 * a.y + l2 * b.y + l3 * c.y;
                    if (y - SrcHall.y < 1f && y > best) best = y;
                }
            }
            return best == float.MinValue ? 0f : best - SrcHall.y;
        }

        // (JD 06/11 : « la 205 a l'air pose dans le vide » : les bandes sombres du sol sont celles du banc d'essai de Fleetari, a cette place) : le vrai banc, sa place et sa collision
        static void VuokraDyno()
        {
            Transform plat = VaMeshSrc("dyno_platform", 0);
            if (plat == null) { L("garage : banc d'essai du jeu absent"); return; }
            MeshFilter pf = plat.GetComponent<MeshFilter>();
            GameObject pg = VaCopy(plat, vaSource);
            if (pg == null) return;
            vaDyno = pf;
            if (pf.sharedMesh.isReadable) pg.AddComponent<MeshCollider>().sharedMesh = pf.sharedMesh;
            int n = 0;
            foreach (MeshFilter f in plat.parent.GetComponentsInChildren<MeshFilter>(true))
                if (f.sharedMesh != null && f.sharedMesh.name == "dyno_roll" && VaCopy(f.transform, vaSource) != null) n++;
            L("garage : banc d'essai du jeu, " + n + " rouleaux, dessus " + RealFloor(11.55f, 8f).ToString("F2") + " / " + RealFloor(11.55f, 10.4f).ToString("F2") + " / " + RealFloor(11.55f, 13f).ToString("F2"));
        }

        // Ponts : le maillage du jeu (REPAIRSHOP/Lifter/Platform/mesh) ; le premier a sa vraie place, le second dans l'autre halle.
        static void VuokraLift()
        {
            GameObject src = Game.FindAny("REPAIRSHOP/Lifter/Platform/mesh");
            vaLiftTop = vaLift2Top = 0.03f;
            if (src == null || src.GetComponent<Renderer>() == null) { L("garage : pont du jeu absent"); return; }
            // (dessus des pistes : leurs collisions, pas les bras rouges)
            float top = float.MinValue, thick = 0.09f;
            foreach (Collider c in src.transform.parent.GetComponentsInChildren<Collider>(true))
                if (c.name == "Coll" && c.bounds.size.y > 0f) { top = Mathf.Max(top, c.bounds.max.y); thick = c.bounds.size.y; }
            if (top == float.MinValue) top = src.GetComponent<Renderer>().bounds.max.y;
            vaLiftTop = top - SrcHall.y;
            GameObject a = VaCopy(src.transform, vaSource);
            if (a != null)
            {
                a.name = "va_nostin";
                GameObject b2 = UnityEngine.Object.Instantiate(a) as GameObject;
                b2.name = "va_nostin2"; b2.transform.SetParent(a.transform.parent, false);
                // (dans l'autre halle : sans fosse, pose sur le sol)
                float lift = RealFloor(16f, 11.4f) + thick + 0.01f - vaLiftTop;
                vaLift2Top = vaLiftTop + lift;
                b2.transform.localPosition = a.transform.localPosition + vaSource.InverseTransformVector(SrcAlong * (16f - VaLiftX) - SrcOut * (11.4f - VaLiftZ) + Vector3.up * lift);
                b2.transform.localRotation = a.transform.localRotation; b2.transform.localScale = a.transform.localScale;
            }
            VaBox(new Vector3(VaLiftX, -0.19f, VaLiftZ), new Vector3(5f, 0.04f, 8f), "va_tumma");
            L("garage : ponts du jeu, dessus a " + vaLiftTop.ToString("F2") + " et " + vaLift2Top.ToString("F2") + " m, sol " + RealFloor(11.4f, 10.4f).ToString("F2") + " et " + RealFloor(16f, 11.4f).ToString("F2"));
        }

        // (une voiture du garage : apercu du F10 sur sa place)
        static void VuokraCar(int i)
        {
            Transform t = vuokra.transform;
            {
                Stand s = stands[i]; Slot sl = VaSlots[i];
                var pivot = new GameObject("va_paikka_" + s.Folder).transform;
                pivot.SetParent(t, false);
                // (sol : le plus haut sous les quatre roues)
                float fy = float.MinValue;
                foreach (float dx in new[] { -0.7f, 0.7f }) foreach (float dz in new[] { -1.25f, 1.25f }) fy = Mathf.Max(fy, RealFloor(sl.X + dx, sl.Z + dz));
                pivot.localPosition = HallP(sl.X, sl.Lift ? (sl.X > 12f ? vaLift2Top : vaLiftTop) : fy, sl.Z);
                pivot.localRotation = HallR(sl.Yaw);
                try
                {
                    s.Number = VaPaint[i % VaPaint.Length];
                    GameObject car = VazCar.BuildPreview(s.Number, Vaz.PaintFor(s.Number, s.Model), 0, out s.Hold, VazCar.ModelOf(s.Model));
                    if (car == null) { L("garage : apercu impossible pour " + s.Name); return; }
                    car.transform.SetParent(pivot, false);
                    car.transform.localPosition = Vector3.zero; car.transform.localRotation = Quaternion.identity;
                    // (pieces a moins de 8 m : les pieces vides d'un addon ont des bornes a l'origine du monde)
                    Bounds bb = new Bounds(); bool any = false;
                    foreach (Renderer r in car.GetComponentsInChildren<Renderer>(true))
                    {
                        if ((r.bounds.center - pivot.position).sqrMagnitude > 64f) continue;
                        if (!any) { bb = r.bounds; any = true; } else bb.Encapsulate(r.bounds);
                    }
                    if (!any) return;
                    // (sur ses roues : le bas des roues, pas le plus bas de la voiture -- bavettes, spoiler -- JD 06/11 : « pour pas qu'elle vole »)
                    float wheels = float.MaxValue;
                    foreach (Renderer r in car.GetComponentsInChildren<Renderer>(true))
                    {
                        if ((r.bounds.center - pivot.position).sqrMagnitude > 64f) continue;
                        for (Transform up = r.transform; up != null && up != car.transform; up = up.parent)
                            if (up.name.StartsWith("roue")) { wheels = Mathf.Min(wheels, r.bounds.min.y); break; }
                    }
                    if (wheels == float.MaxValue) wheels = bb.min.y;
                    Vector3 lc = pivot.InverseTransformPoint(bb.center);
                    car.transform.position += Vector3.up * (pivot.position.y - wheels);
                    car.transform.localPosition = new Vector3(-lc.x, car.transform.localPosition.y, -lc.z);
                    if (Config.Testing)
                    {
                        // (preuve : chaque roue, son bas par rapport au sol de cette place)
                        var low = new Dictionary<Transform, float>();
                        foreach (Renderer r in car.GetComponentsInChildren<Renderer>(true))
                        {
                            if ((r.bounds.center - pivot.position).sqrMagnitude > 64f) continue;
                            for (Transform up = r.transform; up != null && up != car.transform; up = up.parent)
                                if (up.name.StartsWith("roue")) { float o; low[up] = low.TryGetValue(up, out o) ? Mathf.Min(o, r.bounds.min.y) : r.bounds.min.y; break; }
                        }
                        var sbw = new System.Text.StringBuilder("garage : place " + i + " " + s.Name + (sl.Lift ? " (pont)" : " (sol)") + ", " + low.Count + " roues, ecart au sol");
                        foreach (float yw in low.Values) sbw.Append(" ").Append((yw - pivot.position.y).ToString("F3"));
                        L(sbw.ToString());
                    }
                    Vector3 ext = pivot.InverseTransformVector(bb.size);
                    var bc = pivot.gameObject.AddComponent<BoxCollider>();
                    bc.size = new Vector3(Mathf.Abs(ext.x) * 0.92f, Mathf.Abs(ext.y) * 0.9f, Mathf.Abs(ext.z) * 0.95f);
                    bc.center = new Vector3(0f, bc.size.y / 2f + 0.05f, 0f);
                }
                catch (Exception e) { L("garage : voiture " + s.Name + " : " + e.Message); }
            }
        }

        static GameObject VaBox(Vector3 c, Vector3 s, string mat, bool collide = false) { GameObject g = SaPrim(vaHall, PrimitiveType.Cube, c, s, mats[mat], null, collide); g.name = "va_" + mat; return g; }
        static GameObject VaCyl(Vector3 c, float d, float h, string mat, Quaternion? r = null, bool collide = false) { GameObject g = SaPrim(vaHall, PrimitiveType.Cylinder, c, new Vector3(d, h / 2f, d), mats[mat], r, collide); g.name = "va_" + mat; return g; }

        // Dehors : ce qui donne vie a la facade (jamais de nom de marque) : cadres des portes, gouttiere, descentes, socle, fenetre du bureau, boitier, futs, pneus.
        static void VuokraDetails()
        {
            // (JD 06/11 : cadres, boitier et socle etaient 50 cm devant le mur) : tout se pose sur le mur de la collision
            float F = VaWallZ(12.9f, 1.5f);
            if (float.IsNaN(F)) { F = -0.12f; L("garage : mur de facade non trouve"); }
            var sb = new System.Text.StringBuilder("garage : mur de facade a z " + F.ToString("F2") + " ;");
            foreach (float x in new[] { 0.3f, 2.26f, 4f, 6f, 7.5f, 9.5f, 12.9f, 14f, 16f, 18.2f, 20f, 21.3f }) sb.Append(" ").Append(x.ToString("F1")).Append(":").Append(VaWallZ(x, 1.5f).ToString("F2"));
            L(sb.ToString());
            foreach (float[] d in new[] { new[] { Door1X0, Door1X1 }, new[] { Door2X0, Door2X1 } })
            {
                // (assez profonds pour fermer le tableau de l'ouverture, entre le mur et les vrais murs interieurs)
                VaBox(new Vector3(d[0] - 0.08f, 1.55f, F + 0.05f), new Vector3(0.16f, 3.1f, 0.3f), "va_kehys");
                VaBox(new Vector3(d[1] + 0.08f, 1.55f, F + 0.05f), new Vector3(0.16f, 3.1f, 0.3f), "va_kehys");
                VaBox(new Vector3((d[0] + d[1]) / 2f, 3.09f, F + 0.05f), new Vector3(d[1] - d[0] + 0.32f, 0.26f, 0.3f), "va_kehys");
            }
            VaCyl(new Vector3(10.8f, VaBandY + 0.03f, F - 0.12f), 0.12f, 22.6f, "va_teras", Quaternion.Euler(0f, 0f, 90f));
            foreach (float x in new[] { -0.4f, 21.9f, 7.6f }) VaCyl(new Vector3(x, VaBandY / 2f, F - 0.08f), 0.09f, VaBandY, "va_teras");
            // socle, seulement la ou il y a du mur (JD 06/11 : il passait devant les entrees)
            float sx = float.NaN; int spans = 0;
            for (float x = -0.5f; x <= 22.01f; x += 0.05f)
            {
                float wz = VaWallZ(x, 0.25f);
                bool wall = !float.IsNaN(wz) && Mathf.Abs(wz - F) < 0.3f && x < 21.95f && !(x > Door1X0 - 0.16f && x < Door1X1 + 0.16f) && !(x > Door2X0 - 0.16f && x < Door2X1 + 0.16f);
                if (wall && float.IsNaN(sx)) sx = x;
                if (!wall && !float.IsNaN(sx))
                {
                    if (x - sx > 0.2f) { VaBox(new Vector3((sx + x) / 2f, 0.17f, F - 0.03f), new Vector3(x - sx, 0.34f, 0.1f), "va_sokkeli"); spans++; L("garage : socle de " + sx.ToString("F2") + " a " + x.ToString("F2")); }
                    sx = float.NaN;
                }
            }
            // fenetre du bureau, a la place de celle du jeu
            Material glass = GameMat("window_glass") ?? GameMat("psk_glass");
            GameObject gl = VaBox(new Vector3(2.26f, 1.75f, F - 0.02f), new Vector3(1.5f, 1.3f, 0.02f), "va_tumma");
            if (glass != null) gl.GetComponent<Renderer>().sharedMaterial = glass;
            foreach (float s in new[] { -0.8f, 0.8f }) VaBox(new Vector3(2.26f + s, 1.75f, F - 0.03f), new Vector3(0.1f, 1.5f, 0.1f), "va_kehys");
            foreach (float s in new[] { -0.7f, 0.7f }) VaBox(new Vector3(2.26f, 1.75f + s, F - 0.03f), new Vector3(1.7f, 0.1f, 0.1f), "va_kehys");
            VaBox(new Vector3(2.26f, 1.75f, F - 0.03f), new Vector3(0.05f, 1.3f, 0.05f), "va_kehys");
            // boitier electrique ; fûts, pneus, palette, bidon, panneau de rallye : ceux du jeu
            VaBox(new Vector3(12.9f, 1.45f, F - 0.09f), new Vector3(0.5f, 0.7f, 0.18f), "va_kehys");
            VaReal("barrell", new Vector3(19.4f, 0.01f, -1.5f), 0f, 0, null, true);
            VaReal("barrell", new Vector3(20.15f, 0.01f, -1.25f), 40f, 0, null, true);
            VaReal("barrell", new Vector3(19.75f, 0.01f, -2.1f), 110f, 0, null, true);
            VaTires("tire_stock", new Vector3(21f, 0.01f, -1.5f), 4);
            VaTires("tire_stock", new Vector3(21.1f, 0.01f, -2.25f), 3);
            GameObject pal = VaReal("pallet", new Vector3(12.9f, 0.01f, -1.2f), 0f);
            float py = pal != null ? VaBounds(pal).max.y : 0.01f;
            VaReal("jerrycan_fuel", new Vector3(12.7f, py, -1.15f), 20f);
            if (pal != null) VaReal("kilju_bucket", new Vector3(13.2f, py, -1.25f), 0f);
            VaReal("rally_parcferme_sign1", new Vector3(6.6f, 0.01f, -1.15f), 0f, 0, Quaternion.LookRotation(Vector3.up, Vector3.right));
        }

        // Vrais objets du jeu (JD 06/11 : « utilise des vrais objets du jeu pour decorer »). Trouves par le nom de leur maillage : meme nom dans
        // les deux jeux, chemins differents. Seulement les objets que le jeu n'a pas regroupes (les autres sont illisibles : meubles du bureau de Fleetari).
        static readonly string[] VaWanted = { "trophy_gold", "trophy_silver", "trophy_bronze", "phone_handle", "notepad", "coffee_cup_home", "paper_stand1", "cash_register",
            "teimo_clock", "fire_extinguisher", "chair_wooden", "office_shelf", "bookshelf", "tire_stock", "tire_gobra", "barrell", "jerrycan_fuel", "jerrycan_diesel", "kilju_bucket", "pallet", "cardboard_box",
            "dyno_computer", "dyno_fancase", "dyno_platform", "motorhoist_frame", "floor_jack", "battery_charger", "spraycan", "rally_standbanner3", "rally_sign01", "rally_parcferme_sign1" };
        static Dictionary<string, Transform> vaMeshes;
        static readonly HashSet<string> vaMissing = new HashSet<string>();
        static Transform VaMeshSrc(string mesh, int up)
        {
            if (vaMeshes == null)
            {
                vaMeshes = new Dictionary<string, Transform>();
                var want = new HashSet<string>(VaWanted);
                foreach (MeshFilter f in Resources.FindObjectsOfTypeAll<MeshFilter>())
                {
                    if (f == null || f.sharedMesh == null || !want.Contains(f.sharedMesh.name)) continue;
                    Renderer r = f.GetComponent<Renderer>();
                    if (r == null || r.isPartOfStaticBatch) continue;
                    string path = Recon.Path(f.transform);
                    if (path.Contains("MWCoop") || path.Contains("va_") || path.Contains("osat_") || path.Contains("osuus_")) continue;
                    Transform old;
                    if (!vaMeshes.TryGetValue(f.sharedMesh.name, out old) || (!old.gameObject.activeInHierarchy && f.gameObject.activeInHierarchy)) vaMeshes[f.sharedMesh.name] = f.transform;
                }
            }
            Transform t;
            if (!vaMeshes.TryGetValue(mesh, out t)) return null;
            for (int i = 0; i < up && t.parent != null; i++) t = t.parent;
            return t;
        }
        // (bornes dans le repere de la halle : LocalBounds mesure depuis la boutique)
        static Bounds VaBounds(GameObject g)
        {
            bool first = true; Bounds o = new Bounds();
            foreach (MeshRenderer r in g.GetComponentsInChildren<MeshRenderer>())
            {
                Bounds mb = r.GetComponent<MeshFilter>().sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 k = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    Vector3 w = vaHall.InverseTransformPoint(r.transform.TransformPoint(k));
                    if (first) { o = new Bounds(w, Vector3.zero); first = false; } else o.Encapsulate(w);
                }
            }
            return o;
        }
        // copie posee : at = milieu du dessous, repere de la halle. Debout comme dans le jeu, tournee de yaw ; rot pour les objets que le jeu laisse couches.
        static GameObject VaReal(string mesh, Vector3 at, float yaw, int up = 0, Quaternion? rot = null, bool collide = false)
        {
            Transform src = VaMeshSrc(mesh, up);
            if (src == null) { if (vaMissing.Add(mesh)) L("garage : objet du jeu " + mesh + " absent"); return null; }
            var g = new GameObject("va_" + mesh);
            g.transform.SetParent(vaHall, false);
            int n = 0;
            foreach (MeshRenderer r in src.GetComponentsInChildren<MeshRenderer>(true))
            {
                MeshFilter mf = r.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null || r.GetComponent<TextMesh>() != null || r.isPartOfStaticBatch || mf.sharedMesh.name.StartsWith("Combined Mesh")) continue;
                // (pieces eteintes sous l'objet : ses autres etats, bouchon ouvert, ecrans...)
                bool off = false;
                for (Transform u = r.transform; u != src && u != null; u = u.parent) if (!u.gameObject.activeSelf) { off = true; break; }
                if (off) continue;
                var c = new GameObject(r.name); c.transform.SetParent(g.transform, false);
                c.transform.localPosition = Quaternion.Inverse(src.rotation) * (r.transform.position - src.position);
                c.transform.localRotation = Quaternion.Inverse(src.rotation) * r.transform.rotation;
                c.transform.localScale = r.transform.lossyScale;
                c.AddComponent<MeshFilter>().sharedMesh = mf.sharedMesh;
                c.AddComponent<MeshRenderer>().sharedMaterials = r.sharedMaterials;
                n++;
            }
            if (n == 0) { UnityEngine.Object.Destroy(g); if (vaMissing.Add(mesh)) L("garage : objet du jeu " + mesh + " sans maillage"); return null; }
            g.transform.localRotation = rot ?? Quaternion.Euler(0f, yaw, 0f) * Quaternion.Inverse(YawOnly(src.rotation)) * src.rotation;
            Bounds bo = VaBounds(g);
            g.transform.localPosition = at - new Vector3(bo.center.x, bo.min.y, bo.center.z);
            if (collide)
            {
                var cg = new GameObject("va_objet_coll"); cg.transform.SetParent(vaHall, false);
                var bc = cg.AddComponent<BoxCollider>(); bc.size = bo.size; bc.center = new Vector3(at.x, at.y + bo.size.y / 2f, at.z);
            }
            return g;
        }
        // pile de pneus du jeu, a plat
        static void VaTires(string mesh, Vector3 at, int count)
        {
            for (int k = 0; k < count; k++)
            {
                GameObject t = VaReal(mesh, at, 0f, 0, Quaternion.Euler(0f, 37f * k, 90f));
                if (t == null) return;
                at.y = VaBounds(t).max.y;
            }
        }

        // objet du jeu (comme OsatProp) dans le repere de la halle
        static GameObject VaProp(string name, Vector3 at, float yaw)
        {
            GameObject src = OsatPrefab(name);
            if (src == null) return null;
            var g = new GameObject("va_" + name);
            g.transform.SetParent(vaHall, false);
            int n = 0;
            foreach (MeshRenderer r in src.GetComponentsInChildren<MeshRenderer>(true))
            {
                MeshFilter mf = r.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null || r.GetComponent<TextMesh>() != null || mf.sharedMesh.name.StartsWith("Combined Mesh")) continue;
                var c = new GameObject(r.name); c.transform.SetParent(g.transform, false);
                c.transform.localPosition = src.transform.InverseTransformPoint(r.transform.position);
                c.transform.localRotation = Quaternion.Inverse(src.transform.rotation) * r.transform.rotation;
                c.AddComponent<MeshFilter>().sharedMesh = mf.sharedMesh;
                c.AddComponent<MeshRenderer>().sharedMaterials = r.sharedMaterials;
                n++;
            }
            if (n == 0) { UnityEngine.Object.Destroy(g); return null; }
            // (debout, JD 06/11 : les modeles ont leur hauteur sur z)
            g.transform.localRotation = Quaternion.Euler(270f, yaw, 0f);
            Bounds b = VaBounds(g);
            g.transform.localPosition = at - new Vector3(b.center.x, b.min.y, b.center.z);
            return g;
        }

        // Bureau (JD 06/11 : « essaye de decorer un peu l'endroit ») : etageres de produits du jeu, classeurs, affiches des voitures, tarifs, coupes, pneus.
        static void VuokraOffice()
        {
            // etagere, mur de droite : produits du jeu
            const float SX = 5.1f;
            foreach (float y in new[] { 0.5f, 1.05f, 1.6f }) VaBox(new Vector3(SX, y, 2.2f), new Vector3(0.5f, 0.04f, 3f), "va_tiski");
            foreach (float z in new[] { 0.72f, 3.68f }) VaBox(new Vector3(SX, 0.95f, z), new Vector3(0.5f, 1.9f, 0.05f), "va_tiski", true);
            string[] goods = { "motormoil1", "coolant0", "boxoilpump0", "atfoil0", "coolant0", "motormoil1", "boxtimingbelt" };
            int put = 0, real = 0;
            foreach (float y in new[] { 0.52f, 1.07f, 1.62f })
                for (int i = 0; i < 7; i++)
                {
                    Vector3 at = new Vector3(SX, y, 1.0f + i * 0.4f);
                    if (VaProp(goods[(put + i) % goods.Length], at, 0f) != null) { put++; real++; }
                }
            L("garage : etagere, " + real + " produits du jeu sur 21");
            // derriere le bureau : etagere du jeu avec ses classeurs sur My Summer Car, bibliotheque du jeu sur My Winter Car (l'autre y est regroupee) ; dessus, les coupes du jeu
            const float OF = 0.055f;
            GameObject fs = VaReal("office_shelf", new Vector3(0.74f, OF, 4.05f), 90f) ?? VaReal("bookshelf", new Vector3(0.82f, OF, 4.05f), 0f);
            float top = fs != null ? VaBounds(fs).max.y : 1.9f;
            VaReal("trophy_gold", new Vector3(0.8f, top, 4.05f), 90f);
            VaReal("trophy_silver", new Vector3(0.8f, top, 3.6f), 90f);
            VaReal("trophy_bronze", new Vector3(0.8f, top, 4.5f), 90f);
            // dans le coin, cote porte : pneus du jeu ; sous les tarifs : chaise ; pres de l'etagere : extincteur ; au mur : horloge
            VaTires("tire_stock", new Vector3(1.1f, OF, 0.7f), 4);
            VaReal("chair_wooden", new Vector3(0.85f, OF, 2.2f), 90f);
            VaReal("fire_extinguisher", new Vector3(5.2f, OF, 0.38f), 0f, 0, Quaternion.Euler(270f, 0f, 0f));
            VaReal("teimo_clock", new Vector3(5.5f, 2.2f, 2.2f), 180f, 1);
            // affiches des voitures, mur du fond
            int a = 0;
            foreach (Stand s in stands)
            {
                if (a >= 2) break;
                Texture2D poster = VuokraPoster(s);
                if (poster == null) continue;
                s.Poster = poster;
                float x = 1.45f + a * 1.45f;
                VaBox(new Vector3(x, 1.75f, 4.97f), new Vector3(1.26f, 0.9f, 0.03f), "va_tumma");
                var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
                UnityEngine.Object.Destroy(q.GetComponent<Collider>());
                q.name = "va_juliste"; q.transform.SetParent(vaHall, false);
                q.transform.localPosition = new Vector3(x, 1.75f, 4.94f); q.transform.localScale = new Vector3(1.18f, 0.82f, 1f);
                var m = new Material(Shader.Find("Legacy Shaders/Diffuse") ?? Shader.Find("Diffuse")) { mainTexture = poster, name = "va_juliste" };
                q.GetComponent<Renderer>().sharedMaterial = m; vaBaked.Add(m);
                a++;
            }
            // tarifs, mur de gauche
            var list = new List<Stand>(stands);
            Vector3 boardAt = new Vector3(0.56f, 1.5f, 1.55f);
            GameObject board = SaPanel(vaHall, boardAt, Quaternion.Euler(0f, -90f, 0f), 1.1f, 1.3f, new Color(0.93f, 0.91f, 0.82f), h =>
            {
                SaText(h, "HINNASTO", new Vector3(0f, 0.53f, 0f), Quaternion.identity, 0.016f, new Color(0.11f, 0.30f, 0.52f), TextAnchor.MiddleCenter);
                for (int i = 0; i < list.Count && i < 10; i++)
                {
                    SaText(h, list[i].Name, new Vector3(-0.48f, 0.34f - i * 0.1f, 0f), Quaternion.identity, 0.0085f, new Color(0.12f, 0.12f, 0.14f), TextAnchor.MiddleLeft, FontStyle.Normal);
                    SaText(h, list[i].Price + " mk", new Vector3(0.48f, 0.34f - i * 0.1f, 0f), Quaternion.identity, 0.0085f, new Color(0.12f, 0.12f, 0.14f), TextAnchor.MiddleRight, FontStyle.Normal);
                }
            }, 400f);
            board.name = "va_hinnasto";
        }

        // Le lampadaire du village qui tombait sur le parvis : au bout du parking, pied dans le sol (JD 06/11).
        static void MoveLamp(float now)
        {
            if (vaLampDone) return;
            if (vaLampUntil <= 0f) vaLampUntil = now + 40f;
            if (now > vaLampUntil) { vaLampDone = true; L("garage : pas de lampadaire sur le parvis"); return; }
            GameObject maps = GameObject.Find("MWCoop-Cartes");
            if (maps == null) return;
            Vector3 fl = vuokra.transform.TransformPoint(HallP(0f, 0f, 0f));
            Vector3 ax = vaHall.right, az = vaHall.forward;
            foreach (Transform t in maps.GetComponentsInChildren<Transform>(true))
            {
                if (t.name != "lampadaire") continue;
                Renderer r = t.GetComponentInChildren<Renderer>();
                if (r == null) continue;
                Vector3 d = r.bounds.center - fl;
                float x = Vector3.Dot(d, ax), z = Vector3.Dot(d, az);
                if (x < -2f || x > VaW + 2f || z < -VaApron - 1.5f || z > 1f) continue;
                Vector3 goal = fl + ax * -1.3f + az * (-VaApron + 2f);
                RaycastHit? g = GroundHit(goal + Vector3.up * 40f);
                goal.y = (g.HasValue ? g.Value.point.y : fl.y) - 0.25f;
                Vector3 foot = new Vector3(r.bounds.center.x, r.bounds.min.y, r.bounds.center.z);
                t.position += goal - foot;
                vaLampDone = true;
                L("garage : lampadaire deplace au bout du parking, en " + t.position.ToString("F1"));
                return;
            }
        }

        static void VuokraVendor()
        {
            Transform t = vuokra.transform;
            Vector3 spot = vaSource.InverseTransformPoint(SrcVendor + Vector3.up * 0.055f);
            vaVendor = Avatar.CreatePreview("VUOKRA-myyja", 0);
            if (vaVendor == null) L("garage : vendeur indisponible (corps de PNJ)");
            else { Transform r = vaVendor.Root.transform; r.SetParent(t, false); r.localPosition = spot; r.localRotation = HallR(180f); }
            // le bureau du jeu : sa collision, lisible, dessinee en bois
            GameObject rs = Game.FindAny("REPAIRSHOP");
            MeshCollider desk = null;
            if (rs != null) foreach (MeshCollider mc in rs.GetComponentsInChildren<MeshCollider>(true)) if (mc.name.StartsWith("inspection_desk") && mc.sharedMesh != null && mc.sharedMesh.isReadable) desk = mc;
            if (desk != null)
            {
                var mesh = new VaBuild();
                Vector3[] dv = desk.sharedMesh.vertices; int[] dt = desk.sharedMesh.triangles;
                for (int i = 0; i + 2 < dt.Length; i += 3)
                {
                    Vector3 a = desk.transform.TransformPoint(dv[dt[i]]), b = desk.transform.TransformPoint(dv[dt[i + 1]]), d = desk.transform.TransformPoint(dv[dt[i + 2]]);
                    Vector3 n = Vector3.Cross(b - a, d - a); if (n.sqrMagnitude < 1e-8f) continue;
                    mesh.Tri(a, b, d, n.normalized);
                }
                VaPart("va_tiski", mesh.Mesh(), new[] { mats["va_tiski"] }, Vector3.zero, Quaternion.identity, Vector3.one);
                var dc = new GameObject("va_tiski_coll"); dc.transform.SetParent(t, false);
                dc.transform.localPosition = vaSource.InverseTransformPoint(desk.transform.position);
                dc.transform.localRotation = Quaternion.Inverse(vaSource.rotation) * desk.transform.rotation;
                dc.transform.localScale = desk.transform.lossyScale;
                vaDeskColl = dc.AddComponent<MeshCollider>(); vaDeskColl.sharedMesh = desk.sharedMesh;
            }
            var cg = new GameObject("va_myyja_coll"); cg.transform.SetParent(t, false);
            cg.transform.position = t.TransformPoint(vaSource.InverseTransformPoint(SrcVendor + Vector3.up * 0.9f)); cg.transform.rotation = Quaternion.identity;   // (le repere du batiment n'a pas forcement y vers le haut)
            var cc = cg.AddComponent<CapsuleCollider>(); cc.height = 1.8f; cc.radius = 0.3f;
            vaVendorCol = cc;
            L("garage : vendeur " + (vaVendor != null ? "en place" : "absent (collision seule)") + ", bureau " + (desk != null ? desk.name : "absent"));
        }

        // Sur le bureau : telephone, bloc-notes, tasse, publicite, caisse, aux endroits plats de la collision du meuble. Dans les halles : banc d'essai, palan, cric, bidons, pneus, panneaux de rallye.
        static MeshCollider vaDeskColl;
        static void VuokraRealDecor()
        {
            if (vaDeskColl != null)
            {
                var pts = new List<Vector3>(); var seen = new Dictionary<int, int>();
                for (float x = 0.2f; x < 5.6f; x += 0.1f)
                    for (float z = 0f; z < 5.1f; z += 0.1f)
                    {
                        RaycastHit h;
                        if (!vaDeskColl.Raycast(new Ray(vaHall.TransformPoint(new Vector3(x, 3f, z)), Vector3.down), out h, 3f)) continue;
                        Vector3 pt = vaHall.InverseTransformPoint(h.point); pt.x = x; pt.z = z;
                        pts.Add(pt);
                        int k = Mathf.RoundToInt(pt.y / 0.02f); int o; seen.TryGetValue(k, out o); seen[k] = o + 1;
                    }
                int best = 0, bn = 0;
                foreach (var kv in seen) if (kv.Key * 0.02f > 0.5f && kv.Value > bn) { best = kv.Key; bn = kv.Value; }
                // (plateau : la hauteur la plus frequente ; on garde les points entoures de plateau, loin des bords et des cloisons)
                var flat = new HashSet<int>();
                foreach (Vector3 q in pts) if (Mathf.RoundToInt(q.y / 0.02f) == best) flat.Add(Mathf.RoundToInt(q.x * 10f) * 1000 + Mathf.RoundToInt(q.z * 10f));
                var inner = new List<Vector3>();
                foreach (Vector3 q in pts)
                {
                    int ix = Mathf.RoundToInt(q.x * 10f), iz = Mathf.RoundToInt(q.z * 10f);
                    if (flat.Contains(ix * 1000 + iz) && flat.Contains((ix - 1) * 1000 + iz) && flat.Contains((ix + 1) * 1000 + iz) && flat.Contains(ix * 1000 + iz - 1) && flat.Contains(ix * 1000 + iz + 1)) inner.Add(q);
                }
                L("garage : plateau du bureau a " + (best * 0.02f).ToString("F2") + " m, " + inner.Count + " points");
                if (inner.Count > 4)
                {
                    float x0 = float.MaxValue, x1 = float.MinValue, z0 = float.MaxValue, z1 = float.MinValue;
                    foreach (Vector3 q in inner) { x0 = Mathf.Min(x0, q.x); x1 = Mathf.Max(x1, q.x); z0 = Mathf.Min(z0, q.z); z1 = Mathf.Max(z1, q.z); }
                    bool alongX = x1 - x0 > z1 - z0;
                    inner.Sort((p, q) => alongX ? p.x.CompareTo(q.x) : p.z.CompareTo(q.z));
                    Func<float, Vector3> at = f => inner[Mathf.Clamp((int)(inner.Count * f), 0, inner.Count - 1)];
                    float side = alongX ? 90f : 0f;
                    VaReal("phone_handle", at(0.12f), side, 1);
                    VaReal("notepad", at(0.32f), side);
                    VaReal("coffee_cup_home", at(0.42f), side, 0, Quaternion.Euler(270f, side, 0f));
                    VaReal("paper_stand1", at(0.6f), side);
                    VaReal("cash_register", at(0.86f), side);
                }
            }
            const float HF = -0.08f;
            // devant, a droite de la grande porte : ordinateur et ventilateur du banc d'essai, bidons
            VaReal("dyno_computer", new Vector3(12.5f, HF, 1.0f), 90f, 0, null, true);
            VaReal("dyno_fancase", new Vector3(12.45f, HF, 2.9f), 90f, 0, null, true);
            VaReal("jerrycan_diesel", new Vector3(12.6f, HF, 4.5f), 90f);
            VaReal("jerrycan_fuel", new Vector3(12.6f, HF, 5.0f), 90f);
            // devant, a gauche de la grande porte : palan et cric, chargeur de batterie
            VaReal("motorhoist_frame", new Vector3(6.95f, HF, 2.6f), 90f, 1, null, true);
            VaReal("floor_jack", new Vector3(7.3f, HF, 4.9f), 200f, 3);
            VaReal("battery_charger", new Vector3(6.4f, HF, 0.7f), 0f);
            // au fond : pneus, banniere et panneau de rallye du jeu
            VaTires("tire_gobra", new Vector3(12.5f, HF, 13.9f), 4);
            VaTires("tire_stock", new Vector3(11.75f, HF, 14.05f), 2);
            VaReal("rally_standbanner3", new Vector3(9.2f, 2.45f, 14.9f), 0f, 0, Quaternion.LookRotation(Vector3.up, Vector3.right));
            VaReal("rally_sign01", new Vector3(12.9f, HF, 7.2f), 0f, 0, Quaternion.LookRotation(Vector3.up, Vector3.forward));
        }

        static void VuokraLights()
        {
            Transform t = vuokra.transform;
            foreach (Vector3 p in new[] { new Vector3(11f, 3.6f, 5f), new Vector3(16f, 3.6f, 6f), new Vector3(9f, 3.6f, 11f), new Vector3(16f, 3.6f, 12f), new Vector3(4f, 3.6f, 10f), new Vector3(3f, 2.5f, 2.5f) })
            {
                var g = new GameObject("va_valo"); g.transform.SetParent(t, false); g.transform.localPosition = HallP(p.x, p.y, p.z);
                var l = g.AddComponent<Light>(); l.type = LightType.Point; l.range = 12f; l.intensity = 1.1f; l.color = new Color(1f, 0.95f, 0.86f); l.shadows = LightShadows.None;
            }
        }

        static Vector3 VaGround(Vector3 p)
        {
            RaycastHit? g = GroundHit(p + Vector3.up * 6f);
            if (g.HasValue) p.y = g.Value.point.y + 0.5f;
            return p;
        }

        // place de livraison libre devant le garage (monde) ; cap : nez vers la route
        static bool VuokraBay(out Vector3 pos, out float yaw)
        {
            Transform t = vuokra.transform;
            yaw = (t.rotation * Quaternion.Inverse(vaSource.rotation) * Quaternion.LookRotation(SrcOut, Vector3.up)).eulerAngles.y;
            float[] xs = { 10f, 15f, 20f, 5f };
            foreach (float x in xs)
            {
                Vector3 p = VaGround(t.TransformPoint(HallP(x, 0.4f, -6.5f)));
                bool free = true;
                foreach (Collider c in Physics.OverlapSphere(p + Vector3.up * 0.8f, 1.6f))
                    if (!c.isTrigger && c.attachedRigidbody != null) { free = false; break; }
                if (free) { pos = p; return true; }
            }
            pos = VaGround(t.TransformPoint(HallP(xs[0], 0.4f, -6.5f)));
            return false;
        }

        // image du menu : affiche.png du dossier de l'addon, sinon son apercu
        static Texture2D VuokraPoster(Stand s)
        {
            if (s.Poster != null) return s.Poster;
            VazCar.Model m = VazCar.ModelOf(s.Model);
            foreach (string f in new[] { "affiche.png", "apercu-msc.png", "apercu.png" })
            {
                string p = Path.Combine(m.Dir, f);
                if (!File.Exists(p)) continue;
                try
                {
                    var tex = new Texture2D(2, 2, TextureFormat.RGB24, true) { name = "va_" + s.Folder + "_" + f };
                    tex.LoadImage(File.ReadAllBytes(p)); tex.wrapMode = TextureWrapMode.Clamp; tex.anisoLevel = 4;
                    return tex;
                }
                catch (Exception e) { L("garage : image " + p + " : " + e.Message); }
            }
            return null;
        }

        static TextMesh SaText(Transform parent, string s, Vector3 pos, Quaternion rot, float size, Color col, TextAnchor anchor, FontStyle st = FontStyle.Bold)
        {
            if (vaFont == null) vaFont = Resources.GetBuiltinResource(typeof(Font), "Arial.ttf") as Font;
            var g = new GameObject("va_teksti");
            g.transform.SetParent(parent, false); g.transform.localPosition = pos; g.transform.localRotation = rot;
            var tm = g.AddComponent<TextMesh>();
            tm.font = vaFont; tm.fontSize = 96; tm.characterSize = size; tm.anchor = anchor; tm.fontStyle = st; tm.color = col;
            tm.alignment = TextAlignment.Center; tm.text = s;
            g.GetComponent<MeshRenderer>().sharedMaterial = vaFont.material;
            return tm;
        }

        // Panneau de texte cuit : les TextMesh (fill, en metres autour du centre) rendus une fois dans une image, posee sur un quad d'un seul cote.
        static GameObject SaPanel(Transform parent, Vector3 pos, Quaternion rot, float w, float h, Color bg, Action<Transform> fill, float ppm)
        {
            int pw = Mathf.Clamp(Mathf.RoundToInt(w * ppm), 16, 2048), ph = Mathf.Clamp(Mathf.RoundToInt(h * ppm), 16, 2048);
            int layer = Studio.PreviewLayer;
            var holder = new GameObject("va_paistaa");
            holder.transform.position = new Vector3(0f, -900f, 0f);
            var cg = new GameObject("va_paistaa_kamera");
            Texture2D tex = null;
            try
            {
                fill(holder.transform);
                foreach (Transform c in holder.GetComponentsInChildren<Transform>(true)) c.gameObject.layer = layer;
                Camera cam = cg.AddComponent<Camera>();
                cam.enabled = false; cam.orthographic = true; cam.orthographicSize = h / 2f; cam.aspect = w / h;
                cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = bg; cam.cullingMask = 1 << layer;
                cam.nearClipPlane = 0.1f; cam.farClipPlane = 20f;
                cg.transform.position = holder.transform.position + new Vector3(0f, 0f, -5f); cg.transform.rotation = Quaternion.identity;
                var rt = new RenderTexture(pw, ph, 24);
                cam.targetTexture = rt; cam.Render();
                RenderTexture.active = rt;
                tex = new Texture2D(pw, ph, TextureFormat.RGB24, false) { name = "va_paneeli" };
                tex.ReadPixels(new Rect(0, 0, pw, ph), 0, 0); tex.Apply(false);
                tex.wrapMode = TextureWrapMode.Clamp; tex.anisoLevel = 4;
                RenderTexture.active = null; cam.targetTexture = null; rt.Release(); UnityEngine.Object.Destroy(rt);
            }
            finally { UnityEngine.Object.DestroyImmediate(holder); UnityEngine.Object.DestroyImmediate(cg); }
            var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
            UnityEngine.Object.Destroy(q.GetComponent<Collider>());
            q.name = "va_kyltti"; q.transform.SetParent(parent, false);
            q.transform.localPosition = pos; q.transform.localRotation = rot; q.transform.localScale = new Vector3(w, h, 1f);
            var m = new Material(Shader.Find("Legacy Shaders/Diffuse") ?? Shader.Find("Diffuse")) { mainTexture = tex, name = "va_kyltti" };
            q.GetComponent<Renderer>().sharedMaterial = m;
            vaBaked.Add(tex); vaBaked.Add(m);
            return q;
        }

        // ---------------------------------------------------------------- image par image
        static int vaTest; static float vaTestAt; static bool vaAim; static int vaClick; static bool vaObjDone;
        static bool vaWasOpen; static float vaShutAt = -10f;
        // [Test] VuokraLoue=1 (invite) : le F10 ne sort plus ces voitures, la location est le seul chemin
        static int vaRent; static float vaRentAt; static bool vaSaved, vaGiven, vaMineLogged, vaFlipped;
        static void VaRentTest(float now)
        {
            // ([Test] VuokraRetourneA=s : sa voiture sur le toit a s secondes)
            float flipAt = Config.GetFloat("Test", "VuokraRetourneA", 0f);
            if (flipAt > 0f && !vaFlipped && Time.timeSinceLevelLoad > flipAt) { vaFlipped = true; Transform tf = Vaz.Mine != 0 ? Vaz.CarOf(Vaz.OwnerOf(Vaz.Mine)) : null; if (tf != null) { tf.rotation = Quaternion.Euler(0f, tf.eulerAngles.y, 180f); tf.position += Vector3.up; L("essai location : ma voiture retournee"); } }
            // ([Test] VuokraSauveA=s : sauvegarde du jeu a s secondes ; VuokraRendA=s : rendre la sienne)
            float saveAt = Config.GetFloat("Test", "VuokraSauveA", 0f), giveAt = Config.GetFloat("Test", "VuokraRendA", 0f);
            if (saveAt > 0f && !vaSaved && Time.timeSinceLevelLoad > saveAt) { vaSaved = true; L("essai location : sauvegarde du jeu, ma voiture n " + Vaz.Mine); Game.SaveInPlace(); }
            if (giveAt > 0f && !vaGiven && Time.timeSinceLevelLoad > giveAt) { vaGiven = true; L("essai location : rendue, c'etait la n " + Vaz.Mine); Vaz.Release(); }
            if (Time.timeSinceLevelLoad > 40f && !vaMineLogged && Config.GetInt("Test", "VuokraQui", 0) != 0 && Time.timeSinceLevelLoad > Config.GetFloat("Test", "VuokraQuiA", 75f))
            {
                vaMineLogged = true;
                Transform tq = Vaz.Mine != 0 ? Vaz.CarOf(Vaz.OwnerOf(Vaz.Mine)) : null;
                L("essai location : apres chargement, ma voiture n " + Vaz.Mine + (tq != null ? ", haut y " + tq.up.y.ToString("F2") + " en " + tq.position.ToString("F1") : "") + ", argent " + (money != null ? money.Value.ToString("F0") : "?") + " ; fini");
            }
            int mode = Config.GetInt("Test", "VuokraLoue", 0);
            if (vaRent < 0 || mode == 0 || stands.Count == 0 || money == null) return;
            if (mode == 2)
            {
                // (l'hote : retourne la voiture de l'invite des qu'elle est la, puis note ce qu'il en voit)
                if (vaRent == 0 && Vaz.TestFlipOthers() > 0) { vaRent = 1; vaRentAt = now; L("essai location : voiture de l'invite retournee par l'hote"); }
                if (vaRent == 1 && now - vaRentAt > 10f) { vaRent = 2; L("essai location : chez l'hote, 10 s apres, haut y " + Vaz.TestOtherUp().ToString("F2")); }
                if (vaRent == 2 && now - vaRentAt > 26f) { vaRent = -1; L("essai location : chez l'hote, 26 s apres, haut y " + Vaz.TestOtherUp().ToString("F2")); }
                return;
            }
            if (vaRent == 0 && Time.timeSinceLevelLoad > 60f)
            {
                vaRent = 1; vaRentAt = now;
                money.Value = 5000f;
                L("essai location : hote " + Net.Session.IsHost + ", avant : ma voiture n " + Vaz.Mine);
                OpenVuokra(stands[0]);
                RentVuokra(stands[0]);
                L("essai location : " + stands[0].Name + " demandee, argent " + money.Value + " ; note « " + saNote + " »");
                VuokraOpen = false;
            }
            Transform car = Vaz.Mine != 0 ? Vaz.CarOf(Vaz.OwnerOf(Vaz.Mine)) : null;
            if (vaRent == 1 && now - vaRentAt > 14f)
            {
                L("essai location : apres 14 s, ma voiture n " + Vaz.Mine + ", argent " + money.Value + " ; note « " + saNote + " »");
                if (car == null) { vaRent = -1; L("essai location : fini"); return; }
                L("essai location : retournee ? haut y " + car.up.y.ToString("F2") + " en " + car.position.ToString("F1"));
                Autotest.PutPlayer(car.position + Vector3.right * 4f + Vector3.up);
                vaRent = 2; vaRentAt = now;
            }
            if (vaRent == 2 && now - vaRentAt > 3f)
            {
                L("essai location : tout pres " + Vaz.NearMine + ", remettre -> " + Vaz.Upright());
                vaRent = 3; vaRentAt = now;
            }
            if (vaRent == 3 && now - vaRentAt > 6f)
            {
                vaRent = -1;
                L("essai location : apres, haut y " + (car != null ? car.up.y.ToString("F2") + " en " + car.position.ToString("F1") : "?") + " ; fini");
            }
        }

        // [Test] VuokraObjets=1 : tous les objets du jeu non regroupes (donc copiables), un par maillage, pour choisir le decor
        static void VaObjets()
        {
            if (vaObjDone || Config.GetInt("Test", "VuokraObjets", 0) == 0 || Time.timeSinceLevelLoad < 25f) return;
            vaObjDone = true;
            var seen = new HashSet<string>(); int n = 0;
            foreach (MeshRenderer r in Resources.FindObjectsOfTypeAll<MeshRenderer>())
            {
                if (r == null) continue;
                MeshFilter f = r.GetComponent<MeshFilter>(); Mesh m = f != null ? f.sharedMesh : null;
                if (m == null || m.name.StartsWith("Combined Mesh") || r.isPartOfStaticBatch) continue;
                Vector3 s = Vector3.Scale(m.bounds.size, r.transform.lossyScale);
                if (s.magnitude < 0.04f || s.magnitude > 4.5f) continue;
                string path = Recon.Path(r.transform);
                if (path.Contains("MWCoop") || path.Contains("va_") || path.Contains("osat_") || path.Contains("osuus_")) continue;
                if (!seen.Add(m.name)) continue;
                L("objet : " + m.name + " | " + path + " | " + s.ToString("F2") + " | " + (r.sharedMaterial != null ? r.sharedMaterial.name : "-") + " | actif " + r.gameObject.activeInHierarchy + " | rot " + r.transform.rotation.eulerAngles.ToString("F0"));
                n++;
            }
            L("objets : fini, " + n + " maillages");
        }
        // [Test] VuokraClic=1 : le joueur pose sous le porche entre dans le bureau a pied (vrai CharacterController), puis clique sur le loueur
        static void VuokraClickTest(float now)
        {
            if (vaClick < 0 || Config.GetInt("Test", "VuokraClic", 0) == 0) return;
            Transform v = vuokra.transform;
            Func<float, float, float, Vector3> w = (x, y, z) => v.TransformPoint(HallP(x, y, z));
            GameObject pl = Game.PlayerGo;
            if (vaClick == 0 && Time.timeSinceLevelLoad > 35f && pl != null)
            {
                vaClick = 1; vaTestAt = now;
                Log.Info("essai clic : batiment rot " + vaSource.rotation.eulerAngles + ", y local vers " + vaSource.up.ToString("F2") + " ; ancienne zone de clic a " + (v.TransformPoint(vaSource.InverseTransformPoint(SrcVendor) + Vector3.up * 0.9f) - v.TransformPoint(vaSource.InverseTransformPoint(SrcVendor + Vector3.up * 0.9f))).magnitude.ToString("F2") + " m du loueur");
                Autotest.PutPlayer(Config.GetInt("Test", "VuokraDedans", 0) != 0 ? w(4.2f, 1.1f, 7.5f) : w(4.2f, 1.1f, -3f));
            }
            if (vaClick == 1 && now - vaTestAt > 3f && pl != null)
            {
                var cc = pl.GetComponent<CharacterController>();
                if (cc == null) { L("essai clic : pas de CharacterController"); vaClick = -1; return; }
                L("essai clic : controleur allume " + cc.enabled + ", arret du jeu " + Game.GlobalBool("PlayerStop"));
                cc.enabled = true;
                foreach (Collider c in Physics.OverlapSphere(pl.transform.position + Vector3.up * 0.3f, 1.2f))
                    if (!c.isTrigger && c.transform.root != pl.transform.root) L("essai clic : autour du joueur " + Recon.Path(c.transform) + " " + c.GetType().Name + " " + c.bounds.center.ToString("F1") + " " + c.bounds.size.ToString("F1"));
                Vector3 from = pl.transform.position, target = w(3.8f, 1.1f, 1.2f);
                for (int i = 0; i < 90; i++)
                {
                    Vector3 d = target - pl.transform.position; d.y = 0f;
                    if (d.magnitude < 0.1f) break;
                    cc.Move(d.normalized * 0.08f + Vector3.down * 0.02f);
                }
                Vector3 frontLeft = SrcHall + SrcOut * (VaD / 2f) - SrcAlong * (VaW / 2f);
                Vector3 src = vaSource.TransformPoint(v.InverseTransformPoint(pl.transform.position)) - frontLeft;
                L("essai clic : sol sous le joueur " + (cc.isGrounded ? "oui" : "non"));
                L("essai clic : a pied de " + from.ToString("F1") + " a " + pl.transform.position.ToString("F1") + ", dans la halle x " + Vector3.Dot(src, SrcAlong).ToString("F2") + ", z " + Vector3.Dot(src, -SrcOut).ToString("F2") + " (bureau : z de 0 a 5)");
                vaClick = 2; vaTestAt = now;
            }
            if (vaClick == 2 && now - vaTestAt > 2f) { vaClick = 3; vaTestAt = now; vaAim = true; }
            if (vaClick == 4 && now - vaTestAt > 0.02f) { vaAim = true; vaClick = 5; vaTestAt = now; return; }
            if (vaClick == 5 && now - vaTestAt > 0.1f) { L("essai croix : juste apres, menu ouvert " + VuokraOpen + " (attendu False)"); vaClick = 6; vaTestAt = now; return; }
            if (vaClick == 6 && now - vaTestAt > 0.6f) { vaAim = true; vaClick = 7; vaTestAt = now; return; }
            if (vaClick == 7 && now - vaTestAt > 0.1f) { L("essai croix : plus tard, menu ouvert " + VuokraOpen + " (attendu True)"); VuokraOpen = false; vaClick = -1; L("essai clic : fini"); return; }
            if (vaClick == 3 && now - vaTestAt > 1f)
            {
                vaClick = -1;
                L("essai clic : menu ouvert " + VuokraOpen + ", curseur voulu " + Menu.CursorWanted);
                // (la croix : meme enchainement, le menu se ferme puis un clic sur le loueur arrive aussitot)
                if (VuokraOpen) { VuokraOpen = false; vaClick = 4; vaTestAt = now; return; }
                Autotest.CaptureSoon("va-clic", 0.1f);
                if (Config.GetInt("Test", "VuokraSonde", 0) != 0) VaSonde();
                L("essai clic : fini");
            }
        }
        static void TickVuokra(float now)
        {
            if (vuokra == null) return;
            MoveLamp(now);
            VuokraKeys();
            // (JD 06/11 : la croix fermait et rouvrait le menu. Elle reagit a l'evenement de l'interface, et Input voit le meme clic ensuite, alors que le joueur vise encore le loueur)
            VuokraPayStep();
            if (vaWasOpen && !VuokraOpen) vaShutAt = now;
            vaWasOpen = VuokraOpen;
            if (Config.Testing) { VaObjets(); VaRentTest(now); VuokraClickTest(now); VuokraTest(now); ObjetsPhotoTest(); ObjetsMesureTest(); }
            Camera cam = Game.MainCam;
            bool near = cam != null && (cam.transform.position - vuokra.transform.position).sqrMagnitude < 3600f;
            if (guiText == null)
            {
                var gv = FsmVariables.GlobalVariables;
                guiUse = gv.FindFsmBool("GUIuse"); guiBuy = gv.FindFsmBool("GUIbuy"); guiText = gv.FindFsmString("GUIinteraction");
                money = gv.FindFsmFloat("PlayerMoney"); guiSub = gv.FindFsmString("GUIsubtitle");
            }
            bool hit = false;
            if (near && !VuokraOpen && vaVendorCol != null && !Game.GlobalBool("PlayerSleeps"))
            {
                RaycastHit h;
                // ([Test] VuokraClic : le vrai chemin, depuis la vraie camera du joueur, visee sur le loueur)
                Vector3 dir = vaAim ? (vaVendorCol.bounds.center - cam.transform.position).normalized : cam.transform.forward;
                hit = vaVendorCol.Raycast(new Ray(cam.transform.position, dir), out h, 3.2f);
                if (vaAim) L("essai clic : camera en " + cam.transform.position.ToString("F2") + ", loueur a " + (vaVendorCol.bounds.center - cam.transform.position).magnitude.ToString("F2") + " m, touche " + hit);
            }
            if (hit != vaLooked) { if (vaLooked) ClearGui(); vaLooked = hit; }
            if (!hit) { vaAim = false; return; }
            guiUse.Value = true;
            guiText.Value = Lang.TG("LOCATION DE VOITURES", "CAR RENTAL");
            if (vaAim) L("essai clic : texte du jeu « " + guiText.Value + " »");
            if (Input.GetMouseButtonDown(0) || vaAim)
            {
                vaAim = false;
                if (now - vaShutAt < 0.4f) { if (Config.Testing) L("garage : clic ignore juste apres la fermeture du menu"); return; }
                ClearGui(); vaLooked = false; OpenVuokra(saMenu ?? stands[0]);
            }
        }

        // (essai) points de l'emprise de chaque voiture exposee sur un mur du plan
        static bool[,] vaGrid;
        static void VaCheck()
        {
            const float cell = 0.5f, x0 = -2.5f, z0 = -3f;
            Vector3 frontLeft = SrcHall + SrcOut * (VaD / 2f) - SrcAlong * (VaW / 2f);
            foreach (Stand s in stands)
            {
                Transform pv = vuokra.transform.Find("va_paikka_" + s.Folder);
                BoxCollider bc = pv != null ? pv.GetComponent<BoxCollider>() : null;
                if (bc == null) { L("verif : " + s.Name + " pas exposee"); continue; }
                int bad = 0, n = 0;
                for (float a = -0.5f; a <= 0.5f; a += 0.1f)
                    for (float c = -0.5f; c <= 0.5f; c += 0.1f)
                    {
                        Vector3 local = new Vector3(a * bc.size.x, 0f, c * bc.size.z);
                        Vector3 src = vaSource.TransformPoint(vuokra.transform.InverseTransformPoint(pv.TransformPoint(local))) - frontLeft;
                        int i = Mathf.FloorToInt((Vector3.Dot(src, SrcAlong) - x0) / cell), j = Mathf.FloorToInt((Vector3.Dot(src, -SrcOut) - z0) / cell);
                        n++;
                        if (vaGrid != null && j >= 0 && j < vaGrid.GetLength(0) && i >= 0 && i < vaGrid.GetLength(1) && vaGrid[j, i]) bad++;
                    }
                L("verif : " + s.Name + " " + bad + "/" + n + " points sur un mur, taille " + bc.size.ToString("F1"));
            }
        }
        static void VaPlan(float height)
        {
            MeshCollider coll = null;
            foreach (MeshCollider mc in vaSource.GetComponentsInChildren<MeshCollider>(true)) if (mc.name == "coll" && mc.sharedMesh != null) coll = mc;
            if (coll == null) return;
            const float cell = 0.5f; const int nx = 54, nz = 52; float x0 = -2.5f, z0 = -3f;
            var grid = new char[nz, nx];
            for (int j = 0; j < nz; j++) for (int i = 0; i < nx; i++) grid[j, i] = '.';
            Vector3[] vs = coll.sharedMesh.vertices; int[] ts = coll.sharedMesh.triangles;
            float y = SrcHall.y + height;
            Vector3 frontLeft = SrcHall + SrcOut * (VaD / 2f) - SrcAlong * (VaW / 2f);
            for (int t = 0; t + 2 < ts.Length; t += 3)
            {
                Vector3[] p = { coll.transform.TransformPoint(vs[ts[t]]), coll.transform.TransformPoint(vs[ts[t + 1]]), coll.transform.TransformPoint(vs[ts[t + 2]]) };
                var hits = new List<Vector3>();
                for (int k = 0; k < 3; k++) { Vector3 s = p[k], e = p[(k + 1) % 3]; if ((s.y <= y) != (e.y <= y)) hits.Add(Vector3.Lerp(s, e, (y - s.y) / (e.y - s.y))); }
                if (hits.Count < 2) continue;
                for (float f = 0f; f <= 1f; f += 0.02f)
                {
                    Vector3 w = Vector3.Lerp(hits[0], hits[1], f) - frontLeft;
                    int i = Mathf.FloorToInt((Vector3.Dot(w, SrcAlong) - x0) / cell), j = Mathf.FloorToInt((Vector3.Dot(w, -SrcOut) - z0) / cell);
                    if (i >= 0 && i < nx && j >= 0 && j < nz) grid[j, i] = '#';
                }
            }
            if (height < 2f) { vaGrid = new bool[nz, nx]; for (int j = 0; j < nz; j++) for (int i = 0; i < nx; i++) vaGrid[j, i] = grid[j, i] == '#'; }
            if (Config.GetInt("Test", "VuokraPlan", 0) == 0) return;
            L("plan a " + height + " m : colonnes x de " + x0 + " par " + cell + ", lignes z de " + z0);
            for (int j = nz - 1; j >= 0; j--)
            {
                var sb = new System.Text.StringBuilder();
                for (int i = 0; i < nx; i++) sb.Append(grid[j, i]);
                L("plan z " + (z0 + j * cell).ToString("F1") + " " + sb);
            }
        }

        // [Test] VuokraSonde=1 : ce que le vrai garage laisse lire
        static void VaSonde()
        {
            GameObject rs = Game.FindAny("REPAIRSHOP");
            L("sonde : Building rot " + vaSource.rotation.eulerAngles + " ech " + vaSource.lossyScale + ", vuokra rot " + vuokra.transform.rotation.eulerAngles);
            foreach (MeshCollider mc in rs.GetComponentsInChildren<MeshCollider>(true))
            {
                Mesh m = mc.sharedMesh; if (m == null) continue;
                Vector2[] uv = m.isReadable ? m.uv : null;
                L("sonde : collision " + Recon.Path(mc.transform) + " maillage " + m.name + " lisible " + m.isReadable + " sommets " + m.vertexCount + " uv " + (uv != null ? uv.Length : -1) + " sous " + m.subMeshCount + " bornes " + m.bounds.size.ToString("F1"));
            }
            foreach (Renderer r in vaSource.GetComponentsInChildren<Renderer>(true))
            {
                MeshFilter f = r.GetComponent<MeshFilter>(); Mesh m = f != null ? f.sharedMesh : null; if (m == null) continue;
                int n = -1;
                try { Vector3[] vx = m.vertices; n = vx != null ? vx.Length : -1; } catch { }
                L("sonde : rendu " + Recon.Path(r.transform) + " " + m.name + " lisible " + m.isReadable + " sommets lus " + n + " fusion " + r.isPartOfStaticBatch + " actif " + r.gameObject.activeInHierarchy);
            }
            GameObject li = Game.FindAny("REPAIRSHOP/Lifter");
            if (li != null)
                foreach (Transform t in li.GetComponentsInChildren<Transform>(true))
                {
                    MeshFilter f = t.GetComponent<MeshFilter>(); Renderer r = t.GetComponent<Renderer>();
                    var sb = new System.Text.StringBuilder("sonde : pont " + Recon.Path(t) + " pos " + t.position.ToString("F2") + " rot " + t.rotation.eulerAngles.ToString("F0") + " ech " + t.lossyScale.ToString("F2") + " actif " + t.gameObject.activeSelf);
                    if (f != null && f.sharedMesh != null) sb.Append(" maillage ").Append(f.sharedMesh.name).Append(" lisible ").Append(f.sharedMesh.isReadable).Append(" fusion ").Append(r != null && r.isPartOfStaticBatch).Append(" bornes ").Append(r != null ? r.bounds.size.ToString("F2") : "-");
                    foreach (PlayMakerFSM s in t.GetComponents<PlayMakerFSM>()) sb.Append(" automate ").Append(s.FsmName).Append("[").Append(s.ActiveStateName).Append("]");
                    foreach (Component c in t.GetComponents<Component>()) if (c is Animation || c is Collider) sb.Append(" ").Append(c.GetType().Name);
                    L(sb.ToString());
                }
            var seen = new HashSet<string>();
            foreach (Renderer r in rs.GetComponentsInChildren<Renderer>(true))
                foreach (Material m in r.sharedMaterials)
                    if (m != null && m.mainTexture != null && seen.Add(m.mainTexture.name))
                    {
                        string n = m.mainTexture.name;
                        L("sonde : texture " + n + " " + m.mainTexture.width + "x" + m.mainTexture.height + " matiere " + m.name + " couleur " + (m.HasProperty("_Color") ? m.color.ToString() : "-") + " sur " + r.name);
                        if (n == "repair_shop_concrete" || n == "repair_shop_walls_inner" || n == "ATLAS_BUILDINGS" || n == "sheet_metal_wave" || n == "wood_panel6" || n == "metal_roof2" || n == "store_banners" || n == "store_floor")
                        {
                            RenderTexture rt = RenderTexture.GetTemporary(1024, 1024, 0); RenderTexture prev = RenderTexture.active;
                            Graphics.Blit(m.mainTexture, rt); RenderTexture.active = rt;
                            var t = new Texture2D(1024, 1024, TextureFormat.RGB24, false); t.ReadPixels(new Rect(0, 0, 1024, 1024), 0, 0); t.Apply();
                            string png = Path.Combine(Path.Combine(Log.DataDir, "dumps"), "photo-tex-" + n + ".png");
                            File.WriteAllBytes(png, t.EncodeToPNG());
                            RenderTexture.active = prev; RenderTexture.ReleaseTemporary(rt); UnityEngine.Object.Destroy(t);
                        }
                    }
            // ce qui est sur le parvis
            Transform v = vuokra.transform;
            Vector3 a = v.TransformPoint(HallP(-3f, 0f, -VaApron - 4f)), d = v.TransformPoint(HallP(VaW + 3f, 0f, 0f));
            Bounds zone = new Bounds((a + d) / 2f, new Vector3(Mathf.Abs(a.x - d.x), 60f, Mathf.Abs(a.z - d.z)));
            foreach (Renderer r in UnityEngine.Object.FindObjectsOfType<Renderer>())
                if (zone.Contains(r.bounds.center) && !r.transform.IsChildOf(v) && r.bounds.size.y > 2f)
                {
                    Transform t = r.transform; while (t.parent != null && t.parent.parent != null) t = t.parent;
                    L("sonde : parvis " + Recon.Path(r.transform) + " centre " + r.bounds.center.ToString("F1") + " taille " + r.bounds.size.ToString("F1") + " racine " + t.root.name);
                }
            L("sonde : fini");
        }

        // [Test] VuokraEssai=1 : photos du garage, menu ouvert, location de la premiere voiture
        static void VuokraTest(float now)
        {
            if (vaTest < 0 || Config.GetInt("Test", "VuokraEssai", 0) == 0) return;
            float t = Time.timeSinceLevelLoad;
            Transform v = vuokra.transform;
            Func<float, float, float, Vector3> w = (x, y, z) => v.TransformPoint(HallP(x, y, z));
            if (vaTest == 0 && t > 40f)
            {
                vaTest = 1; vaTestAt = now;
                VaPlan(1f); VaPlan(2.8f);
                VaCheck();
                if (Config.GetInt("Test", "VuokraSonde", 0) != 0) VaSonde();
                foreach (Cheats.Place pl in Cheats.Places) if (pl.Key == "vuokra") { QueteGolf.Shot("va-lieu", pl.At + Vector3.up * 0.4f, pl.Face + Vector3.up * 1.5f); L("essai : lieu sur la carte en " + pl.At + ", visant " + pl.Face); }
                Autotest.PutPlayer(w(11f, 1.2f, -8f));
                L("essai : joueur devant le garage");
            }
            if (vaTest == 1 && now - vaTestAt > 6f)
            {
                vaTest = 2; vaTestAt = now;
                QueteGolf.Shot("va-facade", w(11f, 2f, -26f), w(11f, 3f, 0f));
                QueteGolf.Shot("va-facade-biais", w(-12f, 2.5f, -20f), w(10f, 3f, 4f));
                QueteGolf.Shot("va-arriere", w(30f, 4f, 34f), w(11f, 2f, 9f));
                QueteGolf.Shot("va-ciel", w(11f, 70f, 2f), w(11.1f, 0f, 2.1f));
                QueteGolf.Shot("va-entree", w(9.5f, 1.7f, -4f), w(9.5f, 1.2f, 8f));
                QueteGolf.Shot("va-dedans", w(8.6f, 1.7f, 0.8f), w(9f, 1.0f, 13f));
                QueteGolf.Shot("va-dedans-fond", w(1.5f, 1.7f, 14f), w(12f, 1.0f, 5f));
                QueteGolf.Shot("va-halle2", w(16f, 1.7f, 3.8f), w(16f, 1.0f, 13f));
                QueteGolf.Shot("va-vendeur", w(3.8f, 1.7f, 0.4f), w(2.4f, 1.2f, 3.2f));
                QueteGolf.Shot("va-bureau", w(1.2f, 1.8f, 0.5f), w(4.6f, 0.9f, 4f));
                QueteGolf.Shot("va-bureau2", w(5f, 1.8f, 0.5f), w(0.9f, 1.2f, 3.8f));
                QueteGolf.Shot("va-pont", w(VaLiftX - 1.2f, 0.7f, VaLiftZ - 4.5f), w(VaLiftX, 0.3f, VaLiftZ));
                QueteGolf.Shot("va-pont2", w(14.5f, 0.5f, 8.8f), w(16.2f, 0.1f, 11.4f));
                QueteGolf.Shot("va-etagere", w(2.8f, 1.5f, 2.2f), w(5.1f, 1f, 2.2f));
                for (int si = 0; si < VaSlots.Length && si < stands.Count; si++)
                {
                    float sx = VaSlots[si].X, sz = VaSlots[si].Z;
                    QueteGolf.Shot("va-roues-" + si, w(sx - 2.4f, 0.28f, sz - 1.2f), w(sx, 0.2f, sz));
                }
                QueteGolf.Shot("va-banc", w(9.4f, 1.3f, 5.6f), w(11.6f, 0.3f, 10.2f));
                QueteGolf.Shot("va-porte2", w(16.1f, 1.6f, -4f), w(16.1f, 1.3f, 2f));
                QueteGolf.Shot("va-porte2-biais", w(11.3f, 1.7f, -3.2f), w(15.5f, 1.5f, -0.2f));
                QueteGolf.Shot("va-rase", w(22.8f, 1.6f, -0.9f), w(8f, 1.5f, -0.3f));
                QueteGolf.Shot("va-porte2-dedans", w(13.6f, 1.6f, 2.8f), w(16f, 1.4f, -0.2f));
                QueteGolf.Shot("va-entree-bas", w(9.6f, 1.2f, -3f), w(9.6f, 0.1f, 0.5f));
                QueteGolf.Shot("va-horloge", w(3.2f, 1.6f, 2.2f), w(5.5f, 2.3f, 2.2f));
                QueteGolf.Shot("va-panneau", w(10.2f, 1.5f, 7.2f), w(13f, 1.5f, 7.2f));
                QueteGolf.Shot("va-classeurs", w(3.4f, 1.7f, 4.4f), w(0.8f, 1.5f, 3.9f));
                QueteGolf.Shot("va-tiski", w(3.9f, 2f, 0.7f), w(2.6f, 0.9f, 2.6f));
                QueteGolf.Shot("va-avant", w(9.4f, 1.6f, 6.8f), w(7f, 0.6f, 2f));
                QueteGolf.Shot("va-droite", w(11.2f, 1.5f, 7.2f), w(12.5f, 0.8f, 2f));
                QueteGolf.Shot("va-fond", w(10f, 1.5f, 7.5f), w(10.5f, 1.6f, 15f));
                QueteGolf.Shot("va-dehors", w(19f, 1.7f, -6f), w(20.3f, 0.4f, -1.6f));
                QueteGolf.Shot("va-palette", w(11f, 1.5f, -4.5f), w(11f, 0.5f, -1.2f));
                QueteGolf.Shot("va-cloture", w(-5f, 2.2f, -6f), w(1f, 0.8f, 8f));
                int coll = 0;
                foreach (Collider vc in Physics.OverlapSphere(w(VaW / 2f, 1f, VaD / 2f), 22f))
                {
                    if (vc.transform.parent == null || vc.transform.parent.name != "voyage_collisions") continue;
                    Vector3 hp = vaHall.InverseTransformPoint(vc.bounds.center);
                    if (hp.x > -0.5f && hp.x < VaW + 0.5f && hp.z > -VaApron && hp.z < VaD + 0.5f) coll++;
                }
                L("essai : " + coll + " collisions de Voyage dans le garage ou son parvis");
                QueteGolf.Shot("va-sol", w(9f, 3.0f, 4f), w(10f, 0f, 11f));
                QueteGolf.Shot("va-gauche", w(-9f, 2f, -14f), w(8f, 2f, -2f));
                QueteGolf.Shot("va-porte", w(16f, 2f, -9f), w(16f, 1.5f, 0f));
                Vector3 c; float oy; string fl; OsatFrame(out c, out oy, out fl);
                QueteGolf.Shot("va-cardinal", c + Quaternion.Euler(0f, oy, 0f) * new Vector3(14f, 0f, 44f) + Vector3.up * (vaFloor + 3f), c + Quaternion.Euler(0f, oy, 0f) * new Vector3(14f, 0f, 0f) + Vector3.up * (vaFloor + 2f));
                OpenVuokra(stands[0]);
                Autotest.CaptureSoon("va-menu", 1f); vaTestAt = Time.realtimeSinceStartup;   // (les photos ci-dessus prennent plus d'une seconde : sans cela le menu etait referme avant sa photo)
            }
            if (vaTest == 2 && now - vaTestAt > 2f)
            {
                vaTest = 3; vaTestAt = now;
                PlayMakerFSM sun = Game.FindFsm(Game.Msc ? "MAP/SUN/Pivot/SUN" : "MAP/Sun/PivotSun/SUN", "Color");
                if (sun != null) { sun.FsmVariables.GetFsmInt("Time").Value = 24; sun.SendEvent(Game.Msc ? "WAKEUP" : "TIMESKIP"); }
                VuokraOpen = false;
            }
            if (vaTest == 3 && now - vaTestAt > 9f)
            {
                vaTest = 4; vaTestAt = now;
                QueteGolf.Shot("va-nuit", w(11f, 2f, -26f), w(11f, 3f, 0f));
                QueteGolf.Shot("va-nuit-enseigne", w(9f, 1.6f, -14f), w(9f, 4f, 0f));
            }
            if (vaTest == 4 && now - vaTestAt > 1f)
            {
                vaTest = -1;
                if (money != null) money.Value = 5000f;
                float before = money != null ? money.Value : 0f;
                RentVuokra(stands[0]);
                L("essai : loue " + stands[0].Name + ", argent " + before + " -> " + (money != null ? money.Value : 0f) + ", ma voiture n " + Vaz.Mine + " ; fini");
                VuokraOpen = false;
            }
        }
    }
}
