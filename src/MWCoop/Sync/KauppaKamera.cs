using System;
using System.Collections.Generic;
using System.IO;
using HutongGames.PlayMaker;
using MWCoop.Net;
using UnityEngine;

namespace MWCoop
{
    // Camescope d'epaule (etape 3 du coin video, demande de JD, 12/10) : vendu a l'Osuuskauppa ("kamera").
    //  - il faut une cassette : comme le magnetoscope (cassette en main + clic sur le camescope pose ; F tenu = ejecter).
    //  - tenu en main : F = l'equiper (comme la raclette a givre ; demande de JD, 12/10) : la main du jeu (PickUp) est
    //    mise en pause, le camescope monte a l'oeil (les autres le voient a l'oeil, les mains dessus : Gestures suit
    //    l'objet tenu) et on regarde dans le viseur (image 4:3 facon VHS, compteur, REC ; molette = zoom). Equipe (regle
    //    de JD, 12/10) : clic gauche = filmer, puis pause, puis reprise... (R pareil) ; F (ou clic droit) = le rebaisser
    //    dans les mains, ce qui termine la prise ; ensuite un clic droit le jette, comme tout objet du jeu. En main sans
    //    viseur : R = enregistrer / arreter. Pose : R = enregistrer / arreter, F tenu = ejecter ; il filme devant lui (pose sans etre lance, il se
    //    tourne vers la ou regardait le joueur). Touches : [Touches] CameraViseur (F), CameraEnregistrer (R).
    //  - enregistrement chez celui qui l'a lance : camera a part 256x192 a 10 images/s, effet VHS calcule sur l'image
    //    (couleurs qui bavent, neige, lignes, bas d'image qui tremble), JPEG ; son = ce qu'entend le joueur (sortie de
    //    l'AudioListener) en mono 11 025 Hz mu-law. Fichier <profil>\videot\<film>.mwv, au plus [Boutique] VideoMinutes
    //    (10) par prise. A l'arret, la cassette du camescope porte le film. Un autre joueur peut l'arreter (F).
    //  - voyant rouge du camescope allume chez tous pendant l'enregistrement (Data "<cassette>|1" ; "|2" en pause).
    // Contenu d'une cassette : "tyhja" (vierge) ou "<film>~<titre>".
    // Fichier .mwv : "MWV1", u16 largeur, u16 hauteur, u8 images/s, u16 frequence du son ; puis des blocs : u8 sorte
    // (1 image JPEG, 2 son mu-law), u32 instant en ms, i32 longueur, octets.
    public static partial class Kauppa
    {
        public const string KKamera = "kamera";
        const int RecW = 256, RecH = 192, RecFps = 10, RecRate = 11025, MaxClips = 40;

        public static string TapeFilm(string content) { int i = content.IndexOf('~'); string f = i < 0 ? content : content.Substring(0, i); return f == "tyhja" ? "" : f; }
        public static string TapeTitle(string content) { int i = content.IndexOf('~'); return i < 0 ? "" : content.Substring(i + 1); }
        static string FilmDir { get { return Path.Combine(Log.DataDir, "videot"); } }
        static string FilmPath(string film) { return Path.Combine(FilmDir, film + ".mwv"); }

        // ---------------------------------------------------------------- modele (objectif vers +z)
        static void KameraModel(GameObject g, out Vector3 size)
        {
            Material body = Plain(new Color(0.16f, 0.16f, 0.17f)), side = TexMat("kamera-sivu.png");
            Piece(g, PrimitiveType.Cube, Vector3.zero, Vector3.zero, new Vector3(0.11f, 0.14f, 0.30f), body);
            Piece(g, PrimitiveType.Quad, new Vector3(-0.0558f, 0f, 0f), new Vector3(0, 90, 0), new Vector3(0.285f, 0.126f, 1f), side);
            Piece(g, PrimitiveType.Quad, new Vector3(0.0558f, 0f, 0f), new Vector3(0, -90, 0), new Vector3(0.285f, 0.126f, 1f), side);
            Piece(g, PrimitiveType.Cylinder, new Vector3(0f, 0.01f, 0.19f), new Vector3(90, 0, 0), new Vector3(0.075f, 0.04f, 0.075f), blackPlastic);
            GameObject glass = Piece(g, PrimitiveType.Cylinder, new Vector3(0f, 0.01f, 0.2305f), new Vector3(90, 0, 0), new Vector3(0.058f, 0.002f, 0.058f), Plain(new Color(0.07f, 0.09f, 0.16f)));
            glass.name = "linssi";
            Piece(g, PrimitiveType.Cube, new Vector3(-0.072f, 0.05f, -0.06f), Vector3.zero, new Vector3(0.035f, 0.035f, 0.09f), blackPlastic);   // viseur
            Piece(g, PrimitiveType.Cylinder, new Vector3(-0.072f, 0.05f, -0.115f), new Vector3(90, 0, 0), new Vector3(0.03f, 0.01f, 0.03f), blackPlastic);
            Piece(g, PrimitiveType.Cube, new Vector3(0f, 0.093f, 0.0f), Vector3.zero, new Vector3(0.025f, 0.018f, 0.2f), blackPlastic);          // poignee
            foreach (float z in new[] { -0.08f, 0.08f }) Piece(g, PrimitiveType.Cube, new Vector3(0f, 0.077f, z), Vector3.zero, new Vector3(0.02f, 0.016f, 0.02f), blackPlastic);
            Piece(g, PrimitiveType.Cube, new Vector3(0f, -0.082f, -0.07f), Vector3.zero, new Vector3(0.09f, 0.024f, 0.12f), blackPlastic);        // epauliere
            Piece(g, PrimitiveType.Cylinder, new Vector3(0.035f, 0.088f, 0.135f), new Vector3(90, 0, 0), new Vector3(0.02f, 0.035f, 0.02f), blackPlastic);   // micro
            Shader uc = Shader.Find("Unlit/Color");
            GameObject led = Piece(g, PrimitiveType.Sphere, new Vector3(0.035f, 0.05f, 0.152f), Vector3.zero, new Vector3(0.012f, 0.012f, 0.012f),
                                   uc != null ? new Material(uc) { color = new Color(1f, 0.08f, 0.05f) } : Plain(Color.red));
            led.name = "rec";
            led.GetComponent<Renderer>().enabled = false;
            // recentre sur la boite des pieces
            Bounds b = new Bounds(); bool first = true;
            foreach (Renderer r in g.GetComponentsInChildren<Renderer>(true)) { if (first) { b = r.bounds; first = false; } else b.Encapsulate(r.bounds); }
            foreach (Transform c in g.transform) c.localPosition -= b.center;
            size = b.size;
        }

        // ---------------------------------------------------------------- en main / pose
        static Esine HeldKamera()
        {
            foreach (Esine e in esineet)
                if (e.Kind == KKamera && e.Go != null && e.Go.transform.parent != null && Game.UnderPlayer(e.Go.transform)) return e;
            return null;
        }

        static HutongGames.PlayMaker.FsmBool inMenu;
        static bool KameraBusy
        {
            get
            {
                if (inMenu == null) inMenu = HutongGames.PlayMaker.FsmVariables.GlobalVariables.FindFsmBool("PlayerInMenu");
                return Menu.Open || Menu.ChatOpen || MapPanel.Open || CheatPanel.Open || WalletPanel.Open || Respawn.Choosing || inMenu != null && inMenu.Value || DroneCam.Active || MontageOpen;
            }
        }

        static Esine lastHeldKamera;

        // camera du joueur d'ici (PlayerSync ; sans session, la camera FPS du jeu)
        static Transform EyeT
        {
            get
            {
                Transform c = PlayerSync.LocalCamera;
                if (c != null && c.GetComponent<Camera>() != null) return c;
                GameObject g = GameObject.Find("PLAYER/Pivot/AnimPivot/Camera/FPSCamera/FPSCamera");
                if (g != null && g.GetComponent<Camera>() != null) return g.transform;
                return Camera.main != null ? Camera.main.transform : null;
            }
        }

        static void TickKamera(float now)
        {
            Esine held = HeldKamera();
            // pose (lache, pas lance) : debout, objectif vers la ou regardait le joueur
            if (lastHeldKamera != null && held != lastHeldKamera && lastHeldKamera.Go != null && lastHeldKamera.Go.transform.parent == null
                && lastHeldKamera.Rb != null && lastHeldKamera.Rb.velocity.magnitude < 2.5f && EyeT != null)
            {
                Vector3 f = EyeT.forward; f.y = 0f;
                if (f.sqrMagnitude > 1e-4f) { lastHeldKamera.Go.transform.rotation = Quaternion.LookRotation(f.normalized); lastHeldKamera.Rb.angularVelocity = Vector3.zero; }
            }
            lastHeldKamera = held;
            TickEquip(now, held);
            if (held != null && eqStage == 0 && !KameraBusy && lookedItem == null && looked == null)
            {
                if (Input.GetKeyDown(Keys.CameraViewfinder) && VehicleSync.LocalDrivingRoot == null) Equip(held);
                else if (Input.GetKeyDown(Keys.CameraRec)) { if (rec != null && rec.Cam == held) StopRec("touche"); else StartRec(held); }
            }
            if (Viewfinder)
            {
                if (!vfForced && (held == null || KameraBusy || VehicleSync.LocalDrivingRoot != null) || vfCam == null) ExitViewfinder();
                else
                {
                    float wheel = Input.GetAxis("Mouse ScrollWheel");
                    if (Mathf.Abs(wheel) > 1e-3f) { vfFov = Mathf.Clamp(vfFov * Mathf.Exp(-wheel * 2.5f), 7f, 55f); vfZoomShown = now + 1.5f; }
                }
            }
            if (rec != null)
            {
                Esine k = rec.Cam;
                if (k.Go == null || !esineet.Contains(k)) StopRec("camescope parti");
                else if (!k.Data.EndsWith("|1") && !k.Data.EndsWith("|2")) StopRec("arrete par un autre joueur");
                else
                {
                    PollTap();
                    if (rec.Paused) tapOut.Clear();   // (en pause : ni image ni son)
                    else if (now >= rec.NextFrame) { rec.NextFrame = Mathf.Max(rec.NextFrame + 1f / RecFps, now); RecFrame(now); }
                    if (rec != null && RecSeconds(rec, now) >= MaxRecSeconds) { StopRec("fin de bande"); Hud.Toast(Lang.T("Caméscope : fin de bande", "Camcorder: end of tape")); }
                }
            }
            // voyants REC (chez tous)
            bool blink = Mathf.Repeat(now, 1f) < 0.6f;
            foreach (Esine e in esineet)
            {
                if (e.Kind != KKamera || e.Go == null) continue;
                Transform led = e.Go.transform.Find("rec");
                if (led != null) { Renderer r = led.GetComponent<Renderer>(); bool on = e.Data.EndsWith("|1") && blink; if (r != null && r.enabled != on) r.enabled = on; }   // (eteint en pause : "|2")
            }
        }

        // ---------------------------------------------------------------- equiper (camescope a l'oeil)
        static Esine eqCam;
        static int eqStage;              // 0 non, 1 en attente de la main du jeu, 2 equipe
        static int eqSince, eqReenable = -1;
        static PlayMakerFSM eqFsm;
        static bool eqRestart, eqKin;
        static Vector3 eqPos; static Quaternion eqRot;
        static readonly List<KeyValuePair<GameObject, int>> eqLayers = new List<KeyValuePair<GameObject, int>>();

        static PlayMakerFSM PickUpFsm()
        {
            GameObject h = GameObject.Find("PLAYER/Pivot/AnimPivot/Camera/FPSCamera/1Hand_Assemble/Hand");
            return h != null ? Game.FsmOn(h, "PickUp") : null;
        }

        static void Equip(Esine k)
        {
            eqCam = k; eqStage = 1; eqSince = Time.frameCount;
            EnterViewfinder();
            if (!Viewfinder) { eqStage = 0; eqCam = null; }
        }

        // Rendre le camescope a la main du jeu (pose d'avant, physique, couches) ; la main reprend l'image suivante
        // (pas dans l'image du clic droit : elle le lancerait aussitot).
        static void Unequip()
        {
            if (eqStage == 0) return;
            if (eqStage == 2 && eqCam != null && eqCam.Go != null)
            {
                Transform t = eqCam.Go.transform;
                t.localPosition = eqPos; t.localRotation = eqRot;
                if (eqCam.Rb != null) eqCam.Rb.isKinematic = eqKin;
            }
            foreach (var kv in eqLayers) if (kv.Key != null) kv.Key.layer = kv.Value;
            eqLayers.Clear();
            if (eqStage == 2 && eqFsm != null) eqReenable = Time.frameCount + 1;
            if (rec != null && rec.Cam == eqCam) StopRec("camescope baisse");   // (baisser termine la prise)
            eqStage = 0; eqCam = null;
        }

        static void TickEquip(float now, Esine held)
        {
            if (eqReenable >= 0 && Time.frameCount >= eqReenable)
            {
                eqReenable = -1;
                if (eqFsm != null) { eqFsm.enabled = true; eqFsm.Fsm.RestartOnEnable = eqRestart; }
                eqFsm = null;
            }
            if (eqStage == 0) return;
            if (eqCam == null || eqCam.Go == null || held != eqCam || !Viewfinder) { if (Viewfinder) ExitViewfinder(); else Unequip(); return; }
            if (eqStage == 1)
            {
                // la touche Use du jeu fait un tour (objet : Check item -> Hand -> Set pivot 2 ; piece, notre cas avec
                // l'etiquette PART : State 4, une image) : attendre que la main tienne de nouveau (« Part picked » ou
                // « Item picked » ; 0.86.0 n'attendait que le second : en vrai, sortie du viseur au bout d'une seconde)
                PlayMakerFSM f = PickUpFsm();
                if (f == null || Time.frameCount - eqSince > 60) { L("video : camescope pas equipe (main du jeu " + (f != null ? f.ActiveStateName : "introuvable") + ")"); ExitViewfinder(); return; }
                if (f.ActiveStateName != "Part picked" && f.ActiveStateName != "Item picked") return;
                eqFsm = f; eqRestart = f.Fsm.RestartOnEnable;
                f.Fsm.RestartOnEnable = false;   // (a la reprise : toujours « Item picked », l'objet en main)
                f.enabled = false;
                Transform t = eqCam.Go.transform, eye = EyeT;
                eqPos = t.localPosition; eqRot = t.localRotation;
                if (eqCam.Rb != null) { eqKin = eqCam.Rb.isKinematic; eqCam.Rb.isKinematic = true; }
                if (eye != null) { t.position = eye.position + eye.forward * 0.2f + eye.right * 0.04f - eye.up * 0.03f; t.rotation = eye.rotation; }
                // (pieces sur la couche des objets tenus : la camera du jeu, qui dessine le viseur, ne les voit pas)
                eqLayers.Clear();
                foreach (Transform c in eqCam.Go.GetComponentsInChildren<Transform>(true)) { eqLayers.Add(new KeyValuePair<GameObject, int>(c.gameObject, c.gameObject.layer)); c.gameObject.layer = 16; }
                eqStage = 2;
                L("video : camescope equipe");
                return;
            }
            if (KameraBusy) return;
            if (Input.GetMouseButtonDown(0) || Input.GetKeyDown(Keys.CameraRec)) { if (rec != null && rec.Cam == eqCam) Pause(!rec.Paused); else StartRec(eqCam); }
            else if (Input.GetMouseButtonDown(1) || Input.GetKeyDown(Keys.CameraViewfinder)) ExitViewfinder();
        }

        static void Pause(bool on)
        {
            if (rec == null || rec.Paused == on) return;
            float now = Time.realtimeSinceStartup;
            if (on) { rec.Paused = true; rec.PauseAt = now; }
            else { rec.Paused = false; rec.PausedTotal += now - rec.PauseAt; rec.NextFrame = now; tapOut.Clear(); }
            if (rec.Cam.Go != null && esineet.Contains(rec.Cam)) SetData(rec.Cam, Tape(rec.Cam) + (on ? "|2" : "|1"));
            L("video : enregistrement " + (on ? "en pause" : "repris"));
        }

        // duree filmee (pauses retirees)
        static float RecSeconds(Rec r, float now) { return now - r.Start - r.PausedTotal - (r.Paused ? now - r.PauseAt : 0f); }

        static float MaxRecSeconds { get { return Mathf.Clamp(Config.GetFloat("Boutique", "VideoMinutes", 10f), 0.2f, 60f) * 60f; } }

        // ---------------------------------------------------------------- viseur
        public static bool Viewfinder;
        static bool vfForced;   // (essais : viseur sans camescope en main)
        static Camera vfCam, vfHand, vfGui;
        static Behaviour vfLayer;
        static bool vfHandWas, vfGuiWas, vfLayerWas, vfHooked;
        static RenderTexture vfRT;
        static float vfFovSaved, vfFov = 45f, vfZoomShown = -1f;
        static int vfMask = -1;
        static Texture2D vfScan, vfNoise;
        static Color32[] vfNoisePx;
        static int vfNoiseFrame;

        static void EnterViewfinder()
        {
            Transform c = EyeT;
            vfCam = c != null ? c.GetComponent<Camera>() : null;
            if (vfCam == null) { L("video : viseur, camera du joueur introuvable"); return; }
            Transform hc = c.Find("HandCamera");
            vfHand = hc != null ? hc.GetComponent<Camera>() : null;
            if (vfHand != null) { vfHandWas = vfHand.enabled; vfHand.enabled = false; }
            GameObject g = Game.FindAny("GUI/CAM");
            vfGui = g != null ? g.GetComponent<Camera>() : null;
            if (vfGui != null) { vfGuiWas = vfGui.enabled; vfGui.enabled = false; }
            vfLayer = vfCam.GetComponent("GUILayer") as Behaviour;
            if (vfLayer != null) { vfLayerWas = vfLayer.enabled; vfLayer.enabled = false; }
            if (vfRT == null) { vfRT = new RenderTexture(480, 360, 24) { name = "kauppa_viseur", filterMode = FilterMode.Bilinear }; vfRT.Create(); }
            vfFovSaved = vfCam.fieldOfView;
            vfMask = vfCam.cullingMask;
            vfCam.cullingMask = vfMask & ~(1 << 16);   // (le camescope equipe, a l'oeil, est sur la couche des objets tenus)
            vfFov = 45f;
            if (!vfHooked) { vfHooked = true; Camera.onPreCull += VfPreCull; }
            Viewfinder = true;
            try { MasterAudio.PlaySound3DAtVector3AndForget("HouseFoley", c.position, 0.4f, null, 0f, "mailbox_open"); } catch { }
            L("video : viseur du camescope");
        }

        static void ExitViewfinder()
        {
            Unequip();
            Viewfinder = false; vfForced = false;
            if (vfCam != null) { vfCam.targetTexture = null; vfCam.fieldOfView = vfFovSaved; vfCam.cullingMask = vfMask; }
            if (vfHand != null) vfHand.enabled = vfHandWas;
            if (vfGui != null) vfGui.enabled = vfGuiWas;
            if (vfLayer != null) vfLayer.enabled = vfLayerWas;
            vfCam = null; vfHand = null; vfGui = null; vfLayer = null;
        }

        static void VfPreCull(Camera c)
        {
            if (!Viewfinder || c == null || c != vfCam) return;
            if (c.targetTexture != vfRT) c.targetTexture = vfRT;
            c.fieldOfView = vfFov;
        }

        // Core.OnGUI : image du viseur et son affichage (rien d'autre du mod ni du jeu)
        public static void DrawViewfinder()
        {
            if (!Viewfinder || vfRT == null) return;
            if (Event.current.type != EventType.Repaint) return;
            float W = Screen.width, H = Screen.height, now = Time.realtimeSinceStartup;
            Style.Fill(new Rect(0, 0, W, H), Color.black);
            float h = H, w = Mathf.Round(H * 4f / 3f);
            if (w > W) { w = W; h = Mathf.Round(W * 3f / 4f); }
            var r = new Rect(Mathf.Round((W - w) / 2), Mathf.Round((H - h) / 2), w, h);
            Color keep = GUI.color;
            GUI.color = Color.white;
            GUI.DrawTexture(r, vfRT, ScaleMode.StretchToFill, false);
            // couleurs qui bavent : copies teintees decalees
            float sh = Mathf.Max(2f, Style.Px(3));
            GUI.color = new Color(1f, 0.25f, 0.2f, 0.20f); GUI.DrawTexture(new Rect(r.x + sh, r.y, r.width, r.height), vfRT, ScaleMode.StretchToFill, true);
            GUI.color = new Color(0.2f, 0.45f, 1f, 0.14f); GUI.DrawTexture(new Rect(r.x - sh * 0.7f, r.y, r.width, r.height), vfRT, ScaleMode.StretchToFill, true);
            GUI.color = keep;
            Style.Fill(r, new Color(0.10f, 0.07f, 0.02f, 0.10f));   // teinte chaude, noirs releves
            // lignes et neige
            if (vfScan == null)
            {
                vfScan = new Texture2D(1, 4, TextureFormat.ARGB32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Repeat };
                vfScan.SetPixels32(new[] { new Color32(0, 0, 0, 70), new Color32(0, 0, 0, 0), new Color32(0, 0, 0, 0), new Color32(0, 0, 0, 25) });
                vfScan.Apply(false);
                vfNoise = new Texture2D(320, 240, TextureFormat.ARGB32, false) { filterMode = FilterMode.Bilinear };
                vfNoisePx = new Color32[320 * 240];
            }
            if (Time.frameCount - vfNoiseFrame >= 2)
            {
                vfNoiseFrame = Time.frameCount;
                for (int i = 0; i < vfNoisePx.Length; i++) { byte b = (byte)noiseRnd.Next(256); vfNoisePx[i] = new Color32(b, b, b, (byte)(noiseRnd.Next(100) < 12 ? 20 : 0)); }
                vfNoise.SetPixels32(vfNoisePx); vfNoise.Apply(false);
            }
            GUI.DrawTextureWithTexCoords(r, vfScan, new Rect(0, 0, 1, r.height / Mathf.Max(3f, Style.Px(3))));
            GUI.DrawTexture(r, vfNoise);
            float band = Mathf.Repeat(now * 0.11f, 1.25f) - 0.1f;   // bande de suivi qui descend
            if (band > 0f && band < 1f) { GUI.color = new Color(1f, 1f, 1f, 0.8f); GUI.DrawTexture(new Rect(r.x, r.y + band * r.height, r.width, r.height * 0.035f), vfNoise); GUI.color = keep; }
            // affichage du camescope
            Esine k = HeldKamera();
            string tape = k != null ? Tape(k) : "";
            bool recording = rec != null && (k == null || rec.Cam == k);
            bool blink = Mathf.Repeat(now, 1f) < 0.6f;
            int big = 40, small = 26;
            float m = Style.Px(40);
            Color white = new Color(0.96f, 0.97f, 0.95f), red = new Color(1f, 0.15f, 0.1f), amber = new Color(1f, 0.75f, 0.2f);
            var tl = new Rect(r.x + m, r.y + m * 0.8f, r.width * 0.5f, Style.Px(50));
            if (recording && rec.Paused) { if (blink) Style.Text(tl, "❚❚ PAUSE", big, TextAnchor.UpperLeft, amber); }
            else if (recording) { if (blink) Style.Text(tl, "● REC", big, TextAnchor.UpperLeft, red); }
            else if (k != null && tape.Length == 0) { if (blink) Style.Text(tl, Lang.T("PAS DE CASSETTE", "NO TAPE"), big, TextAnchor.UpperLeft, amber); }
            else Style.Text(tl, "STBY", big, TextAnchor.UpperLeft, white);
            // pile et vitesse de bande
            var tr = new Rect(r.xMax - m - Style.Px(110), r.y + m * 0.8f, Style.Px(110), Style.Px(50));
            Style.Text(new Rect(tr.x - Style.Px(80), tr.y, Style.Px(70), tr.height), "SP", big, TextAnchor.UpperRight, white);
            float bx = tr.x + Style.Px(10), by = tr.y + Style.Px(10), bw = Style.Px(80), bh = Style.Px(30), th = Mathf.Max(2f, Style.Px(3));
            Style.Fill(new Rect(bx, by, bw, th), white); Style.Fill(new Rect(bx, by + bh - th, bw, th), white);
            Style.Fill(new Rect(bx, by, th, bh), white); Style.Fill(new Rect(bx + bw - th, by, th, bh), white);
            Style.Fill(new Rect(bx + bw, by + bh * 0.3f, th * 2, bh * 0.4f), white);
            for (int i = 0; i < 3; i++) Style.Fill(new Rect(bx + th * 2 + i * (bw - th * 4) / 3f, by + th * 2, (bw - th * 4) / 3f - th, bh - th * 4), white);
            // compteur
            float t = recording ? RecSeconds(rec, now) : 0f;
            string counter = string.Format("{0}:{1:00}:{2:00}", (int)(t / 3600), (int)(t / 60) % 60, (int)t % 60);
            Style.Text(new Rect(r.x + m, r.yMax - m - Style.Px(50), r.width * 0.5f, Style.Px(50)), counter, big, TextAnchor.LowerLeft, white);
            // zoom
            if (now < vfZoomShown)
            {
                float zx = r.center.x - Style.Px(160), zy = r.yMax - m - Style.Px(80), zw = Style.Px(320);
                Style.Text(new Rect(zx - Style.Px(40), zy - Style.Px(12), Style.Px(36), Style.Px(36)), "W", small, TextAnchor.MiddleRight, white);
                Style.Text(new Rect(zx + zw + Style.Px(6), zy - Style.Px(12), Style.Px(36), Style.Px(36)), "T", small, TextAnchor.MiddleLeft, white);
                Style.Fill(new Rect(zx, zy, zw, th), white);
                float f = Mathf.InverseLerp(Mathf.Log(55f), Mathf.Log(7f), Mathf.Log(vfFov));
                Style.Fill(new Rect(zx + f * zw - th * 2, zy - Style.Px(10), th * 4, Style.Px(22)), white);
            }
            // touches
            string keys = Lang.T("CLIC GAUCHE : FILMER / PAUSE    ", "LEFT CLICK: FILM / PAUSE    ") + Keys.Label(Keys.CameraViewfinder) + Lang.T(" : BAISSER (FIN DE LA PRISE)    MOLETTE : ZOOM", ": LOWER (END OF TAKE)    WHEEL: ZOOM");
            Style.Text(new Rect(r.x, r.yMax - Style.Px(34), r.width, Style.Px(30)), keys, 20, TextAnchor.MiddleCenter, new Color(1f, 1f, 1f, 0.7f));
        }

        // Core.OnGUI : rappel des touches tant qu'on tient le camescope (hors viseur)
        public static void DrawKameraHint()
        {
            if (Viewfinder || !Active || root == null || Event.current.type != EventType.Repaint) return;
            Esine k = HeldKamera();
            if (k == null)
            {
                Pussi hb = HeldBag();
                if (hb != null && !KameraBusy)
                {
                    string bs = hb.Count > 0 ? Lang.T("SAC DE COURSES : " + hb.Count + " ARTICLE(S)  -  F : EN SORTIR UN    F TENU : TOUT SORTIR", "SHOPPING BAG: " + hb.Count + " ITEM(S)  -  F: TAKE ONE OUT    HOLD F: TAKE ALL OUT")
                                             : Lang.T("SAC DE COURSES VIDE", "EMPTY SHOPPING BAG");
                    Style.Text(new Rect(0, Screen.height - Style.Px(120), Screen.width, Style.Px(30)), bs, 20, TextAnchor.MiddleCenter, new Color(1f, 1f, 1f, 0.85f));
                    return;
                }
                // cassette en main : son titre, et comment l'ecrire
                Esine tp = esineet.Find(e => e.Kind == KVhs && e.Go != null && e.Go.transform.parent != null && Game.UnderPlayer(e.Go.transform));
                if (tp == null || KameraBusy) return;
                string ti = TapeTitle(tp.Data), fi = TapeFilm(tp.Data);
                string hs = Lang.T("CASSETTE VHS", "VHS TAPE") + (ti.Length > 0 ? " « " + ti + " »" : "") + (fi.Length > 0 ? Lang.T(" (FILM)", " (FILM)") : Lang.T(" (VIERGE)", " (BLANK)"))
                          + "  -  " + Keys.Label(Keys.CameraRec) + Lang.T(" : MONTAGE (titre, rogner, effacer, exporter)", ": EDITING (title, trim, erase, export)");
                Style.Text(new Rect(0, Screen.height - Style.Px(120), Screen.width, Style.Px(30)), hs, 20, TextAnchor.MiddleCenter, new Color(1f, 1f, 1f, 0.85f));
                return;
            }
            if (KameraBusy) return;
            string s = Lang.T("CAMÉSCOPE", "CAMCORDER") + (Tape(k).Length == 0 ? Lang.T(" (PAS DE CASSETTE)", " (NO TAPE)") : "") + "  -  "
                     + Keys.Label(Keys.CameraViewfinder) + Lang.T(" : À L'ŒIL (VISEUR)    ", ": TO THE EYE (VIEWFINDER)    ") + Keys.Label(Keys.CameraRec) + (rec != null && rec.Cam == k ? Lang.T(" : ARRÊTER", ": STOP") : Lang.T(" : ENREGISTRER", ": RECORD"));
            float W = Screen.width, H = Screen.height;
            Style.Text(new Rect(0, H - Style.Px(120), W, Style.Px(30)), s, 20, TextAnchor.MiddleCenter, new Color(1f, 1f, 1f, 0.85f));
            if (rec != null && rec.Cam == k && Mathf.Repeat(Time.realtimeSinceStartup, 1f) < 0.6f)
                Style.Text(new Rect(0, H - Style.Px(150), W, Style.Px(30)), "● REC", 22, TextAnchor.MiddleCenter, new Color(1f, 0.15f, 0.1f));
        }

        // ---------------------------------------------------------------- enregistrement
        class Rec { public Esine Cam; public string Film; public BinaryWriter W; public float Start, NextFrame, PauseAt, PausedTotal; public bool Paused; public int Frames; public long Bytes, Sound; }
        static Rec rec;
        static Camera recCam;
        static RenderTexture recRT;
        static Texture2D recTex;
        static Color32[] recSrc;

        static void StartRec(Esine k)
        {
            if (rec != null) return;
            string tape = Tape(k);
            if (tape.Length == 0) { Hud.Toast(Lang.T("Caméscope : pas de cassette", "Camcorder: no tape")); return; }
            if (Clips(tape).Count >= MaxClips) { Hud.Toast(Lang.T("Caméscope : bande pleine", "Camcorder: tape full")); return; }
            string film = "f" + Session.LocalId + "-" + DateTime.Now.Ticks.ToString("x");
            BinaryWriter w;
            try
            {
                Directory.CreateDirectory(FilmDir);
                w = new BinaryWriter(new FileStream(FilmPath(film), FileMode.Create, FileAccess.Write, FileShare.Read));
                w.Write(new[] { (byte)'M', (byte)'W', (byte)'V', (byte)'1' });
                w.Write((ushort)RecW); w.Write((ushort)RecH); w.Write((byte)RecFps); w.Write((ushort)RecRate);
            }
            catch (Exception e) { L("video : film pas cree : " + e.Message); return; }
            float now = Time.realtimeSinceStartup;
            rec = new Rec { Cam = k, Film = film, W = w, Start = now, NextFrame = now };
            StartTap();
            SetData(k, tape + "|1");
            L("video : enregistrement " + film + " (camescope " + k.Id + ", cassette " + tape + ")");
        }

        static void StopRec(string why)
        {
            if (rec == null) return;
            Rec r = rec;
            rec = null;
            try { PollTap(); DrainSound(r); } catch { }
            StopTap();
            try { r.W.Close(); } catch { }
            float dur = RecSeconds(r, Time.realtimeSinceStartup);
            L("video : film " + r.Film + " : " + dur.ToString("F1") + " s, " + r.Frames + " images, son " + r.Sound / 1024 + " Ko, " + r.Bytes / 1024 + " Ko (" + why + ")");
            if (r.Cam.Go != null && esineet.Contains(r.Cam))
            {
                // la prise s'ajoute a la suite des precedentes
                string content = Tape(r.Cam), title = TapeTitle(content), films = TapeFilm(content);
                SetData(r.Cam, (r.Frames > 0 ? (films.Length > 0 ? films + "+" : "") + r.Film + "~" + title : content) + "|0");
            }
            if (r.Frames > 0) SendFilm(r.Film);
        }

        static void WriteBlock(Rec r, int kind, float now, byte[] data)
        {
            r.W.Write((byte)kind);
            r.W.Write((uint)Mathf.Max(0f, RecSeconds(r, now) * 1000f));
            r.W.Write(data.Length);
            r.W.Write(data);
            r.Bytes += data.Length + 9;
        }

        static void RecFrame(float now)
        {
            Rec r = rec;
            Esine k = r.Cam;
            Transform eye = EyeT;
            if (recCam == null)
            {
                var g = new GameObject("MWCoop kamera");
                UnityEngine.Object.DontDestroyOnLoad(g);
                recCam = g.AddComponent<Camera>();
                Camera main = eye != null ? eye.GetComponent<Camera>() : null;
                if (main != null) recCam.CopyFrom(main);
                recCam.enabled = false;
                recRT = new RenderTexture(RecW, RecH, 24) { name = "kauppa_kamera" };
                recTex = new Texture2D(RecW, RecH, TextureFormat.RGB24, false);
                recSrc = new Color32[RecW * RecH];
            }
            bool inHand = k.Go.transform.parent != null && Game.UnderPlayer(k.Go.transform);
            Renderer[] own = k.Go.GetComponentsInChildren<Renderer>();
            foreach (Renderer x in own) x.enabled = false;
            if (inHand && eye != null)
            {
                recCam.transform.position = eye.position; recCam.transform.rotation = eye.rotation;
                recCam.fieldOfView = Viewfinder ? vfFov : 45f;
            }
            else
            {
                Transform lens = k.Go.transform.Find("linssi");
                Transform t = k.Go.transform;
                recCam.transform.position = (lens != null ? lens.position : t.position) + t.forward * 0.03f;
                recCam.transform.rotation = t.rotation;
                recCam.fieldOfView = 45f;
            }
            recCam.nearClipPlane = 0.05f;
            recCam.targetTexture = recRT;
            recCam.aspect = (float)RecW / RecH;
            try { if (inHand) recCam.Render(); else ThirdPerson.RenderWithBody(recCam); }   // (pose : le joueur d'ici dans le film)
            finally { if (own != null) foreach (Renderer x in own) if (x != null) x.enabled = x.name != "rec" || k.Data.EndsWith("|1"); }
            RenderTexture prev = RenderTexture.active;
            RenderTexture.active = recRT;
            recTex.ReadPixels(new Rect(0, 0, RecW, RecH), 0, 0, false);
            RenderTexture.active = prev;
            Color32[] px = recTex.GetPixels32();
            VhsLook(px);
            recTex.SetPixels32(px);
            recTex.Apply(false);
            byte[] jpg = recTex.EncodeToJPG(60);
            WriteBlock(r, 1, now, jpg);
            r.Frames++;
            DrainSound(r);
        }

        // Effet VHS sur l'image (rangee 0 en bas) : chrominance floutee et decalee a droite, saturation baissee, noirs
        // releves, neige sur la luminance, une ligne sur deux plus sombre, bas de l'image qui tremble.
        static void VhsLook(Color32[] p)
        {
            Array.Copy(p, recSrc, p.Length);
            System.Random rnd = noiseRnd;
            for (int y = 0; y < RecH; y++)
            {
                int o = y * RecW;
                int shift = y < 6 ? rnd.Next(-5, 6) : 0;
                float dark = (y & 1) == 0 ? 0.93f : 1f;
                float cu = 0f, cv = 0f;
                for (int x = 0; x < RecW; x++)
                {
                    Color32 c = recSrc[o + Mathf.Clamp(x - shift, 0, RecW - 1)];
                    Color32 cc = recSrc[o + Mathf.Clamp(x - shift - 2, 0, RecW - 1)];
                    float yy = 0.299f * c.r + 0.587f * c.g + 0.114f * c.b;
                    float cy = 0.299f * cc.r + 0.587f * cc.g + 0.114f * cc.b;
                    cu = cu * 0.62f + (cc.b - cy) * 0.38f;
                    cv = cv * 0.62f + (cc.r - cy) * 0.38f;
                    yy = (14f + yy * 0.9f + rnd.Next(-7, 8)) * dark;
                    float u = cu * 0.78f, v = cv * 0.78f;
                    float rr = yy + v, bb = yy + u, gg = (yy - 0.299f * rr - 0.114f * bb) / 0.587f;
                    p[o + x] = new Color32((byte)Mathf.Clamp(rr + 3f, 0f, 255f), (byte)Mathf.Clamp(gg, 0f, 255f), (byte)Mathf.Clamp(bb - 4f, 0f, 255f), 255);
                }
            }
        }

        // ---------------------------------------------------------------- son
        // Ce qu'entend le joueur : derniers echantillons de la sortie (AudioListener.GetOutputData), recolles d'une image
        // a l'autre par l'horloge du son (dspTime) ; mono, ramene a 11 025 Hz. (OnAudioFilterRead n'est jamais appele
        // pour un composant du mod dans ce Unity : essai du 12/10, 0 appel.)
        const int TapLen = 8192;
        static readonly float[] tapL = new float[TapLen], tapR = new float[TapLen];
        static readonly List<float> tapOut = new List<float>();
        static double tapDsp, tapStep, tapAcc;
        static float tapSum; static int tapN, tapRate;
        static bool tapStereo;

        static void StartTap()
        {
            tapRate = AudioSettings.outputSampleRate;
            tapDsp = AudioSettings.dspTime; tapStep = Math.Max(1.0, tapRate / (double)RecRate);
            tapAcc = 0; tapSum = 0f; tapN = 0; tapOut.Clear();
            tapStereo = AudioSettings.speakerMode != AudioSpeakerMode.Mono;
            L("video : son de la sortie (" + tapRate + " Hz, " + AudioSettings.speakerMode + ")");
        }

        static void StopTap() { tapOut.Clear(); ownVoice.Clear(); }

        // Voix du joueur qui filme (Voice : decompressee ici, 24 kHz 16 bits) : ramenee a la frequence du film et melee
        // au son de la sortie dans PollTap. Camescope pose : plus faible avec la distance (portee du vocal).
        static readonly Queue<float> ownVoice = new Queue<float>();
        static double voiceAcc; static float voiceSum; static int voiceN;
        public static bool WantsOwnVoice { get { return rec != null && !rec.Paused; } }
        // camescope pose qui filme chez nous : l'avatar du joueur d'ici doit exister (ThirdPerson, invisible pour lui)
        public static bool SelfBodyWanted { get { return rec != null && rec.Cam.Go != null && !(rec.Cam.Go.transform.parent != null && Game.UnderPlayer(rec.Cam.Go.transform)); } }
        public static void OwnVoice(byte[] pcm, int bytes, int rate, float gain)
        {
            if (!WantsOwnVoice) return;
            Esine k = rec.Cam;
            bool inHand = k.Go != null && k.Go.transform.parent != null && Game.UnderPlayer(k.Go.transform);
            if (!inHand && k.Go != null && EyeT != null)
            {
                float d = Vector3.Distance(EyeT.position, k.Go.transform.position), range = Config.GetFloat("Coop", "VocalPortee", 35f);
                gain *= Mathf.Clamp01(1f - d / Mathf.Max(1f, range));
                if (gain <= 0.01f) return;
            }
            double step = Math.Max(1.0, rate / (double)RecRate);
            for (int i = 0; i + 1 < bytes; i += 2)
            {
                short s = (short)(pcm[i] | pcm[i + 1] << 8);
                voiceSum += s / 32768f * gain; voiceN++; voiceAcc += 1.0;
                if (voiceAcc >= step) { voiceAcc -= step; ownVoice.Enqueue(voiceSum / voiceN); voiceSum = 0f; voiceN = 0; }
            }
            while (ownVoice.Count > RecRate * 2) ownVoice.Dequeue();   // (trop en retard : on jette le plus vieux)
        }

        static void PollTap()
        {
            double d = AudioSettings.dspTime;
            int fresh = (int)Math.Round((d - tapDsp) * tapRate);
            if (fresh <= 0) return;
            tapDsp = d;
            fresh = Math.Min(fresh, TapLen);
            AudioListener.GetOutputData(tapL, 0);
            if (tapStereo) { try { AudioListener.GetOutputData(tapR, 1); } catch { tapStereo = false; } }
            for (int i = TapLen - fresh; i < TapLen; i++)
            {
                tapSum += tapStereo ? (tapL[i] + tapR[i]) * 0.5f : tapL[i]; tapN++;
                tapAcc += 1.0;
                if (tapAcc >= tapStep) { tapAcc -= tapStep; float v = ownVoice.Count > 0 ? ownVoice.Dequeue() : 0f; tapOut.Add(Mathf.Clamp(tapSum / tapN + v, -1f, 1f)); tapSum = 0f; tapN = 0; }
            }
        }

        static void DrainSound(Rec r)
        {
            if (tapOut.Count == 0) return;
            var b = new byte[tapOut.Count];
            for (int i = 0; i < b.Length; i++) b[i] = MuLaw(tapOut[i]);
            tapOut.Clear();
            WriteBlock(r, 2, Time.realtimeSinceStartup, b);
            r.Sound += b.Length;
        }

        static byte MuLaw(float f)
        {
            int s = (int)(Mathf.Clamp(f, -1f, 1f) * 32635f);
            int sign = 0;
            if (s < 0) { sign = 0x80; s = -s; }
            s += 0x84;
            if (s > 32767) s = 32767;
            int exp = 7;
            for (int mask = 0x4000; (s & mask) == 0 && exp > 0; exp--, mask >>= 1) { }
            int mant = (s >> (exp + 3)) & 0x0F;
            return (byte)~(sign | (exp << 4) | mant);
        }

        public static float MuLawDecode(byte b)
        {
            int u = ~b & 0xFF;
            int sign = u & 0x80, exp = (u >> 4) & 7, mant = u & 0x0F;
            int s = ((mant << 3) + 0x84) << exp;
            s -= 0x84;
            return (sign != 0 ? -s : s) / 32768f;
        }

        // ---------------------------------------------------------------- essais
        // camescope sur la table du coin cafe, cassette inseree, enregistrement (vers le comptoir)
        static void TestKamera(bool start)
        {
            Esine k = esineet.Find(e => e.Kind == KKamera), tape = esineet.Find(e => e.Kind == KVhs);
            if (!start) { StopRec("essai"); return; }
            L("essai : camescope " + (k != null) + ", cassette " + (tape != null));
            if (k == null) return;
            k.Go.transform.position = root.transform.TransformPoint(new Vector3(3.4f, 0.85f, 2.0f));
            k.Go.transform.rotation = root.transform.rotation * Quaternion.LookRotation(new Vector3(-0.2f, 0f, -4.6f));
            k.Rb.velocity = Vector3.zero; k.Rb.angularVelocity = Vector3.zero;
            string content = tape != null ? tape.Data : Tape(k).Length > 0 ? Tape(k) : "tyhja";
            if (tape != null) RemoveItem(tape, true);
            SetData(k, content + "|0");
            StartRec(k);
        }

        // premiere image du dernier film -> dumps\kauppa-filmi.png ; resume du fichier dans le journal
        static void TestFilm()
        {
            var files = new List<FileInfo>(new DirectoryInfo(FilmDir).GetFiles("*.mwv"));
            files.Sort((a, b) => b.LastWriteTime.CompareTo(a.LastWriteTime));
            if (files.Count == 0) { L("essai : aucun film"); return; }
            byte[] all = File.ReadAllBytes(files[0].FullName);
            int p = 4 + 2 + 2 + 1 + 2, frames = 0, sound = 0; byte[] pick = null; uint last = 0; double sq = 0; float peak = 0f;
            while (p + 9 <= all.Length)
            {
                int kind = all[p]; uint ms = BitConverter.ToUInt32(all, p + 1); int n = BitConverter.ToInt32(all, p + 5);
                p += 9;
                if (n < 0 || p + n > all.Length) break;
                if (kind == 1) { frames++; if (frames == 25 || pick == null) { pick = new byte[n]; Buffer.BlockCopy(all, p, pick, 0, n); } }
                else if (kind == 2) { sound += n; for (int i = 0; i < n; i++) { float s = MuLawDecode(all[p + i]); sq += s * s; peak = Mathf.Max(peak, Mathf.Abs(s)); } }
                last = ms; p += n;
            }
            L("essai : film " + files[0].Name + " : " + frames + " images, son " + sound + " octets (" + (sound / (float)RecRate).ToString("F1") + " s, efficace " + (sound > 0 ? Math.Sqrt(sq / sound) : 0).ToString("F4") + ", crete " + peak.ToString("F3") + "), " + last + " ms, " + all.Length / 1024 + " Ko");
            if (pick == null) return;
            var t = new Texture2D(2, 2);
            if (t.LoadImage(pick))
            {
                string dir = Path.Combine(Log.DataDir, "dumps"); Directory.CreateDirectory(dir);
                File.WriteAllBytes(Path.Combine(dir, "kauppa-filmi.png"), t.EncodeToPNG());
            }
            UnityEngine.Object.Destroy(t);
        }

        static void OnLevelLoadedKamera()
        {
            if (rec != null) StopRec("changement de niveau");
            if (Viewfinder) ExitViewfinder();
            Unequip(); eqFsm = null; eqReenable = -1;
            lastHeldKamera = null; inMenu = null;
        }
    }
}
