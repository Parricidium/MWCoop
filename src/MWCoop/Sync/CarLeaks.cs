using System.Collections.Generic;
using MWCoop.Net;
using UnityEngine;

namespace MWCoop
{
    // Fuites et vapeur des voitures (retour de kakabaka123, 10/10 : « synchroniser l'ebullition du radiateur, et tout liquide
    // qui s'ecoule de la Rivett »). Le jeu les montre par des objets que la simulation de la voiture allume et place :
    //  - Systems/Leaks/* (LeakOil, LeakOilDrain, LeakCoolant, LeakFuel, LeakBrake...) : flaque (« mesh », qui grandit) et
    //    particules, posees sous la fuite (vidange, durite, joint) ;
    //  - <voiture>/.../SteamFountain* : la vapeur du radiateur qui bout.
    // Chez les autres, la simulation de la copie ne les allumait pas (ou autrement). L'autorite de la voiture (conducteur,
    // moteur laisse tournant, sinon l'hote ; la CORRIS -- la SATSUMA sur My Summer Car -- pour Systems/Leaks) envoie leur etat a chaque changement et toutes
    // les 5 s (@fuite : allume, pose, taille de la flaque) ; ailleurs il est tenu tel quel.
    public static class CarLeaks
    {
        class Item
        {
            public string Key; public Transform T, Mesh; public Rigidbody Car;
            public bool On; public Vector3 Pos, Scale; public Quaternion Rot;       // dernier envoye
            public bool RHas, ROn; public Vector3 RPos, RScale; public Quaternion RRot; // recu
        }
        static readonly List<Item> items = new List<Item>();
        static readonly Dictionary<string, Item> byKey = new Dictionary<string, Item>();
        static float nextScan, nextSend, nextFull;
        static int leaksLater;   // (MSC : SATSUMA pas encore enregistree au balayage, Systems/Leaks rattache au suivant ; essais restants)

        public static void OnLevelLoaded() { items.Clear(); byKey.Clear(); leaksLater = 0; nextScan = PlayerSync.InGame ? Time.realtimeSinceStartup + 20f : 0f; }

        static void Add(Transform t, Rigidbody car)
        {
            string k = Recon.Path(t);
            if (byKey.ContainsKey(k)) return;
            var it = new Item { Key = k, T = t, Car = car, Mesh = t.Find("mesh") };
            items.Add(it); byKey[k] = it;
        }

        static void Scan()
        {
            int before = items.Count;
            GameObject leaks = Game.FindAny("Systems/Leaks");
            // (audit MSC 02/11 : sur My Summer Car, Systems/Leaks (LeakOil, LeakOilDrain, LeakFuel, LeakCoolant, LeakBrake, LeakClutch) est
            // tenu par la SATSUMA -- SATSUMA/CarSimulation/Engine/Oil, coupe par MecaMsc hors de son autorite, et ses pieces CARPARTS
            // (carter, reservoir). Rattache a la CORRIS, absente : l'hote seul faisait foi. Invite au volant d'une SATSUMA qui fuit, la
            // flaque n'existait que chez lui, ou il etait force a l'etat fige de l'hote. Rattache a la SATSUMA : son autorite (le
            // conducteur, sinon l'hote), la meme regle que MecaMsc. [Test] FuitesSatsuma=0 : comme avant.)
            bool sats = Game.Msc && Config.GetInt("Test", "FuitesSatsuma", 1) != 0;
            Rigidbody owner = VehicleSync.Body(sats ? "SATSUMA" : "CORRIS");
            bool wait = false;
            if (sats && leaks != null && owner == null)
            {
                if (leaksLater == 0) leaksLater = 20;   // (20 balayages, 5 min ; ensuite a l'hote, comme avant)
                wait = --leaksLater > 0;
            }
            if (!wait)
            {
                leaksLater = 0;
                if (leaks != null) foreach (Transform c in leaks.transform) Add(c, owner);
            }
            // Vapeur : tout SteamFountain* sous une voiture suivie.
            foreach (string car in Game.Msc ? new[] { "SATSUMA", "HAYOSIKO", "KEKMET", "GIFU", "FERNDALE", "RCO_RUSCKO12", "FLATBED" } : new[] { "CORRIS", "SORBET", "BACHGLOTZ", "KEKMET", "GIFU", "MACHTWAGEN" })
            {
                Rigidbody rb = VehicleSync.Body(car);
                if (rb == null) continue;
                foreach (Transform t in rb.GetComponentsInChildren<Transform>(true))
                    if (t.name.StartsWith("SteamFountain")) Add(t, rb);
            }
            if (items.Count > 0 && items.Count != before) Log.Info("fuites et vapeur : " + items.Count + " suivies (" + (leaks != null && !wait ? leaks.transform.childCount + " fuites" + (owner != null ? " a " + owner.name : "") + ", " : "") + "vapeur des radiateurs)");
        }

        static bool Mine(Item it) { return it.Car == null ? Session.IsHost : VehicleSync.Authority(it.Car) == Session.LocalId; }

        public static void Update()
        {
            if (!Session.Active || !PlayerSync.InGame) return;
            float now = Time.realtimeSinceStartup;
            if (items.Count == 0 || leaksLater > 0) { if (now >= nextScan) { nextScan = now + 15f; Scan(); } if (items.Count == 0) return; }
            if (now >= nextSend && Session.RemoteCount > 0)
            {
                nextSend = now + 0.25f;
                bool full = now >= nextFull;
                if (full) nextFull = now + 5f;
                NetWriter w = null; int n = 0;
                foreach (Item it in items)
                {
                    if (it.T == null || !Mine(it)) continue;
                    bool on = it.T.gameObject.activeSelf;
                    Vector3 sc = it.Mesh != null ? it.Mesh.localScale : Vector3.one;
                    bool changed = on != it.On || (on && ((it.T.position - it.Pos).sqrMagnitude > 0.0004f || (sc - it.Scale).sqrMagnitude > 0.0001f));
                    if (!changed && !(full && on)) continue;
                    if (changed && on != it.On) Log.Info("fuites et vapeur : " + it.T.name + (on ? " allumee" : " eteinte") + " ici");
                    it.On = on; it.Pos = it.T.position; it.Rot = it.T.rotation; it.Scale = sc;
                    if (w == null) w = new NetWriter(Msg.Job).U8(Session.LocalId).Str("@fuite");
                    w.Str(it.Key).Bool(on).Vec(it.Pos).Quat(it.Rot).Vec(sc);
                    if (++n >= 12) { Session.SendAll(w.Str(""), true); w = null; n = 0; }
                }
                if (w != null) Session.SendAll(w.Str(""), true);
            }
            // Tenu tel que l'autorite le montre (la simulation de la copie le rallumerait ou l'eteindrait).
            foreach (Item it in items)
            {
                if (it.T == null || !it.RHas || Mine(it)) continue;
                if (it.T.gameObject.activeSelf != it.ROn) it.T.gameObject.SetActive(it.ROn);
                if (!it.ROn) continue;
                it.T.position = it.RPos; it.T.rotation = it.RRot;
                if (it.Mesh != null) it.Mesh.localScale = it.RScale;
            }
        }

        public static void OnRemote(int who, NetReader r)
        {
            if (items.Count == 0) Scan();
            for (int i = 0; i < 64; i++)
            {
                string k = r.Str();
                if (k.Length == 0) break;
                bool on = r.Bool(); Vector3 p = r.Vec(); Quaternion q = r.Quat(); Vector3 sc = r.Vec();
                Item it;
                if (!byKey.TryGetValue(k, out it)) continue;
                if (on != it.ROn || !it.RHas) Log.Info("fuites et vapeur : " + it.T.name + (on ? " allumee" : " eteinte") + " par #" + who);
                it.RHas = true; it.ROn = on; it.RPos = p; it.RRot = q; it.RScale = sc;
            }
        }

        // [Test] Autotest=fuite : l'hote allume la vidange (LeakOilDrain) a 30 s, la flaque grandit, l'eteint a 45 s ;
        // chacun note son etat a 38 et 52 s.
        static int testStep;
        public static void Test(string mode, float t)
        {
            if (mode == "fuiteconducteur") { TestDriver(t); return; }
            if (mode != "fuite") return;
            Item it = null;
            foreach (Item x in items) if (x.T != null && x.T.name == "LeakOilDrain") it = x;
            if (it == null) return;
            if (Session.IsHost)
            {
                Transform pl = Game.PlayerT;
                if (t > 30f && testStep == 0) { testStep = 1; it.T.position = pl.position + pl.forward * 1.5f; it.T.gameObject.SetActive(true); }
                if (testStep == 1 && it.Mesh != null) it.Mesh.localScale = Vector3.one * Mathf.Clamp(0.2f + (t - 30f) * 0.1f, 0.2f, 1.5f);
                if (t > 45f && testStep == 1) { testStep = 2; it.T.gameObject.SetActive(false); }
            }
            if (t > 38f && testStep % 10 < 3 && (testStep == 1 || !Session.IsHost && testStep == 0)) { Log.Info("autotest : fuite LeakOilDrain " + (it.T.gameObject.activeSelf ? "allumee en " + it.T.position.ToString("F1") + " flaque " + (it.Mesh != null ? it.Mesh.localScale.x.ToString("F2") : "?") : "eteinte")); testStep += 10; }
            if (t > 52f && testStep < 20 && testStep >= 10) { Log.Info("autotest : fuite LeakOilDrain " + (it.T.gameObject.activeSelf ? "allumee" : "eteinte")); testStep += 10; }
        }

        // [Test] Autotest=fuiteconducteur (audit MSC 02/11, My Summer Car) : l'invite monte au volant de la SATSUMA (15/20 s) et
        // tient LeakOil allumee devant lui, flaque a 0,4, de 30 a 45 s (comme Engine/Oil chez l'autorite) ; chacun note a 38 et 52 s
        // l'etat de LeakOil, sa voiture de rattachement et qui fait foi. Attendu : allumee chez l'hote a la position de l'invite,
        // « a nous » chez l'invite seulement ; eteinte des deux cotes a 52 s.
        static int drvStep;
        static void TestDriver(float t)
        {
            Item it = null;
            foreach (Item x in items) if (x.T != null && x.T.name == "LeakOil") it = x;
            if (it == null) return;
            const string Car = "SATSUMA(557kg, 248)";
            if (!Session.IsHost)
            {
                if (t > 15f && drvStep == 0) { drvStep = 1; Log.Info("autotest : fuiteconducteur, " + VehicleSync.TestEnter(Car, false)); }
                if (t > 20f && drvStep == 1) { drvStep = 2; Log.Info("autotest : fuiteconducteur, volant -> " + VehicleSync.TestEnter(Car, true)); }
                if (t > 30f && t < 45f && drvStep >= 2)
                {
                    if (drvStep == 2) { drvStep = 3; Transform pl = Game.PlayerT; it.T.position = pl.position + pl.forward * 1.5f; }
                    if (!it.T.gameObject.activeSelf) it.T.gameObject.SetActive(true);
                    if (it.Mesh != null) it.Mesh.localScale = new Vector3(0.4f, 0.4f, it.Mesh.localScale.z);
                }
                if (t > 45f && drvStep == 3) { drvStep = 4; it.T.gameObject.SetActive(false); }
            }
            if (t > 38f && drvLog == 0 || t > 52f && drvLog == 1)
            {
                drvLog++;
                Log.Info("autotest : fuiteconducteur LeakOil " + (it.T.gameObject.activeSelf ? "allumee en " + it.T.position.ToString("F1") + " flaque " + (it.Mesh != null ? it.Mesh.localScale.x.ToString("F2") : "?") : "eteinte")
                         + ", rattachee a " + (it.Car != null ? it.Car.name : "aucune voiture (hote)") + (Mine(it) ? ", a nous" : ", tenue par l'autre"));
            }
        }
        static int drvLog;
    }
}
