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
                if (b != null && t > 20f && Time.frameCount % 150 == 0) Log.Info("autotest : " + car + " en " + b.position.ToString("F1") + " rot " + b.rotation.eulerAngles.ToString("F0") + ", cinematique " + b.isKinematic);
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
            if (mode == "portiere" && t > 38f && step == 0) { step = 1; Log.Info("autotest : " + CarDoors.TestPush(Config.Get("Test", "TestVoiture", "KEKMET(350-400psi)"), -35f)); }
            if (mode == "portiere" && t > 30f && !done)
            {
                done = true;
                Log.Info("autotest : " + CarDoors.TestOpen(Config.Get("Test", "TestVoiture", "KEKMET(350-400psi)"), true));
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
        static string clipShot;
        static float nextSleepLog;
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
