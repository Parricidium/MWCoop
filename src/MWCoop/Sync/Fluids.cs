using System.Collections.Generic;
using HutongGames.PlayMaker;
using MWCoop.Net;
using UnityEngine;

namespace MWCoop
{
    // Liquides et usure : essence, huile, liquide de refroidissement et de frein, encrassement,
    // usure des pieces, contenu des bidons et seaux. Ce sont des variables float des automates
    // des pieces (Data : Wear, FuelLevel, Dirt, Coolant...) et de la simulation des vehicules
    // (Simulation/... : Oil, WaterLevel, BrakeFluidF...). Chaque cote envoie ce qui a change chez lui
    // (par lots, une fois par seconde), l'autre recopie la valeur. Deux garde-fous :
    //  - une voiture conduite par un autre : c'est son conducteur qui la consomme, la derive locale
    //    de ses valeurs est ignoree ;
    //  - une valeur que les deux cotes recalculent sans cesse (derivee chaque image) est dite
    //    disputee : seul l'hote l'envoie encore.
    public static class Fluids
    {
        static readonly HashSet<string> Names = new HashSet<string> {
            "Wear", "Condition", "Dirt", "Oil", "OilLevel", "Fluid", "FuelLevel", "Fuel", "WaterLevel", "Water",
            "Coolant", "BrakeFluidF", "BrakeFluidR", "Level" };
        static readonly HashSet<string> SkipFsm = new HashSet<string> {
            "Scale", "Explosion", "Temp", "Assembly", "Flicker", "Jumping", "Paint", "Pressure" };

        class Watch
        {
            public string Key; public PlayMakerFSM Fsm; public FsmFloat Var;
            public float Last, NextSend;
            public int Sends, Recvs; public float WindowStart; public bool Contested;
        }
        static readonly Dictionary<string, Watch> byKey = new Dictionary<string, Watch>();
        static readonly List<Watch> watches = new List<Watch>();
        static readonly HashSet<PlayMakerFSM> seen = new HashSet<PlayMakerFSM>();
        static float nextScan = -1, nextPoll, nextWarn;
        static int sent, applied;

        public static int Count { get { return watches.Count; } }

        public static void OnLevelLoaded()
        {
            byKey.Clear(); watches.Clear(); seen.Clear();
            nextScan = PlayerSync.InGame ? Time.realtimeSinceStartup + 14f : -1;
        }

        static void Scan()
        {
            int before = watches.Count;
            foreach (Object o in Resources.FindObjectsOfTypeAll(typeof(PlayMakerFSM)))
            {
                var f = (PlayMakerFSM)o;
                if (f.hideFlags != HideFlags.None || seen.Contains(f) || SkipFsm.Contains(f.FsmName)) continue;
                Transform t = f.transform;
                string root = t.root.name;
                if (root == "GUI" || root == "PLAYER" || t.name.StartsWith("CapTrigger")) continue;
                seen.Add(f);
                string owner = KeyOf(t);
                if (owner == null) continue;
                foreach (FsmFloat v in f.FsmVariables.FloatVariables)
                {
                    if (!Names.Contains(v.Name)) continue;
                    string key = owner + ":" + f.FsmName + "." + v.Name;
                    if (byKey.ContainsKey(key)) continue;   // homonymes : on garde le premier
                    var w = new Watch { Key = key, Fsm = f, Var = v, Last = v.Value };
                    byKey[key] = w;
                    watches.Add(w);
                }
            }
            if (watches.Count != before) Log.Info("liquides et usure : " + watches.Count + " valeurs suivies");
        }

        // Cle stable : ID de la piece porteuse (lui-meme ou jusqu'a 4 parents) + chemin relatif, sinon
        // chemin complet. Les objets clones sans ID ((Clone), (itemx) sans ID) sont ignores.
        static string KeyOf(Transform t)
        {
            string rel = "";
            Transform p = t;
            for (int i = 0; p != null && i < 5; i++, p = p.parent)
            {
                string id = Props.ItemId(p.gameObject);
                if (id.Length > 0) return id + (rel.Length > 0 ? "/" + rel : "");
                rel = rel.Length > 0 ? p.name + "/" + rel : p.name;
            }
            string path = Recon.Path(t);
            if (path.Contains("(Clone)") || path.Contains("(itemx)")) return null;
            return "p:" + path;
        }

        public static void Update()
        {
            if (!Session.Active || nextScan < 0) return;
            float now = Time.realtimeSinceStartup;
            if (now >= nextScan) { nextScan = now + 20f; Scan(); }
            if (now < nextPoll || Session.RemoteCount == 0) return;
            nextPoll = now + 1f;
            NetWriter w = null;
            int n = 0;
            foreach (Watch x in watches)
            {
                if (x.Fsm == null) continue;
                float v = x.Var.Value;
                if (Mathf.Abs(v - x.Last) <= 0.005f + 0.001f * Mathf.Abs(v)) continue;
                if (VehicleSync.RemotelyDriven(x.Fsm.transform) || (x.Contested && !Session.IsHost) || now < x.NextSend)
                {
                    if (now >= x.NextSend) x.Last = v;   // derive locale ignoree
                    continue;
                }
                x.Last = v;
                x.NextSend = now + 1f;
                Tally(x, true, now);
                if (w == null) w = new NetWriter(Msg.Fluid).U8(Session.LocalId);
                w.Str(x.Key).F32(v);
                if (++n >= 60) { Session.SendAll(w, true); w = null; n = 0; }
            }
            if (w != null) Session.SendAll(w, true);
        }

        // Fenetre de 10 s : 3 envois et 3 receptions de la meme valeur -> elle est disputee.
        static void Tally(Watch x, bool send, float now)
        {
            if (now - x.WindowStart > 10f) { x.WindowStart = now; x.Sends = x.Recvs = 0; }
            if (send) x.Sends++; else x.Recvs++;
            if (!x.Contested && x.Sends >= 3 && x.Recvs >= 3)
            {
                x.Contested = true;
                Log.Info("liquides et usure : " + x.Key + " recalculee des deux cotes, seul l'hote l'envoie");
            }
            if (send && ++sent % 200 == 1) Log.Info("liquides et usure : " + sent + " valeurs envoyees (" + x.Key + " = " + x.Var.Value + ")");
        }

        public static void OnMessage(Peer from, NetReader r)
        {
            int who = r.U8();
            if (Session.IsHost) who = from.Id;
            var relay = Session.IsHost ? new NetWriter(Msg.Fluid).U8(who) : null;
            float now = Time.realtimeSinceStartup;
            while (r.More)
            {
                string key = r.Str();
                float v = r.F32();
                if (relay != null) relay.Str(key).F32(v);
                Watch x;
                if (!byKey.TryGetValue(key, out x) || x.Fsm == null)
                {
                    if (now >= nextWarn) { nextWarn = now + 10f; Log.Warn("liquides et usure : " + key + " introuvable ici"); }
                    continue;
                }
                x.Var.Value = v;
                x.Last = v;
                Tally(x, false, now);
                if (++applied % 200 == 1) Log.Info("liquides et usure : " + applied + " valeurs recues (" + key + " = " + v + ", joueur #" + who + ")");
            }
            if (relay != null) Session.Broadcast(relay, true, who);
        }

        // Essais : ajoute 3 a la premiere valeur Fluid/FuelLevel non nulle la plus proche du joueur.
        public static string TestNearest(string name)
        {
            GameObject p = GameObject.Find("PLAYER");
            Watch best = null;
            float bd = float.MaxValue;
            foreach (Watch x in watches)
            {
                if (x.Fsm == null || x.Var.Name != name || x.Key.StartsWith("p:CORRIS/Simulation")) continue;
                float d = (x.Fsm.transform.position - p.transform.position).sqrMagnitude;
                if (d < bd) { bd = d; best = x; }
            }
            if (best == null) return "aucune valeur " + name + " (" + watches.Count + " suivies)";
            best.Var.Value += 3f;
            return best.Key + " = " + best.Var.Value + " a " + Mathf.Sqrt(bd).ToString("F0") + " m (" + watches.Count + " suivies)";
        }
    }
}
