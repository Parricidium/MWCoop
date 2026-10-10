using System.Collections.Generic;
using HutongGames.PlayMaker;
using MWCoop.Net;
using UnityEngine;

namespace MWCoop
{
    // Tas de bois des remorques (retour d'un joueur, 10/10 : « les buches n'apparaissent dans la remorque que chez l'hote ») :
    // FLATBED/Bed/LogTrigger :: Logic -- une buche (firewood) ou un tronc (log) qui tombe sur le plateau est detruit,
    // Firewood / Logs augmentent et le tas (LogpileBed) grandit d'autant ("Add scale"). Les buches de la fendeuse arrivent
    // chez tous (@cree), mais c'est la copie de l'hote qui tombe dans son plateau et compte ; il annonce ensuite leur
    // disparition (@parti) : chez les autres, rien n'etait compte, le tas restait vide.
    // Ici l'hote fait autorite : a chaque changement (et toutes les 5 s), ses compteurs et la taille du tas partent (@tas) ;
    // les autres les prennent tels quels (un comptage local entre-temps est remplace).
    public static class LogPiles
    {
        class Pile
        {
            public string Key; public PlayMakerFSM F; public FsmFloat Firewood, Logs; public FsmGameObject Bed;
            public float SFire = -1f, SLogs = -1f; public Vector3 SScale; public bool SOn;
        }
        static readonly List<Pile> piles = new List<Pile>();
        static float nextScan, nextSend, nextFull;

        public static void OnLevelLoaded() { testStep = 0; testLog = 0; piles.Clear(); nextScan = 0f; nextSend = 0f; nextFull = 0f; }

        static void Scan()
        {
            piles.Clear();
            foreach (Object o in Game.AllFsms())
            {
                var f = (PlayMakerFSM)o;
                if (f == null || f.hideFlags != HideFlags.None || f.FsmName != "Logic" || f.gameObject.name != "LogTrigger") continue;
                var p = new Pile { Key = Recon.Path(f.transform), F = f, Firewood = f.FsmVariables.FindFsmFloat("Firewood"), Logs = f.FsmVariables.FindFsmFloat("Logs"), Bed = f.FsmVariables.FindFsmGameObject("LogpileBed") };
                if (p.Firewood == null && p.Logs == null) continue;
                piles.Add(p);
            }
            if (piles.Count > 0) Log.Info("tas de bois : " + piles.Count + " remorque(s) suivie(s) (" + piles[0].Key + ")");
        }

        public static void Update()
        {
            if (!Session.Active || !PlayerSync.InGame) return;
            float now = Time.realtimeSinceStartup;
            if (piles.Count == 0 && now >= nextScan) { nextScan = now + 10f; Scan(); }
            if (!Session.IsHost || piles.Count == 0 || now < nextSend) return;
            nextSend = now + 0.5f;
            bool full = now >= nextFull;
            if (full) nextFull = now + 5f;
            foreach (Pile p in piles)
            {
                if (p.F == null) continue;
                float fw = p.Firewood != null ? p.Firewood.Value : 0f, lg = p.Logs != null ? p.Logs.Value : 0f;
                GameObject bed = p.Bed != null ? p.Bed.Value : null;
                Vector3 sc = bed != null ? bed.transform.localScale : Vector3.one;
                bool on = bed != null && bed.activeSelf;
                bool changed = fw != p.SFire || lg != p.SLogs || sc != p.SScale || on != p.SOn;
                if (!changed && !full) continue;
                if (changed && (fw != p.SFire || lg != p.SLogs)) Log.Info("tas de bois : " + p.Key + " buches " + fw.ToString("F0") + ", troncs " + lg.ToString("F0") + " (envoye)");
                p.SFire = fw; p.SLogs = lg; p.SScale = sc; p.SOn = on;
                Session.SendAll(new NetWriter(Msg.Job).U8(Session.LocalId).Str("@tas").Str(p.Key).F32(fw).F32(lg).Vec(sc).Bool(on), changed);
            }
        }

        // [Test] Autotest=tas : l'hote « recoit » 6 buches sur le plateau a 30 s puis 6 a 36 s (Firewood + 6, "Add scale" : comme
        // des buches tombees) ; chacun note a 33 et 42 s compteurs et taille du tas.
        static int testStep;
        public static void Test(string mode, float t)
        {
            if (mode != "tas") return;
            if (Session.IsHost && (t > 30f && testStep == 0 || t > 36f && testStep == 1))
            {
                testStep++;
                foreach (Pile p in piles)
                    if (p.F != null && p.Firewood != null)
                    {
                        p.Firewood.Value += 60f; if (p.Logs != null) p.Logs.Value += 60f;   // (comme 6 buches : +10 chacune)
                        Game.SetState(p.F, "Add scale");
                        Log.Info("autotest : tas, 6 buches sur " + p.Key + " (etat " + p.F.ActiveStateName + ")");
                        break;
                    }
            }
            if (t > 33f && testLog == 0 || t > 42f && testLog == 1)
            {
                testLog++;
                if (testLog == 1 && piles.Count > 0 && piles[0].F != null)
                    foreach (string sn in new[] { "Destroy Wood", "Destroy Log", "Add scale" })
                    {
                        FsmState st = piles[0].F.Fsm.GetState(sn);
                        if (st == null || st.Actions == null) continue;
                        foreach (FsmStateAction ac in st.Actions) Log.Info("autotest : tas, " + sn + " : " + Recon.Describe(ac));
                    }
                foreach (Pile p in piles)
                {
                    GameObject bed = p.Bed != null ? p.Bed.Value : null;
                    Log.Info("autotest : tas " + p.Key + " buches " + (p.Firewood != null ? p.Firewood.Value.ToString("F0") : "?") + ", tas " + (bed != null ? bed.transform.localScale.ToString("F2") + (bed.activeInHierarchy ? " visible" : " cache") : "?"));
                }
            }
        }
        static int testLog;

        public static void OnRemote(int who, string key, float fw, float lg, Vector3 sc, bool on)
        {
            if (Session.IsHost || who != 0) return;
            if (piles.Count == 0) Scan();
            foreach (Pile p in piles)
            {
                if (p.Key != key || p.F == null) continue;
                bool was = p.Firewood != null && p.Firewood.Value != fw || p.Logs != null && p.Logs.Value != lg;
                if (p.Firewood != null) p.Firewood.Value = fw;
                if (p.Logs != null) p.Logs.Value = lg;
                GameObject bed = p.Bed != null ? p.Bed.Value : null;
                if (bed != null)
                {
                    bed.transform.localScale = sc;
                    if (bed.activeSelf != on) bed.SetActive(on);
                    // masse du plateau comme "Add scale" : vide + Logs / 2 (FLATBED/Bed, deux crans au-dessus du tas)
                    FsmFloat mass = p.F.FsmVariables.FindFsmFloat("Mass"), empty = p.F.FsmVariables.FindFsmFloat("EmptyMass");
                    Transform bt = bed.transform.parent != null ? bed.transform.parent.parent : null;
                    Rigidbody rb = bt != null ? bt.GetComponent<Rigidbody>() : null;
                    if (mass != null && empty != null) { mass.Value = lg / 2f + empty.Value; if (rb != null) rb.mass = mass.Value; }
                }
                if (was) Log.Info("tas de bois : " + key + " buches " + fw.ToString("F0") + ", troncs " + lg.ToString("F0") + " (de l'hote)");
                return;
            }
        }
    }
}
