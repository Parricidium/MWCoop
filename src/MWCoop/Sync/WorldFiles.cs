using System.Collections.Generic;
using System.IO;
using HutongGames.PlayMaker;

namespace MWCoop
{
    // Fichiers du mod lies a UN monde (a cote de la sauvegarde) : objets de la boutique (MWCoop-esineet), sacs de courses
    // (MWCoop-pussit), sacs de couchage (MWCoop-sacs), vestes / casques / tasses en plus (MWCoop-vetements). Le jeu, a une
    // nouvelle partie, efface sa sauvegarde et tire un nouveau PlayerID ; ces fichiers restaient : les objets achetes
    // revenaient dans la nouvelle partie, et chez les invites en double, a leur place d'avant (retour de JD, 13/10).
    // Ici : MWCoop-monde.txt garde le PlayerID du monde de ces fichiers ; chaque module, avant de lire le sien, appelle
    // Check : autre monde -> ces fichiers sont effaces (comme un premier lancement). Pas encore de MWCoop-monde.txt (mise a
    // jour) : les fichiers sont gardes, rattaches au monde charge. Le porte-monnaie, la police et le rallye gardent deja
    // une ligne par monde (PlayerID).
    public static class WorldFiles
    {
        static readonly string[] Files = { "MWCoop-esineet.txt", "MWCoop-pussit.txt", "MWCoop-sacs.txt", "MWCoop-vetements.txt" };
        static readonly HashSet<string> done = new HashSet<string>();

        public static void OnLevelLoaded() { done.Clear(); if (!Game.Msc && Game.LevelName == "GAME") LoadPlayerId(); }

        // Le PlayerID global n'est lu de la sauvegarde (PlayerLeaderboardID2) que par RACES/RALLY/ResultsWeekend :: Data
        // (State 2 -> Exists -> Load 2). RACES :: SwapDates eteint RACES/RALLY les semaines de la course sur glace : partie
        // chargee une de ces semaines, PlayerID restait 0 toute la session -- porte-monnaie et etat de l'invite ni retrouves
        // ni gardes, fichiers du mod par monde sans monde (Донской, hote Dobryak, 30/10). Ici : lu nous-memes dans
        // savefile.txt au chargement (la valeur que Load 2 y lirait ; chez l'invite, la sauvegarde recue de l'hote).
        // [Test] LirePlayerID=0 : ancien comportement.
        static void LoadPlayerId()
        {
            if (Config.GetInt("Test", "LirePlayerID", 1) == 0) return;
            FsmFloat pid = FsmVariables.GlobalVariables.FindFsmFloat("PlayerID");
            if (pid == null) return;
            float id = SavedFloat(Path.Combine(UnityEngine.Application.persistentDataPath, Game.SaveFile), "PlayerLeaderboardID2");
            if (id == 0f || float.IsNaN(id) || id == pid.Value) return;
            Log.Info("monde : PlayerID " + pid.Value.ToString("F0") + " -> " + id.ToString("F0") + " (lu dans " + Game.SaveFile + ")");
            pid.Value = id;
        }

        // Flottant d'un fichier Easy Save 2 : suite de '~' <longueur> <etiquette> <taille uint32> <donnees> ; un flottant :
        // 0xFF, 4 octets de type, la valeur, '{'. 0 si absent ou illisible.
        static float SavedFloat(string file, string tag)
        {
            try
            {
                if (!File.Exists(file)) return 0f;
                byte[] b = File.ReadAllBytes(file);
                int i = 0;
                while (i + 6 <= b.Length && b[i] == 0x7E)
                {
                    int n = b[i + 1];
                    if (i + 2 + n + 4 > b.Length) break;
                    string name = System.Text.Encoding.UTF8.GetString(b, i + 2, n);
                    int len = System.BitConverter.ToInt32(b, i + 2 + n);
                    int at = i + 2 + n + 4;
                    if (len < 0 || at + len > b.Length) break;
                    if (name == tag) return len == 10 && b[at] == 0xFF ? System.BitConverter.ToSingle(b, at + 5) : 0f;
                    i = at + len;
                }
            }
            catch (System.Exception e) { Log.Warn("monde : " + file + " illisible (" + e.Message + ")"); }
            return 0f;
        }

        // Chemin d'un fichier du mod rattache au monde ([Test] DossierSacs : un dossier a part pour les copies d'essai, qui
        // partagent sinon le dossier du vrai jeu ; avant : la meme ligne dans quatre modules).
        public static string PathOf(string file)
        {
            string d = Config.Get("Test", "DossierSacs", "");
            return Path.Combine(d.Length > 0 ? d : UnityEngine.Application.persistentDataPath, file);
        }

        public static void Check(string dir)
        {
            if (done.Contains(dir)) return;
            FsmFloat pid = FsmVariables.GlobalVariables.FindFsmFloat("PlayerID");
            if (pid == null || pid.Value == 0f) return;   // (monde pas encore connu : on verra au prochain appel)
            done.Add(dir);
            string id = pid.Value.ToString("F0");
            string stamp = Path.Combine(dir, "MWCoop-monde.txt");
            try
            {
                string was = File.Exists(stamp) ? File.ReadAllText(stamp).Trim() : "";
                if (was.Length > 0 && was != id)
                {
                    var gone = new List<string>();
                    foreach (string f in Files) { string p = Path.Combine(dir, f); if (File.Exists(p)) { File.Delete(p); gone.Add(f); } }
                    Log.Info("monde : nouvelle partie (" + was + " -> " + id + ") : fichiers du monde d'avant effaces" + (gone.Count > 0 ? " (" + string.Join(", ", gone.ToArray()) + ")" : " (aucun)"));
                }
                else if (was.Length == 0) Log.Info("monde : fichiers du mod rattaches au monde " + id);
                if (was != id) File.WriteAllText(stamp, id);
            }
            catch (System.Exception e) { Log.Warn("monde : " + e.Message); }
        }
    }
}
