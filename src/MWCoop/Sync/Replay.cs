using System.Collections.Generic;
using UnityEngine;

namespace MWCoop
{
    // Commun a tous les modules qui accrochent des automates du jeu.
    //  - Depth : un rejeu venu d'un autre joueur est en cours. Ce qu'il provoque ici (la lampe que
    //    l'interrupteur rejoue allume, la paie d'un boulot rejoue...) n'est renvoye par aucun module :
    //    l'autre l'a deja chez lui.
    //  - Claim : un automate n'est accroche que par un seul module (sinon chaque action partirait deux
    //    fois, et le rejeu de l'un serait renvoye par l'autre).
    //  - HookError : une exception dans un crochet est notee, jamais propagee (PlayMaker ne la rattrape
    //    pas : les vraies actions de l'etat ne tourneraient pas et l'automate du jeu resterait bloque).
    public static class Replay
    {
        public static int Depth;
        static readonly Dictionary<PlayMakerFSM, string> owners = new Dictionary<PlayMakerFSM, string>();
        static float nextErrorLog;

        public static void OnLevelLoaded() { owners.Clear(); Depth = 0; }

        // Vrai si 'module' peut accrocher 'f' (libre, ou deja a lui).
        public static bool Claim(PlayMakerFSM f, string module)
        {
            string o;
            if (owners.TryGetValue(f, out o)) return o == module;
            owners[f] = module;
            return true;
        }

        public static bool ClaimedByOther(PlayMakerFSM f, string module)
        {
            string o;
            return owners.TryGetValue(f, out o) && o != module;
        }

        public static void HookError(System.Exception e)
        {
            if (Time.realtimeSinceStartup < nextErrorLog) return;
            nextErrorLog = Time.realtimeSinceStartup + 10f;
            Log.Error("crochet : " + e);
        }
    }
}
