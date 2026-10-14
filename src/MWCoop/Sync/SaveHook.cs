using System.Collections.Generic;
using HutongGames.PlayMaker;
using UnityEngine;

namespace MWCoop
{
    // Sauvegarde du jeu vue par MWCoop (demande de JD, 14/10) : les objets des ajouts (sacs de couchage, objets du coin video,
    // sacs de courses) ne s'ecrivent plus qu'ici, comme les objets du jeu de base -- quitter sans sauvegarder les remet a
    // leur place de la derniere sauvegarde. Avant, ils s'ecrivaient a chaque changement et toutes les 60 s.
    // Crochet au debut de l'etat "Save game" de Systems/Setup Game :: WaitPlayer, ou mene SAVEGAME (toilettes du jeu, sauvegarde
    // en jeu de MWCoop, sauvegarde envoyee aux invites...). Un ajout coupe ou pas encore charge n'ecrit pas : son fichier reste
    // tel quel et ses objets reviennent quand on le rallume.
    public static class SaveHook
    {
        static bool injected;
        static float nextTry;

        public static void OnLevelLoaded() { injected = false; nextTry = 0f; }

        public static void Update()
        {
            if (injected || Game.LevelName != "GAME" || Time.realtimeSinceStartup < nextTry) return;
            nextTry = Time.realtimeSinceStartup + 2f;
            foreach (PlayMakerFSM f in Object.FindObjectsOfType<PlayMakerFSM>())
            {
                if (f.FsmName != "WaitPlayer" || f.gameObject.name != "Setup Game") continue;
                if (f.Fsm == null || !f.Fsm.Initialized) return;
                FsmState s = f.Fsm.GetState("Save game");
                if (s == null) { injected = true; Log.Warn("sauvegarde du jeu : etat \"Save game\" introuvable, objets des ajouts ecrits a la sortie seulement"); return; }
                var list = new List<FsmStateAction>(s.Actions);
                list.Insert(0, new Hook());
                s.Actions = list.ToArray();
                injected = true;
                Log.Info("sauvegarde du jeu : suivie (objets des ajouts ecrits a chaque sauvegarde)");
                return;
            }
        }

        class Hook : ModHook
        {
            public override string Module { get { return "sauvegarde"; } }
            public override void OnEnter()
            {
                try
                {
                    MWCoop.Log.Info("sauvegarde du jeu : objets des ajouts ecrits");
                    SleepingBags.OnGameSave();
                    Kauppa.OnGameSave();
                    Wear.OnGameSave();   // (avant : toutes les 60 s ; revue du 14/10)
                }
                catch (System.Exception e) { Replay.HookError(e); }
                Finish();
            }
        }
    }
}
