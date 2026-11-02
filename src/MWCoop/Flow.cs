using MWCoop.Net;
using UnityEngine;

namespace MWCoop
{
    // Deroule des ecrans du jeu pilote par le mod : passer l'ouverture et l'introduction,
    // continuer ou commencer une partie depuis le menu (boutons du jeu, par leurs automates).
    // L'invite suit l'hote : des que l'hote est en partie et la sauvegarde recue, il entre en jeu.
    // Lancement groupe depuis le salon du lanceur : lancement.ini porte Partie=continuer|nouvelle ;
    // le jeu de l'hote (ou solo) passe alors l'ouverture et entre en partie tout seul, une fois.
    public static class Flow
    {
        static float next, menuSince = -1;
        static int step;
        static string pending;       // "continuer" | "nouvelle"
        static bool guestStarted;

        static bool launchUsed;

        // Invite : l'hote est en partie et la sauvegarde recue, le menu a ete lance (ecran d'attente).
        public static bool GuestJoining { get { return guestStarted; } }

        // Hote lance par le salon : "continuer" | "nouvelle" (ecran de demarrage) ; null sinon. Remis a null en jeu.
        public static string HostChoice;
        public static bool StartedNew;   // (nouvelle partie lancee par ce menu : la difficulte de depart s'applique au premier chargement, Difficulty)
        // Le bouton du menu a ete actionne : le jeu charge la partie.
        public static bool MenuDone { get { return step == 9; } }

        // Partie demandee par le salon du lanceur (une seule fois par lancement du jeu).
        static string LaunchChoice
        {
            get
            {
                if (launchUsed) return null;
                string p = Config.Get("Coop", "Partie", Config.Get("Test", "Partie", "")).ToLowerInvariant();   // ([Test] Partie : essais)
                return p == "continuer" || p == "nouvelle" ? p : null;
            }
        }

        // Le mod passe lui-meme les ecrans et entre en partie (invite, lancement par le salon, essais).
        public static bool AutoSkip
        {
            get { return Config.GetInt("Test", "PasserIntro", 0) != 0 || Config.HasArg("auto") || (Session.Active && !Session.IsHost) || LaunchChoice != null || MapExport.Active; }
        }

        public static void Update()
        {
            if (Time.realtimeSinceStartup < next) return;
            next = Time.realtimeSinceStartup + 0.5f;
            string level = Game.LevelName;
            if (level != "MainMenu") { menuSince = -1; step = 0; pending = null; }
            if (level == "SplashScreen" && AutoSkip) Splash();
            else if (level == "Intro" && AutoSkip) Intro();
            else if (level == "MainMenu") Menu();
            if (level == "GAME") { guestStarted = false; HostChoice = null; }
        }

        static void Splash()
        {
            // Logic :: FSM, 'State 2' = attente d'une touche (GetKeyDown) -> FINISHED.
            PlayMakerFSM f = Game.FindFsm("Logic", "FSM");
            if (f != null && f.ActiveStateName == "State 2") { Log.Info("ecran d'ouverture passe"); f.SendEvent("FINISHED"); }
            // Disclaimer/Button :: Button, transition globale SKIP -> LoadLevel (menu).
            PlayMakerFSM b = Game.FindFsm("Disclaimer/Button", "Button");
            if (b != null && b.ActiveStateName != "State 1") { Log.Info("avertissement accepte"); b.SendEvent("SKIP"); }
        }

        static void Intro()
        {
            // Button :: SkipIntro, 'State 2' attend Echap/Entree -> LOAD -> LoadLevel GAME (My Summer Car : Button :: FSM).
            PlayMakerFSM f = Game.FindFsm("Button", Game.Msc ? "FSM" : "SkipIntro");
            if (f != null && f.ActiveStateName == "State 2") { Log.Info("introduction passee"); f.SendEvent("LOAD"); }
        }

        static void Menu()
        {
            if (menuSince < 0) menuSince = Time.realtimeSinceStartup;
            HookMenuButtons();
            if (pending == null) pending = Decide();
            if (pending == null || Time.realtimeSinceStartup - menuSince < 2f) return;
            if (Contenu.MenuBusy) return;   // (import des musiques en cours : CD vides = CD absents de la partie)
            if (pending == "continuer") Continue(); else NewGame();
        }

        // Que faire au menu ? Invite : suivre l'hote. Tests : [Test] Continuer / NouvellePartie.
        static string Decide()
        {
            if (Session.Active && !Session.IsHost)
            {
                PlayerInfo host = Session.Host;
                if (guestStarted || host == null || host.Level != 1 || !SaveTransfer.Done) return null;
                if (SaveTransfer.HostHasSave && !SaveTransfer.Received) return null;   // ecriture refusee
                guestStarted = true;
                Log.Info("l'hote est en partie : on le rejoint");
                return SaveTransfer.Received ? "continuer" : "nouvelle";
            }
            string choice = LaunchChoice;
            if (choice != null)
            {
                launchUsed = true;
                if (choice == "continuer" && !HasSave()) choice = "nouvelle";
                Log.Info("salon : partie demandee par le lanceur : " + choice);
                if (Session.Active && Session.IsHost) HostChoice = choice;
                return choice;
            }
            if (!SaveTransfer.IsolatedProfile) return null;   // les tests ne touchent qu'aux profils isoles
            if (Config.GetInt("Test", "Continuer", 0) != 0 && HasSave()) return "continuer";
            if (Config.GetInt("Test", "NouvellePartie", 0) != 0 || Config.GetInt("Test", "Continuer", 0) != 0) return "nouvelle";
            return null;
        }

        // Hote qui clique LUI-MEME Continuer ou Commencer (apres « sauver et quitter », invites revenus au menu avec lui) :
        // seul Flow annoncait le chargement, et 20 a 40 s de chargement sans un paquet faisaient croire aux invites l'hote
        // parti (« delai depasse », transport recree, nouveau numero) -- audit MSC 02/11. Une action en tete des etats de
        // chargement des deux boutons l'annonce, clic a la main ou force par Flow (AnnounceLoading : une fois par image).
        // Accrochee quand le bouton est actif et son automate initialise (ButtonBegin n'apparait qu'avec le nom saisi).
        // [Test] AnnonceChargementMenu=0 : comme avant (My Winter Car : 0 par defaut, memes boutons, a verifier avant).
        static PlayMakerFSM hookedCont, hookedBegin;

        class LoadingHook : ModHook
        {
            public override string Module { get { return "Flow"; } }
            public override void OnEnter()
            {
                try { Session.AnnounceLoading(); }
                catch (System.Exception e) { Replay.HookError(e); }
                Finish();
            }
        }

        static void HookMenuButtons()
        {
            if (!Session.Active || !Session.IsHost) return;
            if (Config.GetInt("Test", "AnnonceChargementMenu", Game.Msc ? 1 : 0) == 0) return;
            HookButton("Interface/Buttons/ButtonContinue", "Reset globals 2", ref hookedCont);
            HookButton("Licence/Buttons/ButtonBegin", "State 2", ref hookedBegin);
        }

        static void HookButton(string path, string state, ref PlayMakerFSM done)
        {
            GameObject go = Game.FindAny(path);
            if (go == null || !go.activeInHierarchy) return;
            PlayMakerFSM f = Game.FsmOn(go, "SetSize");
            if (f == null || f == done || !f.Fsm.Initialized) return;
            HutongGames.PlayMaker.FsmState s = f.Fsm.GetState(state);
            done = f;   // (une fois par bouton et par menu charge : le menu suivant a d'autres objets)
            if (s == null) { Log.Warn("menu : etat '" + state + "' absent de " + path + "::SetSize, chargement non annonce au clic"); return; }
            FsmHooks.Prepend(s, new LoadingHook());
            Log.Info("menu : chargement annonce aux invites au clic de " + go.name);
        }

        // Essai (audit MSC 02/11) : boutons accroches dans ce menu.
        public static string TestMenuHooks()
        {
            HookMenuButtons();
            return "menu : continuer " + (hookedCont != null ? "accroche" : "non accroche") + ", commencer " + (hookedBegin != null ? "accroche" : "non accroche");
        }

        static bool HasSave()
        {
            return System.IO.File.Exists(System.IO.Path.Combine(SaveTransfer.SaveDir, Game.SaveFile));
        }

        static void Continue()
        {
            // ButtonContinue :: SetSize, DOWN -> Reset globals 2 -> Init Steam -> State 1 -> Load (GAME).
            // Le bouton n'est actif que si une sauvegarde existait a l'ouverture du menu.
            GameObject cont = Game.FindAny("Interface/Buttons/ButtonContinue");
            if (step == 0) { Log.Info("menu : continuer"); cont.SetActive(true); step = 1; return; }
            if (step == 1) { Session.AnnounceLoading(); Game.SetState(Game.FsmOn(cont, "SetSize"), "Reset globals 2"); step = 9; }   // (chargement de GAME : les invites tolerent le silence)
        }

        static void NewGame()
        {
            if (step == 0)
            {
                // ButtonNewgame : active Licence (la carte du permis), masque Interface.
                Log.Info("menu : nouvelle partie");
                StartedNew = true;
                Game.FindAny("Licence").SetActive(true);
                Game.FindAny("Interface").SetActive(false);
                // Licence/Buttons :: Data n'active ButtonBegin qu'avec un nom de famille saisi.
                Game.SetGlobal("PlayerFirstName", Session.Me != null ? Session.Me.Name : "Joueur");
                Game.SetGlobal("PlayerLastName", "Coop");
                Game.SetGlobalBool("PlayerPermaDeath", false);
                step = 1;
                return;
            }
            if (step == 1)
            {
                // ButtonBegin : State 2 -> Delete save -> Generate ID -> State 3 -> Load (Intro).
                GameObject begin = Game.FindAny("Licence/Buttons/ButtonBegin");
                if (!begin.activeInHierarchy) begin.SetActive(true);
                Session.AnnounceLoading();   // (chargement de l'intro puis de GAME : les invites tolerent le silence)
                Game.SetState(Game.FsmOn(begin, "SetSize"), "State 2");
                step = 9;
            }
        }
    }
}
