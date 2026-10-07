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
        static readonly string[] tabsFr = { "JOUEURS", "APPARENCE", "TCHAT", "SYNCHRO", "GRAPHISMES" }, tabsEn = { "PLAYERS", "APPEARANCE", "CHAT", "SYNC", "GRAPHICS" };
        static string[] tabs { get { return Lang.Fr ? tabsFr : tabsEn; } }
        const int TabChat = 2;
        const string ChatField = "mwcoop-chat2";
        const string Sep = "  ·  ";
        static string Controls { get { return Lang.T("Haut/Bas : choisir" + Sep + "Gauche/Droite : changer" + Sep + "Entr\u00E9e : valider" + Sep + "Tab, Q/E : onglet" + Sep + "F10/\u00C9chap : fermer",
                                                     "Up/Down: select" + Sep + "Left/Right: change" + Sep + "Enter: confirm" + Sep + "Tab, Q/E: tab" + Sep + "F10/Esc: close"); } }
        static string TabsHint { get { return Lang.T("Gauche/Droite : changer d'onglet" + Sep + "Bas ou Entr\u00E9e : entrer dans la liste",
                                                     "Left/Right: switch tab" + Sep + "Down or Enter: go to the list"); } }

        // Navigation : ligne choisie (-1 = bandeau des onglets), defilement, touches du passage OnGUI en cours.
        static int sel, items;
        static float scroll, contentH, viewH, cw, cy, selY0, selY1;
        static int idx, lines;
        static bool follow, kEnter, mouseMoved, typing, fieldFocused, wasOpen;
        static int kDir;
        static Vector2 lastMouse;
        static string hint;

        static readonly Color rowText = new Color(0.88f, 0.91f, 0.95f);
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
            if (Input.GetKeyDown(KeyCode.F10)) { Open = !Open; ChatOpen = false; }
            else if (Input.GetKeyDown(KeyCode.Escape) && (Open || ChatOpen)) { Open = ChatOpen = false; }
            else if (inGame && !Open && !ChatOpen && Session.Active && Input.GetKeyDown(KeyCode.T))
            {
                ChatOpen = true;
                focusChat = true;
                chatText = "";
            }
            Block(inGame && (Open || ChatOpen || Respawn.Choosing));
        }

        // Curseur du mod (Core le dessine en dernier) : menu ouvert ou choix de reapparition, en partie.
        // [Test] CurseurTest=x,y : dessine a ce point sans lire la souris (captures des instances d'essai).
        public static bool CursorWanted { get { return PlayerSync.InGame && (Open || Respawn.Choosing); } }

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
        static void Block(bool on)
        {
            if (on && blocked.Count == 0)
            {
                GameObject p = GameObject.Find("PLAYER");
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
            if (on) { Screen.lockCursor = false; Cursor.visible = false; }
        }

        public static void Draw()
        {
            if (ChatOpen && testKey != null && Event.current.type == EventType.Layout)
            {
                // Autotest "tchat" : la touche simulee remplace l'evenement de ce passage, comme une vraie frappe.
                Event real = Event.current;
                Event.current = testKey;
                testKey = null;
                DrawChatLine();
                Log.Info("autotest : tchat apres la touche : ligne " + (ChatOpen ? "OUVERTE" : "fermee") + ", texte \"" + chatText + "\"");
                Event.current = real;
                return;
            }
            if (ChatOpen) DrawChatLine();
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

            // Ouverture : fondu et leger glissement (0,16 s).
            float k = Mathf.Clamp01((Time.realtimeSinceStartup - openedAt) / 0.16f);
            k = 1f - (1f - k) * (1f - k);
            Style.Alpha = k;
            float w = Mathf.Min(Style.Px(1000), Screen.width - Style.Px(32));
            float h = Mathf.Min(Style.Px(800), Screen.height - Style.Px(32));
            float x = Mathf.Round((Screen.width - w) / 2), y = Mathf.Round((Screen.height - h) / 2 + (1f - k) * Style.Px(16));
            float pad = Style.Px(24), one = Mathf.Max(1f, Style.Px(1));
            Style.Glass(new Rect(x, y, w, h), Style.Px(22));

            // Entete : nom du mod, pastille de session, version.
            float hh = Style.Px(92);
            var head = new Rect(x + pad, y + Style.Px(10), w - 2 * pad, hh - Style.Px(14));
            // Le logo « my Winter Car coop » (a defaut, le nom en texte).
            float lw = Style.Logo(head);
            if (lw <= 0f) { Style.Title(head, "MWCoop", Style.White, TextAnchor.MiddleLeft, 36); lw = Style.TitleWidth("MWCoop", 36); }
            float nx = head.x + lw + Style.Px(14);
            string badge = !Session.Active ? "SOLO" : Session.IsHost ? Lang.T("H\u00D4TE", "HOST") : Lang.T("INVIT\u00C9", "GUEST");
            float bw = Style.Width(badge, 14) + Style.Px(22), bh = Style.Px(26);
            var br = new Rect(nx, head.center.y - bh / 2 + Style.Px(2), bw, bh);
            Style.Round(br, bh / 2, new Color(Style.Accent.r, Style.Accent.g, Style.Accent.b, 0.18f));
            Style.Ring(br, bh / 2, new Color(Style.Accent.r, Style.Accent.g, Style.Accent.b, 0.5f));
            Style.Text(br, badge, 14, TextAnchor.MiddleCenter, Style.Accent);
            Style.Text(head, Version.Text, 16, TextAnchor.MiddleRight, Style.Dim, false);

            // Onglets : piste arrondie, onglet actif en pilule d'accent.
            float th = Style.Px(48);
            var trk = new Rect(x + pad, y + hh, w - 2 * pad, th);
            Style.Round(trk, th / 2, trackBg);
            float inset = Style.Px(4), tw = (trk.width - 2 * inset) / tabs.Length;
            for (int i = 0; i < tabs.Length; i++)
            {
                var tr = new Rect(Mathf.Round(trk.x + inset + i * tw), trk.y + inset, Mathf.Round(tw), th - 2 * inset);
                bool on = i == tab;
                if (on)
                {
                    Style.Round(tr, tr.height / 2, Style.Accent);
                    if (sel < 0) Style.Ring(new Rect(tr.x - 2 * one, tr.y - 2 * one, tr.width + 4 * one, tr.height + 4 * one), tr.height / 2 + 2 * one, Style.White);
                }
                else if (tr.Contains(e.mousePosition)) Style.Round(tr, tr.height / 2, hoverBg);
                Style.Title(tr, tabs[i], on ? Style.OnAccent : Style.Dim, TextAnchor.MiddleCenter, 19);
                if (e.type == EventType.MouseDown && e.button == 0 && tr.Contains(e.mousePosition)) { SetTab(i); e.Use(); }
            }
            float top = y + hh + th + Style.Px(14);

            // Contenu de l'onglet (defile), puis pied de page.
            float fh = Style.Px(74);
            var view = new Rect(x + pad, top, w - 2 * pad, y + h - fh - Style.Px(6) - top);
            // APPARENCE : apercu 3D de la tenue en surbrillance, a droite de la liste (qui se retrecit d'autant).
            bool preview = tab == 1 && skins != null && skins.Count > 0 && Studio.LiveEnabled;
            Rect pvr = default(Rect);
            if (preview)
            {
                float ph = Mathf.Min(view.height - Style.Px(52), Style.Px(512)), pw = Mathf.Round(ph / 2f);
                pvr = new Rect(view.xMax - pw, view.y, pw, ph);
                view.width -= pw + Style.Px(16);
            }
            else dragging = false;
            viewH = view.height;
            if (e.type == EventType.ScrollWheel && view.Contains(e.mousePosition)) { scroll += e.delta.y * Style.Px(14); e.Use(); }
            scroll = Mathf.Clamp(scroll, 0, Mathf.Max(0, contentH - viewH));
            cw = view.width - Style.Px(14);
            idx = 0; lines = 0; cy = 0; selY0 = selY1 = -1;
            hint = sel < 0 ? TabsHint : null;
            GUI.BeginGroup(view);
            if (tab == 0) Players();
            else if (tab == 1) Skins();
            else if (tab == TabChat) ChatTab();
            else if (tab == 3) SyncTab();
            else GfxTab();
            GUI.EndGroup();
            contentH = cy;
            items = idx;
            if (contentH > viewH)
            {
                var track = new Rect(view.xMax - Style.Px(5), view.y, Mathf.Max(3f, Style.Px(5)), viewH);
                Style.Round(track, track.width / 2, line);
                float thumb = Mathf.Max(Style.Px(28), viewH * viewH / contentH);
                var tb = new Rect(track.x, track.y + (viewH - thumb) * scroll / (contentH - viewH), track.width, thumb);
                Style.Round(tb, tb.width / 2, Style.Accent);
            }
            if (preview) SkinPreview(pvr, e);

            // Fin du passage : selection bornee, Gauche/Droite non pris par une ligne = onglet suivant.
            if (sel >= items) sel = items - 1;
            if (kDir != 0 && sel >= 0) SetTab((tab + kDir + tabs.Length) % tabs.Length);
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

            float fy = y + h - fh;
            Style.Fill(new Rect(x + pad, fy, w - 2 * pad, one), line);
            if (hint != null) Style.Text(new Rect(x + pad, fy + Style.Px(10), w - 2 * pad, Style.Px(28)), hint, 18, TextAnchor.MiddleLeft, Style.White);
            Style.Text(new Rect(x + pad, fy + Style.Px(40), w - 2 * pad, Style.Px(24)), Controls, 15, TextAnchor.MiddleLeft, Style.Dim, false);
            Style.Alpha = 1f;

            if (testLog && e.type == EventType.Repaint) { testLog = false; Log.Info("autotest : menu onglet " + tabs[tab] + ", " + lines + " lignes"); }
        }

        // Touche (KeyDown) : vrai si le menu la prend (l'evenement est alors consomme).
        static bool Key(KeyCode k, bool shift)
        {
            switch (k)
            {
                case KeyCode.UpArrow: Move(-1); return true;
                case KeyCode.DownArrow: Move(1); return true;
                case KeyCode.PageUp: Move(-8); return true;
                case KeyCode.PageDown: Move(8); return true;
                case KeyCode.LeftArrow:
                case KeyCode.RightArrow:
                    if (typing) return false;   // curseur du champ de saisie
                    int d = k == KeyCode.LeftArrow ? -1 : 1;
                    if (sel < 0) SetTab((tab + d + tabs.Length) % tabs.Length); else kDir = d;
                    return true;
                case KeyCode.Return:
                case KeyCode.KeypadEnter:
                    if (sel < 0) Move(1); else kEnter = true;
                    return true;
                case KeyCode.Tab: SetTab((tab + (shift ? -1 : 1) + tabs.Length) % tabs.Length); return true;
                case KeyCode.Q:
                case KeyCode.E:
                    if (typing) return false;
                    SetTab((tab + (k == KeyCode.Q ? -1 : 1) + tabs.Length) % tabs.Length);
                    return true;
            }
            return false;
        }

        static void Move(int d)
        {
            if (sel < 0) { if (d > 0 && items > 0) sel = 0; }
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
            if (!heights.TryGetValue(s, out hh)) heights[s] = hh = Style.Height(s, size, cw - 2 * Style.Px(12), false);
            return hh;
        }

        static bool Visible(Rect r) { return r.yMax > 0 && r.y < viewH; }

        // Ligne choisissable : 0 rien, -1/+1 valeur changee (choice), 2 validee (Entree ou clic).
        // Image a gauche du libelle de la prochaine ligne (avatar Steam d'un joueur), remise a zero par Item.
        static Texture nextIcon;

        static int Item(string label, string value, bool choice, string help, bool wrap)
        {
            Texture icon = nextIcon; nextIcon = null;
            int me = idx++;
            lines++;
            float ip = Style.Px(12), h = Style.Px(38);
            if (wrap) h = Mathf.Max(h, WrapHeight(label, 19) + Style.Px(10));
            var r = new Rect(0, cy - scroll, cw, h);
            cy += h + Style.Px(2);
            bool selected = me == sel;
            if (selected) { selY0 = r.y + scroll; selY1 = selY0 + h; if (help != null) hint = help; }

            // Fleches "< valeur >" a droite (choix).
            float aw = Style.Px(22);
            Rect ra = default(Rect), rv = default(Rect), la = default(Rect);
            if (choice && value != null)
            {
                ra = new Rect(r.xMax - ip - aw, r.y, aw, h);
                float vw = Mathf.Min(Style.Width(value, 21), r.width * 0.45f);
                rv = new Rect(ra.x - Style.Px(6) - vw, r.y, vw, h);
                la = new Rect(rv.x - Style.Px(6) - aw, r.y, aw, h);
            }

            int res = 0;
            Event e = Event.current;
            bool vis = Visible(r);
            if (vis && e.type == EventType.MouseDown && e.button == 0 && r.Contains(e.mousePosition))
            {
                sel = me;
                res = choice ? (e.mousePosition.x <= la.xMax ? -1 : 1) : 2;
                e.Use();
            }
            else if (vis && mouseMoved && !typing && r.Contains(e.mousePosition)) sel = me;
            else if (selected && kEnter) { kEnter = false; res = choice ? 1 : 2; }
            else if (selected && choice && kDir != 0) { res = kDir; kDir = 0; }

            if (!vis || e.type != EventType.Repaint) return res;
            float rr = Style.Px(10);
            if (selected) { Style.Round(r, rr, selFill); Style.Ring(r, rr, selEdge); }
            Color tc = selected ? Style.White : rowText;
            if (icon != null && !wrap)
            {
                float isz = h - Style.Px(10);
                Color gc = GUI.color;
                GUI.color = new Color(1f, 1f, 1f, Style.Alpha);
                GUI.DrawTexture(new Rect(r.x + ip, r.y + (h - isz) / 2, isz, isz), icon);
                GUI.color = gc;
                r.x += isz + Style.Px(8); r.width -= isz + Style.Px(8);
            }
            if (wrap) Style.Text(new Rect(r.x + ip, r.y + Style.Px(5), r.width - 2 * ip, h - Style.Px(10)), label, 19, TextAnchor.UpperLeft, tc, false, true);
            else Style.Text(new Rect(r.x + ip, r.y, (value != null ? r.width * 0.6f : r.width) - 2 * ip, h), label, 20, TextAnchor.MiddleLeft, tc, false);
            if (value == null) return res;
            if (choice)
            {
                Color ac = selected ? Style.White : Style.Accent;
                float cs = Mathf.Min(aw + Style.Px(4), h - Style.Px(10));
                var lc = new Rect(la.center.x - cs / 2, la.center.y - cs / 2, cs, cs);
                var rc = new Rect(ra.center.x - cs / 2, ra.center.y - cs / 2, cs, cs);
                Style.Round(lc, cs / 2, hoverBg);
                Style.Round(rc, cs / 2, hoverBg);
                Style.Text(new Rect(la.x, la.y - Style.Px(1), la.width, la.height), "<", 19, TextAnchor.MiddleCenter, ac);
                Style.Text(rv, value, 20, TextAnchor.MiddleRight, tc);
                Style.Text(new Rect(ra.x, ra.y - Style.Px(1), ra.width, ra.height), ">", 19, TextAnchor.MiddleCenter, ac);
            }
            else Style.Text(new Rect(r.x + r.width * 0.4f, r.y, r.width * 0.6f - ip, h), value, 18, TextAnchor.MiddleRight, selected ? Style.Accent : Style.Dim);
            return res;
        }

        // Paragraphe non choisissable (texte replie).
        static void Para(string s, Color c)
        {
            lines++;
            float ip = Style.Px(12);
            float h = WrapHeight(s, 19) + Style.Px(10);
            var r = new Rect(ip, cy - scroll + Style.Px(4), cw - 2 * ip, h);
            cy += h + Style.Px(4);
            if (Visible(r) && Event.current.type == EventType.Repaint) Style.Text(r, s, 19, TextAnchor.UpperLeft, c, false, true);
        }

        // Entete de section : titre bleu souligne.
        static void Header(string s)
        {
            lines++;
            float h = Style.Px(44);
            var r = new Rect(0, cy - scroll, cw, h);
            cy += h + Style.Px(4);
            if (!Visible(r) || Event.current.type != EventType.Repaint) return;
            Style.Title(new Rect(r.x + Style.Px(12), r.y + Style.Px(8), r.width - Style.Px(24), h - Style.Px(12)), s, Style.Accent, TextAnchor.MiddleLeft, 18);
            Style.Fill(new Rect(r.x + Style.Px(12), r.yMax - Style.Px(3), r.width - Style.Px(24), Mathf.Max(1f, Style.Px(1))), line);
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
            Para(Session.Active ? Lang.Status(Session.Status) : Lang.T("Solo (lancez le jeu depuis MWCoop.exe pour jouer \u00E0 plusieurs)", "Solo (start the game from MWCoop.exe to play with friends)"), Style.Dim);
            RefreshPlayers();
            Header(Lang.T("JOUEURS", "PLAYERS"));
            for (int i = 0; i < prows.Count; i++)
            {
                PlayerInfo pi = prows[i].P;
                bool canGo = !pi.Local && pi.Level == 1 && PlayerSync.InGame;
                nextIcon = MWCoop.Net.SteamNet.Avatar(pi.SteamId);
                if (Item(prows[i].Label, prows[i].Value, false, canGo ? Lang.T("Entr\u00E9e ou clic : aller vers ce joueur", "Enter or click: go to this player") : null, false) == 2 && canGo) GoTo(pi);
            }
            // Hote : faire venir un joueur a soi, ou l'exclure (demandes de JD, 07/10).
            if (Session.Active && Session.IsHost && Session.RemoteCount > 0)
            {
                Header(Lang.T("H\u00D4TE", "HOST"));
                for (int i = 0; i < prows.Count; i++)
                {
                    PlayerInfo pi = prows[i].P;
                    if (pi.Local) continue;
                    bool canBring = pi.Level == 1 && PlayerSync.InGame;
                    if (Item(Lang.T("Faire venir ", "Bring here: ") + pi.Name, canBring ? null : Lang.T("pas en partie", "not in game"), false,
                             Lang.T("Entr\u00E9e ou clic : ce joueur arrive pr\u00E8s de vous", "Enter or click: this player comes next to you"), false) == 2 && canBring)
                    { MWCoop.Net.Admin.Summon(pi.Id); Open = false; }
                    bool armed = kickArmed == pi.Id && Time.realtimeSinceStartup - kickArmedAt < 4f;
                    if (Item(Lang.T("Exclure ", "Kick ") + pi.Name, armed ? Lang.T("cliquez encore pour confirmer", "click again to confirm") : null, false,
                             Lang.T("Deux clics : ce joueur est d\u00E9connect\u00E9 de la partie", "Two clicks: this player is disconnected from the game"), false) == 2)
                    {
                        if (armed) { MWCoop.Net.Admin.Kick(pi.Id, "menu F10"); kickArmed = -1; }
                        else { kickArmed = pi.Id; kickArmedAt = Time.realtimeSinceStartup; }
                    }
                }
            }
            // Partie par Steam : invitation d'amis (overlay Steam) depuis le menu.
            if (Session.Active && Session.Steam && Item(Lang.T("Inviter des amis Steam", "Invite Steam friends"), MWCoop.Net.SteamNet.Lobby != 0 ? "" : Lang.T("salon en cours...", "lobby starting..."), false,
                     Lang.T("Ouvre la fen\u00EAtre d'invitation de Steam (aussi : Maj+Tab). Ils lancent MWCoop.exe > REJOINDRE (Steam) et acceptent.",
                            "Opens Steam's invite window (also: Shift+Tab). They start MWCoop.exe > JOIN (Steam) and accept."), false) == 2)
                MWCoop.Net.SteamNet.InviteDialog();
        }

        static void GoTo(PlayerInfo pi)
        {
            if (VehicleSync.LocalDriving >= 0 || Seats.Seated) { Hud.Toast(Lang.T("Sortez d'abord du v\u00E9hicule", "Get out of the vehicle first")); return; }
            TeleportTo(pi.State.Feet, pi.State.Yaw, pi.Name);
        }

        // Pres de quelqu'un (pieds, cap) : 1,5 m derriere lui, un peu au-dessus du sol (la gravite fait le reste).
        public static void TeleportTo(Vector3 feet, float yaw, string who)
        {
            GameObject p = GameObject.Find("PLAYER");
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
            Para(Lang.T("Votre apparence vue par les autres joueurs.", "Your outfit, as the other players see it."), Style.Dim);
            string me = Looks.Parse(Session.Me.Skin).Shirt;   // (le haut ; le reste : lanceur, volet TENUE)
            int cur = skins.IndexOf(me);
            if (me != lastSkin) { lastSkin = me; lastSkinLabel = SkinLabel(me); }
            int d = Item(Lang.T("Apparence", "Outfit"), cur >= 0 ? skinLabels[cur] : lastSkinLabel, true, Lang.T("Gauche/Droite : changer d'apparence", "Left/Right: change outfit"), false);
            if (d == 1 || d == -1) SetSkin(skins[((cur < 0 ? 0 : cur) + d + skins.Count) % skins.Count]);
            // le reste de l'apparence (Looks) : corpulence, pantalon, visage, chapeau, lunettes, cheveux
            Looks.Look look = Looks.Parse(Session.Me.Skin);
            for (int f = 0; f < LookRows; f++)
            {
                string v = Looks.Get(look, f);
                int dd = Item(Looks.PartName(f), Looks.Label(f, v), true, Lang.T("Gauche/Droite : changer (les autres le voient aussit\u00F4t)", "Left/Right: change (the others see it right away)"), false);
                if (dd != 1 && dd != -1) continue;
                List<string> ch = Looks.Choices(f);
                int i = Mathf.Max(0, ch.IndexOf(v));
                SetPart(f, ch[(i + dd + ch.Count) % ch.Count]);
            }
            Header(Lang.T("TENUES", "OUTFITS"));
            for (int j = 0; j < skins.Count; j++)
                if (Item(skinLabels[j], j == cur ? Lang.T("PORT\u00C9E", "WORN") : null, false, Lang.T("Entr\u00E9e ou clic : porter cette tenue", "Enter or click: wear this outfit"), false) == 2) SetSkin(skins[j]);
        }

        static bool dragging;
        static string pvSkin, pvLabel;
        static readonly Color previewBg = new Color(0f, 0.02f, 0.06f, 0.30f);

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
            float rad = Style.Px(16);
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
            Style.Ring(r, rad, line);
            if (s == me && s.Length > 0)
            {
                float pw = Style.Width(Lang.T("PORT\u00C9E", "WORN"), 13) + Style.Px(18), ph = Style.Px(24);
                var pr = new Rect(r.xMax - pw - Style.Px(10), r.y + Style.Px(10), pw, ph);
                Style.Round(pr, ph / 2, Style.Accent);
                Style.Text(pr, Lang.T("PORT\u00C9E", "WORN"), 13, TextAnchor.MiddleCenter, Style.OnAccent);
            }
            var lr = new Rect(r.x, r.yMax + Style.Px(8), r.width, Style.Px(40));
            Style.Round(lr, lr.height / 2, new Color(1f, 1f, 1f, 0.08f));
            Style.Ring(lr, lr.height / 2, line);
            Style.Title(lr, pvLabel, Style.White, TextAnchor.MiddleCenter, 19);
        }

        // Autotest "tenues" (Studio.Test) : k >= 0 ouvre le menu sur APPARENCE avec une tenue en surbrillance (rendue),
        // k < 0 le ferme.
        static int testSel = -1;

        public static string TestApparence(int k)
        {
            if (k < 0) { Open = false; return null; }
            Open = true;
            SetTab(1);
            LoadSkins();
            if (skins == null || skins.Count == 0) { testSel = 0; return "(aucune tenue)"; }
            int j = k * Mathf.Max(1, skins.Count / 5) % skins.Count;
            testSel = 1 + j;
            return skins[j] + " (" + skinLabels[j] + ")";
        }

        public static string SkinLabel(string s)
        {
            if (s.StartsWith("char_shirt")) return Lang.T("Tenue ", "Outfit ") + s.Substring(10).TrimStart('0');
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
            Looks.Set(l, f, v);
            Session.Me.Skin = Looks.Compose(l);
            Config.Save("Coop", Looks.PartKeys[f], v);
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
            if (Time.realtimeSinceStartup >= syncAt)
            {
                syncAt = Time.realtimeSinceStartup + 0.5f;
                syncHead = Audit.State() + (Audit.Summary.Length > 0 ? "\n" + Audit.Summary : "");
            }
            Para(syncHead, Style.Dim);
            if (Item(Lang.T("\u00C9crire le recensement (dumps/recensement.txt)", "Write the census (dumps/recensement.txt)"), null, false, Lang.T("Entr\u00E9e ou clic : \u00E9crire le recensement", "Enter or click: write the census"), false) == 2) Audit.Census();
            Header(Lang.T("\u00C9CARTS AVEC LES INVIT\u00C9S", "DIFFERENCES WITH THE GUESTS"));
            Para(Session.IsHost ? Lang.T("H\u00F4te : tout le monde est compar\u00E9 \u00E0 vous.", "Host: everyone is compared to you.") : Lang.T("\u00C9carts : vus par l'h\u00F4te (son onglet SYNCHRO et son journal).", "Differences: seen by the host (their SYNC tab and log)."), Style.Dim);
            foreach (string l in Audit.Desyncs) Item(l, null, false, null, true);
            Header(Lang.T("VOS ACTIONS QUI NE PARTENT PAS CHEZ LES AUTRES", "YOUR ACTIONS NOT SHARED WITH THE OTHERS"));
            foreach (string l in Audit.Unshared) Item(l, null, false, null, true);
        }

        // Graphismes (comme la page du lanceur) : prereglage, puis chaque option ; applique tout de suite, garde dans mwcoop.ini.
        static void GfxTab()
        {
            Para(Lang.T("Pour vous seul : les autres joueurs ne voient aucune diff\u00E9rence. Gard\u00E9 pour les prochaines parties (aussi dans le lanceur).",
                        "Just for you: the other players see no difference. Kept for your next games (also in the launcher)."), Style.Dim);
            int on = Gfx.PresetOn();
            int d = Item(Lang.T("Pr\u00E9r\u00E9glage", "Preset"), on >= 0 ? (Lang.Fr ? Gfx.Presets[on].Fr : Gfx.Presets[on].En) : Lang.T("Personnalis\u00E9", "Custom"), true,
                         Lang.T("Gauche/Droite : Performance, \u00C9quilibr\u00E9, Beau, Ultra", "Left/Right: Performance, Balanced, Pretty, Ultra"), false);
            if (d == -1 || d == 1) Gfx.ApplyPreset(on < 0 ? (d > 0 ? 0 : Gfx.Presets.Length - 1) : Mathf.Clamp(on + d, 0, Gfx.Presets.Length - 1));
            string[] heads = { Lang.T("IMAGE", "IMAGE"), Lang.T("OMBRES", "SHADOWS"), Lang.T("DISTANCE ET D\u00C9TAILS", "DISTANCE AND DETAIL"), Lang.T("JEU ET CONFORT", "GAME AND COMFORT"), Lang.T("LUMI\u00C8RES", "LIGHTS") };
            foreach (int g in new[] { 0, 1, 4, 2, 3 })
            {
                Header(heads[g]);
                foreach (Gfx.Row r in Gfx.Rows)
                {
                    if (r.Group != g) continue;
                    int v = Gfx.Value(r);
                    string val;
                    int k = 0;
                    if (r.Vals == null) val = v == 1 ? Lang.T("Oui", "On") : Lang.T("Non", "Off");
                    else { for (int i = 0; i < r.Vals.Length; i++) if (r.Vals[i] == v) k = i; val = Lang.Fr ? r.Lf[k] : r.Le[k]; }
                    int res = Item(Lang.Fr ? r.Fr : r.En, val, true, r.Heavy ? Lang.T("Lourd pour les images par seconde", "Heavy on the frame rate") : null, false);
                    if (res != -1 && res != 1) continue;
                    if (r.Vals == null) Gfx.Set(r.Key, v == 1 ? 0 : 1);
                    else { int nk = Mathf.Clamp(k + res, 0, r.Vals.Length - 1); if (nk != k) Gfx.Set(r.Key, r.Vals[nk]); }
                }
            }
        }

        static void ChatTab()
        {
            Para(Chat.History.Count == 0 ? Lang.T("Aucun message. En jeu, T \u00E9crit sans ouvrir ce menu.", "No messages yet. In game, T opens the chat without this menu.")
                                         : Lang.T("En jeu, T \u00E9crit sans ouvrir ce menu.", "In game, T opens the chat without this menu."), Style.Dim);
            foreach (string l in Chat.History) Item(l, null, false, null, true);
            ChatInput();
        }

        // Ligne de saisie du tchat (choisie = champ actif ; Entree ou ENVOYER : envoyer).
        static void ChatInput()
        {
            int me = idx++;
            lines++;
            float h = Style.Px(44);
            var r = new Rect(0, cy - scroll + Style.Px(4), cw, h);
            cy += h + Style.Px(6);
            bool selected = me == sel;
            if (selected) { selY0 = r.y + scroll; selY1 = selY0 + h; hint = Lang.T("Entr\u00E9e : envoyer le message" + Sep + "Haut : relire l'historique", "Enter: send the message" + Sep + "Up: read the history"); }
            float bw = Style.Px(150);
            var rb = new Rect(r.xMax - bw, r.y, bw, h);
            var rf = new Rect(r.x, r.y, r.width - bw - Style.Px(6), h);
            Event e = Event.current;
            bool send = false;
            if (Visible(r) && e.type == EventType.MouseDown && e.button == 0 && r.Contains(e.mousePosition))
            {
                sel = me;
                if (rb.Contains(e.mousePosition)) { send = true; e.Use(); }   // (clic dans le champ : laisse au champ)
            }
            if (selected && kEnter) { kEnter = false; send = true; }
            if (e.type == EventType.Repaint)
            {
                Style.FieldBack(rf, selected);
                Style.Button(rb, Lang.T("ENVOYER", "SEND"), true, 18, selected);
            }
            // Le caractere de retour a la ligne qui suit Entree n'entre pas dans le champ (Key a deja pris Entree).
            if (e.type == EventType.KeyDown && IsNewline(e.character)) e.Use();
            GUI.SetNextControlName(ChatField);
            chatText = Clean(GUI.TextField(rf, chatText, 200, Style.Field(21)));
            fieldFocused = GUI.GetNameOfFocusedControl() == ChatField;
            if (selected && !fieldFocused) GUI.FocusControl(ChatField);
            else if (!selected && fieldFocused) { GUIUtility.keyboardControl = 0; fieldFocused = false; }
            if (send) { Chat.Send(chatText); chatText = ""; }
        }

        // T : ligne de tchat seule, en bas a gauche.
        static void DrawChatLine()
        {
            float h = Style.Px(42);
            var r = new Rect(Style.Px(24), Screen.height - Style.Px(84), Mathf.Min(Style.Px(760), Screen.width - Style.Px(48)), h);
            Style.Glass(r, h / 2);
            Style.Ring(r, h / 2, new Color(Style.Accent.r, Style.Accent.g, Style.Accent.b, 0.55f));
            float lw = Style.Px(104);
            Style.Title(new Rect(r.x + Style.Px(20), r.y, lw, h), Lang.T("TCHAT", "CHAT"), Style.Accent, TextAnchor.MiddleLeft, 18);
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
            chatText = Clean(GUI.TextField(new Rect(r.x + lw, r.y, r.width - lw, h), chatText, 200, Style.Field(21)));
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

        public static void Test(string mode, float t)
        {
            if (mode == "tchat") { TestChat(t); return; }
            if (mode != "menu" || testEnd || t < 20f) return;
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
        }
    }
}
