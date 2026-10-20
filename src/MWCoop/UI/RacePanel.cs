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
    //    tours, dernier et meilleur tour), en haut a droite, 5 s apres chaque passage de ma voiture sur la ligne d'arrivee
    //    (JD, 18/10). [Hud] ChronoGlace=0 : jamais.
    // Semaines des courses (RACES 'SwapDates' : GlobalWeeksPassed impair = course sur glace, pair = rallye ; GlobalDay 1 = lundi) :
    // glace du vendredi 9 h au dimanche 15 h, rallye du samedi 8 h (inscription) au dimanche 15 h.
    public static class RaceCal
    {
        static int G(string n) { HutongGames.PlayMaker.FsmInt v = HutongGames.PlayMaker.FsmVariables.GlobalVariables.FindFsmInt(n); return v != null ? v.Value : -1; }
        static string Day(int d) { return Lang.A(new[] { "lundi", "mardi", "mercredi", "jeudi", "vendredi", "samedi", "dimanche" }, new[] { "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday" })[Mathf.Clamp(d - 1, 0, 6)]; }

        public static bool Live(bool ice, out string next)
        {
            next = null;
            int wk = G("GlobalWeeksPassed"), day = G("GlobalDay"), hr = G("GlobalHour");
            if (wk < 0 || day < 1) return true;   // (inconnu : comme avant)
            int startDay = ice ? 5 : 6, startHour = ice ? 9 : 8;
            bool week = (wk % 2 == 1) == ice;
            if (week && (day > startDay || day == startDay && hr >= startHour) && !(day == 7 && hr >= 15)) return true;
            int now = wk * 7 + (day - 1);
            for (int w = wk; w <= wk + 2; w++)
            {
                if ((w % 2 == 1) != ice) continue;
                int st = w * 7 + (startDay - 1);
                if (st < now || st == now && hr >= startHour) continue;
                int d = st - now;
                next = d == 0 ? Lang.T("aujourd'hui \u00E0 ", "today at ") + startHour + " h"
                     : d == 1 ? Lang.T("demain (", "tomorrow (") + Day(startDay) + " " + startHour + " h)"
                     : Lang.T("dans ", "in ") + d + Lang.T(" jours (", " days (") + Day(startDay) + " " + startHour + " h)";
                return false;
            }
            return false;
        }
    }

    public static class RacePanel
    {
        static float P(float v) { return Style.Px(v); }

        public static string Sub
        {
            get { return Lang.T("Inscription avec ta voiture · week-end de course · chronos en direct", "Sign up with your car · race weekend · live timing"); }
        }

        // Bandeau « pas en ce moment, disponible dans X jours » (JD, 18/10) ; renvoie vrai si l'evenement est en cours.
        static bool Banner(Rect v, ref float y, bool ice)
        {
            string next;
            if (RaceCal.Live(ice, out next)) return true;
            var r = new Rect(v.x, y, v.width, P(48));
            Style.Round(r, P(10), Kit.A(Kit.Amber, 0.10f));
            Style.Ring(r, P(10), Kit.A(Kit.Amber, 0.30f));
            Style.Text(new Rect(r.x + P(14), r.y + P(5), r.width - P(28), P(20)), ice ? Lang.T("Pas de course sur glace en ce moment", "No ice race right now") : Lang.T("Pas de rallye en ce moment", "No rally right now"), 14, TextAnchor.MiddleLeft, Kit.Amber);
            Style.Text(new Rect(r.x + P(14), r.y + P(24), r.width - P(28), P(18)), next != null ? Lang.T("Disponible ", "Available ") + next : Lang.T("Date inconnue", "Date unknown"), 12, TextAnchor.MiddleLeft, Kit.Soft, false);
            y += P(56);
            return false;
        }

        public static void DrawTab(Rect v)
        {
            float y = v.y, w = v.width;
            bool live = Banner(v, ref y, true);
            // mon inscription
            Kit.Section(new Rect(v.x, y, w, P(20)), Lang.T("TON INSCRIPTION", "YOUR ENTRY")); y += P(24);
            string st; Color sc;
            if (!live) { st = Lang.T("Pas inscrit", "Not registered"); sc = Kit.Red; }
            else if (!PlayerSync.InGame || !IceRace.Ready) { st = Lang.T("Course sur glace pas encore prête (en jeu, au bout de quelques secondes)", "Ice race not ready yet (in game, after a few seconds)"); sc = Kit.Muted; }
            else if (IceRace.MeWithVaz) { st = Lang.T("Inscrit avec ta voiture · n° ", "Registered with your car · no. ") + IceRace.MyNumber + Lang.T(" (elle remplace la voiture IA de ce numéro)", " (it takes that AI car's place)"); sc = Kit.Green; }
            else if (IceRace.VazPending) { st = Lang.T("Inscrit avec ta voiture · numéro en attente de l'hôte", "Registered with your car · waiting for the host's number"); sc = Kit.Amber; }
            else if (IceRace.MeRegistered) { st = Lang.T("Inscrit avec la Rivett · n° 14", "Registered with the Rivett · no. 14"); sc = Kit.Green; }
            else { st = Lang.T("Pas inscrit", "Not registered"); sc = Kit.Red; }
            Style.Round(new Rect(v.x, y, w, P(34)), P(10), Kit.A(sc, 0.10f));
            Style.Round(new Rect(v.x + P(12), y + P(13), P(8), P(8)), P(4), sc);
            Style.Text(new Rect(v.x + P(28), y, w - P(36), P(34)), st, 14, TextAnchor.MiddleLeft, Kit.Ink);
            y += P(42);
            // comment et quand
            string how = Lang.T(
                "1. Prends ta voiture (onglet VAZ-2108) et va au circuit de la course sur glace.\n" +
                "2. Vendredi 9 h - 15 h : gare ta voiture juste à côté de la tente d'inspection (moins de 10 m), puis remplis la feuille d'inscription (150 mk). Ensuite, roule aux essais : ton meilleur tour te place en qualifications.\n" +
                "3. Samedi 9 h - 15 h : qualifications et repêchages. Dimanche 10 h - 15 h : demi-finales et finale. Sois au circuit avec ta voiture : au départ de ta manche, elle est posée sur ta place de la grille.",
                "1. Take your car (VAZ-2108 tab) and drive to the ice race track.\n" +
                "2. Friday 9 am - 3 pm: park your car right next to the inspection tent (under 10 m), then fill in the registration form (150 mk). Then drive in practice: your best lap sets your qualifying place.\n" +
                "3. Saturday 9 am - 3 pm: qualifying and pickups. Sunday 10 am - 3 pm: semifinals and final. Be at the track with your car: when your heat starts, it is put on your grid spot.");
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
            if (!live) return;   // (pas de chronos hors du week-end de course)
            Kit.Section(new Rect(v.x, y + P(4), w, P(20)), Lang.T("CHRONOS", "TIMING")); y += P(28);
            if (rows.Count == 0 || title == null) { Style.Text(new Rect(v.x, y, w, P(22)), Lang.T("Rien en piste pour l'instant (essais le vendredi, manches le week-end)", "Nothing on track yet (practice on Friday, heats at the weekend)"), 12, TextAnchor.MiddleLeft, Kit.Muted, false); return; }
            Style.Text(new Rect(v.x, y, w, P(20)), title, 12, TextAnchor.MiddleLeft, Kit.Ice); y += P(22);
            Table(new Rect(v.x, y, w, v.yMax - y), rows, 12);
        }

        public static string RallySub { get { return Lang.T("Inscription chez Fleetari avec ta voiture \u00B7 d\u00E9parts \u00B7 classement", "Sign up at Fleetari's with your car \u00B7 starts \u00B7 standings"); } }

        public static void DrawRallyTab(Rect v)
        {
            float y = v.y, w = v.width;
            bool live = Banner(v, ref y, false);
            Kit.Section(new Rect(v.x, y, w, P(20)), Lang.T("TON INSCRIPTION", "YOUR ENTRY")); y += P(24);
            string st; Color sc;
            if (!PlayerSync.InGame || !Rally.Ready) { st = live ? Lang.T("Rallye pas encore pr\u00EAt (en jeu, au bout de quelques secondes)", "Rally not ready yet (in game, after a few seconds)") : Lang.T("Pas inscrit", "Not registered"); sc = live ? Kit.Muted : Kit.Red; }
            else if (Rally.MeWithVaz) { st = Lang.T("Inscrit avec ta voiture \u00B7 classe ", "Registered with your car \u00B7 class ") + Rally.MyClass; sc = Kit.Green; }
            else if (Rally.MeRegistered) { st = Lang.T("Inscrit avec la Rivett \u00B7 classe ", "Registered with the Rivett \u00B7 class ") + Rally.MyClass; sc = Kit.Green; }
            else { st = Lang.T("Pas inscrit", "Not registered"); sc = Kit.Red; }
            Style.Round(new Rect(v.x, y, w, P(34)), P(10), Kit.A(sc, 0.10f));
            Style.Round(new Rect(v.x + P(12), y + P(13), P(8), P(8)), P(4), sc);
            Style.Text(new Rect(v.x + P(28), y, w - P(36), P(34)), st, 14, TextAnchor.MiddleLeft, Kit.Ink);
            y += P(40);
            if (Rally.Ready && Rally.MeRegistered)
            {
                int[] t = Rally.MyStarts;
                string starts = t[0] > 0 ? Lang.T("D\u00E9parts : SS1 samedi ", "Starts: SS1 Saturday ") + t[0] + " h \u00B7 SS2 " + t[1] + " h \u00B7 SS3 " + Lang.T("dimanche ", "Sunday ") + t[2] + " h" : Lang.T("Heures de d\u00E9part : en attente", "Start times: pending");
                string done = Rally.MyStages + "/3 " + Lang.T("sp\u00E9ciales", "stages") + (Rally.MyStages > 0 ? " \u00B7 total " + Lap(Rally.MyTotal) : "");
                Style.Text(new Rect(v.x, y, w, P(18)), starts, 12, TextAnchor.MiddleLeft, Kit.Ice);
                Style.Text(new Rect(v.x, y + P(18), w, P(18)), done, 12, TextAnchor.MiddleLeft, Kit.Soft, false);
                y += P(42);
            }
            string how = Lang.T(
                "1. Prends ta voiture (onglet VAZ-2108). Samedi 8 h - 12 h : gare-la pr\u00E8s de la tente de Fleetari (moins de 40 m), parle-lui et remplis la feuille (500 mk en Junior, 1000 mk en Amateur). Sans VAZ, c'est la Rivett et son inspection.\n" +
                "2. Tes heures de d\u00E9part s'affichent ici : SS1 samedi apr\u00E8s-midi, SS2 samedi soir, SS3 dimanche matin. Sois au d\u00E9part avec ta voiture (moins de 6 m de la ligne) \u00E0 ton heure.\n" +
                "3. Entre les sp\u00E9ciales, laisse ta voiture au parc ferm\u00E9 (sinon : p\u00E9nalit\u00E9).",
                "1. Take your car (VAZ-2108 tab). Saturday 8 am - 12 pm: park it near Fleetari's tent (under 40 m), talk to him and fill in the form (500 mk Junior, 1000 mk Amateur). Without a VAZ, it's the Rivett and its inspection.\n" +
                "2. Your start times show here: SS1 Saturday afternoon, SS2 Saturday evening, SS3 Sunday morning. Be at the start with your car (under 6 m from the line) at your time.\n" +
                "3. Between stages, leave your car in parc ferme (otherwise: penalty).");
            float hh = P(84);
            Style.Text(new Rect(v.x, y, w, hh), how, 12, TextAnchor.UpperLeft, Kit.Soft, false, true);
            y += hh + P(4);
            List<Rally.Entrant> ents = Rally.Entrants();
            Kit.Section(new Rect(v.x, y, w, P(20)), Lang.T("PILOTES", "DRIVERS")); y += P(24);
            if (ents.Count == 0) { Style.Text(new Rect(v.x, y, w, P(22)), Lang.T("Personne pour l'instant", "Nobody yet"), 12, TextAnchor.MiddleLeft, Kit.Muted, false); y += P(26); }
            int shown = 0;
            foreach (Rally.Entrant en in ents)
            {
                if (shown++ >= 5) break;
                var r = new Rect(v.x, y, w, P(24));
                if (en.Me) Style.Round(r, P(8), Kit.A(Kit.Ice, 0.08f));
                Style.Text(new Rect(r.x + P(8), r.y, w * 0.34f, r.height), en.Name, 13, TextAnchor.MiddleLeft, en.Me ? Kit.Ice : Kit.Ink);
                Style.Text(new Rect(r.x + w * 0.36f, r.y, w * 0.28f, r.height), en.Registered ? (en.Cls ?? "") : Lang.T("pas inscrit", "not registered"), 12, TextAnchor.MiddleLeft, en.Registered ? Kit.Green : Kit.Muted, false);
                Style.Text(new Rect(r.x + w * 0.64f, r.y, w * 0.36f - P(8), r.height), en.Stages > 0 ? en.Stages + "/3 \u00B7 " + Lap(en.Total) : "", 12, TextAnchor.MiddleRight, Kit.Soft, false);
                y += P(26);
            }
            if (!live) return;
            List<Rally.Standing> rows = Rally.Standings();
            Kit.Section(new Rect(v.x, y + P(4), w, P(20)), Lang.T("CLASSEMENT DE TA CLASSE", "YOUR CLASS STANDINGS")); y += P(28);
            if (rows.Count == 0) { Style.Text(new Rect(v.x, y, w, P(22)), Lang.T("Apr\u00E8s ta premi\u00E8re sp\u00E9ciale", "After your first stage"), 12, TextAnchor.MiddleLeft, Kit.Muted, false); return; }
            float rh = P(20);
            int cap = Mathf.Max(1, Mathf.FloorToInt((v.yMax - y) / rh)), me = rows.FindIndex(x => x.Me);
            if (me >= cap && rows.Count > cap) { var cut = rows.GetRange(0, cap - 1); cut.Add(rows[me]); rows = cut; }
            foreach (Rally.Standing row in rows)
            {
                if (y + rh > v.yMax + 0.5f) break;
                var rr = new Rect(v.x, y, w, rh);
                if (row.Me) Style.Round(rr, P(6), Kit.A(Kit.Ice, 0.12f));
                Color c = row.Me ? Kit.Ice : row.Human ? Kit.Ink : Kit.Soft;
                Style.Text(new Rect(rr.x + P(4), y, w * 0.08f, rh), row.Pos.ToString(), 12, TextAnchor.MiddleLeft, c);
                Style.Text(new Rect(rr.x + w * 0.10f, y, w * 0.6f, rh), row.Name, 12, TextAnchor.MiddleLeft, c, row.Human);
                Style.Text(new Rect(rr.x + w * 0.70f, y, w * 0.30f - P(8), rh), Lap(row.Total), 12, TextAnchor.MiddleRight, c, row.Human);
                y += rh;
            }
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
            if (Menu.Open || Config.GetInt("Hud", "ChronoGlace", 1) == 0 || !IceRace.LiveShow) { shownAt = -1f; return; }
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
