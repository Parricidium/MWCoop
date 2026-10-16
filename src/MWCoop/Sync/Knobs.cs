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
            public float RemoteAt = -10f;   // dernier message d'un autre joueur sur cette commande (il la manoeuvre)
        }

        static readonly List<Knob> knobs = new List<Knob>();
        static readonly Dictionary<string, Knob> byKey = new Dictionary<string, Knob>();
        static readonly HashSet<PlayMakerFSM> refused = new HashSet<PlayMakerFSM>();
        static readonly HashSet<string> applyTypes = new HashSet<string> { "FloatClamp", "FloatOperator", "SetPosition", "SetRotation", "SetFsmFloat", "SetFloatValue" };
        static float nextHost;

        public static void OnLevelLoaded() { knobs.Clear(); byKey.Clear(); refused.Clear(); snapshots.Clear(); parkedLog.Clear(); }

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
            kn.Key = path + "#" + Recon.Twin(f);   // (rang stable, Recon.Twin)
            for (int n = 2; byKey.ContainsKey(kn.Key); n++) kn.Key = path + "#" + Recon.Twin(f) + "+" + n;
            knobs.Add(kn);
            byKey[kn.Key] = kn;
            Log.Info("molettes : " + kn.Key + " suivie (valeur " + kn.Var.Name + " tenue dans " + string.Join(", ", new List<string>(kn.Held).ToArray()) + ", " + kn.Apply.Count + " actions de pose" + (kn.HasLight ? ", voyant" : "") + ")");
            return true;
        }

        static bool Holding(Knob k) { return k.F != null && k.Held.Contains(k.F.ActiveStateName); }

        public static void Update()
        {
            if (!Session.Active) return;
            float now = Time.realtimeSinceStartup;
            if (snapshots.Count > 0) SendSnapshots(now);
            if (knobs.Count == 0) return;
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
                // (pas une commande qu'un autre joueur manoeuvre -- message de lui depuis moins d'1 s : la valeur de l'hote,
                // en retard, la faisait reculer chez tous -- relecture du 15/10, 3.1)
                foreach (Knob k in knobs) if (k.F != null && !k.Mine && now - k.RemoteAt > 1f) Send(k, true);
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
            if (key == ParkedKey) { if (!Session.IsHost) OnParked(from, r); return; }   // (instantane d'une commande de voiture garee : hote -> arrivant seulement)
            float v = r.F32();
            bool final = r.Bool();
            if (Session.IsHost) Session.Broadcast(new NetWriter(Msg.Knob).Str(key).F32(v).Bool(final), final, from.Id);
            Knob k;
            if (!byKey.TryGetValue(key, out k) || k.F == null || k.Mine || Holding(k)) return;
            k.RemoteAt = Time.realtimeSinceStartup;
            if (Mathf.Abs(k.Var.Value - v) < 0.001f && final) return;
            PlayerSync.ReachNearest(k.F.transform);   // (son avatar tient la commande : retour de Menor, 11/10)
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

        // ---------------------------------------------------------------- commandes des voitures garees (arrivants)
        // Phares, essuie-glaces... d'une voiture garee : automates 'Use' des boutons du tableau de bord (ButtonLightModes :
        // entier Selection 0/1/2 que "Mode add" incremente et que "Test" applique ; ButtonWipers...). Leurs clics partent aux
        // autres (Interactions) et l'hote renvoie leurs booleens a l'arrivant ("=etat") -- pas leurs entiers : l'invite arrive
        // en cours de partie voyait les phares eteints quand tous les voyaient allumes (relecture du 15/10, ecart n17). Ici,
        // 20 s apres son arrivee (voitures relevees des deux cotes), l'hote lui envoie, pour chaque voiture : la valeur de
        // chaque commande tenue (Msg.Knob, format habituel, fiable) et l'etat de chaque bouton (Msg.Knob, cle "@garee" :
        // voiture, automate, etat courant, etat vise par USE, entiers, flottants, booleens). Chez l'arrivant, seules les
        // valeurs differentes sont posees ; l'automate est alors rejoue une fois pour que le jeu applique (lampes, rotation du
        // bouton, evenement SWITCH) : l'etat vise par USE, ou l'etat qui le suit quand il INCREMENTE un entier ("Mode add" ->
        // "Test" : l'ajout est deja dans la valeur recue) ; un booleen decisif est inverse avant le rejeu, comme Interactions
        // (l'etat le bascule lui-meme). Rien n'est rejoue si l'automate n'est pas au repos (pas de transition USE depuis son
        // etat courant, ou pas dans l'etat de l'hote) : valeurs posees seulement.
        const string ParkedKey = "@garee";
        static readonly List<KeyValuePair<float, Peer>> snapshots = new List<KeyValuePair<float, Peer>>();   // arrivants (hote)
        static readonly Dictionary<string, float> parkedLog = new Dictionary<string, float>();
        public static void ScheduleSnapshot(Peer p) { if (Session.IsHost) snapshots.Add(new KeyValuePair<float, Peer>(Time.realtimeSinceStartup + 20f, p)); }

        // Bouton du tableau de bord : automate 'Use' d'un objet Button* / Switch* (pas le klaxon, tenu ; pas une commande tenue d'ici).
        static bool IsSwitch(PlayMakerFSM f)
        {
            if (f == null || f.FsmName != "Use") return false;
            string n = f.gameObject.name;
            if (!(n.StartsWith("Button") || n.StartsWith("Switch")) || n.Contains("Horn")) return false;
            foreach (Knob k in knobs) if (k.F == f) return false;
            return true;
        }

        // Etat vise par l'evenement USE depuis l'etat courant (fromActiveOnly), sinon depuis n'importe quel etat ; null : aucun.
        static string UseTarget(PlayMakerFSM f, bool fromActiveOnly)
        {
            try
            {
                if (f.Fsm == null || !f.Fsm.Initialized) return null;
                string any = null;
                foreach (FsmState s in f.Fsm.States)
                    foreach (FsmTransition t in s.Transitions)
                        if (t.EventName == "USE")
                        {
                            if (s.Name == f.ActiveStateName) return t.ToState;
                            if (any == null) any = t.ToState;
                        }
                return fromActiveOnly ? null : any;
            }
            catch { return null; }
        }

        static bool SkipVar(string n) { return n.StartsWith("UT") || n.StartsWith("UniqueTag"); }
        static bool Increments(FsmState s)
        {
            foreach (FsmStateAction a in s.Actions) if (a != null && (a.GetType().Name == "IntAdd" || a.GetType().Name == "IntOperator")) return true;
            return false;
        }
        static string FinishedOf(FsmState s)
        {
            foreach (FsmTransition t in s.Transitions) if (t.EventName == "FINISHED") return t.ToState;
            return s.Transitions.Length == 1 ? s.Transitions[0].ToState : null;
        }
        static string SwitchKey(Transform root, PlayMakerFSM f) { return VehicleSync.RelPath(root, f.transform) + "::" + f.FsmName + "#" + Recon.Twin(f); }

        static void SendSnapshots(float now)
        {
            for (int i = snapshots.Count - 1; i >= 0; i--)
            {
                if (now < snapshots[i].Key) continue;
                Peer p = snapshots[i].Value;
                snapshots.RemoveAt(i);
                if (!p.Accepted || Session.T == null || !Session.T.Peers.Contains(p)) continue;
                int held = 0, switches = 0, carsN = 0;
                foreach (Knob k in knobs)
                    if (k.F != null) { Session.T.SendReliable(p, new NetWriter(Msg.Knob).Str(k.Key).F32(k.Var.Value).Bool(true).ToArray()); held++; }
                for (int ci = 0; ci < VehicleSync.LocalCount; ci++)
                {
                    Rigidbody rb = VehicleSync.LocalBody(ci);
                    if (rb == null || !rb.gameObject.activeInHierarchy) continue;
                    string carKey = VehicleSync.LocalKey(ci);
                    carsN++;
                    foreach (PlayMakerFSM f in rb.GetComponentsInChildren<PlayMakerFSM>(true))
                    {
                        if (!IsSwitch(f)) continue;
                        string target = UseTarget(f, false), state = f.ActiveStateName;
                        if (target == null || string.IsNullOrEmpty(state)) continue;
                        var w = new NetWriter(Msg.Knob).Str(ParkedKey).Str(carKey).Str(SwitchKey(rb.transform, f)).Str(state).Str(target);
                        FsmVariables v = f.FsmVariables;
                        var ints = new List<FsmInt>(); foreach (FsmInt x in v.IntVariables) if (!SkipVar(x.Name)) ints.Add(x);
                        var floats = new List<FsmFloat>(); foreach (FsmFloat x in v.FloatVariables) if (!SkipVar(x.Name) && !float.IsNaN(x.Value) && !float.IsInfinity(x.Value)) floats.Add(x);
                        var bools = new List<FsmBool>(); foreach (FsmBool x in v.BoolVariables) if (!SkipVar(x.Name)) bools.Add(x);
                        w.U8(ints.Count); foreach (FsmInt x in ints) w.Str(x.Name).I32(x.Value);
                        w.U8(floats.Count); foreach (FsmFloat x in floats) w.Str(x.Name).F32(x.Value);
                        w.U8(bools.Count); foreach (FsmBool x in bools) w.Str(x.Name).Bool(x.Value);
                        if (w.Length > Transport.MaxPayload - 20) continue;
                        Session.T.SendReliable(p, w.ToArray());
                        switches++;
                    }
                }
                Log.Info("molettes : instantane pour " + p + " : " + held + " commandes tenues, " + switches + " boutons de " + carsN + " voitures garees");
            }
        }

        static void ParkedLog(string key, string msg)
        {
            float now = Time.realtimeSinceStartup, last;
            if (parkedLog.TryGetValue(key, out last) && now - last < 30f) return;
            parkedLog[key] = now;
            Log.Info("molettes : " + key + " : " + msg);
        }

        // Arrivant : etat d'un bouton d'une voiture garee chez l'hote (voir plus haut).
        static void OnParked(Peer from, NetReader r)
        {
            string carKey = r.Str(), fsmKey = r.Str(), hostState = r.Str(), target = r.Str();
            var ints = new List<KeyValuePair<string, int>>(); for (int i = 0, n = r.U8(); i < n; i++) ints.Add(new KeyValuePair<string, int>(r.Str(), r.I32()));
            var floats = new List<KeyValuePair<string, float>>(); for (int i = 0, n = r.U8(); i < n; i++) floats.Add(new KeyValuePair<string, float>(r.Str(), r.F32()));
            var bools = new List<KeyValuePair<string, bool>>(); for (int i = 0, n = r.U8(); i < n; i++) bools.Add(new KeyValuePair<string, bool>(r.Str(), r.Bool()));
            Transform root = VehicleSync.RootByKey(carKey);
            if (root == null) { ParkedLog(carKey, "voiture inconnue ici, bouton " + fsmKey + " ignore"); return; }
            PlayMakerFSM f = null;
            foreach (PlayMakerFSM x in root.GetComponentsInChildren<PlayMakerFSM>(true))
                if (x.FsmName == "Use" && SwitchKey(root, x) == fsmKey) { f = x; break; }
            if (f == null) { ParkedLog(carKey + fsmKey, "automate introuvable ici"); return; }
            bool intDiff = false, boolDiff = false; string decisive = null;
            var changed = new List<string>();
            foreach (KeyValuePair<string, int> kv in ints)
            {
                FsmInt v = f.FsmVariables.FindFsmInt(kv.Key);
                if (v == null || v.Value == kv.Value || !Game.LocalVar(f, kv.Key)) continue;
                changed.Add(kv.Key + " " + v.Value + "->" + kv.Value); v.Value = kv.Value; intDiff = true;
            }
            foreach (KeyValuePair<string, float> kv in floats)
            {
                FsmFloat v = f.FsmVariables.FindFsmFloat(kv.Key);
                if (v == null || Mathf.Abs(v.Value - kv.Value) < 0.001f || !Game.LocalVar(f, kv.Key)) continue;
                v.Value = kv.Value;
            }
            foreach (KeyValuePair<string, bool> kv in bools)
            {
                FsmBool v = f.FsmVariables.FindFsmBool(kv.Key);
                if (v == null || v.Value == kv.Value || !Game.LocalVar(f, kv.Key)) continue;
                changed.Add(kv.Key + " " + v.Value + "->" + kv.Value); v.Value = kv.Value; boolDiff = true;
                if (decisive == null) decisive = kv.Key;
            }
            if (!intDiff && !boolDiff) return;
            string what = string.Join(", ", changed.ToArray());
            if (UseTarget(f, true) == null || f.ActiveStateName != hostState)
            { Log.Info("molettes : " + carKey + fsmKey + " : " + what + " poses sans rejeu (etat " + f.ActiveStateName + ", chez l'hote " + hostState + ")"); return; }
            string replay = target;
            if (intDiff)
            {
                FsmState ts = f.Fsm.GetState(target);
                if (ts != null && Increments(ts)) { string next = FinishedOf(ts); if (next != null) replay = next; }
            }
            else
            {
                FsmBool d = f.FsmVariables.FindFsmBool(decisive);
                if (d != null) d.Value = !d.Value;
            }
            Replay.Depth++;
            try { Game.SetState(f, replay); }
            catch (System.Exception e) { Replay.HookError(e); }
            finally { Replay.Depth--; }
            Log.Info("molettes : " + carKey + fsmKey + " remis comme chez l'hote (" + what + " ; rejeu " + replay + " -> " + f.ActiveStateName + ")");
        }

        // Essais : clic sur le bouton 'objName' (ButtonLightModes, ButtonWipers) de la voiture 'car', ici, comme le joueur :
        // l'etat vise par USE est pose (le crochet d'Interactions l'envoie aux autres presents, comme un vrai clic).
        public static string TestSwitch(string car, string objName)
        {
            Rigidbody rb = VehicleSync.Body(car);
            if (rb == null) return "voiture " + car + " inconnue";
            foreach (PlayMakerFSM f in rb.GetComponentsInChildren<PlayMakerFSM>(true))
            {
                if (f.FsmName != "Use" || f.gameObject.name != objName) continue;
                string target = UseTarget(f, false);
                if (target == null) return objName + " : pas de transition USE";
                Game.SetState(f, target);
                return objName + " -> " + target + " (" + f.ActiveStateName + ")";
            }
            return objName + " introuvable sous " + rb.name;
        }

        // Essais : phares (Selection de ButtonLightModes : 0 eteints, 1 position, 2 route), essuie-glaces (variables de
        // ButtonWipers), lampes allumees, automate d'animation des essuie-glaces, puis chaque bouton de 'car'.
        public static string DescribeSwitches(string car)
        {
            Rigidbody rb = VehicleSync.Body(car);
            if (rb == null) return "voiture " + car + " inconnue";
            string phares = "?", essuie = "?", anim = "?";
            var sb = new System.Text.StringBuilder();
            foreach (PlayMakerFSM f in rb.GetComponentsInChildren<PlayMakerFSM>(true))
            {
                if (f.gameObject.name.StartsWith("Wiper") && anim == "?") anim = f.gameObject.name + "::" + f.FsmName + " [" + f.ActiveStateName + "]";
                if (!IsSwitch(f)) continue;
                string n = f.gameObject.name;
                var vars = new List<string>();
                foreach (FsmInt x in f.FsmVariables.IntVariables) if (!SkipVar(x.Name)) vars.Add(x.Name + "=" + x.Value);
                foreach (FsmBool x in f.FsmVariables.BoolVariables) if (!SkipVar(x.Name)) vars.Add(x.Name + "=" + (x.Value ? "1" : "0"));
                string desc = string.Join(" ", vars.ToArray());
                if (n == "ButtonLightModes") { FsmInt sel = f.FsmVariables.FindFsmInt("Selection"); phares = sel != null ? sel.Value.ToString() : desc; }
                else if (n == "ButtonWipers") essuie = desc.Length > 0 ? desc : "[" + f.ActiveStateName + "]";
                sb.Append(" | ").Append(n).Append(" [").Append(f.ActiveStateName).Append("] ").Append(desc);
            }
            int lit = 0;
            foreach (Light l in rb.GetComponentsInChildren<Light>(false)) if (l.enabled) lit++;
            return "phares=" + phares + " essuie=" + essuie + " lampes=" + lit + " anim=" + anim + sb;
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
