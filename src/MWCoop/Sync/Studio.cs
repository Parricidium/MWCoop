using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;

namespace MWCoop
{
    // Studio photo des apparences (tenues) : un personnage seul (Avatar.CreatePreview), loin sous le monde, sur une
    // couche a lui que rien d'autre n'utilise, eclaire par ses propres lumieres (allumees le temps du rendu) et filme
    // par une camera desactivee, rendue a la main dans une RenderTexture.
    //  - Apercus du lanceur (format fixe, lu par MWCoop.exe) : en partie, ~30 s apres le chargement, si le cache est
    //    absent ou perime (version du mod, liste des tenues, [Test] RefaireTenues=1), dans <MWCOOP_DIR>\cache\skins\ :
    //      <tenue>.png : bande de 16 vues de 160 x 320 (2560 x 320), fond transparent ; vue i = personnage tourne de
    //        i x 22,5 degres vers SA gauche (vue 0 de face) : 0..15 = un plateau tournant. Pieds a ~8 px du bas,
    //        meme cadrage pour toutes les tenues ;
    //      <tenue>-portrait.png : 128 x 128, tete et epaules, tourne de 15 degres ;
    //      index.txt (UTF-8, sans BOM, lignes \n) : « MWSK 1 <version> » puis « tenue<TAB>libelle » ; efface au debut,
    //        ecrit en dernier. Chaque fichier est ecrit sous un nom temporaire puis renomme.
    //    Une vue par image (la pose et l'angle sont poses en LateUpdate, le rendu a la fin de l'image, apres le calcul
    //    des maillages animes) : ~17 images par tenue. [Coop] ApercuTenues=0 : jamais.
    //  - Apercu en direct du menu F10 > APPARENCE (Live) : la tenue en surbrillance, qui tourne lentement (glisser a la
    //    souris pour la tourner), rendue une fois par image tant que le menu la montre. [Coop] ApercuTenuesMenu=0 : rien.
    // Fond transparent sans dependre du canal alpha des shaders du jeu : chaque vue est rendue sur fond noir puis sur
    // fond blanc ; l'ecart des deux donne la couverture (bords lisses compris), la vue sur noir la couleur.
    // Pendant le rendu : brouillard coupe, lumiere ambiante fixe, soleil (lumieres directionnelles du jeu) ote de la
    // couche ; tout est remis aussitot apres.
    public static class Studio
    {
        const int FrameW = 160, FrameH = 320, Frames = 16, PortraitSize = 128;
        const float Fov = 15f;
        static readonly Vector3 GenPivot = new Vector3(0f, -3000f, 0f), LivePivot = new Vector3(80f, -3000f, 0f);
        static readonly Color LiveBg = new Color(0.07f, 0.11f, 0.18f, 1f);   // bleu nuit des panneaux du menu
        static readonly Color Ambient = new Color(0.36f, 0.37f, 0.40f, 1f);

        class Model
        {
            public Avatar A;
            public Vector3 Pivot, Center;      // Center : centre du corps / racine, a l'angle 0 (axe du plateau)
            public string Skin;
            public bool Measured, Posed;
            public float FeetOff, Height;
            public bool Alive { get { return A != null && A.Root != null; } }
        }

        static int layer = -1;
        static Camera cam;
        static Light[] lights;
        static Model gen, live;
        static bool loopRunning;
        static readonly WaitForEndOfFrame endOfFrame = new WaitForEndOfFrame();

        public static string Dir { get { return Path.Combine(Path.GetDirectoryName(CarColor.MeshPath), "skins"); } }
        static string Header { get { return "MWSK 1 " + Version.Text; } }

        // ---------------------------------------------------------------- deroule

        static float checkAt = -1;
        static int tries;
        static bool forcePending, testing;
        static List<string> todo;
        static int skinIdx, step, written;
        static float jobT0;
        static RenderTexture frameRt, portraitRt;
        static Texture2D readA, readB, readPA, readPB, stripTex, portraitTex;
        static Color32[] strip;

        public static void OnLevelLoaded()
        {
            EndJob();
            ReleaseLive();
            // Objets du studio : detruits avec la scene.
            gen = live = null;
            cam = null;
            lights = null;
            sceneLights = null;
            layer = -1;
            tries = 0;
            checkAt = PlayerSync.InGame ? Time.realtimeSinceStartup + 30f : -1;
        }

        public static void Update()
        {
            if (!PlayerSync.InGame || todo != null || checkAt < 0 || Time.realtimeSinceStartup < checkAt) return;
            checkAt = -1;
            TryStart(forcePending);
        }

        static void Retry(string why)
        {
            if (++tries > 12) { Log.Warn("tenues : apercus abandonnes (" + why + ")"); return; }
            checkAt = Time.realtimeSinceStartup + 10f;
            Log.Info("tenues : " + why + ", nouvel essai dans 10 s");
        }

        static void TryStart(bool force)
        {
            if (todo != null) return;
            bool forced = force || Config.GetInt("Test", "RefaireTenues", 0) != 0;
            forcePending = force;
            if (!forced && Config.GetInt("Coop", "ApercuTenues", 1) == 0) { Log.Info("tenues : apercus du lanceur desactives ([Coop] ApercuTenues=0)"); return; }
            var list = new List<string>();
            char[] bad = Path.GetInvalidFileNameChars();
            foreach (string s in Avatar.SkinNames()) if (s.IndexOfAny(bad) < 0) list.Add(s);
            if (list.Count == 0) { Retry("aucune tenue trouvee"); return; }
            if (!forced && Fresh(list)) { Log.Info("tenues : apercus a jour (" + list.Count + ", " + Dir + ")"); return; }
            if (!EnsureStudio()) { Retry("studio impossible"); return; }
            if (gen == null || !gen.Alive) gen = NewModel(GenPivot, "MWCoop-Studio-Tenues");
            if (gen == null) { Retry("modele d'avatar introuvable"); return; }
            gen.A.Root.SetActive(true);
            try
            {
                Directory.CreateDirectory(Dir);
                string idx = Path.Combine(Dir, "index.txt");
                if (File.Exists(idx)) File.Delete(idx);   // le lanceur ne lit plus le dossier pendant la mise a jour
                foreach (string f in Directory.GetFiles(Dir, "*.tmp")) File.Delete(f);
            }
            catch (System.Exception e) { Log.Warn("tenues : dossier " + Dir + " : " + e.Message); return; }
            int aa = Mathf.Clamp(Config.GetInt("Coop", "ApercuTenuesAA", 1), 1, 8);   // ReadPixels sur une RenderTexture multi-echantillonnee lit du noir (Unity 5.0)
            frameRt = NewRt(FrameW, FrameH, aa);
            portraitRt = NewRt(PortraitSize, PortraitSize, aa);
            readA = NewTex(FrameW, FrameH); readB = NewTex(FrameW, FrameH);
            readPA = NewTex(PortraitSize, PortraitSize); readPB = NewTex(PortraitSize, PortraitSize);
            stripTex = NewTex(FrameW * Frames, FrameH);
            portraitTex = NewTex(PortraitSize, PortraitSize);
            strip = new Color32[FrameW * Frames * FrameH];
            todo = list;
            skinIdx = step = written = 0;
            jobT0 = Time.realtimeSinceStartup;
            forcePending = false;
            Log.Info("tenues : " + list.Count + " apercus a faire" + (forced ? " (forces)" : " (cache absent ou perime)") + ", couche " + layer);
            StartLoop();
        }

        // Cache valable : en-tete de cette version, memes tenues dans le meme ordre, toutes les images presentes.
        static bool Fresh(List<string> list)
        {
            try
            {
                string idx = Path.Combine(Dir, "index.txt");
                if (!File.Exists(idx)) return false;
                string[] l = File.ReadAllLines(idx, System.Text.Encoding.UTF8);
                if (l.Length == 0 || l[0].Trim().TrimStart('﻿') != Header) return false;
                var have = new List<string>();
                for (int i = 1; i < l.Length; i++)
                {
                    string s = l[i].TrimEnd('\r');
                    if (s.Length == 0) continue;
                    int t = s.IndexOf('\t');
                    have.Add(t >= 0 ? s.Substring(0, t) : s);
                }
                if (have.Count != list.Count) return false;
                for (int i = 0; i < list.Count; i++)
                    if (have[i] != list[i] || !File.Exists(Path.Combine(Dir, list[i] + ".png")) || !File.Exists(Path.Combine(Dir, list[i] + "-portrait.png"))) return false;
                return true;
            }
            catch (System.Exception) { return false; }
        }

        static RenderTexture NewRt(int w, int h, int aa)
        {
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32);
            rt.antiAliasing = aa;
            rt.hideFlags = HideFlags.HideAndDontSave;
            rt.Create();
            return rt;
        }

        static Texture2D NewTex(int w, int h)
        {
            return new Texture2D(w, h, TextureFormat.ARGB32, false) { hideFlags = HideFlags.HideAndDontSave };
        }

        static void Free(Object o) { if (o != null) Object.Destroy(o); }

        static void EndJob()
        {
            todo = null;
            if (frameRt != null) frameRt.Release();
            if (portraitRt != null) portraitRt.Release();
            Free(frameRt); Free(portraitRt); Free(readA); Free(readB); Free(readPA); Free(readPB); Free(stripTex); Free(portraitTex);
            frameRt = portraitRt = null;
            readA = readB = readPA = readPB = stripTex = portraitTex = null;
            strip = null;
            if (gen != null && gen.Alive) gen.A.Destroy();
            gen = null;
        }

        // ---------------------------------------------------------------- studio

        static bool EnsureStudio()
        {
            if (cam != null) return true;
            if (layer < 0) layer = PickLayer();
            var go = new GameObject("MWCoop-Studio");
            go.layer = layer;
            go.transform.position = GenPivot;
            cam = go.AddComponent<Camera>();
            cam.enabled = false;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
            cam.cullingMask = 1 << layer;
            cam.renderingPath = RenderingPath.Forward;
            cam.hdr = false;
            cam.useOcclusionCulling = false;
            cam.fieldOfView = Fov;
            // Cle (devant, a gauche de l'image, en hauteur), appoint froid (a droite), contre-jour (derriere).
            lights = new[]
            {
                MakeLight(go, "Cle", Quaternion.Euler(32f, 205f, 0f), new Color(1f, 0.96f, 0.90f), 1.0f),
                MakeLight(go, "Appoint", Quaternion.Euler(12f, 145f, 0f), new Color(0.80f, 0.87f, 1f), 0.45f),
                MakeLight(go, "Contre", Quaternion.Euler(22f, 18f, 0f), Color.white, 0.55f),
            };
            Log.Info("tenues : studio pret, couche " + layer + ", ambiance " + RenderSettings.ambientMode);
            return true;
        }

        static Light MakeLight(GameObject parent, string name, Quaternion rot, Color c, float intensity)
        {
            var go = new GameObject("MWCoop-Studio-" + name);
            go.layer = layer;
            go.transform.parent = parent.transform;
            go.transform.rotation = rot;
            Light l = go.AddComponent<Light>();
            l.type = LightType.Directional;
            l.color = c;
            l.intensity = intensity;
            l.cullingMask = 1 << layer;
            l.shadows = LightShadows.None;
            l.renderMode = LightRenderMode.ForcePixel;
            l.enabled = false;
            return l;
        }

        // Couche libre : sans nom et sans aucun objet (la plus haute) ; [Coop] CoucheApercu=N pour l'imposer.
        static int PickLayer()
        {
            int forced = Config.GetInt("Coop", "CoucheApercu", -1);
            if (forced >= 8 && forced <= 31) return forced;
            var count = new int[32];
            foreach (Object o in Resources.FindObjectsOfTypeAll(typeof(Transform))) count[((Transform)o).gameObject.layer]++;
            for (int l = 31; l >= 8; l--) if (count[l] == 0 && string.IsNullOrEmpty(LayerMask.LayerToName(l))) return l;
            for (int l = 31; l >= 8; l--) if (count[l] == 0) return l;
            Log.Warn("tenues : aucune couche libre, couche 31 partagee");
            return 31;
        }

        static Model NewModel(Vector3 pivot, string name)
        {
            Avatar a = Avatar.CreatePreview(name, layer);
            if (a == null) return null;
            a.Root.transform.position = pivot;
            return new Model { A = a, Pivot = pivot };
        }

        static void StartLoop()
        {
            if (loopRunning || Core.I == null) return;
            loopRunning = true;
            Core.I.StartCoroutine(Loop());
        }

        // ---------------------------------------------------------------- image par image

        static float liveYaw, liveT, dragAt;
        static string liveSkin;
        static int liveW = 256, liveH = 512, liveFrame = -100;
        static RenderTexture liveRt;
        static bool liveReady, liveBroken;

        // Apres les animations : tenue, angle et pose de chaque personnage a photographier dans cette image.
        public static void LateUpdate()
        {
            if (todo != null)
            {
                if (gen == null || !gen.Alive || cam == null) { Log.Warn("tenues : studio perdu, apercus interrompus"); EndJob(); }
                else Pose(gen, todo[skinIdx], step >= Frames ? -15f : -22.5f * step, 0f);   // vers sa gauche
            }
            bool want = Time.frameCount - liveFrame <= 2 && liveSkin != null && !liveBroken && PlayerSync.InGame;
            float now = Time.realtimeSinceStartup;
            if (!want)
            {
                if (live != null && live.Alive && live.A.Root.activeSelf) { live.A.Root.SetActive(false); ReleaseLive(); }
                return;
            }
            if (live == null || !live.Alive)
            {
                if (!EnsureStudio()) return;
                live = NewModel(LivePivot, "MWCoop-Studio-Menu");
                if (live == null) { liveBroken = true; Log.Warn("tenues : apercu du menu impossible (modele d'avatar introuvable)"); return; }
            }
            if (!live.A.Root.activeSelf) { live.A.Root.SetActive(true); liveYaw = 0f; liveT = now; }
            float dt = Mathf.Clamp(now - liveT, 0f, 0.1f);
            liveT = now;
            if (now - dragAt > 1.5f) liveYaw -= 25f * dt;     // tourne lentement vers sa gauche
            Pose(live, liveSkin, liveYaw, now);
            StartLoop();
        }

        static void Pose(Model m, string skin, float yaw, float animTime)
        {
            if (m.Skin != skin) { m.A.PreviewSkin(skin); m.Skin = skin; }
            Quaternion r = Quaternion.Euler(0f, yaw, 0f);
            Transform t = m.A.Root.transform;
            t.rotation = r;
            t.position = m.Pivot - r * m.Center;
            m.A.PoseStanding(animTime);
            m.Posed = true;
        }

        static IEnumerator Loop()
        {
            int idle = 0;
            while (idle < 30)
            {
                yield return endOfFrame;
                bool any = false;
                if (gen != null && gen.Posed && todo != null)
                {
                    gen.Posed = false;
                    any = true;
                    try { GenStep(); }
                    catch (System.Exception e) { Log.Error("tenues : " + e); EndJob(); }
                }
                if (live != null && live.Posed)
                {
                    live.Posed = false;
                    any = true;
                    try { LiveStep(); }
                    catch (System.Exception e) { Log.Error("tenues (menu) : " + e); liveBroken = true; }
                }
                idle = any || todo != null ? 0 : idle + 1;
            }
            loopRunning = false;
        }

        // Premiere image d'un personnage : sa taille et l'axe du plateau, d'apres la boite du maillage anime.
        static void Measure(Model m)
        {
            Bounds b = m.A.BodyBounds;
            float h = b.size.y;
            Vector3 off = b.center - m.Pivot;
            if (h > 1f && h < 3.2f && Mathf.Abs(off.x) < 1f && Mathf.Abs(off.z) < 1f)
            {
                m.FeetOff = b.min.y - m.Pivot.y;
                m.Height = h;
                m.Center = new Vector3(off.x, 0f, off.z);
            }
            else
            {
                Transform head = m.A.HeadBone;
                m.FeetOff = 0f;
                m.Height = head != null ? head.position.y - m.Pivot.y + 0.22f : 1.9f;
                m.Center = Vector3.zero;
                Log.Warn("tenues : boite du personnage douteuse (" + b.size.ToString("F2") + "), mesure par la tete");
            }
            m.Measured = true;
            Log.Info("tenues : personnage de " + m.Height.ToString("F2") + " m (pieds " + m.FeetOff.ToString("F2") + ", axe " + m.Center.ToString("F2") + ")");
        }

        static void GenStep()
        {
            if (!gen.Measured) { Measure(gen); return; }
            string s = todo[skinIdx];
            if (step < Frames)
            {
                FrameBody(gen, FrameW, FrameH, 8f, 10f);
                Color32[] px = Matte(frameRt, readA, readB);
                for (int y = 0; y < FrameH; y++) System.Array.Copy(px, y * FrameW, strip, y * FrameW * Frames + step * FrameW, FrameW);
                step++;
                return;
            }
            FramePortrait(gen);
            portraitTex.SetPixels32(Matte(portraitRt, readPA, readPB));
            stripTex.SetPixels32(strip);
            bool ok = Write(Path.Combine(Dir, s + "-portrait.png"), portraitTex.EncodeToPNG());
            ok &= Write(Path.Combine(Dir, s + ".png"), stripTex.EncodeToPNG());
            if (ok) written++;
            step = 0;
            if (++skinIdx >= todo.Count) Finish();
        }

        static void Finish()
        {
            var sb = new System.Text.StringBuilder(Header).Append('\n');
            var names = new HashSet<string>();
            foreach (string s in todo) { sb.Append(s).Append('\t').Append(Menu.SkinLabel(s)).Append('\n'); names.Add(s); }
            // Images de tenues qui n'existent plus.
            try
            {
                foreach (string f in Directory.GetFiles(Dir, "*.png"))
                {
                    string n = Path.GetFileNameWithoutExtension(f);
                    if (n.EndsWith("-portrait")) n = n.Substring(0, n.Length - 9);
                    if (!names.Contains(n)) File.Delete(f);
                }
            }
            catch (System.Exception e) { Log.Warn("tenues : menage " + e.Message); }
            bool ok = written == todo.Count && Write(Path.Combine(Dir, "index.txt"), new System.Text.UTF8Encoding(false).GetBytes(sb.ToString()));
            string msg = written + " apercus ecrits en " + (Time.realtimeSinceStartup - jobT0).ToString("F1") + " s (" + Path.GetFullPath(Dir) + ")";
            if (ok) Log.Info("tenues : " + msg);
            else Log.Warn("tenues : " + msg + ", " + (todo.Count - written) + " en echec : index.txt non ecrit");
            if (testing) Log.Info("autotest : tenues " + (ok ? "termine, " : "ECHEC, ") + msg);
            EndJob();
        }

        // Nom temporaire puis renomme : le lanceur ne lit jamais un fichier a moitie ecrit.
        static bool Write(string path, byte[] data)
        {
            string tmp = path + ".tmp";
            try
            {
                File.WriteAllBytes(tmp, data);
                if (File.Exists(path)) File.Delete(path);
                File.Move(tmp, path);
                return true;
            }
            catch (System.Exception e)
            {
                Log.Warn("tenues : ecriture " + path + " : " + e.Message);
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch (System.Exception) { }
                return false;
            }
        }

        static void LiveStep()
        {
            if (!live.Measured) { Measure(live); return; }
            if (liveRt == null || liveRt.width != liveW || liveRt.height != liveH)
            {
                ReleaseLive();
                liveRt = NewRt(liveW, liveH, Mathf.Clamp(Config.GetInt("Coop", "ApercuTenuesAA", 4), 1, 8));
            }
            FrameBody(live, liveW, liveH, liveH * 0.035f, liveH * 0.03f);
            Render(liveRt, LiveBg);
            liveReady = true;
        }

        static void ReleaseLive()
        {
            liveReady = false;
            if (liveRt == null) return;
            liveRt.Release();
            Object.Destroy(liveRt);
            liveRt = null;
        }

        // ---------------------------------------------------------------- cadrage et rendu

        // Corps entier, camera horizontale devant le personnage (+Z, qui regarde vers -Z) : pieds a 'bottom' px du bas,
        // haut de la tete a 'top' px du haut.
        static void FrameBody(Model m, int w, int h, float bottom, float top)
        {
            float half = h * 0.5f;
            float fb = (bottom - half) / half, ft = (half - top) / half;
            float T = m.Height / (ft - fb);                  // demi-hauteur vue a la distance de l'axe
            float cy = m.Pivot.y + m.FeetOff - fb * T;
            Place(new Vector3(m.Pivot.x, cy, m.Pivot.z), T, w, h);
        }

        // Tete et epaules.
        static void FramePortrait(Model m)
        {
            Transform head = m.A.HeadBone;
            Vector3 c = head != null ? head.position : m.Pivot + Vector3.up * (m.FeetOff + m.Height * 0.88f);
            c.y -= m.Height * 0.06f;
            Place(c, m.Height * 0.18f, PortraitSize, PortraitSize);
        }

        static void Place(Vector3 target, float halfHeight, int w, int h)
        {
            float d = halfHeight / Mathf.Tan(Fov * 0.5f * Mathf.Deg2Rad);
            cam.transform.position = target + Vector3.forward * d;
            cam.transform.rotation = Quaternion.LookRotation(Vector3.back);
            cam.fieldOfView = Fov;
            cam.aspect = w / (float)h;
            cam.nearClipPlane = Mathf.Max(0.05f, d - 2.5f);
            cam.farClipPlane = d + 2.5f;
        }

        // Rendu sur fond noir puis blanc : couleur et couverture (alpha) de chaque pixel.
        static Color32[] Matte(RenderTexture rt, Texture2D ta, Texture2D tb)
        {
            Render(rt, new Color(0f, 0f, 0f, 0f));
            Read(rt, ta);
            Render(rt, new Color(1f, 1f, 1f, 0f));
            Read(rt, tb);
            Color32[] a = ta.GetPixels32(), b = tb.GetPixels32();
            for (int i = 0; i < a.Length; i++)
            {
                Color32 p = a[i], q = b[i];
                int cov = 255 - ((q.r - p.r) + (q.g - p.g) + (q.b - p.b)) / 3;
                if (cov <= 2) { a[i] = new Color32(0, 0, 0, 0); continue; }
                if (cov > 255) cov = 255;
                a[i] = new Color32(Un(p.r, cov), Un(p.g, cov), Un(p.b, cov), (byte)cov);
            }
            return a;
        }

        static byte Un(byte c, int a)
        {
            int v = c * 255 / a;
            return (byte)(v > 255 ? 255 : v);
        }

        static void Read(RenderTexture rt, Texture2D t)
        {
            RenderTexture prev = RenderTexture.active;
            RenderTexture.active = rt;
            t.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0, false);
            RenderTexture.active = prev;
        }

        static Light[] sceneLights;
        static int[] sceneMasks;
        static float sceneLightsAt;

        static void Render(RenderTexture rt, Color bg)
        {
            // Lumieres directionnelles du jeu (soleil...) : relues toutes les 5 s, otees de la couche du studio.
            if (sceneLights == null || Time.realtimeSinceStartup >= sceneLightsAt)
            {
                sceneLightsAt = Time.realtimeSinceStartup + 5f;
                var l = new List<Light>();
                foreach (Object o in Object.FindObjectsOfType(typeof(Light)))
                {
                    var li = (Light)o;
                    if (li.type == LightType.Directional && System.Array.IndexOf(lights, li) < 0) l.Add(li);
                }
                sceneLights = l.ToArray();
                sceneMasks = new int[sceneLights.Length];
            }
            bool fog = RenderSettings.fog;
            Color amb = RenderSettings.ambientLight;
            SphericalHarmonicsL2 probe = RenderSettings.ambientProbe;
            bool flat = RenderSettings.ambientMode == AmbientMode.Flat;
            try
            {
                RenderSettings.fog = false;
                if (flat) RenderSettings.ambientLight = Ambient;
                var sh = new SphericalHarmonicsL2();
                sh.AddAmbientLight(Ambient);
                RenderSettings.ambientProbe = sh;
                for (int i = 0; i < sceneLights.Length; i++)
                    if (sceneLights[i] != null) { sceneMasks[i] = sceneLights[i].cullingMask; sceneLights[i].cullingMask &= ~(1 << layer); }
                foreach (Light li in lights) li.enabled = true;
                cam.targetTexture = rt;
                cam.backgroundColor = bg;
                cam.Render();
            }
            finally
            {
                cam.targetTexture = null;
                foreach (Light li in lights) li.enabled = false;
                for (int i = 0; i < sceneLights.Length; i++) if (sceneLights[i] != null) sceneLights[i].cullingMask = sceneMasks[i];
                if (flat) RenderSettings.ambientLight = amb;
                RenderSettings.ambientProbe = probe;
                RenderSettings.fog = fog;
            }
        }

        // ---------------------------------------------------------------- menu F10

        public static bool LiveEnabled { get { return PlayerSync.InGame && !liveBroken && Config.GetInt("Coop", "ApercuTenuesMenu", 1) != 0; } }

        // A chaque passage du menu qui montre l'apercu : tenue voulue et taille (pixels). Rend la vue de l'image
        // precedente, null tant qu'il n'y en a pas.
        public static Texture Live(string skin, int w, int h)
        {
            liveFrame = Time.frameCount;
            liveSkin = skin ?? "";
            liveW = Mathf.Clamp(w, 16, 1024);
            liveH = Mathf.Clamp(h, 16, 2048);
            return liveReady && liveRt != null ? liveRt : null;
        }

        // Glisser a la souris : la souris vers la droite tourne le personnage vers sa gauche (on le fait tourner par devant).
        public static void Drag(float dx)
        {
            liveYaw -= dx * 0.6f;
            dragAt = Time.realtimeSinceStartup;
        }

        // ---------------------------------------------------------------- autotest "tenues"
        // 25 s : apercus refaits (comme RefaireTenues=1) ; 40 s : menu F10 sur APPARENCE, une autre tenue en surbrillance
        // toutes les 2 s (captures [Test] Captures=...) ; 50 s : menu ferme.

        static int testK = -1;
        static bool testForced, testEnd;

        public static void Test(string mode, float t)
        {
            if (mode != "tenues") return;
            if (!testForced && t >= 25f)
            {
                testForced = testing = true;
                Log.Info("autotest : tenues regeneration forcee");
                checkAt = -1;
                tries = 0;
                if (todo != null) Log.Info("autotest : tenues deja en cours");
                else TryStart(true);
            }
            if (testEnd || t < 40f) return;
            int k = (int)((t - 40f) / 2f);
            if (k >= 5)
            {
                testEnd = true;
                Menu.TestApparence(-1);
                Log.Info("autotest : tenues menu ferme" + (todo != null ? " (apercus encore en cours, " + skinIdx + "/" + todo.Count + ")" : "") + (liveReady ? ", apercu du menu rendu" : ", apercu du menu jamais rendu"));
                return;
            }
            if (k == testK) return;
            testK = k;
            Log.Info("autotest : tenues menu APPARENCE, surbrillance " + Menu.TestApparence(k));
        }
    }
}
