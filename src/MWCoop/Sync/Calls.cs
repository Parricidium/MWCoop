using System.Collections.Generic;
using System.Reflection;
using HutongGames.PlayMaker;
using MWCoop.Net;
using UnityEngine;

namespace MWCoop
{
    // Appels sortants et commandes de pieces par courrier.
    //  - Telephones (fixe de l'appartement, fixe des parents, telephone du taxi : automate 'Calling' du cadran) :
    //    composer et l'appel lui-meme (sonnerie, voix, sous-titres) restent a celui qui appelle. Ce qui en
    //    decoule est rejoue chez les autres, une fois :
    //     * numero d'une offre (pubs 08-231206, taxi 08-712112, Reijo 08-609553 : CARPARTS/PARTSYSTEM/PhoneNumbers,
    //       automate Data) : l'offre est "appelee" chez tous (renommee numberdisabled, etape sauvegardee : plus
    //       rappelable, et sauvee comme telle par l'hote). Le boulot lui-meme (JOB / SELLCAR vers JOBS) passe par
    //       Jobs, une seule fois : coupe ici pendant le rejeu ;
    //     * petite annonce de pieces : la commande (plus bas) ;
    //     * facture : communications et minutes de l'appel ajoutees chez l'hote (Systems/PhoneBills* : son miroir
    //       les rend a tous, au lieu d'effacer celles d'un invite) ; minutes du taxi (JOBS) : chez tous.
    //  - Commandes (OrdersSpawnerYP : petites annonces ; OrdersSpawnerAMIS : catalogue poste) : creees chez celui
    //    qui commande, rejouees chez les autres avec le meme numero (OrderYP7 : c'est aussi la cle du colis pour
    //    Props) et la MEME liste de pieces (celle de l'annonce ou du bon de commande de l'expediteur : le contenu
    //    du colis est donc le meme pour tous). L'annonce est retrouvee par son nom d'origine (ListRand03 : variable
    //    Name de Generate), pas par son numero de telephone, tire au hasard chez chacun. Catalogue : rejoue par la
    //    boite aux lettres (INBOX) -- l'enveloppe disparait aussi chez les autres, pas de 2e envoi possible.
    //    Numeros entrelaces entre joueurs (SendOrder) ; le createur ne doit jamais depasser la limite de boucle de
    //    PlayMaker au chargement (Arm) -- fil « Magazine ordering issue », 26-27/10.
    //  - Boite aux lettres de la station (Post Box/OrderTrigger) : l'enveloppe du catalogue qu'un AUTRE joueur a
    //    deplacee en dernier (sa copie suit ses messages) n'est jamais postee ici ; seul celui qui la lache la poste.
    //    La lettre de Kela (envelope(kela1)) n'est pas concernee : chaque joueur a la sienne (Expenses local), non
    //    synchronisee, et la poste lui-meme.
    //  Le paiement au guichet (PAYMENT) et l'attente des commandes (miroir de l'hote) : WorldFsms.
    //  - Appels ENTRANTS (PhoneLogicNEW de l'appartement, PhoneLogicOLD de la maison des parents : 'Ring' tire qui
    //    appelle et quand, 'Jokes' les farces) : logique de l'hote, suivie etat par etat par les invites (WorldFsms,
    //    copie arretee chez eux). Si elle ne tourne pas chez l'hote (objet Logic eteint, automate desactive) alors
    //    qu'un invite est pres du telephone, l'invite n'avait plus aucun appel (boulots de fosse septique, de bois...).
    //     * chez l'hote, l'automate desactive alors que son objet est allume est rallume (il tourne quelle que soit la
    //       distance), et l'etat de chaque ligne (tourne ou non) est envoye aux invites (quand il change, et toutes les
    //       10 s) ; logique eteinte chez lui avec un invite a moins de 20 m du telephone : note au journal ;
    //     * un invite a moins de 25 m d'un telephone dont la logique ne tourne pas chez l'hote la fait tourner chez lui
    //       (RunsHere : WorldFsms ne l'arrete plus et ignore les etats de l'hote pour elle), jusqu'a ce que l'hote la
    //       fasse de nouveau tourner ou qu'il s'eloigne (60 m). Decrocher passe ensuite par le monde comme d'habitude
    //       (Ringing* : rejoue sans voix chez les autres).
    public static class Calls
    {
        // Seule enveloppe commune (cle fixe dans Props) : celle du catalogue. Nom compare tel quel par la boite
        // aux lettres (OrderTrigger : Letter1).
        public const string PartsEnvelope = "envelope(parts)";
        const int K_Bill = 0, K_Order = 1, K_Job = 2, K_Phone = 3, K_Week = 4;
        const int H_Find = 0, H_Hangup = 1, H_Hangup2 = 2, H_Create = 3, H_CheckHand = 4;
        const int MaxMsg = 1100, NoList = 255;
        static readonly string[] LinePaths =
        {
            "HOMENEW/Functions/FunctionsDisable/Telephone/Phone/KeypadPhone2",
            "YARD/Building/LIVINGROOM/Telephone 1/Phone/KeypadPhone1",
            "JOBS/TAXIJOB/MACHTWAGEN/TaxiFunctions/Carphone/KeypadPhone2",
        };
        static readonly string[] LineNames = { "appartement", "parents", "taxi" };
        static readonly string[] SpawnerPaths = { "CARPARTS/PARTSYSTEM/OrdersSpawnerYP", "CARPARTS/PARTSYSTEM/OrdersSpawnerAMIS" };
        static readonly string[] SpawnerLists = { "Save", "Order" };   // liste copiee dans la commande : annonce / bon de commande
        const string NumbersPath = "CARPARTS/PARTSYSTEM/PhoneNumbers", AmisListPath = "CARPARTS/PARTSYSTEM/PostSystem/AMISOrderList",
                     PostBoxPath = "PERAPORTTI/ActiveFunctions/Post Box/OrderTrigger";

        class Bill { public string Path, Fsm, Var; public FsmFloat V; public float Start; }
        class Line { public string Name; public PlayMakerFSM F; public bool Hooked, InCall; public List<Bill> Bills; }

        class Hook : ModHook
        {
            public override string Module { get { return "appels"; } }
            public int Kind, Index; public PlayMakerFSM F;
            public override void OnEnter()
            {
                try { if (Replay.Depth == 0) OnHook(this); } catch (System.Exception e) { Replay.HookError(e); }
                Finish();
            }
        }

        static readonly Line[] lines = new Line[3];
        static readonly PlayMakerFSM[] spawners = new PlayMakerFSM[2];
        static readonly bool[] spawnerHooked = new bool[2];
        static PlayMakerFSM amisList, postBox;
        static bool postHooked, resolved;
        static Transform numbers;
        static float resolveAt = -1, nextCheck, nextPostLog, orderCheckAt = -1;
        static bool orderSeen;
        static int orderNum0;
        static GameObject orderListing;
        static readonly Dictionary<string, FsmFloat> billTargets = new Dictionary<string, FsmFloat>();

        public static void OnLevelLoaded()
        {
            for (int i = 0; i < lines.Length; i++) lines[i] = null; ringCapped = false; ringNext = 0f; orderCheckAt = -1;
            spawners[0] = spawners[1] = null;
            spawnerHooked[0] = spawnerHooked[1] = false;
            amisList = postBox = null; numbers = null;
            postHooked = resolved = false;
            billTargets.Clear();
            step = step2 = 0; testCall = null; testKeypad = null; testLogged = false;
            for (int i = 0; i < phones.Length; i++) phones[i] = null;
            nextPhone = 0; nextPhoneSend = 0; phoneSig = ""; farStep = 0; farLog = 0;
            weekSent.Clear(); nextWeekCheck = 0; weekStep = 0;
            // Avant le releve du monde (16 s apres le chargement) : les createurs de commandes sont a nous.
            resolveAt = PlayerSync.InGame ? Time.realtimeSinceStartup + 3f : -1;
            // Avant la premiere image (les automates demarrent a leur Start) : limite de boucle relevee, voir Arm.
            if (Config.GetInt("Test", "CommandeSansLimite", 0) == 0)   // ([Test] CommandeSansLimite=1 : comme avant le 27/10, le createur doit mourir au chargement)
                foreach (string p in SpawnerPaths) { try { GameObject g = Game.FindAny(p); if (g != null) Arm(Game.FsmOn(g, "Spawn")); } catch (System.Exception e) { Log.Warn("appels : limite de boucle de " + p + " : " + e.Message); } }
        }

        // Limite de PlayMaker (un automate qui entre plus de 1000 fois dans le meme etat en une image est arrete, « Loop count
        // exceeded maximum » dans output_log.txt) relevee pour les createurs de commandes : au chargement, 'Load ID' -> 'Add ID'
        // reprend les commandes sauvegardees en bouclant de 1 au compteur, et le compteur depassait 1000 des la premiere commande
        // d'un invite (tranches de 1000 par joueur jusqu'au 27/10) : createur mort chez tous a la partie suivante -- annonce
        // appelee, aucune commande, et les commandes au-dela de 1000 jamais reprises (Donskoy, 26-27/10). Les parties deja
        // passees a 1000 en ont besoin pour toujours.
        const int LoopLimit = 1000000;
        static void Arm(PlayMakerFSM sp)
        {
            if (sp == null || sp.Fsm == null) return;
            if (sp.Fsm.MaxLoopCountOverride < LoopLimit) sp.Fsm.MaxLoopCountOverride = LoopLimit;
        }

        // Createur mort (arrete et fini : boucle du chargement au-dela de la limite, version d'avant le 27/10 ou limite relevee
        // trop tard) : rallume -- il repart de 'Load ID' (RestartOnEnable), reprend les commandes sauvegardees et revient a 'Idle'
        // dans la meme image. Vrai : il l'etait.
        static bool Revive(PlayMakerFSM sp, string why)
        {
            if (sp == null || (sp.enabled && !sp.Fsm.Finished)) return false;
            Arm(sp);
            FsmInt num = sp.FsmVariables.FindFsmInt("ObjectNumberInt");
            sp.enabled = true;
            Log.Warn("appels : createur " + sp.gameObject.name + " arrete (" + why + ") : rallume -> etat '" + sp.ActiveStateName + "', numero " + (num != null ? num.Value : -1));
            return true;
        }

        public static void Update()
        {
            if (resolveAt < 0 || !Session.Active) return;
            float now = Time.realtimeSinceStartup;
            if (!resolved) { if (now < resolveAt) return; Resolve(); }
            if (now >= nextCheck) { nextCheck = now + 1f; Inject(); }
            if (now >= nextPhone) { nextPhone = now + 1f; PhoneStep(now); }
            if (orderCheckAt > 0 && now >= orderCheckAt) { orderCheckAt = -1; if (!orderSeen) NoOrder(); }
            WeekStep(now);
            for (int i = 0; i < lines.Length; i++)
            {
                Line l = lines[i];
                if (l == null || !l.InCall) continue;
                // Combine repose en plein appel (cadran desactive, automate revenu au debut) : ce qui a ete parle compte.
                if (l.F == null || !l.F.gameObject.activeInHierarchy || l.F.ActiveStateName == "State 1") EndCall(l, "raccroche");
            }
        }

        // Objets du jeu, cherches une fois (un seul releve des racines de la scene).
        static void Resolve()
        {
            resolved = true;
            var sb = new System.Text.StringBuilder("appels :");
            for (int i = 0; i < lines.Length; i++)
            {
                GameObject g = Game.FindAny(LinePaths[i]);
                PlayMakerFSM f = g != null ? Game.FsmOn(g, "Calling") : null;
                lines[i] = new Line { Name = LineNames[i], F = f, Hooked = !Own(f) };
                sb.Append(' ').Append(LineNames[i]).Append(f != null ? " ok" : " absent");
            }
            for (int s = 0; s < 2; s++)
            {
                GameObject g = Game.FindAny(SpawnerPaths[s]);
                spawners[s] = g != null ? Game.FsmOn(g, "Spawn") : null;
                spawnerHooked[s] = !Own(spawners[s]);
                if (spawners[s] != null) { Arm(spawners[s]); Revive(spawners[s], "au chargement"); }
            }
            // ([Test] CommandeNumero=N : compteur des commandes d'annonces monte a N -- la prochaine commande, sauvegardee
            // (AppelSauve=1), fait boucler le createur N fois au chargement suivant)
            int forced = Config.GetInt("Test", "CommandeNumero", 0);
            if (forced > 0 && spawners[0] != null)
            {
                FsmInt fn = spawners[0].FsmVariables.FindFsmInt("ObjectNumberInt");
                if (fn != null && fn.Value < forced) { fn.Value = forced; Log.Info("autotest : compteur des commandes d'annonces force a " + forced); }
            }
            GameObject al = Game.FindAny(AmisListPath);
            amisList = al != null ? Game.FsmOn(al, "Data") : null;
            GameObject nb = Game.FindAny(NumbersPath);
            numbers = nb != null ? nb.transform : null;
            GameObject pb = Game.FindAny(PostBoxPath);
            postBox = pb != null ? Game.FsmOn(pb, "Open") : null;
            postHooked = !Own(postBox);
            sb.Append(", commandes ").Append(spawners[0] != null ? "YP" : "-").Append('/').Append(spawners[1] != null ? "AMIS" : "-")
              .Append(" (numeros ").Append(Counter(spawners[0])).Append('/').Append(Counter(spawners[1])).Append(')')
              .Append(", catalogue ").Append(amisList != null).Append(", annonces ").Append(numbers != null ? numbers.childCount : 0)
              .Append(", boite aux lettres ").Append(postBox != null);
            Log.Info(sb.ToString());
        }

        static int Counter(PlayMakerFSM sp)
        {
            FsmInt n = sp != null ? sp.FsmVariables.FindFsmInt("ObjectNumberInt") : null;
            return n != null ? n.Value : -1;
        }

        // Vrai : l'automate est a nous (a accrocher) ; faux : absent ou deja pris par un autre module.
        static bool Own(PlayMakerFSM f)
        {
            if (f == null) return false;
            if (Replay.Claim(f, "appels")) return true;
            Log.Warn("appels : " + Recon.Path(f.transform) + "::" + f.FsmName + " deja pris par " + Replay.Owner(f));
            return false;
        }

        // Chaque seconde : crochets poses des que l'automate a demarre (cadran actif une fois le combine decroche ;
        // les actions d'un automate jamais actif ne se lisent pas).
        static void Inject()
        {
            for (int i = 0; i < lines.Length; i++)
            {
                Line l = lines[i];
                if (l == null || l.Hooked || !Ready(l.F)) continue;
                l.Hooked = true;
                Add(l.F, "Find number", H_Find, i);
                Add(l.F, "Hangup", H_Hangup, i);
                Add(l.F, "Hangup 2", H_Hangup2, i);
                Log.Info("appels : cadran " + l.Name + " suivi");
            }
            for (int s = 0; s < 2; s++)
            {
                if (spawnerHooked[s] || !Ready(spawners[s])) continue;
                spawnerHooked[s] = true;
                Add(spawners[s], "Create product", H_Create, s);
                Log.Info("appels : commandes " + (s == 0 ? "des annonces" : "du catalogue") + " suivies");
            }
            if (!postHooked && Ready(postBox))
            {
                postHooked = true;
                Add(postBox, "Check hand", H_CheckHand, 0);
                Log.Info("appels : boite aux lettres suivie");
            }
        }

        static bool Ready(PlayMakerFSM f)
        {
            if (f == null || !f.gameObject.activeInHierarchy) return false;
            foreach (FsmState s in f.Fsm.States) if (!s.IsInitialized) return false;
            return true;
        }

        static void Add(PlayMakerFSM f, string state, int kind, int index)
        {
            FsmState st = f.Fsm.GetState(state);
            if (st == null) { Log.Warn("appels : etat '" + state + "' absent de " + Recon.Path(f.transform) + "::" + f.FsmName); return; }
            FsmHooks.Prepend(st, new Hook { Kind = kind, Index = index, F = f });
        }

        static void OnHook(Hook h)
        {
            switch (h.Kind)
            {
                case H_Find: StartCall(lines[h.Index]); break;
                case H_Hangup: EndCall(lines[h.Index], "annonce"); ExpectOrder(h.F); break;
                case H_Hangup2: SendJob(lines[h.Index]); EndCall(lines[h.Index], "offre"); break;
                case H_Create: if (h.Index == 0) orderSeen = true; SendOrder(h.Index, h.F); break;
                case H_CheckHand: CheckEnvelope(h.F); break;
            }
        }

        // Annonce appelee jusqu'au bout ('Hangup' : CurrentListing pose, SPAWNITEM au createur, puis 'Create product') sans
        // commande creee dans la seconde -- fil « Magazine ordering issue » (26/10, 1.15.8) : l'hote en solo comme l'invite
        // parlaient au vendeur, aucune commande ; pas reproduit avec la sauvegarde de l'hote. Ce qui pouvait l'empecher va au
        // journal, et la creation est relancee une fois si le compteur du createur n'a pas bouge (pas de doublon possible).
        static void ExpectOrder(PlayMakerFSM call)
        {
            PlayMakerFSM sp = spawners[0];
            FsmInt num = sp != null ? sp.FsmVariables.FindFsmInt("ObjectNumberInt") : null;
            FsmGameObject fl = call != null ? call.FsmVariables.FindFsmGameObject("FoundListing") : null;
            orderNum0 = num != null ? num.Value : -1;
            orderListing = fl != null ? fl.Value : null;
            orderSeen = false;
            orderCheckAt = Time.realtimeSinceStartup + 1f;
        }

        static void NoOrder()
        {
            PlayMakerFSM sp = spawners[0];
            if (sp == null) { Log.Warn("appels : annonce appelee mais pas de commande creee -- createur absent"); return; }
            FsmInt num = sp.FsmVariables.FindFsmInt("ObjectNumberInt");
            FsmGameObject cl = sp.FsmVariables.FindFsmGameObject("CurrentListing");
            string why = "etat '" + sp.ActiveStateName + "', actif " + sp.gameObject.activeInHierarchy + ", automate " + (sp.enabled ? "en marche" : "arrete")
                         + (sp.Fsm.Finished ? " (fini)" : "") + ", numero " + (num != null ? num.Value : -1) + " (avant " + orderNum0 + "), annonce "
                         + (cl != null && cl.Value != null ? cl.Value.name : "aucune") + " / appelee " + (orderListing != null ? orderListing.name : "aucune");
            if (num != null && num.Value != orderNum0) { Log.Warn("appels : commande creee sans passer par le crochet (pas envoyee aux autres) -- " + why); return; }
            if (orderListing == null || !sp.gameObject.activeInHierarchy) { Log.Warn("appels : annonce appelee mais pas de commande creee -- " + why); return; }
            Log.Warn("appels : annonce appelee mais pas de commande creee -- " + why + " ; creation relancee");
            Revive(sp, "annonce appelee");
            if (cl != null) cl.Value = orderListing;
            sp.SendEvent("SPAWNITEM");
            Log.Info("appels : relance -> etat '" + sp.ActiveStateName + "', numero " + (num != null ? num.Value : -1) + (orderSeen ? ", commande envoyee" : ", toujours rien"));
        }

        // ---------------------------------------------------------------- appel en cours, facture
        // Debut d'un appel (numero compose) : compteurs de la facture notes. Ce que l'appel y ajoute est envoye a
        // la fin (raccroche par l'autre, ou combine repose).
        static void StartCall(Line l)
        {
            if (l == null) return;
            if (l.Bills == null) l.Bills = FindBills(l);
            for (int i = 0; i < l.Bills.Count; i++) l.Bills[i].Start = l.Bills[i].V.Value;
            l.InCall = true;
        }

        // Compteurs que l'appel augmente : cibles des actions AddFsmFloat de l'automate (PhoneBills1/2 :
        // Connects, Minutes, ConnectsLong, MinutesLong ; taxi : Payments CallMinutes).
        static List<Bill> FindBills(Line l)
        {
            var r = new List<Bill>();
            var sb = new System.Text.StringBuilder();
            foreach (FsmState st in l.F.Fsm.States)
                foreach (FsmStateAction a in st.Actions)
                {
                    if (a == null || a.GetType().Name != "AddFsmFloat") continue;
                    var od = Field(a, "gameObject") as FsmOwnerDefault;
                    var fsm = Field(a, "fsmName") as FsmString;
                    var name = Field(a, "variableName") as FsmString;
                    if (od == null || fsm == null || name == null) continue;
                    GameObject go = od.OwnerOption == OwnerDefaultOption.UseOwner ? l.F.gameObject : od.GameObject.Value;
                    PlayMakerFSM target = go != null ? Game.FsmOn(go, fsm.Value) : null;
                    FsmFloat v = target != null ? target.FsmVariables.FindFsmFloat(name.Value) : null;
                    if (v == null) continue;
                    bool dup = false;
                    foreach (Bill b in r) if (b.V == v) dup = true;
                    if (dup) continue;
                    r.Add(new Bill { Path = Recon.Path(go.transform), Fsm = fsm.Value, Var = name.Value, V = v });
                    sb.Append(' ').Append(go.name).Append('.').Append(name.Value);
                }
            Log.Info("appels : facture du telephone " + l.Name + " :" + (sb.Length > 0 ? sb.ToString() : " rien"));
            return r;
        }

        static object Field(object o, string name)
        {
            FieldInfo fi = o.GetType().GetField(name, BindingFlags.Public | BindingFlags.Instance);
            return fi != null ? fi.GetValue(o) : null;
        }

        static void EndCall(Line l, string why)
        {
            if (l == null || !l.InCall) return;
            l.InCall = false;
            int n = 0;
            foreach (Bill b in l.Bills) if (Mathf.Abs(b.V.Value - b.Start) > 1e-4f) n++;
            if (n == 0) return;
            var w = new NetWriter(Msg.Call).U8(Session.LocalId).U8(K_Bill).U8(n);
            var sb = new System.Text.StringBuilder();
            foreach (Bill b in l.Bills)
            {
                float d = b.V.Value - b.Start;
                if (Mathf.Abs(d) <= 1e-4f) continue;
                w.Str(b.Path).Str(b.Fsm).Str(b.Var).F32(d);
                sb.Append(' ').Append(b.Var).Append(" +").Append(d.ToString("F2"));
            }
            Session.SendAll(w, true);
            Log.Info("appels : telephone " + l.Name + " (" + why + "), facture" + sb);
        }

        static void OnBill(int who, NetReader r)
        {
            int n = r.U8();
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < n; i++)
            {
                string path = r.Str(), fsm = r.Str(), name = r.Str();
                float d = r.F32();
                // Facture recopiee de l'hote (Systems/PhoneBills*) : seul l'hote l'ajoute, son miroir l'apporte aux
                // invites (qui recoivent deja ses propres appels par ce miroir : pas deux fois).
                int slash = path.IndexOf('/');
                if (!Session.IsHost && WorldFsms.IsMirrorRoot(slash > 0 ? path.Substring(0, slash) : path)) { sb.Append(' ').Append(name).Append(" (par l'hote)"); continue; }
                FsmFloat v = BillTarget(path, fsm, name);
                if (v == null) { sb.Append(' ').Append(name).Append(" introuvable"); continue; }
                v.Value += d;
                sb.Append(' ').Append(name).Append(" +").Append(d.ToString("F2")).Append(" = ").Append(v.Value.ToString("F2"));
            }
            Log.Info("appels de #" + who + " : facture" + sb);
        }

        static FsmFloat BillTarget(string path, string fsm, string name)
        {
            string k = path + "|" + fsm + "|" + name;
            FsmFloat v;
            if (billTargets.TryGetValue(k, out v)) return v;
            GameObject go = Game.FindAny(path);
            PlayMakerFSM f = go != null ? Game.FsmOn(go, fsm) : null;
            v = f != null ? f.FsmVariables.FindFsmFloat(name) : null;
            if (v != null) billTargets[k] = v;
            return v;
        }

        // ---------------------------------------------------------------- offres d'emploi (numeros fixes)
        // Fin de l'appel d'une offre ('Hangup 2', avant que le jeu n'envoie CALLED a l'offre) : rejouee chez les autres.
        static void SendJob(Line l)
        {
            FsmGameObject fl = l != null ? l.F.FsmVariables.FindFsmGameObject("FoundListing") : null;
            GameObject found = fl != null ? fl.Value : null;
            if (found == null) return;
            string key = ListingKey(found);
            Session.SendAll(new NetWriter(Msg.Call).U8(Session.LocalId).U8(K_Job).Str(key), true);
            Log.Info("appels : telephone " + l.Name + ", offre " + key + " appelee : rejouee chez les autres");
        }

        static void OnJob(int who, NetReader r)
        {
            string key = r.Str();
            Transform t = FindListing(key);
            PlayMakerFSM data = t != null ? Game.FsmOn(t.gameObject, "Data") : null;
            if (data == null) { Log.Warn("appels de #" + who + " : offre " + key + " introuvable ici"); return; }
            if (data.ActiveStateName == "State 1" || t.name == "numberdisabled") { Log.Info("appels de #" + who + " : offre " + key + " deja appelee ici"); return; }
            // L'offre passe a "appelee" (nom, etape sauvee) ; l'evenement qu'elle envoie a JOBS (JOB, SELLCAR) est
            // coupe : Jobs rejoue deja cette transition du boulot, une seule fois.
            var muted = new List<FsmStateAction>();
            FsmState st = data.Fsm.GetState("State 1");
            if (st != null && st.IsInitialized)
                foreach (FsmStateAction a in st.Actions)
                    if (a != null && a.Enabled && a.GetType().Name.StartsWith("SendEvent")) { a.Enabled = false; muted.Add(a); }
            Replay.Depth++;
            try { data.SendEvent("CALLED"); }
            finally { Replay.Depth--; foreach (FsmStateAction a in muted) a.Enabled = true; }
            FsmInt stage = data.FsmVariables.FindFsmInt("Stage");
            Log.Info("appels de #" + who + " : offre " + key + " appelee ici aussi (" + t.name + ", etape " + (stage != null ? stage.Value.ToString() : "?")
                     + ", etat " + data.ActiveStateName + ") ; le boulot suit par les quetes");
        }

        // Nom d'origine d'une annonce (avant que le jeu ne la renomme au numero tire chez lui) : variable Name de
        // Generate (petites annonces), Number de Data (offres) ; a defaut son nom. Puis son rang sous PhoneNumbers.
        static string ListingKey(GameObject go)
        {
            string n = StrVar(go, "Generate", "Name");
            if (n.Length == 0) n = StrVar(go, "Data", "Number");
            if (n.Length == 0) n = go.name;
            return n + "|" + go.transform.GetSiblingIndex();
        }

        static Transform FindListing(string key)
        {
            if (numbers == null) return null;
            int bar = key.LastIndexOf('|'), idx = -1;
            string name = bar >= 0 ? key.Substring(0, bar) : key;
            if (bar >= 0) int.TryParse(key.Substring(bar + 1), out idx);
            for (int i = 0; i < numbers.childCount; i++)
            {
                Transform c = numbers.GetChild(i);
                string k = StrVar(c.gameObject, "Generate", "Name");
                if (k.Length == 0) k = StrVar(c.gameObject, "Data", "Number");
                if (k.Length == 0) k = c.name;
                if (k == name) return c;
            }
            if (idx < 0 || idx >= numbers.childCount) return null;
            Log.Warn("appels : annonce " + name + " introuvable par son nom, prise a son rang " + idx);
            return numbers.GetChild(idx);
        }

        static string StrVar(GameObject go, string fsm, string name)
        {
            PlayMakerFSM f = Game.FsmOn(go, fsm);
            FsmString s = f != null ? f.FsmVariables.FindFsmString(name) : null;
            return s != null && s.Value != null ? s.Value : "";
        }

        // ---------------------------------------------------------------- commandes
        // Commande creee ici ('Create product' du createur, avant la creation) : numero a venir, annonce, liste.
        // Numero (ObjectNumberInt + 1 : nom de la commande, donc du colis chez Props) : chacun cree la sienne tout de suite, sans
        // passer par l'hote (lui donner le numero aurait retarde la creation du jeu) ; deux commandes croisees a moins d'une
        // latence prenaient le meme numero chez les deux, colis en collision (relecture du 15/10). Les numeros sont donc
        // entrelaces : le joueur n prend le premier numero >= compteur dont le reste modulo 16 (joueurs au plus) vaut n. Jusqu'au
        // 27/10 c'etait une tranche de 1000 par joueur (rang + 1000 x n) : le compteur passait 1000 des le premier colis d'un
        // invite et le createur mourait au chargement suivant (voir Arm). Une commande recue laisse le compteur a son numero
        // (meme nom) puis le remonte s'il etait plus haut : un numero d'ici ne doit jamais resservir.
        const int NumberSlots = 16;
        static int OwnNumber(int from)
        {
            int v = from < 0 ? 0 : from;
            while ((v + 1) % NumberSlots != Session.LocalId % NumberSlots) v++;
            return v;
        }

        static void SendOrder(int s, PlayMakerFSM f)
        {
            FsmGameObject cl = f.FsmVariables.FindFsmGameObject("CurrentListing");
            GameObject listing = cl != null ? cl.Value : null;
            FsmInt num = f.FsmVariables.FindFsmInt("ObjectNumberInt");
            if (num != null)
            {
                int own = OwnNumber(num.Value);
                if (own != num.Value) { Log.Info("appels : commande n" + (num.Value + 1) + " prise au numero " + (own + 1) + " (joueur #" + Session.LocalId + ")"); num.Value = own; }
            }
            int n = num != null ? num.Value : -1;
            string key = s == 0 && listing != null ? ListingKey(listing) : "";
            System.Collections.ArrayList items = listing != null ? ListOf(listing, SpawnerLists[s]) : null;
            var w = new NetWriter(Msg.Call).U8(Session.LocalId).U8(K_Order).U8(s).I32(n).Str(key);
            WriteList(w, items);
            if (w.Length > MaxMsg) { Log.Warn("appels : commande trop grosse (" + w.Length + " o), pas envoyee"); return; }
            Session.SendAll(w, true);
            Log.Info("appels : commande " + (s == 0 ? "de l'annonce " + key : "du catalogue") + " n" + (n + 1) + " (" + (items != null ? items.Count : 0)
                     + " lignes : " + Summary(items) + ") envoyee");
            WorldFsms.SoonScan();   // l'automate de la commande (attente, paiement) suivi au plus vite
        }

        static void OnOrder(int who, NetReader r)
        {
            int s = r.U8(), n = r.I32();
            string key = r.Str();
            List<object> items = ReadList(r);
            PlayMakerFSM sp = s < 2 ? spawners[s] : null;
            if (sp == null) { Log.Warn("appels de #" + who + " : createur de commandes " + s + " absent ici"); return; }
            // Meme numero que chez l'expediteur : meme nom de commande, donc de colis (Props). Le compteur d'ici, s'il etait plus
            // haut, est remis ensuite (voir SendOrder). Createur mort : rallume d'abord, sinon SPAWNITEM se perd.
            Revive(sp, "commande de #" + who);
            FsmInt num = sp.FsmVariables.FindFsmInt("ObjectNumberInt");
            int keep = num != null ? num.Value : -1, after = -1;
            if (num != null && n >= 0) num.Value = n;
            string what;
            Replay.Depth++;
            try
            {
                if (s == 0)
                {
                    Transform t = FindListing(key);
                    if (t == null) { Log.Warn("appels de #" + who + " : annonce " + key + " introuvable ici, commande perdue"); return; }
                    SetList(t.gameObject, SpawnerLists[0], items);
                    FsmGameObject cl = sp.FsmVariables.FindFsmGameObject("CurrentListing");
                    if (cl != null) cl.Value = t.gameObject;
                    sp.SendEvent("SPAWNITEM");
                    what = "annonce " + key;
                }
                else
                {
                    // Le catalogue rejoue tout : enveloppe rangee, commande creee (son createur), bon remis a zero.
                    if (amisList == null) { Log.Warn("appels de #" + who + " : bon de commande absent ici"); return; }
                    SetList(amisList.gameObject, SpawnerLists[1], items);
                    amisList.SendEvent("INBOX");
                    what = "catalogue";
                }
            }
            finally
            {
                Replay.Depth--;
                after = num != null ? num.Value : -1;
                if (num != null && after < keep) { Log.Info("appels de #" + who + " : compteur remonte de " + after + " a " + keep); num.Value = keep; }
            }
            WorldFsms.SoonScan();
            // (annonce : 'Create product' a tourne dans la meme image, le compteur a avance d'un ; catalogue : la chaine INBOX
            // du bon de commande passe par des attentes, pas verifiee ici)
            bool made = s != 0 || num == null || n < 0 || after == n + 1;
            if (!made) Log.Warn("appels de #" + who + " : commande " + what + " n" + (n + 1) + " PAS creee ici (createur " + sp.ActiveStateName + ", " + (sp.enabled ? "en marche" : "arrete") + ", numero " + after + ")");
            else Log.Info("appels de #" + who + " : commande " + what + " n" + (n + 1) + " creee ici aussi (" + (items != null ? items.Count : 0) + " lignes : " + Summary(items) + ")");
        }

        static System.Collections.ArrayList ListOf(GameObject go, string reference)
        {
            foreach (PlayMakerArrayListProxy p in go.GetComponents<PlayMakerArrayListProxy>())
                if ((p.referenceName ?? "") == reference) return p._arrayList;
            return null;
        }

        static void SetList(GameObject go, string reference, List<object> items)
        {
            if (items == null) return;   // liste absente chez l'expediteur : celle d'ici reste
            System.Collections.ArrayList a = ListOf(go, reference);
            if (a == null) { Log.Warn("appels : liste " + reference + " absente de " + go.name); return; }
            a.Clear();
            foreach (object o in items) a.Add(o);
        }

        static void WriteList(NetWriter w, System.Collections.ArrayList a)
        {
            if (a == null) { w.U8(NoList); return; }
            int n = Mathf.Min(a.Count, 120);
            w.U8(n);
            for (int i = 0; i < n; i++)
            {
                object o = a[i];
                if (o is int) w.U8(0).I32((int)o);
                else if (o is float) w.U8(1).F32((float)o);
                else if (o is string) w.U8(2).Str((string)o);
                else if (o is bool) w.U8(3).Bool((bool)o);
                else w.U8(5);
            }
        }

        static List<object> ReadList(NetReader r)
        {
            int n = r.U8();
            if (n == NoList) return null;
            var l = new List<object>(n);
            for (int i = 0; i < n; i++)
            {
                int t = r.U8();
                if (t == 0) l.Add(r.I32());
                else if (t == 1) l.Add(r.F32());
                else if (t == 2) l.Add(r.Str());
                else if (t == 3) l.Add(r.Bool());
                else l.Add(null);
            }
            return l;
        }

        static string Summary(System.Collections.IEnumerable items)
        {
            if (items == null) return "-";
            var sb = new System.Text.StringBuilder();
            int k = 0;
            foreach (object o in items)
            {
                if (k++ > 0) sb.Append(',');
                if (k > 14) { sb.Append("..."); break; }
                sb.Append(o);
            }
            return sb.ToString();
        }

        // ---------------------------------------------------------------- boite aux lettres
        // 'Check hand' (l'enveloppe est dans la boite) : deplacee en dernier par un autre joueur, elle est postee
        // chez lui (la commande arrive par le reseau) ; ici on attend (FINISHED -> 'Wait 1s', rien n'est poste),
        // puis elle disparait comme chez lui ('Wait 5s' de son jeu) quand ses messages se sont arretes.
        static void CheckEnvelope(PlayMakerFSM f)
        {
            FsmGameObject e = f.FsmVariables.FindFsmGameObject("Envelope");
            GameObject env = e != null ? e.Value : null;
            float age;
            // Lettre de Kela (ou autre) : a ce joueur seul, postee normalement.
            if (env == null || env.name != PartsEnvelope || !Props.MovedByOther(env, out age)) return;
            float now = Time.realtimeSinceStartup;
            if (age > 6f)
            {
                env.SetActive(false);
                Log.Info("appels : enveloppe " + env.name + " postee par un autre joueur, rangee ici aussi");
            }
            else if (now >= nextPostLog)
            {
                nextPostLog = now + 5f;
                Log.Info("appels : enveloppe " + env.name + " deplacee par un autre joueur : postee chez lui, pas ici");
            }
            f.Fsm.Event(FsmEvent.Finished);
        }

        // ---------------------------------------------------------------- reseau
        public static void OnMessage(Peer from, NetReader r)
        {
            byte[] raw = Session.IsHost ? r.Rest() : null;
            int who = r.U8();
            if (Session.IsHost)
            {
                who = from.Id;
                Session.Broadcast(new NetWriter(Msg.Call).U8(who).Raw(raw, 1, raw.Length - 1), true, who);
            }
            if (!PlayerSync.InGame) return;
            if (!resolved) Resolve();
            int kind = r.U8();
            if (kind == K_Bill) OnBill(who, r);
            else if (kind == K_Order) OnOrder(who, r);
            else if (kind == K_Job) OnJob(who, r);
            else if (kind == K_Phone && !Session.IsHost) OnPhones(r);
            else if (kind == K_Week && !Session.IsHost) OnWeek(r);
        }

        // ---------------------------------------------------------------- nouvelle semaine du magazine
        // Le jeu retire les petites annonces chaque semaine (GENERATELIST : 'Generate' de chaque annonce -> nouveau numero, prix,
        // photo, puis 'Format' renomme l'objet au numero). Chez l'invite, rien ne bougeait : le miroir du monde ne recopie que
        // nombres et listes, jamais le renommage -- anciens numeros, et les annonces appelees la semaine d'avant (renommees, plus
        // appelables) le restaient sur le nouveau magazine (Донской, 28/10 : « deja appelees chez l'invite, c'est faux »).
        // L'hote releve chaque seconde le numero de chaque annonce ; quand des annonces en ont un nouveau, il les envoie
        // (numero, prix, photo) ; l'invite les pose et rejoue 'Format'. [Coop] SemaineMagazine=0 : comme avant.
        static readonly Dictionary<string, string> weekSent = new Dictionary<string, string>();
        static float nextWeekCheck;
        static readonly string[] WeekInts = { "NMBR1", "NMBR2", "PriceInt", "Index", "IndexAudio", "TireType" };
        static readonly string[] WeekStrs = { "NMBR1s", "NMBR2s", "Phonenumber", "PriceS" };
        static void WeekStep(float now)
        {
            if (!Session.IsHost || numbers == null || now < nextWeekCheck || Config.GetInt("Coop", "SemaineMagazine", 1) == 0) return;
            nextWeekCheck = now + 1f;
            bool first = weekSent.Count == 0;
            var changed = new List<Transform>();
            for (int i = 0; i < numbers.childCount; i++)
            {
                Transform c = numbers.GetChild(i);
                if (!IsDigits(c.name) || c.name.Length < 7 || Game.FsmOn(c.gameObject, "Generate") == null) continue;   // (appelee : renommee, on garde son dernier numero)
                string key = ListingKey(c.gameObject), old;
                if (weekSent.TryGetValue(key, out old) && old == c.name) continue;
                weekSent[key] = c.name;
                if (!first) changed.Add(c);
            }
            if (changed.Count == 0 || Session.RemoteCount == 0) return;
            for (int s = 0; s < changed.Count; s += 12)
            {
                int n = Mathf.Min(12, changed.Count - s);
                var w = new NetWriter(Msg.Call).U8(Session.LocalId).U8(K_Week).U8(n);
                for (int i = s; i < s + n; i++)
                {
                    PlayMakerFSM g = Game.FsmOn(changed[i].gameObject, "Generate");
                    w.Str(ListingKey(changed[i].gameObject)).Str(changed[i].name);
                    foreach (string v in WeekInts) { FsmInt x = g.FsmVariables.FindFsmInt(v); w.I32(x != null ? x.Value : int.MinValue); }
                    foreach (string v in WeekStrs) { FsmString x = g.FsmVariables.FindFsmString(v); w.Str(x != null && x.Value != null ? x.Value : ""); }
                }
                Session.SendAll(w, true);
            }
            Log.Info("appels : magazine, " + changed.Count + " annonce(s) retiree(s) chez l'hote (nouvelle semaine), envoyee(s) aux invites");
        }

        static void OnWeek(NetReader r)
        {
            int n = r.U8(), ok = 0, renamed = 0;
            var miss = new List<string>();
            for (int i = 0; i < n; i++)
            {
                string key = r.Str(), number = r.Str();
                var ints = new int[WeekInts.Length]; var strs = new string[WeekStrs.Length];
                for (int k = 0; k < ints.Length; k++) ints[k] = r.I32();
                for (int k = 0; k < strs.Length; k++) strs[k] = r.Str();
                Transform t = FindListing(key);
                PlayMakerFSM g = t != null ? Game.FsmOn(t.gameObject, "Generate") : null;
                if (g == null) { miss.Add(key); continue; }
                for (int k = 0; k < ints.Length; k++) { FsmInt x = g.FsmVariables.FindFsmInt(WeekInts[k]); if (x != null && ints[k] != int.MinValue) x.Value = ints[k]; }
                for (int k = 0; k < strs.Length; k++) { FsmString x = g.FsmVariables.FindFsmString(WeekStrs[k]); if (x != null) x.Value = strs[k]; }
                Replay.Depth++;
                try { Game.SetState(g, "Format"); }
                finally { Replay.Depth--; }
                if (t.name != number) { t.name = number; renamed++; }   // (Format d'une autre forme : le numero de l'hote quand meme)
                ok++;
            }
            Log.Info("appels de l'hote : magazine, " + ok + "/" + n + " annonce(s) de la nouvelle semaine posee(s) ici" + (renamed > 0 ? " (" + renamed + " renommee(s) a la main)" : "")
                     + (miss.Count > 0 ? ", introuvable(s) : " + string.Join(", ", miss.ToArray()) : ""));
        }

        // ---------------------------------------------------------------- appels entrants (logique de l'hote)
        static readonly string[] LogicPaths = { "HOMENEW/Functions/FunctionsDisable/Telephone/Logic/PhoneLogicNEW", "YARD/Building/LIVINGROOM/Telephone 1/Logic/PhoneLogicOLD" };
        static readonly string[] LogicNames = { "appartement", "parents" };
        const float TakeDist = 25f, KeepDist = 60f, WarnDist = 20f;
        class Phone
        {
            public string Name; public GameObject Go; public PlayMakerFSM Ring, Jokes;
            public bool HostRuns = true, RunsHere, LastRuns = true, Fought;
            public float HostSeen = -1, WarnAt;
            public string HostState = "";
        }

        // Sonnerie : MasterAudio, groupe HouseFoley, variation phone_ring, jouee a l'emplacement du telephone par 'Ring'
        // (RingingNEW / RingingOLD). Dans le jeu seul, la logique du telephone s'eteint quand on s'eloigne ; en coop elle
        // tourne pour qui est pres (et chez l'hote toujours), et la sonnerie s'entendait de tres loin (retour d'un
        // joueur, 08/10). Ses sources (variations sous MasterAudio) : son en 3D, baisse lineaire, plus rien au-dela de
        // 35 m. Cherchees une fois (et revues tant qu'aucune n'est trouvee).
        const float RingRange = 35f;
        static bool ringCapped;
        static float ringNext;
        static void CapRing(Phone p)
        {
            if (ringCapped || Time.realtimeSinceStartup < ringNext) return;
            ringNext = Time.realtimeSinceStartup + 20f;
            int n = 0;
            foreach (Object o in Resources.FindObjectsOfTypeAll(typeof(AudioSource)))
            {
                var a = (AudioSource)o;
                if (a.hideFlags != HideFlags.None) continue;
                string nm = a.gameObject.name.ToLowerInvariant(), cn = a.clip != null ? a.clip.name.ToLowerInvariant() : "";
                if (nm.IndexOf("phone_ring") < 0 && cn.IndexOf("phone_ring") < 0) continue;
                a.spatialBlend = 1f;
                a.rolloffMode = AudioRolloffMode.Linear;
                a.minDistance = Mathf.Min(a.minDistance, 2f);
                a.maxDistance = RingRange;
                n++;
            }
            if (n == 0) return;
            ringCapped = true;
            Log.Info("appels : sonnerie des telephones bornee a " + RingRange + " m (" + n + " sources)");
        }
        static readonly Phone[] phones = new Phone[2];
        static float nextPhone, nextPhoneSend;
        static string phoneSig = "";

        // Vrai chez un invite qui fait tourner lui-meme la logique de ce telephone (celle de l'hote ne tourne pas) :
        // WorldFsms ne doit ni l'arreter ni la recaler sur les etats de l'hote.
        public static bool RunsHere(PlayMakerFSM f)
        {
            if (Session.IsHost || f == null) return false;
            foreach (Phone p in phones) if (p != null && p.RunsHere && (p.Ring == f || p.Jokes == f)) return true;
            return false;
        }

        static Phone PhoneAt(int i)
        {
            // (objet detruit depuis -- telephone remplace : NullReferenceException a chaque seconde dans le journal de Matilda,
            // 14/10, 1146 fois) : cherche de nouveau
            if (phones[i] != null && phones[i].Go == null) phones[i] = null;
            if (phones[i] != null) return phones[i];
            GameObject go = Game.FindAny(LogicPaths[i]);
            if (go == null) return null;
            phones[i] = new Phone { Name = LogicNames[i], Go = go, Ring = Game.FsmOn(go, "Ring"), Jokes = Game.FsmOn(go, "Jokes") };
            return phones[i];
        }

        static bool Runs(Phone p) { return p.Go != null && p.Go.activeInHierarchy && p.Ring != null && p.Ring.enabled; }

        // Combine decroche chez l'hote (cadran de cette ligne actif) : le jeu eteint la logique des appels entrants pendant son
        // appel. Ce n'est pas une logique qui ne tourne plus : un invite pres du telephone la faisait tourner chez lui pendant
        // tout l'appel de l'hote, et un appel entrant (Simo, farces) arrivait chez lui seul -- fil « Issue with phone » de
        // Donskoy, 26/10 : « le magazine Simo appelle tout seul, chez un des joueurs ». (lignes : appartement, parents, dans
        // le meme ordre que LogicPaths)
        static bool InUse(int i) { return i < lines.Length && lines[i] != null && lines[i].F != null && lines[i].F.gameObject.activeInHierarchy; }

        static float NearestGuest(Vector3 pos, out string name)
        {
            float best = float.MaxValue;
            name = "";
            foreach (PlayerInfo pi in Session.Players.Values)
            {
                if (pi.Local || pi.Level != 1) continue;
                float d = Vector3.Distance(pi.State.Feet, pos);
                if (d < best) { best = d; name = pi.Name; }
            }
            return best;
        }

        static float LocalDistance(Vector3 pos)
        {
            Transform pl = Game.PlayerT;   // (GameObject.Find a chaque appel avant)
            return pl != null ? Vector3.Distance(pl.position, pos) : float.MaxValue;
        }

        // Call : U8 joueur, U8 K_Phone, U8 n, (U8 ligne, Bool tourne chez l'hote, Str etat de 'Ring') x n (hote -> invites).
        static void PhoneStep(float now)
        {
            if (Session.IsHost)
            {
                var sb = new System.Text.StringBuilder();
                for (int i = 0; i < phones.Length; i++)
                {
                    Phone p = PhoneAt(i);
                    if (p == null) continue;
                    CapRing(p);
                    if (p.Go.activeInHierarchy && p.Ring != null && !p.Ring.enabled)
                    {
                        // Objet allume, automate arrete (par une autre logique) : la logique de l'hote tourne quelle que soit la distance.
                        p.Ring.enabled = true;
                        if (p.Jokes != null) p.Jokes.enabled = true;
                        Log.Info("appels : logique du telephone " + p.Name + " arretee chez l'hote alors que son objet est allume : rallumee");
                    }
                    bool runs = Runs(p) || InUse(i);
                    if (runs != p.LastRuns)
                    {
                        p.LastRuns = runs;
                        Log.Info("appels : logique du telephone " + p.Name + (runs ? (Runs(p) ? " tourne de nouveau" : " eteinte par le jeu, combine decroche (elle reste a l'hote)") : " ne tourne plus (objet " + (p.Go.activeInHierarchy ? "allume" : "eteint") + ")") + " chez l'hote");
                    }
                    if (!runs && Session.RemoteCount > 0 && now >= p.WarnAt)
                    {
                        string who;
                        float d = NearestGuest(p.Go.transform.position, out who);
                        if (d < WarnDist) { p.WarnAt = now + 30f; Log.Warn("appels : logique du telephone " + p.Name + " eteinte chez l'hote, " + who + " a " + d.ToString("F0") + " m du telephone (il la fait tourner chez lui)"); }
                    }
                    sb.Append(i).Append(runs ? '1' : '0').Append(p.Ring != null ? p.Ring.ActiveStateName : "").Append('|');
                }
                string sig = sb.ToString();
                if (Session.RemoteCount > 0 && (sig != phoneSig || now >= nextPhoneSend))
                {
                    phoneSig = sig; nextPhoneSend = now + 10f;
                    var w = new NetWriter(Msg.Call).U8(Session.LocalId).U8(K_Phone);
                    int n = 0; foreach (Phone p in phones) if (p != null) n++;
                    w.U8(n);
                    for (int i = 0; i < phones.Length; i++) if (phones[i] != null) w.U8(i).Bool(Runs(phones[i]) || InUse(i)).Str(phones[i].Ring != null ? phones[i].Ring.ActiveStateName ?? "" : "");
                    Session.SendAll(w, true);
                }
                return;
            }
            for (int i = 0; i < phones.Length; i++)
            {
                Phone p = PhoneAt(i);
                if (p == null || p.Ring == null) continue;
                CapRing(p);
                float d = LocalDistance(p.Go.transform.position);
                bool want = !p.HostRuns && p.Go.activeInHierarchy && d < (p.RunsHere ? KeepDist : TakeDist);
                if (want != p.RunsHere)
                {
                    p.RunsHere = want; p.Fought = false;
                    p.Ring.enabled = want;
                    if (p.Jokes != null) p.Jokes.enabled = want;
                    Log.Info("appels : telephone " + p.Name + (want ? " : la logique de l'hote ne tourne pas, elle tourne ici (joueur a " + d.ToString("F0") + " m)"
                                                                     : " : logique rendue a l'hote (" + (p.HostRuns ? "elle tourne de nouveau chez lui" : "joueur a " + d.ToString("F0") + " m") + ")"));
                }
                else if (p.RunsHere && !p.Ring.enabled && !p.Fought)
                {
                    p.Fought = true;   // arretee par WorldFsms (sans Calls.RunsHere) : on ne lutte pas (chaque rallumage la relancerait)
                    Log.Warn("appels : telephone " + p.Name + " : logique arretee par le monde alors qu'elle devrait tourner ici");
                }
            }
        }

        static void OnPhones(NetReader r)
        {
            float now = Time.realtimeSinceStartup;
            for (int i = 0, n = r.U8(); i < n; i++)
            {
                int idx = r.U8();
                bool runs = r.Bool();
                string st = r.Str();
                Phone p = idx < phones.Length ? PhoneAt(idx) : null;
                if (p == null) continue;
                if (p.HostRuns != runs) Log.Info("appels : logique du telephone " + p.Name + (runs ? " tourne" : " ne tourne pas") + " chez l'hote");
                p.HostRuns = runs; p.HostState = st; p.HostSeen = now;
            }
        }

        // Essais (WorldFsms.TestEvent, essai 'colis') : evenement envoye a un automate de ce module, comme le jeu.
        public static string TestEvent(string part, string ev)
        {
            if (!resolved && PlayerSync.InGame) Resolve();
            var all = new List<PlayMakerFSM> { spawners[0], spawners[1], postBox };
            foreach (Line l in lines) if (l != null) all.Add(l.F);
            foreach (PlayMakerFSM f in all)
            {
                if (f == null) continue;
                string p = Recon.Path(f.transform) + "::" + f.FsmName;
                if (!p.Contains(part)) continue;
                string before = f.ActiveStateName;
                f.SendEvent(ev);
                return "appels : " + p + " : " + before + " -" + ev + "-> " + f.ActiveStateName;
            }
            return null;
        }

        // ---------------------------------------------------------------- essais
        // appel ([Test] Autotest=appel) : l'hote compose [Test] TestNumero (08231206 = pubs par defaut ; "annonce" :
        // la 1re petite annonce) sur [Test] TestLigne (0 appartement, 1 parents, 2 taxi) a 43 s, par les variables
        // et etats du cadran (sonneries et voix sautees). Les deux notent a 100 s l'offre, le boulot des pubs
        // (JOBS/ADs), les factures et les commandes. Attendu : offre "numberdisabled" chez les deux, boulot passe
        // (Wait call -> State 3) chez les deux, communications +1 chez les deux ; avec une annonce : meme commande
        // OrderYPn (meme liste) chez les deux.
        // courrier ([Test] Autotest=courrier) : [Test] TestPosteur (hote par defaut, ou invite) remplit le bon de
        // commande du catalogue (1re piece de la table AMISSpawners), sort l'enveloppe (ORDER) a 40 s et la pose
        // dans la boite aux lettres de la station a 43 s (a 48 s, depot simule si le declencheur n'est pas passe).
        // Les deux notent les commandes a 55 s. A 60 s il rend la commande prete (Idle), a 63 s il paie (PAYMENT,
        // comme le guichet) ; les deux notent a 75 s : meme commande OrderAMISn (meme liste), enveloppe rangee,
        // colis colis:OrderAMISn chez les deux.
        static int step, step2, testLine;
        static float lastPoll;
        static PlayMakerFSM testCall;
        static GameObject testKeypad;
        static bool testActivated, testLogged;
        static string testNumber = "";

        public static void Test(string mode, float t)
        {
            if (mode == "appel") TestCall(t);
            else if (mode == "courrier") TestMail(t);
            else if (mode == "appel-loin") TestFar(t);
        }

        // appel-loin ([Test] Autotest=appel-loin) : ligne [Test] TestLigne (0 appartement, 1 parents) ; l'invite pres de ce
        // telephone (TestPos=-1284.5,1,1079.5 pour l'appartement, -9,0.5,10 pour les parents), l'hote n'importe ou.
        // 30 s : chez l'hote, l'automate 'Ring' est arrete (attendu : « rallumee » dans la seconde) ; 36 s : l'hote eteint
        // l'objet Logic de ce telephone (comme si sa logique ne tournait pas) ; 44 s : si la logique tourne chez l'invite,
        // son 'Ring' passe a 'Ring' (comme un appel qui arrive : RingingNEW/RingingOLD s'allume) ; 60 s : l'hote rallume
        // Logic. Chacun note toutes les 2 s de 28 a 70 s : « autotest : appel-loin, <ligne> : hote tourne .., ici tourne ..,
        // Ring <etat>, sonnerie <allumee> ». Attendu chez l'invite : « elle tourne ici » vers 37 s, sonnerie allumee a 44 s,
        // « logique rendue a l'hote » vers 61 s.
        static float farLog;
        static int farStep;

        static void TestFar(float t)
        {
            int line = Mathf.Clamp(Config.GetInt("Test", "TestLigne", 0), 0, phones.Length - 1);
            Phone p = PhoneAt(line);
            if (p == null || p.Ring == null) { if (farStep == 0 && t > 28f) { farStep = 9; Log.Info("autotest : appel-loin, telephone " + line + " absent"); } return; }
            GameObject logic = p.Go.transform.parent != null ? p.Go.transform.parent.gameObject : p.Go;
            if (Session.IsHost && farStep == 0 && t > 30f) { farStep = 1; p.Ring.enabled = false; Log.Info("autotest : appel-loin, l'hote arrete son automate 'Ring'"); }
            // ([Test] AppelLoinCombine=1 : de 34 a 50 s le cadran de l'hote est actif, comme un combine decroche -- la logique eteinte
            // a 36 s doit rester a l'hote jusqu'a 50 s, puis passer a l'invite)
            bool handset = Config.GetInt("Test", "AppelLoinCombine", 0) != 0 && line < lines.Length && lines[line] != null && lines[line].F != null;
            if (Session.IsHost && handset && t > 34f && t < 50f && !lines[line].F.gameObject.activeSelf) { lines[line].F.gameObject.SetActive(true); Log.Info("autotest : appel-loin, combine de l'hote decroche"); }
            if (Session.IsHost && handset && t > 50f && lines[line].F.gameObject.activeSelf && farStep < 3) { lines[line].F.gameObject.SetActive(false); Log.Info("autotest : appel-loin, combine de l'hote raccroche"); }
            if (Session.IsHost && farStep == 1 && t > 36f) { farStep = 2; logic.SetActive(false); Log.Info("autotest : appel-loin, l'hote eteint " + Recon.Path(logic.transform)); }
            if (!Session.IsHost && farStep == 0 && t > 44f)
            {
                farStep = 1;
                if (p.RunsHere) { Game.SetState(p.Ring, "Ring"); Log.Info("autotest : appel-loin, un appel arrive ici -> " + p.Ring.ActiveStateName); }
                else Log.Info("autotest : appel-loin, la logique ne tourne pas ici : pas d'appel force");
            }
            if (Session.IsHost && farStep == 2 && t > 60f) { farStep = 3; logic.SetActive(true); Log.Info("autotest : appel-loin, l'hote rallume " + logic.name); }
            if (t > 28f && t < 71f && t - farLog >= 2f)
            {
                farLog = t;
                FsmGameObject rg = p.Ring.FsmVariables.FindFsmGameObject("Ringing");
                GameObject ringing = rg != null ? rg.Value : null;
                Log.Info("autotest : appel-loin, " + p.Name + " : hote tourne " + (Session.IsHost ? Runs(p) : p.HostRuns) + ", ici tourne " + (Session.IsHost ? Runs(p) : p.RunsHere)
                         + ", Ring " + p.Ring.ActiveStateName + (p.Ring.enabled ? "" : " (arrete)") + ", sonnerie " + (ringing != null && ringing.activeInHierarchy ? "allumee" : "eteinte")
                         + ", a " + LocalDistance(p.Go.transform.position).ToString("F0") + " m");
            }
        }

        static void TestCall(float t)
        {
            // (celui qui appelle : l'hote, ou l'invite avec [Test] AppelInvite=1)
            bool host = Config.GetInt("Test", "AppelInvite", 0) != 0 ? !Session.IsHost : Session.IsHost, whole = Config.GetInt("Test", "AppelEntier", 0) != 0;
            if (host && t > 40f && step == 0)
            {
                step = 1;
                if (!resolved) Resolve();
                testLine = Mathf.Clamp(Config.GetInt("Test", "TestLigne", 0), 0, lines.Length - 1);
                Line l = lines[testLine];
                if (l == null || l.F == null) { Log.Info("autotest : appel, cadran " + testLine + " absent"); step = 9; return; }
                testCall = l.F;
                testKeypad = l.F.gameObject;
                testActivated = !testKeypad.activeSelf;
                if (testActivated) testKeypad.SetActive(true);
                Log.Info("autotest : appel, cadran " + l.Name + " actif " + testKeypad.activeInHierarchy + ", etat '" + testCall.ActiveStateName + "'");
            }
            if (host && t > 43f && step == 1)
            {
                // Le cadran a demarre et ses crochets sont poses (Inject chaque seconde) : numero compose d'un coup.
                step = 2;
                string num = Config.Get("Test", "TestNumero", "08231206");
                if (num.StartsWith("annonce")) { int k; num = FirstAdNumber(int.TryParse(num.Substring(7), out k) ? k : 0); }   // (annonceN : la N-ieme, de 0)
                testNumber = num;
                if (num.Length < 2) { Log.Info("autotest : appel, pas de numero"); step = 9; return; }
                if (testCall.ActiveStateName != "State 1") Game.SetState(testCall, "State 1");
                testCall.FsmVariables.FindFsmString("Number").Value = num.Substring(0, num.Length - 1);
                testCall.FsmVariables.FindFsmString("NumberAdd").Value = num.Substring(num.Length - 1);
                testCall.SendEvent("NUMBER");
                Log.Info("autotest : appel, compose " + num + " -> " + testCall.ActiveStateName + " (crochets " + lines[testLine].Hooked + ")");
            }
            if (host && step == 2 && t - lastPoll >= 0.5f)
            {
                lastPoll = t;
                string s = testCall.ActiveStateName;
                FsmGameObject fl = testCall.FsmVariables.FindFsmGameObject("FoundListing");
                if (s == "Ring 2")
                {
                    if (fl == null || fl.Value == null) { Log.Info("autotest : appel, numero " + testNumber + " introuvable"); step = 3; }
                    else { Log.Info("autotest : appel, pas de reponse (tirage) : on rappelle"); Game.SetState(testCall, "Delay"); }
                }
                else if (s == "State 3") { Log.Info("autotest : appel, factures impayees : appel force"); Game.SetState(testCall, "Delay"); }
                else if (s == "Ring" || s == "Add ring") Game.SetState(testCall, "Check type");   // sonneries sautees
                else if (s == "Call") Game.SetState(testCall, "Hangup 2");                     // voix sautee
                // ([Test] AppelSansCommande=1 : createur arrete avant 'Hangup' -- SPAWNITEM perdu, la relance doit le rallumer et creer la commande)
                if ((s == "Telepuhelu" || s == "Kaukopuhelu") && spawners[0] != null && spawners[0].enabled && Config.GetInt("Test", "AppelSansCommande", 0) != 0)
                { spawners[0].enabled = false; Log.Info("autotest : appel, createur de commandes arrete"); }
                else if ((s == "Telepuhelu" || s == "Kaukopuhelu") && !whole) Game.SetState(testCall, "Hangup");   // ([Test] AppelEntier=1 : conversation jusqu'au bout)
                else if (s == "Beep beep")
                {
                    step = 3; Log.Info("autotest : appel fini (" + (fl != null && fl.Value != null ? fl.Value.name : "?") + "), numeros " + Counter(spawners[0]) + "/" + Counter(spawners[1]));
                    // ([Test] AppelSauve=1 : sauvegarde en jeu juste apres -- le compteur et la commande ecrits, pour le chargement suivant)
                    if (Config.GetInt("Test", "AppelSauve", 0) != 0) { Game.SaveInPlace(); Log.Info("autotest : appel, sauvegarde en jeu"); }
                }
                if (step == 2 && t > (whole ? 150f : 90f)) { step = 3; Log.Info("autotest : appel bloque en '" + s + "'"); }
            }
            if (host && step == 3 && testActivated && testKeypad != null) { testActivated = false; testKeypad.SetActive(false); }
            if (t > (whole ? 160f : 100f) && !testLogged) { testLogged = true; Log.Info("autotest : appel, " + CallState()); }
            // ([Test] AppelSemaine=s, dossier de Донской 28/10 « sur le nouveau magazine, des annonces deja appelees chez l'invite ») :
            // nouvelle semaine du magazine a s secondes chez l'hote (GENERATELIST, comme le jeu) ; annonces des deux cotes 5 s avant et 15 s apres.
            float week = Config.GetFloat("Test", "AppelSemaine", 0f);
            if (week > 0f)
            {
                if (weekStep == 0 && t > week - 5f) { weekStep = 1; Log.Info("autotest : semaine, avant : " + ListingsState()); }
                if (weekStep == 1 && t > week) { weekStep = 2; if (Session.IsHost) { PlayMakerFSM.BroadcastEvent("GENERATELIST"); Log.Info("autotest : semaine, GENERATELIST chez l'hote"); } }
                if (weekStep == 2 && t > week + 15f) { weekStep = 3; Log.Info("autotest : semaine, apres : " + ListingsState()); }
            }
        }
        static int weekStep;

        // Petites annonces (Generate) : nom d'origine -> nom de l'objet (le numero ; un autre nom = plus appelable), etat de Generate.
        static string ListingsState()
        {
            if (numbers == null) return "pas de PhoneNumbers";
            var sb = new System.Text.StringBuilder();
            int n = 0, off = 0;
            for (int i = 0; i < numbers.childCount; i++)
            {
                Transform c = numbers.GetChild(i);
                PlayMakerFSM g = Game.FsmOn(c.gameObject, "Generate");
                if (g == null) continue;
                n++;
                bool num = c.name.Length >= 7 && IsDigits(c.name);
                if (!num) off++;
                sb.Append(StrVar(c.gameObject, "Generate", "Name")).Append('=').Append(num ? c.name : "[" + c.name + "]").Append('/').Append(g.ActiveStateName).Append(' ');
            }
            return n + " annonces, " + off + " plus appelables : " + sb;
        }

        static string FirstAdNumber(int skip)
        {
            if (numbers == null) return "";
            for (int i = 0; i < numbers.childCount; i++)
            {
                Transform c = numbers.GetChild(i);
                if (c.name.Length >= 7 && IsDigits(c.name) && Game.FsmOn(c.gameObject, "Generate") != null && skip-- == 0)
                {
                    Log.Info("autotest : appel, annonce " + StrVar(c.gameObject, "Generate", "Name") + " (" + c.name + ")");
                    return c.name;
                }
            }
            return "";
        }

        static bool IsDigits(string s)
        {
            foreach (char c in s) if (c < '0' || c > '9') return false;
            return true;
        }

        static string CallState()
        {
            var sb = new System.Text.StringBuilder();
            string num = Config.Get("Test", "TestNumero", "08231206");
            if (numbers != null)
                for (int i = 0; i < numbers.childCount; i++)
                {
                    Transform c = numbers.GetChild(i);
                    PlayMakerFSM d = Game.FsmOn(c.gameObject, "Data");
                    if (d == null || StrVar(c.gameObject, "Data", "Number") != num) continue;
                    FsmInt stg = d.FsmVariables.FindFsmInt("Stage");
                    sb.Append("offre ").Append(num).Append(" : ").Append(c.name).Append(", etape ").Append(stg != null ? stg.Value : -1).Append(", etat ").Append(d.ActiveStateName).Append(" ; ");
                }
            GameObject ads = Game.FindAny("JOBS/ADs");
            PlayMakerFSM ad = ads != null ? Game.FsmOn(ads, "Data") : null;
            if (ad != null) { FsmInt js = ad.FsmVariables.FindFsmInt("JobStage"); sb.Append("pubs : etat ").Append(ad.ActiveStateName).Append(", JobStage ").Append(js != null ? js.Value : -1).Append(" ; "); }
            foreach (string b in new[] { "Systems/PhoneBills1", "Systems/PhoneBills2" })
            {
                GameObject g = Game.FindAny(b);
                PlayMakerFSM f = g != null ? Game.FsmOn(g, "Data") : null;
                if (f == null) continue;
                FsmFloat c = f.FsmVariables.FindFsmFloat("Connects"), m = f.FsmVariables.FindFsmFloat("Minutes");
                sb.Append(g.name).Append(' ').Append(c != null ? c.Value.ToString("F0") : "?").Append(" com. ").Append(m != null ? m.Value.ToString("F1") : "?").Append(" min ; ");
            }
            sb.Append(OrdersState(0));
            return sb.ToString();
        }

        static string OrdersState(int s)
        {
            PlayMakerFSM sp = spawners[s];
            if (sp == null) return (s == 0 ? "YP" : "AMIS") + " absent ; ";
            var sb = new System.Text.StringBuilder(s == 0 ? "commandes YP n" : "commandes AMIS n");
            FsmInt num = sp.FsmVariables.FindFsmInt("ObjectNumberInt");
            sb.Append(num != null ? num.Value : -1).Append(" :");
            Transform tr = sp.transform;
            for (int i = 0; i < tr.childCount; i++)
            {
                Transform c = tr.GetChild(i);
                PlayMakerFSM d = Game.FsmOn(c.gameObject, "Data");
                if (d == null) continue;
                FsmBool act = d.FsmVariables.FindFsmBool("OrderActive");
                FsmFloat wt = d.FsmVariables.FindFsmFloat("WaitTime");
                sb.Append(' ').Append(c.name).Append('[').Append(d.ActiveStateName).Append(act != null && act.Value ? ", active" : "")
                  .Append(", attente ").Append(wt != null ? wt.Value.ToString("F0") : "?").Append(", ").Append(Summary(ListOf(c.gameObject, "Parts"))).Append(']');
            }
            return sb.Append(" ; ").ToString();
        }

        static PlayMakerFSM LastOrder(int s)
        {
            PlayMakerFSM sp = spawners[s];
            if (sp == null) return null;
            for (int i = sp.transform.childCount - 1; i >= 0; i--)
            {
                PlayMakerFSM d = Game.FsmOn(sp.transform.GetChild(i).gameObject, "Data");
                if (d != null) return d;
            }
            return null;
        }

        static void TestMail(float t)
        {
            bool poster = Session.IsHost == (Config.Get("Test", "TestPosteur", "hote") != "invite");
            if (!resolved && t > 30f) Resolve();
            if (poster && t > 40f && step == 0)
            {
                step = 1;
                if (amisList == null) { Log.Info("autotest : courrier, bon de commande absent"); step = 9; return; }
                string vin = FirstAmisKey();
                System.Collections.ArrayList order = ListOf(amisList.gameObject, "Order");
                if (order == null) { Log.Info("autotest : courrier, liste Order absente"); step = 9; return; }
                while (order.Count < 2) order.Add(order.Count == 0 ? (object)0 : "");
                order[1] = vin;
                amisList.SendEvent("ORDER");
                FsmGameObject env = amisList.FsmVariables.FindFsmGameObject("ThisEnvelope");
                Log.Info("autotest : courrier, bon de commande " + Summary(order) + ", enveloppe "
                         + (env != null && env.Value != null ? env.Value.name + " active " + env.Value.activeInHierarchy : "?"));
            }
            if (poster && t > 43f && step == 1)
            {
                step = 2;
                FsmGameObject env = amisList.FsmVariables.FindFsmGameObject("ThisEnvelope");
                GameObject e = env != null ? env.Value : null;
                if (e == null || postBox == null) { Log.Info("autotest : courrier, enveloppe ou boite absente"); step = 9; return; }
                e.transform.position = postBox.transform.position;
                Rigidbody rb = e.GetComponent<Rigidbody>();
                if (rb != null && !rb.isKinematic) { rb.velocity = Vector3.zero; rb.WakeUp(); }
                Log.Info("autotest : courrier, enveloppe posee dans la boite (boite active " + postBox.gameObject.activeInHierarchy + ", etat '" + postBox.ActiveStateName + "')");
            }
            if (poster && t > 48f && step == 2)
            {
                step = 3;
                FsmGameObject env = amisList.FsmVariables.FindFsmGameObject("ThisEnvelope");
                GameObject e = env != null ? env.Value : null;
                if (e != null && e.activeInHierarchy)
                {
                    // Declencheur pas passe : depot simule depuis l'etat qui suit la main ; boite inactive (loin) :
                    // directement sa consequence (INBOX au bon de commande).
                    if (postBox != null && postBox.gameObject.activeInHierarchy)
                    {
                        FsmGameObject pe = postBox.FsmVariables.FindFsmGameObject("Envelope");
                        if (pe != null) pe.Value = e;
                        Game.SetState(postBox, "Check hand");
                        Log.Info("autotest : courrier, depot simule -> " + postBox.ActiveStateName);
                    }
                    else { amisList.SendEvent("INBOX"); Log.Info("autotest : courrier, boite inactive : INBOX direct -> " + amisList.ActiveStateName); }
                }
            }
            if (t > 55f && step2 == 0) { step2 = 1; Log.Info("autotest : courrier, " + MailState()); }
            if (poster && t > 60f && step == 3)
            {
                step = 4;
                PlayMakerFSM o = LastOrder(1);
                Log.Info("autotest : courrier, commande prete " + (o != null && Game.SetState(o, "Idle") ? o.gameObject.name + " -> " + o.ActiveStateName : "absente"));
            }
            if (poster && t > 63f && step == 4)
            {
                step = 5;
                PlayMakerFSM o = LastOrder(1);
                if (o != null) { string before = o.ActiveStateName; o.SendEvent("PAYMENT"); Log.Info("autotest : courrier, paye " + o.gameObject.name + " : " + before + " -> " + o.ActiveStateName); }
            }
            if (t > 75f && step2 == 1) { step2 = 2; Log.Info("autotest : courrier, " + MailState() + " colis " + Props.Ids("colis:")); }
        }

        static string MailState()
        {
            var sb = new System.Text.StringBuilder();
            if (amisList != null)
            {
                FsmBool act = amisList.FsmVariables.FindFsmBool("Activated");
                FsmGameObject env = amisList.FsmVariables.FindFsmGameObject("ThisEnvelope");
                sb.Append("bon ").Append(amisList.ActiveStateName).Append(act != null && act.Value ? " (rempli)" : "").Append(' ')
                  .Append(Summary(ListOf(amisList.gameObject, "Order"))).Append(", enveloppe ")
                  .Append(env != null && env.Value != null ? (env.Value.activeInHierarchy ? "sortie" : "rangee") : "?").Append(" ; ");
            }
            sb.Append(OrdersState(1));
            return sb.ToString();
        }

        // 1re piece du catalogue AMIS : cle de la table 'Spawners' de PostSystem/AMISSpawners.
        static string FirstAmisKey()
        {
            GameObject ps = Game.FindAny("CARPARTS/PARTSYSTEM/PostSystem");
            PlayMakerFSM logic = ps != null ? Game.FsmOn(ps, "Logic") : null;
            FsmGameObject sp = logic != null ? logic.FsmVariables.FindFsmGameObject("AMISSpawners") : null;
            if (sp == null || sp.Value == null) return "";
            foreach (PlayMakerHashTableProxy h in sp.Value.GetComponents<PlayMakerHashTableProxy>())
                if (h.referenceName == "Spawners" && h._hashTable != null)
                    foreach (object k in h._hashTable.Keys) return k as string ?? "";
            return "";
        }
    }
}
