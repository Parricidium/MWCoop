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
        static readonly string[] tabs = { "JOUEURS", "APPARENCE", "TCHAT", "SYNCHRO" };
        const int TabChat = 2;
        const string ChatField = "mwcoop-chat2";
        const string Sep = "  ·  ";
        const string Controls = "Haut/Bas : choisir" + Sep + "Gauche/Droite : changer" + Sep + "Entree : valider" + Sep + "Tab, Q/E : onglet" + Sep + "F10/Echap : fermer";
        const string TabsHint = "Gauche/Droite : changer d'onglet" + Sep + "Bas ou Entree : entrer dans la liste";

        // Navigation : ligne choisie (-1 = bandeau des onglets), defilement, touches du passage OnGUI en cours.
        static int sel, items;
        static float scroll, contentH, viewH, cw, cy, selY0, selY1;
        static int idx, lines;
        static bool follow, kEnter, mouseMoved, typing, fieldFocused, wasOpen;
        static int kDir;
        static Vector2 lastMouse;
        static string hint;

        static readonly Color rowText = new Color(0.93f, 0.93f, 0.93f);
        static readonly Color tabOn = new Color(Style.Accent.r, Style.Accent.g, Style.Accent.b, 0.22f);
        static readonly Color tabStrip = new Color(Style.Accent.r, Style.Accent.g, Style.Accent.b, 0.45f);
        static readonly Color line = new Color(1f, 1f, 1f, 0.15f);
        static readonly Color selBar = new Color(Style.Accent.r, Style.Accent.g, Style.Accent.b, 0.9f);
        static readonly Color fieldBg = new Color(1f, 1f, 1f, 0.08f);
        static readonly Color buttonOff = new Color(1f, 1f, 1f, 0.12f);

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
            if (on) { Screen.lockCursor = false; Cursor.visible = true; }
        }

        public static void Draw()
        {
            if (ChatOpen) DrawChatLine();
            if (!Open) { wasOpen = false; return; }
            Event e = Event.current;
            if (!wasOpen)
            {
                wasOpen = true;
                lastMouse = e.mousePosition;
                sel = tab == TabChat ? 9999 : 0;
                scroll = 0;
                follow = true;
                if (fieldFocused) { GUIUtility.keyboardControl = 0; fieldFocused = false; }
            }
            typing = tab == TabChat && fieldFocused;
            if (e.type == EventType.KeyDown && e.keyCode != KeyCode.None && Key(e.keyCode, e.shift)) e.Use();
            mouseMoved = false;
            if (e.type == EventType.Repaint)
            {
                mouseMoved = (e.mousePosition - lastMouse).sqrMagnitude > 1f;
                lastMouse = e.mousePosition;
            }

            float titleH = Style.Px(84);
            float w = Mathf.Min(Style.Px(980), Screen.width - Style.Px(32));
            float h = Mathf.Min(Style.Px(700), Screen.height - titleH - Style.Px(32));
            float x = Mathf.Round((Screen.width - w) / 2), y = Mathf.Round((Screen.height - titleH - h) / 2);
            float pad = Style.Px(18), one = Mathf.Max(1f, Style.Px(1));

            // Titre au-dessus du panneau.
            Style.Title(new Rect(x, y, w, titleH), "MWCoop");
            Style.Text(new Rect(x, y, w, titleH - Style.Px(10)), Version.Text, 18, TextAnchor.LowerRight, Style.Dim);
            y += titleH;
            Style.Panel(new Rect(x, y, w, h));

            // Bandeau des onglets.
            float th = Style.Px(54), tw = w / tabs.Length;
            for (int i = 0; i < tabs.Length; i++)
            {
                var tr = new Rect(x + i * tw, y, tw, th);
                bool on = i == tab;
                if (on)
                {
                    Style.Fill(tr, sel < 0 ? tabStrip : tabOn);
                    Style.Fill(new Rect(tr.x, tr.yMax - Style.Px(4), tr.width, Style.Px(4)), Style.Accent);
                }
                Style.Title(tr, tabs[i], on ? Style.White : Style.Dim, TextAnchor.MiddleCenter, 30);
                if (e.type == EventType.MouseDown && e.button == 0 && tr.Contains(e.mousePosition)) { SetTab(i); e.Use(); }
            }
            Style.Fill(new Rect(x, y + th, w, one), line);

            // Contenu de l'onglet (defile), puis pied de page.
            float fh = Style.Px(72);
            var view = new Rect(x + pad, y + th + Style.Px(12), w - 2 * pad, h - th - Style.Px(12) - fh - Style.Px(6));
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
            else SyncTab();
            GUI.EndGroup();
            contentH = cy;
            items = idx;
            if (contentH > viewH)
            {
                var track = new Rect(view.xMax - Style.Px(5), view.y, Mathf.Max(2f, Style.Px(4)), viewH);
                Style.Fill(track, line);
                float thumb = Mathf.Max(Style.Px(24), viewH * viewH / contentH);
                Style.Fill(new Rect(track.x, track.y + (viewH - thumb) * scroll / (contentH - viewH), track.width, thumb), Style.Accent);
            }

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
            if (hint != null) Style.Text(new Rect(x + pad, fy + Style.Px(6), w - 2 * pad, Style.Px(30)), hint, 19, TextAnchor.MiddleLeft, Style.White);
            Style.Text(new Rect(x + pad, fy + Style.Px(38), w - 2 * pad, Style.Px(26)), Controls, 16, TextAnchor.MiddleLeft, Style.Dim, false);

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
        static int Item(string label, string value, bool choice, string help, bool wrap)
        {
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
            if (selected) Style.Fill(r, selBar);
            Color tc = selected ? Style.White : rowText;
            if (wrap) Style.Text(new Rect(r.x + ip, r.y + Style.Px(5), r.width - 2 * ip, h - Style.Px(10)), label, 19, TextAnchor.UpperLeft, tc, false, true);
            else Style.Text(new Rect(r.x + ip, r.y, (value != null ? r.width * 0.6f : r.width) - 2 * ip, h), label, 21, TextAnchor.MiddleLeft, tc);
            if (value == null) return res;
            if (choice)
            {
                Color ac = selected ? Style.White : Style.Accent;
                Style.Text(la, "<", 21, TextAnchor.MiddleCenter, ac);
                Style.Text(rv, value, 21, TextAnchor.MiddleRight, tc);
                Style.Text(ra, ">", 21, TextAnchor.MiddleCenter, ac);
            }
            else Style.Text(new Rect(r.x + r.width * 0.4f, r.y, r.width * 0.6f - ip, h), value, 19, TextAnchor.MiddleRight, selected ? Style.White : Style.Dim);
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
            Style.Title(new Rect(r.x + Style.Px(12), r.y + Style.Px(6), r.width - Style.Px(24), h - Style.Px(10)), s, Style.Accent, TextAnchor.MiddleLeft, 26);
            Style.Fill(new Rect(r.x, r.yMax - Style.Px(3), r.width, Mathf.Max(1f, Style.Px(2))), line);
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
                r.Label = (pi.Id == 0 ? "[hote] " : "") + pi.Name + (pi.Local ? " (vous)" : "");
                string where = pi.Level == 1 ? "en partie" : "au menu";
                r.Value = !pi.Local && pi.Peer != null ? where + "  " + Mathf.RoundToInt(pi.Peer.Rtt * 1000) + " ms" : where;
            }
            prows.RemoveRange(n, prows.Count - n);
        }

        static void Players()
        {
            Para(Session.Active ? Session.Status : "Solo (lancez le jeu depuis MWCoop.exe pour jouer a plusieurs)", Style.Dim);
            RefreshPlayers();
            Header("JOUEURS");
            for (int i = 0; i < prows.Count; i++)
            {
                PlayerInfo pi = prows[i].P;
                bool canGo = !pi.Local && pi.Level == 1 && PlayerSync.InGame;
                if (Item(prows[i].Label, prows[i].Value, false, canGo ? "Entree ou clic : aller vers ce joueur" : null, false) == 2 && canGo) GoTo(pi);
            }
        }

        static void GoTo(PlayerInfo pi)
        {
            GameObject p = GameObject.Find("PLAYER");
            if (p == null) return;
            Vector3 target = pi.State.Feet + Quaternion.Euler(0, pi.State.Yaw, 0) * new Vector3(0, 0, -1.5f) + Vector3.up * 1.0f;
            var cc = p.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;
            p.transform.position = target;
            if (cc != null) cc.enabled = true;
            Log.Info("teleporte vers " + pi.Name);
            Open = false;
        }

        static string lastSkin, lastSkinLabel;

        static void Skins()
        {
            if ((skins == null || skins.Count == 0) && PlayerSync.InGame)
            {
                skins = Avatar.SkinNames();
                skinLabels.Clear();
                foreach (string s in skins) skinLabels.Add(SkinLabel(s));
            }
            if (skins == null || skins.Count == 0) { Para("Les apparences se choisissent en partie.", Style.Dim); return; }
            Para("Votre apparence vue par les autres joueurs.", Style.Dim);
            string me = Session.Me.Skin ?? "";
            int cur = skins.IndexOf(me);
            if (me != lastSkin) { lastSkin = me; lastSkinLabel = SkinLabel(me); }
            int d = Item("Apparence", cur >= 0 ? skinLabels[cur] : lastSkinLabel, true, "Gauche/Droite : changer d'apparence", false);
            if (d == 1 || d == -1) SetSkin(skins[((cur < 0 ? 0 : cur) + d + skins.Count) % skins.Count]);
            Header("TENUES");
            for (int j = 0; j < skins.Count; j++)
                if (Item(skinLabels[j], j == cur ? "PORTEE" : null, false, "Entree ou clic : porter cette tenue", false) == 2) SetSkin(skins[j]);
        }

        public static string SkinLabel(string s)
        {
            if (s.StartsWith("char_shirt")) return "Tenue " + s.Substring(10).TrimStart('0');
            switch (s)
            {
                case "cop_shirt": return "Policier";
                case "cop_shirt2": return "Policier 2";
                case "rally_shirt": return "Pilote de rallye";
                case "psk_shirt": return "Employe PSK";
                case "inspector_shirt": return "Inspecteur";
            }
            return s;
        }

        static void SetSkin(string s)
        {
            Session.Me.Skin = s;
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
            if (Item("Ecrire le recensement (dumps/recensement.txt)", null, false, "Entree ou clic : ecrire le recensement", false) == 2) Audit.Census();
            Header("ECARTS AVEC LES INVITES");
            Para(Session.IsHost ? "Hote : tout le monde est compare a vous." : "Ecarts : vus par l'hote (son onglet SYNCHRO et son journal).", Style.Dim);
            foreach (string l in Audit.Desyncs) Item(l, null, false, null, true);
            Header("VOS ACTIONS QUI NE PARTENT PAS CHEZ LES AUTRES");
            foreach (string l in Audit.Unshared) Item(l, null, false, null, true);
        }

        static void ChatTab()
        {
            Para(Chat.History.Count == 0 ? "Aucun message. En jeu, T ecrit sans ouvrir ce menu." : "En jeu, T ecrit sans ouvrir ce menu.", Style.Dim);
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
            if (selected) { selY0 = r.y + scroll; selY1 = selY0 + h; hint = "Entree : envoyer le message" + Sep + "Haut : relire l'historique"; }
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
                Style.Fill(rf, fieldBg);
                Style.Fill(new Rect(rf.x, rf.yMax - Style.Px(3), rf.width, Style.Px(3)), selected ? Style.Accent : line);
                Style.Fill(rb, selected ? Style.Accent : buttonOff);
                Style.Title(rb, "ENVOYER", Style.White, TextAnchor.MiddleCenter, 26);
            }
            GUI.SetNextControlName(ChatField);
            chatText = GUI.TextField(rf, chatText, 200, Style.Field(21));
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
            Style.Panel(r);
            Style.Fill(new Rect(r.x, r.yMax - Style.Px(3), r.width, Style.Px(3)), Style.Accent);
            float lw = Style.Px(104);
            Style.Title(new Rect(r.x + Style.Px(12), r.y, lw, h), "TCHAT", Style.Accent, TextAnchor.MiddleLeft, 26);
            GUI.SetNextControlName("mwcoop-chat");
            chatText = GUI.TextField(new Rect(r.x + lw, r.y, r.width - lw, h), chatText, 200, Style.Field(21));
            if (focusChat) { GUI.FocusControl("mwcoop-chat"); focusChat = false; }
            if (EnterPressed())
            {
                Chat.Send(chatText);
                chatText = "";
                ChatOpen = false;
            }
        }

        static bool EnterPressed()
        {
            Event e = Event.current;
            return e.type == EventType.KeyDown && (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter);
        }

        // ---- Autotest "menu" : ouvert a 20 s, un onglet toutes les 3 s (captures), ferme a la fin ----

        static int testTab = -1;
        static bool testLog, testEnd;

        public static void Test(string mode, float t)
        {
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
