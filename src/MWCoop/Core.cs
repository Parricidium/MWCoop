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
            dumpEnabled = Config.GetInt("Test", "Vidage", 0) != 0;
            Log.Info("Core pret, profil '" + System.Environment.GetEnvironmentVariable("MWCOOP_PROFIL") + "'");
            Log.Info("sauvegardes : " + Application.persistentDataPath);
            Session.Start();
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
                    Recon.DumpLevel("auto", Application.loadedLevelName != "GAME");
                    Log.Info("globales : " + Recon.DumpGlobals());
                    Log.Info("personnages : " + Recon.DumpCharacters());
                }
                catch (System.Exception e) { Log.Error("vidage : " + e); }
            }
            Step("session", Session.Update);
            Step("menu", Menu.Update);
            Step("joueurs", PlayerSync.Update);
            Step("monde", World.Update);
            Step("interactions", Interactions.Update);
            Step("deroule", Flow.Update);
            Step("autotest", Autotest.Update);
        }

        static void Step(string what, System.Action a)
        {
            try { a(); } catch (System.Exception e) { Log.Error(what + " : " + e); }
        }

        void OnLevelWasLoaded(int level)
        {
            Log.Info("niveau charge : " + level + " " + Application.loadedLevelName);
            PlayerSync.OnLevelLoaded();
            World.OnLevelLoaded();
            Interactions.OnLevelLoaded();
            if (dumpEnabled) dumpAt = Time.realtimeSinceStartup + (Application.loadedLevelName == "GAME" ? 25f : 5f);
        }

        void OnGUI()
        {
            try { Hud.Draw(); Menu.Draw(); } catch (System.Exception e) { Log.Error("hud : " + e); }
        }

        void OnApplicationQuit()
        {
            Session.Stop();
        }
    }
}
