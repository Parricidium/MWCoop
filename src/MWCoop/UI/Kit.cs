using System.Collections.Generic;
using MWCoop.Net;
using UnityEngine;

namespace MWCoop
{
    // Briques communes du menu F10 et des petits panneaux (refonte du 18/10, maquette « Menus MWCoop en jeu ») : couleurs,
    // icones (assets/menu/make-icons.py, ressources MWCoop.menu.<nom>.png), touches en capsules, boutons compacts,
    // interrupteurs, avatars ronds, entete de panneau. Taille des menus : [Hud] Menu (85/100/120 %), posee dans Style.Scale
    // le temps du dessin (Kit.Begin / Kit.End) ; place du F10 : [Hud] MenuPosition (0 centre, 1 gauche).
    public static class Kit
    {
        // (My Summer Car : gris neutres et accent jaune au lieu du bleu, voir Style.M)
        public static readonly Color Ink = Style.M(new Color(0.93f, 0.96f, 0.98f), new Color(0.95f, 0.95f, 0.95f));
        public static readonly Color Soft = Style.M(new Color(0.86f, 0.90f, 0.95f), new Color(0.89f, 0.89f, 0.89f));
        public static readonly Color Muted = Style.M(new Color(0.62f, 0.70f, 0.78f), new Color(0.69f, 0.69f, 0.69f));
        public static readonly Color Faint = Style.M(new Color(0.51f, 0.59f, 0.68f), new Color(0.57f, 0.57f, 0.57f));
        public static readonly Color Ice = Style.M(new Color(0.65f, 0.85f, 1f), new Color(1f, 0.84f, 0.35f));
        public static readonly Color OnIce = Style.M(new Color(0.04f, 0.09f, 0.15f), new Color(0.09f, 0.08f, 0.03f));
        public static readonly Color Amber = new Color(1f, 0.71f, 0.36f);
        public static readonly Color Green = new Color(0.42f, 0.86f, 0.56f);
        public static readonly Color Red = new Color(1f, 0.54f, 0.59f);
        public static readonly Color Edge = Style.M(new Color(0.80f, 0.89f, 1f, 0.16f), new Color(1f, 1f, 1f, 0.16f));
        public static readonly Color Sep = Style.M(new Color(0.80f, 0.89f, 1f, 0.10f), new Color(1f, 1f, 1f, 0.10f));
        public static readonly Color Tint = new Color(1f, 1f, 1f, 0.05f);
        static readonly Color[] avatarColors = { Ice, new Color(1f, 0.71f, 0.36f), new Color(0.73f, 0.65f, 1f),
                                                 new Color(0.42f, 0.86f, 0.56f), new Color(1f, 0.62f, 0.77f), new Color(1f, 0.54f, 0.42f) };

        public static float MenuScale { get { return Mathf.Clamp(HudSettings.Get("Menu"), 70, 150) / 100f; } }
        public static bool MenuLeft { get { return HudSettings.Get("MenuPosition") == 1; } }

        // Le temps d'un menu : echelle choisie et fondu ; End remet tout.
        public static void Begin(float alpha) { Style.Scale = MenuScale; Style.Alpha = alpha; }
        public static void End() { Style.Scale = 1f; Style.Alpha = 1f; }

        public static float P(float v) { return Style.Px(v); }
        public static Color A(Color c, float a) { return new Color(c.r, c.g, c.b, a); }
        static bool Painting { get { return Event.current != null && Event.current.type == EventType.Repaint; } }

        // ---- icones ----
        static readonly Dictionary<string, Texture2D> icons = new Dictionary<string, Texture2D>();

        static Texture2D IconTex(string name)
        {
            Texture2D t;
            if (icons.TryGetValue(name, out t)) return t;
            t = null;
            try
            {
                using (var s = typeof(Kit).Assembly.GetManifestResourceStream("MWCoop.menu." + name + ".png"))
                    if (s != null)
                    {
                        var b = new byte[s.Length];
                        s.Read(b, 0, b.Length);
                        t = new Texture2D(2, 2, TextureFormat.ARGB32, false) { hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Clamp };
                        if (!t.LoadImage(b)) t = null;
                    }
            }
            catch (System.Exception e) { Log.Warn("menu : icone " + name + " illisible : " + e.Message); }
            if (t == null) Log.Warn("menu : icone " + name + " absente");
            icons[name] = t;
            return t;
        }

        public static void Icon(Rect r, string name, Color c)
        {
            if (!Painting || name == null) return;
            Texture2D t = IconTex(name);
            if (t == null) return;
            Color old = GUI.color;
            c.a *= Style.Alpha;
            GUI.color = c;
            GUI.DrawTexture(r, t, ScaleMode.ScaleToFit, true);
            GUI.color = old;
        }

        // ---- textes courts ----

        // Touche en capsule (bord clair) centree verticalement sur cy ; renvoie sa largeur.
        public static float Keycap(float x, float cy, string label)
        {
            float w = Style.Width(label, 11) + P(12), h = P(18);
            var r = new Rect(x, Mathf.Round(cy - h / 2), w, h);
            if (Painting)
            {
                Style.Ring(r, P(5), A(Ice, 0.30f));
                Style.Text(r, label, 11, TextAnchor.MiddleCenter, Soft);
            }
            return w;
        }

        // Capsule + libelle (pied du menu) ; renvoie le x apres.
        public static float Hint(float x, float cy, string key, string label)
        {
            x += Keycap(x, cy, key) + P(6);
            float lw = Style.Width(label, 12, false);
            if (Painting) Style.Text(new Rect(x, cy - P(10), lw + P(2), P(20)), label, 12, TextAnchor.MiddleLeft, Faint, false);
            return x + lw + P(16);
        }

        // Titre de section : petites capitales grises.
        public static void Section(Rect r, string caps)
        {
            if (Painting) Style.Text(r, caps, 11, TextAnchor.LowerLeft, Faint);
        }

        // Pastille arrondie (etat, role).
        public static float Pill(float x, float cy, string s, Color fg, float bgA = 0.14f)
        {
            float w = Style.Width(s, 11) + P(14), h = P(18);
            var r = new Rect(x, Mathf.Round(cy - h / 2), w, h);
            if (Painting)
            {
                Style.Round(r, h / 2, A(fg, bgA));
                Style.Text(r, s, 11, TextAnchor.MiddleCenter, fg);
            }
            return w;
        }

        // ---- commandes ----

        // Bouton compact : kind 0 discret, 1 accent, 2 danger. hot : survol ou choisi au clavier. Vrai au clic gauche.
        public static bool Button(Rect r, string label, int kind, bool hot, bool enabled = true, string icon = null, int size = 13)
        {
            Event e = Event.current;
            bool over = enabled && r.Contains(e.mousePosition);
            bool click = enabled && e.type == EventType.MouseDown && e.button == 0 && over;
            if (click) e.Use();
            if (!Painting) return click;
            hot = enabled && (hot || over);
            float rad = Mathf.Min(r.height / 2f, P(9));
            Color fg;
            if (!enabled) { Style.Round(r, rad, A(Color.white, 0.03f)); Style.Ring(r, rad, Sep); fg = Faint; }
            else if (kind == 1) { Style.Round(r, rad, hot ? Ice : new Color(0.55f, 0.76f, 0.95f)); fg = OnIce; }
            else if (kind == 2) { Style.Round(r, rad, A(Red, hot ? 0.22f : 0.08f)); Style.Ring(r, rad, A(Red, hot ? 0.8f : 0.4f)); fg = Red; }
            else { Style.Round(r, rad, A(Color.white, hot ? 0.12f : 0.05f)); Style.Ring(r, rad, hot ? A(Ice, 0.7f) : A(Ice, 0.18f)); fg = Soft; }
            float iw = icon != null ? P(14) : 0f, tw = label.Length > 0 ? Style.Width(label, size) : 0f, gap = icon != null && label.Length > 0 ? P(6) : 0f;
            float x0 = Mathf.Round(r.center.x - (iw + gap + tw) / 2);
            if (icon != null) Icon(new Rect(x0, r.center.y - iw / 2, iw, iw), icon, fg);
            if (tw > 0f) Style.Text(new Rect(x0 + iw + gap, r.y, tw + P(2), r.height), label, size, TextAnchor.MiddleLeft, fg);
            return click;
        }

        // Interrupteur (dessin seul).
        public static void Toggle(Rect r, bool on, bool hot)
        {
            if (!Painting) return;
            Style.Round(r, r.height / 2, on ? Ice : A(Color.white, hot ? 0.24f : 0.16f));
            float d = r.height - P(4);
            float kx = on ? r.xMax - d - P(2) : r.x + P(2);
            Style.Round(new Rect(kx, r.y + P(2), d, d), d / 2, Color.white);
        }

        // Pastilles a segments (dessin) : renvoie les rectangles pour les clics.
        public static Rect[] Segments(Rect r, string[] labels, int k, bool hot)
        {
            var rs = new Rect[labels.Length];
            float pad = P(3), x = r.x + pad;
            for (int i = 0; i < labels.Length; i++)
            {
                float w = Style.Width(labels[i], 12) + P(18);
                rs[i] = new Rect(x, r.y + pad, w, r.height - 2 * pad);
                x += w;
            }
            if (Painting)
            {
                Style.Round(r, P(9), A(Color.white, hot ? 0.08f : 0.05f));
                for (int i = 0; i < labels.Length; i++)
                {
                    if (i == k) Style.Round(rs[i], P(7), Ice);
                    Style.Text(rs[i], labels[i], 12, TextAnchor.MiddleCenter, i == k ? OnIce : Soft);
                }
            }
            return rs;
        }

        public static float SegmentsWidth(string[] labels)
        {
            float w = P(6);
            foreach (string l in labels) w += Style.Width(l, 12) + P(18);
            return w;
        }

        // Avatar rond : image Steam si elle est la, sinon l'initiale sur une couleur tiree de l'id.
        public static void Avatar(Rect r, int id, string name, ulong steamId, float alpha = 1f)
        {
            if (!Painting) return;
            Texture t = steamId != 0 ? SteamNet.Avatar(steamId) : null;
            // (image arrondie dessinee en coordonnees d'ecran : depuis un groupe defilant, on y ramene le rectangle)
            Vector2 sp = GUIUtility.GUIToScreenPoint(new Vector2(r.x, r.y));
            Rect sr = new Rect(sp.x, sp.y, r.width, r.height);
            if (t != null && Backdrop.DrawImage(sr, r.width / 2, t, Style.Alpha * alpha)) return;
            Color c = avatarColors[Mathf.Abs(id) % avatarColors.Length];
            Style.Round(r, r.width / 2, A(c, alpha));
            string ini = string.IsNullOrEmpty(name) ? "?" : name.Substring(0, 1).ToUpperInvariant();
            Style.Text(r, ini, Mathf.RoundToInt(r.height / Style.S * 0.45f), TextAnchor.MiddleCenter, A(OnIce, alpha));
        }

        // Cadre de panneau : verre et bord clair.
        public static void Frame(Rect r)
        {
            float rad = P(18);
            Style.Glass(r, rad);
            if (Painting) Style.Ring(r, rad, Edge);
        }

        // Entete de petit panneau : titre, touche, croix de fermeture (vrai au clic sur la croix).
        public static bool Header(Rect r, string title, string key)
        {
            if (Painting) Style.Title(new Rect(r.x, r.y, r.width, r.height), title, Ink, TextAnchor.MiddleLeft, 18);
            float tw = Style.TitleWidth(title, 18);
            if (key != null) Keycap(r.x + tw + P(10), r.center.y, key);
            float cs = P(26);
            return Button(new Rect(r.xMax - cs, r.center.y - cs / 2, cs, cs), "", 0, false, true, "croix");
        }
    }
}
