using HutongGames.PlayMaker;
using MWCoop.Net;
using UnityEngine;

namespace MWCoop
{
    // Faisceau de cablage tenu par un autre joueur (Markos, MSC 24/10 : « des inscriptions avec une coche apparaissent comme si je
    // faisais le cablage », video : « ✓ FUSEBOX » au milieu de l'ecran de l'hote pendant que son ami cablait la Satsuma).
    // Chaque point de cablage (SATSUMA/Wiring/Triggers/*/* :: Assemble) mesure la distance a la globale WiringTool -- le faisceau
    // (ITEMS/wiring mess, automate 'Use' qui la pose sur lui-meme) -- et, a moins de 10 cm, affiche la coche et le nom
    // (GUIassemble, GUIinteraction : globales, donc a l'ecran du joueur d'ici) et attend le bouton Use. La copie du faisceau que
    // l'autre deplace passait devant ces points ici aussi : la coche s'affichait chez nous, et Use l'aurait branche ici.
    // Tant qu'un autre joueur deplace le faisceau, WiringTool pointe ici sur un objet tres loin (aucun point ne s'en approche) ;
    // rendue au faisceau des qu'il n'est plus a lui.
    public static class WiringGuard
    {
        static FsmGameObject tool;
        static GameObject item, away;
        static bool parked;
        static float next;
        static int logs;

        public static void OnLevelLoaded() { tool = null; item = null; parked = false; next = 0f; }

        public static void Update()
        {
            if (!Session.Active || !PlayerSync.InGame || Time.realtimeSinceStartup < next) return;
            next = Time.realtimeSinceStartup + 0.2f;
            if (tool == null) { tool = FsmVariables.GlobalVariables.FindFsmGameObject("WiringTool"); if (tool == null) { next += 10f; return; } }
            if (!parked && tool.Value != null && tool.Value != away) item = tool.Value;
            if (item == null) return;
            Rigidbody rb = item.GetComponent<Rigidbody>();
            bool remote = rb != null && Props.RemoteMover(rb) >= 0;
            if (remote && !parked)
            {
                if (away == null)
                {
                    away = new GameObject("MWCoop-FaisceauLoin");
                    away.transform.position = new Vector3(0f, -10000f, 0f);
                    Object.DontDestroyOnLoad(away);
                }
                tool.Value = away; parked = true;
                if (logs++ < 10) Log.Info("cablage : faisceau deplace par #" + Props.RemoteMover(rb) + " : ses points de cablage ne le voient plus ici (pas de coche a notre ecran)");
            }
            else if (!remote && parked)
            {
                tool.Value = item; parked = false;
                if (logs++ < 10) Log.Info("cablage : faisceau rendu (" + item.name + ")");
            }
        }
    }
}
