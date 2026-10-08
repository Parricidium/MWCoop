using System.Collections.Generic;
using UnityEngine;

namespace MWCoop
{
    // Lumieres (demande de JD, 07/10 : « comme sur GTA VC coop, les phares, les lampadaires et les ombres qui vont avec »).
    // Reglages [Graphismes] (lanceur, F10) ; absents : rien ne change.
    //  - OmbresPhares (0, 2, 4, 8) : les N phares allumes les plus proches de la camera (projecteurs des vehicules : les
    //    notres et ceux de la circulation) projettent des ombres douces ; aucun n'en avait.
    //  - OmbresLampadaires (0, 1, 2, 4) : pareil pour les lampadaires (MAP/StreetLights...), a moins de 80 m.
    //  - Halos (0, 1 discrets, 2 marques) : halo doux, comme les « coronas » de GTA, sur les phares (vus de face), les feux
    //    rouges arriere et les lampadaires allumes ; plus visibles la nuit, caches derriere un obstacle (rayon vers la
    //    camera), un peu plus grands au loin pour rester visibles.
    //  - PorteePhares (0 normale, 1 longue) : portee x1,5 et intensite x1,25 des phares.
    // Rien n'est envoye : chacun son affichage (les phares des autres sont allumes chez lui par CarVisuals).
    public static class Lights
    {
        const int K_BEAM = 0, K_TAIL = 1, K_STREET = 2;
        class L
        {
            public Light Li; public int Kind; public Transform Root;
            public LightShadows Orig; public bool Shadowed;
            public float OrigRange, OrigInt, SetRange, SetInt; public bool Boosted;
            public Halo H;
        }
        class Halo { public GameObject Go; public Material M; public float A; }

        static readonly List<L> all = new List<L>();
        static float scanAt = -1, nextScan;
        static Light sun;
        static Shader haloShader;
        static Texture2D haloTex;
        static Mesh quad;
        static int lastShadowsBeam = -1, lastShadowsStreet = -1, scanGen = -1;

        public static void OnLevelLoaded()
        {
            foreach (L l in all) if (l.H != null && l.H.Go != null) Object.Destroy(l.H.Go);
            all.Clear(); sun = null;
            scanAt = Application.loadedLevelName == "GAME" ? Time.realtimeSinceStartup + 12f : -1;
        }

        static void Scan()
        {
            var known = new Dictionary<Light, L>();
            foreach (L l in all) if (l.Li != null) known[l.Li] = l;
            var roots = new HashSet<Transform>();
            foreach (GameObject r in Recon.SceneRoots()) roots.Add(r.transform);
            all.Clear();
            int beams = 0, tails = 0, streets = 0;
            foreach (Object o in Resources.FindObjectsOfTypeAll(typeof(Light)))
            {
                var li = (Light)o;
                if (li == null || li.hideFlags != HideFlags.None || !roots.Contains(li.transform.root)) continue;
                L l;
                if (known.TryGetValue(li, out l)) { all.Add(l); continue; }
                if (li.type == LightType.Directional) { if (li.name == "SUN") sun = li; continue; }
                Transform car = CarOf(li.transform);
                string path = Recon.Path(li.transform);
                int kind = -1;
                string lp = path.ToLowerInvariant();
                bool inside = lp.Contains("interior") || lp.Contains("dash") || lp.Contains("gauge") || lp.Contains("indicator") || lp.Contains("dome");
                if (car != null && li.type == LightType.Spot && !inside) kind = K_BEAM;
                else if (car != null && li.type == LightType.Point && !inside && li.color.r > 0.8f && li.color.g < 0.35f && li.color.b < 0.35f
                         && (lp.Contains("brake") || lp.Contains("rear") || lp.Contains("tail"))) kind = K_TAIL;
                else if (path.Contains("StreetLights") || li.name == "Street Light") kind = K_STREET;
                if (kind < 0) continue;
                l = new L { Li = li, Kind = kind, Root = car ?? li.transform.root, Orig = li.shadows, OrigRange = li.range, OrigInt = li.intensity };
                all.Add(l);
                if (kind == K_BEAM) beams++; else if (kind == K_TAIL) tails++; else streets++;
            }
            if (beams + tails + streets > 0) Log.Info("lumieres : " + all.Count + " suivies (" + beams + " phares, " + tails + " feux arriere, " + streets + " lampadaires de plus), halos " + (HaloShader() != null ? HaloShader().name : "SANS SHADER"));
        }

        static Transform CarOf(Transform t)
        {
            for (Transform p = t; p != null; p = p.parent) if (p.GetComponent("CarDynamics") != null) return p;
            return null;
        }

        static bool On(Light li) { return li != null && li.enabled && li.gameObject.activeInHierarchy && li.intensity > 0.01f; }

        public static void LateUpdate()
        {
            if (scanAt < 0) return;
            float now = Time.realtimeSinceStartup;
            // (releve : 12 s apres le chargement, puis chaque minute ou des qu'une voiture apparait -- FindObjectsOfTypeAll coute)
            if (now >= scanAt && (now >= nextScan || scanGen != VehicleSync.Generation)) { nextScan = now + 60f; scanGen = VehicleSync.Generation; Scan(); }
            if (all.Count == 0) return;
            Camera cam = Camera.main;
            if (cam == null) return;
            Vector3 cp = cam.transform.position;
            int nBeam = Gfx.Get("OmbresPhares", 0), nStreet = Gfx.Get("OmbresLampadaires", 0), halos = Gfx.Get("Halos", 0);
            bool longBeams = Gfx.Get("PorteePhares", 0) == 1;
            Shadows(K_BEAM, nBeam, 120f, cp);
            Shadows(K_STREET, nStreet, 80f, cp);
            if (nBeam != lastShadowsBeam || nStreet != lastShadowsStreet)
            {
                lastShadowsBeam = nBeam; lastShadowsStreet = nStreet;
                Log.Info("lumieres : ombres des " + nBeam + " phares et " + nStreet + " lampadaires les plus proches, halos " + halos + ", phares " + (longBeams ? "longs" : "normaux"));
            }
            foreach (L l in all) if (l.Kind == K_BEAM) Boost(l, longBeams);
            Halos(cam, halos);
        }

        // Les n lumieres allumees les plus proches (de ce genre, a moins de 'max' m) : ombres douces ; les autres : comme le jeu.
        static readonly List<KeyValuePair<float, L>> near = new List<KeyValuePair<float, L>>();
        static void Shadows(int kind, int n, float max, Vector3 cp)
        {
            near.Clear();
            if (n > 0)
                foreach (L l in all)
                    if (l.Kind == kind && On(l.Li))
                    {
                        float d = (l.Li.transform.position - cp).sqrMagnitude;
                        if (d < max * max) near.Add(new KeyValuePair<float, L>(d, l));
                    }
            near.Sort((a, b) => a.Key.CompareTo(b.Key));
            var chosen = new HashSet<L>();
            for (int i = 0; i < near.Count && i < n; i++) chosen.Add(near[i].Value);
            foreach (L l in all)
            {
                if (l.Kind != kind || l.Li == null) continue;
                bool want = chosen.Contains(l);
                if (want == l.Shadowed) continue;
                l.Shadowed = want;
                l.Li.shadows = want ? LightShadows.Soft : l.Orig;
                if (want) { l.Li.shadowStrength = 0.85f; l.Li.shadowBias = 0.05f; }
            }
        }

        // Portee longue : x1,5 (intensite x1,25) ; si le jeu change lui-meme la portee ou l'intensite, c'est sa nouvelle base.
        static void Boost(L l, bool on)
        {
            Light li = l.Li;
            if (li == null) return;
            if (l.Boosted && (Mathf.Abs(li.range - l.SetRange) > 0.01f || Mathf.Abs(li.intensity - l.SetInt) > 0.001f)) { l.OrigRange = li.range; l.OrigInt = li.intensity; l.Boosted = false; }
            if (on == l.Boosted) return;
            l.Boosted = on;
            li.range = on ? l.OrigRange * 1.5f : l.OrigRange;
            li.intensity = on ? l.OrigInt * 1.25f : l.OrigInt;
            l.SetRange = li.range; l.SetInt = li.intensity;
        }

        // ---------------------------------------------------------------- halos
        static Shader HaloShader()
        {
            if (haloShader != null) return haloShader;
            foreach (string s in new[] { "Particles/Additive", "Legacy Shaders/Particles/Additive", "Mobile/Particles/Additive", "Particles/Additive (Soft)", "Sprites/Default" })
            {
                haloShader = Shader.Find(s);
                if (haloShader != null) break;
            }
            return haloShader;
        }

        static Texture2D HaloTex()
        {
            if (haloTex != null) return haloTex;
            const int N = 64;
            haloTex = new Texture2D(N, N, TextureFormat.ARGB32, false) { wrapMode = TextureWrapMode.Clamp, name = "MWCoop-Halo" };
            var px = new Color[N * N];
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float dx = (x + 0.5f) / N * 2f - 1f, dy = (y + 0.5f) / N * 2f - 1f, r = Mathf.Sqrt(dx * dx + dy * dy);
                    // coeur brillant, halo qui s'eteint en douceur, fines branches en croix (comme les feux de GTA)
                    float a = Mathf.Clamp01(1f - r);
                    a = Mathf.Pow(a, 1.7f) * 0.85f + Mathf.Pow(Mathf.Clamp01(1f - r * 3.5f), 1.5f) * 0.8f;
                    a += Mathf.Clamp01(1f - r) * (Mathf.Clamp01(1f - Mathf.Abs(dx) * 22f) + Mathf.Clamp01(1f - Mathf.Abs(dy) * 22f)) * 0.18f;
                    a = Mathf.Clamp01(a);
                    px[y * N + x] = new Color(a, a, a, a);
                }
            haloTex.SetPixels(px);
            haloTex.Apply();
            return haloTex;
        }

        static Mesh Quad()
        {
            if (quad != null) return quad;
            quad = new Mesh { name = "MWCoop-Halo" };
            quad.vertices = new[] { new Vector3(-0.5f, -0.5f, 0), new Vector3(0.5f, -0.5f, 0), new Vector3(0.5f, 0.5f, 0), new Vector3(-0.5f, 0.5f, 0) };
            quad.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
            quad.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            quad.RecalculateBounds();
            return quad;
        }

        static Halo NewHalo()
        {
            var go = new GameObject("MWCoop-Halo");
            go.AddComponent<MeshFilter>().sharedMesh = Quad();
            var mr = go.AddComponent<MeshRenderer>();
            var m = new Material(HaloShader()) { mainTexture = HaloTex() };
            mr.sharedMaterial = m;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            go.SetActive(false);
            return new Halo { Go = go, M = m };
        }

        static void Halos(Camera cam, int level)
        {
            Transform ct = cam.transform;
            Vector3 cp = ct.position;
            if (level <= 0 || HaloShader() == null)
            {
                foreach (L l in all) if (l.H != null && l.H.Go.activeSelf) { l.H.A = 0f; l.H.Go.SetActive(false); }
                return;
            }
            // Nuit : d'apres la hauteur du soleil (le jeu le fait tourner sous l'horizon ; son intensite ne change pas).
            float night = sun != null ? Mathf.Clamp01((0.12f + sun.transform.forward.y) / 0.3f) : 1f;
            float strength = level >= 2 ? 1f : 0.6f, grow = level >= 2 ? 1.3f : 1f;
            float dt = Time.deltaTime;
            foreach (L l in all)
            {
                Light li = l.Li;
                float want = 0f, dist = 0f;
                Vector3 p = Vector3.zero;
                if (On(li))
                {
                    p = li.transform.position;
                    Vector3 to = cp - p;
                    dist = to.magnitude;
                    if (dist < (l.Kind == K_STREET ? 600f : 350f) && dist > 0.5f)
                    {
                        Vector3 dir = to / dist;
                        float face = 1f;
                        if (l.Kind == K_BEAM) face = Mathf.Clamp01((Vector3.Dot(li.transform.forward, dir) - 0.25f) / 0.5f);   // vu de face
                        else if (l.Kind == K_TAIL && l.Root != null) face = Mathf.Clamp01((Vector3.Dot(-l.Root.forward, dir) + 0.1f) / 0.5f);   // vu de l'arriere
                        float day = l.Kind == K_STREET ? night : Mathf.Max(night, 0.25f);
                        want = face * day * strength;
                        if (want > 0.01f && Occluded(p, cp, l.Root)) want = 0f;
                    }
                }
                if (l.H == null) { if (want <= 0.01f) continue; l.H = NewHalo(); }
                l.H.A = Mathf.MoveTowards(l.H.A, want, dt * 6f);
                bool show = l.H.A > 0.01f;
                if (l.H.Go.activeSelf != show) l.H.Go.SetActive(show);
                if (!show) continue;
                float baseSize = l.Kind == K_BEAM ? 0.8f : l.Kind == K_TAIL ? 0.35f : 2.4f;
                float size = baseSize * grow * (1f + dist / (l.Kind == K_STREET ? 90f : 60f));
                Transform ht = l.H.Go.transform;
                ht.position = p + (cp - p).normalized * 0.15f;   // (devant l'ampoule, pas dans le verre)
                ht.rotation = ct.rotation;
                ht.localScale = new Vector3(size, size, size);
                Color c = li.color * l.H.A;
                c.a = l.H.A;
                l.H.M.SetColor("_TintColor", c * (l.Kind == K_TAIL ? 0.7f : 0.9f));
                l.H.M.color = c;
            }
        }

        // Un obstacle entre la lumiere et la camera (pas la voiture qui la porte, ni un declencheur) ?
        static bool Occluded(Vector3 p, Vector3 cp, Transform own)
        {
            Vector3 d = cp - p;
            float len = d.magnitude;
            if (len < 0.6f) return false;
            RaycastHit[] hs = Physics.RaycastAll(p + d / len * 0.3f, d / len, len - 0.6f);
            foreach (RaycastHit h in hs)
            {
                if (h.collider == null || h.collider.isTrigger) continue;
                if (own != null && h.collider.transform.IsChildOf(own)) continue;
                if (Game.RootName(h.collider.transform) == "PLAYER") continue;
                return true;
            }
            return false;
        }

        // Essais : etat.
        public static string Describe()
        {
            int beams = 0, tails = 0, streets = 0, shadowed = 0, haloed = 0, lit = 0;
            foreach (L l in all)
            {
                if (l.Kind == K_BEAM) beams++; else if (l.Kind == K_TAIL) tails++; else streets++;
                if (On(l.Li)) lit++;
                if (l.Shadowed) shadowed++;
                if (l.H != null && l.H.Go.activeSelf) haloed++;
            }
            return beams + " phares, " + tails + " feux, " + streets + " lampadaires ; " + lit + " allumes, " + shadowed + " avec ombres, " + haloed + " halos ; soleil " + (sun != null ? (-sun.transform.forward.y).ToString("F2") + " (hauteur)" : "?") + ", shader " + (HaloShader() != null ? HaloShader().name : "-");
        }
    }
}
