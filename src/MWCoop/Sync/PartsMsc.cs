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
        class MscPoint { public string Key; public PlayMakerFSM Fsm; public string Click; public bool Swap; public bool Checked; }
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
            public bool AfterCheck;   // (etat atteint par 'Check bolts' -ASSEMBLE-> : CheckedTarget)
            public override void OnEnter()
            {
                try
                {
                    FsmState prev = P.Fsm.Fsm.PreviousActiveState;
                    // (« Wait for assembly 2 », « 3 »... : points a plusieurs pieces, ClickFor)
                    if (!applying && Replay.Depth == 0 && prev != null && (AfterCheck ? prev.Name == "Check bolts" : prev.Name.StartsWith("Wait for assembly") || P.Swap && prev.Name == "Check bolts")) MscLocalInstall(P);
                }
                catch (System.Exception e) { Replay.HookError(e); }
                Finish();
            }
        }

        // En tete de 'Set joint' / 'Set joint 2' d'un point (desync du 02/11, JD et GG : un verrou du 'Use' -- FixedJoint relie a la
        // SATSUMA, 'State 1' 12000 ou 'Set lock 2' -- ou une vieille garde de MscKeepOnCopy survivait a 'Remove part', qui ne detruit que la
        // charniere ; au remontage, 'Assemble 2' (SetJointConnectedBody : le PREMIER joint) rebranchait ce vieux joint a la voiture et la
        // charniere neuve restait ancree au monde -- « door right(Clone) ancre au monde (HingeJoint) ... ecart 0.91 m » chez l'invite, et
        // BoltCheck serrait le mauvais joint). Ici les attaches de la piece vers la voiture du point ou vers le monde sont retirees avant
        // l'AddComponent du jeu, en local comme en rejoue. [Test] JointsPerimes=0 : comme avant.
        class MscJointHook : ModHook
        {
            public override string Module { get { return "pieces"; } }
            public MscPoint P;
            public override void OnEnter()
            {
                try { MscDropStaleJoints(P); } catch (System.Exception e) { Replay.HookError(e); }
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
                    float rr;
                    if (remoteRemoval.Count > 0 && remoteRemoval.TryGetValue(F, out rr))
                    {
                        remoteRemoval.Remove(F);
                        if (Time.realtimeSinceStartup < rr) { Finish(); return; }   // (housse : demontage recu, rejoue a la mise a jour suivante)
                    }
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
                    // (la piece sort vraiment ici, geste d'ici ou rejoue : ses verrous vers la voiture avec elle -- MscDropCarJoints)
                    if (verrouDemontage && Session.Active && HingeRemoval(F)) MscDropCarJoints(F, "demontage");
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
        static int wheelLogs, chanceLogs, chanceEntries;
        class WheelChanceHook : ModHook
        {
            public override string Module { get { return Bolt ? "pieces (casses)" : "pieces (roues)"; } }
            public PlayMakerFSM F;
            public bool Bolt;   // (BoltCheck d'une piece, pas la roue)
            public override void OnEnter()
            {
                try
                {
                    if (Bolt) chanceEntries++;
                    // (relecture audit MSC 02/11 : une entree rejouee -- Replay.Depth > 0 -- ne tire jamais, meme chez le conducteur : un
                    // 'Chance' venu d'un autre faisait un tirage de plus par joueur ; une vraie casse arrive par Part, droit en 'Break off')
                    if (Session.Active && (Replay.Depth > 0 || !VehicleSync.SimAuthority(F.transform)))
                    {
                        foreach (FsmStateAction a in State.Actions) if (a != this && a.Enabled) { a.Enabled = false; wheelCut.Add(a); }
                        if (!Bolt && wheelLogs++ < 10) global::MWCoop.Log.Info("roue " + Recon.Path(F.transform) + " : chute tiree au sort chez celui qui mene la voiture, pas ici");
                        if (Bolt && (chanceLogs++ < 10 || chanceLogs % 100 == 0)) global::MWCoop.Log.Info("piece " + Recon.Path(F.transform) + " : casse tiree au sort chez celui qui mene la voiture, pas ici");
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

        // Meme tirage sur les BoltCheck des pieces (audit MSC 02/11) : carbu, bloc moteur, pistons, phares, pompes, amortisseurs,
        // demi-arbres, triangles, fusees... -- 42 automates : 'Bolts ON' / 'State 1' -BREAK-> (EngineTemp > 81, RPM > 2500, SpeedKMH
        // > 50 : globales, recues du conducteur sur la copie) 'Chance' (SendRandomEvent, poids (32 - Tightness)/100) -> 'Break off'
        // (Tightness et crans a zero, Removal.Detach, feu sous le capot pour le carbu). Chacun tirait de son cote : casse chez un seul
        // joueur -- pas envoyee par la copie (autorite), puis au controle suivant la piece etait demontee chez celui qui conduisait ;
        // pour les pieces « echange » (amortisseur, demi-arbre...) le decrochage {Detach} partait comme un demontage ordinaire. Seul
        // celui qui mene la voiture tire ; sa casse arrive aux autres par Part (flags 1, 'Break off' rejoue). [Test] ChanceConducteur=0 :
        // comme avant.
        static bool chanceConducteur = true;
        static void MscChanceHook(PlayMakerFSM f)
        {
            if (!chanceConducteur || wheelHooked.Contains(f) || f.Fsm == null || f.Fsm.GetState("Break off") == null) return;
            FsmState s = f.Fsm.GetState("Chance");
            if (s == null || !s.IsInitialized) return;
            bool random = false, finished = false;
            foreach (FsmStateAction a in s.Actions) if (a != null && a.GetType().Name == "SendRandomEvent") { random = true; break; }
            foreach (FsmTransition t in s.Transitions) if (t.EventName == "FINISHED") { finished = true; break; }   // ('Chance' -FINISHED-> 'Wait' : la sortie sans casse)
            if (!random || !finished || !Inject(s, new WheelChanceHook { F = f, Bolt = true })) return;
            wheelHooked.Add(f);
        }

        // Essais (audit MSC 02/11, casse tiree par le seul conducteur) : BoltCheck de la piece 'partKey' (db:Database/...) au serrage
        // 'tightness', mis en 'Chance' ici : etat d'arrivee (Wait = pas tire ici), Detach de son Removal, autorite.
        public static string TestChance(string partKey, float tightness)
        {
            GameObject part = MscFindPart(partKey);
            PlayMakerFSM bc = part != null ? Game.FsmOn(part, "BoltCheck") : null;
            if (bc == null || bc.Fsm.GetState("Chance") == null) return "BoltCheck de " + partKey + " introuvable (ou sans 'Chance')";
            MscChanceHook(bc);
            FsmFloat tg = bc.FsmVariables.FindFsmFloat("Tightness");
            if (tg != null) tg.Value = tightness;
            bc.enabled = true;
            Game.SetState(bc, "Chance");
            PlayMakerFSM rem = Game.FsmOn(part, "Removal");
            FsmBool dt = rem != null ? rem.FsmVariables.FindFsmBool("Detach") : null;
            return partKey + " : 'Chance' -> " + bc.ActiveStateName + ", serrage " + (tg != null ? tg.Value.ToString("F0") : "?") + ", Detach " + (dt != null ? dt.Value.ToString() : "?")
                   + ", crochet " + wheelHooked.Contains(bc) + ", autorite ici " + VehicleSync.SimAuthority(bc.transform)
                   + ", suivi par les quetes " + Jobs.Tracks(bc) + ", entrees en 'Chance' " + chanceEntries;
        }

        static void MscReset()
        {
            wheelHooked.Clear(); wheelCut.Clear();
            chanceConducteur = Config.GetInt("Test", "ChanceConducteur", 1) != 0;
            housses = Config.GetInt("Test", "HoussesDemontage", 1) != 0;
            libreCle = Config.GetInt("Test", "LibreCleDb", 1) != 0;
            // (desync du 02/11, JD et GG : une cle de repli par correction, lues ici comme les autres)
            casseCopieGeste = Config.GetInt("Test", "CasseCopieGeste", 1) != 0;
            gardeCharniere = Config.GetInt("Test", "GardeCharniere", 1) != 0;
            jointsPerimes = Config.GetInt("Test", "JointsPerimes", 1) != 0;
            verrouDemontage = Config.GetInt("Test", "VerrouDemontage", 1) != 0;
            tenueInstalled = Config.GetInt("Test", "TenueInstalled", 1) != 0;
            montageAvorte = Config.GetInt("Test", "MontageAvorte", 1) != 0;
            empreinteListe = Config.GetInt("Test", "EmpreinteListeComplete", 1) != 0;
            calandrePivot = Config.GetInt("Test", "CalandrePivot", 1) != 0;
            casseJournal = Config.GetInt("Test", "CasseJournalLimite", 1) != 0;
            apresControle = Config.GetInt("Test", "MontageApresControle", 1) != 0;
            detachRemise = Config.GetInt("Test", "DetachRemise", 1) != 0;
            casseApresMontage = Config.GetInt("Test", "CasseApresMontage", 1) != 0;
            copyAsked.Clear(); hostWaits.Clear(); hingeRemovals.Clear(); keptForce.Clear(); pendingRemovals.Clear(); dbDiffLogged.Clear(); casseStep = 0;
            remoteRemoval.Clear(); mscIn.Clear(); listLogs = 0;
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
            if (f.FsmName != "Assembly" || f.FsmVariables.FindFsmGameObject("Part") == null || WaitState(f) == null) return false;
            return Game.RootName(f.transform) != "ITEMS";   // (piles de la radio : ItemFsms)
        }

        static bool IsMscBolt(PlayMakerFSM f)
        {
            return f.FsmName == "Screw" && f.FsmVariables.FindFsmInt("Stage") != null && f.FsmVariables.FindFsmGameObject("PartAssembled") != null
                   && f.Fsm.GetState("Wait 3") != null && f.Fsm.GetState("Wait 4") != null;
        }

        // Point « echange » : ni {Parent} ni 'Assemble 2', un etat 'Assemble' (la piece montee est deja dans la voiture).
        // Sauf si 'Assemble' rattache la piece elle-meme (SetParent) sur un {Pivot1} : calandre de la SATSUMA (trigger_grille, 'Wait for
        // assembly' -ASSEMBLE-> 'Wait' -> 'Assemble', branche GT 'Wait 2' -> 'Assemble 3'), un point ordinaire. Prise pour un point « echange »
        // (desync du 02/11, JD et GG) : crochet sur 'Assemble' dont l'etat d'avant est 'Wait', jamais envoyee ; sautee par le controle des
        // points et sans entree au controle Database (pas de db_ThisPart) -- calandre montee et vissee chez l'hote, par terre chez l'invite,
        // 16 « vis Grille ... introuvable ici » et l'empreinte differente 3 min. Parmi les 81 points « echange » du vidage, seule la calandre a
        // un {Pivot1} (SetParent aussi dans trigger_disc -- RadioMsc --, TriggerDiskette et le couvercle du seau : laisses). [Test] CalandrePivot=0 :
        // comme avant.
        static bool calandrePivot = true;
        static bool IsSwapPoint(PlayMakerFSM f)
        {
            if (f.FsmVariables.FindFsmGameObject("Parent") != null || f.Fsm.GetState("Assemble 2") != null) return false;
            FsmState a = f.Fsm.GetState("Assemble");
            if (a == null) return false;
            if (calandrePivot && f.FsmVariables.FindFsmGameObject("Pivot1") != null)
            {
                FsmStateAction[] acts = null;
                try { acts = a.Actions; } catch { }
                if (acts != null) foreach (FsmStateAction x in acts) if (x != null && x.GetType().Name == "SetParent") return false;
            }
            return true;
        }

        // Etat ou mene le clic ('Wait for assembly' -ASSEMBLE->) : 'Wait' le plus souvent.
        // Point sans 'Wait for assembly' mais avec 'Wait for assembly 2' (SATSUMA/MiscParts/trigger_battery : 'Check shoes' ->
        // 'Wait for assembly 2' -ASSEMBLE-> 'Assemble') : celui-la. Avant, jamais releve -- la batterie posee dans la voiture
        // n'etait pas envoyee, seul son demontage l'etait (dossier #35, квазарик 31/10). [Test] BatterieAncienne=1 : comme avant.
        static FsmState WaitState(PlayMakerFSM f)
        {
            FsmState w = f.Fsm.GetState("Wait for assembly");
            if (w == null && Config.GetInt("Test", "BatterieAncienne", 0) == 0)
                foreach (FsmState s in f.FsmStates) if (s.Name.StartsWith("Wait for assembly ")) { w = s; break; }
            return w;
        }
        static string ClickTarget(PlayMakerFSM f)
        {
            FsmState w = WaitState(f);
            if (w != null) foreach (FsmTransition t in w.Transitions) if (t.EventName == "ASSEMBLE") return t.ToState;
            return null;
        }

        // Point a plusieurs pieces (volant : GT, rallye, sport, stock sur trigger_steering_wheel) : 'Find correct part 2' compare la
        // piece en main a {Part1}..{Part4} (GameObjectCompare) et mene a 'Wait for assembly', 'Wait for assembly 2'... ; chaque branche
        // ('Wait' -> 'GT', 'Wait 4' -> 'Stock') pose SA piece et met Installed a SON entree de Database. Le rejeu par le clic de la
        // 1re branche montait le volant stock en le declarant GT : GT remonte au rechargement (Kamixak/Mikol, 24/10). Le montage
        // local par une autre branche n'etait pas annonce non plus (crochet sur la 1re seulement : rattrape par le controle).
        // Le radiateur (trigger_radiator : stock / sport) compare le NOM de la piece en main (StringCompare a {HandChild2}=racing
        // radiator(Clone), PART2 -> 'Wait for assembly 2') : pas reconnu, le montage du radiateur sport par l'autre etait rejoue par la
        // branche stock -- radiateur normal monte ici, le sport detruit (dossier #36, xZOTIK 31/10). [Test] RadiateurAncien=1 : comme avant.
        static string WaitStateFor(PlayMakerFSM f, GameObject part)
        {
            if (part == null) return null;
            bool byName = Config.GetInt("Test", "RadiateurAncien", 0) == 0;
            foreach (FsmState s in f.FsmStates)
            {
                FsmStateAction[] acts;
                try { acts = s.Actions; } catch { continue; }
                if (acts == null) continue;
                foreach (FsmStateAction a in acts)
                {
                    if (a == null) continue;
                    string tn = a.GetType().Name;
                    bool match;
                    System.Reflection.FieldInfo cf = a.GetType().GetField("compareTo"), ef = a.GetType().GetField("equalEvent");
                    if (tn == "GameObjectCompare")   // (action d'Assembly-CSharp de MSC : par reflexion)
                    {
                        FsmGameObject c = cf != null ? cf.GetValue(a) as FsmGameObject : null;
                        match = c != null && c.Value == part;
                    }
                    else if (tn == "StringCompare" && byName)
                    {
                        FsmString c = cf != null ? cf.GetValue(a) as FsmString : null;
                        match = c != null && c.Value == part.name;
                    }
                    else continue;
                    FsmEvent e = ef != null ? ef.GetValue(a) as FsmEvent : null;
                    if (!match || e == null) continue;
                    foreach (FsmTransition t in s.Transitions) if (t.EventName == e.Name && t.ToState.StartsWith("Wait for assembly")) return t.ToState;
                }
            }
            return null;
        }
        static string ClickFor(MscPoint p, GameObject part)
        {
            string w = WaitStateFor(p.Fsm, part);
            FsmState ws = w != null ? p.Fsm.Fsm.GetState(w) : null;
            if (ws != null) foreach (FsmTransition t in ws.Transitions) if (t.EventName == "ASSEMBLE") return (p.Checked ? CheckedTarget(p.Fsm, t.ToState) : null) ?? t.ToState;
            return p.Click;
        }

        // Clic qui mene par FINISHED a un 'Check bolts' a deux sorties (ASSEMBLE -> 'Assemble', DISASSEMBLE -> 'Drop part') : pistons
        // (vilebrequin serre ?), compteurs (tableau de bord), plateau d'embrayage. Le crochet sur 'Wait' envoyait le montage AVANT ce
        // controle (desync du 02/11, JD et GG) : piston clique sur un vilebrequin pas serre -- refuse par le jeu, le joueur jure, 'Drop part'
        // arme {Detach} du vilebrequin -- envoye quand meme ; chez l'autre 'Wait' -> 'Check bolts' -> 'Drop part' rejoue, vilebrequin decroche
        // une 2e fois en course avec la casse recue, {Detach} reste arme sur un Removal eteint et le vilebrequin retombait tout seul au
        // remontage suivant (« casse : Crankshaft » 26 ms apres « Crankshaft montee »). Ces points sont accroches et rejoues en 'Assemble'
        // (la cible d'ASSEMBLE de 'Check bolts'), envoyes a son entree depuis 'Check bolts' comme les points « echange » : rien ne part si le jeu
        // passe par 'Drop part'. null : point ordinaire. [Test] MontageApresControle=0 : comme avant (et DetachRemise=0, MscApplyMount).
        static bool apresControle = true, detachRemise = true;
        static string CheckedTarget(PlayMakerFSM f, string click)
        {
            FsmState cs = click != null ? f.Fsm.GetState(click) : null;
            if (cs == null || cs.Transitions.Length != 1 || cs.Transitions[0].EventName != "FINISHED" || cs.Transitions[0].ToState != "Check bolts") return null;
            FsmState cb = f.Fsm.GetState("Check bolts");
            string to = null; bool drop = false;
            if (cb != null)
                foreach (FsmTransition t in cb.Transitions)
                {
                    if (t.EventName == "ASSEMBLE") to = t.ToState;
                    else if (t.EventName == "DISASSEMBLE") drop = true;
                }
            return drop && to != null && f.Fsm.GetState(to) != null ? to : null;
        }

        // Housses de siege, de volant, de tableau de bord (audit MSC 02/11) : leur 'Removal' n'a pas de 'Remove part' mais 'Mouse over'
        // -REMOVE-> 'What part' (ou 'What part 2') qui lit les Installed de ses 4 entrees (zebre, leopard, peluche, suomi) et mene a
        // 'Remove part N' (Installed=false, housse libre neuve par CreateObject dans {Part}). Jamais accrochees : housse enlevee chez un
        // seul joueur, l'autre la sauvait montee, la housse posee ensuite trouvait son point eteint chez lui (« deja occupe ? ») et
        // l'empreinte differait pour de bon. [Test] HoussesDemontage=0 : comme avant.
        static bool housses = true;
        static FsmState WhatPart(PlayMakerFSM f)
        {
            foreach (FsmState s in f.FsmStates) if (s.Name.StartsWith("What part")) return s;
            return null;
        }
        static bool MultiRemoval(PlayMakerFSM f)
        {
            return f.Fsm != null && f.Fsm.GetState("Remove part") == null && f.Fsm.GetState("Remove part 1") != null && WhatPart(f) != null;
        }
        // Housse montee ici d'apres la Database : une des entrees db_ThisPart1..4 du 'Removal' a Installed vrai ('What part' n'a pas
        // d'autre sortie -- sans housse montee, l'automate y resterait bloque).
        static bool MultiInstalled(PlayMakerFSM rem)
        {
            for (int i = 1; i <= 4; i++)
            {
                FsmGameObject db = rem.FsmVariables.FindFsmGameObject("db_ThisPart" + i);
                PlayMakerFSM d = db != null && db.Value != null ? Game.FsmOn(db.Value, "Data") : null;
                FsmBool inst = d != null ? d.FsmVariables.FindFsmBool("Installed") : null;
                if (inst != null && inst.Value) return true;
            }
            return false;
        }
        // Demontage d'une housse recu : 'What part' peut ne passer en 'Remove part N' qu'a la mise a jour suivante de l'automate --
        // marque 1 s pour que le crochet ne le renvoie pas, et la housse libre neuve prend la cle recue des qu'elle existe (MscTick).
        static readonly Dictionary<PlayMakerFSM, float> remoteRemoval = new Dictionary<PlayMakerFSM, float>();
        class MscIn { public PlayMakerFSM Rem; public GameObject Before; public string Key; public int Who; public float Until; }
        static readonly List<MscIn> mscIn = new List<MscIn>();
        static void MscCheckIn(float now)
        {
            for (int i = mscIn.Count - 1; i >= 0; i--)
            {
                MscIn m = mscIn[i];
                FsmGameObject lv = m.Rem != null ? m.Rem.FsmVariables.FindFsmGameObject("Part") : null;
                GameObject loose = lv != null ? lv.Value : null;
                if (loose != null && loose != m.Before && loose != m.Rem.gameObject)
                {
                    Props.Adopt(loose, m.Key); Props.HoldFromRemote(loose, m.Who);
                    mscIn.RemoveAt(i);
                }
                else if (m.Rem == null || now > m.Until) mscIn.RemoveAt(i);
            }
        }

        // Essais (audit MSC 02/11, housses) : 'which' = wheel, seatd, seatp ou dash ; 'remove' : demontage comme le joueur ('Mouse over'
        // -REMOVE->). Etat : Installed des 4 entrees, housse montee allumee, point allume, housses libres suivies par Props.
        public static string TestCover(string which, bool remove)
        {
            string name = which == "seatd" ? "seat cover(xxxx1)" : which == "seatp" ? "seat cover(xxxx2)" : which == "dash" ? "dash cover(xxxxx)" : "wheel cover(xxxxx)";
            PlayMakerFSM rem = null;
            foreach (Object o in Game.FsmsNamed("Removal", 0.5f)) { var f = o as PlayMakerFSM; if (f != null && f.hideFlags == HideFlags.None && f.gameObject.name == name) { rem = f; break; } }
            if (rem == null) return "housse " + name + " introuvable";
            if (remove && rem.gameObject.activeInHierarchy) { rem.enabled = true; Game.SetState(rem, "Mouse over"); rem.SendEvent("REMOVE"); }
            var sb = new System.Text.StringBuilder("housse " + name + " (" + rem.ActiveStateName + ", " + (rem.gameObject.activeSelf ? "allumee" : "eteinte") + ", accrochee " + mscRemovals.Contains(rem) + ") :");
            for (int i = 1; i <= 4; i++)
            {
                FsmGameObject db = rem.FsmVariables.FindFsmGameObject("db_ThisPart" + i);
                PlayMakerFSM d = db != null && db.Value != null ? Game.FsmOn(db.Value, "Data") : null;
                FsmBool inst = d != null ? d.FsmVariables.FindFsmBool("Installed") : null;
                FsmGameObject sp = d != null ? d.FsmVariables.FindFsmGameObject("SpawnThis") : null;
                if (d == null) continue;
                sb.Append(' ').Append(db.Value.name).Append(" Installed=").Append(inst != null ? inst.Value.ToString() : "?");
                if (sp != null && sp.Value != null && sp.Value.activeInHierarchy) sb.Append(" libre ").Append(Props.KnownWorldKey(sp.Value) ?? "sans cle");
                sb.Append(" ;");
            }
            FsmGameObject trig = rem.FsmVariables.FindFsmGameObject("Trigger");
            sb.Append(" point ").Append(trig != null && trig.Value != null ? (trig.Value.activeSelf ? "allume" : "eteint") : "?");
            return sb.ToString();
        }

        static int MscConsider(PlayMakerFSM f)
        {
            if (waitingSet.Contains(f)) return 0;
            bool known;
            if (f.FsmName == "Screw") known = screws.Contains(f) || !IsMscBolt(f);
            else if (f.FsmName == "Removal") known = mscRemovals.Contains(f) || f.Fsm.GetState("Remove part") == null && !(housses && MultiRemoval(f));
            else known = mscPointOf.ContainsKey(f) || !IsMscPoint(f);
            // (couvercle du seau de kilju : a Home sur My Summer Car -- pris ici d'abord chez l'hote, par Home chez l'invite selon l'ordre
            // des releves : deux modules differents pour le meme automate, audit MSC 02/11 ; [Test] KiljuMsc=0 : comme avant)
            if (f.FsmName == "Removal" && !known && Home.KiljuMsc && f.gameObject.name.StartsWith("bucket lid")) known = true;
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
                if (rs != null) { if (!rs.IsInitialized || !Inject(rs, new MscRemovalHook { F = f })) return false; }
                else
                {
                    // (housse : une branche 'Remove part N' par housse possible, toutes accrochees)
                    var rn = new List<FsmState>();
                    foreach (FsmState s in f.FsmStates) if (s.Name.StartsWith("Remove part ")) rn.Add(s);
                    foreach (FsmState s in rn) if (!s.IsInitialized) return false;
                    foreach (FsmState s in rn) if (!Inject(s, new MscRemovalHook { F = f })) return false;
                }
                mscRemovals.Add(f);
                if (f.GetComponent<PartWatch>() == null) f.gameObject.AddComponent<PartWatch>();
                return true;
            }
            bool swap = IsSwapPoint(f);
            // (echange : envoye a l'entree de 'Assemble' -- 'Check bolts' peut encore repartir en 'Drop part' ; rejoue en 'Assemble')
            string click = swap ? "Assemble" : ClickTarget(f);
            // (clic suivi d'un 'Check bolts' : de meme, CheckedTarget)
            string checkedTo = !swap && apresControle ? CheckedTarget(f, click) : null;
            if (checkedTo != null) click = checkedTo;
            FsmState cs = click != null ? f.Fsm.GetState(click) : null;
            if (cs == null || !cs.IsInitialized) return false;
            var p = new MscPoint { Key = MscPointKey(f.transform), Fsm = f, Click = click, Swap = swap, Checked = checkedTo != null };
            // (toutes les branches d'un point a plusieurs pieces : 'Wait for assembly N' -ASSEMBLE-> 'Wait N')
            var hooked = new List<FsmState> { cs };
            var afterCheck = new List<FsmState>();
            if (checkedTo != null) afterCheck.Add(cs);
            if (!swap)
                foreach (FsmState s in f.FsmStates)
                {
                    if (!s.Name.StartsWith("Wait for assembly ")) continue;
                    foreach (FsmTransition t in s.Transitions)
                    {
                        string bt = t.EventName == "ASSEMBLE" && p.Checked ? CheckedTarget(f, t.ToState) : null;
                        FsmState ts = t.EventName == "ASSEMBLE" ? f.Fsm.GetState(bt ?? t.ToState) : null;
                        if (ts == null || hooked.Contains(ts)) continue;
                        if (!ts.IsInitialized) return false;
                        hooked.Add(ts);
                        if (bt != null) afterCheck.Add(ts);
                    }
                }
            foreach (FsmState hs in hooked) if (!Inject(hs, new MscPointHook { P = p, AfterCheck = afterCheck.Contains(hs) })) return false;
            // (attaches perimees retirees avant 'Set joint' : MscJointHook)
            if (!swap && jointsPerimes)
                foreach (FsmState s in f.FsmStates)
                    if (s.Name.StartsWith("Set joint") && s.IsInitialized) Inject(s, new MscJointHook { P = p });
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
                else if (n == "BoltCheck" && f.gameObject.activeInHierarchy) MscChanceHook(f);
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
            // ({Detach} laisse arme par un 'Drop part' rejoue -- vilebrequin, desync du 02/11 : 'End' rallume le Removal, 'Mouse off' -REMOVE->
            // aussitot et la piece qu'on monte retombait ici en casse envoyee. Une piece qu'on monte n'a jamais de {Detach} a garder.
            // [Test] DetachRemise=0 : comme avant)
            FsmBool pdt = detachRemise && prem != null ? prem.FsmVariables.FindFsmBool("Detach") : null;
            if (pdt != null && pdt.Value) { pdt.Value = false; Log.Warn("piece " + id + " : Detach arme ici remis a zero avant montage"); }
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
            MscAdoptLibre();   // (pieces libres « echange » : cle commune, audit MSC 02/11)
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
            // (journal bride comme chez l'invite : un ecart que la liste ne corrige pas -- entree hors controle -- la redemandait toutes les
            // 4 s, une ligne a chaque fois sans fin chez l'hote ; audit MSC 02/11)
            if (listLogs++ < 30 || listLogs % 50 == 0) Log.Info("pieces (MSC) : liste complete renvoyee a la demande de #" + who + (listLogs > 30 ? " (" + listLogs + " fois)" : ""));
        }
        static int listLogs;
        // Controle, genre 2 : Installed des entrees Database des pieces « echange » et des fils (le reste passe par les points et les vis).
        // Et toutes les autres entrees, pour le journal seulement (desync du 02/11, JD et GG : empreinte differente toutes les 4 s pendant
        // 7 min -- Door_Left Installed faux chez l'hote, vrai chez l'invite, meme « point tenu » des deux cotes ; ~70 listes renvoyees sans
        // rien corriger, sans dire quelle entree differait). L'invite note les ecarts qu'aucun controle ne repare (MscOnDigestDb) ; un client
        // d'avant ne fait que Touch sur ces lignes. [Test] EmpreinteListeComplete=0 : comme avant.
        static bool empreinteListe = true;
        static readonly Dictionary<uint, float> dbDiffLogged = new Dictionary<uint, float>();
        static void MscDigestDb()
        {
            if (mscDb.Count == 0) MscDbScan();
            NetWriter w = null; int n = 0;
            foreach (MscDbEntry en in mscDb)
            {
                if (en.Installed == null || en.Swap == null && !en.Wire && !empreinteListe) continue;
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
            System.Text.StringBuilder diff = null;
            while (r.More)
            {
                uint h = (uint)r.I32(); bool inst = r.U8() != 0;
                MscDbEntry en;
                if (mscDbByHash == null || !mscDbByHash.TryGetValue(h, out en) || en.Installed == null) { unknown++; continue; }
                if (en.Installed.Value == inst || Recent(h, now)) continue;
                if (en.Swap == null && !en.Wire)
                {
                    // (entree hors de ce controle : notee, une fois par minute au plus -- reparee par les points, ou pas du tout)
                    float at;
                    if (dbDiffLogged.TryGetValue(h, out at) && now - at < 60f) continue;
                    dbDiffLogged[h] = now;
                    if (diff == null) diff = new System.Text.StringBuilder();
                    if (diff.Length < 700) diff.Append(' ').Append(en.Key).Append(" (ici ").Append(en.Installed.Value ? 1 : 0).Append(", hote ").Append(inst ? 1 : 0).Append(')');
                    continue;
                }
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
            if (diff != null) Log.Warn("pieces (MSC) : empreinte : ecarts Database hors controle (Installed) :" + diff);
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
                float lim;
                if (!keptForce.TryGetValue(kv.Key, out lim)) lim = 200f;
                if (kv.Key != null && float.IsInfinity(kv.Key.breakForce)) { kv.Key.breakForce = lim; kv.Key.breakTorque = lim; }
                if (done == null) done = new List<Joint>();
                done.Add(kv.Key);
            }
            if (done != null) foreach (Joint j in done) { keptJoints.Remove(j); keptForce.Remove(j); }
        }

        // Pieces a charniere (portieres, capot, coffre : 'Remove part' detruit une HingeJoint, 'Mouse off' teste HasComponent HingeJoint).
        static readonly Dictionary<PlayMakerFSM, bool> hingeRemovals = new Dictionary<PlayMakerFSM, bool>();
        static bool HingeRemoval(PlayMakerFSM rem)
        {
            bool h;
            if (rem == null) return false;
            if (hingeRemovals.TryGetValue(rem, out h)) return h;
            FsmState s = rem.Fsm != null ? rem.Fsm.GetState("Remove part") : null;
            FsmStateAction[] acts = null;
            try { acts = s != null ? s.Actions : null; } catch { }
            if (acts != null)
                foreach (FsmStateAction a in acts)
                {
                    if (a == null || a.GetType().Name != "DestroyComponent") continue;
                    System.Reflection.FieldInfo cf = a.GetType().GetField("component");
                    FsmString c = cf != null ? cf.GetValue(a) as FsmString : null;
                    if (c != null && c.Value != null && c.Value.EndsWith("HingeJoint")) { h = true; break; }
                }
            hingeRemovals[rem] = h;
            return h;
        }
        // Limite de montage du point de la piece ({InitialAttachForce} de son 'Assembly' : 1100 pour une portiere), sinon 'def'.
        static float MscAttachForce(PlayMakerFSM rem, float def)
        {
            FsmGameObject trig = rem.FsmVariables.FindFsmGameObject("Trigger");
            PlayMakerFSM asm = trig != null && trig.Value != null ? Game.FsmOn(trig.Value, "Assembly") : null;
            FsmFloat f = asm != null ? asm.FsmVariables.FindFsmFloat("InitialAttachForce") : null;
            return f != null && f.Value > 0f && !float.IsInfinity(f.Value) && f.Value < 1e30f ? f.Value : def;
        }

        // Charniere rompue sur la copie (desync du 02/11, JD et GG) : la garde posait un FixedJoint, mais le 'Mouse off' d'une portiere teste
        // HasComponent HingeJoint -- REMOVE repartait dans la meme image, le FixedJoint pose ici faisait echouer la garde ci-dessous et la
        // portiere tombait chez l'invite seul (« attache disparue ... remise » puis « cassee ici sous l'autorite d'un autre (joints 1) » a la
        // meme milliseconde), soudee au monde par ce FixedJoint incassable que 'Remove part' (SetJointConnectedBody : le premier joint)
        // detachait de la voiture ; et un verrou du 'Use' (FixedJoint) present faisait refuser la garde d'emblee (« joints 3 »). Pour ces
        // pieces : {Detach} reconnu a une charniere encore la, et c'est une charniere neuve qui est posee -- reglages de celle qui vient de
        // rompre (PartWatch), relachee a {InitialAttachForce} quand la voiture redevient la notre. Sans charniere relevee a la rupture (moins
        // de 1 s) : pas gardee, MscLocalRemove la redemande a l'hote. [Test] GardeCharniere=0 : comme avant.
        static bool gardeCharniere = true;
        static readonly Dictionary<Joint, float> keptForce = new Dictionary<Joint, float>();
        static bool MscKeepOnCopy(PlayMakerFSM rem)
        {
            if (!Session.Active) return false;
            Transform car = VehicleSync.CarRoot(rem.transform);
            Rigidbody cb = car != null ? car.GetComponent<Rigidbody>() : null;
            if (cb == null || VehicleSync.Authority(cb) == Session.LocalId) return false;
            bool hinge = gardeCharniere && HingeRemoval(rem);
            PartWatch pw = hinge ? rem.GetComponent<PartWatch>() : null;
            if (hinge)
            {
                if (rem.GetComponent<HingeJoint>() != null || rem.transform.parent == null) return false;   // (decrochage {Detach} du jeu)
                if (pw == null || Time.realtimeSinceStartup - pw.HingeAt > 1f) return false;
            }
            else if (rem.GetComponent<FixedJoint>() != null || rem.transform.parent == null) return false;   // (decrochage {Detach} du jeu, pas une attache disparue)
            Rigidbody to = rem.transform.parent.GetComponentInParent<Rigidbody>();
            if (to == null) to = cb;
            string id = MscPartKey(rem.gameObject);
            Rigidbody prb = rem.GetComponent<Rigidbody>();
            rem.transform.localPosition = Vector3.zero; rem.transform.localRotation = Quaternion.identity;
            if (prb != null) { prb.velocity = Vector3.zero; prb.angularVelocity = Vector3.zero; if (!prb.isKinematic) { prb.position = rem.transform.position; prb.rotation = rem.transform.rotation; } }
            Joint fj;
            float lim = 200f;
            if (hinge)
            {
                // (vieilles gardes ancrees au monde laissees par un 'Remove part' d'avant)
                foreach (FixedJoint old in rem.GetComponents<FixedJoint>()) if (old.connectedBody == null) { keptJoints.Remove(old); keptForce.Remove(old); Object.DestroyImmediate(old); }
                // (pose de montage remise ci-dessus, comme 'Assemble 2' : le zero de la charniere neuve est la portiere fermee, comme au montage)
                HingeJoint hj = rem.gameObject.AddComponent<HingeJoint>();
                hj.axis = pw.HingeAxis; hj.anchor = pw.HingeAnchor; hj.useLimits = pw.HingeUseLimits; hj.limits = pw.HingeLimits;
                hj.connectedBody = to;
                fj = hj;
                lim = MscAttachForce(rem, 1100f);
            }
            else
            {
                FixedJoint nf = rem.gameObject.AddComponent<FixedJoint>();
                nf.connectedBody = to;
                fj = nf;
            }
            VehicleSync.AdoptJoint(cb, fj, new Vector2(lim, lim));   // (InitialAttachForce du montage)
            // Voiture garee menee par l'autre, libre ici (pas une copie cinematique) : l'attache neuve a 200 recassait a chaque pas
            // tant que la piece restait pressee (pare-chocs contre la porte du garage apres une poussee : 60 casses en 1 s, limite
            // 200 -- la « force 1,1e9 » du journal etait cette limite lue en bits, PartWatch --, la caisse secouee ici -- partie de JD
            // et Kristof, 24/10, rejoue par Autotest=mscmontage PousseA). Incassable tant qu'un autre la mene ; ses limites quand elle
            // redevient la notre (MscTick).
            if (!float.IsInfinity(fj.breakForce)) { fj.breakForce = Mathf.Infinity; fj.breakTorque = Mathf.Infinity; keptJoints[fj] = cb; if (hinge) keptForce[fj] = lim; }
            PlayMakerFSM bc = Game.FsmOn(rem.gameObject, "BoltCheck");
            FsmObject jv = bc != null ? bc.FsmVariables.FindFsmObject("Joint") : null;
            if (jv != null) jv.Value = fj;
            // (audit MSC 02/11 : {Detach} pose par un 'Break off' joue sur la copie restait vrai -- 'Mouse off' repartait aussitot en
            // 'Remove part', l'attache neuve faisait cette fois echouer la garde ci-dessus et la piece tombait ici seule. [Test] ChanceConducteur=0 : comme avant)
            FsmBool dt = chanceConducteur ? rem.FsmVariables.FindFsmBool("Detach") : null;
            if (dt != null) dt.Value = false;
            bool back = Game.SwitchLater(rem, "Mouse off");
            if (keepLogs++ < 40 || keepLogs % 50 == 0)
                Log.Warn("piece " + id + " : attache disparue ici sur la copie (voiture menee par #" + VehicleSync.Authority(cb) + "), remise sur " + to.name + (hinge ? " (charniere)" : "")
                         + (prb != null ? ", corps " + (prb.isKinematic ? "cinematique" : "libre") : "") + (back ? "" : " (automate non renvoye en Mouse off)"));
            return back;
        }

        static bool casseCopieGeste = true;
        static readonly Dictionary<uint, float> copyAsked = new Dictionary<uint, float>();   // (point redemande apres une casse sur la copie : 2 s, pas les 60 s d'AskedLately)
        static readonly Dictionary<uint, float> hostWaits = new Dictionary<uint, float>();   // (hote : point vide apres une casse refusee sur sa copie, 25 s)
        // Hote : ce point attend-il l'etat du conducteur ? Fini des qu'il tient une piece, a tout geste ou message apres la casse (Touch), ou au bout de 25 s.
        static bool MscWaitsDriver(uint h, string id, float now)
        {
            float at, tt;
            if (!hostWaits.TryGetValue(h, out at)) return false;
            if (id.Length > 0 || now - at > 25f || touched.TryGetValue(h, out tt) && tt >= at) { hostWaits.Remove(h); return false; }
            return true;
        }
        static void MscLocalRemove(PlayMakerFSM rem, bool breakoff)
        {
            if (!Session.Active || Time.realtimeSinceStartup - loadedAt < 20f) return;
            string id = MscPartKey(rem.gameObject);
            FsmGameObject trig = rem.FsmVariables.FindFsmGameObject("Trigger");
            string key = trig != null && trig.Value != null ? MscPointKey(trig.Value.transform) : "";
            // (casse : Touch seulement une fois prise pour un geste d'ici -- ci-dessous)
            bool copieGeste = casseCopieGeste && breakoff;
            if (key.Length > 0 && !copieGeste) Touch(H(key));
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
                    // Pas un geste (desync du 02/11, JD et GG) : le Touch d'avant ce refus datait le point « touche ici maintenant », 6 s plus tard
                    // le controle de l'hote trouvait ce « geste » plus recent que le sien et renvoyait la casse comme un demontage du joueur #2 --
                    // portiere demontee chez l'hote, qui ne l'avait jamais cassee (17:47:05 et 17:52:53). Le point n'est plus du tout date ici
                    // (jamais « plus recent »), et l'invite redemande tout de suite le point a l'hote, qui renvoie son montage. [Test]
                    // CasseCopieGeste=0 : comme avant.
                    if (copieGeste && key.Length > 0)
                    {
                        uint hk = H(key);
                        touched.Remove(hk);
                        float last;
                        float now = Time.realtimeSinceStartup;
                        if (!Session.IsHost && !(copyAsked.TryGetValue(hk, out last) && now - last < 2f))
                        {
                            copyAsked[hk] = now;
                            Session.SendToHost(new NetWriter(Msg.Job).U8(Session.LocalId).Str("@piecesdemande").I32((int)hk), true);
                            Log.Info("piece " + id + " : point " + key + " redemande a l'hote");
                        }
                        // Hote (relecture du lot) : la casse refusee laissait le point vide ici sans rien pour le remettre, et son controle
                        // annoncait ce vide a un conducteur qui n'y avait jamais touche (age 1e6 des deux cotes) -- la portiere tombait chez
                        // celui qui mene la voiture. Le point attend l'etat du conducteur : MscDigest l'annonce « plus ancien que tout », le
                        // conducteur renvoie son montage (chemin « plus recent que chez l'hote »).
                        if (Session.IsHost)
                        {
                            if (!hostWaits.ContainsKey(hk)) Log.Info("piece " + id + " : point " + key + " vide ici, montage attendu du conducteur #" + VehicleSync.Authority(cb) + " au controle");
                            hostWaits[hk] = now;
                        }
                    }
                    return;
                }
                if (copieGeste && key.Length > 0) Touch(H(key));
                // (choc il y a moins de 2 s : 16 casses en 2 s -- avant, 3 par seconde meme dans un accident : la 4e casse n'etait jamais
                // envoyee et restait cassee ici seulement -- dossier #25, reponts 29/10 : batterie tombee chez l'invite, montee chez l'hote.
                // [Test] CasseRafaleAncienne=1 : comme avant)
                bool crash = CarHits.ImpactAgo(car) < WindowCrash && Config.GetInt("Test", "CasseRafaleAncienne", 0) == 0;
                if (TooMany(car != null ? car.name : key, Time.realtimeSinceStartup, crash ? MscBurstCrash : BurstCalm, crash ? WindowCrash : WindowCalm))
                { Log.Warn("piece " + id + " cassee : trop de casses" + (crash ? " malgre le choc" : "") + ", pas envoyee"); return; }
            }
            // (echange : la nouvelle piece libre n'existe qu'apres les actions de 'Remove part' -- envoye a l'image suivante, MscTick)
            if (rem.FsmVariables.FindFsmGameObject("SpawnPart") != null || housses && MultiRemoval(rem)) { mscOut.Add(new MscOut { Rem = rem, Key = key, Id = id, Flags = breakoff ? 1 : 0 }); return; }
            Log.Info((breakoff ? "casse : " : "demontage : ") + id + " de " + key);
            Session.SendAll(new NetWriter(Msg.Part).U8(Session.LocalId).Bool(false).Str(key).Str(id).U8(breakoff ? 1 : 0), true);
            Props.SettleHere(rem.gameObject);
        }

        // Portiere demontee encore tenue a la voiture (desync du 02/11, JD et GG) : 'Remove part' ne detruit que la charniere, le verrou du
        // 'Use' (FixedJoint vers la SATSUMA, 'State 1' 12000 a la fermeture, 'Set lock 2' a l'ouverture) restait sur la portiere libre --
        // en la portant JD trainait la voiture (557 kg) : « poussee : SATSUMA suit notre poussee », ailes pas vissees arrachees (limite
        // 200, voiture v 1.19), jusqu'a ce que le verrou casse enfin portiere en main (« limite 12000, piece v 1.01 »). Et SetJointConnectedBody
        // ('Remove part' : le PREMIER joint) pouvait detacher le verrou au lieu de la charniere : portiere soudee au monde. En tete de 'Remove part'
        // d'une piece a charniere qui sort vraiment ici (geste d'ici, casse refusee comprise, ou rejoue) : ses attaches autres que la charniere
        // vers sa voiture ou vers le monde sont retirees (DestroyImmediate : SetJointConnectedBody, dans la meme image, ne doit plus les trouver).
        // [Test] VerrouDemontage=0 : comme avant.
        static bool verrouDemontage = true;
        static int dropLogs;
        static void MscDropCarJoints(PlayMakerFSM rem, string why)
        {
            Transform car = VehicleSync.CarRoot(rem.transform);
            FsmGameObject trig = rem.FsmVariables.FindFsmGameObject("Trigger");
            if (car == null && trig != null && trig.Value != null) car = VehicleSync.CarRoot(trig.Value.transform);
            int n = 0; var sb = new System.Text.StringBuilder();
            foreach (Joint j in rem.GetComponents<Joint>())
            {
                if (j == null || j is HingeJoint) continue;   // (la charniere : 'Remove part' la detache et la detruit lui-meme)
                Rigidbody to = j.connectedBody;
                if (to != null && (car == null || !to.transform.IsChildOf(car))) continue;
                sb.Append(sb.Length > 0 ? ", " : "").Append(j.GetType().Name).Append(' ').Append(float.IsInfinity(j.breakForce) ? "Infinity" : j.breakForce.ToString("F0")).Append(to != null ? " vers " + to.name : " vers le monde");
                keptJoints.Remove(j); keptForce.Remove(j);
                Object.DestroyImmediate(j); n++;
            }
            if (n > 0 && (dropLogs++ < 40 || dropLogs % 50 == 0)) Log.Info("piece " + MscPartKey(rem.gameObject) + " : " + n + " attache(s) restee(s) retiree(s) au " + why + " (" + sb + ")");
        }
        // Meme chose pour la piece {Part} d'un point en tete de 'Set joint' (MscJointHook) : toutes ses attaches vers la voiture du point ou vers
        // le monde, avant que le jeu n'ajoute la sienne.
        static bool jointsPerimes = true;
        static void MscDropStaleJoints(MscPoint p)
        {
            if (!Session.Active || p == null || p.Fsm == null) return;
            FsmGameObject pv = p.Fsm.FsmVariables.FindFsmGameObject("Part");
            GameObject part = pv != null ? pv.Value : null;
            if (part == null) return;
            Transform car = VehicleSync.CarRoot(p.Fsm.transform);
            int n = 0; var sb = new System.Text.StringBuilder();
            foreach (Joint j in part.GetComponents<Joint>())
            {
                if (j == null) continue;
                Rigidbody to = j.connectedBody;
                if (to != null && (car == null || !to.transform.IsChildOf(car))) continue;
                sb.Append(sb.Length > 0 ? ", " : "").Append(j.GetType().Name).Append(' ').Append(float.IsInfinity(j.breakForce) ? "Infinity" : j.breakForce.ToString("F0")).Append(to != null ? " vers " + to.name : " vers le monde");
                keptJoints.Remove(j); keptForce.Remove(j);
                Object.DestroyImmediate(j); n++;
            }
            if (n > 0) Log.Warn("montage : " + n + " attache(s) perimee(s) retiree(s) de " + MscPartKey(part) + " sur " + p.Key + " (" + sb + ")");
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
            // (reglages de la charniere au moment d'une rupture -- elle existe encore pendant l'appel : MscKeepOnCopy en pose une pareille)
            public float HingeAt = -100f; public Vector3 HingeAxis, HingeAnchor; public bool HingeUseLimits; public JointLimits HingeLimits;
            void OnJointBreak(float force)
            {
                LastBreak = Time.realtimeSinceStartup;
                try
                {
                    HingeJoint h = GetComponent<HingeJoint>();
                    if (h != null) { HingeAt = LastBreak; HingeAxis = h.axis; HingeAnchor = h.anchor; HingeUseLimits = h.useLimits; HingeLimits = h.limits; }
                    OnPartJointBreak(this, force);
                }
                catch (System.Exception e) { Replay.HookError(e); }
            }
            public string Recent(float within)
            {
                var sb = new System.Text.StringBuilder();
                float now = Time.realtimeSinceStartup;
                for (int i = 0; i < what.Length; i++)
                    if (what[i] != null && now - at[i] < within) sb.Append(sb.Length > 0 ? " ; " : "").Append(what[i]).Append(" il y a ").Append((now - at[i]).ToString("F2")).Append(" s");
                return sb.Length > 0 ? sb.ToString() : "aucun";
            }
        }
        // La « force » d'OnJointBreak (Unity 5.0 de MSC) n'en est pas une (desync du 02/11, JD et GG) : sur 48 ruptures, 4 valeurs seulement,
        // les bits IEEE du breakForce du joint rompu lus comme un entier -- 1128792000 = 200 (aile), 1149862000 = 1100 (charniere sans vis),
        // 1178305000 = 12000 (verrou), 2139095000 = Infinity. Et la « limite » notee etait celle du PREMIER joint, pas forcement celui qui a
        // rompu. Decodee ici, avec tous les joints de la piece (type, limite, vers quoi ; « <- rompu » pour celui de cette limite, « garde »
        // pour une attache posee par MscKeepOnCopy). Journal seulement. [Test] CasseJournalLimite=0 : comme avant.
        static bool casseJournal = true;
        static string LimitText(float f) { return float.IsInfinity(f) ? "Infinity" : f.ToString("F0"); }
        static int breakLogs;
        static void OnPartJointBreak(PartWatch w, float force)
        {
            if (!Session.Active || breakLogs++ >= 60) return;
            Joint j = w.GetComponent<Joint>();
            Rigidbody prb = w.GetComponent<Rigidbody>();
            Transform car = VehicleSync.CarRoot(w.transform);
            Rigidbody cb = car != null ? car.GetComponent<Rigidbody>() : null;
            // (repli : une valeur qui ne ressemble pas a des bits de limite -- autre Unity -- est notee comme avant)
            float lim = casseJournal && force >= 1e8f && force < 2.1e9f ? System.BitConverter.ToSingle(System.BitConverter.GetBytes((int)force), 0) : float.NaN;
            string what;
            if (!float.IsNaN(lim) && lim >= 0f)
            {
                var sb = new System.Text.StringBuilder();
                foreach (Joint x in w.GetComponents<Joint>())
                {
                    if (x == null) continue;
                    bool same = float.IsInfinity(lim) ? float.IsInfinity(x.breakForce) : Mathf.Abs(x.breakForce - lim) <= 1e-3f * Mathf.Max(1f, lim);
                    sb.Append(sb.Length > 0 ? ", " : "").Append(x.GetType().Name).Append(' ').Append(LimitText(x.breakForce)).Append(x.connectedBody != null ? " vers " + x.connectedBody.name : " vers le monde")
                      .Append(same ? " <- rompu" : "").Append(keptJoints.ContainsKey(x) ? " (garde)" : "");
                }
                what = "attache rompue, limite " + LimitText(lim) + " (joints : " + (sb.Length > 0 ? sb.ToString() : "aucun") + ")";
            }
            else what = "attache rompue, force " + force.ToString("F0") + (j != null ? " (limite " + j.breakForce.ToString("F0") + ")" : "");
            Log.Info("piece " + MscPartKey(w.gameObject) + " : " + what
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
            FsmState ws0 = WaitState(p.Fsm);
            Game.SetState(p.Fsm, WaitStateFor(p.Fsm, part) ?? (ws0 != null ? ws0.Name : "Wait for assembly"));   // (branche de la piece, comme 'Find correct part 2')
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

        // [Test] Autotest=dossier35 (2 joueurs, квазарик 31/10), a partir de T (les deux en jeu depuis 20 s) : T+5 l'invite demonte la
        // batterie et l'hote le frein a main (comme le joueur) ; T+15 l'invite repose la batterie (clic de montage) ; T+20 l'hote
        // sauvegarde en jeu ; T+28 chacun note la batterie, le frein a main monte et les freins a main libres ; T+30 l'hote vise le
        // cric rouleur main vide (PickedObject sans prise, comme un clic sur le cric leve) ; T+34 tenu ou non, deplace ou non.
        static float d35T0 = -1; static int d35Step; static string d35Bat; static Vector3 d35Jack;
        public static void TestDossier35()
        {
            float now = Time.realtimeSinceStartup;
            if (d35T0 < 0) { if (!Jobs.OtherInGame(20f)) return; d35T0 = now; MscScan(); }
            float d = now - d35T0;
            string bp = null;
            foreach (KeyValuePair<string, MscPoint> kv in mscPoints) if (kv.Key.EndsWith("trigger_battery")) bp = kv.Key;
            if (d35Step == 0 && d > 5f)
            {
                d35Step = 1;
                string key = Session.IsHost ? "db:Database/DatabaseMechanics/Handbrake" : (bp != null ? MscInstalledId(mscPoints[bp]) : "");
                if (!Session.IsHost) d35Bat = key;
                GameObject part = key.Length > 0 ? MscFindPart(key) : null;
                PlayMakerFSM rem = part != null ? Game.FsmOn(part, "Removal") : null;
                if (rem != null) { rem.enabled = true; Game.SetState(rem, "Mouse over"); rem.SendEvent("REMOVE"); }
                Log.Info("autotest dossier35 : point batterie " + (bp ?? "absent") + ", demontage de " + (key.Length > 0 ? key : "?") + " (" + (rem != null ? "Removal " + rem.ActiveStateName : "introuvable") + ")");
            }
            if (d35Step == 1 && d > 15f)
            {
                d35Step = 2;
                if (!Session.IsHost) Log.Info("autotest dossier35 : " + (bp != null && !string.IsNullOrEmpty(d35Bat) ? MscTestClick(bp, d35Bat) : "pas de batterie a reposer"));
            }
            if (d35Step == 2 && d > 20f)
            {
                d35Step = 3;
                if (Session.IsHost) { Log.Info("autotest dossier35 : sauvegarde en jeu"); Game.SaveInPlace(); }
            }
            if (d35Step == 3 && d > 28f)
            {
                d35Step = 4;
                GameObject hb = Game.FindAny("SATSUMA(557kg, 248)/MiscParts/HandBrake/handbrake(xxxxx)");
                GameObject db = Game.FindAny("Database/DatabaseMechanics/Handbrake");
                PlayMakerFSM data = db != null ? Game.FsmOn(db, "Data") : null;
                FsmBool inst = data != null ? data.FsmVariables.FindFsmBool("Installed") : null;
                int loose = 0;
                foreach (Rigidbody rb in Object.FindObjectsOfType<Rigidbody>()) if (rb.name == "handbrake(Clone)" && rb.gameObject.activeInHierarchy) loose++;
                Log.Info("autotest dossier35 : batterie " + (bp != null ? MscTestHolds(bp) : "point absent") + " ; frein a main monte " + (hb != null ? hb.activeSelf.ToString() : "?")
                         + ", Installed " + (inst != null ? inst.Value.ToString() : "?") + ", Data " + (data != null ? data.ActiveStateName : "?") + ", libres " + loose);
            }
            if (Session.IsHost && d35Step == 4 && d > 30f)
            {
                d35Step = 5;
                GameObject fj = Game.FindAny("ITEMS/floor jack(itemx)");
                if (fj != null) { d35Jack = fj.transform.position; Props.TestSetPicked(fj); }
                Log.Info("autotest dossier35 : cric rouleur vise main vide " + (fj != null ? "en " + d35Jack.ToString("F2") : "introuvable"));
            }
            if (Session.IsHost && d35Step == 5 && d > 34f)
            {
                d35Step = 6;
                GameObject fj = Game.FindAny("ITEMS/floor jack(itemx)");
                Log.Info("autotest dossier35 : cric rouleur " + (Props.Holding ? "TENU" : "pas tenu") + ", deplace de " + (fj != null ? (fj.transform.position - d35Jack).magnitude.ToString("F2") : "?") + " m");
                Props.TestSetPicked(null);
            }
        }

        // [Test] Autotest=radiateur (2 joueurs, dossier #36 xZOTIK 31/10), a partir de T : T+5 l'invite demonte le radiateur stock ; T+10
        // chacun sort le radiateur sport (racing radiator(Clone), eteint tant qu'il n'est pas achete) ; T+16 l'invite le monte (branche
        // 'Wait for assembly 2', comme 'Find correct part') ; T+26 chacun note les deux Installed et les deux radiateurs allumes.
        static float radT0 = -1; static int radStep; static GameObject radPart;
        public static void TestRadiator()
        {
            float now = Time.realtimeSinceStartup;
            if (radT0 < 0) { if (!Jobs.OtherInGame(20f)) return; radT0 = now; MscScan(); }
            float d = now - radT0;
            string pk = null;
            foreach (KeyValuePair<string, MscPoint> kv in mscPoints) if (kv.Key.EndsWith("trigger_radiator")) pk = kv.Key;
            if (radStep == 0 && d > 5f)
            {
                radStep = 1;
                GameObject part = !Session.IsHost ? MscFindPart("db:Database/DatabaseMechanics/Radiator") : null;
                PlayMakerFSM rem = part != null ? Game.FsmOn(part, "Removal") : null;
                if (rem != null) { rem.enabled = true; Game.SetState(rem, "Mouse over"); rem.SendEvent("REMOVE"); }
                Log.Info("autotest radiateur : point " + (pk ?? "absent") + (Session.IsHost ? "" : ", demontage du radiateur stock (" + (rem != null ? "Removal " + rem.ActiveStateName : "introuvable") + ")"));
            }
            if (radStep == 1 && d > 10f)
            {
                radStep = 2;
                // (pas achete : STORE/Boxes/racing radiator(Clone)/racing radiator(Clone), eteint -- {SpawnThis} de sa Database, comme 'Basic state')
                GameObject dbg = Game.FindAny("Database/DatabaseOrders/Racing Radiator");
                PlayMakerFSM dbf = dbg != null ? Game.FsmOn(dbg, "Data") : null;
                FsmGameObject sp = dbf != null ? dbf.FsmVariables.FindFsmGameObject("SpawnThis") : null;
                GameObject rr = sp != null ? sp.Value : null; radPart = rr;
                GameObject trig = pk != null ? mscPoints[pk].Fsm.gameObject : null;
                if (rr != null)
                {
                    rr.transform.parent = null;
                    rr.SetActive(true);
                    if (trig != null) rr.transform.position = trig.transform.position + Vector3.up * 1.5f;
                    Rigidbody rb = rr.GetComponent<Rigidbody>(); if (rb != null) rb.isKinematic = true;
                    Props.Adopt(rr, "w:racing radiator");   // (cle de la partie du joueur : radiateur achete, a la racine)
                }
                Log.Info("autotest radiateur : radiateur sport " + (rr != null ? "sorti" : "introuvable"));
            }
            if (radStep == 2 && d > 24f)
            {
                radStep = 3;
                if (!Session.IsHost && pk != null)
                {
                    MscPoint p = mscPoints[pk];
                    GameObject part = radPart;
                    if (part == null) Log.Info("autotest radiateur : w:racing radiator introuvable");
                    else
                    {
                        part.transform.position = p.Fsm.transform.position;
                        p.Fsm.FsmVariables.GetFsmGameObject("Part").Value = part;
                        Game.SetState(p.Fsm, "Wait for assembly 2");
                        testClick = p.Fsm; testClickFrame = Time.frameCount + 2;
                        Log.Info("autotest radiateur : clic de montage du radiateur sport (cle " + MscPartKey(part) + ")");
                    }
                }
            }
            if (radStep == 3 && d > 34f)
            {
                radStep = 4;
                var sb = new System.Text.StringBuilder("autotest radiateur : ");
                foreach (string db in new[] { "Database/DatabaseMechanics/Radiator", "Database/DatabaseOrders/Racing Radiator" })
                {
                    GameObject g = Game.FindAny(db);
                    PlayMakerFSM dd = g != null ? Game.FsmOn(g, "Data") : null;
                    FsmBool inst = dd != null ? dd.FsmVariables.FindFsmBool("Installed") : null;
                    sb.Append(db.Substring(db.LastIndexOf('/') + 1)).Append(" Installed=").Append(inst != null ? inst.Value.ToString() : "?").Append(" ; ");
                }
                foreach (string m in new[] { "SATSUMA(557kg, 248)/MiscParts/radiator(xxxxx)", "SATSUMA(557kg, 248)/MiscParts/racing radiator(xxxxx)" })
                {
                    GameObject g = Game.FindAny(m);
                    sb.Append(m.Substring(m.LastIndexOf('/') + 1)).Append(' ').Append(g != null ? (g.activeSelf ? "allume" : "eteint") : "?").Append(" ; ");
                }
                Log.Info(sb.ToString());
            }
        }

        // [Test] Autotest=mscremonte (WIGA 25/10 : aile montee chez l'hote, arrachee 50 ms apres -- « force 1e9 » : la limite 200 lue en
        // bits, PartWatch) : le joueur [Test] Monteur demonte [Test] Piece (Fender_Left) comme le joueur a 40 s, la remonte sur son point
        // ([Test] Point, fin du chemin) a 50 s ; a 60 s chacun note si elle est montee (les casses sont notees par PartWatch).
        // Essais du desync du 02/11 (JD et GG), sur la meme horloge (decalee de RemonteA) :
        //  - [Test] RemonteVerrou=1 : a 35 s chacun pose sur la piece montee un verrou comme 'State 1' du 'Use' d'une portiere fermee
        //    (FixedJoint vers la voiture, 12000) ; a 45 s chacun note ses attaches (aucune vers la voiture apres le demontage des 40 s).
        //  - [Test] CasseAuMontage=1 : chez le Monteur, l'attache posee par 'Set joint 2' du clic des 50 s detruite en 'Wait 2' (charniere
        //    rompue pendant le montage) ; =2 : et la casse envoyee tout de suite (arrive chez l'autre pendant sa chaine rejouee).
        //  - [Test] CasseCopieA=s, CasseCopieQui=n (2) : chez le joueur n, a s, la charniere (sinon la 1re attache) de la piece montee rompue
        //    par la physique (limites 0,001) ; a s+5 il note ses attaches et le point (casse sur la copie).
        //  - [Test] MeneQui=n, MeneDe=s, MeneA=s : de MeneDe a MeneA, le joueur n garde la voiture de la piece comme un moteur laisse tournant
        //    (casse sur la copie de l'hote : CasseCopieQui=0, MeneQui=2 ; Monteur=9 pour une piece jamais touchee de la session).
        //  - [Test] Clic2=<fin du point>|<cle de piece>, Clic2Qui=n (2), Clic2A=s (70) : clic de montage par le joueur n a s ; a s+10 chacun
        //    note les deux points et {Detach} de [Test] Piece (piston clique sur un vilebrequin remonte pas serre).
        static int remStep, verStep, copStep, clic2Step; static string clic2Point;
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
                int ca = Config.GetInt("Test", "CasseAuMontage", 0);
                if (me && pk != null && ca != 0)
                {
                    casseStep = 1; casseFsm = mscPoints[pk].Fsm; cassePart = MscFindPart(partKey); casseKey = pk; casseSend = ca == 2; casseUntil = Time.realtimeSinceStartup + 3f;
                }
            }
            if (remStep == 2 && t > 60f)
            {
                remStep = 3;
                Log.Info("autotest mscremonte : " + (pk != null ? MscTestHolds(pk) : "point introuvable") + " ; " + TestJoints(partKey));
            }
            if (verStep == 0 && t > 35f && Config.GetInt("Test", "RemonteVerrou", 0) != 0)
            {
                verStep = 1;
                GameObject part = MscFindPart(partKey);
                Transform car = part != null ? VehicleSync.CarRoot(part.transform) : null;
                Rigidbody cb = car != null ? car.GetComponent<Rigidbody>() : null;
                if (cb != null) { FixedJoint fj = part.AddComponent<FixedJoint>(); fj.connectedBody = cb; fj.breakForce = 12000f; fj.breakTorque = 12000f; }
                Log.Info("autotest mscremonte : verrou " + (cb != null ? "pose" : "pas pose (pas dans une voiture)") + ", " + TestJoints(partKey));
            }
            if (verStep == 1 && t > 45f) { verStep = 2; Log.Info("autotest mscremonte : apres le demontage, " + TestJoints(partKey)); }
            if (Session.LocalId == Config.GetInt("Test", "MeneQui", -1) && t > Config.GetFloat("Test", "MeneDe", 60f) && t < Config.GetFloat("Test", "MeneA", 110f))
            {
                GameObject part = MscFindPart(partKey);
                Transform car = part != null ? VehicleSync.CarRoot(part.transform) : null;
                if (car != null) { VehicleSync.TestKeepRunning(car.name); VehicleSync.TestOwn(car.name); }
            }
            float cca = Config.GetFloat("Test", "CasseCopieA", 0f);
            if (cca > 0f && copStep == 0 && t > cca && Session.LocalId == Config.GetInt("Test", "CasseCopieQui", 2))
            {
                copStep = 1;
                GameObject part = MscFindPart(partKey);
                Joint j = part != null ? part.GetComponent<HingeJoint>() : null;
                if (j == null && part != null) j = part.GetComponent<Joint>();
                if (j != null) { j.breakForce = 0.001f; j.breakTorque = 0.001f; Rigidbody rb = part.GetComponent<Rigidbody>(); if (rb != null) rb.WakeUp(); }
                Transform car = part != null ? VehicleSync.CarRoot(part.transform) : null;
                Rigidbody cb = car != null ? car.GetComponent<Rigidbody>() : null;
                Log.Info("autotest mscremonte : casse sur la copie, " + (j != null ? j.GetType().Name + " a 0,001" : "pas d'attache") + (cb != null ? ", voiture menee par #" + VehicleSync.Authority(cb) : ""));
            }
            if (copStep == 1 && t > cca + 5f) { copStep = 2; Log.Info("autotest mscremonte : apres la casse sur la copie, " + TestJoints(partKey) + " ; " + (pk != null ? MscTestHolds(pk) : "point introuvable")); }
            string c2 = Config.Get("Test", "Clic2", "");
            float c2a = Config.GetFloat("Test", "Clic2A", 70f);
            if (c2.Length > 0 && clic2Step == 0 && t > c2a)
            {
                clic2Step = 1;
                string[] cp = c2.Split('|');
                if (cp.Length == 2) foreach (KeyValuePair<string, MscPoint> kv in mscPoints) if (kv.Key.EndsWith(cp[0])) clic2Point = kv.Key;
                bool me2 = Session.LocalId == Config.GetInt("Test", "Clic2Qui", 2);
                Log.Info("autotest mscremonte : 2e clic, point " + (clic2Point ?? "?") + (me2 && clic2Point != null ? ", " + MscTestClick(clic2Point, cp[1]) : ""));
            }
            if (clic2Step == 1 && t > c2a + 10f)
            {
                clic2Step = 2;
                GameObject part = MscFindPart(partKey);
                PlayMakerFSM rem = part != null ? Game.FsmOn(part, "Removal") : null;
                FsmBool dt = rem != null ? rem.FsmVariables.FindFsmBool("Detach") : null;
                Log.Info("autotest mscremonte : apres le 2e clic, " + (clic2Point != null ? MscTestHolds(clic2Point) : "point introuvable") + " ; " + (pk != null ? MscTestHolds(pk) : "point introuvable")
                         + " ; Detach de " + partKey + " " + (dt != null ? dt.Value.ToString() : "?"));
            }
        }

        // Essais (CasseAuMontage, MscTick) : l'attache de la piece rompue des que son point passe en 'Wait 2'.
        static int casseStep; static PlayMakerFSM casseFsm; static GameObject cassePart; static string casseKey; static float casseUntil; static bool casseSend;
        static void TestCasseTick()
        {
            if (casseFsm == null || cassePart == null || Time.realtimeSinceStartup > casseUntil)
            {
                casseStep = 0;
                Log.Info("autotest : casse au montage pas faite (point " + (casseFsm != null ? "en " + casseFsm.ActiveStateName : "introuvable") + ")");
                return;
            }
            if (casseFsm.ActiveStateName != "Wait 2") return;
            casseStep = 0;
            Joint j = cassePart.GetComponent<HingeJoint>();
            if (j == null) j = cassePart.GetComponent<Joint>();
            string what = j != null ? j.GetType().Name : "pas d'attache";
            if (j != null) Object.Destroy(j);
            string id = MscPartKey(cassePart);
            if (casseSend && Session.Active) Session.SendAll(new NetWriter(Msg.Part).U8(Session.LocalId).Bool(false).Str(casseKey).Str(id).U8(1), true);
            Log.Info("autotest : attache de " + id + " rompue en 'Wait 2' du montage (" + what + ")" + (casseSend ? ", casse envoyee tout de suite" : ""));
        }

        // Essais : attaches de la piece (type, limite, vers quoi, garde de MscKeepOnCopy) et son parent.
        public static string TestJoints(string partKey)
        {
            GameObject part = MscFindPart(partKey);
            if (part == null) return partKey + " introuvable";
            var sb = new System.Text.StringBuilder("attaches de " + partKey + " :");
            int n = 0;
            foreach (Joint j in part.GetComponents<Joint>())
            {
                n++;
                sb.Append(' ').Append(j.GetType().Name).Append(' ').Append(LimitText(j.breakForce)).Append(j.connectedBody != null ? " vers " + j.connectedBody.name : " vers le monde").Append(keptJoints.ContainsKey(j) ? " (garde)" : "").Append(',');
            }
            if (n == 0) sb.Append(" aucune,");
            sb.Append(" parent ").Append(part.transform.parent != null ? part.transform.parent.name : "aucun");
            return sb.ToString();
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

        // Cle de la piece libre neuve d'un demontage (audit MSC 02/11). Avant : « w:msc-out/<piece>#<joueur>-<n> », connue des seuls joueurs
        // presents -- un joueur arrive ensuite chargeait la meme piece libre sous CARPARTS/PartsCar/spindle fl(Clone) ('Load transform' de
        // sa Database), cle Props « w:CARPARTS/... » : jamais la meme, ni pose ni prise en main partagees, et remontee par lui la fusee
        // apparaissait chez l'hote a cote de sa piece libre restee par terre (doublon). Maintenant une cle tiree de l'entree Database, la
        // meme pour tous : « w:libre:db:Database/.../Spindle_FL » (piece « echange » : 'Remove part' en fait le SpawnThis de son entree),
        // donnee aussi a la piece libre rechargee (MscAdoptLibre). Housse : la cle que Props donne a la housse libre a la racine (son nom
        // sans « (Clone) »), celle d'une housse achetee rechargee. Deux demontages en meme temps donnent aussi la meme cle.
        // [Test] LibreCleDb=0 : comme avant.
        static bool libreCle = true;
        static GameObject MscSpawnOf(GameObject dbEntry)
        {
            PlayMakerFSM d = dbEntry != null ? Game.FsmOn(dbEntry, "Data") : null;
            FsmGameObject s = d != null ? d.FsmVariables.FindFsmGameObject("SpawnThis") : null;
            return s != null ? s.Value : null;
        }
        static string MscLibreKey(GameObject dbEntry) { return "w:libre:db:" + Recon.Path(dbEntry.transform); }
        static string MscOutKey(MscOut o, GameObject loose)
        {
            if (libreCle)
            {
                FsmGameObject db = o.Rem.FsmVariables.FindFsmGameObject("db_ThisPart");
                if (db != null && db.Value != null && MscSpawnOf(db.Value) == loose) return MscLibreKey(db.Value);
                if (housses && MultiRemoval(o.Rem) && loose.transform.parent == null) return "w:" + Recon.Path(loose.transform).Replace("(Clone)", "");   // (comme Props.WorldKey a la racine)
            }
            return "w:msc-out/" + o.Id + "#" + Session.LocalId + "-" + (++mscOutSerial);
        }
        // Piece libre « echange » d'une entree pas montee (rechargee de la sauvegarde, ou nee d'un demontage) : la cle commune.
        static int MscAdoptLibre()
        {
            if (!libreCle) return 0;
            int n = 0;
            foreach (MscDbEntry en in mscDb)
            {
                if (en.Swap == null || en.Installed == null || en.Installed.Value || en.Data == null) continue;
                GameObject go = MscSpawnOf(en.Data.gameObject);
                if (go == null || !go.activeInHierarchy || go.GetComponent<Rigidbody>() == null || Game.FsmOn(go, "Removal") != null) continue;
                if (Game.UnderPlayer(go.transform) || Props.RemoteHolder(go) >= 0) continue;   // (tenue : la cle ne change pas en main)
                string k = MscLibreKey(en.Data.gameObject);
                if (Props.KnownWorldKey(go) == k) continue;
                Props.Adopt(go, k); n++;
            }
            if (n > 0) Log.Info("pieces (MSC) : " + n + " piece(s) libre(s) « echange » sous leur cle commune (w:libre:db:...)");
            return n;
        }
        // Essais (audit MSC 02/11, cle commune) : la piece libre de l'entree 'dbPath' (Database/DatabaseBody/Spindle_FL) : cle, position.
        public static string TestLibre(string dbPath)
        {
            GameObject dbg = Game.FindAny(dbPath);
            PlayMakerFSM d = dbg != null ? Game.FsmOn(dbg, "Data") : null;
            FsmBool inst = d != null ? d.FsmVariables.FindFsmBool("Installed") : null;
            GameObject go = MscSpawnOf(dbg);
            if (d == null) return dbPath + " introuvable";
            int loose = 0;
            if (go != null) foreach (Rigidbody rb in Object.FindObjectsOfType<Rigidbody>()) if (rb.name == go.name && rb.gameObject.activeInHierarchy) loose++;
            return dbPath + " Installed=" + (inst != null ? inst.Value.ToString() : "?") + " ; libre " + (go != null ? Recon.Path(go.transform) + (go.activeInHierarchy ? "" : " (eteinte)") + " cle " + (Props.KnownWorldKey(go) ?? "aucune")
                   + " en " + go.transform.position.ToString("F2") : "aucune") + " ; " + loose + " corps actif(s) de ce nom";
        }

        // Demontage d'une piece « echange » fait ici : la nouvelle piece libre ({Part} de 'Removal') recoit une cle commune.
        static void MscTick()
        {
            if (wheelCut.Count > 0) { foreach (FsmStateAction a in wheelCut) a.Enabled = true; wheelCut.Clear(); }
            if (testClick != null) MscTestClickTick();
            MscCheckKept(Time.realtimeSinceStartup);
            MscPrintTick(Time.realtimeSinceStartup);
            if (mscIn.Count > 0) MscCheckIn(Time.realtimeSinceStartup);
            if (pendingRemovals.Count > 0) MscCheckPendingRemovals(Time.realtimeSinceStartup);
            if (casseStep > 0) TestCasseTick();
            if (mscOut.Count == 0) return;
            foreach (MscOut o in mscOut)
            {
                if (o.Rem == null) continue;
                FsmGameObject pv = o.Rem.FsmVariables.FindFsmGameObject("Part");
                GameObject loose = pv != null ? pv.Value : null;
                string outKey = "";
                if (loose != null && loose != o.Rem.gameObject)
                {
                    outKey = MscOutKey(o, loose);
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

        // Montage avorte (desync du 02/11, JD et GG) : la charniere de Door_Left, posee par 'Set joint 2', a rompu chez l'hote pendant 'Wait 2'
        // (0,1 s) ; Removal, eteint par le demontage d'avant, n'a joue 'Remove part' (point rallume, piece detachee, Installed=false) qu'au
        // EnableFSM de 'End' -- qui eteint ensuite le point. Point eteint, {Part} = la portiere par terre, Installed faux : « tenue » pour
        // MscHolds, le controle annoncait Door_Left montee des deux cotes, l'empreinte differait toutes les 4 s pendant 7 min, et JD ne pouvait
        // plus la remonter (point eteint, 14 prises en main sans montage). Point eteint dont la piece n'est pas sous {Parent} et dont l'entree
        // {db_ThisPart} dit Installed faux : piece pas montee (MscHolds, [Test] TenueInstalled=0 : comme avant), point rouvert a la fin de la fenetre de
        // montage (MscAbortedMount, et au montage recu : [Test] MontageAvorte=0 : comme avant). Sans {Parent} ou {db_ThisPart} : rien ne change.
        static bool tenueInstalled = true, montageAvorte = true;
        static bool MscStale(PlayMakerFSM point, GameObject part)
        {
            if (point == null || part == null || point.gameObject.activeSelf) return false;
            FsmGameObject pv = point.FsmVariables.FindFsmGameObject("Parent"), db = point.FsmVariables.FindFsmGameObject("db_ThisPart");
            if (pv == null || pv.Value == null || part.transform.parent == pv.Value.transform || db == null || db.Value == null) return false;
            PlayMakerFSM d = Game.FsmOn(db.Value, "Data");
            FsmBool inst = d != null ? d.FsmVariables.FindFsmBool("Installed") : null;
            return inst != null && !inst.Value;
        }
        // Point eteint qui « tient » une piece libre (MscStale) : rallume, comme 'Remove part' (ActivateGameObject : l'automate repart de
        // 'Check for Part collision'), rien d'envoye. Vrai s'il l'a ete.
        static bool MscReopen(MscPoint p, string why)
        {
            if (!montageAvorte || p == null || p.Fsm == null || p.Swap) return false;
            FsmGameObject pv = p.Fsm.FsmVariables.FindFsmGameObject("Part");
            GameObject part = pv != null ? pv.Value : null;
            if (part == null || !MscStale(p.Fsm, part)) return false;
            pv.Value = null;
            p.Fsm.gameObject.SetActive(true);
            Log.Warn("point " + p.Key + " rouvert : " + MscPartKey(part) + " libre, Installed faux, point eteint (" + why + ")");
            return true;
        }
        // Fin de la fenetre de montage (Parts.CheckMounting, 1 s apres) : montage d'ici avorte -> point rouvert, et le demontage envoye si
        // la voiture est la notre (comme une casse : MscLocalRemove) ; montage rejoue avorte -> point rouvert seulement.
        static void MscAbortedMount(GameObject part, Transform pt)
        {
            if (!montageAvorte || part == null || pt == null) return;
            PlayMakerFSM f = Game.FsmOn(pt.gameObject, "Assembly");
            MscPoint p;
            if (f == null || !mscPointOf.TryGetValue(f, out p)) return;
            FsmGameObject pv = f.FsmVariables.FindFsmGameObject("Part");
            if (pv == null || pv.Value != part) return;
            RemoteMount rmt;
            bool remote = remoteMounts.TryGetValue(part, out rmt) && rmt.Point == p && Time.realtimeSinceStartup - rmt.At < 3f;
            if (!MscReopen(p, "montage " + (remote ? "rejoue" : "d'ici") + " avorte, attache rompue pendant le montage")) return;
            Transform car = VehicleSync.CarRoot(pt);
            Rigidbody cb = car != null ? car.GetComponent<Rigidbody>() : null;
            if (remote || !Session.Active || cb != null && VehicleSync.Authority(cb) != Session.LocalId) return;
            string id = MscPartKey(part);
            Touch(H(p.Key));
            Log.Info("demontage : " + id + " de " + p.Key + " (montage avorte)");
            Session.SendAll(new NetWriter(Msg.Part).U8(Session.LocalId).Bool(false).Str(p.Key).Str(id).U8(0), true);
        }

        static bool MscHolds(PlayMakerFSM point, GameObject part)
        {
            FsmGameObject p = point.FsmVariables.FindFsmGameObject("Part");
            if (p == null || p.Value != part || part == null) return false;
            if (!point.gameObject.activeSelf) return !(tenueInstalled && MscStale(point, part));   // (declencheur eteint par 'End' : piece montee)
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
                    if (!p.Fsm.gameObject.activeSelf) MscReopen(p, "montage recu de #" + who);   // (point bloque par un montage avorte ici : MscStale)
                    if (!p.Fsm.gameObject.activeInHierarchy) { Log.Warn("point " + key + " eteint ici (deja occupe ?) : " + id + " pas montee"); return; }
                    MscApplyMount(p, part, id, key, who);
                    remoteMounts[part] = new RemoteMount { Point = p, Who = who, At = Time.realtimeSinceStartup };
                }
                else MscApplyRemoval(part, key, id, who, flags, outKey, false);
            }
            finally { applying = false; Replay.Depth--; }
        }

        // Casse ou demontage recu pendant la chaine de montage rejouee de la meme piece (desync du 02/11, JD et GG) : montage de Door_Left
        // chez l'hote, charniere rompue 41 ms apres, casse envoyee 145 ms apres le montage ; chez l'invite elle arrivait en 'Wait 2' de son
        // montage rejoue, declencheur encore allume (seul 'End' l'eteint, ~150 ms) : « deja demontee », jetee, puis 'Assemble 2' montait la
        // portiere -- montee chez l'invite, par terre chez l'hote. Gardee et rejouee quand la chaine est finie (point eteint ou piece tenue),
        // 2 s au plus ; seulement apres un montage RECU (remoteMounts) sur ce point. [Test] CasseApresMontage=0 : comme avant.
        class PendingRemoval { public GameObject Part; public string Key, Id, OutKey; public int Who, Flags; public MscPoint Point; public float MountAt, Until; }
        static readonly List<PendingRemoval> pendingRemovals = new List<PendingRemoval>();
        static bool casseApresMontage = true;
        static void MscCheckPendingRemovals(float now)
        {
            for (int i = pendingRemovals.Count - 1; i >= 0; i--)
            {
                PendingRemoval q = pendingRemovals[i];
                RemoteMount rmt;
                bool gone = q.Part == null || q.Point == null || q.Point.Fsm == null || !remoteMounts.TryGetValue(q.Part, out rmt) || rmt.At != q.MountAt;   // (detruite, ou remontee depuis)
                if (!gone && q.Point.Fsm.gameObject.activeSelf && !MscHolds(q.Point.Fsm, q.Part) && now < q.Until) continue;
                pendingRemovals.RemoveAt(i);
                if (gone) continue;
                applying = true; Replay.Depth++;
                try { MscApplyRemoval(q.Part, q.Key, q.Id, q.Who, q.Flags, q.OutKey, true); }
                finally { applying = false; Replay.Depth--; }
            }
        }

        // Demontage ou casse recu (ou rejoue par MscCheckPendingRemovals), sous applying.
        static void MscApplyRemoval(GameObject part, string key, string id, int who, int flags, string outKey, bool noDefer)
        {
            if (part == null) { Log.Warn("piece " + id + " introuvable ici"); return; }
            PlayMakerFSM rem = Game.FsmOn(part, "Removal");
            if (rem == null) { Log.Warn("piece " + id + " sans Removal ici"); return; }
            FsmGameObject trig = rem.FsmVariables.FindFsmGameObject("Trigger");
            // Piece « echange » (SpawnPart : la piece montee s'eteint au demontage) : demontee si elle est eteinte. Son declencheur peut
            // rester allume piece montee (trigger_radiator : 'Already installed' boucle depuis le chargement) -- le demontage du radiateur
            // par l'invite etait ignore chez l'hote (dossier #36, xZOTIK 31/10). [Test] RadiateurAncien=1 : comme avant.
            bool swapPart = rem.FsmVariables.FindFsmGameObject("SpawnPart") != null && Config.GetInt("Test", "RadiateurAncien", 0) == 0;
            RemoteMount rmt; FsmGameObject rpv;
            if (!noDefer && !swapPart && casseApresMontage && remoteMounts.TryGetValue(part, out rmt) && rmt.Point != null && rmt.Point.Fsm != null && rmt.Point.Key == key
                && Time.realtimeSinceStartup - rmt.At < 2f && rmt.Point.Fsm.gameObject.activeSelf && !MscHolds(rmt.Point.Fsm, part)
                && (rpv = rmt.Point.Fsm.FsmVariables.FindFsmGameObject("Part")) != null && rpv.Value == part)
            {
                foreach (PendingRemoval q in pendingRemovals) if (q.Part == part) return;   // (deja en attente : rejouee une fois)
                pendingRemovals.Add(new PendingRemoval { Part = part, Key = key, Id = id, OutKey = outKey, Who = who, Flags = flags, Point = rmt.Point, MountAt = rmt.At, Until = Time.realtimeSinceStartup + 2f });
                Log.Info("piece " + id + " : " + ((flags & 1) != 0 ? "casse" : "demontage") + " de #" + who + " recu pendant son montage (point " + rmt.Point.Fsm.ActiveStateName + "), rejoue a la fin");
                return;
            }
            if (swapPart ? !part.activeSelf : trig != null && trig.Value != null && trig.Value.activeSelf) { Log.Info("piece " + id + " deja demontee"); return; }
            // Casse (flags 1) : d'abord 'Break off' comme chez celui qui mene (BoltCheck, ou 'Use' d'une roue) -- Bolted=false, crans a
            // zero, Tightness 0, Detach. Avant : 'Remove part' seul, la piece par terre ici mais encore « serree » (audit Satsuma 27/10).
            if ((flags & 1) != 0)
                foreach (PlayMakerFSM bf in part.GetComponents<PlayMakerFSM>())
                    if ((bf.FsmName == "BoltCheck" || bf.FsmName == "Use") && bf.Fsm != null && bf.Fsm.GetState("Break off") != null) Game.SetState(bf, "Break off");
            rem.enabled = true;
            // (housse : 'What part' relit les Installed d'ici et choisit sa branche 'Remove part N' -- audit MSC 02/11)
            bool multi = housses && MultiRemoval(rem);
            if (multi && !MultiInstalled(rem)) { Log.Warn("housse " + id + " : aucune entree montee ici, pas demontee"); return; }
            FsmGameObject lv = rem.FsmVariables.FindFsmGameObject("SpawnPart") != null || multi ? rem.FsmVariables.FindFsmGameObject("Part") : null;
            GameObject before = lv != null ? lv.Value : null;
            if (multi) { remoteRemoval[rem] = Time.realtimeSinceStartup + 1f; Game.SetState(rem, WhatPart(rem).Name); }
            else Game.SetState(rem, "Remove part");
            // (echange : la nouvelle piece libre d'ici prend la cle de celle de l'autre et suit sa chute)
            GameObject loose = lv != null && lv.Value != null && lv.Value != part && (!multi || lv.Value != before) ? lv.Value : null;
            if (loose != null && outKey.Length > 0) { Props.Adopt(loose, outKey); Props.HoldFromRemote(loose, who); }
            else if (multi && outKey.Length > 0) mscIn.Add(new MscIn { Rem = rem, Before = before, Key = outKey, Who = who, Until = Time.realtimeSinceStartup + 1f });
            else Props.HoldFromRemote(part, who);
            Log.Info("piece " + id + ((flags & 1) != 0 ? " cassee de " : " demontee de ") + key + " (joueur #" + who + ")");
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
                // (point vide apres une casse refusee sur la copie : annonce plus ancien que tout -- 1e7, un age jamais touche est 1e6 --,
                // celui qui y a la piece la renvoie ; relecture du lot P, desync du 02/11)
                w.I32((int)hk).Str(id).F32(hostWaits.Count > 0 && MscWaitsDriver(hk, id, now) ? 1e7f : Age(hk, now)); n++; total++;
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
