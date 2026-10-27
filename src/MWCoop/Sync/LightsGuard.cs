using System.Collections.Generic;
using HutongGames.PlayMaker;
using UnityEngine;

namespace MWCoop
{
    // Phares a la touche (LightsDrive, L) : l'automate ButtonLightModes d'une voiture (SORBET, CORRIS, mods...) passe dans son
    // etat « Sitting in car » quand PlayerCurrentVehicle vaut son CarName, et n'ecoute la touche que la. Deux trous :
    //  - en co-op il pouvait y rester alors que le joueur local etait dans une AUTRE voiture (PlayerStop reste vrai : il n'en
    //    sort jamais) : L allumait les feux de toutes les voitures visitees (retour de JD, 18/10, 0.97.7) ;
    //  - les voitures clonees d'une autre (voitures privees de JD : Escort, 205, S4... copies de la SORBET) gardent le MEME
    //    CarName : dans l'Escort, la 205 se croyait occupee aussi et L allumait ses feux (retour de JD, 18/10, 0.98.3).
    // Ici la voiture est reconnue par l'objet ou le joueur local est vraiment (VehicleSync : volant, Seats : place passager),
    // pas par son nom : chaque image, l'automate d'une autre voiture recoit un CarName qui ne peut pas correspondre (et quitte
    // « Sitting in car ») ; le sien est rendu des que le joueur y monte. A pied, tout est rendu : le jeu fait le reste. Sans
    // voiture reconnue mais un PlayerCurrentVehicle (mod sans DriveTrigger...) : comparaison des noms, comme avant.
    // Les automates a garde (KEKMET, GIFU : « Check down » compare la voiture avant d'agir) n'ont pas d'etat « Sitting in car ».
    public static class LightsGuard
    {
        const string Away = "MWCoop-ailleurs";
        class Btn { public PlayMakerFSM F; public FsmString CarName; public string Orig, Car; public Transform Root; public bool Blocked; public float NextLog; }
        static readonly List<Btn> btns = new List<Btn>();
        static float nextScan = -1;
        static FsmString cur;

        public static void OnLevelLoaded() { btns.Clear(); cmps.Clear(); cur = null; nextScan = PlayerSync.InGame ? Time.realtimeSinceStartup + 8f : -1; }

        static void Scan()
        {
            btns.RemoveAll(b => b.F == null);
            foreach (Object o in Game.FsmsNamed("Use"))
            {
                PlayMakerFSM f = o as PlayMakerFSM;
                if (f == null || f.FsmName != "Use" || f.gameObject.name != "ButtonLightModes") continue;
                if (btns.Exists(b => b.F == f)) continue;
                if (f.Fsm == null || !f.Fsm.Initialized || f.Fsm.GetState("Sitting in car") == null || f.Fsm.GetState("Not sitting") == null) continue;
                FsmString cn = f.FsmVariables.FindFsmString("CarName");
                if (cn == null || cn.Value == Away) continue;
                btns.Add(new Btn { F = f, CarName = cn, Orig = cn.Value ?? "", Car = Recon.Path(f.transform).Split('/')[0] });
            }
            // voiture de chaque automate (une piece posee peut changer de voiture : refait a chaque releve) ; null : piece libre
            foreach (Btn b in btns) b.Root = VehicleSync.CarRoot(b.F.transform);
            var names = new Dictionary<string, int>();
            foreach (Btn b in btns) { int n; names.TryGetValue(b.Orig, out n); names[b.Orig] = n + 1; }
            foreach (var kv in names) if (kv.Value > 1 && logged.Add(kv.Key)) Log.Info("phares : " + kv.Value + " voitures partagent le nom '" + kv.Key + "' (clones) : reconnues par l'objet, pas par le nom");
            ScanCompares();
        }

        // Commandes ecoutant la touche des phares (HighBeam) qui verifient la voiture du joueur par son nom : sur My Summer Car,
        // l'automate Use du bouton Lights (Ruscko, Kekmet, Jonnez...) passe a « Check down » ou « State 1 » a CHAQUE appui, dans
        // toutes les voitures, et compare PlayerCurrentVehicle a un nom ecrit dans l'action. Les voitures du pack rallye de JD
        // (Audi, Escort, 205, Celica... clones de la Ruscko) comparent toutes « Ruscko » : un appui dans l'une allumait les
        // phares de toutes (retour de JD, 19/10, MSC). Comme pour ButtonLightModes : la voiture est reconnue par l'objet, le nom
        // compare des autres voitures recoit une valeur qui ne peut pas correspondre.
        class Cmp { public FsmString To; public string Orig, Car; public Transform Root; public PlayMakerFSM F; public bool Blocked; }
        static readonly List<Cmp> cmps = new List<Cmp>();
        static void ScanCompares()
        {
            cmps.RemoveAll(c => c.F == null);
            foreach (Object o in Game.FsmsNamed("Use"))
            {
                PlayMakerFSM f = o as PlayMakerFSM;
                if (f == null || f.FsmName != "Use" || f.Fsm == null || !f.Fsm.Initialized || cmps.Exists(c => c.F == f)) continue;
                bool key = false;
                var found = new List<HutongGames.PlayMaker.Actions.StringCompare>();
                foreach (FsmState st in f.Fsm.States)
                {
                    FsmStateAction[] acts;
                    try { acts = st.Actions; } catch { continue; }
                    foreach (FsmStateAction a in acts)
                    {
                        var gb = a as HutongGames.PlayMaker.Actions.GetButtonDown;
                        if (gb != null && gb.buttonName != null && gb.buttonName.Value == "HighBeam") key = true;
                        var sc = a as HutongGames.PlayMaker.Actions.StringCompare;
                        if (sc != null && sc.stringVariable != null && sc.stringVariable.Name == "PlayerCurrentVehicle" && sc.compareTo != null && !string.IsNullOrEmpty(sc.compareTo.Value)) found.Add(sc);
                    }
                }
                if (!key) continue;
                foreach (var sc in found)
                    if (sc.compareTo.Value != Away) cmps.Add(new Cmp { To = sc.compareTo, Orig = sc.compareTo.Value, F = f, Car = Recon.Path(f.transform).Split('/')[0] });
            }
            foreach (Cmp c in cmps) c.Root = VehicleSync.CarRoot(c.F.transform);
            var names = new Dictionary<string, int>();
            foreach (Cmp c in cmps) { int n; names.TryGetValue(c.Orig, out n); names[c.Orig] = n + 1; }
            foreach (var kv in names) if (kv.Value > 1 && logged.Add("cmp:" + kv.Key)) Log.Info("phares : " + kv.Value + " boutons de phares comparent la voiture du joueur a '" + kv.Key + "' : reconnus par l'objet, pas par le nom");
        }
        static void GuardCompares(Transform here, bool inCar)
        {
            foreach (Cmp c in cmps)
            {
                if (c.F == null || c.To == null) continue;
                // (a pied, ou voiture du bouton inconnue : le jeu decide, comme avant)
                bool ours = !inCar || here == null || c.Root == null || c.Root == here;
                if (ours) { if (c.Blocked) { c.To.Value = c.Orig; c.Blocked = false; } continue; }
                if (!c.Blocked) { c.To.Value = Away; c.Blocked = true; }
            }
        }
        static readonly HashSet<string> logged = new HashSet<string>();

        public static void Update()
        {
            if (nextScan < 0 || !PlayerSync.InGame || TestOff) return;
            float now = Time.realtimeSinceStartup;
            if (now >= nextScan) { nextScan = now + 10f; Scan(); }
            if (btns.Count == 0 && cmps.Count == 0 || Game.Saving) return;
            if (cur == null) { cur = FsmVariables.GlobalVariables.FindFsmString("PlayerCurrentVehicle"); if (cur == null) return; }
            Transform here = VehicleSync.LocalDrivingRoot ?? Seats.SeatedCar;
            string mine = cur.Value ?? "";
            bool inCar = here != null || mine.Length > 0;
            GuardCompares(here, inCar);
            foreach (Btn b in btns)
            {
                if (b.F == null) continue;
                // voiture reconnue des deux cotes : par l'objet ; sinon (piece pas montee, mod sans voiture suivie) : par le nom, comme avant
                bool ours = !inCar || (here != null && b.Root != null ? b.Root == here : mine == b.Orig);
                if (ours)
                {
                    if (b.Blocked) { b.CarName.Value = b.Orig; b.Blocked = false; }
                    continue;
                }
                if (!b.Blocked) { b.CarName.Value = Away; b.Blocked = true; }
                if (!b.F.enabled || !b.F.gameObject.activeInHierarchy || b.F.ActiveStateName != "Sitting in car") continue;
                try { Game.SetState(b.F, "Not sitting"); }
                catch (System.Exception e) { Log.Warn("phares : " + b.Car + " : " + e.Message); continue; }
                if (now >= b.NextLog) { b.NextLog = now + 60f; Log.Info("phares : " + b.Car + " ecoutait la touche sans le joueur dedans (joueur dans " + (here != null ? here.name : "'" + mine + "'") + ") : remis au repos"); }
            }
        }

        // Autotest « pharesclone » : la CORRIS recoit le CarName de la SORBET des le chargement (comme une voiture clonee : memes
        // noms), le joueur prend le volant de la SORBET ; a 32 s l'etat des automates (attendu : SORBET « Sitting in car », CORRIS
        // au repos). [Test] SansGarde=1 : garde coupee, pour voir le defaut (CORRIS aussi « Sitting in car »).
        static int testStep;
        public static bool TestOff;
        public static void Test(string mode, float t)
        {
            if (mode != "pharesclone") return;
            string car = Config.Get("Test", "TestVoiture", "SORBET(190-200psi)");
            if (testStep == 0 && t > 5f)
            {
                testStep = 1;
                TestOff = Config.GetInt("Test", "SansGarde", 0) != 0;
                foreach (Object o in Game.FsmsNamed("Use"))
                {
                    PlayMakerFSM f = o as PlayMakerFSM;
                    if (f == null || f.gameObject.name != "ButtonLightModes" || !Recon.Path(f.transform).StartsWith(Config.Get("Test", "ClonePhares", "CARPARTS"))) continue;
                    FsmString cn = f.FsmVariables.FindFsmString("CarName");
                    if (cn == null || cn.Value == "Sorbet") continue;
                    Log.Info("autotest : pharesclone, " + Recon.Path(f.transform) + " '" + cn.Value + "' -> 'Sorbet'" + (TestOff ? " (garde coupee)" : ""));
                    cn.Value = "Sorbet";
                    // (rattachee sous une autre voiture suivie : comme une voiture clonee, meme nom, autre objet)
                    GameObject host = Game.FindAny(Config.Get("Test", "CloneSur", "BACHGLOTZ(1905kg)"));
                    if (host != null) { f.transform.parent = host.transform; Log.Info("autotest : pharesclone, rattachee sous " + host.name); }
                    break;
                }
            }
            if (testStep == 1 && t > 21f) { testStep = 2; Log.Info("autotest : pharesclone, " + VehicleSync.TestEnter(car, false)); }
            if ((testStep == 2 && t > 24f) || (testStep == 3 && t > 26f)) { testStep++; Log.Info("autotest : pharesclone, au volant : " + VehicleSync.TestEnter(car, true)); }
            if (testStep == 4 && t > 32f)
            {
                testStep = 5;
                var sb = new System.Text.StringBuilder();
                foreach (Object o in Game.FsmsNamed("Use"))
                {
                    PlayMakerFSM f = o as PlayMakerFSM;
                    if (f == null || f.gameObject.name != "ButtonLightModes") continue;
                    string root = Recon.Path(f.transform).Split('/')[0];
                    FsmString cn = f.FsmVariables.FindFsmString("CarName");
                    sb.Append(root).Append('[').Append(cn != null ? cn.Value : "?").Append("]:").Append(f.ActiveStateName).Append(' ');
                }
                Log.Info("autotest : pharesclone, phares : " + sb + "; voiture du joueur " + (VehicleSync.LocalDrivingRoot != null ? VehicleSync.LocalDrivingRoot.name : "aucune"));
            }
        }

        // Essais (autotest « mscmort ») : boutons de phares a comparaison de nom, et appui simule sur la touche (ON, puis ON
        // encore : relachee tout de suite) dans chacun.
        public static string CmpState()
        {
            var sb = new System.Text.StringBuilder("phares (noms compares) :");
            foreach (Cmp c in cmps)
            {
                if (c.F == null) continue;
                FsmInt sel = c.F.FsmVariables.FindFsmInt("Selection");
                FsmBool on = c.F.FsmVariables.FindFsmBool("LightsOn");
                sb.Append(' ').Append(c.Car).Append("['").Append(c.To.Value).Append("'").Append(c.Blocked ? " bloque" : "").Append("] ").Append(c.F.ActiveStateName)
                  .Append(sel != null ? " feux " + sel.Value : on != null ? " feux " + on.Value : "").Append(';');
            }
            return sb.ToString();
        }
        public static string TestPress()
        {
            int n = 0;
            foreach (PlayMakerFSM f in new HashSet<PlayMakerFSM>(cmps.ConvertAll(c => c.F)))
            {
                if (f == null || !f.enabled || f.ActiveStateName != "Wait player 2") continue;
                f.SendEvent("ON"); f.SendEvent("ON"); n++;
            }
            return "phares : touche simulee dans " + n + " bouton(s)";
        }

        // Autotest « mscphares » (2 joueurs, chacun au volant de sa voiture) : la touche des phares simulee comme le jeu la lit --
        // chaque automate Use dont l'etat courant lit HighBeam (GetButtonDown) recoit son evenement, dans TOUS les vehicules ;
        // « court » : relachee 0,15 s apres (GetButtonUp des etats qui l'attendent), « long » : tenue (le delai de l'automate joue).
        // [Test] PharesVoiture=HAYOSIKO(1500kg, 250) ; PharesAppuis=55:court,110:long (temps de chacun) ; PharesReleves=95,125 :
        // feux de chaque bouton (Selection / LightsOn) et voiture du joueur. Attendu : seuls les feux de la voiture de celui qui
        // appuie changent, chez lui et chez l'autre (avant le 27/10 : ceux de la voiture de l'autre aussi, chez l'autre).
        static int keyStep, keyLogStep;
        public static bool KeyReleve;
        static float keyReleaseAt = -1;
        public static void TestKey(string mode, float t)
        {
            if (mode != "mscphares") return;
            string car = Config.Get("Test", "PharesVoiture", "SATSUMA(557kg, 248)");
            if (keyStep == 0 && t > 30f) { keyStep = 1; Log.Info("autotest : mscphares, " + VehicleSync.TestEnter(car, false)); }
            if ((keyStep == 1 && t > 33f) || (keyStep == 2 && t > 35f)) { keyStep++; Log.Info("autotest : mscphares, au volant : " + VehicleSync.TestEnter(car, true)); }
            string[] presses = Config.Get("Test", "PharesAppuis", "").Split(new[] { ',' }, System.StringSplitOptions.RemoveEmptyEntries);
            int pi = keyStep - 3;
            if (keyStep >= 3 && pi < presses.Length)
            {
                string[] pp = presses[pi].Split(':');
                float at; float.TryParse(pp[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out at);
                if (t > at)
                {
                    keyStep++;
                    bool shortP = pp.Length < 2 || pp[1] != "long";
                    Log.Info("autotest : mscphares, appui " + (shortP ? "court" : "long") + " (PlayerCurrentVehicle '" + Cur() + "') : " + SendKey("GetButtonDown"));
                    if (shortP) keyReleaseAt = t + 0.15f;
                }
            }
            if (keyReleaseAt > 0 && t > keyReleaseAt) { keyReleaseAt = -1; Log.Info("autotest : mscphares, touche relachee : " + SendKey("GetButtonUp")); }
            string[] logs = Config.Get("Test", "PharesReleves", "").Split(new[] { ',' }, System.StringSplitOptions.RemoveEmptyEntries);
            if (keyLogStep < logs.Length)
            {
                float at; float.TryParse(logs[keyLogStep], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out at);
                if (t > at) { keyLogStep++; KeyReleve = true; Log.Info("autotest : mscphares, releve " + keyLogStep + " (voiture du joueur " + (VehicleSync.LocalDrivingRoot != null ? VehicleSync.LocalDrivingRoot.name : "aucune") + ") : " + LightStates()); }
            }
        }
        static string Cur() { FsmString c = FsmVariables.GlobalVariables.FindFsmString("PlayerCurrentVehicle"); return c != null ? c.Value : "?"; }
        static string SendKey(string action)
        {
            var sb = new System.Text.StringBuilder();
            foreach (Object o in Game.FsmsNamed("Use"))
            {
                PlayMakerFSM f = o as PlayMakerFSM;
                if (f == null || !f.enabled || !f.gameObject.activeInHierarchy || f.Fsm == null || f.Fsm.ActiveState == null) continue;
                FsmStateAction[] acts;
                try { acts = f.Fsm.ActiveState.Actions; } catch { continue; }
                foreach (FsmStateAction a in acts)
                {
                    FsmEvent ev = null; FsmString bn = null;
                    var d = a as HutongGames.PlayMaker.Actions.GetButtonDown; if (d != null && action == "GetButtonDown") { ev = d.sendEvent; bn = d.buttonName; }
                    var u = a as HutongGames.PlayMaker.Actions.GetButtonUp; if (u != null && action == "GetButtonUp") { ev = u.sendEvent; bn = u.buttonName; }
                    if (ev == null || bn == null || bn.Value != "HighBeam") continue;
                    sb.Append(Recon.Path(f.transform).Split('/')[0]).Append(':').Append(f.ActiveStateName).Append("-").Append(ev.Name).Append(' ');
                    f.SendEvent(ev.Name);
                    break;
                }
            }
            return sb.Length > 0 ? sb.ToString() : "aucun bouton";
        }
        static string LightStates()
        {
            var sb = new System.Text.StringBuilder();
            foreach (Object o in Game.FsmsNamed("Use"))
            {
                PlayMakerFSM f = o as PlayMakerFSM;
                if (f == null || f.Fsm == null) continue;
                string n = f.gameObject.name;
                if (n != "Lights" && n != "LightModes" && n != "ButtonLights" && n != "ButtonLightModes") continue;
                FsmInt sel = f.FsmVariables.FindFsmInt("Selection");
                FsmBool on = f.FsmVariables.FindFsmBool("LightsOn");
                string val = on != null ? (on.Value ? "allumes" : "eteints") : sel != null ? sel.Value.ToString() : "?";
                sb.Append(Recon.Path(f.transform).Split('/')[0]).Append('=').Append(val).Append('[').Append(f.ActiveStateName).Append("] ");
            }
            return sb.ToString();
        }

        // Essais : etat des automates (autotest « phares »).
        public static string State()
        {
            var sb = new System.Text.StringBuilder();
            foreach (Btn b in btns) if (b.F != null) sb.Append(b.Car).Append(':').Append(b.F.ActiveStateName).Append(b.Blocked ? "(bloque)" : "").Append(' ');
            return sb.ToString();
        }
    }
}
