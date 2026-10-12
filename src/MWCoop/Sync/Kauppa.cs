using System;
using System.Collections.Generic;
using System.IO;
using HutongGames.PlayMaker;
using MWCoop.Net;
using UnityEngine;

namespace MWCoop
{
    // Boutique OSUUSKAUPPA s21 (demande de JD, 12/10 ; « s21 » = son pseudo, s fraktur) : petite boutique de village a
    // l'ouest du parking de la PSK, ou l'on n'achete que des objets du coop. Batiment : KauppaRakennus ; meubles :
    // KauppaKalusteet.
    //  - vendeur : corps de PNJ des avatars (Avatar.CreatePreview), haut maison (assets\kauppa\myyja-paita.png pose sur la
    //    texture de torse du jeu au chargement), beret corse modelise ici ;
    //  - a vendre : le sac de couchage du coop (SleepingBags), 149 mk, autant qu'on veut. Clic sur les sacs exposes :
    //    l'argent du joueur (PlayerMoney, une depense reste a lui : Wallet), son de caisse du jeu, le sac pose sur le
    //    comptoir. Le sac est cree par l'hote (ou en solo) : un invite lui envoie l'achat (@sacosto, SleepingBags).
    //    Avec la boutique, plus de sacs poses d'office au debut de partie ([Boutique] SacsAuDepart=1 pour les garder).
    //  - [Boutique] Active=0 : pas de boutique.
    // Essais : <donnees MWCoop>\kauppa-essai.txt, une action par ligne "secondes action ..." (comptees apres la
    // construction) : heure h | lieu x y z tx ty tz nom [attente] [fov] | argent n | osto | ovi 0/1 | fin.
    public static partial class Kauppa
    {
        public const int SleepingBagPrice = 149;

        public static bool Active { get { return Ajouts.On && Config.GetInt("Boutique", "Active", 1) != 0; } }

        // Sacs poses d'office au debut de partie (sans boutique : oui).
        public static bool StarterBags { get { return !Active || Config.GetInt("Boutique", "SacsAuDepart", 0) != 0; } }

        static float buildAt = -1;
        static bool built;

        static void L(string s) { Log.Info("boutique : " + s); }

        public static void OnLevelLoaded()
        {
            root = null; door = null; doorAngle = 0f; built = false; vendor = null; looked = null; testCam = null;
            gameMats = null; byName = null; guiText = null; guiUse = null; guiBuy = null; money = null; guiSub = null;
            basketTotal = 0f; registerT = null; registerCol = null; registerText = null; doorOpen = false; doorCol = null;
            lippu = null; lippuBackMesh = null;
            pussit.Clear(); pussiTried = false; pussiMesh = null; pussiMat = null; lookedBag = null; fDown = -1f; subUntil = -1f;
            OnLevelLoadedVideo();
            offers.Clear(); script.Clear(); scriptAt = 0; inside = false; counterBounds = new Bounds();
            buildAt = Active && Application.loadedLevelName == "GAME" ? Time.realtimeSinceStartup + Config.GetFloat("Boutique", "Delai", 12f) : -1f;
        }

        public static void Update()
        {
            if (buildAt < 0f) return;
            float now = Time.realtimeSinceStartup;
            if (!built)
            {
                if (now < buildAt) return;
                built = true;
                var sw = System.Diagnostics.Stopwatch.StartNew();
                try { Build(); } catch (Exception e) { L("construction : " + e); }
                L("construite en " + sw.ElapsedMilliseconds + " ms");
                t0 = now;
                LoadScript();
                if (root != null) LoadBags();
                gameMats = null; byName = null;   // (index liberes)
                return;
            }
            if (root == null) return;
            TickDoor();
            Interact();
            Greet(now);
            TickSay();
            TickBags(now);
            TickItems(now);
            TickLippu();
            RunScript(now);
            if (shotAt > 0f && now >= shotAt) { shotAt = -1f; TakeShot(); }
        }

        // ---------------------------------------------------------------- ressources (textures maison dans la DLL)
        static Texture2D Res(string name, TextureFormat fmt)
        {
            var t = new Texture2D(2, 2, fmt, true) { name = "kauppa_" + name };
            using (Stream s = typeof(Kauppa).Assembly.GetManifestResourceStream("MWCoop.kauppa." + name))
            {
                if (s == null) { L("texture absente de la DLL : " + name); return t; }
                var b = new byte[s.Length];
                int got = 0;
                while (got < b.Length) { int n = s.Read(b, got, b.Length - got); if (n <= 0) break; got += n; }
                t.LoadImage(b);
            }
            return t;
        }

        // premier obstacle solide (pas un declencheur) sous le point
        static RaycastHit? GroundHit(Vector3 from)
        {
            RaycastHit? best = null;
            foreach (RaycastHit h in Physics.RaycastAll(from, Vector3.down, 400f))
                if (!h.collider.isTrigger && (best == null || h.distance < best.Value.distance)) best = h;
            return best;
        }

        static string ObjPath(Transform t) { string p = t.name; while (t.parent != null) { t = t.parent; p = t.name + "/" + p; } return p; }

        // ---------------------------------------------------------------- articles en rayon
        class Offer
        {
            public string Item; public int Price; public string Fr, En; public Collider Col;
            public readonly List<GameObject> Shown = new List<GameObject>();   // exposes en rayon (caches quand on les prend)
            public int Taken;                                                  // pris, pas encore payes
        }
        static readonly List<Offer> offers = new List<Offer>();
        static Bounds counterBounds;

        // Sacs de couchage plies, exposes sur la tablette du milieu de la gondole (cote porte), etiquette de prix devant.
        static void Offers()
        {
            Mesh m; Material mat;
            if (!SleepingBags.DisplayModel(out m, out mat)) { L("sacs de couchage : modele indisponible, rien en rayon"); return; }
            // (gondole : StoreGondola(-2.7, -0.5, -0.55, 0.15, 1.45, 3) -- tablette 2, face avant de mid+0.02 a b1-0.02)
            float shelf = 0.14f + 2f * (1.45f - 0.2f) / 3.3f + 0.025f;
            float zMid = (-0.18f + 0.13f) / 2f;
            var offer = new Offer { Item = "makuupussi", Price = SleepingBagPrice, Fr = "SAC DE COUCHAGE", En = "SLEEPING BAG" };
            foreach (float x in new[] { -2.2f, -1.6f, -1.0f })
            {
                var g = new GameObject("osuus_myynnissa_makuupussi");
                offer.Shown.Add(g);
                g.transform.SetParent(root.transform, false);
                g.AddComponent<MeshFilter>().sharedMesh = m;
                g.AddComponent<MeshRenderer>().sharedMaterial = mat;
                Bounds b = m.bounds;
                // longueur du rouleau en travers de la tablette (le long de x)
                if (b.size.z > b.size.x) g.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);
                Bounds lb = LocalBounds(g);
                g.transform.localPosition = new Vector3(x - lb.center.x, shelf - lb.min.y + 0.002f, zMid - lb.center.z);
            }
            var col = new GameObject("osuus_osto_makuupussi");
            col.layer = 2;   // (Ignore Raycast : ne gene pas la main du jeu)
            col.transform.SetParent(root.transform, false);
            col.transform.localPosition = new Vector3(-1.6f, shelf + 0.17f, zMid);
            var bc = col.AddComponent<BoxCollider>();
            bc.isTrigger = true;
            bc.size = new Vector3(1.9f, 0.36f, 0.4f);
            offer.Col = bc;
            offers.Add(offer);
            try { VideoOffers(); } catch (Exception e) { L("video : rayons " + e); }
            Plane("hintalappu", new Vector3(-1.6f, shelf - 0.075f, 0.152f), 0.18f, 0.09f, Vector3.forward);
            L("en rayon : sac de couchage " + SleepingBagPrice + " mk");
        }

        static object looked;
        static float notEnoughUntil;
        static FsmBool guiUse, guiBuy;
        static FsmString guiText;
        static FsmFloat money;
        static float lastBuy;

        static void Interact()
        {
            if (guiText == null)
            {
                var gv = FsmVariables.GlobalVariables;
                guiUse = gv.FindFsmBool("GUIuse"); guiBuy = gv.FindFsmBool("GUIbuy"); guiText = gv.FindFsmString("GUIinteraction");
                money = gv.FindFsmFloat("PlayerMoney"); guiSub = gv.FindFsmString("GUIsubtitle");
            }
            float now = Time.realtimeSinceStartup;
            Camera cam = Camera.main;
            object hit = null;
            if (cam != null && (cam.transform.position - root.transform.position).sqrMagnitude < 40f * 40f && !Game.GlobalBool("PlayerSleeps"))
            {
                var ray = new Ray(cam.transform.position, cam.transform.forward);
                float best = 1.6f;
                RaycastHit h;
                foreach (Offer o in offers) if (o.Col != null && o.Col.Raycast(ray, out h, best)) { best = h.distance; hit = o; }
                if (registerCol != null && registerCol.Raycast(ray, out h, best)) { best = h.distance; hit = registerCol; }
                if (doorCol != null && doorCol.Raycast(ray, out h, best)) { best = h.distance; hit = doorCol; }
                foreach (Pussi b in pussit)
                    if (b.Go != null && b.Go.transform.parent == null && b.Col.Raycast(ray, out h, best)) { best = h.distance; hit = b; }   // (pas tenu en main)
            }
            else if (cam != null && !Game.GlobalBool("PlayerSleeps"))
            {
                // sacs de courses : partout (emportes a la maison, dans la voiture... ; JD, 12/10 : rien ne sortait loin de la boutique)
                var ray = new Ray(cam.transform.position, cam.transform.forward);
                float best = 1.6f;
                RaycastHit h;
                foreach (Pussi b in pussit)
                    if (b.Go != null && b.Go.transform.parent == null && b.Col.Raycast(ray, out h, best)) { best = h.distance; hit = b; }
            }
            if (hit != looked)
            {
                if (looked != null) ClearGui();
                looked = hit;
                fDown = -1f;
            }
            if (hit == null)
            {
                // objets du coin video (magnetoscope...) : vises partout, pas seulement pres de la boutique
                bool aimed = cam != null && !Game.GlobalBool("PlayerSleeps") && InteractItems(new Ray(cam.transform.position, cam.transform.forward), now);
                // sac de courses tenu en main : F bref = un article, F tenu = tout (devant soi)
                Pussi hb = aimed ? null : HeldBag();
                if (hb != null && !KameraBusy)
                {
                    if (Input.GetKeyDown(KeyCode.F)) heldFDown = now;
                    if (heldFDown >= 0f)
                    {
                        if (now - heldFDown >= 1.0f) { heldFDown = -1f; TakeOut(hb, true); }
                        else if (Input.GetKeyUp(KeyCode.F)) { heldFDown = -1f; TakeOut(hb, false); }
                    }
                }
                else heldFDown = -1f;
                return;
            }
            if (lookedItem != null) { ClearGui(); lookedItem = null; }

            var offer = hit as Offer;
            if (offer != null)
            {
                guiBuy.Value = true;
                guiText.Value = Lang.T(offer.Fr, offer.En) + " " + offer.Price.ToString("F2", System.Globalization.CultureInfo.InvariantCulture) + " MK";
                if (Input.GetMouseButtonDown(0)) Take(offer);
                else if (Input.GetMouseButtonDown(1)) PutBack(offer);
                return;
            }
            if (ReferenceEquals(hit, registerCol))
            {
                guiBuy.Value = true;
                if (now < notEnoughUntil) guiText.Value = "NOT ENOUGH MONEY";
                else guiText.Value = Lang.T("TOTAL ", "PRICE TOTAL ") + basketTotal.ToString("F2", System.Globalization.CultureInfo.InvariantCulture) + " MK";
                if (Input.GetMouseButtonDown(0))
                {
                    if (basketTotal > 0f && (money == null || money.Value < basketTotal)) notEnoughUntil = now + 2f;
                    Pay();
                }
                return;
            }
            if (ReferenceEquals(hit, doorCol))
            {
                guiUse.Value = true;
                if (Input.GetMouseButtonDown(0)) ToggleDoor(!doorOpen, true);
                return;
            }
            var bag = hit as Pussi;
            if (bag != null)
            {
                guiUse.Value = true;
                if (Input.GetKeyDown(KeyCode.F)) fDown = now;
                if (fDown >= 0f)
                {
                    float held = now - fDown;
                    if (held > 0.25f) guiText.Value = Lang.T("OUVERTURE DU SAC...", "OPENING THE BAG...");
                    if (held >= 1.5f) { fDown = -1f; guiText.Value = ""; TakeOut(bag, true); }
                    else if (Input.GetKeyUp(KeyCode.F)) { fDown = -1f; guiText.Value = ""; TakeOut(bag, false); }
                }
            }
        }

        // ---------------------------------------------------------------- accueil
        static bool inside;
        static float lastHello = -100f;

        static void Greet(float now)
        {
            Transform pl = Game.PlayerT;
            if (pl == null) return;
            Vector3 p = root.transform.InverseTransformPoint(pl.position);
            bool inNow = Mathf.Abs(p.x) < W2 - Wall && Mathf.Abs(p.z) < D2 - Wall && p.y > -0.6f && p.y < Ceil;
            if (inNow && !inside && now - lastHello > 30f)
            {
                lastHello = now;
                Say("Myyjä : Tervetuloa ! Bienvenue à l'Osuuskauppa.", "Shopkeeper: Tervetuloa! Welcome to the Osuuskauppa.");
            }
            inside = inNow;
        }

        // ---------------------------------------------------------------- vendeur
        static Avatar vendor;

        static void Vendor()
        {
            vendor = Avatar.CreatePreview("OSUUSKAUPPA-myyja", 0);
            if (vendor == null) { L("vendeur : corps de PNJ indisponible"); return; }
            Transform r = vendor.Root.transform;
            r.SetParent(root.transform, false);
            r.localPosition = vendorSpot;
            r.localRotation = Quaternion.identity;   // (face a la porte, +z)
            SkinnedMeshRenderer br = vendor.BodyRenderer;
            if (br != null)
            {
                Material shirt = ShirtMaterial();
                if (shirt != null)
                {
                    Material[] ms = br.sharedMaterials;
                    int k = 0;
                    for (int i = 0; i < ms.Length; i++) if (ms[i] != null && ms[i].name.StartsWith("char_shirt")) { k = i; break; }
                    ms[k] = shirt;
                    br.sharedMaterials = ms;
                }
            }
            var cg = new GameObject("osuus_myyja_coll");
            cg.transform.SetParent(root.transform, false);
            cg.transform.localPosition = vendorSpot + new Vector3(0f, 0.9f, 0f);
            var cc = cg.AddComponent<CapsuleCollider>(); cc.height = 1.8f; cc.radius = 0.28f;
            Beret(vendor);
            L("vendeur en place");
        }

        // Haut du vendeur : notre calque (sweat MWCoop, mains transparentes) pose sur la texture de torse du jeu.
        static Material ShirtMaterial()
        {
            Material tpl = Avatar.FindMaterial("char_shirt01");
            if (tpl == null || tpl.mainTexture == null) { L("vendeur : matiere de torse introuvable"); return null; }
            Texture src = tpl.mainTexture;
            int w = src.width, h = src.height;
            var rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32);
            RenderTexture prev = RenderTexture.active;
            Graphics.Blit(src, rt);
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, true) { name = "kauppa_myyja_paita" };
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            Texture2D layer = Res("myyja-paita.png", TextureFormat.ARGB32);
            Color[] px = tex.GetPixels();
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    Color c = layer.GetPixelBilinear((x + 0.5f) / w, (y + 0.5f) / h);
                    int i = y * w + x;
                    px[i] = Color.Lerp(px[i], new Color(c.r, c.g, c.b, 1f), c.a);
                }
            tex.SetPixels(px);
            tex.Apply(true);
            UnityEngine.Object.Destroy(layer);
            return new Material(tpl) { name = "kauppa_myyja_paita", mainTexture = tex };
        }

        // Beret corse : dome de laine noire un peu bombe, bandeau, petite queue au sommet ; penche sur le cote.
        // Pose : sur le sommet du crane (haut du corps, mesure), un peu enfonce. [Boutique] BeretPose = enfoncement sous le
        // sommet, avant, cote (m), inclinaison avant, cote (degres).
        static void Beret(Avatar a)
        {
            Transform head = a.HeadBone;
            if (head == null) { L("vendeur : pas d'os de tete pour le beret"); return; }
            // D'abord le beret du jeu (« Latsa », lätsä = beret ; JD, 12/10), pose comme sur le PNJ qui le porte (Looks).
            // ([Boutique] Beret=corse : le beret basque/corse noir modelise ci-dessous a la place)
            string key = Config.Get("Boutique", "Beret", "jeu") == "corse" ? null : Looks.Accessories(Looks.KHat).Find(k => k.StartsWith("latsa@"));
            Looks.AccInfo ai = key != null ? Looks.Acc(key) : null;
            if (ai != null)
            {
                var lg = new GameObject("osuus_latsa");
                lg.layer = head.gameObject.layer;
                lg.transform.parent = head;
                lg.transform.localPosition = ai.Pos;
                lg.transform.localRotation = ai.Rot;
                lg.transform.localScale = ai.Scale;
                Vector3 hd = a.HeadDelta;
                if (hd.sqrMagnitude > 1e-6f) lg.transform.position += hd;
                lg.AddComponent<MeshFilter>().sharedMesh = ai.Mesh;
                lg.AddComponent<MeshRenderer>().sharedMaterials = ai.Mats;
                L("vendeur : beret du jeu (" + key + ")");
                return;
            }
            L("vendeur : Latsa du jeu introuvable, beret modelise");
            var g = new GameObject("osuus_beret");
            g.AddComponent<MeshFilter>().sharedMesh = BeretMesh();
            var tex = new Texture2D(64, 64, TextureFormat.RGB24, true) { name = "kauppa_beret" };
            var rnd = new System.Random(5);
            var px = new Color[64 * 64];
            for (int i = 0; i < px.Length; i++) { float g0 = 0.075f + (float)rnd.NextDouble() * 0.04f; px[i] = new Color(g0, g0, g0 * 1.08f); }
            tex.SetPixels(px); tex.Apply(true);
            Material basis = GameMat("wood_panel7");
            var mat = new Material(basis != null ? basis.shader : Shader.Find("Legacy Shaders/Diffuse")) { name = "kauppa_beret", mainTexture = tex, color = Color.white };
            g.AddComponent<MeshRenderer>().sharedMaterial = mat;
            string[] p = Config.Get("Boutique", "BeretPose", "0.09,0.012,0.012,5,12").Split(',');
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            float[] v = new float[5];
            for (int i = 0; i < 5 && i < p.Length; i++) float.TryParse(p[i], System.Globalization.NumberStyles.Float, ci, out v[i]);
            Transform r = a.Root.transform;
            float crown = a.BodyBounds.max.y;
            Vector3 c = new Vector3(head.position.x, crown, head.position.z);
            g.transform.position = c - Vector3.up * v[0] + r.forward * v[1] + r.right * v[2];
            L("beret : sommet du crane " + (crown - head.position.y).ToString("F3") + " m au-dessus de l'os de la tete");
            g.transform.rotation = r.rotation * Quaternion.Euler(v[3], 0f, -v[4]);
            g.transform.SetParent(head, true);
        }

        static Mesh BeretMesh()
        {
            const int seg = 32, rings = 8;
            const float R = 0.125f, H = 0.06f, band = 0.014f, inner = 0.095f;
            var v = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>();
            // dome : de la bordure (phi 0, un peu plus large que la tete : bombe) au sommet
            for (int k = 0; k <= rings; k++)
            {
                float phi = k / (float)rings * Mathf.PI / 2f;
                float r = R * (1f + 0.2f * Mathf.Sin(phi * 2f)) * Mathf.Cos(phi);
                float y = band + H * Mathf.Sin(phi);
                for (int s = 0; s <= seg; s++)
                {
                    float a = s / (float)seg * Mathf.PI * 2f;
                    v.Add(new Vector3(Mathf.Cos(a) * r, y, Mathf.Sin(a) * r));
                    uv.Add(new Vector2(s / (float)seg, k / (float)rings));
                }
            }
            for (int k = 0; k < rings; k++)
                for (int s = 0; s < seg; s++)
                {
                    int i0 = k * (seg + 1) + s, i1 = i0 + 1, i2 = i0 + seg + 1, i3 = i2 + 1;
                    t.Add(i0); t.Add(i1); t.Add(i2); t.Add(i1); t.Add(i3); t.Add(i2);   // (faces vers l'exterieur)
                }
            // dessous du rebord (de la bordure au bandeau) et bandeau
            int b0 = v.Count;
            for (int s = 0; s <= seg; s++)
            {
                float a = s / (float)seg * Mathf.PI * 2f;
                v.Add(new Vector3(Mathf.Cos(a) * R, band, Mathf.Sin(a) * R)); uv.Add(new Vector2(s / (float)seg, 0f));
                v.Add(new Vector3(Mathf.Cos(a) * inner, band, Mathf.Sin(a) * inner)); uv.Add(new Vector2(s / (float)seg, 0.1f));
                v.Add(new Vector3(Mathf.Cos(a) * inner, 0f, Mathf.Sin(a) * inner)); uv.Add(new Vector2(s / (float)seg, 0.2f));
            }
            for (int s = 0; s < seg; s++)
            {
                int a0 = b0 + s * 3, a1 = a0 + 3;
                t.Add(a0); t.Add(a1); t.Add(a0 + 1); t.Add(a0 + 1); t.Add(a1); t.Add(a1 + 1);           // dessous du rebord
                t.Add(a0 + 1); t.Add(a1 + 1); t.Add(a0 + 2); t.Add(a0 + 2); t.Add(a1 + 1); t.Add(a1 + 2); // bandeau (exterieur)
            }
            // petite queue au sommet
            int q0 = v.Count;
            float top = band + H, qr = 0.006f, qh = 0.02f;
            for (int s = 0; s <= 8; s++)
            {
                float a = s / 8f * Mathf.PI * 2f;
                v.Add(new Vector3(Mathf.Cos(a) * qr, top - 0.004f, Mathf.Sin(a) * qr)); uv.Add(new Vector2(s / 8f, 0.9f));
                v.Add(new Vector3(Mathf.Cos(a) * qr * 0.7f, top + qh, Mathf.Sin(a) * qr * 0.7f)); uv.Add(new Vector2(s / 8f, 1f));
            }
            for (int s = 0; s < 8; s++) { int a0 = q0 + s * 2, a1 = a0 + 2; t.Add(a0); t.Add(a0 + 1); t.Add(a1); t.Add(a1); t.Add(a0 + 1); t.Add(a1 + 1); }
            var m = new Mesh { name = "kauppa_beret" };
            m.vertices = v.ToArray(); m.uv = uv.ToArray(); m.triangles = t.ToArray();
            m.RecalculateNormals();
            // interieur du dome (vu d'en dessous) : sommets dupliques, normales retournees
            int nDome = (rings + 1) * (seg + 1), nAll = v.Count, nTri = t.Count;
            var nrm = new List<Vector3>(m.normals);
            for (int i = 0; i < nDome; i++) { v.Add(v[i] * 0.995f); uv.Add(uv[i]); nrm.Add(-nrm[i]); }
            for (int k = 0; k < rings; k++)
                for (int s = 0; s < seg; s++)
                {
                    int i0 = nAll + k * (seg + 1) + s, i1 = i0 + 1, i2 = i0 + seg + 1, i3 = i2 + 1;
                    t.Add(i0); t.Add(i2); t.Add(i1); t.Add(i1); t.Add(i2); t.Add(i3);
                }
            m.vertices = v.ToArray(); m.uv = uv.ToArray(); m.normals = nrm.ToArray(); m.triangles = t.ToArray();
            m.RecalculateBounds();
            return m;
        }

        // ---------------------------------------------------------------- essais
        static readonly List<string[]> script = new List<string[]>();
        static int scriptAt;
        static float t0;
        static Camera testCam;
        static string shotName; static float shotAt = -1f;

        static void LoadScript()
        {
            string f = Path.Combine(Log.DataDir, "kauppa-essai.txt");
            if (!File.Exists(f)) return;
            foreach (string l in File.ReadAllLines(f))
            {
                string[] a = l.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (a.Length >= 2) script.Add(a);
            }
            L("essai : " + script.Count + " actions");
        }

        static float F(string s) { return float.Parse(s, System.Globalization.CultureInfo.InvariantCulture); }

        static void RunScript(float now)
        {
            while (scriptAt < script.Count)
            {
                string[] a = script[scriptAt];
                if (now - t0 < F(a[0])) return;
                scriptAt++;
                try
                {
                    switch (a[1])
                    {
                        case "heure":
                            GameObject sun = GameObject.Find("MAP/Sun/PivotSun/SUN");
                            foreach (PlayMakerFSM f in sun != null ? sun.GetComponents<PlayMakerFSM>() : new PlayMakerFSM[0])
                                if (f.FsmName == "Color") { f.FsmVariables.GetFsmInt("Time").Value = int.Parse(a[2]); f.SendEvent("TIMESKIP"); }
                            break;
                        case "argent": if (money != null) money.Value = F(a[2]); L("essai : argent " + a[2]); break;
                        case "tex": DumpTex(string.Join(" ", a, 2, a.Length - 2)); break;
                        case "lippu": LippuState(); break;
                        case "ota": { Offer o = a.Length > 2 ? offers.Find(x => x.Item == a[2]) : (offers.Count > 0 ? offers[0] : null); if (o != null) Take(o); break; }
                        case "nauhuri": TestVcr(); break;
                        case "kamera": TestKamera(a.Length < 3 || a[2] != "0"); break;
                        case "filmi": TestFilm(); break;
                        case "kanna": if (pussit.Count > 0 && EyeT != null) { Transform bt = pussit[pussit.Count - 1].Go.transform; pussit[pussit.Count - 1].Rb.isKinematic = true; bt.SetParent(EyeT, true); bt.position = EyeT.position + EyeT.forward * 0.5f; L("essai : sac en main"); } break;
                        case "irti": foreach (Pussi pb in pussit) if (pb.Go != null && pb.Go.transform.parent != null) { pb.Go.transform.SetParent(null, true); pb.Rb.isKinematic = false; L("essai : sac lache"); } break;
                        case "ulos": { Esine k = esineet.Find(e => e.Kind == KKamera); if (k != null) Eject(k); break; }
                        case "nimi": { Esine tp = esineet.FindLast(e => e.Kind == KVhs); if (tp != null) { tp.Go.transform.rotation = root.transform.rotation * Quaternion.Euler(0f, 180f, 0f); string c0 = tp.Data.Split('~')[0]; SetData(tp, (c0.Length > 0 ? c0 : "tyhja") + "~" + string.Join(" ", a, 2, a.Length - 2)); L("essai : titre " + tp.Data); } break; }
                        case "viseur": if (a.Length > 2 && a[2] == "0") ExitViewfinder(); else { EnterViewfinder(); vfForced = true; } break;
                        case "kuva": shotName = a[2]; shotAt = now + (a.Length > 3 ? F(a[3]) : 1f); break;
                        case "maksa": lastBuy = -10f; Pay(); break;
                        case "avaa": if (pussit.Count > 0) TakeOut(pussit[pussit.Count - 1], a.Length > 2 && a[2] == "kaikki"); break;
                        case "pussi": if (pussit.Count > 0) { Pussi pb = pussit[pussit.Count - 1]; L("essai : sac de courses en " + root.transform.InverseTransformPoint(pb.Go.transform.position).ToString("F2") + ", " + pb.Count + " article(s)"); } break;
                        case "ovi": forceDoor = a[2] == "1"; break;
                        case "lieu":
                        {
                            Vector3 at = new Vector3(F(a[2]), F(a[3]), F(a[4])), tg = new Vector3(F(a[5]), F(a[6]), F(a[7]));
                            Transform pl = Game.PlayerT;
                            if (pl != null) { var cc = pl.GetComponent<CharacterController>(); if (cc != null) cc.enabled = false; pl.position = at; }
                            if (testCam == null)
                            {
                                testCam = new GameObject("MWCoop-CameraBoutique").AddComponent<Camera>();
                                if (Camera.main != null) testCam.CopyFrom(Camera.main);
                                testCam.depth = 100;
                            }
                            testCam.farClipPlane = 3000f; testCam.nearClipPlane = 0.03f;
                            testCam.fieldOfView = a.Length > 10 ? F(a[10]) : 62f;
                            testCam.transform.position = at; testCam.transform.rotation = Quaternion.LookRotation(tg - at);
                            shotName = a[8]; shotAt = now + (a.Length > 9 ? F(a[9]) : 6f);
                            break;
                        }
                        case "fin": if (testCam != null) { UnityEngine.Object.Destroy(testCam.gameObject); testCam = null; } break;
                    }
                }
                catch (Exception e) { L("essai " + string.Join(" ", a) + " : " + e.Message); }
            }
        }

        static void TakeShot()
        {
            string dir = Path.Combine(Log.DataDir, "dumps");
            Directory.CreateDirectory(dir);
            Application.CaptureScreenshot(Path.Combine(dir, "kauppa-" + shotName + ".png"));
            L("essai : photo " + shotName);
        }
    }
}
