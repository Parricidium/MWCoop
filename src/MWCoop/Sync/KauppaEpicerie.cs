using System;
using System.Collections.Generic;
using UnityEngine;

namespace MWCoop
{
    // Epicerie de village (JD 23/10, plan : l'OSUUSKAUPPA s21 refaite plus petite et plus chaleureuse, « que dans un rayon
    // seule une personne puisse circuler ») : batiment 4,5 x 5,2 m (KauppaRakennus), comptoir en bois pres de la facade
    // (vendeur dos a la vitrine, face aux rayons), allees de 80 cm. A gauche, deux vitrines vitrees a portes coulissantes
    // (« que l'on peut ouvrir et choisir l'objet a l'interieur ») : coin video (TV, magnetoscopes, camescopes, cassettes)
    // et consoles (enseigne lumineuse au nom generique PELIKONSOLIT, TV de demonstration, boites de jeux inventees -- les
    // consoles arrivent ensuite) ; a droite, etageres en bois : alcool (biere, eau-de-vie) et rallye (GPS KARTTURI, casque) ;
    // au milieu, gondoles en bois : snacks du cinema, sacs de couchage. Les bobines de film sont vendues au cinema.
    // Repere de la boutique : interieur x -2,05..2,05, z -2,4..2,4 (facade et porte en +z).
    public static partial class Kauppa
    {
        public const string KOlut = "olut", KViina = "viina", KKypara = "kypara";
        const int OlutPrice = 14, ViinaPrice = 89, KyparaPrice = 290;
        const float XIn = 2.05f, ZIn = 2.4f;   // (faces interieures des murs)

        // ---------------------------------------------------------------- meubles
        static void Furnish()
        {
            Vector3 bottom = new Vector3(0, -1, 0), top = new Vector3(0, 1, 0);
            // comptoir en bois : bar le long de la facade (cote client vers -z) et retour jusqu'a la facade a gauche
            Box(new Vector3(-1.6f, 0f, 1.1f), new Vector3(0.45f, CounterTop, 1.65f), "tiski");
            Box(new Vector3(-1.65f, CounterTop, 1.05f), new Vector3(0.5f, CounterTop + 0.04f, 1.7f), "taso", false);
            Box(new Vector3(-XIn, 0f, 1.1f), new Vector3(-1.6f, CounterTop, ZIn), "tiski");
            Box(new Vector3(-XIn, CounterTop, 1.05f), new Vector3(-1.55f, CounterTop + 0.04f, ZIn), "taso", false);
            Box(new Vector3(-1.58f, 0.05f, 1.06f), new Vector3(0.43f, 0.12f, 1.1f), "lista", false);   // (plinthe cote client)
            counterBounds = new Bounds(new Vector3(-0.575f, CounterTop / 2f, 1.375f), new Vector3(2.05f, CounterTop, 0.55f));
            vendorSpot = new Vector3(-0.6f, 0f, 2.0f);
            // caisse enregistreuse de la PSK sur le comptoir, tiroir et caissier cote vendeur (+z), ecran vers le client
            GameObject regSrc = FindPath("PERAPORTTI/ActiveFunctions/Store/Cashier/StoreCashRegister"), cashier = FindPath("PERAPORTTI/Building/LOD100/Staff/Cashier1PIVOT");
            GameObject reg = regSrc != null ? Clone("PERAPORTTI/ActiveFunctions/Store/Cashier/StoreCashRegister", 0, Vector3.zero, 0, bottom, false) : null;
            if (reg != null)
            {
                Vector3 w = cashier != null ? cashier.transform.position - regSrc.transform.position : -regSrc.transform.forward; w.y = 0f;
                Vector3 want = root.transform.forward;
                reg.transform.rotation = Quaternion.FromToRotation(w.sqrMagnitude > 1e-6f ? w.normalized : Vector3.forward, want) * regSrc.transform.rotation;
                Bounds rb = LocalBounds(reg);
                Vector3 at = new Vector3(-0.95f, CounterTop + 0.04f, 1.42f);
                reg.transform.localPosition += new Vector3(at.x - rb.center.x, at.y - rb.min.y, at.z - rb.center.z);
                RegisterScreen(reg, regSrc);
                L("caisse posee en " + root.transform.InverseTransformPoint(reg.transform.position).ToString("F2"));
            }
            else Clone("INSPECTION/LOD/inspection_desk/CashRegister/InspectionCashRegister", 0, new Vector3(-0.95f, CounterTop + 0.04f, 1.42f), 0, bottom, false);
            // vitrines (mur gauche) et etageres (mur droit)
            float[] lv = { 0.6f, 1.02f, 1.44f };
            videoCab = Vitrine("video", -XIn, 1f, -0.65f, 0.95f, 2.0f, lv, new Color(1f, 0.9f, 0.75f), null);
            konsoliCab = Vitrine("konsolit", -XIn, 1f, -2.35f, -0.75f, 2.0f, lv, new Color(0.85f, 0.92f, 1f), "kkyltti");
            alcoholTops = Shelf(-0.65f, 0.95f, XIn - 0.4f, XIn, false, 1.9f, 4);
            rallyTops = Shelf(-2.35f, -0.75f, XIn - 0.4f, XIn, false, 1.9f, 4);
            // gondoles en bois au milieu
            WoodGondola(-0.75f, 0.85f, -0.3f, 0.3f, 1.3f, 3);
            WoodGondola(-0.75f, 0.85f, -1.6f, -1.05f, 1.0f, 2);
            // lampes (abat-jour) et deco
            foreach (Vector3 p in new[] { new Vector3(-1.0f, Ceil, 1.6f), new Vector3(-1.1f, Ceil, -1.2f), new Vector3(1.1f, Ceil, -0.6f) })
                Clone("YARD/Building/BEDROOM1/Lamp Paper/lamp_paper", 0, p, 0, top, false, false);
            Clone("HOMENEW/Functions/ElectricThings/RadiatorsApartment/Radiator3", 0, new Vector3(0f, 0.12f, -ZIn - 0.02f + 0.02f), 180, new Vector3(0, -1, -1), false);
            Clone("HOMENEW/Functions/Clock", 0, new Vector3(0f, 2.55f, -ZIn + 0.01f), -90, new Vector3(0, 0, -1), false);
            // dehors : table et chaise de jardin a gauche de la facade
            Clone("COTTAGE/LOD/garden_table 1", 0, new Vector3(-1.3f, float.NaN, D2 + 1.1f), 0, bottom);
            Clone("COTTAGE/LOD/chair1x", 0, new Vector3(-2.0f, float.NaN, D2 + 1.1f), 0, bottom);
        }

        static List<float> alcoholTops, rallyTops;

        // gondole en bois (deux faces) : socle, panneau central, joues, tablettes et porte-etiquettes ; le long de x
        static List<float> WoodGondola(float a0, float a1, float b0, float b1, float h, int levels)
        {
            float mid = (b0 + b1) / 2, t = 0.03f;
            var tops = new List<float>();
            Box(new Vector3(a0, 0, b0 + 0.04f), new Vector3(a1, 0.14f, b1 - 0.04f), "tiski", false);
            Box(new Vector3(a0 + t, 0.14f, mid - 0.02f), new Vector3(a1 - t, h, mid + 0.02f), "paneeli", false);
            Box(new Vector3(a0, 0, b0), new Vector3(a0 + t, h, b1), "tiski", false);
            Box(new Vector3(a1 - t, 0, b0), new Vector3(a1, h, b1), "tiski", false);
            Box(new Vector3(a0, h, b0), new Vector3(a1, h + 0.03f, b1), "taso", false);
            for (int k = 0; k < levels; k++)
            {
                float y = 0.14f + (k + 1) * (h - 0.2f) / (levels + 0.3f);
                Box(new Vector3(a0 + t, y, b0 + 0.02f), new Vector3(a1 - t, y + 0.025f, mid - 0.02f), "taso", false);
                Box(new Vector3(a0 + t, y, mid + 0.02f), new Vector3(a1 - t, y + 0.025f, b1 - 0.02f), "taso", false);
                Box(new Vector3(a0 + t, y - 0.03f, b0 + 0.02f), new Vector3(a1 - t, y + 0.035f, b0 + 0.035f), "tiski", false);
                Box(new Vector3(a0 + t, y - 0.03f, b1 - 0.035f), new Vector3(a1 - t, y + 0.035f, b1 - 0.02f), "tiski", false);
                tops.Add(y + 0.025f);
            }
            cols.Add(new Bounds(new Vector3((a0 + a1) / 2, h / 2, mid), new Vector3(a1 - a0, h, b1 - b0)));
            return tops;
        }
        static float GondolaTop(float h, int levels, int k) { return 0.14f + (k + 1) * (h - 0.2f) / (levels + 0.3f) + 0.025f; }

        // ---------------------------------------------------------------- vitrines a portes coulissantes
        class Vitriini
        {
            public string Name; public int OpenHalf = -1; public float Mid;
            public readonly List<Lasi> Panes = new List<Lasi>();
        }
        class Lasi { public Vitriini V; public int Half; public Transform T; public BoxCollider Col; public float S; public Vector3 Closed, Slide; }
        static readonly List<Vitriini> vitriinit = new List<Vitriini>();
        static Vitriini videoCab, konsoliCab;

        // vitrine contre un mur lateral : wallX = face interieure du mur, dir = +1 facade vers +x (mur gauche) ; de z0 a z1,
        // profondeur 0,5 m, hauteur h, tablettes de verre aux hauteurs lv ; deux vitres coulissantes (vitre 0 devant la moitie
        // z < milieu, sur le rail exterieur ; vitre 1 devant l'autre, rail interieur) ; enseigne eclairee optionnelle au-dessus
        static Vitriini Vitrine(string name, float wallX, float dir, float z0, float z1, float h, float[] lv, Color light, string sign)
        {
            var v = new Vitriini { Name = name, Mid = (z0 + z1) / 2f };
            float front = wallX + dir * 0.5f, lo = Mathf.Min(wallX, front), hi = Mathf.Max(wallX, front), t = 0.04f;
            Box(new Vector3(lo, 0f, z0), new Vector3(hi, 0.12f, z1), "tiski", false);                    // socle
            Box(new Vector3(lo, h - 0.06f, z0), new Vector3(hi, h, z1), "tiski", false);                 // dessus
            Box(new Vector3(lo, 0.12f, z0), new Vector3(hi, h - 0.06f, z0 + t), "tiski", false);         // cotes
            Box(new Vector3(lo, 0.12f, z1 - t), new Vector3(hi, h - 0.06f, z1), "tiski", false);
            Box(new Vector3(dir > 0 ? lo : hi - 0.02f, 0.12f, z0 + t), new Vector3(dir > 0 ? lo + 0.02f : hi, h - 0.06f, z1 - t), "paneeli", false);   // fond
            foreach (float y in lv)
            {
                Box(new Vector3(lo + 0.02f, y - 0.008f, z0 + t), new Vector3(hi - 0.06f, y, z1 - t), "lasi", false);           // tablette de verre
                float fx = front - dir * 0.065f;
                Box(new Vector3(Mathf.Min(fx, fx - dir * 0.012f), y - 0.02f, z0 + t), new Vector3(Mathf.Max(fx, fx - dir * 0.012f), y, z1 - t), "metalli", false);   // chant
            }
            // rails haut et bas
            Box(new Vector3(Mathf.Min(front, front - dir * 0.06f), 0.12f, z0), new Vector3(Mathf.Max(front, front - dir * 0.06f), 0.15f, z1), "metalli", false);
            Box(new Vector3(Mathf.Min(front, front - dir * 0.06f), h - 0.09f, z0), new Vector3(Mathf.Max(front, front - dir * 0.06f), h - 0.06f, z1), "metalli", false);
            cols.Add(new Bounds(new Vector3((lo + hi) / 2f, h / 2f, (z0 + z1) / 2f), new Vector3(hi - lo, h, z1 - z0)));
            if (sign != null)
            {
                // caisson d'enseigne au-dessus, face eclairee vers la piece
                float sh = 0.34f;
                Box(new Vector3(lo, h, z0), new Vector3(hi, h + sh, z1), "tiski", false);
                float sx = front + dir * 0.002f;
                Vector3 bl = new Vector3(sx, h + 0.03f, dir > 0 ? z0 + 0.03f : z1 - 0.03f), br = new Vector3(sx, h + 0.03f, dir > 0 ? z1 - 0.03f : z0 + 0.03f);   // (vue de +x : la droite est +z)
                var uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
                Quad(sign, bl, br, br + Vector3.up * (sh - 0.06f), bl + Vector3.up * (sh - 0.06f), uv);
            }
            // lumiere dans la vitrine
            var lg = new GameObject("osuus_vitriini_valo");
            lg.transform.SetParent(root.transform, false); lg.transform.localPosition = new Vector3((lo + hi) / 2f, h - 0.2f, v.Mid);
            var l = lg.AddComponent<Light>(); l.type = LightType.Point; l.color = light; l.intensity = 1.1f; l.range = 1.6f; l.shadows = LightShadows.None;
            // vitres coulissantes (avant : ce qui etait deja construit -- murs, comptoir -- pose sous la boutique ; Flush dans le
            // repere d'une vitre emportait tout avec elle)
            Flush();
            float w = (z1 - z0 - 2 * t) / 2f + 0.03f;
            for (int k = 0; k < 2; k++)
            {
                float rail = front - dir * (k == 0 ? 0.015f : 0.04f);
                float zc = k == 0 ? z0 + t + w / 2f : z1 - t - w / 2f;
                var p = new GameObject("osuus_vitriini_lasi");
                p.transform.SetParent(root.transform, false);
                p.transform.localPosition = new Vector3(rail, 0.15f, zc);
                var keep = root; root = p;
                float ph = h - 0.09f - 0.15f, f = 0.022f;
                Box(new Vector3(-0.006f, 0f, -w / 2f), new Vector3(0.006f, f, w / 2f), "metalli", false);
                Box(new Vector3(-0.006f, ph - f, -w / 2f), new Vector3(0.006f, ph, w / 2f), "metalli", false);
                Box(new Vector3(-0.006f, 0f, -w / 2f), new Vector3(0.006f, ph, -w / 2f + f), "metalli", false);
                Box(new Vector3(-0.006f, 0f, w / 2f - f), new Vector3(0.006f, ph, w / 2f), "metalli", false);
                Quad("lasi", new Vector3(0, f, -w / 2f + f), new Vector3(0, f, w / 2f - f), new Vector3(0, ph - f, w / 2f - f), new Vector3(0, ph - f, -w / 2f + f));
                Quad("lasi", new Vector3(0, f, w / 2f - f), new Vector3(0, f, -w / 2f + f), new Vector3(0, ph - f, -w / 2f + f), new Vector3(0, ph - f, w / 2f - f));
                // poignee (bouton) cote exterieur de la vitre, vers le milieu
                float hz = k == 0 ? w / 2f - 0.06f : -w / 2f + 0.06f;
                Box(new Vector3(dir > 0 ? 0.006f : -0.02f, ph * 0.5f - 0.05f, hz - 0.012f), new Vector3(dir > 0 ? 0.02f : -0.006f, ph * 0.5f + 0.05f, hz + 0.012f), "lista", false);
                Flush();
                root = keep;
                var cg = new GameObject("osuus_vitriini_lasi_coll");
                cg.layer = 2;
                cg.transform.SetParent(p.transform, false);
                cg.transform.localPosition = new Vector3(0f, ph / 2f, 0f);
                var bc = cg.AddComponent<BoxCollider>(); bc.isTrigger = true; bc.size = new Vector3(0.05f, ph, w);
                var pane = new Lasi { V = v, Half = k, T = p.transform, Col = bc, Closed = p.transform.localPosition, Slide = new Vector3(0f, 0f, (k == 0 ? 1f : -1f) * (w - 0.04f)) };
                v.Panes.Add(pane);
            }
            vitriinit.Add(v);
            return v;
        }

        static void TickVitriinit()
        {
            float dt = Time.deltaTime;
            foreach (Vitriini v in vitriinit)
                foreach (Lasi p in v.Panes)
                {
                    if (p.T == null) continue;
                    float want = v.OpenHalf == p.Half ? 1f : 0f;
                    if (Mathf.Approximately(p.S, want)) continue;
                    p.S = Mathf.MoveTowards(p.S, want, dt * 2.2f);
                    p.T.localPosition = p.Closed + p.Slide * Mathf.SmoothStep(0f, 1f, p.S);
                }
        }

        static void ToggleVitriini(Lasi p)
        {
            Vitriini v = p.V;
            v.OpenHalf = v.OpenHalf == p.Half ? -1 : p.Half;
            try { MasterAudio.PlaySound3DAtVector3AndForget("HouseFoley", p.T.position, 0.6f, null, 0f, "door_open"); } catch { }
            L("vitrine " + v.Name + " : " + (v.OpenHalf < 0 ? "fermee" : "moitie " + v.OpenHalf + " ouverte"));
        }

        // article d'une vitrine : accessible seulement quand sa moitie est ouverte
        static bool Reachable(Offer o) { return o.Cab == null || o.Cab.OpenHalf == o.Half; }

        // ---------------------------------------------------------------- articles
        static void EpicerieOffers()
        {
            try { VideoCabOffers(); } catch (Exception e) { L("vitrine video : " + e); }
            try { KonsoliDisplay(); } catch (Exception e) { L("vitrine consoles : " + e); }
            try { SnackOffers(); } catch (Exception e) { L("snacks : " + e); }
            try { BagOffer(); } catch (Exception e) { L("sacs de couchage : " + e); }
            try { AlcoholOffers(); } catch (Exception e) { L("alcool : " + e); }
            try { RallyOffers(); } catch (Exception e) { L("rallye : " + e); }
        }

        static Offer CabOffer(string item, int price, string fr, string en, Vitriini cab, int half)
        {
            return new Offer { Item = item, Price = price, Fr = fr, En = en, Cab = cab, Half = half };
        }

        // coin video : TV en bas, magnetoscopes, camescopes, cassettes en haut ; une serie dans chaque moitie
        static void VideoCabOffers()
        {
            float x = -XIn + 0.225f, tagX = -XIn + 0.435f;
            float[] zs = { -0.27f, 0.57f };
            for (int k = 0; k < 2; k++)
            {
                float z = zs[k];
                var tv = CabOffer(KTv, 590, "TÉLÉVISION", "TELEVISION", videoCab, k);
                Display(tv, KTv, new Vector3(x, 0.152f, z), -90f);
                OfferCol(tv, new Vector3(x, 0.38f, z), new Vector3(0.45f, 0.44f, 0.7f));
                Plane("hltv", new Vector3(tagX, 0.18f, z + 0.27f), 0.12f, 0.06f, Vector3.right);
                var vcr = CabOffer(KNauhuri, 390, "MAGNÉTOSCOPE", "VCR", videoCab, k);
                Display(vcr, KNauhuri, new Vector3(x, 0.6f, z - 0.14f), -90f);
                Display(vcr, KNauhuri, new Vector3(x, 0.71f, z - 0.14f), -90f);
                OfferCol(vcr, new Vector3(x, 0.7f, z - 0.14f), new Vector3(0.45f, 0.2f, 0.4f));
                Plane("hlnauhuri", new Vector3(tagX, 0.632f, z - 0.14f), 0.12f, 0.06f, Vector3.right);
                var kam = CabOffer(KKamera, 890, "CAMÉSCOPE", "CAMCORDER", videoCab, k);
                Display(kam, KKamera, new Vector3(x - 0.02f, 0.6f, z + 0.2f), 90f);
                OfferCol(kam, new Vector3(x, 0.75f, z + 0.2f), new Vector3(0.45f, 0.3f, 0.3f));
                Plane("hlkamera", new Vector3(tagX, 0.632f, z + 0.2f), 0.12f, 0.06f, Vector3.right);
                var vhs = CabOffer(KVhs, 19, "CASSETTE VHS", "VHS TAPE", videoCab, k);
                for (int i = 0; i < 4; i++) Display(vhs, KVhs, new Vector3(x + 0.04f, 1.02f, z - 0.24f + i * 0.13f), 90f, 78f);
                OfferCol(vhs, new Vector3(x, 1.13f, z - 0.05f), new Vector3(0.45f, 0.22f, 0.6f));
                Plane("hlvhs", new Vector3(tagX, 1.052f, z + 0.25f), 0.12f, 0.06f, Vector3.right);
            }
            L("vitrine video : TV 590, magnetoscope 390, camescope 890, cassette 19 mk");
        }

        // consoles : TV de demonstration au milieu (jeu PIKSELIKETTU a l'ecran), boites de console a cote, boites de jeux
        // (titres inventes) sur les autres tablettes ; pas encore en vente (etiquette PIAN MYYNNISSA)
        static void KonsoliDisplay()
        {
            float x = -XIn + 0.24f, front = -XIn + 0.5f;
            Vector3 size; Transform screen;
            GameObject tv = Model(KTv, out size, out screen);
            tv.transform.SetParent(root.transform, false);
            tv.transform.localRotation = Quaternion.Euler(0f, -90f, 0f);
            Bounds b = LocalBounds(tv);
            tv.transform.localPosition = new Vector3(x - b.center.x, 0.6f - b.min.y + 0.002f, konsoliCab.Mid - b.center.z);
            if (screen != null)
            {
                Shader us = Shader.Find("Unlit/Texture");
                screen.GetComponent<Renderer>().sharedMaterial = new Material(us != null ? us : Shader.Find("Legacy Shaders/Diffuse")) { mainTexture = Res("konsoli-demo.png", TextureFormat.RGB24) };
            }
            // boites de console, de part et d'autre de la TV
            foreach (float dz in new[] { -0.52f, 0.52f })
                GameBox(new Vector3(x + 0.02f, 0.6f, konsoliCab.Mid + dz), new Vector3(0.1f, 0.24f, 0.32f), "kboksi", new Rect(0, 0, 1, 1));
            // boites de jeux : 7 par tablette (haut et bas), couvertures de l'atlas 4 x 2
            int n = 0;
            foreach (float y in new[] { 0.152f, 1.02f, 1.44f })
                for (int i = 0; i < 7; i++, n++)
                {
                    int c = (n * 3 + i) % 8;
                    GameBox(new Vector3(x + 0.12f, y, -2.3f + 0.05f + 0.105f + i * 0.21f), new Vector3(0.03f, 0.176f, 0.128f), "kpelit", new Rect((c % 4) / 4f, (c < 4 ? 0.5f : 0f), 0.25f, 0.5f));
                }
            Plane("hlpian", new Vector3(front - 0.065f, 0.632f, konsoliCab.Mid), 0.14f, 0.07f, Vector3.right);
            L("vitrine consoles : TV de demonstration, " + n + " boites de jeux");
        }

        // boite (pave) posee par le bas en at, couverture sur la face tournee vers +x (vers la piece)
        static void GameBox(Vector3 at, Vector3 s, string tex, Rect r)
        {
            Vector3 mn = new Vector3(at.x - s.x / 2f, at.y, at.z - s.z / 2f), mx = new Vector3(at.x + s.x / 2f, at.y + s.y, at.z + s.z / 2f);
            Box(mn, mx, "kreuna", false);
            var uv = new[] { new Vector2(r.xMin, r.yMin), new Vector2(r.xMax, r.yMin), new Vector2(r.xMax, r.yMax), new Vector2(r.xMin, r.yMax) };
            float fx = mx.x + 0.002f;
            Quad(tex, new Vector3(fx, mn.y, mn.z), new Vector3(fx, mn.y, mx.z), new Vector3(fx, mx.y, mx.z), new Vector3(fx, mx.y, mn.z), uv);   // (vue de +x : la droite est +z)
        }

        // snacks du cinema sur la gondole du milieu : chips et bonbons cote comptoir (+z), popcorn et limonade cote fond
        static void SnackOffers()
        {
            float h = 1.3f; int lv = 3;
            float y1 = GondolaTop(h, lv, 0), y2 = GondolaTop(h, lv, 1);
            float zf = 0.15f, zb = -0.15f;
            GondolaRow(KSipsit, 8, "CHIPS", "CHIPS", "hlsipsit", y1, zf, 1f);
            GondolaRow(KKarkit, 6, "BONBONS", "CANDY", "hlkarkit", y2, zf, 1f);
            GondolaRow(KLimsa, 9, "LIMONADE", "SODA", "hllimsa", y1, zb, -1f);
            GondolaRow(KPopcorn, 12, "POPCORN", "POPCORN", "hlpopcorn", y2, zb, -1f);
            L("gondole : chips 8, bonbons 6, limonade 9, popcorn 12 mk");
        }

        static void GondolaRow(string kind, int price, string fr, string en, string tag, float y, float z, float face)
        {
            var o = new Offer { Item = kind, Price = price, Fr = fr, En = en };
            for (int i = 0; i < 4; i++) Display(o, kind, new Vector3(-0.5f + i * 0.36f, y, z), face > 0 ? 180f : 0f);
            OfferCol(o, new Vector3(0.05f, y + 0.13f, z), new Vector3(1.5f, 0.28f, 0.26f));
            Plane(tag, new Vector3(0.05f, y - 0.002f, face > 0 ? 0.282f : -0.282f), 0.12f, 0.06f, face > 0 ? Vector3.forward : Vector3.back);
        }

        // sacs de couchage : gondole basse du fond, tablette du bas, des deux cotes
        static void BagOffer()
        {
            if (!Ajouts.Bags) { L("sacs de couchage coupes (lanceur : ajouts COOP) : pas en rayon"); return; }
            Mesh m; Material mat;
            if (!SleepingBags.DisplayModel(out m, out mat)) { L("sacs de couchage : modele indisponible, rien en rayon"); return; }
            float y = GondolaTop(1.0f, 2, 0);
            foreach (float face in new[] { 1f, -1f })
            {
                float zMid = face > 0 ? -1.2f : -1.45f;
                var offer = new Offer { Item = "makuupussi", Price = SleepingBagPrice, Fr = "SAC DE COUCHAGE", En = "SLEEPING BAG" };
                foreach (float x in new[] { -0.45f, 0.05f, 0.55f })
                {
                    var g = new GameObject("osuus_myynnissa_makuupussi");
                    offer.Shown.Add(g);
                    g.transform.SetParent(root.transform, false);
                    g.AddComponent<MeshFilter>().sharedMesh = m;
                    g.AddComponent<MeshRenderer>().sharedMaterial = mat;
                    Bounds b = m.bounds;
                    if (b.size.z > b.size.x) g.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);
                    Bounds lb = LocalBounds(g);
                    float sc = Mathf.Min(1f, 0.45f / Mathf.Max(0.01f, lb.size.x));   // (rouleau plus large qu'une place : reduit)
                    g.transform.localScale *= sc;
                    lb = LocalBounds(g);
                    g.transform.localPosition = new Vector3(x - lb.center.x, y - lb.min.y + 0.002f, zMid - lb.center.z);
                }
                OfferCol(offer, new Vector3(0.05f, y + 0.17f, zMid), new Vector3(1.5f, 0.34f, 0.24f));
                Plane("hintalappu", new Vector3(0.05f, y - 0.002f, face > 0 ? -1.068f : -1.582f), 0.12f, 0.06f, face > 0 ? Vector3.forward : Vector3.back);
            }
            L("en rayon : sac de couchage " + SleepingBagPrice + " mk");
        }

        // alcool : bieres sur deux planches, eau-de-vie en haut (etagere du mur droit, face vers -x)
        static void AlcoholOffers()
        {
            float x = XIn - 0.2f, tagX = XIn - 0.4f - 0.004f;
            for (int k = 1; k <= 2; k++)
            {
                var o = new Offer { Item = KOlut, Price = OlutPrice, Fr = "BIÈRE", En = "BEER" };
                for (int i = 0; i < 6; i++) Display(o, KOlut, new Vector3(x + (i % 2 == 0 ? -0.05f : 0.06f), alcoholTops[k], -0.45f + i * 0.24f), 90f);
                OfferCol(o, new Vector3(x, alcoholTops[k] + 0.13f, 0.15f), new Vector3(0.38f, 0.26f, 1.5f));
                Plane("hlolut", new Vector3(tagX, alcoholTops[k] - 0.035f, 0.15f), 0.12f, 0.06f, Vector3.left);
            }
            var v = new Offer { Item = KViina, Price = ViinaPrice, Fr = "EAU-DE-VIE", En = "VODKA" };
            for (int i = 0; i < 5; i++) Display(v, KViina, new Vector3(x, alcoholTops[3], -0.4f + i * 0.28f), 90f);
            OfferCol(v, new Vector3(x, alcoholTops[3] + 0.16f, 0.15f), new Vector3(0.38f, 0.32f, 1.5f));
            Plane("hlviina", new Vector3(tagX, alcoholTops[3] - 0.035f, 0.15f), 0.12f, 0.06f, Vector3.left);
            L("alcool : biere " + OlutPrice + ", eau-de-vie " + ViinaPrice + " mk");
        }

        // rallye : GPS KARTTURI (planche du milieu), casques (planche du haut)
        static void RallyOffers()
        {
            float x = XIn - 0.2f, tagX = XIn - 0.4f - 0.004f, zc = -1.55f;
            var gps = new Offer { Item = KKartturi, Price = KartturiPrice, Fr = "KARTTURI 2000 (GPS DE RALLYE)", En = "KARTTURI 2000 (RALLY GPS)" };
            foreach (float z in new[] { -1.85f, -1.55f, -1.25f }) Display(gps, KKartturi, new Vector3(x, rallyTops[2], z), 90f, -12f);
            Shader us = Shader.Find("Unlit/Texture");
            var splash = new Material(us != null ? us : Shader.Find("Legacy Shaders/Diffuse")) { mainTexture = Kartturi.NewDevice().Tex };
            foreach (GameObject g in gps.Shown) { Transform sc = g.transform.Find("naytto"); if (sc != null) sc.GetComponent<Renderer>().sharedMaterial = splash; }
            OfferCol(gps, new Vector3(x, rallyTops[2] + 0.1f, zc), new Vector3(0.38f, 0.22f, 1.0f));
            Plane("hlkartturi", new Vector3(tagX, rallyTops[2] - 0.035f, zc), 0.12f, 0.06f, Vector3.left);
            // casques : copies du casque du jeu (JD 23/10 : « les casques du jeu qui spawn dans la chambre »), portables comme lui
            var kyp = new Offer { Item = KKypara, Price = KyparaPrice, Fr = "CASQUE", En = "HELMET" };
            foreach (float z in new[] { -1.85f, -1.25f })
            {
                GameObject h = Clone("EQUIPMENTS/Helmet/helmet(itemx)", 0, new Vector3(x, rallyTops[3], z), -90f, new Vector3(0, -1, 0), false, false);   // (visiere vers le client, JD 23/10)
                if (h != null) kyp.Shown.Add(h); else Display(kyp, KKypara, new Vector3(x, rallyTops[3], z), 90f);
            }
            OfferCol(kyp, new Vector3(x, rallyTops[3] + 0.15f, zc), new Vector3(0.38f, 0.3f, 1.0f));
            Plane("hlkypara", new Vector3(tagX, rallyTops[3] - 0.035f, zc), 0.12f, 0.06f, Vector3.left);
            L("rallye : KARTTURI " + KartturiPrice + ", casque " + KyparaPrice + " mk");
        }

        static void EpicerieMaterials()
        {
            OwnMat("kkyltti", "konsoli-kyltti.png", true);
            if (mats.ContainsKey("kkyltti"))
            {
                Shader us = Shader.Find("Unlit/Texture");
                if (us != null) mats["kkyltti"].shader = us;   // (enseigne lumineuse)
            }
            OwnMat("kpelit", "pelit-kannet.png");
            OwnMat("kboksi", "konsoli-laatikko.png");
            Mat("kreuna", "wood_panel20", 1f, new Color(0.18f, 0.18f, 0.2f));
            foreach (string k in new[] { "sipsit", "karkit", "limsa", "popcorn", "olut", "viina", "kypara", "pian" }) OwnMat("hl" + k, "hintalappu-" + k + ".png");
        }

        // ---------------------------------------------------------------- objets : biere, eau-de-vie, casque
        static bool EpiModel(string kind, GameObject g, out Vector3 size)
        {
            size = Vector3.one * 0.1f;
            if (kind == KOlut)
            {
                // bouteille brune 0,33 l, etiquette des deux cotes, capsule
                size = new Vector3(0.065f, 0.23f, 0.065f);
                var brown = Plain(new Color(0.28f, 0.14f, 0.05f));
                Piece(g, PrimitiveType.Cylinder, new Vector3(0, -0.04f, 0), Vector3.zero, new Vector3(0.062f, 0.075f, 0.062f), brown);
                Piece(g, PrimitiveType.Cylinder, new Vector3(0, 0.055f, 0), Vector3.zero, new Vector3(0.036f, 0.025f, 0.036f), brown);
                Piece(g, PrimitiveType.Cylinder, new Vector3(0, 0.09f, 0), Vector3.zero, new Vector3(0.024f, 0.02f, 0.024f), brown);
                Piece(g, PrimitiveType.Cylinder, new Vector3(0, 0.111f, 0), Vector3.zero, new Vector3(0.027f, 0.004f, 0.027f), Plain(new Color(0.8f, 0.7f, 0.2f)));
                Piece(g, PrimitiveType.Quad, new Vector3(0, -0.04f, -0.0315f), Vector3.zero, new Vector3(0.05f, 0.06f, 1f), TexMat("olut-etiketti.png"));
                Piece(g, PrimitiveType.Quad, new Vector3(0, -0.04f, 0.0315f), new Vector3(0, 180, 0), new Vector3(0.05f, 0.06f, 1f), TexMat("olut-etiketti.png"));
                return true;
            }
            if (kind == KViina)
            {
                // bouteille 0,5 l en verre clair (eau-de-vie), bouchon bleu
                size = new Vector3(0.075f, 0.3f, 0.075f);
                var glass = Plain(new Color(0.8f, 0.88f, 0.9f));
                Piece(g, PrimitiveType.Cylinder, new Vector3(0, -0.04f, 0), Vector3.zero, new Vector3(0.072f, 0.11f, 0.072f), glass);
                Piece(g, PrimitiveType.Cylinder, new Vector3(0, 0.1f, 0), Vector3.zero, new Vector3(0.03f, 0.04f, 0.03f), glass);
                Piece(g, PrimitiveType.Cylinder, new Vector3(0, 0.145f, 0), Vector3.zero, new Vector3(0.034f, 0.008f, 0.034f), Plain(new Color(0.1f, 0.2f, 0.55f)));
                Piece(g, PrimitiveType.Quad, new Vector3(0, -0.03f, -0.0365f), Vector3.zero, new Vector3(0.06f, 0.075f, 1f), TexMat("viina-etiketti.png"));
                Piece(g, PrimitiveType.Quad, new Vector3(0, -0.03f, 0.0365f), new Vector3(0, 180, 0), new Vector3(0.06f, 0.075f, 1f), TexMat("viina-etiketti.png"));
                return true;
            }
            if (kind == KKypara)
            {
                // casque integral blanc a bande rouge, visiere fumee vers -z, mentonniere
                size = new Vector3(0.27f, 0.28f, 0.3f);
                var white = Plain(new Color(0.93f, 0.93f, 0.91f)); var red = Plain(new Color(0.78f, 0.1f, 0.08f));
                Piece(g, PrimitiveType.Sphere, new Vector3(0, 0.015f, 0.01f), Vector3.zero, new Vector3(0.26f, 0.27f, 0.29f), white);      // coque
                Piece(g, PrimitiveType.Sphere, new Vector3(0, 0.017f, 0.01f), Vector3.zero, new Vector3(0.045f, 0.276f, 0.296f), red);      // bande
                Piece(g, PrimitiveType.Sphere, new Vector3(0, 0.0f, -0.045f), Vector3.zero, new Vector3(0.215f, 0.1f, 0.22f), Plain(new Color(0.06f, 0.07f, 0.1f)));   // visiere
                Piece(g, PrimitiveType.Sphere, new Vector3(0, -0.085f, -0.03f), Vector3.zero, new Vector3(0.2f, 0.09f, 0.2f), white);      // mentonniere
                return true;
            }
            return false;
        }
    }
}
