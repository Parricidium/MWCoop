using System.Collections;
using System.Collections.Generic;
using MWCoop.Net;
using UnityEngine;

namespace MWCoop
{
    // Camera drone (mode photo, demande de JD, 11/10 : « comme Assetto Corsa ») : touche Keys.Drone (F8, lanceur > Touches >
    // Camera drone). Le corps du joueur reste ou il est (les autres le voient, et lui aussi dans le cadre : son avatar, comme
    // a la 3e personne) ; il pilote une camera volante :
    //  - deplacements (plan horizontal), monter / descendre, rapide (touche tenue), souris pour viser, zoom (molette ou
    //    touches), pencher (roulis), recentrer ; portee limitee autour du joueur ([Coop] DronePortee, 80 m), murs et sol
    //    arretent le drone ;
    //  - comme ThirdPerson, la camera du jeu n'est deplacee QUE pendant le rendu : son, rayons du jeu, tout reste aux yeux.
    //    Les yeux regardent le ciel tant que dure le mode (le clic de la photo ne prend ni n'ouvre rien devant le joueur) ;
    //    le regard envoye aux autres reste celui d'avant (BodyYaw / BodyPitch) ;
    //  - viseur : regle des tiers, croix et coins au centre, legende des touches a droite, logo du mod en haut a gauche (un
    //    peu transparent, aussi sur la photo) ; interface du jeu et du mod cachees. Photo : image de l'ecran avec le logo
    //    seul, dans Images\CoopPictures ([Coop] DossierPhotos), flash (lumiere au drone) et declic ;
    //  - les autres voient un petit drone (modele « Drone » de Kira, Sketchfab, CC BY 4.0 : assets\drone, run\glb2mwm.py)
    //    a la place de la camera : helices qui tournent, bourdonnement faible, flash et declic a chaque photo.
    //    Reseau : @drone (non fiable, 12/s : allume, position, orientation ; 0 a la sortie, fiable), @flash (fiable).
    public static class DroneCam
    {
        public static bool Active;
        public static float BodyYaw, BodyPitch;   // regard du corps a l'entree (envoye aux autres pendant le mode)
        static Camera cam, handCam, guiCam;
        static bool handWas, guiWas, layerWas;
        static Behaviour guiLayer;   // GUILayer de la camera du jeu : dessine le point de visee (GUI/Crosshair)
        static Vector3 eyeWorld;     // yeux du joueur (la camera, hors du rendu)
        static Quaternion eyeSaved;
        static Vector3 pos, vel;
        static float yaw, pitch, roll, fov = 60f, baseFov = 60f, camFov;
        static bool showUi = true, grid = true, hooked, capturing;
        static Vector3 eyeLocal; static Quaternion eyeLocalRot; static bool moved; static int lastFrame = -1;
        static float nextSend, flashUntil = -1, savedAt = -100; static string savedName;
        static Light flashLight;

        public static void OnLevelLoaded()
        {
            if (Active) Exit(false);
            cam = null; handCam = null; guiCam = null;
            foreach (Remote r in remotes.Values) r.Destroy();
            remotes.Clear();
        }

        // ---------------------------------------------------------------- entree / sortie
        static HutongGames.PlayMaker.FsmBool inMenu;
        static bool Busy
        {
            get
            {
                if (inMenu == null) inMenu = HutongGames.PlayMaker.FsmVariables.GlobalVariables.FindFsmBool("PlayerInMenu");   // (menu pause du jeu)
                return Menu.Open || Menu.ChatOpen || MapPanel.Open || CheatPanel.Open || WalletPanel.Open || Respawn.Choosing || inMenu != null && inMenu.Value;
            }
        }

        public static void Update()
        {
            UpdateRemotes();
            if (!PlayerSync.InGame || !Session.Active) { if (Active) Exit(false); return; }
            // Ajouts coop coupes (lanceur, Partie ; Menor, 12/10) : pas de camera drone ici (celles des autres restent visibles).
            // (invite : coupee aussi quand l'hote l'a coupee chez lui -- on le dit a l'appui sur la touche)
            if (!Ajouts.Drone)
            {
                if (Active) Exit(true);
                if (!Busy && Input.GetKeyDown(Keys.Drone)) Hud.Toast(Ajouts.LocalDrone ? Lang.T("Caméra drone coupée par l'hôte", "Drone camera turned off by the host") : Lang.T("Caméra drone coupée (lanceur : Options du jeu > Ajouts COOP)", "Drone camera turned off (launcher: Game options > CO-OP additions)"));
                return;
            }
            bool test = Config.GetInt("Test", "Drone", 0) != 0;
            if (!Busy && Input.GetKeyDown(Keys.Drone) || test && !Active && !testDone)
            {
                if (test) testDone = true;
                if (Active) Exit(true); else Enter();
            }
            if (!Active) return;
            if (cam == null) { Exit(false); return; }
            if (!Busy) Fly();
            if (flashLight != null && Time.realtimeSinceStartup > flashUntil) { Object.Destroy(flashLight.gameObject); flashLight = null; }
            float now = Time.realtimeSinceStartup;
            if (now >= nextSend && Session.RemoteCount > 0)
            {
                nextSend = now + 1f / 12f;
                Session.SendAll(new NetWriter(Msg.Job).U8(Session.LocalId).Str("@drone").U8(1).Vec(pos).Quat(Quaternion.Euler(pitch, yaw, roll)), false);
            }
        }
        static bool testDone;

        static void Enter()
        {
            if (VehicleSync.LocalDrivingRoot != null) { Hud.Toast(Lang.T("Drone : quitte d'abord la place du conducteur", "Drone: leave the driver's seat first")); return; }
            Transform c = PlayerSync.LocalCamera;
            cam = c != null ? c.GetComponent<Camera>() : null;
            if (cam == null) return;
            Transform hc = c.Find("HandCamera");
            handCam = hc != null ? hc.GetComponent<Camera>() : null;
            if (handCam != null) { handWas = handCam.enabled; handCam.enabled = false; }
            GameObject g = Game.FindAny("GUI/CAM");
            guiCam = g != null ? g.GetComponent<Camera>() : null;
            if (guiCam != null) { guiWas = guiCam.enabled; guiCam.enabled = false; }
            guiLayer = cam.GetComponent("GUILayer") as Behaviour;
            if (guiLayer != null) { layerWas = guiLayer.enabled; guiLayer.enabled = false; }
            Vector3 fw = c.forward;
            Vector3 flat = new Vector3(fw.x, 0f, fw.z);
            yaw = flat.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(flat).eulerAngles.y : 0f;
            pitch = Mathf.Clamp(-Mathf.Asin(Mathf.Clamp(fw.y, -1f, 1f)) * Mathf.Rad2Deg, -85f, 85f);
            BodyYaw = yaw; BodyPitch = pitch;
            roll = 0f;
            camFov = cam.fieldOfView; baseFov = camFov; fov = baseFov;
            // Depart : un peu derriere et au-dessus de la tete, sans traverser un mur.
            pos = c.position;
            Move(Vector3.up * 0.35f - flat.normalized * 0.8f);
            vel = Vector3.zero;
            // Yeux vers le ciel le temps du mode (rien de pris ni d'ouvert au clic de la photo).
            eyeSaved = c.rotation;
            c.rotation = Quaternion.LookRotation(Vector3.up, flat.sqrMagnitude > 1e-4f ? -flat.normalized : Vector3.forward);
            if (!hooked) { hooked = true; Camera.onPreCull += PreCull; }
            showUi = true;
            Active = true;
            Log.Info("drone : mode photo (portee " + Range + " m, photos dans " + Folder + ")");
        }

        static void Exit(bool send)
        {
            Active = false;
            if (moved) PutBack();
            if (cam != null) { cam.transform.rotation = eyeSaved; cam.fieldOfView = camFov; }
            if (handCam != null) handCam.enabled = handWas;
            if (guiCam != null) guiCam.enabled = guiWas;
            if (guiLayer != null) guiLayer.enabled = layerWas;
            if (flashLight != null) { Object.Destroy(flashLight.gameObject); flashLight = null; }
            if (send && Session.RemoteCount > 0) Session.SendAll(new NetWriter(Msg.Job).U8(Session.LocalId).Str("@drone").U8(0).Vec(pos).Quat(Quaternion.identity), true);
            Log.Info("drone : fin du mode photo");
        }

        static float Range { get { return Mathf.Clamp(Config.GetFloat("Coop", "DronePortee", 80f), 10f, 500f); } }

        // ---------------------------------------------------------------- pilotage
        static void Fly()
        {
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
            float sens = Config.GetFloat("Coop", "DroneSouris", 2f) * fov / 60f;   // (plus fin quand on zoome)
            yaw += Input.GetAxis("Mouse X") * sens;
            pitch = Mathf.Clamp(pitch - Input.GetAxis("Mouse Y") * sens, -89f, 89f);
            Quaternion yq = Quaternion.Euler(0f, yaw, 0f);
            Vector3 want = Vector3.zero;
            if (Input.GetKey(Keys.DroneForward)) want += yq * Vector3.forward;
            if (Input.GetKey(Keys.DroneBack)) want -= yq * Vector3.forward;
            if (Input.GetKey(Keys.DroneRight)) want += yq * Vector3.right;
            if (Input.GetKey(Keys.DroneLeft)) want -= yq * Vector3.right;
            if (Input.GetKey(Keys.DroneUp)) want += Vector3.up;
            if (Input.GetKey(Keys.DroneDown)) want -= Vector3.up;
            float speed = Config.GetFloat("Coop", "DroneVitesse", 3.5f) * (Input.GetKey(Keys.DroneFast) ? 3.5f : 1f);
            vel = Vector3.Lerp(vel, want.normalized * speed, 1f - Mathf.Exp(-6f * dt));
            Move(vel * dt);
            // Portee : ramene vers le joueur.
            Vector3 eye = cam.transform.position, d = pos - eye;
            if (d.magnitude > Range) pos = eye + d.normalized * Range;
            // Zoom (champ de vision 8 a 100 degres), pencher, recentrer.
            float wheel = Input.GetAxis("Mouse ScrollWheel");
            float z = (Input.GetKey(Keys.DroneZoomIn) ? 1f : 0f) - (Input.GetKey(Keys.DroneZoomOut) ? 1f : 0f);
            fov = Mathf.Clamp(fov * Mathf.Exp(-(wheel * 2.5f + z * 1.2f * dt)), 8f, 100f);
            float r = (Input.GetKey(Keys.DroneRollLeft) ? 1f : 0f) - (Input.GetKey(Keys.DroneRollRight) ? 1f : 0f);
            roll = Mathf.Clamp(roll + r * 40f * dt, -90f, 90f);
            if (Input.GetKeyDown(Keys.DroneReset)) { roll = 0f; fov = baseFov; }
            if (Input.GetKeyDown(Keys.DroneGrid)) grid = !grid;
            if (Input.GetKeyDown(Keys.DroneHud)) showUi = !showUi;
            if (Input.GetKeyDown(Keys.DronePhoto) && !capturing && Core.I != null) Core.I.StartCoroutine(Shoot());
        }

        // Deplacement par glissement : un obstacle (pas le joueur, pas un declencheur) arrete le drone a 15 cm.
        static void Move(Vector3 delta)
        {
            for (int axis = 0; axis < 3; axis++)
            {
                Vector3 step = Vector3.zero; step[axis] = delta[axis];
                float len = step.magnitude;
                if (len < 1e-5f) continue;
                Vector3 dir = step / len;
                float free = len;
                foreach (RaycastHit h in Physics.SphereCastAll(pos, 0.15f, dir, len + 0.02f))
                {
                    if (h.collider.isTrigger || Game.UnderPlayer(h.collider.transform)) continue;
                    if (h.distance < free) free = Mathf.Max(0f, h.distance - 0.02f);
                }
                pos += dir * free;
            }
        }

        // ---------------------------------------------------------------- rendu
        static void PreCull(Camera c)
        {
            if (testLook != null && c != null && c.transform == PlayerSync.LocalCamera) { c.transform.LookAt(testLook.position); c.fieldOfView = 9f; return; }   // (essai : l'hote regarde le drone, au teleobjectif)
            if (!Active || c == null || c != cam || Time.frameCount == lastFrame) return;
            if (moved) PutBack();
            Transform t = c.transform;
            eyeLocal = t.localPosition; eyeLocalRot = t.localRotation; eyeWorld = t.position;
            t.position = pos;
            t.rotation = Quaternion.Euler(pitch, yaw, roll);
            c.fieldOfView = fov;
            moved = true;
            lastFrame = Time.frameCount;
            if (Core.I != null) Core.I.StartCoroutine(Restore());
        }

        static void PutBack()
        {
            if (moved && cam != null) { cam.transform.localPosition = eyeLocal; cam.transform.localRotation = eyeLocalRot; cam.fieldOfView = camFov; }
            moved = false;
        }

        static IEnumerator Restore()
        {
            yield return new WaitForEndOfFrame();
            PutBack();
        }

        // ---------------------------------------------------------------- photo
        static string Folder
        {
            get
            {
                string f = Config.Get("Coop", "DossierPhotos", "");
                if (f.Length == 0)
                {
                    string pics = "";
                    try { pics = System.Environment.GetFolderPath(System.Environment.SpecialFolder.MyPictures); } catch { }
                    f = System.IO.Path.Combine(pics.Length > 0 ? pics : Log.DataDir, "CoopPictures");
                }
                return f;
            }
        }

        static IEnumerator Shoot()
        {
            capturing = true;
            Flash(pos, true);   // (lumiere allumee avant le rendu : elle eclaire la photo)
            yield return new WaitForEndOfFrame();
            string name = null;
            try
            {
                var tex = new Texture2D(Screen.width, Screen.height, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, Screen.width, Screen.height), 0, 0);
                tex.Apply();
                byte[] png = tex.EncodeToPNG();
                Object.Destroy(tex);
                string dir = Folder;
                System.IO.Directory.CreateDirectory(dir);
                name = "MWCoop_" + System.DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss") + ".png";
                string path = System.IO.Path.Combine(dir, name);
                for (int i = 2; System.IO.File.Exists(path); i++) { name = "MWCoop_" + System.DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss") + "_" + i + ".png"; path = System.IO.Path.Combine(dir, name); }
                System.IO.File.WriteAllBytes(path, png);
                Log.Info("drone : photo " + path + " (" + Screen.width + "x" + Screen.height + ", " + png.Length / 1024 + " Ko)");
            }
            catch (System.Exception e) { Log.Warn("drone : photo pas enregistree : " + e.Message); name = null; }
            capturing = false;
            savedName = name; savedAt = Time.realtimeSinceStartup;
            Shutter(null);
            if (Session.RemoteCount > 0) Session.SendAll(new NetWriter(Msg.Job).U8(Session.LocalId).Str("@flash").Vec(pos), true);
        }

        // Flash : lumiere ponctuelle 0,12 s au drone (et blanc a l'ecran chez celui qui prend la photo, apres l'image).
        static void Flash(Vector3 at, bool local)
        {
            var go = new GameObject("MWCoop flash");
            go.transform.position = at;
            Light l = go.AddComponent<Light>();
            l.type = LightType.Point; l.range = 14f; l.intensity = 3.5f; l.color = new Color(1f, 0.97f, 0.92f);
            l.shadows = LightShadows.None;
            if (local) { if (flashLight != null) Object.Destroy(flashLight.gameObject); flashLight = l; flashUntil = Time.realtimeSinceStartup + 0.12f; }
            else Object.Destroy(go, 0.12f);
        }

        // ---------------------------------------------------------------- viseur (Core.OnGUI)
        public static void Draw()
        {
            if (!Active) return;
            float W = Screen.width, H = Screen.height;
            // Logo en haut a gauche (aussi sur la photo).
            Style.Alpha = 0.6f;
            float lh = Mathf.Round(H * 0.085f);
            Style.Logo(new Rect(Mathf.Round(H * 0.03f), Mathf.Round(H * 0.03f), 0, lh));
            Style.Alpha = 1f;
            if (capturing) return;
            float now = Time.realtimeSinceStartup;
            // Blanc du flash, apres l'image.
            if (now - savedAt < 0.35f) { Style.Fill(new Rect(0, 0, W, H), new Color(1f, 1f, 1f, 0.8f * (1f - (now - savedAt) / 0.35f))); }
            if (!showUi) { Hint(Lang.T("Interface cachée : ", "Interface hidden: ") + Keys.Label(Keys.DroneHud)); return; }
            Color line = new Color(1f, 1f, 1f, 0.55f), soft = new Color(1f, 1f, 1f, 0.28f);
            float th = Mathf.Max(1f, Style.Px(1.5f));
            if (grid)
            {
                // Regle des tiers.
                for (int i = 1; i <= 2; i++)
                {
                    Style.Fill(new Rect(Mathf.Round(W * i / 3f), 0, th, H), soft);
                    Style.Fill(new Rect(0, Mathf.Round(H * i / 3f), W, th), soft);
                }
            }
            // Centre : croix fine et coins de mise au point ; horizon (penche avec le roulis).
            float cx = Mathf.Round(W / 2), cy = Mathf.Round(H / 2), a = Style.Px(14);
            Style.Fill(new Rect(cx - a, cy - th / 2, a * 2, th), line);
            Style.Fill(new Rect(cx - th / 2, cy - a, th, a * 2), line);
            float bw = Mathf.Round(H * 0.16f), bh = Mathf.Round(H * 0.11f), c = Style.Px(18);
            Corners(new Rect(cx - bw, cy - bh, bw * 2, bh * 2), c, th * 1.5f, line);
            Corners(new Rect(Style.Px(36), Style.Px(36), W - Style.Px(72), H - Style.Px(72)), Style.Px(34), th * 2f, line);
            if (Mathf.Abs(roll) > 0.5f)
            {
                Matrix4x4 m = GUI.matrix;
                GUIUtility.RotateAroundPivot(-roll, new Vector2(cx, cy));
                Style.Fill(new Rect(cx - Style.Px(120), cy - th / 2, Style.Px(80), th), Style.Warn);
                Style.Fill(new Rect(cx + Style.Px(40), cy - th / 2, Style.Px(80), th), Style.Warn);
                GUI.matrix = m;
            }
            // Bas : enregistrement, focale, roulis, distance.
            float mm = 18f / Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad);   // (equivalent 24x36, hauteur 24 mm)
            float dist = Vector3.Distance(pos, moved ? eyeWorld : cam != null ? cam.transform.position : pos);
            string info = Mathf.RoundToInt(mm) + " mm   " + (roll >= 0 ? "+" : "") + Mathf.RoundToInt(roll) + "°   " + dist.ToString("F0") + " / " + Range.ToString("F0") + " m";
            float iw = Style.Width(info, 15) + Style.Px(52), ih = Style.Px(30);
            var ir = new Rect(cx - iw / 2, H - ih - Style.Px(56), iw, ih);
            Style.Round(ir, ih / 2, new Color(0.03f, 0.06f, 0.12f, 0.55f));
            Style.Round(new Rect(ir.x + Style.Px(12), ir.center.y - Style.Px(5), Style.Px(10), Style.Px(10)), Style.Px(5), new Color(0.95f, 0.25f, 0.22f, 0.55f + 0.45f * Mathf.PingPong(now * 1.6f, 1f)));
            Style.Text(new Rect(ir.x + Style.Px(26), ir.y, iw - Style.Px(30), ih), info, 15, TextAnchor.MiddleCenter, Style.White);
            Legend();
            if (now - savedAt < 4f && savedName != null)
                Hint(Lang.T("Photo enregistrée : CoopPictures\\", "Photo saved: CoopPictures\\") + savedName);
        }

        static void Corners(Rect r, float len, float th, Color c)
        {
            Style.Fill(new Rect(r.x, r.y, len, th), c); Style.Fill(new Rect(r.x, r.y, th, len), c);
            Style.Fill(new Rect(r.xMax - len, r.y, len, th), c); Style.Fill(new Rect(r.xMax - th, r.y, th, len), c);
            Style.Fill(new Rect(r.x, r.yMax - th, len, th), c); Style.Fill(new Rect(r.x, r.yMax - len, th, len), c);
            Style.Fill(new Rect(r.xMax - len, r.yMax - th, len, th), c); Style.Fill(new Rect(r.xMax - th, r.yMax - len, th, len), c);
        }

        static void Hint(string s)
        {
            float w = Style.Width(s, 15) + Style.Px(30), h = Style.Px(30);
            var r = new Rect(Mathf.Round((Screen.width - w) / 2), Style.Px(56), w, h);
            Style.Round(r, h / 2, new Color(0.03f, 0.06f, 0.12f, 0.6f));
            Style.Text(r, s, 15, TextAnchor.MiddleCenter, Style.White);
        }

        // Legende des touches, a droite.
        static void Legend()
        {
            var rows = new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>(Keys.Label(Keys.DroneForward) + " " + Keys.Label(Keys.DroneLeft) + " " + Keys.Label(Keys.DroneBack) + " " + Keys.Label(Keys.DroneRight), Lang.T("Se déplacer", "Move")),
                new KeyValuePair<string, string>(Lang.T("Souris", "Mouse"), Lang.T("Viser", "Aim")),
                new KeyValuePair<string, string>(Keys.Label(Keys.DroneUp) + " / " + Keys.Label(Keys.DroneDown), Lang.T("Monter / descendre", "Up / down")),
                new KeyValuePair<string, string>(Keys.Label(Keys.DroneFast), Lang.T("Plus vite (tenir)", "Faster (hold)")),
                new KeyValuePair<string, string>(Lang.T("Molette", "Wheel") + ", " + Keys.Label(Keys.DroneZoomIn) + " / " + Keys.Label(Keys.DroneZoomOut), Lang.T("Zoom", "Zoom")),
                new KeyValuePair<string, string>(Keys.Label(Keys.DroneRollLeft) + " / " + Keys.Label(Keys.DroneRollRight), Lang.T("Pencher", "Tilt")),
                new KeyValuePair<string, string>(Keys.Label(Keys.DroneReset), Lang.T("Recentrer", "Reset")),
                new KeyValuePair<string, string>(Keys.Label(Keys.DronePhoto), Lang.T("Prendre la photo", "Take the photo")),
                new KeyValuePair<string, string>(Keys.Label(Keys.DroneGrid), Lang.T("Grille", "Grid")),
                new KeyValuePair<string, string>(Keys.Label(Keys.DroneHud), Lang.T("Cacher l'interface", "Hide interface")),
                new KeyValuePair<string, string>(Keys.Label(Keys.Drone), Lang.T("Quitter le drone", "Leave the drone")),
            };
            float rh = Style.Px(26), pad = Style.Px(14), kw = 0f, lw = 0f;
            foreach (var r in rows) { kw = Mathf.Max(kw, Style.Width(r.Key, 14)); lw = Mathf.Max(lw, Style.Width(r.Value, 14, false)); }
            float w = kw + lw + pad * 3 + Style.Px(12), h = rows.Count * rh + pad * 2 + Style.Px(30);
            var box = new Rect(Screen.width - w - Style.Px(56), Mathf.Round((Screen.height - h) / 2), w, h);
            Style.Round(box, Style.Px(14), new Color(0.03f, 0.06f, 0.12f, 0.55f));
            Style.Text(new Rect(box.x + pad, box.y + Style.Px(8), w - pad * 2, Style.Px(26)), Lang.T("CAMÉRA DRONE", "DRONE CAMERA"), 14, TextAnchor.MiddleLeft, Style.Accent);
            float y = box.y + pad + Style.Px(26);
            foreach (var r in rows)
            {
                var kr = new Rect(box.x + pad, y + Style.Px(2), kw + Style.Px(12), rh - Style.Px(4));
                Style.Round(kr, Style.Px(6), new Color(1f, 1f, 1f, 0.12f));
                Style.Text(kr, r.Key, 14, TextAnchor.MiddleCenter, Style.White);
                Style.Text(new Rect(kr.xMax + pad, y, lw + pad, rh), r.Value, 14, TextAnchor.MiddleLeft, Style.Dim, false);
                y += rh;
            }
        }

        // ---------------------------------------------------------------- drones des autres
        class Remote
        {
            public GameObject Go; public Transform[] Props = new Transform[4]; public AudioSource Hum;
            public Vector3 Target, Last, Vel; public Quaternion TargetRot; public float SeenAt, LastAt;
            public void Destroy() { if (Go != null) Object.Destroy(Go); Go = null; }
        }
        static readonly Dictionary<int, Remote> remotes = new Dictionary<int, Remote>();

        public static void OnRemote(int who, int on, Vector3 p, Quaternion q)
        {
            if (who == Session.LocalId) return;
            Remote r;
            remotes.TryGetValue(who, out r);
            if (on == 0) { if (r != null) { r.Destroy(); remotes.Remove(who); Log.Info("drone de #" + who + " pose"); } return; }
            float now = Time.realtimeSinceStartup;
            if (r == null || r.Go == null)
            {
                r = new Remote();
                if (!Build(r, who)) return;
                r.Go.transform.position = p; r.Last = p;
                remotes[who] = r;
                Log.Info("drone de #" + who + " en vol");
            }
            if (now - r.LastAt > 0.01f) { r.Vel = Vector3.Lerp(r.Vel, (p - r.Last) / Mathf.Max(0.02f, now - r.LastAt), 0.5f); }
            r.Last = p; r.LastAt = now; r.Target = p; r.TargetRot = q; r.SeenAt = now;
        }

        public static void OnFlash(int who, Vector3 p)
        {
            if (who == Session.LocalId) return;
            Flash(p, false);
            Shutter(p);
        }

        static void UpdateRemotes()
        {
            if (remotes.Count == 0) return;
            float now = Time.realtimeSinceStartup, dt = Time.deltaTime;
            List<int> gone = null;
            foreach (KeyValuePair<int, Remote> kv in remotes)
            {
                Remote r = kv.Value;
                if (r.Go == null || now - r.SeenAt > 3f) { r.Destroy(); (gone = gone ?? new List<int>()).Add(kv.Key); continue; }
                Transform t = r.Go.transform;
                t.position = Vector3.Lerp(t.position, r.Target, 1f - Mathf.Exp(-12f * dt));
                // Corps : le cap de la camera, penche dans le sens de la marche (la camera vise, le drone vole).
                float yawR = r.TargetRot.eulerAngles.y;
                Vector3 lv = Quaternion.Inverse(Quaternion.Euler(0f, yawR, 0f)) * r.Vel;
                Quaternion want = Quaternion.Euler(Mathf.Clamp(lv.z * 4f, -22f, 22f), yawR, Mathf.Clamp(-lv.x * 4f, -22f, 22f));
                t.rotation = Quaternion.Slerp(t.rotation, want, 1f - Mathf.Exp(-8f * dt));
                float spin = 2200f * dt;
                for (int i = 0; i < 4; i++) if (r.Props[i] != null) r.Props[i].Rotate(0f, (i == 0 || i == 3) ? spin : -spin, 0f, Space.Self);
                if (r.Hum != null) r.Hum.pitch = 1f + Mathf.Min(0.25f, r.Vel.magnitude * 0.03f);
            }
            if (gone != null) foreach (int k in gone) remotes.Remove(k);
        }

        // ---------------------------------------------------------------- modele et sons
        static Mesh body; static Mesh[] props; static Vector3[] hubs; static bool meshTried;
        const float Scale = 0.11f;   // (modele de 2,9 unites d'envergure : ~32 cm, a peu pres une tete)

        static bool Build(Remote r, int who)
        {
            if (!meshTried)
            {
                meshTried = true;
                try
                {
                    body = SleepingBags.LoadMesh("MWCoop.drone-corps.mwm", Scale);
                    props = new Mesh[4]; hubs = new Vector3[4];
                    for (int i = 0; i < 4; i++)
                    {
                        Mesh m = SleepingBags.LoadMesh("MWCoop.drone-helice" + i + ".mwm", Scale);
                        Vector3 c = m.bounds.center;
                        Vector3[] v = m.vertices;
                        for (int k = 0; k < v.Length; k++) v[k] -= c;
                        m.vertices = v; m.RecalculateBounds();
                        props[i] = m; hubs[i] = c;
                    }
                }
                catch (System.Exception e) { Log.Warn("drone : modele illisible : " + e.Message); body = null; }
            }
            if (body == null) return false;
            r.Go = new GameObject("MWCoop drone #" + who);
            var bmf = r.Go.AddComponent<MeshFilter>(); bmf.sharedMesh = body;
            var bmr = r.Go.AddComponent<MeshRenderer>();
            bmr.sharedMaterials = new[] { Mat(0), Mat(1), Mat(2), Mat(3) };
            for (int i = 0; i < 4; i++)
            {
                var p = new GameObject("helice" + i);
                p.transform.parent = r.Go.transform;
                p.transform.localPosition = hubs[i];
                p.AddComponent<MeshFilter>().sharedMesh = props[i];
                p.AddComponent<MeshRenderer>().sharedMaterial = Mat(0);
                r.Props[i] = p.transform;
            }
            AudioSource s = r.Go.AddComponent<AudioSource>();
            s.clip = HumClip(); s.loop = true;
            s.volume = Mathf.Clamp01(Config.GetFloat("Coop", "DroneVolume", 0.35f));
            s.spatialBlend = 1f; s.rolloffMode = AudioRolloffMode.Linear; s.minDistance = 1f; s.maxDistance = 25f; s.dopplerLevel = 0f;
            s.Play();
            r.Hum = s;
            return true;
        }

        static Material[] mats;
        static Material Mat(int i)
        {
            if (mats == null)
            {
                Shader sh = Shader.Find("Legacy Shaders/Diffuse") ?? Shader.Find("Diffuse");
                Shader spec = Shader.Find("Legacy Shaders/Specular") ?? Shader.Find("Specular") ?? sh;
                Color[] cols = { new Color(0.06f, 0.06f, 0.065f), new Color(0.72f, 0.72f, 0.75f), new Color(0.12f, 0.2f, 0.3f), new Color(0.22f, 0.22f, 0.23f) };
                mats = new Material[4];
                for (int k = 0; k < 4; k++)
                {
                    mats[k] = new Material(k == 1 || k == 2 ? spec : sh) { name = "MWCoop drone " + k, color = cols[k], hideFlags = HideFlags.HideAndDontSave };
                    if (mats[k].HasProperty("_Shininess")) mats[k].SetFloat("_Shininess", 0.5f);
                }
                Log.Info("drone : materiaux " + (sh != null ? sh.name : "?") + " / " + (spec != null ? spec.name : "?"));
            }
            return mats[i];
        }

        // Bourdonnement : 4 helices legerement desaccordees (battements), harmoniques et souffle ; boucle de 2 s exacte
        // (frequences multiples de 0,5 Hz).
        static AudioClip hum, click;
        static AudioClip HumClip()
        {
            if (hum != null) return hum;
            const int rate = 22050, n = rate * 2;
            var d = new float[n];
            float[] f = { 188f, 191.5f, 194f, 197f };
            var rnd = new System.Random(7);
            float lp = 0f, peak = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / rate, v = 0f;
                foreach (float fr in f)
                    v += Mathf.Sin(2 * Mathf.PI * fr * t) + 0.45f * Mathf.Sin(2 * Mathf.PI * fr * 2 * t + 1f) + 0.2f * Mathf.Sin(2 * Mathf.PI * fr * 3 * t + 2f);
                lp += ((float)rnd.NextDouble() * 2f - 1f - lp) * 0.08f;
                v = v * 0.25f + lp * 1.6f;
                d[i] = v; peak = Mathf.Max(peak, Mathf.Abs(v));
            }
            for (int i = 0; i < n; i++) d[i] *= 0.6f / peak;
            hum = AudioClip.Create("MWCoop drone", n, 1, rate, false);
            hum.SetData(d, 0);
            return hum;
        }

        // Declic de l'obturateur : deux claquements (ouverture, fermeture), au drone (null : chez soi, sans position).
        static void Shutter(Vector3? at)
        {
            if (click == null)
            {
                const int rate = 22050; int n = (int)(rate * 0.16f);
                var d = new float[n];
                var rnd = new System.Random(3);
                for (int i = 0; i < n; i++)
                {
                    float t = (float)i / rate, v = 0f;
                    foreach (float s0 in new[] { 0f, 0.075f })
                    {
                        float u = t - s0;
                        if (u < 0f) continue;
                        float env = Mathf.Exp(-u * 160f);
                        v += env * (((float)rnd.NextDouble() * 2f - 1f) * 0.7f + Mathf.Sin(2 * Mathf.PI * 2300f * u) * 0.5f);
                    }
                    d[i] = Mathf.Clamp(v * 0.8f, -1f, 1f);
                }
                click = AudioClip.Create("MWCoop declic", n, 1, rate, false);
                click.SetData(d, 0);
            }
            var go = new GameObject("MWCoop declic");
            AudioSource a = go.AddComponent<AudioSource>();
            a.clip = click; a.volume = 0.8f;
            if (at.HasValue) { go.transform.position = at.Value; a.spatialBlend = 1f; a.rolloffMode = AudioRolloffMode.Linear; a.minDistance = 1f; a.maxDistance = 30f; }
            else a.spatialBlend = 0f;
            a.Play();
            Object.Destroy(go, 1f);
        }

        // ---------------------------------------------------------------- essais
        // [Test] Autotest=drone : l'invite passe en drone a 30 s, monte et recule, photo a 40 s ; l'hote note le drone vu.
        public static void Test(string mode, float t)
        {
            if (mode != "drone") return;
            if (!Session.IsHost)
            {
                if (t > 30f && testStep == 0 && !Active) { testStep = 1; Enter(); Log.Info("autotest : drone, mode photo " + (Active ? "actif" : "REFUSE")); }
                if (Active && t > 31f && t < 35f) { Move(Vector3.up * 0.3f * Time.deltaTime); yaw += 15f * Time.deltaTime; roll = 8f; fov = 45f; }
                if (Active && t > 40f && testStep == 1 && Core.I != null) { testStep = 2; Core.I.StartCoroutine(Shoot()); }
                if (t > 41f && testStep == 2) { testStep = 3; Autotest.CaptureSoon("drone-viseur", 0.05f); }
                if (t > 50f && testStep == 3) { testStep = 4; Exit(true); Log.Info("autotest : drone, sortie"); }
            }
            else
            {
                if (testStep == 0 && remotes.Count > 0 && testSeen >= 0 && t > testSeen + 4f)
                {
                    testStep = 1; testShotAt = t;
                    foreach (KeyValuePair<int, Remote> kv in remotes)
                        Log.Info("autotest : drone de #" + kv.Key + " vu en " + (kv.Value.Go != null ? kv.Value.Go.transform.position.ToString("F2") : "?") + ", son " + (kv.Value.Hum != null && kv.Value.Hum.isPlaying));
                    if (remotes.Count == 0) Log.Info("autotest : drone, AUCUN drone vu");
                    // Vue sur le drone de l'invite (capture) : camera du joueur tournee vers lui.
                    foreach (Remote r in remotes.Values) if (r.Go != null && PlayerSync.LocalCamera != null) { testLook = r.Go.transform; }
                }
                if (testLook != null && !hooked) { hooked = true; Camera.onPreCull += PreCull; }
                if (testStep == 0 && remotes.Count > 0 && testSeen < 0) testSeen = t;
                if (testStep == 1 && t > testShotAt + 2f)
                {
                    testStep = 2; Autotest.CaptureSoon("drone-vu", 0.05f);
                    foreach (Remote r in remotes.Values)
                    {
                        if (r.Go == null) continue;
                        Renderer rd = r.Go.GetComponent<Renderer>(); MeshFilter mf = r.Go.GetComponent<MeshFilter>();
                        Camera hc = PlayerSync.LocalCamera != null ? PlayerSync.LocalCamera.GetComponent<Camera>() : null;
                        Log.Info("autotest : drone, rendu : actif " + r.Go.activeInHierarchy + ", couche " + r.Go.layer + ", " + (rd != null ? "bornes " + rd.bounds.center.ToString("F2") + " taille " + rd.bounds.size.ToString("F2") + ", visible " + rd.isVisible + ", materiaux " + rd.sharedMaterials.Length + " (" + (rd.sharedMaterial != null && rd.sharedMaterial.shader != null ? rd.sharedMaterial.shader.name + " " + rd.sharedMaterial.shader.isSupported : "?") + ")" : "sans rendu")
                                 + ", maillage " + (mf != null && mf.sharedMesh != null ? mf.sharedMesh.vertexCount + " sommets, " + mf.sharedMesh.subMeshCount + " parties" : "?")
                                 + ", camera " + (hc != null ? "masque " + hc.cullingMask + ", proche " + hc.nearClipPlane + ", a " + Vector3.Distance(hc.transform.position, r.Go.transform.position).ToString("F1") + " m" : "?"));
                    }
                }
                if (testStep == 2 && t > testShotAt + 22f) { testStep = 3; Log.Info("autotest : drone, apres la sortie : " + remotes.Count + " drone(s) vu(s)"); }
            }
        }
        static int testStep; static Transform testLook; static float testSeen = -1, testShotAt;
    }
}
