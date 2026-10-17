using System.Collections.Generic;
using HutongGames.PlayMaker;
using UnityEngine;

namespace MWCoop
{
    // Phares a la touche (LightsDrive, L) : l'automate ButtonLightModes d'une voiture (SORBET, CORRIS, mods...) n'ecoute la touche
    // que dans son etat « Sitting in car », qu'il quitte quand PlayerStop retombe (joueur sorti). En co-op il peut y rester
    // alors que le joueur local est dans une AUTRE voiture (PlayerStop reste vrai : il n'en sort jamais) : L allumait alors
    // les feux de toutes les voitures visitees, meme avec un autre joueur dedans (retour de JD, 18/10). Ici, comme le jeu le
    // ferait a pied : un automate en « Sitting in car » dont la voiture (variable CarName) n'est pas celle du joueur local
    // (PlayerCurrentVehicle) est remis en « Not sitting ». Les automates a garde (KEKMET, GIFU : « Check down » compare la
    // voiture avant d'agir) n'ont pas ce probleme et n'ont pas d'etat « Sitting in car ».
    public static class LightsGuard
    {
        class Btn { public PlayMakerFSM F; public FsmString CarName; public string Car; public float NextLog; }
        static readonly List<Btn> btns = new List<Btn>();
        static float nextScan = -1, nextLook;
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
                if (cn == null) continue;
                btns.Add(new Btn { F = f, CarName = cn, Car = Recon.Path(f.transform).Split('/')[0] });
            }
        }

        public static void Update()
        {
            if (nextScan < 0 || !PlayerSync.InGame) return;
            float now = Time.realtimeSinceStartup;
            if (now >= nextScan) { nextScan = now + 10f; Scan(); }
            if (now < nextLook) return;
            nextLook = now + 0.5f;
            if (cur == null) { cur = FsmVariables.GlobalVariables.FindFsmString("PlayerCurrentVehicle"); if (cur == null) return; }
            foreach (Btn b in btns)
            {
                if (b.F == null || !b.F.enabled || !b.F.gameObject.activeInHierarchy || b.F.ActiveStateName != "Sitting in car") continue;
                string mine = cur.Value ?? "";
                if (mine == b.CarName.Value) continue;   // (le joueur local y est : l'automate ecoute a bon droit)
                if (Game.Saving) continue;
                try { Game.SetState(b.F, "Not sitting"); }
                catch (System.Exception e) { Log.Warn("phares : " + b.Car + " : " + e.Message); continue; }
                if (now >= b.NextLog) { b.NextLog = now + 60f; Log.Info("phares : " + b.Car + " ecoutait la touche sans le joueur dedans (joueur dans '" + mine + "') : remis au repos"); }
            }
        }
    }
}
