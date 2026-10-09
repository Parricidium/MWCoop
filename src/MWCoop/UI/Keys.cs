using UnityEngine;

namespace MWCoop
{
    // Touches du mod reglables dans le lanceur (Reglages > Chez un ami : section [Touches] de mwcoop.ini, noms des KeyCode
    // de Unity : T, N, V, F5, Alpha1...). Lues a chaque appel (cache de Config) ; nom inconnu : touche par defaut.
    public static class Keys
    {
        public static KeyCode Chat { get { return Get("Tchat", KeyCode.T); } }
        public static KeyCode Wallet { get { return Get("PorteMonnaie", KeyCode.N); } }
        public static KeyCode Voice { get { return Get("Vocal", KeyCode.V); } }

        static KeyCode Get(string name, KeyCode def)
        {
            string s = Config.Get("Touches", name, "");
            if (s.Length == 0) return def;
            try { return (KeyCode)System.Enum.Parse(typeof(KeyCode), s, true); }
            catch { return def; }
        }

        // Nom affiche (Alpha1 -> 1, LeftShift -> Maj gauche...).
        public static string Label(KeyCode k)
        {
            string n = k.ToString();
            if (n.StartsWith("Alpha")) return n.Substring(5);
            if (n.StartsWith("Keypad")) return Lang.T("Pavé ", "Num ") + n.Substring(6);
            switch (k)
            {
                case KeyCode.LeftShift: return Lang.T("Maj gauche", "Left Shift");
                case KeyCode.RightShift: return Lang.T("Maj droite", "Right Shift");
                case KeyCode.LeftControl: return Lang.T("Ctrl gauche", "Left Ctrl");
                case KeyCode.RightControl: return Lang.T("Ctrl droite", "Right Ctrl");
                case KeyCode.LeftAlt: return "Alt";
                case KeyCode.CapsLock: return Lang.T("Verr. maj", "Caps Lock");
                case KeyCode.BackQuote: return "²";
                case KeyCode.Mouse3: return Lang.T("Souris 4", "Mouse 4");
                case KeyCode.Mouse4: return Lang.T("Souris 5", "Mouse 5");
            }
            return n;
        }
    }
}
