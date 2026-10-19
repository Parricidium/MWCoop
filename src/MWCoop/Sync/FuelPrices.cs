using HutongGames.PlayMaker;
using MWCoop.Net;
using UnityEngine;

namespace MWCoop
{
    // Prix du carburant (suggestion de Crowbangus, 12/10 : « le fioul coute 4,43 chez l'un, 3,69 chez l'autre »).
    // PERAPORTTI/FuelPylon::Fuelprices tire les prix au hasard chez chacun a chaque nouveau jour (New prices : essence,
    // diesel, fioul ; RANDOMIZE), les ecrit en texte (S1-S3) pour les ecrans du pylone (Monitor1-3 'SetPrice', NEWPRICE) et
    // dans les pompes (Pump1/Pump2 'Functions' : PriceGasoline, PriceDiesel, PriceFuelOil, ce que la pompe fait payer).
    // L'hote envoie les siens (@carbu) quand ils changent et toutes les 15 s (arrivants) ; l'invite les pose partout.
    public static class FuelPrices
    {
        // My Summer Car (vidage v.250908-04) : STORE::Fuelprices (Price98, PriceDiesel, PriceFuelOil) ; « Set table » ecrit S1-S3
        // dans les afficheurs STORE/LOD/Pumps/Prices/98, Diesel, FuelOil et FuelPrice dans les pompes (Trigger98, TriggerDiesel,
        // TriggerFuelOil :: Use). Pas rejoue : il continue vers « Check distance », qui reapprovisionne le magasin (INVENTORY).
        static readonly string[] VarsMwc = { "PriceGasoline", "PriceDiesel", "PriceFuelOil" }, VarsMsc = { "Price98", "PriceDiesel", "PriceFuelOil" };
        static string[] Vars { get { return Game.Msc ? VarsMsc : VarsMwc; } }
        static readonly string[] MscPumps = { "Trigger98", "TriggerDiesel", "TriggerFuelOil" }, MscScreens = { "98", "Diesel", "FuelOil" };
        static PlayMakerFSM pylon;
        static readonly float[] sent = new float[3];
        static float next, resend;
        static int logs;

        public static void OnLevelLoaded() { pylon = null; next = 0f; resend = 0f; for (int i = 0; i < 3; i++) sent[i] = -1f; }

        static PlayMakerFSM Pylon()
        {
            if (pylon == null) { GameObject g = Game.FindAny(Game.Msc ? "STORE" : "PERAPORTTI/FuelPylon"); if (g != null) pylon = Game.FsmOn(g, "Fuelprices"); }
            return pylon;
        }

        public static void Update()
        {
            if (!Session.Active || !Session.IsHost || !PlayerSync.InGame) return;
            float now = Time.realtimeSinceStartup;
            if (now < next) return;
            next = now + 2f;
            PlayMakerFSM f = Pylon();
            if (f == null) return;
            var w = new float[3];
            bool changed = false;
            for (int i = 0; i < 3; i++)
            {
                FsmFloat v = f.FsmVariables.FindFsmFloat(Vars[i]);
                if (v == null) return;
                w[i] = v.Value;
                changed |= Mathf.Abs(w[i] - sent[i]) > 0.0005f;
            }
            if (!changed && now < resend) return;
            resend = now + 15f;
            for (int i = 0; i < 3; i++) sent[i] = w[i];
            Session.SendAll(new NetWriter(Msg.Job).U8(Session.LocalId).Str("@carbu").F32(w[0]).F32(w[1]).F32(w[2]), true);
            if (changed && logs++ < 10) Log.Info("carburant : prix envoyes (essence " + w[0].ToString("F2") + ", diesel " + w[1].ToString("F2") + ", fioul " + w[2].ToString("F2") + ")");
        }

        public static void OnMessage(int who, NetReader r)
        {
            var w = new float[] { r.F32(), r.F32(), r.F32() };
            if (Session.IsHost || who != 0) return;   // (les prix de l'hote seulement)
            PlayMakerFSM f = Pylon();
            if (f == null) return;
            bool changed = false;
            for (int i = 0; i < 3; i++)
            {
                FsmFloat v = f.FsmVariables.FindFsmFloat(Vars[i]);
                if (v == null) continue;
                changed |= Mathf.Abs(v.Value - w[i]) > 0.0005f;
                v.Value = w[i];
                FsmString s = f.FsmVariables.FindFsmString("S" + (i + 1));
                if (s != null) s.Value = w[i].ToString("0.00");   // (comme ConvertFloatToString du jeu)
            }
            if (!changed) return;
            if (Game.Msc)
            {
                for (int i = 0; i < 3; i++)
                {
                    FsmGameObject pg = f.FsmVariables.FindFsmGameObject(MscPumps[i]);
                    PlayMakerFSM pf = pg != null && pg.Value != null ? Game.FsmOn(pg.Value, "Use") : null;
                    FsmFloat pv = pf != null ? pf.FsmVariables.FindFsmFloat("FuelPrice") : null;
                    if (pv != null) pv.Value = w[i];
                    GameObject sg = Game.FindAny("STORE/LOD/Pumps/Prices/" + MscScreens[i]);
                    TextMesh tm = sg != null ? sg.GetComponent<TextMesh>() : null;
                    if (tm != null) tm.text = w[i].ToString("0.00");
                }
                if (logs++ < 10) Log.Info("carburant : prix de l'hote poses (98 " + w[0].ToString("F2") + ", diesel " + w[1].ToString("F2") + ", fioul " + w[2].ToString("F2") + ")");
                return;
            }
            // Pompes (ce qu'elles font payer) et ecrans du pylone.
            foreach (string pump in new[] { "Pump1", "Pump2", "Pump3", "Pump4", "Pump5", "Pump6" })
            {
                FsmGameObject pg = f.FsmVariables.FindFsmGameObject(pump);
                PlayMakerFSM pf = pg != null && pg.Value != null ? Game.FsmOn(pg.Value, "Functions") : null;
                if (pf == null) continue;
                for (int i = 0; i < 3; i++) { FsmFloat pv = pf.FsmVariables.FindFsmFloat(Vars[i]); if (pv != null) pv.Value = w[i]; }
            }
            for (int m = 1; m <= 3; m++)
            {
                FsmGameObject mg = f.FsmVariables.FindFsmGameObject("PriceMonitor" + m);
                PlayMakerFSM mf = mg != null && mg.Value != null ? Game.FsmOn(mg.Value, "SetPrice") : null;
                if (mf != null && mf.enabled && mf.gameObject.activeInHierarchy) Game.SetState(mf, "State 1");   // (relit S1-S3, comme NEWPRICE)
            }
            if (logs++ < 10) Log.Info("carburant : prix de l'hote poses (essence " + w[0].ToString("F2") + ", diesel " + w[1].ToString("F2") + ", fioul " + w[2].ToString("F2") + ")");
        }

        // [Test] Autotest=carbu (2 joueurs) : a 30 s l'hote pose ses prix a 5,55 / 4,44 / 3,33 ; a 40 s chacun note ceux du
        // pylone, de ses ecrans et de la pompe 1.
        static int tStep;
        public static void Test(string mode, float t)
        {
            if (mode != "carbu") return;
            PlayMakerFSM f = Pylon();
            if (f == null) return;
            if (Session.IsHost && tStep == 0 && t > 30f)
            {
                tStep = 1;
                float[] w = { 5.55f, 4.44f, 3.33f };
                for (int i = 0; i < 3; i++) f.FsmVariables.FindFsmFloat(Vars[i]).Value = w[i];
                Log.Info("autotest : carbu, prix de l'hote poses a 5,55 / 4,44 / 3,33");
            }
            if (tStep < 2 && t > 40f)
            {
                tStep = 2;
                var sb = new System.Text.StringBuilder("autotest : carbu, pylone");
                for (int i = 0; i < 3; i++) sb.Append(' ').Append(f.FsmVariables.FindFsmFloat(Vars[i]).Value.ToString("F2")).Append(" (\"").Append(f.FsmVariables.FindFsmString("S" + (i + 1)).Value).Append("\")");
                FsmGameObject pg = f.FsmVariables.FindFsmGameObject("Pump1");
                PlayMakerFSM pf = pg != null && pg.Value != null ? Game.FsmOn(pg.Value, "Functions") : null;
                if (pf != null) { sb.Append(", pompe 1"); for (int i = 0; i < 3; i++) { FsmFloat pv = pf.FsmVariables.FindFsmFloat(Vars[i]); sb.Append(' ').Append(pv != null ? pv.Value.ToString("F2") : "?"); } }
                FsmGameObject mg = f.FsmVariables.FindFsmGameObject("PriceMonitor1");
                TextMesh tm = mg != null && mg.Value != null ? mg.Value.GetComponentInChildren<TextMesh>() : null;
                if (tm != null) sb.Append(", ecran 1 \"").Append(tm.text).Append('"');
                Log.Info(sb.ToString());
            }
        }
    }
}
