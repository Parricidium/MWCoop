using System;
using System.Collections.Generic;
using HutongGames.PlayMaker;
using MWCoop.Net;
using UnityEngine;

namespace MWCoop
{
    // Triches (proposition d'un joueur, 10/10 : « des commandes dans le tchat, avec une option pour les couper, comme
    // Minecraft » ; JD : en plus un petit panneau cliquable, touche reglable, tout synchronise). L'hote les autorise
    // ([Coop] Triches, lanceur : Options du jeu > Partie et partage ; coupees d'origine) et le dit aux invites (@triches).
    // Commandes : dans le tchat ("/aide") ou par le panneau (CheatPanel, touche Keys.Cheats). Chaque triche est annoncee
    // a tous (@triche + la ligne : « [Triche] JD : /heure 12 »). Celles du monde (heure, neige) sont faites par l'hote
    // (le monde suit l'hote : World) ; « amener » est faite par le joueur vise ; les autres chez celui qui triche.
    public static class Cheats
    {
        public class Place { public string Key, Fr, En; public Vector3 At, Face; }   // (Face : vers ou regarder en arrivant)
        // (appartement, maison des parents : les points de reapparition, Respawn ; les autres : au sol, reperes par
        // CheatsProbe -- Autotest=trichelieux)
        public static readonly Place[] Places = {
            new Place { Key = "appartement", Fr = "Appartement", En = "Apartment", At = new Vector3(-1285.5f, 1.2f, 1076.4f) },
            new Place { Key = "maison", Fr = "Maison des parents", En = "Parents' house", At = new Vector3(-6.2f, 1.0f, 6.6f) },
            // (1,5 m au-dessus d'un sol degage -- herbe, trottoir, route -- pres du lieu, pas sur un toit ; releve du 10/10)
            new Place { Key = "pub", Fr = "Pub Nappo", En = "Pub Nappo", At = new Vector3(-1539.1f, 4.4f, 1190.6f), Face = new Vector3(-1548.3f, 3.3f, 1182.9f) },
            new Place { Key = "ville", Fr = "Perajarvi", En = "Perajarvi", At = new Vector3(-1396.4f, 6.9f, 1156.6f), Face = new Vector3(-1405.6f, 9f, 1148.9f) },
            new Place { Key = "port", Fr = "Port", En = "Harbor", At = new Vector3(-1738.8f, 4.7f, 952.8f), Face = new Vector3(-1760f, 3.2f, 952.8f) },
            new Place { Key = "garage", Fr = "Garage", En = "Repair shop", At = new Vector3(1551.5f, 6.0f, 733.6f), Face = new Vector3(1557.5f, 3.9f, 723.2f) },
            new Place { Key = "inspection", Fr = "Contrôle technique", En = "Inspection", At = new Vector3(-1531.7f, 4.5f, 1281.6f), Face = new Vector3(-1533.8f, 3f, 1269.8f) },
            new Place { Key = "decharge", Fr = "Décharge", En = "Landfill", At = new Vector3(-767.6f, 13.2f, -639.8f), Face = new Vector3(-780f, 12f, -640f) },
            new Place { Key = "danse", Fr = "Salle de danse", En = "Dance hall", At = new Vector3(464.3f, 10.8f, 1325.9f), Face = new Vector3(455.1f, 13.1f, 1318.2f) },
            new Place { Key = "cabane", Fr = "Cabane", En = "Cottage", At = new Vector3(-170f, -2.5f, 1010f), Face = new Vector3(-170f, -3.8f, 1022f) },
            new Place { Key = "chalet", Fr = "Chalet", En = "Cabin", At = new Vector3(-840.1f, -2.3f, 517.9f), Face = new Vector3(-849.3f, -3.7f, 510.2f) },
        };

        static bool hostAllows;
        static float nextAnnounce;

        // Autorisees ici ? (hote ou solo : son reglage ; invite : celui de l'hote, recu)
        public static bool Allowed { get { return !Session.Active || Session.IsHost ? Config.GetInt("Coop", "Triches", 0) != 0 : hostAllows; } }

        public static void Update()
        {
            if (!Session.Active || !Session.IsHost) return;
            if (Time.realtimeSinceStartup < nextAnnounce) return;
            nextAnnounce = Time.realtimeSinceStartup + 10f;
            Session.SendAll(new NetWriter(Msg.Job).U8(Session.LocalId).Str("@triches").Bool(Allowed), true);
        }

        public static void OnAllowed(bool on)
        {
            if (on != hostAllows) Log.Info("triches : " + (on ? "autorisees" : "coupees") + " par l'hote");
            hostAllows = on;
        }

        static void Say(string s) { Chat.System(s); }

        static readonly string[] HelpFr = {
            "/tp <joueur> : aller à un joueur", "/amener <joueur> : faire venir un joueur", "/lieu <nom> : appartement, maison, pub, ville, port, garage, inspection, decharge, danse, cabane, chalet",
            "/argent <montant> : du liquide pour soi", "/soin : faim, soif, fatigue, stress, vessie, saleté à zéro", "/liberer : plus recherché, sorti de prison",
            "/heure <0-24> : l'heure de tous", "/neige oui|non : neige (ou pluie) pour tous" };
        static readonly string[] HelpEn = {
            "/tp <player>: go to a player", "/bring <player>: bring a player to you", "/place <name>: apartment, home, pub, town, harbor, garage, inspection, landfill, dance, cottage, cabin",
            "/money <amount>: cash for yourself", "/heal: hunger, thirst, fatigue, stress, bladder, dirt to zero", "/free: no longer wanted, out of jail",
            "/time <0-24>: everyone's time of day", "/snow on|off: snow (or rain) for everyone" };

        // Commandes anglaises -> les noms d'ici.
        static string Canon(string c)
        {
            switch (c)
            {
                case "help": case "?": return "aide";
                case "goto": case "va": return "tp";
                case "bring": return "amener";
                case "place": case "go": return "lieu";
                case "money": case "cash": return "argent";
                case "heal": return "soin";
                case "free": case "nojail": return "liberer";
                case "time": return "heure";
                case "snow": case "rain": case "pluie": case "meteo": case "weather": return "neige";
            }
            return c;
        }

        // Une ligne "/..." du tchat ou un bouton du panneau. Retourne un message d'erreur (deja dit dans le tchat) ou null.
        public static string Command(string line)
        {
            line = (line ?? "").Trim();
            string[] a = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (a.Length == 0) return null;
            string c = Canon(a[0].TrimStart('/').ToLowerInvariant());
            string arg = a.Length > 1 ? string.Join(" ", a, 1, a.Length - 1) : "";
            if (c == "aide") { foreach (string h in Lang.Fr ? HelpFr : HelpEn) Say(h); return null; }
            if (!Allowed)
            {
                string no = Session.Active && !Session.IsHost ? Lang.T("Triches coupées par l'hôte.", "Cheats are off (host's choice).")
                                                              : Lang.T("Triches coupées : lanceur, Options du jeu > Partie et partage > Autoriser les triches.", "Cheats are off: launcher, Game options > Game and sharing > Allow cheats.");
                Say(no);
                return no;
            }
            string err = Run(c, arg);
            if (err != null) { Say(err); return err; }
            Say("[" + Lang.T("Triche", "Cheat") + "] " + (Session.Me != null ? Session.Me.Name : "") + " : /" + c + (arg.Length > 0 ? " " + arg : ""));
            if (Session.Active) Session.SendAll(new NetWriter(Msg.Job).U8(Session.LocalId).Str("@triche").Str("/" + c + (arg.Length > 0 ? " " + arg : "")), true);
            Log.Info("triches : /" + c + " " + arg);
            return null;
        }

        // Chez celui qui triche (et, pour le monde, chez l'hote).
        static string Run(string c, string arg)
        {
            switch (c)
            {
                case "tp":
                {
                    PlayerInfo pi = FindPlayer(arg);
                    if (pi == null) return Lang.T("Joueur introuvable : ", "No such player: ") + arg;
                    if (pi.Level != 1) return pi.Name + Lang.T(" n'est pas en partie.", " is not in game.");
                    string busy = Busy(); if (busy != null) return busy;
                    Menu.TeleportTo(pi.State.Feet, pi.State.Yaw, pi.Name);
                    return null;
                }
                case "amener":
                {
                    PlayerInfo pi = FindPlayer(arg);
                    if (pi == null) return Lang.T("Joueur introuvable : ", "No such player: ") + arg;
                    if (pi.Level != 1) return pi.Name + Lang.T(" n'est pas en partie.", " is not in game.");
                    return null;   // (fait chez lui, a l'annonce)
                }
                case "lieu":
                {
                    Place p = FindPlace(arg);
                    if (p == null) return Lang.T("Lieu inconnu. /aide pour la liste.", "Unknown place. /help for the list.");
                    string busy = Busy(); if (busy != null) return busy;
                    GoPlace(p);
                    return null;
                }
                case "argent":
                {
                    int n;
                    if (!int.TryParse(arg.Replace(" ", ""), out n) || n <= 0 || n > 1000000) return Lang.T("Montant : de 1 à 1000000.", "Amount: 1 to 1000000.");
                    if (!Wallet.AddLocal(n)) return Lang.T("Porte-monnaie pas prêt.", "Wallet not ready.");
                    return null;
                }
                case "soin":
                    foreach (string g in new[] { "PlayerHunger", "PlayerThirst", "PlayerFatigue", "PlayerStress", "PlayerUrine", "PlayerDirtiness", "PlayerBurns", "PlayerAllergy" })
                        if (Game.GlobalFloat(g) >= 0f) Game.SetGlobalFloat(g, 0f);
                    return null;
                case "liberer":
                    return Free();
                case "heure":
                {
                    int h;
                    if (!int.TryParse(arg, out h) || h < 0 || h > 24) return Lang.T("Heure : de 0 à 24.", "Time: 0 to 24.");
                    if (!Session.Active || Session.IsHost) SetHour(h);   // (invite : l'hote le fait, a l'annonce)
                    return null;
                }
                case "neige":
                {
                    bool? on = OnOff(arg);
                    if (on == null) return Lang.T("/neige oui ou /neige non", "/snow on or /snow off");
                    if (!Session.Active || Session.IsHost) SetSnow(on.Value);
                    return null;
                }
            }
            return Lang.T("Commande inconnue. /aide pour la liste.", "Unknown command. /help for the list.");
        }

        // Annonce d'un autre joueur : montree ; l'hote fait celles du monde (s'il autorise les triches) ; le joueur vise
        // par « amener » va vers celui qui l'appelle.
        public static void OnRemote(int who, string line)
        {
            PlayerInfo from;
            string name = Session.Players.TryGetValue(who, out from) ? from.Name : "#" + who;
            line = Session.Clean(line, 120);
            Say("[" + Lang.T("Triche", "Cheat") + "] " + name + " : " + line);
            string[] a = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (a.Length == 0) return;
            string c = Canon(a[0].TrimStart('/').ToLowerInvariant()), arg = a.Length > 1 ? string.Join(" ", a, 1, a.Length - 1) : "";
            if (Session.IsHost && !Allowed) { Log.Warn("triches : " + name + " : " + line + " refusee (coupees ici)"); return; }
            if (Session.IsHost && c == "heure") { int h; if (int.TryParse(arg, out h) && h >= 0 && h <= 24) SetHour(h); }
            if (Session.IsHost && c == "neige") { bool? on = OnOff(arg); if (on != null) SetSnow(on.Value); }
            if (c == "amener" && from != null)
            {
                PlayerInfo me = FindPlayer(arg);
                if (me == null || !me.Local) return;
                if (Busy() != null) { Hud.Toast(name + Lang.T(" vous appelle : descendez du véhicule.", " calls you: get out of the vehicle.")); return; }
                Menu.TeleportTo(from.State.Feet, from.State.Yaw, name);
            }
        }

        static bool? OnOff(string s)
        {
            s = s.Trim().ToLowerInvariant();
            if (s == "oui" || s == "on" || s == "1" || s == "yes") return true;
            if (s == "non" || s == "off" || s == "0" || s == "no") return false;
            return null;
        }

        static string Busy()
        {
            if (VehicleSync.LocalDriving >= 0 || Seats.Seated) return Lang.T("Descends du véhicule d'abord.", "Get out of the vehicle first.");
            return null;
        }

        static PlayerInfo FindPlayer(string q)
        {
            q = (q ?? "").Trim();
            if (q.Length == 0) return null;
            PlayerInfo best = null;
            foreach (PlayerInfo pi in Session.Players.Values)
            {
                if (string.Equals(pi.Name, q, StringComparison.OrdinalIgnoreCase)) return pi;
                if (best == null && pi.Name.StartsWith(q, StringComparison.OrdinalIgnoreCase)) best = pi;
            }
            return best;
        }

        static Place FindPlace(string q)
        {
            q = (q ?? "").Trim().ToLowerInvariant();
            if (q.Length == 0) return null;
            string[] en = { "apartment", "home", "pub", "town", "harbor", "garage", "inspection", "landfill", "dance", "cottage", "cabin" };
            for (int i = 0; i < Places.Length; i++)
                if (Places[i].Key.StartsWith(q) || (i < en.Length && en[i].StartsWith(q)) || Places[i].Fr.ToLowerInvariant().StartsWith(q)) return Places[i];
            return null;
        }

        // Au sol du lieu (le plus haut sol sous le point, sans les declencheurs), comme une reapparition.
        public static void GoPlace(Place p)
        {
            Vector3 top = p.At;   // (sous le point, comme Respawn : dans l'appartement, pas sur le plafond)
            float best = float.NegativeInfinity;
            foreach (RaycastHit h in Physics.RaycastAll(top, Vector3.down, 8f))
                if (h.collider != null && !h.collider.isTrigger && Game.RootName(h.collider.transform) != "PLAYER" && h.point.y > best) best = h.point.y;
            Vector3 feet = new Vector3(p.At.x, best > float.NegativeInfinity ? best : p.At.y, p.At.z);
            Menu.TeleportTo(feet + Quaternion.Euler(0f, 180f, 0f) * new Vector3(0f, 0f, 1.5f), 180f, Lang.Fr ? p.Fr : p.En);   // (TeleportTo recule de 1,5 m selon le cap : sur le point)
            if (p.Face != Vector3.zero && Game.PlayerT != null)
            {   // tourne vers le lieu (le corps porte le cap ; la camera garde sa hauteur de regard)
                Vector3 d = p.Face - p.At; d.y = 0f;
                if (d.sqrMagnitude > 0.01f) Game.PlayerT.rotation = Quaternion.LookRotation(d);
            }
        }

        // ---------------------------------------------------------------- monde (hote)
        public static void SetHour(int h)
        {
            PlayMakerFSM sun = Game.FindFsm("MAP/Sun/PivotSun/SUN", "Color");
            if (sun == null) return;
            sun.FsmVariables.GetFsmInt("Time").Value = h;
            sun.SendEvent("TIMESKIP");
            PlayMakerFSM wx = Game.FindFsm("MAP/WEATHER/Clouds", "Weather");
            if (wx != null && wx.Fsm.Initialized) { FsmInt t = wx.FsmVariables.FindFsmInt("Time"); if (t != null) t.Value = h; }
            Log.Info("triches : heure " + h);
        }

        // Neige (ou pluie) : ce que l'automate Forecast::Logic pose chaque jour (Fudge) -- Snowing, l'objet des nuages de
        // precipitation, et la pluie du joueur ; l'hote l'envoie aux invites (World). Jusqu'au tirage du jour suivant.
        public static void SetSnow(bool on)
        {
            PlayMakerFSM fc = Game.FindFsm("MAP/WEATHER/Forecast", "Logic");
            FsmBool sn = fc != null ? fc.FsmVariables.FindFsmBool("Snowing") : null;
            if (sn != null) sn.Value = on;
            GameObject co = Game.FindAny("MAP/WEATHER/Clouds/CloudObjects");
            if (co != null) co.SetActive(on);
            GameObject rain = Game.PlayerPart("Rain");
            PlayMakerFSM rf = rain != null ? Game.FsmOn(rain, "Rain") : null;
            FsmBool ry = rf != null ? rf.FsmVariables.FindFsmBool("RainYes") : null;
            if (ry != null) ry.Value = on;
            Log.Info("triches : neige " + (on ? "oui" : "non") + " (Snowing " + (sn != null) + ", nuages " + (co != null) + ", pluie " + (ry != null) + ")");
        }

        // ---------------------------------------------------------------- police
        static readonly string[] CrimeInts = { "AttemptedManslaughter", "ManSlaughter", "PoliceEvasion", "TrafficFatality", "DaysAttemptedManslaughter", "DaysEvasion", "DaysFines", "DaysManslaughter", "DaysTrafficFatality", "DaysInJail", "Sentence" };
        static string Free()
        {
            GameObject wgo = Game.FindAny("Systems/PlayerWanted");
            PlayMakerFSM w = wgo != null ? Game.FsmOn(wgo, "Activate") : null;
            if (w == null || !w.Fsm.Initialized) return Lang.T("Police introuvable.", "Police not found.");
            foreach (string n in CrimeInts) { FsmInt v = w.FsmVariables.FindFsmInt(n); if (v != null) v.Value = 0; }
            FsmFloat d = w.FsmVariables.FindFsmFloat("Days"); if (d != null) d.Value = 0f;
            FsmBool pw = FsmVariables.GlobalVariables.FindFsmBool("PlayerWanted"); if (pw != null) pw.Value = false;
            GameObject cops = Game.FindAny("COPS");
            if (cops != null && cops.activeSelf) cops.SetActive(false);
            foreach (UnityEngine.Object o in Game.AllFsms()) { PlayMakerFSM f = o as PlayMakerFSM; FsmBool b = f != null ? f.FsmVariables.FindFsmBool("CopsAtHome") : null; if (b != null) b.Value = false; }
            if (w.ActiveStateName == "At home") Game.SetState(w, "Idle");
            // en prison : la sortie du jeu (retour en ville)
            GameObject jgo = Game.FindAny("JAIL/Functions");
            PlayMakerFSM jt = jgo != null ? Game.FsmOn(jgo, "Time") : null;
            bool jailed = jt != null && jgo.activeInHierarchy && jt.Fsm.Initialized && Game.PlayerT != null && (Game.PlayerT.position - jgo.transform.position).sqrMagnitude < 40f * 40f;
            if (jailed && jt.Fsm.GetState("Release") != null) { Game.SetState(jt, "Release"); Log.Info("triches : sortie de prison"); }
            return null;
        }

        // [Test] Autotest=triches : l'hote autorise ([Coop] Triches=1) ; a 30 s l'invite fait /heure 6 (l'hote doit
        // suivre, puis lui par World), a 36 s /neige oui, a 42 s /argent 500 et /soin, a 48 s /tp vers l'hote, a 54 s il
        // ouvre le panneau (capture). Journal : heure et neige des deux cotes.
        static int testStep;
        public static void Test(string mode, float t)
        {
            // [Test] Autotest=trichestp : un lieu toutes les 6 s a partir de 25 s, capture lieu-<cle> 4 s apres.
            if (mode == "trichestp")
            {
                int i = (int)((t - 25f) / 6f);
                if (t < 25f || i < 0 || i >= Places.Length || i < testStep) return;
                testStep = i + 1;
                GoPlace(Places[i]);
                Autotest.CaptureSoon("lieu-" + Places[i].Key, 4f);
                Log.Info("autotest : trichestp, " + Places[i].Key + " -> " + (Game.PlayerT != null ? Game.PlayerT.position.ToString("F1") : "?"));
                return;
            }
            if (mode != "triches") return;
            if (t > 30f && testStep == 0) { testStep = 1; if (!Session.IsHost) Command("/heure 6"); }
            if (t > 36f && testStep == 1) { testStep = 2; if (!Session.IsHost) Command("/neige oui"); }
            if (t > 40f && testStep == 2) { testStep = 3; LogWorld(); }
            if (t > 42f && testStep == 3) { testStep = 4; if (!Session.IsHost) { float c0 = Wallet.Cash; Command("/argent 500"); Command("/soin"); Log.Info("autotest : triches, liquide " + c0 + " -> " + Wallet.Cash + ", faim " + Game.GlobalFloat("PlayerHunger")); } }
            if (t > 48f && testStep == 4) { testStep = 5; if (!Session.IsHost && Session.Host != null) { Command("/tp " + Session.Host.Name); } }
            if (t > 52f && testStep == 5) { testStep = 6; LogWorld(); }
            if (t > 54f && testStep == 6) { testStep = 7; if (!Session.IsHost) { CheatPanel.Open = true; Autotest.CaptureSoon("triches", 1f); } }
        }

        static void LogWorld()
        {
            PlayMakerFSM sun = Game.FindFsm("MAP/Sun/PivotSun/SUN", "Color");
            PlayMakerFSM fc = Game.FindFsm("MAP/WEATHER/Forecast", "Logic");
            FsmBool sn = fc != null ? fc.FsmVariables.FindFsmBool("Snowing") : null;
            Log.Info("autotest : triches, heure " + (sun != null ? sun.FsmVariables.GetFsmInt("Time").Value : -1) + ", neige " + (sn != null && sn.Value) + ", autorisees " + Allowed
                     + (Game.PlayerT != null ? ", position " + Game.PlayerT.position.ToString("F0") : ""));
        }
    }
}
