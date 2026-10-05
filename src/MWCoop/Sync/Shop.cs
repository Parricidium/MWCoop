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
    //       rejoue ('Purchase' de Fleetari, statistiques, GUI*) est coupe jusqu'a la fin, puis panier remis --
    //       dans la table que l'inventaire tient A CE MOMENT : 'Reset purchase' (HashTableRevertSnapShot) ne vide
    //       pas la table, il en met une NEUVE dans le proxy (PlayMakerHashTableProxy.RevertToSnapShot) ;
    //     - caisse eteinte chez celui qui recoit (Fleetari loin : son automate ne tourne pas, le message etait
    //       perdu et les compteurs d'objets divergeaient) : la chaine est refaite a la main sur les distributeurs
    //       (racine Spawner, toujours active) -- sac rempli, gros articles un par un, temps de banc. Ce qui sort
    //       sous un point eteint (sac sous ShoppingBagSpawn, toujours eteint ; articles sous SpawnItemStore) est
    //       sorti a la racine comme le fait le BagCreator, et fige (contraintes) tant que le magasin n'est pas
    //       charge ici ou que Props ne l'a pas deplace : actif, son automate demarre et pose son ID (Props et
    //       Consume le suivent), sans tomber a travers le comptoir eteint.
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
    //    Releve refait a chaque rejeu, et seulement sur les references fixes : une variable que l'automate
    //    ecrit lui-meme (verre cree, objet des puces en cours) peut viser un instant l'objet en main du joueur.
    //    Bar : le rejeu entre directement dans 'Pay' ; la carte (OrderList) est cachee le temps du service, comme
    //    le fait 'State 6' a une commande -- sinon une commande locale (PURCHASE global) coupait le service rejoue
    //    (paquet de cigarettes jamais cree ici : compteur du distributeur decale pour toute la partie).
    //    Puces : jamais rejoues par l'automate (le joueur local y a peut-etre sa propre selection : Total, liste
    //    'Bought', articles marques 'Added', que le rejeu ecrasait puis remettait a zero) ; les objets achetes
    //    sont sortis a la main, et retires de la selection locale s'ils y etaient.
    //    Objets crees par le comptoir sans ID (verre de biere, shot, cafe, assiette du pub : rien a sauvegarder) :
    //    un ID est pose a leur creation, le meme partout (nom + joueur + numero d'achat) : Props et Consume les
    //    suivent (bu, mange, deplace) comme les autres articles.
    //    Comptoir eteint chez celui qui recoit : cigarettes du bar, objets des puces et trajet de bus faits a la
    //    main (compteurs, objets persistants, bus de l'hote) ; le reste attend que le comptoir s'allume, ou est
    //    abandonne (verres, cafe). Objets faits a la main sous un point eteint : sortis a la racine et figes,
    //    comme les sacs.
    // 3) TIRAGE DES PUCES (FleaMarketProducts :: Creator) : prix et objets en rayon tires chez chacun -- l'hote fait
    //    reference (voir « prix et rayons des puces » plus bas).
    // 4) RESTAURANT PSK (PERAPORTTI/Building/LOD100/Restaurant). On choisit un plat (OrderTriggers/* 'Buy' ->
    //    'Cashier' : Event FOODn et PriceTotal ecrits dans la caisse, PURCHASE), on paie a la caisse ('Purchase').
    //     - comptoir burger (BurgerCashRegister) : 'Purchase' -> 'State 1|2' (BurgerSmall/Large chez Keijo) ->
    //       'State 4' (Event et ORDER a Keijo) ; Keijo (Staff/BurgerRunnerPIVOT/Keijo :: Work) 'Tray1'... va en
    //       cuisine, et 'Burger2' cree le plateau (prefab Tray : burger, frites, soda ; son automate Data lit Event)
    //       a SpawnTray. Rejoue comme le bar (meme message, argent coupe) quand la caisse est au repos ici et Keijo
    //       libre : Keijo ne prend ORDER que dans 'Hello!', ou il n'est que si le joueur LOCAL est au comptoir --
    //       une action en fin de 'State 4' le met donc a 'Tray1' s'il ne sert pas deja. Ainsi Keijo sert chez tous
    //       (sa pose, envoyee par son auteur, Npcs, montre partout le service) ;
    //     - a la carte (AlaCarteRegister) : Jouni porte l'assiette a la TABLE du client (son marqueur de table, que
    //       personne d'autre n'a au meme endroit) : jamais rejoue par l'automate ; l'assiette est posee chez les
    //       autres d'apres le message du serveur (ci-dessous).
    //    Le plateau ou l'assiette recoit a sa creation l'ID de la commande (nom + acheteur + numero d'achat), le
    //    meme partout : Props le suit. Quand le serveur de l'ACHETEUR le pose ('Burger2', 'Spawn plate'), un message
    //    donne sa pose : chez qui ne l'a pas (caisse eteinte, Keijo occupe, assiette), il est cree de la meme prefab
    //    a cette pose (fige si le restaurant n'est pas charge ici) ; un plateau que Keijo d'ici sert ensuite pour
    //    la meme commande est retire (deja la). Manger (boutons 'Button' du plateau ou de l'assiette : frites,
    //    burger deballe puis mange, soda gorgee par gorgee) : les etats « State N » apres le clic sont rejoues
    //    chez les autres, ce qui touche au joueur coupe (seul celui qui mange a moins faim).
    //    Pas suivis ici : kiosque a saucisses et vendeur de pieces du rallye (leur effet passe deja par un automate
    //    du monde, PURCHASE / ALTERNATOR globaux), bureau de poste (courrier).
    public static class Shop
    {
        const string Mod = "magasin";

        // ================================================================ caisses a sac
        class Register { public string Key; public PlayMakerFSM Fsm; public bool Hooked; }
        // Panier local mis de cote pendant le rejeu d'un achat a la caisse R. Pas de reference a la table elle-meme :
        // 'Reset purchase' en met une neuve dans le proxy, le panier est remis dans celle du moment (CarriedOf).
        class Pending
        {
            public Register R; public float PriceTotal; public int BagStuff; public Hashtable Carried; public float Until;
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
            public GameObject Hidden;                              // carte du bar cachee le temps d'un service rejoue
            // Restaurant : le serveur (Keijo, Jouni :: Work), l'etat ou il prend la commande, celui ou il la pose.
            public PlayMakerFSM Waiter; public string WaiterStart, PlaceState; public bool WaiterHooked;
            public int ServeWho, ServeSerial, LastStart;            // commande que le serveur sert en ce moment
            public bool ReplayOrder;                               // commande rejouee : Keijo a mettre au service
            public GameObject LastMade;                            // plateau, assiette de la commande en cours
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
        // Distributeur a rejouer a la main (caisse eteinte) : un objet tous les 0,15 s, comme la caisse. Area : la
        // caisse (allumee = magasin charge ici).
        class SpawnStep { public PlayMakerFSM Fsm; public GameObject Point, Area; public string Event; }
        // Objet fait a la main loin d'ici, sorti a la racine et fige (contraintes, pas cinematique : Props garde la
        // main sur isKinematic) jusqu'a ce que sa zone soit chargee ici ou qu'il ait ete deplace (pose recue).
        class Frozen { public Rigidbody Body; public GameObject Area; public Vector3 At; public RigidbodyConstraints Was; }

        static readonly Dictionary<string, Counter> counters = new Dictionary<string, Counter>();
        static readonly Dictionary<PlayMakerFSM, Counter> counterOf = new Dictionary<PlayMakerFSM, Counter>();
        static readonly List<Counter> unhooked = new List<Counter>();              // objets eteints : accroches des qu'ils s'allument
        static readonly List<Register> unhookedRegisters = new List<Register>();
        static readonly List<Counter> mutedCounters = new List<Counter>();
        static readonly List<Replayed> queue = new List<Replayed>();
        static readonly List<SpawnStep> spawnSteps = new List<SpawnStep>();
        static readonly HashSet<PlayMakerFSM> ignored = new HashSet<PlayMakerFSM>();
        static readonly Dictionary<FsmState, bool> restCache = new Dictionary<FsmState, bool>();
        static readonly HashSet<string> named = new HashSet<string>();   // ID poses ici sur des objets de comptoir
        static readonly List<Frozen> frozen = new List<Frozen>();
        static bool applying;
        static float nextScan = -1, nextHookCheck, nextSpawnStep, nextWarn, nextFrozen;
        static int serialBase, serialN;

        // Restaurant : plateaux et assiettes servis (ID de commande -> objet), dans l'ordre ; poses recues en
        // attente du Keijo d'ici (il sert la meme commande) ; boutons a manger ; fins recues pas encore applicables.
        static readonly Dictionary<string, GameObject> served = new Dictionary<string, GameObject>();
        static readonly List<string> servedOrder = new List<string>();
        class ServedMsg { public Counter C; public int Who, SerialWho, Serial; public string Event, Id; public Vector3 Pos; public Quaternion Rot; public float Until; }
        static readonly List<ServedMsg> waitServe = new List<ServedMsg>();
        class EatObj { public GameObject Go; public string Id; public readonly HashSet<PlayMakerFSM> Hooked = new HashSet<PlayMakerFSM>(); }
        static readonly List<EatObj> eatObjs = new List<EatObj>();
        class EatMsg { public int Who; public string Id, Path, State; public List<KeyValuePair<string, int>> Ints; public float Until; }
        static readonly List<EatMsg> eatPending = new List<EatMsg>();
        static readonly List<KeyValuePair<float, List<FsmStateAction>>> eatMuted = new List<KeyValuePair<float, List<FsmStateAction>>>();
        static readonly Dictionary<string, GameObject> prefabs = new Dictionary<string, GameObject>();
        static float nextEatCheck, nextWaiterCheck;
        // Keijo :: Work : etats du service d'une commande (de 'Tray1' a 'You welcome') ; ceux de sa ronde, ou il ne
        // sert que si OrderStage > 0 (sa ronde a vide passe aussi par la cuisine).
        static readonly HashSet<string> ServingStates = new HashSet<string> {
            "Tray1", "Tray2", "Tray3", "Soda and fries", "Fries 2", "Get fries", "Get burger", "Burger", "Burger2", "You welcome" };
        static readonly HashSet<string> RoundStates = new HashSet<string> { "Move", "Wait", "Move 2", "State 1", "Wait 2" };
        // Caisse du restaurant : aucun choix du joueur local en cours (rejeu possible) / au repos (fin du rejeu).
        static readonly HashSet<string> RestoFree = new HashSet<string> { "Player distance", "Player waiting", "Hello!" };
        static readonly HashSet<string> RestoIdle = new HashSet<string> { "Player distance", "Player waiting", "Hello!", "Wait player", "Wait button" };
        // Kinds du message Purchase pour le restaurant.
        const int K_Served = 5, K_Eat = 6;

        // Debut du service d'une commande par le serveur ('Tray1' de Keijo, 'Purchase' de Jouni) : la commande qu'il
        // sert est celle du dernier achat a sa caisse (ici ou rejoue). N'envoie rien.
        class ServeStart : FsmStateAction
        {
            public Counter C;
            public override void OnEnter()
            {
                try
                {
                    C.ReplayOrder = false;
                    C.LastMade = null;
                    // Meme achat deja servi (service relance sans nouveau paiement suivi) : pas d'ID partage.
                    if (C.Serial != 0 && C.Serial == C.LastStart) C.ServeSerial = 0;
                    else { C.ServeWho = C.SerialWho; C.ServeSerial = C.Serial; C.LastStart = C.Serial; }
                }
                catch (System.Exception e) { Replay.HookError(e); }
                Finish();
            }
        }

        // Fin d'un etat du serveur qui cree le plateau ou l'assiette (ID de la commande) et/ou qui le pose (pose
        // envoyee par l'acheteur). N'envoie que la pose, pas un ModHook (comme Made).
        class Served : FsmStateAction
        {
            public Counter C; public CreateObject[] Acts; public bool Place;
            public override void OnEnter()
            {
                try
                {
                    foreach (CreateObject co in Acts)
                    {
                        GameObject go = co.storeObject != null ? co.storeObject.Value : null;
                        if (go != null) MadeServed(C, go);
                    }
                    if (Place) PlaceServed(C);
                }
                catch (System.Exception e) { Replay.HookError(e); }
                Finish();
            }
        }

        // Fin de 'State 4' de la caisse burger (ORDER envoye a Keijo) : commande rejouee que Keijo n'a pas prise
        // (pas dans 'Hello!' ici) -> mis au service.
        class OrderHook : FsmStateAction
        {
            public Counter C;
            public override void OnEnter()
            {
                try { if (C.ReplayOrder) { C.ReplayOrder = false; ForceServe(C); } }
                catch (System.Exception e) { Replay.HookError(e); }
                Finish();
            }
        }

        // Etat « State N » d'un bouton a manger (plateau, assiette) atteint apres un clic.
        class EatHook : ModHook
        {
            public override string Module { get { return Mod; } }
            public string Id, Path, StateName;
            public override void OnEnter()
            {
                try { if (!applying && Replay.Depth == 0) OnLocalEat(this, Fsm.Owner as PlayMakerFSM); } catch (System.Exception e) { Replay.HookError(e); }
                Finish();
            }
        }

        static readonly HashSet<string> InputActions = new HashSet<string> {
            "MousePickEvent", "GetButtonDown", "GetButtonUp", "GetMouseButtonDown", "GetMouseButtonUp", "GetKeyDown", "GetButton", "GetMouseButton", "GetKey" };
        static readonly HashSet<string> WriteFields = new HashSet<string> {
            "floatVariable", "intVariable", "boolVariable", "stringVariable", "storeResult", "storeValue", "variable", "store" };
        // Champs objet ecrits par l'action (sorties) : jamais une cible. "variable" : SetGameObject.
        static bool OutputField(string name) { return name.StartsWith("store") || name == "variable" || name == "result"; }

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
            ignored.Clear(); restCache.Clear(); named.Clear(); frozen.Clear();
            served.Clear(); servedOrder.Clear(); waitServe.Clear(); eatObjs.Clear(); eatPending.Clear(); eatMuted.Clear(); prefabs.Clear();
            testStep = 0; testLog = 0; testBag = null; testEatAt = 0;
            nextScan = PlayerSync.InGame ? Time.realtimeSinceStartup + 9f : -1;
            fleaDb = null; fleaCreator = null; fleaHostLists.Clear(); fleaHostShelf = null; fleaSentSig = null;
            fleaRolling = fleaPending = false; fleaReplyAt = -1; fleaApplied = 0; fleaRetry = 0; fleaNextSig = 0;
            fleaAskAt = PlayerSync.InGame ? Time.realtimeSinceStartup + 20f : -1;
        }

        // Stock : le panier de l'inventaire 'inventory' (objet INVENTORY_... qui porte Stocked et Carried) est-il
        // prete au rejeu d'un achat (panier d'un autre) ? Par l'objet, pas par la table (remplacee a chaque
        // 'Reset purchase').
        public static bool Borrowed(GameObject inventory)
        {
            if (inventory == null) return false;
            for (int i = 0; i < pending.Count; i++)
                if (pending[i].R.Fsm != null && Var(pending[i].R.Fsm, "Inventory") == inventory) return true;
            return false;
        }

        public static void Update()
        {
            float now = Time.realtimeSinceStartup;
            if (nextScan > 0 && now >= nextScan) { nextScan = now + 60f; Scan(); }   // caisses et comptoirs sont la des le chargement
            if (nextScan > 0 && Session.Active && now >= fleaNext) { fleaNext = now + 0.2f; FleaPricesStep(now); }
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
                // Le panier du joueur local revient, dans la table que le proxy tient maintenant ('Reset purchase'
                // y a mis une table neuve ; l'ancienne n'est plus lue par personne).
                Hashtable live = CarriedOf(p.R.Fsm);
                if (live != null) { live.Clear(); foreach (DictionaryEntry e in p.Carried) live[e.Key] = e.Value; }
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
                if (c.Fsm == null || now > c.MutedUntil || (now - c.MutedAt > 0.5f && c.Fsm.gameObject.activeInHierarchy && Idle(c))) UnmuteCounter(c);
            }
            if (nextScan > 0) RestoUpdate(now);
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
                    Loosen(Var(st.Fsm, "New"), st.Area);   // sous SpawnItemStore eteint : a la racine, fige
                }
            }
            if (frozen.Count > 0 && now >= nextFrozen)
            {
                nextFrozen = now + 0.5f;
                for (int i = frozen.Count - 1; i >= 0; i--)
                {
                    Frozen fz = frozen[i];
                    if (fz.Body == null) { frozen.RemoveAt(i); continue; }
                    bool loaded = fz.Area == null || fz.Area.activeInHierarchy;
                    bool moved = (fz.Body.transform.position - fz.At).sqrMagnitude > 0.25f;   // pose recue (Props), pris en main
                    if (!loaded && !moved) continue;
                    fz.Body.constraints = fz.Was;
                    if (!fz.Body.isKinematic) fz.Body.WakeUp();
                    frozen.RemoveAt(i);
                    Log.Info("magasin : " + fz.Body.name + " libere (" + (loaded ? "zone chargee ici" : "deplace") + ")");
                }
            }
        }

        // Objet tout juste cree par un distributeur sous un point eteint (sac sous ShoppingBagSpawn, toujours
        // eteint ; articles sous SpawnItemStore, cigarettes sous DrinkSpawnPoint, magasin loin) : jamais demarre
        // la-dessous (pas d'ID, ni Props ni Consume ne le voient, et il disparait avec la zone). Sorti a la racine
        // et active (comme le BagCreator, 'Activate bag'), fige si sa zone ('area') n'est pas chargee ici.
        static void Loosen(GameObject go, GameObject area)
        {
            if (go == null || go.activeInHierarchy) return;   // deja dans le monde (zone chargee) : comme le jeu
            go.transform.parent = null;
            go.SetActive(true);
            Freeze(go, area);
            Consume.Soon(go);   // suivi des que son automate a pose son ID (image suivante)
        }

        static void Freeze(GameObject go, GameObject area)
        {
            Rigidbody rb = go != null ? go.GetComponent<Rigidbody>() : null;
            if (rb == null || area == null || area.activeInHierarchy) return;
            for (int i = 0; i < frozen.Count; i++) if (frozen[i].Body == rb) return;
            frozen.Add(new Frozen { Body = rb, Area = area, At = rb.transform.position, Was = rb.constraints });
            rb.constraints = RigidbodyConstraints.FreezeAll;
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
                if (c.Waiter != null && !Replay.Claim(c.Waiter, Mod))
                {
                    // (Keijo et Jouni n'ont ni clic ni evenement global : le monde ne les prend pas d'ordinaire.)
                    Log.Warn("magasin : " + Recon.Path(c.Waiter.transform) + "::Work deja suivi par " + Replay.Owner(c.Waiter) + " : plateaux sans ID partage");
                    c.Waiter = null;
                }
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
            PlayMakerFSM waiter = null;
            string wStart = null, wPlace = null;
            if (fsm == "Data" && n == "CashRegisterLogic")
            {
                string path = Recon.Path(f.transform);
                if (path.Contains("/PostOffice/")) return null;   // courrier (autre lot)
                key = path + "::" + fsm;
                if (path.Contains("/Restaurant/"))
                {
                    if (f.Fsm.GetState("Purchase") == null) return null;
                    // Burger : Keijo ({Cashier}) prend la commande ('State 4' -> ORDER) ; a la carte : Jouni ({Runner}).
                    PlayMakerFSM keijo = FsmOnVar(f, "Cashier", "Work"), jouni = FsmOnVar(f, "Runner", "Work");
                    if (jouni != null && jouni.Fsm.GetState("Spawn plate") != null)
                    {
                        kind = "carte"; label = "un plat a la carte"; waiter = jouni; wStart = "Purchase"; wPlace = "Spawn plate"; wait = 5f;
                    }
                    else if (keijo != null && f.Fsm.GetState("State 4") != null && keijo.Fsm.GetState("Burger2") != null && keijo.Fsm.GetState("Tray1") != null)
                    {
                        kind = "resto"; label = "un repas au comptoir burger"; waiter = keijo; wStart = "Tray1"; wPlace = "Burger2"; wait = 60f;
                    }
                    else return null;
                    states = new[] { "Purchase" };
                }
                else if (f.Fsm.GetState("Pay") != null) { kind = "bar"; label = "au bar"; states = new[] { "Pay" }; wait = 30f; }
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
            return new Counter { Key = key, Kind = kind, Label = label, Fsm = f, PayStates = states, Wait = wait, Waiter = waiter, WaiterStart = wStart, PlaceState = wPlace };
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
                // Caisse burger : 'State 4' envoie la commande a Keijo ; en fin d'etat, un rejeu qu'il n'a pas prise.
                if (c.Kind == "resto")
                {
                    FsmState s4 = f.Fsm.GetState("State 4");
                    if (!System.Array.Exists(s4.Actions, a => a is OrderHook))
                    {
                        var list = new List<FsmStateAction>(s4.Actions);
                        list.Add(new OrderHook { C = c });
                        s4.Actions = list.ToArray();
                    }
                }
            }
            catch { return; }
            c.Hooked = true;
            Log.Info("magasin : comptoir suivi (" + c.Kind + ") " + c.Key + (made > 0 ? ", " + made + " etats createurs" : ""));
            if (c.Waiter != null) HookWaiter(c);
        }

        // ---------------------------------------------------------------- restaurant
        // Serveur : debut du service (commande servie), etats qui creent le plateau ou l'assiette, etat qui le pose.
        // Actions chargees seulement objet actif : sinon repris par RestoUpdate.
        static void HookWaiter(Counter c)
        {
            PlayMakerFSM w = c.Waiter;
            if (c.WaiterHooked || w == null || !w.gameObject.activeInHierarchy) return;
            int made = 0;
            try
            {
                FsmState start = w.Fsm.GetState(c.WaiterStart);
                if (start != null && !System.Array.Exists(start.Actions, a => a is ServeStart))
                {
                    var l = new List<FsmStateAction>(start.Actions);
                    l.Insert(0, new ServeStart { C = c });
                    start.Actions = l.ToArray();
                }
                foreach (FsmState s in w.Fsm.States)
                {
                    List<CreateObject> acts = null;
                    foreach (FsmStateAction a in s.Actions) { var co = a as CreateObject; if (co != null) { if (acts == null) acts = new List<CreateObject>(); acts.Add(co); } }
                    bool place = s.Name == c.PlaceState;
                    if ((acts == null && !place) || System.Array.Exists(s.Actions, a => a is Served)) continue;
                    var list = new List<FsmStateAction>(s.Actions);
                    list.Add(new Served { C = c, Acts = acts != null ? acts.ToArray() : new CreateObject[0], Place = place });
                    s.Actions = list.ToArray();
                    if (acts != null) made++;
                }
            }
            catch { return; }
            c.WaiterHooked = true;
            Log.Info("magasin : serveur " + w.gameObject.name + " suivi (" + c.Kind + ", " + made + " etats createurs, pose en '" + c.PlaceState + "')");
        }

        static bool Idle(Counter c)
        {
            if (c.Kind == "resto" || c.Kind == "carte") return RestoIdle.Contains(c.Fsm.ActiveStateName ?? "");
            return AtRest(c.Fsm);
        }

        // Comptoir pret a rejouer un achat d'un autre. Burger : caisse sans choix local en cours, Keijo libre.
        static bool Ready(Counter c)
        {
            if (c.Kind != "resto") return AtRest(c.Fsm);
            // (Keijo encaisse aussi a la carte, avec Virpi : 'Move 3' a 'Move 6' -- pas interrompu.)
            string k = c.Waiter != null && c.Waiter.gameObject.activeInHierarchy ? c.Waiter.ActiveStateName ?? "" : "";
            return RestoFree.Contains(c.Fsm.ActiveStateName ?? "") && !WaiterBusy(c) && !KeijoAway.Contains(k);
        }
        static readonly HashSet<string> KeijoAway = new HashSet<string> { "Move 3", "Cashing 3", "Talk to Virpi", "Move 6" };

        static int OrderStage(Counter c)
        {
            FsmInt v = c.Waiter != null ? c.Waiter.FsmVariables.FindFsmInt("OrderStage") : null;
            return v != null ? v.Value : 0;
        }

        // Keijo sert-il une commande ?
        static bool WaiterBusy(Counter c)
        {
            if (c.Waiter == null || !c.Waiter.gameObject.activeInHierarchy) return false;
            string s = c.Waiter.ActiveStateName ?? "";
            return ServingStates.Contains(s) || RoundStates.Contains(s) && OrderStage(c) > 0;
        }

        static void ForceServe(Counter c)
        {
            PlayMakerFSM w = c.Waiter;
            if (w == null || !w.gameObject.activeInHierarchy) { Log.Info("magasin : commande rejouee, serveur absent ou eteint ici"); return; }
            string was = w.ActiveStateName;
            if (WaiterBusy(c)) { Log.Info("magasin : " + w.gameObject.name + " a pris la commande rejouee (" + was + ")"); return; }
            applying = true; Replay.Depth++;
            try { Game.SetState(w, c.WaiterStart); }
            finally { applying = false; Replay.Depth--; }
            Log.Info("magasin : " + w.gameObject.name + " mis au service de la commande rejouee (" + was + " -> " + w.ActiveStateName + ", achat " + c.ServeSerial + ")");
        }

        static GameObject Live(string id)
        {
            GameObject g;
            if (id != null && served.TryGetValue(id, out g) && g != null) return g;
            return id != null ? Props.ObjectOf(id) : null;
        }

        // Plateau ou assiette tout juste cree par le serveur d'ici : ID de la commande servie ; deja la (fait
        // d'apres la pose de l'acheteur) : celui-ci est retire.
        static void MadeServed(Counter c, GameObject go)
        {
            if (c.ServeSerial == 0)
            {
                // Commande sans achat suivi (servie avant le releve, ou service relance) : ID propre a ce joueur.
                string own = Prefix(go.name) + "-" + (Session.Active ? Session.LocalId : 0) + "-" + NextSerial();
                AdoptServed(c, go, own);
                Log.Info("magasin : " + go.name + " servi sans achat suivi, ID local " + own);
                return;
            }
            string id = Prefix(go.name) + "-" + c.ServeWho + "-" + c.ServeSerial;
            GameObject old = Live(id);
            if (old != null && old != go)
            {
                Object.Destroy(go);
                c.LastMade = old;
                Log.Info("magasin : " + id + " deja la ici (pose de l'acheteur) : " + go.name + " de " + c.Waiter.gameObject.name + " retire");
                return;
            }
            AdoptServed(c, go, id);
            Log.Info("magasin : " + go.name + " servi (" + c.Label + "), ID " + id);
        }

        static void AdoptServed(Counter c, GameObject go, string id)
        {
            SetId(go, id);
            served[id] = go;
            servedOrder.Remove(id); servedOrder.Add(id);
            if (servedOrder.Count > 64) { served.Remove(servedOrder[0]); servedOrder.RemoveAt(0); }
            if (c != null) c.LastMade = go;
            Props.Track(go);
            Consume.Soon(go);
            WatchEats(go, id);
        }

        // Le serveur de l'acheteur pose le plateau (a SpawnTray) ou l'assiette (sur sa table) : pose aux autres.
        // Purchase : U8 joueur, U8 K_Served, Str comptoir, U8 acheteur, I32 numero d'achat, Str Event, Str ID,
        // Vec position, Quat rotation.
        static void PlaceServed(Counter c)
        {
            GameObject go = c.LastMade;
            if (!Session.Active || go == null || c.ServeSerial == 0 || c.ServeWho != Session.LocalId) return;
            string id = Props.ItemId(go);
            if (id.Length == 0) return;
            string ev = EventOf(go);
            if (ev.Length == 0) ev = StrVar(c.Waiter, "Event");
            if (ev.Length == 0) ev = StrVar(c.Fsm, "Event");
            var w = new NetWriter(Msg.Purchase).U8(Session.LocalId).U8(K_Served).Str(c.Key).U8(c.ServeWho).I32(c.ServeSerial)
                .Str(ev).Str(id).Vec(go.transform.position).Quat(go.transform.rotation);
            Session.SendAll(w, true);
            Log.Info("magasin : " + id + " pose par " + c.Waiter.gameObject.name + " en " + go.transform.position.ToString("F2") + " (" + ev + "), pose envoyee");
        }

        static string StrVar(PlayMakerFSM f, string name)
        {
            FsmString v = f != null ? f.FsmVariables.FindFsmString(name) : null;
            return v != null && v.Value != null ? v.Value : "";
        }

        static string EventOf(GameObject go)
        {
            foreach (PlayMakerFSM f in go.GetComponents<PlayMakerFSM>()) { string s = StrVar(f, "Event"); if (s.Length > 0) return s; }
            return "";
        }

        static void OnServed(int who, NetReader r)
        {
            var m = new ServedMsg { Who = who };
            string key = r.Str();
            m.SerialWho = r.U8(); m.Serial = r.I32(); m.Event = r.Str(); m.Id = r.Str(); m.Pos = r.Vec(); m.Rot = r.Quat();
            m.C = FindCounter(key);
            if (m.C == null || m.C.Waiter == null) { Log.Warn("magasin : " + m.Id + " servi chez " + PlayerName(who) + ", comptoir " + key + " introuvable ici"); return; }
            GameObject go = Live(m.Id);
            if (go != null) { Align(go, m); return; }
            // Achat pas encore rejoue ici (Keijo occupe) : plus la peine, le plateau arrive tout fait.
            for (int i = queue.Count - 1; i >= 0; i--)
                if (queue[i].C == m.C && queue[i].Serial == m.Serial && queue[i].SerialWho == m.SerialWho)
                {
                    queue.RemoveAt(i);
                    Log.Info("magasin : rejeu de l'achat " + m.Serial + " abandonne, " + m.Id + " pose d'apres l'acheteur");
                }
            // Keijo d'ici sert cette meme commande : son plateau (meme ID, meme SpawnTray) est attendu un moment.
            if (m.C.ServeSerial == m.Serial && m.C.ServeWho == m.SerialWho && WaiterBusy(m.C))
            {
                m.Until = Time.realtimeSinceStartup + 40f;
                waitServe.Add(m);
                Log.Info("magasin : " + m.Id + " pose chez " + PlayerName(who) + ", " + m.C.Waiter.gameObject.name + " le sert encore ici (" + m.C.Waiter.ActiveStateName + ")");
                return;
            }
            MakeServed(m);
        }

        // Deja la ici (Keijo d'ici) : recale s'il est ailleurs que chez l'acheteur et n'a pas bouge depuis.
        static void Align(GameObject go, ServedMsg m)
        {
            float d = (go.transform.position - m.Pos).magnitude;
            Rigidbody rb = go.GetComponent<Rigidbody>();
            float age;
            bool moved = Props.MovedByOther(go, out age) || rb != null && rb.isKinematic;
            if (d > 0.5f && !moved)
            {
                go.transform.position = m.Pos; go.transform.rotation = m.Rot;
                if (rb != null) { rb.velocity = Vector3.zero; rb.angularVelocity = Vector3.zero; }
            }
            Log.Info("magasin : " + m.Id + " deja la ici, a " + d.ToString("0.00") + " m de celui de " + PlayerName(m.Who) + (d > 0.5f && !moved ? " : recale" : ""));
        }

        // Plateau ou assiette de l'acheteur, fait ici de la meme prefab que son serveur, a sa pose.
        static void MakeServed(ServedMsg m)
        {
            GameObject prefab = ServedPrefab(m.C, m.Event);
            if (prefab == null) { Log.Warn("magasin : " + m.Id + " : prefab du serveur introuvable (" + m.C.Kind + " " + m.Event + ")"); return; }
            var go = (GameObject)Object.Instantiate(prefab, m.Pos, m.Rot);
            // = SetFsmString de 'Burger2' : le plateau lit sa commande (Event) a son demarrage.
            if (m.C.Kind == "resto")
                foreach (PlayMakerFSM f in go.GetComponents<PlayMakerFSM>()) { FsmString ev = f.FsmVariables.FindFsmString("Event"); if (ev != null) ev.Value = m.Event; }
            AdoptServed(null, go, m.Id);
            Freeze(go, m.C.Fsm.gameObject);   // restaurant pas charge ici : tenu en l'air, pas a travers le comptoir eteint
            Log.Info("magasin : " + go.name + " de " + PlayerName(m.Who) + " pose ici d'apres lui (" + m.C.Label + " " + m.Event + ", ID " + m.Id + ", "
                     + (m.C.Fsm.gameObject.activeInHierarchy ? "restaurant charge" : "restaurant pas charge ici : fige") + ")");
        }

        // Prefab du plateau (CreateObject de 'Burger2') ou de l'assiette (etat de Jouni choisi par Event dans
        // 'Purchase' : FOOD1 -> Sausages...). Serveur jamais allume ici : ses actions sont chargees a la main.
        static GameObject ServedPrefab(Counter c, string ev)
        {
            string k = c.Key + "|" + (c.Kind == "resto" ? "" : ev);
            GameObject p;
            if (prefabs.TryGetValue(k, out p) && p != null) return p;
            PlayMakerFSM w = c.Waiter;
            string state = c.Kind == "resto" ? c.PlaceState : null;
            try
            {
                FsmState buy = c.Kind == "carte" ? w.Fsm.GetState(c.WaiterStart) : null;
                if (buy != null) foreach (FsmTransition t in buy.Transitions) if (t.EventName == ev) state = t.ToState;
                FsmState s = state != null ? w.Fsm.GetState(state) : null;
                if (s == null) return null;
                FsmStateAction[] acts;
                try { acts = s.Actions; }
                catch { w.Fsm.InitData(); acts = s.Actions; }
                foreach (FsmStateAction a in acts)
                {
                    var co = a as CreateObject;
                    if (co != null && co.gameObject != null && co.gameObject.Value != null) { p = co.gameObject.Value; break; }
                }
            }
            catch (System.Exception e) { Log.Warn("magasin : prefab de " + w.gameObject.name + " '" + state + "' illisible : " + e.Message); return null; }
            if (p != null) prefabs[k] = p;
            return p;
        }

        static void RestoUpdate(float now)
        {
            if (now >= nextWaiterCheck)
            {
                nextWaiterCheck = now + 1f;
                foreach (Counter c in counters.Values) if (c.Waiter != null && c.Hooked && !c.WaiterHooked) HookWaiter(c);
                for (int i = waitServe.Count - 1; i >= 0; i--)
                {
                    ServedMsg m = waitServe[i];
                    GameObject go = Live(m.Id);
                    if (go != null) { waitServe.RemoveAt(i); Align(go, m); continue; }
                    if (now < m.Until && WaiterBusy(m.C)) continue;
                    waitServe.RemoveAt(i);
                    Log.Info("magasin : " + m.Id + " pas servi par " + m.C.Waiter.gameObject.name + " d'ici (" + m.C.Waiter.ActiveStateName + ") : pose d'apres " + PlayerName(m.Who));
                    MakeServed(m);
                }
            }
            if (now >= nextEatCheck && (eatObjs.Count > 0 || eatPending.Count > 0 || eatMuted.Count > 0))
            {
                nextEatCheck = now + 0.25f;
                for (int i = eatObjs.Count - 1; i >= 0; i--) { if (eatObjs[i].Go == null) eatObjs.RemoveAt(i); else HookEats(eatObjs[i]); }
                for (int i = 0; i < eatPending.Count; )
                {
                    EatMsg m = eatPending[i];
                    if (ApplyEat(m)) { eatPending.RemoveAt(i); continue; }
                    if (now > m.Until) { eatPending.RemoveAt(i); Log.Info("magasin : repas " + m.Id + "/" + m.Path + " -> " + m.State + " de " + PlayerName(m.Who) + " abandonne (objet absent ou eteint ici)"); continue; }
                    i++;
                }
                for (int i = eatMuted.Count - 1; i >= 0; i--)
                    if (now > eatMuted[i].Key) { Unmute(eatMuted[i].Value); eatMuted.RemoveAt(i); }
            }
        }

        // ---------------------------------------------------------------- restaurant : manger
        // Boutons 'Button' du plateau (EatFries, DrinkSoda, LARGE/SMALL a deballer, EatBurger) ou de l'assiette (sur
        // elle) : reserves a ce module (sinon le monde rejouerait le clic), accroches des qu'ils s'allument (EatBurger
        // apres le deballage).
        static void WatchEats(GameObject go, string id)
        {
            foreach (EatObj e in eatObjs) if (e.Go == go) return;
            var eo = new EatObj { Go = go, Id = id };
            foreach (PlayMakerFSM f in go.GetComponentsInChildren<PlayMakerFSM>(true))
                if (f.FsmName == "Button" && !Replay.Claim(f, Mod)) Log.Warn("magasin : " + id + "/" + f.gameObject.name + " deja suivi par " + Replay.Owner(f));
            eatObjs.Add(eo);
            HookEats(eo);
        }

        static void HookEats(EatObj e)
        {
            foreach (PlayMakerFSM f in e.Go.GetComponentsInChildren<PlayMakerFSM>(true))
            {
                if (f.FsmName != "Button" || e.Hooked.Contains(f) || !f.gameObject.activeInHierarchy || Replay.ClaimedByOther(f, Mod)) continue;
                string path = RelPath(e.Go.transform, f.transform);
                int n = 0;
                try
                {
                    foreach (FsmState s in f.Fsm.States)
                    {
                        if (!s.Name.StartsWith("State ") || System.Array.Exists(s.Actions, a => a is EatHook)) continue;
                        var l = new List<FsmStateAction>(s.Actions);
                        l.Insert(0, new EatHook { Id = e.Id, Path = path, StateName = s.Name });
                        s.Actions = l.ToArray();
                        n++;
                    }
                }
                catch { continue; }
                e.Hooked.Add(f);
            }
        }

        static string RelPath(Transform root, Transform t)
        {
            string p = "";
            for (; t != null && t != root; t = t.parent) p = p.Length == 0 ? t.name : t.name + "/" + p;
            return p;
        }

        // Purchase : U8 joueur, U8 K_Eat, Str ID, Str chemin du bouton sous l'objet, Str etat, U8 n, (Str, I32) x n
        // (entiers de l'automate avant l'etat : gorgees du soda).
        static void OnLocalEat(EatHook h, PlayMakerFSM f)
        {
            if (!Session.Active || f == null) return;
            var w = new NetWriter(Msg.Purchase).U8(Session.LocalId).U8(K_Eat).Str(h.Id).Str(h.Path).Str(h.StateName);
            FsmInt[] ints = f.FsmVariables.IntVariables;
            int n = Mathf.Min(ints.Length, 16);
            w.U8(n);
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < n; i++) { w.Str(ints[i].Name).I32(ints[i].Value); sb.Append(' ').Append(ints[i].Name).Append('=').Append(ints[i].Value); }
            Session.SendAll(w, true);
            Log.Info("magasin : repas " + h.Id + "/" + (h.Path.Length > 0 ? h.Path : f.gameObject.name) + " -> " + h.StateName + sb);
        }

        static void OnEat(int who, NetReader r)
        {
            var m = new EatMsg { Who = who, Id = r.Str(), Path = r.Str(), State = r.Str(), Ints = new List<KeyValuePair<string, int>>() };
            for (int i = 0, n = r.U8(); i < n; i++) m.Ints.Add(new KeyValuePair<string, int>(r.Str(), r.I32()));
            if (ApplyEat(m)) return;
            // Plateau pas encore la (Keijo d'ici plus lent, pose en route), bouton pas encore allume (burger pas encore
            // deballe ici).
            m.Until = Time.realtimeSinceStartup + 45f;
            eatPending.Add(m);
        }

        // Etat du bouton rejoue ici, ce qui touche au joueur coupe un moment (il ne mange pas, lui).
        static bool ApplyEat(EatMsg m)
        {
            GameObject go = Live(m.Id);
            if (go == null) return false;
            Transform t = m.Path.Length == 0 ? go.transform : go.transform.Find(m.Path);
            PlayMakerFSM f = t != null ? Game.FsmOn(t.gameObject, "Button") : null;
            if (f == null || !t.gameObject.activeInHierarchy || string.IsNullOrEmpty(f.ActiveStateName)) return false;
            string was = f.ActiveStateName;
            if (was == m.State) { Log.Info("magasin : repas " + m.Id + "/" + m.Path + " deja " + m.State + " ici"); return true; }
            foreach (KeyValuePair<string, int> kv in m.Ints) { FsmInt v = f.FsmVariables.FindFsmInt(kv.Key); if (v != null) v.Value = kv.Value; }
            List<FsmStateAction> muted = MuteFsm(f);
            applying = true; Replay.Depth++;
            try { Game.SetState(f, m.State); }
            finally { applying = false; Replay.Depth--; }
            eatMuted.Add(new KeyValuePair<float, List<FsmStateAction>>(Time.realtimeSinceStartup + 1.5f, muted));
            Log.Info("magasin : repas de " + PlayerName(m.Who) + " rejoue ici : " + m.Id + "/" + (m.Path.Length > 0 ? m.Path : go.name) + " " + was + " -> " + f.ActiveStateName
                     + " (" + muted.Count + " actions coupees)");
            return true;
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
            else if (kind == K_Served) OnServed(who, r);
            else if (kind == K_Eat) OnEat(who, r);
            else if (kind == K_FleaLists || kind == K_FleaShelf) { if (!Session.IsHost && who == 0) OnFleaPrices(kind, r); }
            else if (kind == K_FleaAsk) { if (Session.IsHost) { fleaReplyAt = Time.realtimeSinceStartup + 0.5f; Log.Info("magasin : puces : " + PlayerName(who) + " demande le tirage de l'hote"); } }
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
            // Rejeu precedent pas fini a cette caisse (deux achats coup sur coup) : la table tient le panier du
            // premier acheteur, pas celui du joueur local -- c'est la sauvegarde du premier qui sera remise.
            Pending keep = null;
            foreach (Pending x in pending) if (x.R == reg) keep = x;
            if (keep != null)
            {
                pending.Remove(keep);
                keep.Until = Time.realtimeSinceStartup + 20f;
                foreach (KeyValuePair<FsmFloat, float> kv in keep.Floats) kv.Key.Value = 0f;
            }
            else
            {
                keep = new Pending
                {
                    R = reg, Carried = new Hashtable(carried), Until = Time.realtimeSinceStartup + 20f,
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
            }
            foreach (KeyValuePair<string, float> kv in floats) { FsmFloat v = fv.FindFsmFloat(kv.Key); if (v != null) v.Value = kv.Value; }
            carried.Clear();
            for (int i = 0; i < keys.Count; i++) carried[keys[i]] = qtys[i];
            fv.GetFsmInt("BagStuff").Value = bagStuff;
            // 'Purchase' de Fleetari vient apres le sac : coupe (avec l'ecran et les statistiques) jusqu'a la fin.
            List<FsmStateAction> muted = MuteFsm(reg.Fsm);
            if (keep.Muted == null) keep.Muted = muted; else keep.Muted.AddRange(muted);
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
                        // = BagCreator 'Create' (COPY) : 'Activate bag' le sort a la racine (SetParent null) et l'active
                        // -- sous ShoppingBagSpawn (toujours eteint) il ne demarrerait jamais : pas d'ID, invisible a
                        // Props et Consume, jamais montre. Puis 'Copy contents 2' : panier recopie dans ses listes,
                        // creees par l'Awake des proxies a l'activation. Fige tant que le magasin n'est pas charge ici.
                        Loosen(bag, f.gameObject);
                        System.Collections.ArrayList k = ListOn(bag, "Keys"), v = ListOn(bag, "Values");
                        if (k != null && v != null)
                        {
                            k.Clear(); v.Clear();
                            for (int i = 0; i < keys.Count; i++) { k.Add(keys[i]); v.Add(qtys[i]); }
                        }
                        Log.Info("magasin : sac " + bag.name + " cree ici sans la caisse (" + (bag.transform.parent != null ? "sous " + bag.transform.parent.name : "racine")
                                 + (k != null ? "" : ", listes absentes : sac vide") + ")");
                    }
                }
            }
            // Gros articles : la liste 'Separates' de l'inventaire (proxy jamais eveille si le magasin n'a pas ete
            // charge depuis le chargement de la partie : a defaut, un distributeur a ce nom dans {Spawners}).
            System.Collections.ArrayList separates = ListOn(Var(f, "Inventory"), "Separates");
            GameObject spawners = Var(f, "Spawners"), point = Var(f, "SpawnPoint");
            if (spawners != null)
                for (int i = 0; i < keys.Count; i++)
                {
                    if (qtys[i] <= 0 || separates != null && !separates.Contains(keys[i])) continue;
                    PlayMakerFSM pf = Game.FsmOn(spawners, keys[i]);
                    for (int j = 0; j < qtys[i] && pf != null; j++) spawnSteps.Add(new SpawnStep { Fsm = pf, Point = point, Area = f.gameObject, Event = "SPAWNITEM" });
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
            c.ReplayOrder = false;
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
            // Puces : toujours a la main. La caisse au repos ('Wait player'/'Wait button') est justement celle ou le
            // joueur local a peut-etre choisi des objets (Total, 'Bought', TriggerFlea 'Added') : le rejeu les
            // ecrasait, puis 'Delay' et 'State 4' remettaient tout a zero -- objets choisis caches pour de bon,
            // semaines de location perdues. Et un clic local (PURCHASE) coupait la boucle 'Spawn product'.
            // A la carte : Jouni porterait l'assiette a la table du marqueur d'ICI ; l'assiette vient de la pose de
            // l'acheteur.
            if (c.Kind == "puces" || c.Kind == "carte") { Emulate(it); return true; }
            bool on = f.gameObject.activeInHierarchy;
            if (!on && Emulate(it)) return true;   // eteint : ce qui doit exister partout, fait a la main
            if (!on || !Ready(c))
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
            if (c.Kind == "bar") HideOrders(c);
            if (c.Kind == "resto") c.ReplayOrder = true;   // 'State 4' : Keijo mis au service s'il ne prend pas ORDER
            applying = true; Replay.Depth++;
            try { Game.SetState(f, it.State); }
            finally { applying = false; Replay.Depth--; }
            Log.Info("magasin : " + c.Label + " de " + PlayerName(it.Who) + " rejoue (" + c.Kind + " " + Describe(f) + ", " + idle + " -> " + it.State + ", achat " + it.Serial
                     + ", " + (c.Muted != null ? c.Muted.Count : 0) + " actions coupees : " + MutedNames(c) + " ; " + Wallet.State() + ")");
            return true;
        }

        // Bar : carte cachee le temps du service rejoue (ce que fait 'State 6' a une commande, que le rejeu saute en
        // entrant dans 'Pay') ; remise par UnmuteCounter au retour au repos ('State 3'), ou au plus tard 120 s apres.
        static void HideOrders(Counter c)
        {
            c.MutedUntil = c.MutedAt + 120f;   // service du plat (cuisine, micro-ondes) : plus long qu'une biere
            Transform ol = c.Fsm.transform.root.Find("Stuff/LOD/ActivateBar/OrderList");
            if (ol == null || !ol.gameObject.activeSelf) return;   // deja cachee (par le jeu, ou un rejeu en cours)
            ol.gameObject.SetActive(false);
            c.Hidden = ol.gameObject;
            Log.Info("magasin : carte du bar cachee le temps du service rejoue");
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
            if (c.Kind == "resto" || c.Kind == "carte")
            {
                // Rien a faire tout de suite : le plateau ou l'assiette est pose ici quand le serveur de l'acheteur le
                // pose (K_Served), de la meme prefab, au meme endroit.
                string food = "?";
                foreach (var x in it.V.S) if (x.Key == "Event") food = x.Value;
                Log.Info("magasin : " + c.Label + " de " + PlayerName(it.Who) + " (" + food + ") pas rejoue par l'automate ici ("
                         + (c.Kind == "carte" ? "table de l'acheteur" : !c.Fsm.gameObject.activeInHierarchy ? "caisse eteinte" : "caisse ou Keijo occupe")
                         + ") : pose d'apres son serveur");
                return true;
            }
            if (c.Kind == "bar")
            {
                // Cigarettes : distributeur a compteur (le meme que le magasin), toujours actif. Verre, shot, cafe,
                // assiette : rien a garder, seulement vus au bar -- pas faits ici.
                string ev = null;
                foreach (var x in it.V.S) if (x.Key == "EventName") ev = x.Value;
                if (ev == "CIGARETTES")
                {
                    PlayMakerFSM sp = FsmOnVar(c.Fsm, "Spawner", "Cigarettes");
                    if (sp != null)
                    {
                        sp.SendEvent("SPAWNPUB");
                        // Sous le point du comptoir (eteint avec le bar) : a la racine, fige jusqu'a ce que le bar soit
                        // charge ici (sinon jamais d'ID : le paquet empoche par l'acheteur restait ici sur le comptoir).
                        GameObject pack = Var(sp, "New");
                        Loosen(pack, c.Fsm.gameObject);
                        Log.Info("magasin : cigarettes du bar creees ici sans le bar (" + PlayerName(it.Who) + (pack != null ? ", " + pack.name : "") + ")");
                    }
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
                System.Collections.ArrayList mine = ListOn(inv, "Bought");   // selection du joueur local (pas payee)
                int n = 0, dropped = 0;
                for (int i = 0; i < bought.Count && i < items.Count; i++)
                {
                    if (!(bought[i] is bool) || !(bool)bought[i]) continue;
                    if (purchased != null && i < purchased.Count && purchased[i] is bool && (bool)purchased[i]) continue;   // deja vendu ici
                    // = 'Spawn product' : objet sorti de l'inventaire (FleaMarketProducts/Disable, eteint) a la racine
                    // (SetParent null), pose au comptoir, active ; rayon cache. Fige tant que le marche n'est pas charge
                    // ici (il tomberait a travers le comptoir eteint) ou que Props ne l'a pas deplace.
                    var go = items[i] as GameObject;
                    if (go == null) continue;
                    go.transform.parent = null;
                    if (point != null) go.transform.position = point.transform.position + Vector3.up * (0.1f * n);
                    go.SetActive(true);
                    Freeze(go, c.Fsm.gameObject);
                    Consume.Soon(go);
                    if (purchased != null && i < purchased.Count) purchased[i] = true;
                    var sh = shelf != null && i < shelf.Count ? shelf[i] as GameObject : null;
                    if (sh != null) sh.SetActive(false);
                    n++;
                    // Choisi ici aussi : retire de la selection locale (sinon paye une 2e fois et repris a l'acheteur).
                    if (mine != null && i < mine.Count && mine[i] is bool && (bool)mine[i]) { Deselect(c, i, mine); dropped++; }
                }
                if (dropped > 0 && c.Fsm.gameObject.activeInHierarchy && AtRest(c.Fsm))
                {
                    // Total affiche et TotalFinal recalcules par la caisse, comme apres un clic sur un objet.
                    applying = true; Replay.Depth++;
                    try { c.Fsm.SendEvent("PURCHASE"); }
                    finally { applying = false; Replay.Depth--; }
                }
                Log.Info("magasin : " + n + " objets des puces de " + PlayerName(it.Who) + " sortis ici a la main (comptoir "
                         + (c.Fsm.gameObject.activeInHierarchy ? "allume" : "eteint") + (dropped > 0 ? ", " + dropped + " retires de la selection locale" : "") + ")");
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

        // Puces : l'objet de rang i, achete par un autre, etait aussi choisi par le joueur local (TriggerFlea 'Buy'
        // de meme ID : Added, prix ajoute au Total de la caisse, rang i vrai dans 'Bought'). Defait comme 'Subtract'
        // (clic droit) : rang i hors de 'Bought', Added faux, prix retire du total. L'objet du rayon n'est pas
        // retouche : son rayon est cache (vendu). Releve de tous les automates : seulement dans ce cas rare.
        static void Deselect(Counter c, int i, System.Collections.ArrayList mine)
        {
            mine[i] = false;
            foreach (Object o in Resources.FindObjectsOfTypeAll(typeof(PlayMakerFSM)))
            {
                var t = (PlayMakerFSM)o;
                if (t.hideFlags != HideFlags.None || t.FsmName != "Buy" || t.gameObject.name != "TriggerFlea" || Var(t, "CashRegister") != c.Fsm.gameObject) continue;
                FsmInt id = t.FsmVariables.FindFsmInt("ID");
                if (id == null || id.Value != i) continue;
                FsmBool added = t.FsmVariables.FindFsmBool("Added");
                if (added == null || !added.Value) return;
                added.Value = false;
                FsmFloat price = t.FsmVariables.FindFsmFloat("Price");
                float p = price != null ? price.Value : 0f;
                FsmFloat total = c.Fsm.FsmVariables.FindFsmFloat("Total"), final = c.Fsm.FsmVariables.FindFsmFloat("TotalFinal");
                if (total != null) total.Value = total.Value - p < 0.005f ? 0f : total.Value - p;
                if (final != null) final.Value = final.Value - p < 0.005f ? 0f : final.Value - p;
                FsmString pn = t.FsmVariables.FindFsmString("ProductNameString");
                Log.Info("magasin : " + (pn != null ? pn.Value : "objet " + i) + " retire de la selection locale (achete par un autre, " + p + " mk)");
                return;
            }
        }

        // ================================================================ prix et rayons des puces (tirage de l'hote)
        // FleaMarketProducts :: Creator tire au hasard chez chacun, au chargement et a chaque NEWPRODUCTS (nouvelle
        // semaine) : les prix ('Stop' : RandomInt -> liste des prix), les objets mis en rayon ('Is available?' :
        // SendRandomEvent) et la place et l'orientation de leur presentoir ('State 3' : ArrayListGetRandom des
        // emplacements, RandomBool). L'invite voyait d'autres objets a d'autres prix ; les achats rejoues par rang
        // ('Bought') visaient alors un objet que l'autre ne voyait pas en rayon.
        //  - l'hote fait reference : ses listes de valeurs (prix, achetes...) -- sauf 'Bought', la selection du joueur
        //    local -- et ses presentoirs (liste 'Shelf' : allume, position, rotation) partent quand ils changent (releve
        //    toutes les 3 s, Creator au repos), et a la demande d'un invite (20 s apres son chargement) ;
        //  - l'invite les garde : recus pendant un tirage local, appliques a sa fin ; et chaque fois que son propre
        //    Creator finit un tirage (retour a 'State 1'), le tirage de l'hote est remis (et redemande). Les TriggerFlea
        //    allumes et libres relisent alors leur prix ('Init').
        // Purchase : U8 joueur, U8 K_FleaLists, Str liste, U16 n, (U8 type, valeur) x n -- une liste par message ;
        //            U8 joueur, U8 K_FleaShelf, U16 total, U16 debut, U8 n, (U8 allume|2 absent, [Vec pos, Quat rot]) x n ;
        //            U8 joueur, U8 K_FleaAsk (invite -> hote).
        const int K_FleaLists = 2, K_FleaShelf = 3, K_FleaAsk = 4;
        class ShelfPose { public bool On; public Vector3 Pos; public Quaternion Rot; }
        static GameObject fleaDb;
        static PlayMakerFSM fleaCreator;
        static float fleaNext, fleaAskAt = -1, fleaReplyAt = -1, fleaNextSig;
        static string fleaSentSig;
        static bool fleaRolling, fleaPending;
        static int fleaApplied;
        static readonly Dictionary<string, List<object>> fleaHostLists = new Dictionary<string, List<object>>();
        static ShelfPose[] fleaHostShelf;

        static float fleaRetry;

        static bool FleaResolve()
        {
            if (fleaDb != null) return fleaCreator != null;
            if (Time.realtimeSinceStartup < fleaRetry) return false;
            fleaRetry = Time.realtimeSinceStartup + 10f;
            fleaDb = Game.FindAny("FleaMarketProducts");
            if (fleaDb == null) return false;
            fleaCreator = Game.FsmOn(fleaDb, "Creator");
            Log.Info("magasin : puces : listes " + FleaListNames() + (fleaCreator != null ? "" : ", Creator absent"));
            return fleaCreator != null;
        }

        static string FleaListNames()
        {
            var sb = new System.Text.StringBuilder();
            foreach (PlayMakerArrayListProxy p in fleaDb.GetComponents<PlayMakerArrayListProxy>())
            {
                ArrayList a = p._arrayList;
                sb.Append(p.referenceName).Append('(').Append(a != null ? a.Count : -1).Append(a != null && a.Count > 0 && a[0] != null ? " " + a[0].GetType().Name : "").Append(") ");
            }
            return sb.ToString().TrimEnd();
        }

        static bool FleaAtRest() { string s = fleaCreator != null ? fleaCreator.ActiveStateName : null; return string.IsNullOrEmpty(s) || s == "State 1"; }

        // Listes de valeurs du tirage : nombres, textes, drapeaux ; ni les objets (Items, Shelf) ni la selection locale.
        static List<PlayMakerArrayListProxy> FleaValueLists()
        {
            var r = new List<PlayMakerArrayListProxy>();
            foreach (PlayMakerArrayListProxy p in fleaDb.GetComponents<PlayMakerArrayListProxy>())
            {
                string n = p.referenceName ?? "";
                ArrayList a = p._arrayList;
                if (n.Length == 0 || n == "Bought" || n == "Items" || n == "Shelf" || a == null || a.Count == 0 || a.Count > 200) continue;
                bool values = true;
                foreach (object o in a) if (!(o is int || o is float || o is string || o is bool)) { values = false; break; }
                bool dup = false;
                foreach (PlayMakerArrayListProxy q in r) if (q.referenceName == n) dup = true;
                if (values && !dup) r.Add(p);
            }
            return r;
        }

        static string FleaSig()
        {
            var sb = new System.Text.StringBuilder();
            foreach (PlayMakerArrayListProxy p in FleaValueLists())
            {
                sb.Append(p.referenceName).Append(':');
                foreach (object o in p._arrayList) sb.Append(o).Append(',');
                sb.Append(';');
            }
            ArrayList shelf = ListOn(fleaDb, "Shelf");
            if (shelf != null)
                foreach (object o in shelf)
                {
                    var go = o as GameObject;
                    if (go == null) { sb.Append("- "); continue; }
                    Vector3 v = go.transform.position;
                    sb.Append(go.activeSelf ? '1' : '0').Append(Mathf.RoundToInt(v.x * 20f)).Append(',').Append(Mathf.RoundToInt(v.y * 20f)).Append(',').Append(Mathf.RoundToInt(v.z * 20f)).Append(' ');
                }
            return sb.ToString();
        }

        // Empreinte courte du tirage (journal, comparaison entre les deux instances).
        static string FleaHash()
        {
            uint h = 2166136261;
            foreach (char c in FleaSig()) { h ^= c; h *= 16777619; }
            return h.ToString("x8");
        }

        static void FleaPricesStep(float now)
        {
            if (!PlayerSync.InGame || !FleaResolve()) return;
            if (Session.IsHost)
            {
                if (Session.RemoteCount == 0 || !FleaAtRest()) return;
                bool asked = fleaReplyAt > 0 && now >= fleaReplyAt;
                if (!asked && now < fleaNextSig) return;
                fleaNextSig = now + 3f;
                string sig = FleaSig();
                if (!asked && sig == fleaSentSig) return;
                fleaReplyAt = -1;
                string why = asked ? "demande" : fleaSentSig == null ? "premier envoi" : "change";
                fleaSentSig = sig;
                FleaSend(why);
                return;
            }
            bool rest = FleaAtRest();
            if (fleaRolling && rest)
            {
                // Tirage local fini (chargement, nouvelle semaine) : celui de l'hote est remis, et redemande.
                if (fleaHostLists.Count > 0 || fleaHostShelf != null) ApplyFlea("tirage local remplace par celui de l'hote");
                FleaAsk("tirage local fini");
            }
            fleaRolling = !rest;
            if (rest && fleaPending) ApplyFlea("recu pendant un tirage local");
            if (fleaAskAt > 0 && now >= fleaAskAt) { fleaAskAt = -1; FleaAsk("arrivee"); }
        }

        static void FleaAsk(string why)
        {
            Session.SendAll(new NetWriter(Msg.Purchase).U8(Session.LocalId).U8(K_FleaAsk), true);
            Log.Info("magasin : puces : tirage demande a l'hote (" + why + ")");
        }

        static void FleaSend(string why)
        {
            int nl = 0, ns = 0;
            foreach (PlayMakerArrayListProxy p in FleaValueLists())
            {
                ArrayList a = p._arrayList;
                var w = new NetWriter(Msg.Purchase).U8(Session.LocalId).U8(K_FleaLists).Str(p.referenceName).U16(a.Count);
                foreach (object o in a)
                {
                    if (o is int) w.U8(0).I32((int)o);
                    else if (o is float) w.U8(1).F32((float)o);
                    else if (o is string) w.U8(2).Str((string)o);
                    else w.U8(3).Bool((bool)o);
                }
                if (w.Length > 1100) { Log.Warn("magasin : puces : liste " + p.referenceName + " trop grosse (" + w.Length + " o)"); continue; }
                Session.SendAll(w, true);
                nl++;
            }
            ArrayList shelf = ListOn(fleaDb, "Shelf");
            int total = shelf != null ? Mathf.Min(shelf.Count, 1000) : 0;
            for (int start = 0; start < total; start += 24)
            {
                int n = Mathf.Min(24, total - start);
                var w = new NetWriter(Msg.Purchase).U8(Session.LocalId).U8(K_FleaShelf).U16(total).U16(start).U8(n);
                for (int i = start; i < start + n; i++)
                {
                    var go = shelf[i] as GameObject;
                    if (go == null) { w.U8(2); continue; }
                    w.U8(go.activeSelf ? 1 : 0).Vec(go.transform.position).Quat(go.transform.rotation);
                    if (go.activeSelf) ns++;
                }
                Session.SendAll(w, true);
            }
            Log.Info("magasin : puces : tirage de l'hote envoye (" + why + ", " + nl + " listes, " + ns + "/" + total + " presentoirs allumes, empreinte " + FleaHash() + ")");
        }

        static void OnFleaPrices(int kind, NetReader r)
        {
            if (!FleaResolve()) return;
            if (kind == K_FleaLists)
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
                    else items.Add(r.Bool());
                }
                fleaHostLists[name] = items;
            }
            else
            {
                int total = r.U16(), start = r.U16(), n = r.U8();
                if (fleaHostShelf == null || fleaHostShelf.Length != total) fleaHostShelf = new ShelfPose[total];
                for (int i = start; i < start + n; i++)
                {
                    int on = r.U8();
                    if (on == 2) continue;
                    var p = new ShelfPose { On = on == 1, Pos = r.Vec(), Rot = r.Quat() };
                    if (i < total) fleaHostShelf[i] = p;
                }
            }
            if (FleaAtRest()) ApplyFlea(null); else fleaPending = true;
        }

        // Tirage de l'hote recopie ici (listes, presentoirs) ; prix relus par les TriggerFlea si une liste a change.
        static void ApplyFlea(string why)
        {
            fleaPending = false;
            int lists = 0, poses = 0;
            foreach (KeyValuePair<string, List<object>> kv in fleaHostLists)
            {
                ArrayList a = ListOn(fleaDb, kv.Key);
                if (a == null || kv.Key == "Bought") continue;
                bool same = a.Count == kv.Value.Count;
                for (int i = 0; same && i < a.Count; i++) same = Equals(a[i], kv.Value[i]);
                if (same) continue;
                a.Clear();
                foreach (object o in kv.Value) a.Add(o);
                lists++;
            }
            ArrayList shelf = ListOn(fleaDb, "Shelf");
            if (shelf != null && fleaHostShelf != null)
                for (int i = 0; i < shelf.Count && i < fleaHostShelf.Length; i++)
                {
                    ShelfPose p = fleaHostShelf[i];
                    var go = shelf[i] as GameObject;
                    if (p == null || go == null) continue;
                    bool moved = (go.transform.position - p.Pos).sqrMagnitude > 1e-4f || Quaternion.Angle(go.transform.rotation, p.Rot) > 0.5f;
                    if (go.activeSelf == p.On && !moved) continue;
                    go.transform.position = p.Pos; go.transform.rotation = p.Rot;
                    if (go.activeSelf != p.On) go.SetActive(p.On);
                    poses++;
                }
            int init = lists > 0 ? ReinitFleaTriggers() : 0;
            if (lists + poses > 0 && (why != null || ++fleaApplied <= 20 || fleaApplied % 20 == 0))
                Log.Info("magasin : puces : tirage de l'hote recopie" + (why != null ? " (" + why + ")" : "") + " : " + lists + " listes, " + poses + " presentoirs, "
                         + init + " prix relus, empreinte " + FleaHash());
        }

        static int ReinitFleaTriggers()
        {
            int n = 0;
            foreach (Object o in Resources.FindObjectsOfTypeAll(typeof(PlayMakerFSM)))
            {
                var t = (PlayMakerFSM)o;
                if (t.hideFlags != HideFlags.None || t.FsmName != "Buy" || t.gameObject.name != "TriggerFlea" || !t.gameObject.activeInHierarchy) continue;
                FsmBool added = t.FsmVariables.FindFsmBool("Added");
                string s = t.ActiveStateName;
                if ((added != null && added.Value) || (s != "Wait player" && s != "Wait button") || t.Fsm.GetState("Init") == null) continue;
                applying = true; Replay.Depth++;
                try { Game.SetState(t, "Init"); } finally { applying = false; Replay.Depth--; }
                n++;
            }
            return n;
        }

        // Essais 'puces-prix' (n'importe ou : les listes existent meme loin du marche). Chacun note toutes les 4 s de 22 a
        // 70 s l'empreinte du tirage, les 6 premiers elements de chaque liste et les presentoirs allumes. 35 s : l'hote
        // tire une nouvelle semaine (NEWPRODUCTS) ; 52 s : l'invite aussi, chez lui seul. Attendu : meme empreinte des deux
        // cotes a 30 s, puis quelques secondes apres 35 s (nouveau tirage de l'hote) et apres 52 s (« tirage local remplace
        // par celui de l'hote »).
        static void TestFleaPrices(float t)
        {
            if (!FleaResolve()) { if (testStep == 0 && t > 22f) { testStep = 9; Log.Info("autotest : puces-prix, FleaMarketProducts absent"); } return; }
            if (Session.IsHost && testStep == 0 && t > 35f) { testStep = 1; fleaCreator.SendEvent("NEWPRODUCTS"); Log.Info("autotest : puces-prix, l'hote tire une nouvelle semaine -> " + fleaCreator.ActiveStateName); }
            if (!Session.IsHost && testStep == 0 && t > 52f) { testStep = 1; fleaCreator.SendEvent("NEWPRODUCTS"); Log.Info("autotest : puces-prix, l'invite tire chez lui seul -> " + fleaCreator.ActiveStateName); }
            if (t > 22f && t < 71f && t - testLog >= 4f)
            {
                testLog = t;
                var sb = new System.Text.StringBuilder("autotest : puces-prix, empreinte " + FleaHash() + ", Creator " + fleaCreator.ActiveStateName);
                foreach (PlayMakerArrayListProxy p in FleaValueLists())
                {
                    sb.Append(" ; ").Append(p.referenceName).Append(':');
                    for (int i = 0; i < p._arrayList.Count && i < 6; i++) sb.Append(' ').Append(p._arrayList[i]);
                }
                ArrayList shelf = ListOn(fleaDb, "Shelf");
                int on = 0;
                if (shelf != null) foreach (object o in shelf) { var go = o as GameObject; if (go != null && go.activeSelf) on++; }
                sb.Append(" ; presentoirs allumes ").Append(on).Append('/').Append(shelf != null ? shelf.Count : 0);
                Log.Info(sb.ToString());
            }
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
            SetId(go, id);
            Log.Info("magasin : " + go.name + " cree " + c.Label + ", ID " + id);
        }

        // ID d'achat pose sur l'objet : variable 'ID' de son automate Use, a defaut du premier (ajoutee au besoin).
        static void SetId(GameObject go, string id)
        {
            PlayMakerFSM use = Game.FsmOn(go, "Use");
            if (use == null) { PlayMakerFSM[] all = go.GetComponents<PlayMakerFSM>(); if (all.Length > 0) use = all[0]; }
            if (use == null) return;
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
        }

        // Consume : objet de comptoir nomme ici (sa fin "vide" -- barquette mangee -- est suivie aussi).
        public static bool Named(string id) { return id != null && named.Contains(id); }

        // ---------------------------------------------------------------- actions coupees pendant un rejeu
        // Ce qui toucherait au joueur qui rejoue : ecriture d'une globale Player* ou GUI* (argent, sous-titres,
        // corps), statistiques personnelles, objets sous PLAYER ou GUI (jurons, main). Refait a chaque rejeu (rare),
        // jamais garde : la cible d'une action peut changer. Seules les cibles FIXES comptent (objet donne tel quel,
        // globale, variable de l'automate qu'aucune de ses actions n'ecrit) ; les sorties (storeObject du verre cree
        // par 'Spawn beer') et les variables que l'automate remplit lui-meme ({Item} des puces, lu dans 'Items')
        // visent ce qu'elles visaient au dernier passage -- le verre ou l'objet que le joueur a encore EN MAIN :
        // les couper empechait de creer les verres et de sortir les objets des autres pour toute la partie.
        static List<FsmStateAction> PersonalWrites(PlayMakerFSM f)
        {
            var l = new List<FsmStateAction>();
            try
            {
                HashSet<string> written = Written(f);
                foreach (FsmState st in f.Fsm.States)
                    foreach (FsmStateAction a in st.Actions)
                        if (a != null && !(a is ModHook) && !(a is Made) && Personal(f, a, written)) l.Add(a);
            }
            catch { }
            return l;
        }

        // Variables objet ecrites par les actions de l'automate : champs de sortie (storeObject, storeResult...)
        // et resultats des listes (FsmVar : ArrayListGet 'result', ArrayListGetRandom...).
        static HashSet<string> Written(PlayMakerFSM f)
        {
            var w = new HashSet<string>();
            foreach (FsmState st in f.Fsm.States)
                foreach (FsmStateAction a in st.Actions)
                {
                    if (a == null) continue;
                    foreach (FieldInfo fi in a.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
                    {
                        object v = fi.GetValue(a);
                        var g = v as FsmGameObject;
                        if (g != null && g.UseVariable && OutputField(fi.Name) && !string.IsNullOrEmpty(g.Name)) w.Add(g.Name);
                        var fv = v as FsmVar;
                        if (fv != null && fv.useVariable && !string.IsNullOrEmpty(fv.variableName)) w.Add(fv.variableName);
                    }
                }
            return w;
        }

        // Cible qui change en cours de partie : variable sans nom (sortie jetee), ou variable de l'automate qu'il
        // ecrit lui-meme.
        static bool Moving(PlayMakerFSM f, FsmGameObject g, HashSet<string> written)
        {
            if (g == null || !g.UseVariable) return false;
            if (string.IsNullOrEmpty(g.Name)) return true;
            return written.Contains(g.Name) && Game.LocalVar(f, g.Name);
        }

        static bool Personal(PlayMakerFSM f, FsmStateAction a, HashSet<string> written)
        {
            string tn = a.GetType().Name;
            // Tests et conversions lisent leur premiere variable (BoolTest boolVariable...) : jamais coupes pour ca.
            bool reads = tn.Contains("Test") || tn.Contains("Compare") || tn.Contains("Changed") || tn.Contains("Switch") || tn.StartsWith("Bool") || tn.StartsWith("Convert");
            foreach (FieldInfo fi in a.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                object v = fi.GetValue(a);
                var nv = v as NamedVariable;
                if (nv != null && !reads && WriteFields.Contains(fi.Name) && nv.UseVariable
                    && (nv.Name.StartsWith("Player") || nv.Name.StartsWith("GUI")) && !Game.LocalVar(f, nv.Name)) return true;
                if (tn.StartsWith("Get")) continue;   // lit seulement (distance au joueur, variable d'un autre automate)
                if (OutputField(fi.Name)) continue;   // ce que l'action produit (objet cree) : pas sa cible
                GameObject go = null;
                var g = v as FsmGameObject;
                if (g != null) { if (!Moving(f, g, written)) go = g.Value; }
                else if (v is FsmOwnerDefault)
                {
                    var od = (FsmOwnerDefault)v;
                    if (od.OwnerOption != OwnerDefaultOption.UseOwner && !Moving(f, od.GameObject, written)) go = od.GameObject.Value;
                }
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

        static string MutedNames(Counter c)
        {
            if (c.Muted == null) return "";
            var sb = new System.Text.StringBuilder();
            foreach (FsmStateAction a in c.Muted) sb.Append(a.State != null ? a.State.Name : "?").Append('/').Append(a.GetType().Name).Append(' ');
            return sb.ToString();
        }

        static void UnmuteCounter(Counter c)
        {
            if (c.Hidden != null) { c.Hidden.SetActive(true); c.Hidden = null; Log.Info("magasin : carte du bar rendue (" + (c.Fsm != null ? c.Fsm.ActiveStateName : "?") + ", " + Wallet.State() + ")"); }
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
        public static string TestBuy(string product, int qty) { return TestBuy(product, qty, null, 0); }

        // 'extra' : un 2e produit dans le meme panier (gros article : caisse de biere...), hors BagStuff.
        static string TestBuy(string product, int qty, string extra, int extraQty)
        {
            Register r = StoreRegister();
            if (r == null) return "aucune caisse de magasin";
            Hashtable carried = CarriedOf(r.Fsm);
            if (carried == null) return "pas de panier";
            carried[product] = qty;
            if (!string.IsNullOrEmpty(extra) && extraQty > 0) carried[extra] = extraQty;
            r.Fsm.FsmVariables.GetFsmInt("BagStuff").Value = qty;
            r.Fsm.FsmVariables.GetFsmFloat("PriceTotal").Value = 10f * (qty + extraQty);
            Game.SetState(r.Fsm, "Check money");
            return "achat de " + product + " x" + qty + (extraQty > 0 ? " et " + extra + " x" + extraQty : "") + " a " + r.Key;
        }

        // Caisse du magasin PSK (Fleetari a aussi un chemin en /Store/).
        static Register StoreRegister()
        {
            Register any = null;
            foreach (Register r in registers.Values)
            {
                if (r.Fsm == null || !r.Key.Contains("/Store/")) continue;
                if (r.Key.StartsWith("PERAPORTTI")) return r;
                any = r;
            }
            return any;
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
        // [Test] Autotest=panier : les deux joueurs devant la caisse PSK (caisse allumee chez chacun). A 28 s l'invite
        //   met TestProduit (Sausages) x3 dans SON panier (comme en rayon, PriceTotal +30) ; a 32 s l'hote achete
        //   TestProduit x2 ; chacun note de 30 a 60 s le panier que la caisse lit A CE MOMENT (proxy Carried),
        //   PriceTotal et l'etat Stock du produit. Attendu chez l'invite : « rejoue », puis son panier x3 et
        //   PriceTotal 30 a nouveau dans la table lue par la caisse (pas x0 : 'Reset purchase' change de table) ;
        //   chez l'hote, le panier des autres = 3 apres son achat.
        // [Test] Autotest=sac-loin : l'hote devant la caisse PSK, l'invite loin (TestPos ailleurs : caisse eteinte chez
        //   lui). A 32 s l'hote achete TestProduit x2 et [Test] TestGros (Beer : caisse de biere, gros article) x1 ;
        //   chacun note toutes les 5 s de 25 a 70 s les sacs et caisses de biere actifs (parent, fige ou non) et les
        //   ID que Props suit ; de 45 a 50 s l'hote promene le sac (Props.TestCarry). Attendu chez l'invite : « sac
        //   shoppingbagN cree ici sans la caisse (racine) », sac et caisse a la racine, figes, suivis par Props sous
        //   les memes ID que chez l'hote, puis « libere (deplace) » pour le sac quand l'hote le deplace.
        // [Test] Autotest=puces : les deux joueurs devant le comptoir du marche aux puces (TestPos, jour d'ouverture :
        //   TriggerFlea actifs). A 26 s l'invite choisit l'objet de rang [Test] TestObjet (0 : boitier PC) comme un clic
        //   gauche (Added, Total, 'Bought') ; a 30 s l'hote choisit le meme et paie ('Check money', comme le clic sur
        //   la caisse). Chacun note de 28 a 50 s le Total, le nombre d'objets choisis et ou est l'objet. Attendu chez
        //   l'invite : « 1 objets des puces de X sortis ici a la main (comptoir allume, 1 retires de la selection
        //   locale) », puis Total 0, choisis 0 ; l'objet a la racine pres du comptoir des deux cotes, pas fige.
        // [Test] Autotest=resto : les deux joueurs devant le comptoir burger du restaurant PSK (TestPos pres de
        //   WaitingPointBurger, restaurant charge chez chacun). A 35 s l'hote choisit la ligne [Test] TestObjet (1
        //   hamburger par defaut -- plateau burger + frites + soda ; 2 jumbo, 3 bacon, 4 poulet, 5 vege) comme un clic
        //   (OrderTriggers/<plat> 'Buy' -> 'Cashier'), a 37 s paie (caisse -> 'Check money', comme le clic USE).
        //   Chacun note toutes les 2 s de 30 a 110 s la caisse, Keijo (etat, OrderStage, commande servie), les
        //   plateaux (ID, position, etat de chaque bouton et maillage) et son argent. Des 55 s (des que le plateau est
        //   la) l'hote mange [Test] TestManger (EatFries par defaut) : 'Play anim' comme le clic, 'State 3' force 2 s
        //   apres si le clic n'a pas abouti. Attendu : « rejoue » puis Keijo au service chez l'invite, un plateau
        //   de meme ID au meme endroit des deux cotes, argent de l'invite inchange ; puis EatFries en State 3 et Fries
        //   eteint des deux cotes (« repas de X rejoue ici »).
        static int testStep;
        static string testBag;
        static float testLog, testEatAt;
        static Rigidbody testCup;

        public static void Test(string mode, float t)
        {
            if (mode == "resto") TestResto(t);
            else if (mode == "bar") TestBar(t);
            else if (mode == "cafe") TestCafe(t);
            else if (mode == "sac-double") TestBags(t);
            else if (mode == "panier") TestCart(t);
            else if (mode == "sac-loin") TestFar(t);
            else if (mode == "puces") TestFlea(t);
            else if (mode == "puces-prix") TestFleaPrices(t);
        }

        // Puces (essais) : comme un clic gauche sur l'objet de rang k (TriggerFlea 'Add' -> 'Cashier').
        static string FleaSelect(Counter c, int k)
        {
            System.Collections.ArrayList mine = ListOn(Var(c.Fsm, "Inventory"), "Bought");
            foreach (Object o in Resources.FindObjectsOfTypeAll(typeof(PlayMakerFSM)))
            {
                var t = (PlayMakerFSM)o;
                if (t.hideFlags != HideFlags.None || t.FsmName != "Buy" || t.gameObject.name != "TriggerFlea" || Var(t, "CashRegister") != c.Fsm.gameObject) continue;
                FsmInt id = t.FsmVariables.FindFsmInt("ID");
                if (id == null || id.Value != k) continue;
                FsmBool added = t.FsmVariables.FindFsmBool("Added");
                if (added == null || added.Value) return "deja choisi";
                added.Value = true;
                float p = t.FsmVariables.FindFsmFloat("Price").Value;
                c.Fsm.FsmVariables.FindFsmFloat("Total").Value += p;
                c.Fsm.FsmVariables.FindFsmFloat("TotalFinal").Value += p;
                if (mine != null && k < mine.Count) mine[k] = true;
                return t.FsmVariables.FindFsmString("ProductNameString").Value + " " + p + " mk";
            }
            return "TriggerFlea " + k + " introuvable";
        }

        static string FleaState(Counter c, int k)
        {
            GameObject inv = Var(c.Fsm, "Inventory");
            System.Collections.ArrayList mine = ListOn(inv, "Bought"), items = ListOn(inv, "Items");
            int n = 0;
            if (mine != null) foreach (object o in mine) if (o is bool && (bool)o) n++;
            var go = items != null && k < items.Count ? items[k] as GameObject : null;
            return "caisse [" + c.Fsm.ActiveStateName + "] Total=" + c.Fsm.FsmVariables.FindFsmFloat("Total").Value.ToString("0.##")
                   + " TotalFinal=" + c.Fsm.FsmVariables.FindFsmFloat("TotalFinal").Value.ToString("0.##") + ", choisis " + n
                   + ", objet " + k + " " + (go == null ? "?" : go.name + (go.activeInHierarchy ? " actif" : " eteint") + (go.transform.parent != null ? " sous " + go.transform.parent.name : " racine")
                   + " en " + go.transform.position.ToString("F1")) + ", figes " + frozen.Count;
        }

        static void TestFlea(float t)
        {
            Counter c = null;
            foreach (Counter x in counters.Values) if (x.Kind == "puces" && x.Fsm != null) c = x;
            if (c == null || !c.Fsm.gameObject.activeInHierarchy)
            {
                if (testStep == 0 && t > 26f) { testStep = 9; Log.Info("autotest : puces, comptoir " + (c == null ? "introuvable" : "eteint ici (TestPos devant, jour d'ouverture)")); }
                return;
            }
            int k = Config.GetInt("Test", "TestObjet", 0);
            if (!Session.IsHost && testStep == 0 && t > 26f) { testStep = 1; Log.Info("autotest : puces, l'invite choisit " + FleaSelect(c, k) + " ; " + FleaState(c, k)); }
            if (Session.IsHost && testStep == 0 && t > 30f)
            {
                testStep = 1;
                string sel = FleaSelect(c, k);
                Game.SetState(c.Fsm, "Check money");
                Log.Info("autotest : puces, l'hote achete " + sel + " -> " + c.Fsm.ActiveStateName);
            }
            if (t > 28f && t < 51f && t - testLog >= 4f) { testLog = t; Log.Info("autotest : puces, " + FleaState(c, k)); }
        }

        static void TestCart(float t)
        {
            string product = Config.Get("Test", "TestProduit", "Sausages");
            Register reg = StoreRegister();
            if (reg == null || reg.Fsm == null) { if (testStep == 0 && t > 28f) { testStep = 9; Log.Info("autotest : panier, aucune caisse PSK suivie"); } return; }
            if (!Session.IsHost && testStep == 0 && t > 28f)
            {
                testStep = 1;
                Hashtable c = CarriedOf(reg.Fsm);
                if (c != null) { c[product] = (c[product] is int ? (int)c[product] : 0) + 3; reg.Fsm.FsmVariables.GetFsmFloat("PriceTotal").Value += 30f; }
                Log.Info("autotest : panier de l'invite rempli (" + (c != null ? product + " x3" : "pas de panier") + ")");
            }
            if (Session.IsHost && testStep == 0 && t > 32f) { testStep = 1; Log.Info("autotest : " + TestBuy(product, 2)); }
            if (t > 30f && t < 61f && t - testLog >= 3f)
            {
                testLog = t;
                Hashtable c = CarriedOf(reg.Fsm);
                var sb = new System.Text.StringBuilder();
                if (c != null) foreach (DictionaryEntry e in c) if (e.Value is int && (int)e.Value != 0) sb.Append(e.Key).Append(" x").Append(e.Value).Append(' ');
                Log.Info("autotest : panier lu par la caisse [" + reg.Fsm.ActiveStateName + "] " + (c == null ? "absent" : sb.Length > 0 ? sb.ToString().TrimEnd() : "vide")
                         + ", PriceTotal=" + reg.Fsm.FsmVariables.GetFsmFloat("PriceTotal").Value.ToString("0.##") + ", rejeux en cours " + pending.Count + " ; " + Stock.State(product));
            }
        }

        static void TestFar(float t)
        {
            string gros = Config.Get("Test", "TestGros", "Beer");
            if (Session.IsHost && testStep == 0 && t > 32f)
            {
                testStep = 1;
                Log.Info("autotest : " + TestBuy(Config.Get("Test", "TestProduit", "Sausages"), 2, gros, 1));
            }
            if (Session.IsHost && testStep == 1 && t > 44f)
            {
                // Sac le plus recent (compteur le plus haut) : celui de l'achat.
                testStep = 2;
                int best = -1;
                foreach (Rigidbody rb in Object.FindObjectsOfType<Rigidbody>())
                {
                    string id = rb.name.StartsWith("shoppingbag") ? Props.ItemId(rb.gameObject) : "";
                    int num;
                    if (id.StartsWith("shoppingbag") && int.TryParse(id.Substring(11), out num) && num > best) { best = num; testBag = id; }
                }
                Log.Info("autotest : sac a promener " + (testBag ?? "introuvable"));
            }
            if (Session.IsHost && testStep == 2 && testBag != null && t > 45f && t < 50f) Props.TestCarry(testBag, t);
            if (t > 25f && t < 71f && t - testLog >= 5f)
            {
                testLog = t;
                var sb = new System.Text.StringBuilder();
                foreach (Rigidbody rb in Object.FindObjectsOfType<Rigidbody>())
                {
                    if (!rb.name.StartsWith("shoppingbag") && !rb.name.StartsWith("beercase")) continue;
                    bool fz = false;
                    foreach (Frozen x in frozen) if (x.Body == rb) fz = true;
                    sb.Append(rb.name).Append(rb.transform.parent != null ? " sous " + rb.transform.parent.name : " racine").Append(fz ? " fige" : "").Append(" ; ");
                }
                Log.Info("autotest : sac-loin, " + (sb.Length > 0 ? sb.ToString() : "aucun sac actif ; ") + "figes " + frozen.Count + " ; suivis " + Props.Ids("shoppingbag") + " " + Props.Ids("beercase"));
            }
        }

        static void TestResto(float t)
        {
            string who = Session.IsHost ? "hote" : "invite";
            Counter c = null;
            foreach (Counter x in counters.Values) if (x.Kind == "resto" && x.Fsm != null) c = x;
            if (c == null) { if (testStep == 0 && t > 30f) { testStep = 9; Log.Info("autotest : resto (" + who + ") comptoir burger introuvable (" + counters.Count + " comptoirs)"); } return; }
            bool on = c.Fsm.gameObject.activeInHierarchy;
            if (Session.IsHost && testStep == 0 && t > 35f)
            {
                testStep = 1;
                if (!on) { testStep = 9; Log.Info("autotest : resto (hote) caisse burger eteinte ici (TestPos devant WaitingPointBurger)"); }
                else
                {
                    if (!c.Hooked) HookCounter(c);
                    int line = Mathf.Clamp(Config.GetInt("Test", "TestObjet", 1), 1, 5);
                    string[] names = { "Hampurilainen", "Jumbojuusto", "Grillipekoni", "Kanahampurilainen", "Vegehampurilainen" };
                    float[] prices = { 25f, 37f, 35f, 30f, 32f };
                    string before = c.Fsm.ActiveStateName;
                    Transform tr = c.Fsm.transform.root.Find("Building/LOD100/OrderTriggers/" + names[line - 1]);
                    PlayMakerFSM buy = tr != null ? Game.FsmOn(tr.gameObject, "Buy") : null;
                    bool click = buy != null && buy.gameObject.activeInHierarchy;
                    if (click) Game.SetState(buy, "Cashier");   // Event, PriceTotal ecrits dans la caisse, PURCHASE
                    else
                    {
                        c.Fsm.FsmVariables.GetFsmString("Event").Value = "FOOD" + line;
                        c.Fsm.FsmVariables.GetFsmFloat("PriceTotal").Value = prices[line - 1];
                    }
                    Log.Info("autotest : resto (hote) commande " + names[line - 1] + (click ? " par OrderTriggers 'Cashier'" : " directement dans la caisse") + ", caisse "
                             + before + " -> " + c.Fsm.ActiveStateName + ", Event=" + StrVar(c.Fsm, "Event") + " PriceTotal=" + c.Fsm.FsmVariables.GetFsmFloat("PriceTotal").Value + " ; " + Wallet.State());
                }
            }
            if (Session.IsHost && testStep == 1 && t > 37f)
            {
                testStep = 2;
                Game.SetState(c.Fsm, "Check money");   // = clic sur la caisse (Wait button : USE)
                Log.Info("autotest : resto (hote) paie -> caisse " + c.Fsm.ActiveStateName + " ; " + Wallet.State());
            }
            if (Session.IsHost && testStep == 2 && t > 55f)
            {
                GameObject tray = null;
                for (int i = servedOrder.Count - 1; i >= 0 && tray == null; i--) { GameObject g = Live(servedOrder[i]); if (g != null && g.name.StartsWith("Tray")) tray = g; }
                if (tray == null) { if (t > 100f) { testStep = 9; Log.Info("autotest : resto (hote) aucun plateau a manger"); } }
                else
                {
                    testStep = 3; testEatAt = t; testBag = Props.ItemId(tray);
                    PlayMakerFSM bf = TestEatFsm(tray);
                    if (bf != null) Game.SetState(bf, "Play anim");   // = CLICK sur le bouton
                    Log.Info("autotest : resto (hote) mange " + testBag + "/" + Config.Get("Test", "TestManger", "EatFries") + " -> " + (bf != null ? bf.ActiveStateName : "bouton introuvable ou eteint"));
                }
            }
            if (Session.IsHost && testStep == 3 && t > testEatAt + 2f)
            {
                testStep = 4;
                GameObject tray = Live(testBag);
                PlayMakerFSM bf = tray != null ? TestEatFsm(tray) : null;
                if (bf != null && bf.ActiveStateName != "State 3" && bf.Fsm.GetState("State 3") != null)
                {
                    string was = bf.ActiveStateName;
                    Game.SetState(bf, "State 3");
                    Log.Info("autotest : resto (hote) le clic n'a pas abouti (" + was + ") : State 3 force -> " + bf.ActiveStateName);
                }
            }
            if (t > 30f && t < 111f && t - testLog >= 2f)
            {
                testLog = t;
                PlayMakerFSM k = c.Waiter;
                var sb = new System.Text.StringBuilder("autotest : resto (" + who + ") caisse [" + (on ? c.Fsm.ActiveStateName : "eteinte") + "] ");
                sb.Append(k == null ? "serveur ?" : k.gameObject.name + " [" + (k.gameObject.activeInHierarchy ? k.ActiveStateName : "eteint") + "] OrderStage=" + OrderStage(c)
                          + " commande " + c.ServeWho + "-" + c.ServeSerial);
                sb.Append(" ; plateaux : ").Append(TestTrays());
                sb.Append(" ; suivis ").Append(Props.Ids("tray")).Append(" ; rejeux en attente ").Append(queue.Count).Append(", poses attendues ").Append(waitServe.Count);
                sb.Append(" ; ").Append(Wallet.State());
                Log.Info(sb.ToString());
            }
        }

        static PlayMakerFSM TestEatFsm(GameObject tray)
        {
            Transform ef = tray.transform.Find(Config.Get("Test", "TestManger", "EatFries"));
            PlayMakerFSM bf = ef != null ? Game.FsmOn(ef.gameObject, "Button") : null;
            return bf != null && ef.gameObject.activeInHierarchy ? bf : null;
        }

        // Plateaux servis et tout Tray(Clone) de la scene (doublon, plateau sans ID) : ID, pose, enfants.
        static string TestTrays()
        {
            var seen = new List<GameObject>();
            foreach (string id in servedOrder) { GameObject g = Live(id); if (g != null && !seen.Contains(g)) seen.Add(g); }
            foreach (Rigidbody rb in Object.FindObjectsOfType<Rigidbody>()) if (rb.name.StartsWith("Tray(Clone)") && !seen.Contains(rb.gameObject)) seen.Add(rb.gameObject);
            if (seen.Count == 0) return "aucun";
            var sb = new System.Text.StringBuilder();
            foreach (GameObject g in seen)
            {
                string id = Props.ItemId(g);
                sb.Append(g.name).Append(' ').Append(id.Length > 0 ? id : "sans ID").Append(" en ").Append(g.transform.position.ToString("F2"));
                bool fz = false;
                foreach (Frozen x in frozen) if (x.Body != null && x.Body.gameObject == g) fz = true;
                if (fz) sb.Append(" fige");
                sb.Append(" {");
                foreach (Transform ch in g.GetComponentsInChildren<Transform>(true))
                {
                    if (ch == g.transform) continue;
                    PlayMakerFSM b = Game.FsmOn(ch.gameObject, "Button");
                    sb.Append(' ').Append(ch.name).Append(ch.gameObject.activeSelf ? "" : "(off)");
                    if (b != null && ch.gameObject.activeInHierarchy)
                    {
                        sb.Append('[').Append(b.ActiveStateName);
                        foreach (FsmInt v in b.FsmVariables.IntVariables) sb.Append(' ').Append(v.Name).Append('=').Append(v.Value);
                        sb.Append(']');
                    }
                }
                sb.Append(" } ");
            }
            return sb.ToString().TrimEnd();
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
