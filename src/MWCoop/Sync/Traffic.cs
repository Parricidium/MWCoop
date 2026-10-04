using System.Collections.Generic;
using MWCoop.Net;
using UnityEngine;

namespace MWCoop
{
    // Circulation et passants dictes par l'hote (comme la meteo) : voitures de TRAFFIC (routes,
    // chemins) et de NPC_CARS, le train (racine TRAIN), marcheurs de HUMANS/Randomizer/Walkers.
    //  - meme liste des deux cotes : chemin + rang parmi les homonymes (LAMORE x2, VICTRO x3...) ;
    //  - l'hote envoie 5 fois/s, par lots, actif, position, rotation, vitesse de chacun ;
    //  - chez l'invite : leur logique (MobileCarController, automates de conduite et de marche,
    //    automates des conteneurs qui les font apparaitre) est coupee, les voitures deviennent
    //    cinematiques et suivent ; les marcheurs jouent fat_walk / fat_standing selon leur vitesse.
    //  - le train (TRAIN/SpawnEast/TRAIN, que son automate Move fait passer sous SpawnWest et retour) :
    //    chez l'invite seuls Move et Reset sont coupes ; Player (le train tue le joueur local qu'il
    //    percute), Whistle et TunnelAudio restent. Pendant son attente au tunnel l'hote cache sa
    //    carrosserie (Mesh) : il l'envoie comme inactif.
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
            public bool Train;
            public GameObject Mesh;          // train : carrosserie (et collisions), cachee pendant l'attente
        }

        // TRAIN : toute la racine (le train change de parent entre SpawnEast et SpawnWest ; c'en est le seul
        // Rigidbody, son rang ne depend donc pas de son parent du moment).
        static readonly string[] Containers = { "TRAFFIC/VehiclesHighway", "TRAFFIC/VehiclesDirtRoad", "TRAIN", "NPC_CARS" };
        const string WalkersPath = "HUMANS/Randomizer/Walkers";
        static readonly List<Ent> ents = new List<Ent>();
        static readonly List<PlayMakerFSM> mutedSpawners = new List<PlayMakerFSM>();
        static bool built;
        static float buildAt = -1, nextSend, nextLog, nextSample, nextTrainLog;

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
                if (root == null) { Log.Warn("trafic : conteneur " + c + " introuvable"); continue; }
                // Vehicules : objets a CarDynamics (ou le train : Rigidbody direct) sous le conteneur.
                foreach (Rigidbody rb in root.GetComponentsInChildren<Rigidbody>(true))
                {
                    bool train = rb.name == "TRAIN";
                    if (rb.GetComponent("CarDynamics") == null && !train) continue;
                    var e = Add(keys, rb.transform, rb, false);
                    if (!train) continue;
                    e.Train = true;
                    Transform mesh = rb.transform.Find("Mesh");
                    if (mesh != null) e.Mesh = mesh.gameObject;
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
            // Empreinte de la liste (cles dans l'ordre) : la meme chez l'hote et l'invite, sinon les numeros
            // des messages designent d'autres vehicules.
            uint h = 2166136261;
            bool hasTrain = false;
            foreach (Ent e in ents)
            {
                foreach (char ch in e.Key) { h ^= ch; h *= 16777619; }
                if (e.Train) hasTrain = true;
            }
            Log.Info("trafic : " + ents.Count + " vehicules et passants suivis (train " + (hasTrain ? "oui" : "non") + ", empreinte " + h.ToString("x8") + ")");
        }

        static Ent Add(Dictionary<string, int> keys, Transform t, Rigidbody rb, bool walker)
        {
            string path = rb != null && rb.name == "TRAIN" ? "TRAIN/" + t.name : Recon.Path(t);   // train : sans son parent du moment
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
                    bool on = e.T != null && e.T.gameObject.activeInHierarchy && (e.Mesh == null || e.Mesh.activeSelf);
                    w.Bool(on);
                    if (!on) continue;
                    // Train : deplace par son automate (MoveTowards), sa vitesse vient de sa position (sauf
                    // saut : retour au point de depart apres l'attente, cache entre-temps).
                    Vector3 d = e.T.position - e.Pos;
                    Vector3 v = e.Body != null && !e.Body.isKinematic && !e.Train ? e.Body.velocity : d.sqrMagnitude > 400f ? Vector3.zero : d / 0.2f;
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
                if (e.Mesh != null && !e.Mesh.activeSelf) e.Mesh.SetActive(true);   // cachee par Move avant sa coupure
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
                var f = m as PlayMakerFSM;
                if (f != null && e.Train && f.FsmName != "Move" && f.FsmName != "Reset") continue;   // train : collision, sifflet, tunnel
                if (f != null || n == "MobileCarController" || n == "AxisCarController") m.enabled = false;
            }
            if (e.Body != null) e.Body.isKinematic = true;
            foreach (Wheel wh in e.T.GetComponentsInChildren<Wheel>(true)) wh.enabled = false;
            if (e.Walker)
                foreach (PlayMakerFSM f in e.T.GetComponentsInChildren<PlayMakerFSM>(true)) f.enabled = false;
            for (Transform p = e.T.parent; p != null; p = p.parent)
                foreach (PlayMakerFSM f in p.GetComponents<PlayMakerFSM>())
                    if (!mutedSpawners.Contains(f)) { f.enabled = false; mutedSpawners.Add(f); }
        }

        // Essais (Autotest) 'train' : position du train toutes les 2 s des deux cotes (a comparer a la meme
        // heure des journaux : l'invite doit suivre l'hote a quelques metres pres).
        public static void Test(string mode, float t)
        {
            if (mode != "train" || t < 10f || Time.realtimeSinceStartup < nextTrainLog) return;
            nextTrainLog = Time.realtimeSinceStartup + 2f;
            Ent tr = null;
            foreach (Ent e in ents) if (e.Train) { tr = e; break; }
            if (tr == null || tr.T == null) { Log.Info("autotest : train " + (built ? "absent de la liste du trafic" : "pas encore releve")); return; }
            Log.Info("autotest : train (" + (Session.IsHost ? "hote" : "invite") + ") " + tr.T.position.ToString("F1")
                     + " actif " + tr.T.gameObject.activeInHierarchy + ", carrosserie " + (tr.Mesh == null || tr.Mesh.activeInHierarchy)
                     + ", sous " + (tr.T.parent != null ? tr.T.parent.name : "-")
                     + (Session.IsHost ? "" : ", recu il y a " + (Time.realtimeSinceStartup - tr.LastRecv).ToString("F1") + " s"));
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
