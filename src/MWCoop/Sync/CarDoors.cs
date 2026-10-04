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
    // Position en temps reel : tant qu'elle est ouverte, celui qui l'a ouverte envoie son ANGLE autour de
    // sa charniere (HingeJoint) quand il change, et au moins chaque seconde. Chez les autres, le corps de
    // la portiere devient cinematique et prend cet angle autour de SA charniere (pose de fermeture notee
    // portiere fermee) : la pose reste toujours une pose de la charniere, la voiture n'est jamais tiree.
    // Elle bat comme chez celui qui l'a ouverte, avec ses butees. Rendue a la physique a la fermeture.
    public static class CarDoors
    {
        class Door
        {
            public string Key; public PlayMakerFSM Fsm; public string Open, Close;
            public Rigidbody Body; public HingeJoint Hinge;
            public bool IsOpen, Mine;          // ouverte ; ouverte par nous (on envoie son angle)
            public float LastAngle; public bool Sent;
            public bool Showing; public float TargetAngle; public float LastRemote;
            public bool Pinned, PinnedWasKinematic;   // corps fige a l'angle recu
            public float LastSentAt;
            public bool RestSet; public Quaternion RestRot; public Vector3 RestPos;   // pose fermee (repere du parent)
            public float TestOffset;   // essais : degres ajoutes a l'angle envoye
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
            // Pose fermee de chaque portiere (une fois, portiere fermee et au repos).
            foreach (Door d in byKey.Values)
                if (!d.RestSet && d.Hinge != null && !d.IsOpen && !d.Pinned && d.Body != null && ClosedNow(d))
                { d.RestSet = true; d.RestRot = d.Body.transform.localRotation; d.RestPos = d.Body.transform.localPosition; }
            // Chez nous, pour un autre : la portiere prend son angle, chaque image.
            foreach (Door d in byKey.Values)
            {
                if (!d.Showing) continue;
                if (d.Body == null || !d.IsOpen || d.Mine) { StopFollow(d); continue; }
                Show(d);
            }
            if (now < nextAngle || Session.RemoteCount == 0) return;
            nextAngle = now + 1f / 15f;
            foreach (Door d in byKey.Values)
            {
                if (!d.Mine || !d.IsOpen || d.Body == null || d.Fsm == null || d.Hinge == null || !d.RestSet) continue;
                float ang = HingeAngle(d) + d.TestOffset;
                if (d.Sent && Mathf.Abs(Mathf.DeltaAngle(ang, d.LastAngle)) < 0.4f && now - d.LastSentAt < 1f) continue;
                d.Sent = true;
                d.LastAngle = ang;
                d.LastSentAt = now;
                Session.SendAll(new NetWriter(Msg.CarDoor).U8(Session.LocalId).Str(d.Key).U8(2).F32(ang), false);
            }
        }

        static Transform CarOf(Door d) { return d.Fsm.transform.root; }

        public static void ScheduleSnapshot(Peer p) { if (Session.IsHost) snapshots.Add(new KeyValuePair<float, Peer>(Time.realtimeSinceStartup + 20f, p)); }

        // Portiere fermee pour de bon ? (automate au repos, variable Open fausse)
        static bool ClosedNow(Door d)
        {
            FsmBool o = d.Fsm.FsmVariables.FindFsmBool("Open");
            return o != null && !o.Value && d.Fsm.ActiveStateName == "Mouse off" && d.Body.velocity.sqrMagnitude < 1e-4f;
        }

        // Angle (degres) de la portiere autour de l'axe de sa charniere, depuis sa pose fermee.
        static float HingeAngle(Door d)
        {
            Quaternion rel = Quaternion.Inverse(d.RestRot) * d.Body.transform.localRotation;
            float a; Vector3 ax;
            rel.ToAngleAxis(out a, out ax);
            if (a > 180f) a -= 360f;
            return Vector3.Dot(ax, d.Hinge.axis) < 0f ? -a : a;
        }

        // Le corps de la portiere prend l'angle recu autour de SA charniere (l'ancrage ne bouge pas),
        // cinematique tant qu'il suit.
        static void Show(Door d)
        {
            if (d.Hinge == null || !d.RestSet) return;
            if (!d.Pinned) { d.Pinned = true; d.PinnedWasKinematic = d.Body.isKinematic; d.Body.isKinematic = true; }
            Transform b = d.Body.transform;
            Vector3 anchor = Vector3.Scale(b.localScale, d.Hinge.anchor);
            Vector3 pivot = d.RestPos + d.RestRot * anchor;                       // ancrage, repere du parent
            Quaternion q = d.RestRot * Quaternion.AngleAxis(d.TargetAngle, d.Hinge.axis);
            b.localRotation = q;
            b.localPosition = pivot - q * anchor;
        }

        // Fin du suivi : la portiere revient a la physique du jeu.
        static void StopFollow(Door d)
        {
            if (d.Pinned && d.Body != null) d.Body.isKinematic = d.PinnedWasKinematic;
            d.Pinned = false;
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
            float angle = 0f;
            if (kind == 2) angle = r.F32();
            if (Session.IsHost)
            {
                var w = new NetWriter(Msg.CarDoor).U8(who).Str(key).U8(kind);
                if (kind == 2) w.F32(angle);
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
                d.TargetAngle = angle; d.LastRemote = Time.realtimeSinceStartup; d.Showing = true;
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
                d.TestOffset = deg;
                return d.Key + " poussee de " + deg + " deg (angle envoye)";
            }
            return "rien a pousser";
        }

        public static string State(string key)
        {
            foreach (Door d in byKey.Values)
                if (d.Key.StartsWith(key) && d.Fsm != null)
                {
                    return d.Key + " etat " + d.Fsm.ActiveStateName
                           + (d.Body != null && d.Hinge != null && d.RestSet ? ", angle " + HingeAngle(d).ToString("F1") + " deg" : ", pas de pose fermee")
                           + (d.IsOpen ? (d.Mine ? ", ouverte par nous" : ", ouverte par un autre") : ", fermee");
                }
            return "?";
        }
    }
}
