using System.Collections.Generic;
using HutongGames.PlayMaker;
using MWCoop.Net;
using UnityEngine;

namespace MWCoop
{
    // Argent (demande de JD) : chaque joueur a son porte-monnaie (achats chacun de son cote) et, depuis 0.75.42 (JD,
    // 11/10, apres le retour de gamereggo), chacun garde sa paie : ce qui RENTRE chez un joueur (paie, ventes,
    // recompenses) reste a lui. Avant, tout revenu etait recu par tous. Globales PlayerMoney (liquide)
    // et PlayerBankAccount (banque), comparees deux fois par seconde :
    //  - une hausse de la richesse totale est un revenu : garde par ce joueur (plus envoye) ; celle qu'un boulot d'un
    //    autre, rejoue ici, verse a ce joueur n'est pas a lui : retiree ;
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
        // Boulots rejoues (Jobs) : la paie qu'ils versent ici est celle d'un autre joueur, retiree.
        static float suppressUntil;

        public static void Suppress(float seconds) { suppressUntil = Mathf.Max(suppressUntil, Time.realtimeSinceStartup + seconds); }

        static FsmFloat cash, bank;
        static float lastCash, lastBank, next, readyAt = -1, nextStore;
        static string world;

        // Versements du monde, reperes par leur etat de paiement (crochet au debut et a la fin de l'etat :
        // ecart exact de liquide et de banque, hors du releve ordinaire) :
        //  - partages (allocation, aide au logement) : chaque joueur envoie
        //    le montant que sa logique a verse (avec son numero de source) ; chacun garde, UNE fois, le plus
        //    haut montant vu pour ce versement pendant PayWindow : copie locale en trop retiree (celle de
        //    l'autre deja la, ou etat de paie rejoue une 2e fois par Jobs), ecart complete ;
        //  - personnels (interets, paies des boulots depuis 0.75.42) : calcules chez chacun, gardes pour soi, jamais envoyes ;
        //    une paie de boulot versee par un etat rejoue (boulot d'un autre) est retiree.
        // L'ordre de la liste est le numero de source envoye : le meme chez tous (meme version).
        class Pay
        {
            public readonly string Path, Fsm, State, Label;
            public readonly bool Shared, Own;                           // Own : enveloppe reservee a ce module (Claim)
            public PlayMakerFSM F;
            public bool Hooked, Open, Replayed;                         // Replayed : etat entre pendant un rejeu
            public float C0, B0;                                        // releve au debut de l'etat
            public float At = -100, HaveC, HaveB, BestC, BestB, Sent;   // versement en cours : deja en poche, plus haut vu, envoye
            public int Day = -1;                                        // jour de jeu (GlobalDaysPassed) du versement en cours
            public Pay(string path, string fsm, string state, string label, bool shared, bool own = false) { Path = path; Fsm = fsm; State = state; Label = label; Shared = shared; Own = own; }
        }
        static readonly Pay[] Pays =
        {
            new Pay("Systems/Expenses", "Kela", "Pay", "allocation chomage", true),                       // vendredi
            new Pay("Systems/Expenses", "Livingsupport", "Asumistuki", "aide au logement", true),          // vendredi
            // Paies des boulots (0.75.42, chacun garde sa paie) : celle que la logique de CE joueur verse, d'apres SON
            // travail ; plus completee par celle des autres (avant : chacun recevait la plus haute).
            new Pay("JOBS/FACTORY", "PlayerData", "Base salary", "paie de l'usine", false),                // chaque semaine
            new Pay("JOBS/FACTORY", "PlayerData", "High salary", "paie de l'usine (heures sup)", false),
            new Pay("JOBS/ADs", "Data", "Bank transfer 2", "paie des publicites", false),                  // mardi et vendredi
            new Pay("JOBS/TAXIJOB/MACHTWAGEN/TaxiFunctions", "Payments", "Payment", "paie du taxi", false), // mercredi
            new Pay("Systems/BankAccount", "Data", "Interest", "interets", false),                         // chaque jour
            // Gains d'un objet mis par ce joueur (voir Trigs) : touches par lui seul, gardes pour soi.
            new Pay("REPAIRSHOP/LOD/Office/Fleetari/ScrapMoney", "Use", "State 1", "rachat de ferraille", false, true),
            new Pay("FleaMarket/LOD/OpenHours/MoneyFlea", "Use", "State 1", "ventes aux puces", false, true),
            new Pay(JokkePay, "Use", "State 1", "kilju vendu a Jokke", false, true),
            new Pay(LottoPath, "Logic", "Bank", "gains du loto", false),
            // Prix des courses (podium de CE joueur, calcule chez lui) : a lui, gardes pour soi.
            new Pay("RACES/RALLY/SS3/FinishArea/Stuff/PriceMoneyRally", "Use", "State 1", "prix du rallye", false, true),
            new Pay("RACES/ICERACE/TentContents/Prices/PriceMoneyRace", "Use", "State 1", "prix de la course sur glace", false, true),
            // My Summer Car, enveloppe du rallye du dimanche (audit MSC 02/11) : 'Idle 2' de ResultsWeekend l'allume chez chaque
            // engage (Money d'apres SA place) ; personne ne la reservait, le monde rejouait le clic d'un autre ('State 1' :
            // ActivateGameObject(soi, faux)) et eteignait la sienne sans le payer. Reservee a ce module (ClaimEarly : le monde ne la
            // rejoue plus) ; sur MSC son argent passe par le releve ordinaire, garde pour soi. (En fin de liste : numeros de source
            // inchanges. Sur MWC le chemin est absent : sans effet.)

            new Pay("RALLY/Sunday/FinishArea/Stuff/PayMoney", "Use", "State 1", "prix du rallye", false, true),
        };

        const string JokkePay = "JOBS/JOKKEHOME/HouseDrunkNew/KiljuBuyer/Char/skeleton/pelvis/spine_middle/spine_upper/collar_left/shoulder_left/arm_left/hand_left/PayMoney";
        const string ScrapPath = "REPAIRSHOP/Scrapmetal/GarbageTrigger", LottoPath = "PERAPORTTI/Building/LOD100/Store/VoittousArea/TrashTrigger";
        // Un versement partage est repere par son JOUR de jeu (GlobalDaysPassed, le meme chez tous) ; ce delai reel ne sert plus
        // que de secours (jour pas encore recale chez un invite). < 36 s : mardi -> vendredi en sommeil accelere (12 s par jour).
        const float PayWindow = 30f;
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
            if (amount == 0 || amount > 1000000 || amount < -1000000) return;
            // Hote : le receveur est parti entre l'envoi et l'arrivee (le donneur l'avait encore dans sa liste) -- avant, la somme
            // etait debitee sans retour (relecture du 15/10). Rendue au donneur par le meme message @don, montant NEGATIF
            // (= remboursement ; aucun autre format a toucher dans Jobs). Un receveur present mais jamais pret (parti en
            // chargement) garde son attente chez lui : impossible a savoir d'ici, seulement note dans son journal.
            if (amount > 0 && Session.IsHost && to != Session.LocalId && !Session.Players.ContainsKey(to))
            {
                PlayerInfo giver;
                if (Session.Players.TryGetValue(who, out giver) && giver.Peer != null && Session.T != null)
                {
                    Session.T.SendReliable(giver.Peer, new NetWriter(Msg.Job).U8(Session.LocalId).Str("@don").U8((byte)who).I32(-amount).ToArray());
                    Log.Info("argent : don de " + amount + " mk de " + giver.Name + " a #" + to + " (parti) : rendu au donneur");
                }
                else Log.Warn("argent : don de " + amount + " mk de #" + who + " a #" + to + " (parti), donneur introuvable : perdu");
                return;
            }
            if (to != Session.LocalId) return;
            if (amount < 0)
            {
                // Remboursement (de l'hote seul) : la somme revient telle quelle, hors releve (comme un don recu).
                if (Session.IsHost) return;
                amount = -amount;
                if (!Ready) { pendingRefund += amount; Log.Info("argent : " + amount + " mk rendus (receveur parti ; porte-monnaie pas pret : en attente)"); return; }
                cash.Value += amount; lastCash += amount;
                Log.Info("argent : " + amount + " mk rendus (le receveur du don etait parti)");
                Hud.Toast(Lang.T("Don rendu (joueur parti) : ", "Gift returned (player left): ") + amount + " mk");
                Store();
                return;
            }
            PlayerInfo pi;
            string name = Session.Players.TryGetValue(who, out pi) ? pi.Name : "#" + who;
            if (!Ready) { pendingGift += amount; pendingFrom = name; Log.Info("argent : " + amount + " mk recus de " + name + " (porte-monnaie pas pret : en attente)"); return; }
            cash.Value += amount; lastCash += amount;
            Log.Info("argent : " + amount + " mk recus de " + name);
            Hud.Toast(name + Lang.T(" vous a donn\u00E9 ", " gave you ") + amount + " mk");
            Store();
        }
        static int pendingGift, pendingRefund; static string pendingFrom;

        // Triches (/argent) : liquide pour soi seul -- ni revenu partage ni depense (lastCash suit, comme un don recu).
        public static bool AddLocal(int amount)
        {
            if (!Ready || amount <= 0) return false;
            cash.Value += amount; lastCash += amount;
            Store();
            Log.Info("argent : +" + amount + " mk (triche)");
            return true;
        }

        // Achat hors des caisses du jeu (plein de la voiture de la quete) : une baisse, donc une depense ordinaire, locale.
        public static bool Spend(int amount)
        {
            if (!Ready || amount <= 0 || cash.Value < amount) return false;
            cash.Value -= amount;
            return true;
        }

        // Triche de l'hote (/argentde) : liquide fixe a 'amount' -- ni revenu partage ni depense (lastCash suit).
        public static bool SetLocal(int amount)
        {
            if (!Ready || amount < 0) return false;
            cash.Value = amount; lastCash = amount;
            Store();
            Log.Info("argent : fixe a " + amount + " mk (triche de l'hote)");
            return true;
        }

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

        // Kela, meme ligne (relecture du 15/10, ecart n13) : la lettre et l'allocation sont a chacun (Systems/Expenses tourne chez
        // chaque joueur : WorldFsms.SkipObjects, Calls), mais l'etat de l'automate Kela de l'invite venait de la sauvegarde de l'hote
        // a chaque session (tags Kela2Days, Kela2Weeks, Kela2WaitingTime, Kela2Continue, Kela2Amount... : ses variables locales,
        // sauvees par ses UT*) -- lettre a reposter, carence reprise a celle de l'hote. Gardes : ses variables (entiers, reels,
        // booleens, textes courts ; hors UT*) sous kela.<nom>, son etat sous kela.@etat, et les globales Kela* sous kelaG.<nom>.
        // Remis a la lecture ; jamais remis dans l'etat de paie (l'allocation retomberait a chaque session). Au menu l'automate
        // n'existe plus : la derniere ligne relevee en partie est reecrite telle quelle (Store a la sortie du niveau).
        static PlayMakerFSM kela;
        static string kelaLine = "";
        static PlayMakerFSM Kela()
        {
            if (Game.Msc) return null;   // (My Summer Car : pas de Kela)
            if (kela == null && PlayerSync.InGame) { GameObject go = Game.FindAny(Pays[0].Path); kela = go != null ? Game.FsmOn(go, Pays[0].Fsm) : null; }   // (pas au menu : racine absente)
            return kela;
        }
        static bool Tag(string n) { return n.StartsWith("UT") || n.StartsWith("UniqueTag"); }
        static bool Plain(string s) { return s != null && s.Length > 0 && s.Length <= 40 && s.IndexOf(';') < 0 && s.IndexOf(':') < 0 && s.IndexOf('=') < 0; }

        // Usine, meme ligne (16/10) : le pointage et les compteurs de travail de JOBS/FACTORY::PlayerData sont a chacun
        // (Jobs.FactoryOwn : ni envoyes ni rejoues), mais chez l'invite ils venaient de la sauvegarde de l'hote a chaque session
        // (UTMinutes*, UTDaysPassed, UTPaychecks... : les SIENS). Gardes sous usine.<nom>, l'etat sous usine.@etat ; remis a la
        // lecture (apres le « Load game » de l'automate, Restore passe 15 s apres le chargement). L'etat n'est remis que s'il
        // etait « State 2 » (pointe, l'automate guette l'heure de fermeture) et que l'automate attend en « Work ON » : les autres
        // etats (embauche, lettres, paie) suivent la partie. Le total de paquets vit aussi dans LOD1/JOB::JobProgress
        // (PackagesTotal, recopie dans PlayerData a la sauvegarde) : remis la aussi quand l'automate est la.
        static PlayMakerFSM usine;
        static string usineLine = "";
        const string UsineJob = Jobs.FactoryPath + "/OpeningTimes/LOD1/JOB";
        static PlayMakerFSM Usine()
        {
            if (usine == null && PlayerSync.InGame) { GameObject go = Game.FindAny(Jobs.FactoryPath); usine = go != null ? Game.FsmOn(go, Jobs.FactoryFsm) : null; }
            return usine;
        }

        static string UsineLine()
        {
            PlayMakerFSM u = Usine();
            if (u == null) return usineLine;
            var sb = new System.Text.StringBuilder();
            try
            {
                FsmVariables v = u.FsmVariables;
                foreach (string n in Jobs.FactoryOwn)
                {
                    FsmInt vi = v.FindFsmInt(n); FsmFloat vf = v.FindFsmFloat(n); FsmBool vb = v.FindFsmBool(n);
                    if (vi == null && vf == null && vb == null) continue;
                    sb.Append(sb.Length > 0 ? ";" : "").Append("usine.").Append(n).Append(':').Append(vi != null ? vi.Value.ToString(Inv) : vf != null ? vf.Value.ToString("R", Inv) : vb.Value ? "1" : "0");
                }
                // Paquets : la valeur vivante est celle de JobProgress (PlayerData ne la recopie qu'a la sauvegarde).
                GameObject jg = Game.FindAny(UsineJob);
                PlayMakerFSM jp = jg != null ? Game.FsmOn(jg, "JobProgress") : null;
                FsmInt pt = jp != null ? jp.FsmVariables.FindFsmInt("PackagesTotal") : null;
                if (pt != null) sb.Append(sb.Length > 0 ? ";" : "").Append("usine.@paquets:").Append(pt.Value.ToString(Inv));
                string st = u.ActiveStateName;
                if (Plain(st)) sb.Append(sb.Length > 0 ? ";" : "").Append("usine.@etat:").Append(st);
            }
            catch { return usineLine; }   // automate pas encore pret : la derniere ligne
            usineLine = sb.ToString();
            return usineLine;
        }

        // Une valeur usine.<nom> remise dans l'automate ; faux : inconnue ici.
        static bool RestoreUsine(string name, string val)
        {
            string vn = name.Substring(6);
            PlayMakerFSM u = Usine();
            if (u == null || vn.Length == 0 || System.Array.IndexOf(Jobs.FactoryOwn, vn) < 0) return false;
            FsmVariables v = u.FsmVariables;
            float f; int i;
            FsmInt vi = v.FindFsmInt(vn);
            if (vi != null) { if (int.TryParse(val, System.Globalization.NumberStyles.Integer, Inv, out i)) { vi.Value = i; return true; } return false; }
            FsmFloat vf = v.FindFsmFloat(vn);
            if (vf != null) { if (float.TryParse(val, System.Globalization.NumberStyles.Float, Inv, out f)) { vf.Value = f; return true; } return false; }
            FsmBool vb = v.FindFsmBool(vn);
            if (vb != null) { vb.Value = val == "1"; return true; }
            return false;
        }

        // Total de paquets remis dans LOD1/JOB::JobProgress (et dans PlayerData, d'ou le jeu le relit au chargement).
        static bool RestorePackages(string val)
        {
            int n;
            if (!int.TryParse(val, System.Globalization.NumberStyles.Integer, Inv, out n)) return false;
            GameObject jg = Game.FindAny(UsineJob);
            PlayMakerFSM jp = jg != null ? Game.FsmOn(jg, "JobProgress") : null;
            FsmInt pt = null;
            try { pt = jp != null ? jp.FsmVariables.FindFsmInt("PackagesTotal") : null; } catch { }
            if (pt == null) return false;
            pt.Value = n;
            PlayMakerFSM u = Usine();
            FsmInt pp = u != null ? u.FsmVariables.FindFsmInt("ProgressPackagesTotal") : null;
            if (pp != null) pp.Value = n;
            return true;
        }

        static string KelaLine()
        {
            PlayMakerFSM k = Kela();
            if (k == null) return kelaLine;
            var sb = new System.Text.StringBuilder();
            try
            {
                FsmVariables kv = k.FsmVariables;
                foreach (FsmInt x in kv.IntVariables) if (!Tag(x.Name) && Plain(x.Name)) sb.Append(sb.Length > 0 ? ";" : "").Append("kela.").Append(x.Name).Append(':').Append(x.Value.ToString(Inv));
                foreach (FsmFloat x in kv.FloatVariables) if (!Tag(x.Name) && Plain(x.Name)) sb.Append(sb.Length > 0 ? ";" : "").Append("kela.").Append(x.Name).Append(':').Append(x.Value.ToString("R", Inv));
                foreach (FsmBool x in kv.BoolVariables) if (!Tag(x.Name) && Plain(x.Name)) sb.Append(sb.Length > 0 ? ";" : "").Append("kela.").Append(x.Name).Append(':').Append(x.Value ? '1' : '0');
                foreach (FsmString x in kv.StringVariables) if (!Tag(x.Name) && Plain(x.Name) && Plain(x.Value)) sb.Append(sb.Length > 0 ? ";" : "").Append("kela.").Append(x.Name).Append(':').Append(x.Value);
                string st = k.ActiveStateName;
                if (Plain(st)) sb.Append(sb.Length > 0 ? ";" : "").Append("kela.@etat:").Append(st);
                FsmVariables g = FsmVariables.GlobalVariables;
                foreach (FsmInt x in g.IntVariables) if (x.Name.StartsWith("Kela")) sb.Append(sb.Length > 0 ? ";" : "").Append("kelaG.").Append(x.Name).Append(':').Append(x.Value.ToString(Inv));
                foreach (FsmFloat x in g.FloatVariables) if (x.Name.StartsWith("Kela")) sb.Append(sb.Length > 0 ? ";" : "").Append("kelaG.").Append(x.Name).Append(':').Append(x.Value.ToString("R", Inv));
                foreach (FsmBool x in g.BoolVariables) if (x.Name.StartsWith("Kela")) sb.Append(sb.Length > 0 ? ";" : "").Append("kelaG.").Append(x.Name).Append(':').Append(x.Value ? '1' : '0');
            }
            catch { return kelaLine; }   // automate pas encore pret : la derniere ligne
            kelaLine = sb.ToString();
            return kelaLine;
        }

        // Une valeur kela.<nom> / kelaG.<nom> remise dans l'automate (ou les globales) ; faux : inconnue ici.
        static bool RestoreKela(string name, string val)
        {
            bool glob = name.StartsWith("kelaG.");
            string vn = name.Substring(glob ? 6 : 5);
            PlayMakerFSM k = glob ? null : Kela();
            FsmVariables kv = glob ? FsmVariables.GlobalVariables : (k != null ? k.FsmVariables : null);
            if (kv == null || vn.Length == 0) return false;
            float f; int i;
            FsmInt vi = kv.FindFsmInt(vn);
            if (vi != null) { if (int.TryParse(val, System.Globalization.NumberStyles.Integer, Inv, out i)) { vi.Value = i; return true; } return false; }
            FsmFloat vf = kv.FindFsmFloat(vn);
            if (vf != null) { if (float.TryParse(val, System.Globalization.NumberStyles.Float, Inv, out f)) { vf.Value = f; return true; } return false; }
            FsmBool vb = kv.FindFsmBool(vn);
            if (vb != null) { vb.Value = val == "1"; return true; }
            FsmString vs = glob ? null : kv.FindFsmString(vn);
            if (vs != null && !Tag(vn)) { vs.Value = val; return true; }
            return false;
        }

        // My Summer Car, meme ligne (audit MSC 02/11) :
        //  - teimo.<nom> : la dette de l'invite a la caisse de Teimo (STORE/StoreCashRegister/Register::Data : FuelPurchase,
        //    DieselPurchase, FuelOilPurchase, BrokenWindow). Jamais dans la sauvegarde de l'hote, et celle de l'hote est effacee chez
        //    l'invite (Shop.ClearInheritedDebt) : l'essence prise sans payer s'effacait a chaque reconnexion, alors qu'en solo elle
        //    reste due. Relevee seulement apres cet effacement (avant : celle de l'hote) ; remise juste apres (Shop) ou a la lecture
        //    si l'effacement est deja passe. (Pas les litres en cours des pompes.)
        //  - dancing.ReputationAlltime : la reputation au dancing (DANCEHALL/Functions/GUARD/Guard::React, chargee de la sauvegarde
        //    par 'Load', DancehallPlayerReputation) -- a 25 le videur bannit. L'invite prenait celle de l'hote (banni des l'entree sans
        //    rien avoir fait) et perdait la sienne. Remise apres chaque 'Load' de l'automate (premier allumage compris, voir DanceTick).
        // [Test] EtatJoueurMsc=0 : comme avant.
        const string GuardPath = "DANCEHALL/Functions/GUARD/Guard";
        static readonly string[] TeimoDebt = { "FuelPurchase", "DieselPurchase", "FuelOilPurchase", "BrokenWindow" };
        static string teimoLine = "", danceLine = "";
        static Dictionary<string, float> ownDebt;   // dette de l'invite lue pour ce monde (remise apres l'effacement de celle de l'hote)
        static int danceRep = -1;                    // reputation de l'invite pour ce monde (-1 : pas encore lue)
        static PlayMakerFSM guard;                   // Guard::React de ce niveau (null : pas encore vu)
        static bool danceHooked, danceSeen;
        static bool OwnMsc { get { return Game.Msc && Config.GetInt("Test", "EtatJoueurMsc", 1) != 0; } }

        static string TeimoLine()
        {
            PlayMakerFSM r = Shop.DebtClearedOn;   // (null : dette de l'hote pas encore effacee ici, ou autre niveau)
            if (!OwnMsc || r == null) return teimoLine;
            var sb = new System.Text.StringBuilder();
            foreach (string n in TeimoDebt)
            {
                FsmFloat v = r.FsmVariables.FindFsmFloat(n);
                if (v != null) sb.Append(sb.Length > 0 ? ";" : "").Append("teimo.").Append(n).Append(':').Append(v.Value.ToString("R", Inv));
            }
            teimoLine = sb.ToString();
            return teimoLine;
        }

        // Shop, apres l'effacement de la dette de l'hote : celle de l'invite remise ; texte pour le journal.
        public static string ApplyOwnDebt(PlayMakerFSM reg)
        {
            if (!OwnMsc || reg == null || ownDebt == null || ownDebt.Count == 0) return "";
            var sb = new System.Text.StringBuilder(" ; la sienne remise :");
            foreach (KeyValuePair<string, float> kv in ownDebt)
            {
                FsmFloat v = reg.FsmVariables.FindFsmFloat(kv.Key);
                if (v == null) continue;
                v.Value = kv.Value;
                if (kv.Value != 0f) sb.Append(' ').Append(kv.Key).Append('=').Append(kv.Value.ToString("F2"));
            }
            return sb.ToString();
        }

        static PlayMakerFSM Guard()
        {
            if (guard == null && PlayerSync.InGame) { GameObject g = Game.FindAny(GuardPath); guard = g != null ? Game.FsmOn(g, "React") : null; }
            return guard;
        }

        static string DanceLine()
        {
            // (relevee seulement une fois la sienne remise dans CET automate : avant, c'est celle de la sauvegarde de l'hote)
            if (!OwnMsc || danceRep < 0 || guard == null || !danceSeen) return danceLine;
            FsmInt v = guard.FsmVariables.FindFsmInt("ReputationAlltime");
            if (v == null) return danceLine;
            danceRep = v.Value;
            danceLine = "dancing.ReputationAlltime:" + v.Value.ToString(Inv);
            return danceLine;
        }

        // Reputation de l'invite posee dans Guard::React ; texte pour le journal.
        static string PutDance(string why)
        {
            PlayMakerFSM g = Guard();
            FsmInt v = g != null ? g.FsmVariables.FindFsmInt("ReputationAlltime") : null;
            if (v == null || danceRep < 0) return "";
            int was = v.Value;
            v.Value = danceRep;
            danceSeen = true;
            if (was != danceRep) Log.Info("joueur : reputation au dancing " + was + " (sauvegarde de l'hote) -> " + danceRep + " (la sienne, " + why + ")");
            return " ; dancing : reputation " + danceRep;
        }

        // Apres 'Load' (LoadInt DancehallPlayerReputation : la valeur de la sauvegarde de l'hote) : la sienne.
        class DanceLoad : FsmStateAction
        {
            public override void OnEnter()
            {
                try { if (Session.Active && !Session.IsHost && OwnMsc) PutDance("apres le chargement du videur"); } catch (System.Exception e) { Replay.HookError(e); }
                Finish();
            }
        }

        // Chaque image tant que le videur n'est pas accroche (cout : un booleen) : DANCEHALL s'allume en cours de partie, et son
        // automate enchaine dans la meme image State 1 -> Load -> Reputation (-> Ban -> Catch : WALK au videur 1 s plus tard).
        // Accroche entre l'initialisation et le demarrage : 'Load' remet la sienne avant la comparaison. Demarre deja : la sienne
        // posee, et un bannissement venu de la valeur de l'hote annule (WALK en attente retire) si la sienne est sous 25.
        static void DanceTick()
        {
            if (danceHooked || danceRep < 0 || !OwnMsc || Session.IsHost || !Session.Active) return;
            PlayMakerFSM g = Guard();
            if (g == null || g.Fsm == null || !g.Fsm.Initialized) return;
            FsmState ld = g.Fsm.GetState("Load");
            if (ld == null) { danceHooked = true; Log.Warn("joueur : videur du dancing sans etat 'Load' : reputation de l'invite remise une fois"); PutDance("videur sans 'Load'"); return; }
            if (!ld.IsInitialized) return;
            danceHooked = true;
            try { FsmHooks.Append(ld, new DanceLoad()); } catch (System.Exception e) { Log.Warn("joueur : videur du dancing pas suivi (" + e.Message + ")"); }
            string st = g.ActiveStateName;
            if (string.IsNullOrEmpty(st)) { Log.Info("joueur : videur du dancing suivi avant son demarrage (reputation de l'invite remise a son chargement)"); return; }
            FsmInt v = g.FsmVariables.FindFsmInt("ReputationAlltime");
            int was = v != null ? v.Value : -1;
            PutDance("videur deja demarre en " + st);
            PlayMakerFSM sp = Game.FsmOn(g.gameObject, "Speak");
            FsmInt inf = sp != null ? sp.FsmVariables.FindFsmInt("InfractionType") : null;
            if (was >= 25 && danceRep < 25 && inf != null && inf.Value == 5 && (st == "Catch" || st == "Idle" || st == "Ban"))
            {
                g.Fsm.KillDelayedEvents();
                inf.Value = 0;
                Log.Info("joueur : bannissement du dancing venu de la reputation de l'hote (" + was + ") annule ici (la sienne : " + danceRep + ")");
            }
        }

        // [Test] (audit MSC 02/11) : ce que la ligne de l'invite garde de Teimo et du dancing, les valeurs vivantes, et a qui est
        // l'enveloppe du rallye du dimanche (attendu « argent »).
        public static string TestOwnState()
        {
            PlayMakerFSM r = Shop.DebtClearedOn, g = Guard();
            FsmFloat fp = r != null ? r.FsmVariables.FindFsmFloat("FuelPurchase") : null;
            FsmInt rep = g != null ? g.FsmVariables.FindFsmInt("ReputationAlltime") : null;
            GameObject pm = Game.FindAny("RALLY/Sunday/FinishArea/Stuff/PayMoney");
            PlayMakerFSM pu = pm != null ? Game.FsmOn(pm, "Use") : null;
            return "teimo [" + TeimoLine() + "] FuelPurchase " + (fp != null ? fp.Value.ToString("F2") : r == null ? "(dette de l'hote pas encore effacee)" : "?")
                   + " ; dancing [" + DanceLine() + "] ReputationAlltime " + (rep != null ? rep.Value.ToString() : "?") + (danceHooked ? " (videur suivi)" : "")
                   + " ; enveloppe du rallye " + (pu == null ? "absente" : (pm.activeSelf ? "allumee" : "eteinte") + ", a " + (Replay.Owner(pu) ?? "personne"));
        }

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
            string kl = KelaLine();
            if (kl.Length > 0) sb.Append(sb.Length > 0 ? ";" : "").Append(kl);
            string ul = UsineLine();
            if (ul.Length > 0) sb.Append(sb.Length > 0 ? ";" : "").Append(ul);
            string tl = TeimoLine();
            if (tl.Length > 0) sb.Append(sb.Length > 0 ? ";" : "").Append(tl);
            string dl = DanceLine();
            if (dl.Length > 0) sb.Append(sb.Length > 0 ? ";" : "").Append(dl);
            return sb.ToString();
        }

        static void RestoreSelf()
        {
            kelaLine = usineLine = teimoLine = danceLine = "";   // (nouveau monde : rien de Kela ni de l'usine a reporter tant que l'automate n'a pas ete releve ici)
            ownDebt = null; danceRep = -1;
            if (Session.IsHost || world.Length == 0) return;
            // (MSC : premiere venue dans ce monde, ou ligne d'avant ce releve -- aucune dette, reputation 0 : celles de l'hote ne sont pas les siennes)
            if (OwnMsc) { ownDebt = new Dictionary<string, float>(); danceRep = 0; }
            if (!System.IO.File.Exists(SelfPath)) { OwnRestored(); return; }
            foreach (string l in System.IO.File.ReadAllLines(SelfPath))
            {
                int eq = l.IndexOf('=');
                if (eq <= 0 || l.Substring(0, eq) != world) continue;
                int n = 0, nk = 0, nu = 0;
                string kelaState = null, usineState = null;
                bool packages = false;
                var kept = new List<string>();
                var keptUsine = new List<string>();
                foreach (string kv in l.Substring(eq + 1).Split(';'))
                {
                    int c = kv.IndexOf(':');
                    if (c <= 0) continue;
                    string name = kv.Substring(0, c), val = kv.Substring(c + 1);
                    if (name.StartsWith("kela.") || name.StartsWith("kelaG."))
                    {
                        kept.Add(kv);
                        if (name == "kela.@etat") kelaState = val;
                        else if (RestoreKela(name, val)) nk++;
                        continue;
                    }
                    if (OwnMsc && (name.StartsWith("teimo.") || name.StartsWith("dancing.")))
                    {
                        float fo; int io;
                        if (name == "dancing.ReputationAlltime" && int.TryParse(val, System.Globalization.NumberStyles.Integer, Inv, out io)) danceRep = Mathf.Max(0, io);
                        else if (name.StartsWith("teimo.") && System.Array.IndexOf(TeimoDebt, name.Substring(6)) >= 0 && float.TryParse(val, System.Globalization.NumberStyles.Float, Inv, out fo)) ownDebt[name.Substring(6)] = fo;
                        continue;
                    }
                    if (name.StartsWith("usine."))
                    {

                        keptUsine.Add(kv);
                        if (name == "usine.@etat") usineState = val;
                        else if (name == "usine.@paquets") packages = RestorePackages(val);
                        else if (RestoreUsine(name, val)) nu++;
                        continue;
                    }
                    float f; int i;
                    FsmFloat vf = System.Array.IndexOf(SelfFloats, name) >= 0 ? FsmVariables.GlobalVariables.FindFsmFloat(name) : null;
                    FsmInt vi = System.Array.IndexOf(SelfInts, name) >= 0 ? FsmVariables.GlobalVariables.FindFsmInt(name) : null;
                    if (vf != null && float.TryParse(val, System.Globalization.NumberStyles.Float, Inv, out f)) { vf.Value = f; n++; }
                    else if (vi != null && int.TryParse(val, System.Globalization.NumberStyles.Integer, Inv, out i)) { vi.Value = i; n++; }
                }
                // Ce qui a ete lu reste a reecrire tel quel tant que l'automate n'est pas releve ici (Store avant son premier releve).
                kelaLine = string.Join(";", kept.ToArray());
                PlayMakerFSM k = Kela();
                string stNow = null;
                if (kelaState != null && k != null && kelaState != Pays[0].State)
                {
                    try { if (k.ActiveStateName != kelaState && k.Fsm.GetState(kelaState) != null) Game.SetState(k, kelaState); stNow = k.ActiveStateName; }
                    catch (System.Exception e) { Log.Warn("joueur : etat Kela '" + kelaState + "' pas remis (" + e.GetType().Name + ")"); }
                }
                // Usine : ses compteurs ; l'etat seulement s'il etait pointe (« State 2 ») et que l'automate attend en « Work ON ».
                usineLine = string.Join(";", keptUsine.ToArray());
                PlayMakerFSM u = Usine();
                string ustNow = null;
                if (usineState == "State 2" && u != null)
                {
                    try { if (u.ActiveStateName == "Work ON" && u.Fsm.GetState("State 2") != null) { Game.SetState(u, "State 2"); ustNow = u.ActiveStateName; } }
                    catch (System.Exception e) { Log.Warn("joueur : etat de l'usine 'State 2' pas remis (" + e.GetType().Name + ")"); }
                }
                Log.Info("joueur : etat du corps retrouve pour ce monde (" + n + " valeurs : faim, soif, fatigue...)"
                         + (kept.Count > 0 ? " ; Kela : " + nk + " valeurs" + (stNow != null ? ", etat " + stNow : kelaState != null ? ", etat '" + kelaState + "' pas remis" : "") + (k == null ? " (automate absent)" : "") : "")
                         + (keptUsine.Count > 0 ? " ; usine : " + nu + " compteurs" + (packages ? ", paquets" : "") + (ustNow != null ? ", pointe (" + ustNow + ")" : usineState != null ? ", etat '" + usineState + "' laisse" : "") + (u == null ? " (automate absent)" : "") : ""));
            }
            OwnRestored();
        }

        // MSC, invite : dette chez Teimo et reputation au dancing lues pour ce monde (0 sans ligne) : remises, et reecrites telles
        // quelles tant qu'elles ne sont pas relevees ici.
        static void OwnRestored()
        {
            if (!OwnMsc || Session.IsHost || ownDebt == null || danceRep < 0) return;
            var sb = new System.Text.StringBuilder();
            foreach (KeyValuePair<string, float> kv in ownDebt) sb.Append(sb.Length > 0 ? ";" : "").Append("teimo.").Append(kv.Key).Append(':').Append(kv.Value.ToString("R", Inv));
            teimoLine = sb.ToString();
            danceLine = "dancing.ReputationAlltime:" + danceRep.ToString(Inv);
            string debt = Shop.DebtClearedOn != null ? ApplyOwnDebt(Shop.DebtClearedOn) : "";
            float total = 0f; foreach (float v in ownDebt.Values) total += v;
            Log.Info("joueur : MSC, sa dette chez Teimo " + total.ToString("F2") + " mk" + (Shop.DebtClearedOn != null ? " (remise" + debt + ")" : " (remise a l'effacement de celle de l'hote)")
                     + ", sa reputation au dancing " + danceRep + PutDance("lecture de son etat"));
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
            world = pid != null && pid.Value != 0f ? pid.Value.ToString("F0") : "";   // (0 : monde inconnu, jamais une cle)
            if (world.Length == 0 && !Session.IsHost) Log.Warn("argent : PlayerID inconnu, porte-monnaie et etat du joueur ni retrouves ni gardes");
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
        public static string WorldId { get { return world ?? ""; } }
        static void FirstVisit()
        {
            if (Session.IsHost || Config.GetInt("Coop", "ArgentDepart", 1) == 0) return;
            float c = Config.GetFloat("Coop", "ArgentDepartLiquide", startCash), b = Config.GetFloat("Coop", "ArgentDepartBanque", startBank);
            if (c < 0 || b < 0) { Log.Info("argent : premiere venue dans ce monde, argent de depart inconnu : celui de la sauvegarde"); return; }
            // (difficulte de depart de l'hote : meme facteur que chez lui -- Difficulty)
            float df = Difficulty.MoneyFactor(world);
            if (df != 1f) { c = Mathf.Round(c * df); b = Mathf.Round(b * df); }
            Log.Info("argent : premiere venue dans ce monde (" + world + ") : argent de depart " + c + " liquide, " + b + " banque (au lieu de " + cash.Value + " / " + bank.Value + ")");
            cash.Value = c; bank.Value = b;
            Hud.Toast(Lang.T("Premi\u00E8re venue : ", "First visit: ") + Mathf.RoundToInt(c + b) + Lang.T(" mk pour commencer", " mk to start with"));
        }

        // Porte-monnaie de l'invite ecrit : a chaque changement vu, a la fin de session et avant un changement de niveau
        // (avant : toutes les 10 s seulement -- achat puis depart < 10 s : argent duplique a la session suivante, paie juste
        // avant de partir perdue ; relecture du 15/10). Public : a appeler aussi a la fermeture du jeu (Core.OnApplicationQuit,
        // avant Session.Stop), ou Update ne repasse plus.
        public static void Store()
        {
            if (Session.IsHost || cash == null || bank == null || world == null || world.Length == 0) return;
            var lines = new System.Collections.Generic.List<string>();
            if (System.IO.File.Exists(StorePath))
                foreach (string l in System.IO.File.ReadAllLines(StorePath)) if (!l.StartsWith(world + "=")) lines.Add(l);
            lines.Add(world + "=" + cash.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) + ";" + bank.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
            try { System.IO.File.WriteAllLines(StorePath, lines.ToArray()); } catch { }
            StoreSelf();
        }
        static bool ready, pidWaitLogged; static float pidWaitUntil;

        public static void OnLevelLoaded()
        {
            if (Game.LevelName == "MainMenu" && startCash < 0)
            {
                FsmFloat mc = FsmVariables.GlobalVariables.FindFsmFloat("PlayerMoney"), mb = FsmVariables.GlobalVariables.FindFsmFloat("PlayerBankAccount");
                if (mc != null && (mb != null || Game.Msc)) { startCash = mc.Value; startBank = mb != null ? mb.Value : 0f; /* (MSC : pas de banque) */ Log.Info("argent : au menu (nouvelle partie) " + startCash + " liquide, " + startBank + " banque"); }
            }
            // Avant la remise a zero : ce que l'invite a en poche est ecrit (relecture du 15/10) -- sauf si le menu a deja remis
            // les globales a leur valeur de nouvelle partie (ce ne serait plus son argent, mais celui d'une partie neuve).
            if (ready && cash != null && bank != null)
            {
                bool menuReset = startCash >= 0 && Mathf.Abs(cash.Value - startCash) < 0.005f && Mathf.Abs(bank.Value - startBank) < 0.005f
                                 && !(Mathf.Abs(lastCash - startCash) < 0.005f && Mathf.Abs(lastBank - startBank) < 0.005f);
                if (!menuReset) Store();
                else Log.Info("argent : globales deja remises a l'argent de depart par le menu, porte-monnaie pas reecrit (" + State() + ")");
            }
            cash = bank = null;
            ready = false; wasActive = false; pidWaitLogged = false; pidWaitUntil = Time.realtimeSinceStartup + 90f;
            readyAt = PlayerSync.InGame ? Time.realtimeSinceStartup + 15f : -1;   // apres le chargement de la sauvegarde
            foreach (Pay p in Pays) { p.F = null; p.Hooked = p.Open = false; p.At = -100; p.Day = -1; }
            nextPayScan = 0; payHooked = worldLocal = worldIn = 0;
            testStep = 0; remoteSince = -1;
            UnmuteTrigs();
            foreach (Trig g in Trigs) { g.F = null; g.Hooked = false; }
            envs.Clear(); envIncomeUntil = 0;
            claimAt = PlayerSync.InGame ? Time.realtimeSinceStartup + 4f : -1;
            saleLogic = saleSell = null; saleHooked = saleOpen = sellOffLogged = false; sellers.Clear();
            fleaSeller = -1; fleaSellerSent = -2; fleaReported = -1; fleaSellerAt = -1; nextFleaHost = nextFleaSend = nextFleaReport = 0;
            fleaTakeLogged = sellWasOn = false; fleaTables.Clear();
            kela = usine = null;
            guard = null; danceHooked = danceSeen = false; ownDebt = null; danceRep = -1;   // (relus pour ce monde par RestoreSelf ; Store ci-dessus a deja ecrit les lignes)

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
            p.C0 = cash.Value; p.B0 = bank.Value; p.Open = true; p.Replayed = Replay.Depth > 0;
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
            if (!p.Shared && p.Replayed && System.Array.IndexOf(Pays, p) < FirstGain && System.Array.IndexOf(Pays, p) >= 2 && Session.RemoteCount > 0)
            {
                // Etat de paie d'un boulot rejoue ici depuis un autre joueur : sa paie, pas la notre.
                Add(-dC, -dB);
                Log.Info("argent : " + p.Label + " +" + (dC + dB) + " verse par un rejeu (boulot d'un autre joueur) : retire");
                return;
            }
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

        // Libelle d'un versement dans la langue du joueur (les Label restent en francais sans accents pour le journal).
        static string Shown(string label)
        {
            switch (label)
            {
                case "allocation chomage": return Lang.T("allocation ch\u00F4mage", "unemployment benefit");
                case "aide au logement": return Lang.T("aide au logement", "housing support");
                case "paie de l'usine": return Lang.T("paie de l'usine", "factory pay");
                case "paie de l'usine (heures sup)": return Lang.T("paie de l'usine (heures sup)", "factory pay (overtime)");
                case "paie des publicites": return Lang.T("paie des publicit\u00E9s", "ads pay");
                case "paie du taxi": return Lang.T("paie du taxi", "taxi pay");
                case "interets": return Lang.T("int\u00E9r\u00EAts", "interest");
                case "rachat de ferraille": return Lang.T("rachat de ferraille", "scrap sale");
                case "ventes aux puces": return Lang.T("ventes aux puces", "flea market sales");
                case "kilju vendu a Jokke": return Lang.T("kilju vendu \u00E0 Jokke", "kilju sold to Jokke");
                case "gains du loto": return Lang.T("gains du loto", "lottery win");
                case "prix du rallye": return Lang.T("prix du rallye", "rally prize");
                case "prix de la course sur glace": return Lang.T("prix de la course sur glace", "ice race prize");
            }
            return Lang.Fr ? label : Lang.R("income");
        }

        // Versement partage vu chez un autre joueur.
        static void WorldIn(Pay p, float c, float b, string name)
        {
            worldIn++;
            float adj = Merge(p, c, b, false);
            if (adj >= 1f) Hud.Toast("+" + Mathf.RoundToInt(adj) + " mk (" + Shown(p.Label) + ")");
            Log.Info("argent : " + p.Label + " de " + name + " : " + (c + b) + (adj > 0.005f ? ", +" + adj + " ici" : ", deja compte ici"));
        }

        // Un montant de plus pour le versement en cours (ici : deja ajoute par le jeu ; ailleurs : pas encore) :
        // le porte-monnaie garde le plus haut vu, une seule fois. Rend l'ajustement fait.
        // Jour de jeu (GlobalDaysPassed : compte total, pas le jour de la semaine -- deux vendredis sont deux versements) ; -1 : inconnu.
        static int GameDay()
        {
            FsmInt d = FsmVariables.GlobalVariables.FindFsmInt("GlobalDaysPassed");
            return d != null ? d.Value : -1;
        }

        static float Merge(Pay p, float c, float b, bool local)
        {
            float now = Time.realtimeSinceStartup;
            // Meme versement : meme jour de jeu (ou, a defaut, moins de PayWindow s reelles : jour pas encore recale ici). Avant, 30 s
            // reelles seulement : un invite en chargement (ou en pause) voyait celui de l'hote, puis sa propre logique versait le
            // sien > 30 s plus tard, pris pour un nouveau : compte deux fois ; et un 2e montant hors fenetre etait ajoute en
            // entier (relecture du 15/10). Un autre jour = un nouveau versement, meme a quelques secondes (sommeil accelere).
            int day = GameDay();
            bool same = p.At >= 0 && (day >= 0 && day == p.Day || now - p.At <= PayWindow);
            if (!same) { p.At = now; p.Day = day; p.HaveC = p.HaveB = p.BestC = p.BestB = p.Sent = 0; }   // nouveau versement
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
                        FsmHooks.Prepend(st, new TrigHook { G = g, St = st });
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
        //  - table arretee chez l'hote (objet inactif : rien ne se vendait jamais pour les invites -- relecture du 15/10,
        //    ecart n8) : meme reprise que les telephones (Calls.RunsHere). L'hote dit toutes les 5 s qui tire les ventes
        //    (@puces 0 : lui si sa table tourne, sinon l'invite de plus petit numero dont la table tourne, d'apres leurs avis
        //    @puces 1) ; ce seul vendeur fait tourner 'Sell' et envoie chaque vente a l'hote (@puces 2), qui la credite a
        //    celui qui a pose l'objet comme d'habitude (K_FleaCredit ; objet de l'hote : son MoneyTotal). Hote muet 40 s :
        //    l'invite rend la main.
        static PlayMakerFSM saleLogic, saleSell;
        static bool saleHooked, saleOpen, sellOffLogged;
        static float saleStart, saleAt, nextSaleCheck;
        static readonly Dictionary<string, int> sellers = new Dictionary<string, int>();
        static int fleaSeller = -1, fleaSellerSent = -2, fleaReported = -1;   // vendeur (hote : designe ; invite : dernier recu) ; -1 personne
        static float fleaSellerAt = -1, nextFleaHost, nextFleaSend, nextFleaReport;
        static bool fleaTakeLogged, sellWasOn;                                 // sellWasOn : 'Sell' arrete ici par ce module (a rallumer)
        static readonly Dictionary<int, float> fleaTables = new Dictionary<int, float>();   // hote : invites dont la table tourne (dernier avis)

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
                GameObject st = Game.FindAny("FleaMarket/SaleTable");   // (inactif compris : table arretee ici, a dire aux autres)
                if (st != null) { saleLogic = Game.FsmOn(st, "Logic"); saleSell = Game.FsmOn(st, "Sell"); }
            }
            bool runsHere = saleLogic != null && saleLogic.gameObject.activeInHierarchy;
            FleaSellerStep(now, runsHere);
            if (saleLogic == null) return;
            if (!saleHooked && runsHere)
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
            if (!Session.IsHost && saleSell != null)
            {
                if (SellsHere(now, runsHere))
                {
                    // Vendeur designe par l'hote : 'Sell' rallume s'il avait ete arrete ici (sinon la logique du jeu en decide, comme chez l'hote).
                    if (!saleSell.enabled && sellWasOn) saleSell.enabled = true;
                    if (!fleaTakeLogged) { fleaTakeLogged = true; Log.Info("argent : la table des puces ne tourne pas chez l'hote, les ventes sont tirees ici (vendeur designe par l'hote)"); }
                }
                else
                {
                    if (fleaTakeLogged) { fleaTakeLogged = false; Log.Info("argent : ventes des puces rendues (" + (fleaSeller == 0 ? "la table tourne de nouveau chez l'hote" : fleaSeller < 0 ? "hote muet ou table arretee ici" : "autre vendeur #" + fleaSeller) + ")"); }
                    if (saleSell.enabled)
                    {
                        saleSell.enabled = false; sellWasOn = true;
                        if (!sellOffLogged) { sellOffLogged = true; Log.Info("argent : ventes des puces tirees par l'hote seul ('Sell' arrete ici)"); }
                    }
                }
            }
            if (saleOpen && now - saleAt > 2f) SettleSale();   // fin de 'Find item' pas vue
            // Ventes creditees hors jeu (OnFlea), ou tirees par un invite pour l'hote (OnFleaSpecial) : ajoutees des que la table est la
            // (une par passage, dans l'ordre).
            if (pendingFlea.Count > 0 && FleaCredit(pendingFlea[0].Key, pendingFlea[0].Value)) pendingFlea.RemoveAt(0);
        }

        // Invite : c'est lui qui tire les ventes (designe par l'hote il y a moins de 40 s, et sa table tourne).
        static bool SellsHere(float now, bool runsHere) { return runsHere && fleaSeller == Session.LocalId && fleaSellerAt >= 0 && now - fleaSellerAt < 40f; }

        // @puces : U8 genre ; 0 (hote -> invites) : U8 vendeur (255 : personne) ; 1 (invite -> hote) : Bool table active chez lui ;
        // 2 (vendeur invite -> hote) : F32 montant, Str objet. Fiables ; l'hote relaie tout @ aux autres invites, qui ignorent 1 et 2.
        static void FleaSellerStep(float now, bool runsHere)
        {
            if (!Session.Active) return;
            if (Session.IsHost)
            {
                if (now < nextFleaHost) return;
                nextFleaHost = now + 5f;
                if (Session.RemoteCount == 0) { fleaSellerSent = -2; return; }   // (renvoye des qu'un invite est la)
                int seller = runsHere ? Session.LocalId : -1;
                if (seller < 0)
                    foreach (KeyValuePair<int, float> kv in fleaTables)
                        if (now - kv.Value < 30f && Session.Players.ContainsKey(kv.Key) && (seller < 0 || kv.Key < seller)) seller = kv.Key;
                if (seller == fleaSellerSent && now < nextFleaSend) return;
                if (seller != fleaSellerSent)
                    Log.Info("argent : ventes aux puces tirees par " + (seller == Session.LocalId ? "l'hote" : seller < 0 ? "personne (table arretee chez l'hote, active chez aucun invite)" : "l'invite #" + seller + " (table arretee chez l'hote)"));
                fleaSellerSent = fleaSeller = seller; nextFleaSend = now + 30f;
                Session.SendAll(new NetWriter(Msg.Job).U8(Session.LocalId).Str("@puces").U8(0).U8(seller < 0 ? 255 : seller), true);
                return;
            }
            int state = runsHere ? 1 : 0;
            if (state == fleaReported && now < nextFleaReport) return;
            fleaReported = state; nextFleaReport = now + 20f;
            Session.SendAll(new NetWriter(Msg.Job).U8(Session.LocalId).Str("@puces").U8(1).Bool(runsHere), true);
        }

        // Objet que 'Find item' vient de vendre (ItemSold, sinon Item).
        static string SoldKey()
        {
            FsmGameObject sv = saleLogic.FsmVariables.FindFsmGameObject("ItemSold"), iv = saleLogic.FsmVariables.FindFsmGameObject("Item");
            GameObject sold = sv != null && sv.Value != null ? sv.Value : iv != null ? iv.Value : null;
            return ItemKey(sold);
        }

        // Messages @puces (Jobs.OnSpecial).
        public static void OnFleaSpecial(int who, NetReader r)
        {
            int kind = r.U8();
            float now = Time.realtimeSinceStartup;
            if (kind == 0)
            {
                if (Session.IsHost || who != 0) return;
                int v = r.U8(), desig = v == 255 ? -1 : v;
                if (desig != fleaSeller) Log.Info("argent : vendeur aux puces designe par l'hote : " + (desig < 0 ? "personne" : desig == Session.LocalId ? "ce joueur-ci" : desig == 0 ? "l'hote" : "#" + desig));
                fleaSeller = desig; fleaSellerAt = now;
                return;
            }
            if (!Session.IsHost) return;
            if (kind == 1) { if (r.Bool()) fleaTables[who] = now; else fleaTables.Remove(who); return; }
            if (kind != 2) return;
            float d = r.F32();
            string key = r.Str();
            if (d <= 0f || d > 1e6f) return;
            if (saleLogic != null && saleLogic.gameObject.activeInHierarchy)
            {
                // La table tourne de nouveau ici (vendeur pas encore prevenu) : les tirages de l'hote font foi, pas celui-la.
                Log.Info("argent : vente aux puces de #" + who + " (" + key + ", +" + d + ") alors que la table tourne chez l'hote : ignoree");
                return;
            }
            int seller;
            if (!sellers.TryGetValue(key, out seller) || !Session.Players.ContainsKey(seller)) seller = Session.LocalId;
            if (seller == Session.LocalId)
            {
                // Objet de l'hote (ou vendeur parti) : son MoneyTotal, meme table arretee (variable de l'automate ; en file si elle manque).
                if (!FleaCredit(d, key)) pendingFlea.Add(new KeyValuePair<float, string>(d, key));
                Log.Info("argent : " + key + " vendu aux puces par #" + who + " (+" + d + ") pour l'hote");
                return;
            }
            sellers.Remove(key);
            Session.SendAll(new NetWriter(Msg.Home).U8(Session.LocalId).U8(Home.K_FleaCredit).U8(seller).F32(d).Str(key), true);
            Log.Info("argent : " + key + " vendu aux puces par #" + who + " (+" + d + ") pour #" + seller + " : credite chez lui");
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
                if (SellsHere(Time.realtimeSinceStartup, true))
                {
                    // Vendeur designe par l'hote (sa table ne tourne pas chez lui) : la vente part chez lui, il la credite a celui qui a
                    // pose l'objet (K_FleaCredit, nous compris) -- le MoneyTotal d'ici n'est touche que par ce credit (ecart n8).
                    string sk = SoldKey();
                    Session.SendAll(new NetWriter(Msg.Job).U8(Session.LocalId).Str("@puces").U8(2).F32(d).Str(sk), true);
                    Log.Info("argent : " + sk + " vendu aux puces +" + d + " (tire ici a la place de l'hote, envoye a l'hote qui credite le vendeur)");
                    return;
                }
                Log.Info("argent : vente aux puces tiree par la logique de cet invite (+" + d + ") : pas comptee (l'hote vend)");
                return;
            }
            string key = SoldKey();
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
            // Hors jeu (menu, chargement) ou table pas encore la : garde, credite une fois en partie (avant : jete par Home ;
            // relecture du 15/10). La file survit au changement de niveau (pas videe par OnLevelLoaded).
            if (!PlayerSync.InGame || !ready || !FleaCredit(amount, item))
            {
                pendingFlea.Add(new KeyValuePair<float, string>(amount, item));
                Log.Info("argent : " + item + " vendu aux puces par l'hote pour ce joueur (+" + amount + ") : " + (PlayerSync.InGame ? "table absente ici" : "hors jeu") + ", garde pour plus tard");
            }
        }
        static readonly List<KeyValuePair<float, string>> pendingFlea = new List<KeyValuePair<float, string>>();

        // Ajoute la vente au MoneyTotal de la table (enveloppe MoneyFlea a la fin de la location). Faux : table absente ici.
        static bool FleaCredit(float amount, string item)
        {
            if (saleLogic == null) { GameObject st = Game.FindAny("FleaMarket/SaleTable"); saleLogic = st != null ? Game.FsmOn(st, "Logic") : null; }
            FsmFloat mt = MoneyTotal;
            if (mt == null) return false;
            mt.Value += amount;
            Hud.Toast(item + Lang.T(" vendu aux puces : +", " sold at the flea market: +") + Mathf.RoundToInt(amount) + Lang.T(" mk (enveloppe \u00E0 la fin de la location)", " mk (envelope when the rental ends)"));
            Log.Info("argent : " + item + " vendu aux puces par l'hote pour ce joueur : +" + amount + " (MoneyTotal " + mt.Value + ")");
            return true;
        }

        static bool wasActive;

        public static void Update()
        {
            UnmuteTrigs();   // ecritures d'argent coupees pour un seul passage (etat deja entre)
            DanceTick();     // (MSC, invite : reputation au dancing remise au premier allumage du videur)

            // Fin de session en cours de partie (deconnexion de l'hote, exclusion) : porte-monnaie ecrit tout de suite (relecture du 15/10).
            if (wasActive && !Session.Active) { wasActive = false; if (ready) Store(); }
            if (Session.Active) wasActive = true;
            if (Session.Active && claimAt > 0 && Time.realtimeSinceStartup >= claimAt) { claimAt = -1; ClaimEarly(); }
            if (!Session.Active || readyAt < 0 || Time.realtimeSinceStartup < readyAt) return;
            if (!ready)
            {
                cash = FsmVariables.GlobalVariables.FindFsmFloat("PlayerMoney");
                bank = FsmVariables.GlobalVariables.FindFsmFloat("PlayerBankAccount");
                // (My Summer Car : pas de compte en banque -- une variable a nous, toujours 0 : le format "liquide;banque" reste le meme)
                if (bank == null && Game.Msc) bank = new FsmFloat("PlayerBankAccount");
                if (cash == null || bank == null) { readyAt = -1; Log.Warn("argent : globales absentes"); return; }
                // PlayerID pas encore connu (0 : nouvelle partie en cours d'ecriture) : la cle du monde serait "0", la meme pour
                // toutes les parties neuves -- argent d'une partie d'avant retrouve dans la nouvelle (retour de Donskoy, 18/10).
                // On attend (30 s au plus), puis la cle vide : rien de retrouve, rien d'ecrit.
                FsmFloat pid0 = FsmVariables.GlobalVariables.FindFsmFloat("PlayerID");
                if (!Session.IsHost && (pid0 == null || pid0.Value == 0f) && Time.realtimeSinceStartup < pidWaitUntil)
                {
                    readyAt = Time.realtimeSinceStartup + 2f;
                    if (!pidWaitLogged) { pidWaitLogged = true; Log.Info("argent : PlayerID pas encore connu, porte-monnaie et etat du joueur attendus"); }
                    return;
                }
                Restore();
                if (pendingGift > 0) { cash.Value += pendingGift; Hud.Toast(pendingFrom + Lang.T(" vous a donn\u00E9 ", " gave you ") + pendingGift + " mk"); pendingGift = 0; }
                if (pendingRefund > 0) { cash.Value += pendingRefund; Hud.Toast(Lang.T("Don rendu (joueur parti) : ", "Gift returned (player left): ") + pendingRefund + " mk"); pendingRefund = 0; }
                lastCash = cash.Value; lastBank = bank.Value;
                ready = true;
                Log.Info("argent : liquide " + lastCash + ", banque " + lastBank);
            }
            if (!Game.Msc) { HookPays(); HookTrigs(); }   // (MSC : versements et objets de MWC absents)
            HookEnvelopes();
            if (!Game.Msc) FleaStep();
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
            Store();   // changement vu (achat, paie, transfert) : ecrit tout de suite, pas seulement au passage des 10 s (relecture du 15/10)
            if (inC < 0.005f && inB < 0.005f) return;
            float nowU = Time.realtimeSinceStartup;
            if (Machines.KeepsMoney) { Log.Info("argent : gain " + (inC + inB) + " d'une machine a jeu, garde pour soi"); return; }
            if (ModCheatbox.MenuOpen()) { Log.Info("argent : +" + (inC + inB) + " tape dans le menu de Cheatbox++, garde pour soi"); return; }
            if (nowU < suppressUntil && nowU >= envIncomeUntil && Session.RemoteCount > 0)
            {
                // Paie versee ici par le boulot d'un autre joueur, rejoue (Jobs) : la sienne, pas la notre.
                cash.Value -= inC; bank.Value -= inB;
                lastCash = cash.Value; lastBank = bank.Value;
                Log.Info("argent : revenu " + (inC + inB) + " venu d'un boulot rejoue (celui d'un autre joueur) : retire");
                return;
            }
            Log.Info("argent : revenu " + inC + " (liquide) + " + inB + " (banque), garde pour soi (chacun sa paie)");
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
            // Revenu d'un joueur (versions avant 0.75.42) : a lui, chacun sa paie.
            Log.Info("argent : revenu " + (inC + inB) + " annonce par " + name + " : a lui (chacun sa paie), rien ici");
        }

        // ---------------------------------------------------------------- enveloppes de paie
        // Le client d'un boulot (fosses septiques HouseShit*, livraisons de bois HouseWood*, fermier) ou l'organisateur
        // (pas les prix des courses, PriceMoney* : podium de chacun, voir Pays) tend une enveloppe : PayMoney :: Use. Clic -> "State 1" (PlayerMoney +=
        // Money, animation), puis "State 3" (Money a 0, enveloppe cachee) ; le bois passe aussi par "Pay for car" -> "State 3".
        // Le boulot est rejoue chez tous : chacun a SA copie de l'enveloppe, pleine. Le monde (WorldFsms) ne l'aurait
        // suivie qu'a son releve suivant (etale : jusqu'a une minute apres son apparition) ; prise par l'un avant, elle
        // restait pleine chez l'autre, qui la prenait aussi, et chaque prise etant un revenu partage, chacun touchait la
        // paie DEUX fois. Reservees ici des le chargement (inactives comprises) : prise par un joueur (entree en "State 1", le
        // paiement meme -- avant, "State 3", la fin de l'animation : deux clics a moins d'une seconde payaient tous les deux ;
        // relecture du 15/10), elle est retiree chez les autres ("State 3" : ni argent ni son), meme si leur copie n'apparait
        // qu'apres (60 s). Le premier clic arrive paie ; deux clics a quelques ms (moins qu'une latence) restent indecidables :
        // chacun garde ce qu'il a touche, note dans le journal.
        // La paie est un revenu ordinaire du preneur (Update : jamais pris pour celle d'un boulot rejoue), gardee par lui
        // seul (0.75.42). Pas l'enveloppe de Jokke (gain d'objet, voir Pays).
        class Env { public string Key; public PlayMakerFSM F; public bool Hooked; public float RetireUntil = -1, TakenAt = -1; public string TakeState; }

        // My Summer Car : l'etat qui paie (FloatAdd sur PlayerMoney) -- "State 1" le plus souvent, "Anim" chez Jokke (demenagement),
        // "State 6" chez la grand-mere. Null : aucun.
        static FsmState PayState(PlayMakerFSM f)
        {
            foreach (FsmState s in f.Fsm.States)
                foreach (FsmStateAction a in s.Actions)
                {
                    var fa = a as HutongGames.PlayMaker.Actions.FloatAdd;
                    if (fa != null && fa.floatVariable != null && fa.floatVariable.Name == "PlayerMoney") return s;
                }
            return f.Fsm.GetState("State 1");
        }
        static readonly List<Env> envs = new List<Env>();
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
                        if (Take) envIncomeUntil = Time.realtimeSinceStartup + 3f;   // la paie qui suit est a nous (pas celle d'un boulot rejoue)
                        EnvTaken(E);   // des le paiement ("State 1") ; "State 3" seul pour l'achat de la voiture du bois ("Pay for car")
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
            // (My Summer Car : le PayMoney de la machine a sous est un bouton de mise, celui du rallye un prix personnel)
            if (Game.Msc && n == "PayMoney") { string p = Recon.Path(f.transform); if (p.Contains("SlotMachine") || p.StartsWith("RALLY/")) return false; }
            if (Game.Msc && n == "PayMoneyAdvert") return true;   // (MSC : paie des tracts de Teimo, STORE/LOD/ActivateStore/PayMoneyAdvert)
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
                        // (My Summer Car : l'etat qui paie seulement, trouve a son FloatAdd PlayerMoney -- beaucoup d'enveloppes n'ont pas
                        // de "State 3", celle de Jokke paie en "Anim", celle de la grand-mere en "State 6")
                        if (Game.Msc)
                        {
                            FsmState pay = PayState(e.F);
                            if (pay == null) { e.Hooked = true; Log.Warn("argent : enveloppe " + e.Key + " sans etat de paie"); continue; }
                            if (!pay.IsInitialized) continue;
                            FsmHooks.Prepend(pay, new EnvHook { E = e, Take = true });
                            // (tracts de Teimo : montant de l'enveloppe = la part du joueur local, voir TractsHook)
                            FsmState wb = IsTracts(e) ? e.F.Fsm.GetState("Wait button") : null;
                            if (wb != null) FsmHooks.Prepend(wb, new TractsHook { E = e });
                            e.TakeState = pay.Name;
                            e.Hooked = true;
                            Log.Info("argent : enveloppe " + e.Key + " tendue ici (paie en '" + pay.Name + "')");
                            continue;
                        }
                        FsmState take = e.F.Fsm.GetState("State 1"), end = e.F.Fsm.GetState("State 3");
                        if (take == null || end == null) { e.Hooked = true; Log.Warn("argent : enveloppe " + e.Key + " sans State 1/State 3"); continue; }
                        if (!take.IsInitialized || !end.IsInitialized) continue;
                        FsmHooks.Prepend(take, new EnvHook { E = e, Take = true });
                        FsmHooks.Prepend(end, new EnvHook { E = e });
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

        // Tracts de Teimo, My Summer Car (audit MSC 02/11) : l'enveloppe (PayMoneyAdvert : 'Calculate' Money = Delivered x 25, puis 'Wait
        // button' l'affiche) payait au premier preneur toute la tournee de l'equipe, rien aux autres. A plusieurs, en entrant dans 'Wait
        // button', Money et le texte deviennent la part du joueur local (ses boites et celles de personne, TractsMsc.TakerCount) ; a la
        // prise, les parts des autres partent avec le message (EnvTaken) et chacun touche la sienne chez lui (OnPayout). La remise a zero
        // du boulot reste commune (Retire). [Test] TractsParJoueur=0 : comme avant.
        const float TractPrice = 25f;   // PricePerAd du jeu (vidage v.250908-04)
        static bool IsTracts(Env e) { return Game.Msc && e != null && e.F != null && e.F.gameObject.name == "PayMoneyAdvert"; }

        class TractsHook : ModHook
        {
            public override string Module { get { return "argent"; } }
            public Env E;
            public override void OnEnter()
            {
                try
                {
                    if (!envApplying && Replay.Depth == 0 && TractsMsc.SplitPay())
                    {
                        int mine = TractsMsc.TakerCount(new List<KeyValuePair<int, int>>());
                        FsmFloat m = E.F.FsmVariables.FindFsmFloat("Money");
                        FsmFloat price = E.F.FsmVariables.FindFsmFloat("PricePerAd");
                        float amount = mine * (price != null && price.Value > 0f ? price.Value : TractPrice);
                        if (m != null && Mathf.Abs(m.Value - amount) > 0.01f)
                        {
                            m.Value = amount;
                            FsmString v = E.F.FsmVariables.FindFsmString("Value");
                            if (v != null) v.Value = "AD DELIVERY PAYMENT " + amount + " MK";
                            MWCoop.Log.Info("argent : tracts, enveloppe a ta part " + mine + " boites = " + amount + " mk (parts " + TractsMsc.Shares() + ")");
                        }
                    }
                }
                catch (System.Exception ex) { Replay.HookError(ex); }
                Finish();
            }
        }

        // Prise ici (ou achat de la voiture du client du bois) : retiree chez les autres.
        static void EnvTaken(Env e)
        {
            float now = Time.realtimeSinceStartup;
            if (e.TakenAt >= 0 && now - e.TakenAt < 10f) return;   // ("State 3" apres "State 1" : la meme prise, deja envoyee)
            e.TakenAt = now;
            Log.Info("argent : enveloppe " + e.Key + " prise ici");
            var w = new NetWriter(Msg.Payout).U8(Session.LocalId).Str(e.Key);
            // (tracts : parts des autres joueurs, U8 n puis (U8 joueur, U16 boites) x n ; ignorees par une version d'avant)
            if (IsTracts(e) && TractsMsc.SplitPay())
            {
                var others = new List<KeyValuePair<int, int>>();
                TractsMsc.TakerCount(others);
                w.U8(System.Math.Min(others.Count, 255));
                for (int i = 0; i < others.Count && i < 255; i++) w.U8(others[i].Key).U16(System.Math.Min(others[i].Value, 65535));
                if (others.Count > 0) Log.Info("argent : tracts, parts des autres envoyees (" + TractsMsc.Shares() + ")");
            }
            Session.SendAll(w, true);
        }

        static void Retire(Env e)
        {
            e.RetireUntil = -1;
            string st = e.F.ActiveStateName;
            if (st == "State 3" || st == "State 5") return;
            envApplying = true; Replay.Depth++;
            try
            {
                // (My Summer Car : prise chez l'autre a 0 mk -- l'etat de paie fait aussi la suite du boulot : fermier JobStage, oncle
                // et Jokke Paid, tracts JobAccepted, demenagement, grand-mere... ; avant, cela n'arrivait que chez celui qui la prenait)
                if (Game.Msc && e.TakeState != null)
                {
                    if (st == e.TakeState) return;
                    FsmFloat m = e.F.FsmVariables.FindFsmFloat("Money"); if (m != null) m.Value = 0f;
                    // (et l'ajout a PlayerMoney coupe le temps d'y entrer : le montant n'est pas toujours la variable Money)
                    var cut = new List<FsmStateAction>();
                    FsmState ts = e.F.Fsm.GetState(e.TakeState);
                    if (ts != null)
                        foreach (FsmStateAction a in ts.Actions)
                        {
                            var fa = a as HutongGames.PlayMaker.Actions.FloatAdd;
                            if (fa != null && a.Enabled && fa.floatVariable != null && fa.floatVariable.Name == "PlayerMoney") { a.Enabled = false; cut.Add(a); }
                        }
                    try { Game.SetState(e.F, e.TakeState); }
                    finally { foreach (FsmStateAction a in cut) a.Enabled = true; }
                }
                else if (e.F.Fsm.GetState("State 3") != null) Game.SetState(e.F, "State 3");
                else
                {
                    // (MSC, pas de "State 3" : videe et cachee, comme la fin de son "State 1")
                    FsmFloat m = e.F.FsmVariables.FindFsmFloat("Money"); if (m != null) m.Value = 0f;
                    e.F.gameObject.SetActive(false);
                }
            }
            finally { envApplying = false; Replay.Depth--; }
            Log.Info("argent : enveloppe " + e.Key + " retiree ici (" + st + " -> " + e.F.ActiveStateName + ")");
        }

        public static void OnPayout(Peer from, NetReader r)
        {
            int who = r.U8();
            if (Session.IsHost) who = from.Id;
            string key = r.Str();
            if (Session.IsHost) Session.Broadcast(new NetWriter(Msg.Payout).U8(who).Str(key).Raw(r.Rest()), true, who);   // (parts des tracts comprises)
            Env e = envs.Find(x => x.Key == key);
            if (e == null || e.F == null) { Log.Warn("argent : enveloppe " + key + " prise par #" + who + ", introuvable ici"); return; }
            PlayerInfo pi;
            Log.Info("argent : enveloppe " + key + " prise par " + (Session.Players.TryGetValue(who, out pi) ? pi.Name : "#" + who));
            // Tracts (audit MSC 02/11) : notre part, comptee par le preneur, versee ici -- revenu a nous, comme une enveloppe prise.
            if (IsTracts(e) && r.More && Config.GetInt("Test", "TractsParJoueur", 1) != 0)
            {
                int share = 0;
                for (int i = 0, n = r.U8(); i < n; i++) { int id = r.U8(), c = r.U16(); if (id == Session.LocalId) share = c; }
                // (prise ici aussi a moins d'une latence : notre part est deja dans ce qu'on a touche)
                if (share > 0 && (e.TakenAt < 0 || Time.realtimeSinceStartup - e.TakenAt >= 3f))
                {
                    FsmFloat price = e.F.FsmVariables.FindFsmFloat("PricePerAd");
                    float amount = share * (price != null && price.Value > 0f ? price.Value : TractPrice);
                    FsmFloat cash = FsmVariables.GlobalVariables.FindFsmFloat("PlayerMoney"), net = FsmVariables.GlobalVariables.FindFsmFloat("PlayerNetIncome");
                    envIncomeUntil = Time.realtimeSinceStartup + 3f;   // (pas pris pour la paie d'un boulot rejoue)
                    if (cash != null) cash.Value += amount;
                    if (net != null) net.Value += amount;
                    Log.Info("argent : tracts, ta part " + share + " x " + (amount / share) + " = " + amount + " mk versee ici (enveloppe prise par #" + who + ")");
                }
            }
            // (prise ici aussi a moins d'une latence : les deux clics ont paye chacun chez soi, indecidable -- note, rien retire)
            if (e.TakenAt >= 0 && Time.realtimeSinceStartup - e.TakenAt < 3f) Log.Warn("argent : enveloppe " + key + " prise ici aussi a quelques ms pres : chacun garde sa paie");
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

        // ---------------------------------------------------------------- essai paiefosse (dossier #58)
        // [Test] Autotest=paiefosse (2 joueurs ; [Test] PaieFosseMaison=3) : la paie d'une fosse septique prise par l'invite
        // (dossier #58, 08/11 : l'invite devant le client « я не могу взять », l'hote prend l'enveloppe et lui donne l'argent).
        // T = 40 s apres l'arrivee de l'autre. T : l'hote passe la commande (niveau 4,95, ORDERTAKEN -> Full, rejoue chez
        // l'invite). T+3 : chacun pres de la maison, client allume chez les deux ([Test] PaieFossePres=0 : l'hote reste ou il
        // est). T+15 : l'hote (autorite des fosses, GIFU garee) vide la fosse (0,3 -> Pay). De T+5 a T+45, chacun note a chaque
        // changement l'etat de la fosse, les gestes du client et l'enveloppe. T+28 : l'invite se place a 0,6 m devant SON
        // enveloppe, l'hote a 2,5 m sur le cote. T+30 : chacun sonde, d'un meme oeil virtuel (0,6 m devant l'enveloppe, 1,6 m
        // au-dessus du sol), puis l'invite de sa camera, ce que le rayon du survol (MousePickEvent de 'Wait player', memes
        // calques et portee) touche avant l'enveloppe. T+33 : l'invite la prend comme un clic ('Wait button' puis USE). T+40 :
        // argent et enveloppe chez chacun. Attendu : l'invite paye, l'enveloppe de l'hote retiree. Rien que de l'essai.
        static int pfStep, pfLogs; static float pfAt; static string pfLast; static Vector3 pfDir;
        static int PfHouse { get { return Config.GetInt("Test", "PaieFosseMaison", 3); } }
        static string PfWell { get { int m = PfHouse; return "JOBS/HouseShit" + m + "/WasteWell_2000litre/Shit/Level" + (Game.Msc ? m.ToString() : "") + "/ShitLevelTrigger"; } }
        static string PfMan { get { int m = PfHouse; return "JOBS/HouseShit" + m + "/LOD/ShitNPC/" + (Game.Msc ? "ShitMan" + m : "Man"); } }
        static Transform Deep(Transform t, string n)
        {
            if (t.name == n) return t;
            for (int i = 0; i < t.childCount; i++) { Transform r = Deep(t.GetChild(i), n); if (r != null) return r; }
            return null;
        }
        static Transform PfPay() { GameObject man = Game.FindAny(PfMan); return man != null ? Deep(man.transform, "PayMoney") : null; }
        static string PfF(FsmFloat f) { return f != null ? f.Value.ToString("F2") : "?"; }
        static string PfB(FsmBool b) { return b != null ? (b.Value ? "1" : "0") : "?"; }
        static string PfCash() { FsmFloat c = FsmVariables.GlobalVariables.FindFsmFloat("PlayerMoney"); return c != null ? c.Value.ToString("F0") : "?"; }

        static string PfState(bool withLevel)
        {
            GameObject wg = Game.FindAny(PfWell), man = Game.FindAny(PfMan);
            PlayMakerFSM lv = wg != null ? Game.FsmOn(wg, "Level") : null, an = man != null ? Game.FsmOn(man, "Anims") : null;
            Transform pay = PfPay();
            PlayMakerFSM use = pay != null ? Game.FsmOn(pay.gameObject, "Use") : null;
            return "fosse " + (lv != null ? lv.ActiveStateName : "?") + (withLevel ? " niveau " + PfF(lv != null ? lv.FsmVariables.FindFsmFloat("ShitLevel") : null) : "")
                 + " commande " + PfB(lv != null ? lv.FsmVariables.FindFsmBool("Called") : null)
                 + ", client " + (man == null ? "introuvable" : man.activeInHierarchy ? "LA" : "absent") + " gestes " + (an != null ? an.ActiveStateName + (an.enabled ? "" : " (arrete)") : "?")
                 + " Money " + PfB(an != null ? an.FsmVariables.FindFsmBool("Money") : null) + " prix " + PfF(an != null ? an.FsmVariables.FindFsmFloat("Price") : null)
                 + ", enveloppe " + (pay == null ? "introuvable" : (pay.gameObject.activeInHierarchy ? "TENDUE" : "rangee") + " etat " + (use != null ? use.ActiveStateName : "?")
                    + " Money " + PfF(use != null ? use.FsmVariables.FindFsmFloat("Money") : null));
        }

        // Pose le joueur d'ici en (x, z), sur le premier sol sous lui qui n'est pas le client ni sa chaise.
        static string PfPlace(Vector3 at)
        {
            GameObject pl = Game.PlayerGo, npc = Game.FindAny("JOBS/HouseShit" + PfHouse + "/LOD/ShitNPC");
            if (pl == null) return "pas de joueur";
            Vector3 top = new Vector3(at.x, at.y + 40f, at.z);
            RaycastHit[] hits = Physics.RaycastAll(top, Vector3.down, 90f, ~(1 << 2));
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            float y = at.y;
            foreach (RaycastHit h in hits)
            {
                if (h.collider.isTrigger || (npc != null && h.transform.IsChildOf(npc.transform)) || h.transform.IsChildOf(pl.transform)) continue;
                y = h.point.y; break;
            }
            var cc = pl.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;
            pl.transform.position = new Vector3(at.x, y + 0.3f, at.z);
            if (cc != null) cc.enabled = true;
            return "pose en " + pl.transform.position.ToString("F2");
        }

        // Ce que le rayon du survol de l'enveloppe touche, d'un oeil donne (memes calques et portee que son MousePickEvent).
        static string PfRay(Vector3 eye, string who)
        {
            Transform pay = PfPay();
            if (pay == null) return who + " : enveloppe introuvable";
            PlayMakerFSM use = Game.FsmOn(pay.gameObject, "Use");
            var sb = new System.Text.StringBuilder(who + " : enveloppe " + (pay.gameObject.activeInHierarchy ? "tendue" : "rangee") + " (" + (use != null ? use.ActiveStateName : "?") + ") en " + pay.position.ToString("F2"));
            float reach = 1f; int mask = -5;
            FsmState wp = use != null ? use.Fsm.GetState("Wait player") : null;
            FsmStateAction[] acts = null;
            if (wp != null && wp.IsInitialized) try { acts = wp.Actions; } catch { }   // (automate jamais demarre : actions pas chargees)
            if (acts == null) sb.Append(", automate pas demarre (survol inconnu, 1 m pris)");
            if (acts != null)
                foreach (FsmStateAction a in acts)
                {
                    var mp = a as HutongGames.PlayMaker.Actions.MousePickEvent;
                    if (mp == null) continue;
                    reach = mp.rayDistance != null ? mp.rayDistance.Value : reach;
                    int m = 0; var ls = new List<string>();
                    if (mp.layerMask != null) foreach (FsmInt l in mp.layerMask) if (l != null) { m |= 1 << l.Value; ls.Add(l.Value.ToString()); }
                    bool inv = mp.invertMask != null && mp.invertMask.Value;
                    if (inv) m = ~m;
                    mask = m == 0 ? -5 : m;
                    sb.Append(", survol ").Append(reach.ToString("F2")).Append(" m, calques [").Append(string.Join(",", ls.ToArray())).Append(']').Append(inv ? " inverses" : "");
                    break;
                }
            Collider pc = pay.GetComponent<Collider>();
            Vector3 target = pc != null ? pc.bounds.center : pay.position, d = target - eye;
            float len = d.magnitude;
            sb.Append(", oeil a ").Append(len.ToString("F2")).Append(" m").Append(len > reach ? " (HORS DE PORTEE)" : "");
            RaycastHit[] hits = Physics.RaycastAll(eye, d / Mathf.Max(len, 1e-4f), len + 0.3f, mask);
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            sb.Append(", touche :");
            if (hits.Length == 0) sb.Append(" rien");
            for (int i = 0; i < hits.Length && i < 8; i++)
            {
                Collider c = hits[i].collider;
                sb.Append(i == 0 ? " " : " | ").Append(c == pc ? "ENVELOPPE" : Recon.Path(c.transform)).Append(" a ").Append(hits[i].distance.ToString("F2"))
                  .Append(" (calque ").Append(c.gameObject.layer).Append(c.isTrigger ? ", declencheur" : "").Append(')');
            }
            return sb.ToString();
        }

        static void TestSepticPay(float t)
        {
            float now = Time.realtimeSinceStartup;
            if (pfStep == 0 && Jobs.OtherInGame(40f)) { pfStep = 1; pfAt = now; }
            if (pfStep == 0) return;
            float k = now - pfAt;
            GameObject wg = Game.FindAny(PfWell), man = Game.FindAny(PfMan);
            PlayMakerFSM lv = wg != null ? Game.FsmOn(wg, "Level") : null;
            FsmFloat level = lv != null ? lv.FsmVariables.FindFsmFloat("ShitLevel") : null;
            if (pfStep == 1)
            {
                pfStep = 2;
                object rht = null;
                var pr = typeof(Physics).GetProperty("raycastsHitTriggers") ?? typeof(Physics).GetProperty("queriesHitTriggers");
                if (pr != null) try { rht = pr.GetValue(null, null); } catch { }
                Log.Info("autotest : paiefosse, debut (" + (Session.IsHost ? "hote" : "invite") + ", maison " + PfHouse + ", rayons sur declencheurs " + (rht ?? "?") + ") : " + PfState(true));
                if (Session.IsHost && lv != null && level != null) { level.Value = 4.95f; lv.SendEvent("ORDERTAKEN"); Log.Info("autotest : paiefosse, commande passee -> " + lv.ActiveStateName); }
                // (comme dans le jeu : la commande se prend loin de la maison, le client n'apparait qu'a l'arrivee -- a l'allumage de la
                // maison, 'Availability' regarde la commande. Joueurs d'essai deja pres de la maison : eloignes a plus de 150 m d'abord.)
                if (man != null) Log.Info("autotest : paiefosse, " + (Session.IsHost ? "hote" : "invite") + " eloigne : " + PfPlace(man.transform.position + new Vector3(Session.IsHost ? -175f : -170f, 0f, 112f)));
            }
            if (pfStep == 2 && k > 5f)
            {
                GameObject lod = Game.FindAny("JOBS/HouseShit" + PfHouse + "/LOD");
                Log.Info("autotest : paiefosse, maison " + (lod == null ? "introuvable" : lod.activeSelf ? "ENCORE ALLUMEE" : "eteinte") + " loin d'elle, " + PfState(false));
                pfStep = 21;
            }
            if (pfStep == 21 && k > 6f)
            {
                pfStep = 3;
                Transform pay = PfPay();
                pfDir = man != null && pay != null ? pay.position - man.transform.position : Vector3.zero;
                pfDir.y = 0f;
                if (pfDir.sqrMagnitude < 0.01f && man != null) { pfDir = man.transform.forward; pfDir.y = 0f; }
                pfDir = pfDir.sqrMagnitude > 1e-4f ? pfDir.normalized : Vector3.forward;
                Vector3 side = new Vector3(pfDir.z, 0f, -pfDir.x);
                if (man == null) Log.Info("autotest : paiefosse, client introuvable : " + PfMan);
                else if (!Session.IsHost) Log.Info("autotest : paiefosse, invite pres de la maison : " + PfPlace(man.transform.position + pfDir * 2.5f));
                else if (Config.GetInt("Test", "PaieFossePres", 1) != 0) Log.Info("autotest : paiefosse, hote pres de la maison : " + PfPlace(man.transform.position + side * 3f));
            }
            if (pfStep >= 3 && pfStep < 9 && k > 5f && k < 46f)
            {
                string s = PfState(false);
                if (s != pfLast && pfLogs < 40) { pfLast = s; pfLogs++; Log.Info("autotest : paiefosse +" + k.ToString("F1") + " s : " + PfState(true)); }
            }
            if (pfStep == 3 && k > 15f)
            {
                pfStep = 4;
                if (Session.IsHost && level != null) { level.Value = 0.3f; Log.Info("autotest : paiefosse, fosse videe ici (" + lv.ActiveStateName + ")"); }
            }
            if (pfStep == 4 && k > 28f)
            {
                pfStep = 5;
                Transform pay = PfPay();
                if (man != null && pay != null)
                {
                    Vector3 side = new Vector3(pfDir.z, 0f, -pfDir.x);
                    if (!Session.IsHost) Log.Info("autotest : paiefosse, invite devant l'enveloppe : " + PfPlace(pay.position + pfDir * 0.6f));
                    else if (Config.GetInt("Test", "PaieFossePres", 1) != 0) Log.Info("autotest : paiefosse, hote sur le cote : " + PfPlace(man.transform.position + side * 2.5f));
                }
            }
            if (pfStep == 5 && k > 30f)
            {
                pfStep = 6;
                Transform pay = PfPay();
                if (pay != null)
                {
                    Vector3 eye = pay.position + pfDir * 0.6f; eye.y = pay.position.y;
                    RaycastHit g; if (Physics.Raycast(eye + Vector3.up * 2f, Vector3.down, out g, 6f, ~(1 << 2))) eye.y = g.point.y;
                    eye.y += 1.6f;
                    Log.Info("autotest : paiefosse, sonde " + PfRay(eye, "oeil virtuel " + (Session.IsHost ? "hote" : "invite")));
                    Camera cam = Game.MainCam;
                    if (!Session.IsHost && cam != null) Log.Info("autotest : paiefosse, sonde " + PfRay(cam.transform.position, "camera de l'invite " + cam.name));
                    // (photos : de l'oeil virtuel vers l'enveloppe, et de cote ; dumps/tuyau-<hote|invite>-paiefosse-*.png)
                    Collider pc = pay.GetComponent<Collider>();
                    Vector3 aim = pc != null ? pc.bounds.center : pay.position;
                    SewageHose.Shot("-paiefosse-oeil", eye, aim);
                    if (man != null) SewageHose.Shot("-paiefosse-cote", man.transform.position + new Vector3(pfDir.z, 0f, -pfDir.x) * 2.5f + pfDir * 1.5f + Vector3.up * 1.8f, man.transform.position + Vector3.up * 0.8f);
                }
                GameObject chair = Game.FindAny("JOBS/HouseShit" + PfHouse + "/LOD/ShitNPC/ChairPlastic/CrouchTriggerPlus");
                Collider cc = chair != null ? chair.GetComponent<Collider>() : null;
                Log.Info("autotest : paiefosse, siege ajoute sur la chaise du client : " + (chair == null ? "aucun" : (chair.activeInHierarchy ? "allume" : "eteint") + ", calque " + chair.layer + (cc != null ? ", " + cc.GetType().Name + (cc.isTrigger ? " declencheur" : "") + " " + cc.bounds.size.ToString("F2") : "")));
            }
            if (pfStep == 6 && k > 33f)
            {
                pfStep = 7;
                if (!Session.IsHost)
                {
                    Transform pay = PfPay();
                    PlayMakerFSM use = pay != null ? Game.FsmOn(pay.gameObject, "Use") : null;
                    FsmState wb = use != null ? use.Fsm.GetState("Wait button") : null;
                    if (use == null || !pay.gameObject.activeInHierarchy || wb == null) Log.Info("autotest : paiefosse, l'invite ne peut pas prendre : enveloppe " + (pay == null ? "introuvable" : pay.gameObject.activeInHierarchy ? "sans 'Wait button'" : "rangee"));
                    else
                    {
                        string before = use.ActiveStateName, m0 = PfCash();
                        var off = new List<FsmStateAction>();
                        foreach (FsmStateAction a in wb.Actions) if (a != null && a.Enabled && a.GetType().Name == "MousePickEvent") { a.Enabled = false; off.Add(a); }
                        try { Game.SetState(use, "Wait button"); use.SendEvent("USE"); }
                        finally { foreach (FsmStateAction a in off) a.Enabled = true; }
                        Log.Info("autotest : paiefosse, l'invite prend l'enveloppe comme un clic (" + before + " -> " + use.ActiveStateName + "), liquide " + m0 + " -> " + PfCash());
                    }
                }
            }
            if (pfStep == 7 && k > 36f)
            {
                pfStep = 8;
                if (man != null) SewageHose.Shot("-paiefosse-apres", man.transform.position + new Vector3(pfDir.z, 0f, -pfDir.x) * 2.5f + pfDir * 1.5f + Vector3.up * 1.8f, man.transform.position + Vector3.up * 0.8f);
            }
            if (pfStep == 8 && k > 40f) { pfStep = 9; Log.Info("autotest : paiefosse, fin (" + (Session.IsHost ? "hote" : "invite") + ") : liquide " + PfCash() + ", " + PfState(true)); }
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
            if (mode == "paiefosse") { TestSepticPay(t); return; }
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
