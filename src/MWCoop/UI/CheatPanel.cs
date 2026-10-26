using System.Collections.Generic;
using MWCoop.Net;
using UnityEngine;

namespace MWCoop
{
    // Panneau des triches (demande de JD, 10/10) : touche Keys.Cheats (F7 par defaut, reglable dans le lanceur). Les memes
    // commandes que le tchat (Cheats.Command), en boutons, par onglets : joueurs (aller, amener), lieux, moi (soin, argent,
    // liberation, vetements), voitures (/voiture, degivrer), monde (heure et neige pour tous ; My Summer Car : ses lieux,
    // l'heure par 2 h, la pluie). Echap ou la touche : ferme.
    // Souris et deplacements coupes (Menu.Block).
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

        static void Do(string cmd, string done)
        {
            string err = Cheats.Command(cmd);
            note = err ?? done; noteBad = err != null; noteUntil = Time.realtimeSinceStartup + 4f;
            if (err == null && (cmd.StartsWith("/tp") || cmd.StartsWith("/lieu"))) Open = false;   // (teleporte : on regarde ou on est)
        }

        // Panneau a droite de l'ecran (refonte du 18/10) : le jeu reste visible au centre. Taille des menus (Kit).
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
        // Heures proposees (My Summer Car : son soleil avance par tranches de 2 h, Cheats.MscHour)
        static readonly int[] HoursMwc = { 6, 9, 12, 15, 18, 22 }, HoursMsc = { 6, 8, 12, 16, 20, 22 };

        // Onglets (JD, 25/10 : « ranger les triches par categorie, avec des onglets ») : joueurs (en partie), lieux, moi, voitures,
        // monde. Le panneau a la hauteur de son plus grand onglet : il ne saute pas d'un onglet a l'autre. Le dernier choisi reste.
        const int T_Players = 0, T_Places = 1, T_Me = 2, T_Cars = 3, T_World = 4;
        public static int Tab = -1;   // (essais : Cheats.Test « trichesonglets »)
        // Voitures de /voiture (VehicleSync.CarAliases) : cle de la commande, nom montre.
        static readonly string[][] CarsMwc = { new[] { "sorbet", "Sorbet" }, new[] { "rivett", "Rivett" }, new[] { "taxi", "Taxi" }, new[] { "tracteur", "Kekmet" },
            new[] { "camion", "Gifu" }, new[] { "fourgon", "Bachglotz" }, new[] { "moto", "Jonnez" }, new[] { "remorque", "Flatbed" } };
        static readonly string[][] CarsMsc = { new[] { "satsuma", "Satsuma" }, new[] { "hayosiko", "Hayosiko" }, new[] { "ferndale", "Ferndale" }, new[] { "ruscko", "Ruscko" },
            new[] { "tracteur", "Kekmet" }, new[] { "camion", "Gifu" }, new[] { "moto", "Jonnez" }, new[] { "remorque", "Flatbed" }, new[] { "bateau", "Boat" } };

        // Argent fixe par l'hote (/argentde, suggestion de Matilda Prime, 26/10) : montant tape par joueur (-1 : moi).
        static readonly Dictionary<int, string> moneyText = new Dictionary<int, string>();
        static bool HostHere { get { return !Session.Active || Session.IsHost; } }
        static void MoneyRow(Rect r, int id, string name, bool ok)
        {
            string s;
            if (!moneyText.TryGetValue(id, out s)) s = "";
            float bw = P(78), lw = P(120);
            Style.Text(new Rect(r.x, r.y, lw, r.height), Lang.T("Fixer l'argent", "Set money"), 12, TextAnchor.MiddleLeft, Kit.Muted, false);
            string ns = GUI.TextField(new Rect(r.x + lw, r.y + P(2), r.width - lw - bw - P(6), r.height - P(4)), s, 7, Style.Field(13));
            var digits = new System.Text.StringBuilder();
            foreach (char c in ns) if (c >= '0' && c <= '9') digits.Append(c);
            moneyText[id] = digits.ToString();
            int n;
            bool valid = int.TryParse(moneyText[id], out n) && n <= 1000000;
            if (Kit.Button(new Rect(r.xMax - bw, r.y, bw, r.height), Lang.T("Fixer", "Set"), 0, false, ok && valid, null, 12))
                Do("/argentde #" + (id < 0 ? Session.LocalId : id) + " " + n, name + Lang.T(" : ", ": ") + n + " mk");
        }

        static string TabName(int t)
        {
            switch (t)
            {
                case T_Players: return Lang.T("Joueurs", "Players");
                case T_Places: return Lang.T("Lieux", "Places");
                case T_Me: return Lang.T("Moi", "Me");
                case T_Cars: return Lang.T("Voitures", "Cars");
                default: return Lang.T("Monde", "World");
            }
        }

        static void DrawPanel(Event e, float k)
        {
            var players = new List<PlayerInfo>();
            foreach (PlayerInfo pi in Session.Players.Values) if (!pi.Local) players.Add(pi);
            bool ok = Cheats.Allowed;
            float pad = P(16), bh = P(30), gap = P(6), rowH = P(36), tabH = P(30);
            float w = Mathf.Min(P(400), Screen.width - P(32)), iw = w - 2 * pad;
            int perRow = 3, placeRows = (Cheats.Places.Length + perRow - 1) / perRow;
            string[][] carList = Game.Msc ? CarsMsc : CarsMwc;
            int carRows = (carList.Length + perRow - 1) / perRow;
            var tabs = new List<int>();
            if (Session.Active) tabs.Add(T_Players);
            tabs.Add(T_Places); tabs.Add(T_Me); tabs.Add(T_Cars); tabs.Add(T_World);
            if (!tabs.Contains(Tab)) Tab = Session.Active ? T_Players : T_Places;
            string state = ok ? (Session.Active && !Session.IsHost ? Lang.T("Autoris\u00E9es par l'h\u00F4te \u00B7 chaque triche est annonc\u00E9e \u00E0 tous", "Allowed by the host \u00B7 every cheat is announced to everyone")
                                                                    : Cheats.HostOnly && Session.Active ? Lang.T("Autoris\u00E9es pour toi seul (h\u00F4te seulement) \u00B7 annonc\u00E9es \u00E0 tous", "Allowed for you only (host only) \u00B7 announced to everyone")
                                                                    : Lang.T("Autoris\u00E9es \u00B7 chaque triche est annonc\u00E9e \u00E0 tous", "Allowed \u00B7 every cheat is announced to everyone"))
                              : (Session.Active && !Session.IsHost ? (Cheats.HostOnly ? Lang.T("R\u00E9serv\u00E9es \u00E0 l'h\u00F4te.", "For the host only.") : Lang.T("Coup\u00E9es par l'h\u00F4te.", "Turned off by the host."))
                                                                    : Lang.T("Coup\u00E9es : lanceur, Options du jeu > Partie et partage > Autoriser les triches.", "Off: launcher, Game options > Game and sharing > Allow cheats."));
            float stH = Style.Height(state, 12, iw, false) + P(4);
            // hauteur du contenu : le plus grand des onglets
            float body = 0f;
            foreach (int t in tabs)
            {
                float bt = t == T_Players ? Mathf.Max(1, players.Count) * (rowH + (HostHere ? bh + gap : 0f))
                         : t == T_Places ? placeRows * (bh + gap)
                         : t == T_Me ? (HostHere ? 4 : 3) * (bh + gap)
                         : t == T_Cars ? carRows * (bh + gap) + (Game.Msc ? 0f : P(10) + bh + gap)
                         : 2 * (bh + gap);
                body = Mathf.Max(body, bt);
            }
            float h = pad + P(32) + stH + P(6) + tabH + P(12) + body + P(28) + pad;
            h = Mathf.Min(h, Screen.height - P(24));
            float x = Screen.width - w - P(24), y = Mathf.Max(P(12), Mathf.Round((Screen.height - h) / 2)) + (1f - k) * P(10);   // (a droite, centre en hauteur : sous le panneau Equipe du HUD)
            Kit.Frame(new Rect(x, y, w, h));
            float ix = x + pad, cy = y + pad;
            if (Kit.Header(new Rect(ix, cy, iw, P(28)), Lang.T("Triches", "Cheats"), Keys.Label(Keys.Cheats))) { Open = false; return; }
            cy += P(32);
            Style.Text(new Rect(ix, cy, iw, stH), state, 12, TextAnchor.UpperLeft, ok ? Kit.Muted : Kit.Amber, false, true);
            cy += stH + P(6);
            // onglets
            float tw = Mathf.Floor((iw - (tabs.Count - 1) * P(4)) / tabs.Count);
            for (int i = 0; i < tabs.Count; i++)
                if (Kit.Button(new Rect(ix + i * (tw + P(4)), cy, tw, tabH), TabName(tabs[i]), tabs[i] == Tab ? 1 : 0, false, true, null, 12)) Tab = tabs[i];
            cy += tabH + P(12);
            float hw2 = Mathf.Floor((iw - gap) / 2), pw = Mathf.Floor((iw - (perRow - 1) * gap) / perRow);
            switch (Tab)
            {
                case T_Players:
                    if (players.Count == 0) Style.Text(new Rect(ix, cy, iw, rowH), Lang.T("Personne d'autre dans la partie", "Nobody else in the game"), 13, TextAnchor.MiddleLeft, Kit.Muted, false);
                    foreach (PlayerInfo pi in players)
                    {
                        bool inGame = pi.Level == 1;
                        float av = P(24);
                        Kit.Avatar(new Rect(ix, cy + (rowH - av) / 2, av, av), pi.Id, pi.Name, pi.SteamId, inGame ? 1f : 0.55f);
                        float bw = P(78);
                        Style.Text(new Rect(ix + av + P(8), cy, iw - av - P(8) - 2 * bw - P(12), rowH), pi.Name + (inGame ? "" : Lang.T("  (au menu)", "  (in menu)")), 14, TextAnchor.MiddleLeft, inGame ? Kit.Ink : Kit.Faint);
                        if (Kit.Button(new Rect(ix + iw - 2 * bw - gap, cy + (rowH - P(28)) / 2, bw, P(28)), Lang.T("Aller", "Go to"), 1, false, ok && inGame, null, 12)) Do("/tp " + pi.Name, Lang.T("T\u00E9l\u00E9port\u00E9 vers ", "Teleported to ") + pi.Name);
                        if (Kit.Button(new Rect(ix + iw - bw, cy + (rowH - P(28)) / 2, bw, P(28)), Lang.T("Amener", "Bring"), 0, false, ok && inGame, null, 12)) Do("/amener " + pi.Name, pi.Name + Lang.T(" arrive", " is coming"));
                        cy += rowH;
                        if (HostHere) { MoneyRow(new Rect(ix + av + P(8), cy, iw - av - P(8), bh), pi.Id, pi.Name, ok && inGame); cy += bh + gap; }
                    }
                    break;
                case T_Places:
                    for (int i = 0; i < Cheats.Places.Length; i++)
                    {
                        Cheats.Place p = Cheats.Places[i];
                        Rect r = new Rect(ix + (i % perRow) * (pw + gap), cy + (i / perRow) * (bh + gap), pw, bh);
                        string lab = Lang.T(p.Fr, p.En);
                        // (noms longs -- CARDINAL VARAOSAT, Musee de la Corse, OSUUSKAUPPA s21 -- : nom court, sinon ils debordaient du bouton)
                        string shortLab = p.Key == "pieces" ? "CARDINAL" : p.Key == "musee" ? Lang.T("Musée", "Museum") : p.Key == "boutique" ? "s21" : lab;
                        if (Kit.Button(r, shortLab, 0, false, ok, null, 11)) Do("/lieu " + p.Key, lab);
                    }
                    break;
                case T_Me:
                    if (Kit.Button(new Rect(ix, cy, hw2, bh), Lang.T("Soigner", "Heal"), 0, false, ok, null, 12)) Do("/soin", Lang.T("En pleine forme", "Feeling great"));
                    if (Kit.Button(new Rect(ix + hw2 + gap, cy, hw2, bh), Lang.T("Lib\u00E9rer (police)", "Free (police)"), 0, false, ok, null, 12)) Do("/liberer", Lang.T("Plus recherch\u00E9", "No longer wanted"));
                    cy += bh + gap;
                    if (Kit.Button(new Rect(ix, cy, hw2, bh), "+1 000 mk", 0, false, ok, null, 12)) Do("/argent 1000", "+1 000 mk");
                    if (Kit.Button(new Rect(ix + hw2 + gap, cy, hw2, bh), "+10 000 mk", 0, false, ok, null, 12)) Do("/argent 10000", "+10 000 mk");
                    cy += bh + gap;
                    if (Kit.Button(new Rect(ix, cy, iw, bh), Lang.T("Ramener mes v\u00EAtements (veste, combinaison, casque)", "Bring back my clothes (jacket, coverall, helmet)"), 0, false, ok, null, 12)) Do("/vetements", Lang.T("V\u00EAtements ramen\u00E9s", "Clothes brought back"));
                    cy += bh + gap;
                    if (HostHere) MoneyRow(new Rect(ix, cy, iw, bh), -1, Session.Me != null ? Session.Me.Name : "moi", ok);
                    break;
                case T_Cars:
                    for (int i = 0; i < carList.Length; i++)
                    {
                        Rect r = new Rect(ix + (i % perRow) * (pw + gap), cy + (i / perRow) * (bh + gap), pw, bh);
                        if (Kit.Button(r, carList[i][1], 0, false, ok, null, 12)) Do("/voiture " + carList[i][0], carList[i][1] + Lang.T(" devant toi", " in front of you"));
                    }
                    cy += carRows * (bh + gap);
                    // (vitres de toutes les voitures, pour tous -- Kamixak, 25/10 ; pas de givre sur My Summer Car)
                    if (!Game.Msc)
                    {
                        cy += P(10);
                        if (Kit.Button(new Rect(ix, cy, iw, bh), Lang.T("D\u00E9givrer les vitres (toutes les voitures)", "Defrost the windows (all cars)"), 0, false, ok, null, 12)) Do("/degivrer", Lang.T("Vitres d\u00E9givr\u00E9es", "Windows defrosted"));
                    }
                    break;
                default:
                {
                    int[] hours = Game.Msc ? HoursMsc : HoursMwc;
                    float lw = P(64), hw = Mathf.Floor((iw - lw - (hours.Length - 1) * gap) / hours.Length);
                    Style.Text(new Rect(ix, cy, lw, bh), Lang.T("Heure", "Time"), 13, TextAnchor.MiddleLeft, Kit.Soft);
                    for (int i = 0; i < hours.Length; i++)
                        if (Kit.Button(new Rect(ix + lw + i * (hw + gap), cy, hw, bh), hours[i] + " h", 0, false, ok, null, 12)) Do("/heure " + hours[i], Lang.T("Il est ", "It's ") + hours[i] + " h");
                    cy += bh + gap;
                    // (My Summer Car : la pluie, pas de neige l'ete -- Cheats.SetRainMsc)
                    Style.Text(new Rect(ix, cy, lw, bh), Game.Msc ? Lang.T("Pluie", "Rain") : Lang.T("Neige", "Snow"), 13, TextAnchor.MiddleLeft, Kit.Soft);
                    float sw = Mathf.Floor((iw - lw - gap) / 2);
                    string wx = Game.Msc ? "/pluie" : "/neige";
                    if (Kit.Button(new Rect(ix + lw, cy, sw, bh), Lang.T("Oui", "On"), 0, false, ok, null, 12)) Do(wx + " oui", Game.Msc ? Lang.T("Il pleut", "It's raining") : Lang.T("Il neige", "It's snowing"));
                    if (Kit.Button(new Rect(ix + lw + sw + gap, cy, sw, bh), Lang.T("Non", "Off"), 0, false, ok, null, 12)) Do(wx + " non", Lang.T("Temps sec", "Dry weather"));
                    break;
                }
            }
            // bas : resultat, ou rappel du tchat
            Rect foot = new Rect(ix, y + h - pad - P(22), iw, P(22));
            if (note != null && Time.realtimeSinceStartup < noteUntil) Style.Text(foot, note, 12, TextAnchor.MiddleLeft, noteBad ? Kit.Amber : Kit.Green, false);
            else Style.Text(foot, Lang.T("Aussi dans le tchat (", "Also in the chat (") + Keys.Label(Keys.Chat) + Lang.T(") : /aide", "): /help"), 12, TextAnchor.MiddleLeft, Kit.Faint, false);
        }
    }
}
