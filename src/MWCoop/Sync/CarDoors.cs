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
    // Position en temps reel : tant qu'elle est ouverte, celui qui l'a ouverte envoie sa pose (par
    // rapport a la voiture) 15 fois par seconde. Chez les autres, seule la partie VISIBLE (les enfants
    // du corps physique : tole, vitre, poignee) est placee a cette pose ; le corps physique reste ou le
    // jeu le met, la voiture n'est jamais poussee. Rendue a sa place a la fermeture.
    public static class CarDoors
    {
        class Door
        {
            public string Key; public PlayMakerFSM Fsm; public string Open, Close;
            public Rigidbody Body; public HingeJoint Hinge;
            public bool IsOpen, Mine;          // ouverte ; ouverte par nous (on envoie son angle)
            public Quaternion LastRot; public bool Sent;
            public bool Showing; public Quaternion TargetRot; public Vector3 TargetPos; public float LastRemote;
            public Transform[] Kids; public Vector3[] KidPos, RelPos; public Quaternion[] KidRot, RelRot;
            public Quaternion TestOffset = Quaternion.identity;   // essais : pousse la pose envoyee
        }

        static readonly Dictionary<string, Door> byKey = new Dictionary<string, Door>();
        static readonly HashSet<PlayMakerFSM> hooked = new HashSet<PlayMakerFSM>();
        static float nextScan = -1, nextAngle, lastUnknownScan = -100;
        static readonly List<KeyValuePair<float, Peer>> snapshots = new List<KeyValuePair<float, Peer>>();
        static bool applying;

        class Hook : FsmStateAction
        {
            public Door D;
            public bool Opening;
            public override void OnEnter()
            {
                try { if (!applying && Replay.Depth == 0) Send(D, Opening); } catch (System.Exception e) { Replay.HookError(e); }
                Finish();
            }
        }

        public static bool Tracks(PlayMakerFSM f) { return hooked.Contains(f); }

        public static void OnLevelLoaded()
        {
            byKey.Clear(); hooked.Clear(); snapshots.Clear();
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
                    if (f.FsmName != "Use") continue;
                    string open = null, close = null;
                    if (f.Fsm.GetState("Open door") != null && f.Fsm.GetState("Open door 2") != null) { open = "Open door"; close = "Open door 2"; }
                    else if (f.Fsm.GetState("Open hood") != null && f.Fsm.GetState("Close hood") != null)
                    { open = "Open hood"; close = f.Fsm.GetState("Sound") != null ? "Sound" : "Close hood"; }
                    if (open == null) continue;
                    string rel = Recon.Path(f.transform).Substring(car.name.Length);
                    int k; seen.TryGetValue(rel, out k); seen[rel] = k + 1;   // compte aussi les portieres deja suivies
                    if (hooked.Contains(f) || !Replay.Claim(f, "portieres")) continue;
                    var d = new Door { Key = car.name + rel + "#" + k, Fsm = f, Open = open, Close = close };
                    d.Body = f.GetComponentInParent<Rigidbody>();
                    if (d.Body != null && d.Body.transform == car.transform) d.Body = null;
                    d.Hinge = d.Body != null ? d.Body.GetComponent<HingeJoint>() : null;
                    if (!Inject(d, open, true) || !Inject(d, close, false)) continue;
                    hooked.Add(f);
                    byKey[d.Key] = d;
                }
            }
            // Prises du chauffage moteur (cable plug) : "Heater on" = branchee sur la voiture, "Heater off"
            // = debranchee. Partout dans la scene (debranchee, elle pend au poteau de la maison) ; cle :
            // la prise de la voiture (Socket).
            foreach (PlayMakerFSM f in Object.FindObjectsOfType<PlayMakerFSM>())
            {
                if (f.FsmName != "Data" || hooked.Contains(f) || !f.gameObject.name.StartsWith("cable plug")) continue;
                if (f.Fsm.GetState("Heater on") == null || f.Fsm.GetState("Heater off") == null || !Replay.Claim(f, "portieres")) continue;
                FsmGameObject sock = f.FsmVariables.FindFsmGameObject("Socket");
                if (sock == null || sock.Value == null) continue;
                var d = new Door { Key = "prise:" + Recon.Path(sock.Value.transform), Fsm = f, Open = "Heater on", Close = "Heater off" };
                if (!Inject(d, d.Open, true) || !Inject(d, d.Close, false)) continue;
                hooked.Add(f);
                byKey[d.Key] = d;
            }
            if (byKey.Count != before) Log.Info("portieres : " + byKey.Count + " suivies (portes, coffres, hayons, prises)");
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
            if (!Session.Active || nextScan < 0) return;
            float now = Time.realtimeSinceStartup;
            if (now >= nextScan) { nextScan = now + 30f; Scan(); }
            // Arrivee d'un joueur : les portieres, capots et prises deja ouverts/branches chez l'hote.
            for (int i = snapshots.Count - 1; i >= 0; i--)
            {
                if (now < snapshots[i].Key) continue;
                Peer p = snapshots[i].Value;
                snapshots.RemoveAt(i);
                if (!p.Accepted || !Session.T.Peers.Contains(p)) continue;
                int n = 0;
                foreach (Door d in byKey.Values)
                    if (d.IsOpen && d.Fsm != null) { Session.T.SendReliable(p, new NetWriter(Msg.CarDoor).U8(Session.LocalId).Str(d.Key).U8(1).ToArray()); n++; }
                Log.Info("portieres : " + n + " ouvertes envoyees a " + p);
            }
            // Chez nous, pour un autre : la partie visible suit sa pose, chaque image.
            foreach (Door d in byKey.Values)
            {
                if (!d.Showing) continue;
                if (d.Body == null || !d.IsOpen || now - d.LastRemote > 5f) { StopFollow(d); continue; }
                Show(d);
            }
            if (now < nextAngle || Session.RemoteCount == 0) return;
            nextAngle = now + 1f / 15f;
            foreach (Door d in byKey.Values)
            {
                if (!d.Mine || !d.IsOpen || d.Body == null || d.Fsm == null) continue;
                Transform car = CarOf(d);
                Quaternion rel = Quaternion.Inverse(car.rotation) * d.Body.rotation * d.TestOffset;
                if (d.Sent && Quaternion.Angle(rel, d.LastRot) < 0.4f) continue;
                d.Sent = true;
                d.LastRot = rel;
                Session.SendAll(new NetWriter(Msg.CarDoor).U8(Session.LocalId).Str(d.Key).U8(2)
                    .Quat(rel).Vec(car.InverseTransformPoint(d.Body.position)), false);
            }
        }

        static Transform CarOf(Door d) { return d.Fsm.transform.root; }

        public static void ScheduleSnapshot(Peer p) { if (Session.IsHost) snapshots.Add(new KeyValuePair<float, Peer>(Time.realtimeSinceStartup + 20f, p)); }

        // La partie visible a deplacer : les sous-objets qui ont un rendu et AUCUN collider (en eux ou
        // dessous) -- deplacer un collider changerait la forme physique de la portiere et pousserait la voiture.
        static void CollectVisual(Transform t, List<Transform> outList)
        {
            foreach (Transform c in t)
            {
                if (c.GetComponentsInChildren<Collider>(true).Length == 0) { if (c.GetComponentsInChildren<Renderer>(true).Length > 0) outList.Add(c); }
                else CollectVisual(c, outList);
            }
        }

        // Place les enfants du corps (la partie visible) comme si le corps etait a la pose recue.
        static void Show(Door d)
        {
            Transform b = d.Body.transform, car = CarOf(d);
            if (d.Kids == null)
            {
                var l = new List<Transform>();
                CollectVisual(b, l);
                d.Kids = l.ToArray();
                d.KidPos = new Vector3[d.Kids.Length]; d.KidRot = new Quaternion[d.Kids.Length];      // pose locale (a rendre)
                d.RelPos = new Vector3[d.Kids.Length]; d.RelRot = new Quaternion[d.Kids.Length];      // pose dans le repere du corps
                for (int i = 0; i < d.Kids.Length; i++)
                {
                    d.KidPos[i] = d.Kids[i].localPosition; d.KidRot[i] = d.Kids[i].localRotation;
                    d.RelPos[i] = b.InverseTransformPoint(d.Kids[i].position);
                    d.RelRot[i] = Quaternion.Inverse(b.rotation) * d.Kids[i].rotation;
                }
            }
            Quaternion R = car.rotation * d.TargetRot;
            Vector3 P = car.TransformPoint(d.TargetPos);
            Vector3 sc = b.lossyScale;
            for (int i = 0; i < d.Kids.Length; i++)
            {
                if (d.Kids[i] == null) continue;
                d.Kids[i].position = P + R * Vector3.Scale(sc, d.RelPos[i]);
                d.Kids[i].rotation = R * d.RelRot[i];
            }
        }

        // Fin du suivi : la partie visible reprend sa place sur le corps.
        static void StopFollow(Door d)
        {
            if (d.Kids != null)
                for (int i = 0; i < d.Kids.Length; i++)
                    if (d.Kids[i] != null) { d.Kids[i].localPosition = d.KidPos[i]; d.Kids[i].localRotation = d.KidRot[i]; }
            d.Showing = false;
        }

        static void Send(Door d, bool opening)
        {
            d.IsOpen = opening;
            d.Mine = opening;          // celui qui ouvre envoie l'angle ensuite
            d.Sent = false;
            StopFollow(d);
            if (!Session.Active) return;
            Log.Info("portiere " + d.Key + (opening ? " ouverte" : " fermee") + " ici");
            Session.SendAll(new NetWriter(Msg.CarDoor).U8(Session.LocalId).Str(d.Key).U8(opening ? 1 : 0), true);
        }

        public static void OnMessage(Peer from, NetReader r)
        {
            int who = r.U8();
            if (Session.IsHost) who = from.Id;
            string key = r.Str();
            int kind = r.U8();
            Quaternion rot = Quaternion.identity;
            Vector3 pos = Vector3.zero;
            if (kind == 2) { rot = r.Quat(); pos = r.Vec(); }
            if (Session.IsHost)
            {
                var w = new NetWriter(Msg.CarDoor).U8(who).Str(key).U8(kind);
                if (kind == 2) w.Quat(rot).Vec(pos);
                Session.Broadcast(w, kind != 2, who);
            }
            Door d;
            if (!byKey.TryGetValue(key, out d) || d.Fsm == null)
            {
                if (kind == 2) return;
                if (Time.realtimeSinceStartup - lastUnknownScan < 10f) return;   // pas un releve complet a chaque message
                lastUnknownScan = Time.realtimeSinceStartup;
                Scan();
                if (!byKey.TryGetValue(key, out d) || d.Fsm == null) { Log.Warn("portiere " + key + " introuvable ici"); return; }
            }
            if (kind == 2)
            {
                if (!d.IsOpen || d.Mine || d.Body == null) return;
                d.TargetRot = rot; d.TargetPos = pos; d.LastRemote = Time.realtimeSinceStartup; d.Showing = true;
                return;
            }
            bool opening = kind == 1;
            d.IsOpen = opening;
            d.Mine = false;
            StopFollow(d);
            applying = true; Replay.Depth++;
            try { Game.SetState(d.Fsm, opening ? d.Open : d.Close); }
            finally { applying = false; Replay.Depth--; }
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

        // Essais : pousse la portiere ouverte de 'car' de 'deg' degres (ressort local, comme une main).
        public static string TestPush(string car, float deg)
        {
            foreach (Door d in byKey.Values)
            {
                if (d.Body == null || !d.Key.StartsWith(car)) continue;
                d.TestOffset = Quaternion.AngleAxis(deg, Vector3.up);
                return d.Key + " poussee de " + deg + " deg (pose envoyee)";
            }
            return "rien a pousser";
        }

        public static string State(string key)
        {
            foreach (Door d in byKey.Values)
                if (d.Key.StartsWith(key) && d.Fsm != null)
                {
                    return d.Key + " etat " + d.Fsm.ActiveStateName
                           + (d.Body != null ? ", visible " + Quaternion.Angle(Quaternion.identity, Quaternion.Inverse(CarOf(d).rotation) * (d.Kids != null && d.Kids.Length > 0 && d.Kids[0] != null && d.RelRot != null ? d.Kids[0].rotation * Quaternion.Inverse(d.RelRot[0]) : d.Body.rotation)).ToString("F1") + " deg" : "")
                           + (d.IsOpen ? (d.Mine ? ", ouverte par nous" : ", ouverte par un autre") : ", fermee");
                }
            return "?";
        }
    }
}
