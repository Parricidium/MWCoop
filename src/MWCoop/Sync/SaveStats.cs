using System;
using System.Globalization;
using System.IO;
using System.Text;
using HutongGames.PlayMaker;
using UnityEngine;
using MWCoop.Net;

namespace MWCoop
{
    // Resume de la partie pour le lanceur (page Sauvegardes : jour, argent, heures jouees, derniere partie et avec qui) :
    // partie.txt a cote de la sauvegarde, ecrit a chaque sauvegarde du jeu (SaveHook). Le fichier du jeu est binaire
    // (Easy Save 2), le lanceur ne sait pas le lire -- maquette du rework validee par JD, 17/10.
    public static class SaveStats
    {
        static float sessionStart = -1;   // debut du niveau GAME (temps reel) ; -1 : pas en partie
        static float heuresAvant;         // heures deja comptees dans le fichier au chargement

        public static void OnLevelLoaded()
        {
            if (Game.LevelName != "GAME") { sessionStart = -1; return; }
            sessionStart = Time.realtimeSinceStartup;
            heuresAvant = ReadHours();
        }

        static string FilePath { get { return Path.Combine(SaveTransfer.SaveDir, "partie.txt"); } }

        static float ReadHours()
        {
            try
            {
                if (!File.Exists(FilePath)) return 0f;
                foreach (string l in File.ReadAllLines(FilePath))
                {
                    float h;
                    if (l.StartsWith("heures=") && float.TryParse(l.Substring(7), NumberStyles.Float, CultureInfo.InvariantCulture, out h)) return h;
                }
            }
            catch (Exception) { }
            return 0f;
        }

        // A chaque sauvegarde du jeu : cles "cle=valeur", une par ligne (lues par saves.inc du lanceur).
        public static void OnGameSave()
        {
            if (sessionStart < 0) return;
            try
            {
                CultureInfo ci = CultureInfo.InvariantCulture;
                FsmInt day = FsmVariables.GlobalVariables.FindFsmInt("GlobalDaysPassed");
                FsmFloat cash = FsmVariables.GlobalVariables.FindFsmFloat("PlayerMoney"), bank = FsmVariables.GlobalVariables.FindFsmFloat("PlayerBankAccount");
                float heures = heuresAvant + (Time.realtimeSinceStartup - sessionStart) / 3600f;
                var names = new System.Collections.Generic.List<string>();
                foreach (PlayerInfo p in Session.Players.Values)
                    if (!p.Local && !string.IsNullOrEmpty(p.Name)) names.Add(p.Name.Replace('|', ' ').Replace('\n', ' '));
                var sb = new StringBuilder();
                sb.Append("jour=").Append(day != null ? day.Value : -1).Append('\n');
                sb.Append("argent=").Append(cash != null ? (int)cash.Value : -1).Append('\n');
                sb.Append("banque=").Append(bank != null ? (int)bank.Value : -1).Append('\n');
                sb.Append("heures=").Append(heures.ToString("F2", ci)).Append('\n');
                sb.Append("date=").Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm", ci)).Append('\n');
                sb.Append("avec=").Append(string.Join("|", names.ToArray())).Append('\n');
                sb.Append("role=").Append(Session.Active ? (Session.IsHost ? "hote" : "invite") : "solo").Append('\n');
                // (version du mod qui a sauvegarde, et le jeu : « Verifier la sauvegarde » du lanceur signale les versions boguees, 26/10)
                sb.Append("version=").Append(Version.Text).Append('\n');
                sb.Append("jeu=").Append(Game.Msc ? "msc" : "mwc").Append('\n');
                File.WriteAllText(FilePath, sb.ToString(), new UTF8Encoding(false));
            }
            catch (Exception e) { Log.Warn("resume de la partie (partie.txt) : " + e.Message); }
        }
    }
}
