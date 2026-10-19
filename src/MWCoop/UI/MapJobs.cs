using System.Collections;
using System.Collections.Generic;
using HutongGames.PlayMaker;
using UnityEngine;

namespace MWCoop
{
    // Objectifs des boulots sur la carte, pour chaque joueur (demande de JD, 18/10) : la prochaine etape de chaque boulot que
    // le joueur d'ici a pris, mise a jour en direct (nouveau point quand l'etape change, retire quand c'est fini), avec une
    // liste « Tes objectifs » et un message a chaque nouvelle etape. Etats du jeu (releves dans le vidage, 18/10) :
    //  - fosses septiques JOBS/HouseShitN : ShitLevelTrigger 'Level' Called (pris) -> pomper au puits ; puis le client
    //    (LOD/ShitNPC/Man 'Anims' Money) -> se faire payer ;
    //  - bois JOBS/HouseWoodN : WoodJobNPoint 'Logic' Order (pris) -> decharger pres du point (9 m) ; puis le client
    //    (WoodCallerN 'Animations' Money) -> se faire payer ;
    //  - oncle JOBS/UNCLE 'Data' Stage 1 -> le chercher (JOBS/UNCLE/Uncle) ; en voiture (UncleWalker 'Logic' : riding, Near
    //    home) -> pres de chez lui (YARD/UNCLE/UncleHomeDistance) ; Near back -> le ramener (PERAJARVI/Kunnalliskoti) ;
    //  - taxi : pour celui qui conduit le taxi -- client (TaxiWalker 'Logic' en route vers lui) puis destination (DropOffPoint),
    //    et le rendez-vous d'embauche (JOBS/TAXIJOB 'Logic' JobStage 1) pour celui qui a appele ;
    //  - tracts JOBS/ADs 'Data' JobStage 2 ou 5 (tournee en cours) : les boites aux lettres pas encore servies (liste de
    //    booleens de JOBS/ADs, BoxIndex des automates WaitAd).
    // A qui est le boulot : le jeu n'en sait rien. Un boulot se prend au telephone (repondre, appeler) : il est a celui qui
    // est pres d'un telephone (4 m) quand il devient pris ; le taxi, a son conducteur. Garde par monde (boulots.ini) pour
    // une partie rechargee.
    public static partial class MapPanel
    {
        public class Objective { public string Key, Fr, En; public Vector3 At; public bool Small; }
        static readonly List<Objective> objectives = new List<Objective>();
        static readonly Dictionary<string, bool> wasActive = new Dictionary<string, bool>();
        static readonly HashSet<string> mineJobs = new HashSet<string>();
        static readonly Dictionary<string, string> lastStep = new Dictionary<string, string>();
        static readonly Color ObjColor = new Color(1f, 0.84f, 0.25f);
        static float objNext; static int objLevel = -1; static bool objLoaded;
        static Transform[] phones;
        static readonly Dictionary<string, PlayMakerFSM> fsmCache = new Dictionary<string, PlayMakerFSM>();
        static List<KeyValuePair<int, Transform>> boxes;

        public static List<Objective> Objectives { get { return objectives; } }

        static PlayMakerFSM Fsm(string path, string name)
        {
            PlayMakerFSM f;
            string k = path + "::" + name;
            if (fsmCache.TryGetValue(k, out f) && f != null) return f;
            GameObject g = Game.FindAny(path);
            f = g != null ? Game.FsmOn(g, name) : null;
            fsmCache[k] = f;
            return f;
        }
        static Transform T(string path) { GameObject g = Game.FindAny(path); return g != null ? g.transform : null; }
        static bool B(PlayMakerFSM f, string v) { FsmBool b = f != null ? f.FsmVariables.FindFsmBool(v) : null; return b != null && b.Value; }
        static int I(PlayMakerFSM f, string v) { FsmInt i = f != null ? f.FsmVariables.FindFsmInt(v) : null; return i != null ? i.Value : -1; }

        static string World() { FsmFloat pid = FsmVariables.GlobalVariables.FindFsmFloat("PlayerID"); return pid != null ? pid.Value.ToString("F0") : ""; }
        static string JobsPath { get { return System.IO.Path.Combine(Log.DataDir, "boulots.ini"); } }
        static void SaveJobs()
        {
            string w = World() + "|";
            var lines = new List<string>();
            if (System.IO.File.Exists(JobsPath)) foreach (string l in System.IO.File.ReadAllLines(JobsPath)) if (!l.StartsWith(w)) lines.Add(l);
            foreach (string k in mineJobs) lines.Add(w + k);
            try { System.IO.File.WriteAllLines(JobsPath, lines.ToArray()); } catch { }
        }

        static bool NearPhone()
        {
            Transform me = Game.PlayerT;
            if (me == null) return false;
            if (phones == null)
            {
                var l = new List<Transform>();
                foreach (string p in new[] { "HOMENEW/Functions/FunctionsDisable/Telephone", "YARD/Building/LIVINGROOM/Telephone 1" }) { Transform t = T(p); if (t != null) l.Add(t); }
                phones = l.ToArray();
            }
            foreach (Transform t in phones) if (t != null && (t.position - me.position).sqrMagnitude < 16f) return true;
            return false;
        }

        // Pris / fini : a qui il est (au moment ou il devient pris), oublie quand il n'est plus actif.
        static bool Mine(string key, bool active)
        {
            bool was; wasActive.TryGetValue(key, out was);
            wasActive[key] = active;
            if (!active)
            {
                if (mineJobs.Remove(key)) { SaveJobs(); Log.Info("boulots : " + key + " fini"); }
                lastStep.Remove(key);
                return false;
            }
            if (!was && !mineJobs.Contains(key) && objLoaded && NearPhone()) { mineJobs.Add(key); SaveJobs(); Log.Info("boulots : " + key + " pris ici"); }
            return mineJobs.Contains(key);
        }

        static void Add(string key, string fr, string en, Vector3 at, bool small = false)
        {
            objectives.Add(new Objective { Key = key, Fr = fr, En = en, At = at, Small = small });
            if (small) return;
            string step = fr;
            string last;
            if (lastStep.TryGetValue(key, out last) && last == step) return;
            lastStep[key] = step;
            Hud.Toast(Lang.T("Objectif : ", "Objective: ") + Lang.T(fr, en));
            Log.Info("boulots : " + key + " -> " + fr);
        }

        static void JobsTick()
        {
            if (!PlayerSync.InGame) return;
            float now = Time.realtimeSinceStartup;
            if (now < objNext) return;
            objNext = now + 0.5f;
            if (objLevel != Application.loadedLevel)
            {
                objLevel = Application.loadedLevel; fsmCache.Clear(); phones = null; boxes = null; objectives.Clear(); wasActive.Clear(); mineJobs.Clear(); lastStep.Clear(); objLoaded = false;
            }
            if (!objLoaded)
            {
                if (Time.timeSinceLevelLoad < 15f) return;   // (partie chargee : les etats du jeu sont ceux de la sauvegarde)
                objLoaded = true;
                string w = World() + "|";
                if (System.IO.File.Exists(JobsPath)) foreach (string l in System.IO.File.ReadAllLines(JobsPath)) if (l.StartsWith(w)) mineJobs.Add(l.Substring(w.Length));
                if (mineJobs.Count > 0) Log.Info("boulots : " + mineJobs.Count + " boulot(s) a moi repris (" + string.Join(", ", new List<string>(mineJobs).ToArray()) + ")");
            }
            objectives.Clear(); tractsLine = null;
            try { Septic(); Firewood(); Uncle(); Taxi(); Flyers(); }
            catch (System.Exception e) { if (now > objErr) { objErr = now + 30f; Log.Warn("boulots : " + e.Message); } }
        }
        static float objErr;

        static void Septic()
        {
            for (int n = 1; n <= 5; n++)
            {
                PlayMakerFSM lv = Fsm("JOBS/HouseShit" + n + "/WasteWell_2000litre/Shit/Level/ShitLevelTrigger", "Level");
                PlayMakerFSM man = Fsm("JOBS/HouseShit" + n + "/LOD/ShitNPC/Man", "Anims");
                bool called = B(lv, "Called"), money = B(man, "Money");
                string key = "fosse" + n;
                if (!Mine(key, called || money)) continue;
                if (called) { Transform w = T("JOBS/HouseShit" + n + "/WasteWell_2000litre"); if (w != null) Add(key, "Fosse septique : pomper la fosse", "Septic tank: pump it out", w.position); }
                else if (money && man != null) Add(key, "Fosse septique : te faire payer", "Septic tank: get paid", man.transform.position);
            }
        }

        static void Firewood()
        {
            for (int n = 1; n <= 4; n++)
            {
                PlayMakerFSM job = Fsm("JOBS/HouseWood" + n + "/WoodJobNPoint".Replace("N", n.ToString()), "Logic");
                string caller = n == 1 ? "JOBS/HouseWood1/LOD/CarPos/NPCWood/WoodCaller1" : "JOBS/HouseWood" + n + "/LOD/NPCWood/WoodCaller" + n;
                PlayMakerFSM anim = Fsm(caller, "Animations");
                bool order = B(job, "Order"), money = B(anim, "Money");
                string key = "bois" + n;
                if (!Mine(key, order || money)) continue;
                if (order && job != null) Add(key, "Bois : décharger la benne ici (moins de 9 m)", "Firewood: unload the trailer here (under 9 m)", job.transform.position);
                else if (money && anim != null) Add(key, "Bois : te faire payer", "Firewood: get paid", anim.transform.position);
            }
        }

        static void Uncle()
        {
            PlayMakerFSM data = Fsm("JOBS/UNCLE", "Data"), walker = Fsm("JOBS/UNCLE/Uncle/UncleWalker", "Logic");
            int stage = I(data, "Stage");
            string ws = walker != null ? walker.ActiveStateName ?? "" : "";
            bool riding = ws == "In car riding" || ws == "Near home" || ws == "Near back" || ws == "Car speed";
            if (!Mine("oncle", stage == 1 || riding)) return;
            if (ws == "Near back" || ws == "Car speed") { Transform k = T("PERAJARVI/Kunnalliskoti"); if (k != null) Add("oncle", "Oncle : le ramener à la maison de retraite", "Uncle: drive him back to the care home", k.position); }
            else if (riding) { Transform h = T("YARD/UNCLE/UncleHomeDistance"); if (h != null) Add("oncle", "Oncle : l'amener près de chez lui", "Uncle: take him near his home", h.position); }
            else { Transform u = T("JOBS/UNCLE/Uncle"); if (u != null) Add("oncle", "Oncle : aller le chercher (dans la Satsuma)", "Uncle: pick him up (in the Satsuma)", u.position); }
        }

        static readonly HashSet<string> TaxiPickup = new HashSet<string> { "New location", "Activate", "Distance 1", "Distance 2", "State 13", "Which car", "State 2", "Taxi", "State 8", "Towards pick point" };
        static readonly HashSet<string> TaxiRide = new HashSet<string> { "Open door", "Set mass", "Randomize anim", "State 6", "State 7", "Out", "State 9", "Pay", "Offer money" };
        static void Taxi()
        {
            PlayMakerFSM job = Fsm("JOBS/TAXIJOB", "Logic");
            int js = I(job, "JobStage");
            // embauche : rendez-vous au taxi (a celui qui a appele)
            if (Mine("taxi-embauche", js == 1))
            {
                Transform car = T("JOBS/TAXIJOB/MACHTWAGEN");
                if (car != null) Add("taxi-embauche", "Taxi : rendez-vous à la voiture (demain, 11-14 h)", "Taxi: meet at the car (tomorrow, 11 am - 2 pm)", car.position);
            }
            // courses : a celui qui conduit le taxi
            Transform d = VehicleSync.LocalDrivingRoot;
            bool driver = d != null && d.name == "MACHTWAGEN" && d.parent != null && d.parent.name == "TAXIJOB";
            PlayMakerFSM w = Fsm("JOBS/TAXIJOB/Customer1/TaxiWalker", "Logic");
            string st = w != null ? w.ActiveStateName ?? "" : "";
            if (!driver || js < 2) { lastStep.Remove("taxi"); return; }
            if (TaxiPickup.Contains(st)) { Transform c = T("JOBS/TAXIJOB/Customer1"); if (c != null) Add("taxi", "Taxi : aller chercher le client", "Taxi: pick up the customer", c.position); }
            else if (TaxiRide.Contains(st))
            {
                FsmGameObject drop = w.FsmVariables.FindFsmGameObject("DropOffPoint");
                if (drop != null && drop.Value != null) Add("taxi", "Taxi : déposer le client ici", "Taxi: drop the customer here", drop.Value.transform.position);
            }
            else lastStep.Remove("taxi");
        }

        static void Flyers()
        {
            PlayMakerFSM ads = Fsm("JOBS/ADs", "Data");
            int js = I(ads, "JobStage");
            if (!Mine("tracts", js >= 1)) return;
            if (js != 2 && js != 5) return;   // (embauche, tournee pas encore commencee)
            GameObject root = ads.gameObject;
            ArrayList done = null;
            foreach (PlayMakerArrayListProxy p in root.GetComponents<PlayMakerArrayListProxy>()) if (string.IsNullOrEmpty(p.referenceName) && p.arrayList != null) done = p.arrayList;
            if (boxes == null)
            {
                boxes = new List<KeyValuePair<int, Transform>>();
                foreach (PlayMakerFSM f in Resources.FindObjectsOfTypeAll<PlayMakerFSM>())
                    if (f.FsmName == "WaitAd" && f.gameObject.hideFlags == HideFlags.None) { FsmInt bi = f.FsmVariables.FindFsmInt("BoxIndex"); if (bi != null) boxes.Add(new KeyValuePair<int, Transform>(bi.Value, f.transform)); }
                Log.Info("boulots : " + boxes.Count + " boites aux lettres pour les tracts");
            }
            int left = 0;
            foreach (KeyValuePair<int, Transform> b in boxes)
            {
                bool served = done != null && b.Key >= 0 && b.Key < done.Count && done[b.Key] is bool && (bool)done[b.Key];
                if (served || b.Value == null) continue;
                left++;
                Add("tracts", "Tracts", "Flyers", b.Value.position, true);
            }
            if (left == 0) return;
            string fr = "Tracts : " + left + " boîte" + (left > 1 ? "s" : "") + " aux lettres à servir", en = "Flyers: " + left + " mailbox" + (left > 1 ? "es" : "") + " to fill";
            string last; if (!lastStep.TryGetValue("tracts", out last) || last != "tournee") { lastStep["tracts"] = "tournee"; Hud.Toast(Lang.T("Objectif : ", "Objective: ") + Lang.T(fr, en)); }
            tractsLine = Lang.T(fr, en);
        }
        static string tractsLine;

        // Autotest « objectifs » : a 20 s joueur au telephone de l'appartement, puis fosse 2 prise (Called), bois 4 pris (Order),
        // tracts embauche (JobStage 1) ; a 23 s tournee de tracts (JobStage 2) ; a 27 s carte ouverte et capture ; a 31 s fosse 2
        // pompee (Called faux, client Money vrai) -> etape suivante ; a 35 s capture ; bilan des objectifs a chaque etape.
        static int objTest;
        static void TestObjectives(string mode, float t)
        {
            if (mode != "objectifs") return;
            if (objTest == 0 && t > 20f)
            {
                objTest = 1;
                NearPhone(); GameObject pl = GameObject.Find("PLAYER");
                if (pl != null && phones != null && phones.Length > 0 && phones[0] != null) pl.transform.position = phones[0].position + Vector3.up * 0.2f;
                FsmBool c = Fsm("JOBS/HouseShit2/WasteWell_2000litre/Shit/Level/ShitLevelTrigger", "Level") != null ? Fsm("JOBS/HouseShit2/WasteWell_2000litre/Shit/Level/ShitLevelTrigger", "Level").FsmVariables.FindFsmBool("Called") : null;
                FsmBool o = Fsm("JOBS/HouseWood4/WoodJob4Point", "Logic") != null ? Fsm("JOBS/HouseWood4/WoodJob4Point", "Logic").FsmVariables.FindFsmBool("Order") : null;
                FsmInt js = Fsm("JOBS/ADs", "Data") != null ? Fsm("JOBS/ADs", "Data").FsmVariables.FindFsmInt("JobStage") : null;
                if (c != null) c.Value = true; if (o != null) o.Value = true; if (js != null) js.Value = 1;
                Log.Info("autotest objectifs : au telephone " + NearPhone() + ", fosse 2 " + (c != null) + ", bois 4 " + (o != null) + ", tracts " + (js != null));
            }
            if (objTest == 1 && t > 23f) { objTest = 2; FsmInt js = Fsm("JOBS/ADs", "Data") != null ? Fsm("JOBS/ADs", "Data").FsmVariables.FindFsmInt("JobStage") : null; if (js != null) js.Value = 2; }
            if (objTest == 2 && t > 27f) { objTest = 3; LogObjectives(); Open = true; Autotest.CaptureSoon("carte-objectifs", 1.5f); }
            if (objTest == 3 && t > 31f)
            {
                objTest = 4; Open = false;
                PlayMakerFSM lv = Fsm("JOBS/HouseShit2/WasteWell_2000litre/Shit/Level/ShitLevelTrigger", "Level"), man = Fsm("JOBS/HouseShit2/LOD/ShitNPC/Man", "Anims");
                FsmBool c = lv != null ? lv.FsmVariables.FindFsmBool("Called") : null, m = man != null ? man.FsmVariables.FindFsmBool("Money") : null;
                if (c != null) c.Value = false; if (m != null) m.Value = true;
            }
            if (objTest == 4 && t > 35f) { objTest = 5; LogObjectives(); Open = true; Autotest.CaptureSoon("carte-objectifs2", 1.5f); }
            if (objTest == 5 && t > 39f) { objTest = 6; Open = false; }
        }
        static void LogObjectives()
        {
            var sb = new System.Text.StringBuilder("autotest objectifs : " + objectives.Count + " :");
            int small = 0;
            foreach (Objective o in objectives) { if (o.Small) { small++; continue; } sb.Append(" [").Append(o.Key).Append("] ").Append(o.Fr).Append(" @").Append(o.At.ToString("F0")).Append(';'); }
            Log.Info(sb.Append(" + ").Append(small).Append(" boites").ToString());
        }

        // Carte : points des objectifs (dores) et la liste en haut a gauche de la vue.
        static void DrawObjectives(Rect local, List<Rect> used)
        {
            if (objectives.Count == 0) return;
            var lines = new List<string>();
            bool tracts = false;
            foreach (Objective o in objectives)
            {
                if (o.Small)
                {
                    tracts = true;
                    Vector2 s = ToScreen(local, o.At.x, o.At.z);
                    if (!local.Contains(s)) continue;
                    float r = Style.Px(4);
                    Style.Round(new Rect(s.x - r - 1, s.y - r - 1, 2 * r + 2, 2 * r + 2), r + 1, new Color(0f, 0f, 0f, 0.8f));
                    Style.Round(new Rect(s.x - r, s.y - r, 2 * r, 2 * r), r, ObjColor);
                    continue;
                }
                Pin(local, used, o.At, Lang.T(o.Fr, o.En), ObjColor, true);
                lines.Add(Lang.T(o.Fr, o.En));
            }
            if (tracts && tractsLine != null) lines.Add(tractsLine);
            if (lines.Count == 0) return;
            float w = Style.Px(20); foreach (string l in lines) w = Mathf.Max(w, Style.Width(l, 13, true) + Style.Px(30));
            float h = Style.Px(30) + lines.Count * Style.Px(20);
            var box = new Rect(local.x + Style.Px(12), local.y + Style.Px(12), w, h);
            Style.Round(box, Style.Px(10), Kit.A(Style.Shade, 0.82f));
            Style.Text(new Rect(box.x + Style.Px(12), box.y + Style.Px(6), w, Style.Px(18)), Lang.T("TES OBJECTIFS", "YOUR OBJECTIVES"), 11, TextAnchor.MiddleLeft, ObjColor);
            for (int i = 0; i < lines.Count; i++)
            {
                float y = box.y + Style.Px(26) + i * Style.Px(20);
                Style.Round(new Rect(box.x + Style.Px(12), y + Style.Px(7), Style.Px(6), Style.Px(6)), Style.Px(3), ObjColor);
                Style.Text(new Rect(box.x + Style.Px(24), y, w - Style.Px(28), Style.Px(20)), lines[i], 13, TextAnchor.MiddleLeft, Color.white);
            }
        }
    }
}
