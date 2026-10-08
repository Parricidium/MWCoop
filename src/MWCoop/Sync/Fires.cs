using System.Collections.Generic;
using HutongGames.PlayMaker;
using MWCoop.Net;
using UnityEngine;

namespace MWCoop
{
    // Outils communs a la cuisine (Cooking) et aux feux (Fires).
    //  - Annulation d'un etat : un crochet en tete d'etat coupe les autres actions de l'etat et ne finit pas ;
    //    a l'image suivante, hors de PlayMaker, l'automate est remis dans l'etat voulu et les actions rendues.
    //    (Basculer l'automate depuis l'OnEnter d'une de ses actions laisserait PlayMaker finir l'etat quitte.)
    //  - Autorite de proximite : celui qui tient l'objet ; sinon pas celui dont la copie suit un autre joueur
    //    (Props.MovedByOther) ; sinon le joueur le plus proche dans un rayon (1 m d'avance au tenant actuel :
    //    pas de bascule a chaque pas) ; sinon l'hote. Chacun le calcule avec les memes positions.
    public static class FireTools
    {
        class Pending { public PlayMakerFSM F; public string State, Back; public List<FsmStateAction> Off; public int Frame; }
        static readonly List<Pending> cancels = new List<Pending>();
        static Transform player;
        static PlayMakerFSM hand;

        public static void OnLevelLoaded() { cancels.Clear(); player = null; hand = null; }

        // Actions d'un etat ; automate jamais demarre (objet inactif au chargement) : donnees chargees d'abord.
        public static FsmStateAction[] Actions(PlayMakerFSM f, FsmState st)
        {
            try { return st.Actions; }
            catch
            {
                try { f.Fsm.InitData(); return st.Actions; } catch { return null; }
            }
        }

        public static bool Insert(PlayMakerFSM f, FsmState st, FsmStateAction a, bool atEnd)
        {
            if (st == null) return false;
            FsmStateAction[] acts = Actions(f, st);
            if (acts == null) return false;
            var l = new List<FsmStateAction>(acts);
            if (atEnd) l.Add(a); else l.Insert(0, a);
            st.Actions = l.ToArray();
            return true;
        }

        // Depuis l'OnEnter du crochet 'hook' (en tete de 'st') : l'etat n'aura aucun effet, l'automate repart de 'back'.
        // Le crochet ne doit PAS appeler Finish() (sinon l'etat finirait et suivrait sa transition FINISHED).
        public static void Cancel(PlayMakerFSM f, FsmState st, FsmStateAction hook, string back)
        {
            var c = new Pending { F = f, State = st.Name, Back = back, Off = new List<FsmStateAction>(), Frame = Time.frameCount };
            foreach (FsmStateAction a in st.Actions)
                if (a != null && a != hook && a.Enabled) { a.Enabled = false; c.Off.Add(a); }
            cancels.Add(c);
        }

        public static void Update()
        {
            for (int i = cancels.Count - 1; i >= 0; i--)
            {
                Pending c = cancels[i];
                if (Time.frameCount <= c.Frame) continue;
                cancels.RemoveAt(i);
                try
                {
                    if (c.F != null && c.F.ActiveStateName == c.State)
                    {
                        Replay.Depth++;
                        try { Game.SetState(c.F, c.Back); } finally { Replay.Depth--; }
                    }
                }
                catch (System.Exception e) { Log.Warn("annulation de " + c.State + " : " + e.Message); }
                foreach (FsmStateAction a in c.Off) a.Enabled = true;
            }
        }

        public static Vector3 LocalPos
        {
            get
            {
                if (player == null) { GameObject go = GameObject.Find("PLAYER"); if (go != null) player = go.transform; }
                return player != null ? player.position : Vector3.zero;
            }
        }

        // Objet tenu en main par le joueur local (PickUp :: PickedObject).
        public static GameObject Held()
        {
            if (hand == null)
            {
                GameObject h = GameObject.Find("PLAYER/Pivot/AnimPivot/Camera/FPSCamera/1Hand_Assemble/Hand");
                if (h != null) hand = Game.FsmOn(h, "PickUp");
                if (hand == null) return null;
            }
            FsmGameObject g = hand.FsmVariables.FindFsmGameObject("PickedObject");
            return g != null ? g.Value : null;
        }

        // Joueur le plus proche de 'pos' a moins de 'radius' m (sinon l'hote, 0). 'prev' : tenant actuel.
        public static int Nearest(Vector3 pos, float radius, int prev)
        {
            int best = -1;
            float bd = float.MaxValue, now = Time.realtimeSinceStartup;
            foreach (PlayerInfo p in Session.Players.Values)
            {
                Vector3 at;
                if (p.Local || p.Id == Session.LocalId) at = LocalPos;
                else { if (p.Level != 1 || p.StateTime <= 0f || now - p.StateTime > 5f) continue; at = p.State.Feet; }
                float d = (at - pos).magnitude;
                if (p.Id == prev) d -= 1f;
                if (d < bd || (d == bd && p.Id < best)) { bd = d; best = p.Id; }
            }
            if (best < 0) return Session.IsHost || !Session.Active ? Session.LocalId : 0;
            return bd > radius ? 0 : best;
        }

        // Autorite sur un objet mobile (voir l'en-tete). -2 : un autre joueur le deplace (on ne sait pas lequel).
        public static int Authority(GameObject go, Vector3 pos, float radius, ref int prev)
        {
            if (go != null && go == Held()) { prev = Session.LocalId; return prev; }
            float age;
            if (go != null && Props.MovedByOther(go, out age) && age < 2f) return -2;
            prev = Nearest(pos, radius, prev);
            return prev;
        }

        public static string FsmPath(PlayMakerFSM f) { return Recon.Path(f.transform) + "::" + f.FsmName; }

        public static float GetF(PlayMakerFSM f, string n, float def)
        {
            if (f == null) return def;
            FsmFloat v = f.FsmVariables.FindFsmFloat(n);
            return v != null ? v.Value : def;
        }

        public static void SetF(PlayMakerFSM f, string n, float val)
        {
            if (f == null) return;
            FsmFloat v = f.FsmVariables.FindFsmFloat(n);
            if (v != null) v.Value = val;
        }

        // Essais ([Test] Journal...=1) : l'automate avec les parametres de ses actions, dans le journal.
        public static void Journal(string title, PlayMakerFSM f)
        {
            if (f == null) return;
            var sb = new System.Text.StringBuilder(title).Append(" : ").Append(FsmPath(f)).Append('\n');
            try { Recon.AppendFsm(sb, "  ", f, true); } catch (System.Exception e) { sb.Append("  (illisible : " + e.Message + ")"); }
            Log.Info(sb.ToString());
        }

        // Cles stables d'une liste d'automates : chemin::nom, puis #2, #3... pour les doublons (ordre par position).
        public static void Keys<T>(List<T> items, System.Func<T, PlayMakerFSM> fsm, System.Action<T, string> set)
        {
            items.Sort((a, b) =>
            {
                PlayMakerFSM fa = fsm(a), fb = fsm(b);
                int c = string.CompareOrdinal(FsmPath(fa), FsmPath(fb));
                if (c != 0) return c;
                Vector3 pa = fa.transform.position, pb = fb.transform.position;
                c = pa.x.CompareTo(pb.x);
                return c != 0 ? c : pa.z.CompareTo(pb.z);
            });
            var seen = new Dictionary<string, int>();
            foreach (T it in items)
            {
                string k = FsmPath(fsm(it));
                int n; seen.TryGetValue(k, out n); seen[k] = n + 1;
                set(it, n == 0 ? k : k + "#" + (n + 1));
            }
        }

        // Hote : invites qui viennent d'arriver en jeu (niveau 1), a servir 'delay' s plus tard.
        public static void Arrivals(Dictionary<int, int> levels, List<KeyValuePair<float, Peer>> due, float delay)
        {
            foreach (PlayerInfo pi in Session.Players.Values)
            {
                if (pi.Local || pi.Peer == null) continue;
                int old;
                levels.TryGetValue(pi.Id, out old);
                if (pi.Level == 1 && old != 1) due.Add(new KeyValuePair<float, Peer>(Time.realtimeSinceStartup + delay, pi.Peer));
                levels[pi.Id] = pi.Level;
            }
        }

        public static void SendTo(Peer to, NetWriter w)
        {
            if (to == null) Session.SendAll(w, true);
            else if (Session.T != null && to.Accepted && Session.T.Peers.Contains(to)) Session.T.SendReliable(to, w.ToArray());
        }
    }

    // Feux : bois dans les cheminees, poeles, sauna, grill ; incendies.
    //  - BOIS (WoodTrigger 'Trigger', "Destroy firewood" : la buche detruite, Woods + 1, buches montrees) : compte
    //    chez celui dont la buche entre (chez les autres, la copie qui suit ses messages Props est ignoree : etat
    //    annule), puis annonce (1 bois) : les autres prennent ce nombre, montrent les buches et detruisent leur
    //    copie de la buche (par son ID, sinon une buche sans ID a moins de 1,5 m).
    //  - FOYERS (cheminee de la maison, poele du chalet, cheminee / grill / poele du sauna du cottage, baril a
    //    ordures) : l'allumage (SetFire 'Use' -> "Start fire 2", par clic rejoue par WorldFsms ou par les braises)
    //    est annonce par qui le voit commencer chez lui (7 allume) ; le joueur le plus proche (sinon l'hote) envoie
    //    toutes les 2 s ce qui a change : nombre de buches, si ca brule, la chaleur (HeatSource 'Data' Temperature)
    //    et les braises (Grilling 'Logic' HeatBuildup : GrillTrigger, le grill des saucisses) ; les autres s'y
    //    recalent (allumage, extinction quand il n'y a plus de bois).
    //  - INCENDIES decides par l'hote : maison (YARD/Building/HOUSEFIRE/<piece>/Fire<piece> 'Simulation' : depart,
    //    propagation tiree au hasard, meubles detruits, extinction), maison de Koistinen (FIRE 'Logic'), explosion
    //    des bidons d'essence ('Explosion'). Chez un invite, un depart qui vient de sa propre logique (poele, tele,
    //    grill trop pres du mur, sauna surchauffe, propagation) est annule ; s'il vient du joueur (cigarette au
    //    lit : SleepTrigger 'Extras' ; grill renverse : 'Empty'), il est demande a l'hote. L'hote annonce chaque
    //    etape, les invites la rejouent (seulement en avant, jamais un retour en arriere : leur propre minuterie
    //    a pu les y mener avant). Un invite qui eteint une piece (PutOut -> PUTOUT) l'eteint chez lui et le
    //    demande a l'hote. Les drapeaux sauvegardes (Data : Fire<piece>, *Done) suivent deja l'hote (WorldFsms).
    //  - Feux de voiture (FIRE, OverheatFire, FireElectric sous la voiture) : objets actifs ou non, decides par
    //    celui qui fait tourner la voiture (VehicleSync.SimAuthority) ; un autre qui l'eteint le lui demande
    //    (PUTOUT rejoue chez lui). Incendie de la station-service (PERAPORTTI .../GasolineFire) : l'hote.
    public static class Fires
    {
        const string Mod = "feux";
        static float loadedAt = -1, nextFind, nextPlaces, nextFlags, nextArrivals, nextWarn, nextCancelLog;
        static readonly Dictionary<int, int> levels = new Dictionary<int, int>();
        static readonly List<KeyValuePair<float, Peer>> due = new List<KeyValuePair<float, Peer>>();

        // ================================================================ incendies decides par l'hote
        const int House = 0, Koistinen = 1, Can = 2;
        class Ruled
        {
            public string Key; public PlayMakerFSM F; public int Kind;
            public Dictionary<string, int> Rank; public string Start, Idle;
        }
        static readonly Dictionary<string, Ruled> ruled = new Dictionary<string, Ruled>();
        static readonly HashSet<PlayMakerFSM> ruledFsms = new HashSet<PlayMakerFSM>();
        static readonly List<Ruled> fresh = new List<Ruled>();
        static Ruled applying;
        static bool forceForward;   // essais : le prochain depart local est demande a l'hote comme s'il venait du joueur
        static readonly HashSet<string> PlayerSenders = new HashSet<string> { "Extras", "Empty" };

        // Progression de chaque sorte d'incendie (un etat recu n'est rejoue que s'il est plus loin que l'etat local).
        static readonly string[] HouseSteps = { "Wait", "Fire", "Burn gfx", "Destroy|Destroyed", "Spread|Destroy 2", "Reset state", "Stop fire", "State 1" };
        static readonly string[] KoistinenSteps = { "State 1", "Fire", "Explosion", "State 2" };
        static readonly string[] CanSteps = { "Wait|In fire", "Explosion", "Stop fire", "State 1" };

        static Dictionary<string, int> Steps(string[] steps)
        {
            var d = new Dictionary<string, int>();
            for (int i = 0; i < steps.Length; i++) foreach (string s in steps[i].Split('|')) d[s] = i;
            return d;
        }

        static int RankOf(Ruled r, string state)
        {
            int k;
            return state != null && r.Rank.TryGetValue(state, out k) ? k : -1;
        }

        class RuledHook : ModHook
        {
            public override string Module { get { return Mod; } }
            public Ruled R; public FsmState St;
            public override void OnEnter()
            {
                bool cancel = false;
                try { cancel = OnRuledEnter(R, St, this); }
                catch (System.Exception e) { Replay.HookError(e); }
                if (!cancel) Finish();
            }
        }

        static bool Playing { get { return Session.Active && Session.RemoteCount > 0 || testing; } }

        static bool OnRuledEnter(Ruled r, FsmState st, RuledHook h)
        {
            if (!Playing || applying == r) return false;
            string state = st.Name;
            if (Session.IsHost)
            {
                if (RankOf(r, state) > 0) SendRuled(r, state, null);
                return false;
            }
            FsmTransition tr = r.F.Fsm.LastTransition;
            string ev = tr != null ? tr.EventName : "";
            if (state == r.Start)
            {
                // Depart chez un invite : jamais ici sans l'hote.
                FsmEventData ed = Fsm.EventData;
                string sender = ed != null && ed.SentByFsm != null ? ed.SentByFsm.Name : "";
                bool forward = Replay.Depth == 0 && (forceForward || r.Kind == House && PlayerSenders.Contains(sender));
                forceForward = false;
                FsmState prev = r.F.Fsm.PreviousActiveState;
                string back = prev != null && prev.Name != state ? prev.Name : r.Idle;
                FireTools.Cancel(r.F, st, h, back);
                if (forward)
                {
                    Log.Info("feux : " + r.Key + " " + ev + " (" + sender + ") demande a l'hote");
                    Session.SendAll(new NetWriter(Msg.Fire).U8(4).U8(Session.LocalId).Str(r.Key).Str(ev.Length > 0 ? ev : "FIRE"), true);
                }
                else if (Time.realtimeSinceStartup >= nextCancelLog)
                {
                    nextCancelLog = Time.realtimeSinceStartup + 5f;
                    Log.Info("feux : " + r.Key + " -> " + state + " (" + (sender.Length > 0 ? sender : "?") + ") annule ici : l'hote decide");
                }
                return true;
            }
            // Eteint ici par le joueur (extincteur : PutOut "Put out" -> PUTOUT) : chez l'hote aussi.
            if (r.Kind == House && ev == "PUTOUT" && Replay.Depth == 0)
            {
                Log.Info("feux : " + r.Key + " eteint ici (" + state + "), demande a l'hote");
                Session.SendAll(new NetWriter(Msg.Fire).U8(4).U8(Session.LocalId).Str(r.Key).Str("PUTOUT"), true);
            }
            return false;
        }

        static void SendRuled(Ruled r, string state, Peer to)
        {
            Log.Info("feux : " + r.Key + " -> " + state + (to != null ? " (a " + to + ")" : ""));
            FireTools.SendTo(to, new NetWriter(Msg.Fire).U8(3).U8(Session.LocalId).Str(r.Key).Str(state).U8(to != null ? 1 : 0));
        }

        static void FindRuled()
        {
            int added = 0;
            foreach (Object o in Game.AllFsms())
            {
                var f = (PlayMakerFSM)o; if (f == null) continue;
                if (f.hideFlags != HideFlags.None || ruledFsms.Contains(f) || !f.transform.root.gameObject.activeInHierarchy) continue;
                int kind = -1;
                Transform p = f.transform.parent;
                if (f.FsmName == "Simulation" && p != null && p.parent != null && p.parent.name == "HOUSEFIRE") kind = House;
                else if (f.FsmName == "Logic" && f.gameObject.name == "FIRE" && p != null && p.name == "HouseKoistinen") kind = Koistinen;
                else if (f.FsmName == "Explosion" && f.gameObject.name.Contains("(item"))
                {
                    try { if (f.Fsm.GetState("In fire") != null && f.Fsm.GetState("Stop fire") != null) kind = Can; } catch { }
                }
                if (kind < 0) continue;
                ruledFsms.Add(f);
                if (!Replay.Claim(f, Mod)) { Log.Warn("feux : " + FireTools.FsmPath(f) + " deja suivi par " + Replay.Owner(f)); continue; }
                var r = new Ruled { F = f, Kind = kind };
                if (kind == House) { r.Rank = Steps(HouseSteps); r.Start = "Fire"; r.Idle = "Wait"; }
                else if (kind == Koistinen) { r.Rank = Steps(KoistinenSteps); r.Start = "Fire"; r.Idle = "State 1"; }
                else { r.Rank = Steps(CanSteps); r.Start = "Explosion"; r.Idle = "Wait"; }
                bool ok = true;
                foreach (FsmState st in f.Fsm.States)
                    if (!FireTools.Insert(f, st, new RuledHook { R = r, St = st }, false)) ok = false;
                if (!ok) Log.Warn("feux : " + FireTools.FsmPath(f) + " accroche en partie seulement");
                fresh.Add(r);
                added++;
            }
            if (fresh.Count > 0)
            {
                FireTools.Keys(fresh, x => x.F, (x, k) => x.Key = k);
                foreach (Ruled r in fresh)
                {
                    ruled[r.Key] = r;
                    if (r.Kind == House && r.Key.Contains("/Kitchen/") && Config.GetInt("Test", "JournalFeux", 0) != 0) FireTools.Journal("feux (journal)", r.F);
                }
                fresh.Clear();
            }
            if (added > 0) Log.Info("feux : " + added + " incendies decides par l'hote (" + ruled.Count + " en tout)");
        }

        static void OnRuledState(int who, string key, string state, bool snapshot)
        {
            Ruled r;
            if (Session.IsHost) return;   // l'hote decide, il n'en recoit pas
            if (!ruled.TryGetValue(key, out r) || r.F == null) { Warn("incendie " + key + " introuvable ici"); return; }
            int cur = RankOf(r, r.F.ActiveStateName), want = RankOf(r, state);
            if (want < 0 || want <= cur || r.F.Fsm.GetState(state) == null) return;
            if (!r.F.enabled || !r.F.gameObject.activeInHierarchy) { Log.Info("feux : " + key + " -> " + state + " recu, automate inactif ici"); return; }
            applying = r; Replay.Depth++;
            try { Game.SetState(r.F, state); }
            finally { Replay.Depth--; applying = null; }
            Log.Info("feux de #" + who + " : " + key + " -> " + (r.F != null ? r.F.ActiveStateName : "?") + (snapshot ? " (arrivee)" : ""));
        }

        static void OnRequest(int who, string key, string ev)
        {
            Ruled r;
            if (!Session.IsHost || !ruled.TryGetValue(key, out r) || r.F == null) return;
            if (ev == "FIRE" && r.F.ActiveStateName != r.Idle) { Log.Info("feux : depart demande par #" + who + " pour " + key + " ignore (deja " + r.F.ActiveStateName + ")"); return; }
            string before = r.F.ActiveStateName;
            r.F.SendEvent(ev);
            Log.Info("feux : " + key + " " + before + " -" + ev + "-> " + r.F.ActiveStateName + " (demande de #" + who + ")");
        }

        // ================================================================ foyers (bois, allumage, chaleur)
        static readonly HashSet<string> BurnStates = new HashSet<string> { "Start fire 2", "Burn", "Remove wood", "Check wood", "State 2", "State 3", "State 4", "State 5" };
        static readonly HashSet<string> BarrelBurn = new HashSet<string> { "Start fire 2", "Burn", "Destroy", "State 1" };

        class Place
        {
            public string Key; public Transform Root; public bool Barrel;
            public PlayMakerFSM Trigger, SetFire, Grilling, Heat;
            public GameObject SetFireGo; public Transform Logs;
            public FsmState DestroyState;
            public int Auth = -1, SentWoods = -1; public bool SentBurning, WasBurning;
            public float SentTemp, SentHeat, SentAt, AppliedAt = -100f, ApplyAt;
            public string ApplyState;
            public bool PendingWood; public Vector3 LogPos; public string LogId = "";
        }
        static readonly Dictionary<string, Place> places = new Dictionary<string, Place>();
        static readonly HashSet<PlayMakerFSM> placeFsms = new HashSet<PlayMakerFSM>();

        class WoodHook : ModHook
        {
            public override string Module { get { return Mod; } }
            public Place P; public FsmState St;
            public override void OnEnter()
            {
                bool cancel = false;
                try { cancel = OnWood(P, St, this); }
                catch (System.Exception e) { Replay.HookError(e); }
                if (!cancel) Finish();
            }
        }

        // Buche entree dans le foyer (etat "Destroy firewood", avant ses actions).
        static bool OnWood(Place p, FsmState st, WoodHook h)
        {
            if (!Session.Active || Replay.Depth > 0) return false;
            FsmGameObject c = p.Trigger.FsmVariables.FindFsmGameObject("Collider");
            GameObject log = c != null ? c.Value : null;
            float age;
            if (log != null && Props.MovedByOther(log, out age) && age < 3f)
            {
                // Copie d'une buche qu'un autre a jetee : c'est chez lui qu'elle compte.
                FireTools.Cancel(p.Trigger, st, h, "Wait");
                if (Time.realtimeSinceStartup >= nextCancelLog) { nextCancelLog = Time.realtimeSinceStartup + 5f; Log.Info("feux : buche d'un autre joueur dans " + p.Key + ", comptee chez lui"); }
                return true;
            }
            p.PendingWood = true;
            p.LogPos = log != null ? log.transform.position : p.Trigger.transform.position;
            p.LogId = log != null ? Props.ItemId(log) : "";
            return false;
        }

        static int Woods(Place p)
        {
            if (p.Trigger == null) return 0;
            FsmInt w = p.Trigger.FsmVariables.FindFsmInt("Woods");
            return w != null ? w.Value : 0;
        }

        static bool Burning(Place p)
        {
            if (p.SetFireGo == null || p.SetFire == null || !p.SetFireGo.activeInHierarchy) return false;
            return (p.Barrel ? BarrelBurn : BurnStates).Contains(p.SetFire.ActiveStateName ?? "");
        }

        static void SetWoods(Place p, int n)
        {
            if (p.Trigger != null) { FsmInt w = p.Trigger.FsmVariables.FindFsmInt("Woods"); if (w != null) w.Value = n; }
            // Copie lue par SetFire au debut de "Burn" et rendue a "Remove wood" (moins un) : la meme, sinon elle ecraserait.
            if (p.SetFire != null && Game.LocalVar(p.SetFire, "Woods")) { FsmInt w = p.SetFire.FsmVariables.FindFsmInt("Woods"); if (w != null) w.Value = n; }
            ShowLogs(p, n);
        }

        // Buches visibles dans le foyer (Woods/log1..log4) : autant que de bois.
        static void ShowLogs(Place p, int n)
        {
            if (p.Logs == null) return;
            for (int i = 0; i < p.Logs.childCount; i++)
            {
                Transform t = p.Logs.GetChild(i);
                int k;
                if (!t.name.StartsWith("log") || !int.TryParse(t.name.Substring(3), out k)) continue;
                bool on = k <= n;
                if (t.gameObject.activeSelf != on) t.gameObject.SetActive(on);
            }
        }

        // Allume ici comme chez l'autre : SetFire active, puis "Start fire 2" (un peu apres une activation :
        // l'automate tout juste active se met d'abord dans son etat de depart).
        static void Ignite(Place p)
        {
            if (p.SetFireGo == null || p.SetFire == null || Burning(p)) return;
            p.AppliedAt = Time.realtimeSinceStartup;
            if (!p.SetFireGo.activeSelf) { p.SetFireGo.SetActive(true); p.ApplyState = "Start fire 2"; p.ApplyAt = Time.realtimeSinceStartup + 0.25f; return; }
            ApplyFireState(p, "Start fire 2");
        }

        static void ApplyFireState(Place p, string state)
        {
            if (p.SetFire == null || p.SetFire.Fsm.GetState(state) == null) return;
            p.AppliedAt = Time.realtimeSinceStartup;
            Replay.Depth++;
            try { Game.SetState(p.SetFire, state); }
            finally { Replay.Depth--; }
            p.WasBurning = Burning(p);
        }

        static void FindPlaces()
        {
            var found = new List<Place>();
            foreach (Object o in Game.AllFsms())
            {
                var f = (PlayMakerFSM)o; if (f == null) continue;
                if (f.hideFlags != HideFlags.None || placeFsms.Contains(f) || !f.transform.root.gameObject.activeInHierarchy) continue;
                Place p = null;
                if (f.FsmName == "Trigger" && f.gameObject.name == "WoodTrigger" && Game.LocalVar(f, "Woods"))
                {
                    Transform root = f.transform.parent;
                    if (root == null) continue;
                    p = new Place { Root = root, Trigger = f };
                    Transform sf = root.Find("SetFire");
                    if (sf != null) { p.SetFireGo = sf.gameObject; p.SetFire = Game.FsmOn(sf.gameObject, "Use"); }
                    Transform gr = root.Find("Grilling");
                    if (gr != null) p.Grilling = Game.FsmOn(gr.gameObject, "Logic");
                    p.Logs = root.Find("Woods");
                    p.Heat = HeatSource(root) ?? (root.parent != null ? HeatSource(root.parent) : null);
                }
                else if (f.FsmName == "Use" && f.gameObject.name == "Trigger" && f.transform.parent != null && f.transform.parent.name.StartsWith("garbage barrel"))
                {
                    Transform root = f.transform.parent;
                    p = new Place { Root = root, Barrel = true, SetFire = f, SetFireGo = f.gameObject, Heat = HeatSource(root) };
                }
                if (p == null) continue;
                placeFsms.Add(f);
                found.Add(p);
            }
            if (found.Count == 0) return;
            FireTools.Keys(found, x => x.Trigger ?? x.SetFire, (x, k) => x.Key = k);
            int hooked = 0;
            foreach (Place p in found)
            {
                places[p.Key] = p;
                p.WasBurning = Burning(p);
                if (p.Trigger == null) continue;
                if (!Replay.Claim(p.Trigger, Mod)) { Log.Warn("feux : " + p.Key + " deja suivi par " + Replay.Owner(p.Trigger)); continue; }
                p.DestroyState = p.Trigger.Fsm.GetState("Destroy firewood");
                if (FireTools.Insert(p.Trigger, p.DestroyState, new WoodHook { P = p, St = p.DestroyState }, false)) hooked++;
            }
            Log.Info("feux : " + found.Count + " foyers suivis (" + hooked + " comptes de bois accroches)");
            if (Config.GetInt("Test", "JournalFeux", 0) != 0)
                foreach (Place p in found)
                {
                    if (!p.Key.Contains("LIVINGROOM") && !p.Barrel) continue;
                    FireTools.Journal("feux (journal)", p.Trigger); FireTools.Journal("feux (journal)", p.SetFire); FireTools.Journal("feux (journal)", p.Grilling);
                }
        }

        static PlayMakerFSM HeatSource(Transform t)
        {
            for (int i = 0; i < t.childCount; i++)
            {
                Transform c = t.GetChild(i);
                if (c.name.StartsWith("HeatSource")) { PlayMakerFSM d = Game.FsmOn(c.gameObject, "Data"); if (d != null) return d; }
            }
            return null;
        }

        static int PlaceFlags(Place p) { return (Burning(p) ? 1 : 0) | (p.SetFireGo != null && p.SetFireGo.activeSelf ? 2 : 0); }

        static void UpdatePlaces(float now)
        {
            foreach (Place p in places.Values)
            {
                if (p.ApplyState != null && now >= p.ApplyAt) { string s = p.ApplyState; p.ApplyState = null; ApplyFireState(p, s); }
                // Buche entree ici : annoncee (Woods deja compte par l'etat).
                if (p.PendingWood)
                {
                    p.PendingWood = false;
                    int n = Woods(p);
                    Log.Info("feux : buche dans " + p.Key + " ici (" + n + " en tout)");
                    if (Session.Active) Session.SendAll(new NetWriter(Msg.Fire).U8(1).U8(Session.LocalId).Str(p.Key).U8(n).Vec(p.LogPos).Str(p.LogId)
                                                        .U8(p.SetFireGo != null && p.SetFireGo.activeSelf ? 1 : 0), true);
                }
                // Allume ici par le joueur ou la logique locale (pas un rejeu d'ici) : annonce.
                bool burning = Burning(p);
                if (burning && !p.WasBurning && now - p.AppliedAt > 2f && Session.Active)
                {
                    Log.Info("feux : " + p.Key + " allume ici");
                    Session.SendAll(new NetWriter(Msg.Fire).U8(7).U8(Session.LocalId).Str(p.Key), true);
                }
                p.WasBurning = burning;
            }
            if (now < nextPlaces || !Playing) return;
            nextPlaces = now + 2f;
            NetWriter w = null;
            foreach (Place p in places.Values)
            {
                if (p.Root == null) continue;
                int auth = FireTools.Nearest(p.Root.position, 15f, p.Auth);
                if (auth != p.Auth) { p.Auth = auth; p.SentWoods = -1; }
                if (auth != Session.LocalId) continue;
                int woods = Woods(p);
                bool burning = Burning(p);
                float temp = FireTools.GetF(p.Heat, "Temperature", 0f), heat = FireTools.GetF(p.Grilling, "HeatBuildup", 0f);
                bool changed = woods != p.SentWoods || burning != p.SentBurning || Mathf.Abs(temp - p.SentTemp) > 2f || Mathf.Abs(heat - p.SentHeat) > 8f
                               || (burning || woods > 0) && now - p.SentAt > 15f;
                if (!changed) continue;
                p.SentWoods = woods; p.SentBurning = burning; p.SentTemp = temp; p.SentHeat = heat; p.SentAt = now;
                if (w == null) w = new NetWriter(Msg.Fire).U8(2).U8(Session.LocalId);
                w.Str(p.Key).U8(woods).U8(PlaceFlags(p)).F32(temp).F32(heat);
                if (w.Length > 900) { Session.SendAll(w, true); w = null; }
            }
            if (w != null) Session.SendAll(w, true);
        }

        static void SendAllPlaces(Peer to)
        {
            NetWriter w = null;
            foreach (Place p in places.Values)
            {
                if (w == null) w = new NetWriter(Msg.Fire).U8(2).U8(Session.LocalId);
                w.Str(p.Key).U8(Woods(p)).U8(PlaceFlags(p)).F32(FireTools.GetF(p.Heat, "Temperature", 0f)).F32(FireTools.GetF(p.Grilling, "HeatBuildup", 0f));
                if (w.Length > 900) { FireTools.SendTo(to, w); w = null; }
            }
            if (w != null) FireTools.SendTo(to, w);
        }

        static void OnWoodMsg(int who, string key, int woods, Vector3 pos, string logId, bool setFireOn)
        {
            Place p;
            if (!places.TryGetValue(key, out p)) { Warn("foyer " + key + " introuvable ici"); return; }
            SetWoods(p, woods);
            if (setFireOn && p.SetFireGo != null && !p.SetFireGo.activeSelf) p.SetFireGo.SetActive(true);
            // Sa buche : par son ID (suivie par Props), sinon une buche sans ID tout pres.
            GameObject log = logId.Length > 0 ? Props.ObjectOf(logId) : null;
            if (log == null)
            {
                float best = 1.5f * 1.5f;
                foreach (Rigidbody rb in Object.FindObjectsOfType<Rigidbody>())
                {
                    if (!rb.name.StartsWith("firewood") || Props.ItemId(rb.gameObject).Length > 0) continue;
                    float d = (rb.position - pos).sqrMagnitude;
                    if (d < best) { best = d; log = rb.gameObject; }
                }
            }
            if (log != null) Object.Destroy(log);
            Log.Info("feux de #" + who + " : buche dans " + key + " (" + woods + " en tout)" + (log != null ? ", sa copie detruite ici" : ""));
        }

        static void OnPlace(int who, string key, int woods, int flags, float temp, float heat)
        {
            Place p;
            if (!places.TryGetValue(key, out p)) { Warn("foyer " + key + " introuvable ici"); return; }
            if (p.Auth == Session.LocalId) return;   // c'est nous qui faisons reference pour ce foyer
            bool burning = (flags & 1) != 0;
            if (Woods(p) != woods) SetWoods(p, woods);
            if ((flags & 2) != 0 && p.SetFireGo != null && !p.SetFireGo.activeSelf && !p.Barrel) p.SetFireGo.SetActive(true);
            bool here = Burning(p);
            float now = Time.realtimeSinceStartup;
            if (burning && !here && now - p.AppliedAt > 3f) { Ignite(p); Log.Info("feux de #" + who + " : " + key + " brule chez lui, allume ici"); }
            else if (!burning && here && woods == 0 && now - p.AppliedAt > 3f)
            {
                ApplyFireState(p, p.Barrel ? "Fire off" : "State 6");
                Log.Info("feux de #" + who + " : " + key + " eteint chez lui, eteint ici");
            }
            if (p.Heat != null && Mathf.Abs(FireTools.GetF(p.Heat, "Temperature", temp) - temp) > 2f) FireTools.SetF(p.Heat, "Temperature", temp);
            if (p.Grilling != null && Mathf.Abs(FireTools.GetF(p.Grilling, "HeatBuildup", heat) - heat) > 8f) FireTools.SetF(p.Grilling, "HeatBuildup", heat);
        }

        static void OnLight(int who, string key)
        {
            Place p;
            if (!places.TryGetValue(key, out p)) { Warn("foyer " + key + " introuvable ici"); return; }
            if (Burning(p)) { p.AppliedAt = Time.realtimeSinceStartup; return; }
            Ignite(p);
            Log.Info("feux de #" + who + " : " + key + " allume");
        }

        // ================================================================ feux de voiture, station-service (objets actifs)
        static readonly HashSet<string> CarFireNames = new HashSet<string> { "FIRE", "OverheatFire", "FireElectric" };
        class Flag { public string Key; public GameObject Go; public bool Car, Remote, LastSent; public float NextFull; }
        static readonly Dictionary<string, Flag> flags = new Dictionary<string, Flag>();
        static readonly HashSet<GameObject> flagGos = new HashSet<GameObject>();
        static bool stationFound;

        static void FindFlags()
        {
            var found = new List<Flag>();
            for (int i = 0; i < VehicleSync.LocalCount; i++)
            {
                Rigidbody b = VehicleSync.LocalBody(i);
                if (b == null) continue;
                foreach (Transform t in b.GetComponentsInChildren<Transform>(true))
                    if (CarFireNames.Contains(t.name) && !flagGos.Contains(t.gameObject)) found.Add(new Flag { Go = t.gameObject, Car = true });
            }
            if (!stationFound)
                foreach (GameObject root in Recon.SceneRoots())
                {
                    if (root.name != "PERAPORTTI") continue;
                    stationFound = true;
                    foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                        if (t.name == "GasolineFire" && Game.FsmOn(t.gameObject, "Logic") != null && !flagGos.Contains(t.gameObject)) found.Add(new Flag { Go = t.gameObject });
                }
            if (found.Count == 0) return;
            foreach (Flag x in found)
            {
                flagGos.Add(x.Go);
                string k = "o:" + Recon.Path(x.Go.transform);
                int n = 1;
                while (flags.ContainsKey(n == 1 ? k : k + "#" + n)) n++;
                x.Key = n == 1 ? k : k + "#" + n;
                x.Remote = x.LastSent = x.Go.activeSelf;
                flags[x.Key] = x;
            }
            Log.Info("feux : " + found.Count + " feux de voiture / station suivis (" + flags.Count + " en tout)");
        }

        static bool FlagAuthority(Flag x) { return x.Car ? VehicleSync.SimAuthority(x.Go.transform) : Session.IsHost; }

        static void UpdateFlags(float now)
        {
            if (now < nextFlags || !Playing) return;
            nextFlags = now + 0.5f;
            foreach (Flag x in flags.Values)
            {
                if (x.Go == null) continue;
                bool cur = x.Go.activeSelf;
                if (FlagAuthority(x))
                {
                    if (cur == x.LastSent && !(cur && now >= x.NextFull)) continue;
                    if (cur != x.LastSent) Log.Info("feux : " + x.Key + (cur ? " en feu" : " eteint"));
                    x.LastSent = x.Remote = cur;
                    x.NextFull = now + 10f;
                    Session.SendAll(new NetWriter(Msg.Fire).U8(5).U8(Session.LocalId).Str(x.Key).U8(cur ? 1 : 0), true);
                    continue;
                }
                x.LastSent = cur;
                if (cur == x.Remote) continue;
                if (cur)
                {
                    // Prend feu ici sans celui qui fait autorite : annule.
                    Replay.Depth++;
                    try { x.Go.SetActive(false); } finally { Replay.Depth--; }
                    x.LastSent = false;
                    Log.Info("feux : " + x.Key + " a pris feu ici sans l'autorite, annule");
                }
                else
                {
                    // Eteint ici (extincteur) : demande a celui qui fait autorite.
                    x.Remote = false;
                    Log.Info("feux : " + x.Key + " eteint ici, demande a l'autorite");
                    Session.SendAll(new NetWriter(Msg.Fire).U8(6).U8(Session.LocalId).Str(x.Key).U8(0), true);
                }
            }
        }

        static void OnFlag(int who, string key, bool on)
        {
            Flag x;
            if (!flags.TryGetValue(key, out x) || x.Go == null) { Warn(key + " introuvable ici"); return; }
            if (FlagAuthority(x)) return;
            x.Remote = on;
            if (x.Go.activeSelf != on)
            {
                Replay.Depth++;
                try { x.Go.SetActive(on); } finally { Replay.Depth--; }
                Log.Info("feux de #" + who + " : " + key + (on ? " en feu" : " eteint"));
            }
            x.LastSent = x.Go.activeSelf;
        }

        static void OnFlagRequest(int who, string key)
        {
            Flag x;
            if (!flags.TryGetValue(key, out x) || x.Go == null || !FlagAuthority(x) || !x.Go.activeSelf) return;
            // Extinction comme dans le jeu : PUTOUT a l'automate PutOut du feu (Fire off -> Reset), sinon desactive.
            foreach (PlayMakerFSM f in x.Go.GetComponentsInChildren<PlayMakerFSM>(true))
                if (f.FsmName == "PutOut" && f.gameObject.activeInHierarchy) { f.SendEvent("PUTOUT"); Log.Info("feux : " + key + " eteint a la demande de #" + who + " (PUTOUT)"); return; }
            x.Go.SetActive(false);
            Log.Info("feux : " + key + " eteint a la demande de #" + who);
        }

        // ================================================================ cycle
        public static void OnLevelLoaded()
        {
            FireTools.OnLevelLoaded();
            ruled.Clear(); ruledFsms.Clear(); places.Clear(); placeFsms.Clear(); flags.Clear(); flagGos.Clear(); fresh.Clear();
            levels.Clear(); due.Clear();
            applying = null; forceForward = false; stationFound = false;
            testStep = 0; testLog = 0; testing = false;
            loadedAt = PlayerSync.InGame ? Time.realtimeSinceStartup : -1;
            nextFind = loadedAt + 6f;   // avant WorldFsms (16 s), qui ne doit pas prendre ces automates
        }

        public static void Update()
        {
            FireTools.Update();
            if (loadedAt < 0 || !PlayerSync.InGame) return;
            float now = Time.realtimeSinceStartup;
            if (now >= nextFind)
            {
                nextFind = now + 30f;
                try { FindRuled(); } catch (System.Exception e) { Log.Warn("feux : releve des incendies : " + e.Message); }
                try { FindPlaces(); } catch (System.Exception e) { Log.Warn("feux : releve des foyers : " + e.Message); }
                try { FindFlags(); } catch (System.Exception e) { Log.Warn("feux : releve des feux de voiture : " + e.Message); }
            }
            UpdatePlaces(now);
            UpdateFlags(now);
            if (!Session.IsHost || !Session.Active) return;
            if (now >= nextArrivals) { nextArrivals = now + 1f; FireTools.Arrivals(levels, due, 25f); }
            for (int i = due.Count - 1; i >= 0; i--)
            {
                if (now < due[i].Key) continue;
                Peer p = due[i].Value;
                due.RemoveAt(i);
                int n = 0;
                foreach (Ruled r in ruled.Values)
                    if (r.F != null && RankOf(r, r.F.ActiveStateName) > 0) { SendRuled(r, r.F.ActiveStateName, p); n++; }
                SendAllPlaces(p);
                foreach (Flag x in flags.Values)
                    if (x.Go != null && x.Go.activeSelf && FlagAuthority(x)) { FireTools.SendTo(p, new NetWriter(Msg.Fire).U8(5).U8(Session.LocalId).Str(x.Key).U8(1)); n++; }
                Log.Info("feux : etat de " + places.Count + " foyers et " + n + " feux en cours envoyes a " + p);
            }
        }

        static void Warn(string s)
        {
            if (Time.realtimeSinceStartup < nextWarn) return;
            nextWarn = Time.realtimeSinceStartup + 10f;
            Log.Warn("feux : " + s);
        }

        // Messages (Msg.Fire) : [U8 sorte][U8 joueur]... ; l'hote relaie tout aux autres invites (joueur corrige).
        //  1 bois     Str cle du foyer, U8 buches, Vec place de la buche, Str ID de la buche, U8 SetFire actif  (fiable)
        //  2 foyers   n x (Str cle, U8 buches, U8 drapeaux : 1 brule 2 SetFire actif, F32 chaleur, F32 braises) (fiable)
        //  3 incendie Str cle, Str etat, U8 instantane d'arrivee                                                (fiable, de l'hote)
        //  4 demande  Str cle, Str evenement (FIRE, PUTOUT)                                                      (fiable, pour l'hote)
        //  5 drapeau  Str cle, U8 actif                                                                          (fiable, de l'autorite)
        //  6 extinction demandee  Str cle, U8 0                                                                  (fiable, pour l'autorite)
        //  7 allume   Str cle du foyer                                                                           (fiable)
        public static void OnMessage(Peer from, NetReader r)
        {
            byte[] raw = Session.IsHost ? r.Rest() : null;
            int kind = r.U8();
            int who = r.U8();
            if (Session.IsHost)
            {
                who = from.Id;
                Session.Broadcast(new NetWriter(Msg.Fire).U8(kind).U8(who).Raw(raw, 2, raw.Length - 2), true, who);
            }
            if (loadedAt < 0) return;
            switch (kind)
            {
                case 1: { string k = r.Str(); int n = r.U8(); Vector3 pos = r.Vec(); string id = r.Str(); bool on = r.More && r.U8() != 0; OnWoodMsg(who, k, n, pos, id, on); break; }
                case 2:
                    while (r.More) { string k = r.Str(); int n = r.U8(); int fl = r.U8(); float t = r.F32(), h = r.F32(); OnPlace(who, k, n, fl, t, h); }
                    break;
                case 3: { string k = r.Str(); string s = r.Str(); bool snap = r.More && r.U8() != 0; OnRuledState(who, k, s, snap); break; }
                case 4: { string k = r.Str(); string ev = r.Str(); OnRequest(who, k, ev); break; }
                case 5: { string k = r.Str(); bool on = r.U8() != 0; OnFlag(who, k, on); break; }
                case 6: { string k = r.Str(); r.U8(); OnFlagRequest(who, k); break; }
                case 7: { string k = r.Str(); OnLight(who, k); break; }
            }
        }

        // ================================================================ essais
        // [Test] Autotest=cheminee (l'hote agit) / cheminee-invite (l'invite agit) ; TestPos=-6.2,0.4,7.2 (salon, les deux).
        //   30 s : une buche (copie du modele 'firewood', sans collisions) entre dans la cheminee du salon ([Test]
        //   TestFoyer : partie de la cle, LIVINGROOM par defaut) comme si elle tombait dans le WoodTrigger ; 33 s :
        //   une 2e ; 36-37 s : le feu est allume (SetFire active puis "Start fire 2", comme le clic). Chacun note
        //   toutes les 2 s de 28 a 70 s : "autotest : cheminee <cle> bois=2 feu=<etat SetFire> actif=.. brule=..
        //   chaleur=.. braises=.. autorite=#..". Attendu : memes bois des deux cotes 1 s apres chaque buche,
        //   brule=True chez l'autre avant 40 s, chaleur a 2 pres.
        // [Test] Autotest=incendie (l'hote agit) / incendie-invite (l'invite) ; TestPos=-8.5,0.4,6.5 (cuisine) ;
        //   [Test] TestFeuDuree (s avant l'extinction, 3 par defaut ; 0 : laisser bruler -- la maison de la partie
        //   d'essai brule alors chez les deux). 30 s : la cuisine prend feu (FIRE a Kitchen/FireKitchen 'Simulation' ;
        //   chez l'invite : demande a l'hote comme une cigarette au lit). 30 s + duree : PUTOUT (comme l'extincteur).
        //   45 s (incendie-invite) : l'invite envoie FIRE a Bedroom2 de lui-meme (logique locale) : annule, pas
        //   demande. Chacun note toutes les 2 s l'etat de chaque piece, les drapeaux Data et si la cuisine existe :
        //   "autotest : incendie FireKitchen=Fire ... ; Data FireKitchen=True ... ; cuisine presente".
        //   Attendu : memes etats des deux cotes (a un pas pres), FireBedroom2 reste Wait partout.
        static int testStep;
        static float testLog;
        static bool testing;

        public static void Test(string mode, float t)
        {
            bool fireplace = mode == "cheminee" || mode == "cheminee-invite";
            bool house = mode == "incendie" || mode == "incendie-invite";
            if (!fireplace && !house) return;
            testing = true;
            bool actor = Session.IsHost != mode.EndsWith("-invite");
            if (fireplace) TestFireplace(actor, t);
            else TestHouse(actor, mode == "incendie-invite", t);
        }

        static Place TestPlace()
        {
            string part = Config.Get("Test", "TestFoyer", "LIVINGROOM");
            foreach (Place p in places.Values) if (p.Key.Contains(part)) return p;
            return null;
        }

        static void TestFireplace(bool actor, float t)
        {
            Place p = TestPlace();
            if (actor && p != null && p.Trigger != null && (t > 30f && testStep == 0 || t > 33f && testStep == 1))
            {
                testStep++;
                GameObject model = Game.FindAny("firewood");
                GameObject log = model != null ? (GameObject)Object.Instantiate(model, p.Trigger.transform.position + Vector3.up * 0.3f, Quaternion.identity) : new GameObject("firewood");
                foreach (Collider c in log.GetComponentsInChildren<Collider>()) c.enabled = false;   // pas une 2e entree par la physique
                FsmGameObject col = p.Trigger.FsmVariables.FindFsmGameObject("Collider");
                if (col != null) col.Value = log;
                Game.SetState(p.Trigger, "Destroy firewood");
                Log.Info("autotest : buche " + testStep + " dans " + p.Key + " -> " + p.Trigger.ActiveStateName + ", bois=" + Woods(p));
            }
            if (actor && p != null && t > 36f && testStep == 2)
            {
                testStep = 3;
                if (p.SetFireGo != null && !p.SetFireGo.activeSelf) p.SetFireGo.SetActive(true);
            }
            if (actor && p != null && t > 37f && testStep == 3)
            {
                testStep = 4;
                if (p.SetFire != null) Game.SetState(p.SetFire, "Start fire 2");
                Log.Info("autotest : " + p.Key + " allume -> " + (p.SetFire != null ? p.SetFire.ActiveStateName : "pas de SetFire"));
            }
            if (t > 28f && t < 70f && t - testLog >= 2f)
            {
                testLog = t;
                if (p == null) { Log.Info("autotest : cheminee : aucun foyer (" + places.Count + " suivis)"); return; }
                Log.Info("autotest : cheminee " + p.Key + " bois=" + Woods(p) + " feu=" + (p.SetFire != null ? p.SetFire.ActiveStateName : "?")
                         + " actif=" + (p.SetFireGo != null && p.SetFireGo.activeSelf) + " brule=" + Burning(p)
                         + " chaleur=" + FireTools.GetF(p.Heat, "Temperature", -1f).ToString("F1") + " braises=" + FireTools.GetF(p.Grilling, "HeatBuildup", -1f).ToString("F1")
                         + " autorite=#" + p.Auth);
            }
        }

        static Ruled Room(string room)
        {
            foreach (Ruled r in ruled.Values) if (r.Kind == House && r.Key.Contains("/" + room + "/")) return r;
            return null;
        }

        static void TestHouse(bool actor, bool guestMode, float t)
        {
            Ruled kitchen = Room("Kitchen");
            float dur = Config.GetInt("Test", "TestFeuDuree", 3);
            if (actor && kitchen != null && kitchen.F != null && t > 30f && testStep == 0)
            {
                testStep = 1;
                if (!Session.IsHost) forceForward = true;   // comme la cigarette au lit : demande a l'hote
                kitchen.F.SendEvent("FIRE");
                Log.Info("autotest : FIRE -> " + kitchen.Key + " : " + kitchen.F.ActiveStateName);
            }
            if (actor && kitchen != null && kitchen.F != null && dur > 0f && t > 30f + dur && testStep == 1)
            {
                testStep = 2;
                // Comme l'extincteur (PutOut "Put out" : PUTOUT a la simulation de la piece).
                kitchen.F.SendEvent("PUTOUT");
                Log.Info("autotest : PUTOUT -> " + kitchen.Key + " : " + kitchen.F.ActiveStateName);
            }
            if (actor && guestMode && t > 45f && testStep >= 1 && testStep < 3)
            {
                testStep = 3;
                Ruled b2 = Room("Bedroom2");
                if (b2 != null && b2.F != null) { b2.F.SendEvent("FIRE"); Log.Info("autotest : FIRE local -> " + b2.Key + " : " + b2.F.ActiveStateName); }
            }
            if (t > 28f && t < 75f && t - testLog >= 2f)
            {
                testLog = t;
                var sb = new System.Text.StringBuilder("autotest : incendie");
                PlayMakerFSM data = null;
                foreach (Ruled r in ruled.Values)
                {
                    if (r.Kind != House) continue;
                    string room = r.F != null ? r.F.gameObject.name : r.Key;
                    sb.Append(' ').Append(room).Append('=').Append(r.F != null ? r.F.ActiveStateName : "detruit");
                    if (data == null && r.F != null && r.F.transform.parent != null && r.F.transform.parent.parent != null) data = Game.FsmOn(r.F.transform.parent.parent.gameObject, "Data");
                }
                if (data != null)
                {
                    sb.Append(" ; Data");
                    foreach (FsmBool b in data.FsmVariables.BoolVariables) sb.Append(' ').Append(b.Name).Append('=').Append(b.Value);
                }
                GameObject k = Game.FindAny("YARD/Building/KITCHEN");
                sb.Append(" ; cuisine ").Append(k != null ? "presente" : "detruite");
                Log.Info(sb.ToString());
            }
        }
    }
}
