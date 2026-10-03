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
    public static class VehicleSync
    {
        class Car
        {
            public int Index;
            public string Name;
            public Rigidbody Body;
            public PlayMakerFSM Drive;
            public int RemoteDriver = -1;       // joueur qui la conduit chez lui (-1 : personne)
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
            public float[] WheelRot;
        }

        static readonly List<Car> cars = new List<Car>();
        static bool scanned;
        static float scanAt = -1, nextFast, nextSlow;
        public static int LocalDriving = -1;    // index de la voiture conduite localement

        public static void OnLevelLoaded()
        {
            cars.Clear();
            scanned = false;
            LocalDriving = -1;
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
                if (LocalDriving >= 0) Send(cars[LocalDriving], false);   // derniere position, sans conducteur
                LocalDriving = driving;
            }

            if (now >= nextFast)
            {
                nextFast = now + 0.05f;
                if (LocalDriving >= 0) Send(cars[LocalDriving], true);
            }
            if (Session.IsHost && now >= nextSlow)
            {
                nextSlow = now + 2f;
                foreach (Car c in cars)
                    if (c.Index != LocalDriving && c.RemoteDriver < 0 && c.Body != null) Send(c, false);
            }

            foreach (Car c in cars)
            {
                bool remote = c.RemoteDriver >= 0 && now - c.LastRemote < 1.5f && c.Index != LocalDriving;
                if (!remote && c.RemoteDriver >= 0 && now - c.LastRemote >= 1.5f) c.RemoteDriver = -1;
                SetKinematic(c, remote);
                if (remote)
                {
                    Follow(c);
                    Animate(c);
                    if (now >= c.NextLog) { c.NextLog = now + 5f; Log.Info(c.Name + " conduite par #" + c.RemoteDriver + " : " + c.Body.position.ToString("F1") + ", regime " + (c.Dt != null ? c.Dt.rpm.ToString("F0") : "?") +  (Config.GetInt("Test", "JournalSons", 0) != 0 ? " | " + SoundDiag(c) : "")); }
                }
            }
        }

        static void SetKinematic(Car c, bool on)
        {
            if (c.Body == null || c.Kinematic == on) return;
            c.Kinematic = on;
            if (c.Dt != null) { c.Dt.enabled = !on; if (on) c.Dt.startEngine = false; else { c.Dt.rpm = 0; c.Dt.throttle = 0; } }
            if (c.Axis != null) c.Axis.enabled = !on;
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
            foreach (Wheel w in c.Wheels) if (w != null) w.enabled = !on;
            if (on)
            {
                c.WasKinematic = c.Body.isKinematic;
                c.Body.isKinematic = true;
            }
            else
            {
                c.Body.isKinematic = c.WasKinematic;
                if (!c.Body.isKinematic) { c.Body.velocity = c.Vel; c.Body.angularVelocity = c.AngVel; }
            }
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
                if (c.Name == root) return c.RemoteDriver >= 0 && now - c.LastRemote < 1.5f && c.Index != LocalDriving;
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

        static float SteerOf(Car c)
        {
            float s = 0f;
            foreach (Wheel w in c.Wheels) if (w != null && Mathf.Abs(w.steering) > Mathf.Abs(s)) s = w.steering;
            return s;
        }

        static void Send(Car c, bool driven)
        {
            if (c.Body == null) return;
            var w = new NetWriter(Msg.Vehicle).U8(Session.LocalId).U8(c.Index).Bool(driven)
                .Vec(c.Body.position).Quat(c.Body.rotation).Vec(c.Body.velocity).Vec(c.Body.angularVelocity);
            if (driven)
            {
                float rpm = testRpm >= 0f ? testRpm : c.Dt != null ? c.Dt.rpm : 0f;
                float thr = testRpm >= 0f ? testThr : c.Dt != null ? c.Dt.throttle : 0f;
                int mask = 0;
                for (int i = 0; i < c.SoundObjs.Length; i++) if (c.SoundObjs[i] != null && c.SoundObjs[i].activeSelf) mask |= 1 << i;
                w.F32(rpm).F32(thr).F32(SteerOf(c)).U16(mask);
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
            bool driven = r.Bool();
            Vector3 pos = r.Vec();
            Quaternion rot = r.Quat();
            Vector3 vel = r.Vec(), ang = r.Vec();
            float rpm = 0f, thr = 0f, steer = 0f;
            int smask = 0;
            var spitch = new List<float>();
            if (driven)
            {
                rpm = r.F32(); thr = r.F32(); steer = r.F32(); smask = r.U16();
                for (int i = 0; i < 16; i++) if ((smask & (1 << i)) != 0) { spitch.Add(r.F32()); spitch.Add(r.F32()); }
            }
            if (Session.IsHost)
            {
                var fw = new NetWriter(Msg.Vehicle).U8(who).U8(idx).Bool(driven).Vec(pos).Quat(rot).Vec(vel).Vec(ang);
                if (driven) { fw.F32(rpm).F32(thr).F32(steer).U16(smask); foreach (float v in spitch) fw.F32(v); }
                Session.Broadcast(fw, false, who);
            }
            if (!scanned || idx >= cars.Count) return;
            Car c = cars[idx];
            if (c.Index == LocalDriving) return;              // je la conduis : je garde la main
            if (driven)
            {
                c.RemoteDriver = who;
                c.LastRemote = Time.realtimeSinceStartup;
                c.Pos = pos; c.Rot = rot; c.Vel = vel; c.AngVel = ang;
                c.Rpm = rpm; c.Throttle = thr; c.Steer = steer; c.SoundMask = smask;
                for (int i = 0, k = 0; i < c.SoundObjs.Length && i < 16; i++)
                    if ((smask & (1 << i)) != 0 && k + 1 < spitch.Count) { c.SoundPitch[i] = spitch[k]; c.SoundVol[i] = spitch[k + 1]; k += 2; }
                return;
            }
            // Sans conducteur : recalage si la voiture locale a derive (> 1 m ou > 10 degres).
            if (c.RemoteDriver == who) c.RemoteDriver = -1;
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
