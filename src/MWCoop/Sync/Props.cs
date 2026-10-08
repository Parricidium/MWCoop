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
    // Objets transportes (coffre, banquette, plateau) : celui qui conduit la voiture (ou en garde la main
    // moteur tournant) fait autorite sur ce qui est pose dedans. Il envoie leur pose DANS la voiture (etat 3 :
    // numero de la voiture, position et rotation locales) 8 fois/s tant qu'elle roule ; chez les autres
    // l'objet devient cinematique et colle a leur copie de la voiture (pas de glissade ni de traversee sur une
    // copie qui avance par a-coups). Sorti de la voiture, ou quand il n'en a plus la main : pose dans le monde
    // (etat 0, fiable) et l'objet retombe sous la physique. L'hote ne recale pas ce qu'un autre transporte,
    // et une pose au repos recue pour un objet dans la voiture qu'on conduit est ignoree.
    // Pose au repos (etat 0) d'un objet pose dans une voiture : envoyee aussi DANS la voiture, et posee chez
    // les autres sur leur propre voiture -- le recalage de la voiture (avant ou apres) l'emmene alors une seule
    // fois. Une pose au repos hors voiture fait autorite : un recalage de voiture juste apres ne la deplace pas.
    public static class Props
    {
        class Prop
        {
            public string Id;
            public Rigidbody Body;
            public PlayMakerFSM Use;              // automate 'Use' qui porte l'ID (articles) : voit qu'il a disparu
            public int RemoteBy = -1;
            public int LayerWas = -1;             // deplace par un autre : calque du jeu pour un objet tenu (16), le sien avant
            public float LastRemote, SettleUntil;
            public Vector3 Pos, Vel, LastSentPos;
            public Quaternion Rot;
            public bool Kinematic, WasKinematic;
            public int RideCar = -1;              // recu : numero de la voiture ou l'objet est colle ici
            public int RelCar = -1;               // tenu / lache par un autre assis dans cette voiture : pose dans son repere
            public Vector3 RelPos; public Quaternion RelRot;
            public Vector3 RideLocal, RideCur;    // pose recue dans la voiture, pose affichee (rattrape)
            public Quaternion RideLocalRot, RideCurRot;
            public int RideOut = -1, RidePass;    // envoye : voiture ou on le transporte (autorite ici)
            public float RideKeep, RideSeen;
            public float GoneAt;                  // corps detruit ici (disparu) : > 0 en attente, -1 traite
            public float WorldAt;                 // recu : pose au repos hors voiture (CarMoved ne l'emmene pas)
            public float LocalAt, RemoteAt;       // dernier deplacement par ce joueur-ci (tenu, lache) / par un autre (message)
        }

        static readonly Dictionary<string, Prop> props = new Dictionary<string, Prop>();
        static readonly Dictionary<Rigidbody, Prop> byBody = new Dictionary<Rigidbody, Prop>();
        static PlayMakerFSM hand;
        static Prop held;
        static readonly List<Prop> settling = new List<Prop>();
        static readonly List<Prop> ridingIn = new List<Prop>();    // colles ici a une copie de voiture
        static readonly List<Prop> ridingOut = new List<Prop>();   // transportes par nous
        static readonly List<Prop> vanished = new List<Prop>();
        static readonly HashSet<string> noKey = new HashSet<string>();
        static readonly Dictionary<string, float> unknown = new Dictionary<string, float>();
        static float nextScan = -1, nextSend, nextHost, lastForced, nextRide;
        static int ridePass;

        public static void OnLevelLoaded()
        {
            props.Clear(); byBody.Clear(); settling.Clear(); worldKeys.Clear(); worldLogged = false;
            ridingIn.Clear(); ridingOut.Clear(); vanished.Clear(); unknown.Clear();
            hand = null; held = null;
            nextScan = PlayerSync.InGame ? Time.realtimeSinceStartup + 10f : -1;
        }

        // Identifiant d'un objet : la variable texte 'ID' d'un de ses automates (Data pour les pieces :
        // VIN514B1 ; Use pour les articles : beercase1, shoppingbag1). Fixee a la creation de l'objet
        // (nom a compteur sauvegarde), elle reste la meme quand le jeu le renomme ensuite.
        public static string ItemId(GameObject go)
        {
            PlayMakerFSM use;
            return ItemId(go, out use);
        }

        static string ItemId(GameObject go, out PlayMakerFSM use)
        {
            use = null;
            foreach (PlayMakerFSM f in go.GetComponents<PlayMakerFSM>())
            {
                FsmString s = f.FsmVariables.FindFsmString("ID");
                if (s != null && s.Value.Length > 0) { if (f.FsmName == "Use") use = f; return s.Value; }
            }
            return "";
        }

        // Colis de la poste : pas d'ID, mais il porte sa commande (OrderAMIS3, OrderYP1...), au nom unique
        // et identique chez chacun (compteur sauvegarde, commandes rejouees dans le meme ordre).
        static string PackageKey(GameObject go)
        {
            foreach (PlayMakerFSM f in go.GetComponents<PlayMakerFSM>())
            {
                FsmGameObject o = f.FsmVariables.FindFsmGameObject("ThisOrder");
                if (o != null && o.Value != null) return "colis:" + o.Value.name;
            }
            return "";
        }

        static bool censusDone, worldLogged;
        static readonly Dictionary<GameObject, string> worldKeys = new Dictionary<GameObject, string>();
        static readonly HashSet<string> WorldRoots = new HashSet<string> { "EQUIPMENTS", "Systems", "YARD", "COTTAGE", "CABIN", "MISC",
            "JOBS", "STORE_AREA", "SOCCER", "MAP", "PERAJARVI", "HOMENEW" };   // plaques de puits, mobilier du pub, buts, abribus...

        static string WorldKey(Rigidbody rb)
        {
            string k;
            if (worldKeys.TryGetValue(rb.gameObject, out k)) return k;
            Transform t = rb.transform;
            // Vetements (veste, combinaison, casque) : cle fixe. Portes au chargement puis enleves, ils sont a la
            // racine de la scene (hors de EQUIPMENTS) et n'auraient sinon jamais de cle.
            k = Wear.KeyOf(rb.gameObject);
            // Enveloppe du catalogue (commande commune, rejouee chez tous) : sortie du catalogue a la racine de la
            // scene, de meme ; un seul objet. PAS la lettre de Kela (Sheets/envelope(kela1)) : Systems/Expenses tourne
            // chez chaque joueur (WorldFsms.SkipObjects), chacun a et poste la sienne -- partagee, celle de l'un
            // n'etait plus postee chez l'autre (Calls.CheckEnvelope) et sa demande d'allocation de la semaine perdue.
            string tn = t.name;
            if (k == null && tn == Calls.PartsEnvelope) k = "w:enveloppe:" + tn;
            // CD sorti de son boitier (ou d'un lecteur) : 'Remove part' le detache de PivotCD et lui rend un corps,
            // hors de toute racine du monde. Cle fixe par son nom de disque (Data.ThisCD : CD1, CD2...).
            if (k == null && tn == "cd(itemx)") k = CdKey(t.gameObject);
            if (k != null) { worldKeys[rb.gameObject] = k; return k; }
            // Prise du chauffage moteur : son corps est detruit une fois branchee et recree au debranchement. Cle
            // par son UT (maison, usine), pas par l'endroit : tenue en main, elle est sous PLAYER pour les deux.
            bool plug = tn.StartsWith("cable plug");
            if ((!WorldRoots.Contains(t.root.name) && !plug) || rb.GetComponent("CarDynamics") != null) return null;
            if (!plug && VehicleSync.CarRoot(t) != null) return null;   // piece d'une voiture rangee sous une racine du monde (taxi sous JOBS) : CarDoors, pas Props
            string pk = plug ? CarDoors.PlugKey(Game.FsmOn(t.gameObject, "Data")) : null;
            k = plug ? (pk != null ? "w:" + pk : "w:prise:" + t.root.name + "/" + (t.parent != null ? t.parent.name : "")) : "w:" + Recon.Path(t) + "#" + t.GetSiblingIndex();
            worldKeys[rb.gameObject] = k;
            return k;
        }

        // CD par son disque (CD1, CD2...), ou qu'il soit (boitier, lecteur, main, sol).
        public static GameObject FindCd(string disc)
        {
            foreach (Object o in Resources.FindObjectsOfTypeAll(typeof(PlayMakerFSM)))
            {
                var f = (PlayMakerFSM)o;
                if (f.FsmName != "Data" || f.gameObject.name != "cd(itemx)" || f.hideFlags != HideFlags.None) continue;
                FsmString n = f.FsmVariables.FindFsmString("ThisCD");
                if (n != null && n.Value == disc) return f.gameObject;
            }
            return null;
        }

        public static string CdKey(GameObject cd)
        {
            PlayMakerFSM d = Game.FsmOn(cd, "Data");
            FsmString n = d != null ? d.FsmVariables.FindFsmString("ThisCD") : null;
            return n != null && !string.IsNullOrEmpty(n.Value) ? "w:cd:" + n.Value : null;
        }

        static void Register(Rigidbody rb, string id, PlayMakerFSM use)
        {
            Prop p;
            if (!props.TryGetValue(id, out p)) { p = new Prop { Id = id }; props[id] = p; }
            p.Body = rb;
            p.GoneAt = 0f;
            if (use != null) p.Use = use;
            byBody[rb] = p;
        }

        // Objet Unity detruit dont on garde la reference (le corps d'un article mange, empoche, jete).
        static bool Destroyed(Object o) { return !ReferenceEquals(o, null) && o == null; }

        static void Scan()
        {
            // Le Rigidbody d'une piece disparait quand elle est montee et revient au demontage :
            // la table est refaite a chaque passage, seuls les objets physiques actifs y sont.
            float now = Time.realtimeSinceStartup;
            byBody.Clear();
            foreach (Prop p in props.Values) { if (Destroyed(p.Body)) Gone(p, now); p.Body = null; }
            bool census = Config.GetInt("Test", "JournalSansId", 0) != 0 && !censusDone;
            var noId = census ? new Dictionary<string, int>() : null;
            foreach (Rigidbody rb in Object.FindObjectsOfType<Rigidbody>())
            {
                if (rb.transform.root.name == "PLAYER" && rb.transform.parent.name != "ItemPivot") continue;
                if (Garage.Owns(rb)) continue;   // levage d'un cric (corps rajoute par le jeu) : Garage, pas Props (recalage de l'hote sinon)
                PlayMakerFSM use;
                string id = ItemId(rb.gameObject, out use);
                if (id.Length == 0) id = PackageKey(rb.gameObject);
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
                Register(rb, id, use);
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

        // Objet tout juste cree (Consume, a sa creation) : suivi tout de suite, sans attendre le releve.
        public static void Track(GameObject go)
        {
            if (nextScan < 0 || go == null) return;
            Rigidbody rb = go.GetComponent<Rigidbody>();
            if (rb == null || byBody.ContainsKey(rb)) return;
            PlayMakerFSM use;
            string id = ItemId(go, out use);
            if (id.Length == 0) id = PackageKey(go);
            if (id.Length > 0) Register(rb, id, use);
        }

        // Objets crees en nombre (sac vide d'un coup) : releve complet tout de suite (ou presque).
        public static void SoonScan()
        {
            float t = Time.realtimeSinceStartup + 0.3f;
            if (nextScan > t) nextScan = t;
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
                    Consume.Track(go);   // article tout neuf : son automate Use suivi avant qu'il soit mange ou empoche
                    if (h == null && noKey.Add(go.name)) Log.Info("objets : objet tenu sans cle, non synchronise : " + go.name);
                }
            }
            if (h != held)
            {
                if (held != null && !DropInCopy(held)) { held.SettleUntil = now + 5f; if (!settling.Contains(held)) settling.Add(held); }
                if (h != null)
                {
                    settling.Remove(h); Unride(h); h.RemoteBy = -1; SetKinematic(h, false); Log.Info("piece prise : " + h.Id);
                    if (h.Body != null) Consume.Track(h.Body.gameObject);   // empoche ou mange en main : deja suivi
                }
                held = h;
            }

            if (now >= nextSend)
            {
                nextSend = now + 1f / 15f;
                if (held != null && held.Body != null) { held.LocalAt = now; Send(held, 1); }
                for (int i = settling.Count - 1; i >= 0; i--)
                {
                    Prop p = settling[i];
                    bool done = p.Body == null || now > p.SettleUntil || (now > p.SettleUntil - 4.5f && p.Body.IsSleeping());
                    p.LocalAt = now;
                    Send(p, done ? 0 : 2);
                    if (done) settling.RemoveAt(i);
                }
            }
            if (now >= nextRide) { nextRide = now + 0.125f; Ride(now); }

            if (Session.IsHost && now >= nextHost && Session.RemoteCount > 0)
            {
                nextHost = now + 2f;
                foreach (Prop p in props.Values)
                {
                    if (p.Body == null || p == held || p.RemoteBy >= 0 || p.RideOut >= 0 || settling.Contains(p)) continue;
                    if ((p.Body.position - p.LastSentPos).sqrMagnitude < 0.04f) continue;
                    // Piece montee sur un vehicule (portiere, capot...) : elle suit la voiture, pas de recalage.
                    Transform root = VehicleSync.CarRoot(p.Body.transform);
                    if (root != null && root != p.Body.transform) continue;
                    // Posee dans une voiture qu'un autre fait rouler : c'est lui qui la transporte.
                    Rigidbody car = VehicleSync.CarUnder(p.Body);
                    if (car != null && VehicleSync.Authority(car) != Session.LocalId) continue;
                    Send(p, 0);
                }
            }

            foreach (Prop p in props.Values)
            {
                if (p.Body == null) { if (p.GoneAt == 0f && Destroyed(p.Body)) Gone(p, now); continue; }
                if (p.RemoteBy < 0 || p.RideCar >= 0) continue;   // transporte : place apres la physique (LateUpdate)
                if (now - p.LastRemote > 1.5f) { p.RemoteBy = -1; SetKinematic(p, false); continue; }
                Follow(p);
            }
            // Transport recu : la voiture n'est plus une copie ici (conducteur sorti), ou plus de nouvelles
            // (conducteur parti) -> retombe sous la physique locale.
            for (int i = ridingIn.Count - 1; i >= 0; i--)
            {
                Prop p = ridingIn[i];
                bool copy = VehicleSync.IsCopy(p.RideCar);
                if (p.Body != null && p != held && now - p.LastRemote < 3f && copy) continue;
                Log.Info("objet " + p.Id + " n'est plus transporte (" + (copy ? "sans nouvelles" : "voiture rendue") + ")");
                Unglue(p);
            }
            for (int i = vanished.Count - 1; i >= 0; i--)
            {
                Prop p = vanished[i];
                if (p.GoneAt > 0f && now - p.GoneAt < 0.5f) continue;
                vanished.RemoveAt(i);
                if (p.GoneAt <= 0f) continue;   // revenu (meme ID) entre-temps
                p.GoneAt = -1f;
                if (!Consume.Done(p.Id)) Consume.SendGone(p.Id, Consume.GoneState(p.Use));
            }
        }

        // Calque d'un objet tenu en main (automate PickUp du jeu : SetLayer 16 a la prise, 19 au lacher) : il ne
        // heurte pas les voitures. Un objet deplace par un autre joueur y est mis aussi, sinon ce corps cinematique
        // teleporte a chaque image poussait la voiture contre laquelle l'autre l'amenait.
        const int HeldLayer = 16;

        static void HeldLook(Prop p, bool on)
        {
            if (p.Body == null) return;
            GameObject g = p.Body.gameObject;
            if (on && p.LayerWas < 0) { p.LayerWas = g.layer; g.layer = HeldLayer; }
            else if (!on && p.LayerWas >= 0) { if (g.layer == HeldLayer && p != held) g.layer = p.LayerWas; p.LayerWas = -1; }   // pris ici : le jeu le garde au 16
        }

        static void SetKinematic(Prop p, bool on)
        {
            if (!on) HeldLook(p, false);
            if (p.Body == null || p.Kinematic == on) return;
            p.Kinematic = on;
            if (on) { p.WasKinematic = p.Body.isKinematic; p.Body.isKinematic = true; }
            else { p.Body.isKinematic = p.WasKinematic; if (!p.Body.isKinematic) p.Body.velocity = p.Vel; }
        }

        static void Follow(Prop p)
        {
            if (p.LayerWas < 0 && p.Body.gameObject.layer != HeldLayer) HeldLook(p, true);
            Transform t = p.Body.transform;
            float k = 1f - Mathf.Exp(-20f * Time.deltaTime);
            Vector3 tp = p.Pos; Quaternion tr = p.Rot;
            Rigidbody rc = p.RelCar >= 0 ? VehicleSync.CarBody(p.RelCar) : null;
            if (rc != null)
            {
                // Tenu dans une voiture : la cible suit cette voiture ici ; on rattrape le seul ecart dans la voiture.
                Transform ct = rc.transform;
                tp = ct.position + ct.rotation * p.RelPos; tr = ct.rotation * p.RelRot;
                Vector3 cur = Quaternion.Inverse(ct.rotation) * (t.position - ct.position);
                if ((cur - p.RelPos).sqrMagnitude > 4f) { t.position = tp; t.rotation = tr; return; }
                t.position = ct.position + ct.rotation * Vector3.Lerp(cur, p.RelPos, k);
                t.rotation = Quaternion.Slerp(t.rotation, tr, k);
                return;
            }
            if ((tp - t.position).sqrMagnitude > 9f) { t.position = tp; t.rotation = tr; return; }
            t.position = Vector3.Lerp(t.position, tp, k);
            t.rotation = Quaternion.Slerp(t.rotation, tr, k);
        }

        static void Send(Prop p, int state, bool reliable = false)
        {
            if (p.Body == null) return;
            p.LastSentPos = p.Body.position;
            var w = new NetWriter(Msg.Prop).U8(Session.LocalId).Str(p.Id).U8(state)
                .Vec(p.Body.position).Quat(p.Body.rotation).Vec(p.Body.velocity);
            if (state == 0) InCarPose(p, w);
            else if (state == 1 || state == 2) RidePose(p, w);
            Session.SendAll(w, reliable);
        }

        // Lache en passager dans la copie d'une voiture conduite ailleurs : pas de chute ici (la copie, deplacee a chaque
        // image, ne l'emporterait pas) ; colle a la voiture, et sa pose dans la voiture part (fiable) : le conducteur le
        // pose sur la vraie voiture, le laisse retomber et le transporte (Ride) -- comme ce qui est deja dans le coffre.
        static bool DropInCopy(Prop p)
        {
            if (p.Body == null || !Session.Active) return false;
            Rigidbody rc = VehicleSync.LocalRideBody();
            int rci = VehicleSync.CarIndex(rc);
            if (rci < 0 || !VehicleSync.IsCopy(rci)) return false;
            Transform ct = rc.transform;
            Quaternion inv = Quaternion.Inverse(ct.rotation);
            Vector3 lp = inv * (p.Body.position - ct.position);
            Quaternion lr = inv * p.Body.rotation;
            p.LastSentPos = p.Body.position;
            Session.SendAll(new NetWriter(Msg.Prop).U8(Session.LocalId).Str(p.Id).U8(0).Vec(p.Body.position).Quat(p.Body.rotation).Vec(Vector3.zero)
                .U8(rci).Vec(lp).Quat(lr), true);
            Glue(p, VehicleSync.Authority(rc), rci, lp, lr, Vector3.zero);
            Log.Info("objet " + p.Id + " lache dans la copie de " + rc.name + " : pose envoyee au conducteur");
            return true;
        }

        // Tenu ou lache par le joueur local assis dans une voiture (conducteur ou passager) : numero de la voiture, pose dans
        // son repere, a la suite. Chez les autres, l'objet suit LEUR voiture a chaque image : en roulant, la pose du monde
        // (prise sur une copie en retard) le montrait derriere la voiture, et le conducteur le lachait la -- objets qui
        // sortaient de la voiture ou passaient a travers (retour d'un joueur, 08/10).
        static void RidePose(Prop p, NetWriter w)
        {
            Rigidbody car = VehicleSync.LocalRideBody();
            int ci = VehicleSync.CarIndex(car);
            if (ci < 0) return;
            Transform ct = car.transform;
            Quaternion inv = Quaternion.Inverse(ct.rotation);
            w.U8(ci).Vec(inv * (p.Body.position - ct.position)).Quat(inv * p.Body.rotation);
        }

        // Au repos dans une voiture : numero de la voiture, position et rotation dans son repere, a la suite.
        static void InCarPose(Prop p, NetWriter w)
        {
            Rigidbody car = VehicleSync.CarUnder(p.Body);
            int ci = VehicleSync.CarIndex(car);
            if (ci < 0) return;
            Quaternion inv = Quaternion.Inverse(car.rotation);
            w.U8(ci).Vec(inv * (p.Body.position - car.position)).Quat(inv * p.Body.rotation);
        }

        // Corps detruit ici : l'article a-t-il disparu (automate Use detruit, inactif, ou dans un etat de
        // disparition) ? Consume l'a normalement dit aux autres ; sinon (article tout neuf pas encore suivi,
        // etat inconnu) on le dit a sa place une demi-seconde plus tard. Pas les pieces (corps retire au montage).
        static void Gone(Prop p, float now)
        {
            if (p.GoneAt != 0f || ReferenceEquals(p.Use, null)) return;
            if (p.Use != null && p.Use.gameObject.activeInHierarchy && !Consume.IsGone(p.Use)) return;
            p.GoneAt = now;
            vanished.Add(p);
        }

        // Objet suivi de cle 'id' (null : inconnu ou plus de corps ici).
        public static GameObject ObjectOf(string id)
        {
            Prop p;
            return props.TryGetValue(id, out p) && p.Body != null ? p.Body.gameObject : null;
        }

        // Le dernier a avoir deplace l'objet est-il un autre joueur (sa copie suit ici ses messages) ? 'age' : temps
        // depuis son dernier message pour cet objet. Faux pour un objet inconnu, ou deplace en dernier par ce joueur-ci.
        // (Boite aux lettres : seul celui qui lache l'enveloppe la poste.)
        public static bool MovedByOther(GameObject go, out float age)
        {
            age = float.MaxValue;
            Rigidbody rb = go != null ? go.GetComponent<Rigidbody>() : null;
            Prop p;
            if (rb == null || !byBody.TryGetValue(rb, out p) || p.RemoteAt <= 0f || p.RemoteAt <= p.LocalAt) return false;
            age = Time.realtimeSinceStartup - p.RemoteAt;
            return true;
        }

        // Objet que ce joueur-ci va cacher ou deplacer lui-meme (vetement porte par un autre) : plus suivi ni colle,
        // rendu a sa physique d'avant.
        public static void Release(GameObject go)
        {
            Rigidbody rb = go != null ? go.GetComponent<Rigidbody>() : null;
            Prop p;
            if (rb == null || !byBody.TryGetValue(rb, out p)) return;
            Unride(p);
            p.RemoteBy = -1;
            SetKinematic(p, false);
            settling.Remove(p);
        }

        // Objet disparu chez un autre sans automate a rejouer ici (Consume) : on le cache.
        public static bool Vanish(string id)
        {
            Prop p;
            if (!props.TryGetValue(id, out p) || p.Body == null) return false;
            p.Body.gameObject.SetActive(false);
            byBody.Remove(p.Body);
            p.Body = null;
            p.GoneAt = -1f;
            return true;
        }

        // ---------------------------------------------------------------- objets transportes
        // Autorite ici sur une voiture (on la conduit, ou son moteur tourne a nous) : ce qui est pose dedans.
        // Pose dans la voiture envoyee 8 fois/s tant qu'elle roule, sinon quand l'objet a glisse (> 2 cm,
        // 3 degres) et une fois par seconde pour rester colle chez les autres.
        static void Ride(float now)
        {
            ridePass++;
            for (int ci = 0; ci < VehicleSync.Count; ci++)
            {
                if (!VehicleSync.DrivenHere(ci)) continue;
                Rigidbody car = VehicleSync.CarBody(ci);
                if (car == null) continue;
                Transform ct = car.transform;
                Vector3 cp = car.position;
                Quaternion inv = Quaternion.Inverse(car.rotation);
                bool moving = car.velocity.sqrMagnitude > 0.04f || car.angularVelocity.sqrMagnitude > 0.01f;
                foreach (Prop p in props.Values)
                {
                    if (p.Body == null || p == held || p.RemoteBy >= 0 || p.GoneAt != 0f || p.Body.isKinematic) continue;
                    if ((p.Body.position - cp).sqrMagnitude > 49f || p.Body.transform.root == ct || settling.Contains(p)) continue;
                    if (VehicleSync.CarUnder(p.Body) != car) continue;
                    p.RidePass = ridePass; p.RideSeen = now;
                    Vector3 lp = inv * (p.Body.position - cp);
                    Quaternion lr = inv * p.Body.rotation;
                    bool start = p.RideOut != ci;
                    if (!start && !moving && now < p.RideKeep && (lp - p.RideLocal).sqrMagnitude < 0.0004f && Quaternion.Angle(lr, p.RideLocalRot) < 3f) continue;
                    if (start)
                    {
                        if (p.RideOut < 0) ridingOut.Add(p);
                        p.RideOut = ci;
                        Log.Info("objet " + p.Id + " transporte dans " + car.name);
                    }
                    p.RideLocal = lp; p.RideLocalRot = lr; p.RideKeep = now + 1f;
                    p.LastSentPos = p.Body.position;
                    Session.SendAll(new NetWriter(Msg.Prop).U8(Session.LocalId).Str(p.Id).U8(3)
                        .Vec(lp).Quat(lr).Vec(p.Body.velocity).U8(ci), false);
                }
            }
            // Sorti de la voiture, ou on n'en a plus la main : pose dans le monde, il retombe chez tous.
            for (int i = ridingOut.Count - 1; i >= 0; i--)
            {
                Prop p = ridingOut[i];
                if (p.RidePass == ridePass) continue;
                // Pas vu a un passage (cahot, rayon a cote) : encore 0,6 s avant de le declarer sorti.
                bool free = p.Body == null || p == held || p.RemoteBy >= 0 || p.GoneAt != 0f;
                if (!free && VehicleSync.DrivenHere(p.RideOut) && now - p.RideSeen < 0.6f) continue;
                ridingOut.RemoveAt(i);
                p.RideOut = -1;
                // Pris en main (ici ou ailleurs), en train de retomber, disparu : son propre message suit.
                if (p.Body == null || p == held || p.RemoteBy >= 0 || p.GoneAt != 0f || settling.Contains(p)) continue;
                Send(p, 0, true);
                Log.Info("objet " + p.Id + " n'est plus transporte, pose en " + p.Body.position.ToString("F1"));
            }
        }

        // Transport recu : l'objet devient cinematique et colle a notre copie de la voiture, a la pose recue
        // dans la voiture. Pas une copie ici (pas encore, ou plus) : la pose de fin suivra.
        static void Glue(Prop p, int who, int ci, Vector3 lp, Quaternion lr, Vector3 vel)
        {
            Rigidbody car = VehicleSync.CarBody(ci);
            if (car == null || !VehicleSync.IsCopy(ci)) return;
            if (p.RideCar != ci)
            {
                // Depart de la pose actuelle dans la voiture locale, rattrapee en douceur (sauf si trop loin).
                Transform ct = car.transform, t = p.Body.transform;
                Quaternion inv = Quaternion.Inverse(ct.rotation);
                p.RideCur = inv * (t.position - ct.position);
                p.RideCurRot = inv * t.rotation;
                if ((p.RideCur - lp).sqrMagnitude > 1f) { p.RideCur = lp; p.RideCurRot = lr; }
                if (p.RideCar < 0) ridingIn.Add(p);
                p.RideCar = ci;
                settling.Remove(p);
                Log.Info("objet " + p.Id + " transporte dans " + car.name + " par #" + who);
            }
            p.RideLocal = lp; p.RideLocalRot = lr; p.Vel = vel;
            p.RemoteBy = who;
            p.LastRemote = Time.realtimeSinceStartup;
            SetKinematic(p, true);
        }

        // Fin du transport recu sans pose de fin : retombe sous la physique, a la vitesse de la voiture, la
        // ou il est dans la voiture (qui a pu etre recalee d'un coup depuis la derniere image).
        static void Unglue(Prop p)
        {
            int ci = p.RideCar;
            Place(p);
            Unride(p);
            p.RemoteBy = -1;
            p.Vel = VehicleSync.CarVelocity(ci);
            SetKinematic(p, false);
        }

        static void Unride(Prop p)
        {
            if (p.RideCar < 0) return;
            p.RideCar = -1;
            ridingIn.Remove(p);
        }

        // Apres la physique (VehicleSync.LateUpdate) : les objets colles suivent la pose affichee de la copie.
        public static void LateUpdate()
        {
            if (ridingIn.Count == 0) return;
            float k = 1f - Mathf.Exp(-12f * Time.deltaTime);
            for (int i = 0; i < ridingIn.Count; i++)
            {
                Prop p = ridingIn[i];
                if (p.Body == null || p == held) continue;
                p.RideCur = Vector3.Lerp(p.RideCur, p.RideLocal, k);
                p.RideCurRot = Quaternion.Slerp(p.RideCurRot, p.RideLocalRot, k);
                Place(p);
            }
        }

        static void Place(Prop p)
        {
            Rigidbody car = VehicleSync.CarBody(p.RideCar);
            if (p.Body == null || car == null) return;
            Transform ct = car.transform, t = p.Body.transform;
            t.position = ct.position + ct.rotation * p.RideCur;
            t.rotation = ct.rotation * p.RideCurRot;
        }

        // La voiture 'car' va etre replacee d'un coup (recalage d'une voiture garee, copie trop loin) : ce qui
        // est pose dedans la suit (meme deplacement), au lieu de rester sur place ou de tomber a travers.
        // Pas un objet dont on vient de recevoir la pose hors voiture : elle compte deja ce deplacement.
        public static void CarMoved(Rigidbody car, Vector3 pos, Quaternion rot, Vector3 vel)
        {
            if (car == null || nextScan < 0) return;
            Vector3 cp = car.position;
            Quaternion inv = Quaternion.Inverse(car.rotation);
            float now = Time.realtimeSinceStartup;
            int n = 0;
            foreach (Prop p in props.Values)
            {
                if (p.Body == null || p == held || p.RideCar >= 0 || p.RemoteBy >= 0 || now - p.WorldAt < 2.5f) continue;
                if ((p.Body.position - cp).sqrMagnitude > 49f || p.Body.transform.root == car.transform) continue;
                if (VehicleSync.CarUnder(p.Body) != car) continue;
                Transform t = p.Body.transform;
                Quaternion r = rot * (inv * p.Body.rotation);
                t.position = pos + rot * (inv * (p.Body.position - cp));
                t.rotation = r;
                if (!p.Body.isKinematic) p.Body.velocity = vel;
                n++;
            }
            if (n > 0) Log.Info("objets : " + n + " objets suivent " + car.name + " recalee");
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
            int car = state == 3 ? r.U8() : -1;   // transporte : pos et rot sont dans la voiture 'car'
            // Au repos dans une voiture : sa pose dans la voiture 'car' suit (InCarPose).
            bool inCar = state == 0 && r.More;
            bool rel = (state == 1 || state == 2) && r.More;   // tenu / lache depuis une voiture : pose dans son repere
            Vector3 lp = Vector3.zero;
            Quaternion lr = Quaternion.identity;
            if (inCar || rel) { car = r.U8(); lp = r.Vec(); lr = r.Quat(); }
            if (Session.IsHost)
            {
                var w = new NetWriter(Msg.Prop).U8(who).Str(id).U8(state).Vec(pos).Quat(rot).Vec(vel);
                if (state == 3) w.U8(car);
                if (inCar || rel) w.U8(car).Vec(lp).Quat(lr);
                Session.Broadcast(w, state == 0, who);
            }
            Prop p;
            float now = Time.realtimeSinceStartup, until;
            if ((!props.TryGetValue(id, out p) || p.Body == null) && now - lastForced > 1f && !(unknown.TryGetValue(id, out until) && now < until))
            {
                lastForced = now;   // objet tout neuf (achat...) : nouveau passage
                Scan();
                props.TryGetValue(id, out p);
                // Toujours inconnu (objet absent ici) : pas d'autre releve force pour lui avant 10 s (messages a 8-15/s).
                if (p == null || p.Body == null) unknown[id] = now + 10f;
            }
            if (p == null || p.Body == null || p == held) return;
            p.RemoteAt = now;
            if (state == 3) { Glue(p, who, car, pos, rot, vel); return; }
            Unride(p);
            p.Pos = pos; p.Rot = rot; p.Vel = vel;
            p.RelCar = rel && VehicleSync.CarBody(car) != null ? car : -1;
            p.RelPos = lp; p.RelRot = lr;
            if (state != 0)
            {
                if (p.RemoteBy != who) Log.Info("piece " + id + " deplacee par #" + who);
                p.RemoteBy = who;
                p.LastRemote = Time.realtimeSinceStartup;
                SetKinematic(p, true);
                return;
            }
            // Au repos, mais pose dans la voiture qu'on conduit : c'est nous qui le transportons (la pose de
            // l'autre est prise sur sa copie, en retard) ; seulement la fin de son deplacement.
            int ci = inCar ? car : VehicleSync.CarIndex(VehicleSync.CarUnder(p.Body));
            if (VehicleSync.DrivenHere(ci))
            {
                if (p.RemoteBy == who)
                {
                    // Pose dans la voiture recue (repere de la voiture) : posee la sur la vraie voiture, a sa vitesse.
                    Rigidbody dc = inCar ? VehicleSync.CarBody(car) : null;
                    if (dc != null) { Transform dt = dc.transform; p.Body.transform.position = dt.position + dt.rotation * lp; p.Body.transform.rotation = dt.rotation * lr; }
                    p.RemoteBy = -1; p.Vel = VehicleSync.CarVelocity(ci); SetKinematic(p, false);
                }
                return;
            }
            // Pose dans une voiture : sur notre voiture, ou qu'elle soit ici (son recalage l'emmenera ensuite).
            Rigidbody cb = inCar ? VehicleSync.CarBody(car) : null;
            if (cb != null) { Transform ct = cb.transform; pos = ct.position + ct.rotation * lp; rot = ct.rotation * lr; }
            p.WorldAt = cb != null ? 0f : now;
            // Au repos : fin du deplacement distant, ou recalage par l'hote.
            p.RemoteBy = -1;
            SetKinematic(p, false);
            Transform t = p.Body.transform;
            if ((t.position - pos).sqrMagnitude > 0.09f || Quaternion.Angle(t.rotation, rot) > 15f)
            {
                t.position = pos; t.rotation = rot;
                p.Body.velocity = vel;
                VehicleSync.Forget(p.Body);   // voiture sous l'objet a revoir a sa nouvelle place
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

        // Essais : pose l'objet 'id' dans la voiture, a 'local' (repere de la voiture), immobile par rapport a elle.
        public static string TestPlace(string id, Rigidbody car, Vector3 local)
        {
            Prop p;
            if (car == null) return "voiture absente";
            if (!props.TryGetValue(id, out p) || p.Body == null) return "objet " + id + " absent";
            Transform t = p.Body.transform;
            t.position = car.position + car.rotation * local;
            t.rotation = car.rotation;
            if (!p.Body.isKinematic) { p.Body.velocity = car.velocity; p.Body.angularVelocity = Vector3.zero; p.Body.WakeUp(); }
            return id + " pose dans " + car.name + " en " + local.ToString("F2") + (p.Body.isKinematic ? " (cinematique)" : "");
        }

        // Essais : objets suivis poses dans la voiture (pose dans son repere ; colle = transport recu,
        // envoye = transporte par nous), et combien d'autres a moins de 7 m.
        public static string InCar(Rigidbody car)
        {
            var sb = new System.Text.StringBuilder();
            Transform ct = car.transform;
            Quaternion inv = Quaternion.Inverse(ct.rotation);
            int outside = 0;
            foreach (Prop p in props.Values)
            {
                if (p.Body == null || p.Body.transform.root == ct) continue;
                Vector3 d = p.Body.transform.position - ct.position;
                if (d.sqrMagnitude > 49f) continue;
                if (VehicleSync.CarUnder(p.Body) != car) { outside++; continue; }
                sb.Append(p.Id).Append(' ').Append((inv * d).ToString("F2"))
                  .Append(p.RideCar >= 0 ? " colle" : p.RideOut >= 0 ? " envoye" : "").Append(" ; ");
            }
            return (sb.Length > 0 ? sb.ToString() : "rien ; ") + outside + " autres autour";
        }

        // Essais ([Test] Autotest=coffre-objets : l'hote conduit ; coffre-objets-invite : l'invite conduit) :
        // l'hote pose les objets [Test] TestPlace (cles separees par ';', '~' = partie de cle, sinon cle ou debut
        // d'ID ; '@x,y,z' = pose dans le repere de la voiture, coffre de la SORBET par defaut, cote a cote) dans
        // TestVoiture a 20 s. Le conducteur monte a 15-22 s, la voiture est poussee a 8 m/s de 25 a 37 s. Chacun
        // note chaque seconde la pose dans la voiture de ce qui y est pose : moins de 5 cm d'ecart entre les
        // deux journaux, toujours dedans a 45 s.
        // coffre-pousse : personne au volant ; l'hote pousse la voiture garee a 1,5 m/s de 28 a 29,5 s (recalage
        // chez l'invite, objets recales aussi) : chez l'invite la pose dans la voiture ne fait pas un 2e bond
        // (meme pose que chez l'hote a 5 cm pres apres 33 s).
        static int testStep;
        static float testLog;
        static bool testPlaced;

        public static void Test(string mode, float t)
        {
            bool push = mode == "coffre-pousse";
            if (mode != "coffre-objets" && mode != "coffre-objets-invite" && !push) return;
            string name = Config.Get("Test", "TestVoiture", "SORBET(190-200psi)");
            Rigidbody car = VehicleSync.Body(name);
            bool driver = !push && Session.IsHost != (mode == "coffre-objets-invite");
            if (push && Session.IsHost && car != null && t > 28f && t < 29.5f)
            {
                Vector3 f = car.transform.forward; f.y = 0;
                car.velocity = f.normalized * 1.5f + Vector3.up * Mathf.Min(car.velocity.y, 0f);
            }
            if (driver && t > 15f && testStep == 0) { testStep = 1; Log.Info("autotest : " + VehicleSync.TestEnter(name, false)); }
            if (driver && t > 22f && testStep == 1) { testStep = 2; Log.Info("autotest : volant -> " + VehicleSync.TestEnter(name, true)); }
            if (driver && car != null && t > 25f && t < 37f)
            {
                Vector3 f = car.transform.forward; f.y = 0;
                // (accelere a 2 m/s2 jusqu'a 8 m/s, freine de meme : un demarrage instantane ejecte tout chargement)
                float v = Mathf.Min(8f, Mathf.Min((t - 25f) * 2f, (37f - t) * 2f));
                car.velocity = f.normalized * v + Vector3.up * Mathf.Min(car.velocity.y, 0f);
            }
            if (Session.IsHost && car != null && t > 20f && !testPlaced)
            {
                testPlaced = true;
                string[] keys = Config.Get("Test", "TestPlace", "~gasoline").Split(';');
                for (int i = 0; i < keys.Length; i++)
                {
                    string[] ka = keys[i].Trim().Split('@');
                    string k = ka[0];
                    if (k.Length == 0) continue;
                    Vector3 at = ka.Length > 1 ? ParseVec(ka[1]) : new Vector3((i - (keys.Length - 1) * 0.5f) * 0.35f, 0.45f, -1.5f);
                    string id = k.StartsWith("~") ? FindKey(k.Substring(1)) : props.ContainsKey(k) ? k : NearestId(k, car.position) ?? k;
                    Log.Info("autotest : " + TestPlace(id, car, at));
                }
            }
            if (car != null && t > 20f && t < 60f && t - testLog >= 1f) { testLog = t; Log.Info("autotest : dans " + name + " : " + InCar(car)); }
        }

        static Vector3 ParseVec(string s)
        {
            string[] c = s.Split(',');
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            return c.Length < 3 ? Vector3.zero : new Vector3(float.Parse(c[0], ci), float.Parse(c[1], ci), float.Parse(c[2], ci));
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

        // Essais : objets suivis dont l'ID commence par 'prefix', et ou ils sont ('?' : disparus ici).
        public static string Ids(string prefix)
        {
            var sb = new System.Text.StringBuilder();
            foreach (Prop p in props.Values)
                if (p.Id.StartsWith(prefix)) sb.Append(p.Id).Append(' ').Append(p.Body != null ? p.Body.position.ToString("F1") : "?").Append(" ; ");
            return sb.Length > 0 ? sb.ToString() : "aucun";
        }

        public static bool Holding { get { return held != null; } }

        public static string Where(string id)
        {
            Prop p;
            return props.TryGetValue(id, out p) && p.Body != null ? p.Body.position.ToString("F2") : "?";
        }
    }
}
