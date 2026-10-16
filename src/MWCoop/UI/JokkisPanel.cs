using System.Collections.Generic;
using MWCoop.Net;
using UnityEngine;

namespace MWCoop
{
    // Choix d'une Jokkis de location pour la course sur glace (16/10) : les 16 voitures IA du jeu (Jokkis) groupees par
    // modele, chacune avec son numero, son pilote, son club et son etat (libre / prise par <joueur> / la votre), un bouton
    // CHOISIR (ou RENDRE sur la sienne) ; en tete, la Rivett n 14 du jeu, gratuite aussi (information). A droite, la voiture
    // en surbrillance en 3D : copie visuelle (Jokkis.BuildPreview) posee loin sous le monde (Studio.OtherPivot) sur la couche
    // du studio des tenues (Studio.PreviewLayer), qui tourne lentement (glisser a la souris pour la tourner), photographiee
    // a chaque image par la camera du studio dans une RenderTexture 512 x 320 (Studio.RenderPreview) tant que le panneau
    // est ouvert -- rien du tout quand il est ferme (copie detruite, texture liberee).
    // Ouverture : menu F10 > JOUEURS > " Course sur glace : choisir une voiture ", ou IceRace (JokkisPanel.Open = true)
    // quand le joueur s'inscrit au tableau LINEUPS. Echap ferme ; la souris est libre pendant ce temps (Menu.Block,
    // Menu.CursorWanted). Clavier : Haut/Bas voiture, Entree choisir / rendre.
    public static class JokkisPanel
    {
        public static bool Open;
        static bool wasOpen, hooked;
        static float openedAt;
        static int hi;                              // numero en surbrillance (apercu, Entree)
        static int pending; static float pendingAt;  // numero demande a l'hote, en attente de sa table (invite)
        static string note; static float noteUntil; static bool noteBad;
        static float scroll, listH, contentH, hiY = -1f;
        static bool follow;
        static Vector2 lastMouse; static bool mouseMoved;
        static int rows;                            // lignes dessinees au dernier passage (autotest)
        static readonly string[] Models = { "Bubple 1600", "Costo MK2", "Svoboda 180", "Fittan LX", "EDM 700" };
        static readonly List<Jokkis.Def> order = new List<Jokkis.Def>();   // ordre d'affichage (Haut/Bas)

        // apercu 3D
        const int RtW = 512, RtH = 320;
        const float Fov = 24f, Elev = 17f;          // champ vertical (degres), hauteur de la camera au-dessus de l'horizon
        static readonly Color Bg = new Color(0.07f, 0.11f, 0.18f, 1f);        // bleu nuit des panneaux (comme Studio.LiveBg)
        static readonly Color PreviewBg = new Color(0f, 0.02f, 0.06f, 0.30f);
        static GameObject pv; static int pvNumber; static float pvYaw, pvT, pvRetryAt, dragAt;
        static Vector3 pvCenter; static float pvRadius, pvHalfH, pvDist;
        static RenderTexture rt; static bool rtReady, dragging;
        static int renders;

        public static void Update()
        {
            if (!PlayerSync.InGame) Open = false;
            else if (Open && (Menu.Open || Menu.ChatOpen || WalletPanel.Open || CheatPanel.Open || MapPanel.Open)) Open = false;   // (un autre panneau a pris la place)
            else if (Open && Input.GetKeyDown(KeyCode.Escape)) Open = false;
            if (Open != wasOpen) { wasOpen = Open; if (Open) Opened(); else Closed(); }
            if (!Open) return;
            if (pending != 0 && Time.realtimeSinceStartup - pendingAt > 6f) pending = 0;   // (sans reponse : on n'attend plus)
            Preview();
        }

        static void Opened()
        {
            openedAt = Time.realtimeSinceStartup; note = null; pending = 0; scroll = 0f; follow = true; dragging = false;
            WalletPanel.Open = CheatPanel.Open = MapPanel.Open = false;
            if (!hooked) { hooked = true; Jokkis.Changed += OnChanged; }
            int mine = Jokkis.ChosenNumber(Session.LocalId);
            IList<Jokkis.Def> all = Jokkis.All();
            hi = mine != 0 ? mine : all.Count > 0 ? all[0].Number : 0;
            if (rt == null)
            {
                rt = new RenderTexture(RtW, RtH, 24, RenderTextureFormat.ARGB32);
                rt.antiAliasing = Mathf.Clamp(Config.GetInt("Coop", "ApercuTenuesAA", 4), 1, 8);
                rt.hideFlags = HideFlags.HideAndDontSave;
                rt.Create();
            }
            rtReady = false; renders = 0;
        }

        static void Closed()
        {
            DestroyPreview();
            if (rt != null) { rt.Release(); Object.Destroy(rt); rt = null; }
            rtReady = false; dragging = false;
        }

        // Table de l'hote changee : la demande en cours a abouti (la notre) ou non (prise par un autre entre-temps).
        static void OnChanged()
        {
            if (!Open || pending == 0) return;
            int mine = Jokkis.ChosenNumber(Session.LocalId);
            if (mine == pending) { Note(Lang.T("La Jokkis n\u00B0 ", "Jokkis no. ") + mine + Lang.T(" est \u00E0 vous", " is yours"), false); pending = 0; }
            else if (!Jokkis.IsFree(pending)) { Note(Lang.T("La n\u00B0 ", "No. ") + pending + Lang.T(" vient d'\u00EAtre prise", " was just taken"), true); pending = 0; }
        }

        static void Note(string s, bool bad) { note = s; noteBad = bad; noteUntil = Time.realtimeSinceStartup + 4f; }

        // Proprietaire du numero (null : libre ; "?" : pris par un joueur absent de la liste).
        static string OwnerOf(int n, out bool mine)
        {
            mine = false;
            foreach (PlayerInfo pi in Session.Players.Values) if (Jokkis.ChosenNumber(pi.Id) == n) { mine = pi.Local; return pi.Name; }
            if (Jokkis.ChosenNumber(Session.LocalId) == n) { mine = true; return Session.Me != null ? Session.Me.Name : ""; }
            return Jokkis.IsFree(n) ? null : "?";
        }

        // Entree ou bouton : choisir ce numero, ou rendre le sien.
        static void Act(int n)
        {
            Jokkis.Def d = Jokkis.DefOf(n);
            if (d == null) return;
            int mine = Jokkis.ChosenNumber(Session.LocalId);
            if (mine == n)
            {
                Jokkis.Release(Session.LocalId);
                Note(Session.IsHost ? Lang.T("Jokkis n\u00B0 ", "Jokkis no. ") + n + Lang.T(" rendue", " given back") : Lang.T("Retour de la n\u00B0 ", "Return of no. ") + n + Lang.T(" demand\u00E9 \u00E0 l'h\u00F4te", " requested from the host"), false);
                return;
            }
            if (!Session.Active) { Note(Lang.T("En solo : les Jokkis se louent en partie \u00E0 plusieurs", "Solo: Jokkis are rented in a multiplayer game"), true); return; }
            if (mine != 0) { Note(Lang.T("Rendez d'abord la n\u00B0 ", "Give back no. ") + mine + Lang.T("", " first"), true); return; }
            if (!Jokkis.IsFree(n)) { Note(Lang.T("La n\u00B0 ", "No. ") + n + Lang.T(" est d\u00E9j\u00E0 prise", " is already taken"), true); return; }
            pending = n; pendingAt = Time.realtimeSinceStartup;
            if (!Jokkis.Request(n))   // (refus connu d'avance : Jokkis l'a dit a l'ecran, sauf module coupe)
            {
                pending = 0;
                if (Jokkis.ChosenNumber(Session.LocalId) != n) Note(Lang.T("Location refus\u00E9e", "Rental refused"), true);
                return;
            }
            if (!Session.IsHost && pending != 0) Note(Lang.T("Demande envoy\u00E9e \u00E0 l'h\u00F4te\u2026", "Request sent to the host\u2026"), false);
        }

        // ---------------------------------------------------------------- apercu 3D

        static void DestroyPreview() { if (pv != null) Object.Destroy(pv); pv = null; }

        // Chaque image, panneau ouvert : la copie du numero en surbrillance (refaite s'il change), tournee, photographiee.
        static void Preview()
        {
            float now = Time.realtimeSinceStartup;
            if (pv == null || pvNumber != hi)
            {
                DestroyPreview();
                if (hi == 0 || (pvNumber == hi && now < pvRetryAt)) return;
                pvNumber = hi;
                try { pv = Jokkis.BuildPreview(hi, Studio.PreviewLayer); }
                catch (System.Exception e) { Log.Warn("jokkis (apercu) : n " + hi + " : " + e.Message); pv = null; }
                if (pv == null) { pvRetryAt = now + 2f; return; }
                Measure();
                pvYaw = 35f; pvT = now;   // (trois quarts avant au depart)
            }
            float dt = Mathf.Clamp(now - pvT, 0f, 0.1f);
            pvT = now;
            if (now - dragAt > 1.5f) pvYaw += 18f * dt;   // tourne lentement
            Quaternion r = Quaternion.Euler(0f, pvYaw, 0f);
            Transform t = pv.transform;
            t.rotation = r;
            t.position = Studio.OtherPivot - r * pvCenter;   // (le centre de la voiture reste sur l'axe)
            // Camera du cote +Z, un peu au-dessus, tournee vers l'axe : le cote eclaire par la lumiere cle du studio.
            Vector3 pos = Studio.OtherPivot + new Vector3(0f, Mathf.Sin(Elev * Mathf.Deg2Rad) * pvDist, Mathf.Cos(Elev * Mathf.Deg2Rad) * pvDist);
            if (Studio.RenderPreview(pos, Studio.OtherPivot, Fov, pvDist - pvRadius - 1f, pvDist + pvRadius + 1f, rt, Bg)) { rtReady = true; renders++; }
        }

        // Boite de la copie dans le repere de sa racine (encore a l'origine, sans rotation) : centre, rayon au sol (la voiture
        // tourne : la diagonale de son empreinte doit tenir en largeur), demi-hauteur ; distance de la camera pour tout cadrer.
        static void Measure()
        {
            Bounds b = new Bounds();
            bool any = false;
            int n = 0;
            foreach (MeshFilter mf in pv.GetComponentsInChildren<MeshFilter>(true))
            {
                Mesh m = mf.sharedMesh;
                if (m == null) continue;
                n++;
                Matrix4x4 mx = mf.transform.localToWorldMatrix;
                Bounds mb = m.bounds;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 c = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) != 0 ? 1f : -1f, (i & 2) != 0 ? 1f : -1f, (i & 4) != 0 ? 1f : -1f));
                    Vector3 w = mx.MultiplyPoint3x4(c);
                    if (!any) { b = new Bounds(w, Vector3.zero); any = true; } else b.Encapsulate(w);
                }
            }
            if (!any) { pvCenter = Vector3.zero; pvRadius = 2.4f; pvHalfH = 0.8f; }
            else { pvCenter = b.center; pvRadius = Mathf.Max(0.5f, new Vector2(b.size.x, b.size.z).magnitude / 2f); pvHalfH = Mathf.Max(0.3f, b.size.y / 2f); }
            float tanH = Mathf.Tan(Fov * 0.5f * Mathf.Deg2Rad), aspect = RtW / (float)RtH;
            float dh = pvRadius / (tanH * aspect), dv = (pvHalfH + pvRadius * Mathf.Sin(Elev * Mathf.Deg2Rad)) / tanH;
            pvDist = Mathf.Max(dh, dv) * 1.12f + pvRadius * 0.15f;
            Log.Info("jokkis (apercu) : n " + pvNumber + ", " + n + " maillages, boite " + (any ? b.size.ToString("F2") : "inconnue") + ", camera a " + pvDist.ToString("F1") + " m, couche " + Studio.PreviewLayer);
        }

        // ---------------------------------------------------------------- dessin

        static bool Button(Rect r, string label, bool accent, Event e, bool enabled = true)
        {
            bool hover = enabled && r.Contains(e.mousePosition);
            Color bg = !enabled ? new Color(1f, 1f, 1f, 0.04f) : accent ? (hover ? Style.Accent : Style.AccentDeep) : new Color(1f, 1f, 1f, hover ? 0.14f : 0.08f);
            Style.Round(r, r.height / 2, bg);
            Style.Text(r, label, 14, TextAnchor.MiddleCenter, !enabled ? Style.Dim : accent ? Style.OnAccent : Style.White);
            if (enabled && e.type == EventType.MouseDown && e.button == 0 && hover) { e.Use(); return true; }
            return false;
        }

        static void MoveHi(int d)
        {
            if (order.Count == 0) return;
            int i = 0;
            for (int k = 0; k < order.Count; k++) if (order[k].Number == hi) i = k;
            hi = order[Mathf.Clamp(i + d, 0, order.Count - 1)].Number;
            follow = true;
        }

        public static void Draw()
        {
            if (!Open) return;
            Event e = Event.current;
            float now = Time.realtimeSinceStartup;
            if (e.type == EventType.Repaint) { mouseMoved = (e.mousePosition - lastMouse).sqrMagnitude > 1f; lastMouse = e.mousePosition; }
            if (e.type == EventType.KeyDown)
            {
                if (e.keyCode == KeyCode.UpArrow) { MoveHi(-1); e.Use(); }
                else if (e.keyCode == KeyCode.DownArrow) { MoveHi(1); e.Use(); }
                else if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) { Act(hi); e.Use(); }
            }
            float k = Mathf.Clamp01((now - openedAt) / 0.14f);
            Style.Alpha = 1f - (1f - k) * (1f - k);
            float pad = Style.Px(22), one = Mathf.Max(1f, Style.Px(1));
            float w = Mathf.Min(Style.Px(1080), Screen.width - Style.Px(32));
            float h = Mathf.Min(Style.Px(900), Screen.height - Style.Px(24));
            float x = Mathf.Round((Screen.width - w) / 2), y = Mathf.Round((Screen.height - h) / 2 + (1f - k) * Style.Px(12));
            Style.Glass(new Rect(x, y, w, h), Style.Px(20));
            Style.Title(new Rect(x + pad, y + Style.Px(12), w - 2 * pad, Style.Px(40)), Lang.T("JOKKIS DE LOCATION", "RENTAL JOKKIS"), Style.White, TextAnchor.MiddleLeft, 26);
            Style.Text(new Rect(x + pad, y + Style.Px(12), w - 2 * pad, Style.Px(40)), Lang.T("\u00C9chap : fermer", "Esc: close"), 14, TextAnchor.MiddleRight, Style.Dim, false);
            Style.Text(new Rect(x + pad, y + Style.Px(54), w - 2 * pad, Style.Px(26)),
                       Lang.T("Course sur glace : une voiture par joueur, gratuite, premier arriv\u00E9 premier servi ; rendue \u00E0 la fin du week-end.",
                              "Ice race: one car per player, free, first come first served; given back at the end of the weekend."), 15, TextAnchor.MiddleLeft, Style.Dim, false);
            // colonnes : liste a gauche, apercu et fiche a droite
            float top = y + Style.Px(92), foot = Style.Px(44);
            float pvW = Mathf.Min(Style.Px(430), Mathf.Round((w - 2 * pad) * 0.42f));
            var list = new Rect(x + pad, top, w - 2 * pad - pvW - Style.Px(22), y + h - foot - pad - top);
            var pvr = new Rect(list.xMax + Style.Px(22), top, pvW, Mathf.Round(pvW * RtH / (float)RtW));
            listH = list.height;
            if (e.type == EventType.ScrollWheel && list.Contains(e.mousePosition)) { scroll += e.delta.y * Style.Px(14); e.Use(); }
            scroll = Mathf.Clamp(scroll, 0f, Mathf.Max(0f, contentH - listH));
            DrawList(list, e);
            if (contentH > listH)
            {
                var track = new Rect(list.xMax - Style.Px(5), list.y, Mathf.Max(3f, Style.Px(5)), listH);
                Style.Round(track, track.width / 2, Style.Line);
                float thumb = Mathf.Max(Style.Px(28), listH * listH / contentH);
                Style.Round(new Rect(track.x, track.y + (listH - thumb) * scroll / (contentH - listH), track.width, thumb), track.width / 2, Style.Accent);
            }
            DrawPreview(pvr, e, now);
            DrawCard(new Rect(pvr.x, pvr.yMax + Style.Px(14), pvr.width, list.yMax - pvr.yMax - Style.Px(14)), e);
            // pied : resultat ou aide
            Style.Fill(new Rect(x + pad, y + h - foot - Style.Px(6), w - 2 * pad, one), Style.Line);
            var fr = new Rect(x + pad, y + h - foot, w - 2 * pad, Style.Px(30));
            if (note != null && now < noteUntil) Style.Text(fr, note, 15, TextAnchor.MiddleLeft, noteBad ? Style.Warn : Style.Good, false);
            else Style.Text(fr, Lang.T("Haut/Bas : voiture  \u00B7  Entr\u00E9e : choisir ou rendre  \u00B7  glisser l'aper\u00E7u : tourner  \u00B7  \u00C9chap : fermer",
                                       "Up/Down: car  \u00B7  Enter: choose or give back  \u00B7  drag the preview: turn  \u00B7  Esc: close"), 14, TextAnchor.MiddleLeft, Style.Dim, false);
            Style.Alpha = 1f;
        }

        // Liste : la Rivett (information), puis les Jokkis par modele.
        static void DrawList(Rect view, Event e)
        {
            GUI.BeginGroup(view);
            float cw = view.width - Style.Px(12), cy = 0f, rowH = Style.Px(36), headH = Style.Px(30);
            int n = 0;
            order.Clear();
            hiY = -1f;
            // Rivett n 14 : la voiture du jeu
            var rr = new Rect(0, cy - scroll, cw, Style.Px(40));
            if (Visible(rr, view.height) && e.type == EventType.Repaint)
            {
                Style.Round(rr, Style.Px(10), new Color(1f, 1f, 1f, 0.05f));
                Pill(new Rect(rr.x + Style.Px(10), rr.y + Style.Px(7), Style.Px(58), rr.height - Style.Px(14)), "n\u00B0 14", Style.Dim);
                Style.Text(new Rect(rr.x + Style.Px(78), rr.y, cw - Style.Px(90), rr.height), Lang.T("Rivett n\u00B014 : la voiture du jeu, gratuite aussi", "Rivett no. 14: the game's car, free as well"), 16, TextAnchor.MiddleLeft, Style.White, false);
            }
            cy += rr.height + Style.Px(6); n++;
            IList<Jokkis.Def> all = Jokkis.All();
            int mine = Jokkis.ChosenNumber(Session.LocalId);
            for (int g = 0; g <= Models.Length; g++)
            {
                bool any = false;
                foreach (Jokkis.Def d in all)
                {
                    bool inGroup = g < Models.Length ? d.Model == Models[g] : System.Array.IndexOf(Models, d.Model) < 0;
                    if (!inGroup) continue;
                    if (!any)
                    {
                        any = true;
                        var hr = new Rect(0, cy - scroll, cw, headH);
                        if (Visible(hr, view.height) && e.type == EventType.Repaint)
                        {
                            Style.Title(new Rect(hr.x + Style.Px(10), hr.y + Style.Px(6), hr.width - Style.Px(20), hr.height - Style.Px(8)), g < Models.Length ? Models[g].ToUpperInvariant() : Lang.T("AUTRES", "OTHERS"), Style.Accent, TextAnchor.MiddleLeft, 15);
                            Style.Fill(new Rect(hr.x + Style.Px(10), hr.yMax - Style.Px(2), hr.width - Style.Px(20), Mathf.Max(1f, Style.Px(1))), Style.Line);
                        }
                        cy += headH + Style.Px(2); n++;
                    }
                    order.Add(d);
                    var r = new Rect(0, cy - scroll, cw, rowH);
                    cy += rowH + Style.Px(2); n++;
                    bool vis = Visible(r, view.height), selected = d.Number == hi;
                    if (selected) hiY = r.y + scroll;
                    if (vis && mouseMoved && r.Contains(e.mousePosition)) hi = d.Number;
                    bool isMine;
                    string owner = OwnerOf(d.Number, out isMine);
                    float bw = Style.Px(104);
                    var br = new Rect(r.xMax - bw - Style.Px(8), r.y + Style.Px(5), bw, rowH - Style.Px(10));
                    if (vis)
                    {
                        if (isMine) { if (Button(br, Lang.T("RENDRE", "GIVE BACK"), false, e)) Act(d.Number); }
                        else if (owner == null) { if (Button(br, Lang.T("CHOISIR", "CHOOSE"), true, e, mine == 0 && pending == 0)) Act(d.Number); }
                        if (e.type == EventType.MouseDown && e.button == 0 && r.Contains(e.mousePosition)) { hi = d.Number; e.Use(); }
                    }
                    if (!vis || e.type != EventType.Repaint) continue;
                    if (selected) { Style.Round(r, Style.Px(10), new Color(Style.Accent.r, Style.Accent.g, Style.Accent.b, 0.20f)); Style.Ring(r, Style.Px(10), new Color(Style.Accent.r, Style.Accent.g, Style.Accent.b, 0.60f)); }
                    Pill(new Rect(r.x + Style.Px(10), r.y + Style.Px(6), Style.Px(58), rowH - Style.Px(12)), "n\u00B0 " + d.Number, isMine ? Style.Accent : Style.White);
                    float tx = r.x + Style.Px(78);
                    Style.Text(new Rect(tx, r.y, Style.Px(150), rowH), d.Driver, 17, TextAnchor.MiddleLeft, Style.White);
                    Style.Text(new Rect(tx + Style.Px(150), r.y, Style.Px(110), rowH), d.Club, 14, TextAnchor.MiddleLeft, Style.Dim, false);
                    string st = isMine ? Lang.T("la v\u00F4tre", "yours") : owner == null ? Lang.T("libre", "free") : Lang.T("prise par ", "taken by ") + owner;
                    Style.Text(new Rect(tx + Style.Px(260), r.y, br.x - Style.Px(8) - (tx + Style.Px(260)), rowH), st, 14, TextAnchor.MiddleRight, isMine ? Style.Accent : owner == null ? Style.Good : Style.Dim, false);
                }
            }
            GUI.EndGroup();
            contentH = cy;
            if (e.type == EventType.Repaint) rows = n;
            // Haut/Bas : la ligne en surbrillance reste visible.
            if (follow && hiY >= 0f)
            {
                if (hiY < scroll) scroll = hiY;
                else if (hiY + rowH > scroll + listH) scroll = hiY + rowH - listH;
                follow = false;
            }
        }

        static bool Visible(Rect r, float viewH) { return r.yMax > 0 && r.y < viewH; }

        static void Pill(Rect r, string s, Color c)
        {
            Style.Round(r, r.height / 2, new Color(1f, 1f, 1f, 0.08f));
            Style.Ring(r, r.height / 2, new Color(c.r, c.g, c.b, 0.5f));
            Style.Text(r, s, 13, TextAnchor.MiddleCenter, c);
        }

        // La voiture en 3D : texture du studio dans un cadre arrondi ; glisser pour la tourner.
        static void DrawPreview(Rect r, Event e, float now)
        {
            if (e.type == EventType.MouseDown && e.button == 0 && r.Contains(e.mousePosition)) { dragging = true; e.Use(); }
            else if (e.type == EventType.MouseDrag && dragging) { pvYaw += e.delta.x * 0.6f; dragAt = now; e.Use(); }
            else if (e.rawType == EventType.MouseUp && dragging) dragging = false;
            if (e.type != EventType.Repaint) return;
            float rad = Style.Px(16);
            Style.Round(r, rad, PreviewBg);
            Texture tex = rtReady && pv != null ? rt : null;
            if (tex != null && !Backdrop.DrawImage(r, rad, tex, Style.Alpha))
            {
                // Sans maillage arrondi (ou pendant le fondu) : image carree, en retrait pour rester dans le cadre.
                float ins = Mathf.Round(rad * 0.3f);
                Color oc = GUI.color;
                GUI.color = new Color(1f, 1f, 1f, Style.Alpha);
                GUI.DrawTexture(new Rect(r.x + ins, r.y + ins, r.width - 2 * ins, r.height - 2 * ins), tex, ScaleMode.StretchToFill, false);
                GUI.color = oc;
            }
            else if (tex == null) Style.Text(r, pv == null && hi != 0 && pvNumber == hi ? Lang.T("Aper\u00E7u indisponible", "Preview unavailable") : "\u2026", 16, TextAnchor.MiddleCenter, Style.Dim, false);
            Style.Ring(r, rad, Style.Line);
        }

        // Fiche de la voiture en surbrillance : numero, pilote, modele et club, etat, grand bouton.
        static void DrawCard(Rect r, Event e)
        {
            Jokkis.Def d = Jokkis.DefOf(hi);
            if (d == null || r.height < Style.Px(60)) return;
            bool isMine;
            string owner = OwnerOf(d.Number, out isMine);
            int mine = Jokkis.ChosenNumber(Session.LocalId);
            float cy = r.y;
            Style.Title(new Rect(r.x, cy, r.width, Style.Px(44)), "N\u00B0 " + d.Number + "  " + d.Driver.ToUpperInvariant(), Style.White, TextAnchor.MiddleLeft, 26);
            cy += Style.Px(46);
            Style.Text(new Rect(r.x, cy, r.width, Style.Px(24)), d.Model + "  \u00B7  " + d.Club, 16, TextAnchor.MiddleLeft, Style.Dim, false);
            cy += Style.Px(28);
            string st = isMine ? Lang.T("La v\u00F4tre cette semaine", "Yours this week") : owner == null ? Lang.T("Libre", "Free") : Lang.T("Prise par ", "Taken by ") + owner;
            Style.Text(new Rect(r.x, cy, r.width, Style.Px(24)), st, 16, TextAnchor.MiddleLeft, isMine ? Style.Accent : owner == null ? Style.Good : Style.Warn, false);
            cy += Style.Px(34);
            if (cy + Style.Px(44) > r.yMax) return;
            var br = new Rect(r.x, cy, r.width, Style.Px(44));
            if (isMine) { if (Button(br, Lang.T("RENDRE LA N\u00B0 ", "GIVE BACK NO. ") + d.Number, false, e)) Act(d.Number); }
            else if (owner == null) { if (Button(br, Lang.T("CHOISIR LA N\u00B0 ", "CHOOSE NO. ") + d.Number, true, e, mine == 0 && pending == 0)) Act(d.Number); }
            else Button(br, Lang.T("D\u00C9J\u00C0 PRISE", "ALREADY TAKEN"), false, e, false);
            if (mine != 0 && !isMine && owner == null) Style.Text(new Rect(r.x, br.yMax + Style.Px(6), r.width, Style.Px(22)), Lang.T("Rendez d'abord la n\u00B0 ", "Give back no. ") + mine + Lang.T("", " first"), 13, TextAnchor.MiddleLeft, Style.Dim, false);
        }

        // ---------------------------------------------------------------- autotest "jokkispanel"
        // [Test] Autotest=jokkispanel : a 25 s l'hote ouvre le panneau (capture jokkis-panneau a 27 s) ; a 30 s il note le nombre de
        // lignes, la voiture en surbrillance et l'apercu (objet, texture rendue ? taille, rendus) ; a 35 s il le ferme, bilan a 36 s.
        static int testStep;
        public static void Test(string mode, float t)
        {
            if (mode != "jokkispanel" || !Session.IsHost) return;
            if (testStep == 0 && t > 25f) { testStep = 1; Open = true; Autotest.CaptureSoon("jokkis-panneau", 2f); Log.Info("autotest jokkispanel : panneau ouvert"); }
            if (testStep == 1 && t > 30f)
            {
                testStep = 2;
                Jokkis.Def d = Jokkis.DefOf(hi);
                Log.Info("autotest jokkispanel : " + rows + " lignes (" + order.Count + " jokkis), surbrillance n " + hi + (d != null ? " (" + d.Driver + ", " + d.Model + ")" : " (?)")
                         + ", apercu " + (pv != null ? pv.GetComponentsInChildren<MeshRenderer>(true).Length + " maillages" : "sans objet")
                         + ", texture " + (rtReady && rt != null ? "rendue " + rt.width + "x" + rt.height + ", " + renders + " rendus" : "JAMAIS rendue") + ", ouvert " + Open);
            }
            if (testStep == 2 && t > 35f) { testStep = 3; Open = false; }
            if (testStep == 3 && t > 36f) { testStep = 4; Log.Info("autotest jokkispanel : panneau ferme, apercu " + (pv == null ? "detruit" : "ENCORE LA") + ", texture " + (rt == null ? "liberee" : "ENCORE LA")); }
        }
    }
}
