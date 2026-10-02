using System;
using UnityEngine;

namespace MWCoop
{
    // Appele une seule fois par le chargeur (version.dll), sur le fil principal d'Unity,
    // au premier appel du code du jeu. Cree l'objet persistant qui porte tout le mod.
    public static class Entry
    {
        public static GameObject Root;

        public static void Init()
        {
            if (Root != null) return;
            try
            {
                Log.Open();
                Log.Info("MWCoop " + Version.Text + " - Unity " + Application.unityVersion
                         + ", niveau " + Application.loadedLevelName);
                Root = new GameObject("MWCoop");
                UnityEngine.Object.DontDestroyOnLoad(Root);
                Root.AddComponent<Core>();
            }
            catch (Exception e)
            {
                Log.Error("Init : " + e);
            }
        }
    }
}
