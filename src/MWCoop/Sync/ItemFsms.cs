using System.Collections.Generic;
using System.Reflection;
using HutongGames.PlayMaker;
using MWCoop.Net;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MWCoop
{
    // Etat interne des objets portables « (itemx) » : lampe de poche et lanterne allumees ou eteintes, chargeur de
    // batterie en marche, et tout objet du meme genre qu'un joueur actionne d'un clic. Personne ne les suivait : le monde
    // (WorldFsms.Skip) et les boulots (Jobs) ecartent tout ce qui s'appelle « (itemx) » / « (item », Interactions aussi ;
    // Props ne fait suivre que la pose, Consume que les fins (mange, bu, jete) -- relecture du 15/10, ecart n2.
    //  - Objets : ceux que Props connait (ID de l'automate -- beercase1, ou cle « w:<chemin>#rang » d'un objet unique du
    //    monde) ; leurs automates de la liste FsmNames, sur l'objet lui-meme (les declencheurs enfants, « Trigger »,
    //    « Switch », restent au monde et a Interactions). Pas ceux qu'un module dedie mene deja (SkipObjects : kilju,
    //    cafe, vetements, CD, telecommande...), ni ce qu'un autre module a pris (Replay.Claim).
    //  - Comme le monde : une transition qui SORT d'un etat ecoutant une PRESSION (GetButtonDown, MousePickEvent
    //    mouseDown...) par l'evenement de cette pression est l'action du joueur ; elle part (objet, automate, etat de
    //    depart, evenement, etat d'arrivee, variables simples). Le survol (mouseOver / mouseOff) ne part pas. Rien qui
    //    touche au joueur ou a son interface, ni ce qui cree ou detruit un objet (Consume, Boxes), etat d'arrivee et
    //    enchainement automatique compris (Safe).
    //  - Chez les autres : variables recopiees, puis meme evenement si l'automate est dans le meme etat de depart, sinon
    //    recalage sur l'etat d'arrivee (comme Interactions / WorldFsms), sous Replay.Depth : rien n'est renvoye.
    //  - Arrivant : 20 s apres son entree en jeu, l'hote lui rejoue la derniere action de chaque automate qui a bouge
    //    depuis le chargement (une lampe allumee le reste), un message par automate, sous Transport.MaxPayload.
    //  - Canal des evenements (Msg.Event), cle « objet:<ID> » : aiguille dans Events.OnMessage comme « vitre: ».
    public static class ItemFsms
    {
        public const string Prefix = "objet:";
        const string ModName = "objets-etat";

        // Automates d'un objet a suivre (a etendre au besoin). Pas « Data » : Consume refuse un 'Data' pris par un autre
        // module (ClaimedByOther, Consume.cs) et Parts tient ceux des pieces -- le prendre ici leur cassait le suivi des fins.
        static readonly string[] FsmNames = { "Use", "Switch", "Light", "Power" };

        // Objets menes par un module dedie (debut du nom) : jamais pris ici.
        static readonly string[] SkipObjects =
        {
            "bucket(item", "bucket lid(item",                 // kilju : Home
            "coffee pan(item", "coffee cup(item",             // cafe : Cooking (et Shop, Wear pour la tasse)
            "winter jacket(item", "winter coverall(item", "helmet(item",   // vetements : Wear
            "cd case(item", "cd(item", "tv remote control(item",            // CD et telecommande : WorldFsms (exceptions a sa regle)
            "gasoline(item", "diesel(item", "empty plastic can(item",       // bidons : Fluids
            "sofa(item", "sleeping bag(item",                 // sommeil : SleepingBags
            "car jack(item", "floor jack(item", "motor hoist(item",         // atelier : Garage
            "shopping bag",                                   // sacs : Consume (Spawn one / Spawn all)
        };

        // Actions de saisie (comme WorldFsms.InputActions) ; parmi elles, celles d'une PRESSION. MousePickEvent : seuls ses
        // champs mouseDown / mouseUp sont une pression (mouseOver / mouseOff : survol, « Wait player » <-> « Wait button »).
        static readonly HashSet<string> InputActions = new HashSet<string> { "MousePickEvent", "GetButtonDown", "GetButtonUp", "GetMouseButtonDown", "GetMouseButtonUp", "GetAxis", "GetKeyDown", "GetKeyUp", "GetButton", "GetKey" };
        static readonly HashSet<string> PressActions = new HashSet<string> { "GetButtonDown", "GetButtonUp", "GetMouseButtonDown", "GetMouseButtonUp", "GetKeyDown", "GetKeyUp" };
        static readonly HashSet<string> HoverFields = new HashSet<string> { "mouseOver", "mouseOff" };
        // Etats d'arrivee jamais envoyes : attentes du survol (le monde les ecarte aussi) et fins d'articles (Consume).
        static readonly HashSet<string> SkipStates = new HashSet<string> { "Wait player", "Wait button", "Mouse off", "Mouse off 2", "Destroy", "Destroy 2", "Is garbage", "Empty", "Remove bottle", "Spawn one", "Spawn all", "Add inventory" };
        static readonly HashSet<string> PersonalRoots = new HashSet<string> { "PLAYER", "GUI", "Sheets", "COMPUTER" };

        class Tracked
        {
            public string Id, Name; public PlayMakerFSM F;
            public string LoadState;                           // etat au crochet (= au chargement) : l'arrivant y est aussi
            public string LastPrev, LastEvent, LastState;      // derniere action (d'ici ou recue) : rejouee pour un arrivant
            public HashSet<string> InputStates = new HashSet<string>();
            public Dictionary<string, HashSet<string>> InputEvents = new Dictionary<string, HashSet<string>>();   // tous (survol compris) : bornent l'enchainement
            public Dictionary<string, HashSet<string>> PressEvents = new Dictionary<string, HashSet<string>>();   // pressions seules : l'action du joueur
            public HashSet<string> Personal = new HashSet<string>();
            public Dictionary<string, bool> SafeCache = new Dictionary<string, bool>();
            public HashSet<string> Entered = new HashSet<string>();   // etats traverses pendant un rejeu
            public float WindowStart, PausedUntil; public int Count;
        }

        class Vars
        {
            public List<KeyValuePair<string, bool>> B = new List<KeyValuePair<string, bool>>();
            public List<KeyValuePair<string, int>> I = new List<KeyValuePair<string, int>>();
            public List<KeyValuePair<string, float>> F = new List<KeyValuePair<string, float>>();
        }

        // Message recu pour un objet pas encore suivi ici (achete a l'instant, zone pas chargee) : repris 5 s.
        class Late { public float Until; public string Key, Id, Fsm, Prev, Ev, State, Cur; public int Who, Kind; public Vars V; }

        static readonly Dictionary<string, Tracked> byKey = new Dictionary<string, Tracked>();   // « ID::automate »
        static readonly HashSet<PlayMakerFSM> hooked = new HashSet<PlayMakerFSM>();
        static readonly HashSet<PlayMakerFSM> rejected = new HashSet<PlayMakerFSM>();
        static readonly List<Late> late = new List<Late>();
        static readonly List<KeyValuePair<float, Peer>> snapshots = new List<KeyValuePair<float, Peer>>();
        static bool applying;
        static float nextScan = -1, nextWarn;
        static int sent, recv, tooLong;

        class Hook : ModHook
        {
            public override string Module { get { return ModName; } }
            public Tracked T; public string StateName;
            public override void OnEnter()
            {
                try
                {
                    if (Replay.Depth > 0) T.Entered.Add(StateName);
                    else if (!applying) OnLocal(T, StateName);
                }
                catch (System.Exception e) { Replay.HookError(e); }
                Finish();
            }
        }

        public static void OnLevelLoaded()
        {
            byKey.Clear(); hooked.Clear(); rejected.Clear(); late.Clear(); snapshots.Clear();
            applying = false; sent = recv = tooLong = 0;
            // (apres Props a 10 s -- les cles « w: » sont prises quand tout est a sa place --, Consume a 11 s et le monde a 16 s)
            nextScan = PlayerSync.InGame ? Time.realtimeSinceStartup + 18f : -1;
        }

        public static bool Tracks(PlayMakerFSM f) { return hooked.Contains(f); }

        public static void ScheduleSnapshot(Peer p) { if (Session.IsHost) snapshots.Add(new KeyValuePair<float, Peer>(Time.realtimeSinceStartup + 20f, p)); }

        public static void Update()
        {
            if (!Session.Active || nextScan < 0) return;
            float now = Time.realtimeSinceStartup;
            if (snapshots.Count > 0) SendSnapshots(now);
            if (late.Count > 0) RetryLate(now);
            // (toutes les 20 s : objets achetes ou crees depuis, objets poses -- tenu en main, un objet unique n'a pas encore de cle)
            if (now < nextScan) return;
            nextScan = now + 20f;
            Scan();
        }

        // ---------------------------------------------------------------- choix des automates
        static void Scan()
        {
            int before = byKey.Count, added = 0;
            foreach (string name in FsmNames)
                foreach (Object o in Game.FsmsNamed(name))
                {
                    var f = (PlayMakerFSM)o;
                    if (f == null || f.hideFlags != HideFlags.None || f.FsmName != name) continue;
                    if (hooked.Contains(f) || rejected.Contains(f)) continue;
                    GameObject go = f.gameObject;
                    if (go.name.IndexOf("(item", System.StringComparison.Ordinal) < 0) continue;   // (le reste : monde, Interactions, Jobs)
                    if (!go.transform.root.gameObject.activeInHierarchy) continue;                 // modeles (prefabs)
                    string id = IdOf(go);
                    if (id == null) continue;   // (pas de cle : tenu en main, ou pas encore releve par Props) : au prochain passage
                    if (Consider(f, id)) added++;
                }
            if (added > 0) Log.Info("objets : " + (before == 0 ? added + " automates d'objets suivis" : added + " automates d'objets de plus suivis (" + byKey.Count + " en tout)") + " (lampe, lanterne, chargeur... : etat partage)");
        }

        // Cle Props de l'objet : son ID (articles), sinon la cle « w: » d'un objet unique du monde ; null : pas un objet suivi.
        static string IdOf(GameObject go)
        {
            string id = Props.ItemId(go);
            if (id.Length > 0) return id;
            Rigidbody rb = go.GetComponent<Rigidbody>();
            return rb != null ? Props.WorldKeyOf(rb) : null;
        }

        static bool Skipped(string objName)
        {
            foreach (string s in SkipObjects) if (objName.StartsWith(s, System.StringComparison.Ordinal)) return true;
            return false;
        }

        // Vrai : accroche. Faux : ecarte pour de bon (rejected) ou pas encore pret (automate pas initialise : au prochain passage).
        static bool Consider(PlayMakerFSM f, string id)
        {
            GameObject go = f.gameObject;
            if (Skipped(go.name) || go.GetComponent<Rigidbody>() == null) { rejected.Add(f); return false; }
            if (Replay.ClaimedByOther(f, ModName) || Interactions.Tracks(f) || Interactions.Wants(f) || WorldFsms.Tracks(f) || Jobs.Tracks(f)) { rejected.Add(f); return false; }
            string key = id + "::" + f.FsmName;
            if (byKey.ContainsKey(key)) { rejected.Add(f); return false; }   // (homonyme sous la meme cle : n'arrive pas)
            return TryHook(f, id, key);
        }

        static bool TryHook(PlayMakerFSM f, string id, string key)
        {
            if (f.Fsm == null || !f.Fsm.Initialized) return false;   // (pas encore charge : au prochain passage, sans l'avertissement de PlayMaker)
            var t = new Tracked { Id = id, Name = f.FsmName, F = f };
            bool loaded;
            try { loaded = Classify(f, t); } catch { return false; }
            if (!loaded) return false;
            if (t.PressEvents.Count == 0 || !AnySafeTarget(t)) { rejected.Add(f); return false; }   // rien qu'un joueur declenche, ou rien de rejouable
            if (!Replay.Claim(f, ModName)) { rejected.Add(f); return false; }
            t.LoadState = f.ActiveStateName;
            byKey[key] = t;
            hooked.Add(f);
            try { foreach (FsmState st in f.Fsm.States) FsmHooks.Prepend(st, new Hook { T = t, StateName = st.Name }); }
            catch (System.Exception e) { Replay.HookError(e); }
            return true;
        }

        // Lit les actions : saisies et pressions par etat, etats qui touchent au joueur (ou creent / detruisent un objet).
        // Faux : actions pas chargees (objet inactif).
        static bool Classify(PlayMakerFSM f, Tracked t)
        {
            GameObject self = f.gameObject;
            foreach (FsmState st in f.Fsm.States)
            {
                FsmStateAction[] acts = st.Actions;
                if (acts == null) return false;
                var inEv = new HashSet<string>();
                var prEv = new HashSet<string>();
                bool input = false, personal = false;
                foreach (FsmStateAction a in acts)
                {
                    if (a == null) continue;
                    string tn = a.GetType().Name;
                    bool pick = tn == "MousePickEvent";
                    bool isInput = InputActions.Contains(tn);
                    bool press = pick || PressActions.Contains(tn);
                    if (isInput) input = true;
                    // Creation et destruction d'objets : Consume et Boxes (sacs, boites de pieces, bouteilles) ; eteindre
                    // l'objet lui-meme aussi (c'est une fin). Eteindre un enfant (la lampe) reste une action normale.
                    if (tn == "CreateObject" || tn == "DestroySelf" || tn == "DestroyObject" || tn == "DestroyComponent") personal = true;
                    var ag = a as HutongGames.PlayMaker.Actions.ActivateGameObject;
                    if (ag != null && ag.activate != null && !ag.activate.Value && ag.gameObject != null
                        && (ag.gameObject.OwnerOption == OwnerDefaultOption.UseOwner || ag.gameObject.GameObject.Value == self)) personal = true;
                    foreach (FieldInfo fi in a.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
                    {
                        object v = fi.GetValue(a);
                        if (v == null) continue;
                        var ev = v as FsmEvent;
                        if (ev != null)
                        {
                            if (string.IsNullOrEmpty(ev.Name)) continue;
                            if (ev.Name == "SAVEGAME") personal = true;
                            if (isInput) inEv.Add(ev.Name);
                            if (press && !(pick && HoverFields.Contains(fi.Name))) prEv.Add(ev.Name);
                            continue;
                        }
                        GameObject go = null;
                        if (v is FsmGameObject) go = ((FsmGameObject)v).Value;
                        else if (v is FsmOwnerDefault) { var od = (FsmOwnerDefault)v; if (od.OwnerOption != OwnerDefaultOption.UseOwner && od.GameObject != null) go = od.GameObject.Value; }
                        if (go != null && PersonalRoots.Contains(Game.RootName(go.transform))) personal = true;
                        // Globales du joueur (PlayerThirst, PlayerMoney, PlayerStress...) : boire, manger, payer -- a chacun le sien.
                        // Plus strict que le monde (toute globale Player*) : un objet qu'on allume n'y touche pas.
                        var nv = v as NamedVariable;
                        if (nv != null && nv.UseVariable && nv.Name.StartsWith("Player") && !Game.LocalVar(f, nv.Name)) personal = true;
                        if (v is FsmString && ((FsmString)v).Value == "SAVEGAME") personal = true;
                    }
                }
                if (input) t.InputStates.Add(st.Name);
                t.InputEvents[st.Name] = inEv;
                if (prEv.Count > 0) t.PressEvents[st.Name] = prEv;
                if (personal) t.Personal.Add(st.Name);
            }
            return true;
        }

        // Au moins une pression mene a un etat rejouable.
        static bool AnySafeTarget(Tracked t)
        {
            foreach (KeyValuePair<string, HashSet<string>> kv in t.PressEvents)
            {
                FsmState st = t.F.Fsm.GetState(kv.Key);
                if (st == null) continue;
                foreach (FsmTransition tr in st.Transitions)
                    if (kv.Value.Contains(tr.EventName) && !SkipStates.Contains(tr.ToState) && Safe(t, tr.ToState)) return true;
            }
            return false;
        }

        // L'etat 'state' et tout ce qui s'enchaine automatiquement apres lui (pas ce qui attend le joueur) laissent-ils le
        // joueur et ses objets tranquilles ? (WorldFsms.Safe)
        static bool Safe(Tracked t, string state)
        {
            bool r;
            if (t.SafeCache.TryGetValue(state, out r)) return r;
            var seen = new HashSet<string>();
            var todo = new Stack<string>();
            todo.Push(state);
            r = true;
            while (todo.Count > 0 && r)
            {
                string s = todo.Pop();
                if (!seen.Add(s)) continue;
                if (t.Personal.Contains(s)) { r = false; break; }
                FsmState st = t.F.Fsm.GetState(s);
                if (st == null) continue;
                HashSet<string> inEv;
                t.InputEvents.TryGetValue(s, out inEv);
                foreach (FsmTransition tr in st.Transitions)
                    if (inEv == null || !inEv.Contains(tr.EventName)) todo.Push(tr.ToState);   // automatique
            }
            t.SafeCache[state] = r;
            return r;
        }

        // ---------------------------------------------------------------- actions du joueur local
        static void OnLocal(Tracked t, string state)
        {
            if (!Session.Active || Session.RemoteCount == 0 || t.F == null) return;
            FsmTransition tr = t.F.Fsm.LastTransition;
            FsmState prev = t.F.Fsm.PreviousActiveState;
            if (tr == null || prev == null || tr.ToState != state) return;
            HashSet<string> pr;
            if (!t.PressEvents.TryGetValue(prev.Name, out pr) || !pr.Contains(tr.EventName)) return;   // pas une pression du joueur (survol, logique)
            if (SkipStates.Contains(state) || !Safe(t, state)) return;
            float now = Time.realtimeSinceStartup;
            if (now < t.PausedUntil) return;
            if (now - t.WindowStart > 10f) { t.WindowStart = now; t.Count = 0; }
            if (++t.Count > 20) { t.PausedUntil = now + 30f; Log.Warn("objets : " + t.Id + "::" + t.Name + " change trop souvent, en pause 30 s"); return; }
            t.LastPrev = prev.Name; t.LastEvent = tr.EventName; t.LastState = state;
            Session.SendAll(Build(t, Session.LocalId, 0, prev.Name, tr.EventName, state, ""), true);
            if (++sent <= 30 || sent % 50 == 0) Log.Info("objets : " + t.Id + "::" + t.Name + " " + prev.Name + " -" + tr.EventName + "-> " + state + " envoye");
        }

        // Cle, joueur, automate, genre (0 action, 1 instantane), etat de depart, evenement, etat d'arrivee, etat courant
        // (instantane), puis les variables simples de l'automate (booleens, entiers, flottants). Trop long pour un paquet :
        // sans les variables (l'etat suffit a la plupart des objets).
        static NetWriter Build(Tracked t, int who, int kind, string prev, string ev, string state, string cur)
        {
            NetWriter w = Head(t, who, kind, prev, ev, state, cur);
            WriteVars(t.F, w);
            if (w.Length <= Transport.MaxPayload - 40) return w;
            if (tooLong++ < 10) Log.Warn("objets : " + t.Id + "::" + t.Name + " trop de variables (" + w.Length + " octets), envoye sans elles");
            return Head(t, who, kind, prev, ev, state, cur).U8(0).U8(0).U8(0);
        }

        static NetWriter Head(Tracked t, int who, int kind, string prev, string ev, string state, string cur)
        {
            return new NetWriter(Msg.Event).Str(Prefix + t.Id).U8(who).Str(t.Name).U8(kind).Str(prev).Str(ev).Str(state).Str(cur);
        }

        static void WriteVars(PlayMakerFSM f, NetWriter w)
        {
            FsmBool[] bs = f.FsmVariables.BoolVariables;
            int n = Mathf.Min(bs.Length, 255);
            w.U8(n);
            for (int i = 0; i < n; i++) w.Str(bs[i].Name).Bool(bs[i].Value);
            FsmInt[] ins = f.FsmVariables.IntVariables;
            n = Mathf.Min(ins.Length, 255);
            w.U8(n);
            for (int i = 0; i < n; i++) w.Str(ins[i].Name).I32(ins[i].Value);
            // (un flottant NaN / infini ferait rejeter tout le message chez l'autre, NetReader.F32 : ceux-la ne partent pas)
            FsmFloat[] fs = f.FsmVariables.FloatVariables;
            var ok = new List<FsmFloat>();
            foreach (FsmFloat x in fs) if (!float.IsNaN(x.Value) && !float.IsInfinity(x.Value) && ok.Count < 255) ok.Add(x);
            w.U8(ok.Count);
            foreach (FsmFloat x in ok) w.Str(x.Name).F32(x.Value);
        }

        static Vars ReadVars(NetReader r)
        {
            var v = new Vars();
            for (int i = 0, n = r.U8(); i < n; i++) v.B.Add(new KeyValuePair<string, bool>(r.Str(), r.Bool()));
            for (int i = 0, n = r.U8(); i < n; i++) v.I.Add(new KeyValuePair<string, int>(r.Str(), r.I32()));
            for (int i = 0, n = r.U8(); i < n; i++) v.F.Add(new KeyValuePair<string, float>(r.Str(), r.F32()));
            return v;
        }

        static void SetVars(PlayMakerFSM f, Vars v)
        {
            FsmVariables fv = f.FsmVariables;
            foreach (KeyValuePair<string, bool> x in v.B) { FsmBool b = fv.FindFsmBool(x.Key); if (b != null) b.Value = x.Value; }
            foreach (KeyValuePair<string, int> x in v.I) { FsmInt b = fv.FindFsmInt(x.Key); if (b != null) b.Value = x.Value; }
            foreach (KeyValuePair<string, float> x in v.F) { FsmFloat b = fv.FindFsmFloat(x.Key); if (b != null) b.Value = x.Value; }
        }

        // ---------------------------------------------------------------- reception (Events.OnMessage, cle « objet: »)
        public static void OnMessage(Peer from, string key, NetReader r)
        {
            string id = key.Substring(Prefix.Length);
            byte[] raw = Session.IsHost ? r.Rest() : null;
            int who = r.U8();
            if (Session.IsHost) who = from.Id;
            string fsm = r.Str();
            int kind = r.U8();
            string prev = r.Str(), ev = r.Str(), state = r.Str(), cur = r.Str();
            Vars v = ReadVars(r);
            if (Session.IsHost)
            {
                if (kind != 0) return;   // (seul l'hote envoie des instantanes)
                // Relais aux autres invites : le message tel quel, seul le numero du joueur est fixe par l'hote.
                Session.Broadcast(new NetWriter(Msg.Event).Str(key).U8(who).Raw(raw, 1, raw.Length - 1), true, who);
            }
            Tracked t = Find(id, fsm);
            if (t == null)
            {
                if (late.Count >= 64) late.RemoveAt(0);
                late.Add(new Late { Until = Time.realtimeSinceStartup + 5f, Key = key, Id = id, Fsm = fsm, Prev = prev, Ev = ev, State = state, Cur = cur, Who = who, Kind = kind, V = v });
                return;
            }
            Apply(t, who, kind, prev, ev, state, cur, v);
        }

        // Automate suivi de cet objet ; pas encore : l'objet est peut-etre connu de Props (cree a l'instant), accroche tout de suite.
        static Tracked Find(string id, string fsm)
        {
            Tracked t;
            string key = id + "::" + fsm;
            if (byKey.TryGetValue(key, out t) && t.F != null) return t;
            GameObject go = Props.ObjectOf(id);
            if (go == null || nextScan < 0) return null;
            foreach (PlayMakerFSM f in go.GetComponents<PlayMakerFSM>())
                if (f.FsmName == fsm && System.Array.IndexOf(FsmNames, fsm) >= 0 && !hooked.Contains(f) && !rejected.Contains(f)) Consider(f, id);
            return byKey.TryGetValue(key, out t) && t.F != null ? t : null;
        }

        static void RetryLate(float now)
        {
            for (int i = 0; i < late.Count; )
            {
                Late l = late[i];
                Tracked t = Find(l.Id, l.Fsm);
                if (t == null && now < l.Until) { i++; continue; }
                late.RemoveAt(i);
                if (t != null) { Apply(t, l.Who, l.Kind, l.Prev, l.Ev, l.State, l.Cur, l.V); continue; }
                if (now >= nextWarn) { nextWarn = now + 10f; Log.Warn("objets : " + l.Id + "::" + l.Fsm + " introuvable ici (" + l.State + " du joueur #" + l.Who + ")"); }
            }
        }

        static void Apply(Tracked t, int who, int kind, string prev, string ev, string state, string cur, Vars v)
        {
            PlayMakerFSM f = t.F;
            if (f.Fsm == null || !f.Fsm.Initialized) { if (Time.realtimeSinceStartup >= nextWarn) { nextWarn = Time.realtimeSinceStartup + 10f; Log.Warn("objets : " + t.Id + "::" + t.Name + " pas initialise ici, action de #" + who + " perdue"); } return; }
            SetVars(f, v);
            // Instantane : deja dans l'etat de l'hote (rien n'a bouge, ou deja rejoue) -> les variables suffisent.
            if (kind == 1 && cur.Length > 0 && f.ActiveStateName == cur) return;
            applying = true; Replay.Depth++;
            try
            {
                // Meme etat de depart -> meme evenement (l'objet reagit comme a un clic, animation et son compris) ; sinon
                // recalage sur l'etat d'arrivee. Un etat deja traverse par l'evenement n'est pas rejoue une 2e fois.
                t.Entered.Clear();
                if (ev.Length > 0 && prev.Length > 0 && f.ActiveStateName == prev) f.SendEvent(ev);
                if (state.Length > 0 && !t.Entered.Contains(state) && f.ActiveStateName != state && f.Fsm.GetState(state) != null) Game.SetState(f, state);
            }
            catch (System.Exception e) { Log.Warn("objets : rejeu de " + t.Id + "::" + t.Name + " -> " + state + " : " + e.Message); }
            finally { applying = false; Replay.Depth--; }
            if (kind == 0)
            {
                t.LastPrev = prev; t.LastEvent = ev; t.LastState = state;   // (hote : pour les arrivants)
                // Son avatar tend la main vers l'objet, sauf s'il le tient (Props le deplace depuis moins de 2 s).
                float age;
                if (!(Props.MovedByOther(f.gameObject, out age) && age < 2f)) PlayerSync.ReachFor(who, f.transform);
            }
            if (++recv <= 30 || recv % 50 == 0) Log.Info("objets" + (kind == 1 ? " (instantane)" : " de #" + who) + " : " + t.Id + "::" + t.Name + " " + prev + " -" + ev + "-> " + f.ActiveStateName + " (voulu " + (kind == 1 && cur.Length > 0 ? cur : state) + ")" + (f.gameObject.activeInHierarchy ? "" : " (objet inactif)"));
        }

        // ---------------------------------------------------------------- arrivants (hote)
        // Chaque automate qui n'est plus dans son etat de chargement : sa derniere action (rejouee, l'objet bascule comme
        // chez l'hote : lampe allumee, chargeur en marche) ; sans action connue (logique propre), l'etat courant.
        static void SendSnapshots(float now)
        {
            for (int i = snapshots.Count - 1; i >= 0; i--)
            {
                if (now < snapshots[i].Key) continue;
                Peer p = snapshots[i].Value;
                snapshots.RemoveAt(i);
                if (!p.Accepted || !Session.T.Peers.Contains(p)) continue;
                int n = 0, skipped = 0;
                foreach (Tracked t in byKey.Values)
                {
                    if (t.F == null || t.F.Fsm == null) continue;
                    string cur = t.F.ActiveStateName ?? "";
                    if (t.LastState == null && cur == t.LoadState) continue;
                    if (n >= 120) { skipped++; continue; }   // (borne : un message par automate, jamais plus de 120 d'un coup)
                    NetWriter w = t.LastState != null ? Build(t, Session.LocalId, 1, t.LastPrev, t.LastEvent, t.LastState, cur)
                                                      : Build(t, Session.LocalId, 1, "", "", cur, cur);
                    Session.T.SendReliable(p, w.ToArray());
                    n++;
                }
                if (n > 0 || skipped > 0) Log.Info("objets : etat de " + n + " automates d'objets envoye a " + p + (skipped > 0 ? " (" + skipped + " de plus non envoyes)" : ""));
            }
        }
    }
}
