using System;
using System.Runtime.InteropServices;

namespace MWCoop
{
    // Linux / Steam Deck par Proton (Wine) : le jeu n'existe que pour Windows, MWCoop tourne dedans (question de JD, 18/10).
    // Wine se reconnait a ntdll!wine_get_version. Les appels natifs du mod (user32 de Gfx, minuterie de kernel32 d'ExitGuard,
    // clavier de Keys) existent sous Wine ; le ray tracing (MWCoopRT : Direct3D 12 et DXR a cote du Direct3D 11 du jeu, textures
    // partagees entre les deux -- DXVK et vkd3d-proton ne le font pas) y reste coupe : [Test] RTWine=1 pour l'essayer quand meme.
    // Essai sous Windows : variable MWCOOP_WINE=<version> (comme sous Wine).
    public static class Wine
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr GetModuleHandle(string name);
        [DllImport("kernel32.dll", CharSet = CharSet.Ansi)] static extern IntPtr GetProcAddress(IntPtr module, string name);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate IntPtr DStr();

        static bool probed;
        static string version;

        // Version de Wine, ou null sous Windows.
        public static string Version
        {
            get
            {
                if (probed) return version;
                probed = true;
                try
                {
                    IntPtr p = GetProcAddress(GetModuleHandle("ntdll.dll"), "wine_get_version");
                    if (p != IntPtr.Zero)
                    {
                        IntPtr s = ((DStr)Marshal.GetDelegateForFunctionPointer(p, typeof(DStr)))();
                        version = s != IntPtr.Zero ? Marshal.PtrToStringAnsi(s) : "?";
                    }
                }
                catch (Exception e) { Log.Warn("wine : detection impossible (" + e.Message + ")"); }
                if (version == null)
                {
                    string sim = Environment.GetEnvironmentVariable("MWCOOP_WINE");
                    if (!string.IsNullOrEmpty(sim)) version = sim + " (essai MWCOOP_WINE)";
                }
                return version;
            }
        }
        public static bool On { get { return Version != null; } }

        // Pour le journal du demarrage : "" sous Windows.
        public static string Describe()
        {
            if (!On) return "";
            string deck = Environment.GetEnvironmentVariable("SteamDeck") == "1" ? ", Steam Deck" : "";
            string proton = string.IsNullOrEmpty(Environment.GetEnvironmentVariable("STEAM_COMPAT_DATA_PATH")) ? "" : ", Proton";
            return "Wine " + Version + proton + deck;
        }
    }
}
