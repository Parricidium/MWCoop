using System.Collections;
using System.Collections.Generic;
using HutongGames.PlayMaker;
using MWCoop.Net;
using UnityEngine;

namespace MWCoop
{
    // Achats (demande de JD) : chacun paie de son cote, mais tous voient ce que les autres achetent.
    // Caisse d'un magasin : automate 'Data' de CashRegisterLogic. Au paiement : 'Purchase' (retire
    // PriceTotal de PlayerMoney) puis 'Spawn bag' : le sac est cree par Spawner/CreateBagStore et
    // rempli d'apres la table 'Carried' de INVENTORY_store (produit -> quantite), puis les gros
    // articles a part ('Separates') ; enfin 'Reset purchase' vide le panier.
    //  - une action injectee au debut de 'Spawn bag' envoie (caisse, panier) ;
    //  - ailleurs : panier local mis de cote, panier recu pose dans Carried, caisse mise dans
    //    'Spawn bag' (le sac et les articles apparaissent, sans rien payer), puis panier local remis.
    // Les objets crees portent un nom a compteur sauvegarde (shoppingbag5...) : rejoues dans le
    // meme ordre, ils ont le meme nom partout (Props les suit ensuite par ce nom).
    public static class Shop
    {
        class Register { public string Key; public PlayMakerFSM Fsm; }
        class Pending { public Register R; public float PriceTotal; public int BagStuff; public Hashtable Carried; public float Until; }

        static readonly Dictionary<string, Register> registers = new Dictionary<string, Register>();
        static readonly HashSet<PlayMakerFSM> hooked = new HashSet<PlayMakerFSM>();
        static readonly List<Pending> pending = new List<Pending>();
        static bool applying;
        static float nextScan = -1;

        class Hook : FsmStateAction
        {
            public Register R;
            public override void OnEnter()
            {
                if (!applying) OnLocal(R);
                Finish();
            }
        }

        public static void OnLevelLoaded()
        {
            registers.Clear(); hooked.Clear(); pending.Clear();
            nextScan = PlayerSync.InGame ? Time.realtimeSinceStartup + 9f : -1;
        }

        public static void Update()
        {
            float now = Time.realtimeSinceStartup;
            if (nextScan > 0 && now >= nextScan) { nextScan = now + 20f; Scan(); }
            for (int i = pending.Count - 1; i >= 0; i--)
            {
                Pending p = pending[i];
                if (p.R.Fsm == null) { pending.RemoveAt(i); continue; }
                string s = p.R.Fsm.ActiveStateName;
                bool finished = s == "State 5" || s == "Wait player" || s == "Player distance";
                if (!finished && now < p.Until) continue;
                // Le panier du joueur local revient (le jeu l'a vide dans 'Reset purchase').
                Hashtable carried = CarriedOf(p.R.Fsm);
                if (carried != null) { carried.Clear(); foreach (DictionaryEntry e in p.Carried) carried[e.Key] = e.Value; }
                p.R.Fsm.FsmVariables.GetFsmFloat("PriceTotal").Value = p.PriceTotal;
                p.R.Fsm.FsmVariables.GetFsmInt("BagStuff").Value = p.BagStuff;
                pending.RemoveAt(i);
            }
        }

        static void Scan()
        {
            foreach (Object o in Resources.FindObjectsOfTypeAll(typeof(PlayMakerFSM)))
            {
                var f = (PlayMakerFSM)o;
                if (f.hideFlags != HideFlags.None || f.FsmName != "Data" || f.gameObject.name != "CashRegisterLogic" || hooked.Contains(f)) continue;
                FsmState s = f.Fsm.GetState("Spawn bag");
                if (s == null || f.FsmVariables.FindFsmGameObject("Inventory") == null) continue;
                var r = new Register { Key = Recon.Path(f.transform), Fsm = f };
                try
                {
                    var list = new List<FsmStateAction>(s.Actions);
                    list.Insert(0, new Hook { R = r });
                    s.Actions = list.ToArray();
                }
                catch { continue; }
                hooked.Add(f);
                registers[r.Key] = r;
                Log.Info("magasin : caisse suivie " + r.Key);
            }
        }

        static Hashtable CarriedOf(PlayMakerFSM register)
        {
            GameObject inv = register.FsmVariables.GetFsmGameObject("Inventory").Value;
            if (inv == null) return null;
            foreach (PlayMakerHashTableProxy h in inv.GetComponents<PlayMakerHashTableProxy>())
                if (h.referenceName == "Carried") return h._hashTable;
            return null;
        }

        static void OnLocal(Register r)
        {
            if (!Session.Active) return;
            Hashtable carried = CarriedOf(r.Fsm);
            if (carried == null) return;
            var w = new NetWriter(Msg.Purchase).U8(Session.LocalId).Str(r.Key)
                .I32(r.Fsm.FsmVariables.GetFsmInt("BagStuff").Value).U16(carried.Count);
            var desc = new System.Text.StringBuilder();
            foreach (DictionaryEntry e in carried)
            {
                int q = e.Value is int ? (int)e.Value : 0;
                w.Str(e.Key.ToString()).I32(q);
                if (q > 0) desc.Append(e.Key).Append(" x").Append(q).Append(' ');
            }
            Log.Info("magasin : achat " + desc);
            Session.SendAll(w, true);
        }

        public static void OnMessage(Peer from, NetReader r)
        {
            int who = r.U8();
            if (Session.IsHost) who = from.Id;
            string key = r.Str();
            int bagStuff = r.I32();
            int n = r.U16();
            var items = new Hashtable();
            var desc = new System.Text.StringBuilder();
            for (int i = 0; i < n; i++)
            {
                string k = r.Str(); int q = r.I32();
                items[k] = q;
                if (q > 0) desc.Append(k).Append(" x").Append(q).Append(' ');
            }
            if (Session.IsHost)
            {
                var w = new NetWriter(Msg.Purchase).U8(who).Str(key).I32(bagStuff).U16(n);
                foreach (DictionaryEntry e in items) w.Str((string)e.Key).I32((int)e.Value);
                Session.Broadcast(w, true, who);
            }
            Register reg;
            if (!registers.TryGetValue(key, out reg) || reg.Fsm == null) { Scan(); registers.TryGetValue(key, out reg); }
            PlayerInfo pi;
            string name = Session.Players.TryGetValue(who, out pi) ? pi.Name : "?";
            Hud.Toast(name + " a fait des courses : " + desc);
            if (reg == null) { Log.Warn("magasin : caisse " + key + " introuvable ici"); return; }
            Hashtable carried = CarriedOf(reg.Fsm);
            if (carried == null) return;
            var keep = new Pending
            {
                R = reg, Carried = new Hashtable(carried), Until = Time.realtimeSinceStartup + 20f,
                PriceTotal = reg.Fsm.FsmVariables.GetFsmFloat("PriceTotal").Value,
                BagStuff = reg.Fsm.FsmVariables.GetFsmInt("BagStuff").Value,
            };
            carried.Clear();
            foreach (DictionaryEntry e in items) carried[e.Key] = e.Value;
            reg.Fsm.FsmVariables.GetFsmInt("BagStuff").Value = bagStuff;
            applying = true;
            try { Game.SetState(reg.Fsm, "Spawn bag"); }
            finally { applying = false; }
            pending.Add(keep);
            Log.Info("magasin : achat de " + name + " rejoue (" + desc + ")");
        }

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
    }
}
