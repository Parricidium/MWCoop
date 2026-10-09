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

        public static void OnLevelLoaded() { anim = null; pay = null; hooked = false; sold = false; applying = false; nextLook = 0f; }

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
            if (!Find()) return;
            Hook();
            Settle();
        }

        public static string Describe()
        {
            Find();
            return "vente : vendeur " + (anim != null ? anim.ActiveStateName + (anim.gameObject.activeInHierarchy ? "" : " (eteint)") : "?") + ", argent " + (pay != null ? pay.ActiveStateName : "?") + ", vendue " + sold;
        }
    }
}
