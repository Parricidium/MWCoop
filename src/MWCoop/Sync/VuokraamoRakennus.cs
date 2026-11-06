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
        static readonly Vector3 SrcHall = new Vector3(1562.8f, 4.65f, 733.3f), SrcOut = new Vector3(-0.947f, 0f, -0.321f).normalized;
        static readonly Vector3 SrcAlong = new Vector3(0.321f, 0f, -0.947f);
        const float VaW = 21.9f, VaD = 17.6f, VaBandY = 3.4f, VaOfficeX = 7.3f, VaOfficeZ = 6.4f, VaApron = 11f;
        // (place dans le repere du Cardinal : a sa gauche vu de face, facade sur la meme ligne)
        const float VaAtX = 24f, VaAtZ = -9f;
        static readonly Vector3 VaVendorSpot = new Vector3(9.0f, 0f, 7.6f);
        static readonly Slot[] VaSlots = {
            new Slot(9.8f, 14f, 180f, true), new Slot(15.2f, 14f, 180f, true), new Slot(20f, 13.6f, 178f, false), new Slot(4f, 13.4f, 184f, false),
            new Slot(14.5f, 5f, 176f, false), new Slot(19.6f, 5.2f, 183f, false), new Slot(3.6f, 9.4f, 90f, false), new Slot(10f, 3.6f, 180f, false),
        };

        public static bool VuokraOn { get { return Ajouts.Osat && Ville.On && Vaz.Enabled && Config.GetInt("Vuokraamo", "Active", 1) != 0; } }

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
        static float vaFloor;
        static readonly List<UnityEngine.Object> vaBaked = new List<UnityEngine.Object>();
        static readonly int[] VaPaint = { 2, 1, 3, 13, 5, 8, 6, 14 };

        static void ResetVuokra()
        {
            foreach (Stand s in stands) { if (s.Hold != null) VazCar.DestroyPreview(s.Hold); if (s.Poster != null) UnityEngine.Object.Destroy(s.Poster); }
            foreach (UnityEngine.Object o in vaBaked) if (o != null) UnityEngine.Object.Destroy(o);
            vaBaked.Clear();
            stands.Clear(); vuokra = null; vaSource = null; vaLooked = false; VuokraOpen = false; saMenu = null; vaVendor = null; vaVendorCol = null; vaTest = 0;
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
            for (int i = 0; i < px.Length; i++) { float g = Mathf.Clamp01(px[i].grayscale * 1.25f); px[i] = new Color(g, g, g); }
            t.SetPixels(px); t.Apply(true);
            RenderTexture.active = prev; RenderTexture.ReleaseTemporary(rt);
            vaBaked.Add(t);
            return t;
        }

        static void VuokraMaterials()
        {
            Color wall = new Color(0.20f, 0.44f, 0.72f);
            Mat("va_pelti", "wall_metal1", 2f, wall);
            Mat("va_katto", "wall_metal2", 2.5f, new Color(0.34f, 0.36f, 0.40f));
            Mat("va_lauta", "wood_panel1", 2f, new Color(0.30f, 0.34f, 0.42f));
            Mat("va_lattia", "repair_shop_floor", 4f, Color.white);
            Mat("va_sokkeli", "paint_black_matte", 1f, new Color(0.46f, 0.46f, 0.44f));
            Mat("va_sininen", "paint_black_matte", 1f, new Color(0.10f, 0.22f, 0.58f));
            Mat("va_teras", "paint_black_matte", 1f, new Color(0.24f, 0.25f, 0.27f));
            Mat("va_tumma", "paint_black_matte", 1f, new Color(0.12f, 0.12f, 0.14f));
            Mat("va_tiski", "wood12", 1f);
            if (!mats.ContainsKey("ki_asfaltti")) Mat("ki_asfaltti", "mwc_asphalt", 7f);
            try
            {
                foreach (string k in new[] { "va_pelti", "va_lauta" })
                {
                    Material m = mats[k];
                    if (m != null && m.mainTexture != null) m.mainTexture = GrayCopy(m.mainTexture, 512);
                }
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
        static void BuildVuokra()
        {
            GameObject bg = Game.FindAny("REPAIRSHOP/Building");
            if (bg == null) { L("garage : REPAIRSHOP/Building absent"); return; }
            vaSource = bg.transform;
            stands.Clear(); stands.AddRange(VuokraCatalog());
            if (stands.Count == 0) { L("garage : aucune voiture de rallye"); return; }
            VuokraMaterials();

            Vector3 c; float oy; string fl;
            OsatFrame(out c, out oy, out fl);
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
            VuokraShell();
            VuokraDoors();
            VuokraFloor();
            VuokraSign();
            VuokraApron(q);
            VuokraCars();
            VuokraVendor();
            VuokraLights();
            L("garage construit en " + vuokra.transform.position.ToString("F1") + ", plancher " + vaFloor.ToString("F2") + ", " + stands.Count + " voitures");
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
            var walls = new VaBuild(); var band = new VaBuild(); var roof = new VaBuild();
            Vector3[] vs = coll.sharedMesh.vertices; int[] ts = coll.sharedMesh.triangles;
            float bandWorld = SrcHall.y + VaBandY;
            for (int i = 0; i + 2 < ts.Length; i += 3)
            {
                Vector3 a = coll.transform.TransformPoint(vs[ts[i]]), b = coll.transform.TransformPoint(vs[ts[i + 1]]), d = coll.transform.TransformPoint(vs[ts[i + 2]]);
                Vector3 n = Vector3.Cross(b - a, d - a);
                if (n.sqrMagnitude < 1e-8f) continue;
                n.Normalize();
                if (Mathf.Abs(n.y) > 0.3f) { roof.Tri(a, b, d, n); continue; }
                VaBuild.Split(a, b, d, n, bandWorld, walls, band);
            }
            VaPart("va_seinat", walls.Mesh(), new[] { mats["va_pelti"] }, Vector3.zero, Quaternion.identity, Vector3.one);
            VaPart("va_lauta", band.Mesh(), new[] { mats["va_lauta"] }, Vector3.zero, Quaternion.identity, Vector3.one);
            VaPart("va_katto", roof.Mesh(), new[] { mats["va_katto"] }, Vector3.zero, Quaternion.identity, Vector3.one);
            L(string.Format("garage : coque de {0} triangles (murs {1}, bois {2}, toit {3})", ts.Length / 3, walls.Count, band.Count, roof.Count));
            var cg = new GameObject("va_collision"); cg.transform.SetParent(vuokra.transform, false);
            cg.transform.localPosition = vaSource.InverseTransformPoint(coll.transform.position);
            cg.transform.localRotation = Quaternion.Inverse(vaSource.rotation) * coll.transform.rotation;
            cg.AddComponent<MeshCollider>().sharedMesh = coll.sharedMesh;
            if (baseColl != null && baseColl.sharedMesh.isReadable)
            {
                var sokkeli = new VaBuild();
                Vector3[] bv = baseColl.sharedMesh.vertices; int[] bt = baseColl.sharedMesh.triangles;
                for (int i = 0; i + 2 < bt.Length; i += 3)
                {
                    Vector3 a = baseColl.transform.TransformPoint(bv[bt[i]]), b = baseColl.transform.TransformPoint(bv[bt[i + 1]]), d = baseColl.transform.TransformPoint(bv[bt[i + 2]]);
                    Vector3 n = Vector3.Cross(b - a, d - a); if (n.sqrMagnitude < 1e-8f) continue;
                    sokkeli.Tri(a, b, d, n.normalized);
                }
                VaPart("va_sokkeli", sokkeli.Mesh(), new[] { mats["va_sokkeli"] }, Vector3.zero, Quaternion.identity, Vector3.one);
            }
            // murs interieurs, toit du bureau, lampes : les pieces du jeu
            foreach (Renderer r in vaSource.GetComponentsInChildren<Renderer>(true))
                if (r.name == "repair_shop_wall_inner" || r.name == "repair_shop_office_roof") VaCopy(r.transform, vaSource);
            GameObject li = Game.FindAny("REPAIRSHOP/LOD/lights");
            if (li != null) VaCopy(li.transform, vaSource);
        }

        class VaBuild
        {
            readonly List<Vector3> v = new List<Vector3>(); readonly List<Vector2> u = new List<Vector2>(); readonly List<int> t = new List<int>();
            public int Count { get { return t.Count / 6; } }
            public void Tri(Vector3 a, Vector3 b, Vector3 c, Vector3 n)
            {
                Vector3 along = Mathf.Abs(n.y) > 0.3f ? SrcAlong : Vector3.Cross(Vector3.up, n).normalized;
                Vector3 up = Mathf.Abs(n.y) > 0.3f ? SrcOut : Vector3.up;
                foreach (bool back in new[] { false, true })
                {
                    int k = v.Count;
                    foreach (Vector3 p in new[] { a, b, c })
                    {
                        v.Add(vaSource.InverseTransformPoint(p));
                        u.Add(new Vector2(Vector3.Dot(p, along), Vector3.Dot(p, up)) / 2f);
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
            Vector3 openC = new Vector3(1555.6f, 7.6f, 731.4f), shutC = new Vector3(1557.7f, 6.1f, 725.2f);
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
                if (!up) { var bc = new GameObject("va_ovi_coll"); bc.transform.SetParent(vuokra.transform, false); bc.transform.localPosition = HallP(17f, 1.5f, 0f); bc.transform.localRotation = HallR(0f); bc.AddComponent<BoxCollider>().size = new Vector3(3.6f, 3.2f, 0.2f); }
            }
        }

        static void VuokraFloor()
        {
            GameObject f = SaPrim(vuokra.transform, PrimitiveType.Cube, HallP(VaW / 2f, -0.1f, VaD / 2f), new Vector3(VaW + 1f, 0.2f, VaD + 1f), mats["va_lattia"], HallR(0f), true);
            f.name = "va_lattia";
        }

        // Enseigne : a la place de celle du jeu (sa taille), peinte sur de la tole claire
        static void VuokraSign()
        {
            Vector3 at = new Vector3(1556.1f, 8.8f, 727.5f) + SrcOut * 0.28f;
            Vector3 pos = vaSource.InverseTransformPoint(at);
            Quaternion rot = Quaternion.Inverse(vaSource.rotation) * Quaternion.LookRotation(-SrcOut, Vector3.up);
            SaPanel(vuokra.transform, pos, rot, 4.7f, 1.25f, new Color(0.93f, 0.89f, 0.76f), h =>
            {
                SaText(h, "RALLI-VUOKRAAMO", new Vector3(0f, 0.20f, 0f), Quaternion.identity, 0.052f, new Color(0.11f, 0.30f, 0.52f), TextAnchor.MiddleCenter);
                SaText(h, "AUTOVUOKRAUS & KILPA-AUTOT", new Vector3(0f, -0.20f, 0f), Quaternion.identity, 0.021f, new Color(0.16f, 0.36f, 0.50f), TextAnchor.MiddleCenter);
                SaText(h, "puh. 987 561 33", new Vector3(0f, -0.43f, 0f), Quaternion.identity, 0.019f, new Color(0.30f, 0.35f, 0.15f), TextAnchor.MiddleCenter);
            }, 220f);
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

        static void VaLift(Transform t, Vector3 at, Quaternion rot)
        {
            const float H = 1.75f;
            var g = new GameObject("va_nostin"); g.transform.SetParent(t, false);
            g.transform.localPosition = at; g.transform.localRotation = rot;
            Transform l = g.transform;
            Material blue = mats["va_sininen"], steel = mats["va_teras"], dark = mats["va_tumma"];
            foreach (float s in new[] { -1f, 1f })
            {
                float x = s * 1.5f;
                SaPrim(l, PrimitiveType.Cube, new Vector3(x, 1.95f, 0f), new Vector3(0.28f, 3.9f, 0.34f), blue, null, true);
                SaPrim(l, PrimitiveType.Cube, new Vector3(x, 0.01f, 0f), new Vector3(0.6f, 0.02f, 0.62f), steel);
                SaPrim(l, PrimitiveType.Cube, new Vector3(x - s * 0.2f, H - 0.22f, 0f), new Vector3(0.16f, 0.44f, 0.3f), dark);
                foreach (float f in new[] { -1f, 1f })
                {
                    Vector3 a = new Vector3(x - s * 0.24f, H - 0.07f, f * 0.1f), b = new Vector3(s * 0.62f, H - 0.07f, f * 1.15f);
                    SaPrim(l, PrimitiveType.Cube, (a + b) / 2f, new Vector3(0.09f, 0.07f, (b - a).magnitude), steel, Quaternion.LookRotation(b - a));
                }
            }
            SaPrim(l, PrimitiveType.Cube, new Vector3(0f, 3.98f, 0f), new Vector3(3.3f, 0.16f, 0.22f), blue);
        }

        static void VuokraCars()
        {
            Transform t = vuokra.transform;
            for (int i = 0; i < stands.Count && i < VaSlots.Length; i++)
            {
                Stand s = stands[i]; Slot sl = VaSlots[i];
                var pivot = new GameObject("va_paikka_" + s.Folder).transform;
                pivot.SetParent(t, false);
                pivot.localPosition = HallP(sl.X, sl.Lift ? 1.75f : 0f, sl.Z);
                pivot.localRotation = HallR(sl.Yaw);
                if (sl.Lift) VaLift(t, HallP(sl.X, 0f, sl.Z), HallR(sl.Yaw));
                try
                {
                    s.Number = VaPaint[i % VaPaint.Length];
                    GameObject car = VazCar.BuildPreview(s.Number, Vaz.ColorOf(s.Number), 0, out s.Hold, VazCar.ModelOf(s.Model));
                    if (car == null) { L("garage : apercu impossible pour " + s.Name); continue; }
                    car.transform.SetParent(pivot, false);
                    car.transform.localPosition = Vector3.zero; car.transform.localRotation = Quaternion.identity;
                    // (pieces a moins de 8 m : les pieces vides d'un addon ont des bornes a l'origine du monde)
                    Bounds bb = new Bounds(); bool any = false;
                    foreach (Renderer r in car.GetComponentsInChildren<Renderer>(true))
                    {
                        if ((r.bounds.center - pivot.position).sqrMagnitude > 64f) continue;
                        if (!any) { bb = r.bounds; any = true; } else bb.Encapsulate(r.bounds);
                    }
                    if (!any) continue;
                    Vector3 lc = pivot.InverseTransformPoint(bb.center), lmin = pivot.InverseTransformPoint(bb.min);
                    car.transform.localPosition = new Vector3(-lc.x, -lmin.y, -lc.z);
                    Vector3 ext = pivot.InverseTransformVector(bb.size);
                    var bc = pivot.gameObject.AddComponent<BoxCollider>();
                    bc.size = new Vector3(Mathf.Abs(ext.x) * 0.92f, Mathf.Abs(ext.y) * 0.9f, Mathf.Abs(ext.z) * 0.95f);
                    bc.center = new Vector3(0f, bc.size.y / 2f + 0.05f, 0f);
                }
                catch (Exception e) { L("garage : voiture " + s.Name + " : " + e.Message); }
            }
            if (stands.Count > VaSlots.Length) L("garage : " + (stands.Count - VaSlots.Length) + " voiture(s) au menu seulement");
        }

        static void VuokraVendor()
        {
            Transform t = vuokra.transform;
            Vector3 spot = HallP(VaVendorSpot.x, 0f, VaVendorSpot.z);
            vaVendor = Avatar.CreatePreview("VUOKRA-myyja", 0);
            if (vaVendor == null) L("garage : vendeur indisponible (corps de PNJ)");
            else { Transform r = vaVendor.Root.transform; r.SetParent(t, false); r.localPosition = spot; r.localRotation = HallR(180f); }
            // comptoir devant lui
            SaPrim(t, PrimitiveType.Cube, HallP(VaVendorSpot.x, 0.5f, VaVendorSpot.z - 0.9f), new Vector3(2.2f, 1f, 0.6f), mats["va_tiski"], HallR(0f), true);
            var cg = new GameObject("va_myyja_coll"); cg.transform.SetParent(t, false);
            cg.transform.localPosition = HallP(VaVendorSpot.x, 0.9f, VaVendorSpot.z);
            var cc = cg.AddComponent<CapsuleCollider>(); cc.height = 1.8f; cc.radius = 0.3f;
            vaVendorCol = cc;
            L("garage : vendeur " + (vaVendor != null ? "en place" : "absent (collision seule)"));
        }

        static void VuokraLights()
        {
            Transform t = vuokra.transform;
            foreach (Vector3 p in new[] { new Vector3(11f, 3.6f, 5f), new Vector3(18f, 3.6f, 5f), new Vector3(11f, 3.6f, 13f), new Vector3(18f, 3.6f, 13f), new Vector3(4f, 3.6f, 12f) })
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
            var m = new Material(Shader.Find("Unlit/Texture") ?? Shader.Find("Legacy Shaders/Diffuse")) { mainTexture = tex, name = "va_kyltti" };
            q.GetComponent<Renderer>().sharedMaterial = m;
            vaBaked.Add(tex); vaBaked.Add(m);
            return q;
        }

        // ---------------------------------------------------------------- image par image
        static int vaTest; static float vaTestAt;
        static void TickVuokra(float now)
        {
            if (vuokra == null) return;
            VuokraKeys();
            if (Config.Testing) VuokraTest(now);
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
                hit = vaVendorCol.Raycast(new Ray(cam.transform.position, cam.transform.forward), out h, 2.8f);
            }
            if (hit != vaLooked) { if (vaLooked) ClearGui(); vaLooked = hit; }
            if (!hit) return;
            guiUse.Value = true;
            guiText.Value = Lang.TG("LOCATION DE VOITURES", "CAR RENTAL");
            if (Input.GetMouseButtonDown(0)) { ClearGui(); vaLooked = false; OpenVuokra(saMenu ?? stands[0]); }
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
                QueteGolf.Shot("va-entree", w(10.5f, 1.7f, -3f), w(12f, 1.2f, 12f));
                QueteGolf.Shot("va-dedans", w(9f, 1.7f, 1.5f), w(16f, 1.2f, 13f));
                QueteGolf.Shot("va-dedans-fond", w(20f, 1.7f, 16.5f), w(6f, 1.2f, 4f));
                QueteGolf.Shot("va-vendeur", w(9f, 1.7f, 4.8f), w(9f, 1.2f, 7.6f));
                Vector3 c; float oy; string fl; OsatFrame(out c, out oy, out fl);
                QueteGolf.Shot("va-cardinal", c + Quaternion.Euler(0f, oy, 0f) * new Vector3(14f, 0f, 44f) + Vector3.up * (vaFloor + 3f), c + Quaternion.Euler(0f, oy, 0f) * new Vector3(14f, 0f, 0f) + Vector3.up * (vaFloor + 2f));
                OpenVuokra(stands[0]);
                Autotest.CaptureSoon("va-menu", 1f);
            }
            if (vaTest == 2 && now - vaTestAt > 3f)
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
