using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace MWCoop.Net
{
    // Hote : exclure un joueur, le faire venir a soi (menu F10 et lanceur, demandes de JD du 07/10). Et l'etat de la
    // partie pour le lanceur reste ouvert (onglet SERVEUR) : toutes les secondes, <MWCOOP_DATA>\serveur.txt ; ses ordres
    // arrivent par <MWCOOP_DATA>\commandes.txt (lu, execute puis efface). Lignes separees par des tabulations :
    //   etat    <hote 0|1> <steam 0|1> <statut> <niveau> <heure unix>
    //   joueur  <numero> <pseudo> <ping ms, -1 : inconnu> <en partie 0|1> <compte Steam> <apparence> <moi 0|1>
    //   (ordres) kick <numero> | venir <numero>
    public static class Admin
    {
        // ---- exclure
        public static void Kick(int id, string why)
        {
            if (!Session.IsHost || Session.T == null) return;
            PlayerInfo pi;
            if (!Session.Players.TryGetValue(id, out pi) || pi.Local || pi.Peer == null) return;
            Log.Info("exclusion de " + pi.Name + " (" + why + ")");
            Hud.Toast(pi.Name + Lang.T(" a été exclu de la partie", " was kicked from the game"));
            Session.T.Kick(pi.Peer, "exclu par l'hote");
        }

        // ---- faire venir : l'hote envoie sa position au joueur, qui s'y deplace (derriere lui, comme "aller vers")
        public static void Summon(int id)
        {
            if (!Session.IsHost || Session.T == null) return;
            PlayerInfo pi;
            if (!Session.Players.TryGetValue(id, out pi) || pi.Local || pi.Peer == null || !pi.Peer.Accepted) return;
            GameObject me = GameObject.Find("PLAYER");
            if (me == null || !PlayerSync.InGame) { Hud.Toast(Lang.T("Faire venir : soyez en partie", "Bring here: be in game")); return; }
            Vector3 p = me.transform.position;
            float yaw = PlayerSync.LocalCamera != null ? PlayerSync.LocalCamera.eulerAngles.y : me.transform.eulerAngles.y;
            var w = new NetWriter(Msg.Summon).F32(p.x).F32(p.y).F32(p.z).F32(yaw);
            Session.T.SendReliable(pi.Peer, w.ToArray());
            Log.Info("faire venir : " + pi.Name);
            Hud.Toast(pi.Name + Lang.T(" arrive", " is coming"));
        }

        public static void OnSummon(Peer from, NetReader r)
        {
            if (Session.IsHost) return;
            var p = new Vector3(r.F32(), r.F32(), r.F32());
            float yaw = r.F32();
            if (VehicleSync.LocalDriving >= 0 || Seats.Seated)
            {
                Hud.Toast(Lang.T("L'hôte veut vous faire venir : sortez d'abord du véhicule", "The host wants to bring you over: get out of the vehicle first"));
                Log.Info("faire venir : refuse (dans un vehicule)");
                return;
            }
            Menu.TeleportTo(p, yaw, Lang.T("l'hôte", "the host"));
            Hud.Toast(Lang.T("L'hôte vous a fait venir", "The host brought you over"));
        }

        // ---- etat pour le lanceur
        static float next;
        static string dir;

        public static void Update()
        {
            if (Time.realtimeSinceStartup < next) return;
            next = Time.realtimeSinceStartup + 1f;
            try
            {
                if (dir == null) dir = Environment.GetEnvironmentVariable("MWCOOP_DATA") ?? Path.Combine(Environment.GetEnvironmentVariable("MWCOOP_DIR") ?? "MWCoop", "");
                Commands();
                WriteState();
            }
            catch (Exception e) { Log.Warn("etat pour le lanceur : " + e.Message); next += 10f; }
        }

        static string Clean(string s) { return (s ?? "").Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' '); }

        static void WriteState()
        {
            var sb = new StringBuilder();
            long now = (long)(DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalSeconds;
            sb.Append("etat\t").Append(Session.IsHost ? 1 : 0).Append('\t').Append(Session.Steam ? 1 : 0).Append('\t').Append(Clean(Lang.Status(Session.Status)))
              .Append('\t').Append(Clean(Application.loadedLevelName)).Append('\t').Append(now).Append('\n');
            foreach (PlayerInfo pi in Session.Players.Values)
            {
                int ping = -1;
                if (!pi.Local && pi.Peer != null) ping = Mathf.RoundToInt(pi.Peer.Rtt * 1000);
                else if (!pi.Local && !Session.IsHost && pi.Id == 0) ping = Session.HostPing;
                sb.Append("joueur\t").Append(pi.Id).Append('\t').Append(Clean(pi.Name)).Append('\t').Append(ping).Append('\t').Append(pi.Level == 1 ? 1 : 0).Append('\t')
                  .Append(pi.SteamId).Append('\t').Append(Clean(pi.Skin)).Append('\t').Append(pi.Local ? 1 : 0).Append('\n');
            }
            string path = Path.Combine(dir, "serveur.txt"), tmp = path + ".tmp";
            File.WriteAllText(tmp, sb.ToString(), new UTF8Encoding(false));
            if (File.Exists(path)) File.Delete(path);
            File.Move(tmp, path);
        }

        static void Commands()
        {
            string path = Path.Combine(dir, "commandes.txt");
            if (!File.Exists(path)) return;
            string[] lines = File.ReadAllLines(path);
            File.Delete(path);
            foreach (string l in lines)
            {
                string[] f = l.Split('\t');
                int id;
                if (f.Length < 2 || !int.TryParse(f[1], out id)) continue;
                Log.Info("lanceur : " + f[0] + " " + id);
                if (f[0] == "kick") Kick(id, "depuis le lanceur");
                else if (f[0] == "venir") Summon(id);
            }
        }
    }
}
