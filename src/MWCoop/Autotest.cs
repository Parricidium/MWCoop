using HutongGames.PlayMaker;
using UnityEngine;

namespace MWCoop
{
    // Scenarios des instances de test ([Test] Autotest=...), pilotes par le contenu (jamais de
    // touches injectees). Le deroule des menus (PasserIntro, Continuer, NouvellePartie) est dans Flow.
    //  marche : le joueur avance et tourne en rond (avatar vu par les autres)
    public static class Autotest
    {
        static float t0 = -1;
        static System.Collections.Generic.List<float> captureTimes;

        // [Test] BoissonDefile=1 (hote) : de 60 s, chaque boisson de Drinks.Names 8 s a tour de role, en buvant ;
        // [Test] CaptureBoisson=1 (invite, avec CameraAvatar) : capture dumps\ecran-boisson-<nom>.png 3 s apres chaque
        // nouvelle boisson dans la main de l'avatar.
        public static int DrinkCycle
        {
            get
            {
                if (t0 < 0 || Config.GetInt("Test", "BoissonDefile", 0) == 0) return 0;
                float t = Time.realtimeSinceStartup - t0 - 60f;
                if (t < 0f) return 0;
                int i = 1 + (int)(t / 8f);
                return i < Drinks.Names.Length ? i : 0;
            }
        }
        // My Summer Car (19/10) : [Test] Autotest=mscvoiture. L'hote monte dans [Test] TestVoiture (KEKMET par defaut) a 18/22 s,
        // moteur tenu (1500 tr/min) de 24 a 60 s, poussee en avant (6 m/s) de 26 a 38 s ; il ouvre [Test] TestPorte (la premiere
        // par defaut) a 45 s, la relache, et la referme a 65 s. Chacun note la voiture toutes les 2 s et la portiere des 42 s
        // (l'invite doit voir la copie bouger, puis la portiere ouverte et refermee).
        static int mscStep, mscFast; static float mscLog;
        // [Test] Autotest=mscmeca (19/10) : l'hote monte la portiere gauche de la SATSUMA a 60 s (comme un clic : 'Wait for assembly'
        // -ASSEMBLE->), serre deux crans d'une de ses vis a 70 s, fait des courses chez Teimo a 80 s (QSausages = 2, 'Purchase') et
        // demonte la portiere a 100 s (l'invite arrive vers 35 s). Chacun note toutes les 5 s : portiere montee ? (declencheur eteint, parent), vis, argent.
        static int mecaStep; static float mecaLog;
        const string MecaTrigger = "SATSUMA(557kg, 248)/Body/trigger_door_left", MecaDoor = "CARPARTS/PartsCar/door left(Clone)";
        static GameObject mecaDoor;
        static void MscMeca(float t)
        {
            bool host = Net.Session.IsHost;
            GameObject trig = Game.FindAny(MecaTrigger);
            PlayMakerFSM asm = trig != null ? Game.FsmOn(trig, "Assembly") : null;
            if (mecaDoor == null) mecaDoor = Game.FindAny(MecaDoor);
            if (mecaDoor == null && trig != null)
                foreach (Transform x in trig.transform.root.GetComponentsInChildren<Transform>(true)) if (x.name == "door left(Clone)") { mecaDoor = x.gameObject; break; }
            if (host && t > 60f && mecaStep == 0 && asm != null && mecaDoor != null)
            {
                mecaStep = 1;
                asm.FsmVariables.GetFsmGameObject("Part").Value = mecaDoor;
                Game.SetState(asm, "Wait for assembly");
                asm.SendEvent("ASSEMBLE");
                Log.Info("autotest : mscmeca, portiere -> " + asm.ActiveStateName);
            }
            if (host && t > 70f && mecaStep == 1 && mecaDoor != null)
            {
                mecaStep = 2;
                foreach (PlayMakerFSM f in mecaDoor.GetComponentsInChildren<PlayMakerFSM>(true))
                {
                    if (f.FsmName != "Screw" || f.FsmVariables.FindFsmInt("Stage") == null) continue;
                    f.SendEvent("TIGHTEN"); f.SendEvent("TIGHTEN");
                    Log.Info("autotest : mscmeca, vis " + Recon.Path(f.transform) + " -> " + f.FsmVariables.GetFsmInt("Stage").Value + (f.gameObject.activeInHierarchy ? "" : " (inactive)"));
                    break;
                }
            }
            if (host && t > 80f && mecaStep == 2)
            {
                mecaStep = 3;
                PlayMakerFSM reg = Game.FindFsm("STORE/StoreCashRegister/Register", "Data");
                if (reg == null) Log.Info("autotest : mscmeca, caisse de Teimo introuvable");
                else
                {
                    reg.FsmVariables.GetFsmInt("QSausages").Value = 2;
                    reg.FsmVariables.GetFsmFloat("Total").Value = 20f;
                    Game.SetState(reg, "Purchase");
                    Log.Info("autotest : mscmeca, courses chez Teimo -> " + reg.ActiveStateName + ", " + Wallet.State());
                }
            }
            if (host && t > 100f && mecaStep == 3 && mecaDoor != null)
            {
                mecaStep = 4;
                PlayMakerFSM rem = Game.FsmOn(mecaDoor, "Removal");
                if (rem != null) { rem.enabled = true; Game.SetState(rem, "Remove part"); }
                Log.Info("autotest : mscmeca, portiere demontee -> " + (rem != null ? rem.ActiveStateName : "pas de Removal"));
            }
            if (t > 55f && t < 130f && Time.realtimeSinceStartup >= mecaLog)
            {
                mecaLog = Time.realtimeSinceStartup + 5f;
                string bolt = "?";
                if (mecaDoor != null)
                    foreach (PlayMakerFSM f in mecaDoor.GetComponentsInChildren<PlayMakerFSM>(true))
                        if (f.FsmName == "Screw" && f.FsmVariables.FindFsmInt("Stage") != null) { bolt = f.FsmVariables.GetFsmInt("Stage").Value.ToString(); break; }
                Log.Info("autotest : mscmeca (" + (host ? "hote" : "invite") + ") declencheur " + (trig == null ? "absent" : trig.activeSelf ? "allume" : "eteint")
                         + (asm != null ? " [" + asm.ActiveStateName + "]" : "") + ", portiere " + (mecaDoor == null ? "absente" : "sous " + (mecaDoor.transform.parent != null ? mecaDoor.transform.parent.name : "racine"))
                         + ", 1re vis " + bolt + ", " + Wallet.State());
            }
        }

        // [Test] Autotest=mscchassis (20/10, retour de $forever1 : pieces montees par l'invite restees par terre chez l'hote) -- pieces
        // « echange » jouees par l'INVITE : 60 s sub frame sur trigger_subframe ; 70 s ses vis au cran 8 ; 85 s cremaillere sur
        // trigger_steering_axle (chemin 'Check bolts') ; 100 s cremaillere demontee ; 115 s la nouvelle cremaillere libre remontee.
        // Chacun note toutes les 5 s : declencheurs, pieces montees allumees, Database Installed / Bolted, pieces libres.
        static int chassisStep; static float chassisLog; static GameObject chassisNewRack;
        const string ChassisCar = "SATSUMA(557kg, 248)/Chassis/";
        static string DbState(string path)
        {
            PlayMakerFSM d = Game.FindFsm(path, "Data");
            if (d == null) return "?";
            FsmBool i = d.FsmVariables.FindFsmBool("Installed"), b = d.FsmVariables.FindFsmBool("Bolted");
            return (i != null && i.Value ? "monte" : "pas monte") + (b != null ? (b.Value ? "/boulonne" : "/pas boulonne") : "");
        }
        static GameObject Loose(string name)
        {
            GameObject pc = Game.FindAny("CARPARTS/PartsCar");
            if (pc != null) foreach (Transform x in pc.transform) if (x.name == name && x.gameObject.activeInHierarchy) return x.gameObject;
            foreach (GameObject g in Object.FindObjectsOfType<GameObject>()) if (g.name == name && g.transform.parent == null && g.activeInHierarchy) return g;
            return null;
        }
        static void ChassisAssemble(string trigger, GameObject part, string what)
        {
            GameObject trig = Game.FindAny(ChassisCar + trigger);
            PlayMakerFSM asm = trig != null ? Game.FsmOn(trig, "Assembly") : null;
            if (asm == null || part == null) { Log.Info("autotest : mscchassis, " + what + " : " + (asm == null ? "point absent" : "piece libre absente")); return; }
            asm.FsmVariables.GetFsmGameObject("Part").Value = part;
            Game.SetState(asm, "Wait for assembly");
            asm.SendEvent("ASSEMBLE");
            Log.Info("autotest : mscchassis, " + what + " -> " + asm.ActiveStateName);
        }
        static void MscChassis(float t)
        {
            bool host = Net.Session.IsHost;
            if (!host && t > 60f && chassisStep == 0) { chassisStep = 1; ChassisAssemble("trigger_subframe", Loose("sub frame(Clone)"), "sub frame"); }
            if (!host && t > 70f && chassisStep == 1)
            {
                chassisStep = 2;
                GameObject bolts = Game.FindAny(ChassisCar + "sub frame(xxxxx)/Bolts");
                int n = 0;
                if (bolts != null)
                    foreach (PlayMakerFSM f in bolts.GetComponentsInChildren<PlayMakerFSM>(true))
                    {
                        if (f.FsmName != "Screw" || f.FsmVariables.FindFsmInt("Stage") == null) continue;
                        for (int k = 0; k < 10 && f.FsmVariables.GetFsmInt("Stage").Value < 8; k++) f.SendEvent("TIGHTEN");
                        n++;
                    }
                Log.Info("autotest : mscchassis, " + n + " vis du sub frame serrees, Subframe " + DbState("Database/DatabaseBody/Subframe"));
            }
            if (!host && t > 85f && chassisStep == 2) { chassisStep = 3; ChassisAssemble("trigger_steering_axle", Loose("steering rack(Clone)"), "cremaillere"); }
            if (!host && t > 100f && chassisStep == 3)
            {
                chassisStep = 4;
                GameObject rack = Game.FindAny(ChassisCar + "steering rack(xxxxx)");
                PlayMakerFSM rem = rack != null ? Game.FsmOn(rack, "Removal") : null;
                // (comme le clic droit : 'Mouse over' -REMOVE-> ; depuis 'Mouse off' ce serait une casse, envoyee par l'autorite seule)
                if (rem != null) { rem.enabled = true; Game.SetState(rem, "Mouse over"); rem.SendEvent("REMOVE"); FsmGameObject pv = rem.FsmVariables.FindFsmGameObject("Part"); chassisNewRack = pv != null ? pv.Value : null; }
                Log.Info("autotest : mscchassis, cremaillere demontee -> " + (rem != null ? rem.ActiveStateName : "pas de Removal") + ", nouvelle piece libre " + (chassisNewRack != null ? chassisNewRack.name : "aucune"));
            }
            if (!host && t > 115f && chassisStep == 4) { chassisStep = 5; ChassisAssemble("trigger_steering_axle", chassisNewRack, "cremaillere remontee"); }
            if (t > 55f && t < 140f && Time.realtimeSinceStartup >= chassisLog)
            {
                chassisLog = Time.realtimeSinceStartup + 5f;
                GameObject ts = Game.FindAny(ChassisCar + "trigger_subframe"), tr = Game.FindAny(ChassisCar + "trigger_steering_axle");
                GameObject ms = Game.FindAny(ChassisCar + "sub frame(xxxxx)"), mr = Game.FindAny(ChassisCar + "steering rack(xxxxx)");
                Log.Info("autotest : mscchassis (" + (host ? "hote" : "invite") + ") sub frame : declencheur " + (ts != null && ts.activeSelf ? "allume" : "eteint")
                         + ", montee " + (ms != null && ms.activeSelf ? "allumee" : "eteinte") + ", " + DbState("Database/DatabaseBody/Subframe") + ", libre " + (Loose("sub frame(Clone)") != null ? "presente" : "absente")
                         + " | cremaillere : declencheur " + (tr != null && tr.activeSelf ? "allume" : "eteint") + ", montee " + (mr != null && mr.activeSelf ? "allumee" : "eteinte")
                         + ", " + DbState("Database/DatabaseMechanics/SteeringRack") + ", libre " + (Loose("steering rack(Clone)") != null ? "presente" : "absente"));
            }
        }

        // [Test] Autotest=mscregles (19/10, regles de MWC sur MSC) -- l'invite : 60 s decroche un appel de fosse (Shit 1) ; 62 s va au
        // magasin de Teimo ; 75 s y achete 2 saucisses et une caisse de biere (Total 30, il paie) ; 90 s sort un article du sac neuf,
        // 100 s le vide ; 110 s fait 10 L d'essence dans la SATSUMA (pompe 98 : Fuel +10, reservoir +10) et 120 s paie l'essence a la
        // caisse. Chacun note toutes les 10 s : appel, argent, sacs, reservoir, compteur de la pompe.
        static int rgStep; static float rgLog; static System.Collections.Generic.HashSet<string> rgBags; static GameObject rgBag;
        const string RgReg = "STORE/StoreCashRegister/Register", RgPump = "STORE/LOD/Pumps/GasolinePump98/Functions/Trigger98", RgTank = "Database/DatabaseMechanics/FuelTank";
        static System.Collections.Generic.IEnumerable<GameObject> Bags()
        {
            foreach (PlayMakerFSM f in Object.FindObjectsOfType<PlayMakerFSM>())
                if (f.FsmName == "Use" && f.gameObject.name.StartsWith("shopping bag") && f.Fsm.GetState("Spawn one") != null) yield return f.gameObject;
        }
        static int Beers() { int n = 0; foreach (Transform x in Object.FindObjectsOfType<Transform>()) if (x.name.StartsWith("beer case") || x.name.StartsWith("beercase")) n++; return n; }
        static string BagId(GameObject b) { PlayMakerFSM u = Game.FsmOn(b, "Use"); FsmString id = u != null ? u.FsmVariables.FindFsmString("ID") : null; return id != null ? id.Value : "?"; }
        static void MscRegles(float t)
        {
            bool host = Net.Session.IsHost;
            if (t > 50f && rgBags == null) { rgBags = new System.Collections.Generic.HashSet<string>(); foreach (GameObject b in Bags()) rgBags.Add(BagId(b)); Log.Info("autotest : mscregles, " + rgBags.Count + " sacs avant"); }
            if (!host)
            {
                if (t > 60f && rgStep == 0) { rgStep = 1; Log.Info("autotest : mscregles, " + PhoneMsc.TestAnswer("SHIT1")); }
                if (t > 62f && rgStep == 1)
                {
                    rgStep = 2;
                    GameObject reg = Game.FindAny(RgReg), pl = Game.PlayerGo;
                    var cc = pl != null ? pl.GetComponent<CharacterController>() : null;
                    if (reg != null && cc != null) { cc.enabled = false; pl.transform.position = reg.transform.position + reg.transform.forward * -1.2f + Vector3.up * 0.3f; cc.enabled = true; }
                    Log.Info("autotest : mscregles, au magasin de Teimo");
                }
                if (t > 75f && rgStep == 2)
                {
                    rgStep = 3;
                    PlayMakerFSM reg = Game.FindFsm(RgReg, "Data");
                    if (reg != null)
                    {
                        reg.FsmVariables.GetFsmInt("QSausages").Value = 2;
                        FsmInt qb = reg.FsmVariables.FindFsmInt("QBeer"); if (qb != null) qb.Value = 1;
                        reg.FsmVariables.GetFsmFloat("Total").Value = 30f;
                        Game.SetState(reg, "Purchase");
                        Log.Info("autotest : mscregles, achat chez Teimo -> " + reg.ActiveStateName + ", " + Wallet.State());
                    }
                }
                if (t > 90f && rgStep == 3 && rgBag != null)
                {
                    rgStep = 4;
                    PlayMakerFSM u = Game.FsmOn(rgBag, "Use");
                    if (u.Fsm.GetState("Confirm") != null) Game.SetState(u, "Confirm");
                    Game.SetState(u, "Spawn one");
                    Log.Info("autotest : mscregles, un article sorti du sac " + BagId(rgBag));
                }
                if (t > 100f && rgStep == 4 && rgBag != null)
                {
                    rgStep = 5;
                    PlayMakerFSM u = Game.FsmOn(rgBag, "Use");
                    if (u.Fsm.GetState("Confirm") != null) Game.SetState(u, "Confirm");
                    Game.SetState(u, "Spawn all");
                    Log.Info("autotest : mscregles, sac " + BagId(rgBag) + " vide");
                }
                if (t > 110f && rgStep >= 3 && rgStep < 6)
                {
                    rgStep = 6;
                    PlayMakerFSM pump = Game.FindFsm(RgPump, "Use"), tank = Game.FindFsm(RgTank, "Data");
                    if (pump != null) pump.FsmVariables.GetFsmFloat("Fuel").Value += 10f;
                    if (tank != null) tank.FsmVariables.GetFsmFloat("FuelLevel").Value += 10f;
                    Log.Info("autotest : mscregles, plein de 10 L ici");
                }
                if (t > 120f && rgStep == 6)
                {
                    rgStep = 7;
                    PlayMakerFSM reg = Game.FindFsm(RgReg, "Data"), pump = Game.FindFsm(RgPump, "Use");
                    float price = pump != null && pump.FsmVariables.FindFsmFloat("FuelPrice") != null ? pump.FsmVariables.GetFsmFloat("FuelPrice").Value : 4f;
                    if (reg != null)
                    {
                        reg.FsmVariables.GetFsmFloat("FuelPurchase").Value = 10f * price;
                        reg.FsmVariables.GetFsmFloat("Total").Value = 10f * price;
                        Game.SetState(reg, "Purchase");
                        Log.Info("autotest : mscregles, essence payee (" + (10f * price).ToString("F2") + " mk) -> " + Wallet.State());
                    }
                }
            }
            if (rgBags != null && rgBag == null && t > 76f)
                foreach (GameObject b in Bags()) if (!rgBags.Contains(BagId(b))) { rgBag = b; Log.Info("autotest : mscregles, sac neuf " + BagId(b) + " en " + b.transform.position.ToString("F1") + (b.activeInHierarchy ? "" : " (inactif)")); break; }
            if (t > 55f && t < 220f && Time.realtimeSinceStartup >= rgLog)
            {
                rgLog = Time.realtimeSinceStartup + 10f;
                PlayMakerFSM pump = Game.FindFsm(RgPump, "Use"), tank = Game.FindFsm(RgTank, "Data");
                int nb = 0; foreach (GameObject b in Bags()) nb++;
                Log.Info("autotest : mscregles (" + (host ? "hote" : "invite") + ") " + PhoneMsc.State(1) + " | " + Wallet.State() + " | sacs " + nb + (rgBag != null ? ", sac neuf " + BagId(rgBag) : "")
                         + " | reservoir " + (tank != null ? tank.FsmVariables.GetFsmFloat("FuelLevel").Value.ToString("F1") : "?") + ", pompe 98 " + (pump != null ? pump.FsmVariables.GetFsmFloat("Fuel").Value.ToString("F1") : "?")
                         + " | caisses de biere " + Beers() + " | " + Fluids.TestState("FuelTank"));
            }
        }

        // [Test] Autotest=mscreste (19/10) -- l'invite : 60 s decroche la fosse 2 (objectif de carte a lui) ; 65 s sert la boite aux lettres
        // 3 (tracts) ; 70 s va a la station de Teimo, 75 s prend le pistolet 98, 85 s le repose ; 90 s va chez Fleetari, 100 s y achete
        // un alternateur ; 110 s commande des travaux et rentre ; 115 s commande par la poste. Chacun note toutes les 10 s.
        static int rsStep; static float rsLog;
        static void Teleport(string path, Vector3 off)
        {
            GameObject g = Game.FindAny(path), pl = Game.PlayerGo;
            var cc = pl != null ? pl.GetComponent<CharacterController>() : null;
            if (g == null || cc == null) return;
            cc.enabled = false; pl.transform.position = g.transform.position + off; cc.enabled = true;
        }
        static void MscReste(float t)
        {
            bool host = Net.Session.IsHost;
            if (!host)
            {
                if (t > 60f && rsStep == 0) { rsStep = 1; Log.Info("autotest : mscreste, " + PhoneMsc.TestAnswer("SHIT2")); }
                if (t > 65f && rsStep == 1) { rsStep = 2; Log.Info("autotest : mscreste, " + TractsMsc.TestDeliver(3)); }
                if (t > 70f && rsStep == 2) { rsStep = 3; Teleport("STORE/LOD/Pumps/GasolinePump98", new Vector3(1.5f, 0.5f, 0f)); Log.Info("autotest : mscreste, a la station"); }
                if (t > 75f && rsStep == 3)
                {
                    rsStep = 4;
                    PlayMakerFSM u = Game.FindFsm("STORE/LOD/Pumps/GasolinePump98/Functions/Trigger98", "Use");
                    if (u != null) Game.SetState(u, "Activate hand");
                    Log.Info("autotest : mscreste, pistolet 98 pris -> " + (u != null ? u.ActiveStateName : "?"));
                }
                if (t > 85f && rsStep == 4)
                {
                    rsStep = 5;
                    PlayMakerFSM u = Game.FindFsm("STORE/LOD/Pumps/GasolinePump98/Functions/Trigger98", "Use");
                    if (u != null) Game.SetState(u, "Anim drop");
                    Log.Info("autotest : mscreste, pistolet 98 repose -> " + (u != null ? u.ActiveStateName : "?"));
                }
                if (t > 90f && rsStep == 5) { rsStep = 6; Teleport("REPAIRSHOP/LOD/Store/ShopCashRegister", new Vector3(0f, 0.5f, -1.2f)); Log.Info("autotest : mscreste, chez Fleetari"); }
                if (t > 100f && rsStep == 6)
                {
                    rsStep = 7;
                    PlayMakerFSM reg = Game.FindFsm("REPAIRSHOP/LOD/Store/ShopCashRegister/Register", "Data");
                    if (reg != null)
                    {
                        reg.FsmVariables.GetFsmBool("QAlternator").Value = true;
                        reg.FsmVariables.GetFsmFloat("Total").Value = 425f;
                        Game.SetState(reg, "Purchase");
                    }
                    Log.Info("autotest : mscreste, alternateur achete -> " + (reg != null ? reg.ActiveStateName : "caisse introuvable") + ", " + Wallet.State());
                }
                if (t > 110f && rsStep == 7) { rsStep = 8; Log.Info("autotest : mscreste, " + FleetariMsc.TestOrder()); Teleport("YARD/Building", new Vector3(0f, 1f, 0f)); }
                if (t > 115f && rsStep == 8) { rsStep = 9; Log.Info("autotest : mscreste, " + FleetariMsc.TestPost()); }
            }
            if (t > 55f && t < 240f && Time.realtimeSinceStartup >= rsLog)
            {
                rsLog = Time.realtimeSinceStartup + 10f;
                Log.Info("autotest : mscreste (" + (host ? "hote" : "invite") + ") " + PhoneMsc.State(2) + " | " + TractsMsc.State() + " |" + PumpsMsc.State() + " | " + FleetariMsc.State()
                         + ", colis restants " + FleetariMsc.BoxesLeft() + " | " + Wallet.State() + " | objectifs " + MapPanel.Objectives.Count);
            }
        }

        // [Test] Autotest=portemagasin (MSC, 2 joueurs, Markos 24/10) : les deux vont a 10 s devant la porte du magasin ; a 20 s
        // horaires figes, magasin FERME chez l'hote et ouvert chez l'invite (STORE/OpeningHours.OpenStore) ; l'invite ouvre la
        // porte a 50 s et la referme a 62 s. Attendu : la porte suit l'invite chez l'hote (avant : fermee a cle chez lui).
        // [Test] Autotest=mscmontage (MSC, JD et Kristof 24/10 : pieces posees qui retombent) : le joueur [Test] Monteur (0 = hote)
        // fait les montages de [Test] Montages ("point|piece;point|piece", par defaut tableau de bord, compteurs, aile gauche) a 40 s
        // puis toutes les 10 s, comme un clic ; chacun note 5 s apres si la piece tient (les ruptures : « attache rompue »).
        static int mmStep, mmPush;
        static void MscMontage(float t)
        {
            string[] list = Config.Get("Test", "Montages", "SATSUMA(557kg, 248)/Dashboard/trigger_dashboard|db:Database/DatabaseMechanics/Dashboard;"
                + "db:Database/DatabaseMechanics/Dashboard/trigger_meters|db:Database/DatabaseMechanics/DashboardMeters;SATSUMA(557kg, 248)/Body/trigger_fender_left|db:Database/DatabaseBody/Fender_Left").Split(';');
            // ([Test] PousseA=60 : l'invite pousse la voiture 5 s a partir de cet instant, comme Autotest=pousse -- la copie chez l'hote)
            float pa = Config.GetInt("Test", "PousseA", 0);
            if (pa > 0f && !Net.Session.IsHost)
            {
                Rigidbody b = VehicleSync.Body("SATSUMA(557kg, 248)");
                if (b != null && t > pa - 2f && mmPush == 0)
                {
                    mmPush = 1;
                    GameObject pl = Game.PlayerGo;
                    var cc = pl != null ? pl.GetComponent<CharacterController>() : null;
                    if (cc != null) { cc.enabled = false; pl.transform.position = b.transform.TransformPoint(new Vector3(0f, 0f, -3.2f)); cc.enabled = true; }
                    Log.Info("autotest montage : derriere la voiture pour la pousser");
                }
                Gestures.TestPush = t > pa && t < pa + 5f;
                if (b != null && t > pa && t < pa + 5f && !b.isKinematic) { Vector3 f = b.transform.forward; f.y = 0; b.velocity = f.normalized * 1.2f + Vector3.up * Mathf.Min(b.velocity.y, 0f); }
            }
            if (mmStep >= list.Length * 2) return;
            int i = mmStep / 2;
            string[] pp = list[i].Split('|');
            if (pp.Length < 2) { mmStep += 2; return; }
            float at = 40f + 10f * i;
            if (mmStep % 2 == 0 && t > at)
            {
                mmStep++;
                if (Net.Session.LocalId == Config.GetInt("Test", "Monteur", 0)) Log.Info("autotest montage : " + Parts.MscTestClick(pp[0].Trim(), pp[1].Trim()));
            }
            else if (mmStep % 2 == 1 && t > at + 5f) { mmStep++; Log.Info("autotest montage : " + pp[0].Trim() + " " + Parts.MscTestHolds(pp[0].Trim())); }
        }

        static int pmStep; static float pmLog;
        static void PorteMagasin(float t)
        {
            bool host = Net.Session.IsHost;
            if (t > 10f && pmStep == 0) { pmStep = 1; Teleport("STORE/LOD/DoorStore", new Vector3(2f, 0.5f, 2f)); Log.Info("autotest : portemagasin, devant le magasin"); }
            if (t > 20f && pmStep == 1)
            {
                pmStep = 2;
                PlayMakerFSM oh = Game.FindFsm("STORE", "OpeningHours");
                if (oh != null) { oh.enabled = false; oh.FsmVariables.GetFsmBool("OpenStore").Value = !host; }
                Log.Info("autotest : portemagasin, magasin " + (host ? "ferme" : "ouvert") + " ici" + (oh == null ? " (horaires introuvables)" : ""));
            }
            if (!host && t > 50f && pmStep == 2) { pmStep = 3; Log.Info("autotest : portemagasin, " + Interactions.TestNamed("STORE/LOD/DoorStore")); }
            if (!host && t > 62f && pmStep == 3) { pmStep = 4; Log.Info("autotest : portemagasin, " + Interactions.TestNamed("STORE/LOD/DoorStore")); }
            if (t > 45f && t < 80f && Time.realtimeSinceStartup >= pmLog)
            {
                pmLog = Time.realtimeSinceStartup + 4f;
                PlayMakerFSM door = Game.FindFsm("STORE/LOD/DoorStore/Pivot/Handle", "Use");
                FsmBool open = door != null ? door.FsmVariables.FindFsmBool("DoorOpen") : null;
                GameObject piv = Game.FindAny("STORE/LOD/DoorStore/Pivot");
                Log.Info("autotest : portemagasin (" + (host ? "hote" : "invite") + ") porte " + (open != null ? (open.Value ? "ouverte" : "fermee") : "?") + " [" + (door != null ? door.ActiveStateName : "?") + "]"
                         + (piv != null ? " " + piv.transform.localEulerAngles.ToString("F0") : ""));
            }
        }

        // [Test] Autotest=mscporte (19/10, retour de Vaydon : portes de la maison et du garage) -- l'hote : 60 s ouvre la porte
        // d'entree, 66 s allume la cuisine, 72 s pousse la porte gauche du garage (2 s), 85 s referme la porte d'entree. Chacun note
        // toutes les 5 s : porte d'entree (DoorOpen), portes du garage (angle).
        static int pdStep; static float pdLog, pdRelease;
        static void MscPorte(float t)
        {
            bool host = Net.Session.IsHost;
            if (host)
            {
                if (t > 60f && pdStep == 0) { pdStep = 1; Log.Info("autotest : mscporte, " + Interactions.TestNamed("LIVINGROOM/DoorFront")); }
                if (t > 66f && pdStep == 1) { pdStep = 2; Log.Info("autotest : mscporte, " + Interactions.TestNamed("switch_kitchen")); }
                if (t > 72f && pdStep == 2) { pdStep = 3; pdRelease = t + 2f; Log.Info("autotest : mscporte, " + PushDoors.TestPush("DoorLeft", "Open")); }
                if (pdStep == 3 && t > pdRelease) { pdStep = 4; Log.Info("autotest : mscporte, relache " + PushDoors.TestPush("DoorLeft", "Mouse off")); }
                if (t > 85f && pdStep == 4) { pdStep = 5; Log.Info("autotest : mscporte, " + Interactions.TestNamed("LIVINGROOM/DoorFront")); }
            }
            if (t > 55f && t < 120f && Time.realtimeSinceStartup >= pdLog)
            {
                pdLog = Time.realtimeSinceStartup + 5f;
                PlayMakerFSM door = Game.FindFsm("YARD/Building/LIVINGROOM/DoorFront/Pivot/Handle", "Use");
                FsmBool open = door != null ? door.FsmVariables.FindFsmBool("DoorOpen") : null;
                GameObject piv = Game.FindAny("YARD/Building/LIVINGROOM/DoorFront/Pivot");
                Log.Info("autotest : mscporte (" + (host ? "hote" : "invite") + ") porte d'entree " + (open != null ? (open.Value ? "ouverte" : "fermee") : "?")
                         + (piv != null ? " " + piv.transform.localEulerAngles.y.ToString("F0") + " deg" : "") + " | " + PushDoors.State());
            }
        }

        // [Test] Autotest=mscguepe (19/10, JD : mort par les guepes pres d'une voiture, ecran noir et « ENTER DRIVING MODE » apres la
        // reapparition) -- avec [Test] TestReapparition=2 : 25 s a cote de la portiere de [Test] TestVoiture (FERNDALE), 28 s piqures
        // (allergie 110, STING -> mort ~10 s apres), reapparition automatique ; etat de l'allergie, de la vignette et de l'invite de
        // conduite a 26, 36 et 55 s, captures.
        static int gpStep;
        static string GuepeState()
        {
            GameObject cam = Game.PlayerPart("Pivot/AnimPivot/Camera/FPSCamera/FPSCamera");
            PlayMakerFSM bl = cam != null ? Game.FsmOn(cam, "Blindness") : null;
            string vig = "?";
            if (cam != null) foreach (Behaviour b in cam.GetComponents<Behaviour>()) if (b != null && b.GetType().Name.StartsWith("VignetteAndChromatic")) vig = b.enabled ? "allumee" : "eteinte";
            int prompts = 0;
            foreach (PlayMakerFSM f in Object.FindObjectsOfType<PlayMakerFSM>()) if (f.FsmName == "PlayerTrigger" && f.gameObject.name.StartsWith("DriveTrigger") && f.ActiveStateName == "Press return") prompts++;
            return "Blindness [" + (bl != null ? bl.ActiveStateName : "?") + "] allergie " + (bl != null ? bl.FsmVariables.GetFsmFloat("MaxAllergy").Value.ToString("F0") : "?") + ", vignette " + vig
                   + ", GUIdrive " + Game.GlobalBool("GUIdrive") + ", zones 'Press return' " + prompts + ", joueur en " + (Game.PlayerT != null ? Game.PlayerT.position.ToString("F1") : "?");
        }
        static void MscGuepe(float t)
        {
            string car = Config.Get("Test", "TestVoiture", "FERNDALE");
            if (t > 25f && gpStep == 0) { gpStep = 1; Log.Info("autotest : mscguepe, " + VehicleSync.TestEnter(car, false)); }
            if (t > 26.5f && gpStep == 1) { gpStep = 2; Log.Info("autotest : mscguepe, avant : " + GuepeState()); }
            if (t > 28f && gpStep == 2)
            {
                gpStep = 3;
                GameObject cam = Game.PlayerPart("Pivot/AnimPivot/Camera/FPSCamera/FPSCamera");
                PlayMakerFSM bl = cam != null ? Game.FsmOn(cam, "Blindness") : null;
                if (bl != null) { bl.FsmVariables.GetFsmFloat("MaxAllergy").Value = 110f; bl.SendEvent("STING"); }
                Log.Info("autotest : mscguepe, piqures -> " + (bl != null ? bl.ActiveStateName : "pas de Blindness"));
            }
            if (t > 36f && gpStep == 3) { gpStep = 4; Log.Info("autotest : mscguepe, pendant : " + GuepeState()); CaptureSoon("msc-guepe-pendant", 0.2f); }
            if (t > 55f && gpStep == 4) { gpStep = 5; Log.Info("autotest : mscguepe, apres : " + GuepeState()); CaptureSoon("msc-guepe-apres", 0.2f); }
        }

        // Camera d'essai et regard (CameraAvatar, CameraVoiture, Regarder, RegarderPos) : MWC et MSC.
        static void CameraKnobs(float t)
        {
            // Captures : Regarder=1, la camera vise l'avatar le plus proche ; Regarder=2, le milieu du groupe d'avatars
            // (photos a 3-4 joueurs) ; RegarderPos=x,y,z (sans Regarder) : le joueur se tourne vers ce point (pose).
            // [Test] CameraAvatar=dx,dy,dz : une camera d'essai (par-dessus celle du joueur) a ce decalage (m, repere du monde)
            // de l'avatar distant le plus proche, qui le vise : captures d'un avatar ou qu'il soit (lit, voiture...).
            string ca = Config.Get("Test", "CameraAvatar", "");
            if (ca.Length > 0)
            {
                string[] c = ca.Split(',');
                var ci = System.Globalization.CultureInfo.InvariantCulture;
                float dx = 0, dy = 0, dz = 0;
                if (c.Length >= 3) { float.TryParse(c[0], System.Globalization.NumberStyles.Float, ci, out dx); float.TryParse(c[1], System.Globalization.NumberStyles.Float, ci, out dy); float.TryParse(c[2], System.Globalization.NumberStyles.Float, ci, out dz); }
                Avatar near = null;
                foreach (Avatar a in PlayerSync.Avatars) if (a.Root != null) { near = a; break; }
                if (near != null)
                {
                    if (testCam == null)
                    {
                        testCam = new GameObject("MWCoop-CameraEssai").AddComponent<Camera>();
                        testCam.depth = 100;
                        testCam.nearClipPlane = 0.05f;
                        testCam.fieldOfView = 60;
                        // ([Test] CameraLumiere=1 : une lampe sur la camera d'essai -- captures de nuit, casque du 20/10)
                        if (Config.GetInt("Test", "CameraLumiere", 0) != 0) { Light l = testCam.gameObject.AddComponent<Light>(); l.type = LightType.Point; l.range = 8f; l.intensity = 2.5f; }
                    }
                    float hh = float.Parse(Config.Get("Test", "CameraHauteur", "0.3"), System.Globalization.CultureInfo.InvariantCulture);
                    bool local = Config.GetInt("Test", "CameraLocale", 0) != 0;   // (decalage dans le repere de l'avatar : en voiture, celui de la voiture)
                    Transform ar = near.Root.transform;
                    Vector3 target = local ? ar.TransformPoint(new Vector3(0f, hh, 0f)) : ar.position + Vector3.up * hh;
                    testCam.transform.position = local ? ar.TransformPoint(new Vector3(dx, dy, dz)) : ar.position + new Vector3(dx, dy, dz);
                    testCam.transform.LookAt(target);
                }
            }
            // [Test] CameraMonde=x,y,z,vx,vy,vz : camera d'essai fixe dans le monde, tournee vers (vx,vy,vz) -- captures d'un lieu (lit...).
            string cm = Config.Get("Test", "CameraMonde", "");
            if (cm.Length > 0)
            {
                string[] c = cm.Split(',');
                if (c.Length >= 6)
                {
                    var ci = System.Globalization.CultureInfo.InvariantCulture;
                    System.Func<int, float> F = i => float.Parse(c[i], ci);
                    if (testCam == null) { testCam = new GameObject("MWCoop-CameraEssai").AddComponent<Camera>(); testCam.depth = 100; testCam.nearClipPlane = 0.03f; testCam.fieldOfView = 70; }
                    testCam.transform.position = new Vector3(F(0), F(1), F(2));
                    testCam.transform.LookAt(new Vector3(F(3), F(4), F(5)), Vector3.forward);
                }
            }
            // [Test] CameraVoiture=x,y,z,vx,vy,vz : camera d'essai dans le repere de [Test] TestVoiture, tournee vers (vx,vy,vz).
            string cv = Config.Get("Test", "CameraVoiture", "");
            if (cv.Length > 0)
            {
                string[] c = cv.Split(',');
                Rigidbody vb = VehicleSync.Body(Config.Get("Test", "TestVoiture", "SORBET"));
                if (c.Length >= 6 && vb != null)
                {
                    var ci = System.Globalization.CultureInfo.InvariantCulture;
                    System.Func<int, float> F = i => float.Parse(c[i], ci);
                    if (testCam == null) { testCam = new GameObject("MWCoop-CameraEssai").AddComponent<Camera>(); testCam.depth = 100; testCam.nearClipPlane = 0.03f; testCam.fieldOfView = 70; }
                    testCam.transform.position = vb.transform.TransformPoint(new Vector3(F(0), F(1), F(2)));
                    testCam.transform.LookAt(vb.transform.TransformPoint(new Vector3(F(3), F(4), F(5))), vb.transform.up);
                }
            }
            int look = Config.GetInt("Test", "Regarder", 0);
            if (look == 2) LookAtGroup();
            else if (look != 0) LookAtNearestAvatar();
            else if (Config.Get("Test", "RegarderPos", "").Length > 0 && t > 9f) FacePoint(Config.Get("Test", "RegarderPos", ""));
        }

        // [Test] Autotest=conduite : MWC et MSC.
        static void Conduite(float t)
        {
                // Au volant de [Test] TestVoiture a 15 s, puis le vehicule est pousse en avant (8 m/s) 12 s.
                string car = Config.Get("Test", "TestVoiture", "KEKMET(350-400psi)");
                float enterAt = Config.GetFloat("Test", "EntreeA", 15f);
                if (t > enterAt && step == 0) { step = 1; Log.Info("autotest : " + VehicleSync.TestEnter(car, false)); }
                if (t > enterAt + 7f && step == 1) { step = 2; Log.Info("autotest : volant -> " + VehicleSync.TestEnter(car, true)); }
                Rigidbody b = VehicleSync.Body(car);
                float push0 = Config.GetFloat("Test", "PousseDebut", 25f), push1 = Config.GetFloat("Test", "PousseFin", 37f);
                VehicleSync.TestEngine(t > push0 && t < push1 ? 2000f : -1f, 0.6f);
                if (t > 24f && step == 2) { step = 3; Log.Info("autotest : moteur " + VehicleSync.TestSounds(car, true)); }
                if (t > 23f && Time.frameCount % 120 == 0 && t < 30f) Log.Info("autotest : yeux " + Seats.DriverEyes(car.Split('(')[0]));
                if (t > 55f && step == 3 && Config.GetInt("Test", "TestSortie", 0) != 0) { step = 4; Log.Info("autotest : sortie -> " + VehicleSync.TestExit(car)); }
                if (t > 56f && step == 4) { step = 5; Log.Info("autotest : apres sortie -> " + VehicleSync.TestExit(car)); }
                if (b != null && t > push0 && t < push1) { Vector3 f = b.transform.forward; f.y = 0; float turn = Config.GetFloat("Test", "PousseVirage", 0f); if (turn != 0f) b.angularVelocity = new Vector3(b.angularVelocity.x, turn, b.angularVelocity.z); b.velocity = f.normalized * Config.GetFloat("Test", "PousseVitesse", 8f) + Vector3.up * Mathf.Min(b.velocity.y, 0f); }
                if (b != null && t > 20f && Time.frameCount % 150 == 0) Log.Info("autotest : " + car + " en " + b.position.ToString("F1") + " rot " + b.rotation.eulerAngles.ToString("F0") + ", cinematique " + b.isKinematic);
                if (b != null && t > 20f && !b.isKinematic)
                {
                    // Envol ? vitesse verticale et rotation maximales de la voiture conduite.
                    if (b.angularVelocity.magnitude > maxRot) { maxRot = b.angularVelocity.magnitude; if (maxRot > 1f) Log.Info("autotest : rotation max " + maxRot.ToString("F2") + " rad/s"); }
                    if (b.velocity.y > maxUp) { maxUp = b.velocity.y; Log.Info("autotest : vitesse verticale max " + maxUp.ToString("F2") + " m/s, rotation " + b.angularVelocity.magnitude.ToString("F2") + " rad/s en " + b.position.ToString("F1")); }
                }
        }

        // [Test] Autotest=gifulevier (MWC, 2 joueurs) : main de l'avatar au levier de vitesses de la GIFU (Menor, 21/10). L'invite
        // prend le volant de [Test] TestVoiture (GIFU) a 15/22 s et passe 1-2-3-2 de 30 a 70 s (champ gear du
        // Drivetrain, comme TestGear ; [Test] LevierPas=s, 3 par defaut ; [Test] LevierForce=deg : tourne aussi le levier lui-meme de +-deg, si le jeu ne le bouge
        // pas) et note l'angle du levier. L'hote va a 4 m de la GIFU a 8 s (tableau de bord actif), note les mains de l'avatar
        // chaque seconde de 20 a 100 s et capture (CameraAvatar...) ecran-gifu-volant (assis, main au volant) puis
        // ecran-gifu-levier (premiere fois main au levier > 0,9).
        static readonly System.Collections.Generic.Dictionary<Transform, Quaternion> leverRest = new System.Collections.Generic.Dictionary<Transform, Quaternion>();
        static float glNext, glLog, glTryAt, glSeatedAt = -1f; static int glShifts, glTries; static bool glWheelCap, glLeverCap, glRepose, glDumped, glSeat;
        static string LeverAngle(Rigidbody b)
        {
            string s = "";
            foreach (Transform x in b.GetComponentsInChildren<Transform>(true))
            {
                if (x.parent == null || !(x.name.ToLowerInvariant().Contains("gear") || x.parent.name.ToLowerInvariant().Contains("gear") || (x.parent.parent != null && x.parent.parent.name.ToLowerInvariant().Contains("gear")))) continue;
                if (!leverRest.ContainsKey(x)) leverRest[x] = x.localRotation;
                float a = Quaternion.Angle(leverRest[x], x.localRotation);
                if (a > 0.5f) s += " " + x.parent.name + "/" + x.name + " " + a.ToString("F0") + " deg";
            }
            return s.Length > 0 ? "levier" + s : "levier immobile";
        }
        static void GifuLevier(float t)
        {
            string car = Config.Get("Test", "TestVoiture", "GIFU(750/450psi)");
            Rigidbody b = VehicleSync.Body(car);
            if (!MWCoop.Net.Session.IsHost)
            {
                if (t > 15f && step == 0) { step = 1; Log.Info("autotest : " + VehicleSync.TestEnter(car, false)); }
                // (vehicule loin : son declencheur s'allume apres la 1re pose -- reposee a 19 s pour y entrer, comme cabine)
                if (t > 19f && step == 1 && !glRepose) { glRepose = true; Log.Info("autotest : " + VehicleSync.TestEnter(car, false)); }
                if (t > 24f && step == 1) { step = 2; Log.Info("autotest : volant -> " + VehicleSync.TestEnter(car, true)); if (b != null) LeverAngle(b); }
                // (pas au volant -- declencheur pas encore pret selon le chargement : reposee puis volant, toutes les 4 s, 4 fois ;
                // ensuite l'automate d'entree au journal, pour voir ce qu'il attend)
                bool driving = b != null && ReferenceEquals(VehicleSync.LocalDrivingRoot, b.transform);
                if (step == 2 && !driving && t > 26f && glTries < 4 && t >= glTryAt) { glTryAt = t + 4f; glTries++; glSeat = true; Log.Info("autotest : gifulevier, essai " + glTries + " : " + VehicleSync.TestEnter(car, false)); }
                if (step == 2 && !driving && glSeat && t >= glTryAt - 2f) { glSeat = false; Log.Info("autotest : volant -> " + VehicleSync.TestEnter(car, true)); }
                if (step == 2 && !driving && glTries >= 4 && t >= glTryAt && !glDumped && b != null)
                {
                    glDumped = true;
                    foreach (PlayMakerFSM f in b.GetComponentsInChildren<PlayMakerFSM>(true))
                        if (f.FsmName == "PlayerTrigger" && f.gameObject.name.StartsWith("DriveTrigger")) DumpFsms(car + "/" + VehicleSync.RelPath(b.transform, f.transform) + ":PlayerTrigger");
                }
                if (step == 2 && t > 30f && t < 70f && Time.realtimeSinceStartup >= glNext)
                {
                    glNext = Time.realtimeSinceStartup + Config.GetFloat("Test", "LevierPas", 3f);   // (3 s : la main revient au volant entre deux passages)
                    int g = new[] { 1, 2, 3, 2 }[glShifts % 4];
                    string r = VehicleSync.TestGear(car, g);
                    float force = Config.GetFloat("Test", "LevierForce", 0f);
                    if (force != 0f && b != null)
                        foreach (Transform x in b.GetComponentsInChildren<Transform>(true))
                            if (x.name == "Lever" && x.parent != null && x.parent.name == "Pivot")
                            {
                                if (!leverRest.ContainsKey(x)) leverRest[x] = x.localRotation;
                                x.localRotation = leverRest[x] * Quaternion.Euler(glShifts % 2 == 0 ? force : -force, 0f, 0f);
                            }
                    if (glShifts++ < 8 && b != null) Log.Info("autotest : gifulevier, invite : " + r + ", " + LeverAngle(b) + ", au volant " + (ReferenceEquals(VehicleSync.LocalDrivingRoot, b.transform) ? "oui" : "non"));
                }
                return;
            }
            if (t > 8f && !teleported && b != null)
            {
                teleported = true;
                var cc = Game.PlayerGo != null ? Game.PlayerGo.GetComponent<CharacterController>() : null;
                if (cc != null) { cc.enabled = false; Game.PlayerGo.transform.position = b.position + b.transform.right * -4f + Vector3.up; cc.enabled = true; Log.Info("autotest : gifulevier, hote pres de la GIFU"); }
            }
            if (t < 20f || t > 100f) return;
            foreach (Avatar a in PlayerSync.Avatars)
            {
                if (a.Root == null) continue;
                bool seated = VehicleSync.RemoteCarTransform(a.Player.Id) != null;
                if (seated && glSeatedAt < 0f) glSeatedAt = t;
                bool settled = seated && t > glSeatedAt + 3f;   // (camera et pose posees)
                if (Time.realtimeSinceStartup >= glLog || (a.ShiftWeight > 0.9f && !glLeverCap)) { glLog = Time.realtimeSinceStartup + 1f; Log.Info("autotest : gifulevier, hote : " + (seated ? "" : "pas au volant, ") + a.HandsState() + (seated && b != null ? " ; repere GIFU : " + a.CarFrame(b.transform) : "")); }
                if (settled && !glWheelCap && a.ShiftWeight < 0.01f) { glWheelCap = true; CaptureSoon("gifu-volant", 0.1f); }
                else if (settled && glWheelCap && !glLeverCap && a.ShiftWeight > 0.9f && capName == null) { glLeverCap = true; CaptureSoon("gifu-levier", 0.02f); }
            }
        }

        static bool tricheDone;   // ([Test] Triche=<commande du tchat> : une fois, a 30 s)
        static void CdMsc(float t)
        {
            // [Test] Autotest=cdmsc (MSC, 2 joueurs, l'hote agit) : le lecteur CD de la SATSUMA allume par la molette du volume (Knob :
            // UNTIGHTEN depuis « Get scroll »), puis l'etat des trois automates ButtonsCD releve chaque seconde pendant 25 s : il doit
            // rester On (29.30, 22/10 : il se coupait apres une seconde, l'invite renvoyant son « Off 2 »).
            if (MWCoop.Net.Session.IsHost && t > 40f && !done)
            {
                done = true;
                PlayMakerFSM knob = null;
                foreach (PlayMakerFSM f in Object.FindObjectsOfType<PlayMakerFSM>())
                    if (f.FsmName == "Knob" && f.gameObject.name == "RadioVolume" && Recon.Path(f.transform).Contains("/ButtonsCD/")) knob = f;
                if (knob == null) { Log.Warn("autotest cdmsc : bouton de volume du lecteur CD introuvable"); return; }
                Log.Info("autotest cdmsc : lecteur CD, etat " + knob.ActiveStateName + ", UNTIGHTEN envoye");
                knob.SendEvent("UNTIGHTEN");
                cdWatchUntil = Time.realtimeSinceStartup + 25f;
            }
            if (cdWatchUntil > 0f && Time.realtimeSinceStartup >= nextSleepLog)
            {
                nextSleepLog = Time.realtimeSinceStartup + 1f;
                string st = "";
                foreach (PlayMakerFSM f in Object.FindObjectsOfType<PlayMakerFSM>())
                    if (Recon.Path(f.transform).Contains("/ButtonsCD/")) st += " " + f.gameObject.name + "::" + f.FsmName + "=" + f.ActiveStateName;
                Log.Info("autotest cdmsc :" + st);
                if (Time.realtimeSinceStartup >= cdWatchUntil) cdWatchUntil = 0f;
            }
        }

        // Barque de My Summer Car (essais) : moteur lance / coupe par la variable Start de 'Sound Engine' (BOAT/GFX/Audio).
        static string BoatStart(bool on)
        {
            GameObject au = Game.FindAny("BOAT/GFX/Audio");
            PlayMakerFSM se = au != null ? Game.FsmOn(au, "Sound Engine") : null;
            FsmBool st = se != null ? se.FsmVariables.FindFsmBool("Start") : null;
            if (st != null) st.Value = on;
            return "Sound Engine [" + (se != null ? se.ActiveStateName : "?") + "] Start " + (st != null ? st.Value.ToString() : "?");
        }
        static string FsmVar(string path, string fsm, string v)
        {
            GameObject g = Game.FindAny(path);
            PlayMakerFSM f = g != null ? Game.FsmOn(g, fsm) : null;
            if (f == null) return v + " ?";
            FsmFloat x = f.FsmVariables.FindFsmFloat(v);
            return v + " " + (x != null ? x.Value.ToString("F2") : "?") + (f.enabled ? "" : " (automate coupe)") + (g.activeInHierarchy ? "" : " (objet eteint)");
        }
        static string BoatProbe()
        {
            Rigidbody b = VehicleSync.Body("BOAT");
            if (b == null) return "pas de barque";
            GameObject gfx = Game.FindAny("BOAT/GFX");
            Rigidbody g = gfx != null ? gfx.GetComponent<Rigidbody>() : null;
            var sb = new System.Text.StringBuilder();
            sb.Append("racine ").Append(b.position.ToString("F1")).Append(" v ").Append(b.velocity.magnitude.ToString("F1")).Append(b.isKinematic ? " cin" : "").Append(" masse ").Append(b.mass.ToString("F1"));
            if (g != null) sb.Append(" ; GFX v ").Append(g.velocity.magnitude.ToString("F1")).Append(g.isKinematic ? " cin" : "").Append(" masse ").Append(g.mass.ToString("F1")).Append(" ecart ").Append(Vector3.Distance(g.position, b.position).ToString("F2"));
            sb.Append(" ; ").Append(FsmVar("BOAT/GFX/Audio/Engine", "Simulation", "Thrust")).Append(' ').Append(FsmVar("BOAT/GFX/Audio/Engine", "Simulation", "RPM")).Append(' ').Append(FsmVar("BOAT/GFX/Audio/Engine", "Simulation", "Gear"));
            sb.Append(" ; ").Append(FsmVar("BOAT", "Y Velocity", "Velo")).Append(" ; ").Append(FsmVar("BOAT/GFX/Audio", "Sound Engine", "RPM"));
            GameObject au = Game.FindAny("BOAT/GFX/Audio");
            PlayMakerFSM se = au != null ? Game.FsmOn(au, "Sound Engine") : null;
            if (se != null) sb.Append(" [").Append(se.ActiveStateName).Append(se.enabled ? "]" : ", coupe]");
            return sb.ToString();
        }
        static int mscGear, mscVaz, mscLieu; static float mscHandsAt;
        static void MscTest(string mode, float t)
        {
            // Essais communs aux deux jeux (positions des joueurs dans les vehicules sur MSC, 21/10) : camera d'essai, conduite,
            // passager, sonde des places.
            CameraKnobs(t);
            // (24/10, pommeau sorti dans Avatar.KnobPoint par [MWC]) [Test] PassageVitesse=1 : le conducteur passe 1, 2, 3 a 40/43/46 s ;
            // [Test] JournalMains=1 : mains des avatars des autres et rapport de la voiture, toutes les 0,5 s de 30 a 130 s.
            if (Config.GetInt("Test", "PassageVitesse", 0) != 0 && VehicleSync.LocalDrivingName != null)
            {
                string drv = VehicleSync.LocalDrivingName;
                if (t > 40f && mscGear == 0 || t > 43f && mscGear == 1 || t > 46f && mscGear == 2) { mscGear++; Log.Info("autotest : vitesse " + mscGear + " -> " + VehicleSync.TestGear(drv, mscGear)); }
            }
            if (Config.GetInt("Test", "JournalMains", 0) != 0 && t > 30f && t < 130f && Time.realtimeSinceStartup >= mscHandsAt)
            {
                mscHandsAt = Time.realtimeSinceStartup + 0.5f;
                foreach (Avatar a in PlayerSync.Avatars) Log.Info("autotest : mains " + a.HandsState() + " ; " + VehicleSync.GearState(Config.Get("Test", "TestVoiture", "SATSUMA")));
            }
            if (mode == "conduite") { Conduite(t); return; }
            if (mode == "vaz" || mode == "vazcle") { Vaz.Test(mode, t); return; }   // (24/10 : voitures sur la Ruscko)
            if (mode == "vazrallye") { RallyMscVaz.Test(mode, t); return; }   // (24/10 : rallye de MSC avec sa voiture du F10)
            if (mode == "vazpanel") { VazPanel.Test(mode, t); return; }
            if (mode == "tas") { LogPiles.Test(mode, t); return; }
            if (mode == "outils") { HandTools.Test(mode, t); return; }
            if (mode == "gifumain") { Knobs.Test(mode, t); return; }   // (accelerateur a main de la GIFU tire par l'invite, Markos 24/10)
            if (mode == "portebuches") { WoodCarriers.Test(mode, t); return; }   // (outils en main sur MSC : hache, Markos/Squier 24/10)   // (plateau a bois : comptes et prix chez l'invite, rejeu de la paie, 24/10)
            if (mode == "passager" && t > 30f && step == 0) { step = 1; Log.Info("autotest : " + Seats.TestSit(Config.Get("Test", "TestVoiture", "SATSUMA"), Config.GetInt("Test", "TestPlace", 0))); }
            if (mode == "passager") return;
            if (mode == "sondeplaces" && t > 30f && !done) { done = true; Log.Info("autotest : places" + Seats.ProbeSeats(Config.Get("Test", "TestVoiture", "SATSUMA"))); }
            if (mode == "sondeplaces") return;
            if (mode == "portiere" && t > 30f && !done) { done = true; Log.Info("autotest : " + CarDoors.TestOpen(Config.Get("Test", "TestVoiture", "SATSUMA"), true, Config.Get("Test", "TestPorte", ""))); }
            if (mode == "portiere") return;
            // (24/10 : KARTTURI de la boutique s21 sur les speciales de My Summer Car -- copilote : notes ; copilote3 : en voiture)
            if (mode == "copilote" || mode == "copilote3" || mode == "copilote4") { Kartturi.Test(mode, t); Kauppa.TestKartturiDrive(mode, t); return; }
            if (mode == "sac") { SleepingBags.Test(mode, t); return; }   // (sacs de couchage : depliage et sommeil -- [Test] SacDormir)
            if (mode == "mscsauve") { SaveBatchMsc.Test(t); return; }   // (sauvegarde en jeu groupee contre une par une, gel de l'hote 24/10)
            if (Config.Get("Test", "Triche", "").Length > 0 && t > 30f && !tricheDone) { tricheDone = true; Log.Info("autotest : triche " + Config.Get("Test", "Triche", "") + " -> " + (Cheats.Command(Config.Get("Test", "Triche", "")) ?? "ok")); }
            if (mode == "cdmsc") { CdMsc(t); return; }
            if (mode == "mscguepe") { MscGuepe(t); return; }
            if (mode == "mscporte") { MscPorte(t); return; }
            if (mode == "portemagasin") { PorteMagasin(t); return; }
            if (mode == "mscmontage") { MscMontage(t); return; }
            if (mode == "mscvolant") { Parts.TestVolant(t); return; }   // (volant stock monte chez l'autre et declare GT, Kamixak 24/10)
            if (mode == "mscdrag") { DragMsc.Test(t); return; }   // (feuille RESULTS des dragsters avec les pilotes humains, 24/10)
            if (mode == "msctrophee") { RallyMsc.Test(t); return; }   // (trophee du podium du rallye cree chez un joueur : chez les autres ?)
            if (mode == "mscpodium") { RallyMsc.TestPodium(t); return; }   // (podium du rallye qui attend les autres humains, 24/10)
            if (mode == "msclumieres") { Lights.Test(t); return; }   // (ombres des phares, halos, portee sur MSC, 24/10)
            // [Test] Autotest=mscreparation (comme modereparation de MWC, [MWC] 24/10) : l'hote diffuse REPAIRMODE_ON a 40 s et
            // REPAIRMODE_OFF a 55 s ; chacun note toutes les 2 s (30-100 s) l'etat de la vis du volant de la SATSUMA -- chez
            // l'invite elle doit rester dans son etat a lui.
            if (mode == "mscreparation")
            {
                if (Net.Session.IsHost && t > 40f && step == 0) { step = 1; PlayMakerFSM.BroadcastEvent("REPAIRMODE_ON"); Log.Info("autotest : mscreparation, REPAIRMODE_ON diffuse ici"); }
                if (Net.Session.IsHost && t > 55f && step == 1) { step = 2; PlayMakerFSM.BroadcastEvent("REPAIRMODE_OFF"); Log.Info("autotest : mscreparation, REPAIRMODE_OFF diffuse ici"); }
                if (t > 30f && t < 100f && Time.realtimeSinceStartup >= mscHandsAt)
                {
                    mscHandsAt = Time.realtimeSinceStartup + 2f;
                    GameObject bv = Game.FindAny("SATSUMA(557kg, 248)/Dashboard/Steering/CarSteeringPivot/steering column(xxxxx)/pivot_steering_wheel/stock steering wheel(Clone)/Bolts/BoltPM");
                    PlayMakerFSM sf = bv != null ? Game.FsmOn(bv, "Screw") : null;
                    Log.Info("autotest : mscreparation, " + (Net.Session.IsHost ? "hote" : "invite") + " : vis du volant [" + (sf != null ? sf.ActiveStateName : "introuvable") + "]");
                }
                return;
            }
            // [Test] Autotest=sondedetach (24/10) : actions qui ecrivent la variable [Test] SondeVar (Detach) d'un autre automate.
            if (mode == "sondedetach" && t > 40f && step == 0)
            {
                step = 1;
                string vn = Config.Get("Test", "SondeVar", "Detach");
                int n = 0;
                foreach (Object o in Game.AllFsms(0f))
                {
                    var f = o as PlayMakerFSM; if (f == null || f.Fsm == null || f.Fsm.States == null) continue;
                    foreach (FsmState st in f.Fsm.States)
                    {
                        FsmStateAction[] acts;
                        try { acts = st.Actions; } catch { continue; }
                        if (acts == null) continue;
                        foreach (FsmStateAction a in acts)
                        {
                            var sb = a as HutongGames.PlayMaker.Actions.SetFsmBool;
                            if (sb == null || sb.variableName == null || sb.variableName.Value != vn) continue;
                            GameObject tg = sb.gameObject == null ? null : sb.gameObject.OwnerOption == OwnerDefaultOption.UseOwner ? f.gameObject : sb.gameObject.GameObject != null ? sb.gameObject.GameObject.Value : null;
                            n++;
                            Log.Info("autotest sonde : " + Recon.Path(f.transform) + " :: " + f.FsmName + " / " + st.Name + " -> " + (tg != null ? Recon.Path(tg.transform) : "?") + "." + (sb.fsmName != null ? sb.fsmName.Value : "?") + "." + vn + " = " + (sb.setValue != null ? sb.setValue.ToString() : "?"));
                        }
                    }
                }
                Log.Info("autotest sonde : " + n + " action(s)");
            }
            if (mode == "sondedetach") return;
            if (mode == "mscreste") { MscReste(t); return; }
            if (mode == "mscregles") { MscRegles(t); return; }
            if (mode == "mscmeca") { MscMeca(t); return; }
            if (mode == "mscchassis") { MscChassis(t); return; }
            if (mode == "mscinterface")
            {
                // [Test] Autotest=mscinterface (19/10) : captures du HUD noir, des onglets Dragsters / Rallye / Joueurs du F10, puis la carte
                // (MapPanel, comme Autotest=carte : [Test] CarteOuvre).
                if (t > 30f && mscStep == 0) { mscStep = 1; CaptureSoon("msc-hud", 0.5f); }
                if (t > 34f && mscStep == 1) { mscStep = 2; Menu.Show(Menu.TabRace); CaptureSoon("msc-f10-dragsters", 2f); }
                if (t > 40f && mscStep == 2) { mscStep = 3; Menu.Show(Menu.TabRally); CaptureSoon("msc-f10-rallye", 2f); }
                if (t > 46f && mscStep == 3) { mscStep = 4; Menu.Show(Menu.TabPlayers); CaptureSoon("msc-f10-joueurs", 2f); }
                if (t > 52f && mscStep == 4) { mscStep = 5; Menu.Hide(); }
                MapPanel.Test("carte", t);
                return;
            }
            if (mode == "msccarte") MapPanel.Test("carte", t);   // (captures de la carte : [Test] CarteOuvre, CarteZoom2/3)
            if (mode == "msccarte" && t > 25f && mscStep == 0 && Config.GetInt("Test", "CarteReleve", 1) != 0)
            {
                // [Test] Autotest=msccarte (19/10, JD : refaire la carte de MSC) : collisionneurs fixes regroupes (racine/parent/nom,
                // calque, sorte) avec nombre, triangles et etendue ; arbre de MAP sur 3 niveaux ; terrains Unity.
                mscStep = 1;
                var groups = new System.Collections.Generic.Dictionary<string, object[]>();
                foreach (Collider c in Object.FindObjectsOfType<Collider>())
                {
                    if (c.isTrigger || c.attachedRigidbody != null) continue;
                    Transform p = c.transform.parent;
                    string key = Game.RootName(c.transform) + "/" + (p != null ? p.name : "-") + "/" + c.name + " L" + c.gameObject.layer + " " + c.GetType().Name;
                    object[] g;
                    if (!groups.TryGetValue(key, out g)) groups[key] = g = new object[] { 0, 0, c.bounds };
                    g[0] = (int)g[0] + 1;
                    var mc = c as MeshCollider;
                    if (mc != null && mc.sharedMesh != null) try { g[1] = (int)g[1] + mc.sharedMesh.triangles.Length / 3; } catch { }
                    Bounds b = (Bounds)g[2]; b.Encapsulate(c.bounds); g[2] = b;
                }
                var keys = new System.Collections.Generic.List<string>(groups.Keys);
                keys.Sort((a, b) => ((Bounds)groups[b][2]).size.sqrMagnitude.CompareTo(((Bounds)groups[a][2]).size.sqrMagnitude));
                var sb = new System.Text.StringBuilder("autotest : msccarte, collisionneurs (" + keys.Count + " groupes) :");
                foreach (string k in keys)
                {
                    object[] g = groups[k]; Bounds b = (Bounds)g[2];
                    if (Mathf.Max(b.size.x, b.size.z) < 15f) continue;
                    sb.Append("\n  ").Append(k).Append(" x").Append(g[0]).Append(" tri ").Append(g[1]).Append(" min ").Append(b.min.ToString("F0")).Append(" max ").Append(b.max.ToString("F0"));
                }
                Log.Info(sb.ToString());
                sb.Length = 0; sb.Append("autotest : msccarte, MAP :");
                GameObject map = Game.FindAny("MAP");
                if (map != null)
                    foreach (Transform a in map.transform)
                    {
                        sb.Append("\n  ").Append(a.name).Append(a.gameObject.activeSelf ? "" : " (eteint)").Append(" [").Append(a.childCount).Append(']');
                        foreach (Transform b in a)
                        {
                            sb.Append("\n    ").Append(b.name).Append(b.gameObject.activeSelf ? "" : " (eteint)").Append(" [").Append(b.childCount).Append("] L").Append(b.gameObject.layer);
                            int n = 0;
                            foreach (Transform cc in b) { if (n++ >= 25) { sb.Append(" ..."); break; } sb.Append(n == 1 ? " : " : ", ").Append(cc.name); }
                        }
                    }
                foreach (Terrain te in Object.FindObjectsOfType<Terrain>()) sb.Append("\n  TERRAIN ").Append(Recon.Path(te.transform)).Append(" pos ").Append(te.transform.position.ToString("F0")).Append(" taille ").Append(te.terrainData != null ? te.terrainData.size.ToString("F0") : "?");
                Log.Info(sb.ToString());
            }
            // [Test] Autotest=mscfeu (joueurs du 20/10 : « le bois ne prend pas ») : 4 buches (clones du modele firewood) lachees
            // dans la cheminee du salon a 30 s ; automate WoodTrigger::Trigger (etat, Woods) et chaque buche (parent, nom, active)
            // chaque seconde jusqu'a 40 s ; a 42 s SetFire::Use -> « Start fire 2 » comme le clic du joueur ; a 46 s, flamme ?
            if (mode == "mscfeu")
            {
                string fp = Config.Get("Test", "TestCheminee", "YARD/Building/LIVINGROOM/Fireplace");
                GameObject trig = Game.FindAny(fp + "/WoodTrigger");
                // (a deux : [Test] TestActeur=invite -- l'invite lache le bois et allume, l'hote regarde ; sinon l'hote fait tout)
                bool actor = Config.Get("Test", "TestActeur", "hote") == "invite" ? !Net.Session.IsHost : Net.Session.IsHost;
                if (t > 30f && mscStep == 0 && !actor) { mscStep = 9; Log.Info("autotest : mscfeu, regarde"); }
                // (celui qui regarde : l'etat du foyer toutes les 5 s jusqu'a 120 s -- l'autre agit a ses 30-42 s, soit ~40 s plus tard ici)
                if (mscStep == 9 && trig != null && t < 120f && t >= mscLog)
                {
                    mscLog = t + 5f;
                    PlayMakerFSM tf = Game.FsmOn(trig, "Trigger");
                    GameObject sf = Game.FindAny(fp + "/SetFire"), fl = Game.FindAny(fp + "/Flame/FireEffects");
                    PlayMakerFSM uf = sf != null ? Game.FsmOn(sf, "Use") : null;
                    Log.Info("autotest : mscfeu, vu d'ici : Trigger [" + (tf != null ? tf.ActiveStateName : "?") + "] Woods " + (tf != null ? tf.FsmVariables.GetFsmInt("Woods").Value.ToString() : "?") + ", SetFire " + (sf != null && sf.activeInHierarchy ? "actif" : "inactif") + " [" + (uf != null ? uf.ActiveStateName : "?") + "], flamme " + (fl != null && fl.activeInHierarchy ? "ALLUMEE" : "eteinte"));
                }
                if (t > 30f && mscStep == 0)
                {
                    mscStep = 1;
                    GameObject model = Game.FindAny("firewood");
                    if (model == null || trig == null) { Log.Info("autotest : mscfeu, modele " + (model != null) + ", declencheur " + (trig != null)); return; }
                    mscWood = new System.Collections.Generic.List<GameObject>();
                    for (int i = 0; i < 4; i++)
                    {
                        GameObject w = Object.Instantiate(model, trig.transform.position + Vector3.up * (0.35f + 0.12f * i) + trig.transform.right * (0.08f * (i - 1.5f)), trig.transform.rotation) as GameObject;
                        w.SetActive(true); mscWood.Add(w);
                    }
                    Log.Info("autotest : mscfeu, 4 buches lachees dans " + fp + " (modele " + Recon.Path(model.transform) + ", tag " + model.tag + ", calque " + model.layer + ")");
                }
                if (mscStep >= 1 && t < 41f && t >= mscLog && trig != null)
                {
                    mscLog = t + 1f;
                    PlayMakerFSM tf = Game.FsmOn(trig, "Trigger");
                    var sb = new System.Text.StringBuilder("autotest : mscfeu, Trigger [" + (tf != null ? tf.ActiveStateName : "?") + "] Woods " + (tf != null ? tf.FsmVariables.GetFsmInt("Woods").Value.ToString() : "?") + " ;");
                    foreach (GameObject w in mscWood) sb.Append(w == null ? " (detruite)" : " " + w.name + (w.transform.parent != null ? " sous " + w.transform.parent.name : "") + (w.activeInHierarchy ? "" : " inactive") + " v" + (w.GetComponent<Rigidbody>() != null ? w.GetComponent<Rigidbody>().velocity.magnitude.ToString("F2") : "-") + " d" + Vector3.Distance(w.transform.position, trig.transform.position).ToString("F2"));
                    Log.Info(sb.ToString());
                }
                if (t > 42f && mscStep == 1)
                {
                    mscStep = 2;
                    GameObject sf = Game.FindAny(fp + "/SetFire");
                    PlayMakerFSM tf0 = trig != null ? Game.FsmOn(trig, "Trigger") : null;
                    Log.Info("autotest : mscfeu, a 42 s : Trigger [" + (tf0 != null ? tf0.ActiveStateName : "?") + "] Woods " + (tf0 != null ? tf0.FsmVariables.GetFsmInt("Woods").Value.ToString() : "?"));
                    PlayMakerFSM uf = sf != null ? Game.FsmOn(sf, "Use") : null;
                    Log.Info("autotest : mscfeu, SetFire " + (sf == null ? "introuvable" : (sf.activeInHierarchy ? "actif" : "INACTIF") + ", Use [" + (uf != null ? uf.ActiveStateName : "?") + "]"));
                    // (comme le joueur : vise, puis la touche -- « Wait button » et son evenement USE)
                    // (« Wait button » retombe sur « Wait player » des que la souris ne vise plus : etat pose et touche envoyee dans la
                    // meme image, comme un clic en visant)
                    // (le clic ne se simule pas -- « Wait button » retombe aussitot sans la souris dessus : l'etat « Start fire 2 » est pose
                    // directement, comme Fires.TestFireplace de MWC)
                    if (actor && uf != null && sf.activeInHierarchy && uf.Fsm.GetState("Start fire 2") != null) { Game.SetState(uf, "Start fire 2"); mscLog = t + 0.3f; Log.Info("autotest : mscfeu, allume (Start fire 2) -> [" + uf.ActiveStateName + "]"); }
                }
                if (actor && mscStep == 2 && t > 43f && mscLog > 0f && t >= mscLog)
                {
                    mscLog = -1f;
                    GameObject sf = Game.FindAny(fp + "/SetFire");
                    PlayMakerFSM uf = sf != null ? Game.FsmOn(sf, "Use") : null;
                    if (uf != null) Log.Info("autotest : mscfeu, 0,3 s apres la touche : [" + uf.ActiveStateName + "]");
                }
                if (t > 48f && mscStep == 2)
                {
                    mscStep = 3;
                    GameObject fl = Game.FindAny(fp + "/Flame/FireEffects");
                    GameObject sf = Game.FindAny(fp + "/SetFire");
                    PlayMakerFSM uf = sf != null ? Game.FsmOn(sf, "Use") : null;
                    Log.Info("autotest : mscfeu, apres : flamme " + (fl == null ? "introuvable" : fl.activeInHierarchy ? "ALLUMEE" : "eteinte") + ", Use [" + (uf != null ? uf.ActiveStateName : "?") + "]");
                    GameObject pl = Game.PlayerGo; var cc = pl != null ? pl.GetComponent<CharacterController>() : null;
                    if (cc != null && trig != null) { cc.enabled = false; pl.transform.position = trig.transform.position + trig.transform.forward * -1.5f + Vector3.up * 0.9f; pl.transform.LookAt(trig.transform.position); pl.transform.rotation = Quaternion.Euler(0f, pl.transform.eulerAngles.y, 0f); cc.enabled = true; }
                    CaptureSoon("msc-feu", 0.5f);
                }
                return;
            }
            // [Test] Autotest=msctenue (Vaydon, 23/10 : « jonnez when dragging ») : l'invite ([Test] TestActeur=invite) va a cote de la
            // mobylette a 35 s, la prend en main a 40 s comme le jeu (rangee sous le joueur, corps cinematique), la deplace de 4 m
            // de 42 a 52 s, la lache a 55 s ; chacun note sa pose toutes les 2 s (l'hote doit la voir bouger, pas la recaler).
            if (mode == "msctenue")
            {
                Rigidbody jb = VehicleSync.Body("JONNEZ ES(Clone)");
                Transform jt = jb != null ? jb.transform : null;
                if (!Net.Session.IsHost && jt != null)
                {
                    if (t > 35f && mscStep == 0) { mscStep = 1; Log.Info("autotest : msctenue, " + VehicleSync.TestEnter("JONNEZ ES(Clone)", false)); }
                    if (t > 40f && mscStep == 1)
                    {
                        mscStep = 2; Transform pl = Game.PlayerT;
                        jb.isKinematic = true; jt.parent = pl; jt.localPosition = new Vector3(0f, 0.3f, 1.2f); jt.localRotation = Quaternion.identity;
                        Log.Info("autotest : msctenue, mobylette prise en main (sous " + Recon.Path(pl) + ")");
                    }
                    if (t > 42f && t < 52f && mscStep == 2) { Vector3 lp = jt.localPosition; lp.x += 0.4f * Time.deltaTime; jt.localPosition = lp; }
                    if (t > 55f && mscStep == 2) { mscStep = 3; Vector3 wp = jt.position; Quaternion wr = jt.rotation; jt.parent = null; jt.position = wp; jt.rotation = wr; jb.isKinematic = false; Log.Info("autotest : msctenue, mobylette lachee en " + wp.ToString("F1")); }
                }
                if (t > 30f && t < 80f && t >= mscLog && jb != null) { mscLog = t + 2f; Log.Info("autotest : msctenue, mobylette en " + jt.position.ToString("F1") + (jb.isKinematic ? " cin" : "") + (jt.parent != null ? " sous " + jt.parent.name : "")); }
                return;
            }
            // [Test] Autotest=mscbateau2 (квазарик, 22/10 : « la barque disparait pour un joueur, meme apres qu'il en est sorti » --
            // journal de l'hote : BOAT propulsee a 107 puis 219 m/s au premier pas de sa reprise, 15 fois dans la soiree) : l'invite
            // ([Test] TestActeur=invite) monte au moteur a 40 s, lance le moteur a 44 s, avance au large de 48 a 63 s (vitesse
            // imposee), sort a 66 s, coupe le moteur a 70 s (la barque revient a l'hote) ; chacun note la barque toutes les 5 s
            // (pose, vitesse, corps GFX, automates Simulation / Y Velocity / Sound Engine).
            if (mode == "mscbateau2")
            {
                Rigidbody b = VehicleSync.Body("BOAT");
                if (!Net.Session.IsHost)
                {
                    if (t > 35f && mscStep == 0) { mscStep = 1; Log.Info("autotest : mscbateau2, " + VehicleSync.TestEnter("BOAT", false)); }
                    if (t > 40f && mscStep == 1) { mscStep = 2; Log.Info("autotest : mscbateau2, moteur -> " + VehicleSync.TestEnter("BOAT", true)); }
                    if (t > 44f && mscStep == 2) { mscStep = 3; Log.Info("autotest : mscbateau2, lance : " + BoatStart(true)); }
                    if (t > 48f && t < 58f && b != null && !b.isKinematic) { Vector3 v = b.velocity; v.x = 2.8f; v.z = 2.8f; b.velocity = v; }
                    // ('Check speed' refuse la sortie tant qu'un axe de vitesse depasse 0,3 m/s : on attend l'arret)
                    if (t > 68f && mscStep == 3 && b != null && b.velocity.magnitude < 0.2f) { mscStep = 4; Log.Info("autotest : mscbateau2, sortie -> " + VehicleSync.TestExit("BOAT")); }
                    if (t > 74f && mscStep == 4) { mscStep = 5; Log.Info("autotest : mscbateau2, moteur coupe : " + BoatStart(false)); }
                }
                // (l'hote vient pres de la barque a 64 s, comme un joueur qui rejoint le conducteur au rivage : l'automate 'LOD' de la
                // barque la rend physique a moins de 20 m -- meme en copie ?)
                else if (t > 64f && mscStep == 0) { mscStep = 1; Log.Info("autotest : mscbateau2, l'hote s'approche : " + VehicleSync.TestEnter("BOAT", false)); }
                if (t > 30f && t < 130f && t >= mscLog) { mscLog = t + 5f; Log.Info("autotest : mscbateau2, " + BoatProbe()); }
                if (b != null && b.velocity.magnitude > 5f && mscFast++ < 40) Log.Info("autotest : mscbateau2, RAPIDE " + BoatProbe());
                return;
            }
            // [Test] Autotest=mscbateau (joueurs du 20/10) : assis a chaque place de la barque (1, 2, 3) 8 s chacune, captures en
            // troisieme personne ([Test] TroisiemePersonne=1) pour voir la pose et la place.
            if (mode == "mscbateau")
            {
                // A deux ([Test] TestActeur=invite) : l'invite monte au moteur de la barque a 35 s et le lance (Sound Engine.Start) ;
                // l'hote note toutes les 5 s les sources sonores de sa copie (joueurs du 20/10 : moteur du bateau muet chez les autres).
                if (Config.Get("Test", "TestActeur", "") == "invite")
                {
                    if (!Net.Session.IsHost)
                    {
                        if (t > 35f && mscStep == 0) { mscStep = 1; Log.Info("autotest : mscbateau, " + VehicleSync.TestEnter("BOAT", false)); }
                        if (t > 40f && mscStep == 1) { mscStep = 2; Log.Info("autotest : mscbateau, moteur -> " + VehicleSync.TestEnter("BOAT", true)); }
                        if (t > 44f && mscStep == 2)
                        {
                            mscStep = 3;
                            GameObject au = Game.FindAny("BOAT/GFX/Audio");
                            PlayMakerFSM se = au != null ? Game.FsmOn(au, "Sound Engine") : null;
                            FsmBool st = se != null ? se.FsmVariables.FindFsmBool("Start") : null;
                            if (st != null) st.Value = true;
                            Log.Info("autotest : mscbateau, lance : Sound Engine [" + (se != null ? se.ActiveStateName : "?") + "] Start " + (st != null ? st.Value.ToString() : "?"));
                        }
                        if (mscStep == 3 && t < 90f && t >= mscLog) { mscLog = t + 5f; Log.Info("autotest : mscbateau, ici " + VehicleSync.AudioState("BOAT")); }
                    }
                    else if (t > 30f && t < 140f && t >= mscLog) { mscLog = t + 5f; GameObject en = Game.FindAny("BOAT/GFX/Audio/Engine"); Log.Info("autotest : mscbateau, copie : Engine " + (en != null && en.activeInHierarchy ? "allume" : "eteint") + " ; " + VehicleSync.AudioState("BOAT")); }
                    return;
                }
                if (t > 25f && mscStep == 0) { mscStep = 1; Log.Info("autotest : mscbateau, " + Seats.TestSit("BOAT", 1)); CaptureSoon("msc-barque-1", 3f); }
                if (t > 33f && mscStep == 1) { mscStep = 2; Log.Info("autotest : mscbateau, " + Seats.TestLeave() + " ; " + Seats.TestSit("BOAT", 2)); CaptureSoon("msc-barque-2", 3f); }
                if (t > 41f && mscStep == 2) { mscStep = 3; Log.Info("autotest : mscbateau, " + Seats.TestLeave() + " ; " + Seats.TestSit("BOAT", 3)); CaptureSoon("msc-barque-3", 3f); }
                if (t > 49f && mscStep == 3) { mscStep = 4; Log.Info("autotest : mscbateau, " + Seats.TestLeave() + " ; " + Seats.PlaceDans("BOAT")); }
                return;
            }
            // [Test] Autotest=mscfils (Reinmansed, 20/10) : a deux, l'acteur ([Test] TestActeur=invite, sinon l'hote) branche la borne
            // [Test] TestBorne (GroundBattery/BatteryMinusStarter) a 40 s comme un clic (borne allumee, 'Assemble' -> « Finish assembly ») ;
            // chacun note toutes les 5 s l'entree Database/DatabaseWiring/WiringGround1 (Installed) et le fil wiring_ground.
            if (mode == "mscfils")
            {
                bool actor = Config.Get("Test", "TestActeur", "hote") == "invite" ? !Net.Session.IsHost : Net.Session.IsHost;
                string borne = Config.Get("Test", "TestBorne", "GroundBattery/BatteryMinusStarter");
                GameObject sat = null;
                foreach (Rigidbody rb in Object.FindObjectsOfType<Rigidbody>()) if (rb.transform.parent == null && rb.name.StartsWith("SATSUMA")) { sat = rb.gameObject; break; }
                if (sat == null) return;
                if (actor && t > 40f && mscStep == 0)
                {
                    mscStep = 1;
                    Transform tr = sat.transform.Find("Wiring/Triggers/" + borne);
                    PlayMakerFSM f = tr != null ? Game.FsmOn(tr.gameObject, "Assemble") : null;
                    if (f == null) { Log.Info("autotest : mscfils, borne " + borne + " introuvable"); return; }
                    tr.gameObject.SetActive(true);
                    Game.SetState(f, "Finish assembly");
                    Log.Info("autotest : mscfils, borne " + borne + " branchee ici -> [" + f.ActiveStateName + "]");
                }
                if (t > 30f && t < 120f && t >= mscLog)
                {
                    mscLog = t + 5f;
                    GameObject db = Game.FindAny("Database/DatabaseWiring/WiringGround1");
                    PlayMakerFSM d = db != null ? Game.FsmOn(db, "Data") : null;
                    FsmBool inst = d != null ? d.FsmVariables.FindFsmBool("Installed") : null;
                    Transform wire = sat.transform.Find("Wiring/Parts/wiring_ground(xxxxx)");
                    Transform tr = sat.transform.Find("Wiring/Triggers/" + borne);
                    PlayMakerFSM f = tr != null ? Game.FsmOn(tr.gameObject, "Assemble") : null;
                    Log.Info("autotest : mscfils, WiringGround1 Installed " + (inst != null ? inst.Value.ToString() : "?") + ", fil " + (wire != null ? (wire.gameObject.activeSelf ? "allume" : "eteint") : "?") + ", borne " + (tr != null ? (tr.gameObject.activeSelf ? "allumee" : "eteinte") : "?") + " [" + (f != null ? f.ActiveStateName : "?") + "]");
                }
                return;
            }
            if (mode == "mscmort")
            {
                // [Test] Autotest=mscmort (19/10, JD : apres la mort sur la Jonnez, sang a l'ecran et joueur reste accroupi) : au
                // guidon de [Test] TestVoiture (la Jonnez par defaut) a 18/22 s, accident a 30 s (tete detachee + Crash), choix a
                // 33 s ([Test] TestReapparition=2), sonde du sang et posture avant, pendant et apres ; captures.
                string moto = Config.Get("Test", "TestVoiture", "JONNEZ ES(Clone)");
                float tm = t - Config.GetInt("Test", "Decale", 0);   // ([Test] Decale=<s> : tout plus tard, voiture d'un mod appelee apres le chargement)
                // ([Test] MortVaz=<modele> : VAZ n 5 de ce modele appelee a 8 s -- TestVoiture=MWCoop-VAZ-5 ; crash de JD apres la
                // reapparition, mort dans la 205 T16 Evo 2, 24/10)
                if (mscVaz == 0 && t > 8f && Config.Get("Test", "MortVaz", "").Length > 0) { mscVaz = 1; Log.Info("autotest : mscmort, VAZ 5 -> " + Vaz.Request(5, Config.GetInt("Test", "MortVaz", 0))); }
                // ([Test] MortRoule=<m/s> : la voiture poussee droit devant, au volant, jusqu'a l'accident de 30 s -- elle roule encore
                // pendant la mort et la reapparition, comme celle de JD)
                // ([Test] MortLieu=x;y;z;cap : la voiture posee la, au volant, a 23 s -- la route de JD avant son accident)
                string lieu = Config.Get("Test", "MortLieu", "");
                if (lieu.Length > 0 && mscStep >= 2 && mscLieu == 0 && tm > 23f)
                {
                    mscLieu = 1;
                    string[] p = lieu.Split(';');
                    Rigidbody rb = VehicleSync.Body(moto);
                    if (rb != null && p.Length >= 4)
                    {
                        var ic = System.Globalization.CultureInfo.InvariantCulture;
                        Vector3 at = new Vector3(float.Parse(p[0], ic), float.Parse(p[1], ic), float.Parse(p[2], ic));
                        Quaternion q = Quaternion.Euler(0f, float.Parse(p[3], ic), 0f);
                        rb.transform.position = at; rb.transform.rotation = q; rb.position = at; rb.rotation = q; rb.velocity = Vector3.zero;
                        Log.Info("autotest : mscmort, " + moto + " posee en " + at + " cap " + p[3]);
                    }
                }
                float roule = Config.GetFloat("Test", "MortRoule", 0f);
                if (roule > 0f && mscStep >= 2 && mscStep < 4 && tm > 24f)
                {
                    Rigidbody rb = VehicleSync.Body(moto);
                    if (rb != null && !rb.isKinematic) { Vector3 f = rb.transform.forward; f.y = 0f; rb.velocity = f.normalized * roule + Vector3.up * Mathf.Min(rb.velocity.y, 0f); }
                    if (Time.frameCount % 60 == 0) Log.Info("autotest : mscmort, " + moto + " poussee a " + roule + " m/s, en " + (rb != null ? rb.position.ToString("F1") : "?"));
                }
                if (tm > 18f && mscStep == 0) { mscStep = 1; Log.Info("autotest : mscmort, " + VehicleSync.TestEnter(moto, false)); }
                if (tm > 22f && mscStep == 1) { mscStep = 2; Log.Info("autotest : mscmort, guidon -> " + VehicleSync.TestEnter(moto, true)); }
                if (tm > 26f && mscStep == 2) { mscStep = 3; DumpFsms(Config.Get("Test", "Automate", "")); Log.Info("autotest : mscmort, conduite " + VehicleSync.TestDriveDump(moto)); Log.Info("autotest : mscmort, posture au guidon : " + PoseInfo()); Log.Info("autotest : mscmort, " + Respawn.ProbeBlood()); }
                if (tm > 23f && mscStep >= 2 && mscCapot == 0 && Config.GetInt("Test", "TestCapot", 0) != 0) { mscCapot = 1; Log.Info("autotest : mscmort, " + PackHoods.TestSet(moto, true)); }
                if (tm > 25.5f && mscCapot == 1) { mscCapot = 2; Log.Info("autotest : mscmort, " + PackHoods.TestSet(moto, false)); }
                if (tm > 27f && mscStep == 3 && mscPhares == 0) { mscPhares = 1; Log.Info("autotest : mscmort, " + LightsGuard.CmpState()); Log.Info("autotest : mscmort, " + LightsGuard.TestPress()); }
                if (tm > 28.5f && mscStep == 3 && mscPhares == 1) { mscPhares = 2; Log.Info("autotest : mscmort, apres la touche : " + LightsGuard.CmpState()); }
                if (tm > 30f && mscStep == 3) { mscStep = 4; Log.Info("autotest : mscmort, " + Respawn.TestCrash(moto.Split('(')[0].Trim()) + " ; " + (Config.GetInt("Test", "CasseReelle", 0) != 0 ? "mort laissee au jeu" : Respawn.TestDie("Crash"))); }
                if (tm > 31.5f && mscStep == 4) { mscStep = 5; Log.Info("autotest : mscmort, pendant : " + Respawn.ProbeBlood()); CaptureSoon("msc-mort-pendant", 0.2f); }
                if (tm > 38f && mscStep == 5) { mscStep = 6; Log.Info("autotest : mscmort, apres : " + Respawn.State() + " ; posture : " + PoseInfo()); Log.Info("autotest : mscmort, apres : " + Respawn.ProbeBlood()); CaptureSoon("msc-mort-apres", 0.2f); }
                if (tm > 48f && mscStep == 6) { mscStep = 7; Log.Info("autotest : mscmort, 10 s apres : " + Respawn.State() + " ; posture : " + PoseInfo()); CaptureSoon("msc-mort-apres10", 0.2f); }
                if (tm > 50f && mscStep == 7)
                {
                    mscStep = 8;
                    GameObject camGo = Game.PlayerPart("Pivot/AnimPivot/Camera/FPSCamera/FPSCamera");
                    foreach (PlayMakerFSM cr in new[] { Game.PlayerGo != null ? Game.FsmOn(Game.PlayerGo, "Crouch") : null, camGo != null ? Game.FsmOn(camGo, "Death") : null })
                    if (cr != null)
                    {
                        var sb = new System.Text.StringBuilder("autotest : mscmort, " + cr.FsmName + " [" + cr.ActiveStateName + "] depart " + cr.Fsm.StartState + ", globales :");
                        foreach (FsmTransition gt in cr.FsmGlobalTransitions) sb.Append(' ').Append(gt.EventName).Append("->").Append(gt.ToState);
                        foreach (FsmState st in cr.FsmStates)
                        {
                            sb.Append(" ## ").Append(st.Name).Append(" :");
                            foreach (FsmTransition tr in st.Transitions) sb.Append(' ').Append(tr.EventName).Append("->").Append(tr.ToState);
                            sb.Append(" : ").Append(Jobs.DumpActions(cr, st.Name));
                        }
                        sb.Append(" @vars");
                        foreach (NamedVariable v in cr.FsmVariables.GetAllNamedVariables()) sb.Append(' ').Append(v.Name).Append('=').Append(v.ToString());
                        Log.Info(sb.ToString());
                    }
                }
                return;
            }
            if (mode != "mscvoiture") return;
            string car = Config.Get("Test", "TestVoiture", "KEKMET"), part = Config.Get("Test", "TestPorte", "");
            bool host = Net.Session.IsHost;
            if (t > 16f && mscStep == 0) { mscStep = 1; Log.Info("autotest : mscvoiture (" + (host ? "hote" : "invite") + "), portieres de " + car + " : " + CarDoors.ListFor(car)); }
            if (host)
            {
                if (t > 18f && mscStep == 1) { mscStep = 2; Log.Info("autotest : mscvoiture, " + VehicleSync.TestEnter(car, false)); }
                if (t > 22f && mscStep == 2) { mscStep = 3; Log.Info("autotest : mscvoiture, volant -> " + VehicleSync.TestEnter(car, true)); }
                VehicleSync.TestEngine(t > 24f && t < 60f ? 1500f : -1f, 0.5f);
                Rigidbody b = VehicleSync.Body(car);
                if (b != null && t > 26f && t < 38f) { Vector3 f = b.transform.forward; f.y = 0; b.velocity = f.normalized * 6f + Vector3.up * Mathf.Min(b.velocity.y, 0f); }
                // (квазарик, 22/10 : « ignition » affiche chez tous quand un autre tourne la cle du tracteur) : cle sur ACC a 30 s
                // (comme un clic tenu : contact, puis demarreur), relachee a 34 s ('Shut off') ; chacun note l'automate de la cle et le
                // texte GUIinteraction toutes les 2 s -- chez l'invite, 'Wait player' et texte vide.
                // (par evenements, comme la souris : 'Wait button' -ACC-> Test -> ACC on, -START-> Motor starting, -FINISHED-> Shut off ;
                // Game.SetState ne passe pas par les transitions, rien ne serait envoye)
                if (t > 30f && mscStep == 3 && Config.GetInt("Test", "TestCle", 1) != 0) { mscStep = 31; PlayMakerFSM k = IgnitionOf(car); if (k != null) { Game.SetState(k, "Test"); Log.Info("autotest : mscvoiture, cle (clic) -> " + k.ActiveStateName + " (" + Recon.Path(k.transform) + ")"); } }
                if (t > 32f && mscStep == 31) { mscStep = 32; PlayMakerFSM k = IgnitionOf(car); if (k != null) { k.SendEvent("START"); Log.Info("autotest : mscvoiture, cle (demarreur) -> " + k.ActiveStateName); } }
                if (t > 34f && mscStep == 32) { mscStep = 33; PlayMakerFSM k = IgnitionOf(car); if (k != null) { k.SendEvent("FINISHED"); Log.Info("autotest : mscvoiture, cle relachee -> " + k.ActiveStateName); } }
                if (t > 45f && (mscStep == 3 || mscStep == 31 || mscStep == 32 || mscStep == 33)) { mscStep = 4; Log.Info("autotest : mscvoiture, ouvre " + CarDoors.TestOpen(car, true, part)); }
                if (t > 45.6f && mscStep == 4) { mscStep = 5; Log.Info("autotest : mscvoiture, relache " + CarDoors.TestState(car, "Mouse off", part)); }
                if (t > 65f && mscStep == 5) { mscStep = 6; Log.Info("autotest : mscvoiture, ferme " + CarDoors.TestOpen(car, false, part)); }
            }
            if (t > 16f && t < 90f && Time.realtimeSinceStartup >= mscLog)
            {
                mscLog = Time.realtimeSinceStartup + 2f;
                Rigidbody b = VehicleSync.Body(car);
                PlayMakerFSM ig = t > 28f && t < 50f ? IgnitionOf(car) : null;
                FsmString gi = ig != null ? FsmVariables.GlobalVariables.FindFsmString("GUIinteraction") : null;
                Log.Info("autotest : mscvoiture " + VehicleSync.Describe(car) + (b != null ? ", cinematique " + b.isKinematic + ", vitesse " + b.velocity.magnitude.ToString("F1") : "")
                         + (ig != null ? " | cle [" + ig.ActiveStateName + "] texte \"" + (gi != null ? gi.Value : "?") + "\"" : "")
                         + (t > 42f ? " | portiere " + CarDoors.StateOf(car, part) : ""));
            }
        }

        // Automate de la cle de contact d'une voiture de My Summer Car ('Use' avec les etats « ACC on » et « Motor starting »).
        static PlayMakerFSM IgnitionOf(string car)
        {
            Rigidbody b = VehicleSync.Body(car);
            if (b == null) return null;
            foreach (PlayMakerFSM f in b.GetComponentsInChildren<PlayMakerFSM>(true))
                if (f.FsmName == "Use" && f.Fsm.GetState("ACC on") != null && f.Fsm.GetState("Motor starting") != null) return f;
            return null;
        }

        // Posture du joueur local : parent, controleur, automates du joueur (etat), hauteur Crouch, pivots de la camera.
        static string PoseInfo()
        {
            GameObject pl = Game.PlayerGo;
            if (pl == null) return "pas de joueur";
            var sb = new System.Text.StringBuilder();
            var cc = pl.GetComponent<CharacterController>();
            sb.Append("parent ").Append(pl.transform.parent != null ? Recon.Path(pl.transform.parent) : "-").Append(", pos ").Append(pl.transform.position.ToString("F2"))
              .Append(", controleur ").Append(cc != null ? (cc.enabled ? "actif" : "coupe") + " h " + cc.height.ToString("F2") + " c " + cc.center.ToString("F2") : "-");
            foreach (Behaviour b in pl.GetComponents<Behaviour>()) { string n = b.GetType().Name; if (n == "CharacterMotor" || n == "FPSInputController" || n == "MouseLook") sb.Append(", ").Append(n).Append(b.enabled ? " on" : " off"); }
            foreach (PlayMakerFSM f in pl.GetComponentsInChildren<PlayMakerFSM>(true))
            {
                if (!f.gameObject.activeInHierarchy && f.gameObject != pl) continue;
                string n = f.FsmName;
                if (n == "Crouch" || n.Contains("Seat") || n.Contains("Moped") || n.Contains("Death") || f.gameObject == pl)
                {
                    sb.Append(" | ").Append(f.gameObject == pl ? "" : f.name + "::").Append(n).Append(" [").Append(f.ActiveStateName).Append(']');
                    FsmFloat p = f.FsmVariables.FindFsmFloat("Position"); if (p != null) sb.Append(" Position=").Append(p.Value.ToString("F2"));
                }
            }
            foreach (string part in new[] { "Pivot", "Pivot/AnimPivot", "Pivot/AnimPivot/Camera", "Pivot/AnimPivot/Camera/FPSCamera" })
            {
                Transform t = pl.transform.Find(part);
                if (t != null) sb.Append(" | ").Append(part).Append(' ').Append(t.localPosition.ToString("F2")).Append(' ').Append(t.localEulerAngles.ToString("F0"));
            }
            foreach (string g in new[] { "PlayerSeated", "PlayerStop", "PlayerInCar", "PlayerOnMoped", "PlayerCrouch" })
            {
                FsmBool b = FsmVariables.GlobalVariables.FindFsmBool(g); if (b != null) sb.Append(", ").Append(g).Append('=').Append(b.Value);
            }
            FsmString veh = FsmVariables.GlobalVariables.FindFsmString("PlayerCurrentVehicle"); if (veh != null) sb.Append(", PlayerCurrentVehicle='").Append(veh.Value).Append('\'');
            return sb.ToString();
        }

        static string capName;
        static float capAt;
        // [Test] Autotest=pantin : le joueur 2 est « mort » (pantin chez les autres) de 30 a 45 s, en marchant ; captures.
        public static bool TestDead;
        // [Test] MesureCamera=1 : a-coups vus a l'ecran (passager surtout) : a chaque image, l'ecart de la camera (et de la
        // voiture ou l'on est assis) a un mouvement regulier -- p(t) - 2 p(t-1) + p(t-2), ramene a des images de 1/60 s --,
        // et chaque seconde la mediane, le 95e centile et le pire, en millimetres, avec la vitesse.
        static float nanNext;
        static Vector3 mc1, mc2, mv1, mv2; static float md1 = -1f, md2 = -1f, mcNext; static readonly System.Collections.Generic.List<float> mcJ = new System.Collections.Generic.List<float>(), mvJ = new System.Collections.Generic.List<float>();
        static void MeasureCam(float t)
        {
            Transform cam = PlayerSync.LocalCamera;
            if (cam == null) return;
            Transform carT = Seats.SeatedCar;
            if (carT == null && VehicleSync.LocalDrivingRoot != null) carT = VehicleSync.LocalDrivingRoot;
            Vector3 p = cam.position, v = carT != null ? carT.position : p;
            float dt = Time.unscaledDeltaTime;
            if (md2 > 0f && md1 > 0f && dt > 0f)
            {
                // (pas inegaux : second ecart divise par les pas, ramene a 1/60 s)
                Vector3 a = ((p - mc1) / dt - (mc1 - mc2) / md1) * (1f / 60f) * (1f / 60f) / ((dt + md1) * 0.5f);
                Vector3 b = ((v - mv1) / dt - (mv1 - mv2) / md1) * (1f / 60f) * (1f / 60f) / ((dt + md1) * 0.5f);
                mcJ.Add(a.magnitude * 1000f); mvJ.Add(b.magnitude * 1000f);
            }
            mc2 = mc1; mc1 = p; mv2 = mv1; mv1 = v; md2 = md1; md1 = dt;
            if (Time.realtimeSinceStartup < mcNext) return;
            mcNext = Time.realtimeSinceStartup + 1f;
            if (mcJ.Count < 10) { mcJ.Clear(); mvJ.Clear(); return; }
            mcJ.Sort(); mvJ.Sort();
            float spd = carT != null && carT.GetComponent<Rigidbody>() != null ? carT.GetComponent<Rigidbody>().velocity.magnitude : 0f;
            Log.Info("autotest : a-coups camera med " + mcJ[mcJ.Count / 2].ToString("F2") + " p95 " + mcJ[mcJ.Count * 95 / 100].ToString("F2") + " max " + mcJ[mcJ.Count - 1].ToString("F1")
                + " mm | voiture med " + mvJ[mvJ.Count / 2].ToString("F2") + " p95 " + mvJ[mvJ.Count * 95 / 100].ToString("F2") + " max " + mvJ[mvJ.Count - 1].ToString("F1")
                + " mm | " + mcJ.Count + " images, " + (carT != null ? carT.name + " " + spd.ToString("F1") + " m/s" : "a pied"));
            mcJ.Clear(); mvJ.Clear();
        }

        static int mscPhares, mscCapot; static System.Collections.Generic.List<GameObject> mscWood;
        public static void CaptureSoon(string name, float delay) { capName = name; capAt = Time.realtimeSinceStartup + delay; }

        // [Test] Automate=<chemin>:<automate>,... : etats (actions, transitions), variables et evenements de chaque automate.
        // [Test] Autotest=texte, Texte=<mot> : chaque TextMesh (eteints compris) dont le texte contient le mot, avec son chemin et
        // les automates de ses parents (qui l'allume ?).
        static void DumpTexts(string word)
        {
            int n = 0;
            foreach (TextMesh tm in Resources.FindObjectsOfTypeAll<TextMesh>())
            {
                if (tm == null || tm.text == null || tm.gameObject.hideFlags != HideFlags.None) continue;
                if (word.Length > 0 && tm.text.IndexOf(word, System.StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (n++ > 40) break;
                var fs = new System.Text.StringBuilder();
                for (Transform p = tm.transform; p != null; p = p.parent)
                    foreach (PlayMakerFSM f in p.GetComponents<PlayMakerFSM>()) fs.Append(' ').Append(p.name).Append("::").Append(f.FsmName).Append('[').Append(f.ActiveStateName).Append(']');
                Log.Info("autotest : texte '" + tm.text.Replace('\n', ' ') + "' " + Recon.Path(tm.transform) + (tm.gameObject.activeInHierarchy ? " (allume)" : " (eteint)") + " ;" + fs);
            }
            Log.Info("autotest : textes '" + word + "' : " + n);
        }

        static void DumpFsms(string specs)
        {
            foreach (string spec in specs.Split(specs.Contains(";") ? ';' : ','))   // (« ; » quand un chemin contient une virgule : SATSUMA(557kg, 248))
            {
                int c = spec.LastIndexOf(':'); if (c <= 0) continue;
                string path = spec.Substring(0, c).Trim();
                GameObject go = Game.FindAny(path);
                // (racine au nom avec « / » -- GIFU(750/450psi) : FindAny coupe au premier « / » ; par la voiture, puis le reste)
                int close = path.IndexOf(')');
                if (go == null && close > 0 && path.IndexOf('/') < close)
                {
                    Rigidbody vb = VehicleSync.Body(path.Substring(0, close + 1));
                    Transform sub = vb == null ? null : close + 2 < path.Length ? vb.transform.Find(path.Substring(close + 2)) : vb.transform;
                    if (sub != null) go = sub.gameObject;
                }
                // (nom « * » : la liste des automates de l'objet et de ses enfants, avec leurs etats -- pour trouver lequel vider)
                if (go != null && spec.Substring(c + 1).Trim() == "*")
                {
                    foreach (PlayMakerFSM f in go.GetComponentsInChildren<PlayMakerFSM>(true))
                    {
                        var sts = new System.Text.StringBuilder();
                        foreach (FsmState st in f.Fsm.States) sts.Append(st.Name).Append(" | ");
                        Log.Info("autotest : automate " + Recon.Path(f.transform) + " :: " + f.FsmName + " [" + f.ActiveStateName + "] " + sts);
                    }
                    continue;
                }
                PlayMakerFSM fsm = go != null ? Game.FsmOn(go, spec.Substring(c + 1).Trim()) : null;
                if (fsm == null) { Log.Info("autotest : automate " + spec + " introuvable"); continue; }
                Log.Info("autotest : automate " + spec + " (" + Recon.Path(fsm.transform) + "), etat " + fsm.ActiveStateName);
                foreach (FsmState st in fsm.Fsm.States)
                {
                    var tr = new System.Text.StringBuilder();
                    foreach (FsmTransition x in st.Transitions) tr.Append(' ').Append(x.EventName).Append("->").Append(x.ToState);
                    Log.Info("autotest :   " + Jobs.DumpActions(fsm, st.Name) + " ; transitions" + tr);
                }
                foreach (FsmTransition gt in fsm.FsmGlobalTransitions) Log.Info("autotest :   globale " + gt.EventName + " -> " + gt.ToState);
                foreach (NamedVariable v in fsm.FsmVariables.GetAllNamedVariables()) Log.Info("autotest :   var " + v.Name + " = " + v);
                foreach (FsmEvent ev in fsm.Fsm.Events) Log.Info("autotest :   evenement " + ev.Name + (ev.IsGlobal ? " (global)" : ""));
            }
        }

        // [Test] Autotest=cabine, CabinePassager=1 : en fin d'image (ce qui est rendu), l'avatar du conducteur dans le repere
        // de la voiture ou l'invite est assis, pendant qu'elle roule : etendue de ses positions (0 attendu ; avant le
        // correctif du 10/10, vitesse x duree d'image vers l'arriere).
        static bool lateAv; static Vector3 lateMin, lateMax; static float lateLogAt; static int lateN;
        public static int BeltLookLate = -1;   // (essais : regard force en fin d'image vers la ceinture : 0 rangee, 1 boucle)
        // [Test] TestPedale=brake|clutch|throttle : de 40 a 60 s (temps de jeu), cette pedale de [Test] TestVoiture enfoncee ici
        // (15 degres, comme l'automate du jeu), apres CarVisuals et avant les avatars -- le pied du conducteur doit la suivre.
        public static void PedalTest()
        {
            string tped = Config.Get("Test", "TestPedale", "");
            float t = Time.timeSinceLevelLoad;
            if (tped.Length == 0 || t < 40f || t > 60f) return;
            Rigidbody pb = VehicleSync.Body(Config.Get("Test", "TestVoiture", "SORBET"));
            if (pb != null) foreach (Transform x in pb.GetComponentsInChildren<Transform>(true)) if (x.name == "pivot_" + tped) x.localRotation = Quaternion.Euler(15f, 0f, 0f);
        }

        public static void LateMeasure()
        {
            Vector3 blt;
            // (camera d'essai aux yeux du joueur, meme reglage que la sienne : le jeu replace la camera du passager)
            if (BeltLookLate >= 0 && PlayerSync.LocalCamera != null && Seats.TestBeltTarget(BeltLookLate, out blt))
            {
                Camera src = PlayerSync.LocalCamera.GetComponent<Camera>();
                if (testCam == null) { testCam = new GameObject("MWCoop-CameraEssai").AddComponent<Camera>(); testCam.depth = 100; testCam.nearClipPlane = src != null ? src.nearClipPlane : 0.03f; testCam.fieldOfView = src != null ? src.fieldOfView : 60f; }
                testCam.transform.position = PlayerSync.LocalCamera.position;
                testCam.transform.rotation = Quaternion.LookRotation(blt - PlayerSync.LocalCamera.position, PlayerSync.LocalCamera.up);
            }
            if (Config.GetInt("Test", "JournalSiege", 0) != 0 && Time.frameCount % 300 < 3)
                foreach (Avatar a in PlayerSync.Avatars)
                {
                    Transform pv = a.PelvisBone, car = a.VehicleT;
                    if (pv != null && car != null) Log.Info("siege : fin d'image " + Time.frameCount + " bassin " + car.InverseTransformPoint(pv.position).ToString("F2") + " racine " + car.InverseTransformPoint(a.Root.transform.position).ToString("F2"));
                }
            if (Config.Get("Test", "Autotest", "") != "cabine" || Config.GetInt("Test", "CabinePassager", 0) == 0 || Net.Session.IsHost || !Seats.Seated) return;
            Rigidbody b = VehicleSync.Body(Config.Get("Test", "TestVoiture", "SORBET(190-200psi)"));
            if (b == null) return;
            foreach (Avatar a in PlayerSync.Avatars)
            {
                if (a.Root == null) continue;
                float v = VehicleSync.RemoteSpeed(a.Player.Id);
                if (v < 3f) continue;
                Vector3 lp = b.transform.InverseTransformPoint(a.Root.transform.position);
                if (!lateAv) { lateAv = true; lateMin = lateMax = lp; } else { lateMin = Vector3.Min(lateMin, lp); lateMax = Vector3.Max(lateMax, lp); }
                lateN++;
                if (Time.realtimeSinceStartup >= lateLogAt)
                {
                    lateLogAt = Time.realtimeSinceStartup + 1f;
                    Log.Info("autotest : fin d'image, conducteur dans la voiture " + lp.ToString("F3") + " a " + (v * 3.6f).ToString("F0") + " km/h ; etendue " + (lateMax - lateMin).ToString("F3") + " m sur " + lateN + " images");
                }
            }
        }

        public static void Update()
        {
            if (!Config.Testing) return;   // (partie normale : aucun essai, rien a regarder a chaque image)
            string mode = Config.Get("Test", "Autotest", "");
            WaitScreen.Test(mode, Time.realtimeSinceStartup);   // au menu : avant le filtre GAME
            Contenu.MenuTest(mode);
            // [Test] Captures=s1,s2... : capture d'ecran a ces secondes depuis le lancement (menu compris), dumps\ecran-<s>.png.
            if (captureTimes == null)
            {
                captureTimes = new System.Collections.Generic.List<float>();
                foreach (string c in Config.Get("Test", "Captures", "").Split(',')) { float v; if (float.TryParse(c.Trim(), out v)) captureTimes.Add(v); }
            }
            if (captureTimes.Count > 0 && Time.realtimeSinceStartup >= captureTimes[0])
            {
                string png = System.IO.Path.Combine(System.IO.Path.Combine(Log.DataDir, "dumps"), "ecran-" + ((int)captureTimes[0]).ToString("000") + ".png");
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(png));
                Application.CaptureScreenshot(png);
                Log.Info("autotest : capture " + png + " (" + Game.LevelName + ")");
                captureTimes.RemoveAt(0);
            }
            if (capName != null && Time.realtimeSinceStartup >= capAt)
            {
                string png = System.IO.Path.Combine(System.IO.Path.Combine(Log.DataDir, "dumps"), "ecran-" + capName + ".png");
                Application.CaptureScreenshot(png);
                Log.Info("autotest : capture " + png);
                capName = null;
            }
            if (Game.LevelName != "GAME") { t0 = -1; return; }
            if (t0 < 0) t0 = Time.realtimeSinceStartup;
            float t = Time.realtimeSinceStartup - t0;
            if (Game.Msc)   // (My Summer Car : ses essais seulement, les autres visent des objets de MWC ; vidage d'automates : les deux jeux)
            {
                if (mode == "automate" && t > 40f && step == 0) { step = 1; DumpFsms(Config.Get("Test", "Automate", "")); }
                if (mode == "texte" && t > 40f && step == 0) { step = 1; DumpTexts(Config.Get("Test", "Texte", "")); }
                // [Test] Autotest=cablage (MSC, 2 joueurs) : l'invite tient le faisceau de 40 a 55 s ; l'hote note toutes les 3 s ce que
                // vaut sa globale WiringTool (attendu : l'objet lointain pendant la prise, le faisceau ensuite) -- WiringGuard, 24/10.
                if (mode == "cablage")
                {
                    if (!MWCoop.Net.Session.IsHost && t > 40f && t < 55f && PlayerSync.LocalCamera != null) { Props.TestHold(Props.FindKey("wiring mess"), PlayerSync.LocalCamera); if (step == 0) { step = 1; Log.Info("autotest : cablage, faisceau pris par l'invite (" + Props.FindKey("wiring mess") + ")"); } }
                    if (!MWCoop.Net.Session.IsHost && t > 55f && step == 1) { step = 2; Props.TestHold(Props.FindKey("wiring mess"), null); Log.Info("autotest : cablage, faisceau lache"); }
                    if (MWCoop.Net.Session.IsHost && t > 35f && t < 75f && t >= holdLog) { holdLog = t + 3f; FsmGameObject wt = FsmVariables.GlobalVariables.FindFsmGameObject("WiringTool"); Log.Info("autotest : cablage, WiringTool ici = " + (wt != null && wt.Value != null ? wt.Value.name : "null")); }
                }
                MscTest(mode, t); return;
            }
            if (Config.Get("Test", "Triche", "").Length > 0 && t > 30f && !tricheDone) { tricheDone = true; Log.Info("autotest : triche " + Config.Get("Test", "Triche", "") + " -> " + (Cheats.Command(Config.Get("Test", "Triche", "")) ?? "ok")); }
            // Essais propres a chaque module (chacun ses modes et son compteur d'etapes).
            CarDoors.Test(mode, t); Npcs.Test(mode, t); Props.Test(mode, t); AssembleIcon.Test(mode, t); DroneCam.Test(mode, t); TaxiCustomer.Test(mode, t); WalletPanel.Test(mode, t); Cheats.Test(mode, t); MapPanel.Test(mode, t); ModSecondMachtwagen.Test(mode, t); Consume.Test(mode, t); Vitrines.Test(mode, t);
            Wallet.Test(mode, t); Traffic.Test(mode, t); Machines.Test(mode, t); HandTools.Test(mode, t); ThirdPerson.Test(mode, t); LogPiles.Test(mode, t); CarSale.Test(mode, t); VehicleSync.TestRelais(mode, t); Consume.TestPick(mode, t); Fluids.TestFill(mode, t); ModMachtwagen300D.Test(mode, t); SleepingBags.Test(mode, t); ModCheatbox.Test(mode, t); CarLeaks.Test(mode, t); Sauna.Test(mode, t);
            if (mode == "horszones") Props.TestHorsZones(t);   // (objets sans ID hors des anciennes racines du monde : REPAIRSHOP, PERAPORTTI, LANDFILL)
            if (mode == "sondesac" && t > 20f && step == 0)
            {
                step = 1;
                foreach (string sp in new[] { "Spawner/BagContentsFleetari", "Spawner/BagContentsStore" })
                {
                    GameObject g = Game.FindAny(sp);
                    PlayMakerFSM f = g != null ? Game.FsmOn(g, "Logic") : null;
                    if (f == null) continue;
                    foreach (string sn in new[] { "Items", "State 7", "State 15", "State 4", "State 16", "Check empty" })
                    {
                        HutongGames.PlayMaker.FsmState s = f.Fsm.GetState(sn);
                        if (s == null || s.Actions == null) continue;
                        foreach (var a in s.Actions) Log.Info("autotest : sac " + sp + "/" + sn + " : " + Recon.Describe(a));
                    }
                }
            }
            if (mode == "sondeoptions" && t > 20f && step == 0)
            {
                step = 1;
                GameObject db = Game.FindAny("Systems/OptionsDB");
                if (db != null)
                    foreach (PlayMakerFSM f in db.GetComponents<PlayMakerFSM>())
                        foreach (string sn in new[] { "Save data 2", "Load 2", "Save data", "Load", "State 3" })
                        {
                            HutongGames.PlayMaker.FsmState s = f.Fsm.GetState(sn);
                            if (s == null || s.Actions == null) continue;
                            for (int i = 0; i < s.Actions.Length && i < 40; i++) Log.Info("autotest : options " + f.FsmName + "/" + sn + " : " + Recon.Describe(s.Actions[i]));
                        }
            }
            Frost.Test(mode, t); Tow.Test(mode, t); Calls.Test(mode, t); Wear.Test(mode, t);
            Cooking.Test(mode, t); Fires.Test(mode, t); Garage.Test(mode, t); Home.Test(mode, t); Gestures.Test(mode, t);
            VehicleSync.Test(mode, t); Jobs.Test(mode, t); SewageHose.Test(mode, t); RunOver.Test(mode, t); CustomPaint.Test(mode, t); FuelPrices.Test(mode, t); Wear.TestCup(mode, t); PhoneKeys.Test(mode, t); CarHits.Test(mode, t); Gestures.Test2(mode, t); WindowCranks.Test(mode, t); Pvp.Test(mode, t); ModRam1500.Test(mode, t); TeimoSled.Test(mode, t); Parts.Test(mode, t); Races.Test(mode, t); Rally.Test(mode, t); Kartturi.Test(mode, t); Net.ModServe.Test(mode, t); Kauppa.TestKartturi(mode, t); Kauppa.TestKartturiDrive(mode, t); PushDoors.Test(mode, t); WoodCarriers.Test(mode, t); NewHud.Test(mode, t); ExitGuard.Test(mode, t); LightsGuard.Test(mode, t);
            MWCoop.Net.SaveTransfer.Test(mode, t); World.Test(mode, t); Menu.Test(mode, t); Studio.Test(mode, t); Vaz.Test(mode, t);
            if (mode == "marche" && t > 5f)
            {
                GameObject p = Game.PlayerGo;
                var cc = p != null ? p.GetComponent<CharacterController>() : null;
                if (cc == null) return;
                p.transform.Rotate(0, 40f * Time.deltaTime, 0);
                cc.SimpleMove(p.transform.forward * 1.5f);
            }
            // [Test] Autotest=moto : l'invite monte sur la Jonnez (15 s a cote, 22 s au guidon ; [Test] MotoPassager=1 : place
            // arriere, Seats) ; l'hote le regarde (camera vers l'avatar le plus proche), capture moto-<t> a 35, 45 s ; a 40 s
            // l'avatar fait le kick (comme la commande Kickstart rejouee), capture a 40,3 s.
            if (mode == "moto")
            {
                string jn = Config.Get("Test", "MotoVoiture", "JONNEZ ES(Clone)");   // ([Test] MotoVoiture : un autre vehicule, camera pres du conducteur)
                bool other = !jn.StartsWith("JONNEZ");
                bool rearSeat = Config.GetInt("Test", "MotoPassager", 0) != 0;
                if (!Net.Session.IsHost)
                {
                    if (rearSeat) { if (t > 20f && step == 0) { step = 1; Log.Info("autotest : moto, place " + Config.GetInt("Test", "MotoPlace", 1) + " -> " + Seats.TestSit(jn, Config.GetInt("Test", "MotoPlace", 1))); } }
                    else
                    {
                        if (t > 15f && step == 0) { step = 1; Log.Info("autotest : moto -> " + VehicleSync.TestEnter(jn, false)); }
                        if (t > 22f && step == 1) { step = 2; Log.Info("autotest : moto, guidon -> " + VehicleSync.TestEnter(jn, true)); }
                        // ([Test] MotoMoteur=tr/min : moteur tenu en marche (faux regime envoye) -- son chez l'hote, 13/10)
                        if (t > 25f && step == 2 && Config.GetFloat("Test", "MotoMoteur", 0f) > 0f) { step = 3; VehicleSync.TestEngine(Config.GetFloat("Test", "MotoMoteur", 0f), 0.4f); Log.Info("autotest : moto, moteur tenu a " + Config.GetFloat("Test", "MotoMoteur", 0f) + " tr/min"); }
                    }
                }
                else if (t > 25f)
                {
                    // camera d'essai de profil, a 1,6 m ([Test] MotoCam=x,y,z dans le repere de la moto)
                    Rigidbody mob = other ? VehicleSync.Body(jn.Split('(')[0]) : null;   // (nom avec « / » : pas un chemin)
                    GameObject mo = mob != null ? mob.gameObject : Game.FindAny(jn);
                    // (autre vehicule : l'hote vient a 6 m, sinon le jeu ne montre pas l'interieur de la cabine)
                    if (other && mo != null && !teleported) { teleported = true; Menu.TeleportTo(mo.transform.TransformPoint(new Vector3(4f, -0.8f, 2f)), 0f, "essai"); }
                    if (mo != null)
                    {
                        if (testCam == null) { testCam = new GameObject("MWCoop-CameraEssai").AddComponent<Camera>(); testCam.depth = 100; testCam.nearClipPlane = 0.03f; testCam.fieldOfView = 60; }
                        string mc = Config.Get("Test", "MotoCam", "-1.6,0.55,0.1"); string[] mp = mc.Split(',');
                        var ci = System.Globalization.CultureInfo.InvariantCulture;
                        Vector3 off = new Vector3(float.Parse(mp[0], ci), float.Parse(mp[1], ci), float.Parse(mp[2], ci)), dh;
                        if (other && Seats.DriverHead(mo.transform, out dh))
                        {   // (repere : la tete du conducteur ; on vise sa poitrine)
                            testCam.transform.position = mo.transform.TransformPoint(dh + off);
                            Vector3 aim = dh + new Vector3(Config.GetFloat("Test", "MotoViseX", 0f), Config.GetFloat("Test", "MotoViseY", -0.45f), 0.1f);   // ([Test] MotoViseX : decale la visee, ex. vers le passager)
                            testCam.transform.LookAt(mo.transform.TransformPoint(aim));
                        }
                        else
                        {
                            testCam.transform.position = mo.transform.TransformPoint(off);
                            testCam.transform.LookAt(mo.transform.TransformPoint(new Vector3(0f, 0.45f, 0f)));
                        }
                    }
                    if (t > 35f && step == 0) { step = 1; CaptureSoon("moto-35", 0.05f); }
                    if (t > 40f && step == 1)
                    {
                        step = 2;
                        GameObject ks = Game.FindAny(jn + "/LOD/Dashboard/Kickstart");
                        foreach (Avatar a in PlayerSync.Avatars) if (ks != null) a.ReachFor(ks.transform);
                        CaptureSoon("moto-kick", 0.3f);
                        Log.Info("autotest : moto, kick " + (ks != null ? "montre" : "kickstart introuvable"));
                    }
                    if (t > 45f && step == 2) { step = 3; CaptureSoon("moto-45", 0.05f); }
                }
            }
            if (mode == "regarde" && t > 3f)
            {
                // [Test] TestClips=clip@t;clip@t... : a partir de 20 s, un clip toutes les 4 s, capture a +2,5 s.
                string list = Config.Get("Test", "TestClips", "");
                if (list.Length > 0 && t > 20f)
                {
                    string[] items = list.Split(';');
                    int k = (int)((t - 20f) / 4f);
                    if (k < items.Length)
                    {
                        string[] ct = items[k].Split('@');
                        string want = ct[0];
                        float at = ct.Length > 1 ? float.Parse(ct[1], System.Globalization.CultureInfo.InvariantCulture) : 1f;
                        if (Avatar.ForceClip != want || Avatar.ForceTime != at)
                        {
                            Avatar.ForceClip = want; Avatar.ForceTime = at;
                            shotAt = Time.realtimeSinceStartup + 2.5f;
                            seenFlags = -2;
                            clipShot = want + "@" + at;
                        }
                    }
                    else Avatar.ForceClip = null;
                }
                LookAtNearestAvatar();
                // [Test] CapturePeriode=N : en plus, une capture toutes les N s a partir de 20 s (pose-t<s>.png).
                int per = Config.GetInt("Test", "CapturePeriode", 0);
                if (per > 0 && t > 20f && t - lastPeriodic >= per)
                {
                    lastPeriodic = t;
                    string dirp = System.IO.Path.Combine(Log.DataDir, "dumps");
                    System.IO.Directory.CreateDirectory(dirp);
                    string pngp = System.IO.Path.Combine(dirp, "pose-t" + ((int)t).ToString("000") + ".png");
                    Application.CaptureScreenshot(pngp);
                    Log.Info("autotest : capture " + pngp);
                }
                // Capture 2,5 s apres chaque changement de posture de l'avatar regarde : dumps\pose-<drapeaux>.png
                foreach (Avatar a in PlayerSync.Avatars)
                {
                    int fl = a.Player.State.Flags;
                    if (fl != seenFlags) { seenFlags = fl; shotAt = Time.realtimeSinceStartup + 2.5f; }
                    break;
                }
                if (shotAt > 0 && Time.realtimeSinceStartup >= shotAt)
                {
                    shotAt = -1;
                    string dir = System.IO.Path.Combine(Log.DataDir, "dumps");
                    System.IO.Directory.CreateDirectory(dir);
                    string png = System.IO.Path.Combine(dir, Avatar.ForceClip != null && clipShot != null ? "clip-" + clipShot.Replace("@", "-") + ".png" : "pose-" + seenFlags + ".png");
                    Application.CaptureScreenshot(png);
                    Log.Info("autotest : capture " + png);
                }
            }
            if (mode == "accroupi")
            {
                // Comme la touche du jeu : niveau 1 a 22 s, niveau 2 a 34 s, debout a 46 s (automate Crouch).
                PlayMakerFSM cf = Game.FindFsm("PLAYER", "Crouch");
                if (cf != null && t > 22f && step == 0) { step = 1; Game.SetState(cf, "Move down 1"); Log.Info("autotest : accroupi 1"); }
                if (cf != null && t > 34f && step == 1) { step = 2; Game.SetState(cf, "Move down 2"); Log.Info("autotest : accroupi 2"); }
                if (cf != null && t > 46f && step == 2) { step = 3; Game.SetState(cf, "Move up"); Log.Info("autotest : debout"); }
            }
            if (mode == "conduite") Conduite(t);
            if (mode == "gifulevier") GifuLevier(t);
            // [Test] Autotest=modereparation (MWC, 2 joueurs, sauvegarde Rivett) : l'hote diffuse REPAIRMODE_ON a 40 s (cle sortie)
            // et REPAIRMODE_OFF a 55 s ; chacun note a 48 et 63 s l'etat de la vis du culbuteur 1. Chez l'invite elle doit rester
            // dans son etat a lui (avant le 24/10 : rejouee, On puis Off comme chez l'hote).
            if (mode == "modereparation")
            {
                if (MWCoop.Net.Session.IsHost && t > 40f && step == 0) { step = 1; PlayMakerFSM.BroadcastEvent("REPAIRMODE_ON"); Log.Info("autotest : modereparation, REPAIRMODE_ON diffuse ici"); }
                if (MWCoop.Net.Session.IsHost && t > 55f && step == 1) { step = 2; PlayMakerFSM.BroadcastEvent("REPAIRMODE_OFF"); Log.Info("autotest : modereparation, REPAIRMODE_OFF diffuse ici"); }
                if (t > 48f && step2 == 0 || t > 63f && step2 == 1)
                {
                    step2++;
                    GameObject rk = Game.FindAny("CORRIS/MotorPivot/MassCenter/Block/VINP_Block/Engine Block(VINX0)/VINP_Cylinderhead/Cylinder Head(VINX0)/ValvesExhaust/Cyl1Exh/VINP_Rocker1Exh/Rocker(VINXX)/Bolts/BoltPM");
                    PlayMakerFSM sf = rk != null ? Game.FsmOn(rk, "Screw") : null;
                    Log.Info("autotest : modereparation, " + (MWCoop.Net.Session.IsHost ? "hote" : "invite") + " (" + step2 + ") : vis du culbuteur 1 [" + (sf != null ? sf.ActiveStateName : "introuvable") + "]");
                }
            }
            // [Test] Autotest=sondeautomates ([Test] SondeMot=mot, [Test] SondeSous=CORRIS) : a 40 s, chaque etat d'automate sous cet
            // objet dont les actions citent ce mot (ce qui ecrit Drivetrain.clutchEngageSpeed, NotHuyarma 24/10).
            if (mode == "sondeautomates" && t > 40f && !done)
            {
                done = true;
                string word = Config.Get("Test", "SondeMot", "clutch"), under = Config.Get("Test", "SondeSous", "CORRIS");
                GameObject root = Game.FindAny(under);
                int n = 0;
                if (root != null)
                    foreach (PlayMakerFSM f in root.GetComponentsInChildren<PlayMakerFSM>(true))
                        foreach (FsmState st in f.Fsm.States)
                        {
                            string a;
                            try { a = Jobs.DumpActions(f, st.Name); } catch { continue; }
                            if (a.IndexOf(word, System.StringComparison.OrdinalIgnoreCase) < 0) continue;
                            n++; Log.Info("autotest : sonde " + Recon.Path(f.transform) + "::" + f.FsmName + " [" + f.ActiveStateName + "] " + a);
                        }
                Log.Info("autotest : sonde '" + word + "' sous " + under + " : " + n + " etat(s)");
                foreach (MonoBehaviour m in root != null ? root.GetComponentsInChildren<MonoBehaviour>(true) : new MonoBehaviour[0])
                    if (m != null && m.GetType().Name == "Drivetrain")
                        foreach (System.Reflection.FieldInfo fi in m.GetType().GetFields())
                            if (fi.Name.IndexOf(word, System.StringComparison.OrdinalIgnoreCase) >= 0) Log.Info("autotest : sonde Drivetrain." + fi.Name + " = " + fi.GetValue(m));
            }
            // [Test] Autotest=graphismes : etat des reglages a 25 s (journal).
            if (mode == "graphismes" && t > 25f && step == 0) { step = 1; Log.Info("autotest : graphismes " + Gfx.Describe()); }
            if (mode == "graphismes" && t > 28f && step == 1) { step = 2; Gfx.ApplyPreset(Config.GetInt("Test", "TestPrereglage", 3)); }
            if (mode == "graphismes" && t > 32f && step == 2) { step = 3; Log.Info("autotest : graphismes apres prereglage " + Gfx.Describe()); }
            // [Test] Autotest=sondelumieres : toutes les lumieres de la scene (inactives comprises) et les objets qui ressemblent
            // a des lampadaires, dans dumps\lumieres.txt.
            if (mode == "sondelumieres" && t > 20f && !done)
            {
                done = true;
                var sb = new System.Text.StringBuilder();
                int n = 0;
                foreach (Object o in Resources.FindObjectsOfTypeAll(typeof(Light)))
                {
                    var l = (Light)o;
                    if (l.hideFlags != HideFlags.None || l.gameObject.hideFlags != HideFlags.None) continue;
                    n++;
                    sb.Append(l.type).Append('\t').Append(l.gameObject.activeInHierarchy ? "actif" : "inactif").Append(l.enabled ? "" : " (coupee)")
                      .Append('\t').Append(l.shadows).Append('\t').Append(l.range.ToString("F0")).Append('\t').Append(l.intensity.ToString("F2")).Append('\t').Append(l.spotAngle.ToString("F0"))
                      .Append('\t').Append(l.color).Append('\t').Append(l.renderMode).Append('\t').Append(l.cullingMask).Append('\t').Append(Recon.Path(l.transform)).Append('\n');
                }
                sb.Append("\n---- objets nommes comme des lampes\n");
                var seen = new System.Collections.Generic.Dictionary<string, int>();
                foreach (Object o in Resources.FindObjectsOfTypeAll(typeof(Transform)))
                {
                    var tr = (Transform)o;
                    if (tr.hideFlags != HideFlags.None || tr.gameObject.hideFlags != HideFlags.None) continue;
                    string nm = tr.name.ToLowerInvariant();
                    if (!(nm.Contains("lamp") || nm.Contains("streetl") || nm.Contains("valo") || nm.Contains("pylv") || nm.Contains("pole") || nm.Contains("bulb") || nm.Contains("light"))) continue;
                    string key = tr.name + " <- " + (tr.parent != null ? tr.parent.name : "-");
                    int c; seen.TryGetValue(key, out c); seen[key] = c + 1;
                }
                foreach (var kv in seen) sb.Append(kv.Value).Append('\t').Append(kv.Key).Append('\n');
                string dir = System.IO.Path.Combine(Log.DataDir, "dumps");
                System.IO.Directory.CreateDirectory(dir);
                System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "lumieres.txt"), sb.ToString());
                Log.Info("autotest : lumieres : " + n + " lumieres, " + seen.Count + " noms de lampes, dumps\\lumieres.txt ; qualite " + QualitySettings.GetQualityLevel() + ", ombres " + QualitySettings.shadowDistance);
            }
            // [Test] Autotest=lumieres : nuit (TestHeure, 23 h) a 15 s ; a 22 s, devant la SORBET phares allumes ([Test]
            // LumieresLieu=voiture) ou pres des lampadaires (rue) ; etat a 30 s et 40 s (captures : Captures=...).
            if (mode == "lumieres")
            {
                if (t > 15f && step == 0)
                {
                    step = 1;
                    PlayMakerFSM c = Game.FindFsm("MAP/Sun/PivotSun/SUN", "Color");
                    if (c != null) { c.FsmVariables.GetFsmInt("Time").Value = Config.GetInt("Test", "TestHeure", 23); c.SendEvent("TIMESKIP"); }
                    Log.Info("autotest : lumieres : nuit");
                }
                if (t > 22f && step == 1)
                {
                    step = 2;
                    GameObject pl = Game.PlayerGo;
                    Vector3 at = Vector3.zero, toward = Vector3.zero;
                    if (Config.Get("Test", "LumieresLieu", "voiture") == "rue")
                    {
                        GameObject sl = Game.FindAny("MAP/StreetLights/1");
                        if (sl != null) { toward = sl.transform.position; at = toward + new Vector3(18f, 1f, 6f); }
                    }
                    else
                    {
                        Rigidbody car = VehicleSync.Body(Config.Get("Test", "TestVoiture", "SORBET(190-200psi)"));
                        if (car != null)
                        {
                            foreach (Light li in car.GetComponentsInChildren<Light>(true))
                                if (li.type == LightType.Spot && li.name.Contains("Short"))
                                    for (Transform p = li.transform; p != null && p != car.transform; p = p.parent) p.gameObject.SetActive(true);
                            toward = car.position;
                            at = car.transform.TransformPoint(new Vector3(1.5f, 0.5f, 9f));
                        }
                    }
                    if (pl != null && at != Vector3.zero)
                    {
                        var cc = pl.GetComponent<CharacterController>();
                        if (cc != null) cc.enabled = false;
                        pl.transform.position = at;
                        Vector3 d = toward - at; d.y = 0;
                        pl.transform.rotation = Quaternion.LookRotation(d);
                        if (cc != null) cc.enabled = true;
                    }
                    Log.Info("autotest : lumieres : joueur en " + at.ToString("F0") + " vers " + toward.ToString("F0"));
                }
                // (phares de la voiture : rallumes a chaque image, contact coupe le jeu les eteint)
                if (step >= 2 && Config.Get("Test", "LumieresLieu", "voiture") != "rue")
                {
                    Rigidbody car = VehicleSync.Body(Config.Get("Test", "TestVoiture", "SORBET(190-200psi)"));
                    if (car != null)
                        foreach (Light li in car.GetComponentsInChildren<Light>(true))
                            if (li.type == LightType.Spot && li.name.Contains("Short"))
                            {
                                li.enabled = true;
                                for (Transform p = li.transform; p != null && p != car.transform; p = p.parent) if (!p.gameObject.activeSelf) p.gameObject.SetActive(true);
                            }
                }
                if ((t > 30f && step == 2) || (t > 40f && step == 3)) { step++; Log.Info("autotest : lumieres " + Lights.Describe()); }
            }
            // [Test] Autotest=racines : a 40 s, racines de la scene (actives ou non) et les corps a CarDynamics (actifs ou non).
            if (mode == "racines" && t > 40f && !done)
            {
                done = true;
                var sb = new System.Text.StringBuilder("autotest : racines :");
                foreach (GameObject r in Recon.SceneRoots()) sb.Append(' ').Append(r.activeSelf ? "" : "[off]").Append(r.name);
                sb.Append(" ; voitures :");
                foreach (Object o in Resources.FindObjectsOfTypeAll(typeof(Rigidbody)))
                {
                    var rb = (Rigidbody)o;
                    if (rb.hideFlags != HideFlags.None || rb.GetComponent("CarDynamics") == null) continue;
                    sb.Append(' ').Append(Recon.Path(rb.transform)).Append(rb.gameObject.activeInHierarchy ? "" : "[off]").Append(rb.GetComponent<AxisCarController>() != null ? "(axis)" : "");
                }
                Log.Info(sb.ToString());
            }
            // [Test] Autotest=evenements : l'hote passe a un jour de match (GlobalDay 3) a 20 h (fete chez Jokke) a 20 s ;
            // a 45 s et 60 s, les deux notent l'evenement et la fete (a comparer).
            if (mode == "evenements")
            {
                if (Net.Session.IsHost && t > 20f && step == 0)
                {
                    step = 1;
                    FsmInt day = FsmVariables.GlobalVariables.FindFsmInt("GlobalDay");
                    if (day != null) day.Value = Config.GetInt("Test", "TestJour", 3);
                    PlayMakerFSM c = Game.FindFsm("MAP/Sun/PivotSun/SUN", "Color");
                    if (c != null) { c.FsmVariables.GetFsmInt("Time").Value = Config.GetInt("Test", "TestHeure", 20); c.SendEvent("TIMESKIP"); }
                    Log.Info("autotest : evenements : jour " + (day != null ? day.Value : -1) + ", " + Config.GetInt("Test", "TestHeure", 20) + " h");
                }
                if ((t > 45f && step <= 1 && !Net.Session.IsHost) || (t > 45f && step == 1 && Net.Session.IsHost) || (t > 60f && step == 2))
                {
                    step = step <= 1 ? 2 : 3;
                    GameObject party = Game.FindAny("JOBS/JOKKEHOME/HouseDrunkNew/LOD1/DrunkParty");
                    Log.Info("autotest : evenements " + Events.Describe("JOKKEHOME") + " ; fete " + (party != null ? party.activeSelf.ToString() : "?") + ", heure " + FsmVariables.GlobalVariables.FindFsmInt("GlobalHour").Value + ", jour " + FsmVariables.GlobalVariables.FindFsmInt("GlobalDay").Value);
                }
            }
            // [Test] Autotest=rivett (retour d'un joueur, 08/10 : sur la Rivett, coffre qui tombe, voiture en orbite en claquant
            // une portiere, contact du joueur qui la casse, moteur coupe chez l'autre). Sauvegarde avec la CORRIS montee.
            // TestScenario : hote (l'hote conduit, l'invite manie), invite (l'inverse), garee (personne au volant, l'invite
            // manie). Conducteur : au volant a 15-22 s, moteur (1800 tr/min, sons, annonce en marche) de 24 a 90 s.
            // L'autre, par les automates 'Use' du jeu (comme ses clics) : portiere gauche ouverte 36 s, lachee 37, claquee
            // 40 ; droite 44 / 45 / 48 ; coffre ouvert 52, rabattu ("Drop") 60 ; capot 66 / 74 ; colle contre la portiere
            // gauche et pousse de 78 a 82 s ; court dans l'avant a 84-86 s. Les deux : chaque seconde, la voiture (vitesse,
            // rotation) et chaque ouvrant (angle, etat, attaches, tombe ou non).
            if (mode == "rivett")
            {
                string car = Config.Get("Test", "TestVoiture", "CORRIS");
                string scen = Config.Get("Test", "TestScenario", "hote");
                bool host = Net.Session.IsHost;
                // sortie (ruller, 19/10) : l'invite conduit, roule 20 s, s'arrete et SORT ([Test] RivettSortie=<s>, 50 par defaut) ;
                // l'hote regarde seulement, de loin : la voiture rendue a l'hote partait a 3952 m/s 1,5 s apres
                bool driver = scen == "hote" ? host : scen == "invite" || scen == "sortie" ? !host : false;
                bool actor = scen == "garee" ? !host : scen == "sortie" ? false : !driver;
                Rigidbody b = VehicleSync.Body(car);
                if (b != null && rivParts == null)
                {
                    rivParts = new System.Collections.Generic.List<Rigidbody>();
                    foreach (Rigidbody rb in b.GetComponentsInChildren<Rigidbody>(true))
                    {
                        string n = rb.name.ToLowerInvariant();
                        if (rb != b && (n.Contains("door") || n.Contains("bootlid") || n.Contains("hood"))) rivParts.Add(rb);
                    }
                    Log.Info("autotest : rivett (" + scen + ", " + (driver ? "conducteur" : actor ? "manie" : "regarde") + ") : " + rivParts.Count + " ouvrants ; suivis : " + CarDoors.ListFor(car) + " ; SORBET : " + CarDoors.ListFor("SORBET"));
                }
                // sortie, hote : le joueur de l'hote pose loin de la voiture a 12 s ([Test] RivettLieu=x,y,z ; 2 km, comme chez ruller :
                // les pieces de la voiture dorment chez l'hote, loin de son joueur)
                if (scen == "sortie" && host && t > 12f && rivMoved == 0)
                {
                    rivMoved = 1;
                    string[] xyz = Config.Get("Test", "RivettLieu", "1935.5,5.2,-422.4").Split(',');
                    var ci = System.Globalization.CultureInfo.InvariantCulture;
                    Vector3 to = new Vector3(float.Parse(xyz[0], ci), float.Parse(xyz[1], ci), float.Parse(xyz[2], ci));
                    GameObject pl = Game.PlayerGo; var cc = pl != null ? pl.GetComponent<CharacterController>() : null;
                    if (cc != null) { cc.enabled = false; pl.transform.position = to; cc.enabled = true; Log.Info("autotest : rivett : joueur de l'hote pose en " + to.ToString("F1") + (b != null ? ", a " + Vector3.Distance(to, b.position).ToString("F0") + " m de " + car : "")); }
                }
                if (driver)
                {
                    float td = t - (scen == "sortie" ? Config.GetFloat("Test", "RivettDecale", 20f) : 0f);   // (invite : tout apres son chargement)
                    if (td > 15f && step == 0) { step = 1; Log.Info("autotest : " + VehicleSync.TestEnter(car, false)); }
                    // (zone de conduite : le joueur teleporte dedans n'y est vu qu'en bougeant -- reessaye chaque seconde, repose a cote)
                    if (scen == "sortie" && td > 18f && step == 1 && rivDoor == 0) { rivDoor = 1; Log.Info("autotest : rivett : " + RivSet(car, "DoorLeft", "Open door") + " (portiere ouverte pour monter)"); }
                    // (les automates des pieces de la voiture ne s'activent qu'a l'approche du joueur : le siege conducteur se dit
                    // « pas monte » (Data Installed) pendant ~30 s apres la teleportation, et la zone refuse -- reessaye chaque seconde)
                    if (td > 22f && step == 1 && td >= rivTry)
                    {
                        rivTry = td + 1f;
                        string r = VehicleSync.TestEnter(car, true);
                        if (r != "Wait for player" || (int)td % 10 == 0) Log.Info("autotest : volant -> " + r + (r == "Wait for player" ? " ; " + VehicleSync.TestDriveZone(car) : ""));
                        if (r != "Wait for player") step = 2;
                    }
                    if (td > 24f && step == 2) { step = 3; Log.Info("autotest : moteur " + VehicleSync.TestSounds(car, true)); }
                    float outAt = Config.GetFloat("Test", "RivettSortie", 28f + Config.GetFloat("Test", "RivettRoule", 60f) + 4f);
                    VehicleSync.TestEngine(td > 24f && td < (scen == "sortie" ? outAt - 1f : 90f) ? 1800f : -1f, 0.3f);
                    // (roule [Test] RivettRoule s (60) a RivettVitesse m/s (12) le long de la route du jeu RivettRoute (DirtRoad) : vitesse
                    // forcee vers le prochain point, caisse tournee vers lui -- la copie chez l'hote bouge comme pour un vrai trajet)
                    float roule = Config.GetFloat("Test", "RivettRoule", 60f);
                    if (scen == "sortie" && b != null && td > 28f && td < 28f + roule && !b.isKinematic && VehicleSync.LocalDrivingRoot == b.transform)
                    {
                        if (rivRoute == null)
                        {
                            rivRoute = Kartturi.GameRoute(Config.Get("Test", "RivettRoute", "DirtRoad")) ?? new System.Collections.Generic.List<Vector3>();
                            float best = float.MaxValue; rivWp = 0;
                            for (int i = 0; i < rivRoute.Count; i++) { float d = Vector3.Distance(rivRoute[i], b.position); if (d < best) { best = d; rivWp = i; } }
                            Log.Info("autotest : rivett : route " + rivRoute.Count + " points, le plus proche a " + (rivRoute.Count > 0 ? best.ToString("F0") : "?") + " m (" + rivWp + ")");
                        }
                        if (rivRoute.Count > 1)
                        {
                            Vector3 tgt = rivRoute[rivWp % rivRoute.Count]; Vector3 d = tgt - b.position; d.y = 0f;
                            if (d.magnitude < 8f) { rivWp++; tgt = rivRoute[rivWp % rivRoute.Count]; d = tgt - b.position; d.y = 0f; }
                            if (d.sqrMagnitude > 0.01f)
                            {
                                float sp = Config.GetFloat("Test", "RivettVitesse", 12f);
                                b.velocity = d.normalized * sp + Vector3.up * Mathf.Clamp(b.velocity.y, -3f, 1f);
                                b.rotation = Quaternion.Slerp(b.rotation, Quaternion.LookRotation(d.normalized, Vector3.up), 0.15f);
                            }
                        }
                    }
                    if (scen == "sortie" && b != null && td >= 28f + roule && td < 31f + roule && !b.isKinematic && VehicleSync.LocalDrivingRoot == b.transform) b.velocity = Vector3.zero;   // (arret 3 s avant de sortir)
                    if (scen == "sortie" && td > outAt && step == 3) { step = 4; Log.Info("autotest : rivett : sortie -> " + VehicleSync.TestExit(car)); }
               }
                if (actor)
                {
                    if (scen != "garee" && t > 30f && step == 0) { step = 1; Log.Info("autotest : rivett : moteur entendu " + VehicleSync.AudioState(car) + " ; " + VehicleSync.StarterState(car)); }
                    while (rivStep < RivAt.Length && t > RivAt[rivStep])
                    {
                        Log.Info("autotest : rivett : " + RivSet(car, RivPart[rivStep], RivState[rivStep]));
                        rivStep++;
                    }
                    GameObject pl = Game.PlayerGo;
                    var cc = pl != null ? pl.GetComponent<CharacterController>() : null;
                    if (cc != null && b != null && t > 78f && t < 82f)
                    {
                        if (rivPush == 0) { rivPush = 1; cc.enabled = false; pl.transform.position = b.transform.TransformPoint(new Vector3(-1.6f, 0.2f, 0f)); cc.enabled = true; Log.Info("autotest : rivett : contre la portiere gauche"); }
                        cc.Move(b.transform.right * 2.5f * Time.deltaTime);
                    }
                    if (cc != null && b != null && t > 84f && t < 86f)
                    {
                        if (rivPush == 1) { rivPush = 2; cc.enabled = false; pl.transform.position = b.transform.TransformPoint(new Vector3(0f, 0.2f, 5f)); cc.enabled = true; Log.Info("autotest : rivett : court dans l'avant"); }
                        cc.Move(-b.transform.forward * 6f * Time.deltaTime);
                    }
                }
                if (b != null && t > 20f && Time.realtimeSinceStartup >= rivLog)
                {
                    rivLog = Time.realtimeSinceStartup + 1f;
                    var sb = new System.Text.StringBuilder("autotest : rivett " + (b.isKinematic ? "copie" : "locale") + " v " + b.velocity.magnitude.ToString("F1") + " rot " + b.angularVelocity.magnitude.ToString("F2") + " en " + b.position.ToString("F1") + " ;");
                    if (rivParts != null)
                        foreach (Rigidbody rb in rivParts)
                        {
                            if (rb == null) { sb.Append(" (detruite);"); continue; }
                            bool under = rb.transform.IsChildOf(b.transform);
                            string pn = rb.transform.parent != null ? rb.transform.parent.name.Replace("VINP_", "") : rb.name;
                            PlayMakerFSM use = null;
                            foreach (PlayMakerFSM f in rb.GetComponents<PlayMakerFSM>()) if (f.FsmName == "Use") use = f;
                            sb.Append(' ').Append(pn).Append(under ? "" : " TOMBEE " + (rb.position - b.position).magnitude.ToString("F1") + "m")
                              .Append(' ').Append(Quaternion.Angle(rb.transform.localRotation, Quaternion.identity).ToString("F0")).Append("deg")
                              .Append(use != null ? " [" + use.ActiveStateName + "]" : "").Append(" j").Append(rb.GetComponents<Joint>().Length)
                              .Append(rb.isKinematic ? " cin" : "").Append(rb.velocity.magnitude > 2f ? " v" + rb.velocity.magnitude.ToString("F0") : "").Append(';');
                        }
                    Log.Info(sb.ToString());
                }
            }
            // [Test] Autotest=emballe : garde-fou de la physique emballee (CORRIS de ruller, 19/10) -- joueur pose a 6 m de
            // [Test] TestVoiture (CORRIS), lancee a 300 m/s vers le haut (35 s) puis a 80 rad/s (45 s) ; pose relevee chaque
            // seconde, captures avant et apres.
            if (mode == "emballe")
            {
                Rigidbody b = VehicleSync.Body(Config.Get("Test", "TestVoiture", "CORRIS"));
                GameObject pl = Game.PlayerGo;
                var cc = pl != null ? pl.GetComponent<CharacterController>() : null;
                if (b != null && cc != null && t > 25f && step == 0)
                {
                    step = 1;
                    Vector3 at = b.transform.TransformPoint(new Vector3(-6f, 0f, 0f)) + Vector3.up * 1f;
                    cc.enabled = false; pl.transform.position = at; cc.enabled = true;
                    Vector3 d = b.position - at; d.y = 0f;
                    if (d.sqrMagnitude > 0.01f) pl.transform.rotation = Quaternion.LookRotation(d);
                    Log.Info("autotest : emballe : joueur pose a 6 m de " + b.name + " en " + b.position.ToString("F1"));
                }
                if (b != null && t > 33f && step == 1) { step = 2; CaptureSoon("emballe-avant", 0f); }
                if (b != null && t > 35f && step == 2) { step = 3; b.velocity = new Vector3(0f, 300f, 0f); Log.Info("autotest : emballe : " + b.name + " lancee a 300 m/s"); CaptureSoon("emballe-lancee", 0.3f); }
                if (b != null && t > 45f && step == 3) { step = 4; b.angularVelocity = new Vector3(80f, 0f, 30f); Log.Info("autotest : emballe : " + b.name + " lancee a 85 rad/s"); }
                if (b != null && t > 50f && step == 4) { step = 5; CaptureSoon("emballe-apres", 0f); }
                if (b != null && t > 30f && t < 55f && Time.realtimeSinceStartup >= rivLog)
                {
                    rivLog = Time.realtimeSinceStartup + 1f;
                    Log.Info("autotest : emballe : " + b.name + " v " + b.velocity.magnitude.ToString("F1") + " rot " + b.angularVelocity.magnitude.ToString("F1") + " en " + b.position.ToString("F1") + (b.isKinematic ? " (cinematique)" : ""));
                }
            }
            // [Test] Autotest=klaxon : objets "horn" de la scene (chemin, automates et leurs etats, sources audio).
            if (mode == "klaxon" && t > 25f && !done)
            {
                done = true;
                var sb = new System.Text.StringBuilder("autotest : klaxons :");
                foreach (Object o in Resources.FindObjectsOfTypeAll(typeof(Transform)))
                {
                    var tr = (Transform)o;
                    if (tr.hideFlags != HideFlags.None || tr.name.ToLowerInvariant().IndexOf("horn") < 0) continue;
                    sb.Append("\n  ").Append(Recon.Path(tr)).Append(tr.gameObject.activeInHierarchy ? "" : " [off]");
                    foreach (PlayMakerFSM f in tr.GetComponents<PlayMakerFSM>())
                    {
                        sb.Append("\n    FSM ").Append(f.FsmName).Append(" etat=").Append(f.ActiveStateName).Append(" :");
                        try { foreach (HutongGames.PlayMaker.FsmState st in f.FsmStates)
                        {
                            sb.Append(" [").Append(st.Name);
                            foreach (HutongGames.PlayMaker.FsmTransition x in st.Transitions) sb.Append(' ').Append(x.EventName).Append("->").Append(x.ToState);
                            sb.Append(" |");
                            foreach (HutongGames.PlayMaker.FsmStateAction ac in st.Actions) sb.Append(' ').Append(ac.GetType().Name);
                            sb.Append(']');
                        } } catch (System.Exception) { sb.Append(' ').Append('?'); }
                    }
                    foreach (AudioSource au in tr.GetComponents<AudioSource>()) sb.Append("\n    audio ").Append(au.clip != null ? au.clip.name : "-").Append(au.loop ? " boucle" : "").Append(" 3D ").Append(au.spatialBlend.ToString("F1"));
                }
                Log.Info(sb.ToString());
            }
            // [Test] Autotest=banquette : banquette rabattable de la SORBET suivie pareil chez les deux (CarDoors), l'invite la
            // rabat a 30 s et la releve a 45 s ; chacun note son etat a 25, 40 et 55 s.
            if (mode == "banquette")
            {
                string car = Config.Get("Test", "TestVoiture", "SORBET(190-200psi)");
                if (t > 25f && step == 0) { step = 1; Log.Info("autotest : banquette, suivis : " + CarDoors.ListFor(car) + " ; " + CarDoors.StateOf(car, "RearSeat")); }
                if (!Net.Session.IsHost && t > 30f && step == 1) { step = 2; Log.Info("autotest : banquette, rabat " + CarDoors.TestOpen(car, true, "RearSeat")); }
                if (t > 40f && step <= 2 && !done) { done = true; Log.Info("autotest : banquette a 40 s : " + CarDoors.StateOf(car, "RearSeat")); }
                if (!Net.Session.IsHost && t > 45f && step == 2) { step = 3; Log.Info("autotest : banquette, releve " + CarDoors.TestGrab(car, "RearSeat")); }
                if (t > 55f && !watchLogged) { watchLogged = true; Log.Info("autotest : banquette a 55 s : " + CarDoors.StateOf(car, "RearSeat")); }
            }
            // [Test] Autotest=pousse : l'invite, contre la SORBET, la pousse (main forcee, 1,2 m/s vers l'avant) de 30 a 36 s ;
            // chacun note la position de la voiture chaque seconde de 25 a 50 s (attendu : la meme, sans retour en arriere).
            // [Test] Autotest=poussevolant : l'hote au volant de TestVoiture (SORBET) a 20 s ; l'invite derriere, tourne vers elle,
            // pousse (geste du jeu) de 32 a 42 s. Chacun note la voiture chaque seconde de 25 a 50 s. Attendu : elle avance
            // chez l'hote (la poussee lui est envoyee), et chez l'invite (copie).
            if (mode == "poussevolant")
            {
                string car = Config.Get("Test", "TestVoiture", "SORBET(190-200psi)");
                Rigidbody b = VehicleSync.Body(car);
                if (Net.Session.IsHost)
                {
                    if (t > 18f && step == 0) { step = 1; Log.Info("autotest : poussevolant " + VehicleSync.TestEnter(car, false)); }
                    if (t > 22f && step == 1) { step = 2; Log.Info("autotest : poussevolant volant -> " + VehicleSync.TestEnter(car, true)); }
                }
                else if (b != null)
                {
                    if (t > 30f && step == 0)
                    {
                        step = 1;
                        GameObject pl = Game.PlayerGo;
                        var cc = pl != null ? pl.GetComponent<CharacterController>() : null;
                        if (cc != null)
                        {
                            cc.enabled = false;
                            pl.transform.position = b.transform.TransformPoint(new Vector3(0f, 0f, -2.6f));
                            Vector3 f = b.position - pl.transform.position; f.y = 0;
                            pl.transform.rotation = Quaternion.LookRotation(f);
                            cc.enabled = true;
                        }
                        Log.Info("autotest : poussevolant, derriere la voiture");
                    }
                    Gestures.TestPush = t > 32f && t < 42f;
                    // Mains sorties sans marcher de 32 a 35 s (rien ne doit partir), puis en marchant vers la voiture.
                    if (t > 35f && t < 42f)
                    {
                        GameObject pl = Game.PlayerGo;
                        var cc = pl != null ? pl.GetComponent<CharacterController>() : null;
                        if (cc != null) { Vector3 f = b.position - pl.transform.position; f.y = 0f; cc.Move(f.normalized * 1.2f * Time.deltaTime); }
                    }
                }
                if (b != null && t > 25f && t < 50f && Time.realtimeSinceStartup >= rivLog)
                {
                    rivLog = Time.realtimeSinceStartup + 1f;
                    Log.Info("autotest : poussevolant, voiture " + (b.isKinematic ? "copie" : "locale") + " en " + b.position.ToString("F2") + " v " + b.velocity.magnitude.ToString("F2"));
                }
            }
            if (mode == "pousse")
            {
                string car = Config.Get("Test", "TestVoiture", "SORBET(190-200psi)");
                Rigidbody b = VehicleSync.Body(car);
                if (!Net.Session.IsHost && b != null)
                {
                    if (t > 28f && step == 0)
                    {
                        step = 1;
                        GameObject pl = Game.PlayerGo;
                        var cc = pl != null ? pl.GetComponent<CharacterController>() : null;
                        if (cc != null) { cc.enabled = false; pl.transform.position = b.transform.TransformPoint(new Vector3(0f, 0f, -3.2f)); cc.enabled = true; }
                        Log.Info("autotest : pousse, derriere la voiture");
                    }
                    Gestures.TestPush = t > 30f && t < 36f;
                    if (t > 30f && t < 36f && !b.isKinematic) { Vector3 f = b.transform.forward; f.y = 0; b.velocity = f.normalized * 1.2f + Vector3.up * Mathf.Min(b.velocity.y, 0f); }
                }
                if (b != null && t > 25f && t < 50f && Time.realtimeSinceStartup >= rivLog)
                {
                    rivLog = Time.realtimeSinceStartup + 1f;
                    Log.Info("autotest : pousse, voiture " + (b.isKinematic ? "copie" : "locale") + " en " + b.position.ToString("F2") + " v " + b.velocity.magnitude.ToString("F2"));
                }
            }
            // [Test] Autotest=sondevolant : champs du volant (SteeringWheel) et du controleur de la voiture TestVoiture.
            if (mode == "sondevolant" && t > 25f && !done)
            {
                done = true;
                Rigidbody b = VehicleSync.Body(Config.Get("Test", "TestVoiture", "SORBET(190-200psi)"));
                var sb = new System.Text.StringBuilder("autotest : sonde volant :");
                if (b != null)
                    foreach (MonoBehaviour m in b.GetComponentsInChildren<MonoBehaviour>(true))
                    {
                        if (m == null) continue;
                        string tn = m.GetType().Name;
                        if (tn != "SteeringWheel" && tn != "AxisCarController" && tn != "CarController") continue;
                        sb.Append("\n  ").Append(tn).Append(" sur ").Append(Recon.Path(m.transform)).Append(" :");
                        for (System.Type ty = m.GetType(); ty != null && ty != typeof(MonoBehaviour); ty = ty.BaseType)
                            foreach (System.Reflection.FieldInfo fi in ty.GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly))
                            {
                                object v = null; try { v = fi.GetValue(m); } catch { }
                                sb.Append(' ').Append(ty.Name).Append('.').Append(fi.Name).Append('=').Append(v is float ? ((float)v).ToString("F2") : v is UnityEngine.Object ? ((UnityEngine.Object)v).name : v != null ? v.ToString() : "null");
                            }
                    }
                Log.Info(sb.ToString());
            }
            // [Test] Autotest=attelage : l'hote au volant du KEKMET (15-22 s), la FLATBED posee 4,5 m derriere et reliee par
            // un joint fixe a 25 s (comme un attelage), le tracteur avance a 3 m/s de 28 a 40 s ; chacun note chaque seconde
            // de 24 a 60 s la remorque (position, distance au tracteur, copie ou locale).
            if (mode == "attelage")
            {
                if (t < 1f) { attTry = 0f; attExitAt = 1e9f; attVoisin = 0; attAir = 0; }
                Rigidbody k = VehicleSync.Body("KEKMET"), fb = VehicleSync.Body("FLATBED");
                if (Net.Session.IsHost && k != null && fb != null)
                {
                    if (t > 15f && step == 0) { step = 1; Log.Info("autotest : " + VehicleSync.TestEnter("KEKMET", false)); }
                    if (t > 22f && step == 1) { step = 2; Log.Info("autotest : volant -> " + VehicleSync.TestEnter("KEKMET", true)); }
                    if (t > 25f && step == 2)
                    {
                        step = 3;
                        fb.transform.position = k.transform.TransformPoint(new Vector3(0f, 0.3f, Config.GetInt("Test", "AttelageVoisin", 0) != 0 ? -6.5f : -4.5f));
                        fb.transform.rotation = k.transform.rotation;
                        fb.velocity = Vector3.zero; fb.angularVelocity = Vector3.zero;
                        var fj = fb.gameObject.AddComponent<FixedJoint>();
                        fj.connectedBody = k;
                        Log.Info("autotest : attelage, FLATBED reliee au KEKMET");
                    }
                    if (t > 28f && t < (Config.GetInt("Test", "AttelageVoisin", 0) != 0 ? 58f : 40f)) { Vector3 f = k.transform.forward; f.y = 0; k.velocity = f.normalized * 3f + Vector3.up * Mathf.Min(k.velocity.y, 0f); }
                    // [Test] AttelageSortie=1 : moteur tenu en marche, l'hote descend a 42 s (moteur laisse tournant) : la remorque
                    // doit lui rester (pas « rendue ») et suivre chez l'invite.
                    if (Config.GetInt("Test", "AttelageSortie", 0) != 0)
                    {
                        // (le jeu ne laisse pas descendre moteur force en marche : sortie moteur arrete, puis la main reprise
                        // comme un moteur laisse tournant, de 45 a 58 s)
                        if (step == 9 && t < attExitAt + 12f) { VehicleSync.TestKeepRunning("KEKMET"); VehicleSync.TestOwn("KEKMET"); }
                        if (step == 9 && t >= attExitAt + 12f) { step = 10; Log.Info("autotest : attelage, moteur relache"); }
                        if (t > 42f && step >= 3 && step < 9 && t > attTry)
                        {
                            attTry = t + 1f;
                            string st = VehicleSync.TestExit("KEKMET");
                            if (st != "Player in car") { step = 9; attExitAt = t + 1f; Log.Info("autotest : attelage, sorti du tracteur -> " + st); }
                        }
                    }
                }
                // [Test] AttelageVoisin=1 : l'invite se pose a pied a 1,2 m de la remorque a 26 s (elle bouge a cote de lui des
                // 28 s : il ne doit pas la prendre). A 32 s l'hote, au volant du KEKMET, « appuie » sur Range pour la boite de la
                // GIFU (comme la touche) : rien ne doit partir ; l'invite note la pression d'air de la GIFU a 15 et 45 s (ses temps).
                if (Config.GetInt("Test", "AttelageVoisin", 0) != 0)
                {
                    if (!Net.Session.IsHost && fb != null && t > 26f && attVoisin == 0)
                    {
                        attVoisin = 1;
                        GameObject pl = Game.PlayerGo;
                        var cc = pl != null ? pl.GetComponent<CharacterController>() : null;
                        if (cc != null) cc.enabled = false;
                        if (pl != null) pl.transform.position = fb.position + fb.transform.right * 1.8f + Vector3.up * 0.5f;
                        if (cc != null) cc.enabled = true;
                        if (fb.GetComponent<FixedJoint>() == null && k != null) fb.gameObject.AddComponent<FixedJoint>().connectedBody = k;   // (l'attelage du jeu est rejoue ici aussi)
                        Log.Info("autotest : attelage, invite a pied a cote de la remorque");
                    }
                    Rigidbody gifu = VehicleSync.Body("GIFU(750/450psi)");
                    if (Net.Session.IsHost && gifu != null && t > 32f && attVoisin == 0)
                    {
                        attVoisin = 1;
                        Transform tr = gifu.transform.Find("Simulation/Transmission");
                        PlayMakerFSM ch = tr != null ? Game.FsmOn(tr.gameObject, "Change") : null;
                        if (ch != null) { Game.SetState(ch, "Button"); ch.SendEvent("GEAR"); }
                        Log.Info("autotest : attelage, Range de la GIFU appuye ici -> " + (ch != null ? ch.ActiveStateName : "?"));
                    }
                    if (!Net.Session.IsHost && gifu != null && (t > 15f && attAir == 0 || t > 45f && attAir == 1))
                    {
                        attAir++;
                        Transform ab = gifu.transform.Find("Simulation/Airbrakes");
                        PlayMakerFSM ap = ab != null ? Game.FsmOn(ab.gameObject, "Air Pressure") : null;
                        HutongGames.PlayMaker.FsmFloat pr = ap != null ? ap.FsmVariables.FindFsmFloat("Pressure") : null;
                        Log.Info("autotest : attelage, air de la GIFU ici " + (pr != null ? pr.Value.ToString("F1") : "?"));
                    }
                }
                if (k != null && fb != null && t > 24f && t < 60f && Time.realtimeSinceStartup >= rivLog)
                {
                    rivLog = Time.realtimeSinceStartup + 1f;
                    Log.Info("autotest : attelage, remorque " + (fb.isKinematic ? "copie" : "locale") + " en " + fb.position.ToString("F1") + ", a " + (fb.position - k.position).magnitude.ToString("F1") + " m du tracteur " + (k.isKinematic ? "(copie)" : "(local)") + " en " + k.position.ToString("F1"));
                }
            }
            // [Test] Autotest=fendeuse (retour d'un joueur, 10/10 : « attelee par un invite, la fendeuse s'envole ») : l'invite
            // au volant du KEKMET (15-22 s) ; a 25 s chacun pose la fendeuse (Cutter) 2,6 m derriere le tracteur et l'y relie
            // (joint fixe, comme le relevage) ; l'invite roule a 4 m/s de 28 a 40 s ; chacun note toutes les 0,5 s la pose de
            // la fendeuse dans le repere du tracteur, et son etendue.
            if (mode == "fendeuse")
            {
                Rigidbody k = VehicleSync.Body("KEKMET");
                GameObject cg = Game.FindAny("Cutter");
                Rigidbody cb = cg != null ? (cg.GetComponent<Rigidbody>() ?? cg.GetComponentInChildren<Rigidbody>()) : null;
                if (!Net.Session.IsHost && k != null)
                {
                    if (t > 15f && step == 0) { step = 1; Log.Info("autotest : " + VehicleSync.TestEnter("KEKMET", false)); }
                    if (t > 22f && step == 1) { step = 2; Log.Info("autotest : volant -> " + VehicleSync.TestEnter("KEKMET", true)); }
                    if (t > 28f && t < 40f) { Vector3 f = k.transform.forward; f.y = 0; k.velocity = f.normalized * 4f + Vector3.up * Mathf.Min(k.velocity.y, 0f); }
                }
                if (k != null && cb != null && t > 25f && !teleported)
                {
                    teleported = true;
                    cb.transform.position = k.transform.TransformPoint(new Vector3(0f, 0.6f, -2.6f));
                    cb.transform.rotation = k.transform.rotation;
                    cb.velocity = Vector3.zero; cb.angularVelocity = Vector3.zero;
                    var fj = cb.gameObject.AddComponent<FixedJoint>();
                    fj.connectedBody = k;
                    Log.Info("autotest : fendeuse " + cb.name + " reliee au KEKMET");
                }
                if (k != null && cb != null && teleported && t < 50f && Time.realtimeSinceStartup >= rivLog)
                {
                    rivLog = Time.realtimeSinceStartup + 0.5f;
                    Vector3 lp = k.transform.InverseTransformPoint(cb.position);
                    if (!cabAv) { cabAv = true; cabMin = cabMax = lp; } else { cabMin = Vector3.Min(cabMin, lp); cabMax = Vector3.Max(cabMax, lp); }
                    Log.Info("autotest : fendeuse dans le repere du tracteur " + lp.ToString("F2") + " (" + (cb.isKinematic ? "cinematique" : "physique") + "), tracteur " + (k.isKinematic ? "copie" : "local") + " a " + k.velocity.magnitude.ToString("F1") + " m/s ; etendue " + (cabMax - cabMin).ToString("F2"));
                }
            }
            // [Test] Autotest=taxiclient : taxi active des deux cotes (8 s) ; l'invite au volant (15-22 s) ; l'hote lance une
            // course (NEWJOB a 26 s, l'appel decroche : SUCCESS des que Logic est sur 'Call'). L'invite amene le taxi a
            // 2,5 m de SON client quand celui-ci attend (Distance 1/2), puis roule a 5 m/s de 75 a 85 s. Chacun note toutes
            // les 2 s de 26 a 100 s : etat de Logic, lieu de prise en charge, client (marcheur, corps, dans le repere de
            // sa copie du taxi), bagages actifs et le suivi du PNJ (Npcs).
            if (mode == "taxiclient")
            {
                bool host = Net.Session.IsHost;
                if (tcWalker == null) { tcWalker = Game.FindAny("JOBS/TAXIJOB/Customer1/TaxiWalker"); tcCust = Game.FindAny("JOBS/TAXIJOB/Customer1"); }
                GameObject walker = tcWalker;
                PlayMakerFSM logic = walker != null ? Game.FsmOn(walker, "Logic") : null;
                Rigidbody tx = VehicleSync.Body("MACHTWAGEN");
                if (t > 8f && step == 0)
                {
                    step = 1;
                    GameObject taxi = Game.FindAny("JOBS/TAXIJOB/MACHTWAGEN");
                    if (taxi != null && !taxi.activeSelf) taxi.SetActive(true);
                    Log.Info("autotest : taxiclient, taxi " + (taxi == null ? "introuvable" : "actif") + ", client " + (logic == null ? "sans Logic" : logic.ActiveStateName));
                }
                if (t > 24f && !tcOn && walker != null)
                {
                    tcOn = true;
                    for (Transform x = walker.transform; x != null && x.name != "JOBS"; x = x.parent) x.gameObject.SetActive(true);
                    Log.Info("autotest : taxiclient, client active, Logic " + (logic != null ? logic.ActiveStateName : "?"));
                }
                if (!host && t > 15f && step == 1) { step = 2; Log.Info("autotest : taxiclient " + VehicleSync.TestEnter("MACHTWAGEN", false)); }
                if (!host && t > 22f && step == 2) { step = 3; Log.Info("autotest : taxiclient volant -> " + VehicleSync.TestEnter("MACHTWAGEN", true)); }
                if (host && t > 70f && step == 1 && logic != null) { step = 2; logic.SendEvent("NEWJOB"); Log.Info("autotest : taxiclient NEWJOB -> " + logic.ActiveStateName); }
                if (host && step == 2 && logic != null && logic.ActiveStateName == "Call") { step = 3; logic.SendEvent("SUCCESS"); Log.Info("autotest : taxiclient appel decroche -> " + logic.ActiveStateName); }
                if (!host && step == 3 && t > 30f && logic != null && tx != null && (logic.ActiveStateName == "Distance 1" || logic.ActiveStateName == "Distance 2"))
                {
                    step = 4;
                    Vector3 side = walker.transform.right; side.y = 0;
                    tx.transform.position = walker.transform.position + side.normalized * 2.5f + Vector3.up * 0.6f;
                    tx.transform.rotation = Quaternion.LookRotation(Vector3.Cross(side.normalized, Vector3.up));
                    tx.velocity = Vector3.zero; tx.angularVelocity = Vector3.zero;
                    tcAt = t;
                    Log.Info("autotest : taxiclient, taxi amene pres du client en " + tx.position.ToString("F1"));
                }
                if (!host && tx != null && step == 4 && t < tcAt + 3f) { tx.velocity = Vector3.zero; tx.angularVelocity = Vector3.zero; }
                if (!host && step == 4 && logic != null && (logic.ActiveStateName == "Randomize anim" || logic.ActiveStateName == "State 6" || logic.ActiveStateName == "State 7" || t > tcAt + 70f))
                { step = 5; tcAt = t; Log.Info("autotest : taxiclient, depart (client " + logic.ActiveStateName + ")"); }
                if (!host && tx != null && step == 5 && t < tcAt + 10f) { Vector3 f = tx.transform.forward; f.y = 0; tx.velocity = f.normalized * 5f + Vector3.up * Mathf.Min(tx.velocity.y, 0f); }
                if (t > 26f && Time.realtimeSinceStartup >= rivLog && logic != null)
                {
                    rivLog = Time.realtimeSinceStartup + 2f;
                    PlayMakerFSM suit = Game.FsmOn(walker, "Suitcases");
                    var sb = new System.Text.StringBuilder("autotest : taxiclient Logic=" + logic.ActiveStateName + " Suitcases=" + (suit != null ? suit.ActiveStateName : "?"));
                    GameObject pp = logic.FsmVariables.GetFsmGameObject("PickupPoint").Value;
                    sb.Append(", prise ").Append(pp != null ? pp.name : "null").Append(", ID ").Append(logic.FsmVariables.GetFsmString("ID").Value);
                    sb.Append(", marcheur en ").Append(walker.transform.position.ToString("F1")).Append(" sous ").Append(walker.transform.parent != null ? walker.transform.parent.name : "-");
                    Transform ch = walker.transform.Find("Char");
                    if (ch != null && tx != null) sb.Append(", corps dans le taxi ").Append(tx.transform.InverseTransformPoint(ch.position).ToString("F2")).Append(tx.isKinematic ? " (copie)" : " (local)");
                    sb.Append(", bagages");
                    if (tcLug == null && tcCust != null)
                    {
                        tcLug = new System.Collections.Generic.List<Transform>();
                        foreach (Transform x in tcCust.GetComponentsInChildren<Transform>(true)) if (x.name.EndsWith("(lugga)")) tcLug.Add(x);
                    }
                    if (tcLug != null)
                        foreach (Transform x in tcLug)
                            if (x != null && x.gameObject.activeInHierarchy)
                                sb.Append(' ').Append(x.name).Append('@').Append(x.parent != null ? x.parent.name : "-").Append(tx != null ? tx.transform.InverseTransformPoint(x.position).ToString("F1") : "");
                    Log.Info(sb.ToString());
                    Log.Info("autotest : taxiclient PNJ " + Npcs.State("TAXIJOB/Customer1/"));
                }
            }
            if (mode == "sondegfx" && t > 20f && !done)
            {
                done = true;
                var sb = new System.Text.StringBuilder("autotest : graphismes : qualite " + QualitySettings.GetQualityLevel() + "/" + string.Join(",", QualitySettings.names));
                sb.Append(", ombres ").Append(QualitySettings.shadowDistance).Append(" m cascades ").Append(QualitySettings.shadowCascades).Append(" projection ").Append(QualitySettings.shadowProjection);
                sb.Append(", lodBias ").Append(QualitySettings.lodBias).Append(" maxLOD ").Append(QualitySettings.maximumLODLevel).Append(", textures ").Append(QualitySettings.masterTextureLimit);
                sb.Append(", aniso ").Append(QualitySettings.anisotropicFiltering).Append(", lumieres ").Append(QualitySettings.pixelLightCount).Append(", msaa ").Append(QualitySettings.antiAliasing);
                sb.Append(", vsync ").Append(QualitySettings.vSyncCount).Append(", images max ").Append(Application.targetFrameRate).Append(", particules douces ").Append(QualitySettings.softVegetation);
                Camera c = Game.MainCam;
                if (c != null) sb.Append(", camera ").Append(c.name).Append(" ").Append(c.renderingPath).Append(" hdr ").Append(c.hdr).Append(" loin ").Append(c.farClipPlane).Append(" fov ").Append(c.fieldOfView);
                Terrain[] ts = Object.FindObjectsOfType<Terrain>();
                sb.Append(", terrains ").Append(ts.Length);
                foreach (Terrain tr in ts) sb.Append(" [").Append(tr.name).Append(" herbe ").Append(tr.detailObjectDistance).Append("/").Append(tr.detailObjectDensity).Append(" arbres ").Append(tr.treeDistance).Append(" erreur ").Append(tr.heightmapPixelError).Append("]");
                foreach (Light l in Object.FindObjectsOfType<Light>()) if (l.type == LightType.Directional) sb.Append(", lumiere ").Append(l.name).Append(" ombres ").Append(l.shadows).Append(" force ").Append(l.shadowStrength);
                Log.Info(sb.ToString());
            }
            if (mode == "cabine")
            {
                // Hote : au volant de [Test] TestVoiture (SORBET) a 15 s, ceinture bouclee a 24 s (variable du jeu), roule
                // de 27 a 40 s ; starter tire (1,8) a 30 s, frein a main relache (0) a 32 s. Invite : a 36 s et 44 s, ce qu'il
                // voit (valeurs des commandes tenues, ceinture du conducteur, mains au volant, portieres figees).
                string car = Config.Get("Test", "TestVoiture", "SORBET(190-200psi)");
                t -= Config.GetInt("Test", "CabineDecalage", 0);   // (voiture d'un mod : apparait plus tard)
                if (Net.Session.IsHost)
                {
                    if (t > 15f && step == 0) { step = 1; Log.Info("autotest : " + VehicleSync.TestEnter(car, false)); }
                    // (vehicule loin : son declencheur s'allume apres la 1re pose -- reposee a 19 s pour y entrer)
                    if (t > 19f && step == 1 && !cabRetp) { cabRetp = true; Log.Info("autotest : " + VehicleSync.TestEnter(car, false)); }
                    if (t > 22f && step == 1) { step = 2; Log.Info("autotest : volant -> " + VehicleSync.TestEnter(car, true)); }
                    if (t > 24f && step == 2) { step = 3; Game.SetGlobalBool("PlayerSeatbeltsOn", true); Log.Info("autotest : ceinture bouclee (variable)"); }
                    Rigidbody b = VehicleSync.Body(car);
                    VehicleSync.TestEngine(t > 26f && t < 41f ? 1800f : -1f, 0.4f);
                    if (b != null && t > 27f && t < 40f) { Vector3 f = b.transform.forward; f.y = 0; b.velocity = f.normalized * Config.GetFloat("Test", "CabineVitesse", 6f) + Vector3.up * Mathf.Min(b.velocity.y, 0f); }
                    // [Test] CabineTourne=deg/s : le vehicule tourne sur lui-meme pendant ce temps (siege qui glisse selon le cap ?)
                    float turn = Config.GetFloat("Test", "CabineTourne", 0f);
                    if (b != null && turn != 0f && t > 27f && t < 40f) b.transform.rotation = Quaternion.Euler(0f, turn * Time.deltaTime, 0f) * b.transform.rotation;
                    if (t > 30f && step == 3) { step = 4; Log.Info("autotest : starter " + Knobs.TestHold("Choke", 1.8f)); }
                    if (t > 32f && step == 4) { step = 5; Log.Info("autotest : frein a main " + Knobs.TestHold("HandBrake", 0f)); }
                    if (t > 36f && step == 5) { step = 6; Log.Info("autotest : commandes " + Knobs.Describe()); }
                    VehicleSync.TestHorn(t > 45f && t < 52f);
                    VehicleSync.TestSteer(t > 30f && t < 38f ? 0.6f : float.NaN);   // (braquage annonce : l'invite le voit vers 15-23 s)
                    if (t > 40f && step == 6) { step = 7; Log.Info("autotest : " + VehicleSync.TestGear(car, 2)); }
                    if (t > 42f && step == 7) { step = 8; Log.Info("autotest : " + VehicleSync.TestGear(car, 3)); }   // (klaxon annonce de 45 a 52 s : l'invite, ~15 s de retard, le voit de 30 a 37 s)
                }
                else
                {
                    // [Test] CabinePassager=1 : l'invite s'assoit a l'avant a 20 s ; de 28 a 40 s (voiture qui roule), ecart
                    // maximal entre la pose physique de la copie et sa pose affichee (camera du passager) -- 0 attendu
                    // depuis 0.54 (copie deplacee a chaque image), jusqu'a vitesse x 20 ms avant.
                    if (Config.GetInt("Test", "CabinePassager", 0) != 0)
                    {
                        Rigidbody b = VehicleSync.Body(car);
                        if (t > 20f && !teleported) { teleported = true; Log.Info("autotest : passager " + Seats.TestSit(car, 0)); }
                        if (b != null && t > 28f && t < 40f) cabGap = Mathf.Max(cabGap, (b.position - b.transform.position).magnitude);
                        // Avatar du conducteur dans le repere de la voiture : etendue de ses positions en roulant (sauts).
                        if (b != null && t > 12f && t < 26f)
                            foreach (Avatar a in PlayerSync.Avatars)
                                if (a.Root != null)
                                {
                                    Vector3 lp = b.transform.InverseTransformPoint(a.Root.transform.position);
                                    if (!cabAv) { cabAv = true; cabMin = cabMax = lp; } else { cabMin = Vector3.Min(cabMin, lp); cabMax = Vector3.Max(cabMax, lp); }
                                    if (Time.realtimeSinceStartup >= cabLogAt) { cabLogAt = Time.realtimeSinceStartup + 0.5f; Log.Info("autotest : passager, avatar " + lp.ToString("F2") + ", voiture v " + VehicleSync.RemoteSpeed(a.Player.Id).ToString("F1")); }
                                }
                        if (t > 17f && t < 26f && Time.realtimeSinceStartup >= cabWheelAt)
                        {
                            cabWheelAt = Time.realtimeSinceStartup + 1f;
                            string hands = ""; foreach (Avatar a in PlayerSync.Avatars) hands = a.HandsState();
                            Log.Info("autotest : passager, " + VehicleSync.WheelState(car) + " ; " + hands);
                        }
                        if (t > 24f && t < 30f && Time.realtimeSinceStartup >= cabGearAt)
                        {
                            cabGearAt = Time.realtimeSinceStartup + 0.1f;
                            string hands = ""; foreach (Avatar a in PlayerSync.Avatars) hands = a.HandsState();
                            Log.Info("autotest : passager, " + VehicleSync.GearState(car) + " ; " + hands);
                        }
                        if (t > 26f && !cabAvLogged) { cabAvLogged = true; Log.Info("autotest : passager, avatar du conducteur dans la voiture (en roulant) : etendue " + (cabMax - cabMin).ToString("F3") + " m, de " + cabMin.ToString("F2") + " a " + cabMax.ToString("F2")); }
                        if (t > 34f && !sleepWatch) { sleepWatch = true; Log.Info("autotest : passager, " + VehicleSync.HornState(car)); }
                        if (t > 41f && !foodMade) { foodMade = true; Log.Info("autotest : passager, apres : " + VehicleSync.HornState(car)); }
                        if (t > 40f && !watchLogged) { watchLogged = true; Log.Info("autotest : passager, ecart pose physique / affichee max " + (cabGap * 100f).ToString("F1") + " cm, assis " + Seats.Seated + ", voiture " + (b != null ? (b.isKinematic ? "copie " : "locale ") + b.interpolation : "?")); }
                    }
                }
                if (!Net.Session.IsHost && ((t > 36f && step == 0) || (t > 44f && step == 1)))
                {
                    step++;
                    Log.Info("autotest : commandes " + Knobs.Describe());
                    Log.Info("autotest : ceintures " + Seats.BeltState());
                    foreach (Avatar a in PlayerSync.Avatars) Log.Info("autotest : mains " + a.HandsState());
                    Log.Info("autotest : hayon " + CarDoors.StateOf(car, "Hatch"));
                }
            }
            // [Test] Autotest=detacher (retour d'un joueur, 08/10 : « driver cant unbuckle seatbelt ») : l'hote au volant de
            // [Test] TestVoiture a 15/22 s, boucle par l'automate du jeu a 26 s, etat toutes les 2 s, detache a 40 s.
            // [Test] DetacherPassager=1 : l'invite s'assoit a l'avant a 20 s.
            if (mode == "detacher")
            {
                string car = Config.Get("Test", "TestVoiture", "SORBET(190-200psi)");
                // ([Test] DetacherConducteur=invite : l'invite au volant, l'hote passager)
                if (Net.Session.IsHost != (Config.Get("Test", "DetacherConducteur", "hote") == "invite"))
                {
                    if (t > 15f && step == 0) { step = 1; Log.Info("autotest : " + VehicleSync.TestEnter(car, false)); }
                    if (t > 18f && step == 1) { step = 10; Log.Info("autotest : debout " + Seats.TestDriverBelt(car, 0)); }
                    if (t > 22f && step == 10) { step = 2; Log.Info("autotest : volant -> " + VehicleSync.TestEnter(car, true)); }
                    if (t > 24f && step == 2) { step = 11; Log.Info("autotest : assis " + Seats.TestDriverBelt(car, 0)); }
                    if (t > 26f && step == 11) { step = 3; Log.Info("autotest : " + Seats.TestDriverBelt(car, 1)); cabLogAt = Time.realtimeSinceStartup + 1f; }
                    if (step == 3 && t < 40f && Time.realtimeSinceStartup >= cabLogAt) { cabLogAt = Time.realtimeSinceStartup + 2f; Log.Info("autotest : " + Seats.TestDriverBelt(car, 0)); }
                    // ([Test] DetacherCasser=1 : boucle eteinte puis rallumee a 30 s, comme recopiee d'un autre ; detacher par le clic)
                    if (Config.GetInt("Test", "DetacherCasser", 0) != 0 && t > 30f && step == 3 && !brokeLock) { brokeLock = true; Log.Info("autotest : " + Seats.TestBreakLock(car)); }
                    if (t > 40f && step == 3) { step = 4; Log.Info("autotest : " + Seats.TestDriverBelt(car, 2)); }
                    if (t > 43f && step == 4) { step = 5; Log.Info("autotest : apres " + Seats.TestDriverBelt(car, 0)); }
                }
                else
                {
                    if (Config.GetInt("Test", "DetacherPassager", 0) != 0 && t > 20f && step == 0) { step = 1; Log.Info("autotest : passager " + Seats.TestSit(car, 0)); }
                    // ([Test] DetacherCeinture=s : le passager s'attache a s secondes)
                    float dc = Config.GetFloat("Test", "DetacherCeinture", 0f);
                    if (dc > 0f && t > dc && step == 1) { step = 6; Log.Info("autotest : passager " + Seats.TestBelt()); }
                    if (step == 6 && t > 34f) step = 1;
                    if (t > 34f && step < 2) { step = 2; Log.Info("autotest : ceintures " + Seats.BeltState()); }
                }
            }
            // [Test] Autotest=axes : commandes des voitures garees (pedale de frein qui bouge quand un joueur recule a pied, retour
            // de witefold, 11/10).
            if (mode == "axes" && ((t > 30f && step == 0) || (t > 60f && step == 1))) { step++; Log.Info("autotest : axes " + VehicleSync.AxesState()); }
            if (mode == "audit" && t > 70f && step == 0) { step = 1; Log.Info("autotest : clic " + Audit.TestAct()); }
            if (mode == "audit" && t > 75f && step == 1) { step = 2; Log.Info("autotest : " + Audit.State()); }
            CameraKnobs(t);
            if (mode == "cd") TestCd(t);
            if (mode == "prise") TestPlug(t);
            if (mode == "paie") TestPay(t);
            if (mode == "teletexte") TestTeletext(t);
            if (mode == "nuages" && !MWCoop.Net.Session.IsHost && t > 30f && step == 0)
            {
                // Invite : nuages inverses ici ; le message suivant de l'hote doit les remettre comme chez lui.
                step = 1;
                GameObject co = GameObject.Find("MAP/WEATHER/Clouds");
                Transform o = co != null ? co.transform.Find("CloudObjects") : null;
                if (o != null) { o.gameObject.SetActive(!o.gameObject.activeSelf); Log.Info("autotest : nuages inverses ici -> " + o.gameObject.activeSelf); }
            }
            if (mode == "nuages" && t > 28f && t < 40f && t >= cdLog)
            {
                cdLog = t + 2f;
                GameObject co = GameObject.Find("MAP/WEATHER/Clouds");
                Transform o = co != null ? co.transform.Find("CloudObjects") : null;
                Log.Info("autotest : nuages " + (o != null ? o.gameObject.activeSelf.ToString() : "?"));
            }
            if (mode == "fluide")
            {
                // Invite : vitesse apparente de la copie de TestVoiture image par image (a-coups = grand ecart-type).
                Rigidbody b = VehicleSync.Body(Config.Get("Test", "TestVoiture", "SORBET(190-200psi)"));
                if (b != null && Time.deltaTime > 0f)
                {
                    Vector3 pos = b.transform.position;
                    if (fluLast != Vector3.zero) { float v = (pos - fluLast).magnitude / Time.deltaTime; fluN++; fluSum += v; fluSq += v * v; if (v < 0.05f) fluStill++; }
                    fluLast = pos;
                    // Pieces de la voiture qui bougent PAR RAPPORT a elle (banquette, coffre qui tremblent chez le passager) :
                    // deplacement moyen par image de chaque corps et de chaque objet a rendu nomme, dans le repere de la voiture.
                    foreach (Transform c in b.GetComponentsInChildren<Transform>())
                    {
                        if (c == b.transform || (c.GetComponent<Rigidbody>() == null && c.GetComponent<Renderer>() == null)) continue;
                        string cn = c.name.ToLowerInvariant();
                        if (cn.Contains("tire") || cn.Contains("rim") || cn.Contains("wheel") || cn.Contains("hubcap") || cn.Contains("needle")) continue;   // (tournent normalement)
                        Vector3 lp = b.transform.InverseTransformPoint(c.position);
                        Quaternion lr = Quaternion.Inverse(b.rotation) * c.rotation;
                        float[] acc;
                        if (!fluParts.TryGetValue(c, out acc)) { fluParts[c] = new float[] { lp.x, lp.y, lp.z, 0, 0, 0, lr.x, lr.y, lr.z, lr.w }; continue; }
                        var plp = new Vector3(acc[0], acc[1], acc[2]);
                        var plr = new Quaternion(acc[6], acc[7], acc[8], acc[9]);
                        acc[3] += (lp - plp).magnitude; acc[4] += Quaternion.Angle(lr, plr); acc[5] += 1;
                        acc[0] = lp.x; acc[1] = lp.y; acc[2] = lp.z; acc[6] = lr.x; acc[7] = lr.y; acc[8] = lr.z; acc[9] = lr.w;
                    }
                    if (t >= fluLog) { fluLog = t + 2f; if (fluN > 0) { float m = fluSum / fluN; Log.Info("autotest : fluide, vitesse " + m.ToString("F2") + " m/s, ecart-type " + Mathf.Sqrt(Mathf.Max(0f, fluSq / fluN - m * m)).ToString("F2") + ", images immobiles " + fluStill + "/" + fluN); } fluN = 0; fluSum = fluSq = 0f; fluStill = 0;
                        var worst = new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<float, string>>();
                        foreach (var kv in fluParts)
                            if (kv.Key != null && kv.Value[5] > 0)
                            {
                                float mm = kv.Value[3] / kv.Value[5] * 1000f, deg = kv.Value[4] / kv.Value[5];
                                if (mm > 0.5f || deg > 0.05f) worst.Add(new System.Collections.Generic.KeyValuePair<float, string>(mm + deg * 10f, VehicleSync.RelPath(b.transform, kv.Key) + (kv.Key.GetComponent<Rigidbody>() != null ? "[corps " + (kv.Key.GetComponent<Rigidbody>().isKinematic ? "cin" : "dyn") + "]" : "") + " " + mm.ToString("F1") + " mm " + deg.ToString("F2") + " deg"));
                                kv.Value[3] = kv.Value[4] = kv.Value[5] = 0;
                            }
                        worst.Sort((x, y) => y.Key.CompareTo(x.Key));
                        var sbw = new System.Text.StringBuilder("autotest : fluide, pieces qui bougent dans la voiture (par image) :");
                        for (int i = 0; i < worst.Count && i < 8; i++) sbw.Append(' ').Append(worst[i].Value).Append(';');
                        Log.Info(worst.Count == 0 ? "autotest : fluide, aucune piece ne bouge dans la voiture" : sbw.ToString());
                    }
                }
            }
            if (mode == "mort")
            {
                // [Test] TestCause (booleen de Systems/Death) a 30 s ; TestReapparition=1/2 choisit 3 s apres. MortVolant=1 : au
                // volant de TestVoiture des 22 s (mort au volant : commandes coupees, puis rendues).
                if (Config.GetInt("Test", "MortVolant", 0) != 0 && t > 22f && step == 0 && step2 == 0) { step2 = 1; Log.Info("autotest : mort au volant, " + VehicleSync.TestEnter(Config.Get("Test", "TestVoiture", "SORBET(190-200psi)"), true)); }
                if (t > 30f && step == 0) { step = 1; Log.Info("autotest : " + Respawn.TestDie(Config.Get("Test", "TestCause", "Hunger"))); }
                if (t > 31f && Time.frameCount % 90 == 0 && t < 45f) Log.Info("autotest : " + Respawn.State() + " ; " + Respawn.FxDiff());
                if (t > 38f && step == 1)
                {
                    step = 2;
                    string png = System.IO.Path.Combine(System.IO.Path.Combine(Log.DataDir, "dumps"), "reapparition-" + Config.GetInt("Test", "TestReapparition", 1) + ".png");
                    Application.CaptureScreenshot(png);
                    Log.Info("autotest : capture " + png);
                }
                // ([Test] SangRetour=1 : a 50 s, l'automate Death de la camera relance hors de toute mort -- le sang revient ;
                // attendu : eteint par la surveillance)
                if (Config.GetInt("Test", "SangRetour", 0) != 0 && t > 50f && step == 2) { step = 3; Log.Info("autotest : sang relance, " + Respawn.TestStrayBlood()); }
                if (step == 3 && t > 50.5f && t < 56f && Time.frameCount % 60 == 0) Log.Info("autotest : apres le sang relance : " + Respawn.State());
            }
            // [Test] Autotest=peinture : chacun fait apparaitre la meme piece neuve (distributeur [Test] PeintureDistributeur,
            // Prefab VIN405 : pare-chocs) a 40 s ; sa peinture a 50 et 90 s (l'invite doit avoir celle de l'hote).
            if (mode == "peinture")
            {
                string pf = Config.Get("Test", "PeintureDistributeur", "VIN405");
                if (t > 40f && step == 0)
                {
                    step = 1;
                    foreach (Object o in Game.AllFsms())
                    {
                        PlayMakerFSM f = o as PlayMakerFSM;
                        FsmGameObject pv = f != null && f.FsmName == "Spawn" ? f.FsmVariables.FindFsmGameObject("Prefab") : null;
                        if (Config.GetInt("Test", "PeintureRetirer", 0) != 0 && f != null && f.FsmName == "Paint" && f.FsmVariables.FindFsmString("ID") != null && f.FsmVariables.FindFsmString("ID").Value == pf + "C1") { Game.SetState(f, "Random type"); Log.Info("autotest : peinture, " + pf + "C1 retiree au hasard ici"); break; }
                        if (pv == null || pv.Value == null || pv.Value.name != pf) continue;
                        Log.Info("autotest : peinture, distributeur " + Recon.Path(f.transform) + " [" + f.ActiveStateName + "] allume " + f.enabled + " actif " + f.gameObject.activeInHierarchy);
                        f.SendEvent("SPAWNITEM");   // (comme un colis : numero suivant, peinture tiree)
                        Log.Info("autotest : peinture, " + pf + " demande -> [" + f.ActiveStateName + "]");
                        break;
                    }
                }
                if ((t > 50f && step2 == 0) || (t > 90f && step2 == 1)) { step2++; Log.Info("autotest : peinture " + pf + "C1 : " + Paint.State(pf + "C1")); }
            }
            // [Test] Autotest=vin : table de la plaque VIN a 30 s et 70 s (chacun).
            if (mode == "vin" && ((t > 30f && step == 0) || (t > 70f && step == 1))) { step++; Log.Info("autotest : " + Vin.Describe()); }
            if (mode == "parebrise")
            {
                // Hote : casse le pare-brise de [Test] TestVoiture (CORRIS) a 30 s ; chacun note son etat a 25 et 70 s.
                string car = Config.Get("Test", "TestVoiture", "CORRIS");
                if (Net.Session.IsHost && t > 30f && step == 0) { step = 1; Log.Info("autotest : " + Windshields.TestBreak(car)); }
                if ((t > 25f && step2 == 0) || (t > 70f && step2 == 1)) { step2++; Log.Info("autotest : t=" + t.ToString("F0") + " " + Windshields.State(car)); }
            }
            if (mode == "couchette")
            {
                // Invite assis sur la couchette de la GIFU (place [Test] TestPlace, 2 : milieu) a 20 s ; l'hote le filme
                // (camera d'essai devant la couchette) a 40 s.
                if (!Net.Session.IsHost && t > 20f && step == 0) { step = 1; Log.Info("autotest : couchette " + Seats.TestSit("GIFU", Config.GetInt("Test", "TestPlace", 2))); }
                if (Net.Session.IsHost && t > 30f)
                {
                    Transform car; Vector3 head; string cn;
                    for (int id = 1; id < 8; id++)
                        if (Seats.RemoteSeat(id, out car, out head, out cn) && car != null)
                        {
                            if (testCam == null) { testCam = new GameObject("MWCoop-CameraEssai").AddComponent<Camera>(); testCam.depth = 100; testCam.nearClipPlane = 0.03f; testCam.fieldOfView = 70; }
                            testCam.transform.position = car.TransformPoint(head + (Config.Get("Test", "CouchetteVue", "") == "cote" ? new Vector3(0.95f, -0.05f, 0.35f) : new Vector3(0.55f, -0.1f, 1.1f)));
                            testCam.transform.LookAt(car.TransformPoint(head + new Vector3(0f, -0.35f, 0f)), car.up);
                            if (step == 0 && t > 70f) { step = 1; CaptureSoon("couchette", 0.1f); }
                            break;
                        }
                }
            }
            if (mode == "souleve")
            {
                // Hote : a cote de la mobylette a 30 s, la deplace a la main (1 m/s, 4 s), monte dessus a 45 s ; chacun note sa
                // position (l'invite la voit-elle bouger ? revient-elle a sa place quand on monte ?).
                string car = Config.Get("Test", "TestVoiture", "JONNEZ ES(Clone)");
                Rigidbody b = VehicleSync.Body(car);
                if (Net.Session.IsHost)
                {
                    if (t > 30f && step == 0) { step = 1; Log.Info("autotest : souleve " + VehicleSync.TestEnter(car, false) + (b != null ? ", " + car + " en " + b.position.ToString("F2") : ", introuvable")); }
                    if (b != null && t > 33f && t < 37f) { Vector3 f = b.transform.forward; f.y = 0; b.velocity = f.normalized * 1f + Vector3.up * Mathf.Min(b.velocity.y, 0f); }
                    if (t > 40f && step == 1) { step = 2; Log.Info("autotest : souleve, deplacee en " + (b != null ? b.position.ToString("F2") : "?")); }
                    if (t > 45f && step == 2) { step = 3; Log.Info("autotest : souleve, monte -> " + VehicleSync.TestEnter(car, true)); }
                    if (t > 52f && step == 3) { step = 4; Log.Info("autotest : souleve, apres montee en " + (b != null ? b.position.ToString("F2") : "?")); }
                }
                else if (b != null && Time.frameCount % 300 == 0 && t > 20f) Log.Info("autotest : souleve, vue ici en " + b.position.ToString("F2") + (b.isKinematic ? " (copie)" : ""));
            }
            if (mode == "accident")
            {
                // Au volant (mode conduite), la tete du conducteur se detache a 30 s, en roulant.
                string car = Config.Get("Test", "TestVoiture", "SORBET(190-200psi)");
                float choc = Config.GetInt("Test", "TestChoc", 30);   // (passager a bord, voiture de mod : plus tard ; tout le reste suit)
                float o = choc - 30f;
                if (t > 15f + o && step == 0) { step = 1; Log.Info("autotest : " + VehicleSync.TestEnter(car, false)); }
                if (t > 22f + o && step == 1) { step = 2; Log.Info("autotest : volant -> " + VehicleSync.TestEnter(car, true)); }
                Rigidbody b = VehicleSync.Body(car);
                if (b != null && t > choc - 5f && t < choc + 1f) { Vector3 f = b.transform.forward; f.y = 0; b.velocity = f.normalized * 8f + Vector3.up * Mathf.Min(b.velocity.y, 0f); }
                if (t > choc && step == 2) { step = 3; Log.Info("autotest : choc " + Respawn.TestCrash(car)); }
                if (t > 30.5f + o && Time.frameCount % 90 == 0 && t < 46f + o) Log.Info("autotest : " + Respawn.State());
                if (t > 46f + o && step == 3) { step = 4; Log.Info("autotest : remonte " + VehicleSync.TestEnter(car, false)); }
                if (t > 52f + o && step == 4) { step = 5; Log.Info("autotest : volant -> " + VehicleSync.TestEnter(car, true)); }
                if (t > 54f + o && step == 5 && Time.frameCount % 120 == 0 && t < 60f + o) Log.Info("autotest : yeux " + Seats.DriverEyes(car.Split('(')[0]));
                // (retour de JD, 14/10 : apres une mort au volant, remonte dans la voiture, teleporte a la maison a la sortie)
                // [Test] DeplaceJoueur=1 : a 56 s, le joueur (accroche au siege) est pose 800 m plus loin -- la garde doit le ramener.
                if (t > 56f + o && step == 5 && Config.GetInt("Test", "DeplaceJoueur", 0) != 0 && !moveDone) { moveDone = true; Transform pl = Game.PlayerT; if (pl != null) { pl.position += new Vector3(800f, 0f, 0f); Log.Info("autotest : joueur deplace de 800 m (accroche a " + (pl.parent != null ? pl.parent.name : "rien") + ")"); } }
                if (b != null && t > 58f + o && step == 5) { step = 6; Transform pl = Game.PlayerT; Log.Info("autotest : remonte, joueur a " + (pl != null ? (pl.position - b.position).magnitude.ToString("F1") : "?") + " m de la voiture, parent " + (pl != null && pl.parent != null ? Recon.Path(pl.parent) + ", local " + pl.localPosition.ToString("F3") : "aucun")); }
                if (t > 60f + o && step == 6) { step = 7; Log.Info("autotest : conduite " + VehicleSync.TestDriveDump(car)); Log.Info("autotest : sortie -> " + VehicleSync.TestExit(car)); }
                if (b != null && t > 64f + o && step == 7) { step = 8; Transform pl = Game.PlayerT; Log.Info("autotest : apres la sortie, joueur en " + (pl != null ? pl.position.ToString("F1") : "?") + ", a " + (pl != null ? (pl.position - b.position).magnitude.ToString("F1") : "?") + " m de la voiture"); }
            }
            if (mode == "coffre")
            {
                // Invite : ouvre [Test] TestPorte de TestVoiture des que la voiture (conduite par l'autre) roule,
                // la lache, puis la referme 3 s plus tard (bouton tenu jusqu'au claquement).
                string vc = Config.Get("Test", "TestVoiture", "SORBET(190-200psi)"), vp = Config.Get("Test", "TestPorte", "Hatch");
                Rigidbody b = VehicleSync.Body(vc);
                float now = Time.realtimeSinceStartup;
                if (b != null && t > 12f && !coffreStartSet) { coffreStartSet = true; coffreStart = b.position; }
                if (b != null && coffreStartSet && step == 0 && (b.position - coffreStart).magnitude > 3f) { step = 1; coffreT = now; Log.Info("autotest : la voiture roule, ouvre " + CarDoors.TestOpen(vc, true, vp)); }
                if (step == 1 && now - coffreT > 0.6f) { step = 2; coffreT = now; Log.Info("autotest : lache " + CarDoors.TestState(vc, "Mouse off", vp)); }
                if (step == 2 && now - coffreT > 3f) { step = 3; Log.Info("autotest : referme " + CarDoors.TestGrab(vc, vp)); }
                if (b != null && step >= 1 && Time.frameCount % 60 == 0) Log.Info("autotest : " + CarDoors.StateOf(vc, vp) + ", voiture en " + b.position.ToString("F1"));
            }
            if (mode == "peindre")
            {
                // [Test] TestPiece peinte en rouge a 30 s ; etat a 40 s.
                string id = Config.Get("Test", "TestPiece", "VIN4111");
                if (t > 30f && step == 0) { step = 1; Log.Info("autotest : " + Paint.TestSpray(id, new Color(0.8f, 0.1f, 0.1f, 1f))); }
                if (t > 40f && step == 1) { step = 2; Log.Info("autotest : peinture " + id + " : " + Paint.State(id)); }
            }
            if (mode == "etatpeinture" && t > 36f && !done) { done = true; string id = Config.Get("Test", "TestPiece", "VIN4111"); Log.Info("autotest : peinture " + id + " : " + Paint.State(id)); }
            if (mode == "reglage" && t > 30f && !done) { done = true; Log.Info("autotest : reglage " + Settings.TestNearest()); }
            if (mode == "parallelisme")
            {
                // Sauvegarde Rivett (cremaillere montee) : l'hote regle la biellette gauche de +0,5 a 30 s (comme sa vis) ; etat des deux cotes a 45 s.
                if (t > 30f && step == 0) { step = 1; if (MWCoop.Net.Session.IsHost) Log.Info("autotest : parallelisme " + Settings.TestToe(true)); }
                if (t > 45f && step == 1) { step = 2; Log.Info("autotest : parallelisme " + Settings.TestToe(false)); }
            }
            if (mode == "quete")
            {
                // [Test] TestQuete=partie de cle, TestEvenement=evenement envoye a 30 s ; etat a 40 s.
                string part = Config.Get("Test", "TestQuete", "WoodJob1Point");
                if (t > 30f && step == 0)
                {
                    step = 1;
                    Log.Info("autotest : quete " + Jobs.TestEvent(part, Config.Get("Test", "TestEvenement", "ORDER")));
                    if (Config.GetInt("Test", "TestPaie", 0) != 0) Log.Info("autotest : le boulot paie, " + Wallet.Test(300));
                }
                if (t > 40f && step == 1) { step = 2; Log.Info("autotest : quete " + Jobs.StateOf(part)); }
            }
            if (mode == "etatquete")
            {
                string part = Config.Get("Test", "TestQuete", "WoodJob1Point");
                string st = Jobs.StateOf(part);
                // Le boulot rejoue ici paie lui aussi (simule) : ne doit pas etre compte deux fois.
                if (Config.GetInt("Test", "TestPaie", 0) != 0 && step == 0 && st.EndsWith("Activate order")) { step = 1; Log.Info("autotest : le boulot rejoue paie, " + Wallet.Test(300)); }
                if (t > 45f && !done) { done = true; Log.Info("autotest : quete " + st + ", " + Wallet.State()); }
            }
            if (mode == "desynchro" && t > 6f && !done) { done = true; Log.Info("autotest : porte locale seulement " + Interactions.TestLocalDoor(Game.PlayerGo.transform.position)); }
            // [Test] Autotest2=etat : apres l'essai principal, l'hote met TestQuete dans TestEtat a 45 s.
            if (Config.Get("Test", "Autotest2", "") == "etat" && MWCoop.Net.Session.IsHost && t > 45f && step2 == 0)
            { step2 = 1; Log.Info("autotest : " + WorldFsms.TestState(Config.Get("Test", "TestQuete", "TVPrograms::Schedule"), Config.Get("Test", "TestEtat", "News"))); }
            if (mode == "phares" && t > 20f && !done) { done = true; Log.Info("autotest : " + Interactions.TestNamed(Config.Get("Test", "TestObjet", "KEKMET(350-400psi)/LOD/Dashboard/ButtonLightModes"))); }
            if (mode == "porte" && t > 15f && !done)
            {
                done = true;
                GameObject pl = Game.PlayerGo;
                Log.Info("autotest : porte " + Interactions.TestNearestDoor(pl.transform.position));
            }
            // [Test] Autotest=declencheur : a 25 s l'hote est teleporte dans la zone de [Test] TestDeclencheur (par defaut la
            // porte arriere du bar, STORE_AREA/Stuff/LOD/DoorBarRear/TriggerIn : 'Wait IN 2' -FINISHED-> 'State 14', Teimo
            // salue) ; a 20 s chacun note si l'automate est suivi, a 40 s son etat (attendu 'State 14' chez l'invite aussi).
            if (mode == "declencheur")
            {
                string tdPart = Config.Get("Test", "TestDeclencheur", "STORE_AREA/Stuff/LOD/DoorBarRear/TriggerIn");
                if (t > 20f && step == 0) { step = 1; Log.Info("autotest declencheur : a 20 s " + WorldFsms.StateOf(tdPart)); }
                if (t > 25f && step == 1)
                {
                    step = 2;
                    if (Net.Session.IsHost)
                    {
                        GameObject tz = Game.FindAny(tdPart);
                        if (tz == null) Log.Info("autotest declencheur : " + tdPart + " introuvable, pas de teleportation");
                        else
                        {
                            // (TeleportTo pose 1,5 m derriere 'feet' selon le cap, 1 m au-dessus : le point vise est la zone elle-meme)
                            Vector3 zp = tz.transform.position;
                            Menu.TeleportTo(zp + new Vector3(0f, 0f, 1.5f), 0f, "essai declencheur");
                            Log.Info("autotest declencheur : hote teleporte dans " + tdPart + " a " + zp.ToString("F1") + " ; " + WorldFsms.StateOf(tdPart));
                        }
                    }
                }
                if (t > 40f && step == 2) { step = 3; Log.Info("autotest declencheur : " + (Net.Session.IsHost ? "hote" : "invite") + " a 40 s " + WorldFsms.StateOf(tdPart)); }
            }
            if (mode == "monter")
            {
                // [Test] TestPiece monte a 30 s, demontee a 45 s (comme les clics du jeu).
                string id = Config.Get("Test", "TestPiece", "VIN413C1");
                // ([Test] PorterAvant=1 : la piece promenee comme tenue en main de 24 a 29 s, puis montee : chez l'autre elle
                // etait cinematique, suivie a distance)
                if (Config.GetInt("Test", "PorterAvant", 0) != 0 && t > 24f && t < 29f && step == 0) { string w = Props.TestCarry(id, t); if (Time.frameCount % 60 == 0) Log.Info("autotest : " + w); }
                if (t > 30f && step == 0) { step = 1; Log.Info("autotest : " + Parts.TestToggle(id)); }
                if (Config.GetInt("Test", "TestVis", 0) != 0)
                {
                    // Vis : 3 crans serres (36-38 s), 1 desserre (40 s), etat a 44 s ; pas de demontage.
                    if (t > 35f + step && step >= 1 && step <= 3) { step++; Log.Info("autotest : vis " + Parts.TestBolt(id, true)); }
                    if (t > 40f && step == 4) { step = 5; Log.Info("autotest : vis " + Parts.TestBolt(id, false)); }
                    if (t > 44f && step == 5) { step = 6; Log.Info("autotest : vis " + Parts.BoltState(id)); }
                }
                else if (t > 45f && step == 1) { step = 2; Log.Info("autotest : " + Parts.TestToggle(id)); }
            }
            if (mode == "palan") Palan(t);
            // [Test] Autotest=garee (2 joueurs, l'invite arrive en retard) : l'hote allume les phares de [Test] TestVoiture (SORBET) a
            // 20 et 22 s (ButtonLightModes deux fois : position puis route, Selection 2) et les essuie-glaces a 24 s (ButtonWipers) ;
            // chacun note l'etat a 25, 40 et 45 s ("autotest garee : phares=... essuie=..."). Chez l'invite arrive apres ces clics,
            // phares et essuie-glaces doivent etre comme chez l'hote a 40-45 s (instantane de Knobs, 20 s apres son arrivee en jeu).
            if (mode == "garee")
            {
                string gcar = Config.Get("Test", "TestVoiture", "SORBET(190-200psi)");
                if (Net.Session.IsHost)
                {
                    if (t > 20f && step == 0) { step = 1; Log.Info("autotest garee : " + Knobs.TestSwitch(gcar, "ButtonLightModes")); }
                    if (t > 22f && step == 1) { step = 2; Log.Info("autotest garee : " + Knobs.TestSwitch(gcar, "ButtonLightModes")); }
                    if (t > 24f && step == 2) { step = 3; Log.Info("autotest garee : " + Knobs.TestSwitch(gcar, "ButtonWipers")); }
                }
                if ((t > 25f && step2 == 0) || (t > 40f && step2 == 1) || (t > 45f && step2 == 2))
                { step2++; Log.Info("autotest garee : t=" + t.ToString("F0") + " (" + (Net.Session.IsHost ? "hote" : "invite") + ") " + Knobs.DescribeSwitches(gcar)); }
            }
            // [Test] Autotest=casse (2 joueurs) : l'INVITE « casse » a 30 s les pieces de [Test] TestPieces (montees sur la CORRIS de
            // l'hote) comme des BREAKOFF ; avec CasseSansGarde=1 elles partent quand meme : attendu chez l'hote « sans autorite
            // sur la voiture : ignoree » (et « plus de 3 casses en 1 s ») -- rien de demonte chez lui.
            if (mode == "casse" && Net.Session.IsHost && t > 25f && step == 0)
            {
                step = 1;
                foreach (string id in Config.Get("Test", "TestPieces", "VIN405C1,VIN4111,VIN413D1,VIN406B2,VIN4072,VIN2141").Split(','))
                    Log.Info("autotest : monte " + id.Trim() + " : " + Parts.TestToggle(id.Trim()));
            }
            if (mode == "casse" && !Net.Session.IsHost && t > 40f && step == 0)
            {
                step = 1;
                foreach (string id in Config.Get("Test", "TestPieces", "VIN405C1,VIN4111,VIN413D1,VIN406B2,VIN4072,VIN2141").Split(','))
                    Log.Info("autotest : casse " + id.Trim() + " : " + Parts.TestBreak(id.Trim()));
            }
            // [Test] SuivrePiece=~gasoline : position de cet objet a 46 s (l'autre joueur le promene).
            string watchKey = Config.Get("Test", "SuivrePiece", "");
            if (watchKey.Length > 0 && t > Config.GetInt("Test", "SuivreDelai", 46) && !watchLogged)
            {
                watchLogged = true;
                string k = watchKey.StartsWith("~") ? Props.FindKey(watchKey.Substring(1)) : watchKey;
                Log.Info("autotest : " + k + " est en " + Props.Where(k));
            }
            // [Test] Autotest=tenir (TestPiece, ~sandbag(item1) par defaut) : l'hote tient l'objet devant lui en marchant en
            // rond de 40 a 70 s ; l'invite note 2 fois/s sa position dans le repere de l'avatar de l'hote (pieds, cap) et,
            // a 85 s, l'ecart type de cette position : un objet colle a l'avatar ne bouge pas dans son repere.
            if (mode == "tenir")
            {
                string id = Config.Get("Test", "TestPiece", "~sandbag(item1)");
                if (id.StartsWith("~")) id = Props.FindKey(id.Substring(1)) ?? id;
                if (Net.Session.IsHost == (Config.Get("Test", "Teneur", "hote") != "invite"))   // ([Test] Teneur=invite : l'invite tient, l'hote note)
                {
                    Transform cam = PlayerSync.LocalCamera;
                    if (t > 40f && t < 70f && cam != null)
                    {
                        GameObject pl = Game.PlayerGo;
                        var cc = pl != null ? pl.GetComponent<CharacterController>() : null;
                        if (cc != null) { pl.transform.Rotate(0, 30f * Time.deltaTime, 0); cc.SimpleMove(pl.transform.forward * 1.8f); }
                        Rigidbody hb = Props.TestHold(id, cam);
                        if (step == 0) { step = 1; Log.Info("autotest : tenir " + id + (hb != null ? "" : " (absent)")); }
                    }
                    if (t > 70f && step == 1) { step = 2; Props.TestHold(id, null); Log.Info("autotest : tenir, lache"); }
                }
                else
                {
                    Avatar av = null;
                    foreach (Avatar a in PlayerSync.Avatars) if (a.Root != null) { av = a; break; }
                    Rigidbody b = Props.BodyOf(id);
                    if (av != null && b != null && b.isKinematic && (av.Player.State.Flags & PlayerSync.F_Carry) != 0 && t >= holdLog && t < 120f)
                    {
                        holdLog = t + 0.5f;
                        Vector3 lp = av.Root.transform.InverseTransformPoint(b.position);
                        holdN++; holdSum += lp; holdSq += Vector3.Scale(lp, lp);
                        if (holdN % 6 == 1) Log.Info("autotest : tenir, objet dans le repere de l'avatar " + lp.ToString("F2"));
                    }
                    if (t > 125f && holdN > 0 && !done)
                    {
                        done = true;
                        Vector3 m = holdSum / holdN, v = holdSq / holdN - Vector3.Scale(m, m);
                        Log.Info("autotest : tenir, " + holdN + " mesures, moyenne " + m.ToString("F2") + ", ecart type " + new Vector3(Mathf.Sqrt(Mathf.Max(v.x, 0)), Mathf.Sqrt(Mathf.Max(v.y, 0)), Mathf.Sqrt(Mathf.Max(v.z, 0))).ToString("F3"));
                    }
                }
            }
            // [Test] Autotest=ranger (TestPiece, ~sandbag(item1)) : comme un mod d'inventaire (YAIM). L'hote tient l'objet de
            // 40 a 50 s, l'eteint en main a 50 s (range), le rallume pose 1,5 m devant lui a 70 s (sorti au sol). L'invite note
            // toutes les 2 s s'il le voit (actif) et ou. Attendu : cache chez l'invite apres 50 s (horloge de l'hote), revu
            // pose devant l'hote apres 70 s.
            // [Test] Rangeur=invite : l'invite range ; RessortirMain=1 : comme l'outil du jeu (grattoir, F puis F) -- l'objet
            // reste dans la main eteint, puis rallume dans la main a 70 s, tenu jusqu'a 76 s et lache. Le rangeur note aussi.
            // [Test] Autotest=tracts : l'hote lance le boulot des tracts (JOBS/ADs :: Data -> "New job") a 30 s, tient le paquet
            // de tracts de 45 a 55 s (Props.TestHold) ; chacun note toutes les 3 s de 28 a 70 s le paquet (actif, pose, parent,
            // cle de Props) et l'etat de l'automate.
            if (mode == "tracts")
            {
                GameObject ads = Game.FindAny("JOBS/ADs");
                PlayMakerFSM data = ads != null ? Game.FsmOn(ads, "Data") : null;
                // (vrai depart : l'appel aux pubs de Calls, Autotest=appel ; tenu ensuite de 65 a 75 s)
                if (Config.GetInt("Test", "TractsForce", 0) == 0) Calls.Test("appel", t);
                // ([Test] TractsForce=1 : "New job" pose chez CHACUN a 30 s, comme le jour du boulot venu ; l'invite tient le paquet
                // de 80 a 88 s)
                else if (t > 30f && step == 0 && data != null) { step = 1; Game.SetState(data, "New job"); Log.Info("autotest : tracts, New job ici -> " + data.ActiveStateName); }
                if (!Net.Session.IsHost && Config.GetInt("Test", "TractsForce", 0) != 0 && t > 80f && t < 88f && PlayerSync.LocalCamera != null) { Rigidbody gh = Props.TestHold(Props.FindKey("advert pile"), PlayerSync.LocalCamera); if (step == 1) { step = 2; Log.Info("autotest : tracts, l'invite tient le paquet (" + (gh != null ? "ok" : "absent") + ")"); } }
                if (!Net.Session.IsHost && t > 88f && step == 2) { step = 3; Props.TestHold(Props.FindKey("advert pile"), null); }
                float h0 = 65f;
                if (step == 0 && t > 32f && data != null && data.ActiveStateName != "Wait call") step = 1;
                string pk = Props.FindKey("advert pile");
                if (Net.Session.IsHost && t > h0 && t < h0 + 10f && PlayerSync.LocalCamera != null) { Rigidbody hb = Props.TestHold(pk, PlayerSync.LocalCamera); if (step == 1) { step = 2; Log.Info("autotest : tracts, paquet tenu (" + pk + ", " + (hb != null ? "ok" : "absent") + ")"); } }
                if (Net.Session.IsHost && t > h0 + 10f && step == 2) { step = 3; Props.TestHold(pk, null); Log.Info("autotest : tracts, paquet lache"); }
                if (t > 40f && t < 95f && t >= holdLog)
                {
                    holdLog = t + 3f;
                    GameObject pile = null;
                    foreach (Rigidbody rb in Resources.FindObjectsOfTypeAll(typeof(Rigidbody))) if (rb.name == "advert pile(itemx)" && rb.hideFlags == HideFlags.None) pile = rb.gameObject;
                    Log.Info("autotest : tracts, automate " + (data != null ? data.ActiveStateName : "?") + ", paquet " + (pile == null ? "introuvable" : (pile.activeInHierarchy ? "actif" : "inactif") + " en " + pile.transform.position.ToString("F1") + " sous " + (pile.transform.parent != null ? Recon.Path(pile.transform.parent) : "racine")) + ", cle " + pk);
                }
            }
            // [Test] Autotest=usine : a 25 s l'hote gonfle ses compteurs de travail de l'usine (JOBS/FACTORY::PlayerData : minutes,
            // jours, paies) et envoie son automate comme un changement ordinaire ; l'invite note ses compteurs a 20 s et a 40 s.
            // Attendu chez l'invite : les deux lignes identiques (compteurs de l'hote pas recopies) et, entre les deux, « quete de
            // #1 : JOBS/FACTORY::PlayerData#0 -> ... ». Les deux employes et au repos (« Work ON »), voir Jobs.TestUsine.
            if (mode == "usine")
            {
                if (Net.Session.IsHost && t > 25f && step == 0) { step = 1; Log.Info("autotest usine : " + Jobs.TestUsine(0)); }
                if (!Net.Session.IsHost && t > 20f && step == 0) { step = 1; Log.Info("autotest usine : invite avant, " + Jobs.TestUsine(1)); }
                if (!Net.Session.IsHost && t > 40f && step == 1) { step = 2; Log.Info("autotest usine : invite apres, " + Jobs.TestUsine(1)); }
            }
            // [Test] Autotest=boulotloin : l'invite va a l'usine a 40 s (l'hote reste chez lui) ; a 60 et 75 s, chacun note la
            // distance mesuree par l'usine (JOBS/FACTORY :: Database, Distance -- l'usine ferme au-dela de 200 m) et si son
            // site est allume (OpeningTimes/LOD1). Attendu chez l'hote : distance de l'invite, site allume.
            if (mode == "boulotloin")
            {
                if (!Net.Session.IsHost && t > 40f && step == 0)
                {
                    step = 1;
                    GameObject pallet = Game.FindAny("JOBS/FACTORY/OpeningTimes/LOD1/JOB/NPClookPallet");
                    GameObject pl = Game.PlayerGo;
                    if (pallet != null && pl != null)
                    {
                        var cc = pl.GetComponent<CharacterController>(); if (cc != null) cc.enabled = false;
                        pl.transform.position = pallet.transform.position + Vector3.up * 1f; if (cc != null) cc.enabled = true;
                        Log.Info("autotest : boulotloin, invite pose a l'usine en " + pl.transform.position.ToString("F0"));
                    }
                    else Log.Info("autotest : boulotloin, usine introuvable");
                }
                if ((t > 60f && step <= 1 && holdLog < 60f) || (t > 75f && holdLog < 75f))
                {
                    holdLog = t;
                    GameObject fac = Game.FindAny("JOBS/FACTORY"), lod = Game.FindAny("JOBS/FACTORY/OpeningTimes/LOD1");
                    PlayMakerFSM db = fac != null ? Game.FsmOn(fac, "Database") : null;
                    HutongGames.PlayMaker.FsmFloat dist = db != null ? db.FsmVariables.FindFsmFloat("Distance") : null;
                    Log.Info("autotest : boulotloin, usine : distance " + (dist != null ? dist.Value.ToString("F0") : "?") + " (etat " + (db != null ? db.ActiveStateName : "?") + "), site " + (lod != null ? (lod.activeInHierarchy ? "allume" : "eteint") : "?") + ", joueur vise : " + NearDoors.Describe("JOBS/FACTORY"));
                }
            }
            // [Test] Autotest=radioautre : l'hote allume la radio du BACHGLOTZ ([Test] RadioBouton) a 40 s (bouton de volume, comme un clic : "Check
            // tune", volume 0,6), l'eteint a 58 s ; chacun note toutes les 3 s ou est son vrai jeu de canaux et les copies.
            // Attendu chez l'invite : une copie dans le BACHGLOTZ de 40 a 58 s, son vrai jeu de canaux pas deplace.
            if (mode == "radioflux" && !Net.Session.IsHost) Radio.TestStream(t);
            if (mode == "boombox") Radio.TestBoombox(t);   // (radio portable allumee par l'hote a 25 s ; copie chez l'invite ? voir Radio.TestBoombox)
            // (l'hote fait apparaitre a 40 s ; chacun compte ensuite les buches et sacs de Cheatbox++ a 55 s)
            // [Test] Autotest=fendattel : l'hote attelle la fendeuse au KEKMET a 40 s, la detelle a 55 s ; chacun note son etat
            // toutes les 3 s de 35 a 70 s. Attendu : meme etat des deux cotes quelques dixiemes apres chaque geste.
            // [Test] AutomateSuivi=<nom d'objet> : toutes les 5 s, l'etat de tous les automates 'Use' des objets de ce nom (quel que
            // soit le mode ; phares : ButtonLightModes -- retour de JD, 18/10 : L allume les feux de toutes les voitures visitees).
            if (t > 20f && t >= suiviLog)
            {
                suiviLog = t + 5f;
                string want = Config.Get("Test", "AutomateSuivi", "");
                if (want.Length > 0)
                {
                    var sb = new System.Text.StringBuilder("autotest : " + want + " :");
                    foreach (Object o in Game.FsmsNamed("Use"))
                    {
                        PlayMakerFSM f = o as PlayMakerFSM;
                        if (f == null || f.FsmName != "Use" || f.gameObject.name != want) continue;
                        sb.Append(' ').Append(Recon.Path(f.transform).Split('/')[0]).Append('=').Append(f.ActiveStateName).Append(f.gameObject.activeInHierarchy ? "" : "(eteint)").Append(';');
                    }
                    FsmString curVeh = FsmVariables.GlobalVariables.FindFsmString("PlayerCurrentVehicle");
                    FsmBool pStop = FsmVariables.GlobalVariables.FindFsmBool("PlayerStop");
                    sb.Append(" PlayerCurrentVehicle='").Append(curVeh != null ? curVeh.Value : "?").Append("' PlayerStop=").Append(pStop != null ? pStop.Value.ToString() : "?");
                    Log.Info(sb.ToString());
                }
            }
            // [Test] Autotest=jokkisia : a 40 s, composants d'une voiture IA de la course sur glace (la premiere trouvee) et de la
            // SORBET : scripts, sons, Drivetrain (pour greffer la mecanique et les sons de l'IA sur la Jokkis louee, JD 18/10).
            if (mode == "jokkisia" && t > 40f && step == 0)
            {
                step = 1;
                var targets = new System.Collections.Generic.List<GameObject>();
                GameObject ice = Game.FindAny("RACES/ICERACE");
                if (ice != null)
                    foreach (Transform tr in ice.GetComponentsInChildren<Transform>(true))
                        if (tr.Find("NavigationIce") != null && tr.GetComponent("CarDynamics") != null) { targets.Add(tr.gameObject); break; }
                GameObject so = Game.FindAny("SORBET(190-200psi)"); if (so != null) targets.Add(so);
                foreach (GameObject g in targets)
                {
                    Log.Info("autotest : objet " + Recon.Path(g.transform) + (g.activeInHierarchy ? "" : " (eteint)") + ", masse " + (g.GetComponent<Rigidbody>() != null ? g.GetComponent<Rigidbody>().mass.ToString("F0") : "?"));
                    foreach (Component c in g.GetComponents<Component>()) if (c != null) Log.Info("autotest :   racine : " + c.GetType().Name);
                    Drivetrain dt = g.GetComponent<Drivetrain>();
                    if (dt != null) Log.Info("autotest :   Drivetrain : " + dt.maxPower + " ch, couple " + dt.maxTorque + ", regimes " + dt.minRPM + "-" + dt.maxRPM + " (puissance a " + dt.maxPowerRPM + ", couple a " + dt.maxTorqueRPM + "), " + dt.transmission + ", rapports " + (dt.gearRatios != null ? dt.gearRatios.Length : 0) + ", final " + dt.finalDriveRatio);
                    int n = 0;
                    foreach (Component c in g.GetComponentsInChildren<Component>(true))
                    {
                        if (c == null || c is Transform || c is MeshFilter || c is MeshRenderer || c is Collider) continue;
                        string tn = c.GetType().Name;
                        if (tn == "PlayMakerFSM" || tn == "Wheel" || tn == "AudioSource" || tn.Contains("Sound") || tn.Contains("Audio") || tn == "CarDynamics" || tn == "Axles" || tn.Contains("Deform") || tn.Contains("Skid") || tn.Contains("Navigation"))
                        {
                            if (n++ < 90) Log.Info("autotest :   " + tn + " @ " + Recon.Path(c.transform).Substring(Recon.Path(g.transform).Length) + (tn == "PlayMakerFSM" ? " '" + ((PlayMakerFSM)c).FsmName + "'" : "") + (tn == "AudioSource" ? " clip " + (((AudioSource)c).clip != null ? ((AudioSource)c).clip.name : "-") : ""));
                        }
                    }
                    Log.Info("autotest :   " + n + " composants notables");
                }
            }
            // [Test] Autotest=automate, Automate=<chemin>:<nom>[,...] : a 40 s, etats, actions, variables et evenements de ces
            // automates (diagnostic ; OpNoob, 17/10 : l'arbre de la fendeuse ne s'attelle pas au tracteur).
            if (mode == "automate" && t > 40f && step == 0) { step = 1; DumpFsms(Config.Get("Test", "Automate", "Cutter/PTOCheck:Attach")); }
            if (mode == "fendattel")
            {
                if (Net.Session.IsHost && t > 40f && step == 0) { step = 1; Log.Info("autotest : " + CutterHitch.TestSet(1)); }
                if (Net.Session.IsHost && t > 55f && step == 1) { step = 2; Log.Info("autotest : " + CutterHitch.TestSet(0)); }
                if (t > 35f && t < 70f && t >= holdLog) { holdLog = t + 3f; Log.Info("autotest : fendattel, " + CutterHitch.State()); }
            }
            if (mode == "cbxextra" && Net.Session.IsHost && t > 40f && step == 0) { step = 1; Log.Info("autotest : cbxextra " + ModCheatbox.TestExtra()); }
            if (mode == "cbxextra" && t > 55f && step2 == 0) { step2 = 1; Log.Info("autotest : cbxextra, ici " + ModCheatbox.TestCount()); }
            if (mode == "jonne" && Net.Session.IsHost && t > 45f && step == 0) { step = 1; Log.Info("autotest : jonne, " + ModJonne.Test(0)); }
            if (mode == "jonne" && Net.Session.IsHost && t > 58f && step == 1) { step = 2; Log.Info("autotest : jonne, " + ModJonne.Test(1)); }
            if (mode == "jonne" && !Net.Session.IsHost && t > 50f && step == 0) { step = 1; Log.Info("autotest : jonne, " + ModJonne.Test(2)); }
            ModTowCart.Test(mode, t);
            ModJackStands.Test(mode, t);
            if (mode == "jonne2" && Net.Session.IsHost && t > 45f && step == 0) { step = 1; Log.Info("autotest : jonne, " + ModJonne.Test(5)); }
            if (mode == "jonne2" && t > 60f && step2 == 0) { step2 = 1; Log.Info("autotest : jonne, objets " + Props.Ids("w:jfs:") + " ; " + ModJonne.Test(3)); }
            if (mode == "jonne" && Net.Session.IsHost && t > 64f && step == 2) { step = 3; Log.Info("autotest : jonne, " + ModJonne.Test(4)); }
            if (mode == "jonne" && t > 70f && step2 == 0) { step2 = 1; Log.Info("autotest : jonne, " + ModJonne.Test(3)); }
            if (mode == "vendeur" && Net.Session.IsHost && t > 40f && step == 0) { step = 1; Log.Info("autotest : " + ModCheatbox.TestSeller(0, 0)); }
            if (mode == "vendeur" && Net.Session.IsHost && t > 46f && step == 1) { step = 2; Log.Info("autotest : " + ModCheatbox.TestSeller(1, Config.GetInt("Test", "VendeurPiece", 0))); }
            if (mode == "vendeur" && Net.Session.IsHost && t > 52f && step == 2) { step = 3; Log.Info("autotest : " + ModCheatbox.TestSeller(1, Config.GetInt("Test", "VendeurPiece2", 3))); }
            // [Test] Autotest=valise (mod Suitcase installe des deux cotes) : chacun note la valise toutes les 2 s de 30 a 85 s ;
            // l'hote la tient devant lui de 40 a 50 s ; l'invite la fait reapparaitre devant lui a 62 s comme le mod (ancienne
            // detruite, nouvelle a sa place). Attendu : memes poses des deux cotes apres chaque geste.
            if (mode == "valise")
            {
                GameObject sc = GameObject.Find("Suitcase");
                if (t > 30f && t < 85f && t >= holdLog) { holdLog = t + 2f; Log.Info("autotest : valise " + (sc != null ? sc.transform.position.ToString("F2") : "absente") + " | " + Props.Ids("w:mod:")); }
                if (Net.Session.IsHost && t > 40f && t < 50f && PlayerSync.LocalCamera != null) { Props.TestHold(Props.FindKey("valise"), PlayerSync.LocalCamera); if (step == 0) { step = 1; Log.Info("autotest : valise tenue par l'hote"); } }
                if (Net.Session.IsHost && t > 50f && step == 1) { step = 2; Props.TestHold(Props.FindKey("valise"), null); Log.Info("autotest : valise lachee par l'hote"); }
                if (!Net.Session.IsHost && t > 62f && step == 0 && sc != null && PlayerSync.LocalCamera != null)
                {
                    step = 1;
                    Transform cam = PlayerSync.LocalCamera;
                    Vector3 f = cam.forward; f.y = 0f; f.Normalize();
                    GameObject nc = (GameObject)Object.Instantiate(sc, cam.position + f * 1.5f - Vector3.up * 0.9f, Quaternion.LookRotation(-f, Vector3.up));
                    nc.name = "Suitcase";
                    Object.Destroy(sc);
                    Log.Info("autotest : valise reapparue devant l'invite en " + nc.transform.position.ToString("F2"));
                }
            }
            if (mode == "radioautre")
            {
                string part = Config.Get("Test", "RadioBouton", "BACHGLOTZ(1905kg)/RadioPivot/Radio/ButtonsRadio/RadioVolume::Knob");
                if (Net.Session.IsHost && t > 40f && step == 0)
                {
                    step = 1;
                    GameObject vb = Game.FindAny(part.Substring(0, part.IndexOf("::")));
                    PlayMakerFSM kf = vb != null ? Game.FsmOn(vb, "Knob") : null;
                    if (kf != null) { HutongGames.PlayMaker.FsmFloat vf = kf.FsmVariables.FindFsmFloat("Volume"); if (vf != null) vf.Value = 0.6f; }
                    Log.Info("autotest : radioautre, " + Jobs.TestSend(part, "Mouse off 2", "ON", "Check tune"));
                }
                if (Net.Session.IsHost && t > 50f && step == 1 && Config.GetInt("Test", "RadioCoupe", 0) != 0) { step = 2; Log.Info("autotest : radioautre, " + Radio.TestCut()); }
                if (Net.Session.IsHost && t > 58f && step == 1) { step = 2; Log.Info("autotest : radioautre, " + Jobs.TestSend(part, "Decrease", "OFF", "Off")); }
                if (!Net.Session.IsHost && Config.GetInt("Test", "RadioFlux", 0) != 0 && t > 30f && step == 0) { step = 1; Log.Info("autotest : radioautre, " + Radio.TestFolkSong()); }
                // ([Test] RadioLocale=s : l'invite allume ici la radio du KEKMET a s secondes, volume 0,3 -- ses reglages doivent rester les siens)
                float rl = Config.GetFloat("Test", "RadioLocale", 0f);
                if (!Net.Session.IsHost && rl > 0f && t > rl && step2 == 0)
                {
                    step2 = 1;
                    GameObject kv = Game.FindAny("KEKMET(350-400psi)/RadioPivot/Radio/ButtonsRadio/RadioVolume");
                    PlayMakerFSM kk = kv != null ? Game.FsmOn(kv, "Knob") : null;
                    if (kk != null) { HutongGames.PlayMaker.FsmFloat vf = kk.FsmVariables.FindFsmFloat("Volume"); if (vf != null) vf.Value = 0.3f; Game.SetState(kk, "Check tune"); }
                    Log.Info("autotest : radioautre, radio du KEKMET allumee ici " + (kk != null));
                }
                if (t > 35f && t < 72f && t >= holdLog) { holdLog = t + 3f; Log.Info("autotest : radioautre, " + Radio.GhostState()); }
            }
            // [Test] Autotest=tenirvoiture (TestPiece, ~sandbag(item1) ; TestVoiture, SORBET) : l'hote « tient » l'objet de 40 a
            // 55 s au milieu de l'habitacle de la voiture (comme une piece amenee a son support) ; chacun note toutes les
            // secondes la pose de la voiture. Attendu : la voiture ne bouge pas chez l'invite (copie de l'objet tenu).
            if (mode == "tenirvoiture")
            {
                string id = Config.Get("Test", "TestPiece", "~sandbag(item1)");
                if (id.StartsWith("~")) id = Props.FindKey(id.Substring(1)) ?? id;
                Rigidbody car = VehicleSync.Body(Config.Get("Test", "TestVoiture", "SORBET(190-200psi)"));
                if (Net.Session.IsHost && car != null)
                {
                    if (t > 40f && t < 55f)
                    {
                        if (holdCam == null) holdCam = new GameObject("MWCoop-EssaiMain").transform;
                        holdCam.rotation = car.rotation;
                        holdCam.position = car.worldCenterOfMass + car.transform.up * 0.45f - car.transform.forward * 0.7f;   // (objet pose 0,7 m devant, 0,25 m sous : au centre)
                        Rigidbody hb = Props.TestHold(id, holdCam);
                        if (hb != null) hb.detectCollisions = false;   // (ici l'objet de l'essai est cinematique, pas tenu par la main du jeu : il pousserait la voiture de l'hote)
                        if (step == 0) { step = 1; Log.Info("autotest : tenirvoiture, tient " + id + " dans " + car.name); }
                    }
                    if (t > 55f && step == 1) { step = 2; Rigidbody hb = Props.TestHold(id, null); if (hb != null) hb.detectCollisions = true; Log.Info("autotest : tenirvoiture, lache"); }
                }
                if (car != null && t > 35f && t < 70f && t >= holdLog) { holdLog = t + 1f; Log.Info("autotest : tenirvoiture, " + car.name + " en " + car.position.ToString("F2") + " rot " + car.rotation.eulerAngles.ToString("F0") + " v " + car.velocity.magnitude.ToString("F2")); }
            }
            if (mode == "ranger")
            {
                string id = Config.Get("Test", "TestPiece", "~sandbag(item1)");
                if (id.StartsWith("~")) id = Props.FindKey(id.Substring(1)) ?? id;
                bool storer = Net.Session.IsHost == (Config.Get("Test", "Rangeur", "hote") != "invite");
                bool inHand = Config.GetInt("Test", "RessortirMain", 0) != 0;
                if (storer)
                {
                    Transform cam = PlayerSync.LocalCamera;
                    if (t > 40f && t < 50f && cam != null) { Props.TestHold(id, cam); if (step == 0) { step = 1; Log.Info("autotest : ranger, tient " + id); } }
                    if (t > 50f && step == 1) { step = 2; Rigidbody rb = Props.BodyOf(id); if (rb != null) { rangeGo = rb.gameObject; rangeGo.SetActive(false); } Log.Info("autotest : ranger, eteint en main"); }
                    if (t > 52f && step == 2 && !inHand) { step = 3; Props.TestHold(id, null); }
                    if (inHand && t > 70f && step == 2 && rangeGo != null && cam != null) { step = 5; rangeGo.SetActive(true); Log.Info("autotest : ranger, rallume dans la main"); }
                    if (inHand && step == 5 && t < 76f && cam != null) Props.TestHold(id, cam);
                    if (inHand && step == 5 && t >= 76f) { step = 6; Props.TestHold(id, null); Log.Info("autotest : ranger, lache"); }
                    if (t >= holdLog && t > 44f && t < 100f) { holdLog = t + 2f; Rigidbody rb2 = Props.BodyOf(id); Log.Info("autotest : ranger, chez le rangeur " + (rb2 == null ? "sans corps" : (rb2.gameObject.activeInHierarchy ? "visible en " + rb2.position.ToString("F1") : "cache"))); }
                    if (t > 70f && step == 3 && rangeGo != null && cam != null)
                    {
                        step = 4;
                        Vector3 f = cam.forward; f.y = 0;
                        rangeGo.transform.position = cam.position + f.normalized * 1.5f;
                        rangeGo.SetActive(true);
                        Rigidbody rb = rangeGo.GetComponent<Rigidbody>(); if (rb != null) rb.isKinematic = false;
                        Log.Info("autotest : ranger, ressorti en " + rangeGo.transform.position.ToString("F1"));
                    }
                }
                else if (t >= holdLog && t < 140f)
                {
                    holdLog = t + 2f;

                    Rigidbody rb = Props.BodyOf(id);
                    Log.Info("autotest : ranger, objet " + (rb == null ? "sans corps ici" : (rb.gameObject.activeInHierarchy ? "visible en " + rb.position.ToString("F1") : "cache")));
                }
            }
            // [Test] Autotest=poro (instances avec le mod Okkelmo Poro) : l'hote monte sur la motoneige a 60 s comme le mod
            // (joueur range sous PORO, PlayerCurrentVehicle "Poro"), la pousse a 4 m/s de 65 a 80 s, descend a 90 s.
            // Chacun note toutes les 2 s la position de PORO et si c'est une copie.
            if (mode == "poro")
            {
                GameObject poro = GameObject.Find("PORO");
                Rigidbody pb = poro != null ? poro.GetComponent<Rigidbody>() : null;
                if (Net.Session.IsHost && pb != null)
                {
                    Transform pl = Game.PlayerT;
                    if (t > 60f && step == 0 && pl != null)
                    {
                        step = 1;
                        var cc = pl.GetComponent<CharacterController>(); if (cc != null) cc.enabled = false;
                        pl.position = poro.transform.TransformPoint(new Vector3(0f, 0.9f, -0.3f));
                        pl.parent = poro.transform;
                        Game.SetGlobal("PlayerCurrentVehicle", "Poro");
                        Log.Info("autotest : poro, monte ; moteur " + VehicleSync.ModEngine("PORO", true));
                    }
                    if (t > 65f && t < 80f) { Vector3 f = poro.transform.forward; f.y = 0; pb.velocity = f.normalized * 4f + Vector3.up * Mathf.Min(pb.velocity.y, 0f); }
                    if (t > 90f && step == 1 && pl != null)
                    {
                        step = 2;
                        pl.parent = null;
                        pl.position += Vector3.up * 0.3f + poro.transform.right * 1.2f;
                        var cc = pl.GetComponent<CharacterController>(); if (cc != null) cc.enabled = true;
                        Game.SetGlobal("PlayerCurrentVehicle", "");
                        Log.Info("autotest : poro, descendu");
                    }
                }
                if (pb != null && t > 20f && t < 160f && Time.realtimeSinceStartup >= rivLog)
                {
                    rivLog = Time.realtimeSinceStartup + 2f;
                    Log.Info("autotest : poro en " + pb.position.ToString("F1") + (pb.isKinematic ? " (copie)" : " (locale)") + " v " + pb.velocity.magnitude.ToString("F1") + ", " + VehicleSync.ModEngine("PORO", false));
                }
                if (pb == null && t > 20f && step == 0) { step = 9; Log.Info("autotest : poro, pas de PORO ici"); }
            }
            // [Test] Autotest=rivettvente (TestPos pres de JOBS/HouseWood1) : l'achat de la Rivett, par le vrai chemin. A 40 s
            // l'hote appelle Reijo (CALLED sur CARPARTS/PARTSYSTEM/PhoneNumbers/08609553::Data -> SELLCAR -> WoodJob1Point
            // "State 1" : flechette Wood1Car, vendeur) ; a 60 s de SA montre l'acheteur ([Test] Acheteur=invite|hote, invite par
            // defaut) paie ("Pay for car" de PayMoney::Use, comme le clic : -500 mk, PAID -> Give keys -> ORDERTAKEN ->
            // WoodJob1Point "Activate order" : flechette eteinte). Chacun note toutes les 3 s flechettes, vendeur, PayMoney,
            // WoodJob1Point (Car, Order), etape du numero.
            if (mode == "rivettvente")
            {
                GameObject dart = Game.FindAny("YARD/Building/MAP/Darts/Wood1Car"), dartW = Game.FindAny("YARD/Building/MAP/Darts/Wood1");
                GameObject seller = Game.FindAny("JOBS/HouseWood1/LOD/CarPos/NPCWood/WoodCaller1"), job = Game.FindAny("JOBS/HouseWood1/WoodJob1Point");
                GameObject payGo = Game.FindAny(PayPath);
                PlayMakerFSM anim = seller != null ? Game.FsmOn(seller, "Animations") : null, logic = job != null ? Game.FsmOn(job, "Logic") : null;
                PlayMakerFSM pay = payGo != null ? Game.FsmOn(payGo, "Use") : null;
                bool buyer = Config.Get("Test", "Acheteur", "invite") == (Net.Session.IsHost ? "hote" : "invite");
                if (Net.Session.IsHost && t > 40f && step == 0)
                {
                    step = 1;
                    PlayMakerFSM num = null;
                    foreach (Object o in Game.AllFsms())
                    {
                        var f = (PlayMakerFSM)o;
                        if (f != null && f.FsmName == "Data" && f.transform.parent != null && f.transform.parent.name == "PhoneNumbers" && (f.gameObject.name == "08609553" || f.gameObject.name == "numberdisabled"))
                        {
                            FsmString nb = f.FsmVariables.FindFsmString("Number");
                            if (nb != null && nb.Value == "08609553") num = f;
                        }
                    }
                    if (num != null) num.SendEvent("CALLED");
                    Log.Info("autotest : rivettvente, appel de Reijo -> " + (num != null ? num.gameObject.name + " " + num.ActiveStateName : "numero introuvable"));
                }
                // [Test] VentePres=x,y,z : l'acheteur y va a 52 s (pres du vendeur ; TestPos a plus de 50 m de lui : le jeu ne
                // l'allume qu'a un joueur parti a plus de 50 m, de 8 a 20 h). VenteForcer=0 : vendeur pas allume de force.
                // [Test] VenteTemoin=x,y,z : l'autre joueur y va aussi (a quelques metres : voit la scene sans la declencher).
                string near = Config.Get("Test", buyer ? "VentePres" : "VenteTemoin", "");
                if (near.Length > 0 && t > 52f && !rivNear)
                {
                    rivNear = true;
                    string[] c = near.Split(',');
                    var ci = System.Globalization.CultureInfo.InvariantCulture;
                    GameObject pl = Game.PlayerGo;
                    var cc = pl.GetComponent<CharacterController>();
                    cc.enabled = false; pl.transform.position = new Vector3(float.Parse(c[0], ci), float.Parse(c[1], ci), float.Parse(c[2], ci)); cc.enabled = true;
                    Log.Info("autotest : rivettvente, " + (buyer ? "acheteur" : "temoin") + " pres du vendeur (" + near + ")");
                }
                if (buyer && t > Config.GetFloat("Test", "VentePaie", 60f) && (step == 0 || step == 1))
                {
                    step = 2;
                    if (Config.GetInt("Test", "VenteForcer", 1) != 0)
                        for (Transform x = seller != null ? seller.transform : null; x != null; x = x.parent) if (!x.gameObject.activeSelf) { Log.Info("autotest : rivettvente, allume " + x.name); x.gameObject.SetActive(true); }
                    if (payGo != null && !payGo.activeSelf) payGo.SetActive(true);
                    if (pay != null) Game.SetState(pay, "Pay for car");
                    Log.Info("autotest : rivettvente, paie le vendeur -> PayMoney " + (pay != null ? pay.ActiveStateName : "introuvable") + ", vendeur " + (anim != null ? anim.ActiveStateName : "?"));
                }
                if (t > 30f && t < 300f && Time.realtimeSinceStartup >= rivLog)
                {
                    rivLog = Time.realtimeSinceStartup + 3f;
                    FsmBool car = logic != null ? logic.FsmVariables.FindFsmBool("Car") : null, order = logic != null ? logic.FsmVariables.FindFsmBool("Order") : null;
                    Log.Info("autotest : rivettvente, flechette voiture " + (dart == null ? "?" : dart.activeSelf ? "ALLUMEE" : "eteinte") + ", bois " + (dartW == null ? "?" : dartW.activeSelf ? "allumee" : "eteinte")
                             + ", vendeur " + (anim != null ? anim.ActiveStateName + (anim.enabled ? "" : " (coupe)") + (anim.gameObject.activeInHierarchy ? "" : " (eteint)") : "?")
                             + ", PayMoney " + (pay != null ? pay.ActiveStateName + (payGo.activeInHierarchy ? "" : " (eteint)") : "?")
                             + ", WoodJob1Point " + (logic != null ? logic.ActiveStateName : "?") + " Car " + (car != null && car.Value) + " Order " + (order != null && order.Value));
                }
            }
            // [Test] Autotest=teleinvite ([Test] TeleQui=invite|hote) : ce joueur allume la tele du salon a 30 s ; chacun note
            // toutes les 3 s de 28 a 80 s la grille (TVPrograms :: Schedule, ADs : etat, actif) et l'ecran (enfants actifs).
            if (mode == "teleinvite")
            {
                bool me = Net.Session.IsHost == (Config.Get("Test", "TeleQui", "invite") == "hote");
                if (me && t > 30f && step == 0)
                {
                    step = 1;
                    GameObject sw = Game.FindAny("YARD/Building/LIVINGROOM/TV/TVSwitch");
                    PlayMakerFSM u = sw != null ? Game.FsmOn(sw, "Use") : null;
                    if (u != null) { Game.SetState(u, "Switch"); Log.Info("autotest : tele allumee ici -> " + u.ActiveStateName); }
                }
                // (photos de l'ecran : camera d'essai a 1,2 m de TVScreen, sur ses 4 cotes, a 45-48 s)
                for (int k = 0; k < 4; k++)
                    if (t > 45f + k && step3 == k)
                    {
                        step3 = k + 1;
                        GameObject scr = null, tvr = Game.FindAny("YARD/Building/LIVINGROOM/TV");
                        if (tvr != null) foreach (Transform c in tvr.GetComponentsInChildren<Transform>(true)) if (c.name == "TVScreen") { scr = c.gameObject; break; }
                        if (scr == null) { Log.Info("autotest : tele, TVScreen introuvable"); break; }
                        if (testCam == null) { testCam = new GameObject("MWCoop-CameraEssai").AddComponent<Camera>(); testCam.depth = 100; testCam.nearClipPlane = 0.02f; testCam.fieldOfView = 50f; }
                        Transform st = scr.transform;
                        Vector3 n = k == 0 ? st.forward : k == 1 ? -st.forward : k == 2 ? st.right : -st.right;
                        testCam.transform.position = st.position + n * 1.2f;
                        testCam.transform.rotation = Quaternion.LookRotation(st.position - testCam.transform.position, Vector3.up);
                        CaptureSoon("tele-" + k, 0.4f);
                        Log.Info("autotest : tele, photo " + k + " (" + Recon.Path(st) + ")");
                    }
                if (t > 40f && step2 == 0)
                {
                    step2 = 1;
                    foreach (Object o in Game.AllFsms())
                    {
                        var f = (PlayMakerFSM)o;
                        if (f == null || f.hideFlags != HideFlags.None || f.gameObject.name != "TVPrograms" || f.FsmName != "Schedule") continue;
                        var names = new System.Text.StringBuilder();
                        foreach (HutongGames.PlayMaker.FsmState st in f.FsmStates) names.Append(st.Name).Append(", ");
                        Log.Info("autotest : grille, etats : " + names);
                        foreach (string st in new[] { f.ActiveStateName, "State 1", "Start", "Check time", "Time" }) Log.Info("autotest : grille, " + Jobs.DumpActions(f, st));
                        break;
                    }
                }
                if (t > 28f && t < 80f && t >= ttLog2)
                {
                    ttLog2 = t + 3f;
                    var sb = new System.Text.StringBuilder("autotest : tele");
                    foreach (Object o in Game.AllFsms())
                    {
                        var f = (PlayMakerFSM)o;
                        if (f == null || f.hideFlags != HideFlags.None || f.gameObject.name != "TVPrograms") continue;
                        sb.Append(" | ").Append(Recon.Path(f.transform)).Append("::").Append(f.FsmName).Append(" [").Append(f.ActiveStateName).Append("]").Append(f.enabled ? "" : " COUPE").Append(f.gameObject.activeInHierarchy ? "" : " eteint");
                    }
                    GameObject tv = Game.FindAny("YARD/Building/LIVINGROOM/TV");
                    if (tv != null) foreach (Transform c in tv.GetComponentsInChildren<Transform>(false)) if (c.name.ToLowerInvariant().Contains("screen") || c.name.ToLowerInvariant().Contains("noise") || c.name.ToLowerInvariant().Contains("static")) sb.Append(" | ").Append(c.name).Append(" on");
                    Log.Info(sb.ToString());
                }
            }
            // [Test] Autotest=particules : de 30 a 90 s, toutes les secondes, le plus grand nombre de particules vivantes de
            // chaque emetteur actif (ParticleSystem et ParticleEmitter) ; a 92 s, ceux qui ont emis, et le nombre d'emetteurs
            // actifs muets. A comparer chez l'hote et l'invite (retour de JD, 14/10 : fumee, poussiere du trafic absentes chez l'invite).
            if (mode == "particules")
            {
                if (t > 30f && t < 150f && t >= partNext)
                {
                    partNext = t + 1f;
                    foreach (ParticleSystem ps in Object.FindObjectsOfType<ParticleSystem>())
                    {
                        string k = Recon.Path(ps.transform) + " [S]";
                        int c; partMax.TryGetValue(k, out c); partMax[k] = Mathf.Max(c, ps.particleCount);
                    }
                    foreach (ParticleEmitter pe in Object.FindObjectsOfType<ParticleEmitter>())
                    {
                        string k = Recon.Path(pe.transform) + " [E" + (pe.emit ? "+" : "-") + "]";
                        int c; partMax.TryGetValue(k, out c); partMax[k] = Mathf.Max(c, pe.particleCount);
                    }
                }
                if (t > 36f && step3 == 0 && Net.Session.IsHost)
                {
                    step3 = 1;
                    GameObject hp = Game.FindAny("TRAFFIC/VehiclesDirtRoad/Rally/HEPPA");
                    PlayMakerFSM th = hp != null ? Game.FsmOn(hp, "Throttle") : null;
                    if (th != null) foreach (string st in new[] { "Accelerate", "Cruise", "Cruise 2", "Other", "State 1", "State 2", "State 3" }) Log.Info("autotest : particules, Throttle " + Jobs.DumpActions(th, st));
                    PlayMakerFSM sh = hp != null ? Game.FsmOn(hp, "Shifting") : null;
                    if (sh != null) foreach (string st in new[] { "State 2", "State 3" }) Log.Info("autotest : particules, Shifting " + Jobs.DumpActions(sh, st));
                }
                if (t > 35f && step2 == 99)
                {
                    // qui commande ces emetteurs : automates (de la meme racine) dont une action vise l'emetteur ou son parent
                    step2 = 1;
                    var sb2 = new System.Text.StringBuilder("autotest : particules, commandes :");
                    var targets = new System.Collections.Generic.List<GameObject>();
                    foreach (ParticleEmitter pe in Resources.FindObjectsOfTypeAll(typeof(ParticleEmitter)))
                        if (pe != null && pe.hideFlags == HideFlags.None && (pe.name == "RoosterTail" || pe.name.StartsWith("SnowCloud") || pe.name == "ExhaustParticle" || pe.name.StartsWith("SmokeHomeKiuas") || pe.transform.parent != null && pe.transform.parent.name == "Kiuas")) targets.Add(pe.gameObject);
                    var seen = new System.Collections.Generic.HashSet<string>();
                    foreach (GameObject tg in targets)
                    {
                        Transform root = tg.transform;
                        for (int up = 0; up < 3 && root.parent != null; up++) root = root.parent;   // (pas toute la racine : YARD figeait le jeu)
                        foreach (PlayMakerFSM f in root.GetComponentsInChildren<PlayMakerFSM>(true))
                        {
                            HutongGames.PlayMaker.FsmState[] sts;
                            try { sts = f.FsmStates; } catch { continue; }
                            foreach (HutongGames.PlayMaker.FsmState st in sts)
                            {
                                HutongGames.PlayMaker.FsmStateAction[] acts;
                                try { acts = st.Actions; } catch { continue; }
                                foreach (HutongGames.PlayMaker.FsmStateAction ac in acts ?? new HutongGames.PlayMaker.FsmStateAction[0])
                                {
                                    if (ac == null) continue;
                                    bool hit = false;
                                    foreach (System.Reflection.FieldInfo fi in ac.GetType().GetFields())
                                    {
                                        object v = null; try { v = fi.GetValue(ac); } catch { }
                                        GameObject go = null;
                                        var od = v as HutongGames.PlayMaker.FsmOwnerDefault; if (od != null) go = od.OwnerOption == HutongGames.PlayMaker.OwnerDefaultOption.UseOwner ? f.gameObject : od.GameObject != null ? od.GameObject.Value : null;
                                        var fg = v as HutongGames.PlayMaker.FsmGameObject; if (fg != null) go = fg.Value;
                                        var fp = v as HutongGames.PlayMaker.FsmProperty; if (fp != null && fp.TargetObject != null) { Component cp = fp.TargetObject.Value as Component; go = cp != null ? cp.gameObject : fp.TargetObject.Value as GameObject; }
                                        if (go != null && (go == tg || tg.transform.IsChildOf(go.transform))) hit = true;
                                    }
                                    if (!hit) continue;
                                    string k = Recon.Path(f.transform) + "::" + f.FsmName + " / " + st.Name + " : " + ac.GetType().Name + " -> " + tg.name;
                                    if (seen.Add(k)) sb2.Append(" | ").Append(k).Append(f.enabled ? "" : " (COUPE)").Append(" [").Append(f.ActiveStateName).Append("]");
                                }
                            }
                        }
                    }
                    Log.Info(sb2.ToString());
                }
                if (t > 40f && Time.frameCount % 900 == 0) Log.Info("autotest : particules, lumieres " + Particles.LightReport());
                if (t > 152f && step == 0)
                {
                    step = 1;
                    var sb = new System.Text.StringBuilder("autotest : particules, emetteurs actifs " + partMax.Count + " ; ont emis :");
                    var keys = new System.Collections.Generic.List<string>(partMax.Keys); keys.Sort();
                    foreach (string k in keys) if (partMax[k] > 0) sb.Append(" | ").Append(k).Append(" = ").Append(partMax[k]);
                    sb.Append(" || muets :");
                    foreach (string k in keys) if (partMax[k] == 0) sb.Append(" | ").Append(k);
                    Log.Info(sb.ToString());
                }
            }
            if (mode == "sangsonde" && t > 25f && !done) { done = true; Log.Info("autotest : " + Respawn.ProbeBlood()); }
            if (mode == "pointsonde" && t > 30f && !done) { done = true; Log.Info("autotest : pointsonde " + Parts.DumpPoint(Config.Get("Test", "TestPoint", "CORRIS/Assemblies/VINP_Grille"))); Log.Info("autotest : pieces " + Parts.ListIds()); }
            if (mode == "sonsonde" && t > 30f && Time.frameCount % 600 == 0) Log.Info("autotest : " + SaunaProbe.ProbeSounds() + " || " + CopySounds.Report());
            if (mode == "kiljusonde" && t > 25f && !done) { done = true; Log.Info("autotest : " + SaunaProbe.ProbeKilju()); }
            if (mode == "vapeursonde" && t > 25f && !done) { done = true; Log.Info("autotest : " + SaunaProbe.Probe()); Log.Info("autotest : " + SaunaProbe.Probe2()); }
            if (mode == "porter")
            {
                // [Test] TestPiece promenee de 30 a 36 s (comme tenue en main), position a 45 s.
                string id = Config.Get("Test", "TestPiece", "VIN2121");
                if (id.StartsWith("~")) id = Props.FindKey(id.Substring(1));
                if (t > 30f && t < 36f) { string w = Props.TestCarry(id, t); if (Time.frameCount % 60 == 0) Log.Info("autotest : " + w); }
                if (t > 45f && !done) { done = true; Log.Info("autotest : " + id + " finit en " + Props.Where(id)); }
            }
            // Autotest "cameras" : toutes les cameras (chemin, allumee, texture cible) a pied a 20 s, puis au volant de
            // [Test] TestVoiture a 32 s (retroviseurs : qui dessine dans quelle texture).
            if (mode == "cameras")
            {
                string car = Config.Get("Test", "TestVoiture", "SORBET(190-200psi)");
                if (t > 20f && step == 0) { step = 1; DumpCameras("a pied"); VehicleSync.TestEnter(car, false); }
                if (t > 23f && step == 1) { step = 2; VehicleSync.TestEnter(car, true); }
                if (t > 25f && step == 2) { step = 3; VehicleSync.TestEnter(car, true); }
                if (t > 32f && step == 3) { step = 4; DumpCameras("au volant de " + car); }
            }
            // Autotest "taximiroir" (2 joueurs) : a 30 s l'hote met le PlayerTrigger du taxi en "Press return" (comme s'il y montait) ;
            // a 38 s chacun note ou sont les cameras des retroviseurs (attendu chez l'invite : Systems/Mirrors/Disable).
            if (mode == "taximiroir")
            {
                if (t > 30f && step == 0) { step = 1; if (MWCoop.Net.Session.IsHost) { PlayMakerFSM f = Game.FindFsm("JOBS/TAXIJOB/MACHTWAGEN/Functions/PlayerTrigger", "PlayerTrigger"); Log.Info("autotest : taximiroir, taxi " + (f != null ? f.ActiveStateName + " -> Press return" : "introuvable")); if (f != null) Game.SetState(f, "Press return"); } }
                if (t > 38f && step == 1) { step = 2; var sb = new System.Text.StringBuilder("autotest : taximiroir, cameras :"); foreach (string n in new[] { "LeftSideMirrorCam", "RightSideMirrorCam", "RearViewMirrorCam" }) { foreach (Camera c in Resources.FindObjectsOfTypeAll<Camera>()) if (c.name == n && c.transform.parent != null) sb.Append(' ').Append(n).Append(" sous ").Append(Recon.Path(c.transform.parent)).Append(';'); } Log.Info(sb.ToString()); }
            }
            // [Test] HeureCapture=<h> : l'heure du jeu posee a 10 s, tous modes (captures en plein jour).
            if (t > 10f && !hourSet && Config.GetInt("Test", "HeureCapture", -1) >= 0)
            {
                hourSet = true;
                PlayMakerFSM sun = Game.FindFsm("MAP/Sun/PivotSun/SUN", "Color");
                if (sun != null) { sun.FsmVariables.GetFsmInt("Time").Value = Config.GetInt("Test", "HeureCapture", 12); sun.SendEvent("TIMESKIP"); }
                Log.Info("autotest : heure posee a " + Config.GetInt("Test", "HeureCapture", 12) + " h" + (sun == null ? " (soleil introuvable)" : ""));
            }
            // [Test] TestPos=x,y,z : le joueur y est teleporte a 8 s (recos, scenes a cote d'un lieu).
            string tp = Config.Get("Test", "TestPos", "");
            // [Test] Autotest=kiosque, sans TestPos : les deux joueurs vont a 8 s devant le kiosque a saucisses du rallye
            // (le reste de l'essai -- jour, heure, achat de l'hote, releves -- est dans Shop.TestKiosque).
            if (mode == "kiosque" && tp.Length == 0 && t > 8f && !teleported) { teleported = true; Shop.TestKioskGo(); }
            if (tp.Length > 0 && t > 8f && !teleported)
            {
                teleported = true;
                string[] c = tp.Split(',');
                GameObject pl = Game.PlayerGo;
                var cc = pl.GetComponent<CharacterController>();
                cc.enabled = false;
                pl.transform.position = new Vector3(float.Parse(c[0], System.Globalization.CultureInfo.InvariantCulture),
                    float.Parse(c[1], System.Globalization.CultureInfo.InvariantCulture), float.Parse(c[2], System.Globalization.CultureInfo.InvariantCulture));
                cc.enabled = true;
                Log.Info("autotest : teleporte en " + tp);
            }
            if (mode == "courses" && t > 30f && !done)
            {
                done = true;
                Log.Info("autotest : " + Shop.TestBuy(Config.Get("Test", "TestProduit", "Beer"), 2));
            }
            if (mode == "poses")
            {
                // Une posture toutes les 8 s a partir de 20 s : accroupi, assis, porte, boit, fume, salue, debout.
                int[] seq = { PlayerSync.F_Crouch, PlayerSync.F_Seated, PlayerSync.F_Carry, PlayerSync.F_Drink,
                              PlayerSync.F_Smoke, PlayerSync.F_Hello, 0 };
                int i = t < 20f ? -1 : (int)((t - 20f) / 8f);
                int fl = i >= 0 && i < seq.Length ? seq[i] : 0;
                if (fl != PoseFlags) { PoseFlags = fl; Log.Info("autotest : posture " + fl); }
            }
            if (mode == "courses")
            {
                // Apres l'achat (30 s) : le sac est promene de 36 a 42 s (comme porte), objets a 50 s.
                if (t > 36f && t < 42f)
                {
                    if (bagId == null) { bagId = Props.NearestId("shoppingbag", Game.PlayerGo.transform.position); Log.Info("autotest : sac " + bagId); }
                    if (bagId != null) Props.TestCarry(bagId, t);
                }
            }
            if ((mode == "courses" || mode == "") && t > 50f && step == 0 && Config.Get("Test", "TestPos", "").Length > 0)
            {
                step = 1;
                Log.Info("autotest : objets autour : " + Props.Near(Game.PlayerGo.transform.position, 8f));
            }
            if (mode == "argent")
            {
                // Paie de 500 a 25 s (partagee), achat de 120 a 30 s (local), etat a 40 s.
                if (t > 25f && step == 0) { step = 1; Log.Info("autotest : paie, " + Wallet.Test(500)); }
                if (t > 30f && step == 1) { step = 2; Log.Info("autotest : achat, " + Wallet.Test(-120)); }
                if (t > 40f && step == 2) { step = 3; Log.Info("autotest : " + Wallet.State()); }
            }
            if (mode == "etatargent" && t > 35f && !done) { done = true; Log.Info("autotest : " + Wallet.State()); }
            if (mode == "ou" && t > 40f && !done) { done = true; string id = Config.Get("Test", "TestPiece", "VIN2121"); Log.Info("autotest : " + id + " finit en " + Props.Where(id)); }
            if (mode == "etatvis" && t > 40f && !done) { done = true; Log.Info("autotest : vis " + Parts.BoltState(Config.Get("Test", "TestPiece", ""))); }
            if (mode == "sauver" && (t > 55f && step == 0 || t > 75f && step == 1 || t > 95f && step == 2 && Config.Get("Test", "Autotest2", "") == "porte"))
            {
                // Etat des automates Use (articles) avant et apres SAVEGAME : le jeu ne sauve qu'en quittant.
                step++;
                var sb = new System.Text.StringBuilder("autotest : automates Use " + (step == 1 ? "avant" : "apres") + " SAVEGAME :");
                foreach (PlayMakerFSM f in Object.FindObjectsOfType<PlayMakerFSM>())
                    if (f.FsmName == "Use" || f.FsmName == "Data" && f.gameObject.name.Contains("(itemx)"))
                        sb.Append(' ').Append(f.gameObject.name).Append('=').Append(f.ActiveStateName).Append(';');
                Log.Info(sb.ToString());
                Log.Info("autotest : " + (step == 1 ? "avant" : "apres") + " SAVEGAME, " + Parts.Census());
            }
            if (mode == "sauver" && t > 60f && !done)
            {
                // Sauvegarde complete comme le jeu (1482 automates ecoutent SAVEGAME).
                done = true;
                Game.SaveInPlace();
                Log.Info("autotest : SAVEGAME envoye");
            }
            if (mode == "tchat" && t > 12f && !done)
            {
                done = true;
                Chat.Send("Salut, c'est " + Net.Session.Me.Name + " !");
            }
            // [Test] TestNourriture=Sausages : le jeu cree ce produit a 25 s (meme compteur, meme ID des deux cotes).
            string food = Config.Get("Test", "TestNourriture", "");
            if (food.Length > 0 && t > 25f && !foodMade)
            {
                foodMade = true;
                foreach (PlayMakerFSM f in Object.FindObjectsOfType<PlayMakerFSM>())
                    if (f.FsmName == food && f.gameObject.name == "CreateItems")
                    {
                        f.SendEvent("SPAWNITEM");
                        GameObject made = f.FsmVariables.GetFsmGameObject("New").Value;
                        Log.Info("autotest : produit cree " + (made != null ? made.name + " " + Props.ItemId(made) : "?"));
                    }
            }
            if (mode == "monde_send" && t > 35f && !done)
            {
                // [Test] TestEnvoi=partie|etat avant|evenement|etat apres
                done = true;
                string[] ps = Config.Get("Test", "TestEnvoi", "OpenMailBox|State 2|CLICK|Open").Split('|');
                Log.Info("autotest : " + WorldFsms.TestSend(ps[0], ps[1], ps[2], ps[3]));
            }
            if (mode == "telephone" && MWCoop.Net.Session.IsHost)
            {
                // L'hote fait sonner le telephone (etat [Test] TestEtat), puis decroche.
                string logic = Config.Get("Test", "TestQuete", "PhoneLogicNEW::Ring");
                if (t > 35f && step == 0) { step = 1; Log.Info("autotest : " + WorldFsms.TestState(logic, Config.Get("Test", "TestEtat", "Joke"))); }
                if (t > 50f && step == 1) { step = 2; Log.Info("autotest : " + WorldFsms.Var(logic, "Wait") + " ; " + WorldFsms.TestState(logic, "Ring")); }
                if (t > 56f && step == 2 && Config.Get("Test", "VidageSonnerie", "").Length > 0) { Log.Info("autotest : vidage " + Recon.DumpTargets(Config.Get("Test", "VidageSonnerie", ""))); step = 21; }
                if (t > 62f && (step == 2 || step == 21)) { step = 3; Log.Info("autotest : " + WorldFsms.TestEvent(Config.Get("Test", "TestSonnerie", "RingingNEW::Ring"), "ANSWER")); }
            }
            if (mode == "colis")
            {
                // Commande AMIS (hote), commande prete des deux cotes, guichet de la poste actif, puis
                // l'hote paie au guichet : le colis doit apparaitre chez les deux.
                bool host = MWCoop.Net.Session.IsHost;
                if (host && t > 35f && step == 0) { step = 1; Log.Info("autotest : " + WorldFsms.TestEvent("OrdersSpawnerAMIS", "SPAWNITEM")); }
                if (!host && t > 35f && step == 0) { step = 1; if (Config.GetInt("Test", "ColisOuvrir", 0) != 0) Log.Info("autotest : " + WorldFsms.TestEvent("OrdersSpawnerAMIS", "SPAWNITEM")); }   // (ouverture : la meme commande chez l'invite)
                if (t > 50f && step == 1)
                {
                    step = 2;
                    GameObject pile = Game.FindAny("PERAPORTTI/ActiveFunctions/Store/PostOffice/NotificationsPile");
                    if (pile != null) pile.SetActive(true);
                    string orders = "";
                    foreach (Object o in Resources.FindObjectsOfTypeAll(typeof(GameObject)))
                    {
                        var g = (GameObject)o;
                        if (g.hideFlags == HideFlags.None && g.name.StartsWith("OrderAMIS") && g.name != "OrderAMIS" && g.transform.root.gameObject.activeInHierarchy)
                        { orders += Recon.Path(g.transform) + (g.activeInHierarchy ? "" : " (inactif)") + " "; if (orders.Length < 300) Log.Info("autotest : " + WorldFsms.TestState(Recon.Path(g.transform) + "::Data", "Idle")); }
                    }
                    Log.Info("autotest : guichet " + (pile != null) + (pile != null ? " actif " + pile.activeInHierarchy : "") + " ; commandes " + orders);
                }
                if (host && t > 62f && step == 2)
                {
                    step = 3;
                    GameObject pile = Game.FindAny("PERAPORTTI/ActiveFunctions/Store/PostOffice/NotificationsPile");
                    PlayMakerFSM pf = pile != null ? Game.FsmOn(pile, "Use") : null;
                    if (pf != null)
                    {
                        Log.Info("autotest : guichet enabled " + pf.enabled + ", demarre " + pf.Fsm.Started + ", fini " + pf.Fsm.Finished + ", etat '" + pf.ActiveStateName + "'");
                        pf.enabled = true;
                    }
                    Log.Info("autotest : " + WorldFsms.TestState("NotificationsPile::Use", "Wait button") + " ; " + WorldFsms.TestEvent("NotificationsPile::Use", "PAY"));
                }
            }
            if (mode == "colis")
            {
                // ... puis l'hote l'ouvre a 85 s (comme un clic) : il doit s'ouvrir chez les deux ([Test] ColisOuvrir=1).
                if (Config.GetInt("Test", "ColisDistributeurs", 0) != 0 && t > 40f && step2 == 0) { step2 = 9; Packages.DumpSpawners(); }
                if (Config.GetInt("Test", "ColisOuvrir", 0) != 0)
                {
                    if (t > 70f && step2 == 0) { step2 = 1; Log.Info("autotest : colis cree " + WorldFsms.TestState("OrderAMIS1::Data", "Spawn package")); }
                    if (MWCoop.Net.Session.IsHost && t > 85f && step2 == 1) { step2 = 2; Log.Info("autotest : " + Packages.TestOpen()); }
                    if (t > 80f && Time.frameCount % 300 == 0 && t < 130f) Log.Info("autotest : " + Packages.State());
                }
            }
            if (mode == "monde_evt" && t > 35f && !done)
            {
                done = true;
                Log.Info("autotest : " + WorldFsms.TestEvent(Config.Get("Test", "TestQuete", "ElectricityBills1"), Config.Get("Test", "TestEvenement", "GLOBALEVENT")));
            }
            string countWatch = Config.Get("Test", "CompterObjets", "");
            if (countWatch.Length > 0 && Time.frameCount % 600 == 0 && t > 20f)
            {
                int cnt = 0; string names = "";
                foreach (Object o in Resources.FindObjectsOfTypeAll(typeof(GameObject)))
                {
                    var g = (GameObject)o;
                    if (g.hideFlags == HideFlags.None && g.name.StartsWith(countWatch) && g.transform.root.gameObject.activeInHierarchy) { cnt++; if (names.Length < 200) names += g.name + " "; }
                }
                Log.Info("autotest : " + cnt + " objets " + countWatch + "* : " + names);
            }
            if (mode == "sac" && MWCoop.Net.Session.IsHost && t > 30f && step == 0) { step = 1; Log.Info("autotest : " + Shop.TestBuy(Config.Get("Test", "TestProduit", "Sausages"), 2)); }
            if (mode == "sac" && MWCoop.Net.Session.IsHost && t > 42f && step == 1 && Config.Get("Test", "VidageSac", "").Length > 0) { step = 11; Log.Info("autotest : vidage " + Recon.DumpTargets(Config.Get("Test", "VidageSac", ""))); }
            if (mode == "sac" && MWCoop.Net.Session.IsHost && t > 48f && (step == 1 || step == 11) && Config.GetInt("Test", "SacOuvrir", 1) != 0) { step = 2; Log.Info("autotest : " + Consume.TestOpenBag()); }
            if (mode == "fume")
            {
                // Cigarette : en main 20-30 s, tire 30-37 s, souffle 37-41 s, en main, tire 46-50, souffle 50-53.
                int fl = t < 20f ? 0 : t < 30f ? PlayerSync.F_Smoke : t < 37f ? PlayerSync.F_Smoke | PlayerSync.F_Inhale : t < 41f ? PlayerSync.F_Smoke | PlayerSync.F_Exhale
                         : t < 46f ? PlayerSync.F_Smoke : t < 50f ? PlayerSync.F_Smoke | PlayerSync.F_Inhale : t < 53f ? PlayerSync.F_Smoke | PlayerSync.F_Exhale : 0;
                if (fl != PoseFlags) { PoseFlags = fl; Log.Info("autotest : cigarette " + fl); }
            }
            if (mode == "magasin" && MWCoop.Net.Session.IsHost && t > 32f && step == 0) { step = 1; Log.Info("autotest : panier " + Stock.TestCarry(Config.Get("Test", "TestProduit", "Sausages"), 3)); }
            if (mode == "magasin" && MWCoop.Net.Session.IsHost && t > 48f && step == 1) { step = 2; Log.Info("autotest : repose " + Stock.TestCarry(Config.Get("Test", "TestProduit", "Sausages"), -3)); }
            // (invite : il prend 2 articles a 40 s ; attendu chez l'hote : « pris » +2 et les numeros du haut caches)
            if (mode == "magasin" && !MWCoop.Net.Session.IsHost && MWCoop.Net.Session.Active && t > 40f && step == 0) { step = 1; Log.Info("autotest : panier invite " + Stock.TestCarry(Config.Get("Test", "TestProduit", "Sausages"), 2)); }
            string npcWatch = Config.Get("Test", "SuivrePNJ", "");
            if (npcWatch.Length > 0 && Time.frameCount % 300 == 0 && t > 20f) Log.Info("autotest : pnj " + Npcs.State(npcWatch) + " | garees " + Parked.State());
            string stockWatch = Config.Get("Test", "SuivreStock", "");
            if (stockWatch.Length > 0 && Time.frameCount % 300 == 0 && t > 20f) Log.Info("autotest : rayon " + Stock.State(stockWatch));
            // [Test] SuivreEtat=chemin::automate : etat courant d'un automate quelconque.
            string stateWatch = Config.Get("Test", "SuivreEtat", "");
            if (stateWatch.Length > 0 && Time.frameCount % 120 == 0 && t > 20f)
            {
                string[] pf = stateWatch.Split(new[] { "::" }, System.StringSplitOptions.None);
                GameObject g = Game.FindAny(pf[0]);
                PlayMakerFSM sf = g != null && pf.Length > 1 ? Game.FsmOn(g, pf[1]) : null;
                Log.Info("autotest : etat " + stateWatch + " = " + (sf != null ? sf.ActiveStateName : "?"));
            }
            if (Config.GetInt("Test", "MesureCamera", 0) != 0) MeasureCam(t);
            string nanWatch = Config.Get("Test", "SuivreNaN", "");
            if (nanWatch.Length > 0 && t > 20f && Time.realtimeSinceStartup >= nanNext) { nanNext = Time.realtimeSinceStartup + 1f; Log.Info("autotest : " + VehicleSync.TestNan(nanWatch)); }
            if (mode == "passager" && Time.frameCount % 120 == 0 && t > 28f) Log.Info("autotest : place " + Seats.PlaceDans(Config.Get("Test", "TestVoiture", "SORBET")));
            string soundWatch = Config.Get("Test", "SuivreSons", "");
            if (soundWatch.Length > 0 && Time.frameCount % 90 == 0 && t > 20f) Log.Info("autotest : sons " + VehicleSync.AudioState(soundWatch));
            if (soundWatch.Length > 0 && Time.frameCount % 900 == 0 && t > 20f) Log.Info("autotest : " + VehicleSync.DtState(soundWatch));
            string activeWatch = Config.Get("Test", "SuivreActif", "");
            if (activeWatch.Length > 0 && Time.frameCount % 300 == 0 && t > 20f)
            {
                GameObject g = Game.FindAny(activeWatch);
                Log.Info("autotest : " + activeWatch + (g == null ? " introuvable" : g.activeSelf ? " actif" : " inactif"));
            }
            string worldWatch = Config.Get("Test", "SuivreMonde", "");
            if (worldWatch.Length > 0 && Time.frameCount % 300 == 0 && t > 20f)
            {
                string[] pv = worldWatch.Split(':');
                Log.Info("autotest : " + WorldFsms.Var(pv[0], pv.Length > 1 ? pv[1] : "Cutoff"));
            }
            if (mode == "molette" && t > 30f && step < 3 && t > 30f + step * 2f)
            {
                step++;
                Log.Info("autotest : " + Jobs.TestSend(Config.Get("Test", "TestQuete", "ButtonHeaterTemp"), "Get scroll", "INCREASE", "Decrease"));
            }
            string varWatch = Config.Get("Test", "SuivreVar", "");
            if (varWatch.Length > 0 && Time.frameCount % 300 == 0 && t > 20f)
            {
                string[] pv = varWatch.Split(':');
                Log.Info("autotest : " + Jobs.Var(pv[0], pv.Length > 1 ? pv[1] : "Angle"));
            }
            if (mode == "habitacle")
            {
                string vc = Config.Get("Test", "TestVoiture", "SORBET");
                if (t > 25f && step == 0) { step = 1; Log.Info("autotest : " + Seats.TestStandIn(vc, Config.GetInt("Test", "TestPlace", 0))); }
                if (t > 26f && step >= 1 && step < 6 && t > 26f + step) { step++; Log.Info("autotest : habitacle " + Seats.PlaceDans(vc)); }
            }
            if (mode == "passager2" && t > 27f && step == 0) { step = 1; Log.Info("autotest : " + Seats.TestStandIn(Config.Get("Test", "TestVoiture", "SORBET"), Config.GetInt("Test", "TestPlace", 0))); }
            if (mode == "passager2" && t > 31f && step == 1) { step = 2; Log.Info("autotest : entree " + Seats.TestEnter()); }
            if (mode == "passager2" && t > 50f && step == 2) { step = 3; Log.Info("autotest : " + Seats.TestLeave()); }
            if (mode == "passager2" && Time.frameCount % 120 == 0 && t > 26f) Log.Info("autotest : place " + Seats.PlaceDans(Config.Get("Test", "TestVoiture", "SORBET")));
            if (mode == "sondeplaces" && t > 30f && !done) { done = true; Log.Info("autotest : places" + Seats.ProbeSeats(Config.Get("Test", "TestVoiture", "GIFU"))); Log.Info("autotest : coupe" + Seats.ProbeGrid(Config.Get("Test", "TestVoiture", "GIFU"))); }
            if (mode == "passager" && t > 30f && step == 0) { step = 1; Log.Info("autotest : " + Seats.TestSit(Config.Get("Test", "TestVoiture", "SORBET"), Config.GetInt("Test", "TestPlace", 0))); }
            // [Test] Autotest=ceinture : l'invite s'assoit a l'avant de [Test] TestVoiture, attache sa ceinture a 36 s ;
            // l'hote le regarde (CameraAvatar) et voit la ceinture bouclee (journal + captures).
            if (mode == "ceinture" && !Net.Session.IsHost && t > 30f && step == 0) { step = 1; Log.Info("autotest : " + Seats.TestSit(Config.Get("Test", "TestVoiture", "SORBET"), Config.GetInt("Test", "CeinturePlace", 0))); }
            // [Test] CeintureVisee=1 : avant d'attacher, le regard vers la ceinture rangee (37-41 s) puis la boucle (42-46 s),
            // ce que voit la visee note chaque seconde, et captures (ecran-ceinture-*).
            if (mode == "ceinture" && !Net.Session.IsHost && Config.GetInt("Test", "CeintureVisee", 0) != 0 && step >= 1 && step < 9 && t > 37f && t < 47f)
            {
                Log.Info("autotest : " + Seats.TestBeltLook(t < 42f ? 0 : 1));
                if (Time.realtimeSinceStartup >= rivLog) { rivLog = Time.realtimeSinceStartup + 1f; Log.Info("autotest : " + Seats.TestBeltAim()); }
                if (step == 1 && t > 39f) { step = 3; CaptureSoon("ceinture-rangee", 0.1f); }
                if (step == 3 && t > 44f) { step = 4; CaptureSoon("ceinture-boucle", 0.1f); }
            }
            // [Test] CeintureTenue=1 : ceinture tenue de 36 a 44 s (regard vers la boucle), capture ecran-ceinture-tenue, puis bouclee.
            if (mode == "ceinture" && !Net.Session.IsHost && Config.GetInt("Test", "CeintureTenue", 0) != 0 && step >= 1 && step < 5 && t > 36f)
            {
                if (Config.GetInt("Test", "CeintureRegard", 0) != 0) Seats.TestBeltLook(1);
                if (t < 44f) { string tenue = Seats.TestPull(true); if (Time.frameCount % 120 == 0) Log.Info("autotest : " + tenue); }
                if (step == 1 && t > 41f) { step = 3; CaptureSoon("ceinture-tenue", 0.05f); }
                if (step == 3 && t > 44f) { step = 4; Log.Info("autotest : " + Seats.TestPull(false)); }
                if (step == 4 && t > 45f) { step = 5; Log.Info("autotest : " + Seats.TestBelt()); }
                if (step == 4 && t > 47f) { step = 5; CaptureSoon("ceinture-bouclee-invite", 0.05f); }
            }
            if (mode == "ceinture" && !Net.Session.IsHost && Config.GetInt("Test", "CeintureVisee", 0) == 0 && Config.GetInt("Test", "CeintureTenue", 0) == 0 && t > 36f && step == 1) { step = 2; Log.Info("autotest : " + Seats.TestBelt()); }
            // (puis la vue du passager vers sa ceinture bouclee : ecran-ceinture-vue-passager)
            if (mode == "ceinture" && !Net.Session.IsHost && step >= 2 && step < 4 && t > 38f) { BeltLookLate = Config.GetInt("Test", "CeintureVueRegard", 1); if (step == 2 && t > 41f) { step = 3; CaptureSoon("ceinture-vue-passager", 0.05f); Log.Info("autotest : " + Seats.TestBeltCopy()); } }
            // [Test] Autotest=apimods : (avec MSCLoader et MWCoopTestMod) l'hote sauvegarde sur place a 20 s : son Mods.txt
            // (compteur du mod d'essai) partira avec la sauvegarde a la partie suivante.
            if (mode == "apimods" && Net.Session.IsHost && t > 20f && step == 0) { step = 1; Log.Info("autotest : sauvegarde de l'hote"); Game.SaveInPlace(); }
            if (mode == "ceinture" && Net.Session.IsHost && t > 30f)
            {
                // camera d'essai a la place du conducteur, vers le passager avant
                Transform car; Vector3 head; string cn;
                for (int id = 1; id < 8; id++)
                    if (Seats.RemoteSeat(id, out car, out head, out cn) && car != null)
                    {
                        if (testCam == null) { testCam = new GameObject("MWCoop-CameraEssai").AddComponent<Camera>(); testCam.depth = 100; testCam.nearClipPlane = 0.03f; testCam.fieldOfView = 70; }
                        bool front = Config.Get("Test", "CameraCeinture", "") == "avant";   // (de face, depuis le tableau de bord)
                        testCam.transform.position = car.TransformPoint(front ? head + new Vector3(Config.GetFloat("Test", "CeintureCamX", -0.12f), Config.GetFloat("Test", "CeintureCamY", 0f), Config.GetFloat("Test", "CeintureCamZ", 0.42f)) : new Vector3(-head.x * 0.7f, head.y + 0.05f, head.z + 0.25f));
                        testCam.transform.LookAt(car.TransformPoint(head + new Vector3(Config.GetFloat("Test", "CeintureViseX", 0f), Config.GetFloat("Test", "CeintureViseY", -0.3f), 0f)), car.up);
                        if (step == 0 && t > Config.GetInt("Test", "CeintureCapture", 110)) { step = 1; CaptureSoon("ceinture-hote", 0.1f); }
                        break;
                    }
            }
            if (mode == "passager" && t > 60f && step == 1 && Config.GetInt("Test", "TestSortie", 1) != 0) { step = 2; Log.Info("autotest : " + Seats.TestLeave()); }
            if (mode == "tableau" && t > 35f && !done)
            {
                done = true;
                Log.Info("autotest : " + CarVisuals.Test(Config.Get("Test", "TestVoiture", "SORBET(190-200psi)"), Config.Get("Test", "TestElement", "pivot_brake")));
            }
            string visWatch = Config.Get("Test", "SuivreElement", "");
            if (visWatch.Length > 0 && Time.frameCount % 300 == 0 && t > 20f)
                Log.Info("autotest : " + CarVisuals.State(Config.Get("Test", "TestVoiture", "SORBET(190-200psi)"), visWatch));
            if (mode == "portiere" && t > 52f && step == 1 && Config.GetInt("Test", "TestPousse", 0) != 0) { step = 2; Log.Info("autotest : refermee en poussant " + CarDoors.TestState(Config.Get("Test", "TestVoiture", "KEKMET(350-400psi)"), "Reset 2")); }
            if (mode == "portiere" && t > 38f && step == 0 && Config.GetInt("Test", "TestPousse", 0) != 0) { step = 1; Log.Info("autotest : " + CarDoors.TestPush(Config.Get("Test", "TestVoiture", "KEKMET(350-400psi)"), -35f)); }
            if (mode == "portiere" && t > 30f && !done)
            {
                done = true;
                Log.Info("autotest : " + CarDoors.TestOpen(Config.Get("Test", "TestVoiture", "KEKMET(350-400psi)"), true, Config.Get("Test", "TestPorte", "")));   // ([Test] TestPorte : telle portiere, ex. DoorRear(right))
            }
            if (mode == "chemin" || mode == "spam")
            {
                // Portiere [Test] TestVoiture / TestPorte. chemin : l'invite debout dans sa course, l'hote l'ouvre
                // (bouton tenu 0,6 s) ; spam : les deux cliquent au hasard tres vite, puis on compare.
                string vc = Config.Get("Test", "TestVoiture", "SORBET(190-200psi)"), vp = Config.Get("Test", "TestPorte", "DoorRear(right)");
                // [Test] Deverrouiller=1 : verrouillage centralise d'un mod (CentralLocking.UnlockDoors) ouvert a 20 s chez chacun.
                // (Deverrouiller=2 : l'hote seulement -- l'invite doit le recevoir)
                if (mode == "chemin" && (Config.GetInt("Test", "Deverrouiller", 0) == 1 || Config.GetInt("Test", "Deverrouiller", 0) == 2 && MWCoop.Net.Session.IsHost) && t > 20f && step2 == 0)
                {
                    step2 = 1;
                    GameObject vcar = GameObject.Find(vc);
                    Component cl = null;
                    if (vcar != null) foreach (MonoBehaviour m in vcar.GetComponentsInChildren<MonoBehaviour>(true)) if (m != null && m.GetType().Name == "CentralLocking") { cl = m; break; }
                    var um = cl != null ? cl.GetType().GetMethod("UnlockDoors") : null;
                    if (um != null) um.Invoke(cl, new object[] { false });
                    Log.Info("autotest : verrouillage de " + vc + " : " + (cl == null ? "aucun" : "ouvert"));
                }
                // [Test] AcheterMW2=1 : l'hote achete la Second Machtwagen a 18 s (poignees et contact rallumes)
                if (mode == "chemin" && MWCoop.Net.Session.IsHost && Config.GetInt("Test", "AcheterMW2", 0) != 0 && t > 18f && step2 < 5) { step2 = 5; Log.Info("autotest : second machtwagen " + ModSecondMachtwagen.TestBuyNow()); }
                // [Test] FermeInvite=1 : c'est l'invite qui referme (a 50 s) la portiere ouverte par l'hote.
                if (mode == "chemin" && !MWCoop.Net.Session.IsHost && Config.GetInt("Test", "FermeInvite", 0) != 0 && t > 40f && step == 1) { step = 3; Log.Info("autotest : invite pousse vers la fermeture " + CarDoors.TestGrab(vc, vp)); }
                if (mode == "chemin" && !MWCoop.Net.Session.IsHost && t > 15f && step == 0) { step = 1; Log.Info("autotest : " + CarDoors.TestStandInPath(vc, vp)); }
                if (mode == "chemin" && MWCoop.Net.Session.IsHost && t > 40f && step == 0) { step = 1; Log.Info("autotest : " + CarDoors.TestOpen(vc, true, vp)); }
                if (mode == "chemin" && MWCoop.Net.Session.IsHost && t > 40.6f && step == 1) { step = 2; Log.Info("autotest : relache " + CarDoors.TestState(vc, "Mouse off", vp)); }
                // TestRelache=1 : l'hote appuie pour la refermer et relache tout de suite (avant qu'elle soit fermee).
                if (mode == "chemin" && MWCoop.Net.Session.IsHost && t > 46f && step == 2 && Config.GetInt("Test", "TestRelache", 0) != 0) { step = 30; Log.Info("autotest : appuie pour fermer " + CarDoors.TestGrab(vc, vp)); }
                if (mode == "chemin" && MWCoop.Net.Session.IsHost && t > 46.12f && step == 30) { step = 31; Log.Info("autotest : relache tout de suite " + CarDoors.TestState(vc, "Check position", vp) + " " + CarDoors.StateOf(vc, vp)); }
                if (mode == "chemin" && MWCoop.Net.Session.IsHost && t > 45f && step == 2 && Config.GetInt("Test", "TestPousse", 0) != 0) { step = 22; Log.Info("autotest : refermee en poussant " + CarDoors.TestState(vc, "Reset 2", vp)); }
                if (mode == "chemin" && MWCoop.Net.Session.IsHost && t > 50f && (step == 2 || step == 22) && Config.GetInt("Test", "FermeInvite", 0) == 0) { step = 3; Log.Info("autotest : pousse vers la fermeture " + CarDoors.TestGrab(vc, vp)); }
                // (fenetre a l'horloge du PC : les deux s'arretent a la meme seconde, en plein echange)
                int sec = System.DateTime.Now.Second;
                if (mode == "spam" && t > 25f && sec >= 5 && sec < 45) { string w = CarDoors.TestRace(vc, vp); if (w != null) Log.Info("autotest : " + w); }
                if (mode == "spam" && t > 25f && (sec == 47 || sec == 51 || sec == 55) && sec != spamLogged) { spamLogged = sec; Log.Info("autotest : portiere a :" + sec + " " + CarDoors.StateOf(vc, vp)); }
                int mark = mode == "spam" ? 0
                         : (t > 62f ? 5 : t > 55f ? 4 : t > 48f ? 3 : t > 44f ? 2 : t > 41f ? 1 : 0);
                if (mark > portiereMark) { portiereMark = mark; Log.Info("autotest : portiere " + mark + " " + CarDoors.StateOf(vc, vp)); }
            }
            string doorWatch = Config.Get("Test", "SuivrePortiere", "");
            if (doorWatch.Length > 0 && Time.frameCount % 300 == 0 && t > 20f) Log.Info("autotest : " + CarDoors.State(doorWatch));
            if (mode == "voix" && t > 35f && !done)
            {
                done = true;
                Log.Info("autotest : voix " + Voices.Test(Config.Get("Test", "TestVoix", "Teimo")));
            }
            if (mode == "manger" && t > 40f && !done)
            {
                done = true;
                Log.Info("autotest : manger " + Consume.TestNearest());
            }
            if (mode == "liquide" && t > 40f && !done)
            {
                done = true;
                string bump = Config.Get("Test", "LiquideCle", "");   // ([Test] LiquideCle=gasoline : +10 a cette valeur)
                Log.Info("autotest : liquide " + (bump.Length > 0 ? Fluids.TestBump(bump, 10f) : Fluids.TestNearest(Config.Get("Test", "TestLiquide", "Fluid"))));
            }
            // [Test] SuivreLiquide=gasoline : valeurs suivies dont la cle contient ce texte, a 35 et 60 s.
            string fluidWatch = Config.Get("Test", "SuivreLiquide", "");
            if (fluidWatch.Length > 0 && ((t > 35f && step == 0) || (t > 60f && step == 1))) { step++; Log.Info("autotest : liquide t=" + t.ToString("F0") + " " + Fluids.TestState(fluidWatch)); }
            if (mode == "interactifs" && t > 40f && !done) { done = true; Log.Info("autotest : releve " + Recon.DumpInteractive()); }
            if (mode == "monde" && t > 45f && !done) { done = true; Log.Info("autotest : releve " + Recon.DumpWorldFsms()); }
            // [Test] Autotest=corps : etat du corps de l'invite garde d'une session a l'autre. Chacun note faim, soif, ivresse
            // a 25 et 50 s ; avec [Test] CorpsPoser=1, l'invite pose faim 77, soif 66, ivresse 1,5 a 30 s (gardes a 40 s).
            if (mode == "corps")
            {
                if ((t > 25f && step == 0) || (t > 50f && step == 2))
                {
                    step++;
                    Log.Info("autotest : corps t=" + t.ToString("F0") + " : faim " + Game.GlobalFloat("PlayerHunger").ToString("F1") + ", soif " + Game.GlobalFloat("PlayerThirst").ToString("F1")
                             + ", ivresse " + Game.GlobalFloat("PlayerDrunk").ToString("F2") + ", fatigue " + Game.GlobalFloat("PlayerFatigue").ToString("F1"));
                }
                if (t > 30f && step == 1)
                {
                    step = 2;
                    if (!MWCoop.Net.Session.IsHost && Config.GetInt("Test", "CorpsPoser", 0) != 0)
                    {
                        Game.SetGlobalFloat("PlayerHunger", 77f); Game.SetGlobalFloat("PlayerThirst", 66f); Game.SetGlobalFloat("PlayerDrunk", 1.5f);
                        Log.Info("autotest : corps : faim 77, soif 66, ivresse 1,5 posees ici");
                    }
                }
            }
            if (mode == "globales" && t > 35f && !done)
            {
                done = true;
                var sb = new System.Text.StringBuilder();
                FsmVariables g = FsmVariables.GlobalVariables;
                foreach (FsmFloat x in g.FloatVariables) sb.Append(" f:").Append(x.Name).Append('=').Append(x.Value.ToString("F2"));
                foreach (FsmInt x in g.IntVariables) sb.Append(" i:").Append(x.Name).Append('=').Append(x.Value);
                foreach (FsmBool x in g.BoolVariables) sb.Append(" b:").Append(x.Name).Append('=').Append(x.Value);
                foreach (FsmString x in g.StringVariables) sb.Append(" s:").Append(x.Name).Append('=').Append(x.Value);
                Log.Info("autotest : globales" + sb);
            }
            // [Test] Autotest=releveco : automates dont une variable ou une action parle de monoxyde (Carbon*, CO), et
            // effets d'image de la camera du joueur (dumps/co.txt, avec parametres).
            // [Test] Autotest=co : a 30 s, mort par le monoxyde comme dans le jeu (PassoutEyes allume, 'Logic' en "Close eyes 2" :
            // paupieres fermees, son baisse, puis Systems/Death). Avec [Test] TestReapparition=n, la reapparition est
            // choisie seule ; a 45 s, etat des paupieres et du son (captures : [Test] Captures=46).
            if (mode == "co" && t > 30f && step == 0)
            {
                step = 1;
                GameObject pe = Game.FindAny("PLAYER/Pivot/AnimPivot/Camera/FPSCamera/FPSCamera/Carbon/PassoutEyes");
                if (pe != null)
                {
                    pe.SetActive(true);
                    PlayMakerFSM l = Game.FsmOn(pe, "Logic");
                    if (l != null) Game.SetState(l, "Close eyes 2");
                    Log.Info("autotest : co : paupieres qui se ferment (" + (l != null ? l.ActiveStateName : "?") + ")");
                }
                else Log.Info("autotest : co : PassoutEyes introuvable");
            }
            if (mode == "co" && t > 45f && step == 1)
            {
                step = 2;
                GameObject pe = Game.FindAny("PLAYER/Pivot/AnimPivot/Camera/FPSCamera/FPSCamera/Carbon/PassoutEyes");
                FsmFloat vol = FsmVariables.GlobalVariables.FindFsmFloat("GameVolume");
                Log.Info("autotest : co : paupieres " + (pe != null && pe.activeInHierarchy ? "FERMEES (allumees)" : "rouvertes") + ", son " + (vol != null ? vol.Value.ToString("F2") : "?"));
            }
            // [Test] Autotest=relevenom, ReleveNom=a;b : vidage (avec parametres) de chaque objet de la scene portant un
            // automate dont le nom d'objet contient a ou b (dumps/cibles.txt), et la liste dans le journal.
            // [Test] Autotest=robinet : l'hote ouvre le robinet ([Test] Robinet, defaut la cuisine des parents) a 30 s, le
            // ferme a 40 s (comme le clic : "Position") ; chacun note toutes les 2 s si l'eau coule (objet [Test] Eau).
            if (mode == "robinet")
            {
                string tap = Config.Get("Test", "Robinet", "KitchenWaterTap/Trigger::Use");
                GameObject water = Game.FindAny(Config.Get("Test", "Eau", "YARD/Building/KITCHEN/KitchenWaterTap/ParticleDrink"));
                if (t > 10f && t < 60f && t >= tapLog)
                {
                    tapLog = t + 2f;
                    Log.Info("autotest : robinet t=" + t.ToString("F0") + " : eau " + (water != null && water.activeInHierarchy ? "COULE" : "coupee"));
                }
                // RobinetActeur=croise : l'hote ouvre a 30 s, l'invite (parti ~10 s apres) ferme a ses 25 s, 5 s apres.
                string who = Config.Get("Test", "RobinetActeur", "hote");
                bool host = MWCoop.Net.Session.IsHost;
                bool actor = who == "invite" ? !host : who == "croise" ? true : host;
                bool now2 = who == "croise" ? (host ? t > 30f && step == 0 : t > 25f && step == 0) : (t > 30f && step == 0) || (t > 40f && step == 1);
                if (actor && now2)
                {
                    step++;
                    // Comme un vrai clic sur le robinet vise : "Wait button" (sa detection de la souris coupee le temps du clic), USE.
                    PlayMakerFSM tf = null;
                    int cut = tap.IndexOf("::");
                    GameObject tg = Game.FindAny(cut > 0 ? tap.Substring(0, cut) : tap);
                    if (tg != null) tf = Game.FsmOn(tg, "Use");
                    var off = new System.Collections.Generic.List<FsmStateAction>();
                    FsmState wb = tf != null ? tf.Fsm.GetState("Wait button") : null;
                    if (wb != null) foreach (FsmStateAction a2 in wb.Actions) if (a2 is HutongGames.PlayMaker.Actions.MousePickEvent && a2.Enabled) { a2.Enabled = false; off.Add(a2); }
                    try { WorldFsms.TestClick(tap, "Wait button"); Log.Info("autotest : robinet " + (step == 1 ? "ouvert" : "ferme") + " : " + WorldFsms.TestEvent(tap, "USE")); }
                    finally { foreach (FsmStateAction a2 in off) a2.Enabled = true; }
                }
            }
            if (mode == "relevenom" && t > 40f && !done)
            {
                done = true;
                string[] keys = Config.Get("Test", "ReleveNom", "").Split(';');
                bool inactive = Config.GetInt("Test", "ReleveInactifs", 0) != 0;   // (aussi sous une racine inactive)
                var paths = new System.Collections.Generic.List<string>();
                foreach (PlayMakerFSM f in Resources.FindObjectsOfTypeAll(typeof(PlayMakerFSM)))
                {
                    if (f.hideFlags != HideFlags.None || (!inactive && !f.transform.root.gameObject.activeInHierarchy)) continue;
                    foreach (string k in keys)
                        if (k.Length > 0 && f.gameObject.name.IndexOf(k, System.StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            string pth = Recon.Path(f.transform);
                            if (!paths.Contains(pth)) paths.Add(pth);
                        }
                }
                Log.Info("autotest : relevenom : " + paths.Count + " objets : " + string.Join(" | ", paths.ToArray()) + " ; " + Recon.DumpTargets(string.Join(";", paths.ToArray())));
            }
            if (mode == "releveicones" && t > 40f && !done)
            {
                done = true;
                string[] iconVars = Config.Get("Test", "ReleveVar", "GUIpassenger;GUIdrive").Split(';');
                var sb = new System.Text.StringBuilder();
                foreach (PlayMakerFSM f in Resources.FindObjectsOfTypeAll(typeof(PlayMakerFSM)))
                {
                    if (f.hideFlags != HideFlags.None || !f.transform.root.gameObject.activeInHierarchy) continue;
                    try
                    {
                        if (f.Fsm.States.Length > 0 && !f.Fsm.States[0].IsInitialized) f.Fsm.InitData();
                        var hit = new System.Collections.Generic.HashSet<string>();
                        foreach (FsmState st in f.Fsm.States)
                            foreach (FsmStateAction a2 in st.Actions)
                            {
                                if (a2 == null) continue;
                                foreach (System.Reflection.FieldInfo fi in a2.GetType().GetFields())
                                {
                                    var nv = fi.GetValue(a2) as NamedVariable;
                                    if (nv != null && nv.UseVariable && System.Array.IndexOf(iconVars, nv.Name) >= 0) hit.Add(nv.Name + "@" + st.Name);
                                }
                            }
                        if (hit.Count > 0) sb.Append(" | ").Append(Recon.Path(f.transform)).Append("::").Append(f.FsmName).Append(" ").Append(string.Join(",", new System.Collections.Generic.List<string>(hit).ToArray()));
                    }
                    catch { }
                }
                Log.Info("autotest : icones :" + sb);
            }
            if (mode == "relevebattantes" && t > 40f && !done)
            {
                done = true;
                var sb = new System.Text.StringBuilder();
                int n = 0;
                foreach (PlayMakerFSM f in Resources.FindObjectsOfTypeAll(typeof(PlayMakerFSM)))
                {
                    if (f.hideFlags != HideFlags.None || !f.transform.root.gameObject.activeInHierarchy) continue;
                    try
                    {
                        if (f.Fsm.States.Length > 0 && !f.Fsm.States[0].IsInitialized) f.Fsm.InitData();
                        int torque = 0; bool up = false;
                        foreach (FsmState st in f.Fsm.States)
                            foreach (FsmStateAction a2 in st.Actions)
                            {
                                if (a2 == null) continue;
                                string tn = a2.GetType().Name;
                                if (tn == "AddTorque") torque++;
                                if (tn == "GetMouseButtonUp") up = true;
                            }
                        if (torque == 0) continue;
                        n++;
                        sb.Append(" | ").Append(Recon.Path(f.transform)).Append("::").Append(f.FsmName).Append(" couples ").Append(torque).Append(up ? " (bouton tenu)" : "").Append(" [").Append(Replay.Owner(f) ?? "libre").Append("]");
                    }
                    catch { }
                }
                Log.Info("autotest : battantes : " + n + sb);
            }
            if (mode == "releveco" && t > 40f && !done)
            {
                done = true;
                var hits = new System.Collections.Generic.List<string>();
                foreach (PlayMakerFSM f in Resources.FindObjectsOfTypeAll(typeof(PlayMakerFSM)))
                {
                    if (f.hideFlags != HideFlags.None) continue;
                    bool hit = false;
                    foreach (NamedVariable v in f.FsmVariables.GetAllNamedVariables()) if (v.Name.Contains("Carbon") || v.Name == "CO") hit = true;
                    if (!hit) foreach (FsmFloat v in FsmVariables.GlobalVariables.FloatVariables) { }
                    if (hit) hits.Add(Recon.Path(f.transform));
                }
                var cam = Game.PlayerPart("Pivot/AnimPivot/Camera/FPSCamera/FPSCamera");
                var sb = new System.Text.StringBuilder();
                if (cam != null) foreach (MonoBehaviour m in cam.GetComponents<MonoBehaviour>()) sb.Append(' ').Append(m.GetType().Name).Append(m.enabled ? "+" : "-");
                Log.Info("autotest : releveco : " + hits.Count + " automates ; camera :" + sb + " ; " + Recon.DumpTargets(string.Join(";", hits.ToArray())));
            }
            if (mode == "releveracine" && t > 45f && !done) { done = true; foreach (string rr in Config.Get("Test", "RacineReleve", "RACES").Split(';')) Log.Info("autotest : releve " + Recon.DumpRoot(rr.Trim())); }   // (plusieurs : a;b;c)
            if (mode == "releveargent" && t > 45f && !done) { done = true; Log.Info("autotest : releve " + Recon.DumpMoney()); }
            if (mode == "menu" && t > 70f && !done)
            {
                // L'hote revient au menu (puis Continuer=1 le relance) : les invites doivent suivre.
                done = true;
                Log.Info("autotest : retour au menu");
                Application.LoadLevel("MainMenu");
            }
            if ((mode == "dormir" || (mode == "dormirseul" && MWCoop.Net.Session.IsHost)) && t > 20f && !done)
            {
                // Le joueur se couche dans le lit le plus proche (fatigue montee pour qu'il dorme
                // plusieurs heures) ; on suit l'heure et la fatigue chaque seconde.
                done = true;
                Vector3 me = Game.PlayerGo.transform.position;
                PlayMakerFSM bed = null;
                foreach (PlayMakerFSM f in Object.FindObjectsOfType<PlayMakerFSM>())
                    if (f.gameObject.name == "SleepTrigger" && f.FsmName == "Activate"
                        && (bed == null || (f.transform.position - me).sqrMagnitude < (bed.transform.position - me).sqrMagnitude)) bed = f;
                if (bed == null) { Log.Warn("autotest : aucun lit"); return; }
                FsmFloat fat = FsmVariables.GlobalVariables.FindFsmFloat("PlayerFatigue");
                if (fat != null) fat.Value = 60f;
                Log.Info("autotest : au lit " + Recon.Path(bed.transform) + " a " + (bed.transform.position - me).magnitude.ToString("F0") + " m, releve " + Recon.DumpTargets(Recon.Path(bed.transform)));
                Game.SetState(bed, "Get positions");
                sleepWatch = true;
            }
            if (sleepWatch && Time.realtimeSinceStartup >= nextSleepLog)
            {
                nextSleepLog = Time.realtimeSinceStartup + 1f;
                FsmFloat fat = FsmVariables.GlobalVariables.FindFsmFloat("PlayerFatigue");
                PlayMakerFSM c = Game.FindFsm("MAP/Sun/PivotSun/SUN", "Color");
                bool sl = Game.GlobalBool("PlayerSleeps");
                Log.Info("autotest : dort " + sl + ", heure " + c.FsmVariables.GetFsmInt("Time").Value
                         + ", fatigue " + (fat != null ? fat.Value.ToString("F1") : "?")
                         + ", echelle " + FsmVariables.GlobalVariables.FindFsmFloat("GlobalTimeScale").Value
                         + ", joueur " + (Game.PlayerT != null ? Game.PlayerT.position.ToString("F2") + " rot " + Game.PlayerT.eulerAngles.ToString("F0") + " parent " + (Game.PlayerT.parent != null ? Game.PlayerT.parent.name : "-") : "?")
                         + ", camera " + (Camera.main != null ? Camera.main.transform.position.ToString("F2") : "?"));
                if (!sl && fat != null && fat.Value < 50f) { if (sleepWakeLogs++ > 12) sleepWatch = false; }   // (reveil : encore 12 s de releves -- chute sous le sol, lit des parents, JD 22/10)
            }
            // [Test] SondeRoues=1 (22/10, roues de la CORRIS qui s'enfoncent chez un joueur) : donnees des roues de la CORRIS a 10, 20 et 30 s.
            // [Test] PressionPneus=200 (23/10, Rivett qui s'envole a la reprise chez les joueurs) : pneus de la CORRIS gonfles a cette
            // pression (optimale aussi) a 12 s, raideur recalculee par le jeu (Wheel.SetTireStiffness) -- l'etat d'une voiture dont les
            // pneus ont ete gonfles a la station, que la sauvegarde d'essai n'a pas (pression 0 : modele de pneu inactif).
            float pneusP = Config.GetFloat("Test", "PressionPneus", 0f);
            if (pneusP > 0f && t > 12f && !pressDone)
            {
                pressDone = true;
                GameObject pc = GameObject.Find("CORRIS");
                var sb0 = new System.Text.StringBuilder("autotest : pneus de la CORRIS gonfles a " + pneusP + " :");
                if (pc != null) foreach (Wheel w in pc.GetComponentsInChildren<Wheel>(true))
                {
                    w.pressure = pneusP; w.optimalPressure = pneusP;
                    try { w.SetTireStiffness(); } catch (System.Exception e) { sb0.Append(" (SetTireStiffness : " + e.Message + ")"); }
                    sb0.Append(' ').Append(w.name).Append(" raideur ").Append(FieldF(w, "verticalTireStiffness")).Append(" amort ").Append(FieldF(w, "tireDampingRate"));
                }
                Log.Info(sb0.ToString());
            }
            int sondeMax = Config.GetInt("Test", "SondeRoues", 0) == 2 ? 12 : 3;   // (2 : toutes les 10 s pendant 2 min)
            if (Config.GetInt("Test", "SondeRoues", 0) >= 1 && t > 10f * (sondeRoues + 1) && sondeRoues < sondeMax)
            {
                sondeRoues++;
                GameObject car = GameObject.Find("CORRIS");
                var sb = new System.Text.StringBuilder("autotest : roues CORRIS a " + t.ToString("F0") + " s :");
                if (car == null) sb.Append(" introuvable");
                else sb.Append(" position ").Append(car.transform.position.ToString("F2"));
                if (car != null) foreach (Wheel w in car.GetComponentsInChildren<Wheel>(true))
                    sb.Append(" [").Append(w.name).Append(" pression ").Append(w.pressure).Append('/').Append(w.optimalPressure).Append(" rayon ").Append(w.radius).Append('/').Append(w.rimRadius)
                      .Append(" largeur ").Append(w.width).Append(" masse ").Append(w.mass).Append(" raideur ").Append(FieldF(w, "verticalTireStiffness")).Append(" amort ").Append(FieldF(w, "tireDampingRate")).Append(" actif ").Append(w.gameObject.activeInHierarchy).Append(']');
                Log.Info(sb.ToString());
            }
            if (mode == "heure" && t > 15f && !done)
            {
                // L'hote saute a 18 h : les invites doivent suivre (World).
                done = true;
                PlayMakerFSM c = Game.FindFsm("MAP/Sun/PivotSun/SUN", "Color");
                int h = Config.GetInt("Test", "TestHeure", 18);
                c.FsmVariables.GetFsmInt("Time").Value = h;
                c.SendEvent("TIMESKIP");
                Log.Info("autotest : l'hote passe a " + h + " h");
            }
        }

        // [Test] Autotest=palan (2 joueurs, l'hote agit) : moteur VIN1010 accroche au palan comme dans le jeu (detecteurs du
        // crochet a vrai, vis du palan serrees par leur automate -> Remove hinge/Set joint/Hook 2), palan (cinematique)
        // amene pour que le moteur soit a sa pose de montage (les detecteurs du point montrent ses vis), vis du moteur
        // serrees (-> Bolted -> Hook 2 INCAR -> State 2 -> INSTALL au point -> Install 2), palan desserre (Off hook) ;
        // etat (parent, corps, joint, point) note chez chacun a 70 s. Journal : Trace des automates du palan, du point
        // et du moteur.
        static Transform palan, palanHook, palanPivot; static GameObject moteur, palanPoint;
        static void Palan(float t)
        {
            if (step == 0 && t > (Net.Session.IsHost ? 30f : 12f))
            {
                step = 1;
                GameObject h = Game.FindAny("EQUIPMENTS/motor hoist(itemx)");
                moteur = Parts.FindById("VIN1010");
                palanPoint = Game.FindAny("CORRIS/MotorPivot/MassCenter/Block/VINP_Block");
                if (h == null || moteur == null || palanPoint == null) { Log.Warn("autotest : palan " + (h != null) + " moteur " + (moteur != null) + " point " + (palanPoint != null)); step = 99; return; }
                palan = h.transform; palanHook = palan.Find("Hoist"); palanPivot = palan.Find("Hoist/BlockPivot");
                PlayMakerFSM hook = Game.FsmOn(palanHook.gameObject, "Hook");
                Trace.Watch(hook, "palan Hook", 70f);
                Trace.Watch(Game.FsmOn(palanHook.gameObject, "Data"), "palan Data", 70f);
                Trace.Watch(Game.FsmOn(palanPoint, "Data"), "point VINP_Block", 70f);
                Trace.Watch(Game.FsmOn(moteur, "Data"), "moteur", 70f);
                Trace.Watch(Game.FsmOn(palanPoint.transform.Find("Trigger_motor1").gameObject, "Assembly"), "detecteur 1 du point", 70f);
                if (!Net.Session.IsHost) return;
                Transform car = palanPoint.transform.root;
                Vector3 at = palanPoint.transform.position + car.forward * 2.2f;
                RaycastHit hit; if (Physics.Raycast(at + Vector3.up * 2f, Vector3.down, out hit, 6f)) at.y = hit.point.y; else at.y = car.position.y;
                foreach (Rigidbody rb in new[] { h.GetComponent<Rigidbody>(), palanHook.GetComponent<Rigidbody>() }) if (rb != null) { rb.velocity = Vector3.zero; rb.angularVelocity = Vector3.zero; rb.isKinematic = true; }
                palan.position = at; palan.rotation = Quaternion.LookRotation(-car.forward, Vector3.up);
                Rigidbody mrb = moteur.GetComponent<Rigidbody>();
                if (mrb != null) { mrb.velocity = Vector3.zero; mrb.angularVelocity = Vector3.zero; mrb.isKinematic = true; }
                moteur.transform.position = palanPivot.position; moteur.transform.rotation = palanPivot.rotation;
                Log.Info("autotest : palan en " + at.ToString("F1") + " (cinematique), moteur sous le crochet ; Hook [" + hook.ActiveStateName + "]");
            }
            if (step < 1 || step > 90 || moteur == null) return;
            PlayMakerFSM hk = palanHook != null ? Game.FsmOn(palanHook.gameObject, "Hook") : null;
            string parent = moteur.transform.parent != null ? moteur.transform.parent.name : "aucun";
            if (t > 70f && !done)
            {
                done = true;
                PlayMakerFSM pd = Game.FsmOn(palanPoint, "Data");
                Log.Info("autotest : etat final : moteur parent " + parent + ", corps " + (moteur.GetComponent<Rigidbody>() != null) + ", joint " + (moteur.GetComponent<FixedJoint>() != null)
                    + ", point Installed " + pd.FsmVariables.GetFsmBool("Installed").Value + " Bolted " + pd.FsmVariables.GetFsmBool("Bolted").Value + ", Hook [" + (hk != null ? hk.ActiveStateName : "?") + "], moteur en "
                    + moteur.transform.position.ToString("F2") + " (point en " + palanPoint.transform.position.ToString("F2") + "), vis " + Parts.BoltState("VIN1010"));
            }
            if (!Net.Session.IsHost || hk == null) return;
            // detecteurs du crochet tenus a vrai tant que le crochet attend (le jeu : les deux boules du moteur dans les deux detecteurs)
            if (step <= 16 && (hk.ActiveStateName == "Wait" || hk.ActiveStateName == "Trigger" || hk.ActiveStateName == "Reset"))
            { hk.FsmVariables.GetFsmBool("Trigger1").Value = true; hk.FsmVariables.GetFsmBool("Trigger2").Value = true; }
            if (step >= 1 && step <= 16 && t > 32f + step * 0.2f)
            {
                Transform bolts = palanHook.Find("Hooks/Bolts");
                int i = (step - 1) % 2;
                PlayMakerFSM f = bolts != null && bolts.childCount > i ? Game.FsmOn(bolts.GetChild(i).gameObject, "Screw") : null;
                if (f != null) f.SendEvent("TIGHTEN");
                if (step == 16) Log.Info("autotest : vis du palan serrees (" + (f != null ? f.FsmVariables.GetFsmInt("BoltTightness").Value.ToString() : "automate absent") + ") ; Hook [" + hk.ActiveStateName + "], moteur parent " + parent + ", joint " + (moteur.GetComponent<FixedJoint>() != null));
                step++;
            }
            if (step == 17 && t > 40f)
            {
                step = 18;
                Rigidbody mrb = moteur.GetComponent<Rigidbody>();
                if (mrb != null) mrb.isKinematic = false;
                // le pivot du bloc sur la pose du point : le moteur (pose locale remise a zero sous le pivot) a sa pose de montage
                Quaternion q = palanPoint.transform.rotation * Quaternion.Inverse(palanPivot.rotation);
                palan.rotation = q * palan.rotation;
                palan.position += palanPoint.transform.position - palanPivot.position;
                moteur.transform.position = palanPoint.transform.position; moteur.transform.rotation = palanPoint.transform.rotation;
                if (mrb != null) { mrb.velocity = Vector3.zero; mrb.angularVelocity = Vector3.zero; }
                Transform e1 = moteur.transform.Find("EnginePointTrigger1"), t1 = palanPoint.transform.Find("Trigger_motor1");
                Log.Info("autotest : palan amene ; Hook [" + hk.ActiveStateName + "], moteur parent " + parent + ", joint " + (moteur.GetComponent<FixedJoint>() != null) + ", detecteur 1 a " + (e1 != null && t1 != null ? (e1.position - t1.position).magnitude.ToString("F3") + " m" : "?"));
            }
            if (step >= 18 && step <= 25 && t > 44f + (step - 18) * 0.5f) { Log.Info("autotest : vis moteur " + Parts.TestBolt("VIN1010", true) + " ; Hook [" + hk.ActiveStateName + "]"); step++; }
            if (step >= 26 && step <= 41 && t > 52f + (step - 26) * 0.2f)
            {
                Transform bolts = palanHook.Find("Hooks/Bolts");
                int i = (step - 26) % 2;
                PlayMakerFSM f = bolts != null && bolts.childCount > i ? Game.FsmOn(bolts.GetChild(i).gameObject, "Screw") : null;
                if (f != null) f.SendEvent("UNTIGHTEN");
                if (step == 41) Log.Info("autotest : palan desserre ; Hook [" + hk.ActiveStateName + "], moteur parent " + parent + ", joint " + (moteur.GetComponent<FixedJoint>() != null));
                step++;
            }
        }

        static void DumpCameras(string when)
        {
            var sb = new System.Text.StringBuilder("autotest : cameras (" + when + ") :");
            foreach (Camera c in Resources.FindObjectsOfTypeAll<Camera>())
            {
                if (c == null || (c.hideFlags & HideFlags.HideAndDontSave) != 0) continue;
                RenderTexture rt = c.targetTexture;
                bool on = c.enabled && c.gameObject.activeInHierarchy;
                if (rt == null && !on) continue;
                sb.Append("\n  ").Append(Recon.Path(c.transform)).Append(on ? " ALLUMEE" : " eteinte")
                  .Append(rt != null ? " -> " + rt.name + "#" + rt.GetInstanceID() + " " + rt.width + "x" + rt.height : " -> ecran");
            }
            Log.Info(sb.ToString());
            // automates qui touchent aux cameras des retroviseurs (qui les deplace d'une voiture a l'autre)
            var ms = new System.Text.StringBuilder("autotest : automates des retroviseurs (" + when + ") :");
            foreach (Object o in Resources.FindObjectsOfTypeAll(typeof(PlayMakerFSM)))
            {
                var f = (PlayMakerFSM)o;
                if (f.hideFlags != HideFlags.None || f.Fsm == null) continue;
                try
                {
                    foreach (HutongGames.PlayMaker.FsmState st in f.Fsm.States)
                        foreach (HutongGames.PlayMaker.FsmStateAction act in st.Actions)
                            foreach (System.Reflection.FieldInfo fi in act.GetType().GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
                            {
                                object v = fi.GetValue(act);
                                GameObject go = null;
                                if (v is HutongGames.PlayMaker.FsmGameObject) go = ((HutongGames.PlayMaker.FsmGameObject)v).Value;
                                else if (v is HutongGames.PlayMaker.FsmOwnerDefault) { var od = (HutongGames.PlayMaker.FsmOwnerDefault)v; go = od.OwnerOption == HutongGames.PlayMaker.OwnerDefaultOption.UseOwner ? f.gameObject : od.GameObject.Value; }
                                if (go != null && go.name.EndsWith("MirrorCam"))
                                    ms.Append("NL  ").Append(Recon.Path(f.transform)).Append(" :: ").Append(f.FsmName).Append(" [").Append(f.ActiveStateName).Append("] etat ").Append(st.Name).Append(" ").Append(act.GetType().Name).Append(" ").Append(fi.Name).Append("=").Append(go.name);
                            }
                }
                catch { }
            }
            Log.Info(ms.ToString().Replace("NL", ((char)10).ToString()));
        }

        static bool done, teleported, hourSet, sleepWatch, foodMade, watchLogged;
        static System.Collections.Generic.List<Rigidbody> rivParts;
        static bool rivNear; static int attVoisin, attAir; static float rivLog, cabGap, holdLog, attTry, attExitAt = 1e9f;
        static int holdN;
        static GameObject rangeGo;
        static Transform holdCam;
        static bool cabRetp;
        static Vector3 holdSum, holdSq;
        static bool tcOn;
        static float tcAt;
        static GameObject tcWalker, tcCust;
        static System.Collections.Generic.List<Transform> tcLug;
        static bool cabAv, cabAvLogged;
        static float cabLogAt, cabWheelAt, cabGearAt;
        static Vector3 cabMin, cabMax;
        static int rivStep, rivPush, rivDoor, rivPhase, rivDump, rivMoved, rivWp; static System.Collections.Generic.List<Vector3> rivRoute; static float rivTry;
        static readonly float[] RivAt = { 36f, 37f, 40f, 44f, 45f, 48f, 52f, 60f, 66f, 74f };
        static readonly string[] RivPart = { "DoorLeft", "DoorLeft", "DoorLeft", "DoorRight", "DoorRight", "DoorRight", "Bootlid", "Bootlid", "Hood", "Hood" };
        static readonly string[] RivState = { "Open door", "Mouse off", "~fermer", "Open door", "Mouse off", "~fermer", "Open hood", "~fermer", "Open hood", "~fermer" };

        // Essais (rivett) : met l'automate 'Use' de l'ouvrant VINP_<point> de 'car' dans 'state' ("~fermer" : son etat
        // de fermeture, celui du second clic -- "Open door 2"/"Open door 3" des portieres, "Drop" ou "State 2" des capots).
        static string RivSet(string car, string point, string state)
        {
            GameObject g = Game.FindAny(car + "/Assemblies/VINP_" + point);
            PlayMakerFSM use = null;
            if (g != null) foreach (PlayMakerFSM f in g.GetComponentsInChildren<PlayMakerFSM>(true)) if (f.FsmName == "Use") { use = f; break; }
            if (use == null) return point + " : pas d'automate Use";
            if (state == "~fermer")
                foreach (string s in new[] { "Open door 2", "Open door 3", "Drop", "State 2" })
                    if (use.Fsm.GetState(s) != null) { state = s; break; }
            if (use.Fsm.GetState(state) == null) return point + " : pas d'etat " + state;
            Game.SetState(use, state);
            return point + " -> " + state;
        }
        static Camera testCam;
        static float cdLog, fluLog, fluSum, fluSq, tapLog;
        static int fluN, fluStill;
        static Vector3 fluLast;
        static readonly System.Collections.Generic.Dictionary<Transform, float[]> fluParts = new System.Collections.Generic.Dictionary<Transform, float[]>();

        // Boitier de CD et CD : l'hote ouvre le boitier 1 (45 s), en sort le CD (49 s), le pose 1 m plus loin (52 s),
        // referme le boitier (60 s) ; les deux cotes notent le boitier et le CD toutes les 2 s.
        // Teletexte ([Test] Autotest=teletexte) : a 30 s l'hote allume le teletexte avec la telecommande (Use : 'Bool
        // test' -> Open), a 36 s il tape la page 123 (Input : 'Send code', NumberInt) ; les deux notent toutes les 2 s si
        // Systems/TV/Teletext est allume et sa page (Pages.PageNumber). Attendu chez l'invite : allume, 123.
        static float ttLog, ttLog2, partNext;
        static readonly System.Collections.Generic.Dictionary<string, int> partMax = new System.Collections.Generic.Dictionary<string, int>();
        static void TestTeletext(float t)
        {
            if (MWCoop.Net.Session.IsHost && t > 24f && step == 0)
            {
                step = 10;   // la tele d'abord (la telecommande ne marche que tele allumee)
                GameObject sw = Game.FindAny("YARD/Building/LIVINGROOM/TV/TVSwitch");
                PlayMakerFSM u = sw != null ? Game.FsmOn(sw, "Use") : null;
                if (u != null) { Game.SetState(u, "Switch"); Log.Info("autotest : tele -> " + u.ActiveStateName); }
            }
            // Comme le joueur : il vise la telecommande (Use 'Wait button', le pave Input s'allume), clique (USE :
            // teletexte allume), puis la vise de nouveau et tape 1, 2, 3 (Input 'Wait key' -KEY-> avec Key).
            GameObject rc = Game.FindAny("EQUIPMENTS/tv remote control(item1)");
            PlayMakerFSM use = rc != null ? Game.FsmOn(rc, "Use") : null, inp = rc != null ? Game.FsmOn(rc, "Input") : null;
            if (MWCoop.Net.Session.IsHost && use != null && inp != null)
            {
                if (t > 30f && step == 10) { step = 1; Log.Info("autotest : clic (envoye comme chez le joueur) " + WorldFsms.TestSend("tv remote control(item1)::Use", "Wait button", "USE", "Bool test")); }
                for (int k = 0; k < 3; k++)
                    if (t > 36f + k && step == 2 + k)
                    {
                        step = 3 + k;
                        Game.SetState(use, "Wait button");
                        if (!inp.enabled) inp.enabled = true;
                        Game.SetState(inp, "Wait key");
                        inp.FsmVariables.FindFsmString("Key").Value = (k + 1).ToString();
                        Log.Info("autotest : touche " + (k + 1) + " " + WorldFsms.TestEvent("tv remote control(item1)::Input", "KEY"));
                    }
                if (t > 35f && step == 1) step = 2;
            }
            if (t > 28f && t < 60f && t >= ttLog)
            {
                ttLog = t + 2f;
                GameObject tt = Game.FindAny("Systems/TV/Teletext");
                PlayMakerFSM pages = tt != null ? Game.FsmOn(tt, "Pages") : null;
                FsmInt pn = pages != null ? pages.FsmVariables.FindFsmInt("PageNumber") : null;
                Log.Info("autotest : teletexte " + (tt == null ? "absent" : tt.activeSelf ? "allume" : "eteint") + ", page " + (pn != null ? pn.Value.ToString() : "?"));
            }
        }

        // [Test] Autotest=prise : chacun note toutes les 3 s (16-90 s) les prises du chauffage moteur (cable plug) :
        // etat de 'Data', corps, position ; a 20 s, vidage de leurs parents et de leur prise de voiture (cibles.txt).
        static float plugLog;
        static bool plugDumped;
        static void TestPlug(float t)
        {
            if (t < 16f || t > 90f || t < plugLog) return;
            plugLog = t + 3f;
            var sb = new System.Text.StringBuilder();
            var dump = new System.Collections.Generic.List<string>();
            foreach (PlayMakerFSM f in Resources.FindObjectsOfTypeAll(typeof(PlayMakerFSM)))
            {
                if (f.hideFlags != HideFlags.None || !f.gameObject.name.StartsWith("cable plug")) continue;
                FsmGameObject sock = f.FsmVariables.FindFsmGameObject("Socket");
                sb.Append(" | ").Append(Recon.Path(f.transform)).Append("::").Append(f.FsmName).Append(' ').Append(f.ActiveStateName)
                  .Append(f.gameObject.activeInHierarchy ? "" : " (inactif)").Append(f.GetComponent<Rigidbody>() != null ? " corps" : " sans corps")
                  .Append(" en ").Append(f.transform.position.ToString("F1"))
                  .Append(sock != null && sock.Value != null ? " prise " + Recon.Path(sock.Value.transform) : "")
                  .Append(Replay.Owner(f) != null ? " [" + Replay.Owner(f) + "]" : "");
                if (!plugDumped)
                {
                    dump.Add(Recon.Path(f.transform.parent != null ? f.transform.parent : f.transform));
                    if (sock != null && sock.Value != null) dump.Add(Recon.Path(sock.Value.transform));
                }
            }
            Log.Info("autotest : prises t=" + t.ToString("F0") + (sb.Length > 0 ? sb.ToString() : " aucune"));
            if (!plugDumped && t >= 20f && dump.Count > 0 && Config.GetInt("Test", "VidagePrises", 0) != 0) { plugDumped = true; Log.Info("autotest : vidage prises " + Recon.DumpTargets(string.Join(";", dump.ToArray()))); }
            // Hote : debranche la prise de la maison ([Test] TestPrise, UT de son automate) a 34 s, la rebranche a 55 s.
            if (!MWCoop.Net.Session.IsHost) return;
            int want = t >= 34f && plugStep == 0 ? 1 : t >= 55f && plugStep == 1 ? 2 : 0;
            if (want == 0) return;
            plugStep = want;
            string ut = Config.Get("Test", "TestPrise", "HeaterCable1On");
            foreach (PlayMakerFSM f in Resources.FindObjectsOfTypeAll(typeof(PlayMakerFSM)))
            {
                if (f.hideFlags != HideFlags.None || f.FsmName != "Data" || !f.gameObject.name.StartsWith("cable plug")) continue;
                FsmString u = f.FsmVariables.FindFsmString("UT");
                if (u == null || u.Value != ut) continue;
                string before = f.ActiveStateName;
                Game.SetState(f, want == 1 ? "Heater off" : "Heater on");
                Log.Info("autotest : prise " + ut + (want == 1 ? " debranchee" : " rebranchee") + " ici (" + before + " -> " + f.ActiveStateName + ")");
                return;
            }
            Log.Info("autotest : prise " + ut + " introuvable");
        }
        static int plugStep;

        // [Test] Autotest=paie : enveloppe de paie d'un boulot ([Test] TestEnveloppe, defaut : livraison de bois 1),
        // allumee chez chacun a 25 s avec Money = 123 (comme le boulot rejoue l'aurait fait) ; a 27 s, vidage de son
        // automate ; a 36 s l'hote la prend (etat [Test] TestPrendre, defaut 'State 1', comme le clic) ; argent note
        // a 30, 45 et 60 s. Attendu : +123 chez l'hote, +123 chez l'invite (revenu partage), pas 246.
        static int payStep;
        static float payTrace;
        static GameObject payEnv;
        static int payPrep;
        static string payTraceK;
        static string PayPath { get { return Config.Get("Test", "TestEnveloppe", "JOBS/HouseWood1/LOD/CarPos/NPCWood/WoodCaller1/skeleton/pelvis/spine_middle/spine_upper/collar_right/shoulder_right/arm_right/hand_right/PayMoney"); } }
        static string Money()
        {
            FsmFloat c = FsmVariables.GlobalVariables.FindFsmFloat("PlayerMoney"), b = FsmVariables.GlobalVariables.FindFsmFloat("PlayerBankAccount");
            return "liquide " + (c != null ? c.Value.ToString("F2") : "?") + ", banque " + (b != null ? b.Value.ToString("F2") : "?");
        }
        static void TestPay(float t)
        {
            GameObject env = payEnv != null ? payEnv : Game.FindAny(PayPath);
            // Preparee a 20 s chez tous (montant, sortie de sous le PNJ, nom fixe) mais encore eteinte ; allumee a 25 s.
            // [Test] PaieRetard=n : chez l'invite, allumee n s plus tard (prise par l'hote avant qu'elle n'apparaisse).
            if (payPrep == 0 && t >= 20f)
            {
                payPrep = 1;
                if (env == null) { Log.Info("autotest : paie : enveloppe introuvable " + PayPath); payStep = 9; return; }
                // Montant pose AVANT l'allumage : au demarrage l'automate le compare a 0 et s'eteint s'il est nul.
                PlayMakerFSM u = Game.FsmOn(env, "Use");
                FsmFloat m = u != null ? u.FsmVariables.FindFsmFloat("Money") : null;
                if (m != null) m.Value = 123f;
                // Sortie de sous le PNJ (le boulot eteint le client tant qu'il n'est pas en cours), meme place chez tous.
                env.SetActive(false);
                env.transform.parent = null;
                env.name = "EssaiPaie";   // (pas "MWCoop..." : racines sautees par WorldFsms)
                payEnv = env;
                Log.Info("autotest : paie : " + Wallet.TestEnvelope(u));
            }
            if (payStep == 0 && payPrep == 1 && t >= 25f + (MWCoop.Net.Session.IsHost ? 0 : Config.GetInt("Test", "PaieRetard", 0)))
            {
                payStep = 1;
                env.SetActive(true);
                PlayMakerFSM u = Game.FsmOn(env, "Use");
                Log.Info("autotest : paie : enveloppe allumee (" + (u != null ? u.ActiveStateName : "pas d'automate") + ") ; " + Money());
            }
            if (payStep >= 1 && payStep < 4 && env != null && t >= payTrace)
            {
                payTrace = t + 0.5f;
                Transform off = null;
                for (Transform p = env.transform; p != null; p = p.parent) if (!p.gameObject.activeSelf) off = p;
                PlayMakerFSM u = Game.FsmOn(env, "Use");
                string k = (off != null ? "coupee a " + Recon.Path(off) : "allumee") + ", etat " + (u != null ? u.ActiveStateName : "?");
                if (k != payTraceK) { payTraceK = k; Log.Info("autotest : paie t=" + t.ToString("F1") + " : enveloppe " + k); }
            }
            if (payStep == 1 && t >= 27f) { payStep = 2; if (env != null) Log.Info("autotest : paie : vidage " + Recon.DumpTargets(Recon.Path(env.transform))); }
            if (payStep == 2 && t >= 30f) { payStep = 3; Log.Info("autotest : paie t=30 : " + Money()); }
            if (payStep == 3 && t >= 36f)
            {
                payStep = 4;
                if (MWCoop.Net.Session.IsHost && env != null)
                {
                    // Comme le clic (Wait button -USE-> State 1) : pas un rejeu.
                    PlayMakerFSM u = Game.FsmOn(env, "Use");
                    string before = u != null ? u.ActiveStateName : "?";
                    if (u != null) Game.SetState(u, Config.Get("Test", "TestPrendre", "State 1"));
                    Log.Info("autotest : paie : prise (" + before + " -> " + (u != null ? u.ActiveStateName : "?") + ") ; " + Money());
                }
            }
            if (payStep == 4 && t >= 45f) { payStep = 5; Log.Info("autotest : paie t=45 : " + Money() + (env != null ? ", enveloppe " + (env.activeInHierarchy ? "visible" : "cachee") : "")); }
            if (payStep == 5 && t >= 60f) { payStep = 6; Log.Info("autotest : paie t=60 : " + Money()); }
        }

        static void TestCd(float t)
        {
            GameObject cases = GameObject.Find("Systems/CDs");
            Transform c1 = cases != null ? cases.transform.Find("cd case(item1)") : null;
            if (MWCoop.Net.Session.IsHost)
            {
                if (step == 0 && t > 45f) { step = 1; Log.Info("autotest : cd, ouvre " + CaseClick()); }
                if (step == 1 && t > 49f) step = 2;   // le jeu libere le CD 1 s apres l'ouverture (CHECK -> Detach)
                if (step == 2 && t > 52f)
                {
                    step = 3;
                    GameObject cd = CdObject("CD1");
                    Rigidbody rb = cd != null ? cd.GetComponent<Rigidbody>() : null;
                    if (rb != null && c1 != null) { rb.position = c1.position + Vector3.up * 0.3f + c1.right * 1f; rb.velocity = Vector3.zero; Props.SoonScan(); }
                    Log.Info("autotest : cd, CD1 pose a cote : " + (rb != null ? rb.position.ToString("F2") : "pas de corps"));
                }
                if (step == 3 && t > 56f)
                {
                    // Remis dans le boitier comme le joueur : le point d'insertion a le CD tenu ({Part}, 'Find correct
                    // part'), 'Wait for assembly' attend le clic -> ASSEMBLE.
                    step = 4;
                    GameObject cd = CdObject("CD1");
                    Transform trig = c1 != null ? c1.Find("DiscTriggerCase1") : null;
                    PlayMakerFSM d = trig != null ? Game.FsmOn(trig.gameObject, "Data") : null;
                    if (d != null && cd != null)
                    {
                        d.FsmVariables.FindFsmGameObject("Part").Value = cd;
                        WorldFsms.TestClick("DiscTriggerCase1::Data", "Wait for assembly");
                        Log.Info("autotest : cd, remis dans le boitier " + WorldFsms.TestEvent("DiscTriggerCase1::Data", "ASSEMBLE"));
                    }
                    else Log.Info("autotest : cd, remise impossible (point " + (d != null) + ", CD " + (cd != null) + ")");
                }
                if (step == 4 && t > 62f) { step = 5; Log.Info("autotest : cd, referme " + CaseClick()); }
            }
            if (t > 40f && t < 75f && t - cdLog >= 2f)
            {
                cdLog = t;
                PlayMakerFSM use = c1 != null ? Game.FsmOn(c1.gameObject, "Use") : null;
                FsmBool open = use != null ? use.FsmVariables.FindFsmBool("Open") : null;
                GameObject cd = CdObject("CD1");
                Transform lid = c1 != null ? c1.Find("PivotTop") : null;
                Log.Info("autotest : cd, boitier 1 " + (use != null ? use.ActiveStateName + " Open=" + (open != null && open.Value) : "introuvable")
                         + (lid != null ? " couvercle " + lid.localEulerAngles.ToString("F0") : "")
                         + " ; CD1 " + (cd != null ? "sous " + (cd.transform.parent != null ? cd.transform.parent.name : "(racine)") + " en " + cd.transform.position.ToString("F2")
                                        + (cd.GetComponent<Rigidbody>() != null ? ", physique" : ", sans corps") : "introuvable")
                         + " ; suivis " + Props.Ids("w:cd:"));
            }
        }

        // Comme un vrai clic : 'Wait button' (survol) puis USE dans la meme image (sinon MousePickEvent ressort).
        // Le survol (MousePickEvent, curseur absent) est coupe le temps du clic.
        static string CaseClick()
        {
            GameObject c = GameObject.Find("Systems/CDs/cd case(item1)");
            PlayMakerFSM use = c != null ? Game.FsmOn(c, "Use") : null;
            FsmState wb = use != null ? use.Fsm.GetState("Wait button") : null;
            if (wb == null) return "boitier introuvable";
            var off = new System.Collections.Generic.List<FsmStateAction>();
            foreach (FsmStateAction a in wb.Actions) if (a != null && a.Enabled && a.GetType().Name == "MousePickEvent") { a.Enabled = false; off.Add(a); }
            try
            {
                WorldFsms.TestClick("cd case(item1)::Use", "Wait button");
                return WorldFsms.TestEvent("cd case(item1)::Use", "USE");
            }
            finally { foreach (FsmStateAction a in off) a.Enabled = true; }
        }

        static GameObject CdObject(string name)
        {
            foreach (PlayMakerFSM f in Resources.FindObjectsOfTypeAll(typeof(PlayMakerFSM)))
                if (f.FsmName == "Data" && f.gameObject.name == "cd(itemx)" && f.hideFlags == HideFlags.None)
                {
                    FsmString n = f.FsmVariables.FindFsmString("ThisCD");
                    if (n != null && n.Value == name) return f.gameObject;
                }
            return null;
        }
        static string clipShot;
        static float nextSleepLog; static int sleepWakeLogs; static float cdWatchUntil; static int sondeRoues; static bool pressDone;
        static string FieldF(object o, string name)   // (champ flottant prive, par reflexion)
        {
            var fi = o.GetType().GetField(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
            return fi == null ? "?" : fi.GetValue(o).ToString();
        }
        public static int PoseFlags;
        static int seenFlags = -1;
        static float shotAt = -1;
        static string bagId;
        static int step, step2, step3, portiereMark, spamLogged = -1;
        static float suiviLog;   // (AutomateSuivi)
        static bool brokeLock;
        static float maxUp = 0.5f, maxRot, coffreT;
        static bool coffreStartSet;
        static bool moveDone;
        static Vector3 coffreStart;
        static float lastPeriodic;

        // La camera du joueur suit l'avatar le plus proche (souris du jeu coupee pendant le test).
        static void LookAtGroup()
        {
            GameObject p = Game.PlayerGo;
            Transform cam = p != null ? p.transform.Find("Pivot/AnimPivot/Camera/FPSCamera") : null;
            if (cam == null) return;
            Vector3 sum = Vector3.zero;
            int n = 0;
            foreach (Avatar a in PlayerSync.Avatars) if (a.Root != null) { sum += a.Root.transform.position; n++; }
            if (n == 0) return;
            AimAt(p, cam, sum / n + Vector3.up * 1.0f);
        }

        static void FacePoint(string xyz)
        {
            GameObject p = Game.PlayerGo;
            Transform cam = p != null ? p.transform.Find("Pivot/AnimPivot/Camera/FPSCamera") : null;
            string[] c = xyz.Split(',');
            if (cam == null || c.Length < 3) return;
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            AimAt(p, cam, new Vector3(float.Parse(c[0], ci), float.Parse(c[1], ci), float.Parse(c[2], ci)));
        }

        internal static void AimAt(GameObject p, Transform cam, Vector3 target)
        {
            foreach (Behaviour b in p.GetComponents<Behaviour>()) if (b.GetType().Name.Contains("MouseLook")) b.enabled = false;
            foreach (Behaviour b in cam.GetComponents<Behaviour>()) if (b.GetType().Name.Contains("MouseLook")) b.enabled = false;
            Vector3 to = target - cam.position;
            p.transform.rotation = Quaternion.Euler(0, Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg, 0);
            cam.localRotation = Quaternion.Euler(-Mathf.Atan2(to.y, new Vector2(to.x, to.z).magnitude) * Mathf.Rad2Deg, 0, 0);
        }

        static void LookAtNearestAvatar()
        {
            GameObject p = Game.PlayerGo;
            if (p == null) return;
            Transform cam = p.transform.Find("Pivot/AnimPivot/Camera/FPSCamera");
            Avatar best = null;
            float bd = float.MaxValue;
            foreach (Avatar a in PlayerSync.Avatars)
            {
                if (a.Root == null) continue;
                float d = (a.Root.transform.position - p.transform.position).sqrMagnitude;
                if (d < bd) { bd = d; best = a; }
            }
            if (best == null || cam == null) return;
            var cc = p.GetComponent<CharacterController>();
            if (cc != null && bd < 2.5f * 2.5f)
            {
                Vector3 away = p.transform.position - best.Root.transform.position;
                away.y = 0;
                cc.SimpleMove((away.sqrMagnitude > 0.01f ? away.normalized : Vector3.forward) * 1.5f);
            }
            foreach (Behaviour b in p.GetComponents<Behaviour>()) if (b.GetType().Name.Contains("MouseLook")) b.enabled = false;
            foreach (Behaviour b in cam.GetComponents<Behaviour>()) if (b.GetType().Name.Contains("MouseLook")) b.enabled = false;
            Vector3 to = best.Root.transform.position + Vector3.up * 1.2f - cam.position;
            p.transform.rotation = Quaternion.Euler(0, Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg, 0);
            float pitch = -Mathf.Atan2(to.y, new Vector2(to.x, to.z).magnitude) * Mathf.Rad2Deg;
            cam.localRotation = Quaternion.Euler(pitch, 0, 0);
        }
    }
}
