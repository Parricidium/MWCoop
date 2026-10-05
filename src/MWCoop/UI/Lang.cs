using UnityEngine;

namespace MWCoop
{
    // Langue des textes du mod en jeu : celle du lanceur, qui l'ecrit dans [Coop] Langue=fr|en a chaque lancement ;
    // sans elle, celle de Windows (francais, sinon anglais). [Test] Langue la force pour les essais (captures).
    public static class Lang
    {
        static int fr = -1;

        public static bool Fr
        {
            get
            {
                if (fr < 0)
                {
                    string l = Config.Get("Test", "Langue", "");
                    if (l.Length == 0) l = Config.Get("Coop", "Langue", "");
                    l = l.ToLowerInvariant();
                    fr = l == "fr" ? 1 : l == "en" ? 0 : Application.systemLanguage == SystemLanguage.French ? 1 : 0;
                    Log.Info("interface : textes en " + (fr == 1 ? "francais" : "anglais"));
                }
                return fr == 1;
            }
        }

        public static bool En { get { return !Fr; } }

        public static string T(string f, string e) { return Fr ? f : e; }

        // Etat de la session (Session.Status, ecrit en francais et lu par d'autres modules) pour l'affichage.
        public static string Status(string s)
        {
            if (Fr || string.IsNullOrEmpty(s)) return s;
            if (s == "solo") return "solo";
            if (s.StartsWith("hote, port ")) return "host, port " + s.Substring(11);
            if (s.StartsWith("connexion a ")) return "connecting to " + s.Substring(12);
            if (s.StartsWith("connecte a ")) return "connected to " + s.Substring(11);
            if (s.StartsWith("deconnecte (")) return "disconnected (" + s.Substring(12).Replace("nouvel essai", "retrying");
            if (s.StartsWith("refuse : ")) return "refused: " + s.Substring(9);
            if (s.StartsWith("erreur : ")) return "error: " + s.Substring(9);
            return s;
        }

        public static string Players(int n) { return n + (Fr ? (n > 1 ? " joueurs" : " joueur") : (n > 1 ? " players" : " player")); }
    }
}
