using System.Collections.Generic;
using System.Reflection;
using HutongGames.PlayMaker;
using MWCoop.Net;
using UnityEngine;

namespace MWCoop
{
    // Quetes / boulots communs (demande de JD : une quete entamee par l'un et terminee par l'autre
    // est validee pour les deux). Les boulots (JOBS : taxi, fosses septiques, bois, Jokke, usine,
    // publicites, oncle...) gardent leur progression dans des automates qui declarent leurs cles de
    // sauvegarde (variables texte UniqueTag* / UT*). Ce sont eux qu'on suit :
    //  - une action injectee au debut de chacun de leurs etats repere les changements venus d'un vrai
    //    evenement (pas FINISHED, ni chargement/sauvegarde) et les envoie avec les variables de
    //    l'automate (etape, compteurs, reels, booleens) ;
    //  - ailleurs : variables recopiees, puis meme evenement si l'automate est dans le meme etat de
    //    depart, sinon recalage direct sur l'etat d'arrivee.
    // L'argent d'un boulot rejoue n'est pas double : voir Wallet.Suppress.
    // Meme mecanisme pour les automates a sauvegarde des vehicules (cablage electrique de la
    // CORRIS, pare-brise, boutons du tableau de bord : starter, warnings, chauffage, frein a main...),
    // hors peinture (Paint), boutons deja suivis par Interactions et degats de roues (terrain).
    // Et les commandes des vehicules sans sauvegarde (cle de contact, frein a main, manivelles de
    // vitres, levier de vitesse, molettes, interrupteurs...) : n'importe quel joueur, passager compris,
    // les actionne, et c'est rejoue chez les autres -- chez le conducteur, ou tournent le moteur et la
    // physique. Hors portieres (CarDoors) ; garde-fou plus large (une manivelle tourne vite).
    // Taxi : la MACHTWAGEN est rangee sous JOBS/TAXIJOB, pas a la racine : traitee comme un vehicule pour
    // ses commandes (cle, boutons, molettes) ; ses automates a sauvegarde gardent la regle des boulots
    // (paie rejouee pas renvoyee, garde-fou serre).
    // Commandes qui n'existent qu'une fois la piece montee (boite a gants, loquet du capot, branchements du
    // cablage, interrupteurs et jauges ajoutes) : cherchees des qu'une piece est montee (SoonScan, Parts).
    // Bois et fosses septiques : voir en bas du fichier.
    public static class Jobs
    {
        static readonly HashSet<string> Ignore = new HashSet<string> { "FINISHED", "SAVEGAME", "LOAD", "EXISTS", "NOTEXISTS", "DONOTEXIST", "DOESNOTEXIST", "SAVE",
                                                                        "TERRAIN", "DEEPSNOW", "RIM", "LOOP" };
        static readonly HashSet<string> RandomActions = new HashSet<string> { "SendRandomEvent", "RandomEvent" };
        // Evenements des portieres ("Open door"/"Close door" les envoient a la lumiere de l'habitacle) : CarDoors
        // rejoue ces etats chez les autres, qui les envoient donc eux-memes -- les rejouer aussi allumait ou
        // eteignait la lumiere une 2e fois (forcee sur "State 1" apres la fermeture).
        static readonly HashSet<string> DoorEvents = new HashSet<string> { "DOOROPEN", "DOORCLOSE", "DOOR" };
        static readonly string[] InputActions = { "MousePick", "GetMouse", "GetButton", "GetKey", "GetAxis" };
        static readonly HashSet<string> ClickActions = new HashSet<string> { "GetMouseButtonDown", "GetButtonDown", "GetKeyDown" };

        // Racines suivies : JOBS (sans les automates Use des objets) et chaque vehicule (avec ses boutons), y
        // compris un vehicule conduisible range sous JOBS (taxi) -- ses automates sont alors sautes au passage
        // de JOBS (tri garde par automate : il doit etre fait comme vehicule, meme taxi encore inactif).
        // Le taxi est pris des le 1er releve meme inactif (hors service : TAXIJOB l'active et le coupe) : ses
        // commandes sont reservees tout de suite sur chaque machine et accrochees a leur activation
        // (CheckWaiting). Pris seulement actif (FindObjectsOfType ne voit pas les inactifs), WorldFsms les
        // prenait a l'activation (releve de 2 s) avant le releve d'ici (30 s), et le module des commandes du
        // taxi dependait de l'etat du taxi au chargement de chacun (cles de l'un introuvables chez l'autre).
        static readonly List<Transform> jobCars = new List<Transform>();

        static List<KeyValuePair<GameObject, bool>> RootsNow()
        {
            var list = new List<KeyValuePair<GameObject, bool>>();
            GameObject jobsRoot = Game.FindAny("JOBS");
            if (jobsRoot != null)
            {
                list.Add(new KeyValuePair<GameObject, bool>(jobsRoot, false));
                if (jobCars.Count == 0)
                    foreach (Rigidbody rb in jobsRoot.GetComponentsInChildren<Rigidbody>(true))
                        if (JobCar(rb)) jobCars.Add(rb.transform);
            }
            foreach (Transform t in jobCars)
                if (t != null) list.Add(new KeyValuePair<GameObject, bool>(t.gameObject, true));
            foreach (Rigidbody rb in Object.FindObjectsOfType<Rigidbody>())
                if (rb.transform.parent == null && rb.GetComponent("CarDynamics") != null)
                    list.Add(new KeyValuePair<GameObject, bool>(rb.gameObject, true));
            list.Sort((a, b) => string.CompareOrdinal(a.Key.name, b.Key.name));
            return list;
        }

        // Vehicule conduisible range sous JOBS (MACHTWAGEN du taxi).
        static bool JobCar(Rigidbody rb)
        {
            return rb.transform.parent != null && rb.transform.root.name == "JOBS" && rb.GetComponent("CarDynamics") != null && rb.GetComponent<AxisCarController>() != null;
        }

        static bool UnderJobCar(Transform t)
        {
            for (int i = 0; i < jobCars.Count; i++) if (jobCars[i] != null && t.IsChildOf(jobCars[i])) return true;
            return false;
        }

        class Job
        {
            public string Key; public PlayMakerFSM F; public float WindowStart, NoisySince; public int Count; public bool Noisy, Control;
            public int Kind;                                                                 // K_* : traitement particulier
            public HashSet<string> Entered = new HashSet<string>();
            public HashSet<string> RandomStates = new HashSet<string>();                     // etats qui tirent au sort
            public HashSet<string> ClickStates = new HashSet<string>();                      // etats qui attendent un clic
            public Dictionary<string, float> LocalRecent = new Dictionary<string, float>();  // transitions prises ici
        }
        const int K_PLAIN = 0, K_LOGTRIGGER = 1, K_FEEDLOG = 2;
        static readonly HashSet<string> ControlFsms = new HashSet<string> { "Use", "Knob", "Screw", "Usage", "Change", "Switch", "ChangeChannel", "ChangeTrack", "Attach",
                                                                             "Latch", "Assemble" };   // loquet du capot ; branchements du cablage

        // Commande de vehicule sans sauvegarde : automate d'interaction, pas la logique de conduite.
        static bool IsControl(PlayMakerFSM f)
        {
            if (!ControlFsms.Contains(f.FsmName) || CarDoors.Tracks(f)) return false;
            if (CarDoorsLike(f)) return false;                          // portieres, capots, hayons : CarDoors (meme pas encore vus)
            if (f.FsmName == "Screw" && Parts.IsBolt(f)) return false;   // vis des pieces : Parts
            if (DoorDriven(f)) return false;   // suit les portieres rejouees
            string n = f.gameObject.name;
            // Raccord du tuyau de la GIFU ('Attach' : se raccroche au camion d'apres la main du joueur local, boucle
            // PROCEED/LOOP tant qu'il est pres du camion) : pas une commande ; la fosse suit le tuyau par @tuyau.
            if (n.StartsWith("hose coupler")) return false;
            return !n.StartsWith("PlayerTrigger") && !n.StartsWith("DriveTrigger") && !n.StartsWith("CameraPivot");
        }

        // Ce que CarDoors prend : portieres ("Open door", "Open door 2", "Close door") et capots, hayons ("Open
        // hood", "Close hood"). Le reste a "Open door" (boite a gants, hayon du plateau, trappes animees) est
        // une commande ordinaire : avant, toute "Open door" etait laissee a CarDoors, qui ne la prenait pas.
        static bool CarDoorsLike(PlayMakerFSM f)
        {
            if (f.FsmName != "Use") return false;
            Fsm m = f.Fsm;
            return m.GetState("Open door") != null && m.GetState("Open door 2") != null && m.GetState("Close door") != null
                   || m.GetState("Open hood") != null && m.GetState("Close hood") != null;
        }

        // Automate mene seulement par les portieres : pas d'autre transition que FINISHED et leurs evenements, et
        // aucune action d'entree (souris, touches). Les transitions d'abord : elles se lisent toujours. Les
        // actions d'un automate jamais demarre (objet jamais actif, Awake pas joue) ne se chargent pas -- le
        // getter de PlayMaker leve une exception, qui coupait tout le releve (JOBS compris) : les transitions
        // seules decident alors (meme tri qu'une fois l'objet actif).
        static bool DoorDriven(PlayMakerFSM f)
        {
            bool door = false;
            foreach (FsmTransition t in f.Fsm.GlobalTransitions)
            {
                if (DoorEvents.Contains(t.EventName)) door = true;
                else if (t.EventName != "FINISHED") return false;
            }
            foreach (FsmState s in f.Fsm.States)
                foreach (FsmTransition t in s.Transitions)
                {
                    if (DoorEvents.Contains(t.EventName)) door = true;
                    else if (t.EventName != "FINISHED") return false;
                }
            if (!door) return false;
            foreach (FsmState s in f.Fsm.States)
            {
                if (!s.IsInitialized) continue;
                FsmStateAction[] acts;
                try { acts = s.Actions; } catch { continue; }
                foreach (FsmStateAction a in acts)
                {
                    if (a == null) continue;
                    string tn = a.GetType().Name;
                    foreach (string p in InputActions) if (tn.StartsWith(p)) return false;
                }
            }
            return true;
        }
        static readonly Dictionary<string, Job> jobs = new Dictionary<string, Job>();
        class Classified { public string Path; public bool Control; }
        static readonly Dictionary<PlayMakerFSM, Classified> classified = new Dictionary<PlayMakerFSM, Classified>();
        static readonly HashSet<PlayMakerFSM> hooked = new HashSet<PlayMakerFSM>();
        // Automates pris par ScanSpecials (createurs, fosses, fendeuse) : jamais repris par le releve general,
        // meme pas encore accroches (Replay.Claim rend vrai au meme module : la fendeuse attelee au tracteur
        // etait reprise comme commande du vehicule, sous une autre cle).
        static readonly HashSet<PlayMakerFSM> reserved = new HashSet<PlayMakerFSM>();
        static float nextScan = -1, loadedAt, nextWarn;
        static bool applying;

        class Hook : ModHook
        {
            public override string Module { get { return "quetes"; } }
            public Job J;
            public string State;
            public override void OnEnter()
            {
                try { if (Replay.Depth == 0) OnLocal(J, State); else J.Entered.Add(State); } catch (System.Exception e) { Replay.HookError(e); }
                Finish();
            }
        }

        public static bool Tracks(PlayMakerFSM f) { return hooked.Contains(f); }

        public static void OnLevelLoaded()
        {
            jobs.Clear(); hooked.Clear(); reserved.Clear(); classified.Clear(); jobCars.Clear(); waitingJobs.Clear(); waitingSet.Clear();
            creators.Clear(); creatorsWaiting.Clear(); tagged.Clear(); wells.Clear(); wellsWaiting.Clear();
            specialsScanned = false; logTrigger = null; ltCollider = null;
            feed = null; feedHand = null; feedMute = null; feedMuted.Clear(); feedClick = feedActivation = feedWasActive = false; feedStageLocal = true;
            bed = flatbed = null; bedHinge = null; bedTargetAt = -100; bedSent = float.NaN; bedLift = null; bedFollow = false;
            feedStep2 = false; feedStageSeen = -1; feedCaughtUp = 0; jobGen = -1; fastWaitUntil = 0;
            chopped = null; lastId = null; testStep = testLogs = 0; testBefore = false; otherSince = -1; testClick2 = false;
            loadedAt = Time.realtimeSinceStartup;
            nextScan = PlayerSync.InGame ? loadedAt + 10f : -1;
        }

        // Une piece vient d'etre montee (Parts) : ses commandes (boite a gants, interrupteurs, jauges, loquet,
        // branchements) sont cherchees tout de suite, sans attendre le releve des 30 s.
        // Les automates d'abord inactifs (gardes de cote au releve) sont alors guettes 4 fois par seconde pendant 10 s :
        // le montage active la boite a gants, les jauges ou les fils une image plus tard (Wiring/Status, Installed).
        // Meme chose quand VehicleSync trouve une voiture de plus (taxi, voiture pretee, corps recree).
        public static void SoonScan()
        {
            if (nextScan < 0 || Time.realtimeSinceStartup - loadedAt < 12f) return;
            nextScan = Mathf.Min(nextScan, Time.realtimeSinceStartup + 1.5f);
            fastWaitUntil = Time.realtimeSinceStartup + 10f;
            nextWaiting = 0f;
        }
        static int jobGen = -1;
        static float fastWaitUntil;

        public static void Update()
        {
            if (nextScan < 0) return;
            float now = Time.realtimeSinceStartup;
            if (VehicleSync.Generation != jobGen) { if (jobGen >= 0) SoonScan(); jobGen = VehicleSync.Generation; }
            SpecialUpdate(now);
            if (now < nextScan) return;
            nextScan = now + 30f;
            if (!specialsScanned) ScanSpecials();
            int added = 0;
            var seen = new Dictionary<string, int>();
            foreach (KeyValuePair<GameObject, bool> root in RootsNow())
            {
                GameObject r = root.Key;
                bool vehicle = root.Value;
                bool jobCar = vehicle && r.transform.parent != null;
                foreach (PlayMakerFSM f in r.GetComponentsInChildren<PlayMakerFSM>(true))
                {
                    if (!vehicle && UnderJobCar(f.transform)) continue;   // taxi : vu comme vehicule
                    if (reserved.Contains(f)) continue;                   // bois, fosses, fendeuse (meme attelee)
                    if (f.FsmName == "Use" && f.gameObject.name == "FeedLog") { ReserveFeed(f); continue; }   // (fendeuse deja attelee au 1er releve)
                    // Tri fait une fois par automate (le releve revient toutes les 30 s sans tout refaire).
                    Classified c;
                    if (!classified.TryGetValue(f, out c))
                    {
                        // Un automate illisible ne coupe pas le releve (les suivants, JOBS compris) : revu au prochain.
                        bool control;
                        try { control = vehicle && !Persistent(f) && IsControl(f); }
                        catch (System.Exception e)
                        {
                            if (Time.realtimeSinceStartup >= nextWarn) { nextWarn = Time.realtimeSinceStartup + 10f; Log.Warn("progression : " + f.gameObject.name + "::" + f.FsmName + " illisible (" + e.GetType().Name + "), revu au prochain releve"); }
                            continue;
                        }
                        string on = f.gameObject.name;
                        bool keep = !((f.FsmName == "Use" && !vehicle) || f.FsmName == "LOD" || f.FsmName == "Paint" || (!Persistent(f) && !control))
                                    && !Interactions.Tracks(f) && !Interactions.Wants(f)
                                    && !(on.Contains("(itemx)") || (on.Contains("(Clone)") && f.gameObject != r));   // objets : Props/Interactions
                        c = new Classified { Path = keep ? Recon.Path(f.transform) + "::" + f.FsmName : null, Control = control };
                        classified[f] = c;
                    }
                    if (c.Path == null) continue;
                    bool controlF = c.Control;
                    string path = c.Path;
                    int k;
                    seen.TryGetValue(path, out k);
                    seen[path] = k + 1;
                    string key = path + "#" + k;
                    if (hooked.Contains(f) || !Replay.Claim(f, "quetes")) continue;
                    // Vehicules : molettes, boutons ; le taxi garde la regle des boulots pour ses automates a sauvegarde.
                    var j = new Job { Key = key, F = f, Control = controlF || (vehicle && !jobCar) };
                    if (path.EndsWith("/LogTrigger::Logic")) j.Kind = K_LOGTRIGGER;
                    if (!InjectAll(j))
                    {
                        // Automate pas encore charge (objet jamais actif : boite a gants d'un tableau de bord pas
                        // monte, branchements du cablage) : accroche des que son objet s'active (SpecialUpdate).
                        if (waitingSet.Add(f)) waitingJobs.Add(j);
                        continue;
                    }
                    Hooked(j);
                    added++;
                }
            }
            if (added > 0)
            {
                int controls = 0;
                foreach (Job x in jobs.Values) if (x.Control) controls++;
                Log.Info("progression : " + added + " automates de plus suivis (boulots, cablage, tableaux de bord : " + jobs.Count + " en tout, dont " + controls + " commandes de vehicules)");
            }
        }

        static void Hooked(Job j)
        {
            hooked.Add(j.F);
            jobs[j.Key] = j;
            if (j.Kind == K_LOGTRIGGER) logTrigger = j;
            if (j.Kind == K_FEEDLOG) HookFeed(j);
        }

        // Automates gardes de cote au releve (pas encore charges) : repris a leur activation, 1 fois/s.
        static readonly List<Job> waitingJobs = new List<Job>();
        static readonly HashSet<PlayMakerFSM> waitingSet = new HashSet<PlayMakerFSM>();
        static float nextWaiting;

        static void CheckWaiting(float now)
        {
            if (now < nextWaiting || waitingJobs.Count == 0) return;
            nextWaiting = now + (now < fastWaitUntil ? 0.25f : 1f);
            int n = 0;
            for (int i = waitingJobs.Count - 1; i >= 0; i--)
            {
                Job j = waitingJobs[i];
                if (j.F == null || hooked.Contains(j.F)) { waitingJobs.RemoveAt(i); waitingSet.Remove(j.F); continue; }
                if (!j.F.gameObject.activeInHierarchy || !InjectAll(j)) continue;
                waitingJobs.RemoveAt(i); waitingSet.Remove(j.F);
                Hooked(j);
                n++;
            }
            if (n > 0) Log.Info("progression : " + n + " automates suivis a leur activation (" + jobs.Count + " en tout, " + waitingJobs.Count + " en attente)");
        }

        static bool Persistent(PlayMakerFSM f)
        {
            foreach (FsmString s in f.FsmVariables.StringVariables)
                if (s.Name.StartsWith("UniqueTag") || s.Name.StartsWith("UT")) return true;
            return false;
        }

        static bool InjectAll(Job j)
        {
            // Automate jamais demarre : rien a lire (et pas une erreur PlayMaker par etat a chaque releve).
            foreach (FsmState s in j.F.Fsm.States) if (!s.IsInitialized) return false;
            try
            {
                foreach (FsmState s in j.F.Fsm.States)
                {
                    foreach (FsmStateAction a in s.Actions)
                    {
                        if (a == null) continue;
                        string tn = a.GetType().Name;
                        if (RandomActions.Contains(tn)) j.RandomStates.Add(s.Name);
                        if (ClickActions.Contains(tn)) j.ClickStates.Add(s.Name);
                    }
                    var list = new List<FsmStateAction>(s.Actions);
                    list.Insert(0, new Hook { J = j, State = s.Name });
                    s.Actions = list.ToArray();
                }
                return true;
            }
            catch { return false; }
        }

        static void OnLocal(Job j, string state)
        {
            if (!Session.Active || Time.realtimeSinceStartup - loadedAt < 25f) return;
            FsmTransition tr = j.F.Fsm.LastTransition;
            if (tr == null || tr.ToState != state) return;
            FsmState from = j.F.Fsm.PreviousActiveState;
            // Clic rendu par FINISHED (boite a gants : "Mouse over 1" -FINISHED-> "Open door") : une vraie action.
            bool click = tr.EventName == "FINISHED" && j.Control && from != null && j.ClickStates.Contains(from.Name) && (state == "Open door" || state == "Close door");
            if (Ignore.Contains(tr.EventName) && !click) return;
            if (j.Control && DoorEvents.Contains(tr.EventName)) return;   // (lumiere de l'habitacle : la portiere rejouee la mene deja chez les autres)
            // Cablage : seul le branchement (clic -> "Sound") part ; l'approche du fil (ASSEMBLE au survol) non.
            if (j.F.FsmName == "Assemble" && state != "Sound") return;
            j.LocalRecent[(from != null ? from.Name : "") + "|" + tr.EventName + "|" + state] = Time.realtimeSinceStartup;
            // Fendeuse : la fin de la buche (State 4 -STOP-> State 1) n'est annoncee que par celui dont c'etait
            // l'etape ; chez les autres elle suit l'etape rejouee (sinon renvoyee en retard sur la buche suivante).
            if (j.Kind == K_FEEDLOG && tr.EventName == "STOP" && !feedStageLocal) return;
            // Tirage au sort : seul celui de l'hote compte (les invites s'y recalent a son message).
            if (from != null && j.RandomStates.Contains(from.Name) && !Session.IsHost) return;
            // Retour a l'attente (souris partie, fin de survol) : de la tenue de survol, pas une action.
            if (state == "Wait player" || state == "Mouse off" || state == "Mouse off 2" || state == "Wait button") return;
            // Garde-fou : un automate qui boucle (plus de 5 changements en 10 s ; 40 pour les commandes des
            // vehicules, une molette tourne vite) n'est plus envoye -- jusqu'a 30 s de calme.
            float now = Time.realtimeSinceStartup;
            if (j.Noisy && now - j.NoisySince > 30f) { j.Noisy = false; j.WindowStart = now; j.Count = 0; Log.Info("quete : " + j.Key + " de nouveau envoye"); }
            if (now - j.WindowStart > 10f) { j.WindowStart = now; j.Count = 0; }
            if (++j.Count > (j.Control ? 40 : 5) || j.Noisy)
            {
                if (!j.Noisy) Log.Warn("quete : " + j.Key + " change trop souvent, en pause 30 s");
                j.Noisy = true;
                j.NoisySince = now;
                return;
            }
            var w = new NetWriter(Msg.Job).U8(Session.LocalId).Str(j.Key).Str(from != null ? from.Name : "").Str(tr.EventName).Str(state);
            WriteVars(j.F, w);
            Log.Info("quete : " + j.Key + " " + (from != null ? from.Name : "?") + " -" + tr.EventName + "-> " + state);
            Session.SendAll(w, true);
        }

        static bool Skip(string n) { return n.StartsWith("UT") || n.StartsWith("UniqueTag"); }

        static void WriteVars(PlayMakerFSM f, NetWriter w)
        {
            FsmVariables v = f.FsmVariables;
            var ints = new List<FsmInt>(); foreach (FsmInt x in v.IntVariables) if (!Skip(x.Name)) ints.Add(x);
            var floats = new List<FsmFloat>(); foreach (FsmFloat x in v.FloatVariables) if (!Skip(x.Name)) floats.Add(x);
            var bools = new List<FsmBool>(); foreach (FsmBool x in v.BoolVariables) if (!Skip(x.Name)) bools.Add(x);
            w.U8(ints.Count); foreach (FsmInt x in ints) w.Str(x.Name).I32(x.Value);
            w.U8(floats.Count); foreach (FsmFloat x in floats) w.Str(x.Name).F32(x.Value);
            w.U8(bools.Count); foreach (FsmBool x in bools) w.Str(x.Name).Bool(x.Value);
        }

        public static void OnMessage(Peer from, NetReader r)
        {
            int who = r.U8();
            if (Session.IsHost) who = from.Id;
            string key = r.Str();
            if (key.Length > 0 && key[0] == '@') { OnSpecial(who, key, r); return; }   // bois, fosses (voir plus bas)
            string prev = r.Str(), ev = r.Str(), state = r.Str();
            var ints = new List<KeyValuePair<string, int>>();
            var floats = new List<KeyValuePair<string, float>>();
            var bools = new List<KeyValuePair<string, bool>>();
            for (int i = 0, n = r.U8(); i < n; i++) ints.Add(new KeyValuePair<string, int>(r.Str(), r.I32()));
            for (int i = 0, n = r.U8(); i < n; i++) floats.Add(new KeyValuePair<string, float>(r.Str(), r.F32()));
            for (int i = 0, n = r.U8(); i < n; i++) bools.Add(new KeyValuePair<string, bool>(r.Str(), r.Bool()));
            if (Session.IsHost)
            {
                var w = new NetWriter(Msg.Job).U8(who).Str(key).Str(prev).Str(ev).Str(state);
                w.U8(ints.Count); foreach (var x in ints) w.Str(x.Key).I32(x.Value);
                w.U8(floats.Count); foreach (var x in floats) w.Str(x.Key).F32(x.Value);
                w.U8(bools.Count); foreach (var x in bools) w.Str(x.Key).Bool(x.Value);
                Session.Broadcast(w, true, who);
            }
            Job j;
            if (!jobs.TryGetValue(key, out j) || j.F == null)
            {
                if (Time.realtimeSinceStartup >= nextWarn) { nextWarn = Time.realtimeSinceStartup + 10f; Log.Warn("quete " + key + " introuvable ici"); }
                return;
            }
            // Fendeuse arretee ici (mise en marche pas recue : arrivee en cours de buche) : seule la fin de la
            // buche est reprise (rend le declencheur), une etape rejouee sur l'automate inactif ne mene a rien.
            if (j.Kind == K_FEEDLOG && !j.F.gameObject.activeInHierarchy && state != "State 1")
            {
                Log.Info("quete de #" + who + " : " + key + " -" + ev + "-> " + state + " ignore (fendeuse arretee ici)");
                return;
            }
            // Deja fait ici a l'instant (la meme logique a tourne chez les deux, ex. le jour de paie) : pas une 2e fois.
            // Sauf la fendeuse : deux clics de suite (un par joueur) sont deux etapes.
            float done;
            if (j.Kind != K_FEEDLOG && j.LocalRecent.TryGetValue(prev + "|" + ev + "|" + state, out done) && Time.realtimeSinceStartup - done < 10f)
            {
                Log.Info("quete de #" + who + " : " + key + " -" + ev + "-> deja fait ici");
                return;
            }
            FsmVariables v = j.F.FsmVariables;
            foreach (var x in ints) { FsmInt t = v.FindFsmInt(x.Key); if (t != null) t.Value = x.Value; }
            foreach (var x in floats) { FsmFloat t = v.FindFsmFloat(x.Key); if (t != null) t.Value = x.Value; }
            foreach (var x in bools) { FsmBool t = v.FindFsmBool(x.Key); if (t != null) t.Value = x.Value; }
            // Plateau a bois : la buche comptee chez l'autre est detruite ici par son propre message (@parti) ;
            // l'objet que "Destroy Wood" detruirait ici (variable Log, d'un ancien passage) n'est pas le bon.
            if (j.Kind == K_LOGTRIGGER) { FsmGameObject lg = v.FindFsmGameObject("Log"); if (lg != null) lg.Value = null; }
            if (!j.Control) Wallet.Suppress(8f);   // la paie que le boulot rejoue ici n'est pas renvoyee aux autres
            applying = true; Replay.Depth++;
            try
            {
                // Meme etat de depart : meme evenement (memes actions). Sinon, ou si l'automate n'a
                // pas suivi (condition locale differente), recalage direct sur l'etat d'arrivee.
                j.Entered.Clear();
                if (j.F.ActiveStateName == prev) j.F.SendEvent(ev);
                if (!j.Entered.Contains(state) && j.F.ActiveStateName != state && j.F.Fsm.GetState(state) != null) Game.SetState(j.F, state);
            }
            finally { applying = false; Replay.Depth--; }
            Log.Info("quete de #" + who + " : " + key + " -> " + j.F.ActiveStateName + " (voulu " + state + ")");
        }

        // Essais : comme un joueur qui actionne la commande : l'automate passe de 'prev' a 'state' par
        // 'ev' ici (actions jouees), et le message part comme en vrai.
        public static string TestSend(string part, string prev, string ev, string state)
        {
            foreach (Job j in jobs.Values)
                if (j.F != null && j.Key.Contains(part))
                {
                    var w = new NetWriter(Msg.Job).U8(Session.LocalId).Str(j.Key).Str(prev).Str(ev).Str(state);
                    WriteVars(j.F, w);
                    applying = true; Replay.Depth++;
                    try { Game.SetState(j.F, state); } finally { applying = false; Replay.Depth--; }
                    Session.SendAll(w, true);
                    return j.Key + " " + prev + " -" + ev + "-> " + state + " envoye";
                }
            return "rien pour " + part;
        }

        public static string Var(string part, string name)
        {
            foreach (Job j in jobs.Values)
                if (j.F != null && j.Key.Contains(part))
                {
                    FsmFloat f = j.F.FsmVariables.FindFsmFloat(name);
                    return j.Key + " " + name + " = " + (f != null ? f.Value.ToString("F1") : "?") + " (etat " + j.F.ActiveStateName + ")";
                }
            return "?";
        }

        // Essais : envoie l'evenement 'ev' a l'automate de boulot dont la cle contient 'part'.
        public static string TestEvent(string part, string ev)
        {
            foreach (Job j in jobs.Values)
                if (j.F != null && j.Key.Contains(part))
                {
                    string before = j.F.ActiveStateName;
                    // L'etat courant n'attend pas cet evenement : on passe d'abord par un etat qui l'attend
                    // (une molette n'ecoute qu'en survol, "Get scroll").
                    bool ok = false;
                    FsmState cur = j.F.Fsm.GetState(before);
                    if (cur != null) foreach (FsmTransition t in cur.Transitions) if (t.EventName == ev) ok = true;
                    if (!ok)
                        foreach (FsmState st in j.F.Fsm.States)
                            foreach (FsmTransition t in st.Transitions)
                                if (!ok && t.EventName == ev) { Game.SetState(j.F, st.Name); ok = true; }
                    before = j.F.ActiveStateName;
                    j.F.SendEvent(ev);
                    return j.Key + " : " + before + " -" + ev + "-> " + j.F.ActiveStateName;
                }
            return "aucun boulot " + part + " (" + jobs.Count + " suivis)";
        }

        public static string StateOf(string part)
        {
            foreach (Job j in jobs.Values)
                if (j.F != null && j.Key.Contains(part)) return j.Key + " = " + j.F.ActiveStateName;
            return "?";
        }

        // ================================================================ bois de chauffage
        // Buches creees par le jeu (CreateObject, sans ID) : billot (Logwall "Create log" : buche en deux moities
        // tenues par une attache), tas de grumes (LogLongPile : grume pour la fendeuse), fendeuse (Cutter/Conveyer
        // "State 2" sur SPAWN : buche fendue jetee sur le tapis). Rejoue tel quel chez chacun, chacun avait sa
        // buche, sans cle commune : Props ne pouvait pas les apparier (doublons, buches fantomes).
        // Desormais la creation n'a lieu qu'une fois, chez celui qui l'a provoquee (clic, fendeuse qui tourne
        // chez lui) ; il donne a chaque corps cree un ID (automate "MWCoopId", variable ID : Props le suit comme
        // un article) et l'annonce (@cree : createur, ID, pose). Chez les autres la meme creation est rejouee
        // (actions qui touchent au joueur coupees) ou, createur inactif ici (cabane loin), le modele est copie ;
        // puis l'objet prend la pose et l'ID recus. Personne d'autre ne rejoue ces createurs (Jobs les prend
        // avant WorldFsms).
        //  - fendeuse (Cutter/FeedLog 'Use') : chaque etape (clic "Wait button" -USE-> "State 2", avance de la
        //    grume, "State 4" : morceau coupe, deux buches du reservoir (ConveyerPool) posees sur le tapis, qui
        //    y finissent en SPAWN -> Conveyer) est rejouee partout (meme grume, meme etape chez tous), mais ses
        //    buches ne sortent que chez celui dont c'est l'etape : son clic, ou la 1re etape (automatique a la
        //    mise en marche : Reset -> State 2 ... -> State 4) chez celui qui a pose la grume (Triggers 'Logic',
        //    LogInHand : la grume de SA main, que les declencheurs ne voient que chez lui). Ailleurs les deux
        //    SetParent (et le succes Steam) de "State 4" sont coupes le temps de l'etape (FeedStep) ; les
        //    buches arrivent par @cree. Avant, la mise en marche rejouee (Triggers par WorldFsms) faisait sortir
        //    la 1re etape chez chacun, annoncee par chacun : N fois les buches.
        //  - fendre (attache de la buche cassee par la hache : "Check joint" -> "State 2", renomme les moities
        //    firewood(Clone), les detache, PART) : annonce (@fend), rejouee chez les autres sur leur copie
        //    (attache retiree). Les 6 de la variable Money ne sont PAS une paie : ils sont retires du stress du
        //    joueur (FloatSubtract PlayerStress), avec +1 a ses statistiques (LogsChopped) : remis comme avant chez
        //    ceux qui rejouent ;
        //  - disparition (buche jetee dans le plateau, grume avalee par la fendeuse, bois brule) : l'objet marque
        //    detruit ici l'est chez tous (@parti) ;
        //  - plateau (FLATBED/Bed/LogTrigger 'Logic' : compte les buches, les detruit, empile, fait payer) : seul
        //    celui qui fait autorite sur le plateau (VehicleSync.Authority : l'hote tant que personne ne le tire
        //    chez lui) compte ; ailleurs son declencheur est coupe, les comptes viennent de son message (Jobs) ;
        //  - benne du plateau (corps sur charniere) : angle de l'autorite, suivi par les autres (@benne) ;
        //  - hayon du plateau, boite a gants : commandes ordinaires (CarDoorsLike).
        class Creator
        {
            public string Key; public PlayMakerFSM F; public string State;
            public GameObject Prefab;                 // modele de CreateObject (copie quand ce createur est inactif ici)
            public FsmStateAction[] Personal;         // actions de l'etat qui touchent au joueur : coupees au rejeu
            public bool Ready;
        }
        static readonly Dictionary<string, Creator> creators = new Dictionary<string, Creator>();
        static readonly List<Creator> creatorsWaiting = new List<Creator>();
        static readonly Dictionary<string, GameObject> tagged = new Dictionary<string, GameObject>();   // ID -> corps cree
        static readonly List<string> deadIds = new List<string>();
        static bool specialsScanned;
        static float nextSpecialWait, nextGone, nextAuthority;
        static int tagCounter;
        static readonly string salt = Salt();   // (deux lettres tirees au lancement : un joueur revenu sous le meme numero ne reprend pas les memes ID)
        static string lastId;

        static string Salt()
        {
            var r = new System.Random();
            return new string(new[] { (char)('a' + r.Next(26)), (char)('a' + r.Next(26)) });
        }
        static FsmInt chopped;

        class CreateHook : ModHook
        {
            public override string Module { get { return "quetes (bois)"; } }
            public Creator C;
            public override void OnEnter()
            {
                try { if (Replay.Depth == 0 && !applying) Created(C); } catch (System.Exception e) { Replay.HookError(e); }
                Finish();
            }
        }

        class SplitHook : ModHook
        {
            public override string Module { get { return "quetes (bois)"; } }
            public string Id;
            public override void OnEnter()
            {
                try { if (Replay.Depth == 0 && !applying) Split(Id); } catch (System.Exception e) { Replay.HookError(e); }
                Finish();
            }
        }

        static readonly string[] SpecialRoots = { "YARD", "COTTAGE", "CABIN", "JOBS", "Cutter" };

        // Une fois par chargement (objets du decor) : createurs de buches, fendeuse, declencheurs des fosses.
        static void ScanSpecials()
        {
            specialsScanned = true;
            int claimed = 0;
            foreach (string rn in SpecialRoots)
            {
                GameObject root = Game.FindAny(rn);
                if (root == null) continue;
                foreach (PlayMakerFSM f in root.GetComponentsInChildren<PlayMakerFSM>(true))
                {
                    if (f.hideFlags != HideFlags.None) continue;
                    string on = f.gameObject.name;
                    if (f.FsmName == "Use" && on == "FeedLog") { if (ReserveFeed(f)) claimed++; continue; }   // fendeuse (voir FeedStep)
                    bool creator = f.FsmName == "Use" && f.Fsm.GetState("Create log") != null
                                   || f.FsmName == "Logic" && on == "Conveyer" && f.Fsm.GetState("State 2") != null;
                    bool well = f.FsmName == "Trigger" && on == "ShitLevelTrigger" && f.Fsm.GetState("Hose in") != null && f.Fsm.GetState("Wait 2") != null;
                    if (!creator && !well) continue;
                    if (!Replay.Claim(f, "quetes")) { Log.Warn("bois/fosse : " + Recon.Path(f.transform) + " deja a " + Replay.Owner(f)); continue; }
                    reserved.Add(f);
                    string key = Parts.RankPath(f.transform) + "::" + f.FsmName;
                    if (creator)
                    {
                        var c = new Creator { Key = key, F = f, State = f.FsmName == "Use" ? "Create log" : "State 2" };
                        creators[key] = c;
                        if (!HookCreator(c)) creatorsWaiting.Add(c);
                    }
                    else
                    {
                        var w = new Well { Key = key, F = f };
                        wells[key] = w;
                        if (!HookWell(w)) wellsWaiting.Add(w);
                    }
                }
            }
            Log.Info("bois et fosses : " + creators.Count + " createurs de buches (" + creatorsWaiting.Count + " pas encore charges), "
                     + wells.Count + " fosses (" + wellsWaiting.Count + " en attente), " + claimed + " fendeuse(s) (buches a celui dont c'est l'etape)");
        }

        static bool HookCreator(Creator c)
        {
            FsmState s = c.F.Fsm.GetState(c.State);
            if (s == null || !s.IsInitialized) return false;
            try
            {
                FsmStateAction[] acts = s.Actions;
                var personal = new List<FsmStateAction>();
                foreach (FsmStateAction a in acts)
                {
                    if (a == null) continue;
                    var co = a as HutongGames.PlayMaker.Actions.CreateObject;
                    if (co != null && co.gameObject != null) c.Prefab = co.gameObject.Value;
                    if (TouchesPlayer(a)) personal.Add(a);
                }
                c.Personal = personal.ToArray();
                // En fin d'etat : l'objet est cree, pose et tourne (variable Log).
                var list = new List<FsmStateAction>(acts);
                list.Add(new CreateHook { C = c });
                s.Actions = list.ToArray();
            }
            catch { return false; }
            c.Ready = true;
            hooked.Add(c.F);
            return true;
        }

        // L'action vise-t-elle le joueur ou son interface (evenement STOPPUSH a la main du joueur...) ?
        static bool TouchesPlayer(FsmStateAction a)
        {
            foreach (FieldInfo fi in a.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                object v = fi.GetValue(a);
                GameObject go = null;
                if (v is FsmGameObject) go = ((FsmGameObject)v).Value;
                else if (v is FsmOwnerDefault) go = OwnerGo((FsmOwnerDefault)v);
                else if (v is FsmEventTarget) go = OwnerGo(((FsmEventTarget)v).gameObject);
                if (go == null) continue;
                string rn = go.transform.root.name;
                if (rn == "PLAYER" || rn == "GUI") return true;
            }
            return false;
        }

        static GameObject OwnerGo(FsmOwnerDefault od)
        {
            if (od == null || od.OwnerOption == OwnerDefaultOption.UseOwner || od.GameObject == null) return null;
            return od.GameObject.Value;
        }

        static string NewId() { return "b" + Session.LocalId + salt + "-" + (++tagCounter); }

        // Chaque corps de l'objet cree recoit son ID (une buche du billot : deux moities, a et b).
        static void Tag(GameObject made, string id)
        {
            Rigidbody[] bodies = made.GetComponentsInChildren<Rigidbody>(true);
            for (int i = 0; i < bodies.Length; i++)
            {
                GameObject go = bodies[i].gameObject;
                string bid = bodies.Length == 1 ? id : id + (char)('a' + i);
                PlayMakerFSM idf = go.AddComponent<PlayMakerFSM>();
                idf.FsmName = "MWCoopId";
                idf.FsmVariables.StringVariables = new[] { new FsmString("ID") { Value = bid } };
                tagged[bid] = go;
                PlayMakerFSM cj = Game.FsmOn(go, "Check joint");
                if (cj != null) HookSplit(cj, id);
                Props.Track(go);
            }
            lastId = id;
        }

        static void HookSplit(PlayMakerFSM cj, string id)
        {
            FsmState s = cj.Fsm.GetState("State 2");
            if (s == null || !s.IsInitialized || !Replay.Claim(cj, "quetes")) return;
            try
            {
                var list = new List<FsmStateAction>(s.Actions);
                list.Insert(0, new SplitHook { Id = id });
                s.Actions = list.ToArray();
                hooked.Add(cj);
            }
            catch (System.Exception e) { Log.Warn("bois : fente de " + id + " pas suivie (" + e.GetType().Name + ")"); }
        }

        static void Created(Creator c)
        {
            if (!Session.Active) return;
            FsmGameObject v = c.F.FsmVariables.FindFsmGameObject("Log");
            GameObject made = v != null ? v.Value : null;
            if (made == null) { Log.Warn("bois : " + c.Key + " n'a rien cree"); return; }
            if (Props.ItemId(made).Length > 0) return;   // deja marque
            string id = NewId();
            Tag(made, id);
            Session.SendAll(new NetWriter(Msg.Job).U8(Session.LocalId).Str("@cree").Str(c.Key).Str(id)
                .Vec(made.transform.position).Quat(made.transform.rotation), true);
            Log.Info("bois : " + made.name + " " + id + " cree ici (" + c.Key + ")");
        }

        static void OnCreated(int who, string key, string id, Vector3 pos, Quaternion rot)
        {
            if (tagged.ContainsKey(id) || tagged.ContainsKey(id + "a")) return;
            Creator c;
            if (!creators.TryGetValue(key, out c) || c.F == null) { Log.Warn("bois : createur " + key + " introuvable ici, " + id + " perdu"); return; }
            GameObject made = null;
            if (c.Ready && c.F.gameObject.activeInHierarchy)
            {
                // La meme creation que chez l'autre (son, rattachement), sans ce qui touche au joueur d'ici.
                FsmGameObject v = c.F.FsmVariables.FindFsmGameObject("Log");
                if (v != null) v.Value = null;
                var muted = new List<FsmStateAction>();
                foreach (FsmStateAction a in c.Personal) if (a.Enabled) { a.Enabled = false; muted.Add(a); }
                applying = true; Replay.Depth++;
                try { Game.SetState(c.F, c.State); }
                finally
                {
                    applying = false; Replay.Depth--;
                    foreach (FsmStateAction a in muted) a.Enabled = true;
                }
                made = v != null ? v.Value : null;
            }
            if (made == null)
            {
                // Createur pas charge ici (billot de la cabane, loin) : copie du meme modele.
                GameObject prefab = c.Prefab;
                if (prefab == null)
                    foreach (Creator o in creators.Values)
                        if (o.Prefab != null && o.F != null && o.F.gameObject.name == c.F.gameObject.name) { prefab = o.Prefab; break; }
                if (prefab == null) { Log.Warn("bois : pas de modele pour " + key + ", " + id + " perdu"); return; }
                made = (GameObject)Object.Instantiate(prefab, pos, rot);
            }
            made.transform.position = pos;
            made.transform.rotation = rot;
            Tag(made, id);
            Log.Info("bois : " + made.name + " " + id + " cree par #" + who + " (" + key + ")");
        }

        static void Split(string id)
        {
            if (!Session.Active) return;
            Session.SendAll(new NetWriter(Msg.Job).U8(Session.LocalId).Str("@fend").Str(id), true);
            Log.Info("bois : buche " + id + " fendue ici");
        }

        static FsmInt LogsChopped()
        {
            if (chopped != null) return chopped;
            GameObject s = Game.FindAny("Systems/Statistics");
            PlayMakerFSM d = s != null ? Game.FsmOn(s, "Data") : null;
            chopped = d != null ? d.FsmVariables.FindFsmInt("LogsChopped") : null;
            return chopped;
        }

        static void OnSplit(int who, string id)
        {
            GameObject b;
            if (!tagged.TryGetValue(id + "b", out b) || b == null) { Log.Warn("bois : buche " + id + " a fendre introuvable ici"); return; }
            PlayMakerFSM cj = Game.FsmOn(b, "Check joint");
            if (cj == null || cj.ActiveStateName == "State 2") return;   // deja fendue ici
            FsmFloat stress = FsmVariables.GlobalVariables.FindFsmFloat("PlayerStress");
            FsmInt count = LogsChopped();
            float s0 = stress != null ? stress.Value : 0f;
            int c0 = count != null ? count.Value : 0;
            // (ce qui viserait le joueur d'ici -- camera, interface -- est coupe le temps du rejeu)
            var muted = new List<FsmStateAction>();
            FsmState s2 = cj.Fsm.GetState("State 2");
            if (s2 != null && s2.IsInitialized)
                foreach (FsmStateAction a in s2.Actions)
                    if (a != null && a.Enabled && !(a is ModHook) && TouchesPlayer(a)) { a.Enabled = false; muted.Add(a); }
            applying = true; Replay.Depth++;
            try { Game.SetState(cj, "State 2"); }
            finally
            {
                applying = false; Replay.Depth--;
                foreach (FsmStateAction a in muted) a.Enabled = true;
            }
            // La detente du coup de hache et la statistique sont a celui qui a fendu.
            if (stress != null) stress.Value = s0;
            if (count != null) count.Value = c0;
            FixedJoint fj = b.GetComponent<FixedJoint>();
            if (fj != null) Object.Destroy(fj);
            Log.Info("bois : buche " + id + " fendue par #" + who);
        }

        // Objets marques detruits ici (plateau, fendeuse, feu, rejeu de l'autre) : annonces par lots.
        static void CheckGone(float now)
        {
            if (now < nextGone || tagged.Count == 0) return;
            nextGone = now + 0.5f;
            deadIds.Clear();
            foreach (KeyValuePair<string, GameObject> kv in tagged) if (kv.Value == null) deadIds.Add(kv.Key);
            if (deadIds.Count == 0) return;
            foreach (string id in deadIds) tagged.Remove(id);
            if (!Session.Active || Session.RemoteCount == 0) return;
            for (int start = 0; start < deadIds.Count; start += 30)
            {
                int n = Mathf.Min(30, deadIds.Count - start);
                var w = new NetWriter(Msg.Job).U8(Session.LocalId).Str("@parti").U8(n);
                for (int i = 0; i < n; i++) w.Str(deadIds[start + i]);
                Session.SendAll(w, true);
            }
            Log.Info("bois : " + deadIds.Count + " objets marques disparus ici (" + deadIds[0] + (deadIds.Count > 1 ? "..." : "") + ")");
        }

        static void OnGone(string id)
        {
            GameObject go;
            if (!tagged.TryGetValue(id, out go)) return;
            tagged.Remove(id);   // (pas renvoye)
            if (go != null) Object.Destroy(go);
        }

        // ---------------------------------------------------------------- fendeuse (Cutter/FeedLog)
        static Job feed;
        static FsmGameObject feedHand;                  // Cutter/Triggers 'Logic' LogInHand (grume tenue par le joueur d'ici)
        static FsmStateAction[] feedMute;               // "State 4" : SetParent des buches du reservoir, succes Steam
        static readonly List<FsmStateAction> feedMuted = new List<FsmStateAction>();
        static bool feedClick, feedActivation, feedWasActive, feedStageLocal = true, feedStep2;

        class FeedHook : ModHook
        {
            public override string Module { get { return "quetes"; } }   // (meme module que le suivi de l'automate)
            public int Step;   // 0 : entree de "State 2" ; 1 : debut de "State 4" ; 2 : apres ses SetParent
            public override void OnEnter()
            {
                try { FeedStep(Step); } catch (System.Exception e) { Replay.HookError(e); }
                Finish();
            }
        }

        static bool ReserveFeed(PlayMakerFSM f)
        {
            if (!Replay.Claim(f, "quetes")) { Log.Warn("bois : fendeuse " + Recon.Path(f.transform) + " deja a " + Replay.Owner(f)); return false; }
            reserved.Add(f);
            if (feed != null) { Log.Warn("bois : 2e fendeuse " + Recon.Path(f.transform) + " pas suivie"); return true; }
            // Cle fixe : attelee au tracteur, la fendeuse change de parent (KEKMET/HitchPivot/AttachPoint).
            feed = new Job { Key = "Cutter/FeedLog::Use#0", F = f, Control = true, Kind = K_FEEDLOG };
            feedWasActive = f.gameObject.activeInHierarchy;
            if (feedWasActive && InjectAll(feed)) Hooked(feed);
            else if (waitingSet.Add(f)) waitingJobs.Add(feed);   // (inactive au chargement : accrochee a sa mise en marche)
            return true;
        }

        // Apres InjectAll (Hooked) : crochets de la fendeuse dans "State 2" et "State 4".
        static void HookFeed(Job j)
        {
            try
            {
                FsmState s2 = j.F.Fsm.GetState("State 2"), s4 = j.F.Fsm.GetState("State 4");
                if (s2 != null)
                {
                    var l2 = new List<FsmStateAction>(s2.Actions);
                    l2.Insert(0, new FeedHook { Step = 0 });
                    s2.Actions = l2.ToArray();
                }
                if (s4 == null) { Log.Warn("bois : fendeuse sans \"State 4\", buches pas gardees"); return; }
                var l4 = new List<FsmStateAction>(s4.Actions);
                var mute = new List<FsmStateAction>();
                int last = -1;
                for (int i = 0; i < l4.Count; i++)
                {
                    FsmStateAction a = l4[i];
                    if (a == null || a is ModHook) continue;
                    string tn = a.GetType().Name;
                    if (tn == "SetParent") { mute.Add(a); last = i; }
                    else if (tn == "SendEventByName") mute.Add(a);
                }
                // Retabli juste apres les SetParent : avant IntCompare, dont l'evenement STOP arrete la liste.
                if (last >= 0) l4.Insert(last + 1, new FeedHook { Step = 2 });
                feedStep2 = last >= 0;
                l4.Insert(0, new FeedHook { Step = 1 });
                s4.Actions = l4.ToArray();
                feedMute = mute.ToArray();
                TestMuteAchievement();
                Log.Info("bois : fendeuse suivie (" + mute.Count + " actions coupees quand l'etape est a un autre joueur)");
            }
            catch (System.Exception e) { Log.Warn("bois : crochets de la fendeuse pas poses (" + e.GetType().Name + ")"); }
        }

        static void FeedStep(int step)
        {
            if (feed == null || feed.F == null) return;
            Fsm m = feed.F.Fsm;
            if (step == 0)
            {
                // "State 2" : clic d'ici ("Wait button" -USE->), ou suite de la mise en marche (Reset), ou rejeu.
                FsmState prev = m.PreviousActiveState;
                FsmTransition tr = m.LastTransition;
                feedClick = Replay.Depth == 0 && prev != null && prev.Name == "Wait button" && tr != null && tr.EventName == "USE";
                if (Replay.Depth > 0 || prev == null || prev.Name != "Reset") feedActivation = false;   // 1re etape passee ou remplacee
                return;
            }
            FeedRestore();
            FsmInt stage = feed.F.FsmVariables.FindFsmInt("Stage");
            if (step == 2)
            {
                // Apres IntAdd : l'etape faite ici est annoncee (les autres s'y recalent, voir OnFeedStage).
                if (feedStageLocal && stage != null) SendFeedStage(stage.Value);
                return;
            }
            // Debut de "State 4" (avant IntAdd) : a qui sont les deux buches de cette etape ?
            bool first = stage != null && stage.Value == 0;
            feedStageLocal = !Session.Active || Session.RemoteCount == 0 || feedClick || (first && feedActivation);
            feedClick = false;
            if (first) feedActivation = false;
            if (feedStageLocal && !feedStep2 && stage != null) SendFeedStage(stage.Value + 1);   // (pas de crochet apres IntAdd)
            if (feedStageLocal || feedMute == null) return;
            foreach (FsmStateAction a in feedMute) if (a.Enabled) { a.Enabled = false; feedMuted.Add(a); }
            Log.Info("bois : etape " + (stage != null ? (stage.Value + 1).ToString() : "?") + " de la fendeuse a un autre joueur, ses buches arrivent par @cree");
        }

        // Etape de la fendeuse : celui dont c'etait l'etape annonce le Stage atteint (@etape). Chez les autres la meme
        // etape est rejouee (clic recu) mais attend leur propre lame ("Wait blade" : Blocked de Conveyer 'Blade') ;
        // une lame en retard laissait l'etape en plan, et le clic suivant (recalage direct sur "State 2") la sautait :
        // Stage 1 chez l'invite quand l'hote etait a 2. A l'annonce, une fendeuse d'ici en retard d'une etape fait
        // "State 4" tout de suite (rejeu : sans ses buches, qui arrivent par @cree) ; plus en retard, Stage est
        // d'abord mis juste avant.
        static void SendFeedStage(int s)
        {
            if (!Session.Active || Session.RemoteCount == 0) return;
            Session.SendAll(new NetWriter(Msg.Job).U8(Session.LocalId).Str("@etape").U8(s), true);
        }

        static void OnFeedStage(int who, int s)
        {
            if (feed == null || feed.F == null) return;
            FsmInt stage = feed.F.FsmVariables.FindFsmInt("Stage");
            if (stage == null) return;
            if (!feed.F.gameObject.activeInHierarchy || !hooked.Contains(feed.F))
            {
                Log.Info("bois : etape " + s + " de la fendeuse chez #" + who + ", fendeuse arretee ici (Stage " + stage.Value + ")");
                return;
            }
            int local = stage.Value;
            if (local >= s) { feedStageSeen = s; return; }   // deja faite ici (lame d'ici a l'heure)
            if (local < s - 1)
            {
                stage.Value = s - 1;
                FsmString sn = feed.F.FsmVariables.FindFsmString("StageName");
                if (sn != null) sn.Value = (s - 1).ToString();
            }
            string was = feed.F.ActiveStateName;
            feedClick = false; feedActivation = false;   // (etape d'un autre : ses buches sont coupees ici)
            applying = true; Replay.Depth++;
            try { Game.SetState(feed.F, "State 4"); }
            finally { applying = false; Replay.Depth--; }
            feedStageSeen = s;
            feedCaughtUp++;
            Log.Info("bois : etape " + s + " de la fendeuse recue de #" + who + ", rattrapee ici (Stage " + local + " -> " + stage.Value + ", etait en " + was + ", maintenant " + feed.F.ActiveStateName + ")");
        }
        static int feedStageSeen = -1, feedCaughtUp;

        static void FeedRestore()
        {
            for (int i = 0; i < feedMuted.Count; i++) feedMuted[i].Enabled = true;
            feedMuted.Clear();
        }

        static FsmGameObject FeedHand()
        {
            if (feedHand != null || feed == null || feed.F == null) return feedHand;
            Transform cutter = feed.F.transform.parent;
            Transform tr = cutter != null ? cutter.Find("Triggers") : null;
            PlayMakerFSM lf = tr != null ? Game.FsmOn(tr.gameObject, "Logic") : null;
            feedHand = lf != null ? lf.FsmVariables.FindFsmGameObject("LogInHand") : null;
            return feedHand;
        }

        // Mise en marche (Triggers "State 2" : ici au clic, ailleurs par WorldFsms) : vue a l'image d'apres au
        // plus ; la 1re etape (automatique) part au moins 0,4 s plus tard (Move), crochets poses avant.
        static void FeedActivated()
        {
            if (!hooked.Contains(feed.F) && InjectAll(feed))
            {
                waitingJobs.Remove(feed); waitingSet.Remove(feed.F);
                Hooked(feed);
            }
            // LogInHand : la grume de la main du joueur d'ici (Trigger1/2 ne la cherchent que dans SA main),
            // detruite par Triggers "State 2" (la reference reste) ; nulle ou la mise en marche est rejouee.
            // Remise a zero : une vieille grume ne compte pas pour la mise en marche suivante.
            FsmGameObject hand = FeedHand();
            if (hand == null)
            {
                feedActivation = true;
                Log.Warn("bois : grume de la fendeuse introuvable (Triggers 'Logic' LogInHand), 1re etape gardee ici");
                return;
            }
            feedActivation = !ReferenceEquals(hand.Value, null);
            hand.Value = null;
            Log.Info("bois : fendeuse mise en marche " + (feedActivation ? "ici (grume du joueur d'ici)" : "par un autre joueur (1re etape sans buches ici)"));
        }

        // ---------------------------------------------------------------- plateau (FLATBED)
        static Job logTrigger;
        static Collider ltCollider;
        static Rigidbody bed, flatbed;
        static HingeJoint bedHinge;
        static Quaternion bedRest;
        static float bedTarget, bedTargetAt = -100, bedSent = float.NaN, bedSentAt, nextBedSend, bedLastFixed = -1;
        // La benne monte par sa charniere : automate 'Lift' de FLATBED/Bed (evenements globaux HYD_UP -> "UP",
        // HYD_DOWN -> "UP 2", HYD_OFF -> "UP 3", tenue "UP 4" : SetHingeJointProperties = moteur / butees de la
        // charniere). Chez celui qui suit, ce moteur et ces butees tenaient la benne contre la correction : le temps
        // du suivi, l'automate est arrete et la charniere libre (moteur, ressort, butees coupes), puis rendus.
        static PlayMakerFSM bedLift;
        static bool bedFollow, bedLiftWas, bedMotorWas, bedLimitsWas, bedSpringWas;

        static void FindBed()
        {
            if (bed != null || logTrigger == null || logTrigger.F == null) return;
            Transform p = logTrigger.F.transform.parent;
            Rigidbody b = p != null ? p.GetComponent<Rigidbody>() : null;
            if (b == null) return;
            bed = b;
            bedHinge = b.GetComponent<HingeJoint>();
            flatbed = b.transform.root.GetComponent<Rigidbody>();
            bedRest = b.transform.localRotation;   // (benne baissee au chargement : la sauvegarde ne garde que le plateau)
            ltCollider = logTrigger.F.GetComponent<Collider>();
            bedLift = Game.FsmOn(b.gameObject, "Lift");
            if (bedLift != null && !Replay.Claim(bedLift, "quetes")) { Log.Warn("bois : 'Lift' de la benne deja a " + Replay.Owner(bedLift)); bedLift = null; }
            if (bedHinge != null) Log.Info("bois : benne trouvee (" + HingeState() + ")");
        }

        static string HingeState()
        {
            if (bedHinge == null) return "pas de charniere";
            JointMotor m = bedHinge.motor; JointLimits l = bedHinge.limits; JointSpring s = bedHinge.spring;
            return "angle " + (bed != null ? BedAngle().ToString("F1") : "?") + ", moteur " + (bedHinge.useMotor ? "v " + m.targetVelocity.ToString("F1") + " f " + m.force.ToString("F0") : "non")
                   + ", butees " + (bedHinge.useLimits ? "[" + l.min.ToString("F1") + " ; " + l.max.ToString("F1") + "]" : "non")
                   + ", ressort " + (bedHinge.useSpring ? s.spring.ToString("F0") + " vers " + s.targetPosition.ToString("F1") : "non")
                   + ", Lift " + (bedLift != null ? (bedLift.enabled ? "" : "arrete, ") + bedLift.ActiveStateName : "?");
        }

        static void BedFollow(bool on)
        {
            if (on == bedFollow || bedHinge == null) return;
            bedFollow = on;
            if (on)
            {
                bedMotorWas = bedHinge.useMotor; bedLimitsWas = bedHinge.useLimits; bedSpringWas = bedHinge.useSpring;
                bedLiftWas = bedLift != null && bedLift.enabled;
                if (bedLift != null) bedLift.enabled = false;
                bedHinge.useMotor = false; bedHinge.useSpring = false; bedHinge.useLimits = false;
                Log.Info("bois : benne suivie (charniere libre le temps du suivi)");
                return;
            }
            // Rendue a sa charniere : tenue a l'angle atteint (la cible du ressort, les butees d'avant l'auraient ramenee).
            float ang = bedHinge.angle;
            if (bedSpringWas) { JointSpring s = bedHinge.spring; s.targetPosition = ang; bedHinge.spring = s; }
            if (bedLimitsWas)
            {
                JointLimits l = bedHinge.limits;
                if (ang < l.min) l.min = ang; if (ang > l.max) l.max = ang;
                bedHinge.limits = l;
            }
            if (bedMotorWas) { JointMotor m = bedHinge.motor; m.targetVelocity = 0f; bedHinge.motor = m; }
            bedHinge.useMotor = bedMotorWas; bedHinge.useLimits = bedLimitsWas; bedHinge.useSpring = bedSpringWas;
            if (bedLift != null) bedLift.enabled = bedLiftWas;
            Log.Info("bois : benne rendue a sa charniere (" + HingeState() + ")");
        }

        static int BedAuthority() { return flatbed != null ? VehicleSync.Authority(flatbed) : 0; }

        // Angle (degres) de la benne autour de l'axe de sa charniere depuis la pose baissee.
        static float BedAngle()
        {
            Quaternion rel = Quaternion.Inverse(bedRest) * bed.transform.localRotation;
            float a; Vector3 ax;
            rel.ToAngleAxis(out a, out ax);
            if (a > 180f) a -= 360f;
            return Vector3.Dot(ax, bedHinge.axis) < 0f ? -a : a;
        }

        static void BedUpdate(float now)
        {
            if (bed == null || bedHinge == null) return;
            bool active = Session.Active && Session.RemoteCount > 0;
            bool authority = BedAuthority() == Session.LocalId;
            BedFollow(active && !authority && now - bedTargetAt < 2f && !bed.isKinematic);
            if (!active) return;
            if (authority)
            {
                if (now < nextBedSend) return;
                float a = BedAngle();
                if (!float.IsNaN(bedSent) && Mathf.Abs(a - bedSent) < 0.3f && now - bedSentAt < 3f) return;
                nextBedSend = now + 0.25f;
                bedSent = a; bedSentAt = now;
                Session.SendAll(new NetWriter(Msg.Job).U8(Session.LocalId).Str("@benne").F32(a), false);
                return;
            }
            if (!bedFollow) return;
            // Une correction par pas de physique : les VelocityChange s'additionnent jusqu'au pas suivant et
            // angularVelocity ne bouge pas entre deux images sans pas -- a 100-144 i/s (2-3 images par pas de
            // 50 Hz) la correction etait appliquee 2-3 fois et la benne oscillait contre ses butees.
            if (Time.fixedTime == bedLastFixed) return;
            bedLastFixed = Time.fixedTime;
            float err = Mathf.DeltaAngle(BedAngle(), bedTarget);
            Vector3 axis = bed.transform.TransformDirection(bedHinge.axis).normalized;
            Rigidbody car = bedHinge.connectedBody;
            Vector3 rel = bed.angularVelocity - (car != null ? car.angularVelocity : Vector3.zero);
            float w = Vector3.Dot(rel, axis) * Mathf.Rad2Deg;
            // (charniere libre : sans correction a chaque pas, la benne retomberait sous son poids)
            if (Mathf.Abs(err) < 0.05f && Mathf.Abs(w) < 0.5f) return;
            float want = Mathf.Clamp(err * 4f, -25f, 25f);
            bed.AddTorque(axis * ((want - w) * Mathf.Deg2Rad), ForceMode.VelocityChange);
            if (bed.IsSleeping()) bed.WakeUp();
        }

        static void OnBed(float a) { bedTarget = Mathf.Clamp(a, -10f, 90f); bedTargetAt = Time.realtimeSinceStartup; }

        // Declencheur du plateau : seulement chez l'autorite (sinon chacun comptait la buche tombee chez lui).
        static void LogTriggerAuthority()
        {
            if (logTrigger == null || logTrigger.F == null) return;
            FindBed();
            if (ltCollider == null) return;
            bool on = !Session.Active || Session.RemoteCount == 0 || BedAuthority() == Session.LocalId;
            if (ltCollider.enabled == on) return;
            ltCollider.enabled = on;
            Log.Info("bois : plateau " + (on ? "compte les buches ici" : "compte chez #" + BedAuthority() + " (declencheur coupe ici)"));
        }

        // ================================================================ fosses septiques
        // Pompage (GIFU) : le niveau de chaque fosse et le contenu de la citerne sont tenus par l'autorite de la
        // GIFU (son conducteur, ou celui qui a laisse son moteur tourner, sinon l'hote) et recopies ailleurs
        // (Fluids). Pour qu'elle pompe, il lui faut le tuyau dans la fosse ; or le tuyau n'est dans la fosse que
        // chez celui qui l'y a mis (le tuyau et son raccord ne sont pas synchronises) : le declencheur de la
        // fosse (ShitLevelTrigger 'Trigger', "Hose in" -> met HoseInShit et ShitWell a la pompe de la GIFU ;
        // "Wait 2" -> les retire) est annonce par celui qui le vit (@tuyau) et rejoue chez les autres.
        // VehicleSync.DriverRpm n'est pas necessaire : la copie de la GIFU recoit deja le regime du conducteur
        // (VehicleSync l'ecrit dans son Drivetrain) et c'est l'autorite, au vrai regime, qui fait foi.
        class Well { public string Key; public PlayMakerFSM F; public bool LocalIn, RemoteIn; }
        static readonly Dictionary<string, Well> wells = new Dictionary<string, Well>();
        static readonly List<Well> wellsWaiting = new List<Well>();
        static readonly HashSet<string> HoseInStates = new HashSet<string> { "Hose in", "Wait", "Check hose out" };

        class HoseHook : ModHook
        {
            public override string Module { get { return "quetes (fosse)"; } }
            public Well W; public bool In;
            public override void OnEnter()
            {
                try { if (Replay.Depth == 0 && !applying) HoseLocal(W, In); } catch (System.Exception e) { Replay.HookError(e); }
                Finish();
            }
        }

        static bool HookWell(Well w)
        {
            FsmState a = w.F.Fsm.GetState("Hose in"), b = w.F.Fsm.GetState("Wait 2");
            if (!a.IsInitialized || !b.IsInitialized) return false;
            try
            {
                var la = new List<FsmStateAction>(a.Actions); la.Insert(0, new HoseHook { W = w, In = true }); a.Actions = la.ToArray();
                var lb = new List<FsmStateAction>(b.Actions); lb.Insert(0, new HoseHook { W = w, In = false }); b.Actions = lb.ToArray();
            }
            catch { return false; }
            hooked.Add(w.F);
            return true;
        }

        // "Wait 2" suit aussi tout objet qui n'est pas le tuyau : seul le passage dedans -> dehors du tuyau compte.
        static void HoseLocal(Well w, bool inWell)
        {
            if (w.LocalIn == inWell) return;
            w.LocalIn = inWell;
            Log.Info("fosse : tuyau " + (inWell ? "dans " : "sorti de ") + w.Key + " ici");
            if (Session.Active) Session.SendAll(new NetWriter(Msg.Job).U8(Session.LocalId).Str("@tuyau").Str(w.Key).Bool(inWell), true);
        }

        static void OnHose(int who, string key, bool inWell)
        {
            Well w;
            if (!wells.TryGetValue(key, out w) || w.F == null) { Log.Warn("fosse " + key + " introuvable ici"); return; }
            w.RemoteIn = inWell;
            bool isIn = HoseInStates.Contains(w.F.ActiveStateName);
            if (isIn == inWell || !w.F.gameObject.activeInHierarchy || !hooked.Contains(w.F))
            {
                Log.Info("fosse : tuyau de #" + who + (inWell ? " dans " : " sorti de ") + key + (isIn == inWell ? " (deja ainsi ici)" : " (fosse pas chargee ici)"));
                return;
            }
            applying = true; Replay.Depth++;
            try { Game.SetState(w.F, inWell ? "Hose in" : "Wait 2"); }
            finally { applying = false; Replay.Depth--; }
            Log.Info("fosse : tuyau de #" + who + (inWell ? " dans " : " sorti de ") + key + " -> " + w.F.ActiveStateName);
        }

        // ================================================================ messages @
        // Msg.Job dont la cle commence par '@' : (joueur, type, donnees).
        //  @cree   createur, ID, position, rotation   (fiable)
        //  @fend   ID de la buche                     (fiable)
        //  @parti  nombre, IDs                        (fiable)
        //  @benne  angle de la benne                  (non fiable, 4/s au plus)
        //  @tuyau  fosse, dedans                      (fiable)
        //  @etape  Stage atteint par la fendeuse      (fiable, U8)
        static void OnSpecial(int who, string kind, NetReader r)
        {
            if (Session.IsHost) Session.Broadcast(new NetWriter(Msg.Job).U8(who).Str(kind).Raw(r.Rest()), kind != "@benne", who);
            switch (kind)
            {
                case "@cree":
                {
                    string key = r.Str(), id = r.Str();
                    Vector3 pos = r.Vec();
                    Quaternion rot = r.Quat();
                    OnCreated(who, key, id, pos, rot);
                    break;
                }
                case "@fend": OnSplit(who, r.Str()); break;
                case "@parti":
                {
                    int n = r.U8();
                    for (int i = 0; i < n; i++) OnGone(r.Str());
                    break;
                }
                case "@benne": OnBed(r.F32()); break;
                case "@etape": OnFeedStage(who, r.U8()); break;
                case "@tuyau":
                {
                    string key = r.Str();
                    OnHose(who, key, r.Bool());
                    break;
                }
                default: Log.Warn("quetes : message " + kind + " inconnu"); break;
            }
        }

        static void SpecialUpdate(float now)
        {
            CheckWaiting(now);
            if (!specialsScanned) return;
            if (now >= nextSpecialWait && (creatorsWaiting.Count > 0 || wellsWaiting.Count > 0))
            {
                // Createurs et fosses pas encore charges (objet jamais actif) : repris des qu'ils s'activent.
                nextSpecialWait = now + 2f;
                for (int i = creatorsWaiting.Count - 1; i >= 0; i--)
                {
                    Creator c = creatorsWaiting[i];
                    if (c.F == null) { creatorsWaiting.RemoveAt(i); continue; }
                    if (c.F.gameObject.activeInHierarchy && HookCreator(c)) { creatorsWaiting.RemoveAt(i); Log.Info("bois : createur " + c.Key + " suivi"); }
                }
                for (int i = wellsWaiting.Count - 1; i >= 0; i--)
                {
                    Well w = wellsWaiting[i];
                    if (w.F == null) { wellsWaiting.RemoveAt(i); continue; }
                    if (w.F.gameObject.activeInHierarchy && HookWell(w)) { wellsWaiting.RemoveAt(i); Log.Info("fosse : " + w.Key + " suivie"); }
                }
            }
            if (feed != null && feed.F != null)
            {
                bool act = feed.F.gameObject.activeInHierarchy;
                if (act && !feedWasActive) FeedActivated();
                feedWasActive = act;
            }
            CheckGone(now);
            if (now >= nextAuthority) { nextAuthority = now + 1f; LogTriggerAuthority(); }
            BedUpdate(now);
        }

        // ================================================================ essais
        // Chacun note son etat a 28 s ("avant"), puis a 40, 52, 64 et 76 s. Les gestes partent a 30 s au plus
        // tot, une fois l'autre joueur en partie depuis 15 s (T = ce moment).
        // [Test] Autotest=bois (TestQuete : partie de la cle du billot, "MachineHall/Logging/Logwall" par defaut) :
        // l'hote pose une buche sur le billot a T (comme le clic), la fend a T+4 s (attache cassee, comme la
        // hache), jette une moitie fendue dans le plateau a T+14 s. Etat : objets marques, stress, buches
        // coupees, argent, plateau. Attendu chez l'invite : "bois : log(Clone) b0..-1 cree par #0", "buche ...
        // fendue par #0", stress et "coupees" comme "avant", deux moities "firewood(Clone)" ; apres T+14 s,
        // "objets marques disparus ici" chez l'hote, cette moitie "absent" chez l'invite aussi, plateau : meme
        // Firewood des deux cotes, declencheur actif chez l'hote, coupe chez l'invite.
        // [Test] Autotest=fosse : l'hote (autorite de la GIFU garee) vide 1 m de la premiere fosse et ajoute
        // 1000 L a la citerne a T ; l'invite derive seul la 2e fosse a T+3 s (pas envoye), met le tuyau dans la
        // 1re a T+5 s (comme le declencheur), l'en retire a T+17 s. Attendu : chez l'invite la 1re fosse et la
        // citerne = l'hote ; chez l'hote la 2e fosse inchangee, "fosse : tuyau de #1 dans ..." puis
        // HoseInShit=True, puis "sorti de" et HoseInShit=False.
        // [Test] Autotest=fendeuse : chacun simule une lame qui tourne (Conveyer 'Blade' arrete le temps de
        // l'essai, Speed 1, Blocked vrai 1 s apres l'entree en "Wait blade") et coupe le succes Steam ; l'hote met
        // la fendeuse en marche a T+1 s (grume factice dans LogInHand, Triggers -ACTIVATE-> "State 2", rejoue
        // chez l'invite par WorldFsms), puis clique a T+6 s ("Wait button" -USE-> "State 2"). Attendu : hote
        // "fendeuse mise en marche ici", invite "par un autre joueur" ; chez l'invite "etape 1 ... a un autre
        // joueur" puis "quete de #0 : Cutter/FeedLog::Use#0 -> ..." et "etape 2 ... a un autre joueur" ; 4 fois
        // "cree ici (...Conveyer::Logic)" chez l'hote, 4 fois "cree par #0" chez l'invite ; a 52 s, "buches
        // marquees 4" et le meme Stage (2) des deux cotes.
        // [Test] Autotest=benne : l'hote (autorite du plateau) souleve la benne a T (couple sur l'axe de la
        // charniere 1,5 s, puis l'autre sens si elle n'a pas bouge) ; l'invite la suit et note une fois par pas
        // de physique l'ecart et la vitesse. Attendu chez l'invite a T+12 s : "benne suivie" avec peu
        // d'inversions de l'ecart (moins de 10) et une vitesse max d'environ 25 deg/s (pas des centaines), angle
        // final proche de celui de l'hote.
        // [Test] Autotest=taxi : chacun note a qui sont les automates du taxi (MACHTWAGEN sous JOBS) gardes par
        // Jobs ; a T chacun active le taxi s'il est inactif (rendu inactif a T+20 s). Attendu, des "avant" et
        // pareil des deux cotes : "autres 0" (aucune commande du taxi a WorldFsms), puis apres activation
        // "en attente" qui baisse et "accroches" qui monte (CheckWaiting).
        // [Test] Autotest=fendeuse2 (TestPos=62,0,-85 : devant la fendeuse) : comme fendeuse, mais la lame simulee
        // de l'invite est en retard ([Test] FendeuseRetard=8 s apres "Wait blade", au lieu de 1 s) et l'hote clique
        // une 2e fois a T+16 s. Attendu chez l'invite : 3 fois "etape N de la fendeuse recue de #0, rattrapee ici"
        // (N = 1, 2, 3) et, a 64 et 76 s, le meme Stage (3) que l'hote.
        // [Test] Autotest=benne2 (TestPos=62,0,-78 : a cote du plateau) : la benne levee comme par le levier du
        // tracteur : l'hote envoie HYD_UP a 'Lift' de FLATBED/Bed a T, HYD_OFF a T+5, HYD_DOWN a T+9, HYD_OFF a T+15
        // ([Test] BenneForce=x : HydraulicForce mise avant, si le jeu la tient a 0 sans tracteur). Si la benne n'a
        // pas bouge 3 s apres HYD_UP, la charniere de l'hote est menee directement (moteur 10 deg/s, 3 s : note
        // "moteur d'essai"). Les actions de 'Lift' (parametres) sont notees au debut. Chacun note chaque seconde
        // angle, cible, suivi et charniere. Attendu chez l'invite : "benne suivie", ecart final < 3 deg ("OK").
        // [Test] Autotest=boitegants (TestPos=1933,5,-419 : a cote de la CORRIS) : a T, chacun active
        // [Test] BoiteGants (CORRIS/Assemblies/VINP_Dashboard/Glovebox, comme le montage du tableau de bord) ; l'hote
        // clique la boite a T+5 ("Mouse over 1" -FINISHED-> "Open door") et le loquet du capot a T+9 ("Mouse over 2"
        // -PROCEED-> "State 1"), puis les deux encore a T+15 et T+19. Attendu : "automates suivis a leur activation"
        // des deux cotes (quelques dixiemes de seconde apres T) ; chez l'invite "quete de #0 : ...Glovebox/Pivot/coll
        // ::Use#0 -> Open door" puis la boite Open=True, le loquet Open qui bascule comme chez l'hote.
        static int testStep, testLogs;
        static float testAt;
        static bool testBefore, testClick2;

        public static void Test(string mode, float t)
        {
            if (mode == "fendeuse" || mode == "fendeuse2" || mode == "benne" || mode == "benne2" || mode == "taxi-commandes" || mode == "boitegants")
            {
                if (t > 28f && !testBefore) { testBefore = true; Log.Info("autotest : " + mode + ", avant : " + MoreState(mode)); }
                if (t > 30f)
                {
                    if (mode == "fendeuse" || mode == "fendeuse2") TestFeed(mode == "fendeuse2");
                    else if (mode == "benne") TestBed();
                    else if (mode == "benne2") TestBed2();
                    else if (mode == "boitegants") TestGlovebox();
                    else TestTaxi();
                }
                if (testLogs < 4 && t > 40f + 12f * testLogs) { testLogs++; Log.Info("autotest : " + mode + ", " + MoreState(mode)); }
                return;
            }
            if (mode != "bois" && mode != "fosse") return;
            bool bois = mode == "bois";
            if (t > 28f && !testBefore) { testBefore = true; Log.Info("autotest : " + mode + ", avant : " + (bois ? BoisState() : Fluids.SepticState() + " ; " + HoseState())); }
            if (bois) TestBois(); else TestFosse();
            // Etats notes par chacun a 40, 52, 64 et 76 s.
            if (testLogs < 4 && t > 40f + 12f * testLogs) { testLogs++; Log.Info("autotest : " + mode + ", " + (bois ? BoisState() : Fluids.SepticState() + " ; " + HoseState())); }
        }

        // Essais : un autre joueur est en partie depuis 'seconds' s (ses automates sont releves).
        static float otherSince = -1;
        public static bool OtherInGame(float seconds)
        {
            float now = Time.realtimeSinceStartup;
            bool any = false;
            foreach (PlayerInfo p in Session.Players.Values) if (!p.Local && p.Level == 1 && now - p.StateTime < 3f) { any = true; break; }
            if (!any) { otherSince = -1; return false; }
            if (otherSince < 0) otherSince = now;
            return now - otherSince > seconds;
        }

        static void TestBois()
        {
            if (!Session.IsHost) return;
            float now = Time.realtimeSinceStartup;
            if (testStep == 0 && OtherInGame(15f)) { testStep = 1; testAt = now; Log.Info("autotest : bois, " + TestCreate(Config.Get("Test", "TestQuete", "MachineHall/Logging/Logwall"))); }
            if (testStep == 1 && now - testAt > 4f) { testStep = 2; Log.Info("autotest : bois, " + TestSplit(lastId)); }
            if (testStep == 2 && now - testAt > 14f) { testStep = 3; Log.Info("autotest : bois, " + TestDrop(lastId != null ? lastId + "a" : null)); }
        }

        static string TestCreate(string part)
        {
            foreach (Creator c in creators.Values)
                if (c.F != null && c.Key.Contains(part))
                {
                    if (!c.Ready) return c.Key + " pas encore charge";
                    Game.SetState(c.F, c.State);   // (pas un rejeu : la creation part comme au clic)
                    return c.Key + " -> " + c.F.ActiveStateName + ", " + (lastId ?? "rien de marque");
                }
            return "aucun createur " + part + " (" + creators.Count + " suivis)";
        }

        static string TestSplit(string id)
        {
            GameObject b;
            if (id == null || !tagged.TryGetValue(id + "b", out b) || b == null) return "pas de buche " + id;
            FixedJoint fj = b.GetComponent<FixedJoint>();
            if (fj == null) return "buche " + id + " sans attache";
            Object.Destroy(fj);   // comme le coup de hache : "Check joint" le voit a l'image suivante
            return "attache de " + id + " cassee";
        }

        static string TestDrop(string id)
        {
            GameObject go;
            if (id == null || !tagged.TryGetValue(id, out go) || go == null) return "pas d'objet " + id;
            FindBed();
            if (ltCollider == null) return "pas de plateau";
            Rigidbody rb = go.GetComponent<Rigidbody>();
            go.transform.position = ltCollider.bounds.center + Vector3.up * 0.2f;
            if (rb != null) { rb.velocity = Vector3.zero; rb.WakeUp(); }
            return go.name + " " + id + " jete dans le plateau en " + go.transform.position.ToString("F1");
        }

        static string BoisState()
        {
            int halves = 0, wood = 0, longs = 0;
            foreach (KeyValuePair<string, GameObject> kv in tagged)
            {
                if (kv.Value == null) continue;
                string n = kv.Value.name;
                if (n.StartsWith("firewood")) wood++; else if (n.StartsWith("long log")) longs++; else halves++;
            }
            FsmFloat stress = FsmVariables.GlobalVariables.FindFsmFloat("PlayerStress");
            FsmInt count = LogsChopped();
            string mine = "";
            if (lastId != null)
                foreach (string s in new[] { "a", "b" })
                {
                    GameObject g;
                    mine += " " + lastId + s + "=" + (tagged.TryGetValue(lastId + s, out g) && g != null ? g.name + "@" + g.transform.position.ToString("F1") : "absent");
                }
            string plateau = "?";
            if (logTrigger != null && logTrigger.F != null)
            {
                FsmFloat fw = logTrigger.F.FsmVariables.FindFsmFloat("Firewood"), lg = logTrigger.F.FsmVariables.FindFsmFloat("Logs");
                plateau = "Firewood " + (fw != null ? fw.Value.ToString("F0") : "?") + ", Logs " + (lg != null ? lg.Value.ToString("F0") : "?")
                          + ", etat " + logTrigger.F.ActiveStateName + ", declencheur " + (ltCollider != null ? (ltCollider.enabled ? "actif" : "coupe") : "?")
                          + (bed != null && bedHinge != null ? ", benne " + BedAngle().ToString("F1") : "");
            }
            return "marques : " + halves + " moities, " + wood + " buches fendues, " + longs + " grumes ;" + mine
                   + " ; stress " + (stress != null ? stress.Value.ToString("F1") : "?") + ", coupees " + (count != null ? count.Value.ToString() : "?")
                   + ", " + Wallet.State() + " ; plateau " + plateau;
        }

        static void TestFosse()
        {
            float now = Time.realtimeSinceStartup;
            if (testStep == 0 && OtherInGame(15f))
            {
                testStep = 1; testAt = now;
                if (Session.IsHost) Log.Info("autotest : fosse, pompe ici : " + Fluids.TestSeptic(0, -1f, 1000f));
            }
            if (Session.IsHost) return;
            if (testStep == 1 && now - testAt > 3f) { testStep = 2; Log.Info("autotest : fosse, derive locale : " + Fluids.TestSeptic(1, -0.5f, 0f)); }
            if (testStep == 2 && now - testAt > 5f) { testStep = 3; Log.Info("autotest : fosse, " + TestHose(0, true)); }
            if (testStep == 3 && now - testAt > 17f) { testStep = 4; Log.Info("autotest : fosse, " + TestHose(0, false)); }
        }

        static List<Well> SortedWells()
        {
            var l = new List<Well>(wells.Values);
            l.Sort((a, b) => string.CompareOrdinal(a.Key, b.Key));
            return l;
        }

        static string TestHose(int index, bool inWell)
        {
            List<Well> l = SortedWells();
            if (index >= l.Count) return "pas de fosse " + index + " (" + l.Count + ")";
            Well w = l[index];
            if (!hooked.Contains(w.F)) return w.Key + " pas encore suivie";
            Game.SetState(w.F, inWell ? "Hose in" : "Wait 2");   // comme le declencheur quand le bout du tuyau entre/sort
            return "tuyau " + (inWell ? "mis dans " : "retire de ") + w.Key + " -> " + w.F.ActiveStateName;
        }

        static string HoseState()
        {
            var sb = new System.Text.StringBuilder("tuyaux :");
            foreach (Well w in SortedWells())
            {
                string[] seg = w.Key.Split('/');
                string k = seg.Length > 1 ? seg[1] : w.Key;   // JOBS/HouseShit1/... -> HouseShit1
                sb.Append(' ').Append(k).Append('=').Append(w.F != null ? w.F.ActiveStateName : "?").Append(w.LocalIn ? "(ici)" : "").Append(w.RemoteIn ? "(autre)" : "");
            }
            return sb.ToString();
        }

        static string MoreState(string mode)
        {
            if (mode == "fendeuse" || mode == "fendeuse2") return FeedState();
            if (mode == "benne" || mode == "benne2") { FindBed(); return bed != null && bedHinge != null ? "benne " + BedAngle().ToString("F1") + " (autorite #" + BedAuthority() + ", " + HingeState() + ")" : "pas de benne (plateau pas suivi)"; }
            if (mode == "boitegants") return GloveState();
            return TaxiState();
        }

        // ---------------------------------------------------------------- essai fendeuse
        static PlayMakerFSM testBlade;
        static bool testBladeWas, testNoAchievement;
        static float testWaitBladeSince = -1;

        static void TestFeed(bool late)
        {
            float now = Time.realtimeSinceStartup;
            testBladeDelay = late && !Session.IsHost ? Config.GetInt("Test", "FendeuseRetard", 8) : 1f;
            if (testStep == 0 && OtherInGame(15f))
            {
                testStep = 1; testAt = now;
                if (feed == null || feed.F == null) { testStep = 99; Log.Info("autotest : fendeuse, pas de fendeuse ici"); return; }
                Transform cutter = feed.F.transform.parent;
                Transform conv = cutter != null ? cutter.Find("Conveyer") : null;
                testBlade = conv != null ? Game.FsmOn(conv.gameObject, "Blade") : null;
                if (testBlade != null) { testBladeWas = testBlade.enabled; testBlade.enabled = false; }
                testNoAchievement = true;
                TestMuteAchievement();
                Log.Info("autotest : fendeuse, lame simulee ici (" + (testBlade != null ? "Blade arrete" : "pas de Blade") + "), " + FeedState());
            }
            if (testStep < 1 || testStep > 3) return;
            TestBladeSim(now);
            if (Session.IsHost && testStep == 1 && now - testAt > 1f) { testStep = 2; Log.Info("autotest : fendeuse, " + TestFeedStart()); }
            if (Session.IsHost && testStep == 2 && now - testAt > 6f) { testStep = 3; Log.Info("autotest : fendeuse, " + TestFeedClick()); }
            if (late && Session.IsHost && testStep == 3 && !testClick2 && now - testAt > 16f) { testClick2 = true; Log.Info("autotest : fendeuse2, 2e " + TestFeedClick()); }
            if (now - testAt > (late ? 32f : 25f))
            {
                testStep = 4;
                if (testBlade != null) testBlade.enabled = testBladeWas;
                Log.Info("autotest : fendeuse, lame rendue au jeu ; " + FeedState());
            }
        }

        // Lame qui tourne : Blocked faux, sauf 1 s apres l'entree en "Wait blade" (la lame passe devant la grume).
        static void TestBladeSim(float now)
        {
            if (testBlade == null || feed == null || feed.F == null) return;
            FsmFloat sp = testBlade.FsmVariables.FindFsmFloat("Speed");
            if (sp != null) sp.Value = 1f;   // (vitesse du tapis, lue par les buches du reservoir)
            FsmBool bl = testBlade.FsmVariables.FindFsmBool("Blocked");
            if (bl == null) return;
            if (!feed.F.gameObject.activeInHierarchy || feed.F.ActiveStateName != "Wait blade") { testWaitBladeSince = -1; bl.Value = false; return; }
            if (testWaitBladeSince < 0) testWaitBladeSince = now;
            bl.Value = now - testWaitBladeSince > testBladeDelay;
        }
        static float testBladeDelay = 1f;

        static void TestMuteAchievement()
        {
            if (!testNoAchievement || feedMute == null) return;
            foreach (FsmStateAction a in feedMute) if (a.GetType().Name == "SendEventByName") a.Enabled = false;   // (FeedRestore ne les rend pas)
        }

        // Comme le joueur qui pose la grume : grume (factice) dans LogInHand, Triggers -ACTIVATE-> "State 2".
        static string TestFeedStart()
        {
            Transform cutter = feed.F.transform.parent;
            Transform tt = cutter != null ? cutter.Find("Triggers") : null;
            PlayMakerFSM trig = tt != null ? Game.FsmOn(tt.gameObject, "Logic") : null;
            if (trig == null) return "pas de Triggers 'Logic'";
            if (!trig.gameObject.activeInHierarchy) return "Triggers inactif (fendeuse deja en marche ?), " + FeedState();
            FsmGameObject hand = FeedHand();
            if (hand != null) hand.Value = new GameObject("MWCoop-essai-grume");   // (detruite par "State 2", comme la vraie)
            FsmBool t1 = trig.FsmVariables.FindFsmBool("Trigger1"), t2 = trig.FsmVariables.FindFsmBool("Trigger2");
            if (t1 != null) t1.Value = true;
            if (t2 != null) t2.Value = true;
            if (trig.ActiveStateName != "Wait for assembly") Game.SetState(trig, "Wait for assembly");
            // Par WorldFsms (comme si le joueur venait de cliquer : l'evenement part chez l'autre), sinon directement.
            string r = WorldFsms.TestEvent("Cutter/Triggers::Logic", "ACTIVATE");
            if (r.StartsWith("rien pour")) { trig.SendEvent("ACTIVATE"); r = "Triggers pas suivi par WorldFsms, evenement local seulement"; }
            return "grume posee : " + r + ", fendeuse " + (feed.F.gameObject.activeInHierarchy ? "en marche" : "arretee");
        }

        // Comme le clic : "Wait button" -USE-> "State 2" (le survol, souris ailleurs -> FINISHED, coupe le temps du clic).
        static string TestFeedClick()
        {
            if (!feed.F.gameObject.activeInHierarchy) return "fendeuse arretee (" + feed.F.ActiveStateName + "), pas de clic";
            FsmState wb = feed.F.Fsm.GetState("Wait button");
            if (wb == null) return "pas d'etat Wait button";
            var off = new List<FsmStateAction>();
            foreach (FsmStateAction a in wb.Actions) if (a != null && a.Enabled && a.GetType().Name.StartsWith("MousePick")) { a.Enabled = false; off.Add(a); }
            try { Game.SetState(feed.F, "Wait button"); feed.F.SendEvent("USE"); }
            finally { foreach (FsmStateAction a in off) a.Enabled = true; }
            return "clic sur la fendeuse -> " + feed.F.ActiveStateName + " (Stage " + FeedStage() + ", clic d'ici " + feedClick + ")";
        }

        static string FeedStage()
        {
            FsmInt s = feed != null && feed.F != null ? feed.F.FsmVariables.FindFsmInt("Stage") : null;
            return s != null ? s.Value.ToString() : "?";
        }

        static string FeedState()
        {
            if (feed == null || feed.F == null) return "pas de fendeuse";
            int wood = 0;
            foreach (KeyValuePair<string, GameObject> kv in tagged) if (kv.Value != null && kv.Value.name.StartsWith("firewood")) wood++;
            return "fendeuse " + (feed.F.gameObject.activeInHierarchy ? "en marche" : "arretee") + ", etat " + feed.F.ActiveStateName + ", Stage " + FeedStage()
                   + ", suivie " + (hooked.Contains(feed.F) ? "oui" : "non") + ", derniere etape " + (feedStageLocal ? "ici" : "a un autre") + ", buches marquees " + wood
                   + ", etapes rattrapees " + feedCaughtUp;
        }

        // ---------------------------------------------------------------- essai benne
        static float testBedErr, testBedMaxW, testBedLastFixed = -1;
        static int testBedFlips;

        static void TestBed()
        {
            float now = Time.realtimeSinceStartup;
            if (testStep == 0 && OtherInGame(15f))
            {
                testStep = 1; testAt = now;
                FindBed();
                if (bed == null || bedHinge == null) { testStep = 99; Log.Info("autotest : benne, pas de benne ici (plateau pas suivi)"); return; }
                testBedFlips = 0; testBedMaxW = 0; testBedErr = 0;
                Log.Info("autotest : benne, debut : benne " + BedAngle().ToString("F1") + ", autorite #" + BedAuthority());
            }
            if (testStep != 1 || bed == null || bedHinge == null) return;
            if (Time.fixedTime == testBedLastFixed) return;   // une fois par pas de physique
            testBedLastFixed = Time.fixedTime;
            float dt = now - testAt;
            Vector3 axis = bed.transform.TransformDirection(bedHinge.axis).normalized;
            if (BedAuthority() == Session.LocalId)
            {
                // (couple seulement : ni cinematique ni ressort/butees de la charniere touches)
                if (dt < 1.5f) bed.AddTorque(axis * 4f, ForceMode.Acceleration);
                else if (dt < 3f && Mathf.Abs(BedAngle()) < 1f) bed.AddTorque(-axis * 4f, ForceMode.Acceleration);
                if (dt < 3f && bed.IsSleeping()) bed.WakeUp();
            }
            else if (now - bedTargetAt < 2f)
            {
                float err = Mathf.DeltaAngle(BedAngle(), bedTarget);
                Rigidbody car = bedHinge.connectedBody;
                float w = Mathf.Abs(Vector3.Dot(bed.angularVelocity - (car != null ? car.angularVelocity : Vector3.zero), axis)) * Mathf.Rad2Deg;
                if (w > testBedMaxW) testBedMaxW = w;
                if (Mathf.Abs(err) > 0.5f)
                {
                    if (testBedErr != 0f && Mathf.Sign(err) != Mathf.Sign(testBedErr)) testBedFlips++;
                    testBedErr = err;
                }
            }
            if (dt > 12f)
            {
                testStep = 2;
                Log.Info("autotest : benne suivie : angle " + BedAngle().ToString("F1") + (BedAuthority() == Session.LocalId ? " (autorite ici)"
                         : ", cible " + bedTarget.ToString("F1") + ", inversions " + testBedFlips + ", vitesse max " + testBedMaxW.ToString("F0") + " deg/s, " + (1f / Mathf.Max(Time.smoothDeltaTime, 0.001f)).ToString("F0") + " i/s"));
            }
        }

        // ---------------------------------------------------------------- essai benne (levier)
        static float testBed2Log, testBed2Up = float.NaN, testBed2MaxErr;
        static bool testBed2Motor;

        static void TestBed2()
        {
            float now = Time.realtimeSinceStartup;
            if (testStep == 0 && OtherInGame(15f))
            {
                testStep = 1; testAt = now; testBed2MaxErr = 0f; testBed2Motor = false;
                FindBed();
                if (bed == null || bedHinge == null) { testStep = 99; Log.Info("autotest : benne2, pas de benne ici (plateau pas suivi)"); return; }
                Log.Info("autotest : benne2, debut : " + HingeState() + ", autorite #" + BedAuthority());
                if (bedLift != null) foreach (string st in new[] { "UP", "UP 2", "UP 3", "UP 4" }) Log.Info("autotest : benne2, 'Lift' " + DumpActions(bedLift, st));
            }
            if (testStep < 1 || testStep > 6 || bed == null || bedHinge == null) return;
            float dt = now - testAt;
            bool host = BedAuthority() == Session.LocalId;
            if (host && bedLift == null && testStep == 1) { testStep = 2; Log.Info("autotest : benne2, pas d'automate 'Lift' sur la benne"); }
            if (host && bedLift != null)
            {
                if (testStep == 1)
                {
                    testStep = 2;
                    float force = Config.GetInt("Test", "BenneForce", 0);
                    FsmFloat hf = bedLift.FsmVariables.FindFsmFloat("HydraulicForce");
                    if (force > 0f && hf != null) hf.Value = force;
                    testBed2Up = BedAngle();
                    bedLift.SendEvent("HYD_UP");   // comme le levier du tracteur
                    Log.Info("autotest : benne2, HYD_UP -> " + bedLift.ActiveStateName + " (" + HingeState() + ")");
                }
                // La benne n'a pas bougee (pas de pression sans tracteur ?) : charniere menee directement.
                if (testStep == 2 && dt > 3f && Mathf.Abs(BedAngle() - testBed2Up) < 1f)
                {
                    testBed2Motor = true;
                    Log.Info("autotest : benne2, pas montee par HYD_UP (" + HingeState() + "), moteur d'essai 10 deg/s");
                    bedLift.enabled = false;   // (ses SetHingeJointProperties remettraient le moteur du jeu ; rendu a la fin)
                }
                if (testBed2Motor && testStep == 2)
                {
                    JointMotor m = bedHinge.motor; m.targetVelocity = dt < 6f ? 10f : 0f; m.force = 50000f; m.freeSpin = false;
                    bedHinge.motor = m; bedHinge.useMotor = true; bedHinge.useLimits = false;
                    if (bed.IsSleeping()) bed.WakeUp();
                }
                if (testStep == 2 && dt > 5f) { testStep = 3; if (!testBed2Motor) bedLift.SendEvent("HYD_OFF"); Log.Info("autotest : benne2, HYD_OFF -> " + bedLift.ActiveStateName + ", angle " + BedAngle().ToString("F1")); }
                if (testStep == 3 && dt > 9f)
                {
                    testStep = 4;
                    if (testBed2Motor) { JointMotor m = bedHinge.motor; m.targetVelocity = -10f; bedHinge.motor = m; }
                    else bedLift.SendEvent("HYD_DOWN");
                    Log.Info("autotest : benne2, HYD_DOWN -> " + bedLift.ActiveStateName);
                }
                if (testStep == 4 && dt > 15f)
                {
                    testStep = 5;
                    if (testBed2Motor) { JointMotor m = bedHinge.motor; m.targetVelocity = 0f; bedHinge.motor = m; bedLift.enabled = true; }
                    else bedLift.SendEvent("HYD_OFF");
                    Log.Info("autotest : benne2, HYD_OFF -> " + bedLift.ActiveStateName + ", angle " + BedAngle().ToString("F1"));
                }
            }
            if (!host && dt > 2f && now - bedTargetAt < 1f) testBed2MaxErr = Mathf.Max(testBed2MaxErr, Mathf.Abs(Mathf.DeltaAngle(BedAngle(), bedTarget)));
            if (now >= testBed2Log && dt < 20f)
            {
                testBed2Log = now + 1f;
                Log.Info("autotest : benne2 (" + (Session.IsHost ? "hote" : "invite") + ") t+" + dt.ToString("F0") + " angle " + BedAngle().ToString("F1")
                         + (host ? " (autorite)" : ", cible " + bedTarget.ToString("F1") + " (recue il y a " + (now - bedTargetAt).ToString("F1") + " s), suivi " + bedFollow)
                         + " | " + HingeState());
            }
            if (dt > 20f)
            {
                testStep = 7;
                float err = host ? 0f : Mathf.Abs(Mathf.DeltaAngle(BedAngle(), bedTarget));
                Log.Info("autotest : benne2 " + (host ? "fin (autorite), angle " + BedAngle().ToString("F1") + (testBed2Motor ? ", moteur d'essai" : "")
                         : (err < 3f ? "OK" : "ECHEC") + " : angle " + BedAngle().ToString("F1") + ", cible " + bedTarget.ToString("F1") + ", ecart max en route " + testBed2MaxErr.ToString("F1") + " deg"));
            }
        }

        // Essais : actions d'un etat, avec leurs parametres (champs publics : variables nommees, valeurs, cibles).
        public static string DumpActions(PlayMakerFSM f, string state)
        {
            if (f == null) return "pas d'automate";
            FsmState s = f.Fsm.GetState(state);
            if (s == null) return "pas d'etat " + state;
            var sb = new System.Text.StringBuilder(state + " :");
            FsmStateAction[] acts;
            try { acts = s.Actions; } catch (System.Exception e) { return state + " illisible (" + e.GetType().Name + ")"; }
            foreach (FsmStateAction a in acts)
            {
                if (a == null || a is ModHook) continue;
                sb.Append(" [").Append(a.GetType().Name);
                foreach (FieldInfo fi in a.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
                {
                    object v = fi.GetValue(a);
                    string txt = null;
                    var nv = v as NamedVariable;
                    if (nv != null) txt = nv.UseVariable && !string.IsNullOrEmpty(nv.Name) ? "{" + nv.Name + "}" : nv.ToString();
                    else if (v is FsmOwnerDefault) { var od = (FsmOwnerDefault)v; txt = od.OwnerOption == OwnerDefaultOption.UseOwner ? "proprietaire" : od.GameObject != null && od.GameObject.Value != null ? od.GameObject.Value.name : "?"; }
                    else if (v is FsmEvent) txt = ((FsmEvent)v).Name;
                    else if (v is bool || v is int || v is float || v is string || v is System.Enum) txt = v.ToString();
                    if (txt != null) sb.Append(' ').Append(fi.Name).Append('=').Append(txt);
                }
                sb.Append(']');
            }
            return sb.ToString();
        }

        // ---------------------------------------------------------------- essai boite a gants / loquet
        static PlayMakerFSM testGlove, testLatch;

        static void TestGlovebox()
        {
            float now = Time.realtimeSinceStartup;
            if (testStep == 0 && OtherInGame(15f))
            {
                testStep = 1; testAt = now;
                GameObject gb = Game.FindAny(Config.Get("Test", "BoiteGants", "CORRIS/Assemblies/VINP_Dashboard/Glovebox"));
                if (gb == null) { testStep = 99; Log.Info("autotest : boitegants, boite a gants introuvable"); return; }
                bool was = gb.activeSelf;
                if (!was) gb.SetActive(true);   // comme le tableau de bord monte (la boite s'active, ses automates aussi)
                foreach (PlayMakerFSM f in gb.GetComponentsInChildren<PlayMakerFSM>(true)) if (f.FsmName == "Use") { testGlove = f; break; }
                GameObject lt = Game.FindAny("CORRIS/BODY/HoodLatch/Function");
                testLatch = lt != null ? Game.FsmOn(lt, "Latch") : null;
                SoonScan();
                Log.Info("autotest : boitegants, boite a gants " + (was ? "deja active" : "activee ici") + " ; " + GloveState());
            }
            if (!Session.IsHost || testStep < 1 || testStep > 4) return;
            if (testStep == 1 && now - testAt > 5f) { testStep = 2; Log.Info("autotest : boitegants, " + TestClick(testGlove, GloveOpen() ? "Mouse over 2" : "Mouse over 1", "FINISHED")); }
            if (testStep == 2 && now - testAt > 9f) { testStep = 3; Log.Info("autotest : boitegants, " + TestClick(testLatch, "Mouse over 2", "PROCEED")); }
            if (testStep == 3 && now - testAt > 15f) { testStep = 4; Log.Info("autotest : boitegants, " + TestClick(testGlove, GloveOpen() ? "Mouse over 2" : "Mouse over 1", "FINISHED")); }
            if (testStep == 4 && now - testAt > 19f) { testStep = 5; Log.Info("autotest : boitegants, " + TestClick(testLatch, "Mouse over 2", "PROCEED")); }
        }

        static bool GloveOpen()
        {
            FsmBool o = testGlove != null ? testGlove.FsmVariables.FindFsmBool("Open") : null;
            return o != null && o.Value;
        }

        // Comme le clic : l'etat de survol (sans son MousePick, qui le quitterait faute de souris), puis l'evenement du clic.
        static string TestClick(PlayMakerFSM f, string state, string ev)
        {
            if (f == null) return "pas d'automate";
            if (!f.gameObject.activeInHierarchy) return f.gameObject.name + " inactif";
            FsmState s = f.Fsm.GetState(state);
            if (s == null) return "pas d'etat " + state;
            var off = new List<FsmStateAction>();
            foreach (FsmStateAction a in s.Actions) if (a != null && a.Enabled && a.GetType().Name.StartsWith("MousePick")) { a.Enabled = false; off.Add(a); }
            try { Game.SetState(f, state); f.SendEvent(ev); }
            finally { foreach (FsmStateAction a in off) a.Enabled = true; }
            return "clic " + f.gameObject.name + "::" + f.FsmName + " " + state + " -" + ev + "-> " + f.ActiveStateName + " (suivi " + (hooked.Contains(f) ? "oui" : "non") + ")";
        }

        static string GloveState()
        {
            string g = "boite ?";
            if (testGlove != null)
            {
                FsmBool o = testGlove.FsmVariables.FindFsmBool("Open");
                g = "boite " + (testGlove.gameObject.activeInHierarchy ? "active" : "inactive") + " etat " + testGlove.ActiveStateName + " Open " + (o != null ? o.Value.ToString() : "?")
                    + " suivie " + (hooked.Contains(testGlove) ? "oui" : waitingSet.Contains(testGlove) ? "en attente" : "non (" + (Replay.Owner(testGlove) ?? "personne") + ")");
            }
            string l = "loquet ?";
            if (testLatch != null)
            {
                FsmBool o = testLatch.FsmVariables.FindFsmBool("Open");
                FsmFloat r = testLatch.FsmVariables.FindFsmFloat("Rotation");
                l = "loquet etat " + testLatch.ActiveStateName + " Open " + (o != null ? o.Value.ToString() : "?") + " Rotation " + (r != null ? r.Value.ToString("F1") : "?")
                    + " suivi " + (hooked.Contains(testLatch) ? "oui" : "non (" + (Replay.Owner(testLatch) ?? "personne") + ")");
            }
            return g + " ; " + l;
        }

        // ---------------------------------------------------------------- essai taxi
        static bool testTaxiActivated;

        static void TestTaxi()
        {
            float now = Time.realtimeSinceStartup;
            if (testStep == 0 && OtherInGame(15f))
            {
                testStep = 1; testAt = now;
                Transform car = jobCars.Count > 0 ? jobCars[0] : null;
                if (car == null) { testStep = 99; Log.Info("autotest : taxi, pas de taxi sous JOBS"); return; }
                testTaxiActivated = !car.gameObject.activeSelf;
                if (testTaxiActivated) car.gameObject.SetActive(true);
                Log.Info("autotest : taxi, " + (testTaxiActivated ? "active ici" : "deja actif") + " ; " + TaxiState());
            }
            if (testStep == 1 && now - testAt > 6f) { testStep = 2; Log.Info("autotest : taxi, apres activation : " + TaxiState()); }
            if (testStep == 2 && now - testAt > 20f)
            {
                testStep = 3;
                if (testTaxiActivated && jobCars.Count > 0 && jobCars[0] != null) jobCars[0].gameObject.SetActive(false);
                Log.Info("autotest : taxi, " + (testTaxiActivated ? "rendu inactif" : "laisse actif"));
            }
        }

        // Automates du taxi gardes par Jobs (commandes, sauvegardes) : a qui ils sont, accroches ou en attente.
        static string TaxiState()
        {
            Transform car = jobCars.Count > 0 ? jobCars[0] : null;
            if (car == null) return "pas de taxi (releve pas encore fait ?)";
            int kept = 0, controls = 0, mine = 0, other = 0, hookedN = 0, waitingN = 0;
            string firstOther = null;
            foreach (PlayMakerFSM f in car.GetComponentsInChildren<PlayMakerFSM>(true))
            {
                Classified c;
                if (!classified.TryGetValue(f, out c) || c.Path == null) continue;
                kept++;
                if (c.Control) controls++;
                string o = Replay.Owner(f);
                if (o == "quetes") mine++;
                else { other++; if (firstOther == null) firstOther = f.gameObject.name + "::" + f.FsmName + "=" + (o ?? "personne"); }
                if (hooked.Contains(f)) hookedN++; else if (waitingSet.Contains(f)) waitingN++;
            }
            return "taxi " + (car.gameObject.activeInHierarchy ? "actif" : "inactif") + " : " + kept + " automates gardes (" + controls + " commandes), quetes " + mine
                   + ", autres " + other + (firstOther != null ? " (" + firstOther + ")" : "") + ", accroches " + hookedN + ", en attente " + waitingN;
        }
    }
}
