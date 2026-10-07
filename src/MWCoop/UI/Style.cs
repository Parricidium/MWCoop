using System.Collections.Generic;
using UnityEngine;

namespace MWCoop
{
    // Habillage commun des ecrans du mod (menu F10, tchat, ecran d'attente, messages, pseudos, mort), accorde au
    // lanceur en theme sombre : panneaux « verre » bleu nuit a coins arrondis (l'image du jeu floutee derriere, voir
    // Backdrop), bord clair fin, ombre douce, accent bleu ciel, police Segoe UI. Rectangles en pixels ecran ; tailles
    // de police donnees pour 1080 lignes et mises a l'echelle (S = Screen.height / 1080). Textures (coins arrondis,
    // anneaux, ombres, curseur) fabriquees une fois par rayon : rien d'alloue par image.
    public static class Style
    {
        public static readonly Color Accent = new Color(0.47f, 0.74f, 1f);          // bleu ciel (accent du lanceur sombre)
        public static readonly Color AccentDeep = new Color(0.20f, 0.46f, 0.78f);
        public static readonly Color OnAccent = new Color(0.04f, 0.09f, 0.16f);     // texte pose sur l'accent
        public static readonly Color White = new Color(0.94f, 0.96f, 0.99f);
        public static readonly Color Dim = new Color(0.62f, 0.69f, 0.78f);           // texte secondaire
        public static readonly Color Good = new Color(0.42f, 0.86f, 0.56f);
        public static readonly Color Warn = new Color(1f, 0.71f, 0.36f);
        public static readonly Color PanelColor = new Color(0.05f, 0.08f, 0.14f, 0.88f);   // panneau sans flou
        public static readonly Color Line = new Color(0.80f, 0.88f, 1f, 0.12f);
        static readonly Color glassTint = new Color(0.04f, 0.08f, 0.15f, 0.60f);
        static readonly Color glassEdge = new Color(0.80f, 0.89f, 1f, 0.18f);
        static readonly Color glassShine = new Color(1f, 1f, 1f, 0.05f);
        static readonly Color shadowColor = new Color(0f, 0.01f, 0.04f, 0.55f);
        static readonly Color textShadow = new Color(0f, 0.02f, 0.06f, 0.55f);

        // Fondu des ecrans (0..1) : multiplie l'opacite de tout ce qui est dessine.
        public static float Alpha = 1f;

        static Texture2D white;
        static Font titleFont, bodyFont;
        static GUIStyle titleSt, bodySt, fieldSt;
        static bool titleBold;   // pas de Segoe UI Semibold : titres en Segoe UI gras
        static readonly GUIContent content = new GUIContent();
        static readonly Dictionary<int, GUIStyle> rounds = new Dictionary<int, GUIStyle>();
        static readonly Dictionary<int, GUIStyle> rings = new Dictionary<int, GUIStyle>();
        static readonly Dictionary<int, GUIStyle> shadows = new Dictionary<int, GUIStyle>();
        static Texture2D shine, cursorTex;
        static int cursorSize;

        // Echelle de l'interface (1 en 1080 lignes, 0,67 en 720, 1,33 en 1440).
        public static float S { get { return Mathf.Max(0.6f, Screen.height / 1080f); } }
        public static float Px(float v) { return Mathf.Round(v * S); }
        static int Size(int size) { return Mathf.Max(9, Mathf.RoundToInt(size * S)); }
        static bool Painting { get { return Event.current != null && Event.current.type == EventType.Repaint; } }

        static void Init()
        {
            if (titleSt != null) return;
            white = new Texture2D(1, 1, TextureFormat.ARGB32, false);
            white.SetPixel(0, 0, Color.white);
            white.Apply();
            white.hideFlags = HideFlags.HideAndDontSave;
            string[] have = null;
            try { have = Font.GetOSInstalledFontNames(); } catch { }
            titleFont = OsFont(have, "Segoe UI Semibold", "Segoe UI", "Arial");
            titleBold = titleFont == null || titleFont.name != "Segoe UI Semibold";
            bodyFont = OsFont(have, "Segoe UI", "Tahoma", "Arial");
            Log.Info("interface : polices " + (titleFont != null ? titleFont.name : "par defaut") + " / " + (bodyFont != null ? bodyFont.name : "par defaut"));
            titleSt = Plain(titleFont ?? GUI.skin.font);
            bodySt = Plain(bodyFont ?? GUI.skin.font);
            bodySt.clipping = TextClipping.Clip;
            fieldSt = new GUIStyle(GUI.skin.textField) { font = bodyFont ?? GUI.skin.font, border = new RectOffset(0, 0, 0, 0), alignment = TextAnchor.MiddleLeft };
            fieldSt.normal.background = fieldSt.hover.background = fieldSt.focused.background = fieldSt.active.background = null;
            fieldSt.normal.textColor = fieldSt.hover.textColor = fieldSt.focused.textColor = fieldSt.active.textColor = White;
            // Reflet du verre : blanc qui s'efface du haut vers le milieu.
            shine = new Texture2D(1, 64, TextureFormat.ARGB32, false) { wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave };
            for (int y = 0; y < 64; y++) { float t = y / 63f; shine.SetPixel(0, y, new Color(1, 1, 1, Mathf.Clamp01((t - 0.55f) / 0.45f))); }
            shine.Apply();
        }

        static Font OsFont(string[] have, params string[] names)
        {
            foreach (string n in names)
            {
                if (have != null && System.Array.IndexOf(have, n) < 0) continue;
                try { Font f = Font.CreateDynamicFontFromOSFont(n, 32); if (f != null) { f.hideFlags = HideFlags.HideAndDontSave; return f; } }
                catch { }
            }
            return null;
        }

        static GUIStyle Plain(Font f)
        {
            var st = new GUIStyle(GUI.skin.label) { font = f, padding = new RectOffset(0, 0, 0, 0), margin = new RectOffset(0, 0, 0, 0), richText = false };
            st.normal.background = null;
            return st;
        }

        static Color A(Color c) { c.a *= Alpha; return c; }

        // ---- Formes ----

        // Aplat de couleur (texture blanche teintee).
        public static void Fill(Rect r, Color c)
        {
            Init();
            if (!Painting) return;
            Color old = GUI.color;
            GUI.color = A(c);
            GUI.DrawTexture(r, white);
            GUI.color = old;
        }

        // Rectangle a coins arrondis (rayon en pixels ecran, borne a la demi-hauteur : pilule).
        public static void Round(Rect r, float radius, Color c) { Sliced(rounds, 0, r, radius, c); }

        // Bord seul (1 px a l'echelle 1080) d'un rectangle arrondi.
        public static void Ring(Rect r, float radius, Color c) { Sliced(rings, 1, r, radius, c); }

        // Ombre douce sous un panneau (debord de spread pixels, un peu decalee vers le bas).
        public static void Shadow(Rect r, float radius, float spread)
        {
            int sp = Mathf.Max(2, Mathf.RoundToInt(spread));
            var big = new Rect(r.x - sp, r.y - sp + sp * 0.35f, r.width + 2 * sp, r.height + 2 * sp);
            Sliced(shadows, 2 + sp * 1000, big, radius + sp, shadowColor);
        }

        // kind : 0 plein, 1 anneau, 2+spread*1000 ombre. Le rayon est arrondi au pixel (une texture par rayon).
        static void Sliced(Dictionary<int, GUIStyle> cache, int kind, Rect r, float radius, Color c)
        {
            Init();
            if (!Painting || r.width < 1 || r.height < 1) return;
            // (au plus la demi-hauteur moins 1 : les bords decoupes font rad + 1 ; une pilule a la demi-hauteur pile faisait
            // se chevaucher ses moities haute et basse -- un trait au milieu des onglets, vu par JD le 07/10)
            int rad = Mathf.Clamp(Mathf.RoundToInt(radius), 0, Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(r.width, r.height) / 2f) - 1));
            if (rad < 1 && kind == 0) { Fill(r, c); return; }
            int ring = kind == 1 ? Mathf.Max(1, Mathf.RoundToInt(S)) : 0;
            int spread = kind >= 2 ? kind / 1000 : 0;
            int key = rad * 64 + ring + spread * 100000;
            GUIStyle st;
            if (!cache.TryGetValue(key, out st)) cache[key] = st = MakeSliced(rad, ring, spread);
            Color old = GUI.color;
            GUI.color = A(c);
            st.Draw(r, false, false, false, false);
            GUI.color = old;
        }

        // Texture n x n (n = 2*rad + 3) d'un rectangle arrondi (anticrenele), decoupee en 9 (bords = rad + 1).
        // ring > 0 : seulement une bande de cette epaisseur au bord ; spread > 0 : ombre (rad inclut le debord).
        static GUIStyle MakeSliced(int rad, int ring, int spread)
        {
            int n = 2 * rad + 3;
            var tex = new Texture2D(n, n, TextureFormat.ARGB32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.HideAndDontSave };
            float c = n / 2f, inner = c - rad;
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float dx = Mathf.Max(Mathf.Abs(x + 0.5f - c) - inner, 0f), dy = Mathf.Max(Mathf.Abs(y + 0.5f - c) - inner, 0f);
                    float d = Mathf.Sqrt(dx * dx + dy * dy) - rad;   // < 0 a l'interieur
                    float a;
                    if (spread > 0)
                    {
                        float t = Mathf.Clamp01((d + spread) / spread);   // 0 au bord du rectangle, 1 au bout du debord
                        a = (1f - t) * (1f - t);
                    }
                    else
                    {
                        a = Mathf.Clamp01(0.5f - d);
                        if (ring > 0) a *= Mathf.Clamp01(d + ring + 0.5f);
                    }
                    tex.SetPixel(x, y, new Color(1, 1, 1, a));
                }
            tex.Apply();
            var st = new GUIStyle { border = new RectOffset(rad + 1, rad + 1, rad + 1, rad + 1) };
            st.normal.background = tex;
            return st;
        }

        // Panneau verre : ombre, image du jeu floutee (si disponible), teinte bleu nuit, reflet, bord clair.
        public static void Glass(Rect r, float radius) { Glass(r, radius, true); }

        public static void Glass(Rect r, float radius, bool shadow)
        {
            Init();
            Backdrop.Want();
            if (!Painting) return;
            if (shadow) Shadow(r, radius, Px(18));
            bool blur = Backdrop.Draw(r, radius, Alpha);
            Round(r, radius, blur ? glassTint : PanelColor);
            if (r.height > Px(40))
            {
                Color old = GUI.color;
                GUI.color = A(glassShine);
                GUI.DrawTexture(new Rect(r.x + radius * 0.5f, r.y + 1, r.width - radius, Mathf.Min(r.height * 0.5f, Px(120))), shine);
                GUI.color = old;
            }
            Ring(r, radius, glassEdge);
        }

        // Ancien panneau : verre aux coins arrondis.
        public static void Panel(Rect r) { Glass(r, Px(18)); }

        // ---- Textes ----

        // Grand titre a la hauteur du rectangle.
        public static void Title(Rect r, string s) { Title(r, s, White, TextAnchor.MiddleLeft, 0); }

        // Titre (ou onglet, entete) : Segoe UI Semibold ; size = 0 : d'apres la hauteur du rectangle.
        public static void Title(Rect r, string s, Color c, TextAnchor a, int size)
        {
            Init();
            titleSt.fontSize = size > 0 ? Size(size) : Mathf.Max(9, Mathf.RoundToInt(r.height * 0.72f));
            titleSt.fontStyle = titleBold ? FontStyle.Bold : FontStyle.Normal;
            titleSt.alignment = a;
            titleSt.wordWrap = false;
            Shadowed(r, s, titleSt, c);
        }

        // Texte (Segoe UI, gras ou non), ombre discrete.
        public static void Text(Rect r, string s, int size, TextAnchor a, Color c, bool bold = true, bool wrap = false)
        {
            Init();
            Prep(size, a, bold, wrap);
            Shadowed(r, s, bodySt, c);
        }

        // Largeur et hauteur d'un texte (pour placer les fleches "< >" ou les paragraphes).
        public static float Width(string s, int size, bool bold = true)
        {
            Init();
            Prep(size, TextAnchor.MiddleLeft, bold, false);
            content.text = s;
            return bodySt.CalcSize(content).x;
        }

        public static float TitleWidth(string s, int size)
        {
            Init();
            titleSt.fontSize = Size(size);
            titleSt.fontStyle = titleBold ? FontStyle.Bold : FontStyle.Normal;
            titleSt.wordWrap = false;
            content.text = s;
            return titleSt.CalcSize(content).x;
        }

        public static float Height(string s, int size, float width, bool bold = true)
        {
            Init();
            Prep(size, TextAnchor.UpperLeft, bold, true);
            content.text = s;
            return bodySt.CalcHeight(content, width);
        }

        static void Prep(int size, TextAnchor a, bool bold, bool wrap)
        {
            bodySt.fontSize = Size(size);
            bodySt.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal;
            bodySt.alignment = a;
            bodySt.wordWrap = wrap;
        }

        static void Shadowed(Rect r, string s, GUIStyle st, Color c)
        {
            if (!Painting) return;
            float o = Mathf.Max(1f, Mathf.Round(S));
            Color sc = textShadow;
            sc.a *= c.a * Alpha;
            st.normal.textColor = sc;
            GUI.Label(new Rect(r.x, r.y + o, r.width, r.height), s, st);
            st.normal.textColor = A(c);
            GUI.Label(r, s, st);
        }

        // ---- Commandes ----

        // Jauge arrondie : fond sombre, part remplie (0..1) dans la couleur donnee.
        public static void Bar(Rect r, float frac, Color c)
        {
            float rad = r.height / 2f;
            Round(r, rad, new Color(1f, 1f, 1f, 0.10f));
            float w = Mathf.Max(r.height, r.width * Mathf.Clamp01(frac));
            if (frac > 0.001f) Round(new Rect(r.x, r.y, Mathf.Min(w, r.width), r.height), rad, c);
        }

        // Bouton arrondi ; vrai au clic gauche. primary : rempli de l'accent.
        public static bool Button(Rect r, string label, bool primary, int size = 20) { return Button(r, label, primary, size, false); }

        public static bool Button(Rect r, string label, bool primary, int size, bool selected)
        {
            Event e = Event.current;
            bool hot = r.Contains(e.mousePosition) || selected;
            bool click = false;
            if (e.type == EventType.MouseDown && e.button == 0 && r.Contains(e.mousePosition)) { click = true; e.Use(); }
            if (!Painting) return click;
            float rad = Mathf.Min(r.height / 2f, Px(14));
            if (primary)
            {
                Round(r, rad, hot ? Accent : new Color(Accent.r * 0.85f, Accent.g * 0.85f, Accent.b * 0.92f));
                Title(r, label, OnAccent, TextAnchor.MiddleCenter, size);
            }
            else
            {
                Round(r, rad, new Color(1f, 1f, 1f, hot ? 0.16f : 0.08f));
                Ring(r, rad, hot ? new Color(Accent.r, Accent.g, Accent.b, 0.8f) : glassEdge);
                Title(r, label, White, TextAnchor.MiddleCenter, size);
            }
            return click;
        }

        // Champ de saisie sans fond (a poser sur Field), police du texte a la taille donnee.
        public static GUIStyle Field(int size)
        {
            Init();
            fieldSt.fontSize = Size(size);
            fieldSt.padding.left = fieldSt.padding.right = (int)Px(12);
            fieldSt.padding.top = fieldSt.padding.bottom = 0;
            return fieldSt;
        }

        // Fond d'un champ de saisie : arrondi clair, bord accent quand il est actif.
        public static void FieldBack(Rect r, bool active)
        {
            float rad = Mathf.Min(r.height / 2f, Px(12));
            Round(r, rad, new Color(1f, 1f, 1f, active ? 0.10f : 0.06f));
            Ring(r, rad, active ? Accent : glassEdge);
        }

        // ---- Logo ----

        static Texture2D logo;
        static bool logoTried;

        // Titre « my Winter Car coop » (launcher\logo-titre.png, ressource du DLL) a gauche du rectangle, a sa hauteur ;
        // largeur dessinee (0 : pas de logo, l'appelant ecrit « MWCoop »).
        public static float Logo(Rect r)
        {
            if (!logoTried)
            {
                logoTried = true;
                try
                {
                    using (var s = typeof(Style).Assembly.GetManifestResourceStream("MWCoop.logo-titre.png"))
                        if (s != null)
                        {
                            var b = new byte[s.Length];
                            s.Read(b, 0, b.Length);
                            logo = new Texture2D(2, 2, TextureFormat.ARGB32, false) { hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Clamp };
                            if (!logo.LoadImage(b)) logo = null;
                        }
                }
                catch (System.Exception e) { Log.Warn("interface : logo illisible : " + e.Message); logo = null; }
                Log.Info("interface : logo " + (logo != null ? logo.width + "x" + logo.height : "absent"));
            }
            if (logo == null) return 0f;
            float w = Mathf.Round(r.height * logo.width / logo.height);
            if (Painting)
            {
                Color old = GUI.color;
                GUI.color = new Color(1f, 1f, 1f, Alpha);
                GUI.DrawTexture(new Rect(r.x, r.y, w, r.height), logo, ScaleMode.StretchToFill, true);
                GUI.color = old;
            }
            return w;
        }

        // ---- Curseur ----

        // Fleche dessinee par le mod (le jeu cache le curseur systeme en partie, meme libere) au point donne.
        public static void DrawCursor(Vector2 p)
        {
            if (!Painting) return;
            int n = Mathf.Max(16, Mathf.RoundToInt(26 * S));
            if (cursorTex == null || cursorSize != n) { if (cursorTex != null) Object.Destroy(cursorTex); cursorTex = MakeCursor(n); cursorSize = n; }
            Color old = GUI.color;
            GUI.color = Color.white;
            GUI.DrawTexture(new Rect(Mathf.Round(p.x) - 1, Mathf.Round(p.y) - 1, n, n), cursorTex);
            GUI.color = old;
        }

        // Fleche blanche a bord bleu nuit, anticrenelee (4 x 4 echantillons par pixel), pointe en haut a gauche.
        static Texture2D MakeCursor(int n)
        {
            var poly = new[] { new Vector2(1, 1), new Vector2(1, 19), new Vector2(5.5f, 15), new Vector2(8.5f, 22), new Vector2(11.5f, 20.8f), new Vector2(8.6f, 14), new Vector2(14.5f, 14) };
            float k = n / 24f;
            for (int i = 0; i < poly.Length; i++) poly[i] *= k;
            float border = Mathf.Max(1.2f, 1.6f * k);
            var tex = new Texture2D(n, n, TextureFormat.ARGB32, false) { wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave };
            var edge = new Color(0.04f, 0.08f, 0.15f);
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float fill = 0, ink = 0;
                    for (int sy = 0; sy < 4; sy++)
                        for (int sx = 0; sx < 4; sx++)
                        {
                            var p = new Vector2(x + (sx + 0.5f) / 4f, y + (sy + 0.5f) / 4f);
                            bool inside = Inside(poly, p);
                            float d = EdgeDist(poly, p);
                            if (inside && d > border) fill += 1f / 16f;
                            else if (inside || d <= border * 0.75f) ink += 1f / 16f;
                        }
                    float a = fill + ink;
                    Color c = a > 0 ? Color.Lerp(edge, Color.white, fill / a) : Color.clear;
                    c.a = Mathf.Clamp01(a);
                    tex.SetPixel(x, n - 1 - y, c);   // texture : y vers le haut
                }
            // Ombre portee legere (decalee de 1-2 px) sous la fleche.
            var px = tex.GetPixels();
            var outp = new Color[px.Length];
            int off = Mathf.Max(1, Mathf.RoundToInt(1.5f * k));
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    Color c = px[y * n + x];
                    int sx = x - off, sy = y + off;
                    float sa = sx >= 0 && sy < n ? px[sy * n + sx].a * 0.35f : 0f;
                    float a = c.a + sa * (1f - c.a);
                    outp[y * n + x] = a > 0 ? new Color(c.r * c.a / a, c.g * c.a / a, c.b * c.a / a, a) : Color.clear;
                }
            tex.SetPixels(outp);
            tex.Apply();
            return tex;
        }

        static bool Inside(Vector2[] poly, Vector2 p)
        {
            bool c = false;
            for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
                if ((poly[i].y > p.y) != (poly[j].y > p.y) && p.x < (poly[j].x - poly[i].x) * (p.y - poly[i].y) / (poly[j].y - poly[i].y) + poly[i].x) c = !c;
            return c;
        }

        static float EdgeDist(Vector2[] poly, Vector2 p)
        {
            float best = float.MaxValue;
            for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
            {
                Vector2 a = poly[j], b = poly[i], ab = b - a;
                float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(1e-5f, ab.sqrMagnitude));
                best = Mathf.Min(best, (a + ab * t - p).magnitude);
            }
            return best;
        }
    }
}
