using System.Collections.Generic;
using System.IO;
using MWCoop.Net;
using UnityEngine;

namespace MWCoop
{
    // Visage personnalise (demande de JD, 10/10 -- « comme Arx Fatalis ») : le joueur importe une image dans le lanceur (onglet
    // TENUE : %LOCALAPPDATA%\MWCoop\visage.jpg, au format du visage des PNJ, 512x256). Son visage dans la chaine d'apparence
    // devient "perso_<empreinte>" ; chez les autres, l'image est demandee (@visage?) a qui l'a, recue par morceaux (@visagep,
    // fiables, 900 octets), gardee en cache (%LOCALAPPDATA%\MWCoop\visages\<empreinte>.jpg) et posee sur une copie de la
    // matiere du visage du jeu. [Coop] VisagePerso=0 : le sien n'est pas montre ; VoirVisages=0 : ceux des autres non plus.
    // Gabarit (Export) : la texture du visage du jeu, et la meme avec le trace des coutures (UV) du visage du corps des PNJ,
    // ecrites dans %LOCALAPPDATA%\MWCoop (gabarit-visage.png, visage-jeu.png) : on peint par-dessus, on importe.
    public static class CustomFace
    {
        public static int Generation;   // change quand une image arrive : les avatars reprennent leur apparence
        const int Chunk = 900;
        static readonly Dictionary<string, Material> made = new Dictionary<string, Material>();
        static readonly Dictionary<string, float> asked = new Dictionary<string, float>(), sentAt = new Dictionary<string, float>();
        class Parts { public byte[][] P; public int Got; }
        static readonly Dictionary<string, Parts> incoming = new Dictionary<string, Parts>();
        static string myKey; static bool myLooked;

        // ([Test] RacineVisages : un dossier par instance d'essai, qui partagent sinon le meme LOCALAPPDATA)
        static string Root { get { string o = Config.Get("Test", "RacineVisages", ""); return o.Length > 0 ? o : Path.Combine(System.Environment.GetEnvironmentVariable("LOCALAPPDATA") ?? ".", "MWCoop"); } }
        static string CacheDir { get { return Path.Combine(Root, "visages"); } }
        static string MyFile { get { return Path.Combine(Root, "visage.jpg"); } }
        static string CacheFile(string hash) { return Path.Combine(CacheDir, hash + ".jpg"); }

        static string Hash(byte[] b)
        {
            using (var md5 = System.Security.Cryptography.MD5.Create())
            {
                byte[] h = md5.ComputeHash(b);
                var sb = new System.Text.StringBuilder();
                for (int i = 0; i < 6; i++) sb.Append(h[i].ToString("x2"));
                return sb.ToString();
            }
        }

        // Cle du visage du joueur local ("perso_<empreinte>"), ou null (pas d'image, ou option coupee).
        public static string MyKey
        {
            get
            {
                if (myLooked) return myKey;
                myLooked = true;
                try
                {
                    if (Config.GetInt("Coop", "VisagePerso", 1) == 0 || !File.Exists(MyFile)) return null;
                    byte[] b = File.ReadAllBytes(MyFile);
                    if (b.Length < 100 || b.Length > 600000) { Log.Warn("visage perso : image refusee (" + b.Length + " octets)"); return null; }
                    string h = Hash(b);
                    Directory.CreateDirectory(CacheDir);
                    if (!File.Exists(CacheFile(h))) File.WriteAllBytes(CacheFile(h), b);
                    myKey = "perso_" + h;
                    Log.Info("visage perso : " + myKey + " (" + b.Length + " octets)");
                }
                catch (System.Exception e) { Log.Warn("visage perso : " + e.Message); }
                return myKey;
            }
        }

        public static Material Make(string name)
        {
            if (!name.StartsWith("perso_")) return null;
            Material m;
            if (made.TryGetValue(name, out m) && m != null) return m;
            if (Config.GetInt("Coop", "VoirVisages", 1) == 0 && name != MyKey) return null;
            string h = name.Substring(6), f = CacheFile(h);
            if (!File.Exists(f)) { Ask(h); return null; }
            Material tpl = Avatar.FindMaterial("char_face01");
            if (tpl == null) return null;
            try
            {
                var tex = new Texture2D(2, 2, TextureFormat.RGB24, true);
                if (!tex.LoadImage(File.ReadAllBytes(f))) { Object.Destroy(tex); Log.Warn("visage perso : " + h + " illisible"); return null; }
                tex.name = name;
                m = new Material(tpl) { name = name, mainTexture = tex };
                made[name] = m;
                Log.Info("visage perso : " + name + " pose (" + tex.width + "x" + tex.height + ")");
            }
            catch (System.Exception e) { Log.Warn("visage perso : " + e.Message); }
            return m;
        }

        static void Ask(string h)
        {
            float t;
            if (!Session.Active || (asked.TryGetValue(h, out t) && Time.realtimeSinceStartup - t < 30f)) return;
            asked[h] = Time.realtimeSinceStartup;
            Session.SendAll(new NetWriter(Msg.Job).U8(Session.LocalId).Str("@visage?").Str(h), true);
            Log.Info("visage perso : " + h + " demande");
        }

        public static void OnAsk(int who, string h)
        {
            float t;
            string f = CacheFile(h);
            if (!File.Exists(f) || (sentAt.TryGetValue(h, out t) && Time.realtimeSinceStartup - t < 20f)) return;
            if (!Session.IsHost && MyKey != "perso_" + h) return;   // (seul celui qui l'a importee repond, et l'hote qui l'a en cache)
            sentAt[h] = Time.realtimeSinceStartup;
            byte[] b = File.ReadAllBytes(f);
            int n = (b.Length + Chunk - 1) / Chunk;
            for (int i = 0; i < n; i++)
                Session.SendAll(new NetWriter(Msg.Job).U8(Session.LocalId).Str("@visagep").Str(h).U16(i).U16(n).Bytes(b, i * Chunk, Mathf.Min(Chunk, b.Length - i * Chunk)), true);
            Log.Info("visage perso : " + h + " envoye a la demande de #" + who + " (" + n + " morceaux)");
        }

        public static void OnPart(int who, string h, int i, int n, byte[] data)
        {
            if (File.Exists(CacheFile(h)) || n <= 0 || n > 800 || i >= n) return;
            Parts p;
            if (!incoming.TryGetValue(h, out p) || p.P.Length != n) incoming[h] = p = new Parts { P = new byte[n][] };
            if (p.P[i] != null) return;
            p.P[i] = data; p.Got++;
            if (p.Got < n) return;
            incoming.Remove(h);
            var all = new List<byte>();
            foreach (byte[] x in p.P) all.AddRange(x);
            byte[] b = all.ToArray();
            if (Hash(b) != h) { Log.Warn("visage perso : " + h + " recu abime"); return; }
            try { Directory.CreateDirectory(CacheDir); File.WriteAllBytes(CacheFile(h), b); } catch (System.Exception e) { Log.Warn("visage perso : " + e.Message); return; }
            made.Remove("perso_" + h);
            Generation++;
            Log.Info("visage perso : " + h + " recu de #" + who + " (" + b.Length + " octets)");
        }

        // ---------------------------------------------------------------- gabarit
        // Une fois par version du mod : la texture du visage des PNJ (visage-jeu.png) et la meme avec le trace des
        // triangles du visage (sous-maillage 2 du corps des PNJ, en rouge : gabarit-visage.png).
        public static void ExportTemplate()
        {
            string tag = Path.Combine(Root, "gabarit-visage.version");
            try { if (File.Exists(tag) && File.ReadAllText(tag) == Version.Text) return; } catch { }
            Material face = Avatar.FindMaterial("char_face01");
            Texture src = face != null ? face.mainTexture : null;
            SkinnedMeshRenderer body = null;
            foreach (SkinnedMeshRenderer s in Resources.FindObjectsOfTypeAll(typeof(SkinnedMeshRenderer)))
                if (s.name == "bodymesh" && s.sharedMesh != null && s.sharedMesh.subMeshCount > 2 && s.sharedMesh.isReadable) { body = s; break; }
            if (src == null) { Log.Info("visage perso : gabarit pas encore possible (visage du jeu introuvable)"); return; }
            int w = src.width, h = src.height;
            var rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32);
            RenderTexture prev = RenderTexture.active;
            Graphics.Blit(src, rt);
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            try
            {
                Directory.CreateDirectory(Root);
                File.WriteAllBytes(Path.Combine(Root, "visage-jeu.png"), tex.EncodeToPNG());
                int tris = 0;
                if (body != null)
                {
                    Vector2[] uv = body.sharedMesh.uv;
                    int[] t = body.sharedMesh.GetTriangles(2);
                    Color red = new Color(1f, 0.15f, 0.1f);
                    for (int i = 0; i + 2 < t.Length; i += 3, tris++)
                        for (int k = 0; k < 3; k++) Line(tex, uv[t[i + k]], uv[t[i + (k + 1) % 3]], red);
                    tex.Apply();
                }
                File.WriteAllBytes(Path.Combine(Root, "gabarit-visage.png"), tex.EncodeToPNG());
                File.WriteAllText(tag, Version.Text);
                Log.Info("visage perso : gabarit ecrit (" + w + "x" + h + ", " + tris + " triangles) dans " + Root);
            }
            catch (System.Exception e) { Log.Warn("visage perso : gabarit : " + e.Message); }
            Object.Destroy(tex);
        }

        static void Line(Texture2D t, Vector2 a, Vector2 b, Color c)
        {
            int w = t.width, h = t.height;
            int x0 = Mathf.RoundToInt(Frac(a.x) * (w - 1)), y0 = Mathf.RoundToInt(Frac(a.y) * (h - 1)), x1 = Mathf.RoundToInt(Frac(b.x) * (w - 1)), y1 = Mathf.RoundToInt(Frac(b.y) * (h - 1));
            int n = Mathf.Max(Mathf.Abs(x1 - x0), Mathf.Abs(y1 - y0), 1);
            for (int i = 0; i <= n; i++) t.SetPixel(x0 + (x1 - x0) * i / n, y0 + (y1 - y0) * i / n, c);
        }
        static float Frac(float v) { v = v - Mathf.Floor(v); return v; }

        static float exportAt = -1;
        public static void OnLevelLoaded() { exportAt = Application.loadedLevelName == "GAME" ? Time.realtimeSinceStartup + 20f : -1; myLooked = false; }
        public static void Update()
        {
            if (exportAt > 0 && Time.realtimeSinceStartup >= exportAt) { exportAt = -1; ExportTemplate(); }
        }
    }
}
