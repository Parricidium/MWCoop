using System.Collections.Generic;
using MWCoop.Net;
using UnityEngine;

namespace MWCoop
{
    // Portieres, capots, coffres et hayons des vehicules (corps physiques sur charniere). Leur angle
    // par rapport a la voiture est suivi 15 fois par seconde : celui qui la bouge (a moins de 5 m ou
    // au volant de cette voiture) envoie sa pose relative tant qu'elle bouge, puis sa pose de repos.
    // Chez les autres, la portiere devient cinematique et suit, puis reste figee au meme angle (comme
    // une portiere fermee du jeu) jusqu'a ce qu'un joueur sur place l'actionne a son tour.
    public static class CarDoors
    {
        class Door
        {
            public string Key;
            public Rigidbody Body;
            public Transform Car;
            public Quaternion LastRot;
            public float StillSince, NextSend;
            public bool Moving;              // local : en train d'envoyer
            public int RemoteBy = -1;
            public float LastRemote;
            public Quaternion TargetRot;
            public Vector3 TargetPos;
            public bool WasKinematic;
        }

        static readonly Dictionary<string, Door> byKey = new Dictionary<string, Door>();
        static readonly List<Door> doors = new List<Door>();
        static float nextScan = -1, nextPoll;
        static Transform player;

        public static void OnLevelLoaded()
        {
            byKey.Clear(); doors.Clear(); player = null;
            nextScan = PlayerSync.InGame ? Time.realtimeSinceStartup + 12f : -1;
        }

        static bool IsDoorName(string n)
        {
            n = n.ToLowerInvariant();
            if (n.Contains("headpivot") || n.Contains("driver") || n.Contains("passenger")) return false;
            return n.Contains("door") || n.Contains("hatch") || n.Contains("boot") || n.Contains("hood") || n.Contains("lid") || n.Contains("tailgate");
        }

        static void Scan()
        {
            int before = doors.Count;
            doors.RemoveAll(d => d.Body == null);
            foreach (Rigidbody car in Object.FindObjectsOfType<Rigidbody>())
            {
                if (car.transform.parent != null || car.GetComponent("CarDynamics") == null) continue;
                var seen = new Dictionary<string, int>();
                foreach (Rigidbody rb in car.GetComponentsInChildren<Rigidbody>(true))
                {
                    if (rb == car || rb.GetComponent<Joint>() == null || !IsDoorName(rb.name)) continue;
                    string rel = Recon.Path(rb.transform).Substring(car.name.Length);
                    int k; seen.TryGetValue(rel, out k); seen[rel] = k + 1;
                    string key = car.name + rel + "#" + k;
                    Door d;
                    if (byKey.TryGetValue(key, out d) && d.Body == rb) continue;
                    d = new Door { Key = key, Body = rb, Car = car.transform, LastRot = Rel(car.transform, rb.transform) };
                    byKey[key] = d;
                    doors.Add(d);
                }
            }
            if (doors.Count != before) Log.Info("portieres : " + doors.Count + " suivies (portes, capots, coffres)");
        }

        static Quaternion Rel(Transform car, Transform t) { return Quaternion.Inverse(car.rotation) * t.rotation; }

        public static void Update()
        {
            if (!Session.Active || nextScan < 0) return;
            float now = Time.realtimeSinceStartup;
            if (now >= nextScan) { nextScan = now + 30f; Scan(); }
            if (player == null) { GameObject p = GameObject.Find("PLAYER"); if (p == null) return; player = p.transform; }

            // Portieres suivies chez nous pour un autre joueur.
            foreach (Door d in doors)
            {
                if (d.RemoteBy < 0 || d.Body == null) continue;
                Transform t = d.Body.transform;
                Quaternion want = d.Car.rotation * d.TargetRot;
                Vector3 wantPos = d.Car.TransformPoint(d.TargetPos);
                float k = 1f - Mathf.Exp(-18f * Time.deltaTime);
                t.rotation = Quaternion.Slerp(t.rotation, want, k);
                t.position = Vector3.Lerp(t.position, wantPos, k);
                d.LastRot = Rel(d.Car, t);
                if (now - d.LastRemote > 3f) d.RemoteBy = -1;   // plus de nouvelles : reste ou elle est
            }

            if (now < nextPoll || Session.RemoteCount == 0) return;
            nextPoll = now + 1f / 15f;
            foreach (Door d in doors)
            {
                if (d.Body == null || d.RemoteBy >= 0) continue;
                Quaternion r = Rel(d.Car, d.Body.transform);
                float moved = Quaternion.Angle(r, d.LastRot);
                bool mine = (d.Body.position - player.position).sqrMagnitude < 25f || VehicleSync.LocalDrivingName == d.Car.name;
                if (moved > (d.Moving ? 0.5f : 3f) && mine && !VehicleSync.RemotelyDriven(d.Car))
                {
                    d.Moving = true;
                    d.StillSince = now;
                    d.LastRot = r;
                    Send(d, 1);
                }
                else if (d.Moving && now - d.StillSince > 0.8f)
                {
                    d.Moving = false;
                    d.LastRot = r;
                    Send(d, 0);
                }
                else if (!d.Moving) d.LastRot = r;   // derive lointaine (physique locale) : pas de message
            }
        }

        static void Send(Door d, int state)
        {
            Transform t = d.Body.transform;
            Session.SendAll(new NetWriter(Msg.CarDoor).U8(Session.LocalId).Str(d.Key).U8(state)
                .Quat(Rel(d.Car, t)).Vec(d.Car.InverseTransformPoint(t.position)), state == 0);
        }


        public static void OnMessage(Peer from, NetReader r)
        {
            int who = r.U8();
            if (Session.IsHost) who = from.Id;
            string key = r.Str();
            int state = r.U8();
            Quaternion rot = r.Quat();
            Vector3 pos = r.Vec();
            if (Session.IsHost)
                Session.Broadcast(new NetWriter(Msg.CarDoor).U8(who).Str(key).U8(state).Quat(rot).Vec(pos), state == 0, who);
            Door d;
            if (!byKey.TryGetValue(key, out d) || d.Body == null) { Scan(); if (!byKey.TryGetValue(key, out d) || d.Body == null) return; }
            if (d.RemoteBy < 0)
            {
                d.WasKinematic = d.Body.isKinematic;
                d.Body.isKinematic = true;
                Log.Info("portiere " + key + " bougee par #" + who);
            }
            d.RemoteBy = who;
            d.LastRemote = Time.realtimeSinceStartup;
            d.TargetRot = rot;
            d.TargetPos = pos;
            d.Moving = false;
            if (state == 0)
            {
                // Repos : pose exacte, figee (cinematique) ; nos propres mouvements repartiront d'ici.
                d.Body.transform.rotation = d.Car.rotation * rot;
                d.Body.transform.position = d.Car.TransformPoint(pos);
                d.LastRot = rot;
                d.RemoteBy = -1;
            }
        }

        // Essais : ouvre la premiere portiere de 'car' de 'deg' degres autour de sa charniere, sans physique.
        public static string TestOpen(string car, float deg)
        {
            Scan();
            foreach (Door d in doors)
            {
                if (d.Body == null || !d.Key.StartsWith(car)) continue;
                HingeJoint h = d.Body.GetComponent<HingeJoint>();
                Vector3 axis = h != null ? d.Body.transform.TransformDirection(h.axis) : d.Car.up;
                Vector3 pivot = h != null ? d.Body.transform.TransformPoint(h.anchor) : d.Body.position;
                d.Body.isKinematic = true;
                d.Body.transform.RotateAround(pivot, axis, deg);
                return d.Key + " ouverte de " + deg + " deg";
            }
            return "aucune portiere sur " + car + " (" + doors.Count + ")";
        }

        public static string State(string key)
        {
            foreach (Door d in doors)
                if (d.Key.StartsWith(key) && d.Body != null) return d.Key + " a " + Quaternion.Angle(Rel(d.Car, d.Body.transform), Quaternion.identity).ToString("F1") + " deg";
            return "?";
        }
    }
}
