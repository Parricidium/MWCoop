using System.Collections.Generic;
using HutongGames.PlayMaker;
using MWCoop.Net;
using UnityEngine;

namespace MWCoop
{
    // Vente de la Rivett par Reijo (JOBS/HouseWood1, vendeur WoodCaller1::Animations). Le vendeur est un PNJ : chacun a
    // le sien, mene par sa logique (WorldFsms le laisse) et par la distance du joueur LOCAL ("Distance?" : moins de 3 m ->
    // "Explain car" -> "Anim" -> "Money" : l'argent tendu, PayMoney "PAY 500 MK" ; clic -> PAID -> "Give keys" : cles
    // (PlayerKeySatsuma), ORDERTAKEN a WoodJob1Point -- flechette Wood1Car eteinte, passe par Jobs --, etape 3 du numero
    // de Reijo, qui supprime l'annonce au prochain chargement). Chez les autres rien de tout cela : leur vendeur attendait
    // toujours, refaisait la vente s'ils s'approchaient (voiture rachetee), et l'hote dont l'invite avait achete gardait
    // l'etape 2 dans SA sauvegarde -- au chargement suivant le jeu relancait la vente, flechette comprise (retour d'un
    // joueur, 10/10 : « la punaise ne disparait pas et je peux racheter la voiture », « le vendeur n'est pas synchronise »).
    //  - l'explication commencee chez l'un (@vente 0) commence aussi chez les autres pres du vendeur qui attend ;
    //  - la vente conclue chez l'un (@vente 1) l'est chez tous : etape 3, cles, "Give keys" chez qui est pres du vendeur ;
    //    ensuite un vendeur qui revient a la vente (rallume, tout le monde : la variable Car de WoodJob1Point reste vraie
    //    jusqu'au chargement) est remis au repos ("State 1") et son argent tendu retire.
    public static class CarSale
    {
        const string SellerPath = "JOBS/HouseWood1/LOD/CarPos/NPCWood/WoodCaller1";
        const string PayPath = SellerPath + "/skeleton/pelvis/spine_middle/spine_upper/collar_right/shoulder_right/arm_right/hand_right/PayMoney";
        const string Number = "08609553";
        static readonly HashSet<string> SellStates = new HashSet<string> { "State 11", "Distance?", "Explain car", "Anim", "Money", "Wait" };
        const float Near = 40f;

        static PlayMakerFSM anim, pay;
        static bool hooked, sold, applying;
        static float nextLook;

        class SaleHook : ModHook
        {
            public override string Module { get { return "vente"; } }
            public int Kind;
            public override void OnEnter()
            {
                try { if (!applying && Replay.Depth == 0) Local(Kind); } catch (System.Exception e) { Replay.HookError(e); }
                Finish();
            }
        }

        public static void OnLevelLoaded() { anim = null; pay = null; hooked = false; sold = false; applying = false; nextLook = 0f; repairAt = Time.realtimeSinceStartup + 8f; repaired = false; testStep = 0; }

        // Sauvegardes abimees (retour d'un joueur, 10/10 : « la punaise reste, le vendeur redemande 500 mk a chaque
        // livraison de bois ») : jusqu'a 0.75.2, la sauvegarde coop en jeu remettait le numero de Reijo dans son etat
        // « appele » (State 1 : etape 2, SELLCAR) apres l'avoir ecrit a l'etape 3 -- la vente repartait, et l'etape 2 etait
        // ensuite sauvee. Au chargement : si la Rivett a deja ses cles (PlayerKeySatsuma) mais que la vente a ete relancee
        // (numero a l'etape 2), elle est annulee comme le jeu l'aurait fait avec l'etape 3 : numero detruit ("Destroy"),
        // flechette Wood1Car eteinte, Car de WoodJob1Point a faux et retour a sa boucle, vendeur au repos.
        static float repairAt; static bool repaired;
        static void Repair()
        {
            if (repaired || Time.realtimeSinceStartup < repairAt) return;
            repaired = true;
            FsmInt key = FsmVariables.GlobalVariables.FindFsmInt("PlayerKeySatsuma");
            PlayMakerFSM data = null;
            foreach (Object o in Game.AllFsms())
            {
                var f = (PlayMakerFSM)o;
                if (f == null || f.FsmName != "Data" || f.transform.parent == null || f.transform.parent.name != "PhoneNumbers") continue;
                FsmString nb = f.FsmVariables.FindFsmString("Number");
                if (nb != null && nb.Value == Number) { data = f; break; }
            }
            FsmInt stage = data != null ? data.FsmVariables.FindFsmInt("Stage") : null;
            bool keys = key != null && key.Value > 0 || Config.GetInt("Test", "VenteCles", 0) != 0;
            Log.Info("vente : au chargement, cles de la Rivett " + (key != null ? key.Value.ToString() : "?") + ", numero de Reijo " + (stage != null ? "etape " + stage.Value + " (" + data.ActiveStateName + ")" : "absent (vente finie)"));
            if (!keys || stage == null || stage.Value != 2) return;
            Cancel(data, stage, "cles deja la, vente relancee par une sauvegarde");
        }

        static void Cancel(PlayMakerFSM data, FsmInt stage, string why)
        {
            sold = true;
            stage.Value = 3;
            if (data != null && data.gameObject.activeInHierarchy && data.Fsm.GetState("Destroy") != null) Set(data, "Destroy");
            GameObject wj = Game.FindAny("JOBS/HouseWood1/WoodJob1Point");
            PlayMakerFSM logic = wj != null ? Game.FsmOn(wj, "Logic") : null;
            string was = logic != null ? logic.ActiveStateName : "?";
            if (logic != null)
            {
                FsmBool car = logic.FsmVariables.FindFsmBool("Car");
                if (car != null) car.Value = false;
                FsmGameObject dart = logic.FsmVariables.FindFsmGameObject("DartCar");
                if (dart != null && dart.Value != null) dart.Value.SetActive(false);
                if (was == "State 1" && logic.Fsm.GetState("Calc rate") != null) Set(logic, "Calc rate");
            }
            Log.Info("vente : Rivett deja vendue (" + why + ") : numero de Reijo a l'etape 3, flechette eteinte, WoodJob1Point " + was + " -> " + (logic != null ? logic.ActiveStateName : "?"));
            Settle();
        }

        static bool Find()
        {
            if (anim == null)
            {
                GameObject s = Game.FindAny(SellerPath);
                anim = s != null ? Game.FsmOn(s, "Animations") : null;
                GameObject p = Game.FindAny(PayPath);
                pay = p != null ? Game.FsmOn(p, "Use") : null;
            }
            return anim != null;
        }

        // Accroche a sa premiere activation (etats charges).
        static void Hook()
        {
            if (hooked || anim == null || !anim.gameObject.activeInHierarchy) return;
            // (pas reserve : le monde -- WorldFsms -- le suit aussi, mais ne rejoue pas "Give keys", qui touche au joueur :
            // cles, sous-titre ; nos poses se font en rejeu, ignorees par ses accroches)
            FsmState explain = anim.Fsm.GetState("Explain car"), keys = anim.Fsm.GetState("Give keys");
            if (explain == null || keys == null) { hooked = true; Log.Warn("vente : vendeur sans Explain car / Give keys"); return; }
            if (!explain.IsInitialized || !keys.IsInitialized) return;
            var l1 = new List<FsmStateAction>(explain.Actions); l1.Insert(0, new SaleHook { Kind = 0 }); explain.Actions = l1.ToArray();
            var l2 = new List<FsmStateAction>(keys.Actions); l2.Insert(0, new SaleHook { Kind = 1 }); keys.Actions = l2.ToArray();
            hooked = true;
            Log.Info("vente : vendeur de la Rivett suivi (" + anim.ActiveStateName + ")");
        }

        static void Local(int kind)
        {
            if (kind == 1) { sold = true; Log.Info("vente : Rivett achetee ici (cles donnees)"); }
            else Log.Info("vente : le vendeur explique la voiture ici");
            if (Session.Active) Session.SendAll(new NetWriter(Msg.Job).U8(Session.LocalId).Str("@vente").U8((byte)kind), true);
        }

        static bool PlayerNear()
        {
            GameObject pl = GameObject.Find("PLAYER");
            return pl != null && anim != null && (pl.transform.position - anim.transform.position).sqrMagnitude < Near * Near;
        }

        static void Set(PlayMakerFSM f, string state)
        {
            applying = true; Replay.Depth++;
            try { Game.SetState(f, state); }
            finally { applying = false; Replay.Depth--; }
        }

        public static void OnRemote(int who, int kind)
        {
            Find();
            if (kind == 0)
            {
                if (anim != null && anim.gameObject.activeInHierarchy && anim.ActiveStateName == "Distance?" && PlayerNear())
                {
                    Set(anim, "Explain car");
                    Log.Info("vente : le vendeur explique la voiture, comme chez #" + who);
                }
                return;
            }
            sold = true;
            // Etape 3 du numero (l'annonce disparait au prochain chargement de cette sauvegarde) et cles, comme "Give keys".
            foreach (Object o in Game.AllFsms())
            {
                var f = (PlayMakerFSM)o;
                if (f == null || f.FsmName != "Data" || f.transform.parent == null || f.transform.parent.name != "PhoneNumbers") continue;
                FsmString nb = f.FsmVariables.FindFsmString("Number");
                FsmInt stage = f.FsmVariables.FindFsmInt("Stage");
                if (nb != null && nb.Value == Number && stage != null && stage.Value < 3) stage.Value = 3;
            }
            FsmInt key = FsmVariables.GlobalVariables.FindFsmInt("PlayerKeySatsuma");
            if (key != null) key.Value = 1;
            string was = anim != null ? anim.ActiveStateName : "?";
            if (anim != null && anim.gameObject.activeInHierarchy && was != "Give keys" && PlayerNear()) Set(anim, "Give keys");
            Log.Info("vente : Rivett achetee par #" + who + " : etape 3, cles ; vendeur " + was + " -> " + (anim != null ? anim.ActiveStateName : "?"));
            Settle();
        }

        // Vente faite : vendeur revenu a la vente -> au repos, argent tendu retire.
        static void Settle()
        {
            if (!sold || anim == null || !anim.gameObject.activeInHierarchy) return;
            string st = anim.ActiveStateName;
            if (SellStates.Contains(st)) { Set(anim, "State 1"); Log.Info("vente : vendeur remis au repos (" + st + " : Rivett deja vendue)"); }
            if (pay != null && pay.gameObject.activeInHierarchy && (pay.ActiveStateName == "Wait player 2" || pay.ActiveStateName == "Wait button 2"))
            {
                Set(pay, "State 3");
                Log.Info("vente : argent du vendeur retire (Rivett deja vendue)");
            }
        }

        public static void Update()
        {
            if (!PlayerSync.InGame || Time.realtimeSinceStartup < nextLook) return;
            nextLook = Time.realtimeSinceStartup + 1f;
            Repair();
            if (!Find()) return;
            Hook();
            Settle();
        }

        // [Test] Autotest=vente-sauve (hote) : 25 s, Reijo « appele » (numero en State 1 : etape 2, SELLCAR) ; 28 s, la Rivett
        // achetee par un autre (@vente 1) ; 31 s, sauvegarde coop en jeu ; 38 s, etat (attendu : etape 3, WoodJob1Point pas
        // en State 1 apres la sauvegarde). 42 s : sauvegarde abimee simulee (etape 2, numero en State 1, cles) et reparation.
        static int testStep;
        public static void Test(string mode, float t)
        {
            if (mode != "vente-sauve" || !Session.IsHost) return;
            PlayMakerFSM data = null;
            foreach (Object o in Game.AllFsms())
            {
                var f = (PlayMakerFSM)o;
                if (f == null || f.FsmName != "Data" || f.transform.parent == null || f.transform.parent.name != "PhoneNumbers") continue;
                FsmString nb = f.FsmVariables.FindFsmString("Number");
                if (nb != null && nb.Value == Number) { data = f; break; }
            }
            GameObject wj = Game.FindAny("JOBS/HouseWood1/WoodJob1Point");
            PlayMakerFSM logic = wj != null ? Game.FsmOn(wj, "Logic") : null;
            FsmInt stage = data != null ? data.FsmVariables.FindFsmInt("Stage") : null;
            System.Func<string> st = () => "numero " + (data != null ? data.ActiveStateName + " etape " + (stage != null ? stage.Value : -1) : "absent") + ", WoodJob1Point " + (logic != null ? logic.ActiveStateName + " Car " + logic.FsmVariables.FindFsmBool("Car").Value : "?");
            if (t > 25f && testStep == 0) { testStep = 1; if (data != null) Game.SetState(data, "State 1"); Log.Info("autotest : vente, Reijo appele : " + st()); }
            if (t > 28f && testStep == 1) { testStep = 2; OnRemote(1, 1); Log.Info("autotest : vente, achetee par #1 : " + st()); }
            if (t > 31f && testStep == 2) { testStep = 3; Game.SaveInPlace(); }
            if (t > 38f && testStep == 3) { testStep = 4; Log.Info("autotest : vente, apres la sauvegarde : " + st()); }
            if (t > 42f && testStep == 4)
            {
                testStep = 5;
                if (data != null && stage != null) { Game.SetState(data, "State 1"); stage.Value = 2; }
                FsmInt key = FsmVariables.GlobalVariables.FindFsmInt("PlayerKeySatsuma"); if (key != null) key.Value = 1;
                sold = false; repaired = false; repairAt = 0f;
                Log.Info("autotest : vente, sauvegarde abimee simulee : " + st());
            }
            if (t > 46f && testStep == 5) { testStep = 6; Log.Info("autotest : vente, apres reparation : " + st() + ", " + Describe()); }
        }

        public static string Describe()
        {
            Find();
            return "vente : vendeur " + (anim != null ? anim.ActiveStateName + (anim.gameObject.activeInHierarchy ? "" : " (eteint)") : "?") + ", argent " + (pay != null ? pay.ActiveStateName : "?") + ", vendue " + sold;
        }
    }
}
