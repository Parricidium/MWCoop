using System.Collections.Generic;
using System.Reflection;
using HutongGames.PlayMaker;
using UnityEngine;

namespace MWCoop
{
    // Banc d'essai de la tenue de route des voitures du F10 (VAZ et addons) -- demande de JD, 28/10, sur My Summer Car : « des que
    // je tourne au clavier, ca met un coup de volant et a haute vitesse elles partent en tonneau ». [Test] Autotest=vaztenue :
    //  - 20 s : la voiture du F10 ([Test] VazModele) ou, avec [Test] VazTenueVoiture=SATSUMA (ou un autre nom), une voiture du jeu ;
    //  - 30 s : releve de ses reglages a cote de ceux de la Satsuma (MSC) / Rivett (MWC) -- dumps\vaz-tenue.txt : controleur de
    //    conduite (vitesses de braquage clavier), essieux, roues, corps (masse, centre de gravite, inertie), moteur ;
    //  - puis sur une piste plate posee loin au-dessus de la carte (6 x 6 km, matiere de route du jeu), au volant, boite automatique
    //    le temps de l'essai, entrees « clavier » (tout ou rien, posees apres le controleur, comme une touche tenue) : pour chaque
    //    vitesse de [Test] VazTenueVitesses (50,100,150) : acceleration en ligne droite, puis gauche a fond 2,5 s -- roulis maxi,
    //    tonneau (plus de 75 degres), vitesse de lacet, temps pour que la direction arrive a 90 % ; puis slalom a 80 km/h (8 s,
    //    gauche / droite toutes les 1,2 s) et freinage a fond en courbe a 100 km/h. Chaque essai repart de l'arret, a plat.
    public static class VazTenue
    {
        static GameObject track;
        static Transform car;
        static Rigidbody rb;
        static AxisCarController ax;
        static Drivetrain dt;
        static int phase = -1, sub;
        static float phaseAt, maxRoll, maxYaw, steerAt90, speedAtTurn, maxRearHb, maxSteerDeg;
        static bool notDriving, launched;
        static float subLog;
        static bool phaseRamp;
        static float yawAt, yawKmh;
        static List<FsmFloat> knobs;
        static bool rolled, seated;
        static float[] speeds;
        static FieldInfo autoField;
        static readonly Vector3 Start = new Vector3(0f, 1501.2f, -2600f);
        const float TrackY = 1500f;

        static bool Mine { get { return !Net.Session.Active || Net.Session.IsHost || Two; } }
        // [Test] VazTenueDeux=1 (MSC, essai a deux, 28/10) : le banc tourne aussi chez l'invite, sur sa voie (80 m a cote) ; chacun
        // releve la copie de la voiture de l'autre toutes les 2 s (sauts, roues qui tremblent, braquage maxi de la copie).
        // [Test] VazTenueRelais=s (avec VazTenueDeux) : passage de volant -- banc fini et s secondes passees, l'hote descend de sa
        // voiture ; 6 s apres (et son banc fini), l'invite descend de la sienne, prend celle de l'hote et refait le banc avec.
        static bool Two { get { return Net.Session.Active && Config.GetInt("Test", "VazTenueDeux", 0) != 0; } }
        static bool Guest { get { return Net.Session.Active && !Net.Session.IsHost; } }
        static Vector3 StartPos { get { return Guest ? Start + new Vector3(80f, 0f, 0f) : Start; } }

        public static void Test(string mode, float t)
        {
            if (mode != "vaztenue" || !Mine) return;
            string other = Config.Get("Test", "VazTenueVoiture", "");
            if (phase == -1 && t > 15f) { phase = 0; MakeTrack(); }
            if (phase == 0 && t > 20f)
            {
                phase = 1;
                if (other.Length == 0) Log.Info("autotest vaztenue : demande -> " + Vaz.Request(Guest ? 4 : 3, Config.GetInt("Test", "VazModele", 0)));
            }
            if (phase == 1 && t > 30f)
            {
                phase = 2;
                car = other.Length > 0 ? Find(other) : Vaz.CarOf(Vaz.MeId);
                if (car == null) { Log.Info("autotest vaztenue : voiture introuvable (" + (other.Length > 0 ? other : "F10") + ")"); phase = 99; return; }
                rb = car.GetComponent<Rigidbody>(); ax = car.GetComponent<AxisCarController>(); dt = car.GetComponent<Drivetrain>();
                Dump();
                Seat();
            }
            if (phase == 2 && t > 31f) Seat();
            if (phase == 2 && (seated && t > 33f || t > 44f))
            {
                phase = 3; phaseAt = t;
                Place();
                Starter(true);
                speeds = Parse(Config.Get("Test", "VazTenueVitesses", "50,100,150"));
                sub = 0;
                Log.Info("autotest vaztenue : piste, " + car.name + " au depart, entree " + (driveFsm != null ? driveFsm.ActiveStateName : "?") + ", controleur " + (ax != null && ax.enabled ? "allume" : "COUPE"));
            }
            if (phase == 3 && dt != null) dt.startEngine = dt.rpm < 600f;   // (demarreur du moteur tant qu'il ne tourne pas)
            if (phase == 3 && t > phaseAt + 6f) { Starter(false); if (dt != null) dt.startEngine = false; phase = 10; phaseAt = t; Reset(); }
            if (phase >= 10 && phase < 99) Run(t);
            if (Two && phase >= 3) { Watch(t); Relay(t); }
        }

        // Copie de la voiture de l'autre : plus grand saut d'une image a l'autre (ecart entre deux deplacements successifs), plus
        // grand coup de direction des roues avant d'une image a l'autre, braquage maxi et corps cinematique.
        static Transform wCar; static Vector3 wPos, wStep; static float wJerk, wSpeed, wSteerJump, wNext, wLastSteer; static int wFrames;
        static void Watch(float t)
        {
            Transform c = null;
            foreach (int id in Vaz.RentIds()) if (id != Vaz.MeId && Vaz.CarOf(id) != null && Vaz.CarOf(id) != car) { c = Vaz.CarOf(id); break; }
            if (c == null) return;
            if (c != wCar) { wCar = c; wPos = c.position; wStep = Vector3.zero; wFrames = 0; wNext = t + 2f; return; }
            Vector3 step = c.position - wPos; wPos = c.position;
            if (wFrames > 0) wJerk = Mathf.Max(wJerk, (step - wStep).magnitude);
            wStep = step; wFrames++;
            if (Time.deltaTime > 0f) wSpeed = Mathf.Max(wSpeed, step.magnitude / Time.deltaTime * 3.6f);
            float steer = 0f, maxA = 0f;
            foreach (Wheel w in c.GetComponentsInChildren<Wheel>()) if (w.wheelPos.ToString().StartsWith("FRONT")) { steer = w.steering; maxA = Mathf.Max(maxA, w.maxSteeringAngle); }
            if (wFrames > 1) wSteerJump = Mathf.Max(wSteerJump, Mathf.Abs(steer - wLastSteer));
            wLastSteer = steer;
            if (t < wNext) return;
            wNext = t + 2f;
            Rigidbody r = c.GetComponent<Rigidbody>();
            Log.Info("autotest vaztenue : copie " + c.name + " : " + wFrames + " images, vitesse maxi " + wSpeed.ToString("F0") + " km/h, saut maxi " + wJerk.ToString("F3") + " m/image, coup de direction maxi " + wSteerJump.ToString("F2")
                     + ", braquage maxi des roues avant " + maxA.ToString("F1") + " deg, " + (r == null ? "sans corps" : r.isKinematic ? "cinematique" : "SIMULEE ICI") + ", en " + c.position.ToString("F0"));
            wJerk = 0f; wSpeed = 0f; wSteerJump = 0f; wFrames = 0;
        }

        static int relay; static float relayAt; static Transform relayCar;
        static void Relay(float t)
        {
            float at = Config.GetFloat("Test", "VazTenueRelais", 0f);
            if (at <= 0f || phase != 99 || car == null) return;
            if (!Guest)
            {
                if (relay == 0 && t > at) { relay = 1; relayAt = t; }
                if (relay == 1 && t < relayAt + 8f)
                {
                    if (driveFsm != null && driveFsm.ActiveStateName == "Player in car") driveFsm.SendEvent("Key DOWN");
                    if (driveFsm != null && driveFsm.ActiveStateName != "Player in car" && Game.PlayerGo != null)
                    {
                        relay = 2;
                        Move(car.position + car.right * 6f + Vector3.up * 1f);
                        Log.Info("autotest vaztenue : relais, l'hote descend de " + car.name + " (" + driveFsm.ActiveStateName + ")");
                    }
                }
                return;
            }
            if (relay == 0 && t > at + 6f)
            {
                foreach (int id in Vaz.RentIds()) if (id != Vaz.MeId && Vaz.CarOf(id) != null) { relayCar = Vaz.CarOf(id); break; }
                if (relayCar == null) { Log.Info("autotest vaztenue : relais, pas de voiture de l'hote"); relay = 9; return; }
                relay = 1; relayAt = t;
            }
            if (relay == 1)
            {
                if (driveFsm != null && driveFsm.ActiveStateName == "Player in car") { driveFsm.SendEvent("Key DOWN"); return; }
                Log.Info("autotest vaztenue : relais, l'invite descend de " + car.name + " et prend " + relayCar.name);
                car = relayCar; rb = car.GetComponent<Rigidbody>(); ax = car.GetComponent<AxisCarController>(); dt = car.GetComponent<Drivetrain>();
                driveFsm = null; knobs = null; seated = false; seatTry = 0f; relay = 2;
            }
            if (relay == 2)
            {
                Seat();
                if (seated)
                {
                    relay = 3; relayAt = t;
                    Log.Info("autotest vaztenue : relais, invite au volant de " + car.name + ", corps " + (rb != null && rb.isKinematic ? "CINEMATIQUE" : "simule ici") + ", braquage maxi " + MaxSteer().ToString("F1") + " deg");
                }
                else if (t > relayAt + 15f) { Log.Info("autotest vaztenue : relais, pas au volant (" + (driveFsm != null ? driveFsm.ActiveStateName : "?") + ")"); relay = 9; }
            }
            if (relay == 3 && t > relayAt + 2f)
            {
                relay = 4;
                speeds = new[] { 100f, 150f };
                phase = 10; phaseAt = t; Reset();
                Log.Info("autotest vaztenue : relais, banc avec la voiture de l'hote (100, 150 km/h, slalom, freinage)");
            }
        }

        static void Move(Vector3 p)
        {
            GameObject pl = Game.PlayerGo; if (pl == null) return;
            var cc = pl.GetComponent<CharacterController>(); if (cc != null) cc.enabled = false;
            pl.transform.position = p;
            if (cc != null) cc.enabled = true;
        }

        static void Run(float t)
        {
            if (car == null || rb == null || ax == null || dt == null) { phase = 99; return; }
            float dtp = t - phaseAt;
            float kmh = rb.velocity.magnitude * 3.6f;
            float roll = Vector3.Angle(car.up, Vector3.up);
            if (roll > maxRoll) maxRoll = roll;
            if (roll > 75f) rolled = true;
            float yaw = Mathf.Abs(rb.angularVelocity.y) * Mathf.Rad2Deg;
            // essais 10 + i : virage a speeds[i] ; 20 : slalom ; 21 : freinage en courbe ; 30 : fini
            bool turnTest = phase >= 10 && phase < 10 + speeds.Length;
            float target = turnTest ? speeds[phase - 10] : phase == 20 ? 80f : 100f;
            Auto();
            if (dt.rpm < 300f) dt.startEngine = true; else if (dt.startEngine) dt.startEngine = false;   // (cale : relance)
            if (sub == 0)
            {
                // acceleration pour de vrai, boite automatique (45 s au plus) ; sinon (moteur qui ne tourne pas) : lancee, posee 1,5 s
                // sur ses ressorts puis montee en vitesse au point mort, virage au point mort (un moteur au ralenti en prise avec des roues
                // a 100 km/h bloquerait les roues arriere : tete-a-queue du banc, pas de la voiture)
                bool engine = dt.rpm > 300f;
                if (t >= subLog) { subLog = t + 5f; Log.Info("autotest vaztenue : mise en vitesse, " + kmh.ToString("F0") + " km/h vises " + target + ", regime " + dt.rpm.ToString("F0") + ", rapport " + dt.gear + ", depuis " + dtp.ToString("F0") + " s"); }
                if (engine && dtp < 45f && kmh < target && !(dtp > 8f && kmh < 10f)) { Keys(1f, 0f, 0f); return; }   // (8 s sans avancer : moteur sans effet, lancee)
                if (!engine && dtp < 1.5f) { Keys(0f, 0f, 0f); return; }
                launched = !(engine && kmh >= target - 3f);
                phaseRamp = launched && kmh < target - 1f;
                if (phaseRamp)
                {
                    // (montee en vitesse progressive, 40 km/h par seconde au point mort : la vitesse donnee d'un coup a 150 km/h
                    // faisait mourir le conducteur « d'accident » -- le mod serrait alors le frein a main de la voiture garee et elle
                    // partait en tete-a-queue : essai de [MSC], 28/10)
                    dt.gear = 1;
                    float v = Mathf.Min(target, kmh + 40f * Time.deltaTime) / 3.6f;
                    rb.velocity = car.forward * v; rb.angularVelocity = Vector3.zero;
                    foreach (Wheel w in car.GetComponentsInChildren<Wheel>()) if (w.radius > 0.05f) w.angularVelocity = v / w.radius;
                    Keys(0f, 0f, 0f);
                    return;
                }
                sub = 1; phaseAt = t; maxRoll = roll; maxYaw = 0f; rolled = false; steerAt90 = -1f; speedAtTurn = launched ? target : kmh;
                maxRearHb = 0f; maxSteerDeg = 0f; notDriving = false; yawAt = -1f; yawKmh = 0f;
                return;
            }
            if (launched) dt.gear = 1;
            if (yaw > maxYaw) maxYaw = yaw;
            if (yaw > 60f && yawAt < 0f) { yawAt = dtp; yawKmh = kmh; }
            foreach (Wheel w in car.GetComponentsInChildren<Wheel>()) { if (w.wheelPos.ToString().StartsWith("REAR")) maxRearHb = Mathf.Max(maxRearHb, w.handbrake); else maxSteerDeg = Mathf.Max(maxSteerDeg, Mathf.Abs(w.steering) * w.maxSteeringAngle); }
            if (VehicleSync.LocalDrivingRoot != car) notDriving = true;
            if (steerAt90 < 0f && Mathf.Abs(ax.steering) >= 0.9f) steerAt90 = dtp;
            if (turnTest)
            {
                Keys(Config.GetFloat("Test", "VazTenueGaz", 0f), 0f, -Config.GetFloat("Test", "VazTenueBraquage", 1f));
                if (dtp > 2.5f || rolled) End(t, "virage a gauche a fond");
            }
            else if (phase == 20)
            {
                float s = ((int)(dtp / 1.2f)) % 2 == 0 ? -1f : 1f;
                Keys(Config.GetFloat("Test", "VazTenueGaz", 0f), 0f, s);
                if (dtp > 8f || rolled) End(t, "slalom 1,2 s");
            }
            else if (phase == 21)
            {
                Keys(0f, 1f, -0.5f);
                if (dtp > 3f || rolled || kmh < 3f) End(t, "freinage a fond en courbe (demi-braquage)");
            }
        }

        static void End(float t, string what)
        {
            Log.Info("autotest vaztenue : " + Name() + " " + what + " a " + speedAtTurn.ToString("F0") + " km/h" + (launched ? " (lancee, point mort)" : " (acceleree, pied leve)") + " : roulis maxi " + maxRoll.ToString("F0") + " deg"
                     + (rolled ? ", TONNEAU" : ", pas de tonneau") + ", lacet maxi " + maxYaw.ToString("F0") + " deg/s, direction a 90 % en "
                     + (steerAt90 < 0f ? "jamais" : steerAt90.ToString("F2") + " s") + ", vitesse a la fin " + (rb.velocity.magnitude * 3.6f).ToString("F0") + " km/h, roues avant braquees jusqu'a " + maxSteerDeg.ToString("F1") + " deg, frein a main AR maxi " + maxRearHb.ToString("F2") + (yawAt >= 0f ? ", lacet > 60 deg/s des " + yawAt.ToString("F2") + " s (" + yawKmh.ToString("F0") + " km/h)" : ""));
            phase = phase >= 10 + speeds.Length - 1 && phase < 20 ? 20 : phase == 20 ? 21 : phase == 21 ? 30 : phase + 1;
            if (phase == 30) { Keys(0f, 1f, 0f); Log.Info("autotest vaztenue fini"); phase = 99; return; }
            phaseAt = t; Reset();
        }

        // Entrees du controleur, posees a chaque image apres lui (LateUpdate) et dans Update : touche tenue.
        static float kThrottle, kBrake, kSteer;
        static void Keys(float throttle, float brake, float steer) { kThrottle = throttle; kBrake = brake; kSteer = steer; Apply(); }
        public static void Late() { if (phase >= 10 && phase < 99) Apply(); }
        static void Apply()
        {
            if (ax == null) return;
            ax.throttleInput = kThrottle; ax.brakeInput = kBrake; ax.steerInput = kSteer; ax.handbrakeInput = 0f;
            // (tirette du frein a main relachee : automate Use du levier, KnobPos)
            if (knobs == null)
            {
                knobs = new List<FsmFloat>();
                foreach (PlayMakerFSM f in car.GetComponentsInChildren<PlayMakerFSM>(true))
                    if (f.FsmName == "Use" && (f.gameObject.name.Contains("ParkingBrake") || f.gameObject.name.Contains("HandBrake") || f.gameObject.name.Contains("Handbrake"))) { FsmFloat k = f.FsmVariables.FindFsmFloat("KnobPos"); if (k != null) knobs.Add(k); }
                Log.Info("autotest vaztenue : " + knobs.Count + " tirette(s) de frein a main relachee(s)");
            }
            foreach (FsmFloat k in knobs) k.Value = 0f;
            // (le banc ne passe pas l'automate d'entree a « Player in car » : le mod tient alors le frein a main arriere serre comme sur
            // une voiture garee -- VazCar.MscBrake ; relache ici, apres lui, a chaque image)
            foreach (Wheel w in car.GetComponentsInChildren<Wheel>()) if (w.wheelPos.ToString().StartsWith("REAR")) w.handbrake = 0f;
        }

        // Boite : passee par le banc (la boite automatique du jeu ne monte pas les rapports sur la copie de la Ruscko : elle
        // restait en 1re puis repassait au point mort) -- rapport superieur au-dela de 85 % du regime maxi, inferieur sous 2 500
        // tr/min ; point mort pendant la montee en vitesse et le virage lances.
        static float shiftAt;
        static void Auto()
        {
            if (dt == null) return;
            if (autoField == null) autoField = dt.GetType().GetField("automatic");
            if (autoField != null && autoField.FieldType == typeof(bool)) autoField.SetValue(dt, false);
            bool neutral = launched && (sub == 1 || (sub == 0 && phaseRamp));
            if (neutral) { dt.gear = 1; return; }
            if (dt.gear < 2) dt.gear = 2;
            float now = Time.realtimeSinceStartup;
            if (now < shiftAt || dt.gearRatios == null) return;
            if (dt.rpm > dt.maxRPM * 0.85f && dt.gear < dt.gearRatios.Length - 1) { dt.gear++; shiftAt = now + 0.6f; }
            else if (dt.rpm < 2500f && dt.gear > 2) { dt.gear--; shiftAt = now + 0.6f; }
        }

        static void Reset()
        {
            sub = 0; launched = false; phaseRamp = false; Keys(0f, 1f, 0f);
            Place();
        }

        static void Place()
        {
            if (car == null || rb == null) return;
            rb.velocity = Vector3.zero; rb.angularVelocity = Vector3.zero;
            car.position = StartPos; car.rotation = Quaternion.identity;
            rb.position = StartPos; rb.rotation = Quaternion.identity;
            foreach (Rigidbody r in car.GetComponentsInChildren<Rigidbody>()) { r.velocity = Vector3.zero; r.angularVelocity = Vector3.zero; }
        }

        // Au volant pour de vrai (automate d'entree de la voiture a « Player in car », sinon le mod la traite comme garee : frein a
        // main arriere serre) : joueur pose dans la zone, puis « touche Entree » quand l'automate l'attend ; essais repetes.
        static PlayMakerFSM driveFsm;
        static float seatTry;
        static void Seat()
        {
            if (car == null || seated) return;
            if (driveFsm == null)
                foreach (PlayMakerFSM f in car.GetComponentsInChildren<PlayMakerFSM>(true)) if (f.FsmName == "PlayerTrigger" && f.gameObject.name.StartsWith("DriveTrigger")) { driveFsm = f; break; }
            if (driveFsm == null) { Log.Info("autotest vaztenue : zone du conducteur introuvable"); seated = true; return; }
            string st = driveFsm.ActiveStateName;
            if (st == "Player in car") { seated = true; Log.Info("autotest vaztenue : au volant (Player in car)"); return; }
            if (Time.realtimeSinceStartup < seatTry) return;
            seatTry = Time.realtimeSinceStartup + 0.7f;
            GameObject pl = Game.PlayerGo;
            if (pl != null && Vector3.Distance(pl.transform.position, driveFsm.transform.position) > 1.2f)
            {
                var cc = pl.GetComponent<CharacterController>(); if (cc != null) cc.enabled = false;
                pl.transform.position = driveFsm.transform.position + Vector3.up * 0.3f;
                if (cc != null) cc.enabled = true;
            }
            if (st == "Press return") driveFsm.SendEvent("Key DOWN");
            else if (st == "Wait for player" || st == "State 1") Game.SetState(driveFsm, "Check seat");
            Log.Info("autotest vaztenue : entree, automate " + st + " -> " + driveFsm.ActiveStateName);
        }

        static void Starter(bool on)
        {
            if (car == null) return;
            Transform st = car.Find("Starter") ?? car.Find("LOD/Starter");
            PlayMakerFSM f = null;
            if (st != null) foreach (PlayMakerFSM x in st.GetComponents<PlayMakerFSM>()) if (x.FsmName == "Starter") f = x;
            if (f == null) foreach (PlayMakerFSM x in car.GetComponentsInChildren<PlayMakerFSM>(true)) if (x.FsmName == "Starter") { f = x; break; }
            if (f == null) { if (on) Log.Info("autotest vaztenue : demarreur introuvable"); return; }
            FsmBool acc = f.FsmVariables.GetFsmBool("ACC"), s = f.FsmVariables.GetFsmBool("Starting");
            if (acc != null) acc.Value = true;
            if (s != null) s.Value = on;
            if (!on) Log.Info("autotest vaztenue : demarreur " + f.ActiveStateName + ", regime " + (dt != null ? dt.rpm.ToString("F0") : "?"));
        }

        static void MakeTrack()
        {
            PhysicMaterial road = null; var count = new Dictionary<PhysicMaterial, int>();
            foreach (Collider c in Object.FindObjectsOfType<Collider>())
            {
                PhysicMaterial m = c.sharedMaterial;
                if (m == null || c.isTrigger) continue;
                string n = m.name;
                if (n.StartsWith("Track") || n.StartsWith("RoadSnow") || n.StartsWith("Asphalt")) { int k; count.TryGetValue(m, out k); count[m] = k + 1; }
            }
            int best = 0; foreach (var kv in count) if (kv.Value > best) { best = kv.Value; road = kv.Key; }
            track = GameObject.CreatePrimitive(PrimitiveType.Cube);
            track.name = "MWCoop-PisteEssai";
            Object.Destroy(track.GetComponent<Renderer>());
            track.transform.position = new Vector3(0f, TrackY - 0.5f, 0f);
            track.transform.localScale = new Vector3(6000f, 1f, 6000f);
            Collider col = track.GetComponent<Collider>();
            if (road != null) col.sharedMaterial = road;
            Log.Info("autotest vaztenue : piste 6 x 6 km a " + TrackY + " m, matiere " + (road != null ? road.name + " (" + best + " colliders)" : "aucune"));
        }

        static Transform Find(string n)
        {
            GameObject g = Game.FindAny(n);
            if (g == null) foreach (Transform x in Resources.FindObjectsOfTypeAll<Transform>()) if (x.hideFlags == HideFlags.None && x.parent == null && x.name.StartsWith(n)) { g = x.gameObject; break; }
            return g != null ? g.transform : null;
        }

        static string Name() { return car != null ? car.name : "?"; }
        static float MaxSteer() { float m = 0f; if (car != null) foreach (Wheel w in car.GetComponentsInChildren<Wheel>()) m = Mathf.Max(m, w.maxSteeringAngle); return m; }

        static float[] Parse(string s)
        {
            var l = new List<float>();
            foreach (string p in s.Split(',')) { float v; if (float.TryParse(p.Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v)) l.Add(v); }
            return l.ToArray();
        }

        // Releve : la voiture d'essai, puis la voiture du jeu de reference.
        static void Dump()
        {
            var sb = new System.Text.StringBuilder();
            DumpCar(sb, car);
            Transform refCar = Find(Game.Msc ? "SATSUMA(557kg, 248)" : "CORRIS");
            if (refCar != null && refCar != car) { sb.Append("\n\n"); DumpCar(sb, refCar); }
            string dir = System.IO.Path.Combine(Log.DataDir, "dumps");
            System.IO.Directory.CreateDirectory(dir);
            System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "vaz-tenue.txt"), sb.ToString());
            Log.Info("autotest vaztenue : releve dumps\\vaz-tenue.txt (" + car.name + (refCar != null ? " et " + refCar.name : "") + ")");
        }

        static void DumpCar(System.Text.StringBuilder sb, Transform c)
        {
            sb.Append("==== ").Append(c.name).Append('\n');
            Rigidbody r = c.GetComponent<Rigidbody>();
            if (r != null)
                sb.Append("Rigidbody : masse ").Append(r.mass).Append(", centre ").Append(r.centerOfMass.ToString("F3")).Append(", inertie ").Append(r.inertiaTensor.ToString("F1"))
                  .Append(" rot ").Append(r.inertiaTensorRotation.eulerAngles.ToString("F0")).Append(", trainee ").Append(r.drag).Append(" / ").Append(r.angularDrag)
                  .Append(", interp ").Append(r.interpolation).Append(", vitesse angulaire maxi ").Append(r.maxAngularVelocity).Append('\n');
            foreach (string cn in new[] { "AxisCarController", "CarController", "MobileCarController", "CarDynamics", "Drivetrain", "Axles", "Aerodynamics", "FuelTank", "SteeringWheel" })
            {
                Component k = c.GetComponent(cn);
                if (k == null) continue;
                sb.Append("-- ").Append(cn).Append(" (").Append(k.GetType().Name).Append(")\n");
                Fields(sb, k, "  ");
                if (cn == "Axles")
                    foreach (string an in new[] { "frontAxle", "rearAxle" })
                    {
                        FieldInfo af = k.GetType().GetField(an); object a = af != null ? af.GetValue(k) : null;
                        if (a != null) { sb.Append("  [").Append(an).Append("]\n"); Fields(sb, a, "    "); }
                    }
            }
            foreach (Wheel w in c.GetComponentsInChildren<Wheel>(true))
            {
                sb.Append("-- Wheel ").Append(w.wheelPos).Append(" ").Append(Recon.Path(w.transform)).Append('\n');
                Fields(sb, w, "  ");
                if (w.wheelPos.ToString() != "FRONT_LEFT") break;   // (une avant, une arriere : les autres sont symetriques)
            }
            foreach (Wheel w in c.GetComponentsInChildren<Wheel>(true)) if (w.wheelPos.ToString() == "REAR_LEFT") { sb.Append("-- Wheel REAR_LEFT\n"); Fields(sb, w, "  "); break; }
            Component cd = c.GetComponent("CarDynamics");
            FieldInfo cf = cd != null ? cd.GetType().GetField("centerOfMass") : null;
            Transform com = cf != null ? cf.GetValue(cd) as Transform : null;
            if (com != null) sb.Append("centre de gravite (CarDynamics) : ").Append(c.InverseTransformPoint(com.position).ToString("F3")).Append('\n');
            foreach (PlayMakerFSM f in c.GetComponentsInChildren<PlayMakerFSM>(true))
                if (f.FsmName == "SteerLimit" || f.FsmName == "Steering" || f.FsmName.Contains("Steer"))
                {
                    sb.Append("-- automate ").Append(Recon.Path(f.transform)).Append("::").Append(f.FsmName).Append(f.enabled ? "" : " (coupe)").Append(" :");
                    foreach (FsmFloat v in f.FsmVariables.FloatVariables) sb.Append(' ').Append(v.Name).Append('=').Append(v.Value.ToString("F2"));
                    sb.Append('\n');
                }
        }

        static void Fields(System.Text.StringBuilder sb, object o, string ind)
        {
            foreach (FieldInfo f in o.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                System.Type t = f.FieldType;
                if (!(t == typeof(float) || t == typeof(int) || t == typeof(bool) || t.IsEnum || t == typeof(Vector3) || t == typeof(float[]))) continue;
                object v = f.GetValue(o);
                sb.Append(ind).Append(f.Name).Append(" = ");
                if (v is float[]) sb.Append(string.Join(" ", System.Array.ConvertAll((float[])v, x => x.ToString("F3"))));
                else if (v is float) sb.Append(((float)v).ToString("G5"));
                else sb.Append(v);
                sb.Append('\n');
            }
        }
    }
}
