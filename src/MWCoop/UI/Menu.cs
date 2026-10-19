using System.Collections.Generic;
using MWCoop.Net;
using UnityEngine;

namespace MWCoop
{
    // F10 : menu coop facon GTA (joueurs, apparence, tchat, synchro). T : ecrire dans le tchat. Echap : fermer.
    // Clavier (evenements OnGUI consommes, jamais de lecture Input en plus de F10/Echap/T) : Haut/Bas lignes,
    // Gauche/Droite valeur (ou onglet), Entree valider, Tab ou Q/E onglet. Souris : survol, clic, molette.
    // Tant qu'il est ouvert, le jeu ne recoit plus la souris ni les deplacements.
    public static class Menu
    {
        public static bool Open, ChatOpen;
        static int tab;
        static string chatText = "";
        static List<string> skins;
        static readonly List<string> skinLabels = new List<string>();
        static readonly List<Behaviour> blocked = new List<Behaviour>();
        static bool focusChat, testDone;
        // Onglets (refonte du 18/10) : barre a gauche en trois groupes (PARTIE, MOI, AVANCE), icones assets/menu.
        static readonly string[] tabsFr = { "Joueurs", "Tchat", "Course sur glace", "Rallye", "VAZ-2108", "Apparence", "HUD", "Graphismes", "Synchro" },
                                 tabsEn = { "Players", "Chat", "Ice race", "Rally", "VAZ-2108", "Appearance", "HUD", "Graphics", "Sync" };
        static readonly string[] tabIcons = { "joueurs", "tchat", "course", "course", "voiture", "apparence", "hud", "graphismes", "synchro" };
        static readonly int[] tabGroup = { 0, 0, 0, 0, 0, 1, 1, 1, 2 };
        static readonly string[] groupsFr = { "PARTIE", "MOI", "AVANC\u00C9" }, groupsEn = { "GAME", "ME", "ADVANCED" };
        // My Summer Car (19/10) : Dragsters a la place de la course sur glace, le rallye du jeu, pas d'onglet VAZ (MscEvents).
        static readonly string[] tabsMscFr = { "Joueurs", "Tchat", "Dragsters", "Rallye", "VAZ-2108", "Apparence", "HUD", "Graphismes", "Synchro" },
                                 tabsMscEn = { "Players", "Chat", "Drag race", "Rally", "VAZ-2108", "Appearance", "HUD", "Graphics", "Sync" };
        static string[] tabs { get { return Game.Msc ? Lang.A(tabsMscFr, tabsMscEn) : Lang.A(tabsFr, tabsEn); } }
        static bool TabHidden(int i) { return Game.Msc && i == TabVaz; }
        static int StepTab(int from, int d) { int n = tabsFr.Length, i = from; do i = (i + d + n) % n; while (TabHidden(i) && i != from); return i; }
        public const int TabPlayers = 0, TabChat = 1, TabRace = 2, TabRally = 3, TabVaz = 4, TabLook = 5, TabHud = 6, TabGfx = 7, TabSync = 8;   // (Rallye : 18/10)
        public static int Tab { get { return tab; } }
        const string ChatField = "mwcoop-chat2";
        const string Sep = "  \u00B7  ";
        static string TabsHint { get { return Lang.T("Haut/Bas : changer d'onglet", "Up/Down: switch tab") + Sep + Lang.T("Droite ou Entr\u00E9e : entrer dans la liste", "Right or Enter: go to the list"); } }
        // Navigation : ligne choisie (-1 = bandeau des onglets), defilement, touches du passage OnGUI en cours.
        static int sel, items;
        static float scroll, contentH, viewH, cw, cy, selY0, selY1;
        static int idx, lines;
        static bool follow, kEnter, mouseMoved, typing, fieldFocused, wasOpen;
        static int kDir;
        static Vector2 lastMouse;
        static string hint;

        static readonly Color rowText = Kit.Soft;
        static readonly Color line = Style.Line;
        static readonly Color selFill = new Color(Style.Accent.r, Style.Accent.g, Style.Accent.b, 0.20f);
        static readonly Color selEdge = new Color(Style.Accent.r, Style.Accent.g, Style.Accent.b, 0.60f);
        static readonly Color trackBg = new Color(1f, 1f, 1f, 0.06f);
        static readonly Color hoverBg = new Color(1f, 1f, 1f, 0.07f);
        static float openedAt;

        public static void Update()
        {
            bool inGame = PlayerSync.InGame;
            int test = Config.GetInt("Test", "TestMenu", 0);   // essais : menu ouvert sur l'onglet test-1
            if (test > 0 && inGame && !testDone && Time.timeSinceLevelLoad > 8f) { testDone = true; Open = true; tab = test - 1; }
            if (Input.GetKeyDown(KeyCode.F10)) { Open = !Open; ChatOpen = false; WalletPanel.Open = false; CheatPanel.Open = false; MapPanel.Open = false; }
            else if (Input.GetKeyDown(KeyCode.Escape) && (Open || ChatOpen)) { Open = ChatOpen = false; }
            else if (inGame && !Open && !ChatOpen && !WalletPanel.Open && !CheatPanel.Open && !MapPanel.Open && !Kauppa.MontageOpen && Session.Active && Input.GetKeyDown(Keys.Chat))   // (touche reglable dans le lanceur)
            {
                ChatOpen = true;
                focusChat = true;
                chatText = "";
            }
            bool panel = Open || ChatOpen || Respawn.Choosing || WalletPanel.Open || CheatPanel.Open || MapPanel.Open || Kauppa.MontageOpen;
            // Menu pause du jeu ouvert : rien de bloque. Son automate 'Update Cursor' doit voir l'aller-retour du menu (il rend
            // a la camera du joueur l'etiquette MainCamera que le menu donne a sa camera CAM) ; bloque, le menu referme laissait
            // le jeu sans camera principale (OpNoob, 0.75.50 : apres la camera drone, Echap, plus aucun clic -- erreur
            // WherePlayerIsLooking a chaque image, automate fige, MousePick sans camera).
            if (gameMenu == null) gameMenu = HutongGames.PlayMaker.FsmVariables.GlobalVariables.FindFsmBool("PlayerInMenu");
            bool inGameMenu = gameMenu != null && gameMenu.Value;
            Block(inGame && !inGameMenu && (panel || DroneCam.Active), panel);   // (camera drone : joueur immobile, curseur garde par le jeu)
            if (inGame && !inGameMenu) MainCameraGuard();
        }
        static HutongGames.PlayMaker.FsmBool gameMenu;

        // Ouvre le menu sur un onglet, la liste choisie (inscription a la course sur glace : JokkisPanel ; autotests).
        public static void Show(int t)
        {
            WalletPanel.Open = CheatPanel.Open = MapPanel.Open = false;
            ChatOpen = false;
            Open = true;
            if (t != tab) { tab = t; scroll = 0; follow = true; }
            if (wasOpen) sel = 0;
        }

        public static void Hide() { Open = false; }

        static float nextCamCheck;
        static int camFixes;
        static bool camLost;
        // Garde-fou : en partie, hors menu du jeu, plus aucune camera principale (Game.MainCam nul) -- la camera du joueur la
        // redevient (repare aussi une partie deja bloquee).
        static void MainCameraGuard()
        {
            float now = Time.realtimeSinceStartup;
            if (now < nextCamCheck) return;
            nextCamCheck = now + 0.5f;
            // [Test] CameraPerdue=<s> : a ce temps de jeu, la camera du joueur perd son etiquette (comme apres le drone + Echap).
            int lose = Config.GetInt("Test", "CameraPerdue", 0);
            if (lose > 0 && !camLost && Time.timeSinceLevelLoad > lose && PlayerSync.LocalCamera != null)
            { camLost = true; PlayerSync.LocalCamera.gameObject.tag = "Untagged"; Log.Info("autotest : camera du joueur sans etiquette, Game.MainCam " + (Game.MainCam != null ? Game.MainCam.name : "nul")); }
            if (Game.MainCam != null) return;
            Transform c = PlayerSync.LocalCamera;
            if (c == null || c.GetComponent<Camera>() == null || !c.gameObject.activeInHierarchy) return;
            c.gameObject.tag = "MainCamera";
            if (camFixes++ < 5) Log.Warn("interface : plus de camera principale (menu du jeu referme pendant un blocage ?) -- " + c.name + " la redevient");
        }

        // Curseur du mod (Core le dessine en dernier) : menu ouvert ou choix de reapparition, en partie.
        // [Test] CurseurTest=x,y : dessine a ce point sans lire la souris (captures des instances d'essai).
        public static bool CursorWanted { get { return PlayerSync.InGame && (Open || Respawn.Choosing || WalletPanel.Open || CheatPanel.Open || MapPanel.Open || Kauppa.MontageOpen); } }

        public static Vector2 CursorPos()
        {
            string t = Config.Get("Test", "CurseurTest", "");
            if (t.Length > 0)
            {
                string[] p = t.Split(',');
                float cx, cy2;
                if (p.Length == 2 && float.TryParse(p[0], out cx) && float.TryParse(p[1], out cy2)) return new Vector2(cx, cy2);
            }
            return Event.current.mousePosition;
        }

        // Coupe/rend la visee et les deplacements du joueur (et le verrouillage du curseur du jeu).
        static void Block(bool on, bool freeCursor)
        {
            if (on && blocked.Count == 0)
            {
                GameObject p = Game.PlayerGo;
                if (p == null) return;
                var list = new List<Behaviour>();
                list.AddRange(p.GetComponents<Behaviour>());
                Transform cam = p.transform.Find("Pivot/AnimPivot/Camera/FPSCamera");
                if (cam != null) list.AddRange(cam.GetComponents<Behaviour>());
                foreach (Behaviour b in list)
                {
                    string n = b.GetType().Name;
                    bool fsm = b is PlayMakerFSM && ((PlayMakerFSM)b).FsmName == "Update Cursor";
                    if ((n.Contains("MouseLook") || n == "FPSInputController" || fsm) && b.enabled)
                    {
                        b.enabled = false;
                        blocked.Add(b);
                    }
                }
            }
            else if (!on && blocked.Count > 0)
            {
                foreach (Behaviour b in blocked) if (b != null) b.enabled = true;
                blocked.Clear();
            }
            // Curseur libere ; le jeu le laisse invisible : Core dessine le notre (Style.DrawCursor) par-dessus.
            if (on && freeCursor) { Screen.lockCursor = false; Cursor.visible = false; }
            else if (on && !Screen.lockCursor) Screen.lockCursor = true;   // (camera drone, menu referme : la souris revient au drone)
        }

        public static void Draw()
        {
            if (ChatOpen && testKey != null && Event.current.type == EventType.Repaint)
            {
                // Autotest "tchat" : la touche simulee remplace l'evenement de ce passage, comme une vraie frappe.
                Event real = Event.current;
                Event.current = testKey;
                testKey = null;
                Kit.Begin(1f);
                try { DrawChatLine(); } finally { Kit.End(); }
                Log.Info("autotest : tchat apres la touche : ligne " + (ChatOpen ? "OUVERTE" : "fermee") + ", texte \"" + chatText + "\"");
                Event.current = real;
                return;
            }
            if (ChatOpen) { Kit.Begin(1f); try { DrawChatLine(); } finally { Kit.End(); } }
            if (!Open) { wasOpen = false; return; }
            Event e = Event.current;
            if (!wasOpen)
            {
                wasOpen = true;
                openedAt = Time.realtimeSinceStartup;
                lastMouse = e.mousePosition;
                sel = tab == TabChat ? 9999 : 0;
                scroll = 0;
                follow = true;
                if (fieldFocused) { GUIUtility.keyboardControl = 0; fieldFocused = false; }
            }
            if (testSel >= 0) { sel = testSel; testSel = -1; follow = true; }
            typing = tab == TabChat && fieldFocused;
            if (e.type == EventType.KeyDown && e.keyCode != KeyCode.None && Key(e.keyCode, e.shift)) e.Use();
            mouseMoved = false;
            if (e.type == EventType.Repaint)
            {
                mouseMoved = (e.mousePosition - lastMouse).sqrMagnitude > 1f;
                lastMouse = e.mousePosition;
            }
            // Ouverture : fondu et leger glissement (0,16 s). Taille choisie des menus (Kit.MenuScale) le temps du dessin.
            float k = Mathf.Clamp01((Time.realtimeSinceStartup - openedAt) / 0.16f);
            k = 1f - (1f - k) * (1f - k);
            Kit.Begin(k);
            try { DrawMenu(e, k); }
            finally { Kit.End(); }
            if (testLog && e.type == EventType.Repaint) { testLog = false; Log.Info("autotest : menu onglet " + tabs[tab] + ", " + lines + " lignes"); }
        }

        // Le menu (refonte du 18/10, maquette « Menus MWCoop en jeu ») : 880 x 620 en 1080 lignes (fois la taille choisie),
        // au centre ou a gauche ; barre d'onglets a gauche, titre et sous-titre de l'onglet, liste qui defile, pied (aide de la
        // ligne choisie, touches).
        static void DrawMenu(Event e, float k)
        {
            float w = Mathf.Min(Style.Px(880), Screen.width - 32f), h = Mathf.Min(Style.Px(620), Screen.height - 32f);
            float x = Kit.MenuLeft ? Mathf.Min(Style.Px(40), Screen.width - w) : Mathf.Round((Screen.width - w) / 2);
            float y = Mathf.Round((Screen.height - h) / 2 + (1f - k) * Style.Px(14));
            float one = Mathf.Max(1f, Style.Px(1));
            Kit.Frame(new Rect(x, y, w, h));
            float railW = Style.Px(200);
            DrawRail(new Rect(x, y, railW, h), e);
            Style.Fill(new Rect(x + railW, y + Style.Px(14), one, h - Style.Px(28)), Kit.Sep);
            float pad = Style.Px(22), cx0 = x + railW + pad, cwid = w - railW - 2 * pad;
            Style.Title(new Rect(cx0, y + Style.Px(16), cwid, Style.Px(30)), tabs[tab], Kit.Ink, TextAnchor.MiddleLeft, 22);
            Style.Text(new Rect(cx0, y + Style.Px(46), cwid, Style.Px(20)), TabSub(), 13, TextAnchor.MiddleLeft, Kit.Muted, false);
            float top = y + Style.Px(78), fh = Style.Px(62);
            var view = new Rect(cx0, top, cwid, y + h - fh - Style.Px(6) - top);
            idx = 0; lines = 0; cy = 0; selY0 = selY1 = -1; gridFirst = gridLast = -1;
            hint = sel < 0 ? TabsHint : null;
            if (Game.Msc && (tab == TabRace || tab == TabRally))
            {
                dragging = false;
                MscEvents.DrawTab(view, tab == TabRace ? MscEvents.Kind.Drag : MscEvents.Kind.Rally);
                contentH = viewH = view.height;
                items = 0;
            }
            else if (tab == TabRace)
            {
                // Course sur glace : RacePanel (inscription, quand venir, pilotes, chronos) ; rien a choisir (18/10 : plus de
                // Jokkis de location, la VAZ prend la place d'une IA).
                dragging = false;
                RacePanel.DrawTab(view);
                contentH = viewH = view.height;
                items = 0;
            }
            else if (tab == TabRally)
            {
                // Rallye : RacePanel (inscription chez Fleetari avec la VAZ, departs, pilotes, classement), comme la course sur glace
                dragging = false;
                RacePanel.DrawRallyTab(view);
                contentH = viewH = view.height;
                items = 0;
            }
            else if (tab == TabVaz)
            {
                // VAZ-2108 : VazPanel dessine la grille des couleurs, l'apercu 3D et la fiche (meme navigation que la course)
                idx = 1; lines = 1; dragging = false;
                if (VazPanel.DrawTab(view, e, sel >= 0, mouseMoved) && sel < 0) sel = 0;
                if (sel >= 0) hint = VazPanel.Hint;
                contentH = viewH = view.height;
                items = 1;
            }
            else
            {
                // APPARENCE : apercu 3D de la tenue en surbrillance, a droite de la liste (qui se retrecit d'autant).
                bool preview = tab == TabLook && skins != null && skins.Count > 0 && Studio.LiveEnabled;
                Rect pvr = default(Rect);
                if (preview)
                {
                    float pw = Style.Px(190);
                    pvr = new Rect(view.xMax - pw, view.y, pw, view.height - Style.Px(40));
                    view.width -= pw + Style.Px(16);
                }
                else dragging = false;
                viewH = view.height;
                if (e.type == EventType.ScrollWheel && view.Contains(e.mousePosition)) { scroll += e.delta.y * Style.Px(14); e.Use(); }
                scroll = Mathf.Clamp(scroll, 0, Mathf.Max(0, contentH - viewH));
                cw = view.width - Style.Px(12);
                GUI.BeginGroup(view);
                if (tab == TabPlayers) Players();
                else if (tab == TabLook) Skins();
                else if (tab == TabChat) ChatTab();
                else if (tab == TabSync) SyncTab();
                else if (tab == TabGfx) GfxTab();
                else HudTab();
                GUI.EndGroup();
                contentH = cy;
                items = idx;
                if (contentH > viewH)
                {
                    var track = new Rect(view.xMax - Style.Px(4), view.y, Mathf.Max(3f, Style.Px(4)), viewH);
                    Style.Round(track, track.width / 2, Kit.Sep);
                    float thumb = Mathf.Max(Style.Px(28), viewH * viewH / contentH);
                    var tb = new Rect(track.x, track.y + (viewH - thumb) * scroll / (contentH - viewH), track.width, thumb);
                    Style.Round(tb, tb.width / 2, Kit.A(Kit.Ice, 0.8f));
                }
                if (preview) SkinPreview(pvr, e);
            }
            // Fin du passage : selection bornee ; Gauche non prise par une ligne : retour a la barre d'onglets.
            if (sel >= items) sel = items - 1;
            if (kDir < 0 && sel >= 0) { sel = -1; follow = true; }
            kDir = 0; kEnter = false;
            if (follow)
            {
                if (sel <= 0) { scroll = 0; follow = false; }
                else if (selY0 >= 0)
                {
                    if (selY0 < scroll) scroll = selY0;
                    else if (selY1 > scroll + viewH) scroll = selY1 - viewH;
                    follow = false;
                }
            }
            // Pied : aide de la ligne choisie, puis les touches.
            float fy = y + h - fh;
            Style.Fill(new Rect(cx0, fy, cwid, one), Kit.Sep);
            if (hint != null) Style.Text(new Rect(cx0, fy + Style.Px(8), cwid, Style.Px(22)), hint, 13, TextAnchor.MiddleLeft, Kit.Ink, false);
            float hx = cx0, hy = fy + Style.Px(44);
            if (sel < 0) { hx = Kit.Hint(hx, hy, "\u2191\u2193", Lang.T("onglet", "tab")); hx = Kit.Hint(hx, hy, Lang.T("Entr\u00E9e", "Enter"), Lang.T("entrer", "enter")); }
            else
            {
                hx = Kit.Hint(hx, hy, "\u2191\u2193", Lang.T("choisir", "select"));
                hx = Kit.Hint(hx, hy, "\u2190\u2192", Lang.T("changer", "change"));
                hx = Kit.Hint(hx, hy, Lang.T("Entr\u00E9e", "Enter"), Lang.T("valider", "confirm"));
            }
            Kit.Hint(hx, hy, "Tab", Lang.T("onglet suivant", "next tab"));
            string close = Lang.T("fermer", "close");
            float cwd = Style.Width("F10", 11) + Style.Px(18) + Style.Width(close, 12, false);
            Kit.Hint(cx0 + cwid - cwd, hy, "F10", close);
        }

        static string TabSub()
        {
            switch (tab)
            {
                case TabPlayers:
                    if (!Session.Active) return Lang.T("Solo : lancez le jeu depuis MWCoop.exe pour jouer \u00E0 plusieurs", "Solo: start the game from MWCoop.exe to play with friends");
                    return Lang.Status(Session.Status) + Sep + Session.Players.Count + Lang.T(" joueur(s)", " player(s)");
                case TabChat: return Lang.T("En jeu, ", "In game, ") + Keys.Label(Keys.Chat) + Lang.T(" \u00E9crit sans ouvrir ce menu", " opens the chat without this menu");
                case TabRace: return Game.Msc ? MscEvents.Sub(MscEvents.Kind.Drag) : RacePanel.Sub;
                case TabRally: return Game.Msc ? MscEvents.Sub(MscEvents.Kind.Rally) : RacePanel.RallySub;
                case TabVaz: return Lang.T("Une par joueur, gratuite \u00B7 une couleur et un num\u00E9ro chacun \u00B7 rendue en quittant", "One per player, free \u00B7 a colour and a number each \u00B7 given back when you leave");
                case TabLook: return Lang.T("Ce que les autres voient \u00B7 chang\u00E9 tout de suite chez eux", "What the others see \u00B7 changed right away for them");
                case TabHud: return Lang.T("Pour vous seul \u00B7 aussi dans le lanceur (R\u00E9glages \u203A HUD) \u00B7 le HUD change autour du menu", "Just for you \u00B7 also in the launcher (Settings \u203A HUD) \u00B7 the HUD changes around this menu");
                case TabGfx: return Lang.T("Pour vous seul \u00B7 gard\u00E9 pour les prochaines parties \u00B7 aussi dans le lanceur", "Just for you \u00B7 kept for your next games \u00B7 also in the launcher");
                default: return Lang.T("\u00C9carts entre l'h\u00F4te et les invit\u00E9s, recensement, resynchronisation", "Differences between host and guests, census, resync");
            }
        }

        // Barre d'onglets : logo, role, groupes et onglets (icone, nom), version en bas. Onglet actif cercle quand la barre a
        // le clavier (sel < 0).
        static void DrawRail(Rect r, Event e)
        {
            float px = r.x + Style.Px(12), pw = r.width - Style.Px(24), yy = r.y + Style.Px(16);
            float lw = Style.Logo(new Rect(px + Style.Px(4), yy, pw - Style.Px(4), Style.Px(58)));
            if (lw <= 0f) Style.Title(new Rect(px + Style.Px(8), yy, pw, Style.Px(58)), "MWCoop", Kit.Ink, TextAnchor.MiddleLeft, 24);
            yy += Style.Px(64);
            string badge = !Session.Active ? "SOLO" : Session.IsHost ? Lang.T("H\u00D4TE", "HOST") : Lang.T("INVIT\u00C9", "GUEST");
            Kit.Pill(px + Style.Px(8), yy + Style.Px(9), badge, Kit.Ice);
            yy += Style.Px(30);
            int lastG = -1;
            string[] names = tabs, groups = Lang.A(groupsFr, groupsEn);
            int mine = PlayerSync.InGame ? IceRace.MyNumber : 0;
            for (int i = 0; i < names.Length; i++)
            {
                if (TabHidden(i)) continue;
                if (tabGroup[i] != lastG)
                {
                    lastG = tabGroup[i];
                    Kit.Section(new Rect(px + Style.Px(10), yy, pw, Style.Px(22)), groups[lastG]);
                    yy += Style.Px(26);
                }
                var tr = new Rect(px, yy, pw, Style.Px(36));
                yy += Style.Px(38);
                bool on = i == tab, over = tr.Contains(e.mousePosition);
                if (e.type == EventType.MouseDown && e.button == 0 && over) { SetTab(i); e.Use(); }
                if (e.type != EventType.Repaint) continue;
                float rad = Style.Px(11);
                if (on) { Style.Round(tr, rad, Kit.A(Kit.Ice, 0.16f)); if (sel < 0) Style.Ring(tr, rad, Kit.A(Kit.Ice, 0.75f)); }
                else if (over) Style.Round(tr, rad, Kit.A(Color.white, 0.06f));
                Color c = on ? Kit.Ice : Kit.Soft;
                Kit.Icon(new Rect(tr.x + Style.Px(12), tr.center.y - Style.Px(9), Style.Px(18), Style.Px(18)), tabIcons[i], c);
                Style.Text(new Rect(tr.x + Style.Px(40), tr.y, tr.width - Style.Px(48), tr.height), names[i], 14, TextAnchor.MiddleLeft, c);
                string tag = i == TabPlayers && Session.Active ? Session.Players.Count.ToString() : i == TabRace && mine != 0 ? "#" + mine : i == TabVaz && PlayerSync.InGame && Vaz.Mine != 0 ? "n\u00B0 " + Vaz.Mine : null;
                if (tag != null) Style.Text(new Rect(tr.x, tr.y, tr.width - Style.Px(12), tr.height), tag, 11, TextAnchor.MiddleRight, on ? Kit.Ice : Kit.Faint);
            }
            Style.Text(new Rect(px + Style.Px(10), r.yMax - Style.Px(34), pw, Style.Px(20)), "version " + Version.Text, 11, TextAnchor.MiddleLeft, Kit.Faint, false);
        }

        // Touche (KeyDown) : vrai si le menu la prend (l'evenement est alors consomme). Barre d'onglets (sel < 0) : Haut/Bas
        // changent d'onglet, Droite ou Entree entrent dans la liste. Liste : Gauche non prise par la ligne revient a la barre.
        static bool Key(KeyCode k, bool shift)
        {
            if (tab == TabVaz && sel >= 0 && VazPanel.Key(k)) return true;
            switch (k)
            {
                case KeyCode.UpArrow: if (sel < 0) SetTab(StepTab(tab, -1)); else Move(-1); return true;
                case KeyCode.DownArrow: if (sel < 0) SetTab(StepTab(tab, 1)); else Move(1); return true;
                case KeyCode.PageUp: Move(-8); return true;
                case KeyCode.PageDown: Move(8); return true;
                case KeyCode.LeftArrow:
                case KeyCode.RightArrow:
                    if (typing) return false;   // curseur du champ de saisie
                    int d = k == KeyCode.LeftArrow ? -1 : 1;
                    if (sel < 0) { if (d > 0) Move(1); } else kDir = d;
                    return true;
                case KeyCode.Return:
                case KeyCode.KeypadEnter:
                    if (sel < 0) Move(1); else kEnter = true;
                    return true;
                case KeyCode.Tab: SetTab(StepTab(tab, shift ? -1 : 1)); return true;
                case KeyCode.Q:
                case KeyCode.E:
                    if (typing) return false;
                    SetTab(StepTab(tab, k == KeyCode.Q ? -1 : 1));
                    return true;
            }
            return false;
        }

        // Grille de tuiles (tenues) du dernier passage : Haut/Bas y sautent d'une rangee.
        static int gridFirst = -1, gridLast = -1, gridCols = 1;

        static void Move(int d)
        {
            if (sel < 0) { if (d > 0 && items > 0) sel = 0; }
            else if (gridFirst >= 0 && sel >= gridFirst && sel <= gridLast && (d == 1 || d == -1))
            {
                int nx = sel + d * gridCols;
                if (nx < gridFirst) sel = gridFirst - 1;
                else if (nx > gridLast) sel = (sel - gridFirst) / gridCols < (gridLast - gridFirst) / gridCols ? gridLast : Mathf.Min(gridLast + 1, items - 1);
                else sel = nx;
            }
            else
            {
                sel += d;
                if (sel < 0) sel = d == -1 ? -1 : 0;
                if (sel >= items) sel = items - 1;
            }
            follow = true;
        }

        static void SetTab(int i)
        {
            if (i == tab) return;
            tab = i;
            scroll = 0;
            follow = true;
            if (sel >= 0) sel = i == TabChat ? 9999 : 0;   // tchat : directement sur la saisie
            if (fieldFocused) { GUIUtility.keyboardControl = 0; fieldFocused = false; }
        }

        // ---- Lignes (coordonnees du groupe defilant) ----

        static readonly Dictionary<string, float> heights = new Dictionary<string, float>();
        static float heightsW;

        static float WrapHeight(string s, int size)
        {
            if (cw != heightsW || heights.Count > 400) { heights.Clear(); heightsW = cw; }
            float hh;
            string key = size + "|" + s;
            if (!heights.TryGetValue(key, out hh)) heights[key] = hh = Style.Height(s, size, cw - 2 * Style.Px(10), false);
            return hh;
        }

        static bool Visible(Rect r) { return r.yMax > 0 && r.y < viewH; }

        // Image a gauche du libelle de la prochaine ligne, remise a zero par Item.
        static Texture nextIcon;
        // Prochaine ligne grisee (action indisponible : libelle en gris), remis a zero par Item.
        static bool nextDim;
        // Derniere ligne dessinee (interrupteur, pastilles a segments) : rectangle, choisie, clic de souris.
        static Rect lastRow;
        static bool lastSelected, lastClick;

        // Ligne choisissable (36 px) : 0 rien, -1/+1 valeur changee (choice), 2 validee (Entree ou clic).
        static int Item(string label, string value, bool choice, string help, bool wrap)
        {
            Texture icon = nextIcon; nextIcon = null;
            bool dim = nextDim; nextDim = false;
            int me = idx++;
            lines++;
            float ip = Style.Px(10), h = Style.Px(36);
            if (wrap) h = Mathf.Max(h, WrapHeight(label, 14) + Style.Px(12));
            var r = new Rect(0, cy - scroll, cw, h);
            cy += h + Style.Px(2);
            bool selected = me == sel;
            lastRow = r; lastSelected = selected; lastClick = false;
            if (selected) { selY0 = r.y + scroll; selY1 = selY0 + h; if (help != null) hint = help; }

            // « < valeur > » a droite (choix).
            float aw = Style.Px(24);
            Rect ra = default(Rect), rv = default(Rect), la = default(Rect);
            if (choice && value != null)
            {
                ra = new Rect(r.xMax - ip - aw, r.y + (h - aw) / 2, aw, aw);
                float vw = Mathf.Min(Mathf.Max(Style.Width(value, 14), Style.Px(64)), r.width * 0.45f);
                rv = new Rect(ra.x - Style.Px(4) - vw, r.y, vw, h);
                la = new Rect(rv.x - Style.Px(4) - aw, ra.y, aw, aw);
            }

            int res = 0;
            Event e = Event.current;
            bool vis = Visible(r);
            if (vis && e.type == EventType.MouseDown && e.button == 0 && r.Contains(e.mousePosition))
            {
                sel = me; lastClick = true;
                res = choice ? (value != null && e.mousePosition.x <= la.xMax ? -1 : 1) : 2;
                e.Use();
            }
            else if (vis && mouseMoved && !typing && r.Contains(e.mousePosition)) sel = me;
            else if (selected && kEnter) { kEnter = false; res = choice ? 1 : 2; }
            else if (selected && choice && kDir != 0) { res = kDir; kDir = 0; }

            if (!vis || e.type != EventType.Repaint) return res;
            float rr = Style.Px(9);
            if (selected) { Style.Round(r, rr, Kit.A(Kit.Ice, 0.12f)); Style.Ring(r, rr, Kit.A(Kit.Ice, 0.45f)); }
            Color tc = dim ? Kit.Faint : selected ? Kit.Ink : Kit.Soft;
            if (icon != null && !wrap)
            {
                float isz = h - Style.Px(12);
                Color gc = GUI.color;
                GUI.color = new Color(1f, 1f, 1f, Style.Alpha);
                GUI.DrawTexture(new Rect(r.x + ip, r.y + (h - isz) / 2, isz, isz), icon);
                GUI.color = gc;
                r.x += isz + Style.Px(8); r.width -= isz + Style.Px(8);
            }
            if (wrap) Style.Text(new Rect(r.x + ip, r.y + Style.Px(6), r.width - 2 * ip, h - Style.Px(12)), label, 14, TextAnchor.UpperLeft, tc, false, true);
            else Style.Text(new Rect(r.x + ip, r.y, (value != null ? r.width * 0.6f : r.width) - 2 * ip, h), label, 14, TextAnchor.MiddleLeft, tc);
            if (value == null) return res;
            if (choice)
            {
                Color ac = selected ? Kit.Ice : Kit.Muted;
                Style.Round(la, Style.Px(7), Kit.A(Color.white, 0.06f));
                Style.Round(ra, Style.Px(7), Kit.A(Color.white, 0.06f));
                Style.Text(la, "<", 13, TextAnchor.MiddleCenter, ac);
                Style.Text(rv, value, 14, TextAnchor.MiddleCenter, selected ? Kit.Ink : Kit.Soft);
                Style.Text(ra, ">", 13, TextAnchor.MiddleCenter, ac);
            }
            else Style.Text(new Rect(r.x + r.width * 0.4f, r.y, r.width * 0.6f - ip, h), value, 12, TextAnchor.MiddleRight, selected ? Kit.Ice : Kit.Muted);
            return res;
        }

        // Oui/Non en interrupteur : vrai s'il faut basculer (clic, Entree, Gauche/Droite).
        static bool ToggleRow(string label, bool on, string help)
        {
            int res = Item(label, null, true, help, false);
            if (Visible(lastRow) && Event.current.type == EventType.Repaint)
                Kit.Toggle(new Rect(lastRow.xMax - Style.Px(48), lastRow.center.y - Style.Px(10), Style.Px(38), Style.Px(20)), on, lastSelected);
            return res != 0;
        }

        // Peu de valeurs (4 au plus) : pastilles a segments ; renvoie le nouveau rang, ou -1.
        static int SegRow(string label, string[] opts, int k, string help)
        {
            int res = Item(label, null, true, help, false);
            if (!Visible(lastRow)) return -1;
            float sw = Kit.SegmentsWidth(opts), sh = Style.Px(28);
            Rect[] rs = Kit.Segments(new Rect(lastRow.xMax - Style.Px(8) - sw, lastRow.center.y - sh / 2, sw, sh), opts, k, lastSelected);
            if (res == 0) return -1;
            if (lastClick)
            {
                for (int i = 0; i < rs.Length; i++) if (rs[i].Contains(Event.current.mousePosition)) return i == k ? -1 : i;
                return -1;
            }
            int nk = Mathf.Clamp(k + res, 0, opts.Length - 1);
            return nk == k ? -1 : nk;
        }

        // Carte d'action (icone, titre, sous-titre, chevron) : vrai si validee (clic ou Entree) et possible.
        static bool ActionRow(string icon, string title, string sub, string help, bool enabled)
        {
            int me = idx++;
            lines++;
            float h = Style.Px(52);
            var r = new Rect(0, cy - scroll, cw, h);
            cy += h + Style.Px(6);
            bool selected = me == sel;
            if (selected) { selY0 = r.y + scroll; selY1 = selY0 + h; if (help != null) hint = help; }
            Event e = Event.current;
            bool vis = Visible(r), go = false;
            if (vis && e.type == EventType.MouseDown && e.button == 0 && r.Contains(e.mousePosition)) { sel = me; go = true; e.Use(); }
            else if (vis && mouseMoved && !typing && r.Contains(e.mousePosition)) sel = me;
            else if (selected && kEnter) { kEnter = false; go = true; }
            if (vis && e.type == EventType.Repaint)
            {
                float rr = Style.Px(12);
                Style.Round(r, rr, selected ? Kit.A(Kit.Ice, 0.12f) : Kit.Tint);
                Style.Ring(r, rr, selected ? Kit.A(Kit.Ice, 0.45f) : Kit.Sep);
                var ib = new Rect(r.x + Style.Px(8), r.y + Style.Px(8), h - Style.Px(16), h - Style.Px(16));
                Style.Round(ib, Style.Px(9), Kit.A(Kit.Ice, enabled ? 0.14f : 0.06f));
                Kit.Icon(new Rect(ib.center.x - Style.Px(9), ib.center.y - Style.Px(9), Style.Px(18), Style.Px(18)), icon, enabled ? Kit.Ice : Kit.Faint);
                float tx = ib.xMax + Style.Px(12);
                Style.Text(new Rect(tx, r.y + Style.Px(7), r.width - tx - Style.Px(40), Style.Px(20)), title, 14, TextAnchor.MiddleLeft, enabled ? Kit.Ink : Kit.Faint);
                Style.Text(new Rect(tx, r.y + Style.Px(27), r.width - tx - Style.Px(40), Style.Px(18)), sub, 12, TextAnchor.MiddleLeft, Kit.Muted, false);
                Kit.Icon(new Rect(r.xMax - Style.Px(26), r.center.y - Style.Px(7), Style.Px(14), Style.Px(14)), "suivant", Kit.Faint);
            }
            return go && enabled;
        }

        // Grille de tuiles (tenues) : une tuile par libelle, celle portee marquee ; renvoie le rang valide, ou -1.
        static int TileGrid(List<string> labels, int cur, int cols)
        {
            float gap = Style.Px(6), tw = Mathf.Floor((cw - (cols - 1) * gap) / cols), th = Style.Px(42);
            int first = idx, res = -1;
            Event e = Event.current;
            for (int j = 0; j < labels.Count; j++)
            {
                int me = idx++;
                var r = new Rect((j % cols) * (tw + gap), cy - scroll + (j / cols) * (th + gap), tw, th);
                bool selected = me == sel, worn = j == cur, vis = Visible(r);
                if (selected)
                {
                    selY0 = r.y + scroll; selY1 = selY0 + th;
                    hint = Lang.T("Entr\u00E9e ou clic : porter cette tenue", "Enter or click: wear this outfit");
                    if (kDir != 0 && me + kDir >= first && me + kDir < first + labels.Count) { sel = me + kDir; kDir = 0; follow = true; }
                    if (kEnter) { kEnter = false; res = j; }
                }
                if (vis && e.type == EventType.MouseDown && e.button == 0 && r.Contains(e.mousePosition)) { sel = me; res = j; e.Use(); }
                else if (vis && mouseMoved && !typing && r.Contains(e.mousePosition)) sel = me;
                if (!vis || e.type != EventType.Repaint) continue;
                float rr = Style.Px(9);
                Style.Round(r, rr, worn ? Kit.A(Kit.Ice, 0.12f) : Kit.Tint);
                Style.Ring(r, rr, selected ? Kit.A(Kit.Ice, 0.75f) : worn ? Kit.A(Kit.Ice, 0.45f) : Kit.Sep);
                Kit.Icon(new Rect(r.x + Style.Px(9), r.center.y - Style.Px(8), Style.Px(16), Style.Px(16)), "apparence", worn ? Kit.Ice : Kit.Faint);
                float tx = r.x + Style.Px(32), tw2 = r.width - Style.Px(38);
                if (worn)
                {
                    Style.Text(new Rect(tx, r.y + Style.Px(4), tw2, Style.Px(20)), labels[j], 13, TextAnchor.MiddleLeft, Kit.Ink);
                    Style.Text(new Rect(tx, r.y + Style.Px(22), tw2, Style.Px(16)), Lang.T("PORT\u00C9E", "WORN"), 10, TextAnchor.MiddleLeft, Kit.Ice);
                }
                else Style.Text(new Rect(tx, r.y, tw2, r.height), labels[j], 13, TextAnchor.MiddleLeft, selected ? Kit.Ink : Kit.Soft);
            }
            int rows = (labels.Count + cols - 1) / cols;
            cy += rows * (th + gap);
            lines += rows;
            gridFirst = first; gridLast = idx - 1; gridCols = cols;
            return res;
        }

        // Paragraphe non choisissable (texte replie).
        static void Para(string s, Color c)
        {
            lines++;
            float ip = Style.Px(10);
            float h = WrapHeight(s, 13) + Style.Px(8);
            var r = new Rect(ip, cy - scroll + Style.Px(2), cw - 2 * ip, h);
            cy += h + Style.Px(6);
            if (Visible(r) && Event.current.type == EventType.Repaint) Style.Text(r, s, 13, TextAnchor.UpperLeft, c, false, true);
        }

        // Entete de section : petites capitales grises.
        static void Header(string s)
        {
            lines++;
            float h = Style.Px(30);
            var r = new Rect(0, cy - scroll, cw, h);
            cy += h + Style.Px(2);
            if (!Visible(r) || Event.current.type != EventType.Repaint) return;
            Kit.Section(new Rect(r.x + Style.Px(10), r.y, r.width - Style.Px(20), h - Style.Px(6)), s);
        }

        // ---- Onglets ----

        class PRow { public PlayerInfo P; public string Label, Value; }
        static readonly List<PRow> prows = new List<PRow>();
        static float prowsAt;

        // Libelles des joueurs refaits 2 fois par seconde (ping) ou a chaque arrivee/depart.
        static void RefreshPlayers()
        {
            if (Time.realtimeSinceStartup < prowsAt && prows.Count == Session.Players.Count) return;
            prowsAt = Time.realtimeSinceStartup + 0.5f;
            int n = 0;
            foreach (PlayerInfo pi in Session.Players.Values)
            {
                if (n == prows.Count) prows.Add(new PRow());
                PRow r = prows[n++];
                r.P = pi;
                r.Label = (pi.Id == 0 ? Lang.T("[h\u00F4te] ", "[host] ") : "") + pi.Name + (pi.Local ? Lang.T(" (vous)", " (you)") : "");
                string where = pi.Level == 1 ? Lang.T("en partie", "in game") : Lang.T("au menu", "at the menu");
                r.Value = !pi.Local && pi.Peer != null ? where + "  " + Mathf.RoundToInt(pi.Peer.Rtt * 1000) + " ms" : where;
            }
            prows.RemoveRange(n, prows.Count - n);
        }

        static void Players()
        {
            RefreshPlayers();
            for (int i = 0; i < prows.Count; i++) PlayerCard(prows[i].P);
            // Partie par Steam : invitation d'amis (overlay Steam) depuis le menu.
            if (Session.Active && Session.Steam)
            {
                bool ready = MWCoop.Net.SteamNet.Lobby != 0;
                if (ActionRow("inviter", Lang.T("Inviter des amis Steam", "Invite Steam friends"),
                              ready ? Lang.T("Fen\u00EAtre d'invitation de Steam (aussi : Maj+Tab)", "Steam's invite window (also: Shift+Tab)") : Lang.T("salon en cours...", "lobby starting..."),
                              Lang.T("Ouvre la fen\u00EAtre d'invitation de Steam (aussi : Maj+Tab). Ils lancent MWCoop.exe > REJOINDRE (Steam) et acceptent.",
                                     "Opens Steam's invite window (also: Shift+Tab). They start MWCoop.exe > JOIN (Steam) and accept."), ready))
                    MWCoop.Net.SteamNet.InviteDialog();
            }
            // Course sur glace : son onglet (My Summer Car : les dragsters et le rallye du jeu).
            if (PlayerSync.InGame && Game.Msc)
            {
                string nd, nr;
                bool ld = MscEvents.Live(MscEvents.Kind.Drag, out nd), lr = MscEvents.Live(MscEvents.Kind.Rally, out nr);
                if (ActionRow("course", Lang.T("Dragsters et rallye", "Drag race and rally"),
                              ld ? Lang.T("Piste de dragsters ouverte", "Drag strip open") : lr ? Lang.T("Rallye en cours", "Rally on now")
                                 : Lang.T("Dragsters ", "Drag race ") + (nd ?? "?") + Sep + Lang.T("rallye ", "rally ") + (nr ?? "?"),
                              Lang.T("Entrée ou clic : l'onglet Dragsters", "Enter or click: the Drag race tab"), true))
                    SetTab(TabRace);
            }
            else if (PlayerSync.InGame)
            {
                int mine = IceRace.MyNumber;
                if (ActionRow("course", Lang.T("Course sur glace", "Ice race"),
                              mine != 0 ? Lang.T("Inscrit \u00B7 n\u00B0 ", "Registered \u00B7 no. ") + mine + (IceRace.MeWithVaz ? " (VAZ)" : "") : Lang.T("Pas inscrit \u00B7 inscription avec ta VAZ", "Not registered \u00B7 sign up with your VAZ"),
                              Lang.T("Entr\u00E9e ou clic : l'onglet Course sur glace", "Enter or click: the Ice race tab"), true))
                    SetTab(TabRace);
            }
            ResyncCard();
        }

        // Carte d'un joueur : avatar, nom et role, ou il est, ping ; boutons Aller (a lui), Faire venir et Exclure (hote).
        // Clavier : Gauche/Droite choisissent le bouton, Entree le declenche.
        static int btnSel;
        static readonly Rect[] pbR = new Rect[3];
        static readonly int[] pbA = new int[3];

        static void PlayerCard(PlayerInfo pi)
        {
            int me = idx++;
            lines++;
            float h = Style.Px(56);
            var r = new Rect(0, cy - scroll, cw, h);
            cy += h + Style.Px(6);
            bool selected = me == sel, inGame = pi.Level == 1;
            bool canGo = !pi.Local && inGame && PlayerSync.InGame;
            bool host = Session.Active && Session.IsHost && !pi.Local;
            bool armed = kickArmed == pi.Id && Time.realtimeSinceStartup - kickArmedAt < 4f;
            string lGo = Lang.T("Aller", "Go to"), lBring = Lang.T("Faire venir", "Bring here"), lKick = Lang.T("Confirmer ?", "Confirm?");
            // boutons de droite a gauche, puis dans l'ordre du clavier (gauche a droite)
            int nb = 0;
            float bh = Style.Px(30), by = r.center.y - bh / 2, bx = r.xMax - Style.Px(10);
            if (host) { float bw = armed ? Style.Width(lKick, 13) + Style.Px(22) : bh; bx -= bw; pbR[nb] = new Rect(bx, by, bw, bh); pbA[nb++] = 2; bx -= Style.Px(6); }
            if (host) { float bw = Style.Width(lBring, 13) + Style.Px(42); bx -= bw; pbR[nb] = new Rect(bx, by, bw, bh); pbA[nb++] = 1; bx -= Style.Px(6); }
            if (!pi.Local) { float bw = Style.Width(lGo, 13) + Style.Px(42); bx -= bw; pbR[nb] = new Rect(bx, by, bw, bh); pbA[nb++] = 0; bx -= Style.Px(6); }
            System.Array.Reverse(pbR, 0, nb); System.Array.Reverse(pbA, 0, nb);
            if (selected)
            {
                selY0 = r.y + scroll; selY1 = selY0 + h;
                btnSel = Mathf.Clamp(btnSel, 0, Mathf.Max(0, nb - 1));
                if (kDir != 0 && btnSel + kDir >= 0 && btnSel + kDir < nb) { btnSel += kDir; kDir = 0; }
                if (nb > 0)
                {
                    int a = pbA[btnSel];
                    hint = a == 0 ? Lang.T("Entr\u00E9e : aller vers ce joueur", "Enter: go to this player")
                         : a == 1 ? Lang.T("Entr\u00E9e : ce joueur arrive pr\u00E8s de vous", "Enter: this player comes next to you")
                         : armed ? Lang.T("Entr\u00E9e encore : exclure ce joueur", "Enter again: kick this player")
                                 : Lang.T("Deux fois : ce joueur est d\u00E9connect\u00E9 de la partie", "Twice: this player is disconnected from the game");
                }
            }
            Event e = Event.current;
            bool vis = Visible(r);
            int act = -1;
            if (vis && e.type == EventType.Repaint)
            {
                float rr = Style.Px(12);
                Style.Round(r, rr, selected ? Kit.A(Kit.Ice, 0.10f) : Kit.Tint);
                Style.Ring(r, rr, selected ? Kit.A(Kit.Ice, 0.40f) : Kit.Sep);
                float av = Style.Px(36), fade = inGame ? 1f : 0.55f;
                Kit.Avatar(new Rect(r.x + Style.Px(10), r.center.y - av / 2, av, av), pi.Id, pi.Name, pi.SteamId, fade);
                float tx = r.x + Style.Px(56), nw = Style.Width(pi.Name, 15);
                Style.Text(new Rect(tx, r.y + Style.Px(8), nw + Style.Px(4), Style.Px(22)), pi.Name, 15, TextAnchor.MiddleLeft, inGame ? Kit.Ink : Kit.Faint);
                string tag = pi.Local ? (pi.Id == 0 ? Lang.T("vous \u00B7 h\u00F4te", "you \u00B7 host") : Lang.T("vous", "you")) : pi.Id == 0 ? Lang.T("h\u00F4te", "host") : null;
                if (tag != null) Kit.Pill(tx + nw + Style.Px(8), r.y + Style.Px(19), tag, Kit.Ice);
                string where = !inGame ? Lang.T("Au menu du jeu", "At the game's menu") : pi.Local ? Lang.T("En partie", "In game")
                             : NewHud.Where(pi.Id, Game.PlayerGo != null ? Game.PlayerGo.transform : null);
                if (where.Length == 0) where = Lang.T("En partie", "In game");
                Style.Text(new Rect(tx, r.y + Style.Px(30), bx - tx, Style.Px(18)), where, 12, TextAnchor.MiddleLeft, Kit.Muted, false);
                if (!pi.Local && pi.Peer != null)
                {
                    int ms = Mathf.RoundToInt(pi.Peer.Rtt * 1000);
                    Style.Text(new Rect(tx, r.y, bx - tx, h), ms + " ms", 12, TextAnchor.MiddleRight, ms < 90 ? Kit.Green : ms < 180 ? Kit.Amber : Kit.Red);
                }
            }
            if (vis)
                for (int i = 0; i < nb; i++)
                {
                    int a = pbA[i];
                    bool en = a == 0 ? canGo : a == 1 ? inGame && PlayerSync.InGame : true;
                    string lab = a == 0 ? lGo : a == 1 ? lBring : armed ? lKick : "";
                    string ic = a == 0 ? "aller" : a == 1 ? "venir" : armed ? null : "croix";
                    if (Kit.Button(pbR[i], lab, a == 2 ? 2 : 0, selected && btnSel == i, en, ic)) { sel = me; btnSel = i; act = a; }
                }
            if (vis && e.type == EventType.MouseDown && e.button == 0 && r.Contains(e.mousePosition)) { sel = me; e.Use(); }
            else if (vis && mouseMoved && !typing && r.Contains(e.mousePosition)) sel = me;
            if (selected && kEnter && nb > 0)
            {
                kEnter = false;
                int a = pbA[btnSel];
                if (a == 0 ? canGo : a == 1 ? inGame && PlayerSync.InGame : true) act = a;
            }
            if (act == 0) GoTo(pi);
            else if (act == 1) { MWCoop.Net.Admin.Summon(pi.Id); Open = false; }
            else if (act == 2)
            {
                if (armed) { MWCoop.Net.Admin.Kick(pi.Id, "menu F10"); kickArmed = -1; }
                else { kickArmed = pi.Id; kickArmedAt = Time.realtimeSinceStartup; }
            }
        }

        static void GoTo(PlayerInfo pi)
        {
            if (VehicleSync.LocalDriving >= 0 || Seats.Seated) { Hud.Toast(Lang.T("Sortez d'abord du v\u00E9hicule", "Get out of the vehicle first")); return; }
            TeleportTo(pi.State.Feet, pi.State.Yaw, pi.Name);
        }

        // Pres de quelqu'un (pieds, cap) : 1,5 m derriere lui, un peu au-dessus du sol (la gravite fait le reste).
        public static void TeleportTo(Vector3 feet, float yaw, string who)
        {
            GameObject p = Game.PlayerGo;
            if (p == null) return;
            Vector3 target = feet + Quaternion.Euler(0, yaw, 0) * new Vector3(0, 0, -1.5f) + Vector3.up * 1.0f;
            var cc = p.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;
            p.transform.position = target;
            if (cc != null) cc.enabled = true;
            Log.Info("teleporte vers " + who);
            Open = false;
        }

        // Hote : exclure demande deux clics (le 2e dans les 4 s) ; le 1er le dit dans la ligne.
        static int kickArmed = -1;
        static float kickArmedAt;

        static string lastSkin, lastSkinLabel;

        static void LoadSkins()
        {
            if ((skins == null || skins.Count == 0) && PlayerSync.InGame)
            {
                skins = Avatar.SkinNames();
                skinLabels.Clear();
                foreach (string s in skins) skinLabels.Add(SkinLabel(s));
            }
        }

        static void Skins()
        {
            LoadSkins();
            if (skins == null || skins.Count == 0) { Para(Lang.T("Les apparences se choisissent en partie.", "Outfits are picked in game."), Style.Dim); return; }
            string me = Looks.Parse(Session.Me.Skin).Shirt;   // (le haut ; le reste : lanceur, volet TENUE)
            int cur = skins.IndexOf(me);
            if (me != lastSkin) { lastSkin = me; lastSkinLabel = SkinLabel(me); }
            int d = Item(Lang.T("Tenue", "Outfit"), cur >= 0 ? skinLabels[cur] : lastSkinLabel, true, Lang.T("Gauche/Droite : changer d'apparence", "Left/Right: change outfit"), false);
            if (d == 1 || d == -1) SetSkin(skins[((cur < 0 ? 0 : cur) + d + skins.Count) % skins.Count]);
            // le reste de l'apparence (Looks) : corpulence, pantalon, visage, chapeau, lunettes, cheveux
            Looks.Look look = Looks.Parse(Session.Me.Skin);
            for (int f = 0; f < LookRows; f++)
            {
                string v = Looks.Get(look, f);
                int dd = Item(Looks.PartName(f), Looks.Label(f, v), true, Lang.T("Gauche/Droite : changer (les autres le voient aussit\u00F4t)", "Left/Right: change (the others see it right away)"), false);
                if (dd != 1 && dd != -1) continue;
                List<string> ch = Looks.Choices(f);
                int i = Mathf.Max(0, ch.IndexOf(CustomFace.IsCustom(v) ? "perso" : v));
                SetPart(f, ch[(i + dd + ch.Count) % ch.Count]);
            }
            Header(Lang.T("TENUES", "OUTFITS") + Sep + skins.Count);
            int jw = TileGrid(skinLabels, cur, 3);
            if (jw >= 0) SetSkin(skins[jw]);
        }

        static bool dragging;
        static string pvSkin, pvLabel;
        static readonly Color previewBg = Style.M(new Color(0f, 0.02f, 0.06f, 0.30f), new Color(0f, 0f, 0f, 0.30f));

        // Apercu 3D (Studio.Live) : la tenue en surbrillance dans la liste (sinon celle portee), qui tourne lentement ;
        // glisser a la souris pour la tourner. Son nom dessous, facon GTA.
        static void SkinPreview(Rect r, Event e)
        {
            string me = Looks.Parse(Session.Me.Skin).Shirt;
            int k = sel - 1 - LookRows;   // lignes : 0 = « Apparence », puis les parties (LookRows), puis les tenues
            string s = k >= 0 && k < skins.Count ? skins[k] : me;
            if (s != pvSkin) { pvSkin = s; pvLabel = s.Length > 0 ? SkinLabel(s).ToUpperInvariant() : Lang.T("PAR D\u00C9FAUT", "DEFAULT"); }
            Texture tex = Studio.Live(Looks.WithShirt(Session.Me.Skin, s), (int)r.width, (int)r.height);   // (le reste de l'apparence avec)
            if (e.type == EventType.MouseDown && e.button == 0 && r.Contains(e.mousePosition)) { dragging = true; e.Use(); }
            else if (e.type == EventType.MouseDrag && dragging) { Studio.Drag(e.delta.x); e.Use(); }
            else if (e.rawType == EventType.MouseUp && dragging) { dragging = false; e.Use(); }
            if (e.type != EventType.Repaint) return;
            float rad = Style.Px(14);
            Style.Round(r, rad, previewBg);
            if (tex != null && !Backdrop.DrawImage(r, rad, tex, Style.Alpha))
            {
                // Sans maillage arrondi (ou pendant le fondu) : image carree, en retrait pour rester dans le cadre.
                float ins = Mathf.Round(rad * 0.3f);
                Color oc = GUI.color;
                GUI.color = new Color(1f, 1f, 1f, Style.Alpha);
                GUI.DrawTexture(new Rect(r.x + ins, r.y + ins, r.width - 2 * ins, r.height - 2 * ins), tex, ScaleMode.StretchToFill, false);
                GUI.color = oc;
            }
            Style.Ring(r, rad, Kit.Sep);
            if (s == me && s.Length > 0)
            {
                string worn = Lang.T("PORT\u00C9E", "WORN");
                float pw = Style.Width(worn, 11) + Style.Px(16), ph = Style.Px(20);
                var pr = new Rect(r.xMax - pw - Style.Px(8), r.y + Style.Px(8), pw, ph);
                Style.Round(pr, ph / 2, Kit.Ice);
                Style.Text(pr, worn, 11, TextAnchor.MiddleCenter, Kit.OnIce);
            }
            Style.Text(new Rect(r.x + Style.Px(10), r.yMax - Style.Px(24), r.width - Style.Px(20), Style.Px(20)), Lang.T("glisser pour tourner", "drag to turn"), 11, TextAnchor.MiddleLeft, Kit.Faint, false);
            Style.Title(new Rect(r.x, r.yMax + Style.Px(6), r.width, Style.Px(30)), pvLabel, Kit.Ink, TextAnchor.MiddleCenter, 15);
        }

        // Autotest "tenues" (Studio.Test) : k >= 0 ouvre le menu sur APPARENCE avec une tenue en surbrillance (rendue),
        // k < 0 le ferme.
        static int testSel = -1;

        public static string TestApparence(int k)
        {
            if (k < 0) { Open = false; return null; }
            Open = true;
            SetTab(TabLook);
            LoadSkins();
            if (skins == null || skins.Count == 0) { testSel = 0; return "(aucune tenue)"; }
            int j = k * Mathf.Max(1, skins.Count / 5) % skins.Count;
            testSel = 1 + LookRows + j;   // (lignes : la tenue, les parties, puis les tuiles)
            return skins[j] + " (" + skinLabels[j] + ")";
        }

        public static string SkinLabel(string s)
        {
            if (s.StartsWith("char_shirt")) return Lang.T("Tenue ", "Outfit ") + s.Substring(10).TrimStart('0');
            if (CustomFace.IsCustom(s)) return Lang.T("Haut perso", "Custom top");
            if (Tenues.Has(s)) return Lang.T("Tenue ", "Outfit ") + Tenues.Label(s);
            switch (s)
            {
                case "cop_shirt": return Lang.T("Policier", "Police officer");
                case "cop_shirt2": return Lang.T("Policier 2", "Police officer 2");
                case "rally_shirt": return Lang.T("Pilote de rallye", "Rally driver");
                case "psk_shirt": return Lang.T("Employ\u00E9 PSK", "PSK employee");
                case "inspector_shirt": return Lang.T("Inspecteur", "Inspector");
            }
            return s;
        }

        const int LookRows = 6;
        static void SetPart(int f, string v)
        {
            Looks.Look l = Looks.Parse(Session.Me.Skin);
            Config.Save("Coop", Looks.PartKeys[f], v);
            if (v == "perso") { CustomFace.Forget(); v = CustomFace.MyKeyOf(f == 1 ? CustomFace.Pants : CustomFace.Face) ?? ""; }   // (image importee)
            Looks.Set(l, f, v);
            Session.Me.Skin = Looks.Compose(l);
            Session.SendProfile();
        }

        static void SetSkin(string s)
        {
            Session.Me.Skin = Looks.WithShirt(Session.Me.Skin, s);
            Config.Save("Coop", "Apparence", s);
            Session.SendProfile();
        }

        static string syncHead = "";
        static float syncAt;

        // Audit de la synchro : ecarts durables entre l'hote et les invites, actions locales non partagees.
        static void SyncTab()
        {
            ResyncRow();
            if (Time.realtimeSinceStartup >= syncAt)
            {
                syncAt = Time.realtimeSinceStartup + 0.5f;
                syncHead = !Audit.Enabled ? Lang.T("Audit coup\u00E9 par d\u00E9faut (performances). Pour l'activer : Audit=1 sous [Coop] dans MWCoop\\mwcoop.ini, puis relancer.",
                                                   "Audit is off by default (performance). To turn it on: Audit=1 under [Coop] in MWCoop\\mwcoop.ini, then restart.")
                          : Audit.State() + (Audit.Summary.Length > 0 ? "\n" + Audit.Summary : "");
            }
            Para(syncHead, Style.Dim);
            if (Item(Lang.T("\u00C9crire le recensement (dumps/recensement.txt)", "Write the census (dumps/recensement.txt)"), null, false, Lang.T("Entr\u00E9e ou clic : \u00E9crire le recensement", "Enter or click: write the census"), false) == 2) Audit.Census();
            Header(Lang.T("\u00C9CARTS AVEC LES INVIT\u00C9S", "DIFFERENCES WITH THE GUESTS"));
            Para(Session.IsHost ? Lang.T("H\u00F4te : tout le monde est compar\u00E9 \u00E0 vous.", "Host: everyone is compared to you.") : Lang.T("\u00C9carts : vus par l'h\u00F4te (son onglet SYNCHRO et son journal).", "Differences: seen by the host (their SYNC tab and log)."), Style.Dim);
            foreach (string l in Audit.Desyncs) Item(l, null, false, null, true);
            Header(Lang.T("VOS ACTIONS QUI NE PARTENT PAS CHEZ LES AUTRES", "YOUR ACTIONS NOT SHARED WITH THE OTHERS"));
            foreach (string l in Audit.Unshared) Item(l, null, false, null, true);
        }

        // « Resynchroniser le monde » (16/10) : l'invite redemande tout l'etat a l'hote, comme a son arrivee (seule la
        // sauvegarde de l'hote compte : ce qui a derive sans bruit chez l'invite se recale) ; l'hote le renvoie a chaque invite
        // en jeu. Une fois toutes les 30 s : en attendant, ligne grisee et le delai a droite.
        static void ResyncCard()
        {
            if (!Session.Active) return;
            bool can; float wait; string val, help;
            ResyncInfo(out can, out wait, out val, out help);
            if (ActionRow("synchro", Lang.T("Resynchroniser le monde", "Resync the world"), val ?? help, help, can)) DoResync(can, wait);
        }

        static void ResyncInfo(out bool can, out float wait, out string val, out string help)
        {
            wait = PlayerSync.ResyncWait;
            bool guestsIn = false;
            foreach (PlayerInfo pi in Session.Players.Values) if (!pi.Local && pi.Level == 1) guestsIn = true;
            can = wait <= 0f && PlayerSync.InGame && (Session.IsHost ? guestsIn : Session.HostConnected);
            val = wait > 0f ? Lang.T("d\u00E9j\u00E0 demand\u00E9, attends ", "already requested, wait ") + Mathf.CeilToInt(wait) + " s"
                : Session.IsHost && !guestsIn ? Lang.T("aucun invit\u00E9 en partie", "no guest in game")
                : !Session.IsHost && !Session.HostConnected ? Lang.T("h\u00F4te injoignable", "host unreachable") : null;
            help = Session.IsHost ? Lang.T("Renvoie tout l'\u00E9tat du monde \u00E0 chaque invit\u00E9, comme \u00E0 son arriv\u00E9e", "Resends the whole world state to every guest, as on their arrival")
                                  : Lang.T("Redemande tout l'\u00E9tat \u00E0 l'h\u00F4te, comme \u00E0 l'arriv\u00E9e", "Asks the host for the whole world state, as on arrival");
        }

        static void DoResync(bool can, float wait)
        {
            if (can && PlayerSync.RequestResync())
            {
                Hud.Toast(Session.IsHost ? Lang.T("\u00C9tat complet renvoy\u00E9 aux invit\u00E9s", "Whole world state resent to the guests")
                                         : Lang.T("Resynchronisation demand\u00E9e \u00E0 l'h\u00F4te", "Resync requested from the host"));
                Open = false;
            }
            else if (wait > 0f) Hud.Toast(Lang.T("D\u00E9j\u00E0 demand\u00E9, attends ", "Already requested, wait ") + Mathf.CeilToInt(wait) + " s");
        }

        static void ResyncRow()
        {
            if (!Session.Active) return;
            float wait = PlayerSync.ResyncWait;
            bool guestsIn = false;
            foreach (PlayerInfo pi in Session.Players.Values) if (!pi.Local && pi.Level == 1) guestsIn = true;
            bool can = wait <= 0f && PlayerSync.InGame && (Session.IsHost ? guestsIn : Session.HostConnected);
            string val = wait > 0f ? Lang.T("d\u00E9j\u00E0 demand\u00E9, attends ", "already requested, wait ") + Mathf.CeilToInt(wait) + " s"
                       : Session.IsHost && !guestsIn ? Lang.T("aucun invit\u00E9 en partie", "no guest in game")
                       : !Session.IsHost && !Session.HostConnected ? Lang.T("h\u00F4te injoignable", "host unreachable") : null;
            string help = Session.IsHost ? Lang.T("Renvoie tout l'\u00E9tat du monde \u00E0 chaque invit\u00E9, comme \u00E0 son arriv\u00E9e", "Resends the whole world state to every guest, as on their arrival")
                                         : Lang.T("Redemande tout l'\u00E9tat \u00E0 l'h\u00F4te, comme \u00E0 l'arriv\u00E9e", "Asks the host for the whole world state, as on arrival");
            nextDim = !can;
            if (Item(Lang.T("Resynchroniser le monde", "Resync the world"), val, false, help, false) != 2) return;
            if (can && PlayerSync.RequestResync())
            {
                Hud.Toast(Session.IsHost ? Lang.T("\u00C9tat complet renvoy\u00E9 aux invit\u00E9s", "Whole world state resent to the guests")
                                         : Lang.T("Resynchronisation demand\u00E9e \u00E0 l'h\u00F4te", "Resync requested from the host"));
                Open = false;
            }
            else if (wait > 0f) Hud.Toast(Lang.T("D\u00E9j\u00E0 demand\u00E9, attends ", "Already requested, wait ") + Mathf.CeilToInt(wait) + " s");
        }

        // Graphismes (comme la page du lanceur) : prereglage, puis chaque option ; applique tout de suite, garde dans mwcoop.ini.
        static void GfxTab()
        {
            int on = Gfx.PresetOn();
            string[] pl = new string[Gfx.Presets.Length + (on < 0 ? 1 : 0)];
            for (int i = 0; i < Gfx.Presets.Length; i++) pl[i] = Lang.T(Gfx.Presets[i].Fr, Gfx.Presets[i].En);
            if (on < 0) pl[pl.Length - 1] = Lang.T("Perso", "Custom");
            int pk = SegRow(Lang.T("Pr\u00E9r\u00E9glage", "Preset"), pl, on >= 0 ? on : pl.Length - 1, Lang.T("Tout d'un coup ; chaque ligne reste modifiable", "Everything at once; every line stays editable"));
            if (pk >= 0 && pk < Gfx.Presets.Length) Gfx.ApplyPreset(pk);
            Header(Lang.T("MENU MWCOOP", "MWCOOP MENU"));
            HudRow(HudSettings.Find("Menu"));
            HudRow(HudSettings.Find("MenuPosition"));
            string[] heads = { Lang.T("IMAGE", "IMAGE"), Lang.T("OMBRES", "SHADOWS"), Lang.T("DISTANCE ET D\u00C9TAILS", "DISTANCE AND DETAIL"), Lang.T("JEU ET CONFORT", "GAME AND COMFORT"), Lang.T("LUMI\u00C8RES", "LIGHTS"), Lang.T("\u00C9CRAN", "SCREEN"),
                               Lang.T("RAY TRACING (EN D\u00C9VELOPPEMENT)", "RAY TRACING (IN DEVELOPMENT)") };
            foreach (int g in new[] { 5, 0, 1, 6, 4, 2, 3 })
            {
                Header(heads[g]);
                foreach (Gfx.Row r in Gfx.Rows)
                {
                    if (r.Group != g) continue;
                    int v = Gfx.Value(r);
                    string hint = r.Heavy ? Lang.T("Lourd pour les images par seconde", "Heavy on the frame rate") : null;
                    if (r.Key == "RayTracing" && !RayTracing.Direct) hint = Lang.T("Actif au prochain lancement par le lanceur", "Active at the next launch from the launcher");
                    if (r.Vals == null) { if (ToggleRow(Lang.T(r.Fr, r.En), v == 1, hint)) Gfx.Set(r.Key, v == 1 ? 0 : 1); continue; }
                    int k = 0;
                    for (int i = 0; i < r.Vals.Length; i++) if (r.Vals[i] == v) k = i;
                    int res = Item(Lang.T(r.Fr, r.En), Lang.T(r.Lf[k], r.Le[k]), true, hint, false);
                    if (res != -1 && res != 1) continue;
                    int nk = Mathf.Clamp(k + res, 0, r.Vals.Length - 1);
                    if (nk != k) Gfx.Set(r.Key, r.Vals[nk]);
                }
            }
        }

        // HUD (UI/NewHud.cs, reglages UI/HudSettings.cs) : style, puis sa personnalisation, les jauges montrees et la coop. Le HUD
        // change aussitot autour du menu (apercu en direct). Memes cles que le lanceur (Reglages > HUD).
        static void HudTab()
        {
            int style = HudSettings.StyleNow;
            HudRow(HudSettings.Find("Style"));
            Header(Lang.T("PERSONNALISER", "CUSTOMISE"));
            if (style == HudSettings.Vanilla)
                Para(Lang.T("Vanilla : le HUD d'origine du jeu, tel quel. Le panneau \u00C9quipe et les alertes (plus bas) restent disponibles.",
                            "Vanilla: the game's own HUD, as is. The team panel and the alerts (below) stay available."), Style.Dim);
            else foreach (HudSettings.Opt o in HudSettings.Opts) if (o.Group == 1) HudRow(o);
            if (style != HudSettings.Vanilla)
            {
                Header(Lang.T("JAUGES AFFICH\u00C9ES", "GAUGES SHOWN"));
                foreach (HudSettings.Opt o in HudSettings.Opts) if (o.Group == 2) HudRow(o);
            }
            Header(Lang.T("COOP", "CO-OP"));
            foreach (HudSettings.Opt o in HudSettings.Opts) if (o.Group == 3) HudRow(o);
            if (style != HudSettings.Vanilla && Item(Lang.T("Remettre ce style par d\u00E9faut", "Reset this style to default"), null, false,
                                                      Lang.T("Taille, position, couleur, jauges et options reviennent \u00E0 leur valeur de d\u00E9part", "Size, position, colour, gauges and options go back to their defaults"), false) == 2)
                HudSettings.ResetCustom();
        }

        static void HudRow(HudSettings.Opt o)
        {
            if (o == null) return;
            int v = HudSettings.Get(o.Key), k = 0;
            string label = Lang.T(o.Fr, o.En), help = o.HelpFr != null ? Lang.T(o.HelpFr, o.HelpEn) : null;
            if (o.Vals == null) { if (ToggleRow(label, v != 0, help)) HudSettings.Set(o.Key, v != 0 ? 0 : 1); return; }
            for (int i = 0; i < o.Vals.Length; i++) if (o.Vals[i] == v) k = i;
            if (o.Vals.Length <= 4)
            {
                string[] labs = new string[o.Vals.Length];
                for (int i = 0; i < labs.Length; i++) labs[i] = Lang.T(o.Lf[i], o.Le[i]);
                int nk = SegRow(label, labs, k, help);
                if (nk >= 0) HudSettings.Set(o.Key, o.Vals[nk]);
                return;
            }
            int res = Item(label, Lang.T(o.Lf[k], o.Le[k]), true, help, false);
            if (res != -1 && res != 1) return;
            int nk2 = o.Key == "Style" ? (k + res + o.Vals.Length) % o.Vals.Length : Mathf.Clamp(k + res, 0, o.Vals.Length - 1);   // (le style tourne en boucle)
            if (nk2 != k) HudSettings.Set(o.Key, o.Vals[nk2]);
        }

        static void ChatTab()
        {
            if (Chat.History.Count == 0) Para(Lang.T("Aucun message pour l'instant.", "No messages yet."), Kit.Muted);
            foreach (string l in Chat.History) Item(l, null, false, null, true);
            ChatInput();
        }

        // Ligne de saisie du tchat (choisie = champ actif ; Entree ou ENVOYER : envoyer).
        static void ChatInput()
        {
            int me = idx++;
            lines++;
            float h = Style.Px(40);
            var r = new Rect(0, cy - scroll + Style.Px(6), cw, h);
            cy += h + Style.Px(8);
            bool selected = me == sel;
            if (selected) { selY0 = r.y + scroll; selY1 = selY0 + h; hint = Lang.T("Entr\u00E9e : envoyer le message", "Enter: send the message") + Sep + Lang.T("Haut : relire l'historique", "Up: read the history"); }
            float bw = Style.Px(116);
            var rb = new Rect(r.xMax - bw, r.y, bw, h);
            var rf = new Rect(r.x, r.y, r.width - bw - Style.Px(6), h);
            Event e = Event.current;
            bool send = false;
            if (Visible(r) && e.type == EventType.MouseDown && e.button == 0 && r.Contains(e.mousePosition))
            {
                sel = me;
                // (clic sur Envoyer : Kit.Button ; clic dans le champ : laisse au champ)
            }
            if (selected && kEnter) { kEnter = false; send = true; }
            if (e.type == EventType.Repaint)
            {
                Style.FieldBack(rf, selected);
            }
            // Le caractere de retour a la ligne qui suit Entree n'entre pas dans le champ (Key a deja pris Entree).
            if (e.type == EventType.KeyDown && IsNewline(e.character)) e.Use();
            GUI.SetNextControlName(ChatField);
            if (Kit.Button(rb, Lang.T("Envoyer", "Send"), 1, selected)) send = true;
            chatText = Clean(GUI.TextField(rf, chatText, 200, Style.Field(15)));
            fieldFocused = GUI.GetNameOfFocusedControl() == ChatField;
            if (selected && !fieldFocused) GUI.FocusControl(ChatField);
            else if (!selected && fieldFocused) { GUIUtility.keyboardControl = 0; fieldFocused = false; }
            if (send) { Chat.Send(chatText); chatText = ""; }
        }

        // T : ligne de tchat seule, en bas a gauche.
        static void DrawChatLine()
        {
            float h = Style.Px(40), rad = Style.Px(12);
            var r = new Rect(Style.Px(24), Screen.height - Style.Px(80), Mathf.Min(Style.Px(540), Screen.width - Style.Px(48)), h);
            Style.Glass(r, rad);
            Style.Ring(r, rad, Kit.A(Kit.Ice, 0.45f));
            string lab = Lang.T("Tchat", "Chat"), ent = Lang.T("Entr\u00E9e", "Enter");
            float lw = Style.Width(lab, 13) + Style.Px(26), kw = Style.Width(ent, 11) + Style.Px(12);
            Style.Text(new Rect(r.x + Style.Px(14), r.y, lw, h), lab, 13, TextAnchor.MiddleLeft, Kit.Ice);
            Kit.Keycap(r.xMax - kw - Style.Px(12), r.center.y, ent);
            // Entree AVANT le champ : le champ actif consomme la touche (le message ne partait pas, retour de JD
            // du 05/10) et le caractere '\n' qui la suit s'inscrivait dans le texte.
            Event e = Event.current;
            if (e.type == EventType.KeyDown && (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter || IsNewline(e.character)))
            {
                e.Use();
                Chat.Send(chatText);
                chatText = "";
                ChatOpen = false;
                GUIUtility.keyboardControl = 0;
                return;
            }
            GUI.SetNextControlName("mwcoop-chat");
            chatText = Clean(GUI.TextField(new Rect(r.x + lw, r.y, r.width - lw - kw - Style.Px(24), h), chatText, 200, Style.Field(15)));
            if (focusChat) { GUI.FocusControl("mwcoop-chat"); focusChat = false; }
        }
        static bool IsNewline(char c) { return c == '\n' || c == '\r'; }

        static string Clean(string s) { return s.IndexOf('\n') < 0 && s.IndexOf('\r') < 0 ? s : s.Replace("\r", "").Replace("\n", ""); }

        // ---- Autotest "menu" : ouvert a 20 s, un onglet toutes les 3 s (captures), ferme a la fin ----

        static int testTab = -1;
        static bool testLog, testEnd;

        // Autotest "tchat" : T a 20 s (texte pose), Entree simulee a 22 s (KeyDown Return comme Windows l'envoie,
        // puis le caractere retour a la ligne), message attendu chez l'autre et ligne fermee ; puis a 26 s le
        // caractere seul dans la ligne ouverte (ne doit pas s'inscrire), Entree a 28 s.
        static Event testKey;
        static int testStep;

        static void TestChat(float t)
        {
            if (testStep == 0 && t > 20f) { testStep = 1; ChatOpen = true; focusChat = true; chatText = Config.Get("Test", "TchatTexte", "essai du tchat " + Session.LocalId); Log.Info("autotest : tchat ouvert"); }
            else if (testStep == 1 && t > 22f) { testStep = 2; testKey = new Event { type = EventType.KeyDown, keyCode = KeyCode.Return }; }
            else if (testStep == 2 && t > 24f) { testStep = 3; Log.Info("autotest : tchat historique : " + string.Join(" | ", Chat.History.ToArray())); }
            else if (testStep == 3 && t > 26f) { testStep = 4; ChatOpen = true; chatText = "second " + Session.LocalId; testKey = new Event { type = EventType.KeyDown, character = '\n' }; }
            else if (testStep == 4 && t > 30f) { testStep = 5; Log.Info("autotest : tchat historique : " + string.Join(" | ", Chat.History.ToArray()) + " ; ligne " + (ChatOpen ? "ouverte" : "fermee")); }
        }

        // Autotest "langue" (traduction, 13/10) : les onglets comme "menu" (20 s, un toutes les 3 s), puis des textes du jeu
        // (police du jeu : cyrillique ?) a 37 s, la carte a 41 s, les triches a 45 s, le porte-monnaie a 49 s ; ferme a 53 s.
        static int langStep;
        static void TestLang(float t)
        {
            if (langStep == 0 && t > 37f) { langStep = 1; Game.SetGlobal("GUIinteraction", "ПРОВЕРКА ШРИФТА : ВЗЯТЬ"); Game.SetGlobal("GUIsubtitle", "Продавец: спасибо! Kiitos!"); Log.Info("autotest : langue, textes du jeu en cyrillique"); }
            if (langStep == 1 && t > 41f) { langStep = 2; Game.SetGlobal("GUIinteraction", ""); Game.SetGlobal("GUIsubtitle", ""); MapPanel.Open = true; }
            if (langStep == 2 && t > 45f) { langStep = 3; MapPanel.Open = false; CheatPanel.Open = true; Autotest.CaptureSoon("panneau-triches", 1.5f); }
            if (langStep == 3 && t > 49f) { langStep = 4; CheatPanel.Open = false; WalletPanel.Open = true; Autotest.CaptureSoon("panneau-porte-monnaie", 1.5f); }
            if (langStep == 4 && t > 53f) { langStep = 5; WalletPanel.Open = false; }
        }

        public static void Test(string mode, float t)
        {
            JokkisPanel.Test(mode, t);   // (Autotest=jokkispanel : panneau des Jokkis ouvert chez l'hote a 25 s, bilan a 30 s, ferme a 35 s)
            VazPanel.Test(mode, t);      // (Autotest=vazpanel : onglet VAZ-2108 ouvert chez l'hote a 25 s, bilan a 32 s, ferme a 35 s)
            if (mode == "tchat") { TestChat(t); return; }
            if (mode == "resync") { PlayerSync.TestResync(t); return; }   // (demande de l'invite a 30 s, bilan a 45 s)
            if (mode == "langue") TestLang(t);
            if ((mode != "menu" && mode != "langue") || testEnd || t < 20f) return;
            int k = (int)((t - 20f) / 3f);
            if (k >= tabs.Length)
            {
                testEnd = true;
                Open = false;
                Log.Info("autotest : menu ferme");
                return;
            }
            if (k == testTab) return;
            testTab = k;
            Open = true;
            SetTab(k);
            testLog = true;
            Autotest.CaptureSoon("menu-" + k, 1.5f);   // (captures de chaque onglet : ecran-menu-<n>.png)
        }
    }
}
