using System.Collections.Generic;
using MWCoop.Net;
using UnityEngine;

namespace MWCoop
{
    // My Summer Car : les courses du jeu dans le F10, a la place de la course sur glace et du rallye de la VAZ (JD, 19/10 :
    // « la course sur glace n'est pas la, remplace-la par les evenements de cette version »). D'apres le vidage du jeu :
    //  - dragsters : DRAGRACE/LOD 'OpeningHours', le vendredi (GlobalDay 5), piste ouverte aux heures paires de 6 h a 20 h ;
    //  - rallye : RALLY 'Reset', le samedi et le dimanche (GlobalDay 6 et 7), de 10 h a 18 h.
    // Onglets : quand (en cours, ou dans combien de jours), ou (lieux de la carte, Cheats.Places), qui y est (joueurs a moins de 400 m).
    public static class MscEvents
    {
        public enum Kind { Drag, Rally }
        static float P(float v) { return Style.Px(v); }
        static int G(string n) { HutongGames.PlayMaker.FsmInt v = HutongGames.PlayMaker.FsmVariables.GlobalVariables.FindFsmInt(n); return v != null ? v.Value : -1; }
        static string Day(int d) { return Lang.A(new[] { "lundi", "mardi", "mercredi", "jeudi", "vendredi", "samedi", "dimanche" }, new[] { "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday" })[Mathf.Clamp(d - 1, 0, 6)]; }

        static void Hours(Kind k, out int[] days, out int from, out int to)
        {
            if (k == Kind.Drag) { days = new[] { 5 }; from = 6; to = 21; }
            else { days = new[] { 6, 7 }; from = 10; to = 18; }
        }

        // En cours ? Sinon next = quand (« demain (samedi 10 h) », « dans 3 jours (vendredi 6 h) »).
        public static bool Live(Kind k, out string next)
        {
            next = null;
            int day = G("GlobalDay"), hr = G("GlobalHour");
            if (day < 1 || hr < 0) return false;
            int[] days; int from, to;
            Hours(k, out days, out from, out to);
            if (System.Array.IndexOf(days, day) >= 0 && hr >= from && hr < to) return true;
            for (int d = 0; d <= 7; d++)
            {
                int wd = (day - 1 + d) % 7 + 1;
                if (System.Array.IndexOf(days, wd) < 0 || d == 0 && hr >= from) continue;
                next = d == 0 ? Lang.T("aujourd'hui à ", "today at ") + from + " h"
                     : d == 1 ? Lang.T("demain (", "tomorrow (") + Day(wd) + " " + from + " h)"
                     : Lang.T("dans ", "in ") + d + Lang.T(" jours (", " days (") + Day(wd) + " " + from + " h)";
                return false;
            }
            return false;
        }

        public static string When(Kind k)
        {
            return k == Kind.Drag ? Lang.T("Le vendredi, de 6 h à 20 h", "On Fridays, from 6 am to 8 pm")
                                  : Lang.T("Le samedi et le dimanche, de 10 h à 18 h", "On Saturdays and Sundays, from 10 am to 6 pm");
        }

        public static string Sub(Kind k)
        {
            return k == Kind.Drag ? Lang.T("La piste de dragsters · le vendredi · qui y est", "The drag strip · on Fridays · who is there")
                                  : Lang.T("Le rallye du week-end · samedi et dimanche · qui y est", "The weekend rally · Saturday and Sunday · who is there");
        }

        // Lieu de l'evenement (pour « qui y est ») : la piste, ou le depart du rallye.
        static Vector3? Place(Kind k)
        {
            GameObject g = k == Kind.Drag ? Game.FindAny("DRAGRACE/DistancePoint") : Game.FindAny("RALLY/SpawnToRally");
            return g != null ? g.transform.position : (Vector3?)null;
        }

        public static void DrawTab(Rect v, Kind k)
        {
            float y = v.y, w = v.width;
            string next;
            bool live = Live(k, out next);
            var r = new Rect(v.x, y, w, P(48));
            Color c = live ? Kit.Green : Kit.Amber;
            Style.Round(r, P(10), Kit.A(c, 0.10f));
            Style.Ring(r, P(10), Kit.A(c, 0.30f));
            string title = live ? (k == Kind.Drag ? Lang.T("La piste de dragsters est ouverte", "The drag strip is open") : Lang.T("Le rallye a lieu en ce moment", "The rally is on right now"))
                                : (k == Kind.Drag ? Lang.T("Pas de dragsters en ce moment", "No drag racing right now") : Lang.T("Pas de rallye en ce moment", "No rally right now"));
            Style.Text(new Rect(r.x + P(14), r.y + P(5), r.width - P(28), P(20)), title, 14, TextAnchor.MiddleLeft, c);
            Style.Text(new Rect(r.x + P(14), r.y + P(24), r.width - P(28), P(18)),
                       live ? When(k) : next != null ? Lang.T("Prochain : ", "Next: ") + next : When(k), 12, TextAnchor.MiddleLeft, Kit.Soft, false);
            y += P(60);

            Kit.Section(new Rect(v.x, y, w, P(20)), Lang.T("QUAND ET OÙ", "WHEN AND WHERE")); y += P(24);
            Style.Text(new Rect(v.x, y, w, P(20)), When(k), 13, TextAnchor.MiddleLeft, Kit.Ink); y += P(22);
            Style.Text(new Rect(v.x, y, w, P(20)), k == Kind.Drag ? Lang.T("Sur la carte (M) : le lieu « Dragsters »", "On the map (M): the “Drag strip” place")
                                                                   : Lang.T("Sur la carte (M) : le lieu « Rallye »", "On the map (M): the “Rally” place"), 13, TextAnchor.MiddleLeft, Kit.Muted, false);
            y += P(34);

            Kit.Section(new Rect(v.x, y, w, P(20)), Lang.T("QUI Y EST", "WHO IS THERE")); y += P(24);
            Vector3? at = Place(k);
            var here = new List<string>();
            if (at.HasValue && PlayerSync.InGame)
            {
                GameObject me = Game.FindAny("PLAYER");
                if (me != null && (me.transform.position - at.Value).magnitude < 400f) here.Add(Lang.T("Toi", "You"));
                foreach (Avatar a in PlayerSync.Avatars)
                    if (a != null && a.Root != null && a.Player != null && (a.Root.transform.position - at.Value).magnitude < 400f) here.Add(a.Player.Name);
            }
            if (here.Count == 0) Style.Text(new Rect(v.x, y, w, P(20)), Lang.T("Personne pour l'instant", "Nobody yet"), 13, TextAnchor.MiddleLeft, Kit.Faint, false);
            foreach (string n in here)
            {
                Style.Text(new Rect(v.x, y, w, P(20)), "•  " + n, 13, TextAnchor.MiddleLeft, Kit.Ink);
                y += P(22);
            }
        }
    }
}
