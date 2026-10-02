using System.Collections.Generic;
using MWCoop.Net;
using UnityEngine;

namespace MWCoop
{
    // F10 : menu coop (joueurs, apparence, tchat). T : ecrire dans le tchat. Echap : fermer.
    // Tant qu'il est ouvert, le jeu ne recoit plus la souris ni les deplacements.
    public static class Menu
    {
        public static bool Open, ChatOpen;
        static int tab;
        static string chatText = "";
        static Vector2 scroll;
        static List<string> skins;
        static readonly List<Behaviour> blocked = new List<Behaviour>();
        static bool focusChat, testDone;
        static readonly string[] tabs = { "JOUEURS", "APPARENCE", "TCHAT" };
        static GUIStyle title, small, button, active;

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
            Block(inGame && (Open || ChatOpen));
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

        static void Styles()
        {
            if (title != null) return;
            title = new GUIStyle(GUI.skin.label) { fontSize = 16, fontStyle = FontStyle.Bold };
            small = new GUIStyle(GUI.skin.label) { fontSize = 12, wordWrap = true };
            button = new GUIStyle(GUI.skin.button) { fontSize = 13 };
            active = new GUIStyle(button) { fontStyle = FontStyle.Bold };
            active.normal.textColor = new Color(0.55f, 0.8f, 1f);
        }

        public static void Draw()
        {
            Styles();
            if (ChatOpen) DrawChatLine();
            if (!Open) return;
            float w = 560, h = 420;
            GUI.Window(0x4D57, new Rect((Screen.width - w) / 2, (Screen.height - h) / 2, w, h), Window, "MWCoop " + Version.Text);
        }

        static void Window(int id)
        {
            GUILayout.BeginHorizontal();
            for (int i = 0; i < tabs.Length; i++)
                if (GUILayout.Button(tabs[i], i == tab ? active : button)) { tab = i; scroll = Vector2.zero; }
            GUILayout.EndHorizontal();
            GUILayout.Space(6);
            scroll = GUILayout.BeginScrollView(scroll);
            if (tab == 0) Players();
            else if (tab == 1) Skins();
            else ChatTab();
            GUILayout.EndScrollView();
            GUILayout.Label("F10 ou Echap : fermer.   T : tchat.", small);
        }

        static void Players()
        {
            GUILayout.Label(Session.Active ? Session.Status : "Solo (lancez le jeu depuis MWCoop.exe pour jouer a plusieurs)", small);
            foreach (PlayerInfo pi in Session.Players.Values)
            {
                GUILayout.BeginHorizontal();
                string where = pi.Level == 1 ? "en partie" : "au menu";
                string ping = !pi.Local && pi.Peer != null ? ", " + Mathf.RoundToInt(pi.Peer.Rtt * 1000) + " ms" : "";
                GUILayout.Label((pi.Id == 0 ? "[hote] " : "") + pi.Name + (pi.Local ? " (vous)" : "") + "  -  " + where + ping, title);
                if (!pi.Local && pi.Level == 1 && PlayerSync.InGame && GUILayout.Button("Aller vers", button, GUILayout.Width(110)))
                    GoTo(pi);
                GUILayout.EndHorizontal();
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

        static void Skins()
        {
            if (skins == null || skins.Count == 0) skins = PlayerSync.InGame ? Avatar.SkinNames() : new List<string>();
            if (skins.Count == 0) { GUILayout.Label("Les apparences se choisissent en partie.", small); return; }
            GUILayout.Label("Votre apparence vue par les autres joueurs : " + SkinLabel(Session.Me.Skin), small);
            int cols = 3;
            for (int i = 0; i < skins.Count; i += cols)
            {
                GUILayout.BeginHorizontal();
                for (int j = i; j < i + cols && j < skins.Count; j++)
                    if (GUILayout.Button(SkinLabel(skins[j]), skins[j] == Session.Me.Skin ? active : button)) SetSkin(skins[j]);
                GUILayout.EndHorizontal();
            }
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

        static void ChatTab()
        {
            foreach (string l in Chat.History) GUILayout.Label(l, small);
            GUILayout.BeginHorizontal();
            GUI.SetNextControlName("mwcoop-chat2");
            chatText = GUILayout.TextField(chatText, 200);
            if (GUILayout.Button("Envoyer", button, GUILayout.Width(90)) || EnterPressed()) { Chat.Send(chatText); chatText = ""; }
            GUILayout.EndHorizontal();
        }

        static void DrawChatLine()
        {
            var r = new Rect(20, Screen.height - 60, Mathf.Min(600, Screen.width - 40), 26);
            GUI.SetNextControlName("mwcoop-chat");
            chatText = GUI.TextField(r, chatText, 200);
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
    }
}
