using System.Collections.Generic;
using HutongGames.PlayMaker;
using MWCoop.Net;
using UnityEngine;

namespace MWCoop
{
    // Commandes tenues des vehicules (starter de la SORBET et de la CORRIS, frein a main...) : automate 'Use' dont un etat
    // fait monter ou descendre une valeur TANT QUE le bouton est tenu (FloatAdd par seconde, a chaque image) et en sort au
    // relachement (GetMouseButtonUp). Rejouer ces etats chez les autres (Jobs) les y laissait : rien n'y relache le bouton,
    // la valeur courait jusqu'a sa butee ou restait au point d'arrivee du rejeu -- starter qui « ne garde pas son etat »
    // (retour de JD, 07/10, comme la porte du garage). Ici, comme PushDoors : on ne rejoue plus rien.
    //  - celui qui tient le bouton (son automate dans un de ces etats) envoie la valeur 10 fois/s, puis une derniere fois ;
    //  - chez les autres, la valeur est posee dans la variable de l'automate, puis les actions de calcul et de pose de
    //    l'etat sont jouees une fois (bornes, position du levier, valeur envoyee au moteur) ; le frein a main lit lui-meme
    //    la variable (son automate 'Brake'). A la derniere valeur, "Check light" (voyant du starter) s'il existe ;
    //  - l'hote renvoie toutes les valeurs toutes les 5 s (arrivants ; ecart de plus de 0,001 corrige).
    // Reconnus par Jobs a son releve (avant de les accrocher en quetes) : Adopt.
    public static class Knobs
    {
        class Knob
        {
            public string Key; public PlayMakerFSM F; public FsmFloat Var;
            public HashSet<string> Held = new HashSet<string>();
            public List<FsmStateAction> Apply = new List<FsmStateAction>();
            public bool HasLight;
            public bool Mine; public float NextSend; public float LastSent = float.NaN;
        }

        static readonly List<Knob> knobs = new List<Knob>();
        static readonly Dictionary<string, Knob> byKey = new Dictionary<string, Knob>();
        static readonly HashSet<PlayMakerFSM> refused = new HashSet<PlayMakerFSM>();
        static readonly HashSet<string> applyTypes = new HashSet<string> { "FloatClamp", "FloatOperator", "SetPosition", "SetRotation", "SetFsmFloat", "SetFloatValue" };
        static float nextHost;

        public static void OnLevelLoaded() { knobs.Clear(); byKey.Clear(); refused.Clear(); }

        // Jobs, a son releve, pour chaque automate 'Use' d'un vehicule : vrai si c'est une commande tenue (reservee ici).
        public static bool Adopt(PlayMakerFSM f, string path)
        {
            if (f == null || f.FsmName != "Use" || refused.Contains(f)) return false;
            foreach (Knob k in knobs) if (k.F == f) return true;
            var kn = new Knob { F = f };
            try
            {
                foreach (FsmState s in f.Fsm.States)
                {
                    if (!s.IsInitialized) { refused.Add(f); return false; }
                    FsmFloat v = null;
                    bool up = false;
                    foreach (FsmStateAction a in s.Actions)
                    {
                        var add = a as HutongGames.PlayMaker.Actions.FloatAdd;
                        var sub = a as HutongGames.PlayMaker.Actions.FloatSubtract;
                        if (add != null && add.perSecond && add.everyFrame) v = add.floatVariable;
                        if (sub != null && sub.perSecond && sub.everyFrame) v = sub.floatVariable;
                        if (a is HutongGames.PlayMaker.Actions.GetMouseButtonUp) up = true;
                    }
                    if (v == null || !up || string.IsNullOrEmpty(v.Name)) continue;
                    if (kn.Var != null && kn.Var.Name != v.Name) { refused.Add(f); return false; }
                    kn.Var = v;
                    kn.Held.Add(s.Name);
                    if (kn.Apply.Count == 0)
                        foreach (FsmStateAction a in s.Actions)
                            if (a != null && applyTypes.Contains(a.GetType().Name)) kn.Apply.Add(a);
                }
                kn.HasLight = f.Fsm.GetState("Check light") != null;
            }
            catch { refused.Add(f); return false; }
            if (kn.Var == null || !Replay.Claim(f, "molettes")) { refused.Add(f); return false; }
            int n = 0;
            while (byKey.ContainsKey(path + "#" + n)) n++;
            kn.Key = path + "#" + n;
            knobs.Add(kn);
            byKey[kn.Key] = kn;
            Log.Info("molettes : " + kn.Key + " suivie (valeur " + kn.Var.Name + " tenue dans " + string.Join(", ", new List<string>(kn.Held).ToArray()) + ", " + kn.Apply.Count + " actions de pose" + (kn.HasLight ? ", voyant" : "") + ")");
            return true;
        }

        static bool Holding(Knob k) { return k.F != null && k.Held.Contains(k.F.ActiveStateName); }

        public static void Update()
        {
            if (!Session.Active || knobs.Count == 0) return;
            float now = Time.realtimeSinceStartup;
            foreach (Knob k in knobs)
            {
                if (k.F == null) continue;
                if (Holding(k))
                {
                    k.Mine = true;
                    if (now >= k.NextSend) { k.NextSend = now + 0.1f; Send(k, false); }
                }
                else if (k.Mine) { k.Mine = false; Send(k, true); }
            }
            if (Session.IsHost && Session.RemoteCount > 0 && now >= nextHost)
            {
                nextHost = now + 5f;
                foreach (Knob k in knobs) if (k.F != null && !k.Mine) Send(k, true);
            }
        }

        static void Send(Knob k, bool final)
        {
            k.LastSent = k.Var.Value;
            Session.SendAll(new NetWriter(Msg.Knob).Str(k.Key).F32(k.Var.Value).Bool(final), final);
        }

        public static void OnMessage(Peer from, NetReader r)
        {
            string key = r.Str();
            float v = r.F32();
            bool final = r.Bool();
            if (Session.IsHost) Session.Broadcast(new NetWriter(Msg.Knob).Str(key).F32(v).Bool(final), final, from.Id);
            Knob k;
            if (!byKey.TryGetValue(key, out k) || k.F == null || k.Mine || Holding(k)) return;
            if (Mathf.Abs(k.Var.Value - v) < 0.001f && final) return;
            Set(k, v, final);
        }

        static void Set(Knob k, float v, bool final)
        {
            k.Var.Value = v;
            foreach (FsmStateAction a in k.Apply)
            {
                try { a.OnEnter(); a.OnLateUpdate(); }
                catch (System.Exception e) { Replay.HookError(e); }
            }
            if (final && k.HasLight && k.F.ActiveStateName == "Wait player") Game.SetState(k.F, "Check light");
        }

        // Essais : valeur et pose du levier de chaque commande suivie.
        public static string Describe()
        {
            var sb = new System.Text.StringBuilder();
            foreach (Knob k in knobs)
                if (k.F != null) sb.Append(k.Key).Append(" = ").Append(k.Var.Value.ToString("F3")).Append(" (").Append(k.F.ActiveStateName).Append(") ; ");
            return sb.Length > 0 ? sb.ToString() : "aucune";
        }

        // Essais : pose la valeur 'to' sur la commande dont la cle contient 'part', ici, et l'envoie aux autres.
        public static string TestHold(string part, float to)
        {
            foreach (Knob k in knobs)
                if (k.F != null && k.Key.Contains(part))
                {
                    Set(k, to, false);
                    Send(k, true);
                    return k.Key + " -> " + to.ToString("F2");
                }
            return "aucune commande " + part;
        }
    }
}
