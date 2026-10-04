using System.Collections.Generic;
using MWCoop.Net;
using UnityEngine;

namespace MWCoop
{
    // Voitures (objets racines portant CarDynamics : CORRIS, GIFU, KEKMET, FLATBED...).
    // Celui qui conduit fait autorite : il envoie position et vitesses 20 fois/s ; chez les autres
    // la voiture devient cinematique et suit. Sans conducteur, l'hote recale toutes les 2 s les
    // voitures qui ont derive (arrivee d'un invite, voiture poussee...).
    // Conduite : automate 'PlayerTrigger' d'un objet DriveTrigger* de la voiture, etat 'Player in car'.
    // Le conducteur envoie aussi regime, accelerateur et braquage : chez les autres, la copie
    // cinematique a son Drivetrain, ses Wheel et son AxisCarController coupes ; le regime et
    // l'accelerateur recus nourrissent le son, et les roues sont tournees et braquees ici
    // (Wheel.CalcWheelMovement du jeu, sans sa physique). Le bruit du moteur est fait par des objets
    // a AudioSource sous un conteneur 'Sounds' (KEKMET/LOD/Sounds/SoundKekmet...), allumes par le
    // contact quand le moteur tourne : le conducteur envoie lesquels sont actifs, avec leur hauteur
    // et leur volume (leur automate les calcule d'apres le vehicule conduit LOCALEMENT : coupe ici).
    // Moteur laisse tournant : celui qui sort de la voiture en garde la main (etat 2) tant que son moteur
    // tourne et que personne d'autre ne la prend : les autres entendent toujours ce moteur, sans
    // conducteur assis. A la fin de la copie, moteur, commandes et roues reprennent l'etat d'AVANT
    // (le jeu coupe le Drivetrain d'un moteur arrete : le rallumer de force le faisait caler et
    // redemarrer en boucle, sons superposes).
    // Chaleur de l'habitacle : la source de chaleur de la voiture (HeatSource*, ecrite par son automate
    // CarTemp... d'apres le moteur, le chauffage, les portieres) est envoyee avec la voiture ; sur la
    // copie (moteur froid), elle est reprise apres la logique du jeu -- le passager se rechauffe aussi.
    public static class VehicleSync
    {
        class Car
        {
            public int Index;
            public string Name;
            public Rigidbody Body;
            public PlayMakerFSM Drive;
            public int RemoteDriver = -1;       // joueur qui la conduit chez lui (-1 : personne)
            public int RemoteBy = -1;           // joueur qui la fait avancer et tourner chez lui (conducteur ou moteur laisse tournant)
            public bool DtWas, AxisWas; public bool[] WheelsWas;
            public Dictionary<Joint, Vector2> JointsWas;   // attaches rendues incassables sur la copie
            public float NextJoints;
            public float LastRemote;
            public Vector3 Pos, Vel, AngVel;
            public Quaternion Rot;
            public bool Kinematic;
            public bool WasKinematic;
            public float NextLog;
            public Drivetrain Dt;
            public Wheel[] Wheels;
            public AxisCarController Axis;
            public SoundController Sound;     // coupe par le jeu quand le joueur local n'est pas au volant
            public bool SoundWasOn;
            public GameObject[] SoundObjs;
            public bool[] SoundObjsWas;
            public int SoundMask;
            public float[] SoundPitch, SoundVol;
            public float Rpm, Throttle, Steer;
            public HutongGames.PlayMaker.FsmFloat Heat; public float RemoteHeat = float.NaN;   // temperature de la source de chaleur
            public float[] WheelRot;
        }

        static readonly List<Car> cars = new List<Car>();
        static bool scanned;
        static float scanAt = -1, nextFast, nextSlow;
        public static int LocalDriving = -1;    // index de la voiture conduite localement
        static int owned = -1, ownedTick;        // voiture quittee moteur tournant : on en garde la main
        public static string LocalDrivingName { get { return LocalDriving >= 0 && LocalDriving < cars.Count ? cars[LocalDriving].Name : null; } }

        public static void OnLevelLoaded()
        {
            cars.Clear();
            scanned = false;
            LocalDriving = -1; owned = -1;
            scanAt = PlayerSync.InGame ? Time.realtimeSinceStartup + 3f : -1;
        }

        static void Scan()
        {
            scanned = true;
            var roots = new List<GameObject>();
            foreach (Component c in Object.FindObjectsOfType<Rigidbody>())
            {
                GameObject go = c.gameObject;
                if (go.transform.parent == null && go.GetComponent("CarDynamics") != null) roots.Add(go);
            }
            roots.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            foreach (GameObject go in roots)
            {
                var car = new Car { Index = cars.Count, Name = go.name, Body = go.GetComponent<Rigidbody>(),
                                    Dt = go.GetComponent<Drivetrain>(), Wheels = go.GetComponentsInChildren<Wheel>(true),
                                    Axis = go.GetComponent<AxisCarController>(), Sound = go.GetComponent<SoundController>() };
                car.WheelRot = new float[car.Wheels.Length];
                var snd = new List<GameObject>();
                foreach (Transform t in go.GetComponentsInChildren<Transform>(true))
                    if (t.name == "Sounds")
                        foreach (Transform s in t) if (s.GetComponent<AudioSource>() != null && snd.Count < 16) snd.Add(s.gameObject);
                car.SoundObjs = snd.ToArray();
                car.SoundObjsWas = new bool[snd.Count];
                car.SoundPitch = new float[snd.Count];
                car.SoundVol = new float[snd.Count];
                foreach (PlayMakerFSM f in go.GetComponentsInChildren<PlayMakerFSM>(true))
                    if (f.FsmName == "PlayerTrigger" && f.gameObject.name.StartsWith("DriveTrigger")) { car.Drive = f; break; }
                foreach (PlayMakerFSM f in go.GetComponentsInChildren<PlayMakerFSM>(true))
                    if (f.FsmName == "Data" && f.gameObject.name.StartsWith("HeatSource")) { car.Heat = f.FsmVariables.FindFsmFloat("Temperature"); break; }
                cars.Add(car);
            }
            var names = new List<string>();
            foreach (Car c in cars) names.Add(c.Name + (c.Drive != null ? "" : "(?)"));
            Log.Info("voitures : " + string.Join(", ", names.ToArray()));
        }

        public static void Update()
        {
            if (!Session.Active || !PlayerSync.InGame) return;
            if (!scanned) { if (scanAt > 0 && Time.realtimeSinceStartup >= scanAt) Scan(); return; }
            float now = Time.realtimeSinceStartup;

            int driving = -1;
            foreach (Car c in cars)
                if (c.Drive != null && c.Drive.ActiveStateName == "Player in car") { driving = c.Index; break; }
            if (driving != LocalDriving)
            {
                Log.Info(driving >= 0 ? "au volant de " + cars[driving].Name : "sorti de " + cars[LocalDriving].Name);
                if (LocalDriving >= 0)
                {
                    if (EngineRunning(cars[LocalDriving])) { owned = LocalDriving; Log.Info("moteur laisse tournant : " + cars[owned].Name + " reste a nous"); }
                    else Send(cars[LocalDriving], 0);   // derniere position, sans conducteur
                }
                if (driving >= 0) owned = -1;
                LocalDriving = driving;
            }

            if (now >= nextFast)
            {
                nextFast = now + 0.05f;
                if (LocalDriving >= 0) Send(cars[LocalDriving], 1);
                else if (owned >= 0 && (++ownedTick & 1) == 0)   // 10 fois/s
                {
                    Car o = cars[owned];
                    if (o.RemoteBy >= 0 && o.RemoteBy != Session.LocalId) owned = -1;     // un autre l'a prise
                    else if (EngineRunning(o)) Send(o, 2);
                    else { Send(o, 0); Log.Info("moteur coupe : " + o.Name + " rendue"); owned = -1; }
                }
            }
            if (Session.IsHost && now >= nextSlow)
            {
                nextSlow = now + 2f;
                foreach (Car c in cars)
                    if (c.Index != LocalDriving && c.Index != owned && c.RemoteBy < 0 && c.Body != null) Send(c, 0);
            }

            foreach (Car c in cars)
            {
                bool remote = c.RemoteBy >= 0 && now - c.LastRemote < 1.5f && c.Index != LocalDriving && c.Index != owned;
                if (!remote && c.RemoteBy >= 0 && now - c.LastRemote >= 1.5f) { c.RemoteBy = -1; c.RemoteDriver = -1; }
                SetKinematic(c, remote);
                if (remote)
                {
                    if (now >= c.NextJoints) ProtectJoints(c);   // pieces montees entre-temps
                    Follow(c);
                    Animate(c);
                    if (now >= c.NextLog) { c.NextLog = now + 5f; Log.Info(c.Name + (c.RemoteDriver >= 0 ? " conduite par #" + c.RemoteDriver : " moteur tournant chez #" + c.RemoteBy) + " : " + c.Body.position.ToString("F1") + ", regime " + (c.Dt != null ? c.Dt.rpm.ToString("F0") : "?") + ", chaleur " + (c.Heat != null ? c.Heat.Value.ToString("F1") : "?") + " (recue " + c.RemoteHeat.ToString("F1") + ")" +  (Config.GetInt("Test", "JournalSons", 0) != 0 ? " | " + SoundDiag(c) : "")); }
                }
            }
        }

        // Apres la logique du jeu : la chaleur de l'habitacle de la copie est celle de chez le conducteur.
        public static void LateUpdate()
        {
            if (!scanned) return;
            float now = Time.realtimeSinceStartup;
            foreach (Car c in cars)
                if (c.Heat != null && !float.IsNaN(c.RemoteHeat) && c.RemoteBy >= 0 && now - c.LastRemote < 1.5f && c.Index != LocalDriving)
                    c.Heat.Value = c.RemoteHeat;
        }

        static void SetKinematic(Car c, bool on)
        {
            if (c.Body == null || c.Kinematic == on) return;
            c.Kinematic = on;
            if (c.Dt != null)
            {
                if (on) { c.DtWas = c.Dt.enabled; c.Dt.enabled = false; c.Dt.startEngine = false; }
                else { c.Dt.rpm = 0; c.Dt.throttle = 0; c.Dt.enabled = c.DtWas; }
            }
            if (c.Axis != null) { if (on) { c.AxisWas = c.Axis.enabled; c.Axis.enabled = false; } else c.Axis.enabled = c.AxisWas; }
            if (c.Sound != null)
            {
                if (on) { c.SoundWasOn = c.Sound.enabled; c.Sound.enabled = true; }
                else c.Sound.enabled = c.SoundWasOn;
            }
            for (int i = 0; i < c.SoundObjs.Length; i++)
            {
                if (c.SoundObjs[i] == null) continue;
                if (on) c.SoundObjsWas[i] = c.SoundObjs[i].activeSelf;
                else c.SoundObjs[i].SetActive(c.SoundObjsWas[i]);
                foreach (PlayMakerFSM f in c.SoundObjs[i].GetComponents<PlayMakerFSM>()) f.enabled = !on;
            }
            if (on) { c.WheelsWas = new bool[c.Wheels.Length]; for (int i = 0; i < c.Wheels.Length; i++) if (c.Wheels[i] != null) { c.WheelsWas[i] = c.Wheels[i].enabled; c.Wheels[i].enabled = false; } }
            else for (int i = 0; i < c.Wheels.Length; i++) if (c.Wheels[i] != null) c.Wheels[i].enabled = c.WheelsWas == null || c.WheelsWas[i];
            if (on)
            {
                c.WasKinematic = c.Body.isKinematic;
                c.Body.isKinematic = true;
                ProtectJoints(c);
            }
            else
            {
                if (c.JointsWas != null)
                {
                    foreach (KeyValuePair<Joint, Vector2> kv in c.JointsWas)
                        if (kv.Key != null) { kv.Key.breakForce = kv.Value.x; kv.Key.breakTorque = kv.Value.y; }
                    c.JointsWas = null;
                }
                c.Body.isKinematic = c.WasKinematic;
                if (!c.Body.isKinematic) { c.Body.velocity = c.Vel; c.Body.angularVelocity = c.AngVel; }
            }
        }

        // Copie conduite ailleurs : ses attaches cassables (pare-brise, portieres, pieces montees) ne cassent
        // pas ici -- la copie suit par a-coups (recalages) et le pare-brise volait en eclats en roulant. Une
        // vraie casse chez le conducteur est renvoyee par les automates de la voiture (Jobs).
        static void ProtectJoints(Car c)
        {
            if (c.JointsWas == null) c.JointsWas = new Dictionary<Joint, Vector2>();
            foreach (Joint j in c.Body.GetComponentsInChildren<Joint>(true))
            {
                if (j == null || c.JointsWas.ContainsKey(j)) continue;
                c.JointsWas[j] = new Vector2(j.breakForce, j.breakTorque);
                j.breakForce = Mathf.Infinity;
                j.breakTorque = Mathf.Infinity;
            }
            c.NextJoints = Time.realtimeSinceStartup + 2f;
        }

        static void Follow(Car c)
        {
            // Extrapole a partir du dernier etat recu, puis rattrape en douceur.
            float dt = Mathf.Min(Time.realtimeSinceStartup - c.LastRemote, 0.3f);
            Vector3 target = c.Pos + c.Vel * dt;
            Quaternion rot = c.Rot;
            if (c.AngVel.sqrMagnitude > 1e-4f)
                rot = Quaternion.AngleAxis(c.AngVel.magnitude * dt * Mathf.Rad2Deg, c.AngVel.normalized) * c.Rot;
            Transform t = c.Body.transform;
            float k = 1f - Mathf.Exp(-15f * Time.deltaTime);
            if ((target - t.position).sqrMagnitude > 25f) { t.position = target; t.rotation = rot; }
            else
            {
                c.Body.MovePosition(Vector3.Lerp(t.position, target, k));
                c.Body.MoveRotation(Quaternion.Slerp(t.rotation, rot, k));
            }
        }

        // Son du moteur et roues de la copie conduite ailleurs.
        static void Animate(Car c)
        {
            if (c.Dt != null) { c.Dt.rpm = c.Rpm; c.Dt.throttle = c.Throttle; }
            for (int i = 0; i < c.SoundObjs.Length; i++)
            {
                bool want = (c.SoundMask & (1 << i)) != 0;
                GameObject g = c.SoundObjs[i];
                if (g == null) continue;
                if (g.activeSelf != want) g.SetActive(want);
                if (!want) continue;
                AudioSource a = g.GetComponent<AudioSource>();
                a.pitch = c.SoundPitch[i];
                a.volume = c.SoundVol[i];
                if (!a.isPlaying && a.loop) a.Play();
            }
            float fwd = Vector3.Dot(c.Vel, c.Body.transform.forward);
            for (int i = 0; i < c.Wheels.Length; i++)
            {
                Wheel w = c.Wheels[i];
                if (w == null || w.model == null || w.radius <= 0f) continue;
                c.WheelRot[i] += fwd / w.radius * Time.deltaTime;
                float steer = w.maxSteeringAngle * c.Steer;
                w.model.transform.localRotation = Quaternion.Euler(0f, steer, 0f) * Quaternion.AngleAxis(57.29578f * c.WheelRot[i], Vector3.right);
            }
        }

        static string SoundDiag(Car c)
        {
            SoundController sc = c.Sound;
            if (sc == null) return "pas de SoundController";
            var sb = new System.Text.StringBuilder("sons moteur :");
            foreach (GameObject g in c.SoundObjs)
                if (g != null) { AudioSource a = g.GetComponent<AudioSource>(); sb.Append(' ').Append(g.name).Append(g.activeInHierarchy ? "/on" : "/off").Append('/').Append(a.isPlaying ? "joue" : "-").Append('/').Append(a.pitch.ToString("F2")); }
            if (sb.Length > 0) return sb.ToString();
            sb.Append("SoundController " + (sc.enabled ? "actif" : "coupe") + ", throttle=" + (sc.engineThrottle != null ? sc.engineThrottle.name : "null") + " :");
            foreach (AudioSource a in c.Body.GetComponentsInChildren<AudioSource>(true))
                sb.Append(' ').Append(a.clip != null ? a.clip.name : "-").Append('/').Append(a.isPlaying ? "joue" : "arret").Append('/').Append(a.volume.ToString("F2")).Append(a.enabled && a.gameObject.activeInHierarchy ? "" : "(off)");
            return sb.ToString();
        }

        static float EngineVolume(Car c)
        {
            SoundController sc = c.Body.GetComponent<SoundController>();
            float v = 0f;
            if (sc == null) return -1f;
            foreach (AudioSource a in c.Body.GetComponentsInChildren<AudioSource>())
                if (a.clip != null && (a.clip == sc.engineThrottle || a.clip == sc.engineNoThrottle) && a.isPlaying) v = Mathf.Max(v, a.volume);
            return v;
        }

        // Essais : regime et accelerateur envoyes a la place de ceux du moteur local (< 0 : les vrais).
        static float testRpm = -1f, testThr;

        // Essais : allume les sons moteur de la voiture locale (comme le contact quand le moteur tourne).
        // L'objet appartient-il a une voiture qu'un autre joueur conduit en ce moment ?
        public static bool RemotelyDriven(Transform t)
        {
            string root = t.root.name;
            float now = Time.realtimeSinceStartup;
            foreach (Car c in cars)
                if (c.Name == root) return c.RemoteBy >= 0 && now - c.LastRemote < 1.5f && c.Index != LocalDriving && c.Index != owned;
            return false;
        }

        public static string TestSounds(string name, bool on)
        {
            foreach (Car c in cars)
                if (c.Name == name)
                {
                    foreach (GameObject g in c.SoundObjs)
                        if (g != null && g.name.StartsWith("Sound"))
                        {
                            g.SetActive(on);
                            // Moteur pas vraiment demarre : on fige une hauteur de son pour verifier l'envoi.
                            foreach (PlayMakerFSM f in g.GetComponents<PlayMakerFSM>()) f.enabled = false;
                            g.GetComponent<AudioSource>().pitch = 0.9f;
                        }
                    return SoundDiag(c);
                }
            return "?";
        }
        public static void TestEngine(float rpm, float thr) { testRpm = rpm; testThr = thr; }

        // Essais : toutes les sources audio de la voiture (clip, joue, volume, hauteur) et son etat.
        public static string AudioState(string name)
        {
            foreach (Car c in cars)
                if (c.Name == name && c.Body != null)
                {
                    var sb = new System.Text.StringBuilder(name + (c.Kinematic ? " (copie)" : "") + " SoundController " + (c.Sound != null ? (c.Sound.enabled ? "actif" : "coupe") : "-") + ", regime " + (c.Dt != null ? c.Dt.rpm.ToString("F0") : "?") + " :");
                    foreach (AudioSource a in c.Body.GetComponentsInChildren<AudioSource>(true))
                        if (a.isPlaying && a.volume > 0.001f) sb.Append(' ').Append(a.gameObject.name).Append('/').Append(a.clip != null ? a.clip.name : "-").Append('/').Append(a.volume.ToString("F2")).Append('/').Append(a.pitch.ToString("F2"));
                    return sb.ToString();
                }
            return "?";
        }

        // Essais : champs simples du Drivetrain (nombres, booleens) de la voiture.
        public static string DtState(string name)
        {
            foreach (Car c in cars)
                if (c.Name == name && c.Dt != null)
                {
                    var sb = new System.Text.StringBuilder("drivetrain " + name + " (" + (c.Dt.enabled ? "actif" : "coupe") + ") :");
                    foreach (System.Reflection.FieldInfo fi in c.Dt.GetType().GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance))
                    {
                        object v = fi.GetValue(c.Dt);
                        if (v is bool || v is int) sb.Append(' ').Append(fi.Name).Append('=').Append(v);
                        else if (v is float) sb.Append(' ').Append(fi.Name).Append('=').Append(((float)v).ToString("F1"));
                    }
                    return sb.ToString();
                }
            return "?";
        }

        // Essais : sort le joueur local de la voiture comme la touche ENTREE.
        public static string TestExit(string name)
        {
            foreach (Car c in cars)
                if (c.Name == name && c.Drive != null)
                {
                    if (c.Drive.ActiveStateName == "Player in car") c.Drive.SendEvent("Key DOWN");
                    return c.Drive.ActiveStateName;
                }
            return "?";
        }

        static float SteerOf(Car c)
        {
            float s = 0f;
            foreach (Wheel w in c.Wheels) if (w != null && Mathf.Abs(w.steering) > Mathf.Abs(s)) s = w.steering;
            return s;
        }

        // Moteur en marche ici (regime, ou un son moteur allume par le jeu).
        static bool EngineRunning(Car c)
        {
            if (c.Body == null) return false;
            if (c.Dt != null && c.Dt.enabled && c.Dt.rpm > 150f) return true;
            foreach (GameObject g in c.SoundObjs) if (g != null && g.activeSelf) return true;
            return false;
        }

        static void Send(Car c, int mode)
        {
            if (c.Body == null) return;
            bool driven = mode != 0;
            var w = new NetWriter(Msg.Vehicle).U8(Session.LocalId).U8(c.Index).U8(mode)
                .Vec(c.Body.position).Quat(c.Body.rotation).Vec(c.Body.velocity).Vec(c.Body.angularVelocity);
            if (driven)
            {
                float rpm = testRpm >= 0f ? testRpm : c.Dt != null ? c.Dt.rpm : 0f;
                float thr = testRpm >= 0f ? testThr : c.Dt != null ? c.Dt.throttle : 0f;
                int mask = 0;
                for (int i = 0; i < c.SoundObjs.Length; i++) if (c.SoundObjs[i] != null && c.SoundObjs[i].activeSelf) mask |= 1 << i;
                w.F32(rpm).F32(thr).F32(SteerOf(c)).F32(c.Heat != null ? c.Heat.Value : float.NaN).U16(mask);
                for (int i = 0; i < c.SoundObjs.Length; i++)
                    if ((mask & (1 << i)) != 0)
                    {
                        AudioSource a = c.SoundObjs[i].GetComponent<AudioSource>();
                        w.F32(a.pitch).F32(a.volume);
                    }
            }
            Session.SendAll(w, false);
        }

        // Avatar d'un joueur qui conduit chez lui : position et rotation dans la copie locale de la
        // voiture (calculees par rapport a l'etat de la voiture envoye en meme temps : pas de tremblement).
        public static Transform RemoteCarTransform(int player)
        {
            foreach (Car c in cars) if (c.RemoteDriver == player && c.Body != null) return c.Body.transform;
            return null;
        }

        // Nom de la voiture que 'player' conduit chez lui (null : aucune).
        public static string RemoteCarName(int player)
        {
            foreach (Car c in cars) if (c.RemoteDriver == player) return c.Name;
            return null;
        }

        public static bool SeatPose(int player, Vector3 feet, out Vector3 pos, out Quaternion rot)
        {
            foreach (Car c in cars)
            {
                if (c.RemoteDriver != player || c.Body == null) continue;
                Vector3 local = Quaternion.Inverse(c.Rot) * (feet - c.Pos);
                pos = c.Body.transform.TransformPoint(local);
                rot = c.Body.transform.rotation;
                return true;
            }
            pos = feet; rot = Quaternion.identity;
            return false;
        }

        public static void OnMessage(Peer from, NetReader r)
        {
            int who = r.U8();
            if (Session.IsHost) who = from.Id;
            int idx = r.U8();
            int mode = r.U8();
            bool driven = mode != 0;
            Vector3 pos = r.Vec();
            Quaternion rot = r.Quat();
            Vector3 vel = r.Vec(), ang = r.Vec();
            float rpm = 0f, thr = 0f, steer = 0f, heat = float.NaN;
            int smask = 0;
            var spitch = new List<float>();
            if (driven)
            {
                rpm = r.F32(); thr = r.F32(); steer = r.F32(); heat = r.F32(); smask = r.U16();
                for (int i = 0; i < 16; i++) if ((smask & (1 << i)) != 0) { spitch.Add(r.F32()); spitch.Add(r.F32()); }
            }
            if (Session.IsHost)
            {
                var fw = new NetWriter(Msg.Vehicle).U8(who).U8(idx).U8(mode).Vec(pos).Quat(rot).Vec(vel).Vec(ang);
                if (driven) { fw.F32(rpm).F32(thr).F32(steer).F32(heat).U16(smask); foreach (float v in spitch) fw.F32(v); }
                Session.Broadcast(fw, false, who);
            }
            if (!scanned || idx >= cars.Count) return;
            Car c = cars[idx];
            if (c.Index == LocalDriving) return;              // je la conduis : je garde la main
            if (c.Index == owned && mode != 1) return;        // mon moteur tourne : je garde la main
            if (c.Index == owned) { owned = -1; Log.Info(c.Name + " prise par #" + who); }
            if (driven)
            {
                c.RemoteBy = who;
                c.RemoteDriver = mode == 1 ? who : -1;
                c.LastRemote = Time.realtimeSinceStartup;
                c.Pos = pos; c.Rot = rot; c.Vel = vel; c.AngVel = ang;
                c.Rpm = rpm; c.Throttle = thr; c.Steer = steer; c.SoundMask = smask; c.RemoteHeat = heat;
                for (int i = 0, k = 0; i < c.SoundObjs.Length && i < 16; i++)
                    if ((smask & (1 << i)) != 0 && k + 1 < spitch.Count) { c.SoundPitch[i] = spitch[k]; c.SoundVol[i] = spitch[k + 1]; k += 2; }
                return;
            }
            // Sans conducteur : recalage si la voiture locale a derive (> 1 m ou > 10 degres).
            if (c.RemoteBy == who) { c.RemoteBy = -1; c.RemoteDriver = -1; }
            c.Vel = vel; c.AngVel = ang;
            Transform t = c.Body.transform;
            if ((t.position - pos).sqrMagnitude > 1f || Quaternion.Angle(t.rotation, rot) > 10f)
            {
                SetKinematic(c, false);
                t.position = pos;
                t.rotation = rot;
                c.Body.velocity = vel;
                c.Body.angularVelocity = ang;
                Log.Info("voiture " + c.Name + " recalee sur " + (who == 0 ? "l'hote" : "#" + who));
            }
        }

        // Essais : 1) amene le joueur a la portiere (le jeu active les objets proches),
        // 2) le met au volant comme l'automate du jeu (etat 'Check seat').
        public static string TestEnter(string name, bool seat)
        {
            foreach (Car c in cars)
            {
                if (c.Name != name || c.Drive == null) continue;
                if (!seat)
                {
                    GameObject p = GameObject.Find("PLAYER");
                    var cc = p.GetComponent<CharacterController>();
                    cc.enabled = false;
                    p.transform.position = c.Drive.transform.position + Vector3.up * 0.5f;
                    cc.enabled = true;
                    return "a cote, automate " + (c.Drive.gameObject.activeInHierarchy ? "actif" : "inactif");
                }
                // 'Press return' attend la touche d'entree (GetButtonDown -> Key DOWN).
                if (c.Drive.ActiveStateName == "Press return") c.Drive.SendEvent("Key DOWN");
                else Game.SetState(c.Drive, "Check seat");
                return c.Drive.ActiveStateName;
            }
            return null;
        }

        public static Rigidbody Body(string name)
        {
            foreach (Car c in cars) if (c.Name == name) return c.Body;
            return null;
        }
    }
}
