using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace MWCoop
{
    // KARTTURI 2000 (copilote de rallye, suggestion #37 ; idee de JD, 13/10) : vieux GPS a ecran facon Game Boy (160 x 144,
    // quatre verts) vendu a l'Osuuskauppa (KauppaKartturi.cs : modele, rayon, en main). Ici : trace, notes et ecran.
    //  - trace des speciales : celui des voitures IA du rallye (automate Navigation de RALLYCARn) dans les listes de points
    //    de TRAFFIC/Routes : SS1 = DirtRoad 2168 -> 3512 (arrivee), SS3 = DirtRoad 56 -> 1846, SS2 = SaturdaySS2 1 -> 627.
    //    Les memes chez tous (listes du jeu). Le reste des routes (TRAFFIC/Routes) fait le fond de carte.
    //  - notes : trace repris tous les 2 m et lisse, courbure -> virages (gauche/droite, cote 1 a 6 d'apres le rayon le plus
    //    serre, H epingle ; RESSERRE, S'OUVRE, LONG) et bosses (pente qui casse) ; les zones de freinage du jeu
    //    (BrakezonesSS1) tombent sur les memes virages (verifie le 13/10).
    //  - suivi : point du trace le plus proche dans le sens de la marche du GPS (SS1 et SS3 se partagent la route en sens
    //    inverse) ; hors speciale, la carte seule.
    //  - ecran : carte qui tourne autour de la voiture (route de la speciale en fonce), puis la prochaine note en grand et
    //    la suivante ; bip quand une note est passee (deux pour une epingle ou un 1 a moins de 80 m).
    public static class Kartturi
    {
        public const int W = 160, H = 144;
        const float Step = 2f;

        public class Note { public int I, J; public int Side; public int Grade; public bool Tightens, Opens, Long, Crest; }
        class Stage { public string Name; public Vector3[] P; public Vector3[] Dir; public List<Note> Notes = new List<Note>(); }
        static readonly List<Stage> stages = new List<Stage>();
        static readonly List<Vector2> roadA = new List<Vector2>(), roadB = new List<Vector2>();   // fond de carte (segments)
        static bool built;
        static float buildAt;

        public static void OnLevelLoaded() { built = false; stages.Clear(); roadA.Clear(); roadB.Clear(); buildAt = 0f; }

        // Trace d'une speciale (1-3 : SS1, SS2, SS3 ; un point tous les 2 m, depart -> arrivee) pour la carte (MapPanel) ; null :
        // routes du rallye pas encore chargees.
        public static Vector3[] StageRoute(int ss) { if (!Build() || ss < 1 || ss > stages.Count) return null; return stages[ss - 1].P; }
        // Liste de points d'une route du jeu (TRAFFIC/Routes/<nom>) : la piste de glace (Icetrack...) pour la carte.
        public static List<Vector3> GameRoute(string name) { return Route(name); }

        // ---------------------------------------------------------------- trace et notes
        static List<Vector3> Route(string name)
        {
            var l = new List<Vector3>();
            GameObject g = Game.FindAny("TRAFFIC/Routes/" + name);
            if (g == null) return l;
            foreach (PlayMakerArrayListProxy p in g.GetComponents<PlayMakerArrayListProxy>())
            {
                ArrayList a = p._arrayList ?? p.arrayList;
                if (a == null || a.Count == 0) continue;
                foreach (object o in a) { var go = o as GameObject; if (go != null) l.Add(go.transform.position); }
                break;
            }
            return l;
        }

        static bool Build()
        {
            if (built) return true;
            if (Time.realtimeSinceStartup < buildAt) return false;
            buildAt = Time.realtimeSinceStartup + 10f;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            List<Vector3> dirt = Route("DirtRoad"), ss2 = Route("SaturdaySS2");
            if (dirt.Count < 3700 || ss2.Count < 630) { Log.Info("copilote : routes du rallye absentes (" + dirt.Count + ", " + ss2.Count + ")"); return false; }
            stages.Clear();
            stages.Add(MakeStage("SS1", dirt, 2168, 3512));
            stages.Add(MakeStage("SS2", ss2, 1, 627));
            stages.Add(MakeStage("SS3", dirt, 56, 1846));
            roadA.Clear(); roadB.Clear();
            GameObject routes = Game.FindAny("TRAFFIC/Routes");
            if (routes != null)
                foreach (Transform c in routes.transform)
                {
                    if (c.name.StartsWith("Icetrack") || c.name.EndsWith("Brakezones")) continue;
                    List<Vector3> r = Route(c.name);
                    Vector3 last = Vector3.zero; bool has = false;
                    foreach (Vector3 v in r)
                    {
                        if (has && (v - last).sqrMagnitude < 64f) continue;   // (un point tous les 8 m au moins)
                        if (has && (v - last).sqrMagnitude < 2500f) { roadA.Add(new Vector2(last.x, last.z)); roadB.Add(new Vector2(v.x, v.z)); }
                        last = v; has = true;
                    }
                }
            built = true;
            var sb = new StringBuilder("copilote : speciales");
            foreach (Stage s in stages) sb.Append(' ').Append(s.Name).Append(' ').Append((s.P.Length * Step / 1000f).ToString("F1")).Append(" km ").Append(s.Notes.Count).Append(" notes");
            Log.Info(sb.Append(", fond de carte ").Append(roadA.Count).Append(" segments (").Append(sw.ElapsedMilliseconds).Append(" ms)").ToString());
            return true;
        }

        static float[] Smooth(float[] v, int w)
        {
            var o = new float[v.Length];
            for (int i = 0; i < v.Length; i++)
            {
                int a = Mathf.Max(0, i - w), b = Mathf.Min(v.Length, i + w + 1); float s = 0f;
                for (int k = a; k < b; k++) s += v[k];
                o[i] = s / (b - a);
            }
            return o;
        }

        static Stage MakeStage(string name, List<Vector3> route, int a, int b)
        {
            // reprise tous les 2 m (au sol : x, z ; hauteur suivie)
            var pts = new List<Vector3> { route[a] };
            float acc = 0f;
            for (int i = a; i < b; i++)
            {
                Vector3 p = route[i], q = route[i + 1];
                float seg = new Vector2(q.x - p.x, q.z - p.z).magnitude;
                if (seg < 1e-4f) continue;
                float t = Step - acc;
                while (t <= seg) { pts.Add(Vector3.Lerp(p, q, t / seg)); t += Step; }
                acc = seg - (t - Step);
            }
            int n = pts.Count;
            var xs = new float[n]; var ys = new float[n]; var zs = new float[n];
            for (int i = 0; i < n; i++) { xs[i] = pts[i].x; ys[i] = pts[i].y; zs[i] = pts[i].z; }
            xs = Smooth(xs, 3); zs = Smooth(zs, 3); ys = Smooth(ys, 5);
            var s = new Stage { Name = name, P = new Vector3[n], Dir = new Vector3[n] };
            for (int i = 0; i < n; i++) s.P[i] = new Vector3(xs[i], ys[i], zs[i]);
            var hd = new float[n];
            for (int i = 0; i < n; i++)
            {
                int j = Mathf.Min(n - 1, i + 1), k = j == i ? i - 1 : i;
                Vector3 d = s.P[j] - s.P[k]; d.y = 0f;
                s.Dir[i] = d.normalized;
                hd[i] = Mathf.Atan2(d.x, d.z);
            }
            var dh = new float[n];
            for (int i = 1; i < n - 1; i++) dh[i] = Mathf.DeltaAngle(hd[i - 1] * Mathf.Rad2Deg, hd[i] * Mathf.Rad2Deg) * Mathf.Deg2Rad / Step;
            float[] k1 = Smooth(dh, 4);
            // virages : courbure au-dela de 1/220 m, sur au moins 18 degres
            const float KMin = 1f / 220f;
            int c0 = 0;
            while (c0 < n)
            {
                if (Mathf.Abs(k1[c0]) < KMin) { c0++; continue; }
                int sg = k1[c0] > 0 ? 1 : -1, c1 = c0; float sum = 0f, kmax = 0f; int mid;
                while (c1 < n && k1[c1] * sg > KMin * 0.7f) { sum += k1[c1]; kmax = Mathf.Max(kmax, Mathf.Abs(k1[c1])); c1++; }
                float ang = Mathf.Abs(sum) * Step * Mathf.Rad2Deg;
                if (ang > 18f)
                {
                    float r = 1f / kmax;
                    var nt = new Note { I = c0, J = c1, Side = sg, Grade = r < 14f || ang > 150f ? 0 : r < 22f ? 1 : r < 35f ? 2 : r < 55f ? 3 : r < 85f ? 4 : r < 130f ? 5 : 6, Long = (c1 - c0) * Step > 90f };
                    mid = (c0 + c1) / 2; float m1 = 1e-6f, m2 = 1e-6f;
                    for (int q = c0; q <= mid; q++) m1 = Mathf.Max(m1, Mathf.Abs(k1[q]));
                    for (int q = mid; q < c1; q++) m2 = Mathf.Max(m2, Mathf.Abs(k1[q]));
                    float r1 = 1f / m1, r2 = 1f / m2;
                    nt.Tightens = r2 < r1 * 0.7f; nt.Opens = r1 < r2 * 0.7f;
                    s.Notes.Add(nt);
                }
                c0 = Mathf.Max(c1, c0 + 1);
            }
            // bosses : la pente casse vers le bas (convexe), hors virage
            var sl = new float[n];
            for (int i = 0; i < n; i++) sl[i] = (ys[Mathf.Min(n - 1, i + 6)] - ys[Mathf.Max(0, i - 6)]) / (12f * Step);
            for (int i = 12; i < n - 12; i++)
            {
                float d = sl[i - 6] - sl[i + 6];
                if (d < 0.12f) continue;
                bool best = true;
                for (int q = i - 8; q <= i + 8 && best; q++) if (q != i && sl[q - 6] - sl[q + 6] > d) best = false;
                if (!best) continue;
                bool inCorner = false;
                foreach (Note c in s.Notes) if (i >= c.I - 5 && i <= c.J + 5) { inCorner = true; break; }
                if (!inCorner) s.Notes.Add(new Note { I = i - 4, J = i + 4, Crest = true });
            }
            s.Notes.Sort((x, y) => x.I.CompareTo(y.I));
            return s;
        }

        // ---------------------------------------------------------------- un GPS (ecran, suivi)
        public class Device
        {
            public Texture2D Tex;
            public Color32[] Px = new Color32[W * H];
            public int Stage = -1, Prog, NextNote = -1;
            public Vector3 LastPos, Dir = Vector3.forward;
            public float LastT, NextDraw, NextSearch;
            public AudioSource Beep;
        }

        static readonly Color32[] Pal = { new Color32(155, 188, 15, 255), new Color32(139, 172, 15, 255), new Color32(48, 98, 48, 255), new Color32(15, 56, 15, 255) };

        public static Device NewDevice()
        {
            var d = new Device { Tex = new Texture2D(W, H, TextureFormat.RGB24, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = "kartturi" } };
            for (int i = 0; i < d.Px.Length; i++) d.Px[i] = Pal[0];
            Splash(d);
            return d;
        }

        static void Splash(Device d)
        {
            Fill(d, 0, 0, W, H, 0);
            TextC(d, 50, "KARTTURI", 3, 3);
            TextC(d, 72, "2000", 3, 3);
            TextC(d, 100, "RALLI GPS", 1, 2);
            d.Tex.SetPixels32(d.Px); d.Tex.Apply(false);
        }

        // Suivi et dessin (au plus 10 fois par seconde) : pos = position du GPS, carHint = vitesse connue (voiture) ou zero.
        public static void Tick(Device d, Vector3 pos, Vector3 carHint, float now)
        {
            if (now < d.NextDraw) return;
            d.NextDraw = now + 0.1f;
            if (!Build()) return;
            // sens de la marche : vitesse de la voiture, sinon deplacement du GPS
            Vector3 v = carHint;
            if (v.sqrMagnitude < 1f && d.LastT > 0f && now > d.LastT) v = (pos - d.LastPos) / (now - d.LastT);
            v.y = 0f;
            if (v.sqrMagnitude > 2.25f) d.Dir = Vector3.Slerp(d.Dir, v.normalized, 0.5f).normalized;
            d.LastPos = pos; d.LastT = now;
            Track(d, pos, v.sqrMagnitude > 2.25f);
            int before = d.NextNote;
            Stage s = d.Stage >= 0 ? stages[d.Stage] : null;
            d.NextNote = -1;
            if (s != null) for (int i = 0; i < s.Notes.Count; i++) if (s.Notes[i].J > d.Prog + 2) { d.NextNote = i; break; }
            if (s != null && before >= 0 && d.NextNote != before && d.NextNote >= 0 && d.Beep != null)
            {
                Note n = s.Notes[d.NextNote];
                bool hard = !n.Crest && n.Grade <= 1 && (n.I - d.Prog) * Step < 80f;
                d.Beep.clip = hard ? BeepClip2 : BeepClip;
                d.Beep.Play();
            }
            Draw(d, pos, s);
        }

        static void Track(Device d, Vector3 pos, bool moving)
        {
            // garde la speciale : fenetre autour du point courant
            if (d.Stage >= 0)
            {
                Stage s = stages[d.Stage];
                int a = Mathf.Max(0, d.Prog - 25), b = Mathf.Min(s.P.Length - 1, d.Prog + 60), best = -1; float bd = 30f * 30f;
                for (int i = a; i <= b; i++) { float dd = Flat(s.P[i] - pos); if (dd < bd) { bd = dd; best = i; } }
                if (best >= 0 && (!moving || Vector3.Dot(s.Dir[best], d.Dir) > 0f)) { d.Prog = best; return; }
                d.Stage = -1;
            }
            if (!moving || Time.realtimeSinceStartup < d.NextSearch) return;
            d.NextSearch = Time.realtimeSinceStartup + 0.5f;
            float bestD = 18f * 18f; int bs = -1, bi = -1;
            for (int k = 0; k < stages.Count; k++)
            {
                Stage s = stages[k];
                for (int i = 0; i < s.P.Length; i += 2)
                {
                    float dd = Flat(s.P[i] - pos);
                    if (dd < bestD && Vector3.Dot(s.Dir[i], d.Dir) > 0.7f) { bestD = dd; bs = k; bi = i; }
                }
            }
            if (bs >= 0) { d.Stage = bs; d.Prog = bi; d.NextNote = -1; }
        }

        static float Flat(Vector3 v) { return v.x * v.x + v.z * v.z; }

        // ---------------------------------------------------------------- dessin
        const int MapTop = 10, MapBottom = 99;
        const float MPerPx = 3f;

        static void Draw(Device d, Vector3 pos, Stage s)
        {
            Fill(d, 0, 0, W, H, 0);
            // carte : voiture en (80, 74), cap vers le haut
            int cx = 80, cy = 74;
            float yaw = Mathf.Atan2(d.Dir.x, d.Dir.z), cs = Mathf.Cos(yaw), sn = Mathf.Sin(yaw);
            Vector2 c = new Vector2(pos.x, pos.z);
            float r2 = (110f * MPerPx) * (110f * MPerPx);
            for (int i = 0; i < roadA.Count; i++)
            {
                Vector2 a = roadA[i] - c, b = roadB[i] - c;
                if (a.sqrMagnitude > r2 && b.sqrMagnitude > r2) continue;
                Line(d, Map(a, cs, sn, cx, cy), Map(b, cs, sn, cx, cy), 2, MapTop, MapBottom);
            }
            if (s != null)
            {
                int from = Mathf.Max(0, d.Prog - 60), to = Mathf.Min(s.P.Length - 1, d.Prog + 160);
                for (int i = from; i < to; i++)
                {
                    Vector2 a = new Vector2(s.P[i].x, s.P[i].z) - c, b = new Vector2(s.P[i + 1].x, s.P[i + 1].z) - c;
                    Vector2 pa = Map(a, cs, sn, cx, cy), pb = Map(b, cs, sn, cx, cy);
                    Line(d, pa, pb, 3, MapTop, MapBottom); Line(d, pa + Vector2.right, pb + Vector2.right, 3, MapTop, MapBottom);
                }
                // arrivee
                Vector2 f = Map(new Vector2(s.P[s.P.Length - 1].x, s.P[s.P.Length - 1].z) - c, cs, sn, cx, cy);
                if (f.y > MapTop + 2 && f.y < MapBottom - 2 && f.x > 2 && f.x < W - 3) Fill(d, (int)f.x - 2, (int)f.y - 2, 5, 5, 3);
            }
            // voiture : fleche
            for (int y = 0; y < 7; y++) for (int x = -y / 2; x <= y / 2; x++) Px(d, cx + x, cy - 3 + y, 3);
            // bandeau du haut
            Fill(d, 0, 0, W, 9, 3);
            if (s != null)
            {
                Text(d, 2, 2, s.Name, 0, 1);
                float left = Mathf.Max(0f, (s.P.Length - 1 - d.Prog) * Step / 1000f);
                string km = left.ToString("F1", System.Globalization.CultureInfo.InvariantCulture) + "KM";
                Text(d, W - 2 - TextW(km, 1), 2, km, 0, 1);
            }
            else TextC(d, 2, "KARTTURI 2000", 0, 1);
            // notes
            Fill(d, 0, MapBottom + 1, W, 1, 3);
            if (s == null) { TextC(d, 112, "EI ERIKOISKOE", 3, 1); TextC(d, 124, "NO STAGE", 2, 1); }
            else if (d.NextNote < 0) { TextC(d, 112, "MAALI", 3, 2); }
            else
            {
                Note n = s.Notes[d.NextNote];
                int dist = Mathf.Max(0, Mathf.RoundToInt((n.I - d.Prog) * Step / 10f) * 10);
                DrawNote(d, n, 4, 104, 4);
                string ds = dist > 0 ? dist.ToString() : "NOW";
                Text(d, 98 - TextW(ds, 2), 108, ds, 3, 2);
                if (dist > 0) Text(d, 100, 114, "M", 3, 1);
                // la suivante (distance depuis la fin de celle-ci)
                if (d.NextNote + 1 < s.Notes.Count)
                {
                    Note m = s.Notes[d.NextNote + 1];
                    Fill(d, 108, MapBottom + 2, 1, H - MapBottom - 2, 2);
                    DrawNote(d, m, 114, 106, 2);
                    int gap = Mathf.Max(0, Mathf.RoundToInt((m.I - n.J) * Step / 10f) * 10);
                    string gs = gap < 20 ? "INTO" : gap.ToString();
                    Text(d, 114, 132, gs, 2, 1);
                }
            }
            d.Tex.SetPixels32(d.Px); d.Tex.Apply(false);
        }

        // une note : fleche (ou bosse) et cote en grand ; mots dessous
        static void DrawNote(Device d, Note n, int x, int y, int scale)
        {
            if (n.Crest)
            {
                for (int i = 0; i < 3 * scale; i++) Fill(d, x + 3 * scale - i, y + 2 * scale + i / 2, 1 + i * 0, Mathf.Max(1, scale / 2), 3);
                for (int i = 0; i < 3 * scale; i++) Fill(d, x + 3 * scale + i, y + 2 * scale + i / 2, 1, Mathf.Max(1, scale / 2), 3);
                Text(d, x, y + 6 * scale, "CREST", 3, scale >= 4 ? 2 : 1);
                return;
            }
            // fleche : tige qui monte, coude vers le cote du virage, pointe
            int s = scale, dir = n.Side < 0 ? -1 : 1, ax = x + 4 * s - s / 2, barY = y + 2 * s;
            Fill(d, ax, barY, s, 4 * s, 3);                                    // tige
            int bx0 = dir > 0 ? ax : ax - 2 * s, bx1 = dir > 0 ? ax + 3 * s : ax + s;
            Fill(d, bx0, barY, bx1 - bx0, s, 3);                               // coude
            int baseX = dir > 0 ? bx1 : bx0 - 1, cyy = barY + s / 2;
            for (int j = 0; j <= s + 1; j++) { int half = s + 1 - j; Fill(d, baseX + dir * j, cyy - half, 1, 2 * half + 1, 3); }
            string g = n.Grade == 0 ? "H" : n.Grade.ToString();
            Text(d, x + 9 * s, y, g, 3, s + 1);
            string w = n.Tightens ? "TIGHTENS" : n.Opens ? "OPENS" : n.Long ? "LONG" : "";
            if (n.Long && (n.Tightens || n.Opens)) w = (n.Tightens ? "TIGHT" : "OPENS") + " LONG";
            if (w.Length > 0) Text(d, x, y + 6 * (s + 1) + 2, w, 2, 1);
        }

        static Vector2 Map(Vector2 rel, float cs, float sn, int cx, int cy)
        {
            // repere de la voiture : droite = (cos, -sin), avant = (sin, cos)
            float rx = rel.x * cs - rel.y * sn, fz = rel.x * sn + rel.y * cs;
            return new Vector2(cx + rx / MPerPx, cy - fz / MPerPx);
        }

        static void Px(Device d, int x, int y, int c) { if (x >= 0 && x < W && y >= 0 && y < H) d.Px[(H - 1 - y) * W + x] = Pal[c]; }
        static void Fill(Device d, int x, int y, int w, int h, int c) { for (int j = y; j < y + h; j++) for (int i = x; i < x + w; i++) Px(d, i, j, c); }

        static void Line(Device d, Vector2 a, Vector2 b, int c, int y0, int y1)
        {
            if ((a.y < y0 && b.y < y0) || (a.y > y1 && b.y > y1) || (a.x < 0 && b.x < 0) || (a.x >= W && b.x >= W)) return;
            int x0 = Mathf.RoundToInt(a.x), yy0 = Mathf.RoundToInt(a.y), x1 = Mathf.RoundToInt(b.x), yy1 = Mathf.RoundToInt(b.y);
            int dx = Mathf.Abs(x1 - x0), dy = -Mathf.Abs(yy1 - yy0), sx = x0 < x1 ? 1 : -1, sy = yy0 < yy1 ? 1 : -1, err = dx + dy;
            for (int guard = 0; guard < 600; guard++)
            {
                if (yy0 >= y0 && yy0 <= y1) Px(d, x0, yy0, c);
                if (x0 == x1 && yy0 == yy1) break;
                int e2 = 2 * err;
                if (e2 >= dy) { err += dy; x0 += sx; }
                if (e2 <= dx) { err += dx; yy0 += sy; }
            }
        }

        // ---------------------------------------------------------------- police 3 x 5
        static readonly Dictionary<char, int> font = BuildFont();
        static Dictionary<char, int> BuildFont()
        {
            string[] g = {
                "0111101101101111", "1010110010010111", "2111001111100111", "3111001111001111", "4101101111001001", "5111100111001111",
                "6111100111101111", "7111001001010010", "8111101111101111", "9111101111001111", "A010101111101101", "B110101110101110",
                "C011100100100011", "D110101101101110", "E111100110100111", "F111100110100100", "G011100101101011", "H101101111101101",
                "I111010010010111", "J001001001101010", "K101101110101101", "L100100100100111", "M101111111101101", "N110101101101101",
                "O010101101101010", "P110101110100100", "Q010101101110011", "R110101110101101", "S011100010001110", "T111010010010010",
                "U101101101101111", "V101101101101010", "W101101111111101", "X101101010101101", "Y101101010010010", "Z111001010100111",
                ".000000000000010", "-000000111000000", ":000010000010000", "/001001010100100", "!010010010000010", "+000010111010000",
                "'010010000000000" };
            var f = new Dictionary<char, int>();
            foreach (string s in g) { int m = 0; for (int i = 1; i < 16; i++) m = (m << 1) | (s[i] == '1' ? 1 : 0); f[s[0]] = m; }
            f[' '] = 0;
            return f;
        }

        // Etiquette du boitier (police de l'ecran, une couleur sur un fond)
        public static Texture2D Label(string s, Color32 fg, Color32 bg)
        {
            int w = TextW(s, 1) + 4, h = 9;
            var t = new Texture2D(w, h, TextureFormat.RGB24, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[w * h];
            for (int i = 0; i < px.Length; i++) px[i] = bg;
            int x = 2;
            foreach (char ch in s)
            {
                int m;
                if (font.TryGetValue(ch, out m))
                    for (int r = 0; r < 5; r++) for (int k = 0; k < 3; k++)
                        if ((m >> (14 - (r * 3 + k)) & 1) != 0) px[(h - 3 - r) * w + x + k] = fg;
                x += 4;
            }
            t.SetPixels32(px); t.Apply(false);
            return t;
        }

        static int TextW(string s, int scale) { return s.Length * 4 * scale - scale; }
        static void TextC(Device d, int y, string s, int c, int scale) { Text(d, (W - TextW(s, scale)) / 2, y, s, c, scale); }
        static void Text(Device d, int x, int y, string s, int c, int scale)
        {
            foreach (char ch0 in s)
            {
                char ch = char.ToUpperInvariant(ch0);
                if (ch == 'Ä') ch = 'A'; else if (ch == 'Ö') ch = 'O';
                int m;
                if (font.TryGetValue(ch, out m))
                    for (int r = 0; r < 5; r++) for (int k = 0; k < 3; k++)
                        if ((m >> (14 - (r * 3 + k)) & 1) != 0) Fill(d, x + k * scale, y + r * scale, scale, scale, c);
                x += 4 * scale;
            }
        }

        // ---------------------------------------------------------------- bip (onde carree, facon console)
        static AudioClip beep1, beep2;
        public static AudioClip BeepClip { get { if (beep1 == null) beep1 = MakeBeep(1, "kartturi-bip"); return beep1; } }
        public static AudioClip BeepClip2 { get { if (beep2 == null) beep2 = MakeBeep(2, "kartturi-bip2"); return beep2; } }
        static AudioClip MakeBeep(int count, string name)
        {
            const int rate = 22050; int one = rate * 7 / 100, gap = rate * 5 / 100, n = count * one + (count - 1) * gap;
            var data = new float[n];
            for (int b = 0; b < count; b++)
                for (int i = 0; i < one; i++)
                {
                    float t = (float)i / rate;
                    float env = Mathf.Min(1f, Mathf.Min(i, one - i) / (rate * 0.004f));
                    data[b * (one + gap) + i] = (Mathf.Repeat(t * 1318f, 1f) < 0.5f ? 0.35f : -0.35f) * env;
                }
            AudioClip c = AudioClip.Create(name, n, 1, rate, false);
            c.SetData(data, 0);
            return c;
        }

        // Essais : point de la speciale k a 'm' metres du depart (et son sens)
        public static bool StagePoint(int k, float m, out Vector3 pos, out Vector3 dir)
        {
            pos = dir = Vector3.zero;
            if (!Build() || k < 0 || k >= stages.Count) return false;
            Stage s = stages[k];
            int i = Mathf.Clamp(Mathf.RoundToInt(m / Step), 0, s.P.Length - 1);
            pos = s.P[i]; dir = s.Dir[i];
            return true;
        }
        public static string State(Device d)
        {
            if (d.Stage < 0) return "hors speciale";
            Stage s = stages[d.Stage];
            Note n = d.NextNote >= 0 ? s.Notes[d.NextNote] : null;
            return s.Name + " a " + (d.Prog * Step) + " m, note suivante " + (n == null ? "-" : n.Crest ? "bosse" : (n.Side < 0 ? "G" : "D") + (n.Grade == 0 ? "H" : n.Grade.ToString())) + (n != null ? " dans " + ((n.I - d.Prog) * Step) + " m" : "");
        }

        // ---------------------------------------------------------------- essais
        // [Test] Autotest=copilote : a 40 s, les notes des speciales dans dumps\copilote-notes.txt et une image de l'ecran
        // (GPS pose au depart de SS1, puis 600 m plus loin) dans dumps\copilote-ecran-*.png.
        static bool probed;
        public static void Test(string mode, float t)
        {
            if (mode != "copilote" || probed || t < 40f) return;
            probed = true;
            if (!Build()) return;
            var sb = new StringBuilder();
            foreach (Stage s in stages)
            {
                sb.Append("== ").Append(s.Name).Append(' ').Append(s.P.Length * Step).Append(" m\n");
                foreach (Note n in s.Notes)
                    sb.Append((n.I * Step).ToString("F0")).Append(" m : ").Append(n.Crest ? "bosse" : (n.Side < 0 ? "G" : "D") + (n.Grade == 0 ? "H" : n.Grade.ToString()))
                      .Append(n.Tightens ? " resserre" : "").Append(n.Opens ? " s'ouvre" : "").Append(n.Long ? " long" : "").Append('\n');
            }
            string dir = Path.Combine(Log.DataDir, "dumps");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "copilote-notes.txt"), sb.ToString());
            foreach (int at in new[] { 10, 300, 700 })
            {
                Stage s = stages[0];
                var d = NewDevice();
                d.Dir = s.Dir[at];
                d.NextDraw = 0f;
                Tick(d, s.P[at], s.Dir[at] * 20f, 1f);
                File.WriteAllBytes(Path.Combine(dir, "copilote-ecran-" + at + ".png"), d.Tex.EncodeToPNG());
            }
            Log.Info("copilote : notes et ecrans dans " + dir);
        }
    }
}
