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
    //  - gains declenches par un OBJET (ferraille jetee dans la benne de Fleetari, billet de loto gagnant jete a la
    //    poubelle du Voittous, objets vendus a la table des puces, kilju vendu a Jokke) : le declencheur tourne chez
    //    chaque joueur ou la copie Props de l'objet tombe, et chacun se payait (puis relayait). Le gain va une fois, a
    //    celui qui a mis l'objet (Home.CausedHere) ; chez les autres les ecritures d'argent de l'etat sont coupees
    //    (Trigs). Il reste a lui : l'enveloppe qu'il touche (ScrapMoney, MoneyFlea, PayMoney de Jokke) et la banque du
    //    loto sont des versements personnels (Pays, gardes pour soi), et ces enveloppes sont a ce module (le monde n'y
    //    rejoue plus le clic d'un autre, qui vidait la sienne). Voir Trigs et Puces.
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
            public readonly bool Shared, Own;                           // Own : enveloppe reservee a ce module (Claim)
            public PlayMakerFSM F;
            public bool Hooked, Open;
            public float C0, B0;                                        // releve au debut de l'etat
            public float At = -100, HaveC, HaveB, BestC, BestB, Sent;   // versement en cours : deja en poche, plus haut vu, envoye
            public Pay(string path, string fsm, string state, string label, bool shared, bool own = false) { Path = path; Fsm = fsm; State = state; Label = label; Shared = shared; Own = own; }
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
            // Gains d'un objet mis par ce joueur (voir Trigs) : touches par lui seul, gardes pour soi.
            new Pay("REPAIRSHOP/LOD/Office/Fleetari/ScrapMoney", "Use", "State 1", "rachat de ferraille", false, true),
            new Pay("FleaMarket/LOD/OpenHours/MoneyFlea", "Use", "State 1", "ventes aux puces", false, true),
            new Pay(JokkePay, "Use", "State 1", "kilju vendu a Jokke", false, true),
            new Pay(LottoPath, "Logic", "Bank", "gains du loto", false),
            // Prix des courses (podium de CE joueur, calcule chez lui) : a lui, gardes pour soi.
            new Pay("RACES/RALLY/SS3/FinishArea/Stuff/PriceMoneyRally", "Use", "State 1", "prix du rallye", false, true),
            new Pay("RACES/ICERACE/TentContents/Prices/PriceMoneyRace", "Use", "State 1", "prix de la course sur glace", false, true),
        };
        const string JokkePay = "JOBS/JOKKEHOME/HouseDrunkNew/KiljuBuyer/Char/skeleton/pelvis/spine_middle/spine_upper/collar_left/shoulder_left/arm_left/hand_left/PayMoney";
        const string ScrapPath = "REPAIRSHOP/Scrapmetal/GarbageTrigger", LottoPath = "PERAPORTTI/Building/LOD100/Store/VoittousArea/TrashTrigger";
        const float PayWindow = 30f;   // < 36 s : mardi -> vendredi des publicites en sommeil accelere (12 s par jour)
        const int FirstGain = 7;       // rang du 1er gain d'objet dans Pays
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

        // ---------------------------------------------------------------- don entre joueurs (porte-monnaie, WalletPanel)
        // Le donneur retire la somme de son liquide, le receveur l'ajoute au sien ; ni l'un ni l'autre n'est un revenu
        // ou une depense pour le releve (pas renvoye aux autres comme une paie).
        public static bool Ready { get { return ready && cash != null; } }
        public static float Cash { get { return cash != null ? cash.Value : 0f; } }

        public static string Give(int to, int amount)
        {
            if (!Ready || !Session.Active) return Lang.T("Porte-monnaie pas pr\u00EAt", "Wallet not ready");
            PlayerInfo pi;
            if (!Session.Players.TryGetValue(to, out pi) || pi.Local) return Lang.T("Joueur introuvable", "Player not found");
            if (amount <= 0) return Lang.T("Montant nul", "Nothing to give");
            if (cash.Value < amount) return Lang.T("Pas assez de liquide", "Not enough cash");
            cash.Value -= amount; lastCash -= amount;
            Session.SendAll(new NetWriter(Msg.Job).U8(Session.LocalId).Str("@don").U8((byte)to).I32(amount), true);
            Log.Info("argent : " + amount + " mk donnes a " + pi.Name + " (#" + to + ")");
            Store();
            return null;
        }

        public static void OnGift(int who, int to, int amount)
        {
            if (to != Session.LocalId || amount <= 0 || amount > 1000000) return;
            PlayerInfo pi;
            string name = Session.Players.TryGetValue(who, out pi) ? pi.Name : "#" + who;
            if (!Ready) { pendingGift += amount; pendingFrom = name; Log.Info("argent : " + amount + " mk recus de " + name + " (porte-monnaie pas pret : en attente)"); return; }
            cash.Value += amount; lastCash += amount;
            Log.Info("argent : " + amount + " mk recus de " + name);
            Hud.Toast(name + Lang.T(" vous a donn\u00E9 ", " gave you ") + amount + " mk");
            Store();
        }
        static int pendingGift; static string pendingFrom;

        // Etat du corps de l'invite, garde comme son porte-monnaie (etat-joueur.ini, une ligne par monde) : la
        // sauvegarde recue a chaque connexion est celle de l'hote, et ces globales y sont les SIENNES -- l'invite
        // reprenait sinon la faim, la soif, la fatigue, l'ivresse... de l'hote a chaque session. Les cles de vehicules,
        // amendes, nom et adresse restent ceux de la partie (communs).
        static readonly string[] SelfFloats = { "PlayerHunger", "PlayerThirst", "PlayerFatigue", "PlayerStress", "PlayerDirtiness", "PlayerUrine",
            "PlayerDrunk", "PlayerDrunkAdjusted", "PlayerAlcoholism", "PlayerSweat", "PlayerTemp", "PlayerWeight", "PlayerBurns", "PlayerAllergy",
            "PlayerBerryPickSkill" };
        static readonly string[] SelfInts = { "PlayerCigarettes" };
        static string SelfPath { get { return System.IO.Path.Combine(Log.DataDir, "etat-joueur.ini"); } }
        static readonly System.Globalization.CultureInfo Inv = System.Globalization.CultureInfo.InvariantCulture;

        static string SelfLine()
        {
            var sb = new System.Text.StringBuilder();
            foreach (string n in SelfFloats)
            {
                FsmFloat v = FsmVariables.GlobalVariables.FindFsmFloat(n);
                if (v != null) sb.Append(sb.Length > 0 ? ";" : "").Append(n).Append(':').Append(v.Value.ToString("R", Inv));
            }
            foreach (string n in SelfInts)
            {
                FsmInt v = FsmVariables.GlobalVariables.FindFsmInt(n);
                if (v != null) sb.Append(sb.Length > 0 ? ";" : "").Append(n).Append(':').Append(v.Value.ToString(Inv));
            }
            return sb.ToString();
        }

        static void RestoreSelf()
        {
            if (Session.IsHost || world.Length == 0 || !System.IO.File.Exists(SelfPath)) return;
            foreach (string l in System.IO.File.ReadAllLines(SelfPath))
            {
                int eq = l.IndexOf('=');
                if (eq <= 0 || l.Substring(0, eq) != world) continue;
                int n = 0;
                foreach (string kv in l.Substring(eq + 1).Split(';'))
                {
                    int c = kv.IndexOf(':');
                    if (c <= 0) continue;
                    string name = kv.Substring(0, c), val = kv.Substring(c + 1);
                    float f; int i;
                    FsmFloat vf = System.Array.IndexOf(SelfFloats, name) >= 0 ? FsmVariables.GlobalVariables.FindFsmFloat(name) : null;
                    FsmInt vi = System.Array.IndexOf(SelfInts, name) >= 0 ? FsmVariables.GlobalVariables.FindFsmInt(name) : null;
                    if (vf != null && float.TryParse(val, System.Globalization.NumberStyles.Float, Inv, out f)) { vf.Value = f; n++; }
                    else if (vi != null && int.TryParse(val, System.Globalization.NumberStyles.Integer, Inv, out i)) { vi.Value = i; n++; }
                }
                Log.Info("joueur : etat du corps retrouve pour ce monde (" + n + " valeurs : faim, soif, fatigue...)");
            }
        }

        static void StoreSelf()
        {
            if (Session.IsHost || world == null || world.Length == 0) return;
            var lines = new List<string>();
            if (System.IO.File.Exists(SelfPath))
                foreach (string l in System.IO.File.ReadAllLines(SelfPath)) if (!l.StartsWith(world + "=")) lines.Add(l);
            lines.Add(world + "=" + SelfLine());
            try { System.IO.File.WriteAllLines(SelfPath, lines.ToArray()); } catch { }
        }

        static void Restore()
        {
            FsmFloat pid = FsmVariables.GlobalVariables.FindFsmFloat("PlayerID");
            world = pid != null ? pid.Value.ToString("F0") : "";
            RestoreSelf();
            if (Session.IsHost || world.Length == 0) return;
            if (System.IO.File.Exists(StorePath)) foreach (string l in System.IO.File.ReadAllLines(StorePath))
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
                    Hud.Toast(Lang.T("Votre porte-monnaie : ", "Your wallet: ") + Mathf.RoundToInt(c) + " mk");
                    return;
                }
            }
            FirstVisit();
        }

        // Premiere venue d'un invite dans ce monde : l'argent d'une nouvelle partie, pas celui de l'hote a cet instant
        // (demande de JD, 10/10). Valeurs relevees au menu principal (globales du jeu avant tout chargement) ; [Coop]
        // ArgentDepart=0 : comme avant (l'argent de la sauvegarde de l'hote).
        static float startCash = -1, startBank = -1;
        static void FirstVisit()
        {
            if (Session.IsHost || Config.GetInt("Coop", "ArgentDepart", 1) == 0) return;
            float c = Config.GetFloat("Coop", "ArgentDepartLiquide", startCash), b = Config.GetFloat("Coop", "ArgentDepartBanque", startBank);
            if (c < 0 || b < 0) { Log.Info("argent : premiere venue dans ce monde, argent de depart inconnu : celui de la sauvegarde"); return; }
            Log.Info("argent : premiere venue dans ce monde (" + world + ") : argent de depart " + c + " liquide, " + b + " banque (au lieu de " + cash.Value + " / " + bank.Value + ")");
            cash.Value = c; bank.Value = b;
            Hud.Toast(Lang.T("Premi\u00E8re venue : ", "First visit: ") + Mathf.RoundToInt(c + b) + Lang.T(" mk pour commencer", " mk to start with"));
        }

        static void Store()
        {
            if (Session.IsHost || world == null || world.Length == 0) return;
            var lines = new System.Collections.Generic.List<string>();
            if (System.IO.File.Exists(StorePath))
                foreach (string l in System.IO.File.ReadAllLines(StorePath)) if (!l.StartsWith(world + "=")) lines.Add(l);
            lines.Add(world + "=" + cash.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) + ";" + bank.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
            try { System.IO.File.WriteAllLines(StorePath, lines.ToArray()); } catch { }
            StoreSelf();
        }
        static bool ready;

        public static void OnLevelLoaded()
        {
            if (Application.loadedLevelName == "MainMenu" && startCash < 0)
            {
                FsmFloat mc = FsmVariables.GlobalVariables.FindFsmFloat("PlayerMoney"), mb = FsmVariables.GlobalVariables.FindFsmFloat("PlayerBankAccount");
                if (mc != null && mb != null) { startCash = mc.Value; startBank = mb.Value; Log.Info("argent : au menu (nouvelle partie) " + startCash + " liquide, " + startBank + " banque"); }
            }
            cash = bank = null;
            ready = false;
            readyAt = PlayerSync.InGame ? Time.realtimeSinceStartup + 15f : -1;   // apres le chargement de la sauvegarde
            foreach (Pay p in Pays) { p.F = null; p.Hooked = p.Open = false; p.At = -100; }
            nextPayScan = 0; payHooked = worldLocal = worldIn = 0;
            testStep = 0; remoteSince = -1;
            UnmuteTrigs();
            foreach (Trig g in Trigs) { g.F = null; g.Hooked = false; }
            envs.Clear(); envIncomeUntil = 0; recentIn.Clear();
            claimAt = PlayerSync.InGame ? Time.realtimeSinceStartup + 4f : -1;
            saleLogic = saleSell = null; saleHooked = saleOpen = sellOffLogged = false; sellers.Clear();
            trigLocal = trigOther = 0;
        }

        // Accroche les etats de paiement des automates charges (objet actif) ; les autres (taxi pas encore la, enveloppe
        // pas encore sortie) au prochain passage, toutes les 2 s : une enveloppe touchee juste apres son apparition doit
        // deja etre suivie (sinon son argent passait pour un revenu ordinaire, partage).
        static void HookPays()
        {
            if (payHooked == Pays.Length || Time.realtimeSinceStartup < nextPayScan) return;
            nextPayScan = Time.realtimeSinceStartup + 2f;
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
            if (!p.Shared)
            {
                bool gain = System.Array.IndexOf(Pays, p) >= FirstGain;
                Log.Info("argent : " + p.Label + " +" + (dC + dB) + (gain ? ", garde pour soi (gain d'un objet mis par ce joueur, pas partage)" : ", gardes pour soi (calcules sur son propre solde)"));
                return;
            }
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
            if (f == null) return false;
            foreach (Pay p in Pays) if (p.F == f) return true;
            return f == saleLogic;
        }

        // ================================================================ gains declenches par un objet
        // Declencheur qui paie quand un objet y tombe : 'Object' = l'objet ; ses etats d'argent n'ecrivent rien chez un
        // joueur qui ne l'a pas mis la (sa copie Props y est tombee aussi). Ces automates (sans saisie ni sauvegarde) ne
        // sont rejoues par personne : ils sont a ce module.
        //  - benne a ferraille de Fleetari : 'State 3' (AddFsmFloat : valeur du metal ajoutee a l'enveloppe ScrapMoney) ;
        //  - poubelle du Voittous (billets de loto, megaveto) : 'State 4', 'Bank' (gain vire a la banque, releve).
        // Le billet ou la piece part a la decharge chez tous ('Turn into garbage' n'est pas coupe).
        class Trig
        {
            public readonly string Path, Fsm, Label; public readonly string[] States;
            public PlayMakerFSM F; public bool Hooked;
            public Trig(string path, string fsm, string label, params string[] states) { Path = path; Fsm = fsm; Label = label; States = states; }
        }
        static readonly Trig[] Trigs =
        {
            new Trig(ScrapPath, "Logic", "ferraille", "State 3"),
            new Trig(LottoPath, "Logic", "loto", "State 4", "Bank"),
        };
        static readonly List<FsmStateAction> trigMuted = new List<FsmStateAction>();
        static float claimAt = -1, nextTrigScan;
        static int trigLocal, trigOther;

        class TrigHook : ModHook
        {
            public override string Module { get { return "argent"; } }
            public Trig G; public FsmState St;
            public override void OnEnter()
            {
                try { if (Session.Active && Replay.Depth == 0) OnTrig(this); } catch (System.Exception e) { Replay.HookError(e); }
                Finish();
            }
        }

        // Avant le releve du monde (16 s) : enveloppes et declencheurs a ce module.
        static void ClaimEarly()
        {
            var sb = new System.Text.StringBuilder("argent : reserves a ce module :");
            foreach (Pay p in Pays) if (p.Own) sb.Append(' ').Append(ClaimAt(p.Path, p.Fsm));
            foreach (Trig g in Trigs) sb.Append(' ').Append(ClaimAt(g.Path, g.Fsm));
            sb.Append(", ").Append(ClaimEnvelopes());
            Log.Info(sb.ToString());
        }

        static string ClaimAt(string path, string fsm)
        {
            GameObject go = Game.FindAny(path);
            PlayMakerFSM f = go != null ? Game.FsmOn(go, fsm) : null;
            string n = path.Substring(path.LastIndexOf('/') + 1);
            if (f == null) return n + " (absent)";
            return Replay.Claim(f, "argent") ? n : n + " (deja a " + Replay.Owner(f) + ")";
        }

        static void HookTrigs()
        {
            if (Time.realtimeSinceStartup < nextTrigScan) return;
            nextTrigScan = Time.realtimeSinceStartup + 2f;
            foreach (Trig g in Trigs)
            {
                if (g.Hooked) continue;
                GameObject go = GameObject.Find(g.Path);
                PlayMakerFSM f = go != null && go.activeInHierarchy ? Game.FsmOn(go, g.Fsm) : null;
                if (f == null || Replay.Owner(f) != "argent") continue;
                try
                {
                    foreach (string s in g.States)
                    {
                        FsmState st = f.Fsm.GetState(s);
                        if (st == null) { Log.Warn("argent : etat " + s + " absent de " + g.Path); continue; }
                        if (System.Array.Exists(st.Actions, a => a is TrigHook)) continue;
                        var list = new List<FsmStateAction>(st.Actions);
                        list.Insert(0, new TrigHook { G = g, St = st });
                        st.Actions = list.ToArray();
                    }
                }
                catch { continue; }   // pas encore pret
                g.F = f; g.Hooked = true;
                Log.Info("argent : declencheur suivi : " + g.Label + " (" + g.Path + "::" + g.Fsm + "), paye seulement chez celui qui y met l'objet");
            }
        }

        // Ecritures d'argent : vers un autre automate (AddFsmFloat, SetFsmString : releve de banque, SendEventByName :
        // avis), ou vers une globale (PlayerBankAccount, PlayerMoney).
        static bool MoneyAction(PlayMakerFSM f, FsmStateAction a)
        {
            string n = a.GetType().Name;
            if (n == "AddFsmFloat" || n == "SetFsmFloat" || n == "SubtractFsmFloat" || n == "SetFsmInt" || n == "AddToFsmInt"
                || n == "SetFsmString" || n == "SetFsmBool" || n == "SendEventByName") return true;
            foreach (System.Reflection.FieldInfo fi in a.GetType().GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
            {
                if (fi.Name != "floatVariable" && fi.Name != "intVariable" && fi.Name != "storeResult") continue;
                var nv = fi.GetValue(a) as NamedVariable;
                if (nv != null && nv.UseVariable && !Game.LocalVar(f, nv.Name)) return true;
            }
            return false;
        }

        static void OnTrig(TrigHook h)
        {
            Trig g = h.G;
            FsmGameObject ov = g.F != null ? g.F.FsmVariables.FindFsmGameObject("Object") : null;
            GameObject obj = ov != null ? ov.Value : null;
            if (Home.CausedHere(obj, 120f))
            {
                trigLocal++;
                Log.Info("argent : " + g.Label + " (" + h.St.Name + ") : " + (obj != null ? obj.name : "?") + " mis par ce joueur-ci, paye ici");
                return;
            }
            int n = 0;
            bool after = false;
            foreach (FsmStateAction a in h.St.Actions)
            {
                if (a == h) { after = true; continue; }
                if (!after || a == null || !a.Enabled || !MoneyAction(g.F, a)) continue;
                a.Enabled = false; trigMuted.Add(a); n++;
            }
            trigOther++;
            Log.Info("argent : " + g.Label + " (" + h.St.Name + ") : " + (obj != null ? obj.name : "?") + " mis par un autre joueur, rien verse ici (" + n + " actions coupees)");
        }

        static void UnmuteTrigs()
        {
            if (trigMuted.Count == 0) return;
            foreach (FsmStateAction a in trigMuted) if (a != null) a.Enabled = true;
            trigMuted.Clear();
        }

        // ================================================================ table des puces (objets vendus par le joueur)
        // FleaMarket/SaleTable : on y pose des objets a vendre ('Freeze object' : prix, ID tire au hasard) ; l'automate
        // 'Sell' tire au hasard toutes les 30 s une vente (SELL) ; 'Find item' / 'Find item 2' retirent l'objet vendu et
        // ajoutent son prix a MoneyTotal, verse en fin de location dans l'enveloppe MoneyFlea (State 2). Chacun tirait ses
        // propres ventes (autres objets, autres sommes) et touchait sa propre enveloppe.
        //  - seul l'hote vend : 'Sell' arrete chez les invites (et une vente faite quand meme chez un invite par sa logique
        //    n'ajoute rien a son MoneyTotal) ; l'objet vendu disparait chez les autres par Props (pose recalee par l'hote) ;
        //  - le vendeur est celui qui a pose l'objet ('Freeze object' chez celui qui le tenait : un invite l'annonce a
        //    l'hote) ; une vente d'un objet d'invite est retiree du MoneyTotal de l'hote et creditee au MoneyTotal de
        //    l'invite (K_FleaCredit) : chacun touche ses ventes dans sa propre enveloppe, une fois.
        static PlayMakerFSM saleLogic, saleSell;
        static bool saleHooked, saleOpen, sellOffLogged;
        static float saleStart, saleAt, nextSaleCheck;
        static readonly Dictionary<string, int> sellers = new Dictionary<string, int>();

        class FleaHook : FsmStateAction
        {
            public int Kind;   // 0 objet pose, 1 debut de vente, 2 fin de vente
            public override void OnEnter()
            {
                try { if (Session.Active) OnFleaHook(Kind); } catch (System.Exception e) { Replay.HookError(e); }
                Finish();
            }
        }

        static FsmFloat MoneyTotal { get { return saleLogic != null ? saleLogic.FsmVariables.FindFsmFloat("MoneyTotal") : null; } }

        static string ItemKey(GameObject go)
        {
            if (go == null) return "";
            string id = Props.ItemId(go);
            return id.Length > 0 ? id : go.name;
        }

        static void FleaStep()
        {
            float now = Time.realtimeSinceStartup;
            if (now < nextSaleCheck) return;
            nextSaleCheck = now + 1f;
            if (saleLogic == null)
            {
                GameObject st = GameObject.Find("FleaMarket/SaleTable");
                if (st == null) return;
                saleLogic = Game.FsmOn(st, "Logic");
                saleSell = Game.FsmOn(st, "Sell");
            }
            if (!saleHooked && saleLogic != null && saleLogic.gameObject.activeInHierarchy)
            {
                try
                {
                    AddFlea("Freeze object", 0, false);
                    AddFlea("Find item", 1, false); AddFlea("Find item", 2, true);
                    AddFlea("Find item 2", 1, false); AddFlea("Find item 2", 2, true);
                    saleHooked = true;
                    Log.Info("argent : table des puces suivie (" + (Session.IsHost ? "l'hote vend" : "les ventes viennent de l'hote") + ")");
                }
                catch { }
            }
            if (!Session.IsHost && saleSell != null && saleSell.enabled)
            {
                saleSell.enabled = false;
                if (!sellOffLogged) { sellOffLogged = true; Log.Info("argent : ventes des puces tirees par l'hote seul ('Sell' arrete ici)"); }
            }
            if (saleOpen && now - saleAt > 2f) SettleSale();   // fin de 'Find item' pas vue
        }

        static void AddFlea(string state, int kind, bool end)
        {
            FsmState st = saleLogic.Fsm.GetState(state);
            if (st == null) return;
            foreach (FsmStateAction a in st.Actions) { var h = a as FleaHook; if (h != null && h.Kind == kind) return; }
            var list = new List<FsmStateAction>(st.Actions);
            if (end) list.Add(new FleaHook { Kind = kind }); else list.Insert(0, new FleaHook { Kind = kind });
            st.Actions = list.ToArray();
        }

        static void OnFleaHook(int kind)
        {
            if (kind == 0)
            {
                if (Replay.Depth > 0) return;
                FsmGameObject iv = saleLogic.FsmVariables.FindFsmGameObject("Item");
                GameObject item = iv != null ? iv.Value : null;
                if (item == null || Home.HeldAgo(item) > 60f) return;   // copie d'un objet pose par un autre
                string key = ItemKey(item);
                if (Session.IsHost) sellers[key] = Session.LocalId;
                else Session.SendAll(new NetWriter(Msg.Home).U8(Session.LocalId).U8(Home.K_FleaPlaced).Str(key), true);
                Log.Info("argent : " + key + " mis en vente aux puces par ce joueur-ci" + (Session.IsHost ? "" : " (annonce a l'hote)"));
                return;
            }
            FsmFloat mt = MoneyTotal;
            if (mt == null) return;
            if (kind == 1) { saleOpen = true; saleStart = mt.Value; saleAt = Time.realtimeSinceStartup; return; }
            SettleSale();
        }

        // Home : U8 joueur, U8 K_FleaCredit, U8 vendeur, F32 montant, Str objet (hote -> tous ; seul le vendeur l'ajoute).
        static void SettleSale()
        {
            if (!saleOpen) return;
            saleOpen = false;
            FsmFloat mt = MoneyTotal;
            if (mt == null) return;
            float d = mt.Value - saleStart;
            if (d <= 0.005f) return;
            if (!Session.IsHost)
            {
                if (Replay.Depth > 0) return;
                mt.Value = saleStart;
                Log.Info("argent : vente aux puces tiree par la logique de cet invite (+" + d + ") : pas comptee (l'hote vend)");
                return;
            }
            FsmGameObject sv = saleLogic.FsmVariables.FindFsmGameObject("ItemSold"), iv = saleLogic.FsmVariables.FindFsmGameObject("Item");
            GameObject sold = sv != null && sv.Value != null ? sv.Value : iv != null ? iv.Value : null;
            string key = ItemKey(sold);
            int seller;
            if (!sellers.TryGetValue(key, out seller)) seller = Session.LocalId;
            if (seller == Session.LocalId || !Session.Players.ContainsKey(seller))
            {
                Log.Info("argent : " + key + " vendu aux puces +" + d + " (enveloppe de l'hote" + (seller != Session.LocalId ? ", vendeur #" + seller + " parti" : "") + ")");
                return;
            }
            mt.Value -= d;
            sellers.Remove(key);
            Session.SendAll(new NetWriter(Msg.Home).U8(Session.LocalId).U8(Home.K_FleaCredit).U8(seller).F32(d).Str(key), true);
            Log.Info("argent : " + key + " vendu aux puces +" + d + " pour #" + seller + " (retire ici, credite chez lui)");
        }

        // Messages de la table des puces (recus par Home).
        public static void OnFlea(int who, int kind, NetReader r)
        {
            if (kind == Home.K_FleaPlaced)
            {
                string key = r.Str();
                if (!Session.IsHost) return;
                sellers[key] = who;
                Log.Info("argent : " + key + " mis en vente aux puces par #" + who);
                return;
            }
            int seller = r.U8();
            float amount = r.F32();
            string item = r.Str();
            if (seller != Session.LocalId || amount <= 0f || amount > 1e6f) return;
            if (saleLogic == null) { GameObject st = Game.FindAny("FleaMarket/SaleTable"); saleLogic = st != null ? Game.FsmOn(st, "Logic") : null; }
            FsmFloat mt = MoneyTotal;
            if (mt == null) { Log.Warn("argent : vente aux puces de " + item + " (+" + amount + ") perdue : table absente ici"); return; }
            mt.Value += amount;
            Hud.Toast(item + Lang.T(" vendu aux puces : +", " sold at the flea market: +") + Mathf.RoundToInt(amount) + Lang.T(" mk (enveloppe \u00E0 la fin de la location)", " mk (envelope when the rental ends)"));
            Log.Info("argent : " + item + " vendu aux puces par l'hote pour ce joueur : +" + amount + " (MoneyTotal " + mt.Value + ")");
        }

        public static void Update()
        {
            UnmuteTrigs();   // ecritures d'argent coupees pour un seul passage (etat deja entre)
            if (Session.Active && claimAt > 0 && Time.realtimeSinceStartup >= claimAt) { claimAt = -1; ClaimEarly(); }
            if (!Session.Active || readyAt < 0 || Time.realtimeSinceStartup < readyAt) return;
            if (!ready)
            {
                cash = FsmVariables.GlobalVariables.FindFsmFloat("PlayerMoney");
                bank = FsmVariables.GlobalVariables.FindFsmFloat("PlayerBankAccount");
                if (cash == null || bank == null) { readyAt = -1; Log.Warn("argent : globales absentes"); return; }
                Restore();
                if (pendingGift > 0) { cash.Value += pendingGift; Hud.Toast(pendingFrom + Lang.T(" vous a donn\u00E9 ", " gave you ") + pendingGift + " mk"); pendingGift = 0; }
                lastCash = cash.Value; lastBank = bank.Value;
                ready = true;
                Log.Info("argent : liquide " + lastCash + ", banque " + lastBank);
            }
            HookPays();
            HookTrigs();
            HookEnvelopes();
            FleaStep();
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
            float nowU = Time.realtimeSinceStartup;
            if (nowU < suppressUntil && nowU >= envIncomeUntil)
            {
                // Le meme revenu deja recu de celui qui a fait le boulot (son annonce arrivee AVANT que le boulot
                // rejoue ici ne paie) : cette copie est en trop, retiree. Sinon gardee, et l'annonce a venir l'usera.
                recentIn.RemoveAll(x => nowU - x.Key > 20f);
                int got = recentIn.FindIndex(x => Mathf.Abs(x.Value - (inC + inB)) < 0.5f);
                if (got >= 0)
                {
                    recentIn.RemoveAt(got);
                    cash.Value -= inC; bank.Value -= inB;
                    lastCash = cash.Value; lastBank = bank.Value;
                    Log.Info("argent : revenu " + (inC + inB) + " du boulot rejoue deja recu de celui qui l'a fait : retire");
                    return;
                }
                suppressed.Add(new KeyValuePair<float, float>(nowU, inC + inB));
                Log.Info("argent : revenu " + (inC + inB) + " venu d'un boulot rejoue, garde pour soi");
                return;
            }
            if (Machines.KeepsMoney) { Log.Info("argent : gain " + (inC + inB) + " d'une machine a jeu, garde pour soi"); return; }   // encaissement, gain pris : pas un revenu a partager
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
            recentIn.Add(new KeyValuePair<float, float>(Time.realtimeSinceStartup, inC + inB));
            Hud.Toast("+" + Mathf.RoundToInt(inC + inB) + Lang.T(" mk (revenu de ", " mk (income from ") + name + ")");
            Log.Info("argent : +" + inC + " liquide, +" + inB + " banque, de " + name);
        }

        // ---------------------------------------------------------------- enveloppes de paie
        // Le client d'un boulot (fosses septiques HouseShit*, livraisons de bois HouseWood*, fermier) ou l'organisateur
        // (pas les prix des courses, PriceMoney* : podium de chacun, voir Pays) tend une enveloppe : PayMoney :: Use. Clic -> "State 1" (PlayerMoney +=
        // Money, animation), puis "State 3" (Money a 0, enveloppe cachee) ; le bois passe aussi par "Pay for car" -> "State 3".
        // Le boulot est rejoue chez tous : chacun a SA copie de l'enveloppe, pleine. Le monde (WorldFsms) ne l'aurait
        // suivie qu'a son releve suivant (etale : jusqu'a une minute apres son apparition) ; prise par l'un avant, elle
        // restait pleine chez l'autre, qui la prenait aussi, et chaque prise etant un revenu partage, chacun touchait la
        // paie DEUX fois. Reservees ici des le chargement (inactives comprises) : prise par un joueur (entree en "State 3"),
        // elle est retiree chez les autres ("State 3" : ni argent ni son), meme si leur copie n'apparait qu'apres (60 s).
        // La paie reste un revenu ordinaire du preneur (Update : jamais pris pour celle d'un boulot rejoue), recu une
        // fois par chacun. Pas l'enveloppe de Jokke (gain d'objet, voir Pays).
        class Env { public string Key; public PlayMakerFSM F; public bool Hooked; public float RetireUntil = -1; }
        static readonly List<Env> envs = new List<Env>();
        static readonly List<KeyValuePair<float, float>> recentIn = new List<KeyValuePair<float, float>>();   // revenus ordinaires recus (heure, montant)
        static float nextEnvHook, envIncomeUntil;
        static bool envApplying;

        class EnvHook : ModHook
        {
            public override string Module { get { return "argent"; } }
            public Env E; public bool Take;
            public override void OnEnter()
            {
                try
                {
                    if (Session.Active && !envApplying && Replay.Depth == 0)
                    {
                        if (Take) envIncomeUntil = Time.realtimeSinceStartup + 3f;   // la paie qui suit est a nous : partagee
                        else EnvTaken(E);
                    }
                }
                catch (System.Exception e) { Replay.HookError(e); }
                Finish();
            }
        }

        static bool IsEnvelope(PlayMakerFSM f)
        {
            if (f.FsmName != "Use" || f.hideFlags != HideFlags.None) return false;
            string n = f.gameObject.name;
            return n == "PayMoney";   // (PriceMoney* : prix des courses, personnels -- voir Pays)
        }

        static string ClaimEnvelopes()
        {
            envs.Clear();
            var keys = new HashSet<string>();
            foreach (Object o in Game.AllFsms())
            {
                var f = (PlayMakerFSM)o; if (f == null) continue;
                if (!IsEnvelope(f) || !f.transform.root.gameObject.activeInHierarchy) continue;   // (modeles : racine inactive)
                string key = Recon.Path(f.transform);
                if (key == JokkePay || !keys.Add(key)) continue;
                if (!Replay.Claim(f, "argent")) { Log.Warn("argent : enveloppe " + key + " deja a " + Replay.Owner(f)); continue; }
                envs.Add(new Env { Key = key, F = f });
            }
            return envs.Count + " enveloppes de paie";
        }

        // Accrochees a leur premiere apparition (automate demarre : etats charges) ; retrait en attente applique.
        static void HookEnvelopes()
        {
            float now = Time.realtimeSinceStartup;
            if (now < nextEnvHook) return;
            nextEnvHook = now + 0.25f;
            foreach (Env e in envs)
            {
                if (e.F == null || !e.F.gameObject.activeInHierarchy) continue;
                if (!e.Hooked)
                {
                    try
                    {
                        FsmState take = e.F.Fsm.GetState("State 1"), end = e.F.Fsm.GetState("State 3");
                        if (take == null || end == null) { e.Hooked = true; Log.Warn("argent : enveloppe " + e.Key + " sans State 1/State 3"); continue; }
                        if (!take.IsInitialized || !end.IsInitialized) continue;
                        var l1 = new List<FsmStateAction>(take.Actions); l1.Insert(0, new EnvHook { E = e, Take = true }); take.Actions = l1.ToArray();
                        var l3 = new List<FsmStateAction>(end.Actions); l3.Insert(0, new EnvHook { E = e }); end.Actions = l3.ToArray();
                        e.Hooked = true;
                        Log.Info("argent : enveloppe " + e.Key + " tendue ici");
                    }
                    catch { continue; }   // automate pas encore pret
                }
                if (e.RetireUntil > 0)
                {
                    if (now > e.RetireUntil) e.RetireUntil = -1;
                    else Retire(e);
                }
            }
        }

        // Prise ici (ou achat de la voiture du client du bois) : retiree chez les autres.
        static void EnvTaken(Env e)
        {
            Log.Info("argent : enveloppe " + e.Key + " prise ici");
            Session.SendAll(new NetWriter(Msg.Payout).U8(Session.LocalId).Str(e.Key), true);
        }

        static void Retire(Env e)
        {
            e.RetireUntil = -1;
            string st = e.F.ActiveStateName;
            if (st == "State 3" || st == "State 5") return;
            envApplying = true; Replay.Depth++;
            try { Game.SetState(e.F, "State 3"); }
            finally { envApplying = false; Replay.Depth--; }
            Log.Info("argent : enveloppe " + e.Key + " retiree ici (" + st + " -> " + e.F.ActiveStateName + ")");
        }

        public static void OnPayout(Peer from, NetReader r)
        {
            int who = r.U8();
            if (Session.IsHost) who = from.Id;
            string key = r.Str();
            if (Session.IsHost) Session.Broadcast(new NetWriter(Msg.Payout).U8(who).Str(key), true, who);
            Env e = envs.Find(x => x.Key == key);
            if (e == null || e.F == null) { Log.Warn("argent : enveloppe " + key + " prise par #" + who + ", introuvable ici"); return; }
            PlayerInfo pi;
            Log.Info("argent : enveloppe " + key + " prise par " + (Session.Players.TryGetValue(who, out pi) ? pi.Name : "#" + who));
            if (e.F.gameObject.activeInHierarchy && e.Hooked) Retire(e);
            else e.RetireUntil = Time.realtimeSinceStartup + 60f;   // pas encore tendue ici : retiree a son apparition
        }

        // Essais (Autotest=paie) : enveloppe sortie de sous son PNJ, suivie comme les autres sous son nouveau chemin.
        public static string TestEnvelope(PlayMakerFSM f)
        {
            if (f == null) return "pas d'automate";
            Env e = envs.Find(x => x.F == f);
            if (e == null) { e = new Env { F = f }; envs.Add(e); }
            e.Key = Recon.Path(f.transform);
            return "enveloppe d'essai " + e.Key + " suivie (" + (Replay.Owner(f) ?? "libre") + ")";
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
            if (mode == "ferraille" && ready) { TestScrap(t); return; }
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

        // Essais 'ferraille' : les deux a la benne de Fleetari (TestPos=1556,6,718). Objet jete : [Test] TestObjet (cle Props,
        // ou '~partie de cle') ou, par defaut, l'objet suivi le plus proche de la benne (meme regle des deux cotes : meme
        // objet). 26-32 s : l'invite le promene (Props.TestCarry : ses poses partent, il passe pour tenu ici) ; 34 s : chez
        // les deux, la benne recoit l'objet (Object, Weight 10, 'State 3' : comme apres 'Turn into garbage') ; 40 s :
        // l'enveloppe ScrapMoney sort chez l'invite ; 42 s : il la touche ('State 1'). Notes a 30 s et 50 s.
        // Attendu : invite « ferraille (State 3) : ... mis par ce joueur-ci, paye ici », hote « mis par un autre joueur,
        // rien verse ici » (enveloppe de l'hote inchangee) ; invite « rachat de ferraille +X, garde pour soi », aucun
        // « argent : +... de » chez l'hote.
        static string testKey;
        static GameObject testObj;

        static void TestScrap(float t)
        {
            Trig g = Trigs[0];
            if (g.F == null) { if (testStep == 0 && t > 26f) { testStep = 9; Log.Info("autotest : ferraille, benne pas suivie ici (REPAIRSHOP charge ? TestPos)"); } return; }
            if (testStep == 0 && t > 26f)
            {
                testStep = 1;
                string k = Config.Get("Test", "TestObjet", "");
                testKey = k.StartsWith("~") ? Props.FindKey(k.Substring(1)) : k.Length > 0 ? k : Props.NearestId("", g.F.transform.position);
                testObj = testKey != null ? Props.ObjectOf(testKey) : null;
                Log.Info("autotest : ferraille, objet " + (testKey ?? "?") + (testObj != null ? " en " + testObj.transform.position.ToString("F1") : " absent") + " ; " + State() + " ; " + PotState(g.F));
            }
            if (!Session.IsHost && testStep == 1 && t < 32f && testObj != null) { Props.TestCarry(testKey, t); Home.MarkHeld(testObj); }
            if (testStep == 1 && t > 30f) { testStep = 2; Log.Info("autotest : ferraille, " + State()); }
            if (testStep == 2 && t > 34f)
            {
                testStep = 3;
                if (testObj == null) { Log.Info("autotest : ferraille, pas d'objet"); return; }
                g.F.FsmVariables.FindFsmGameObject("Object").Value = testObj;
                FsmFloat wv = g.F.FsmVariables.FindFsmFloat("Weight");
                if (wv != null) wv.Value = 10f;
                string before = PotState(g.F);
                Game.SetState(g.F, "State 3");
                Log.Info("autotest : ferraille, la benne recoit " + testObj.name + " : " + before + " -> " + PotState(g.F) + " (etat " + g.F.ActiveStateName + ")");
            }
            GameObject env = Game.FindAny("REPAIRSHOP/LOD/Office/Fleetari/ScrapMoney");
            PlayMakerFSM use = env != null ? Game.FsmOn(env, "Use") : null;
            if (!Session.IsHost && testStep == 3 && t > 40f)
            {
                testStep = 4;
                if (env != null && !env.activeSelf) env.SetActive(true);
                Log.Info("autotest : ferraille, enveloppe sortie chez l'invite : " + (env != null ? "active " + env.activeInHierarchy : "absente") + ", " + (use != null ? "Money " + use.FsmVariables.FindFsmFloat("Money").Value : "?"));
            }
            if (!Session.IsHost && testStep == 4 && t > 42f)
            {
                testStep = 5;
                if (use != null && env.activeInHierarchy) Game.SetState(use, "State 1");
                Log.Info("autotest : ferraille, l'invite touche l'enveloppe : " + State());
            }
            if (testStep >= 3 && testStep < 9 && t > 50f) { testStep = 9; Log.Info("autotest : ferraille, fin : " + State() + " ; " + PotState(g.F) + " ; payes ici " + trigLocal + ", pas payes ici " + trigOther); }
        }

        // Essais : ou 'State 3' de la benne ajoute (AddFsmFloat : automate, variable, valeur actuelle).
        static string PotState(PlayMakerFSM f)
        {
            var sb = new System.Text.StringBuilder("pot");
            try
            {
                FsmState st = f.Fsm.GetState("State 3");
                foreach (FsmStateAction a in st.Actions)
                {
                    if (a == null || a.GetType().Name != "AddFsmFloat") continue;
                    var od = a.GetType().GetField("gameObject").GetValue(a) as FsmOwnerDefault;
                    var fsm = a.GetType().GetField("fsmName").GetValue(a) as FsmString;
                    var name = a.GetType().GetField("variableName").GetValue(a) as FsmString;
                    GameObject go = od == null ? null : od.OwnerOption == OwnerDefaultOption.UseOwner ? f.gameObject : od.GameObject.Value;
                    PlayMakerFSM target = go != null && fsm != null ? Game.FsmOn(go, fsm.Value) : null;
                    FsmFloat v = target != null && name != null ? target.FsmVariables.FindFsmFloat(name.Value) : null;
                    sb.Append(' ').Append(go != null ? go.name : "?").Append('.').Append(name != null ? name.Value : "?").Append('=').Append(v != null ? v.Value.ToString("0.##") : "?");
                }
            }
            catch (System.Exception e) { sb.Append(" ?").Append(e.GetType().Name); }
            return sb.ToString();
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
