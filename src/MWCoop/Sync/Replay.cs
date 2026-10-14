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
    // Base de toute action ajoutee par un module dans un automate du jeu : l'audit lit ainsi qui a accroche quoi
    // (et voit les automates accroches par deux modules).
    public abstract class ModHook : HutongGames.PlayMaker.FsmStateAction
    {
        public abstract string Module { get; }
    }

    // Accroches : une action de plus dans un etat d'automate, au debut, a la fin ou a un rang (avant : la meme copie de liste
    // dans 37 endroits ; refonte du 14/10).
    public static class FsmHooks
    {
        public static void Prepend(HutongGames.PlayMaker.FsmState s, HutongGames.PlayMaker.FsmStateAction a) { Insert(s, 0, a); }
        public static void Append(HutongGames.PlayMaker.FsmState s, HutongGames.PlayMaker.FsmStateAction a) { Insert(s, -1, a); }
        // at < 0 ou au-dela : a la fin
        public static void Insert(HutongGames.PlayMaker.FsmState s, int at, HutongGames.PlayMaker.FsmStateAction a)
        {
            var old = s.Actions ?? new HutongGames.PlayMaker.FsmStateAction[0];
            if (at < 0 || at > old.Length) at = old.Length;
            var n = new HutongGames.PlayMaker.FsmStateAction[old.Length + 1];
            System.Array.Copy(old, 0, n, 0, at);
            n[at] = a;
            System.Array.Copy(old, at, n, at + 1, old.Length - at);
            s.Actions = n;
        }
    }

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

        // Module qui a accroche 'f' (null : personne).
        public static string Owner(PlayMakerFSM f)
        {
            string o;
            return owners.TryGetValue(f, out o) ? o : null;
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
