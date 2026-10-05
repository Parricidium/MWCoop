using HutongGames.PlayMaker;
using UnityEngine;

namespace MWCoop
{
    // Scenarios des instances de test ([Test] Autotest=...), pilotes par le contenu (jamais de
    // touches injectees). Le deroule des menus (PasserIntro, Continuer, NouvellePartie) est dans Flow.
    //  marche : le joueur avance et tourne en rond (avatar vu par les autres)
    public static class Autotest
    {
        static float t0 = -1;
        static System.Collections.Generic.List<float> captureTimes;

        public static void Update()
        {
            string mode = Config.Get("Test", "Autotest", "");
            WaitScreen.Test(mode, Time.realtimeSinceStartup);   // au menu : avant le filtre GAME
            // [Test] Captures=s1,s2... : capture d'ecran a ces secondes depuis le lancement (menu compris), dumps\ecran-<s>.png.
            if (captureTimes == null)
            {
                captureTimes = new System.Collections.Generic.List<float>();
                foreach (string c in Config.Get("Test", "Captures", "").Split(',')) { float v; if (float.TryParse(c.Trim(), out v)) captureTimes.Add(v); }
            }
            if (captureTimes.Count > 0 && Time.realtimeSinceStartup >= captureTimes[0])
            {
                string png = System.IO.Path.Combine(System.IO.Path.Combine(Log.DataDir, "dumps"), "ecran-" + ((int)captureTimes[0]).ToString("000") + ".png");
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(png));
                Application.CaptureScreenshot(png);
                Log.Info("autotest : capture " + png + " (" + Application.loadedLevelName + ")");
                captureTimes.RemoveAt(0);
            }
            if (Application.loadedLevelName != "GAME") { t0 = -1; return; }
            if (t0 < 0) t0 = Time.realtimeSinceStartup;
            float t = Time.realtimeSinceStartup - t0;
            // Essais propres a chaque module (chacun ses modes et son compteur d'etapes).
            CarDoors.Test(mode, t); Npcs.Test(mode, t); Props.Test(mode, t); Consume.Test(mode, t);
            Wallet.Test(mode, t); Traffic.Test(mode, t); Machines.Test(mode, t);
            Frost.Test(mode, t); Tow.Test(mode, t); Calls.Test(mode, t); Wear.Test(mode, t);
            VehicleSync.Test(mode, t); Jobs.Test(mode, t); Parts.Test(mode, t);
            MWCoop.Net.SaveTransfer.Test(mode, t); World.Test(mode, t); Menu.Test(mode, t); Studio.Test(mode, t);
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
                // [Test] TestClips=clip@t;clip@t... : a partir de 20 s, un clip toutes les 4 s, capture a +2,5 s.
                string list = Config.Get("Test", "TestClips", "");
                if (list.Length > 0 && t > 20f)
                {
                    string[] items = list.Split(';');
                    int k = (int)((t - 20f) / 4f);
                    if (k < items.Length)
                    {
                        string[] ct = items[k].Split('@');
                        string want = ct[0];
                        float at = ct.Length > 1 ? float.Parse(ct[1], System.Globalization.CultureInfo.InvariantCulture) : 1f;
                        if (Avatar.ForceClip != want || Avatar.ForceTime != at)
                        {
                            Avatar.ForceClip = want; Avatar.ForceTime = at;
                            shotAt = Time.realtimeSinceStartup + 2.5f;
                            seenFlags = -2;
                            clipShot = want + "@" + at;
                        }
                    }
                    else Avatar.ForceClip = null;
                }
                LookAtNearestAvatar();
                // [Test] CapturePeriode=N : en plus, une capture toutes les N s a partir de 20 s (pose-t<s>.png).
                int per = Config.GetInt("Test", "CapturePeriode", 0);
                if (per > 0 && t > 20f && t - lastPeriodic >= per)
                {
                    lastPeriodic = t;
                    string dirp = System.IO.Path.Combine(Log.DataDir, "dumps");
                    System.IO.Directory.CreateDirectory(dirp);
                    string pngp = System.IO.Path.Combine(dirp, "pose-t" + ((int)t).ToString("000") + ".png");
                    Application.CaptureScreenshot(pngp);
                    Log.Info("autotest : capture " + pngp);
                }
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
                    string png = System.IO.Path.Combine(dir, Avatar.ForceClip != null && clipShot != null ? "clip-" + clipShot.Replace("@", "-") + ".png" : "pose-" + seenFlags + ".png");
                    Application.CaptureScreenshot(png);
                    Log.Info("autotest : capture " + png);
                }
            }
            if (mode == "accroupi")
            {
                // Comme la touche du jeu : niveau 1 a 22 s, niveau 2 a 34 s, debout a 46 s (automate Crouch).
                PlayMakerFSM cf = Game.FindFsm("PLAYER", "Crouch");
                if (cf != null && t > 22f && step == 0) { step = 1; Game.SetState(cf, "Move down 1"); Log.Info("autotest : accroupi 1"); }
                if (cf != null && t > 34f && step == 1) { step = 2; Game.SetState(cf, "Move down 2"); Log.Info("autotest : accroupi 2"); }
                if (cf != null && t > 46f && step == 2) { step = 3; Game.SetState(cf, "Move up"); Log.Info("autotest : debout"); }
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
                if (t > 23f && Time.frameCount % 120 == 0 && t < 30f) Log.Info("autotest : yeux " + Seats.DriverEyes(car.Split('(')[0]));
                if (t > 55f && step == 3 && Config.GetInt("Test", "TestSortie", 0) != 0) { step = 4; Log.Info("autotest : sortie -> " + VehicleSync.TestExit(car)); }
                if (t > 56f && step == 4) { step = 5; Log.Info("autotest : apres sortie -> " + VehicleSync.TestExit(car)); }
                if (b != null && t > 25f && t < 37f) { Vector3 f = b.transform.forward; f.y = 0; b.velocity = f.normalized * 8f + Vector3.up * Mathf.Min(b.velocity.y, 0f); }
                if (b != null && t > 20f && Time.frameCount % 150 == 0) Log.Info("autotest : " + car + " en " + b.position.ToString("F1") + " rot " + b.rotation.eulerAngles.ToString("F0") + ", cinematique " + b.isKinematic);
                if (b != null && t > 20f && !b.isKinematic)
                {
                    // Envol ? vitesse verticale et rotation maximales de la voiture conduite.
                    if (b.angularVelocity.magnitude > maxRot) { maxRot = b.angularVelocity.magnitude; if (maxRot > 1f) Log.Info("autotest : rotation max " + maxRot.ToString("F2") + " rad/s"); }
                    if (b.velocity.y > maxUp) { maxUp = b.velocity.y; Log.Info("autotest : vitesse verticale max " + maxUp.ToString("F2") + " m/s, rotation " + b.angularVelocity.magnitude.ToString("F2") + " rad/s en " + b.position.ToString("F1")); }
                }
            }
            if (mode == "audit" && t > 70f && step == 0) { step = 1; Log.Info("autotest : clic " + Audit.TestAct()); }
            if (mode == "audit" && t > 75f && step == 1) { step = 2; Log.Info("autotest : " + Audit.State()); }
            if (mode == "cd") TestCd(t);
            if (mode == "nuages" && !MWCoop.Net.Session.IsHost && t > 30f && step == 0)
            {
                // Invite : nuages inverses ici ; le message suivant de l'hote doit les remettre comme chez lui.
                step = 1;
                GameObject co = GameObject.Find("MAP/WEATHER/Clouds");
                Transform o = co != null ? co.transform.Find("CloudObjects") : null;
                if (o != null) { o.gameObject.SetActive(!o.gameObject.activeSelf); Log.Info("autotest : nuages inverses ici -> " + o.gameObject.activeSelf); }
            }
            if (mode == "nuages" && t > 28f && t < 40f && t >= cdLog)
            {
                cdLog = t + 2f;
                GameObject co = GameObject.Find("MAP/WEATHER/Clouds");
                Transform o = co != null ? co.transform.Find("CloudObjects") : null;
                Log.Info("autotest : nuages " + (o != null ? o.gameObject.activeSelf.ToString() : "?"));
            }
            if (mode == "fluide")
            {
                // Invite : vitesse apparente de la copie de TestVoiture image par image (a-coups = grand ecart-type).
                Rigidbody b = VehicleSync.Body(Config.Get("Test", "TestVoiture", "SORBET(190-200psi)"));
                if (b != null && Time.deltaTime > 0f)
                {
                    Vector3 pos = b.transform.position;
                    if (fluLast != Vector3.zero) { float v = (pos - fluLast).magnitude / Time.deltaTime; fluN++; fluSum += v; fluSq += v * v; if (v < 0.05f) fluStill++; }
                    fluLast = pos;
                    if (t >= fluLog) { fluLog = t + 2f; if (fluN > 0) { float m = fluSum / fluN; Log.Info("autotest : fluide, vitesse " + m.ToString("F2") + " m/s, ecart-type " + Mathf.Sqrt(Mathf.Max(0f, fluSq / fluN - m * m)).ToString("F2") + ", images immobiles " + fluStill + "/" + fluN); } fluN = 0; fluSum = fluSq = 0f; fluStill = 0; }
                }
            }
            if (mode == "mort")
            {
                // [Test] TestCause (booleen de Systems/Death) a 30 s ; TestReapparition=1/2 choisit 3 s apres.
                if (t > 30f && step == 0) { step = 1; Log.Info("autotest : " + Respawn.TestDie(Config.Get("Test", "TestCause", "Hunger"))); }
                if (t > 31f && Time.frameCount % 90 == 0 && t < 45f) Log.Info("autotest : " + Respawn.State());
                if (t > 38f && step == 1)
                {
                    step = 2;
                    string png = System.IO.Path.Combine(System.IO.Path.Combine(Log.DataDir, "dumps"), "reapparition-" + Config.GetInt("Test", "TestReapparition", 1) + ".png");
                    Application.CaptureScreenshot(png);
                    Log.Info("autotest : capture " + png);
                }
            }
            if (mode == "accident")
            {
                // Au volant (mode conduite), la tete du conducteur se detache a 30 s, en roulant.
                string car = Config.Get("Test", "TestVoiture", "SORBET(190-200psi)");
                if (t > 15f && step == 0) { step = 1; Log.Info("autotest : " + VehicleSync.TestEnter(car, false)); }
                if (t > 22f && step == 1) { step = 2; Log.Info("autotest : volant -> " + VehicleSync.TestEnter(car, true)); }
                Rigidbody b = VehicleSync.Body(car);
                if (b != null && t > 25f && t < 31f) { Vector3 f = b.transform.forward; f.y = 0; b.velocity = f.normalized * 8f + Vector3.up * Mathf.Min(b.velocity.y, 0f); }
                if (t > 30f && step == 2) { step = 3; Log.Info("autotest : choc " + Respawn.TestCrash(car)); }
                if (t > 30.5f && Time.frameCount % 90 == 0 && t < 46f) Log.Info("autotest : " + Respawn.State());
                if (t > 46f && step == 3) { step = 4; Log.Info("autotest : remonte " + VehicleSync.TestEnter(car, false)); }
                if (t > 52f && step == 4) { step = 5; Log.Info("autotest : volant -> " + VehicleSync.TestEnter(car, true)); }
                if (t > 54f && step == 5 && Time.frameCount % 120 == 0 && t < 60f) Log.Info("autotest : yeux " + Seats.DriverEyes(car.Split('(')[0]));
            }
            if (mode == "coffre")
            {
                // Invite : ouvre [Test] TestPorte de TestVoiture des que la voiture (conduite par l'autre) roule,
                // la lache, puis la referme 3 s plus tard (bouton tenu jusqu'au claquement).
                string vc = Config.Get("Test", "TestVoiture", "SORBET(190-200psi)"), vp = Config.Get("Test", "TestPorte", "Hatch");
                Rigidbody b = VehicleSync.Body(vc);
                float now = Time.realtimeSinceStartup;
                if (b != null && t > 12f && !coffreStartSet) { coffreStartSet = true; coffreStart = b.position; }
                if (b != null && coffreStartSet && step == 0 && (b.position - coffreStart).magnitude > 3f) { step = 1; coffreT = now; Log.Info("autotest : la voiture roule, ouvre " + CarDoors.TestOpen(vc, true, vp)); }
                if (step == 1 && now - coffreT > 0.6f) { step = 2; coffreT = now; Log.Info("autotest : lache " + CarDoors.TestState(vc, "Mouse off", vp)); }
                if (step == 2 && now - coffreT > 3f) { step = 3; Log.Info("autotest : referme " + CarDoors.TestGrab(vc, vp)); }
                if (b != null && step >= 1 && Time.frameCount % 60 == 0) Log.Info("autotest : " + CarDoors.StateOf(vc, vp) + ", voiture en " + b.position.ToString("F1"));
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
            if (mode == "quete")
            {
                // [Test] TestQuete=partie de cle, TestEvenement=evenement envoye a 30 s ; etat a 40 s.
                string part = Config.Get("Test", "TestQuete", "WoodJob1Point");
                if (t > 30f && step == 0)
                {
                    step = 1;
                    Log.Info("autotest : quete " + Jobs.TestEvent(part, Config.Get("Test", "TestEvenement", "ORDER")));
                    if (Config.GetInt("Test", "TestPaie", 0) != 0) Log.Info("autotest : le boulot paie, " + Wallet.Test(300));
                }
                if (t > 40f && step == 1) { step = 2; Log.Info("autotest : quete " + Jobs.StateOf(part)); }
            }
            if (mode == "etatquete")
            {
                string part = Config.Get("Test", "TestQuete", "WoodJob1Point");
                string st = Jobs.StateOf(part);
                // Le boulot rejoue ici paie lui aussi (simule) : ne doit pas etre compte deux fois.
                if (Config.GetInt("Test", "TestPaie", 0) != 0 && step == 0 && st.EndsWith("Activate order")) { step = 1; Log.Info("autotest : le boulot rejoue paie, " + Wallet.Test(300)); }
                if (t > 45f && !done) { done = true; Log.Info("autotest : quete " + st + ", " + Wallet.State()); }
            }
            if (mode == "desynchro" && t > 6f && !done) { done = true; Log.Info("autotest : porte locale seulement " + Interactions.TestLocalDoor(GameObject.Find("PLAYER").transform.position)); }
            // [Test] Autotest2=etat : apres l'essai principal, l'hote met TestQuete dans TestEtat a 45 s.
            if (Config.Get("Test", "Autotest2", "") == "etat" && MWCoop.Net.Session.IsHost && t > 45f && step2 == 0)
            { step2 = 1; Log.Info("autotest : " + WorldFsms.TestState(Config.Get("Test", "TestQuete", "TVPrograms::Schedule"), Config.Get("Test", "TestEtat", "News"))); }
            if (mode == "phares" && t > 20f && !done) { done = true; Log.Info("autotest : " + Interactions.TestNamed(Config.Get("Test", "TestObjet", "KEKMET(350-400psi)/LOD/Dashboard/ButtonLightModes"))); }
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
            // [Test] SuivrePiece=~gasoline : position de cet objet a 46 s (l'autre joueur le promene).
            string watchKey = Config.Get("Test", "SuivrePiece", "");
            if (watchKey.Length > 0 && t > Config.GetInt("Test", "SuivreDelai", 46) && !watchLogged)
            {
                watchLogged = true;
                string k = watchKey.StartsWith("~") ? Props.FindKey(watchKey.Substring(1)) : watchKey;
                Log.Info("autotest : " + k + " est en " + Props.Where(k));
            }
            if (mode == "porter")
            {
                // [Test] TestPiece promenee de 30 a 36 s (comme tenue en main), position a 45 s.
                string id = Config.Get("Test", "TestPiece", "VIN2121");
                if (id.StartsWith("~")) id = Props.FindKey(id.Substring(1));
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
            if (mode == "sauver" && (t > 55f && step == 0 || t > 75f && step == 1 || t > 95f && step == 2 && Config.Get("Test", "Autotest2", "") == "porte"))
            {
                // Etat des automates Use (articles) avant et apres SAVEGAME : le jeu ne sauve qu'en quittant.
                step++;
                var sb = new System.Text.StringBuilder("autotest : automates Use " + (step == 1 ? "avant" : "apres") + " SAVEGAME :");
                foreach (PlayMakerFSM f in Object.FindObjectsOfType<PlayMakerFSM>())
                    if (f.FsmName == "Use" || f.FsmName == "Data" && f.gameObject.name.Contains("(itemx)"))
                        sb.Append(' ').Append(f.gameObject.name).Append('=').Append(f.ActiveStateName).Append(';');
                Log.Info(sb.ToString());
            }
            if (mode == "sauver" && t > 60f && !done)
            {
                // Sauvegarde complete comme le jeu (1482 automates ecoutent SAVEGAME).
                done = true;
                Game.SaveInPlace();
                Log.Info("autotest : SAVEGAME envoye");
            }
            if (mode == "tchat" && t > 12f && !done)
            {
                done = true;
                Chat.Send("Salut, c'est " + Net.Session.Me.Name + " !");
            }
            // [Test] TestNourriture=Sausages : le jeu cree ce produit a 25 s (meme compteur, meme ID des deux cotes).
            string food = Config.Get("Test", "TestNourriture", "");
            if (food.Length > 0 && t > 25f && !foodMade)
            {
                foodMade = true;
                foreach (PlayMakerFSM f in Object.FindObjectsOfType<PlayMakerFSM>())
                    if (f.FsmName == food && f.gameObject.name == "CreateItems")
                    {
                        f.SendEvent("SPAWNITEM");
                        GameObject made = f.FsmVariables.GetFsmGameObject("New").Value;
                        Log.Info("autotest : produit cree " + (made != null ? made.name + " " + Props.ItemId(made) : "?"));
                    }
            }
            if (mode == "monde_send" && t > 35f && !done)
            {
                // [Test] TestEnvoi=partie|etat avant|evenement|etat apres
                done = true;
                string[] ps = Config.Get("Test", "TestEnvoi", "OpenMailBox|State 2|CLICK|Open").Split('|');
                Log.Info("autotest : " + WorldFsms.TestSend(ps[0], ps[1], ps[2], ps[3]));
            }
            if (mode == "telephone" && MWCoop.Net.Session.IsHost)
            {
                // L'hote fait sonner le telephone (etat [Test] TestEtat), puis decroche.
                string logic = Config.Get("Test", "TestQuete", "PhoneLogicNEW::Ring");
                if (t > 35f && step == 0) { step = 1; Log.Info("autotest : " + WorldFsms.TestState(logic, Config.Get("Test", "TestEtat", "Joke"))); }
                if (t > 50f && step == 1) { step = 2; Log.Info("autotest : " + WorldFsms.Var(logic, "Wait") + " ; " + WorldFsms.TestState(logic, "Ring")); }
                if (t > 56f && step == 2 && Config.Get("Test", "VidageSonnerie", "").Length > 0) { Log.Info("autotest : vidage " + Recon.DumpTargets(Config.Get("Test", "VidageSonnerie", ""))); step = 21; }
                if (t > 62f && (step == 2 || step == 21)) { step = 3; Log.Info("autotest : " + WorldFsms.TestEvent(Config.Get("Test", "TestSonnerie", "RingingNEW::Ring"), "ANSWER")); }
            }
            if (mode == "colis")
            {
                // Commande AMIS (hote), commande prete des deux cotes, guichet de la poste actif, puis
                // l'hote paie au guichet : le colis doit apparaitre chez les deux.
                bool host = MWCoop.Net.Session.IsHost;
                if (host && t > 35f && step == 0) { step = 1; Log.Info("autotest : " + WorldFsms.TestEvent("OrdersSpawnerAMIS", "SPAWNITEM")); }
                if (!host && t > 35f && step == 0) step = 1;
                if (t > 50f && step == 1)
                {
                    step = 2;
                    GameObject pile = Game.FindAny("PERAPORTTI/ActiveFunctions/Store/PostOffice/NotificationsPile");
                    if (pile != null) pile.SetActive(true);
                    string orders = "";
                    foreach (Object o in Resources.FindObjectsOfTypeAll(typeof(GameObject)))
                    {
                        var g = (GameObject)o;
                        if (g.hideFlags == HideFlags.None && g.name.StartsWith("OrderAMIS") && g.name != "OrderAMIS" && g.transform.root.gameObject.activeInHierarchy)
                        { orders += Recon.Path(g.transform) + (g.activeInHierarchy ? "" : " (inactif)") + " "; if (orders.Length < 300) Log.Info("autotest : " + WorldFsms.TestState(Recon.Path(g.transform) + "::Data", "Idle")); }
                    }
                    Log.Info("autotest : guichet " + (pile != null) + (pile != null ? " actif " + pile.activeInHierarchy : "") + " ; commandes " + orders);
                }
                if (host && t > 62f && step == 2)
                {
                    step = 3;
                    GameObject pile = Game.FindAny("PERAPORTTI/ActiveFunctions/Store/PostOffice/NotificationsPile");
                    PlayMakerFSM pf = pile != null ? Game.FsmOn(pile, "Use") : null;
                    if (pf != null)
                    {
                        Log.Info("autotest : guichet enabled " + pf.enabled + ", demarre " + pf.Fsm.Started + ", fini " + pf.Fsm.Finished + ", etat '" + pf.ActiveStateName + "'");
                        pf.enabled = true;
                    }
                    Log.Info("autotest : " + WorldFsms.TestState("NotificationsPile::Use", "Wait button") + " ; " + WorldFsms.TestEvent("NotificationsPile::Use", "PAY"));
                }
            }
            if (mode == "monde_evt" && t > 35f && !done)
            {
                done = true;
                Log.Info("autotest : " + WorldFsms.TestEvent(Config.Get("Test", "TestQuete", "ElectricityBills1"), Config.Get("Test", "TestEvenement", "GLOBALEVENT")));
            }
            string countWatch = Config.Get("Test", "CompterObjets", "");
            if (countWatch.Length > 0 && Time.frameCount % 600 == 0 && t > 20f)
            {
                int cnt = 0; string names = "";
                foreach (Object o in Resources.FindObjectsOfTypeAll(typeof(GameObject)))
                {
                    var g = (GameObject)o;
                    if (g.hideFlags == HideFlags.None && g.name.StartsWith(countWatch) && g.transform.root.gameObject.activeInHierarchy) { cnt++; if (names.Length < 200) names += g.name + " "; }
                }
                Log.Info("autotest : " + cnt + " objets " + countWatch + "* : " + names);
            }
            if (mode == "sac" && MWCoop.Net.Session.IsHost && t > 30f && step == 0) { step = 1; Log.Info("autotest : " + Shop.TestBuy(Config.Get("Test", "TestProduit", "Sausages"), 2)); }
            if (mode == "sac" && MWCoop.Net.Session.IsHost && t > 42f && step == 1 && Config.Get("Test", "VidageSac", "").Length > 0) { step = 11; Log.Info("autotest : vidage " + Recon.DumpTargets(Config.Get("Test", "VidageSac", ""))); }
            if (mode == "sac" && MWCoop.Net.Session.IsHost && t > 48f && (step == 1 || step == 11) && Config.GetInt("Test", "SacOuvrir", 1) != 0) { step = 2; Log.Info("autotest : " + Consume.TestOpenBag()); }
            if (mode == "fume")
            {
                // Cigarette : en main 20-30 s, tire 30-37 s, souffle 37-41 s, en main, tire 46-50, souffle 50-53.
                int fl = t < 20f ? 0 : t < 30f ? PlayerSync.F_Smoke : t < 37f ? PlayerSync.F_Smoke | PlayerSync.F_Inhale : t < 41f ? PlayerSync.F_Smoke | PlayerSync.F_Exhale
                         : t < 46f ? PlayerSync.F_Smoke : t < 50f ? PlayerSync.F_Smoke | PlayerSync.F_Inhale : t < 53f ? PlayerSync.F_Smoke | PlayerSync.F_Exhale : 0;
                if (fl != PoseFlags) { PoseFlags = fl; Log.Info("autotest : cigarette " + fl); }
            }
            if (mode == "magasin" && MWCoop.Net.Session.IsHost && t > 32f && step == 0) { step = 1; Log.Info("autotest : panier " + Stock.TestCarry(Config.Get("Test", "TestProduit", "Sausages"), 3)); }
            if (mode == "magasin" && MWCoop.Net.Session.IsHost && t > 48f && step == 1) { step = 2; Log.Info("autotest : repose " + Stock.TestCarry(Config.Get("Test", "TestProduit", "Sausages"), -3)); }
            string npcWatch = Config.Get("Test", "SuivrePNJ", "");
            if (npcWatch.Length > 0 && Time.frameCount % 300 == 0 && t > 20f) Log.Info("autotest : pnj " + Npcs.State(npcWatch) + " | garees " + Parked.State());
            string stockWatch = Config.Get("Test", "SuivreStock", "");
            if (stockWatch.Length > 0 && Time.frameCount % 300 == 0 && t > 20f) Log.Info("autotest : rayon " + Stock.State(stockWatch));
            // [Test] SuivreEtat=chemin::automate : etat courant d'un automate quelconque.
            string stateWatch = Config.Get("Test", "SuivreEtat", "");
            if (stateWatch.Length > 0 && Time.frameCount % 120 == 0 && t > 20f)
            {
                string[] pf = stateWatch.Split(new[] { "::" }, System.StringSplitOptions.None);
                GameObject g = Game.FindAny(pf[0]);
                PlayMakerFSM sf = g != null && pf.Length > 1 ? Game.FsmOn(g, pf[1]) : null;
                Log.Info("autotest : etat " + stateWatch + " = " + (sf != null ? sf.ActiveStateName : "?"));
            }
            if (mode == "passager" && Time.frameCount % 120 == 0 && t > 28f) Log.Info("autotest : place " + Seats.PlaceDans(Config.Get("Test", "TestVoiture", "SORBET")));
            string soundWatch = Config.Get("Test", "SuivreSons", "");
            if (soundWatch.Length > 0 && Time.frameCount % 90 == 0 && t > 20f) Log.Info("autotest : sons " + VehicleSync.AudioState(soundWatch));
            if (soundWatch.Length > 0 && Time.frameCount % 900 == 0 && t > 20f) Log.Info("autotest : " + VehicleSync.DtState(soundWatch));
            string activeWatch = Config.Get("Test", "SuivreActif", "");
            if (activeWatch.Length > 0 && Time.frameCount % 300 == 0 && t > 20f)
            {
                GameObject g = Game.FindAny(activeWatch);
                Log.Info("autotest : " + activeWatch + (g == null ? " introuvable" : g.activeSelf ? " actif" : " inactif"));
            }
            string worldWatch = Config.Get("Test", "SuivreMonde", "");
            if (worldWatch.Length > 0 && Time.frameCount % 300 == 0 && t > 20f)
            {
                string[] pv = worldWatch.Split(':');
                Log.Info("autotest : " + WorldFsms.Var(pv[0], pv.Length > 1 ? pv[1] : "Cutoff"));
            }
            if (mode == "molette" && t > 30f && step < 3 && t > 30f + step * 2f)
            {
                step++;
                Log.Info("autotest : " + Jobs.TestSend(Config.Get("Test", "TestQuete", "ButtonHeaterTemp"), "Get scroll", "INCREASE", "Decrease"));
            }
            string varWatch = Config.Get("Test", "SuivreVar", "");
            if (varWatch.Length > 0 && Time.frameCount % 300 == 0 && t > 20f)
            {
                string[] pv = varWatch.Split(':');
                Log.Info("autotest : " + Jobs.Var(pv[0], pv.Length > 1 ? pv[1] : "Angle"));
            }
            if (mode == "habitacle")
            {
                string vc = Config.Get("Test", "TestVoiture", "SORBET");
                if (t > 25f && step == 0) { step = 1; Log.Info("autotest : " + Seats.TestStandIn(vc, Config.GetInt("Test", "TestPlace", 0))); }
                if (t > 26f && step >= 1 && step < 6 && t > 26f + step) { step++; Log.Info("autotest : habitacle " + Seats.PlaceDans(vc)); }
            }
            if (mode == "passager2" && t > 27f && step == 0) { step = 1; Log.Info("autotest : " + Seats.TestStandIn(Config.Get("Test", "TestVoiture", "SORBET"), Config.GetInt("Test", "TestPlace", 0))); }
            if (mode == "passager2" && t > 31f && step == 1) { step = 2; Log.Info("autotest : entree " + Seats.TestEnter()); }
            if (mode == "passager2" && t > 50f && step == 2) { step = 3; Log.Info("autotest : " + Seats.TestLeave()); }
            if (mode == "passager2" && Time.frameCount % 120 == 0 && t > 26f) Log.Info("autotest : place " + Seats.PlaceDans(Config.Get("Test", "TestVoiture", "SORBET")));
            if (mode == "passager" && t > 30f && step == 0) { step = 1; Log.Info("autotest : " + Seats.TestSit(Config.Get("Test", "TestVoiture", "SORBET"), Config.GetInt("Test", "TestPlace", 0))); }
            if (mode == "passager" && t > 60f && step == 1 && Config.GetInt("Test", "TestSortie", 1) != 0) { step = 2; Log.Info("autotest : " + Seats.TestLeave()); }
            if (mode == "tableau" && t > 35f && !done)
            {
                done = true;
                Log.Info("autotest : " + CarVisuals.Test(Config.Get("Test", "TestVoiture", "SORBET(190-200psi)"), Config.Get("Test", "TestElement", "pivot_brake")));
            }
            string visWatch = Config.Get("Test", "SuivreElement", "");
            if (visWatch.Length > 0 && Time.frameCount % 300 == 0 && t > 20f)
                Log.Info("autotest : " + CarVisuals.State(Config.Get("Test", "TestVoiture", "SORBET(190-200psi)"), visWatch));
            if (mode == "portiere" && t > 52f && step == 1 && Config.GetInt("Test", "TestPousse", 0) != 0) { step = 2; Log.Info("autotest : refermee en poussant " + CarDoors.TestState(Config.Get("Test", "TestVoiture", "KEKMET(350-400psi)"), "Reset 2")); }
            if (mode == "portiere" && t > 38f && step == 0) { step = 1; Log.Info("autotest : " + CarDoors.TestPush(Config.Get("Test", "TestVoiture", "KEKMET(350-400psi)"), -35f)); }
            if (mode == "portiere" && t > 30f && !done)
            {
                done = true;
                Log.Info("autotest : " + CarDoors.TestOpen(Config.Get("Test", "TestVoiture", "KEKMET(350-400psi)"), true));
            }
            if (mode == "chemin" || mode == "spam")
            {
                // Portiere [Test] TestVoiture / TestPorte. chemin : l'invite debout dans sa course, l'hote l'ouvre
                // (bouton tenu 0,6 s) ; spam : les deux cliquent au hasard tres vite, puis on compare.
                string vc = Config.Get("Test", "TestVoiture", "SORBET(190-200psi)"), vp = Config.Get("Test", "TestPorte", "DoorRear(right)");
                if (mode == "chemin" && !MWCoop.Net.Session.IsHost && t > 15f && step == 0) { step = 1; Log.Info("autotest : " + CarDoors.TestStandInPath(vc, vp)); }
                if (mode == "chemin" && MWCoop.Net.Session.IsHost && t > 40f && step == 0) { step = 1; Log.Info("autotest : " + CarDoors.TestOpen(vc, true, vp)); }
                if (mode == "chemin" && MWCoop.Net.Session.IsHost && t > 40.6f && step == 1) { step = 2; Log.Info("autotest : relache " + CarDoors.TestState(vc, "Mouse off", vp)); }
                // TestRelache=1 : l'hote appuie pour la refermer et relache tout de suite (avant qu'elle soit fermee).
                if (mode == "chemin" && MWCoop.Net.Session.IsHost && t > 46f && step == 2 && Config.GetInt("Test", "TestRelache", 0) != 0) { step = 30; Log.Info("autotest : appuie pour fermer " + CarDoors.TestGrab(vc, vp)); }
                if (mode == "chemin" && MWCoop.Net.Session.IsHost && t > 46.12f && step == 30) { step = 31; Log.Info("autotest : relache tout de suite " + CarDoors.TestState(vc, "Check position", vp) + " " + CarDoors.StateOf(vc, vp)); }
                if (mode == "chemin" && MWCoop.Net.Session.IsHost && t > 45f && step == 2 && Config.GetInt("Test", "TestPousse", 0) != 0) { step = 22; Log.Info("autotest : refermee en poussant " + CarDoors.TestState(vc, "Reset 2", vp)); }
                if (mode == "chemin" && MWCoop.Net.Session.IsHost && t > 50f && (step == 2 || step == 22)) { step = 3; Log.Info("autotest : pousse vers la fermeture " + CarDoors.TestGrab(vc, vp)); }
                // (fenetre a l'horloge du PC : les deux s'arretent a la meme seconde, en plein echange)
                int sec = System.DateTime.Now.Second;
                if (mode == "spam" && t > 25f && sec >= 5 && sec < 45) { string w = CarDoors.TestRace(vc, vp); if (w != null) Log.Info("autotest : " + w); }
                if (mode == "spam" && t > 25f && (sec == 47 || sec == 51 || sec == 55) && sec != spamLogged) { spamLogged = sec; Log.Info("autotest : portiere a :" + sec + " " + CarDoors.StateOf(vc, vp)); }
                int mark = mode == "spam" ? 0
                         : (t > 62f ? 5 : t > 55f ? 4 : t > 48f ? 3 : t > 44f ? 2 : t > 41f ? 1 : 0);
                if (mark > portiereMark) { portiereMark = mark; Log.Info("autotest : portiere " + mark + " " + CarDoors.StateOf(vc, vp)); }
            }
            string doorWatch = Config.Get("Test", "SuivrePortiere", "");
            if (doorWatch.Length > 0 && Time.frameCount % 300 == 0 && t > 20f) Log.Info("autotest : " + CarDoors.State(doorWatch));
            if (mode == "voix" && t > 35f && !done)
            {
                done = true;
                Log.Info("autotest : voix " + Voices.Test(Config.Get("Test", "TestVoix", "Teimo")));
            }
            if (mode == "manger" && t > 40f && !done)
            {
                done = true;
                Log.Info("autotest : manger " + Consume.TestNearest());
            }
            if (mode == "liquide" && t > 40f && !done)
            {
                done = true;
                Log.Info("autotest : liquide " + Fluids.TestNearest(Config.Get("Test", "TestLiquide", "Fluid")));
            }
            if (mode == "interactifs" && t > 40f && !done) { done = true; Log.Info("autotest : releve " + Recon.DumpInteractive()); }
            if (mode == "monde" && t > 45f && !done) { done = true; Log.Info("autotest : releve " + Recon.DumpWorldFsms()); }
            if (mode == "menu" && t > 70f && !done)
            {
                // L'hote revient au menu (puis Continuer=1 le relance) : les invites doivent suivre.
                done = true;
                Log.Info("autotest : retour au menu");
                Application.LoadLevel("MainMenu");
            }
            if (mode == "dormir" && t > 20f && !done)
            {
                // Le joueur se couche dans le lit le plus proche (fatigue montee pour qu'il dorme
                // plusieurs heures) ; on suit l'heure et la fatigue chaque seconde.
                done = true;
                Vector3 me = GameObject.Find("PLAYER").transform.position;
                PlayMakerFSM bed = null;
                foreach (PlayMakerFSM f in Object.FindObjectsOfType<PlayMakerFSM>())
                    if (f.gameObject.name == "SleepTrigger" && f.FsmName == "Activate"
                        && (bed == null || (f.transform.position - me).sqrMagnitude < (bed.transform.position - me).sqrMagnitude)) bed = f;
                if (bed == null) { Log.Warn("autotest : aucun lit"); return; }
                FsmFloat fat = FsmVariables.GlobalVariables.FindFsmFloat("PlayerFatigue");
                if (fat != null) fat.Value = 60f;
                Log.Info("autotest : au lit " + Recon.Path(bed.transform) + " a " + (bed.transform.position - me).magnitude.ToString("F0") + " m");
                Game.SetState(bed, "Get positions");
                sleepWatch = true;
            }
            if (sleepWatch && Time.realtimeSinceStartup >= nextSleepLog)
            {
                nextSleepLog = Time.realtimeSinceStartup + 1f;
                FsmFloat fat = FsmVariables.GlobalVariables.FindFsmFloat("PlayerFatigue");
                PlayMakerFSM c = Game.FindFsm("MAP/Sun/PivotSun/SUN", "Color");
                bool sl = Game.GlobalBool("PlayerSleeps");
                Log.Info("autotest : dort " + sl + ", heure " + c.FsmVariables.GetFsmInt("Time").Value
                         + ", fatigue " + (fat != null ? fat.Value.ToString("F1") : "?")
                         + ", echelle " + FsmVariables.GlobalVariables.FindFsmFloat("GlobalTimeScale").Value);
                if (!sl && fat != null && fat.Value < 50f) sleepWatch = false;
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

        static bool done, teleported, sleepWatch, foodMade, watchLogged;
        static float cdLog, fluLog, fluSum, fluSq;
        static int fluN, fluStill;
        static Vector3 fluLast;

        // Boitier de CD et CD : l'hote ouvre le boitier 1 (45 s), en sort le CD (49 s), le pose 1 m plus loin (52 s),
        // referme le boitier (60 s) ; les deux cotes notent le boitier et le CD toutes les 2 s.
        static void TestCd(float t)
        {
            GameObject cases = GameObject.Find("Systems/CDs");
            Transform c1 = cases != null ? cases.transform.Find("cd case(item1)") : null;
            if (MWCoop.Net.Session.IsHost)
            {
                if (step == 0 && t > 45f) { step = 1; Log.Info("autotest : cd, ouvre " + CaseClick()); }
                if (step == 1 && t > 49f) step = 2;   // le jeu libere le CD 1 s apres l'ouverture (CHECK -> Detach)
                if (step == 2 && t > 52f)
                {
                    step = 3;
                    GameObject cd = CdObject("CD1");
                    Rigidbody rb = cd != null ? cd.GetComponent<Rigidbody>() : null;
                    if (rb != null && c1 != null) { rb.position = c1.position + Vector3.up * 0.3f + c1.right * 1f; rb.velocity = Vector3.zero; Props.SoonScan(); }
                    Log.Info("autotest : cd, CD1 pose a cote : " + (rb != null ? rb.position.ToString("F2") : "pas de corps"));
                }
                if (step == 3 && t > 56f)
                {
                    // Remis dans le boitier comme le joueur : le point d'insertion a le CD tenu ({Part}, 'Find correct
                    // part'), 'Wait for assembly' attend le clic -> ASSEMBLE.
                    step = 4;
                    GameObject cd = CdObject("CD1");
                    Transform trig = c1 != null ? c1.Find("DiscTriggerCase1") : null;
                    PlayMakerFSM d = trig != null ? Game.FsmOn(trig.gameObject, "Data") : null;
                    if (d != null && cd != null)
                    {
                        d.FsmVariables.FindFsmGameObject("Part").Value = cd;
                        WorldFsms.TestClick("DiscTriggerCase1::Data", "Wait for assembly");
                        Log.Info("autotest : cd, remis dans le boitier " + WorldFsms.TestEvent("DiscTriggerCase1::Data", "ASSEMBLE"));
                    }
                    else Log.Info("autotest : cd, remise impossible (point " + (d != null) + ", CD " + (cd != null) + ")");
                }
                if (step == 4 && t > 62f) { step = 5; Log.Info("autotest : cd, referme " + CaseClick()); }
            }
            if (t > 40f && t < 75f && t - cdLog >= 2f)
            {
                cdLog = t;
                PlayMakerFSM use = c1 != null ? Game.FsmOn(c1.gameObject, "Use") : null;
                FsmBool open = use != null ? use.FsmVariables.FindFsmBool("Open") : null;
                GameObject cd = CdObject("CD1");
                Transform lid = c1 != null ? c1.Find("PivotTop") : null;
                Log.Info("autotest : cd, boitier 1 " + (use != null ? use.ActiveStateName + " Open=" + (open != null && open.Value) : "introuvable")
                         + (lid != null ? " couvercle " + lid.localEulerAngles.ToString("F0") : "")
                         + " ; CD1 " + (cd != null ? "sous " + (cd.transform.parent != null ? cd.transform.parent.name : "(racine)") + " en " + cd.transform.position.ToString("F2")
                                        + (cd.GetComponent<Rigidbody>() != null ? ", physique" : ", sans corps") : "introuvable")
                         + " ; suivis " + Props.Ids("w:cd:"));
            }
        }

        // Comme un vrai clic : 'Wait button' (survol) puis USE dans la meme image (sinon MousePickEvent ressort).
        // Le survol (MousePickEvent, curseur absent) est coupe le temps du clic.
        static string CaseClick()
        {
            GameObject c = GameObject.Find("Systems/CDs/cd case(item1)");
            PlayMakerFSM use = c != null ? Game.FsmOn(c, "Use") : null;
            FsmState wb = use != null ? use.Fsm.GetState("Wait button") : null;
            if (wb == null) return "boitier introuvable";
            var off = new System.Collections.Generic.List<FsmStateAction>();
            foreach (FsmStateAction a in wb.Actions) if (a != null && a.Enabled && a.GetType().Name == "MousePickEvent") { a.Enabled = false; off.Add(a); }
            try
            {
                WorldFsms.TestClick("cd case(item1)::Use", "Wait button");
                return WorldFsms.TestEvent("cd case(item1)::Use", "USE");
            }
            finally { foreach (FsmStateAction a in off) a.Enabled = true; }
        }

        static GameObject CdObject(string name)
        {
            foreach (PlayMakerFSM f in Resources.FindObjectsOfTypeAll(typeof(PlayMakerFSM)))
                if (f.FsmName == "Data" && f.gameObject.name == "cd(itemx)" && f.hideFlags == HideFlags.None)
                {
                    FsmString n = f.FsmVariables.FindFsmString("ThisCD");
                    if (n != null && n.Value == name) return f.gameObject;
                }
            return null;
        }
        static string clipShot;
        static float nextSleepLog;
        public static int PoseFlags;
        static int seenFlags = -1;
        static float shotAt = -1;
        static string bagId;
        static int step, step2, portiereMark, spamLogged = -1;
        static float maxUp = 0.5f, maxRot, coffreT;
        static bool coffreStartSet;
        static Vector3 coffreStart;
        static float lastPeriodic;

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
