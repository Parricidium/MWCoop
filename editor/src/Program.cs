using System;
using System.Windows.Forms;

namespace EditeurCarte
{
    // Editeur de carte My Seasons Car (demande de JD, 19/10 : « un programme simple, une fenetre moderne », MSC ou MWC au
    // choix). V0 : choix du jeu (Steam, sinon le .exe), preparation de la carte (export par le mod, MapExport.cs), visite libre.
    // Arguments (le lanceur, page Outils, les donne tous) : --jeu msc|mwc (ouvre la carte de ce jeu si elle est prete),
    // --dossier <dossier du jeu> (celui que le lanceur connait, pour --jeu), --lanceur <MWCoop.exe> (celui qui fera
    // l'export), --langue fr|en|ru|ar, --mod <dossier MWCoop du mod> (ou deposer les cartes compilees) ; --debug : outils
    // de developpement de WebView2 ; --navigateur : l'interface dans le navigateur (d'office sous Wine, LocalServer).
    static class Program
    {
        public static bool Debug;
        public static string OpenGame, GameDirArg, LauncherArg, LangArg, ModArg;   // (ModArg : dossier MWCoop du mod pour ce jeu -- cartes compilees)
        public static string LaunchedFor;   // (jeu de --jeu, garde : OpenGame est consomme a la premiere page)

        [STAThread]
        static void Main(string[] args)
        {
            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i], v = i + 1 < args.Length ? args[i + 1] : null;
                if (a == "--debug") Debug = true;
                else if (a == "--jeu" && v != null) { OpenGame = LaunchedFor = v; i++; }
                else if (a == "--dossier" && v != null) { GameDirArg = v.TrimEnd('\\', '/'); i++; }
                else if (a == "--lanceur" && v != null) { LauncherArg = v; i++; }
                else if (a == "--langue" && v != null) { LangArg = v; i++; }
                else if (a == "--mod" && v != null) { ModArg = v.TrimEnd('\\', '/'); i++; }
                else if (a == "--navigateur") Browser = true;
            }
            // sous Wine (Linux, Proton) : pas de WebView2 -- l'interface dans le navigateur (MainForm, LocalServer) ; MWCOOP_WINE=<version> :
            // essais sous Windows, comme le lanceur
            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("MWCOOP_WINE")) || IsWine()) Browser = true;
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }

        public static bool Browser;   // (mode navigateur : --navigateur, Wine, ou WebView2 qui ne demarre pas)
        static bool IsWine()
        {
            try { IntPtr m = GetModuleHandle("ntdll.dll"); return m != IntPtr.Zero && GetProcAddress(m, "wine_get_version") != IntPtr.Zero; } catch { return false; }
        }
        [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)] static extern IntPtr GetModuleHandle(string name);
        [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Ansi)] static extern IntPtr GetProcAddress(IntPtr m, string name);
    }
}
