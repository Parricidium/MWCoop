using MWCoop.Net;
using UnityEngine;

namespace MWCoop
{
    // Onglet VAZ-2108 du F10 (18/10) : les 16 couleurs en grille (pastille de couleur, numero, libre / prise par <joueur> / la
    // votre), a droite la VAZ de la couleur en surbrillance en 3D (meme studio que les Jokkis : copie posee loin sous le monde,
    // photographiee dans une RenderTexture tant que l'onglet est affiche), sa fiche et les boutons PRENDRE, FAIRE VENIR et
    // RENDRE. Clavier (Menu.Key le passe ici) : fleches dans la grille (Haut sur la premiere rangee : retour aux onglets),
    // Entree prendre / rendre, V faire venir ; double clic sur une case : prendre.
    // Evo III (extension privee MWCoop/evo3, JD 20/10) : choix du modele sur la fiche (VAZ-2108 / Evo III, touche M), seulement
    // si l'extension est installee ; l'apercu montre le modele (et la livree) du joueur qui a la couleur, sinon le choix d'ici.
    public static class VazPanel
    {
        static int pick = -1;   // modele choisi ici pour la prochaine prise ([Coop] VoitureModele)
        static int Pick
        {
            get { if (pick < 0) pick = Config.GetInt("Coop", "VoitureModele", 0); return VazCar.Has(pick) ? pick : VazCar.ModelVaz; }
            set { pick = value; Config.Save("Coop", "VoitureModele", value.ToString()); }
        }
        static int pvModel = -1; static string pvLivery = "";
        // filtre des modeles par categorie (Gr. A, Gr. B... -- JD, 23/10) : "" = tous ([Coop] VoitureFiltre)
        static string filter;
        static string Filter
        {
            get { if (filter == null) filter = Config.Get("Coop", "VoitureFiltre", ""); return filter; }
            set { filter = value ?? ""; Config.Save("Coop", "VoitureFiltre", filter); }
        }
        static bool InFilter(int m) { return Filter.Length == 0 || VazCar.ModelOf(m).Group == Filter; }
        // categories des modeles installes, dans l'ordre : MWCoop (la VAZ) d'abord, puis par nom
        static System.Collections.Generic.List<string> Groups()
        {
            var g = new System.Collections.Generic.List<string>();
            for (int i = 0; i < VazCar.Models.Length; i++) if (VazCar.Has(i) && !g.Contains(VazCar.ModelOf(i).Group)) g.Add(VazCar.ModelOf(i).Group);
            g.Sort((a, b) => a == "MWCoop" ? -1 : b == "MWCoop" ? 1 : string.CompareOrdinal(a, b));
            return g;
        }
        // modele et livree a montrer pour la couleur n
        static void Shown(int n, out int model, out string livery)
        {
            int id = Vaz.OwnerOf(n);
            model = id >= 0 ? Vaz.ModelOf(id) : Pick;
            livery = id >= 0 ? Vaz.LiveryOf(id) : EvoLivery.MyHash(model);
            if (!VazCar.Has(model)) { model = VazCar.ModelVaz; livery = ""; }
        }
        static bool shown, hooked;
        static int hi = 1;
        static int pending; static float pendingAt;
        static string note; static float noteUntil; static bool noteBad;
        const int Cols = 4;

        const int RtW = 512, RtH = 320;
        const float Fov = 24f, Elev = 17f;
        static readonly Color Bg = new Color(0.07f, 0.11f, 0.18f, 1f);
        static readonly Color PreviewBg = new Color(0f, 0.02f, 0.06f, 0.30f);
        static GameObject pv; static VazCar.Built pvHold; static int pvNumber; static float pvYaw, pvT, pvRetryAt, dragAt;
        static Vector3 pvCenter; static float pvRadius, pvHalfH, pvDist;
        static RenderTexture rt; static bool rtReady, dragging;
        static int renders;

        public static string Hint
        {
            get
            {
                if (note != null && Time.realtimeSinceStartup < noteUntil) return note;
                if (Vaz.EvoAvailable)
                    return Lang.T("Fl\u00E8ches : couleur  \u00B7  Entr\u00E9e : prendre ou rendre  \u00B7  V : faire venir  \u00B7  M : mod\u00E8le  \u00B7  glisser l'aper\u00E7u : tourner",
                                  "Arrows: colour  \u00B7  Enter: take or give back  \u00B7  V: bring it here  \u00B7  M: model  \u00B7  drag the preview: turn");
                return Lang.T("Fl\u00E8ches : couleur  \u00B7  Entr\u00E9e : prendre ou rendre  \u00B7  V : faire venir  \u00B7  glisser l'aper\u00E7u : tourner",
                              "Arrows: colour  \u00B7  Enter: take or give back  \u00B7  V: bring it here  \u00B7  drag the preview: turn");
            }
        }

        public static void Update()
        {
            bool now = PlayerSync.InGame && Menu.Open && Menu.Tab == Menu.TabVaz;
            if (now != shown) { shown = now; if (now) Opened(); else Closed(); }
            if (!shown) return;
            if (pending != 0 && Time.realtimeSinceStartup - pendingAt > 6f) pending = 0;
            if (Vaz.Enabled) Preview();   // (coupee : pas d'apercu 3D -- son modele chargeait ~1 s a chaque ouverture de l'onglet, retour de Menor 25/10)
        }

        static void Opened()
        {
            note = null; pending = 0; dragging = false;
            if (!hooked) { hooked = true; Vaz.Changed += OnChanged; }
            if (Vaz.Mine != 0) hi = Vaz.Mine;
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
            if (pv != null) pv.SetActive(false);   // (garde : rebatir le modele a chaque ouverture coutait ~1 s)
            if (rt != null) { rt.Release(); Object.Destroy(rt); rt = null; }
            rtReady = false; dragging = false;
        }

        static void OnChanged()
        {
            if (!shown || pending == 0) return;
            if (Vaz.Mine == pending) { Note(Lang.T("La VAZ n\u00B0 ", "VAZ no. ") + pending + Lang.T(" est \u00E0 vous, \u00E0 c\u00F4t\u00E9 de vous", " is yours, right next to you"), false); pending = 0; }
            else if (!Vaz.IsFree(pending)) { Note(Lang.T("La n\u00B0 ", "No. ") + pending + Lang.T(" vient d'\u00EAtre prise", " was just taken"), true); pending = 0; }
        }

        static void Note(string s, bool bad) { note = s; noteBad = bad; noteUntil = Time.realtimeSinceStartup + 4f; }

        static string OwnerName(int n, out bool mine)
        {
            int id = Vaz.OwnerOf(n);
            mine = id >= 0 && Vaz.Mine == n;
            if (id < 0) return null;
            if (mine) return Session.Me != null ? Session.Me.Name : "";
            PlayerInfo pi;
            return Session.Players.TryGetValue(id, out pi) ? pi.Name : "?";
        }

        static void Act(int n)
        {
            if (Vaz.Mine == n) { Vaz.Release(); Note(Lang.T("VAZ n\u00B0 ", "VAZ no. ") + n + Lang.T(" rendue", " given back"), false); return; }
            if (!Vaz.Enabled) { Note(Lang.T("VAZ-2108 coup\u00E9e dans cette partie (lanceur, Ajouts)", "VAZ-2108 is off in this game (launcher, Add-ons)"), true); return; }
            if (Vaz.Mine != 0) { Note(Lang.T("Rendez d'abord la n\u00B0 ", "Give back no. ") + Vaz.Mine + Lang.T("", " first"), true); return; }
            if (!Vaz.IsFree(n)) { Note(Lang.T("La n\u00B0 ", "No. ") + n + Lang.T(" est d\u00E9j\u00E0 prise", " is already taken"), true); return; }
            pending = n; pendingAt = Time.realtimeSinceStartup;
            if (!Vaz.Request(n, Pick)) { pending = 0; return; }
            if (Session.Active && !Session.IsHost) Note(Lang.T("Demande envoy\u00E9e \u00E0 l'h\u00F4te\u2026", "Request sent to the host\u2026"), false);
        }

        static void CallMine()
        {
            if (Vaz.Mine == 0) return;
            Vaz.Call();
            Note(Lang.T("La VAZ arrive \u00E0 c\u00F4t\u00E9 de vous", "The VAZ is coming next to you"), false);
        }

        // ---------------------------------------------------------------- apercu 3D
        static void DestroyPreview() { if (pvHold != null) VazCar.DestroyPreview(pvHold); else if (pv != null) Object.Destroy(pv); pv = null; pvHold = null; }

        static void Preview()
        {
            float now = Time.realtimeSinceStartup;
            int sm; string sl;
            Shown(hi, out sm, out sl);
            if (pv != null && !pv.activeSelf) pv.SetActive(true);
            if (pv == null || pvNumber != hi || pvModel != sm)
            {
                DestroyPreview();
                if (pvNumber == hi && pvModel == sm && now < pvRetryAt) return;
                pvNumber = hi; pvModel = sm; pvLivery = null;
                try { pv = VazCar.BuildPreview(hi, Vaz.ColorOf(hi), Studio.PreviewLayer, out pvHold, VazCar.ModelOf(sm)); }
                catch (System.Exception e) { Log.Warn("vaz (apercu) : n " + hi + " : " + e.Message); pv = null; }
                if (pv == null) { pvRetryAt = now + 2f; return; }
                Measure();
                pvYaw = 35f; pvT = now;
            }
            if (pvHold != null && sm != VazCar.ModelVaz && pvLivery != sl)
            {
                Texture2D lt = sl.Length > 0 ? EvoLivery.Get(sl) : null;
                if (sl.Length == 0 || lt != null) { VazCar.SetLivery(pvHold, lt, sl.Length > 0 ? sl : null); pvLivery = sl; }
            }
            float dt = Mathf.Clamp(now - pvT, 0f, 0.1f);
            pvT = now;
            if (now - dragAt > 1.5f) pvYaw += 18f * dt;
            Quaternion r = Quaternion.Euler(0f, pvYaw, 0f);
            pv.transform.rotation = r;
            pv.transform.position = Studio.OtherPivot - r * pvCenter;
            Vector3 pos = Studio.OtherPivot + new Vector3(0f, Mathf.Sin(Elev * Mathf.Deg2Rad) * pvDist, Mathf.Cos(Elev * Mathf.Deg2Rad) * pvDist);
            if (Studio.RenderPreview(pos, Studio.OtherPivot, Fov, pvDist - pvRadius - 1f, pvDist + pvRadius + 1f, rt, Bg)) { rtReady = true; renders++; }
        }

        static void Measure()
        {
            Bounds b = new Bounds(); bool any = false;
            foreach (MeshFilter mf in pv.GetComponentsInChildren<MeshFilter>(true))
            {
                Mesh m = mf.sharedMesh;
                if (m == null) continue;
                Matrix4x4 mx = mf.transform.localToWorldMatrix;
                Bounds mb = m.bounds;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 w = mx.MultiplyPoint3x4(mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) != 0 ? 1f : -1f, (i & 2) != 0 ? 1f : -1f, (i & 4) != 0 ? 1f : -1f)));
                    if (!any) { b = new Bounds(w, Vector3.zero); any = true; } else b.Encapsulate(w);
                }
            }
            if (!any) { pvCenter = Vector3.zero; pvRadius = 2.2f; pvHalfH = 0.7f; }
            else { pvCenter = b.center; pvRadius = Mathf.Max(0.5f, new Vector2(b.size.x, b.size.z).magnitude / 2f); pvHalfH = Mathf.Max(0.3f, b.size.y / 2f); }
            float tanH = Mathf.Tan(Fov * 0.5f * Mathf.Deg2Rad), aspect = RtW / (float)RtH;
            float dh = pvRadius / (tanH * aspect), dv = (pvHalfH + pvRadius * Mathf.Sin(Elev * Mathf.Deg2Rad)) / tanH;
            pvDist = Mathf.Max(dh, dv) * 1.12f + pvRadius * 0.15f;
        }

        // ---------------------------------------------------------------- clavier
        public static bool Key(KeyCode k)
        {
            switch (k)
            {
                case KeyCode.UpArrow: if (hi <= Cols) return false; hi -= Cols; return true;
                case KeyCode.DownArrow: if (hi + Cols <= Vaz.Count) hi += Cols; return true;
                case KeyCode.LeftArrow: if (hi > 1) hi--; return true;
                case KeyCode.RightArrow: if (hi < Vaz.Count) hi++; return true;
                case KeyCode.V: CallMine(); return true;
                case KeyCode.M:
                    if (!Vaz.EvoAvailable) return false;
                    { int m = Pick, n = 0; do { m = (m + 1) % VazCar.Models.Length; n++; } while ((!VazCar.Has(m) || !InFilter(m)) && n <= VazCar.Models.Length); if (VazCar.Has(m)) Pick = m; }   // (modele suivant installe, dans le filtre)
                    return true;
                case KeyCode.Return:
                case KeyCode.KeypadEnter: Act(hi); return true;
            }
            return false;
        }

        static float P(float v) { return Style.Px(v); }

        // ---------------------------------------------------------------- dessin
        public static bool DrawTab(Rect v, Event e, bool focused, bool moved)
        {
            if (!PlayerSync.InGame)
            {
                Style.Text(new Rect(v.x + P(10), v.y + P(4), v.width - P(20), P(60)), Lang.T("En partie seulement.", "In game only."), 13, TextAnchor.UpperLeft, Kit.Muted, false, true);
                return false;
            }
            float now = Time.realtimeSinceStartup;
            float cardW = P(236), gap = P(16);
            var left = new Rect(v.x, v.y, v.width - cardW - gap, v.height);
            var card = new Rect(left.xMax + gap, v.y, cardW, v.height);
            bool click = false;
            float top = left.y;
            if (!Vaz.Enabled)
            {
                Style.Text(new Rect(left.x, top, left.width, P(40)), VazCar.HasFiles ? Lang.T("VAZ-2108 coup\u00E9e dans cette partie : lanceur, R\u00E9glages \u203A Co-op \u203A Ajouts.", "VAZ-2108 is off in this game: launcher, Settings \u203A Co-op \u203A Add-ons.")
                                                                                     : Lang.T("Fichiers de la VAZ absents (MWCoop\\vaz) : r\u00E9installez MWCoop.", "VAZ files missing (MWCoop\\vaz): reinstall MWCoop."),
                           12, TextAnchor.UpperLeft, Kit.Amber, false, true);
                top += P(44);
            }
            // grille 4 x 4
            float cg = P(8), cw = (left.width - cg * (Cols - 1)) / Cols, ch = Mathf.Min(P(66), (left.yMax - top - cg * 3) / 4f);
            for (int n = 1; n <= Vaz.Count; n++)
            {
                int i = n - 1;
                var r = new Rect(left.x + (i % Cols) * (cw + cg), top + (i / Cols) * (ch + cg), cw, ch);
                bool over = r.Contains(e.mousePosition), sel = n == hi;
                if (moved && over) hi = n;
                if (e.type == EventType.MouseDown && e.button == 0 && over)
                {
                    hi = n; click = true;
                    if (e.clickCount >= 2) Act(n);
                    e.Use();
                }
                if (e.type != EventType.Repaint) continue;
                bool mine;
                string owner = OwnerName(n, out mine);
                float rad = P(10);
                Style.Round(r, rad, sel ? Kit.A(Kit.Ice, 0.14f) : mine ? Kit.A(Kit.Ice, 0.08f) : Kit.A(Color.white, over ? 0.07f : 0.04f));
                Style.Ring(r, rad, sel ? Kit.A(Kit.Ice, focused ? 0.7f : 0.4f) : mine ? Kit.A(Kit.Ice, 0.35f) : Kit.Edge);
                float sw = Mathf.Min(P(30), ch - P(16));
                var swr = new Rect(r.x + P(10), r.center.y - sw / 2, sw, sw);
                Style.Round(swr, sw / 2, Vaz.ColorOf(n));
                Style.Ring(swr, sw / 2, Kit.A(Color.white, 0.35f));
                float tx = swr.xMax + P(8);
                Style.Text(new Rect(tx, r.y + P(6), r.xMax - tx - P(6), P(20)), "n\u00B0 " + n, 13, TextAnchor.MiddleLeft, Kit.Ink);   // (nom de la couleur : sur la fiche)
                string st = mine ? Lang.T("la v\u00F4tre", "yours") : owner == null ? Lang.T("libre", "free") : owner;
                Style.Text(new Rect(tx, r.yMax - P(24), r.xMax - tx - P(6), P(18)), st, 11, TextAnchor.MiddleLeft, mine ? Kit.Ice : owner == null ? Kit.Green : Kit.Amber, false);
            }
            var pvr = new Rect(card.x, card.y, card.width, Mathf.Round(card.width * RtH / (float)RtW));
            DrawPreview(pvr, e, now);
            DrawCard(new Rect(card.x, pvr.yMax + P(10), card.width, card.yMax - pvr.yMax - P(10)), e);
            return click;
        }

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
                float ins = Mathf.Round(rad * 0.3f);
                Color oc = GUI.color;
                GUI.color = new Color(1f, 1f, 1f, Style.Alpha);
                GUI.DrawTexture(new Rect(r.x + ins, r.y + ins, r.width - 2 * ins, r.height - 2 * ins), tex, ScaleMode.StretchToFill, false);
                GUI.color = oc;
            }
            else if (tex == null) Style.Text(r, pv == null && pvNumber == hi && Time.realtimeSinceStartup < pvRetryAt ? Lang.T("Aper\u00E7u indisponible", "Preview unavailable") : "\u2026", 13, TextAnchor.MiddleCenter, Kit.Muted, false);
            Style.Ring(r, rad, Kit.Sep);
        }

        static void DrawCard(Rect r, Event e)
        {
            float cy = r.y;
            bool mine;
            string owner = OwnerName(hi, out mine);
            int sm; string sl;
            Shown(hi, out sm, out sl);
            bool evo = sm != VazCar.ModelVaz;
            Style.Title(new Rect(r.x, cy, r.width, P(26)), VazCar.ModelOf(sm).Name + "  n\u00B0 " + hi, Kit.Ink, TextAnchor.MiddleLeft, 17);
            cy += P(26);
            string desc = evo ? VazCar.ModelOf(sm).Desc : Game.Msc ? Lang.T("r\u00E9glages de la Satsuma rallye", "set up like the rally Satsuma") : Lang.T("r\u00E9glages de la Rivett rallye", "set up like the rally Rivett");
            if (Style.Width(Vaz.ColorName(hi) + "  \u00B7  " + desc, 12, false) <= r.width)
                Style.Text(new Rect(r.x, cy, r.width, P(18)), Vaz.ColorName(hi) + "  \u00B7  " + desc, 12, TextAnchor.MiddleLeft, Kit.Muted, false);
            else
            {
                // (fiche d'addon longue : la couleur, puis la fiche sur sa ligne)
                Style.Text(new Rect(r.x, cy, r.width, P(18)), Vaz.ColorName(hi), 12, TextAnchor.MiddleLeft, Kit.Muted, false);
                cy += P(16);
                Style.Text(new Rect(r.x, cy, r.width, P(18)), desc, 12, TextAnchor.MiddleLeft, Kit.Muted, false);
            }
            cy += P(24);
            if (Vaz.EvoAvailable && owner == null)
            {
                // choix du modele (extensions installees) : un bouton par modele, a la largeur de son nom, en lignes (retour a
                // la ligne quand la place manque ; chaque ligne remplie sur toute la largeur) -- JD 21/10, quatre voitures : les
                // noms se chevauchaient dans quatre boutons de meme largeur
                // filtre par categorie (au-dessus des modeles, seulement s'il y a plusieurs categories) : Tous, MWCoop, Gr. A...
                System.Collections.Generic.List<string> gs = Groups();
                if (gs.Count > 1)
                {
                    if (Filter.Length > 0 && !gs.Contains(Filter)) Filter = "";
                    var labels = new System.Collections.Generic.List<string> { Lang.T("Tous", "All") };
                    labels.AddRange(gs);
                    float fx = r.x, fg = P(6);
                    for (int k = 0; k < labels.Count; k++)
                    {
                        float fw = Style.Width(labels[k], 12) + P(18);
                        if (fx > r.x && fx + fw > r.xMax) { fx = r.x; cy += P(28); }
                        bool on = k == 0 ? Filter.Length == 0 : Filter == labels[k];
                        if (Kit.Button(new Rect(fx, cy, fw, P(24)), labels[k], on ? 1 : 0, false, true, null, 12)) Filter = k == 0 ? "" : labels[k];
                        fx += fw + fg;
                    }
                    cy += P(30);
                }
                var ms = new System.Collections.Generic.List<int>();
                for (int i = 0; i < VazCar.Models.Length; i++) if (VazCar.Has(i) && InFilter(i)) ms.Add(i);
                if (ms.Count > 0 && !ms.Contains(Pick)) Pick = ms[0];   // (le modele choisi est toujours un de ceux du filtre)
                float gap = P(8);
                var w = new float[ms.Count];
                for (int k = 0; k < ms.Count; k++) w[k] = Mathf.Min(r.width, Mathf.Max(P(70), Style.Width(VazCar.ModelOf(ms[k]).Name, 13) + P(26)));
                int start = 0;
                while (start < ms.Count)
                {
                    int end = start; float used = w[start];
                    while (end + 1 < ms.Count && used + gap + w[end + 1] <= r.width) { end++; used += gap + w[end]; }
                    float extra = (r.width - used) / (end - start + 1), x = r.x;
                    for (int k = start; k <= end; k++)
                    {
                        if (Kit.Button(new Rect(x, cy, w[k] + extra, P(28)), VazCar.ModelOf(ms[k]).Name, Pick == ms[k] ? 1 : 0, false)) Pick = ms[k];
                        x += w[k] + extra + gap;
                    }
                    cy += P(34);
                    start = end + 1;
                }
            }
            if (evo)
            {
                Style.Text(new Rect(r.x, cy, r.width, P(16)), sl.Length > 0 ? Lang.T("Livr\u00E9e perso", "Custom livery") : Lang.T("Couleur + num\u00E9ro (livr\u00E9e : lanceur, Addons)", "Colour + number (livery: launcher, Add-ons)"), 11, TextAnchor.MiddleLeft, Kit.Faint, false);
                cy += P(18);
            }
            string st = mine ? Lang.T("La v\u00F4tre", "Yours") : owner == null ? Lang.T("Libre", "Free") : Lang.T("Prise par ", "Taken by ") + owner;
            Kit.Pill(r.x, cy + P(9), st, mine ? Kit.Ice : owner == null ? Kit.Green : Kit.Amber);
            cy += P(26);
            var br = new Rect(r.x, cy, r.width, P(34));
            if (mine)
            {
                float hw = (r.width - P(8)) / 2;
                if (Kit.Button(new Rect(r.x, cy, hw, P(34)), Lang.T("Faire venir", "Bring here"), 1, false, true, "venir")) CallMine();
                if (Kit.Button(new Rect(r.x + hw + P(8), cy, hw, P(34)), Lang.T("Rendre", "Give back"), 0, false)) Act(hi);
            }
            else if (owner == null) { if (Kit.Button(br, Lang.T("Prendre la n\u00B0 ", "Take no. ") + hi, 1, false, Vaz.Enabled && Vaz.Mine == 0 && pending == 0)) Act(hi); }
            else Kit.Button(br, Lang.T("D\u00E9j\u00E0 prise", "Already taken"), 0, false, false);
            cy += P(40);
            string line = note != null && Time.realtimeSinceStartup < noteUntil ? note : Vaz.Mine != 0 && !mine && owner == null ? Lang.T("Rendez d'abord la n\u00B0 ", "Give back no. ") + Vaz.Mine + Lang.T("", " first") : null;
            if (line != null) Style.Text(new Rect(r.x, cy, r.width, P(32)), line, 12, TextAnchor.UpperLeft, note != null && noteBad ? Kit.Amber : note != null ? Kit.Green : Kit.Muted, false, true);
            cy += P(34);
            // (pilote « addon standalone », [Test] AddonStandalone=1 sur My Summer Car : la R5 Maxi construite par notre code, a cote de
            // la copie ; essai solo, rien chez les autres joueurs pour l'instant)
            if (StandaloneCar.On && sm == VazCar.ModelR5)
            {
                if (Kit.Button(new Rect(r.x, cy, r.width, P(28)), StandaloneCar.Spawned ? Lang.T("Retirer la R5 standalone", "Remove the standalone R5") : VazCar.R5.Name + " (standalone)", 0, false)) StandaloneCar.Toggle();
                cy += P(34);
            }
            if (cy + P(16) > r.yMax) return;
            if (!evo) Style.Text(new Rect(r.x, r.yMax - P(16), r.width, P(14)), Lang.T("Mod\u00E8le : matthew0451, CC BY-NC 4.0", "Model: matthew0451, CC BY-NC 4.0"), 10, TextAnchor.LowerLeft, Kit.Faint, false);
        }

        // ---------------------------------------------------------------- autotest "vazpanel"
        // 25 s : l'hote ouvre l'onglet (capture vaz-panneau a 28 s) ; 32 s : bilan (apercu, texture) ; 35 s : ferme.
        static int testStep;
        public static void Test(string mode, float t)
        {
            if (mode != "vazpanel" || (Session.Active && !Session.IsHost)) return;
            float d = Config.GetFloat("Test", "VazPanelT", 25f);   // ([Test] VazPanelT : ouverture plus tard, quand l'invite a sa voiture)
            if (testStep == 0 && t > d) { testStep = 1; Menu.Show(Menu.TabVaz); Autotest.CaptureSoon("vaz-panneau", 3f); Log.Info("autotest vazpanel : onglet ouvert"); }
            if (testStep == 1 && t > d + 7f)
            {
                testStep = 2;
                Log.Info("autotest vazpanel : surbrillance n " + hi + ", apercu " + (pv != null ? pv.GetComponentsInChildren<MeshRenderer>(true).Length + " maillages" : "sans objet")
                         + ", texture " + (rtReady && rt != null ? "rendue, " + renders + " rendus" : "JAMAIS rendue") + ", affiche " + shown);
            }
            if (testStep == 2 && t > d + 10f) { testStep = 3; Menu.Hide(); }
        }
    }
}
