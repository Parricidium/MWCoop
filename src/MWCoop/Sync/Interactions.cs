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
            { "switch_*",     "Switch" },           // interrupteurs de la maison (switch_kitchen...)
            { "TVSwitch",     "Switch" },
            { "CDSwitch",     "Switch" },
            { "Eject",        "Switch" },
            { "ButtonLightModes", "Mode add" },     // phares des vehicules (CORRIS...)
            { "ButtonLightModes", "Test" },         // phares (tracteur...)
            { "ButtonWipers", "Test" },             // essuie-glaces
        };

        class Entry { public string Id; public PlayMakerFSM Fsm; }
        static readonly Dictionary<string, Entry> byId = new Dictionary<string, Entry>();
        static readonly HashSet<PlayMakerFSM> tracked = new HashSet<PlayMakerFSM>();
        static bool applying, scanned;
        static float scanAt = -1;

        // Action injectee : previent a l'entree de l'etat.
        class Hook : ModHook
        {
            public override string Module { get { return "interactions"; } }
            public Entry Target;
            public string StateName;
            public override void OnEnter()
            {
                try { if (!applying && Replay.Depth == 0) Send(Target, StateName); } catch (System.Exception e) { Replay.HookError(e); }
                Finish();
            }
        }

        public static void OnLevelLoaded()
        {
            byId.Clear(); tracked.Clear();
            scanned = false;
            scanAt = PlayerSync.InGame ? Time.realtimeSinceStartup + 3f : -1;
        }

        static readonly List<KeyValuePair<float, Entry>> checks = new List<KeyValuePair<float, Entry>>();

        public static void Update()
        {
            if (snapshots.Count > 0) SendSnapshots();
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
            // Nouveau passage toutes les 20 s : les objets lointains (inactifs) ne peuvent etre suivis
            // qu'une fois actives (leur automate n'est pas charge avant).
            if (scanAt < 0 || Time.realtimeSinceStartup < scanAt) return;
            scanAt = Time.realtimeSinceStartup + 20f;
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
                if (byId.ContainsKey(e.Id)) continue;
                if (Inject(e, target)) { byId[e.Id] = e; tracked.Add(e.Fsm); hooked++; }
            }
            if (hooked > 0) Log.Info("interactions : " + hooked + " objets de plus suivis (portes, interrupteurs...), " + byId.Count + " en tout");
        }

        static string AllowedTarget(PlayMakerFSM f)
        {
            string obj = f.gameObject.name;
            for (int i = 0; i < Allowed.GetLength(0); i++)
            {
                string pat = Allowed[i, 0];
                if (pat.EndsWith("*") ? !obj.StartsWith(pat.Substring(0, pat.Length - 1)) : pat != obj) continue;
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
            catch (Exception) { return false; }   // automate pas encore charge (objet inactif) : au prochain passage
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
            if (!byId.TryGetValue(id, out e) || e.Fsm == null) { if (state != "=etat") Log.Warn("interaction inconnue : " + id); return; }
            if (state == "=etat") { if (!Session.IsHost) ApplySnapshot(e, names, vals); return; }
            for (int i = 0; i < n; i++)
            {
                FsmBool b = e.Fsm.FsmVariables.GetFsmBool(names[i]);
                if (b != null) b.Value = vals[i];
            }
            applying = true; Replay.Depth++;
            try { Game.SetState(e.Fsm, state); }
            finally { applying = false; Replay.Depth--; }
            checks.Add(new KeyValuePair<float, Entry>(Time.realtimeSinceStartup + 2f, e));
            Log.Info("interaction de #" + who + " : " + id + " -> " + state + (e.Fsm.gameObject.activeInHierarchy ? "" : " (objet inactif)"));
        }

        // Arrivee d'un invite en cours de partie : l'hote lui envoie l'etat de tout ce qui est suivi.
        static readonly List<KeyValuePair<float, Peer>> snapshots = new List<KeyValuePair<float, Peer>>();
        public static void ScheduleSnapshot(Peer p) { snapshots.Add(new KeyValuePair<float, Peer>(Time.realtimeSinceStartup + 12f, p)); }

        static void SendSnapshots()
        {
            for (int i = snapshots.Count - 1; i >= 0; i--)
            {
                if (Time.realtimeSinceStartup < snapshots[i].Key) continue;
                Peer p = snapshots[i].Value;
                snapshots.RemoveAt(i);
                if (!p.Accepted) continue;
                int n = 0;
                foreach (Entry e in byId.Values)
                {
                    if (e.Fsm == null || e.Fsm.FsmVariables.BoolVariables.Length == 0) continue;
                    var w = new NetWriter(Msg.Interact).U8(0).Str(e.Id).Str("=etat");
                    Bools(e.Fsm, w);
                    Session.T.SendReliable(p, w.ToArray());
                    n++;
                }
                Log.Info("interactions : etat de " + n + " objets envoye a " + p);
            }
        }

        // Invite : objet dans un autre etat que chez l'hote -> on remet la variable decisive a
        // l'oppose de celle de l'hote puis on rejoue l'action : le jeu bascule (porte, interrupteur)
        // avec son animation et son son, et retombe sur l'etat de l'hote.
        static void ApplySnapshot(Entry e, string[] names, bool[] vals)
        {
            string decisive = null;
            for (int i = 0; i < names.Length; i++)
            {
                FsmBool b = e.Fsm.FsmVariables.GetFsmBool(names[i]);
                if (b == null || b.Value == vals[i]) continue;
                if (decisive == null) decisive = names[i];
                b.Value = vals[i];
            }
            if (decisive == null) return;
            FsmBool d = e.Fsm.FsmVariables.GetFsmBool(decisive);
            d.Value = !d.Value;
            applying = true; Replay.Depth++;
            try { Game.SetState(e.Fsm, AllowedTarget(e.Fsm)); }
            finally { applying = false; Replay.Depth--; }
            Log.Info("interactions : " + e.Id + " remis comme chez l'hote (" + decisive + ")");
        }

        // Automate que ce module prend (ou prendra des qu'il sera actif) : portes, interrupteurs...
        public static bool Wants(PlayMakerFSM f)
        {
            if (f.FsmName != "Use") return false;
            try { return AllowedTarget(f) != null; } catch { return false; }
        }

        public static bool Tracks(PlayMakerFSM f)
        {
            return tracked.Contains(f);
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

        // Essais : ouvre/ferme la porte la plus proche SANS rien envoyer (desynchronisation voulue).
        public static string TestLocalDoor(Vector3 pos)
        {
            applying = true; Replay.Depth++;
            try { return TestNearestDoor(pos); }
            finally { applying = false; Replay.Depth--; }
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
