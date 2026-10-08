using System.Collections.Generic;
using System.Reflection;
using HutongGames.PlayMaker;
using MWCoop.Net;
using UnityEngine;

namespace MWCoop
{
    // Maison : fusibles et electricite, kilju (lot 4).
    //
    // 1) FUSIBLES. Tableaux : appartement (HOMENEW/Functions/ElectricThings/FuseTable, 4 porte-fusibles) et maison des
    //    parents (YARD/Building/Dynamics/FuseTable, 7). Chaque porte-fusible 'fuse holder(Clone)' :: Use tient FuseState
    //    (fusible bon, grille, absent) et le recopie dans la base ElecDatabase* ('Set data', ArrayListSet), que
    //    ElectricAppliances 'Fuses' relit en boucle pour les lampes (LOD_Consumption : Fuse). Un fusible grille par
    //    BLOWFUSE ('Set data' -> 'State 6' : SetIntValue), envoye par la logique de surcharge (ordinateur 'Fuseblow',
    //    orage...) qui tourne chez chacun : il grillait chez un seul joueur, et les lampes divergeaient.
    //     - l'hote decide : ses changements de FuseState sont envoyes. Chez un invite, un BLOWFUSE de sa propre logique
    //       est bloque ('State 6' sans son SetIntValue : le fusible reste bon) ; seul celui de l'hote passe.
    //     - fusible retire (UNINSTALL -> 'State 5') ou remis (FuseTrigger 'Assembly' -> 'Assemble' : FuseState, maillage,
    //       INSTALL), par n'importe qui : son nouveau FuseState est envoye et refait chez les autres par les memes
    //       evenements (UNINSTALL ; ou FuseState, maillage allume, declencheur eteint, INSTALL). Base et lampes suivent.
    //     - FuseState releve 5 fois par seconde : le declencheur FuseTrigger (eteint au releve du monde) n'a pas besoin
    //       d'etre accroche. L'hote envoie aussi l'etat complet toutes les 10 s (invite arrive, message perdu).
    //    Ces automates 'Use' sont pris a ce module avant le releve du monde (16 s) : le monde n'y rejoue plus UNINSTALL ni
    //    ne recopie leurs variables (FuseState et Tightness sont envoyes ici).
    //    HouseElectricity 'Status' (coupure : lampes eteintes, 'Lights off 2' / 'State 10') : etat de l'hote envoye ; un
    //    invite reste allume plus de 3 s alors que l'hote est coupe (ou l'inverse) : recale (au plus une fois par 30 s).
    // 2) KILJU. Seau EQUIPMENTS/bucket(itemx) :: Use (Sugar, Yeast, Water, Alcohol, Time, LidOn, KiljuFinished...) : le
    //    monde ne le suit pas ((itemx)), chacun brassait le sien. Un joueur fait autorite (l'hote au chargement, puis le
    //    dernier qui a tenu le seau ou son couvercle, ou y a jete sucre ou levure) : il envoie les variables du seau
    //    quand elles changent (3 s au plus souvent, 5 s sans changement) ; les autres les recopient, et recalent
    //    l'affichage ('State 3') quand un ingredient ou un drapeau a change.
    //     - sucre et levure (TriggerIngredients 'Level' : 'Sugar'/'Yeast', AddFsmFloat au seau) : comptes seulement chez
    //       celui qui les a jetes (la copie qui tombe dans le seau chez l'autre n'ajoute rien) ;
    //     - couvercle retire ('bucket lid(itemx)' :: Removal 'Remove part', ignore du monde : (itemx)) : refait chez les
    //       autres (sinon il restait sur le seau chez eux, sans corps : Props ne pouvait pas le deplacer) ;
    //     - vente a Jokke (JOBS/JOKKEHOME/.../KiljuBuyer/CanTrigger :: Logic) : 'Freeze Can' (Jokke prend la canette)
    //       chez tous, mais le gout, le verdict et la paie (PayMoney) seulement chez celui qui a pose la canette ; chez
    //       les autres l'automate passe 'Player?' sans rien tester, coupe la replique de 'Lets see' et revient a 'Wait'.
    //       La paie touchee est gardee pour soi (Wallet).
    // Aussi : main du joueur local (objet tenu, PickUp), pour dire qui a mis un objet quelque part (CausedHere).
    public static class Home
    {
        const string Mod = "maison";
        const int K_Fuse = 0, K_Status = 1, K_Brew = 2, K_Lid = 3;
        public const int K_FleaPlaced = 6, K_FleaCredit = 7;   // marche aux puces (Wallet)
        const int HK_Blow = 0, HK_Ingredient = 1, HK_Lid = 2, HK_Freeze = 3, HK_Player = 4, HK_LetsSee = 5;

        class Hook : ModHook
        {
            public override string Module { get { return Mod; } }
            public int Kind; public object Target; public FsmState St; public PlayMakerFSM F;
            public override void OnEnter()
            {
                try { if (Session.Active) OnHook(this); } catch (System.Exception e) { Replay.HookError(e); }
                Finish();
            }
        }

        static float startAt = -1, nextHookCheck, nextPoll, nextFull, nextStatus, nextRescan, nextBrew;
        static bool resolved, leftHooked;

        public static void OnLevelLoaded()
        {
            Unmute();
            holders.Clear(); holderOf.Clear(); statuses.Clear(); heldAt.Clear();
            hand = null; resolved = false; applyingHolder = null;
            bucket = lid = null; brew = lidRemoval = ingredients = jokke = null;
            lidHooked = ingHooked = jokkeHooked = false;
            brewAuth = 0; brewSig = ""; lastBrewSent = -100; brewDirty = false; jokkeHere = true; jokkeCut = false;
            brewApplied = brewSent = 0;
            testStep = testStep2 = 0; testLog = 0; testDescribed = false;
            // Avant le releve du monde (16 s) : les porte-fusibles, le seau et Jokke sont a ce module.
            startAt = PlayerSync.InGame ? Time.realtimeSinceStartup + 8f : -1;
            nextRescan = Time.realtimeSinceStartup + 40f;
            if (!leftHooked) { leftHooked = true; Session.PlayerLeft += OnPlayerLeft; }
        }

        static void OnPlayerLeft(PlayerInfo p)
        {
            if (p == null || p.Id != brewAuth) return;
            brewAuth = 0;
            Log.Info("kilju : " + p.Name + " parti, le seau suit de nouveau l'hote");
        }

        public static void Update()
        {
            Unmute();   // actions coupees pour un seul passage (etat deja entre)
            if (!Session.Active || startAt < 0) return;
            float now = Time.realtimeSinceStartup;
            if (now < startAt) return;
            if (!resolved) { resolved = true; Resolve(); }
            TrackHand(now);
            if (now >= nextHookCheck) { nextHookCheck = now + 1f; HookAll(); }
            if (now >= nextPoll) { nextPoll = now + 0.2f; PollFuses(now); }
            if (Session.IsHost && now >= nextFull) { nextFull = now + 10f; if (Session.RemoteCount > 0 && holders.Count > 0) SendFuses(holders, true); }
            if (now >= nextStatus) { nextStatus = now + 1f; StatusStep(now); }
            if (now >= nextBrew) { nextBrew = now + 0.25f; BrewStep(now); }
            if (jokkeCut)
            {
                jokkeCut = false;
                if (jokke != null && jokke.ActiveStateName == "Lets see")
                {
                    Replay.Depth++;
                    try { Game.SetState(jokke, "Wait"); } finally { Replay.Depth--; }
                    Log.Info("kilju : Jokke revient a 'Wait' ici (le verdict et la paie sont chez celui qui a pose la canette)");
                }
            }
            if (now >= nextRescan) { nextRescan = now + 30f; ScanHolders(); }   // porte-fusible retire puis remis : meme automate
        }

        static void Resolve()
        {
            ScanHolders();
            ScanStatus();
            ResolveKilju();
            Log.Info("maison : " + holders.Count + " porte-fusibles, " + statuses.Count + " compteurs d'electricite, seau " + (brew != null ? "ok" : "absent")
                     + ", couvercle " + (lidRemoval != null ? "ok" : "absent") + ", ingredients " + (ingredients != null ? "ok" : "absent") + ", Jokke " + (jokke != null ? "ok" : "absent"));
        }

        // ================================================================ outils
        static readonly List<FsmStateAction> mutedOnce = new List<FsmStateAction>();

        static void Unmute()
        {
            if (mutedOnce.Count == 0) return;
            foreach (FsmStateAction a in mutedOnce) if (a != null) a.Enabled = true;
            mutedOnce.Clear();
        }

        // Coupe, pour ce passage seulement, les actions de l'etat qui suivent le crochet (celles que 'mute' designe ;
        // null : toutes) : PlayMaker saute une action eteinte (finie d'office). Remises a la mise a jour suivante.
        static int MuteAfter(Hook h, System.Predicate<FsmStateAction> mute)
        {
            int n = 0;
            bool after = false;
            foreach (FsmStateAction a in h.St.Actions)
            {
                if (a == h) { after = true; continue; }
                if (!after || a == null || !a.Enabled || (mute != null && !mute(a))) continue;
                a.Enabled = false;
                mutedOnce.Add(a);
                n++;
            }
            return n;
        }

        static bool Ready(PlayMakerFSM f)
        {
            if (f == null || !f.gameObject.activeInHierarchy) return false;
            try { foreach (FsmState s in f.Fsm.States) if (!s.IsInitialized) return false; }
            catch { return false; }
            return true;
        }

        static Hook Inject(PlayMakerFSM f, string state, int kind, object target)
        {
            FsmState st = f.Fsm.GetState(state);
            if (st == null) { Log.Warn("maison : etat '" + state + "' absent de " + Recon.Path(f.transform) + "::" + f.FsmName); return null; }
            foreach (FsmStateAction a in st.Actions) { var h0 = a as Hook; if (h0 != null && h0.Kind == kind) return h0; }
            var h = new Hook { Kind = kind, Target = target, St = st, F = f };
            var list = new List<FsmStateAction>(st.Actions);
            list.Insert(0, h);
            st.Actions = list.ToArray();
            return h;
        }

        static void Own(PlayMakerFSM f)
        {
            if (f != null && !Replay.Claim(f, Mod)) Log.Warn("maison : " + Recon.Path(f.transform) + "::" + f.FsmName + " deja pris par " + Replay.Owner(f));
        }

        static GameObject GoVar(PlayMakerFSM f, string name)
        {
            FsmGameObject v = f != null ? f.FsmVariables.FindFsmGameObject(name) : null;
            return v != null ? v.Value : null;
        }

        static void HookAll()
        {
            foreach (Holder h in holders) if (!h.Hooked && Ready(h.F)) HookHolder(h);
            if (!lidHooked && Ready(lidRemoval) && Replay.Owner(lidRemoval) == Mod) { lidHooked = true; Inject(lidRemoval, "Remove part", HK_Lid, null); Log.Info("kilju : couvercle du seau suivi"); }
            if (!ingHooked && Ready(ingredients) && Replay.Owner(ingredients) == Mod)
            {
                ingHooked = true;
                Inject(ingredients, "Sugar", HK_Ingredient, "sucre");
                Inject(ingredients, "Yeast", HK_Ingredient, "levure");
                Log.Info("kilju : ingredients du seau suivis");
            }
            if (!jokkeHooked && Ready(jokke) && Replay.Owner(jokke) == Mod)
            {
                jokkeHooked = true;
                Inject(jokke, "Freeze Can", HK_Freeze, null);
                Inject(jokke, "Player?", HK_Player, null);
                Inject(jokke, "Lets see", HK_LetsSee, null);
                Log.Info("kilju : Jokke (CanTrigger) suivi");
            }
        }

        static void OnHook(Hook h)
        {
            switch (h.Kind)
            {
                case HK_Blow: OnBlow(h); break;
                case HK_Ingredient: OnIngredient(h); break;
                case HK_Lid: if (Replay.Depth == 0) OnLocalLid(); break;
                case HK_Freeze:
                    {
                        if (Replay.Depth > 0) break;
                        GameObject can = GoVar(jokke, "Object");
                        jokkeHere = CausedHere(can, 120f);
                        Log.Info("kilju : Jokke prend " + (can != null ? can.name : "?") + (jokkeHere ? ", posee par ce joueur-ci : gout, verdict et paie ici" : ", posee par un autre joueur : verdict et paie chez lui"));
                        break;
                    }
                case HK_Player:
                    if (!jokkeHere && Replay.Depth == 0) MuteAfter(h, null);   // tout fini d'office : FINISHED -> 'Lets see'
                    break;
                case HK_LetsSee:
                    if (!jokkeHere && Replay.Depth == 0)
                    {
                        MuteAfter(h, a => a.GetType().Name != "Wait");   // replique et sous-titre coupes, l'attente reste
                        jokkeCut = true;
                    }
                    break;
            }
        }

        // ================================================================ main du joueur, qui a mis quoi
        static PlayMakerFSM hand;
        static float nextHand;
        static readonly Dictionary<GameObject, float> heldAt = new Dictionary<GameObject, float>();

        static void TrackHand(float now)
        {
            if (hand == null)
            {
                if (now < nextHand) return;
                nextHand = now + 2f;
                GameObject h = Game.PlayerPart("Pivot/AnimPivot/Camera/FPSCamera/1Hand_Assemble/Hand");
                hand = h != null ? Game.FsmOn(h, "PickUp") : null;
                if (hand == null) return;
            }
            FsmGameObject p = hand.FsmVariables.FindFsmGameObject("PickedObject");
            GameObject go = p != null ? p.Value : null;
            if (go != null) heldAt[go] = now;
        }

        // Secondes depuis que le joueur local a tenu 'go' (tres grand : jamais).
        public static float HeldAgo(GameObject go)
        {
            float t;
            return go != null && heldAt.TryGetValue(go, out t) ? Time.realtimeSinceStartup - t : float.MaxValue;
        }

        // Essais : comme si le joueur local venait de tenir 'go'.
        public static void MarkHeld(GameObject go) { if (go != null) heldAt[go] = Time.realtimeSinceStartup; }

        // 'go' a-t-il ete mis la par le joueur local ? Deplace en dernier par un autre (sa copie suit ses messages) : non ;
        // tenu ici depuis moins de 'window' s : oui ; sinon (objet au repos, personne) : seulement chez l'hote.
        public static bool CausedHere(GameObject go, float window)
        {
            if (go == null) return Session.IsHost;
            float age;
            if (Props.MovedByOther(go, out age)) return false;
            if (HeldAgo(go) < window) return true;
            return Session.IsHost;
        }

        // ================================================================ fusibles
        class Holder
        {
            public string Key; public PlayMakerFSM F; public FsmInt State; public FsmFloat Tight;
            public int Last, None = 0, Blown = 2; public bool Hooked; public float LocalAt = -100;
        }
        static readonly List<Holder> holders = new List<Holder>();
        static readonly Dictionary<string, Holder> holderOf = new Dictionary<string, Holder>();
        static Holder applyingHolder;

        static void ScanHolders()
        {
            int added = 0;
            foreach (Object o in Game.AllFsms())
            {
                var f = (PlayMakerFSM)o; if (f == null) continue;
                if (f.hideFlags != HideFlags.None || f.FsmName != "Use" || !f.gameObject.name.StartsWith("fuse holder")) continue;
                if (!f.transform.root.gameObject.activeInHierarchy) continue;   // modeles (prefabs)
                bool known = false;
                foreach (Holder x in holders) if (x.F == f) { known = true; break; }
                if (known || !Game.LocalVar(f, "FuseState")) continue;
                Own(f);
                FsmString id = f.FsmVariables.FindFsmString("ID");
                string key = id != null && !string.IsNullOrEmpty(id.Value) ? id.Value : Recon.Path(f.transform);
                if (holderOf.ContainsKey(key)) key = key + "|" + Recon.Path(f.transform);
                var h = new Holder { Key = key, F = f, State = f.FsmVariables.FindFsmInt("FuseState"), Tight = f.FsmVariables.FindFsmFloat("Tightness") };
                h.Last = h.State.Value;
                holders.Add(h); holderOf[key] = h;
                added++;
            }
            if (added > 0) Log.Info("maison : " + added + " porte-fusibles de plus suivis (" + holders.Count + ")");
        }

        static void HookHolder(Holder h)
        {
            h.Hooked = true;
            h.None = IntSet(h.F, "State 5", 0);    // fusible retire
            h.Blown = IntSet(h.F, "State 6", 2);   // grille
            if (Replay.Owner(h.F) == Mod) Inject(h.F, "State 6", HK_Blow, h);
            Log.Info("fusible " + h.Key + " suivi : " + Name(h, h.State.Value) + " (absent=" + h.None + ", grille=" + h.Blown + ")");
        }

        // Valeur ecrite par le 1er SetIntValue de l'etat (FuseState du fusible retire, grille).
        static int IntSet(PlayMakerFSM f, string state, int def)
        {
            try
            {
                FsmState st = f.Fsm.GetState(state);
                if (st == null) return def;
                foreach (FsmStateAction a in st.Actions)
                {
                    if (a == null || a.GetType().Name != "SetIntValue") continue;
                    FieldInfo fi = a.GetType().GetField("intValue", BindingFlags.Public | BindingFlags.Instance);
                    var v = fi != null ? fi.GetValue(a) as FsmInt : null;
                    if (v != null) return v.Value;
                }
            }
            catch { }
            return def;
        }

        static string Name(Holder h, int v) { return v == h.None ? "absent" : v == h.Blown ? "grille" : "bon(" + v + ")"; }

        // BLOWFUSE ('State 6') : chez l'hote, passe (le releve l'enverra) ; chez un invite, bloque s'il vient de sa logique.
        static void OnBlow(Hook hk)
        {
            var h = (Holder)hk.Target;
            if (Replay.Depth > 0) return;   // fusible grille chez l'hote, refait ici
            if (Session.IsHost) { Log.Info("fusible " + h.Key + " grille chez l'hote (surcharge, orage)"); return; }
            int n = MuteAfter(hk, a => a.GetType().Name == "SetIntValue");
            Log.Info("fusible " + h.Key + " : grille par la logique de cet invite, bloque (" + n + " action coupee ; l'hote decide)");
        }

        static void PollFuses(float now)
        {
            List<Holder> changed = null;
            foreach (Holder h in holders)
            {
                if (h.F == null) continue;
                int cur = h.State.Value;
                if (cur == h.Last) continue;
                int was = h.Last;
                h.Last = cur;
                if (applyingHolder == h) continue;
                if (!Session.IsHost && cur == h.Blown)
                {
                    // Grille ici sans passer par 'State 6' accroche (automate pas encore pret) : remis comme chez l'hote.
                    Apply(h, was, "grille par la logique de cet invite, remis");
                    continue;
                }
                if (!Session.IsHost) h.LocalAt = now;
                if (changed == null) changed = new List<Holder>();
                changed.Add(h);
                Log.Info("fusible " + h.Key + " : " + Name(h, was) + " -> " + Name(h, cur) + " ici (etat " + h.F.ActiveStateName + "), envoye");
            }
            if (changed != null) SendFuses(changed, false);
        }

        // Home : U8 joueur, U8 K_Fuse, Bool complet, U8 n, (Str porte-fusible, I32 FuseState, F32 Tightness) x n.
        static void SendFuses(List<Holder> list, bool full)
        {
            if (Session.IsHost && Session.RemoteCount == 0) return;
            int n = Mathf.Min(list.Count, 40);
            var w = new NetWriter(Msg.Home).U8(Session.LocalId).U8(K_Fuse).Bool(full).U8(n);
            for (int i = 0; i < n; i++)
            {
                Holder h = list[i];
                w.Str(h.Key).I32(h.State.Value).F32(h.Tight != null ? h.Tight.Value : 0f);
            }
            Session.SendAll(w, true);
        }

        static void OnFuses(int who, NetReader r)
        {
            bool full = r.Bool();
            int n = r.U8();
            float now = Time.realtimeSinceStartup;
            for (int i = 0; i < n; i++)
            {
                string key = r.Str();
                int st = r.I32();
                float tight = r.F32();
                Holder h;
                if (!holderOf.TryGetValue(key, out h) || h.F == null) { if (!full) Log.Warn("fusible " + key + " introuvable ici"); continue; }
                if (h.State.Value == st) { if (h.Tight != null) h.Tight.Value = tight; continue; }
                if (full && now - h.LocalAt < 4f) continue;   // notre propre changement est en route vers l'hote
                if (h.Tight != null) h.Tight.Value = tight;
                Apply(h, st, full ? "etat de l'hote" : "change par #" + who);
            }
        }

        // Met le porte-fusible dans l'etat 'target' par les evenements du jeu (visuels et base refaits par l'automate).
        static void Apply(Holder h, int target, string why)
        {
            PlayMakerFSM f = h.F;
            int was = h.State.Value;
            applyingHolder = h;
            Replay.Depth++;
            try
            {
                if (target == h.Blown) f.SendEvent("BLOWFUSE");            // 'Set data' -> 'State 6' -> 'Blown fuse'
                else if (target == h.None) f.SendEvent("UNINSTALL");       // global -> 'State 4' -> 'State 5' (retire)
                else
                {
                    h.State.Value = target;
                    if (was == h.None)
                    {
                        // = FuseTrigger 'Assembly' / 'Assemble' : fusible pose (maillage allume, declencheur eteint), INSTALL.
                        GameObject mesh = GoVar(f, "MeshFuse"), trig = GoVar(f, "FuseTrigger");
                        if (mesh != null) mesh.SetActive(true);
                        if (trig != null) trig.SetActive(false);
                        f.SendEvent("INSTALL");
                    }
                    else Game.SetState(f, "Set data");
                }
                if (h.State.Value != target) { h.State.Value = target; Game.SetState(f, "Set data"); }
            }
            catch (System.Exception e) { Log.Warn("fusible " + h.Key + " : " + e.Message); }
            finally { Replay.Depth--; applyingHolder = null; h.Last = h.State.Value; }
            Log.Info("fusible " + h.Key + " : " + Name(h, was) + " -> " + Name(h, h.State.Value) + " (" + why + ", etat " + f.ActiveStateName + ")");
        }

        // ---------------------------------------------------------------- coupure (HouseElectricity 'Status')
        class Status { public string Key; public PlayMakerFSM F; public string Host, Sent; public float DiffSince = -1, FixedAt = -100; }
        static readonly List<Status> statuses = new List<Status>();

        static void ScanStatus()
        {
            foreach (Object o in Game.AllFsms())
            {
                var f = (PlayMakerFSM)o; if (f == null) continue;
                if (f.hideFlags != HideFlags.None || f.FsmName != "Status" || f.gameObject.name != "HouseElectricity") continue;
                if (!f.transform.root.gameObject.activeInHierarchy) continue;
                statuses.Add(new Status { Key = Recon.Path(f.transform), F = f });
            }
        }

        static bool Off(string s) { return s == "Lights off 2" || s == "State 10"; }

        // Home : U8 joueur, U8 K_Status, U8 n, (Str compteur, Str etat) x n (hote -> invites, quand un etat change).
        static void StatusStep(float now)
        {
            if (Session.IsHost)
            {
                bool changed = false;
                foreach (Status s in statuses) if (s.F != null && s.F.ActiveStateName != s.Sent) changed = true;
                if (!changed || Session.RemoteCount == 0) return;
                var w = new NetWriter(Msg.Home).U8(Session.LocalId).U8(K_Status).U8(statuses.Count);
                foreach (Status s in statuses) { s.Sent = s.F != null ? s.F.ActiveStateName : ""; w.Str(s.Key).Str(s.Sent ?? ""); }
                Session.SendAll(w, true);
                return;
            }
            foreach (Status s in statuses)
            {
                if (s.F == null || s.Host == null || !s.F.gameObject.activeInHierarchy) continue;
                string local = s.F.ActiveStateName;
                if (Off(s.Host) == Off(local)) { s.DiffSince = -1; continue; }
                if (s.DiffSince < 0) { s.DiffSince = now; continue; }
                if (now - s.DiffSince < 3f || now - s.FixedAt < 30f) continue;
                s.DiffSince = -1; s.FixedAt = now;
                Replay.Depth++;
                try { Game.SetState(s.F, Off(s.Host) ? "Lights off 2" : "Reset mat"); } finally { Replay.Depth--; }
                Log.Info("maison : electricite " + s.Key + " recalee sur l'hote (" + s.Host + ") : " + local + " -> " + s.F.ActiveStateName);
            }
        }

        static void OnStatus(NetReader r)
        {
            int n = r.U8();
            for (int i = 0; i < n; i++)
            {
                string key = r.Str(), st = r.Str();
                foreach (Status s in statuses) if (s.Key == key) s.Host = st;
            }
        }

        // ================================================================ kilju
        static GameObject bucket, lid;
        static PlayMakerFSM brew, lidRemoval, ingredients, jokke;
        static bool lidHooked, ingHooked, jokkeHooked, brewDirty, jokkeHere = true, jokkeCut;
        static int brewAuth, brewApplied, brewSent;   // brewAuth : joueur qui fait autorite sur le seau
        static float lastBrewSent;
        static string brewSig = "";
        const string BucketPath = "EQUIPMENTS/bucket(itemx)", JokkePath = "JOBS/JOKKEHOME/HouseDrunkNew/KiljuBuyer/CanTrigger";

        static void ResolveKilju()
        {
            bucket = Game.FindAny(BucketPath);
            brew = bucket != null ? Game.FsmOn(bucket, "Use") : null;
            lid = GoVar(brew, "Lid") ?? Game.FindAny("EQUIPMENTS/bucket lid(itemx)");
            lidRemoval = lid != null ? Game.FsmOn(lid, "Removal") : null;
            Transform ti = bucket != null ? bucket.transform.Find("TriggerIngredients") : null;
            ingredients = ti != null ? Game.FsmOn(ti.gameObject, "Level") : null;
            GameObject jk = Game.FindAny(JokkePath);
            jokke = jk != null ? Game.FsmOn(jk, "Logic") : null;
            Own(brew); Own(lidRemoval); Own(ingredients); Own(jokke);
        }

        // Variables du seau envoyees : ses nombres et drapeaux, sauf les calculs intermediaires.
        static bool BrewVar(string n) { return !n.StartsWith("Math") && !n.StartsWith("UniqueTag"); }

        static string BrewSignature()
        {
            var sb = new System.Text.StringBuilder();
            foreach (FsmFloat x in brew.FsmVariables.FloatVariables) if (BrewVar(x.Name)) sb.Append(x.Value.ToString("F3")).Append('|');
            foreach (FsmBool x in brew.FsmVariables.BoolVariables) if (BrewVar(x.Name)) sb.Append(x.Value ? '1' : '0');
            return sb.ToString();
        }

        static void ClaimBrew(string why)
        {
            brewDirty = true;
            if (brewAuth == Session.LocalId) return;
            brewAuth = Session.LocalId;
            Log.Info("kilju : le seau suit ce joueur-ci (" + why + ")");
        }

        // Home : U8 joueur, U8 K_Brew, U8 n, (Str variable, F32 valeur) x n, U8 m, (Str drapeau, Bool) x m.
        static void BrewStep(float now)
        {
            if (brew == null) return;
            if (HeldAgo(bucket) < 0.5f) ClaimBrew("seau tenu");
            else if (HeldAgo(lid) < 0.5f) ClaimBrew("couvercle tenu");
            if (brewAuth != Session.LocalId) return;
            if (Session.IsHost && Session.RemoteCount == 0) return;
            string sig = BrewSignature();
            bool changed = sig != brewSig;
            if (!(changed && (brewDirty || now - lastBrewSent > 3f)) && now - lastBrewSent < 5f) return;
            brewSig = sig; brewDirty = false; lastBrewSent = now;
            var floats = new List<FsmFloat>(); foreach (FsmFloat x in brew.FsmVariables.FloatVariables) if (BrewVar(x.Name)) floats.Add(x);
            var bools = new List<FsmBool>(); foreach (FsmBool x in brew.FsmVariables.BoolVariables) if (BrewVar(x.Name)) bools.Add(x);
            var w = new NetWriter(Msg.Home).U8(Session.LocalId).U8(K_Brew).U8(floats.Count);
            foreach (FsmFloat x in floats) w.Str(x.Name).F32(x.Value);
            w.U8(bools.Count);
            foreach (FsmBool x in bools) w.Str(x.Name).Bool(x.Value);
            Session.SendAll(w, true);
            if (++brewSent <= 5 || brewSent % 50 == 0) Log.Info("kilju : seau envoye (" + BrewState() + ")");
        }

        static readonly string[] Ingredients = { "Sugar", "Yeast", "Water", "Alcohol", "Vinegar" };

        static void OnBrew(int who, NetReader r)
        {
            if (who == Session.LocalId) return;
            var floats = new List<KeyValuePair<string, float>>();
            var bools = new List<KeyValuePair<string, bool>>();
            for (int i = 0, n = r.U8(); i < n; i++) floats.Add(new KeyValuePair<string, float>(r.Str(), r.F32()));
            for (int i = 0, n = r.U8(); i < n; i++) bools.Add(new KeyValuePair<string, bool>(r.Str(), r.Bool()));
            if (brewAuth != who) Log.Info("kilju : le seau suit #" + who);
            brewAuth = who;
            if (brew == null) return;
            bool significant = false;
            Replay.Depth++;
            try
            {
                foreach (var x in floats)
                {
                    FsmFloat v = brew.FsmVariables.FindFsmFloat(x.Key);
                    if (v == null) continue;
                    if (System.Array.IndexOf(Ingredients, x.Key) >= 0 && Mathf.Abs(v.Value - x.Value) > 0.01f) significant = true;
                    v.Value = x.Value;
                }
                foreach (var x in bools)
                {
                    FsmBool v = brew.FsmVariables.FindFsmBool(x.Key);
                    if (v == null) continue;
                    if (v.Value != x.Value) significant = true;
                    v.Value = x.Value;
                }
                // Surface du sucre, levure, bulles, fini : refaits par la logique du seau depuis 'State 3' (comme au chargement).
                if (significant && brew.gameObject.activeInHierarchy && brew.Fsm.GetState("State 3") != null) Game.SetState(brew, "State 3");
            }
            finally { Replay.Depth--; }
            brewSig = BrewSignature();
            if (significant || ++brewApplied <= 3) Log.Info("kilju : seau de #" + who + " recopie (" + BrewState() + ")" + (significant ? ", affichage recale" : ""));
        }

        static string BrewState()
        {
            if (brew == null) return "seau absent";
            var sb = new System.Text.StringBuilder("autorite #").Append(brewAuth);
            foreach (string n in new[] { "Sugar", "Yeast", "Water", "Alcohol", "Vinegar", "Sweetness", "Time" })
            {
                FsmFloat v = brew.FsmVariables.FindFsmFloat(n);
                if (v != null) sb.Append(", ").Append(n).Append(' ').Append(v.Value.ToString("0.###"));
            }
            FsmBool lidOn = brew.FsmVariables.FindFsmBool("LidOn"), done = brew.FsmVariables.FindFsmBool("KiljuFinished");
            sb.Append(", couvercle ").Append(lidOn != null && lidOn.Value ? "mis" : "retire").Append(done != null && done.Value ? ", fini" : "").Append(", etat ").Append(brew.ActiveStateName);
            return sb.ToString();
        }

        // Sucre ou levure qui tombe dans le seau : compte seulement chez celui qui l'a jete.
        static void OnIngredient(Hook hk)
        {
            if (Replay.Depth > 0) return;
            GameObject obj = GoVar(hk.F, "Object");
            string what = hk.Target as string;
            if (CausedHere(obj, 60f)) { ClaimBrew(what + " jete"); Log.Info("kilju : " + what + " (" + (obj != null ? obj.name : "?") + ") ajoute au seau ici"); return; }
            int n = MuteAfter(hk, a => a.GetType().Name == "AddFsmFloat");
            Log.Info("kilju : " + what + " (" + (obj != null ? obj.name : "?") + ") jete par un autre joueur : pas ajoute ici (" + n + " action coupee, le seau vient de lui)");
        }

        // Home : U8 joueur, U8 K_Lid (couvercle du seau retire).
        static void OnLocalLid()
        {
            ClaimBrew("couvercle retire");
            Session.SendAll(new NetWriter(Msg.Home).U8(Session.LocalId).U8(K_Lid), true);
            Log.Info("kilju : couvercle retire ici, envoye");
        }

        static void OnLid(int who)
        {
            FsmBool on = brew != null ? brew.FsmVariables.FindFsmBool("LidOn") : null;
            if (lidRemoval == null || on == null) { Log.Warn("kilju : couvercle retire par #" + who + ", seau ou couvercle absent ici"); return; }
            if (!on.Value) { Log.Info("kilju : couvercle retire par #" + who + ", deja retire ici"); return; }
            if (!Ready(lidRemoval)) { Log.Warn("kilju : couvercle retire par #" + who + ", automate du couvercle pas pret ici"); return; }
            Replay.Depth++;
            try { Game.SetState(lidRemoval, "Remove part"); } finally { Replay.Depth--; }
            Props.SoonScan();   // il a de nouveau un corps : suivi tout de suite
            Log.Info("kilju : couvercle retire par #" + who + ", refait ici (couvercle " + (on.Value ? "toujours mis" : "retire") + ")");
        }

        // ================================================================ reseau
        public static void OnMessage(Peer from, NetReader r)
        {
            byte[] raw = Session.IsHost ? r.Rest() : null;
            int who = r.U8();
            if (Session.IsHost) who = from.Id;
            int kind = r.U8();
            // Relais aux autres invites (pas les annonces destinees a l'hote seul).
            if (Session.IsHost && kind != K_FleaPlaced) Session.Broadcast(new NetWriter(Msg.Home).U8(who).Raw(raw, 1, raw.Length - 1), true, who);
            if (!PlayerSync.InGame) return;
            if (!resolved && startAt > 0) { resolved = true; Resolve(); }
            switch (kind)
            {
                case K_Fuse: OnFuses(who, r); break;
                case K_Status: if (!Session.IsHost) OnStatus(r); break;
                case K_Brew: OnBrew(who, r); break;
                case K_Lid: OnLid(who); break;
                case K_FleaPlaced:
                case K_FleaCredit: Wallet.OnFlea(who, kind, r); break;
            }
        }

        // ================================================================ essais
        // [Test] Autotest=fusible : les deux joueurs devant le tableau de la maison des parents (TestPos=-11,0.5,8 ;
        //   [Test] TestMaison=appartement : celui de l'appartement, TestPos=-1283,1.5,1080). Fusibles A = rang
        //   [Test] TestFusible (0) et B = rang suivant au tableau. 25 s : etat de depart (et actions de 'State 5', 'State 6',
        //   'Set data'). 30 s : l'hote fait griller A (BLOWFUSE, comme une surcharge) ; 34 s : la logique de l'invite fait
        //   griller B (bloque chez lui) ; 40 s : l'invite retire A (UNINSTALL) ; 46 s : l'invite pose un fusible neuf dans A
        //   (FuseTrigger 'Assemble'). Chacun note toutes les 3 s de 25 a 61 s « autotest : fusible, A=... B=... ; coupure ».
        //   Attendu : A grille (32 s) puis absent (42 s) puis bon (48 s) des deux cotes ; B toujours bon des deux cotes.
        // [Test] Autotest=kilju : les deux devant le seau de l'appartement (TestPos=-1284.5,1,1078.5). 28 s : l'invite tient
        //   le seau ; 30 s : il y met sucre +2, levure +0.5, eau +10 ; 33 s : chez l'hote seul, sucre +1 (la copie d'un
        //   sachet qui tomberait chez lui) ; 40 s : l'invite retire le couvercle s'il est mis. Chacun note toutes les 3 s de
        //   25 a 61 s « autotest : kilju, autorite #.., Sugar.. ». Attendu : autorite #invite des deux cotes des 30 s, memes
        //   Sugar/Yeast/Water (le +1 de l'hote efface au plus tard 5 s apres), couvercle retire des deux cotes apres 40 s.
        static int testStep, testStep2;
        static float testLog;
        static bool testDescribed;

        public static void Test(string mode, float t)
        {
            if (!resolved) return;
            if (mode == "fusible") TestFuse(t);
            else if (mode == "kilju") TestKilju(t);
        }

        static string SlotName(Holder h) { GameObject s = GoVar(h.F, "Slot"); return s != null ? s.name : h.Key; }

        static List<Holder> TestTable()
        {
            bool flat = Config.Get("Test", "TestMaison", "parents") == "appartement";
            var r = new List<Holder>();
            foreach (Holder h in holders)
            {
                GameObject db = GoVar(h.F, "Database");
                if (db != null && db.name == (flat ? "ElecDatabaseApartment" : "ElecDatabaseHouse")) r.Add(h);
            }
            r.Sort((a, b) => string.CompareOrdinal(SlotName(a), SlotName(b)));
            return r;
        }

        static string Describe(Holder h)
        {
            if (h == null) return "?";
            return h.Key + ":" + Name(h, h.State.Value) + "[" + h.F.ActiveStateName + "]";
        }

        static string FuseState(Holder a, Holder b)
        {
            var sb = new System.Text.StringBuilder("A=").Append(Describe(a)).Append(" B=").Append(Describe(b)).Append(" ; coupure");
            foreach (Status s in statuses) sb.Append(' ').Append(s.F != null ? s.F.transform.root.name + ":" + s.F.ActiveStateName : "?");
            return sb.ToString();
        }

        static string Event(Holder h, string ev)
        {
            if (h == null) return "fusible absent";
            string before = h.F.ActiveStateName;
            h.F.SendEvent(ev);
            return h.Key + " : " + before + " -" + ev + "-> " + h.F.ActiveStateName + ", " + Name(h, h.State.Value);
        }

        static void TestFuse(float t)
        {
            List<Holder> table = TestTable();
            int k = Config.GetInt("Test", "TestFusible", 0);
            Holder a = k < table.Count ? table[k] : null, b = k + 1 < table.Count ? table[k + 1] : null;
            if (t > 25f && !testDescribed)
            {
                testDescribed = true;
                var sb = new System.Text.StringBuilder("autotest : fusible, " + table.Count + " porte-fusibles au tableau, debut : " + FuseState(a, b));
                if (a != null && Ready(a.F))
                    foreach (string st in new[] { "State 5", "State 6", "Set data" })
                    {
                        FsmState s = a.F.Fsm.GetState(st);
                        if (s == null) continue;
                        sb.Append("\n  ").Append(st).Append(" :");
                        foreach (FsmStateAction x in s.Actions) if (x != null && !(x is ModHook)) sb.Append(' ').Append(Recon.Describe(x));
                    }
                Log.Info(sb.ToString());
            }
            if (Session.IsHost && testStep == 0 && t > 30f) { testStep = 1; Log.Info("autotest : fusible, l'hote fait griller A : " + Event(a, "BLOWFUSE")); }
            if (!Session.IsHost && testStep == 0 && t > 34f) { testStep = 1; Log.Info("autotest : fusible, la logique de l'invite fait griller B : " + Event(b, "BLOWFUSE")); }
            if (!Session.IsHost && testStep == 1 && t > 40f) { testStep = 2; Log.Info("autotest : fusible, l'invite retire A : " + Event(a, "UNINSTALL")); }
            if (!Session.IsHost && testStep == 2 && t > 46f && a != null)
            {
                testStep = 3;
                GameObject trig = GoVar(a.F, "FuseTrigger");
                PlayMakerFSM asm = trig != null ? Game.FsmOn(trig, "Assembly") : null;
                string before = Describe(a);
                if (asm != null && trig.activeInHierarchy) Game.SetState(asm, "Assemble");
                Log.Info("autotest : fusible, l'invite pose un fusible dans A (" + (asm == null ? "declencheur absent" : trig.activeInHierarchy ? "FuseTrigger 'Assemble'" : "declencheur eteint")
                         + ") : " + before + " -> " + Describe(a));
            }
            if (t > 25f && t < 61f && t - testLog >= 3f) { testLog = t; Log.Info("autotest : fusible, " + FuseState(a, b)); }
        }

        static void AddBrew(string name, float d)
        {
            FsmFloat v = brew != null ? brew.FsmVariables.FindFsmFloat(name) : null;
            if (v != null) v.Value += d;
        }

        static void TestKilju(float t)
        {
            if (brew == null) { if (testStep == 0 && t > 25f) { testStep = 9; Log.Info("autotest : kilju, seau absent"); } return; }
            if (!Session.IsHost && testStep == 0 && t > 28f) { testStep = 1; MarkHeld(bucket); Log.Info("autotest : kilju, l'invite tient le seau"); }
            if (!Session.IsHost && testStep == 1 && t > 30f)
            {
                testStep = 2;
                AddBrew("Sugar", 2f); AddBrew("Yeast", 0.5f); AddBrew("Water", 10f);
                brewDirty = true;
                Log.Info("autotest : kilju, l'invite met sucre +2, levure +0.5, eau +10 : " + BrewState());
            }
            if (Session.IsHost && testStep == 0 && t > 33f) { testStep = 1; AddBrew("Sugar", 1f); Log.Info("autotest : kilju, sucre +1 chez l'hote seul (copie d'un sachet) : " + BrewState()); }
            if (!Session.IsHost && testStep2 == 0 && t > 40f)
            {
                testStep2 = 1;
                FsmBool on = brew.FsmVariables.FindFsmBool("LidOn");
                if (on == null || !on.Value || !Ready(lidRemoval)) Log.Info("autotest : kilju, couvercle pas sur le seau : essai du couvercle saute");
                else { Game.SetState(lidRemoval, "Remove part"); Log.Info("autotest : kilju, l'invite retire le couvercle : " + BrewState()); }
            }
            if (t > 25f && t < 61f && t - testLog >= 3f)
            {
                testLog = t;
                Log.Info("autotest : kilju, " + BrewState() + (jokke != null ? " ; Jokke " + (jokke.gameObject.activeInHierarchy ? jokke.ActiveStateName : "eteint") : ""));
            }
        }
    }
}
