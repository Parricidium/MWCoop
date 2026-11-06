using System.Collections.Generic;
using System.Text.RegularExpressions;
using HutongGames.PlayMaker;
using UnityEngine;

namespace MWCoop
{
    // S'asseoir sur plus de meubles (Menos, 04/11, 8 captures : chaises du pub, des chambres, chaises de jardin, canape et banc du chalet) : le jeu n'a un declencheur
    // (CrouchTrigger*) que sur une partie de ses chaises. Sur les autres, une copie de celui du canape de la maison, comme les fauteuils du cinema : Gestures et l'avatar
    // assis marchent comme sur un siege du jeu. Par le nom du meuble, chez chacun, une fois apres le chargement ; rien a partager. [Test] SiegesPlus=0 : comme avant.
    public static class Sieges
    {
        static readonly Regex Names = new Regex("^(chair|Chair|chair_wood|chair_wooden|chair_pub|chair1x|ChairPlastic|PlasticChair|armchair|bench|kitchen_bench|sofa|sofa_wood|Cottage_sofa|ChairSecretary|ChairBoss|ChairMeetingBoss)( \\d+)?$");
        // (PNJ assis dessus en permanence, tas de la decharge, cinematiques ; maillages decoupes en morceaux : le meuble parent suffit)
        static readonly string[] Skip = { "/Waypoints/", "LANDFILL/", "JAIL/", "MISC/", "COMPUTER/", "/Servant/", "/Cashier/", "/Ventti/", "/Uncle/" };
        static float at = -1f;

        public static void OnLevelLoaded() { at = Application.loadedLevelName == "GAME" ? Time.realtimeSinceStartup + 12f : -1f; }

        public static void Update()
        {
            if (at < 0f || Time.realtimeSinceStartup < at) return;
            at = -1f;
            if (Config.GetInt("Test", "SiegesPlus", 1) == 0) return;
            if (Game.Msc && Config.GetInt("Test", "SiegesMsc", 0) == 0) return;   // (My Summer Car : pas encore essaye, partie de [MSC])
            try { Add(); } catch (System.Exception e) { Log.Warn("sieges : " + e.Message); }
        }

        static void Add()
        {
            GameObject src = Game.FindAny("HOMENEW/FurnitureDisable/sofa/CrouchTriggerBasic") ?? Game.FindAny("COTTAGE/LOD/Chair/CrouchTriggerBasic");
            var existing = new List<Vector3>();
            var furniture = new List<Transform>();
            foreach (Transform t in Resources.FindObjectsOfTypeAll<Transform>())
            {
                if (t.gameObject.hideFlags != HideFlags.None) continue;
                if (t.name.StartsWith("CrouchTrigger")) { existing.Add(t.position); if (src == null) src = t.gameObject; continue; }
                if (Names.IsMatch(t.name)) furniture.Add(t);
            }
            if (src == null) { Log.Info("sieges : pas de declencheur de chaise du jeu a copier"); return; }
            int n = 0, seats = 0;
            var names = new System.Text.StringBuilder();
            foreach (Transform t in furniture)
            {
                string path = Recon.Path(t);
                if (Skipped(path) || Game.UnderPlayer(t) || t.root.name.StartsWith("MWCoop") || VehicleSync.CarRoot(t) != null) continue;
                Bounds bb; if (!Box(t, out bb)) continue;
                if (bb.size.x > 4f || bb.size.z > 4f || bb.size.y > 2f) continue;   // (decor fusionne, pas un meuble)
                if (Near(existing, bb)) continue;
                float wide = Mathf.Max(bb.size.x, bb.size.z);
                int places = wide > 2.2f ? 3 : wide > 1.1f ? 2 : 1;
                Vector3 along = bb.size.x > bb.size.z ? Vector3.right : Vector3.forward;
                for (int i = 0; i < places; i++)
                {
                    Vector3 p = new Vector3(bb.center.x, bb.min.y, bb.center.z) + along * wide * ((i + 0.5f) / places - 0.5f);
                    Make(src, t, p);
                    existing.Add(p);
                    seats++;
                }
                if (n++ < 12) names.Append(' ').Append(path);
            }
            Log.Info("sieges : " + seats + " places ajoutees sur " + n + " meubles :" + names);
        }

        static bool Skipped(string path) { foreach (string s in Skip) if (path.Contains(s)) return true; return false; }

        static bool Box(Transform t, out Bounds bb)
        {
            bb = new Bounds();
            bool any = false;
            foreach (Renderer r in t.GetComponentsInChildren<Renderer>(true))
            {
                if (!any) { bb = r.bounds; any = true; } else bb.Encapsulate(r.bounds);
            }
            return any;
        }

        // Un declencheur du jeu sur ou tout pres du meuble : deja un siege.
        static bool Near(List<Vector3> existing, Bounds bb)
        {
            foreach (Vector3 e in existing)
            {
                float dx = Mathf.Max(Mathf.Abs(e.x - bb.center.x) - bb.extents.x, 0f), dz = Mathf.Max(Mathf.Abs(e.z - bb.center.z) - bb.extents.z, 0f);
                if (dx * dx + dz * dz < 0.09f && Mathf.Abs(e.y - bb.center.y) < 1.5f) return true;
            }
            return false;
        }

        // (Sous le meuble : Gestures y lit le sens du siege et son sol. Droit et a l'echelle du monde, quel que soit l'axe du maillage.)
        static void Make(GameObject src, Transform furn, Vector3 feet)
        {
            var g = (GameObject)Object.Instantiate(src);
            g.name = "CrouchTriggerPlus";
            g.SetActive(false);
            g.transform.SetParent(furn, false);
            g.transform.position = feet;
            g.transform.rotation = Quaternion.identity;
            Vector3 ls = furn.lossyScale;
            g.transform.localScale = new Vector3(Mathf.Abs(ls.x) > 1e-3f ? 1f / ls.x : 1f, Mathf.Abs(ls.y) > 1e-3f ? 1f / ls.y : 1f, Mathf.Abs(ls.z) > 1e-3f ? 1f / ls.z : 1f);
            var cap = g.GetComponent<CapsuleCollider>();
            if (cap != null) { cap.direction = 1; cap.radius = 0.3f; cap.height = 1.8f; cap.center = new Vector3(0f, 0.9f, 0f); }
            foreach (PlayMakerFSM f in g.GetComponents<PlayMakerFSM>())
            {
                if (f.FsmName != "PlayerTrigger") continue;
                FsmBool added = f.FsmVariables.FindFsmBool("Added");
                if (added != null) added.Value = false;   // (la copie s'inscrit elle-meme dans Systems/CrouchTriggers)
            }
            g.SetActive(true);
        }

        // Essais (Autotest=sieges) : le joueur pose sur chaque place ajoutee, l'une apres l'autre ; 'Press return' attendu.
        static List<Transform> testList; static int testI, testTours; static float testNext;
        public static void Test(string mode, float t)
        {
            if (mode != "sieges" || t < 40f || !Net.Session.IsHost) return;
            if (testList == null)
            {
                testList = new List<Transform>();
                string only = Config.Get("Test", "SiegesSeul", "");
                foreach (Transform x in Resources.FindObjectsOfTypeAll<Transform>()) if (x.name == "CrouchTriggerPlus" && x.gameObject.hideFlags == HideFlags.None && (only.Length == 0 || Recon.Path(x).Contains(only))) testList.Add(x);
                Log.Info("autotest : sieges, " + testList.Count + " places");
            }
            CharacterController nudge = Game.PlayerT != null ? Game.PlayerT.GetComponent<CharacterController>() : null;
            if (nudge != null && nudge.enabled && testI > 0 && testI != 9999) nudge.Move(Vector3.down * 0.01f);   // (un deplacement : le declencheur voit le joueur)
            if (Time.realtimeSinceStartup < testNext) return;
            testNext = Time.realtimeSinceStartup + Config.GetFloat("Test", "SiegesPause", 2f);
            if (testI > 0 && testI <= testList.Count)
            {
                Transform p = testList[testI - 1];
                PlayMakerFSM f = Game.FsmOn(p.gameObject, "PlayerTrigger");
                Log.Info("autotest : sieges, " + Recon.Path(p.parent) + " : " + (f != null ? f.ActiveStateName : "?") + (p.gameObject.activeInHierarchy ? "" : " (eteint)"));
            }
            if (testI == testList.Count && testTours++ < Config.GetInt("Test", "SiegesTours", 0)) testI = 0;
            if (testI >= testList.Count || testI >= Config.GetInt("Test", "SiegesMax", 60)) { if (testI != 9999) Log.Info("autotest : sieges, fin"); testI = 9999; return; }
            Transform me = Game.PlayerT;
            if (me != null)
            {
                var cc = me.GetComponent<CharacterController>();
                if (cc != null) cc.enabled = false;
                me.position = testList[testI].position + Vector3.up * 0.3f;
                if (cc != null) cc.enabled = true;
            }
            testI++;
        }
    }
}
