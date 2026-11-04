using System.IO;

namespace MWCoop
{
    // Assakyla, le petit village de JD (04/11), d'apres sa carte s21 town : routes, maisons du jeu vides, clotures, lampadaires, panneau a
    // l'embranchement, et les batiments du mod (boutique s21, musee, cinema, magasin CARDINAL) dedans. Regle (JD 04/11, « Oui, construis-la ») :
    //  - les ajouts de l'hote decident pour tous : aucun batiment coche, rien (zone du jeu d'origine) ; au moins un, tout le village, au
    //    chargement de la partie, chaque batiment seulement s'il est coche (sa place reste un terrain vide) ;
    //  - le village est dans la DLL (assets\ville\*.mwcmap, generes hors du depot) : rien a installer ni a recevoir ;
    //  - emplacements ci-dessous : valeurs par defaut quand le village est la, [Boutique] Paikka etc. restent des reglages d'essai ;
    //    planchers fixes, les memes chez tous. [Ville] Active=0 : anciens emplacements, sans village (essais).
    public static class Ville
    {
        public const string Shop = "-610.91,1715.49,186.2,5.30", Museo = "-682.36,1713.91,96.9,6.72", Kino = "-581.66,1675.38,5.8,4.35", Osat = "-534.4,1697.6,275.9,3.88";
        public const string Pad = "-682.36,1713.91,96.9,10.6,7.9", QuestCar = "-604.2,1711.2,152";

        public static bool On { get { return Kauppa.Active && Config.GetInt("Ville", "Active", 1) != 0; } }

        public static byte[] Pack()
        {
            using (Stream s = typeof(Ville).Assembly.GetManifestResourceStream(Game.Msc ? "MWCoop.ville.msc.mwcmap" : "MWCoop.ville.mwc.mwcmap"))
            {
                if (s == null) { Log.Warn("village : paquet absent de la DLL"); return null; }
                var b = new byte[s.Length]; int got = 0;
                while (got < b.Length) { int n = s.Read(b, got, b.Length - got); if (n <= 0) break; got += n; }
                return b;
            }
        }
    }
}
