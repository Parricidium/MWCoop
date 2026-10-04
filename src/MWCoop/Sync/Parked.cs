using System.Collections.Generic;
using MWCoop.Net;
using UnityEngine;

namespace MWCoop
{
    // Voitures garees du decor (station-service : l'automate ParkedCars tire au sort des voitures de
    // StaticCars et les pose sur des places quand un joueur approche) : chaque jeu tirait les siennes.
    // L'hote seul les tire ; chez les invites l'automate est arrete, et la place de chaque voiture
    // (parent, pose, visible) est recopiee depuis l'hote (envoyee quand elle change, et toutes les 5 s).
    public static class Parked
    {
        class Car { public string Name; public Transform T; public string Parent; public Vector3 Pos; public Quaternion Rot; public bool Active; }
        static readonly List<Car> cars = new List<Car>();
        static readonly List<PlayMakerFSM> placers = new List<PlayMakerFSM>();
        static float nextScan = -1, nextSend, nextFull;

        public static void OnLevelLoaded()
        {
            cars.Clear(); placers.Clear();
            nextScan = PlayerSync.InGame ? Time.realtimeSinceStartup + 16f : -1;
        }

        // Une seule station : ses deux objets sont cherches par leur chemin (pas de releve de toute la scene).
        static void Scan()
        {
            GameObject pc = Game.FindAny("PERAPORTTI/ParkedCars");
            PlayMakerFSM f = pc != null ? Game.FsmOn(pc, "ParkedCars") : null;
            if (f != null && !placers.Contains(f)) placers.Add(f);
            GameObject sc = Game.FindAny("PERAPORTTI/Building/LOD300/StaticCars");
            if (sc == null) return;
            foreach (Transform c in sc.transform)
            {
                bool known = false;
                foreach (Car k in cars) if (k.T == c) known = true;
                if (!known) cars.Add(new Car { Name = c.name, T = c });
            }
        }

        static Car Find(string name)
        {
            foreach (Car c in cars) if (c.Name == name && c.T != null) return c;
            return null;
        }

        public static void Update()
        {
            if (!Session.Active || nextScan < 0 || !PlayerSync.InGame) return;
            float now = Time.realtimeSinceStartup;
            if (now >= nextScan) { nextScan = cars.Count == 0 || placers.Count == 0 ? now + 30f : float.MaxValue; Scan(); }
            if (!Session.IsHost)
            {
                foreach (PlayMakerFSM f in placers) if (f != null && f.enabled) { f.enabled = false; Log.Info("voitures garees : tirage de l'hote (" + Recon.Path(f.transform) + ")"); }
                return;
            }
            if (Session.RemoteCount == 0 || now < nextSend) return;
            nextSend = now + 1f;
            bool full = now >= nextFull;
            if (full) nextFull = now + 5f;
            NetWriter w = null;
            foreach (Car c in cars)
            {
                if (c.T == null) continue;
                string parent = c.T.parent != null ? Recon.Path(c.T.parent) : "";
                bool act = c.T.gameObject.activeSelf;
                if (!full && parent == c.Parent && act == c.Active && (c.T.localPosition - c.Pos).sqrMagnitude < 1e-4f && Quaternion.Angle(c.T.localRotation, c.Rot) < 0.5f) continue;
                c.Parent = parent; c.Active = act; c.Pos = c.T.localPosition; c.Rot = c.T.localRotation;
                if (w != null && w.Length > 800) { Session.SendAll(w, true); w = null; }
                if (w == null) w = new NetWriter(Msg.Parked);
                w.Str(c.Name).Str(parent).Vec(c.Pos).Quat(c.Rot).Bool(act);
            }
            if (w != null) Session.SendAll(w, true);
        }

        public static void OnMessage(Peer from, NetReader r)
        {
            if (Session.IsHost) return;
            int moved = 0;
            while (r.More)
            {
                string name = r.Str(), parent = r.Str();
                Vector3 p = r.Vec(); Quaternion q = r.Quat(); bool act = r.Bool();
                Car c = Find(name);
                if (c == null) continue;
                Transform par = parent.Length > 0 ? (Game.FindAny(parent) != null ? Game.FindAny(parent).transform : null) : null;
                if (par != null && c.T.parent != par) { c.T.parent = par; moved++; }
                c.T.localPosition = p; c.T.localRotation = q;
                if (c.T.gameObject.activeSelf != act) c.T.gameObject.SetActive(act);
            }
            if (moved > 0) Log.Info("voitures garees : " + moved + " placees comme chez l'hote");
        }

        public static string State()
        {
            var sb = new System.Text.StringBuilder();
            foreach (Car c in cars) if (c.T != null && c.T.parent != null && c.T.parent.name != "StaticCars") sb.Append(c.Name).Append('@').Append(c.T.parent.name).Append(' ');
            return sb.Length > 0 ? sb.ToString() : "aucune garee";
        }
    }
}
