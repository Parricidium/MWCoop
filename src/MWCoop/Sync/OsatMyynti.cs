using System;
using System.Collections.Generic;
using System.IO;
using HutongGames.PlayMaker;
using MWCoop.Net;
using UnityEngine;

namespace MWCoop
{
    // CARDINAL VARAOSAT : la vente (batiment : OsatRakennus).
    //  - Aile CARDINAL : les objets que les mods inscrivent (CoopApi.AddShopItem : nom, prix, modele, rappel a l'achat). Le
    //    modele est copie (maillages seulement) dans une case d'etagere, ou sur une estrade s'il est gros. Stock illimite. A
    //    l'achat, le joueur paie, et le rappel du mod est appele chez TOUS les joueurs (meme ordre, @osat op 2) avec l'acheteur
    //    et la place de retrait : le mod fait apparaitre son objet (le mod est chez tout le monde : MWCoop partage les mods).
    //  - Aile KAYTETYT OSAT : des pieces d'occasion de la Rivett, tirees par l'hote dans la liste des petites annonces du jeu
    //    (PostSystem/VINLIST_order, VINS) tous les 3 jours du jeu (GlobalDaysPassed), usure 15-70 %, prix d'apres le guide des
    //    prix du marche aux puces (FleaMarket/SaleTable, PriceGuide) ; le stock est envoye a tous (@osat op 0) et garde dans la
    //    sauvegarde de l'hote (MWCoop-osat.txt). A l'achat : la piece sort du distributeur du jeu (SPAWNERS_VIN, SPAWNITEM,
    //    comme une commande des petites annonces : vraie piece, sauvee et rechargee par le jeu), usure forcee a celle du stock ;
    //    chez les autres, le meme distributeur est rejoue avec ses compteurs mis a ceux de l'acheteur (meme nom, donc meme ID :
    //    Parts, Props et Fluids la suivent ensuite), comme Boxes.
    public static partial class Kauppa
    {
        public const int OsatRestockDays = 3, OsatStockSize = 16;

        // ---------------------------------------------------------------- objets des mods (gardes d'un chargement a l'autre)
        public class OsatModItem { public string Id, Name; public float Price; public GameObject Model; public Action<int, bool, Vector3, Quaternion> Bought; }
        static readonly List<OsatModItem> osatModItems = new List<OsatModItem>();

        public static bool OsatAddModItem(string id, string name, float price, GameObject model, Action<int, bool, Vector3, Quaternion> bought)
        {
            if (string.IsNullOrEmpty(id) || bought == null || price < 0f) return false;
            OsatModItem it = osatModItems.Find(x => x.Id == id);
            if (it == null) { it = new OsatModItem { Id = id }; osatModItems.Add(it); }
            it.Name = string.IsNullOrEmpty(name) ? id : name; it.Price = price; it.Model = model; it.Bought = bought;
            Log.Info("magasin de pieces : objet de mod '" + id + "' (" + it.Name + ", " + price.ToString("F2") + " mk) inscrit");
            osatDirty = true;
            return true;
        }

        public static void OsatRemoveModItem(string id)
        {
            if (osatModItems.RemoveAll(x => x.Id == id) > 0) osatDirty = true;
        }

        public static int OsatModItemCount { get { return osatModItems.Count; } }

        // ---------------------------------------------------------------- stock d'occasion
        class OsatUsed { public string Vin, Name; public int Price; public float Wear; public bool Sold; }
        static List<OsatUsed> osatStock;
        static int osatStockDay = -1;
        static bool osatLoaded, osatAsked, osatDirty;

        // une offre en rayon (objet de mod ou piece d'occasion)
        class OsatOffer { public int Wing; public string Name; public int Price; public OsatModItem Mod; public OsatUsed Used; public int UsedIndex; public GameObject Display, Tag; public BoxCollider Col; public OsatSlot Slot; }
        static readonly List<OsatOffer> osatOffers = new List<OsatOffer>();
        static OsatOffer osatLooked;
        static float osatLastBuy, osatNoMoneyUntil, osatLastHello = -100f, osatNextAsk;
        static bool osatInside;
        static int osatBuyN;
        static Avatar osatVendor;
        static readonly List<KeyValuePair<GameObject, float>> osatWear = new List<KeyValuePair<GameObject, float>>();
        static float osatWearUntil;

        static void ResetOsatMyynti()
        {
            osatStock = null; osatStockDay = -1; osatLoaded = false; osatAsked = false; osatDirty = true;
            osatOffers.Clear(); osatSlots.Clear(); osatLooked = null; osatInside = false; osatVendor = null; osatWear.Clear(); osatTestDone = false;
        }

        static int DaysPassed
        {
            get
            {
                FsmInt d = FsmVariables.GlobalVariables.FindFsmInt("GlobalDaysPassed");
                return d != null ? d.Value : GameDay;
            }
        }

        static void OsatVendor()
        {
            // (JD, 23/10 : un corps plus fin que le vendeur de l'Osuuskauppa, et un polo aux couleurs de l'enseigne)
            osatVendor = Avatar.CreatePreview("OSAT-myyja", 0, SlimBody());
            if (osatVendor == null) { L("magasin de pieces : vendeur indisponible (corps de PNJ)"); return; }
            SkinnedMeshRenderer br = osatVendor.BodyRenderer;
            Material shirt = br != null ? ShirtMaterial("osat-paita.png") : null;
            if (shirt != null)
            {
                Material[] ms = br.sharedMaterials;
                int k = 0;
                for (int i = 0; i < ms.Length; i++) if (ms[i] != null && ms[i].name.StartsWith("char_shirt")) { k = i; break; }
                ms[k] = shirt;
                br.sharedMaterials = ms;
            }
            Transform r = osatVendor.Root.transform;
            r.SetParent(osat.transform, false);
            r.localPosition = new Vector3(0f, 0f, -3.4f);
            r.localRotation = Quaternion.identity;
            var cg = new GameObject("osat_myyja_coll");
            cg.transform.SetParent(osat.transform, false);
            cg.transform.localPosition = new Vector3(0f, 0.9f, -3.4f);
            var cc = cg.AddComponent<CapsuleCollider>(); cc.height = 1.8f; cc.radius = 0.28f;
        }

        // corps fin : [Osat] MyyjaKeho, sinon 1714-32 (Keijo du PERAPORTTI, Julli...), sinon le plus etroit des corps d'adulte
        static string SlimBody()
        {
            string want = Config.Get("Osat", "MyyjaKeho", "1714-32");
            List<string> all = Looks.Bodies();
            if (all.Contains(want)) return want;
            string best = null; int bw = int.MaxValue;
            foreach (string k in all)
            {
                string[] a = k.Split('-');
                int wcm, vc;
                if (a.Length != 2 || !int.TryParse(a[0], out vc) || !int.TryParse(a[1], out wcm) || wcm < 30) continue;   // (pas l'enfant, pas les prefixes)
                if (wcm < bw) { bw = wcm; best = k; }
            }
            L("magasin de pieces : corps du vendeur " + (best ?? "par defaut") + " (" + want + " absent ; corps : " + string.Join(", ", all.ToArray()) + ")");
            return best;
        }

        // ---------------------------------------------------------------- chaque image
        static void TickOsat(float now)
        {
            if (osat == null) return;
            TickOsatDoors();
            KinoGui();
            TickKinoTexts();
            if (!osatLoaded) { osatLoaded = true; if (BagAuthority) LoadOsat(); }
            if (Config.GetInt("Test", "OsatTesti", 0) != 0) OsatTestItems();
            // stock : l'hote le tire (tous les 3 jours) ; un invite le demande
            if (BagAuthority)
            {
                if (osatStock == null || DaysPassed - osatStockDay >= OsatRestockDays || DaysPassed < osatStockDay) DrawStock();
            }
            else if (osatStock == null && now >= osatNextAsk && Session.Active)
            {
                osatNextAsk = now + 10f;
                Session.SendToHost(new NetWriter(Msg.Job).U8(Session.LocalId).Str("@osat").U8(4), true);
            }
            if (osatDirty) { osatDirty = false; OsatDisplays(); }
            TickOsatWear(now);
            // visee et achat
            OsatOffer hit = null;
            Camera cam = Game.MainCam;
            if (cam != null && !Game.GlobalBool("PlayerSleeps") && (cam.transform.position - osat.transform.position).sqrMagnitude < 30f * 30f)
            {
                var ray = new Ray(cam.transform.position, cam.transform.forward);
                float best = 2.2f; RaycastHit h;
                foreach (OsatOffer o in osatOffers) if (o.Col != null && o.Col.Raycast(ray, out h, best)) { best = h.distance; hit = o; }
            }
            if (hit != osatLooked) { if (osatLooked != null) KinoClearGui(); osatLooked = hit; }
            if (hit != null)
            {
                guiBuy.Value = true;
                var ci = System.Globalization.CultureInfo.InvariantCulture;
                if (now < osatNoMoneyUntil) guiText.Value = "NOT ENOUGH MONEY";
                else guiText.Value = hit.Name.ToUpperInvariant() + (hit.Used != null ? " (" + Lang.TG("ÉTAT ", "CONDITION ") + Mathf.RoundToInt(hit.Used.Wear) + " %)" : "") + " " + hit.Price.ToString("F2", ci) + " MK";
                if (Input.GetMouseButtonDown(0)) OsatBuy(hit, now);
            }
            // accueil
            bool inNow = InOsat(Game.PlayerT);
            if (inNow && !osatInside && now - osatLastHello > 40f && osatVendor != null)
            {
                osatLastHello = now;
                Say("Myyjä : Tervetuloa ! Les pièces des mods de Cardinal à gauche, l'occasion pour la Rivett à droite.", "Vendor: Tervetuloa! Cardinal's mod parts on the left, used Rivett parts on the right.");
            }
            osatInside = inNow;
        }

        // ---------------------------------------------------------------- vitrines : une offre par case
        static void OsatDisplays()
        {
            foreach (OsatOffer o in osatOffers) { if (o.Display != null) UnityEngine.Object.Destroy(o.Display); if (o.Tag != null) UnityEngine.Object.Destroy(o.Tag); if (o.Col != null) UnityEngine.Object.Destroy(o.Col.gameObject); }
            osatOffers.Clear(); osatLooked = null;
            foreach (OsatSlot s in osatSlots) s.Item = null;
            foreach (OsatModItem it in osatModItems)
                OsatPlace(new OsatOffer { Wing = 0, Name = it.Name, Price = Mathf.RoundToInt(it.Price), Mod = it }, it.Model);
            if (osatStock != null)
                for (int i = 0; i < osatStock.Count; i++)
                {
                    OsatUsed u = osatStock[i];
                    if (u.Sold) continue;
                    OsatPlace(new OsatOffer { Wing = 1, Name = u.Name, Price = u.Price, Used = u, UsedIndex = i }, Template(u.Vin));
                }
            L("magasin de pieces : " + osatOffers.Count + " article(s) en rayon (" + osatModItems.Count + " de mods, " + (osatStock != null ? osatStock.FindAll(x => !x.Sold).Count : 0) + " d'occasion)");
        }

        // pose l'offre dans une case libre de son aile (les gros objets sur une estrade, sinon reduits) et sa copie visible
        static void OsatPlace(OsatOffer o, GameObject model)
        {
            GameObject vis = model != null ? VisualCopy(model) : null;
            Bounds b = vis != null ? LocalBoundsOf(vis) : new Bounds(Vector3.zero, new Vector3(0.3f, 0.2f, 0.3f));
            // couche a plat (le plus petit cote vertical), le plus long le long de la case
            Vector3 e = b.size;
            Quaternion lay = e.y <= e.x && e.y <= e.z ? Quaternion.identity : e.x <= e.z ? Quaternion.Euler(0f, 0f, 90f) : Quaternion.Euler(90f, 0f, 0f);
            Vector3 le = Abs(lay * e);
            Quaternion turn = le.z > le.x ? Quaternion.Euler(0f, 90f, 0f) : Quaternion.identity;
            Quaternion orient = turn * lay;
            Vector3 sz = Abs(orient * e);
            bool big = sz.x > 0.75f || sz.z > 0.5f || sz.y > 0.4f;
            // case libre de l'aile : a hauteur des yeux d'abord, pres du comptoir d'abord (les cases du bon gabarit avant les autres)
            OsatSlot slot = null; float best = float.MaxValue;
            foreach (OsatSlot s in osatSlots)
            {
                if (s.Wing != o.Wing || s.Item != null) continue;
                float y = s.Pos.y, rank = s.Big ? 0f : y > 1f && y < 1.2f ? 0f : y > 0.5f && y < 0.7f ? 1f : y > 1.4f ? 2f : 3f;
                float score = (s.Big == big ? 0f : 1000f) + rank * 100f + Mathf.Abs(s.Pos.x) + (s.Face.x != 0f ? 5f : 0f);
                if (score < best) { best = score; slot = s; }
            }
            if (slot == null) { if (vis != null) UnityEngine.Object.Destroy(vis); L("magasin de pieces : plus de place pour " + o.Name); return; }
            slot.Item = o; o.Slot = slot;
            Quaternion face = Quaternion.LookRotation(slot.Face);
            if (vis != null)
            {
                vis.transform.SetParent(osat.transform, false);
                // reduit pour tenir dans la case
                float k = Mathf.Min(1f, slot.Size.x / Mathf.Max(0.01f, sz.x), slot.Size.y / Mathf.Max(0.01f, sz.y), slot.Size.z / Mathf.Max(0.01f, sz.z));
                vis.transform.localScale = Vector3.one * k;
                vis.transform.localRotation = face * orient;
                Vector3 c = orient * b.center;   // (bas de la boite tournee : centre - demi-hauteur)
                Vector3 bottom = face * (new Vector3(c.x, c.y - sz.y / 2f, c.z) * k);
                vis.transform.localPosition = slot.Pos - bottom + Vector3.up * 0.005f;
                o.Display = vis;
            }
            var cg = new GameObject("osat_article");
            cg.transform.SetParent(osat.transform, false);
            cg.transform.localPosition = slot.Pos + Vector3.up * slot.Size.y / 2f;
            cg.transform.localRotation = face;
            o.Col = cg.AddComponent<BoxCollider>();
            o.Col.size = new Vector3(slot.Size.x, slot.Size.y, slot.Size.z);
            o.Col.isTrigger = true;
            o.Tag = OsatTag(o.Name, o.Price, slot);
            osatOffers.Add(o);
        }

        static Vector3 Abs(Vector3 v) { return new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z)); }

        // etiquette de prix sur le bord de la case (nom et prix), lisible depuis l'allee
        static GameObject OsatTag(string name, int price, OsatSlot slot)
        {
            if (arial == null) arial = Resources.GetBuiltinResource<Font>("Arial.ttf");
            if (arial == null) return null;
            var g = new GameObject("osat_hinta");
            g.transform.SetParent(osat.transform, false);
            float edge = slot.Big ? 0.73f : slot.Size.z / 2f + 0.03f;
            g.transform.localPosition = slot.Pos + slot.Face * edge + Vector3.up * (slot.Big ? -0.12f : -0.035f);
            g.transform.localRotation = Quaternion.LookRotation(-slot.Face);
            var tm = g.AddComponent<TextMesh>();
            string n = name.Length > 18 ? name.Substring(0, 17) + "." : name;
            tm.font = arial; tm.fontSize = 64; tm.characterSize = (slot.Big ? 0.05f : 0.04f) / 6f; tm.anchor = TextAnchor.MiddleCenter; tm.alignment = TextAlignment.Center;
            tm.fontStyle = FontStyle.Bold; tm.color = Color.white; tm.text = n.ToUpperInvariant() + "  " + price + " MK";
            // (sans eclairage : lisible dans l'ombre des tablettes ; pas GUI/Text Shader, qui se dessine a travers les murs)
            Shader sh = Shader.Find("Unlit/Transparent") ?? Shader.Find("Legacy Shaders/Transparent/Diffuse") ?? Shader.Find("Transparent/Diffuse") ?? arial.material.shader;
            var m = new Material(sh) { name = "osat_hinta", color = new Color(1f, 0.92f, 0.3f), mainTexture = arial.material.mainTexture };
            g.GetComponent<MeshRenderer>().sharedMaterial = m;
            kinoTexts.Add(m);
            return g;
        }

        // copie visible d'un objet (maillages et matieres seulement, enfants inactifs compris), repere de sa racine
        static GameObject VisualCopy(GameObject src)
        {
            var g = new GameObject("osat_vitrine_" + src.name);
            Transform st = src.transform;
            int n = 0;
            foreach (Renderer r in src.GetComponentsInChildren<Renderer>(true))
            {
                Mesh mesh = null;
                if (r is SkinnedMeshRenderer) mesh = ((SkinnedMeshRenderer)r).sharedMesh;
                else if (r is MeshRenderer) { MeshFilter mf = r.GetComponent<MeshFilter>(); mesh = mf != null ? mf.sharedMesh : null; }
                if (mesh == null || mesh.name.StartsWith("Combined Mesh")) continue;
                var c = new GameObject(r.name);
                c.transform.SetParent(g.transform, false);
                c.transform.localPosition = st.InverseTransformPoint(r.transform.position);
                c.transform.localRotation = Quaternion.Inverse(st.rotation) * r.transform.rotation;
                Vector3 ls = r.transform.lossyScale, ss = st.lossyScale;
                c.transform.localScale = new Vector3(ls.x / Mathf.Max(1e-4f, ss.x), ls.y / Mathf.Max(1e-4f, ss.y), ls.z / Mathf.Max(1e-4f, ss.z));
                c.AddComponent<MeshFilter>().sharedMesh = mesh;
                c.AddComponent<MeshRenderer>().sharedMaterials = r.sharedMaterials;
                n++;
            }
            if (n == 0) { UnityEngine.Object.Destroy(g); return null; }
            return g;
        }

        static Bounds LocalBoundsOf(GameObject g)
        {
            bool any = false; Bounds b = new Bounds();
            foreach (MeshFilter mf in g.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null) continue;
                Bounds mb = mf.sharedMesh.bounds;
                Matrix4x4 m = Matrix4x4.TRS(mf.transform.localPosition, mf.transform.localRotation, mf.transform.localScale);
                if (mf.transform.parent != g.transform) m = g.transform.worldToLocalMatrix * mf.transform.localToWorldMatrix;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 p = m.MultiplyPoint3x4(mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1)));
                    if (!any) { b = new Bounds(p, Vector3.zero); any = true; } else b.Encapsulate(p);
                }
            }
            return any ? b : new Bounds(Vector3.zero, Vector3.one * 0.3f);
        }

        // ---------------------------------------------------------------- achat
        static void OsatBuy(OsatOffer o, float now)
        {
            if (now - osatLastBuy < 0.6f) return;
            osatLastBuy = now;
            if (money == null || money.Value < o.Price)
            {
                osatNoMoneyUntil = now + 2f;
                Say("Myyjä : il te manque de l'argent.", "Vendor: you're a bit short on money.");
                return;
            }
            Vector3 pk = OsatPickup(o.Wing) + new Vector3(UnityEngine.Random.Range(-0.35f, 0.35f), 0.25f + 0.1f * (osatBuyN++ % 4), UnityEngine.Random.Range(-0.2f, 0.2f));
            Vector3 pos = osat.transform.TransformPoint(pk);
            Quaternion rot = osat.transform.rotation * Quaternion.Euler(0f, UnityEngine.Random.Range(0f, 360f), 0f);
            if (o.Mod != null)
            {
                money.Value -= o.Price;   // (Wallet : une depense reste au joueur)
                CashSound();
                L("magasin de pieces : achat de '" + o.Mod.Id + "' " + o.Price + " mk, reste " + money.Value.ToString("F0") + " mk");
                InvokeMod(o.Mod, Session.LocalId, true, pos, rot);
                if (Session.Active) Session.SendAll(new NetWriter(Msg.Job).U8(Session.LocalId).Str("@osat").U8(2).Str(o.Mod.Id).Vec(pos).Quat(rot), true);
                Say("Myyjä : Kiitos ! Tu le trouveras sur la palette NOUTO.", "Vendor: Kiitos! You'll find it on the NOUTO pallet.");
                return;
            }
            OsatUsed u = o.Used;
            if (u == null || u.Sold) return;
            List<KeyValuePair<string, int>> before;
            GameObject go = SpawnUsed(u.Vin, u.Wear, pos, rot, null, out before);
            if (go == null) { Say("Myyjä : celle-là, je ne la retrouve plus... prends-en une autre.", "Vendor: can't find that one anymore... pick another."); return; }
            money.Value -= o.Price;
            CashSound();
            u.Sold = true; osatDirty = true;
            L("magasin de pieces : achat de " + u.Name + " (" + u.Vin + ", usure " + u.Wear.ToString("F0") + ") " + o.Price + " mk : " + go.name + ", reste " + money.Value.ToString("F0") + " mk");
            if (Session.Active)
            {
                var w = new NetWriter(Msg.Job).U8(Session.LocalId).Str("@osat").U8(1).U8(o.UsedIndex).Str(u.Vin).F32(u.Wear).Vec(pos).Quat(rot).U8(before.Count);
                foreach (KeyValuePair<string, int> kv in before) w.Str(kv.Key).I32(kv.Value);
                Session.SendAll(w, true);
            }
            Say("Myyjä : Kiitos ! Elle est sur la palette NOUTO, à droite.", "Vendor: Kiitos! It's on the NOUTO pallet, on the right.");
        }

        static void CashSound()
        {
            try { MasterAudio.PlaySound3DAtVector3AndForget("Store", osat.transform.TransformPoint(new Vector3(1.4f, 1.1f, -2.3f)), 1f, null, 0f, "cash_register_2"); } catch { }
        }

        static void InvokeMod(OsatModItem it, int buyer, bool here, Vector3 pos, Quaternion rot)
        {
            try { it.Bought(buyer, here, pos, rot); }
            catch (Exception e) { Log.Warn("magasin de pieces : le mod de '" + it.Id + "' a plante a l'achat : " + e); }
        }

        // ---------------------------------------------------------------- pieces du jeu (distributeurs SPAWNERS_VIN)
        static GameObject PostChild(string name) { return Game.FindAny("CARPARTS/PARTSYSTEM/PostSystem/" + name); }

        static PlayMakerFSM VinSpawner(string vin)
        {
            GameObject vs = PostChild("VINSpawners");
            if (vs == null) return null;
            foreach (PlayMakerHashTableProxy h in vs.GetComponents<PlayMakerHashTableProxy>())
            {
                if (h.referenceName != "Spawners" || h._hashTable == null || !h._hashTable.ContainsKey(vin)) continue;
                GameObject g = h._hashTable[vin] as GameObject;
                return g != null ? Game.FsmOn(g, "Spawn") : null;
            }
            return null;
        }

        static GameObject Template(string vin)
        {
            PlayMakerFSM sp = VinSpawner(vin);
            FsmGameObject pf = sp != null ? sp.FsmVariables.FindFsmGameObject("Prefab") : null;
            return pf != null ? pf.Value : null;
        }

        static string PartName(GameObject tpl, string vin)
        {
            if (tpl != null)
                foreach (PlayMakerFSM f in tpl.GetComponents<PlayMakerFSM>())
                {
                    FsmString n = f.FsmVariables.FindFsmString("Name");
                    if (n != null && !string.IsNullOrEmpty(n.Value)) return n.Value;
                }
            return vin;
        }

        static System.Collections.Hashtable priceGuide;
        static int UsedPrice(string name, string cat, float wear)
        {
            if (priceGuide == null)
            {
                GameObject st = Game.FindAny("FleaMarket/SaleTable");
                if (st != null) foreach (PlayMakerHashTableProxy h in st.GetComponents<PlayMakerHashTableProxy>()) if (h.referenceName == "PriceGuide") priceGuide = h._hashTable;
                if (priceGuide == null) priceGuide = new System.Collections.Hashtable();
            }
            float baseP = 0f;
            object v = priceGuide[name.ToLowerInvariant()];
            if (v != null) float.TryParse(v.ToString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out baseP);
            if (baseP <= 0f) baseP = cat == "ENGINE" ? 120f : cat == "CHASSIS" ? 90f : cat == "BODY" ? 70f : cat == "ACCESSORIES" ? 60f : 40f;
            // (le guide = ce que paie le marche aux puces ; a la revente, plus cher, et moins usee = plus chere)
            float p = baseP * (1.3f + 0.9f * wear / 100f);
            return Mathf.Max(5, Mathf.RoundToInt(p / 5f) * 5);
        }

        static void DrawStock()
        {
            GameObject vl = PostChild("VINLIST_order");
            var lists = new Dictionary<string, System.Collections.ArrayList>();
            if (vl != null) foreach (PlayMakerArrayListProxy p in vl.GetComponents<PlayMakerArrayListProxy>()) if (p._arrayList != null) lists[p.referenceName ?? ""] = p._arrayList;
            System.Collections.ArrayList all;
            if (!lists.TryGetValue("VINS", out all) || all.Count == 0)
            {
                if (osatStock == null) { osatStock = new List<OsatUsed>(); osatStockDay = DaysPassed; L("magasin de pieces : liste des pieces (VINLIST_order) introuvable, pas d'occasion"); }
                return;
            }
            var pool = new List<string>();
            foreach (object o in all) { string s = o as string; if (!string.IsNullOrEmpty(s) && !pool.Contains(s) && VinSpawner(s) != null) pool.Add(s); }
            var stock = new List<OsatUsed>();
            var rnd = new System.Random();
            while (stock.Count < OsatStockSize && pool.Count > 0)
            {
                int i = rnd.Next(pool.Count);
                string vin = pool[i]; pool.RemoveAt(i);
                string cat = "";
                foreach (KeyValuePair<string, System.Collections.ArrayList> kv in lists) if (kv.Key != "VINS" && kv.Value.Contains(vin)) cat = kv.Key;
                float wear = Mathf.Round(15f + (float)rnd.NextDouble() * 55f);
                string name = PartName(Template(vin), vin);
                stock.Add(new OsatUsed { Vin = vin, Name = name, Wear = wear, Price = UsedPrice(name, cat, wear) });
            }
            osatStock = stock; osatStockDay = DaysPassed; osatDirty = true;
            L("magasin de pieces : nouveau stock d'occasion (jour " + osatStockDay + ") : " + string.Join(", ", stock.ConvertAll(x => x.Name + " " + x.Price).ToArray()));
            SendStock();
        }

        static void SendStock()
        {
            if (!Session.Active || !BagAuthority || osatStock == null) return;
            var w = new NetWriter(Msg.Job).U8(Session.LocalId).Str("@osat").U8(0).I32(osatStockDay).U8(osatStock.Count);
            foreach (OsatUsed u in osatStock) w.Str(u.Vin).Str(u.Name).F32(u.Wear).I32(u.Price).Bool(u.Sold);
            Session.SendAll(w, true);
        }

        // fait sortir la piece du distributeur du jeu, posee en pos ; before (rejeu) : compteurs de celui qui l'a achetee
        static GameObject SpawnUsed(string vin, float wear, Vector3 pos, Quaternion rot, List<KeyValuePair<string, int>> before, out List<KeyValuePair<string, int>> mine)
        {
            mine = new List<KeyValuePair<string, int>>();
            PlayMakerFSM sp = VinSpawner(vin);
            if (sp == null) { Log.Warn("magasin de pieces : distributeur de " + vin + " introuvable"); return null; }
            foreach (FsmInt v in sp.FsmVariables.IntVariables) mine.Add(new KeyValuePair<string, int>(v.Name, v.Value));
            if (before != null)
                foreach (KeyValuePair<string, int> kv in before)
                {
                    FsmInt v = sp.FsmVariables.FindFsmInt(kv.Key);
                    if (v == null || v.Value >= kv.Value) { if (v != null && v.Value > kv.Value) Log.Warn("magasin de pieces : " + vin + " " + kv.Key + " deja a " + v.Value + " ici (" + kv.Value + " chez l'acheteur), numero suivant"); continue; }
                    Log.Info("magasin de pieces : " + vin + " " + kv.Key + " " + v.Value + " -> " + kv.Value + " (rattrape celui de l'acheteur)");
                    v.Value = kv.Value;
                }
            FsmFloat minW = sp.FsmVariables.FindFsmFloat("MinimumWear");
            FsmGameObject spot = FsmVariables.GlobalVariables.FindFsmGameObject("PartSpawnPoint");
            float wasW = minW != null ? minW.Value : 0f;
            GameObject wasSpot = spot != null ? spot.Value : null;
            var tmp = new GameObject("osat_nouto");
            tmp.transform.position = pos; tmp.transform.rotation = rot;
            try
            {
                if (minW != null) minW.Value = wear;
                if (spot != null) spot.Value = tmp;
                sp.SendEvent("SPAWNITEM");
            }
            finally { if (minW != null) minW.Value = wasW; if (spot != null) spot.Value = wasSpot; UnityEngine.Object.Destroy(tmp); }
            FsmGameObject nv = sp.FsmVariables.FindFsmGameObject("New");
            GameObject go = nv != null ? nv.Value : null;
            if (go == null) { Log.Warn("magasin de pieces : rien de cree par le distributeur de " + vin); return null; }
            go.transform.position = pos; go.transform.rotation = rot;
            Rigidbody rb = go.GetComponent<Rigidbody>();
            if (rb != null) { rb.position = pos; rb.rotation = rot; if (!rb.isKinematic) { rb.velocity = Vector3.zero; rb.angularVelocity = Vector3.zero; } }
            SetWear(go, wear);
            osatWear.Add(new KeyValuePair<GameObject, float>(go, wear)); osatWearUntil = Time.realtimeSinceStartup + 3f;
            Props.Track(go);
            return go;
        }

        static void SetWear(GameObject go, float wear)
        {
            PlayMakerFSM d = Game.FsmOn(go, "Data");
            if (d == null) return;
            FsmFloat w = d.FsmVariables.FindFsmFloat("Wear");
            if (w != null) w.Value = wear;
            FsmFloat mn = d.FsmVariables.FindFsmFloat("WearMin");
            if (mn != null) mn.Value = Mathf.Min(mn.Value, wear);
        }

        // (l'init de la piece tire son usure a son premier passage : remise a celle du stock pendant 3 s)
        static void TickOsatWear(float now)
        {
            if (osatWear.Count == 0) return;
            foreach (KeyValuePair<GameObject, float> kv in osatWear) if (kv.Key != null) SetWear(kv.Key, kv.Value);
            if (now <= osatWearUntil) return;
            foreach (KeyValuePair<GameObject, float> kv in osatWear)
            {
                PlayMakerFSM d = kv.Key != null ? Game.FsmOn(kv.Key, "Data") : null;
                FsmFloat w = d != null ? d.FsmVariables.FindFsmFloat("Wear") : null;
                if (kv.Key != null) L("magasin de pieces : " + kv.Key.name + " usure " + (w != null ? w.Value.ToString("F0") : "?") + " (stock " + kv.Value.ToString("F0") + ")");
            }
            osatWear.Clear();
        }

        // ---------------------------------------------------------------- reseau (@osat, Msg.Job)
        public static void OnOsat(int who, NetReader r)
        {
            int op = r.U8();
            if (op == 0)
            {
                int day = r.I32(), n = r.U8();
                var l = new List<OsatUsed>();
                for (int i = 0; i < n; i++) l.Add(new OsatUsed { Vin = r.Str(), Name = r.Str(), Wear = r.F32(), Price = r.I32(), Sold = r.Bool() });
                if (BagAuthority) return;
                osatStock = l; osatStockDay = day; osatDirty = true;
                L("magasin de pieces : stock d'occasion recu de l'hote (" + n + " pieces, jour " + day + ")");
                return;
            }
            if (op == 1)
            {
                int idx = r.U8(); string vin = r.Str(); float wear = r.F32(); Vector3 pos = r.Vec(); Quaternion rot = r.Quat();
                int nb = r.U8();
                var before = new List<KeyValuePair<string, int>>();
                for (int i = 0; i < nb; i++) before.Add(new KeyValuePair<string, int>(r.Str(), r.I32()));
                if (osatStock != null && idx < osatStock.Count && osatStock[idx].Vin == vin) { osatStock[idx].Sold = true; osatDirty = true; }
                List<KeyValuePair<string, int>> mine;
                GameObject go = SpawnUsed(vin, wear, pos, rot, before, out mine);
                L("magasin de pieces : piece d'occasion " + vin + " achetee par #" + who + (go != null ? ", recree ici : " + go.name : ", PAS recreee ici"));
                return;
            }
            if (op == 2)
            {
                string id = r.Str(); Vector3 pos = r.Vec(); Quaternion rot = r.Quat();
                OsatModItem it = osatModItems.Find(x => x.Id == id);
                if (it == null) { Log.Warn("magasin de pieces : objet de mod '" + id + "' achete par #" + who + ", mod absent ici"); return; }
                L("magasin de pieces : '" + id + "' achete par #" + who);
                InvokeMod(it, who, false, pos, rot);
                return;
            }
            if (op == 4 && BagAuthority) SendStock();
        }

        // ---------------------------------------------------------------- sauvegarde (hote)
        static string OsatFile { get { return WorldFiles.PathOf("MWCoop-osat.txt"); } }

        public static void OnGameSaveOsat()
        {
            if (osat == null || osatStock == null || !BagAuthority) return;
            try
            {
                var sb = new System.Text.StringBuilder();
                sb.Append("jour;").Append(osatStockDay).Append('\n');
                var ci = System.Globalization.CultureInfo.InvariantCulture;
                foreach (OsatUsed u in osatStock) sb.Append(u.Vin).Append(';').Append(u.Wear.ToString(ci)).Append(';').Append(u.Price).Append(';').Append(u.Sold ? 1 : 0).Append('\n');
                File.WriteAllText(OsatFile, sb.ToString());
            }
            catch (Exception e) { L("magasin de pieces : sauvegarde " + e.Message); }
        }

        static void LoadOsat()
        {
            if (!File.Exists(OsatFile)) return;
            try
            {
                var ci = System.Globalization.CultureInfo.InvariantCulture;
                var l = new List<OsatUsed>(); int day = -1;
                foreach (string line in File.ReadAllLines(OsatFile))
                {
                    string[] a = line.Split(';');
                    if (a.Length == 2 && a[0] == "jour") { int.TryParse(a[1], out day); continue; }
                    if (a.Length < 4) continue;
                    var u = new OsatUsed { Vin = a[0], Wear = float.Parse(a[1], ci), Price = int.Parse(a[2], ci), Sold = a[3] == "1" };
                    u.Name = PartName(Template(u.Vin), u.Vin);
                    l.Add(u);
                }
                if (day >= 0 && l.Count > 0) { osatStock = l; osatStockDay = day; osatDirty = true; L("magasin de pieces : stock d'occasion recharge (" + l.Count + " pieces, jour " + day + ")"); }
            }
            catch (Exception e) { L("magasin de pieces : lecture du stock " + e.Message); }
        }
    }
}
