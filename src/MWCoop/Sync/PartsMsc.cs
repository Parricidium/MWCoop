using System.Collections.Generic;
using HutongGames.PlayMaker;
using MWCoop.Net;
using UnityEngine;

namespace MWCoop
{
    // Pieces de My Summer Car (vidage v.250908-04, 19/10). Pas d'ID ni de points VINP_ : autre mecanique, meme principe.
    //  - point de montage : un declencheur (SATSUMA/Body/trigger_door_left...) avec l'automate 'Assembly' : la piece tenue en main
    //    (Part), 'Wait for assembly' -ASSEMBLE-> (le clic) 'Wait' -> 'Freeze car' -> 'Set joint 2' -> 'Assemble 2' (SetParent
    //    sur son pivot, Database/.../Data.Installed) -> 'End' (le declencheur s'eteint). Au chargement le jeu passe par 'Check
    //    Assembly' (Setup), jamais par 'Wait for assembly' : seul ce passage-la est un montage du joueur.
    //  - demontage : l'automate 'Removal' de la piece, etat 'Remove part' (declencheur rallume, piece detachee). 'Mouse over'
    //    -REMOVE-> : clic du joueur ; 'Mouse off' -REMOVE-> : casse (joint rompu).
    //  - vis : 'Screw' (Stage 0..8, PartAssembled) ; TIGHTEN / UNTIGHTEN -> 'Screw 2' / 'Unscrew 2' -> 'Wait 3' / 'Wait 4' (+-1 au
    //    BoltCheck.Tightness de la piece). Une action au debut de 'Wait 3' / 'Wait 4' envoie (piece, vis, Stage) ; ailleurs on
    //    rejoue TIGHTEN / UNTIGHTEN jusqu'a ce Stage.
    // Cle d'une piece : "db:" + chemin de son entree Database (db_ThisPart de 'Removal'), sinon l'ID de son 'Use' (roues), sinon
    // son chemin a rangs a la premiere vue. Cle d'un point : cle de la piece qui le porte + chemin relatif, ou chemin complet.
    //  - points « echange » (retour de $forever1, 20/10 : toute la mecanique de CARPARTS/PartsCar -- sub frame, trains, freins,
    //    direction ; 75 points) : pas de {Parent} ni de 'Assemble 2'. Le clic mene a 'Assemble' (ou a 'Check bolts' puis
    //    'Assemble') qui allume la piece montee deja dans la voiture (SATSUMA/.../sub frame(xxxxx)), met Installed et DETRUIT la
    //    piece libre ({Part}, sans automate : sa cle est celle de Props, w:...). Rejoue : 'Assemble', avec la copie d'ici de la
    //    piece libre (Props) si elle existe, sinon sans. Demontage : 'Remove part' cree une NOUVELLE piece libre ({SpawnPart} au
    //    RemovalPoint) -- celui qui demonte lui donne une cle (Props.Adopt), envoyee avec le demontage ; l'autre la donne a la
    //    sienne. Avant : « piece w:... introuvable ici », pieces restees par terre chez l'hote, 551 vis refusees.
    public static partial class Parts
    {
        class MscPoint { public string Key; public PlayMakerFSM Fsm; public string Click; public bool Swap; }
        static readonly Dictionary<string, MscPoint> mscPoints = new Dictionary<string, MscPoint>();
        static readonly Dictionary<PlayMakerFSM, MscPoint> mscPointOf = new Dictionary<PlayMakerFSM, MscPoint>();
        static readonly HashSet<PlayMakerFSM> mscRemovals = new HashSet<PlayMakerFSM>();
        static readonly Dictionary<GameObject, string> mscFirstKey = new Dictionary<GameObject, string>();
        static readonly Dictionary<string, GameObject> mscParts = new Dictionary<string, GameObject>();
        static float mscPartsAt = -100f;

        class MscPointHook : ModHook
        {
            public override string Module { get { return "pieces"; } }
            public MscPoint P;
            public override void OnEnter()
            {
                try
                {
                    FsmState prev = P.Fsm.Fsm.PreviousActiveState;
                    // (« Wait for assembly 2 », « 3 »... : points a plusieurs pieces, ClickFor)
                    if (!applying && Replay.Depth == 0 && prev != null && (prev.Name.StartsWith("Wait for assembly") || P.Swap && prev.Name == "Check bolts")) MscLocalInstall(P);
                }
                catch (System.Exception e) { Replay.HookError(e); }
                Finish();
            }
        }

        class MscRemovalHook : ModHook
        {
            public override string Module { get { return "pieces"; } }
            public PlayMakerFSM F;
            public override void OnEnter()
            {
                try
                {
                    FsmState prev = F.Fsm.PreviousActiveState;
                    // (piece « echange » : pas de joint, 'Mouse off' -REMOVE-> y est le decrochage {Detach} du joueur d'ici, pas une casse)
                    if (!applying && Replay.Depth == 0)
                    {
                        // (piece « echange » : son 'Mouse off' -REMOVE-> est aussi le decrochage {Detach} du joueur d'ici -- sauf si son attache vient de
                        // rompre (PartWatch, 1 s) : alors c'est une casse, comme les autres -- reservoir arrache sur la copie et « demonte » chez l'hote,
                        // chaos S3 27/10)
                        PartWatch pw = F.GetComponent<PartWatch>();
                        bool jointBroke = pw != null && Time.realtimeSinceStartup - pw.LastBreak < 1f;
                        bool breakoff = prev != null && prev.Name == "Mouse off" && (F.FsmVariables.FindFsmGameObject("SpawnPart") == null || jointBroke);
                        if (breakoff && MscKeepOnCopy(F)) { Finish(); return; }
                        MscLocalRemove(F, breakoff);
                    }
                }
                catch (System.Exception e) { Replay.HookError(e); }
                Finish();
            }
        }

        class MscBoltHook : ModHook
        {
            public override string Module { get { return "pieces (boulons)"; } }
            public Bolt B;
            public override void OnEnter()
            {
                try { if (!applying && Replay.Depth == 0) MscLocalBolt(B); } catch (System.Exception e) { Replay.HookError(e); }
                Finish();
            }
        }

        // Roues de la SATSUMA (.../pivot_wheel_standard/wheel_regula::Use) : au-dessus de 33 km/h avec des ecrous pas serres a fond
        // (« Bolts ON 2 »), « Chance » tire au sort (SendRandomEvent) la chute de la roue -- « Break off » : ecrous remis a zero,
        // Removal Detach. Chacun tirait de son cote, sur sa copie aussi : roue tombee chez un seul joueur, ecrous a zero chez lui
        // (journaux de Yunus et Markos, 25/10 : « Bolts ON 2 -BREAK-> Chance » sur les 4 roues, rejoue chez l'autre ; boulons qui
        // flottent a cote de la voiture en roulant). Seul celui qui mene la voiture tire ; ailleurs « Chance » repart en « Wait »
        // sans tirer, et une vraie chute arrive par le demontage de la roue chez lui.
        static readonly HashSet<PlayMakerFSM> wheelHooked = new HashSet<PlayMakerFSM>();
        static readonly List<FsmStateAction> wheelCut = new List<FsmStateAction>();
        static int wheelLogs;
        class WheelChanceHook : ModHook
        {
            public override string Module { get { return "pieces (roues)"; } }
            public PlayMakerFSM F;
            public override void OnEnter()
            {
                try
                {
                    if (Session.Active && !VehicleSync.SimAuthority(F.transform))
                    {
                        foreach (FsmStateAction a in State.Actions) if (a != this && a.Enabled) { a.Enabled = false; wheelCut.Add(a); }
                        if (wheelLogs++ < 10) global::MWCoop.Log.Info("roue " + Recon.Path(F.transform) + " : chute tiree au sort chez celui qui mene la voiture, pas ici");
                        Fsm.Event(FsmEvent.Finished);
                    }
                }
                catch (System.Exception e) { Replay.HookError(e); }
                Finish();
            }
        }
        static void MscWheelHook(PlayMakerFSM f)
        {
            if (wheelHooked.Contains(f) || f.Fsm == null || f.Fsm.GetState("Bolts ON 2") == null || f.Fsm.GetState("Break off") == null) return;
            FsmState s = f.Fsm.GetState("Chance");
            if (s == null || !s.IsInitialized || !Inject(s, new WheelChanceHook { F = f })) return;
            wheelHooked.Add(f);
        }

        static void MscReset()
        {
            wheelHooked.Clear(); wheelCut.Clear();
            mscPoints.Clear(); mscPointOf.Clear(); mscRemovals.Clear(); mscFirstKey.Clear(); mscParts.Clear(); mscPartsAt = -100f; mscOut.Clear(); keptJoints.Clear();
            mscDb.Clear(); mscDbByHash = null; nextPrint = lastListAsk = lastListSent = 0f; printMiss = 0; mscLostOnce = false;
        }

        // ---------------------------------------------------------------- cles
        public static string MscPartKey(GameObject part)
        {
            if (part == null) return "";
            PlayMakerFSM rem = Game.FsmOn(part, "Removal");
            FsmGameObject db = rem != null ? rem.FsmVariables.FindFsmGameObject("db_ThisPart") : null;
            if (db != null && db.Value != null) return "db:" + Recon.Path(db.Value.transform);
            PlayMakerFSM use = Game.FsmOn(part, "Use");
            FsmString id = use != null ? use.FsmVariables.FindFsmString("ID") : null;
            if (id != null && id.Value.Length > 0) return "id:" + id.Value;
            // Sans entree Database (guirlande de Noel...) : la cle du monde de Props, la meme chez tous (prise au chargement) ; sinon le
            // chemin a rangs, jamais pris sous PLAYER (piece en main : chemin propre a ce joueur -- « introuvable ici » chez l'autre).
            string k = Props.KnownWorldKey(part);
            if (k != null) return k;
            if (mscFirstKey.TryGetValue(part, out k)) return k;
            k = "r:" + RankPath(part.transform);
            if (!Game.UnderPlayer(part.transform)) mscFirstKey[part] = k;
            return k;
        }

        static bool IsMscPart(GameObject go) { return go != null && Game.FsmOn(go, "Removal") != null; }

        static string MscPointKey(Transform t)
        {
            string rel = t.name;
            for (Transform p = t.parent; p != null; p = p.parent)
            {
                if (IsMscPart(p.gameObject)) return MscPartKey(p.gameObject) + "/" + rel;
                rel = Link(p) + "/" + rel;
            }
            return rel;
        }

        static GameObject MscFindPart(string key)
        {
            GameObject go;
            if (mscParts.TryGetValue(key, out go) && go != null) return go;
            if (Time.realtimeSinceStartup - mscPartsAt < 1f) return null;
            mscPartsAt = Time.realtimeSinceStartup;
            mscParts.Clear();
            foreach (Object o in Game.FsmsNamed("Removal", 0.5f))
            {
                var f = o as PlayMakerFSM;
                if (f == null || f.hideFlags != HideFlags.None) continue;
                mscParts[MscPartKey(f.gameObject)] = f.gameObject;
            }
            return mscParts.TryGetValue(key, out go) ? go : null;
        }

        // ---------------------------------------------------------------- releve
        static bool IsMscPoint(PlayMakerFSM f)
        {
            if (f.FsmName != "Assembly" || f.FsmVariables.FindFsmGameObject("Part") == null || f.Fsm.GetState("Wait for assembly") == null) return false;
            return Game.RootName(f.transform) != "ITEMS";   // (piles de la radio : ItemFsms)
        }

        static bool IsMscBolt(PlayMakerFSM f)
        {
            return f.FsmName == "Screw" && f.FsmVariables.FindFsmInt("Stage") != null && f.FsmVariables.FindFsmGameObject("PartAssembled") != null
                   && f.Fsm.GetState("Wait 3") != null && f.Fsm.GetState("Wait 4") != null;
        }

        // Point « echange » : ni {Parent} ni 'Assemble 2', un etat 'Assemble' (la piece montee est deja dans la voiture).
        static bool IsSwapPoint(PlayMakerFSM f)
        {
            return f.FsmVariables.FindFsmGameObject("Parent") == null && f.Fsm.GetState("Assemble") != null && f.Fsm.GetState("Assemble 2") == null;
        }

        // Etat ou mene le clic ('Wait for assembly' -ASSEMBLE->) : 'Wait' le plus souvent.
        static string ClickTarget(PlayMakerFSM f)
        {
            FsmState w = f.Fsm.GetState("Wait for assembly");
            if (w != null) foreach (FsmTransition t in w.Transitions) if (t.EventName == "ASSEMBLE") return t.ToState;
            return null;
        }

        // Point a plusieurs pieces (volant : GT, rallye, sport, stock sur trigger_steering_wheel) : 'Find correct part 2' compare la
        // piece en main a {Part1}..{Part4} (GameObjectCompare) et mene a 'Wait for assembly', 'Wait for assembly 2'... ; chaque branche
        // ('Wait' -> 'GT', 'Wait 4' -> 'Stock') pose SA piece et met Installed a SON entree de Database. Le rejeu par le clic de la
        // 1re branche montait le volant stock en le declarant GT : GT remonte au rechargement (Kamixak/Mikol, 24/10). Le montage
        // local par une autre branche n'etait pas annonce non plus (crochet sur la 1re seulement : rattrape par le controle).
        static string WaitStateFor(PlayMakerFSM f, GameObject part)
        {
            if (part == null) return null;
            foreach (FsmState s in f.FsmStates)
            {
                FsmStateAction[] acts;
                try { acts = s.Actions; } catch { continue; }
                if (acts == null) continue;
                foreach (FsmStateAction a in acts)
                {
                    if (a == null || a.GetType().Name != "GameObjectCompare") continue;   // (action d'Assembly-CSharp de MSC : par reflexion)
                    System.Reflection.FieldInfo cf = a.GetType().GetField("compareTo"), ef = a.GetType().GetField("equalEvent");
                    FsmGameObject c = cf != null ? cf.GetValue(a) as FsmGameObject : null;
                    FsmEvent e = ef != null ? ef.GetValue(a) as FsmEvent : null;
                    if (c == null || e == null || c.Value != part) continue;
                    foreach (FsmTransition t in s.Transitions) if (t.EventName == e.Name && t.ToState.StartsWith("Wait for assembly")) return t.ToState;
                }
            }
            return null;
        }
        static string ClickFor(MscPoint p, GameObject part)
        {
            string w = WaitStateFor(p.Fsm, part);
            FsmState ws = w != null ? p.Fsm.Fsm.GetState(w) : null;
            if (ws != null) foreach (FsmTransition t in ws.Transitions) if (t.EventName == "ASSEMBLE") return t.ToState;
            return p.Click;
        }

        static int MscConsider(PlayMakerFSM f)
        {
            if (waitingSet.Contains(f)) return 0;
            bool known;
            if (f.FsmName == "Screw") known = screws.Contains(f) || !IsMscBolt(f);
            else if (f.FsmName == "Removal") known = mscRemovals.Contains(f) || f.Fsm.GetState("Remove part") == null;
            else known = mscPointOf.ContainsKey(f) || !IsMscPoint(f);
            if (known) return 0;
            Replay.Claim(f, "pieces");
            if (MscTryHook(f)) return 1;
            waitingSet.Add(f);
            waiting.Add(f);
            return 0;
        }

        static bool MscTryHook(PlayMakerFSM f)
        {
            if (f.FsmName == "Screw")
            {
                FsmState s3 = f.Fsm.GetState("Wait 3"), s4 = f.Fsm.GetState("Wait 4");
                if (!s3.IsInitialized || !s4.IsInitialized) return false;
                var b = new Bolt { F = f, Key = BoltKey(f, f.FsmVariables.GetFsmGameObject("PartAssembled").Value) };
                if (!Inject(s3, new MscBoltHook { B = b }) || !Inject(s4, new MscBoltHook { B = b })) return false;
                screws.Add(f); bolts.Add(b);
                return true;
            }
            if (f.FsmName == "Removal")
            {
                FsmState rs = f.Fsm.GetState("Remove part");
                if (!rs.IsInitialized || !Inject(rs, new MscRemovalHook { F = f })) return false;
                mscRemovals.Add(f);
                if (f.GetComponent<PartWatch>() == null) f.gameObject.AddComponent<PartWatch>();
                return true;
            }
            bool swap = IsSwapPoint(f);
            // (echange : envoye a l'entree de 'Assemble' -- 'Check bolts' peut encore repartir en 'Drop part' ; rejoue en 'Assemble')
            string click = swap ? "Assemble" : ClickTarget(f);
            FsmState cs = click != null ? f.Fsm.GetState(click) : null;
            if (cs == null || !cs.IsInitialized) return false;
            var p = new MscPoint { Key = MscPointKey(f.transform), Fsm = f, Click = click, Swap = swap };
            // (toutes les branches d'un point a plusieurs pieces : 'Wait for assembly N' -ASSEMBLE-> 'Wait N')
            var hooked = new List<FsmState> { cs };
            if (!swap)
                foreach (FsmState s in f.FsmStates)
                {
                    if (!s.Name.StartsWith("Wait for assembly ")) continue;
                    foreach (FsmTransition t in s.Transitions)
                    {
                        FsmState ts = t.EventName == "ASSEMBLE" ? f.Fsm.GetState(t.ToState) : null;
                        if (ts == null || hooked.Contains(ts)) continue;
                        if (!ts.IsInitialized) return false;
                        hooked.Add(ts);
                    }
                }
            foreach (FsmState hs in hooked) if (!Inject(hs, new MscPointHook { P = p })) return false;
            mscPoints[p.Key] = p; mscPointOf[f] = p;
            return true;
        }

        static void MscScan()
        {
            int before = mscPoints.Count, beforeBolts = screws.Count, beforeRem = mscRemovals.Count;
            foreach (Object o in Game.AllFsms(0.5f))
            {
                var f = (PlayMakerFSM)o;
                if (f == null || f.hideFlags != HideFlags.None) continue;
                string n = f.FsmName;
                if (n == "Assembly" || n == "Removal" || n == "Screw") MscConsider(f);
                else if (n == "Use" && f.gameObject.activeInHierarchy) MscWheelHook(f);
            }
            if (mscPoints.Count != before || screws.Count != beforeBolts || mscRemovals.Count != beforeRem)
            {
                Log.Info("pieces (MSC) : " + mscPoints.Count + " points de montage, " + mscRemovals.Count + " pieces demontables, " + screws.Count + " vis ("
                         + waiting.Count + " automates en attente de leur activation)");
                if (mscPoints.Count != before) MscDbScan();   // (points « echange » rattaches a leur entree Database)
            }
        }

        static void MscCheckWaiting()
        {
            int n = 0;
            for (int i = waiting.Count - 1; i >= 0; i--)
            {
                PlayMakerFSM f = waiting[i];
                if (f == null) { waiting.RemoveAt(i); continue; }
                if (!f.gameObject.activeInHierarchy) continue;
                if (!MscTryHook(f)) continue;
                waiting.RemoveAt(i); waitingSet.Remove(f);
                n++;
            }
            if (n > 0) Log.Info("pieces (MSC) : " + n + " vis, pieces ou points accroches a leur activation");
        }

        // ---------------------------------------------------------------- montage, demontage
        static bool mscLostOnce;
        static void MscLocalInstall(MscPoint p)
        {
            GameObject part = p.Fsm.FsmVariables.GetFsmGameObject("Part").Value;
            if (!Session.Active || Time.realtimeSinceStartup - loadedAt < 20f || part == null) return;
            string id = MscPartKey(part);
            // Montage ici d'une piece qu'un autre tient, ou qu'il vient de monter AILLEURS (moins de 1,5 s) : pas le notre -- dossier #27
            // (Markos 30/10) : l'hote remontait les porte-fusibles du tableau electrique, la copie d'un porte-fusible qu'il tenait passait
            // dans un autre emplacement chez l'invite, un clic de l'invite l'y montait (« montage : fuseholder07 sur Fuse7 » 86 ms apres
            // « montee sur Fuse5 (joueur #0) ») : pieces sans courant chez l'un, pas chez l'autre. Defait ici (Removal), remise la ou
            // l'autre l'a montee, rien d'envoye. [Test] MontageAutreLibre=1 : comme avant.
            if (Config.GetInt("Test", "MontageAutreLibre", 0) == 0)
            {
                int holder = Props.RemoteHolder(part);
                RemoteMount rmt;
                bool elsewhere = remoteMounts.TryGetValue(part, out rmt) && Time.realtimeSinceStartup - rmt.At < 1.5f && rmt.Point != null && rmt.Point != p;
                if (holder >= 0 || elsewhere)
                {
                    PlayMakerFSM rem = Game.FsmOn(part, "Removal");
                    applying = true; Replay.Depth++;
                    try
                    {
                        if (rem != null && rem.Fsm.GetState("Remove part") != null) { rem.enabled = true; Game.SetState(rem, "Remove part"); }
                        // (et ce point-ci rendu vide : sinon il « tenait » encore la piece, et le controle de l'hote la retirait plus tard de
                        // la ou elle avait ete remise -- essai Autotest=fusibles FusibleCas=B)
                        FsmGameObject pp = p.Fsm.FsmVariables.FindFsmGameObject("Part");
                        if (pp != null && pp.Value == part) pp.Value = null;
                        if (!p.Fsm.gameObject.activeSelf) p.Fsm.gameObject.SetActive(true);
                        if (elsewhere) { if (!rmt.Point.Fsm.gameObject.activeSelf) rmt.Point.Fsm.gameObject.SetActive(true); MscApplyMount(rmt.Point, part, id, rmt.Point.Key, rmt.Who); }
                        else Props.HoldFromRemote(part, holder);
                    }
                    finally { applying = false; Replay.Depth--; }
                    Log.Warn("montage : " + id + " sur " + p.Key + " ici refuse : " + (elsewhere ? "montee sur " + rmt.Point.Key + " par #" + rmt.Who + " a l'instant, remise la-bas" : "tenue par #" + holder) + ", rien d'envoye");
                    return;
                }
            }
            Touch(H(p.Key));
            Log.Info("montage : " + id + " sur " + p.Key);
            VehicleSync.CalmAfterPart(p.Fsm.transform);
            mounting[part] = Time.realtimeSinceStartup + 1f; mountingAt[part] = p.Fsm.transform;
            // ([Test] PerteMontage=1 : le premier montage d'ici n'est pas envoye -- message perdu -- pour voir l'empreinte le rattraper, Autotest=mscperte)
            if (Config.GetInt("Test", "PerteMontage", 0) != 0 && !mscLostOnce) { mscLostOnce = true; Log.Warn("montage : " + id + " sur " + p.Key + " : message NON envoye (essai de perte)"); return; }
            Session.SendAll(new NetWriter(Msg.Part).U8(Session.LocalId).Bool(true).Str(p.Key).Str(id).U8(0), true);
        }

        // Montage recu (ou remis) : la piece posee sur son pivot puis l'automate du point mene au montage, comme le clic du joueur.
        class RemoteMount { public MscPoint Point; public int Who; public float At; }
        static readonly Dictionary<GameObject, RemoteMount> remoteMounts = new Dictionary<GameObject, RemoteMount>();
        static void MscApplyMount(MscPoint p, GameObject part, string id, string key, int who)
        {
            p.Fsm.FsmVariables.GetFsmGameObject("Part").Value = part;
            Props.ReleaseRemote(part);
            // A sa place sur son pivot ({Parent} : SetParent de 'Assemble 2') AVANT le joint de 'Set joint 2' : posee au
            // declencheur, le joint etait tendu au SetParent et cassait aussitot (force 1100 : portiere tombee chez l'invite).
            FsmGameObject pv = p.Fsm.FsmVariables.FindFsmGameObject("Parent");
            Transform at = pv != null && pv.Value != null ? pv.Value.transform : p.Fsm.transform;
            part.transform.position = at.position; part.transform.rotation = at.rotation;
            // Et cinematique, comme en main : 'Assemble 2' la rend libre (SetIsKinematic false). Libre pendant 'Wait 2', dans la
            // caisse figee ('Freeze car'), elle etait repoussee et son joint cassait (0 joint, portiere retombee chez l'invite).
            Rigidbody prb = part.GetComponent<Rigidbody>();
            if (prb != null) { if (!prb.isKinematic) { prb.velocity = Vector3.zero; prb.angularVelocity = Vector3.zero; } prb.isKinematic = true; }
            VehicleSync.CalmAfterPart(p.Fsm.transform);
            mounting[part] = Time.realtimeSinceStartup + 1f; mountingAt[part] = p.Fsm.transform;
            Trace.Watch(p.Fsm, "point " + key, 8f);
            PlayMakerFSM prem = Game.FsmOn(part, "Removal");
            if (prem != null) Trace.Watch(prem, "piece " + id, 8f);
            string click = ClickFor(p, part);
            Game.SetState(p.Fsm, click);
            Freshen(part, p.Fsm, id);
            Log.Info("piece " + id + " montee sur " + key + " (joueur #" + who + (click != p.Click ? ", branche " + click : "") + ")");
        }

        // ---------------------------------------------------------------- empreinte (lot D, JD 27/10 : « controle par empreinte toutes les 2 s, liste complete si ecart »)
        // Toutes les 2 s l'hote envoie deux empreintes (FNV des cles triees) : Installed de chaque entree Database (pieces a pivot, pieces
        // « echange », fils : 226) et cran de chaque vis active. L'invite calcule les siennes a la reception ; deux ecarts de suite (4 s), sans
        // geste local dans les 3 s (montage encore en route), et il demande la liste (@piecesliste) : l'hote renvoie tout de suite le controle
        // complet -- points et vis comme a 10 s, plus les Installed de la Database pour les pieces « echange » et les fils, que le controle des
        // points ne couvrait pas (une liste par 2 s au plus). Le controle des 10 s reste en secours.
        class MscDbEntry { public string Key; public uint Hash; public FsmBool Installed; public PlayMakerFSM Data; public bool Wire; public MscPoint Swap; public PlayMakerFSM Bolt; public PlayMakerArrayListProxy Crans; public bool BoltLooked; }
        static readonly List<MscDbEntry> mscDb = new List<MscDbEntry>();
        static Dictionary<uint, MscDbEntry> mscDbByHash;
        static float nextPrint, lastListAsk, lastListSent; static int printMiss, printLogs;
        static void MscDbScan()
        {
            GameObject db = Game.FindAny("Database");
            if (db == null) return;
            mscDb.Clear(); mscDbByHash = new Dictionary<uint, MscDbEntry>();
            foreach (Transform cat in db.transform)
                foreach (Transform e in cat)
                {
                    PlayMakerFSM d = Game.FsmOn(e.gameObject, "Data");
                    FsmBool inst = d != null ? d.FsmVariables.FindFsmBool("Installed") : null;
                    if (inst == null) continue;
                    var en = new MscDbEntry { Key = cat.name + "/" + e.name, Installed = inst, Data = d, Wire = cat.name == "DatabaseWiring" };
                    en.Hash = H(en.Key);
                    mscDb.Add(en); mscDbByHash[en.Hash] = en;
                }
            mscDb.Sort((a, b) => string.CompareOrdinal(a.Key, b.Key));
            // (piece « echange » : son point, sans {Parent}, porte l'entree Database en db_ThisPart)
            foreach (MscPoint p in mscPoints.Values)
            {
                if (!p.Swap || p.Fsm == null) continue;
                FsmGameObject tp = p.Fsm.FsmVariables.FindFsmGameObject("db_ThisPart");
                if (tp == null || tp.Value == null) continue;
                foreach (MscDbEntry en in mscDb) if (en.Data != null && en.Data.gameObject == tp.Value) { en.Swap = p; break; }
            }
        }
        static void MscPrints(out uint hi, out uint hb, out int ni, out int nb)
        {
            hi = 2166136261u; ni = 0;
            foreach (MscDbEntry en in mscDb)
            {
                if (en.Installed == null) continue;
                hi ^= en.Hash; hi *= 16777619u; hi ^= en.Installed.Value ? 1u : 0u; hi *= 16777619u; ni++;
            }
            // Vis : par le BoltCheck de la piece de chaque entree (Tightness et tableau des crans, ceux que le jeu sauve) -- pas par les automates
            // 'Screw' accroches ici, dont le nombre depend de l'avancement du releve de chaque cote (fausse alerte a l'arrivee, 27/10 08:32).
            hb = 2166136261u; nb = 0;
            foreach (MscDbEntry en in mscDb)
            {
                if (!en.BoltLooked || en.Bolt == null && en.Crans == null)
                {
                    en.BoltLooked = true;
                    FsmGameObject tp = en.Data != null ? en.Data.FsmVariables.FindFsmGameObject("ThisPart") : null;
                    GameObject part = tp != null ? tp.Value : null;
                    en.Bolt = part != null ? Game.FsmOn(part, "BoltCheck") : null;
                    en.Crans = null;
                    if (part != null) foreach (PlayMakerArrayListProxy px in part.GetComponents<PlayMakerArrayListProxy>()) if (en.Crans == null || string.IsNullOrEmpty(px.referenceName)) en.Crans = px;
                }
                if (en.Bolt == null) continue;
                FsmFloat tg = en.Bolt.FsmVariables.FindFsmFloat("Tightness");
                hb ^= en.Hash; hb *= 16777619u;
                hb ^= (uint)Mathf.RoundToInt(tg != null ? tg.Value : -1f); hb *= 16777619u;
                if (en.Crans != null && en.Crans._arrayList != null)
                    for (int i = 0; i < en.Crans._arrayList.Count; i++) { object o = en.Crans._arrayList[i]; hb ^= (uint)(o is int ? (int)o : o is float ? (int)(float)o : 0); hb *= 16777619u; }
                nb++;
            }
        }
        static bool TouchedWithin(float now, float within) { foreach (KeyValuePair<uint, float> kv in touched) if (now - kv.Value < within) return true; return false; }
        static void MscPrintTick(float now)
        {
            if (!Session.IsHost || !Session.Active || Session.RemoteCount == 0 || now < nextPrint || now - loadedAt < 30f) return;
            nextPrint = now + 2f;
            if (mscDb.Count == 0) MscDbScan();
            uint hi, hb; int ni, nb;
            MscPrints(out hi, out hb, out ni, out nb);
            Session.SendAll(new NetWriter(Msg.Job).U8(Session.LocalId).Str("@empreinte").I32((int)hi).I32((int)hb).U16(ni).U16(nb), false);
        }
        public static void OnPrint(int who, NetReader r)
        {
            float now = Time.realtimeSinceStartup;
            if (Session.IsHost || !PlayerSync.InGame || now - loadedAt < 30f) return;
            uint hi = (uint)r.I32(), hb = (uint)r.I32(); int ni = r.U16(), nb = r.U16();
            if (mscDb.Count == 0) MscDbScan();
            uint mi, mb; int ci, cb;
            MscPrints(out mi, out mb, out ci, out cb);
            if (mi == hi && mb == hb) { printMiss = 0; return; }
            if (TouchedWithin(now, 3f)) return;   // (geste encore en route ici : pas un ecart)
            printMiss++;
            if (printMiss < 2 || now - lastListAsk < 4f) return;
            lastListAsk = now; printMiss = 0;
            Session.SendToHost(new NetWriter(Msg.Job).U8(Session.LocalId).Str("@piecesliste"), true);
            if (printLogs++ < 30 || printLogs % 20 == 0)
                Log.Info("pieces (MSC) : empreinte differente de l'hote (" + (mi != hi ? "pieces montees " + ci + " ici / " + ni + " chez lui" : "") + (mi != hi && mb != hb ? ", " : "") + (mb != hb ? "vis " + cb + " / " + nb : "") + ") : liste complete demandee");
        }
        public static void OnListAsk(int who, NetReader r)
        {
            float now = Time.realtimeSinceStartup;
            if (!Session.IsHost || now - lastListSent < 2f) return;
            lastListSent = now;
            MscDigest(now);
            MscDigestDb();
            Log.Info("pieces (MSC) : liste complete renvoyee a la demande de #" + who);
        }
        // Controle, genre 2 : Installed des entrees Database des pieces « echange » et des fils (le reste passe par les points et les vis).
        static void MscDigestDb()
        {
            if (mscDb.Count == 0) MscDbScan();
            NetWriter w = null; int n = 0;
            foreach (MscDbEntry en in mscDb)
            {
                if (en.Installed == null || en.Swap == null && !en.Wire) continue;
                if (w == null) { w = new NetWriter(Msg.Job).U8(Session.LocalId).Str("@pieces").U8(2); n = 0; }
                w.I32((int)en.Hash).U8(en.Installed.Value ? (byte)1 : (byte)0); n++;
                if (n >= 120) { Session.SendAll(w, true); w = null; }
            }
            if (w != null) Session.SendAll(w, true);
        }
        static void MscOnDigestDb(int who, NetReader r, float now)
        {
            if (mscDb.Count == 0) MscDbScan();
            int fixedN = 0, unknown = 0;
            while (r.More)
            {
                uint h = (uint)r.I32(); bool inst = r.U8() != 0;
                MscDbEntry en;
                if (mscDbByHash == null || !mscDbByHash.TryGetValue(h, out en) || en.Installed == null) { unknown++; continue; }
                if (en.Installed.Value == inst || Recent(h, now)) continue;
                Touch(h);
                if (en.Wire)
                {
                    if (inst) { if (WiringMsc.ApplyByDb(en.Data.gameObject, who)) fixedN++; }
                    else Log.Warn("pieces (MSC) : fil " + en.Key + " branche ici, pas chez l'hote : laisse (debranchement = vis et piece)");
                    continue;
                }
                MscPoint p = en.Swap;
                if (p == null || p.Fsm == null) continue;
                applying = true; Replay.Depth++;
                try
                {
                    if (inst) { MscSwapInstall(p, null, "db:" + Recon.Path(en.Data.transform), p.Key, who); fixedN++; }
                    else
                    {
                        FsmGameObject tp = en.Data.FsmVariables.FindFsmGameObject("ThisPart");
                        PlayMakerFSM rem = tp != null && tp.Value != null ? Game.FsmOn(tp.Value, "Removal") : null;
                        if (rem != null) { rem.enabled = true; Game.SetState(rem, "Remove part"); fixedN++; Log.Info("piece " + en.Key + " demontee ici au controle de l'hote (echange)"); }
                    }
                }
                finally { applying = false; Replay.Depth--; }
            }
            if (fixedN > 0 || unknown > 0) Log.Info("pieces (MSC) : controle de l'hote (Database) : " + fixedN + " corrigee(s)" + (unknown > 0 ? ", " + unknown + " inconnue(s) ici" : ""));
        }

        // Attache (FixedJoint) d'une piece disparue ici alors que la voiture est menee par un autre : 'Mouse off' (HasComponent)
        // envoyait la piece en 'Remove part' -- detachee, Installed=false, par terre ici seulement (les 4 ailes et pare-chocs de la
        // SATSUMA chez l'invite qui venait d'en descendre, 8 pieces chez l'hote pendant 1,5 s de copie ; 20/10). Une vraie casse
        // chez celui qui la mene arrive par Part (rejouee avec applying) : ici la piece est remise a sa pose de montage (locale
        // nulle sous son pivot, comme 'Assemble 2'), une attache neuve posee, et l'automate renvoye en 'Mouse off' AVANT les
        // actions de 'Remove part' (SwitchLater). 'BoltCheck' recoit la nouvelle attache (sa variable Joint, lue a l'entree de
        // Bolts ON 2 seulement).
        static int keepLogs;
        static readonly Dictionary<Joint, Rigidbody> keptJoints = new Dictionary<Joint, Rigidbody>();
        static float nextKept;
        static void MscCheckKept(float now)
        {
            if (keptJoints.Count == 0 || now < nextKept) return;
            nextKept = now + 0.5f;
            List<Joint> done = null;
            foreach (KeyValuePair<Joint, Rigidbody> kv in keptJoints)
            {
                if (kv.Key != null && kv.Value != null && VehicleSync.Authority(kv.Value) != Session.LocalId) continue;
                if (kv.Key != null && float.IsInfinity(kv.Key.breakForce)) { kv.Key.breakForce = 200f; kv.Key.breakTorque = 200f; }
                if (done == null) done = new List<Joint>();
                done.Add(kv.Key);
            }
            if (done != null) foreach (Joint j in done) keptJoints.Remove(j);
        }
        static bool MscKeepOnCopy(PlayMakerFSM rem)
        {
            if (!Session.Active) return false;
            Transform car = VehicleSync.CarRoot(rem.transform);
            Rigidbody cb = car != null ? car.GetComponent<Rigidbody>() : null;
            if (cb == null || VehicleSync.Authority(cb) == Session.LocalId) return false;
            if (rem.GetComponent<FixedJoint>() != null || rem.transform.parent == null) return false;   // (decrochage {Detach} du jeu, pas une attache disparue)
            Rigidbody to = rem.transform.parent.GetComponentInParent<Rigidbody>();
            if (to == null) to = cb;
            string id = MscPartKey(rem.gameObject);
            Rigidbody prb = rem.GetComponent<Rigidbody>();
            rem.transform.localPosition = Vector3.zero; rem.transform.localRotation = Quaternion.identity;
            if (prb != null) { prb.velocity = Vector3.zero; prb.angularVelocity = Vector3.zero; if (!prb.isKinematic) { prb.position = rem.transform.position; prb.rotation = rem.transform.rotation; } }
            FixedJoint fj = rem.gameObject.AddComponent<FixedJoint>();
            fj.connectedBody = to;
            VehicleSync.AdoptJoint(cb, fj, new Vector2(200f, 200f));   // (InitialAttachForce du montage)
            // Voiture garee menee par l'autre, libre ici (pas une copie cinematique) : l'attache neuve a 200 recassait a chaque pas
            // tant que la piece restait pressee (pare-chocs contre la porte du garage apres une poussee : 60 casses en 1 s, force
            // 1,1e9, la caisse secouee ici -- partie de JD et Kristof, 24/10, rejoue par Autotest=mscmontage PousseA). Incassable
            // tant qu'un autre la mene ; ses limites quand elle redevient la notre (MscTick).
            if (!float.IsInfinity(fj.breakForce)) { fj.breakForce = Mathf.Infinity; fj.breakTorque = Mathf.Infinity; keptJoints[fj] = cb; }
            PlayMakerFSM bc = Game.FsmOn(rem.gameObject, "BoltCheck");
            FsmObject jv = bc != null ? bc.FsmVariables.FindFsmObject("Joint") : null;
            if (jv != null) jv.Value = fj;
            bool back = Game.SwitchLater(rem, "Mouse off");
            if (keepLogs++ < 40 || keepLogs % 50 == 0)
                Log.Warn("piece " + id + " : attache disparue ici sur la copie (voiture menee par #" + VehicleSync.Authority(cb) + "), remise sur " + to.name
                         + (prb != null ? ", corps " + (prb.isKinematic ? "cinematique" : "libre") : "") + (back ? "" : " (automate non renvoye en Mouse off)"));
            return back;
        }

        static void MscLocalRemove(PlayMakerFSM rem, bool breakoff)
        {
            if (!Session.Active || Time.realtimeSinceStartup - loadedAt < 20f) return;
            string id = MscPartKey(rem.gameObject);
            FsmGameObject trig = rem.FsmVariables.FindFsmGameObject("Trigger");
            string key = trig != null && trig.Value != null ? MscPointKey(trig.Value.transform) : "";
            if (key.Length > 0) Touch(H(key));
            if (breakoff)
            {
                // Casse : envoyee seulement par celui qui fait autorite sur la voiture (sa copie ici a pu s'emballer) -- comme MWC.
                Transform car = VehicleSync.CarRoot(rem.transform);
                Rigidbody cb = car != null ? car.GetComponent<Rigidbody>() : null;
                if (cb != null && VehicleSync.Authority(cb) != Session.LocalId)
                {
                    Rigidbody prb = rem.GetComponent<Rigidbody>();
                    Log.Warn("piece " + id + " cassee ici sous l'autorite d'un autre : pas envoyee (joints " + rem.GetComponents<Joint>().Length + ", parent "
                             + (rem.transform.parent != null ? rem.transform.parent.name : "aucun") + (prb != null ? ", corps " + (prb.isKinematic ? "cinematique" : "libre") + " v " + prb.velocity.magnitude.ToString("F1") : "") + ")");
                    return;
                }
                // (choc il y a moins de 2 s : 16 casses en 2 s -- avant, 3 par seconde meme dans un accident : la 4e casse n'etait jamais
                // envoyee et restait cassee ici seulement -- dossier #25, reponts 29/10 : batterie tombee chez l'invite, montee chez l'hote.
                // [Test] CasseRafaleAncienne=1 : comme avant)
                bool crash = CarHits.ImpactAgo(car) < WindowCrash && Config.GetInt("Test", "CasseRafaleAncienne", 0) == 0;
                if (TooMany(car != null ? car.name : key, Time.realtimeSinceStartup, crash ? MscBurstCrash : BurstCalm, crash ? WindowCrash : WindowCalm))
                { Log.Warn("piece " + id + " cassee : trop de casses" + (crash ? " malgre le choc" : "") + ", pas envoyee"); return; }
            }
            // (echange : la nouvelle piece libre n'existe qu'apres les actions de 'Remove part' -- envoye a l'image suivante, MscTick)
            if (rem.FsmVariables.FindFsmGameObject("SpawnPart") != null) { mscOut.Add(new MscOut { Rem = rem, Key = key, Id = id, Flags = breakoff ? 1 : 0 }); return; }
            Log.Info((breakoff ? "casse : " : "demontage : ") + id + " de " + key);
            Session.SendAll(new NetWriter(Msg.Part).U8(Session.LocalId).Bool(false).Str(key).Str(id).U8(breakoff ? 1 : 0), true);
            Props.SettleHere(rem.gameObject);
        }

        // Temoin sur chaque piece de MSC (24/10, partie de JD et Kristof : aile gauche cassee 0,05 a 0,3 s apres chaque montage chez
        // l'hote, tableau de bord casse a la pose des compteurs) : a la rupture de son attache, la force et les derniers contacts
        // (autre collisionneur, vitesse relative, il y a combien) -- ce qui l'a poussee.
        public class PartWatch : MonoBehaviour
        {
            readonly string[] what = new string[6]; readonly float[] at = new float[6]; int next;
            void OnCollisionEnter(Collision c)
            {
                if (c == null || c.collider == null) return;
                Rigidbody o = c.rigidbody;
                what[next] = Recon.Path(c.collider.transform) + (o == null ? " (fixe)" : o.isKinematic ? " (cinematique)" : "") + " " + (c.relativeVelocity.magnitude * 3.6f).ToString("F0") + " km/h";
                at[next] = Time.realtimeSinceStartup; next = (next + 1) % what.Length;
            }
            public float LastBreak = -100f;
            void OnJointBreak(float force) { LastBreak = Time.realtimeSinceStartup; try { OnPartJointBreak(this, force); } catch (System.Exception e) { Replay.HookError(e); } }
            public string Recent(float within)
            {
                var sb = new System.Text.StringBuilder();
                float now = Time.realtimeSinceStartup;
                for (int i = 0; i < what.Length; i++)
                    if (what[i] != null && now - at[i] < within) sb.Append(sb.Length > 0 ? " ; " : "").Append(what[i]).Append(" il y a ").Append((now - at[i]).ToString("F2")).Append(" s");
                return sb.Length > 0 ? sb.ToString() : "aucun";
            }
        }
        static int breakLogs;
        static void OnPartJointBreak(PartWatch w, float force)
        {
            if (!Session.Active || breakLogs++ >= 60) return;
            Joint j = w.GetComponent<Joint>();
            Rigidbody prb = w.GetComponent<Rigidbody>();
            Transform car = VehicleSync.CarRoot(w.transform);
            Rigidbody cb = car != null ? car.GetComponent<Rigidbody>() : null;
            Log.Info("piece " + MscPartKey(w.gameObject) + " : attache rompue, force " + force.ToString("F0") + (j != null ? " (limite " + j.breakForce.ToString("F0") + ")" : "")
                     + (prb != null ? ", piece v " + prb.velocity.magnitude.ToString("F2") + (prb.isKinematic ? " cin" : "") : "")
                     + (cb != null ? ", voiture v " + cb.velocity.magnitude.ToString("F2") + " rot " + cb.angularVelocity.magnitude.ToString("F2") + (cb.isKinematic ? " cin" : "") + " menee par #" + VehicleSync.Authority(cb) : "")
                     + " ; contacts 1 s : " + w.Recent(1f));
        }

        // Essais ([Test] Autotest=mscmontage) : clic de montage comme celui du joueur -- la piece tenue (cinematique) au declencheur,
        // 'Wait for assembly', puis ASSEMBLE a l'image suivante.
        static PlayMakerFSM testClick; static int testClickFrame;
        // Essais (Autotest=fusibles, dossier #27) : l'hote demonte les porte-fusibles FusibleA (07) et FusibleB (03) a FusibleT0 s (120) ;
        // FusibleCas=A : il tient A en main de T0+4 a T0+12 et l'invite, des qu'il voit A tenu, le monte (clic) a la place de B ;
        // FusibleCas=B : l'hote remonte A a sa place a T0+8 et l'invite, des qu'il recoit ce montage, monte A a la place de B.
        // L'hote remonte A a sa place a T0+12 (cas A). Chacun note les 7 emplacements a T0+25 (invite : sa propre horloge, FusibleDecalage 70).
        static int fStep, fGuest; static string fSlotA, fSlotB; static bool fLogged;
        static string FuseSlot(string partKey, out bool installed)
        {
            installed = false;
            GameObject part = MscFindPart(partKey);
            PlayMakerFSM rem = part != null ? Game.FsmOn(part, "Removal") : null;
            FsmGameObject trig = rem != null ? rem.FsmVariables.FindFsmGameObject("Trigger") : null;
            if (trig == null || trig.Value == null) return null;
            installed = !trig.Value.activeSelf;
            return MscPointKey(trig.Value.transform);
        }
        public static string FuseState()
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 1; i <= 7; i++)
            {
                bool inst; string s = FuseSlot("id:fuseholder0" + i, out inst);
                sb.Append("0").Append(i).Append(inst && s != null ? "@" + s.Substring(s.LastIndexOf('/') + 1) : " libre").Append(' ');
            }
            return sb.ToString();
        }
        public static void TestFuses(float t)
        {
            string a = Config.Get("Test", "FusibleA", "id:fuseholder07"), b = Config.Get("Test", "FusibleB", "id:fuseholder03");
            string cas = Config.Get("Test", "FusibleCas", "A");
            float t0 = Config.GetFloat("Test", "FusibleT0", 120f) - (Session.IsHost ? 0f : Config.GetFloat("Test", "FusibleDecalage", 70f));
            bool ia;
            if (fSlotA == null && t > Mathf.Min(30f, t0 - 5f)) { fSlotA = FuseSlot(a, out ia); fSlotB = FuseSlot(b, out ia); Log.Info("autotest : fusibles, " + a + " sur " + fSlotA + ", " + b + " sur " + fSlotB + " ; " + FuseState()); }
            if (Session.IsHost)
            {
                if (fStep == 0 && t > t0)
                {
                    fStep = 1;
                    foreach (string k in new[] { a, b })
                    {
                        GameObject part = MscFindPart(k); PlayMakerFSM rem = part != null ? Game.FsmOn(part, "Removal") : null;
                        if (rem != null) { rem.enabled = true; Game.SetState(rem, "Remove part"); }
                    }
                    Log.Info("autotest : fusibles, " + a + " et " + b + " demontes");
                }
                if (fStep == 1 && cas == "A" && t > t0 + 4f) { fStep = 2; Log.Info("autotest : fusibles, " + a + " en main : " + (Props.TestHold(Props.FindKey(a.Substring(a.IndexOf(':') + 1)), PlayerSync.LocalCamera) != null)); }
                if ((fStep == 1 && cas == "B" && t > t0 + 8f) || (fStep == 2 && t > t0 + 12f))
                {
                    if (fStep == 2) Props.TestHold(Props.FindKey(a.Substring(a.IndexOf(':') + 1)), null);
                    fStep = 3; Log.Info("autotest : fusibles, " + a + " remonte : " + MscTestClick(fSlotA, a));
                }
            }
            else if (fGuest == 0 && fSlotB != null && t > t0)
            {
                GameObject pa = MscFindPart(a);
                RemoteMount rmt;
                bool go = cas == "A" ? pa != null && Props.RemoteHolder(pa) >= 0 : pa != null && remoteMounts.TryGetValue(pa, out rmt) && Time.realtimeSinceStartup - rmt.At < 0.3f;
                if (go) { fGuest = 1; Log.Info("autotest : fusibles, invite monte " + a + " sur " + fSlotB + " (cas " + cas + ") : " + MscTestClick(fSlotB, a)); }
            }
            if (!fLogged && t > t0 + 25f) { fLogged = true; Log.Info("autotest : fusibles, etat final : " + FuseState()); }
        }

        public static string MscTestClick(string pointKey, string partKey)
        {
            MscPoint p;
            if (!mscPoints.TryGetValue(pointKey, out p) || p.Fsm == null) { MscScan(); mscPoints.TryGetValue(pointKey, out p); }
            if (p == null || p.Fsm == null) return "point " + pointKey + " introuvable";
            GameObject part = MscFindPart(partKey) ?? Props.ObjectOf("w:" + partKey);
            if (part == null) return "piece " + partKey + " introuvable";
            if (!p.Fsm.gameObject.activeInHierarchy) return "point " + pointKey + " eteint";
            Rigidbody prb = part.GetComponent<Rigidbody>();
            if (prb != null) { prb.velocity = Vector3.zero; prb.angularVelocity = Vector3.zero; prb.isKinematic = true; }
            FsmGameObject pv = p.Fsm.FsmVariables.FindFsmGameObject("Parent");
            part.transform.position = p.Fsm.transform.position;
            part.transform.rotation = pv != null && pv.Value != null ? pv.Value.transform.rotation : p.Fsm.transform.rotation;
            p.Fsm.FsmVariables.GetFsmGameObject("Part").Value = part;
            Game.SetState(p.Fsm, WaitStateFor(p.Fsm, part) ?? "Wait for assembly");   // (branche de la piece, comme 'Find correct part 2')
            testClick = p.Fsm; testClickFrame = Time.frameCount + 2;
            return "clic de montage de " + partKey + " sur " + pointKey;
        }
        // [Test] Autotest=mscvolant (24/10, volant stock declare GT) : le joueur [Test] Monteur demonte le volant stock a 40 s et le
        // remonte a 50 s (branche 'Wait for assembly 4') ; a 62 s ([Test] VolantFin) chacun note le volant monte et les Installed.
        static int volStep;
        public static void TestVolant(float t)
        {
            const string point = "SATSUMA(557kg, 248)/Dashboard/Steering/CarSteeringPivot/steering column(xxxxx)/pivot_steering_wheel";
            bool me = Session.LocalId == Config.GetInt("Test", "Monteur", 0);
            if (volStep == 0 && t > 40f)
            {
                volStep = 1;
                GameObject part = MscFindPart("db:Database/DatabaseMechanics/SteeringWheel");
                PlayMakerFSM rem = part != null ? Game.FsmOn(part, "Removal") : null;
                // (comme le joueur : 'Mouse over' -REMOVE->, pas un decrochage depuis 'Mouse off')
                if (me && rem != null) { rem.enabled = true; Game.SetState(rem, "Mouse over"); rem.SendEvent("REMOVE"); }
                Log.Info("autotest mscvolant : " + (me ? "demontage du volant stock (" + (rem != null ? "Removal " + rem.ActiveStateName : "introuvable") + ")" : "attente du demontage"));
            }
            if (volStep == 1 && t > 50f)
            {
                volStep = 2;
                string pk = null;
                foreach (KeyValuePair<string, MscPoint> kv in mscPoints) if (kv.Key.EndsWith("trigger_steering_wheel")) pk = kv.Key;
                Log.Info("autotest mscvolant : point " + (pk ?? "?") + (me && pk != null ? ", " + MscTestClick(pk, "db:Database/DatabaseMechanics/SteeringWheel") : ""));
            }
            if (volStep == 2 && t > Config.GetFloat("Test", "VolantFin", 62f))
            {
                volStep = 3;
                var sb = new System.Text.StringBuilder("autotest mscvolant : ");
                foreach (string db in new[] { "Database/DatabaseMechanics/SteeringWheel", "Database/DatabaseOrders/SteeringWheelGT", "Database/DatabaseOrders/SportWheel", "Database/DatabaseOrders/Rally Wheel" })
                {
                    GameObject g = Game.FindAny(db);
                    PlayMakerFSM d = g != null ? Game.FsmOn(g, "Data") : null;
                    FsmBool inst = d != null ? d.FsmVariables.FindFsmBool("Installed") : null;
                    sb.Append(db.Substring(db.LastIndexOf('/') + 1)).Append(" Installed=").Append(inst != null ? inst.Value.ToString() : "?").Append(" ; ");
                }
                Transform piv = Game.FindAny(point) != null ? Game.FindAny(point).transform : null;
                sb.Append("sur le pivot : ");
                if (piv != null) foreach (Transform c in piv) sb.Append(c.name).Append(' ');
                Log.Info(sb.ToString());
            }
        }

        // [Test] Autotest=mscremonte (WIGA 25/10 : aile montee chez l'hote, arrachee 50 ms apres, force 1e9) : le joueur [Test] Monteur
        // demonte [Test] Piece (Fender_Left) comme le joueur a 40 s, la remonte sur son point ([Test] Point, fin du chemin) a 50 s ;
        // a 60 s chacun note si elle est montee (les casses sont notees par PartWatch).
        static int remStep;
        public static void TestRemount(float t)
        {
            string partKey = Config.Get("Test", "Piece", "db:Database/DatabaseBody/Fender_Left"), pointEnd = Config.Get("Test", "Point", "trigger_fender_left");
            bool me = Session.LocalId == Config.GetInt("Test", "Monteur", 0);
            t -= Config.GetFloat("Test", "RemonteA", 40f) - 40f;   // ([Test] RemonteA=s : demontage a s secondes, 40 par defaut ; remontage 10 s apres)
            if (remStep == 0 && t > 40f)
            {
                remStep = 1;
                GameObject part = MscFindPart(partKey);
                PlayMakerFSM rem = part != null ? Game.FsmOn(part, "Removal") : null;
                if (me && rem != null) { rem.enabled = true; Game.SetState(rem, "Mouse over"); rem.SendEvent("REMOVE"); }
                Log.Info("autotest mscremonte : " + (me ? "demontage de " + partKey + " (" + (rem != null ? "Removal " + rem.ActiveStateName : "introuvable") + ")" : "attente du demontage"));
            }
            string pk = null;
            foreach (KeyValuePair<string, MscPoint> kv in mscPoints) if (kv.Key.EndsWith(pointEnd)) pk = kv.Key;
            if (remStep == 1 && t > 50f)
            {
                remStep = 2;
                Log.Info("autotest mscremonte : point " + (pk ?? "?") + (me && pk != null ? ", " + MscTestClick(pk, partKey) : ""));
            }
            if (remStep == 2 && t > 60f)
            {
                remStep = 3;
                Log.Info("autotest mscremonte : " + (pk != null ? MscTestHolds(pk) : "point introuvable"));
            }
        }

        // Essais (Autotest=mscmontage, [Test] MontageVisser=1) : serre ici, comme le joueur (envoye aux autres), toutes les vis de la
        // piece 'partKey' jusqu'au cran 'target'.
        public static string MscTestTighten(string partKey, int target)
        {
            int n = 0, ok = 0;
            foreach (Bolt b in bolts)
            {
                if (b.F == null || !b.F.gameObject.activeInHierarchy || MscPartKey(b.F.FsmVariables.GetFsmGameObject("PartAssembled").Value) != partKey) continue;
                n++;
                FsmInt st = b.F.FsmVariables.GetFsmInt("Stage");
                for (int guard = 0; st.Value < target && guard < 10; guard++) b.F.SendEvent("TIGHTEN");
                if (st.Value >= target) ok++;
            }
            return n + " vis de " + partKey + ", " + ok + " au cran " + target;
        }
        // Essais : attaches de la piece (limite de casse) et crans de ses vis, ici.
        public static string MscTestPartState(string partKey)
        {
            GameObject part = MscFindPart(partKey);
            if (part == null) return "piece introuvable";
            var sb = new System.Text.StringBuilder();
            foreach (Joint j in part.GetComponents<Joint>()) sb.Append(sb.Length > 0 ? "," : "").Append(j.GetType().Name).Append(" ").Append(float.IsInfinity(j.breakForce) ? "inf" : j.breakForce.ToString("F0"));
            if (sb.Length == 0) sb.Append("sans attache");
            sb.Append(part.GetComponent<Rigidbody>() != null && part.GetComponent<Rigidbody>().isKinematic ? " (cinematique)" : "").Append(", vis");
            foreach (Bolt b in bolts)
                if (b.F != null && MscPartKey(b.F.FsmVariables.GetFsmGameObject("PartAssembled").Value) == partKey) sb.Append(" ").Append(b.F.FsmVariables.GetFsmInt("Stage").Value);
            PlayMakerFSM bc = Game.FsmOn(part, "BoltCheck");
            FsmFloat tg = bc != null ? bc.FsmVariables.FindFsmFloat("Tightness") : null;
            if (tg != null) sb.Append(", serrage ").Append(tg.Value.ToString("F0")).Append(" [").Append(bc.ActiveStateName).Append("]");
            return sb.ToString();
        }

        public static string MscTestHolds(string pointKey)
        {
            MscPoint p;
            if (!mscPoints.TryGetValue(pointKey, out p) || p.Fsm == null) return "point introuvable";
            string id = MscInstalledId(p);
            return id.Length > 0 ? "monte : " + id : "vide (etat " + p.Fsm.ActiveStateName + ")";
        }
        static void MscTestClickTick()
        {
            if (testClick == null || Time.frameCount < testClickFrame) return;
            PlayMakerFSM f = testClick; testClick = null;
            Log.Info("autotest : ASSEMBLE dans " + f.ActiveStateName);
            f.SendEvent("ASSEMBLE");
        }

        class MscOut { public PlayMakerFSM Rem; public string Key, Id; public int Flags; }
        static readonly List<MscOut> mscOut = new List<MscOut>();
        static int mscOutSerial;

        // Demontage d'une piece « echange » fait ici : la nouvelle piece libre ({Part} de 'Removal') recoit une cle commune.
        static void MscTick()
        {
            if (wheelCut.Count > 0) { foreach (FsmStateAction a in wheelCut) a.Enabled = true; wheelCut.Clear(); }
            if (testClick != null) MscTestClickTick();
            MscCheckKept(Time.realtimeSinceStartup);
            MscPrintTick(Time.realtimeSinceStartup);
            if (mscOut.Count == 0) return;
            foreach (MscOut o in mscOut)
            {
                if (o.Rem == null) continue;
                FsmGameObject pv = o.Rem.FsmVariables.FindFsmGameObject("Part");
                GameObject loose = pv != null ? pv.Value : null;
                string outKey = "";
                if (loose != null && loose != o.Rem.gameObject)
                {
                    outKey = "w:msc-out/" + o.Id + "#" + Session.LocalId + "-" + (++mscOutSerial);
                    Props.Adopt(loose, outKey);
                    Props.SettleHere(loose);
                }
                Log.Info(((o.Flags & 1) != 0 ? "casse : " : "demontage : ") + o.Id + " de " + o.Key + (outKey.Length > 0 ? ", piece libre " + outKey : ", pas de piece libre"));
                Session.SendAll(new NetWriter(Msg.Part).U8(Session.LocalId).Bool(false).Str(o.Key).Str(o.Id).U8(o.Flags).Str(outKey), true);
            }
            mscOut.Clear();
        }

        // Montage « echange » de l'autre : 'Assemble' allume la piece montee d'ici et detruit la copie de la piece libre (si elle est ici).
        static void MscSwapInstall(MscPoint p, GameObject part, string id, string key, int who)
        {
            if (!p.Fsm.gameObject.activeInHierarchy)
            {
                Log.Info("piece " + id + " deja montee sur " + key);
                if (part != null && Props.Vanish(id)) Log.Info("piece " + id + " : copie libre d'ici cachee");
                return;
            }
            if (part != null) Props.ReleaseRemote(part);
            p.Fsm.FsmVariables.GetFsmGameObject("Part").Value = part;
            VehicleSync.CalmAfterPart(p.Fsm.transform);
            Trace.Watch(p.Fsm, "point " + key, 8f);
            Game.SetState(p.Fsm, p.Click);
            if (part != null) Props.Vanish(id);   // (detruite par 'Assemble' : plus suivie)
            Log.Info("piece " + id + " montee (echange) sur " + key + " (joueur #" + who + (part == null ? ", pas de copie libre ici" : "") + ")");
        }

        static bool MscHolds(PlayMakerFSM point, GameObject part)
        {
            FsmGameObject p = point.FsmVariables.FindFsmGameObject("Part");
            if (p == null || p.Value != part || part == null) return false;
            if (!point.gameObject.activeSelf) return true;   // (declencheur eteint par 'End' : piece montee)
            // 'End' eteint parfois un GROUPE de declencheurs et pas le declencheur lui-meme (SATSUMA/MiscParts/Triggers Exhaust Pipes :
            // tuyau d'echappement, silencieux) : la piece y etait toujours « pas montee », des deux cotes, et le controle ne voyait
            // jamais qu'elle manquait chez l'invite (essai feux 27/10 : echappement demonte chez l'invite arrive pendant que l'hote
            // etait au volant, jamais remonte, empreinte differente toutes les 4 s). Montee aussi : enfant direct de son pivot ({Parent},
            // SetParent de 'Assemble 2').
            FsmGameObject pv = point.FsmVariables.FindFsmGameObject("Parent");
            return pv != null && pv.Value != null && part.transform.parent == pv.Value.transform;
        }

        static void MscOnMessage(Peer from, NetReader r)
        {
            int who = r.U8();
            if (Session.IsHost) who = from.Id;
            bool install = r.Bool();
            string key = r.Str(), id = r.Str();
            int flags = r.More ? r.U8() : 0;
            string outKey = r.More ? r.Str() : "";
            Touch(H(key));
            if (Session.IsHost)
            {
                var fw = new NetWriter(Msg.Part).U8(who).Bool(install).Str(key).Str(id).U8(flags);
                if (outKey.Length > 0) fw.Str(outKey);
                Session.Broadcast(fw, true, who);
            }
            // (piece libre « echange » : pas d'automate, retrouvee par Props sous la meme cle)
            GameObject part = MscFindPart(id) ?? Props.ObjectOf(id);
            applying = true; Replay.Depth++;
            try
            {
                if (install)
                {
                    MscPoint p;
                    if (!mscPoints.TryGetValue(key, out p) || p.Fsm == null) { MscScan(); mscPoints.TryGetValue(key, out p); }
                    if (p == null || p.Fsm == null) { Log.Warn("point " + key + " introuvable ici"); return; }
                    if (p.Swap) { MscSwapInstall(p, part, id, key, who); return; }
                    if (part == null) { Log.Warn("piece " + id + " introuvable ici"); return; }
                    if (MscHolds(p.Fsm, part)) { Log.Info("piece " + id + " deja montee"); return; }
                    if (!p.Fsm.gameObject.activeInHierarchy) { Log.Warn("point " + key + " eteint ici (deja occupe ?) : " + id + " pas montee"); return; }
                    MscApplyMount(p, part, id, key, who);
                    remoteMounts[part] = new RemoteMount { Point = p, Who = who, At = Time.realtimeSinceStartup };
                }
                else
                {
                    if (part == null) { Log.Warn("piece " + id + " introuvable ici"); return; }
                    PlayMakerFSM rem = Game.FsmOn(part, "Removal");
                    if (rem == null) { Log.Warn("piece " + id + " sans Removal ici"); return; }
                    FsmGameObject trig = rem.FsmVariables.FindFsmGameObject("Trigger");
                    if (trig != null && trig.Value != null && trig.Value.activeSelf) { Log.Info("piece " + id + " deja demontee"); return; }
                    // Casse (flags 1) : d'abord 'Break off' comme chez celui qui mene (BoltCheck, ou 'Use' d'une roue) -- Bolted=false, crans a
                    // zero, Tightness 0, Detach. Avant : 'Remove part' seul, la piece par terre ici mais encore « serree » (audit Satsuma 27/10).
                    if ((flags & 1) != 0)
                        foreach (PlayMakerFSM bf in part.GetComponents<PlayMakerFSM>())
                            if ((bf.FsmName == "BoltCheck" || bf.FsmName == "Use") && bf.Fsm != null && bf.Fsm.GetState("Break off") != null) Game.SetState(bf, "Break off");
                    rem.enabled = true;
                    Game.SetState(rem, "Remove part");
                    // (echange : la nouvelle piece libre d'ici prend la cle de celle de l'autre et suit sa chute)
                    FsmGameObject lv = rem.FsmVariables.FindFsmGameObject("SpawnPart") != null ? rem.FsmVariables.FindFsmGameObject("Part") : null;
                    GameObject loose = lv != null && lv.Value != null && lv.Value != part ? lv.Value : null;
                    if (loose != null && outKey.Length > 0) { Props.Adopt(loose, outKey); Props.HoldFromRemote(loose, who); }
                    else Props.HoldFromRemote(part, who);
                    Log.Info("piece " + id + ((flags & 1) != 0 ? " cassee de " : " demontee de ") + key + " (joueur #" + who + ")");
                }
            }
            finally { applying = false; Replay.Depth--; }
        }

        // ---------------------------------------------------------------- vis
        static void MscLocalBolt(Bolt b)
        {
            if (!Session.Active || Time.realtimeSinceStartup - loadedAt < 20f) return;
            string part = MscPartKey(b.F.FsmVariables.GetFsmGameObject("PartAssembled").Value);
            if (part.Length == 0) return;
            Touch(H(part + "|" + b.Key));
            int stage = b.F.FsmVariables.GetFsmInt("Stage").Value;
            Session.SendAll(new NetWriter(Msg.Bolt).U8(Session.LocalId).Str(part).Str(b.Key).U8(Mathf.Clamp(stage, 0, 255)), true);
        }

        static Bolt MscFindBolt(string part, string key)
        {
            foreach (Bolt b in bolts)
                if (b.F != null && b.Key == key && MscPartKey(b.F.FsmVariables.GetFsmGameObject("PartAssembled").Value) == part) return b;
            return null;
        }


        // ---------------------------------------------------------------- controle periodique, My Summer Car (23/10)
        // Meme principe que sur MWC (Parts.Digest) : points 'Assembly' (hors points « echange », sans piece libre a renvoyer) et vis
        // 'Screw' (Stage). Piece en trop ici -> 'Remove part' de son 'Removal' ; piece manquante -> demandee, l'hote renvoie le
        // montage (Msg.Part comme MscLocalInstall) ; vis au Stage de l'hote.
        static Dictionary<uint, MscPoint> mscPointsByHash; static int mscPointsByHashN = -1;
        static string MscBoltKeyOf(Bolt b) { return MscPartKey(b.F.FsmVariables.GetFsmGameObject("PartAssembled").Value) + "|" + b.Key; }
        static void MscHashMaps()
        {
            if (mscPointsByHash == null || mscPointsByHashN != mscPoints.Count)
            {
                mscPointsByHash = new Dictionary<uint, MscPoint>();
                foreach (KeyValuePair<string, MscPoint> kv in mscPoints) mscPointsByHash[H(kv.Key)] = kv.Value;
                mscPointsByHashN = mscPoints.Count;
            }
            if (boltsByHash == null || boltsByHashN != bolts.Count)
            {
                boltsByHash = new Dictionary<uint, Bolt>();
                foreach (Bolt b in bolts) if (b.F != null) boltsByHash[H(MscBoltKeyOf(b))] = b;
                boltsByHashN = bolts.Count;
            }
        }
        static string MscInstalledId(MscPoint p)
        {
            FsmGameObject pv = p.Fsm.FsmVariables.FindFsmGameObject("Part");
            GameObject part = pv != null ? pv.Value : null;
            return part != null && MscHolds(p.Fsm, part) ? MscPartKey(part) : "";
        }
        static int lastDigestMounted = -1;
        static void MscDigest(float now)
        {
            NetWriter w = null; int n = 0, total = 0, totalB = 0, mounted = 0;
            foreach (KeyValuePair<string, MscPoint> kv in mscPoints)
            {
                MscPoint p = kv.Value;
                if (p.Fsm == null || p.Swap) continue;
                string id = MscInstalledId(p);
                if (id.Length > 0) mounted++;
                if (w == null) { w = new NetWriter(Msg.Job).U8(Session.LocalId).Str("@pieces").U8(0); n = 0; }
                uint hk = H(p.Key);
                w.I32((int)hk).Str(id).F32(Age(hk, now)); n++; total++;
                // (coupe aussi a la taille : 40 points aux cles longues d'une SATSUMA montee depassaient Transport.MaxPayload,
                // l'envoi levait et tout le controle de ce tour etait perdu, vis comprises -- 24/10)
                if (n >= 40 || w.Length > 900) { Session.SendAll(w, true); w = null; }
            }
            if (w != null) Session.SendAll(w, true);
            w = null; n = 0;
            foreach (Bolt b in bolts)
            {
                if (b.F == null || !b.F.gameObject.activeInHierarchy) continue;
                FsmInt st = b.F.FsmVariables.FindFsmInt("Stage"); if (st == null) continue;
                if (w == null) { w = new NetWriter(Msg.Job).U8(Session.LocalId).Str("@pieces").U8(1); n = 0; }
                uint hb = H(MscBoltKeyOf(b));
                w.I32((int)hb).U8(Mathf.Clamp(st.Value, 0, 255)).F32(Age(hb, now)); n++; totalB++;
                if (n >= 100) { Session.SendAll(w, true); w = null; }
            }
            if (w != null) Session.SendAll(w, true);
            // (nombre de points montes : journal aussi quand il change -- une chute sans geste de joueur = etat de l'hote faux)
            if (digestLogs++ < 3 || now >= lastDigestLog + 120f || mounted != lastDigestMounted) { lastDigestLog = now; Log.Info("pieces (MSC) : controle envoye aux invites (" + total + " points dont " + mounted + " montes, " + totalB + " vis)"); }
            lastDigestMounted = mounted;
        }
        static float lastWouldLog = -100f;
        static void MscOnDigest(int who, NetReader r, int kind, float now)
        {
            MscHashMaps();
            List<int> ask = null; int removed = 0, fixedBolts = 0, unknown = 0, resent = 0;
            if (kind == 0)
            {
                var rows = new List<KeyValuePair<uint, KeyValuePair<string, float>>>();
                while (r.More) { uint h0 = (uint)r.I32(); string id0 = r.Str(); float a0 = r.F32(); rows.Add(new KeyValuePair<uint, KeyValuePair<string, float>>(h0, new KeyValuePair<string, float>(id0, a0))); }
                int would = 0;
                System.Text.StringBuilder wl = null;
                foreach (var row in rows)
                {
                    MscPoint q;
                    if (!mscPointsByHash.TryGetValue(row.Key, out q) || q.Fsm == null || q.Swap || Recent(row.Key, now)) continue;
                    FsmGameObject qv = q.Fsm.FsmVariables.FindFsmGameObject("Part");
                    GameObject qp = qv != null ? qv.Value : null;
                    string qm = MscInstalledId(q);
                    if (qm.Length == 0 || qm == row.Value.Key || mounting.ContainsKey(qp) || PendingReinstall(qp)) continue;
                    if (!(Age(row.Key, now) + 0.5f < row.Value.Value && row.Value.Value > 0f))
                    {
                        would++;
                        if (wl == null) wl = new System.Text.StringBuilder();
                        if (would <= 20) wl.Append(' ').Append(q.Key).Append(" [ici ").Append(qm).Append(", hote \"").Append(row.Value.Key).Append("\"]");
                    }
                }
                // (quels points : cause du controle de l'hote qui demonte une SATSUMA montee, Markos 24/10 -- une fois par minute)
                if (would > 0 && now >= lastWouldLog + 60f) { lastWouldLog = now; Log.Info("pieces (MSC) : controle de l'hote : " + would + " point(s) monte(s) ici, pas chez l'hote :" + wl); }
                bool trusted = DigestTrusted(would, now);
                foreach (var row in rows)
                {
                    uint h = row.Key; string id = row.Value.Key; float hostAge = row.Value.Value;
                    MscPoint p;
                    if (!mscPointsByHash.TryGetValue(h, out p) || p.Fsm == null) { unknown++; continue; }
                    if (Recent(h, now) || p.Swap) continue;
                    FsmGameObject pv = p.Fsm.FsmVariables.FindFsmGameObject("Part");
                    GameObject part = pv != null ? pv.Value : null;
                    string mine = MscInstalledId(p);
                    if (mine == id) continue;
                    if (mine.Length > 0 && (mounting.ContainsKey(part) || PendingReinstall(part))) continue;
                    if (Age(h, now) + 0.5f < hostAge && hostAge > 0f)
                    {
                        // Mon geste est le plus recent : l'hote se met a jour (montage comme MscLocalInstall, demontage comme MscLocalRemove)
                        if (mine.Length > 0) Session.SendAll(new NetWriter(Msg.Part).U8(Session.LocalId).Bool(true).Str(p.Key).Str(mine).U8(0), true);
                        else if (id.Length > 0) Session.SendAll(new NetWriter(Msg.Part).U8(Session.LocalId).Bool(false).Str(p.Key).Str(id).U8(0), true);
                        Log.Info("piece " + (mine.Length > 0 ? mine + " montee" : id + " demontee") + " ici sur " + p.Key + ", plus recent que chez l'hote : renvoye");
                        Touch(h); resent++;
                        continue;
                    }
                    if (mine.Length > 0 && !trusted) continue;   // (controle pas fiable : rien de demonte, rien demande)
                    if (mine.Length > 0)
                    {
                        PlayMakerFSM rem = Game.FsmOn(part, "Removal");
                        if (rem != null)
                        {
                            VehicleSync.CalmAfterPart(rem.transform);
                            applying = true; Replay.Depth++;
                            try { rem.enabled = true; Game.SetState(rem, "Remove part"); }
                            finally { applying = false; Replay.Depth--; }
                            Props.HoldFromRemote(part, who);
                            Log.Info("piece " + mine + " demontee de " + p.Key + " au controle de l'hote : pas montee chez lui" + (id.Length > 0 ? " (il y a " + id + ")" : ""));
                            removed++;
                        }
                    }
                    if (id.Length > 0 && !AskedLately(h, now)) { if (ask == null) ask = new List<int>(); if (ask.Count < 20) ask.Add((int)h); }
                    Touch(h);
                }
                if (ask != null)
                {
                    var w = new NetWriter(Msg.Job).U8(Session.LocalId).Str("@piecesdemande");
                    foreach (int h in ask) w.I32(h);
                    Session.SendToHost(w, true);
                }
            }
            else if (kind == 2) MscOnDigestDb(who, r, now);
            else
            {
                while (r.More)
                {
                    uint h = (uint)r.I32(); int target = r.U8(); float hostAge = r.F32();
                    Bolt b;
                    if (!boltsByHash.TryGetValue(h, out b) || b.F == null) { unknown++; continue; }
                    if (Recent(h, now) || !b.F.gameObject.activeInHierarchy) continue;
                    FsmInt st = b.F.FsmVariables.FindFsmInt("Stage");
                    if (st == null || st.Value == target) continue;
                    string part = MscPartKey(b.F.FsmVariables.GetFsmGameObject("PartAssembled").Value);
                    if (Age(h, now) + 0.5f < hostAge && hostAge > 0f)
                    {
                        Session.SendAll(new NetWriter(Msg.Bolt).U8(Session.LocalId).Str(part).Str(b.Key).U8(Mathf.Clamp(st.Value, 0, 255)), true);
                        Log.Info("vis " + part + "/" + b.Key + " = " + st.Value + " ici, plus recent que chez l'hote (" + target + ") : renvoyee");
                        Touch(h); resent++;
                        continue;
                    }
                    if (MscApplyBolt(who, part, b.Key, target, true)) { fixedBolts++; Touch(h); }
                }
            }
            int asked = ask != null ? ask.Count : 0;
            if (removed + fixedBolts + asked + resent > 0)
                Log.Info("pieces (MSC) : controle de l'hote : " + removed + " demontee(s) ici, " + asked + " demandee(s), " + fixedBolts + " vis reglee(s), " + resent + " geste(s) d'ici plus recent(s) renvoye(s)" + (unknown > 0 ? ", " + unknown + " inconnue(s) ici" : ""));
        }
        static void MscOnDigestAsk(int who, NetReader r)
        {
            MscHashMaps();
            int n = 0;
            while (r.More && n < 20)
            {
                uint h = (uint)r.I32(); n++;
                MscPoint p;
                if (!mscPointsByHash.TryGetValue(h, out p) || p.Fsm == null || p.Swap) continue;
                string id = MscInstalledId(p);
                if (id.Length == 0) continue;
                Session.Broadcast(new NetWriter(Msg.Part).U8(Session.LocalId).Bool(true).Str(p.Key).Str(id).U8(0), true);
                Log.Info("pieces (MSC) : montage de " + id + " sur " + p.Key + " renvoye a la demande de #" + who);
            }
        }

        static bool MscApplyBolt(int who, string part, string key, int target, bool last)
        {
            Bolt b = MscFindBolt(part, key);
            if (b == null || !b.F.gameObject.activeInHierarchy)
            {
                if (last) Log.Warn("vis " + part + "/" + key + (b == null ? " introuvable ici (piece pas montee ici ?)" : " inactive ici"));
                return false;
            }
            FsmInt st = b.F.FsmVariables.GetFsmInt("Stage");
            applying = true; Replay.Depth++;
            try
            {
                for (int guard = 0; st.Value != target && guard < 10; guard++)
                    b.F.SendEvent(st.Value < target ? "TIGHTEN" : "UNTIGHTEN");
            }
            finally { applying = false; Replay.Depth--; }
            if (st.Value != target)
            {
                if (last) Log.Warn("vis " + part + "/" + key + " : " + st.Value + " au lieu de " + target);
                return false;
            }
            Log.Info("vis " + part + "/" + key + " = " + target + " (joueur #" + who + ")");
            return true;
        }
    }
}
