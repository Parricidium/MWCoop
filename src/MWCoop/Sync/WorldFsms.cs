using System.Collections.Generic;
using System.Reflection;
using HutongGames.PlayMaker;
using MWCoop.Net;
using UnityEngine;

namespace MWCoop
{
    // Tout le reste du monde : les automates que les autres modules ne suivent pas (maison, jardin,
    // Systems -- factures, courrier, electricite, telephone, loto... --, ville, garage, chalets, objets,
    // pieces, boulots sans sauvegarde...).
    //  - ACTIONS : une transition provoquee par un joueur (sortie d'un etat qui attend un clic, une
    //    touche ou la molette, ou evenement global comme le paiement d'une facture) est rejouee chez les
    //    autres. Les transitions de la logique propre (horloge, comparaisons) ne le sont pas : chacun les
    //    calcule, les rejouer les doublerait (facture ajoutee deux fois).
    //  - Jamais ce qui agit sur le joueur lui-meme ou son interface (objets sous PLAYER, GUI, feuilles
    //    Sheets, ordinateur) ni les options et la sauvegarde. Decide etat par etat : une transition n'est
    //    rejouee que si son etat d'arrivee ET tout ce qui peut s'enchainer automatiquement apres
    //    (minuteurs, comparaisons -- pas ce qui attend le joueur) ne touchent pas au joueur. Ainsi le pont
    //    elevateur ou les fusibles passent, le lit (qui finit par deplacer le joueur) non.
    //  - Ni la logique propre des PNJ (marche, ragdoll, telephone : Npcs fait suivre leur corps), sauf ce
    //    qu'un joueur leur fait, ni les machines a sous et le video-poker (la partie reste a celui qui joue).
    //  - Chez celui qui rejoue, son argent et son corps (globales Player*) sont remis comme avant :
    //    seul celui qui paie paie, seul celui qui mange mange.
    //  - ETATS : l'hote fait reference pour les variables des automates sauvegardes (UT/UniqueTag :
    //    factures, compteur, coupure...) et pour les globales de la maison (House*) ; il envoie ce qui
    //    change chaque seconde, et un instantane complet a l'arrivee d'un invite.
    public static class WorldFsms
    {
        static readonly HashSet<string> SkipRoots = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase) { "PLAYER", "GUI", "Sheets", "COMPUTER", "TRAFFIC", "NPC_CARS", "Spawner", "Radio" };
        static readonly HashSet<string> PersonalRoots = new HashSet<string> { "PLAYER", "GUI", "Sheets", "COMPUTER" };
        static readonly string[] SkipObjects = { "OptionsDB", "InitializeControls", "Photomode", "Statistics", "Setup Game", "SAVEGAME", "BankAccount", "Expenses", "PlayerWanted",
                                                 "Cashier", "CashRegister", "INVENTORY",
                                                 "PlayerDatabase" };   // simulation du joueur (faim, soif, fatigue, ivresse, cigarettes) : a chacun la sienne
        // Machines a sous et video-poker (station, bar : SlotMachinePub) : la partie reste a celui qui joue (son
        // argent, ses cartes et ses rouleaux tires au hasard). Rejouee chez l'autre, elle y tirait d'autres cartes
        // et creditait sa machine gratuitement (l'argent est force pendant un rejeu). Tout ce qui est dessous.
        // Pompes a essence (FuelPumps_*) : Machines en recopie ce qu'on voit. Rejouees ici, la prise du pistolet
        // (FuelTrigger*::Use, Check hand) le mettait dans la main du joueur de chaque client, puis il disparaissait ;
        // le clavier et le terminal (argent) tournaient aussi chez les autres (vrai partie du 05/10).
        // Courses : la participation de chaque pilote est a lui (Races) -- inscription et resultats du week-end
        // (ResultsWeekend : REGISTER, SS1-3, RESET), controle technique de SA voiture (RacingInspection), chrono et feux
        // des speciales (TimingSSn, RallyTree), parc ferme, inscription a la glace (LINEUPS). Rejoues, l'inscription
        // d'un invite inscrivait l'hote (autocollants sur sa CORRIS, ses heures de depart retirees).
        static readonly string[] SkipParents = { "VideoPoker", "SlotMachine", "FuelPumps_",
                                                 "ResultsWeekend", "RacingInspection", "TimingSS", "RallyTree", "ParcFerme", "LINEUPS",
                                                 // Menu d'options du jeu (Systems/OptionsMenu : commandes de conduite, souris, graphismes...) : a chacun
                                                 // les siennes. Rejoues, les clics d'un joueur changeaient les reglages des autres (retour de joueurs, 10/10).
                                                 "OptionsMenu" };
        // "Buy" : prendre un article en rayon le met dans SON panier ; c'est la caisse qui est synchronisee (Shop).
        static readonly HashSet<string> SkipFsmNames = new HashSet<string> { "Paint", "LOD", "Death", "HeadForce", "Coldness", "Strafe", "Buy" };
        // Automates de PNJ provoques par un joueur (colere quand on lui urine dessus ou lui fait un doigt, coup de
        // poing, voiture qui le renverse, client au comptoir) : suivis malgre Npcs.IsNpcLogic, ils vont a l'hote.
        static readonly HashSet<string> NpcPlayerFsms = new HashSet<string> { "Anger", "PlayerHit", "CarHit", "Work" };
        // Miroir de l'hote : seulement les systemes de la maison et du monde (pas les machines qu'un invite
        // utilise en ce moment, comme une pompe a essence : l'hote ecraserait son compteur).
        static readonly HashSet<string> MirrorRoots = new HashSet<string> { "Systems", "HOMENEW", "YARD", "CABIN", "COTTAGE" };
        // Variables sous cette racine recopiees de l'hote (Calls : un invite n'y ajoute pas la facture d'un appel, l'hote
        // l'ajoute et son miroir la lui apporte).
        public static bool IsMirrorRoot(string rootName) { return MirrorRoots.Contains(rootName); }
        static readonly HashSet<string> Ignore = new HashSet<string> { "FINISHED", "SAVEGAME", "LOAD", "EXISTS", "NOTEXISTS", "DONOTEXIST", "DOESNOTEXIST", "SAVE", "LOOP" };
        static readonly HashSet<string> InputActions = new HashSet<string> { "MousePickEvent", "GetButtonDown", "GetButtonUp", "GetMouseButtonDown", "GetMouseButtonUp", "GetAxis", "GetKeyDown", "GetButton", "AnyKeyStoreString" };   // (AnyKeyStoreString : pave de la telecommande)

        class W
        {
            public string Key; public PlayMakerFSM F; public bool Persistent, Mirror;
            public bool HostDriven, External, Tv;   // voir HostDrivenFsm / ExternalFsm
            public HashSet<string> InputStates = new HashSet<string>();
            public HashSet<string> GlobalEvents = new HashSet<string>();
            public float WindowStart, NoisySince; public int Count; public bool Noisy;
            public HashSet<string> Entered = new HashSet<string>();   // etats traverses pendant un rejeu
            public HashSet<string> PersonalStates = new HashSet<string>();
            public Dictionary<string, HashSet<string>> InputEvents = new Dictionary<string, HashSet<string>>();
            public Dictionary<string, bool> SafeCache = new Dictionary<string, bool>();
            public Dictionary<string, float> Sent = new Dictionary<string, float>();   // miroir (hote)
            public Dictionary<string, string> SentLists = new Dictionary<string, string>();
            public Dictionary<string, float> LocalRecent = new Dictionary<string, float>();   // transitions prises ici
            public List<FsmStateAction> Writes, Muted; public float MutedUntil;               // ecritures Player*
            public HashSet<string> Creates = new HashSet<string>();                           // etats qui creent un objet
        }

        static readonly Dictionary<string, W> byKey = new Dictionary<string, W>();
        static readonly HashSet<PlayMakerFSM> hooked = new HashSet<PlayMakerFSM>();
        static readonly HashSet<PlayMakerFSM> rejected = new HashSet<PlayMakerFSM>();
        static readonly HashSet<PlayMakerFSM> known = new HashSet<PlayMakerFSM>();   // suivis ou en attente
        static readonly List<W> pending = new List<W>();
        static float nextPending;
        static readonly List<KeyValuePair<float, Peer>> snapshots = new List<KeyValuePair<float, Peer>>();
        static readonly Dictionary<string, float> houseSent = new Dictionary<string, float>();
        static float nextScan = -1, nextMirror, loadedAt, nextWarn, lastInput = -100, nextStopCheck;
        static bool applying;
        static int sentEvents, recvEvents;

        class Hook : ModHook
        {
            public override string Module { get { return "monde"; } }
            public W J; public string State;
            public override void OnEnter()
            {
                try
                {
                    if (J.Creates.Contains(State)) SoonScan();
                    if (Replay.Depth == 0)
                    {
                        if (J.Muted != null && J.InputStates.Contains(State)) Unmute(J);   // la chaine rejouee est finie
                        OnLocal(J, State);
                    }
                    else J.Entered.Add(State);
                }
                catch (System.Exception e) { Replay.HookError(e); }
                Finish();
            }
        }

        public static bool Tracks(PlayMakerFSM f) { return hooked.Contains(f); }

        // Cle de l'autre introuvable ici : l'objet a pu changer de parent entre les deux releves (la tele
        // allumee prend Systems/TV/TVPrograms sous elle). On cherche alors par le nom de l'objet et de
        // l'automate, s'il est unique.
        static readonly Dictionary<string, W> alias = new Dictionary<string, W>();
        static bool Lookup(string key, out W j)
        {
            if (byKey.TryGetValue(key, out j)) return true;
            if (alias.TryGetValue(key, out j) && j.F != null) return true;
            string tail = key.Substring(key.LastIndexOf('/') + 1);
            W found = null;
            foreach (W x in byKey.Values)
                if (x.Key == tail || x.Key.EndsWith("/" + tail)) { if (found != null) { j = null; return false; } found = x; }
            j = found;
            if (found != null) { alias[key] = found; Log.Info("monde : " + key + " = " + found.Key + " ici"); }
            return found != null;
        }

        // Un automate suivi vient de creer un objet (commande, colis...) : releve dans 1,5 s, pour que
        // l'objet soit suivi avant qu'un joueur s'en serve. (Aussi Calls : commande creee ou rejouee.)
        public static void SoonScan()
        {
            float t = Time.realtimeSinceStartup + 1.5f;
            if (nextScan > t) nextScan = t;
            rootsDirty = true;   // l'objet cree peut etre une nouvelle racine (colis...)
        }

        // Logique tiree au hasard qui doit etre la meme pour tous : seul l'hote la fait tourner, les
        // invites la suivent etat par etat (leur copie est arretee). Telephone : qui appelle et quand.
        // Tele (Systems/TV/TVPrograms : grille, episodes tires au sort, pubs) : suivie seulement tant que
        // la tele de l'hote est allumee (TVOn) ; sinon chacun garde la sienne (un invite seul devant une
        // autre tele).
        static bool HostDrivenFsm(PlayMakerFSM f)
        {
            return (f.FsmName == "Ring" || f.FsmName == "Jokes") && f.gameObject.name.StartsWith("PhoneLogic") || IsTv(f);
        }
        static bool IsTv(PlayMakerFSM f) { return f.gameObject.name == "TVPrograms" && (f.FsmName == "Schedule" || f.FsmName == "ADs"); }
        static bool hostTvOn;   // tele de l'hote allumee (dernier etat recu de sa grille)

        // Automates commandes par un AUTRE automate apres une action du joueur (decrocher le telephone
        // envoie ANSWER a la sonnerie) : ces evenements-la comptent comme une action du joueur.
        static bool ExternalFsm(PlayMakerFSM f) { return f.gameObject.name.StartsWith("Ringing"); }

        // Variables objet envoyees par leur chemin (la fiche de la commande en cours).
        // Part : le CD tenu en main que le point d'insertion pose (boitier, chaine, lecteur de voiture : DiscTrigger*).
        static bool GoVar(string n) { return n == "CurrentListing" || n == "FoundListing" || n == "Part"; }

        // Les trois CD s'appellent tous cd(itemx) : designes par leur disque (Data.ThisCD), "cd:CD1".
        static string GoRef(GameObject go)
        {
            if (go == null) return "";
            if (go.name == "cd(itemx)") { string k = Props.CdKey(go); if (k != null) return "cd:" + k.Substring("w:cd:".Length); }
            return Recon.Path(go.transform);
        }

        static GameObject GoFind(string r)
        {
            if (r.StartsWith("cd:")) return Props.FindCd(r.Substring(3));
            return Game.FindAny(r);
        }

        // Commande de pieces (CARPARTS/PARTSYSTEM/OrdersSpawnerYP/OrderYP7::Data, ...AMIS/OrderAMIS3::Data) : creee
        // par Calls (meme nom chez tous). Son attente avant livraison (WaitTime, tiree au hasard chez chacun), son
        // etat (OrderActive : avis de colis, guichet) et sa liste de pieces suivent l'hote (miroir) ; le paiement
        // au guichet (PAYMENT) est rejoue chez tous : le colis apparait partout, pas seulement chez celui qui paie.
        static bool IsOrder(string path) { return path.Contains("/OrdersSpawner") && path.Contains("::Data"); }
        public static int Count { get { return byKey.Count; } }

        public static void OnLevelLoaded()
        {
            byKey.Clear(); hooked.Clear(); rejected.Clear(); snapshots.Clear(); houseSent.Clear(); known.Clear(); pending.Clear(); pathOf.Clear(); mutedList.Clear(); alias.Clear();
            hostTvOn = false; scanIdx = -1; scanRoots = null; rootsDirty = true; npcLeft = npcLogged = 0;
            loadedAt = Time.realtimeSinceStartup;
            nextScan = PlayerSync.InGame ? loadedAt + 16f : -1;
        }

        public static void ScheduleSnapshot(Peer p) { snapshots.Add(new KeyValuePair<float, Peer>(Time.realtimeSinceStartup + 22f, p)); }

        // ---------------------------------------------------------------- choix des automates
        static bool Skip(PlayMakerFSM f)
        {
            Transform root = f.transform.root;
            if (!root.gameObject.activeInHierarchy) return true;                        // modeles (prefabs)
            if (SkipRoots.Contains(root.name) || root.name.StartsWith("MWCoop") || Game.UnderPlayer(f.transform)) return true;
            // Vehicules : Jobs, CarDoors... Sauf le lecteur CD (DiscTriggerPlayer* :: Data, point d'insertion du CD).
            if (root.GetComponent("CarDynamics") != null && !(f.gameObject.name.StartsWith("DiscTrigger") && f.FsmName == "Data")) return true;
            if (SkipFsmNames.Contains(f.FsmName)) return true;
            string n = f.gameObject.name;
            // Objets portes : Props, Consume. Sauf l'ouverture des boitiers de CD (Systems/CDs/cd case(itemN) :: Use,
            // Bool test -> Open/Close), et la telecommande de la tele (Use : teletexte allume/eteint, Input : numero de
            // page) -- personne d'autre ne les suit ; sans elle, la tele des autres ne changeait pas (retour de JD, 05/10).
            bool remote = n.StartsWith("tv remote control(item") && (f.FsmName == "Use" || f.FsmName == "Input");
            if ((n.Contains("(itemx)") || n.Contains("(item")) && !(n.StartsWith("cd case(item") && f.FsmName == "Use") && !remote) return true;
            foreach (string s in SkipObjects) if (n.Contains(s) || root.name.Contains(s)) return true;
            for (Transform p = f.transform; p != null; p = p.parent)
            {
                string pn = p.name;
                foreach (string s in SkipParents) if (pn.StartsWith(s, System.StringComparison.Ordinal)) return true;
            }
            if (f.FsmName == "Data" && Parts.IdOf(f.gameObject).Length > 0) return true;  // pieces : Parts
            if (n.StartsWith("VINP")) return true;                                       // points de montage : Parts
            if (Interactions.Tracks(f) || Interactions.Wants(f) || Jobs.Tracks(f) || CarDoors.Tracks(f) || Consume.Tracks(f)) return true;
            if (root.name == "JOBS" && f.FsmName != "Use" && HasSave(f)) return true;   // boulots : Jobs (meme s'il ne les a pas encore vus)
            if (n == "CashRegisterLogic") return true;                                    // magasin : Shop
            if (n.StartsWith("fuse holder")) return true;                                 // porte-fusibles : Home (crees tard chez l'invite, ils etaient pris ici avant lui)
            // Createurs d'objets (pieces, articles) : jamais rejoues directement -- c'est l'action qui les
            // declenche (ouvrir un colis, passer une commande) qui l'est, sinon l'objet apparaitrait en
            // double. Sauf les createurs de COMMANDES (OrdersSpawner*), seul chemin de la commande -- d'ordinaire
            // pris avant nous par Calls (annonce retrouvee par son nom d'origine, meme liste chez tous).
            try { if (f.Fsm.GetState("Create product") != null && !n.StartsWith("OrdersSpawner")) return true; } catch { }
            return false;
        }

        static bool HasSave(PlayMakerFSM f)
        {
            foreach (FsmString x in f.FsmVariables.StringVariables) if (x.Name.StartsWith("UniqueTag") || x.Name.StartsWith("UT")) return true;
            return false;
        }

        // Automate de PNJ qu'un joueur fait reagir : par son nom (colere, coup, voiture, comptoir) ou parce qu'il
        // attend un clic ou une touche (objet tenu par le PNJ qu'on peut prendre). Actions non chargees : non.
        public static bool PlayerCaused(PlayMakerFSM f)
        {
            if (NpcPlayerFsms.Contains(f.FsmName)) return true;
            try
            {
                foreach (FsmState st in f.Fsm.States)
                    foreach (FsmStateAction a in st.Actions)
                        if (a != null && InputActions.Contains(a.GetType().Name)) return true;
            }
            catch { }
            return false;
        }

        // Lit les actions : commandes du joueur, references au joueur / a son interface, SAVEGAME.
        // 'personal' en sortie : l'automate touche a la sauvegarde (jamais rejoue du tout). Les etats qui
        // touchent au joueur sont notes un par un (PersonalStates).
        static bool Classify(PlayMakerFSM f, W w, out bool personal)
        {
            personal = false;
            foreach (FsmString s in f.FsmVariables.StringVariables)
                if (s.Name.StartsWith("UniqueTag") || s.Name.StartsWith("UT")) w.Persistent = true;
            foreach (FsmTransition t in f.Fsm.GlobalTransitions) if (!Ignore.Contains(t.EventName)) w.GlobalEvents.Add(t.EventName);
            foreach (FsmState st in f.Fsm.States)
            {
                var inEv = new HashSet<string>();
                foreach (FsmStateAction a in st.Actions)
                {
                    if (a == null) continue;
                    bool input = InputActions.Contains(a.GetType().Name);
                    if (a.GetType().Name == "CreateObject") w.Creates.Add(st.Name);
                    if (input) w.InputStates.Add(st.Name);
                    foreach (FieldInfo fi in a.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
                    {
                        object v = fi.GetValue(a);
                        GameObject go = null;
                        if (v is FsmGameObject) go = ((FsmGameObject)v).Value;
                        else if (v is FsmOwnerDefault) { var od = (FsmOwnerDefault)v; if (od.OwnerOption != OwnerDefaultOption.UseOwner) go = od.GameObject.Value; }
                        // Point d'insertion d'un CD : 'Find correct part' LIT la main du joueur (GetChild de ItemPivot) ;
                        // ce n'est pas agir sur lui, et sans cela la pose du CD (ASSEMBLE) n'etait jamais envoyee.
                        bool reads = f.gameObject.name.StartsWith("DiscTrigger") && a.GetType().Name.StartsWith("Get");
                        if (go != null && PersonalRoots.Contains(Game.RootName(go.transform)) && !reads) w.PersonalStates.Add(st.Name);
                        var nv = v as NamedVariable;
                        if (nv != null && nv.UseVariable && nv.Name.StartsWith("Player") && !Game.LocalVar(f, nv.Name)
                            && (v is FsmGameObject || nv.Name == "PlayerStop" || nv.Name == "PlayerInMenu" || nv.Name == "PlayerSeated" || nv.Name == "PlayerSleeps"))
                            w.PersonalStates.Add(st.Name);
                        if (v is FsmEvent && ((FsmEvent)v).Name == "SAVEGAME") personal = true;
                        if (v is FsmString && ((FsmString)v).Value == "SAVEGAME") personal = true;
                        if (input && v is FsmEvent && v != null) inEv.Add(((FsmEvent)v).Name);
                    }
                }
                w.InputEvents[st.Name] = inEv;
            }
            w.HostDriven = HostDrivenFsm(f);
            w.Tv = IsTv(f);
            w.External = ExternalFsm(f);
            return w.Persistent || w.InputStates.Count > 0 || w.GlobalEvents.Count > 0 || w.HostDriven || w.External;
        }

        // L'etat 'state' et tout ce qui s'enchaine automatiquement apres lui laissent-ils le joueur tranquille ?
        static bool Safe(W w, string state)
        {
            bool r;
            if (w.SafeCache.TryGetValue(state, out r)) return r;
            var seen = new HashSet<string>();
            var todo = new Stack<string>();
            todo.Push(state);
            r = true;
            while (todo.Count > 0 && r)
            {
                string s = todo.Pop();
                if (!seen.Add(s)) continue;
                if (w.PersonalStates.Contains(s)) { r = false; break; }
                FsmState st = w.F.Fsm.GetState(s);
                if (st == null) continue;
                HashSet<string> inEv;
                w.InputEvents.TryGetValue(s, out inEv);
                foreach (FsmTransition t in st.Transitions)
                    if (inEv == null || !inEv.Contains(t.EventName)) todo.Push(t.ToState);   // automatique
            }
            w.SafeCache[state] = r;
            return r;
        }

        // Cle = chemin::automate#k, k compte sur tous les automates du meme chemin (suivis ou non, actifs
        // ou non) dans l'ordre de la hierarchie : la meme cle designe le meme automate chez chacun.
        static readonly Dictionary<PlayMakerFSM, string> pathOf = new Dictionary<PlayMakerFSM, string>();

        // Releve etale sur plusieurs images (4 ms par image, racine par racine) : plus d'a-coup toutes les
        // 60 s. Les racines de la scene sont relevees une fois, puis de nouveau seulement quand un objet a
        // ete cree (SoonScan).
        static List<GameObject> scanRoots;
        public static List<GameObject> CachedRoots { get { return scanRoots; } }   // (audit : evite un releve de 80 ms)
        static bool rootsDirty = true;
        static int scanIdx = -1;
        static Dictionary<string, int> scanSeen;
        static readonly System.Diagnostics.Stopwatch scanWatch = new System.Diagnostics.Stopwatch();

        static void StartScan()
        {
            if (rootsDirty || scanRoots == null)
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                scanRoots = Recon.SceneRoots();
                scanRoots.Sort((x, y) => { int c = string.CompareOrdinal(x.name, y.name); return c != 0 ? c : x.transform.GetSiblingIndex().CompareTo(y.transform.GetSiblingIndex()); });
                rootsDirty = false;
                if (sw.ElapsedMilliseconds > 25) Log.Info("monde : racines relevees en " + sw.ElapsedMilliseconds + " ms");
            }
            scanIdx = 0;
            scanSeen = new Dictionary<string, int>();
        }

        // Vrai quand le releve est fini.
        static bool ScanStep()
        {
            scanWatch.Reset(); scanWatch.Start();
            while (scanIdx < scanRoots.Count && scanWatch.ElapsedMilliseconds < 4)
            {
                GameObject root = scanRoots[scanIdx++];
                long t0 = scanWatch.ElapsedMilliseconds;
                if (root != null) ScanRoot(root, scanSeen);
                if (scanWatch.ElapsedMilliseconds - t0 > 25 && root != null) Log.Info("monde : releve de " + root.name + " " + (scanWatch.ElapsedMilliseconds - t0) + " ms");
            }
            if (scanIdx < scanRoots.Count) return false;
            scanIdx = -1;
            LogAdded();
            return true;
        }

        static void ScanRoot(GameObject root, Dictionary<string, int> seen)
        {
                foreach (PlayMakerFSM f in root.GetComponentsInChildren<PlayMakerFSM>(true))
                {
                    if (f.hideFlags != HideFlags.None) continue;
                    string path;
                    if (!pathOf.TryGetValue(f, out path)) { path = Recon.Path(f.transform) + "::" + f.FsmName; pathOf[f] = path; }
                    int k; seen.TryGetValue(path, out k); seen[path] = k + 1;
                    if (known.Contains(f) || rejected.Contains(f)) continue;
                    if (Skip(f)) { if (root.activeInHierarchy) rejected.Add(f); continue; }
                    // Miroir aussi des petites annonces de pieces (PhoneNumbers, tirees au hasard chaque semaine), des
                    // commandes en attente (IsOrder) et de la poste (avis de colis lu : guichet ouvert).
                    bool mirror = MirrorRoots.Contains(root.name) || f.FsmName == "Fuelprices" || path.Contains("/PhoneNumbers/")
                                  || IsOrder(path) || path.EndsWith("/PostSystem::Logic");
                    var w = new W { Key = path + "#" + k, F = f, Mirror = mirror };
                    known.Add(f);
                    // Objet inactif : ses actions ne sont pas chargees ; on le reprend quand il s'active.
                    if (!f.gameObject.activeInHierarchy || !TryHook(w)) pending.Add(w);
                }
        }

        static int addedSinceLog, npcLeft, npcLogged;
        static void LogAdded()
        {
            if (npcLeft != npcLogged) { npcLogged = npcLeft; Log.Info("monde : " + npcLeft + " automates de PNJ laisses a leur logique (corps suivi par Npcs)"); }
            if (addedSinceLog == 0) return;
            int p = 0; foreach (W x in byKey.Values) if (x.Persistent) p++;
            Log.Info("monde : " + addedSinceLog + " automates de plus suivis (" + byKey.Count + " en tout, dont " + p + " sauvegardes, " + pending.Count + " en attente)");
            addedSinceLog = 0;
        }

        // Vrai : traite (suivi ou ecarte pour de bon) ; faux : a reprendre plus tard.
        static bool TryHook(W w)
        {
            PlayMakerFSM f = w.F;
            if (f == null) return true;
            bool personal;
            try { if (!Classify(f, w, out personal)) { rejected.Add(f); return true; } }
            catch { return false; }
            if (personal) { rejected.Add(f); return true; }
            // Logique propre d'un PNJ (marche WALK, colere ANGRY, ragdoll, telephone, regard) : chacun la sienne,
            // le corps visible vient de l'hote (Npcs). Rejouee, elle faisait sauter le PNJ de l'autre a un autre
            // point de passage. Restent ce qu'un joueur provoque (PlayerCaused), qui va a l'hote.
            if (Npcs.IsNpcLogic(f.transform) && !PlayerCaused(f)) { rejected.Add(f); npcLeft++; return true; }
            if (byKey.ContainsKey(w.Key)) return true;
            if (!Replay.Claim(f, "monde")) { rejected.Add(f); return true; }   // deja a un autre module
            try
            {
                foreach (FsmState st in f.Fsm.States)
                {
                    var list = new List<FsmStateAction>(st.Actions);
                    list.Insert(0, new Hook { J = w, State = st.Name });
                    st.Actions = list.ToArray();
                }
            }
            catch { return false; }
            hooked.Add(f);
            byKey[w.Key] = w;
            addedSinceLog++;
            return true;
        }

        // Toutes les 2 s : les automates en attente dont l'objet vient de s'activer.
        static void CheckPending()
        {
            for (int i = pending.Count - 1; i >= 0; i--)
            {
                W w = pending[i];
                if (w.F == null) { pending.RemoveAt(i); continue; }
                if (w.F.gameObject.activeInHierarchy && TryHook(w)) pending.RemoveAt(i);
            }
            LogAdded();
        }

        // ---------------------------------------------------------------- actions des joueurs
        static void OnLocal(W j, string state)
        {
            if (!Session.Active || Session.RemoteCount == 0 || Time.realtimeSinceStartup - loadedAt < 25f) return;
            FsmTransition tr = j.F.Fsm.LastTransition;
            FsmState prev = j.F.Fsm.PreviousActiveState;
            float now = Time.realtimeSinceStartup;
            if (j.HostDriven)
            {
                // Logique de l'hote : chaque etat est envoye (FINISHED compris), les invites s'y recalent.
                if (Session.IsHost) Send(j, prev, tr != null ? tr.EventName : "", 2, state);
                return;
            }
            if (tr == null || Ignore.Contains(tr.EventName) || tr.ToState != state) return;
            j.LocalRecent[(prev != null ? prev.Name : "") + "|" + tr.EventName + "|" + state] = now;
            if (j.GlobalEvents.Contains(tr.EventName)) j.LocalRecent["g|" + tr.EventName] = now;
            if (j.External)
            {
                // Decroche (ANSWER), ou autre evenement juste apres une action du joueur : chez les autres,
                // l'appel est rejoue sans voix ni sous-titres, puis le telephone raccroche.
                if (now - lastInput <= 1f) Send(j, prev, tr.EventName, 3, state);
                return;
            }
            if (state == "Wait player" || state == "Mouse off" || state == "Mouse off 2" || state == "Wait button") return;
            bool global = j.GlobalEvents.Contains(tr.EventName);
            bool byPlayer = prev != null && j.InputStates.Contains(prev.Name);
            // Provoquee par le joueur : il vient d'agir (clic, touche, molette) ET la transition sort d'un
            // etat qui l'ecoute, ou c'est un evenement global (paiement...). Le reste (horloge, radio,
            // reveil...) tourne pareil chez chacun : le rejouer le doublerait.
            // Les commandes (OrdersSpawner*) arrivent apres l'appel ou le courrier, longtemps apres le
            // dernier clic : seul celui qui commande les declenche, elles passent toujours.
            bool order = global && j.Key.Contains("OrdersSpawner");
            // Paiement d'une commande au guichet (NotificationsPile -> PAYMENT : transition locale de la commande,
            // sans saisie, renvoyee par le guichet) : seul celui qui paie la declenche, elle passe toujours ; chez
            // les autres le colis est cree aussi (recalage sur 'Spawn package' si leur commande attend encore).
            if (tr.EventName == "PAYMENT" && IsOrder(j.Key)) order = true;
            // Global diffuse par la logique du monde (horloge, hockey, radio : un automate sans aucune
            // commande du joueur) : chacun le recoit de son propre jeu, le rejouer le doublerait.
            if (global && !byPlayer && !order && !forceNext && SenderIsWorldLogic(j)) return;
            forceNext = false;
            if ((!global && !byPlayer && !order) || (now - lastInput > 1f && !order)) return;
            if (!Safe(j, state)) return;   // finirait par agir sur ce joueur-ci chez l'autre
            if (j.Noisy && now - j.NoisySince > 30f) { j.Noisy = false; j.WindowStart = now; j.Count = 0; }
            if (now - j.WindowStart > 10f) { j.WindowStart = now; j.Count = 0; }
            if (++j.Count > 40 || j.Noisy)
            {
                if (!j.Noisy) { Log.Warn("monde : " + j.Key + " change trop souvent, en pause 30 s"); j.Noisy = true; j.NoisySince = now; }
                return;
            }
            Send(j, prev, tr.EventName, global ? 1 : 0, state);
        }

        static bool forceNext;   // essais : le prochain global part comme s'il venait du joueur
        static readonly List<KeyValuePair<PlayMakerFSM, float>> reDisable = new List<KeyValuePair<PlayMakerFSM, float>>();

        // Automates allumes pour un rejeu (OnMessage) : recoupes 0,5 s apres.
        static void ReDisable()
        {
            float now = Time.realtimeSinceStartup;
            for (int i = reDisable.Count - 1; i >= 0; i--)
                if (now >= reDisable[i].Value)
                {
                    if (reDisable[i].Key != null) reDisable[i].Key.enabled = false;
                    reDisable.RemoveAt(i);
                }
        }
        static readonly Dictionary<Fsm, bool> worldLogic = new Dictionary<Fsm, bool>();

        // L'expediteur de l'evenement en cours est-il un automate de pure logique (aucune action de saisie,
        // hors interface du joueur) ? Inconnu (envoye par le code, ou par l'automate lui-meme) : non.
        static bool SenderIsWorldLogic(W j)
        {
            FsmEventData ed = Fsm.EventData;
            Fsm from = ed != null ? ed.SentByFsm : null;
            if (from == null || from == j.F.Fsm || from.Owner == null) return false;
            if (PersonalRoots.Contains(Game.RootName(from.Owner.transform))) return false;   // feuille, ecran, main du joueur
            bool r;
            if (worldLogic.TryGetValue(from, out r)) return r;
            r = true;
            foreach (FsmState st in from.States)
                foreach (FsmStateAction a in st.Actions)
                    if (a != null && InputActions.Contains(a.GetType().Name)) r = false;
            worldLogic[from] = r;
            return r;
        }

        // mode : 0 meme etat de depart -> meme evenement ; 1 evenement global ; 2 recalage direct (hote) ;
        // 3 rejeu muet (actions qui touchent au joueur coupees) puis repos.
        static void Send(W j, FsmState prev, string ev, int mode, string state)
        {
            NetWriter w = null;
            for (int pass = 0; pass < 2; pass++)
            {
                w = new NetWriter(Msg.WorldFsm).U8(Session.LocalId).Str(j.Key).Str(prev != null ? prev.Name : "").Str(ev).U8(mode).Str(state);
                WriteVars(j.F, w, pass == 0);
                if (w.Length <= MaxMsg) break;
                if (pass == 1) { Log.Warn("monde : " + j.Key + " trop gros a envoyer (" + w.Length + " o)"); return; }
            }
            if (++sentEvents <= 30 || sentEvents % 50 == 0) Log.Info("monde : " + j.Key + " " + (prev != null ? prev.Name : "?") + " -" + ev + "-> " + state);
            Session.SendAll(w, true);
        }

        static bool SkipVar(string n) { return n.StartsWith("UT") || n.StartsWith("UniqueTag"); }

        // Compteur de boucle de la poste (PostSystem::Logic parcourt ses commandes toutes les 10 s) : pas au miroir,
        // il casserait la boucle de l'invite en cours de route.
        static bool LoopVar(W j, string n) { return n == "Index" && j.Key.Contains("/PostSystem::"); }

        // Argent et corps du joueur (globales Player* nombres) : un rejeu ne les touche pas, ni sur le
        // moment (RestorePersonal) ni plus tard dans la chaine automatique (paie apres un minuteur...).
        // Les actions qui les ecrivent sont coupees jusqu'a ce que l'automate attende de nouveau le
        // joueur (ou 60 s).
        static readonly HashSet<string> WriteFields = new HashSet<string> { "floatVariable", "intVariable", "storeResult", "storeValue", "variable", "store" };
        static readonly List<W> mutedList = new List<W>();

        static void MuteWrites(W j)
        {
            if (j.Writes == null)
            {
                j.Writes = new List<FsmStateAction>();
                foreach (FsmState st in j.F.Fsm.States)
                    foreach (FsmStateAction a in st.Actions)
                    {
                        if (a == null || a is Hook) continue;
                        foreach (FieldInfo fi in a.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
                        {
                            if (!WriteFields.Contains(fi.Name)) continue;
                            var nv = fi.GetValue(a) as NamedVariable;
                            // (Booleens aussi : PlayerHandRight mis a vrai par le pistolet de la pompe rejoue laissait la main de
                            // l'invite « occupee » : plus de coup de poing ni de cigarette, vraie partie du 05/10.)
                            if (nv != null && nv.UseVariable && nv.Name.StartsWith("Player") && (nv is FsmFloat || nv is FsmInt || nv is FsmBool) && !Game.LocalVar(j.F, nv.Name))
                            { j.Writes.Add(a); break; }
                        }
                    }
            }
            if (j.Writes.Count == 0) return;
            if (j.Muted == null) { j.Muted = new List<FsmStateAction>(); mutedList.Add(j); }
            foreach (FsmStateAction a in j.Writes) if (a.Enabled) { a.Enabled = false; j.Muted.Add(a); }
            j.MutedUntil = Time.realtimeSinceStartup + 60f;
        }

        static void Unmute(W j)
        {
            if (j.Muted == null) return;
            foreach (FsmStateAction a in j.Muted) a.Enabled = true;
            j.Muted = null;
            mutedList.Remove(j);
        }

        // Actions qui touchent au joueur local : son, camera, interface (sous-titres), globales Player*.
        static List<FsmStateAction> Mute(W j)
        {
            var r = new List<FsmStateAction>();
            foreach (FsmState st in j.F.Fsm.States)
                foreach (FsmStateAction a in st.Actions)
                {
                    if (a == null || a is Hook || !a.Enabled) continue;
                    bool personal = false;
                    foreach (FieldInfo fi in a.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
                    {
                        object v = fi.GetValue(a);
                        GameObject go = null;
                        if (v is FsmGameObject) go = ((FsmGameObject)v).Value;
                        else if (v is FsmOwnerDefault) { var od = (FsmOwnerDefault)v; if (od.OwnerOption != OwnerDefaultOption.UseOwner) go = od.GameObject.Value; }
                        if (go != null && PersonalRoots.Contains(Game.RootName(go.transform))) personal = true;
                        var nv = v as NamedVariable;
                        if (nv != null && nv.UseVariable && (nv.Name.StartsWith("GUI") || nv.Name.StartsWith("Player")) && !Game.LocalVar(j.F, nv.Name)) personal = true;
                    }
                    if (a.GetType().Name.StartsWith("MasterAudio")) personal = true;   // la voix de l'appel, la tonalite
                    if (personal) { a.Enabled = false; r.Add(a); }
                }
            return r;
        }

        const int MaxMsg = 1100;

        static void WriteVars(PlayMakerFSM f, NetWriter w, bool lists)
        {
            FsmVariables v = f.FsmVariables;
            var ints = new List<FsmInt>(); foreach (FsmInt x in v.IntVariables) if (!SkipVar(x.Name)) ints.Add(x);
            var floats = new List<FsmFloat>(); foreach (FsmFloat x in v.FloatVariables) if (!SkipVar(x.Name)) floats.Add(x);
            var bools = new List<FsmBool>(); foreach (FsmBool x in v.BoolVariables) if (!SkipVar(x.Name)) bools.Add(x);
            w.U8(System.Math.Min(ints.Count, 255)); for (int i = 0; i < ints.Count && i < 255; i++) w.Str(ints[i].Name).I32(ints[i].Value);
            w.U8(System.Math.Min(floats.Count, 255)); for (int i = 0; i < floats.Count && i < 255; i++) w.Str(floats[i].Name).F32(floats[i].Value);
            w.U8(System.Math.Min(bools.Count, 255)); for (int i = 0; i < bools.Count && i < 255; i++) w.Str(bools[i].Name).Bool(bools[i].Value);
            var strs = new List<FsmString>(); foreach (FsmString x in v.StringVariables) if (!SkipVar(x.Name) && (x.Value ?? "").Length < 200) strs.Add(x);
            w.U8(System.Math.Min(strs.Count, 255)); for (int i = 0; i < strs.Count && i < 255; i++) w.Str(strs[i].Name).Str(strs[i].Value);
            var gos = new List<FsmGameObject>(); foreach (FsmGameObject x in v.GameObjectVariables) if (GoVar(x.Name)) gos.Add(x);
            w.U8(gos.Count); foreach (FsmGameObject x in gos) w.Str(x.Name).Str(GoRef(x.Value));
            if (lists) WriteLists(f, w); else w.U8(0);
        }

        // Listes du jeu (ArrayMaker) : celles de l'objet, et celle de la commande en cours (CurrentListing).
        static List<KeyValuePair<string, PlayMakerArrayListProxy>> Lists(PlayMakerFSM f)
        {
            var l = new List<KeyValuePair<string, PlayMakerArrayListProxy>>();
            AddLists(l, "", f.gameObject);
            FsmGameObject cl = f.FsmVariables.FindFsmGameObject("CurrentListing");
            if (cl != null && cl.Value != null) AddLists(l, "CurrentListing", cl.Value);
            return l;
        }

        // Une liste n'est transmise que si on sait la recopier exactement : 120 elements au plus, rien
        // que des nombres, textes, booleens ou objets de la scene, et un nom unique sur l'objet (sinon
        // le receveur la viderait ou la remplirait de vides).
        static void AddLists(List<KeyValuePair<string, PlayMakerArrayListProxy>> l, string owner, GameObject go)
        {
            PlayMakerArrayListProxy[] all = go.GetComponents<PlayMakerArrayListProxy>();
            foreach (PlayMakerArrayListProxy p in all)
            {
                if (!Sendable(p._arrayList, 120)) continue;
                int same = 0; foreach (PlayMakerArrayListProxy q in all) if ((q.referenceName ?? "") == (p.referenceName ?? "")) same++;
                if (same == 1) l.Add(new KeyValuePair<string, PlayMakerArrayListProxy>(owner, p));
            }
        }

        static bool Sendable(System.Collections.ArrayList a, int max)
        {
            if (a == null || a.Count > max) return false;
            foreach (object o in a)
                if (!(o is int || o is float || o is string || o is bool || (o is GameObject && (GameObject)o != null))) return false;
            return true;
        }

        static void WriteLists(PlayMakerFSM f, NetWriter w)
        {
            List<KeyValuePair<string, PlayMakerArrayListProxy>> l = Lists(f);
            w.U8(System.Math.Min(l.Count, 16));
            for (int k = 0; k < l.Count && k < 16; k++)
            {
                System.Collections.ArrayList a = l[k].Value._arrayList;
                int n = a != null ? System.Math.Min(a.Count, 120) : 0;
                w.Str(l[k].Key).Str(l[k].Value.referenceName ?? "").U16(n);
                for (int i = 0; i < n; i++)
                {
                    object o = a[i];
                    if (o is int) w.U8(0).I32((int)o);
                    else if (o is float) w.U8(1).F32((float)o);
                    else if (o is string) w.U8(2).Str((string)o);
                    else if (o is bool) w.U8(3).Bool((bool)o);
                    else if (o is GameObject && (GameObject)o != null) w.U8(4).Str(Recon.Path(((GameObject)o).transform));
                    else w.U8(5);
                }
            }
        }

        class ListData { public string Owner, Ref; public List<object> Items = new List<object>(); }

        static List<ListData> ReadLists(NetReader r)
        {
            var res = new List<ListData>();
            if (!r.More) return res;
            int c = r.U8();
            for (int k = 0; k < c; k++)
            {
                var d = new ListData { Owner = r.Str(), Ref = r.Str() };
                int n = r.U16();
                for (int i = 0; i < n; i++)
                {
                    int t = r.U8();
                    if (t == 0) d.Items.Add(r.I32());
                    else if (t == 1) d.Items.Add(r.F32());
                    else if (t == 2) d.Items.Add(r.Str());
                    else if (t == 3) d.Items.Add(r.Bool());
                    else if (t == 4) d.Items.Add(Game.FindAny(r.Str()));
                    else d.Items.Add(null);
                }
                res.Add(d);
            }
            return res;
        }

        static void WriteListData(NetWriter w, List<ListData> lists)
        {
            w.U8(lists.Count);
            foreach (ListData d in lists)
            {
                w.Str(d.Owner).Str(d.Ref).U16(d.Items.Count);
                foreach (object o in d.Items)
                {
                    if (o is int) w.U8(0).I32((int)o);
                    else if (o is float) w.U8(1).F32((float)o);
                    else if (o is string) w.U8(2).Str((string)o);
                    else if (o is bool) w.U8(3).Bool((bool)o);
                    else if (o is GameObject && (GameObject)o != null) w.U8(4).Str(Recon.Path(((GameObject)o).transform));
                    else w.U8(5);
                }
            }
        }

        static void ApplyLists(PlayMakerFSM f, List<ListData> lists)
        {
            List<KeyValuePair<string, PlayMakerArrayListProxy>> mine = Lists(f);
            foreach (ListData d in lists)
                foreach (KeyValuePair<string, PlayMakerArrayListProxy> kv in mine)
                {
                    if (kv.Key != d.Owner || (kv.Value.referenceName ?? "") != d.Ref) continue;
                    System.Collections.ArrayList a = kv.Value._arrayList;
                    if (a == null) break;
                    a.Clear();
                    foreach (object o in d.Items) a.Add(o);
                    break;
                }
        }

        // Argent et corps du joueur local : notes avant un rejeu, remis apres.
        class Personal { public List<KeyValuePair<FsmFloat, float>> F = new List<KeyValuePair<FsmFloat, float>>(); public List<KeyValuePair<FsmInt, int>> I = new List<KeyValuePair<FsmInt, int>>(); public List<KeyValuePair<FsmBool, bool>> B = new List<KeyValuePair<FsmBool, bool>>(); }
        static Personal SavePersonal()
        {
            var p = new Personal();
            foreach (FsmFloat x in FsmVariables.GlobalVariables.FloatVariables) if (x.Name.StartsWith("Player")) p.F.Add(new KeyValuePair<FsmFloat, float>(x, x.Value));
            foreach (FsmInt x in FsmVariables.GlobalVariables.IntVariables) if (x.Name.StartsWith("Player")) p.I.Add(new KeyValuePair<FsmInt, int>(x, x.Value));
            foreach (FsmBool x in FsmVariables.GlobalVariables.BoolVariables) if (x.Name.StartsWith("PlayerHand")) p.B.Add(new KeyValuePair<FsmBool, bool>(x, x.Value));
            return p;
        }
        static void RestorePersonal(Personal p)
        {
            foreach (var kv in p.F) kv.Key.Value = kv.Value;
            foreach (var kv in p.I) kv.Key.Value = kv.Value;
            foreach (var kv in p.B) kv.Key.Value = kv.Value;
        }

        public static void OnMessage(Peer from, NetReader r)
        {
            byte[] raw = Session.IsHost ? r.Rest() : null;
            int who = r.U8();
            if (Session.IsHost) who = from.Id;
            string key = r.Str(), prev = r.Str(), ev = r.Str();
            int global = r.U8();
            string state = r.Str();
            var ints = new List<KeyValuePair<string, int>>();
            var floats = new List<KeyValuePair<string, float>>();
            var bools = new List<KeyValuePair<string, bool>>();
            for (int i = 0, n = r.U8(); i < n; i++) ints.Add(new KeyValuePair<string, int>(r.Str(), r.I32()));
            for (int i = 0, n = r.U8(); i < n; i++) floats.Add(new KeyValuePair<string, float>(r.Str(), r.F32()));
            for (int i = 0, n = r.U8(); i < n; i++) bools.Add(new KeyValuePair<string, bool>(r.Str(), r.Bool()));
            var strs = new List<KeyValuePair<string, string>>();
            var gos = new List<KeyValuePair<string, string>>();
            for (int i = 0, n = r.U8(); i < n; i++) strs.Add(new KeyValuePair<string, string>(r.Str(), r.Str()));
            for (int i = 0, n = r.U8(); i < n; i++) gos.Add(new KeyValuePair<string, string>(r.Str(), r.Str()));
            List<ListData> lists = ReadLists(r);
            // Relais aux autres invites : le message tel quel, seul le numero du joueur est fixe par l'hote.
            if (Session.IsHost) Session.Broadcast(new NetWriter(Msg.WorldFsm).U8(who).Raw(raw, 1, raw.Length - 1), true, who);
            W j;
            if (!Lookup(key, out j) || j.F == null)
            {
                if (Time.realtimeSinceStartup >= nextWarn) { nextWarn = Time.realtimeSinceStartup + 10f; Log.Warn("monde : " + key + " introuvable ici"); }
                return;
            }
            // Deja fait ici a l'instant (la meme logique a tourne chez les deux) : pas une 2e fois. Jamais pour un clic de
            // joueur (evenement d'une action de commande de l'etat de depart) : chaque clic est une action a part -- un
            // robinet ouvert ici puis ferme par l'autre dans les 10 s ne se fermait pas ici (meme passage "Wait button
            // -USE-> Position"), l'eau continuait de couler (retour de JD, 06/10).
            HashSet<string> inEv;
            bool click = prev != null && j.InputEvents.TryGetValue(prev, out inEv) && inEv.Contains(ev);
            float done, now = Time.realtimeSinceStartup;
            if (global < 2 && !click && (j.LocalRecent.TryGetValue(prev + "|" + ev + "|" + state, out done) && now - done < 10f
                               || global == 1 && j.LocalRecent.TryGetValue("g|" + ev, out done) && now - done < 10f))
            {
                Log.Info("monde de #" + who + " : " + key + " -" + ev + "-> deja fait ici");
                return;
            }
            FsmVariables v = j.F.FsmVariables;
            // Automate coupe ici (pave de la telecommande : le jeu ne l'allume que quand on la vise) : allume le temps
            // que sa chaine se deroule (0,5 s), puis recoupe -- sinon l'etat rejoue reste en plan (page jamais tapee).
            if (!j.F.enabled && global < 2) { j.F.enabled = true; reDisable.Add(new KeyValuePair<PlayMakerFSM, float>(j.F, Time.realtimeSinceStartup + 0.5f)); }
            Personal mine = SavePersonal();
            if (global < 2) MuteWrites(j);
            // Celui qui a agi a deja verifie qu'il pouvait payer : ici, la meme verification (argent du
            // joueur local) passe toujours ; l'argent est remis juste apres (RestorePersonal).
            FsmFloat money = FsmVariables.GlobalVariables.FindFsmFloat("PlayerMoney");
            if (money != null && global < 2) money.Value = 1e7f;
            applying = true; Replay.Depth++;
            try
            {
                foreach (var x in ints) { FsmInt t = v.FindFsmInt(x.Key); if (t != null) t.Value = x.Value; }
                foreach (var x in floats) { FsmFloat t = v.FindFsmFloat(x.Key); if (t != null) t.Value = x.Value; }
                foreach (var x in bools) { FsmBool t = v.FindFsmBool(x.Key); if (t != null) t.Value = x.Value; }
                foreach (var x in strs) { FsmString t = v.FindFsmString(x.Key); if (t != null) t.Value = x.Value; }
                foreach (var x in gos)
                {
                    FsmGameObject t = v.FindFsmGameObject(x.Key);
                    if (t == null) continue;
                    GameObject go = x.Value.Length > 0 ? GoFind(x.Value) : null;
                    if (go != null || x.Value.Length == 0) t.Value = go;
                }
                ApplyLists(j.F, lists);
                // Evenement global (paiement...) : renvoye tel quel ; sinon meme etat de depart -> meme
                // evenement, et a defaut recalage direct sur l'etat d'arrivee (ses actions sont jouees).
                // (Un etat de passage deja traverse par l'evenement n'est pas rejoue une 2e fois.)
                j.Entered.Clear();
                if (global == 2)
                {
                    // Logique de l'hote (tele : seulement si la sienne est allumee).
                    if (j.Tv && j.F.FsmName == "Schedule") { FsmBool on = j.F.FsmVariables.FindFsmBool("TVOn"); hostTvOn = on != null && on.Value; }
                    if ((!j.Tv || hostTvOn) && !Calls.RunsHere(j.F) && j.F.Fsm.GetState(state) != null) Game.SetState(j.F, state);   // telephone repris ici (hote loin) : sa logique tourne ici
                }
                else if (global == 3)
                {
                    // Les consequences de l'appel (boulot accepte, repere sur la carte, drapeaux) ont lieu
                    // ici aussi, mais la voix et les sous-titres restent chez celui qui a decroche.
                    List<FsmStateAction> muted = Mute(j);
                    try { if (j.F.ActiveStateName == prev) j.F.SendEvent(ev); }
                    finally { foreach (FsmStateAction a in muted) a.Enabled = true; }
                    if (j.F.Fsm.GetState("Disable phone") != null) Game.SetState(j.F, "Disable phone");
                }
                else if (global == 1 || j.F.ActiveStateName == prev) j.F.SendEvent(ev);
                if (global < 2 && !j.Entered.Contains(state) && j.F.ActiveStateName != state && j.F.Fsm.GetState(state) != null) Game.SetState(j.F, state);
            }
            finally
            {
                applying = false; Replay.Depth--; RestorePersonal(mine);
                if (j.Muted != null && (j.InputStates.Contains(j.F.ActiveStateName) || string.IsNullOrEmpty(j.F.ActiveStateName))) Unmute(j);
            }
            if (++recvEvents <= 30 || recvEvents % 50 == 0) Log.Info("monde de #" + who + " : " + key + " -" + ev + "-> " + j.F.ActiveStateName + " (voulu " + state + ")");
        }

        // ---------------------------------------------------------------- miroir des etats (hote)
        public static void Update()
        {
            // [Coop] SynchroMonde=0 coupe ce module (au cas ou il generait en partie).
            if (!Session.Active || nextScan < 0 || Config.GetInt("Coop", "SynchroMonde", 1) == 0) return;
            float now = Time.realtimeSinceStartup;
            if (reDisable.Count > 0) ReDisable();
            // Releve complet toutes les 60 s (objets crees en jeu) ; les objets qui s'activent, toutes les 2 s.
            if (scanIdx >= 0) { if (ScanStep()) nextScan = now + 60f; }
            else if (now >= nextScan) StartScan();
            else if (now >= nextPending) { nextPending = now + 2f; CheckPending(); }
            if (Input.anyKeyDown || Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1) || Input.GetAxis("Mouse ScrollWheel") != 0f) lastInput = now;
            for (int i = mutedList.Count - 1; i >= 0; i--) if (now > mutedList[i].MutedUntil) Unmute(mutedList[i]);
            if (!Session.IsHost && now >= nextStopCheck)
            {
                nextStopCheck = now + 2f;
                foreach (W x in byKey.Values)
                {
                    if (!x.HostDriven || x.F == null) continue;
                    bool follow = (!x.Tv || hostTvOn) && !Calls.RunsHere(x.F);
                    if (x.F.enabled == follow) { x.F.enabled = !follow; Log.Info("monde : " + x.Key + (follow ? " suit l'hote" : " tourne ici")); }
                }
            }
            if (!Session.IsHost || Session.RemoteCount == 0) return;
            for (int i = snapshots.Count - 1; i >= 0; i--)
            {
                if (now < snapshots[i].Key) continue;
                Peer p = snapshots[i].Value;
                snapshots.RemoveAt(i);
                if (p.Accepted && Session.T.Peers.Contains(p))
                {
                    Mirror(p, true);
                    foreach (W x in byKey.Values)
                        if (x.HostDriven && x.F != null && !string.IsNullOrEmpty(x.F.ActiveStateName))
                        {
                            var w = new NetWriter(Msg.WorldFsm).U8(Session.LocalId).Str(x.Key).Str("").Str("").U8(2).Str(x.F.ActiveStateName);
                            WriteVars(x.F, w, false);
                            if (w.Length <= MaxMsg) Session.T.SendReliable(p, w.ToArray());
                        }
                }
            }
            if (now < nextMirror) return;
            nextMirror = now + 1f;
            Mirror(null, false);
        }

        static bool Changed(Dictionary<string, float> sent, string k, float v)
        {
            float old;
            if (sent.TryGetValue(k, out old) && Mathf.Abs(old - v) <= 0.005f + 0.002f * Mathf.Abs(v)) return false;
            sent[k] = v;
            return true;
        }

        // Lot : [cle][nb][(type, nom, valeur)...] ; cle "" = globales de la maison.
        static void Mirror(Peer only, bool all)
        {
            NetWriter w = null;
            int n = 0;
            System.Action flush = () => { if (w == null) return; if (only != null) Session.T.SendReliable(only, w.ToArray()); else Session.SendAll(w, true); w = null; };
            var entries = new List<KeyValuePair<string, object>>();
            foreach (W j in byKey.Values)
            {
                if (!j.Persistent || !j.Mirror || j.F == null) continue;
                entries.Clear();
                foreach (FsmFloat x in j.F.FsmVariables.FloatVariables) if (!SkipVar(x.Name) && (all || Changed(j.Sent, x.Name, x.Value))) entries.Add(new KeyValuePair<string, object>(x.Name, x.Value));
                foreach (FsmInt x in j.F.FsmVariables.IntVariables) if (!SkipVar(x.Name) && !LoopVar(j, x.Name) && (all || Changed(j.Sent, "i:" + x.Name, x.Value))) entries.Add(new KeyValuePair<string, object>(x.Name, x.Value));
                foreach (FsmBool x in j.F.FsmVariables.BoolVariables) if (!SkipVar(x.Name) && (all || Changed(j.Sent, "b:" + x.Name, x.Value ? 1 : 0))) entries.Add(new KeyValuePair<string, object>(x.Name, x.Value));
                // Listes de l'objet (annonces, numeros tires...) : envoyees quand leur contenu change.
                foreach (PlayMakerArrayListProxy pr in j.F.GetComponents<PlayMakerArrayListProxy>())
                {
                    System.Collections.ArrayList a = pr._arrayList;
                    if (!Sendable(a, 60)) continue;
                    var items = new List<object>(a.Count);
                    var sig = new System.Text.StringBuilder();
                    foreach (object o in a) { items.Add(o); sig.Append(o).Append('|'); }
                    string name = pr.referenceName ?? "", old;
                    if (!all && j.SentLists.TryGetValue(name, out old) && old == sig.ToString()) continue;
                    if (!all) j.SentLists[name] = sig.ToString();   // instantane d'un arrivant : les autres ne l'ont pas recu
                    if (Config.GetInt("Test", "JournalListes", 0) != 0) Log.Info("liste envoyee " + j.Key + " / " + name + " : " + Short(sig.ToString()));
                    entries.Add(new KeyValuePair<string, object>(name, items));
                }
                if (entries.Count == 0) continue;
                AddEntries(ref w, ref n, j.Key, entries, flush);
            }
            entries.Clear();
            foreach (FsmFloat x in FsmVariables.GlobalVariables.FloatVariables) if (x.Name.StartsWith("House") && (all || Changed(houseSent, x.Name, x.Value))) entries.Add(new KeyValuePair<string, object>(x.Name, x.Value));
            foreach (FsmBool x in FsmVariables.GlobalVariables.BoolVariables) if (x.Name.StartsWith("House") && (all || Changed(houseSent, "b:" + x.Name, x.Value ? 1 : 0))) entries.Add(new KeyValuePair<string, object>(x.Name, x.Value));
            if (entries.Count > 0) AddEntries(ref w, ref n, "", entries, flush);
            flush();
            if (all) Log.Info("monde : etat de " + byKey.Count + " automates envoye a " + only);
        }

        // Chaque entree est encodee a part pour connaitre sa taille exacte ; un automate trop gros
        // est coupe en plusieurs blocs (meme cle), une liste seule trop grosse est laissee de cote.
        static void AddEntries(ref NetWriter w, ref int n, string key, List<KeyValuePair<string, object>> entries, System.Action flush)
        {
            int head = 3 + System.Text.Encoding.UTF8.GetByteCount(key);
            var block = new List<byte[]>();
            int blockLen = head;
            for (int i = 0; i <= entries.Count; i++)
            {
                byte[] e = i < entries.Count ? Encode(entries[i]) : null;
                if (e != null && e.Length + head > 900) { Log.Warn("monde : " + entries[i].Key + " de " + key + " trop gros (" + e.Length + " o), pas envoye"); continue; }
                if (block.Count > 0 && (e == null || blockLen + e.Length > 900 || block.Count == 255))
                {
                    if (w != null && w.Length + blockLen > 1000) flush();
                    if (w == null) w = new NetWriter(Msg.WorldVars);
                    w.Str(key).U8(block.Count);
                    foreach (byte[] x in block) w.Raw(x);
                    n++;
                    block.Clear(); blockLen = head;
                }
                if (e != null) { block.Add(e); blockLen += e.Length; }
            }
        }

        static string Short(string s) { return s.Length > 120 ? s.Substring(0, 120) + "..." : s; }

        static byte[] Encode(KeyValuePair<string, object> entry)
        {
            var w = new NetWriter(Msg.WorldVars);
            object v = entry.Value;
            if (v is float) w.U8(0).Str(entry.Key).F32((float)v);
            else if (v is int) w.U8(1).Str(entry.Key).I32((int)v);
            else if (v is bool) w.U8(2).Str(entry.Key).Bool((bool)v);
            else
            {
                var items = (List<object>)v;
                w.U8(3).Str(entry.Key).U16(items.Count);
                foreach (object o in items)
                {
                    if (o is int) w.U8(0).I32((int)o);
                    else if (o is float) w.U8(1).F32((float)o);
                    else if (o is string) w.U8(2).Str((string)o);
                    else if (o is bool) w.U8(3).Bool((bool)o);
                    else if (o is GameObject && (GameObject)o != null) w.U8(4).Str(Recon.Path(((GameObject)o).transform));
                    else w.U8(5);
                }
            }
            byte[] all = w.ToArray();
            var r = new byte[all.Length - 1];
            System.Buffer.BlockCopy(all, 1, r, 0, r.Length);   // sans l'octet de type du message
            return r;
        }

        public static void OnVars(Peer from, NetReader r)
        {
            if (Session.IsHost) return;
            while (r.More)
            {
                string key = r.Str();
                int n = r.U8();
                W j = null;
                if (key.Length > 0) Lookup(key, out j);
                for (int i = 0; i < n; i++)
                {
                    int t = r.U8();
                    string name = r.Str();
                    if (t == 0)
                    {
                        float v = r.F32();
                        FsmFloat x = key.Length == 0 ? FsmVariables.GlobalVariables.FindFsmFloat(name) : j != null && j.F != null ? j.F.FsmVariables.FindFsmFloat(name) : null;
                        if (x != null) x.Value = v;
                    }
                    else if (t == 1)
                    {
                        int v = r.I32();
                        FsmInt x = key.Length == 0 ? FsmVariables.GlobalVariables.FindFsmInt(name) : j != null && j.F != null ? j.F.FsmVariables.FindFsmInt(name) : null;
                        if (x != null) x.Value = v;
                    }
                    else if (t == 2)
                    {
                        bool v = r.Bool();
                        FsmBool x = key.Length == 0 ? FsmVariables.GlobalVariables.FindFsmBool(name) : j != null && j.F != null ? j.F.FsmVariables.FindFsmBool(name) : null;
                        if (x != null) x.Value = v;
                    }
                    else
                    {
                        // Liste : contenu remplace par celui de l'hote.
                        int c = r.U16();
                        var items = new List<object>(c);
                        for (int q = 0; q < c; q++)
                        {
                            int it = r.U8();
                            if (it == 0) items.Add(r.I32());
                            else if (it == 1) items.Add(r.F32());
                            else if (it == 2) items.Add(r.Str());
                            else if (it == 3) items.Add(r.Bool());
                            else if (it == 4) items.Add(Game.FindAny(r.Str()));
                            else items.Add(null);
                        }
                        if (j != null && j.F != null)
                            foreach (PlayMakerArrayListProxy pr in j.F.GetComponents<PlayMakerArrayListProxy>())
                                if ((pr.referenceName ?? "") == name && pr._arrayList != null)
                                {
                                    pr._arrayList.Clear(); foreach (object o in items) pr._arrayList.Add(o);
                                    if (Config.GetInt("Test", "JournalListes", 0) != 0)
                                    {
                                        var sb = new System.Text.StringBuilder(); foreach (object o in pr._arrayList) sb.Append(o).Append('|');
                                        Log.Info("liste recue " + key + " / " + name + " : " + Short(sb.ToString()));
                                    }
                                    break;
                                }
                    }
                }
            }
        }

        // ---------------------------------------------------------------- essais
        // Essais : comme un joueur qui actionne la commande : l'automate passe de 'prev' a 'state' par 'ev'
        // ici (actions jouees), et le message part comme en vrai.
        public static string TestSend(string part, string prev, string ev, string state)
        {
            foreach (W j in byKey.Values)
                if (j.F != null && j.Key.Contains(part))
                {
                    var w = new NetWriter(Msg.WorldFsm).U8(Session.LocalId).Str(j.Key).Str(prev).Str(ev).U8(0).Str(state);
                    WriteVars(j.F, w, true);
                    applying = true; Replay.Depth++;
                    try { Game.SetState(j.F, state); } finally { applying = false; Replay.Depth--; }
                    Session.SendAll(w, true);
                    return j.Key + " " + prev + " -" + ev + "-> " + state + " envoye";
                }
            return "rien pour " + part;
        }

        public static string TestEvent(string part, string ev)
        {
            foreach (W j in byKey.Values)
                if (j.F != null && j.Key.Contains(part))
                {
                    string before = j.F.ActiveStateName;
                    lastInput = Time.realtimeSinceStartup;   // comme si le joueur venait d'agir
                    forceNext = true;
                    j.F.SendEvent(ev);
                    forceNext = false;
                    return j.Key + " : " + before + " -" + ev + "-> " + j.F.ActiveStateName;
                }
            // Automates passes a Calls (createurs de commandes, cadrans, boite aux lettres) : l'essai 'colis' marche encore.
            return Calls.TestEvent(part, ev) ?? "rien pour " + part;
        }

        // Comme si le joueur venait d'agir et que l'automate passait a 'state' (boitier de CD : 'Bool test').
        public static string TestClick(string part, string state)
        {
            foreach (W j in byKey.Values)
                if (j.F != null && j.Key.Contains(part))
                {
                    string before = j.F.ActiveStateName;
                    lastInput = Time.realtimeSinceStartup;
                    Game.SetState(j.F, state);
                    return j.Key + " : " + before + " => " + j.F.ActiveStateName;
                }
            return "rien pour " + part;
        }

        // Comme si la logique passait d'elle-meme a 'state' (le crochet envoie comme en vrai).
        public static string TestState(string part, string state)
        {
            foreach (W j in byKey.Values)
                if (j.F != null && j.Key.Contains(part))
                {
                    string before = j.F.ActiveStateName;
                    Game.SetState(j.F, state);
                    return j.Key + " : " + before + " => " + j.F.ActiveStateName;
                }
            return "rien pour " + part;
        }

        public static string Var(string part, string name)
        {
            foreach (W j in byKey.Values)
                if (j.F != null && j.Key.Contains(part))
                {
                    NamedVariable v = j.F.FsmVariables.GetVariable(name);
                    return j.Key + " " + name + " = " + (v != null ? v.ToString() : "?") + " (etat " + j.F.ActiveStateName + ")";
                }
            FsmBool g = FsmVariables.GlobalVariables.FindFsmBool(name);
            if (g != null) return "globale " + name + " = " + g.Value;
            FsmFloat gf = FsmVariables.GlobalVariables.FindFsmFloat(name);
            return gf != null ? "globale " + name + " = " + gf.Value : "?";
        }
    }
}
