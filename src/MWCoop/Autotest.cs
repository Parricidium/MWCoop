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
            if (Application.loadedLevelName != "GAME") { t0 = -1; return; }
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
            if (mode == "regarde" && t > 3f)
            {
                LookAtNearestAvatar();
                // Capture 2,5 s apres chaque changement de posture de l'avatar regarde : dumps\pose-<drapeaux>.png
                foreach (Avatar a in PlayerSync.Avatars)
                {
                    int fl = a.Player.State.Flags;
                    if (fl != seenFlags) { seenFlags = fl; shotAt = Time.realtimeSinceStartup + 2.5f; }
                    break;
                }
                if (shotAt > 0 && Time.realtimeSinceStartup >= shotAt)
                {
                    shotAt = -1;
                    string dir = System.IO.Path.Combine(Log.DataDir, "dumps");
                    System.IO.Directory.CreateDirectory(dir);
                    string png = System.IO.Path.Combine(dir, "pose-" + seenFlags + ".png");
                    Application.CaptureScreenshot(png);
                    Log.Info("autotest : capture " + png);
                }
            }
            if (mode == "conduite")
            {
                // Au volant de [Test] TestVoiture a 15 s, puis le vehicule est pousse en avant (8 m/s) 12 s.
                string car = Config.Get("Test", "TestVoiture", "KEKMET(350-400psi)");
                if (t > 15f && step == 0) { step = 1; Log.Info("autotest : " + VehicleSync.TestEnter(car, false)); }
                if (t > 22f && step == 1) { step = 2; Log.Info("autotest : volant -> " + VehicleSync.TestEnter(car, true)); }
                Rigidbody b = VehicleSync.Body(car);
                VehicleSync.TestEngine(t > 25f && t < 37f ? 2000f : -1f, 0.6f);
                if (t > 24f && step == 2) { step = 3; Log.Info("autotest : moteur " + VehicleSync.TestSounds(car, true)); }
                if (b != null && t > 25f && t < 37f) { Vector3 f = b.transform.forward; f.y = 0; b.velocity = f.normalized * 8f + Vector3.up * Mathf.Min(b.velocity.y, 0f); }
                if (b != null && t > 20f && Time.frameCount % 150 == 0) Log.Info("autotest : " + car + " en " + b.position.ToString("F1") + ", cinematique " + b.isKinematic);
            }
            if (mode == "peindre")
            {
                // [Test] TestPiece peinte en rouge a 30 s ; etat a 40 s.
                string id = Config.Get("Test", "TestPiece", "VIN4111");
                if (t > 30f && step == 0) { step = 1; Log.Info("autotest : " + Paint.TestSpray(id, new Color(0.8f, 0.1f, 0.1f, 1f))); }
                if (t > 40f && step == 1) { step = 2; Log.Info("autotest : peinture " + id + " : " + Paint.State(id)); }
            }
            if (mode == "etatpeinture" && t > 36f && !done) { done = true; string id = Config.Get("Test", "TestPiece", "VIN4111"); Log.Info("autotest : peinture " + id + " : " + Paint.State(id)); }
            if (mode == "reglage" && t > 30f && !done) { done = true; Log.Info("autotest : reglage " + Settings.TestNearest()); }
            if (mode == "phares" && t > 20f && !done) { done = true; Log.Info("autotest : " + Interactions.TestNamed("KEKMET(350-400psi)/LOD/Dashboard/ButtonLightModes")); }
            if (mode == "porte" && t > 15f && !done)
            {
                done = true;
                GameObject pl = GameObject.Find("PLAYER");
                Log.Info("autotest : porte " + Interactions.TestNearestDoor(pl.transform.position));
            }
            if (mode == "monter")
            {
                // [Test] TestPiece monte a 30 s, demontee a 45 s (comme les clics du jeu).
                string id = Config.Get("Test", "TestPiece", "VIN413C1");
                if (t > 30f && step == 0) { step = 1; Log.Info("autotest : " + Parts.TestToggle(id)); }
                if (Config.GetInt("Test", "TestVis", 0) != 0)
                {
                    // Vis : 3 crans serres (36-38 s), 1 desserre (40 s), etat a 44 s ; pas de demontage.
                    if (t > 35f + step && step >= 1 && step <= 3) { step++; Log.Info("autotest : vis " + Parts.TestBolt(id, true)); }
                    if (t > 40f && step == 4) { step = 5; Log.Info("autotest : vis " + Parts.TestBolt(id, false)); }
                    if (t > 44f && step == 5) { step = 6; Log.Info("autotest : vis " + Parts.BoltState(id)); }
                }
                else if (t > 45f && step == 1) { step = 2; Log.Info("autotest : " + Parts.TestToggle(id)); }
            }
            if (mode == "porter")
            {
                // [Test] TestPiece promenee de 30 a 36 s (comme tenue en main), position a 45 s.
                string id = Config.Get("Test", "TestPiece", "VIN2121");
                if (t > 30f && t < 36f) { string w = Props.TestCarry(id, t); if (Time.frameCount % 60 == 0) Log.Info("autotest : " + w); }
                if (t > 45f && !done) { done = true; Log.Info("autotest : " + id + " finit en " + Props.Where(id)); }
            }
            // [Test] TestPos=x,y,z : le joueur y est teleporte a 8 s (recos, scenes a cote d'un lieu).
            string tp = Config.Get("Test", "TestPos", "");
            if (tp.Length > 0 && t > 8f && !teleported)
            {
                teleported = true;
                string[] c = tp.Split(',');
                GameObject pl = GameObject.Find("PLAYER");
                var cc = pl.GetComponent<CharacterController>();
                cc.enabled = false;
                pl.transform.position = new Vector3(float.Parse(c[0], System.Globalization.CultureInfo.InvariantCulture),
                    float.Parse(c[1], System.Globalization.CultureInfo.InvariantCulture), float.Parse(c[2], System.Globalization.CultureInfo.InvariantCulture));
                cc.enabled = true;
                Log.Info("autotest : teleporte en " + tp);
            }
            if (mode == "courses" && t > 30f && !done)
            {
                done = true;
                Log.Info("autotest : " + Shop.TestBuy(Config.Get("Test", "TestProduit", "Beer"), 2));
            }
            if (mode == "poses")
            {
                // Une posture toutes les 8 s a partir de 20 s : accroupi, assis, porte, boit, fume, salue, debout.
                int[] seq = { PlayerSync.F_Crouch, PlayerSync.F_Seated, PlayerSync.F_Carry, PlayerSync.F_Drink,
                              PlayerSync.F_Smoke, PlayerSync.F_Hello, 0 };
                int i = t < 20f ? -1 : (int)((t - 20f) / 8f);
                int fl = i >= 0 && i < seq.Length ? seq[i] : 0;
                if (fl != PoseFlags) { PoseFlags = fl; Log.Info("autotest : posture " + fl); }
            }
            if (mode == "courses")
            {
                // Apres l'achat (30 s) : le sac est promene de 36 a 42 s (comme porte), objets a 50 s.
                if (t > 36f && t < 42f)
                {
                    if (bagId == null) { bagId = Props.NearestId("shoppingbag", GameObject.Find("PLAYER").transform.position); Log.Info("autotest : sac " + bagId); }
                    if (bagId != null) Props.TestCarry(bagId, t);
                }
            }
            if ((mode == "courses" || mode == "") && t > 50f && step == 0 && Config.Get("Test", "TestPos", "").Length > 0)
            {
                step = 1;
                Log.Info("autotest : objets autour : " + Props.Near(GameObject.Find("PLAYER").transform.position, 8f));
            }
            if (mode == "argent")
            {
                // Paie de 500 a 25 s (partagee), achat de 120 a 30 s (local), etat a 40 s.
                if (t > 25f && step == 0) { step = 1; Log.Info("autotest : paie, " + Wallet.Test(500)); }
                if (t > 30f && step == 1) { step = 2; Log.Info("autotest : achat, " + Wallet.Test(-120)); }
                if (t > 40f && step == 2) { step = 3; Log.Info("autotest : " + Wallet.State()); }
            }
            if (mode == "etatargent" && t > 35f && !done) { done = true; Log.Info("autotest : " + Wallet.State()); }
            if (mode == "ou" && t > 40f && !done) { done = true; string id = Config.Get("Test", "TestPiece", "VIN2121"); Log.Info("autotest : " + id + " finit en " + Props.Where(id)); }
            if (mode == "etatvis" && t > 40f && !done) { done = true; Log.Info("autotest : vis " + Parts.BoltState(Config.Get("Test", "TestPiece", ""))); }
            if (mode == "sauver" && t > 60f && !done)
            {
                // Sauvegarde complete comme le jeu (1482 automates ecoutent SAVEGAME).
                done = true;
                PlayMakerFSM.BroadcastEvent("SAVEGAME");
                Log.Info("autotest : SAVEGAME envoye");
            }
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

        static bool done, teleported;
        public static int PoseFlags;
        static int seenFlags = -1;
        static float shotAt = -1;
        static string bagId;
        static int step;

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
