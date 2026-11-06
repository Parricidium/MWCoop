using System.Collections;
using System.Collections.Generic;
using HutongGames.PlayMaker;
using MWCoop.Net;
using UnityEngine;

namespace MWCoop
{
    // Peinture de la voiture de la quete s21 chez Fleetari (JD 06/11 : « une brochure personnalisee, en plus de l'originale, a cote sur le
    // comptoir », « le meme style », « meme procedure pour le choix »). L'originale n'est pas touchee.
    //  - Bureau : une deuxieme brochure a cote de REPAIRSHOP/LOD/Office/Fleetari/Brochure (meme maillage, couverture « Peinture »).
    //  - Feuille : copie de Sheets/ServiceBrochure sans ses automates, deux pages (couverture, peinture) ; memes cases Regular et Metallic,
    //    memes couleurs d'usine et RGB, memes prix (automates PagePaintCar/Data et Jobs/Paintjob/Work). La logique est ici.
    //  - Commande, comme le jeu : My Summer Car paie a la commande (10 % de remise) ; My Winter Car paie la facture posee a la place de la
    //    brochure une fois les travaux finis (cout + 15). Delai 1400 a 2600 s, compte seulement quand l'acheteur est a plus de 100 m ; puis
    //    Fleetari telephone (Ring::RepairReady). Pret de sa voiture a la commande (« You can take my Ferndale »).
    //  - Differences voulues : voiture absente, la commande est refusee au lieu de payer pour rien ; voiture partie, le travail attend.
    //  - A plusieurs : l'hote tient la commande (QueteGolf : message V_QUETE 20 et 21, etat diffuse, sauvegarde).
    public static class QueteBrochure
    {
        const float PriceRegular = 10150f, PriceMetallic = 18950f, DelayMin = 1400f, DelayMax = 2600f, FarFromShop = 100f, CarNear = 25f;
        const int SheetLayer = 14;
        static readonly Color[] FactoryFallback = {
            new Color(0.161f, 0.647f, 0.765f), new Color(0.765f, 0.518f, 0.137f), new Color(0.447f, 0.408f, 0.090f), new Color(0.620f, 0.678f, 0.694f),
            new Color(0.808f, 0.769f, 0.239f), new Color(0.012f, 0.149f, 0.271f), new Color(0.463f, 0.294f, 0.196f), new Color(0.792f, 0.788f, 0.765f),
            new Color(0.792f, 0.035f, 0.000f), new Color(0.965f, 0.965f, 0.965f), new Color(0.918f, 0.851f, 0.525f), new Color(0.196f, 0.227f, 0.149f) };

        // ---------------------------------------------------------------- commande (hote ; les invites en recoivent l'etat)
        public static bool Ordered { get; private set; }
        public static int BillDue { get; private set; }
        static Color orderColor; static bool orderMetal; static float orderTime, orderSentAt = -100f; static int orderer, orderPrice;

        // ---------------------------------------------------------------- objets
        static GameObject desk, bill, sheet, pageFront, pagePaint, buttons, factoryRoot, customRoot, noMoney, confirm, optionsFsmGo;
        static Collider deskCol, billCol;
        static Camera cam;
        static PlayMakerFSM optionsFsm;
        static TextMesh priceText, finalText, percentText;
        static Renderer factoryIcon, customIcon;
        static readonly Renderer[] barIcon = new Renderer[3];
        static readonly TextMesh[] barValue = new TextMesh[3];
        static GameObject xRegular, xMetallic, xFactory, xCustom;
        static Color[] factory;
        static bool open, hovering, built;
        static int page = 1, factorySel = 1;
        static bool regular, metallic, useCustom;
        static readonly float[] rgb = { 0.5f, 0.5f, 0.5f };
        static float nextBuild, noMoneyUntil, closeAt = -1f;
        static Transform shop;

        public static void OnLevelLoaded()
        {
            desk = bill = sheet = pageFront = pagePaint = buttons = factoryRoot = customRoot = noMoney = confirm = null;
            deskCol = billCol = null; cam = null; optionsFsm = null; shop = null; built = false; open = false; hovering = false; closeAt = -1f; nextBuild = 0f;
            page = 1; regular = metallic = useCustom = false; factorySel = 1; rgb[0] = rgb[1] = rgb[2] = 0.5f;
            testStep = 0; testAt = 0f; phones = null;
        }
        public static void Clear() { Ordered = false; BillDue = 0; orderTime = 0f; }

        // ---------------------------------------------------------------- image par image (QueteGolf.Update, tous les joueurs)
        public static void Update()
        {
            if (!built) { Build(); if (!built) return; }
            if (Host) HostTick();
            bool show = QueteGolf.Done && QueteGolf.On;
            if (desk != null && desk.activeSelf != (show && !Ordered && BillDue == 0)) desk.SetActive(show && !Ordered && BillDue == 0);
            if (bill != null && bill.activeSelf != (show && BillDue > 0)) bill.SetActive(show && BillDue > 0);
            if (open) { SheetTick(); if (Config.Testing) TestTick(); return; }
            Hover();
            if (Config.Testing) TestTick();
        }
        static bool Host { get { return !Session.Active || Session.IsHost; } }

        static void Hover()
        {
            Camera mc = Game.MainCam;
            bool can = mc != null && !Menu.Open && !Menu.ChatOpen && !StandaloneCar.Driving;
            Ray ray = can ? new Ray(mc.transform.position, mc.transform.forward) : default(Ray);
            RaycastHit hit;
            bool onDesk = can && desk != null && desk.activeInHierarchy && deskCol.Raycast(ray, out hit, 1f);
            bool onBill = can && bill != null && bill.activeInHierarchy && billCol.Raycast(ray, out hit, 1f);
            if (onDesk || onBill)
            {
                hovering = true;
                Game.SetGlobalBool("GUIuse", true);
                Game.SetGlobal("GUIinteraction", onDesk ? Lang.TG("BROCHURE PEINTURE", "PAINT BROCHURE") : Lang.TG("FACTURE ", "BILL ") + BillDue + " MK");
                if (Input.GetMouseButtonDown(0)) { if (onDesk) Open(); else PayBill(); }
            }
            else if (hovering) { hovering = false; Game.SetGlobalBool("GUIuse", false); Game.SetGlobal("GUIinteraction", ""); }
        }

        // ---------------------------------------------------------------- construction
        static void Build()
        {
            if (!QueteGolf.On || !QueteGolf.Done || Time.realtimeSinceStartup < nextBuild) return;
            nextBuild = Time.realtimeSinceStartup + 3f;
            GameObject b0 = Game.FindAny("REPAIRSHOP/LOD/Office/Fleetari/Brochure"), s0 = Game.FindAny("Sheets/ServiceBrochure");
            if (b0 == null || s0 == null || s0.activeSelf) return;
            GameObject sh = Game.FindAny("REPAIRSHOP/Order") ?? Game.FindAny("REPAIRSHOP/OrderFleetari");
            GameObject rs = Game.FindAny("REPAIRSHOP");
            if (rs != null) foreach (Transform t in rs.GetComponentsInChildren<Transform>(true)) if (t.name == "DistanceTarget") { shop = t; break; }
            if (shop == null) shop = sh != null ? sh.transform : b0.transform;
            BuildSheet(s0);
            // (a cote de la brochure du jeu, le long du bord du bureau, cote client -- de l'autre cote, la caisse ; meme orientation)
            Vector3 at = b0.transform.position + new Vector3(0.083f, 0.001f, -0.293f) * Config.GetFloat("Test", "BrochureEcart", 1.2f);
            Texture2D cover = RenderCover();
            desk = Paper(b0, "BrochurePeinture", at, cover, out deskCol);
            GameObject b1 = Game.FindAny("REPAIRSHOP/LOD/Office/Fleetari/Bill");
            if (b1 != null) bill = Paper(b1, "FacturePeinture", at, null, out billCol);
            else bill = null;
            foreach (PlayMakerFSM f in Resources.FindObjectsOfTypeAll<PlayMakerFSM>())
                if (f.FsmName == "Open Menu" && f.gameObject.hideFlags == HideFlags.None) { optionsFsm = f; break; }
            built = true;
            Log.Info("brochure peinture : posee en " + at.ToString("F3") + ", garage en " + shop.position.ToString("F1") + " (" + shop.name + ")");
        }

        // Meme maillage, meme taille et meme orientation que l'original, sans son automate.
        static GameObject Paper(GameObject src, string name, Vector3 at, Texture2D tex, out Collider col)
        {
            var g = new GameObject(name);
            g.transform.SetParent(src.transform.parent, false);
            g.transform.localScale = src.transform.localScale;
            g.transform.rotation = src.transform.rotation;
            g.transform.position = at;
            g.layer = src.layer;
            g.AddComponent<MeshFilter>().sharedMesh = src.GetComponent<MeshFilter>().sharedMesh;
            var r = g.AddComponent<MeshRenderer>();
            r.sharedMaterial = src.GetComponent<Renderer>().sharedMaterial;
            if (tex != null) { var m = new Material(r.sharedMaterial); m.mainTexture = tex; r.sharedMaterial = m; }
            var box = g.AddComponent<BoxCollider>(); box.size = new Vector3(1f, 20f, 1f); box.isTrigger = true; col = box;
            g.SetActive(false);
            return g;
        }

        static GameObject Kid(GameObject root, string path) { if (root == null) return null; Transform t = root.transform.Find(path); return t != null ? t.gameObject : null; }
        static TextMesh TextAt(GameObject root, string path) { GameObject g = Kid(root, path); return g != null ? g.GetComponent<TextMesh>() : null; }
        static GameObject Near(GameObject parent, Vector3 local)
        {
            GameObject best = null; float bd = 0.03f;
            foreach (Transform t in parent.transform)
            {
                if (t.name != "x" && !t.name.StartsWith("x ")) continue;
                float d = ((Vector2)(t.localPosition - local)).magnitude;
                if (d < bd) { bd = d; best = t.gameObject; }
            }
            return best;
        }

        static void BuildSheet(GameObject s0)
        {
            // (couleurs d'usine du jeu, sinon celles de My Summer Car)
            factory = (Color[])FactoryFallback.Clone();
            GameObject fi = Kid(s0, "PagePaintCar/Buttons/FactoryColors/ColorIcon");
            PlayMakerFSM cf = fi != null ? Game.FsmOn(fi, "Colors") : null;
            if (cf != null) for (int i = 0; i < 12; i++) { FsmColor c = cf.FsmVariables.FindFsmColor("Color" + (i + 1).ToString("00")); if (c != null && c.Value.a > 0f) factory[i] = c.Value; }

            sheet = Object.Instantiate(s0) as GameObject;   // (original eteint : la copie l'est aussi, ses automates ne demarrent pas)
            sheet.name = "ServiceBrochurePeinture";
            sheet.transform.SetParent(s0.transform.parent, true);
            sheet.transform.position = s0.transform.position; sheet.transform.rotation = s0.transform.rotation;
            foreach (PlayMakerFSM f in sheet.GetComponentsInChildren<PlayMakerFSM>(true)) Object.DestroyImmediate(f);
            var keep = new HashSet<string> { "Cam", "PageFront", "PagePaintCar", "Background", "ButtonPrevious", "ButtonNext", "ButtonOrder", "ButtonMesh", "NoMoney", "Text", "Text 1", "Price", "PriceDiscount", "Discount", "Confimation" };
            var drop = new List<GameObject>();
            foreach (Transform t in sheet.transform) if (!keep.Contains(t.name)) drop.Add(t.gameObject);
            foreach (GameObject g in drop) Object.DestroyImmediate(g);

            cam = Kid(sheet, "Cam").GetComponent<Camera>();
            pageFront = Kid(sheet, "PageFront"); pagePaint = Kid(sheet, "PagePaintCar");
            buttons = Kid(pagePaint, "Buttons"); factoryRoot = Kid(buttons, "FactoryColors"); customRoot = Kid(buttons, "CustomColors");
            noMoney = Kid(sheet, "NoMoney"); confirm = Kid(sheet, "Confimation");
            priceText = TextAt(sheet, "Price"); finalText = TextAt(sheet, "PriceDiscount"); percentText = TextAt(sheet, "Text");
            factoryIcon = Kid(factoryRoot, "ColorIcon").GetComponent<Renderer>(); customIcon = Kid(customRoot, "ColorIcon").GetComponent<Renderer>();
            string[] bars = { "Bar1/ColorRed", "Bar2/ColorGreen", "Bar3/ColorBlue" }, vals = { "Bar1/Value", "Bar2/Value", "Bar3/Value" };
            for (int i = 0; i < 3; i++) { barIcon[i] = Kid(customRoot, bars[i]).GetComponent<Renderer>(); barValue[i] = TextAt(customRoot, vals[i]); }
            xRegular = Near(pagePaint, Kid(pagePaint, "Regular").transform.localPosition);
            xMetallic = Near(pagePaint, Kid(pagePaint, "Metallic").transform.localPosition);
            foreach (Transform t in factoryRoot.transform) if (t.name == "x") xFactory = t.gameObject;
            foreach (Transform t in customRoot.transform) if (t.name == "x") { if (xCustom != null) t.gameObject.SetActive(false); xCustom = t.gameObject; }
            // (peintures qui n'ont pas de sens sur cette voiture : image du client, GT, autocollants)
            var off = new List<GameObject>();
            foreach (string n in new[] { "Art", "GT", "Vinyl" })
            {
                GameObject b = Kid(pagePaint, n); if (b == null) continue;
                GameObject x = Near(pagePaint, b.transform.localPosition); if (x != null) off.Add(x);
                off.Add(b);
            }
            foreach (GameObject g in off) Object.DestroyImmediate(g);
            // (couverture : titre refait sur la bande du titre de l'original ; page : lignes retirees couvertes de papier)
            Transform bg = pageFront.transform.Find("bg");
            Material paper = bg.GetComponent<Renderer>().sharedMaterial;
            Patch(pageFront, paper, new Vector3(-0.200f, -0.0707f, -0.001f), new Vector2(0.385f, 0.0667f), new Rect(0.05f, 0.08f, 0.70f, 0.10f));
            Title(pageFront, Lang.T("PEINTURE", "PAINT"), new Vector3(-0.200f, -0.052f, -0.002f), 0.0046f);
            Title(pageFront, "MAALAUS / MÅLNING", new Vector3(-0.200f, -0.078f, -0.002f), 0.0027f);
            Title(pageFront, "PAINTING BROCHURE", new Vector3(-0.200f, -0.092f, -0.002f), 0.0027f);
            float y0 = Config.GetFloat("Test", "BrochureBandeY", Game.Msc ? -0.145f : -0.135f), h0 = Config.GetFloat("Test", "BrochureBandeH", Game.Msc ? 0.20f : 0.21f);
            Material pm = Kid(pagePaint, "bg").GetComponent<Renderer>().sharedMaterial;
            Rect blank = new Rect(0.03f, 0.865f, 0.94f, 0.04f);   // (bande vide sous l'en-tete)
            Patch(pagePaint, pm, new Vector3(-0.200f, y0, -0.001f), new Vector2(0.385f, h0), blank);
            // (bas de page, a gauche de la fleche)
            float yb = y0 - h0 / 2f;
            Patch(pagePaint, pm, new Vector3(-0.2335f, yb - 0.023f, -0.001f), new Vector2(0.318f, 0.046f), blank);
            SyncPage();
            sheet.SetActive(false);
        }

        // Morceau de papier : la meme image, une zone sans texte.
        static void Patch(GameObject page, Material mat, Vector3 at, Vector2 size, Rect uv)
        {
            var g = new GameObject("papier"); g.layer = SheetLayer;
            g.transform.SetParent(page.transform, false); g.transform.localPosition = at; g.transform.localScale = new Vector3(size.x, size.y, 1f);
            // (a l'echelle de la page : bandes empilees, une sur deux retournee, plutot qu'une seule etiree)
            int rows = Mathf.Max(1, Mathf.RoundToInt(size.y / (uv.height * 0.6f)));
            var vs = new List<Vector3>(); var uvs = new List<Vector2>(); var tris = new List<int>();
            for (int k = 0; k < rows; k++)
            {
                float y1 = -0.5f + (float)k / rows, y2 = -0.5f + (float)(k + 1) / rows;
                float v1 = k % 2 == 0 ? uv.yMin : uv.yMax, v2 = k % 2 == 0 ? uv.yMax : uv.yMin;
                int n = vs.Count;
                vs.Add(new Vector3(-0.5f, y1)); vs.Add(new Vector3(0.5f, y1)); vs.Add(new Vector3(0.5f, y2)); vs.Add(new Vector3(-0.5f, y2));
                uvs.Add(new Vector2(uv.xMin, v1)); uvs.Add(new Vector2(uv.xMax, v1)); uvs.Add(new Vector2(uv.xMax, v2)); uvs.Add(new Vector2(uv.xMin, v2));
                tris.Add(n); tris.Add(n + 2); tris.Add(n + 1); tris.Add(n); tris.Add(n + 3); tris.Add(n + 2);
            }
            var m = new Mesh { vertices = vs.ToArray(), uv = uvs.ToArray(), triangles = tris.ToArray() };
            m.RecalculateNormals();
            g.AddComponent<MeshFilter>().sharedMesh = m;
            g.AddComponent<MeshRenderer>().sharedMaterial = mat;
        }

        static void Title(GameObject page, string text, Vector3 at, float size)
        {
            Font f = Resources.GetBuiltinResource(typeof(Font), "Arial.ttf") as Font;
            var g = new GameObject("titre"); g.layer = SheetLayer;
            g.transform.SetParent(page.transform, false); g.transform.localPosition = at;
            var tm = g.AddComponent<TextMesh>();
            tm.font = f; tm.fontSize = 64; tm.fontStyle = FontStyle.Bold; tm.characterSize = size; tm.anchor = TextAnchor.MiddleCenter; tm.alignment = TextAlignment.Center;
            tm.color = new Color(0.1f, 0.1f, 0.1f); tm.text = text;
            g.GetComponent<Renderer>().sharedMaterial = f.material;
        }

        // Couverture de la brochure du bureau : la page de couverture, photographiee par la camera de la feuille.
        static Texture2D RenderCover()
        {
            if (cam == null) return null;
            RenderTexture rt = RenderTexture.GetTemporary(512, 768, 16);
            RenderTexture prev = RenderTexture.active;
            Vector3 lp = cam.transform.localPosition; float size = cam.orthographicSize; bool wasOn = sheet.activeSelf;
            page = 1; SyncPage();
            sheet.SetActive(true);
            try
            {
                cam.transform.localPosition = lp + new Vector3(-0.2f, 0f, 0f);
                cam.orthographicSize = 0.3f; cam.aspect = 512f / 768f; cam.targetTexture = rt;
                cam.Render();
                RenderTexture.active = rt;
                var t = new Texture2D(512, 768, TextureFormat.RGB24, true);
                t.ReadPixels(new Rect(0, 0, 512, 768), 0, 0); t.Apply();
                return t;
            }
            catch (System.Exception e) { Log.Warn("brochure peinture : couverture " + e.Message); return null; }
            finally
            {
                cam.targetTexture = null; cam.ResetAspect(); cam.orthographicSize = size; cam.transform.localPosition = lp;
                RenderTexture.active = prev; RenderTexture.ReleaseTemporary(rt);
                sheet.SetActive(wasOn);
            }
        }

        // ---------------------------------------------------------------- feuille
        static void Open()
        {
            if (open || sheet == null) return;
            open = true; hovering = false; page = 1;
            Game.SetGlobal("GUIinteraction", ""); Game.SetGlobalBool("GUIuse", false);
            Game.SetGlobalBool("PlayerInMenu", true);
            if (optionsFsm != null) optionsFsm.enabled = false;
            if (noMoney != null) noMoney.SetActive(false);
            if (confirm != null) confirm.SetActive(false);
            sheet.SetActive(true);
            SyncPage(); Refresh();
            Sound("page_turn", 1f);
            Log.Info("brochure peinture : ouverte");
        }
        static void Close()
        {
            if (!open) return;
            open = false; closeAt = -1f;
            sheet.SetActive(false);
            if (optionsFsm != null) optionsFsm.enabled = true;
            Game.SetGlobalBool("PlayerInMenu", false);
            Log.Info("brochure peinture : fermee");
        }
        static void SyncPage()
        {
            if (pageFront != null) pageFront.SetActive(page == 1);
            if (pagePaint != null) pagePaint.SetActive(page == 2);
        }
        static void Sound(string v, float vol)
        {
            Camera mc = Game.MainCam;
            try { MasterAudio.PlaySound3DAtVector3AndForget("GUI", mc != null ? mc.transform.position : Vector3.zero, vol, null, 0f, v); } catch { }
        }

        static void SheetTick()
        {
            if (closeAt > 0f) { if (Time.realtimeSinceStartup >= closeAt) Close(); return; }
            if (Input.GetKeyDown(KeyCode.Escape)) { Close(); return; }
            if (noMoney != null && noMoney.activeSelf && Time.realtimeSinceStartup > noMoneyUntil) noMoney.SetActive(false);
            Ray ray = cam.ScreenPointToRay(Input.mousePosition);
            RaycastHit hit;
            bool on = Physics.Raycast(ray, out hit, 100f, 1 << SheetLayer);
            if (on && Input.GetMouseButtonDown(0)) Click(hit.transform);
            if (on && Input.GetMouseButton(0)) Hold(hit.transform, Time.deltaTime);
            Refresh();
        }

        static void Click(Transform t)
        {
            string n = t.name; string par = t.parent != null ? t.parent.name : "";
            if (n == "ButtonNext" || n == "ButtonPrevious") { page = page == 1 ? 2 : 1; SyncPage(); Sound("page_turn", 1f); }
            else if (n == "Regular") { regular = !regular; if (regular) metallic = false; }
            else if (n == "Metallic") { metallic = !metallic; if (metallic) regular = false; }
            else if (n == "3" && par == "Buttons") useCustom = !useCustom;
            else if (par == "FactoryColors" && (n == "1" || n == "2")) factorySel = Mathf.Clamp(factorySel + (n == "2" ? 1 : -1), 1, factory.Length);
            else if (n == "ButtonOrder") Order();
        }
        static void Hold(Transform t, float dt)
        {
            if (t.parent == null || !t.parent.name.StartsWith("Bar") || (t.name != "1" && t.name != "2")) return;
            int i = t.parent.name == "Bar1" ? 0 : t.parent.name == "Bar2" ? 1 : 2;
            rgb[i] = Mathf.Clamp01(rgb[i] + (t.name == "2" ? 0.35f : -0.35f) * dt);
        }
        static Color Chosen { get { return useCustom ? new Color(rgb[0], rgb[1], rgb[2], 1f) : factory[Mathf.Clamp(factorySel - 1, 0, factory.Length - 1)]; } }
        static float BasePrice { get { return regular ? PriceRegular : metallic ? PriceMetallic : 0f; } }
        static float Discount { get { return Game.Msc ? 0.1f : 0f; } }

        static void Refresh()
        {
            bool colour = regular || metallic;
            if (xRegular != null) xRegular.SetActive(regular);
            if (xMetallic != null) xMetallic.SetActive(metallic);
            if (buttons != null && buttons.activeSelf != colour) buttons.SetActive(colour);
            if (factoryRoot != null && factoryRoot.activeSelf != (colour && !useCustom)) factoryRoot.SetActive(colour && !useCustom);
            if (customRoot != null && customRoot.activeSelf != (colour && useCustom)) customRoot.SetActive(colour && useCustom);
            if (colour)
            {
                if (factoryIcon != null) factoryIcon.material.color = factory[Mathf.Clamp(factorySel - 1, 0, factory.Length - 1)];
                if (customIcon != null) customIcon.material.color = new Color(rgb[0], rgb[1], rgb[2], 1f);
                for (int i = 0; i < 3; i++)
                {
                    if (barIcon[i] != null) barIcon[i].material.color = new Color(i == 0 ? rgb[0] : 0f, i == 1 ? rgb[1] : 0f, i == 2 ? rgb[2] : 0f, 1f);
                    if (barValue[i] != null) barValue[i].text = Mathf.RoundToInt(rgb[i] * 255f).ToString("000");
                }
            }
            if (Game.Msc)
            {
                float b = BasePrice;
                if (percentText != null) percentText.text = Mathf.RoundToInt(Discount * 100f).ToString("00");
                if (priceText != null) priceText.text = b.ToString("F0") + ",-";
                if (finalText != null) finalText.text = (b * (1f - Discount)).ToString("F0") + ",-";
            }
        }

        static bool CarIsNear()
        {
            if (Config.Testing && Config.GetInt("Test", "BrochureProche", 0) != 0) return true;
            Transform c = Vaz.CarOf(QueteGolf.Owner);
            return c != null && shop != null && (c.position - shop.position).magnitude <= CarNear;
        }

        static void Order()
        {
            float b = BasePrice;
            if (b <= 0f) return;
            if (!CarIsNear())
            {
                Hud.Toast(Lang.T("Amenez d'abord la voiture devant le garage.", "Bring the car to the garage first."));
                Log.Info("brochure peinture : voiture absente, commande refusee");
                return;
            }
            int price = Mathf.RoundToInt(b * (1f - Discount));
            if (Game.Msc)
            {
                if (Wallet.Ready && Wallet.Cash < price) { NoMoney(); return; }
                if (Wallet.Ready) Wallet.Spend(price);
            }
            Sound("buy", 0.3f);
            float time = Config.GetFloat("Test", "BrochureDelai", 0f);
            if (time <= 0f) time = Random.Range(DelayMin, DelayMax);
            Color c = Chosen;
            Log.Info("brochure peinture : commande, couleur " + c + ", metallisee " + metallic + ", prix " + price + ", delai " + time.ToString("F0") + " s");
            if (Host) StartOrder(c, metallic, time, Game.Msc ? 0 : price + 15, Session.Active ? Session.LocalId : 0);
            else
            {
                Ordered = true; orderSentAt = Time.realtimeSinceStartup;
                Session.SendToHost(new NetWriter(Msg.Race).U8(Vaz.V_QUETE).U8(20).U8(To8(c.r)).U8(To8(c.g)).U8(To8(c.b)).U8(metallic ? 1 : 0).F32(time).I32(Game.Msc ? 0 : price + 15), true);
            }
            LendCar();
            regular = metallic = false;
            // (My Winter Car : le « OK! » de la feuille, 2 s)
            if (!Game.Msc && confirm != null) { confirm.SetActive(true); closeAt = Time.realtimeSinceStartup + 2f; }
            else Close();
        }
        static int To8(float v) { return Mathf.Clamp(Mathf.RoundToInt(v * 255f), 0, 255); }

        static void FleetariSays(string list)
        {
            try
            {
                GameObject fl = Game.FindAny("REPAIRSHOP/LOD/Office/Fleetari");
                if (fl == null) return;
                foreach (PlayMakerArrayListProxy p in fl.GetComponents<PlayMakerArrayListProxy>())
                {
                    if (p.referenceName != list || p.arrayList == null || p.arrayList.Count == 0) continue;
                    object clip = p.arrayList[Random.Range(0, p.arrayList.Count)];
                    MasterAudio.PlaySound3DAtVector3AndForget("Fleetari", fl.transform.position + Vector3.up * 0.7f, 1f, null, 0f, clip as string ?? (clip != null ? clip.ToString() : null));
                    return;
                }
            }
            catch (System.Exception e) { Log.Warn("brochure peinture : voix de Fleetari " + e.Message); }
        }
        static void NoMoney()
        {
            if (noMoney != null) noMoney.SetActive(true);
            noMoneyUntil = Time.realtimeSinceStartup + 2f;
            Game.SetGlobal("GUIsubtitle", "Why do you even come here if you don't have any money?");
            FleetariSays("Nomoney");
            Log.Info("brochure peinture : pas assez d'argent");
        }
        // (comme le jeu : voiture laissee au garage, Fleetari prete la sienne)
        static void LendCar()
        {
            Game.SetGlobal("GUIsubtitle", Game.Msc ? "You can take my Ferndale if you need to get back home." : "You can take my Bachglotz if you need to get back home.");
            FsmInt k = FsmVariables.GlobalVariables.FindFsmInt("PlayerKeyFerndale");
            if (k != null) k.Value = 1;
            FleetariSays("Carloan");
        }

        // ---------------------------------------------------------------- hote
        static void StartOrder(Color c, bool metal, float time, int billLater, int who)
        {
            Ordered = true; orderColor = c; orderMetal = metal; orderTime = time; orderer = who; orderPrice = billLater;
            QueteGolf.SendState();
        }
        public static void OnOrderMessage(int who, NetReader r)
        {
            var c = new Color(r.U8() / 255f, r.U8() / 255f, r.U8() / 255f, 1f);
            bool metal = r.U8() != 0; float time = r.F32(); int bl = r.I32();
            if (Ordered || BillDue > 0) { Log.Warn("brochure peinture : commande de #" + who + " ignoree, une autre est en cours"); return; }
            Log.Info("brochure peinture : commande de #" + who);
            StartOrder(c, metal, time, bl, who);
        }
        public static void OnBillPaid() { BillDue = 0; QueteGolf.SendState(); }

        static Vector3 PosOf(int id)
        {
            if (Session.Active && id != Session.LocalId)
                foreach (Avatar a in PlayerSync.Avatars) if (a.Player != null && a.Player.Id == id && a.Root != null) return a.Root.transform.position;
            Camera mc = Game.MainCam;
            return mc != null ? mc.transform.position : Vector3.zero;
        }
        static void HostTick()
        {
            if (!Ordered || shop == null) return;
            bool far = Config.GetInt("Test", "BrochureLoin", 0) != 0 || (PosOf(orderer) - shop.position).magnitude > FarFromShop;
            if (far && orderTime > 0f) orderTime -= Time.deltaTime;
            if (orderTime >= 1f || !CarIsNear()) return;
            Log.Info("brochure peinture : travaux finis");
            Ordered = false; BillDue = orderPrice;
            QueteGolf.ApplyPaint(orderColor, orderMetal);
            Ring();
            QueteGolf.SendState();
        }

        // Etat de l'hote, chez les invites : fin des travaux = Fleetari telephone ici aussi.
        public static void OnState(bool ordered, int bl)
        {
            // (etat parti avant que l'hote recoive la commande d'ici : pas une fin de travaux)
            if (Ordered && !ordered && Time.realtimeSinceStartup - orderSentAt < 5f) return;
            if (Ordered && !ordered) Ring();
            Ordered = ordered; BillDue = bl;
        }

        static List<PlayMakerFSM> phones;
        static void Ring()
        {
            if (phones == null)
            {
                phones = new List<PlayMakerFSM>();
                foreach (PlayMakerFSM f in Resources.FindObjectsOfTypeAll<PlayMakerFSM>())
                    if (f.FsmName == "Ring" && f.gameObject.hideFlags == HideFlags.None && (f.gameObject.name == "PhoneLogic" || f.gameObject.name == "PhoneLogicNEW" || f.gameObject.name == "PhoneLogicOLD")) phones.Add(f);
            }
            foreach (PlayMakerFSM f in phones)
            {
                FsmBool b = f.FsmVariables.FindFsmBool("RepairReady");
                if (b != null) b.Value = true;
            }
            Log.Info("brochure peinture : Fleetari va telephoner (" + phones.Count + " telephones)");
        }

        static void PayBill()
        {
            if (BillDue <= 0) return;
            if (Wallet.Ready && Wallet.Cash < BillDue) { Game.SetGlobal("GUIsubtitle", "Why do you even come here if you don't have any money?"); FleetariSays("Nomoney"); return; }
            if (Wallet.Ready) Wallet.Spend(BillDue);
            Sound("buy", 0.3f);
            Log.Info("brochure peinture : facture payee, " + BillDue + " mk");
            if (Host) OnBillPaid();
            else { BillDue = 0; Session.SendToHost(new NetWriter(Msg.Race).U8(Vaz.V_QUETE).U8(21), true); }
        }

        // ---------------------------------------------------------------- sauvegarde (MWCoop-s21.txt de l'hote)
        public static string SaveLines()
        {
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            string s = "";
            if (Ordered) s += "\ncommande " + orderColor.r.ToString("F3", ci) + " " + orderColor.g.ToString("F3", ci) + " " + orderColor.b.ToString("F3", ci) + " " + (orderMetal ? 1 : 0) + " " + orderTime.ToString("F0", ci) + " " + orderPrice;
            if (BillDue > 0) s += "\nfacture " + BillDue;
            return s;
        }
        public static void LoadLine(string[] p)
        {
            var ci = System.Globalization.CultureInfo.InvariantCulture; const System.Globalization.NumberStyles st = System.Globalization.NumberStyles.Float;
            float r, g, b, t; int m, bl;
            if (p[0] == "commande" && p.Length > 6 && float.TryParse(p[1], st, ci, out r) && float.TryParse(p[2], st, ci, out g) && float.TryParse(p[3], st, ci, out b)
                && int.TryParse(p[4], out m) && float.TryParse(p[5], st, ci, out t) && int.TryParse(p[6], out bl))
            { Ordered = true; orderColor = new Color(r, g, b, 1f); orderMetal = m != 0; orderTime = t; orderPrice = bl; orderer = Session.Active ? Session.LocalId : 0; }
            else if (p[0] == "facture" && p.Length > 1 && int.TryParse(p[1], out bl)) BillDue = bl;
        }

        // ---------------------------------------------------------------- essai ([Test] BrochureEssai=1, le joueur qui commande)
        //  BrochureLoin=1 : acheteur compte loin ; BrochureProche=1 : voiture comptee presente ; BrochureDelai=s : delai court.
        //  Les clics passent par le vrai rayon de la camera, vise sur le centre du bouton.
        static int testStep; static float testAt; static bool testMoved;
        static void ClickAt(string path)
        {
            GameObject b = Kid(sheet, path);
            if (b == null) { Log.Warn("brochure essai : " + path + " absent"); return; }
            RaycastHit hit;
            Ray ray = cam.ScreenPointToRay(cam.WorldToScreenPoint(b.transform.position));
            if (Physics.Raycast(ray, out hit, 100f, 1 << SheetLayer)) { Log.Info("brochure essai : clic sur " + path + " -> " + hit.transform.name); Click(hit.transform); }
            else Log.Warn("brochure essai : clic sur " + path + " : rien sous le rayon");
        }
        static void TestTick()
        {
            if (Config.GetInt("Test", "BrochureEssai", 0) == 0 || testStep < 0) return;
            float now = Time.realtimeSinceStartup;
            if (testStep == 0)
            {
                if (desk != null && !testMoved && Time.timeSinceLevelLoad > Config.GetFloat("Test", "BrochureDepart", 45f) - 12f && Config.GetInt("Test", "BrochureIci", 0) != 0)
                { testMoved = true; Autotest.PutPlayer(desk.transform.position + new Vector3(1.7f, 0.2f, -2.1f)); Log.Info("brochure essai : joueur au bureau"); }
                if (desk == null || !desk.activeSelf || Time.timeSinceLevelLoad < Config.GetFloat("Test", "BrochureDepart", 45f)) return;
                testAt = now; testStep = 1;
                Vector3 p = desk.transform.position;
                QueteGolf.Shot("brochure-bureau-dessus", p + Vector3.up * 0.9f + new Vector3(0.25f, 0f, -0.1f), p);
                QueteGolf.Shot("brochure-bureau-cote", p + new Vector3(-1.1f, 0.55f, -0.5f), p);
                RaycastHit vise;
                Log.Info("brochure essai : le rayon touche la brochure " + deskCol.Raycast(new Ray(p + Vector3.up * 0.4f, Vector3.down), out vise, 1f));
                Log.Info("brochure essai : argent " + Wallet.Cash + ", voiture presente " + CarIsNear() + ", bureau visible " + desk.activeInHierarchy);
                Open();
                Autotest.CaptureSoon("brochure-couverture", 0.8f);
            }
            float d = now - testAt;
            if (testStep == 1 && d > 2f) { testStep = 2; ClickAt("ButtonNext"); Autotest.CaptureSoon("brochure-page", 0.8f); }
            if (testStep == 2 && d > 4f) { testStep = 3; ClickAt("PagePaintCar/Regular"); }
            if (testStep == 3 && d > 5f) { testStep = 4; ClickAt("PagePaintCar/Buttons/FactoryColors/2"); ClickAt("PagePaintCar/Buttons/FactoryColors/2"); Autotest.CaptureSoon("brochure-usine", 0.8f); Log.Info("brochure essai : couleur d'usine " + Chosen); }
            if (testStep == 4 && d > 7f) { testStep = 5; ClickAt("PagePaintCar/Buttons/3"); }
            if (testStep == 5 && d > 8f && d < 9f) { GameObject b = Kid(sheet, "PagePaintCar/Buttons/CustomColors/Bar1/2"); if (b != null) Hold(b.transform, Time.deltaTime); }
            if (testStep == 5 && d > 9f) { testStep = 6; Autotest.CaptureSoon("brochure-perso", 0.8f); Log.Info("brochure essai : couleur perso " + Chosen); }
            if (testStep == 6 && d > 11f) { testStep = 7; ClickAt("PagePaintCar/Metallic"); }
            if (testStep == 7 && d > 12f) { testStep = 8; Autotest.CaptureSoon("brochure-metal", 0.5f); }
            // (sans argent, puis avec : [Test] BrochureArgent=mk)
            if (testStep == 8 && d > 14f && d < 16f) { testStep = 80; Wallet.SetLocal(100); ClickAt("ButtonOrder"); Autotest.CaptureSoon("brochure-sans-argent", 0.5f); Log.Info("brochure essai : sans argent, commandee " + Ordered + ", ouverte " + open); }
            if (testStep == 80 && d > 17f) { testStep = 8; int am = Config.GetInt("Test", "BrochureArgent", 30000); Wallet.SetLocal(am); }
            if (testStep == 8 && d > 17.5f) { testStep = 9; float a = Wallet.Cash; ClickAt("ButtonOrder"); Log.Info("brochure essai : commande, argent " + a + " -> " + Wallet.Cash + ", commandee " + Ordered + ", ouverte " + open); }
            if (testStep == 9 && !Ordered && d > 19f)
            {
                testStep = 10; testAt = now;
                Transform c = Vaz.CarOf(QueteGolf.Owner);
                if (c != null) QueteGolf.Shot("brochure-voiture", c.position + c.right * 5f + Vector3.up * 1.5f, c.position);
                Log.Info("brochure essai : travaux vus, couleur " + QueteGolf.Paint + ", metallisee " + QueteGolf.Metal + ", facture " + BillDue);
            }
            if (testStep == 10 && now - testAt > 2f)
            {
                float a = Wallet.Cash;
                if (BillDue > 0) PayBill();
                testStep = -1;
                Log.Info("brochure essai : fini, argent " + a + " -> " + Wallet.Cash + ", facture " + BillDue + ", brochure " + (desk != null && desk.activeSelf));
            }
        }
    }
}
