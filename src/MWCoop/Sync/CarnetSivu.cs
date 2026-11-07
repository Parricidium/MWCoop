using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace MWCoop
{
    // Page du carnet du copilote (croquis de JD, 07/11), dessinee sur le GPU dans une RenderTexture de 1024 x 1448 posee sur la
    // feuille du dessus du modele (Carnet.cs : donnees ; KauppaCarnet.cs : objet, en main, vente).
    //  - papier : assets/carnet/page.png (run/carnet/carnet.py) ; encre : la planche de JD (assets/carnet/symboles.png, 8 x 6
    //    cases de 128 px, fond retire par [Launcher]), couleurs gardees, images jamais retournees.
    //  - colonne de gauche, le « radar » : le trace de la speciale au stylo bleu, tremble de facon stable (decalage fonction de
    //    l'index du point sur la route, jamais du temps) ; la route monte toujours (sens de la route au point de la voiture) ;
    //    la voiture est le rond vert, fixe ; la route defile dessous, 0,8 m par pixel.
    //  - colonne de droite, les notes : une note par ligne, tous les deux traits du papier ; la note du virage actuel sur le
    //    trait orange (fixe), les suivantes au-dessus (elles arrivent par le haut), les passees dessous ; defilement doux, la
    //    position est interpolee en metres entre deux notes. Pas de distances en chiffres.
    //  - une ligne : cote (chiffre ou H), fleche (ou carre), long, resserre / s'ouvre, ne coupe pas, !, enchaine. Version v1 ou
    //    v2 de chaque symbole stable par note (meme regle que le lanceur : h = m * 2654435761, case k en v2 si le bit 31 - k).
    //  Mise a jour : au plus 30 fois par seconde pour le carnet leve, 6 pour les autres ; [Test] CarnetMesure=1 : cout moyen au
    //  journal toutes les 20 s.
    public static class CarnetSivu
    {
        public const int W = 1024, H = 1448;
        // (pixels de la page, y vers le bas)
        const float RuleStep = 0.046f * H, RuleTop = 0.075f * H;
        const float Yo = RuleTop + 12 * RuleStep;      // trait orange : sur le 13e trait du papier (62,7 % de la hauteur)
        const float RowStep = 2f * RuleStep;          // une note tous les deux traits
        const float NotesX0 = 0.505f * W, NotesX1 = 0.975f * W;
        const float CellH = 190f;                     // hauteur d'une case de la planche sur la page (corps des chiffres ~ 80 px)
        const float Base = 0.70f, Mid = 0.477f;       // bas des dessins de JD dans une case ; milieu du trait orange
        const float RadarX0 = 0.15f * W, RadarX1 = 0.49f * W, CarX = 0.31f * W, CarY = 0.48f * H;
        static float MPerPx = 0.8f;

        public class Page
        {
            public RenderTexture Rt;
            public Material Mat;                       // matiere de la feuille du dessus (eclairee par la scene)
            public Kartturi.Device Trk = Kartturi.NewTracker();
            public Carnet.Book Book;                   // carnet affiche (null : notes automatiques)
            public float NextDraw, Lamp;               // Lamp : 0..1, lampe du copilote (nuit)
            public int Stage = -1; public float M;     // dernier etat dessine (essais)
        }

        static Texture2D paper, atlas, pen, disc;
        // (roue des symboles : la planche, un disque doux, le rectangle d'une case en coordonnees de texture)
        public static Texture2D Atlas { get { return Load() ? atlas : null; } }
        public static Texture2D Disc { get { Load(); return disc; } }
        public static Rect CellRect(int c) { int col = c % 8, row = c / 8; return new Rect(col / 8f, 1f - (row + 1) / 6f, 1f / 8f, 1f / 6f); }
        static Material ink;
        static readonly float[] inkX0 = new float[48], inkX1 = new float[48];   // largeur d'encre de chaque case (0..1 de la case)
        static bool tried;
        static readonly Color Blue = new Color(68f / 255f, 77f / 255f, 205f / 255f, 1f);

        static Texture2D Res(string name, bool alpha)
        {
            var t = new Texture2D(2, 2, alpha ? TextureFormat.RGBA32 : TextureFormat.RGB24, false) { name = "carnet_" + name, wrapMode = TextureWrapMode.Clamp };
            using (Stream s = typeof(CarnetSivu).Assembly.GetManifestResourceStream("MWCoop.kauppa.carnet-" + name))
            {
                if (s == null) { Log.Warn("carnet : " + name + " absent de la DLL"); return null; }
                var b = new byte[s.Length]; int got = 0;
                while (got < b.Length) { int n = s.Read(b, got, b.Length - got); if (n <= 0) break; got += n; }
                t.LoadImage(b);
            }
            return t;
        }

        static bool Load()
        {
            if (tried) return ink != null;
            tried = true;
            Shader sh = Shader.Find("Sprites/Default") ?? Shader.Find("Particles/Alpha Blended") ?? Shader.Find("GUI/Text Shader");
            paper = Res("page.png", false); atlas = Res("symboles.png", true);
            if (sh == null || paper == null || atlas == null) { Log.Warn("carnet : page impossible (" + (sh == null ? "shader" : "images") + " absent)"); return false; }
            ink = new Material(sh) { name = "carnet_encre", hideFlags = HideFlags.HideAndDontSave };
            atlas.filterMode = FilterMode.Trilinear; atlas.anisoLevel = 2;
            // largeur d'encre de chaque case (les dessins de JD sont centres dans des cases de 128 px avec du blanc autour)
            Color32[] px = atlas.GetPixels32();
            int aw = atlas.width, ah = atlas.height, cw = aw / 8, ch = ah / 6;
            for (int c = 0; c < 48; c++)
            {
                int col = c % 8, row = c / 8, x0 = col * cw, y0 = ah - (row + 1) * ch, lo = cw, hi = -1;
                for (int x = 0; x < cw; x++)
                    for (int y = 0; y < ch; y++)
                        if (px[(y0 + y) * aw + x0 + x].a > 40) { if (x < lo) lo = x; if (x > hi) hi = x; break; }
                if (hi < 0) { lo = 0; hi = cw - 1; }
                inkX0[c] = Mathf.Max(0, lo - 3) / (float)cw; inkX1[c] = Mathf.Min(cw, hi + 4) / (float)cw;
            }
            // stylo : bord doux en travers du trait
            pen = new Texture2D(4, 32, TextureFormat.RGBA32, false) { name = "carnet_stylo", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var pp = new Color32[4 * 32];
            for (int y = 0; y < 32; y++)
            {
                float d = Mathf.Abs((y + 0.5f) / 32f - 0.5f) * 2f;   // 0 au milieu, 1 au bord
                byte a = (byte)(255f * Mathf.Clamp01((1f - d) / 0.35f));
                for (int x = 0; x < 4; x++) pp[y * 4 + x] = new Color32(255, 255, 255, a);
            }
            pen.SetPixels32(pp); pen.Apply(false);
            disc = new Texture2D(64, 64, TextureFormat.RGBA32, false) { name = "carnet_disque", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var dp = new Color32[64 * 64];
            for (int y = 0; y < 64; y++) for (int x = 0; x < 64; x++)
            {
                float r = new Vector2(x - 31.5f, y - 31.5f).magnitude / 32f;
                dp[y * 64 + x] = new Color32(255, 255, 255, (byte)(255f * Mathf.Clamp01((1f - r) / 0.06f)));
            }
            disc.SetPixels32(dp); disc.Apply(false);
            MPerPx = Config.GetFloat("Test", "CarnetEchelle", 0.8f);   // (0,5 le 07/11 : la route sortait vite de la colonne dans les virages)
            LoadFont();
            Log.Info("carnet : page prete (shader " + sh.name + ", planche " + aw + "x" + ah + ", police " + glyphs.Count + " glyphes)");
            return true;
        }

        // ---------------------------------------------------------------- texte libre : Caveat (SIL OFL 1.1), atlas de glyphes
        // (run/carnet/carnet.py : caveat.png, caveat.txt « taille hauteur ascendante espace », puis « code x y l h dx dy avance »)
        static Texture2D font;
        static readonly Dictionary<char, float[]> glyphs = new Dictionary<char, float[]>();
        static float fontAsc = 58f, fontSpace = 15f;
        static void LoadFont()
        {
            try
            {
                font = Res("caveat.png", true);
                string txt;
                using (Stream s = typeof(CarnetSivu).Assembly.GetManifestResourceStream("MWCoop.kauppa.carnet-caveat.txt"))
                {
                    if (s == null || font == null) { Log.Warn("carnet : police absente de la DLL, pas de texte libre"); font = null; return; }
                    txt = new StreamReader(s, System.Text.Encoding.UTF8).ReadToEnd();
                }
                var ci = System.Globalization.CultureInfo.InvariantCulture;
                string[] lines = txt.Split('\n');
                string[] h = lines[0].Trim().Split(' ');
                fontAsc = float.Parse(h[2], ci); fontSpace = float.Parse(h[3], ci);
                for (int i = 1; i < lines.Length; i++)
                {
                    string[] f = lines[i].Trim().Split(' ');
                    if (f.Length < 8) continue;
                    var g = new float[7];
                    for (int k = 0; k < 7; k++) g[k] = float.Parse(f[k + 1], ci);
                    glyphs[(char)int.Parse(f[0], ci)] = g;
                }
                font.filterMode = FilterMode.Trilinear;
            }
            catch (System.Exception e) { Log.Warn("carnet : police illisible : " + e.Message); font = null; glyphs.Clear(); }
        }
        static float TextWidth(string s)
        {
            float w = 0f; float[] g;
            foreach (char c in s) { if (c == ' ') w += fontSpace; else if (glyphs.TryGetValue(c, out g)) w += g[6]; }
            return w;
        }
        // textes de la page (dessines apres les symboles : une autre texture)
        struct TextAt { public string S; public float X, Base, Scale, Alpha; }
        static readonly List<TextAt> texts = new List<TextAt>();
        static void DrawTexts()
        {
            if (texts.Count == 0) return;
            if (font == null) { texts.Clear(); return; }
            float fw = font.width, fh = font.height;
            Use(font); GL.Begin(GL.QUADS);
            foreach (TextAt t in texts)
            {
                float x = t.X, top = t.Base - fontAsc * t.Scale;
                Color c = Blue; c.a = t.Alpha;
                foreach (char ch in t.S)
                {
                    float[] g;
                    if (ch == ' ') { x += fontSpace * t.Scale; continue; }
                    if (!glyphs.TryGetValue(ch, out g)) continue;   // (arabe : pas dans Caveat)
                    float x0 = x + g[4] * t.Scale, y0 = top + g[5] * t.Scale;
                    Quad(x0, y0, x0 + g[2] * t.Scale, y0 + g[3] * t.Scale, g[0] / fw, 1f - (g[1] + g[3]) / fh, (g[0] + g[2]) / fw, 1f - g[1] / fh, c);
                    x += g[6] * t.Scale;
                }
            }
            GL.End();
            texts.Clear();
        }

        public static Page NewPage(Shader lit)
        {
            if (!Load()) return null;
            var p = new Page();
            // (sans mipmaps : rien ne dit qu'Unity 5 les refait apres un dessin GL, sans camera ; une page lointaine scintille un peu)
            p.Rt = new RenderTexture(W, H, 0, RenderTextureFormat.ARGB32) { name = "carnet_page", useMipMap = false, anisoLevel = 4, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            p.Rt.Create();
            p.Mat = new Material(lit != null ? lit : Shader.Find("Legacy Shaders/Diffuse")) { name = "carnet_page", mainTexture = p.Rt, color = Color.white };
            Blank(p);
            return p;
        }

        public static void Release(Page p)
        {
            if (p == null) return;
            if (p.Rt != null) { p.Rt.Release(); Object.Destroy(p.Rt); p.Rt = null; }
            if (p.Mat != null) { Object.Destroy(p.Mat); p.Mat = null; }
        }

        // papier seul (avant le premier suivi)
        static void Blank(Page p)
        {
            Begin(p.Rt);
            Use(paper); GL.Begin(GL.QUADS); Quad(0, 0, W, H, 0, 0, 1, 1, Color.white); GL.End();
            End();
        }

        // ---------------------------------------------------------------- suivi et dessin
        static double costMs; static int costN; static float costAt;

        // pos : position du carnet (voiture) ; vel : vitesse connue (voiture) ou zero ; fast : carnet leve ici (30 Hz)
        public static void Tick(Page p, Vector3 pos, Vector3 vel, float now, bool fast)
        {
            if (p == null || p.Rt == null) return;
            if (!p.Rt.IsCreated()) { p.Rt.Create(); p.NextDraw = 0f; }   // (perdue : changement de mode d'affichage)
            if (now < p.NextDraw) return;
            p.NextDraw = now + (fast ? 1f / 30f : 1f / 6f);
            if (!Kartturi.Ready) return;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            Kartturi.Follow(p.Trk, pos, vel, now);
            int k = Kartturi.TrackedStage(p.Trk);
            float m = 0f;
            if (k < 0) k = NearStart(pos);   // (avant le depart, arrete : la speciale dont on est au depart, a 0 m)
            else m = Progress(k, Kartturi.TrackedIndex(p.Trk), pos);
            Draw(p, k, m);
            p.Stage = k; p.M = m;
            costMs += sw.Elapsed.TotalMilliseconds; costN++;
            if (Config.Testing && Config.GetInt("Test", "CarnetMesure", 0) != 0 && now > costAt)
            {
                if (costAt > 0f) Log.Info("carnet : page, " + costN + " dessins, " + (costMs / Mathf.Max(1, costN)).ToString("F3") + " ms en moyenne (processeur)");
                costAt = now + 20f; costMs = 0; costN = 0;
            }
        }

        static int NearStart(Vector3 pos)
        {
            int best = -1; float bd = 60f * 60f;
            for (int k = 0; k < Kartturi.StageCount; k++)
            {
                Vector3 s = Kartturi.StagePoints(k)[0];
                float d = (s.x - pos.x) * (s.x - pos.x) + (s.z - pos.z) * (s.z - pos.z);
                if (d < bd) { bd = d; best = k; }
            }
            return best;
        }

        // metres le long de la speciale k : point suivi (index) affine sur ses deux segments
        static float Progress(int k, int i, Vector3 pos)
        {
            Vector3[] P = Kartturi.StagePoints(k);
            float step = Kartturi.StepM, best = i * step, bd = float.MaxValue;
            for (int s = Mathf.Max(0, i - 1); s <= Mathf.Min(P.Length - 2, i); s++)
            {
                Vector2 a = new Vector2(P[s].x, P[s].z), b = new Vector2(P[s + 1].x, P[s + 1].z), q = new Vector2(pos.x, pos.z);
                Vector2 ab = b - a; float t = Mathf.Clamp01(Vector2.Dot(q - a, ab) / Mathf.Max(1e-4f, ab.sqrMagnitude));
                float d = (a + ab * t - q).sqrMagnitude;
                if (d < bd) { bd = d; best = (s + t) * step; }
            }
            return best;
        }

        static void Draw(Page p, int k, float m)
        {
            Begin(p.Rt);
            Use(paper); GL.Begin(GL.QUADS); Quad(0, 0, W, H, 0, 0, 1, 1, Color.white); GL.End();
            if (k >= 0)
            {
                Radar(k, m);
                Use(atlas); GL.Begin(GL.QUADS);
                Notes(Carnet.NotesFor(k, p.Book), m);
                GL.End();
                DrawTexts();
            }
            // la voiture, toujours la
            Use(atlas); GL.Begin(GL.QUADS);
            float cs = 150f, w = (inkX1[23] - inkX0[23]) * cs;
            CellAt(23, CarX - w / 2f, CarY, cs, 1f, true);
            GL.End();
            End();
        }

        // ---------------------------------------------------------------- radar
        static void Radar(int k, float m)
        {
            Vector3[] P = Kartturi.StagePoints(k);
            float step = Kartturi.StepM;
            int n = P.Length;
            float fi = Mathf.Clamp(m / step, 0f, n - 1.001f);
            int i0 = Mathf.FloorToInt(fi); float t = fi - i0;
            Vector3 c = Vector3.Lerp(P[i0], P[Mathf.Min(n - 1, i0 + 1)], t);
            // sens de la route au point de la voiture (sur 12 m) : vers le haut
            Vector3 dv = P[Mathf.Min(n - 1, i0 + 4)] - P[Mathf.Max(0, i0 - 2)]; dv.y = 0f;
            if (dv.sqrMagnitude < 1e-4f) dv = Vector3.forward;
            dv.Normalize();
            float cs = dv.z, sn = dv.x;
            int from = Mathf.Max(0, i0 - Mathf.CeilToInt((H - CarY + 40f) * MPerPx / step)), to = Mathf.Min(n - 1, i0 + Mathf.CeilToInt((CarY + 40f) * MPerPx / step));
            Use(pen); GL.Begin(GL.QUADS);
            Vector2 prevA = Vector2.zero, prevB = Vector2.zero; float prevAl = 0f; bool has = false;
            for (int i = from; i <= to; i++)
            {
                Vector2 s = Scr(P[i], c, cs, sn);
                // tremblement : decalage en travers de la route, fonction de l'index seul (stable d'une image a l'autre)
                Vector2 dir = Scr(P[Mathf.Min(n - 1, i + 1)], c, cs, sn) - Scr(P[Mathf.Max(0, i - 1)], c, cs, sn);
                if (dir.sqrMagnitude < 1e-6f) dir = Vector2.up;
                dir.Normalize();
                Vector2 nrm = new Vector2(-dir.y, dir.x);
                float wob = Mathf.Sin(i * 0.61f + 1.3f) * 1.6f + Mathf.Sin(i * 0.23f + 4.1f) * 1.2f + Mathf.Sin(i * 1.7f) * 0.5f;
                float half = 4.6f + 0.8f * Mathf.Sin(i * 0.13f + 0.7f);
                s += nrm * wob;
                Vector2 a = s - nrm * half, b = s + nrm * half;
                float al = Mathf.Clamp01((s.x - RadarX0) / 40f) * Mathf.Clamp01((RadarX1 - s.x) / 40f) * Mathf.Clamp01((s.y - 24f) / 80f) * Mathf.Clamp01((H - 24f - s.y) / 80f);
                if (has && (al > 0f || prevAl > 0f))
                {
                    Color c0 = Blue, c1 = Blue; c0.a = prevAl * 0.92f; c1.a = al * 0.92f;
                    GL.Color(c0); GL.TexCoord2(0.5f, 0f); GL.Vertex3(prevA.x, prevA.y, 0f); GL.TexCoord2(0.5f, 1f); GL.Vertex3(prevB.x, prevB.y, 0f);
                    GL.Color(c1); GL.TexCoord2(0.5f, 1f); GL.Vertex3(b.x, b.y, 0f); GL.TexCoord2(0.5f, 0f); GL.Vertex3(a.x, a.y, 0f);
                }
                prevA = a; prevB = b; prevAl = al; has = true;
            }
            GL.End();
        }

        // monde -> page : la voiture en (CarX, CarY), le sens de la route vers le haut
        static Vector2 Scr(Vector3 w, Vector3 c, float cs, float sn)
        {
            float x = w.x - c.x, z = w.z - c.z;
            float rx = x * cs - z * sn, fz = x * sn + z * cs;
            return new Vector2(CarX + rx / MPerPx, CarY - fz / MPerPx);
        }

        // ---------------------------------------------------------------- notes
        static void Notes(List<Carnet.Note> l, float m)
        {
            int n = l.Count;
            if (n == 0) { OrangeLine(); return; }
            // position continue : k0 = derniere note passee (m_k <= m), interpolee jusqu'a la suivante
            int k0 = -1;
            for (int i = 0; i < n; i++) { if (l[i].M <= m) k0 = i; else break; }
            float s;
            if (k0 < 0) s = -1f + Mathf.Clamp01(1f - (l[0].M - m) / 150f);
            else if (k0 >= n - 1) s = n - 1 + Mathf.Clamp01((m - l[n - 1].M) / 60f);
            else s = k0 + Mathf.Clamp01((m - l[k0].M) / Mathf.Max(1f, l[k0 + 1].M - l[k0].M));
            int lo = Mathf.Max(0, Mathf.FloorToInt(s - 4f)), hi = Mathf.Min(n - 1, Mathf.CeilToInt(s + 7f));
            for (int i = lo; i <= hi; i++)
            {
                float y = Yo - (i - s) * RowStep;
                if (y < 40f || y > H + 60f) continue;
                float al = Mathf.Clamp01((y - 60f) / 120f) * Mathf.Clamp01((H + 30f - y) / 140f);
                if (al > 0f) Line(l[i], y, al);
            }
            OrangeLine();
        }

        static void OrangeLine()
        {
            // cases 24 a 27 : une seule image sur quatre cases ; son trait a 47,7 % de la hauteur, pose juste sous la note actuelle
            float w = NotesX1 - NotesX0 + 20f, h = w / 4f;
            float top = Yo + 10f - Mid * h;
            float u1 = 4f / 8f, v1 = 1f - 3f / 6f, v0 = v1 - 1f / 6f;
            Quad(NotesX0 - 20f, top, NotesX0 - 20f + w, top + h, 0f, v0, u1, v1, Color.white);
        }

        static readonly List<int> cells = new List<int>(12);
        static void Line(Carnet.Note n, float y, float alpha)
        {
            cells.Clear();
            uint h = (uint)n.M * 2654435761u;
            bool v0 = ((h >> 31) & 1) != 0, v1 = ((h >> 30) & 1) != 0, v2 = ((h >> 29) & 1) != 0, v3 = ((h >> 28) & 1) != 0;
            bool corner = n.Type == "virage" || n.Type == "carre";
            switch (n.Type)
            {
                case "virage":
                    cells.Add(n.Grade == 0 ? 10 : v0 ? 32 + n.Grade : n.Grade);
                    cells.Add(n.Side > 0 ? (v1 ? 12 : 11) : (v1 ? 14 : 13));
                    break;
                case "carre": cells.Add(n.Side > 0 ? (v1 ? 43 : 42) : (v1 ? 45 : 44)); break;
                case "bosse": cells.Add(v2 ? 19 : 18); break;
                case "pont": cells.Add(30); break;
                case "arrivee": cells.Add(29); break;
                case "droit": cells.Add(31); break;
                case "danger": cells.Add(v3 ? 21 : 20); break;
                case "texte": break;   // (texte libre : ecrit en Caveat, avant les modificateurs)
            }
            if (corner && (n.Mods & Carnet.MLong) != 0) cells.Add(15);
            if (corner && (n.Mods & Carnet.MResserre) != 0) cells.Add(16);
            else if (corner && (n.Mods & Carnet.MOuvre) != 0) cells.Add(17);
            if (corner && (n.Mods & Carnet.MNc) != 0) cells.Add(22);
            if (n.Type != "danger" && (n.Mods & Carnet.MDanger) != 0) cells.Add(v3 ? 21 : 20);
            if ((n.Mods & Carnet.MEnchaine) != 0) cells.Add(28);
            bool text = n.Type == "texte" && n.Text.Length > 0 && glyphs.Count > 0;
            if (cells.Count == 0 && !text) return;
            // largeur : reduite si la ligne deborde de la colonne
            const float TextScale = 1.55f;   // (majuscules de Caveat a la hauteur des chiffres de JD)
            float ch = CellH, gap = 8f, wsum = text ? TextWidth(n.Text) * TextScale + gap : 0f;
            foreach (int c in cells) wsum += (inkX1[c] - inkX0[c]) * ch + gap;
            float avail = NotesX1 - NotesX0, k = wsum > avail ? avail / wsum : 1f;
            ch *= k;
            float x = NotesX0;
            if (text) { texts.Add(new TextAt { S = n.Text, X = x, Base = y, Scale = TextScale * k, Alpha = alpha }); x += TextWidth(n.Text) * TextScale * k + gap * k; }
            foreach (int c in cells) x += CellAt(c, x, y - Base * ch, ch, alpha, false) + gap * ch / CellH;
        }

        // une case de la planche (sa largeur d'encre seulement), haut a y, hauteur ch ; renvoie la largeur dessinee
        static float CellAt(int c, float x, float top, float ch, float alpha, bool center)
        {
            int col = c % 8, row = c / 8;
            float u0 = (col + inkX0[c]) / 8f, u1 = (col + inkX1[c]) / 8f, v1 = 1f - row / 6f, v0 = v1 - 1f / 6f;
            float w = (inkX1[c] - inkX0[c]) * ch;
            if (center) top -= ch / 2f;
            Quad(x, top, x + w, top + ch, u0, v0, u1, v1, new Color(1f, 1f, 1f, alpha));
            return w;
        }

        // ---------------------------------------------------------------- GL
        static RenderTexture prevRt;
        static void Begin(RenderTexture rt) { prevRt = RenderTexture.active; RenderTexture.active = rt; GL.PushMatrix(); GL.LoadPixelMatrix(0, W, H, 0); }
        static void End() { GL.PopMatrix(); RenderTexture.active = prevRt; }
        static void Use(Texture t) { ink.mainTexture = t; ink.SetPass(0); }
        // (y vers le bas sur la page ; v vers le haut dans les textures)
        static void Quad(float x0, float y0, float x1, float y1, float u0, float v0, float u1, float v1, Color c)
        {
            GL.Color(c);
            GL.TexCoord2(u0, v1); GL.Vertex3(x0, y0, 0f);
            GL.TexCoord2(u1, v1); GL.Vertex3(x1, y0, 0f);
            GL.TexCoord2(u1, v0); GL.Vertex3(x1, y1, 0f);
            GL.TexCoord2(u0, v0); GL.Vertex3(x0, y1, 0f);
        }

        // Essais : la page en PNG (lue sur le GPU)
        public static void SavePng(Page p, string file)
        {
            if (p == null || p.Rt == null) return;
            RenderTexture prev = RenderTexture.active;
            RenderTexture.active = p.Rt;
            var t = new Texture2D(W, H, TextureFormat.RGB24, false);
            t.ReadPixels(new Rect(0, 0, W, H), 0, 0); t.Apply(false);
            RenderTexture.active = prev;
            File.WriteAllBytes(file, t.EncodeToPNG());
            Object.Destroy(t);
        }

        public static void OnLevelLoaded() { costAt = 0f; costMs = 0; costN = 0; }
    }
}
