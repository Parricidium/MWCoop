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

        public static void OnLevelLoaded() { icePanes.Clear(); iceComp = null; iceLook = 0f; lotSeed = 0; lotSeedApplied = 0; lotSeedNext = Time.realtimeSinceStartup + 20f; ram = null; drive = null; findAt = Time.realtimeSinceStartup + 10f; tStep = 0; slots = null; lot.Clear(); lotCamper = null; lotInit = false; lotLook = Time.realtimeSinceStartup + 12f; }

        const BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        // Assemblage du mod, cherche une fois (puis toutes les 30 s s'il manque) : avant, chaque appel parcourait tous les
        // assemblages (GetName alloue) -- a chaque image sans le mod (LotWatch), d'ou des passages du ramasse-miettes.
        static Assembly ramAsm;
        static float ramAsmLook = -1f;
        static readonly Dictionary<string, Type> types = new Dictionary<string, Type>();
        static Type TypeOf(string name)
        {
            if (ramAsm == null)
            {
                float now = Time.realtimeSinceStartup;
                if (ramAsmLook >= 0f && now < ramAsmLook) return null;
                ramAsmLook = now + 30f;
                foreach (Assembly a in AppDomain.CurrentDomain.GetAssemblies())
                    if (a.GetName().Name == "ram1500") { ramAsm = a; break; }
                if (ramAsm == null) return null;
            }
            Type t;
            if (!types.TryGetValue(name, out t)) { t = ramAsm.GetType(name, false); types[name] = t; }
            return t;
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
        class Slot { public string Key; public Func<int> GetV; public Action<int> Apply; public int Shared = int.MinValue; public float Hold; }   // Hold : valeur recue en cours d'application (demarreur...) : pas d'envoi
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
            AddPaintAndRadio();
        }
        static Component camper;
        static int camperId;

        // Peinture (bombe du jeu sur les pieces MSCPaintablePart : carrosserie, capot, portieres, hayon, ailes, cellule) : la
        // couleur de sa matiere, RGB sur 24 bits ; radio (marche, volume -- la frequence depend du reglage « radio du jeu ou
        // internet » de chacun).
        static void AddPaintAndRadio()
        {
            Type pt = TypeOf("MSCPaintablePart");
            if (pt != null)
            {
                var roots = new List<GameObject> { ram };
                if (camper != null) roots.Add(camper.gameObject);
                var seen = new HashSet<string>();
                foreach (GameObject root in roots)
                    foreach (Component pc in root.GetComponentsInChildren(pt, true))
                    {
                        Component c = pc;
                        string key = "peinture:" + root.name + "/" + c.gameObject.name;
                        if (!seen.Add(key)) continue;
                        Add(key, () => { Material m = Get(c, "paintMaterial") as Material; if (m == null) return -1; Color k = m.color; return (Mathf.RoundToInt(k.r * 255f) << 16) | (Mathf.RoundToInt(k.g * 255f) << 8) | Mathf.RoundToInt(k.b * 255f); },
                            v => { Material m = Get(c, "paintMaterial") as Material; if (m == null || v < 0) return; Color k = new Color(((v >> 16) & 255) / 255f, ((v >> 8) & 255) / 255f, (v & 255) / 255f, m.color.a); m.color = k; if (m.HasProperty("_Color")) m.SetColor("_Color", k); });
                    }
            }
            Component radio = Comp("RadioController", null);
            if (radio != null)
            {
                Add("radio", () => Int(Get(radio, "radioON")), v => Set(radio, "radioON", v == 1));
                Add("radioVolume", () => Mathf.RoundToInt((float)Get(radio, "radioVolume") * 10f), v => Set(radio, "radioVolume", v / 10f));
                // (frequence du cadran : la meme chez tous ; ce qu'on y entend depend du reglage « radio du jeu ou internet » de chacun)
                Add("radioFreq", () => Mathf.RoundToInt((float)Get(radio, "radioFrequency") * 10f), v => Set(radio, "radioFrequency", v / 10f));
            }
        }

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
            if (now < lotLook) return;
            lotLook = now + 1f;
            if (TypeOf("SellerLogic") == null) return;
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

        // ---------------------------------------------------------------- camions du parc, les memes chez tous
        // SellerLogic.SpawnLotTrucks tire tout au hasard (UnityEngine.Random : sens, version, moteur, boite, couleur, mat) : le
        // parc differait d'un joueur a l'autre. L'hote refait son parc (et la cellule du parc) avec une graine a lui et l'envoie
        // (@ram 4, redit toutes les 20 s) ; chacun refait le sien avec la meme graine -- meme tirage, memes camions.
        static int lotSeed, lotSeedApplied;
        static float lotSeedNext;
        static bool LotRespawn(int seed)
        {
            Type st = TypeOf("SellerLogic");
            if (st == null || StaticBool("SellerLogic", "LotSold")) return false;
            FieldInfo cp = st.GetField("CachedPrefab", BindingFlags.Public | BindingFlags.Static), ccp = st.GetField("CachedCamperPrefab", BindingFlags.Public | BindingFlags.Static);
            GameObject pf = cp != null ? cp.GetValue(null) as GameObject : null;
            if (pf == null) return false;
            int was = UnityEngine.Random.seed;
            try
            {
                UnityEngine.Random.seed = seed;
                st.GetMethod("SpawnLotTrucks", BindingFlags.Public | BindingFlags.Static).Invoke(null, new object[] { pf });
                GameObject cpf = ccp != null ? ccp.GetValue(null) as GameObject : null;
                MethodInfo sc = st.GetMethod("SpawnLotCamper", BindingFlags.Public | BindingFlags.Static);
                if (cpf != null && sc != null && !StaticBool("SellerLogic", "CamperLotSold")) { UnityEngine.Random.seed = seed + 1; sc.Invoke(null, new object[] { cpf }); }
            }
            catch (Exception e) { Log.Warn("ram : parc refait : " + (e.InnerException ?? e).Message); return false; }
            finally { UnityEngine.Random.seed = was ^ seed; }
            lotInit = false;   // (releve du parc repris sur les nouveaux camions)
            return true;
        }
        static void LotSeedTick(float now)
        {
            if (!Session.IsHost || now < lotSeedNext) return;
            lotSeedNext = now + (lotSeed == 0 ? 3f : 20f);
            if (lotSeed == 0)
            {
                int sd = new System.Random().Next(1, int.MaxValue);
                if (!LotRespawn(sd)) return;
                lotSeed = sd;
                Log.Info("ram : parc du vendeur refait (graine " + sd + "), envoye aux autres");
            }
            if (Session.RemoteCount > 0) Session.SendAll(new NetWriter(Msg.Job).U8(Session.LocalId).Str("@ram").U8(4).I32(lotSeed), true);
        }
        static void OnLotSeed(int who, int seed)
        {
            if (Session.IsHost || seed == lotSeedApplied) return;
            if (LotRespawn(seed)) { lotSeedApplied = seed; Log.Info("ram : parc du vendeur refait comme chez #" + who + " (graine " + seed + ")"); }
        }

        // ---------------------------------------------------------------- givre gratte sur les vitres
        // GlassIce (une instance) : chaque vitre (Pane) a une grille d'epaisseur de glace (thick, res x res) que le grattoir
        // creuse (StampBlade). Celui qui gratte (clic tenu) envoie les cases creusees (indice, epaisseur sur 255 de celle du
        // depart) 4 fois par seconde ; les autres ne font que creuser d'autant (jamais regeler). Vitres sans masque : la part
        // grattee (uniformScraped). Fonte et regel : la simulation de chacun.
        class IcePane { public object P; public float[] Shadow; public float Max; public float Uniform; }
        static readonly List<IcePane> icePanes = new List<IcePane>();
        static Component iceComp;
        static float iceNext, iceLook;
        static FieldInfo fThick, fUniform, fDirty, fMasked;
        static MethodInfo mRefresh;
        static bool IceFind(float now)
        {
            if (iceComp != null && icePanes.Count > 0) return true;
            if (now < iceLook) return false;
            iceLook = now + 5f;
            Type gt = TypeOf("GlassIce");
            FieldInfo inst = gt != null ? gt.GetField("Instance", BindingFlags.Public | BindingFlags.Static) : null;
            iceComp = inst != null ? inst.GetValue(null) as Component : null;
            if (iceComp == null) return false;
            System.Collections.IList list = Get(iceComp, "panes") as System.Collections.IList;
            if (list == null || list.Count == 0) return false;
            icePanes.Clear();
            Type pt = list[0].GetType();
            fThick = pt.GetField("thick"); fUniform = pt.GetField("uniformScraped"); fDirty = pt.GetField("dirty"); fMasked = pt.GetField("masked");
            mRefresh = gt.GetMethod("RefreshPane", BindingFlags.NonPublic | BindingFlags.Instance);
            foreach (object p in list)
            {
                float[] th = fThick.GetValue(p) as float[];
                float mx = 0f; if (th != null) foreach (float v in th) if (v > mx) mx = v;
                icePanes.Add(new IcePane { P = p, Shadow = th != null ? (float[])th.Clone() : null, Max = mx > 0f ? mx : 1f, Uniform = (float)fUniform.GetValue(p) });
            }
            Log.Info("ram : givre, " + icePanes.Count + " vitre(s) suivie(s)");
            return true;
        }
        static void IceTick(float now)
        {
            if (now < iceNext) return;
            iceNext = now + 0.25f;
            if (!IceFind(now)) return;
            bool scraping = Input.GetMouseButton(0) || testScrape;
            for (int i = 0; i < icePanes.Count; i++)
            {
                IcePane ip = icePanes[i];
                float[] th = fThick.GetValue(ip.P) as float[];
                if (th == null) continue;
                if (ip.Shadow == null || ip.Shadow.Length != th.Length) { ip.Shadow = (float[])th.Clone(); float mx = 0f; foreach (float v in th) if (v > mx) mx = v; ip.Max = mx > 0f ? mx : 1f; continue; }
                var cells = new List<int>();
                for (int k = 0; k < th.Length; k++)
                {
                    if (scraping && th[k] < ip.Shadow[k] - ip.Max * 0.02f) cells.Add(k);
                    else if (!scraping || th[k] > ip.Shadow[k]) ip.Shadow[k] = th[k];   // (fonte ou regel d'ici : pas envoyes)
                }
                float u = (float)fUniform.GetValue(ip.P);
                bool uni = scraping && u > ip.Uniform + 0.01f;
                if (!uni) ip.Uniform = Mathf.Max(ip.Uniform, u);
                if (cells.Count == 0 && !uni) continue;
                for (int c0 = 0; c0 < Math.Max(1, cells.Count); c0 += 300)
                {
                    int n = Math.Min(300, cells.Count - c0);
                    var w = new NetWriter(Msg.Job).U8(Session.LocalId).Str("@ram").U8(5).U8(i).F32(u).U16(Math.Max(0, n));
                    for (int j = 0; j < n; j++) { int k = cells[c0 + j]; w.U16(k).U8(Mathf.Clamp(Mathf.RoundToInt(th[k] / ip.Max * 255f), 0, 255)); ip.Shadow[k] = th[k]; }
                    if (Session.RemoteCount > 0) Session.SendAll(w, true);
                }
                ip.Uniform = u;
                if (iceLogs++ < 6) Log.Info("ram : givre gratte ici (vitre " + i + ", " + cells.Count + " cases), envoye");
            }
        }
        static int iceLogs;
        public static bool testScrape;
        static void OnIce(int who, NetReader r)
        {
            int i = r.U8(); float u = r.F32(); int n = r.U16();
            if (!IceFind(Time.realtimeSinceStartup) || i >= icePanes.Count) return;
            IcePane ip = icePanes[i];
            float[] th = fThick.GetValue(ip.P) as float[];
            bool changed = false;
            for (int j = 0; j < n; j++)
            {
                int k = r.U16(); float v = r.U8() / 255f * ip.Max;
                if (th == null || k >= th.Length || th[k] <= v) continue;
                th[k] = v; changed = true;
                if (ip.Shadow != null && k < ip.Shadow.Length) ip.Shadow[k] = v;
            }
            if ((float)fUniform.GetValue(ip.P) < u) { fUniform.SetValue(ip.P, u); ip.Uniform = u; changed = true; }
            if (!changed) return;
            fDirty.SetValue(ip.P, true);
            try { if (mRefresh != null) mRefresh.Invoke(iceComp, new object[] { ip.P, true }); } catch { }
            if (iceLogs++ < 12) Log.Info("ram : givre gratte chez #" + who + " (vitre " + i + ", " + n + " cases), applique ici");
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
            LotSeedTick(now);
            IceTick(now);
            if (!Find()) return;
            // Place du conducteur fermee au joueur d'ici quand il est assis en passager ou qu'un autre conduit : la zone du
            // RamDrivingController (OnTriggerStay, touche DrivingMode = Entree) n'est pas un automate du jeu que Seats sait couper
            // -- Entree pour descendre de la banquette aurait aussi pris le volant. Son delai d'entree est tenu ouvert.
            if (drive != null && !(Get(drive, "InCar") is bool && (bool)Get(drive, "InCar"))
                && (Seats.SeatedCar == ram.transform || VehicleSync.RemotelyDriven(ram.transform)))
            { object cd = Get(drive, "cooldown"); if (cd is float && (float)cd < 0.3f) Set(drive, "cooldown", 0.5f); }
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
                    // (premier releve : seule l'autorite donne l'etat de depart -- sinon l'arrivant eteignait les phares de
                    // l'hote, ou y remettait sa peinture)
                    if (s.Shared == int.MinValue && !auth) { s.Shared = v; continue; }
                    if (v == s.Shared && !full) continue;
                    // (valeur recue pas encore atteinte ici -- demarrage rejoue, porte qui s'ouvre : ce n'est pas un geste
                    // d'ici ; renvoyee, elle coupait le moteur de l'hote pendant que l'invite rejouait le demarreur)
                    if (v != s.Shared && now < s.Hold) continue;
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
                if (slow) w.F32(fuel != null ? (float)Get(fuel, "fuelLevel") : -1f).F32(fuel != null ? (float)Get(fuel, "fuelOilLevel") : -1f).F32(TempGet()).F32(Mileage());
                Session.SendAll(w, false);
            }
        }

        static float Mileage() { Component od = Comp("Odometer", null); object v = Get(od, "totalMileage"); return v != null ? (float)v : -1f; }
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
            if (kind == 4) { OnLotSeed(who, r.I32()); return; }
            if (kind == 5) { OnIce(who, r); return; }
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
                        s.Hold = Time.realtimeSinceStartup + 3f;
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
                float fl = r.F32(), fo = r.F32(), te = r.F32(), km = r.More ? r.F32() : -1f;
                if (!wasAuth && km >= 0f) { Component od = Comp("Odometer", null); if (od != null && Mathf.Abs((float)Get(od, "totalMileage") - km) > 0.05f) Call(od, "LoadMileage", km); }
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
            // [Test] Autotest=ramgivre : l'hote gratte le pare-brise a 45 s (grattoir du mod, StampBlade, une ligne) ; a 60 s
            // chacun note l'epaisseur moyenne de chaque vitre et la graine du parc ; et la radio a 88,5 chez l'hote a 50 s.
            if (mode == "ramgivre")
            {
                float now = Time.realtimeSinceStartup;
                if (Session.IsHost && tStep == 0 && t > 45f && IceFind(now))
                {
                    tStep = 1; testScrape = true;
                    MethodInfo sb = iceComp.GetType().GetMethod("StampBlade", BindingFlags.NonPublic | BindingFlags.Instance);
                    object p0 = icePanes.Count > 0 ? icePanes[0].P : null;
                    float got = 0f;
                    if (sb != null && p0 != null)
                        for (int k = 0; k <= 20; k++) got += (float)sb.Invoke(iceComp, new object[] { p0, new Vector2(0.3f + k * 0.02f, 0.5f), new Vector2(1f, 0f), 4f, 2f, 10f });
                    Log.Info("autotest : ramgivre, pare-brise gratte ici (" + got.ToString("F1") + " retire)");
                    Component radio = Comp("RadioController", null);
                    if (radio != null) { Set(radio, "radioFrequency", 88.5f); Log.Info("autotest : ramgivre, radio a 88,5"); }
                }
                if (tStep == 1 && t > 47f) { tStep = 2; testScrape = false; }
                if (t > 60f && tStep < 9)
                {
                    tStep = 9;
                    var sbl = new System.Text.StringBuilder("autotest : ramgivre, ici :");
                    if (IceFind(now))
                        for (int i = 0; i < icePanes.Count; i++) { float[] th = fThick.GetValue(icePanes[i].P) as float[]; double sum = 0; if (th != null) foreach (float v in th) sum += v; sbl.Append(" vitre ").Append(i).Append(" ").Append(th != null && th.Length > 0 ? (sum / th.Length).ToString("F4") : "-"); }
                    Component radio = Comp("RadioController", null);
                    sbl.Append(" | radio ").Append(radio != null ? Get(radio, "radioFrequency") : "?").Append(" | graine du parc ").Append(Session.IsHost ? lotSeed : lotSeedApplied);
                    Type tt = TypeOf("LotTruckTag");
                    if (tt != null) foreach (UnityEngine.Object o in UnityEngine.Object.FindObjectsOfType(tt)) { Component c = o as Component; sbl.Append(" ; ").Append(Get(c, "brandingStyle")).Append(Get(c, "isDiesel") is bool && (bool)Get(c, "isDiesel") ? " D" : "").Append(" ").Append(((Color)Get(c, "paintColor")).ToString()); }
                    Log.Info(sbl.ToString());
                }
                return;
            }
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
                GameObject pl = Game.PlayerGo;
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
                         + ", moteur " + Get(ign, "engineOn") + " cle " + Get(ign, "Stage") + ", regime " + (dt != null ? dt.rpm.ToString("F0") : "?") + ", phares " + Get(lights, "lightingStage") + ", portiere G " + Get(dl, "doorOpen") + ", au volant ici " + (VehicleSync.LocalDrivingName ?? "-") + (Game.MainCam != null && VehicleSync.LocalDrivingName != null ? ", yeux (repere Ram) " + ram.transform.InverseTransformPoint(Game.MainCam.transform.position).ToString("F2") : ""));
            }
        }
    }
}
