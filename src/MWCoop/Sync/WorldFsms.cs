using System.Collections.Generic;
using System.Reflection;
using HutongGames.PlayMaker;
using MWCoop.Net;
using UnityEngine;

namespace MWCoop
{
    // Tout le reste du monde : les automates que les autres modules ne suivent pas (maison, jardin,
    // Systems -- factures, courrier, electricite, telephone, loto... --, ville, garage, chalets, objets,
    // pieces, boulots sans sauvegarde...).
    //  - ACTIONS : une transition provoquee par un joueur (sortie d'un etat qui attend un clic, une
    //    touche ou la molette, ou evenement global comme le paiement d'une facture) est rejouee chez les
    //    autres. Les transitions de la logique propre (horloge, comparaisons) ne le sont pas : chacun les
    //    calcule, les rejouer les doublerait (facture ajoutee deux fois).
    //  - Jamais ce qui agit sur le joueur lui-meme ou son interface (objets sous PLAYER, GUI, feuilles
    //    Sheets, ordinateur) ni les options et la sauvegarde. Decide etat par etat : une transition n'est
    //    rejouee que si son etat d'arrivee ET tout ce qui peut s'enchainer automatiquement apres
    //    (minuteurs, comparaisons -- pas ce qui attend le joueur) ne touchent pas au joueur. Ainsi le pont
    //    elevateur ou les fusibles passent, le lit (qui finit par deplacer le joueur) non.
    //  - Chez celui qui rejoue, son argent et son corps (globales Player*) sont remis comme avant :
    //    seul celui qui paie paie, seul celui qui mange mange.
    //  - ETATS : l'hote fait reference pour les variables des automates sauvegardes (UT/UniqueTag :
    //    factures, compteur, coupure...) et pour les globales de la maison (House*) ; il envoie ce qui
    //    change chaque seconde, et un instantane complet a l'arrivee d'un invite.
    public static class WorldFsms
    {
        static readonly HashSet<string> SkipRoots = new HashSet<string> { "PLAYER", "GUI", "Sheets", "COMPUTER", "TRAFFIC", "NPC_CARS", "Spawner", "Radio" };
        static readonly HashSet<string> PersonalRoots = new HashSet<string> { "PLAYER", "GUI", "Sheets", "COMPUTER" };
        static readonly string[] SkipObjects = { "OptionsDB", "InitializeControls", "Photomode", "Statistics", "Setup Game", "SAVEGAME", "BankAccount", "Expenses", "PlayerWanted",
                                                 "Cashier", "CashRegister", "INVENTORY" };
        // "Buy" : prendre un article en rayon le met dans SON panier ; c'est la caisse qui est synchronisee (Shop).
        static readonly HashSet<string> SkipFsmNames = new HashSet<string> { "Paint", "LOD", "Death", "HeadForce", "Coldness", "Strafe", "Buy" };
        // Miroir de l'hote : seulement les systemes de la maison et du monde (pas les machines qu'un invite
        // utilise en ce moment, comme une pompe a essence : l'hote ecraserait son compteur).
        static readonly HashSet<string> MirrorRoots = new HashSet<string> { "Systems", "HOMENEW", "YARD", "CABIN", "COTTAGE" };
        static readonly HashSet<string> Ignore = new HashSet<string> { "FINISHED", "SAVEGAME", "LOAD", "EXISTS", "NOTEXISTS", "DONOTEXIST", "DOESNOTEXIST", "SAVE", "LOOP" };
        static readonly HashSet<string> InputActions = new HashSet<string> { "MousePickEvent", "GetButtonDown", "GetButtonUp", "GetMouseButtonDown", "GetMouseButtonUp", "GetAxis", "GetKeyDown", "GetButton" };

        class W
        {
            public string Key; public PlayMakerFSM F; public bool Persistent, Mirror;
            public bool HostDriven, External;   // voir HostDrivenFsm / ExternalFsm
            public HashSet<string> InputStates = new HashSet<string>();
            public HashSet<string> GlobalEvents = new HashSet<string>();
            public float WindowStart, NoisySince; public int Count; public bool Noisy;
            public HashSet<string> Entered = new HashSet<string>();   // etats traverses pendant un rejeu
            public HashSet<string> PersonalStates = new HashSet<string>();
            public Dictionary<string, HashSet<string>> InputEvents = new Dictionary<string, HashSet<string>>();
            public Dictionary<string, bool> SafeCache = new Dictionary<string, bool>();
            public Dictionary<string, float> Sent = new Dictionary<string, float>();   // miroir (hote)
            public Dictionary<string, string> SentLists = new Dictionary<string, string>();
        }

        static readonly Dictionary<string, W> byKey = new Dictionary<string, W>();
        static readonly HashSet<PlayMakerFSM> hooked = new HashSet<PlayMakerFSM>();
        static readonly HashSet<PlayMakerFSM> rejected = new HashSet<PlayMakerFSM>();
        static readonly HashSet<PlayMakerFSM> known = new HashSet<PlayMakerFSM>();   // suivis ou en attente
        static readonly List<W> pending = new List<W>();
        static float nextPending;
        static readonly List<KeyValuePair<float, Peer>> snapshots = new List<KeyValuePair<float, Peer>>();
        static readonly Dictionary<string, float> houseSent = new Dictionary<string, float>();
        static float nextScan = -1, nextMirror, loadedAt, nextWarn, lastInput = -100, nextStopCheck;
        static bool applying;
        static int sentEvents, recvEvents;

        class Hook : FsmStateAction
        {
            public W J; public string State;
            public override void OnEnter() { if (!applying) OnLocal(J, State); else J.Entered.Add(State); Finish(); }
        }

        public static bool Tracks(PlayMakerFSM f) { return hooked.Contains(f); }

        // Logique tiree au hasard qui doit etre la meme pour tous : seul l'hote la fait tourner, les
        // invites la suivent etat par etat (leur copie est arretee). Telephone : qui appelle et quand.
        static bool HostDrivenFsm(PlayMakerFSM f)
        {
            return (f.FsmName == "Ring" || f.FsmName == "Jokes") && f.gameObject.name.StartsWith("PhoneLogic");
        }

        // Automates commandes par un AUTRE automate apres une action du joueur (decrocher le telephone
        // envoie ANSWER a la sonnerie) : ces evenements-la comptent comme une action du joueur.
        static bool ExternalFsm(PlayMakerFSM f) { return f.gameObject.name.StartsWith("Ringing"); }

        // Variables objet envoyees par leur chemin (la fiche de la commande en cours).
        static bool GoVar(string n) { return n == "CurrentListing" || n == "FoundListing"; }
        public static int Count { get { return byKey.Count; } }

        public static void OnLevelLoaded()
        {
            byKey.Clear(); hooked.Clear(); rejected.Clear(); snapshots.Clear(); houseSent.Clear(); known.Clear(); pending.Clear();
            loadedAt = Time.realtimeSinceStartup;
            nextScan = PlayerSync.InGame ? loadedAt + 16f : -1;
        }

        public static void ScheduleSnapshot(Peer p) { snapshots.Add(new KeyValuePair<float, Peer>(Time.realtimeSinceStartup + 22f, p)); }

        // ---------------------------------------------------------------- choix des automates
        static bool Skip(PlayMakerFSM f)
        {
            Transform root = f.transform.root;
            if (!root.gameObject.activeInHierarchy) return true;                        // modeles (prefabs)
            if (SkipRoots.Contains(root.name) || root.name.StartsWith("MWCoop")) return true;
            if (root.GetComponent("CarDynamics") != null) return true;                  // vehicules : Jobs, CarDoors...
            if (SkipFsmNames.Contains(f.FsmName)) return true;
            string n = f.gameObject.name;
            if (n.Contains("(itemx)") || n.Contains("(item")) return true;              // objets portes : Props, Consume
            foreach (string s in SkipObjects) if (n.Contains(s) || root.name.Contains(s)) return true;
            if (f.FsmName == "Data" && Parts.IdOf(f.gameObject).Length > 0) return true;  // pieces : Parts
            if (n.StartsWith("VINP")) return true;                                       // points de montage : Parts
            if (Interactions.Tracks(f) || Interactions.Wants(f) || Jobs.Tracks(f) || CarDoors.Tracks(f) || Consume.Tracks(f)) return true;
            if (n == "CashRegisterLogic") return true;                                    // magasin : Shop
            // Createurs d'objets (pieces, articles) : jamais rejoues directement -- c'est l'action qui les
            // declenche (ouvrir un colis, passer une commande) qui l'est, sinon l'objet apparaitrait en
            // double. Sauf les createurs de COMMANDES (OrdersSpawner*), seul chemin de la commande.
            try { if (f.Fsm.GetState("Create product") != null && !n.StartsWith("OrdersSpawner")) return true; } catch { }
            return false;
        }

        // Lit les actions : commandes du joueur, references au joueur / a son interface, SAVEGAME.
        // 'personal' en sortie : l'automate touche a la sauvegarde (jamais rejoue du tout). Les etats qui
        // touchent au joueur sont notes un par un (PersonalStates).
        static bool Classify(PlayMakerFSM f, W w, out bool personal)
        {
            personal = false;
            foreach (FsmString s in f.FsmVariables.StringVariables)
                if (s.Name.StartsWith("UniqueTag") || s.Name.StartsWith("UT")) w.Persistent = true;
            foreach (FsmTransition t in f.Fsm.GlobalTransitions) if (!Ignore.Contains(t.EventName)) w.GlobalEvents.Add(t.EventName);
            foreach (FsmState st in f.Fsm.States)
            {
                var inEv = new HashSet<string>();
                foreach (FsmStateAction a in st.Actions)
                {
                    if (a == null) continue;
                    bool input = InputActions.Contains(a.GetType().Name);
                    if (input) w.InputStates.Add(st.Name);
                    foreach (FieldInfo fi in a.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
                    {
                        object v = fi.GetValue(a);
                        GameObject go = null;
                        if (v is FsmGameObject) go = ((FsmGameObject)v).Value;
                        else if (v is FsmOwnerDefault) { var od = (FsmOwnerDefault)v; if (od.OwnerOption != OwnerDefaultOption.UseOwner) go = od.GameObject.Value; }
                        if (go != null && PersonalRoots.Contains(go.transform.root.name)) w.PersonalStates.Add(st.Name);
                        var nv = v as NamedVariable;
                        if (nv != null && nv.UseVariable && nv.Name.StartsWith("Player") && f.FsmVariables.GetVariable(nv.Name) == null
                            && (v is FsmGameObject || nv.Name == "PlayerStop" || nv.Name == "PlayerInMenu" || nv.Name == "PlayerSeated" || nv.Name == "PlayerSleeps"))
                            w.PersonalStates.Add(st.Name);
                        if (v is FsmEvent && ((FsmEvent)v).Name == "SAVEGAME") personal = true;
                        if (v is FsmString && ((FsmString)v).Value == "SAVEGAME") personal = true;
                        if (input && v is FsmEvent && v != null) inEv.Add(((FsmEvent)v).Name);
                    }
                }
                w.InputEvents[st.Name] = inEv;
            }
            w.HostDriven = HostDrivenFsm(f);
            w.External = ExternalFsm(f);
            return w.Persistent || w.InputStates.Count > 0 || w.GlobalEvents.Count > 0 || w.HostDriven || w.External;
        }

        // L'etat 'state' et tout ce qui s'enchaine automatiquement apres lui laissent-ils le joueur tranquille ?
        static bool Safe(W w, string state)
        {
            bool r;
            if (w.SafeCache.TryGetValue(state, out r)) return r;
            var seen = new HashSet<string>();
            var todo = new Stack<string>();
            todo.Push(state);
            r = true;
            while (todo.Count > 0 && r)
            {
                string s = todo.Pop();
                if (!seen.Add(s)) continue;
                if (w.PersonalStates.Contains(s)) { r = false; break; }
                FsmState st = w.F.Fsm.GetState(s);
                if (st == null) continue;
                HashSet<string> inEv;
                w.InputEvents.TryGetValue(s, out inEv);
                foreach (FsmTransition t in st.Transitions)
                    if (inEv == null || !inEv.Contains(t.EventName)) todo.Push(t.ToState);   // automatique
            }
            w.SafeCache[state] = r;
            return r;
        }

        static void Scan()
        {
            int added = 0;
            var seen = new Dictionary<string, int>();
            var all = new List<KeyValuePair<string, PlayMakerFSM>>();
            foreach (Object o in Resources.FindObjectsOfTypeAll(typeof(PlayMakerFSM)))
            {
                var f = (PlayMakerFSM)o;
                if (f.hideFlags != HideFlags.None || known.Contains(f) || rejected.Contains(f)) continue;
                if (Skip(f)) { if (f.transform.root.gameObject.activeInHierarchy) rejected.Add(f); continue; }
                all.Add(new KeyValuePair<string, PlayMakerFSM>(Recon.Path(f.transform) + "::" + f.FsmName, f));
            }
            all.Sort((a, b) => string.CompareOrdinal(a.Key, b.Key));
            foreach (KeyValuePair<string, PlayMakerFSM> kv in all)
            {
                int k; seen.TryGetValue(kv.Key, out k); seen[kv.Key] = k + 1;
                PlayMakerFSM f = kv.Value;
                // Miroir aussi des petites annonces de pieces (PhoneNumbers, tirees au hasard chaque semaine).
                bool mirror = MirrorRoots.Contains(f.transform.root.name) || f.FsmName == "Fuelprices" || kv.Key.Contains("/PhoneNumbers/");
                var w = new W { Key = kv.Key + "#" + k, F = f, Mirror = mirror };
                known.Add(f);
                // Objet inactif : ses actions ne sont pas chargees ; on le reprend quand il s'active.
                if (!f.gameObject.activeInHierarchy || !TryHook(w)) pending.Add(w);
            }
            LogAdded();
        }

        static int addedSinceLog;
        static void LogAdded()
        {
            if (addedSinceLog == 0) return;
            int p = 0; foreach (W x in byKey.Values) if (x.Persistent) p++;
            Log.Info("monde : " + addedSinceLog + " automates de plus suivis (" + byKey.Count + " en tout, dont " + p + " sauvegardes, " + pending.Count + " en attente)");
            addedSinceLog = 0;
        }

        // Vrai : traite (suivi ou ecarte pour de bon) ; faux : a reprendre plus tard.
        static bool TryHook(W w)
        {
            PlayMakerFSM f = w.F;
            if (f == null) return true;
            bool personal;
            try { if (!Classify(f, w, out personal)) { rejected.Add(f); return true; } }
            catch { return false; }
            if (personal) { rejected.Add(f); return true; }
            if (byKey.ContainsKey(w.Key)) return true;
            try
            {
                foreach (FsmState st in f.Fsm.States)
                {
                    var list = new List<FsmStateAction>(st.Actions);
                    list.Insert(0, new Hook { J = w, State = st.Name });
                    st.Actions = list.ToArray();
                }
            }
            catch { return false; }
            hooked.Add(f);
            byKey[w.Key] = w;
            addedSinceLog++;
            return true;
        }

        // Toutes les 2 s : les automates en attente dont l'objet vient de s'activer.
        static void CheckPending()
        {
            for (int i = pending.Count - 1; i >= 0; i--)
            {
                W w = pending[i];
                if (w.F == null) { pending.RemoveAt(i); continue; }
                if (w.F.gameObject.activeInHierarchy && TryHook(w)) pending.RemoveAt(i);
            }
            LogAdded();
        }

        // ---------------------------------------------------------------- actions des joueurs
        static void OnLocal(W j, string state)
        {
            if (!Session.Active || Session.RemoteCount == 0 || Time.realtimeSinceStartup - loadedAt < 25f) return;
            FsmTransition tr = j.F.Fsm.LastTransition;
            FsmState prev = j.F.Fsm.PreviousActiveState;
            float now = Time.realtimeSinceStartup;
            if (j.HostDriven)
            {
                // Logique de l'hote : chaque etat est envoye (FINISHED compris), les invites s'y recalent.
                if (Session.IsHost) Send(j, prev, tr != null ? tr.EventName : "", 2, state);
                return;
            }
            if (tr == null || Ignore.Contains(tr.EventName) || tr.ToState != state) return;
            if (j.External)
            {
                // Decroche (ANSWER), ou autre evenement juste apres une action du joueur : chez les autres,
                // l'appel est rejoue sans voix ni sous-titres, puis le telephone raccroche.
                if (now - lastInput <= 1f) Send(j, prev, tr.EventName, 3, state);
                return;
            }
            if (state == "Wait player" || state == "Mouse off" || state == "Mouse off 2" || state == "Wait button") return;
            bool global = j.GlobalEvents.Contains(tr.EventName);
            bool byPlayer = prev != null && j.InputStates.Contains(prev.Name);
            // Provoquee par le joueur : il vient d'agir (clic, touche, molette) ET la transition sort d'un
            // etat qui l'ecoute, ou c'est un evenement global (paiement...). Le reste (horloge, radio,
            // reveil...) tourne pareil chez chacun : le rejouer le doublerait.
            // Les commandes (OrdersSpawner*) arrivent apres l'appel ou le courrier, longtemps apres le
            // dernier clic : seul celui qui commande les declenche, elles passent toujours.
            bool order = global && j.Key.Contains("OrdersSpawner");
            if ((!global && !byPlayer) || (now - lastInput > 1f && !order)) return;
            if (!Safe(j, state)) return;   // finirait par agir sur ce joueur-ci chez l'autre
            if (j.Noisy && now - j.NoisySince > 30f) { j.Noisy = false; j.WindowStart = now; j.Count = 0; }
            if (now - j.WindowStart > 10f) { j.WindowStart = now; j.Count = 0; }
            if (++j.Count > 40 || j.Noisy)
            {
                if (!j.Noisy) { Log.Warn("monde : " + j.Key + " change trop souvent, en pause 30 s"); j.Noisy = true; j.NoisySince = now; }
                return;
            }
            Send(j, prev, tr.EventName, global ? 1 : 0, state);
        }

        // mode : 0 meme etat de depart -> meme evenement ; 1 evenement global ; 2 recalage direct (hote) ;
        // 3 rejeu muet (actions qui touchent au joueur coupees) puis repos.
        static void Send(W j, FsmState prev, string ev, int mode, string state)
        {
            var w = new NetWriter(Msg.WorldFsm).U8(Session.LocalId).Str(j.Key).Str(prev != null ? prev.Name : "").Str(ev).U8(mode).Str(state);
            WriteVars(j.F, w);
            if (++sentEvents <= 30 || sentEvents % 50 == 0) Log.Info("monde : " + j.Key + " " + (prev != null ? prev.Name : "?") + " -" + ev + "-> " + state);
            Session.SendAll(w, true);
        }

        static bool SkipVar(string n) { return n.StartsWith("UT") || n.StartsWith("UniqueTag"); }

        // Actions qui touchent au joueur local : son, camera, interface (sous-titres), globales Player*.
        static List<FsmStateAction> Mute(W j)
        {
            var r = new List<FsmStateAction>();
            foreach (FsmState st in j.F.Fsm.States)
                foreach (FsmStateAction a in st.Actions)
                {
                    if (a == null || a is Hook || !a.Enabled) continue;
                    bool personal = false;
                    foreach (FieldInfo fi in a.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
                    {
                        object v = fi.GetValue(a);
                        GameObject go = null;
                        if (v is FsmGameObject) go = ((FsmGameObject)v).Value;
                        else if (v is FsmOwnerDefault) { var od = (FsmOwnerDefault)v; if (od.OwnerOption != OwnerDefaultOption.UseOwner) go = od.GameObject.Value; }
                        if (go != null && PersonalRoots.Contains(go.transform.root.name)) personal = true;
                        var nv = v as NamedVariable;
                        if (nv != null && nv.UseVariable && (nv.Name.StartsWith("GUI") || nv.Name.StartsWith("Player")) && j.F.FsmVariables.GetVariable(nv.Name) == null) personal = true;
                    }
                    if (a.GetType().Name.StartsWith("MasterAudio")) personal = true;   // la voix de l'appel, la tonalite
                    if (personal) { a.Enabled = false; r.Add(a); }
                }
            return r;
        }

        static void WriteVars(PlayMakerFSM f, NetWriter w)
        {
            FsmVariables v = f.FsmVariables;
            var ints = new List<FsmInt>(); foreach (FsmInt x in v.IntVariables) if (!SkipVar(x.Name)) ints.Add(x);
            var floats = new List<FsmFloat>(); foreach (FsmFloat x in v.FloatVariables) if (!SkipVar(x.Name)) floats.Add(x);
            var bools = new List<FsmBool>(); foreach (FsmBool x in v.BoolVariables) if (!SkipVar(x.Name)) bools.Add(x);
            w.U8(System.Math.Min(ints.Count, 255)); for (int i = 0; i < ints.Count && i < 255; i++) w.Str(ints[i].Name).I32(ints[i].Value);
            w.U8(System.Math.Min(floats.Count, 255)); for (int i = 0; i < floats.Count && i < 255; i++) w.Str(floats[i].Name).F32(floats[i].Value);
            w.U8(System.Math.Min(bools.Count, 255)); for (int i = 0; i < bools.Count && i < 255; i++) w.Str(bools[i].Name).Bool(bools[i].Value);
            var strs = new List<FsmString>(); foreach (FsmString x in v.StringVariables) if (!SkipVar(x.Name) && (x.Value ?? "").Length < 200) strs.Add(x);
            w.U8(System.Math.Min(strs.Count, 255)); for (int i = 0; i < strs.Count && i < 255; i++) w.Str(strs[i].Name).Str(strs[i].Value);
            var gos = new List<FsmGameObject>(); foreach (FsmGameObject x in v.GameObjectVariables) if (GoVar(x.Name)) gos.Add(x);
            w.U8(gos.Count); foreach (FsmGameObject x in gos) w.Str(x.Name).Str(x.Value != null ? Recon.Path(x.Value.transform) : "");
            WriteLists(f, w);
        }

        // Listes du jeu (ArrayMaker) : celles de l'objet, et celle de la commande en cours (CurrentListing).
        static List<KeyValuePair<string, PlayMakerArrayListProxy>> Lists(PlayMakerFSM f)
        {
            var l = new List<KeyValuePair<string, PlayMakerArrayListProxy>>();
            foreach (PlayMakerArrayListProxy p in f.GetComponents<PlayMakerArrayListProxy>()) l.Add(new KeyValuePair<string, PlayMakerArrayListProxy>("", p));
            FsmGameObject cl = f.FsmVariables.FindFsmGameObject("CurrentListing");
            if (cl != null && cl.Value != null)
                foreach (PlayMakerArrayListProxy p in cl.Value.GetComponents<PlayMakerArrayListProxy>()) l.Add(new KeyValuePair<string, PlayMakerArrayListProxy>("CurrentListing", p));
            return l;
        }

        static void WriteLists(PlayMakerFSM f, NetWriter w)
        {
            List<KeyValuePair<string, PlayMakerArrayListProxy>> l = Lists(f);
            w.U8(System.Math.Min(l.Count, 16));
            for (int k = 0; k < l.Count && k < 16; k++)
            {
                System.Collections.ArrayList a = l[k].Value._arrayList;
                int n = a != null ? System.Math.Min(a.Count, 120) : 0;
                w.Str(l[k].Key).Str(l[k].Value.referenceName ?? "").U16(n);
                for (int i = 0; i < n; i++)
                {
                    object o = a[i];
                    if (o is int) w.U8(0).I32((int)o);
                    else if (o is float) w.U8(1).F32((float)o);
                    else if (o is string) w.U8(2).Str((string)o);
                    else if (o is bool) w.U8(3).Bool((bool)o);
                    else if (o is GameObject && (GameObject)o != null) w.U8(4).Str(Recon.Path(((GameObject)o).transform));
                    else w.U8(5);
                }
            }
        }

        class ListData { public string Owner, Ref; public List<object> Items = new List<object>(); }

        static List<ListData> ReadLists(NetReader r)
        {
            var res = new List<ListData>();
            if (!r.More) return res;
            int c = r.U8();
            for (int k = 0; k < c; k++)
            {
                var d = new ListData { Owner = r.Str(), Ref = r.Str() };
                int n = r.U16();
                for (int i = 0; i < n; i++)
                {
                    int t = r.U8();
                    if (t == 0) d.Items.Add(r.I32());
                    else if (t == 1) d.Items.Add(r.F32());
                    else if (t == 2) d.Items.Add(r.Str());
                    else if (t == 3) d.Items.Add(r.Bool());
                    else if (t == 4) d.Items.Add(Game.FindAny(r.Str()));
                    else d.Items.Add(null);
                }
                res.Add(d);
            }
            return res;
        }

        static void WriteListData(NetWriter w, List<ListData> lists)
        {
            w.U8(lists.Count);
            foreach (ListData d in lists)
            {
                w.Str(d.Owner).Str(d.Ref).U16(d.Items.Count);
                foreach (object o in d.Items)
                {
                    if (o is int) w.U8(0).I32((int)o);
                    else if (o is float) w.U8(1).F32((float)o);
                    else if (o is string) w.U8(2).Str((string)o);
                    else if (o is bool) w.U8(3).Bool((bool)o);
                    else if (o is GameObject && (GameObject)o != null) w.U8(4).Str(Recon.Path(((GameObject)o).transform));
                    else w.U8(5);
                }
            }
        }

        static void ApplyLists(PlayMakerFSM f, List<ListData> lists)
        {
            List<KeyValuePair<string, PlayMakerArrayListProxy>> mine = Lists(f);
            foreach (ListData d in lists)
                foreach (KeyValuePair<string, PlayMakerArrayListProxy> kv in mine)
                {
                    if (kv.Key != d.Owner || (kv.Value.referenceName ?? "") != d.Ref) continue;
                    System.Collections.ArrayList a = kv.Value._arrayList;
                    if (a == null) break;
                    a.Clear();
                    foreach (object o in d.Items) a.Add(o);
                    break;
                }
        }

        // Argent et corps du joueur local : notes avant un rejeu, remis apres.
        class Personal { public List<KeyValuePair<FsmFloat, float>> F = new List<KeyValuePair<FsmFloat, float>>(); public List<KeyValuePair<FsmInt, int>> I = new List<KeyValuePair<FsmInt, int>>(); }
        static Personal SavePersonal()
        {
            var p = new Personal();
            foreach (FsmFloat x in FsmVariables.GlobalVariables.FloatVariables) if (x.Name.StartsWith("Player")) p.F.Add(new KeyValuePair<FsmFloat, float>(x, x.Value));
            foreach (FsmInt x in FsmVariables.GlobalVariables.IntVariables) if (x.Name.StartsWith("Player")) p.I.Add(new KeyValuePair<FsmInt, int>(x, x.Value));
            return p;
        }
        static void RestorePersonal(Personal p)
        {
            foreach (var kv in p.F) kv.Key.Value = kv.Value;
            foreach (var kv in p.I) kv.Key.Value = kv.Value;
        }

        public static void OnMessage(Peer from, NetReader r)
        {
            int who = r.U8();
            if (Session.IsHost) who = from.Id;
            string key = r.Str(), prev = r.Str(), ev = r.Str();
            int global = r.U8();
            string state = r.Str();
            var ints = new List<KeyValuePair<string, int>>();
            var floats = new List<KeyValuePair<string, float>>();
            var bools = new List<KeyValuePair<string, bool>>();
            for (int i = 0, n = r.U8(); i < n; i++) ints.Add(new KeyValuePair<string, int>(r.Str(), r.I32()));
            for (int i = 0, n = r.U8(); i < n; i++) floats.Add(new KeyValuePair<string, float>(r.Str(), r.F32()));
            for (int i = 0, n = r.U8(); i < n; i++) bools.Add(new KeyValuePair<string, bool>(r.Str(), r.Bool()));
            var strs = new List<KeyValuePair<string, string>>();
            var gos = new List<KeyValuePair<string, string>>();
            for (int i = 0, n = r.U8(); i < n; i++) strs.Add(new KeyValuePair<string, string>(r.Str(), r.Str()));
            for (int i = 0, n = r.U8(); i < n; i++) gos.Add(new KeyValuePair<string, string>(r.Str(), r.Str()));
            List<ListData> lists = ReadLists(r);
            if (Session.IsHost)
            {
                var w = new NetWriter(Msg.WorldFsm).U8(who).Str(key).Str(prev).Str(ev).U8(global).Str(state);
                w.U8(ints.Count); foreach (var x in ints) w.Str(x.Key).I32(x.Value);
                w.U8(floats.Count); foreach (var x in floats) w.Str(x.Key).F32(x.Value);
                w.U8(bools.Count); foreach (var x in bools) w.Str(x.Key).Bool(x.Value);
                w.U8(strs.Count); foreach (var x in strs) w.Str(x.Key).Str(x.Value);
                w.U8(gos.Count); foreach (var x in gos) w.Str(x.Key).Str(x.Value);
                WriteListData(w, lists);
                Session.Broadcast(w, true, who);
            }
            W j;
            if (!byKey.TryGetValue(key, out j) || j.F == null)
            {
                if (Time.realtimeSinceStartup >= nextWarn) { nextWarn = Time.realtimeSinceStartup + 10f; Log.Warn("monde : " + key + " introuvable ici"); }
                return;
            }
            FsmVariables v = j.F.FsmVariables;
            Personal mine = SavePersonal();
            applying = true;
            try
            {
                foreach (var x in ints) { FsmInt t = v.FindFsmInt(x.Key); if (t != null) t.Value = x.Value; }
                foreach (var x in floats) { FsmFloat t = v.FindFsmFloat(x.Key); if (t != null) t.Value = x.Value; }
                foreach (var x in bools) { FsmBool t = v.FindFsmBool(x.Key); if (t != null) t.Value = x.Value; }
                foreach (var x in strs) { FsmString t = v.FindFsmString(x.Key); if (t != null) t.Value = x.Value; }
                foreach (var x in gos)
                {
                    FsmGameObject t = v.FindFsmGameObject(x.Key);
                    if (t == null) continue;
                    GameObject go = x.Value.Length > 0 ? Game.FindAny(x.Value) : null;
                    if (go != null || x.Value.Length == 0) t.Value = go;
                }
                ApplyLists(j.F, lists);
                // Evenement global (paiement...) : renvoye tel quel ; sinon meme etat de depart -> meme
                // evenement, et a defaut recalage direct sur l'etat d'arrivee (ses actions sont jouees).
                // (Un etat de passage deja traverse par l'evenement n'est pas rejoue une 2e fois.)
                j.Entered.Clear();
                if (global == 2) { if (j.F.Fsm.GetState(state) != null) Game.SetState(j.F, state); }   // logique de l'hote
                else if (global == 3)
                {
                    // Les consequences de l'appel (boulot accepte, repere sur la carte, drapeaux) ont lieu
                    // ici aussi, mais la voix et les sous-titres restent chez celui qui a decroche.
                    List<FsmStateAction> muted = Mute(j);
                    try { if (j.F.ActiveStateName == prev) j.F.SendEvent(ev); }
                    finally { foreach (FsmStateAction a in muted) a.Enabled = true; }
                    if (j.F.Fsm.GetState("Disable phone") != null) Game.SetState(j.F, "Disable phone");
                }
                else if (global == 1 || j.F.ActiveStateName == prev) j.F.SendEvent(ev);
                if (global < 2 && !j.Entered.Contains(state) && j.F.ActiveStateName != state && j.F.Fsm.GetState(state) != null) Game.SetState(j.F, state);
            }
            finally { applying = false; RestorePersonal(mine); }
            if (++recvEvents <= 30 || recvEvents % 50 == 0) Log.Info("monde de #" + who + " : " + key + " -" + ev + "-> " + j.F.ActiveStateName + " (voulu " + state + ")");
        }

        // ---------------------------------------------------------------- miroir des etats (hote)
        public static void Update()
        {
            // [Coop] SynchroMonde=0 coupe ce module (au cas ou il generait en partie).
            if (!Session.Active || nextScan < 0 || Config.GetInt("Coop", "SynchroMonde", 1) == 0) return;
            float now = Time.realtimeSinceStartup;
            // Releve complet toutes les 60 s (objets crees en jeu) ; les objets qui s'activent, toutes les 2 s.
            if (now >= nextScan) { nextScan = now + 60f; Scan(); }
            else if (now >= nextPending) { nextPending = now + 2f; CheckPending(); }
            if (Input.anyKeyDown || Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1) || Input.GetAxis("Mouse ScrollWheel") != 0f) lastInput = now;
            if (!Session.IsHost && now >= nextStopCheck)
            {
                nextStopCheck = now + 2f;
                foreach (W x in byKey.Values)
                    if (x.HostDriven && x.F != null && x.F.enabled) { x.F.enabled = false; Log.Info("monde : " + x.Key + " suit l'hote"); }
            }
            if (!Session.IsHost || Session.RemoteCount == 0) return;
            for (int i = snapshots.Count - 1; i >= 0; i--)
            {
                if (now < snapshots[i].Key) continue;
                Peer p = snapshots[i].Value;
                snapshots.RemoveAt(i);
                if (p.Accepted && Session.T.Peers.Contains(p)) Mirror(p, true);
            }
            if (now < nextMirror) return;
            nextMirror = now + 1f;
            Mirror(null, false);
        }

        static bool Changed(Dictionary<string, float> sent, string k, float v)
        {
            float old;
            if (sent.TryGetValue(k, out old) && Mathf.Abs(old - v) <= 0.005f + 0.002f * Mathf.Abs(v)) return false;
            sent[k] = v;
            return true;
        }

        // Lot : [cle][nb][(type, nom, valeur)...] ; cle "" = globales de la maison.
        static void Mirror(Peer only, bool all)
        {
            NetWriter w = null;
            int n = 0;
            System.Action flush = () => { if (w == null) return; if (only != null) Session.T.SendReliable(only, w.ToArray()); else Session.SendAll(w, true); w = null; };
            var entries = new List<KeyValuePair<string, object>>();
            foreach (W j in byKey.Values)
            {
                if (!j.Persistent || !j.Mirror || j.F == null) continue;
                entries.Clear();
                foreach (FsmFloat x in j.F.FsmVariables.FloatVariables) if (!SkipVar(x.Name) && (all || Changed(j.Sent, x.Name, x.Value))) entries.Add(new KeyValuePair<string, object>(x.Name, x.Value));
                foreach (FsmInt x in j.F.FsmVariables.IntVariables) if (!SkipVar(x.Name) && (all || Changed(j.Sent, "i:" + x.Name, x.Value))) entries.Add(new KeyValuePair<string, object>(x.Name, x.Value));
                foreach (FsmBool x in j.F.FsmVariables.BoolVariables) if (!SkipVar(x.Name) && (all || Changed(j.Sent, "b:" + x.Name, x.Value ? 1 : 0))) entries.Add(new KeyValuePair<string, object>(x.Name, x.Value));
                // Listes de l'objet (annonces, numeros tires...) : envoyees quand leur contenu change.
                foreach (PlayMakerArrayListProxy pr in j.F.GetComponents<PlayMakerArrayListProxy>())
                {
                    System.Collections.ArrayList a = pr._arrayList;
                    if (a == null || a.Count > 60) continue;
                    var items = new List<object>(a.Count);
                    var sig = new System.Text.StringBuilder();
                    foreach (object o in a) { items.Add(o); sig.Append(o).Append('|'); }
                    string name = pr.referenceName ?? "", old;
                    if (!all && j.SentLists.TryGetValue(name, out old) && old == sig.ToString()) continue;
                    j.SentLists[name] = sig.ToString();
                    if (Config.GetInt("Test", "JournalListes", 0) != 0) Log.Info("liste envoyee " + j.Key + " / " + name + " : " + Short(sig.ToString()));
                    entries.Add(new KeyValuePair<string, object>(name, items));
                }
                if (entries.Count == 0) continue;
                AddEntries(ref w, ref n, j.Key, entries, flush);
            }
            entries.Clear();
            foreach (FsmFloat x in FsmVariables.GlobalVariables.FloatVariables) if (x.Name.StartsWith("House") && (all || Changed(houseSent, x.Name, x.Value))) entries.Add(new KeyValuePair<string, object>(x.Name, x.Value));
            foreach (FsmBool x in FsmVariables.GlobalVariables.BoolVariables) if (x.Name.StartsWith("House") && (all || Changed(houseSent, "b:" + x.Name, x.Value ? 1 : 0))) entries.Add(new KeyValuePair<string, object>(x.Name, x.Value));
            if (entries.Count > 0) AddEntries(ref w, ref n, "", entries, flush);
            flush();
            if (all) Log.Info("monde : etat de " + byKey.Count + " automates envoye a " + only);
        }

        // Chaque entree est encodee a part pour connaitre sa taille exacte ; un automate trop gros
        // est coupe en plusieurs blocs (meme cle), une liste seule trop grosse est laissee de cote.
        static void AddEntries(ref NetWriter w, ref int n, string key, List<KeyValuePair<string, object>> entries, System.Action flush)
        {
            int head = 3 + System.Text.Encoding.UTF8.GetByteCount(key);
            var block = new List<byte[]>();
            int blockLen = head;
            for (int i = 0; i <= entries.Count; i++)
            {
                byte[] e = i < entries.Count ? Encode(entries[i]) : null;
                if (e != null && e.Length + head > 900) { Log.Warn("monde : " + entries[i].Key + " de " + key + " trop gros (" + e.Length + " o), pas envoye"); continue; }
                if (block.Count > 0 && (e == null || blockLen + e.Length > 900 || block.Count == 255))
                {
                    if (w != null && w.Length + blockLen > 1000) flush();
                    if (w == null) w = new NetWriter(Msg.WorldVars);
                    w.Str(key).U8(block.Count);
                    foreach (byte[] x in block) w.Raw(x);
                    n++;
                    block.Clear(); blockLen = head;
                }
                if (e != null) { block.Add(e); blockLen += e.Length; }
            }
        }

        static string Short(string s) { return s.Length > 120 ? s.Substring(0, 120) + "..." : s; }

        static byte[] Encode(KeyValuePair<string, object> entry)
        {
            var w = new NetWriter(Msg.WorldVars);
            object v = entry.Value;
            if (v is float) w.U8(0).Str(entry.Key).F32((float)v);
            else if (v is int) w.U8(1).Str(entry.Key).I32((int)v);
            else if (v is bool) w.U8(2).Str(entry.Key).Bool((bool)v);
            else
            {
                var items = (List<object>)v;
                w.U8(3).Str(entry.Key).U16(items.Count);
                foreach (object o in items)
                {
                    if (o is int) w.U8(0).I32((int)o);
                    else if (o is float) w.U8(1).F32((float)o);
                    else if (o is string) w.U8(2).Str((string)o);
                    else if (o is bool) w.U8(3).Bool((bool)o);
                    else if (o is GameObject && (GameObject)o != null) w.U8(4).Str(Recon.Path(((GameObject)o).transform));
                    else w.U8(5);
                }
            }
            byte[] all = w.ToArray();
            var r = new byte[all.Length - 1];
            System.Buffer.BlockCopy(all, 1, r, 0, r.Length);   // sans l'octet de type du message
            return r;
        }

        public static void OnVars(Peer from, NetReader r)
        {
            if (Session.IsHost) return;
            while (r.More)
            {
                string key = r.Str();
                int n = r.U8();
                W j = null;
                if (key.Length > 0) byKey.TryGetValue(key, out j);
                for (int i = 0; i < n; i++)
                {
                    int t = r.U8();
                    string name = r.Str();
                    if (t == 0)
                    {
                        float v = r.F32();
                        FsmFloat x = key.Length == 0 ? FsmVariables.GlobalVariables.FindFsmFloat(name) : j != null && j.F != null ? j.F.FsmVariables.FindFsmFloat(name) : null;
                        if (x != null) x.Value = v;
                    }
                    else if (t == 1)
                    {
                        int v = r.I32();
                        FsmInt x = key.Length == 0 ? FsmVariables.GlobalVariables.FindFsmInt(name) : j != null && j.F != null ? j.F.FsmVariables.FindFsmInt(name) : null;
                        if (x != null) x.Value = v;
                    }
                    else if (t == 2)
                    {
                        bool v = r.Bool();
                        FsmBool x = key.Length == 0 ? FsmVariables.GlobalVariables.FindFsmBool(name) : j != null && j.F != null ? j.F.FsmVariables.FindFsmBool(name) : null;
                        if (x != null) x.Value = v;
                    }
                    else
                    {
                        // Liste : contenu remplace par celui de l'hote.
                        int c = r.U16();
                        var items = new List<object>(c);
                        for (int q = 0; q < c; q++)
                        {
                            int it = r.U8();
                            if (it == 0) items.Add(r.I32());
                            else if (it == 1) items.Add(r.F32());
                            else if (it == 2) items.Add(r.Str());
                            else if (it == 3) items.Add(r.Bool());
                            else if (it == 4) items.Add(Game.FindAny(r.Str()));
                            else items.Add(null);
                        }
                        if (j != null && j.F != null)
                            foreach (PlayMakerArrayListProxy pr in j.F.GetComponents<PlayMakerArrayListProxy>())
                                if ((pr.referenceName ?? "") == name && pr._arrayList != null)
                                {
                                    pr._arrayList.Clear(); foreach (object o in items) pr._arrayList.Add(o);
                                    if (Config.GetInt("Test", "JournalListes", 0) != 0)
                                    {
                                        var sb = new System.Text.StringBuilder(); foreach (object o in pr._arrayList) sb.Append(o).Append('|');
                                        Log.Info("liste recue " + key + " / " + name + " : " + Short(sb.ToString()));
                                    }
                                    break;
                                }
                    }
                }
            }
        }

        // ---------------------------------------------------------------- essais
        // Essais : comme un joueur qui actionne la commande : l'automate passe de 'prev' a 'state' par 'ev'
        // ici (actions jouees), et le message part comme en vrai.
        public static string TestSend(string part, string prev, string ev, string state)
        {
            foreach (W j in byKey.Values)
                if (j.F != null && j.Key.Contains(part))
                {
                    var w = new NetWriter(Msg.WorldFsm).U8(Session.LocalId).Str(j.Key).Str(prev).Str(ev).U8(0).Str(state);
                    WriteVars(j.F, w);
                    applying = true;
                    try { Game.SetState(j.F, state); } finally { applying = false; }
                    Session.SendAll(w, true);
                    return j.Key + " " + prev + " -" + ev + "-> " + state + " envoye";
                }
            return "rien pour " + part;
        }

        public static string TestEvent(string part, string ev)
        {
            foreach (W j in byKey.Values)
                if (j.F != null && j.Key.Contains(part))
                {
                    string before = j.F.ActiveStateName;
                    lastInput = Time.realtimeSinceStartup;   // comme si le joueur venait d'agir
                    j.F.SendEvent(ev);
                    return j.Key + " : " + before + " -" + ev + "-> " + j.F.ActiveStateName;
                }
            return "rien pour " + part;
        }

        // Comme si la logique passait d'elle-meme a 'state' (le crochet envoie comme en vrai).
        public static string TestState(string part, string state)
        {
            foreach (W j in byKey.Values)
                if (j.F != null && j.Key.Contains(part))
                {
                    string before = j.F.ActiveStateName;
                    Game.SetState(j.F, state);
                    return j.Key + " : " + before + " => " + j.F.ActiveStateName;
                }
            return "rien pour " + part;
        }

        public static string Var(string part, string name)
        {
            foreach (W j in byKey.Values)
                if (j.F != null && j.Key.Contains(part))
                {
                    NamedVariable v = j.F.FsmVariables.GetVariable(name);
                    return j.Key + " " + name + " = " + (v != null ? v.ToString() : "?") + " (etat " + j.F.ActiveStateName + ")";
                }
            FsmBool g = FsmVariables.GlobalVariables.FindFsmBool(name);
            if (g != null) return "globale " + name + " = " + g.Value;
            FsmFloat gf = FsmVariables.GlobalVariables.FindFsmFloat(name);
            return gf != null ? "globale " + name + " = " + gf.Value : "?";
        }
    }
}
