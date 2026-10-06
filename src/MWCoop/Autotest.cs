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
            Cooking.Test(mode, t); Fires.Test(mode, t); Garage.Test(mode, t); Home.Test(mode, t); Gestures.Test(mode, t);
            VehicleSync.Test(mode, t); Jobs.Test(mode, t); Parts.Test(mode, t); Races.Test(mode, t); Rally.Test(mode, t); PushDoors.Test(mode, t);
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
            // Captures : Regarder=1, la camera vise l'avatar le plus proche ; Regarder=2, le milieu du groupe d'avatars
            // (photos a 3-4 joueurs) ; RegarderPos=x,y,z (sans Regarder) : le joueur se tourne vers ce point (pose).
            int look = Config.GetInt("Test", "Regarder", 0);
            if (look == 2) LookAtGroup();
            else if (look != 0) LookAtNearestAvatar();
            else if (Config.Get("Test", "RegarderPos", "").Length > 0 && t > 9f) FacePoint(Config.Get("Test", "RegarderPos", ""));
            if (mode == "cd") TestCd(t);
            if (mode == "prise") TestPlug(t);
            if (mode == "paie") TestPay(t);
            if (mode == "teletexte") TestTeletext(t);
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
                    // Pieces de la voiture qui bougent PAR RAPPORT a elle (banquette, coffre qui tremblent chez le passager) :
                    // deplacement moyen par image de chaque corps et de chaque objet a rendu nomme, dans le repere de la voiture.
                    foreach (Transform c in b.GetComponentsInChildren<Transform>())
                    {
                        if (c == b.transform || (c.GetComponent<Rigidbody>() == null && c.GetComponent<Renderer>() == null)) continue;
                        string cn = c.name.ToLowerInvariant();
                        if (cn.Contains("tire") || cn.Contains("rim") || cn.Contains("wheel") || cn.Contains("hubcap") || cn.Contains("needle")) continue;   // (tournent normalement)
                        Vector3 lp = b.transform.InverseTransformPoint(c.position);
                        Quaternion lr = Quaternion.Inverse(b.rotation) * c.rotation;
                        float[] acc;
                        if (!fluParts.TryGetValue(c, out acc)) { fluParts[c] = new float[] { lp.x, lp.y, lp.z, 0, 0, 0, lr.x, lr.y, lr.z, lr.w }; continue; }
                        var plp = new Vector3(acc[0], acc[1], acc[2]);
                        var plr = new Quaternion(acc[6], acc[7], acc[8], acc[9]);
                        acc[3] += (lp - plp).magnitude; acc[4] += Quaternion.Angle(lr, plr); acc[5] += 1;
                        acc[0] = lp.x; acc[1] = lp.y; acc[2] = lp.z; acc[6] = lr.x; acc[7] = lr.y; acc[8] = lr.z; acc[9] = lr.w;
                    }
                    if (t >= fluLog) { fluLog = t + 2f; if (fluN > 0) { float m = fluSum / fluN; Log.Info("autotest : fluide, vitesse " + m.ToString("F2") + " m/s, ecart-type " + Mathf.Sqrt(Mathf.Max(0f, fluSq / fluN - m * m)).ToString("F2") + ", images immobiles " + fluStill + "/" + fluN); } fluN = 0; fluSum = fluSq = 0f; fluStill = 0;
                        var worst = new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<float, string>>();
                        foreach (var kv in fluParts)
                            if (kv.Key != null && kv.Value[5] > 0)
                            {
                                float mm = kv.Value[3] / kv.Value[5] * 1000f, deg = kv.Value[4] / kv.Value[5];
                                if (mm > 0.5f || deg > 0.05f) worst.Add(new System.Collections.Generic.KeyValuePair<float, string>(mm + deg * 10f, VehicleSync.RelPath(b.transform, kv.Key) + (kv.Key.GetComponent<Rigidbody>() != null ? "[corps " + (kv.Key.GetComponent<Rigidbody>().isKinematic ? "cin" : "dyn") + "]" : "") + " " + mm.ToString("F1") + " mm " + deg.ToString("F2") + " deg"));
                                kv.Value[3] = kv.Value[4] = kv.Value[5] = 0;
                            }
                        worst.Sort((x, y) => y.Key.CompareTo(x.Key));
                        var sbw = new System.Text.StringBuilder("autotest : fluide, pieces qui bougent dans la voiture (par image) :");
                        for (int i = 0; i < worst.Count && i < 8; i++) sbw.Append(' ').Append(worst[i].Value).Append(';');
                        Log.Info(worst.Count == 0 ? "autotest : fluide, aucune piece ne bouge dans la voiture" : sbw.ToString());
                    }
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
            // [Test] Autotest=corps : etat du corps de l'invite garde d'une session a l'autre. Chacun note faim, soif, ivresse
            // a 25 et 50 s ; avec [Test] CorpsPoser=1, l'invite pose faim 77, soif 66, ivresse 1,5 a 30 s (gardes a 40 s).
            if (mode == "corps")
            {
                if ((t > 25f && step == 0) || (t > 50f && step == 2))
                {
                    step++;
                    Log.Info("autotest : corps t=" + t.ToString("F0") + " : faim " + Game.GlobalFloat("PlayerHunger").ToString("F1") + ", soif " + Game.GlobalFloat("PlayerThirst").ToString("F1")
                             + ", ivresse " + Game.GlobalFloat("PlayerDrunk").ToString("F2") + ", fatigue " + Game.GlobalFloat("PlayerFatigue").ToString("F1"));
                }
                if (t > 30f && step == 1)
                {
                    step = 2;
                    if (!MWCoop.Net.Session.IsHost && Config.GetInt("Test", "CorpsPoser", 0) != 0)
                    {
                        Game.SetGlobalFloat("PlayerHunger", 77f); Game.SetGlobalFloat("PlayerThirst", 66f); Game.SetGlobalFloat("PlayerDrunk", 1.5f);
                        Log.Info("autotest : corps : faim 77, soif 66, ivresse 1,5 posees ici");
                    }
                }
            }
            if (mode == "globales" && t > 35f && !done)
            {
                done = true;
                var sb = new System.Text.StringBuilder();
                FsmVariables g = FsmVariables.GlobalVariables;
                foreach (FsmFloat x in g.FloatVariables) sb.Append(" f:").Append(x.Name).Append('=').Append(x.Value.ToString("F2"));
                foreach (FsmInt x in g.IntVariables) sb.Append(" i:").Append(x.Name).Append('=').Append(x.Value);
                foreach (FsmBool x in g.BoolVariables) sb.Append(" b:").Append(x.Name).Append('=').Append(x.Value);
                foreach (FsmString x in g.StringVariables) sb.Append(" s:").Append(x.Name).Append('=').Append(x.Value);
                Log.Info("autotest : globales" + sb);
            }
            // [Test] Autotest=releveco : automates dont une variable ou une action parle de monoxyde (Carbon*, CO), et
            // effets d'image de la camera du joueur (dumps/co.txt, avec parametres).
            // [Test] Autotest=co : a 30 s, mort par le monoxyde comme dans le jeu (PassoutEyes allume, 'Logic' en "Close eyes 2" :
            // paupieres fermees, son baisse, puis Systems/Death). Avec [Test] TestReapparition=n, la reapparition est
            // choisie seule ; a 45 s, etat des paupieres et du son (captures : [Test] Captures=46).
            if (mode == "co" && t > 30f && step == 0)
            {
                step = 1;
                GameObject pe = Game.FindAny("PLAYER/Pivot/AnimPivot/Camera/FPSCamera/FPSCamera/Carbon/PassoutEyes");
                if (pe != null)
                {
                    pe.SetActive(true);
                    PlayMakerFSM l = Game.FsmOn(pe, "Logic");
                    if (l != null) Game.SetState(l, "Close eyes 2");
                    Log.Info("autotest : co : paupieres qui se ferment (" + (l != null ? l.ActiveStateName : "?") + ")");
                }
                else Log.Info("autotest : co : PassoutEyes introuvable");
            }
            if (mode == "co" && t > 45f && step == 1)
            {
                step = 2;
                GameObject pe = Game.FindAny("PLAYER/Pivot/AnimPivot/Camera/FPSCamera/FPSCamera/Carbon/PassoutEyes");
                FsmFloat vol = FsmVariables.GlobalVariables.FindFsmFloat("GameVolume");
                Log.Info("autotest : co : paupieres " + (pe != null && pe.activeInHierarchy ? "FERMEES (allumees)" : "rouvertes") + ", son " + (vol != null ? vol.Value.ToString("F2") : "?"));
            }
            if (mode == "relevebattantes" && t > 40f && !done)
            {
                done = true;
                var sb = new System.Text.StringBuilder();
                int n = 0;
                foreach (PlayMakerFSM f in Resources.FindObjectsOfTypeAll(typeof(PlayMakerFSM)))
                {
                    if (f.hideFlags != HideFlags.None || !f.transform.root.gameObject.activeInHierarchy) continue;
                    try
                    {
                        if (f.Fsm.States.Length > 0 && !f.Fsm.States[0].IsInitialized) f.Fsm.InitData();
                        int torque = 0; bool up = false;
                        foreach (FsmState st in f.Fsm.States)
                            foreach (FsmStateAction a2 in st.Actions)
                            {
                                if (a2 == null) continue;
                                string tn = a2.GetType().Name;
                                if (tn == "AddTorque") torque++;
                                if (tn == "GetMouseButtonUp") up = true;
                            }
                        if (torque == 0) continue;
                        n++;
                        sb.Append(" | ").Append(Recon.Path(f.transform)).Append("::").Append(f.FsmName).Append(" couples ").Append(torque).Append(up ? " (bouton tenu)" : "").Append(" [").Append(Replay.Owner(f) ?? "libre").Append("]");
                    }
                    catch { }
                }
                Log.Info("autotest : battantes : " + n + sb);
            }
            if (mode == "releveco" && t > 40f && !done)
            {
                done = true;
                var hits = new System.Collections.Generic.List<string>();
                foreach (PlayMakerFSM f in Resources.FindObjectsOfTypeAll(typeof(PlayMakerFSM)))
                {
                    if (f.hideFlags != HideFlags.None) continue;
                    bool hit = false;
                    foreach (NamedVariable v in f.FsmVariables.GetAllNamedVariables()) if (v.Name.Contains("Carbon") || v.Name == "CO") hit = true;
                    if (!hit) foreach (FsmFloat v in FsmVariables.GlobalVariables.FloatVariables) { }
                    if (hit) hits.Add(Recon.Path(f.transform));
                }
                var cam = GameObject.Find("PLAYER/Pivot/AnimPivot/Camera/FPSCamera/FPSCamera");
                var sb = new System.Text.StringBuilder();
                if (cam != null) foreach (MonoBehaviour m in cam.GetComponents<MonoBehaviour>()) sb.Append(' ').Append(m.GetType().Name).Append(m.enabled ? "+" : "-");
                Log.Info("autotest : releveco : " + hits.Count + " automates ; camera :" + sb + " ; " + Recon.DumpTargets(string.Join(";", hits.ToArray())));
            }
            if (mode == "releveracine" && t > 45f && !done) { done = true; Log.Info("autotest : releve " + Recon.DumpRoot(Config.Get("Test", "RacineReleve", "RACES"))); }
            if (mode == "releveargent" && t > 45f && !done) { done = true; Log.Info("autotest : releve " + Recon.DumpMoney()); }
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
        static readonly System.Collections.Generic.Dictionary<Transform, float[]> fluParts = new System.Collections.Generic.Dictionary<Transform, float[]>();

        // Boitier de CD et CD : l'hote ouvre le boitier 1 (45 s), en sort le CD (49 s), le pose 1 m plus loin (52 s),
        // referme le boitier (60 s) ; les deux cotes notent le boitier et le CD toutes les 2 s.
        // Teletexte ([Test] Autotest=teletexte) : a 30 s l'hote allume le teletexte avec la telecommande (Use : 'Bool
        // test' -> Open), a 36 s il tape la page 123 (Input : 'Send code', NumberInt) ; les deux notent toutes les 2 s si
        // Systems/TV/Teletext est allume et sa page (Pages.PageNumber). Attendu chez l'invite : allume, 123.
        static float ttLog;
        static void TestTeletext(float t)
        {
            if (MWCoop.Net.Session.IsHost && t > 24f && step == 0)
            {
                step = 10;   // la tele d'abord (la telecommande ne marche que tele allumee)
                GameObject sw = Game.FindAny("YARD/Building/LIVINGROOM/TV/TVSwitch");
                PlayMakerFSM u = sw != null ? Game.FsmOn(sw, "Use") : null;
                if (u != null) { Game.SetState(u, "Switch"); Log.Info("autotest : tele -> " + u.ActiveStateName); }
            }
            // Comme le joueur : il vise la telecommande (Use 'Wait button', le pave Input s'allume), clique (USE :
            // teletexte allume), puis la vise de nouveau et tape 1, 2, 3 (Input 'Wait key' -KEY-> avec Key).
            GameObject rc = Game.FindAny("EQUIPMENTS/tv remote control(item1)");
            PlayMakerFSM use = rc != null ? Game.FsmOn(rc, "Use") : null, inp = rc != null ? Game.FsmOn(rc, "Input") : null;
            if (MWCoop.Net.Session.IsHost && use != null && inp != null)
            {
                if (t > 30f && step == 10) { step = 1; Log.Info("autotest : clic (envoye comme chez le joueur) " + WorldFsms.TestSend("tv remote control(item1)::Use", "Wait button", "USE", "Bool test")); }
                for (int k = 0; k < 3; k++)
                    if (t > 36f + k && step == 2 + k)
                    {
                        step = 3 + k;
                        Game.SetState(use, "Wait button");
                        if (!inp.enabled) inp.enabled = true;
                        Game.SetState(inp, "Wait key");
                        inp.FsmVariables.FindFsmString("Key").Value = (k + 1).ToString();
                        Log.Info("autotest : touche " + (k + 1) + " " + WorldFsms.TestEvent("tv remote control(item1)::Input", "KEY"));
                    }
                if (t > 35f && step == 1) step = 2;
            }
            if (t > 28f && t < 60f && t >= ttLog)
            {
                ttLog = t + 2f;
                GameObject tt = Game.FindAny("Systems/TV/Teletext");
                PlayMakerFSM pages = tt != null ? Game.FsmOn(tt, "Pages") : null;
                FsmInt pn = pages != null ? pages.FsmVariables.FindFsmInt("PageNumber") : null;
                Log.Info("autotest : teletexte " + (tt == null ? "absent" : tt.activeSelf ? "allume" : "eteint") + ", page " + (pn != null ? pn.Value.ToString() : "?"));
            }
        }

        // [Test] Autotest=prise : chacun note toutes les 3 s (16-90 s) les prises du chauffage moteur (cable plug) :
        // etat de 'Data', corps, position ; a 20 s, vidage de leurs parents et de leur prise de voiture (cibles.txt).
        static float plugLog;
        static bool plugDumped;
        static void TestPlug(float t)
        {
            if (t < 16f || t > 90f || t < plugLog) return;
            plugLog = t + 3f;
            var sb = new System.Text.StringBuilder();
            var dump = new System.Collections.Generic.List<string>();
            foreach (PlayMakerFSM f in Resources.FindObjectsOfTypeAll(typeof(PlayMakerFSM)))
            {
                if (f.hideFlags != HideFlags.None || !f.gameObject.name.StartsWith("cable plug")) continue;
                FsmGameObject sock = f.FsmVariables.FindFsmGameObject("Socket");
                sb.Append(" | ").Append(Recon.Path(f.transform)).Append("::").Append(f.FsmName).Append(' ').Append(f.ActiveStateName)
                  .Append(f.gameObject.activeInHierarchy ? "" : " (inactif)").Append(f.GetComponent<Rigidbody>() != null ? " corps" : " sans corps")
                  .Append(" en ").Append(f.transform.position.ToString("F1"))
                  .Append(sock != null && sock.Value != null ? " prise " + Recon.Path(sock.Value.transform) : "")
                  .Append(Replay.Owner(f) != null ? " [" + Replay.Owner(f) + "]" : "");
                if (!plugDumped)
                {
                    dump.Add(Recon.Path(f.transform.parent != null ? f.transform.parent : f.transform));
                    if (sock != null && sock.Value != null) dump.Add(Recon.Path(sock.Value.transform));
                }
            }
            Log.Info("autotest : prises t=" + t.ToString("F0") + (sb.Length > 0 ? sb.ToString() : " aucune"));
            if (!plugDumped && t >= 20f && dump.Count > 0 && Config.GetInt("Test", "VidagePrises", 0) != 0) { plugDumped = true; Log.Info("autotest : vidage prises " + Recon.DumpTargets(string.Join(";", dump.ToArray()))); }
            // Hote : debranche la prise de la maison ([Test] TestPrise, UT de son automate) a 34 s, la rebranche a 55 s.
            if (!MWCoop.Net.Session.IsHost) return;
            int want = t >= 34f && plugStep == 0 ? 1 : t >= 55f && plugStep == 1 ? 2 : 0;
            if (want == 0) return;
            plugStep = want;
            string ut = Config.Get("Test", "TestPrise", "HeaterCable1On");
            foreach (PlayMakerFSM f in Resources.FindObjectsOfTypeAll(typeof(PlayMakerFSM)))
            {
                if (f.hideFlags != HideFlags.None || f.FsmName != "Data" || !f.gameObject.name.StartsWith("cable plug")) continue;
                FsmString u = f.FsmVariables.FindFsmString("UT");
                if (u == null || u.Value != ut) continue;
                string before = f.ActiveStateName;
                Game.SetState(f, want == 1 ? "Heater off" : "Heater on");
                Log.Info("autotest : prise " + ut + (want == 1 ? " debranchee" : " rebranchee") + " ici (" + before + " -> " + f.ActiveStateName + ")");
                return;
            }
            Log.Info("autotest : prise " + ut + " introuvable");
        }
        static int plugStep;

        // [Test] Autotest=paie : enveloppe de paie d'un boulot ([Test] TestEnveloppe, defaut : livraison de bois 1),
        // allumee chez chacun a 25 s avec Money = 123 (comme le boulot rejoue l'aurait fait) ; a 27 s, vidage de son
        // automate ; a 36 s l'hote la prend (etat [Test] TestPrendre, defaut 'State 1', comme le clic) ; argent note
        // a 30, 45 et 60 s. Attendu : +123 chez l'hote, +123 chez l'invite (revenu partage), pas 246.
        static int payStep;
        static float payTrace;
        static GameObject payEnv;
        static int payPrep;
        static string payTraceK;
        static string PayPath { get { return Config.Get("Test", "TestEnveloppe", "JOBS/HouseWood1/LOD/CarPos/NPCWood/WoodCaller1/skeleton/pelvis/spine_middle/spine_upper/collar_right/shoulder_right/arm_right/hand_right/PayMoney"); } }
        static string Money()
        {
            FsmFloat c = FsmVariables.GlobalVariables.FindFsmFloat("PlayerMoney"), b = FsmVariables.GlobalVariables.FindFsmFloat("PlayerBankAccount");
            return "liquide " + (c != null ? c.Value.ToString("F2") : "?") + ", banque " + (b != null ? b.Value.ToString("F2") : "?");
        }
        static void TestPay(float t)
        {
            GameObject env = payEnv != null ? payEnv : Game.FindAny(PayPath);
            // Preparee a 20 s chez tous (montant, sortie de sous le PNJ, nom fixe) mais encore eteinte ; allumee a 25 s.
            // [Test] PaieRetard=n : chez l'invite, allumee n s plus tard (prise par l'hote avant qu'elle n'apparaisse).
            if (payPrep == 0 && t >= 20f)
            {
                payPrep = 1;
                if (env == null) { Log.Info("autotest : paie : enveloppe introuvable " + PayPath); payStep = 9; return; }
                // Montant pose AVANT l'allumage : au demarrage l'automate le compare a 0 et s'eteint s'il est nul.
                PlayMakerFSM u = Game.FsmOn(env, "Use");
                FsmFloat m = u != null ? u.FsmVariables.FindFsmFloat("Money") : null;
                if (m != null) m.Value = 123f;
                // Sortie de sous le PNJ (le boulot eteint le client tant qu'il n'est pas en cours), meme place chez tous.
                env.SetActive(false);
                env.transform.parent = null;
                env.name = "EssaiPaie";   // (pas "MWCoop..." : racines sautees par WorldFsms)
                payEnv = env;
                Log.Info("autotest : paie : " + Wallet.TestEnvelope(u));
            }
            if (payStep == 0 && payPrep == 1 && t >= 25f + (MWCoop.Net.Session.IsHost ? 0 : Config.GetInt("Test", "PaieRetard", 0)))
            {
                payStep = 1;
                env.SetActive(true);
                PlayMakerFSM u = Game.FsmOn(env, "Use");
                Log.Info("autotest : paie : enveloppe allumee (" + (u != null ? u.ActiveStateName : "pas d'automate") + ") ; " + Money());
            }
            if (payStep >= 1 && payStep < 4 && env != null && t >= payTrace)
            {
                payTrace = t + 0.5f;
                Transform off = null;
                for (Transform p = env.transform; p != null; p = p.parent) if (!p.gameObject.activeSelf) off = p;
                PlayMakerFSM u = Game.FsmOn(env, "Use");
                string k = (off != null ? "coupee a " + Recon.Path(off) : "allumee") + ", etat " + (u != null ? u.ActiveStateName : "?");
                if (k != payTraceK) { payTraceK = k; Log.Info("autotest : paie t=" + t.ToString("F1") + " : enveloppe " + k); }
            }
            if (payStep == 1 && t >= 27f) { payStep = 2; if (env != null) Log.Info("autotest : paie : vidage " + Recon.DumpTargets(Recon.Path(env.transform))); }
            if (payStep == 2 && t >= 30f) { payStep = 3; Log.Info("autotest : paie t=30 : " + Money()); }
            if (payStep == 3 && t >= 36f)
            {
                payStep = 4;
                if (MWCoop.Net.Session.IsHost && env != null)
                {
                    // Comme le clic (Wait button -USE-> State 1) : pas un rejeu.
                    PlayMakerFSM u = Game.FsmOn(env, "Use");
                    string before = u != null ? u.ActiveStateName : "?";
                    if (u != null) Game.SetState(u, Config.Get("Test", "TestPrendre", "State 1"));
                    Log.Info("autotest : paie : prise (" + before + " -> " + (u != null ? u.ActiveStateName : "?") + ") ; " + Money());
                }
            }
            if (payStep == 4 && t >= 45f) { payStep = 5; Log.Info("autotest : paie t=45 : " + Money() + (env != null ? ", enveloppe " + (env.activeInHierarchy ? "visible" : "cachee") : "")); }
            if (payStep == 5 && t >= 60f) { payStep = 6; Log.Info("autotest : paie t=60 : " + Money()); }
        }

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
        static void LookAtGroup()
        {
            GameObject p = GameObject.Find("PLAYER");
            Transform cam = p != null ? p.transform.Find("Pivot/AnimPivot/Camera/FPSCamera") : null;
            if (cam == null) return;
            Vector3 sum = Vector3.zero;
            int n = 0;
            foreach (Avatar a in PlayerSync.Avatars) if (a.Root != null) { sum += a.Root.transform.position; n++; }
            if (n == 0) return;
            AimAt(p, cam, sum / n + Vector3.up * 1.0f);
        }

        static void FacePoint(string xyz)
        {
            GameObject p = GameObject.Find("PLAYER");
            Transform cam = p != null ? p.transform.Find("Pivot/AnimPivot/Camera/FPSCamera") : null;
            string[] c = xyz.Split(',');
            if (cam == null || c.Length < 3) return;
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            AimAt(p, cam, new Vector3(float.Parse(c[0], ci), float.Parse(c[1], ci), float.Parse(c[2], ci)));
        }

        static void AimAt(GameObject p, Transform cam, Vector3 target)
        {
            foreach (Behaviour b in p.GetComponents<Behaviour>()) if (b.GetType().Name.Contains("MouseLook")) b.enabled = false;
            foreach (Behaviour b in cam.GetComponents<Behaviour>()) if (b.GetType().Name.Contains("MouseLook")) b.enabled = false;
            Vector3 to = target - cam.position;
            p.transform.rotation = Quaternion.Euler(0, Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg, 0);
            cam.localRotation = Quaternion.Euler(-Mathf.Atan2(to.y, new Vector2(to.x, to.z).magnitude) * Mathf.Rad2Deg, 0, 0);
        }

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
