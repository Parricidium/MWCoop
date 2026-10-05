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

        static bool Enabled { get { return Config.GetInt("Coop", "EcranAttente", 1) != 0; } }

        static string L(string f, string e) { return Lang.T(f, e); }

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

        static float cardH;

        // Carte verre au centre (le menu du jeu flou derriere) : titre, etapes numerotees reliees (faite : vert,
        // en cours : accent qui pulse), detail et jauge de la sauvegarde, erreur encadree, bouton Quitter.
        public static void Draw()
        {
            // [Test] AttenteForcee=etape (1-4) : l'ecran par-dessus la partie, pour le photographier (la capture
            // d'ecran du jeu ne s'ecrit pas au menu principal) ; jauge a 60 % a l'etape 3, [Test] AttenteErreur=texte.
            int forced = Application.loadedLevelName == "GAME" ? Config.GetInt("Test", "AttenteForcee", 0) : 0;
            if (!Shown && forced == 0) return;
            if (forced > 0) { Current = Mathf.Clamp(forced - 1, 0, 3); Error = Config.Get("Test", "AttenteErreur", ""); }
            float now = Time.realtimeSinceStartup;
            Style.Fill(new Rect(0, 0, Screen.width, Screen.height), new Color(0.02f, 0.04f, 0.08f, 0.55f));
            float w = Mathf.Min(Style.Px(700), Screen.width - Style.Px(40)), pad = Style.Px(34), iw = w - 2 * pad;
            float h = cardH > 0 ? cardH : Style.Px(520);
            float x = Mathf.Round((Screen.width - w) / 2f), y0 = Mathf.Round(Mathf.Max(Style.Px(16), (Screen.height - h) / 2f));
            float one = Mathf.Max(1f, Style.Px(1));
            Style.Glass(new Rect(x, y0, w, h), Style.Px(24));

            float y = y0 + Style.Px(24);
            var head = new Rect(x + pad, y, iw, Style.Px(54));
            Style.Title(head, "MWCoop", Style.White, TextAnchor.MiddleLeft, 42);
            string badge = L("INVITÉ", "GUEST");
            float bw = Style.Width(badge, 14) + Style.Px(22), bh = Style.Px(26);
            var br = new Rect(head.x + Style.TitleWidth("MWCoop", 42) + Style.Px(14), head.center.y - bh / 2 + Style.Px(2), bw, bh);
            Style.Round(br, bh / 2, new Color(Style.Accent.r, Style.Accent.g, Style.Accent.b, 0.18f));
            Style.Ring(br, bh / 2, new Color(Style.Accent.r, Style.Accent.g, Style.Accent.b, 0.5f));
            Style.Text(br, badge, 14, TextAnchor.MiddleCenter, Style.Accent);
            Style.Text(head, Version.Text, 16, TextAnchor.MiddleRight, Style.Dim, false);
            y += Style.Px(56);
            Style.Text(new Rect(x + pad, y, iw, Style.Px(24)), L("Partie coop : tu rejoins la partie de l'hôte", "Co-op game: you are joining the host's game"), 17, TextAnchor.MiddleLeft, Style.Dim, false);
            y += Style.Px(38);
            Style.Fill(new Rect(x + pad, y, iw, one), Style.Line);
            y += Style.Px(24);

            string dots = new string('.', 1 + (int)(now * 2.5f) % 3);
            float dot = Style.Px(28), tx = x + pad + dot + Style.Px(16), tw = iw - dot - Style.Px(16);
            float prevBottom = -1;
            for (int i = 0; i < 4; i++)
            {
                bool done = i < Current, cur = i == Current;
                var dr = new Rect(x + pad, y, dot, dot);
                if (prevBottom >= 0)
                    Style.Round(new Rect(dr.center.x - one, prevBottom + Style.Px(5), 2 * one, Mathf.Max(0, dr.y - prevBottom - Style.Px(10))), one,
                                done || cur ? new Color(Style.Good.r, Style.Good.g, Style.Good.b, 0.55f) : Style.Line);
                if (done)
                {
                    Style.Round(dr, dot / 2, Style.Good);
                    Style.Text(dr, (i + 1).ToString(), 15, TextAnchor.MiddleCenter, Style.OnAccent);
                }
                else if (cur)
                {
                    float pulse = (now * 0.9f) % 1f;
                    float grow = pulse * Style.Px(9);
                    Style.Ring(new Rect(dr.x - grow, dr.y - grow, dr.width + 2 * grow, dr.height + 2 * grow), dot / 2 + grow,
                               new Color(Style.Accent.r, Style.Accent.g, Style.Accent.b, 0.8f * (1f - pulse)));
                    Style.Round(dr, dot / 2, Style.Accent);
                    Style.Text(dr, (i + 1).ToString(), 15, TextAnchor.MiddleCenter, Style.OnAccent);
                }
                else
                {
                    Style.Ring(dr, dot / 2, new Color(Style.Dim.r, Style.Dim.g, Style.Dim.b, 0.6f));
                    Style.Text(dr, (i + 1).ToString(), 15, TextAnchor.MiddleCenter, Style.Dim);
                }
                prevBottom = dr.yMax;
                Style.Text(new Rect(tx, y, tw, dot), StepLabel(i) + (cur ? dots : ""), 20, TextAnchor.MiddleLeft,
                           cur ? Style.White : done ? new Color(0.82f, 0.88f, 0.92f) : Style.Dim, cur);
                y += dot + Style.Px(2);
                string d = i <= Current ? StepDetail(i) : "";
                if (d.Length > 0)
                {
                    float dh = Style.Height(d, 15, tw, false);
                    Style.Text(new Rect(tx, y, tw, dh), d, 15, TextAnchor.UpperLeft, Style.Dim, false, true);
                    y += dh;
                }
                if (i == 2 && cur && (SaveTransfer.Receiving || forced > 0))
                {
                    y += Style.Px(6);
                    Style.Bar(new Rect(tx, y, tw, Style.Px(8)), forced > 0 ? 0.6f : SaveTransfer.Progress, Style.Accent);
                    y += Style.Px(10);
                }
                y += Style.Px(18);
            }

            if (Error.Length > 0)
            {
                float eh = Style.Height(Error, 15, iw - Style.Px(32), false) + Style.Px(22);
                var er = new Rect(x + pad, y, iw, eh);
                Style.Round(er, Style.Px(12), new Color(Style.Warn.r, Style.Warn.g, Style.Warn.b, 0.12f));
                Style.Ring(er, Style.Px(12), new Color(Style.Warn.r, Style.Warn.g, Style.Warn.b, 0.5f));
                Style.Text(new Rect(er.x + Style.Px(16), er.y + Style.Px(11), er.width - Style.Px(32), eh - Style.Px(22)), Error, 15, TextAnchor.UpperLeft, Style.Warn, false, true);
                y += eh + Style.Px(16);
            }
            Style.Fill(new Rect(x + pad, y, iw, one), Style.Line);
            y += Style.Px(18);
            float bh2 = Style.Px(42), bw2 = Style.Px(190);
            // Echap quitte le jeu au menu (automate Quit du jeu) ; a l'introduction, il la passe.
            if (Application.loadedLevelName == "MainMenu")
                Style.Text(new Rect(x + pad, y, iw - bw2 - Style.Px(12), bh2), L("Échap : quitter le jeu", "Esc: quit the game"), 15, TextAnchor.MiddleLeft, Style.Dim, false);
            if (Style.Button(new Rect(x + w - pad - bw2, y, bw2, bh2), L("Quitter le jeu", "Quit the game"), false, 17))
            {
                Log.Info("ecran d'attente : quitter");
                Application.Quit();
            }
            y += bh2 + Style.Px(26);
            if (Event.current.type == EventType.Repaint) cardH = y - y0;
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
