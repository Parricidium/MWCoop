using HutongGames.PlayMaker;
using MWCoop.Net;
using UnityEngine;

namespace MWCoop
{
    // Trajets de Teimo en luge (STORE_AREA/TeimoInSled::Move), decides par l'hote. L'horaire du magasin
    // (STORE_AREA::OpeningHours) relit l'heure chaque seconde de jeu et envoie TOSTORE pendant l'heure 10 ; TeimoInBar
    // (::Animate) envoie TOHOME a la fermeture. L'heure est commune, mais elle est relue a une seconde de jeu pres :
    // pendant un sommeil accelere, l'un voyait passer 10 h (luge vers le magasin), l'autre la sautait (Teimo
    // directement au comptoir) -- Teimo arrivait en luge chez l'un pendant qu'il etait au pub chez l'autre (retour de
    // CBetoy, 13/10). Ici :
    //  - hote : son automate tourne comme d'habitude ; la phase du trajet (chez lui, vers le magasin, au magasin, vers
    //    chez lui) part a chaque changement et toutes les 10 s, sur le canal des evenements (Events, cle Key) ;
    //  - invite : TOSTORE et TOHOME n'ont plus d'effet sur sa luge ; elle passe dans la phase de l'hote (son corps suit
    //    deja celui de l'hote, Npcs).
    public static class TeimoSled
    {
        public const string Key = "teimo:luge";
        static PlayMakerFSM move;
        static float nextFind, nextSend, nextFull;
        static string lastSent, wanted;
        static bool patched;
        static int logs;

        public static void OnLevelLoaded() { move = null; patched = false; lastSent = null; wanted = null; nextFind = 0f; }

        static string Phase(string state)
        {
            switch (state)
            {
                case "At home": return "home";
                case "Set route": case "New path": case "Loop": return "tostore";
                case "State 2": case "At store": return "store";
                case "Set route 2": case "New path 2": case "Loop 2": return "tohome";
            }
            return null;
        }

        public static void Update()
        {
            if (!Session.Active || !PlayerSync.InGame) return;
            float now = Time.realtimeSinceStartup;
            if (move == null)
            {
                if (now < nextFind) return;
                nextFind = now + 5f;
                GameObject g = Game.FindAny("STORE_AREA/TeimoInSled");
                move = g != null ? Game.FsmOn(g, "Move") : null;
                if (move == null) return;
            }
            if (!Session.IsHost)
            {
                if (!patched) Patch();
                if (wanted != null && now >= nextSend) { nextSend = now + 0.5f; Follow(); }
                return;
            }
            if (Session.RemoteCount == 0 || now < nextSend) return;
            nextSend = now + 0.5f;
            string ph = Phase(move.ActiveStateName);
            if (ph == null) return;
            bool full = now >= nextFull;
            if (ph == lastSent && !full) return;
            if (full) nextFull = now + 10f;
            if (ph != lastSent && logs++ < 20) Log.Info("teimo : luge, phase " + ph + " envoyee");
            lastSent = ph;
            Session.SendAll(new NetWriter(Msg.Event).Str(Key).Str(ph), true);
        }

        // Invite : TOSTORE / TOHOME envoyes par son horaire ne menent plus nulle part (evenements renommes).
        static void Patch()
        {
            if (move.Fsm == null || !move.Fsm.Initialized) return;
            int n = 0;
            foreach (FsmState s in move.Fsm.States)
                foreach (FsmTransition t in s.Transitions)
                    if (t.EventName == "TOSTORE" || t.EventName == "TOHOME") { t.FsmEvent = FsmEvent.GetFsmEvent("MWCOOP_" + t.EventName); n++; }
            patched = true;
            Log.Info("teimo : luge menee par l'hote (" + n + " transitions de l'horaire coupees ici)");
        }

        public static void OnMessage(NetReader r)
        {
            if (Session.IsHost) return;
            wanted = r.Str();
            nextSend = 0f;
        }

        // [Test] Autotest=luge : a 30 s l'hote envoie Teimo au magasin (TOSTORE, comme l'horaire a 10 h) ; a 45 s l'invite
        // envoie TOSTORE a SA luge (doit rester sans effet) ; toutes les 5 s, chacun note la phase et le point de la luge.
        // Attendu chez l'invite : « luge home -> tostore (comme chez l'hote) », puis la meme phase que l'hote.
        static int tStep; static float tLog;
        public static void Test(string mode, float t)
        {
            if (mode != "luge" || move == null) return;
            if (Session.IsHost && t > 30f && tStep == 0) { tStep = 1; move.SendEvent("TOSTORE"); Log.Info("autotest : luge, TOSTORE envoye ici (hote)"); }
            if (!Session.IsHost && t > 45f && tStep == 0) { tStep = 1; move.SendEvent("TOSTORE"); Log.Info("autotest : luge, TOSTORE envoye ici (invite) : sans effet attendu"); }
            if (t > tLog) { tLog = t + 5f; Log.Info("autotest : luge, etat " + move.ActiveStateName + " (" + Phase(move.ActiveStateName) + "), luge en " + move.transform.position.ToString("F1")); }
        }

        static void Follow()
        {
            string here = Phase(move.ActiveStateName);
            if (here == null || here == wanted) return;
            string to = wanted == "home" ? "At home" : wanted == "tostore" ? "Set route" : wanted == "store" ? "State 2" : wanted == "tohome" ? "Set route 2" : null;
            if (to == null) return;
            // (arrivee au magasin pendant le trajet ici : la luge finit d'abord son chemin, comme chez l'hote)
            if (wanted == "store" && here == "tostore") return;
            if (wanted == "home" && here == "tohome") return;
            Game.SetState(move, to);
            if (logs++ < 20) Log.Info("teimo : luge " + here + " -> " + wanted + " (comme chez l'hote)");
        }
    }
}
