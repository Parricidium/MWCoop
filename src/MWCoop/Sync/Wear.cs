using System.Collections.Generic;
using System.IO;
using HutongGames.PlayMaker;
using MWCoop.Net;
using UnityEngine;

namespace MWCoop
{
    // Vetements portes : veste d'hiver, combinaison, casque. Le jeu n'en a qu'un exemplaire de chacun ; les mettre les
    // range sous PLAYER (inactifs) chez celui qui les porte ; chez les autres ils restaient au crochet, prenables une 2e
    // fois, et l'avatar ne montrait rien.
    //  - une veste, une combinaison et un casque par joueur (demande de JD, 11/10 : « X paires selon X joueurs » ; Jomppi en
    //    voulait 2 ; casque : ᏒullΞᏒ, 12/10, « deux de chaque objet ») : l'hote copie la veste, la combinaison et le casque
    //    du jeu pour chaque joueur en plus (veste 2, combinaison 2, casque 2...), posees
    //    dans la maison du debut de partie ; leur nombre ne baisse pas pendant la partie. Liste et poses envoyees aux
    //    invites (@vet), gardees a cote de la sauvegarde de l'hote (MWCoop-vetements.txt). Chaque copie a ses propres
    //    etiquettes de sauvegarde du jeu (JacketPosMWC2...) : elle n'ecrase pas celles de l'originale.
    //  - qui porte quoi : message fiable (vetement, porte ou non, pose la ou il a ete enleve), redit toutes les 5 s tant
    //    qu'il est porte (arrivee en cours de partie, message perdu) ; perime apres 15 s ou au depart du joueur. Les bits
    //    de l'etat du joueur (PlayerSync.F_Jacket, F_Coverall, F_Helmet) ne disent plus que le genre porte, pour l'avatar.
    //  - chez les autres, le vetement porte par un autre est cache (desactive : ni visible ni prenable) ; enleve, il
    //    reapparait la ou il a ete enleve (a defaut la ou il etait, ex. joueur parti en le portant).
    //  - porte par deux joueurs a la fois (sauvegarde de l'hote faite en le portant, chargee par un invite ; deux clics
    //    croises) : le plus petit numero (l'hote d'abord) le garde, l'autre l'enleve (logique du jeu).
    // Detection par sondage 4 fois/s (sans allocation) : l'objet porte est sous PLAYER et inactif ; tenu en main il est
    // sous PLAYER mais actif (ItemPivot). "Sous PLAYER" = descendant (IsChildOf), pas racine : au volant ou passager,
    // PLAYER lui-meme est range sous la voiture.
    public static class Wear
    {
        // (3 : tasse a cafe -- pas un vetement : jamais « portee », juste une par joueur ; ᏒullΞᏒ, 12/10)
        const int NBase = 4;
        static readonly string[] Paths = { "EQUIPMENTS/winter jacket(itemx)", "EQUIPMENTS/winter coverall(itemx)", "EQUIPMENTS/Helmet/helmet(itemx)", "EQUIPMENTS/coffee cup(itemx)" };
        static readonly string[] Kinds = { "veste", "combinaison", "casque", "tasse" };
        static readonly string[] CopyNames = { "winter jacket", "winter coverall", "helmet", "coffee cup" };
        const int PerSet = 4;   // (par joueur en plus : veste, combinaison, casque, tasse)
        static readonly string[] UtPos = { "JacketPos", "CoverallPos" }, UtState = { "JacketState", "CoverallState" };
        static readonly int[] Bits = { PlayerSync.F_Jacket, PlayerSync.F_Coverall, PlayerSync.F_Helmet, 0 };
        // Depuis PLAYER : au volant ou passager, PLAYER est range sous la voiture (DriveTrigger 'Reset view', Seats) et
        // n'est plus une racine de la scene (Game.FindAny ne le trouverait pas).
        const string ClothingPath = "Pivot/AnimPivot/Camera/FPSCamera/FPSCamera/Clothing";
        const string HelmetPath = "Pivot/AnimPivot/Camera/FPSCamera/FPSCamera/Helmet";

        class Item
        {
            public GameObject Go; public int Kind; public string Name, Key;
            public bool LocalWorn, Hidden, HidActive;
            public Transform HidParent; public Vector3 HidPos; public Quaternion HidRot; public float HidAt;
            public int WornBy = -1; public float WornAt;                          // porte par un autre (message fiable, redit toutes les 5 s)
            public int OffWho = -1; public Vector3 OffPos; public Quaternion OffRot; public float OffAt = -1;  // pose ou un autre l'a enleve
            public int LastWearer = -1;                                           // dernier porteur vu
            public float YieldAt = -100, HeldLog = -100, SentAt;
            public Vector3 FreePos, FreeEul;                                      // (casque achete : derniere pose libre, sauvegardee)
        }

        // 0 veste, 1 combinaison, 2 casque, 3 tasse, puis par joueur k >= 2 : veste k, combinaison k, casque k, tasse k.
        static readonly List<Item> items = new List<Item>();
        static int pairs = 1;
        static float loadedAt;   // (copies faites 12 s apres le chargement : casque et tasse trouves d'ici la)
        static PlayMakerFSM clothLogic, helmetLogic;
        static Transform player;   // PLAYER (par reference : pas de nom lu 12 fois/s), garde une fois trouve
        static float nextPoll, findAt = -1, nextList;
        static bool found, fileRead;
        static readonly List<Vector3[]> filePoses = new List<Vector3[]>();   // (hote) poses des copies lues du fichier
        static int swapKind = -1; static float swapAt, swapUntil;            // vetement rendu : une copie libre a mettre a la place

        static Wear() { Reset(); }

        static void Reset()
        {
            items.Clear();
            for (int i = 0; i < NBase; i++) items.Add(new Item { Kind = i, Name = Kinds[i], Key = "w:vetement:" + Kinds[i] });
            pairs = 1;
        }

        public static void OnLevelLoaded()
        {
            Reset();
            clothLogic = helmetLogic = null;
            helmetHooked.Clear(); cupSeen.Clear(); cupResend = 0f;
            osto.Clear(); ostoFile.Clear(); ostoRead = false;
            player = null;
            found = fileRead = false;
            filePoses.Clear();
            nextList = 0f;
            swapKind = -1;
            step = seatStep = 0; testLog = 0; testSeat = null;
            // Tout de suite (avant que le jeu ne range sous PLAYER un vetement porte a la sauvegarde), puis 5 s plus
            // tard pour ce qui manquerait.
            loadedAt = Time.realtimeSinceStartup;
            if (PlayerSync.InGame) { Find(); findAt = Time.realtimeSinceStartup + 5f; }
            else findAt = -1;
        }

        static void Find()
        {
            found = true;
            for (int i = 0; i < NBase; i++)
            {
                if (items[i].Go != null) continue;
                items[i].Go = Game.FindAny(Paths[i]);
                if (items[i].Go == null) found = false;
            }
            // PLAYER par son nom seul (GameObject.Find cherche alors partout, pas seulement les racines) : trouve
            // aussi quand le joueur est assis dans une voiture ; une fois trouve, garde (jamais remis a null ici).
            if (player == null) { GameObject p = Game.PlayerGo; if (p != null) player = p.transform; }
            if (player == null) { found = false; return; }
            if (clothLogic == null) { Transform c = player.Find(ClothingPath); if (c != null) clothLogic = Game.FsmOn(c.gameObject, "Logic"); }
            if (helmetLogic == null) { Transform h = player.Find(HelmetPath); if (h != null) helmetLogic = Game.FsmOn(h.gameObject, "Logic"); }
            // Deja range sous PLAYER (porte au chargement) : l'automate du jeu sait lequel.
            if (helmetLogic != null && items[2].Go == null) items[2].Go = GoVar(helmetLogic, "Helmet");
            if (clothLogic != null)
            {
                GameObject cl = GoVar(clothLogic, "Cloth") ?? GoVar(clothLogic, "NewCloth");
                if (cl != null) for (int i = 0; i < 2; i++) if (items[i].Go == null && cl.name == Paths[i].Substring(Paths[i].LastIndexOf('/') + 1)) items[i].Go = cl;
            }
        }

        // Casque mis (son 'Use', etat "State 3", juste avant EQUIP) : la logique du joueur prend CE casque-la (copie ou
        // celui du jeu), elle range, montre et rend l'objet de sa variable {Helmet}.
        class HelmetHook : ModHook
        {
            public override string Module { get { return "vetements (casque)"; } }
            public GameObject Go;
            public override void OnEnter()
            {
                try
                {
                    if (helmetLogic == null && player != null) { Transform h = player.Find(HelmetPath); if (h != null) helmetLogic = Game.FsmOn(h.gameObject, "Logic"); }
                    FsmGameObject v = helmetLogic != null ? helmetLogic.FsmVariables.FindFsmGameObject("Helmet") : null;
                    if (v != null && Go != null) v.Value = Go;
                }
                catch (System.Exception e) { Replay.HookError(e); }
                Finish();
            }
        }
        static readonly HashSet<GameObject> helmetHooked = new HashSet<GameObject>();
        static void HookHelmets()
        {
            foreach (Item it in AllItems())
            {
                if (it.Kind != 2 || it.Go == null || helmetHooked.Contains(it.Go)) continue;
                PlayMakerFSM use = Game.FsmOn(it.Go, "Use");
                if (use == null || use.Fsm == null) continue;
                FsmState st = use.Fsm.GetState("State 3");
                if (st == null || !st.IsInitialized || st.Actions == null) continue;
                FsmHooks.Prepend(st, new HelmetHook { Go = it.Go });
                helmetHooked.Add(it.Go);
            }
        }

        static GameObject GoVar(PlayMakerFSM f, string name)
        {
            FsmGameObject v = f.FsmVariables.FindFsmGameObject(name);
            return v != null ? v.Value : null;
        }

        // Cle fixe des vetements pour Props (ils changent de parent : EQUIPMENTS, PLAYER, racine de la scene).
        // Triche /vetements (Maxim Pennis, 17/10) : ma veste, ma combinaison et mon casque -- le dernier exemplaire que j'ai
        // porte, sinon un exemplaire que personne ne porte -- poses devant moi. Rend le nombre d'objets ramenes.
        public static int BringMine()
        {
            Transform me = Game.PlayerT;
            if (me == null) return 0;
            int n = 0, meId = Session.Active ? Session.LocalId : 0;
            for (int kind = 0; kind < 3; kind++)
            {
                Item pick = null;
                foreach (Item it in items) if (it.Kind == kind && it.Go != null && !it.LocalWorn && it.WornBy < 0 && it.LastWearer == meId) { pick = it; break; }
                if (pick == null) foreach (Item it in items) if (it.Kind == kind && it.Go != null && !it.LocalWorn && it.WornBy < 0 && !it.Hidden) { pick = it; break; }
                if (pick == null) continue;
                Vector3 fwd = me.forward; fwd.y = 0f; fwd = fwd.sqrMagnitude > 1e-4f ? fwd.normalized : Vector3.forward;
                Vector3 at = me.position + fwd * 0.9f + Vector3.up * (1.1f + 0.15f * n) + Vector3.Cross(Vector3.up, fwd) * (0.3f * (n - 1));
                Rigidbody rb = pick.Go.GetComponent<Rigidbody>();
                if (rb != null) { rb.velocity = Vector3.zero; rb.angularVelocity = Vector3.zero; }
                pick.Go.transform.position = at;
                Log.Info("vetements : " + Kinds[kind] + " (" + pick.Name + ") ramene devant moi");
                n++;
            }
            if (n > 0) Props.SoonScan();
            return n;
        }

        public static string KeyOf(GameObject go)
        {
            foreach (Item it in AllItems()) if (it.Go != null && it.Go == go) return it.Key;
            return null;
        }

        // Objet du vetement i (0 veste, 1 combinaison, 2 casque), pour l'avatar (copie du casque).
        public static GameObject ItemObject(int i) { return i >= 0 && i < items.Count ? items[i].Go : null; }

        // Bits des genres de vetements portes ici (etat du joueur, pour l'avatar).
        public static int LocalFlags
        {
            get
            {
                int f = 0;
                foreach (Item it in AllItems()) if (it.LocalWorn) f |= Bits[it.Kind];
                return f;
            }
        }

        // ---------------------------------------------------------------- copies (une paire par joueur)
        // Point de la paire k (>= 2) dans la maison du debut de partie : couloir, pres de l'armoire (a cote des sacs de
        // couchage) ; posees un peu en l'air, elles tombent au sol.
        static bool SpawnPoint(int kind, int k, out Vector3 at)
        {
            at = Vector3.zero;
            // Casques et tasses : a cote de l'original du jeu (bureau de l'appartement, cuisine), pas par terre au point de depart
            // (Menor, 20/10 : « les doubles devraient apparaitre sur le bureau comme celui du jeu »). 22 cm plus loin par paire,
            // reposes sur ce qu'il y a dessous (le bureau).
            GameObject orig = kind >= 2 && kind < items.Count ? items[kind].Go : null;
            if (orig != null)
            {
                Transform o = orig.transform;
                at = o.position + Vector3.Cross(Vector3.up, o.forward).normalized * (0.22f * (k - 1)) + Vector3.up * 0.05f;
                RaycastHit under;
                if (Physics.Raycast(at + Vector3.up * 0.3f, Vector3.down, out under, 1.2f, ~(1 << 2)) && !under.collider.isTrigger && under.collider.transform.root != o.root) at = under.point + Vector3.up * 0.06f;
                return true;
            }
            GameObject home = Game.FindAny("HOMENEW");
            if (home == null) return false;
            at = home.transform.position + new Vector3(0.45f + kind * 0.3f, 0.2f, 1.7f + (k - 2) * 0.45f);   // (racine (-1285,6 ; 1,5 ; 1078,8))
            RaycastHit hit;
            if (Physics.Raycast(at + Vector3.up * 0.5f, Vector3.down, out hit, 3f, ~(1 << 2))) at = hit.point + Vector3.up * 0.15f;
            return true;
        }

        static Item MakeCopy(int kind, int k, Vector3 pos, Quaternion rot)
        {
            GameObject src = items[kind].Go;
            if (src == null && kind >= 2)
            {
                // (casque ou tasse absent de cette partie : place vide, la numerotation reste la meme chez tous)
                var none = new Item { Go = null, Kind = kind, Name = Kinds[kind] + " " + k, Key = "w:vetement:" + Kinds[kind] + k };
                items.Add(none);
                return none;
            }
            if (src == null) return null;
            var it = NewCopy(kind, k, src, CopyNames[kind] + " " + k + "(itemx)", Kinds[kind] + " " + k, "w:vetement:" + Kinds[kind] + k, pos, rot);
            items.Add(it);
            return it;
        }

        // copie d'un vetement du jeu (« (itemx) » dans le nom : la main du jeu le prend comme un objet), pas encore listee
        static Item NewCopy(int kind, int k, GameObject src, string goName, string name, string key, Vector3 pos, Quaternion rot)
        {
            var go = (GameObject)Object.Instantiate(src, pos, rot);
            go.name = goName;
            go.transform.parent = null;
            go.transform.position = pos; go.transform.rotation = rot;
            go.transform.localScale = Vector3.one;   // (comme le chargement du jeu : SetScale 1)
            go.layer = 19; go.tag = "PART";
            go.SetActive(true);
            Rigidbody rb = go.GetComponent<Rigidbody>();
            if (rb != null) { rb.isKinematic = false; rb.velocity = Vector3.zero; rb.angularVelocity = Vector3.zero; }
            PlayMakerFSM use = Game.FsmOn(go, "Use");
            if (kind == 3 && use != null)
            {
                // Tasse : ses propres etiquettes de sauvegarde du jeu (CoffeeCupCoffee...), pas celles de la tasse d'origine.
                foreach (string v in new[] { "UniqueTagCoffee", "UniqueTagCaffeine", "UniqueTagTransform" })
                {
                    FsmString t = use.FsmVariables.FindFsmString(v);
                    if (t != null) t.Value = t.Value + "MWC" + k;
                }
                FsmFloat cf = use.FsmVariables.FindFsmFloat("Coffee"); if (cf != null) cf.Value = 0.0001f;
            }
            else if (kind == 2)
            {
                // Casque : sa peinture ecrirait la sauvegarde du casque du jeu (EQUIPMENTS/Helmet 'Save') -- coupee ; la
                // logique du joueur (Helmet 'Logic', variable {Helmet}) est rebranchee sur la copie quand on la met (HookHelmet).
                PlayMakerFSM paint = Game.FsmOn(go, "Paint");
                if (paint != null) paint.enabled = false;
            }
            else if (use != null)
            {
                FsmGameObject o = use.FsmVariables.FindFsmGameObject("Owner"); if (o != null) o.Value = go;
                FsmString up = use.FsmVariables.FindFsmString("UTPos"); if (up != null) up.Value = UtPos[kind] + "MWC" + k;
                FsmString us = use.FsmVariables.FindFsmString("UTState"); if (us != null) us.Value = UtState[kind] + "MWC" + k;
                FsmInt st = use.FsmVariables.FindFsmInt("State"); if (st != null) st.Value = 0;
                FsmBool ld = use.FsmVariables.FindFsmBool("Loaded"); if (ld != null) ld.Value = true;
            }
            var nit = new Item { Go = go, Kind = kind, Name = name, Key = key, FreePos = pos, FreeEul = rot.eulerAngles };
            Props.SoonScan();
            return nit;
        }

        // ---------------------------------------------------------------- casques achetes (epicerie, JD 23/10)
        // Copies du casque du jeu vendues a l'Osuuskauppa, en plus des paires (une par joueur) : creees chez l'hote (ou en
        // solo), liste et poses envoyees a tous (@vetosto, redite avec celle des paires), gardees a cote de la sauvegarde de
        // l'hote (MWCoop-kyparat.txt). Messages de port : numero 128 + j (hors des paires, dont l'ordre peut differer).
        const int OstoBase = 128;
        static readonly List<Item> osto = new List<Item>();
        static readonly List<Vector3[]> ostoFile = new List<Vector3[]>();
        static bool ostoRead;
        static bool Authority { get { return !Session.Active || Session.IsHost; } }
        static string OstoPath { get { return WorldFiles.PathOf("MWCoop-kyparat.txt"); } }

        static IEnumerable<Item> AllItems()
        {
            foreach (Item it in items) yield return it;
            foreach (Item it in osto) yield return it;
        }

        public static void BuyHelmet(Vector3 pos, Quaternion rot)
        {
            if (Authority) { AddOsto(pos, rot, true, Session.Active ? Session.LocalId : 0); return; }
            Session.SendAll(new NetWriter(Msg.Job).U8(Session.LocalId).Str("@vetostoa").Vec(pos).Quat(rot), true);
            Log.Info("vetements : casque achete, demande envoyee a l'hote");
        }

        public static void OnBuyRequest(int who, Vector3 pos, Quaternion rot) { if (Session.IsHost) AddOsto(pos, rot, true, who); }

        // essai (boutique, action « kyparapaalle ») : le dernier casque achete mis par son automate Use, comme un clic
        public static string TestOsto()
        {
            if (osto.Count == 0) return "aucun casque achete";
            Item it = osto[osto.Count - 1];
            PlayMakerFSM use = it.Go != null ? Game.FsmOn(it.Go, "Use") : null;
            if (use == null) return it.Name + " : pas d'automate Use";
            Game.SetState(use, "State 1");
            return it.Name + " : mis par son automate (" + use.ActiveStateName + ")";
        }
        public static string OstoState() { return osto.Count + " casque(s) achete(s)" + (osto.Count > 0 ? ", dernier : " + State2(osto[osto.Count - 1]) : ""); }
        static string State2(Item it) { return it.Name + (it.Go == null ? " absent" : (it.Go.activeSelf ? " visible en " + it.Go.transform.position.ToString("F2") : " inactif") + ", porte ici " + it.LocalWorn + ", PlayerHelmet " + Game.GlobalBool("PlayerHelmet")); }

        static Item AddOsto(Vector3 pos, Quaternion rot, bool bought, int who)
        {
            GameObject src = items[2].Go;
            if (src == null) { Log.Info("vetements : casque du jeu introuvable, casque achete pas cree"); return null; }
            int j = osto.Count + 1;
            Item it = NewCopy(2, j, src, "helmet osto " + j + "(itemx)", "casque achete " + j, "w:vetement:casqueosto" + j, pos, rot);
            osto.Add(it);
            HookHelmets();
            if (bought)
            {
                Log.Info("vetements : casque achete " + j + " par #" + who + ", pose en " + pos.ToString("F1"));
                SaveOsto(); SendOsto();
            }
            return it;
        }

        static void LoadOsto()
        {
            ostoRead = true;
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            try
            {
                if (File.Exists(OstoPath))
                    foreach (string l in File.ReadAllLines(OstoPath))
                    {
                        string[] c = l.Split(';');
                        if (c.Length < 6) continue;
                        Vector3 p = new Vector3(float.Parse(c[0], ci), float.Parse(c[1], ci), float.Parse(c[2], ci)), e = new Vector3(float.Parse(c[3], ci), float.Parse(c[4], ci), float.Parse(c[5], ci));
                        AddOsto(p, Quaternion.Euler(e), false, 0);
                    }
            }
            catch (System.Exception ex) { Log.Warn("vetements : lecture des casques achetes " + ex.Message); }
            if (osto.Count > 0) Log.Info("vetements : " + osto.Count + " casque(s) achete(s) repris de la sauvegarde");
        }

        static void SaveOsto()
        {
            if (!Authority || !ostoRead) return;
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            var sb = new System.Text.StringBuilder();
            foreach (Item it in osto)
            {
                foreach (float f in new[] { it.FreePos.x, it.FreePos.y, it.FreePos.z, it.FreeEul.x, it.FreeEul.y, it.FreeEul.z }) sb.Append(f.ToString("R", ci)).Append(';');
                sb.Length--; sb.Append('\n');
            }
            try { File.WriteAllText(OstoPath, sb.ToString()); } catch (System.Exception ex) { Log.Warn("vetements : ecriture des casques achetes " + ex.Message); }
        }

        static void SendOsto()
        {
            if (!Session.Active || !Session.IsHost || osto.Count == 0 || Session.RemoteCount == 0) return;
            var w = new NetWriter(Msg.Job).U8(Session.LocalId).Str("@vetosto").U8(osto.Count);
            foreach (Item it in osto) w.Vec(it.FreePos).Vec(it.FreeEul);
            Session.SendAll(w, true);
        }

        // Invite : liste de l'hote -- les casques achetes qui manquent ici, a la pose de l'hote.
        public static void OnOstoList(NetReader r)
        {
            int n = r.U8();
            var ps = new List<Vector3[]>();
            for (int j = 0; j < n; j++) ps.Add(new[] { r.Vec(), r.Vec() });
            if (Session.IsHost || items[2].Go == null) return;
            for (int j = osto.Count; j < n; j++) AddOsto(ps[j][0], Quaternion.Euler(ps[j][1]), false, 0);
        }

        // Paires jusqu'a n (poses : veste, rotation, combinaison, rotation par paire k >= 2, sinon point de la maison).
        static bool EnsurePairs(int n, List<Vector3[]> poses, string why)
        {
            if (items[0].Go == null || items[1].Go == null) return false;
            int was = pairs;
            while (pairs < n)
            {
                int k = pairs + 1;
                Vector3[] ps = poses != null && k - 2 < poses.Count ? poses[k - 2] : null;
                for (int kind = 0; kind < PerSet; kind++)
                {
                    Vector3 at; Quaternion q = Quaternion.identity;
                    if (ps != null && kind * 2 + 1 < ps.Length) { at = ps[kind * 2]; q = Quaternion.Euler(ps[kind * 2 + 1]); }
                    else if (!SpawnPoint(kind, k, out at)) { Transform pl = Game.PlayerT; if (pl == null) return false; at = pl.position + pl.forward * 1.2f + pl.right * (kind - 0.5f) * 0.6f + Vector3.up * 0.3f; }
                    if (MakeCopy(kind, k, at, q) == null) return false;
                }
                pairs = k;
            }
            if (pairs > was) Log.Info("vetements : " + pairs + " vestes, combinaisons, casques et tasses (" + why + ")");
            return true;
        }

        static string FilePath { get { return WorldFiles.PathOf("MWCoop-vetements.txt"); } }   // ([Test] DossierSacs : comme SleepingBags)

        static void LoadFile()
        {
            WorldFiles.Check(Path.GetDirectoryName(FilePath));
            fileRead = true;
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            try
            {
                if (!File.Exists(FilePath)) return;
                foreach (string l in File.ReadAllLines(FilePath))
                {
                    string[] c = l.Split(';');
                    if (c.Length < 12) continue;
                    var v = new Vector3[System.Math.Min(c.Length / 3, PerSet * 2)];   // (avant 0.83.2 : veste et combinaison seules)
                    for (int j = 0; j < v.Length; j++) v[j] = new Vector3(float.Parse(c[j * 3], ci), float.Parse(c[j * 3 + 1], ci), float.Parse(c[j * 3 + 2], ci));
                    filePoses.Add(v);
                }
            }
            catch (System.Exception e) { Log.Warn("vetements : lecture " + e.Message); }
        }

        // Pose gardee d'une copie : la ou elle est, sauf portee ou cachee (porte par quelqu'un) : son point de la maison.
        static void PoseOf(Item it, int k, out Vector3 p, out Vector3 e)
        {
            Transform t = it.Go != null ? it.Go.transform : null;
            bool away = t == null || it.LocalWorn || it.Hidden || !it.Go.activeInHierarchy || (player != null && t.IsChildOf(player));
            if (!away) { p = t.position; e = t.rotation.eulerAngles; return; }
            e = Vector3.zero;
            if (!SpawnPoint(it.Kind, k, out p)) p = t != null ? t.position : Vector3.zero;
        }

        // A la sauvegarde du jeu (SaveHook), comme les autres objets des ajouts : poses des tenues de plus.
        public static void OnGameSave() { if (Session.Active && fileRead) SaveFile(); SaveOsto(); }

        static void SaveFile()
        {
            if (!Session.IsHost || pairs < 2) return;
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            var sb = new System.Text.StringBuilder();
            for (int k = 2; k <= pairs; k++)
            {
                for (int kind = 0; kind < PerSet; kind++)
                {
                    Vector3 p, e;
                    PoseOf(items[NBase + (k - 2) * PerSet + kind], k, out p, out e);
                    foreach (float f in new[] { p.x, p.y, p.z, e.x, e.y, e.z }) sb.Append(f.ToString("R", ci)).Append(';');
                }
                sb.Length--; sb.Append('\n');
            }
            try { File.WriteAllText(FilePath, sb.ToString()); } catch (System.Exception ex) { Log.Warn("vetements : ecriture " + ex.Message); }
        }

        static void SendList()
        {
            if (Session.RemoteCount == 0 || pairs < 2) return;
            var w = new NetWriter(Msg.Job).U8(Session.LocalId).Str("@vet").U8(pairs);
            for (int k = 2; k <= pairs; k++)
                for (int kind = 0; kind < PerSet; kind++)
                {
                    Item it = items[NBase + (k - 2) * PerSet + kind];
                    Vector3 p, e;
                    PoseOf(it, k, out p, out e);
                    w.Vec(p).Vec(e);
                }
            Session.SendAll(w, true);
        }

        // Invite : liste de l'hote (@vet) -- les copies qui manquent ici, a la pose de l'hote.
        public static void OnList(NetReader r)
        {
            int n = r.U8();
            var poses = new List<Vector3[]>();
            for (int k = 2; k <= n; k++) { var ps = new Vector3[PerSet * 2]; for (int j = 0; j < ps.Length; j++) ps[j] = r.Vec(); poses.Add(ps); }
            if (Session.IsHost || n <= pairs) return;
            if (!EnsurePairs(n, poses, "liste de l'hote")) Log.Info("vetements : liste de l'hote (" + n + " paires) en attente, veste ou combinaison du jeu pas encore trouvee");
        }

        // ---------------------------------------------------------------- boucle
        // Cafe des tasses (suggestion de Crowbangus, 12/10 : « je verse du cafe dans la tasse et la tends a l'autre : il ne le
        // voit pas »). Chaque tasse (celle du jeu et les copies par joueur, meme rang partout) garde son cafe dans 'Use' (Coffee,
        // Caffeine ; Pour le remplit depuis la casserole, Play anim le vide quand on boit). Celui chez qui il change de plus de
        // 0,004 l'envoie (@tasse, 4 fois/s au plus pendant qu'on verse) ; les autres le posent et repassent par l'etat Data du
        // jeu, qui recalcule la surface du cafe (hauteur, couleur). L'hote renvoie toutes les tasses non vides toutes les 15 s
        // (arrivants).
        static readonly Dictionary<int, float> cupSeen = new Dictionary<int, float>();
        static float cupResend;
        static int cupLogs;
        static PlayMakerFSM CupFsm(int i, out FsmFloat coffee, out FsmFloat caf)
        {
            coffee = caf = null;
            if (i < 0 || i >= items.Count || items[i].Kind != 3 || items[i].Go == null) return null;
            PlayMakerFSM f = Game.FsmOn(items[i].Go, "Use");
            if (f == null) return null;
            coffee = f.FsmVariables.FindFsmFloat("Coffee");
            caf = f.FsmVariables.FindFsmFloat("Caffeine");
            return coffee != null ? f : null;
        }
        static void CupTick(float now)
        {
            if (!Session.Active) return;
            bool resend = Session.IsHost && now >= cupResend;
            if (resend) cupResend = now + 15f;
            for (int i = 0; i < items.Count; i++)
            {
                FsmFloat c, k;
                if (CupFsm(i, out c, out k) == null) continue;
                float last;
                if (!cupSeen.TryGetValue(i, out last)) { cupSeen[i] = c.Value; continue; }
                bool changed = Mathf.Abs(c.Value - last) >= 0.004f;
                if (!changed && !(resend && c.Value > 0.01f)) continue;
                cupSeen[i] = c.Value;
                Session.SendAll(new NetWriter(Msg.Job).U8(Session.LocalId).Str("@tasse").U8(i).F32(c.Value).F32(k != null ? k.Value : 0f), true);
                if (changed && cupLogs++ < 10) Log.Info("tasse " + items[i].Name + " : cafe " + c.Value.ToString("F3") + " envoye");
            }
        }
        // [Test] Autotest=tasse (2 joueurs) : a 45 s l'invite met 0,25 de cafe dans la tasse du jeu (rang 3) et repasse par Data ;
        // a 52 s chacun note son cafe et la hauteur de sa surface.
        static int cupStep;
        public static void TestCup(string mode, float t)
        {
            if (mode != "tasse") return;
            FsmFloat c, k;
            PlayMakerFSM f = CupFsm(3, out c, out k);
            if (f == null) return;
            if (!Session.IsHost && cupStep == 0 && t > 45f)
            {
                cupStep = 1;
                c.Value = 0.25f; if (k != null) k.Value = 0.8f;
                Game.SetState(f, "Data");
                Log.Info("autotest : tasse, cafe 0,25 verse ici");
            }
            if (cupStep < 2 && t > 52f)
            {
                cupStep = 2;
                Transform surf = items[3].Go.transform.Find("mesh/Pivot/surface");
                Log.Info("autotest : tasse, cafe " + c.Value.ToString("F3") + ", surface z " + (surf != null ? surf.localPosition.z.ToString("F4") : "?") + " [" + f.ActiveStateName + "]");
            }
        }
        public static void OnCup(int who, NetReader r)
        {
            int i = r.U8();
            float coffee = r.F32(), caf = r.F32();
            FsmFloat c, k;
            PlayMakerFSM f = CupFsm(i, out c, out k);
            if (f == null) return;
            if (Mathf.Abs(c.Value - coffee) < 0.0005f) { cupSeen[i] = c.Value; return; }
            string st = f.ActiveStateName;
            if (st == "Pour" || st == "Play anim") return;   // (on verse ou on boit ici : notre valeur partira)
            c.Value = coffee;
            if (k != null) k.Value = caf;
            cupSeen[i] = coffee;
            if (f.enabled && f.gameObject.activeInHierarchy) Game.SetState(f, "Data");   // (surface du cafe : hauteur et couleur)
            if (cupLogs++ < 10) Log.Info("tasse " + items[i].Name + " : cafe " + coffee.ToString("F3") + " recu de #" + who);
        }

        public static void Update()
        {
            if (findAt < 0 || !PlayerSync.InGame) return;
            float now = Time.realtimeSinceStartup;
            if (!found && now >= findAt) { findAt = now + 10f; Find(); }
            if (now < nextPoll) return;
            nextPoll = now + 0.25f;
            if (found && player == null) { found = false; findAt = now; }   // PLAYER detruit : recherche a la prochaine image
            HookHelmets();
            CupTick(now);
            if (Session.Active && Session.IsHost && items[0].Go != null && items[1].Go != null && now - loadedAt > 12f)
            {
                if (!fileRead) LoadFile();
                int want = Mathf.Max(Session.RemoteCount + 1, filePoses.Count + 1);
                if (want > pairs && EnsurePairs(want, filePoses, filePoses.Count + 1 >= want ? "reprises de la sauvegarde" : "une par joueur")) { SaveFile(); nextList = 0f; }
                if (now >= nextList) { nextList = now + 10f; SendList(); }
            }
            if (Authority && !ostoRead && items[2].Go != null && now - loadedAt > 12f) { LoadOsto(); SendOsto(); }
            if (Session.Active && Session.IsHost && ostoRead && now >= nextOsto) { nextOsto = now + 10f; SendOsto(); }
            if (swapKind >= 0 && now >= swapAt) Swap(now);
            for (int i = 0; i < items.Count; i++) Tick(items[i], i, now);
            for (int j = 0; j < osto.Count; j++) Tick(osto[j], OstoBase + j, now);
        }
        static float nextOsto;

        static void Tick(Item it, int i, float now)
        {
            {
                GameObject go = it.Go;
                if (go == null) return;
                Transform t = go.transform;
                // Sous PLAYER (pas sa racine : au volant ou passager, la racine est la voiture).
                bool worn = !go.activeSelf && player != null && t.IsChildOf(player);
                if (worn != it.LocalWorn)
                {
                    it.LocalWorn = worn;
                    if (worn) it.Hidden = false;
                    if (!worn && i >= OstoBase) { it.FreePos = t.position; it.FreeEul = t.rotation.eulerAngles; }   // (cache ici, puis mis par ce joueur-ci : impossible, mais propre)
                    if (Session.Active) { SendWear(i, worn, t); it.SentAt = now; }
                    Log.Info("vetements : " + it.Name + (worn ? " mis(e)" : " enleve(e) en " + t.position.ToString("F1")));
                }
                else if (worn && Session.Active && now - it.SentAt > 5f) { SendWear(i, true, t); it.SentAt = now; }   // (redit : arrivee, message perdu)
                if (i >= OstoBase && !worn && !it.Hidden && go.activeInHierarchy && (player == null || !t.IsChildOf(player))) { it.FreePos = t.position; it.FreeEul = t.rotation.eulerAngles; }
                if (!Session.Active) return;
                int wearer = RemoteWearer(it, now);
                if (worn)
                {
                    if (wearer >= 0 && wearer < Session.LocalId) Yield(it, wearer, now);
                    return;
                }
                if (wearer >= 0)
                {
                    it.LastWearer = wearer;
                    if (!it.Hidden) Hide(it, wearer, now);
                }
                else if (it.Hidden)
                {
                    // Plus porte : a l'endroit ou celui qui le portait l'a enleve (son message fiable), sinon a sa place d'avant.
                    bool off = it.OffWho == it.LastWearer && it.OffAt >= it.HidAt;
                    Unhide(it, off);
                }
            }
        }

        static void SendWear(int i, bool worn, Transform t)
        {
            Session.SendAll(new NetWriter(Msg.Wear).U8(Session.LocalId).U8(i).Bool(worn).Vec(t.position).Quat(t.rotation), true);
        }

        // Joueur distant qui porte le vetement, -1 : personne (message de moins de 15 s, joueur toujours la).
        static int RemoteWearer(Item it, float now)
        {
            if (it.WornBy < 0 || it.WornBy == Session.LocalId) return -1;
            if (!Session.Players.ContainsKey(it.WornBy) || now - it.WornAt > 15f) { it.WornBy = -1; return -1; }
            return it.WornBy;
        }

        static void Hide(Item it, int wearer, float now)
        {
            Transform t = it.Go.transform;
            if (player != null && t.IsChildOf(player))
            {
                // Tenu en main (ou porte) ici, meme assis dans une voiture : on attend qu'il soit lache.
                if (now - it.HeldLog > 10f) { it.HeldLog = now; Log.Info("vetements : " + it.Name + " porte(e) par #" + wearer + " mais tenu(e) ici"); }
                return;
            }
            Props.Release(it.Go);
            it.HidParent = t.parent; it.HidPos = t.position; it.HidRot = t.rotation; it.HidAt = now; it.HidActive = it.Go.activeSelf;
            it.Go.SetActive(false);
            it.Hidden = true;
            Log.Info("vetements : " + it.Name + " porte(e) par #" + wearer + ", cache(e) ici");
        }

        static void Unhide(Item it, bool off)
        {
            Transform t = it.Go.transform;
            if (off) { t.parent = null; t.position = it.OffPos; t.rotation = it.OffRot; }
            else { t.parent = it.HidParent; t.position = it.HidPos; t.rotation = it.HidRot; }
            it.Go.SetActive(off || it.HidActive);
            Rigidbody rb = it.Go.GetComponent<Rigidbody>();
            if (rb != null && !rb.isKinematic) { rb.velocity = Vector3.zero; rb.WakeUp(); }
            it.Hidden = false;
            Props.SoonScan();
            Log.Info("vetements : " + it.Name + (off ? " enleve(e) par l'autre, reapparait en " : " plus porte(e), reapparait a sa place en ") + t.position.ToString("F1"));
        }

        // Porte ici ET par un joueur de plus petit numero : on l'enleve (etat stable de l'automate du jeu seulement,
        // pas en pleine animation), comme le joueur en regardant en bas ; il sera ensuite cache ici.
        static void Yield(Item it, int wearer, float now)
        {
            if (now - it.YieldAt < 5f) return;
            PlayMakerFSM logic = it.Kind == 2 ? helmetLogic : clothLogic;
            if (logic == null) return;
            string s = logic.ActiveStateName;
            if (s != "State 1" && s != "Helmet ON") return;
            if (GoVar(logic, it.Kind == 2 ? "Helmet" : "Cloth") != it.Go) return;
            it.YieldAt = now;
            Log.Info("vetements : " + it.Name + " deja porte(e) par #" + wearer + " (numero plus petit) : enleve(e) ici");
            Game.SetState(logic, "Anim off1");
            // Veste ou combinaison : une copie libre est mise a la place une fois celle-ci enlevee (animation : ~1,5 s). Avant :
            // l'invite qui chargeait la partie en la portant (sauvegarde de l'hote) la voyait s'enlever toute seule (retours de
            // Hall0wMan et d'OpNoob, 11/10).
            if (it.Kind < 2) { swapKind = it.Kind; swapAt = now + 2f; swapUntil = now + 30f; }
        }

        static void Swap(float now)
        {
            if (now > swapUntil) { Log.Info("vetements : pas de " + Kinds[swapKind] + " libre a mettre a la place"); swapKind = -1; return; }
            swapAt = now + 1f;
            if (clothLogic == null || clothLogic.ActiveStateName != "Wait") return;   // (animation pas finie)
            foreach (Item c in items)
            {
                if (c.Kind != swapKind || c.Go == null || c.LocalWorn || c.Hidden || !c.Go.activeInHierarchy || RemoteWearer(c, now) >= 0) continue;
                if (player != null && c.Go.transform.IsChildOf(player)) continue;
                PlayMakerFSM use = Game.FsmOn(c.Go, "Use");
                if (use == null) continue;
                Game.SetState(use, "State 1");   // (mains libres ? -> Wear -> EQUIP a la logique du joueur)
                Log.Info("vetements : " + c.Name + " mis(e) a la place");
                swapKind = -1;
                return;
            }
        }

        public static void OnMessage(Peer from, NetReader r)
        {
            int who = r.U8();
            if (Session.IsHost) who = from.Id;
            int i = r.U8();
            bool worn = r.Bool();
            Vector3 pos = r.Vec();
            Quaternion rot = r.Quat();
            if (Session.IsHost) Session.Broadcast(new NetWriter(Msg.Wear).U8(who).U8(i).Bool(worn).Vec(pos).Quat(rot), true, who);
            Item it = i >= OstoBase ? (i - OstoBase < osto.Count ? osto[i - OstoBase] : null) : (i < items.Count ? items[i] : null);
            if (it == null) return;   // (copie pas encore la : redit dans 5 s)
            float now = Time.realtimeSinceStartup;
            bool was = it.WornBy == who;
            if (worn) { it.WornBy = who; it.WornAt = now; }
            else
            {
                if (it.WornBy == who) it.WornBy = -1;
                it.OffWho = who; it.OffPos = pos; it.OffRot = rot; it.OffAt = now;
            }
            if (worn != was) Log.Info("vetements de #" + who + " : " + it.Name + (worn ? " mis(e)" : " enleve(e) en " + pos.ToString("F1")));
        }

        // Essais : etat d'un vetement ici (objet, porte, cache) et ce que montrent les avatars.
        public static string State(int i)
        {
            if (i < 0 || i >= items.Count) return "vetement " + i + " absent (" + items.Count + " ici)";
            Item it = items[i];
            var sb = new System.Text.StringBuilder(it.Name);
            if (it.Go == null) sb.Append(" absent(e)");
            else sb.Append(it.Go.activeSelf ? " visible en " + it.Go.transform.position.ToString("F2") : " inactif(ve)").Append(", sous ").Append(it.Go.transform.root.name);
            sb.Append(", porte(e) ici ").Append(it.LocalWorn).Append(", cache(e) ").Append(it.Hidden)
              .Append(", par #").Append(Session.Active ? RemoteWearer(it, Time.realtimeSinceStartup) : -1);
            foreach (Avatar a in PlayerSync.Avatars) sb.Append(" ; avatar ").Append(a.ClothesState());
            return sb.ToString();
        }

        // veste ([Test] Autotest=veste) : [Test] TestPorteur (invite par defaut, ou hote) met [Test] TestVetement
        // (0 veste, 1 combinaison, 2 casque, 3 veste 2, 4 combinaison 2...) a 40 s par l'automate Use de l'objet (comme un
        // clic), l'enleve a 60 s (logique du jeu, comme en regardant en bas). Les deux notent toutes les 2 s de 30 a 75 s :
        // chez l'autre, l'objet est cache de ~40 a ~60 s puis reapparait la ou il a ete enleve ; l'avatar montre la teinte
        // (veste, combinaison) ou le casque pendant ce temps.
        // [Test] TestVetementAutre=j : l'autre joueur met le vetement j aux memes moments (TestVetementAutreA=s : le met a s) (deux vestes portees a la fois :
        // TestVetement=0, TestVetementAutre=3) ; chacun note aussi j.
        // [Test] TestVetementAssis=1 : de 45 a 55 s, PLAYER du porteur est range sous un objet vide (comme au volant ou
        // passager : DriveTrigger 'Reset view', Seats) ; le vetement doit rester porte (pas de "enleve(e)" ici, toujours
        // cache chez l'autre), State montre "sous MWCoop-EssaiSiege".
        static int step, seatStep;
        static float testLog;
        static GameObject testSeat;

        public static void Test(string mode, float t)
        {
            if (mode != "veste") return;
            bool wearer = Session.IsHost == (Config.Get("Test", "TestPorteur", "invite") == "hote");
            int i = Config.GetInt("Test", "TestVetement", 0);
            int other = Config.GetInt("Test", "TestVetementAutre", -1);
            int mine = wearer ? i : other;
            float at = wearer ? 40f : Config.GetInt("Test", "TestVetementAutreA", 40);   // (TestVetementAutreA : moment de l'autre, s)
            if (mine >= 0 && t > at && step == 0) { step = 1; Log.Info("autotest : " + TestWear(mine, true)); }
            if (wearer && Config.GetInt("Test", "TestVetementAssis", 0) != 0)
            {
                if (t > 45f && seatStep == 0 && player != null)
                {
                    seatStep = 1;
                    testSeat = new GameObject("MWCoop-EssaiSiege");
                    testSeat.transform.position = player.position;
                    player.parent = testSeat.transform;
                    Log.Info("autotest : vetements, PLAYER range sous " + testSeat.name + " (comme assis dans une voiture)");
                }
                if (t > 55f && seatStep == 1)
                {
                    seatStep = 2;
                    if (player != null && testSeat != null && player.parent == testSeat.transform) player.parent = null;
                    if (testSeat != null) Object.Destroy(testSeat);
                    testSeat = null;
                    Log.Info("autotest : vetements, PLAYER rendu a la racine");
                }
            }
            if (mine >= 0 && t > 60f && step == 1) { step = 2; Log.Info("autotest : " + TestWear(mine, false)); }
            if (t > 30f && t < 75f && t - testLog >= 2f)
            {
                testLog = t;
                Log.Info("autotest : vetements, " + State(i) + (other >= 0 ? " | " + State(other) : "") + " | " + pairs + " paires");
            }
        }

        static string TestWear(int i, bool on)
        {
            if (i < 0 || i >= items.Count) return "vetement " + i + " absent (" + items.Count + " ici)";
            Item it = items[i];
            if (it.Go == null) return it.Name + " absent(e)";
            if (on)
            {
                PlayMakerFSM use = Game.FsmOn(it.Go, "Use");
                // [Test] TestForcer=1 : meme cache ici (porte par un autre), comme le chargement d'une sauvegarde ou il est porte.
                if (use != null && it.Hidden && Config.GetInt("Test", "TestForcer", 0) != 0) { it.Go.SetActive(true); it.Hidden = false; }
                if (use == null || !it.Go.activeInHierarchy) return it.Name + " : automate Use absent ou objet inactif";
                // 'State 1' : mains libres ? -> Wear (veste, combinaison) / State 3 (casque) -> EQUIP a la logique du joueur.
                Game.SetState(use, "State 1");
                return it.Name + " mis(e) par Use -> " + use.ActiveStateName;
            }
            PlayMakerFSM logic = it.Kind == 2 ? helmetLogic : clothLogic;
            if (logic == null) return it.Name + " : logique du joueur absente";
            string before = logic.ActiveStateName;
            Game.SetState(logic, "Anim off1");
            return it.Name + " enleve(e) : " + before + " -> " + logic.ActiveStateName;
        }
    }
}
