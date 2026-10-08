using System.Collections.Generic;
using MWCoop.Net;
using UnityEngine;

namespace MWCoop
{
    // Tout ce qui bouge ou s'allume dans les vehicules, vu par tous : aiguilles du tableau de bord,
    // voyants, feux, clignotants, vitres, essuie-glaces, frein a main, leviers, volant, pedales,
    // ceintures, pare-soleil, retroviseurs, lecteur CD, givre... Pour chaque voiture, on retient les
    // objets dont le nom l'indique ; celui qui conduit (ou, a l'arret, le joueur a cote) envoie 10 fois
    // par seconde ce qui a change : pose locale (objets qui bougent) ou visibilite (objets qui
    // s'allument). Chez les autres, les valeurs recues sont reappliquees apres la logique du jeu
    // (LateUpdate), tant que la voiture est conduite par l'autre, ou 3 s apres le dernier message.
    // Voitures : celles de VehicleSync (taxi sous JOBS compris), par leur cle ; releve toutes les 30 s et des que
    // sa liste change (voiture activee plus tard).
    // Maillages "New"/"Damaged"/"Broken" (pieces usees ou cassees) suivis comme des lampes. Le bras du chargeur du
    // KEKMET et les appareils de l'atelier sont a Garage (son LateUpdate est appele d'ici, apres la logique du jeu).
    public static class CarVisuals
    {
        static readonly string[] PoseWords = { "needle", "glasspivot", "windowpivot", "lever", "pivot_brake", "handbrake", "belt", "steering",
            "pedal", "wiper", "knob", "switch", "sunvisor", "mirror", "sled", "gear", "key", "hand_brake", "button", "heater", "dial", "slider" };
        static readonly string[] ShowWords = { "light", "beam", "indicator", "lamp", "frost", "frozen", "heater", "belt", "lock", "key",
            "sunvisors", "marker", "brakes", "blinker", "turnsignal", "reverse" };   // reverse : feux de recul
        static readonly string[] SkipWords = { "wheel", "tire", "rim", "hubcap", "spindle", "driver", "passenger", "headpivot" };
        // Maillages echanges a l'usure ou au choc ("New" / "Damaged" sous une piece montee : bloc moteur, phares...) :
        // nom exact (un mot cle "new" prendrait n'importe quoi).
        static readonly HashSet<string> ShowNames = new HashSet<string> { "new", "damaged", "broken" };

        class Item
        {
            public string Key; public Transform T; public bool Pose, Show;
            public Vector3 Pos; public Quaternion Rot; public bool Active;           // dernier connu (envoye ou recu)
            public bool Held; public Vector3 HeldPos; public Quaternion HeldRot; public bool HeldActive; public bool HeldShow;
            public float HeldAt;
            public bool Shown; public Vector3 ShowPos; public Quaternion ShowRot;   // pose montree : glisse vers la recue
            public bool Gear;                                                     // levier de vitesses : passage anime
            public float ShiftAt = -1f; public Quaternion ShiftFrom, ShiftTo;      // passage en cours (debut, de, vers)
        }
        // Levier de vitesses distant : un rapport recu ne fait plus sauter le levier (avant : ~60 ms, « pas d'animation »,
        // retour d'un joueur 08/10). La main de l'avatar part vers le pommeau tout de suite (LastShift), le levier attend
        // 0,15 s puis glisse vers le nouveau rapport en 0,35 s.
        const float ShiftDelay = 0.15f, ShiftTime = 0.35f;
        static readonly Dictionary<Transform, float> lastShift = new Dictionary<Transform, float>();
        public static float LastShift(Transform carRoot) { float t; return carRoot != null && lastShift.TryGetValue(carRoot, out t) ? t : -1f; }
        class Car { public string Name; public Transform Root; public List<Item> Items = new List<Item>(); public Dictionary<string, Item> ByKey = new Dictionary<string, Item>(); }

        static readonly Dictionary<string, Car> cars = new Dictionary<string, Car>();
        static float nextScan = -1, nextSend;
        static Transform player;
        static int sentTotal, gen = -1;

        public static void OnLevelLoaded()
        {
            cars.Clear(); player = null; gen = -1;
            nextScan = PlayerSync.InGame ? Time.realtimeSinceStartup + 13f : -1;
        }

        static bool Has(string n, string[] words) { foreach (string w in words) if (n.Contains(w)) return true; return false; }

        static void Scan()
        {
            int total = 0;
            gen = VehicleSync.Generation;
            for (int ci = 0; ci < VehicleSync.LocalCount; ci++)
            {
                Rigidbody rb = VehicleSync.LocalBody(ci);
                if (rb == null) continue;
                var c = new Car { Name = VehicleSync.LocalKey(ci), Root = rb.transform };
                var seen = new Dictionary<string, int>();
                foreach (Transform t in rb.GetComponentsInChildren<Transform>(true))
                {
                    if (t == rb.transform || t.GetComponent<Rigidbody>() != null) continue;
                    string n = t.name.ToLowerInvariant();
                    if (Has(n, SkipWords)) continue;
                    bool pose = Has(n, PoseWords), show = Has(n, ShowWords) || ShowNames.Contains(n) || t.GetComponent<Light>() != null;   // toute lampe
                    // Levier de vitesses de la SORBET : c'est Gearstick/Pivot qui tourne (son automate Movemement), pas Gearstick.
                    if (!pose && n == "pivot" && t.parent != null && t.parent.name.ToLowerInvariant().Contains("gear")) pose = true;
                    if (!pose && !show) continue;
                    string rel = VehicleSync.RelPath(rb.transform, t);
                    int k; seen.TryGetValue(rel, out k); seen[rel] = k + 1;
                    var it = new Item { Key = rel + "#" + k, T = t, Pose = pose, Show = show, Pos = t.localPosition, Rot = t.localRotation, Active = t.gameObject.activeSelf,
                                        Gear = pose && (n.Contains("gear") || (n == "pivot" && t.parent != null && t.parent.name.ToLowerInvariant().Contains("gear"))) };
                    c.Items.Add(it);
                    c.ByKey[it.Key] = it;
                }
                // Une voiture deja connue garde ses valeurs tenues (nouveau passage : pieces montees depuis).
                Car old;
                if (cars.TryGetValue(c.Name, out old))
                    foreach (Item it in c.Items) { Item o; if (old.ByKey.TryGetValue(it.Key, out o) && o.T == it.T) { it.Held = o.Held; it.HeldPos = o.HeldPos; it.HeldRot = o.HeldRot; it.HeldActive = o.HeldActive; it.HeldShow = o.HeldShow; it.HeldAt = o.HeldAt; } }
                cars[c.Name] = c;
                total += c.Items.Count;
            }
            Log.Info("tableaux de bord et equipements : " + total + " elements suivis sur " + cars.Count + " vehicules");
        }

        // Ce joueur doit-il envoyer l'etat de cette voiture ? Il la conduit, ou personne ne la conduit
        // et il est a moins de 6 m.
        static bool Sender(Car c)
        {
            if (ReferenceEquals(VehicleSync.LocalDrivingRoot, c.Root)) return true;
            if (VehicleSync.RemotelyDriven(c.Root)) return false;
            return player != null && (c.Root.position - player.position).sqrMagnitude < 36f;
        }

        public static void Update()
        {
            if (!Session.Active || nextScan < 0) return;
            float now = Time.realtimeSinceStartup;
            // Liste des voitures changee (voiture activee, corps recree) apres le premier releve : releve tout de suite.
            if (now >= nextScan || (gen >= 0 && gen != VehicleSync.Generation)) { nextScan = now + 30f; Scan(); }
            if (player == null) { GameObject p = GameObject.Find("PLAYER"); if (p == null) return; player = p.transform; }
            if (now < nextSend || Session.RemoteCount == 0) return;
            // 20 fois par seconde pour la voiture qu'on conduit (volant, aiguilles : retour d'un joueur, "le volant tourne
            // avec du retard"), 10 sinon ; chez les autres, chaque pose recue est rejointe en douceur (LateUpdate).
            nextSend = now + (VehicleSync.LocalDrivingRoot != null ? 0.05f : 0.1f);
            foreach (Car c in cars.Values)
            {
                if (c.Root == null || !c.Root.gameObject.activeInHierarchy || !Sender(c)) continue;
                NetWriter w = null;
                foreach (Item it in c.Items)
                {
                    if (it.T == null || it.Held) continue;
                    bool act = it.T.gameObject.activeSelf;
                    bool showCh = it.Show && act != it.Active;
                    bool poseCh = it.Pose && (Quaternion.Angle(it.T.localRotation, it.Rot) > 0.5f || (it.T.localPosition - it.Pos).sqrMagnitude > 4e-6f);
                    if (!showCh && !poseCh) continue;
                    it.Active = act; it.Pos = it.T.localPosition; it.Rot = it.T.localRotation;
                    if (w != null && w.Length + 40 + it.Key.Length > 1000) { Session.SendAll(w, false); w = null; }
                    if (w == null) w = new NetWriter(Msg.CarVisual).U8(Session.LocalId).Str(c.Name);
                    w.Str(it.Key).U8((it.Pose ? 1 : 0) | (it.Show ? 2 : 0) | (act ? 4 : 0));
                    if (it.Pose) w.Vec(it.Pos).Quat(it.Rot);
                    if (++sentTotal % 500 == 1) Log.Info("tableaux de bord : " + sentTotal + " changements envoyes (" + c.Name + it.Key + ")");
                }
                if (w != null) Session.SendAll(w, false);
            }
        }

        // Apres la logique du jeu : les valeurs recues l'emportent.
        public static void LateUpdate()
        {
            if (!Session.Active) return;
            // Atelier (crics, pont, palan, chargeur) : memes poses tenues apres la logique du jeu.
            try { Garage.LateUpdate(); } catch (System.Exception e) { if (Time.frameCount % 600 == 0) Log.Warn("atelier (poses) : " + e.Message); }
            float now = Time.realtimeSinceStartup;
            foreach (Car c in cars.Values)
            {
                if (c.Root == null) continue;
                bool driven = VehicleSync.RemotelyDriven(c.Root);
                foreach (Item it in c.Items)
                {
                    if (!it.Held || it.T == null) continue;
                    if (!driven && now - it.HeldAt > 3f) { it.Held = false; it.Shown = false; it.Active = it.T.gameObject.activeSelf; it.Pos = it.T.localPosition; it.Rot = it.T.localRotation; continue; }
                    if (it.Pose)
                    {
                        // ~60 ms pour rejoindre la pose recue (envoyee toutes les 50 a 100 ms) : plus de saccades
                        // (Levier : part de sa pose du moment, le premier rapport recu est anime lui aussi.)
                        if (!it.Shown) { it.Shown = true; it.ShowPos = it.Gear ? it.T.localPosition : it.HeldPos; it.ShowRot = it.Gear ? it.T.localRotation : it.HeldRot; }
                        if (it.Gear)
                        {
                            // Nouveau rapport : passage minute (la main d'abord, puis le levier).
                            if (Quaternion.Angle(it.HeldRot, it.ShiftAt >= 0f ? it.ShiftTo : it.ShowRot) > 3f)
                            {
                                it.ShiftFrom = it.ShowRot; it.ShiftTo = it.HeldRot; it.ShiftAt = now;
                                lastShift[c.Root] = now;
                            }
                            if (it.ShiftAt >= 0f)
                            {
                                float u = Mathf.Clamp01((now - it.ShiftAt - ShiftDelay) / ShiftTime);
                                u = u * u * (3f - 2f * u);
                                it.ShowRot = Quaternion.Slerp(it.ShiftFrom, it.ShiftTo, u);
                                if (u >= 1f) it.ShiftAt = -1f;
                            }
                            else it.ShowRot = Quaternion.Slerp(it.ShowRot, it.HeldRot, 1f - Mathf.Exp(-Time.deltaTime * 16f));
                            it.ShowPos = Vector3.Lerp(it.ShowPos, it.HeldPos, 1f - Mathf.Exp(-Time.deltaTime * 16f));
                        }
                        else
                        {
                            float k = 1f - Mathf.Exp(-Time.deltaTime * 16f);
                            it.ShowPos = Vector3.Lerp(it.ShowPos, it.HeldPos, k);
                            it.ShowRot = Quaternion.Slerp(it.ShowRot, it.HeldRot, k);
                        }
                        it.T.localPosition = it.ShowPos; it.T.localRotation = it.ShowRot;
                    }
                    if (it.HeldShow && it.T.gameObject.activeSelf != it.HeldActive) it.T.gameObject.SetActive(it.HeldActive);
                }
            }
        }

        public static void OnMessage(Peer from, NetReader r)
        {
            int who = r.U8();
            if (Session.IsHost) who = from.Id;
            string car = r.Str();
            NetWriter relay = Session.IsHost ? new NetWriter(Msg.CarVisual).U8(who).Str(car) : null;
            Car c;
            cars.TryGetValue(car, out c);
            float now = Time.realtimeSinceStartup;
            while (r.More)
            {
                string key = r.Str();
                int fl = r.U8();
                Vector3 pos = Vector3.zero; Quaternion rot = Quaternion.identity;
                if ((fl & 1) != 0) { pos = r.Vec(); rot = r.Quat(); }
                if (relay != null) { relay.Str(key).U8(fl); if ((fl & 1) != 0) relay.Vec(pos).Quat(rot); }
                Item it;
                if (c == null || !c.ByKey.TryGetValue(key, out it) || it.T == null) continue;
                it.Held = true; it.HeldAt = now;
                if ((fl & 1) != 0) { it.HeldPos = pos; it.HeldRot = rot; }
                if ((fl & 2) != 0) { it.HeldShow = true; it.HeldActive = (fl & 4) != 0; }
            }
            if (relay != null) Session.Broadcast(relay, false, who);
        }

        // Essais : bouge / allume le premier element de 'car' dont la cle contient 'part'.
        public static string Test(string car, string part)
        {
            Car c;
            if (!cars.TryGetValue(car, out c)) return "voiture " + car + " inconnue (" + cars.Count + ")";
            foreach (Item it in c.Items)
            {
                if (it.T == null || !it.Key.Contains(part)) continue;
                if (it.Pose) it.T.localRotation = it.T.localRotation * Quaternion.Euler(0f, 0f, 40f);
                else it.T.gameObject.SetActive(!it.T.gameObject.activeSelf);
                return car + it.Key + (it.Pose ? " tourne de 40 deg" : " visible " + it.T.gameObject.activeSelf);
            }
            return "rien pour " + part;
        }

        // Essais : elements suivis sur la voiture de cle 'car' (-1 : voiture pas relevee).
        public static int CountFor(string car)
        {
            Car c;
            return cars.TryGetValue(car, out c) ? c.Items.Count : -1;
        }

        public static string State(string car, string part)
        {
            Car c;
            if (!cars.TryGetValue(car, out c)) return "?";
            foreach (Item it in c.Items)
                if (it.T != null && it.Key.Contains(part))
                    return car + it.Key + " rot " + it.T.localEulerAngles.ToString("F0") + " visible " + it.T.gameObject.activeSelf + (it.Held ? " (tenu)" : "");
            return "?";
        }
    }
}
