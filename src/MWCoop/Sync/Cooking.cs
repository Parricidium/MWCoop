using System.Collections.Generic;
using HutongGames.PlayMaker;
using MWCoop.Net;
using UnityEngine;

namespace MWCoop
{
    // Cuisine : saucisses et viande grillees, cafe, peremption.
    //  - SAUCISSES : un paquet pose sur un foyer (SausageTrigger 'Logic', "State 4" : quatre copies du modele
    //    sausage0 creees, leur etat de conservation pris du paquet, le paquet envoye a la poubelle) n'est ouvert
    //    que chez celui qui le tient (chez les autres, la copie du paquet qui suit ses messages Props est
    //    ignoree : etat annule). Chaque saucisse creee recoit un ID commun (variable ID ajoutee a son automate
    //    'Use' : Props suit sa physique, Consume sa disparition quand on la mange ou la jette) et la creation est
    //    annoncee (1 creation) : les autres copient le meme modele a la meme place, avec le meme ID. Un invite qui
    //    arrive recoit les saucisses deja la.
    //  - CUISSON et PEREMPTION (tout objet a ID dont l'automate 'Fire' a GrillingTime -- saucisse, viande d'elan --
    //    ou dont 'Use' a Condition et SpoilingRate) : celui qui fait autorite sur l'objet (FireTools.Authority :
    //    celui qui le tient, sinon le joueur le plus proche a 6 m, sinon l'hote) envoie chaque seconde ce qui a
    //    change : Condition (peremption), Grill et Burn (cuisson) et la fin de cuisson ('Fire' arrive dans
    //    "Grilled" ou "Burnt"). Les autres prennent les valeurs et rejouent la meme fin : l'automate du jeu envoie
    //    lui-meme GRILLED / BURNED a 'Use' (maillage grille, materiau brule, nom), comme chez l'autre.
    //  - CAFE : cafetiere (coffee pan(itemx) 'Data' : Water, Ground, Coffee, BoilVolume) et tasse (coffee cup(itemx)
    //    'Use' : Coffee, Caffeine) de la maison, meme autorite, memes envois. (Le cafe des machines : Shop.)
    public static class Cooking
    {
        const string Mod = "cuisine";
        static float loadedAt = -1, nextFind, nextScan, nextSend, nextArrivals, nextWarn, lastRescan;
        static int counter;
        static readonly string salt = Salt();
        static readonly Dictionary<int, int> joinLevels = new Dictionary<int, int>();
        static readonly List<KeyValuePair<float, Peer>> due = new List<KeyValuePair<float, Peer>>();
        static int testAuth = -1;   // essais : autorite imposee sur la nourriture

        static string Salt()
        {
            var r = new System.Random();
            return new string(new[] { (char)('a' + r.Next(26)), (char)('a' + r.Next(26)) });
        }

        // ================================================================ saucisses creees sur le feu
        class Grill
        {
            public string Key; public PlayMakerFSM F; public FsmState St;
            public HashSet<int> Before; public GameObject Package; public string CloneName;
        }
        static readonly Dictionary<string, Grill> grills = new Dictionary<string, Grill>();
        static readonly HashSet<PlayMakerFSM> grillFsms = new HashSet<PlayMakerFSM>();

        class GrillHook : ModHook
        {
            public override string Module { get { return Mod; } }
            public Grill G; public bool After;
            public override void OnEnter()
            {
                bool cancel = false;
                try { if (After) AfterCreate(G); else cancel = BeforeCreate(G, this); }
                catch (System.Exception e) { Replay.HookError(e); }
                if (!cancel) Finish();
            }
        }

        static GameObject PrefabOf(Grill g)
        {
            FsmGameObject v = g.F != null ? g.F.FsmVariables.FindFsmGameObject("SausagePrefab") : null;
            return v != null ? v.Value : null;
        }

        // Debut de "State 4" : le paquet est-il celui d'un autre ? Sinon, copies du modele deja la (pour trouver
        // les nouvelles a la fin de l'etat).
        static bool BeforeCreate(Grill g, GrillHook h)
        {
            g.Before = null;
            if (!Session.Active || Replay.Depth > 0) return false;
            FsmGameObject pk = g.F.FsmVariables.FindFsmGameObject("Package");
            GameObject pkg = pk != null ? pk.Value : null;
            float age;
            if (pkg != null && Props.MovedByOther(pkg, out age) && age < 3f)
            {
                FireTools.Cancel(g.F, g.St, h, "State 3");
                Log.Info("cuisine : paquet " + Props.ItemId(pkg) + " d'un autre joueur sur " + g.Key + ", ouvert chez lui");
                return true;
            }
            GameObject prefab = PrefabOf(g);
            if (prefab == null) return false;
            g.Package = pkg;
            g.CloneName = prefab.name + "(Clone)";
            g.Before = new HashSet<int>();
            foreach (GameObject go in Clones(g.CloneName)) g.Before.Add(go.GetInstanceID());
            return false;
        }

        static List<GameObject> Clones(string name)
        {
            var l = new List<GameObject>();
            foreach (Rigidbody rb in Object.FindObjectsOfType<Rigidbody>()) if (rb.name == name) l.Add(rb.gameObject);
            return l;
        }

        // Fin de "State 4" (apres les CreateObject) : les saucisses nees ici recoivent un ID et sont annoncees.
        static void AfterCreate(Grill g)
        {
            if (g.Before == null) return;
            var made = new List<GameObject>();
            foreach (GameObject go in Clones(g.CloneName)) if (!g.Before.Contains(go.GetInstanceID())) made.Add(go);
            g.Before = null;
            if (made.Count == 0) { Log.Warn("cuisine : " + g.Key + " n'a rien cree"); return; }
            var w = new NetWriter(Msg.Cook).U8(1).U8(Session.LocalId).U8(0).Str(g.Key).Str(g.CloneName.Replace("(Clone)", "")).U8(made.Count);
            var ids = new System.Text.StringBuilder();
            foreach (GameObject go in made)
            {
                string id = "makkara" + Session.LocalId + salt + "-" + (++counter);
                SetId(go, id);
                Food f = Register(go, id, g.Key);
                Props.Track(go); Consume.Track(go);
                w.Str(id).Vec(go.transform.position).Quat(go.transform.rotation).F32(FireTools.GetF(f.Use, "Condition", 100f));
                ids.Append(' ').Append(id);
            }
            if (g.Package != null)
            {
                string pid = Props.ItemId(g.Package);
                if (pid.Length > 0) packages.Add(new KeyValuePair<float, KeyValuePair<string, GameObject>>(Time.realtimeSinceStartup + 1.5f, new KeyValuePair<string, GameObject>(pid, g.Package)));
            }
            g.Package = null;
            Log.Info("cuisine : " + made.Count + " saucisses creees ici sur " + g.Key + " :" + ids);
            if (Session.Active) Session.SendAll(w, true);
        }

        // Paquet ouvert ici : s'il a disparu sans que Consume l'ait dit (fin non suivie), on le dit.
        static readonly List<KeyValuePair<float, KeyValuePair<string, GameObject>>> packages = new List<KeyValuePair<float, KeyValuePair<string, GameObject>>>();

        // Variable ID ajoutee a l'automate 'Use' de la saucisse (rien dans le jeu ne la lit) : Props.ItemId la trouve.
        static void SetId(GameObject go, string id)
        {
            PlayMakerFSM use = Game.FsmOn(go, "Use");
            if (use == null) { PlayMakerFSM[] all = go.GetComponents<PlayMakerFSM>(); if (all.Length > 0) use = all[0]; }
            if (use == null) return;
            FsmString v = use.FsmVariables.FindFsmString("ID");
            if (v == null)
            {
                v = new FsmString("ID");
                var l = new List<FsmString>(use.FsmVariables.StringVariables);
                l.Add(v);
                use.FsmVariables.StringVariables = l.ToArray();
            }
            v.Value = id;
        }

        static void FindGrills()
        {
            var found = new List<Grill>();
            foreach (Object o in Game.AllFsms())
            {
                var f = (PlayMakerFSM)o; if (f == null) continue;
                if (f.hideFlags != HideFlags.None || grillFsms.Contains(f) || f.FsmName != "Logic" || f.gameObject.name != "SausageTrigger") continue;
                if (!f.transform.root.gameObject.activeInHierarchy) continue;
                FsmState st;
                try { st = f.Fsm.GetState("State 4"); } catch { continue; }
                if (st == null) continue;
                grillFsms.Add(f);
                if (!Replay.Claim(f, Mod)) { Log.Warn("cuisine : " + FireTools.FsmPath(f) + " deja suivi par " + Replay.Owner(f)); continue; }
                var g = new Grill { F = f, St = st };
                if (!FireTools.Insert(f, st, new GrillHook { G = g }, false) || !FireTools.Insert(f, st, new GrillHook { G = g, After = true }, true))
                { Log.Warn("cuisine : " + FireTools.FsmPath(f) + " pas accroche (actions illisibles)"); grillFsms.Remove(f); continue; }
                found.Add(g);
            }
            if (found.Count == 0) return;
            FireTools.Keys(found, x => x.F, (x, k) => x.Key = k);
            foreach (Grill g in found) grills[g.Key] = g;
            Log.Info("cuisine : " + found.Count + " foyers a saucisses suivis (" + grills.Count + " en tout)");
            if (Config.GetInt("Test", "JournalCuisine", 0) != 0)
                foreach (Grill g in found)
                {
                    if (!g.Key.Contains("LIVINGROOM")) continue;
                    FireTools.Journal("cuisine (journal)", g.F);
                    GameObject prefab = PrefabOf(g);
                    if (prefab != null) foreach (PlayMakerFSM f in prefab.GetComponents<PlayMakerFSM>()) FireTools.Journal("cuisine (journal) modele", f);
                }
        }

        static void OnCreate(int who, bool snapshot, string grillKey, string prefabName, List<KeyValuePair<string, Pose>> made)
        {
            Grill g;
            GameObject prefab = grills.TryGetValue(grillKey, out g) ? PrefabOf(g) : null;
            if (prefab == null) prefab = Game.FindAny(prefabName);
            if (prefab == null) { Warn("modele " + prefabName + " introuvable ici"); return; }
            int n = 0;
            foreach (KeyValuePair<string, Pose> m in made)
            {
                Food old;
                if (foods.TryGetValue(m.Key, out old) && old.Go != null || Props.ObjectOf(m.Key) != null || Consume.Done(m.Key)) continue;
                var go = (GameObject)Object.Instantiate(prefab, m.Value.Pos, m.Value.Rot);
                SetId(go, m.Key);
                Food f = Register(go, m.Key, grillKey);
                FireTools.SetF(f.Use, "Condition", m.Value.Condition);
                Props.Track(go); Consume.Track(go);
                n++;
            }
            Log.Info("cuisine de #" + who + " : " + n + " saucisses creees ici" + (snapshot ? " (arrivee)" : "") + " sur " + grillKey);
        }

        struct Pose { public Vector3 Pos; public Quaternion Rot; public float Condition; }

        // ================================================================ cuisson, peremption
        class Food
        {
            public string Id, Grill; public GameObject Go; public PlayMakerFSM Use, Fire;
            public int Auth = -1, S = -1; public bool WasAuth; public float C = -999f, G = -999f, B = -999f;
        }
        static readonly Dictionary<string, Food> foods = new Dictionary<string, Food>();

        static Food Register(GameObject go, string id, string grill)
        {
            var f = new Food { Id = id, Go = go, Use = Game.FsmOn(go, "Use"), Fire = Game.FsmOn(go, "Fire"), Grill = grill };
            foods[id] = f;
            return f;
        }

        static void ScanFoods()
        {
            int n = 0;
            foreach (Rigidbody rb in Object.FindObjectsOfType<Rigidbody>())
            {
                GameObject go = rb.gameObject;
                PlayMakerFSM fire = Game.FsmOn(go, "Fire"), use = Game.FsmOn(go, "Use");
                bool cook = fire != null && fire.FsmVariables.FindFsmFloat("GrillingTime") != null;
                bool spoil = use != null && use.FsmVariables.FindFsmFloat("SpoilingRate") != null && use.FsmVariables.FindFsmFloat("Condition") != null;
                if (!cook && !spoil) continue;
                string id = Props.ItemId(go);
                if (id.Length == 0) continue;
                Food f;
                if (foods.TryGetValue(id, out f) && f.Go == go) continue;
                Register(go, id, f != null ? f.Grill : null);
                n++;
            }
            if (n > 0) Log.Info("cuisine : " + n + " aliments de plus suivis (" + foods.Count + ")");
        }

        static int FireState(Food f)
        {
            string s = f.Fire != null ? f.Fire.ActiveStateName : null;
            return s == "Grilled" ? 1 : s == "Burnt" ? 2 : 0;
        }

        static int FoodAuthority(Food f)
        {
            if (testAuth >= 0) return testAuth;
            return FireTools.Authority(f.Go, f.Go.transform.position, 6f, ref f.Auth);
        }

        static bool Moved(float a, float b, float tol) { return Mathf.Abs(a - b) > tol; }

        static void WriteFood(NetWriter w, Food f)
        {
            int n = (f.Use != null ? 1 : 0) + (f.Fire != null ? 2 : 0);
            w.Str(f.Id).U8(f.S).U8(n);
            if (f.Use != null) w.Str("c").F32(f.C);
            if (f.Fire != null) w.Str("g").F32(f.G).Str("b").F32(f.B);
        }

        static void ReadFood(Food f)
        {
            f.C = FireTools.GetF(f.Use, "Condition", -1f);
            f.G = FireTools.GetF(f.Fire, "Grill", -1f);
            f.B = FireTools.GetF(f.Fire, "Burn", -1f);
            f.S = FireState(f);
        }

        static void ApplyFood(int who, Food f, int s, List<KeyValuePair<string, float>> vals)
        {
            if (FoodAuthority(f) == Session.LocalId) return;   // reference ici (on le tient, ou on est le plus pres)
            foreach (KeyValuePair<string, float> v in vals)
            {
                if (v.Key == "c") { if (Moved(FireTools.GetF(f.Use, "Condition", v.Value), v.Value, 0.25f)) FireTools.SetF(f.Use, "Condition", v.Value); f.C = v.Value; }
                else if (v.Key == "g") { if (Moved(FireTools.GetF(f.Fire, "Grill", v.Value), v.Value, 0.5f)) FireTools.SetF(f.Fire, "Grill", v.Value); f.G = v.Value; }
                else if (v.Key == "b") { if (Moved(FireTools.GetF(f.Fire, "Burn", v.Value), v.Value, 0.25f)) FireTools.SetF(f.Fire, "Burn", v.Value); f.B = v.Value; }
            }
            if (s > 0 && f.Fire != null && FireState(f) != s)
            {
                string st = s == 1 ? "Grilled" : "Burnt";
                if (f.Fire.Fsm.GetState(st) != null && f.Fire.enabled && f.Go.activeInHierarchy)
                {
                    Replay.Depth++;
                    try { Game.SetState(f.Fire, st); } finally { Replay.Depth--; }
                    Log.Info("cuisine de #" + who + " : " + f.Id + " " + (s == 1 ? "grille" : "brule") + " ici aussi");
                }
            }
            f.S = FireState(f);
        }

        // ================================================================ cafe (cafetiere, tasse de la maison)
        class Level
        {
            public string Key; public PlayMakerFSM F; public string[] Vars; public float[] Sent;
            public int Auth = -1; public bool WasAuth;
        }
        static readonly Dictionary<string, Level> levels = new Dictionary<string, Level>();
        static readonly HashSet<PlayMakerFSM> levelFsms = new HashSet<PlayMakerFSM>();
        static readonly string[] PanVars = { "Water", "Ground", "Coffee", "BoilVolume" };
        static readonly string[] CupVars = { "Coffee", "Caffeine" };

        static void FindLevels()
        {
            var found = new List<Level>();
            foreach (PlayMakerFSM f in Object.FindObjectsOfType<PlayMakerFSM>())
            {
                if (levelFsms.Contains(f) || f.transform.root.name != "EQUIPMENTS") continue;
                string[] vars = null;
                if (f.gameObject.name == "coffee pan(itemx)" && f.FsmName == "Data") vars = PanVars;
                else if (f.gameObject.name == "coffee cup(itemx)" && f.FsmName == "Use") vars = CupVars;
                if (vars == null) continue;
                var have = new List<string>();
                foreach (string v in vars) if (f.FsmVariables.FindFsmFloat(v) != null) have.Add(v);
                if (have.Count == 0) continue;
                levelFsms.Add(f);
                var l = new Level { F = f, Vars = have.ToArray(), Sent = new float[have.Count] };
                for (int i = 0; i < l.Sent.Length; i++) l.Sent[i] = -999f;
                found.Add(l);
            }
            if (found.Count == 0) return;
            FireTools.Keys(found, x => x.F, (x, k) => x.Key = "cafe:" + k);
            foreach (Level l in found) levels[l.Key] = l;
            Log.Info("cuisine : " + found.Count + " cafetiere(s) / tasse(s) suivies");
            if (Config.GetInt("Test", "JournalCuisine", 0) != 0)
                foreach (Level l in found) if (l.F.FsmName == "Data") { FireTools.Journal("cuisine (journal)", l.F); FireTools.Journal("cuisine (journal)", Game.FsmOn(l.F.gameObject, "Fire")); }
        }

        static int LevelAuthority(Level l)
        {
            return FireTools.Authority(l.F.gameObject, l.F.transform.position, 6f, ref l.Auth);
        }

        static void ApplyLevel(Level l, List<KeyValuePair<string, float>> vals)
        {
            if (LevelAuthority(l) == Session.LocalId) return;
            foreach (KeyValuePair<string, float> v in vals)
            {
                FsmFloat x = l.F.FsmVariables.FindFsmFloat(v.Key);
                if (x != null && Mathf.Abs(x.Value - v.Value) > 0.001f + 0.005f * Mathf.Abs(v.Value)) x.Value = v.Value;
                int i = System.Array.IndexOf(l.Vars, v.Key);
                if (i >= 0) l.Sent[i] = v.Value;
            }
        }

        // ================================================================ envois
        static bool Playing { get { return Session.Active && Session.RemoteCount > 0 || testing; } }

        static void SendStates(float now)
        {
            NetWriter w = null;
            System.Action flush = () => { if (w != null) Session.SendAll(w, true); w = null; };
            var gone = new List<string>();
            foreach (Food f in foods.Values)
            {
                if (f.Go == null) { gone.Add(f.Id); continue; }
                if (FoodAuthority(f) != Session.LocalId) { f.WasAuth = false; continue; }
                if (!f.WasAuth) { f.WasAuth = true; f.S = -1; }   // vient de passer a nous : tout envoyer
                float c = FireTools.GetF(f.Use, "Condition", -1f), g = FireTools.GetF(f.Fire, "Grill", -1f), b = FireTools.GetF(f.Fire, "Burn", -1f);
                int s = FireState(f);
                if (s == f.S && !Moved(c, f.C, 0.5f) && !Moved(g, f.G, 1f) && !Moved(b, f.B, 0.5f)) continue;
                ReadFood(f);
                if (w == null) w = new NetWriter(Msg.Cook).U8(2).U8(Session.LocalId);
                WriteFood(w, f);
                if (w.Length > 900) flush();
            }
            foreach (string id in gone) foods.Remove(id);
            foreach (Level l in levels.Values)
            {
                if (l.F == null) continue;
                if (LevelAuthority(l) != Session.LocalId) { l.WasAuth = false; continue; }
                bool all = !l.WasAuth;
                l.WasAuth = true;
                var changed = new List<int>();
                for (int i = 0; i < l.Vars.Length; i++)
                {
                    float v = FireTools.GetF(l.F, l.Vars[i], 0f);
                    if (all || Mathf.Abs(v - l.Sent[i]) > 0.002f + 0.01f * Mathf.Abs(v)) { l.Sent[i] = v; changed.Add(i); }
                }
                if (changed.Count == 0) continue;
                if (w == null) w = new NetWriter(Msg.Cook).U8(2).U8(Session.LocalId);
                w.Str(l.Key).U8(0).U8(changed.Count);
                foreach (int i in changed) w.Str(l.Vars[i]).F32(l.Sent[i]);
                if (w.Length > 900) flush();
            }
            flush();
        }

        // Hote : a un invite qui arrive, les saucisses deja la et l'etat de tout ce qui cuit, perime ou infuse.
        static void SendArrival(Peer p)
        {
            int n = 0;
            foreach (Food f in foods.Values)
            {
                if (f.Go == null || f.Grill == null || !f.Id.StartsWith("makkara")) continue;
                string model = f.Go.name.EndsWith("(Clone)") ? f.Go.name.Replace("(Clone)", "") : "sausage0";
                Grill g;
                if (grills.TryGetValue(f.Grill, out g) && PrefabOf(g) != null) model = PrefabOf(g).name;
                FireTools.SendTo(p, new NetWriter(Msg.Cook).U8(1).U8(Session.LocalId).U8(1).Str(f.Grill).Str(model).U8(1)
                    .Str(f.Id).Vec(f.Go.transform.position).Quat(f.Go.transform.rotation).F32(FireTools.GetF(f.Use, "Condition", 100f)));
                n++;
            }
            NetWriter w = null;
            foreach (Food f in foods.Values)
            {
                if (f.Go == null) continue;
                // Valeurs du moment, sans toucher a ce qui a ete envoye a tous (f.C, f.G...).
                var now = new Food { Id = f.Id, Use = f.Use, Fire = f.Fire };
                ReadFood(now);
                if (w == null) w = new NetWriter(Msg.Cook).U8(2).U8(Session.LocalId);
                WriteFood(w, now);
                if (w.Length > 900) { FireTools.SendTo(p, w); w = null; }
            }
            foreach (Level l in levels.Values)
            {
                if (l.F == null) continue;
                if (w == null) w = new NetWriter(Msg.Cook).U8(2).U8(Session.LocalId);
                w.Str(l.Key).U8(0).U8(l.Vars.Length);
                foreach (string v in l.Vars) w.Str(v).F32(FireTools.GetF(l.F, v, 0f));
                if (w.Length > 900) { FireTools.SendTo(p, w); w = null; }
            }
            if (w != null) FireTools.SendTo(p, w);
            Log.Info("cuisine : " + n + " saucisses et l'etat de " + foods.Count + " aliments envoyes a " + p);
        }

        // ================================================================ cycle
        public static void OnLevelLoaded()
        {
            grills.Clear(); grillFsms.Clear(); foods.Clear(); levels.Clear(); levelFsms.Clear(); packages.Clear();
            joinLevels.Clear(); due.Clear();
            testStep = 0; testLog = 0; testing = false; testAuth = -1; testGrill = null;
            loadedAt = PlayerSync.InGame ? Time.realtimeSinceStartup : -1;
            nextFind = loadedAt + 7f;   // avant WorldFsms (16 s)
            nextScan = loadedAt + 12f;  // apres le premier releve de Props (10 s) : les ID sont poses
        }

        public static void Update()
        {
            if (loadedAt < 0 || !PlayerSync.InGame) return;
            float now = Time.realtimeSinceStartup;
            if (now >= nextFind)
            {
                nextFind = now + 30f;
                try { FindGrills(); } catch (System.Exception e) { Log.Warn("cuisine : releve des foyers : " + e.Message); }
                try { FindLevels(); } catch (System.Exception e) { Log.Warn("cuisine : releve du cafe : " + e.Message); }
            }
            if (now >= nextScan) { nextScan = now + 15f; ScanFoods(); }
            for (int i = packages.Count - 1; i >= 0; i--)
            {
                if (now < packages[i].Key) continue;
                KeyValuePair<string, GameObject> pk = packages[i].Value;
                packages.RemoveAt(i);
                if ((pk.Value == null || !pk.Value.activeInHierarchy) && !Consume.Done(pk.Key)) Consume.SendGone(pk.Key, "Destroy");
            }
            if (Playing && now >= nextSend) { nextSend = now + 1f; SendStates(now); }
            if (!Session.IsHost || !Session.Active) return;
            if (now >= nextArrivals) { nextArrivals = now + 1f; FireTools.Arrivals(joinLevels, due, 25f); }
            for (int i = due.Count - 1; i >= 0; i--)
            {
                if (now < due[i].Key) continue;
                Peer p = due[i].Value;
                due.RemoveAt(i);
                SendArrival(p);
            }
        }

        static void Warn(string s)
        {
            if (Time.realtimeSinceStartup < nextWarn) return;
            nextWarn = Time.realtimeSinceStartup + 10f;
            Log.Warn("cuisine : " + s);
        }

        // Messages (Msg.Cook) : [U8 sorte][U8 joueur]... ; l'hote relaie tout aux autres invites (joueur corrige).
        //  1 creation  U8 instantane d'arrivee, Str cle du foyer, Str modele, U8 n,
        //              n x (Str ID, Vec position, Quat rotation, F32 Condition)                         (fiable)
        //  2 etats     n x (Str ID ou cle cafe:, U8 fin de cuisson 0/1 grille/2 brule, U8 m,
        //              m x (Str nom, F32 valeur) : c Condition, g Grill, b Burn ; ou variables du cafe)  (fiable)
        public static void OnMessage(Peer from, NetReader r)
        {
            byte[] raw = Session.IsHost ? r.Rest() : null;
            int kind = r.U8();
            int who = r.U8();
            if (Session.IsHost)
            {
                who = from.Id;
                Session.Broadcast(new NetWriter(Msg.Cook).U8(kind).U8(who).Raw(raw, 2, raw.Length - 2), true, who);
            }
            if (loadedAt < 0) return;
            if (kind == 1)
            {
                bool snapshot = r.U8() != 0;
                string grill = r.Str(), model = r.Str();
                int n = r.U8();
                var made = new List<KeyValuePair<string, Pose>>();
                for (int i = 0; i < n; i++)
                {
                    string id = r.Str();
                    made.Add(new KeyValuePair<string, Pose>(id, new Pose { Pos = r.Vec(), Rot = r.Quat(), Condition = r.F32() }));
                }
                OnCreate(who, snapshot, grill, model, made);
                return;
            }
            if (kind != 2) return;
            var vals = new List<KeyValuePair<string, float>>();
            while (r.More)
            {
                string id = r.Str();
                int s = r.U8(), m = r.U8();
                vals.Clear();
                for (int i = 0; i < m; i++) { string name = r.Str(); vals.Add(new KeyValuePair<string, float>(name, r.F32())); }
                Food f; Level l;
                if (foods.TryGetValue(id, out f) && f.Go != null) ApplyFood(who, f, s, vals);
                else if (levels.TryGetValue(id, out l) && l.F != null) ApplyLevel(l, vals);
                else if (Time.realtimeSinceStartup - lastRescan > 3f)
                {
                    lastRescan = Time.realtimeSinceStartup;   // aliment tout neuf ici (achete, cree a l'instant) : nouveau releve
                    ScanFoods();
                    if (foods.TryGetValue(id, out f) && f.Go != null) ApplyFood(who, f, s, vals);
                }
            }
        }

        // ================================================================ essais
        // [Test] Autotest=grill (l'hote agit) / grill-invite (l'invite agit, autorite sur la nourriture imposee a
        //   l'invite) ; TestPos=-6.2,0.4,7.2 (salon, les deux) ; [Test] TestGrill : partie de la cle du foyer
        //   (LIVINGROOM/Fireplace par defaut).
        //   30 s : un paquet (objet vide) est ouvert sur la cheminee du salon (SausageTrigger -> "State 4", comme le
        //   paquet tenu au-dessus du feu) : 4 saucisses ; 40 s : la 1re est grillee ('Fire' -> "Grilled", comme a
        //   la fin de la cuisson) ; 46 s : la 2e brule ("Burnt") ; 52 s : la 3e est mangee ('Use' -> "Destroy").
        //   Chacun note toutes les 2 s de 28 a 66 s : "autotest : grill N saucisses : <ID>@<position> c= g= b=
        //   feu=<etat Fire> use=<etat Use> grille=<maillage grilled actif> ; ...". Attendu : memes ID et positions
        //   des deux cotes des 31 s, feu=Grilled / Burnt et grille=True sur les memes ID a 1 s pres, 3e ID absent
        //   partout apres 52 s.
        // [Test] Autotest=cafetiere ; TestPos=-847.5,-1.2,503.0 (cottage). 30 s : l'hote verse de l'eau et du cafe
        //   (cafetiere 'Data' Water 0.4, Ground 12) ; 34 s : ONFIRE (sur le feu : "Cooking"). Chacun note toutes les
        //   2 s : "autotest : cafetiere Water= Ground= Coffee= etat= ; tasse Coffee=". Attendu chez l'invite : memes
        //   valeurs a 1 % pres a 1 s pres.
        static int testStep;
        static float testLog;
        static bool testing;
        static Grill testGrill;
        static GameObject testPackage;

        public static void Test(string mode, float t)
        {
            bool grill = mode == "grill" || mode == "grill-invite";
            if (!grill && mode != "cafetiere") return;
            testing = true;
            if (grill) TestGrill(Session.IsHost != (mode == "grill-invite"), mode == "grill-invite", t);
            else TestCoffee(t);
        }

        static List<Food> OursSorted()
        {
            var l = new List<Food>();
            foreach (Food f in foods.Values) if (f.Id.StartsWith("makkara")) l.Add(f);
            l.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
            return l;
        }

        static void TestGrill(bool actor, bool guestMode, float t)
        {
            // Autorite imposee : l'hote (grill), l'invite (grill-invite) -- deux instances au meme endroit.
            if (!guestMode) testAuth = 0;
            else if (!Session.IsHost) testAuth = Session.LocalId;
            else foreach (int id in Session.Players.Keys) if (id != Session.LocalId) { testAuth = id; break; }
            if (testGrill == null)
            {
                string part = Config.Get("Test", "TestGrill", "LIVINGROOM/Fireplace");
                foreach (Grill g in grills.Values) if (g.Key.Contains(part)) { testGrill = g; break; }
            }
            if (actor && testGrill != null && t > 30f && testStep == 0)
            {
                testStep = 1;
                testPackage = new GameObject("MWCoop essai paquet");
                FsmGameObject pk = testGrill.F.FsmVariables.FindFsmGameObject("Package");
                if (pk != null) pk.Value = testPackage;
                Game.SetState(testGrill.F, "State 4");
                Log.Info("autotest : paquet ouvert sur " + testGrill.Key + " -> " + testGrill.F.ActiveStateName + ", " + OursSorted().Count + " saucisses");
            }
            if (actor && t > 32f && testPackage != null) { Object.Destroy(testPackage); testPackage = null; }
            List<Food> ours = OursSorted();
            if (actor && t > 40f && testStep == 1 && ours.Count > 0)
            {
                testStep = 2;
                if (ours[0].Fire != null) Game.SetState(ours[0].Fire, "Grilled");
                Log.Info("autotest : " + ours[0].Id + " grillee -> " + (ours[0].Fire != null ? ours[0].Fire.ActiveStateName : "pas de Fire"));
            }
            if (actor && t > 46f && testStep == 2 && ours.Count > 1)
            {
                testStep = 3;
                if (ours[1].Fire != null) Game.SetState(ours[1].Fire, "Burnt");
                Log.Info("autotest : " + ours[1].Id + " brulee -> " + (ours[1].Fire != null ? ours[1].Fire.ActiveStateName : "pas de Fire"));
            }
            if (actor && t > 52f && testStep == 3 && ours.Count > 2)
            {
                testStep = 4;
                if (ours[2].Use != null) Game.SetState(ours[2].Use, "Destroy");
                Log.Info("autotest : " + ours[2].Id + " mangee");
            }
            if (t > 28f && t < 66f && t - testLog >= 2f)
            {
                testLog = t;
                var sb = new System.Text.StringBuilder("autotest : grill " + ours.Count + " saucisses :");
                foreach (Food f in ours)
                {
                    if (f.Go == null) { sb.Append(' ').Append(f.Id).Append("@disparue ;"); continue; }
                    Transform grilled = f.Go.transform.Find("grilled");
                    sb.Append(' ').Append(f.Id).Append('@').Append(f.Go.transform.position.ToString("F2"))
                      .Append(" c=").Append(FireTools.GetF(f.Use, "Condition", -1f).ToString("F1"))
                      .Append(" g=").Append(FireTools.GetF(f.Fire, "Grill", -1f).ToString("F1"))
                      .Append(" b=").Append(FireTools.GetF(f.Fire, "Burn", -1f).ToString("F1"))
                      .Append(" feu=").Append(f.Fire != null ? f.Fire.ActiveStateName : "?")
                      .Append(" use=").Append(f.Use != null ? f.Use.ActiveStateName : "?")
                      .Append(" grille=").Append(grilled != null && grilled.gameObject.activeSelf).Append(" ;");
                }
                Log.Info(sb.ToString());
            }
        }

        static void TestCoffee(float t)
        {
            Level pan = null, cup = null;
            foreach (Level l in levels.Values) { if (l.F == null) continue; if (l.F.FsmName == "Data") pan = pan ?? l; else cup = cup ?? l; }
            if (Session.IsHost && pan != null && t > 30f && testStep == 0)
            {
                testStep = 1;
                FireTools.SetF(pan.F, "Water", 0.4f);
                FireTools.SetF(pan.F, "Ground", 12f);
                Log.Info("autotest : cafetiere remplie (Water 0.4, Ground 12)");
            }
            if (Session.IsHost && pan != null && t > 34f && testStep == 1)
            {
                testStep = 2;
                pan.F.SendEvent("ONFIRE");
                Log.Info("autotest : cafetiere sur le feu -> " + pan.F.ActiveStateName);
            }
            if (t > 28f && t < 70f && t - testLog >= 2f)
            {
                testLog = t;
                if (pan == null) { Log.Info("autotest : cafetiere introuvable (" + levels.Count + " suivis)"); return; }
                Log.Info("autotest : cafetiere Water=" + FireTools.GetF(pan.F, "Water", -1f).ToString("F3") + " Ground=" + FireTools.GetF(pan.F, "Ground", -1f).ToString("F2")
                         + " Coffee=" + FireTools.GetF(pan.F, "Coffee", -1f).ToString("F3") + " etat=" + pan.F.ActiveStateName + " autorite=#" + pan.Auth
                         + " ; tasse Coffee=" + (cup != null ? FireTools.GetF(cup.F, "Coffee", -1f).ToString("F3") : "?"));
            }
        }
    }
}
