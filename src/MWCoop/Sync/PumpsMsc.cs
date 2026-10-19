using System.Collections.Generic;
using HutongGames.PlayMaker;
using MWCoop.Net;
using UnityEngine;

namespace MWCoop
{
    // Pompes de la station de Teimo, My Summer Car (vidage v.250908-04, 19/10). STORE/LOD/Pumps/<pompe>/Functions/Trigger*::Use :
    // "Activate hand" (pistolet pris : gasoline_pistol_* cache, autres pompes coupees), "Calculate" (chaque image : Cost = Fuel x
    // FuelPrice, afficheurs Meter/Litres et Meter/Price, achat a la caisse), "Wait drop", "Anim drop" / "State 2" (repose). Le
    // compteur Fuel et l'achat restent a celui qui pompe (Fluids, Shop) ; ici seulement ce que les autres voient : le pistolet
    // de la pompe pris ou en place, les afficheurs, et la pompe bloquee pour eux tant qu'un autre la tient ("Check hand" ->
    // LOCKED, comme main gauche prise).
    public static class PumpsMsc
    {
        static readonly string[] Triggers = { "Trigger98", "TriggerD", "TriggerPÖ" };
        static readonly string[] Pistols = { "gasoline_pistol_98", "gasoline_pistol_D", "gasoline_pistol_PÖ" };
        static readonly HashSet<string> HeldStates = new HashSet<string> { "Activate hand", "Calculate", "Wait drop", "Anim drop" };
        class Pump { public PlayMakerFSM Use; public GameObject Pistol; public TextMesh Litres, Price; public bool Hooked; public bool SentHeld; public string SentText = "", SentText2 = ""; public float NextSend, NextKeep; public int RemoteBy = -1; public float RemoteAt; }
        static readonly Pump[] pumps = new Pump[3];
        static float nextFind, nextPoll;
        static readonly List<FsmStateAction> cut = new List<FsmStateAction>();

        public static void OnLevelLoaded() { for (int i = 0; i < 3; i++) pumps[i] = null; nextFind = 0f; cut.Clear(); }

        class LockHook : ModHook
        {
            public override string Module { get { return "carburant (pompes)"; } }
            public int I;
            public override void OnEnter()
            {
                try
                {
                    Pump p = pumps[I];
                    if (p != null && p.RemoteBy >= 0 && Time.realtimeSinceStartup - p.RemoteAt < 3f)
                    {
                        foreach (FsmStateAction a in State.Actions) if (a != this && a.Enabled) { a.Enabled = false; cut.Add(a); }
                        Hud.Toast(Lang.T("Pistolet déjà pris par ", "Nozzle already taken by ") + Shop.PlayerName(p.RemoteBy));
                        Fsm.Event("LOCKED");
                    }
                }
                catch (System.Exception e) { Replay.HookError(e); }
                Finish();
            }
        }

        static void Find()
        {
            for (int i = 0; i < 3; i++)
            {
                if (pumps[i] != null && pumps[i].Use != null) continue;
                string root = "STORE/LOD/Pumps/" + PumpName(i);
                PlayMakerFSM u = Game.FindFsm(root + "/Functions/" + Triggers[i], "Use");
                if (u == null) continue;
                var p = new Pump { Use = u, Pistol = Game.FindAny(root + "/" + Pistols[i]) };
                GameObject l = Game.FindAny(root + "/Functions/Meter/Litres"), pr = Game.FindAny(root + "/Functions/Meter/Price");
                p.Litres = l != null ? l.GetComponent<TextMesh>() : null; p.Price = pr != null ? pr.GetComponent<TextMesh>() : null;
                pumps[i] = p;
            }
        }
        static string PumpName(int i) { return i == 0 ? "GasolinePump98" : i == 1 ? "GasolinePumpD" : "GasolinePumpPÖ"; }

        static void Hook(int i)
        {
            Pump p = pumps[i];
            if (p == null || p.Hooked || p.Use == null || !p.Use.gameObject.activeInHierarchy) return;
            FsmState s = p.Use.Fsm.GetState("Check hand");
            if (s == null || !s.IsInitialized) return;
            try { FsmHooks.Prepend(s, new LockHook { I = i }); p.Hooked = true; } catch { }
        }

        public static void Update()
        {
            if (!Game.Msc || !Session.Active || !PlayerSync.InGame) return;
            if (cut.Count > 0) { foreach (FsmStateAction a in cut) a.Enabled = true; cut.Clear(); }
            float now = Time.realtimeSinceStartup;
            if (now >= nextFind) { nextFind = now + 10f; Find(); }
            if (now < nextPoll) return;
            nextPoll = now + 0.1f;
            for (int i = 0; i < 3; i++)
            {
                Pump p = pumps[i];
                if (p == null || p.Use == null) continue;
                Hook(i);
                if (p.RemoteBy >= 0 && now - p.RemoteAt > 3f) Release(p, "plus de nouvelles");
                bool held = p.Use.gameObject.activeInHierarchy && HeldStates.Contains(p.Use.ActiveStateName ?? "");
                FsmString t1 = p.Use.FsmVariables.FindFsmString("Text"), t2 = p.Use.FsmVariables.FindFsmString("Text2");
                string a = t1 != null ? t1.Value ?? "" : "", b = t2 != null ? t2.Value ?? "" : "";
                bool changed = held != p.SentHeld, texts = a != p.SentText || b != p.SentText2;
                if (!changed && !(held && (texts && now >= p.NextSend || now >= p.NextKeep)) && !(!held && texts && p.SentHeld)) continue;
                if (!changed && !held && !texts) continue;
                p.SentHeld = held; p.SentText = a; p.SentText2 = b; p.NextSend = now + 0.2f; p.NextKeep = now + 1f;
                Session.SendAll(new NetWriter(Msg.Job).U8(Session.LocalId).Str("@pompe").U8(i).Bool(held).Str(a).Str(b), changed || !held);
                if (changed) Log.Info("pompes : pistolet " + PumpName(i) + (held ? " pris ici" : " repose ici"));
            }
        }

        static void Release(Pump p, string why)
        {
            if (p.RemoteBy < 0) return;
            p.RemoteBy = -1;
            if (p.Pistol != null && !(p.Use != null && HeldStates.Contains(p.Use.ActiveStateName ?? ""))) p.Pistol.SetActive(true);
            Log.Info("pompes : pistolet rendu (" + why + ")");
        }

        public static void OnMessage(int who, NetReader r)
        {
            int i = r.U8(); bool held = r.Bool(); string a = r.Str(), b = r.Str();
            if (i < 0 || i > 2) return;
            Find();
            Pump p = pumps[i];
            if (p == null) return;
            bool mine = p.Use != null && HeldStates.Contains(p.Use.ActiveStateName ?? "");
            if (held && !mine)
            {
                if (p.RemoteBy != who) Log.Info("pompes : pistolet " + PumpName(i) + " pris par #" + who);
                p.RemoteBy = who; p.RemoteAt = Time.realtimeSinceStartup;
                if (p.Pistol != null && p.Pistol.activeSelf) p.Pistol.SetActive(false);
            }
            else if (!held && p.RemoteBy == who) Release(p, "repose par #" + who);
            if (!mine)
            {
                if (p.Litres != null && a.Length > 0) p.Litres.text = a;
                if (p.Price != null && b.Length > 0) p.Price.text = b;
            }
        }

        public static void PlayerLeft(int id) { foreach (Pump p in pumps) if (p != null && p.RemoteBy == id) Release(p, "joueur parti"); }

        // Essais : etat des pompes d'ici.
        public static string State()
        {
            Find();
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < 3; i++)
            {
                Pump p = pumps[i];
                if (p == null) { sb.Append(' ').Append(PumpName(i)).Append(" ?"); continue; }
                sb.Append(' ').Append(PumpName(i)).Append(" [").Append(p.Use.ActiveStateName).Append("] pistolet ").Append(p.Pistol != null ? (p.Pistol.activeSelf ? "en place" : "pris") : "?")
                  .Append(", afficheur ").Append(p.Litres != null ? p.Litres.text : "?").Append('/').Append(p.Price != null ? p.Price.text : "?").Append(p.RemoteBy >= 0 ? ", tenu par #" + p.RemoteBy : "");
            }
            return sb.ToString();
        }
    }
}
