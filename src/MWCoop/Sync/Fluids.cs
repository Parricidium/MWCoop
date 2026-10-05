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
    // Fosses septiques (boulot de la GIFU) : le niveau de chaque fosse (JOBS/HouseShit*/.../ShitLevelTrigger
    // 'Level', ShitLevel) et le contenu de la citerne (GIFU/ShitTank 'Waste', Waste) ne sont tenus que par
    // l'autorite de la GIFU (son conducteur, celui qui a laisse son moteur tourner, sinon l'hote : c'est chez
    // lui que la pompe tourne au vrai regime, tuyau dans la fosse -- voir Jobs, @tuyau). Lui seul les envoie ;
    // ailleurs la derive locale (pompage refait sur la copie avec le regime recu, montee lente du niveau) n'est
    // jamais envoyee et la valeur recue l'ecrase. Jamais disputees : une seule source.
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
            public bool Septic;   // fosse ou citerne : envoyee par l'autorite de la GIFU seulement
        }

        static Rigidbody gifu;   // corps de la GIFU (racine de la citerne)

        static bool SepticVar(PlayMakerFSM f, FsmFloat v)
        {
            return v.Name == "Waste" && f.FsmName == "Waste" && f.gameObject.name == "ShitTank"
                   || v.Name == "ShitLevel" && f.FsmName == "Level" && f.gameObject.name == "ShitLevelTrigger";
        }

        // Qui tient les fosses et la citerne : l'autorite de la GIFU (l'hote tant qu'elle n'est pas trouvee).
        static int SepticAuthority() { return gifu != null ? VehicleSync.Authority(gifu) : 0; }
        static readonly Dictionary<string, Watch> byKey = new Dictionary<string, Watch>();
        static readonly List<Watch> watches = new List<Watch>();
        static readonly HashSet<PlayMakerFSM> seen = new HashSet<PlayMakerFSM>();
        static readonly HashSet<string> ambiguous = new HashSet<string>();
        static float nextScan = -1, nextPoll, nextWarn;
        static readonly List<KeyValuePair<float, Peer>> snapshots = new List<KeyValuePair<float, Peer>>();

        // Hote : invite arrive en jeu -> 20 s plus tard (ses automates sont trouves), toutes les valeurs.
        public static void ScheduleSnapshot(Peer p) { snapshots.Add(new KeyValuePair<float, Peer>(Time.realtimeSinceStartup + 20f, p)); }

        static void SendSnapshots(float now)
        {
            for (int i = snapshots.Count - 1; i >= 0; i--)
            {
                if (now < snapshots[i].Key) continue;
                Peer p = snapshots[i].Value;
                snapshots.RemoveAt(i);
                if (!p.Accepted || !Session.T.Peers.Contains(p)) continue;
                NetWriter w = null;
                int n = 0;
                foreach (Watch x in watches)
                {
                    if (x.Fsm == null) continue;
                    if (w != null && w.Length + 6 + System.Text.Encoding.UTF8.GetByteCount(x.Key) > 1000) { Session.T.SendReliable(p, w.ToArray()); w = null; }
                    if (w == null) w = new NetWriter(Msg.Fluid).U8(Session.LocalId);
                    w.Str(x.Key).F32(x.Var.Value);
                    n++;
                }
                if (w != null) Session.T.SendReliable(p, w.ToArray());
                Log.Info("liquides et usure : instantane de " + n + " valeurs envoye a " + p);
            }
        }
        static int sent, applied;

        public static int Count { get { return watches.Count; } }

        public static void OnLevelLoaded()
        {
            byKey.Clear(); watches.Clear(); seen.Clear(); ambiguous.Clear(); snapshots.Clear();
            gifu = null;
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
                    bool septic = SepticVar(f, v);
                    if (!Names.Contains(v.Name) && !septic) continue;
                    if (septic && f.FsmName == "Waste") gifu = f.transform.root.GetComponent<Rigidbody>();
                    string key = owner + ":" + f.FsmName + "." + v.Name;
                    // Homonymes : l'ordre de decouverte differe d'une machine a l'autre, la cle est abandonnee.
                    if (ambiguous.Contains(key)) continue;
                    Watch old;
                    if (byKey.TryGetValue(key, out old))
                    {
                        if (old.Fsm == f) continue;
                        byKey.Remove(key); watches.Remove(old); ambiguous.Add(key);
                        continue;
                    }
                    var w = new Watch { Key = key, Fsm = f, Var = v, Last = v.Value, Septic = septic };
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
            if (now >= nextScan) { nextScan = now + 20f; watches.RemoveAll(x => x.Fsm == null); Scan(); }
            if (snapshots.Count > 0 && Session.IsHost) SendSnapshots(now);
            if (now < nextPoll) return;
            nextPoll = now + 1f;
            bool alone = Session.RemoteCount == 0;
            bool septicHere = SepticAuthority() == Session.LocalId;
            NetWriter w = null;
            foreach (Watch x in watches)
            {
                if (x.Fsm == null) continue;
                float v = x.Var.Value;
                if (x.Contested && now - x.WindowStart > 30f) x.Contested = false;   // plus de conflit depuis 30 s
                if (Mathf.Abs(v - x.Last) <= 0.005f + 0.001f * Mathf.Abs(v)) continue;
                if (alone) { x.Last = v; continue; }   // personne a prevenir : l'arrivant recevra l'instantane
                if (x.Septic)
                {
                    // Fosse, citerne : pas a nous, la derive d'ici ne part jamais (la valeur recue l'ecrase).
                    if (!septicHere) { x.Last = v; continue; }
                    if (now < x.NextSend) continue;
                }
                else if (VehicleSync.RemotelyDriven(x.Fsm.transform) || (x.Contested && !Session.IsHost) || now < x.NextSend)
                {
                    if (now >= x.NextSend) x.Last = v;   // derive locale ignoree
                    continue;
                }
                x.Last = v;
                x.NextSend = now + 1f;
                Tally(x, true, now);
                // Lots limites en octets (un message fiable tient dans un paquet de 1150 octets).
                if (w != null && w.Length + 6 + System.Text.Encoding.UTF8.GetByteCount(x.Key) > 1000) { Session.SendAll(w, true); w = null; }
                if (w == null) w = new NetWriter(Msg.Fluid).U8(Session.LocalId);
                w.Str(x.Key).F32(v);
            }
            if (w != null) Session.SendAll(w, true);
        }

        // Fenetre de 10 s : 3 envois et 3 receptions de la meme valeur -> elle est disputee.
        static void Tally(Watch x, bool send, float now)
        {
            if (now - x.WindowStart > 10f) { x.WindowStart = now; x.Sends = x.Recvs = 0; }
            if (send) x.Sends++; else x.Recvs++;
            if (!x.Septic && !x.Contested && x.Sends >= 3 && x.Recvs >= 3)
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

        // Essais (Jobs, [Test] Autotest=fosse) : fosses (par cle) et citerne suivies.
        static List<Watch> SepticWatches(string var)
        {
            var l = new List<Watch>();
            foreach (Watch x in watches) if (x.Septic && x.Fsm != null && x.Var.Name == var) l.Add(x);
            l.Sort((a, b) => string.CompareOrdinal(a.Key, b.Key));
            return l;
        }

        public static string SepticState()
        {
            var sb = new System.Text.StringBuilder("fosses (autorite #" + SepticAuthority() + ") :");
            foreach (Watch x in SepticWatches("ShitLevel"))
            {
                string[] seg = x.Key.Split('/');
                sb.Append(' ').Append(seg.Length > 1 ? seg[1] : x.Key).Append('=').Append(x.Var.Value.ToString("F2"));
            }
            List<Watch> tank = SepticWatches("Waste");
            sb.Append(" ; citerne ").Append(tank.Count > 0 ? tank[0].Var.Value.ToString("F0") + " L" : "?");
            if (tank.Count > 0)
            {
                PlayMakerFSM pump = Game.FsmOn(tank[0].Fsm.gameObject, "Pump");
                FsmBool hose = pump != null ? pump.FsmVariables.FindFsmBool("HoseInShit") : null;
                sb.Append(", HoseInShit=").Append(hose != null ? hose.Value.ToString() : "?").Append(" (pompe ").Append(pump != null ? pump.ActiveStateName : "?").Append(')');
            }
            return sb.ToString();
        }

        // Essais : change ici le niveau de la fosse 'well' (rang par cle) et la citerne, comme la pompe.
        public static string TestSeptic(int well, float dLevel, float dWaste)
        {
            List<Watch> wl = SepticWatches("ShitLevel"), tank = SepticWatches("Waste");
            string r = "";
            if (well < wl.Count && dLevel != 0f) { wl[well].Var.Value += dLevel; r += wl[well].Key + " = " + wl[well].Var.Value.ToString("F2") + " "; }
            if (tank.Count > 0 && dWaste != 0f) { tank[0].Var.Value += dWaste; r += "citerne = " + tank[0].Var.Value.ToString("F0") + " L "; }
            return (r.Length > 0 ? r : "rien (" + wl.Count + " fosses, " + tank.Count + " citerne) ") + "(autorite #" + SepticAuthority() + (SepticAuthority() == Session.LocalId ? " : envoye" : " : pas envoye") + ")";
        }
    }
}
