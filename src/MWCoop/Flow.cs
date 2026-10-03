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

        // Partie demandee par le salon du lanceur (une seule fois par lancement du jeu).
        static string LaunchChoice
        {
            get
            {
                if (launchUsed) return null;
                string p = Config.Get("Coop", "Partie", "").ToLowerInvariant();
                return p == "continuer" || p == "nouvelle" ? p : null;
            }
        }

        static bool AutoSkip
        {
            get { return Config.GetInt("Test", "PasserIntro", 0) != 0 || Config.HasArg("auto") || (Session.Active && !Session.IsHost) || LaunchChoice != null; }
        }

        public static void Update()
        {
            if (Time.realtimeSinceStartup < next) return;
            next = Time.realtimeSinceStartup + 0.5f;
            string level = Application.loadedLevelName;
            if (level != "MainMenu") { menuSince = -1; step = 0; pending = null; }
            if (level == "SplashScreen" && AutoSkip) Splash();
            else if (level == "Intro" && AutoSkip) Intro();
            else if (level == "MainMenu") Menu();
            if (level == "GAME") guestStarted = false;
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
            // Button :: SkipIntro, 'State 2' attend Echap/Entree -> LOAD -> LoadLevel GAME.
            PlayMakerFSM f = Game.FindFsm("Button", "SkipIntro");
            if (f != null && f.ActiveStateName == "State 2") { Log.Info("introduction passee"); f.SendEvent("LOAD"); }
        }

        static void Menu()
        {
            if (menuSince < 0) menuSince = Time.realtimeSinceStartup;
            if (pending == null) pending = Decide();
            if (pending == null || Time.realtimeSinceStartup - menuSince < 2f) return;
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
                return choice;
            }
            if (!SaveTransfer.IsolatedProfile) return null;   // les tests ne touchent qu'aux profils isoles
            if (Config.GetInt("Test", "Continuer", 0) != 0 && HasSave()) return "continuer";
            if (Config.GetInt("Test", "NouvellePartie", 0) != 0 || Config.GetInt("Test", "Continuer", 0) != 0) return "nouvelle";
            return null;
        }

        static bool HasSave()
        {
            return System.IO.File.Exists(System.IO.Path.Combine(SaveTransfer.SaveDir, "savefile.txt"));
        }

        static void Continue()
        {
            // ButtonContinue :: SetSize, DOWN -> Reset globals 2 -> Init Steam -> State 1 -> Load (GAME).
            // Le bouton n'est actif que si une sauvegarde existait a l'ouverture du menu.
            GameObject cont = Game.FindAny("Interface/Buttons/ButtonContinue");
            if (step == 0) { Log.Info("menu : continuer"); cont.SetActive(true); step = 1; return; }
            if (step == 1) { Game.SetState(Game.FsmOn(cont, "SetSize"), "Reset globals 2"); step = 9; }
        }

        static void NewGame()
        {
            if (step == 0)
            {
                // ButtonNewgame : active Licence (la carte du permis), masque Interface.
                Log.Info("menu : nouvelle partie");
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
                Game.SetState(Game.FsmOn(begin, "SetSize"), "State 2");
                step = 9;
            }
        }
    }
}
