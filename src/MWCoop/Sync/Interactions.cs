using System;
using System.Collections.Generic;
using HutongGames.PlayMaker;
using MWCoop.Net;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MWCoop
{
    // Portes, interrupteurs, tele... vus par tous. Les objets du decor portent un automate 'Use' :
    // l'etat 'Wait button' recoit USE au clic du joueur, puis la logique suit (ouvrir/fermer...).
    // Une action est injectee au debut de l'etat ou mene USE : quand le joueur local l'atteint,
    // on envoie (automate, etat, booleens de l'automate) ; chez les autres, les booleens sont
    // recopies puis l'automate est mis dans cet etat -> meme decision, meme animation, meme son.
    // Liste blanche : rien qui depense de l'argent, nourrit, soigne ou deplace le joueur.
    public static class Interactions
    {
        static readonly string[,] Allowed =
        {
            // objet          etat vise par USE
            { "Handle",       "Check position" },   // portes des maisons et batiments
            { "DoorHandle",   "Check position" },
            { "Trigger",      "Check position" },
            { "Mesh",         "Check position" },
            { "Switch",       "Sound" },            // interrupteurs, radiateurs
            { "TVSwitch",     "Switch" },
            { "CDSwitch",     "Switch" },
            { "Eject",        "Switch" },
            { "ButtonLightModes", "Mode add" },     // phares des vehicules (CORRIS...)
            { "ButtonLightModes", "Test" },         // phares (tracteur...)
            { "ButtonWipers", "Test" },             // essuie-glaces
        };

        class Entry { public string Id; public PlayMakerFSM Fsm; }
        static readonly Dictionary<string, Entry> byId = new Dictionary<string, Entry>();
        static bool applying, scanned;
        static float scanAt = -1;

        // Action injectee : previent a l'entree de l'etat.
        class Hook : FsmStateAction
        {
            public Entry Target;
            public string StateName;
            public override void OnEnter()
            {
                if (!applying) Send(Target, StateName);
                Finish();
            }
        }

        public static void OnLevelLoaded()
        {
            byId.Clear();
            scanned = false;
            scanAt = PlayerSync.InGame ? Time.realtimeSinceStartup + 3f : -1;
        }

        static readonly List<KeyValuePair<float, Entry>> checks = new List<KeyValuePair<float, Entry>>();

        public static void Update()
        {
            for (int i = checks.Count - 1; i >= 0; i--)
            {
                if (Time.realtimeSinceStartup < checks[i].Key) continue;
                Entry c = checks[i].Value;
                checks.RemoveAt(i);
                if (c.Fsm == null) continue;
                FsmBool open = c.Fsm.FsmVariables.GetFsmBool("DoorOpen");
                Log.Info("interaction " + c.Id + " : etat " + c.Fsm.ActiveStateName + (open != null ? ", DoorOpen " + open.Value : "")
                         + ", angle " + c.Fsm.transform.parent.localEulerAngles.ToString("F0"));
            }
            if (scanned || scanAt < 0 || Time.realtimeSinceStartup < scanAt) return;
            scanned = true;
            Scan();
        }

        static void Scan()
        {
            var seen = new Dictionary<string, int>();
            var all = new List<PlayMakerFSM>();
            foreach (Object o in Resources.FindObjectsOfTypeAll(typeof(PlayMakerFSM)))
            {
                var f = (PlayMakerFSM)o;
                if (f.hideFlags != HideFlags.None || f.FsmName != "Use") continue;
                string n = f.gameObject.name;
                if (n.Contains("(Clone)") || n.Contains("(itemx)")) continue;   // objets crees en jeu : ids differents
                all.Add(f);
            }
            // Ordre stable des deux cotes : chemin, puis rang parmi les homonymes.
            var paths = new List<KeyValuePair<string, PlayMakerFSM>>();
            foreach (PlayMakerFSM f in all) paths.Add(new KeyValuePair<string, PlayMakerFSM>(Recon.Path(f.transform), f));
            paths.Sort((a, b) => string.CompareOrdinal(a.Key, b.Key));
            int hooked = 0;
            foreach (KeyValuePair<string, PlayMakerFSM> kv in paths)
            {
                int k;
                seen.TryGetValue(kv.Key, out k);
                seen[kv.Key] = k + 1;
                string target = AllowedTarget(kv.Value);
                if (target == null) continue;
                var e = new Entry { Id = kv.Key + "#" + k, Fsm = kv.Value };
                if (Inject(e, target)) { byId[e.Id] = e; hooked++; }
            }
            Log.Info("interactions : " + hooked + " objets suivis (portes, interrupteurs...) sur " + all.Count + " automates Use");
        }

        static string AllowedTarget(PlayMakerFSM f)
        {
            string obj = f.gameObject.name;
            for (int i = 0; i < Allowed.GetLength(0); i++)
            {
                if (Allowed[i, 0] != obj) continue;
                foreach (FsmState s in f.Fsm.States)
                    foreach (FsmTransition t in s.Transitions)
                        if (t.EventName == "USE" && t.ToState == Allowed[i, 1]) return t.ToState;
            }
            return null;
        }

        static bool Inject(Entry e, string stateName)
        {
            FsmState s = e.Fsm.Fsm.GetState(stateName);
            if (s == null) return false;
            try
            {
                var list = new List<FsmStateAction>(s.Actions);
                list.Insert(0, new Hook { Target = e, StateName = stateName });
                s.Actions = list.ToArray();
                return true;
            }
            catch (Exception ex) { Log.Warn("injection " + e.Id + " : " + ex.Message); return false; }
        }

        static void Send(Entry e, string state)
        {
            if (!Session.Active) return;
            var w = new NetWriter(Msg.Interact).U8(Session.LocalId).Str(e.Id).Str(state);
            Bools(e.Fsm, w);
            Session.SendAll(w, true);
        }

        static void Bools(PlayMakerFSM f, NetWriter w)
        {
            FsmBool[] bools = f.FsmVariables.BoolVariables;
            w.U8(bools.Length);
            foreach (FsmBool b in bools) w.Str(b.Name).Bool(b.Value);
        }

        public static void OnMessage(Peer from, NetReader r)
        {
            int who = r.U8();
            if (Session.IsHost) who = from.Id;
            string id = r.Str(), state = r.Str();
            int n = r.U8();
            var names = new string[n];
            var vals = new bool[n];
            for (int i = 0; i < n; i++) { names[i] = r.Str(); vals[i] = r.Bool(); }
            if (Session.IsHost)
            {
                var w = new NetWriter(Msg.Interact).U8(who).Str(id).Str(state).U8(n);
                for (int i = 0; i < n; i++) w.Str(names[i]).Bool(vals[i]);
                Session.Broadcast(w, true, who);
            }
            Entry e;
            if (!byId.TryGetValue(id, out e) || e.Fsm == null) { Log.Warn("interaction inconnue : " + id); return; }
            for (int i = 0; i < n; i++)
            {
                FsmBool b = e.Fsm.FsmVariables.GetFsmBool(names[i]);
                if (b != null) b.Value = vals[i];
            }
            applying = true;
            try { Game.SetState(e.Fsm, state); }
            finally { applying = false; }
            checks.Add(new KeyValuePair<float, Entry>(Time.realtimeSinceStartup + 2f, e));
            Log.Info("interaction de #" + who + " : " + id + " -> " + state + (e.Fsm.gameObject.activeInHierarchy ? "" : " (objet inactif)"));
        }

        // Essais : actionne l'objet suivi dont l'identifiant contient 'part' (comme un clic).
        public static string TestNamed(string part)
        {
            foreach (Entry e in byId.Values)
            {
                if (e.Fsm == null || !e.Id.Contains(part)) continue;
                string target = AllowedTarget(e.Fsm);
                Game.SetState(e.Fsm, target);
                checks.Add(new KeyValuePair<float, Entry>(Time.realtimeSinceStartup + 2f, e));
                return e.Id + " -> " + target;
            }
            return "rien pour " + part;
        }

        // Essais : declenche la premiere porte proche du joueur (comme un clic).
        public static string TestNearestDoor(Vector3 pos)
        {
            Entry best = null;
            float bd = float.MaxValue;
            foreach (Entry e in byId.Values)
            {
                if (e.Fsm == null || !e.Fsm.gameObject.activeInHierarchy || AllowedTarget(e.Fsm) != "Check position") continue;
                float d = (e.Fsm.transform.position - pos).sqrMagnitude;
                if (d < bd) { bd = d; best = e; }
            }
            if (best == null) return null;
            Game.SetState(best.Fsm, "Check position");   // la Hook envoie, comme pour un vrai clic
            checks.Add(new KeyValuePair<float, Entry>(Time.realtimeSinceStartup + 2f, best));
            return best.Id;
        }
    }
}
