using System;
using System.Collections.Generic;
using System.Reflection;
using MWCoop.Net;
using UnityEngine;

namespace MWCoop
{
    // Mod RAM 1500 (EdwardZNorton, demande de JD 13/10 : « la synchro de ce mod, au meme titre que la 300D »). Pick-up
    // « Ram1500 » sorti d'un AssetBundle (CarDynamics, Drivetrain, Axles, AxisCarController) mais pilote par les scripts du mod
    // (RamDrivingController : touche DrivingMode, joueur range sous PlayerPivotObject, PlayerCurrentVehicle ; IgnitionKnob,
    // HeadlightsKnob, RamDoors...), sans DriveTrigger du jeu. VehicleSync le suit comme les autres (conducteur reconnu par le
    // joueur range sous la voiture) ; sa configuration et sa sauvegarde (Mods/Config/Mod Settings/ram1500) partent avec le mod
    // chez les invites. Ici l'etat de ses commandes (classes du mod internes : tout par reflexion) :
    //  - etats discrets (cle et moteur, phares, clignotants/warning, essuie-glaces, plafonnier, portieres, hayon, capot, vitres,
    //    frein a main, levier, 4x4) : la plupart se manipulent par n'importe quel joueur (clic) -- celui chez qui l'un change
    //    l'envoie (@ram, cle + valeur), chacun l'applique par les methodes du mod (sons et animations compris) ; celui qui fait
    //    autorite sur le Ram (conducteur, sinon l'hote) renvoie tout toutes les 3 s (arrivants, rattrapage) ;
    //  - continu, de l'autorite seule (10/s) : regime, feux stop, rapport (feux de recul), carburant et temperature (toutes
    //    les 2 s). Ailleurs : regime pose en fin d'image avec le Drivetrain coupe (sa simulation ne l'ecrase pas ; le son et
    //    les cadrans le lisent), feux stop menes d'ici (BrakeLightsController coupe : le frein est a 0 sur la copie),
    //    consommation, surchauffe et panne d'ABS au hasard neutralisees (elles coupaient le moteur chez un seul joueur).
    // Pas encore : achat au parc du vendeur (camions tires au hasard chez chacun), cellule camping, radio, givre gratte,
    // compteur kilometrique, peinture.
    public static class ModRam1500
    {
        static GameObject ram;
        static Component drive;   // RamDrivingController
        static float findAt;

        public static bool Present { get { return ram != null; } }

        public static void OnLevelLoaded() { ram = null; drive = null; findAt = Time.realtimeSinceStartup + 10f; tStep = 0; slots = null; lot.Clear(); lotCamper = null; lotInit = false; lotLook = Time.realtimeSinceStartup + 12f; }

        const BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        static Type TypeOf(string name)
        {
            foreach (Assembly a in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (a.GetName().Name != "ram1500") continue;
                Type t = a.GetType(name, false);
                if (t != null) return t;
            }
            return null;
        }
        static Component Comp(string type, string goName)
        {
            Type t = TypeOf(type);
            if (t == null || ram == null) return null;
            foreach (Component c in ram.GetComponentsInChildren(t, true))
                if (goName == null || c.gameObject.name == goName) return c;
            return null;
        }
        static object Get(object o, string field) { if (o == null) return null; FieldInfo fi = o.GetType().GetField(field, F); if (fi != null) return fi.GetValue(o); PropertyInfo pi = o.GetType().GetProperty(field, F); return pi != null ? pi.GetValue(o, null) : null; }
        static void Set(object o, string field, object v)
        {
            if (o == null) return;
            FieldInfo fi = o.GetType().GetField(field, F);
            if (fi != null) { fi.SetValue(o, fi.FieldType.IsEnum ? Enum.ToObject(fi.FieldType, v) : v); return; }
            PropertyInfo pi = o.GetType().GetProperty(field, F);
            MethodInfo sm = pi != null ? pi.GetSetMethod(true) : null;
            if (sm != null) sm.Invoke(o, new[] { pi.PropertyType.IsEnum ? Enum.ToObject(pi.PropertyType, v) : v });
        }
        static object Call(object o, string method, params object[] args)
        {
            if (o == null) return null;
            foreach (MethodInfo m in o.GetType().GetMethods(F))
                if (m.Name == method && m.GetParameters().Length == args.Length)
                {
                    try { return m.Invoke(o, args); } catch (Exception e) { if (errs++ < 10) Log.Warn("ram : " + method + " : " + (e.InnerException ?? e).Message); return null; }
                }
            return null;
        }
        static int Int(object v) { return v == null ? 0 : v is bool ? ((bool)v ? 1 : 0) : Convert.ToInt32(v); }
        static int errs, logs;

        // ---------------------------------------------------------------- etats discrets
        class Slot { public string Key; public Func<int> GetV; public Action<int> Apply; public int Shared = int.MinValue; }
        static List<Slot> slots;
        static Component ign, lights, wipers, intLight, windows, handbrake, auto, manual, x4, fuel, mech, brakeLights;
        static Drivetrain dt;
        static AxisCarController acc;

        static bool Find()
        {
            if (ram != null && slots != null) return true;
            if (Time.realtimeSinceStartup < findAt) return false;
            findAt = Time.realtimeSinceStartup + 5f;
            slots = null;
            ram = GameObject.Find("Ram1500");
            if (ram == null || TypeOf("IgnitionKnob") == null) { ram = null; return false; }
            drive = Comp("RamDrivingController", null);
            ign = Comp("IgnitionKnob", null); lights = Comp("HeadlightsKnob", null); wipers = Comp("WiperAndRainManager", null);
            intLight = Comp("InteriorLightController", null); windows = Comp("WindowController", null); handbrake = Comp("HandbrakeKnob", null);
            auto = Comp("Automatic", null); manual = Comp("Manual", null); x4 = Comp("Ram4x4Controller", null);
            fuel = Comp("FuelController", null); mech = Comp("Mechanical", null); brakeLights = Comp("BrakeLightsController", null);
            dt = ram.GetComponent<Drivetrain>(); acc = ram.GetComponent<AxisCarController>();
            if (lights != null && acc != null) Set(lights, "carController", acc);   // (sinon FindObjectOfType : le clignotant se coupait sur le volant d'une AUTRE voiture)
            Build();
            Log.Info("mods : RAM 1500 present, " + slots.Count + " commandes suivies (" + (auto != null ? "automatique" : manual != null ? "manuelle" : "boite ?") + ")");
            return true;
        }

        static void Add(string key, Func<int> g, Action<int> a) { slots.Add(new Slot { Key = key, GetV = g, Apply = a }); }

        static void Build()
        {
            slots = new List<Slot>();
            if (ign != null)
            {
                // Cle : 0 retiree, 1 contact, 2 demarreur. Le demarrage se rejoue (StartCranking : sons, voyants, CarRunning).
                Add("cle", () => Int(Get(ign, "Stage")), v =>
                {
                    int cur = Int(Get(ign, "Stage"));
                    if (v == 1 && cur == 0) Call(ign, "InsertKey");
                    else if (v == 0 && cur != 0) Call(ign, "RemoveKey");
                    else if (v == 2 && !(bool)Get(ign, "isCranking")) { if (cur == 0) Call(ign, "InsertKey"); if (fuel != null && (float)Get(fuel, "fuelLevel") <= 0f) Set(fuel, "fuelLevel", 1f); Call(ign, "StartCranking"); }
                });
                Add("moteur", () => Int(Get(ign, "engineOn")), v =>
                {
                    bool on = (bool)Get(ign, "engineOn");
                    if (v == 1 && !on && !(bool)Get(ign, "isCranking")) { Set(ign, "engineOn", true); Set(ign, "Stage", 1); }
                    else if (v == 0 && on) Call(ign, "TurnOffEngine", true);
                });
            }
            if (lights != null)
            {
                Add("phares", () => Int(Get(lights, "lightingStage")) + 8 * Int(Get(lights, "highbeamsActive")), v =>
                {
                    Set(lights, "lightingStage", v & 7); Set(lights, "highbeamsActive", (v & 8) != 0);
                    Call(lights, "ApplyLightingChanges");
                });
                Add("clignos", () => Int(Get(lights, "lightState")), v =>
                {
                    int cur = Int(Get(lights, "lightState"));
                    if (cur == v) return;
                    if (cur == 1) Call(lights, "ToggleHazards"); else if (cur != 0) Call(lights, "TurnOffBlinker");
                    if (v == 1) Call(lights, "ToggleHazards");
                    else if (v == 3) Call(lights, "TurnOnBlinker", 3, 8f, false);
                    else if (v == 2) Call(lights, "TurnOnBlinker", 2, -8f, true);
                });
            }
            if (wipers != null)
                Add("essuie", () => Int(Get(wipers, "WiperState")), v => { int prev = Int(Get(wipers, "WiperState")); Set(wipers, "WiperState", v); Call(wipers, "UpdateStoppingState", prev); });
            if (intLight != null)
                Add("plafonnier", () => Int(Get(intLight, "intLightActive")) + 2 * Int(Get(intLight, "intLightManualOverride")), v =>
                { Set(intLight, "intLightActive", (v & 1) != 0); Set(intLight, "intLightManualOverride", (v & 2) != 0); Call(intLight, "ApplyIntLightChanges"); });
            foreach (string d in new[] { "doorLeft", "doorRight", "tailgate" })
            {
                Component door = Comp("RamDoors", d);
                if (door == null) continue;
                Component dd = door;
                Add(d, () => Int(Get(dd, "doorOpen")), v =>
                {
                    if (Int(Get(dd, "doorOpen")) == v) return;
                    ((MonoBehaviour)dd).StopAllCoroutines();
                    Call(dd, v == 1 ? "StartOpenNPC" : "StartCloseNPC");
                    if (intLight != null) Call(intLight, "SetIntLightFromDoors", AnyCabDoorOpen(dd, v == 1));
                });
            }
            Component hood = Comp("RamDoors", "Hood");
            if (hood != null)
                Add("capot", () => Int(Get(hood, "hoodState")), v =>
                {
                    // Locked 0 -> Popped 1 -> Open 2 -> Locked : release (0->1), poignee (1->2, 2->0)
                    for (int i = 0; i < 3 && Int(Get(hood, "hoodState")) != v; i++)
                        Call(hood, Int(Get(hood, "hoodState")) == 0 ? "PressHoodRelease" : "PressHoodHandle");
                });
            if (windows != null)
            {
                Add("vitreG", () => Int(Get(windows, "leftIteration")), v => { Set(windows, "leftIteration", v); Call(windows, "ApplyLeftWindow"); });
                Add("vitreD", () => Int(Get(windows, "rightIteration")), v => { Set(windows, "rightIteration", v); Call(windows, "ApplyRightWindow"); });
            }
            if (handbrake != null)
                Add("frein", () => Int(Get(handbrake, "brakeOn")), v => Set(handbrake, "brakeOn", v == 1));
            if (auto != null)
                Add("levier", () => Int(Get(auto, "mode")), v => Set(auto, "mode", v));
            else if (manual != null)
                Add("levier", () => Int(Get(manual, "currentGear")), v =>
                {
                    object old = Get(manual, "currentGear");
                    Set(manual, "currentGear", v);
                    Call(manual, "BeginLeverPath", old, Get(manual, "currentGear"));
                    Set(manual, "displayGear", v);
                });
            if (x4 != null)
                Add("4x4", () => Int(Get(x4, "currentMode")), v => { if (Int(Get(x4, "currentMode")) != v) Call(x4, v == 0 ? "SetH2" : v == 1 ? "SetH4" : "SetL4"); });
            // Cellule camping (si elle existe) : montee sur le Ram ou posee a part (alors objet pris a la main : Props,
            // w:mod:ramcamper) ; sa porte et ses deux poignees.
            GameObject cg = GameObject.Find("CamperShell");
            Type ct = TypeOf("CamperShellManager");
            camper = cg != null && ct != null ? cg.GetComponent(ct) : null;
            camperId = cg != null ? cg.GetInstanceID() : 0;
            if (camper != null)
            {
                Component cm = camper;
                Add("cellule", () => Int(Get(cm, "isFitted")), v =>
                {
                    if (Int(Get(cm, "isFitted")) == v) return;
                    if (v == 1) Call(cm, "RestoreFitted", true); else Call(cm, "Detach");
                    MethodInfo st = TypeOf("ram1500") != null ? TypeOf("ram1500").GetMethod("SyncTruckAndCamper", BindingFlags.Public | BindingFlags.Static) : null;
                    if (st != null) { try { st.Invoke(null, null); } catch { } }
                });
                Add("cellulePorte", () => Int(Get(cm, "doorOpen")), v => { if (Int(Get(cm, "doorOpen")) != v) Call(cm, "ToggleDoor"); });
                Add("cellulePoigneeG", () => Int(Get(cm, "leftHandleTwisted")), v => { if (Int(Get(cm, "leftHandleTwisted")) != v) Call(cm, "ToggleHandle", true); });
                Add("cellulePoigneeD", () => Int(Get(cm, "rightHandleTwisted")), v => { if (Int(Get(cm, "rightHandleTwisted")) != v) Call(cm, "ToggleHandle", false); });
            }
        }
        static Component camper;
        static int camperId;

        // ---------------------------------------------------------------- achat au parc du vendeur (histoire du mod)
        // Le joueur paie, le camion (ou la cellule) du parc est detruit, ram1500.SpawnPurchasedTruck (ou SpawnPurchasedCamper)
        // cree le sien a la meme place et SellerLogic.LockLot ferme le parc -- chez lui seul. Ici : le parc (configuration et
        // pose de chaque camion, de la cellule) est releve toutes les secondes ; quand il passe a « vendu » chez nous sans
        // qu'on l'ait applique, l'achat part (@ram 2 / 3 : celui le plus pres du camion apparu) ; les autres creent le meme et
        // ferment leur parc, sans payer. (Les camions du parc sont tires au hasard chez chacun : seul l'achete est le meme.)
        class LotEntry { public Vector3 Pos; public Quaternion Rot; public string Branding; public bool Dually, Diesel, Manual, Matte; public Color Paint; }
        static readonly List<LotEntry> lot = new List<LotEntry>();
        static LotEntry lotCamper;
        static bool lotSoldSeen, camperSoldSeen, lotInit;
        static float lotLook;
        static bool StaticBool(string type, string field) { Type t = TypeOf(type); FieldInfo f = t != null ? t.GetField(field, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static) : null; return f != null && (bool)f.GetValue(null); }
        static void LotWatch(float now)
        {
            if (now < lotLook || TypeOf("SellerLogic") == null) return;
            lotLook = now + 1f;
            bool sold = StaticBool("SellerLogic", "LotSold"), csold = StaticBool("SellerLogic", "CamperLotSold");
            if (!lotInit) { lotInit = true; lotSoldSeen = sold; camperSoldSeen = csold; }
            if (!sold)
            {
                lot.Clear();
                Type tt = TypeOf("LotTruckTag");
                if (tt != null)
                    foreach (UnityEngine.Object o in UnityEngine.Object.FindObjectsOfType(tt))
                    {
                        Component c = o as Component;
                        if (c == null) continue;
                        lot.Add(new LotEntry { Pos = c.transform.position, Rot = c.transform.rotation, Branding = (string)Get(c, "brandingStyle"), Dually = (bool)Get(c, "isDually"), Diesel = (bool)Get(c, "isDiesel"), Manual = (bool)Get(c, "isManual"), Paint = (Color)Get(c, "paintColor"), Matte = (bool)Get(c, "isMatte") });
                    }
            }
            else if (!lotSoldSeen && lot.Count > 0)
            {
                GameObject nr = GameObject.Find("Ram1500");
                LotEntry best = null; float bd = float.MaxValue;
                foreach (LotEntry e in lot) { float d = nr != null ? (e.Pos - nr.transform.position).sqrMagnitude : 0f; if (d < bd) { bd = d; best = e; } }
                if (best != null)
                {
                    Session.SendAll(new NetWriter(Msg.Job).U8(Session.LocalId).Str("@ram").U8(2).Vec(best.Pos).Quat(best.Rot).Str(best.Branding ?? "").Bool(best.Dually).Bool(best.Diesel).Bool(best.Manual)
                        .F32(best.Paint.r).F32(best.Paint.g).F32(best.Paint.b).Bool(best.Matte), true);
                    Log.Info("ram : camion achete ici au parc (" + best.Branding + (best.Diesel ? ", diesel" : "") + (best.Manual ? ", manuelle" : "") + "), envoye");
                }
            }
            lotSoldSeen = sold;
            if (!csold)
            {
                lotCamper = null;
                Type lt = TypeOf("LotCamperTag");
                UnityEngine.Object lo = lt != null ? UnityEngine.Object.FindObjectOfType(lt) : null;
                Component lc = lo as Component;
                if (lc != null) lotCamper = new LotEntry { Pos = lc.transform.position, Rot = lc.transform.rotation, Paint = (Color)Get(lc, "paintColor"), Matte = (bool)Get(lc, "isMatte") };
            }
            else if (!camperSoldSeen && lotCamper != null)
            {
                Session.SendAll(new NetWriter(Msg.Job).U8(Session.LocalId).Str("@ram").U8(3).Vec(lotCamper.Pos).Quat(lotCamper.Rot).F32(lotCamper.Paint.r).F32(lotCamper.Paint.g).F32(lotCamper.Paint.b).Bool(lotCamper.Matte), true);
                Log.Info("ram : cellule achetee ici au parc, envoyee");
            }
            camperSoldSeen = csold;
        }

        static void OnBought(int kind, int who, NetReader r)
        {
            Type rt = TypeOf("ram1500"), st = TypeOf("SellerLogic");
            if (rt == null || st == null) return;
            Vector3 pos = r.Vec(); Quaternion rot = r.Quat();
            if (kind == 2)
            {
                string br = r.Str(); bool du = r.Bool(), di = r.Bool(), ma = r.Bool();
                Color pc = new Color(r.F32(), r.F32(), r.F32()); bool mt = r.Bool();
                if (StaticBool("SellerLogic", "LotSold")) { Log.Info("ram : achat de #" + who + " recu, parc deja vendu ici"); return; }
                MethodInfo sp = rt.GetMethod("SpawnPurchasedTruck", BindingFlags.Public | BindingFlags.Static);
                object ok = sp != null ? sp.Invoke(null, new object[] { pos, rot, br, du, di, ma, pc, mt }) : null;
                MethodInfo ll = st.GetMethod("LockLot", BindingFlags.Public | BindingFlags.Static);
                if (ll != null) ll.Invoke(null, null);
                lotSoldSeen = true;
                ram = null; slots = null; findAt = 0f;   // (nouveau Ram : commandes relevees de nouveau)
                Log.Info("ram : camion achete par #" + who + " cree ici (" + br + ", " + ok + "), parc ferme");
            }
            else
            {
                Color pc = new Color(r.F32(), r.F32(), r.F32()); bool mt = r.Bool();
                if (StaticBool("SellerLogic", "CamperLotSold")) return;
                MethodInfo sp = rt.GetMethod("SpawnPurchasedCamper", BindingFlags.Public | BindingFlags.Static);
                if (sp != null) sp.Invoke(null, new object[] { pos, rot, pc, mt });
                MethodInfo ll = st.GetMethod("LockCamperLot", BindingFlags.Public | BindingFlags.Static);
                if (ll != null) ll.Invoke(null, null);
                camperSoldSeen = true;
                slots = null; findAt = 0f;
                Log.Info("ram : cellule achetee par #" + who + " creee ici");
            }
        }

        static bool AnyCabDoorOpen(Component changed, bool open)
        {
            bool any = false;
            foreach (string d in new[] { "doorLeft", "doorRight" })
            {
                Component c = Comp("RamDoors", d);
                if (c == null) continue;
                any |= c == changed ? open : Int(Get(c, "doorOpen")) == 1;
            }
            return any;
        }

        // ---------------------------------------------------------------- boucle
        static float nextPoll, nextFull, nextCont, nextSlow, camperLook;
        static bool wasAuth = true;
        static float recvRpm; static bool recvBrake; static int recvGear = -1; static bool haveCont;
        static float fuelFactor = -1f, absMiles = -1f, stallTemp = -1f;

        static bool Authority()
        {
            Rigidbody rb = ram != null ? ram.GetComponent<Rigidbody>() : null;
            return rb != null && VehicleSync.Authority(rb) == Session.LocalId;
        }

        public static void Update()
        {
            if (!PlayerSync.InGame || !Session.Active) return;
            float now = Time.realtimeSinceStartup;
            LotWatch(now);
            if (!Find()) return;
            if (now >= camperLook)
            {
                camperLook = now + 5f;
                GameObject cg = GameObject.Find("CamperShell");
                if ((cg != null ? cg.GetInstanceID() : 0) != camperId) { slots = null; findAt = 0f; return; }   // (cellule achetee ou recreee)
            }
            bool auth = Authority();
            if (auth != wasAuth) { wasAuth = auth; Local(auth); }
            if (now >= nextPoll)
            {
                nextPoll = now + 0.1f;
                bool full = auth && now >= nextFull;
                if (full) nextFull = now + 3f;
                NetWriter w = null;
                foreach (Slot s in slots)
                {
                    int v;
                    try { v = s.GetV(); } catch { continue; }
                    if (v == s.Shared && !full) continue;
                    if (w == null) w = new NetWriter(Msg.Job).U8(Session.LocalId).Str("@ram").U8(0);
                    w.Str(s.Key).I32(v);
                    if (v != s.Shared && logs++ < 30) Log.Info("ram : " + s.Key + " = " + v + " envoye");
                    s.Shared = v;
                }
                if (w != null) Session.SendAll(w, true);
            }
            if (auth && now >= nextCont && dt != null)
            {
                nextCont = now + 0.1f;
                bool brake = false;
                foreach (Wheel wh in ram.GetComponentsInChildren<Wheel>()) brake |= wh.brake > 0.05f;
                var w = new NetWriter(Msg.Job).U8(Session.LocalId).Str("@ram").U8(1).F32(dt.rpm).Bool(brake).U8(dt.gear);
                bool slow = now >= nextSlow;
                if (slow) nextSlow = now + 2f;
                w.Bool(slow);
                if (slow) w.F32(fuel != null ? (float)Get(fuel, "fuelLevel") : -1f).F32(fuel != null ? (float)Get(fuel, "fuelOilLevel") : -1f).F32(TempGet());
                Session.SendAll(w, false);
            }
        }

        static object engineTemp;
        static float TempGet() { Component et = Comp("EngineTemp", null); engineTemp = et; object v = Get(et, "Temp"); return v != null ? (float)v : -1f; }

        // Autorite gagnee / perdue : moteur maison rendu ou neutralise.
        static void Local(bool auth)
        {
            if (fuel != null) { if (fuelFactor < 0f) fuelFactor = (float)Get(fuel, "consumptionFactor"); Set(fuel, "consumptionFactor", auth ? fuelFactor : 1e9f); }
            if (mech != null)
            {
                if (absMiles < 0f) { absMiles = (float)Get(mech, "absRandomFailMiles"); stallTemp = (float)Get(mech, "overheatStallTemp"); }
                Set(mech, "absRandomFailMiles", auth ? absMiles : float.MaxValue); Set(mech, "overheatStallTemp", auth ? stallTemp : float.MaxValue);
            }
            if (brakeLights != null) ((Behaviour)brakeLights).enabled = auth;
            if (auth && dt != null && ign != null && (bool)Get(ign, "engineOn")) dt.enabled = true;
            haveCont = false;
            Log.Info("ram : " + (auth ? "autorite ici (moteur, carburant, feux stop d'ici)" : "mene par un autre (regime, feux stop et carburant recus)"));
        }

        // Fin d'image (temoin pose sur le Ram) : regime recu, Drivetrain coupe pour que sa simulation ne l'ecrase pas.
        public class Late : MonoBehaviour { void LateUpdate() { ModRam1500.LateTick(); } }
        static void LateTick()
        {
            if (!haveCont || wasAuth || dt == null || ign == null) return;
            if ((bool)Get(ign, "engineOn")) { dt.rpm = recvRpm; dt.enabled = false; }
            if (recvGear >= 0) dt.gear = recvGear;
            bool lit = recvBrake && Int(Get(ign, "Stage")) != 0;
            foreach (string p in new[] { "Lights/Brakes", "Lights/RealLights/RealBrakes" })
            {
                Transform t = ram.transform.Find(p);
                if (t != null && t.gameObject.activeSelf != lit) t.gameObject.SetActive(lit);
            }
        }

        public static void OnMessage(int who, NetReader r)
        {
            int kind = r.U8();
            if (kind == 2 || kind == 3) { OnBought(kind, who, r); return; }
            if (!Find()) return;
            if (kind == 0)
            {
                while (r.More)
                {
                    string key = r.Str(); int v = r.I32();
                    foreach (Slot s in slots)
                    {
                        if (s.Key != key) continue;
                        s.Shared = v;
                        int cur; try { cur = s.GetV(); } catch { cur = int.MinValue; }
                        if (cur != v) { try { s.Apply(v); } catch (Exception e) { if (errs++ < 10) Log.Warn("ram : " + key + " : " + e.Message); } if (logs++ < 30) Log.Info("ram : " + key + " = " + v + " de #" + who); }
                        try { s.Shared = s.GetV() == v ? v : s.Shared; } catch { }
                    }
                }
                return;
            }
            recvRpm = r.F32(); recvBrake = r.Bool(); recvGear = r.U8();
            bool slow = r.Bool();
            if (slow)
            {
                float fl = r.F32(), fo = r.F32(), te = r.F32();
                if (!wasAuth)
                {
                    if (fuel != null && fl >= 0f) { Set(fuel, "fuelLevel", fl); Set(fuel, "fuelOilLevel", Mathf.Max(0f, fo)); }
                    Component et = Comp("EngineTemp", null);
                    if (et != null && te >= 0f) { FieldInfo bf = et.GetType().GetField("<Temp>k__BackingField", F); if (bf != null) bf.SetValue(et, te); }
                }
            }
            haveCont = true;
            if (ram.GetComponent<Late>() == null) ram.AddComponent<Late>();
        }

        // [Test] Autotest=ram (2 joueurs, mod installe) : a 40 s l'hote se pose a la place et y monte (EnterDrivingMode) ;
        // 42 s cle et demarreur, 44 s phares, 45 s portiere gauche ouverte par l'INVITE ; de 48 a 56 s le Ram roule a 8 m/s ;
        // chacun note toutes les 3 s la pose, le moteur, le regime, les phares et la portiere.
        static int tStep; static float tLog;
        public static void Test(string mode, float t)
        {
            if (mode == "ramreperes" && Find() && tStep == 0 && t > 30f)
            {
                tStep = 9;
                var sb = new System.Text.StringBuilder("autotest : ram reperes (repere du Ram) :");
                foreach (Transform x in ram.GetComponentsInChildren<Transform>(true))
                {
                    string n = x.name.ToLowerInvariant();
                    if (n.Contains("pivot") || n.Contains("trigger") || n.Contains("seat") || n.Contains("steer") || n.Contains("passenger") || n.Contains("getin") || n.Contains("bench") || n.Contains("door"))
                        sb.Append((char)10).Append("  ").Append(Recon.Path(x)).Append(" ").Append(ram.transform.InverseTransformPoint(x.position).ToString("F2"));
                }
                Renderer best = null; float vol = 0f;
                foreach (Renderer rr in ram.GetComponentsInChildren<Renderer>(true)) { Vector3 e = rr.bounds.size; if (e.x * e.y * e.z > vol && rr.name.ToLowerInvariant().Contains("body")) { vol = e.x * e.y * e.z; best = rr; } }
                if (best != null) sb.Append((char)10).Append("  carrosserie ").Append(best.name).Append(" taille ").Append(best.bounds.size.ToString("F2"));
                Log.Info(sb.ToString());
            }
            // [Test] Autotest=ramcellule : l'hote monte la cellule a 40 s (RestoreFitted), la demonte a 50 s ; chacun note.
            if (mode == "ramcellule" && Find())
            {
                if (Session.IsHost && tStep == 0 && t > 55f) { tStep = 1; Call(camper, "RestoreFitted", true); Log.Info("autotest : ramcellule, montee ici (" + (camper != null) + ")"); }
                if (Session.IsHost && tStep == 1 && t > 66f) { tStep = 2; Call(camper, "Detach"); Log.Info("autotest : ramcellule, demontee ici"); }
                if (t > 50f && t < 76f && t >= tLog) { tLog = t + 3f; Log.Info("autotest : ramcellule, montee " + Get(camper, "isFitted") + (camper != null ? ", parent " + (camper.transform.parent != null ? camper.transform.parent.name : "-") : "")); }
                return;
            }
            // [Test] Autotest=ramachat (histoire du mod : parc du vendeur) : a 40 s l'hote « achete » le premier camion du parc
            // (comme LotBuyInteract : camion du parc detruit, SpawnPurchasedTruck a sa place, LockLot) ; chacun note a 50 s.
            if (mode == "ramachat")
            {
                if (Session.IsHost && tStep == 0 && t > 40f)
                {
                    tStep = 1;
                    Type tt = TypeOf("LotTruckTag");
                    UnityEngine.Object o = tt != null ? UnityEngine.Object.FindObjectOfType(tt) : null;
                    Component c = o as Component;
                    if (c == null) { Log.Info("autotest : ramachat, pas de camion au parc"); return; }
                    Vector3 pos = c.transform.position; Quaternion rot = c.transform.rotation;
                    object[] a = { pos, rot, Get(c, "brandingStyle"), Get(c, "isDually"), Get(c, "isDiesel"), Get(c, "isManual"), Get(c, "paintColor"), Get(c, "isMatte") };
                    MethodInfo rl = TypeOf("SellerLogic").GetMethod("RemoveLotTruck", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                    if (rl != null) rl.Invoke(null, new object[] { c.gameObject });
                    UnityEngine.Object.Destroy(c.gameObject);
                    object ok = TypeOf("ram1500").GetMethod("SpawnPurchasedTruck", BindingFlags.Public | BindingFlags.Static).Invoke(null, a);
                    TypeOf("SellerLogic").GetMethod("LockLot", BindingFlags.Public | BindingFlags.Static).Invoke(null, null);
                    Log.Info("autotest : ramachat, achete ici (" + a[2] + ", " + ok + ")");
                }
                if (tStep < 2 && t > 52f) { tStep = 2; GameObject g = GameObject.Find("Ram1500"); Log.Info("autotest : ramachat, Ram1500 " + (g != null ? g.transform.position.ToString("F1") : "absent") + ", parc vendu " + StaticBool("SellerLogic", "LotSold")); }
                return;
            }
            if (mode != "ram" || !Find()) return;
            Rigidbody rb = ram.GetComponent<Rigidbody>();
            if (Session.IsHost && tStep == 0 && t > 40f)
            {
                tStep = 1;
                GameObject pl = GameObject.Find("PLAYER");
                var cc = pl != null ? pl.GetComponent<CharacterController>() : null;
                if (cc != null) cc.enabled = false;
                FieldInfo pv = drive != null ? drive.GetType().GetField("PlayerPivotObject") : null;
                Transform seat = pv != null ? pv.GetValue(drive) as Transform : null;
                if (pl != null) pl.transform.position = seat != null ? seat.position : ram.transform.position - ram.transform.right * 1.6f + Vector3.up * 0.5f;   // (la place : le mod range le joueur la ou il est)
                if (cc != null) cc.enabled = true;
                Call(drive, "EnterDrivingMode");
                Log.Info("autotest : ram, au volant");
            }
            if (Session.IsHost && tStep == 1 && t > 42f) { tStep = 2; Call(ign, "InsertKey"); Call(ign, "StartCranking"); Log.Info("autotest : ram, demarreur"); }
            if (Session.IsHost && tStep == 2 && t > 44f) { tStep = 3; Set(lights, "lightingStage", 2); Call(lights, "ApplyLightingChanges"); Log.Info("autotest : ram, phares"); }
            if (!Session.IsHost && tStep == 0 && t > 45f) { tStep = 3; Component d = Comp("RamDoors", "doorLeft"); Call(d, "StartOpenNPC"); Log.Info("autotest : ram, portiere ouverte ici"); }
            if (Session.IsHost && tStep >= 1 && t > 48f && t < 56f && rb != null)
            {
                Vector3 f = ram.transform.forward; f.y = 0f;
                rb.velocity = f.normalized * 8f + Vector3.up * Mathf.Min(rb.velocity.y, 0f);
            }
            if (t > 38f && t < 66f && t >= tLog)
            {
                tLog = t + 3f;
                Component dl = Comp("RamDoors", "doorLeft");
                Log.Info("autotest : ram, " + ram.transform.position.ToString("F1") + (rb != null ? " v " + rb.velocity.magnitude.ToString("F1") + (rb.isKinematic ? " cinematique" : "") : "")
                         + ", moteur " + Get(ign, "engineOn") + " cle " + Get(ign, "Stage") + ", regime " + (dt != null ? dt.rpm.ToString("F0") : "?") + ", phares " + Get(lights, "lightingStage") + ", portiere G " + Get(dl, "doorOpen") + ", au volant ici " + (VehicleSync.LocalDrivingName ?? "-") + (Camera.main != null && VehicleSync.LocalDrivingName != null ? ", yeux (repere Ram) " + ram.transform.InverseTransformPoint(Camera.main.transform.position).ToString("F2") : ""));
            }
        }
    }
}
