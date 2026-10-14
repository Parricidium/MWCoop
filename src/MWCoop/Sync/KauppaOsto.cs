using System;
using System.Collections.Generic;
using System.IO;
using HutongGames.PlayMaker;
using MWCoop.Net;
using UnityEngine;

namespace MWCoop
{
    // Boutique : achats comme dans les magasins du jeu (releve sur la PSK, 12/10, demande de JD) :
    //  - en rayon : "SAC DE COUCHAGE 149.00 MK" ; clic gauche = je prends (l'article disparait du rayon), clic droit = je
    //    repose ;
    //  - a la caisse : "PRICE TOTAL ... MK" (et sur l'ecran de la caisse) ; clic = je paie (PlayerMoney ; sinon "NOT
    //    ENOUGH MONEY" et le sous-titre du vendeur), son de caisse, le rayon se regarnit ;
    //  - les achats arrivent dans un sac de courses a notre logo (modele du sac du jeu, notre calque sur sa texture) :
    //    objet du jeu (couche 19, etiquette PART : on le prend en main) ; F bref = un article sort, F tenu 1,5 s
    //    ("OPENING THE BAG...") = tout sort ; vide, il disparait. Les articles (sacs de couchage) sont crees par l'hote
    //    (SleepingBags.Purchase). Le sac est vu de tous : cree / vide chez chacun (@kassi), pose suivie par Props
    //    (cle "pussi:<id>") ; l'hote (ou le joueur seul) garde la liste (MWCoop-pussit.txt) et la renvoie aux invites.
    //  - porte : clic sur la porte = ouvrir / fermer (son de la porte de magasin du jeu), pour tous (@kovi).
    public static partial class Kauppa
    {
        // ---------------------------------------------------------------- panier et caisse
        static float basketTotal;
        static Transform registerT;
        static Collider registerCol;
        static TextMesh registerText;
        static FsmString guiSub;
        static float subUntil = -1f;
        static string subText;

        static void Say(string fr, string en)
        {
            if (guiSub == null) guiSub = FsmVariables.GlobalVariables.FindFsmString("GUIsubtitle");
            subText = Lang.TG(fr, en);
            if (guiSub != null) guiSub.Value = subText; else Hud.Toast(subText);
            subUntil = Time.realtimeSinceStartup + 3.5f;
        }

        static void TickSay()
        {
            if (subUntil > 0f && Time.realtimeSinceStartup >= subUntil)
            {
                subUntil = -1f;
                if (guiSub != null && guiSub.Value == subText) guiSub.Value = "";
            }
        }

        // Ecran de la caisse : texte comme celui de la PSK (meme police, meme place sur la caisse).
        static void RegisterScreen(GameObject reg, GameObject regSrc)
        {
            registerT = reg.transform;
            Bounds rb = LocalBounds(reg);
            var cg = new GameObject("osuus_kassa");
            cg.layer = 2;
            cg.transform.SetParent(root.transform, false);
            cg.transform.localPosition = rb.center;
            var bc = cg.AddComponent<BoxCollider>(); bc.isTrigger = true; bc.size = rb.size + new Vector3(0.08f, 0.08f, 0.08f);
            registerCol = bc;
            Transform logic = regSrc.transform.Find("CashRegisterLogic");
            TextMesh src = logic != null ? logic.GetComponent<TextMesh>() : null;
            if (src == null) { L("caisse : ecran de la PSK introuvable"); return; }
            var tg = new GameObject("osuus_kassa_naytto");
            tg.transform.SetParent(reg.transform, false);
            tg.transform.localPosition = regSrc.transform.InverseTransformPoint(logic.position);
            tg.transform.localRotation = Quaternion.Inverse(regSrc.transform.rotation) * logic.rotation;
            Vector3 ls = logic.lossyScale, ss = regSrc.transform.lossyScale;
            tg.transform.localScale = new Vector3(ls.x / ss.x, ls.y / ss.y, ls.z / ss.z);
            var tm = tg.AddComponent<TextMesh>();
            tm.font = src.font; tm.fontSize = src.fontSize; tm.characterSize = src.characterSize; tm.anchor = src.anchor;
            tm.alignment = src.alignment; tm.color = src.color; tm.lineSpacing = src.lineSpacing; tm.offsetZ = src.offsetZ;
            var mr = tg.GetComponent<MeshRenderer>();
            Renderer sr = logic.GetComponent<Renderer>();
            if (mr != null && sr != null) mr.sharedMaterials = sr.sharedMaterials;
            tm.text = "0.00";
            registerText = tm;
        }

        static void ShowTotal() { if (registerText != null) registerText.text = basketTotal.ToString("F2", System.Globalization.CultureInfo.InvariantCulture); }

        static void Take(Offer o)
        {
            if (o.Taken >= o.Shown.Count) { Say("Myyjä : il n'y en a plus en rayon, passe à la caisse.", "Shopkeeper: that's all on the shelf, come to the till."); return; }
            o.Shown[o.Taken].SetActive(false);
            o.Taken++;
            basketTotal += o.Price;
            ShowTotal();
        }

        static void PutBack(Offer o)
        {
            if (o.Taken <= 0) return;
            o.Taken--;
            o.Shown[o.Taken].SetActive(true);
            basketTotal -= o.Price;
            if (basketTotal < 0.01f) basketTotal = 0f;
            ShowTotal();
        }

        static void Pay()
        {
            float now = Time.realtimeSinceStartup;
            if (now - lastBuy < 0.7f) return;
            lastBuy = now;
            if (basketTotal <= 0f) return;
            if (money == null || money.Value < basketTotal)
            {
                if (guiText != null) guiText.Value = "NOT ENOUGH MONEY";
                Say("Myyjä : tu n'as pas assez d'argent pour tout ça.", "Shopkeeper: you are too poor for these purchases.");
                return;
            }
            money.Value -= basketTotal;   // (Wallet : une depense reste au joueur)
            int count = 0;
            var items = new List<string>();
            foreach (Offer o in offers) { count += o.Taken; for (int k = 0; k < o.Taken; k++) items.Add(o.Item); o.Taken = 0; foreach (GameObject g in o.Shown) if (g != null) g.SetActive(true); }
            L("achat : " + count + " article(s), " + basketTotal.ToString("F2") + " mk, reste " + money.Value.ToString("F0") + " mk");
            basketTotal = 0f;
            ShowTotal();
            Vector3 at = registerT != null ? registerT.position : root.transform.position;
            try { MasterAudio.PlaySound3DAtVector3AndForget("Store", at, 1f, null, 0f, "cash_register_2"); } catch { }
            // le sac de courses, sur le tapis du comptoir cote client
            Bounds cb = counterBounds.size.sqrMagnitude > 0f ? counterBounds : new Bounds(new Vector3(3.3f, 0.5f, -1.2f), new Vector3(3.4f, 1.05f, 1.6f));
            Vector3 p = root.transform.TransformPoint(new Vector3(cb.min.x + 0.8f, CounterTop + 0.3f, cb.max.z - 0.4f));
            NewBag(p, root.transform.rotation * Quaternion.Euler(0f, 180f, 0f), items);
            Say("Myyjä : Kiitos ! Bonne nuit au chaud.", "Shopkeeper: Kiitos! Sleep warm.");
        }

        // ---------------------------------------------------------------- porte
        static bool doorOpen;
        static Collider doorCol;

        static void ToggleDoor(bool open, bool send)
        {
            if (open == doorOpen) return;
            doorOpen = open;
            if (door != null) try { MasterAudio.PlaySound3DAtVector3AndForget("Store", door.position, 1f, null, 0f, open ? "door_open" : "door_close"); } catch { }
            if (send && Session.Active) Session.SendAll(new NetWriter(Msg.Job).U8(Session.LocalId).Str("@kovi").Bool(open), true);
        }

        public static void OnDoor(int who, bool open) { if (root != null) ToggleDoor(open, false); }

        // ---------------------------------------------------------------- sacs de courses
        // (Items : sorte de chaque article dans le sac, "makuupussi", "vhs", "nauhuri", "tv")
        class Pussi { public string Id; public GameObject Go; public Rigidbody Rb; public BoxCollider Col; public readonly List<string> Items = new List<string>(); public int Count { get { return Items.Count; } } }
        static readonly List<Pussi> pussit = new List<Pussi>();
        static Mesh pussiMesh;
        static Quaternion pussiRot = Quaternion.identity;
        static bool pussiLogged;

        // essais : texture principale d'une matiere du jeu -> dumps\tex-<nom>.png
        static void DumpTex(string name)
        {
            Material m = null;
            foreach (Material x in Resources.FindObjectsOfTypeAll<Material>()) if (x.name == name) { m = x; break; }
            if (m == null || m.mainTexture == null) { L("essai : matiere " + name + " sans texture"); return; }
            Texture s = m.mainTexture;
            var rt = RenderTexture.GetTemporary(s.width, s.height, 0, RenderTextureFormat.ARGB32);
            RenderTexture prev = RenderTexture.active;
            Graphics.Blit(s, rt);
            RenderTexture.active = rt;
            var tex = new Texture2D(s.width, s.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, s.width, s.height), 0, 0);
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            string dir = Path.Combine(Log.DataDir, "dumps");
            Directory.CreateDirectory(dir);
            File.WriteAllBytes(Path.Combine(dir, "tex-" + name + ".png"), tex.EncodeToPNG());
            UnityEngine.Object.Destroy(tex);
            L("essai : texture " + name + " sortie (" + s.width + "x" + s.height + ")");
        }
        static Material pussiMat;
        static bool pussiTried;
        static Pussi lookedBag;
        static float fDown = -1f;
        static float nextBagSync;

        public static string KeyOf(Rigidbody rb)
        {
            foreach (Pussi b in pussit) if (b.Rb == rb) return "pussi:" + b.Id;
            return null;
        }

        static bool BagModel()
        {
            if (pussiTried) return pussiMesh != null;
            pussiTried = true;
            GameObject src = FindPath("shoppingbag");
            SkinnedMeshRenderer[] sms = src != null ? src.GetComponentsInChildren<SkinnedMeshRenderer>(true) : new SkinnedMeshRenderer[0];
            SkinnedMeshRenderer sm = sms.Length > 0 ? sms[0] : null;
            if (sm == null || sm.sharedMesh == null) { L("sac de courses : modele du jeu introuvable"); return false; }
            pussiMesh = sm.sharedMesh;
            pussiRot = Quaternion.Inverse(src.transform.rotation) * sm.transform.rotation;
            Material tpl = sm.sharedMaterial;
            pussiMat = new Material(tpl) { name = "kauppa_pussi" };
            Texture2D lay = Res("pussi.png", TextureFormat.ARGB32);
            if (tpl.mainTexture != null && lay.width > 2)
            {
                Texture s = tpl.mainTexture;
                int w = s.width, h = s.height;
                var rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32);
                RenderTexture prev = RenderTexture.active;
                Graphics.Blit(s, rt);
                RenderTexture.active = rt;
                var tex = new Texture2D(w, h, TextureFormat.RGB24, true) { name = "kauppa_pussi" };
                tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                RenderTexture.active = prev;
                RenderTexture.ReleaseTemporary(rt);
                Color[] px = tex.GetPixels();
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                    {
                        Color c = lay.GetPixelBilinear((x + 0.5f) / w, (y + 0.5f) / h);
                        int i = y * w + x;
                        px[i] = Color.Lerp(px[i], new Color(c.r, c.g, c.b, 1f), c.a);
                    }
                tex.SetPixels(px); tex.Apply(true);
                pussiMat.mainTexture = tex;
            }
            UnityEngine.Object.Destroy(lay);
            return true;
        }

        static Pussi FindBag(string id) { foreach (Pussi b in pussit) if (b.Id == id) return b; return null; }

        static Pussi CreateBag(string id, Vector3 pos, Quaternion rot, List<string> items)
        {
            if (!BagModel()) return null;
            var go = new GameObject("shopping bag osuuskauppa(itemx)");
            go.transform.position = pos;
            var model = new GameObject("Model");
            model.transform.SetParent(go.transform, false);
            model.transform.localRotation = pussiRot;   // (pose du maillage dans l'objet du jeu)
            model.AddComponent<MeshFilter>().sharedMesh = pussiMesh;
            model.AddComponent<MeshRenderer>().sharedMaterial = pussiMat;
            // debout : le modele du jeu est couche (ses os le redressent) ; sa plus grande dimension passe a la verticale,
            // anses en haut ([Boutique] SacRetourne=1 si elles etaient en bas)
            Bounds lb = model.GetComponent<MeshRenderer>().bounds;   // (objet encore sans rotation : repere monde = le sien)
            Vector3 sz = lb.size;
            Vector3 longAxis = sz.x >= sz.y && sz.x >= sz.z ? Vector3.right : sz.z >= sz.y ? Vector3.forward : Vector3.up;
            Quaternion stand = Quaternion.FromToRotation(longAxis, Config.GetInt("Boutique", "SacRetourne", 0) != 0 ? Vector3.down : Vector3.up);
            model.transform.localRotation = stand * model.transform.localRotation;
            if (!pussiLogged) { pussiLogged = true; L("sac de courses : modele " + sz.ToString("F2") + ", redresse depuis l'axe " + longAxis); }
            var b = new Pussi { Id = id, Go = go };
            b.Items.AddRange(items);
            Bounds mb = model.GetComponent<MeshRenderer>().bounds;
            b.Col = go.AddComponent<BoxCollider>(); b.Col.center = go.transform.InverseTransformPoint(mb.center); b.Col.size = mb.size;
            go.transform.rotation = rot;
            b.Rb = go.AddComponent<Rigidbody>(); b.Rb.mass = 1.5f;
            go.layer = 19; go.tag = "PART";   // (objet du jeu : la main le prend)
            pussit.Add(b);
            Props.SoonScan();
            return b;
        }

        static void NewBag(Vector3 pos, Quaternion rot, List<string> items)
        {
            string id = "p" + Session.LocalId + "-" + DateTime.Now.Ticks.ToString("x");
            Pussi b = CreateBag(id, pos, rot, items);
            if (b == null) { for (int i = 0; i < items.Count; i++) SpawnKind(items[i], pos + Vector3.up * 0.25f * i, rot); return; }   // (sans modele : articles directs)
            SendBag(b);
            L("sac de courses " + id + " : " + string.Join(", ", items.ToArray()));
        }

        // un article sorti du sac (ou achete sans sac)
        static void SpawnKind(string kind, Vector3 p, Quaternion q)
        {
            if (kind == "makuupussi") SleepingBags.Purchase(p, q);
            else SpawnItem(kind, kind == KVhs ? "tyhja" : "", p, q);
        }

        static List<string> ParseItems(string s, int count)
        {
            var l = new List<string>();
            if (!string.IsNullOrEmpty(s)) foreach (string k in s.Split(',')) if (k.Length > 0) l.Add(k);
            while (l.Count < count) l.Add("makuupussi");   // (sacs d'avant 0.84 : seulement des sacs de couchage)
            return l;
        }

        static void SendBag(Pussi b)
        {
            if (!Session.Active) return;
            Session.SendAll(new NetWriter(Msg.Job).U8(Session.LocalId).Str("@kassi").Str(b.Id).U8(b.Count).Vec(b.Go != null ? b.Go.transform.position : Vector3.zero).Quat(b.Go != null ? b.Go.transform.rotation : Quaternion.identity).Str(string.Join(",", b.Items.ToArray())), true);
        }

        public static void OnBag(int who, NetReader r)
        {
            string id = r.Str(); int count = r.U8(); Vector3 p = r.Vec(); Quaternion q = r.Quat();
            List<string> items = ParseItems(r.More ? r.Str() : "", count);
            if (root == null) return;
            Pussi b = FindBag(id);
            if (count <= 0) { if (b != null) RemoveBag(b); }
            else if (b == null) CreateBag(id, p, q, items);
            else { b.Items.Clear(); b.Items.AddRange(items); }
        }

        static void RemoveBag(Pussi b)
        {
            if (lookedBag == b) { ClearGui(); lookedBag = null; }
            if (b.Go != null) UnityEngine.Object.Destroy(b.Go);
            pussit.Remove(b);
        }

        static float heldFDown = -1f;

        // sac de courses dans la main du joueur d'ici
        static Pussi HeldBag()
        {
            foreach (Pussi b in pussit) if (b.Go != null && b.Go.transform.parent != null && Game.UnderPlayer(b.Go.transform)) return b;
            return null;
        }

        // Un article (ou tous) sort du sac, pose a cote (tenu en main : devant le joueur) ; vide, le sac disparait (tenu :
        // une fois lache, pour ne pas detruire l'objet que tient la main du jeu).
        static void TakeOut(Pussi b, bool all)
        {
            int n = all ? b.Count : 1;
            Transform t = b.Go.transform;
            bool held = t.parent != null;
            Transform eye = held ? EyeT : null;
            Vector3 fw = eye != null ? new Vector3(eye.forward.x, 0f, eye.forward.z) : Vector3.zero;
            if (fw.sqrMagnitude < 1e-4f) fw = Vector3.forward;
            fw.Normalize();
            for (int i = 0; i < n && b.Count > 0; i++)
            {
                string kind = b.Items[0];
                b.Items.RemoveAt(0);
                Vector3 p = eye != null ? eye.position + fw * 0.9f + Vector3.up * (-0.3f + 0.3f * i)
                                        : t.position + Vector3.up * (0.35f + 0.3f * i) + t.right * 0.35f;
                SpawnKind(kind, p, eye != null ? Quaternion.LookRotation(fw) : Quaternion.Euler(0f, t.eulerAngles.y + 90f, 0f));
            }
            L("sac de courses " + b.Id + " : " + n + " article(s) sorti(s)" + (held ? " (en main)" : "") + ", reste " + b.Count);
            SendBag(b);
            if (b.Count <= 0 && !held) RemoveBag(b);
        }

        static bool BagAuthority { get { return !Session.Active || Session.IsHost; } }
        static string BagFile { get { return WorldFiles.PathOf("MWCoop-pussit.txt"); } }

        static void SaveBags()
        {
            if (!BagAuthority) return;
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            var sb = new System.Text.StringBuilder();
            foreach (Pussi b in pussit)
            {
                if (b.Go == null || b.Count <= 0) continue;
                Vector3 p = b.Go.transform.position; Quaternion q = b.Go.transform.rotation;
                sb.Append(b.Id).Append(';').Append(b.Count);
                foreach (float f in new[] { p.x, p.y, p.z, q.x, q.y, q.z, q.w }) sb.Append(';').Append(f.ToString("R", ci));
                sb.Append(';').Append(string.Join(",", b.Items.ToArray()));
                sb.Append('\n');
            }
            try { File.WriteAllText(BagFile, sb.ToString()); } catch (Exception e) { L("sacs de courses : ecriture " + e.Message); }
        }

        static bool bagsLoaded;
        // Sauvegarde du jeu (SaveHook) : seul moment ou les sacs de courses s'ecrivent (JD, 14/10) ; boutique coupee : rien.
        public static void OnGameSaveBags() { if (bagsLoaded) SaveBags(); }

        static void LoadBags()
        {
            bagsLoaded = true;
            WorldFiles.Check(Path.GetDirectoryName(BagFile));
            if (!BagAuthority || !File.Exists(BagFile)) return;
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            try
            {
                foreach (string l in File.ReadAllLines(BagFile))
                {
                    string[] c = l.Split(';');
                    if (c.Length < 9) continue;
                    CreateBag(c[0], new Vector3(float.Parse(c[2], ci), float.Parse(c[3], ci), float.Parse(c[4], ci)),
                              new Quaternion(float.Parse(c[5], ci), float.Parse(c[6], ci), float.Parse(c[7], ci), float.Parse(c[8], ci)), ParseItems(c.Length > 9 ? c[9] : "", int.Parse(c[1])));
                }
                if (pussit.Count > 0) L("sacs de courses : " + pussit.Count + " repris de la sauvegarde");
            }
            catch (Exception e) { L("sacs de courses : lecture " + e.Message); }
        }

        static void TickBags(float now)
        {
            // sac vide sorti en main : retire une fois lache
            for (int i = pussit.Count - 1; i >= 0; i--)
                if (pussit[i].Count <= 0 && (pussit[i].Go == null || pussit[i].Go.transform.parent == null)) RemoveBag(pussit[i]);
            if (BagAuthority && pussit.Count > 0)
            {
                if (Session.Active && Session.RemoteCount > 0 && now >= nextBagSync) { nextBagSync = now + 10f; foreach (Pussi b in pussit) SendBag(b); }
            }
        }

        static void ClearGui()
        {
            FsmBool asm = FsmVariables.GlobalVariables.FindFsmBool("GUIassemble");
            if (asm != null) asm.Value = false;
            if (guiBuy != null) guiBuy.Value = false;
            if (guiUse != null) guiUse.Value = false;
            if (guiText != null) guiText.Value = "";
        }
    }
}
