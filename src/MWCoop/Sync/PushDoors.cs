using System.Collections.Generic;
using HutongGames.PlayMaker;
using MWCoop.Net;
using UnityEngine;

namespace MWCoop
{
    // Portes poussees hors vehicules (portes du garage de la maison des parents : YARD/Building/Garage/GarageDoors/
    // Left|Right/Door/Coll :: Use). Le jeu les pousse par une force (AddTorque) TANT QUE le bouton est tenu : clic gauche
    // "Open" (+400), clic droit "Close" (-400), relachement -> "Mouse off". Rejouer ces etats chez les autres (WorldFsms)
    // les laissait pousser sans fin (le bouton n'y est jamais relache), et dans "Open"/"Close" N'IMPORTE QUEL clic de
    // l'autre joueur (GetMouseButtonDown, sans viser la porte) inversait la porte, inversion renvoyee a tous : portes qui
    // s'ouvrent et se ferment « selon leur humeur » (vraie partie du 06/10). Ici, on ne rejoue plus rien :
    //  - celui qui pousse (son automate en "Open"/"Close") envoie l'angle de la porte 10 fois/s, puis encore 1,5 s apres
    //    le relachement (elle continue sur sa lancee) et une derniere fois arretee ;
    //  - chez les autres, la porte devient cinematique le temps du suivi et prend cet angle, puis redevient libre ;
    //  - l'hote renvoie l'angle de chaque porte toutes les 5 s (arrivants, porte bougee par la physique d'un cote) ; un
    //    ecart de plus de 2 degres est corrige.
    // Le joueur d'ici qui se met a pousser une porte suivie la reprend aussitot (physique rendue).
    public static class PushDoors
    {
        class D
        {
            public string Key; public PlayMakerFSM F; public Rigidbody Body; public Transform T;
            public bool Mine; public float MineUntil, NextSend;
            public bool Following; public Quaternion Target; public float LastRecv; public bool FinalRecv;
        }

        static readonly List<D> doors = new List<D>();
        static float scanAt = -1, nextHost;
        const int K_POSE = 1;

        public static void OnLevelLoaded()
        {
            doors.Clear();
            // Avant le releve du monde (16 s) : ces automates sont reserves ici.
            scanAt = PlayerSync.InGame ? Time.realtimeSinceStartup + 8f : -1;
        }

        static void Scan()
        {
            var found = new List<KeyValuePair<string, D>>();
            foreach (PlayMakerFSM f in Object.FindObjectsOfType<PlayMakerFSM>())
            {
                if (f.FsmName != "Use") continue;
                Transform root = f.transform.root;
                if (root.GetComponent("CarDynamics") != null || f.transform.root.name.StartsWith("Hood")) continue;   // vehicules : CarDoors
                FsmState open = f.Fsm.GetState("Open"), close = f.Fsm.GetState("Close");
                if (open == null || close == null) continue;
                Rigidbody body = null;
                bool held = false;
                try
                {
                    foreach (FsmStateAction a in open.Actions)
                    {
                        var t = a as HutongGames.PlayMaker.Actions.AddTorque;
                        if (t != null) { GameObject g = f.Fsm.GetOwnerDefaultTarget(t.gameObject); if (g != null) body = g.GetComponent<Rigidbody>(); }
                        if (a is HutongGames.PlayMaker.Actions.GetMouseButtonUp) held = true;
                    }
                }
                catch { continue; }
                if (body == null || !held || body.GetComponent<HingeJoint>() == null) continue;
                found.Add(new KeyValuePair<string, D>(Recon.Path(f.transform), new D { F = f, Body = body, T = body.transform }));
            }
            found.Sort((a, b) => string.CompareOrdinal(a.Key, b.Key));
            doors.Clear();
            foreach (var kv in found)
            {
                if (!Replay.Claim(kv.Value.F, "battantes")) { Log.Warn("battantes : " + kv.Key + " deja a " + Replay.Owner(kv.Value.F)); continue; }
                kv.Value.Key = kv.Key;
                doors.Add(kv.Value);
            }
            Log.Info("battantes : " + doors.Count + " portes poussees suivies (angle de celui qui pousse)");
        }

        static bool Pushing(D d) { string s = d.F.ActiveStateName; return s == "Open" || s == "Close"; }

        public static void Update()
        {
            if (!Session.Active || scanAt < 0) return;
            float now = Time.realtimeSinceStartup;
            if (now >= scanAt) { scanAt = float.MaxValue; Scan(); }
            for (int i = 0; i < doors.Count; i++)
            {
                D d = doors[i];
                if (d.F == null || d.Body == null) continue;
                if (Pushing(d))
                {
                    if (d.Following) StopFollow(d);   // on la reprend
                    d.Mine = true; d.MineUntil = now + 1.5f;
                }
                if (d.Mine)
                {
                    bool moving = d.Body.angularVelocity.sqrMagnitude > 0.0025f;
                    if (now < d.MineUntil || moving)
                    {
                        if (now >= d.NextSend) { d.NextSend = now + 0.1f; Send(i, d, false); }
                    }
                    else { d.Mine = false; Send(i, d, true); }
                }
                else if (d.Following) Follow(d, now);
            }
            if (Session.IsHost && Session.RemoteCount > 0 && now >= nextHost)
            {
                nextHost = now + 5f;
                for (int i = 0; i < doors.Count; i++) if (!doors[i].Mine && !doors[i].Following && doors[i].T != null) Send(i, doors[i], true);
            }
        }

        static void Send(int i, D d, bool final)
        {
            Session.SendAll(new NetWriter(Msg.PushDoor).U8(K_POSE).U8(i).Quat(d.T.localRotation).Bool(final), final);
        }

        public static void OnMessage(Peer from, NetReader r)
        {
            int kind = r.U8();
            if (kind != K_POSE) return;
            int i = r.U8();
            Quaternion q = r.Quat();
            bool final = r.Bool();
            if (Session.IsHost) Session.Broadcast(new NetWriter(Msg.PushDoor).U8(K_POSE).U8(i).Quat(q).Bool(final), final, from.Id);
            if (i >= doors.Count) return;
            D d = doors[i];
            if (d.F == null || d.Body == null || d.Mine || Pushing(d)) return;
            // Releve periodique de l'hote : seulement un vrai ecart.
            if (final && !d.Following && Quaternion.Angle(d.T.localRotation, q) < 2f) return;
            if (!d.Following)
            {
                d.Following = true;
                d.Body.isKinematic = true;
                Log.Info("battantes : " + d.Key + " suivie (angle d'un autre joueur)");
            }
            d.Target = q; d.LastRecv = Time.realtimeSinceStartup; d.FinalRecv = final;
        }

        static void Follow(D d, float now)
        {
            d.T.localRotation = Quaternion.Slerp(d.T.localRotation, d.Target, 1f - Mathf.Exp(-15f * Time.deltaTime));
            if ((d.FinalRecv && Quaternion.Angle(d.T.localRotation, d.Target) < 0.5f) || now - d.LastRecv > 3f)
            {
                d.T.localRotation = d.Target;
                StopFollow(d);
            }
        }

        static void StopFollow(D d)
        {
            d.Following = false;
            d.Body.isKinematic = false;
            d.Body.velocity = Vector3.zero; d.Body.angularVelocity = Vector3.zero;
        }

        // [Test] Autotest=garage : l'hote ouvre la porte gauche du garage (clic gauche tenu 2 s : "Open" puis "Mouse off") a
        // 30 s, la referme (clic droit tenu 2 s) a 40 s ; chacun note l'angle des deux portes toutes les 2 s, de 28 a 52 s.
        // Attendu : chez l'invite, la porte gauche suit l'hote (ouverte puis refermee), la droite ne bouge pas.
        static int testStep;
        static float testLog, testRelease;
        public static void Test(string mode, float t)
        {
            if (mode != "garage" || doors.Count == 0) return;
            if (t > 28f && t < 52f && t >= testLog)
            {
                testLog = t + 2f;
                var sb = new System.Text.StringBuilder("autotest : garage t=" + t.ToString("F0") + " :");
                foreach (D d in doors) sb.Append(' ').Append(d.T.parent != null ? d.T.parent.name : d.T.name).Append(' ').Append(d.T.localEulerAngles.ToString("F0")).Append(' ').Append(d.F.ActiveStateName).Append(d.Following ? " (suivie)" : "");
                Log.Info(sb.ToString());
            }
            if (!Session.IsHost) return;
            D left = doors.Find(x => x.Key.Contains("/Left/"));
            if (left == null) return;
            if (testStep == 0 && t > 30f) { testStep = 1; Game.SetState(left.F, "Open"); testRelease = t + 2f; Log.Info("autotest : garage : porte gauche poussee (ouvrir)"); }
            if (testStep == 1 && t > testRelease) { testStep = 2; Game.SetState(left.F, "Mouse off"); }
            if (testStep == 2 && t > 40f) { testStep = 3; Game.SetState(left.F, "Close"); testRelease = t + 2f; Log.Info("autotest : garage : porte gauche poussee (fermer)"); }
            if (testStep == 3 && t > testRelease) { testStep = 4; Game.SetState(left.F, "Mouse off"); }
        }
    }
}
