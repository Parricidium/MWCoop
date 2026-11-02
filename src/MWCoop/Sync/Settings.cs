using System.Collections.Generic;
using HutongGames.PlayMaker;
using MWCoop.Net;
using UnityEngine;

namespace MWCoop
{
    // Reglages faits a la main sur les pieces (vis de richesse du carburateur, repartiteur de
    // freinage...) : variables Adjust1..8, Setting, SettingMixture, AdjustmentF des automates des
    // pieces. Surveillees deux fois par seconde sur les pieces a moins de 4 m du joueur (c'est lui
    // qui regle) ; un changement part aux autres, qui recopient la valeur et, si l'automate a une
    // transition globale ADJUST, la declenchent pour que l'effet soit recalcule.
    public static class Settings
    {
        // TierodLeft/RightSetting : parallelisme, sur le Data de la cremaillere (sa vis de reglage ajoute +-0,05 et envoie
        // ADJUSTMENT, mais seulement avec la cle a cliquet en main : rejouee chez l'invite elle ne faisait rien -- Vaydon, 16/10).
        static readonly string[] Names = { "Adjust1", "Adjust2", "Adjust3", "Adjust4", "Adjust5", "Adjust6", "Adjust7", "Adjust8",
                                           "Setting", "SettingMixture", "AdjustmentF", "TierodLeftSetting", "TierodRightSetting" };
        static bool anyRange;   // essais : sans la regle des 4 m

        class Watch { public string Id; public PlayMakerFSM Fsm; public FsmFloat Var; public float Last, NextSend; public FsmGameObject Part; }

        // My Summer Car (vidage v.250908-04) : pas d'ID sur les pieces ; les reglages sont dans les automates 'Data' de Database/*
        // (les vis et molettes y ecrivent par SetFsmFloat). Cle "p:" + chemin ; la regle des 4 m se mesure a la piece (ThisPart :
        // les objets Database sont tous a l'origine).
        static readonly string[][] MscTable = {
            new[] { "DatabaseMotor/Carburator", "IdleAdjust" },
            new[] { "DatabaseOrders/Twin Carburators", "IdleAdjust" },
            // (carbus de course : les 4 vis, puis les valeurs derivees que la chaine ADJUST du jeu recalcule de facon cumulative -- recopiees
            // telles quelles apres les vis, sans ADJUST (Derived), pour que l'invite ait exactement celles de l'hote ; JD 27/10)
            new[] { "DatabaseOrders/Racing Carburators", "Adjust1", "Adjust2", "Adjust3", "Adjust4", "AdjustAverage", "AdjustMax", "AdjustMin", "DifferenceMax", "DifferenceMin" },
            new[] { "DatabaseMotor/RockerShaft", "cyl1intake", "cyl1exhaust", "cyl2intake", "cyl2exhaust", "cyl3intake", "cyl3exhaust", "cyl4intake", "cyl4exhaust" },
            new[] { "DatabaseMotor/Distributor", "SparkAngle" },
            new[] { "DatabaseOrders/N2O Bottle", "Setting" },
            new[] { "DatabaseOrders/Shock Rally FL", "SettingBump", "SettingRebound" },
            new[] { "DatabaseOrders/Shock Rally FR", "SettingBump", "SettingRebound" },
            new[] { "DatabaseOrders/Shock Rally RL", "SettingBump", "SettingRebound" },
            new[] { "DatabaseOrders/Shock Rally RR", "SettingBump", "SettingRebound" },
            new[] { "DatabaseOrders/Tachometer", "RPMSetting" },
            // (passe Satsuma 27/10) calage de la distribution : angle du pignon d'arbre a cames, TIRE AU SORT par le jeu a chaque chargement
            // tant que le pignon n'est pas monte ('Rand angle') -- different chez chacun, moteur regle chez l'un qui cogne chez l'autre ;
            // parallelisme : tirage de meme (-6..6 a chaque chargement) puis la vis de la biellette ; rapport de pont (travaux Fleetari).
            // Rien ne change a chaud pour ces trois-la sans geste du joueur : c'est l'instantane de l'hote (arrivant) qui les aligne.
            new[] { "DatabaseMotor/CamshaftGear", "Angle" },
            new[] { "DatabaseBody/SteeringRod_FL", "Alignment" },
            new[] { "DatabaseBody/SteeringRod_FR", "Alignment" },
            new[] { "DatabaseMechanics/Gears", "FinalGear" },
        };

        // Arrivant (passe Satsuma 27/10) : l'hote lui envoie toutes les valeurs 20 s apres son entree en jeu (ses automates releves a 12 s).
        // Avant, rien : les reglages venaient de la sauvegarde recue, sauf ceux que le jeu retire au sort au chargement (voir MscTable).
        static readonly List<KeyValuePair<float, Peer>> snapshots = new List<KeyValuePair<float, Peer>>();
        public static void ScheduleSnapshot(Peer p) { snapshots.Add(new KeyValuePair<float, Peer>(Time.realtimeSinceStartup + 20f, p)); }
        static void SendSnapshots(float now)
        {
            for (int i = snapshots.Count - 1; i >= 0; i--)
            {
                if (now < snapshots[i].Key) continue;
                Peer p = snapshots[i].Value;
                snapshots.RemoveAt(i);
                if (!p.Accepted || !Session.T.Peers.Contains(p)) continue;
                int n = 0;
                foreach (Watch w in watches)
                {
                    if (w.Fsm == null || float.IsNaN(w.Var.Value) || float.IsInfinity(w.Var.Value)) continue;
                    Session.T.SendReliable(p, new NetWriter(Msg.Setting).U8(Session.LocalId).Str(w.Id).Str(w.Fsm.FsmName).Str(w.Var.Name).F32(w.Var.Value).ToArray());
                    n++;
                }
                Log.Info("reglages : instantane de " + n + " valeurs envoye a " + p);
            }
        }

        static bool Derived(string name) { return name == "AdjustAverage" || name == "AdjustMax" || name == "AdjustMin" || name == "DifferenceMax" || name == "DifferenceMin"; }
        static void ScanMsc()
        {
            if (watches.Count > 0) return;   // (objets fixes : un releve suffit)
            foreach (string[] row in MscTable)
            {
                GameObject g = Game.FindAny("Database/" + row[0]);
                PlayMakerFSM f = g != null ? Game.FsmOn(g, "Data") : null;
                if (f == null) continue;
                // (pas de ThisPart -- biellettes SteeringRod_FL/FR : la piece montee, ActivateThis ; sinon la regle des 4 m se mesurait a
                // l'objet Database, a l'origine du monde, et un parallelisme regle au cliquet n'etait jamais envoye -- audit MSC 02/11.
                // [Test] ParallelismeRoue=0 : comme avant)
                FsmGameObject part = f.FsmVariables.FindFsmGameObject("ThisPart");
                if (part == null && Config.GetInt("Test", "ParallelismeRoue", 1) != 0) part = f.FsmVariables.FindFsmGameObject("ActivateThis");
                for (int i = 1; i < row.Length; i++)
                {
                    FsmFloat v = f.FsmVariables.FindFsmFloat(row[i]);
                    if (v != null) watches.Add(new Watch { Id = "p:Database/" + row[0], Fsm = f, Var = v, Last = v.Value, Part = part });
                }
            }
            // Valeurs derivees (carbus de course : AdjustAverage, AdjustMax/Min, DifferenceMax/Min -> Mixture.AirRatio) : le jeu ne les
            // recalcule qu'a un reglage (ADJUST) ; pas sauvees, elles restaient a zero chez l'hote et calculees chez l'invite (qui recoit ADJUST
            // avec l'instantane) -- JD 27/10 : « chaque variable les memes pour tout le monde ». Recalcul une fois au releve, chez chacun.
            int adj = 0;
            var done = new HashSet<PlayMakerFSM>();
            foreach (Watch w in watches)
            {
                if (w.Fsm == null || done.Contains(w.Fsm)) continue;
                done.Add(w.Fsm);
                foreach (FsmTransition t in w.Fsm.Fsm.GlobalTransitions) if (t.EventName == "ADJUST") { w.Fsm.SendEvent("ADJUST"); adj++; break; }
            }
            Log.Info("reglages (MSC) : " + watches.Count + " suivis" + (adj > 0 ? ", " + adj + " recalcul(s) ADJUST" : ""));
        }
        static readonly List<Watch> watches = new List<Watch>();
        static readonly HashSet<PlayMakerFSM> seen = new HashSet<PlayMakerFSM>();
        // Automates hors des pieces : ecartes jusqu'au prochain grand releve (toutes les 2 min) -- le releve de toute la
        // scene toutes les 20 s coutait 15 ms (a-coup ; retour de joueurs, 10/10).
        static readonly HashSet<PlayMakerFSM> noPart = new HashSet<PlayMakerFSM>();
        static float noPartClear;
        static float nextScan = -1, nextPoll;
        static Transform player;

        public static void OnLevelLoaded()
        {
            watches.Clear(); seen.Clear(); noPart.Clear(); player = null; snapshots.Clear(); screwsOf = null; suspension = null;
            if (Config.GetInt("Test", "ReglagesPartout", 0) != 0) anyRange = true;   // (essais : sans la regle des 4 m)
            nextScan = PlayerSync.InGame ? Time.realtimeSinceStartup + 12f : -1;
        }

        static void Scan()
        {
            if (Time.realtimeSinceStartup >= noPartClear) { noPartClear = Time.realtimeSinceStartup + 120f; noPart.Clear(); }
            foreach (Object o in Game.AllFsms())
            {
                var f = (PlayMakerFSM)o; if (f == null) continue;
                if (seen.Contains(f) || noPart.Contains(f) || f.hideFlags != HideFlags.None || f.FsmName == "Paint") continue;
                if (f.FsmName == "Data" && f.FsmVariables.FindFsmFloat("TierodLeftSetting") == null) continue;   // (Data : la cremaillere seule)
                string id = PartIdOf(f.transform);
                if (id.Length == 0) { noPart.Add(f); continue; }
                seen.Add(f);
                foreach (string n in Names)
                {
                    FsmFloat v = f.FsmVariables.FindFsmFloat(n);
                    if (v != null) watches.Add(new Watch { Id = id, Fsm = f, Var = v, Last = v.Value });
                }
            }
        }

        // ID de la piece qui porte cet objet (lui-meme ou un parent : bouton, molette...).
        static string PartIdOf(Transform t)
        {
            for (int i = 0; t != null && i < 4; i++, t = t.parent)
            {
                string id = Parts.IdOf(t.gameObject);
                if (id.Length > 0) return id;
            }
            return "";
        }

        public static void Update()
        {
            if (!Session.Active || nextScan < 0) return;
            float now = Time.realtimeSinceStartup;
            if (now >= nextScan) { nextScan = now + 20f; if (Game.Msc) ScanMsc(); else Scan(); }
            if (snapshots.Count > 0 && Session.IsHost) SendSnapshots(now);
            if (now < nextPoll) return;
            nextPoll = now + 0.5f;
            if (player == null) { GameObject p = Game.PlayerGo; if (p == null) return; player = p.transform; }
            foreach (Watch w in watches)
            {
                if (w.Fsm == null || Mathf.Abs(w.Var.Value - w.Last) < 1e-4f) continue;
                Vector3 at = w.Part != null && w.Part.Value != null ? w.Part.Value.transform.position : w.Fsm.transform.position;
                if (now < w.NextSend || (!anyRange && (at - player.position).sqrMagnitude > 16f)) { w.Last = w.Var.Value; continue; }
                w.Last = w.Var.Value;
                w.NextSend = now + 0.5f;
                Session.SendAll(new NetWriter(Msg.Setting).U8(Session.LocalId).Str(w.Id).Str(w.Fsm.FsmName).Str(w.Var.Name).F32(w.Var.Value), true);
            }
        }

        public static void OnMessage(Peer from, NetReader r)
        {
            int who = r.U8();
            if (Session.IsHost) who = from.Id;
            string id = r.Str(), fsm = r.Str(), name = r.Str();
            float value = r.F32();
            if (Session.IsHost) Session.Broadcast(new NetWriter(Msg.Setting).U8(who).Str(id).Str(fsm).Str(name).F32(value), true, who);
            if (Game.Msc && watches.Count == 0 && PlayerSync.InGame) ScanMsc();   // (recu avant le releve des 12 s : « introuvable ici » a tort, 27/10)
            foreach (Watch w in watches)
            {
                if (w.Id != id || w.Fsm == null || w.Fsm.FsmName != fsm || w.Var.Name != name) continue;
                w.Var.Value = value;
                w.Last = value;
                if (!Derived(name))
                    foreach (FsmTransition t in w.Fsm.Fsm.GlobalTransitions)
                        if (t.EventName == "ADJUST") { w.Fsm.SendEvent("ADJUST"); break; }
                // Cremaillere montee (etat Bolted) : ADJUSTMENT borne la valeur et la passe a la voiture, qui tourne la roue (ToeFL/FR).
                if (name.StartsWith("Tierod")) w.Fsm.SendEvent("ADJUSTMENT");
                string fx = Game.Msc ? ApplyMsc(w, name, value) : "";
                Log.Info("reglage " + id + " " + fsm + "." + name + " = " + value + " (joueur #" + who + ")" + fx);
                return;
            }
            Log.Warn("reglage " + id + " " + fsm + "." + name + " introuvable ici");
        }

        // My Summer Car (audit MSC 02/11) : les Data de la Database n'ont pas de transition ADJUST -- la valeur recopiee seule laissait chez
        // celui qui recoit ce que le geste du joueur fait en plus dans le jeu (instantane de l'arrivant compris, meme chemin) :
        //  - amortisseurs rallye (vis DamperAdjustment, 'Setup') : Suspension.Rally{Front|Rear}{L|R}{Bump|Rebound}, que la boucle de 1 s de
        //    Suspension pose sur wheelXX.bumpRate / reboundRate -- la voiture conduite par celui qui recoit gardait l'ancienne tenue de route
        //    jusqu'au rechargement. [Test] AmortisRallye=0 : comme avant.
        //  - parallelisme (vis de la biellette, 'Wait1') : rotation locale y de wheelFL/FR, bornee -6..6 ('Installed' du Data ne la pose
        //    qu'au chargement) -- la voiture roulait sans le reglage chez l'autre. [Test] ParallelismeRoue=0 : comme avant.
        //  - avance (HandRotate du distributeur) et calage (BoltCheck 'Adjust' du pignon d'arbre a cames) : le jeu repart de la piece 3D a
        //    chaque cran (GetRotation de Pivot / camshaft_gear_mesh) -- restee a l'ancienne position chez celui qui recoit, son cran suivant
        //    ecrasait chez tous la valeur de l'autre (ancienne + pas). Rotation posee comme 'Activate' / 'Set angle'. [Test] AllumageCalage=0 : comme avant.
        //  - vis de ralenti du carbu et vis des amortisseurs rallye ('Screw') : {Alignment} n'est relu de la Database qu'a l'entree en 'Idle',
        //    leur cran suivant partait de l'ancienne valeur (12 + 0,2 au lieu de 15 + 0,2) et l'envoyait a tous. L'automate repasse par 'Idle'
        //    (relit la Database, tourne la molette ; pas 'Setup', qui renverrait la valeur). [Test] VisReglageRelue=0 : comme avant.
        static PlayMakerFSM suspension;
        static Dictionary<GameObject, List<PlayMakerFSM>> screwsOf;
        static string ApplyMsc(Watch w, string name, float value)
        {
            string fx = "";
            try
            {
                string id = w.Id;
                if ((name == "SettingBump" || name == "SettingRebound") && id.StartsWith("p:Database/DatabaseOrders/Shock Rally ") && Config.GetInt("Test", "AmortisRallye", 1) != 0)
                {
                    string c = id.Substring(id.Length - 2);   // (FL, FR, RL, RR)
                    string v = "Rally" + (c[0] == 'F' ? "Front" : "Rear") + c[1] + (name == "SettingBump" ? "Bump" : "Rebound");
                    if (suspension == null) { GameObject s = Game.FindAny("SATSUMA(557kg, 248)/CarSimulation/Car/Suspension"); suspension = s != null ? Game.FsmOn(s, "Suspension") : null; }
                    FsmFloat sv = suspension != null ? suspension.FsmVariables.FindFsmFloat(v) : null;
                    if (sv != null) { sv.Value = value; fx += ", Suspension." + v; }
                }
                if (name == "Alignment" && id.StartsWith("p:Database/DatabaseBody/SteeringRod_") && Config.GetInt("Test", "ParallelismeRoue", 1) != 0)
                {
                    FsmGameObject wh = w.Fsm.FsmVariables.FindFsmGameObject("Wheel");
                    FsmBool ins = w.Fsm.FsmVariables.FindFsmBool("Installed");
                    if (wh != null && wh.Value != null && (ins == null || ins.Value))
                    {
                        Transform t = wh.Value.transform;
                        Vector3 e = t.localEulerAngles;
                        t.localEulerAngles = new Vector3(e.x, Mathf.Clamp(value, -6f, 6f), e.z);   // (SetRotation yAngle, space Self)
                        fx += ", " + t.name + " tournee";
                    }
                }
                if ((name == "SparkAngle" || name == "Angle" && id.EndsWith("/CamshaftGear")) && Config.GetInt("Test", "AllumageCalage", 1) != 0)
                {
                    GameObject tp = w.Part != null ? w.Part.Value : null;
                    FsmGameObject mv = w.Fsm.FsmVariables.FindFsmGameObject("Mesh");
                    // (par le transform de la piece et pas par son chemin CARPARTS : montee, elle est sous le bloc moteur)
                    Transform r = name == "SparkAngle" ? (tp != null ? tp.transform.Find("Pivot") : null)
                                : mv != null && mv.Value != null ? mv.Value.transform : tp != null ? tp.transform.Find("camshaft_gear_mesh") : null;
                    if (r != null)
                    {
                        r.localRotation = name == "SparkAngle" ? Quaternion.Euler(0f, 0f, Mathf.Clamp(value, 0f, 20f)) : Quaternion.Euler(value, 0f, 0f);
                        fx += ", " + r.name + " tourne";
                    }
                }
                if ((name == "IdleAdjust" || name == "SettingBump" || name == "SettingRebound") && Config.GetInt("Test", "VisReglageRelue", 1) != 0)
                {
                    int n = ResyncScrews(w.Fsm.gameObject, name, value);
                    if (n > 0) fx += ", " + n + " vis relue(s)";
                }
            }
            catch (System.Exception e) { Log.Warn("reglage " + w.Id + "." + name + " : effet dans le jeu : " + e.Message); }
            return fx;
        }

        // Automates 'Screw' qui reglent une entree de la Database (variable db_PartRequired), releves une fois par niveau.
        static List<PlayMakerFSM> ScrewsOf(GameObject data)
        {
            if (screwsOf == null)
            {
                screwsOf = new Dictionary<GameObject, List<PlayMakerFSM>>();
                foreach (Object o in Game.FsmsNamed("Screw"))
                {
                    var f = o as PlayMakerFSM;
                    if (f == null || f.hideFlags != HideFlags.None) continue;
                    FsmGameObject db = f.FsmVariables.FindFsmGameObject("db_PartRequired");
                    if (db == null || db.Value == null) continue;
                    List<PlayMakerFSM> l;
                    if (!screwsOf.TryGetValue(db.Value, out l)) screwsOf[db.Value] = l = new List<PlayMakerFSM>();
                    l.Add(f);
                }
            }
            List<PlayMakerFSM> r;
            return screwsOf.TryGetValue(data, out r) ? r : null;
        }
        // Vis dont l'etat 'Idle' lit 'name' de cette entree (GetFsmFloat -> {Alignment}) : {Alignment} = value, et 'Idle' rejoue si l'automate
        // tourne (molette a sa place). Les autres vis de la meme entree (rebond / detente) ne bougent pas.
        static int ResyncScrews(GameObject data, string name, float value)
        {
            List<PlayMakerFSM> l = ScrewsOf(data);
            if (l == null) return 0;
            int n = 0;
            foreach (PlayMakerFSM f in l)
            {
                if (f == null || f.Fsm == null) continue;
                FsmState idle = f.Fsm.GetState("Idle");
                if (idle == null || !IdleReads(idle, name)) continue;
                FsmFloat al = f.FsmVariables.FindFsmFloat("Alignment");
                if (al != null) al.Value = value;
                if (f.enabled && f.gameObject.activeInHierarchy) Game.SetState(f, "Idle");
                n++;
            }
            return n;
        }
        static bool IdleReads(FsmState idle, string name)
        {
            FsmStateAction[] acts;
            try { acts = idle.Actions; } catch { return false; }
            if (acts == null) return false;
            foreach (FsmStateAction a in acts)
            {
                if (a == null || a.GetType().Name != "GetFsmFloat") continue;
                System.Reflection.FieldInfo vf = a.GetType().GetField("variableName");
                FsmString vn = vf != null ? vf.GetValue(a) as FsmString : null;
                if (vn != null && (vn.Value == name || vn.Name == name)) return true;
            }
            return false;
        }

        // Essais (audit MSC 02/11) : 'what' = ralenti, amorti (RL, detente), parallelisme (FL), avance, calage. TestMscSet pose la valeur ici
        // comme le geste du joueur (Data + effets dans le jeu ; envoyee aux autres par Update, sans la regle des 4 m) ; TestMscScrew
        // donne un cran a la vis (TIGHTEN, comme le joueur) ; TestMscState : la valeur et ses effets ici.
        static string[] TestRow(string what)
        {
            switch (what)
            {
                case "ralenti": return new[] { "p:Database/DatabaseMotor/Carburator", "IdleAdjust" };
                case "amorti": return new[] { "p:Database/DatabaseOrders/Shock Rally RL", "SettingBump" };
                case "parallelisme": return new[] { "p:Database/DatabaseBody/SteeringRod_FL", "Alignment" };
                case "avance": return new[] { "p:Database/DatabaseMotor/Distributor", "SparkAngle" };
                case "calage": return new[] { "p:Database/DatabaseMotor/CamshaftGear", "Angle" };
            }
            return null;
        }
        static Watch TestWatch(string what)
        {
            string[] row = TestRow(what);
            if (row == null) return null;
            if (watches.Count == 0 && Game.Msc) ScanMsc();
            foreach (Watch w in watches) if (w.Id == row[0] && w.Var.Name == row[1]) return w;
            return null;
        }
        public static string TestMscSet(string what, float value)
        {
            anyRange = true;
            Watch w = TestWatch(what);
            if (w == null) return "reglage " + what + " introuvable";
            w.Var.Value = value;
            return what + " = " + value + " ici" + ApplyMsc(w, w.Var.Name, value);
        }
        public static string TestMscScrew(string what, bool tighten)
        {
            anyRange = true;
            Watch w = TestWatch(what);
            List<PlayMakerFSM> l = w != null ? ScrewsOf(w.Fsm.gameObject) : null;
            if (l != null)
                foreach (PlayMakerFSM f in l)
                {
                    FsmState idle = f != null && f.Fsm != null ? f.Fsm.GetState("Idle") : null;
                    if (idle == null || !IdleReads(idle, w.Var.Name) || !f.gameObject.activeInHierarchy) continue;
                    f.SendEvent(tighten ? "TIGHTEN" : "UNTIGHTEN");
                    return what + " : cran " + (tighten ? "TIGHTEN" : "UNTIGHTEN") + " -> " + w.Var.Value;
                }
            return what + " : vis introuvable ou eteinte";
        }
        public static string TestMscState()
        {
            var sb = new System.Text.StringBuilder("reglages ici :");
            foreach (string what in new[] { "ralenti", "amorti", "parallelisme", "avance", "calage" })
            {
                Watch w = TestWatch(what);
                if (w == null) { sb.Append(' ').Append(what).Append(" ? ;"); continue; }
                sb.Append(' ').Append(what).Append('=').Append(w.Var.Value.ToString("F2"));
                List<PlayMakerFSM> l = ScrewsOf(w.Fsm.gameObject);
                if (l != null)
                    foreach (PlayMakerFSM f in l)
                    {
                        FsmState idle = f != null && f.Fsm != null ? f.Fsm.GetState("Idle") : null;
                        FsmFloat al = f != null ? f.FsmVariables.FindFsmFloat("Alignment") : null;
                        if (idle != null && al != null && IdleReads(idle, w.Var.Name)) { sb.Append(" vis ").Append(al.Value.ToString("F2")); break; }
                    }
                if (what == "amorti")
                {
                    if (suspension == null) { GameObject s = Game.FindAny("SATSUMA(557kg, 248)/CarSimulation/Car/Suspension"); suspension = s != null ? Game.FsmOn(s, "Suspension") : null; }
                    FsmFloat sv = suspension != null ? suspension.FsmVariables.FindFsmFloat("RallyRearLBump") : null;
                    sb.Append(" Suspension ").Append(sv != null ? sv.Value.ToString("F0") : "?");
                }
                if (what == "parallelisme") { FsmGameObject wh = w.Fsm.FsmVariables.FindFsmGameObject("Wheel"); sb.Append(" roue y ").Append(wh != null && wh.Value != null ? wh.Value.transform.localEulerAngles.y.ToString("F2") : "?"); }
                GameObject tp = w.Part != null ? w.Part.Value : null;
                if (what == "avance") { Transform pv = tp != null ? tp.transform.Find("Pivot") : null; sb.Append(" Pivot z ").Append(pv != null ? pv.localEulerAngles.z.ToString("F2") : "?"); }
                if (what == "calage") { FsmGameObject mv = w.Fsm.FsmVariables.FindFsmGameObject("Mesh"); sb.Append(" pignon x ").Append(mv != null && mv.Value != null ? mv.Value.transform.localEulerAngles.x.ToString("F2") : "?"); }
                sb.Append(" ;");
            }
            return sb.ToString();
        }

        // Essais : parallelisme -- la cremaillere montee : +0,5 sur la biellette gauche puis ADJUSTMENT (comme sa vis) si
        // 'bump' ; etat (piece, voiture, angle de la roue).
        public static string TestToe(bool bump)
        {
            anyRange = true;
            PlayMakerFSM rack = null, any = null;
            foreach (Object o in Game.AllFsms())
            {
                var f = (PlayMakerFSM)o;
                if (f == null || f.FsmName != "Data" || f.FsmVariables.FindFsmFloat("TierodLeftSetting") == null || !f.gameObject.activeInHierarchy) continue;
                if (f.transform.root.name.StartsWith("CORRIS")) { rack = f; break; }
                if (any == null) any = f;
            }
            if (rack == null) rack = any;
            if (rack == null) return "cremaillere introuvable";
            FsmFloat v = rack.FsmVariables.GetFsmFloat("TierodLeftSetting");
            if (bump) { v.Value += 0.5f; rack.SendEvent("ADJUSTMENT"); }
            GameObject car = Game.FindAny("CORRIS/PhysicalAssemblies/FRONT/AxleFront/VINP_SteeringRack");
            PlayMakerFSM cd = car != null ? Game.FsmOn(car, "Data") : null;
            FsmFloat cv = cd != null ? cd.FsmVariables.FindFsmFloat("TieRodLeftSetting") : null;
            GameObject toe = Game.FindAny("CORRIS/PhysicalAssemblies/FRONT/AxleFront/LinkFL/ToeFL");
            return rack.gameObject.name + " (" + rack.ActiveStateName + ") TierodLeftSetting=" + v.Value.ToString("F2")
                   + " ; voiture TieRodLeftSetting=" + (cv != null ? cv.Value.ToString("F2") : "?")
                   + " ; ToeFL y=" + (toe != null ? toe.transform.localEulerAngles.y.ToString("F2") : "?") + " ; " + watches.Count + " reglages suivis";
        }

        // Essais : change le premier reglage suivi de la piece la plus proche du joueur.
        public static string TestNearest()
        {
            if (player == null) return "pas de joueur";
            Watch best = null;
            float bd = float.MaxValue;
            foreach (Watch w in watches)
            {
                if (w.Fsm == null) continue;
                float d = (w.Fsm.transform.position - player.position).sqrMagnitude;
                if (d < bd) { bd = d; best = w; }
            }
            if (best == null) return "aucun reglage suivi (" + watches.Count + ")";
            best.Var.Value += 1.5f;
            return best.Id + " " + best.Fsm.FsmName + "." + best.Var.Name + " = " + best.Var.Value + " (" + watches.Count + " suivis)";
        }
    }
}
