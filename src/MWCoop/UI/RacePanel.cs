using System.Collections.Generic;
using UnityEngine;

namespace MWCoop
{
    // Course sur glace (demande de JD, 18/10 : « comment je sais si je suis inscrit, a quel moment prendre la VAZ ? », plus de
    // Jokkis de location, un tableau des chronos en direct) :
    //  - onglet Course sur glace du F10 (a la place de la liste des Jokkis) : mon inscription (VAZ et son numero, Rivett,
    //    pas inscrit), comment et quand venir, les pilotes humains (inscrits, numero, meilleur tour, derniere manche) et les
    //    chronos ;
    //  - chronos en direct (Draw) : au circuit pendant les essais du vendredi (meilleurs tours) et les manches (positions,
    //    tours, dernier et meilleur tour), en haut a droite. [Hud] ChronoGlace=0 : jamais.
    public static class RacePanel
    {
        static float P(float v) { return Style.Px(v); }

        public static string Sub
        {
            get { return Lang.T("Inscription avec ta VAZ · week-end de course · chronos en direct", "Sign up with your VAZ · race weekend · live timing"); }
        }

        public static void DrawTab(Rect v)
        {
            float y = v.y, w = v.width;
            // mon inscription
            Kit.Section(new Rect(v.x, y, w, P(20)), Lang.T("TON INSCRIPTION", "YOUR ENTRY")); y += P(24);
            string st; Color sc;
            if (!PlayerSync.InGame || !IceRace.Ready) { st = Lang.T("Course sur glace pas encore prête (en jeu, au bout de quelques secondes)", "Ice race not ready yet (in game, after a few seconds)"); sc = Kit.Muted; }
            else if (IceRace.MeWithVaz) { st = Lang.T("Inscrit avec ta VAZ · n° ", "Registered with your VAZ · no. ") + IceRace.MyNumber + Lang.T(" (elle remplace la voiture IA de ce numéro)", " (it takes that AI car's place)"); sc = Kit.Green; }
            else if (IceRace.VazPending) { st = Lang.T("Inscrit avec ta VAZ · numéro en attente de l'hôte", "Registered with your VAZ · waiting for the host's number"); sc = Kit.Amber; }
            else if (IceRace.MeRegistered) { st = Lang.T("Inscrit avec la Rivett · n° 14", "Registered with the Rivett · no. 14"); sc = Kit.Green; }
            else { st = Lang.T("Pas inscrit", "Not registered"); sc = Kit.Red; }
            Style.Round(new Rect(v.x, y, w, P(34)), P(10), Kit.A(sc, 0.10f));
            Style.Round(new Rect(v.x + P(12), y + P(13), P(8), P(8)), P(4), sc);
            Style.Text(new Rect(v.x + P(28), y, w - P(36), P(34)), st, 14, TextAnchor.MiddleLeft, Kit.Ink);
            y += P(42);
            // comment et quand
            string how = Lang.T(
                "1. Prends ta VAZ (onglet VAZ-2108) et va au circuit de la course sur glace.\n" +
                "2. Vendredi 9 h - 15 h : gare ta VAZ juste à côté de la tente d'inspection (moins de 6 m), puis remplis la feuille d'inscription (150 mk). Ensuite, roule aux essais : ton meilleur tour te place en qualifications.\n" +
                "3. Samedi 9 h - 15 h : qualifications et repêchages. Dimanche 10 h - 15 h : demi-finales et finale. Sois au circuit avec ta VAZ : au départ de ta manche, elle est posée sur ta place de la grille.",
                "1. Take your VAZ (VAZ-2108 tab) and drive to the ice race track.\n" +
                "2. Friday 9 am - 3 pm: park your VAZ right next to the inspection tent (under 6 m), then fill in the registration form (150 mk). Then drive in practice: your best lap sets your qualifying place.\n" +
                "3. Saturday 9 am - 3 pm: qualifying and pickups. Sunday 10 am - 3 pm: semifinals and final. Be at the track with your VAZ: when your heat starts, it is put on your grid spot.");
            float hh = P(96);
            Style.Text(new Rect(v.x, y, w, hh), how, 12, TextAnchor.UpperLeft, Kit.Soft, false, true);
            y += hh + P(6);
            // pilotes
            List<IceRace.Entrant> ents = IceRace.Entrants();
            Kit.Section(new Rect(v.x, y, w, P(20)), Lang.T("PILOTES", "DRIVERS")); y += P(24);
            if (ents.Count == 0) { Style.Text(new Rect(v.x, y, w, P(22)), Lang.T("Personne pour l'instant", "Nobody yet"), 12, TextAnchor.MiddleLeft, Kit.Muted, false); y += P(26); }
            int shown = 0;
            foreach (IceRace.Entrant en in ents)
            {
                if (shown++ >= 6) break;
                var r = new Rect(v.x, y, w, P(24));
                if (en.Me) Style.Round(r, P(8), Kit.A(Kit.Ice, 0.08f));
                Style.Text(new Rect(r.x + P(8), r.y, w * 0.34f, r.height), en.Name, 13, TextAnchor.MiddleLeft, en.Me ? Kit.Ice : Kit.Ink);
                string what = !en.Registered ? Lang.T("pas inscrit", "not registered") : (en.Vaz ? "VAZ" : en.Number == 14 ? "Rivett" : "") + (en.Number > 0 ? " n° " + en.Number : "");
                Style.Text(new Rect(r.x + w * 0.36f, r.y, w * 0.28f, r.height), what, 12, TextAnchor.MiddleLeft, en.Registered ? Kit.Green : Kit.Muted, false);
                string res = (en.Lap > 0f ? Lang.T("tour ", "lap ") + Lap(en.Lap) : "") + (en.Pos > 0 ? (en.Lap > 0f ? " · " : "") + en.Heat + " " + en.Pos + Lang.T("e", "") : "");
                Style.Text(new Rect(r.x + w * 0.64f, r.y, w * 0.36f - P(8), r.height), res, 12, TextAnchor.MiddleRight, Kit.Soft, false);
                y += P(26);
            }
            // chronos
            string title;
            List<IceRace.LiveRow> rows = IceRace.LiveRows(out title);
            Kit.Section(new Rect(v.x, y + P(4), w, P(20)), Lang.T("CHRONOS", "TIMING")); y += P(28);
            if (rows.Count == 0 || title == null) { Style.Text(new Rect(v.x, y, w, P(22)), Lang.T("Rien en piste pour l'instant (essais le vendredi, manches le week-end)", "Nothing on track yet (practice on Friday, heats at the weekend)"), 12, TextAnchor.MiddleLeft, Kit.Muted, false); return; }
            Style.Text(new Rect(v.x, y, w, P(20)), title, 12, TextAnchor.MiddleLeft, Kit.Ice); y += P(22);
            Table(new Rect(v.x, y, w, v.yMax - y), rows, 12);
        }

        static string Lap(float s) { return ((int)(s / 60f)) + ":" + (s % 60f).ToString("00.0", System.Globalization.CultureInfo.InvariantCulture); }

        // Lignes du tableau des chronos : place, numero, pilote, tours (ou etat), dernier tour, meilleur tour.
        static void Table(Rect r, List<IceRace.LiveRow> rows, int size)
        {
            float rh = P(20), y = r.y;
            float[] cx = { 0f, 0.07f, 0.17f, 0.56f, 0.71f, 0.86f };
            string[] head = { "#", Lang.T("N°", "No."), Lang.T("Pilote", "Driver"), Lang.T("Tours", "Laps"), Lang.T("Dernier", "Last"), Lang.T("Meilleur", "Best") };
            for (int i = 0; i < head.Length; i++) Style.Text(new Rect(r.x + r.width * cx[i] + P(4), y, r.width * ((i + 1 < cx.Length ? cx[i + 1] : 1f) - cx[i]), rh), head[i], size - 1, TextAnchor.MiddleLeft, Kit.Faint);
            y += rh;
            int cap = Mathf.Max(1, Mathf.FloorToInt((r.yMax - y) / rh)), me = rows.FindIndex(x => x.Me);
            if (me >= cap && rows.Count > cap) { var cut = rows.GetRange(0, cap - 1); cut.Add(rows[me]); rows = cut; }
            foreach (IceRace.LiveRow row in rows)
            {
                if (y + rh > r.yMax + 0.5f) break;
                var rr = new Rect(r.x, y, r.width, rh);
                if (row.Me) Style.Round(rr, P(6), Kit.A(Kit.Ice, 0.12f));
                Color c = row.Me ? Kit.Ice : row.Human ? Kit.Ink : Kit.Soft;
                string[] cells = { row.Pos.ToString(), row.Num, row.Name, row.Laps, row.Last, row.Best };
                for (int i = 0; i < cells.Length; i++)
                    Style.Text(new Rect(r.x + r.width * cx[i] + P(4), y, r.width * ((i + 1 < cx.Length ? cx[i + 1] : 1f) - cx[i]) - P(4), rh), cells[i] ?? "", size, TextAnchor.MiddleLeft, i == 5 && row.Done ? Kit.Green : c, row.Human || i == 0);
                y += rh;
            }
        }

        // Chronos en direct, en haut a droite, au circuit.
        static float shownAt = -1f;
        public static void Draw()
        {
            if (Menu.Open || Config.GetInt("Hud", "ChronoGlace", 1) == 0 || !IceRace.LiveOn) { shownAt = -1f; return; }
            string title;
            List<IceRace.LiveRow> rows = IceRace.LiveRows(out title);
            if (rows.Count == 0 || title == null) return;
            if (shownAt < 0f) shownAt = Time.realtimeSinceStartup;
            float k = Mathf.Clamp01((Time.realtimeSinceStartup - shownAt) / 0.2f);
            Kit.Begin(k);
            try
            {
                int n = Mathf.Min(rows.Count, 12);
                float pad = P(12), w = Mathf.Min(P(380), Screen.width * 0.42f), h = pad * 2 + P(24) + P(20) * (n + 1);
                var fr = new Rect(Screen.width - w - P(16), Mathf.Round(Screen.height * 0.16f), w, h);
                Kit.Frame(fr);
                Style.Text(new Rect(fr.x + pad, fr.y + pad, w - 2 * pad, P(20)), title, 12, TextAnchor.MiddleLeft, Kit.Ice);
                Table(new Rect(fr.x + pad, fr.y + pad + P(24), w - 2 * pad, P(20) * (n + 1)), rows, 11);
            }
            finally { Kit.End(); }
        }
    }
}
