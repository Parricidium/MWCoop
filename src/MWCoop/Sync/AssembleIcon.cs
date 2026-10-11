using HutongGames.PlayMaker;
using UnityEngine;

namespace MWCoop
{
    // Icone de montage (la coche, globale GUIassemble) : retour de gamereggo (11/10) -- quand un autre joueur approche une
    // piece de son point sur la Rivett, la coche s'affichait chez tous. Les points de montage (Installer de la CORRIS : distance
    // entre la piece et le point), le cablage (distance a "wiring mess"), la prise du chauffage ou la buche de la fendeuse
    // regardent ou est l'objet, pas qui le tient : la copie de la piece tenue par l'autre suffit a les allumer ici. Le jeu
    // n'allume cette icone que pour un objet en main ; si le joueur d'ici ne tient rien, elle est eteinte apres les automates
    // (LateUpdate, avant l'image) : globale, automate GUI/Icons 'Logic' remis a 'Test' (texture vide), texte d'interaction vide.
    public static class AssembleIcon
    {
        static FsmBool asm;
        static FsmString inter;
        static PlayMakerFSM icons;
        static TextMesh[] texts;
        static float nextFind, loggedAt = -100f;
        static int resets, testStep;

        public static void OnLevelLoaded() { asm = null; inter = null; icons = null; texts = null; nextFind = 0; testStep = 0; }

        // Essai 'coche' : de 30 a 34 s, la coche allumee a chaque image comme par un point de montage, sans rien en main ;
        // a 32 s, texture de l'icone (vide attendu) et etat de GUI/Icons.
        public static void Test(string mode, float t)
        {
            if (mode != "coche" || t < 30f || t > 36f) return;
            FsmBool b = FsmVariables.GlobalVariables.FindFsmBool("GUIassemble");
            if (t < 34f && b != null) b.Value = true;
            if (t > 32f && testStep == 0)
            {
                testStep = 1;
                GameObject tx = Game.FindAny("GUI/Icons/GUITexture");
                GUITexture g = tx != null ? tx.GetComponent<GUITexture>() : null;
                PlayMakerFSM ic = Game.FindAny("GUI/Icons") != null ? Game.FsmOn(Game.FindAny("GUI/Icons"), "Logic") : null;
                Log.Info("autotest : coche, texture " + (g != null && g.texture != null ? g.texture.name : "?") + ", icones " + (ic != null ? ic.ActiveStateName : "?")
                         + ", en main " + (Props.LocalHeld() != null ? Props.LocalHeld().name : "rien") + ", eteinte " + resets + " fois");
            }
        }

        public static void LateUpdate()
        {
            if (!Net.Session.Active || !PlayerSync.InGame) return;
            if (asm == null)
            {
                if (Time.realtimeSinceStartup < nextFind) return;
                nextFind = Time.realtimeSinceStartup + 5f;
                asm = FsmVariables.GlobalVariables.FindFsmBool("GUIassemble");
                inter = FsmVariables.GlobalVariables.FindFsmString("GUIinteraction");
                if (asm == null) return;
            }
            if (!asm.Value || !Props.HandFound || Props.LocalHeld() != null) return;
            if (Config.GetInt("Test", "CocheSansGarde", 0) != 0) return;   // (essai temoin)
            asm.Value = false;
            if (icons == null) { GameObject ic = Game.FindAny("GUI/Icons"); icons = ic != null ? Game.FsmOn(ic, "Logic") : null; }
            if (icons != null && icons.ActiveStateName == "Assemble") Game.SetState(icons, "Test");
            if (inter != null && inter.Value.Length > 0)
            {
                inter.Value = "";
                if (texts == null)
                {
                    GameObject it = Game.FindAny("GUI/Indicators/Interaction");
                    texts = it != null ? it.GetComponentsInChildren<TextMesh>(true) : new TextMesh[0];
                }
                foreach (TextMesh t in texts) if (t != null) t.text = "";
            }
            resets++;
            if (Time.realtimeSinceStartup - loggedAt > 30f) { loggedAt = Time.realtimeSinceStartup; Log.Info("icone de montage eteinte ici : rien en main (piece d'un autre joueur pres de son point)"); }
        }
    }
}
