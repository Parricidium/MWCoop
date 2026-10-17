using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using HutongGames.PlayMaker;
using MWCoop.Net;
using UnityEngine;

namespace MWCoop
{
    // La partie de l'hote rangee chez l'invite (demande de JD, 18/10) : quand l'hote sauvegarde (toilettes, K_NOW de
    // SaveTransfer), l'invite sauvegarde aussi chez lui (profil isole MWCoop\profils\invite : le monde tel qu'il le voit,
    // synchronise) puis recopie ses fichiers dans une partie du lanceur MWCoop\profils\partie-N (une par monde : PlayerID ->
    // partie-N dans profils\invite\parties.ini), nommee « Chez <hote> » (nom.txt, comme le lanceur). Elle apparait dans
    // Sauvegardes : il peut la continuer en hote avec quelqu'un d'autre. Son propre etat (argent, corps : Wallet) est deja
    // dans ses globales au moment de la sauvegarde, donc dans cette copie.
    public static class GuestSlot
    {
        static float copyAt = -1;
        static int seq;
        static string lastDone = "";

        public static void OnLevelLoaded() { copyAt = -1; }

        // K_NOW de l'hote (il ecrit maintenant) : sauvegarde locale, copie 4 s plus tard (SaveInPlace fige ~4 s puis ecrit).
        public static void OnHostSaving(int s)
        {
            if (Session.IsHost || !SaveTransfer.IsolatedProfile || !PlayerSync.InGame) return;
            if (Config.GetInt("Coop", "PartieChezInvite", 1) == 0) return;   // ([Coop] PartieChezInvite=0 : comme avant)
            if (!Game.Saving) Game.SaveInPlace();
            seq = s;
            copyAt = Time.realtimeSinceStartup + 4f;
        }

        public static void Update()
        {
            if (copyAt < 0 || Time.realtimeSinceStartup < copyAt || Game.Saving) return;
            copyAt = -1;
            try { Copy(); } catch (Exception e) { Log.Warn("partie chez l'invite : " + e.Message); }
        }

        // MWCoop\profils (racine des parties du lanceur) depuis le dossier de sauvegarde du profil isole.
        static string ProfilsRoot(string saveDir)
        {
            string d = saveDir.Replace('\\', '/').TrimEnd('/');
            int i = d.LastIndexOf("/profils/", StringComparison.OrdinalIgnoreCase);
            return i > 0 ? d.Substring(0, i + 8) : null;
        }

        static void Copy()
        {
            string src = SaveTransfer.SaveDir, root = ProfilsRoot(src);
            if (root == null) { Log.Warn("partie chez l'invite : dossier de profils introuvable (" + src + ")"); return; }
            FsmFloat pid = FsmVariables.GlobalVariables.FindFsmFloat("PlayerID");
            if (pid == null || pid.Value == 0f) { Log.Warn("partie chez l'invite : PlayerID inconnu, pas de copie"); return; }
            string world = pid.Value.ToString("F0");
            PlayerInfo host = Session.Host;
            string hostName = host != null && !string.IsNullOrEmpty(host.Name) ? host.Name : "?";
            // Partie de ce monde (parties.ini : <PlayerID>=partie-N), sinon la premiere libre.
            string map = Path.Combine(Path.Combine(root, "invite"), "parties.ini");
            var lines = new List<string>();
            string prof = null;
            if (File.Exists(map))
                foreach (string l in File.ReadAllLines(map))
                {
                    int eq = l.IndexOf('=');
                    if (eq <= 0) continue;
                    if (l.Substring(0, eq) == world) prof = l.Substring(eq + 1).Trim();
                    lines.Add(l);
                }
            if (prof == null || !prof.StartsWith("partie-"))
            {
                int n = 1;
                while (Directory.Exists(Path.Combine(root, "partie-" + n))) n++;
                prof = "partie-" + n;
                lines.Add(world + "=" + prof);
                Directory.CreateDirectory(Path.Combine(root, "invite"));
                File.WriteAllLines(map, lines.ToArray());
            }
            string dir = Path.Combine(root, prof);
            string dst = Path.Combine(Path.Combine(Path.Combine(dir, "LocalLow"), "Amistech"), "My Winter Car");
            Directory.CreateDirectory(dst);
            int n2 = 0; long bytes = 0;
            foreach (string f in Directory.GetFiles(src, "*.txt"))
            {
                string name = Path.GetFileName(f);
                if (string.Equals(name, "options.txt", StringComparison.OrdinalIgnoreCase)) continue;   // (reglages du joueur : les siens)
                File.Copy(f, Path.Combine(dst, name), true);
                n2++; bytes += new FileInfo(f).Length;
            }
            string nom = Path.Combine(dir, "nom.txt");
            string label = Lang.T("Chez ", "At ") + hostName;
            string was = File.Exists(nom) ? File.ReadAllText(nom, Encoding.UTF8).Trim() : "";
            if (was.Length == 0 || was.StartsWith("Chez ") || was.StartsWith("At ")) File.WriteAllText(nom, label, new UTF8Encoding(false));
            else label = was;   // (renommee par le joueur dans le lanceur : son nom garde)
            string key = seq + ":" + prof;
            if (lastDone != key)
            {
                lastDone = key;
                Log.Info("partie chez l'invite : sauvegarde coop #" + seq + " rangee dans " + prof + " (« " + label + "», " + n2 + " fichiers, " + bytes / 1024 + " Ko, monde " + world + ")");
                Hud.Toast(Lang.T("Partie rangée chez vous : « ", "Game kept on your side: \"") + label + Lang.T(" » (lanceur > Sauvegardes)", "\" (launcher > Saves)"));
            }
        }
    }
}
