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

        public static void OnLevelLoaded() { done.Clear(); }

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
