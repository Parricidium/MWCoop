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
    // Plus generalement, tout etat terminal d'un automate Use ou Data a ID qui detruit l'objet ou l'eteint
    // (DestroySelf, DestroyObject ou ActivateGameObject faux sur lui-meme) est une disparition, quel que soit son nom.
    // Pieces (automate Data a ID, sans automate Use a ID) : seulement ces disparitions -- "Is garbage" surtout
    // (piece jetee a la decharge : GARBAGE ne partait que la ou elle tombait dans la benne).
    // Les fins "vides" ne sont pas des disparitions : l'objet vide reste la (renomme) et peut encore etre
    // jete ("Is garbage", par GARBAGE) -- il reste suivi pour que cette vraie fin soit rejouee aussi.
    // Message : (joueur, ID, drapeaux : 1 = instantane d'arrivee, 2 = disparu, 4 = variables a la suite,
    // 8 = prise dans une boite (Boxes) a la suite, 16 = contenu seul, etat) -- un "State N" ne dit pas de lui-meme
    // s'il fait disparaitre l'objet.
    // Boissons : lait, gnole, biere, boisson energisante, verres du bar se boivent d'un coup ('Eat 2' -> 'Play
    // anim' -> 'Destroy', ou 'Play anim' -> 'State 2' DestroySelf), deja suivis par leur fin. Ce qui a un CONTENU
    // qui reste apres usage : le bidon de jus (ContainsJuice / ContainsKilju / Kilju* : bu, il reste la, vide), la
    // caisse de biere (DestroyedBottles). Contenu = variables que le jeu sauvegarde avec l'objet (une variable
    // texte UniqueTag<Suffixe> leur donne leur cle), sauf la fraicheur (Condition, que chacun fait baisser).
    // Quand le joueur quitte un etat d'attente par un clic (USE), ce contenu est note ; au retour dans un etat
    // d'attente, ce qui a change part aux autres (contenu seul), avec l'etat juste avant le retour s'il ne fait
    // que renommer ou poser des variables propres ("Empty" du bidon : renomme vide) -- rejoue chez les autres.
    // Le cafe des machines a son niveau (variable Coffee de la tasse) suivi par Shop avec la machine.
    // Caisse de biere : "Remove bottle" (une bouteille de moins) est rejoue sans enchainer sur "Play anim"
    // (la biere en main de celui qui boit ; la bouteille vide lancee apres est un objet du joueur sans ID, elle
    // n'existe que chez lui). Son compte (DestroyedBottles) suit ensuite comme contenu, et les bouteilles visibles
    // en trop chez un autre (comptes qui avaient diverge) sont retirees.
    // Pareil pour les sacs de courses : « Spawn one » (sortir un article) et « Spawn all » (tout vider)
    // sont rejoues sur le meme sac chez les autres -- les memes articles sortent (memes ID, compteurs
    // identiques), puis Props suit leur physique.
    // Objets crees en jeu (distributeurs Spawner/..., pub, sacs) : un observateur en fin de chaque etat qui
    // cree un objet le fait suivre des qu'il a son ID (image suivante), sans attendre le releve de 10 s -- un
    // article empoche ou mange juste apres sa creation disparait aussi chez les autres. Un autre, en tete de
    // l'etat, sert aux boites de pieces (Boxes : compteurs du distributeur avant la creation).
    public static class Consume
    {
        static readonly Dictionary<string, PlayMakerFSM> byId = new Dictionary<string, PlayMakerFSM>();
        static readonly HashSet<PlayMakerFSM> hooked = new HashSet<PlayMakerFSM>();
        static readonly HashSet<PlayMakerFSM> plainData = new HashSet<PlayMakerFSM>();   // pieces a ID sans fin suivie : plus relues
        static readonly HashSet<PlayMakerFSM> watched = new HashSet<PlayMakerFSM>();   // etats createurs deja observes
        static readonly List<PlayMakerFSM> toWatch = new List<PlayMakerFSM>();         // a lire, 2 ms par image
        static readonly System.Diagnostics.Stopwatch watchClock = new System.Diagnostics.Stopwatch();
        static int watchedStates;
        static readonly string[] GoneStates = { "Destroy", "Destroy 2", "Is garbage" };   // l'article quitte le monde
        const string Empty = "Empty";                                                      // vide, mais toujours la
        const string Bottle = "Remove bottle";
        public const int F_Snapshot = 1, F_Gone = 2, F_Vars = 4, F_Box = 8, F_Content = 16;
        static float nextScan = -1, lastRescan, nextWarn;
        internal static bool Applying;   // rejeu d'un message recu en cours (rien a renvoyer)
        static readonly List<KeyValuePair<string, string>> consumed = new List<KeyValuePair<string, string>>();   // hote : (ID, fin) depuis le chargement
        static readonly HashSet<string> remembered = new HashSet<string>();
        static readonly HashSet<string> rememberedGone = new HashSet<string>();   // (ID|fin) qui font disparaitre l'article
        static readonly HashSet<string> done = new HashSet<string>();   // articles disparus, dits ou recus (Props n'a rien a redire)
        static readonly List<KeyValuePair<float, Peer>> snapshots = new List<KeyValuePair<float, Peer>>();
        static readonly List<KeyValuePair<float, GameObject>> fresh = new List<KeyValuePair<float, GameObject>>();
        static int freshLogged;
        public static float EatUntil;

        // Hote : dernier contenu connu de chaque article (variables, etat a rejouer), pour un invite qui arrive.
        class Content { public string Visual = ""; public List<KeyValuePair<string, object>> Vars = new List<KeyValuePair<string, object>>(); }
        static readonly Dictionary<string, Content> contents = new Dictionary<string, Content>();

        // Contenu note quand le joueur quitte un etat d'attente par un clic.
        class Mark { public float At; public NamedVariable[] Vars; public object[] Values; }
        static readonly Dictionary<PlayMakerFSM, Mark> marks = new Dictionary<PlayMakerFSM, Mark>();

        // Caisse de biere dont le compte vient d'etre recopie : bouteilles visibles en trop retirees un peu apres
        // (les DestroyObject du jeu ne prennent effet qu'en fin d'image).
        class Recount { public PlayMakerFSM F; public string Id; public int Total; public float At; }
        static readonly List<Recount> recounts = new List<Recount>();

        // Message pour un article pas encore la ici (cree par le meme rejeu a l'instant, son ID pas encore
        // pose : sac ouvert puis paquet empoche, recus dans la meme image) : repris jusqu'a 3 s.
        class Late
        {
            public float Until; public string Id, State; public int Who, Flags; public bool Snapshot;
            public List<KeyValuePair<string, object>> Vars; public List<Boxes.Spawn> Spawns;
        }
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
                    if (!Applying && Replay.Depth == 0) OnLocal(Id, Fsm.PreviousActiveState != null ? Fsm.PreviousActiveState.Name : "", StateName, EndKind(State));
                }
                catch (System.Exception e) { Replay.HookError(e); }
                Finish();
            }
        }

        // Contenu : en tete d'un etat atteint par un clic depuis un etat d'attente (Check drink...).
        class UseMark : ModHook
        {
            public override string Module { get { return "consommables"; } }
            public PlayMakerFSM F;
            public override void OnEnter()
            {
                try { if (!Applying && Replay.Depth == 0) MarkUse(F); }
                catch (System.Exception e) { Replay.HookError(e); }
                Finish();
            }
        }

        // Contenu : en tete des etats d'attente du joueur (retour apres l'usage).
        class UseBack : ModHook
        {
            public override string Module { get { return "consommables"; } }
            public PlayMakerFSM F; public string Id;
            public override void OnEnter()
            {
                try { if (marks.Count > 0 && !Applying && Replay.Depth == 0) Back(F, Id, Fsm.PreviousActiveState); }
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
                        if (go == null) continue;
                        Fresh(go); any = true;
                        if (Boxes.Busy) Boxes.Created(Fsm.Owner as PlayMakerFSM, go);
                    }
                    if (!any) SoonScan();   // objet cree que l'automate ne garde pas : releve complet
                }
                catch (System.Exception e) { Replay.HookError(e); }
                Finish();
            }
        }

        // En tete du meme etat : compteurs du distributeur avant la creation (seulement pendant une prise dans
        // une boite, ici ou rejouee).
        class PreSpawn : FsmStateAction
        {
            public override void OnEnter()
            {
                try { if (Boxes.Busy) Boxes.BeforeCreate(Fsm.Owner as PlayMakerFSM); }
                catch (System.Exception e) { Replay.HookError(e); }
                Finish();
            }
        }

        public static void OnLevelLoaded()
        {
            byId.Clear(); hooked.Clear(); plainData.Clear(); watched.Clear(); toWatch.Clear(); consumed.Clear(); remembered.Clear(); rememberedGone.Clear(); snapshots.Clear(); fresh.Clear(); done.Clear(); late.Clear();
            missed.Clear(); missedOrder.Clear(); contents.Clear(); marks.Clear(); recounts.Clear();
            trackedCache.Clear(); destroyCache.Clear(); contentCache.Clear();
            testStep = 0; testLog = 0; testFsm = null; testBags = null; lastEnd = null; lastEndF = null;
            watchedStates = 0;
            Boxes.OnLevelLoaded();
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
                    Session.T.SendReliable(p, Write(Session.LocalId, c.Key, F_Snapshot | (rememberedGone.Contains(c.Key + "|" + c.Value) ? F_Gone : 0), c.Value, null, null).ToArray());
                int nc = 0;
                foreach (KeyValuePair<string, Content> c in contents)
                {
                    if (done.Contains(c.Key)) continue;
                    Session.T.SendReliable(p, Write(Session.LocalId, c.Key, F_Snapshot | F_Content, c.Value.Visual, c.Value.Vars, null).ToArray());
                    nc++;
                }
                if (consumed.Count > 0 || nc > 0) Log.Info("consommables : " + consumed.Count + " objets deja consommes et " + nc + " contenus envoyes a " + p);
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
                if (found) { Dispatch(f, l); continue; }
                if (!l.Snapshot) Miss(l);   // instantane d'arrivee : surtout des articles finis avant la sauvegarde, jamais la
                if (!l.Snapshot && now >= nextWarn) { nextWarn = now + 10f; Log.Warn("consommables : " + l.Id + " introuvable ici (garde pour quand il sera la)"); }
            }
            for (int i = recounts.Count - 1; i >= 0; i--)
            {
                if (now < recounts[i].At) continue;
                Recount rc = recounts[i];
                recounts.RemoveAt(i);
                RecountBottles(rc);
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

        // Etat suivi d'un automate Use (ou Data) d'article. Les fins autres que "Destroy" seulement si elles sont
        // terminales (rien ne s'enchaine : ni animation du joueur, ni logique de chargement), sans rien qui
        // touche au joueur ; un etat terminal d'un autre nom seulement s'il fait disparaitre l'objet (ou, pour un
        // "State N", le vide). Automate Data (pieces) : seulement les disparitions terminales. Lu une fois par etat.
        static readonly Dictionary<FsmState, bool> trackedCache = new Dictionary<FsmState, bool>();
        static readonly Dictionary<FsmState, int> destroyCache = new Dictionary<FsmState, int>();

        static bool Tracked(FsmState s)
        {
            string n = s.Name;
            bool data = s.Fsm != null && s.Fsm.Name == "Data";
            if (!data && (n == "Destroy" || IsBag(n))) return true;
            bool r;
            if (trackedCache.TryGetValue(s, out r)) return r;
            bool terminal = s.Transitions.Length == 0;
            if (data) r = terminal && (IsGoneName(n) || Destroys(s) == 2);
            else if (n == Bottle) r = true;
            else if (IsEndName(n)) r = terminal;
            else if (!terminal || n == "Add inventory") r = false;
            else
            {
                // "State N" vide seulement pour "State 4" (caisse de biere, bidons : deja suivi) et les objets nommes
                // par Shop (barquette du pub). Les boites de pieces sont a Boxes.
                int d = Destroys(s);
                r = d == 2 || IsGeneric(n) && d == 1 && (n == "State 4" || Shop.Named(IdOf(s)));
            }
            if (r) r = !TouchesPlayer(s);
            trackedCache[s] = r;
            return r;
        }

        static string IdOf(FsmState s)
        {
            FsmString v = s.Fsm != null ? s.Fsm.Variables.FindFsmString("ID") : null;
            return v != null ? v.Value : "";
        }

        // Ce que l'etat detruit : 2 l'objet lui-meme (DestroySelf, DestroyObject de l'objet, ActivateGameObject
        // faux sur l'objet), 1 autre chose (une bouteille, la barquette, un composant), 0 rien.
        static int Destroys(FsmState s)
        {
            int k;
            if (destroyCache.TryGetValue(s, out k)) return k;
            k = 0;
            GameObject self = s.Fsm != null ? s.Fsm.GameObject : null;
            FsmStateAction[] actions;
            try { actions = s.Actions; } catch { actions = new FsmStateAction[0]; }   // automate jamais demarre
            foreach (FsmStateAction a in actions)
            {
                if (a is HutongGames.PlayMaker.Actions.DestroySelf) { k = 2; break; }
                var d = a as HutongGames.PlayMaker.Actions.DestroyObject;
                if (d != null)
                {
                    GameObject g = d.gameObject != null ? d.gameObject.Value : null;
                    if (g != null && g == self) { k = 2; break; }
                    k = 1;
                    continue;
                }
                if (a is HutongGames.PlayMaker.Actions.DestroyComponent) { k = 1; continue; }
                var ag = a as HutongGames.PlayMaker.Actions.ActivateGameObject;
                if (ag != null && ag.activate != null && !ag.activate.Value && ag.gameObject != null
                    && (ag.gameObject.OwnerOption == OwnerDefaultOption.UseOwner || ag.gameObject.GameObject.Value == self && self != null)) { k = 2; break; }
            }
            destroyCache[s] = k;
            return k;
        }

        // Fin suivie : 2 l'article disparait (Destroy, jete, ou etat terminal qui le detruit), 1 vide mais toujours la,
        // 0 pas une fin.
        static int EndKind(FsmState s)
        {
            if (s == null) return 0;
            string n = s.Name;
            if (IsGoneName(n)) return 2;
            if (n == Empty) return 1;
            if (n == Bottle || IsBag(n) || !Tracked(s)) return 0;
            return Destroys(s);
        }

        // L'etat agit-il sur le joueur ou son interface (objet sous PLAYER ou GUI, evenement envoye a tous) ?
        // Rejoue chez l'autre, il toucherait a SON joueur.
        internal static bool TouchesPlayer(FsmState s)
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
                if ((f.FsmName == "Use" || f.FsmName == "Data" && DataItem(f)) && !hooked.Contains(f) && TryHook(f, created) && (++freshLogged <= 20 || freshLogged % 50 == 0))
                    Log.Info("consommables : " + idv.Value + " suivi des sa creation (" + byId.Count + ")");
            }
            return id;
        }

        // Automate Data d'une piece a ID (actif : ses actions sont chargees), pas un point de montage (Parts), pas
        // un objet qui a deja un automate Use a ID (suivi par celui-la).
        static bool DataItem(PlayMakerFSM f)
        {
            if (!f.gameObject.activeInHierarchy || f.gameObject.name.StartsWith("VINP")) return false;
            FsmString idv = f.FsmVariables.FindFsmString("ID");
            if (idv == null || idv.Value.Length == 0) return false;
            if (Replay.ClaimedByOther(f, "consommables")) return false;
            try { if (f.Fsm.GetState("Install 2") != null) return false; } catch { return false; }
            foreach (PlayMakerFSM o in f.GetComponents<PlayMakerFSM>())
            {
                if (o == f || o.FsmName != "Use") continue;
                FsmString u = o.FsmVariables.FindFsmString("ID");
                if (u != null && u.Value.Length > 0) return false;
            }
            return true;
        }

        static void Scan()
        {
            hooked.RemoveWhere(x => x == null); plainData.RemoveWhere(x => x == null);
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
                if (hooked.Contains(f) || plainData.Contains(f)) continue;
                if (f.FsmName == "Use") { if (TryHook(f)) n++; }
                else if (f.FsmName == "Data" && DataItem(f)) { if (TryHook(f)) n++; else plainData.Add(f); }
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
                try
                {
                    if (!Tracked(s)) continue;
                    var list = new List<FsmStateAction>(s.Actions);
                    list.Insert(0, new Hook { Id = idv.Value, StateName = s.Name });
                    s.Actions = list.ToArray();
                    any = true;
                }
                catch { }
            }
            if (f.FsmName == "Use")
            {
                // Boite de pieces (Boxes) ; sinon contenu qui reste apres usage (bidon de jus, caisse de biere).
                try { if (Boxes.Hook(f, idv.Value) > 0) any = true; else if (HookContent(f, idv.Value)) any = true; }
                catch (System.Exception e) { Replay.HookError(e); }
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

        // Etats qui creent un objet (CreateObject) : un observateur ajoute en fin d'etat, apres la creation, et un
        // en tete (compteurs avant la creation, pour Boxes).
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
                    list.Insert(0, new PreSpawn());
                    list.Add(new Spawned(made.ToArray()));
                    st.Actions = list.ToArray();
                    n++;
                }
            }
            catch { }
            return n;
        }

        // ---------------------------------------------------------------- contenu (bidon de jus, caisse de biere)
        static readonly Dictionary<PlayMakerFSM, NamedVariable[]> contentCache = new Dictionary<PlayMakerFSM, NamedVariable[]>();
        static readonly HashSet<string> InputActions = new HashSet<string> { "MousePickEvent", "GetButtonDown", "GetButtonUp", "GetMouseButtonDown", "GetMouseButtonUp", "GetKeyDown", "GetButton" };

        // Variables de contenu : int, float ou bool propres a l'automate, dont le nom est (ou finit par) le suffixe
        // d'une variable texte UniqueTag<Suffixe> (cle de sauvegarde) : ContainsJuice/KiljuAlc (UniqueTagJuice,
        // UniqueTagKiljuAlc), DestroyedBottles (UniqueTagBottles), Quantity (UniqueTagQuantity). Pas Condition
        // (fraicheur, que chacun fait baisser), ni ItemConsumed/Consumed (la fin de l'article le dit).
        internal static NamedVariable[] ContentVars(PlayMakerFSM f)
        {
            NamedVariable[] r;
            if (contentCache.TryGetValue(f, out r)) return r;
            var suffixes = new List<string>();
            foreach (FsmString s in f.FsmVariables.StringVariables)
                if (s.Name.StartsWith("UniqueTag") && s.Name.Length > 9 && s.Name != "UniqueTagTransform" && s.Name != "UniqueTagConsumed") suffixes.Add(s.Name.Substring(9));
            var l = new List<NamedVariable>();
            if (suffixes.Count > 0)
                foreach (NamedVariable v in f.FsmVariables.GetAllNamedVariables())
                {
                    if (!(v is FsmInt || v is FsmFloat || v is FsmBool)) continue;
                    if (v.Name == "Condition" || v.Name == "ItemConsumed" || v.Name == "Consumed") continue;
                    foreach (string sfx in suffixes) if (v.Name == sfx || v.Name.EndsWith(sfx)) { l.Add(v); break; }
                    if (l.Count >= 12) break;
                }
            r = l.ToArray();
            contentCache[f] = r;
            return r;
        }

        static object ValueOf(NamedVariable v)
        {
            if (v is FsmInt) return ((FsmInt)v).Value;
            if (v is FsmFloat) return ((FsmFloat)v).Value;
            if (v is FsmBool) return ((FsmBool)v).Value;
            return null;
        }

        static bool Same(object a, object b)
        {
            if (a is float && b is float) return Mathf.Abs((float)a - (float)b) < 0.0001f;
            return Equals(a, b);
        }

        // Contenu actuel de 'f' (Boxes : compte restant d'une boite).
        internal static List<KeyValuePair<string, object>> ContentValues(PlayMakerFSM f)
        {
            var r = new List<KeyValuePair<string, object>>();
            foreach (NamedVariable v in ContentVars(f)) r.Add(new KeyValuePair<string, object>(v.Name, ValueOf(v)));
            return r;
        }

        // Pose le contenu recu (variables propres seulement, du bon type).
        internal static void SetValues(PlayMakerFSM f, List<KeyValuePair<string, object>> vars)
        {
            if (vars == null) return;
            foreach (KeyValuePair<string, object> kv in vars)
            {
                if (!Game.LocalVar(f, kv.Key)) continue;
                if (kv.Value is int) { FsmInt v = f.FsmVariables.FindFsmInt(kv.Key); if (v != null) v.Value = (int)kv.Value; }
                else if (kv.Value is float) { FsmFloat v = f.FsmVariables.FindFsmFloat(kv.Key); if (v != null) v.Value = (float)kv.Value; }
                else if (kv.Value is bool) { FsmBool v = f.FsmVariables.FindFsmBool(kv.Key); if (v != null) v.Value = (bool)kv.Value; }
            }
        }

        internal static string Describe(List<KeyValuePair<string, object>> vars)
        {
            if (vars == null || vars.Count == 0) return "-";
            var sb = new System.Text.StringBuilder();
            foreach (KeyValuePair<string, object> kv in vars)
            {
                if (sb.Length > 0) sb.Append(' ');
                sb.Append(kv.Key).Append('=').Append(kv.Value is float ? ((float)kv.Value).ToString("0.###") : kv.Value.ToString());
            }
            return sb.ToString();
        }

        static bool HasInput(FsmState s)
        {
            foreach (FsmStateAction a in s.Actions) if (a != null && InputActions.Contains(a.GetType().Name)) return true;
            return false;
        }

        // Evenements que les actions de commande de l'etat envoient (USE...).
        static HashSet<string> InputEvents(FsmState s)
        {
            var r = new HashSet<string>();
            foreach (FsmStateAction a in s.Actions)
            {
                if (a == null || !InputActions.Contains(a.GetType().Name)) continue;
                foreach (System.Reflection.FieldInfo fi in a.GetType().GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
                {
                    var ev = fi.GetValue(a) as FsmEvent;
                    if (ev != null && !string.IsNullOrEmpty(ev.Name) && ev.Name != "FINISHED") r.Add(ev.Name);
                }
            }
            return r;
        }

        // Crochets de contenu : marque en tete des etats atteints par un clic depuis un etat d'attente, controle en
        // tete des etats d'attente. Seulement pour un automate qui a des variables de contenu.
        static bool HookContent(PlayMakerFSM f, string id)
        {
            if (ContentVars(f).Length == 0) return false;
            var rest = new List<FsmState>();
            foreach (FsmState s in f.Fsm.States) if (HasInput(s)) rest.Add(s);
            if (rest.Count == 0) return false;
            var targets = new HashSet<string>();
            foreach (FsmState s in rest)
            {
                HashSet<string> ev = InputEvents(s);
                foreach (FsmTransition t in s.Transitions)
                {
                    if (!ev.Contains(t.EventName)) continue;
                    FsmState to = f.Fsm.GetState(t.ToState);
                    if (to != null && !rest.Contains(to)) targets.Add(to.Name);
                }
            }
            if (targets.Count == 0) return false;
            foreach (FsmState s in rest) Prepend(s, new UseBack { F = f, Id = id });
            foreach (string n in targets) Prepend(f.Fsm.GetState(n), new UseMark { F = f });
            return true;
        }

        static void Prepend(FsmState s, FsmStateAction a)
        {
            var list = new List<FsmStateAction>(s.Actions);
            list.Insert(0, a);
            s.Actions = list.ToArray();
        }

        internal static void MarkUse(PlayMakerFSM f)
        {
            NamedVariable[] vars = ContentVars(f);
            if (vars.Length == 0) return;
            var m = new Mark { At = Time.realtimeSinceStartup, Vars = vars, Values = new object[vars.Length] };
            for (int i = 0; i < vars.Length; i++) m.Values[i] = ValueOf(vars[i]);
            marks[f] = m;
        }

        // Retour dans un etat d'attente apres un usage : ce qui a change dans le contenu part aux autres.
        static void Back(PlayMakerFSM f, string id, FsmState prev)
        {
            Mark m;
            if (f == null || !marks.TryGetValue(f, out m)) return;
            marks.Remove(f);
            if (Time.realtimeSinceStartup - m.At > 60f || done.Contains(id)) return;
            var diff = new List<KeyValuePair<string, object>>();
            for (int i = 0; i < m.Vars.Length; i++)
            {
                object now = ValueOf(m.Vars[i]);
                if (!Same(now, m.Values[i])) diff.Add(new KeyValuePair<string, object>(m.Vars[i].Name, now));
            }
            if (diff.Count == 0) return;
            string visual = prev != null && prev.Fsm == f.Fsm && SafeVisual(prev, f) ? prev.Name : "";
            if (Session.IsHost) RememberContent(id, visual, diff);
            Log.Info("consommables : contenu de " + id + " change ici : " + Describe(diff) + (visual.Length > 0 ? " (" + visual + ")" : ""));
            Session.SendAll(Write(Session.LocalId, id, F_Content, visual, diff, null), true);
        }

        // Etat a rejouer chez les autres avec le contenu : seulement s'il ne fait que renommer, tester ou poser des
        // variables propres a l'automate (pas de globale, rien sous PLAYER/GUI).
        static readonly HashSet<string> VisualActions = new HashSet<string> { "SetName", "SetBoolValue", "SetFloatValue", "SetIntValue",
            "SetStringValue", "BoolTest", "BoolNoneTrue", "BoolAllTrue", "IntCompare", "FloatCompare", "SetTag", "SetMaterial", "SetMass" };

        static bool SafeVisual(FsmState s, PlayMakerFSM f)
        {
            if (s.Name == Bottle || IsBag(s.Name) || EndKind(s) > 0) return false;
            foreach (FsmStateAction a in s.Actions)
            {
                if (a == null || a.GetType().Assembly == typeof(Consume).Assembly) continue;   // nos crochets
                if (!VisualActions.Contains(a.GetType().Name)) return false;
                foreach (System.Reflection.FieldInfo fi in a.GetType().GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
                {
                    var nv = fi.GetValue(a) as NamedVariable;
                    if (nv != null && nv.UseVariable && !string.IsNullOrEmpty(nv.Name) && !Game.LocalVar(f, nv.Name)) return false;
                }
            }
            return !TouchesPlayer(s);
        }

        static void RememberContent(string id, string visual, List<KeyValuePair<string, object>> vars)
        {
            Content c;
            if (!contents.TryGetValue(id, out c)) contents[id] = c = new Content();
            if (visual != null) c.Visual = visual;
            foreach (KeyValuePair<string, object> kv in vars)
            {
                int i = c.Vars.FindIndex(x => x.Key == kv.Key);
                if (i >= 0) c.Vars[i] = kv; else c.Vars.Add(kv);
            }
        }

        // Boxes : compte restant d'une boite, a donner aussi a un invite qui arrive.
        internal static void RememberBox(string id, List<KeyValuePair<string, object>> vars)
        {
            if (Session.IsHost && vars != null && vars.Count > 0) RememberContent(id, null, vars);
        }

        static int ActiveChildren(Transform t)
        {
            int n = 0;
            for (int i = 0; i < t.childCount; i++) if (t.GetChild(i).gameObject.activeSelf) n++;
            return n;
        }

        // Bouteilles + bouteilles bues (DestroyedBottles) : le total de la caisse (-1 : pas une caisse).
        static int BottleTotal(PlayMakerFSM f)
        {
            FsmInt d = Game.LocalVar(f, "DestroyedBottles") ? f.FsmVariables.FindFsmInt("DestroyedBottles") : null;
            return d != null && f.Fsm.GetState(Bottle) != null ? ActiveChildren(f.transform) + d.Value : -1;
        }

        // Caisse dont le compte a ete recopie : bouteilles visibles en trop (comptes qui avaient diverge) retirees.
        static void RecountBottles(Recount rc)
        {
            if (rc.F == null) return;
            FsmInt d = rc.F.FsmVariables.FindFsmInt("DestroyedBottles");
            if (d == null) return;
            Transform t = rc.F.transform;
            int want = Mathf.Max(0, rc.Total - d.Value), have = ActiveChildren(t), removed = 0;
            for (int i = 0; i < t.childCount && have > want; i++)
            {
                GameObject c = t.GetChild(i).gameObject;
                if (!c.activeSelf) continue;
                c.SetActive(false);
                Object.Destroy(c);
                have--; removed++;
            }
            if (removed > 0) Log.Info("consommables : " + rc.Id + " : " + removed + " bouteille(s) en trop retiree(s) ici (" + have + " restent)");
        }

        // Contenu recu : variables posees, puis l'etat d'apres (renomme vide) rejoue s'il est sans risque.
        static void ApplyContent(PlayMakerFSM f, string id, string visual, List<KeyValuePair<string, object>> vars, int who, bool snapshot)
        {
            if (EndKind(f.Fsm.ActiveState) == 2) return;   // deja jete ou bu ici
            int total = BottleTotal(f);
            Applying = true; Replay.Depth++;
            try
            {
                SetValues(f, vars);
                FsmState vs = string.IsNullOrEmpty(visual) ? null : f.Fsm.GetState(visual);
                if (vs != null && f.ActiveStateName != visual && SafeVisual(vs, f)) Game.SetState(f, visual);
            }
            finally { Applying = false; Replay.Depth--; }
            if (total >= 0) recounts.Add(new Recount { F = f, Id = id, Total = total, At = Time.realtimeSinceStartup + 0.3f });
            Log.Info("consommables : contenu de " + id + (snapshot ? " (arrivee)" : " (joueur #" + who + ")") + " : " + Describe(vars) + (string.IsNullOrEmpty(visual) ? "" : " -> " + f.ActiveStateName));
        }

        // ---------------------------------------------------------------- messages
        static NetWriter Write(int who, string id, int flags, string state, List<KeyValuePair<string, object>> vars, List<Boxes.Spawn> spawns)
        {
            flags = flags & ~(F_Vars | F_Box) | (vars != null ? F_Vars : 0) | (spawns != null ? F_Box : 0);
            var w = new NetWriter(Msg.Consume).U8(who).Str(id).U8(flags).Str(state ?? "");
            if (vars != null)
            {
                int n = Mathf.Min(vars.Count, 12);
                w.U8(n);
                for (int i = 0; i < n; i++)
                {
                    object v = vars[i].Value;
                    if (v is int) w.U8(0).Str(vars[i].Key).I32((int)v);
                    else if (v is float) w.U8(1).Str(vars[i].Key).F32((float)v);
                    else w.U8(2).Str(vars[i].Key).U8(v is bool && (bool)v ? 1 : 0);
                }
            }
            if (spawns != null) Boxes.Write(w, spawns);
            return w;
        }

        static List<KeyValuePair<string, object>> ReadVars(NetReader r)
        {
            int n = r.U8();
            var l = new List<KeyValuePair<string, object>>(n);
            for (int i = 0; i < n; i++)
            {
                int kind = r.U8();
                string name = r.Str();
                object v = kind == 0 ? (object)r.I32() : kind == 1 ? (object)r.F32() : (object)(r.U8() != 0);
                l.Add(new KeyValuePair<string, object>(name, v));
            }
            return l;
        }

        // Boxes : prise dans une boite ici (etat, compte restant, objets crees).
        internal static void SendBox(string id, string state, List<KeyValuePair<string, object>> vars, List<Boxes.Spawn> spawns)
        {
            RememberBox(id, vars);
            Session.SendAll(Write(Session.LocalId, id, 0, state, vars, spawns), true);
        }

        // Hote : fins a renvoyer a un invite qui arrive (une fois par article et par fin).
        static void Remember(string id, string state, bool gone)
        {
            if (remembered.Add(id + "|" + state)) consumed.Add(new KeyValuePair<string, string>(id, state));
            if (gone) rememberedGone.Add(id + "|" + state);
        }

        static int Flags(bool snapshot, bool gone) { return (snapshot ? F_Snapshot : 0) | (gone ? F_Gone : 0); }

        static void OnLocal(string id, string from, string state, int end)
        {
            if (IsBag(state))
            {
                Log.Info("consommables : sac " + id + " ouvert ici (" + state + ")");
                Session.SendAll(Write(Session.LocalId, id, 0, state, null, null), true);
                return;
            }
            if (state == Bottle)
            {
                Log.Info("consommables : " + id + " une bouteille de moins ici");
                Session.SendAll(Write(Session.LocalId, id, 0, state, null, null), true);
                return;
            }
            // Mange ou bu (pas jete) : la main va a la bouche.
            if (from.Contains("Eat") || from.Contains("Drink") || from.Contains("drink") || from == "Play anim")
                EatUntil = Time.realtimeSinceStartup + 2f;
            bool gone = end == 2;
            if (gone) done.Add(id);
            if (Session.IsHost) Remember(id, state, gone);
            lastEnd = id;
            byId.TryGetValue(id, out lastEndF);
            Log.Info("consommables : " + id + (gone ? " consomme ou jete ici (": " vide ici (") + from + (state != "Destroy" ? " -> " + state : "") + ")");
            Session.SendAll(Write(Session.LocalId, id, Flags(false, gone), state, null, null), true);
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
            Session.SendAll(Write(Session.LocalId, id, Flags(false, gone), state, null, null), true);
        }

        public static void OnMessage(Peer from, NetReader r)
        {
            int who = r.U8();
            if (Session.IsHost) who = from.Id;
            string id = r.Str();
            int flags = r.U8();
            bool snapshot = (flags & F_Snapshot) != 0;
            string state = r.More ? r.Str() : "Destroy";
            List<KeyValuePair<string, object>> vars = (flags & F_Vars) != 0 ? ReadVars(r) : null;
            List<Boxes.Spawn> spawns = (flags & F_Box) != 0 ? Boxes.Read(r) : null;
            bool special = spawns != null || (flags & F_Content) != 0;   // prise dans une boite, contenu : pas une fin
            bool gone = !special && ((flags & F_Gone) != 0 || IsGoneName(state));
            bool over = done.Contains(id);   // deja disparu ici (mange, jete, recu) : rien a rejouer ni a cacher
            if (gone) done.Add(id);
            // Fin (a renvoyer a un invite qui arrive) : tout etat suivi sauf sac ouvert et bouteille retiree.
            bool end = !special && (gone || IsEndName(state) || IsGeneric(state));
            if (Session.IsHost)
            {
                if (end) Remember(id, state, gone);
                if (vars != null && !snapshot) RememberContent(id, (flags & F_Content) != 0 ? state : null, vars);
                Session.Broadcast(Write(who, id, (flags & ~F_Snapshot & ~F_Gone) | (gone ? F_Gone : 0), state, vars, spawns), true, who);
            }
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
            var msg = new Late { Until = now + 3f, Id = id, State = state, Who = who, Flags = flags, Snapshot = snapshot, Vars = vars, Spawns = spawns };
            if (f == null || queued)
            {
                // Disparu chez l'autre, sans automate suivi ici pour le rejouer : Props le cache s'il le connait
                // (pas un objet seulement vide : il est toujours la, et sa vraie fin viendra).
                if (f == null && gone && Props.Vanish(id)) { Log.Info("consommables : " + id + " disparu chez le joueur #" + who + ", cache ici"); return; }
                late.Add(msg);
                return;
            }
            Dispatch(f, msg);
        }

        static void Dispatch(PlayMakerFSM f, Late l)
        {
            if (l.Spawns != null)
            {
                // Prise dans une boite : pas dans l'instantane d'arrivee (la sauvegarde envoyee a deja les objets).
                if (l.Snapshot) return;
                Applying = true; Replay.Depth++;
                try { Boxes.Apply(f, l.Id, l.State, l.Who, l.Vars, l.Spawns); }
                finally { Applying = false; Replay.Depth--; }
                return;
            }
            if ((l.Flags & F_Content) != 0) { ApplyContent(f, l.Id, l.State, l.Vars, l.Who, l.Snapshot); return; }
            Apply(f, l.Id, l.State, l.Who, l.Snapshot);
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
            Applying = true; Replay.Depth++;
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
            finally { Applying = false; Replay.Depth--; }
            if (IsBag(state)) { Log.Info("consommables : sac " + id + " ouvert par le joueur #" + who + " (" + state + ")"); return; }
            if (state == Bottle) { Log.Info("consommables : " + id + " une bouteille de moins (joueur #" + who + ")"); return; }
            lastEnd = id; lastEndF = f;
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

        // ---------------------------------------------------------------- essais
        // Boxes (essai 'boite') : articles suivis, releve force.
        internal static Dictionary<string, PlayMakerFSM> ById { get { return byId; } }
        internal static void ScanNow() { Scan(); }

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

        // Essais : sacs de courses suivis maintenant (a laisser quand on ouvre celui d'un achat).
        internal static HashSet<string> TestBags()
        {
            var r = new HashSet<string>();
            foreach (KeyValuePair<string, PlayMakerFSM> kv in byId) if (kv.Value != null && kv.Value.Fsm.GetState("Spawn all") != null) r.Add(kv.Key);
            return r;
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
        static PlayMakerFSM testFsm, lastEndF;

        public static void Test(string mode, float t)
        {
            Shop.Test(mode, t);   // modes bar, cafe, sac-double (achats)
            Boxes.Test(mode, t);  // mode boite
            if (mode == "boite-vide") { TestBox(t); return; }
            if (mode == "gorgee") { TestSip(t); return; }
            if (mode == "biere") { TestBeer(t); return; }
            if (mode == "gorgee2") { TestJuice(t); return; }
            if (mode == "poubelle") { TestTrash(t); return; }
            if (mode != "poche") return;
            string prefix = Config.Get("Test", "TestPiece", "cigarettes");
            if (Session.IsHost && t > 30f && testStep == 0)
            {
                testStep = 1;
                testPacks = new HashSet<string>();
                foreach (PlayMakerFSM f in Pockets(prefix)) testPacks.Add(f.FsmVariables.FindFsmString("ID").Value);
                testBags = TestBags();
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

        // Essais : article suivi le plus proche du joueur (ID commencant par 'prefix') que 'ok' accepte.
        static PlayMakerFSM TestFind(string prefix, System.Predicate<PlayMakerFSM> ok)
        {
            Scan();
            GameObject pl = GameObject.Find("PLAYER");
            Vector3 me = pl != null ? pl.transform.position : Vector3.zero;
            PlayMakerFSM best = null;
            foreach (KeyValuePair<string, PlayMakerFSM> kv in byId)
            {
                PlayMakerFSM f = kv.Value;
                if (f == null || !kv.Key.StartsWith(prefix) || EndKind(f.Fsm.ActiveState) > 0 || !ok(f)) continue;
                if (best == null || (f.transform.position - me).sqrMagnitude < (best.transform.position - me).sqrMagnitude) best = f;
            }
            return best;
        }

        static string IdText(PlayMakerFSM f)
        {
            FsmString v = f != null ? f.FsmVariables.FindFsmString("ID") : null;
            return v != null ? v.Value : "?";
        }

        // Essais : achat de secours ([Test] TestAchat ; vide = pas d'achat) quand l'essai ne trouve pas son article,
        // sac neuf vide 3 s plus tard (pas pour la caisse de biere, gros article hors sac).
        static void TestBuyFallback(float t, string product, bool bag)
        {
            if (product.Length == 0) { Log.Info("autotest : rien de tel ici, pas d'achat ([Test] TestAchat vide)"); return; }
            testBags = TestBags();
            Log.Info("autotest : " + Shop.TestBuy(product, 1));
            testAt = t;
            if (!bag) testBags = null;
        }

        // Essais ([Test] Autotest=biere, TestPos dans le magasin) : a 30 s l'hote prend une bouteille dans la caisse de
        // biere suivie la plus proche ([Test] TestPiece : debut d'ID, sinon n'importe laquelle ; aucune : achat de
        // [Test] TestAchat, Beer par defaut, prise 6 s plus tard), comme le clic (contenu note, 'Remove bottle' --
        // la biere va dans SA main), puis une 2e 8 s apres. Chacun note toutes les 2 s de 28 a 70 s chaque caisse a
        // moins de 80 m : bouteilles visibles, DestroyedBottles, etat. Attendu : memes nombres des deux cotes pour le
        // meme ID ; chez l'invite « une bouteille de moins (joueur #0) » puis « contenu de beercaseN (joueur #0) :
        // DestroyedBottles=… », jamais de biere en main.
        static void TestBeer(float t)
        {
            string prefix = Config.Get("Test", "TestPiece", "");
            System.Predicate<PlayMakerFSM> isCase = f => f.Fsm.GetState(Bottle) != null && ActiveChildren(f.transform) > 0;
            if (Session.IsHost && t > 30f && testStep == 0)
            {
                testStep = 1;
                testFsm = TestFind(prefix, isCase);
                if (testFsm == null) TestBuyFallback(t, Config.Get("Test", "TestAchat", "Beer"), false);
            }
            if (Session.IsHost && testStep == 1 && (testFsm != null || t > testAt + 6f))
            {
                if (testFsm == null) testFsm = TestFind(prefix, isCase);
                testStep = testFsm != null ? 2 : 9;
                testAt = t;
                if (testFsm == null) Log.Info("autotest : biere, aucune caisse (" + byId.Count + " suivis)");
                else TestTakeBottle(testFsm);
            }
            if (Session.IsHost && testStep == 2 && t > testAt + 8f) { testStep = 3; if (testFsm != null) TestTakeBottle(testFsm); }
            if (t > 28f && t < 70f && t - testLog >= 2f)
            {
                testLog = t;
                Log.Info("autotest : biere " + TestList(80f, f => f.Fsm.GetState(Bottle) != null, f =>
                {
                    FsmInt d = f.FsmVariables.FindFsmInt("DestroyedBottles");
                    return "bouteilles=" + ActiveChildren(f.transform) + " DestroyedBottles=" + (d != null ? d.Value.ToString() : "?");
                }));
            }
        }

        static void TestTakeBottle(PlayMakerFSM f)
        {
            MarkUse(f);
            Game.SetState(f, Bottle);
            FsmInt d = f.FsmVariables.FindFsmInt("DestroyedBottles");
            Log.Info("autotest : biere prise dans " + IdText(f) + " -> " + f.ActiveStateName + " (DestroyedBottles=" + (d != null ? d.Value.ToString() : "?") + ")");
        }

        // Essais : articles suivis a moins de 'radius' m que 'ok' accepte, tries par ID : "ID [etat] details ; ...".
        static string TestList(float radius, System.Predicate<PlayMakerFSM> ok, System.Func<PlayMakerFSM, string> details)
        {
            GameObject pl = GameObject.Find("PLAYER");
            Vector3 me = pl != null ? pl.transform.position : Vector3.zero;
            var keys = new List<string>();
            foreach (KeyValuePair<string, PlayMakerFSM> kv in byId)
                if (kv.Value != null && (kv.Value.transform.position - me).sqrMagnitude < radius * radius && ok(kv.Value)) keys.Add(kv.Key);
            keys.Sort(System.StringComparer.Ordinal);
            var sb = new System.Text.StringBuilder();
            foreach (string k in keys)
            {
                PlayMakerFSM f = byId[k];
                sb.Append(k).Append(" [").Append(f.ActiveStateName).Append("] ").Append(details(f)).Append(" ; ");
            }
            return sb.Length > 0 ? sb.ToString() : "aucun";
        }

        // Essais ([Test] Autotest=gorgee2, TestPos dans le magasin) : a 30 s l'hote boit au bidon de jus suivi le plus
        // proche qui contient du jus ([Test] TestPiece : debut d'ID ; aucun : achat de [Test] TestAchat, Juice par
        // defaut, sac vide 3 s plus tard, bidon cherche 6 s apres l'achat), comme le clic : contenu note, 'Play anim'
        // (DRINKJUICE a SON joueur) -> Status -> Empty -> Wait player. Chacun note toutes les 2 s de 28 a 60 s les
        // bidons a moins de 80 m : ContainsJuice, ContainsKilju, nom. Attendu : chez l'invite « contenu de
        // juiceconcentrateN (joueur #0) : ContainsJuice=False -> Wait player » et le meme nom (vide) des deux cotes.
        static void TestJuice(float t)
        {
            string prefix = Config.Get("Test", "TestPiece", "");
            System.Predicate<PlayMakerFSM> full = f => { FsmBool b = f.FsmVariables.FindFsmBool("ContainsJuice"); return b != null && b.Value && Game.LocalVar(f, "ContainsJuice"); };
            if (Session.IsHost && t > 30f && testStep == 0)
            {
                testStep = 1;
                testFsm = TestFind(prefix, full);
                if (testFsm == null) TestBuyFallback(t, Config.Get("Test", "TestAchat", "Juice"), true);
            }
            if (Session.IsHost && testStep == 1 && testFsm == null && testBags != null && t > testAt + 3f) { Log.Info("autotest : " + TestOpenBag(testBags)); testBags = null; }
            if (Session.IsHost && testStep == 1 && (testFsm != null || t > testAt + 6f))
            {
                if (testFsm == null) testFsm = TestFind(prefix, full);
                testStep = 2;
                if (testFsm == null) { Log.Info("autotest : gorgee2, aucun bidon de jus (" + byId.Count + " suivis)"); }
                else
                {
                    MarkUse(testFsm);
                    Game.SetState(testFsm, "Play anim");
                    Log.Info("autotest : gorgee2 " + IdText(testFsm) + " -> " + testFsm.ActiveStateName);
                }
            }
            if (t > 28f && t < 60f && t - testLog >= 2f)
            {
                testLog = t;
                Log.Info("autotest : bidons " + TestList(80f, f => Game.LocalVar(f, "ContainsJuice"), f =>
                {
                    FsmBool j = f.FsmVariables.FindFsmBool("ContainsJuice"), k = f.FsmVariables.FindFsmBool("ContainsKilju");
                    return "ContainsJuice=" + (j != null && j.Value) + " ContainsKilju=" + (k != null && k.Value) + " nom=" + f.gameObject.name;
                }));
            }
        }

        // Essais ([Test] Autotest=poubelle) : a 30 s l'hote envoie GARBAGE (comme une poubelle) a l'article suivi le
        // plus proche qui l'ecoute ([Test] TestPiece : debut d'ID ; [Test] TestPoubelle=data : une piece, automate
        // Data). Chacun note toutes les 2 s de 28 a 50 s la derniere fin vue ici (dite ou recue), son etat et sa
        // place. Attendu chez l'invite : « consomme par le joueur #0 (Is garbage / Destroy / Destroy 2) », meme ID,
        // meme etat et meme place (celle de la poubelle) que chez l'hote.
        static void TestTrash(float t)
        {
            if (Session.IsHost && t > 30f && testStep == 0)
            {
                testStep = 1;
                bool data = Config.Get("Test", "TestPoubelle", "") == "data";
                PlayMakerFSM f = TestFind(Config.Get("Test", "TestPiece", ""), x =>
                {
                    if ((x.FsmName == "Data") != data) return false;
                    foreach (FsmTransition g in x.Fsm.GlobalTransitions) if (g.EventName == "GARBAGE") return true;
                    return false;
                });
                if (f == null) { Log.Info("autotest : poubelle, rien a jeter (" + byId.Count + " suivis)"); return; }
                f.SendEvent("GARBAGE");
                Log.Info("autotest : poubelle " + IdText(f) + " (" + f.FsmName + ") -> " + f.ActiveStateName + " en " + f.transform.position.ToString("F2"));
            }
            if (t > 28f && t < 50f && t - testLog >= 2f)
            {
                testLog = t;
                PlayMakerFSM f = lastEndF;
                Log.Info("autotest : poubelle derniere fin " + (lastEnd ?? "aucune") + " [" + (f != null ? f.ActiveStateName : "?") + "] en "
                         + (f != null ? f.transform.position.ToString("F2") : lastEnd != null ? Props.Where(lastEnd) : "?"));
            }
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
