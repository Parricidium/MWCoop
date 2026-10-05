using MWCoop.Net;
using UnityEngine;

namespace MWCoop
{
    // Le composant unique du mod : boucle d'image, interface.
    public class Core : MonoBehaviour
    {
        public static Core I;
        float nextBeat, dumpAt = -1;
        bool dumpEnabled;

        void Awake()
        {
            I = this;
            Config.Load();
            dumpEnabled = Config.GetInt("Test", "Vidage", 0) != 0 || Config.Get("Test", "VidageCibles", "").Length > 0;
            Log.Info("Core pret, profil '" + System.Environment.GetEnvironmentVariable("MWCOOP_PROFIL") + "'");
            Log.Info("sauvegardes : " + Application.persistentDataPath);
            SaveTransfer.CopyPlayerOptions();
            Session.Start();
        }

        void LateUpdate()
        {
            try { PlayerSync.LateUpdate(); CarVisuals.LateUpdate(); Machines.LateUpdate(); Npcs.LateUpdate(); VehicleSync.LateUpdate(); }
            catch (System.Exception e) { if (Time.frameCount % 600 == 0) Log.Warn("poses : " + e.Message); }
        }

        void Update()
        {
            if (Time.realtimeSinceStartup >= nextBeat)
            {
                nextBeat = Time.realtimeSinceStartup + 10f;
                Log.Info("images " + Time.frameCount + ", temps " + Time.time.ToString("F1")
                         + ", niveau " + Application.loadedLevelName + ", " + Session.Status
                         + (Session.T != null ? ", recu " + Session.T.BytesIn / 1024 + " Ko, envoye " + Session.T.BytesOut / 1024 + " Ko" : ""));
            }
            if (dumpAt > 0 && Time.realtimeSinceStartup >= dumpAt)
            {
                dumpAt = -1;
                try
                {
                    string targets = Config.Get("Test", "VidageCibles", "");
                    if (targets.Length > 0 && Application.loadedLevelName == "GAME") Log.Info("cibles : " + Recon.DumpTargets(targets) + ", pieces : " + Parts.DumpIds());
                    if (Config.GetInt("Test", "Vidage", 0) == 1) Recon.DumpLevel("auto", Application.loadedLevelName != "GAME");
                    Log.Info("globales : " + Recon.DumpGlobals());
                    Log.Info("personnages : " + Recon.DumpCharacters());
                    Log.Info("clips : " + Recon.DumpClips());
                }
                catch (System.Exception e) { Log.Error("vidage : " + e); }
            }
            Step("session", Session.Update);
            Step("sauvegarde en jeu", Game.Update);
            Step("menu", Menu.Update);
            Step("joueurs", PlayerSync.Update);
            Step("monde", World.Update);
            Step("interactions", Interactions.Update);
            Step("voitures", VehicleSync.Update);
            Step("pieces", Parts.Update);
            Step("objets", Props.Update);
            Step("argent", Wallet.Update);
            Step("magasin", Shop.Update);
            Step("peinture", Paint.Update);
            Step("reglages", Settings.Update);
            Step("liquides", Fluids.Update);
            Step("sauvegarde", SaveTransfer.Update);
            Step("consommables", Consume.Update);
            Step("voix", Voices.Update);
            Step("portieres", CarDoors.Update);
            Step("tableaux de bord", CarVisuals.Update);
            Step("passagers", Seats.Update);
            Step("reapparition", Respawn.Update);
            Step("audit", Audit.Update);
            Step("portes automatiques", NearDoors.Update);
            Step("machines", Machines.Update);
            Step("givre", Frost.Update);
            Step("remorquage", Tow.Update);
            Step("appels", Calls.Update);
            Step("vetements", Wear.Update);
            Step("rayons", Stock.Update);
            Step("PNJ", Npcs.Update);
            Step("voitures garees", Parked.Update);
            Step("monde (automates)", WorldFsms.Update);
            Step("trafic", Traffic.Update);
            Step("couleur", CarColor.Update);
            Step("quetes", Jobs.Update);
            Step("trace", Trace.Update);
            Step("deroule", Flow.Update);
            Step("ecran d'attente", WaitScreen.Update);
            Step("autotest", Autotest.Update);
        }

        // Chaque module, chronometre : un passage de plus de 30 ms (a-coup visible) est note, 1 fois / 10 s par module.
        static readonly System.Diagnostics.Stopwatch watch = new System.Diagnostics.Stopwatch();
        static readonly System.Collections.Generic.Dictionary<string, float> slowLogged = new System.Collections.Generic.Dictionary<string, float>();

        static void Step(string what, System.Action a)
        {
            watch.Reset(); watch.Start();
            try { a(); } catch (System.Exception e) { Log.Error(what + " : " + e); }
            watch.Stop();
            if (watch.ElapsedMilliseconds > 30)
            {
                float last;
                if (!slowLogged.TryGetValue(what, out last) || Time.realtimeSinceStartup - last > 10f)
                {
                    slowLogged[what] = Time.realtimeSinceStartup;
                    Log.Warn("lent : " + what + " " + watch.ElapsedMilliseconds + " ms");
                }
            }
        }

        void OnLevelWasLoaded(int level)
        {
            Log.Info("niveau charge : " + level + " " + Application.loadedLevelName);
            Replay.OnLevelLoaded();
            PlayerSync.OnLevelLoaded();
            World.OnLevelLoaded();
            Interactions.OnLevelLoaded();
            VehicleSync.OnLevelLoaded();
            Parts.OnLevelLoaded();
            Props.OnLevelLoaded();
            Wallet.OnLevelLoaded();
            Shop.OnLevelLoaded();
            Paint.OnLevelLoaded();
            Settings.OnLevelLoaded();
            Fluids.OnLevelLoaded();
            Consume.OnLevelLoaded();
            Voices.OnLevelLoaded();
            CarDoors.OnLevelLoaded();
            CarVisuals.OnLevelLoaded();
            Seats.OnLevelLoaded();
            Respawn.OnLevelLoaded();
            Audit.OnLevelLoaded();
            NearDoors.OnLevelLoaded();
            Machines.OnLevelLoaded();
            Frost.OnLevelLoaded();
            Tow.OnLevelLoaded();
            Calls.OnLevelLoaded();
            Wear.OnLevelLoaded();
            Stock.OnLevelLoaded();
            Npcs.OnLevelLoaded();
            Parked.OnLevelLoaded();
            WorldFsms.OnLevelLoaded();
            Traffic.OnLevelLoaded();
            CarColor.OnLevelLoaded();
            Jobs.OnLevelLoaded();
            WaitScreen.OnLevelLoaded();
            if (dumpEnabled) dumpAt = Time.realtimeSinceStartup + (Application.loadedLevelName == "GAME" ? Config.GetInt("Test", "VidageDelai", 25) : 5f);
        }

        void OnGUI()
        {
            try { Hud.Draw(); WaitScreen.Draw(); Menu.Draw(); Respawn.Draw(); } catch (System.Exception e) { Log.Error("hud : " + e); }
        }

        void OnApplicationQuit()
        {
            Session.Stop();
        }
    }
}
