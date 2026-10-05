using System.Collections.Generic;
using UnityEngine;

namespace MWCoop
{
    // Acces aux objets du jeu.
    public static class Game
    {
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
            int n = 0;
            foreach (KeyValuePair<PlayMakerFSM, string> kv in restore)
            {
                if (kv.Key == null || kv.Key.ActiveStateName == kv.Value) continue;
                // Parti ailleurs entre-temps (action d'un joueur rejouee pendant l'ecriture) : on le laisse.
                string saved;
                if (afterSave != null && afterSave.TryGetValue(kv.Key, out saved) && kv.Key.ActiveStateName != saved) continue;
                try { SetState(kv.Key, kv.Value); n++; }
                catch (System.Exception e) { Log.Warn("sauvegarde en jeu : " + kv.Key.name + " -> " + kv.Value + " : " + e.Message); }
            }
            restore = null; afterSave = null;
            Log.Info("sauvegarde en jeu : " + n + " automates remis dans leur etat d'avant");
        }
        // Premier automate 'fsmName' porte par un objet actif nomme 'objectName'.
        public static PlayMakerFSM FindFsm(string objectName, string fsmName)
        {
            GameObject go = GameObject.Find(objectName);
            return go != null ? FsmOn(go, fsmName) : null;
        }

        static List<GameObject> roots;
        static int rootsFrame = -1;

        // Objet par chemin "Racine/Enfant/...", actif ou non (GameObject.Find ne voit que les actifs).
        public static GameObject FindAny(string path)
        {
            string[] parts = path.Split('/');
            // Racines relevees une fois par image (un message peut demander des dizaines d'objets).
            if (rootsFrame != Time.frameCount || roots == null) { roots = Recon.SceneRoots(); rootsFrame = Time.frameCount; }
            foreach (GameObject root in roots)
            {
                if (root == null || root.name != parts[0]) continue;
                Transform t = root.transform;
                for (int i = 1; i < parts.Length && t != null; i++) t = t.Find(parts[i]);
                if (t != null) return t.gameObject;
            }
            return null;
        }

        public static void SetGlobal(string name, string value)
        {
            var v = HutongGames.PlayMaker.FsmVariables.GlobalVariables.FindFsmString(name);
            if (v != null) v.Value = value; else Log.Warn("globale texte absente : " + name);
        }

        public static bool GlobalBool(string name)
        {
            var v = HutongGames.PlayMaker.FsmVariables.GlobalVariables.FindFsmBool(name);
            return v != null && v.Value;
        }

        public static void SetGlobalBool(string name, bool value)
        {
            var v = HutongGames.PlayMaker.FsmVariables.GlobalVariables.FindFsmBool(name);
            if (v != null) v.Value = value; else Log.Warn("globale booleenne absente : " + name);
        }

        public static float GlobalFloat(string name)
        {
            var v = HutongGames.PlayMaker.FsmVariables.GlobalVariables.FindFsmFloat(name);
            return v != null ? v.Value : -1f;
        }

        public static void SetGlobalFloat(string name, float value)
        {
            var v = HutongGames.PlayMaker.FsmVariables.GlobalVariables.FindFsmFloat(name);
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
