using HutongGames.PlayMaker;
using MWCoop.Net;
using UnityEngine;

namespace MWCoop
{
    // Porte automatique du garage de Fleetari (retour d'un joueur, 10/10 : « la porte du garage n'est pas synchronisee ») :
    // REPAIRSHOP :: OpeningHours -- garage ferme, « Wait distance 2 » mesure la distance du joueur LOCAL (GetDistance) et ferme
    // la porte (« Close door » : garage_door 1 remis sous Door1Closed) quand il s'eloigne. Chacun la fermait selon sa propre
    // distance : fermee chez l'hote parti au loin, ouverte chez l'ami reste devant. Ici, juste apres la mesure du jeu, la
    // distance devient celle du joueur le plus proche (joueur d'ici ou avatar d'un autre) : la porte ne se ferme que quand
    // tout le monde est loin, et pareil chez tous.
    public static class GarageDoor
    {
        static bool hooked;
        static float nextLook;

        class Nearest : ModHook
        {
            public override string Module { get { return "porte du garage"; } }
            public FsmFloat Var;
            public Transform From;
            public override void OnEnter() { Apply(); }
            public override void OnUpdate() { Apply(); }
            void Apply()
            {
                if (Var == null || From == null || !Session.Active) return;
                float best = Var.Value;
                foreach (Avatar a in PlayerSync.Avatars)
                    if (a != null && a.Root != null) best = Mathf.Min(best, Vector3.Distance(From.position, a.Root.transform.position));
                Var.Value = best;
            }
        }

        public static void OnLevelLoaded() { hooked = false; nextLook = 0f; }

        public static void Update()
        {
            if (hooked || !PlayerSync.InGame || Time.realtimeSinceStartup < nextLook) return;
            nextLook = Time.realtimeSinceStartup + 5f;
            GameObject shop = Game.FindAny("REPAIRSHOP");
            PlayMakerFSM f = shop != null ? Game.FsmOn(shop, "OpeningHours") : null;
            FsmState s = f != null ? f.Fsm.GetState("Wait distance 2") : null;
            if (s == null || !s.IsInitialized) return;
            hooked = true;
            var list = new System.Collections.Generic.List<FsmStateAction>(s.Actions);
            int at = -1;
            for (int i = 0; i < list.Count; i++) if (list[i] != null && list[i].GetType().Name == "GetDistance") { at = i; break; }
            FsmFloat dist = f.FsmVariables.FindFsmFloat("Distance");
            if (at < 0 || dist == null) { Log.Warn("porte du garage : etat Wait distance 2 sans GetDistance / Distance"); return; }
            // (depuis l'objet que mesure le jeu : le champ gameObject de GetDistance, sinon l'automate)
            Transform from = f.transform;
            System.Reflection.FieldInfo gf = list[at].GetType().GetField("gameObject");
            FsmOwnerDefault od = gf != null ? gf.GetValue(list[at]) as FsmOwnerDefault : null;
            GameObject go = od != null ? f.Fsm.GetOwnerDefaultTarget(od) : null;
            if (go != null) from = go.transform;
            list.Insert(at + 1, new Nearest { Var = dist, From = from });
            s.Actions = list.ToArray();
            Log.Info("porte du garage de Fleetari : fermee seulement quand tous les joueurs sont loin");
        }
    }
}
