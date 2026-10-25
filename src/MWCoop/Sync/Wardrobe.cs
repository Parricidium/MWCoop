using UnityEngine;

namespace MWCoop
{
    // Armoire (ᏒullΞᏒ, 17/10 : « changer de tenue -- celles du lanceur -- par l'armoire de la chambre, a l'appartement et chez les
    // parents ») : l'armoire visee a moins de 2 m (HOMENEW : « Closet 1 » ; YARD/Building/BEDROOM1 : « closet ») montre la main
    // du jeu et « TENUE » ; la touche d'usage ([Touches] Usage, F) ouvre l'onglet Personnage du menu (F10), ou l'on change de
    // tenue, de pantalon et de visage. My Winter Car (les armoires du jeu sont les siennes). [Coop] Armoire=0 : rien (choix de
    // chacun ; Menor, 25/10 : « l'armoire est tres buggee, retirez-la »).
    public static class Wardrobe
    {
        static readonly System.Collections.Generic.List<Collider> closets = new System.Collections.Generic.List<Collider>();
        static float scanAt = -1, next; static bool aimed, listed;

        public static void OnLevelLoaded() { closets.Clear(); aimed = false; listed = false; scanAt = PlayerSync.InGame && !Game.Msc ? Time.realtimeSinceStartup + 10f : -1; }

        static void Scan()
        {
            closets.Clear();
            foreach (string path in new[] { "YARD/Building/BEDROOM1/closet" })
            {
                GameObject g = Game.FindAny(path);
                Collider c = g != null ? g.GetComponent<Collider>() : null;
                if (c != null) closets.Add(c);
            }
            GameObject home = Game.FindAny("HOMENEW");
            if (home != null) foreach (Transform t in home.GetComponentsInChildren<Transform>(true)) if (t.name == "Closet 1" && t.GetComponent<Collider>() != null) closets.Add(t.GetComponent<Collider>());
            Log.Info("armoire : " + closets.Count + " armoire(s) (tenues par le menu)");
            listed = true;
            if (Config.GetInt("Test", "ArmoireVisee", 0) != 0) Probe();
        }

        // Essais ([Test] ArmoireVisee=1) : chaque armoire, son collider, et ce que touche un rayon vise sur elle depuis 4 cotes
        // (1,3 m, a la hauteur de son centre et a 1,6 m du sol) -- l'armoire est-elle dans les touches ?
        static void Probe()
        {
            foreach (string root in new[] { "HOMENEW", "YARD" })
            {
                GameObject g = Game.FindAny(root);
                if (g == null) continue;
                foreach (Transform t in g.GetComponentsInChildren<Transform>(true))
                {
                    string n = t.name.ToLowerInvariant();
                    if (!n.Contains("closet") && !n.Contains("wardrobe") && !n.Contains("kaappi") && !n.Contains("cabinet") && !n.Contains("cupboard")) continue;
                    Collider cc = t.GetComponent<Collider>();
                    Log.Info("armoire essai : objet " + Recon.Path(t) + " en " + t.position.ToString("F2") + (cc != null ? " collider " + cc.bounds.size.ToString("F2") : " sans collider"));
                }
            }
            foreach (Collider c in closets)
            {
                Bounds bd = c.bounds;
                Log.Info("armoire essai : " + Recon.Path(c.transform) + " " + c.GetType().Name + (c.enabled ? "" : " (COUPE)") + (c.isTrigger ? " declencheur" : "")
                         + (c.gameObject.activeInHierarchy ? "" : " (ETEINT)") + ", couche " + c.gameObject.layer + ", centre " + bd.center.ToString("F2") + " taille " + bd.size.ToString("F2"));
                foreach (Vector3 dir in new[] { Vector3.forward, Vector3.back, Vector3.left, Vector3.right })
                    foreach (float h in new[] { bd.center.y, bd.min.y + 1.6f })
                    {
                        Vector3 target = new Vector3(bd.center.x, h, bd.center.z), from = target + dir * (Mathf.Max(bd.extents.x, bd.extents.z) + 1.3f);
                        var sb = new System.Text.StringBuilder();
                        bool got = false;
                        RaycastHit[] hs = Physics.RaycastAll(from, (target - from).normalized, 2.2f);
                        System.Array.Sort(hs, (a, x) => a.distance.CompareTo(x.distance));
                        foreach (RaycastHit hh in hs) { sb.Append(' ').Append(hh.collider.name).Append('@').Append(hh.distance.ToString("F2")); if (hh.collider == c) got = true; }
                        Log.Info("armoire essai :   depuis " + dir + " h " + h.ToString("F2") + " : " + (got ? "ARMOIRE TOUCHEE" : "pas l'armoire") + " ;" + sb);
                    }
            }
        }

        public static void Update()
        {
            if (scanAt < 0) return;
            float now = Time.realtimeSinceStartup;
            if (!listed) { if (now < scanAt) return; Scan(); }
            if (closets.Count == 0) return;
            if (now < next) return;
            next = now + 0.1f;
            Transform cam = PlayerSync.LocalCamera;
            bool hit = false;
            if (cam != null && !Menu.Open && !Menu.ChatOpen && Config.GetInt("Coop", "Armoire", 1) != 0)
            {
                foreach (RaycastHit h in Physics.RaycastAll(cam.position, cam.forward, 2.2f))
                    if (closets.Contains(h.collider)) { hit = true; break; }
                if (Config.GetInt("Test", "ArmoireEssai", 0) == 2 && Time.timeSinceLevelLoad > 30f) hit = true;   // (essais : comme visee, ou que l'on soit)
            }
            if (hit != aimed)
            {
                aimed = hit;
                Game.SetGlobalBool("GUIuse", hit);
                Game.SetGlobal("GUIinteraction", hit ? Lang.TG("TENUE", "OUTFIT") : "");
            }
            if (hit && (Input.GetKeyDown(Keys.Use) || Config.GetInt("Test", "ArmoireEssai", 0) != 0))
            {
                aimed = false; Game.SetGlobalBool("GUIuse", false); Game.SetGlobal("GUIinteraction", "");
                Menu.Show(Menu.TabLook);
                Log.Info("armoire : menu Personnage ouvert");
            }
        }
    }
}
