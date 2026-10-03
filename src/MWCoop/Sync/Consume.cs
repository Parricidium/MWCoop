using System.Collections.Generic;
using HutongGames.PlayMaker;
using MWCoop.Net;
using UnityEngine;

namespace MWCoop
{
    // Objets consommes ou jetes : nourriture mangee, boisson bue, article jete a la poubelle.
    // Leur automate Use finit dans l'etat "Destroy" (manger : Eat -> Destroy ; poubelle : GARBAGE).
    // Une action ajoutee en tete de cet etat previent les autres, qui menent l'objet de meme ID
    // au meme etat : il disparait chez tout le monde. Celui qui mange porte la main a la bouche.
    public static class Consume
    {
        static readonly Dictionary<string, PlayMakerFSM> byId = new Dictionary<string, PlayMakerFSM>();
        static readonly HashSet<PlayMakerFSM> hooked = new HashSet<PlayMakerFSM>();
        static float nextScan = -1, lastRescan, nextWarn;
        static bool applying;
        static readonly List<string> consumed = new List<string>();   // hote : consommes depuis le chargement
        static readonly List<KeyValuePair<float, Peer>> snapshots = new List<KeyValuePair<float, Peer>>();
        public static float EatUntil;

        class Hook : FsmStateAction
        {
            public string Id;
            public override void OnEnter()
            {
                if (!applying) OnLocal(Id, Fsm.PreviousActiveState != null ? Fsm.PreviousActiveState.Name : "");
                Finish();
            }
        }

        public static void OnLevelLoaded()
        {
            byId.Clear(); hooked.Clear(); consumed.Clear(); snapshots.Clear();
            nextScan = PlayerSync.InGame ? Time.realtimeSinceStartup + 11f : -1;
        }

        public static void Update()
        {
            if (!Session.Active || nextScan < 0) return;
            float now = Time.realtimeSinceStartup;
            for (int i = snapshots.Count - 1; i >= 0; i--)
            {
                if (now < snapshots[i].Key) continue;
                Peer p = snapshots[i].Value;
                snapshots.RemoveAt(i);
                if (!p.Accepted || !Session.T.Peers.Contains(p)) continue;
                foreach (string id in consumed) Session.T.SendReliable(p, new NetWriter(Msg.Consume).U8(Session.LocalId).Str(id).U8(1).ToArray());
                if (consumed.Count > 0) Log.Info("consommables : " + consumed.Count + " objets deja consommes envoyes a " + p);
            }
            if (now < nextScan) return;
            nextScan = now + 10f;
            Scan();
        }

        // Hote : invite arrive en jeu -> 20 s plus tard, ce qui a ete mange ou jete pendant son chargement.
        public static void ScheduleSnapshot(Peer p) { snapshots.Add(new KeyValuePair<float, Peer>(Time.realtimeSinceStartup + 20f, p)); }

        static void Scan()
        {
            hooked.RemoveWhere(x => x == null);
            var gone = new List<string>();
            foreach (KeyValuePair<string, PlayMakerFSM> kv in byId) if (kv.Value == null) gone.Add(kv.Key);
            foreach (string g in gone) byId.Remove(g);
            int n = 0;
            foreach (Object o in Resources.FindObjectsOfTypeAll(typeof(PlayMakerFSM)))
            {
                var f = (PlayMakerFSM)o;
                if (f.hideFlags != HideFlags.None || f.FsmName != "Use" || hooked.Contains(f)) continue;
                FsmString idv = f.FsmVariables.FindFsmString("ID");
                if (idv == null || idv.Value.Length == 0) continue;
                FsmState s = f.Fsm.GetState("Destroy");
                if (s == null) continue;
                try
                {
                    var list = new List<FsmStateAction>(s.Actions);
                    list.Insert(0, new Hook { Id = idv.Value });
                    s.Actions = list.ToArray();
                }
                catch { continue; }
                hooked.Add(f);
                byId[idv.Value] = f;
                n++;
            }
            if (n > 0) Log.Info("consommables : " + n + " objets de plus suivis (" + byId.Count + ")");
        }

        static void OnLocal(string id, string from)
        {
            // Mange ou bu (pas jete) : la main va a la bouche.
            if (from.Contains("Eat") || from.Contains("Drink") || from.Contains("drink") || from == "Play anim")
                EatUntil = Time.realtimeSinceStartup + 2f;
            if (Session.IsHost) consumed.Add(id);
            Log.Info("consommables : " + id + " consomme ou jete ici (" + from + ")");
            Session.SendAll(new NetWriter(Msg.Consume).U8(Session.LocalId).Str(id).U8(0), true);
        }

        public static void OnMessage(Peer from, NetReader r)
        {
            int who = r.U8();
            if (Session.IsHost) who = from.Id;
            string id = r.Str();
            bool snapshot = r.U8() == 1;
            if (Session.IsHost) { consumed.Add(id); Session.Broadcast(new NetWriter(Msg.Consume).U8(who).Str(id).U8(0), true, who); }
            PlayMakerFSM f;
            float now = Time.realtimeSinceStartup;
            if ((!byId.TryGetValue(id, out f) || f == null) && now - lastRescan > 1f)
            {
                lastRescan = now;   // objet tout neuf (achete il y a peu) : nouveau passage
                Scan();
                byId.TryGetValue(id, out f);
            }
            if (f == null)
            {
                if (!snapshot && now >= nextWarn) { nextWarn = now + 10f; Log.Warn("consommables : " + id + " introuvable ici"); }
                return;
            }
            applying = true;
            try { Game.SetState(f, "Destroy"); }
            finally { applying = false; }
            byId.Remove(id);
            Log.Info("consommables : " + id + " consomme par le joueur #" + who);
        }

        // Essais : mange l'objet suivi le plus proche du joueur (comme si on avait clique dessus).
        public static string TestNearest()
        {
            Scan();
            Vector3 me = GameObject.Find("PLAYER").transform.position;
            PlayMakerFSM best = null;
            string bestId = null;
            foreach (KeyValuePair<string, PlayMakerFSM> kv in byId)
            {
                if (kv.Value == null || kv.Value.Fsm.GetState("Eat") == null && kv.Value.Fsm.GetState("Eat 2") == null) continue;
                if (best == null || (kv.Value.transform.position - me).sqrMagnitude < (best.transform.position - me).sqrMagnitude) { best = kv.Value; bestId = kv.Key; }
            }
            if (best == null) return "rien a manger (" + byId.Count + " suivis)";
            Game.SetState(best, best.Fsm.GetState("Eat") != null ? "Eat" : "Eat 2");
            return bestId + " a " + (best.transform.position - me).magnitude.ToString("F0") + " m";
        }
    }
}
