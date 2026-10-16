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
        // TierodLeft/RightSetting : parallelisme, sur le Data de la cremaillere (sa vis de reglage ajoute +-0,05 et envoie
        // ADJUSTMENT, mais seulement avec la cle a cliquet en main : rejouee chez l'invite elle ne faisait rien -- Vaydon, 16/10).
        static readonly string[] Names = { "Adjust1", "Adjust2", "Adjust3", "Adjust4", "Adjust5", "Adjust6", "Adjust7", "Adjust8",
                                           "Setting", "SettingMixture", "AdjustmentF", "TierodLeftSetting", "TierodRightSetting" };
        static bool anyRange;   // essais : sans la regle des 4 m

        class Watch { public string Id; public PlayMakerFSM Fsm; public FsmFloat Var; public float Last, NextSend; }
        static readonly List<Watch> watches = new List<Watch>();
        static readonly HashSet<PlayMakerFSM> seen = new HashSet<PlayMakerFSM>();
        // Automates hors des pieces : ecartes jusqu'au prochain grand releve (toutes les 2 min) -- le releve de toute la
        // scene toutes les 20 s coutait 15 ms (a-coup ; retour de joueurs, 10/10).
        static readonly HashSet<PlayMakerFSM> noPart = new HashSet<PlayMakerFSM>();
        static float noPartClear;
        static float nextScan = -1, nextPoll;
        static Transform player;

        public static void OnLevelLoaded()
        {
            watches.Clear(); seen.Clear(); noPart.Clear(); player = null;
            nextScan = PlayerSync.InGame ? Time.realtimeSinceStartup + 12f : -1;
        }

        static void Scan()
        {
            if (Time.realtimeSinceStartup >= noPartClear) { noPartClear = Time.realtimeSinceStartup + 120f; noPart.Clear(); }
            foreach (Object o in Game.AllFsms())
            {
                var f = (PlayMakerFSM)o; if (f == null) continue;
                if (seen.Contains(f) || noPart.Contains(f) || f.hideFlags != HideFlags.None || f.FsmName == "Paint") continue;
                if (f.FsmName == "Data" && f.FsmVariables.FindFsmFloat("TierodLeftSetting") == null) continue;   // (Data : la cremaillere seule)
                string id = PartIdOf(f.transform);
                if (id.Length == 0) { noPart.Add(f); continue; }
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
            if (player == null) { GameObject p = Game.PlayerGo; if (p == null) return; player = p.transform; }
            foreach (Watch w in watches)
            {
                if (w.Fsm == null || Mathf.Abs(w.Var.Value - w.Last) < 1e-4f) continue;
                if (now < w.NextSend || (!anyRange && (w.Fsm.transform.position - player.position).sqrMagnitude > 16f)) { w.Last = w.Var.Value; continue; }
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
                // Cremaillere montee (etat Bolted) : ADJUSTMENT borne la valeur et la passe a la voiture, qui tourne la roue (ToeFL/FR).
                if (name.StartsWith("Tierod")) w.Fsm.SendEvent("ADJUSTMENT");
                Log.Info("reglage " + id + " " + fsm + "." + name + " = " + value + " (joueur #" + who + ")");
                return;
            }
            Log.Warn("reglage " + id + " " + fsm + "." + name + " introuvable ici");
        }

        // Essais : parallelisme -- la cremaillere montee : +0,5 sur la biellette gauche puis ADJUSTMENT (comme sa vis) si
        // 'bump' ; etat (piece, voiture, angle de la roue).
        public static string TestToe(bool bump)
        {
            anyRange = true;
            PlayMakerFSM rack = null, any = null;
            foreach (Object o in Game.AllFsms())
            {
                var f = (PlayMakerFSM)o;
                if (f == null || f.FsmName != "Data" || f.FsmVariables.FindFsmFloat("TierodLeftSetting") == null || !f.gameObject.activeInHierarchy) continue;
                if (f.transform.root.name.StartsWith("CORRIS")) { rack = f; break; }
                if (any == null) any = f;
            }
            if (rack == null) rack = any;
            if (rack == null) return "cremaillere introuvable";
            FsmFloat v = rack.FsmVariables.GetFsmFloat("TierodLeftSetting");
            if (bump) { v.Value += 0.5f; rack.SendEvent("ADJUSTMENT"); }
            GameObject car = Game.FindAny("CORRIS/PhysicalAssemblies/FRONT/AxleFront/VINP_SteeringRack");
            PlayMakerFSM cd = car != null ? Game.FsmOn(car, "Data") : null;
            FsmFloat cv = cd != null ? cd.FsmVariables.FindFsmFloat("TieRodLeftSetting") : null;
            GameObject toe = Game.FindAny("CORRIS/PhysicalAssemblies/FRONT/AxleFront/LinkFL/ToeFL");
            return rack.gameObject.name + " (" + rack.ActiveStateName + ") TierodLeftSetting=" + v.Value.ToString("F2")
                   + " ; voiture TieRodLeftSetting=" + (cv != null ? cv.Value.ToString("F2") : "?")
                   + " ; ToeFL y=" + (toe != null ? toe.transform.localEulerAngles.y.ToString("F2") : "?") + " ; " + watches.Count + " reglages suivis";
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
