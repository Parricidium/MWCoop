using System.Collections.Generic;
using HutongGames.PlayMaker;
using MWCoop.Net;
using UnityEngine;

namespace MWCoop
{
    // KARTTURI 2000 en boutique et en main (le trace, les notes et l'ecran : Kartturi.cs).
    //  - objet de la boutique ("kartturi", 690 mk) : etagere haute, au-dessus des camescopes ; comme les autres objets
    //    (sac de courses, objet physique pris en main, sauve, suivi par Props).
    //  - tenu en main et ASSIS EN PASSAGER (regle de JD, 13/10) : F = le tenir devant soi a deux mains, en bas de l'image
    //    (facon carte de Minecraft : il remonte quand on baisse les yeux), la route reste visible ; F (ou clic droit) = le
    //    rebaisser. Comme le camescope : la main du jeu est mise en pause, l'objet reste le sien (les autres le voient
    //    tenu devant l'avatar, les mains dessus : Gestures suit l'objet tenu).
    //  - ecran : chaque KARTTURI a moins de 40 m de la camera calcule le sien (meme trace chez tous), 10 fois par seconde.
    public static partial class Kauppa
    {
        public const string KKartturi = "kartturi";
        public const int KartturiPrice = 690;

        static readonly Dictionary<Esine, Kartturi.Device> gpsDev = new Dictionary<Esine, Kartturi.Device>();
        static Material gpsBody, gpsBezel, gpsButton, gpsLabel;

        // ---------------------------------------------------------------- modele (ecran vers -z, debout)
        static void KartturiModel(GameObject g, out Vector3 size, out Transform screen)
        {
            if (gpsBody == null)
            {
                gpsBody = Plain(new Color(0.74f, 0.73f, 0.70f));
                gpsBezel = Plain(new Color(0.32f, 0.33f, 0.38f));
                gpsButton = Plain(new Color(0.55f, 0.12f, 0.30f));
                Shader us = Shader.Find("Unlit/Texture");
                gpsLabel = new Material(us != null ? us : Shader.Find("Legacy Shaders/Diffuse")) { mainTexture = Kartturi.Label("KARTTURI 2000", new Color32(40, 40, 110, 255), new Color32(189, 186, 178, 255)) };
            }
            size = new Vector3(0.1f, 0.165f, 0.03f);
            Piece(g, PrimitiveType.Cube, Vector3.zero, Vector3.zero, size, gpsBody);
            Piece(g, PrimitiveType.Cube, new Vector3(0f, 0.03f, -0.0148f), Vector3.zero, new Vector3(0.088f, 0.08f, 0.002f), gpsBezel);
            GameObject scr = Piece(g, PrimitiveType.Quad, new Vector3(0f, 0.031f, -0.0162f), Vector3.zero, new Vector3(0.072f, 0.0648f, 1f), blackPlastic);
            scr.name = "naytto";
            screen = scr.transform;
            Piece(g, PrimitiveType.Quad, new Vector3(0f, -0.019f, -0.0152f), Vector3.zero, new Vector3(0.066f, 0.0082f, 1f), gpsLabel);
            // croix et boutons A / B
            Piece(g, PrimitiveType.Cube, new Vector3(-0.025f, -0.048f, -0.016f), Vector3.zero, new Vector3(0.026f, 0.0085f, 0.004f), blackPlastic);
            Piece(g, PrimitiveType.Cube, new Vector3(-0.025f, -0.048f, -0.016f), Vector3.zero, new Vector3(0.0085f, 0.026f, 0.004f), blackPlastic);
            foreach (Vector2 bt in new[] { new Vector2(0.029f, -0.043f), new Vector2(0.016f, -0.053f) })
                Piece(g, PrimitiveType.Cylinder, new Vector3(bt.x, bt.y, -0.016f), new Vector3(90, 0, 0), new Vector3(0.012f, 0.002f, 0.012f), gpsButton);
            // haut-parleur, antenne
            for (int i = 0; i < 4; i++) Piece(g, PrimitiveType.Cube, new Vector3(0.024f + i * 0.005f, -0.072f, -0.0152f), new Vector3(0, 0, -30), new Vector3(0.0018f, 0.012f, 0.001f), bl);
            Piece(g, PrimitiveType.Cylinder, new Vector3(0.037f, 0.103f, 0.004f), Vector3.zero, new Vector3(0.008f, 0.022f, 0.008f), blackPlastic);
            Piece(g, PrimitiveType.Sphere, new Vector3(0.037f, 0.126f, 0.004f), Vector3.zero, new Vector3(0.011f, 0.011f, 0.011f), blackPlastic);
        }
        static Material bl { get { return blackPlastic; } }

        // ---------------------------------------------------------------- en main
        static Esine HeldKartturi()
        {
            foreach (Esine e in esineet)
                if (e.Kind == KKartturi && e.Go != null && e.Go.transform.parent != null && Game.UnderPlayer(e.Go.transform)) return e;
            return null;
        }

        static Esine gpsEq;
        static int gpsStage, gpsSince, gpsReenable = -1;   // 0 non, 1 en attente de la main du jeu, 2 tenu devant soi
        static PlayMakerFSM gpsFsm;
        static bool gpsRestart, gpsKin;
        static Vector3 gpsPos; static Quaternion gpsRot;
        static float gpsLift;
        public static bool KartturiUp { get { return gpsStage == 2; } }

        static void TickKartturi(float now)
        {
            if (gpsReenable >= 0 && Time.frameCount >= gpsReenable)
            {
                gpsReenable = -1;
                if (gpsFsm != null) { gpsFsm.enabled = true; gpsFsm.Fsm.RestartOnEnable = gpsRestart; }
                gpsFsm = null;
            }
            Esine held = HeldKartturi();
            if (gpsStage == 0)
            {
                if (held != null && Seats.Seated && eqStage == 0 && !KameraBusy && (Input.GetKeyDown(Keys.CameraViewfinder) || gpsTestUp)) { gpsEq = held; gpsStage = 1; gpsSince = Time.frameCount; gpsTestUp = false; }
            }
            else if (gpsEq == null || gpsEq.Go == null || held != gpsEq || !Seats.Seated) KartturiDown();
            else if (gpsStage == 1)
            {
                PlayMakerFSM f = PickUpFsm();
                if (f == null || Time.frameCount - gpsSince > 60) { L("copilote : KARTTURI pas leve (main du jeu " + (f != null ? f.ActiveStateName : "introuvable") + ")"); gpsStage = 0; gpsEq = null; }
                else if (f.ActiveStateName == "Part picked" || f.ActiveStateName == "Item picked")
                {
                    gpsFsm = f; gpsRestart = f.Fsm.RestartOnEnable;
                    f.Fsm.RestartOnEnable = false;
                    f.enabled = false;
                    Transform t = gpsEq.Go.transform;
                    gpsPos = t.localPosition; gpsRot = t.localRotation;
                    if (gpsEq.Rb != null) { gpsKin = gpsEq.Rb.isKinematic; gpsEq.Rb.isKinematic = true; }
                    gpsStage = 2; gpsLift = 0f;
                    ShowView(true);
                    L("copilote : KARTTURI tenu devant soi");
                }
            }
            else if (!KameraBusy && (Input.GetKeyDown(Keys.CameraViewfinder) || Input.GetMouseButtonDown(1))) KartturiDown();
            TickGpsScreens(now);
        }

        // Pose devant les yeux (a chaque image, apres la camera) : en bas de l'image, plus haut quand on baisse les yeux.
        public static void LateKartturi()
        {
            if (gpsStage != 2 || gpsEq == null || gpsEq.Go == null) return;
            Transform eye = EyeT;
            if (eye == null) return;
            float down = Mathf.Clamp01((Vector3.Angle(eye.forward, Vector3.down) < 90f ? 90f - Vector3.Angle(eye.forward, Vector3.down) : 0f) / 50f);
            gpsLift = Mathf.Lerp(gpsLift, down, 1f - Mathf.Exp(-10f * Time.deltaTime));
            // ce qu'on voit : la copie, devant les yeux, lisible
            Vector3 p = eye.position + eye.forward * Mathf.Lerp(0.2f, 0.19f, gpsLift) - eye.up * Mathf.Lerp(0.08f, 0.035f, gpsLift);
            if (gpsView != null) { gpsView.transform.position = p; gpsView.transform.rotation = Quaternion.LookRotation(p - eye.position, eye.up); }
            // l'objet (ce que voient les autres, les mains de l'avatar dessus) : a hauteur de poitrine, ecran vers les yeux
            Transform car = Seats.SeatedCar;
            Vector3 up = car != null ? car.up : Vector3.up, fw = Vector3.ProjectOnPlane(eye.forward, up);
            if (fw.sqrMagnitude < 1e-4f) fw = car != null ? car.forward : Vector3.forward;
            fw.Normalize();
            Vector3 q = eye.position + fw * 0.34f - up * Mathf.Lerp(0.3f, 0.24f, gpsLift);
            Transform t = gpsEq.Go.transform;
            t.position = q;
            t.rotation = Quaternion.LookRotation(q - eye.position, up);
        }

        // Copie montree devant les yeux (l'objet lui-meme, plus bas, est cache ici) : meme ecran.
        static GameObject gpsView;
        static readonly List<Renderer> gpsHidden = new List<Renderer>();
        static void ShowView(bool on)
        {
            if (gpsView != null) { UnityEngine.Object.Destroy(gpsView); gpsView = null; }
            foreach (Renderer r in gpsHidden) if (r != null) r.enabled = true;
            gpsHidden.Clear();
            if (!on || gpsEq == null || gpsEq.Go == null) return;
            Vector3 size; Transform screen;
            gpsView = Model(KKartturi, out size, out screen);
            gpsView.name = "kartturi (nakyma)";
            Kartturi.Device d;
            Renderer sr = screen != null ? screen.GetComponent<Renderer>() : null;
            if (sr != null && gpsEq.Screen != null && gpsEq.Screen.GetComponent<Renderer>() != null) sr.sharedMaterial = gpsEq.Screen.GetComponent<Renderer>().sharedMaterial;
            else if (sr != null && gpsDev.TryGetValue(gpsEq, out d)) sr.material = new Material(Shader.Find("Unlit/Texture")) { mainTexture = d.Tex };
            int layer = gpsEq.Go.layer;
            foreach (Transform c in gpsView.GetComponentsInChildren<Transform>(true)) c.gameObject.layer = layer;
            foreach (Renderer r in gpsEq.Go.GetComponentsInChildren<Renderer>()) if (r.enabled) { r.enabled = false; gpsHidden.Add(r); }
        }

        static void KartturiDown()
        {
            if (gpsStage == 2 && gpsEq != null && gpsEq.Go != null)
            {
                Transform t = gpsEq.Go.transform;
                t.localPosition = gpsPos; t.localRotation = gpsRot;
                if (gpsEq.Rb != null) gpsEq.Rb.isKinematic = gpsKin;
                if (gpsFsm != null) gpsReenable = Time.frameCount + 1;
                L("copilote : KARTTURI baisse");
            }
            if (gpsStage == 2)
            {
                ShowView(false);
            }
            gpsStage = 0; gpsEq = null;
        }

        // ---------------------------------------------------------------- ecrans
        static void TickGpsScreens(float now)
        {
            Transform eye = EyeT;
            if (eye == null) return;
            foreach (Esine e in esineet)
            {
                if (e.Kind != KKartturi || e.Go == null || e.Screen == null) continue;
                if ((e.Go.transform.position - eye.position).sqrMagnitude > 40f * 40f) continue;
                Kartturi.Device d;
                if (!gpsDev.TryGetValue(e, out d))
                {
                    d = Kartturi.NewDevice();
                    gpsDev[e] = d;
                    Shader us = Shader.Find("Unlit/Texture");
                    var r = e.Screen.GetComponent<Renderer>();
                    if (r != null) r.material = new Material(us != null ? us : Shader.Find("Legacy Shaders/Diffuse")) { mainTexture = d.Tex };
                    d.Beep = e.Go.AddComponent<AudioSource>();
                    d.Beep.spatialBlend = 1f; d.Beep.minDistance = 0.6f; d.Beep.maxDistance = 6f; d.Beep.volume = 0.55f; d.Beep.playOnAwake = false;
                    d.Beep.rolloffMode = AudioRolloffMode.Linear;
                }
                // vitesse : celle de la voiture ou on est assis (GPS tenu ici), sinon celle de l'objet
                Vector3 v = Vector3.zero;
                if (e == gpsEq || Game.UnderPlayer(e.Go.transform)) { Transform car = Seats.SeatedCar; Rigidbody cr = car != null ? car.GetComponent<Rigidbody>() : null; if (cr != null) v = cr.velocity; }
                else if (e.Rb != null && !e.Rb.isKinematic) v = e.Rb.velocity;
                Kartturi.Tick(d, e.Go.transform.position, v, now);
            }
        }

        // Core.OnGUI (par DrawKameraHint) : rappel des touches, GPS en main
        static bool DrawKartturiHint()
        {
            if (KameraBusy) return false;
            Esine g = HeldKartturi();
            if (g == null) return false;
            string s = gpsStage == 2 ? Keys.Label(Keys.CameraViewfinder) + Lang.T(" : BAISSER LE KARTTURI", ": LOWER THE KARTTURI")
                     : Seats.Seated ? "KARTTURI 2000  -  " + Keys.Label(Keys.CameraViewfinder) + Lang.T(" : LE TENIR DEVANT SOI", ": HOLD IT UP")
                     : Lang.T("KARTTURI 2000  -  ASSIEDS-TOI EN PASSAGER POUR LE LIRE", "KARTTURI 2000  -  SIT AS A PASSENGER TO READ IT");
            Style.Text(new Rect(0, Screen.height - Style.Px(gpsStage == 2 ? 40 : 120), Screen.width, Style.Px(30)), s, gpsStage == 2 ? 16 : 20, TextAnchor.MiddleCenter, new Color(1f, 1f, 1f, gpsStage == 2 ? 0.6f : 0.85f));
            return true;
        }

        // ---------------------------------------------------------------- essais
        // [Test] Autotest=copilote2 (2 joueurs) : l'invite demande un KARTTURI a 30 s, le prend en main a 34 s, s'assoit a
        // l'avant de [Test] TestVoiture (SORBET) a 37 s et le leve a 41 s (comme F) ; captures dumps\ecran-gps-<s>.png chez
        // chacun a 45 et 50 s (l'hote : avec [Test] CameraVoiture, l'avatar du passager).
        static int gpsTest;
        static bool gpsTestUp;
        public static void TestKartturi(string mode, float t)
        {
            if (mode != "copilote2" || !Active || root == null) return;
            bool guest = Session.Active && !Session.IsHost;
            if (guest && gpsTest == 0 && t > 30f && EyeT != null) { gpsTest = 1; SpawnItem(KKartturi, "", EyeT.position + EyeT.forward * 0.6f, Quaternion.identity); L("essai : KARTTURI demande"); }
            if (guest && gpsTest == 1 && t > 34f)
            {
                gpsTest = 2;
                Esine k = esineet.FindLast(e => e.Kind == KKartturi); PlayMakerFSM pf = PickUpFsm();
                if (k != null && pf != null) { k.Rb.isKinematic = false; if (EyeT != null) k.Go.transform.position = EyeT.position + EyeT.forward * 0.6f; pf.FsmVariables.GetFsmGameObject("PickedObject").Value = k.Go; Game.SetState(pf, PickSetState); }
                L("essai : KARTTURI " + (k != null ? k.Id : "absent") + ", main " + (pf != null ? pf.ActiveStateName : "?"));
            }
            if (guest && gpsTest == 2 && t > 37f) { gpsTest = 3; L("essai : " + Seats.TestSit(Config.Get("Test", "TestVoiture", "SORBET"), 0) + ", tenu " + (HeldKartturi() != null)); }
            if (guest && gpsTest == 3 && t > 41f) { gpsTest = 4; gpsTestUp = true; }
            // hote : camera d'essai devant la tete de l'avatar du passager (repere de sa voiture), qui le regarde
            if (!guest && t > 50f)
                foreach (Avatar av in PlayerSync.Avatars)
                {
                    Transform hb = av.HeadBone, car = av.VehicleT;
                    if (hb == null) continue;
                    if (gpsCam == null) { gpsCam = new GameObject("MWCoop-CameraGps").AddComponent<Camera>(); gpsCam.depth = 100; gpsCam.nearClipPlane = 0.03f; gpsCam.fieldOfView = 60f; }
                    Vector3 fw = car != null ? car.forward : hb.forward, up = car != null ? car.up : Vector3.up, rt = car != null ? car.right : hb.right;
                    gpsCam.transform.position = hb.position + fw * 0.75f - up * 0.05f - rt * 0.25f;
                    gpsCam.transform.LookAt(hb.position - up * 0.3f, up);
                    break;
                }
            float c1 = guest ? 45f : 60f; if (gpsTest < 10 && ((t > c1 && t < c1 + 0.5f) || (t > c1 + 5f && t < c1 + 5.5f)))
            {
                int sec = (int)t;
                if (sec != gpsShot) { gpsShot = sec; string png = System.IO.Path.Combine(System.IO.Path.Combine(Log.DataDir, "dumps"), "ecran-gps-" + sec + ".png"); Application.CaptureScreenshot(png); L("essai : capture " + png + ", leve " + KartturiUp + ", tenu " + (HeldKartturi() != null) + ", assis " + Seats.Seated);
                    foreach (Avatar av in PlayerSync.Avatars) { Gestures.Remote gr = Gestures.Of(av.Player.Id); L("essai : avatar #" + av.Player.Id + " drapeaux " + av.Player.State.Flags + " (porte " + ((av.Player.State.Flags & PlayerSync.F_Carry) != 0) + "), gestes " + (gr != null ? gr.Bits + " tenu " + gr.Held.ToString("F2") + " taille " + gr.HeldSize : "aucun") + ", place " + Seats.RemoteSeatIndex(av.Player.Id) + ", voiture " + (av.VehicleT != null ? av.VehicleT.name : "-")); } }
            }
        }
        static int gpsShot;

        // [Test] Autotest=copilote3 (seul) : KARTTURI cree et pris en main (25-28 s), assis a l'avant de la SORBET (31 s),
        // leve (34 s) ; de 37 a 67 s la voiture suit SS1 depuis le depart a [Test] CopiloteVitesse (20 m/s), cinematique ;
        // l'etat du GPS chaque seconde, captures dumps\ecran-gps3-<s>.png toutes les 5 s.
        static float gpsDrive;
        public static void TestKartturiDrive(string mode, float t)
        {
            // [Test] Autotest=copilote4 : camera d'essai devant le rayon des KARTTURI, capture dumps\ecran-gps-rayon.png a 30 s
            if (mode == "copilote4" && Active && root != null && t > 26f)
            {
                string item = Config.Get("Test", "RayonArticle", KKartturi);   // ([Test] RayonArticle=tv : la TV, de trois cotes)
                Offer o = offers.Find(x => x.Item == item);
                if (o == null || o.Col == null) return;
                if (gpsCam == null) { gpsCam = new GameObject("MWCoop-CameraGps").AddComponent<Camera>(); gpsCam.depth = 100; gpsCam.nearClipPlane = 0.03f; gpsCam.fieldOfView = 60f; }
                Vector3 lc = root.transform.InverseTransformPoint(o.Col.transform.position);
                int view = t < 31f ? 0 : t < 33f ? 1 : 2;
                Vector3 off = item == KKartturi ? new Vector3(1.5f, 0.1f, 0f) : view == 0 ? new Vector3(1.1f, 0.15f, 0f) : view == 1 ? new Vector3(0.9f, 0.4f, 0.6f) : new Vector3(1.4f, -0.05f, -0.7f);
                gpsCam.transform.position = root.transform.TransformPoint(lc + off);
                gpsCam.transform.LookAt(root.transform.TransformPoint(lc + new Vector3(0f, item == KKartturi ? -0.25f : -0.1f, 0f)));
                int shot = t > 30f && t < 31f ? 1 : t > 32f && t < 33f ? 2 : t > 34f && t < 35f ? 3 : 0;
                if (shot > gpsShot) { gpsShot = shot; Application.CaptureScreenshot(System.IO.Path.Combine(System.IO.Path.Combine(Log.DataDir, "dumps"), "ecran-rayon-" + item + "-" + shot + ".png")); L("essai : rayon " + item + " " + shot); }
                return;
            }
            if (mode != "copilote3" || !Active || root == null) return;
            if (gpsTest == 0 && t > 25f && EyeT != null) { gpsTest = 1; CreateItem("essaigps", KKartturi, EyeT.position + EyeT.forward * 0.6f, Quaternion.identity, ""); }
            if (gpsTest == 1 && t > 28f)
            {
                gpsTest = 2;
                Esine k = esineet.FindLast(e => e.Kind == KKartturi); PlayMakerFSM pf = PickUpFsm();
                if (k != null && pf != null) { k.Rb.isKinematic = false; k.Go.transform.position = EyeT.position + EyeT.forward * 0.6f; pf.FsmVariables.GetFsmGameObject("PickedObject").Value = k.Go; Game.SetState(pf, PickSetState); }
            }
            string carName = Game.Msc ? "SATSUMA" : "SORBET";   // (My Summer Car : la Satsuma, 24/10)
            if (gpsTest == 2 && t > 31f) { gpsTest = 3; L("essai : " + Seats.TestSit(carName, 0)); }
            if (gpsTest == 3 && t > 34f) { gpsTest = 4; gpsTestUp = true; }
            Rigidbody car = VehicleSync.Body(carName);
            // (en solo sur My Summer Car, pas de voitures suivies ni de places : le joueur lui-meme suit la speciale, GPS en main)
            Transform walker = car == null && Game.Msc ? Game.PlayerT : null;
            if (gpsTest == 4 && t > 37f && (car != null || walker != null))
            {
                if (t > 67f) { gpsTest = 5; if (car != null) car.isKinematic = false; L("essai : fin du parcours"); return; }
                if (car != null) car.isKinematic = true;
                gpsDrive = (t - 37f) * Config.GetFloat("Test", "CopiloteVitesse", 20f);
                Vector3 p, d;
                if (Kartturi.StagePoint(0, gpsDrive, out p, out d))
                {
                    if (car != null)
                    {
                        car.transform.position = p + Vector3.up * 0.6f;
                        car.transform.rotation = Quaternion.LookRotation(d, Vector3.up);
                        car.velocity = Vector3.zero;
                    }
                    else
                    {
                        var cc = walker.GetComponent<CharacterController>(); if (cc != null) cc.enabled = false;
                        walker.position = p + Vector3.up * 1.0f; walker.rotation = Quaternion.LookRotation(d, Vector3.up);
                    }
                }
                int sec = (int)t;
                if (sec != gpsShot)
                {
                    gpsShot = sec;
                    Esine k = esineet.FindLast(e => e.Kind == KKartturi); Kartturi.Device dv;
                    L("essai : " + gpsDrive.ToString("F0") + " m, GPS " + (k != null && gpsDev.TryGetValue(k, out dv) ? Kartturi.State(dv) : "?") + ", leve " + KartturiUp);
                    if (sec % 5 == 0) Application.CaptureScreenshot(System.IO.Path.Combine(System.IO.Path.Combine(Log.DataDir, "dumps"), "ecran-gps3-" + sec + ".png"));
                }
            }
        }
        static Camera gpsCam;

        static void OnLevelLoadedKartturi()
        {
            gpsDev.Clear(); gpsStage = 0; gpsEq = null; gpsFsm = null; gpsReenable = -1; gpsView = null; gpsHidden.Clear();
            gpsBody = null; gpsBezel = null; gpsButton = null; gpsLabel = null;
            Kartturi.OnLevelLoaded();
        }
    }
}
