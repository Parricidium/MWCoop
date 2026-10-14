using System.Collections.Generic;
using MWCoop.Net;
using UnityEngine;

namespace MWCoop
{
    // Particules du monde chez les invites (retour de JD, 14/10 : « fumee du sauna, trainee de neige, fumee, terre / neige
    // soulevee par les vehicules PNJ ne sont pas visibles chez le client »). Les emetteurs (ParticleEmitter, legacy) sont
    // allumes par des automates : la poussiere (RoosterTail) et la trainee de neige (SnowCloudTrail*) du trafic par
    // Throttle / Steering / Shifting de la voiture, coupes chez l'invite (copie cinematique, Traffic.cs) ; la fumee du sauna
    // (SmokeHomeKiuas) par Simulation::Burn, active seulement chez celui dont le feu brule. Releve chez l'invite (essai
    // Autotest=particules) : rien n'emettait, chez l'hote RoosterTail, SnowCloudTrail, Exhaust du bus.
    // Ici : l'hote dit quels emetteurs sont allumes (@emit : a chaque changement, et tous toutes les 5 s) ; l'invite les pose.
    // Vehicules du trafic, PNJ et courses (copies) : repose a chaque seconde (un automate local, LOD, peut les eteindre) ;
    // le reste (poeles, cheminees) : seulement aux changements de l'hote. A part : voitures des joueurs (leur conducteur
    // fait foi, VehicleSync), le joueur, douches et robinets, vapeur de l'eau jetee sur le poele, traces de pneus.
    public static class Particles
    {
        class Em { public string Key; public ParticleEmitter E; public bool Last, Want, Has, Copy; }
        static readonly Dictionary<string, Em> byKey = new Dictionary<string, Em>();
        static float nextScan, nextSend, nextFull, nextApply;
        static int sentLogs, gotLogs;

        public static void OnLevelLoaded() { byKey.Clear(); nextScan = 0f; nextFull = 0f; }

        static bool Eligible(ParticleEmitter pe)
        {
            string n = pe.name;
            if (n.Contains("Shower") || n.Contains("Tap") || n.StartsWith("Steam") || n.Contains("Skid")) return false;
            Transform t = pe.transform;
            string root = t.root.name;
            if (root == "PLAYER" || root.StartsWith("MWCoop")) return false;
            if (VehicleSync.IsPlayerCar(t)) return false;   // (voiture d'un joueur : son conducteur)
            return true;
        }
        static bool IsCopy(Transform t) { string r = t.root.name; return r == "TRAFFIC" || r == "NPC_CARS" || r == "RACES" || r == "TRAIN"; }

        static void Scan()
        {
            foreach (ParticleEmitter pe in Object.FindObjectsOfType<ParticleEmitter>())
            {
                if (pe == null || !Eligible(pe)) continue;
                string k = Recon.Path(pe.transform) + "#" + pe.transform.GetSiblingIndex();
                Em e;
                if (!byKey.TryGetValue(k, out e)) { e = new Em { Key = k }; byKey[k] = e; e.Last = pe.emit; }
                e.E = pe; e.Copy = IsCopy(pe.transform);
            }
        }

        public static void Update()
        {
            if (!Session.Active || !PlayerSync.InGame) return;
            float now = Time.realtimeSinceStartup;
            if (now >= nextScan) { nextScan = now + 5f; Scan(); }
            if (Session.IsHost)
            {
                if (Session.RemoteCount == 0 || now < nextSend) return;
                nextSend = now + 0.2f;
                bool full = now >= nextFull;
                if (full) nextFull = now + 5f;
                var batch = new List<Em>();
                foreach (Em e in byKey.Values)
                {
                    if (e.E == null) continue;
                    bool v = e.E.emit;
                    if (full || v != e.Last) batch.Add(e);
                    e.Last = v;
                }
                for (int i = 0; i < batch.Count; i += 16)
                {
                    int n = Mathf.Min(16, batch.Count - i);
                    var w = new NetWriter(Msg.Job).U8(Session.LocalId).Str("@emit").U8(n);
                    for (int k = 0; k < n; k++) w.Str(batch[i + k].Key).Bool(batch[i + k].Last);
                    Session.SendAll(w, !full);
                }
                if (!full && batch.Count > 0 && sentLogs++ < 10) Log.Info("particules : " + batch.Count + " emetteur(s) change(s) envoye(s) (" + batch[0].Key + " " + batch[0].Last + ")");
                return;
            }
            if (now < nextApply) return;   // invite : copies reposees chaque seconde
            nextApply = now + 1f;
            foreach (Em e in byKey.Values) if (e.Has && e.Copy && e.E != null && e.E.emit != e.Want) e.E.emit = e.Want;
        }

        public static void OnMessage(int who, NetReader r)
        {
            int n = r.U8();
            for (int i = 0; i < n; i++)
            {
                string k = r.Str(); bool v = r.Bool();
                if (Session.IsHost || who != 0) continue;
                Em e;
                if (!byKey.TryGetValue(k, out e)) { e = new Em { Key = k }; byKey[k] = e; }
                bool changed = !e.Has || e.Want != v;
                e.Want = v; e.Has = true;
                if (changed && e.E != null && e.E.emit != v)
                {
                    e.E.emit = v;
                    if (gotLogs++ < 10) Log.Info("particules : " + k + " " + (v ? "allume" : "eteint") + " (hote)");
                }
            }
        }
    }
}
