using System.Collections.Generic;
using UnityEngine;

namespace MWCoop
{
    // Courses sur la carte (demande de JD, 18/10) :
    //  - rallye : trace de chaque speciale du jour encore a courir par au moins un pilote inscrit (Rally.StageOnMap), en orange
    //    (la mienne, epaisse ; celle des autres seulement, fine) ; depart marque avec MON heure de depart et le temps restant
    //    (« depart 16 h, dans 23 min »), arrivee marquee. Trace : celui du KARTTURI (Kartturi.StageRoute, routes du jeu) ;
    //  - course sur glace : la piste (TRAFFIC/Routes/Icetrack) pendant le week-end de course, et ma place sur la grille quand ma
    //    manche est la prochaine (IceRace.MyHeat).
    // Chacun voit ses heures et sa manche : rien d'envoye, tout est lu ici.
    public static partial class MapPanel
    {
        static readonly Color RallyMine = new Color(1f, 0.55f, 0.2f), RallyOther = new Color(1f, 0.7f, 0.4f, 0.55f), IceTrack = new Color(0.1f, 0.45f, 0.95f, 0.95f);   // (bleu soutenu : la piste est sur le lac gele, bleu clair)
        static List<Vector3> iceRoute; static int iceLevel = -1;

        // pins = false : les traces seulement (sous les lieux) ; pins = true : departs, arrivees, ma place (au-dessus de tout,
        // dessines en dernier ; JD 18/10).
        static void DrawRaces(Rect local, List<Rect> used, bool pins)
        {
            string next;
            // rallye
            if (Rally.Ready && RaceCal.Live(false, out next))
                for (int n = 1; n <= 3; n++)
                {
                    bool mine; int start, left;
                    if (!Rally.StageOnMap(n, out mine, out start, out left)) continue;
                    Vector3[] r = Kartturi.StageRoute(n);
                    if (r == null || r.Length < 2) continue;
                    if (!pins) { Polyline(local, r, mine ? RallyMine : RallyOther, Style.Px(mine ? 4 : 2)); continue; }
                    string label = "SS" + n;
                    if (mine && start > 0) label += Lang.T(" · ton départ ", " · your start ") + start + " h" + Until(start);
                    else label += " · " + left + Lang.T(left > 1 ? " pilotes restants" : " pilote restant", left > 1 ? " drivers left" : " driver left");
                    Pin(local, used, r[0], label, mine ? RallyMine : RallyOther, true);
                    Pin(local, used, r[r.Length - 1], Lang.T("Arrivée SS", "Finish SS") + n, mine ? RallyMine : RallyOther, false);
                }
            // course sur glace
            if (RaceCal.Live(true, out next) && IceRace.Ready)
            {
                if (iceLevel != Application.loadedLevel || iceRoute == null || iceRoute.Count < 2) { iceRoute = Kartturi.GameRoute("Icetrack"); iceLevel = Application.loadedLevel; }
                if (!pins) { if (iceRoute != null && iceRoute.Count > 1) Polyline(local, iceRoute.ToArray(), IceTrack, Style.Px(4), true); return; }
                string heat; int lane; Vector3 slot;
                if (IceRace.MyHeat(out heat, out lane, out slot) && slot != Vector3.zero)
                    Pin(local, used, slot, Lang.T("Ta place : manche ", "Your spot: heat ") + heat + Lang.T(", voie ", ", lane ") + lane, IceTrack, true);
            }
        }

        // Temps restant avant une heure du jeu (ClockMinutes, GlobalHour) : « , dans 23 min », « , dans 2 h 05 », rien si passee.
        static string Until(int hour)
        {
            HutongGames.PlayMaker.FsmInt h = HutongGames.PlayMaker.FsmVariables.GlobalVariables.FindFsmInt("GlobalHour");
            HutongGames.PlayMaker.FsmFloat m = HutongGames.PlayMaker.FsmVariables.GlobalVariables.FindFsmFloat("ClockMinutes");
            if (h == null) return "";
            int left = hour * 60 - (h.Value * 60 + (m != null ? Mathf.FloorToInt(m.Value) : 0));
            if (left <= 0) return Lang.T(", c'est l'heure", ", it's time");
            return Lang.T(", dans ", ", in ") + (left < 60 ? left + " min" : (left / 60) + " h " + (left % 60).ToString("00"));
        }

        // Ligne brisee (monde -> carte) : un point tous les 3 px a l'ecran au moins, segments hors de la vue sautes.
        static void Polyline(Rect local, Vector3[] pts, Color c, float width, bool closed = false)
        {
            if (Event.current.type != EventType.Repaint) return;
            Vector2 last = ToScreen(local, pts[0].x, pts[0].z), first = last;
            float min = Style.Px(3) * Style.Px(3);
            for (int i = 1; i < pts.Length; i++)
            {
                Vector2 s = ToScreen(local, pts[i].x, pts[i].z);
                if ((s - last).sqrMagnitude < min && i < pts.Length - 1) continue;
                Seg(local, last, s, width, c);
                last = s;
            }
            if (closed) Seg(local, last, first, width, c);
        }

        static void Seg(Rect local, Vector2 a, Vector2 b, float w, Color c)
        {
            Rect bb = Rect.MinMaxRect(Mathf.Min(a.x, b.x) - w, Mathf.Min(a.y, b.y) - w, Mathf.Max(a.x, b.x) + w, Mathf.Max(a.y, b.y) + w);
            if (!bb.Overlaps(local)) return;
            Vector2 d = b - a;
            float len = d.magnitude;
            if (len < 0.5f) return;
            Matrix4x4 was = GUI.matrix;
            GUIUtility.RotateAroundPivot(Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg, a);
            Style.Fill(new Rect(a.x, a.y - w / 2f, len + w / 2f, w), c);
            GUI.matrix = was;
        }

        // Point toujours dessine (depart, arrivee, ma place, objectifs) ; son nom aussi, sauf s'il chevaucherait un autre nom deja
        // ecrit (les objectifs sont aussi dans la liste en haut a gauche).
        static void Pin(Rect local, List<Rect> used, Vector3 at, string name, Color c, bool big)
        {
            Vector2 s = ToScreen(local, at.x, at.z);
            if (!local.Contains(s)) return;
            float r = Style.Px(big ? 8 : 6);
            Style.Round(new Rect(s.x - r - Style.Px(2), s.y - r - Style.Px(2), 2 * r + Style.Px(4), 2 * r + Style.Px(4)), r + Style.Px(2), new Color(0f, 0f, 0f, 0.8f));
            Style.Round(new Rect(s.x - r, s.y - r, 2 * r, 2 * r), r, c);
            Style.Round(new Rect(s.x - r * 0.4f, s.y - r * 0.4f, r * 0.8f, r * 0.8f), r * 0.4f, Color.white);
            var lr = new Rect(s.x + r + Style.Px(6), s.y - Style.Px(12), Style.Width(name, 14, true) + Style.Px(6), Style.Px(24));
            foreach (Rect u in used) if (u.Overlaps(lr)) return;   // (deux noms l'un sur l'autre : le premier ; on zoome pour l'autre)
            Style.Round(new Rect(lr.x - Style.Px(4), lr.y + Style.Px(1), lr.width + Style.Px(4), lr.height - Style.Px(2)), Style.Px(6), new Color(0f, 0f, 0f, 0.55f));
            Label(lr, name, 14, c);
            used.Add(lr);
        }
    }
}
