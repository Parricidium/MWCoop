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
            // Invite : roues qui tournent et pose au sol (la copie cinematique n'a plus ses Wheel actives).
            public Wheel[] Wheels; public float[] WheelRot; public float Steer, Ride; public Vector3 Base; public bool BaseSet;
            public Transform[] WheelVis; public float[] WheelR;   // roue visible (Wheel.model, sinon son enfant Wheel/Tire/tire) et rayon
            public float LastYaw;
            // Choc avec un invite (passage de physique) : Owner = joueur qui en a la physique (-1 : l'hote, comme d'habitude).
            public int Owner = -1; public float OwnSince, FarSince, OwnSent;
            public List<Behaviour> HostOff;  // hote : logique coupee pendant que l'invite l'a
            public bool Traffic;             // voiture de TRAFFIC/Vehicles* (seules celles-ci passent a l'invite)
            public float GuardUntil = -1f; public Vector3 GuardVel;   // hote : figee pres d'une voiture d'invite (HostGuard)
        }

        // TRAIN : toute la racine (le train change de parent entre SpawnEast et SpawnWest ; c'en est le seul
        // Rigidbody, son rang ne depend donc pas de son parent du moment).
        static readonly string[] Containers = { "TRAFFIC/VehiclesHighway", "TRAFFIC/VehiclesDirtRoad", "TRAIN", "NPC_CARS", "TRAFFIC/Police" };
        const string WalkersPath = "HUMANS/Randomizer/Walkers";
        static readonly List<Ent> ents = new List<Ent>();
        static readonly List<string> wheelNotes = new List<string>();
        static readonly List<PlayMakerFSM> mutedSpawners = new List<PlayMakerFSM>();
        static bool built, verified;
        static float buildAt = -1, nextSend, nextLog, nextSample, nextTrainLog, nextAsk;
        static uint listHash;
        const int Control = 0xFFFF, C_SAME = 1, C_ASK = 2, C_KEYS = 3;
        const int C_CLAIM = 20, C_RELEASE = 21, C_REFUSE = 22, C_STATE = 23;   // chocs : prise, rendu, refus, etat (invite -> hote)
        static int[] remap;                 // invite : numero de l'hote -> rang ici (-1 : absente ici) ; null : memes listes
        static string[] hostKeys;           // invite : cles de l'hote en cours de reception
        static int hostKeysGot;

        public static void OnLevelLoaded()
        {
            ents.Clear(); mutedSpawners.Clear(); wokenLogged.Clear(); triggersMuted = false; cousinMuted = false;
            built = false; verified = false; remap = null; hostKeys = null; hostKeysGot = 0; nextAsk = 0;
            buildAt = PlayerSync.InGame ? Time.realtimeSinceStartup + 6f : -1;
            Police.OnLevelLoaded();
            Contenu.OnLevelLoaded();
            Windshields.OnLevelLoaded();
            Packages.OnLevelLoaded();
            Vin.OnLevelLoaded();
            CarSale.OnLevelLoaded();
            TaxiCustomer.OnLevelLoaded();
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
            Log.Info("trafic : " + ents.Count + " vehicules et passants suivis (train " + (hasTrain ? "oui" : "non") + ", empreinte " + h.ToString("x8") + ")"
                     + (wheelNotes.Count > 0 ? ", roues sans modele (enfant tourne) : " + string.Join(", ", wheelNotes.ToArray()) : ""));
            wheelNotes.Clear();
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
            e.Traffic = !walker && rb != null && path.StartsWith("TRAFFIC/Vehicles");
            if (rb != null && !walker && rb.name != "TRAIN")
            {
                e.Wheels = t.GetComponentsInChildren<Wheel>(true); e.WheelRot = new float[e.Wheels.Length];
                // Voitures de la route (LAMORE, VICTRO...) : leur Wheel n'a pas de 'model' (c'est leur pilote, coupe chez
                // l'invite, qui tourne les roues) -- on tourne alors l'enfant visible de la roue.
                e.WheelVis = new Transform[e.Wheels.Length]; e.WheelR = new float[e.Wheels.Length];
                int noModel = 0;
                for (int i = 0; i < e.Wheels.Length; i++)
                {
                    Wheel w = e.Wheels[i];
                    if (w == null) continue;
                    Transform vis = w.model != null ? w.model.transform : w.transform.Find("Wheel") ?? w.transform.Find("Tire") ?? w.transform.Find("tire");
                    if (w.model == null) noModel++;
                    e.WheelVis[i] = vis;
                    e.WheelR[i] = w.radius > 0.05f ? w.radius : 0.32f;
                }
                if (noModel > 0) wheelNotes.Add(t.name + " " + noModel + "/" + e.Wheels.Length);
            }
            ents.Add(e);
            return e;
        }

        public static void Update()
        {
            if (!Session.Active || !PlayerSync.InGame) return;
            Perf.Sub("police", Police.Update);
            Perf.Sub("contenu", Contenu.Update);
            Perf.Sub("pare-brise", Windshields.Update);
            Perf.Sub("colis", Packages.Update);
            Perf.Sub("vin", Vin.Update);
            Perf.Sub("vente", CarSale.Update);
            Perf.Sub("taxi", TaxiCustomer.Update);
            Perf.Sub("trafic seul", UpdateTraffic);
            Perf.Sub("bus", Bus.Update);   // apres le suivi : la copie du bus est a sa place de cette image
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
                        sb.Append(lod != null ? (lod.gameObject.activeSelf ? " lod on" : " lod off") : "");
                        if (e.Wheels != null && e.Wheels.Length > 0)
                        {
                            Wheel w0 = e.Wheels[0];
                            sb.Append(" roue ").Append(w0 == null ? "?" : w0.model == null ? "sans modele" : w0.model.name + " r" + w0.radius.ToString("F2") + " rot " + w0.model.transform.localEulerAngles.ToString("F0") + (w0.enabled ? " ACTIVE" : ""))
                              .Append(" (").Append(e.Wheels.Length).Append(", tour ").Append(e.WheelRot != null && e.WheelRot.Length > 0 ? e.WheelRot[0].ToString("F0") : "-").Append(')');
                            // Hauteur du centre de la roue (modele) au-dessus du sol d'ici, et ecart de la caisse applique (invite).
                            Transform v0 = e.WheelVis != null && e.WheelVis.Length > 0 ? e.WheelVis[0] : (w0 != null && w0.model != null ? w0.model.transform : null);
                            if (v0 != null)
                            {
                                float best = float.MaxValue; Collider bc = null;
                                foreach (RaycastHit h in Physics.RaycastAll(v0.position + Vector3.up * 1.2f, Vector3.down, 4f))
                                    if (!h.collider.isTrigger && !h.collider.transform.IsChildOf(e.T) && h.distance < best) { best = h.distance; bc = h.collider; }
                                if (best < float.MaxValue) sb.Append(" centre roue a ").Append((1.2f - best).ToString("F2")).Append(" m du sol (r ").Append((e.WheelR != null && e.WheelR.Length > 0 ? e.WheelR[0] : 0f).ToString("F2")).Append(", sol ").Append(Recon.Path(bc.transform)).Append(", roue ").Append(Recon.Path(v0)).Append(")");
                            }
                            if (!Session.IsHost) sb.Append(" ecart caisse ").Append(e.Ride.ToString("F2"));
                        }
                        sb.Append(';');
                    }
                Log.Info(sb.ToString());
            }
            if (Session.IsHost)
            {
                HostFollowOwned();
                HostGuard(now);
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
                if (e.Owner == Session.LocalId) continue;   // physique ici (choc) : rien de l'hote
                Follow(e);
                if (e.Vel.sqrMagnitude > 0.5f) moving++;
            }
            GuestContacts(now);
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
                if (e.T == null || e.Owner == Session.LocalId) continue;   // (physique ici : la copie de l'hote nous suit)
                Mute(e);
                if (e.T.gameObject.activeSelf != on) e.T.gameObject.SetActive(on);
                if (!on) continue;
                if (!e.T.gameObject.activeInHierarchy) WakeParents(e);
                if (e.Mesh != null && !e.Mesh.activeSelf) e.Mesh.SetActive(true);   // cachee par Move avant sa coupure
                if (e.LastRecv <= 0 || (p - e.T.position).sqrMagnitude > 400f) { e.T.position = p; e.T.rotation = q; e.Base = p; e.BaseSet = true; e.Ride = 0f; }
                e.Pos = p; e.Rot = q; e.Vel = v; e.LastRecv = now;
            }
        }

        // Liste de l'invite comparee a celle de l'hote (empreinte), cles de l'hote si elles different.
        static void OnControl(Peer from, NetReader r)
        {
            int kind = r.U8();
            // Barrages (10), bus (11, 12) : leurs propres messages. Chocs (20 a 23) : passage de physique.
            if (kind == 10) { Police.OnMessage(from, r); return; }
            if (kind == 11 || kind == 12) { Bus.OnMessage(kind, from, r); return; }
            if (kind >= C_CLAIM && kind <= C_STATE) { OnOwnership(kind, from, r); return; }
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

        // Conteneur (TRAFFIC/VehiclesHighway ou VehiclesDirtRoad) eteint ici alors que l'hote montre la voiture : le jeu
        // n'allume que celui de la route ou passe le joueur LOCAL (TRAFFIC/TriggerManager, declencheurs d'entree et de
        // sortie). Chez l'invite les voitures de la route de l'hote restaient donc invisibles (vraie partie du 05/10 :
        // « 7 actifs, 17 en mouvement selon l'hote »). On rallume les parents, et ces declencheurs sont coupes ici.
        static readonly HashSet<GameObject> wokenLogged = new HashSet<GameObject>();

        static void WakeParents(Ent e)
        {
            MuteTriggerManager();
            for (Transform p = e.T.parent; p != null; p = p.parent)
                if (!p.gameObject.activeSelf)
                {
                    p.gameObject.SetActive(true);
                    if (wokenLogged.Add(p.gameObject)) Log.Info("trafic : " + Recon.Path(p) + " rallume ici (eteint par les declencheurs du joueur local, l'hote y a " + e.T.name + ")");
                }
        }

        static bool triggersMuted, testOffDone;

        static void MuteTriggerManager()
        {
            if (triggersMuted) return;
            triggersMuted = true;
            GameObject tm = Game.FindAny("TRAFFIC/TriggerManager");
            if (tm == null) { Log.Warn("trafic : TRAFFIC/TriggerManager introuvable"); return; }
            int n = 0;
            foreach (PlayMakerFSM f in tm.GetComponentsInChildren<PlayMakerFSM>(true)) if (f.enabled) { f.enabled = false; n++; }
            Log.Info("trafic : " + n + " declencheurs de TRAFFIC/TriggerManager coupes ici (routes allumees d'apres l'hote)");
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
            if (!e.Walker && !e.Train) MuteCrash(e);
            if (e.Walker)
                foreach (PlayMakerFSM f in e.T.GetComponentsInChildren<PlayMakerFSM>(true)) f.enabled = false;
            // Police : pas ses parents (Cops::SpeakDB fait parler les agents ; l'automate Police est coupe par Police.cs).
            if (e.Police) return;
            for (Transform p = e.T.parent; p != null; p = p.parent)
                foreach (PlayMakerFSM f in p.GetComponents<PlayMakerFSM>())
                    if (!mutedSpawners.Contains(f)) { f.enabled = false; mutedSpawners.Add(f); }
        }

        // Accident d'une voiture de PNJ (pick-up du cousin TRAFFIC/VehiclesDirtRoad/Rally/HEPPA ; KYLAJANI, AMIS2, EDM) :
        // <voiture>/CrashEvent::Crash attend que casse l'attache (FixedJoint) de CrashEvent/DeathForce -- resistance 500
        // quand le joueur LOCAL est a moins de 300 m (DeathForce::PlayerDistance). Chez l'invite la copie est deplacee a
        // la main : l'attache cassait, le pilote sortait en pantin, du sang au sol, voiture vide qui roulait (vraie partie
        // du 07/10), et l'automate pouvait compter un homicide routier a l'invite (Systems/PlayerWanted). Coupes ici,
        // attache incassable. CousinSpawns (repose le pick-up et lui rend sa physique au reveil) : coupe aussi.
        static void MuteCrash(Ent e)
        {
            int n = 0;
            foreach (PlayMakerFSM f in e.T.GetComponentsInChildren<PlayMakerFSM>(true))
                if ((f.FsmName == "Crash" || f.FsmName == "PlayerDistance") && f.enabled) { f.enabled = false; n++; }
            foreach (FixedJoint j in e.T.GetComponentsInChildren<FixedJoint>(true))
                if (j.name == "DeathForce") { j.breakForce = Mathf.Infinity; j.breakTorque = Mathf.Infinity; n++; }
            if (n > 0) Log.Info("trafic : " + e.Key + " : accident du pilote coupe ici (" + n + ")");
            if (cousinMuted) return;
            cousinMuted = true;
            GameObject cs = Game.FindAny("TRAFFIC/CousinSpawns");
            if (cs != null) foreach (PlayMakerFSM f in cs.GetComponents<PlayMakerFSM>()) f.enabled = false;
        }
        static bool cousinMuted;

        // Essais (Autotest) 'train' : position du train toutes les 2 s des deux cotes (a comparer a la meme
        // heure des journaux : l'invite doit suivre l'hote a quelques metres pres).
        // Essais : [Test] CameraTrafic=<nom> (HEPPA...) : camera d'essai a 5 m sur le cote de ce vehicule, qui le vise, et
        // une fois par seconde son ecart au sol (pivot de la roue avant, sol sous elle, ecart applique, caisse/racine).
        static Camera trafCam;
        static float trafLog;
        static void TestCamera()
        {
            string name = Config.Get("Test", "CameraTrafic", "");
            if (name.Length == 0) return;
            Ent e = null;
            foreach (Ent x in ents) if (x.T != null && x.T.name == name && x.T.gameObject.activeInHierarchy) { e = x; break; }
            if (e == null) return;
            if (trafCam == null) { trafCam = new GameObject("MWCoop-CameraTrafic").AddComponent<Camera>(); trafCam.depth = 100; trafCam.nearClipPlane = 0.05f; trafCam.fieldOfView = 55; }
            trafCam.transform.position = e.T.position + e.T.right * 5f + Vector3.up * 0.6f;
            trafCam.transform.LookAt(e.T.position + Vector3.up * 0.4f);
            camT = trafCam.transform;   // (le calage au sol se fait pres de cette camera)
            if (Time.realtimeSinceStartup < trafLog) return;
            trafLog = Time.realtimeSinceStartup + 1f;
            var sb = new System.Text.StringBuilder("autotest : trafic " + name + " en " + e.T.position.ToString("F2") + ", ecart " + e.Ride.ToString("F2") + (e.Body != null ? ", caisse-racine " + (e.Body.position - e.T.position).ToString("F2") : ""));
            for (int i = 0; e.WheelVis != null && i < e.WheelVis.Length; i++)
            {
                Transform v = e.WheelVis[i];
                if (v == null) continue;
                float best = float.MaxValue;
                foreach (RaycastHit h in Physics.RaycastAll(v.position + Vector3.up * 1.2f, Vector3.down, 4f))
                    if (!h.collider.isTrigger && !h.collider.transform.IsChildOf(e.T) && h.distance < best) best = h.distance;
                Renderer rr = v.GetComponentInChildren<Renderer>();
                sb.Append(" | roue ").Append(i).Append(" pivot ").Append(best < float.MaxValue ? (1.2f - best).ToString("F2") : "?").Append(" m du sol")
                  .Append(rr != null ? ", bas du rendu " + (rr.bounds.min.y - (v.position.y - (1.2f - best))).ToString("F2") + " m du sol" : "");
            }
            Log.Info(sb.ToString());
        }

        public static void Test(string mode, float t)
        {
            TestCamera();
            Police.Test(mode, t);
            Contenu.Test(mode, t);
            Bus.Test(mode, t);
            if (mode == "choc") TestChoc(t);
            if (mode == "frontal") TestFrontal(t);
            // [Test] EteindreConteneur=chemin (invite, 25 s) : comme un declencheur de route du joueur local.
            string off = Config.Get("Test", "EteindreConteneur", "");
            if (off.Length > 0 && !Session.IsHost && t > 25f && !testOffDone)
            {
                testOffDone = true;
                GameObject c = Game.FindAny(off);
                if (c != null) { c.SetActive(false); Log.Info("autotest : " + off + " eteint ici"); }
            }
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

        // Copie d'une voiture (invite) : roues qui tournent a la vitesse recue, braquees d'apres son virage ; pres de la
        // camera (150 m), les roues posees sur le sol d'ici (rayon sous chacune) : la caisse monte ou descend de l'ecart
        // moyen, en douceur et dans +-0,6 m -- elle ne flotte plus ni ne s'enfonce (retour d'un joueur, 07/10).
        static void WheelsAndGround(Ent e)
        {
            float fwd = Vector3.Dot(e.Vel, e.T.forward);
            float yaw = e.T.eulerAngles.y, dyaw = Mathf.DeltaAngle(e.LastYaw, yaw);
            e.LastYaw = yaw;
            float rate = Time.deltaTime > 1e-4f ? dyaw / Time.deltaTime : 0f;   // deg/s
            float want = Mathf.Abs(fwd) > 1f ? Mathf.Clamp(rate * 2.6f / Mathf.Abs(fwd) / 35f * Mathf.Sign(fwd), -1f, 1f) : 0f;
            e.Steer = Mathf.Lerp(e.Steer, want, 1f - Mathf.Exp(-6f * Time.deltaTime));
            for (int i = 0; i < e.Wheels.Length; i++)
            {
                Wheel w = e.Wheels[i];
                Transform vis = e.WheelVis != null ? e.WheelVis[i] : null;
                if (w == null || vis == null) continue;
                e.WheelRot[i] += fwd / e.WheelR[i] * Time.deltaTime;
                vis.localRotation = Quaternion.Euler(0f, e.Steer * w.maxSteeringAngle, 0f) * Quaternion.AngleAxis(57.29578f * e.WheelRot[i], Vector3.right);
            }
            // Calage au sol (0.40) coupe par defaut depuis 0.56 : la pose de l'hote est juste (meme hauteur des deux cotes,
            // mesure et capture, 08/10) ; le rayon sous les roues touchait un collisionneur de route ~0,6 m au-dessus de la
            // chaussee visible et decalait les voitures proches de la camera (« voitures enfoncees », retour d'un joueur).
            // [Test] CalageTrafic=1 le remet.
            if (Config.GetInt("Test", "CalageTrafic", 0) == 0) { e.Ride = 0f; return; }
            if (camT == null) { Camera c = Camera.main; if (c != null) camT = c.transform; }
            if (camT == null || (camT.position - e.T.position).sqrMagnitude > 150f * 150f) return;
            float sum = 0f;
            int n = 0;
            for (int wi = 0; wi < e.Wheels.Length; wi++)
            {
                Wheel w = e.Wheels[wi];
                Transform vis = e.WheelVis != null ? e.WheelVis[wi] : null;
                if (w == null || vis == null) continue;
                Vector3 c0 = vis.position;
                RaycastHit[] hs = Physics.RaycastAll(c0 + Vector3.up * 1.2f, Vector3.down, 4f);
                float best = float.MaxValue;
                for (int h = 0; h < hs.Length; h++)
                {
                    if (hs[h].collider.isTrigger || hs[h].collider.transform.IsChildOf(e.T)) continue;   // (sa propre caisse)
                    if (hs[h].distance < best) best = hs[h].distance;
                }
                if (best == float.MaxValue) continue;
                float groundY = c0.y + 1.2f - best;
                sum += groundY + e.WheelR[wi] - c0.y;
                n++;
            }
            if (n < 2) return;
            float err = Mathf.Clamp(e.Ride + sum / n, -0.6f, 0.6f);
            e.Ride = Mathf.Lerp(e.Ride, err, 1f - Mathf.Exp(-4f * Time.deltaTime));
        }

        // ---------------------------------------------------------------- chocs : passage de physique
        // L'invite qui conduit pres d'une voiture de la circulation (TRAFFIC/Vehicles*, moins de 9 m) en prend la
        // physique ICI : elle repart de la vitesse recue, ses Wheel remises, et il envoie sa position 20 fois par seconde
        // (C_STATE). L'hote (C_CLAIM) coupe son pilote (MobileCarController, automates), la rend cinematique et suit ;
        // deja a un autre joueur : refus (C_REFUSE), l'invite la rend aussitot. Eloigne (plus de 14 m pendant 1,5 s, ou
        // 25 s ecoulees) : l'invite la rend (C_RELEASE : position, rotation, vitesse) ; l'hote lui rend sa physique et son
        // pilote a cet endroit. Les autres invites voient la copie de l'hote comme toujours.
        static int HostIndexOf(int li)
        {
            if (remap == null) return li;
            for (int i = 0; i < remap.Length; i++) if (remap[i] == li) return i;
            return -1;
        }
        static Ent EntOfHostIndex(int hi)
        {
            int li = Session.IsHost || remap == null ? hi : hi < remap.Length ? remap[hi] : -1;
            return li >= 0 && li < ents.Count ? ents[li] : null;
        }

        static void GuestContacts(float now)
        {
            Transform car = VehicleSync.LocalDrivingRoot;
            Rigidbody carBody = car != null ? car.GetComponent<Rigidbody>() : null;
            Vector3 carVel = carBody != null ? carBody.velocity : Vector3.zero;
            int me = Session.LocalId;
            for (int li = 0; li < ents.Count; li++)
            {
                Ent e = ents[li];
                if (e.T == null || !e.Traffic || e.Body == null) continue;
                bool mine = e.Owner == me;
                float d2 = car != null && e.T.gameObject.activeInHierarchy ? (e.T.position - car.position).sqrMagnitude : float.MaxValue;
                if (!mine)
                {
                    // Rayon selon la vitesse de rapprochement : 9 m a l'arret, ~30 m pour deux voitures face a face a 70 km/h.
                    // Avant : 9 m tout court, soit 0,2 s face a face -- la prise arrivait chez l'hote apres le choc, ou la copie
                    // cinematique de notre voiture envoyait en l'air sa voiture, physique (retour d'un joueur, 08/10).
                    float claimR = 9f;
                    if (d2 < 2500f)
                    {
                        float d = Mathf.Sqrt(d2);
                        float closing = d > 0.01f ? -Vector3.Dot((e.T.position - car.position) / d, e.Vel - carVel) : 0f;
                        claimR += Mathf.Clamp(closing, 0f, 60f) * 0.5f;
                    }
                    if (e.Owner < 0 && d2 < claimR * claimR && e.LastRecv > 0 && verified && Config.GetInt("Test", "SansPrise", 0) == 0) Claim(e, li, now);
                    continue;
                }
                if (now - e.OwnSent >= 0.05f)
                {
                    e.OwnSent = now;
                    int hi = HostIndexOf(li);
                    if (hi >= 0) Session.SendToHost(new NetWriter(Msg.Traffic).U16(Control).U8(C_STATE).U16(hi).Vec(e.T.position).Quat(e.T.rotation).Vec(e.Body.velocity), false);
                }
                bool far = d2 > 196f;
                if (!far) e.FarSince = -1; else if (e.FarSince < 0) e.FarSince = now;
                if ((far && now - e.FarSince > 1.5f) || (now - e.OwnSince > 25f && d2 > 64f) || car == null && now - e.OwnSince > 3f) Release(e, li, "eloignee");
            }
        }

        static void Claim(Ent e, int li, float now)
        {
            int hi = HostIndexOf(li);
            if (hi < 0) return;
            e.Owner = Session.LocalId; e.OwnSince = now; e.FarSince = -1; e.OwnSent = 0;
            e.Body.isKinematic = false;
            e.Body.velocity = e.Vel;
            e.Body.angularVelocity = Vector3.zero;
            e.T.position = e.Base + Vector3.up * e.Ride;
            e.Ride = 0f;
            if (e.Wheels != null) foreach (Wheel w in e.Wheels) if (w != null) w.enabled = true;
            Session.SendToHost(new NetWriter(Msg.Traffic).U16(Control).U8(C_CLAIM).U16(hi).Vec(e.T.position).Quat(e.T.rotation).Vec(e.Vel), true);
            Log.Info("trafic : choc possible avec " + e.Key + " : physique ici (" + (e.Vel.magnitude * 3.6f).ToString("F0") + " km/h)");
        }

        static void Release(Ent e, int li, string why)
        {
            int hi = HostIndexOf(li);
            Vector3 v = e.Body != null ? e.Body.velocity : Vector3.zero;
            if (hi >= 0) Session.SendToHost(new NetWriter(Msg.Traffic).U16(Control).U8(C_RELEASE).U16(hi).Vec(e.T.position).Quat(e.T.rotation).Vec(v), true);
            BackToCopy(e);
            e.Pos = e.T.position; e.Rot = e.T.rotation; e.Vel = v; e.Base = e.T.position; e.BaseSet = true; e.LastRecv = Time.realtimeSinceStartup;
            Log.Info("trafic : " + e.Key + " rendue a l'hote (" + why + ")");
        }

        static void BackToCopy(Ent e)   // invite : de nouveau la copie cinematique de l'hote
        {
            e.Owner = -1;
            if (e.Body != null) e.Body.isKinematic = true;
            if (e.Wheels != null) foreach (Wheel w in e.Wheels) if (w != null) w.enabled = false;
        }

        static void OnOwnership(int kind, Peer from, NetReader r)
        {
            int hi = r.U16();
            Ent e = EntOfHostIndex(hi);
            if (!Session.IsHost)
            {
                if (kind == C_REFUSE && e != null && e.Owner == Session.LocalId) { BackToCopy(e); Log.Info("trafic : " + e.Key + " deja a un autre joueur, rendue"); }
                return;
            }
            Vector3 p = r.Vec(); Quaternion q = r.Quat(); Vector3 v = r.Vec();
            if (e == null || e.T == null || e.Body == null) return;
            float now = Time.realtimeSinceStartup;
            if (kind == C_CLAIM)
            {
                if (e.Owner >= 0 && e.Owner != from.Id)
                {
                    Session.T.SendReliable(from, new NetWriter(Msg.Traffic).U16(Control).U8(C_REFUSE).U16(hi).ToArray());
                    return;
                }
                if (e.Owner != from.Id)
                {
                    e.Owner = from.Id;
                    e.GuardUntil = -1f;
                    e.HostOff = new List<Behaviour>();
                    foreach (MonoBehaviour m in e.T.GetComponents<MonoBehaviour>())
                    {
                        string n = m.GetType().Name;
                        var f = m as PlayMakerFSM;
                        if (f != null && f.FsmName == "LOD") continue;
                        if ((f != null || n == "MobileCarController" || n == "AxisCarController") && m.enabled) { m.enabled = false; e.HostOff.Add(m); }
                    }
                    if (e.Wheels != null) foreach (Wheel w in e.Wheels) if (w != null && w.enabled) { w.enabled = false; e.HostOff.Add(w); }
                    e.Body.isKinematic = true;
                    Log.Info("trafic : " + e.Key + " : physique a " + from + " (choc possible), pilote en pause");
                }
            }
            if (e.Owner != from.Id) return;
            e.Pos = p; e.Rot = q; e.Vel = v; e.LastRecv = now;
            if (kind == C_RELEASE)
            {
                e.T.position = p; e.T.rotation = q;
                e.Body.isKinematic = false;
                e.Body.velocity = v;
                if (e.HostOff != null) foreach (Behaviour b in e.HostOff) if (b != null) b.enabled = true;
                e.HostOff = null;
                e.Owner = -1;
                Log.Info("trafic : " + e.Key + " rendue par " + from + ", pilote repris (" + (v.magnitude * 3.6f).ToString("F0") + " km/h)");
            }
        }

        // Hote : la voiture d'un invite au volant est ici une copie cinematique (VehicleSync) ; une voiture de la circulation,
        // physique ici, qu'elle touche avant que la prise de l'invite (GuestContacts) arrive est repoussee comme par une masse
        // infinie -- envolee en tournoyant (retour d'un joueur, 08/10 : « the npc jumps and flips »). Pres d'une voiture
        // d'invite (rayon selon la vitesse de rapprochement), elle devient cinematique et continue tout droit a sa vitesse,
        // jusqu'a la prise de l'invite (C_CLAIM), ou 0,6 s apres l'ecart revenu ; puis physique de nouveau, a cette vitesse.
        static int guardLogs;
        static readonly List<KeyValuePair<Vector3, Vector3>> guardCars = new List<KeyValuePair<Vector3, Vector3>>();
        static void HostGuard(float now)
        {
            guardCars.Clear();
            if (Config.GetInt("Test", "SansGarde", 0) != 0) return;   // (essais : comme avant 0.60.7)
            foreach (int id in Session.Players.Keys)
            {
                if (id == Session.LocalId) continue;
                Transform t = VehicleSync.RemoteCarTransform(id);
                if (t != null && t.gameObject.activeInHierarchy) guardCars.Add(new KeyValuePair<Vector3, Vector3>(t.position, VehicleSync.RemoteVelocity(id)));
            }
            foreach (Ent e in ents)
            {
                if (e.T == null || !e.Traffic || e.Body == null || e.Owner >= 0) { if (e.T != null) e.GuardUntil = -1f; continue; }
                if (!e.T.gameObject.activeInHierarchy) { if (e.GuardUntil > 0f) { e.GuardUntil = -1f; e.Body.isKinematic = false; } continue; }
                Vector3 tv = e.GuardUntil > 0f ? e.GuardVel : e.Body.velocity;
                bool near = false;
                foreach (KeyValuePair<Vector3, Vector3> c in guardCars)
                {
                    Vector3 rel = e.T.position - c.Key;
                    float d = rel.magnitude;
                    float closing = d > 0.01f ? -Vector3.Dot(rel / d, tv - c.Value) : 0f;
                    if (d < 7f + Mathf.Clamp(closing, 0f, 60f) * 0.6f) { near = true; break; }
                }
                if (near)
                {
                    if (e.GuardUntil < 0f)
                    {
                        if (e.Body.isKinematic) continue;   // (cinematique pour une autre raison : pas a nous)
                        e.GuardVel = e.Body.velocity;
                        e.Body.isKinematic = true;
                        if (guardLogs++ < 20) Log.Info("trafic : " + e.Key + " figee pres d'une voiture d'invite (" + (e.GuardVel.magnitude * 3.6f).ToString("F0") + " km/h), en attendant sa prise");
                    }
                    e.GuardUntil = now + 0.6f;
                }
                if (e.GuardUntil < 0f) continue;
                if (now < e.GuardUntil) { e.T.position += e.GuardVel * Time.deltaTime; continue; }
                e.GuardUntil = -1f;
                e.Body.isKinematic = false;
                e.Body.velocity = e.GuardVel;
            }
        }

        // Hote : voitures dont un invite a la physique, suivies d'apres ses messages ; invite parti : reprises.
        static void HostFollowOwned()
        {
            float now = Time.realtimeSinceStartup;
            foreach (Ent e in ents)
            {
                if (e.Owner < 0 || e.T == null || e.Body == null) continue;
                bool gone = !Session.Players.ContainsKey(e.Owner) || now - e.LastRecv > 3f;
                if (gone)
                {
                    e.Body.isKinematic = false;
                    if (e.HostOff != null) foreach (Behaviour b in e.HostOff) if (b != null) b.enabled = true;
                    e.HostOff = null;
                    Log.Info("trafic : " + e.Key + " reprise (joueur #" + e.Owner + " muet ou parti)");
                    e.Owner = -1;
                    continue;
                }
                float dt = Mathf.Min(now - e.LastRecv, 0.2f);
                float k = 1f - Mathf.Exp(-15f * Time.deltaTime);
                e.T.position = Vector3.Lerp(e.T.position, e.Pos + e.Vel * dt, k);
                e.T.rotation = Quaternion.Slerp(e.T.rotation, e.Rot, k);
            }
        }

        // Essai [Test] Autotest=frontal (retour d'un joueur, 08/10 : voiture de la circulation envolee pres de la voiture d'un
        // invite) : l'invite au volant de [Test] TestVoiture (15/22 s) ; a 40 s, sa voiture posee 30 m devant la voiture de la
        // circulation en mouvement la plus proche, face a elle, lancee a 15 m/s. L'hote note de 38 a 70 s, pour chaque
        // voiture de la circulation active, sa montee et sa rotation maximales. [Test] SansPrise=1 : l'invite ne prend
        // jamais la physique (seule la garde de l'hote joue).
        static int frontStep;
        static float frontNext;
        static readonly Dictionary<Ent, Vector3> frontMax = new Dictionary<Ent, Vector3>();   // (vitesse verticale max, rotation max, haut min)
        static readonly Dictionary<Ent, Vector3> frontLast = new Dictionary<Ent, Vector3>();
        public static void TestFrontal(float t)
        {
            string car = Config.Get("Test", "TestVoiture", "SORBET(190-200psi)");
            if (Session.IsHost)
            {
                if (t > 38f && t < 70f)
                    foreach (Ent e in ents)
                    {
                        if (e.T == null || !e.Traffic || e.Body == null || !e.T.gameObject.activeInHierarchy) continue;
                        Vector3 last;
                        bool had = frontLast.TryGetValue(e, out last);
                        frontLast[e] = e.T.position;
                        if (!had || Time.deltaTime <= 0f) continue;
                        float vy = (e.T.position.y - last.y) / Time.deltaTime;
                        Vector3 m; if (!frontMax.TryGetValue(e, out m)) m = new Vector3(0f, 0f, 1f);
                        frontMax[e] = new Vector3(Mathf.Max(m.x, vy), Mathf.Max(m.y, e.Body.isKinematic ? 0f : e.Body.angularVelocity.magnitude), Mathf.Min(m.z, e.T.up.y));
                    }
                if (t > 70f && frontStep == 0)
                {
                    frontStep = 1;
                    var sb = new System.Text.StringBuilder("autotest : frontal (hote) :");
                    foreach (KeyValuePair<Ent, Vector3> kv in frontMax)
                        if (kv.Value.x > 2f || kv.Value.y > 1f || kv.Value.z < 0.8f) sb.Append(' ').Append(kv.Key.Key).Append(" monte a ").Append(kv.Value.x.ToString("F1")).Append(" m/s, rotation ").Append(kv.Value.y.ToString("F1")).Append(" rad/s, haut min ").Append(kv.Value.z.ToString("F2")).Append(';');
                    sb.Append(" (").Append(frontMax.Count).Append(" voitures suivies)");
                    Log.Info(sb.ToString());
                }
                return;
            }
            if (t > 15f && frontStep == 0) { frontStep = 1; Log.Info("autotest : " + VehicleSync.TestEnter(car, false)); }
            if (t > 22f && frontStep == 1) { frontStep = 2; Log.Info("autotest : volant -> " + VehicleSync.TestEnter(car, true)); }
            if (t > frontNext && t > 40f && t < 60f && frontStep == 2)   // (toutes les secondes jusqu'a une voiture en mouvement)
            {
                frontNext = t + 1f;
                Rigidbody mine = VehicleSync.Body(car);
                Ent best = null;
                float bd = float.MaxValue;
                foreach (Ent e in ents)
                {
                    if (e.T == null || !e.Traffic || e.Body == null || !e.T.gameObject.activeInHierarchy || e.LastRecv <= 0 || e.Vel.magnitude < 8f) continue;
                    float d = mine != null ? (e.T.position - mine.position).sqrMagnitude : 0f;
                    if (d < bd) { bd = d; best = e; }
                }
                if (best == null || mine == null) { if (t > 59f) Log.Info("autotest : frontal : aucune voiture de la circulation en mouvement (" + (mine == null ? "pas de voiture" : "") + ")"); return; }
                frontStep = 3;
                Vector3 dir = best.Vel; dir.y = 0f; dir.Normalize();
                Vector3 at = best.T.position + dir * 30f + Vector3.up * 0.6f;
                mine.transform.position = at;
                mine.transform.rotation = Quaternion.LookRotation(-dir, Vector3.up);
                mine.velocity = -dir * 15f;
                mine.angularVelocity = Vector3.zero;
                Log.Info("autotest : frontal : " + best.Key + " a " + (best.Vel.magnitude * 3.6f).ToString("F0") + " km/h, ma voiture posee 30 m devant, face a elle, a 54 km/h");
            }
        }

        // Essai [Test] Autotest=choc (invite) : a 40 s, prend la physique de la voiture de la circulation active la plus
        // proche (comme un choc), la pousse de cote ; la rend a 52 s. Attendu : "physique ici" puis "rendue" chez
        // l'invite, "physique a #1" puis "rendue par #1, pilote repris" chez l'hote.
        static int chocStep;
        static Ent chocEnt;
        static float chocY, chocUntil, chocNext;
        static string ChocState(Ent e)
        {
            if (e == null || e.T == null || e.Body == null) return "?";
            return "dy " + (e.T.position.y - chocY).ToString("F2") + " m, corps dy " + (e.Body.position.y - chocY).ToString("F2") + " m, v " + e.Body.velocity.magnitude.ToString("F1")
                   + " m/s (vy " + e.Body.velocity.y.ToString("F1") + "), rotation " + e.Body.angularVelocity.magnitude.ToString("F1") + " rad/s, haut " + e.T.up.y.ToString("F2")
                   + (e.Body.isKinematic ? ", cinematique" : "") + ", corps " + (e.Body.transform == e.T ? "racine" : Recon.Path(e.Body.transform));
        }
        public static void TestChoc(float t)
        {
            if (Session.IsHost || !verified) return;
            if (chocStep == 0 && t > 40f)
            {
                chocStep = 1;
                Transform me = Camera.main != null ? Camera.main.transform : null;
                int best = -1;
                float bd = float.MaxValue;
                for (int i = 0; i < ents.Count; i++)
                {
                    Ent e = ents[i];
                    if (e.T == null || !e.Traffic || e.Body == null || !e.T.gameObject.activeInHierarchy || e.LastRecv <= 0) continue;
                    float d = me != null ? (e.T.position - me.position).sqrMagnitude : 0f;
                    if (d < bd) { bd = d; best = i; }
                }
                if (best < 0) { Log.Info("autotest : choc : aucune voiture de la circulation active ici"); return; }
                chocEnt = ents[best]; chocY = chocEnt.T.position.y; chocUntil = t + 4f; chocNext = 0f;
                Log.Info("autotest : choc : avant " + ChocState(chocEnt));
                Claim(ents[best], best, Time.realtimeSinceStartup);
                // ([Test] ChocPousse=0 : sans poussee -- la prise de physique seule, comme une voiture qui passe pres)
                if (Config.GetInt("Test", "ChocPousse", 1) != 0) ents[best].Body.AddForce(ents[best].T.right * ents[best].Body.mass * 4f, ForceMode.Impulse);
                Log.Info("autotest : choc : " + ents[best].Key + " poussee a " + Mathf.Sqrt(bd).ToString("F0") + " m");
            }
            if (chocStep == 1 && chocEnt != null && t < chocUntil && t >= chocNext) { chocNext = t + 0.25f; Log.Info("autotest : choc : " + ChocState(chocEnt)); }
            if (chocStep == 1 && t > 52f)
            {
                chocStep = 2;
                for (int i = 0; i < ents.Count; i++) if (ents[i].Owner == Session.LocalId) Release(ents[i], i, "fin de l'essai");
            }
        }

        // Objet suivi ici d'apres l'hote (copie cinematique de la circulation) ?
        public static bool Follows(Transform t)
        {
            if (Session.IsHost || t == null) return false;
            foreach (Ent e in ents) if (e.T == t) return e.LastRecv > 0;
            return false;
        }

        static Transform camT;

        static void Follow(Ent e)
        {
            if (!e.T.gameObject.activeInHierarchy) return;
            float dt = Mathf.Min(Time.realtimeSinceStartup - e.LastRecv, 0.4f);
            Vector3 target = e.Pos + e.Vel * dt;
            float k = 1f - Mathf.Exp(-10f * Time.deltaTime);
            if (!e.BaseSet) { e.Base = e.T.position; e.BaseSet = true; }
            e.Base = Vector3.Lerp(e.Base, target, k);   // position de l'hote, sans la pose au sol d'ici
            e.T.rotation = Quaternion.Slerp(e.T.rotation, e.Rot, k);
            e.T.position = e.Base + Vector3.up * e.Ride;
            if (e.Wheels != null && e.Wheels.Length > 0) WheelsAndGround(e);
            if (e.Train && e.Body != null && !e.Body.isKinematic) e.Body.velocity = Vector3.zero;   // pas de derive apres un choc
            if (e.Anim != null)
            {
                string clip = e.Vel.sqrMagnitude > 0.2f ? "fat_walk" : "fat_standing";
                if (e.Anim[clip] != null && !e.Anim.IsPlaying(clip)) e.Anim.CrossFade(clip, 0.3f);
            }
        }
    }
}
