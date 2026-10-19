using System.Collections.Generic;
using System.IO;
using MWCoop.Net;
using UnityEngine;

namespace MWCoop
{
    // Carte en grand (demande de JD, 10/10) : touche Keys.Map (M, reglable dans le lanceur). La carte vue du ciel faite par
    // MapRender, les joueurs en direct (pseudo, fleche du regard, en voiture), les lieux. Molette : zoom autour de la
    // souris ; glisser : deplacer ; C : recentrer sur soi ; Echap ou la touche : fermer. Clic droit : son marqueur partage
    // (Markers : pose, deplace, retire sur le sien) ; les marqueurs de tous, a la couleur de leur joueur.
    public static partial class MapPanel   // (+ MapRaces.cs : rallye et course sur glace ; MapJobs.cs : objectifs des boulots)
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
            if (!Game.Msc) JobsTick();   // (objectifs des boulots, MapJobs.cs : suivis meme carte fermee, messages d'etape ; MSC : pas encore)
            if (Menu.Open || Menu.ChatOpen) return;
            if (Open && Input.GetKeyDown(KeyCode.Escape)) { Open = false; return; }
            // [Coop] CarteMaintenir=1 (lanceur) : ouverte tant que la touche est tenue ; sinon un appui ouvre, un autre ferme
            bool hold = Config.GetInt("Coop", "CarteMaintenir", 0) != 0;
            bool want = hold ? Input.GetKey(Keys.Map) : (Input.GetKeyDown(Keys.Map) ? !Open : Open);
            if (want != Open)
            {
                Open = want;
                if (Open) { openedAt = Time.realtimeSinceStartup; WalletPanel.Open = false; CheatPanel.Open = false; CenterOnMe(); }
            }
            if (Open && Input.GetKeyDown(KeyCode.C)) CenterOnMe();
            if (Open && Input.GetKeyDown(KeyCode.B)) showJobs = !showJobs;
            TaxiTick();
        }

        // ---------------------------------------------------------------- boulots
        // Lieux ou gagner de l'argent (demande de « ...., », forum des suggestions, 13/10) : fosses septiques (JOBS/HouseShitN),
        // livraisons de bois (JOBS/HouseWoodN/WoodJobNPoint), tas de bois a ramasser (JOBS/LogLongPile), tracts (JOBS/ADs) ;
        // et pour le taxi, le client qui attend et la destination demandee (JOBS/TAXIJOB/Customer1/TaxiWalker :: Logic,
        // variable DropOffPoint). L'automate du client tourne chez l'hote : il envoie la destination (@taxicarte).
        // B, carte ouverte : montrer / cacher.
        class Spot { public Vector3 At; public string Fr, En; public Color C; }
        static readonly List<Spot> spots = new List<Spot>();
        static int spotsLevel = -1;
        static bool showJobs = true;
        static readonly Color JobColor = new Color(0.45f, 0.85f, 1f), TaxiColor = new Color(1f, 0.62f, 0.25f);

        static void BuildSpots()
        {
            if (Game.Msc && spotsLevel == Application.loadedLevel && spots.Count > 0) return;
            if (spotsLevel == Application.loadedLevel && spots.Count > 0) return;
            spots.Clear();
            spotsLevel = Application.loadedLevel;
            GameObject jobs = Game.FindAny("JOBS");
            if (jobs == null) return;
            foreach (Transform c in jobs.transform)
            {
                if (c.name.StartsWith("HouseShit")) { Transform w = c.Find("WasteWell_2000litre"); spots.Add(new Spot { At = (w ?? c).position, Fr = "Fosse septique", En = "Septic tank", C = JobColor }); }
                else if (c.name.StartsWith("HouseWood"))
                    foreach (Transform d in c) { if (d.name.StartsWith("WoodJob")) spots.Add(new Spot { At = d.position, Fr = "Livraison de bois", En = "Firewood delivery", C = JobColor }); }
                else if (c.name == "LogLongPile") spots.Add(new Spot { At = c.position, Fr = "Tas de bois", En = "Log pile", C = JobColor });
                else if (c.name == "ADs" || c.name == "TeimoADs") spots.Add(new Spot { At = c.position, Fr = "Tracts", En = "Flyers", C = JobColor });
                else if (Game.Msc && c.name == "StrawberryField") spots.Add(new Spot { At = c.position, Fr = "Champ de fraises", En = "Strawberry field", C = JobColor });
                else if (Game.Msc && c.name == "HayBales") spots.Add(new Spot { At = c.position, Fr = "Bottes de foin", En = "Hay bales", C = JobColor });
            }
            Log.Info("carte : " + spots.Count + " lieux de boulots");
        }

        static PlayMakerFSM taxiLogic; static GameObject taxiCust; static float taxiNext, taxiSentAt;
        static string taxiDropPath, taxiSent; static bool taxiActive;
        static void TaxiTick()
        {
            if (!PlayerSync.InGame || !Session.IsHost) return;
            float now = Time.realtimeSinceStartup;
            if (now < taxiNext) return;
            taxiNext = now + 2f;
            if (taxiCust == null) { taxiCust = Game.FindAny("JOBS/TAXIJOB/Customer1"); GameObject w = Game.FindAny("JOBS/TAXIJOB/Customer1/TaxiWalker"); taxiLogic = w != null ? Game.FsmOn(w, "Logic") : null; }
            HutongGames.PlayMaker.FsmGameObject drop = taxiLogic != null ? taxiLogic.FsmVariables.FindFsmGameObject("DropOffPoint") : null;
            taxiActive = taxiCust != null && taxiCust.activeInHierarchy;
            taxiDropPath = taxiActive && drop != null && drop.Value != null ? Recon.Path(drop.Value.transform) : "";
            string msg = (taxiActive ? "1" : "0") + taxiDropPath;
            if (Session.RemoteCount > 0 && (msg != taxiSent || now - taxiSentAt > 15f))
            {
                taxiSent = msg; taxiSentAt = now;
                Session.SendAll(new NetWriter(Msg.Job).U8(Session.LocalId).Str("@taxicarte").Bool(taxiActive).Str(taxiDropPath), true);
            }
        }

        public static void OnTaxi(int who, NetReader r)
        {
            bool active = r.Bool(); string drop = r.Str();
            if (Session.IsHost || who != 0) return;
            taxiActive = active; taxiDropPath = drop;
        }

        static void DrawJobs(Rect local, List<Rect> used)
        {
            BuildSpots();
            foreach (Spot sp in spots) Place(local, used, sp.At, Lang.T(sp.Fr, sp.En), sp.C, false);
            if (!taxiActive) return;
            if (taxiCust == null) taxiCust = Game.FindAny("JOBS/TAXIJOB/Customer1");
            Transform walker = taxiCust != null ? taxiCust.transform.Find("TaxiWalker") : null;
            if (walker != null && walker.gameObject.activeInHierarchy) Place(local, used, walker.position, Lang.T("Taxi : client", "Taxi: customer"), TaxiColor, true);
            GameObject dst = !string.IsNullOrEmpty(taxiDropPath) ? Game.FindAny(taxiDropPath) : null;
            if (dst != null) Place(local, used, dst.transform.position, Lang.T("Taxi : destination", "Taxi: destination"), TaxiColor, true);
        }

        static void Place(Rect local, List<Rect> used, Vector3 at, string name, Color c, bool big)
        {
            Vector2 s = ToScreen(local, at.x, at.z);
            if (!local.Contains(s)) return;
            float r = Style.Px(big ? 7 : 5);
            Style.Round(new Rect(s.x - r - Style.Px(2), s.y - r - Style.Px(2), 2 * r + Style.Px(4), 2 * r + Style.Px(4)), r + Style.Px(2), new Color(0f, 0f, 0f, 0.75f));
            Style.Round(new Rect(s.x - r, s.y - r, 2 * r, 2 * r), r, c);
            var lr = new Rect(s.x + r + Style.Px(5), s.y - Style.Px(11), Style.Width(name, 13, true) + Style.Px(4), Style.Px(22));
            foreach (Rect u in used) if (u.Overlaps(lr)) return;
            used.Add(lr);
            Label(lr, name, 13, c);
        }

        // Marqueurs partages (Markers) : epingle de la couleur du joueur, coeur blanc, nom du proprietaire (le sien : « Moi »).
        // Dessines apres les lieux et les boulots : un nom qui en chevaucherait un autre n'est pas ecrit (on zoome).
        static void DrawMarkers(Rect local, List<Rect> used)
        {
            if (Markers.Count == 0) return;
            foreach (Markers.Marker m in Markers.All)
            {
                if (!Markers.Shown(m)) continue;
                Vector2 s = ToScreen(local, m.At.x, m.At.z);
                if (!local.Contains(s)) continue;
                Color c = Markers.ColorOf(m.Owner);
                float r = Style.Px(8);
                Style.Round(new Rect(s.x - r - Style.Px(2), s.y - r - Style.Px(2), 2 * r + Style.Px(4), 2 * r + Style.Px(4)), r + Style.Px(2), new Color(0f, 0f, 0f, 0.75f));
                Style.Round(new Rect(s.x - r, s.y - r, 2 * r, 2 * r), r, c);
                Style.Round(new Rect(s.x - r * 0.4f, s.y - r * 0.4f, r * 0.8f, r * 0.8f), r * 0.4f, Style.White);
                string name = Markers.NameOf(m.Owner);
                var lr = new Rect(s.x + r + Style.Px(5), s.y - Style.Px(11), Style.Width(name, 13, true) + Style.Px(4), Style.Px(22));
                bool clash = false;
                foreach (Rect u in used) if (u.Overlaps(lr)) { clash = true; break; }
                if (clash) continue;
                used.Add(lr);
                Label(lr, name, 13, c);
            }
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
                       Lang.T("Molette : zoom · glisser : déplacer · C : moi · ", "Wheel: zoom · drag: move · C: me · ") + Lang.T("B : boulots · ", "B: jobs · ") + Lang.T("clic droit : marqueur · ", "right-click: marker · ") + Keys.Label(Keys.Map) + Lang.T(" / Échap : fermer", " / Esc: close"), 14, TextAnchor.MiddleRight, Style.Dim, false);
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
                // clic droit : son marqueur ici (sur le sien, a l'ecran : le retire)
                if (e.type == EventType.MouseDown && e.button == 1)
                {
                    Markers.Marker mine = Markers.Mine;
                    if (mine != null && Vector2.Distance(ToScreen(view, mine.At.x, mine.At.z), e.mousePosition) < Style.Px(14)) Markers.Remove();
                    else { Vector2 w = ToWorld(view, e.mousePosition); Markers.PlaceOnMap(w.x, w.y); }
                    e.Use();
                }
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
                string name = Lang.T(p.Fr, p.En);
                var lr = new Rect(s.x + Style.Px(8), s.y - Style.Px(11), Style.Width(name, 13, true) + Style.Px(4), Style.Px(22));
                bool clash = false;
                foreach (Rect u in used) if (u.Overlaps(lr)) { clash = true; break; }
                if (clash) continue;
                used.Add(lr);
                Label(lr, name, 13, new Color(1f, 0.92f, 0.7f));
            }
            DrawRaces(local, used, false);   // (traces : sous les lieux)
            if (showJobs) DrawJobs(local, used);
            DrawMarkers(local, used);
            // joueurs
            foreach (PlayerInfo pi in Session.Players.Values)
            {
                if (pi.Local || pi.Level != 1) continue;
                Marker(local, pi.State.Feet, pi.State.Yaw, pi.Name, Markers.ColorOf(pi.Id), VehicleSync.RemoteCarName(pi.Id) != null || Seats.RemoteSeatIndex(pi.Id) >= 0);   // (sa couleur : celle de son marqueur)
            }
            Transform me = Game.PlayerT;
            if (me != null)
            {
                float yaw = PlayerSync.LocalCamera != null ? PlayerSync.LocalCamera.eulerAngles.y : me.eulerAngles.y;
                Marker(local, me.position, yaw, Lang.T("Moi", "Me"), Markers.ColorOf(Session.LocalId), VehicleSync.LocalDriving >= 0 || Seats.Seated);
            }
            // objectifs des quetes et points des courses : au-dessus de tous les autres marqueurs (JD, 18/10), noms entre eux seulement
            var top = new List<Rect>();
            DrawRaces(local, top, true);
            DrawObjectives(local, top);
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
            Style.Round(pill, th / 2, Kit.A(Style.Shade, 0.88f));
            Style.Round(new Rect(pill.x, pill.yMax - Style.Px(3), pill.width, Style.Px(3)), Style.Px(1.5f), c);
            Style.Text(pill, name, 14, TextAnchor.MiddleCenter, Style.White, true);
        }

        // [Test] Autotest=carte : a [Test] CarteOuvre (90 s) la carte s'ouvre (zoom [Test] CarteZoom), capture a +2 s.
        static int testStep;
        static bool probed;
        public static void Test(string mode, float t)
        {
            Markers.Test(mode, t);   // (Autotest=marqueur : pose / retrait de l'hote, releves de l'invite)
            TestObjectives(mode, t);   // (Autotest=objectifs, MapJobs.cs)
            if (mode == "carteboulots" && t > 30f && !probed)
            {
                probed = true;
                GameObject jobs = Game.FindAny("JOBS");
                var sb = new System.Text.StringBuilder("autotest : JOBS :");
                if (jobs != null)
                    foreach (Transform c in jobs.transform)
                    {
                        sb.Append(" | ").Append(c.name).Append(" ").Append(c.position.ToString("F0")).Append(c.gameObject.activeSelf ? "" : " (eteint)");
                        foreach (Transform d in c) sb.Append(" || ").Append(d.name).Append(" ").Append(d.position.ToString("F0")).Append(d.gameObject.activeSelf ? "" : " (eteint)");
                    }
                Log.Info(sb.ToString());
                GameObject cust = Game.FindAny("JOBS/TAXIJOB/Customer1");
                if (cust != null)
                    foreach (PlayMakerFSM f in cust.GetComponentsInChildren<PlayMakerFSM>(true))
                    {
                        var v = new System.Text.StringBuilder("autotest : taxi " + Recon.Path(f.transform) + "::" + f.FsmName + " [" + f.ActiveStateName + "] :");
                        foreach (var x in f.FsmVariables.GameObjectVariables) v.Append(' ').Append(x.Name).Append('=').Append(x.Value != null ? x.Value.name : "-");
                        foreach (var x in f.FsmVariables.Vector3Variables) v.Append(' ').Append(x.Name).Append('=').Append(x.Value.ToString("F0"));
                        foreach (var x in f.FsmVariables.StringVariables) v.Append(' ').Append(x.Name).Append("='").Append(x.Value).Append("'");
                        foreach (var x in f.FsmVariables.IntVariables) v.Append(' ').Append(x.Name).Append('=').Append(x.Value);
                        Log.Info(v.ToString());
                    }
            }
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
