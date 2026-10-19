using System.Collections.Generic;
using UnityEngine;

namespace MWCoop
{
    // Acces aux objets du jeu.
    public static class Game
    {
        // Le jeu : My Summer Car (switch MSC / MWC du lanceur, 19/10, BETA) ou My Winter Car. Sur MSC, seuls les modules
        // generiques tournent (Core.MscModules) tant que les autres n'ont pas leur table de noms pour ce jeu.
        public static readonly bool Msc = Application.dataPath.EndsWith("mysummercar_Data", System.StringComparison.OrdinalIgnoreCase);
        public static string Name { get { return Msc ? "My Summer Car" : "My Winter Car"; } }
        // Version annoncee aux autres (bonjour en jeu, salon Steam) : "@msc" sur My Summer Car, comme le lanceur -- un joueur
        // de l'autre jeu est refuse comme une autre version.
        public static string ModVersion { get { return Msc ? Version.Text + "@msc" : Version.Text; } }

        // Sauvegarde en pleine partie. Le jeu ne sauve qu'en quittant : SAVEGAME envoie des centaines
        // d'automates (interrupteurs, objets, boutons...) dans un etat "Save" sans sortie. On note
        // l'etat de chaque automate qui ecoute SAVEGAME, on sauve, et 2,5 s plus tard (le temps que
        // les fichiers soient ecrits) on remet chacun dans son etat d'avant.
        // Appelee avant l'envoi de la sauvegarde a un invite qui arrive, et par la sauvegarde coop des
        // toilettes (SaveTransfer) : chez l'hote, et chez l'invite pour comparer les deux sauvegardes.
        // Les boutons des toilettes (SAVEGAME :: Button) n'ecoutent pas SAVEGAME : jamais touches ici.
        // Exception : Systems/Setup Game :: WaitPlayer (le chargement du joueur) ne s'arrete pas dans son etat
        // de sauvegarde. SAVEGAME -> "Save game" (SaveBool PlayerDead, SaveTransform PLAYER : sa place dans la
        // sauvegarde) -FINISHED-> Load game 2 -> Check dead (dans la meme image : PlayerSeated, PlayerHelmet,
        // PlayerInMenu, PlayerComputer = faux, CarVelocity et PlayerVelocity = 0) -> Load position -> Wait for
        // options load (1 s) -> Move player (SetRotation PLAYER x/z = 0 en monde, echelle 1, PlayerCurrentVehicle,
        // PlayerStop et PlayerSeated = faux, EngineTemp = AmbientTemperature, LoadSongs relance) -> 5 s -> Jailed?
        // -> Activate game : tout le chargement de la partie, que le jeu ne voit jamais (il charge le menu 0,4 s
        // apres SAVEGAME). Assis dans une voiture qui roule, le joueur retrouvait la marche ; la restauration
        // ci-dessous ne pouvait rien (a 2,5 s il est dans Move player, "parti ailleurs").
        // Pendant la diffusion, la sortie FINISHED de "Save game" vise donc un etat qui n'existe pas (le
        // DoTransition de PlayMaker ne fait alors rien) : il ecrit, puis reste dans "Save game" (fini, aucune
        // action par image), et n'est pas remis dans "Activate game" (ses ActivateGameObject seraient rejoues ;
        // aucune n'a resetOnExit ni everyFrame : l'y laisser ou non ne change rien d'autre). Seulement s'il est
        // au repos ("Activate game", ou "Save game" d'une sauvegarde precedente) : en plein chargement (debut de
        // partie, reapparition), la chaine doit aller au bout comme avant.
        static List<KeyValuePair<PlayMakerFSM, string>> restore;
        static Dictionary<PlayMakerFSM, string> afterSave;   // etat atteint par SAVEGAME
        static float restoreAt;
        public static bool Saving { get { return restore != null; } }
        const string HoldState = "(MWCoop : sauvegarde en jeu)";   // nom d'etat absent : transition sans effet

        public static void SaveInPlace()
        {
            if (restore != null) return;
            // Hors de la partie (menu) : rien a sauver, SAVEGAME n'y ecrirait qu'une sauvegarde vide.
            if (Application.loadedLevelName != "GAME") { Log.Warn("sauvegarde en jeu refusee : niveau " + Application.loadedLevelName); return; }
            restore = new List<KeyValuePair<PlayMakerFSM, string>>();
            PlayMakerFSM setup = null;
            foreach (PlayMakerFSM f in Object.FindObjectsOfType<PlayMakerFSM>())
            {
                string s = f.ActiveStateName;
                if (string.IsNullOrEmpty(s)) continue;
                bool listens = false;
                foreach (HutongGames.PlayMaker.FsmTransition t in f.Fsm.GlobalTransitions)
                    if (t.EventName == "SAVEGAME") { listens = true; break; }
                if (!listens) continue;
                if (setup == null && f.FsmName == "WaitPlayer" && f.gameObject.name == "Setup Game" && (s == "Activate game" || s == "Save game")) setup = f;
                else restore.Add(new KeyValuePair<PlayMakerFSM, string>(f, s));
            }
            HutongGames.PlayMaker.FsmTransition held = null;
            string heldTo = null;
            bool odd = false;
            if (setup != null)
            {
                HutongGames.PlayMaker.FsmState sg = setup.Fsm.GetState("Save game");
                if (sg != null)
                    foreach (HutongGames.PlayMaker.FsmTransition t in sg.Transitions)
                        if (t.EventName == "FINISHED") { held = t; heldTo = t.ToState; break; }
                // Forme inattendue (mise a jour du jeu) : comme les autres, remis dans son etat d'avant.
                if (held == null) { restore.Add(new KeyValuePair<PlayMakerFSM, string>(setup, setup.ActiveStateName)); setup = null; odd = true; }
                else held.ToState = HoldState;
            }
            try { PlayMakerFSM.BroadcastEvent("SAVEGAME"); }
            finally { if (held != null) held.ToState = heldTo; }
            afterSave = new Dictionary<PlayMakerFSM, string>();
            foreach (KeyValuePair<PlayMakerFSM, string> kv in restore) if (kv.Key != null) afterSave[kv.Key] = kv.Key.ActiveStateName;
            restoreAt = Time.realtimeSinceStartup + 2.5f;
            string wp = odd ? "chargement du joueur : \"Save game\" sans sortie FINISHED, traite comme les autres"
                      : setup == null ? "chargement du joueur absent ou pas au repos, traite comme les autres"
                      : setup.ActiveStateName == "Save game" ? "chargement du joueur arrete apres l'ecriture de sa place"
                      : "ATTENTION chargement du joueur parti dans " + setup.ActiveStateName;
            Log.Info("sauvegarde en jeu : " + restore.Count + " automates notes avant SAVEGAME ; " + wp);
        }

        public static void Update()
        {
            if (restore == null || Time.realtimeSinceStartup < restoreAt) return;
            int n = 0, kept = 0;
            foreach (KeyValuePair<PlayMakerFSM, string> kv in restore)
            {
                if (kv.Key == null || kv.Key.ActiveStateName == kv.Value) continue;
                // Etat d'avant terminal et sans action par image (« State 1 » du numero de Reijo : renomme, etape 2,
                // SELLCAR) : y revenir ne ferait que rejouer ses actions -- la vente de la Rivett relancee a chaque
                // sauvegarde (retour d'un joueur, 10/10). Rester dans l'etat de sauvegarde (terminal aussi) revient au meme.
                if (OneShot(kv.Key, kv.Value)) { kept++; continue; }
                // Parti ailleurs entre-temps (action d'un joueur rejouee pendant l'ecriture) : on le laisse.
                string saved;
                if (afterSave != null && afterSave.TryGetValue(kv.Key, out saved) && kv.Key.ActiveStateName != saved) continue;
                try { SetState(kv.Key, kv.Value); n++; }
                catch (System.Exception e) { Log.Warn("sauvegarde en jeu : " + kv.Key.name + " -> " + kv.Value + " : " + e.Message); }
            }
            restore = null; afterSave = null;
            Log.Info("sauvegarde en jeu : " + n + " automates remis dans leur etat d'avant" + (kept > 0 ? ", " + kept + " laisses (etat d'avant terminal : ses actions ne sont pas rejouees)" : ""));
        }

        // Etat sans transition ni action repetee a chaque image (une fois ses actions faites, il ne fait plus rien) et
        // qui envoie un evenement a d'autres automates (y revenir relancerait ce qu'il a declenche). Les autres sont remis.
        static bool OneShot(PlayMakerFSM f, string state)
        {
            HutongGames.PlayMaker.FsmState st = f.Fsm.GetState(state);
            if (st == null || st.Transitions == null || st.Transitions.Length > 0 || st.Actions == null) return false;
            bool sends = false;
            foreach (HutongGames.PlayMaker.FsmStateAction a in st.Actions)
            {
                if (a == null) continue;
                string tn = a.GetType().Name;
                if (tn.StartsWith("Send") || tn.StartsWith("Broadcast")) sends = true;
            }
            if (!sends) return false;
            if (st.Actions != null)
                foreach (HutongGames.PlayMaker.FsmStateAction a in st.Actions)
                {
                    if (a == null) continue;
                    foreach (string fn in new[] { "everyFrame", "everySecond", "perSecond" })
                    {
                        System.Reflection.FieldInfo fi = a.GetType().GetField(fn);
                        if (fi != null && fi.FieldType == typeof(bool) && (bool)fi.GetValue(a)) return false;
                    }
                }
            return true;
        }
        // Premier automate 'fsmName' porte par un objet actif nomme 'objectName'.
        public static PlayMakerFSM FindFsm(string objectName, string fsmName)
        {
            GameObject go = GameObject.Find(objectName);
            PlayMakerFSM f = go != null ? FsmOn(go, fsmName) : null;
            if (f != null || go == null) return f;
            // Un homonyme sans l'automate est passe devant (interface de MSCLoader : ses objets "Button" cachaient celui
            // du jeu, l'intro n'etait plus passee, 07/10) : l'automate lui-meme, sur un objet de ce nom et de ce chemin.
            string last = objectName.Substring(objectName.LastIndexOf('/') + 1);
            foreach (PlayMakerFSM x in Object.FindObjectsOfType<PlayMakerFSM>())
                if (x.FsmName == fsmName && x.gameObject.name == last && (objectName.IndexOf('/') < 0 || Recon.Path(x.transform).EndsWith(objectName))) return x;
            return null;
        }

        // Nom du niveau et camera principale, lus une fois par image (Application.loadedLevelName cree une chaine a chaque
        // appel, Camera.main cherche par etiquette ; des dizaines d'appels par image -- revue du 14/10). LevelChanged :
        // au chargement d'un niveau (Core.OnLevelWasLoaded), pour ne pas garder l'ancien nom jusqu'a l'image suivante.
        static string levelName; static int levelFrame = -1;
        public static string LevelName
        {
            get { int f = Time.frameCount; if (f != levelFrame || levelName == null) { levelFrame = f; levelName = Application.loadedLevelName; } return levelName; }
        }
        static Camera mainCam; static int mainCamFrame = -1;
        public static Camera MainCam
        {
            get { int f = Time.frameCount; if (f != mainCamFrame || mainCam == null) { mainCamFrame = f; mainCam = Camera.main; } return mainCam; }
        }
        public static void LevelChanged() { levelFrame = -1; mainCamFrame = -1; gBools.Clear(); gFloats.Clear(); }

        // Tous les automates (actifs ou non), releve partage par les modules qui balaient tout periodiquement : un seul
        // Resources.FindObjectsOfTypeAll toutes les 3 s au plus (70-90 ms chacun sur une grande scene ; une douzaine de
        // modules le faisaient chacun de leur cote). Un automate detruit depuis le releve vaut null (a sauter).
        static Object[] allFsms;
        static float allFsmsAt = -100f;
        static int allFsmsLevel = -1, allFsmsLogs;
        // maxAge : age maximal du releve (plus court pour un objet attendu tout juste apparu : piece achetee, Parts).
        public static Object[] AllFsms(float maxAge = 3f)
        {
            float now = Time.realtimeSinceStartup;
            if (allFsms == null || now - allFsmsAt > maxAge || now < allFsmsAt || allFsmsLevel != Application.loadedLevel)
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                allFsms = Resources.FindObjectsOfTypeAll(typeof(PlayMakerFSM)); allFsmsAt = now; allFsmsLevel = Application.loadedLevel;
                FsmScans++; FsmScanMs += sw.Elapsed.TotalMilliseconds;
                if (sw.ElapsedMilliseconds > 15 && allFsmsLogs++ < 5) Log.Info("automates : releve complet " + allFsms.Length + " en " + sw.ElapsedMilliseconds + " ms");
            }
            return allFsms;
        }

        // Automates d'un nom (index du releve AllFsms, refait avec lui) : les balayages qui ne gardent qu'un nom ("Use",
        // "Data", "Logic"...) ne parcourent plus les ~10 000 automates de la scene (refonte du 14/10, 22 balayages).
        static Object[] namedFrom;
        static Dictionary<string, List<Object>> named = new Dictionary<string, List<Object>>();
        static readonly List<Object> noneNamed = new List<Object>();
        public static List<Object> FsmsNamed(string name, float maxAge = 3f)
        {
            Object[] all = AllFsms(maxAge);
            if (!ReferenceEquals(all, namedFrom))
            {
                namedFrom = all;
                named = new Dictionary<string, List<Object>>();   // (listes neuves : une boucle en cours sur l'ancienne n'est pas derangee)
                foreach (Object o in all)
                {
                    var f = o as PlayMakerFSM;
                    if (f == null) continue;
                    string n = f.FsmName ?? "";
                    List<Object> l;
                    if (!named.TryGetValue(n, out l)) named[n] = l = new List<Object>();
                    l.Add(o);
                }
            }
            List<Object> r;
            return named.TryGetValue(name, out r) ? r : noneNamed;
        }

        // Racines de la scene par nom (Resources.FindObjectsOfTypeAll(GameObject) : 10-40 ms sur la scene du jeu, plus sur un
        // PC modeste -- les a-coups reguliers en coop, retour de joueurs 10/10). Gardees tant qu'elles restent des racines ;
        // nouveau releve seulement quand un chemin n'est pas trouve, au plus une fois par seconde, et pour un meme chemin
        // absent toutes les 5 s au plus (avant : toutes les 10 s quoi qu'il arrive, et a chaque image d'un chemin absent).
        static Dictionary<string, List<GameObject>> rootsByName;
        static int rootsFrame = -1, rootsLevel = -1;
        static float rootsAt = -100f;
        static readonly Dictionary<string, float> rootMiss = new Dictionary<string, float>();
        public static int RootScans, rootMissLogs, FsmScans; public static double FsmScanMs; public static double RootScanMs;

        static void ScanRoots()
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            rootsByName = new Dictionary<string, List<GameObject>>();
            foreach (GameObject root in Recon.SceneRoots())
            {
                List<GameObject> l;
                if (!rootsByName.TryGetValue(root.name, out l)) rootsByName[root.name] = l = new List<GameObject>(1);
                l.Add(root);
            }
            rootsFrame = Time.frameCount; rootsAt = Time.realtimeSinceStartup; rootsLevel = Application.loadedLevel;
            RootScans++; RootScanMs += sw.Elapsed.TotalMilliseconds;
        }

        // Racines de la scene (releve garde ; rescan=true : nouveau releve).
        public static HashSet<Transform> RootSet(bool rescan)
        {
            if (rescan || rootsByName == null || rootsLevel != Application.loadedLevel) ScanRoots();
            var set = new HashSet<Transform>();
            foreach (List<GameObject> l in rootsByName.Values)
                foreach (GameObject g in l) if (g != null && g.transform.parent == null) set.Add(g.transform);
            return set;
        }

        static bool HasRoot(string rootName)
        {
            List<GameObject> l;
            if (!rootsByName.TryGetValue(rootName, out l)) return false;
            foreach (GameObject g in l) if (g != null && g.transform.parent == null) return true;
            return false;
        }

        static GameObject FindIn(string rootName, string rest)
        {
            List<GameObject> l;
            if (!rootsByName.TryGetValue(rootName, out l)) return null;
            foreach (GameObject root in l)
            {
                if (root == null) continue;
                Transform t = root.transform;
                if (t.parent != null) continue;   // (plus une racine)
                if (rest != null) t = t.Find(rest);
                if (t != null) return t.gameObject;
            }
            return null;
        }

        // Objet par chemin "Racine/Enfant/...", actif ou non (GameObject.Find ne voit que les actifs).
        // Joueur local (PLAYER). Assis dans une voiture, il est range sous le siege : sa racine est alors la voiture, les
        // tests "root.name == PLAYER" ne le reconnaissaient plus (ses automates et objets passaient pour ceux de la voiture,
        // GameObject.Find("PLAYER/...") ne trouvait plus rien).
        static Transform playerT;
        static int playerLookFrame = -1;
        public static Transform PlayerT
        {
            get
            {
                if (playerT == null && Time.frameCount != playerLookFrame)   // (absent : cherche au plus une fois par image)
                {
                    playerLookFrame = Time.frameCount;
                    GameObject g = GameObject.Find("PLAYER");
                    playerT = g != null && g.transform.Find("Pivot") != null ? g.transform : null;
                    // (un autre objet nomme PLAYER trouve d'abord : remonte depuis la camera du joueur)
                    for (Transform t = playerT == null ? PlayerSync.LocalCamera : null; t != null; t = t.parent)
                        if (t.name == "PLAYER" && t.Find("Pivot") != null) { playerT = t; break; }
                }
                return playerT;
            }
        }

        // Objet PLAYER (GameObject.Find("PLAYER") a chaque appel avant, 80 endroits : revue du 14/10)
        public static GameObject PlayerGo { get { Transform t = PlayerT; return t != null ? t.gameObject : null; } }

        // Racine "logique" de t : "PLAYER" pour tout ce qui est sous le joueur, meme assis ; sinon sa vraie racine.
        public static string RootName(Transform t)
        {
            if (t == null) return "";
            Transform r = t.root;
            if (r.name == "PLAYER") return "PLAYER";
            Transform p = PlayerT;
            return p != null && p.parent != null && t.IsChildOf(p) ? "PLAYER" : r.name;
        }

        public static bool UnderPlayer(Transform t) { return RootName(t) == "PLAYER"; }

        // Objet sous le joueur par son chemin relatif ("Pivot/AnimPivot/..."), assis ou non.
        public static GameObject PlayerPart(string rel)
        {
            Transform p = PlayerT;
            Transform t = p != null ? p.Find(rel) : null;
            return t != null ? t.gameObject : null;
        }

        public static GameObject FindAny(string path)
        {
            int slash = path.IndexOf('/');
            string rootName = slash < 0 ? path : path.Substring(0, slash), rest = slash < 0 ? null : path.Substring(slash + 1);
            if (rootsByName == null || rootsLevel != Application.loadedLevel) { rootMiss.Clear(); rootMissLogs = 0; ScanRoots(); }
            GameObject g = FindIn(rootName, rest);
            if (g != null) return g;
            // Absent : racine creee (ou remplacee) depuis le releve ? Seulement si la racine elle-meme manque : racine la mais
            // objet absent dessous (un PNJ pas encore cree, JOBS/HouseWood1/.../WoodCaller1), un nouveau releve n'y change
            // rien -- il revenait toutes les 5 s, 24 releves par minute (journaux de HowardCS, 11/10).
            if (HasRoot(rootName)) return null;
            float now = Time.realtimeSinceStartup, last;
            if (rootsFrame == Time.frameCount || now - rootsAt < 1f) return null;
            if (rootMiss.TryGetValue(path, out last) && now - last < 5f) return null;
            rootMiss[path] = now;
            ScanRoots();
            g = FindIn(rootName, rest);
            if (g == null && rootMissLogs++ < 15) Log.Info("objets : " + path + " introuvable (racines relevees de nouveau ; au plus toutes les 5 s)");
            return g;
        }

        public static void SetGlobal(string name, string value)
        {
            var v = HutongGames.PlayMaker.FsmVariables.GlobalVariables.FindFsmString(name);
            if (v != null) v.Value = value; else Log.Warn("globale texte absente : " + name);
        }

        // Globales du jeu par nom, gardees (FindFsmBool parcourt toute la liste des globales a chaque appel ; une
        // quarantaine d'appels, plusieurs par image -- refonte du 14/10). Les globales restent les memes objets toute la partie.
        static readonly Dictionary<string, HutongGames.PlayMaker.FsmBool> gBools = new Dictionary<string, HutongGames.PlayMaker.FsmBool>();
        static readonly Dictionary<string, HutongGames.PlayMaker.FsmFloat> gFloats = new Dictionary<string, HutongGames.PlayMaker.FsmFloat>();
        static HutongGames.PlayMaker.FsmBool GBool(string name)
        {
            HutongGames.PlayMaker.FsmBool v;
            if (gBools.TryGetValue(name, out v) && v != null) return v;
            v = HutongGames.PlayMaker.FsmVariables.GlobalVariables.FindFsmBool(name);
            if (v != null) gBools[name] = v;
            return v;
        }
        static HutongGames.PlayMaker.FsmFloat GFloat(string name)
        {
            HutongGames.PlayMaker.FsmFloat v;
            if (gFloats.TryGetValue(name, out v) && v != null) return v;
            v = HutongGames.PlayMaker.FsmVariables.GlobalVariables.FindFsmFloat(name);
            if (v != null) gFloats[name] = v;
            return v;
        }

        public static bool GlobalBool(string name)
        {
            var v = GBool(name);
            return v != null && v.Value;
        }

        public static void SetGlobalBool(string name, bool value)
        {
            var v = GBool(name);
            if (v != null) v.Value = value; else Log.Warn("globale booleenne absente : " + name);
        }

        public static float GlobalFloat(string name)
        {
            var v = GFloat(name);
            return v != null ? v.Value : -1f;
        }

        public static void SetGlobalFloat(string name, float value)
        {
            var v = GFloat(name);
            if (v != null) v.Value = value; else Log.Warn("globale flottante absente : " + name);
        }

        static System.Reflection.MethodInfo switchState;

        // Force l'etat d'un automate (Fsm.SwitchState est prive dans cette version de PlayMaker).
        public static bool SetState(PlayMakerFSM f, string state)
        {
            if (f == null) return false;
            HutongGames.PlayMaker.FsmState s = f.Fsm.GetState(state);
            if (s == null) { Log.Warn("etat '" + state + "' absent de " + f.FsmName); return false; }
            if (switchState == null)
                switchState = typeof(HutongGames.PlayMaker.Fsm).GetMethod("SwitchState",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            switchState.Invoke(f.Fsm, new object[] { s });
            return true;
        }

        // Variable propre a l'automate (pas une globale). FsmVariables.GetVariable cherche aussi dans les globales :
        // un test "GetVariable(n) == null" ne reconnait jamais PlayerMoney, PlayerStop, GUI*... (vu au bar : l'invite
        // payait la biere de l'hote).
        public static bool LocalVar(PlayMakerFSM f, string name)
        {
            foreach (HutongGames.PlayMaker.NamedVariable v in f.FsmVariables.GetAllNamedVariables()) if (v.Name == name) return true;
            return false;
        }

        public static PlayMakerFSM FsmOn(GameObject go, string fsmName)
        {
            foreach (PlayMakerFSM f in go.GetComponents<PlayMakerFSM>())
                if (f.FsmName == fsmName) return f;
            return null;
        }
    }
}
