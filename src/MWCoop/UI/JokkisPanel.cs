using System.Collections.Generic;
using MWCoop.Net;
using UnityEngine;

namespace MWCoop
{
    // Course sur glace (16/10 ; onglet du menu F10 depuis la refonte du 18/10) : les 16 voitures IA du jeu (Jokkis) a louer,
    // groupees par modele (pastilles pour filtrer un modele), chacune avec son numero, son pilote, son club et son etat (libre
    // / prise par <joueur> / la votre) ; a droite la voiture en surbrillance en 3D, sa fiche et le bouton CHOISIR (ou RENDRE),
    // puis l'equipe (qui a quelle voiture). Apercu : copie visuelle (Jokkis.BuildPreview) posee loin sous le monde
    // (Studio.OtherPivot) sur la couche du studio des tenues (Studio.PreviewLayer), qui tourne lentement (glisser a la souris
    // pour la tourner), photographiee a chaque image par la camera du studio dans une RenderTexture 512 x 320
    // (Studio.RenderPreview) tant que l'onglet est affiche -- rien du tout sinon (copie detruite, texture liberee).
    // Ouverture : F10 > Course sur glace, ou Open = true (IceRace, quand le joueur s'inscrit au tableau LINEUPS ; autotest) qui
    // ouvre le menu sur cet onglet. Clavier (Menu.Key le passe ici) : Haut/Bas voiture (Haut sur la premiere : retour aux
    // onglets), Gauche/Droite modele, Entree choisir / rendre ; double clic sur une ligne : choisir / rendre.
    public static class JokkisPanel
    {
        public static bool Open;                    // demande d'ouverture : le menu F10 s'ouvre sur l'onglet
        static bool shown, hooked;
        static int hi;                              // numero en surbrillance (apercu, Entree)
        static int pending; static float pendingAt;  // numero demande a l'hote, en attente de sa table (invite)
        static string note; static float noteUntil; static bool noteBad;
        static float scroll, listH, contentH, hiY = -1f, rowH;
        static bool follow;
        static int rows;                            // lignes dessinees au dernier passage (autotest)
        static int filter;                          // 0 : toutes ; 1.. : Models[filter - 1]
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

        // Aide du pied du menu : le resultat de la derniere demande, sinon les touches.
        public static string Hint
        {
            get
            {
                if (note != null && Time.realtimeSinceStartup < noteUntil) return note;
                return Lang.T("Entr\u00E9e : choisir ou rendre  \u00B7  Gauche/Droite : mod\u00E8le  \u00B7  glisser l'aper\u00E7u : tourner",
                              "Enter: choose or give back  \u00B7  Left/Right: model  \u00B7  drag the preview: turn");
            }
        }

        public static void Update()
        {
            if (Open) { Open = false; if (PlayerSync.InGame) Menu.Show(Menu.TabRace); }
            bool now = PlayerSync.InGame && Menu.Open && Menu.Tab == Menu.TabRace;
            if (now != shown) { shown = now; if (now) Opened(); else Closed(); }
            if (!shown) return;
            if (pending != 0 && Time.realtimeSinceStartup - pendingAt > 6f) pending = 0;   // (sans reponse : on n'attend plus)
            Preview();
        }

        static void Opened()
        {
            note = null; pending = 0; scroll = 0f; follow = true; dragging = false;
            if (!hooked) { hooked = true; Jokkis.Changed += OnChanged; }
            int mine = Jokkis.ChosenNumber(Session.LocalId);
            IList<Jokkis.Def> all = Jokkis.All();
            if (mine != 0) { hi = mine; filter = 0; }
            else if (hi == 0 || Jokkis.DefOf(hi) == null) hi = all.Count > 0 ? all[0].Number : 0;
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
            if (!shown || pending == 0) return;
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

        // ---------------------------------------------------------------- dessin (onglet du menu)

        static bool InFilter(Jokkis.Def d, int f)
        {
            if (f == 0) return true;
            return f - 1 < Models.Length && d.Model == Models[f - 1];
        }

        static void SetFilter(int f)
        {
            int n = Models.Length + 1;
            filter = ((f % n) + n) % n;
            Jokkis.Def d = Jokkis.DefOf(hi);
            if (d == null || !InFilter(d, filter))
                foreach (Jokkis.Def x in Jokkis.All()) if (InFilter(x, filter)) { hi = x.Number; break; }
            scroll = 0f; follow = true;
        }

        static void MoveHi(int d)
        {
            if (order.Count == 0) return;
            int i = 0;
            for (int k = 0; k < order.Count; k++) if (order[k].Number == hi) i = k;
            hi = order[Mathf.Clamp(i + d, 0, order.Count - 1)].Number;
            follow = true;
        }

        // Touche passee par le menu (la liste a le clavier) : vrai si prise.
        public static bool Key(KeyCode k)
        {
            switch (k)
            {
                case KeyCode.UpArrow: if (order.Count == 0 || order[0].Number == hi) return false; MoveHi(-1); return true;
                case KeyCode.DownArrow: MoveHi(1); return true;
                case KeyCode.PageUp: MoveHi(-6); return true;
                case KeyCode.PageDown: MoveHi(6); return true;
                case KeyCode.LeftArrow: SetFilter(filter - 1); return true;
                case KeyCode.RightArrow: SetFilter(filter + 1); return true;
                case KeyCode.Return:
                case KeyCode.KeypadEnter: Act(hi); return true;
            }
            return false;
        }

        static float P(float v) { return Style.Px(v); }

        // Contenu de l'onglet dans v (coordonnees ecran) : vrai si la souris a choisi une voiture (le menu donne le clavier
        // a la liste).
        public static bool DrawTab(Rect v, Event e, bool focused, bool moved)
        {
            if (!PlayerSync.InGame)
            {
                Style.Text(new Rect(v.x + P(10), v.y + P(4), v.width - P(20), P(60)), Lang.T("En partie seulement : la course sur glace se pr\u00E9pare dans le jeu.", "In game only: the ice race is prepared in the game."),
                           13, TextAnchor.UpperLeft, Kit.Muted, false, true);
                return false;
            }
            float now = Time.realtimeSinceStartup;
            float cardW = P(236), gap = P(16);
            var left = new Rect(v.x, v.y, v.width - cardW - gap, v.height);
            var card = new Rect(left.xMax + gap, v.y, cardW, v.height);
            bool click = false;
            // pastilles des modeles
            IList<Jokkis.Def> all = Jokkis.All();
            int free = 0;
            foreach (Jokkis.Def d in all) if (Jokkis.IsFree(d.Number)) free++;
            float chx = left.x, chy = left.y, chh = P(28);
            for (int f = 0; f <= Models.Length; f++)
            {
                if (f > 0) { bool any = false; foreach (Jokkis.Def d in all) if (d.Model == Models[f - 1]) any = true; if (!any) continue; }
                string lab = f == 0 ? Lang.T("Toutes", "All") + "  \u00B7  " + free + Lang.T(" libres", " free") : Models[f - 1];
                float w = Style.Width(lab, 12) + P(22);
                if (chx + w > left.xMax && chx > left.x) { chx = left.x; chy += chh + P(6); }   // (pastilles sur deux rangees au besoin)
                var cr = new Rect(chx, chy, w, chh);
                chx += w + P(6);
                bool on = f == filter, over = cr.Contains(e.mousePosition);
                if (e.type == EventType.MouseDown && e.button == 0 && over) { SetFilter(f); click = true; e.Use(); }
                if (e.type != EventType.Repaint) continue;
                Style.Round(cr, chh / 2, on ? Kit.A(Kit.Ice, 0.18f) : Kit.A(Color.white, over ? 0.09f : 0.05f));
                Style.Ring(cr, chh / 2, on ? Kit.A(Kit.Ice, 0.55f) : Kit.Edge);
                Style.Text(cr, lab, 12, TextAnchor.MiddleCenter, on ? Kit.Ice : Kit.Soft);
            }
            // liste
            float lt = chy + chh + P(10);
            var list = new Rect(left.x, lt, left.width, left.yMax - lt);
            if (!Jokkis.Enabled && (!Session.Active || Session.IsHost))
            {
                var nr = new Rect(list.x, list.y, list.width, P(40));
                Style.Text(nr, Lang.T("Location coup\u00E9e dans cette partie : lanceur, R\u00E9glages \u203A Co-op \u203A Course sur glace (b\u00EAta).", "Rental is off in this game: launcher, Settings \u203A Co-op \u203A Ice race (beta)."),
                           12, TextAnchor.UpperLeft, Kit.Amber, false, true);
                list.y += P(44); list.height -= P(44);
            }
            listH = list.height;
            if (e.type == EventType.ScrollWheel && list.Contains(e.mousePosition)) { scroll += e.delta.y * P(14); e.Use(); }
            scroll = Mathf.Clamp(scroll, 0f, Mathf.Max(0f, contentH - listH));
            if (DrawList(list, e, focused, moved)) click = true;
            if (contentH > listH)
            {
                var track = new Rect(list.xMax - P(4), list.y, Mathf.Max(3f, P(4)), listH);
                Style.Round(track, track.width / 2, Kit.Sep);
                float thumb = Mathf.Max(P(28), listH * listH / contentH);
                Style.Round(new Rect(track.x, track.y + (listH - thumb) * scroll / (contentH - listH), track.width, thumb), track.width / 2, Kit.A(Kit.Ice, 0.8f));
            }
            // fiche
            var pvr = new Rect(card.x, card.y, card.width, Mathf.Round(card.width * RtH / (float)RtW));
            DrawPreview(pvr, e, now);
            DrawCard(new Rect(card.x, pvr.yMax + P(10), card.width, card.yMax - pvr.yMax - P(10)), e);
            return click;
        }

        // Liste : la Rivett (information), puis les Jokkis par modele (filtre).
        static bool DrawList(Rect view, Event e, bool focused, bool moved)
        {
            bool click = false;
            GUI.BeginGroup(view);
            float cw = view.width - P(10), cy = 0f, headH = P(26);
            rowH = P(32);
            int n = 0;
            order.Clear();
            hiY = -1f;
            if (filter == 0)
            {
                var rr = new Rect(0, cy - scroll, cw, P(30));
                if (Visible(rr, view.height) && e.type == EventType.Repaint)
                {
                    Style.Round(rr, P(8), Kit.Tint);
                    NumberPill(new Rect(rr.x + P(8), rr.center.y - P(10), P(46), P(20)), 14, Kit.Muted);
                    Style.Text(new Rect(rr.x + P(62), rr.y, cw - P(70), rr.height), Lang.T("Rivett n\u00B014 : la voiture du jeu, gratuite aussi", "Rivett no. 14: the game's car, free as well"), 12, TextAnchor.MiddleLeft, Kit.Muted, false);
                }
                cy += rr.height + P(4); n++;
            }
            IList<Jokkis.Def> all = Jokkis.All();
            for (int g = 0; g <= Models.Length; g++)
            {
                if (filter != 0 && g != filter - 1) continue;
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
                            Kit.Section(new Rect(hr.x + P(6), hr.y, hr.width - P(12), hr.height - P(4)), g < Models.Length ? Models[g].ToUpperInvariant() : Lang.T("AUTRES", "OTHERS"));
                        cy += headH; n++;
                    }
                    order.Add(d);
                    var r = new Rect(0, cy - scroll, cw, rowH);
                    cy += rowH + P(3); n++;
                    bool vis = Visible(r, view.height), selected = d.Number == hi;
                    if (selected) hiY = r.y + scroll;
                    if (vis && moved && r.Contains(e.mousePosition)) hi = d.Number;
                    if (vis && e.type == EventType.MouseDown && e.button == 0 && r.Contains(e.mousePosition))
                    {
                        hi = d.Number; click = true;
                        if (e.clickCount >= 2) Act(d.Number);
                        e.Use();
                    }
                    if (!vis || e.type != EventType.Repaint) continue;
                    bool isMine;
                    string owner = OwnerOf(d.Number, out isMine);
                    float rr2 = P(8);
                    if (selected) { Style.Round(r, rr2, Kit.A(Kit.Ice, isMine ? 0.16f : 0.12f)); Style.Ring(r, rr2, Kit.A(Kit.Ice, focused ? 0.65f : 0.35f)); }
                    else if (isMine) { Style.Round(r, rr2, Kit.A(Kit.Ice, 0.08f)); Style.Ring(r, rr2, Kit.A(Kit.Ice, 0.30f)); }
                    NumberPill(new Rect(r.x + P(8), r.center.y - P(10), P(46), P(20)), d.Number, isMine ? Kit.Ice : Kit.Soft);
                    float tx = r.x + P(62), stw = P(118);
                    Style.Text(new Rect(tx, r.y, (r.width - tx - stw) * 0.6f, rowH), d.Driver, 14, TextAnchor.MiddleLeft, Kit.Ink);
                    Style.Text(new Rect(tx + (r.width - tx - stw) * 0.6f, r.y, (r.width - tx - stw) * 0.4f, rowH), d.Club, 12, TextAnchor.MiddleLeft, Kit.Muted, false);
                    string st = isMine ? Lang.T("la v\u00F4tre", "yours") : owner == null ? Lang.T("libre", "free") : Lang.T("prise par ", "taken by ") + owner;
                    Style.Text(new Rect(r.xMax - stw - P(8), r.y, stw, rowH), st, 12, TextAnchor.MiddleRight, isMine ? Kit.Ice : owner == null ? Kit.Green : Kit.Amber);
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
            return click;
        }

        static bool Visible(Rect r, float viewH) { return r.yMax > 0 && r.y < viewH; }

        static void NumberPill(Rect r, int number, Color c)
        {
            Style.Ring(r, P(6), Kit.A(c, 0.45f));
            Style.Text(r, "n\u00B0 " + number, 11, TextAnchor.MiddleCenter, c);
        }

        // La voiture en 3D : texture du studio dans un cadre arrondi ; glisser pour la tourner.
        static void DrawPreview(Rect r, Event e, float now)
        {
            if (e.type == EventType.MouseDown && e.button == 0 && r.Contains(e.mousePosition)) { dragging = true; e.Use(); }
            else if (e.type == EventType.MouseDrag && dragging) { pvYaw += e.delta.x * 0.6f; dragAt = now; e.Use(); }
            else if (e.rawType == EventType.MouseUp && dragging) dragging = false;
            if (e.type != EventType.Repaint) return;
            float rad = P(12);
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
            else if (tex == null) Style.Text(r, pv == null && hi != 0 && pvNumber == hi ? Lang.T("Aper\u00E7u indisponible", "Preview unavailable") : "\u2026", 13, TextAnchor.MiddleCenter, Kit.Muted, false);
            Style.Ring(r, rad, Kit.Sep);
        }

        // Fiche de la voiture en surbrillance : numero et pilote, modele et club, etat, bouton ; puis l'equipe.
        static void DrawCard(Rect r, Event e)
        {
            Jokkis.Def d = Jokkis.DefOf(hi);
            float cy = r.y;
            if (d != null)
            {
                bool isMine;
                string owner = OwnerOf(d.Number, out isMine);
                int mine = Jokkis.ChosenNumber(Session.LocalId);
                Style.Title(new Rect(r.x, cy, r.width, P(26)), "N\u00B0 " + d.Number + "  \u00B7  " + d.Driver, Kit.Ink, TextAnchor.MiddleLeft, 17);
                cy += P(26);
                Style.Text(new Rect(r.x, cy, r.width, P(18)), d.Model + "  \u00B7  " + d.Club, 12, TextAnchor.MiddleLeft, Kit.Muted, false);
                cy += P(24);
                string st = isMine ? Lang.T("La v\u00F4tre cette semaine", "Yours this week") : owner == null ? Lang.T("Libre", "Free") : Lang.T("Prise par ", "Taken by ") + owner;
                Kit.Pill(r.x, cy + P(9), st, isMine ? Kit.Ice : owner == null ? Kit.Green : Kit.Amber);
                cy += P(26);
                var br = new Rect(r.x, cy, r.width, P(34));
                if (isMine) { if (Kit.Button(br, Lang.T("Rendre la n\u00B0 ", "Return no. ") + d.Number, 0, false)) Act(d.Number); }
                else if (owner == null) { if (Kit.Button(br, Lang.T("Choisir la n\u00B0 ", "Choose no. ") + d.Number, 1, false, mine == 0 && pending == 0)) Act(d.Number); }
                else Kit.Button(br, Lang.T("D\u00E9j\u00E0 prise", "Already taken"), 0, false, false);
                cy += P(40);
                string line = note != null && Time.realtimeSinceStartup < noteUntil ? note
                            : mine != 0 && !isMine && owner == null ? Lang.T("Rendez d'abord la n\u00B0 ", "Give back no. ") + mine + Lang.T("", " first") : null;
                if (line != null) Style.Text(new Rect(r.x, cy, r.width, P(32)), line, 12, TextAnchor.UpperLeft, note != null && noteBad ? Kit.Amber : note != null ? Kit.Green : Kit.Muted, false, true);
                cy += P(34);
            }
            if (!Session.Active || cy + P(50) > r.yMax) return;
            Style.Fill(new Rect(r.x, cy, r.width, Mathf.Max(1f, P(1))), Kit.Sep);
            cy += P(6);
            Kit.Section(new Rect(r.x, cy, r.width, P(18)), Lang.T("L'\u00C9QUIPE", "THE TEAM"));
            cy += P(22);
            foreach (PlayerInfo pi in Session.Players.Values)
            {
                if (cy + P(22) > r.yMax) break;
                float av = P(20);
                Kit.Avatar(new Rect(r.x, cy + P(1), av, av), pi.Id, pi.Name, pi.SteamId);
                Style.Text(new Rect(r.x + av + P(8), cy, r.width - av - P(8), P(22)), pi.Name, 13, TextAnchor.MiddleLeft, Kit.Soft);
                int n = Jokkis.ChosenNumber(pi.Id);
                Style.Text(new Rect(r.x, cy, r.width, P(22)), n != 0 ? "n\u00B0 " + n : Lang.T("pas encore", "not yet"), 12, TextAnchor.MiddleRight, n != 0 ? Kit.Ice : Kit.Faint, false);
                cy += P(24);
            }
        }

        // ---------------------------------------------------------------- autotest "jokkispanel"
        // [Test] Autotest=jokkispanel : a 25 s l'hote ouvre l'onglet (capture jokkis-panneau a 27 s) ; a 30 s il note le nombre de
        // lignes, la voiture en surbrillance et l'apercu (objet, texture rendue ? taille, rendus) ; a 35 s il ferme le menu, bilan a 36 s.
        static int testStep;
        public static void Test(string mode, float t)
        {
            if (mode != "jokkispanel" || !Session.IsHost) return;
            if (testStep == 0 && t > 25f) { testStep = 1; Open = true; Autotest.CaptureSoon("jokkis-panneau", 2f); Log.Info("autotest jokkispanel : onglet ouvert"); }
            if (testStep == 1 && t > 30f)
            {
                testStep = 2;
                Jokkis.Def d = Jokkis.DefOf(hi);
                Log.Info("autotest jokkispanel : " + rows + " lignes (" + order.Count + " jokkis), surbrillance n " + hi + (d != null ? " (" + d.Driver + ", " + d.Model + ")" : " (?)")
                         + ", apercu " + (pv != null ? pv.GetComponentsInChildren<MeshRenderer>(true).Length + " maillages" : "sans objet")
                         + ", texture " + (rtReady && rt != null ? "rendue " + rt.width + "x" + rt.height + ", " + renders + " rendus" : "JAMAIS rendue") + ", affiche " + shown);
            }
            if (testStep == 2 && t > 35f) { testStep = 3; Menu.Hide(); }
            if (testStep == 3 && t > 36f) { testStep = 4; Log.Info("autotest jokkispanel : menu ferme, apercu " + (pv == null ? "detruit" : "ENCORE LA") + ", texture " + (rt == null ? "liberee" : "ENCORE LA")); }
        }
    }
}
