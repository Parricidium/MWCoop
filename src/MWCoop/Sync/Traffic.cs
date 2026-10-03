using System.Collections.Generic;
using MWCoop.Net;
using UnityEngine;

namespace MWCoop
{
    // Circulation et passants dictes par l'hote (comme la meteo) : voitures de TRAFFIC (routes,
    // chemins, train) et de NPC_CARS, marcheurs de HUMANS/Randomizer/Walkers.
    //  - meme liste des deux cotes : chemin + rang parmi les homonymes (LAMORE x2, VICTRO x3...) ;
    //  - l'hote envoie 5 fois/s, par lots, actif, position, rotation, vitesse de chacun ;
    //  - chez l'invite : leur logique (MobileCarController, automates de conduite et de marche,
    //    automates des conteneurs qui les font apparaitre) est coupee, les voitures deviennent
    //    cinematiques et suivent ; les marcheurs jouent fat_walk / fat_standing selon leur vitesse.
    public static class Traffic
    {
        class Ent
        {
            public string Key;
            public Transform T;
            public Rigidbody Body;
            public Animation Anim;           // marcheurs : squelette anime
            public bool Walker;
            public Vector3 Pos, Vel;
            public Quaternion Rot;
            public float LastRecv;
            public bool Muted;
        }

        static readonly string[] Containers = { "TRAFFIC/VehiclesHighway", "TRAFFIC/VehiclesDirtRoad", "TRAFFIC/SpawnEast", "TRAFFIC/SpawnWest", "NPC_CARS" };
        const string WalkersPath = "HUMANS/Randomizer/Walkers";
        static readonly List<Ent> ents = new List<Ent>();
        static readonly List<PlayMakerFSM> mutedSpawners = new List<PlayMakerFSM>();
        static bool built;
        static float buildAt = -1, nextSend, nextLog, nextSample;

        public static void OnLevelLoaded()
        {
            ents.Clear(); mutedSpawners.Clear();
            built = false;
            buildAt = PlayerSync.InGame ? Time.realtimeSinceStartup + 6f : -1;
        }

        static void Build()
        {
            built = true;
            var keys = new Dictionary<string, int>();
            foreach (string c in Containers)
            {
                GameObject root = Game.FindAny(c);
                if (root == null) continue;
                // Vehicules : objets a CarDynamics (ou le train : Rigidbody direct) sous le conteneur.
                foreach (Rigidbody rb in root.GetComponentsInChildren<Rigidbody>(true))
                {
                    if (rb.GetComponent("CarDynamics") == null && rb.name != "TRAIN") continue;
                    Add(keys, rb.transform, rb, false);
                }
            }
            GameObject walkers = Game.FindAny(WalkersPath);
            if (walkers != null)
                foreach (Transform w in walkers.transform)
                {
                    var e = Add(keys, w, null, true);
                    Transform sk = w.Find("Pivot/Char/skeleton");
                    if (sk != null) e.Anim = sk.GetComponent<Animation>();
                }
            Log.Info("trafic : " + ents.Count + " vehicules et passants suivis");
        }

        static Ent Add(Dictionary<string, int> keys, Transform t, Rigidbody rb, bool walker)
        {
            string path = Recon.Path(t);
            int k;
            keys.TryGetValue(path, out k);
            keys[path] = k + 1;
            var e = new Ent { Key = path + "#" + k, T = t, Body = rb, Walker = walker, Pos = t.position, Rot = t.rotation };
            ents.Add(e);
            return e;
        }

        public static void Update()
        {
            if (!Session.Active || !PlayerSync.InGame) return;
            if (!built) { if (buildAt > 0 && Time.realtimeSinceStartup >= buildAt) Build(); return; }
            float now = Time.realtimeSinceStartup;
            if (Config.GetInt("Test", "JournalTrafic", 0) != 0 && now >= nextSample)
            {
                // Essais : meme instant (horloge du PC) des deux cotes, toutes les 10 s.
                nextSample = now + 10f;
                var sb = new System.Text.StringBuilder("trafic echantillon " + System.DateTime.Now.ToString("ss.f") + " :");
                int k = 0;
                foreach (Ent e in ents)
                    if (e.T != null && e.T.gameObject.activeInHierarchy && k++ < 4)
                        sb.Append(' ').Append(e.T.name).Append(e.T.position.ToString("F1"));
                Log.Info(sb.ToString());
            }
            if (Session.IsHost)
            {
                if (now < nextSend || Session.RemoteCount == 0) return;
                nextSend = now + 0.2f;
                SendAll();
                return;
            }
            int moving = 0;
            foreach (Ent e in ents)
            {
                if (e.T == null || e.LastRecv <= 0) continue;
                Follow(e);
                if (e.Vel.sqrMagnitude > 0.5f) moving++;
            }
            if (now >= nextLog)
            {
                nextLog = now + 15f;
                int active = 0;
                foreach (Ent e in ents) if (e.T != null && e.T.gameObject.activeInHierarchy) active++;
                Log.Info("trafic : " + active + " actifs, " + moving + " en mouvement (selon l'hote)");
            }
        }

        static void SendAll()
        {
            // Lots de 24 entites par message (u16 index, actif, position, rotation compacte, vitesse).
            for (int start = 0; start < ents.Count; start += 24)
            {
                int n = Mathf.Min(24, ents.Count - start);
                var w = new NetWriter(Msg.Traffic).U16(start).U8(n);
                for (int i = start; i < start + n; i++)
                {
                    Ent e = ents[i];
                    bool on = e.T != null && e.T.gameObject.activeInHierarchy;
                    w.Bool(on);
                    if (!on) continue;
                    Vector3 v = e.Body != null && !e.Body.isKinematic ? e.Body.velocity : (e.T.position - e.Pos) / 0.2f;
                    e.Pos = e.T.position;
                    w.Vec(e.T.position).Quat(e.T.rotation).Vec(v);
                }
                Session.Broadcast(w, false);
            }
        }

        public static void OnMessage(Peer from, NetReader r)
        {
            if (Session.IsHost || !built) return;
            int start = r.U16(), n = r.U8();
            float now = Time.realtimeSinceStartup;
            for (int i = start; i < start + n; i++)
            {
                bool on = r.Bool();
                Vector3 p = Vector3.zero, v = Vector3.zero;
                Quaternion q = Quaternion.identity;
                if (on) { p = r.Vec(); q = r.Quat(); v = r.Vec(); }
                if (i >= ents.Count) continue;
                Ent e = ents[i];
                if (e.T == null) continue;
                Mute(e);
                if (e.T.gameObject.activeSelf != on) e.T.gameObject.SetActive(on);
                if (!on) continue;
                if (e.LastRecv <= 0 || (p - e.T.position).sqrMagnitude > 400f) { e.T.position = p; e.T.rotation = q; }
                e.Pos = p; e.Rot = q; e.Vel = v; e.LastRecv = now;
            }
        }

        // Coupe la logique locale d'une entite (une fois) et celle des conteneurs qui la font apparaitre.
        static void Mute(Ent e)
        {
            if (e.Muted) return;
            e.Muted = true;
            foreach (MonoBehaviour m in e.T.GetComponents<MonoBehaviour>())
            {
                string n = m.GetType().Name;
                if (m is PlayMakerFSM || n == "MobileCarController" || n == "AxisCarController") m.enabled = false;
            }
            if (e.Body != null) e.Body.isKinematic = true;
            foreach (Wheel wh in e.T.GetComponentsInChildren<Wheel>(true)) wh.enabled = false;
            if (e.Walker)
                foreach (PlayMakerFSM f in e.T.GetComponentsInChildren<PlayMakerFSM>(true)) f.enabled = false;
            for (Transform p = e.T.parent; p != null; p = p.parent)
                foreach (PlayMakerFSM f in p.GetComponents<PlayMakerFSM>())
                    if (!mutedSpawners.Contains(f)) { f.enabled = false; mutedSpawners.Add(f); }
        }

        static void Follow(Ent e)
        {
            if (!e.T.gameObject.activeInHierarchy) return;
            float dt = Mathf.Min(Time.realtimeSinceStartup - e.LastRecv, 0.4f);
            Vector3 target = e.Pos + e.Vel * dt;
            float k = 1f - Mathf.Exp(-10f * Time.deltaTime);
            e.T.position = Vector3.Lerp(e.T.position, target, k);
            e.T.rotation = Quaternion.Slerp(e.T.rotation, e.Rot, k);
            if (e.Anim != null)
            {
                string clip = e.Vel.sqrMagnitude > 0.2f ? "fat_walk" : "fat_standing";
                if (e.Anim[clip] != null && !e.Anim.IsPlaying(clip)) e.Anim.CrossFade(clip, 0.3f);
            }
        }
    }
}
