using System.Collections;
using System.Collections.Generic;
using HutongGames.PlayMaker;
using MWCoop.Net;
using UnityEngine;

namespace MWCoop
{
    // Course sur glace a plusieurs pilotes humains (relecture du 15/10, ecart n1 : chacun courait seul, Races ne faisait
    // suivre que les voitures IA). Transpose de Rally.cs, en plus simple : pas d'heures de depart (tout le monde part de la
    // grille), pas de classe.
    // Dans le jeu, la participation est celle d'UN joueur (voiture n 14 = CORRIS, Cars[14] de TrackFunctions) :
    //  - RACES/ICERACE/TrackFunctions/LINEUPS 'Logic' : inscription (REGISTER -> 'State 4' : PlayerRegistered, autocollants,
    //    CORRIS/DB allume, Cars[14] = CORRIS ; sinon Cars[14] = Reserved), listes des manches Qual1-4, Pickup1-2,
    //    Semifinal1-2, Final (numeros de voiture, textes) ;
    //  - RACES/ICERACE/TrackFunctions/RESULTS 'Leaderboard' : essais du vendredi, table LapTimes (numero -> meilleur tour) ;
    //    les 16 tours des IA sont TIRES AU HASARD ('State 7', 80-100 s), differents chez chacun -> grilles differentes ;
    //    le vendredi, la table est triee toutes les 4 s et remplit Qual1-4 (17 voitures au plus : ListLenght) ;
    //  - RACES/ICERACE/TrackFunctions 'Data' : 'Create grid' lit Heats[Reference][voie 1-6] -> Cars[numero] -> son automate
    //    'Throttle' (variable Navigation) -> 'Navigation' StartPositionID = voie ; 'Weekend 2' / 'Weekend 4' (HeatStage+1)
    //    remplissent Semifinal/Pickup/Final avec ResultsNumbers[0..3] ; 'Get pos' : podium par ResultsNumbers[0..2] == "14" ;
    //  - CORRIS/DB/RaceTrigger 'Data' : tours du joueur d'ici (LapTimes["14"]), arrivee d'une manche ('State 2' : son
    //    numero ajoute a ResultsNumbers, son DB a ResultsCars -- la table des resultats Stats/ResultsRace lit ResultsCars).
    // WorldFsms saute LINEUPS (inscription personnelle, une seule CORRIS pour tous) : ce module relie les joueurs :
    //  - inscription : chacun annonce la sienne a l'hote (crochet sur 'State 4'), tous la voient (tableau, message) ;
    //  - numeros : l'hote donne a chaque invite inscrit un numero libre (15-43, hors IA ; garde par semaine dans
    //    glace-hote.ini, par pseudo). Chez chacun, le joueur d'ici reste la voiture 14 du jeu : les numeros de l'hote
    //    sont "canoniques", et chez un invite de numero N les numeros 14 et N sont ECHANGES (Local/Canon) -- meme
    //    grille, memes voies, seules les etiquettes 14/N different ;
    //  - doublures : chez chacun, chaque AUTRE pilote humain a une voiture de paille (MWCoop-Glace-<n> : 'Throttle' avec
    //    Navigation -> 'Navigation' StartPositionID, et DB 'SkinChange' Number/DriverName) mise dans Cars[n] : 'Create
    //    grid' lui reserve sa voie sans erreur, le panneau de la grille montre son numero, la table des resultats son nom ;
    //  - tirage de l'hote : sa table LapTimes (16 IA de son tirage, lui en 14, les invites a leur numero avec leur meilleur
    //    tour d'essai, ou 999.<n> sans tour ; au-dela de 17 voitures les IA les plus lentes sont retirees) est envoyee a
    //    tous ; l'invite la recopie (echange 14/N, sa propre voiture gardee) -> meme grille de qualification partout ;
    //  - manches : chacun releve les arrivees de sa liste ResultsNumbers (10 fois/s) ; un invite annonce la sienne a
    //    l'hote (K_FINISH), l'hote la date a la reception et fusionne IA + humains par instant d'arrivee, reecrit SA liste
    //    (ResultsNumbers/ResultsCars) et envoie l'ensemble (K_GRID : manche de l'hote, numeros, table, 9 listes de
    //    LINEUPS, arrivees) ; l'invite recopie listes et arrivees (echange 14/N), suit la manche de l'hote (HeatStage) et,
    //    si l'hote clot une manche que sa piste attend encore, passe a 'Weekend 2' / 'Weekend 4' : memes Semifinal /
    //    Pickup / Final, meme podium ('Get pos' : sa 14 a sa vraie place) ;
    //  - resultats : chacun annonce son meilleur tour d'essai et son arrivee de manche ; l'hote tient le tableau commun
    //    (places fusionnees), le classe (manche la plus avancee, place, tour) et le renvoie a tous ;
    //  - invite : sa participation est gardee par monde (glace.ini, comme rallye.ini) ; la sauvegarde recue a chaque
    //    connexion contient celle de l'hote, remplacee par la sienne (ou effacee) au chargement.
    // Locations Jokkis (16/10, module Jokkis : voitures de location conduisibles) : un joueur qui loue une voiture court avec
    // le numero de CETTE voiture (p. ex. 33) et sa livree ; l'IA de ce numero ne court pas de la semaine :
    //  - numeros : Jokkis.ChosenNumber(id) = n > 0 remplace la case 15-43 (hote compris : lui n'est alors plus la 14, son
    //    numero voyage dans K_GRID comme celui des invites, identifiant 0) ; chez le locataire pas d'echange 14/N (son
    //    numero est reel) ; la table de l'hote met son tour a la cle n (le tirage de l'IA n est garde et rendu a la
    //    liberation, Jokkis.Changed) ; la 14 est retiree de la table si l'hote loue ;
    //  - voiture : Cars[n] = la racine de la location (Jokkis.CarOf) chez tous, avec les memes automates legers que les
    //    doublures si elle n'en a pas ('Throttle'.Navigation -> 'Navigation'.StartPositionID, DB 'SkinChange') ; la
    //    voiture IA du jeu sort aussi d'AllCars (essais du vendredi) ; le RaceTrigger du jeu sous son DB, s'il y en a un,
    //    est coupe (comptage ci-dessous) ; le tout rendu a la liberation ;
    //  - grille : 'Create grid' pose StartPositionID sur la location comme sur une IA ; le locataire (autorite de sa
    //    voiture) la teleporte alors sur sa voie (Gridding[voie] : TrackFunctions/StartPos<voie>, cap vers DragPoint1),
    //    les autres la voient par VehicleSync ;
    //  - tours : le RaceTrigger du jeu (CORRIS/DB) ne compte que la 14 ; pour une location le module suit la position de
    //    la voiture et les boites des lignes de la piste (Checkpoint1, Checkpoint2, StartFinishLaps/StartFinishTime :
    //    MeshCollider, evenements LAP/TIME du jeu aux colliders 'StagingWheel') : un tour = Checkpoint1 puis Checkpoint2
    //    puis la ligne ; vendredi (StartFinishTime allume) meilleur tour -> LapTimes[n] ; en manche (StartFinishLaps)
    //    RaceDistance tours -> arrivee : n ajoute a ResultsNumbers, DB a ResultsCars et FINISHRACE diffuse, comme
    //    RaceTrigger 'State 2' ; l'hote fusionne comme pour la 14 (K_FINISH) ;
    //  - inscription : LINEUPS 'State 4' (autocollants, CORRIS/DB, Cars[14] = CORRIS) est reduit a PlayerRegistered pour
    //    un locataire ; podium ('Get pos' compare a "14") : son numero est mis en 14 le temps de l'etat.
    // [Coop] CourseGlace=0 coupe tout le module (vrai jeu : si cela se comporte mal).
    public static class IceRace
    {
        // (Msg.Race, rangs >= 20 : Races.OnMessage les passe ici ; 10-14 : Rally)
        //  20 K_HUMAN  invite -> hote : participation (pseudo, inscrit, tour, manche+1, place)         (0.96.0)
        //  21 K_BOARD  hote -> tous : semaine, tours de la table de l'hote (sans 14), humains classes   (0.96.0)
        //  22 K_GRID   hote -> tous : semaine, HeatStage+1, numeros (id, n), table complete (14 compris), les 9 listes
        //              de LINEUPS, arrivees de la manche (ResultsNumbers), indicateur piste en course        (16/10)
        //  23 K_FINISH invite -> hote : semaine, manche+1 -- "ma voiture vient de finir la manche"          (16/10)
        const int K_HUMAN = 20, K_BOARD = 21, K_GRID = 22, K_FINISH = 23;
        const string ME = "14";                 // numero de la voiture du joueur (CORRIS/DB 'SkinChange' Number)
        const int MAX_CARS = 17;                // 'Leaderboard' ListLenght : voitures gardees dans Qual1-4
        static readonly string[] HeatNames = { "Q1", "Q2", "Q3", "Q4", "P1", "P2", "SF1", "SF2", "F" };   // HeatStage 0-8
        static readonly string[] HeatLists = { "Qual1", "Qual2", "Qual3", "Qual4", "Pickup1", "Pickup2", "Semifinal1", "Semifinal2", "Final" };
        static readonly int[] AiNumbers = { 10, 11, 12, 13, 20, 21, 26, 28, 31, 32, 33, 37, 40, 41, 42, 43 };   // (si la table manque)
        // TrackFunctions 'Data' en course (de la grille aux resultats) : HeatStage n'y est pas recale sur l'hote.
        static readonly string[] RacingStates = { "Create grid", "Create grid 2", "Wait command", "Wait command 2", "Start race", "Start race 2", "Wait results", "Wait results 2", "State 14", "State 15" };

        class Human { public int Id; public string Name = ""; public bool Registered; public float Lap; public int Heat = -1, Pos; }
        class Arrival { public string Num; public float At; public GameObject Db; }

        static PlayMakerFSM lineups, board, track;
        static Hashtable lapTimes;
        static ArrayList resultsNumbers, resultsCars, carsList;
        static readonly Dictionary<string, ArrayList> heatLists = new Dictionary<string, ArrayList>();
        static bool enabled, hookedLineups, hookedBoard, restored, carsWarned, hostLapsFull;
        static float readyAt = -1, nextLook, nextReport, nextStore, nextBoard, nextApply, nextGrid, nextGridFull, nextPoll, nextTable, nextTick, nextSkipLog;
        static string lastReport = "", lastRank = "", gridSig = "", lastGridLog = "";
        static int boardWeek = -1, localWeek = -1, lastHeat = -1, lastPos;
        static int myNum;              // numero canonique du joueur d'ici (0 : aucun ; l'hote est toujours 14)
        static int hostHeat = -1;      // invite : HeatStage de la piste de l'hote (K_GRID)
        static bool hostRacing;        // invite : piste de l'hote en course
        static readonly Dictionary<int, Human> humans = new Dictionary<int, Human>();
        static readonly Dictionary<int, int> numbers = new Dictionary<int, int>();            // id -> numero canonique (jamais l'hote)
        static readonly Dictionary<string, int> numbersByName = new Dictionary<string, int>(); // hote : pseudo -> numero (semaine)
        static readonly Dictionary<string, float> hostLaps = new Dictionary<string, float>();  // table de l'hote : numero -> tour (s)
        static readonly Dictionary<string, string[]> hostLists = new Dictionary<string, string[]>();
        static string[] hostResults;
        static readonly Dictionary<int, GameObject> standIns = new Dictionary<int, GameObject>();   // numero local -> doublure
        static readonly List<Arrival> arrivals = new List<Arrival>();
        static readonly HashSet<string> seen = new HashSet<string>();
        static int arrivalsHeat = -1, arrivalsWeek = -1;
        static readonly System.Globalization.CultureInfo Inv = System.Globalization.CultureInfo.InvariantCulture;
        // Locations Jokkis (16/10)
        static bool myRental;                       // le joueur d'ici court dans une location : numero reel, pas d'echange 14/N
        static bool hookedTrack, jokkisHooked, rentalsDirty, placeAnyState, linesWarned, slotWarned, testNoBroadcast;
        static float nextRentals, nextLineLog;
        static readonly Dictionary<string, float> aiSaved = new Dictionary<string, float>();       // hote : tour IA remplace par un locataire
        static readonly Dictionary<int, GameObject> carsOrig = new Dictionary<int, GameObject>();  // Cars[n] du jeu remplace par une location
        static readonly Dictionary<int, GameObject> aiHidden = new Dictionary<int, GameObject>();  // voiture IA sortie d'AllCars
        static ArrayList allCars;
        static readonly List<FsmStateAction> muted = new List<FsmStateAction>();   // LINEUPS 'State 4' : actions coupees pour un locataire
        static int mutedFrame = -1, prizeIdx = -1, prizeFrame = -1;
        // location d'ici : lignes de la piste (0 Checkpoint1, 1 Checkpoint2, 2 depart/arrivee), tours, arrivee
        static Bounds[] lines; static readonly bool[] inside = new bool[3];
        static GameObject lineLaps, lineTime;
        static Vector3 lastCarPos; static bool hasLast, cp1, cp2, timing, finished;
        static float lapStart, bestLap;
        static int laps; static string lapsKey = "", placedKey = "";

        public static void OnLevelLoaded()
        {
            lineups = board = track = null; lapTimes = null; resultsNumbers = resultsCars = carsList = null; heatLists.Clear();
            hookedLineups = hookedBoard = restored = carsWarned = hostLapsFull = false; lastReport = lastRank = gridSig = lastGridLog = "";
            humans.Clear(); numbers.Clear(); numbersByName.Clear(); hostLaps.Clear(); hostLists.Clear(); hostResults = null; standIns.Clear();
            arrivals.Clear(); seen.Clear(); arrivalsHeat = arrivalsWeek = -1;
            boardWeek = localWeek = -1; lastHeat = -1; lastPos = 0; myNum = 0; hostHeat = -1; hostRacing = false;
            myRental = hookedTrack = rentalsDirty = placeAnyState = linesWarned = slotWarned = testNoBroadcast = false;
            aiSaved.Clear(); carsOrig.Clear(); aiHidden.Clear(); allCars = null; muted.Clear(); mutedFrame = prizeIdx = prizeFrame = -1;
            lines = null; lineLaps = lineTime = null; hasLast = cp1 = cp2 = timing = finished = false; bestLap = 0f; laps = 0; lapsKey = placedKey = "";
            testStep = 0;
            if (!jokkisHooked) { jokkisHooked = true; try { Jokkis.Changed += OnJokkisChanged; } catch (System.Exception e) { Log.Warn("glace : module Jokkis : " + e.Message); } }
            enabled = Config.GetInt("Coop", "CourseGlace", 1) != 0;
            readyAt = PlayerSync.InGame && enabled ? Time.realtimeSinceStartup + 10f : -1;
            if (PlayerSync.InGame && !enabled) Log.Info("glace : module coupe ([Coop] CourseGlace=0)");
        }

        class Hook : ModHook
        {
            public override string Module { get { return "courses"; } }
            public int Kind;   // 0 tirage des essais fait (apres 'Set result'), 2 inscription, 3 podium ('Get pos')
            public override void OnEnter()
            {
                try
                {
                    if (Kind == 2) { if (RentalNumber(Session.LocalId) > 0) MuteState(State, this); OnRegistered(); }
                    else if (Kind == 3) PrizeSwap();
                    else { ApplyHostLaps(true); FixMyLap(); nextTable = 0; }
                }
                catch (System.Exception e) { Replay.HookError(e); }
                Finish();
            }
        }

        // ---------------------------------------------------------------- releve
        // ICERACE est eteint les semaines de rallye (Races 'SwapDates') : ses automates ne tournent pas, on repasse toutes
        // les 3 s. Les crochets ne sont poses que sur un automate demarre (etat actif connu), comme Rally.
        static bool Find()
        {
            if (lineups != null && board != null) return true;
            if (Time.realtimeSinceStartup < nextLook) return false;
            nextLook = Time.realtimeSinceStartup + 3f;
            if (lineups == null)
            {
                GameObject go = Game.FindAny("RACES/ICERACE/TrackFunctions/LINEUPS");
                PlayMakerFSM l = go != null ? Game.FsmOn(go, "Logic") : null;
                if (l == null || string.IsNullOrEmpty(l.ActiveStateName)) return false;
                GameObject tf = Game.FindAny("RACES/ICERACE/TrackFunctions");
                track = tf != null ? Game.FsmOn(tf, "Data") : null;
                if (tf != null) foreach (PlayMakerArrayListProxy p in tf.GetComponents<PlayMakerArrayListProxy>()) { if (p.referenceName == "Cars") carsList = p.arrayList; if (p.referenceName == "AllCars") allCars = p.arrayList; }
                if (tf != null) FindLines(tf);
                if (track != null && !hookedTrack) { hookedTrack = true; Inject(track, "Get pos", 3); }
                foreach (PlayMakerArrayListProxy p in go.GetComponents<PlayMakerArrayListProxy>()) if (System.Array.IndexOf(HeatLists, p.referenceName) >= 0) heatLists[p.referenceName] = p.arrayList;
                if (track == null) Log.Warn("course glace : automate RACES/ICERACE/TrackFunctions 'Data' introuvable : manches pas suivies");
                if (heatLists.Count < HeatLists.Length) Log.Warn("course glace : listes de manches de LINEUPS incompletes (" + heatLists.Count + "/" + HeatLists.Length + ")");
                lineups = l;
                if (!hookedLineups) { hookedLineups = true; Inject(lineups, "State 4", 2); }
                Log.Info("glace : inscription suivie (" + lineups.ActiveStateName + ", inscrit " + Registered() + ", Cars " + (carsList != null ? carsList.Count.ToString() : "?") + ")");
            }
            if (board == null)
            {
                GameObject go = Game.FindAny("RACES/ICERACE/TrackFunctions/RESULTS");
                PlayMakerFSM b = go != null ? Game.FsmOn(go, "Leaderboard") : null;
                if (b == null || string.IsNullOrEmpty(b.ActiveStateName)) return false;
                foreach (PlayMakerHashTableProxy p in go.GetComponents<PlayMakerHashTableProxy>()) if (p.referenceName == "LapTimes") lapTimes = p.hashTable;
                foreach (PlayMakerArrayListProxy p in go.GetComponents<PlayMakerArrayListProxy>())
                {
                    if (p.referenceName == "ResultsNumbers") resultsNumbers = p.arrayList;
                    if (p.referenceName == "ResultsCars") resultsCars = p.arrayList;
                }
                if (lapTimes == null || resultsNumbers == null) { Log.Warn("glace : table LapTimes ou liste ResultsNumbers absente de RESULTS"); nextLook = Time.realtimeSinceStartup + 30f; return false; }
                if (resultsCars == null) Log.Warn("course glace : liste ResultsCars absente de RESULTS : table des resultats sans les noms");
                board = b;
                // Apres le tirage du jeu (et non avant : 'Set result' ecrirait par-dessus les tours de l'hote).
                if (!hookedBoard) { hookedBoard = true; FsmState s = board.Fsm.GetState("Set result"); if (s != null) FsmHooks.Append(s, new Hook { Kind = 0 }); else Log.Warn("glace : etat 'Set result' absent"); }
                Log.Info("glace : essais suivis (" + board.ActiveStateName + ", " + lapTimes.Count + " tours, mon tour " + MyLap().ToString("F1") + ")");
            }
            return lineups != null && board != null;
        }

        static void Inject(PlayMakerFSM f, string state, int kind)
        {
            FsmState s = f.Fsm.GetState(state);
            if (s == null) { Log.Warn("glace : etat " + state + " absent de " + f.FsmName); return; }
            FsmHooks.Prepend(s, new Hook { Kind = kind });
        }

        static bool Registered() { FsmBool b = lineups != null ? lineups.FsmVariables.FindFsmBool("PlayerRegistered") : null; return b != null && b.Value; }
        static int TrackHeat() { FsmInt h = track != null ? track.FsmVariables.FindFsmInt("HeatStage") : null; return h != null ? h.Value : -1; }
        static string HeatName(int h) { return h >= 0 && h < HeatNames.Length ? HeatNames[h] : "?"; }
        static bool IsRacing(string state) { return System.Array.IndexOf(RacingStates, state) >= 0; }

        // Cle de la voiture du joueur d'ici dans la table LapTimes et les listes : "14" (CORRIS), ou son numero s'il loue.
        static string MyKey() { return myRental && myNum != 0 ? myNum.ToString() : ME; }

        // Meilleur tour d'essai du joueur d'ici (0 : aucun ; 999 est la valeur du jeu pour "pas de tour").
        static float MyLap()
        {
            string k = MyKey();
            if (lapTimes == null || !lapTimes.ContainsKey(k)) return 0f;
            float v = ToF(lapTimes[k]);
            return v > 0f && v < 900f ? v : 0f;
        }

        static float ToF(object o)
        {
            if (o is float) return (float)o;
            if (o is double) return (float)(double)o;
            if (o is int) return (int)o;
            float f; return o != null && float.TryParse(o.ToString(), System.Globalization.NumberStyles.Float, Inv, out f) ? f : 0f;
        }

        static string World()
        {
            FsmFloat pid = FsmVariables.GlobalVariables.FindFsmFloat("PlayerID");
            return pid != null ? pid.Value.ToString("F0") : "";
        }

        static int Week() { FsmInt wk = FsmVariables.GlobalVariables.FindFsmInt("GlobalWeeksPassed"); return wk != null ? wk.Value : 0; }

        // ---------------------------------------------------------------- numeros : canonique (hote) <-> local (ici)
        // L'hote est 14 chez lui et pour tous ; un invite de numero N est 14 chez lui : chez lui, 14 et N sont echanges.
        // Un locataire (location Jokkis) court avec son vrai numero partout : rien d'echange.
        static int LocalNum(int canon) { return myNum == 0 || myRental ? canon : canon == 14 ? myNum : canon == myNum ? 14 : canon; }
        static string Local(string canon) { int n; return int.TryParse(canon, out n) ? LocalNum(n).ToString() : canon; }
        static string Canon(string local) { return Local(local); }   // (echange : la meme fonction)

        // Joueur dont c'est le numero canonique (-1 : voiture IA ou inconnu). La 14 est l'hote, sauf s'il loue (numbers[0]).
        static int IdOfNumber(string canon)
        {
            int n; if (!int.TryParse(canon, out n)) return -1;
            foreach (KeyValuePair<int, int> kv in numbers) if (kv.Value == n) return kv.Key;
            return n == 14 && !numbers.ContainsKey(0) ? 0 : -1;
        }

        // ---------------------------------------------------------------- locations Jokkis
        // Numero et voiture loues par un joueur (0 / null : aucun). [Test] GlaceJokkis=n : l'invite 1 est repute louer la
        // voiture n (racine factice MWCoop-Jokkis-<n>) -- essai glace4 sans le module Jokkis.
        static int RentalNumber(int id)
        {
            int t = Config.Testing && id == 1 ? Config.GetInt("Test", "GlaceJokkis", 0) : 0;
            if (t > 0) return t;
            try { return Jokkis.ChosenNumber(id); } catch { return 0; }
        }

        static Transform RentalCar(int id)
        {
            int t = Config.Testing && id == 1 ? Config.GetInt("Test", "GlaceJokkis", 0) : 0;
            if (t > 0)
            {
                GameObject go = GameObject.Find("MWCoop-Jokkis-" + t);
                if (go == null) { go = new GameObject("MWCoop-Jokkis-" + t); go.transform.position = new Vector3(-1150f, -4.5f, 900f); go.AddComponent<Rigidbody>().isKinematic = true; }
                return go.transform;
            }
            try { return Jokkis.CarOf(id); } catch { return null; }
        }

        static string RentalModel(int n)
        {
            if (Config.Testing && n == Config.GetInt("Test", "GlaceJokkis", 0)) return "Fittan";
            try { foreach (Jokkis.Def d in Jokkis.All()) if (d.Number == n) return d.Model ?? "-"; } catch { }
            return "-";
        }

        static void OnJokkisChanged() { rentalsDirty = true; }

        // Numeros des locataires (hote : numbers, identifiant 0 pour lui-meme) et situation d'ici (myNum/myRental) ; a
        // chaque changement du module Jokkis et toutes les 2 s. Hote : un locataire garde son numero de voiture ; une
        // location rendue en cours de semaine redonne une case 15-43 (AssignNumber), l'IA revient (HostSyncTable).
        static void RefreshRentals()
        {
            if (Session.IsHost)
            {
                var ids = new List<int>(humans.Keys);
                foreach (int id in ids)
                {
                    Human h = humans[id];
                    int rn = h.Registered ? RentalNumber(id) : 0, cur;
                    numbers.TryGetValue(id, out cur);
                    if (rn > 0 && cur != rn)
                    {
                        numbers[id] = rn;
                        Log.Info("glace : " + h.Name + " court avec sa location n " + rn + (cur != 0 ? " (etait " + cur + ")" : ""));
                        Hud.Toast(h.Name + Lang.T(" : voiture n° ", ": car no. ") + rn);
                        nextTable = 0; nextGrid = 0; lastGridLog = "";
                    }
                    else if (rn == 0 && cur != 0 && !numbersByName.ContainsValue(cur))
                    {
                        // location rendue : l'IA reprend son numero, le joueur une case libre (ou aucune : l'hote en CORRIS)
                        numbers.Remove(id);
                        Log.Info("glace : " + h.Name + " a rendu la location n " + cur);
                        if (id != 0 && h.Registered) AssignNumber(h);
                        nextTable = 0; nextGrid = 0; lastGridLog = "";
                    }
                }
                int mine; numbers.TryGetValue(0, out mine);
                if (mine != myNum) { myNum = mine; lastGridLog = ""; }
            }
            bool r = myNum != 0 && RentalNumber(Session.LocalId) == myNum;
            if (r == myRental) return;
            myRental = r;
            lastReport = ""; lastGridLog = ""; nextTable = 0;
            Log.Info("glace : " + (myRental ? "je cours avec ma location n " + myNum + " (numero reel, rien d'echange)" : "plus de location : " + (myNum != 0 ? "14 ici, " + myNum + " chez l'hote" : "voiture 14")));
        }

        // LINEUPS 'State 4' pour un locataire : seul PlayerRegistered compte (pas d'autocollants ni de CORRIS/DB, Cars[14]
        // reste 'Reserved') ; les autres actions de l'etat sont coupees le temps de l'etat, rendues a l'image suivante.
        static void MuteState(FsmState s, FsmStateAction hook)
        {
            if (s == null) return;
            FsmBool reg = lineups != null ? lineups.FsmVariables.FindFsmBool("PlayerRegistered") : null;
            if (reg != null) reg.Value = true;
            foreach (FsmStateAction a in s.Actions) if (a != null && a != hook && a.Enabled) { a.Enabled = false; muted.Add(a); }
            mutedFrame = Time.frameCount;
            Log.Info("glace : inscription en location : " + s.Name + " reduit a PlayerRegistered (" + muted.Count + " actions sautees)");
        }

        static void Unmute()
        {
            if (muted.Count > 0 && Time.frameCount > mutedFrame) { foreach (FsmStateAction a in muted) a.Enabled = true; muted.Clear(); }
            if (prizeIdx >= 0 && Time.frameCount > prizeFrame)
            {
                if (resultsNumbers != null && prizeIdx < resultsNumbers.Count && resultsNumbers[prizeIdx] != null && resultsNumbers[prizeIdx].ToString() == ME) resultsNumbers[prizeIdx] = MyKey();
                prizeIdx = -1;
            }
        }

        // 'Get pos' (podium, prix) compare ResultsNumbers[0..2] a "14" : pour un locataire, son numero y est mis en 14 le
        // temps de l'etat (remis a l'image suivante ; RESULTSPublic et Final ont deja ete recopies par 'State 44').
        static void PrizeSwap()
        {
            if (!myRental || resultsNumbers == null) return;
            string k = MyKey();
            for (int i = 0; i < 3 && i < resultsNumbers.Count; i++)
                if (resultsNumbers[i] != null && resultsNumbers[i].ToString() == k)
                {
                    resultsNumbers[i] = ME; prizeIdx = i; prizeFrame = Time.frameCount;
                    Log.Info("glace : podium : ma location n " + k + " (" + (i + 1) + "e) comptee comme la 14 pour le prix");
                    return;
                }
        }

        static string NameOf(int id)
        {
            Human h; PlayerInfo pi;
            if (humans.TryGetValue(id, out h) && h.Name.Length > 0) return h.Name;
            if (Session.Players.TryGetValue(id, out pi)) return pi.Name;
            return "#" + id;
        }

        // Etiquette d'un numero LOCAL : "15 (Bob)" pour un humain, "37" pour une IA.
        static string Label(string local)
        {
            int id = IdOfNumber(Canon(local));
            return id < 0 ? local : local + " (" + (id == Session.LocalId ? Lang.T("moi", "me") : NameOf(id)) + ")";
        }

        // ---------------------------------------------------------------- mise a jour
        public static void Update()
        {
            if (!enabled || !Session.Active || !PlayerSync.InGame || readyAt < 0 || Time.realtimeSinceStartup < readyAt) return;
            if (!Find()) return;
            string wld = World();
            if (wld.Length == 0 || wld == "0") return;   // identifiant de la partie pas encore charge : cles par monde fausses
            float now = Time.realtimeSinceStartup;
            int w = Week();
            if (w != localWeek)
            {
                // Nouvelle semaine : la derniere arrivee de manche ne compte plus (le jeu remet les listes a zero).
                if (localWeek >= 0) { lastHeat = -1; lastPos = 0; lastReport = ""; }
                localWeek = w;
                bestLap = 0f; timing = false; lapsKey = placedKey = "";   // (location : tours de la semaine passee)
            }
            if (!Session.IsHost && !restored)
            {
                // Apres le chargement (State 1 -> Exists -> Load -> Check status -> State 4 / Reset decals -> State 2) :
                // sinon Load ecraserait la participation d'ici par celle de la sauvegarde de l'hote.
                if (lineups.ActiveStateName != "State 2") return;
                restored = true; RestoreGuest();
            }
            Unmute();
            if (rentalsDirty || now >= nextRentals) { rentalsDirty = false; nextRentals = now + 2f; RefreshRentals(); }
            if (myRental) { TrackRental(); PlaceRental(); }
            if (now >= nextPoll) { nextPoll = now + 0.1f; PollArrivals(); }
            if (Session.IsHost)
            {
                HostWeek(w);
                if (now >= nextTable) { nextTable = now + 2f; HostSyncTable(); EnsureStandIns(); }
                if (now >= nextBoard) { nextBoard = now + 20f; SendBoard(); }   // (arrivants)
                if (now >= nextGrid) { nextGrid = now + 1f; SendGrid(false); }
            }
            else if (now >= nextApply) { nextApply = now + 2f; ApplyHostLaps(false); EnsureStandIns(); }
            if (now >= nextTick) { nextTick = now + 1f; if (!Session.IsHost) FollowHostHeat(); LogGrid(); }
            if (now >= nextReport) { nextReport = now + 5f; Report(false); }
            if (now >= nextStore) { nextStore = now + 10f; Store(); }
        }

        // Arrivee de manche du joueur d'ici : son numero dans ResultsNumbers (ordre d'arrivee, remis a zero a la manche
        // suivante) ; la derniere arrivee est gardee (lastHeat, lastPos) jusqu'a la semaine suivante.
        static void ReadFinish()
        {
            if (resultsNumbers == null) return;
            for (int i = 0; i < resultsNumbers.Count; i++)
            {
                object o = resultsNumbers[i];
                if (o == null || o.ToString() != MyKey()) continue;
                int heat = TrackHeat();
                if (lastPos != i + 1 || lastHeat != heat) Log.Info("glace : arrive " + (i + 1) + "e de la manche " + HeatName(heat) + " ici");
                lastPos = i + 1; lastHeat = heat;
                return;
            }
        }

        // Notre participation, annoncee a l'hote quand elle change (et toutes les 30 s si inscrit : un hote qui revient).
        static void Report(bool force)
        {
            if (lineups == null) return;
            ReadFinish();
            bool reg = Registered();
            float lap = MyLap();
            string sig = reg + "|" + lap.ToString("R") + "|" + lastHeat + "|" + lastPos;
            if (!force && sig == lastReport && Time.realtimeSinceStartup % 30f > 5f) return;
            if (!reg && lap == 0f && lastPos == 0 && lastReport.Length == 0) return;
            lastReport = sig;
            var h = new Human { Id = Session.LocalId, Name = Session.Me != null ? Session.Me.Name : "?", Registered = reg, Lap = lap, Heat = lastHeat, Pos = lastPos };
            if (Session.IsHost) { SetHuman(h); return; }
            Session.SendToHost(new NetWriter(Msg.Race).U8(K_HUMAN).Str(h.Name).Bool(h.Registered).F32(h.Lap).U8(h.Heat + 1).U8(h.Pos), true);
        }

        static void SetHuman(Human h)
        {
            Human old;
            if (humans.TryGetValue(h.Id, out old) && old.Registered == h.Registered && old.Lap == h.Lap && old.Heat == h.Heat && old.Pos == h.Pos) return;
            bool newReg = h.Registered && (old == null || !old.Registered);
            humans[h.Id] = h;
            Log.Info("glace : " + h.Name + (h.Registered ? " inscrit" : "") + ", tour " + h.Lap.ToString("F1") + (h.Pos > 0 ? ", " + h.Pos + "e en " + HeatName(h.Heat) : ""));
            if (newReg && h.Id != Session.LocalId) Hud.Toast(h.Name + Lang.T(" s'inscrit à la course sur glace", " registered for the ice race"));
            if (Session.IsHost && h.Registered && h.Id != 0 && !numbers.ContainsKey(h.Id)) AssignNumber(h);
            if (Session.IsHost) RefreshRentals();   // (location de l'hote lui-meme, ou d'un inscrit)
            nextTable = 0;   // (table de l'hote : son tour, son numero)
            ShowRank();
            SendBoard();
        }

        // ---------------------------------------------------------------- hote : semaine, numeros, tirage, tableau
        static void HostWeek(int w)
        {
            if (w == boardWeek) return;
            // Numeros de la semaine passee : sortis de la table (sinon ils y resteraient en voitures fantomes) ; une location
            // rend son tirage a l'IA (le jeu retire tout le vendredi suivant de toute facon).
            if (lapTimes != null)
                foreach (int n in numbers.Values)
                {
                    string k = n.ToString(); float ai;
                    if (n == 0) continue;
                    if (aiSaved.TryGetValue(k, out ai)) lapTimes[k] = ai;
                    else if (lapTimes.ContainsKey(k)) lapTimes.Remove(k);
                }
            aiSaved.Clear();
            boardWeek = w;
            humans.Clear(); numbers.Clear(); numbersByName.Clear();
            lastReport = "";
            LoadHost();
            Log.Info("glace : semaine " + w + ", tirage des essais de l'hote donne a tous" + (numbersByName.Count > 0 ? " (" + numbersByName.Count + " numeros gardes)" : ""));
            SendBoard();
        }

        static string HostPath { get { return System.IO.Path.Combine(Log.DataDir, "glace-hote.ini"); } }

        // Numeros gardes par monde et par semaine (glace-hote.ini : monde|semaine=pseudo:numero;...) : un hote qui relance
        // son jeu en plein week-end redonne le meme numero a chacun (ses listes de manches, sauvegardees, le contiennent).
        static void LoadHost()
        {
            string key = World() + "|" + boardWeek + "=";
            if (!System.IO.File.Exists(HostPath)) return;
            foreach (string l in System.IO.File.ReadAllLines(HostPath))
            {
                if (!l.StartsWith(key)) continue;
                foreach (string part in l.Substring(key.Length).Split(';'))
                {
                    int c = part.LastIndexOf(':'); int n;
                    if (c > 0 && int.TryParse(part.Substring(c + 1), out n)) numbersByName[part.Substring(0, c)] = n;
                }
            }
        }

        static void SaveHost()
        {
            string key = World() + "|" + boardWeek + "=";
            var sb = new System.Text.StringBuilder(key);
            bool first = true;
            foreach (KeyValuePair<string, int> kv in numbersByName) { if (!first) sb.Append(';'); first = false; sb.Append(kv.Key).Append(':').Append(kv.Value); }
            var lines = new List<string>();
            if (System.IO.File.Exists(HostPath)) foreach (string l in System.IO.File.ReadAllLines(HostPath)) if (!l.StartsWith(key)) lines.Add(l);
            lines.Add(sb.ToString());
            try { System.IO.File.WriteAllLines(HostPath, lines.ToArray()); } catch { }
        }

        static string Clean(string s) { return s.Replace(";", "").Replace(":", "").Replace("=", "").Replace("|", ""); }

        // Numero libre pour un invite inscrit : 15-43 hors des IA (cles de la table) et des numeros deja donnes. Un
        // locataire (Jokkis) garde le numero de sa voiture.
        static void AssignNumber(Human h)
        {
            string who = Clean(h.Name);
            int n = RentalNumber(h.Id);
            if (n > 0)
            {
                numbers[h.Id] = n;
                Log.Info("glace : " + h.Name + " court avec sa location n " + n);
                Hud.Toast(h.Name + Lang.T(" : voiture n° ", ": car no. ") + n);
                nextGrid = 0;
                return;
            }
            if (!numbersByName.TryGetValue(who, out n))
            {
                var used = new HashSet<int>(numbersByName.Values);
                var ai = new HashSet<int>();
                if (lapTimes != null) foreach (object k in lapTimes.Keys) { int x; if (int.TryParse(k.ToString(), out x)) ai.Add(x); }
                else foreach (int x in AiNumbers) ai.Add(x);
                n = 0;
                for (int c = 15; c <= 43 && n == 0; c++) if (!used.Contains(c) && !ai.Contains(c)) n = c;
                if (n == 0) { Log.Warn("glace : plus de numero libre pour " + h.Name); return; }
                numbersByName[who] = n;
                SaveHost();
            }
            numbers[h.Id] = n;
            Log.Info("glace : " + h.Name + " court avec le numero " + n);
            Hud.Toast(h.Name + Lang.T(" : voiture n° ", ": car no. ") + n);
            nextGrid = 0;
        }

        static bool HasValue(Hashtable t, float v, string except)
        {
            foreach (DictionaryEntry e in t) if (e.Key.ToString() != except && Mathf.Abs(ToF(e.Value) - v) < 0.0005f) return true;
            return false;
        }

        // Table de l'hote = la table canonique : chaque invite inscrit y est a son numero (son tour d'essai, ou 999.<n>
        // sans tour ; valeurs toutes distinctes : 'Leaderboard' retrouve le numero PAR la valeur) ; au-dela de 17
        // voitures, les IA les plus lentes cedent leur place (le jeu ne garde que 17 tours dans Qual1-4).
        // Locations : le locataire prend la cle de sa voiture (le tirage de l'IA est garde dans aiSaved et rendu quand la
        // location est rendue) ; l'hote qui loue n'est plus la 14, retiree de la table.
        static void HostSyncTable()
        {
            if (!Session.IsHost || lapTimes == null) return;
            var wanted = new Dictionary<string, float>();
            foreach (Human h in humans.Values)
            {
                int n;
                if (!h.Registered || !numbers.TryGetValue(h.Id, out n) || n == 0) continue;   // (l'hote n'y est que s'il loue : numbers[0])
                wanted[n.ToString()] = h.Lap > 0f ? h.Lap : 999f + n * 0.01f;
            }
            int changed = 0;
            var gone = new List<string>();
            foreach (object k in lapTimes.Keys) { string key = k.ToString(); if (key != ME && IdOfNumber(key) >= 0 && !wanted.ContainsKey(key)) gone.Add(key); }
            foreach (string key in gone) { lapTimes.Remove(key); changed++; }
            var back = new List<string>();
            foreach (KeyValuePair<string, float> kv in aiSaved) if (!wanted.ContainsKey(kv.Key)) back.Add(kv.Key);
            foreach (string key in back) { lapTimes[key] = aiSaved[key]; aiSaved.Remove(key); changed++; Log.Info("glace : voiture IA n " + key + " de retour aux essais (location rendue)"); }
            if (myRental && lapTimes.ContainsKey(ME)) { lapTimes.Remove(ME); changed++; Log.Info("glace : la 14 (CORRIS) retiree de la table : l'hote court en location n " + myNum); }
            foreach (KeyValuePair<string, float> kv in wanted)
            {
                float v = kv.Value;
                if (lapTimes.ContainsKey(kv.Key) && Mathf.Abs(ToF(lapTimes[kv.Key]) - v) < 0.05f) continue;
                if (lapTimes.ContainsKey(kv.Key) && !aiSaved.ContainsKey(kv.Key) && RentalNumber(IdOfNumber(kv.Key)) > 0 && ToF(lapTimes[kv.Key]) < 900f)
                {
                    aiSaved[kv.Key] = ToF(lapTimes[kv.Key]);
                    Log.Info("glace : voiture IA n " + kv.Key + " retiree des essais de la semaine (voiture louee par " + NameOf(IdOfNumber(kv.Key)) + ")");
                }
                while (HasValue(lapTimes, v, kv.Key)) v += 0.01f;
                lapTimes[kv.Key] = v; changed++;
            }
            while (lapTimes.Count > MAX_CARS)
            {
                string slowest = null; float worst = -1f;
                foreach (object k in lapTimes.Keys)
                {
                    string key = k.ToString();
                    if (key == ME || IdOfNumber(key) >= 0) continue;
                    float v = ToF(lapTimes[key]);
                    if (v > worst) { worst = v; slowest = key; }
                }
                if (slowest == null) break;
                lapTimes.Remove(slowest); changed++;
                Log.Info("glace : voiture IA n " + slowest + " retiree des essais (place aux pilotes humains, " + MAX_CARS + " voitures au plus)");
            }
            if (changed > 0) Log.Info("glace : table des essais de l'hote : " + wanted.Count + " invite(s) inscrit(s), " + lapTimes.Count + " voitures");
        }

        static void SendBoard()
        {
            if (!Session.IsHost || Session.RemoteCount == 0) return;
            Session.Broadcast(BoardMsg(), true);
        }

        // Tableau : semaine, tours d'essai de la table de l'hote (sans sa propre voiture), humains dans l'ordre du
        // classement de l'hote. (Format de 0.96.0, inchange.)
        static NetWriter BoardMsg()
        {
            var w = new NetWriter(Msg.Race).U8(K_BOARD).I32(boardWeek);
            var laps = new List<KeyValuePair<string, float>>();
            if (lapTimes != null)
                foreach (object k in lapTimes.Keys)
                {
                    string key = k.ToString();
                    if (key == ME) continue;
                    laps.Add(new KeyValuePair<string, float>(key, ToF(lapTimes[key])));
                }
            w.U8(laps.Count);
            foreach (var kv in laps) w.Str(kv.Key).F32(kv.Value);
            List<Human> ranked = Ranked();
            w.U8(ranked.Count);
            foreach (Human h in ranked) w.U8(h.Id).Str(h.Name).Bool(h.Registered).F32(h.Lap).U8(h.Heat + 1).U8(h.Pos);
            return w;
        }

        // ---------------------------------------------------------------- hote : grille et manches (K_GRID)
        static string ListText(ArrayList l)
        {
            var sb = new System.Text.StringBuilder();
            if (l != null) for (int i = 0; i < l.Count; i++) sb.Append(i > 0 ? "," : "").Append(l[i]);
            return sb.ToString();
        }

        static string GridSig()
        {
            var sb = new System.Text.StringBuilder();
            sb.Append(TrackHeat()).Append('|');
            foreach (KeyValuePair<int, int> kv in numbers) sb.Append(kv.Key).Append('=').Append(kv.Value).Append(',');
            sb.Append('|');
            foreach (string n in HeatLists) { ArrayList l; if (heatLists.TryGetValue(n, out l)) sb.Append(ListText(l)); sb.Append(';'); }
            sb.Append('|').Append(ListText(resultsNumbers));
            float sum = 0f; if (lapTimes != null) foreach (object v in lapTimes.Values) sum += ToF(v);
            sb.Append('|').Append(lapTimes != null ? lapTimes.Count : 0).Append(':').Append(sum.ToString("F2", Inv));
            return sb.ToString();
        }

        // Envoye a chaque changement (verifie toutes les secondes) et toutes les 10 s (arrivants).
        static void SendGrid(bool force)
        {
            if (!Session.IsHost || Session.RemoteCount == 0 || track == null) return;
            float now = Time.realtimeSinceStartup;
            string sig = GridSig();
            if (!force && sig == gridSig && now < nextGridFull) return;
            gridSig = sig; nextGridFull = now + 10f;
            var w = new NetWriter(Msg.Race).U8(K_GRID).I32(boardWeek).U8(TrackHeat() + 1);
            w.U8(numbers.Count);
            foreach (KeyValuePair<int, int> kv in numbers) w.U8(kv.Key).U8(kv.Value);
            var laps = new List<KeyValuePair<string, float>>();
            if (lapTimes != null) foreach (object k in lapTimes.Keys) laps.Add(new KeyValuePair<string, float>(k.ToString(), ToF(lapTimes[k])));
            w.U8(laps.Count);
            foreach (var kv in laps) w.Str(kv.Key).F32(kv.Value);
            w.U8(HeatLists.Length);
            foreach (string n in HeatLists)
            {
                ArrayList l; heatLists.TryGetValue(n, out l);
                w.Str(n).U8(l != null ? l.Count : 0);
                if (l != null) for (int i = 0; i < l.Count; i++) w.Str(l[i] != null ? l[i].ToString() : "");
            }
            w.U8(resultsNumbers != null ? resultsNumbers.Count : 0);
            if (resultsNumbers != null) for (int i = 0; i < resultsNumbers.Count; i++) w.Str(resultsNumbers[i] != null ? resultsNumbers[i].ToString() : "");
            w.U8(IsRacing(track.ActiveStateName) ? 1 : 0);
            Session.Broadcast(w, true);
        }

        // ---------------------------------------------------------------- arrivees de manche
        // Chacun releve sa liste ResultsNumbers (le jeu y ajoute un numero a chaque voiture qui finit). Chez l'invite,
        // seule sa voiture y arrive (RaceTrigger des IA coupes par Races.Mute) : il l'annonce a l'hote (K_FINISH).
        static void PollArrivals()
        {
            if (resultsNumbers == null) return;
            int heat = TrackHeat();
            if (heat != arrivalsHeat || localWeek != arrivalsWeek) { arrivals.Clear(); seen.Clear(); arrivalsHeat = heat; arrivalsWeek = localWeek; }
            if (resultsNumbers.Count == 0) { if (seen.Count > 0) { arrivals.Clear(); seen.Clear(); } return; }   // (liste remise a zero : nouvelle manche)
            bool changed = false;
            float now = Time.realtimeSinceStartup;
            for (int i = 0; i < resultsNumbers.Count; i++)
            {
                object o = resultsNumbers[i];
                string s = o != null ? o.ToString() : "";
                if (s.Length == 0 || seen.Contains(s)) continue;
                seen.Add(s);
                arrivals.Add(new Arrival { Num = Canon(s), At = now, Db = resultsCars != null && i < resultsCars.Count ? resultsCars[i] as GameObject : null });
                changed = true;
                Log.Info("glace : arrivee de " + Label(s) + " en manche " + HeatName(heat) + " vue ici (" + (i + 1) + "e)");
                if (s == MyKey() && !Session.IsHost) Session.SendToHost(new NetWriter(Msg.Race).U8(K_FINISH).I32(localWeek).U8(heat + 1), true);
            }
            if (!changed) return;
            ReadFinish();
            if (Session.IsHost) Merge();
        }

        // Hote : arrivee d'un invite, datee ici a la reception (un ordre commun sans horloge partagee ; l'aller
        // simple du reseau, quelques dizaines de ms, compte comme un retard du pilote).
        static void OnFinish(Peer from, int week, int heat)
        {
            int n;
            if (!numbers.TryGetValue(from.Id, out n) || n == 0) { Log.Warn("glace : arrivee de " + NameOf(from.Id) + " sans numero : ignoree"); return; }
            string s = n.ToString();
            string st = track != null ? track.ActiveStateName : "";
            if (week != localWeek || (heat != arrivalsHeat && !IsRacing(st)))
            {
                Log.Warn("glace : arrivee de " + NameOf(from.Id) + " (semaine " + week + ", manche " + HeatName(heat) + ") hors de la manche en cours ici (" + HeatName(arrivalsHeat) + ", piste en " + st + ") : ignoree");
                return;
            }
            if (heat != arrivalsHeat) Log.Warn("glace : " + NameOf(from.Id) + " annonce la manche " + HeatName(heat) + ", ici " + HeatName(arrivalsHeat) + " : arrivee prise quand meme");
            if (seen.Contains(s)) return;
            seen.Add(s);
            arrivals.Add(new Arrival { Num = s, At = Time.realtimeSinceStartup, Db = DbOf(s) });
            Log.Info("glace : " + NameOf(from.Id) + " (n " + s + ") arrive en manche " + HeatName(arrivalsHeat) + " (" + arrivals.Count + "e)");
            Merge();
        }

        // Hote : IA et humains dans l'ordre d'arrivee -> la liste du jeu (ResultsNumbers, ResultsCars) est reecrite :
        // 'Weekend 2/4' en tire Semifinal/Pickup/Final, 'Get pos' le podium, Stats/ResultsRace les noms.
        static void Merge()
        {
            if (resultsNumbers == null) return;
            arrivals.Sort(delegate(Arrival a, Arrival b) { int c = a.At.CompareTo(b.At); return c != 0 ? c : string.CompareOrdinal(a.Num, b.Num); });
            resultsNumbers.Clear();
            if (resultsCars != null) resultsCars.Clear();
            bool changed = false;
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < arrivals.Count; i++)
            {
                Arrival a = arrivals[i];
                resultsNumbers.Add(a.Num);
                if (resultsCars != null) resultsCars.Add(a.Db != null ? a.Db : DbOf(a.Num));
                sb.Append(i > 0 ? " " : "").Append(i + 1).Append(':').Append(Label(a.Num));
                int id = IdOfNumber(a.Num);
                Human h;
                if (id >= 0 && humans.TryGetValue(id, out h) && (h.Heat != arrivalsHeat || h.Pos != i + 1)) { h.Heat = arrivalsHeat; h.Pos = i + 1; changed = true; }
            }
            Log.Info("glace : manche " + HeatName(arrivalsHeat) + " : arrivees fusionnees " + sb);
            if (changed) { ShowRank(); SendBoard(); }
            SendGrid(true);
        }

        // Objet DB (automate 'SkinChange' : numero, nom) d'un numero LOCAL : une location, la CORRIS, une doublure, une IA.
        static GameObject DbOf(string local)
        {
            int n; if (!int.TryParse(local, out n)) return null;
            GameObject car = null;
            int id = IdOfNumber(Canon(local));
            Transform rc = id >= 0 && RentalNumber(id) == LocalNum(n) ? RentalCar(id) : null;
            if (rc != null) car = rc.gameObject;
            else if (local == ME) car = Game.FindAny("CORRIS");
            else if (!standIns.TryGetValue(n, out car) && carsList != null && n >= 0 && n < carsList.Count) car = carsList[n] as GameObject;
            Transform db = car != null ? car.transform.Find("DB") : null;
            return db != null ? db.gameObject : null;
        }

        // ---------------------------------------------------------------- doublures des autres pilotes
        // Une voiture de paille par AUTRE pilote humain, dans Cars[numero local] : 'Create grid' y lit 'Throttle'.Navigation
        // puis pose 'Navigation'.StartPositionID (la voie) ; la table des resultats lit DB 'SkinChange' Number/DriverName.
        // Automates vides, comme l'etiquette MWCoopId de Jobs. A la racine de la scene (WorldFsms saute MWCoop*).
        // Locations : Cars[n] = la racine de la location (la sienne comprise), munie des memes automates si elle n'en a pas
        // (EnsureProxy) ; la voiture IA du jeu qui y etait est gardee (carsOrig) et sortie d'AllCars, rendues a la liberation.
        static void EnsureStandIns()
        {
            if (lineups == null) return;
            var others = new List<KeyValuePair<int, string>>();   // numero canonique -> pseudo
            if (!Session.IsHost)
            {
                int hn; if (!numbers.TryGetValue(0, out hn)) hn = 14;
                if (hn != 14 || myNum != 0) others.Add(new KeyValuePair<int, string>(hn, NameOf(0)));   // (hote en location : toujours ; en CORRIS : si on a un numero)
            }
            foreach (KeyValuePair<int, int> kv in numbers) if (kv.Value != 0 && kv.Key != Session.LocalId && kv.Key != 0) others.Add(new KeyValuePair<int, string>(kv.Value, NameOf(kv.Key)));
            if (myRental) others.Add(new KeyValuePair<int, string>(myNum, Session.Me != null ? Session.Me.Name : "?"));   // ma location : 'Create grid' lui donne sa voie
            RestoreCars(others);
            if (others.Count == 0) return;
            if (carsList == null)
            {
                if (!carsWarned) { carsWarned = true; Log.Warn("course glace : liste Cars de RACES/ICERACE/TrackFunctions introuvable : les autres pilotes ne seront pas sur la grille d'ici"); }
                return;
            }
            foreach (KeyValuePair<int, string> o in others)
            {
                int local = LocalNum(o.Key);
                if (local < 0 || local >= carsList.Count || (local == 14 && !myRental)) { if (!carsWarned) { carsWarned = true; Log.Warn("course glace : numero " + local + " hors de la liste Cars (" + carsList.Count + ")"); } continue; }
                int id = IdOfNumber(o.Key.ToString());
                Transform rc = id >= 0 && RentalNumber(id) == o.Key ? RentalCar(id) : null;
                GameObject go;
                if (rc != null)
                {
                    go = rc.gameObject;
                    EnsureProxy(go, local, o.Value, RentalModel(o.Key));
                    GameObject si; if (standIns.TryGetValue(local, out si)) { standIns.Remove(local); if (si != null) Object.Destroy(si); }
                    if (carsList[local] as GameObject != go)
                    {
                        if (!carsOrig.ContainsKey(local)) carsOrig[local] = carsList[local] as GameObject;
                        carsList[local] = go;
                        HideAi(local, carsOrig[local]);
                        Log.Info("glace : " + o.Value + " sur la grille d'ici avec sa location n " + local + " (Cars[" + local + "] = " + go.name + (carsOrig[local] != null ? ", voiture IA " + carsOrig[local].name + " ecartee" : "") + ")");
                    }
                    continue;
                }
                if (!standIns.TryGetValue(local, out go) || go == null) { go = MakeStandIn(local, o.Value); standIns[local] = go; }
                else
                {
                    Transform db = go.transform.Find("DB");
                    PlayMakerFSM skin = db != null ? Game.FsmOn(db.gameObject, "SkinChange") : null;
                    FsmString dn = skin != null ? skin.FsmVariables.FindFsmString("DriverName") : null;
                    if (dn != null && dn.Value != o.Value) dn.Value = o.Value;
                }
                if (carsList[local] as GameObject != go)
                {
                    if (!carsOrig.ContainsKey(local) && carsList[local] is GameObject) carsOrig[local] = carsList[local] as GameObject;
                    carsList[local] = go;
                    Log.Info("glace : " + o.Value + " sur la grille d'ici avec le numero " + local + " (Cars[" + local + "] = doublure)");
                }
            }
        }

        // Cars[n] et AllCars rendus au jeu pour les numeros qui ne sont plus a un humain (location rendue, semaine suivante).
        static void RestoreCars(List<KeyValuePair<int, string>> others)
        {
            if (carsList == null || carsOrig.Count == 0) return;
            var keep = new HashSet<int>();
            foreach (KeyValuePair<int, string> o in others) keep.Add(LocalNum(o.Key));
            var back = new List<int>();
            foreach (int local in carsOrig.Keys) if (!keep.Contains(local)) back.Add(local);
            foreach (int local in back)
            {
                GameObject orig = carsOrig[local]; carsOrig.Remove(local);
                if (local < carsList.Count && orig != null) carsList[local] = orig;
                GameObject ai; if (aiHidden.TryGetValue(local, out ai)) { aiHidden.Remove(local); if (allCars != null && ai != null && !allCars.Contains(ai)) allCars.Add(ai); }
                GameObject si; if (standIns.TryGetValue(local, out si)) { standIns.Remove(local); if (si != null) Object.Destroy(si); }
                Log.Info("glace : Cars[" + local + "] rendu au jeu (" + (orig != null ? orig.name : "-") + ")");
            }
        }

        // La voiture IA du numero loue sort d'AllCars (TrackFunctions 'State 6' : voitures tirees au hasard aux essais du
        // vendredi) ; Jokkis l'eteint, 'Create grid' ne la voit plus (Cars[n] = la location).
        static void HideAi(int local, GameObject ai)
        {
            if (ai == null || allCars == null || aiHidden.ContainsKey(local)) return;
            if (allCars.Contains(ai)) { allCars.Remove(ai); aiHidden[local] = ai; }
        }

        // La location a-t-elle ce qu'y lit le jeu ? 'Create grid' : 'Throttle'.Navigation -> 'Navigation'.StartPositionID ;
        // table des resultats : DB 'SkinChange' Number/DriverName/CarModel. Ce qui manque est ajoute (automates vides, comme
        // MakeStandIn) ; ce qui existe (voiture tiree d'une IA) est garde et renseigne. Le RaceTrigger du jeu sous DB
        // ('Data' : tours de la 14 / des IA) est coupe : le module compte lui-meme (TrackRental), sinon deux arrivees.
        static void EnsureProxy(GameObject root, int local, string name, string model)
        {
            PlayMakerFSM thr = Game.FsmOn(root, "Throttle");
            if (thr == null) { thr = root.AddComponent<PlayMakerFSM>(); thr.FsmName = "Throttle"; thr.FsmVariables.GameObjectVariables = new[] { new FsmGameObject("Navigation") }; }
            FsmGameObject navVar = thr.FsmVariables.FindFsmGameObject("Navigation");
            if (navVar == null)
            {
                navVar = new FsmGameObject("Navigation");
                var l = new List<FsmGameObject>(thr.FsmVariables.GameObjectVariables ?? new FsmGameObject[0]); l.Add(navVar);
                thr.FsmVariables.GameObjectVariables = l.ToArray();
            }
            GameObject nav = navVar.Value;
            if (nav == null)
            {
                Transform nt = root.transform.Find("NavigationIce");
                nav = nt != null ? nt.gameObject : new GameObject("NavigationIce");
                if (nt == null) nav.transform.parent = root.transform;
                navVar.Value = nav;
            }
            PlayMakerFSM navF = Game.FsmOn(nav, "Navigation");
            if (navF == null) { navF = nav.AddComponent<PlayMakerFSM>(); navF.FsmName = "Navigation"; navF.FsmVariables.IntVariables = new[] { new FsmInt("StartPositionID") }; }
            else if (navF.FsmVariables.FindFsmInt("StartPositionID") == null)
            {
                var l = new List<FsmInt>(navF.FsmVariables.IntVariables ?? new FsmInt[0]); l.Add(new FsmInt("StartPositionID"));
                navF.FsmVariables.IntVariables = l.ToArray();
            }
            Transform dbT = root.transform.Find("DB");
            GameObject db = dbT != null ? dbT.gameObject : null;
            if (db == null) { db = new GameObject("DB"); db.transform.parent = root.transform; }
            if (!db.activeSelf) db.SetActive(true);   // (variables lisibles par ArrayListGet/GetFsmString du jeu)
            PlayMakerFSM skin = Game.FsmOn(db, "SkinChange");
            if (skin == null)
            {
                skin = db.AddComponent<PlayMakerFSM>();
                skin.FsmName = "SkinChange";
                skin.FsmVariables.StringVariables = new[] { new FsmString("Number"), new FsmString("ID"), new FsmString("DriverName"), new FsmString("CarModel"), new FsmString("UA") { Value = "-" } };
                skin.FsmVariables.IntVariables = new[] { new FsmInt("DriverID") };
            }
            SetStr(skin, "Number", local.ToString()); SetStr(skin, "ID", local.ToString()); SetStr(skin, "DriverName", name);
            FsmString cm = skin.FsmVariables.FindFsmString("CarModel");
            if (cm != null && (string.IsNullOrEmpty(cm.Value) || cm.Value == "-") && model != "-") cm.Value = model;
            FsmInt did = skin.FsmVariables.FindFsmInt("DriverID"); if (did != null && did.Value != local) did.Value = local;
            Transform rt = db.transform.Find("RaceTrigger");
            PlayMakerFSM data = rt != null ? Game.FsmOn(rt.gameObject, "Data") : null;
            if (data != null && data.enabled) { data.enabled = false; Log.Info("glace : " + root.name + " : RaceTrigger 'Data' du jeu coupe (tours comptes par le module)"); }
        }

        static void SetStr(PlayMakerFSM f, string name, string v) { FsmString s = f.FsmVariables.FindFsmString(name); if (s != null && s.Value != v) s.Value = v; }

        // ---------------------------------------------------------------- location d'ici : grille
        // 'Create grid' a pose StartPositionID (1-6) sur ma location : je la teleporte sur sa voie (Gridding[voie] de
        // TrackFunctions, sinon StartPos<voie>), cap vers DragPoint1 (la ligne droite du depart), une fois par manche et
        // par voie, pendant la constitution de la grille seulement ; les autres la voient par VehicleSync.
        static void PlaceRental()
        {
            if (track == null || carsList == null || myNum <= 0 || myNum >= carsList.Count) return;
            string st = track.ActiveStateName;
            if (!placeAnyState && st != "Create grid" && st != "Create grid 2" && st != "Wait command" && st != "Wait command 2") return;
            int lane = LaneOf(myNum);
            if (lane <= 0) return;
            string key = localWeek + "|" + TrackHeat() + "|" + lane;
            if (key == placedKey) return;
            Transform car = RentalCar(Session.LocalId);
            if (car == null) return;
            Transform slot = GridSlot(lane);
            if (slot == null) { if (!slotWarned) { slotWarned = true; Log.Warn("glace : voie " + lane + " : emplacement de grille introuvable (Gridding / StartPos" + lane + ")"); } placedKey = key; return; }
            placedKey = key;
            Transform drag = slot.Find("DragPoint1"), first = slot.Find("1");
            Vector3 fwd = drag != null ? drag.position - slot.position : first != null ? slot.position - first.position : slot.forward;
            fwd.y = 0f;
            if (fwd.sqrMagnitude < 0.01f) fwd = slot.forward;
            Quaternion rot = Quaternion.LookRotation(fwd.normalized, Vector3.up);
            Vector3 pos = slot.position + Vector3.up * 0.3f;
            Rigidbody rb = car.GetComponent<Rigidbody>();
            if (rb != null && !rb.isKinematic) { rb.velocity = Vector3.zero; rb.angularVelocity = Vector3.zero; }
            car.position = pos; car.rotation = rot;
            if (rb != null) { rb.position = pos; rb.rotation = rot; }
            hasLast = false;   // (pas un franchissement de ligne)
            Log.Info("glace : ma location n " + myNum + " placee sur la voie " + lane + " de la grille " + HeatName(TrackHeat()) + " @" + pos.ToString("F1") + ", cap " + rot.eulerAngles.y.ToString("F0") + " (piste en " + st + ")");
        }

        static Transform GridSlot(int lane)
        {
            GameObject tf = Game.FindAny("RACES/ICERACE/TrackFunctions");
            if (tf == null) return null;
            foreach (PlayMakerArrayListProxy p in tf.GetComponents<PlayMakerArrayListProxy>())
                if (p.referenceName == "Gridding" && p.arrayList != null && lane < p.arrayList.Count) { var g = p.arrayList[lane] as GameObject; if (g != null) return g.transform; }
            return tf.transform.Find("StartPos" + lane);
        }

        // ---------------------------------------------------------------- location d'ici : tours et arrivee
        // Boites (monde) des lignes de la piste, depuis leur MeshCollider (objets eteints compris : StartFinishLaps et
        // StartFinishTime alternent) ; sans maillage, 20 m autour de l'objet.
        static void FindLines(GameObject tf)
        {
            lines = new Bounds[3];
            string[] names = { "Checkpoint1", "Checkpoint2", "StartFinishLaps" };
            for (int i = 0; i < 3; i++)
            {
                Transform t = tf.transform.Find(names[i]);
                if (t == null) { lines = null; if (!linesWarned) { linesWarned = true; Log.Warn("glace : ligne " + names[i] + " introuvable sous TrackFunctions : tours d'une location pas comptes"); } return; }
                lines[i] = LineBounds(t);
            }
            Transform lt = tf.transform.Find("StartFinishTime");
            if (lt != null) lines[2].Encapsulate(LineBounds(lt));
            lineLaps = tf.transform.Find("StartFinishLaps").gameObject;
            lineTime = lt != null ? lt.gameObject : null;
            Log.Info("glace : lignes de la piste relevees (CP1 " + lines[0].center.ToString("F0") + " " + lines[0].size.ToString("F0") + ", CP2 " + lines[1].center.ToString("F0") + ", arrivee " + lines[2].center.ToString("F0") + " " + lines[2].size.ToString("F0") + ")");
        }

        static Bounds LineBounds(Transform t)
        {
            var mc = t.GetComponent<MeshCollider>();
            Mesh m = mc != null ? mc.sharedMesh : null;
            if (m == null) return new Bounds(t.position, new Vector3(20f, 6f, 20f));
            Bounds lb = m.bounds;
            var b = new Bounds(t.TransformPoint(lb.center), Vector3.zero);
            for (int i = 0; i < 8; i++)
            {
                var c = new Vector3((i & 1) != 0 ? lb.max.x : lb.min.x, (i & 2) != 0 ? lb.max.y : lb.min.y, (i & 4) != 0 ? lb.max.z : lb.min.z);
                b.Encapsulate(t.TransformPoint(c));
            }
            b.Expand(new Vector3(1f, 2f, 1f));
            return b;
        }

        // La voiture (son pivot) vient-elle d'entrer dans la boite i entre ses deux dernieres positions ? (le segment coupe
        // la boite, ou la position d'arrivee y est, et elle n'y etait pas avant)
        static bool Crossed(int i, Vector3 a, Vector3 b)
        {
            Bounds bx = lines[i];
            bool now = bx.Contains(b), was = inside[i];
            if (!now && !was)
            {
                Vector3 d = b - a; float len = d.magnitude, dist;
                if (len > 0.001f && bx.IntersectRay(new Ray(a, d / len), out dist) && dist <= len) now = true;
            }
            bool entered = now && !was;
            inside[i] = bx.Contains(b);
            return entered;
        }

        static int RaceDistance()
        {
            PlayMakerFSM timing = board != null ? Game.FsmOn(board.gameObject, "Timing") : null;
            FsmInt d = timing != null ? timing.FsmVariables.FindFsmInt("RaceDistance") : null;
            return d != null && d.Value > 0 ? d.Value : 2;
        }

        // Comme CORRIS/DB/RaceTrigger 'Data', pour ma location : vendredi (StartFinishTime allume) un tour chronometre entre
        // deux passages de la ligne avec les deux points de passage -> meilleur tour dans LapTimes[n] ; en manche
        // (StartFinishLaps allume, piste de 'Start race' a 'State 14/15') RaceDistance tours -> arrivee (RentalFinished).
        // Tours remis a zero a chaque grille ; la teleportation sur la grille n'est pas un franchissement.
        static void TrackRental()
        {
            if (lines == null || track == null || myNum <= 0) return;
            Transform car = RentalCar(Session.LocalId);
            if (car == null) { hasLast = false; return; }
            string st = track.ActiveStateName;
            if (st == "Create grid" || st == "Create grid 2" || st == "Wait command" || st == "Wait command 2")
            {
                string key = localWeek + "|" + TrackHeat();
                if (key != lapsKey) { lapsKey = key; laps = 0; cp1 = cp2 = finished = false; Log.Info("glace : ma location n " + myNum + " : tours remis a zero pour la manche " + HeatName(TrackHeat())); }
                hasLast = false;
                return;
            }
            Vector3 p = car.position;
            if (!hasLast) { lastCarPos = p; hasLast = true; for (int i = 0; i < 3; i++) inside[i] = lines[i].Contains(p); return; }
            if ((p - lastCarPos).sqrMagnitude > 2500f) { lastCarPos = p; hasLast = false; return; }   // (teleportation)
            bool c1 = Crossed(0, lastCarPos, p), c2 = Crossed(1, lastCarPos, p), sf = Crossed(2, lastCarPos, p);
            lastCarPos = p;
            if (c1 && !cp1) cp1 = true;
            if (c2 && !cp2) cp2 = true;
            if (!sf) return;
            bool practice = lineTime != null && lineTime.activeInHierarchy, racing = lineLaps != null && lineLaps.activeInHierarchy;
            float now = Time.time;
            if (practice)
            {
                if (timing && cp1 && cp2)
                {
                    float lap = now - lapStart;
                    bool best = bestLap <= 0f || lap < bestLap;
                    if (best) { bestLap = lap; SetMyLap(lap); }
                    Log.Info("glace : ma location n " + myNum + " : tour d'essai " + lap.ToString("F1") + " s" + (best ? " (meilleur)" : " (meilleur " + bestLap.ToString("F1") + ")"));
                }
                else if (timing) Log.Info("glace : ma location n " + myNum + " : ligne franchie sans les deux points de passage, tour non compte");
                timing = true; lapStart = now; cp1 = cp2 = false;
                return;
            }
            if (!racing || finished) return;
            if (st != "Start race" && st != "Start race 2" && st != "Wait results" && st != "Wait results 2" && st != "State 14" && st != "State 15") return;
            if (!(cp1 && cp2))
            {
                if (Time.realtimeSinceStartup >= nextLineLog) { nextLineLog = Time.realtimeSinceStartup + 5f; Log.Info("glace : ma location n " + myNum + " : ligne franchie (depart ou tour incomplet), " + laps + " tour(s)"); }
                return;
            }
            laps++; cp1 = cp2 = false;
            int dist = RaceDistance();
            Log.Info("glace : ma location n " + myNum + " : tour " + laps + "/" + dist + " de la manche " + HeatName(TrackHeat()));
            if (laps >= dist) RentalFinished();
        }

        // Mon meilleur tour d'essai dans la table, a ma cle, valeur distincte des autres ('Leaderboard' retrouve le numero
        // par la valeur) ; Report l'annonce a l'hote (K_HUMAN), qui le met a ma cle dans sa table.
        static void SetMyLap(float lap)
        {
            if (lapTimes == null) return;
            if (bestLap <= 0f || lap < bestLap) bestLap = lap;
            string k = MyKey();
            float v = lap;
            while (HasValue(lapTimes, v, k)) v += 0.01f;
            lapTimes[k] = v;
            lastReport = "";
        }

        // Ma cle apres le tirage du jeu ('Set result' ecrit un tour au hasard sur les 16 numeros IA, le mien compris) : mon
        // meilleur tour mesure ici, sinon celui que l'hote garde pour moi (rechargement), sinon 999.<n> (pas de tour).
        static void FixMyLap()
        {
            if (!myRental || lapTimes == null) return;
            string k = MyKey();
            float hostV; hostLaps.TryGetValue(k, out hostV);   // (invite : table de l'hote ; hote : rien)
            float v = bestLap > 0f ? bestLap : !Session.IsHost && hostV > 0f && hostV < 900f ? hostV : 999f + myNum * 0.01f;
            if (lapTimes.ContainsKey(k) && Mathf.Abs(ToF(lapTimes[k]) - v) < 0.0005f) return;
            while (v < 900f && HasValue(lapTimes, v, k)) v += 0.01f;
            lapTimes[k] = v; lastReport = "";
            Log.Info("glace : ma location n " + myNum + " : tour d'essai remis a " + (v < 900f ? v.ToString("F2") + " s" : "aucun") + " apres le tirage du jeu");
        }

        // Arrivee de ma location, comme RaceTrigger 'State 2' : mon numero dans ResultsNumbers, mon DB dans ResultsCars,
        // FINISHRACE a tous (TrackFunctions 'Start race' -> 'Wait results', CORRIS/NavigationIce) ; PollArrivals la voit et
        // l'annonce a l'hote (K_FINISH) ou la fusionne (hote).
        static void RentalFinished()
        {
            finished = true;
            if (resultsNumbers == null) return;
            string k = MyKey();
            for (int i = 0; i < resultsNumbers.Count; i++) if (resultsNumbers[i] != null && resultsNumbers[i].ToString() == k) return;
            resultsNumbers.Add(k);
            if (resultsCars != null) resultsCars.Add(DbOf(k));
            Log.Info("glace : ma location n " + k + " a fini la manche " + HeatName(TrackHeat()) + " (" + resultsNumbers.Count + "e ici)");
            if (!testNoBroadcast) PlayMakerFSM.BroadcastEvent("FINISHRACE");
            nextPoll = 0;
        }

        static GameObject MakeStandIn(int local, string name)
        {
            var go = new GameObject("MWCoop-Glace-" + local);
            var nav = new GameObject("NavigationIce");
            nav.transform.parent = go.transform;
            PlayMakerFSM navF = nav.AddComponent<PlayMakerFSM>();
            navF.FsmName = "Navigation";
            navF.FsmVariables.IntVariables = new[] { new FsmInt("StartPositionID") };
            PlayMakerFSM thr = go.AddComponent<PlayMakerFSM>();
            thr.FsmName = "Throttle";
            thr.FsmVariables.GameObjectVariables = new[] { new FsmGameObject("Navigation") { Value = nav } };
            var db = new GameObject("DB");
            db.transform.parent = go.transform;
            PlayMakerFSM skin = db.AddComponent<PlayMakerFSM>();
            skin.FsmName = "SkinChange";
            skin.FsmVariables.StringVariables = new[] { new FsmString("Number") { Value = local.ToString() }, new FsmString("ID") { Value = local.ToString() }, new FsmString("DriverName") { Value = name }, new FsmString("CarModel") { Value = "-" }, new FsmString("UA") { Value = "-" } };
            skin.FsmVariables.IntVariables = new[] { new FsmInt("DriverID") { Value = local } };
            return go;
        }

        // Voie donnee par 'Create grid' a une doublure ou a une location (Cars[local] : 'Throttle'.Navigation ->
        // 'Navigation'.StartPositionID ; 0 : pas encore, ou voiture IA du jeu).
        static int LaneOf(int local)
        {
            GameObject go;
            if (!standIns.TryGetValue(local, out go) || go == null) go = carsList != null && local >= 0 && local < carsList.Count ? carsList[local] as GameObject : null;
            if (go == null || (!standIns.ContainsKey(local) && !carsOrig.ContainsKey(local))) return 0;
            PlayMakerFSM thr = Game.FsmOn(go, "Throttle");
            FsmGameObject nv = thr != null ? thr.FsmVariables.FindFsmGameObject("Navigation") : null;
            GameObject nav = nv != null ? nv.Value : null;
            PlayMakerFSM f = nav != null ? Game.FsmOn(nav, "Navigation") : null;
            FsmInt v = f != null ? f.FsmVariables.FindFsmInt("StartPositionID") : null;
            return v != null ? v.Value : 0;
        }

        // La grille de la manche en cours (liste de LINEUPS selon HeatStage), notee quand elle change.
        static void LogGrid()
        {
            int heat = TrackHeat();
            ArrayList l;
            if (heat < 0 || heat >= HeatLists.Length || !heatLists.TryGetValue(HeatLists[heat], out l) || l == null) return;
            var sb = new System.Text.StringBuilder();
            int n = 0;
            for (int i = 1; i < l.Count && i <= 6; i++)
            {
                string s = l[i] != null ? l[i].ToString() : "";
                if (s.Length == 0) continue;
                sb.Append(n++ > 0 ? " " : "").Append(i).Append(':').Append(Label(s));
                int x; if (int.TryParse(s, out x) && LaneOf(x) > 0) sb.Append("[voie ").Append(LaneOf(x)).Append(']');
            }
            string text = HeatName(heat) + " " + sb;
            if (text == lastGridLog) return;
            lastGridLog = text;
            if (n > 0) Log.Info("glace : grille " + text);
        }

        // ---------------------------------------------------------------- messages
        public static void OnMessage(int kind, Peer from, NetReader r)
        {
            if (!enabled) return;
            if (kind == K_HUMAN && Session.IsHost)
            {
                PlayerInfo pi;
                var h = new Human { Id = from.Id, Name = r.Str(), Registered = r.Bool(), Lap = r.F32(), Heat = r.U8() - 1, Pos = r.U8() };
                if (Session.Players.TryGetValue(from.Id, out pi)) h.Name = pi.Name;
                // Sa place vient de la fusion d'ici (chez lui, seule sa voiture arrive : toujours 1er).
                Human old;
                if (humans.TryGetValue(from.Id, out old) && old.Heat >= h.Heat) { h.Heat = old.Heat; h.Pos = old.Pos; }
                SetHuman(h);
                return;
            }
            if (kind == K_FINISH && Session.IsHost)
            {
                int week = r.I32(), heat = r.U8() - 1;
                OnFinish(from, week, heat);
                return;
            }
            if (kind == K_BOARD && !Session.IsHost)
            {
                boardWeek = r.I32();
                int n0 = r.U8();
                if (!hostLapsFull) hostLaps.Clear();
                for (int i = 0; i < n0; i++) { string k = r.Str(); float v = r.F32(); if (!hostLapsFull) hostLaps[k] = v; }
                humans.Clear();
                for (int i = 0, n = r.U8(); i < n; i++)
                {
                    var h = new Human { Id = r.U8(), Name = r.Str(), Registered = r.Bool(), Lap = r.F32(), Heat = r.U8() - 1, Pos = r.U8() };
                    humans[h.Id] = h;
                }
                nextApply = 0;
                ShowRank();
                return;
            }
            if (kind == K_GRID && !Session.IsHost) OnGrid(r);
        }

        // Invite : grille et manches de l'hote.
        static void OnGrid(NetReader r)
        {
            boardWeek = r.I32();
            int hh = r.U8() - 1;
            numbers.Clear();
            for (int i = 0, n = r.U8(); i < n; i++) { int id = r.U8(); numbers[id] = r.U8(); }
            int mine; if (!numbers.TryGetValue(Session.LocalId, out mine)) mine = 0;
            if (mine != myNum)
            {
                myNum = mine;
                bool rental = myNum != 0 && RentalNumber(Session.LocalId) == myNum;
                Log.Info("glace : mon numero chez l'hote : " + (myNum != 0 ? myNum.ToString() : "aucun") + (myNum != 0 ? rental ? " (ma location : numero reel)" : " (14 ici : 14 et " + myNum + " echanges)" : ""));
                if (myNum != 0 && !rental) Hud.Toast(Lang.T("Glace : vous êtes la voiture n° ", "Ice race: you are car no. ") + myNum + Lang.T(" pour les autres", " for the others"));
                lastGridLog = "";
            }
            RefreshRentals();
            hostLaps.Clear(); hostLapsFull = true;
            for (int i = 0, n = r.U8(); i < n; i++) { string k = r.Str(); hostLaps[k] = r.F32(); }
            hostLists.Clear();
            for (int i = 0, n = r.U8(); i < n; i++)
            {
                string name = r.Str();
                var items = new string[r.U8()];
                for (int j = 0; j < items.Length; j++) items[j] = r.Str();
                hostLists[name] = items;
            }
            hostResults = new string[r.U8()];
            for (int i = 0; i < hostResults.Length; i++) hostResults[i] = r.Str();
            hostRacing = r.U8() != 0;
            hostHeat = hh;
            nextApply = 0;
            ApplyHostLaps(false);
            EnsureStandIns();
            ApplyLists();
            ApplyResults();
            FollowHostHeat();
        }

        // Les 9 listes de LINEUPS de l'hote, recopiees ici (echange 14/N) quand elles different. Le vendredi,
        // 'Leaderboard' refait Qual1-4 toutes les 4 s depuis la table (identique : meme contenu).
        static void ApplyLists()
        {
            int changed = 0;
            foreach (KeyValuePair<string, string[]> kv in hostLists)
            {
                ArrayList l;
                if (!heatLists.TryGetValue(kv.Key, out l) || l == null) continue;
                bool same = l.Count == kv.Value.Length;
                for (int i = 0; same && i < kv.Value.Length; i++) same = (l[i] != null ? l[i].ToString() : "") == Local(kv.Value[i]);
                if (same) continue;
                l.Clear();
                foreach (string s in kv.Value) l.Add(Local(s));
                changed++;
            }
            if (changed > 0) { Log.Info("glace : " + changed + " liste(s) de manches recopiee(s) de l'hote"); lastGridLog = ""; }
        }

        // Les arrivees de la manche de l'hote (fusionnees), recopiees ici (echange 14/N) si c'est aussi notre manche.
        static void ApplyResults()
        {
            if (resultsNumbers == null || hostResults == null) return;
            int heat = TrackHeat();
            if (heat != hostHeat)
            {
                if (hostResults.Length > 0 && Time.realtimeSinceStartup >= nextSkipLog) { nextSkipLog = Time.realtimeSinceStartup + 30f; Log.Info("glace : arrivees de l'hote (manche " + HeatName(hostHeat) + ") pas recopiees : manche d'ici " + HeatName(heat)); }
                return;
            }
            bool same = resultsNumbers.Count == hostResults.Length;
            for (int i = 0; same && i < hostResults.Length; i++) same = (resultsNumbers[i] != null ? resultsNumbers[i].ToString() : "") == Local(hostResults[i]);
            if (same) return;
            resultsNumbers.Clear();
            if (resultsCars != null) resultsCars.Clear();
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < hostResults.Length; i++)
            {
                string s = Local(hostResults[i]);
                resultsNumbers.Add(s);
                if (resultsCars != null) resultsCars.Add(DbOf(s));
                seen.Add(s);
                sb.Append(i > 0 ? " " : "").Append(i + 1).Append(':').Append(Label(s));
            }
            Log.Info("glace : arrivees de la manche " + HeatName(heat) + " recopiees de l'hote : " + sb);
            ReadFinish();
        }

        // Invite : la manche de l'hote fait reference. Hors course : HeatStage recale. En course et l'hote a clos la
        // manche (son HeatStage a avance) alors que la piste d'ici attend encore des resultats : 'Weekend 2' / 'Weekend 4'
        // (HeatStage+1, Semifinal/Pickup/Final depuis ResultsNumbers -- deja ceux de l'hote).
        static void FollowHostHeat()
        {
            if (Session.IsHost || track == null || hostHeat < 0) return;
            FsmInt hs = track.FsmVariables.FindFsmInt("HeatStage");
            if (hs == null) return;
            int mine = hs.Value;
            if (mine == hostHeat) return;
            string st = track.ActiveStateName;
            if (!IsRacing(st))
            {
                hs.Value = hostHeat;
                lastGridLog = "";
                Log.Info("glace : manche d'ici " + HeatName(mine) + " -> " + HeatName(hostHeat) + " comme chez l'hote (piste en " + st + ")");
                return;
            }
            string next = null;
            if (hostHeat > mine && (st == "Start race" || st == "Wait results" || st == "State 14")) next = "Weekend 2";
            if (hostHeat > mine && (st == "Start race 2" || st == "Wait results 2" || st == "State 15")) next = "Weekend 4";
            if (next == null)
            {
                if (Time.realtimeSinceStartup >= nextSkipLog) { nextSkipLog = Time.realtimeSinceStartup + 30f; Log.Info("glace : manche d'ici " + HeatName(mine) + ", hote " + HeatName(hostHeat) + ", piste en " + st + " : pas recalee"); }
                return;
            }
            hs.Value = hostHeat - 1;
            Replay.Depth++;
            try { Game.SetState(track, next); }
            finally { Replay.Depth--; }
            Log.Info("glace : manche " + HeatName(hostHeat - 1) + " close chez l'hote : resultats pris ici (" + next + ", piste etait en " + st + ")");
        }

        // ---------------------------------------------------------------- inscription
        static void OnRegistered()
        {
            if (!Session.Active) return;
            if (!Session.IsHost && !restored) return;   // chargement : c'est l'inscription de l'hote (sauvegarde), pas la notre
            lastReport = "";
            Report(true);
            // Inscrit sans la Rivett (ou pas encore de voiture) : le panneau des Jokkis a louer s'ouvre (Jokkis actif chez l'hote)
            if (RentalNumber(Session.LocalId) == 0 && Config.GetInt("Coop", "Jokkis", 0) != 0) JokkisPanel.Open = true;
        }

        // ---------------------------------------------------------------- tirage de l'hote chez l'invite
        // La table de l'hote (16 IA, lui en 14, les invites a leur numero) remplace celle tiree ici, numeros echanges
        // (14 <-> le notre), sauf notre propre voiture ; les numeros absents chez l'hote sont retires (IA cedant la place).
        // Le 'Leaderboard' retrie toutes les 4 s le vendredi et refait Qual1-4 -> meme grille que chez l'hote.
        // Locataire : sa cle est son numero (la 14 est alors celle de l'hote, recopiee ou retiree comme les autres) ; sans
        // tour ici (rechargement), celui que l'hote garde pour lui est repris.
        static void ApplyHostLaps(bool afterDraw)
        {
            if (Session.IsHost || lapTimes == null || hostLaps.Count == 0) return;
            int changed = 0, removed = 0;
            float mine = MyLap();
            string my = MyKey();
            foreach (KeyValuePair<string, float> kv in hostLaps)
            {
                string lk = Local(kv.Key);
                if (kv.Value <= 0f) continue;
                if (lk == my)
                {
                    if (myRental && mine <= 0f && kv.Value < 900f) { lapTimes[lk] = kv.Value; mine = kv.Value; changed++; Log.Info("glace : mon tour d'essai (" + kv.Value.ToString("F1") + " s) repris de la table de l'hote"); }
                    continue;
                }
                float v = kv.Value;
                if (mine > 0f && Mathf.Abs(v - mine) < 0.0005f) v += 0.01f;   // (meme tour que le notre : valeurs distinctes)
                if (lapTimes.ContainsKey(lk) && Mathf.Abs(ToF(lapTimes[lk]) - v) < 0.001f) continue;
                lapTimes[lk] = v;
                changed++;
            }
            if (hostLapsFull)
            {
                var gone = new List<string>();
                foreach (object k in lapTimes.Keys) { string key = k.ToString(); if (key != my && !hostLaps.ContainsKey(Canon(key))) gone.Add(key); }
                foreach (string key in gone) { lapTimes.Remove(key); removed++; }
            }
            if (changed > 0 || removed > 0) Log.Info("glace : " + changed + " tours d'essai recopies de l'hote" + (removed > 0 ? ", " + removed + " voiture(s) retiree(s)" : "") + (afterDraw ? " (apres le tirage du jeu ici)" : "") + " ; " + lapTimes.Count + " voitures");
        }

        // ---------------------------------------------------------------- classement commun
        // Manche la plus avancee d'abord, puis la place, puis le meilleur tour d'essai (sans tour : dernier).
        static List<Human> Ranked()
        {
            var l = new List<Human>(humans.Values);
            l.Sort(delegate(Human a, Human b)
            {
                if (a.Heat != b.Heat) return b.Heat.CompareTo(a.Heat);
                int pa = a.Pos > 0 ? a.Pos : 99, pb = b.Pos > 0 ? b.Pos : 99;
                if (pa != pb) return pa.CompareTo(pb);
                float la = a.Lap > 0f ? a.Lap : 1e9f, lb = b.Lap > 0f ? b.Lap : 1e9f;
                if (la != lb) return la.CompareTo(lb);
                return a.Id.CompareTo(b.Id);
            });
            return l;
        }

        static string LapText(float s) { return s > 0f ? ((int)(s / 60f)) + ":" + (s % 60f).ToString("00.0", Inv) : "-"; }

        // Numero LOCAL d'un humain (0 : aucun).
        static int NumOf(Human h)
        {
            if (h.Id == Session.LocalId) return myRental ? myNum : 14;
            int n; if (numbers.TryGetValue(h.Id, out n)) return LocalNum(n);
            return h.Id == 0 ? LocalNum(14) : 0;
        }

        // Le tableau commun, montre (message) quand il change ; seuls les inscrits ou ceux qui ont couru y figurent.
        static void ShowRank()
        {
            var sb = new System.Text.StringBuilder();
            int n = 0;
            foreach (Human h in Ranked())
            {
                if (!h.Registered && h.Lap == 0f && h.Pos == 0) continue;
                if (n > 0) sb.Append(", ");
                sb.Append(++n).Append(". ").Append(h.Name);
                int num = NumOf(h);
                if (num > 0 && h.Registered) sb.Append(" #").Append(num);
                if (h.Pos > 0) sb.Append(' ').Append(HeatName(h.Heat)).Append(' ').Append(h.Pos).Append(Lang.T("e", h.Pos == 1 ? "st" : h.Pos == 2 ? "nd" : h.Pos == 3 ? "rd" : "th"));
                if (h.Lap > 0f) sb.Append(' ').Append(LapText(h.Lap));
            }
            string s = sb.ToString();
            if (s == lastRank) return;
            bool first = lastRank.Length == 0;
            lastRank = s;
            if (n == 0) return;
            Log.Info("glace : classement commun : " + s);
            if (!first || n > 1) Hud.Toast(Lang.T("Glace : ", "Ice race: ") + s);
        }

        // ---------------------------------------------------------------- participation gardee par monde
        // Invite : inscription, etapes et meilleur tour (la sauvegarde recue est celle de l'hote). Tous : derniere arrivee
        // de manche (semaine, manche, place), que le jeu ne garde pas.
        static string StorePath { get { return System.IO.Path.Combine(Log.DataDir, "glace.ini"); } }

        static void Store()
        {
            string w = World();
            if (w.Length == 0 || lineups == null) return;
            var sb = new System.Text.StringBuilder();
            sb.Append("Registered:").Append(Registered() ? 1 : 0).Append(';');
            FsmInt rs = lineups.FsmVariables.FindFsmInt("RaceStage"), hs = lineups.FsmVariables.FindFsmInt("HeatStage");
            if (rs != null) sb.Append("RaceStage:").Append(rs.Value).Append(';');
            if (hs != null) sb.Append("HeatStage:").Append(hs.Value).Append(';');
            sb.Append("Lap:").Append(MyLap().ToString("R", Inv)).Append(';');
            sb.Append("Num:").Append(myRental ? myNum : 0).Append(';');   // (location : le tour est a ce numero)
            sb.Append("Week:").Append(localWeek).Append(';');
            sb.Append("Heat:").Append(lastHeat).Append(';');
            sb.Append("Pos:").Append(lastPos).Append(';');
            string line = w + "=" + sb;
            var lines = new List<string>();
            bool same = false;
            if (System.IO.File.Exists(StorePath)) foreach (string l in System.IO.File.ReadAllLines(StorePath)) { if (l == line) same = true; if (!l.StartsWith(w + "=")) lines.Add(l); }
            if (same) return;   // (rien de neuf : pas d'ecriture toutes les 10 s)
            lines.Add(line);
            try { System.IO.File.WriteAllLines(StorePath, lines.ToArray()); } catch { }
        }

        static Dictionary<string, string> Load()
        {
            var d = new Dictionary<string, string>();
            string w = World(), line = null;
            if (w.Length > 0 && System.IO.File.Exists(StorePath))
                foreach (string l in System.IO.File.ReadAllLines(StorePath)) if (l.StartsWith(w + "=")) line = l.Substring(w.Length + 1);
            if (line == null) return null;
            foreach (string kv in line.Split(';'))
            {
                int c = kv.IndexOf(':');
                if (c > 0) d[kv.Substring(0, c)] = kv.Substring(c + 1);
            }
            return d;
        }

        // La sauvegarde recue est celle de l'hote : sa participation est remplacee par celle de l'invite (ou effacee).
        // 'Check status' du jeu refait ce qui en decoule (Cars[14] = CORRIS ou Reserved, autocollants, CORRIS/DB) et
        // 'State 2' redonne HeatStage a TrackFunctions. (Les listes de manches de la sauvegarde sont celles de l'hote, en
        // numeros canoniques : K_GRID les recopie echangees dans les 10 s.)
        static void RestoreGuest()
        {
            Dictionary<string, string> d = Load();
            FsmBool reg = lineups.FsmVariables.FindFsmBool("PlayerRegistered");
            FsmInt rs = lineups.FsmVariables.FindFsmInt("RaceStage"), hs = lineups.FsmVariables.FindFsmInt("HeatStage");
            bool hadHost = Registered();
            int i; float f;
            if (d == null)
            {
                if (reg != null) reg.Value = false;
                if (rs != null) rs.Value = 0; if (hs != null) hs.Value = 0;
                if (lapTimes != null && lapTimes.ContainsKey(ME)) lapTimes[ME] = 999f;   // (valeur du jeu : pas de tour)
                if (hadHost) Log.Info("glace : participation de l'hote (sauvegarde) effacee ici : pas inscrit");
            }
            else
            {
                string v;
                if (reg != null && d.TryGetValue("Registered", out v)) reg.Value = v == "1";
                if (rs != null && d.TryGetValue("RaceStage", out v) && int.TryParse(v, out i)) rs.Value = i;
                if (hs != null && d.TryGetValue("HeatStage", out v) && int.TryParse(v, out i)) hs.Value = i;
                int num = 0; if (d.TryGetValue("Num", out v)) int.TryParse(v, out num);
                if (lapTimes != null && d.TryGetValue("Lap", out v) && float.TryParse(v, System.Globalization.NumberStyles.Float, Inv, out f))
                {
                    if (num <= 0) lapTimes[ME] = f > 0f ? f : 999f;
                    else if (f > 0f && RentalNumber(Session.LocalId) == num) lapTimes[num.ToString()] = f;   // (toujours locataire de cette voiture)
                }
                int wk = -1;
                if (d.TryGetValue("Week", out v)) int.TryParse(v, out wk);
                if (wk == localWeek)
                {
                    if (d.TryGetValue("Heat", out v) && int.TryParse(v, out i)) lastHeat = i;
                    if (d.TryGetValue("Pos", out v) && int.TryParse(v, out i)) lastPos = i;
                }
                Log.Info("glace : participation d'ici retrouvee (inscrit " + Registered() + ", tour " + MyLap().ToString("F1") + (lastPos > 0 ? ", " + lastPos + "e en " + HeatName(lastHeat) : "") + ")");
            }
            if (d != null || hadHost)
            {
                Replay.Depth++;
                try { Game.SetState(lineups, "Check status"); }
                finally { Replay.Depth--; }
            }
            lastReport = "";
        }

        // ---------------------------------------------------------------- essais
        // [Test] Autotest=glace2 (avec la semaine de la glace de Races 'glace', vendredi 10 h) : chacun s'inscrit a 30 s
        // (REGISTER a LINEUPS, comme la feuille), a 45 s pose son meilleur tour d'essai ([Test] TourGlace, hote 88 s,
        // invite 86 s, dans LapTimes["14"] comme CORRIS/DB/RaceTrigger 'Set result'), a 70 s note sa table LapTimes, ses
        // listes Qual1-4 et le tableau des humains. Attendu : memes tours des IA des deux cotes (ceux de l'hote), les deux
        // pilotes humains inscrits dans le tableau de chacun, l'invite devant ; l'invite a un numero (15) : chez l'hote
        // 14=88 15=86, chez l'invite 14=86 15=88, memes voies dans Qual1-4 (etiquettes 14/15 echangees), Cars[15] =
        // doublure des deux cotes.
        // [Test] Autotest=glace3 : glace2, puis une manche simulee (vendredi, HeatStage 0) : l'hote ajoute "37" a 80 s et
        // "10" a 83 s a sa liste ResultsNumbers (comme le RaceTrigger des IA), l'invite ajoute "14" (lui) a 86 s, l'hote
        // "14" (lui) a 92 s. A 105 s chacun note ses arrivees et le classement. Attendu : hote 37 10 15 14, invite
        // 37 10 14 15 (le meme ordre, numeros echanges) ; classement commun : invite Q1 3e, hote Q1 4e.
        // [Test] Autotest=glace4 (avec [Test] GlaceJokkis=33 des deux cotes) : l'hote s'inscrit en CORRIS (14), l'invite est
        // repute louer la voiture 33 (racine factice MWCoop-Jokkis-33) : son inscription a 30 s passe par 'State 4' reduit
        // (pas d'autocollants), son tour d'essai (84 s) va a LapTimes["33"] par SetMyLap. A 70 s chacun note sa table (attendu
        // des deux cotes : 14=88.0 33=84.0, IA 33 remplacee, pas d'echange), ses listes Qual (33 et 14 dedans, identiques)
        // et Cars (33 = location). A 75 s l'invite simule 'Create grid' (StartPositionID 3 sur sa location) : placee sur la
        // voie 3. Manche simulee : hote "37" a 80 s, hote "14" a 86 s, invite arrivee de sa location a 92 s (RentalFinished :
        // "33" dans ResultsNumbers, K_FINISH). A 105 s attendu des deux cotes : 37 14 33 ; classement : hote 2e, invite 3e.
        static int testStep;
        public static void Test(string mode, float t)
        {
            if ((mode != "glace2" && mode != "glace3" && mode != "glace4") || lineups == null || board == null) return;
            bool g4 = mode == "glace4";
            if (testStep == 0 && t > 30f)
            {
                testStep = 1;
                if (g4) { testNoBroadcast = true; placeAnyState = true; }
                lineups.SendEvent("REGISTER");
                Log.Info("autotest : " + mode + " : inscription -> " + lineups.ActiveStateName + ", inscrit " + Registered() + (g4 ? ", location " + RentalNumber(Session.LocalId) : ""));
            }
            if (testStep == 1 && t > 45f)
            {
                testStep = 2;
                float lap = Config.GetInt("Test", Session.IsHost ? "TourGlace" : "TourGlaceInvite", Session.IsHost ? 88 : g4 ? 84 : 86);
                if (g4 && !Session.IsHost && !myRental) Log.Warn("autotest : glace4 : l'hote ne m'a pas encore donne ma location (numero " + myNum + ")");
                if (g4) SetMyLap(lap); else if (lapTimes != null) lapTimes[ME] = lap;
                Log.Info("autotest : " + mode + " : meilleur tour d'ici " + lap + " s (cle " + MyKey() + ")");
            }
            if (testStep == 2 && t > 70f)
            {
                testStep = 3;
                var sb = new System.Text.StringBuilder();
                if (lapTimes != null)
                {
                    var keys = new List<string>();
                    foreach (object k in lapTimes.Keys) keys.Add(k.ToString());
                    keys.Sort(string.CompareOrdinal);
                    foreach (string k in keys) sb.Append(' ').Append(k).Append('=').Append(ToF(lapTimes[k]).ToString("F1"));
                }
                var ql = new System.Text.StringBuilder();
                foreach (PlayMakerArrayListProxy p in lineups.gameObject.GetComponents<PlayMakerArrayListProxy>())
                {
                    if (!p.referenceName.StartsWith("Qual")) continue;
                    ql.Append(' ').Append(p.referenceName).Append('[');
                    for (int i = 0; i < p.arrayList.Count; i++) ql.Append(i > 0 ? "," : "").Append(p.arrayList[i]);
                    ql.Append(']');
                }
                var hs = new System.Text.StringBuilder();
                foreach (Human h in Ranked()) hs.Append(' ').Append(h.Name).Append('=').Append(h.Registered ? "inscrit" : "-").Append('/').Append(h.Lap.ToString("F1")).Append("/n").Append(NumOf(h));
                var cs = new System.Text.StringBuilder();
                foreach (KeyValuePair<int, GameObject> kv in standIns) cs.Append(' ').Append(kv.Key).Append('=').Append(carsList != null && kv.Key < carsList.Count && carsList[kv.Key] as GameObject == kv.Value ? "doublure" : "?");
                foreach (KeyValuePair<int, GameObject> kv in carsOrig) if (!standIns.ContainsKey(kv.Key)) { var g = carsList != null && kv.Key < carsList.Count ? carsList[kv.Key] as GameObject : null; cs.Append(' ').Append(kv.Key).Append('=').Append(g != null && g.name.StartsWith("MWCoop-Jokkis") ? "location" : g != null ? g.name : "?"); }
                Log.Info("autotest : " + mode + " : mon numero " + myNum + (myRental ? " (location)" : "") + " ; tours" + sb + " ; grilles" + ql + " ; humains" + hs + " ; Cars" + cs);
            }
            if (g4)
            {
                if (testStep == 3 && t > 75f && !Session.IsHost)
                {
                    testStep = 4;
                    GameObject go = carsList != null && myNum > 0 && myNum < carsList.Count ? carsList[myNum] as GameObject : null;
                    PlayMakerFSM thr = go != null ? Game.FsmOn(go, "Throttle") : null;
                    FsmGameObject nv = thr != null ? thr.FsmVariables.FindFsmGameObject("Navigation") : null;
                    PlayMakerFSM nav = nv != null && nv.Value != null ? Game.FsmOn(nv.Value, "Navigation") : null;
                    FsmInt sp = nav != null ? nav.FsmVariables.FindFsmInt("StartPositionID") : null;
                    if (sp != null) sp.Value = 3;
                    Log.Info("autotest : glace4 : 'Create grid' simule : voie 3 posee sur Cars[" + myNum + "] (" + (go != null ? go.name : "-") + ", StartPositionID " + (sp != null ? "ok" : "absent") + ")");
                }
                if (testStep == 4 && t > 78f && !Session.IsHost)
                {
                    testStep = 5;
                    Transform car = RentalCar(Session.LocalId);
                    Log.Info("autotest : glace4 : ma location " + (car != null ? "@" + car.position.ToString("F1") + " cap " + car.eulerAngles.y.ToString("F0") : "absente") + " ; voie lue " + LaneOf(myNum) + " ; grille " + lastGridLog);
                }
                if (testStep == 3 && t > 80f && Session.IsHost) { testStep = 5; if (resultsNumbers != null) resultsNumbers.Add("37"); Log.Info("autotest : glace4 : arrivee IA 37 simulee ici"); }
                if (testStep == 5 && t > 86f && Session.IsHost) { testStep = 6; if (resultsNumbers != null) resultsNumbers.Add(ME); Log.Info("autotest : glace4 : mon arrivee simulee ici (14)"); }
                if (testStep == 5 && t > 92f && !Session.IsHost) { testStep = 6; RentalFinished(); Log.Info("autotest : glace4 : arrivee de ma location simulee ici (" + MyKey() + ")"); }
                if (testStep == 6 && t > 105f)
                {
                    testStep = 7;
                    var sb = new System.Text.StringBuilder();
                    if (resultsNumbers != null) for (int i = 0; i < resultsNumbers.Count; i++) sb.Append(i > 0 ? " " : "").Append(i + 1).Append(':').Append(Label(resultsNumbers[i] != null ? resultsNumbers[i].ToString() : ""));
                    var cs = new System.Text.StringBuilder();
                    if (resultsCars != null) for (int i = 0; i < resultsCars.Count; i++) { var g = resultsCars[i] as GameObject; cs.Append(i > 0 ? "," : "").Append(g != null && g.transform.parent != null ? g.transform.parent.name : "-"); }
                    var hs = new System.Text.StringBuilder();
                    foreach (Human h in Ranked()) hs.Append(' ').Append(h.Name).Append('=').Append(h.Pos > 0 ? h.Pos + "e/" + HeatName(h.Heat) : "-").Append("/n").Append(NumOf(h));
                    Log.Info("autotest : glace4 : arrivees " + sb + " ; DB " + cs + " ; classement" + hs + " ; manche ici " + HeatName(TrackHeat()) + (Session.IsHost ? "" : ", hote " + HeatName(hostHeat)) + " ; ma cle " + MyKey());
                }
                return;
            }
            if (mode != "glace3" || resultsNumbers == null) return;
            if (testStep == 3 && t > 80f && Session.IsHost) { testStep = 4; resultsNumbers.Add("37"); Log.Info("autotest : glace3 : arrivee IA 37 simulee ici"); }
            if (testStep == 4 && t > 83f && Session.IsHost) { testStep = 5; resultsNumbers.Add("10"); Log.Info("autotest : glace3 : arrivee IA 10 simulee ici"); }
            if (testStep == 3 && t > 86f && !Session.IsHost) { testStep = 5; resultsNumbers.Add(ME); Log.Info("autotest : glace3 : mon arrivee simulee ici (14)"); }
            if (testStep == 5 && t > 92f && Session.IsHost) { testStep = 6; resultsNumbers.Add(ME); Log.Info("autotest : glace3 : mon arrivee simulee ici (14)"); }
            if (testStep >= 5 && testStep < 7 && t > 105f)
            {
                testStep = 7;
                var sb = new System.Text.StringBuilder();
                for (int i = 0; i < resultsNumbers.Count; i++) sb.Append(i > 0 ? " " : "").Append(i + 1).Append(':').Append(Label(resultsNumbers[i] != null ? resultsNumbers[i].ToString() : ""));
                var cs = new System.Text.StringBuilder();
                if (resultsCars != null) for (int i = 0; i < resultsCars.Count; i++) { var g = resultsCars[i] as GameObject; cs.Append(i > 0 ? "," : "").Append(g != null && g.transform.parent != null ? g.transform.parent.name : "-"); }
                var hs = new System.Text.StringBuilder();
                foreach (Human h in Ranked()) hs.Append(' ').Append(h.Name).Append('=').Append(h.Pos > 0 ? h.Pos + "e/" + HeatName(h.Heat) : "-");
                Log.Info("autotest : glace3 : arrivees " + sb + " ; DB " + cs + " ; classement" + hs + " ; manche ici " + HeatName(TrackHeat()) + (Session.IsHost ? "" : ", hote " + HeatName(hostHeat)));
            }
        }

        // TODO (a verifier en jeu, vidage a faire) -- ce qui reste de l'ecart n1 :
        //  - doublures : automates 'Throttle'/'Navigation'/'SkinChange' crees vides a chaud (comme MWCoopId de Jobs) ;
        //    verifier dans le journal du jeu (output_log) que 'Create grid' ne se plaint pas, et que le panneau de la
        //    grille (StartingGrid 'State 4') montre bien le numero de l'autre pilote dans sa voie ;
        //  - instants d'arrivee : l'arrivee d'un invite est datee a la reception chez l'hote (pas d'horloge commune) :
        //    deux voitures a moins de ~0,1 s peuvent etre inversees ;
        //  - pistes decalees : TrackFunctions 'Data' tourne chez chacun (grille 6 x 11 s, 'Wait command' sans attente chez
        //    l'invite : ses IA sont coupees) ; HeatStage recale hors course, 'Weekend 2/4' force quand l'hote clot une
        //    manche que la piste d'ici attend ; un invite dont la piste est encore en 'Create grid' quand l'hote clot n'est
        //    pas recale (journal "pas recalee") ;
        //  - listes de manches de l'invite entre deux sessions : la sauvegarde recue (celle de l'hote) les contient en
        //    numeros canoniques, K_GRID les recopie echangees dans les 10 s ; RESULTSPublic (table publique) est refaite
        //    par le jeu de chacun depuis ResultsNumbers (recopiee) aux evenements QUAL1... ;
        //  - prix (TentContents/Prices/PriceMoneyRace, 'Get pos' : podium par ResultsNumbers[0..2] == "14") : chacun
        //    touche le sien chez lui (Wallet), sa 14 est a sa vraie place dans la liste recopiee ;
        //  - feuille des resultats (Sheets/IceRacePractice, TentContents/Results) : le tableau commun n'y est pas, il
        //    passe par message (Hud.Toast) et le journal ;
        //  - un invite parti en cours de semaine garde son numero dans la table (voiture qui ne se presente pas, comme un
        //    inscrit absent dans le jeu) jusqu'a la semaine suivante ;
        //  - locations (16/10, a verifier en jeu) : boites des lignes (MeshCollider des Checkpoint1/2 et StartFinish*) :
        //    verifier au journal que CP1, CP2 et l'arrivee sont vus a chaque tour d'une location (sinon elargir LineBounds) ;
        //    'Create grid' sur une location : pas d'erreur PlayMaker au journal du jeu, StartPositionID bien pose, voiture
        //    teleportee sur sa voie (PlaceRental) ; un joueur deja inscrit en CORRIS (autocollants poses) qui loue ensuite
        //    garde la CORRIS/DB active : ses tours en Rivett compteraient pour la 14 (a traiter si cela arrive) ; prix du
        //    podium pour un locataire : 'Get pos' avec son numero mis en 14 le temps de l'etat (PrizeSwap).
    }
}
