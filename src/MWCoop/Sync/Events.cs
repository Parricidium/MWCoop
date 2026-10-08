using System.Collections.Generic;
using HutongGames.PlayMaker;
using MWCoop.Net;
using UnityEngine;

namespace MWCoop
{
    // Evenements du monde decides par l'hote (demande de JD, 07/10 : « pour tous les evenements de ce genre, qu'ils
    // soient synchronises »). La fete chez Jokke (JOBS/JOKKEHOME/HouseDrunkNew::OpeningHours) se decidait chez chacun :
    // jour et heure (communs), mais aussi la distance au joueur LOCAL (GetDistance vers PLAYER) et un tirage au sort --
    // l'invite, tout pres, voyait la fete, porte ouverte ; l'hote, piece eteinte et vide (vraie partie du 07/10).
    // Concerne : dans les lieux du monde (Places : boulots, villages, magasins, garage, salle des fetes...), les automates
    // que personne d'autre ne suit, sans commande du joueur (clic, touche, declencheur), qui MESURENT LA DISTANCE AU
    // JOUEUR et allument ou eteignent des objets (ActivateGameObject) -- et les valeurs qu'ils reglent ailleurs
    // (SetFsmBool/Float/Int). Pas les vehicules, les PNJ (Npcs), les LOD (affichage selon la camera de chacun), ni les
    // clignotements. (Une premiere regle -- tout tirage au sort, partout -- prenait 219 automates, dont la navigation des
    // voitures de course et l'evanouissement du joueur, et figeait le jeu 4 s a chaque releve.)
    //  - Hote : ils tournent comme d'habitude, mais leurs mesures de distance visent le joueur le plus proche (NearDoors) ;
    //    il envoie le RESULTAT -- chaque objet allume ou eteint, chaque valeur reglee -- a chaque changement (2 fois par
    //    seconde au plus) et en entier toutes les 10 s (arrivants).
    //  - Invite : ces automates sont arretes ; il pose ce que l'hote envoie. (Rejouer leurs etats ne suffisait pas : un
    //    etat rejoue enchainait sur la logique de l'invite, sa distance et son tirage.)
    public static class Events
    {
        const int T_ACTIVE = 0, T_BOOL = 1, T_FLOAT = 2, T_INT = 3;
        class Target { public int Kind; public GameObject Go; public NamedVariable Var; }
        class Ev { public string Key; public PlayMakerFSM F; public List<Target> T = new List<Target>(); public string Last; }

        static readonly List<Ev> evs = new List<Ev>();
        static readonly Dictionary<string, Ev> byKey = new Dictionary<string, Ev>();
        static readonly HashSet<PlayMakerFSM> seen = new HashSet<PlayMakerFSM>();
        static readonly string[] Places = { "JOBS", "PERAJARVI", "STORE_AREA", "PERAPORTTI", "REPAIRSHOP", "DANCEHALL", "WATERFACILITY", "LANDFILL",
                                            "FleaMarket", "CABIN", "COTTAGE", "SOCCER" };
        static readonly HashSet<string> SkipNames = new HashSet<string> { "LOD", "Paint", "Flicker", "Blink", "Death", "HeadForce" };
        static readonly HashSet<string> Input = new HashSet<string> { "MousePickEvent", "GetButtonDown", "GetButtonUp", "GetMouseButtonDown", "GetMouseButtonUp", "GetAxis", "GetKeyDown", "GetKey", "GetButton", "AnyKeyStoreString", "TriggerEvent", "CollisionEvent" };
        static float scanAt = -1, nextScan, nextSend, nextFull;
        static int scans;

        public static void OnLevelLoaded()
        {
            evs.Clear(); byKey.Clear(); seen.Clear(); scans = 0; todo.Clear(); placeAt = 0; todoAt = 0;
            // (avant le releve du monde, a 16 s : ces automates sont reserves ici)
            scanAt = PlayerSync.InGame ? Time.realtimeSinceStartup + 12f : -1;
            nextScan = 0;
        }

        // Releve etale sur plusieurs images (120 automates par image : un releve d'un coup figeait le jeu 1 s).
        static readonly List<PlayMakerFSM> todo = new List<PlayMakerFSM>();
        static readonly List<string> names = new List<string>();
        static int todoAt, before;

        static void StartScan()
        {
            todo.Clear(); names.Clear(); todoAt = 0; before = evs.Count; placeAt = 0;
            NextPlace();
        }

        // Un lieu par image (GetComponentsInChildren de tous d'un coup : 90 ms).
        static int placeAt;
        static void NextPlace()
        {
            for (; placeAt < Places.Length && todoAt >= todo.Count; placeAt++)
            {
                GameObject r = Game.FindAny(Places[placeAt]);
                if (r != null) todo.AddRange(r.GetComponentsInChildren<PlayMakerFSM>(true));
            }
        }

        static void ScanSome()
        {
            int end = Mathf.Min(todo.Count, todoAt + 120);
            for (; todoAt < end; todoAt++) Consider(todo[todoAt]);
            if (todoAt < todo.Count) return;
            if (placeAt < Places.Length) { NextPlace(); return; }
            todo.Clear();
            if (evs.Count != before)
                Log.Info("evenements : " + (evs.Count - before) + " de plus decides par l'hote (" + evs.Count + " en tout) : " + string.Join(", ", names.ToArray()));
        }

        static void Consider(PlayMakerFSM f)
        {
                if (f == null || seen.Contains(f)) return;
                if (Replay.Owner(f) != null || SkipNames.Contains(f.FsmName) || Odd(f.gameObject.name)) { seen.Add(f); return; }
                Ev ev;
                bool loaded = Read(f, out ev);
                if (!loaded) return;   // (actions pas encore chargees : au prochain releve)
                seen.Add(f);
                if (ev != null && Npcs.IsNpcLogic(f.transform)) return;

                if (ev == null || !Replay.Claim(f, "evenements")) return;
                string path = Recon.Path(f.transform) + "::" + f.FsmName;
                int n = 0;
                while (byKey.ContainsKey(path + "#" + n)) n++;
                ev.Key = path + "#" + n;
                evs.Add(ev);
                byKey[ev.Key] = ev;
                if (Session.IsHost) NearDoors.Retarget(f);
                else f.enabled = false;
                names.Add(ev.Key + " (" + ev.T.Count + ")");
        }

        // Clignotements ; reapprovisionnement des magasins (Stock, Shop) ; banc de puissance (il accroche la voiture du
        // joueur qui s'en sert).
        static bool Odd(string n) { return n.Contains("Strobo") || n.Contains("Flicker") || n.Contains("Blink") || n.StartsWith("INVENTORY") || n == "AttachPoint"; }

        // Un evenement ? (faux : actions pas chargees ; ev null : pas un evenement)
        static bool Read(PlayMakerFSM f, out Ev ev)
        {
            ev = null;
            FsmState[] states;
            try { states = f.Fsm.States; } catch { return false; }
            // Evenements globaux envoyes d'ailleurs (PAID : l'argent donne au vendeur de la Rivett, UNLOADED : le bois
            // decharge, GREETINGS : le salut) : des gestes du joueur. Arrete chez l'invite, l'automate les perdait : la
            // Rivett achetee par un invite restait a vendre (pas de cles, flechette restee sur la carte des parents --
            // retour d'un joueur, 08/10). Laisse au monde (WorldFsms), qui rejoue ses transitions chez tous.
            try { foreach (FsmTransition g in f.Fsm.GlobalTransitions) if (g.EventName != "SAVEGAME") return true; }
            catch { return false; }
            bool near = false, activates = false;
            var targets = new List<Target>();
            foreach (FsmState st in states)
            {
                FsmStateAction[] acts;
                try { acts = st.Actions; } catch { return false; }
                if (acts == null) return false;
                foreach (FsmStateAction a in acts)
                {
                    if (a == null) continue;
                    string tn = a.GetType().Name;
                    if (Input.Contains(tn)) return true;   // commande du joueur : le monde (WorldFsms) rejoue ses actions
                    if (tn == "GetDistance")
                    {
                        var tgt = a.GetType().GetField("target").GetValue(a) as FsmGameObject;
                        if (tgt != null && tgt.Value != null && tgt.Value.name == "PLAYER") near = true;
                    }
                    if (tn == "ActivateGameObject") activates = true;
                }
            }
            if (!near || !activates) return true;
            foreach (FsmState st in states)
                foreach (FsmStateAction a in st.Actions)
                {
                    if (a == null) continue;
                    string tn = a.GetType().Name;
                    if (tn == "ActivateGameObject" || tn.StartsWith("SetFsm")) { try { AddTarget(f, a, targets); } catch { } }
                }
            if (!targets.Exists(x => x.Kind == T_ACTIVE)) return true;
            ev = new Ev { F = f, T = targets };
            return true;
        }

        static void AddTarget(PlayMakerFSM f, FsmStateAction a, List<Target> list)
        {
            var act = a as HutongGames.PlayMaker.Actions.ActivateGameObject;
            if (act != null)
            {
                GameObject go = f.Fsm.GetOwnerDefaultTarget(act.gameObject);
                if (Ok(go) && !list.Exists(x => x.Kind == T_ACTIVE && x.Go == go)) list.Add(new Target { Kind = T_ACTIVE, Go = go });
                return;
            }
            GameObject g = null; string fsm = null, name = null; int kind = -1;
            var sb = a as HutongGames.PlayMaker.Actions.SetFsmBool;
            var sf = a as HutongGames.PlayMaker.Actions.SetFsmFloat;
            var si = a as HutongGames.PlayMaker.Actions.SetFsmInt;
            if (sb != null) { g = f.Fsm.GetOwnerDefaultTarget(sb.gameObject); fsm = sb.fsmName.Value; name = sb.variableName.Value; kind = T_BOOL; }
            else if (sf != null) { g = f.Fsm.GetOwnerDefaultTarget(sf.gameObject); fsm = sf.fsmName.Value; name = sf.variableName.Value; kind = T_FLOAT; }
            else if (si != null) { g = f.Fsm.GetOwnerDefaultTarget(si.gameObject); fsm = si.fsmName.Value; name = si.variableName.Value; kind = T_INT; }
            if (kind < 0 || !Ok(g) || string.IsNullOrEmpty(name)) return;
            PlayMakerFSM tf = null;
            foreach (PlayMakerFSM x in g.GetComponents<PlayMakerFSM>()) if (string.IsNullOrEmpty(fsm) || x.FsmName == fsm) { tf = x; break; }
            if (tf == null) return;
            NamedVariable v = kind == T_BOOL ? (NamedVariable)tf.FsmVariables.FindFsmBool(name) : kind == T_FLOAT ? (NamedVariable)tf.FsmVariables.FindFsmFloat(name) : tf.FsmVariables.FindFsmInt(name);
            if (v != null && !list.Exists(x => x.Var == v)) list.Add(new Target { Kind = kind, Go = g, Var = v });
        }

        // Cible partagee : dans le monde, pas sur le joueur, son interface ou nos objets.
        static bool Ok(GameObject go)
        {
            if (go == null) return false;
            string r = Game.RootName(go.transform);
            return r != "PLAYER" && r != "GUI" && !r.StartsWith("MWCoop");
        }

        public static void Update()
        {
            if (!Session.Active || scanAt < 0) return;
            float now = Time.realtimeSinceStartup;
            // (releve a 12 s, puis 40 s apres : automates d'objets actives plus tard)
            if (todo.Count > 0 || placeAt < Places.Length && scans > 0) ScanSome();
            else if (now >= scanAt && now >= nextScan && scans < 2) { nextScan = now + 40f; scans++; StartScan(); }
            if (!Session.IsHost || evs.Count == 0 || Session.RemoteCount == 0 || now < nextSend) return;
            nextSend = now + 0.5f;
            bool full = now >= nextFull;
            if (full) nextFull = now + 10f;
            foreach (Ev e in evs)
            {
                if (e.F == null) continue;
                string sig = Signature(e);
                if (!full && sig == e.Last) continue;
                bool changed = sig != e.Last;
                e.Last = sig;
                var w = new NetWriter(Msg.Event).Str(e.Key).U8(e.T.Count);
                foreach (Target t in e.T) Write(w, t);
                Session.SendAll(w, changed);
                if (changed && logged++ < 40) Log.Info("evenements : " + e.Key + " -> " + sig + " (etat " + e.F.ActiveStateName + ")");
            }
        }
        static int logged;

        static string Signature(Ev e)
        {
            var sb = new System.Text.StringBuilder();
            foreach (Target t in e.T)
            {
                if (t.Kind == T_ACTIVE) sb.Append(t.Go != null && t.Go.activeSelf ? '1' : '0');
                else if (t.Kind == T_BOOL) sb.Append(((FsmBool)t.Var).Value ? 'v' : 'f');
                else if (t.Kind == T_FLOAT) sb.Append(((FsmFloat)t.Var).Value.ToString("F2"));
                else sb.Append(((FsmInt)t.Var).Value);
                sb.Append(' ');
            }
            return sb.ToString().TrimEnd();
        }

        static void Write(NetWriter w, Target t)
        {
            if (t.Kind == T_ACTIVE) w.Bool(t.Go != null && t.Go.activeSelf);
            else if (t.Kind == T_BOOL) w.Bool(((FsmBool)t.Var).Value);
            else if (t.Kind == T_FLOAT) w.F32(((FsmFloat)t.Var).Value);
            else w.I32(((FsmInt)t.Var).Value);
        }

        public static void OnMessage(Peer from, NetReader r)
        {
            if (Session.IsHost) return;   // (seul l'hote decide)
            string key = r.Str();
            int n = r.U8();
            Ev e;
            if (!byKey.TryGetValue(key, out e) || e.T.Count != n) return;   // (pas encore releve ici : au prochain envoi complet)
            if (e.F != null && e.F.enabled) e.F.enabled = false;
            int changed = 0;
            foreach (Target t in e.T)
            {
                if (t.Kind == T_ACTIVE) { bool on = r.Bool(); if (t.Go != null && t.Go.activeSelf != on) { t.Go.SetActive(on); changed++; } }
                else if (t.Kind == T_BOOL) { bool b = r.Bool(); if (((FsmBool)t.Var).Value != b) { ((FsmBool)t.Var).Value = b; changed++; } }
                else if (t.Kind == T_FLOAT) { float f = r.F32(); if (Mathf.Abs(((FsmFloat)t.Var).Value - f) > 0.001f) { ((FsmFloat)t.Var).Value = f; changed++; } }
                else { int i = r.I32(); if (((FsmInt)t.Var).Value != i) { ((FsmInt)t.Var).Value = i; changed++; } }
            }
            if (changed > 0 && logged++ < 40) Log.Info("evenements : " + key + " comme chez l'hote (" + changed + " changement(s))");
        }

        // Essais : etat d'un evenement (cle contenant 'part').
        public static string Describe(string part)
        {
            foreach (Ev e in evs)
                if (e.Key.Contains(part)) return e.Key + " : " + Signature(e) + " (etat " + (e.F != null ? e.F.ActiveStateName : "?") + (e.F != null && !e.F.enabled ? ", arrete" : "") + ")";
            return "aucun evenement " + part + " (" + evs.Count + " suivis)";
        }
    }
}
