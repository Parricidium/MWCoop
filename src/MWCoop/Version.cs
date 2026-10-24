namespace MWCoop
{
    public static class Version
    {
        // Version du mod, posee par dist\make-release.ps1 -Jeu mwc|msc (1.15.0 : un numero par flux ; ici, celui de la derniere
        // release, MWC ou MSC -- chaque paquet porte la DLL compilee avec son propre numero). Le lanceur a le sien : launcher\version.txt.
        public const string Text = "1.15.0";
    }
}
