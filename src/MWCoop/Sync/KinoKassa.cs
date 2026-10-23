using System;
using System.Collections.Generic;
using HutongGames.PlayMaker;
using UnityEngine;

namespace MWCoop
{
    // Cinema KINO TAHTI : le comptoir du hall (billets et confiserie) et son vendeur (corps de PNJ, comme a l'Osuuskauppa).
    //  - billet (LIPPU, 35 mk) : ouvre les portes des deux salles jusqu'a la fin du jour du jeu (KinoSali) ; a chacun le sien.
    //  - popcorn 12 mk, chips 8 mk, limonade 9 mk, bonbons 6 mk : clic sur l'article du comptoir, paye tout de suite
    //    (l'argent du joueur, comme a la boutique), l'objet est pose sur le comptoir devant soi (KinoEsineet).
    public static partial class Kauppa
    {
        static Avatar kinoVendor;
        static readonly Vector3 kinoVendorSpot = new Vector3(0f, 0f, -6.6f);
        const float KCounterTop = 1.05f, KCounterFront = -4.8f;

        class KinoOffer { public string Item, Fr, En; public int Price; public Collider Col; public float X; }
        static readonly List<KinoOffer> kinoOffers = new List<KinoOffer>();
        static object kinoLooked;
        static int ticketDay = -1;
        static float kinoLastBuy, kinoNoMoneyUntil, kinoLastHello = -100f;
        static bool kinoInside;

        public const int TicketPrice = 35;

        static void ResetKinoKassa() { kinoVendor = null; kinoOffers.Clear(); kinoLooked = null; kinoInside = false; foodHint = false; heldFood = null; emptied.Clear(); }

        static int GameDay { get { FsmInt d = FsmVariables.GlobalVariables.FindFsmInt("GlobalDay"); return d != null ? d.Value : 0; } }
        static bool HasTicket { get { return ticketDay >= 0 && ticketDay == GameDay; } }

        static void KinoGui()
        {
            if (guiText != null) return;
            var gv = FsmVariables.GlobalVariables;
            guiUse = gv.FindFsmBool("GUIuse"); guiBuy = gv.FindFsmBool("GUIbuy"); guiText = gv.FindFsmString("GUIinteraction");
            money = gv.FindFsmFloat("PlayerMoney"); guiSub = gv.FindFsmString("GUIsubtitle");
        }
        static void KinoClearGui() { KinoGui(); if (guiUse != null) guiUse.Value = false; if (guiBuy != null) guiBuy.Value = false; if (guiText != null) guiText.Value = ""; }

        static void KinoVendor()
        {
            kinoVendor = Avatar.CreatePreview("KINO-myyja", 0);
            if (kinoVendor == null) { L("cinema : vendeur indisponible (corps de PNJ)"); }
            else
            {
                Transform r = kinoVendor.Root.transform;
                r.SetParent(kino.transform, false);
                r.localPosition = kinoVendorSpot;
                r.localRotation = Quaternion.identity;   // (face au hall, +z)
                var cg = new GameObject("kino_myyja_coll");
                cg.transform.SetParent(kino.transform, false);
                cg.transform.localPosition = kinoVendorSpot + new Vector3(0f, 0.9f, 0f);
                var cc = cg.AddComponent<CapsuleCollider>(); cc.height = 1.8f; cc.radius = 0.28f;
                L("cinema : vendeur en place");
            }
            try { KinoCounter(); } catch (Exception e) { L("cinema : comptoir " + e); }
        }

        // ---------------------------------------------------------------- articles sur le comptoir
        static void KinoCounter()
        {
            parts.Clear(); cols.Clear();
            var keep = root; root = kino;
            float y = KCounterTop;
            // billets : petit guichet (plexi, fente), distributeur de billets, panneau LIPUT
            Box(new Vector3(-4.3f, y, -5.5f), new Vector3(-3.0f, y + 0.02f, -4.9f), "ki_musta", false);
            Box(new Vector3(-4.0f, y, -5.35f), new Vector3(-3.6f, y + 0.18f, -5.1f), "ki_punainen", false);
            GlassX(-4.3f, -3.0f, y + 0.25f, y + 0.85f, -5.15f);
            Box(new Vector3(-4.32f, y, -5.18f), new Vector3(-4.28f, y + 0.9f, -5.12f), "ki_alu", false);
            Box(new Vector3(-3.02f, y, -5.18f), new Vector3(-2.98f, y + 0.9f, -5.12f), "ki_alu", false);
            Box(new Vector3(-4.32f, y + 0.85f, -5.18f), new Vector3(-2.98f, y + 1.1f, -5.12f), "ki_punainen", false);
            KinoSign("LIPUT · TICKETS", new Vector3(-3.65f, y + 0.975f, -5.11f), 0.045f, Color.white);
            AddKinoOffer("lippu", TicketPrice, "BILLET", "TICKET", new Vector3(-3.65f, y + 0.4f, -4.95f), new Vector3(1.3f, 0.8f, 0.5f));
            // machine a popcorn : vitrine sur pieds, chapeau rouge, tas de popcorn eclaire
            float px = -2.0f;
            Box(new Vector3(px - 0.35f, y, -5.5f), new Vector3(px + 0.35f, y + 0.12f, -4.95f), "ki_punainen", false);
            Box(new Vector3(px - 0.35f, y + 0.82f, -5.5f), new Vector3(px + 0.35f, y + 0.98f, -4.95f), "ki_punainen", false);
            foreach (float cx in new[] { px - 0.34f, px + 0.32f }) foreach (float cz in new[] { -5.49f, -4.97f })
                Box(new Vector3(cx, y + 0.12f, cz), new Vector3(cx + 0.02f, y + 0.82f, cz + 0.02f), "ki_kulta", false);
            GlassX(px - 0.33f, px + 0.33f, y + 0.12f, y + 0.82f, -4.96f);
            Box(new Vector3(px - 0.3f, y + 0.12f, -5.45f), new Vector3(px + 0.3f, y + 0.3f, -5.0f), "ki_popcorn", false);
            KinoSign("POPCORN", new Vector3(px, y + 0.9f, -4.94f), 0.04f, new Color(1f, 0.85f, 0.3f));
            AddKinoOffer(KPopcorn, 12, "POPCORN", "POPCORN", new Vector3(px, y + 0.45f, -4.95f), new Vector3(0.8f, 0.9f, 0.5f));
            // bocaux de bonbons
            float kx = -0.6f;
            AddKinoOffer(KKarkit, 6, "BONBONS", "CANDY", new Vector3(kx, y + 0.18f, -5.05f), new Vector3(0.8f, 0.4f, 0.4f));
            // limonade : fontaine a trois robinets, gobelets empiles
            float lx = 1.0f;
            Box(new Vector3(lx - 0.35f, y, -5.5f), new Vector3(lx + 0.35f, y + 0.62f, -5.1f), "ki_valkoinen", false);
            Box(new Vector3(lx - 0.33f, y + 0.4f, -5.1f), new Vector3(lx + 0.33f, y + 0.6f, -5.08f), "ki_sininen", false);
            foreach (float tx in new[] { lx - 0.2f, lx, lx + 0.2f }) Box(new Vector3(tx - 0.03f, y + 0.28f, -5.1f), new Vector3(tx + 0.03f, y + 0.36f, -5.0f), "ki_alu", false);
            KinoSign("LIMSA", new Vector3(lx, y + 0.5f, -5.07f), 0.035f, Color.white);
            AddKinoOffer(KLimsa, 9, "LIMONADE", "SODA", new Vector3(lx, y + 0.3f, -5.05f), new Vector3(0.8f, 0.65f, 0.5f));
            // presentoir de chips
            float sx = 3.0f;
            Box(new Vector3(sx - 0.4f, y, -5.45f), new Vector3(sx + 0.4f, y + 0.03f, -5.0f), "ki_alu", false);
            Box(new Vector3(sx - 0.4f, y, -5.47f), new Vector3(sx + 0.4f, y + 0.55f, -5.43f), "ki_alu", false);
            AddKinoOffer(KSipsit, 8, "CHIPS", "CHIPS", new Vector3(sx, y + 0.25f, -5.1f), new Vector3(0.9f, 0.6f, 0.5f));
            // bobines de film (JD 23/10 : vendues au cinema, plus a l'Osuuskauppa) : boites debout entre limonade et chips
            float rx = 2.05f;
            AddKinoOffer(KKela, KelaPrice, "BOBINE DE FILM", "FILM REEL", new Vector3(rx, y + 0.2f, -5.15f), new Vector3(0.55f, 0.42f, 0.5f));
            Flush();
            root = keep;
            // articles exposes (modeles des objets, sans physique)
            ShowKinoItem(KKarkit, new Vector3(kx - 0.25f, y, -5.15f), 15f);
            ShowKinoItem(KKarkit, new Vector3(kx, y, -5.2f), -10f);
            ShowKinoItem(KKarkit, new Vector3(kx + 0.25f, y, -5.15f), 5f);
            ShowKinoItem(KLimsa, new Vector3(lx - 0.45f, y, -5.0f), 0f);
            ShowKinoItem(KLimsa, new Vector3(lx + 0.45f, y, -5.0f), 0f);
            for (int i = 0; i < 3; i++) ShowKinoItem(KSipsit, new Vector3(sx - 0.25f + i * 0.25f, y + 0.03f, -5.3f), 0f, -12f);
            ShowKinoItem(KPopcorn, new Vector3(px - 0.15f, y + 0.3f, -5.2f), 0f);
            ShowKinoItem(KPopcorn, new Vector3(px + 0.15f, y + 0.3f, -5.25f), 30f);
            ShowKinoItem(KKela, new Vector3(rx - 0.11f, y, -5.3f), 0f, 90f);
            ShowKinoItem(KKela, new Vector3(rx + 0.11f, y, -5.3f), 0f, 90f);
            L("cinema : comptoir garni (" + kinoOffers.Count + " articles)");
        }

        static void AddKinoOffer(string item, int price, string fr, string en, Vector3 c, Vector3 s)
        {
            var g = new GameObject("kino_osto_" + item);
            g.layer = 2;   // (Ignore Raycast : ne gene pas la main du jeu)
            g.transform.SetParent(kino.transform, false);
            g.transform.localPosition = c;
            var bc = g.AddComponent<BoxCollider>(); bc.isTrigger = true; bc.size = s;
            kinoOffers.Add(new KinoOffer { Item = item, Price = price, Fr = fr, En = en, Col = bc, X = c.x });
        }

        static void ShowKinoItem(string kind, Vector3 at, float yaw, float tilt = 0f)
        {
            Vector3 size; Transform scr;
            GameObject g = Model(kind, out size, out scr);
            g.transform.SetParent(kino.transform, false);
            g.transform.localRotation = Quaternion.Euler(tilt, yaw, 0f);
            Bounds b = LocalBounds(g);
            g.transform.localPosition = new Vector3(at.x - b.center.x, at.y - b.min.y + 0.002f, at.z - b.center.z);
        }

        // petit texte 3D (TextMesh, Arial du jeu) tourne vers +z
        static void KinoSign(string text, Vector3 at, float size, Color c)
        {
            if (arial == null) arial = Resources.GetBuiltinResource<Font>("Arial.ttf");
            if (arial == null) return;
            var g = new GameObject("kino_teksti");
            g.transform.SetParent(kino.transform, false);
            g.transform.localPosition = at;
            g.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            var tm = g.AddComponent<TextMesh>();
            tm.font = arial; tm.fontSize = 64; tm.characterSize = size / 6f; tm.anchor = TextAnchor.MiddleCenter; tm.alignment = TextAlignment.Center;
            tm.fontStyle = FontStyle.Bold; tm.color = c; tm.text = text;
            // (pas le materiau d'Arial, GUI/Text Shader : il se dessine a travers tout -- transparent ordinaire sur son atlas)
            Shader sh = Shader.Find("Legacy Shaders/Transparent/Diffuse") ?? Shader.Find("Transparent/Diffuse") ?? arial.material.shader;
            var m = new Material(sh) { name = "kino_teksti", color = c, mainTexture = arial.material.mainTexture };
            g.GetComponent<MeshRenderer>().sharedMaterial = m;
            kinoTexts.Add(m);
        }
        static readonly List<Material> kinoTexts = new List<Material>();

        static void TickKinoTexts()
        {
            if (arial == null) return;
            foreach (Material m in kinoTexts) if (m != null && m.mainTexture != arial.material.mainTexture) m.mainTexture = arial.material.mainTexture;   // (atlas refait par la police)
        }

        // ---------------------------------------------------------------- achats, accueil
        static void TickKinoKassa(float now)
        {
            KinoGui();
            TickFood();
            TickKinoTexts();
            Transform pl = Game.PlayerT;
            if (pl == null) return;
            // accueil en entrant dans le hall
            Vector3 lp = kino.transform.InverseTransformPoint(pl.position);
            bool inNow = Mathf.Abs(lp.x) < KPart - 0.2f && lp.z > -KD && lp.z < KD - 0.2f && lp.y > -0.6f && lp.y < KLobCeil;
            if (inNow && !kinoInside && now - kinoLastHello > 40f && kinoVendor != null)
            {
                kinoLastHello = now;
                Say("Myyjä : Tervetuloa Kino Tähteen ! Billets et confiseries au comptoir.", "Vendor: Tervetuloa to Kino Tähti! Tickets and snacks at the counter.");
            }
            kinoInside = inNow;
        }

        // visee et clic (appele par KinoSali avec la visee des portes et des projecteurs) : vrai si un article est vise
        static bool KinoBuyAim(Ray ray, ref float best, out KinoOffer hit)
        {
            hit = null;
            RaycastHit h;
            foreach (KinoOffer o in kinoOffers) if (o.Col != null && o.Col.Raycast(ray, out h, best)) { best = h.distance; hit = o; }
            return hit != null;
        }

        static void KinoOfferGui(KinoOffer o, float now)
        {
            guiBuy.Value = true;
            if (now < kinoNoMoneyUntil) guiText.Value = "NOT ENOUGH MONEY";
            else if (o.Item == "lippu" && HasTicket) guiText.Value = Lang.TG("BILLET DÉJÀ ACHETÉ (VALABLE AUJOURD'HUI)", "TICKET ALREADY BOUGHT (VALID TODAY)");
            else guiText.Value = Lang.TG(o.Fr, o.En) + " " + o.Price.ToString("F2", System.Globalization.CultureInfo.InvariantCulture) + " MK";
            if (Input.GetMouseButtonDown(0)) KinoBuy(o, now);
        }

        static void KinoBuy(KinoOffer o, float now)
        {
            if (now - kinoLastBuy < 0.6f) return;
            kinoLastBuy = now;
            if (o.Item == "lippu" && HasTicket) { Say("Myyjä : ton billet est encore bon pour aujourd'hui.", "Vendor: your ticket is still good for today."); return; }
            if (money == null || money.Value < o.Price)
            {
                kinoNoMoneyUntil = now + 2f;
                Say("Myyjä : il te manque de l'argent.", "Vendor: you're a bit short on money.");
                return;
            }
            money.Value -= o.Price;   // (Wallet : une depense reste au joueur)
            try { MasterAudio.PlaySound3DAtVector3AndForget("Store", kino.transform.TransformPoint(new Vector3(o.X, KCounterTop, -5.2f)), 1f, null, 0f, "cash_register_2"); } catch { }
            if (o.Item == "lippu")
            {
                ticketDay = GameDay;
                Say("Myyjä : Kiitos ! Ton billet vaut pour les deux salles, toute la journée. Bon film !", "Vendor: Kiitos! Your ticket is good for both screens all day. Enjoy the film!");
                Hud.Toast(Lang.T("Billet de cinéma : salles 1 et 2, aujourd'hui", "Cinema ticket: screens 1 and 2, today"));
                L("cinema : billet achete (jour " + ticketDay + "), reste " + money.Value.ToString("F0") + " mk");
                return;
            }
            // l'article, pose sur le comptoir devant le joueur
            Transform pl = Game.PlayerT;
            float x = o.X;
            if (pl != null) x = Mathf.Clamp(kino.transform.InverseTransformPoint(pl.position).x, o.X - 0.4f, o.X + 0.4f);
            Vector3 at = kino.transform.TransformPoint(new Vector3(x, KCounterTop + 0.16f, KCounterFront - 0.15f));
            SpawnItem(o.Item, o.Item == KKela ? "tyhja" : Portions(o.Item).ToString(), at, kino.transform.rotation * Quaternion.Euler(0f, 180f, 0f));
            Say("Myyjä : Ole hyvä !", "Vendor: Ole hyvä! Here you go.");
            L("cinema : achat " + o.Item + " " + o.Price + " mk, reste " + money.Value.ToString("F0") + " mk");
        }
    }
}
