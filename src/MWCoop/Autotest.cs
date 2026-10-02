using UnityEngine;

namespace MWCoop
{
    // Scenarios des instances de test ([Test] Autotest=...), pilotes par le contenu (jamais de
    // touches injectees). Le deroule des menus (PasserIntro, Continuer, NouvellePartie) est dans Flow.
    //  marche : le joueur avance et tourne en rond (avatar vu par les autres)
    public static class Autotest
    {
        static float t0 = -1;

        public static void Update()
        {
            string mode = Config.Get("Test", "Autotest", "");
            if (mode.Length == 0 || Application.loadedLevelName != "GAME") { t0 = -1; return; }
            if (t0 < 0) t0 = Time.realtimeSinceStartup;
            float t = Time.realtimeSinceStartup - t0;
            if (mode == "marche" && t > 5f)
            {
                GameObject p = GameObject.Find("PLAYER");
                var cc = p != null ? p.GetComponent<CharacterController>() : null;
                if (cc == null) return;
                p.transform.Rotate(0, 40f * Time.deltaTime, 0);
                cc.SimpleMove(p.transform.forward * 1.5f);
            }
            if (mode == "regarde" && t > 3f) LookAtNearestAvatar();
            if (mode == "tchat" && t > 12f && !done)
            {
                done = true;
                Chat.Send("Salut, c'est " + Net.Session.Me.Name + " !");
            }
            if (mode == "heure" && t > 15f && !done)
            {
                // L'hote saute a 18 h : les invites doivent suivre (World).
                done = true;
                PlayMakerFSM c = Game.FindFsm("MAP/Sun/PivotSun/SUN", "Color");
                int h = Config.GetInt("Test", "TestHeure", 18);
                c.FsmVariables.GetFsmInt("Time").Value = h;
                c.SendEvent("TIMESKIP");
                Log.Info("autotest : l'hote passe a " + h + " h");
            }
        }

        static bool done;

        // La camera du joueur suit l'avatar le plus proche (souris du jeu coupee pendant le test).
        static void LookAtNearestAvatar()
        {
            GameObject p = GameObject.Find("PLAYER");
            if (p == null) return;
            Transform cam = p.transform.Find("Pivot/AnimPivot/Camera/FPSCamera");
            Avatar best = null;
            float bd = float.MaxValue;
            foreach (Avatar a in PlayerSync.Avatars)
            {
                if (a.Root == null) continue;
                float d = (a.Root.transform.position - p.transform.position).sqrMagnitude;
                if (d < bd) { bd = d; best = a; }
            }
            if (best == null || cam == null) return;
            var cc = p.GetComponent<CharacterController>();
            if (cc != null && bd < 2.5f * 2.5f)
            {
                Vector3 away = p.transform.position - best.Root.transform.position;
                away.y = 0;
                cc.SimpleMove((away.sqrMagnitude > 0.01f ? away.normalized : Vector3.forward) * 1.5f);
            }
            foreach (Behaviour b in p.GetComponents<Behaviour>()) if (b.GetType().Name.Contains("MouseLook")) b.enabled = false;
            foreach (Behaviour b in cam.GetComponents<Behaviour>()) if (b.GetType().Name.Contains("MouseLook")) b.enabled = false;
            Vector3 to = best.Root.transform.position + Vector3.up * 1.2f - cam.position;
            p.transform.rotation = Quaternion.Euler(0, Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg, 0);
            float pitch = -Mathf.Atan2(to.y, new Vector2(to.x, to.z).magnitude) * Mathf.Rad2Deg;
            cam.localRotation = Quaternion.Euler(pitch, 0, 0);
        }
    }
}
