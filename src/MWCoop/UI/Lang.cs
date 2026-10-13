using UnityEngine;

namespace MWCoop
{
    // Langue des textes du mod en jeu : celle du lanceur, qui l'ecrit dans [Coop] Langue=fr|en|ru a chaque lancement ;
    // sans elle, celle de Windows (francais, russe, sinon anglais). [Test] Langue la force pour les essais (captures).
    // Russe (13/10) : traduction de l'anglais (LangRu.cs, generee), l'anglais si un texte n'y est pas.
    public static class Lang
    {
        static int lang = -1;   // 0 anglais, 1 francais, 2 russe

        static int Code
        {
            get
            {
                if (lang < 0)
                {
                    string l = Config.Get("Test", "Langue", "");
                    if (l.Length == 0) l = Config.Get("Coop", "Langue", "");
                    l = l.ToLowerInvariant();
                    lang = l == "fr" ? 1 : l == "en" ? 0 : l == "ru" ? 2
                         : Application.systemLanguage == SystemLanguage.French ? 1 : Application.systemLanguage == SystemLanguage.Russian ? 2 : 0;
                    Log.Info("interface : textes en " + (lang == 1 ? "francais" : lang == 2 ? "russe (" + LangRu.Table.Count + " traductions)" : "anglais"));
                }
                return lang;
            }
        }

        public static bool Fr { get { return Code == 1; } }
        public static bool Ru { get { return Code == 2; } }
        public static bool En { get { return !Fr; } }

        public static string T(string f, string e) { return Code == 1 ? f : Code == 2 ? R(e) : e; }

        // Texte anglais -> russe en russe (l'anglais s'il n'est pas traduit) ; tel quel dans les autres langues.
        public static string R(string e)
        {
            string r;
            return Code == 2 && e != null && LangRu.Table.TryGetValue(e, out r) ? r : e;
        }

        // Textes ecrits par le jeu avec sa police (GUIinteraction, GUIsubtitle) : sans cyrillique, l'anglais en russe.
        public static string TG(string f, string e) { return Code == 1 ? f : e; }

        // Tableau de textes (onglets...) : le francais, l'anglais ou sa traduction (gardee).
        static readonly System.Collections.Generic.Dictionary<string[], string[]> ruArrays = new System.Collections.Generic.Dictionary<string[], string[]>();
        public static string[] A(string[] f, string[] e)
        {
            if (Code == 1) return f;
            if (Code != 2) return e;
            string[] r;
            if (!ruArrays.TryGetValue(e, out r)) { r = new string[e.Length]; for (int i = 0; i < e.Length; i++) r[i] = R(e[i]); ruArrays[e] = r; }
            return r;
        }

        // Etat de la session (Session.Status, ecrit en francais et lu par d'autres modules) pour l'affichage.
        public static string Status(string s)
        {
            if (Fr || string.IsNullOrEmpty(s)) return s;
            if (s == "solo") return R("solo");
            if (s.StartsWith("hote, port ")) return R("host, port ") + s.Substring(11);
            if (s == "hote Steam") return R("Steam host");
            if (s == "attente d'une invitation Steam") return R("waiting for a Steam invite");
            if (s.StartsWith("connexion a ")) return R("connecting to ") + s.Substring(12);
            if (s.StartsWith("connecte a ")) return R("connected to ") + s.Substring(11);
            if (s.StartsWith("deconnecte (")) return R("disconnected (") + s.Substring(12).Replace("nouvel essai", "retrying");
            if (s.StartsWith("refuse : ")) return R("refused: ") + s.Substring(9);
            if (s.StartsWith("sans reponse de ")) return R("no answer from ") + s.Substring(16);
            if (s.StartsWith("erreur : ")) return R("error: ") + s.Substring(9);
            return s;
        }

        public static string Players(int n) { return n + (Fr ? (n > 1 ? " joueurs" : " joueur") : Ru ? " " + RuPlural(n, "игрок", "игрока", "игроков") : (n > 1 ? " players" : " player")); }

        // Pluriel russe : 1 игрок, 2-4 игрока, 5+ игроков (11-14 : игроков).
        public static string RuPlural(int n, string one, string few, string many)
        {
            int a = System.Math.Abs(n) % 100, b = a % 10;
            return a >= 11 && a <= 14 ? many : b == 1 ? one : b >= 2 && b <= 4 ? few : many;
        }
    }
}
