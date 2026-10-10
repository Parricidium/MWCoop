using System.Collections.Generic;
using MWCoop.Net;
using UnityEngine;

namespace MWCoop
{
    // Panneau des triches (demande de JD, 10/10) : touche Keys.Cheats (F7 par defaut, reglable dans le lanceur). Les memes
    // commandes que le tchat (Cheats.Command), en boutons : aller a un joueur ou le faire venir, lieux, soin, argent,
    // liberation, heure et neige pour tous. Echap ou la touche : ferme. Souris et deplacements coupes (Menu.Block).
    public static class CheatPanel
    {
        public static bool Open;
        static float openedAt;
        static string note; static float noteUntil; static bool noteBad;

        public static void Update()
        {
            if (!PlayerSync.InGame) { Open = false; return; }
            if (Menu.Open || Menu.ChatOpen) return;
            if (Open && Input.GetKeyDown(KeyCode.Escape)) { Open = false; return; }
            if (Input.GetKeyDown(Keys.Cheats)) { Open = !Open; if (Open) { openedAt = Time.realtimeSinceStartup; note = null; WalletPanel.Open = false; MapPanel.Open = false; } }
        }

        static bool Button(Rect r, string label, bool accent, Event e, bool enabled = true)
        {
            bool hover = enabled && r.Contains(e.mousePosition);
            Color bg = !enabled ? new Color(1f, 1f, 1f, 0.04f) : accent ? (hover ? Style.Accent : Style.AccentDeep) : new Color(1f, 1f, 1f, hover ? 0.14f : 0.08f);
            Style.Round(r, r.height / 2, bg);
            Style.Text(r, label, 14, TextAnchor.MiddleCenter, !enabled ? Style.Dim : accent ? Style.OnAccent : Style.White);
            if (enabled && e.type == EventType.MouseDown && e.button == 0 && hover) { e.Use(); return true; }
            return false;
        }

        static void Do(string cmd, string done)
        {
            string err = Cheats.Command(cmd);
            note = err ?? done; noteBad = err != null; noteUntil = Time.realtimeSinceStartup + 4f;
            if (err == null && (cmd.StartsWith("/tp") || cmd.StartsWith("/lieu"))) Open = false;   // (teleporte : on regarde ou on est)
        }

        static void Header(float x, ref float y, float w, string s)
        {
            Style.Text(new Rect(x, y, w, Style.Px(24)), s, 13, TextAnchor.MiddleLeft, Style.Dim);
            y += Style.Px(28);
        }

        public static void Draw()
        {
            if (!Open) return;
            Event e = Event.current;
            float k = Mathf.Clamp01((Time.realtimeSinceStartup - openedAt) / 0.14f);
            Style.Alpha = 1f - (1f - k) * (1f - k);
            var players = new List<PlayerInfo>();
            foreach (PlayerInfo pi in Session.Players.Values) if (!pi.Local) players.Add(pi);
            bool ok = Cheats.Allowed;
            float pad = Style.Px(22), bh = Style.Px(34), gap = Style.Px(8), rowH = Style.Px(42);
            float w = Mathf.Min(Style.Px(680), Screen.width - Style.Px(32));
            int perRow = 4, placeRows = (Cheats.Places.Length + perRow - 1) / perRow;
            float h = Style.Px(120) + (Session.Active ? Style.Px(30) + Mathf.Max(1, players.Count) * rowH : 0f) + Style.Px(30) + placeRows * (bh + gap) + Style.Px(30) + bh + gap + Style.Px(30) + 2 * (bh + gap) + Style.Px(60);
            h = Mathf.Min(h, Screen.height - Style.Px(24));
            float x = Mathf.Round((Screen.width - w) / 2), y = Mathf.Round((Screen.height - h) / 2 + (1f - k) * Style.Px(12));
            Style.Glass(new Rect(x, y, w, h), Style.Px(20));
            Style.Title(new Rect(x + pad, y + Style.Px(12), w - 2 * pad, Style.Px(40)), Lang.T("TRICHES", "CHEATS"), Style.White, TextAnchor.MiddleLeft, 26);
            Style.Text(new Rect(x + pad, y + Style.Px(12), w - 2 * pad, Style.Px(40)), Keys.Label(Keys.Cheats) + Lang.T(" / Échap : fermer", " / Esc: close"), 14, TextAnchor.MiddleRight, Style.Dim, false);
            float cy = y + Style.Px(56), iw = w - 2 * pad, ix = x + pad;
            string state = ok ? (Session.Active && !Session.IsHost ? Lang.T("Autorisées par l'hôte. Chaque triche est annoncée à tous.", "Allowed by the host. Every cheat is announced to everyone.")
                                                                    : Lang.T("Autorisées. Chaque triche est annoncée à tous.", "Allowed. Every cheat is announced to everyone."))
                              : (Session.Active && !Session.IsHost ? Lang.T("Coupées par l'hôte.", "Turned off by the host.")
                                                                    : Lang.T("Coupées : lanceur, Options du jeu > Partie et partage > Autoriser les triches.", "Off: launcher, Game options > Game and sharing > Allow cheats."));
            Style.Text(new Rect(ix, cy, iw, Style.Px(26)), state, 15, TextAnchor.MiddleLeft, ok ? Style.Good : Style.Warn, false);
            cy += Style.Px(40);
            // joueurs
            if (Session.Active)
            {
                Header(ix, ref cy, iw, Lang.T("JOUEURS", "PLAYERS"));
                if (players.Count == 0) { Style.Text(new Rect(ix, cy, iw, rowH), Lang.T("Personne d'autre dans la partie", "Nobody else in the game"), 15, TextAnchor.MiddleLeft, Style.Dim, false); cy += rowH; }
                foreach (PlayerInfo pi in players)
                {
                    bool inGame = pi.Level == 1;
                    Style.Text(new Rect(ix, cy, iw - Style.Px(260), rowH), pi.Name + (inGame ? "" : Lang.T("  (au menu)", "  (in menu)")), 17, TextAnchor.MiddleLeft, inGame ? Style.White : Style.Dim);
                    float bw = Style.Px(124);
                    if (Button(new Rect(ix + iw - 2 * bw - gap, cy + Style.Px(4), bw, bh), Lang.T("ALLER", "GO TO"), true, e, ok && inGame)) Do("/tp " + pi.Name, Lang.T("Téléporté vers ", "Teleported to ") + pi.Name);
                    if (Button(new Rect(ix + iw - bw, cy + Style.Px(4), bw, bh), Lang.T("AMENER", "BRING"), false, e, ok && inGame)) Do("/amener " + pi.Name, pi.Name + Lang.T(" arrive", " is coming"));
                    cy += rowH;
                }
            }
            // lieux
            Header(ix, ref cy, iw, Lang.T("LIEUX", "PLACES"));
            float pw = Mathf.Floor((iw - (perRow - 1) * gap) / perRow);
            for (int i = 0; i < Cheats.Places.Length; i++)
            {
                Cheats.Place p = Cheats.Places[i];
                Rect r = new Rect(ix + (i % perRow) * (pw + gap), cy + (i / perRow) * (bh + gap), pw, bh);
                if (Button(r, Lang.Fr ? p.Fr : p.En, false, e, ok)) Do("/lieu " + p.Key, Lang.Fr ? p.Fr : p.En);
            }
            cy += placeRows * (bh + gap) + Style.Px(6);
            // moi
            Header(ix, ref cy, iw, Lang.T("MOI", "ME"));
            float mw = Mathf.Floor((iw - 3 * gap) / 4);
            if (Button(new Rect(ix, cy, mw, bh), Lang.T("SOIGNER", "HEAL"), false, e, ok)) Do("/soin", Lang.T("En pleine forme", "Feeling great"));
            if (Button(new Rect(ix + (mw + gap), cy, mw, bh), "+1 000 mk", false, e, ok)) Do("/argent 1000", "+1 000 mk");
            if (Button(new Rect(ix + 2 * (mw + gap), cy, mw, bh), "+10 000 mk", false, e, ok)) Do("/argent 10000", "+10 000 mk");
            if (Button(new Rect(ix + 3 * (mw + gap), cy, mw, bh), Lang.T("LIBÉRER", "FREE"), false, e, ok)) Do("/liberer", Lang.T("Plus recherché", "No longer wanted"));
            cy += bh + gap + Style.Px(6);
            // monde
            Header(ix, ref cy, iw, Lang.T("MONDE (POUR TOUS)", "WORLD (FOR EVERYONE)"));
            int[] hours = { 6, 9, 12, 15, 18, 22 };
            float hw = Mathf.Floor((iw - Style.Px(90) - (hours.Length - 1) * gap) / hours.Length);
            Style.Text(new Rect(ix, cy, Style.Px(90), bh), Lang.T("Heure", "Time"), 15, TextAnchor.MiddleLeft, Style.White, false);
            for (int i = 0; i < hours.Length; i++)
                if (Button(new Rect(ix + Style.Px(90) + i * (hw + gap), cy, hw, bh), hours[i] + " h", false, e, ok)) Do("/heure " + hours[i], Lang.T("Il est ", "It's ") + hours[i] + " h");
            cy += bh + gap;
            Style.Text(new Rect(ix, cy, Style.Px(90), bh), Lang.T("Neige", "Snow"), 15, TextAnchor.MiddleLeft, Style.White, false);
            float sw = Style.Px(140);
            if (Button(new Rect(ix + Style.Px(90), cy, sw, bh), Lang.T("OUI", "ON"), false, e, ok)) Do("/neige oui", Lang.T("Il neige", "It's snowing"));
            if (Button(new Rect(ix + Style.Px(90) + sw + gap, cy, sw, bh), Lang.T("NON", "OFF"), false, e, ok)) Do("/neige non", Lang.T("Temps sec", "Dry weather"));
            cy += bh + gap;
            // bas : resultat, ou rappel du tchat
            Rect foot = new Rect(ix, y + h - Style.Px(44), iw, Style.Px(30));
            if (note != null && Time.realtimeSinceStartup < noteUntil) Style.Text(foot, note, 15, TextAnchor.MiddleLeft, noteBad ? Style.Warn : Style.Good, false);
            else Style.Text(foot, Lang.T("Aussi dans le tchat (", "Also in the chat (") + Keys.Label(Keys.Chat) + Lang.T(") : /aide", "): /help"), 14, TextAnchor.MiddleLeft, Style.Dim, false);
            Style.Alpha = 1f;
        }
    }
}
