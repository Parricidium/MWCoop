using System.Collections.Generic;
using System.IO;
using MWCoop.Net;
using UnityEngine;

namespace MWCoop
{
    // Carte en grand (demande de JD, 10/10) : touche Keys.Map (M, reglable dans le lanceur). La carte vue du ciel faite par
    // MapRender, les joueurs en direct (pseudo, fleche du regard, en voiture), les lieux. Molette : zoom autour de la
    // souris ; glisser : deplacer ; C : recentrer sur soi ; Echap ou la touche : fermer.
    public static class MapPanel
    {
        public static bool Open;
        static float openedAt;
        static Vector2 center;          // monde (x, z) au centre de la vue
        static float zoom = -1f;        // pixels d'ecran par metre
        static bool dragging; static Vector2 dragFrom;
        static readonly Color32 Forest = new Color32(64, 92, 74, 255);   // (fond : forets, autour du monde)

        public static void Update()
        {
            if (!PlayerSync.InGame) { Open = false; return; }
            if (Menu.Open || Menu.ChatOpen) return;
            if (Open && Input.GetKeyDown(KeyCode.Escape)) { Open = false; return; }
            // [Coop] CarteMaintenir=1 (lanceur) : ouverte tant que la touche est tenue ; sinon un appui ouvre, un autre ferme
            bool hold = Config.GetInt("Coop", "CarteMaintenir", 0) != 0;
            bool want = hold ? Input.GetKey(Keys.Map) : (Input.GetKeyDown(Keys.Map) ? !Open : Open);
            if (hold && testHold) want = true;
            if (want != Open)
            {
                Open = want;
                if (Open) { openedAt = Time.realtimeSinceStartup; WalletPanel.Open = false; CheatPanel.Open = false; CenterOnMe(); }
            }
            if (Open && Input.GetKeyDown(KeyCode.C)) CenterOnMe();
        }

        static void CenterOnMe()
        {
            Transform p = Game.PlayerT;
            if (p != null) center = new Vector2(p.position.x, p.position.z);
        }

        // monde (x, z) -> ecran, et inverse
        static Vector2 ToScreen(Rect view, float x, float z) { return new Vector2(view.center.x + (x - center.x) * zoom, view.center.y - (z - center.y) * zoom); }
        static Vector2 ToWorld(Rect view, Vector2 s) { return new Vector2(center.x + (s.x - view.center.x) / zoom, center.y - (s.y - view.center.y) / zoom); }

        public static void Draw()
        {
            if (!Open) return;
            Event e = Event.current;
            float k = Mathf.Clamp01((Time.realtimeSinceStartup - openedAt) / 0.14f);
            Style.Alpha = 1f - (1f - k) * (1f - k);
            float m = Style.Px(24);
            var panel = new Rect(m, m, Screen.width - 2 * m, Screen.height - 2 * m);
            Style.Glass(panel, Style.Px(20));
            float pad = Style.Px(18);
            Style.Title(new Rect(panel.x + pad, panel.y + Style.Px(8), panel.width - 2 * pad, Style.Px(40)), Lang.T("CARTE", "MAP"), Style.White, TextAnchor.MiddleLeft, 24);
            Style.Text(new Rect(panel.x + pad, panel.y + Style.Px(8), panel.width - 2 * pad, Style.Px(40)),
                       Lang.T("Molette : zoom · glisser : déplacer · C : moi · ", "Wheel: zoom · drag: move · C: me · ") + Keys.Label(Keys.Map) + Lang.T(" / Échap : fermer", " / Esc: close"), 14, TextAnchor.MiddleRight, Style.Dim, false);
            var view = new Rect(panel.x + pad, panel.y + Style.Px(52), panel.width - 2 * pad, panel.height - Style.Px(52) - pad);
            // (hors de la carte : le verre du panneau, demande de JD)
            if (!MapRender.Ready)
            {
                string pct = MapRender.Progress >= 0f ? " " + Mathf.FloorToInt(MapRender.Progress * 100f) + " %" : "";
                Style.Text(view, Lang.T("Carte en préparation", "Map being made") + pct + "…", 17, TextAnchor.MiddleCenter, Style.Dim, false);
                Style.Alpha = 1f;
                return;
            }
            if (zoom < 0f) zoom = Mathf.Min(view.width, view.height) / MapRender.Size;
            float minZoom = Mathf.Min(view.width, view.height) / MapRender.Size * 0.8f, maxZoom = 40f;   // (vectoriel : on peut zoomer loin)
            // entrees : zoom autour de la souris, glisser
            if (view.Contains(e.mousePosition))
            {
                if (e.type == EventType.ScrollWheel)
                {
                    Vector2 before = ToWorld(view, e.mousePosition);
                    zoom = Mathf.Clamp(zoom * (e.delta.y < 0 ? 1.25f : 0.8f), minZoom, maxZoom);
                    Vector2 after = ToWorld(view, e.mousePosition);
                    center += before - after;
                    e.Use();
                }
                if (e.type == EventType.MouseDown && e.button == 0) { dragging = true; dragFrom = e.mousePosition; e.Use(); }
            }
            if (dragging && e.type == EventType.MouseDrag) { Vector2 d = e.mousePosition - dragFrom; dragFrom = e.mousePosition; center += new Vector2(-d.x, d.y) / zoom; e.Use(); }
            if (e.type == EventType.MouseUp) dragging = false;
            // la carte, dessinee a la taille de la vue (vectorielle : nette a tous les zooms)
            GUI.BeginGroup(view);
            var local = new Rect(0, 0, view.width, view.height);
            if (e.type == EventType.Repaint)
            {
                RenderTexture tex = MapRender.View(Mathf.RoundToInt(view.width), Mathf.RoundToInt(view.height), center, 1f / zoom, Forest);
                if (tex != null)
                {
                    Color was = GUI.color;
                    GUI.color = new Color(1f, 1f, 1f, Style.Alpha);
                    GUI.DrawTexture(local, tex);
                    GUI.color = was;
                }
            }
            // lieux (un nom qui en chevaucherait un autre n'est pas ecrit : on zoome pour le voir)
            var used = new List<Rect>();
            // (places des pastilles des joueurs d'abord : un nom de lieu dessous n'est pas ecrit)
            foreach (PlayerInfo pi in Session.Players.Values)
            {
                Vector3 at = pi.Local ? (Game.PlayerT != null ? Game.PlayerT.position : Vector3.zero) : pi.State.Feet;
                if (!pi.Local && pi.Level != 1) continue;
                Vector2 ps = ToScreen(local, at.x, at.z);
                used.Add(new Rect(ps.x - Style.Px(12), ps.y - Style.Px(14), Style.Px(40) + Style.Width(pi.Local ? Lang.T("Moi", "Me") : pi.Name, 14, true) + Style.Px(24), Style.Px(28)));
            }
            foreach (Cheats.Place p in Cheats.Places)
            {
                Vector2 s = ToScreen(local, p.Face != Vector3.zero ? p.Face.x : p.At.x, p.Face != Vector3.zero ? p.Face.z : p.At.z);
                if (!local.Contains(s)) continue;
                Style.Round(new Rect(s.x - Style.Px(4), s.y - Style.Px(4), Style.Px(8), Style.Px(8)), Style.Px(4), new Color(1f, 0.85f, 0.35f, 0.95f));
                string name = Lang.Fr ? p.Fr : p.En;
                var lr = new Rect(s.x + Style.Px(8), s.y - Style.Px(11), Style.Width(name, 13, true) + Style.Px(4), Style.Px(22));
                bool clash = false;
                foreach (Rect u in used) if (u.Overlaps(lr)) { clash = true; break; }
                if (clash) continue;
                used.Add(lr);
                Label(lr, name, 13, new Color(1f, 0.92f, 0.7f));
            }
            // joueurs
            foreach (PlayerInfo pi in Session.Players.Values)
            {
                if (pi.Local || pi.Level != 1) continue;
                Marker(local, pi.State.Feet, pi.State.Yaw, pi.Name, Style.Accent, VehicleSync.RemoteCarName(pi.Id) != null || Seats.RemoteSeatIndex(pi.Id) >= 0);
            }
            Transform me = Game.PlayerT;
            if (me != null)
            {
                float yaw = PlayerSync.LocalCamera != null ? PlayerSync.LocalCamera.eulerAngles.y : me.eulerAngles.y;
                Marker(local, me.position, yaw, Lang.T("Moi", "Me"), Style.Good, VehicleSync.LocalDriving >= 0 || Seats.Seated);
            }
            GUI.EndGroup();
            // echelle
            // echelle : 1, 2, 5 x 10^n metres, la plus petite qui fait au moins 70 px
            float meters = 1f;
            for (int g = 0; g < 30 && meters * zoom < Style.Px(70); g++) { float d = Mathf.Pow(10f, Mathf.Floor(Mathf.Log10(meters))); meters = meters / d < 1.5f ? 2f * d : meters / d < 3.5f ? 5f * d : 10f * d; }
            float bar = meters * zoom;
            var sr = new Rect(view.x + Style.Px(14), view.yMax - Style.Px(30), bar, Style.Px(4));
            Style.Fill(sr, Style.White);
            Label(new Rect(sr.x, sr.y - Style.Px(22), Style.Px(160), Style.Px(20)), meters >= 1000f ? (meters / 1000f) + " km" : meters + " m", 13, Style.White);
            Style.Alpha = 1f;
        }

        static void Label(Rect r, string s, int size, Color c)
        {
            Style.Text(new Rect(r.x + 1, r.y + 1, r.width, r.height), s, size, TextAnchor.MiddleLeft, new Color(0f, 0f, 0f, 0.85f), true);
            Style.Text(r, s, size, TextAnchor.MiddleLeft, c, true);
        }

        // Pastille du joueur, fleche du regard, pseudo.
        static void Marker(Rect local, Vector3 at, float yaw, string name, Color c, bool inCar)
        {
            Vector2 s = ToScreen(local, at.x, at.z);
            if (!local.Contains(s)) return;
            float r = Style.Px(inCar ? 9 : 7);
            // fleche : un trait du centre vers le regard (rotation de l'interface autour de la pastille)
            Matrix4x4 keep = GUI.matrix;
            GUIUtility.RotateAroundPivot(yaw, s);
            Style.Fill(new Rect(s.x - Style.Px(2), s.y - r - Style.Px(12), Style.Px(4), Style.Px(14)), c);
            GUI.matrix = keep;
            Style.Round(new Rect(s.x - r - Style.Px(2), s.y - r - Style.Px(2), 2 * r + Style.Px(4), 2 * r + Style.Px(4)), r + Style.Px(2), new Color(0f, 0f, 0f, 0.7f));
            Style.Round(new Rect(s.x - r, s.y - r, 2 * r, 2 * r), r, c);
            if (inCar) Style.Round(new Rect(s.x - r * 0.45f, s.y - r * 0.45f, r * 0.9f, r * 0.9f), r * 0.45f, Style.White);
            // pseudo dans une pastille au-dessus du point (lisible sur la carte claire, distinct des noms de lieux)
            float tw = Style.Width(name, 14, true) + Style.Px(18), th = Style.Px(24);
            var pill = new Rect(Mathf.Round(s.x + r + Style.Px(16)), Mathf.Round(s.y - th / 2), tw, th);   // (a droite : la fleche reste visible)
            Style.Round(pill, th / 2, new Color(0.06f, 0.08f, 0.12f, 0.88f));
            Style.Round(new Rect(pill.x, pill.yMax - Style.Px(3), pill.width, Style.Px(3)), Style.Px(1.5f), c);
            Style.Text(pill, name, 14, TextAnchor.MiddleCenter, Style.White, true);
        }

        // [Test] Autotest=carte : a [Test] CarteOuvre (90 s) la carte s'ouvre (zoom [Test] CarteZoom), capture a +2 s.
        static int testStep;
        static bool testHold;
        public static void Test(string mode, float t)
        {
            if (mode != "carte") return;
            // (invite : au garage a 30 s, pour voir deux joueurs loin l'un de l'autre)
            if (!Session.IsHost && testStep == 0 && t > 30f) { testStep = 9; foreach (Cheats.Place p in Cheats.Places) if (p.Key == "garage") Cheats.GoPlace(p); }
            if (!Session.IsHost) return;
            float at = Config.GetFloat("Test", "CarteOuvre", 90f);
            if (testStep == 0 && t > at)
            {
                testStep = 1;
                Open = true; openedAt = 0; CenterOnMe();
                zoom = -1f;
                float z = Config.GetFloat("Test", "CarteZoom", -1f); if (z > 0f) zoom = z;
                Autotest.CaptureSoon("carte", 2f);
                Log.Info("autotest : carte ouverte, prete " + MapRender.Ready);
            }
            if (testStep == 1 && t > at + 5f) { testStep = 2; CenterOnMe(); zoom = Config.GetFloat("Test", "CarteZoom2", 1.2f); Autotest.CaptureSoon("carte-zoom", 2f); }
            if (testStep == 2 && t > at + 10f) { testStep = 3; CenterOnMe(); zoom = Config.GetFloat("Test", "CarteZoom3", 8f); Autotest.CaptureSoon("carte-zoom2", 2f); }
        }
    }
}
