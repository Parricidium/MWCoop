using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;

namespace MWCoop
{
    // Ray tracing materiel (demande de JD, 11/10 : « comme sur GTAVC Coop ») : MWCoopRT.dll (rt\mwcrt.cpp, rt\shadow.hlsl).
    // Etape 1 : ombres du soleil (et de la lune) par rayons, a la place des ombres d'Unity.
    //  - Le device Direct3D 11 du jeu est tire d'une texture du mod (RtInit). Le jeu tourne avec -force-gfx-direct (Unity
    //    dessine sur son fil principal) : le mod appelle la DLL depuis OnPostRender de la camera principale -- apres la
    //    scene, avant les effets d'image (brouillard, tonemapping...) et l'interface.
    //  - Entrees : textures internes d'Unity, trouvees par leur nom (les tampons de commandes des cameras ne s'executent
    //    pas dans ce jeu) : « Deferred Depth » (profondeur) et « Deferred GBuffer » ARGB2101010 (normales du monde).
    //  - Scene : maillages des rendus a moins de [Graphismes] RTPortee m (250) de la camera, qui projettent une ombre,
    //    opaques ou a decoupe (feuillages : arbres, haies -- uv et alpha de la texture envoyes, test alpha par rayon) ;
    //    envoyes une fois (positions, triangles), puis matrices a chaque image. Lots statiques : le maillage
    //    combine (deja dans le monde) une fois. Personnages (maillages animes) : pose recalculee a chaque image.
    //  - Ombres : Unity 5.0 rassemble les ombres du soleil dans une carte a l'echelle de l'ecran (« Screenspace ShadowMap »)
    //    juste avant son eclairage differe. La DLL remplace son contenu par les ombres RT a ce moment-la (crochet sur le
    //    changement de cible) : l'eclairage reste celui d'Unity (ambiance, reflets, force). Les ombres d'Unity du soleil
    //    restent donc allumees (rallumees si le joueur les a coupees, rendues a la sortie).
    //  - Passe tardive (fin du rendu de la camera) : ombres des lampes, occlusion ambiante, reflets, rebond, vitres.
    // Rendu du jeu (sonde du 11/10) : DeferredShading en HDR, soleil MAP/Sun/PivotSun/SUN (ombres douces, force 0.8).
    public static class RayTracing
    {
        // MWCoopRT.dll : chargee par son chemin (dossier MWCoop), fonctions prises par leur adresse -- un DllImport par nom ne
        // la trouve pas quand le jeu part d'une copie de lancement (et MWCoop.dll, chargee depuis la memoire, n'a pas de dossier).
        [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)] static extern System.IntPtr LoadLibraryW(string path);
        [DllImport("kernel32", CharSet = CharSet.Ansi)] static extern System.IntPtr GetProcAddress(System.IntPtr mod, string name);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Unicode)] delegate int DProbe(string logPath);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int DPtr(System.IntPtr p);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void DInt(int v);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int DInputs(System.IntPtr depth, System.IntPtr normal, int w, int h);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int DMesh(int id, float[] verts, int nv, int[] idx, int ni);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int DMeshAlpha(int id, float[] verts, int nv, int[] idx, int ni, float[] uvs, int tex, float cutoff);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int DTexture(int id, System.IntPtr d3d11tex, int maxSize);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void DFloats(float[] data, int n);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int DMeshUpdate(int id, float[] verts, int nv);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void DLamps(float[] data, int n, float strength);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void DAmbient(float strength, float radius, int rays);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void DFloat(float v);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void DColor(int id, float r, float g, float b);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int DTexAvg(System.IntPtr tex, float[] rgb);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int DColorTex(int id, System.IntPtr tex, int maxSize);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void DMask(int id, int mask);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void DForget(int id);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int DMeshParts(int id, float[] uvs, int nv, int n, int[] triStart, int[] texIds, float[] cols);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void DFrame(float[] p);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void DInts(int[] data, int n);
        static DProbe fProbe; static DPtr fInit; static DInt fPostRender, fCapture, fArm; static DInputs fInputs; static DMesh fMesh;
        static DMeshAlpha fMeshAlpha; static DTexture fTexture; static DFloats fInstances; static DFrame fFrame; static DInts fStaticCounts; static DMeshUpdate fMeshUpdate; static DLamps fLamps; static DAmbient fAmbient; static DFloat fRefl;
        static DColor fMeshColor, fStaticColor; static DTexAvg fTexAvg; static DColorTex fColorTex; static DMeshParts fMeshParts; static DMask fMeshMask; static DForget fForget; static DFloat fBounce, fGlass;
        static T Fn<T>(System.IntPtr lib, string name) where T : class
        {
            System.IntPtr p = GetProcAddress(lib, name);
            if (p == System.IntPtr.Zero) throw new System.EntryPointNotFoundException(name);
            return Marshal.GetDelegateForFunctionPointer(p, typeof(T)) as T;
        }
        static void Bind(System.IntPtr lib)
        {
            fProbe = Fn<DProbe>(lib, "RtProbe"); fInit = Fn<DPtr>(lib, "RtInit"); fPostRender = Fn<DInt>(lib, "RtPostRender");
            fInputs = Fn<DInputs>(lib, "RtSetInputs"); fMesh = Fn<DMesh>(lib, "RtMesh"); fMeshAlpha = Fn<DMeshAlpha>(lib, "RtMeshAlpha");
            fTexture = Fn<DTexture>(lib, "RtTextureFrom"); fInstances = Fn<DFloats>(lib, "RtInstances"); fFrame = Fn<DFrame>(lib, "RtFrame");
            fStaticCounts = Fn<DInts>(lib, "RtStaticCounts"); fCapture = Fn<DInt>(lib, "RtCapture"); fArm = Fn<DInt>(lib, "RtArm");
            fMeshUpdate = Fn<DMeshUpdate>(lib, "RtMeshUpdate");
            fLamps = Fn<DLamps>(lib, "RtLamps");
            fAmbient = Fn<DAmbient>(lib, "RtAmbient");
            fRefl = Fn<DFloat>(lib, "RtReflections");
            fMeshColor = Fn<DColor>(lib, "RtMeshColor"); fStaticColor = Fn<DColor>(lib, "RtStaticColor"); fTexAvg = Fn<DTexAvg>(lib, "RtTexAverage");
            fColorTex = Fn<DColorTex>(lib, "RtColorTexture"); fMeshParts = Fn<DMeshParts>(lib, "RtMeshParts"); fMeshMask = Fn<DMask>(lib, "RtMeshMask"); fForget = Fn<DForget>(lib, "RtForgetMesh"); fBounce = Fn<DFloat>(lib, "RtBounce"); fGlass = Fn<DFloat>(lib, "RtGlass");
        }
        static int RtProbe(string logPath) { return fProbe(logPath); }
        static int RtInit(System.IntPtr d3d11Resource) { return fInit(d3d11Resource); }
        static void RtPostRender(int mode) { fPostRender(mode); }
        static int RtSetInputs(System.IntPtr depth, System.IntPtr normal, int w, int h) { return fInputs(depth, normal, w, h); }
        static int RtMesh(int id, float[] verts, int nv, int[] idx, int ni) { return fMesh(id, verts, nv, idx, ni); }
        static int RtMeshAlpha(int id, float[] verts, int nv, int[] idx, int ni, float[] uvs, int tex, float cutoff) { return fMeshAlpha(id, verts, nv, idx, ni, uvs, tex, cutoff); }
        static int RtTextureFrom(int id, System.IntPtr d3d11tex, int maxSize) { return fTexture(id, d3d11tex, maxSize); }
        static void RtInstances(float[] data, int n) { fInstances(data, n); }
        static void RtFrame(float[] p) { fFrame(p); }
        static void RtStaticCounts(int[] counts, int n) { fStaticCounts(counts, n); }
        static void RtCapture(int on) { fCapture(on); }
        static void RtArm(int flags) { fArm(flags); }
        static int RtMeshUpdate(int id, float[] verts, int nv) { return fMeshUpdate(id, verts, nv); }
        static void RtLamps(float[] data, int n, float strength) { fLamps(data, n, strength); }
        static void RtAmbient(float strength, float radius, int rays) { fAmbient(strength, radius, rays); }
        static void RtReflections(float strength) { fRefl(strength); }

        // Couleur d'une matiere pour les reflets hors de l'ecran : _Color x moyenne de la texture (lue par la DLL, gardee).
        static readonly Dictionary<int, Color> texAvg = new Dictionary<int, Color>();
        static readonly float[] avgBuf = new float[3];
        static Color MatColor(Material m)
        {
            if (m == null) return new Color(0.6f, 0.6f, 0.6f);
            Color c = m.HasProperty("_Color") ? m.color : Color.white;
            Texture t = m.HasProperty("_MainTex") ? m.mainTexture : null;
            if (t != null)
            {
                int id = t.GetInstanceID();
                Color a;
                if (!texAvg.TryGetValue(id, out a))
                {
                    a = Color.white;
                    try { if (fTexAvg(t.GetNativeTexturePtr(), avgBuf) != 0) a = new Color(avgBuf[0], avgBuf[1], avgBuf[2]); } catch { }
                    texAvg[id] = a;
                }
                c *= a;
            }
            return new Color(Mathf.Clamp01(c.r), Mathf.Clamp01(c.g), Mathf.Clamp01(c.b));
        }
        static readonly Dictionary<Mesh, Material> meshMat = new Dictionary<Mesh, Material>();
        static readonly Dictionary<int, Color> staticSum = new Dictionary<int, Color>();
        static readonly Dictionary<int, int> staticN = new Dictionary<int, int>();
        static readonly HashSet<int> staticColorSent = new HashSet<int>();
        // Reflets : [Graphismes] RTReflets (0 non, 50 discrets, 100 normaux ; 100 par defaut).
        static float ReflStrength { get { return Mathf.Clamp(Config.GetInt("Test", "RTReflets", Gfx.Get("RTReflets", 100)), 0, 3000) * 0.01f; } }

        // Occlusion ambiante : [Graphismes] RTOcclusion (0 non, 50 legere, 100 normale, 150 forte ; 100 par defaut).
        static float AmbientStrength { get { return Mathf.Clamp(Config.GetInt("Test", "RTOcclusion", Gfx.Get("RTOcclusion", 100)), 0, 1000) * 0.01f; } }

        // Ombres des lampes (phares, lampadaires) par rayons : [Graphismes] RTLampes = nombre de lampes (0 : non ; 8 par defaut).
        // Actives : le module Lumieres ne donne plus d'ombres d'Unity a ces lampes.
        public static bool LampsActive { get { return ready && Wanted && Direct && LampCount > 0; } }
        // Corps du joueur local a la premiere personne (demande de JD, 12/10 : « la reflexion du joueur ») : son avatar existe,
        // invisible a la camera (ombre seulement), pour les rayons -- reflets, ombres, occlusion. [Graphismes] RTCorps (1).
        public static bool BodyWanted { get { return ready && Wanted && Direct && Gfx.Get("RTCorps", 1) != 0; } }
        static int LampCount { get { return Mathf.Clamp(Config.GetInt("Test", "RTLampes", Gfx.Get("RTLampes", 8)), 0, 16); } }
        static readonly List<Light> lampList = new List<Light>();
        static readonly float[] lampData = new float[16 * 12];
        static int lampLogs;
        static int SendLamps(Vector3 cp)
        {
            int want = LampCount;
            if (want <= 0) { RtLamps(lampData, 0, 0); return 0; }
            Lights.CollectLamps(cp, want, lampList);
            int n = 0;
            foreach (Light li in lampList)
            {
                if (li == null) continue;
                int o = n * 12;
                Vector3 p = li.transform.position, f = li.transform.forward;
                Color c = li.color * li.intensity;
                lampData[o] = p.x; lampData[o + 1] = p.y; lampData[o + 2] = p.z; lampData[o + 3] = li.range;
                lampData[o + 4] = c.r; lampData[o + 5] = c.g; lampData[o + 6] = c.b; lampData[o + 7] = li.type == LightType.Spot ? 1 : 0;
                lampData[o + 8] = f.x; lampData[o + 9] = f.y; lampData[o + 10] = f.z; lampData[o + 11] = Mathf.Cos(li.spotAngle * 0.5f * Mathf.Deg2Rad);
                n++;
            }
            RtLamps(lampData, n, Config.GetInt("Test", "RTLampesForce", 100) * 0.01f);
            if (n > 0 && lampLogs++ < 3) Log.Info("rt : " + n + " lampes (plus proche : " + Recon.Path(lampList[0].transform) + ", " + lampList[0].type + ", portee " + lampList[0].range.ToString("F1") + ", intensite " + lampList[0].intensity.ToString("F2") + ")");
            return n;
        }

        // Lots statiques (decor regroupe par Unity, illisible) : releves sur la carte pendant une image de capture (ombres
        // d'Unity rallumees pour elle) -- voir RtStaticCounts / RtCapture dans rt\mwcrt.cpp.
        static readonly HashSet<int> staticCounts = new HashSet<int>();
        static bool staticChanged, captureNow;
        static float nextCapture;

        static float probeAt = -1;
        static bool probed, ready;
        static Camera hooked;
        static Texture2D devTex;

        // Ray tracing cache pour le moment (JD, 24/10 : « on se penchera dessus plus tard ») : jamais active, quoi que dise mwcoop.ini
        // (ancien [Graphismes] RayTracing=1, [Test] RT=1, lanceur pas a jour, reglage recu), et ses lignes du menu F10 cachees. Le code reste :
        // true le rallume.
        public static readonly bool Disponible = false;

        public static bool Wanted { get { return Disponible && (Config.GetInt("Test", "RT", 0) != 0 || Gfx.Get("RayTracing", 0) != 0); } }

        // Jeu lance avec -force-gfx-direct : Unity dessine sur son fil principal, le mod peut appeler la DLL au bon moment.
        static int direct = -1;
        public static bool Direct
        {
            get
            {
                if (direct < 0) { direct = 0; foreach (string a in System.Environment.GetCommandLineArgs()) if (a.ToLowerInvariant() == "-force-gfx-direct") direct = 1; }
                return direct == 1;
            }
        }

        public static void OnLevelLoaded()
        {
            RestoreShadows();
            probed = false; hooked = null; ready = false;
            lastDepth = lastNormal = System.IntPtr.Zero;
            tracked.Clear(); meshIds.Clear(); sentMeshes.Clear(); badMeshes.Clear(); toSend.Clear(); skins.Clear(); skinNear.Clear(); meshMat.Clear(); staticSum.Clear(); staticN.Clear(); staticColorSent.Clear(); groundIds.Clear(); groundMasked.Clear(); lastSeen.Clear(); glassIds.Clear(); queued.Clear(); cutMats.Clear(); texQueue.Clear(); texQueued.Clear();
            probeAt = PlayerSync.InGame && Wanted ? Time.realtimeSinceStartup + 8f : -1;
        }

        public static void Update()
        {
            if (probeAt < 0 && PlayerSync.InGame && Wanted && Direct) probeAt = Time.realtimeSinceStartup + 1f;   // (allume en jeu, menu F10)
            if (probeAt < 0 || Time.realtimeSinceStartup < probeAt) return;
            if (!probed) { probed = true; Probe(); }
            if (!ready || !Wanted) return;
            Camera c = Game.MainCam;
            if (c != null && c != hooked && GameCam(c))
            {
                if (c.GetComponent<RtCamHook>() == null) c.gameObject.AddComponent<RtCamHook>();
                hooked = c;
                Log.Info("rt : dessin accroche a la camera " + c.name);
            }
            if (hooked == null) return;
            Inputs();
            Scan(hooked);
            ProcessTextures();   // (textures de feuillage lues par la DLL : possible hors rendu)
            SendMeshes();
        }

        static void Probe()
        {
            if (Wine.On && Config.GetInt("Test", "RTWine", 0) == 0)
            {
                Log.Info("rt : coupe sous Wine/Proton (Direct3D 12 et DXR a cote du Direct3D 11 du jeu : pas pris en charge par DXVK et vkd3d-proton ; [Test] RTWine=1 pour essayer)");
                return;
            }
            try
            {
                // MWCoopRT.dll a cote du mwcoop.ini (dossier MWCoop du jeu)
                string own = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Config.IniPath), "MWCoopRT.dll");
                if (!System.IO.File.Exists(own)) own = "MWCoopRT.dll";   // (essais : a cote du jeu)
                System.IntPtr h = LoadLibraryW(own);
                if (h == System.IntPtr.Zero) throw new System.DllNotFoundException(own + " (erreur " + Marshal.GetLastWin32Error() + ")");
                Bind(h);
                RtProbe(System.IO.Path.Combine(Log.DataDir, "logs\\rt.log"));
                if (devTex == null) { devTex = new Texture2D(4, 4, TextureFormat.ARGB32, false) { hideFlags = HideFlags.HideAndDontSave }; devTex.Apply(); }
                int i = RtInit(devTex.GetNativeTexturePtr());
                ready = (i & 1) != 0 && (i & 2) != 0 && Direct;
                Log.Info("rt : device du jeu " + ((i & 1) != 0 ? "recupere" : "ABSENT") + ", DXR " + ((i & 2) != 0 ? "oui" : "non") + ", rendu sur un seul fil (-force-gfx-direct) "
                         + (Direct ? "oui" : "NON : rien n'est dessine") + " (journal logs\\rt.log)");
            }
            catch (System.Exception e) { Log.Warn("rt : plugin MWCoopRT absent ou illisible (" + e.GetType().Name + " : " + e.Message + ")"); }
        }

        // ---------------------------------------------------------------- entrees (textures internes d'Unity)
        static System.IntPtr lastDepth, lastNormal;
        static float nextFind;
        static void Inputs()
        {
            if (Time.realtimeSinceStartup < nextFind) return;
            nextFind = Time.realtimeSinceStartup + 2f;
            RenderTexture d = null, n = null;
            foreach (Object o in Resources.FindObjectsOfTypeAll(typeof(RenderTexture)))
            {
                var r = (RenderTexture)o;
                if (r.width != Screen.width || r.height != Screen.height || !r.IsCreated()) continue;
                if (r.name == "Deferred Depth") d = r;
                else if (r.name == "Deferred GBuffer" && r.format == RenderTextureFormat.ARGB2101010) n = r;
            }
            System.IntPtr dp = d != null ? d.GetNativeTexturePtr() : System.IntPtr.Zero, np = n != null ? n.GetNativeTexturePtr() : System.IntPtr.Zero;
            if (dp == lastDepth && np == lastNormal) return;
            lastDepth = dp; lastNormal = np;
            int ok = RtSetInputs(dp, np, Screen.width, Screen.height);
            Log.Info("rt : entrees " + Screen.width + "x" + Screen.height + " : profondeur " + (d != null ? "trouvee" : "ABSENTE") + ", normales " + (n != null ? "trouvees" : "ABSENTES") + (ok != 0 ? ", lues par la DLL" : ", PAS lues par la DLL"));
        }

        // ---------------------------------------------------------------- scene
        class Tracked { public Renderer R; public int Mesh; public bool Static; }
        // Sol (rendus sans ombre : MAP/MESH/TERRAIN_OBJ...) : leur collision a le maillage d'origine, lisible (le rendu est en
        // lot statique, illisible, et absent de la carte d'ombres ou le decor est releve). Masque 2 : vu par l'occlusion
        // ambiante et les reflets, pas par les ombres (pas plus que chez Unity). [Graphismes] RTSol (1).
        static readonly HashSet<int> groundIds = new HashSet<int>(), groundMasked = new HashSet<int>();
        // Memoire : maillages qui ne sont plus dans la scene depuis 60 s oublies (la DLL libere leurs tampons et leurs cases).
        static readonly Dictionary<int, float> lastSeen = new Dictionary<int, float>();
        static float nextForget;
        static void ForgetOld()
        {
            float now = Time.realtimeSinceStartup, keep = Config.GetInt("Test", "RTOubli", 60);
            foreach (Tracked t in tracked) lastSeen[t.Mesh] = now;
            foreach (SkinnedMeshRenderer sm in skinNear) { SkinState ss; if (sm != null && skins.TryGetValue(sm, out ss)) lastSeen[ss.Id] = now; }
            if (now < nextForget) return;
            nextForget = now + Mathf.Min(10f, keep);
            var goneSkins = new List<SkinnedMeshRenderer>();
            foreach (var kv in skins)
            {
                float seen;
                if (kv.Key != null && lastSeen.TryGetValue(kv.Value.Id, out seen) && now - seen < keep) continue;
                goneSkins.Add(kv.Key);
            }
            foreach (SkinnedMeshRenderer sm in goneSkins)
            {
                SkinState ss = skins[sm];
                if (ss.Sent) { try { fForget(ss.Id); } catch { } sentMeshes.Remove(ss.Id); }
                lastSeen.Remove(ss.Id);
                if (ss.Baked != null) Object.Destroy(ss.Baked);
                skins.Remove(sm);
            }
            var gone = new List<Mesh>();
            foreach (var kv in meshIds)
            {
                float seen;
                if (!sentMeshes.Contains(kv.Value) || (lastSeen.TryGetValue(kv.Value, out seen) && now - seen < keep)) continue;
                if (!lastSeen.ContainsKey(kv.Value)) { lastSeen[kv.Value] = now; continue; }
                gone.Add(kv.Key);
            }
            foreach (Mesh m in gone)
            {
                int id = meshIds[m];
                try { fForget(id); } catch { }
                meshIds.Remove(m); sentMeshes.Remove(id); lastSeen.Remove(id); groundIds.Remove(id); groundMasked.Remove(id); cutMats.Remove(m); meshMat.Remove(m);
            }
            if (gone.Count + goneSkins.Count > 0) Log.Info("rt : " + gone.Count + " maillages et " + goneSkins.Count + " personnages oublies (plus dans la scene depuis " + keep + " s), " + sentMeshes.Count + " gardes");
        }
        // Personnages (maillages animes : joueurs, PNJ) : pose relevee a chaque image (BakeMesh), au plus 8 a 40 m.
        class SkinState { public int Id; public Mesh Baked; public bool Sent; public float[] Buf; public string PartsSig; public float NextParts; }
        static readonly Dictionary<SkinnedMeshRenderer, SkinState> skins = new Dictionary<SkinnedMeshRenderer, SkinState>();
        static readonly List<SkinnedMeshRenderer> skinNear = new List<SkinnedMeshRenderer>();
        static readonly List<Tracked> tracked = new List<Tracked>();
        static readonly Dictionary<Mesh, int> meshIds = new Dictionary<Mesh, int>();
        static readonly HashSet<int> sentMeshes = new HashSet<int>(), badMeshes = new HashSet<int>();
        static readonly Queue<Mesh> toSend = new Queue<Mesh>();
        static readonly HashSet<Mesh> queued = new HashSet<Mesh>();
        static readonly Dictionary<Mesh, Material> cutMats = new Dictionary<Mesh, Material>();   // maillages a decoupe
        static readonly HashSet<int> sentTex = new HashSet<int>(), badTex = new HashSet<int>();
        static readonly Queue<Texture> texQueue = new Queue<Texture>();   // (lues pendant le rendu : OnPostRender)
        static readonly HashSet<int> texQueued = new HashSet<int>();
        static float nextScan, nextScanLog;
        static int nextMeshId = 1;

        static float Range { get { return Mathf.Clamp(Gfx.Get("RTPortee", 250), 50, 1000); } }

        static void Scan(Camera cam)
        {
            if (Time.realtimeSinceStartup < nextScan) return;
            nextScan = Time.realtimeSinceStartup + 1.5f;
            Vector3 cp = cam.transform.position;
            float r2 = Range * Range;
            tracked.Clear();
            var statics = new HashSet<Mesh>();
            int total = 0, off = 0, noShadow = 0, far = 0, transp = 0, excl = 0, noMesh = 0, dup = 0, bad = 0, leaves = 0, grounds = 0, glasses = 0;
            foreach (MeshRenderer mr in Object.FindObjectsOfType<MeshRenderer>())
            {
                total++;
                if (!mr.enabled) { off++; continue; }
                if (mr.shadowCastingMode == UnityEngine.Rendering.ShadowCastingMode.Off)
                {
                    noShadow++;
                    if (Gfx.Get("RTSol", 1) == 0 || mr.bounds.SqrDistance(cp) > r2) continue;
                    MeshCollider gc = mr.GetComponent<MeshCollider>();
                    Material gm = mr.sharedMaterial;
                    if (gc == null || gc.sharedMesh == null || gm == null || gm.renderQueue >= 2500) continue;
                    string gr = mr.transform.root.name;
                    if (gr == "PLAYER" || gr == "GUI" || gr.StartsWith("MWCoop")) continue;
                    Mesh gmesh = gc.sharedMesh;
                    int gid;
                    if (!meshIds.TryGetValue(gmesh, out gid)) { gid = nextMeshId++; meshIds[gmesh] = gid; groundIds.Add(gid); }
                    if (badMeshes.Contains(gid)) continue;
                    if (!meshMat.ContainsKey(gmesh)) meshMat[gmesh] = gm;
                    if (!sentMeshes.Contains(gid) && queued.Add(gmesh)) toSend.Enqueue(gmesh);
                    tracked.Add(new Tracked { R = mr, Mesh = gid });
                    grounds++;
                    continue;
                }
                if (mr.bounds.SqrDistance(cp) > r2) { far++; continue; }
                Material m = mr.sharedMaterial;
                // Feuillages : shaders a decoupe (_Cutoff ; « Nature/Tree Soft Occlusion Leaves » est dans la file transparente)
                // Vitres (nom du rendu ou de la matiere : glass, window, windshield) : leur propre masque (4), ni ombre ni occlusion,
                // reflets calcules a part (rayon de la camera qui les traverse).
                bool glass = m != null && !mr.isPartOfStaticBatch && IsGlass(mr, m);
                if (glass)
                {
                    MeshFilter gf = mr.GetComponent<MeshFilter>();
                    Mesh gmesh = gf != null ? gf.sharedMesh : null;
                    if (gmesh == null) { noMesh++; continue; }
                    int gid;
                    if (!meshIds.TryGetValue(gmesh, out gid)) { gid = nextMeshId++; meshIds[gmesh] = gid; }
                    glassIds.Add(gid);
                    if (badMeshes.Contains(gid)) { bad++; continue; }
                    if (!sentMeshes.Contains(gid) && queued.Add(gmesh)) toSend.Enqueue(gmesh);
                    tracked.Add(new Tracked { R = mr, Mesh = gid });
                    glasses++;
                    continue;
                }
                bool cut = m != null && Cutout(m) && mr.sharedMaterials.Length == 1 && !mr.isPartOfStaticBatch;
                if (m == null || (m.renderQueue >= 2500 && !cut)) { transp++; continue; }    // transparents (vitres...) : pas d'ombre
                Transform root = mr.transform.root;
                if (root.name == "PLAYER" || root.name == "GUI" || (root.name.StartsWith("MWCoop") && !root.name.StartsWith("MWCoop-Joueur"))) { excl++; continue; }   // (avatars : chapeaux, accessoires compris)
                MeshFilter mf = mr.GetComponent<MeshFilter>();
                Mesh mesh = mf != null ? mf.sharedMesh : null;
                if (mesh == null) { noMesh++; continue; }
                bool st = mr.isPartOfStaticBatch;
                if (st)
                {
                    // (lot statique : releve sur la carte, pas lu ici)
                    if (statics.Add(mesh) && staticCounts.Add(mesh.vertexCount)) staticChanged = true;
                    if (!staticColorSent.Contains(mesh.vertexCount))
                    {
                        Color sc0; int sn0;
                        staticSum.TryGetValue(mesh.vertexCount, out sc0); staticN.TryGetValue(mesh.vertexCount, out sn0);
                        staticSum[mesh.vertexCount] = sc0 + MatColor(m); staticN[mesh.vertexCount] = sn0 + 1;
                    }
                    dup++;
                    continue;
                }
                int id;
                if (!meshIds.TryGetValue(mesh, out id)) { id = nextMeshId++; meshIds[mesh] = id; }
                if (badMeshes.Contains(id)) { bad++; continue; }
                if (cut) { cutMats[mesh] = m; leaves++; }
                if (!meshMat.ContainsKey(mesh)) meshMat[mesh] = m;
                if (!sentMeshes.Contains(id) && queued.Add(mesh)) toSend.Enqueue(mesh);
                tracked.Add(new Tracked { R = mr, Mesh = id, Static = st });
            }
            skinNear.Clear();
            foreach (SkinnedMeshRenderer sm in Object.FindObjectsOfType<SkinnedMeshRenderer>())
            {
                if (skinNear.Count >= 8) break;
                if (!sm.enabled || sm.shadowCastingMode == UnityEngine.Rendering.ShadowCastingMode.Off || sm.sharedMesh == null) continue;
                if (sm.bounds.SqrDistance(cp) > 40f * 40f || sm.transform.root.name == "GUI") continue;
                skinNear.Add(sm);
            }
            foreach (var kv in staticSum)   // (couleur moyenne de chaque lot, une fois)
                if (!staticColorSent.Contains(kv.Key))
                {
                    staticColorSent.Add(kv.Key);
                    Color avg = kv.Value * (1f / Mathf.Max(1, staticN[kv.Key]));
                    try { fStaticColor(kv.Key, avg.r, avg.g, avg.b); } catch { }
                }
            if (staticChanged)
            {
                staticChanged = false;
                var arr = new int[staticCounts.Count]; staticCounts.CopyTo(arr);
                RtStaticCounts(arr, arr.Length);
                captureNow = true;
                Log.Info("rt : " + arr.Length + " lots statiques a relever");
            }
            if (Time.realtimeSinceStartup > nextCapture) { nextCapture = Time.realtimeSinceStartup + 10f; captureNow = true; }
            ForgetOld();
            if (Time.realtimeSinceStartup > nextScanLog)
            {
                nextScanLog = Time.realtimeSinceStartup + 20f;
                Log.Info("rt : scene a " + Range + " m : " + tracked.Count + " objets (sur " + total + " : coupes " + off + ", sans ombre " + noShadow + ", loin " + far + ", transparents " + transp
                         + ", exclus " + excl + ", sans maillage " + noMesh + ", lot deja pris " + dup + ", illisibles " + bad + ", feuillages " + leaves + ", sol " + grounds + ", vitres " + glasses + ", personnages " + skinNear.Count + "), " + sentMeshes.Count + " maillages envoyes, " + toSend.Count + " a envoyer");
            }
        }

        static void SendMeshes()
        {
            float budget = Time.realtimeSinceStartup + 0.004f;   // (au plus ~4 ms par image)
            List<Mesh> wait = null;
            while (toSend.Count > 0 && Time.realtimeSinceStartup < budget)
            {
                Mesh mesh = toSend.Dequeue();
                if (mesh == null) continue;
                int id = meshIds[mesh];
                if (sentMeshes.Contains(id)) { queued.Remove(mesh); continue; }
                Material wm;
                if (cutMats.TryGetValue(mesh, out wm) && wm != null && wm.mainTexture != null)
                {
                    int tid = wm.mainTexture.GetInstanceID();
                    if (badTex.Contains(tid)) { queued.Remove(mesh); badMeshes.Add(id); continue; }
                    if (!sentTex.Contains(tid))
                    {   // (texture d'alpha d'abord)
                        if (texQueued.Add(tid)) texQueue.Enqueue(wm.mainTexture);
                        if (wait == null) wait = new List<Mesh>();
                        wait.Add(mesh);
                        continue;
                    }
                }
                queued.Remove(mesh);
                try
                {
                    Vector3[] v = mesh.vertices;
                    int[] t = mesh.triangles;
                    if (v == null || v.Length == 0 || t == null || t.Length < 3) { badMeshes.Add(id); continue; }
                    var f = new float[v.Length * 3];
                    for (int i = 0; i < v.Length; i++) { f[i * 3] = v[i].x; f[i * 3 + 1] = v[i].y; f[i * 3 + 2] = v[i].z; }
                    Material cm;
                    int sent;
                    if (cutMats.TryGetValue(mesh, out cm) && cm != null && cm.mainTexture != null && sentTex.Contains(cm.mainTexture.GetInstanceID()))
                    {
                        Vector2[] uv = mesh.uv;
                        Vector2 sc = cm.mainTextureScale, of = cm.mainTextureOffset;
                        var u = new float[v.Length * 2];
                        if (uv != null && uv.Length == v.Length)
                            for (int i = 0; i < v.Length; i++) { u[i * 2] = uv[i].x * sc.x + of.x; u[i * 2 + 1] = uv[i].y * sc.y + of.y; }
                        float cutoff = Mathf.Clamp(cm.GetFloat("_Cutoff"), 0.01f, 0.99f);
                        sent = RtMeshAlpha(id, f, v.Length, t, t.Length, u, cm.mainTexture.GetInstanceID(), cutoff);
                        float u0 = 9, u1 = -9, v0 = 9, v1 = -9;
                        for (int i = 0; i < v.Length; i++) { u0 = Mathf.Min(u0, u[i * 2]); u1 = Mathf.Max(u1, u[i * 2]); v0 = Mathf.Min(v0, u[i * 2 + 1]); v1 = Mathf.Max(v1, u[i * 2 + 1]); }
                        Log.Info("rt : feuillage " + mesh.name + " (" + v.Length + " sommets, " + (t.Length / 3) + " triangles, texture " + cm.mainTexture.name + ", seuil " + cutoff.ToString("0.00")
                                 + ", uv " + (uv != null ? uv.Length : -1) + " de " + u0.ToString("0.000") + "," + v0.ToString("0.000") + " a " + u1.ToString("0.000") + "," + v1.ToString("0.000") + ", echelle " + sc + " decalage " + of + ")");
                    }
                    else sent = RtMesh(id, f, v.Length, t, t.Length);
                    if (sent != 0) sentMeshes.Add(id); else badMeshes.Add(id);
                    if (sent != 0 && groundIds.Contains(id)) { fMeshMask(id, 2); groundMasked.Add(id); }
                    if (sent != 0 && glassIds.Contains(id)) fMeshMask(id, 4);
                    Material mc;
                    if (sent != 0 && meshMat.TryGetValue(mesh, out mc)) { Color cc = MatColor(mc); fMeshColor(id, cc.r, cc.g, cc.b); }
                }
                catch { badMeshes.Add(id); }
            }
            if (wait != null) foreach (Mesh m in wait) toSend.Enqueue(m);
        }

        // Une texture d'alpha par image, pendant le rendu (hors rendu, la copie par la carte ne lit rien).
        static void ProcessTextures()
        {
            if (texQueue.Count == 0) return;
            Texture tex = texQueue.Dequeue();
            if (tex == null) return;
            texQueued.Remove(tex.GetInstanceID());
            if (!SendAlphaTexture(tex)) badTex.Add(tex.GetInstanceID());
        }

        // Matiere a decoupe : feuillages de Unity (« Leaves »), shaders « Cutout », Standard en mode decoupe.
        static bool IsGlass(Renderer r, Material m)
        {
            string a = r.name.ToLowerInvariant(), b = m.name.ToLowerInvariant();
            return a.Contains("glass") || a.Contains("window") || a.Contains("windshield") || b.Contains("glass") || b.Contains("window");
        }
        static readonly HashSet<int> glassIds = new HashSet<int>();
        static bool Cutout(Material m)
        {
            if (m.mainTexture == null || !m.HasProperty("_Cutoff")) return false;
            string n = m.shader != null ? m.shader.name : "";
            return n.Contains("Leaves") || n.Contains("Cutout") || m.IsKeywordEnabled("_ALPHATEST_ON");
        }

        // Alpha d'une texture de feuillage : lue par la DLL directement sur la carte (Unity ne recopie pas ces textures).
        static bool SendAlphaTexture(Texture tex)
        {
            if (tex == null) return false;
            int id = tex.GetInstanceID();
            if (sentTex.Contains(id)) return true;
            try
            {
                int ok = RtTextureFrom(id, tex.GetNativeTexturePtr(), Config.GetInt("Test", "RTAlphaTaille", 2048));
                Log.Info("rt : texture de feuillage " + tex.name + " " + tex.width + "x" + tex.height + (ok != 0 ? " envoyee" : " NON LUE (laissee opaque, journal rt.log)"));
                if (ok != 0) sentTex.Add(id);
                return ok != 0;
            }
            catch (System.Exception e) { Log.Warn("rt : texture " + tex.name + " illisible (" + e.Message + ")"); return false; }
        }

        // Personnages : textures et couleurs de chaque partie (peau, habits) pour la couleur exacte dans les reflets ;
        // renvoyees quand la tenue change (signature des textures, revue toutes les 2 s).
        static void SendParts(SkinnedMeshRenderer sm, SkinState st)
        {
            Material[] mats = sm.sharedMaterials;
            Mesh src = st.Baked;
            int n = Mathf.Min(src.subMeshCount, Mathf.Min(mats.Length, 8));
            if (n <= 0) return;
            var sig = new System.Text.StringBuilder();
            for (int k = 0; k < n; k++) { Material m = mats[k]; Texture t = m != null && m.HasProperty("_MainTex") ? m.mainTexture : null; sig.Append(t != null ? t.GetInstanceID() : 0).Append(m != null ? m.color.GetHashCode() : 0).Append(';'); }
            string s = sig.ToString();
            if (s == st.PartsSig) return;
            var starts = new int[n]; var texIds = new int[n]; var cols = new float[n * 3];
            int tri = 0;
            for (int k = 0; k < n; k++)
            {
                starts[k] = tri;
                tri += src.GetTriangles(k).Length / 3;
                Material m = mats[k];
                Color c = m != null && m.HasProperty("_Color") ? m.color : Color.white;
                cols[k * 3] = c.r; cols[k * 3 + 1] = c.g; cols[k * 3 + 2] = c.b;
                Texture t = m != null && m.HasProperty("_MainTex") ? m.mainTexture : null;
                texIds[k] = -1;
                if (t != null) { try { if (fColorTex(t.GetInstanceID(), t.GetNativeTexturePtr(), 256) != 0) texIds[k] = t.GetInstanceID(); } catch { } }
            }
            Vector2[] uv = src.uv;
            if (uv == null || uv.Length != src.vertexCount) return;
            var u = new float[uv.Length * 2];
            for (int i = 0; i < uv.Length; i++) { u[i * 2] = uv[i].x; u[i * 2 + 1] = uv[i].y; }
            if (fMeshParts(st.Id, u, uv.Length, n, starts, texIds, cols) != 0)
            {
                st.PartsSig = s;
                Log.Info("rt : personnage " + sm.name + " : " + n + " parties colorees (" + s + ")");
            }
        }

        static void UpdateSkins()
        {
            foreach (SkinnedMeshRenderer sm in skinNear)
            {
                if (sm == null || !sm.enabled || !sm.gameObject.activeInHierarchy) continue;
                SkinState st;
                if (!skins.TryGetValue(sm, out st)) { st = new SkinState { Id = nextMeshId++, Baked = new Mesh() }; skins[sm] = st; }
                try
                {
                    sm.BakeMesh(st.Baked);
                    Vector3[] v = st.Baked.vertices;
                    if (v.Length == 0) continue;
                    if (st.Buf == null || st.Buf.Length != v.Length * 3) st.Buf = new float[v.Length * 3];
                    for (int i = 0; i < v.Length; i++) { st.Buf[i * 3] = v[i].x; st.Buf[i * 3 + 1] = v[i].y; st.Buf[i * 3 + 2] = v[i].z; }
                    if (!st.Sent)
                    {
                        int[] t = st.Baked.triangles;
                        st.Sent = RtMesh(st.Id, st.Buf, v.Length, t, t.Length) != 0;
                        if (st.Sent) { Color cc = MatColor(sm.sharedMaterial); fMeshColor(st.Id, cc.r, cc.g, cc.b); }
                        if (st.Sent) sentMeshes.Add(st.Id);
                        Bounds b = st.Baked.bounds;
                        Log.Info("rt : personnage " + Recon.Path(sm.transform) + " : " + v.Length + " sommets, " + (t.Length / 3) + " triangles, pose de " + b.min.ToString("F2") + " a " + b.max.ToString("F2")
                                 + ", objet en " + sm.transform.position.ToString("F1") + " echelle " + sm.transform.lossyScale.ToString("F2") + (st.Sent ? "" : " REFUSE"));
                    }
                    else RtMeshUpdate(st.Id, st.Buf, v.Length);
                    if (st.Sent && Time.realtimeSinceStartup >= st.NextParts) { st.NextParts = Time.realtimeSinceStartup + 2f; SendParts(sm, st); }
                }
                catch { }
            }
        }

        static float[] instBuf = new float[0];
        static void SendInstances()
        {
            int n = 0;
            if (instBuf.Length < (tracked.Count + skinNear.Count) * 13) instBuf = new float[(tracked.Count + skinNear.Count) * 13 + 1300];
            foreach (Tracked t in tracked)
            {
                if (t.R == null || !sentMeshes.Contains(t.Mesh) || !t.R.enabled || !t.R.gameObject.activeInHierarchy) continue;
                Matrix4x4 m = t.Static ? Matrix4x4.identity : t.R.localToWorldMatrix;
                int o = n * 13;
                instBuf[o] = t.Mesh;
                for (int r = 0; r < 3; r++) for (int c = 0; c < 4; c++) instBuf[o + 1 + r * 4 + c] = m[r, c];
                n++;
            }
            foreach (SkinnedMeshRenderer sm in skinNear)
            {
                SkinState st;
                if (sm == null || !sm.enabled || !sm.gameObject.activeInHierarchy || !skins.TryGetValue(sm, out st) || !st.Sent) continue;
                // (pose deja a l'echelle de l'objet : position et rotation seules)
                Matrix4x4 m = Matrix4x4.TRS(sm.transform.position, sm.transform.rotation, Vector3.one);
                int o = n * 13;
                instBuf[o] = st.Id;
                for (int r = 0; r < 3; r++) for (int c = 0; c < 4; c++) instBuf[o + 1 + r * 4 + c] = m[r, c];
                n++;
            }
            RtInstances(instBuf, n);
        }

        // ---------------------------------------------------------------- soleil, camera
        static Light sun;
        static LightShadows sunShadowsWas;
        static bool sunCut;
        static float nextSun;
        static Light FindSun()
        {
            if (Time.realtimeSinceStartup < nextSun && sun != null) return sun;
            nextSun = Time.realtimeSinceStartup + 2f;
            Light best = null;
            foreach (Light l in Object.FindObjectsOfType<Light>())
                if (l.type == LightType.Directional && l.isActiveAndEnabled && (best == null || l.intensity > best.intensity)) best = l;
            if (best != sun) { RestoreShadows(); sun = best; }
            return sun;
        }
        static void RestoreShadows()
        {
            if (sunCut && sun != null) sun.shadows = sunShadowsWas;
            sunCut = false;
            if (distSet) { QualitySettings.shadowDistance = distWas; distSet = false; }
        }
        // L'eclairage d'Unity estompe les ombres au-dela de SA distance d'ombre (retour de JD, 12/10 : plus d'ombres RT au
        // loin avec le prereglage RT, qui la met a 30 m) : portee a celle du RT tant qu'il est actif.
        static bool distSet;
        static float distWas;

        static readonly float[] frame = new float[36];
        static float nextFrameLog;
        static void SendFrame(Camera cam, bool cut)
        {
            Light l = FindSun();
            Matrix4x4 vp = GL.GetGPUProjectionMatrix(cam.projectionMatrix, true) * cam.worldToCameraMatrix;
            Matrix4x4 inv = vp.inverse;
            for (int r = 0; r < 4; r++) for (int c = 0; c < 4; c++) frame[r * 4 + c] = inv[r, c];
            Vector3 toSun = l != null ? -l.transform.forward : Vector3.up;
            Color sc = l != null ? l.color * l.intensity : Color.black;
            Color amb = RenderSettings.ambientLight * RenderSettings.ambientIntensity;
            frame[16] = toSun.x; frame[17] = toSun.y; frame[18] = toSun.z; frame[19] = 0;
            frame[20] = sc.r; frame[21] = sc.g; frame[22] = sc.b; frame[23] = 0;
            frame[24] = amb.r; frame[25] = amb.g; frame[26] = amb.b; frame[27] = 0;
            frame[28] = l != null ? l.shadowStrength : 0.8f;               // force de l'ombre
            frame[29] = Range + 50f;                                        // portee des rayons
            frame[30] = Mathf.Clamp(Config.GetInt("Test", "RTRayons", Gfx.Get("RTRayons", 2)), 1, 16);          // rayons par pixel
            frame[31] = Config.GetInt("Test", "RTDouceur", Gfx.Get("RTDouceur", 1)) * 0.001f;                   // demi-angle du soleil (radians)
            Vector3 cpos = cam.transform.position;
            frame[32] = cpos.x; frame[33] = cpos.y; frame[34] = cpos.z;
            frame[35] = 0;
            if (Time.realtimeSinceStartup > nextFrameLog)
            {
                nextFrameLog = Time.realtimeSinceStartup + 20f;
                Log.Info("rt : soleil " + (l != null ? l.name + " couleur " + l.color + " x " + l.intensity + ", hauteur " + toSun.y.ToString("0.00") : "absent") + ", ambiance " + RenderSettings.ambientMode + " " + RenderSettings.ambientLight + " x " + RenderSettings.ambientIntensity
                         + ", ciel " + RenderSettings.ambientSkyColor + ", sol " + RenderSettings.ambientGroundColor);
            }
            RtFrame(frame);
            // Ombres d'Unity de cette lumiere : allumees (leur carte a l'echelle de l'ecran recoit les ombres RT).
            if (l != null && cut)
            {
                if (l.shadows == LightShadows.None)
                {
                    if (!sunCut) { sunShadowsWas = l.shadows; sunCut = true; Log.Info("rt : ombres d'Unity de " + l.name + " rallumees (support des ombres RT)"); }
                    l.shadows = LightShadows.Soft;
                }
                if (!distSet) { distWas = QualitySettings.shadowDistance; distSet = true; Log.Info("rt : distance des ombres d'Unity " + distWas + " m -> " + Range + " m (portee du RT)"); }
                if (QualitySettings.shadowDistance != Range && !capturing) QualitySettings.shadowDistance = Range;
            }
            else if (cut) RestoreShadows();
        }

        static bool capturing;
        // Camera du jeu seulement (retour de JD, 12/10 : en pause, Echap, la camera du menu « CAM » devenait Game.MainCam ; le
        // RT s'y accrochait et dessinait a la taille de l'ecran dans une image plus petite -- quart d'ecran agrandi) : la
        // camera FPS, ou une camera en rendu differe a la taille de l'ecran, sans texture cible.
        static bool GameCam(Camera c)
        {
            if (c == null || !c.enabled || c.targetTexture != null) return false;
            if (c.name == "FPSCamera") return true;
            return c.actualRenderingPath == RenderingPath.DeferredShading && c.pixelWidth == Screen.width && c.pixelHeight == Screen.height;
        }
        class RtCamHook : MonoBehaviour
        {
            void OnPreRender()
            {
                if (ready && !Wanted) { RestoreShadows(); return; }   // (coupe en jeu, menu F10 : ombres d'Unity rendues)
                if (!ready || !Wanted || !GameCam(GetComponent<Camera>())) return;
                if (captureNow)
                {
                    captureNow = false;
                    capturing = true;
                    // (carte d'ombres d'Unity de cette image, a la portee du RT : c'est elle qu'on releve)
                    RtCapture(1);
                }
                {
                    UpdateSkins();
                    SendInstances();
                    SendFrame(GetComponent<Camera>(), true);
                    int flags = 1;
                    float aoS = AmbientStrength;
                    RtAmbient(aoS, 1.5f, Config.GetInt("Test", "RTOcclusionRayons", 2));
                    float rfS = ReflStrength;
                    RtReflections(rfS);
                    // Rebond de lumiere : [Graphismes] RTRebond (0 non, 50 leger, 100 normal ; 100 par defaut).
                    float giS = Mathf.Clamp(Config.GetInt("Test", "RTRebond", Gfx.Get("RTRebond", 100)), 0, 3000) * 0.01f;
                    fBounce(giS);
                    if (giS > 0) flags |= 16;
                    // Reflets des vitres : [Graphismes] RTVitres (0 / 1, oui par defaut) ; [Test] RTVitres = force en % (1000 : controle).
                    float glS = Config.GetInt("Test", "RTVitres", -1) >= 0 ? Mathf.Clamp(Config.GetInt("Test", "RTVitres", 100), 0, 3000) * 0.01f : (Gfx.Get("RTVitres", 1) != 0 ? 1f : 0f);
                    fGlass(glS);
                    if (glS > 0) flags |= 16;
                    if (SendLamps(transform.position) > 0 || aoS > 0 || rfS > 0) flags |= 16;
                    RtArm(flags);
                }
            }
            void OnPostRender()
            {
                if (!ready || !Wanted || !GameCam(GetComponent<Camera>())) { if (ready) RtArm(0); return; }
                if (capturing) { RtCapture(0); capturing = false; }
                RtPostRender(7); RtArm(0);   // (lots releves traites ; passe tardive, armee jusque-la)
            }
            void OnDisable() { RestoreShadows(); }
        }
    }
}
