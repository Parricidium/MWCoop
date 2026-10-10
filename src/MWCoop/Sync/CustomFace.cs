using System.Collections.Generic;
using System.IO;
using MWCoop.Net;
using UnityEngine;

namespace MWCoop
{
    // Images importees (demande de JD, 10/10 -- « comme Arx Fatalis ») : le joueur importe une image dans le lanceur (onglet
    // TENUE : %LOCALAPPDATA%\MWCoop\visage.jpg, haut.jpg, pantalon.jpg, peintes sur le gabarit de la texture du jeu) et
    // choisit « Visage perso » / « Haut perso » / « Pantalon perso » dans la liste ([Coop] Visage / Apparence / Pantalon =
    // "perso"). Dans la chaine d'apparence la partie devient "perso_<empreinte>" (visage), "persoh_" (haut), "persop_"
    // (pantalon) ; chez les autres, l'image est demandee (@visage?) a qui l'a, recue par morceaux (@visagep, fiables, 900
    // octets), gardee en cache (%LOCALAPPDATA%\MWCoop\visages\<empreinte>.jpg) et posee sur une copie de la matiere du jeu
    // (char_face01, char_shirt01, char_pants01, comme les tenues offertes). [Coop] VoirVisages=0 : celles des autres non.
    // Gabarits (ExportTemplate) : la texture du jeu, et la meme avec le trace des coutures (UV) de la partie du corps des
    // PNJ, ecrites dans %LOCALAPPDATA%\MWCoop (gabarit-visage.png, gabarit-haut.png, gabarit-pantalon.png, et *-jeu.png).
    public static class CustomFace
    {
        public const int Face = 0, Shirt = 1, Pants = 2;
        static readonly string[] Prefix = { "perso_", "persoh_", "persop_" };
        static readonly string[] FileName = { "visage", "haut", "pantalon" };
        static readonly string[] Template = { "char_face01", "char_shirt01", "char_pants01" };
        static readonly string[] CfgKey = { "Visage", "Apparence", "Pantalon" };
        static readonly string[] MatPrefix = { "char_face", "char_shirt", "char_pants" };

        public static int Generation;   // change quand une image arrive : les avatars reprennent leur apparence
        const int Chunk = 900;
        static readonly Dictionary<string, Material> made = new Dictionary<string, Material>();
        static readonly Dictionary<string, float> asked = new Dictionary<string, float>(), sentAt = new Dictionary<string, float>();
        class Parts { public byte[][] P; public int Got; }
        static readonly Dictionary<string, Parts> incoming = new Dictionary<string, Parts>();
        static readonly string[] myKey = new string[3];
        static readonly bool[] myLooked = new bool[3];

        // ([Test] RacineVisages : un dossier par instance d'essai, qui partagent sinon le meme LOCALAPPDATA)
        static string Root { get { string o = Config.Get("Test", "RacineVisages", ""); return o.Length > 0 ? o : Path.Combine(System.Environment.GetEnvironmentVariable("LOCALAPPDATA") ?? ".", "MWCoop"); } }
        static string CacheDir { get { return Path.Combine(Root, "visages"); } }
        static string MyFile(int k) { return Path.Combine(Root, FileName[k] + ".jpg"); }
        static string CacheFile(string hash) { return Path.Combine(CacheDir, hash + ".jpg"); }

        public static int KindOf(string name)
        {
            if (string.IsNullOrEmpty(name)) return -1;
            for (int k = 0; k < 3; k++) if (name.StartsWith(Prefix[k])) return k;
            return -1;
        }
        public static bool IsCustom(string name) { return KindOf(name) >= 0; }

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

        // La partie k reglee sur "perso" ([Coop] Visage / Apparence / Pantalon) ?
        public static bool Chosen(int k) { return Config.Get("Coop", CfgKey[k], "") == "perso"; }

        // Cle de l'image du joueur local pour la partie k ("perso_<empreinte>"...), ou null (pas choisie, pas d'image).
        public static string MyKeyOf(int k)
        {
            if (!Chosen(k)) return null;
            if (myLooked[k]) return myKey[k];
            myLooked[k] = true;
            myKey[k] = null;
            try
            {
                if (!File.Exists(MyFile(k))) { Log.Warn(FileName[k] + " perso : choisi, mais pas d'image (" + MyFile(k) + ")"); return null; }
                byte[] b = File.ReadAllBytes(MyFile(k));
                if (b.Length < 100 || b.Length > 2000000) { Log.Warn(FileName[k] + " perso : image refusee (" + b.Length + " octets)"); return null; }
                string h = Hash(b);
                Directory.CreateDirectory(CacheDir);
                if (!File.Exists(CacheFile(h))) File.WriteAllBytes(CacheFile(h), b);
                myKey[k] = Prefix[k] + h;
                Log.Info(FileName[k] + " perso : " + myKey[k] + " (" + b.Length + " octets)");
            }
            catch (System.Exception e) { Log.Warn(FileName[k] + " perso : " + e.Message); }
            return myKey[k];
        }

        public static bool HasImage(int k) { try { return File.Exists(MyFile(k)); } catch { return false; } }
        public static void Forget() { for (int k = 0; k < 3; k++) myLooked[k] = false; }

        static bool Mine(string name) { for (int k = 0; k < 3; k++) if (MyKeyOf(k) == name) return true; return false; }

        public static Material Make(string name)
        {
            int k = KindOf(name);
            if (k < 0) return null;
            Material m;
            if (made.TryGetValue(name, out m) && m != null) return m;
            if (Config.GetInt("Coop", "VoirVisages", 1) == 0 && !Mine(name)) return null;
            string h = name.Substring(Prefix[k].Length), f = CacheFile(h);
            if (!File.Exists(f)) { Ask(h); return null; }
            Material tpl = Avatar.FindMaterial(Template[k]);
            if (tpl == null) return null;
            try
            {
                var tex = new Texture2D(2, 2, TextureFormat.RGB24, true);
                if (!tex.LoadImage(File.ReadAllBytes(f))) { Object.Destroy(tex); Log.Warn(FileName[k] + " perso : " + h + " illisible"); return null; }
                tex.name = name;
                m = new Material(tpl) { name = name, mainTexture = tex };
                made[name] = m;
                Log.Info(FileName[k] + " perso : " + name + " pose (" + tex.width + "x" + tex.height + ")");
            }
            catch (System.Exception e) { Log.Warn(FileName[k] + " perso : " + e.Message); }
            return m;
        }

        static void Ask(string h)
        {
            float t;
            if (!Session.Active || (asked.TryGetValue(h, out t) && Time.realtimeSinceStartup - t < 30f)) return;
            asked[h] = Time.realtimeSinceStartup;
            Session.SendAll(new NetWriter(Msg.Job).U8(Session.LocalId).Str("@visage?").Str(h), true);
            Log.Info("image perso : " + h + " demandee");
        }

        public static void OnAsk(int who, string h)
        {
            float t;
            string f = CacheFile(h);
            if (!File.Exists(f) || (sentAt.TryGetValue(h, out t) && Time.realtimeSinceStartup - t < 20f)) return;
            bool mine = false;
            for (int k = 0; k < 3; k++) if (MyKeyOf(k) == Prefix[k] + h) mine = true;
            if (!Session.IsHost && !mine) return;   // (seul celui qui l'a importee repond, et l'hote qui l'a en cache)
            sentAt[h] = Time.realtimeSinceStartup;
            byte[] b = File.ReadAllBytes(f);
            int n = (b.Length + Chunk - 1) / Chunk;
            for (int i = 0; i < n; i++)
                Session.SendAll(new NetWriter(Msg.Job).U8(Session.LocalId).Str("@visagep").Str(h).U16(i).U16(n).Bytes(b, i * Chunk, Mathf.Min(Chunk, b.Length - i * Chunk)), true);
            Log.Info("image perso : " + h + " envoyee a la demande de #" + who + " (" + n + " morceaux)");
        }

        public static void OnPart(int who, string h, int i, int n, byte[] data)
        {
            if (File.Exists(CacheFile(h)) || n <= 0 || n > 2500 || i >= n) return;
            Parts p;
            if (!incoming.TryGetValue(h, out p) || p.P.Length != n) incoming[h] = p = new Parts { P = new byte[n][] };
            if (p.P[i] != null) return;
            p.P[i] = data; p.Got++;
            if (p.Got < n) return;
            incoming.Remove(h);
            var all = new List<byte>();
            foreach (byte[] x in p.P) all.AddRange(x);
            byte[] b = all.ToArray();
            if (Hash(b) != h) { Log.Warn("image perso : " + h + " recue abimee"); return; }
            try { Directory.CreateDirectory(CacheDir); File.WriteAllBytes(CacheFile(h), b); } catch (System.Exception e) { Log.Warn("image perso : " + e.Message); return; }
            foreach (string pre in Prefix) made.Remove(pre + h);
            Generation++;
            Log.Info("image perso : " + h + " recue de #" + who + " (" + b.Length + " octets)");
        }

        // ---------------------------------------------------------------- gabarits
        // Une fois par version du mod : pour le visage, le haut et le pantalon, la texture du jeu (<partie>-jeu.png) et la
        // meme avec le trace des triangles de cette partie du corps des PNJ, en rouge (gabarit-<partie>.png).
        public static void ExportTemplate()
        {
            string tag = Path.Combine(Root, "gabarits.version");
            try { if (File.Exists(tag) && File.ReadAllText(tag) == Version.Text) return; } catch { }
            SkinnedMeshRenderer body = null;
            foreach (SkinnedMeshRenderer s in Resources.FindObjectsOfTypeAll(typeof(SkinnedMeshRenderer)))
                if (s.name == "bodymesh" && s.sharedMesh != null && s.sharedMesh.subMeshCount > 2 && s.sharedMesh.isReadable) { body = s; break; }
            int done = 0;
            for (int k = 0; k < 3; k++) if (ExportOne(k, body)) done++;
            if (done < 3) { Log.Info("image perso : gabarits pas encore tous possibles (" + done + "/3)"); return; }
            try { File.WriteAllText(tag, Version.Text); } catch { }
        }

        static bool ExportOne(int k, SkinnedMeshRenderer body)
        {
            Material mat = Avatar.FindMaterial(Template[k]);
            Texture src = mat != null ? mat.mainTexture : null;
            if (src == null) return false;
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
            bool ok = false;
            try
            {
                Directory.CreateDirectory(Root);
                File.WriteAllBytes(Path.Combine(Root, FileName[k] + "-jeu.png"), tex.EncodeToPNG());
                int tris = 0, sub = -1;
                if (body != null)
                {
                    // sous-maillage de cette partie : celui dont la matiere est un visage / haut / pantalon de PNJ
                    Material[] ms = body.sharedMaterials;
                    for (int i = 0; i < ms.Length && i < body.sharedMesh.subMeshCount; i++)
                        if (ms[i] != null && ms[i].name.StartsWith(MatPrefix[k])) { sub = i; break; }
                    if (sub < 0) sub = k == Face ? 2 : k == Shirt ? 0 : 1;
                    Vector2[] uv = body.sharedMesh.uv;
                    int[] t = body.sharedMesh.GetTriangles(sub);
                    Color red = new Color(1f, 0.15f, 0.1f);
                    for (int i = 0; i + 2 < t.Length; i += 3, tris++)
                        for (int j = 0; j < 3; j++) Line(tex, uv[t[i + j]], uv[t[i + (j + 1) % 3]], red);
                    tex.Apply();
                }
                File.WriteAllBytes(Path.Combine(Root, "gabarit-" + FileName[k] + ".png"), tex.EncodeToPNG());
                Log.Info(FileName[k] + " perso : gabarit ecrit (" + w + "x" + h + ", sous-maillage " + sub + ", " + tris + " triangles) dans " + Root);
                ok = true;
            }
            catch (System.Exception e) { Log.Warn(FileName[k] + " perso : gabarit : " + e.Message); }
            Object.Destroy(tex);
            return ok;
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
        public static void OnLevelLoaded()
        {
            exportAt = Application.loadedLevelName == "GAME" ? Time.realtimeSinceStartup + 20f : -1;
            for (int k = 0; k < 3; k++) myLooked[k] = false;
        }
        public static void Update()
        {
            if (exportAt > 0 && Time.realtimeSinceStartup >= exportAt) { exportAt = -1; ExportTemplate(); }
        }
    }
}
