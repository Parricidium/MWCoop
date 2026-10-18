using System.Collections.Generic;
using MWCoop.Net;
using UnityEngine;

namespace MWCoop
{
    // Porte-monnaie (demande de JD, 10/10) : touche Keys.Wallet (N par defaut, reglable dans le lanceur). Liquide du joueur,
    // montant choisi (boutons -1000 ... +1000, molette), un bouton DONNER par joueur en partie. Echap ou la touche : ferme.
    // Pendant qu'il est ouvert, le jeu ne recoit plus la souris ni les deplacements (Menu.Block).
    public static class WalletPanel
    {
        public static bool Open;
        static int amount = 100;
        static string note; static float noteUntil; static bool noteBad;
        static float openedAt;
        static readonly int[] Steps = { -1000, -100, -10, 10, 100, 1000 };

        public static void Update()
        {
            if (!PlayerSync.InGame || !Session.Active) { Open = false; return; }
            if (Menu.Open || Menu.ChatOpen) return;
            if (Open && Input.GetKeyDown(KeyCode.Escape)) { Open = false; return; }
            if (Input.GetKeyDown(Keys.Wallet)) { Open = !Open; if (Open) { openedAt = Time.realtimeSinceStartup; note = null; CheatPanel.Open = false; MapPanel.Open = false; } }
        }

        static void Note(string s, bool bad) { note = s; noteBad = bad; noteUntil = Time.realtimeSinceStartup + 4f; }

        // Petit panneau au centre, sous le viseur (refonte du 18/10). Taille des menus (Kit).
        public static void Draw()
        {
            if (!Open) return;
            Event e = Event.current;
            float k = Mathf.Clamp01((Time.realtimeSinceStartup - openedAt) / 0.14f);
            Kit.Begin(1f - (1f - k) * (1f - k));
            try { DrawPanel(e, k); }
            finally { Kit.End(); }
        }

        static float P(float v) { return Style.Px(v); }

        static void DrawPanel(Event e, float k)
        {
            var players = new List<PlayerInfo>();
            foreach (PlayerInfo pi in Session.Players.Values) if (!pi.Local) players.Add(pi);
            float pad = P(16), rowH = P(42), sec = P(26), gap = P(5);
            float w = Mathf.Min(P(380), Screen.width - P(32)), iw = w - 2 * pad;
            float h = pad + P(32) + P(8) + P(32) + P(10) + sec + Mathf.Max(1, players.Count) * rowH + P(26) + pad;
            float x = Mathf.Round((Screen.width - w) / 2);
            float y = Mathf.Round(Mathf.Min(Screen.height * 0.56f, Screen.height - h - P(16)) + (1f - k) * P(10));
            Kit.Frame(new Rect(x, y, w, h));
            float ix = x + pad, cy = y + pad;
            var hr = new Rect(ix, cy, iw, P(28));
            if (Kit.Header(hr, Lang.T("Porte-monnaie", "Wallet"), Keys.Label(Keys.Wallet))) { Open = false; return; }
            string cash = Mathf.FloorToInt(Wallet.Cash) + " mk";
            float cwid = Style.Width(cash, 11) + P(14);
            Kit.Pill(hr.xMax - P(26) - P(10) - cwid, hr.center.y, cash, Kit.Green);
            cy += P(40);
            // montant : -1000 -100 -10 [montant] +10 +100 +1000 ; molette dessus : +/- 10
            float aw = P(84), bw = Mathf.Floor((iw - aw - 6 * gap) / 6), bx = ix, bh = P(30);
            for (int i = 0; i < Steps.Length; i++)
            {
                if (i == 3)
                {
                    var ar = new Rect(bx, cy, aw, bh);
                    if (Event.current.type == EventType.Repaint) { Style.Round(ar, P(9), Kit.A(Kit.Ice, 0.12f)); Style.Ring(ar, P(9), Kit.A(Kit.Ice, 0.45f)); }
                    Style.Text(ar, amount + " mk", 14, TextAnchor.MiddleCenter, Kit.Ink);
                    bx += aw + gap;
                }
                if (Kit.Button(new Rect(bx, cy, bw, bh), (Steps[i] > 0 ? "+" : "") + Steps[i], 0, false, true, null, 11)) amount = Mathf.Clamp(amount + Steps[i], 1, 1000000);
                bx += bw + gap;
            }
            var amountRect = new Rect(ix, cy, iw, bh);
            if (e.type == EventType.ScrollWheel && amountRect.Contains(e.mousePosition)) { amount = Mathf.Clamp(amount + (e.delta.y < 0 ? 10 : -10), 1, 1000000); e.Use(); }
            cy += bh + P(12);
            // joueurs
            Kit.Section(new Rect(ix, cy, iw, sec - P(6)), Lang.T("DONNER \u00C0", "GIVE TO"));
            cy += sec;
            if (players.Count == 0) Style.Text(new Rect(ix, cy, iw, rowH), Lang.T("Personne d'autre dans la partie", "Nobody else in the game"), 13, TextAnchor.MiddleLeft, Kit.Muted, false);
            foreach (PlayerInfo pi in players)
            {
                bool inGame = pi.Level == 1;
                float av = P(26), gw = P(136);
                Kit.Avatar(new Rect(ix, cy + (rowH - av) / 2, av, av), pi.Id, pi.Name, pi.SteamId, inGame ? 1f : 0.55f);
                Style.Text(new Rect(ix + av + P(8), cy, iw - av - P(8) - gw - P(8), rowH), pi.Name + (inGame ? "" : Lang.T("  (au menu)", "  (in menu)")), 14, TextAnchor.MiddleLeft, inGame ? Kit.Ink : Kit.Faint);
                if (Kit.Button(new Rect(ix + iw - gw, cy + (rowH - P(30)) / 2, gw, P(30)), Lang.T("Donner ", "Give ") + amount + " mk", 1, false, inGame && Wallet.Ready && Wallet.Cash >= amount, null, 12))
                {
                    string err = Wallet.Give(pi.Id, amount);
                    if (err == null) Note(amount + Lang.T(" mk donn\u00E9s \u00E0 ", " mk given to ") + pi.Name, false);
                    else Note(err, true);
                }
                cy += rowH;
            }
            if (note != null && Time.realtimeSinceStartup < noteUntil)
                Style.Text(new Rect(ix, y + h - pad - P(22), iw, P(22)), note, 12, TextAnchor.MiddleLeft, noteBad ? Kit.Amber : Kit.Green, false);
        }

        // [Test] Autotest=don : a 30 s chacun note son liquide ; a 32 s l'hote donne 250 mk au premier invite ; a 36 s chacun
        // note de nouveau ; a 38 s l'hote ouvre le panneau (capture ecran-porte-monnaie a 39 s).
        static int testStep;
        public static void Test(string mode, float t)
        {
            if (mode != "don") return;
            if (t > 30f && testStep == 0) { testStep = 1; Log.Info("autotest : don, liquide avant " + Wallet.Cash); }
            if (t > 32f && testStep == 1) { testStep = 2; if (Session.IsHost) Log.Info("autotest : don, " + TestGive(250)); }
            if (t > 36f && testStep == 2) { testStep = 3; Log.Info("autotest : don, liquide apres " + Wallet.Cash); }
            if (t > 38f && testStep == 3) { testStep = 4; if (Session.IsHost) { Open = true; openedAt = 0; Autotest.CaptureSoon("porte-monnaie", 1f); } }
        }

        // Essais : don direct (sans l'interface).
        public static string TestGive(int amount)
        {
            foreach (PlayerInfo pi in Session.Players.Values) if (!pi.Local) { string err = Wallet.Give(pi.Id, amount); return err ?? "donne " + amount + " a " + pi.Name; }
            return "personne";
        }
    }
}
