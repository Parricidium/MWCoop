using System.Collections.Generic;
using MWCoop.Net;
using UnityEngine;

namespace MWCoop
{
    // Ecran d'attente de l'invite : au menu principal (et a l'introduction que Flow passe), le menu du jeu
    // est recouvert par l'avancement (connexion, hote en partie, sauvegarde recue, chargement) et ses
    // boutons ne repondent plus : Flow fait entrer l'invite en partie quand tout est pret.
    // Les boutons du menu sont des MousePickEvent (raycast camera -> collider) : leurs colliders, sous
    // Interface, InterfaceActive et Licence, sont coupes tant que l'ecran est affiche, puis rendus.
    // Flow pilote les boutons par leurs automates (Game.SetState), sans clic : il n'est pas gene.
    // [Coop] EcranAttente=0 : sans. [Coop] Langue=fr|en, sinon la langue du systeme.
    public static class WaitScreen
    {
        public static bool Shown;            // affiche (dernier passage)
        public static int Current;           // etape en cours : 0 connexion, 1 hote, 2 sauvegarde, 3 chargement
        public static string Error = "";     // ligne d'erreur (vide : rien)
        static readonly string[] Roots = { "Interface", "InterfaceActive", "Licence" };
        static readonly List<Collider> blocked = new List<Collider>();
        static bool blocking;
        static float nextScan, lostSince = -1, nextTestLog;
        static string refusal = "", drop = "";
        static GUIStyle title, sub, step, stepOn, stepOff, detail, error, hint, button;
        static int fr = -1;

        static bool Enabled { get { return Config.GetInt("Coop", "EcranAttente", 1) != 0; } }

        static string L(string f, string e)
        {
            if (fr < 0)
            {
                string l = Config.Get("Coop", "Langue", "").ToLowerInvariant();
                fr = l == "fr" ? 1 : l == "en" ? 0 : Application.systemLanguage == SystemLanguage.French ? 1 : 0;
            }
            return fr == 1 ? f : e;
        }

        public static void OnLevelLoaded()
        {
            // Les colliders coupes appartenaient au niveau quitte : rien a rendre.
            blocked.Clear();
            blocking = false;
            nextScan = 0;
        }

        public static void Update()
        {
            string level = Application.loadedLevelName;
            float now = Time.realtimeSinceStartup;
            bool want = Session.Active && !Session.IsHost && Enabled && (level == "MainMenu" || level == "Intro");
            if (want != Shown)
            {
                Shown = want;
                if (want) Log.Info("ecran d'attente : affiche (invite)");
                else Log.Info("ecran d'attente : retire (" + (level == "GAME" ? "en jeu" : !Session.Active ? "session terminee" : !Enabled ? "desactive" : level) + ")");
            }
            if (want && level == "MainMenu") { if (!blocking && now >= nextScan) Block(now); }
            else if (blocking) Unblock();
            if (!want) return;

            // Connexion : refus et coupure gardes jusqu'a la prochaine connexion (Session reessaie et reecrit son etat).
            bool connected = Session.HostConnected;
            string st = Session.Status ?? "";
            if (st.StartsWith("refuse")) refusal = st.Substring(st.IndexOf(':') + 1).Trim();
            if (st.StartsWith("deconnecte")) drop = st;
            if (connected) { lostSince = -1; refusal = drop = ""; }
            else if (lostSince < 0) lostSince = now;

            PlayerInfo host = Session.Host;
            bool hostIn = host != null && host.Level == 1;
            if (Flow.GuestJoining || level == "Intro") Current = 3;
            else if (!connected) Current = 0;
            else if (!hostIn && !SaveTransfer.Receiving && !SaveTransfer.Done) Current = 1;
            else if (!SaveTransfer.Done) Current = 2;
            else Current = 3;

            Error = "";
            if (refusal.Length > 0) Error = L("Refusé par l'hôte : ", "Refused by the host: ") + refusal;
            else if (!connected && lostSince > 0 && now - lostSince >= 30f)
                Error = L("Aucune réponse de l'hôte depuis " + (int)(now - lostSince) + " s : vérifie l'adresse, et que le port UDP "
                          + Port + " est redirigé sur sa box (et autorisé par son pare-feu).",
                          "No answer from the host for " + (int)(now - lostSince) + " s: check the address, and that UDP port "
                          + Port + " is forwarded on the host's router (and allowed by the firewall).");
            else if (!connected && drop.Length > 0) Error = L("Connexion perdue, nouvel essai...", "Connection lost, retrying...");
            else if (SaveTransfer.Done && SaveTransfer.HostHasSave && !SaveTransfer.Received)
                Error = L("La sauvegarde de l'hôte n'a pas pu être écrite (voir le journal MWCoop) : lance le jeu depuis MWCoop.exe.",
                          "The host's save could not be written (see the MWCoop log): start the game from MWCoop.exe.");
        }

        static int Port { get { return Config.GetInt("Coop", "Port", 7870); } }

        // Coupe les colliders des boutons du menu (une fois par chargement du menu ; ceux deja coupes restent a part).
        static void Block(float now)
        {
            nextScan = now + 1f;
            int found = 0;
            foreach (string r in Roots)
            {
                GameObject go = Game.FindAny(r);
                if (go == null) continue;
                found++;
                foreach (Collider c in go.GetComponentsInChildren<Collider>(true))
                    if (c.enabled) { c.enabled = false; blocked.Add(c); }
            }
            if (found == 0) return;   // menu pas encore la : nouvel essai dans 1 s
            blocking = true;
            Log.Info("ecran d'attente : " + blocked.Count + " colliders du menu coupes");
        }

        static void Unblock()
        {
            int n = 0;
            foreach (Collider c in blocked) if (c != null) { c.enabled = true; n++; }
            if (n > 0) Log.Info("ecran d'attente : " + n + " colliders du menu rendus");
            blocked.Clear();
            blocking = false;
        }

        static string StepLabel(int i)
        {
            PlayerInfo host = Session.Host;
            string name = host != null && host.Name.Length > 0 && host.Name != "?" ? " (" + host.Name + ")" : "";
            switch (i)
            {
                case 0: return L("Connexion à l'hôte", "Connecting to the host");
                case 1: return L("En attente de l'hôte" + name + " : il crée ou charge la partie", "Waiting for the host" + name + " to create or load the game");
                case 2: return L("Réception de la sauvegarde", "Receiving the save");
                default: return L("Chargement de la partie", "Loading the game");
            }
        }

        static string StepDetail(int i)
        {
            switch (i)
            {
                case 0:
                    string addr = Config.Get("Coop", "Adresse", "127.0.0.1") + ":" + Port;
                    return Current == 0 ? addr + " (UDP)" : addr;
                case 1:
                    return Current == 1 ? L("L'hôte est au menu. Tu entreras en jeu avec lui.", "The host is at the menu. You will join when they start.") : "";
                case 2:
                    int got = SaveTransfer.BytesGot / 1024, exp = SaveTransfer.BytesExpected / 1024;
                    if (SaveTransfer.Receiving) return Mathf.FloorToInt(SaveTransfer.Progress * 100) + " %  (" + got + " / " + exp + L(" Ko)", " KB)");
                    if (SaveTransfer.Done)
                        return SaveTransfer.Received ? L("Reçue (" + exp + " Ko)", "Received (" + exp + " KB)")
                             : !SaveTransfer.HostHasSave ? L("Aucune : l'hôte commence une nouvelle partie", "None: the host starts a new game")
                             : L("Non écrite", "Not written");
                    return Current == 2 ? L("L'hôte prépare sa sauvegarde...", "The host is preparing the save...") : "";
                default:
                    return Current == 3 ? L("Le jeu se charge, encore un instant.", "The game is loading, one moment.") : "";
            }
        }

        static void Styles()
        {
            if (title != null) return;
            title = new GUIStyle(GUI.skin.label) { fontSize = 40, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            title.normal.textColor = Color.white;
            sub = new GUIStyle(GUI.skin.label) { fontSize = 15, alignment = TextAnchor.MiddleCenter };
            sub.normal.textColor = new Color(0.65f, 0.7f, 0.75f);
            step = new GUIStyle(GUI.skin.label) { fontSize = 18 };
            step.normal.textColor = new Color(0.8f, 0.85f, 0.8f);
            stepOn = new GUIStyle(step) { fontStyle = FontStyle.Bold };
            stepOn.normal.textColor = Color.white;
            stepOff = new GUIStyle(step);
            stepOff.normal.textColor = new Color(0.45f, 0.47f, 0.5f);
            detail = new GUIStyle(GUI.skin.label) { fontSize = 13, wordWrap = true };
            detail.normal.textColor = new Color(0.6f, 0.65f, 0.7f);
            error = new GUIStyle(GUI.skin.label) { fontSize = 14, wordWrap = true };
            error.normal.textColor = new Color(1f, 0.6f, 0.35f);
            hint = new GUIStyle(GUI.skin.label) { fontSize = 12, alignment = TextAnchor.MiddleLeft };
            hint.normal.textColor = new Color(0.5f, 0.53f, 0.56f);
            button = new GUIStyle(GUI.skin.button) { fontSize = 13 };
        }

        static void Fill(Rect r, Color c)
        {
            Color old = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = old;
        }

        public static void Draw()
        {
            if (!Shown) return;
            Styles();
            float now = Time.realtimeSinceStartup;
            Fill(new Rect(0, 0, Screen.width, Screen.height), new Color(0.05f, 0.06f, 0.07f, 0.97f));
            float w = Mathf.Min(640f, Screen.width - 40f), x = (Screen.width - w) / 2f;
            float y = Mathf.Max(16f, Screen.height * 0.5f - 230f);
            GUI.Label(new Rect(x, y, w, 50), "MWCoop", title); y += 52;
            GUI.Label(new Rect(x, y, w, 22), L("Partie coop : invité", "Co-op game: guest") + "   -   " + Version.Text, sub); y += 34;
            Fill(new Rect(x, y, w, 1), new Color(1, 1, 1, 0.12f)); y += 22;

            string dots = new string('.', 1 + (int)(now * 2.5f) % 3);
            for (int i = 0; i < 4; i++)
            {
                bool done = i < Current, cur = i == Current;
                Color mark = done ? new Color(0.35f, 0.8f, 0.45f)
                           : cur ? new Color(1f, 0.75f, 0.25f, 0.45f + 0.55f * Mathf.Abs(Mathf.Sin(now * 3f)))
                           : new Color(0.3f, 0.32f, 0.35f);
                Fill(new Rect(x + 2, y + 6, 14, 14), mark);
                GUI.Label(new Rect(x + 28, y, w - 28, 26), StepLabel(i) + (cur ? dots : ""), done ? step : cur ? stepOn : stepOff);
                y += 26;
                string d = i <= Current ? StepDetail(i) : "";
                if (d.Length > 0)
                {
                    float h = detail.CalcHeight(new GUIContent(d), w - 28);
                    GUI.Label(new Rect(x + 28, y, w - 28, h), d, detail);
                    y += h;
                }
                if (i == 2 && cur && SaveTransfer.Receiving)
                {
                    y += 4;
                    Fill(new Rect(x + 28, y, w - 28, 10), new Color(1, 1, 1, 0.1f));
                    Fill(new Rect(x + 28, y, (w - 28) * Mathf.Clamp01(SaveTransfer.Progress), 10), new Color(0.4f, 0.7f, 1f));
                    y += 12;
                }
                y += 12;
            }

            if (Error.Length > 0)
            {
                y += 4;
                float h = error.CalcHeight(new GUIContent(Error), w);
                GUI.Label(new Rect(x, y, w, h), Error, error);
                y += h + 8;
            }
            y += 14;
            Fill(new Rect(x, y, w, 1), new Color(1, 1, 1, 0.12f)); y += 12;
            // Echap quitte le jeu au menu (automate Quit du jeu) ; a l'introduction, il la passe.
            if (Application.loadedLevelName == "MainMenu")
                GUI.Label(new Rect(x, y, w - 170, 28), L("Échap : quitter le jeu", "Esc: quit the game"), hint);
            if (GUI.Button(new Rect(x + w - 160, y, 160, 28), L("Quitter le jeu", "Quit the game"), button))
            {
                Log.Info("ecran d'attente : quitter");
                Application.Quit();
            }
        }

        // Autotest "attente" : etat de l'ecran toutes les 2 s (menu compris : appele avant le filtre GAME).
        public static void Test(string mode, float t)
        {
            if (mode != "attente" || t < nextTestLog) return;
            nextTestLog = t + 2f;
            string s = Shown ? "etape " + (Current + 1) + " " + StepLog(Current) : "ecran cache (" + Application.loadedLevelName + ")";
            Log.Info("autotest : attente : " + s + ", sauvegarde " + Mathf.FloorToInt(SaveTransfer.Progress * 100) + " % ("
                     + SaveTransfer.BytesGot / 1024 + "/" + SaveTransfer.BytesExpected / 1024 + " Ko"
                     + (SaveTransfer.Done ? ", finie" + (SaveTransfer.Received ? ", ecrite" : SaveTransfer.HostHasSave ? ", NON ecrite" : ", vide") : "")
                     + "), clics bloques " + blocked.Count + " colliders" + (Error.Length > 0 ? ", erreur : " + Error : ""));
        }

        static string StepLog(int i)
        {
            switch (i)
            {
                case 0: return "connexion a l'hote";
                case 1: return "attente de l'hote";
                case 2: return "reception de la sauvegarde";
                default: return "chargement de la partie";
            }
        }
    }
}
