using System;
using System.Collections.Generic;
using System.Reflection;
using HutongGames.PlayMaker;
using UnityEngine;

namespace MWCoop
{
    // Contrat de conduite de la voiture standalone (spec du 28/10, §2 et « Conduite ») : ce que fait l'automate DriveTrigger des
    // voitures de MSC (releve sur la Ruscko et le bateau, ref/msc-v.250908-04), fait par notre code -- rien de copie :
    //  - zone d'entree : a cote de la portiere conducteur (la caisse est fermee : boites de collision), voiture presque arretee et
    //    sur ses roues -> GUIdrive + « ENTER DRIVING MODE » ; touche du jeu DrivingMode (cInput) pour monter ;
    //  - au volant : PlayerStop, PlayerCurrentVehicle (« R5Maxi » : nom a nous, aucun automate du jeu ne le reconnait -- avec
    //    « Ruscko », les phares de la vraie Ruscko repondraient), PlayerInCar de l'automate Crouch (camera basse, joueur sans
    //    controle), CharacterController coupe, PLAYER range sous le pivot du conducteur, yeux poses au point de vue du modele
    //    (pivot de tete + EyeFromPivot du jeu, comme la copie) ; touches du jeu lues par cInput (gaz, frein, direction, frein a
    //    main, embrayage, rapport +/-) ;
    //  - descente : meme touche, voiture arretee (< 0,5 m/s) ; joueur pose au sol a cote de la portiere, tout remis ;
    //  - tableau de bord : boutons du modele (Knobs) -- contact (rouge : clic tenu = demarreur, clic moteur tournant = arret),
    //    phares (blanc : clic) ; vises au centre de l'ecran comme les commandes du jeu ;
    //  - moteur coupe a la naissance (Drivetrain.Start le demarre tout seul), son de demarrage de l'addon (VazSound).
    public static partial class StandaloneCar
    {
        public const string VehicleName = "R5Maxi";
        static VazCar.Built driving;
        static VazCar.Built prompted;        // (voiture dont nous avons pose le texte d'entree)
        static int hovered = -1;             // (bouton du tableau de bord vise, texte pose par nous)
        static float enteredAt, exitBlock, footHeight = -1f;
        static FieldInfo engineVelo;

        public static bool IsDriving { get { return driving != null; } }

        // Camera du joueur (FPSCamera/FPSCamera, celle qui rend) : PlayerSync.LocalCamera n'est remplie qu'en partie a plusieurs
        // (essai monte du 28/10 : vide en solo, yeux jamais recales)
        static Transform playerCam;
        static Transform PlayerCam()
        {
            if (playerCam != null) return playerCam;
            GameObject pl = Game.PlayerGo;
            if (pl == null) return null;
            playerCam = pl.transform.Find("Pivot/AnimPivot/Camera/FPSCamera/FPSCamera") ?? pl.transform.Find("Pivot/AnimPivot/Camera/FPSCamera");
            return playerCam;
        }

        // ---------------------------------------------------------------- cInput (touches du jeu, reglables dans ses options)
        static MethodInfo axisRaw, buttonDown;
        static bool cinTried;
        static void Cin()
        {
            if (cinTried) return;
            cinTried = true;
            Type t = Type.GetType("cInput, cInput");
            if (t == null) foreach (Assembly a in AppDomain.CurrentDomain.GetAssemblies()) { t = a.GetType("cInput"); if (t != null) break; }
            if (t == null) { Log.Warn("standalone : cInput introuvable (clavier de secours)"); return; }
            axisRaw = t.GetMethod("GetAxisRaw", new[] { typeof(string) });
            buttonDown = t.GetMethod("GetButtonDown", new[] { typeof(string) });
            // (noms des touches et axes du jeu que lit la standalone : definis dans cInput ?)
            MethodInfo ax = t.GetMethod("IsAxisDefined", new[] { typeof(string) }), kd = t.GetMethod("IsKeyDefined", new[] { typeof(string) });
            var sb = new System.Text.StringBuilder("standalone : cInput");
            try
            {
                foreach (string n in new[] { "Throttle", "Brake", "Horizontal", "Handbrake", "Clutch" }) sb.Append(" ").Append(n).Append(ax != null && (bool)ax.Invoke(null, new object[] { n }) ? "" : " (ABSENT)");
                foreach (string n in new[] { "ShiftUp", "ShiftDown", "DrivingMode" }) sb.Append(" ").Append(n).Append(kd != null && (bool)kd.Invoke(null, new object[] { n }) ? "" : " (ABSENT)");
            }
            catch (Exception e) { sb.Append(" : ").Append(e.Message); }
            Log.Info(sb.ToString());
        }
        public static float Axis(string n)
        {
            Cin();
            if (axisRaw == null) return 0f;
            try { float v = (float)axisRaw.Invoke(null, new object[] { n }); return float.IsNaN(v) ? 0f : Mathf.Clamp(v, -1f, 1f); } catch { return 0f; }
        }
        public static bool Down(string n)
        {
            Cin();
            if (buttonDown == null) return n == "DrivingMode" && Input.GetKeyDown(KeyCode.Return);
            try { return (bool)buttonDown.Invoke(null, new object[] { n }); } catch { return false; }
        }

        // ---------------------------------------------------------------- construction (appelee par Build)
        static void SetupDrive(VazCar.Built b, State st, Dictionary<string, Vector3> inf)
        {
            VazCar.Model m = b.M;
            st.Pivot = new GameObject("DriverPivot").transform;
            st.Pivot.SetParent(b.Car.transform, false);
            st.Pivot.localPosition = m.Head;
            // boutons visibles du tableau de bord (cylindres colores, comme sur la copie)
            if (m.Knobs != null)
                for (int i = 0; i < m.Knobs.Length; i++)
                {
                    VazCar.Knob kn = m.Knobs[i];
                    GameObject g = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                    UnityEngine.Object.DestroyImmediate(g.GetComponent<Collider>());
                    g.name = "bouton_" + i;
                    g.transform.SetParent(b.Body, false);
                    g.transform.localPosition = kn.At;
                    g.transform.localRotation = Quaternion.FromToRotation(Vector3.up, kn.Normal);
                    g.transform.localScale = new Vector3(0.017f, 0.006f, 0.017f);
                    Shader ks = Shader.Find("Legacy Shaders/Specular") ?? Shader.Find("Specular");
                    if (ks != null) g.GetComponent<MeshRenderer>().sharedMaterial = new Material(ks) { color = kn.Col, name = "bouton_" + i };
                }
            // phares : deux projecteurs aux feux avant du modele
            var heads = new List<Light>();
            foreach (string n in new[] { "headlight_l", "headlight_r" })
            {
                Vector3 p;
                if (!inf.TryGetValue(n, out p)) continue;
                var lg = new GameObject("phare_" + n.Substring(10)).transform;
                lg.SetParent(b.Body, false);
                lg.localPosition = p + new Vector3(0f, 0f, 0.08f);
                lg.localRotation = Quaternion.Euler(4f, 0f, 0f);
                Light l = lg.gameObject.AddComponent<Light>();
                l.type = LightType.Spot; l.range = 45f; l.spotAngle = 70f; l.intensity = 2.2f; l.color = new Color(1f, 0.96f, 0.86f); l.enabled = false;
                heads.Add(l);
            }
            st.Heads = heads.ToArray();
            // son moteur de l'addon (boucles + demarrage) au moteur du modele
            var eng = new GameObject("moteur_son").transform;
            eng.SetParent(b.Body, false); eng.localPosition = m.Engine;
            b.Sound = VazSound.Create(b.Car, eng, m);
            st.EngineOn = false;
            Lamps(b, st);
        }

        // ---------------------------------------------------------------- chaque image
        static void Drive(VazCar.Built b, State st)
        {
            var d = b.Axis as StandaloneDriver;
            if (d == null) return;
            Engine(b, st);
            if (b.Sound != null) b.Sound.Update();
            Lamps(b, st);
            GameObject pl = Game.PlayerGo;
            if (pl == null || !PlayerSync.InGame) return;
            if (driving == null)
            {
                bool zone = InZone(b, pl.transform) && b.Rb.velocity.magnitude < 2f && Vector3.Angle(b.Car.transform.up, Vector3.up) < 45f;
                if (zone)
                {
                    prompted = b;
                    Game.SetGlobalBool("GUIdrive", true); Game.SetGlobal("GUIinteraction", "ENTER DRIVING MODE");
                    if (Time.realtimeSinceStartup > exitBlock && Down("DrivingMode")) Enter(b, st);
                }
                else if (prompted == b) { prompted = null; Game.SetGlobalBool("GUIdrive", false); Game.SetGlobal("GUIinteraction", ""); }
                return;
            }
            if (driving != b) return;
            Knobs(b, st);
            if (Time.realtimeSinceStartup - enteredAt > 0.5f && Down("DrivingMode"))
            {
                if (b.Rb.velocity.magnitude < 0.5f) Exit(b, false);
                else Log.Info("standalone : descente refusee, la voiture roule (" + (b.Rb.velocity.magnitude * 3.6f).ToString("F0") + " km/h)");
            }
        }

        // Zone d'entree : le long de la portiere conducteur (cote gauche), a hauteur du siege, a moins de 1,4 m de la caisse.
        static bool InZone(VazCar.Built b, Transform pl)
        {
            Vector3 lp = b.Car.transform.InverseTransformPoint(pl.position);
            float side = Mathf.Abs(b.M.Head.x) > 0.01f ? Mathf.Sign(b.M.Head.x) : -1f;
            return lp.x * side > 0.55f && lp.x * side < 2.0f && Mathf.Abs(lp.z - b.M.Head.z) < 1.0f && lp.y > -1.0f && lp.y < 2.0f;
        }

        static void Enter(VazCar.Built b, State st)
        {
            GameObject pl = Game.PlayerGo;
            float gy;
            footHeight = Ground(pl.transform.position + Vector3.up * 0.3f, pl.transform, b.Car.transform, out gy) ? pl.transform.position.y - gy : 1.0f;
            footHeight = Mathf.Clamp(footHeight, 0.2f, 2f);
            var cc = pl.GetComponent<CharacterController>(); if (cc != null) cc.enabled = false;
            Game.SetGlobalBool("GUIdrive", false); Game.SetGlobal("GUIinteraction", ""); prompted = null;
            Game.SetGlobalBool("PlayerStop", true);
            Game.SetGlobal("PlayerCurrentVehicle", VehicleName);
            InCar(true);
            pl.transform.SetParent(st.Pivot, true);
            pl.transform.localRotation = Quaternion.identity;
            pl.transform.localPosition = Vector3.zero;
            var d = (StandaloneDriver)b.Axis;
            d.Keys = true; d.Handbrake = 0f;
            driving = b; enteredAt = Time.realtimeSinceStartup;
            Log.Info("standalone : joueur au volant de " + b.Car.name + " (pied a " + footHeight.ToString("F2") + " m)");
        }

        // force : sortie d'urgence (voiture detruite, fin de partie) sans condition de vitesse
        static void Exit(VazCar.Built b, bool force)
        {
            GameObject pl = Game.PlayerGo;
            Transform ct = b.Car != null ? b.Car.transform : null;
            if (pl != null)
            {
                pl.transform.SetParent(null, true);
                if (ct != null)
                {
                    float side = Mathf.Abs(b.M.Head.x) > 0.01f ? Mathf.Sign(b.M.Head.x) : -1f;
                    Vector3 at = ct.TransformPoint(new Vector3(side * 1.45f, 0.4f, b.M.Head.z));
                    float gy;
                    if (Ground(at + Vector3.up * 2f, pl.transform, ct, out gy)) at.y = gy + (footHeight > 0f ? footHeight : 1f);
                    pl.transform.position = at;
                    pl.transform.rotation = Quaternion.Euler(0f, ct.eulerAngles.y, 0f);
                }
                var cc = pl.GetComponent<CharacterController>(); if (cc != null) cc.enabled = true;
            }
            Game.SetGlobalBool("PlayerStop", false);
            Game.SetGlobal("PlayerCurrentVehicle", "");
            InCar(false);
            ClearHover();
            var d = b.Axis as StandaloneDriver;
            if (d != null) { d.Keys = false; d.Throttle = 0f; d.Brake = 0f; d.Steer = 0f; d.Clutch = 0f; d.Handbrake = 1f; }
            driving = null; exitBlock = Time.realtimeSinceStartup + 0.6f;
            Log.Info("standalone : joueur descendu" + (force ? " (force)" : "") + (pl != null ? " en " + pl.transform.position.ToString("F1") : ""));
        }

        static void InCar(bool on)
        {
            GameObject pl = Game.PlayerGo;
            PlayMakerFSM cr = pl != null ? Game.FsmOn(pl, "Crouch") : null;
            FsmBool v = cr != null ? cr.FsmVariables.FindFsmBool("PlayerInCar") : null;
            if (v != null) v.Value = on;
        }

        // Sol sous un point (premier collider plein qui n'est ni le joueur ni la voiture)
        static bool Ground(Vector3 from, Transform player, Transform car, out float y)
        {
            y = 0f; float best = float.MaxValue; bool ok = false;
            foreach (RaycastHit h in Physics.RaycastAll(from, Vector3.down, 6f))
            {
                if (h.collider == null || h.collider.isTrigger) continue;
                if (player != null && h.collider.transform.IsChildOf(player)) continue;
                if (car != null && h.collider.transform.IsChildOf(car)) continue;
                if (h.distance < best) { best = h.distance; y = h.point.y; ok = true; }
            }
            return ok;
        }

        // Apres les automates et la camera du joueur : yeux du joueur ramenes au point de vue (pivot de tete + EyeFromPivot du jeu,
        // comme la copie) ; volant visible tourne avec la direction.
        static void DriveLate(VazCar.Built b, State st)
        {
            var d = b.Axis as CarController;
            if (b.SteerPivot != null && d != null) b.SteerPivot.localRotation = Quaternion.AngleAxis(-Mathf.Clamp(d.steering, -1f, 1f) * (b.M.SteerDeg > 0f ? b.M.SteerDeg : 270f), b.SteerAxis);
            if (driving != b) return;
            GameObject pl = Game.PlayerGo;
            Transform cam = PlayerCam();
            if (pl == null || cam == null || pl.transform.parent != st.Pivot) return;
            Vector3 eye = b.Car.transform.TransformPoint(b.M.Head + Seats.EyeFromPivot);
            Vector3 delta = eye - cam.position;
            if (delta.sqrMagnitude > 1e-6f && delta.sqrMagnitude < 9f) pl.transform.position += delta;
        }

        // ---------------------------------------------------------------- moteur
        public static bool EngineRunning(VazCar.Built b) { State st; return states.TryGetValue(b, out st) && st.EngineOn; }
        public static void SetEngine(VazCar.Built b, bool on)
        {
            State st;
            if (!states.TryGetValue(b, out st)) return;
            st.EngineOn = on; st.CrankT = -1f;
            if (on) b.Dt.StartEngine();
            Log.Info("standalone : moteur " + (on ? "demarre" : "coupe"));
        }

        static void Engine(VazCar.Built b, State st)
        {
            if (st.EngineOn) return;
            // (moteur coupe : Drivetrain.Start le demarre a la naissance, et le ralenti le relancerait -- regime tenu a zero)
            if (b.Dt.rpm > 1f)
            {
                if (engineVelo == null) engineVelo = typeof(Drivetrain).GetField("engineAngularVelo", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                if (engineVelo != null) engineVelo.SetValue(b.Dt, 0f);
            }
            b.Dt.idlethrottle = 0f;
        }

        // ---------------------------------------------------------------- tableau de bord
        const int KIgnition = 0, KLights = 2;
        static void Knobs(VazCar.Built b, State st)
        {
            Transform cam = PlayerCam();
            VazCar.Knob[] ks = b.M.Knobs;
            if (cam == null || ks == null) return;
            int hit = -1; float bestPerp = 0.035f;
            foreach (int i in new[] { KIgnition, KLights })
            {
                if (i >= ks.Length) continue;
                Vector3 v = b.Car.transform.TransformPoint(ks[i].At) - cam.position;
                float along = Vector3.Dot(v, cam.forward);
                if (along < 0.15f || along > 1.3f) continue;
                float perp = (v - cam.forward * along).magnitude;
                if (perp < bestPerp) { bestPerp = perp; hit = i; }
            }
            if (hit < 0) { ClearHover(); CrankStop(b, st); return; }
            hovered = hit;
            Game.SetGlobalBool("GUIuse", true);
            Game.SetGlobal("GUIinteraction", hit == KIgnition ? (st.EngineOn ? "ENGINE OFF" : "START ENGINE") : st.LightsOn ? "LIGHTS OFF" : "LIGHTS ON");
            if (hit == KLights) { if (Input.GetMouseButtonDown(0)) { st.LightsOn = !st.LightsOn; Log.Info("standalone : phares " + (st.LightsOn ? "allumes" : "eteints")); } return; }
            // contact : clic moteur tournant = arret ; moteur coupe, clic tenu = demarreur, le moteur prend apres 0,9 s
            if (st.EngineOn) { if (Input.GetMouseButtonDown(0)) SetEngine(b, false); return; }
            if (Input.GetMouseButton(0))
            {
                if (st.CrankT < 0f) { st.CrankT = Time.time; if (b.Sound != null) b.Sound.ExternalStarter(true, false); }
                if (Time.time - st.CrankT > 0.9f) { SetEngine(b, true); if (b.Sound != null) b.Sound.ExternalStarter(false, true); }
            }
            else CrankStop(b, st);
        }
        static void CrankStop(VazCar.Built b, State st) { if (st.CrankT >= 0f) { st.CrankT = -1f; if (b.Sound != null) b.Sound.ExternalStarter(false, false); } }
        static void ClearHover() { if (hovered < 0) return; hovered = -1; Game.SetGlobalBool("GUIuse", false); Game.SetGlobal("GUIinteraction", ""); }

        // Feux du modele : avant allumes avec les phares, arriere faibles avec les phares et forts au freinage, recul en marche arriere.
        static void Lamps(VazCar.Built b, State st)
        {
            var d = b.Axis as CarController;
            bool f = st.LightsOn, br = d != null && d.brake > 0.1f, rv = b.Dt != null && b.Dt.gearRatios != null && b.Dt.gear >= 0 && b.Dt.gear < b.Dt.gearRatios.Length && b.Dt.gearRatios[b.Dt.gear] < 0f;
            if (b.LampsInit && f == b.FrontOn && br == b.BrakeOn && rv == b.ReverseOn) return;
            b.LampsInit = true; b.FrontOn = f; b.RearOn = f; b.BrakeOn = br; b.ReverseOn = rv;
            if (st.Heads != null) foreach (Light l in st.Heads) if (l != null) l.enabled = f;
            foreach (KeyValuePair<MeshRenderer, string> kv in b.Lamps)
            {
                if (kv.Key == null) continue;
                string k = kv.Value; Material m = null;
                if (k.EndsWith("_ar")) { if (br) b.Lit.TryGetValue(k, out m); else if (f && !b.Dim.TryGetValue(k, out m)) b.Lit.TryGetValue(k, out m); }
                else if ((k.EndsWith("_av") && f) || (k.EndsWith("_re") && rv)) b.Lit.TryGetValue(k, out m);
                Material basic; b.Mats.TryGetValue(k, out basic);
                kv.Key.sharedMaterial = m ?? basic;
            }
        }

        // ---------------------------------------------------------------- [Test] Autotest=standalonemonte (+ AddonStandalone=1,
        // StandaloneEssai=1) : sur la piste plate du banc, 20 cycles (voiture tournee de 0 / 120 / 240 degres) : joueur pose dans la zone
        // d'entree (texte du jeu verifie), monte (Enter : la touche du jeu ne se simule pas), yeux au point de vue, 2 s de gaz en 1re
        // puis frein, descend ; au sol a cote, CharacterController et PlayerStop rendus. Bilan : cycles reussis.
        static int mStep = -1, mCycle, mOk; static float mAt; static string mWhy;
        public static void MountTest(float t)
        {
            Test(t);
            if (mStep == 99 || cars.Count == 0 || cars[0].Car == null) return;
            VazCar.Built b = cars[0]; State st; if (!states.TryGetValue(b, out st)) return;
            var d = (StandaloneDriver)b.Axis;
            GameObject pl = Game.PlayerGo; if (pl == null) return;
            float e = t - mAt;
            Transform ct = b.Car.transform;
            switch (mStep)
            {
                case -1:
                    VazTenue.MakeTrack(); mStep = 0; mAt = t; mCycle = 0; mOk = 0;
                    if (!EngineRunning(b)) SetEngine(b, true);
                    break;
                case 0:   // voiture posee, joueur dans la zone
                {
                    Quaternion r = Quaternion.Euler(0f, (mCycle % 3) * 120f, 0f);
                    Vector3 p0 = new Vector3(mCycle * 30f, VazTenue.TrackY + 0.6f, 0f);
                    b.Rb.velocity = Vector3.zero; b.Rb.angularVelocity = Vector3.zero;
                    ct.position = p0; ct.rotation = r; b.Rb.position = p0; b.Rb.rotation = r;
                    var cc = pl.GetComponent<CharacterController>(); if (cc != null) cc.enabled = false;
                    pl.transform.position = ct.TransformPoint(new Vector3(-1.25f, 0.6f, b.M.Head.z));
                    if (cc != null) cc.enabled = true;
                    mWhy = ""; mStep = 1; mAt = t;
                    break;
                }
                case 1:
                    if (e < 1.5f) break;
                    FsmString gi = FsmVariables.GlobalVariables.FindFsmString("GUIinteraction");
                    if (!InZone(b, pl.transform)) mWhy += " hors zone (" + ct.InverseTransformPoint(pl.transform.position).ToString("F2") + ")";
                    else if (gi == null || gi.Value != "ENTER DRIVING MODE") mWhy += " texte '" + (gi != null ? gi.Value : "?") + "'";
                    Enter(b, st); mStep = 2; mAt = t;
                    break;
                case 2:
                    if (e < 1.5f) break;
                    {
                        FsmBool ps = FsmVariables.GlobalVariables.FindFsmBool("PlayerStop"); FsmString cv = FsmVariables.GlobalVariables.FindFsmString("PlayerCurrentVehicle");
                        Transform cam = PlayerCam();
                        float eyeErr = cam != null ? (cam.position - ct.TransformPoint(b.M.Head + Seats.EyeFromPivot)).magnitude : 9f;
                        if (ps == null || !ps.Value) mWhy += " PlayerStop faux";
                        if (cv == null || cv.Value != VehicleName) mWhy += " vehicule '" + (cv != null ? cv.Value : "?") + "'";
                        if (pl.transform.parent != st.Pivot) mWhy += " pas sous le pivot";
                        if (eyeErr > 0.05f) mWhy += " yeux a " + eyeErr.ToString("F2") + " m";
                        if (mCycle == 0) { st.LightsOn = true; Autotest.CaptureSoon("standalone-volant", 0.2f); }
                        d.Keys = false; d.Handbrake = 0f; d.Gear = 2; d.Throttle = 0.5f; d.Brake = 0f;
                        mStep = 3; mAt = t;
                    }
                    break;
                case 3:
                    if (e < 2f) break;
                    d.Throttle = 0f; d.Brake = 1f; d.Gear = 1; mStep = 4; mAt = t;
                    if (b.Rb.velocity.magnitude < 1f) mWhy += " n'a pas roule";
                    if (pl.transform.parent != st.Pivot) mWhy += " ejecte en roulant";
                    break;
                case 4:
                    if (e < 2.5f && b.Rb.velocity.magnitude > 0.3f) break;
                    if (b.Rb.velocity.magnitude >= 0.5f) { mWhy += " pas arretee"; b.Rb.velocity = Vector3.zero; }
                    d.Brake = 0f; d.Gear = -1;
                    Exit(b, false); mStep = 5; mAt = t;
                    break;
                case 5:
                    if (e < 1.5f) break;
                    {
                        FsmBool ps = FsmVariables.GlobalVariables.FindFsmBool("PlayerStop");
                        var cc = pl.GetComponent<CharacterController>();
                        Vector3 lp = ct.InverseTransformPoint(pl.transform.position);
                        if (pl.transform.parent != null) mWhy += " encore range";
                        if (ps != null && ps.Value) mWhy += " PlayerStop vrai";
                        if (cc != null && !cc.enabled) mWhy += " CharacterController coupe";
                        if (pl.transform.position.y < VazTenue.TrackY - 0.2f || pl.transform.position.y > VazTenue.TrackY + 3f) mWhy += " hauteur " + (pl.transform.position.y - VazTenue.TrackY).ToString("F2");
                        if (lp.x > -0.8f) mWhy += " pas a cote de la portiere (" + lp.ToString("F2") + ")";
                        bool ok = mWhy.Length == 0;
                        if (ok) mOk++;
                        Log.Info("standalone : monte " + (mCycle + 1) + "/20 (cap " + ((mCycle % 3) * 120) + ") " + (ok ? "OK" : "ECHEC :" + mWhy) + ", joueur a " + lp.ToString("F2") + " de la voiture, " + (pl.transform.position.y - VazTenue.TrackY).ToString("F2") + " m au-dessus de la piste");
                        mCycle++;
                        if (mCycle >= 20) { mStep = 99; Log.Info("standalone : monte fini, " + mOk + "/20 reussis"); }
                        else { mStep = 0; mAt = t; }
                    }
                    break;
            }
        }
    }
}
