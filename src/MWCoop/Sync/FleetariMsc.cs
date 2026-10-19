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

        public static void OnLevelLoaded()
        {
            points.Clear(); nextLook = 0f; paidQ = null; orderer = -1;
            if (!Game.Msc) return;
            points.Add(new Point { Path = RegPath, Fsm = "Data", State = "Purchase", Make = () => new Act { Run = OnLocalPaid } });
            points.Add(new Point { Path = RegPath, Fsm = "Data", State = "State 1", Make = () => new Act { Run = OnLocalSpawned, Send = true } });
            points.Add(new Point { Path = "Sheets/ServiceBrochure/ButtonOrder", Fsm = "Button", State = "State 3", Make = () => new Act { Run = OnLocalOrder, Send = true } });
            points.Add(new Point { Path = "REPAIRSHOP/Order", Fsm = "Data", State = "Work", Make = () => new Act { Run = OnLocalWork, Send = true } });
            points.Add(new Point { Path = "REPAIRSHOP/Order", Fsm = "Data", State = "Reset job", Make = () => new Act { Run = OnLocalWorkDone, Send = true } });
            points.Add(new Point { Path = "STORE/LOD/ActivateStore/PostOffice/PostOrderBuy", Fsm = "Use", State = "State 3", Make = () => new Act { Run = () => OnLocalPost(false), Send = true } });
            points.Add(new Point { Path = "STORE/LOD/ActivateStore/PostOffice/PostOrderBuyComputer", Fsm = "Use", State = "State 3", Make = () => new Act { Run = () => OnLocalPost(true), Send = true } });
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
            if (!Game.Msc || !PlayerSync.InGame || Time.realtimeSinceStartup < nextLook) return;
            nextLook = Time.realtimeSinceStartup + 2f;
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
                    if (db != null)
                    {
                        FsmBool dmg = db.FsmVariables.FindFsmBool("Damaged"); if (dmg != null) dmg.Value = false;
                        db.SendEvent("REPAIR");
                        FsmGameObject tp = db.FsmVariables.FindFsmGameObject("ThisPart"); part = tp != null ? tp.Value : null;
                    }
                    if (part != null && spawn != null)
                    {
                        part.transform.SetParent(spawn, false);
                        part.transform.localPosition = Vector3.zero; part.transform.localRotation = Quaternion.identity;
                        try { part.tag = "PART"; } catch { }
                    }
                    Transform btn = shop != null ? shop.transform.Find("LOD/Store/Parts/" + it[3]) : null;
                    if (btn != null) Object.Destroy(btn.gameObject);
                    done.Append(' ').Append(it[1]).Append(part == null ? " (piece introuvable)" : "");
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
            Log.Info("poste : commande de #" + who + " : " + done + "/" + names.Count + " colis sortis ici");
        }

        // Essais : commande de travaux (reglage du parallelisme, fini dans 1 s) envoyee comme le bouton de la brochure.
        public static string TestOrder()
        {
            PlayMakerFSM o = Game.FindFsm("REPAIRSHOP/Order", "Data");
            if (o == null) return "REPAIRSHOP/Order introuvable";
            FsmBool toe = o.FsmVariables.FindFsmBool("ToeAdjustment"), ord = o.FsmVariables.FindFsmBool("_Order");
            FsmFloat t = o.FsmVariables.FindFsmFloat("_OrderTime");
            if (toe != null) toe.Value = true; if (t != null) t.Value = 1f; if (ord != null) ord.Value = true;
            OnLocalOrder();
            return "travaux commandes ici (" + o.ActiveStateName + ")";
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
