using System.Collections.Generic;
using HutongGames.PlayMaker;
using MWCoop.Net;
using UnityEngine;

namespace MWCoop
{
    // Ouverture des colis (retour d'un joueur, 09/10 : « quand je deballe le colis, mon ami doit le deballer aussi pour que
    // les pieces apparaissent ») : le colis (Post Package(Clone), cree a la commande chez tous, meme liste -- Calls) s'ouvre
    // par son automate 'Use' (clic : "1".."4" le couvercle, puis "Get list" fait apparaitre les pieces une a une, "Close box",
    // "Remove order"). Seul celui qui cliquait l'ouvrait. Ici : quand 'Use' quitte "Wait button" pour "1" chez un joueur,
    // le meme colis (cle : sa commande, ThisOrder -- comme Props) s'ouvre chez les autres, pieces comprises.
    public static class Packages
    {
        class Box { public string Key; public PlayMakerFSM Use; public string Last; }
        static readonly List<Box> boxes = new List<Box>();
        static float nextScan = -1, nextLook;
        static readonly HashSet<string> remoteOpened = new HashSet<string>();
        static readonly Dictionary<string, float> pending = new Dictionary<string, float>();   // ouvert ailleurs, pas encore ici (1 min)

        public static void OnLevelLoaded() { boxes.Clear(); remoteOpened.Clear(); pending.Clear(); nextScan = PlayerSync.InGame ? Time.realtimeSinceStartup + 10f : -1; }

        static string KeyOf(PlayMakerFSM f)
        {
            FsmGameObject o = f.FsmVariables.FindFsmGameObject("ThisOrder");
            return o != null && o.Value != null ? o.Value.name : null;
        }

        static void Scan()
        {
            boxes.RemoveAll(b => b.Use == null);
            foreach (Object o in Game.AllFsms())
            {
                PlayMakerFSM f = o as PlayMakerFSM;
                if (f == null || f.FsmName != "Use" || !f.gameObject.name.StartsWith("Post Package") || f.FsmVariables.FindFsmGameObject("ThisOrder") == null) continue;
                if (boxes.Exists(b => b.Use == f)) continue;
                string key = KeyOf(f);
                if (key == null) continue;   // (gabarit cache, ou commande pas encore liee)
                boxes.Add(new Box { Key = key, Use = f, Last = f.ActiveStateName });
            }
        }

        public static void Update()
        {
            float now = Time.realtimeSinceStartup;
            if (nextScan > 0 && now >= nextScan) { nextScan = now + (pending.Count > 0 ? 1f : 5f); Scan(); }
            if (pending.Count > 0)
                foreach (string k in new List<string>(pending.Keys))
                    if (now - pending[k] > 60f) pending.Remove(k);
                    else if (Open(k, -1, false)) pending.Remove(k);
            if (now < nextLook) return;
            nextLook = now + 0.1f;
            foreach (Box b in boxes)
            {
                if (b.Use == null) continue;
                string st = b.Use.ActiveStateName;
                if (st == b.Last) continue;
                string was = b.Last;
                b.Last = st;
                if (st != "1" || (was != "Wait button" && was != "Wait player")) continue;
                if (remoteOpened.Remove(b.Key)) continue;   // (ouverture recue : pas renvoyee)
                Log.Info("colis : " + b.Key + " ouvert ici");
                if (Session.Active && Session.RemoteCount > 0)
                    Session.SendAll(new NetWriter(Msg.Job).U8(Session.LocalId).Str("@colis").Str(b.Key), true);
            }
        }

        public static void OnRemote(int who, string key)
        {
            Scan();
            if (!Open(key, who, true)) { pending[key] = Time.realtimeSinceStartup; Log.Info("colis : " + key + " ouvert par #" + who + ", pas encore ici (ouvert des qu'il apparait)"); }
        }

        static bool Open(string key, int who, bool log)
        {
            foreach (Box b in boxes)
            {
                if (b.Key != key || b.Use == null) continue;
                string st = b.Use.ActiveStateName;
                if (st != "Wait player" && st != "Wait button") { if (log) Log.Info("colis : " + key + " ouvert par #" + who + ", deja en cours ici (" + st + ")"); return true; }
                remoteOpened.Add(key);
                b.Last = "Wait button";
                Game.SetState(b.Use, "1");
                Log.Info("colis : " + key + " ouvert ailleurs, ouvert ici aussi");
                return true;
            }
            return false;
        }

        // Essais : ouvre le premier colis (comme un clic).
        public static string TestOpen()
        {
            Scan();
            foreach (Box b in boxes) if (b.Use != null) { Game.SetState(b.Use, "1"); return "colis " + b.Key + " ouvert"; }
            return "aucun colis (" + boxes.Count + ")";
        }
        public static string State()
        {
            var sb = new System.Text.StringBuilder("colis :");
            foreach (Box b in boxes) sb.Append(' ').Append(b.Key).Append(" [").Append(b.Use != null ? b.Use.ActiveStateName : "detruit").Append(']');
            return sb.ToString();
        }
    }
}
