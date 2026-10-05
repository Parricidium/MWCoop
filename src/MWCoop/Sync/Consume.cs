using System.Collections.Generic;
using HutongGames.PlayMaker;
using MWCoop.Net;
using UnityEngine;
using CreateObject = HutongGames.PlayMaker.Actions.CreateObject;

namespace MWCoop
{
    // Objets consommes ou jetes : nourriture mangee, boisson bue, article jete a la poubelle.
    // Leur automate Use finit dans l'etat "Destroy" (manger : Eat -> Destroy ; poubelle : GARBAGE).
    // Une action ajoutee en tete de cet etat previent les autres, qui menent l'objet de meme ID
    // au meme etat : il disparait chez tout le monde. Celui qui mange porte la main a la bouche.
    // Autres fins rejouees par leur nom, seulement si rien ne s'enchaine apres (etat terminal) : "Destroy 2"
    // (sucre, levure), "Is garbage" (jete), "Empty" (boite de pieces videe, charbon), et les etats terminaux
    // sans nom propre ("State N") qui detruisent : l'objet lui-meme (DestroySelf : verre de biere, shot, cafe du
    // bar une fois bus -- ID pose par Shop) = disparu ; autre chose (DestroyObject/DestroyComponent) = vide, pour
    // "State 4" (caisse de biere vide, bidon d'huile vide) et les objets nommes par Shop (barquette saucisse-frites
    // mangee). Jamais "Add inventory" (paquet empoche : il compte dans les cigarettes de celui qui l'empoche) -- le
    // "Destroy" qui le suit suffit.
    // Les fins "vides" ne sont pas des disparitions : l'objet vide reste la (renomme) et peut encore etre
    // jete ("Is garbage", par GARBAGE) -- il reste suivi pour que cette vraie fin soit rejouee aussi.
    // Message : (joueur, ID, drapeaux : 1 = instantane d'arrivee, 2 = disparu, etat) -- un "State N" ne dit pas
    // de lui-meme s'il fait disparaitre l'objet.
    // Pas de niveau partiel a synchroniser pour les boissons : lait, gnole, biere, jus se boivent d'un coup
    // ('Eat 2' -> 'Play anim' -> 'Destroy' ; la main du joueur fait le reste) ; seul le cafe des machines a un
    // niveau (variable Coffee de la tasse), suivi par Shop avec la machine.
    // Caisse de biere : "Remove bottle" (une bouteille de moins) est rejoue sans enchainer sur "Play anim"
    // (la biere en main de celui qui boit).
    // Pareil pour les sacs de courses : « Spawn one » (sortir un article) et « Spawn all » (tout vider)
    // sont rejoues sur le meme sac chez les autres -- les memes articles sortent (memes ID, compteurs
    // identiques), puis Props suit leur physique.
    // Objets crees en jeu (distributeurs Spawner/..., pub, sacs) : un observateur en fin de chaque etat qui
    // cree un objet le fait suivre des qu'il a son ID (image suivante), sans attendre le releve de 10 s -- un
    // article empoche ou mange juste apres sa creation disparait aussi chez les autres.
    public static class Consume
    {
        static readonly Dictionary<string, PlayMakerFSM> byId = new Dictionary<string, PlayMakerFSM>();
        static readonly HashSet<PlayMakerFSM> hooked = new HashSet<PlayMakerFSM>();
        static readonly HashSet<PlayMakerFSM> watched = new HashSet<PlayMakerFSM>();   // etats createurs deja observes
        static readonly List<PlayMakerFSM> toWatch = new List<PlayMakerFSM>();         // a lire, 2 ms par image
        static readonly System.Diagnostics.Stopwatch watchClock = new System.Diagnostics.Stopwatch();
        static int watchedStates;
        static readonly string[] GoneStates = { "Destroy", "Destroy 2", "Is garbage" };   // l'article quitte le monde
        const string Empty = "Empty";                                                      // vide, mais toujours la
        const string Bottle = "Remove bottle";
        static float nextScan = -1, lastRescan, nextWarn;
        static bool applying;
        static readonly List<KeyValuePair<string, string>> consumed = new List<KeyValuePair<string, string>>();   // hote : (ID, fin) depuis le chargement
        static readonly HashSet<string> remembered = new HashSet<string>();
        static readonly HashSet<string> rememberedGone = new HashSet<string>();   // (ID|fin) qui font disparaitre l'article
        static readonly HashSet<string> done = new HashSet<string>();   // articles disparus, dits ou recus (Props n'a rien a redire)
        static readonly List<KeyValuePair<float, Peer>> snapshots = new List<KeyValuePair<float, Peer>>();
        static readonly List<KeyValuePair<float, GameObject>> fresh = new List<KeyValuePair<float, GameObject>>();
        static int freshLogged;
        public static float EatUntil;

        // Message pour un article pas encore la ici (cree par le meme rejeu a l'instant, son ID pas encore
        // pose : sac ouvert puis paquet empoche, recus dans la meme image) : repris jusqu'a 3 s.
        class Late { public float Until; public string Id, State; public int Who; public bool Snapshot; }
        static readonly List<Late> late = new List<Late>();
        // Messages restes sans objet apres ces 3 s (objet sous une zone pas chargee ici, jamais demarre : pas d'ID ;
        // ou cree a la main par Shop loin d'ici) : gardes, et rejoues dans l'ordre des que l'objet de cet ID est
        // enfin suivi -- sinon il reapparaissait plein ou intact (sac deja vide, caisse deja bue chez l'autre).
        // 256 articles au plus, 8 fins par article ; pas l'instantane d'arrivee (articles finis avant la sauvegarde
        // envoyee, qui ne seront jamais la).
        static readonly Dictionary<string, List<Late>> missed = new Dictionary<string, List<Late>>();
        static readonly List<string> missedOrder = new List<string>();

        class Hook : ModHook
        {
            public override string Module { get { return "consommables"; } }
            public string Id, StateName;
            public override void OnEnter()
            {
                try
                {
                    if (IsBag(StateName)) SoonScan();   // articles sortis du sac (ici ou rejoue) : suivis tout de suite
                    if (!applying && Replay.Depth == 0) OnLocal(Id, Fsm.PreviousActiveState != null ? Fsm.PreviousActiveState.Name : "", StateName, EndKind(State));
                }
                catch (System.Exception e) { Replay.HookError(e); }
                Finish();
            }
        }

        // Observateur pose en fin d'un etat qui cree un objet (apres le CreateObject) : l'objet cree sera suivi
        // des que son automate lui aura donne son ID. N'envoie rien : pas un ModHook (l'audit ne doit pas y
        // voir un module de plus).
        class Spawned : FsmStateAction
        {
            readonly CreateObject[] made;
            public Spawned(CreateObject[] m) { made = m; }
            public override void OnEnter()
            {
                try
                {
                    bool any = false;
                    foreach (CreateObject c in made)
                    {
                        GameObject go = c.storeObject != null ? c.storeObject.Value : null;
                        if (go != null) { Fresh(go); any = true; }
                    }
                    if (!any) SoonScan();   // objet cree que l'automate ne garde pas : releve complet
                }
                catch (System.Exception e) { Replay.HookError(e); }
                Finish();
            }
        }

        public static void OnLevelLoaded()
        {
            byId.Clear(); hooked.Clear(); watched.Clear(); toWatch.Clear(); consumed.Clear(); remembered.Clear(); rememberedGone.Clear(); snapshots.Clear(); fresh.Clear(); done.Clear(); late.Clear();
            missed.Clear(); missedOrder.Clear();
            trackedCache.Clear(); destroyCache.Clear();
            testStep = 0; testLog = 0;
            watchedStates = 0;
            nextScan = PlayerSync.InGame ? Time.realtimeSinceStartup + 11f : -1;
        }

        public static void Update()
        {
            if (!Session.Active || nextScan < 0) return;
            float now = Time.realtimeSinceStartup;
            for (int i = snapshots.Count - 1; i >= 0; i--)
            {
                if (now < snapshots[i].Key) continue;
                Peer p = snapshots[i].Value;
                snapshots.RemoveAt(i);
                if (!p.Accepted || !Session.T.Peers.Contains(p)) continue;
                foreach (KeyValuePair<string, string> c in consumed)
                    Session.T.SendReliable(p, new NetWriter(Msg.Consume).U8(Session.LocalId).Str(c.Key).U8(1 | (rememberedGone.Contains(c.Key + "|" + c.Value) ? 2 : 0)).Str(c.Value).ToArray());
                if (consumed.Count > 0) Log.Info("consommables : " + consumed.Count + " objets deja consommes envoyes a " + p);
            }
            // Objets tout juste crees : suivis des qu'ils ont leur ID (2 s au plus, sinon au releve suivant).
            for (int i = fresh.Count - 1; i >= 0; i--)
            {
                GameObject go = fresh[i].Value;
                bool alive = go != null && now < fresh[i].Key;
                if (alive && !Track(go, true)) continue;
                if (alive) Props.Track(go);
                fresh.RemoveAt(i);
            }
            for (int i = 0; i < late.Count; )   // dans l'ordre d'arrivee (sac ouvert, puis article pris)
            {
                Late l = late[i];
                PlayMakerFSM f;
                bool found = byId.TryGetValue(l.Id, out f) && f != null;
                if (!found && now < l.Until) { i++; continue; }
                late.RemoveAt(i);
                if (found) { Apply(f, l.Id, l.State, l.Who, l.Snapshot); continue; }
                if (!l.Snapshot) Miss(l);   // instantane d'arrivee : surtout des articles finis avant la sauvegarde, jamais la
                if (!l.Snapshot && now >= nextWarn) { nextWarn = now + 10f; Log.Warn("consommables : " + l.Id + " introuvable ici (garde pour quand il sera la)"); }
            }
            // Lecture des automates a observer etalee (milliers d'automates au premier releve : pas d'a-coup).
            if (toWatch.Count > 0)
            {
                watchClock.Reset(); watchClock.Start();
                while (toWatch.Count > 0 && watchClock.ElapsedMilliseconds < 2)
                {
                    PlayMakerFSM f = toWatch[toWatch.Count - 1];
                    toWatch.RemoveAt(toWatch.Count - 1);
                    if (f != null) watchedStates += WatchCreates(f);
                }
            }
            if (toWatch.Count == 0 && watchedStates > 0) { Log.Info("consommables : " + watchedStates + " etats createurs d'objets observes"); watchedStates = 0; }
            if (now < nextScan) return;
            nextScan = now + 10f;
            Scan();
        }

        // Hote : invite arrive en jeu -> 20 s plus tard, ce qui a ete mange ou jete pendant son chargement.
        public static bool Tracks(PlayMakerFSM f) { return hooked.Contains(f); }

        public static void ScheduleSnapshot(Peer p) { snapshots.Add(new KeyValuePair<float, Peer>(Time.realtimeSinceStartup + 20f, p)); }

        static bool IsBag(string state) { return state == "Spawn one" || state == "Spawn all"; }
        static bool IsGoneName(string state) { return System.Array.IndexOf(GoneStates, state) >= 0; }
        static bool IsEndName(string state) { return IsGoneName(state) || state == Empty; }
        static bool IsGeneric(string state) { return state.StartsWith("State "); }   // etat sans nom propre ("State 4")

        // Etat suivi d'un automate Use d'article. Les fins autres que "Destroy" seulement si elles sont
        // terminales (rien ne s'enchaine : ni animation du joueur, ni logique de chargement), sans rien qui
        // touche au joueur, et un "State N" seulement s'il detruit quelque chose. Lu une fois par etat.
        static readonly Dictionary<FsmState, bool> trackedCache = new Dictionary<FsmState, bool>();
        static readonly Dictionary<FsmState, int> destroyCache = new Dictionary<FsmState, int>();

        static bool Tracked(FsmState s)
        {
            string n = s.Name;
            if (n == "Destroy" || IsBag(n)) return true;
            bool generic = IsGeneric(n);
            if (n != Bottle && !IsEndName(n) && !generic) return false;
            bool r;
            if (trackedCache.TryGetValue(s, out r)) return r;
            r = n == Bottle || s.Transitions.Length == 0;
            // "State N" : s'il fait disparaitre l'objet ; "vide" seulement pour "State 4" (caisse de biere, bidons :
            // deja suivi) et les objets nommes par Shop (barquette du pub) -- pas les boites de pieces ('State 10'
            // de SpringsBox : les ressorts ne sortent que chez celui qui ouvre, un autre lot).
            if (r && generic) { int d = Destroys(s); r = d == 2 || d == 1 && (n == "State 4" || Shop.Named(IdOf(s))); }
            if (r) r = !TouchesPlayer(s);
            trackedCache[s] = r;
            return r;
        }

        static string IdOf(FsmState s)
        {
            FsmString v = s.Fsm != null ? s.Fsm.Variables.FindFsmString("ID") : null;
            return v != null ? v.Value : "";
        }

        // Ce que l'etat detruit : 2 l'objet lui-meme (DestroySelf, DestroyObject de l'objet), 1 autre chose
        // (une bouteille, la barquette, un composant), 0 rien.
        static int Destroys(FsmState s)
        {
            int k;
            if (destroyCache.TryGetValue(s, out k)) return k;
            k = 0;
            GameObject self = s.Fsm != null ? s.Fsm.GameObject : null;
            foreach (FsmStateAction a in s.Actions)
            {
                if (a is HutongGames.PlayMaker.Actions.DestroySelf) { k = 2; break; }
                var d = a as HutongGames.PlayMaker.Actions.DestroyObject;
                if (d != null)
                {
                    GameObject g = d.gameObject != null ? d.gameObject.Value : null;
                    if (g != null && g == self) { k = 2; break; }
                    k = 1;
                }
                else if (a is HutongGames.PlayMaker.Actions.DestroyComponent) k = 1;
            }
            destroyCache[s] = k;
            return k;
        }

        // Fin suivie : 2 l'article disparait (Destroy, jete, ou "State N" qui le detruit), 1 vide mais toujours la,
        // 0 pas une fin.
        static int EndKind(FsmState s)
        {
            if (s == null) return 0;
            string n = s.Name;
            if (IsGoneName(n)) return 2;
            if (n == Empty) return 1;
            if (!IsGeneric(n) || !Tracked(s)) return 0;
            return Destroys(s);
        }

        // L'etat agit-il sur le joueur ou son interface (objet sous PLAYER ou GUI, evenement envoye a tous) ?
        // Rejoue chez l'autre, il toucherait a SON joueur.
        static bool TouchesPlayer(FsmState s)
        {
            foreach (FsmStateAction a in s.Actions)
            {
                if (a == null) continue;
                foreach (System.Reflection.FieldInfo fi in a.GetType().GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
                {
                    object v = fi.GetValue(a);
                    var et = v as FsmEventTarget;
                    if (et != null)
                    {
                        if (et.target == FsmEventTarget.EventTarget.BroadcastAll) return true;
                        v = et.gameObject;
                    }
                    GameObject go = null;
                    if (v is FsmGameObject) go = ((FsmGameObject)v).Value;
                    else if (v is FsmOwnerDefault) { var od = (FsmOwnerDefault)v; if (od.OwnerOption != OwnerDefaultOption.UseOwner) go = od.GameObject.Value; }
                    if (go == null) continue;
                    string root = go.transform.root.name;
                    if (root == "PLAYER" || root == "GUI") return true;
                }
            }
            return false;
        }

        // L'automate Use 'f' est-il dans une de ses fins suivies (article disparu ou vide) ?
        public static bool IsGone(PlayMakerFSM f)
        {
            FsmState s = f != null ? f.Fsm.ActiveState : null;
            return s != null && EndKind(s) > 0 && Tracked(s);
        }

        // Fin a annoncer pour un article disparu (Props) : son etat s'il est suivi, sinon "Destroy".
        public static string GoneState(PlayMakerFSM f) { return IsGone(f) ? f.ActiveStateName : "Destroy"; }

        public static bool Done(string id) { return done.Contains(id); }

        // Articles crees en nombre (sac ouvert) : releve complet tres bientot, ici et dans Props.
        static void SoonScan()
        {
            float t = Time.realtimeSinceStartup + 0.3f;
            if (nextScan > t) nextScan = t;
            Props.SoonScan();
        }

        static void Miss(Late l)
        {
            List<Late> m;
            if (!missed.TryGetValue(l.Id, out m))
            {
                missed[l.Id] = m = new List<Late>();
                missedOrder.Add(l.Id);
                if (missedOrder.Count > 256) { missed.Remove(missedOrder[0]); missedOrder.RemoveAt(0); }
            }
            if (m.Count < 8) m.Add(l);
        }

        // Shop : objet cree a la main (achat d'un autre, comptoir eteint ici) et sorti a la racine : suivi des que
        // son automate a pose son ID.
        public static void Soon(GameObject go) { if (go != null) Fresh(go); }

        static void Fresh(GameObject go)
        {
            if (go.GetComponent<Rigidbody>() == null) return;   // pas un article (effet, decor)
            if (fresh.Count >= 64) fresh.RemoveAt(0);
            fresh.Add(new KeyValuePair<float, GameObject>(Time.realtimeSinceStartup + 2f, go));
        }

        // Objet tout juste cree, ou pris en main : son automate Use suivi tout de suite. Faux tant qu'il n'a
        // pas son ID (son automate ne l'a pas encore pose, a son demarrage).
        public static bool Track(GameObject go) { return Track(go, false); }

        // created : objet tout juste cree ici (observateur de CreateObject, Shop) -- pas celui que visaient des fins
        // recues avant (voir TryHook).
        static bool Track(GameObject go, bool created)
        {
            if (nextScan < 0 || go == null) return false;
            bool id = false;
            foreach (PlayMakerFSM f in go.GetComponents<PlayMakerFSM>())
            {
                FsmString idv = f.FsmVariables.FindFsmString("ID");
                if (idv == null || idv.Value.Length == 0) continue;
                id = true;
                if (f.FsmName == "Use" && !hooked.Contains(f) && TryHook(f, created) && (++freshLogged <= 20 || freshLogged % 50 == 0))
                    Log.Info("consommables : " + idv.Value + " suivi des sa creation (" + byId.Count + ")");
            }
            return id;
        }

        static void Scan()
        {
            hooked.RemoveWhere(x => x == null);
            watched.RemoveWhere(x => x == null);
            var gone = new List<string>();
            foreach (KeyValuePair<string, PlayMakerFSM> kv in byId) if (kv.Value == null) gone.Add(kv.Key);
            foreach (string g in gone) byId.Remove(g);
            int n = 0;
            GameObject sp = GameObject.Find("/Spawner");   // la racine (un autre objet porte ce nom plus bas)
            Transform spawner = sp != null ? sp.transform : null;
            foreach (Object o in Resources.FindObjectsOfTypeAll(typeof(PlayMakerFSM)))
            {
                var f = (PlayMakerFSM)o;
                if (f.hideFlags != HideFlags.None) continue;
                // Createurs d'objets a observer : les distributeurs (Spawner/...) tout de suite, les autres peu a
                // peu. Objet inactif : ses actions ne sont pas chargees ; observe quand il s'active.
                if (!watched.Contains(f) && f.gameObject.activeInHierarchy)
                {
                    watched.Add(f);
                    if (f.transform.root == spawner) watchedStates += WatchCreates(f); else toWatch.Add(f);
                }
                if (f.FsmName != "Use" || hooked.Contains(f)) continue;
                if (TryHook(f)) n++;
            }
            if (n > 0) Log.Info("consommables : " + n + " objets de plus suivis (" + byId.Count + ")");
        }

        static bool TryHook(PlayMakerFSM f, bool created = false)
        {
            FsmString idv = f.FsmVariables.FindFsmString("ID");
            if (idv == null || idv.Value.Length == 0) return false;
            bool any = false;
            foreach (FsmState s in f.Fsm.States)
            {
                if (!Tracked(s)) continue;
                try
                {
                    var list = new List<FsmStateAction>(s.Actions);
                    list.Insert(0, new Hook { Id = idv.Value, StateName = s.Name });
                    s.Actions = list.ToArray();
                    any = true;
                }
                catch { }
            }
            if (!any) return false;
            hooked.Add(f);
            byId[idv.Value] = f;
            // Fins recues avant qu'il soit la (objet qui vient de s'activer : zone chargee, pris en main) : rejouees
            // maintenant, dans l'ordre, par la file 'late'. Pas sur un objet tout juste CREE ici : un compteur en
            // retard redonne ce nom a un nouvel objet du joueur local, que ces fins videraient ou detruiraient.
            List<Late> m;
            if (missed.TryGetValue(idv.Value, out m))
            {
                missed.Remove(idv.Value);
                missedOrder.Remove(idv.Value);
                if (created) Log.Info("consommables : " + idv.Value + " cree ici, " + m.Count + " fin(s) recue(s) plus tot pour un autre objet de ce nom oubliee(s)");
                else
                {
                    float until = Time.realtimeSinceStartup + 3f;
                    foreach (Late l in m) { l.Until = until; late.Add(l); }
                    Log.Info("consommables : " + idv.Value + " enfin la ici, " + m.Count + " fin(s) recue(s) plus tot a rejouer (" + m[m.Count - 1].State + ")");
                }
            }
            return true;
        }

        // Etats qui creent un objet (CreateObject) : un observateur ajoute en fin d'etat, apres la creation.
        static int WatchCreates(PlayMakerFSM f)
        {
            int n = 0;
            try
            {
                foreach (FsmState st in f.Fsm.States)
                {
                    List<CreateObject> made = null;
                    foreach (FsmStateAction a in st.Actions)
                    {
                        var c = a as CreateObject;
                        if (c == null) continue;
                        if (made == null) made = new List<CreateObject>();
                        made.Add(c);
                    }
                    if (made == null) continue;
                    var list = new List<FsmStateAction>(st.Actions);
                    list.Add(new Spawned(made.ToArray()));
                    st.Actions = list.ToArray();
                    n++;
                }
            }
            catch { }
            return n;
        }

        // Hote : fins a renvoyer a un invite qui arrive (une fois par article et par fin).
        static void Remember(string id, string state, bool gone)
        {
            if (remembered.Add(id + "|" + state)) consumed.Add(new KeyValuePair<string, string>(id, state));
            if (gone) rememberedGone.Add(id + "|" + state);
        }

        static int Flags(bool snapshot, bool gone) { return (snapshot ? 1 : 0) | (gone ? 2 : 0); }

        static void OnLocal(string id, string from, string state, int end)
        {
            if (IsBag(state))
            {
                Log.Info("consommables : sac " + id + " ouvert ici (" + state + ")");
                Session.SendAll(new NetWriter(Msg.Consume).U8(Session.LocalId).Str(id).U8(0).Str(state), true);
                return;
            }
            if (state == Bottle)
            {
                Log.Info("consommables : " + id + " une bouteille de moins ici");
                Session.SendAll(new NetWriter(Msg.Consume).U8(Session.LocalId).Str(id).U8(0).Str(state), true);
                return;
            }
            // Mange ou bu (pas jete) : la main va a la bouche.
            if (from.Contains("Eat") || from.Contains("Drink") || from.Contains("drink") || from == "Play anim")
                EatUntil = Time.realtimeSinceStartup + 2f;
            bool gone = end == 2;
            if (gone) done.Add(id);
            if (Session.IsHost) Remember(id, state, gone);
            lastEnd = id;
            Log.Info("consommables : " + id + (gone ? " consomme ou jete ici (" : " vide ici (") + from + (state != "Destroy" ? " -> " + state : "") + ")");
            Session.SendAll(new NetWriter(Msg.Consume).U8(Session.LocalId).Str(id).U8(Flags(false, gone)).Str(state), true);
        }

        // Props : article disparu ici sans que Consume l'ait vu (cree a l'instant, fin non suivie) -> les autres
        // le font disparaitre aussi, par son ID.
        public static void SendGone(string id, string state)
        {
            if (done.Contains(id)) return;
            PlayMakerFSM f;
            bool gone = IsGoneName(state) || byId.TryGetValue(id, out f) && f != null && EndKind(f.Fsm.GetState(state)) == 2;
            if (gone) done.Add(id);
            if (Session.IsHost) Remember(id, state, gone);
            Log.Info("consommables : " + id + " disparu ici sans etat suivi, signale aux autres (" + state + ")");
            Session.SendAll(new NetWriter(Msg.Consume).U8(Session.LocalId).Str(id).U8(Flags(false, gone)).Str(state), true);
        }

        public static void OnMessage(Peer from, NetReader r)
        {
            int who = r.U8();
            if (Session.IsHost) who = from.Id;
            string id = r.Str();
            int flags = r.U8();
            bool snapshot = (flags & 1) != 0;
            string state = r.More ? r.Str() : "Destroy";
            bool gone = (flags & 2) != 0 || IsGoneName(state);
            bool over = done.Contains(id);   // deja disparu ici (mange, jete, recu) : rien a rejouer ni a cacher
            if (gone) done.Add(id);
            // Fin (a renvoyer a un invite qui arrive) : tout etat suivi sauf sac ouvert et bouteille retiree.
            bool end = gone || IsEndName(state) || IsGeneric(state);
            if (Session.IsHost) { if (end) Remember(id, state, gone); Session.Broadcast(new NetWriter(Msg.Consume).U8(who).Str(id).U8(Flags(false, gone)).Str(state), true, who); }
            if (over) { if (!snapshot) Log.Info("consommables : " + id + " deja fini ici (" + state + " du joueur #" + who + ")"); return; }
            PlayMakerFSM f;
            float now = Time.realtimeSinceStartup;
            if (!byId.TryGetValue(id, out f) || f == null)
            {
                GameObject go = Props.ObjectOf(id);   // connu de Props (cree a l'instant) : son automate tout de suite
                if (go != null && Track(go)) byId.TryGetValue(id, out f);
            }
            if ((!byId.TryGetValue(id, out f) || f == null) && now - lastRescan > 1f)
            {
                lastRescan = now;   // objet tout neuf (achete il y a peu) : nouveau passage
                Scan();
                byId.TryGetValue(id, out f);
            }
            bool queued = false;   // un message plus ancien pour le meme article attend : celui-ci apres lui
            foreach (Late l in late) if (l.Id == id) { queued = true; break; }
            if (f == null || queued)
            {
                // Disparu chez l'autre, sans automate suivi ici pour le rejouer : Props le cache s'il le connait
                // (pas un objet seulement vide : il est toujours la, et sa vraie fin viendra).
                if (f == null && gone && Props.Vanish(id)) { Log.Info("consommables : " + id + " disparu chez le joueur #" + who + ", cache ici"); return; }
                late.Add(new Late { Until = now + 3f, Id = id, State = state, Who = who, Snapshot = snapshot });
                return;
            }
            Apply(f, id, state, who, snapshot);
        }

        static void Apply(PlayMakerFSM f, string id, string state, int who, bool snapshot)
        {
            FsmState s = f.Fsm.GetState(state);
            if (s == null || !Tracked(s)) return;
            int ek = EndKind(s);
            bool gone = ek == 2, end = ek > 0;
            if (gone) done.Add(id);
            // Deja la (meme fin atteinte des deux cotes, sac deja vide) : pas une 2e fois.
            if ((end || state == "Spawn all") && f.ActiveStateName == state) { if (!snapshot) Log.Info("consommables : " + id + " deja " + state + " ici"); return; }
            // Deja jete ou mange ici : un "vide" arrive apres coup ne le ramene pas en arriere.
            if (end && !gone && EndKind(f.Fsm.ActiveState) == 2) return;
            applying = true; Replay.Depth++;
            try
            {
                if (state == Bottle) ReplayAlone(f, s);
                else
                {
                    // Sac : « Confirm » le declare sac courant aupres du distributeur d'articles (CurrentBag) ;
                    // sans lui, « Spawn all » viderait un sac inconnu.
                    if (IsBag(state) && f.Fsm.GetState("Confirm") != null) Game.SetState(f, "Confirm");
                    Game.SetState(f, state);
                }
            }
            finally { applying = false; Replay.Depth--; }
            if (IsBag(state)) { Log.Info("consommables : sac " + id + " ouvert par le joueur #" + who + " (" + state + ")"); return; }
            if (state == Bottle) { Log.Info("consommables : " + id + " une bouteille de moins (joueur #" + who + ")"); return; }
            lastEnd = id;
            // Vide mais toujours la : reste suivi, sa vraie fin (jete) sera rejouee elle aussi.
            if (!gone) { Log.Info("consommables : " + id + " vide par le joueur #" + who + " (" + state + ")"); return; }
            byId.Remove(id);
            Log.Info("consommables : " + id + " consomme par le joueur #" + who + (state != "Destroy" ? " (" + state + ")" : ""));
        }

        // Rejoue l'etat 's' sans enchainer : ses transitions sont retirees le temps de l'entree, puis l'automate
        // revient ou il etait (la caisse de biere passerait sinon a la biere en main de celui qui la rejoue).
        static void ReplayAlone(PlayMakerFSM f, FsmState s)
        {
            string back = f.ActiveStateName;
            FsmTransition[] tr = s.Transitions;
            s.Transitions = new FsmTransition[0];
            try { Game.SetState(f, s.Name); }
            finally { s.Transitions = tr; }
            if (!string.IsNullOrEmpty(back) && back != s.Name) Game.SetState(f, back);
        }

        // Essais : vide le sac de courses suivi le plus proche (comme ENTREE sur le sac), message compris ;
        // 'skip' : sacs a laisser (deja la avant un achat).
        public static string TestOpenBag(HashSet<string> skip = null)
        {
            Scan();
            Vector3 me = GameObject.Find("PLAYER").transform.position;
            PlayMakerFSM best = null; string bestId = null;
            foreach (KeyValuePair<string, PlayMakerFSM> kv in byId)
            {
                if (kv.Value == null || kv.Value.Fsm.GetState("Spawn all") == null || skip != null && skip.Contains(kv.Key)) continue;
                if (best == null || (kv.Value.transform.position - me).sqrMagnitude < (best.transform.position - me).sqrMagnitude) { best = kv.Value; bestId = kv.Key; }
            }
            if (best == null) return "aucun sac (" + byId.Count + " suivis)";
            Game.SetState(best, "Confirm");
            Game.SetState(best, "Spawn all");
            return "sac " + bestId + " vide a " + (best.transform.position - me).magnitude.ToString("F1") + " m";
        }

        // Essais : mange l'objet suivi le plus proche du joueur (comme si on avait clique dessus).
        public static string TestNearest()
        {
            Scan();
            Vector3 me = GameObject.Find("PLAYER").transform.position;
            PlayMakerFSM best = null;
            string bestId = null;
            foreach (KeyValuePair<string, PlayMakerFSM> kv in byId)
            {
                if (kv.Value == null || kv.Value.Fsm.GetState("Eat") == null && kv.Value.Fsm.GetState("Eat 2") == null) continue;
                if (best == null || (kv.Value.transform.position - me).sqrMagnitude < (best.transform.position - me).sqrMagnitude) { best = kv.Value; bestId = kv.Key; }
            }
            if (best == null) return "rien a manger (" + byId.Count + " suivis)";
            Game.SetState(best, best.Fsm.GetState("Eat") != null ? "Eat" : "Eat 2");
            return bestId + " a " + (best.transform.position - me).magnitude.ToString("F0") + " m";
        }

        // Essais ([Test] Autotest=poche) : l'hote achete [Test] TestProduit (Cigarettes) a 30 s, vide le sac neuf
        // 3 s plus tard, puis empoche le premier paquet sorti des qu'il a son ID (etat 'Check pocket' de son
        // automate, comme la touche d'utilisation). Chacun note toutes les 2 s les paquets suivis (debut d'ID :
        // [Test] TestPiece, cigarettes par defaut) et ou ils sont ('?' : disparu) ; chez l'invite : « consomme
        // par le joueur #0 » et ce paquet a '?'.
        static int testStep;
        static float testAt, testLog;
        static HashSet<string> testPacks, testBags;
        static string lastEnd, testBox;   // dernier article fini ici (dit ou recu) ; boite videe par l'essai
        static bool testBoxLogged;

        public static void Test(string mode, float t)
        {
            Shop.Test(mode, t);   // modes bar, cafe, sac-double (achats)
            if (mode == "boite-vide") { TestBox(t); return; }
            if (mode == "gorgee") { TestSip(t); return; }
            if (mode != "poche") return;
            string prefix = Config.Get("Test", "TestPiece", "cigarettes");
            if (Session.IsHost && t > 30f && testStep == 0)
            {
                testStep = 1;
                testPacks = new HashSet<string>();
                foreach (PlayMakerFSM f in Pockets(prefix)) testPacks.Add(f.FsmVariables.FindFsmString("ID").Value);
                testBags = new HashSet<string>();
                foreach (KeyValuePair<string, PlayMakerFSM> kv in byId) if (kv.Value != null && kv.Value.Fsm.GetState("Spawn all") != null) testBags.Add(kv.Key);
                Log.Info("autotest : " + Shop.TestBuy(Config.Get("Test", "TestProduit", "Cigarettes"), 1) + " (" + testPacks.Count + " paquets et " + testBags.Count + " sacs avant)");
            }
            if (Session.IsHost && t > 33f && testStep == 1) { testStep = 2; testAt = t; Log.Info("autotest : " + TestOpenBag(testBags)); }
            if (Session.IsHost && testStep == 2 && Time.frameCount % 3 == 0)
            {
                foreach (PlayMakerFSM f in Pockets(prefix))
                {
                    string id = f.FsmVariables.FindFsmString("ID").Value;
                    if (testPacks.Contains(id)) continue;
                    testStep = 3;
                    bool tracked = hooked.Contains(f);
                    Game.SetState(f, "Check pocket");
                    Log.Info("autotest : poche " + id + " " + (t - testAt).ToString("F2") + " s apres l'ouverture du sac, suivi " + (tracked ? "oui" : "NON") + " -> " + f.ActiveStateName);
                    break;
                }
                if (testStep == 2 && t - testAt > 8f) { testStep = 3; Log.Info("autotest : aucun paquet neuf a empocher"); }
            }
            if (t > 34f && t < 60f && t - testLog >= 2f) { testLog = t; Log.Info("autotest : paquets " + Props.Ids(prefix)); }
        }

        // Essais ([Test] Autotest=boite-vide) : l'hote vide a 30 s l'article suivi le plus proche qui a une fin
        // "vide" ("Empty" : boite de pieces, "State 4" : caisse de biere, bidon d'huile) et "Is garbage" ([Test]
        // TestBoite : debut d'ID, n'importe lequel par defaut), puis le jette a 34 s. Chez l'invite : « vide par le
        // joueur #0 », puis « consomme par le joueur #0 (Is garbage) » -- jamais « cache ici » ni « introuvable » ;
        // a 37 s chacun note ou est cet article (meme place des deux cotes, celle de la poubelle).
        static void TestBox(float t)
        {
            if (Session.IsHost && t > 30f && testStep == 0)
            {
                testStep = 1;
                PlayMakerFSM f = EmptyBox(Config.Get("Test", "TestBoite", ""));
                if (f == null) { Log.Info("autotest : aucune boite a vider (" + byId.Count + " suivis)"); return; }
                testBox = f.FsmVariables.FindFsmString("ID").Value;
                string empty = f.Fsm.GetState("Empty") != null ? "Empty" : "State 4";
                Game.SetState(f, empty);
                Log.Info("autotest : boite " + testBox + " videe (" + empty + ") -> " + f.ActiveStateName);
            }
            if (Session.IsHost && t > 34f && testStep == 1)
            {
                testStep = 2;
                PlayMakerFSM f = null;
                if (testBox != null && byId.TryGetValue(testBox, out f) && f != null) Game.SetState(f, "Is garbage");
                Log.Info("autotest : boite " + testBox + " jetee -> " + (f != null ? f.ActiveStateName : "introuvable"));
            }
            if (t > 37f && !testBoxLogged)
            {
                testBoxLogged = true;
                Log.Info("autotest : derniere fin " + (lastEnd ?? "aucune") + " en " + (lastEnd != null ? Props.Where(lastEnd) : "?"));
            }
        }

        // Essais : article suivi le plus proche du joueur, pas encore vide, qui a une fin "vide" suivie et "Is garbage".
        static PlayMakerFSM EmptyBox(string prefix)
        {
            Scan();
            Vector3 me = GameObject.Find("PLAYER").transform.position;
            PlayMakerFSM best = null;
            foreach (KeyValuePair<string, PlayMakerFSM> kv in byId)
            {
                PlayMakerFSM f = kv.Value;
                if (f == null || !kv.Key.StartsWith(prefix) || f.Fsm.GetState("Is garbage") == null || EndKind(f.Fsm.ActiveState) > 0) continue;
                FsmState e = f.Fsm.GetState("Empty") ?? f.Fsm.GetState("State 4");
                if (e == null || !Tracked(e)) continue;
                if (best == null || (f.transform.position - me).sqrMagnitude < (best.transform.position - me).sqrMagnitude) best = f;
            }
            return best;
        }

        // Essais ([Test] Autotest=gorgee) : l'hote boit a 30 s l'article suivi le plus proche dont l'ID commence par
        // [Test] TestPiece (milk par defaut ; booze...), comme le clic ('Eat 2' : la suite 'Play anim' -> 'Destroy'
        // est celle du jeu) ; il note avant ses variables (pas de niveau a boire : seulement l'etat de conservation).
        // Chacun note de 28 a 44 s les articles de ce prefixe et ou ils sont ('?' : disparu). Attendu chez l'invite :
        // « consomme par le joueur #0 » et ce meme ID a '?' -- la bouteille est bue d'un coup, il ne reste rien a
        // synchroniser comme niveau.
        static void TestSip(float t)
        {
            string prefix = Config.Get("Test", "TestPiece", "milk");
            if (Session.IsHost && t > 30f && testStep == 0)
            {
                testStep = 1;
                Scan();
                Vector3 me = GameObject.Find("PLAYER").transform.position;
                PlayMakerFSM best = null; string bestId = null;
                foreach (KeyValuePair<string, PlayMakerFSM> kv in byId)
                {
                    if (kv.Value == null || !kv.Key.StartsWith(prefix) || kv.Value.Fsm.GetState("Eat 2") == null || EndKind(kv.Value.Fsm.ActiveState) > 0) continue;
                    if (best == null || (kv.Value.transform.position - me).sqrMagnitude < (best.transform.position - me).sqrMagnitude) { best = kv.Value; bestId = kv.Key; }
                }
                if (best == null) { Log.Info("autotest : rien a boire (" + prefix + ", " + byId.Count + " suivis)"); return; }
                var vars = new System.Text.StringBuilder();
                foreach (FsmFloat x in best.FsmVariables.FloatVariables) vars.Append(x.Name).Append('=').Append(x.Value.ToString("0.###")).Append(' ');
                Game.SetState(best, "Eat 2");
                Log.Info("autotest : gorgee " + bestId + " a " + (best.transform.position - me).magnitude.ToString("F1") + " m (" + vars.ToString().TrimEnd() + ") -> " + best.ActiveStateName);
            }
            if (t > 28f && t < 45f && t - testLog >= 4f) { testLog = t; Log.Info("autotest : boissons " + Props.Ids(prefix)); }
        }

        // Essais : automates Use actifs des paquets (etat 'Check pocket'), ID deja pose.
        static List<PlayMakerFSM> Pockets(string prefix)
        {
            var r = new List<PlayMakerFSM>();
            foreach (PlayMakerFSM f in Object.FindObjectsOfType<PlayMakerFSM>())
            {
                if (f.FsmName != "Use" || f.Fsm.GetState("Check pocket") == null) continue;
                FsmString idv = f.FsmVariables.FindFsmString("ID");
                if (idv != null && idv.Value.StartsWith(prefix)) r.Add(f);
            }
            return r;
        }
    }
}
