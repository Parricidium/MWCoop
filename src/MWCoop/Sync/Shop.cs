using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HutongGames.PlayMaker;
using MWCoop.Net;
using UnityEngine;
using CreateObject = HutongGames.PlayMaker.Actions.CreateObject;

namespace MWCoop
{
    // Achats (demande de JD) : chacun paie de son cote, mais tous voient ce que les autres achetent.
    //
    // 1) CAISSES A SAC (magasin PSK, Fleetari) : automate 'Data' de CashRegisterLogic. Au paiement : 'Purchase'
    //    (retire PriceTotal de PlayerMoney) et 'Spawn bag' : le sac est cree par Spawner/CreateBag... et rempli
    //    d'apres la table 'Carried' de l'inventaire (produit -> quantite), puis les gros articles a part
    //    ('Separates') ; chez Fleetari ensuite le temps de banc d'essai achete (QDynoTimes, 'Spawn 16') et
    //    seulement alors 'Purchase' ; enfin 'Reset purchase' vide le panier.
    //     - une action injectee au debut de 'Spawn bag' envoie (caisse, panier, temps de banc) ;
    //     - ailleurs : panier local mis de cote, panier recu pose dans Carried, caisse mise dans 'Spawn bag'
    //       (le sac et les articles apparaissent) ; ce qui ferait payer ou ecrirait a l'ecran de celui qui
    //       rejoue ('Purchase' de Fleetari, statistiques, GUI*) est coupe jusqu'a la fin, puis panier remis ;
    //     - caisse eteinte chez celui qui recoit (Fleetari loin : son automate ne tourne pas, le message etait
    //       perdu et les compteurs d'objets divergeaient) : la chaine est refaite a la main sur les distributeurs
    //       (racine Spawner, toujours active) -- sac rempli, gros articles un par un, temps de banc.
    //    Les objets crees portent un nom a compteur sauvegarde (shoppingbag5...) : rejoues dans le meme ordre,
    //    ils ont le meme nom partout (Props les suit ensuite par ce nom).
    //    Le sac est prepare par le BagCreator de la caisse (automate 'Create', global COPY envoye par 'Spawn
    //    bag' : sac active, panier recopie dedans). Le rejeu de 'Spawn bag' le refait deja : il est reserve a ce
    //    module (Replay.Claim) des le premier releve (9 s, avant celui du monde a 16 s), meme si la caisse n'est
    //    pas encore accrochable (objet eteint) -- sinon le monde rejouait aussi le COPY (sac en double, ou vide).
    //
    // 2) COMPTOIRS que le monde (WorldFsms) ne suit pas ('CashRegister' et 'Buy' ecartes, NPC_CARS aussi) :
    //     - bar du pub (PubCashRegister, etat 'Pay' : Total, EventName) : Teimo sert, cree le verre ou le plat,
    //       ou les cigarettes (Spawner/CreateItems, compteur) ;
    //     - controle technique et station d'epuration (etat 'Purchase') : frais payes, portes ouvertes ;
    //     - marche aux puces (FleaCashRegister 'Purchase' + liste 'Bought' de FleaMarketProducts) : les memes
    //       objets sortent ('Spawn product') ;
    //     - ticket de bus (BUS/Ticket 'Pay trip') : le bus de l'hote repart quand un invite paie ;
    //     - machine a cafe (CoffeeButton 'Purchase', gratuit : POUR au verseur) et sa tasse (coffee cup 'State 1',
    //       tasse bue et remise sous la machine). Le verseur (PanTarget) est reserve a ce module : le monde ne le
    //       rejouait que si le clic avait eu lieu dans la seconde.
    //    Une action au debut de l'etat de paiement envoie (comptoir, etat, variables de l'automate, liste 'Bought')
    //    avec un numero d'achat ; ailleurs les variables sont recopiees et l'automate mis dans cet etat quand il
    //    est au repos (etat qui attend le joueur, ou sans suite) : la suite (service, objets crees) se deroule
    //    comme chez l'acheteur. Ce qui touche au joueur qui rejoue (PlayerMoney, GUI*, statistiques, objets sous
    //    PLAYER) est coupe jusqu'au retour au repos : seul l'acheteur paie, les sous-titres restent chez lui.
    //    Objets crees par le comptoir sans ID (verre de biere, shot, cafe, assiette du pub : rien a sauvegarder) :
    //    un ID est pose a leur creation, le meme partout (nom + joueur + numero d'achat) : Props et Consume les
    //    suivent (bu, mange, deplace) comme les autres articles.
    //    Comptoir eteint chez celui qui recoit : cigarettes du bar, objets des puces et trajet de bus faits a la
    //    main (compteurs, objets persistants, bus de l'hote) ; le reste attend que le comptoir s'allume, ou est
    //    abandonne (verres, cafe).
    //    Pas suivis ici (voir le rapport du lot 2) : restaurant PSK (le repas est cuisine par Keijo/Jouni d'apres
    //    la distance du joueur LOCAL au comptoir), kiosque a saucisses et vendeur de pieces du rallye (leur effet
    //    passe deja par un automate du monde, PURCHASE / ALTERNATOR globaux), bureau de poste (courrier).
    public static class Shop
    {
        const string Mod = "magasin";

        // ================================================================ caisses a sac
        class Register { public string Key; public PlayMakerFSM Fsm; public bool Hooked; }
        class Pending
        {
            public Register R; public float PriceTotal; public int BagStuff; public Hashtable Carried, Live; public float Until;
            public List<KeyValuePair<FsmFloat, float>> Floats = new List<KeyValuePair<FsmFloat, float>>();
            public List<FsmStateAction> Muted;
        }

        static readonly Dictionary<string, Register> registers = new Dictionary<string, Register>();
        static readonly Dictionary<PlayMakerFSM, Register> registerOf = new Dictionary<PlayMakerFSM, Register>();
        static readonly List<Pending> pending = new List<Pending>();
        // Variables de la caisse transmises avec le panier (temps de banc d'essai achete chez Fleetari).
        static readonly string[] BagFloats = { "QDynoTimes" };

        // ================================================================ comptoirs
        class Counter
        {
            public string Key, Kind, Label; public PlayMakerFSM Fsm; public string[] PayStates;
            public bool Hooked; public float Wait;                 // attente max d'un rejeu (comptoir eteint, occupe)
            public int SerialWho, Serial, Made;                    // achat en cours : nom des objets crees
            public List<FsmStateAction> Muted; public float MutedUntil, MutedAt;
        }
        class Replayed
        {
            public Counter C; public string State; public int Who, SerialWho, Serial; public float Until;
            public Vars V; public List<KeyValuePair<string, List<object>>> Lists;
        }
        class Vars
        {
            public List<KeyValuePair<string, float>> F = new List<KeyValuePair<string, float>>();
            public List<KeyValuePair<string, int>> I = new List<KeyValuePair<string, int>>();
            public List<KeyValuePair<string, bool>> B = new List<KeyValuePair<string, bool>>();
            public List<KeyValuePair<string, string>> S = new List<KeyValuePair<string, string>>();
        }
        // Distributeur a rejouer a la main (caisse eteinte) : un objet tous les 0,15 s, comme la caisse.
        class SpawnStep { public PlayMakerFSM Fsm; public GameObject Point; public string Event; }

        static readonly Dictionary<string, Counter> counters = new Dictionary<string, Counter>();
        static readonly Dictionary<PlayMakerFSM, Counter> counterOf = new Dictionary<PlayMakerFSM, Counter>();
        static readonly List<Counter> unhooked = new List<Counter>();              // objets eteints : accroches des qu'ils s'allument
        static readonly List<Register> unhookedRegisters = new List<Register>();
        static readonly List<Counter> mutedCounters = new List<Counter>();
        static readonly List<Replayed> queue = new List<Replayed>();
        static readonly List<SpawnStep> spawnSteps = new List<SpawnStep>();
        static readonly HashSet<PlayMakerFSM> ignored = new HashSet<PlayMakerFSM>();
        static readonly Dictionary<PlayMakerFSM, List<FsmStateAction>> personalOf = new Dictionary<PlayMakerFSM, List<FsmStateAction>>();
        static readonly Dictionary<FsmState, bool> restCache = new Dictionary<FsmState, bool>();
        static readonly HashSet<string> named = new HashSet<string>();   // ID poses ici sur des objets de comptoir
        static bool applying;
        static float nextScan = -1, nextHookCheck, nextSpawnStep, nextWarn;
        static int serialBase, serialN;

        static readonly HashSet<string> InputActions = new HashSet<string> {
            "MousePickEvent", "GetButtonDown", "GetButtonUp", "GetMouseButtonDown", "GetMouseButtonUp", "GetKeyDown", "GetButton", "GetMouseButton", "GetKey" };
        static readonly HashSet<string> WriteFields = new HashSet<string> {
            "floatVariable", "intVariable", "boolVariable", "stringVariable", "storeResult", "storeValue", "variable", "store" };

        class Hook : ModHook
        {
            public override string Module { get { return Mod; } }
            public Register R;
            public override void OnEnter()
            {
                try { if (!applying && Replay.Depth == 0) OnLocal(R); } catch (System.Exception e) { Replay.HookError(e); }
                Finish();
            }
        }

        // Debut d'un etat de paiement d'un comptoir.
        class PayHook : ModHook
        {
            public override string Module { get { return Mod; } }
            public Counter C; public string StateName;
            public override void OnEnter()
            {
                try { if (!applying && Replay.Depth == 0) OnLocalPay(C, StateName); } catch (System.Exception e) { Replay.HookError(e); }
                Finish();
            }
        }

        // Fin d'un etat du comptoir qui cree un objet : l'objet recoit son ID d'achat s'il n'en a pas. N'envoie
        // rien (pas un ModHook) : chacun nomme ses propres objets, de la meme facon.
        class Made : FsmStateAction
        {
            public Counter C; public CreateObject[] Acts;
            public override void OnEnter()
            {
                try
                {
                    foreach (CreateObject c in Acts)
                    {
                        GameObject go = c.storeObject != null ? c.storeObject.Value : null;
                        if (go != null) Name(C, go);
                    }
                }
                catch (System.Exception e) { Replay.HookError(e); }
                Finish();
            }
        }

        public static void OnLevelLoaded()
        {
            registers.Clear(); registerOf.Clear(); pending.Clear(); unhookedRegisters.Clear();
            counters.Clear(); counterOf.Clear(); unhooked.Clear(); mutedCounters.Clear(); queue.Clear(); spawnSteps.Clear();
            ignored.Clear(); personalOf.Clear(); restCache.Clear(); named.Clear();
            testStep = 0; testLog = 0;
            nextScan = PlayerSync.InGame ? Time.realtimeSinceStartup + 9f : -1;
        }

        // Stock : la table Carried d'un magasin est-elle pretee au rejeu d'un achat (panier d'un autre) ?
        public static bool Borrowed(Hashtable carried)
        {
            for (int i = 0; i < pending.Count; i++) if (pending[i].Live == carried) return true;
            return false;
        }

        public static void Update()
        {
            float now = Time.realtimeSinceStartup;
            if (nextScan > 0 && now >= nextScan) { nextScan = now + 60f; Scan(); }   // caisses et comptoirs sont la des le chargement
            // Caisses et comptoirs eteints au releve (loin) : accroches des que leur objet s'allume (une tasse prise
            // sous la machine peut etre bue avant le releve suivant).
            if (nextScan > 0 && now >= nextHookCheck && (unhooked.Count > 0 || unhookedRegisters.Count > 0))
            {
                nextHookCheck = now + 1f;
                for (int i = unhooked.Count - 1; i >= 0; i--)
                {
                    Counter c = unhooked[i];
                    if (c.Fsm == null || c.Hooked) { unhooked.RemoveAt(i); continue; }
                    if (c.Fsm.gameObject.activeInHierarchy) HookCounter(c);
                }
                for (int i = unhookedRegisters.Count - 1; i >= 0; i--)
                {
                    Register r = unhookedRegisters[i];
                    if (r.Fsm == null || r.Hooked) { unhookedRegisters.RemoveAt(i); continue; }
                    if (r.Fsm.gameObject.activeInHierarchy) HookRegister(r);
                }
            }
            for (int i = pending.Count - 1; i >= 0; i--)
            {
                Pending p = pending[i];
                if (p.R.Fsm == null) { Unmute(p.Muted); pending.RemoveAt(i); continue; }
                string s = p.R.Fsm.ActiveStateName;
                bool finished = s == "State 5" || s == "Wait player" || s == "Player distance";
                if (!finished && now < p.Until) continue;
                // Le panier du joueur local revient (le jeu l'a vide dans 'Reset purchase').
                if (p.Live != null) { p.Live.Clear(); foreach (DictionaryEntry e in p.Carried) p.Live[e.Key] = e.Value; }
                p.R.Fsm.FsmVariables.GetFsmFloat("PriceTotal").Value = p.PriceTotal;
                p.R.Fsm.FsmVariables.GetFsmInt("BagStuff").Value = p.BagStuff;
                foreach (KeyValuePair<FsmFloat, float> kv in p.Floats) kv.Key.Value = kv.Value;
                Unmute(p.Muted);
                pending.RemoveAt(i);
            }
            // Comptoirs : rejeux en attente (comptoir eteint ou occupe), actions coupees jusqu'au repos.
            for (int i = 0; i < queue.Count; )
            {
                if (TryApply(queue[i], now)) queue.RemoveAt(i); else i++;
            }
            for (int i = mutedCounters.Count - 1; i >= 0; i--)
            {
                Counter c = mutedCounters[i];
                if (c.Fsm == null || now > c.MutedUntil || (now - c.MutedAt > 0.5f && c.Fsm.gameObject.activeInHierarchy && AtRest(c.Fsm))) UnmuteCounter(c);
            }
            if (spawnSteps.Count > 0 && now >= nextSpawnStep)
            {
                nextSpawnStep = now + 0.15f;
                SpawnStep st = spawnSteps[0];
                spawnSteps.RemoveAt(0);
                if (st.Fsm != null)
                {
                    FsmGameObject sp = st.Fsm.FsmVariables.FindFsmGameObject("SpawnPoint");
                    if (sp != null && st.Point != null) sp.Value = st.Point;
                    st.Fsm.SendEvent(st.Event);
                }
            }
        }

        // ---------------------------------------------------------------- releve
        static void Scan()
        {
            ignored.RemoveWhere(x => x == null);
            foreach (Object o in Resources.FindObjectsOfTypeAll(typeof(PlayMakerFSM)))
            {
                var f = (PlayMakerFSM)o;
                if (f.hideFlags != HideFlags.None || ignored.Contains(f) || registerOf.ContainsKey(f) || counterOf.ContainsKey(f)) continue;
                string fsm = f.FsmName;
                if (fsm != "Data" && fsm != "Buy" && fsm != "Button" && fsm != "Use") { ignored.Add(f); continue; }
                string n = f.gameObject.name;
                if (fsm == "Data" && n == "CashRegisterLogic" && f.Fsm.GetState("Spawn bag") != null && f.FsmVariables.FindFsmGameObject("Inventory") != null)
                {
                    var r = new Register { Key = Recon.Path(f.transform), Fsm = f };
                    registers[r.Key] = r; registerOf[f] = r;
                    ClaimBagCreator(f);   // avant de pouvoir accrocher la caisse : le monde ne doit jamais prendre le BagCreator
                    HookRegister(r);
                    if (!r.Hooked) unhookedRegisters.Add(r);
                    continue;
                }
                Counter c = NewCounter(f, n);
                if (c == null) { ignored.Add(f); continue; }
                counters[c.Key] = c; counterOf[f] = c;
                if (c.Kind == "cafe") ClaimPan(c);
                if (c.Fsm == null) continue;
                HookCounter(c);
                if (!c.Hooked) unhooked.Add(c);
            }
        }

        static void HookRegister(Register r)
        {
            if (r.Hooked || r.Fsm == null || !r.Fsm.gameObject.activeInHierarchy) return;   // actions pas chargees
            FsmState s = r.Fsm.Fsm.GetState("Spawn bag");
            try
            {
                var list = new List<FsmStateAction>(s.Actions);
                list.Insert(0, new Hook { R = r });
                s.Actions = list.ToArray();
            }
            catch { return; }
            r.Hooked = true;
            Log.Info("magasin : caisse suivie " + r.Key);
        }

        // BagCreator de la caisse (variable {BagCreator}) : a ce module, avant le premier releve du monde (16 s).
        static void ClaimBagCreator(PlayMakerFSM register)
        {
            FsmGameObject v = register.FsmVariables.FindFsmGameObject("BagCreator");
            GameObject go = v != null ? v.Value : null;
            if (go == null) return;
            foreach (PlayMakerFSM f in go.GetComponents<PlayMakerFSM>())
            {
                if (Replay.Claim(f, Mod)) Log.Info("magasin : " + go.name + "::" + f.FsmName + " reserve a la caisse (refait par le rejeu de l'achat)");
                else Log.Warn("magasin : " + go.name + "::" + f.FsmName + " deja suivi par " + Replay.Owner(f) + " : sac prepare deux fois chez les autres");
            }
        }

        // Comptoir reconnu : son genre, ses etats de paiement. Null : pas un comptoir suivi ici.
        static Counter NewCounter(PlayMakerFSM f, string n)
        {
            string fsm = f.FsmName, kind = null, label = null, key = null;
            string[] states = null;
            float wait = 30f;
            Transform parent = f.transform.parent;
            if (fsm == "Data" && n == "CashRegisterLogic")
            {
                string path = Recon.Path(f.transform);
                // Courrier (autre lot) ; repas du restaurant : cuisine par les PNJ d'apres le joueur local.
                if (path.Contains("/PostOffice/") || path.Contains("/Restaurant/")) return null;
                key = path + "::" + fsm;
                if (f.Fsm.GetState("Pay") != null) { kind = "bar"; label = "au bar"; states = new[] { "Pay" }; wait = 30f; }
                else if (f.Fsm.GetState("Spawn product") != null && f.FsmVariables.FindFsmGameObject("Inventory") != null) { kind = "puces"; label = "au marche aux puces"; states = new[] { "Purchase" }; wait = 120f; }
                else if (f.Fsm.GetState("Purchase") != null)
                {
                    kind = "caisse"; states = new[] { "Purchase" };
                    bool water = path.StartsWith("WATERFACILITY");
                    label = water ? "a la station d'epuration" : path.StartsWith("INSPECTION") ? "au controle technique" : "a une caisse";
                    wait = water ? 60f : 600f;
                }
            }
            else if (fsm == "Buy" && n == "CoffeeButton" && f.Fsm.GetState("Purchase") != null)
            {
                kind = "cafe"; label = "un cafe"; states = new[] { "Purchase" }; wait = 20f;
            }
            else if (fsm == "Button" && n == "Ticket" && parent != null && parent.name == "BUS" && f.transform.root.name == "NPC_CARS"
                     && f.Fsm.GetState("Pay trip") != null)
            {
                // Le bus change de parent (point de depart) : cle sans le chemin au-dessus de BUS (racine NPC_CARS :
                // pas un modele charge en memoire).
                kind = "bus"; label = "un ticket de bus"; states = new[] { "Pay trip" }; wait = 60f; key = "BUS/Ticket::Button";
            }
            else if (fsm == "Use" && n == "coffee cup(itemx)" && parent != null && parent.name == "CupPivot"
                     && f.Fsm.GetState("State 1") != null && f.FsmVariables.FindFsmFloat("Coffee") != null)
            {
                kind = "tasse"; label = "la tasse"; states = new[] { "State 1" }; wait = 20f;
            }
            if (kind == null) return null;
            if (key == null) key = Recon.Path(f.transform) + "::" + fsm;
            return new Counter { Key = key, Kind = kind, Label = label, Fsm = f, PayStates = states, Wait = wait };
        }

        // Verseur de la machine a cafe ({Pan}) : a ce module (sinon le monde rejouerait aussi POUR). Deja au monde :
        // le bouton n'est pas accroche ici (un seul chemin).
        static void ClaimPan(Counter c)
        {
            FsmGameObject v = c.Fsm.FsmVariables.FindFsmGameObject("Pan");
            GameObject go = v != null ? v.Value : null;
            if (go == null) return;
            foreach (PlayMakerFSM f in go.GetComponents<PlayMakerFSM>())
                if (!Replay.Claim(f, Mod))
                {
                    Log.Warn("magasin : " + Recon.Path(go.transform) + "::" + f.FsmName + " deja suivi par " + Replay.Owner(f) + " : bouton a cafe laisse au monde");
                    c.Fsm = null;
                    return;
                }
        }

        static void HookCounter(Counter c)
        {
            PlayMakerFSM f = c.Fsm;
            if (c.Hooked || f == null || !f.gameObject.activeInHierarchy) return;   // actions pas chargees
            if (!Replay.Claim(f, Mod)) { Log.Warn("magasin : " + c.Key + " deja suivi par " + Replay.Owner(f)); c.Fsm = null; return; }
            int made = 0;
            try
            {
                foreach (string st in c.PayStates)
                {
                    FsmState s = f.Fsm.GetState(st);
                    if (System.Array.Exists(s.Actions, a => a is PayHook)) continue;   // essai precedent interrompu
                    var list = new List<FsmStateAction>(s.Actions);
                    list.Insert(0, new PayHook { C = c, StateName = st });
                    s.Actions = list.ToArray();
                }
                // Objets crees par le comptoir (verres du bar) : ID d'achat a la creation.
                if (c.Kind == "bar")
                    foreach (FsmState s in f.Fsm.States)
                    {
                        List<CreateObject> acts = null;
                        foreach (FsmStateAction a in s.Actions) { var co = a as CreateObject; if (co != null) { if (acts == null) acts = new List<CreateObject>(); acts.Add(co); } }
                        if (acts == null || System.Array.Exists(s.Actions, a => a is Made)) continue;
                        var list = new List<FsmStateAction>(s.Actions);
                        list.Add(new Made { C = c, Acts = acts.ToArray() });
                        s.Actions = list.ToArray();
                        made++;
                    }
            }
            catch { return; }
            c.Hooked = true;
            Log.Info("magasin : comptoir suivi (" + c.Kind + ") " + c.Key + (made > 0 ? ", " + made + " etats createurs" : ""));
        }

        static GameObject Var(PlayMakerFSM f, string name)
        {
            FsmGameObject v = f != null ? f.FsmVariables.FindFsmGameObject(name) : null;
            return v != null ? v.Value : null;
        }

        static PlayMakerFSM FsmOnVar(PlayMakerFSM f, string var, string fsmName)
        {
            GameObject go = Var(f, var);
            return go != null ? Game.FsmOn(go, fsmName) : null;
        }

        static System.Collections.ArrayList ListOn(GameObject go, string reference)
        {
            if (go == null) return null;
            foreach (PlayMakerArrayListProxy p in go.GetComponents<PlayMakerArrayListProxy>())
                if (p.referenceName == reference) return p._arrayList;
            return null;
        }

        static Hashtable CarriedOf(PlayMakerFSM register)
        {
            GameObject inv = register.FsmVariables.GetFsmGameObject("Inventory").Value;
            if (inv == null) return null;
            foreach (PlayMakerHashTableProxy h in inv.GetComponents<PlayMakerHashTableProxy>())
                if (h.referenceName == "Carried") return h._hashTable;
            return null;
        }

        static string PlayerName(int who)
        {
            PlayerInfo pi;
            return Session.Players.TryGetValue(who, out pi) ? pi.Name : "?";
        }

        // ---------------------------------------------------------------- caisses a sac : envoi
        // Purchase : U8 joueur, U8 genre (0 sac), Str caisse, I32 BagStuff, U16 n, (Str produit, I32 quantite) x n,
        // U8 m, (Str variable, F32 valeur) x m.
        static void OnLocal(Register r)
        {
            if (!Session.Active) return;
            Hashtable carried = CarriedOf(r.Fsm);
            if (carried == null) return;
            var w = new NetWriter(Msg.Purchase).U8(Session.LocalId).U8(0).Str(r.Key)
                .I32(r.Fsm.FsmVariables.GetFsmInt("BagStuff").Value).U16(carried.Count);
            var desc = new System.Text.StringBuilder();
            foreach (DictionaryEntry e in carried)
            {
                int q = e.Value is int ? (int)e.Value : 0;
                w.Str(e.Key.ToString()).I32(q);
                if (q > 0) desc.Append(e.Key).Append(" x").Append(q).Append(' ');
            }
            var floats = new List<KeyValuePair<string, float>>();
            foreach (string name in BagFloats)
            {
                FsmFloat v = r.Fsm.FsmVariables.FindFsmFloat(name);
                if (v != null && v.Value > 0f) { floats.Add(new KeyValuePair<string, float>(name, v.Value)); desc.Append(name).Append(' ').Append(v.Value).Append(' '); }
            }
            w.U8(floats.Count);
            foreach (KeyValuePair<string, float> kv in floats) w.Str(kv.Key).F32(kv.Value);
            Log.Info("magasin : achat " + desc);
            Session.SendAll(w, true);
        }

        public static void OnMessage(Peer from, NetReader r)
        {
            byte[] raw = Session.IsHost ? r.Rest() : null;
            int who = r.U8();
            if (Session.IsHost) who = from.Id;
            // Relais aux autres invites : le message tel quel, seul le numero du joueur est fixe par l'hote.
            if (Session.IsHost) Session.Broadcast(new NetWriter(Msg.Purchase).U8(who).Raw(raw, 1, raw.Length - 1), true, who);
            int kind = r.U8();
            if (kind == 0) OnBag(who, r);
            else if (kind == 1) OnCounter(who, r);
        }

        // ---------------------------------------------------------------- caisses a sac : reception
        static void OnBag(int who, NetReader r)
        {
            string key = r.Str();
            int bagStuff = r.I32();
            int n = r.U16();
            var keys = new List<string>(n);
            var qtys = new List<int>(n);
            var desc = new System.Text.StringBuilder();
            for (int i = 0; i < n; i++)
            {
                string k = r.Str(); int q = r.I32();
                keys.Add(k); qtys.Add(q);
                if (q > 0) desc.Append(k).Append(" x").Append(q).Append(' ');
            }
            var floats = new List<KeyValuePair<string, float>>();
            if (r.More) for (int i = 0, m = r.U8(); i < m; i++) floats.Add(new KeyValuePair<string, float>(r.Str(), r.F32()));
            foreach (KeyValuePair<string, float> kv in floats) desc.Append(kv.Key).Append(' ').Append(kv.Value).Append(' ');
            Register reg;
            if (!registers.TryGetValue(key, out reg) || reg.Fsm == null) { Scan(); registers.TryGetValue(key, out reg); }
            string name = PlayerName(who);
            Hud.Toast(name + " a fait des courses : " + desc);
            if (reg == null || reg.Fsm == null) { Log.Warn("magasin : caisse " + key + " introuvable ici"); return; }
            if (!reg.Fsm.gameObject.activeInHierarchy)
            {
                EmulateBag(reg, keys, qtys, bagStuff, floats);
                Log.Info("magasin : achat de " + name + " refait sur les distributeurs, caisse eteinte ici (" + desc + ")");
                return;
            }
            Hashtable carried = CarriedOf(reg.Fsm);
            if (carried == null) return;
            FsmVariables fv = reg.Fsm.FsmVariables;
            var keep = new Pending
            {
                R = reg, Carried = new Hashtable(carried), Live = carried, Until = Time.realtimeSinceStartup + 20f,
                PriceTotal = fv.GetFsmFloat("PriceTotal").Value,
                BagStuff = fv.GetFsmInt("BagStuff").Value,
            };
            foreach (string bf in BagFloats)
            {
                FsmFloat v = fv.FindFsmFloat(bf);
                if (v == null) continue;
                keep.Floats.Add(new KeyValuePair<FsmFloat, float>(v, v.Value));
                v.Value = 0f;
            }
            foreach (KeyValuePair<string, float> kv in floats) { FsmFloat v = fv.FindFsmFloat(kv.Key); if (v != null) v.Value = kv.Value; }
            carried.Clear();
            for (int i = 0; i < keys.Count; i++) carried[keys[i]] = qtys[i];
            fv.GetFsmInt("BagStuff").Value = bagStuff;
            // 'Purchase' de Fleetari vient apres le sac : coupe (avec l'ecran et les statistiques) jusqu'a la fin.
            keep.Muted = MuteFsm(reg.Fsm);
            applying = true; Replay.Depth++;
            try { Game.SetState(reg.Fsm, "Spawn bag"); }
            finally { applying = false; Replay.Depth--; }
            pending.Add(keep);
            Log.Info("magasin : achat de " + name + " rejoue (" + desc + ")");
        }

        // Caisse eteinte ici : ce que ferait la chaine, directement sur les distributeurs (toujours actifs).
        static void EmulateBag(Register reg, List<string> keys, List<int> qtys, int bagStuff, List<KeyValuePair<string, float>> floats)
        {
            PlayMakerFSM f = reg.Fsm;
            if (bagStuff > 0)
            {
                PlayMakerFSM sp = FsmOnVar(f, "CreateShoppingBag", "ShoppingBag");
                if (sp != null)
                {
                    sp.SendEvent("SPAWNITEM");   // global : cree le sac tout de suite (etat 'Create product')
                    GameObject bag = Var(sp, "New");
                    if (bag != null)
                    {
                        // = BagCreator 'Create' (COPY) : sac active, panier recopie dedans. Il reste sous son point de
                        // depart (eteint avec le magasin) : sorti, il tomberait a travers le comptoir et le sol eteints ;
                        // il apparait sur le comptoir quand le magasin s'allume ici.
                        bag.SetActive(true);
                        System.Collections.ArrayList k = ListOn(bag, "Keys"), v = ListOn(bag, "Values");
                        if (k != null && v != null)
                        {
                            k.Clear(); v.Clear();
                            for (int i = 0; i < keys.Count; i++) { k.Add(keys[i]); v.Add(qtys[i]); }
                        }
                        Log.Info("magasin : sac " + bag.name + " cree ici sans la caisse (sous " + (bag.transform.parent != null ? bag.transform.parent.name : "rien") + ")");
                    }
                }
            }
            System.Collections.ArrayList separates = ListOn(Var(f, "Inventory"), "Separates");
            GameObject spawners = Var(f, "Spawners"), point = Var(f, "SpawnPoint");
            if (separates != null && spawners != null)
                for (int i = 0; i < keys.Count; i++)
                {
                    if (qtys[i] <= 0 || !separates.Contains(keys[i])) continue;
                    PlayMakerFSM pf = Game.FsmOn(spawners, keys[i]);
                    for (int j = 0; j < qtys[i] && pf != null; j++) spawnSteps.Add(new SpawnStep { Fsm = pf, Point = point, Event = "SPAWNITEM" });
                }
            foreach (KeyValuePair<string, float> kv in floats)
            {
                if (kv.Key != "QDynoTimes" || kv.Value <= 0f) continue;
                PlayMakerFSM dyno = FsmOnVar(f, "Dyno", "Use");
                FsmFloat tb = dyno != null ? dyno.FsmVariables.FindFsmFloat("TimeBought") : null;
                if (tb != null) tb.Value += kv.Value;
            }
        }

        // ---------------------------------------------------------------- comptoirs : envoi
        // Purchase : U8 joueur, U8 genre (1 comptoir), Str comptoir, Str etat, U8 acheteur, I32 numero d'achat,
        // variables de l'automate (U8 n + (Str, F32) ; idem I32, Bool, Str), U8 listes + (Str nom, U16 n, elements).
        static int NextSerial()
        {
            if (serialBase == 0) serialBase = Random.Range(1, 9999) * 1000;
            return serialBase + (++serialN);
        }

        static void OnLocalPay(Counter c, string state)
        {
            // Achat du joueur local pendant que le comptoir etait coupe (rejeu d'un autre) : il paie bien, lui
            // (les actions de l'etat viennent apres ce crochet).
            UnmuteCounter(c);
            c.SerialWho = Session.Active ? Session.LocalId : 0; c.Serial = NextSerial(); c.Made = 0;
            if (!Session.Active) return;
            var w = new NetWriter(Msg.Purchase).U8(Session.LocalId).U8(1).Str(c.Key).Str(state).U8(c.SerialWho).I32(c.Serial);
            WriteVars(c.Fsm, w);
            WriteLists(c, w);
            if (w.Length > 1100) { Log.Warn("magasin : " + c.Key + " trop gros a envoyer (" + w.Length + " o)"); return; }
            Log.Info("magasin : paye " + c.Label + " (" + c.Kind + " " + Describe(c.Fsm) + ", achat " + c.Serial + ")");
            Session.SendAll(w, true);
        }

        static bool SkipVar(string n) { return n.StartsWith("UT") || n.StartsWith("UniqueTag"); }

        static void WriteVars(PlayMakerFSM f, NetWriter w)
        {
            FsmVariables v = f.FsmVariables;
            var fl = new List<FsmFloat>(); foreach (FsmFloat x in v.FloatVariables) if (!SkipVar(x.Name)) fl.Add(x);
            var il = new List<FsmInt>(); foreach (FsmInt x in v.IntVariables) if (!SkipVar(x.Name)) il.Add(x);
            var bl = new List<FsmBool>(); foreach (FsmBool x in v.BoolVariables) if (!SkipVar(x.Name)) bl.Add(x);
            var sl = new List<FsmString>(); foreach (FsmString x in v.StringVariables) if (!SkipVar(x.Name) && (x.Value ?? "").Length < 120) sl.Add(x);
            w.U8(Mathf.Min(fl.Count, 255)); for (int i = 0; i < fl.Count && i < 255; i++) w.Str(fl[i].Name).F32(fl[i].Value);
            w.U8(Mathf.Min(il.Count, 255)); for (int i = 0; i < il.Count && i < 255; i++) w.Str(il[i].Name).I32(il[i].Value);
            w.U8(Mathf.Min(bl.Count, 255)); for (int i = 0; i < bl.Count && i < 255; i++) w.Str(bl[i].Name).Bool(bl[i].Value);
            w.U8(Mathf.Min(sl.Count, 255)); for (int i = 0; i < sl.Count && i < 255; i++) w.Str(sl[i].Name).Str(sl[i].Value ?? "");
        }

        static Vars ReadVars(NetReader r)
        {
            var v = new Vars();
            for (int i = 0, n = r.U8(); i < n; i++) v.F.Add(new KeyValuePair<string, float>(r.Str(), r.F32()));
            for (int i = 0, n = r.U8(); i < n; i++) v.I.Add(new KeyValuePair<string, int>(r.Str(), r.I32()));
            for (int i = 0, n = r.U8(); i < n; i++) v.B.Add(new KeyValuePair<string, bool>(r.Str(), r.Bool()));
            for (int i = 0, n = r.U8(); i < n; i++) v.S.Add(new KeyValuePair<string, string>(r.Str(), r.Str()));
            return v;
        }

        // Marche aux puces : la liste 'Bought' (objets choisis, par rang) de l'inventaire FleaMarketProducts.
        static void WriteLists(Counter c, NetWriter w)
        {
            System.Collections.ArrayList bought = c.Kind == "puces" ? ListOn(Var(c.Fsm, "Inventory"), "Bought") : null;
            if (bought == null || bought.Count > 200) { w.U8(0); return; }
            w.U8(1).Str("Bought").U16(bought.Count);
            foreach (object o in bought)
            {
                if (o is bool) w.U8(3).Bool((bool)o);
                else if (o is int) w.U8(0).I32((int)o);
                else if (o is float) w.U8(1).F32((float)o);
                else if (o is string) w.U8(2).Str((string)o);
                else w.U8(5);
            }
        }

        static List<KeyValuePair<string, List<object>>> ReadLists(NetReader r)
        {
            var res = new List<KeyValuePair<string, List<object>>>();
            if (!r.More) return res;
            for (int k = 0, c = r.U8(); k < c; k++)
            {
                string name = r.Str();
                int n = r.U16();
                var items = new List<object>(n);
                for (int i = 0; i < n; i++)
                {
                    int t = r.U8();
                    if (t == 0) items.Add(r.I32());
                    else if (t == 1) items.Add(r.F32());
                    else if (t == 2) items.Add(r.Str());
                    else if (t == 3) items.Add(r.Bool());
                    else items.Add(null);
                }
                res.Add(new KeyValuePair<string, List<object>>(name, items));
            }
            return res;
        }

        static readonly string[] DescStrings = { "EventName", "Event" };
        static readonly string[] DescFloats = { "Total", "TotalFinal", "PriceTotal", "HandlingCost", "Price", "Coffee" };

        static string Describe(PlayMakerFSM f)
        {
            var sb = new System.Text.StringBuilder();
            foreach (string n in DescStrings) { FsmString s = f.FsmVariables.FindFsmString(n); if (s != null && !string.IsNullOrEmpty(s.Value)) sb.Append(s.Value).Append(' '); }
            foreach (string n in DescFloats) { FsmFloat x = f.FsmVariables.FindFsmFloat(n); if (x != null && x.Value != 0f) sb.Append(n).Append('=').Append(x.Value.ToString("0.##")).Append(' '); }
            return sb.ToString().TrimEnd();
        }

        // ---------------------------------------------------------------- comptoirs : reception
        static void OnCounter(int who, NetReader r)
        {
            string key = r.Str(), state = r.Str();
            int sw = r.U8(), serial = r.I32();
            Vars v = ReadVars(r);
            List<KeyValuePair<string, List<object>>> lists = ReadLists(r);
            Counter c = FindCounter(key);
            if (c == null || c.Fsm == null)
            {
                if (Time.realtimeSinceStartup >= nextWarn) { nextWarn = Time.realtimeSinceStartup + 10f; Log.Warn("magasin : comptoir " + key + " introuvable ici"); }
                return;
            }
            if (c.Kind != "tasse") Hud.Toast(PlayerName(who) + (c.Kind == "cafe" ? " a pris " : " a paye ") + c.Label);
            var item = new Replayed { C = c, State = state, Who = who, SerialWho = sw, Serial = serial, V = v, Lists = lists, Until = Time.realtimeSinceStartup + c.Wait };
            if (!TryApply(item, Time.realtimeSinceStartup))
            {
                queue.Add(item);
                Log.Info("magasin : " + c.Label + " de " + PlayerName(who) + " en attente (comptoir " + (c.Fsm.gameObject.activeInHierarchy ? "occupe" : "eteint") + " ici)");
            }
        }

        // Cle de l'autre introuvable : par la fin du chemin (objet et parent), si elle est unique.
        static Counter FindCounter(string key)
        {
            Counter c;
            if (counters.TryGetValue(key, out c) && c.Fsm != null) return c;
            Scan();
            if (counters.TryGetValue(key, out c) && c.Fsm != null) return c;
            int cut = key.LastIndexOf('/');
            cut = cut > 0 ? key.LastIndexOf('/', cut - 1) : -1;
            string tail = cut >= 0 ? key.Substring(cut) : "/" + key;
            Counter found = null;
            foreach (Counter x in counters.Values)
                if (x.Fsm != null && x.Key.EndsWith(tail)) { if (found != null) return null; found = x; }
            return found;
        }

        // Vrai : traite (rejoue, refait a la main ou abandonne) ; faux : a reprendre.
        static bool TryApply(Replayed it, float now)
        {
            Counter c = it.C;
            PlayMakerFSM f = c.Fsm;
            if (f == null) return true;
            bool on = f.gameObject.activeInHierarchy;
            if (!on && Emulate(it)) return true;   // eteint : ce qui doit exister partout, fait a la main
            if (!on || !AtRest(f))
            {
                if (now < it.Until) return false;
                // Trop longtemps eteint ou occupe (le joueur local s'en sert) : fait a la main si possible, sinon abandonne.
                if (!Emulate(it)) Log.Info("magasin : " + c.Label + " de " + PlayerName(it.Who) + " abandonne (comptoir " + (on ? "occupe" : "eteint") + " ici)");
                return true;
            }
            if (!c.Hooked) HookCounter(c);
            string idle = f.ActiveStateName;
            FsmVariables fv = f.FsmVariables;
            foreach (var x in it.V.F) { FsmFloat t = fv.FindFsmFloat(x.Key); if (t != null) t.Value = x.Value; }
            // Location de la table des puces : RENT a la table passe deja par le monde (automate de SaleTable).
            foreach (var x in it.V.I) { FsmInt t = fv.FindFsmInt(x.Key); if (t != null) t.Value = c.Kind == "puces" && x.Key == "Weeks" ? 0 : x.Value; }
            foreach (var x in it.V.B) { FsmBool t = fv.FindFsmBool(x.Key); if (t != null) t.Value = x.Value; }
            foreach (var x in it.V.S) { FsmString t = fv.FindFsmString(x.Key); if (t != null) t.Value = x.Value; }
            SetLists(c, it.Lists);
            c.SerialWho = it.SerialWho; c.Serial = it.Serial; c.Made = 0;
            MuteCounter(c);
            applying = true; Replay.Depth++;
            try { Game.SetState(f, it.State); }
            finally { applying = false; Replay.Depth--; }
            Log.Info("magasin : " + c.Label + " de " + PlayerName(it.Who) + " rejoue (" + c.Kind + " " + Describe(f) + ", " + idle + " -> " + it.State + ", achat " + it.Serial + ")");
            return true;
        }

        static void SetLists(Counter c, List<KeyValuePair<string, List<object>>> lists)
        {
            if (lists == null || lists.Count == 0) return;
            GameObject inv = Var(c.Fsm, "Inventory");
            foreach (KeyValuePair<string, List<object>> l in lists)
            {
                System.Collections.ArrayList a = ListOn(inv, l.Key);
                if (a == null) continue;
                a.Clear();
                foreach (object o in l.Value) a.Add(o);
            }
        }

        // Comptoir eteint ici : ce qui doit exister partout est fait a la main. Faux : rien a faire de tel (attendre).
        static bool Emulate(Replayed it)
        {
            Counter c = it.C;
            if (c.Kind == "bar")
            {
                // Cigarettes : distributeur a compteur (le meme que le magasin), toujours actif. Verre, shot, cafe,
                // assiette : rien a garder, seulement vus au bar -- pas faits ici.
                string ev = null;
                foreach (var x in it.V.S) if (x.Key == "EventName") ev = x.Value;
                if (ev == "CIGARETTES")
                {
                    PlayMakerFSM sp = FsmOnVar(c.Fsm, "Spawner", "Cigarettes");
                    if (sp != null) { sp.SendEvent("SPAWNPUB"); Log.Info("magasin : cigarettes du bar creees ici sans le bar (" + PlayerName(it.Who) + ")"); }
                }
                else Log.Info("magasin : " + (ev ?? "?") + " du bar pas servi ici (bar eteint ou occupe)");
                return true;
            }
            if (c.Kind == "puces")
            {
                GameObject inv = Var(c.Fsm, "Inventory");
                System.Collections.ArrayList items = ListOn(inv, "Items"), shelf = ListOn(inv, "Shelf"), purchased = ListOn(inv, "Purchased");
                List<object> bought = null;
                foreach (var l in it.Lists) if (l.Key == "Bought") bought = l.Value;
                if (items == null || bought == null) return true;
                GameObject point = Var(c.Fsm, "SpawnPoint");
                int n = 0;
                for (int i = 0; i < bought.Count && i < items.Count; i++)
                {
                    if (!(bought[i] is bool) || !(bool)bought[i]) continue;
                    // = 'Spawn product' : objet sorti de l'inventaire, pose au comptoir, rayon cache. Pose SOUS le point de
                    // sortie (eteint avec le marche) plutot qu'a la racine : il tomberait a travers le comptoir eteint ;
                    // il apparait au comptoir quand le marche s'allume ici.
                    var go = items[i] as GameObject;
                    if (go == null) continue;
                    go.transform.parent = point != null ? point.transform : null;
                    if (point != null) go.transform.position = point.transform.position + Vector3.up * (0.1f * n);
                    go.SetActive(true);
                    if (purchased != null && i < purchased.Count) purchased[i] = true;
                    var sh = shelf != null && i < shelf.Count ? shelf[i] as GameObject : null;
                    if (sh != null) sh.SetActive(false);
                    n++;
                }
                Log.Info("magasin : " + n + " objets des puces sortis ici sans le comptoir (" + PlayerName(it.Who) + ")");
                return true;
            }
            if (c.Kind == "bus")
            {
                // = 'Pay trip' : le trajet est paye (le bus de l'hote repart).
                Transform bus = c.Fsm.transform.parent;
                Transform route = bus != null ? bus.Find("Route") : null;
                PlayMakerFSM start = route != null ? Game.FsmOn(route.gameObject, "Start") : null;
                FsmBool paid = start != null ? start.FsmVariables.FindFsmBool("Paid") : null;
                if (paid != null) { paid.Value = true; Log.Info("magasin : trajet en bus paye par " + PlayerName(it.Who) + " (ticket eteint ici)"); }
                return true;
            }
            return false;
        }

        // ---------------------------------------------------------------- objets crees : ID d'achat
        static string Prefix(string name)
        {
            int cut = name.IndexOf('(');
            if (cut >= 0) name = name.Substring(0, cut);
            var sb = new System.Text.StringBuilder();
            foreach (char ch in name.ToLowerInvariant()) if (ch >= 'a' && ch <= 'z') sb.Append(ch);
            return sb.Length > 0 ? sb.ToString() : "objet";
        }

        static void Name(Counter c, GameObject go)
        {
            if (Props.ItemId(go).Length > 0) return;   // deja un ID (compteur du jeu)
            PlayMakerFSM use = Game.FsmOn(go, "Use");
            if (use == null) { PlayMakerFSM[] all = go.GetComponents<PlayMakerFSM>(); if (all.Length > 0) use = all[0]; }
            if (use == null) return;
            string id = Prefix(go.name) + "-" + c.SerialWho + "-" + c.Serial + (c.Made > 0 ? "-" + c.Made : "");
            c.Made++;
            FsmString v = use.FsmVariables.FindFsmString("ID");
            if (v == null)
            {
                // Verre de biere, assiette : pas de variable ID dans leur automate, on l'ajoute (rien ne la lit).
                v = new FsmString("ID");
                var l = new List<FsmString>(use.FsmVariables.StringVariables);
                l.Add(v);
                use.FsmVariables.StringVariables = l.ToArray();
            }
            v.Value = id;
            named.Add(id);
            Log.Info("magasin : " + go.name + " cree " + c.Label + ", ID " + id);
        }

        // Consume : objet de comptoir nomme ici (sa fin "vide" -- barquette mangee -- est suivie aussi).
        public static bool Named(string id) { return id != null && named.Contains(id); }

        // ---------------------------------------------------------------- actions coupees pendant un rejeu
        // Ce qui toucherait au joueur qui rejoue : ecriture d'une globale Player* ou GUI* (argent, sous-titres,
        // corps), statistiques personnelles, objets sous PLAYER ou GUI (jurons, main). Lu une fois par automate.
        static List<FsmStateAction> PersonalWrites(PlayMakerFSM f)
        {
            List<FsmStateAction> l;
            if (personalOf.TryGetValue(f, out l)) return l;
            l = new List<FsmStateAction>();
            try
            {
                foreach (FsmState st in f.Fsm.States)
                    foreach (FsmStateAction a in st.Actions)
                        if (a != null && !(a is ModHook) && !(a is Made) && Personal(f, a)) l.Add(a);
            }
            catch { }
            personalOf[f] = l;
            return l;
        }

        static bool Personal(PlayMakerFSM f, FsmStateAction a)
        {
            string tn = a.GetType().Name;
            // Tests et conversions lisent leur premiere variable (BoolTest boolVariable...) : jamais coupes pour ca.
            bool reads = tn.Contains("Test") || tn.Contains("Compare") || tn.Contains("Changed") || tn.Contains("Switch") || tn.StartsWith("Bool") || tn.StartsWith("Convert");
            foreach (FieldInfo fi in a.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                object v = fi.GetValue(a);
                var nv = v as NamedVariable;
                if (nv != null && !reads && WriteFields.Contains(fi.Name) && nv.UseVariable
                    && (nv.Name.StartsWith("Player") || nv.Name.StartsWith("GUI")) && f.FsmVariables.GetVariable(nv.Name) == null) return true;
                if (tn.StartsWith("Get")) continue;   // lit seulement (distance au joueur, variable d'un autre automate)
                GameObject go = null;
                if (v is FsmGameObject) go = ((FsmGameObject)v).Value;
                else if (v is FsmOwnerDefault) { var od = (FsmOwnerDefault)v; if (od.OwnerOption != OwnerDefaultOption.UseOwner) go = od.GameObject.Value; }
                if (go == null) continue;
                string root = go.transform.root.name;
                if (go.name == "Statistics" || root == "PLAYER" || root == "GUI") return true;
            }
            return false;
        }

        static List<FsmStateAction> MuteFsm(PlayMakerFSM f)
        {
            var muted = new List<FsmStateAction>();
            foreach (FsmStateAction a in PersonalWrites(f)) if (a.Enabled) { a.Enabled = false; muted.Add(a); }
            return muted;
        }

        static void Unmute(List<FsmStateAction> muted)
        {
            if (muted == null) return;
            foreach (FsmStateAction a in muted) a.Enabled = true;
            muted.Clear();
        }

        static void MuteCounter(Counter c)
        {
            List<FsmStateAction> m = MuteFsm(c.Fsm);
            if (c.Muted == null) c.Muted = m; else c.Muted.AddRange(m);
            c.MutedAt = Time.realtimeSinceStartup;
            c.MutedUntil = c.MutedAt + 60f;
            if (!mutedCounters.Contains(c)) mutedCounters.Add(c);
        }

        static void UnmuteCounter(Counter c)
        {
            if (c.Muted == null) return;
            Unmute(c.Muted);
            c.Muted = null;
            mutedCounters.Remove(c);
        }

        // Au repos : l'etat attend le joueur (clic, touche) ou n'a pas de suite (bar entre deux commandes).
        static bool AtRest(PlayMakerFSM f)
        {
            FsmState s = f.Fsm.ActiveState;
            if (s == null) return true;
            bool r;
            if (restCache.TryGetValue(s, out r)) return r;
            r = s.Transitions.Length == 0;
            if (!r)
                try { foreach (FsmStateAction a in s.Actions) if (a != null && InputActions.Contains(a.GetType().Name)) { r = true; break; } }
                catch { }
            restCache[s] = r;
            return r;
        }

        // ---------------------------------------------------------------- essais
        // Essais : met des produits dans le panier de la caisse 'key' et paie comme le joueur.
        public static string TestBuy(string product, int qty)
        {
            foreach (Register r in registers.Values)
            {
                if (r.Fsm == null || !r.Key.Contains("/Store/")) continue;
                Hashtable carried = CarriedOf(r.Fsm);
                if (carried == null) return "pas de panier";
                carried[product] = qty;
                r.Fsm.FsmVariables.GetFsmInt("BagStuff").Value = qty;
                r.Fsm.FsmVariables.GetFsmFloat("PriceTotal").Value = 10f * qty;
                Game.SetState(r.Fsm, "Check money");
                return "achat de " + product + " x" + qty + " a " + r.Key;
            }
            return "aucune caisse de magasin";
        }

        // Appele par Consume.Test (deja branche dans Autotest).
        // [Test] Autotest=bar : l'hote (TestPos devant le comptoir du pub, les deux joueurs pres du pub pour que son
        //   automate tourne chez chacun) commande a 30 s la ligne [Test] TestObjet de la carte (1 biere par defaut,
        //   2 vodka, 3 saucisse-frites, 4 cafe, 5 cigarettes) par son automate Buy 'Purchase' (a defaut directement
        //   a la caisse, comme Buy) ; chacun note a 28 s puis de 40 a 56 s son argent et les objets suivis dont l'ID
        //   commence par [Test] TestProduit (beer par defaut). Attendu : le meme ID des deux cotes ("beer-0-...")
        //   au meme endroit, « rejoue » chez l'invite, argent de l'invite inchange (seul l'hote paie 8 mk).
        // [Test] Autotest=cafe : a 26 s chacun prend une tasse (GetACup, sans rien envoyer) et la pose sous le verseur
        //   (TestPos devant la machine du controle technique) ; a 30 s l'hote presse le bouton ('Purchase', comme le
        //   clic) ; chacun note de 29 a 41 s l'etat du verseur et le niveau 'Coffee' de la tasse ; a 42 s l'hote boit
        //   ('Play anim' -> 'State 1') ; a 50 s chacun note la tasse. Attendu : verseur ON des deux cotes, niveau
        //   0,2 partout, puis 'State 1' rejoue chez l'invite (tasse remise sous la machine, Coffee 0).
        // [Test] Autotest=sac-double : l'hote achete a 32 s [Test] TestProduit (Sausages) x2 a la caisse PSK ; chacun
        //   note toutes les 5 s de 25 a 60 s les sacs (shoppingbag*) et le module qui tient chaque BagCreator.
        //   Attendu : un seul sac de plus, le meme ID des deux cotes, BagCreator a « magasin » partout.
        static int testStep;
        static float testLog;
        static Rigidbody testCup;

        public static void Test(string mode, float t)
        {
            if (mode == "bar") TestBar(t);
            else if (mode == "cafe") TestCafe(t);
            else if (mode == "sac-double") TestBags(t);
        }

        static void TestBar(float t)
        {
            string prefix = Config.Get("Test", "TestProduit", "beer");
            if (testStep == 0 && t > 28f) { testStep = 1; Log.Info("autotest : bar, avant : " + Wallet.State() + " ; " + Props.Ids(prefix)); }
            if (Session.IsHost && testStep == 1 && t > 30f)
            {
                testStep = 2;
                Counter bar = null;
                foreach (Counter c in counters.Values) if (c.Kind == "bar" && c.Fsm != null) bar = c;
                if (bar == null) { Log.Info("autotest : bar introuvable (" + counters.Count + " comptoirs)"); return; }
                if (!bar.Fsm.gameObject.activeInHierarchy) { Log.Info("autotest : bar eteint ici (TestPos devant le comptoir)"); return; }
                if (!bar.Hooked) HookCounter(bar);
                int line = Mathf.Clamp(Config.GetInt("Test", "TestObjet", 1), 1, 5);
                string[] events = { "BEER", "VODKA", "POTATOES", "COFFEE", "CIGARETTES" };
                float[] prices = { 8f, 30f, 25f, 7f, 17f };
                Transform order = bar.Fsm.transform.root.Find("Stuff/LOD/ActivateBar/OrderList/" + line);
                PlayMakerFSM buy = order != null ? Game.FsmOn(order.gameObject, "Buy") : null;
                if (buy != null && buy.gameObject.activeInHierarchy) { Game.SetState(buy, "Purchase"); Log.Info("autotest : commande " + events[line - 1] + " par " + Recon.Path(order) + " -> caisse " + bar.Fsm.ActiveStateName); }
                else
                {
                    bar.Fsm.FsmVariables.GetFsmFloat("Total").Value = prices[line - 1];
                    bar.Fsm.FsmVariables.GetFsmString("EventName").Value = events[line - 1];
                    bar.Fsm.SendEvent("PURCHASE");
                    Log.Info("autotest : commande " + events[line - 1] + " directement a la caisse (carte eteinte) -> " + bar.Fsm.ActiveStateName);
                }
            }
            if (t > 40f && t < 57f && t - testLog >= 4f) { testLog = t; Log.Info("autotest : bar, " + Wallet.State() + " ; " + Props.Ids(prefix)); }
        }

        static void TestCafe(float t)
        {
            Counter btn = Nearest("cafe");
            if (testStep == 0 && t > 26f)
            {
                testStep = 1;
                if (btn == null) { Log.Info("autotest : aucun bouton a cafe actif suivi (" + counters.Count + " comptoirs, TestPos devant la machine)"); return; }
                // Une tasse, chacun chez soi (comme 'PICK A CUP', sans rien envoyer) ; elle sort de CupPivot (eteint)
                // a l'image suivante.
                Transform get = btn.Fsm.transform.parent != null ? btn.Fsm.transform.parent.Find("GetACup") : null;
                PlayMakerFSM gf = get != null ? Game.FsmOn(get.gameObject, "Use") : null;
                Replay.Depth++;
                try { if (gf != null && get.gameObject.activeInHierarchy) Game.SetState(gf, "State 1"); }
                finally { Replay.Depth--; }
                Log.Info("autotest : tasse prise par " + (gf != null ? Recon.Path(get) + " [" + gf.ActiveStateName + "]" : "rien (GetACup introuvable)"));
            }
            if (testStep == 1 && t > 27.5f && btn != null)
            {
                testStep = 2;
                // Posee sous le verseur (PourTarget de la tasse sur PanTarget).
                Transform fn = btn.Fsm.transform.parent;
                Counter cup = null;
                foreach (Counter c in counters.Values) if (c.Kind == "tasse" && c.Fsm != null && fn != null && c.Key.StartsWith(Recon.Path(fn))) cup = c;
                if (cup != null && cup.Fsm.gameObject.activeInHierarchy)
                {
                    HookCounter(cup);
                    GameObject pan = Var(btn.Fsm, "Pan");
                    Transform pour = cup.Fsm.transform.Find("PourTarget");
                    testCup = cup.Fsm.GetComponent<Rigidbody>();
                    if (pan != null && pour != null && testCup != null)
                    {
                        testCup.isKinematic = true;   // tenue sous le verseur le temps de l'essai (une tasse, pas une portiere)
                        cup.Fsm.transform.position += pan.transform.position - pour.position;
                    }
                }
                Log.Info("autotest : tasse " + (cup != null ? cup.Key + " [" + cup.Fsm.ActiveStateName + "]" : "introuvable") + " ; " + Cups());
            }
            if (t > 29f && t < 41f && t - testLog >= 3f) { testLog = t; Log.Info("autotest : cafe, " + Cups()); }
            if (Session.IsHost && testStep == 2 && t > 30f)
            {
                testStep = 3;
                if (btn != null) { Game.SetState(btn.Fsm, "Purchase"); Log.Info("autotest : bouton " + btn.Key + " presse -> " + btn.Fsm.ActiveStateName); }
            }
            if (testCup != null && t > 41f) { testCup.isKinematic = false; testCup = null; }
            if (Session.IsHost && testStep == 3 && t > 42f)
            {
                testStep = 4;
                Counter cup = Nearest("tasse");
                if (cup == null) { Log.Info("autotest : aucune tasse active"); return; }
                Game.SetState(cup.Fsm, "Play anim");
                Log.Info("autotest : tasse " + cup.Key + " bue -> " + cup.Fsm.ActiveStateName);
            }
            if (testStep < 5 && t > 50f) { testStep = 5; Log.Info("autotest : cafe, apres : " + Cups()); }
        }

        static Counter Nearest(string kind)
        {
            GameObject pl = GameObject.Find("PLAYER");
            Counter best = null;
            foreach (Counter c in counters.Values)
            {
                if (c.Kind != kind || c.Fsm == null || !c.Fsm.gameObject.activeInHierarchy) continue;
                if (best == null || pl != null && (c.Fsm.transform.position - pl.transform.position).sqrMagnitude < (best.Fsm.transform.position - pl.transform.position).sqrMagnitude) best = c;
            }
            return best;
        }

        static string Cups()
        {
            var sb = new System.Text.StringBuilder();
            foreach (Counter c in counters.Values)
            {
                // (tasse bue : remise sous la machine, dans CupPivot eteint -- notee quand meme)
                if (c.Fsm == null || (c.Kind != "tasse" && c.Kind != "cafe") || c.Kind == "cafe" && !c.Fsm.gameObject.activeInHierarchy) continue;
                if (c.Kind == "tasse" && string.IsNullOrEmpty(c.Fsm.ActiveStateName)) continue;   // jamais prise
                if (c.Kind == "tasse")
                    sb.Append("tasse ").Append(c.Fsm.transform.root.name).Append(" Coffee=").Append(c.Fsm.FsmVariables.FindFsmFloat("Coffee").Value.ToString("0.000"))
                      .Append(" [").Append(c.Fsm.ActiveStateName).Append("] sous ").Append(c.Fsm.transform.parent != null ? c.Fsm.transform.parent.name : "rien").Append(" ; ");
                else
                {
                    PlayMakerFSM pf = FsmOnVar(c.Fsm, "Pan", "Data");
                    sb.Append("bouton ").Append(c.Fsm.transform.root.name).Append(" [").Append(c.Fsm.ActiveStateName).Append("] verseur ")
                      .Append(pf != null ? pf.ActiveStateName + " (" + (Replay.Owner(pf) ?? "personne") + ")" : "?").Append(" ; ");
                }
            }
            return sb.Length > 0 ? sb.ToString() : "aucune machine active";
        }

        static void TestBags(float t)
        {
            if (Session.IsHost && testStep == 0 && t > 32f)
            {
                testStep = 1;
                Log.Info("autotest : " + TestBuy(Config.Get("Test", "TestProduit", "Sausages"), 2));
            }
            if (t > 25f && t < 61f && t - testLog >= 5f)
            {
                testLog = t;
                int n = 0;
                var ids = new System.Text.StringBuilder();
                foreach (Rigidbody rb in Object.FindObjectsOfType<Rigidbody>())
                {
                    if (!rb.name.StartsWith("shoppingbag")) continue;
                    n++;
                    ids.Append(rb.name).Append(' ');
                }
                var owners = new System.Text.StringBuilder();
                foreach (Register r in registers.Values)
                {
                    PlayMakerFSM cf = FsmOnVar(r.Fsm, "BagCreator", "Create");
                    owners.Append(cf != null ? cf.gameObject.name + "=" + (Replay.Owner(cf) ?? "personne") : "?").Append(' ');
                }
                Log.Info("autotest : sacs " + n + " (" + ids.ToString().TrimEnd() + ") ; BagCreator " + owners.ToString().TrimEnd() + " ; suivis " + Props.Ids("shoppingbag"));
            }
        }
    }
}
