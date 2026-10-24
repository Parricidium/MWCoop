using System.Collections.Generic;
using HutongGames.PlayMaker;
using MWCoop.Net;
using UnityEngine;

namespace MWCoop
{
    // Rallye de My Summer Car avec sa voiture du F10 (VAZ ou addon, copie de la Ruscko -- JD 24/10 : « prevois les memes
    // fonctionnalites, faire le rallye a plusieurs »). Le rallye du jeu ne connait que la SATSUMA (vidage ref/msc-v.250908-04) :
    //  - feuille d'inscription (RALLY/<jour>/StartArea/StartAreaObjects/RegisterForm<jour> 'Activate') : cliquable si la SATSUMA
    //    est a moins de 20 m (GetDistance) ;
    //  - feuille (Sheets/RallyformAmateur, RallyformJunior 'Setup') : arceau, harnais, pneus de la SATSUMA, extincteur, controle
    //    technique -> ButtonConfirm 'Button' (ReqRollcage, ReqHarness, ReqTires, ReqExtinguisher, ReqInspected) ; casque et
    //    sobriete restent ceux du joueur ;
    //  - parc ferme (RALLY/ParcFerme 'Logic') : SATSUMA a moins de 25-50 m, sinon une minute de penalite ;
    //  - spectateurs (HumanTriggerCrime 'CarHit', etat 'On rally?') : renverse hors de la SATSUMA = crime, meme en speciale.
    // Les points de passage et le chrono comptent n'importe quel vehicule conduit (PlayerCurrentVehicle non vide).
    // Ici : la voiture du rallye est celle des deux (SATSUMA ou la sienne du F10) la plus proche de la feuille d'inscription
    // (ou du parc ferme) ; avec la sienne, les GetDistance de la feuille et du parc ferme visent elle, la feuille la tient pour
    // une voiture de rallye equipee, et les spectateurs la comptent comme la SATSUMA. Le classement entre humains : RallyMsc.
    public static class RallyMscVaz
    {
        static readonly string[] Forms = { "RALLY/Saturday/StartArea/StartAreaObjects/RegisterFormSaturday", "RALLY/Sunday/StartArea/StartAreaObjects/RegisterFormSunday" };
        static readonly string[] SheetNames = { "Sheets/RallyformAmateur", "Sheets/RallyformJunior" };
        static readonly string[] Reqs = { "ReqRollcage", "ReqHarness", "ReqTires", "ReqExtinguisher", "ReqInspected" };
        static readonly List<HutongGames.PlayMaker.Actions.GetDistance> dists = new List<HutongGames.PlayMaker.Actions.GetDistance>();
        static readonly List<HutongGames.PlayMaker.Actions.StringCompare> crimes = new List<HutongGames.PlayMaker.Actions.StringCompare>();
        static GameObject satsuma, park;
        static readonly GameObject[] sheets = new GameObject[2];
        static float next, nextScan;
        static string pointed = "";
        static bool sheetLogged;

        public static void OnLevelLoaded() { dists.Clear(); crimes.Clear(); satsuma = park = last = null; sheets[0] = sheets[1] = null; next = nextScan = 0f; pointed = ""; sheetLogged = false; testStep = 0; }

        static bool IsCar(GameObject g) { return g != null && (g.name.StartsWith("SATSUMA") || g.name.StartsWith(Vaz.Prefix)); }

        // Actions a rediriger : GetDistance vers la SATSUMA (feuilles, parc ferme), StringCompare « Satsuma » des spectateurs
        // (relu toutes les 10 s : la reserve de spectateurs en detruit et en recree).
        static void Scan(float now)
        {
            if (now < nextScan) return;
            nextScan = now + 10f;
            dists.Clear(); crimes.Clear();
            if (park == null) park = Game.FindAny("RALLY/ParcFerme");
            for (int i = 0; i < 2; i++) if (sheets[i] == null) sheets[i] = Game.FindAny(SheetNames[i]);
            GameObject rally = Game.FindAny("RALLY");
            if (rally == null) return;
            foreach (PlayMakerFSM f in rally.GetComponentsInChildren<PlayMakerFSM>(true))
            {
                bool form = f.FsmName == "Activate" && f.gameObject.name.StartsWith("RegisterForm"), parc = f.FsmName == "Logic" && f.gameObject.name == "ParcFerme", crime = f.FsmName == "CarHit";
                if (!form && !parc && !crime) continue;
                // (automate jamais demarre -- objet jamais allume : ses actions ne se chargent pas encore ; relu dans 10 s)
                if (f.Fsm == null || string.IsNullOrEmpty(f.ActiveStateName)) continue;
                try
                {
                    foreach (FsmState st in f.FsmStates)
                        if (st != null && st.Actions != null)
                            foreach (FsmStateAction a in st.Actions)
                            {
                                var gd = a as HutongGames.PlayMaker.Actions.GetDistance;
                                if (gd != null && gd.target != null && !gd.target.UseVariable && IsCar(gd.target.Value)) dists.Add(gd);
                                var sc = a as HutongGames.PlayMaker.Actions.StringCompare;
                                if (crime && sc != null && sc.stringVariable != null && sc.stringVariable.Name == "PlayerCurrentVehicle" && sc.compareTo != null) crimes.Add(sc);
                            }
                }
                catch (System.Exception e) { Log.Warn("rallye (MSC) : " + f.gameObject.name + "::" + f.FsmName + " illisible (" + e.GetType().Name + ")"); }
            }
            if (park == null) park = Game.FindAny("RALLY/ParcFerme");   // (feuilles et parc ferme cherches avant le balayage, qui peut echouer)
            for (int i = 0; i < 2; i++) if (sheets[i] == null) sheets[i] = Game.FindAny(SheetNames[i]);
        }

        // Voiture du rallye : la mienne du F10 si elle est plus pres de la feuille d'inscription (ou du parc ferme) que la SATSUMA ;
        // ni feuille ni parc ferme (speciales, apres l'inscription) : le dernier choix, tant que la voiture existe.
        static GameObject last;
        static GameObject Want(out bool mine)
        {
            mine = false;
            if (satsuma == null) satsuma = Game.FindAny("SATSUMA(557kg, 248)");
            Transform car = Vaz.CarOf(Session.Active ? Session.LocalId : 0);
            if (car == null) return last = satsuma;
            Vector3 at = Vector3.zero; bool any = false;
            foreach (string fp in Forms) { GameObject g = Game.FindAny(fp); if (g != null && g.activeInHierarchy) { at = g.transform.position; any = true; break; } }
            if (!any && park != null && park.activeInHierarchy) { at = park.transform.position; any = true; }
            if (!any) { mine = last == car.gameObject; return last != null ? last : satsuma; }
            float dm = Vector3.Distance(car.position, at), ds = satsuma != null ? Vector3.Distance(satsuma.transform.position, at) : float.MaxValue;
            mine = dm < ds;
            return last = mine ? car.gameObject : satsuma;
        }

        public static void Update()
        {
            if (!Game.Msc || !PlayerSync.InGame) return;
            float now = Time.realtimeSinceStartup;
            bool mine = pointed.StartsWith(Vaz.Prefix);
            if (now >= next)
            {
                next = now + 1f;
                Scan(now);
                GameObject want = Want(out mine);
                if (want != null)
                {
                    int n = 0;
                    foreach (HutongGames.PlayMaker.Actions.GetDistance gd in dists) if (gd.target.Value != want) { gd.target.Value = want; n++; }
                    string veh = mine ? "Ruscko" : "Satsuma";   // (vehicule du joueur au volant d'une copie de la Ruscko : son automate PlayerTrigger)
                    foreach (HutongGames.PlayMaker.Actions.StringCompare sc in crimes) if ((sc.compareTo.Value == "Satsuma" || sc.compareTo.Value == "Ruscko") && sc.compareTo.Value != veh) { sc.compareTo.Value = veh; n++; }
                    if (want.name != pointed)
                    {
                        pointed = want.name;
                        Log.Info("rallye (MSC) : la voiture du rallye est " + want.name + " (" + dists.Count + " distances, " + crimes.Count + " spectateurs, " + n + " changements)");
                        if (mine) Hud.Toast(Lang.T("Rallye : inscription, parc fermé et spéciales avec ta voiture", "Rally: registration, parc fermé and stages with your car"));
                    }
                }
            }
            // feuille ouverte avec ma voiture : elle est equipee pour le rallye (les croix de la SATSUMA effacees)
            if (!mine) { sheetLogged = false; return; }
            foreach (GameObject sh in sheets)
            {
                if (sh == null || !sh.activeInHierarchy) continue;
                Transform bt = sh.transform.Find("ButtonConfirm");
                PlayMakerFSM bf = bt != null ? Game.FsmOn(bt.gameObject, "Button") : null;
                if (bf != null) foreach (string r in Reqs) { FsmBool v = bf.FsmVariables.FindFsmBool(r); if (v != null && !v.Value) v.Value = true; }
                Transform sy = sh.transform.Find("Symbols");
                if (sy != null) for (int i = 0; i < 5; i++) { Transform x = sy.Find("x" + i); if (x != null && x.gameObject.activeSelf) x.gameObject.SetActive(false); }
                if (!sheetLogged) { sheetLogged = true; Log.Info("rallye (MSC) : feuille d'inscription ouverte avec " + pointed + " : equipee pour le rallye"); }
            }
        }

        // [Test] Autotest=vazrallye (solo, MSC) : 20 s samedi 12 h ; 26 s a la tente de Fleetari ; 30 s voiture du F10 ([Test]
        // VazModele) ; 40 s releve et capture vazrallye-place ; 41 s feuille d'inscription ouverte ; 44 s ce qu'elle exige
        // (Req*), capture vazrallye-feuille ; 47 s feuille fermee.
        static int testStep;
        static readonly Vector3 FormAt = new Vector3(-1351.9f, 2.6f, 1234.9f);
        public static void Test(string mode, float t)
        {
            if (mode != "vazrallye") return;
            if (testStep == 0 && t > 20f)
            {
                testStep = 1;
                FsmInt day = FsmVariables.GlobalVariables.FindFsmInt("GlobalDay");
                if (day != null) day.Value = 6;
                Cheats.SetHour(12); Log.Info("autotest vazrallye : samedi 12 h");
            }
            if (testStep == 1 && t > 26f)
            {
                testStep = 2;
                Cheats.GoPlace(new Cheats.Place { Key = "tente", Fr = "Tente de Fleetari", En = "Fleetari's tent", At = FormAt + new Vector3(6f, 6f, 3f), Face = FormAt });
                Log.Info("autotest vazrallye : joueur a la tente en " + (Game.PlayerT != null ? Game.PlayerT.position.ToString("F1") : "?"));
            }
            if (testStep == 2 && t > 30f) { testStep = 3; Log.Info("autotest vazrallye : voiture -> " + Vaz.Request(3, Config.GetInt("Test", "VazModele", 5))); }
            if (testStep == 3 && t > 40f)
            {
                testStep = 4;
                GameObject form = Game.FindAny(Forms[0]);
                Transform car = Vaz.CarOf(0);
                Log.Info("autotest vazrallye : feuille " + (form != null ? (form.activeInHierarchy ? "active" : "eteinte") : "absente") + ", voiture du rallye " + pointed + ", " + dists.Count + " distances, " + crimes.Count + " spectateurs"
                         + (car != null && form != null ? ", ma voiture a " + Vector3.Distance(car.position, form.transform.position).ToString("F1") + " m de la feuille" : ""));
                Autotest.CaptureSoon("vazrallye-place", 0.5f);
            }
            if (testStep == 4 && t > 41f)
            {
                testStep = 5;
                GameObject sh = Game.FindAny(SheetNames[0]);
                if (sh != null) { Game.SetGlobalBool("PlayerInMenu", true); sh.SetActive(true); }
                Log.Info("autotest vazrallye : feuille d'inscription " + (sh != null ? "ouverte" : "introuvable"));
            }
            if (testStep == 5 && t > 44f)
            {
                testStep = 6;
                GameObject sh = Game.FindAny(SheetNames[0]);
                Transform bt = sh != null ? sh.transform.Find("ButtonConfirm") : null;
                PlayMakerFSM bf = bt != null ? Game.FsmOn(bt.gameObject, "Button") : null;
                var sb = new System.Text.StringBuilder("autotest vazrallye : la feuille exige");
                if (bf != null) foreach (string r in Reqs) { FsmBool v = bf.FsmVariables.FindFsmBool(r); sb.Append(' ').Append(r).Append('=').Append(v != null ? v.Value.ToString() : "?"); }
                Log.Info(sb.ToString());
                Autotest.CaptureSoon("vazrallye-feuille", 0.3f);
            }
            if (testStep == 6 && t > 47f)
            {
                testStep = 7;
                GameObject sh = Game.FindAny(SheetNames[0]);
                if (sh != null) sh.SetActive(false);
                Game.SetGlobalBool("PlayerInMenu", false);
            }
        }
    }
}
