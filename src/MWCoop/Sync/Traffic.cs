using System.Collections.Generic;
using MWCoop.Net;
using UnityEngine;

namespace MWCoop
{
    // Circulation et passants dictes par l'hote (comme la meteo) : voitures de TRAFFIC (routes,
    // chemins), de NPC_CARS (dont le bus : Bus.cs) et des barrages de police (TRAFFIC/Police : Police.cs), le train
    // (racine TRAIN), marcheurs de HUMANS/Randomizer/Walkers.
    //  - meme liste des deux cotes : chemin + rang parmi les homonymes (LAMORE x2, VICTRO x3...) ;
    //  - l'hote envoie 5 fois/s, par lots, actif, position, rotation, vitesse de chacun ;
    //  - chez l'invite : leur logique (MobileCarController, automates de conduite et de marche,
    //    automates des conteneurs qui les font apparaitre) est coupee, les voitures deviennent
    //    cinematiques et suivent ; les marcheurs jouent fat_walk / fat_standing selon leur vitesse.
    //  - le train (TRAIN/SpawnEast/TRAIN, que son automate Move fait passer sous SpawnWest et retour) :
    //    chez l'invite seuls Move et Reset sont coupes ; Player (le train tue le joueur local qu'il
    //    percute), Whistle et TunnelAudio restent, et son corps reste dynamique (sinon pas de collision
    //    avec le CharacterController du joueur). Pendant son attente au tunnel l'hote cache sa
    //    carrosserie (Mesh) : il l'envoie comme inactif.
    // Numeros : rang dans la liste (meme construction des deux cotes). L'invite donne l'empreinte de sa liste a
    // l'hote (toutes les 3 s tant qu'il n'a pas de reponse) : meme empreinte -> confirmee ; sinon l'hote envoie
    // ses cles et l'invite traduit chaque numero de l'hote vers sa propre entite de meme cle (absente : ignoree).
    // Avant la reponse, rien n'est applique (un numero pouvait deplacer un autre vehicule).
    // Message de controle : numero de depart 0xFFFF, puis genre (1 liste confirmee, 2 demande, 3 cles ; 10 barrage de
    // police, 11 action d'un invite dans le bus, 12 etat du bus : voir Police.cs et Bus.cs).
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
            public bool Police;              // voiture de police d'un barrage (Police.cs)
        }

        // TRAIN : toute la racine (le train change de parent entre SpawnEast et SpawnWest ; c'en est le seul
        // Rigidbody, son rang ne depend donc pas de son parent du moment).
        static readonly string[] Containers = { "TRAFFIC/VehiclesHighway", "TRAFFIC/VehiclesDirtRoad", "TRAIN", "NPC_CARS", "TRAFFIC/Police" };
        const string WalkersPath = "HUMANS/Randomizer/Walkers";
        static readonly List<Ent> ents = new List<Ent>();
        static readonly List<PlayMakerFSM> mutedSpawners = new List<PlayMakerFSM>();
        static bool built, verified;
        static float buildAt = -1, nextSend, nextLog, nextSample, nextTrainLog, nextAsk;
        static uint listHash;
        const int Control = 0xFFFF, C_SAME = 1, C_ASK = 2, C_KEYS = 3;
        static int[] remap;                 // invite : numero de l'hote -> rang ici (-1 : absente ici) ; null : memes listes
        static string[] hostKeys;           // invite : cles de l'hote en cours de reception
        static int hostKeysGot;

        public static void OnLevelLoaded()
        {
            ents.Clear(); mutedSpawners.Clear();
            built = false; verified = false; remap = null; hostKeys = null; hostKeysGot = 0; nextAsk = 0;
            buildAt = PlayerSync.InGame ? Time.realtimeSinceStartup + 6f : -1;
            Police.OnLevelLoaded();
            Bus.OnLevelLoaded();
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
            listHash = h;
            Log.Info("trafic : " + ents.Count + " vehicules et passants suivis (train " + (hasTrain ? "oui" : "non") + ", empreinte " + h.ToString("x8") + ")");
        }

        static Ent Add(Dictionary<string, int> keys, Transform t, Rigidbody rb, bool walker)
        {
            // Train, bus : sans leur parent du moment (le train passe de SpawnEast a SpawnWest, le bus d'un BusSpawn* a
            // l'autre : NPC_CARS::Bus Setup) ; voitures de police : sans le lieu du barrage du jour (Police::Parenting).
            string path = rb != null && rb.name == "TRAIN" ? "TRAIN/" + t.name : rb != null && rb.name == "BUS" ? "NPC_CARS/BUS"
                        : rb != null && rb.name.StartsWith("POLICECAR") ? "TRAFFIC/Police/" + t.name : Recon.Path(t);
            int k;
            keys.TryGetValue(path, out k);
            keys[path] = k + 1;
            var e = new Ent { Key = path + "#" + k, T = t, Body = rb, Walker = walker, Pos = t.position, Rot = t.rotation, Police = path.StartsWith("TRAFFIC/Police/") };
            ents.Add(e);
            return e;
        }

        public static void Update()
        {
            if (!Session.Active || !PlayerSync.InGame) return;
            Police.Update();
            UpdateTraffic();
            Bus.Update();   // apres le suivi : la copie du bus est a sa place de cette image
        }

        static void UpdateTraffic()
        {
            if (!built) { if (buildAt > 0 && Time.realtimeSinceStartup >= buildAt) Build(); return; }
            float now = Time.realtimeSinceStartup;
            if (Config.GetInt("Test", "JournalTrafic", 0) != 0 && now >= nextSample)
            {
                // Essais : meme instant (horloge du PC) des deux cotes, toutes les 10 s.
                nextSample = now + 10f;
                var sb = new System.Text.StringBuilder("trafic echantillon " + System.DateTime.Now.ToString("ss.f") + " :");
                // Vehicules actifs (pas les marcheurs) : position, cap, vitesse, rendu visible ([Test] JournalTrafic=2 : tous).
                int k = 0, max = Config.GetInt("Test", "JournalTrafic", 0) >= 2 ? 99 : 4;
                foreach (Ent e in ents)
                    if (e.T != null && e.T.gameObject.activeInHierarchy && !e.Walker && k++ < max)
                    {
                        bool vis = false;
                        foreach (Renderer r in e.T.GetComponentsInChildren<Renderer>()) if (r.enabled) { vis = true; break; }
                        sb.Append(' ').Append(e.T.name).Append(e.T.position.ToString("F0")).Append(" cap ").Append(e.T.eulerAngles.y.ToString("F0"))
                          .Append(e.Body != null ? " v" + e.Body.velocity.magnitude.ToString("F0") : "").Append(vis ? " rendu" : " SANS RENDU");
                        Transform lod = e.T.Find("LOD");
                        sb.Append(lod != null ? (lod.gameObject.activeSelf ? " lod on" : " lod off") : "").Append(';');
                    }
                Log.Info(sb.ToString());
            }
            if (Session.IsHost)
            {
                if (now < nextSend || Session.RemoteCount == 0) return;
                nextSend = now + 0.2f;
                SendAll();
                return;
            }
            // Invite : empreinte de sa liste a l'hote jusqu'a sa reponse.
            if (!verified && now >= nextAsk)
            {
                nextAsk = now + 3f;
                Session.SendToHost(new NetWriter(Msg.Traffic).U16(Control).U8(C_ASK).I32((int)listHash).U16(ents.Count), true);
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
            if (!built) return;
            int start = r.U16();
            if (start == Control) { OnControl(from, r); return; }
            if (Session.IsHost || !verified) return;
            int n = r.U8();
            float now = Time.realtimeSinceStartup;
            for (int i = start; i < start + n; i++)
            {
                bool on = r.Bool();
                Vector3 p = Vector3.zero, v = Vector3.zero;
                Quaternion q = Quaternion.identity;
                if (on) { p = r.Vec(); q = r.Quat(); v = r.Vec(); }
                int li = remap == null ? i : i < remap.Length ? remap[i] : -1;
                if (li < 0 || li >= ents.Count) continue;
                Ent e = ents[li];
                if (e.T == null) continue;
                Mute(e);
                if (e.T.gameObject.activeSelf != on) e.T.gameObject.SetActive(on);
                if (!on) continue;
                if (e.Mesh != null && !e.Mesh.activeSelf) e.Mesh.SetActive(true);   // cachee par Move avant sa coupure
                if (e.LastRecv <= 0 || (p - e.T.position).sqrMagnitude > 400f) { e.T.position = p; e.T.rotation = q; }
                e.Pos = p; e.Rot = q; e.Vel = v; e.LastRecv = now;
            }
        }

        // Liste de l'invite comparee a celle de l'hote (empreinte), cles de l'hote si elles different.
        static void OnControl(Peer from, NetReader r)
        {
            int kind = r.U8();
            // Barrages (10), bus (11, 12) : leurs propres messages.
            if (kind == 10) { Police.OnMessage(from, r); return; }
            if (kind == 11 || kind == 12) { Bus.OnMessage(kind, from, r); return; }
            uint h = (uint)r.I32();
            if (Session.IsHost)
            {
                if (kind != C_ASK) return;
                int count = r.U16();
                if (h == listHash && count == ents.Count)
                {
                    Session.T.SendReliable(from, new NetWriter(Msg.Traffic).U16(Control).U8(C_SAME).I32((int)listHash).U16(ents.Count).ToArray());
                    Log.Info("trafic : liste de " + from + " identique (" + count + ", empreinte " + h.ToString("x8") + ")");
                    return;
                }
                Log.Warn("trafic : liste de " + from + " differente (" + count + " entites, empreinte " + h.ToString("x8") + " ; ici " + ents.Count + ", " + listHash.ToString("x8") + ") : envoi des cles");
                for (int first = 0; first < ents.Count; )
                {
                    var w = new NetWriter(Msg.Traffic).U16(Control).U8(C_KEYS).I32((int)listHash).U16(ents.Count).U16(first);
                    int n = 0, len = 0;
                    while (first + n < ents.Count && n < 200 && (n == 0 || len + ents[first + n].Key.Length < 900)) { len += ents[first + n].Key.Length + 2; n++; }
                    w.U8(n);
                    for (int i = first; i < first + n; i++) w.Str(ents[i].Key);
                    Session.T.SendReliable(from, w.ToArray());
                    first += n;
                }
                return;
            }
            if (verified) return;
            int total = r.U16();
            if (kind == C_SAME)
            {
                if (h != listHash || total != ents.Count) return;   // (reponse a une autre liste)
                verified = true; remap = null;
                Log.Info("trafic : liste identique a celle de l'hote (" + total + ")");
                return;
            }
            if (kind != C_KEYS) return;
            int start = r.U16(), cnt = r.U8();
            if (hostKeys == null || hostKeys.Length != total) { hostKeys = new string[total]; hostKeysGot = 0; }
            for (int i = start; i < start + cnt; i++)
            {
                string k = r.Str();
                if (i < total && hostKeys[i] == null) { hostKeys[i] = k; hostKeysGot++; }
            }
            if (hostKeysGot < total) return;
            // Toutes les cles de l'hote : traduction de ses numeros vers les notres.
            var mine = new Dictionary<string, int>();
            for (int i = 0; i < ents.Count; i++) mine[ents[i].Key] = i;
            remap = new int[total];
            var notHere = new List<string>();
            var hostHas = new HashSet<string>();
            for (int i = 0; i < total; i++)
            {
                int li;
                remap[i] = mine.TryGetValue(hostKeys[i], out li) ? li : -1;
                if (remap[i] < 0 && notHere.Count < 12) notHere.Add(hostKeys[i]);
                hostHas.Add(hostKeys[i]);
            }
            var notThere = new List<string>();
            foreach (Ent e in ents) if (!hostHas.Contains(e.Key) && notThere.Count < 12) notThere.Add(e.Key);
            verified = true;
            hostKeys = null;
            Log.Warn("trafic : liste differente de celle de l'hote (" + total + " chez lui, " + ents.Count + " ici), numeros traduits par cle -- absentes ici : "
                     + string.Join(", ", notHere.ToArray()) + " ; absentes chez l'hote : " + string.Join(", ", notThere.ToArray()));
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
                // 'LOD' : montre ou cache le modele (<vehicule>/LOD) selon la distance a la camera du joueur D'ICI (300 m).
                // Coupe, il restait dans l'etat du moment : une voiture loin de l'invite a son arrivee restait invisible
                // pour toujours, meme en le croisant (vraie partie du 05/10 : 3 voitures croisees par l'hote, pas vues).
                if (f != null && f.FsmName == "LOD") continue;
                if (f != null || n == "MobileCarController" || n == "AxisCarController") m.enabled = false;
            }
            // Train : son corps reste dynamique comme chez l'hote (scene : non cinematique, positions X/Y et
            // rotations bloquees). Un corps cinematique ne recoit aucune collision du CharacterController du
            // joueur : Player ne tuerait plus l'invite sur la voie.
            if (e.Body != null && !e.Train) e.Body.isKinematic = true;
            foreach (Wheel wh in e.T.GetComponentsInChildren<Wheel>(true)) wh.enabled = false;
            if (e.Walker)
                foreach (PlayMakerFSM f in e.T.GetComponentsInChildren<PlayMakerFSM>(true)) f.enabled = false;
            // Police : pas ses parents (Cops::SpeakDB fait parler les agents ; l'automate Police est coupe par Police.cs).
            if (e.Police) return;
            for (Transform p = e.T.parent; p != null; p = p.parent)
                foreach (PlayMakerFSM f in p.GetComponents<PlayMakerFSM>())
                    if (!mutedSpawners.Contains(f)) { f.enabled = false; mutedSpawners.Add(f); }
        }

        // Essais (Autotest) 'train' : position du train toutes les 2 s des deux cotes (a comparer a la meme
        // heure des journaux : l'invite doit suivre l'hote a quelques metres pres).
        public static void Test(string mode, float t)
        {
            Police.Test(mode, t);
            Bus.Test(mode, t);
            if (mode != "train" || t < 10f || Time.realtimeSinceStartup < nextTrainLog) return;
            nextTrainLog = Time.realtimeSinceStartup + 2f;
            Ent tr = null;
            foreach (Ent e in ents) if (e.Train) { tr = e; break; }
            if (tr == null || tr.T == null) { Log.Info("autotest : train " + (built ? "absent de la liste du trafic" : "pas encore releve")); return; }
            Log.Info("autotest : train (" + (Session.IsHost ? "hote" : "invite") + ") " + tr.T.position.ToString("F1")
                     + " actif " + tr.T.gameObject.activeInHierarchy + ", carrosserie " + (tr.Mesh == null || tr.Mesh.activeInHierarchy)
                     + ", sous " + (tr.T.parent != null ? tr.T.parent.name : "-")
                     + ", corps " + (tr.Body != null && tr.Body.isKinematic ? "cinematique" : "dynamique")
                     + (Session.IsHost ? "" : ", recu il y a " + (Time.realtimeSinceStartup - tr.LastRecv).ToString("F1") + " s"));
        }

        // Objet suivi ici d'apres l'hote (copie cinematique de la circulation) ?
        public static bool Follows(Transform t)
        {
            if (Session.IsHost || t == null) return false;
            foreach (Ent e in ents) if (e.T == t) return e.LastRecv > 0;
            return false;
        }

        static void Follow(Ent e)
        {
            if (!e.T.gameObject.activeInHierarchy) return;
            float dt = Mathf.Min(Time.realtimeSinceStartup - e.LastRecv, 0.4f);
            Vector3 target = e.Pos + e.Vel * dt;
            float k = 1f - Mathf.Exp(-10f * Time.deltaTime);
            e.T.position = Vector3.Lerp(e.T.position, target, k);
            e.T.rotation = Quaternion.Slerp(e.T.rotation, e.Rot, k);
            if (e.Train && e.Body != null && !e.Body.isKinematic) e.Body.velocity = Vector3.zero;   // pas de derive apres un choc
            if (e.Anim != null)
            {
                string clip = e.Vel.sqrMagnitude > 0.2f ? "fat_walk" : "fat_standing";
                if (e.Anim[clip] != null && !e.Anim.IsPlaying(clip)) e.Anim.CrossFade(clip, 0.3f);
            }
        }
    }
}
