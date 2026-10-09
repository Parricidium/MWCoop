using System.Collections.Generic;
using HutongGames.PlayMaker;
using MWCoop.Net;
using UnityEngine;

namespace MWCoop
{
    // Pare-brise (retour d'un joueur, 09/10 : « le conducteur est mort, son pare-brise a eclate, celui du passager est
    // reste intact ») : chaque pare-brise a un automate 'Data' (variable BrokenWindshield) qui casse la vitre quand sa
    // liaison (FixedJoint, BreakForce) lache sous un choc, ou sur l'evenement global DEATH (mort dans l'accident) -- chez le
    // conducteur seulement : chez les autres, la voiture est une copie (pas de choc) et personne n'y meurt. Ici l'etat suit :
    // l'automate qui entre dans "Break glass" (cassee) ou "Reset" (vitre remplacee) chez un joueur est mis au meme etat chez
    // les autres (@vitre). Le chargement de la partie (les deux cotes lisent la meme sauvegarde) n'envoie rien.
    public static class Windshields
    {
        class Glass { public string Key; public PlayMakerFSM Fsm; public string Last; }
        static readonly List<Glass> glasses = new List<Glass>();
        static float scanAt = -1, nextLook;
        static string applying;

        public static void OnLevelLoaded()
        {
            glasses.Clear();
            scanAt = PlayerSync.InGame ? Time.realtimeSinceStartup + 12f : -1;
        }

        static void Scan()
        {
            glasses.Clear();
            foreach (Object o in Game.AllFsms())
            {
                PlayMakerFSM f = o as PlayMakerFSM;
                if (f == null || f.FsmName != "Data" || f.FsmVariables.FindFsmGameObject("BrokenWindshield") == null) continue;
                glasses.Add(new Glass { Key = Recon.Path(f.transform), Fsm = f, Last = f.ActiveStateName });
            }
            Log.Info("pare-brise : " + glasses.Count + " suivis");
        }

        public static void Update()
        {
            float now = Time.realtimeSinceStartup;
            if (scanAt > 0 && now >= scanAt) { scanAt = -1; Scan(); }
            if (now < nextLook || glasses.Count == 0) return;
            nextLook = now + 0.2f;
            foreach (Glass g in glasses)
            {
                if (g.Fsm == null) continue;
                string st = g.Fsm.ActiveStateName;
                if (st == g.Last) continue;
                g.Last = st;
                int what = st == "Break glass" ? 1 : st == "Reset" ? 2 : 0;
                if (what == 0) continue;
                if (applying == g.Key) { applying = null; continue; }   // (etat recu : pas renvoye)
                Log.Info("pare-brise : " + g.Key + (what == 1 ? " casse" : " remplace") + " ici");
                if (Session.Active && Session.RemoteCount > 0)
                    Session.SendAll(new NetWriter(Msg.Job).U8(Session.LocalId).Str("@vitre").Str(g.Key).U8(what), true);
            }
        }

        public static void OnRemote(int who, string key, int what)
        {
            foreach (Glass g in glasses)
            {
                if (g.Key != key || g.Fsm == null) continue;
                string st = g.Fsm.ActiveStateName;
                if ((what == 1 && st == "Break glass") || (what == 2 && (st == "Assemble" || st == "Reset"))) return;   // (deja)
                applying = key;
                if (!g.Fsm.enabled) g.Fsm.enabled = true;
                Game.SetState(g.Fsm, what == 1 ? "Break glass" : "Reset");
                Log.Info("pare-brise : " + key + (what == 1 ? " casse" : " remplace") + " par #" + who);
                return;
            }
        }

        // Essais : casse le pare-brise dont le chemin contient 'car' (comme le choc).
        public static string TestBreak(string car)
        {
            foreach (Glass g in glasses)
                if (g.Key.Contains(car) && g.Fsm != null) { Game.SetState(g.Fsm, "Break glass"); return "pare-brise " + g.Key + " casse"; }
            return "pas de pare-brise pour " + car + " (" + glasses.Count + " suivis)";
        }
        public static string State(string car)
        {
            foreach (Glass g in glasses) if (g.Key.Contains(car) && g.Fsm != null) return "pare-brise " + g.Key + " [" + g.Fsm.ActiveStateName + "]";
            return "pas de pare-brise pour " + car;
        }
    }
}
