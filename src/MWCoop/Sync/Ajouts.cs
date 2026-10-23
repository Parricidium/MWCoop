using MWCoop.Net;
using UnityEngine;

namespace MWCoop
{
    // Ajouts de MWCoop au jeu (ce qui n'existe pas dans le jeu de base) : camera drone (F8), sacs de couchage, boutique
    // OSUUSKAUPPA (coin video, KARTTURI), musee de la Corse. Dans le lanceur (Options du jeu > Coop > Ajouts COOP) :
    //  - « Tout desactiver » ([Coop] SansAjouts=1 ; demande de Menor, 12/10 : « un jeu 100 % vanilla », et de JD : un bouton
    //    pour tous, presents et a venir) ;
    //  - et un interrupteur par ajout (demande de JD, 13/10 : garder le drone mais retirer la boutique et le musee...) :
    //    [Coop] AjoutDrone, AjoutSacs, AjoutBoutique, AjoutMusee, AjoutVaz, AjoutKino, AjoutOsat (1 par defaut ; VAZ-2108 du F10 : demande de JD, 18/10).
    // En partie, les choix de l'HOTE valent pour tous ses invites (JD, 13/10) : il les annonce (@ajouts, toutes les 10 s et a
    // chaque arrivee) ; un invite garde les siens en plus (il peut couper pour lui, pas rallumer ce que l'hote a coupe).
    // La boutique et le musee (construits 12 s apres le chargement) et le defrichage attendent l'annonce de l'hote (Ready ;
    // 30 s au plus : hote d'une version plus ancienne).
    // Tout nouvel ajout de ce genre doit avoir son interrupteur ici et dans le lanceur (et, s'il vaut pour My Summer Car, son
    // bit dans B_Msc et sa cle dans les premiers de kAjoutKeys, launcher/gfx.inc).
    public static class Ajouts
    {
        const int B_Drone = 1, B_Bags = 2, B_Shop = 4, B_Museum = 8, B_Vaz = 16, B_Kino = 32, B_Osat = 64;
        static int hostBits = -1;
        static float nextSend, loadedAt;
        static int lastRemote;

        // My Summer Car (19/10) : seul le drone existe pour ce jeu (sacs de couchage, boutique s21, musee et VAZ-2108 sont faits
        // pour la carte et l'hiver de My Winter Car) -- les autres bits restent coupes, quels que soient les reglages.
        const int B_Msc = B_Drone;
        public static bool On { get { return Config.GetInt("Coop", "SansAjouts", 0) == 0; } }
        static int LocalBits
        {
            get
            {
                if (!On) return 0;
                int b = (Config.GetInt("Coop", "AjoutDrone", 1) != 0 ? B_Drone : 0) | (Config.GetInt("Coop", "AjoutSacs", 1) != 0 ? B_Bags : 0)
                      | (Config.GetInt("Coop", "AjoutBoutique", 1) != 0 ? B_Shop : 0) | (Config.GetInt("Coop", "AjoutMusee", 1) != 0 ? B_Museum : 0)
                      | (Config.GetInt("Coop", "AjoutVaz", 1) != 0 ? B_Vaz : 0) | (Config.GetInt("Coop", "AjoutKino", 1) != 0 ? B_Kino : 0)
                      | (Config.GetInt("Coop", "AjoutOsat", 1) != 0 ? B_Osat : 0);
                return Game.Msc ? b & B_Msc : b;
            }
        }
        static bool Guest { get { return Session.Active && !Session.IsHost; } }
        static int Bits { get { int b = LocalBits; return Guest && hostBits >= 0 ? b & hostBits : b; } }

        public static bool Drone { get { return (Bits & B_Drone) != 0; } }
        public static bool LocalDrone { get { return (LocalBits & B_Drone) != 0; } }
        public static bool Bags { get { return (Bits & B_Bags) != 0; } }
        public static bool Shop { get { return (Bits & B_Shop) != 0; } }
        public static bool Museum { get { return (Bits & B_Museum) != 0; } }
        // (hote d'une version sans la VAZ : son annonce n'a pas le bit -> coupee chez l'invite aussi, rien a suivre chez lui)
        public static bool Vaz { get { return (Bits & B_Vaz) != 0; } }
        // cinema KINO TAHTI (JD, 22/10 ; hote d'une version sans lui : pas de bit -> coupe chez l'invite aussi)
        public static bool Kino { get { return (Bits & B_Kino) != 0; } }
        // magasin de pieces CARDINAL VARAOSAT (JD, 23/10)
        public static bool Osat { get { return (Bits & B_Osat) != 0; } }

        // Choix de l'hote connus (ou pas besoin) : la boutique, le musee et le defrichage peuvent se faire.
        public static bool Ready { get { return !Guest || hostBits >= 0 || Time.realtimeSinceStartup - loadedAt > 30f; } }

        // (au menu : choix d'un ancien hote oublies ; la prochaine partie attend ceux du sien)
        public static void OnLevelLoaded() { loadedAt = Time.realtimeSinceStartup; if (Game.LevelName != "GAME") hostBits = -1; }

        public static void Update()
        {
            if (!Session.Active || !Session.IsHost) return;
            float now = Time.realtimeSinceStartup;
            if (now < nextSend && Session.RemoteCount <= lastRemote) { lastRemote = Session.RemoteCount; return; }
            lastRemote = Session.RemoteCount;
            nextSend = now + 10f;
            if (Session.RemoteCount > 0) Session.SendAll(new NetWriter(Msg.Job).U8(Session.LocalId).Str("@ajouts").U8(LocalBits), true);
        }

        public static void OnHost(int bits)
        {
            if (bits != hostBits)
                Log.Info("ajouts : ceux de l'hote " + Names(bits) + (hostBits < 0 ? "" : " (avant " + Names(hostBits) + ")") + ", les miens " + Names(LocalBits));
            hostBits = bits;
        }

        static string Names(int b)
        {
            if (b == 0) return "aucun";
            return (((b & B_Drone) != 0 ? "drone " : "") + ((b & B_Bags) != 0 ? "sacs " : "") + ((b & B_Shop) != 0 ? "boutique " : "") + ((b & B_Museum) != 0 ? "musee " : "") + ((b & B_Vaz) != 0 ? "vaz " : "") + ((b & B_Kino) != 0 ? "cinema " : "") + ((b & B_Osat) != 0 ? "pieces" : "")).TrimEnd();
        }

    }
}
