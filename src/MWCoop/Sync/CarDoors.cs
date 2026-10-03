using System.Collections.Generic;
using HutongGames.PlayMaker;
using MWCoop.Net;
using UnityEngine;

namespace MWCoop
{
    // Portieres, coffres, hayons et capots des vehicules. Leur automate 'Use' ouvre et ferme :
    //  - portieres : "Open door" (ouvrir), "Open door 2" -> Sound -> "Close door" (fermer) ;
    //  - hayons, capots : "Open hood" (ouvrir), "Sound" -> "Close hood" (fermer).
    // Une action ajoutee en tete de ces etats previent les autres, qui menent la meme portiere au
    // meme etat : le jeu l'ouvre ou la ferme lui-meme, animation et son compris. Rien n'est deplace
    // de force (une portiere teleportee pousse la voiture, qui s'envole).
    public static class CarDoors
    {
        class Door { public string Key; public PlayMakerFSM Fsm; public string Open, Close; }

        static readonly Dictionary<string, Door> byKey = new Dictionary<string, Door>();
        static readonly HashSet<PlayMakerFSM> hooked = new HashSet<PlayMakerFSM>();
        static float nextScan = -1;
        static bool applying;

        class Hook : FsmStateAction
        {
            public Door D;
            public bool Opening;
            public override void OnEnter()
            {
                if (!applying) Send(D, Opening);
                Finish();
            }
        }

        public static void OnLevelLoaded()
        {
            byKey.Clear(); hooked.Clear();
            nextScan = PlayerSync.InGame ? Time.realtimeSinceStartup + 12f : -1;
        }

        static void Scan()
        {
            int before = byKey.Count;
            foreach (Rigidbody car in Object.FindObjectsOfType<Rigidbody>())
            {
                if (car.transform.parent != null || car.GetComponent("CarDynamics") == null) continue;
                var seen = new Dictionary<string, int>();
                foreach (PlayMakerFSM f in car.GetComponentsInChildren<PlayMakerFSM>(true))
                {
                    if (f.FsmName != "Use" || hooked.Contains(f)) continue;
                    string open = null, close = null;
                    if (f.Fsm.GetState("Open door") != null && f.Fsm.GetState("Open door 2") != null) { open = "Open door"; close = "Open door 2"; }
                    else if (f.Fsm.GetState("Open hood") != null && f.Fsm.GetState("Close hood") != null)
                    { open = "Open hood"; close = f.Fsm.GetState("Sound") != null ? "Sound" : "Close hood"; }
                    if (open == null) continue;
                    string rel = Recon.Path(f.transform).Substring(car.name.Length);
                    int k; seen.TryGetValue(rel, out k); seen[rel] = k + 1;
                    var d = new Door { Key = car.name + rel + "#" + k, Fsm = f, Open = open, Close = close };
                    if (!Inject(d, open, true) || !Inject(d, close, false)) continue;
                    hooked.Add(f);
                    byKey[d.Key] = d;
                }
            }
            if (byKey.Count != before) Log.Info("portieres : " + byKey.Count + " suivies (portes, coffres, hayons)");
        }

        static bool Inject(Door d, string state, bool opening)
        {
            FsmState s = d.Fsm.Fsm.GetState(state);
            if (s == null) return false;
            try
            {
                var list = new List<FsmStateAction>(s.Actions);
                list.Insert(0, new Hook { D = d, Opening = opening });
                s.Actions = list.ToArray();
                return true;
            }
            catch { return false; }
        }

        public static void Update()
        {
            if (!Session.Active || nextScan < 0 || Time.realtimeSinceStartup < nextScan) return;
            nextScan = Time.realtimeSinceStartup + 30f;
            Scan();
        }

        static void Send(Door d, bool opening)
        {
            if (!Session.Active) return;
            Log.Info("portiere " + d.Key + (opening ? " ouverte" : " fermee") + " ici");
            Session.SendAll(new NetWriter(Msg.CarDoor).U8(Session.LocalId).Str(d.Key).Bool(opening), true);
        }

        public static void OnMessage(Peer from, NetReader r)
        {
            int who = r.U8();
            if (Session.IsHost) who = from.Id;
            string key = r.Str();
            bool opening = r.Bool();
            if (Session.IsHost) Session.Broadcast(new NetWriter(Msg.CarDoor).U8(who).Str(key).Bool(opening), true, who);
            Door d;
            if (!byKey.TryGetValue(key, out d) || d.Fsm == null) { Scan(); if (!byKey.TryGetValue(key, out d) || d.Fsm == null) { Log.Warn("portiere " + key + " introuvable ici"); return; } }
            applying = true;
            try { Game.SetState(d.Fsm, opening ? d.Open : d.Close); }
            finally { applying = false; }
            Log.Info("portiere " + key + (opening ? " ouverte" : " fermee") + " par #" + who);
        }

        // Essais : ouvre (ou ferme) la premiere portiere de 'car' comme un clic du joueur.
        public static string TestOpen(string car, bool open)
        {
            Scan();
            foreach (Door d in byKey.Values)
            {
                if (d.Fsm == null || !d.Key.StartsWith(car)) continue;
                Game.SetState(d.Fsm, open ? d.Open : d.Close);
                return d.Key + (open ? " -> " + d.Open : " -> " + d.Close);
            }
            return "aucune portiere sur " + car + " (" + byKey.Count + ")";
        }

        public static string State(string key)
        {
            foreach (Door d in byKey.Values)
                if (d.Key.StartsWith(key) && d.Fsm != null)
                {
                    Rigidbody rb = d.Fsm.GetComponentInParent<Rigidbody>();
                    return d.Key + " etat " + d.Fsm.ActiveStateName + (rb != null ? ", corps " + rb.name + " rot " + rb.transform.localEulerAngles.ToString("F0") : "");
                }
            return "?";
        }
    }
}
