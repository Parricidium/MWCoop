using System;
using System.Runtime.InteropServices;

namespace MWCoop
{
    // Fin de partie bloquee (Matilda Prime, 18/10) : chez certains joueurs le jeu ecrit « jeu ferme normalement » puis ne se
    // termine jamais (processus sans fenetre qui garde version.dll : le lanceur ne peut plus mettre a jour ni bien relancer).
    // A la sortie, une minuterie du systeme -- fil natif du pool de Windows, independant de Mono et d'Unity -- termine le
    // processus s'il vit encore quelques secondes plus tard. Rappel WAITORTIMERCALLBACK(param, fired) en x64 : RCX = param =
    // GetCurrentProcess() (-1), RDX = fired (1) : c'est exactement TerminateProcess(soi, 1).
    // [Coop] FinForcee=<s> (defaut 20 ; 0 : jamais). [Test] BloqueSortie=1 : la sortie se bloque (essai de la minuterie).
    public static class ExitGuard
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr GetModuleHandle(string name);
        [DllImport("kernel32.dll", CharSet = CharSet.Ansi)] static extern IntPtr GetProcAddress(IntPtr module, string name);
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool CreateTimerQueueTimer(out IntPtr timer, IntPtr queue, IntPtr callback, IntPtr param, uint due, uint period, uint flags);
        [DllImport("kernel32.dll")] static extern bool DeleteTimerQueueTimer(IntPtr queue, IntPtr timer, IntPtr completion);
        const uint WT_EXECUTEONLYONCE = 0x8;
        static bool armed;
        static IntPtr timer;
        static float armedAt;

        public static void Arm()
        {
            if (armed) return;
            armed = true;
            int s = Config.GetInt("Coop", "FinForcee", 20);
            if (s <= 0) { Log.Info("sortie : fin forcee coupee ([Coop] FinForcee=0)"); return; }
            if (IntPtr.Size != 8) { Log.Info("sortie : fin forcee seulement en 64 bits"); return; }
            try
            {
                IntPtr term = GetProcAddress(GetModuleHandle("kernel32.dll"), "TerminateProcess");
                armedAt = UnityEngine.Time.realtimeSinceStartup;
                bool ok = term != IntPtr.Zero && CreateTimerQueueTimer(out timer, IntPtr.Zero, term, new IntPtr(-1), (uint)(s * 1000), 0, WT_EXECUTEONLYONCE);
                Log.Info("sortie : le jeu sera termine de force dans " + s + " s s'il ne s'est pas ferme" + (ok ? "" : " -- minuterie IMPOSSIBLE (erreur " + Marshal.GetLastWin32Error() + ")"));
            }
            catch (Exception e) { Log.Warn("sortie : minuterie de fin forcee : " + e.Message); }
            if (Config.GetInt("Test", "BloqueSortie", 0) != 0) { Log.Info("autotest : sortie bloquee (BloqueSortie)"); System.Threading.Thread.Sleep(120000); }
        }

        // Le jeu tourne encore une seconde apres (fermeture annulee par un mod ?) : minuterie retiree, rien n'est force.
        public static void Update()
        {
            if (!armed || timer == IntPtr.Zero || UnityEngine.Time.realtimeSinceStartup - armedAt < 1f) return;
            DeleteTimerQueueTimer(IntPtr.Zero, timer, IntPtr.Zero);
            timer = IntPtr.Zero;
            armed = false;
            Log.Info("sortie : le jeu continue (fermeture annulee) : fin forcee retiree");
        }

        // Autotest "quitter" : le jeu se ferme tout seul a 25 s (comme Quitter du menu).
        static bool quitAsked;
        public static void Test(string mode, float t)
        {
            if (mode != "quitter" || quitAsked || t < 25f) return;
            quitAsked = true;
            Log.Info("autotest : quitter");
            UnityEngine.Application.Quit();
        }
    }
}
