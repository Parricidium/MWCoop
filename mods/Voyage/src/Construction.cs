using System.Collections.Generic;
using UnityEngine;

namespace Voyage
{
    // Constructions en code (comme la boutique et le cinema de MWCoop) : quadrilateres regroupes par matiere en un maillage
    // par matiere, matieres du jeu retrouvees par leur nom (bois, tole, neige...) et recopiees, boites de collision.
    // Coordonnees locales a Root (x a droite, y en haut, z devant) ; UV en metres divises par la tuile de la matiere.
    class Construction
    {
        class Part { public readonly List<Vector3> V = new List<Vector3>(), N = new List<Vector3>(); public readonly List<Vector2> U = new List<Vector2>(); public readonly List<int> T = new List<int>(); }

        public readonly GameObject Root;
        readonly Dictionary<string, Part> parts = new Dictionary<string, Part>();
        readonly Dictionary<string, Material> mats = new Dictionary<string, Material>();
        readonly Dictionary<string, float> tiles = new Dictionary<string, float>();
        readonly List<Bounds> cols = new List<Bounds>();

        public Construction(string name, Vector3 pos, float yaw, Transform parent = null)
        {
            Root = new GameObject(name);
            if (parent != null) Root.transform.SetParent(parent, false);
            Root.transform.position = pos;
            Root.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        }

        // ---------------------------------------------------------------- matieres
        static Dictionary<string, Material> gameMats;
        static Font arial;

        public static Material GameMat(string name)
        {
            if (gameMats == null)
            {
                gameMats = new Dictionary<string, Material>();
                foreach (Material m in Resources.FindObjectsOfTypeAll<Material>()) if (m != null && !gameMats.ContainsKey(m.name)) gameMats[m.name] = m;
            }
            Material r;
            return gameMats.TryGetValue(name, out r) ? r : null;
        }

        static Shader Diffuse { get { return Shader.Find("Legacy Shaders/Diffuse") ?? Shader.Find("Diffuse"); } }

        // matiere du jeu (recopiee, teinte au besoin) ; a defaut, une couleur unie
        public void Mat(string key, string game, float tile, Color? tint = null)
        {
            Material src = GameMat(game);
            Material m = src != null ? new Material(src) : new Material(Diffuse);
            if (src == null) { Journal.Info("matiere du jeu introuvable : " + game); if (m.HasProperty("_Color")) m.color = tint ?? UnityEngine.Color.gray; }
            else if (tint.HasValue && m.HasProperty("_Color")) m.color = tint.Value;
            m.name = "voyage_" + key;
            mats[key] = m; tiles[key] = tile;
        }

        public void Color(string key, Color c)
        {
            Material basis = GameMat("wood_panel7");
            var m = new Material(basis != null ? basis.shader : Diffuse) { name = "voyage_" + key, mainTexture = Texture2D.whiteTexture };
            if (m.HasProperty("_Color")) m.color = c;
            mats[key] = m; tiles[key] = 1f;
        }

        public void Glass(string key, Color c)
        {
            Shader gs = Shader.Find("Legacy Shaders/Transparent/Specular") ?? Shader.Find("Legacy Shaders/Transparent/Diffuse") ?? Shader.Find("Transparent/Diffuse");
            var m = new Material(gs ?? Diffuse) { name = "voyage_" + key, color = c };
            if (m.HasProperty("_SpecColor")) m.SetColor("_SpecColor", new Color(0.6f, 0.6f, 0.6f));
            if (m.HasProperty("_Shininess")) m.SetFloat("_Shininess", 0.9f);
            mats[key] = m; tiles[key] = 1f;
        }

        // texture tiree au hasard entre deux couleurs (bruit de Perlin a deux octaves), repetee tous les 'tile' m
        public void Noise(string key, Color a, Color b, float tile, int seed)
        {
            const int n = 128;
            var tex = new Texture2D(n, n, TextureFormat.RGB24, true) { name = "voyage_" + key, wrapMode = TextureWrapMode.Repeat, anisoLevel = 4 };
            var px = new Color[n * n];
            float o = seed * 17.3f;
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    // (bruit periodique : echantillonne sur un tore pour que la tuile se raccorde)
                    float u = x / (float)n * Mathf.PI * 2f, v = y / (float)n * Mathf.PI * 2f;
                    float t = Mathf.PerlinNoise(o + Mathf.Cos(u) * 2f + 10f, o + Mathf.Sin(u) * 2f + Mathf.Cos(v) * 2f) * 0.65f
                            + Mathf.PerlinNoise(o + Mathf.Cos(u) * 7f + 30f, o + Mathf.Sin(v) * 7f + Mathf.Sin(u) * 3f) * 0.35f;
                    px[y * n + x] = UnityEngine.Color.Lerp(a, b, Mathf.Clamp01((t - 0.25f) * 2f));
                }
            tex.SetPixels(px); tex.Apply(true);
            Own(key, tex);
            tiles[key] = tile;
        }

        // grillage a mailles en losange (fils gris, transparent entre les fils : decoupe alpha), repete tous les 'tile' m
        public void ChainLink(string key, float tile)
        {
            const int n = 64, w = 5;   // (fils plus epais : la maille est petite)
            var tex = new Texture2D(n, n, TextureFormat.ARGB32, true) { name = "voyage_" + key, wrapMode = TextureWrapMode.Repeat, anisoLevel = 4 };
            var px = new Color[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    int d1 = ((x + y) % n + n) % n, d2 = ((x - y) % n + n) % n;
                    bool wire = d1 < w || d2 < w;
                    float shade = 0.62f + 0.12f * ((x * 7 + y * 3) % 5) / 5f;
                    px[y * n + x] = wire ? new Color(shade, shade, shade * 1.02f, 1f) : new Color(0.5f, 0.5f, 0.5f, 0f);
                }
            tex.SetPixels(px); tex.Apply(true);
            Shader sh = Shader.Find("Legacy Shaders/Transparent/Cutout/Diffuse") ?? Shader.Find("Transparent/Cutout/Diffuse") ?? Diffuse;
            var m = new Material(sh) { name = "voyage_" + key, mainTexture = tex };
            if (m.HasProperty("_Cutoff")) m.SetFloat("_Cutoff", 0.35f);
            mats[key] = m; tiles[key] = tile;
        }

        public void Own(string key, Texture2D tex)
        {
            Material basis = GameMat("wood_panel7");
            var m = new Material(basis != null ? basis.shader : Diffuse) { name = "voyage_" + key, mainTexture = tex };
            if (m.HasProperty("_Color")) m.color = UnityEngine.Color.white;
            mats[key] = m; tiles[key] = 1f;
        }

        // ---------------------------------------------------------------- geometrie
        Part P(string key) { Part p; if (!parts.TryGetValue(key, out p)) parts[key] = p = new Part(); return p; }

        // quadrilatere a (bas gauche), b (bas droite), c (haut droite), d (haut gauche) vu de face
        public void Quad(string key, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector2[] uv = null)
        {
            if (key == null) return;
            Part p = P(key);
            Vector3 ud = (b - a).normalized, vd = (d - a).normalized, n = Vector3.Cross(vd, ud).normalized;
            float t = tiles.ContainsKey(key) ? tiles[key] : 1f;
            int i0 = p.V.Count;
            Vector3[] q = { a, b, c, d };
            for (int i = 0; i < 4; i++)
            {
                p.V.Add(q[i]); p.N.Add(n);
                p.U.Add(uv != null ? uv[i] : new Vector2(Vector3.Dot(q[i], ud) / t, Vector3.Dot(q[i], vd) / t));
            }
            p.T.Add(i0); p.T.Add(i0 + 3); p.T.Add(i0 + 2);
            p.T.Add(i0); p.T.Add(i0 + 2); p.T.Add(i0 + 1);
        }

        public void Tri(string key, Vector3 a, Vector3 b, Vector3 c)   // a, b, c dans le sens des aiguilles vu de face
        {
            Part p = P(key);
            Vector3 n = Vector3.Cross(b - a, c - a).normalized;
            Vector3 ud = Vector3.Cross(Vector3.up, -n).normalized; if (ud == Vector3.zero) ud = Vector3.right;
            float t = tiles.ContainsKey(key) ? tiles[key] : 1f;
            int i0 = p.V.Count;
            foreach (Vector3 v in new[] { a, b, c }) { p.V.Add(v); p.N.Add(n); p.U.Add(new Vector2(Vector3.Dot(v, ud) / t, v.y / t)); }
            p.T.Add(i0); p.T.Add(i0 + 1); p.T.Add(i0 + 2);
        }

        // boite alignee ; faces : +x, -x, +y, -y, +z, -z (null = pas de face)
        public void Box(Vector3 mn, Vector3 mx, string px, string nx, string py, string ny, string pz, string nz, bool collide = true)
        {
            Quad(px, new Vector3(mx.x, mn.y, mn.z), new Vector3(mx.x, mn.y, mx.z), new Vector3(mx.x, mx.y, mx.z), new Vector3(mx.x, mx.y, mn.z));
            Quad(nx, new Vector3(mn.x, mn.y, mx.z), new Vector3(mn.x, mn.y, mn.z), new Vector3(mn.x, mx.y, mn.z), new Vector3(mn.x, mx.y, mx.z));
            Quad(py, new Vector3(mn.x, mx.y, mn.z), new Vector3(mx.x, mx.y, mn.z), new Vector3(mx.x, mx.y, mx.z), new Vector3(mn.x, mx.y, mx.z));
            Quad(ny, new Vector3(mn.x, mn.y, mx.z), new Vector3(mx.x, mn.y, mx.z), new Vector3(mx.x, mn.y, mn.z), new Vector3(mn.x, mn.y, mn.z));
            Quad(pz, new Vector3(mx.x, mn.y, mx.z), new Vector3(mn.x, mn.y, mx.z), new Vector3(mn.x, mx.y, mx.z), new Vector3(mx.x, mx.y, mx.z));
            Quad(nz, new Vector3(mn.x, mn.y, mn.z), new Vector3(mx.x, mn.y, mn.z), new Vector3(mx.x, mx.y, mn.z), new Vector3(mn.x, mx.y, mn.z));
            if (collide) cols.Add(new Bounds((mn + mx) / 2, mx - mn));
        }
        public void Box(Vector3 mn, Vector3 mx, string all, bool collide = true) { Box(mn, mx, all, all, all, all, all, all, collide); }

        // boite tournee : centre, rotation, taille (sans collision)
        public void OBox(Vector3 c, Quaternion r, Vector3 s, string top, string bottom, string sides)
        {
            Vector3 h = s / 2;
            System.Func<float, float, float, Vector3> pt = (x, y, z) => c + r * new Vector3(x * h.x, y * h.y, z * h.z);
            Quad(sides, pt(1, -1, -1), pt(1, -1, 1), pt(1, 1, 1), pt(1, 1, -1));
            Quad(sides, pt(-1, -1, 1), pt(-1, -1, -1), pt(-1, 1, -1), pt(-1, 1, 1));
            Quad(top, pt(-1, 1, -1), pt(1, 1, -1), pt(1, 1, 1), pt(-1, 1, 1));
            Quad(bottom, pt(-1, -1, 1), pt(1, -1, 1), pt(1, -1, -1), pt(-1, -1, -1));
            Quad(sides, pt(1, -1, 1), pt(-1, -1, 1), pt(-1, 1, 1), pt(1, 1, 1));
            Quad(sides, pt(-1, -1, -1), pt(1, -1, -1), pt(1, 1, -1), pt(-1, 1, -1));
        }

        // cylindre vertical (poteau, tronc) : n cotes, rayon r, du pied y0 au sommet y1
        public void Cylinder(string key, Vector3 foot, float r0, float r1, float height, int n = 8, bool collide = false)
        {
            for (int i = 0; i < n; i++)
            {
                float a0 = i * Mathf.PI * 2f / n, a1 = (i + 1) * Mathf.PI * 2f / n;
                Vector3 d0 = new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0)), d1 = new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1));
                Quad(key, foot + d1 * r0, foot + d0 * r0, foot + d0 * r1 + Vector3.up * height, foot + d1 * r1 + Vector3.up * height);
            }
            if (collide) cols.Add(new Bounds(foot + Vector3.up * height / 2, new Vector3(r0 * 1.6f, height, r0 * 1.6f)));
        }

        public void Collider(Vector3 mn, Vector3 mx) { cols.Add(new Bounds((mn + mx) / 2, mx - mn)); }

        // texte 3D (Arial d'Unity) tourne vers -z local (lisible depuis l'avant d'un mur qui regarde -z)
        public void Text(string text, Vector3 at, float yaw, float size, Color c)
        {
            if (arial == null)
            {
                arial = Resources.GetBuiltinResource<Font>("Arial.ttf");
                if (arial != null) arial.textureRebuildCallback += () => lettersDirty = true;   // (atlas refait : lettres a recopier tout de suite)
            }
            if (arial == null) return;
            var g = new GameObject("voyage_texte");
            g.transform.SetParent(Root.transform, false);
            g.transform.localPosition = at;
            g.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            var tm = g.AddComponent<TextMesh>();
            tm.font = arial; tm.fontSize = 64; tm.characterSize = size / 6f; tm.anchor = TextAnchor.MiddleCenter; tm.alignment = TextAlignment.Center;
            tm.fontStyle = FontStyle.Bold; tm.color = c; tm.text = text;
            arial.RequestCharactersInTexture(text, 64, FontStyle.Bold);
            // Pas le materiau d'Arial (GUI/Text Shader : il se dessine a travers tout). L'atlas de la police n'a que
            // l'alpha (lettres noires sous un shader ordinaire) : recopie en lettres blanches (Letters), teintees par la matiere.
            // (non eclaire : le maillage d'un TextMesh n'a pas de normales, un shader eclaire assombrissait une lettre sur deux ;
            // Sprites/Default prend la couleur des sommets, celle du TextMesh ; pas Particles/* : particules douces, effacees
            // pres d'une surface -- le texte est a 1 cm de son panneau)
            Shader sh = Shader.Find("Sprites/Default") ?? Shader.Find("Unlit/Transparent") ?? arial.material.shader;
            var tmat = new Material(sh) { name = "voyage_texte", mainTexture = Letters(true), color = UnityEngine.Color.white };
            g.GetComponent<MeshRenderer>().sharedMaterial = tmat;
            texts.Add(g.GetComponent<MeshRenderer>().sharedMaterial);
        }
        static readonly List<Material> texts = new List<Material>();
        static RenderTexture letters;
        static Material letterBlit;
        static float nextLetters;
        static bool lettersDirty;

        // atlas d'Arial en lettres blanches sur fond transparent (blit par GUI/Text Shader, blanc) ; refait avec l'atlas
        static Texture Letters(bool now)
        {
            Texture src = arial.material.mainTexture;
            if (src == null) return null;
            if (letters == null || letters.width != src.width || letters.height != src.height)
            {
                if (letters != null) letters.Release();
                letters = new RenderTexture(src.width, src.height, 0, RenderTextureFormat.ARGB32) { name = "voyage_lettres", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
                foreach (Material m in texts) if (m != null) m.mainTexture = letters;
                now = true;
            }
            if (now || lettersDirty || Time.realtimeSinceStartup >= nextLetters)
            {
                nextLetters = Time.realtimeSinceStartup + 2f;
                lettersDirty = false;
                if (letterBlit == null) letterBlit = new Material(arial.material) { name = "voyage_lettres", color = UnityEngine.Color.white };
                RenderTexture keep = RenderTexture.active;
                RenderTexture.active = letters;
                GL.Clear(true, true, new Color(1f, 1f, 1f, 0f));
                RenderTexture.active = keep;
                Graphics.Blit(src, letters, letterBlit);
            }
            return letters;
        }

        // l'atlas d'Arial est refait par la police quand d'autres textes s'affichent : on suit
        public static void TickTexts(bool now = false)
        {
            if (arial == null || texts.Count == 0) return;
            texts.RemoveAll(m => m == null);
            Letters(now);
        }

        public Light Lamp(Vector3 at, Color c, float intensity, float range)
        {
            var g = new GameObject("voyage_lampe");
            g.transform.SetParent(Root.transform, false); g.transform.localPosition = at;
            var l = g.AddComponent<Light>(); l.type = LightType.Point; l.color = c; l.intensity = intensity; l.range = range; l.shadows = LightShadows.None;
            return l;
        }

        // maillages (un par matiere) et collisions sous Root
        public void Flush()
        {
            foreach (var kv in parts)
            {
                if (kv.Value.V.Count == 0) continue;
                var g = new GameObject("voyage_" + kv.Key);
                g.transform.SetParent(Root.transform, false);
                var m = new Mesh { name = g.name };
                m.vertices = kv.Value.V.ToArray(); m.normals = kv.Value.N.ToArray(); m.uv = kv.Value.U.ToArray(); m.triangles = kv.Value.T.ToArray();
                m.RecalculateBounds();
                g.AddComponent<MeshFilter>().sharedMesh = m;
                var r = g.AddComponent<MeshRenderer>();
                Material mat;
                r.sharedMaterial = mats.TryGetValue(kv.Key, out mat) ? mat : null;
                if (mat != null && mat.shader != null && mat.shader.name.Contains("Transparent")) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            if (cols.Count > 0)
            {
                var cg = new GameObject("voyage_collisions");
                cg.transform.SetParent(Root.transform, false);
                foreach (Bounds b in cols)
                {
                    var c = new GameObject("c");
                    c.transform.SetParent(cg.transform, false);
                    c.transform.localPosition = b.center;
                    c.AddComponent<BoxCollider>().size = b.size;
                }
            }
            parts.Clear(); cols.Clear();
        }

        // maillage libre (sol de l'ile) avec sa collision exacte
        public GameObject MeshObject(string key, Vector3[] v, Vector2[] uv, int[] t, bool collide)
        {
            var g = new GameObject("voyage_" + key);
            g.transform.SetParent(Root.transform, false);
            var m = new Mesh { name = g.name };
            m.vertices = v; m.uv = uv; m.triangles = t;
            m.RecalculateNormals(); m.RecalculateBounds();
            g.AddComponent<MeshFilter>().sharedMesh = m;
            Material mat;
            g.AddComponent<MeshRenderer>().sharedMaterial = mats.TryGetValue(key, out mat) ? mat : null;
            if (collide) g.AddComponent<MeshCollider>().sharedMesh = m;
            return g;
        }
    }
}
