using UnityEngine;

namespace MWCoop
{
    // Armoire (ᏒullΞᏒ, 17/10 : « changer de tenue -- celles du lanceur -- par l'armoire de la chambre, a l'appartement et chez les
    // parents ») : l'armoire visee a moins de 2 m (HOMENEW : « Closet 1 » ; YARD/Building/BEDROOM1 : « closet ») montre la main
    // du jeu et « TENUE » ; la touche d'usage ([Touches] Usage, F) ouvre l'onglet Personnage du menu (F10), ou l'on change de
    // tenue, de pantalon et de visage. My Winter Car (les armoires du jeu sont les siennes).
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
            if (cam != null && !Menu.Open && !Menu.ChatOpen)
                foreach (RaycastHit h in Physics.RaycastAll(cam.position, cam.forward, 2.2f))
                    if (closets.Contains(h.collider)) { hit = true; break; }
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
