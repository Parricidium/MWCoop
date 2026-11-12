using System.IO;

namespace MWCoop
{
    // Images perso par jeu (retour d'un joueur, 12/11 : une chemise sur My Summer Car, un manteau sur My Winter Car ; le
    // manteau remplacait la chemise) : %LOCALAPPDATA%\MWCoop\visage-mwc.jpg, haut-msc.jpg, pantalon-mwc.jpg... Les
    // anciennes images communes (visage.jpg, haut.jpg, pantalon.jpg) sont recopiees une fois pour chaque jeu (marqueur
    // perso-<jeu>.migre) : rien de perdu. L'ancienne reste en place pour un mod plus ancien. Meme regle dans le lanceur
    // (launcher\perso.inc, PersoImgMigrate). Sans Unity : essayee a part (run\essai-persofiles.cs).
    public static class PersoFiles
    {
        public static string GameId(bool msc) { return msc ? "msc" : "mwc"; }

        // <racine>\<nom>-<jeu>.jpg
        public static string Image(string root, string name, string game) { return Path.Combine(root, name + "-" + game + ".jpg"); }
        // texture du jeu (<nom>-jeu-<jeu>.png) et gabarit (gabarit-<nom>-<jeu>.png), propres a chaque jeu
        public static string GameTexture(string root, string name, string game) { return Path.Combine(root, name + "-jeu-" + game + ".png"); }
        public static string Template(string root, string name, string game) { return Path.Combine(root, "gabarit-" + name + "-" + game + ".png"); }
        public static string TemplateTag(string root, string game) { return Path.Combine(root, "gabarits-" + game + ".version"); }

        // Une fois par jeu : chaque ancienne image commune devient celle de ce jeu, si ce jeu n'en a pas deja une.
        // Rend le nombre d'images recopiees (-1 : deja fait).
        public static int Migrate(string root, string game, string[] names)
        {
            string mark = Path.Combine(root, "perso-" + game + ".migre");
            if (File.Exists(mark)) return -1;
            int n = 0;
            bool fail = false;
            foreach (string name in names)
            {
                string old = Path.Combine(root, name + ".jpg"), now = Image(root, name, game);
                try { if (File.Exists(old) && !File.Exists(now)) { File.Copy(old, now, false); n++; } }
                catch { if (!File.Exists(now)) fail = true; }   // (fichier pris : on reessaiera au prochain passage)
            }
            if (!fail) { try { Directory.CreateDirectory(root); File.WriteAllText(mark, "1"); } catch { } }
            return n;
        }
    }
}
