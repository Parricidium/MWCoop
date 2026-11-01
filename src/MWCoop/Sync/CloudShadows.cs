using System.Collections.Generic;
using UnityEngine;

namespace MWCoop
{
    // Ombre des nuages « en grillage » (JD 01/11 : par beau temps, quand un nuage passe au-dessus du joueur, son ombre au sol et sur les
    // voitures est un moire de points). Les nuages du jeu (MSC : MAP/CloudSystem/Clouds/CloudObjects/1..5, materiau « cloud ») ont le
    // shader Standard en mode Fade (_Mode 2, _ALPHABLEND_ON) et projettent leur ombre (TwoSided) : Unity 5 trame l'ombre d'un objet
    // semi-transparent (dithering) -- d'ou le grillage, dans le jeu de base aussi.
    // [Graphismes] OmbresNuages=1 (par defaut) : chaque nuage ne projette plus d'ombre lui-meme ; deux copies invisibles « ombre
    // seulement » (meme maillage, a l'endroit et a l'envers : comme TwoSided) avec le materiau en decoupe (alpha test) projettent une
    // ombre pleine, aux bords adoucis par les ombres douces. OmbresNuages=0 : comme le jeu (copies retirees, ombres d'origine remises).
    public static class CloudShadows
    {
        class Cloud { public Renderer Src; public UnityEngine.Rendering.ShadowCastingMode Was; public MeshRenderer A, B; public Material SrcMat; }
        static readonly List<Cloud> clouds = new List<Cloud>();
        static readonly Dictionary<Material, Material> cutouts = new Dictionary<Material, Material>();
        static readonly Dictionary<Mesh, Mesh> flips = new Dictionary<Mesh, Mesh>();
        static float nextScan;
        static int mode = -1;   // (cutout : 0 inconnu, 1 Standard _ALPHATEST_ON, 2 shader de decoupe herite, 3 aucun)
        static Shader legacyCutout;
        static bool logged;

        public static void OnLevelLoaded() { clouds.Clear(); cutouts.Clear(); flips.Clear(); nextScan = 0f; logged = false; }

        static string Root { get { return Game.Msc ? "MAP/CloudSystem" : "MAP/WEATHER"; } }

        // (toutes les secondes, depuis Gfx.Update)
        public static void Update()
        {
            if (Game.LevelName != "GAME") return;
            bool on = Gfx.Get("OmbresNuages", 1) != 0;
            if (!on) { if (clouds.Count > 0) Restore(); return; }
            float now = Time.realtimeSinceStartup;
            if (now >= nextScan) { nextScan = now + 10f; Scan(); }
            for (int i = clouds.Count - 1; i >= 0; i--)
            {
                Cloud c = clouds[i];
                if (c.Src == null) { if (c.A != null) Object.Destroy(c.A.gameObject); if (c.B != null) Object.Destroy(c.B.gameObject); clouds.RemoveAt(i); continue; }
                // (materiau change par la meteo -- cloud_rain -- ou par World ; teinte et alpha suivis)
                Material sm = c.Src.sharedMaterial;
                if (sm != c.SrcMat) { c.SrcMat = sm; Material cm = Cutout(sm); if (c.A != null) c.A.sharedMaterial = cm; if (c.B != null) c.B.sharedMaterial = cm; }
                Material own = c.A != null ? c.A.sharedMaterial : null;
                if (own != null && sm != null && own != sm && sm.HasProperty("_Color") && own.HasProperty("_Color") && own.color != sm.color) own.color = sm.color;
                bool show = c.Src.enabled;
                if (c.A != null && c.A.enabled != show) c.A.enabled = show;
                if (c.B != null && c.B.enabled != show) c.B.enabled = show;
                if (c.Src.shadowCastingMode != UnityEngine.Rendering.ShadowCastingMode.Off) c.Src.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
        }

        static void Scan()
        {
            GameObject root = Game.FindAny(Root);
            if (root == null) return;
            if (mode < 0) PickMode();
            if (mode == 3) return;
            int added = 0;
            foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true))
            {
                if (!(r is MeshRenderer) || r.name.StartsWith("MWCoop-ombre")) continue;
                Material m = r.sharedMaterial;
                if (m == null || (!m.IsKeywordEnabled("_ALPHABLEND_ON") && !m.IsKeywordEnabled("_ALPHAPREMULTIPLY_ON"))) continue;
                if (r.shadowCastingMode == UnityEngine.Rendering.ShadowCastingMode.Off || r.shadowCastingMode == UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly) continue;
                bool known = false;
                foreach (Cloud k in clouds) if (k.Src == r) { known = true; break; }
                if (known) continue;
                MeshFilter mf = r.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;
                var c = new Cloud { Src = r, Was = r.shadowCastingMode, SrcMat = m };
                Material cm = Cutout(m);
                c.A = Copy(r, mf.sharedMesh, cm, "MWCoop-ombre");
                if (c.Was == UnityEngine.Rendering.ShadowCastingMode.TwoSided) c.B = Copy(r, Flip(mf.sharedMesh), cm, "MWCoop-ombre-envers");
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                clouds.Add(c);
                added++;
            }
            if (added > 0 && !logged) { logged = true; Log.Info("ombres des nuages : " + added + " nuage(s), ombre pleine (" + (mode == 1 ? "Standard en decoupe" : "shader de decoupe herite") + ") au lieu de l'ombre tramee"); }
        }

        static MeshRenderer Copy(Renderer src, Mesh mesh, Material mat, string name)
        {
            var go = new GameObject(name);
            go.layer = src.gameObject.layer;
            go.transform.SetParent(src.transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly;
            mr.receiveShadows = false;
            return mr;
        }

        static Mesh Flip(Mesh m)
        {
            Mesh f;
            if (flips.TryGetValue(m, out f) && f != null) return f;
            f = Object.Instantiate(m);
            f.name = m.name + " (envers)";
            for (int s = 0; s < f.subMeshCount; s++)
            {
                int[] t = f.GetTriangles(s);
                for (int i = 0; i + 2 < t.Length; i += 3) { int a = t[i]; t[i] = t[i + 1]; t[i + 1] = a; }
                f.SetTriangles(t, s);
            }
            flips[m] = f;
            return f;
        }

        // Variante de decoupe disponible dans le jeu : un materiau Standard deja charge avec _ALPHATEST_ON (sa variante d'ombre est dans
        // le jeu), sinon le shader de decoupe herite d'Unity.
        static void PickMode()
        {
            mode = 3;
            foreach (Material m in Resources.FindObjectsOfTypeAll<Material>())
                if (m != null && m.shader != null && m.shader.name == "Standard" && m.IsKeywordEnabled("_ALPHATEST_ON")) { mode = 1; break; }
            if (mode == 3)
            {
                legacyCutout = Shader.Find("Legacy Shaders/Transparent/Cutout/Diffuse") ?? Shader.Find("Transparent/Cutout/Diffuse");
                if (legacyCutout != null) mode = 2;
            }
            if (mode == 3) Log.Warn("ombres des nuages : aucun shader de decoupe dans le jeu, ombres d'origine gardees");
        }

        static Material Cutout(Material src)
        {
            if (src == null) return null;
            Material c;
            if (cutouts.TryGetValue(src, out c) && c != null) return c;
            if (mode == 1)
            {
                c = new Material(src);
                c.SetFloat("_Mode", 1f);
                c.DisableKeyword("_ALPHABLEND_ON"); c.DisableKeyword("_ALPHAPREMULTIPLY_ON"); c.EnableKeyword("_ALPHATEST_ON");
                c.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One); c.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.Zero); c.SetInt("_ZWrite", 1);
                c.renderQueue = 2450;
            }
            else
            {
                c = new Material(legacyCutout);
                if (src.HasProperty("_MainTex")) { c.mainTexture = src.mainTexture; c.mainTextureScale = src.mainTextureScale; c.mainTextureOffset = src.mainTextureOffset; }
                if (src.HasProperty("_Color")) c.color = src.color;
            }
            c.SetFloat("_Cutoff", 0.35f);
            c.name = src.name + " (MWCoop ombre)";
            cutouts[src] = c;
            return c;
        }

        static void Restore()
        {
            foreach (Cloud c in clouds)
            {
                if (c.Src != null) c.Src.shadowCastingMode = c.Was;
                if (c.A != null) Object.Destroy(c.A.gameObject);
                if (c.B != null) Object.Destroy(c.B.gameObject);
            }
            Log.Info("ombres des nuages : " + clouds.Count + " nuage(s) rendus a l'ombre du jeu");
            clouds.Clear(); logged = false; nextScan = 0f;
        }

        // Essais (Autotest=sondeombres) : etat.
        public static string State()
        {
            int n = 0; foreach (Cloud c in clouds) if (c.Src != null) n++;
            return "ombres des nuages : " + (Gfx.Get("OmbresNuages", 1) != 0 ? "corrigees" : "du jeu") + ", " + n + " nuage(s) suivis, decoupe " + mode;
        }
    }
}
