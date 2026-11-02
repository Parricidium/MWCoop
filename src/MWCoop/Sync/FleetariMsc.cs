using System.Collections.Generic;
using HutongGames.PlayMaker;
using MWCoop.Net;
using UnityEngine;

namespace MWCoop
{
    // My Summer Car (vidage v.250908-04, 19/10) : Fleetari et la poste.
    //  - ACHAT AU COMPTOIR (REPAIRSHOP/LOD/Store/ShopCashRegister/Register::Data) : 'Purchase' (paie) puis un etat par piece cochee
    //    (Q<X>) : usure tiree au hasard (RandomWear -> SATSUMA/CarSimulation/MechanicalWear::Data.Wear<X>), Database/DatabaseMotor/<X>
    //    reparee (Damaged faux, REPAIR), la piece de CARPARTS posee sur REPAIRSHOP/LOD/Store/PartSpawn, bouton Buy<X> detruit ; banc
    //    d'essai : Computer::Use.TimeBought += QDynoTimes. Rejoue par l'automate (avant), l'usure etait tiree differemment chez chacun,
    //    et chez un joueur loin (comptoir sous le LOD, eteint) l'achat etait abandonne. Maintenant l'acheteur envoie, en fin de chaine
    //    ('State 1'), les pieces vraiment sorties (Q vrai au paiement, faux a la fin -- le bloc moteur saute si des pieces sont
    //    montees) et leur usure ; les autres font la meme chose a la main, sans payer.
    //  - TRAVAUX (REPAIRSHOP/Order::Data : cases de la brochure, payees dans Sheets/ServiceBrochure/ButtonOrder::Button 'Date') :
    //    l'acheteur envoie la commande ; chez les autres l'automate Order est arrete (son compte a rebours attend que LEUR joueur
    //    s'eloigne) ; quand celui de l'acheteur passe en 'Work', les autres aussi ; a 'Reset job' il envoie les tirages (usure du
    //    moteur, couleurs des toles, peinture) et les autres les prennent.
    //  - POSTE (PostOrderBuy::Use / PostOrderBuyComputer::Use 'State 3' : paie, colis livres ensuite par Sheets/OrderList/Timer
    //    depuis STORE/Boxes) : l'acheteur envoie les noms des colis ; chez les autres ils sortent de STORE/Boxes (une seule fois).
    public static class FleetariMsc
    {
        const string RegPath = "REPAIRSHOP/LOD/Store/ShopCashRegister/Register", WearPath = "SATSUMA(557kg, 248)/CarSimulation/MechanicalWear";
        // Q<X> -> base de donnees (Database/DatabaseMotor/...), usure (MechanicalWear), bouton (REPAIRSHOP/LOD/Store/Parts/Buy...)
        static readonly string[][] Items = {
            new[] { "QAlternator", "Alternator", "WearAlternator", "BuyAlternator" },
            new[] { "QBlock", "Block", "", "BuyBlock" },
            new[] { "QCrankshaft", "Crankshaft", "WearCrankshaft", "BuyCrankshaft" },
            new[] { "QGearbox", "Gearbox", "WearGearbox", "BuyGearbox" },
            new[] { "QHeadgasket", "Headgasket", "WearHeadgasket", "BuyHeadgasket" },
            new[] { "QOilpan", "Oilpan", "", "BuyOilpan" },
            new[] { "QPiston1", "Piston1", "WearPiston1", "BuyPiston1" },
            new[] { "QPiston2", "Piston2", "WearPiston2", "BuyPiston2" },
            new[] { "QPiston3", "Piston3", "WearPiston3", "BuyPiston3" },
            new[] { "QPiston4", "Piston4", "WearPiston4", "BuyPiston4" },
            new[] { "QRockershaft", "RockerShaft", "WearRockershaft", "BuyRockershaft" },
            new[] { "QStarter", "Starter", "WearStarter", "BuyStarter" },
            new[] { "QWaterpump", "Waterpump", "WearWaterpump", "BuyWaterpump" },
            new[] { "QFuelpump", "Fuelpump", "WearFuelpump", "BuyFuelpump" },
            new[] { "QClutchDisc", "ClutchPlate", "WearClutch", "BuyClutchDisc" },
        };
        // Toles repeintes au hasard par Bodyfix (REPAIRSHOP/Jobs) : leur couleur est envoyee a la fin des travaux.
        static readonly string[] Panels = { "bootlid(Clone)", "door left(Clone)", "door right(Clone)", "fender left(Clone)", "fender right(Clone)", "hood(Clone)" };

        class Point { public string Path, Fsm, State; public System.Func<FsmStateAction> Make; public bool Hooked; }
        static readonly List<Point> points = new List<Point>();
        static float nextLook;
        static bool applying;
        static Dictionary<string, bool> paidQ;   // Q<X> au paiement (achat local en cours)
        static float paidDyno;
        static int orderer = -1;                  // joueur dont la commande de travaux est en cours (-1 : aucune, ou la notre)
        static bool leftHooked, saveHooked;
        static float waitSince = -1f, nextGuard;   // caisse en 'Wait tobacco' depuis (garde de l'encaissement)

        public static void OnLevelLoaded()
        {
            points.Clear(); nextLook = 0f; paidQ = null; orderer = -1; saveHooked = false; waitSince = -1f; nextGuard = 0f;
            if (!Game.Msc) return;
            if (!leftHooked) { leftHooked = true; Session.PlayerLeft += OnPlayerLeft; }
            points.Add(new Point { Path = RegPath, Fsm = "Data", State = "Purchase", Make = () => new Act { Run = OnLocalPaid } });
            points.Add(new Point { Path = RegPath, Fsm = "Data", State = "State 1", Make = () => new Act { Run = OnLocalSpawned, Send = true } });
            points.Add(new Point { Path = "Sheets/ServiceBrochure/ButtonOrder", Fsm = "Button", State = "State 3", Make = () => new Act { Run = OnLocalOrder, Send = true } });
            points.Add(new Point { Path = "REPAIRSHOP/Order", Fsm = "Data", State = "Work", Make = () => new Act { Run = OnLocalWork, Send = true } });
            points.Add(new Point { Path = "REPAIRSHOP/Order", Fsm = "Data", State = "Reset job", Make = () => new Act { Run = OnLocalWorkDone, Send = true } });
            points.Add(new Point { Path = "STORE/LOD/ActivateStore/PostOffice/PostOrderBuy", Fsm = "Use", State = "State 3", Make = () => new Act { Run = () => OnLocalPost(false), Send = true } });
            points.Add(new Point { Path = "STORE/LOD/ActivateStore/PostOffice/PostOrderBuyComputer", Fsm = "Use", State = "State 3", Make = () => new Act { Run = () => OnLocalPost(true), Send = true } });
            points.Add(new Point { Path = "STORE/LOD/Post Box/OrderTrigger", Fsm = "Open", State = "Car parts", Make = () => new Act { Run = () => OnLocalPostBox(false), Send = true } });
            points.Add(new Point { Path = "STORE/LOD/Post Box/OrderTrigger", Fsm = "Open", State = "Computer", Make = () => new Act { Run = () => OnLocalPostBox(true), Send = true } });
        }

        class Act : ModHook
        {
            public override string Module { get { return "magasin (Fleetari, poste)"; } }
            public System.Action Run; public bool Send;
            public override void OnEnter()
            {
                try { if (!applying && Replay.Depth == 0 && (!Send || Session.Active)) Run(); }
                catch (System.Exception e) { Replay.HookError(e); }
                Finish();
            }
        }

        public static void Update()
        {
            if (!Game.Msc || !PlayerSync.InGame) return;
            if (Time.realtimeSinceStartup >= nextGuard) { nextGuard = Time.realtimeSinceStartup + 0.5f; CashGuard(); }
            if (Time.realtimeSinceStartup < nextLook) return;
            nextLook = Time.realtimeSinceStartup + 2f;
            if (!saveHooked) HookSave();
            foreach (Point p in points)
            {
                if (p.Hooked) continue;
                PlayMakerFSM f = Game.FindFsm(p.Path, p.Fsm);
                if (f == null || !f.gameObject.activeInHierarchy) continue;   // (actions chargees a l'allumage)
                FsmState s = f.Fsm.GetState(p.State);
                if (s == null || !s.IsInitialized) continue;
                try { FsmHooks.Prepend(s, p.Make()); p.Hooked = true; Log.Info("fleetari / poste : " + p.Path + "::" + p.Fsm + " '" + p.State + "' suivi"); } catch { }
            }
        }

        // ---------------------------------------------------------------- achat au comptoir
        static void OnLocalPaid()
        {
            PlayMakerFSM reg = Game.FindFsm(RegPath, "Data");
            if (reg == null) return;
            paidQ = new Dictionary<string, bool>();
            foreach (string[] it in Items) { FsmBool q = reg.FsmVariables.FindFsmBool(it[0]); paidQ[it[0]] = q != null && q.Value; }
            FsmFloat d = reg.FsmVariables.FindFsmFloat("QDynoTimes"); paidDyno = d != null ? d.Value : 0f;
        }

        static void OnLocalSpawned()
        {
            PlayMakerFSM reg = Game.FindFsm(RegPath, "Data"), wear = Game.FindFsm(WearPath, "Data");
            if (reg == null || paidQ == null) return;
            var w = new NetWriter(Msg.Job).U8(Session.LocalId).Str("@fleetmsc");
            var got = new List<string[]>();
            foreach (string[] it in Items)
            {
                FsmBool q = reg.FsmVariables.FindFsmBool(it[0]);
                bool was; paidQ.TryGetValue(it[0], out was);
                if (was && q != null && !q.Value) got.Add(it);
            }
            w.U8(got.Count);
            var names = new System.Text.StringBuilder();
            foreach (string[] it in got)
            {
                FsmFloat wv = wear != null && it[2].Length > 0 ? wear.FsmVariables.FindFsmFloat(it[2]) : null;
                w.Str(it[0]).F32(wv != null ? wv.Value : -1f);
                names.Append(' ').Append(it[1]).Append(wv != null ? " (" + wv.Value.ToString("F1") + ")" : "");
            }
            w.F32(paidDyno);
            paidQ = null;
            if (got.Count == 0 && paidDyno <= 0f) return;
            Log.Info("fleetari : achat ici :" + names + (paidDyno > 0f ? ", banc " + paidDyno : ""));
            if (Session.Active) Session.SendAll(w, true);
        }

        // Etats de la caisse hors encaissement (attente du joueur, son et anim de fin, magasin ferme).
        static readonly HashSet<string> RegIdle = new HashSet<string> { "Wait player", "Wait button", "State 2", "State 4", "Check purchase", "State 6", "State 8", "Store Closed", "Teimo" };

        // Piece q retiree du panier de la caisse d'ici (Q faux, Total moins le prix du bouton) ; texte pour le journal.
        static string Unpick(PlayMakerFSM reg, bool idle, string q, Transform btn)
        {
            FsmBool qv = reg.FsmVariables.FindFsmBool(q);
            if (qv == null || !qv.Value) return "";
            if (!idle) return " (aussi dans le panier d'ici, caisse en " + reg.ActiveStateName + " : laissee)";
            PlayMakerFSM bf = btn != null ? Game.FsmOn(btn.gameObject, "Buy") : null;
            FsmFloat pr = bf != null ? bf.FsmVariables.FindFsmFloat("Price") : null;
            FsmFloat tot = reg.FsmVariables.FindFsmFloat("Total");
            qv.Value = false;
            if (tot != null && pr != null) tot.Value = Mathf.Max(0f, tot.Value - pr.Value);
            return " (retiree du panier d'ici, total " + (tot != null ? tot.Value.ToString("F0") : "?") + ")";
        }

        public static void OnPurchase(int who, NetReader r)
        {
            int n = r.U8();
            var got = new List<KeyValuePair<string, float>>();
            for (int i = 0; i < n; i++) got.Add(new KeyValuePair<string, float>(r.Str(), r.F32()));
            float dyno = r.F32();
            PlayMakerFSM wear = Game.FindFsm(WearPath, "Data");
            GameObject shop = Game.FindAny("REPAIRSHOP");
            Transform spawn = shop != null ? shop.transform.Find("LOD/Store/PartSpawn") : null;
            var done = new System.Text.StringBuilder();
            // Panier d'ici (audit MSC 02/11) : la meme piece cochee ici avant l'achat de l'autre (BuyX::Buy 'Play anim 2' : Q<X> vrai et
            // Total += Price dans NOTRE caisse) y restait apres la destruction du bouton -- payee une 2e fois au prochain passage, et
            // 'Spawn' arrachait la piece unique que l'autre avait peut-etre deja montee. Retiree du panier (Q<X> faux, Total moins le
            // prix du bouton), sauf caisse en plein encaissement (chaine Purchase/Spawn : l'achat d'ici suit son cours).
            // [Test] FleetariPanier=0 : comme avant.
            GameObject regGo = Config.GetInt("Test", "FleetariPanier", 1) != 0 ? Game.FindAny(RegPath) : null;   // (comptoir sous le LOD : eteint, panier garde)
            PlayMakerFSM reg = regGo != null ? Game.FsmOn(regGo, "Data") : null;

            bool regIdle = reg != null && (!reg.gameObject.activeInHierarchy || RegIdle.Contains(reg.ActiveStateName));
            applying = true; Replay.Depth++;
            try
            {
                foreach (KeyValuePair<string, float> g in got)
                {
                    string[] it = System.Array.Find(Items, x => x[0] == g.Key);
                    if (it == null) continue;
                    FsmFloat wv = wear != null && it[2].Length > 0 ? wear.FsmVariables.FindFsmFloat(it[2]) : null;
                    if (wv != null && g.Value >= 0f) wv.Value = g.Value;
                    PlayMakerFSM db = Game.FindFsm("Database/DatabaseMotor/" + it[1], "Data");
                    GameObject part = null;
                    bool mounted = false;
                    if (db != null)
                    {
                        FsmBool dmg = db.FsmVariables.FindFsmBool("Damaged"); if (dmg != null) dmg.Value = false;
                        db.SendEvent("REPAIR");
                        FsmGameObject tp = db.FsmVariables.FindFsmGameObject("ThisPart"); part = tp != null ? tp.Value : null;
                        // (piece deja montee ici : jamais arrachee de la voiture -- Installed resterait vrai, piece hors du moteur)
                        FsmBool inst = reg != null ? db.FsmVariables.FindFsmBool("Installed") : null;
                        mounted = inst != null && inst.Value;
                    }
                    if (part != null && spawn != null && !mounted)
                    {
                        part.transform.SetParent(spawn, false);
                        part.transform.localPosition = Vector3.zero; part.transform.localRotation = Quaternion.identity;
                        try { part.tag = "PART"; } catch { }
                    }
                    Transform btn = shop != null ? shop.transform.Find("LOD/Store/Parts/" + it[3]) : null;
                    string basket = reg != null ? Unpick(reg, regIdle, it[0], btn) : "";
                    if (btn != null) Object.Destroy(btn.gameObject);
                    done.Append(' ').Append(it[1]).Append(part == null ? " (piece introuvable)" : mounted ? " (montee ici, laissee)" : "").Append(basket);
                }
                if (dyno > 0f && shop != null)
                {
                    Transform comp = shop.transform.Find("LOD/Dyno/dyno_computer/Computer");
                    PlayMakerFSM cu = comp != null ? Game.FsmOn(comp.gameObject, "Use") : null;
                    FsmFloat tb = cu != null ? cu.FsmVariables.FindFsmFloat("TimeBought") : null;
                    if (tb != null) tb.Value += dyno;
                    done.Append(" banc +").Append(dyno);
                }
            }
            finally { applying = false; Replay.Depth--; }
            Hud.Toast(Shop.PlayerName(who) + Lang.T(" a acheté des pièces chez Fleetari", " bought parts at Fleetari's"));
            Log.Info("fleetari : achat de #" + who + " fait ici :" + done + (spawn == null ? " (comptoir introuvable)" : ""));
        }

        // ---------------------------------------------------------------- travaux
        static readonly HashSet<string> OrderSkip = new HashSet<string>();
        static void WriteOrder(NetWriter w, PlayMakerFSM o)
        {
            FsmVariables v = o.FsmVariables;
            var bl = new List<FsmBool>(); foreach (FsmBool b in v.BoolVariables) if (!b.Name.StartsWith("UniqueTag")) bl.Add(b);
            var fl = new List<FsmFloat>(); foreach (FsmFloat f in v.FloatVariables) fl.Add(f);
            var cl = new List<FsmColor>(); foreach (FsmColor c in v.ColorVariables) cl.Add(c);
            w.U8(bl.Count); foreach (FsmBool b in bl) w.Str(b.Name).Bool(b.Value);
            w.U8(fl.Count); foreach (FsmFloat f in fl) w.Str(f.Name).F32(f.Value);
            w.U8(cl.Count); foreach (FsmColor c in cl) w.Str(c.Name).F32(c.Value.r).F32(c.Value.g).F32(c.Value.b).F32(c.Value.a);
        }
        static void ReadOrder(NetReader r, PlayMakerFSM o)
        {
            FsmVariables v = o != null ? o.FsmVariables : null;
            for (int i = 0, n = r.U8(); i < n; i++) { string k = r.Str(); bool x = r.Bool(); FsmBool t = v != null ? v.FindFsmBool(k) : null; if (t != null) t.Value = x; }
            for (int i = 0, n = r.U8(); i < n; i++) { string k = r.Str(); float x = r.F32(); FsmFloat t = v != null ? v.FindFsmFloat(k) : null; if (t != null) t.Value = x; }
            for (int i = 0, n = r.U8(); i < n; i++) { string k = r.Str(); var c = new Color(r.F32(), r.F32(), r.F32(), r.F32()); FsmColor t = v != null ? v.FindFsmColor(k) : null; if (t != null) t.Value = c; }
        }

        static void OnLocalOrder()
        {
            PlayMakerFSM o = Game.FindFsm("REPAIRSHOP/Order", "Data");
            if (o == null) return;
            orderer = -1;
            var w = new NetWriter(Msg.Job).U8(Session.LocalId).Str("@fleetordre");
            WriteOrder(w, o);
            Session.SendAll(w, true);
            Log.Info("fleetari : travaux commandes ici (" + (o.FsmVariables.FindFsmFloat("_OrderTime") != null ? o.FsmVariables.GetFsmFloat("_OrderTime").Value.ToString("F0") + " s" : "?") + ")");
        }

        public static void OnOrder(int who, NetReader r)
        {
            PlayMakerFSM o = Game.FindFsm("REPAIRSHOP/Order", "Data");
            ReadOrder(r, o);
            if (o == null) { Log.Warn("fleetari : commande de #" + who + ", REPAIRSHOP/Order introuvable ici"); return; }
            orderer = who;
            // (son compte a rebours attend que le joueur D'ICI s'eloigne : arrete, c'est celui de l'acheteur qui compte)
            o.enabled = false;
            GameObject br = Game.FindAny("REPAIRSHOP/LOD/Office/Fleetari/Brochure");
            if (br != null) br.SetActive(false);
            Hud.Toast(Shop.PlayerName(who) + Lang.T(" a commandé des travaux chez Fleetari", " ordered work at Fleetari's"));
            Log.Info("fleetari : travaux commandes par #" + who + " (automate Order arrete ici jusqu'aux travaux)");
        }

        static void OnLocalWork()
        {
            if (orderer >= 0) return;   // (commande d'un autre : c'est lui qui envoie)
            Session.SendAll(new NetWriter(Msg.Job).U8(Session.LocalId).Str("@fleettravail"), true);
            Log.Info("fleetari : travaux en cours ici, envoyes");
        }

        public static void OnWork(int who, NetReader r)
        {
            PlayMakerFSM o = Game.FindFsm("REPAIRSHOP/Order", "Data");
            if (o == null) return;
            o.enabled = true;
            applying = true; Replay.Depth++;
            try { Game.SetState(o, "Work"); }
            finally { applying = false; Replay.Depth--; }
            Log.Info("fleetari : travaux de #" + who + " faits ici aussi (" + o.ActiveStateName + ")");
        }

        static void OnLocalWorkDone()
        {
            if (orderer >= 0) { orderer = -1; return; }
            PlayMakerFSM wear = Game.FindFsm(WearPath, "Data");
            var w = new NetWriter(Msg.Job).U8(Session.LocalId).Str("@fleetfin");
            var wl = new List<FsmFloat>();
            if (wear != null) foreach (FsmFloat f in wear.FsmVariables.FloatVariables) if (f.Name.StartsWith("Wear")) wl.Add(f);
            w.U8(wl.Count); foreach (FsmFloat f in wl) w.Str(f.Name).F32(f.Value);
            w.U8(Panels.Length);
            foreach (string pn in Panels)
            {
                Renderer rd = PanelRenderer(pn);
                Color c = rd != null && rd.sharedMaterial != null ? rd.material.color : Color.clear;
                w.Str(pn).F32(c.r).F32(c.g).F32(c.b).F32(c.a);
            }
            PlayMakerFSM pj = Game.FindFsm("REPAIRSHOP/Jobs/Paintjob", "Work");
            FsmInt pt = pj != null ? pj.FsmVariables.FindFsmInt("PaintType") : null;
            w.I32(pt != null ? pt.Value : -1);
            Session.SendAll(w, true);
            Log.Info("fleetari : travaux finis ici, resultats envoyes (" + wl.Count + " usures)");
        }

        static Renderer PanelRenderer(string name)
        {
            foreach (Transform t in Object.FindObjectsOfType<Transform>())
                if (t.name == name) { Renderer rd = t.GetComponent<Renderer>(); if (rd != null) return rd; }
            return null;
        }

        public static void OnWorkDone(int who, NetReader r)
        {
            PlayMakerFSM wear = Game.FindFsm(WearPath, "Data");
            for (int i = 0, n = r.U8(); i < n; i++) { string k = r.Str(); float x = r.F32(); FsmFloat t = wear != null ? wear.FsmVariables.FindFsmFloat(k) : null; if (t != null) t.Value = x; }
            for (int i = 0, n = r.U8(); i < n; i++)
            {
                string pn = r.Str(); var c = new Color(r.F32(), r.F32(), r.F32(), r.F32());
                if (c.a <= 0f) continue;
                Renderer rd = PanelRenderer(pn);
                if (rd != null) rd.material.color = c;
            }
            int paint = r.I32();
            PlayMakerFSM pj = Game.FindFsm("REPAIRSHOP/Jobs/Paintjob", "Work");
            FsmInt pt = pj != null ? pj.FsmVariables.FindFsmInt("PaintType") : null;
            if (pt != null && paint >= 0) pt.Value = paint;
            orderer = -1;
            Log.Info("fleetari : resultats des travaux de #" + who + " pris ici");
        }

        // Acheteur parti avant ses travaux (audit MSC 02/11) : chez les autres l'automate Order restait arrete et la brochure cachee
        // jusqu'au rechargement -- travaux payes jamais faits, plus rien a commander chez Fleetari, appel de la Ferndale fige. Hote :
        // automate rendu ; il repart de 'Wait for work' avec la commande recue (ReadOrder : _Order, _OrderTime, cases), 'Distance
        // player' recache la brochure, le compte a rebours d'ici finit les travaux et les envoie (orderer -1 : OnLocalWork). Autres
        // invites : la commande passe a l'hote (son @fleettravail rallumera leur automate). [Test] FleetariDepart=0 : comme avant.
        static void OnPlayerLeft(PlayerInfo p)
        {
            if (p == null || orderer < 0 || p.Id != orderer || !Game.Msc || Config.GetInt("Test", "FleetariDepart", 1) == 0) return;
            if (!Session.IsHost) { orderer = 0; Log.Info("fleetari : #" + p.Id + " parti avant ses travaux, la commande passe a l'hote"); return; }
            orderer = -1;
            PlayMakerFSM o = Game.FindFsm("REPAIRSHOP/Order", "Data");
            if (o != null && !o.enabled) o.enabled = true;
            Log.Info("fleetari : #" + p.Id + " parti avant ses travaux, commande reprise ici (" + (o != null ? o.ActiveStateName : "Order introuvable") + ")");
        }

        // Sauvegarde pendant la commande d'un autre (audit MSC 02/11) : l'automate Order arrete ne recoit pas SAVEGAME (diffusion aux
        // seuls automates allumes) -- la sauvegarde de l'hote, monde de tous, perdait les travaux payes par l'invite. Crochet au debut
        // de « Save game » de Systems/Setup Game :: WaitPlayer (comme SaveHook) : les actions de « Save game » d'Order jouees a la
        // main (SaveBool/SaveFloat/SaveColor/SaveInt ; groupees par SaveBatchMsc pendant une sauvegarde en jeu). Sans sortie
        // FINISHED depuis l'etat ou il attend (seul 'Wait day' en a une, vers 'Distance player' : sans effet). [Test] FleetariSauve=0 :
        // comme avant.
        static void HookSave()
        {
            PlayMakerFSM f = Game.FindFsm("Setup Game", "WaitPlayer");
            if (f == null || f.Fsm == null || !f.Fsm.Initialized) return;
            FsmState s = f.Fsm.GetState("Save game");
            saveHooked = true;
            if (s == null) return;
            try { FsmHooks.Prepend(s, new SaveAct()); } catch (System.Exception e) { Log.Warn("fleetari : sauvegarde des travaux pas suivie (" + e.Message + ")"); }
        }

        class SaveAct : ModHook
        {
            public override string Module { get { return "magasin (Fleetari, poste)"; } }
            public override void OnEnter()
            {
                try { SaveOrder(); } catch (System.Exception e) { Replay.HookError(e); }
                Finish();
            }
        }

        static string SaveOrder()
        {
            PlayMakerFSM o = Game.FindFsm("REPAIRSHOP/Order", "Data");
            if (o == null || o.enabled || orderer < 0 || Config.GetInt("Test", "FleetariSauve", 1) == 0) return "rien (Order " + (o == null ? "introuvable" : o.enabled ? "allume" : "arrete") + ", acheteur " + orderer + ")";
            FsmBool ord = o.FsmVariables.FindFsmBool("_Order");
            if (ord == null || !ord.Value) return "rien (pas de commande)";
            FsmState st = o.Fsm.GetState("Save game");
            if (st == null || !st.IsInitialized) return "rien (« Save game » absent)";
            int n = 0;
            foreach (FsmStateAction a in st.Actions)
            {
                if (a == null || !a.Enabled) continue;
                try { a.OnEnter(); n++; } catch (System.Exception e) { Log.Warn("fleetari : sauvegarde des travaux : " + a.GetType().Name + " : " + e.Message); }
            }
            string msg = "travaux de #" + orderer + " ecrits dans la sauvegarde d'ici (" + n + " valeurs, Order arrete)";
            Log.Info("fleetari : " + msg);
            return msg;
        }

        // Caisse figee en 'Wait tobacco' (audit MSC 02/11) : elle attend CASH, envoye par Fleetari::Work 'Cash anim', atteint depuis
        // 'Loop' seulement ; 'Tobacco' en sort a la fin du clip du bras. Quand un autre joueur pilote le corps de Fleetari (Npcs : clip
        // tenu, rejoue a chaque image ici), le clip ne finit jamais : achat impossible tant qu'il est la (meme cause que TeimoWatch,
        // Shop). Apres 4 s en 'Tobacco' : 'Cash anim' (chemin du jeu : CASH -> 'Purchase', CashOut faux) ; apres 10 s quoi qu'il en
        // soit : CASH a la caisse et CashOut faux. [Test] FleetariGarde=0 : comme avant.
        static void CashGuard()
        {
            PlayMakerFSM reg = Game.FindFsm(RegPath, "Data");
            if (reg == null || !reg.gameObject.activeInHierarchy || reg.ActiveStateName != "Wait tobacco") { waitSince = -1f; return; }
            float now = Time.realtimeSinceStartup;
            if (waitSince < 0f) { waitSince = now; return; }
            if (now - waitSince < 4f || !Session.Active || Config.GetInt("Test", "FleetariGarde", 1) == 0) return;
            PlayMakerFSM work = Game.FindFsm("REPAIRSHOP/LOD/Office/Fleetari", "Work");
            if (work != null && work.ActiveStateName == "Tobacco")
            {
                Game.SetState(work, "Cash anim");
                Log.Info("fleetari : caisse restee en 'Wait tobacco' (bras de Fleetari suivi ailleurs) : encaissement (" + reg.ActiveStateName + ")");
                waitSince = -1f;
                return;
            }
            if (now - waitSince < 10f) return;
            reg.SendEvent("CASH");
            FsmBool co = work != null ? work.FsmVariables.FindFsmBool("CashOut") : null;
            if (co != null) co.Value = false;
            Log.Info("fleetari : caisse en 'Wait tobacco' depuis 10 s (Fleetari en " + (work != null ? work.ActiveStateName : "?") + ") : CASH envoye (" + reg.ActiveStateName + ")");
            waitSince = -1f;
        }

        // ---------------------------------------------------------------- poste
        static void OnLocalPost(bool computer)
        {
            var names = new List<string>();
            if (computer) names.Add("computer(Clone)");
            else
            {
                GameObject ol = Game.FindAny("Sheets/OrderList");
                if (ol != null)
                    foreach (PlayMakerArrayListProxy px in ol.GetComponents<PlayMakerArrayListProxy>())
                        if (px.referenceName == "Delivering" && px.arrayList != null)
                            foreach (object o in px.arrayList) { string s = o as string; if (!string.IsNullOrEmpty(s) && !names.Contains(s)) names.Add(s); }
            }
            if (names.Count == 0) return;
            var w = new NetWriter(Msg.Job).U8(Session.LocalId).Str("@postcmd").Bool(computer).U8(names.Count);
            foreach (string n in names) w.Str(n);
            Session.SendAll(w, true);
            Log.Info("poste : commande payee ici, " + names.Count + " colis (" + string.Join(", ", names.ToArray()) + ")");
        }

        public static void OnPost(int who, NetReader r)
        {
            bool computer = r.Bool();
            var names = new List<string>();
            for (int i = 0, n = r.U8(); i < n; i++) names.Add(r.Str());
            GameObject store = Game.FindAny("STORE");
            Transform boxes = store != null ? store.transform.Find("Boxes") : null;
            int done = 0;
            foreach (string n in names)
            {
                Transform b = boxes != null ? boxes.Find(n) : null;
                if (b == null) continue;   // (deja sorti : une seule fois)
                PlayMakerFSM u = Game.FsmOn(b.gameObject, "Use");
                FsmBool pu = u != null ? u.FsmVariables.FindFsmBool("Purchased") : null;
                if (pu != null) pu.Value = true;
                b.name = computer ? "atk harri package(xxxxx)" : "amis-auto ky package(xxxxx)";
                b.SetParent(null, true);
                done++;
            }
            Hud.Toast(Shop.PlayerName(who) + Lang.T(" a reçu une commande par la poste", " got a mail order"));
            Log.Info("poste : commande de #" + who + " : " + done + "/" + names.Count + " colis sortis ici ; " + FinishOrder(computer));
        }

        // Commande payee par un autre (Kamixak et Mikol, 25/10) : ici elle restait en cours -- minuteur de livraison sur 'Pay',
        // PhoneLogic.OrderReady vrai (Teimo rappelait de venir payer), OrderList.Wait vrai (le bouton du catalogue refusait toute
        // nouvelle commande et cachait l'enveloppe). Finie comme le jeu : minuteur sur 'Pay' -> Paid (il passe a 'Fetch product',
        // colis deja sortis, puis 'Reset purchase') ; sinon remise a zero faite a la main, comme 'Reset purchase'.
        static string FinishOrder(bool computer)
        {
            string root = computer ? "Sheets/OrderComputer" : "Sheets/OrderList";
            PlayMakerFSM phone = Game.FindFsm("YARD/Building/LIVINGROOM/Telephone/Logic/PhoneLogic", "Ring");
            FsmBool ready = phone != null ? phone.FsmVariables.FindFsmBool("OrderReady") : null;
            if (ready != null) ready.Value = false;
            GameObject buy = Game.FindAny(computer ? "STORE/LOD/ActivateStore/PostOffice/PostOrderBuyComputer" : "STORE/LOD/ActivateStore/PostOffice/PostOrderBuy");
            if (buy != null && buy.activeSelf) buy.SetActive(false);
            PlayMakerFSM data = Game.FindFsm(root, "Data"), timer = Game.FindFsm(root + "/Timer", "Delivery");
            if (data == null) return "pas de " + root;
            if (timer != null && timer.gameObject.activeInHierarchy && timer.ActiveStateName == "Pay")
            {
                SetBool(data, "Paid", true);
                return "commande finie par son minuteur (Paid)";
            }
            ClearList(data.gameObject, "Delivering");
            SetBool(data, "Wait", false); SetBool(data, "Activated", false); SetBool(data, "Paid", false);
            FsmFloat tc = data.FsmVariables.FindFsmFloat("TimeCurrent"); if (tc != null) tc.Value = 0f;
            FsmFloat pt = data.FsmVariables.FindFsmFloat("PriceTotal"); if (pt != null) pt.Value = 0f;
            if (timer != null && timer.gameObject.activeSelf) timer.gameObject.SetActive(false);
            return "commande remise a zero (minuteur " + (timer != null ? timer.ActiveStateName : "?") + ")";
        }
        static void SetBool(PlayMakerFSM f, string n, bool v) { FsmBool b = f.FsmVariables.FindFsmBool(n); if (b != null) b.Value = v; }
        static PlayMakerArrayListProxy ListOf(GameObject go, string reference)
        {
            foreach (PlayMakerArrayListProxy px in go.GetComponents<PlayMakerArrayListProxy>()) if (px.referenceName == reference && px.arrayList != null) return px;
            return null;
        }
        static void ClearList(GameObject go, string reference) { PlayMakerArrayListProxy px = ListOf(go, reference); if (px != null) for (int i = 0; i < px.arrayList.Count; i++) px.arrayList[i] = ""; }

        // Enveloppe du catalogue postee a la boite du magasin (STORE/LOD/Post Box/OrderTrigger 'Car parts' / 'Computer') : chez
        // celui qui la poste seulement (le minuteur de livraison, puis l'appel de Teimo et le guichet, ne tournaient que chez lui ;
        // chez l'hote rien, chez les autres la commande refusee ensuite). La liste du bon (Order, entrees vides comprises : le jeu la
        // lit par rang) part a tous ; chacun la copie dans Delivering et lance son minuteur, comme 'Copy order list'.
        static void OnLocalPostBox(bool computer)
        {
            var w = new NetWriter(Msg.Job).U8(Session.LocalId).Str("@postbox").Bool(computer);
            int n = 0;
            if (!computer)
            {
                GameObject ol = Game.FindAny("Sheets/OrderList");
                PlayMakerArrayListProxy px = ol != null ? ListOf(ol, "Order") : null;
                PlayMakerFSM data = ol != null ? Game.FsmOn(ol, "Data") : null;
                FsmFloat pt = data != null ? data.FsmVariables.FindFsmFloat("PriceTotal") : null;
                w.F32(pt != null ? pt.Value : 0f);
                n = px != null ? px.arrayList.Count : 0;
                w.U8(n);
                for (int i = 0; i < n; i++) w.Str(px.arrayList[i] as string ?? "");
            }
            Session.SendAll(w, true);
            Log.Info("poste : enveloppe " + (computer ? "de l'ordinateur" : "du catalogue (" + n + " lignes)") + " postee ici, envoyee aux autres");
        }

        public static void OnPostBox(int who, NetReader r)
        {
            bool computer = r.Bool();
            string root = computer ? "Sheets/OrderComputer" : "Sheets/OrderList";
            GameObject ol = Game.FindAny(root);
            PlayMakerFSM data = ol != null ? Game.FsmOn(ol, "Data") : null;
            var items = new List<string>();
            float price = 0f;
            if (!computer) { price = r.F32(); for (int i = 0, n = r.U8(); i < n; i++) items.Add(r.Str()); }
            if (data == null) { Log.Warn("poste : enveloppe de #" + who + " : " + root + " introuvable"); return; }
            FsmBool wait = data.FsmVariables.FindFsmBool("Wait");
            if (wait != null && wait.Value) Log.Warn("poste : enveloppe de #" + who + " alors qu'une commande est deja en route ici : remplacee");
            if (!computer)
            {
                foreach (string reference in new[] { "Order", "Delivering" })
                {
                    PlayMakerArrayListProxy px = ListOf(ol, reference);
                    if (px == null) continue;
                    for (int i = 0; i < items.Count; i++) { if (i < px.arrayList.Count) px.arrayList[i] = items[i]; else px.arrayList.Add(items[i]); }
                }
                FsmFloat pt = data.FsmVariables.FindFsmFloat("PriceTotal"); if (pt != null) pt.Value = price;
            }
            // (enveloppe : postee, donc rangee, comme 'Car parts' -- la sienne n'existait que chez lui)
            Transform env = ol.transform.root.Find(computer ? "envelope(computer)" : "envelope(xxxxx)");
            GameObject envGo = env != null ? env.gameObject : GameObject.Find(computer ? "envelope(computer)" : "envelope(xxxxx)");
            if (envGo != null && envGo.activeSelf) envGo.SetActive(false);
            SetBool(data, "Activated", false);
            FsmFloat tc = data.FsmVariables.FindFsmFloat("TimeCurrent"); if (tc != null) tc.Value = 0f;
            Transform tm = ol.transform.Find("Timer");
            if (tm != null) { if (tm.gameObject.activeSelf) tm.gameObject.SetActive(false); tm.gameObject.SetActive(true); }
            int lines = 0; foreach (string s in items) if (!string.IsNullOrEmpty(s)) lines++;
            Hud.Toast(Shop.PlayerName(who) + Lang.T(" a posté une commande", " posted an order"));
            Log.Info("poste : enveloppe de #" + who + " (" + (computer ? "ordinateur" : lines + " pieces, " + price.ToString("F0") + " mk") + ") : livraison lancee ici" + (tm != null ? "" : " (minuteur introuvable)"));
        }

        // [Test] Autotest=mscposte (2 joueurs ; Kamixak et Mikol 25/10) : chacun avance son minuteur de livraison a 5 s de l'arrivee ;
        // l'invite (Triche=lieu magasin) remplit le bon avec les 2 premiers colis de STORE/Boxes et passe la boite du magasin a
        // 'Car parts' a 45 s, paie au guichet ('State 3') a 75 s ; chacun note l'etat de la commande toutes les 5 s de 40 a 110 s.
        static int posteStep; static float posteLog;
        public static void Test(string mode, float t)
        {
            if (mode != "mscposte") return;
            PlayMakerFSM timer = Game.FindFsm("Sheets/OrderList/Timer", "Delivery");
            FsmFloat cur = timer != null ? timer.FsmVariables.FindFsmFloat("Current") : null;
            if (timer != null && timer.gameObject.activeInHierarchy && timer.ActiveStateName == "State 1" && cur != null && cur.Value < 4990f) { cur.Value = 4995f; Log.Info("autotest : poste, minuteur avance a 4995"); }
            if (!Session.IsHost && t > 32f && posteStep == 0) { posteStep = 10; Log.Info("autotest : poste, au magasin -> " + (Cheats.Command("lieu magasin") ?? "ok")); }
            if (!Session.IsHost && t > 45f && posteStep == 10)
            {
                posteStep = 1;
                GameObject ol = Game.FindAny("Sheets/OrderList"), store = Game.FindAny("STORE");
                Transform boxes = store != null ? store.transform.Find("Boxes") : null;
                PlayMakerArrayListProxy px = ol != null ? ListOf(ol, "Order") : null;
                PlayMakerFSM trig = Game.FindFsm("STORE/LOD/Post Box/OrderTrigger", "Open");
                if (px == null || boxes == null || boxes.childCount < 2 || trig == null) { Log.Info("autotest : poste, bon ou boite introuvable"); return; }
                for (int i = 0; i < px.arrayList.Count; i++) px.arrayList[i] = i < 2 ? boxes.GetChild(i).name : "";
                Log.Info("autotest : poste, bon rempli (" + boxes.GetChild(0).name + ", " + boxes.GetChild(1).name + "), boite " + trig.ActiveStateName + (trig.gameObject.activeInHierarchy ? "" : " (eteinte)"));
                Game.SetState(trig, "Car parts");
            }
            if (!Session.IsHost && t > 75f && posteStep == 1)
            {
                posteStep = 2;
                PlayMakerFSM buy = Game.FindFsm("STORE/LOD/ActivateStore/PostOffice/PostOrderBuy", "Use");
                Log.Info("autotest : poste, guichet " + (buy == null ? "introuvable" : buy.ActiveStateName + (buy.gameObject.activeInHierarchy ? "" : " (eteint)")));
                if (buy != null && buy.gameObject.activeInHierarchy) Game.SetState(buy, "State 3");
                else
                {   // (guichet sous le LOD : joueur devant le magasin -- ce que fait 'State 3' : Paid, et l'envoi du crochet)
                    PlayMakerFSM d = Game.FindFsm("Sheets/OrderList", "Data");
                    if (d != null) SetBool(d, "Paid", true);
                    OnLocalPost(false);
                }
            }
            if (t > 40f && t < 170f && t >= posteLog)
            {
                posteLog = t + 5f;
                PlayMakerFSM data = Game.FindFsm("Sheets/OrderList", "Data"), phone = Game.FindFsm("YARD/Building/LIVINGROOM/Telephone/Logic/PhoneLogic", "Ring");
                System.Func<PlayMakerFSM, string, string> B = (f, n) => { FsmBool b = f != null ? f.FsmVariables.FindFsmBool(n) : null; return b == null ? "?" : b.Value ? "1" : "0"; };
                GameObject ol = Game.FindAny("Sheets/OrderList");
                PlayMakerArrayListProxy dl = ol != null ? ListOf(ol, "Delivering") : null;
                int lines = 0; if (dl != null) foreach (object o in dl.arrayList) if (!string.IsNullOrEmpty(o as string)) lines++;
                Log.Info("autotest : poste t=" + t.ToString("F0") + " Wait " + B(data, "Wait") + " Activated " + B(data, "Activated") + " Paid " + B(data, "Paid") + " Delivering " + lines
                         + " ; minuteur " + (timer == null ? "?" : timer.gameObject.activeInHierarchy ? timer.ActiveStateName + " " + (cur != null ? cur.Value.ToString("F0") : "?") : "eteint")
                         + " ; OrderReady " + B(phone, "OrderReady"));
            }
        }

        // Essais : commande de travaux (reglage du parallelisme, fini dans 1 s) envoyee comme le bouton de la brochure.
        public static string TestOrder() { return TestOrder(1f); }
        // (audit MSC 02/11 : 'time' long -- 3000 s -- pour quitter avant les travaux, Autotest=fleettravaux)
        public static string TestOrder(float time)
        {
            PlayMakerFSM o = Game.FindFsm("REPAIRSHOP/Order", "Data");
            if (o == null) return "REPAIRSHOP/Order introuvable";
            FsmBool toe = o.FsmVariables.FindFsmBool("ToeAdjustment"), ord = o.FsmVariables.FindFsmBool("_Order");
            FsmFloat t = o.FsmVariables.FindFsmFloat("_OrderTime");
            if (toe != null) toe.Value = true; if (t != null) t.Value = time; if (ord != null) ord.Value = true;
            OnLocalOrder();
            return "travaux commandes ici (" + o.ActiveStateName + ", " + time.ToString("F0") + " s)";
        }
        // Essais (audit MSC 02/11) : ecriture a la main des travaux d'un autre (comme la sauvegarde la fait) ; acheteur en cours.
        public static string TestSaveOrder() { return SaveOrder() + " ; acheteur " + orderer; }

        // Essais (audit MSC 02/11, Autotest=fleetpanier) : la piece q (QAlternator...) cochee dans la caisse d'ici comme son bouton
        // ('Play anim 2' : Q vrai, Total + prix), sans PURCHASE.
        public static string TestPick(string q)
        {
            GameObject rg = Game.FindAny(RegPath), shop = Game.FindAny("REPAIRSHOP");
            PlayMakerFSM reg = rg != null ? Game.FsmOn(rg, "Data") : null;
            string[] it = System.Array.Find(Items, x => x[0] == q);
            if (reg == null || it == null) return "caisse ou piece " + q + " introuvable";
            Transform btn = shop != null ? shop.transform.Find("LOD/Store/Parts/" + it[3]) : null;
            PlayMakerFSM bf = btn != null ? Game.FsmOn(btn.gameObject, "Buy") : null;
            FsmFloat pr = bf != null ? bf.FsmVariables.FindFsmFloat("Price") : null, tot = reg.FsmVariables.FindFsmFloat("Total");
            FsmBool qv = reg.FsmVariables.FindFsmBool(q);
            if (qv == null || tot == null) return "variables de la caisse introuvables";
            qv.Value = true; tot.Value += pr != null ? pr.Value : 0f;
            return "coche ici : " + Basket();
        }
        // Essais : panier de la caisse de Fleetari ici (pieces cochees, total, etat) et etat du bras de Fleetari (garde 'Wait tobacco').
        public static string Basket()
        {
            GameObject rg = Game.FindAny(RegPath);
            PlayMakerFSM reg = rg != null ? Game.FsmOn(rg, "Data") : null, work = Game.FindFsm("REPAIRSHOP/LOD/Office/Fleetari", "Work");
            if (reg == null) return "caisse introuvable";
            var sb = new System.Text.StringBuilder();
            foreach (string[] it in Items) { FsmBool q = reg.FsmVariables.FindFsmBool(it[0]); if (q != null && q.Value) sb.Append(' ').Append(it[0]); }
            FsmFloat tot = reg.FsmVariables.FindFsmFloat("Total");
            return "panier [" + sb.ToString().Trim() + "] total " + (tot != null ? tot.Value.ToString("F0") : "?") + ", caisse " + (reg.gameObject.activeInHierarchy ? reg.ActiveStateName : "eteinte")
                   + ", Fleetari " + (work != null ? work.ActiveStateName : "?") + (waitSince >= 0f ? ", en 'Wait tobacco' depuis " + (Time.realtimeSinceStartup - waitSince).ToString("F1") + " s" : "");
        }

        // Essais : colis 'n' (premier enfant de STORE/Boxes sinon) paye a la poste, envoye comme PostOrderBuy 'State 3'.
        public static string TestPost()
        {
            GameObject store = Game.FindAny("STORE");
            Transform boxes = store != null ? store.transform.Find("Boxes") : null;
            if (boxes == null || boxes.childCount == 0) return "STORE/Boxes vide ou introuvable";
            string n = boxes.GetChild(0).name;
            var w = new NetWriter(Msg.Job).U8(Session.LocalId).Str("@postcmd").Bool(false).U8(1).Str(n);
            Session.SendAll(w, true);
            return "colis " + n + " envoye (" + boxes.childCount + " dans STORE/Boxes)";
        }
        public static int BoxesLeft() { GameObject store = Game.FindAny("STORE"); Transform b = store != null ? store.transform.Find("Boxes") : null; return b != null ? b.childCount : -1; }

        // Essais : etat ici.
        public static string State()
        {
            PlayMakerFSM o = Game.FindFsm("REPAIRSHOP/Order", "Data"), wear = Game.FindFsm(WearPath, "Data");
            FsmBool ord = o != null ? o.FsmVariables.FindFsmBool("_Order") : null;
            FsmFloat wa = wear != null ? wear.FsmVariables.FindFsmFloat("WearAlternator") : null;
            int hooked = 0; foreach (Point p in points) if (p.Hooked) hooked++;
            return "travaux " + (ord != null ? ord.Value.ToString() : "?") + (o != null ? " [" + (o.enabled ? o.ActiveStateName : "arrete") + "]" : "") + ", WearAlternator " + (wa != null ? wa.Value.ToString("F1") : "?") + ", " + hooked + "/" + points.Count + " points suivis";
        }
    }
}
