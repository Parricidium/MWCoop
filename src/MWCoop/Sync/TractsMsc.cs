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
    public static class TractsMsc
    {
        static PlayMakerFSM data;
        static ArrayList list;
        static bool[] seen;
        static float nextPoll, nextFind;
        static bool applying;

        public static void OnLevelLoaded() { data = null; list = null; seen = null; nextFind = 0f; }

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
                    Log.Info("tracts : boite " + i + " servie ici");
                }
                seen[i] = on;
            }
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
            return "tracts : " + n + " boites servies, livres " + (d != null ? d.Value.ToString() : "?") + ", boulot pris " + (ja != null ? ja.Value.ToString() : "?");
        }
    }
}
