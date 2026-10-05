using HutongGames.PlayMaker;
using MWCoop.Net;
using UnityEngine;

namespace MWCoop
{
    // Vetements portes : veste d'hiver, combinaison, casque (un seul exemplaire de chacun dans le monde). Les
    // mettre les range sous PLAYER (inactifs) chez celui qui les porte ; chez les autres ils restaient au crochet,
    // prenables une 2e fois, et l'avatar ne montrait rien.
    //  - qui porte quoi : 3 bits de l'etat du joueur (PlayerSync.F_Jacket, F_Coverall, F_Helmet), envoyes en
    //    continu (arrivee en cours de partie, depart, perte) ; l'avatar les montre (Avatar).
    //  - mettre / enlever : message fiable (vetement, porte ou non, pose la ou il a ete enleve).
    //  - chez les autres, le vetement porte par un autre est cache (desactive : ni visible ni prenable) ; enleve,
    //    il reapparait la ou il a ete enleve (a defaut la ou il etait, ex. joueur parti en le portant).
    //  - porte par deux joueurs a la fois (sauvegarde de l'hote faite en le portant, chargee par un invite ; deux
    //    clics croises) : le plus petit numero (l'hote d'abord) le garde, l'autre l'enleve (logique du jeu).
    // Detection par sondage 4 fois/s (3 objets, sans allocation) : l'objet porte est sous PLAYER et inactif ;
    // tenu en main il est sous PLAYER mais actif (ItemPivot). "Sous PLAYER" = descendant (IsChildOf), pas racine :
    // au volant ou passager, PLAYER lui-meme est range sous la voiture.
    public static class Wear
    {
        const int N = 3;
        static readonly string[] Paths = { "EQUIPMENTS/winter jacket(itemx)", "EQUIPMENTS/winter coverall(itemx)", "EQUIPMENTS/Helmet/helmet(itemx)" };
        static readonly string[] Names = { "veste", "combinaison", "casque" };
        static readonly int[] Bits = { PlayerSync.F_Jacket, PlayerSync.F_Coverall, PlayerSync.F_Helmet };
        // Depuis PLAYER : au volant ou passager, PLAYER est range sous la voiture (DriveTrigger 'Reset view', Seats) et
        // n'est plus une racine de la scene (Game.FindAny ne le trouverait pas).
        const string ClothingPath = "Pivot/AnimPivot/Camera/FPSCamera/FPSCamera/Clothing";
        const string HelmetPath = "Pivot/AnimPivot/Camera/FPSCamera/FPSCamera/Helmet";

        class Item
        {
            public GameObject Go;
            public bool LocalWorn, Hidden, HidActive;
            public Transform HidParent; public Vector3 HidPos; public Quaternion HidRot; public float HidAt;
            public int MsgWho = -1; public bool MsgWorn; public float MsgAt;      // dernier message recu
            public int OffWho = -1; public Vector3 OffPos; public Quaternion OffRot; public float OffAt = -1;  // pose ou un autre l'a enleve
            public int LastWearer = -1; public float FreeSince = -1f;             // dernier porteur vu ; plus porte depuis
            public float YieldAt = -100, HeldLog = -100;
        }

        static readonly Item[] items = new Item[N];
        static PlayMakerFSM clothLogic, helmetLogic;
        static Transform player;   // PLAYER (par reference : pas de nom lu 12 fois/s), garde une fois trouve
        static float nextPoll, findAt = -1;
        static bool found;

        static Wear()
        {
            for (int i = 0; i < N; i++) items[i] = new Item();
        }

        public static void OnLevelLoaded()
        {
            for (int i = 0; i < N; i++) items[i] = new Item();
            clothLogic = helmetLogic = null;
            player = null;
            found = false;
            step = seatStep = 0; testLog = 0; testSeat = null;
            // Tout de suite (avant que le jeu ne range sous PLAYER un vetement porte a la sauvegarde), puis 5 s plus
            // tard pour ce qui manquerait.
            if (PlayerSync.InGame) { Find(); findAt = Time.realtimeSinceStartup + 5f; }
            else findAt = -1;
        }

        static void Find()
        {
            found = true;
            for (int i = 0; i < N; i++)
            {
                if (items[i].Go != null) continue;
                items[i].Go = Game.FindAny(Paths[i]);
                if (items[i].Go == null) found = false;
            }
            // PLAYER par son nom seul (GameObject.Find cherche alors partout, pas seulement les racines) : trouve
            // aussi quand le joueur est assis dans une voiture ; une fois trouve, garde (jamais remis a null ici).
            if (player == null) { GameObject p = GameObject.Find("PLAYER"); if (p != null) player = p.transform; }
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

        static GameObject GoVar(PlayMakerFSM f, string name)
        {
            FsmGameObject v = f.FsmVariables.FindFsmGameObject(name);
            return v != null ? v.Value : null;
        }

        // Cle fixe des vetements pour Props (ils changent de parent : EQUIPMENTS, PLAYER, racine de la scene).
        public static string KeyOf(GameObject go)
        {
            for (int i = 0; i < N; i++) if (items[i].Go != null && items[i].Go == go) return "w:vetement:" + Names[i];
            return null;
        }

        // Objet du vetement i (0 veste, 1 combinaison, 2 casque), pour l'avatar (copie du casque).
        public static GameObject ItemObject(int i) { return i >= 0 && i < N ? items[i].Go : null; }

        // Bits des vetements portes ici (etat du joueur).
        public static int LocalFlags
        {
            get
            {
                int f = 0;
                for (int i = 0; i < N; i++) if (items[i].LocalWorn) f |= Bits[i];
                return f;
            }
        }

        public static void Update()
        {
            if (findAt < 0 || !PlayerSync.InGame) return;
            float now = Time.realtimeSinceStartup;
            if (!found && now >= findAt) { findAt = now + 10f; Find(); }
            if (now < nextPoll) return;
            nextPoll = now + 0.25f;
            if (found && player == null) { found = false; findAt = now; }   // PLAYER detruit : recherche a la prochaine image
            for (int i = 0; i < N; i++)
            {
                Item it = items[i];
                GameObject go = it.Go;
                if (go == null) continue;
                Transform t = go.transform;
                // Sous PLAYER (pas sa racine : au volant ou passager, la racine est la voiture).
                bool worn = !go.activeSelf && player != null && t.IsChildOf(player);
                if (worn != it.LocalWorn)
                {
                    it.LocalWorn = worn;
                    if (worn) it.Hidden = false;   // (cache ici, puis mis par ce joueur-ci : impossible, mais propre)
                    if (Session.Active)
                        Session.SendAll(new NetWriter(Msg.Wear).U8(Session.LocalId).U8(i).Bool(worn).Vec(t.position).Quat(t.rotation), true);
                    Log.Info("vetements : " + Names[i] + (worn ? " mis(e)" : " enleve(e) en " + t.position.ToString("F1")));
                }
                if (!Session.Active) continue;
                int wearer = RemoteWearer(it, i, now);
                if (worn)
                {
                    if (wearer >= 0 && wearer < Session.LocalId) Yield(it, i, wearer, now);
                    continue;
                }
                if (wearer >= 0)
                {
                    it.LastWearer = wearer; it.FreeSince = -1f;
                    if (!it.Hidden) Hide(it, i, wearer, now);
                }
                else if (it.Hidden)
                {
                    // Plus porte : a l'endroit ou celui qui le portait l'a enleve (son message fiable) ; l'etat du
                    // joueur peut arriver avant ce message : 1,5 s de grace, sauf s'il est parti (a sa place d'avant).
                    bool off = it.OffWho == it.LastWearer && it.OffAt >= it.HidAt;
                    bool gone = it.LastWearer < 0 || !Session.Players.ContainsKey(it.LastWearer);
                    if (it.FreeSince < 0f) it.FreeSince = now;
                    if (off || gone || now - it.FreeSince > 1.5f) Unhide(it, i, off);
                }
            }
        }

        // Joueur distant qui porte le vetement i (le plus petit numero), -1 : personne. Le message fiable fait foi
        // 1 s (l'etat du joueur, non fiable, peut arriver avant ou apres lui).
        static int RemoteWearer(Item it, int i, float now)
        {
            bool fresh = it.MsgWho >= 0 && now - it.MsgAt < 1f;
            int best = -1;
            foreach (PlayerInfo pi in Session.Players.Values)
            {
                if (pi.Local) continue;
                bool on;
                if (fresh && pi.Id == it.MsgWho) on = it.MsgWorn;
                else on = pi.Level == 1 && now - pi.StateTime < 3f && (pi.State.Flags & Bits[i]) != 0;
                if (on && (best < 0 || pi.Id < best)) best = pi.Id;
            }
            return best;
        }

        static void Hide(Item it, int i, int wearer, float now)
        {
            Transform t = it.Go.transform;
            if (player != null && t.IsChildOf(player))
            {
                // Tenu en main (ou porte) ici, meme assis dans une voiture : on attend qu'il soit lache.
                if (now - it.HeldLog > 10f) { it.HeldLog = now; Log.Info("vetements : " + Names[i] + " porte(e) par #" + wearer + " mais tenu(e) ici"); }
                return;
            }
            Props.Release(it.Go);
            it.HidParent = t.parent; it.HidPos = t.position; it.HidRot = t.rotation; it.HidAt = now; it.HidActive = it.Go.activeSelf;
            it.Go.SetActive(false);
            it.Hidden = true;
            Log.Info("vetements : " + Names[i] + " porte(e) par #" + wearer + ", cache(e) ici");
        }

        static void Unhide(Item it, int i, bool off)
        {
            Transform t = it.Go.transform;
            if (off) { t.parent = null; t.position = it.OffPos; t.rotation = it.OffRot; }
            else { t.parent = it.HidParent; t.position = it.HidPos; t.rotation = it.HidRot; }
            it.Go.SetActive(off || it.HidActive);
            Rigidbody rb = it.Go.GetComponent<Rigidbody>();
            if (rb != null && !rb.isKinematic) { rb.velocity = Vector3.zero; rb.WakeUp(); }
            it.Hidden = false;
            Props.SoonScan();
            Log.Info("vetements : " + Names[i] + (off ? " enleve(e) par l'autre, reapparait en " : " plus porte(e), reapparait a sa place en ") + t.position.ToString("F1"));
        }

        // Porte ici ET par un joueur de plus petit numero : on l'enleve (etat stable de l'automate du jeu seulement,
        // pas en pleine animation), comme le joueur en regardant en bas ; il sera ensuite cache ici.
        static void Yield(Item it, int i, int wearer, float now)
        {
            if (now - it.YieldAt < 5f) return;
            PlayMakerFSM logic = i == 2 ? helmetLogic : clothLogic;
            if (logic == null) return;
            string s = logic.ActiveStateName;
            if (s != "State 1" && s != "Helmet ON") return;
            if (GoVar(logic, i == 2 ? "Helmet" : "Cloth") != it.Go) return;
            it.YieldAt = now;
            Log.Info("vetements : " + Names[i] + " deja porte(e) par #" + wearer + " (numero plus petit) : enleve(e) ici");
            Game.SetState(logic, "Anim off1");
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
            if (i < 0 || i >= N) return;
            Item it = items[i];
            float now = Time.realtimeSinceStartup;
            it.MsgWho = who; it.MsgWorn = worn; it.MsgAt = now;
            if (!worn) { it.OffWho = who; it.OffPos = pos; it.OffRot = rot; it.OffAt = now; }
            Log.Info("vetements de #" + who + " : " + Names[i] + (worn ? " mis(e)" : " enleve(e) en " + pos.ToString("F1")));
        }

        // Essais : etat d'un vetement ici (objet, porte, cache) et ce que montrent les avatars.
        public static string State(int i)
        {
            if (i < 0 || i >= N) return "?";
            Item it = items[i];
            var sb = new System.Text.StringBuilder(Names[i]);
            if (it.Go == null) sb.Append(" absent(e)");
            else sb.Append(it.Go.activeSelf ? " visible en " + it.Go.transform.position.ToString("F2") : " inactif(ve)").Append(", sous ").Append(it.Go.transform.root.name);
            sb.Append(", porte(e) ici ").Append(it.LocalWorn).Append(", cache(e) ").Append(it.Hidden)
              .Append(", par #").Append(Session.Active ? RemoteWearer(it, i, Time.realtimeSinceStartup) : -1);
            foreach (Avatar a in PlayerSync.Avatars) sb.Append(" ; avatar ").Append(a.ClothesState());
            return sb.ToString();
        }

        // veste ([Test] Autotest=veste) : [Test] TestPorteur (invite par defaut, ou hote) met [Test] TestVetement
        // (0 veste, 1 combinaison, 2 casque) a 40 s par l'automate Use de l'objet (comme un clic), l'enleve a 60 s
        // (logique du jeu, comme en regardant en bas). Les deux notent toutes les 2 s de 30 a 75 s : chez l'autre,
        // l'objet est cache de ~40 a ~60 s puis reapparait la ou il a ete enleve ; l'avatar montre la teinte (veste,
        // combinaison) ou le casque pendant ce temps.
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
            int i = Mathf.Clamp(Config.GetInt("Test", "TestVetement", 0), 0, N - 1);
            if (wearer && t > 40f && step == 0) { step = 1; Log.Info("autotest : " + TestWear(i, true)); }
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
            if (wearer && t > 60f && step == 1) { step = 2; Log.Info("autotest : " + TestWear(i, false)); }
            if (t > 30f && t < 75f && t - testLog >= 2f) { testLog = t; Log.Info("autotest : vetements, " + State(i)); }
        }

        static string TestWear(int i, bool on)
        {
            Item it = items[i];
            if (it.Go == null) return Names[i] + " absent(e)";
            if (on)
            {
                PlayMakerFSM use = Game.FsmOn(it.Go, "Use");
                if (use == null || !it.Go.activeInHierarchy) return Names[i] + " : automate Use absent ou objet inactif";
                // 'State 1' : mains libres ? -> Wear (veste, combinaison) / State 3 (casque) -> EQUIP a la logique du joueur.
                Game.SetState(use, "State 1");
                return Names[i] + " mis(e) par Use -> " + use.ActiveStateName;
            }
            PlayMakerFSM logic = i == 2 ? helmetLogic : clothLogic;
            if (logic == null) return Names[i] + " : logique du joueur absente";
            string before = logic.ActiveStateName;
            Game.SetState(logic, "Anim off1");
            return Names[i] + " enleve(e) : " + before + " -> " + logic.ActiveStateName;
        }
    }
}
