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
                var car = new Car { Index = cars.Count, Name = go.name, Body = go.GetComponent<Rigidbody>() };
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
                    if (now >= c.NextLog) { c.NextLog = now + 5f; Log.Info(c.Name + " conduite par #" + c.RemoteDriver + " : " + c.Body.position.ToString("F1")); }
                }
            }
        }

        static void SetKinematic(Car c, bool on)
        {
            if (c.Body == null || c.Kinematic == on) return;
            c.Kinematic = on;
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

        static void Send(Car c, bool driven)
        {
            if (c.Body == null) return;
            var w = new NetWriter(Msg.Vehicle).U8(Session.LocalId).U8(c.Index).Bool(driven)
                .Vec(c.Body.position).Quat(c.Body.rotation).Vec(c.Body.velocity).Vec(c.Body.angularVelocity);
            Session.SendAll(w, false);
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
            if (Session.IsHost)
                Session.Broadcast(new NetWriter(Msg.Vehicle).U8(who).U8(idx).Bool(driven).Vec(pos).Quat(rot).Vec(vel).Vec(ang), false, who);
            if (!scanned || idx >= cars.Count) return;
            Car c = cars[idx];
            if (c.Index == LocalDriving) return;              // je la conduis : je garde la main
            if (driven)
            {
                c.RemoteDriver = who;
                c.LastRemote = Time.realtimeSinceStartup;
                c.Pos = pos; c.Rot = rot; c.Vel = vel; c.AngVel = ang;
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
