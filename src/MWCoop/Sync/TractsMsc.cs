using System.Collections;
using System.Collections.Generic;
using HutongGames.PlayMaker;
using MWCoop.Net;
using UnityEngine;

namespace MWCoop
{
    // Tracts de Teimo, My Summer Car (vidage v.250908-04, 19/10). JOBS/TeimoADs::Data tient le boulot (JobAccepted, Delivered) et une
    // liste de 28 booleens (une case par boite aux lettres, reference ""). Une boite (*/MailBox/BoxHatch::WaitAd, BoxIndex 0..27)
    // servie : "Close" -> AdvertPlaced, Delivered +1, case de la liste a vrai. Chez les autres rien ne bougeait : la boite restait
    // a servir, et la paie (PayMoneyAdvert : Delivered x 25) ne comptait que les tracts poses chez soi.
    // Ici : la liste est relue 4 fois par seconde ; une case passee a vrai part (@tract i) ; chez les autres, si elle est encore
    // fausse : case a vrai, Delivered +1, AdvertPlaced de la boite (jamais "Open"/"Close" : "Open" detruit l'objet tenu, "Close"
    // compterait deux fois). Les remises a zero (lundi, fin du boulot) restent a chacun.
    // Paie (audit MSC 02/11) : PayMoneyAdvert verse Delivered x 25, le total de l'equipe, a celui qui prend l'enveloppe le premier ;
    // les autres n'avaient rien (enveloppe retiree, Wallet). Les boites sont maintenant comptees par joueur (servies ici : Update ;
    // chez un autre : @tract) : le preneur touche les siennes et celles de personne (servies avant le chargement, joueur parti),
    // chaque autre joueur present la sienne, versee chez lui a la prise (Wallet.OnPayout). Compte remis a zero quand Delivered
    // retombe a 0 (paie, lundi). Delivered lui-meme n'est compte que par ce module (Jobs.OwnVars). [Test] TractsParJoueur=0 : comme avant.
    public static class TractsMsc
    {
        static PlayMakerFSM data;
        static ArrayList list;
        static bool[] seen;
        static float nextPoll, nextFind;
        static bool applying;
        static readonly Dictionary<int, int> served = new Dictionary<int, int>();   // boites servies pendant la tournee, par joueur

        public static void OnLevelLoaded() { data = null; list = null; seen = null; nextFind = 0f; served.Clear(); }

        static void Served(int who)
        {
            int n; served.TryGetValue(who, out n); served[who] = n + 1;
        }

        static int Delivered()
        {
            FsmInt d = data != null ? data.FsmVariables.FindFsmInt("Delivered") : null;
            return d != null ? d.Value : 0;
        }

        // Paie partagee par joueur : MSC, partie a plusieurs, pas coupee pour essai.
        public static bool SplitPay()
        {
            return Game.Msc && Session.Active && Session.RemoteCount > 0 && Config.GetInt("Test", "TractsParJoueur", 1) != 0 && Find();
        }

        // Preneur local de l'enveloppe : nombre de boites a se faire payer (Delivered moins les parts des autres joueurs presents) ;
        // 'others' recoit (joueur, boites) de chacun d'eux, tel que compte ici -- envoye avec la prise (Wallet), le total paye reste Delivered.
        public static int TakerCount(List<KeyValuePair<int, int>> others)
        {
            int del = Delivered(), given = 0;
            foreach (KeyValuePair<int, int> kv in served)
            {
                if (kv.Key == Session.LocalId || kv.Value <= 0 || !Session.Players.ContainsKey(kv.Key)) continue;
                int n = System.Math.Min(kv.Value, del - given);
                if (n <= 0) continue;
                others.Add(new KeyValuePair<int, int>(kv.Key, n));
                given += n;
            }
            return System.Math.Max(0, del - given);
        }

        static bool Find()
        {
            if (data != null && list != null) return true;
            if (Time.realtimeSinceStartup < nextFind) return false;
            nextFind = Time.realtimeSinceStartup + 10f;
            GameObject g = Game.FindAny("JOBS/TeimoADs");
            if (g == null) return false;
            data = Game.FsmOn(g, "Data");
            foreach (PlayMakerArrayListProxy p in g.GetComponents<PlayMakerArrayListProxy>())
                if ((p.referenceName ?? "") == "" && p.arrayList != null) { list = p.arrayList; break; }
            if (data == null || list == null) return false;
            seen = new bool[list.Count];
            for (int i = 0; i < list.Count; i++) seen[i] = list[i] is bool && (bool)list[i];
            Log.Info("tracts : " + list.Count + " boites aux lettres suivies");
            return true;
        }

        public static void Update()
        {
            if (!Game.Msc || !Session.Active || !PlayerSync.InGame || Time.realtimeSinceStartup < nextPoll) return;
            nextPoll = Time.realtimeSinceStartup + 0.25f;
            if (!Find()) return;
            if (seen.Length != list.Count) { seen = new bool[list.Count]; }
            for (int i = 0; i < list.Count; i++)
            {
                bool on = list[i] is bool && (bool)list[i];
                if (on && !seen[i] && !applying)
                {
                    Session.SendAll(new NetWriter(Msg.Job).U8(Session.LocalId).Str("@tract").U8(i), true);
                    Served(Session.LocalId);
                    Log.Info("tracts : boite " + i + " servie ici");
                }
                seen[i] = on;
            }
            // Tournee payee ou remise a zero (Delivered a 0) : compte par joueur efface.
            if (served.Count > 0 && Delivered() == 0) { Log.Info("tracts : tournee remise a zero (parts " + Shares() + ")"); served.Clear(); }
        }

        public static void OnMessage(int who, NetReader r)
        {
            int i = r.U8();
            if (!Find() || i >= list.Count) { Log.Warn("tracts : boite " + i + " de #" + who + " introuvable ici"); return; }
            if (list[i] is bool && (bool)list[i]) { seen[i] = true; return; }   // (deja servie ici : une seule fois)
            applying = true;
            try
            {
                list[i] = true; seen[i] = true;
                FsmInt d = data.FsmVariables.FindFsmInt("Delivered"); if (d != null) d.Value += 1;
                Served(who);
                foreach (Object o in Game.FsmsNamed("WaitAd"))
                {
                    var f = o as PlayMakerFSM;
                    if (f == null || f.hideFlags != HideFlags.None) continue;
                    FsmInt bi = f.FsmVariables.FindFsmInt("BoxIndex");
                    if (bi == null || bi.Value != i) continue;
                    FsmBool ap = f.FsmVariables.FindFsmBool("AdvertPlaced"); if (ap != null) ap.Value = true;
                }
                Log.Info("tracts : boite " + i + " servie par #" + who + " (livres ici : " + (d != null ? d.Value.ToString() : "?") + ")");
            }
            finally { applying = false; }
        }

        // Essais : sert la boite 'i' ici comme le jeu ("Close" : case, Delivered, AdvertPlaced).
        public static string TestDeliver(int i)
        {
            if (!Find() || i >= list.Count) return "liste des boites introuvable";
            list[i] = true;
            FsmInt d = data.FsmVariables.FindFsmInt("Delivered"); if (d != null) d.Value += 1;
            return "boite " + i + " servie, livres " + (d != null ? d.Value.ToString() : "?");
        }
        public static string State()
        {
            if (!Find()) return "tracts : ?";
            int n = 0; foreach (object o in list) if (o is bool && (bool)o) n++;
            FsmInt d = data.FsmVariables.FindFsmInt("Delivered"); FsmBool ja = data.FsmVariables.FindFsmBool("JobAccepted");
            return "tracts : " + n + " boites servies, livres " + (d != null ? d.Value.ToString() : "?") + ", boulot pris " + (ja != null ? ja.Value.ToString() : "?")
                   + ", etat " + (data.ActiveStateName ?? "?") + ", parts " + Shares();
        }

        // Boites comptees par joueur (journal, essais) : "#0:1 #1:3", "aucune".
        public static string Shares()
        {
            if (served.Count == 0) return "aucune";
            var sb = new System.Text.StringBuilder();
            foreach (KeyValuePair<int, int> kv in served) sb.Append(sb.Length > 0 ? " " : "").Append('#').Append(kv.Key).Append(':').Append(kv.Value);
            return sb.ToString();
        }

        // Essais (audit MSC 02/11) : boulot pris ici, JOBS/TeimoADs::Data pose en 'Wait player' (comme la pile de tracts emportee) --
        // pour que le @tract d'un autre arrive sur l'etat du vrai jeu (Delivered compte deux fois avant Jobs.OwnVars).
        public static string TestAccept()
        {
            if (!Find()) return "tracts : ?";
            FsmBool ja = data.FsmVariables.FindFsmBool("JobAccepted"); if (ja != null) ja.Value = true;
            if (data.Fsm.GetState("Wait player") == null) return "tracts : pas d'etat 'Wait player'";
            Replay.Depth++;
            try { Game.SetState(data, "Wait player"); } finally { Replay.Depth--; }
            return "tracts : boulot pris, Data en " + data.ActiveStateName;
        }
    }
}
