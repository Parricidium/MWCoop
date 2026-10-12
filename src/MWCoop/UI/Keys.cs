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
        public static KeyCode Cheats { get { return Get("Triches", KeyCode.F7); } }
        public static KeyCode Map { get { return Get("Carte", KeyCode.M); } }
        public static KeyCode View { get { return Get("Vue", KeyCode.F5); } }

        // Camescope de la boutique (tenu en main) : viseur, enregistrer.
        public static KeyCode CameraViewfinder { get { return Get("CameraViseur", KeyCode.F); } }
        public static KeyCode CameraRec { get { return Get("CameraEnregistrer", KeyCode.R); } }

        // Camera drone (mode photo, demande de JD, 11/10) : categorie « Camera drone » des Touches du lanceur. Lettres par
        // defaut selon le clavier (AZERTY : ZQSD, A/E pour pencher ; sinon WASD, Q/E).
        public static KeyCode Drone { get { return Get("Drone", KeyCode.F8); } }
        public static KeyCode DroneForward { get { return Get("DroneAvant", Azerty ? KeyCode.Z : KeyCode.W); } }
        public static KeyCode DroneBack { get { return Get("DroneArriere", KeyCode.S); } }
        public static KeyCode DroneLeft { get { return Get("DroneGauche", Azerty ? KeyCode.Q : KeyCode.A); } }
        public static KeyCode DroneRight { get { return Get("DroneDroite", KeyCode.D); } }
        public static KeyCode DroneUp { get { return Get("DroneMonter", KeyCode.Space); } }
        public static KeyCode DroneDown { get { return Get("DroneDescendre", KeyCode.C); } }
        public static KeyCode DroneFast { get { return Get("DroneRapide", KeyCode.LeftShift); } }
        public static KeyCode DroneZoomIn { get { return Get("DroneZoomPlus", KeyCode.R); } }
        public static KeyCode DroneZoomOut { get { return Get("DroneZoomMoins", KeyCode.F); } }
        public static KeyCode DroneRollLeft { get { return Get("DronePencherG", Azerty ? KeyCode.A : KeyCode.Q); } }
        public static KeyCode DroneRollRight { get { return Get("DronePencherD", KeyCode.E); } }
        public static KeyCode DroneReset { get { return Get("DroneRecentrer", KeyCode.X); } }
        public static KeyCode DronePhoto { get { return Get("DronePhoto", KeyCode.Mouse0); } }
        public static KeyCode DroneGrid { get { return Get("DroneGrille", KeyCode.G); } }
        public static KeyCode DroneHud { get { return Get("DroneInterface", KeyCode.H); } }

        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern System.IntPtr GetKeyboardLayout(uint thread);
        static int azerty = -1;
        // Clavier AZERTY (francais de France, de Belgique, du Luxembourg, de Monaco ; pas le canadien ni le suisse).
        public static bool Azerty
        {
            get
            {
                if (azerty < 0)
                {
                    try { int id = (int)((long)GetKeyboardLayout(0) & 0xFFFF); azerty = id == 0x040C || id == 0x080C || id == 0x140C || id == 0x180C ? 1 : 0; }
                    catch { azerty = 0; }
                }
                return azerty == 1;
            }
        }

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
                case KeyCode.Mouse0: return Lang.T("Clic gauche", "Left click");
                case KeyCode.Mouse1: return Lang.T("Clic droit", "Right click");
                case KeyCode.Mouse2: return Lang.T("Clic molette", "Middle click");
                case KeyCode.Space: return Lang.T("Espace", "Space");
                case KeyCode.Mouse3: return Lang.T("Souris 4", "Mouse 4");
                case KeyCode.Mouse4: return Lang.T("Souris 5", "Mouse 5");
            }
            return n;
        }
    }
}
