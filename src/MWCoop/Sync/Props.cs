using System.Collections.Generic;
using HutongGames.PlayMaker;
using MWCoop.Net;
using UnityEngine;

namespace MWCoop
{
    // Pieces libres (non montees) et objets crees en jeu (sacs de courses, articles), suivis par leur
    // ID (ItemId) et deplaces par les joueurs. La main du joueur garde l'objet tenu
    // dans PLAYER/.../1Hand_Assemble/Hand :: PickUp, variable PickedObject.
    //  - celui qui tient une piece envoie sa position 15 fois/s, puis encore apres l'avoir lachee
    //    jusqu'a ce qu'elle s'immobilise (5 s au plus) ;
    //  - chez les autres, la piece devient cinematique et suit, puis retombe sous la physique locale ;
    //  - l'hote recale toutes les 2 s les pieces au repos qui ont bouge chez lui (arrivee d'un invite,
    //    piece poussee...).
    // Les objets uniques du monde n'ont pas d'ID (bidons, cric, palan, hache, seaux, lanterne,
    // boitiers de CD, buches...) : cle "w:<chemin>#<rang>" prise au premier passage, quand tout est
    // encore a sa place de chargement (meme sauvegarde -> meme cle des deux cotes), puis gardee
    // pour cet objet meme s'il change de parent (tenu, lache).
    public static class Props
    {
        class Prop
        {
            public string Id;
            public Rigidbody Body;
            public int RemoteBy = -1;
            public float LastRemote, SettleUntil;
            public Vector3 Pos, Vel, LastSentPos;
            public Quaternion Rot;
            public bool Kinematic, WasKinematic;
        }

        static readonly Dictionary<string, Prop> props = new Dictionary<string, Prop>();
        static readonly Dictionary<Rigidbody, Prop> byBody = new Dictionary<Rigidbody, Prop>();
        static PlayMakerFSM hand;
        static Prop held;
        static readonly List<Prop> settling = new List<Prop>();
        static float nextScan = -1, nextSend, nextHost, lastForced;

        public static void OnLevelLoaded()
        {
            props.Clear(); byBody.Clear(); settling.Clear(); worldKeys.Clear(); worldLogged = false;
            hand = null; held = null;
            nextScan = PlayerSync.InGame ? Time.realtimeSinceStartup + 10f : -1;
        }

        // Identifiant d'un objet : la variable texte 'ID' d'un de ses automates (Data pour les pieces :
        // VIN514B1 ; Use pour les articles : beercase1, shoppingbag1). Fixee a la creation de l'objet
        // (nom a compteur sauvegarde), elle reste la meme quand le jeu le renomme ensuite.
        public static string ItemId(GameObject go)
        {
            foreach (PlayMakerFSM f in go.GetComponents<PlayMakerFSM>())
            {
                FsmString s = f.FsmVariables.FindFsmString("ID");
                if (s != null && s.Value.Length > 0) return s.Value;
            }
            return "";
        }

        static bool censusDone, worldLogged;
        static readonly Dictionary<Rigidbody, string> worldKeys = new Dictionary<Rigidbody, string>();
        static readonly HashSet<string> WorldRoots = new HashSet<string> { "EQUIPMENTS", "Systems", "YARD", "COTTAGE", "CABIN", "MISC" };

        static string WorldKey(Rigidbody rb)
        {
            string k;
            if (worldKeys.TryGetValue(rb, out k)) return k;
            Transform t = rb.transform;
            if (!WorldRoots.Contains(t.root.name) || rb.GetComponent("CarDynamics") != null) return null;
            k = "w:" + Recon.Path(t) + "#" + t.GetSiblingIndex();
            worldKeys[rb] = k;
            return k;
        }

        static void Scan()
        {
            // Le Rigidbody d'une piece disparait quand elle est montee et revient au demontage :
            // la table est refaite a chaque passage, seuls les objets physiques actifs y sont.
            byBody.Clear();
            foreach (Prop p in props.Values) p.Body = null;
            bool census = Config.GetInt("Test", "JournalSansId", 0) != 0 && !censusDone;
            var noId = census ? new Dictionary<string, int>() : null;
            foreach (Rigidbody rb in Object.FindObjectsOfType<Rigidbody>())
            {
                if (rb.transform.root.name == "PLAYER" && rb.transform.parent.name != "ItemPivot") continue;
                string id = ItemId(rb.gameObject);
                if (id.Length == 0) id = WorldKey(rb) ?? "";
                if (id.Length == 0)
                {
                    if (census && !rb.isKinematic && rb.GetComponent("CarDynamics") == null)
                    {
                        string k = rb.transform.root == rb.transform ? rb.name : rb.transform.root.name + "/.../" + rb.name;
                        int c; noId.TryGetValue(k, out c); noId[k] = c + 1;
                    }
                    continue;
                }
                Prop p;
                if (!props.TryGetValue(id, out p)) { p = new Prop { Id = id }; props[id] = p; }
                p.Body = rb;
                byBody[rb] = p;
            }
            if (census)
            {
                censusDone = true;
                var sb = new System.Text.StringBuilder("objets physiques sans ID (non synchronises) :");
                foreach (KeyValuePair<string, int> kv in noId) sb.Append("\n  ").Append(kv.Value).Append(" x ").Append(kv.Key);
                Log.Info(sb.ToString());
            }
            if (!worldLogged && worldKeys.Count > 0)
            {
                worldLogged = true;
                Log.Info("objets : " + worldKeys.Count + " objets du monde sans ID suivis par leur chemin");
            }
            if (hand == null)
            {
                GameObject h = GameObject.Find("PLAYER/Pivot/AnimPivot/Camera/FPSCamera/1Hand_Assemble/Hand");
                if (h != null) hand = Game.FsmOn(h, "PickUp");
            }
        }

        public static void Update()
        {
            if (!Session.Active || !PlayerSync.InGame || nextScan < 0) return;
            float now = Time.realtimeSinceStartup;
            if (now >= nextScan) { nextScan = now + 10f; Scan(); }

            // Piece tenue localement
            Prop h = null;
            if (hand != null)
            {
                GameObject go = hand.FsmVariables.GetFsmGameObject("PickedObject").Value;
                Rigidbody rb = go != null ? go.GetComponent<Rigidbody>() : null;
                if (rb != null && !byBody.TryGetValue(rb, out h) && Time.realtimeSinceStartup - lastForced > 1f)
                {
                    lastForced = Time.realtimeSinceStartup;
                    Scan();
                    byBody.TryGetValue(rb, out h);
                }
            }
            if (h != held)
            {
                if (held != null) { held.SettleUntil = now + 5f; if (!settling.Contains(held)) settling.Add(held); }
                if (h != null) { settling.Remove(h); h.RemoteBy = -1; SetKinematic(h, false); Log.Info("piece prise : " + h.Id); }
                held = h;
            }

            if (now >= nextSend)
            {
                nextSend = now + 1f / 15f;
                if (held != null && held.Body != null) Send(held, 1);
                for (int i = settling.Count - 1; i >= 0; i--)
                {
                    Prop p = settling[i];
                    bool done = p.Body == null || now > p.SettleUntil || (now > p.SettleUntil - 4.5f && p.Body.IsSleeping());
                    Send(p, done ? 0 : 2);
                    if (done) settling.RemoveAt(i);
                }
            }

            if (Session.IsHost && now >= nextHost && Session.RemoteCount > 0)
            {
                nextHost = now + 2f;
                foreach (Prop p in props.Values)
                {
                    if (p.Body == null || p == held || p.RemoteBy >= 0 || settling.Contains(p)) continue;
                    if ((p.Body.position - p.LastSentPos).sqrMagnitude < 0.04f) continue;
                    Send(p, 0);
                }
            }

            foreach (Prop p in props.Values)
            {
                if (p.RemoteBy < 0 || p.Body == null) continue;
                if (now - p.LastRemote > 1.5f) { p.RemoteBy = -1; SetKinematic(p, false); continue; }
                Follow(p);
            }
        }

        static void SetKinematic(Prop p, bool on)
        {
            if (p.Body == null || p.Kinematic == on) return;
            p.Kinematic = on;
            if (on) { p.WasKinematic = p.Body.isKinematic; p.Body.isKinematic = true; }
            else { p.Body.isKinematic = p.WasKinematic; if (!p.Body.isKinematic) p.Body.velocity = p.Vel; }
        }

        static void Follow(Prop p)
        {
            Transform t = p.Body.transform;
            float k = 1f - Mathf.Exp(-20f * Time.deltaTime);
            if ((p.Pos - t.position).sqrMagnitude > 9f) { t.position = p.Pos; t.rotation = p.Rot; return; }
            t.position = Vector3.Lerp(t.position, p.Pos, k);
            t.rotation = Quaternion.Slerp(t.rotation, p.Rot, k);
        }

        static void Send(Prop p, int state)
        {
            if (p.Body == null) return;
            p.LastSentPos = p.Body.position;
            Session.SendAll(new NetWriter(Msg.Prop).U8(Session.LocalId).Str(p.Id).U8(state)
                .Vec(p.Body.position).Quat(p.Body.rotation).Vec(p.Body.velocity), false);
        }

        public static void OnMessage(Peer from, NetReader r)
        {
            int who = r.U8();
            if (Session.IsHost) who = from.Id;
            string id = r.Str();
            int state = r.U8();
            Vector3 pos = r.Vec();
            Quaternion rot = r.Quat();
            Vector3 vel = r.Vec();
            if (Session.IsHost)
                Session.Broadcast(new NetWriter(Msg.Prop).U8(who).Str(id).U8(state).Vec(pos).Quat(rot).Vec(vel), false, who);
            Prop p;
            if ((!props.TryGetValue(id, out p) || p.Body == null) && Time.realtimeSinceStartup - lastForced > 1f)
            {
                lastForced = Time.realtimeSinceStartup;   // objet tout neuf (achat...) : nouveau passage
                Scan();
                props.TryGetValue(id, out p);
            }
            if (p == null || p.Body == null || p == held) return;
            p.Pos = pos; p.Rot = rot; p.Vel = vel;
            if (state != 0)
            {
                if (p.RemoteBy != who) Log.Info("piece " + id + " deplacee par #" + who);
                p.RemoteBy = who;
                p.LastRemote = Time.realtimeSinceStartup;
                SetKinematic(p, true);
                return;
            }
            // Au repos : fin du deplacement distant, ou recalage par l'hote.
            p.RemoteBy = -1;
            SetKinematic(p, false);
            Transform t = p.Body.transform;
            if ((t.position - pos).sqrMagnitude > 0.09f || Quaternion.Angle(t.rotation, rot) > 15f)
            {
                t.position = pos; t.rotation = rot;
                p.Body.velocity = vel;
            }
        }

        // Essais : fait comme si le joueur local tenait la piece 'id' et la promenait devant lui.
        public static string TestCarry(string id, float t)
        {
            Prop p;
            if (!props.TryGetValue(id, out p) || p.Body == null) return "piece " + id + " absente";
            if (!settling.Contains(p)) { settling.Add(p); }
            p.SettleUntil = Time.realtimeSinceStartup + 5f;
            p.Body.isKinematic = true;
            p.Body.position = p.Body.position + new Vector3(Mathf.Cos(t) * 0.05f, 0.02f, Mathf.Sin(t) * 0.05f);
            p.Body.isKinematic = false;
            return p.Id + " en " + p.Body.position.ToString("F2");
        }

        // Essais : objets physiques a moins de 'radius' m de 'pos' (nom et position).
        public static string Near(Vector3 pos, float radius)
        {
            var sb = new System.Text.StringBuilder();
            foreach (Rigidbody rb in Object.FindObjectsOfType<Rigidbody>())
                if ((rb.position - pos).sqrMagnitude < radius * radius && rb.transform.root.name != "PLAYER")
                    sb.Append(rb.name).Append('[').Append(ItemId(rb.gameObject)).Append(']').Append(rb.position.ToString("F2")).Append("  ");
            return sb.ToString();
        }

        // Essais : ID du plus proche objet suivi dont l'ID commence par 'prefix'.
        public static string NearestId(string prefix, Vector3 pos)
        {
            Scan();
            string best = null;
            float bd = float.MaxValue;
            foreach (Prop p in props.Values)
            {
                if (p.Body == null || !p.Id.StartsWith(prefix)) continue;
                float d = (p.Body.position - pos).sqrMagnitude;
                if (d < bd) { bd = d; best = p.Id; }
            }
            return best;
        }

        // Essais : premiere cle suivie qui contient 'part' (objets du monde : "~gasoline").
        public static string FindKey(string part)
        {
            foreach (string k in props.Keys) if (k.Contains(part)) return k;
            return part;
        }

        public static bool Holding { get { return held != null; } }

        public static string Where(string id)
        {
            Prop p;
            return props.TryGetValue(id, out p) && p.Body != null ? p.Body.position.ToString("F2") : "?";
        }
    }
}
