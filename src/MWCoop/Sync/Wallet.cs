using System.Collections.Generic;
using HutongGames.PlayMaker;
using MWCoop.Net;
using UnityEngine;

namespace MWCoop
{
    // Argent (demande de JD) : chaque joueur a son porte-monnaie (achats chacun de son cote), mais
    // tout ce qui RENTRE (paie, ventes, recompenses) est recu par tous. Globales PlayerMoney (liquide)
    // et PlayerBankAccount (banque), comparees deux fois par seconde :
    //  - une hausse de la richesse totale est un revenu, envoye aux autres (sur le meme compte) ;
    //  - une baisse est une depense : elle reste locale ;
    //  - liquide <-> banque (distributeur, depot) est un transfert : rien a envoyer.
    //  - l'invite recoit la sauvegarde de l'hote a chaque connexion : son porte-monnaie est garde a
    //    part dans son profil (porte-monnaie.ini, une ligne par monde = PlayerID de la sauvegarde)
    //    et retabli a son retour dans ce monde.
    //  - versements DU MONDE (la logique du jeu paie d'elle-meme, a l'heure commune, chez CHAQUE joueur :
    //    ce n'est pas un joueur qui gagne) : jamais pris pour un revenu ordinaire (relayes, ils etaient
    //    doubles : chacun recevait le sien plus celui de l'autre). Voir Pays.
    public static class Wallet
    {
        // Boulots rejoues (Jobs) : la paie qu'ils versent ici n'est pas renvoyee, et elle « consomme »
        // le meme revenu annonce par le joueur qui a vraiment fait le boulot (pas de double paie).
        static float suppressUntil;
        static readonly System.Collections.Generic.List<KeyValuePair<float, float>> suppressed = new System.Collections.Generic.List<KeyValuePair<float, float>>();

        public static void Suppress(float seconds) { suppressUntil = Mathf.Max(suppressUntil, Time.realtimeSinceStartup + seconds); }

        static FsmFloat cash, bank;
        static float lastCash, lastBank, next, readyAt = -1, nextStore;
        static string world;

        // Versements du monde, reperes par leur etat de paiement (crochet au debut et a la fin de l'etat :
        // ecart exact de liquide et de banque, hors du releve ordinaire) :
        //  - partages (allocation, aide au logement, paies hebdomadaires des boulots) : chaque joueur envoie
        //    le montant que sa logique a verse (avec son numero de source) ; chacun garde, UNE fois, le plus
        //    haut montant vu pour ce versement pendant PayWindow : copie locale en trop retiree (celle de
        //    l'autre deja la, ou etat de paie rejoue une 2e fois par Jobs), ecart complete. Le plus haut :
        //    celui qui a vraiment fait le boulot (taxi conduit, publicites distribuees) a toute la progression ;
        //  - personnels (interets) : calcules sur le solde de chacun, gardes pour soi, jamais envoyes.
        // L'ordre de la liste est le numero de source envoye : le meme chez tous (meme version).
        class Pay
        {
            public readonly string Path, Fsm, State, Label;
            public readonly bool Shared;
            public PlayMakerFSM F;
            public bool Hooked, Open;
            public float C0, B0;                                        // releve au debut de l'etat
            public float At = -100, HaveC, HaveB, BestC, BestB, Sent;   // versement en cours : deja en poche, plus haut vu, envoye
            public Pay(string path, string fsm, string state, string label, bool shared) { Path = path; Fsm = fsm; State = state; Label = label; Shared = shared; }
        }
        static readonly Pay[] Pays =
        {
            new Pay("Systems/Expenses", "Kela", "Pay", "allocation chomage", true),                       // vendredi
            new Pay("Systems/Expenses", "Livingsupport", "Asumistuki", "aide au logement", true),          // vendredi
            new Pay("JOBS/FACTORY", "PlayerData", "Base salary", "paie de l'usine", true),                 // chaque semaine
            new Pay("JOBS/FACTORY", "PlayerData", "High salary", "paie de l'usine (heures sup)", true),
            new Pay("JOBS/ADs", "Data", "Bank transfer 2", "paie des publicites", true),                   // mardi et vendredi
            new Pay("JOBS/TAXIJOB/MACHTWAGEN/TaxiFunctions", "Payments", "Payment", "paie du taxi", true),  // mercredi
            new Pay("Systems/BankAccount", "Data", "Interest", "interets", false),                         // chaque jour
        };
        const float PayWindow = 30f;   // < 36 s : mardi -> vendredi des publicites en sommeil accelere (12 s par jour)
        static float nextPayScan;
        static int payHooked, worldLocal, worldIn;

        // Observe un etat de paiement. Pas une ModHook : ce n'est pas un rejeu, et l'audit compterait les
        // paies des boulots comme accrochees par deux modules (Jobs les suit deja).
        class PayHook : FsmStateAction
        {
            public Pay P;
            public bool End;
            public override void OnEnter()
            {
                try { if (End) PayEnd(P); else PayStart(P); } catch (System.Exception e) { Replay.HookError(e); }
                Finish();
            }
        }

        static string StorePath { get { return System.IO.Path.Combine(Log.DataDir, "porte-monnaie.ini"); } }

        static void Restore()
        {
            FsmFloat pid = FsmVariables.GlobalVariables.FindFsmFloat("PlayerID");
            world = pid != null ? pid.Value.ToString("F0") : "";
            if (Session.IsHost || world.Length == 0 || !System.IO.File.Exists(StorePath)) return;
            foreach (string l in System.IO.File.ReadAllLines(StorePath))
            {
                string[] f = l.Split('=');
                if (f.Length != 2 || f[0] != world) continue;
                string[] v = f[1].Split(';');
                float c, b;
                if (v.Length == 2 && float.TryParse(v[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out c)
                    && float.TryParse(v[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out b))
                {
                    cash.Value = c; bank.Value = b;
                    Log.Info("argent : porte-monnaie retrouve pour ce monde (" + world + ")");
                    Hud.Toast("Votre porte-monnaie : " + Mathf.RoundToInt(c) + " mk");
                }
            }
        }

        static void Store()
        {
            if (Session.IsHost || world == null || world.Length == 0) return;
            var lines = new System.Collections.Generic.List<string>();
            if (System.IO.File.Exists(StorePath))
                foreach (string l in System.IO.File.ReadAllLines(StorePath)) if (!l.StartsWith(world + "=")) lines.Add(l);
            lines.Add(world + "=" + cash.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) + ";" + bank.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
            try { System.IO.File.WriteAllLines(StorePath, lines.ToArray()); } catch { }
        }
        static bool ready;

        public static void OnLevelLoaded()
        {
            cash = bank = null;
            ready = false;
            readyAt = PlayerSync.InGame ? Time.realtimeSinceStartup + 15f : -1;   // apres le chargement de la sauvegarde
            foreach (Pay p in Pays) { p.F = null; p.Hooked = p.Open = false; p.At = -100; }
            nextPayScan = 0; payHooked = worldLocal = worldIn = 0;
            testStep = 0; remoteSince = -1;
        }

        // Accroche les etats de paiement des automates charges (objet actif) ; les autres (taxi pas encore la)
        // au prochain passage, toutes les 10 s.
        static void HookPays()
        {
            if (payHooked == Pays.Length || Time.realtimeSinceStartup < nextPayScan) return;
            nextPayScan = Time.realtimeSinceStartup + 10f;
            foreach (Pay p in Pays)
            {
                if (p.Hooked) continue;
                GameObject go = GameObject.Find(p.Path);   // actifs seulement : automate initialise
                PlayMakerFSM f = go != null && go.activeInHierarchy ? Game.FsmOn(go, p.Fsm) : null;
                if (f == null) continue;
                try
                {
                    FsmState st = f.Fsm.GetState(p.State);
                    if (st == null) { p.Hooked = true; payHooked++; Log.Warn("argent : etat " + p.State + " absent de " + p.Path + "::" + p.Fsm); continue; }
                    var list = new List<FsmStateAction>(st.Actions);
                    bool already = false;
                    foreach (FsmStateAction a in list) { var h = a as PayHook; if (h != null && h.P == p) already = true; }
                    if (!already)
                    {
                        list.Insert(0, new PayHook { P = p });
                        list.Add(new PayHook { P = p, End = true });
                        st.Actions = list.ToArray();
                    }
                }
                catch { continue; }   // automate pas encore pret : au prochain passage
                p.F = f; p.Hooked = true; payHooked++;
                Log.Info("argent : versement du monde suivi : " + p.Label + " (" + p.Path + "::" + p.Fsm + " / " + p.State + ", " + (p.Shared ? "compte une fois, au plus haut des joueurs" : "chacun le sien") + ")");
            }
        }

        // Debut de l'etat de paiement (avant ses actions) : releve du liquide et de la banque.
        static void PayStart(Pay p)
        {
            if (!ready || !Session.Active) return;
            p.C0 = cash.Value; p.B0 = bank.Value; p.Open = true;
        }

        // Fin de l'etat (apres ses actions ; sinon a la mise a jour suivante) : l'ecart est le versement.
        static void PayEnd(Pay p)
        {
            if (!p.Open) return;
            p.Open = false;
            Settle(p, cash.Value - p.C0, bank.Value - p.B0);
        }

        static void Settle(Pay p, float dC, float dB)
        {
            dC = Mathf.Max(dC, 0f); dB = Mathf.Max(dB, 0f);
            if (dC < 0.005f && dB < 0.005f) return;   // rien de verse
            lastCash += dC; lastBank += dB;            // hors du releve ordinaire : jamais renvoye comme un revenu
            if (!p.Shared) { Log.Info("argent : " + p.Label + " +" + (dC + dB) + ", gardes pour soi (calcules sur son propre solde)"); return; }
            worldLocal++;
            float adj = Merge(p, dC, dB, true);
            if (dC + dB > p.Sent + 0.005f)
            {
                p.Sent = dC + dB;
                Session.SendAll(new NetWriter(Msg.Income).U8(Session.LocalId).F32(dC).F32(dB).U8(System.Array.IndexOf(Pays, p) + 1), true);
            }
            Log.Info("argent : " + p.Label + " +" + (dC + dB) + " verse ici par le monde"
                     + (adj < -0.005f ? ", deja compte : " + (-adj) + " retire" : adj > 0.005f ? ", complete de " + adj : "") + " (une fois, au plus haut des joueurs)");
        }

        // Versement partage vu chez un autre joueur.
        static void WorldIn(Pay p, float c, float b, string name)
        {
            worldIn++;
            float adj = Merge(p, c, b, false);
            if (adj >= 1f) Hud.Toast("+" + Mathf.RoundToInt(adj) + " mk (" + p.Label + ")");
            Log.Info("argent : " + p.Label + " de " + name + " : " + (c + b) + (adj > 0.005f ? ", +" + adj + " ici" : ", deja compte ici"));
        }

        // Un montant de plus pour le versement en cours (ici : deja ajoute par le jeu ; ailleurs : pas encore) :
        // le porte-monnaie garde le plus haut vu, une seule fois. Rend l'ajustement fait.
        static float Merge(Pay p, float c, float b, bool local)
        {
            float now = Time.realtimeSinceStartup;
            if (now - p.At > PayWindow) { p.At = now; p.HaveC = p.HaveB = p.BestC = p.BestB = p.Sent = 0; }   // nouveau versement
            if (local) { p.HaveC += c; p.HaveB += b; }
            p.BestC = Mathf.Max(p.BestC, c); p.BestB = Mathf.Max(p.BestB, b);
            float dC = p.BestC - p.HaveC, dB = p.BestB - p.HaveB;
            if (Mathf.Abs(dC) >= 0.005f || Mathf.Abs(dB) >= 0.005f) Add(dC, dB);
            p.HaveC = p.BestC; p.HaveB = p.BestB;
            return dC + dB;
        }

        // Change le porte-monnaie sans que le releve ordinaire le prenne pour un revenu ou une depense.
        static void Add(float c, float b)
        {
            cash.Value += c; bank.Value += b;
            lastCash += c; lastBank += b;
        }

        // Audit : automate dont Wallet observe un etat de paiement.
        public static bool Observes(PlayMakerFSM f)
        {
            foreach (Pay p in Pays) if (p.F == f && f != null) return true;
            return false;
        }

        public static void Update()
        {
            if (!Session.Active || readyAt < 0 || Time.realtimeSinceStartup < readyAt) return;
            if (!ready)
            {
                cash = FsmVariables.GlobalVariables.FindFsmFloat("PlayerMoney");
                bank = FsmVariables.GlobalVariables.FindFsmFloat("PlayerBankAccount");
                if (cash == null || bank == null) { readyAt = -1; Log.Warn("argent : globales absentes"); return; }
                Restore();
                lastCash = cash.Value; lastBank = bank.Value;
                ready = true;
                Log.Info("argent : liquide " + lastCash + ", banque " + lastBank);
            }
            HookPays();
            foreach (Pay p in Pays) if (p.Open) PayEnd(p);   // fin d'etat pas vue (etat quitte avant la fin de ses actions)
            if (Time.realtimeSinceStartup >= nextStore) { nextStore = Time.realtimeSinceStartup + 10f; Store(); }
            if (Time.realtimeSinceStartup < next) return;
            next = Time.realtimeSinceStartup + 0.5f;
            float dC = cash.Value - lastCash, dB = bank.Value - lastBank;
            if (Mathf.Abs(dC) < 0.005f && Mathf.Abs(dB) < 0.005f) return;
            float transfer = 0;
            if (dC > 0 && dB < 0) transfer = Mathf.Min(dC, -dB);
            else if (dC < 0 && dB > 0) transfer = Mathf.Min(-dC, dB);
            float inC = dC > 0 ? dC - (dB < 0 ? transfer : 0) : 0;
            float inB = dB > 0 ? dB - (dC < 0 ? transfer : 0) : 0;
            lastCash = cash.Value; lastBank = bank.Value;
            if (inC < 0.005f && inB < 0.005f) return;
            if (Time.realtimeSinceStartup < suppressUntil)
            {
                suppressed.Add(new KeyValuePair<float, float>(Time.realtimeSinceStartup, inC + inB));
                Log.Info("argent : revenu " + (inC + inB) + " venu d'un boulot rejoue, garde pour soi");
                return;
            }
            Log.Info("argent : revenu " + inC + " (liquide) + " + inB + " (banque), partage");
            Session.SendAll(new NetWriter(Msg.Income).U8(Session.LocalId).F32(inC).F32(inB), true);
        }

        public static void OnMessage(Peer from, NetReader r)
        {
            int who = r.U8();
            if (Session.IsHost) who = from.Id;
            float inC = r.F32(), inB = r.F32();
            int src = r.More ? r.U8() : 0;   // versement du monde (numero dans Pays), 0 : revenu d'un joueur
            if (Session.IsHost)
            {
                var w = new NetWriter(Msg.Income).U8(who).F32(inC).F32(inB);
                if (src > 0) w.U8(src);
                Session.Broadcast(w, true, who);
            }
            if (!ready || inC < 0 || inB < 0 || inC + inB > 1e6f) return;
            PlayerInfo pi;
            string name = Session.Players.TryGetValue(who, out pi) ? pi.Name : "?";
            if (src > 0) { if (src <= Pays.Length && Pays[src - 1].Shared) WorldIn(Pays[src - 1], inC, inB, name); return; }
            // Deja recu par le boulot rejoue ici ?
            suppressed.RemoveAll(s => Time.realtimeSinceStartup - s.Key > 20f);
            int hit = suppressed.FindIndex(s => Mathf.Abs(s.Value - (inC + inB)) < 0.5f);
            if (hit >= 0) { suppressed.RemoveAt(hit); Log.Info("argent : revenu " + (inC + inB) + " deja verse par le boulot rejoue"); return; }
            cash.Value += inC; bank.Value += inB;
            lastCash += inC; lastBank += inB;   // pas de renvoi
            Hud.Toast("+" + Mathf.RoundToInt(inC + inB) + " mk (revenu de " + name + ")");
            Log.Info("argent : +" + inC + " liquide, +" + inB + " banque, de " + name);
        }

        // Essais (Autotest) 'revenu' : a 25 s, etat ; a 30 s (l'hote attend en plus un invite en partie depuis
        // 20 s, porte-monnaie pret), le monde verse ici l'aide au logement de la semaine, chez l'hote comme
        // chez l'invite (comme chaque vendredi) ; 10 s plus tard, les interets du jour ; 22 s apres, etat.
        // Attendu des deux cotes : l'aide comptee UNE fois, les interets de chacun gardes pour soi, aucun
        // « argent : +... de » (revenu ordinaire) pour ces versements.
        static int testStep;
        static float testAt, remoteSince = -1;

        public static void Test(string mode, float t)
        {
            if (mode != "revenu" || !ready) return;
            float now = Time.realtimeSinceStartup;
            if (testStep == 0 && t > 25f) { testStep = 1; Log.Info("autotest : revenu, avant : " + State()); }
            if (testStep == 1 && t > 30f && (!Session.IsHost || GuestIn(now)))
            {
                testStep = 2; testAt = now;
                Log.Info("autotest : revenu, le monde verse l'aide au logement ici : " + Force("Systems/Expenses", "Livingsupport", "Asumistuki") + ", " + State());
            }
            if (testStep == 2 && now - testAt > 10f) { testStep = 3; Log.Info("autotest : revenu, interets du jour ici : " + Force("Systems/BankAccount", "Data", "State 1") + ", " + State()); }
            if (testStep == 3 && now - testAt > 22f)
            {
                testStep = 4;
                Log.Info("autotest : revenu, apres : " + State() + " (versements du monde : " + worldLocal + " ici, " + worldIn + " recus)");
            }
        }

        // Hote : un invite est en partie depuis 20 s.
        static bool GuestIn(float now)
        {
            bool any = false;
            foreach (PlayerInfo p in Session.Players.Values) if (!p.Local && p.Level == 1 && now - p.StateTime < 3f) any = true;
            if (!any) { remoteSince = -1; return false; }
            if (remoteSince < 0) remoteSince = now;
            return now - remoteSince > 20f;
        }

        // Force l'etat 'state' de l'automate 'path::fsm' d'un versement suivi (comme le jour venu).
        static string Force(string path, string fsm, string state)
        {
            foreach (Pay p in Pays)
                if (p.Path == path && p.Fsm == fsm && p.F != null) return Game.SetState(p.F, state) ? p.Path + "::" + fsm + " -> " + p.F.ActiveStateName : "etat " + state + " absent";
            return "automate " + fsm + " pas suivi";
        }

        // Essais : gagne (ou depense) 'amount' en liquide, comme une paie ou un achat.
        public static string Test(float amount)
        {
            if (!ready) return "argent pas pret";
            cash.Value += amount;
            return "liquide " + cash.Value;
        }

        public static string State() { return ready ? "liquide " + cash.Value + ", banque " + bank.Value : "?"; }
    }
}
