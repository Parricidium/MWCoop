using System.Collections.Generic;
using HutongGames.PlayMaker;
using MWCoop.Net;
using UnityEngine;

namespace MWCoop
{
    // Reglages faits a la main sur les pieces (vis de richesse du carburateur, repartiteur de
    // freinage...) : variables Adjust1..8, Setting, SettingMixture, AdjustmentF des automates des
    // pieces. Surveillees deux fois par seconde sur les pieces a moins de 4 m du joueur (c'est lui
    // qui regle) ; un changement part aux autres, qui recopient la valeur et, si l'automate a une
    // transition globale ADJUST, la declenchent pour que l'effet soit recalcule.
    public static class Settings
    {
        static readonly string[] Names = { "Adjust1", "Adjust2", "Adjust3", "Adjust4", "Adjust5", "Adjust6", "Adjust7", "Adjust8",
                                           "Setting", "SettingMixture", "AdjustmentF" };

        class Watch { public string Id; public PlayMakerFSM Fsm; public FsmFloat Var; public float Last, NextSend; }
        static readonly List<Watch> watches = new List<Watch>();
        static readonly HashSet<PlayMakerFSM> seen = new HashSet<PlayMakerFSM>();
        static float nextScan = -1, nextPoll;
        static Transform player;

        public static void OnLevelLoaded()
        {
            watches.Clear(); seen.Clear(); player = null;
            nextScan = PlayerSync.InGame ? Time.realtimeSinceStartup + 12f : -1;
        }

        static void Scan()
        {
            foreach (Object o in Game.AllFsms())
            {
                var f = (PlayMakerFSM)o; if (f == null) continue;
                if (f.hideFlags != HideFlags.None || seen.Contains(f) || f.FsmName == "Data" || f.FsmName == "Paint") continue;
                string id = PartIdOf(f.transform);
                if (id.Length == 0) continue;
                seen.Add(f);
                foreach (string n in Names)
                {
                    FsmFloat v = f.FsmVariables.FindFsmFloat(n);
                    if (v != null) watches.Add(new Watch { Id = id, Fsm = f, Var = v, Last = v.Value });
                }
            }
        }

        // ID de la piece qui porte cet objet (lui-meme ou un parent : bouton, molette...).
        static string PartIdOf(Transform t)
        {
            for (int i = 0; t != null && i < 4; i++, t = t.parent)
            {
                string id = Parts.IdOf(t.gameObject);
                if (id.Length > 0) return id;
            }
            return "";
        }

        public static void Update()
        {
            if (!Session.Active || nextScan < 0) return;
            float now = Time.realtimeSinceStartup;
            if (now >= nextScan) { nextScan = now + 20f; Scan(); }
            if (now < nextPoll) return;
            nextPoll = now + 0.5f;
            if (player == null) { GameObject p = GameObject.Find("PLAYER"); if (p == null) return; player = p.transform; }
            foreach (Watch w in watches)
            {
                if (w.Fsm == null || Mathf.Abs(w.Var.Value - w.Last) < 1e-4f) continue;
                if (now < w.NextSend || (w.Fsm.transform.position - player.position).sqrMagnitude > 16f) { w.Last = w.Var.Value; continue; }
                w.Last = w.Var.Value;
                w.NextSend = now + 0.5f;
                Session.SendAll(new NetWriter(Msg.Setting).U8(Session.LocalId).Str(w.Id).Str(w.Fsm.FsmName).Str(w.Var.Name).F32(w.Var.Value), true);
            }
        }

        public static void OnMessage(Peer from, NetReader r)
        {
            int who = r.U8();
            if (Session.IsHost) who = from.Id;
            string id = r.Str(), fsm = r.Str(), name = r.Str();
            float value = r.F32();
            if (Session.IsHost) Session.Broadcast(new NetWriter(Msg.Setting).U8(who).Str(id).Str(fsm).Str(name).F32(value), true, who);
            foreach (Watch w in watches)
            {
                if (w.Id != id || w.Fsm == null || w.Fsm.FsmName != fsm || w.Var.Name != name) continue;
                w.Var.Value = value;
                w.Last = value;
                foreach (FsmTransition t in w.Fsm.Fsm.GlobalTransitions)
                    if (t.EventName == "ADJUST") { w.Fsm.SendEvent("ADJUST"); break; }
                Log.Info("reglage " + id + " " + fsm + "." + name + " = " + value + " (joueur #" + who + ")");
                return;
            }
            Log.Warn("reglage " + id + " " + fsm + "." + name + " introuvable ici");
        }

        // Essais : change le premier reglage suivi de la piece la plus proche du joueur.
        public static string TestNearest()
        {
            if (player == null) return "pas de joueur";
            Watch best = null;
            float bd = float.MaxValue;
            foreach (Watch w in watches)
            {
                if (w.Fsm == null) continue;
                float d = (w.Fsm.transform.position - player.position).sqrMagnitude;
                if (d < bd) { bd = d; best = w; }
            }
            if (best == null) return "aucun reglage suivi (" + watches.Count + ")";
            best.Var.Value += 1.5f;
            return best.Id + " " + best.Fsm.FsmName + "." + best.Var.Name + " = " + best.Var.Value + " (" + watches.Count + " suivis)";
        }
    }
}
