using UnityEngine;

namespace MWCoop
{
    // Acces aux objets du jeu.
    public static class Game
    {
        // Premier automate 'fsmName' porte par un objet actif nomme 'objectName'.
        public static PlayMakerFSM FindFsm(string objectName, string fsmName)
        {
            GameObject go = GameObject.Find(objectName);
            return go != null ? FsmOn(go, fsmName) : null;
        }

        // Objet par chemin "Racine/Enfant/...", actif ou non (GameObject.Find ne voit que les actifs).
        public static GameObject FindAny(string path)
        {
            string[] parts = path.Split('/');
            foreach (GameObject root in Recon.SceneRoots())
            {
                if (root.name != parts[0]) continue;
                Transform t = root.transform;
                for (int i = 1; i < parts.Length && t != null; i++) t = t.Find(parts[i]);
                if (t != null) return t.gameObject;
            }
            return null;
        }

        public static void SetGlobal(string name, string value)
        {
            var v = HutongGames.PlayMaker.FsmVariables.GlobalVariables.FindFsmString(name);
            if (v != null) v.Value = value; else Log.Warn("globale texte absente : " + name);
        }

        public static bool GlobalBool(string name)
        {
            var v = HutongGames.PlayMaker.FsmVariables.GlobalVariables.FindFsmBool(name);
            return v != null && v.Value;
        }

        public static void SetGlobalBool(string name, bool value)
        {
            var v = HutongGames.PlayMaker.FsmVariables.GlobalVariables.FindFsmBool(name);
            if (v != null) v.Value = value; else Log.Warn("globale booleenne absente : " + name);
        }

        static System.Reflection.MethodInfo switchState;

        // Force l'etat d'un automate (Fsm.SwitchState est prive dans cette version de PlayMaker).
        public static bool SetState(PlayMakerFSM f, string state)
        {
            if (f == null) return false;
            HutongGames.PlayMaker.FsmState s = f.Fsm.GetState(state);
            if (s == null) { Log.Warn("etat '" + state + "' absent de " + f.FsmName); return false; }
            if (switchState == null)
                switchState = typeof(HutongGames.PlayMaker.Fsm).GetMethod("SwitchState",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            switchState.Invoke(f.Fsm, new object[] { s });
            return true;
        }

        public static PlayMakerFSM FsmOn(GameObject go, string fsmName)
        {
            foreach (PlayMakerFSM f in go.GetComponents<PlayMakerFSM>())
                if (f.FsmName == fsmName) return f;
            return null;
        }
    }
}
