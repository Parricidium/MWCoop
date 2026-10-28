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

        // Coin s21 a construire : la boutique ou le musee (chacun son interrupteur, Ajouts) ; [Boutique] Active=0 : rien.
        public static bool Active { get { return (Ajouts.Shop || Ajouts.Museum || Ajouts.Kino || Ajouts.Osat) && Config.GetInt("Boutique", "Active", 1) != 0; } }
        public static bool ShopOn { get { return Active && Ajouts.Shop; } }

        // Sacs poses d'office au debut de partie (sans boutique : oui).
        public static bool StarterBags { get { return !ShopOn || Config.GetInt("Boutique", "SacsAuDepart", 0) != 0; } }

        static float buildAt = -1;
        static bool built;

        static void L(string s) { Log.Info("boutique : " + s); }

        public static void OnLevelLoaded()
        {
            root = null; door = null; doorAngle = 0f; built = false; vendor = null; looked = null; testCam = null;
            gameMats = null; byName = null; guiText = null; guiUse = null; guiBuy = null; money = null; guiSub = null;
            basketTotal = 0f; registerT = null; registerCol = null; registerText = null; doorOpen = false; doorCol = null;
            ResetMuseo();
            ResetKino();
            ResetOsat();
            pussit.Clear(); bagsLoaded = false; pussiTried = false; pussiMesh = null; pussiMat = null; lookedBag = null; fDown = -1f; subUntil = -1f;
            OnLevelLoadedVideo();
            offers.Clear(); vitriinit.Clear(); videoCab = konsoliCab = null; script.Clear(); scriptAt = 0; inside = false; counterBounds = new Bounds();
            // (programmee meme coupee : chez un invite, les ajouts suivent l'hote depuis le 28/10, et son annonce arrive APRES le chargement --
            // decide dans Update une fois connue, Ajouts.Ready ; avant, un invite en SansAjouts=1 n'avait jamais ni boutique, ni musee, ni cinema)
            buildAt = Game.LevelName == "GAME" && Config.GetInt("Boutique", "Active", 1) != 0 ? Time.realtimeSinceStartup + Config.GetFloat("Boutique", "Delai", 12f) : -1f;
        }

        public static void OnGameSave() { OnGameSaveVideo(); OnGameSaveBags(); OnGameSaveKino(); OnGameSaveOsat(); }

        public static void Update()
        {
            if (buildAt < 0f) return;
            float now = Time.realtimeSinceStartup;
            if (!built)
            {
                if (now < buildAt || !Ajouts.Ready) return;   // (invite : les choix de l'hote d'abord)
                if (!Active) { buildAt = -1f; L("ajouts COOP coupes (lanceur, ou choix de l'hote) : rien a construire"); return; }
                built = true;
                var sw = System.Diagnostics.Stopwatch.StartNew();
                if (ShopOn) try { Build(); } catch (Exception e) { L("construction : " + e); }
                else L("boutique coupee (ajouts COOP : lanceur, ou choix de l'hote)");
                if (Ajouts.Museum) try { if (!ShopOn) Materials(); BuildMuseo(); } catch (Exception e) { L("musee : " + e); }   // (materiaux communs, d'habitude faits par la boutique)
                else L("musee coupe (ajouts COOP : lanceur, ou choix de l'hote)");
                if (KinoOn) try { if (!ShopOn && !Ajouts.Museum) Materials(); BuildKino(); } catch (Exception e) { L("cinema : " + e); }
                else L("cinema coupe (ajouts COOP : lanceur, ou choix de l'hote)");
                // magasin de pieces CARDINAL VARAOSAT (JD, 23/10)
                if (OsatOn) try { if (!ShopOn && !Ajouts.Museum && !KinoOn) Materials(); BuildOsat(); } catch (Exception e) { L("magasin de pieces : " + e); }
                else L("magasin de pieces coupe (ajouts COOP : lanceur, ou choix de l'hote)");
                L("construite en " + sw.ElapsedMilliseconds + " ms");
                t0 = now;
                LoadScript();
                if (root != null) LoadBags();
                gameMats = null; byName = null;   // (index liberes)
                return;
            }
            TickMuseo();
            TickKino(now);
            TickOsat(now);
            if (root == null)
            {
                // (cinema ou magasin de pieces sans boutique : leurs objets -- popcorn, bobines... -- et les sous-titres des vendeurs)
                if (kino != null || osat != null) { TickSay(); TickItems(now); RunScript(now); if (shotAt > 0f && now >= shotAt) { shotAt = -1f; TakeShot(); } }
                return;
            }
            TickDoor();
            TickVitriinit();
            BeretRetry(Time.realtimeSinceStartup);
            Interact();
            Greet(now);
            TickSay();
            TickBags(now);
            TickItems(now);
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
            public Vitriini Cab; public int Half;                              // (vitrine : moitie a ouvrir avant de prendre)
        }
        static readonly List<Offer> offers = new List<Offer>();
        static Bounds counterBounds;

        // Sacs de couchage plies, exposes sur la tablette du milieu de la gondole (cote porte), etiquette de prix devant.
        static void Offers()
        {
            EpicerieOffers();
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
            Camera cam = Game.MainCam;
            object hit = null;
            if (cam != null && (cam.transform.position - root.transform.position).sqrMagnitude < 40f * 40f && !Game.GlobalBool("PlayerSleeps"))
            {
                var ray = new Ray(cam.transform.position, cam.transform.forward);
                float best = 1.6f;
                RaycastHit h;
                foreach (Offer o in offers) if (o.Col != null && Reachable(o) && o.Col.Raycast(ray, out h, best)) { best = h.distance; hit = o; }
                foreach (Vitriini v in vitriinit) foreach (Lasi p in v.Panes) if (p.Col != null && p.Col.Raycast(ray, out h, best)) { best = h.distance; hit = p; }
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
                guiText.Value = Lang.TG(offer.Fr, offer.En) + " " + offer.Price.ToString("F2", System.Globalization.CultureInfo.InvariantCulture) + " MK";
                if (Input.GetMouseButtonDown(0)) Take(offer);
                else if (Input.GetMouseButtonDown(1)) PutBack(offer);
                return;
            }
            if (ReferenceEquals(hit, registerCol))
            {
                guiBuy.Value = true;
                if (now < notEnoughUntil) guiText.Value = "NOT ENOUGH MONEY";
                else guiText.Value = Lang.TG("TOTAL ", "PRICE TOTAL ") + basketTotal.ToString("F2", System.Globalization.CultureInfo.InvariantCulture) + " MK";
                if (Input.GetMouseButtonDown(0))
                {
                    if (basketTotal > 0f && (money == null || money.Value < basketTotal)) notEnoughUntil = now + 2f;
                    Pay();
                }
                return;
            }
            var pane = hit as Lasi;
            if (pane != null)
            {
                guiUse.Value = true;
                guiText.Value = pane.V.OpenHalf == pane.Half ? Lang.TG("FERMER LA VITRINE", "CLOSE THE CABINET") : Lang.TG("OUVRIR LA VITRINE", "OPEN THE CABINET");
                if (Input.GetMouseButtonDown(0)) ToggleVitriini(pane);
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
                    if (held > 0.25f) guiText.Value = Lang.TG("OUVERTURE DU SAC...", "OPENING THE BAG...");
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
            r.localRotation = Quaternion.Euler(0f, 180f, 0f);   // (epicerie : dos a la vitrine, face aux rayons)
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
        static Material ShirtMaterial(string png = "myyja-paita.png")
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
            Texture2D layer = Res(png, TextureFormat.ARGB32);
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
        // (beret du jeu pas encore releve par Looks a la construction -- epicerie, 23/10 : le vendeur avait le beret modelise ;
        // JD : « remet le beret du jeu » -- nouvel essai toutes les 2 s pendant une minute, le modelise est alors remplace)
        static GameObject beretModel; static float beretRetryUntil = -1f, beretNext;
        static void BeretRetry(float now)
        {
            if (beretRetryUntil < 0f || now < beretNext) return;
            beretNext = now + 2f;
            if (now > beretRetryUntil || vendor == null) { beretRetryUntil = -1f; return; }
            bool ok = false;
            try { ok = GameBeret(vendor); } catch (Exception e) { L("vendeur : beret du jeu " + e.Message); }
            if (!ok) { if (Config.GetInt("Test", "BeretLog", 0) != 0) L("vendeur : beret du jeu pas encore (" + string.Join(" ", Looks.Accessories(Looks.KHat).ToArray()) + ")"); return; }
            if (beretModel != null) UnityEngine.Object.Destroy(beretModel);
            beretModel = null; beretRetryUntil = -1f;
        }

        static bool GameBeret(Avatar a)
        {
            Transform head = a.HeadBone;
            if (head == null || Config.Get("Boutique", "Beret", "jeu") == "corse") return false;
            string key = Looks.Accessories(Looks.KHat).Find(k => k.StartsWith("latsa@"));
            Looks.AccInfo ai = key != null ? Looks.Acc(key) : null;
            if (ai == null) return false;
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
            return true;
        }

        static void Beret(Avatar a)
        {
            Transform head = a.HeadBone;
            if (head == null) { L("vendeur : pas d'os de tete pour le beret"); return; }
            if (GameBeret(a)) return;
            L("vendeur : Latsa du jeu introuvable, beret modelise (nouvel essai pendant une minute)");
            var g = new GameObject("osuus_beret");
            beretModel = g;
            if (Config.Get("Boutique", "Beret", "jeu") != "corse") { beretRetryUntil = Time.realtimeSinceStartup + 60f; beretNext = 0f; }
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
                            GameObject sun = GameObject.Find(Game.Msc ? "MAP/SUN/Pivot/SUN" : "MAP/Sun/PivotSun/SUN");
                            foreach (PlayMakerFSM f in sun != null ? sun.GetComponents<PlayMakerFSM>() : new PlayMakerFSM[0])
                                if (f.FsmName == "Color") { f.FsmVariables.GetFsmInt("Time").Value = int.Parse(a[2]); f.SendEvent("TIMESKIP"); }
                            break;
                        case "argent": if (money != null) money.Value = F(a[2]); L("essai : argent " + a[2]); break;
                        case "tex": DumpTex(string.Join(" ", a, 2, a.Length - 2)); break;
                        case "materiaux":
                        {
                            // (essais : toutes les matieres du jeu chargees -- nom, shader, texture -- dans dumps\materiaux.txt)
                            var seen = new SortedDictionary<string, string>();
                            foreach (Material x in Resources.FindObjectsOfTypeAll<Material>())
                                if (x != null && !seen.ContainsKey(x.name)) seen[x.name] = (x.shader != null ? x.shader.name : "?") + " | " + (x.mainTexture != null ? x.mainTexture.name + " " + x.mainTexture.width + "x" + x.mainTexture.height : "-");
                            var sbm = new System.Text.StringBuilder();
                            foreach (KeyValuePair<string, string> kv in seen) sbm.Append(kv.Key).Append(" | ").Append(kv.Value).Append('\n');
                            string dm = Path.Combine(Log.DataDir, "dumps"); Directory.CreateDirectory(dm);
                            File.WriteAllText(Path.Combine(dm, "materiaux.txt"), sbm.ToString());
                            L("essai : " + seen.Count + " matieres ecrites");
                            break;
                        }
                        case "museo": forceMuseoDoor = a.Length > 2 && a[2] == "auki"; L("essai : portes du musee " + (forceMuseoDoor ? "ouvertes" : "libres")); break;
                        case "ota": { Offer o = a.Length > 2 ? offers.Find(x => x.Item == a[2]) : (offers.Count > 0 ? offers[0] : null); if (o != null) Take(o); break; }
                        case "nauhuri": TestVcr(); break;
                        case "kamera": TestKamera(a.Length < 3 || a[2] != "0"); break;
                        case "filmi": TestFilm(); break;
                        case "tuo": { Esine tp = esineet.FindLast(e => e.Kind == KVhs); if (tp != null && EyeT != null) { tp.Rb.isKinematic = true; tp.Go.transform.SetParent(EyeT, true); } ChatCommand("/importer " + string.Join(" ", a, 2, a.Length - 2)); if (tp != null) { tp.Go.transform.SetParent(null, true); tp.Rb.isKinematic = false; } break; }
                        case "prends": { Esine k = esineet.Find(e => e.Kind == KKamera); PlayMakerFSM pf = PickUpFsm(); if (k != null && pf != null) { if (EyeT != null) k.Go.transform.position = EyeT.position + EyeT.forward * 0.6f; pf.FsmVariables.GetFsmGameObject("PickedObject").Value = k.Go; Game.SetState(pf, PickSetState); } L("essai : prise du camescope (" + (pf != null ? pf.ActiveStateName : "pas de main") + ")"); break; }
                        case "equipe": { Esine k = HeldKamera(); L("essai : en main " + (k != null)); if (k != null) Equip(k); break; }
                        case "rec": { Esine k = eqCam ?? HeldKamera(); if (k != null) { if (rec != null && eqStage == 2) Pause(!rec.Paused); else if (rec != null) StopRec("essai"); else StartRec(k); } break; }
                        case "pause": Pause(rec != null && !rec.Paused); break;
                        case "sors": ExitViewfinder(); break;
                        case "etat": { PlayMakerFSM pf = PickUpFsm(); Esine k = esineet.Find(e => e.Kind == KKamera); L("essai : main " + (pf != null ? pf.ActiveStateName + (pf.enabled ? "" : " (en pause)") : "?") + ", camescope tenu " + (k != null && HeldKamera() == k) + ", equipe " + eqStage + ", viseur " + Viewfinder + ", rec " + (rec != null ? (rec.Paused ? "pause" : "oui") : "non") + ", data " + (k != null ? k.Data : "")); break; }
                        case "montage": { Esine tp = esineet.FindLast(e => e.Kind == KVhs && TapeFilm(e.Data).Length > 0) ?? esineet.Find(e => e.Kind == KVhs); if (tp != null) OpenMontage(tp); break; }
                        case "joue": mPlaying = true; ShowMontageFrame(true); break;
                        case "sel": mA = F(a[2]); mB = Mathf.Min(F(a[3]), mTotal); Seek(mA); L("essai : selection " + mA + "-" + mB + " sur " + mTotal); break;
                        case "rogne": Trim(); L("essai : apres rognage " + mTotal.ToString("F2") + " s, data " + (mTape != null ? mTape.Data : "")); break;
                        case "exporte": Export(); break;
                        case "ferme": CloseMontage(); break;
                        case "rtenu": testRUntil = now + F(a[2]); break;
                        case "echap":
                        {
                            CloseMontage();
                            GameObject od = GameObject.Find(Game.Msc ? "Systems/Options" : "Systems/OptionsDB") ?? GameObject.Find("OptionsDB");
                            PlayMakerFSM om = null;
                            foreach (PlayMakerFSM pf in UnityEngine.Object.FindObjectsOfType<PlayMakerFSM>()) if (pf.FsmName == "Open Menu") om = pf;
                            if (om != null) om.SendEvent("MENU");
                            L("essai : Echap (menu du jeu " + (om != null ? om.ActiveStateName : "?") + ")");
                            break;
                        }
                        case "prendsvhs": { Esine k = esineet.FindLast(e => e.Kind == KVhs); PlayMakerFSM pf = PickUpFsm(); if (k != null && pf != null) { k.Rb.isKinematic = false; if (EyeT != null) k.Go.transform.position = EyeT.position + EyeT.forward * 0.6f; pf.FsmVariables.GetFsmGameObject("PickedObject").Value = k.Go; Game.SetState(pf, PickSetState); } L("essai : prise de la cassette (" + (pf != null ? pf.ActiveStateName : "pas de main") + "), tenue " + (HeldTape() != null)); break; }
                        case "focus": testFocus = true; break;
                        case "photo": Autotest.CaptureSoon(a[2], 0.2f); break;
                        case "texte": { var tfi = typeof(GUIUtility).GetProperty("textFieldInput", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public); L("essai : keyboardControl " + GUIUtility.keyboardControl + ", textFieldInput " + (tfi != null ? tfi.GetValue(null, null) : "?") + ", eat " + Input.eatKeyPressOnTextFieldFocus); break; }
                        case "reprend": { FsmBool im = FsmVariables.GlobalVariables.FindFsmBool("PlayerInMenu"); if (im != null) im.Value = false; GameObject sy = GameObject.Find("Systems"); Transform omn = sy != null ? sy.transform.Find("OptionsMenu") : null; if (omn != null) omn.gameObject.SetActive(false); L("essai : menu du jeu referme"); break; }
                        case "camera": { FsmBool im = FsmVariables.GlobalVariables.FindFsmBool("PlayerInMenu"); L("essai : Game.MainCam " + (Game.MainCam != null ? Game.MainCam.name : "nul") + ", PlayerInMenu " + (im != null && im.Value) + ", curseur bloque " + Screen.lockCursor); break; }
                        case "liste":
                        {
                            // liste <chemin> [profondeur] : enfants (nombre de leurs enfants, position) -- essais
                            int sl2 = a[2].IndexOf('/'); GameObject lg = GameObject.Find(sl2 < 0 ? a[2] : a[2].Substring(0, sl2));
                            if (lg != null && sl2 >= 0) { Transform lt = lg.transform.Find(a[2].Substring(sl2 + 1)); lg = lt != null ? lt.gameObject : null; }
                            if (lg == null) { L("essai : pas de " + a[2]); break; }
                            int depth = a.Length > 3 ? int.Parse(a[3]) : 1;
                            System.Action<Transform, int> walk = null;
                            walk = (tr, d) =>
                            {
                                foreach (Transform ch in tr)
                                {
                                    L("essai :" + new string(' ', d * 2) + " " + ch.name + " (" + ch.childCount + ") " + ch.position.ToString("F0") + (ch.gameObject.activeSelf ? "" : " [off]"));
                                    if (d < depth) walk(ch, d + 1);
                                }
                            };
                            L("essai : liste de " + a[2] + " (" + lg.transform.childCount + ")");
                            walk(lg.transform, 1);
                            break;
                        }
                        case "fsms":
                        {
                            int sl = a[2].IndexOf('/'); GameObject fg = GameObject.Find(sl < 0 ? a[2] : a[2].Substring(0, sl));
                            if (fg != null && sl >= 0) { Transform ft = fg.transform.Find(a[2].Substring(sl + 1)); fg = ft != null ? ft.gameObject : null; }
                            if (fg == null) { L("essai : pas de " + a[2]); break; }
                            foreach (PlayMakerFSM pf in fg.GetComponentsInChildren<PlayMakerFSM>(true))
                            {
                                if (a.Length > 3 && pf.FsmName != string.Join(" ", a, 3, a.Length - 3)) continue;
                                L("essai : FSM " + pf.gameObject.name + "/'" + pf.FsmName + "' etat " + pf.ActiveStateName + (pf.enabled ? "" : " (coupe)"));
                                foreach (HutongGames.PlayMaker.FsmState st in pf.FsmStates)
                                {
                                    var sb = new System.Text.StringBuilder("essai :    " + st.Name + " :");
                                    foreach (HutongGames.PlayMaker.FsmTransition tr in st.Transitions) sb.Append(" ").Append(tr.EventName).Append("->").Append(tr.ToState);
                                    sb.Append(" |");
                                    foreach (HutongGames.PlayMaker.FsmStateAction ac in st.Actions)
                                    {
                                        sb.Append(" ").Append(ac.GetType().Name).Append("(");
                                        foreach (System.Reflection.FieldInfo fi in ac.GetType().GetFields())
                                        {
                                            object v = fi.GetValue(ac);
                                            string vs = v is HutongGames.PlayMaker.NamedVariable ? ((HutongGames.PlayMaker.NamedVariable)v).Name + "=" + v : v is HutongGames.PlayMaker.FsmEvent ? "evt:" + ((HutongGames.PlayMaker.FsmEvent)v).Name : v is HutongGames.PlayMaker.FsmOwnerDefault ? "owner" : "" + v;
                                            sb.Append(fi.Name).Append("=").Append(vs).Append(",");
                                        }
                                        sb.Append(")");
                                    }
                                    L(sb.ToString());
                                }
                            }
                            break;
                        }
                        case "cassette": { Transform ey = EyeT; if (ey != null) { Esine tp = esineet.FindLast(e => e.Kind == KVhs); Vector3 at = ey.position + ey.forward * 0.6f; if (tp == null) CreateItem("essaivhs", KVhs, at, Quaternion.identity, a.Length > 2 ? a[2] : "tyhja"); else { tp.Go.transform.SetParent(null, true); tp.Go.transform.position = at; if (a.Length > 2) tp.Data = a[2]; } tp = esineet.FindLast(e => e.Kind == KVhs); if (tp != null && tp.Rb != null) { tp.Rb.isKinematic = true; tp.Rb.velocity = Vector3.zero; } L("essai : cassette devant les yeux " + at.ToString("F2")); } break; }
                        case "kanna": if (pussit.Count > 0 && EyeT != null) { Transform bt = pussit[pussit.Count - 1].Go.transform; pussit[pussit.Count - 1].Rb.isKinematic = true; bt.SetParent(EyeT, true); bt.position = EyeT.position + EyeT.forward * 0.5f; L("essai : sac en main"); } break;
                        case "irti": foreach (Pussi pb in pussit) if (pb.Go != null && pb.Go.transform.parent != null) { pb.Go.transform.SetParent(null, true); pb.Rb.isKinematic = false; L("essai : sac lache"); } break;
                        case "ulosv": { Esine v = esineet.Find(e => e.Kind == KNauhuri && Tape(e).Length > 0); if (v != null) { Eject(v); L("essai : magnetoscope en " + v.Go.transform.position.ToString("F2")); } break; }
                        case "ou": foreach (Esine e in esineet) if (e.Kind == KVhs && e.Go != null) L("essai : cassette " + e.Id + " en " + e.Go.transform.position.ToString("F2")); break;
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
                                if (Game.MainCam != null) testCam.CopyFrom(Game.MainCam);
                                testCam.depth = 100;
                            }
                            testCam.farClipPlane = 3000f; testCam.nearClipPlane = 0.03f;
                            testCam.fieldOfView = a.Length > 10 ? F(a[10]) : 62f;
                            testCam.transform.position = at; testCam.transform.rotation = Quaternion.LookRotation(tg - at);
                            shotName = a[8]; shotAt = now + (a.Length > 9 ? F(a[9]) : 6f);
                            break;
                        }
                        // (epicerie : camera en coordonnees de la boutique ; vitrine ouverte)
                        case "sisalla":
                        {
                            Vector3 at = root.transform.TransformPoint(new Vector3(F(a[2]), F(a[3]), F(a[4]))), tg = root.transform.TransformPoint(new Vector3(F(a[5]), F(a[6]), F(a[7])));
                            Transform pl = Game.PlayerT;
                            if (pl != null) { var cc = pl.GetComponent<CharacterController>(); if (cc != null) cc.enabled = false; pl.position = at - Vector3.up * 1.5f; }
                            if (testCam == null) { testCam = new GameObject("MWCoop-CameraBoutique").AddComponent<Camera>(); if (Game.MainCam != null) testCam.CopyFrom(Game.MainCam); testCam.depth = 100; }
                            testCam.farClipPlane = 3000f; testCam.nearClipPlane = 0.03f; testCam.fieldOfView = a.Length > 10 ? F(a[10]) : 70f;
                            testCam.transform.position = at; testCam.transform.rotation = Quaternion.LookRotation(tg - at);
                            shotName = a[8]; shotAt = now + (a.Length > 9 ? F(a[9]) : 3f);
                            break;
                        }
                        case "kyparapaalle": L("essai : " + Wear.TestOsto()); break;
                        case "kypara": L("essai : " + Wear.OstoState()); break;
                        case "vitriini": { Vitriini v = vitriinit.Find(x => x.Name == a[2]); if (v != null) { v.OpenHalf = int.Parse(a[3]); L("essai : vitrine " + a[2] + " moitie " + a[3]); } break; }
                        case "fin": if (testCam != null) { UnityEngine.Object.Destroy(testCam.gameObject); testCam = null; } break;
                        default: KinoEssai(a, now); break;   // (actions du cinema : KinoRakennus.cs)
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
