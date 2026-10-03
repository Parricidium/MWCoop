using System.Collections.Generic;
using HutongGames.PlayMaker;
using MWCoop.Net;
using UnityEngine;

namespace MWCoop
{
    // Quetes / boulots communs (demande de JD : une quete entamee par l'un et terminee par l'autre
    // est validee pour les deux). Les boulots (JOBS : taxi, fosses septiques, bois, Jokke, usine,
    // publicites, oncle...) gardent leur progression dans des automates qui declarent leurs cles de
    // sauvegarde (variables texte UniqueTag* / UT*). Ce sont eux qu'on suit :
    //  - une action injectee au debut de chacun de leurs etats repere les changements venus d'un vrai
    //    evenement (pas FINISHED, ni chargement/sauvegarde) et les envoie avec les variables de
    //    l'automate (etape, compteurs, reels, booleens) ;
    //  - ailleurs : variables recopiees, puis meme evenement si l'automate est dans le meme etat de
    //    depart, sinon recalage direct sur l'etat d'arrivee.
    // L'argent d'un boulot rejoue n'est pas double : voir Wallet.Suppress.
    public static class Jobs
    {
        static readonly string[] Roots = { "JOBS" };
        static readonly HashSet<string> Ignore = new HashSet<string> { "FINISHED", "SAVEGAME", "LOAD", "EXISTS", "NOTEXISTS", "DONOTEXIST", "DOESNOTEXIST", "SAVE" };
        class Job { public string Key; public PlayMakerFSM F; public float WindowStart; public int Count; public bool Noisy; }
        static readonly Dictionary<string, Job> jobs = new Dictionary<string, Job>();
        static readonly HashSet<PlayMakerFSM> hooked = new HashSet<PlayMakerFSM>();
        static float nextScan = -1, loadedAt;
        static bool applying;

        class Hook : FsmStateAction
        {
            public Job J;
            public string State;
            public override void OnEnter()
            {
                if (!applying) OnLocal(J, State);
                Finish();
            }
        }

        public static void OnLevelLoaded()
        {
            jobs.Clear(); hooked.Clear();
            loadedAt = Time.realtimeSinceStartup;
            nextScan = PlayerSync.InGame ? loadedAt + 10f : -1;
        }

        public static void Update()
        {
            if (nextScan < 0 || Time.realtimeSinceStartup < nextScan) return;
            nextScan = Time.realtimeSinceStartup + 20f;
            int added = 0;
            var seen = new Dictionary<string, int>();
            foreach (string root in Roots)
            {
                GameObject r = Game.FindAny(root);
                if (r == null) continue;
                foreach (PlayMakerFSM f in r.GetComponentsInChildren<PlayMakerFSM>(true))
                {
                    if (f.FsmName == "Use" || f.FsmName == "LOD" || !Persistent(f)) continue;
                    string on = f.gameObject.name;
                    if (on.Contains("(itemx)") || on.Contains("(Clone)")) continue;   // objets : Props/Interactions
                    string path = Recon.Path(f.transform) + "::" + f.FsmName;
                    int k;
                    seen.TryGetValue(path, out k);
                    seen[path] = k + 1;
                    string key = path + "#" + k;
                    if (hooked.Contains(f)) continue;
                    var j = new Job { Key = key, F = f };
                    if (!InjectAll(j)) continue;   // automate pas encore charge : au prochain passage
                    hooked.Add(f);
                    jobs[key] = j;
                    added++;
                }
            }
            if (added > 0) Log.Info("quetes : " + added + " automates de boulots de plus suivis (" + jobs.Count + " en tout)");
        }

        static bool Persistent(PlayMakerFSM f)
        {
            foreach (FsmString s in f.FsmVariables.StringVariables)
                if (s.Name.StartsWith("UniqueTag") || s.Name.StartsWith("UT")) return true;
            return false;
        }

        static bool InjectAll(Job j)
        {
            try
            {
                foreach (FsmState s in j.F.Fsm.States)
                {
                    var list = new List<FsmStateAction>(s.Actions);
                    list.Insert(0, new Hook { J = j, State = s.Name });
                    s.Actions = list.ToArray();
                }
                return true;
            }
            catch { return false; }
        }

        static void OnLocal(Job j, string state)
        {
            if (!Session.Active || Time.realtimeSinceStartup - loadedAt < 25f) return;
            FsmTransition tr = j.F.Fsm.LastTransition;
            if (tr == null || Ignore.Contains(tr.EventName) || tr.ToState != state) return;
            // Garde-fou : un automate qui boucle (plus de 5 changements en 10 s) n'est plus envoye.
            float now = Time.realtimeSinceStartup;
            if (now - j.WindowStart > 10f) { j.WindowStart = now; j.Count = 0; }
            if (++j.Count > 5 || j.Noisy)
            {
                if (!j.Noisy) Log.Warn("quete : " + j.Key + " change trop souvent, plus envoye");
                j.Noisy = true;
                return;
            }
            FsmState prev = j.F.Fsm.PreviousActiveState;
            var w = new NetWriter(Msg.Job).U8(Session.LocalId).Str(j.Key).Str(prev != null ? prev.Name : "").Str(tr.EventName).Str(state);
            WriteVars(j.F, w);
            Log.Info("quete : " + j.Key + " " + (prev != null ? prev.Name : "?") + " -" + tr.EventName + "-> " + state);
            Session.SendAll(w, true);
        }

        static bool Skip(string n) { return n.StartsWith("UT") || n.StartsWith("UniqueTag"); }

        static void WriteVars(PlayMakerFSM f, NetWriter w)
        {
            FsmVariables v = f.FsmVariables;
            var ints = new List<FsmInt>(); foreach (FsmInt x in v.IntVariables) if (!Skip(x.Name)) ints.Add(x);
            var floats = new List<FsmFloat>(); foreach (FsmFloat x in v.FloatVariables) if (!Skip(x.Name)) floats.Add(x);
            var bools = new List<FsmBool>(); foreach (FsmBool x in v.BoolVariables) if (!Skip(x.Name)) bools.Add(x);
            w.U8(ints.Count); foreach (FsmInt x in ints) w.Str(x.Name).I32(x.Value);
            w.U8(floats.Count); foreach (FsmFloat x in floats) w.Str(x.Name).F32(x.Value);
            w.U8(bools.Count); foreach (FsmBool x in bools) w.Str(x.Name).Bool(x.Value);
        }

        public static void OnMessage(Peer from, NetReader r)
        {
            int who = r.U8();
            if (Session.IsHost) who = from.Id;
            string key = r.Str(), prev = r.Str(), ev = r.Str(), state = r.Str();
            var ints = new List<KeyValuePair<string, int>>();
            var floats = new List<KeyValuePair<string, float>>();
            var bools = new List<KeyValuePair<string, bool>>();
            for (int i = 0, n = r.U8(); i < n; i++) ints.Add(new KeyValuePair<string, int>(r.Str(), r.I32()));
            for (int i = 0, n = r.U8(); i < n; i++) floats.Add(new KeyValuePair<string, float>(r.Str(), r.F32()));
            for (int i = 0, n = r.U8(); i < n; i++) bools.Add(new KeyValuePair<string, bool>(r.Str(), r.Bool()));
            if (Session.IsHost)
            {
                var w = new NetWriter(Msg.Job).U8(who).Str(key).Str(prev).Str(ev).Str(state);
                w.U8(ints.Count); foreach (var x in ints) w.Str(x.Key).I32(x.Value);
                w.U8(floats.Count); foreach (var x in floats) w.Str(x.Key).F32(x.Value);
                w.U8(bools.Count); foreach (var x in bools) w.Str(x.Key).Bool(x.Value);
                Session.Broadcast(w, true, who);
            }
            Job j;
            if (!jobs.TryGetValue(key, out j) || j.F == null) { Log.Warn("quete " + key + " introuvable ici"); return; }
            FsmVariables v = j.F.FsmVariables;
            foreach (var x in ints) { FsmInt t = v.FindFsmInt(x.Key); if (t != null) t.Value = x.Value; }
            foreach (var x in floats) { FsmFloat t = v.FindFsmFloat(x.Key); if (t != null) t.Value = x.Value; }
            foreach (var x in bools) { FsmBool t = v.FindFsmBool(x.Key); if (t != null) t.Value = x.Value; }
            Wallet.Suppress(8f);   // la paie que le boulot rejoue ici n'est pas renvoyee aux autres
            applying = true;
            try
            {
                // Meme etat de depart : meme evenement (memes actions). Sinon, ou si l'automate n'a
                // pas suivi (condition locale differente), recalage direct sur l'etat d'arrivee.
                if (j.F.ActiveStateName == prev) j.F.SendEvent(ev);
                if (j.F.ActiveStateName != state && j.F.Fsm.GetState(state) != null) Game.SetState(j.F, state);
            }
            finally { applying = false; }
            Log.Info("quete de #" + who + " : " + key + " -> " + j.F.ActiveStateName + " (voulu " + state + ")");
        }

        // Essais : envoie l'evenement 'ev' a l'automate de boulot dont la cle contient 'part'.
        public static string TestEvent(string part, string ev)
        {
            foreach (Job j in jobs.Values)
                if (j.F != null && j.Key.Contains(part))
                {
                    string before = j.F.ActiveStateName;
                    j.F.SendEvent(ev);
                    return j.Key + " : " + before + " -" + ev + "-> " + j.F.ActiveStateName;
                }
            return "aucun boulot " + part + " (" + jobs.Count + " suivis)";
        }

        public static string StateOf(string part)
        {
            foreach (Job j in jobs.Values)
                if (j.F != null && j.Key.Contains(part)) return j.Key + " = " + j.F.ActiveStateName;
            return "?";
        }
    }
}
