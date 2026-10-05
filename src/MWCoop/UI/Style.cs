using UnityEngine;

namespace MWCoop
{
    // Habillage "facon GTA" commun aux ecrans du mod (menu F10, ecran d'attente...) : panneaux noirs translucides,
    // titres en police grasse etroite (Impact, sinon Arial Black / Arial) a contour noir, texte blanc contoure.
    // Les rectangles sont en pixels ecran ; les tailles de police sont donnees pour 1080 lignes et mises a l'echelle
    // (S = Screen.height / 1080). Polices, styles et texture crees une seule fois (rien d'alloue par image).
    public static class Style
    {
        public static readonly Color Accent = new Color(0.33f, 0.62f, 0.95f);      // bleu des selections et onglets
        public static readonly Color White = new Color(1f, 1f, 1f);
        public static readonly Color Dim = new Color(0.68f, 0.70f, 0.72f);          // texte secondaire
        public static readonly Color PanelColor = new Color(0f, 0f, 0f, 0.80f);
        static readonly Color outline = new Color(0f, 0f, 0f, 0.9f);

        static Texture2D white;
        static Font titleFont, bodyFont;
        static GUIStyle titleSt, bodySt, fieldSt;
        static readonly GUIContent content = new GUIContent();

        // Echelle de l'interface (1 en 1080 lignes, 0,67 en 720, 1,33 en 1440).
        public static float S { get { return Mathf.Max(0.6f, Screen.height / 1080f); } }
        public static float Px(float v) { return Mathf.Round(v * S); }
        static int Size(int size) { return Mathf.Max(9, Mathf.RoundToInt(size * S)); }

        static void Init()
        {
            if (titleSt != null) return;
            white = new Texture2D(1, 1, TextureFormat.ARGB32, false);
            white.SetPixel(0, 0, Color.white);
            white.Apply();
            white.hideFlags = HideFlags.HideAndDontSave;
            string[] have = null;
            try { have = Font.GetOSInstalledFontNames(); } catch { }
            titleFont = OsFont(have, "Impact", "Arial Black", "Arial");
            bodyFont = OsFont(have, "Arial", "Tahoma", "Verdana");
            Log.Info("interface : polices " + (titleFont != null ? titleFont.name : "par defaut") + " / " + (bodyFont != null ? bodyFont.name : "par defaut"));
            titleSt = Plain(titleFont ?? GUI.skin.font);
            bodySt = Plain(bodyFont ?? GUI.skin.font);
            bodySt.clipping = TextClipping.Clip;
            fieldSt = new GUIStyle(GUI.skin.textField) { font = bodyFont ?? GUI.skin.font, border = new RectOffset(0, 0, 0, 0), alignment = TextAnchor.MiddleLeft };
            fieldSt.normal.background = fieldSt.hover.background = fieldSt.focused.background = fieldSt.active.background = null;
            fieldSt.normal.textColor = fieldSt.hover.textColor = fieldSt.focused.textColor = fieldSt.active.textColor = Color.white;
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

        // Aplat de couleur (texture blanche teintee).
        public static void Fill(Rect r, Color c)
        {
            Init();
            Color old = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, white);
            GUI.color = old;
        }

        // Panneau noir translucide.
        public static void Panel(Rect r) { Fill(r, PanelColor); }

        // Grand titre blanc a contour noir epais, a la hauteur du rectangle.
        public static void Title(Rect r, string s) { Title(r, s, White, TextAnchor.MiddleLeft, 0); }

        // Titre (ou onglet, entete) : police grasse etroite ; size = 0 : d'apres la hauteur du rectangle.
        public static void Title(Rect r, string s, Color c, TextAnchor a, int size)
        {
            Init();
            titleSt.fontSize = size > 0 ? Size(size) : Mathf.Max(9, Mathf.RoundToInt(r.height * 0.86f));
            titleSt.fontStyle = FontStyle.Normal;
            titleSt.alignment = a;
            titleSt.wordWrap = false;
            Outlined(r, s, titleSt, c, Mathf.Max(1f, Mathf.Round(titleSt.fontSize / 18f)));
        }

        // Texte blanc (ou colore) a contour noir fin.
        public static void Text(Rect r, string s, int size, TextAnchor a, Color c, bool bold = true, bool wrap = false)
        {
            Init();
            Prep(size, a, bold, wrap);
            Outlined(r, s, bodySt, c, Mathf.Max(1f, Mathf.Round(S)));
        }

        // Largeur et hauteur d'un texte (pour placer les fleches "< >" ou les paragraphes).
        public static float Width(string s, int size, bool bold = true)
        {
            Init();
            Prep(size, TextAnchor.MiddleLeft, bold, false);
            content.text = s;
            return bodySt.CalcSize(content).x;
        }

        public static float Height(string s, int size, float width, bool bold = true)
        {
            Init();
            Prep(size, TextAnchor.UpperLeft, bold, true);
            content.text = s;
            return bodySt.CalcHeight(content, width);
        }

        // Jauge : fond sombre, part remplie (0..1) dans la couleur donnee, bord noir.
        public static void Bar(Rect r, float frac, Color c)
        {
            float b = Mathf.Max(1f, Px(2));
            Fill(r, new Color(0f, 0f, 0f, 0.85f));
            var inner = new Rect(r.x + b, r.y + b, r.width - 2 * b, r.height - 2 * b);
            Fill(inner, new Color(c.r * 0.3f, c.g * 0.3f, c.b * 0.3f, 0.9f));
            inner.width *= Mathf.Clamp01(frac);
            Fill(inner, c);
        }

        // Style de champ de saisie sans fond (a poser sur un Panel), police du texte a la taille donnee.
        public static GUIStyle Field(int size)
        {
            Init();
            fieldSt.fontSize = Size(size);
            fieldSt.padding.left = fieldSt.padding.right = (int)Px(8);
            fieldSt.padding.top = fieldSt.padding.bottom = 0;
            return fieldSt;
        }

        static void Prep(int size, TextAnchor a, bool bold, bool wrap)
        {
            bodySt.fontSize = Size(size);
            bodySt.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal;
            bodySt.alignment = a;
            bodySt.wordWrap = wrap;
        }

        static void Outlined(Rect r, string s, GUIStyle st, Color c, float o)
        {
            Color oc = outline;
            oc.a *= c.a;
            st.normal.textColor = oc;
            GUI.Label(new Rect(r.x - o, r.y - o, r.width, r.height), s, st);
            GUI.Label(new Rect(r.x + o, r.y - o, r.width, r.height), s, st);
            GUI.Label(new Rect(r.x - o, r.y + o, r.width, r.height), s, st);
            GUI.Label(new Rect(r.x + o, r.y + o, r.width, r.height), s, st);
            st.normal.textColor = c;
            GUI.Label(r, s, st);
        }
    }
}
