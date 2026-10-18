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

        public static void OnLevelLoaded() { btns.Clear(); cur = null; nextScan = PlayerSync.InGame ? Time.realtimeSinceStartup + 8f : -1; }

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
        }
        static readonly HashSet<string> logged = new HashSet<string>();

        public static void Update()
        {
            if (nextScan < 0 || !PlayerSync.InGame || TestOff) return;
            float now = Time.realtimeSinceStartup;
            if (now >= nextScan) { nextScan = now + 10f; Scan(); }
            if (btns.Count == 0 || Game.Saving) return;
            if (cur == null) { cur = FsmVariables.GlobalVariables.FindFsmString("PlayerCurrentVehicle"); if (cur == null) return; }
            Transform here = VehicleSync.LocalDrivingRoot ?? Seats.SeatedCar;
            string mine = cur.Value ?? "";
            bool inCar = here != null || mine.Length > 0;
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

        // Essais : etat des automates (autotest « phares »).
        public static string State()
        {
            var sb = new System.Text.StringBuilder();
            foreach (Btn b in btns) if (b.F != null) sb.Append(b.Car).Append(':').Append(b.F.ActiveStateName).Append(b.Blocked ? "(bloque)" : "").Append(' ');
            return sb.ToString();
        }
    }
}
