using System.Collections.Generic;
using HutongGames.PlayMaker;
using MWCoop.Net;
using UnityEngine;

namespace MWCoop
{
    // Places passagers (le jeu n'a qu'une place, celle du conducteur), faites comme la sienne :
    //  - places : d'apres les yeux du conducteur (DriverHeadPivot + 27 cm vers l'avant, mesure au volant
    //    de la SORBET) : avant droite en symetrique ; banquette a l'arriere pour SORBET, CORRIS, BACHGLOTZ
    //    (demande de JD, 08/10 : une voiture, banquette a 85 cm comme les autres, devant l'essieu arriere) et le
    //    taxi (MACHTWAGEN, sous JOBS/TAXIJOB : voitures de VehicleSync, par leur cle) ; GIFU : le passager et trois
    //    places sur la couchette de la cabine (08/10) ;
    //  - on entre dans l'habitacle jusqu'au siege (pieds sur le plancher, a moins de 38 cm de cote et
    //    50 cm en long de la place) : l'icone passager du jeu s'affiche (GUIpassenger, comme le volant) ;
    //  - ENTREE : le joueur est accroche a la voiture LA OU IL EST (pas de teleportation), tourne vers
    //    l'avant ; l'automate Crouch du joueur passe en « Incar » (variable PlayerInCar), exactement comme
    //    pour le conducteur : il abaisse la camera a la hauteur assise et fige les deplacements ;
    //  - ENTREE de nouveau : decroche sur place (dans l'habitacle), Crouch « Get out » releve la camera.
    // Jamais quand le jeu s'apprete a faire conduire (zone du conducteur en attente d'ENTREE). Les autres
    // voient l'avatar assis, la tete a la place reelle de sa camera (envoyee), qui suit son regard. Les
    // commandes du vehicule restent accessibles et sont rejouees chez tous (Jobs).
    //  - Ceinture du passager avant (pour le decor, demande de JD, 08/10 : le jeu n'en a que pour le conducteur) :
    //    on vise la boucle (celle du conducteur, en miroir) et on clique, comme le conducteur. Bouclee : une copie
    //    en miroir de la ceinture bouclee du conducteur (Fastened_mesh, ou Seatbelts/Close), la ceinture du
    //    passager rangee (PassengerBelt, ou Seatbelts/Passenger) cachee ; chez tous (envoyee avec la place).
    //  - Ceinture du CONDUCTEUR (retour de JD, 07/10 : les autres ne la voyaient pas) : le jeu ne montre la ceinture bouclee
    //    (Fastened_mesh) qu'a celui qui la porte (PlayerSeatbeltsOn, a chaque joueur). Ses automates (Seatbelts/...) ne
    //    sont plus rejoues par Jobs (le rejeu bouclait la ceinture... du joueur d'en face, et un clic du passager sur sa
    //    boucle detachait celle du conducteur) : le conducteur envoie son etat (Msg.Seat, place 0xFF), les autres voient
    //    une copie de la ceinture bouclee sur son avatar (un peu en avant, comme le passager) et la sangle pendante
    //    (DriverBelt/HandleUpPivot) cachee.
    public static class Seats
    {
        class Seat { public string Car; public Transform CarT; public int Index; public Vector3 Head; public float ZoneX = 0.38f, ZoneZ = 0.5f, MaxX = 9f; }
        class Remote { public string Car; public int Index; public Vector3 Head; public bool Belt; }
        // Ceinture du passager avant d'une voiture : modeles du jeu (ceinture bouclee du conducteur, ceinture du
        // passager rangee), boucle (repere voiture), copie en miroir montree quand le passager est attache.
        class Belt { public GameObject Fastened, Open, Copy; public Vector3 Buckle, DriverBuckle, Top, Floor; public bool Anchors; public bool OpenHidden; public GameObject DriverOpen, DriverCopy; public bool DriverOpenHidden; public Mesh Bulged; public bool CopyPlain; public Vector3 MeshBuckle; }
        static readonly Dictionary<int, bool> driverBelt = new Dictionary<int, bool>();   // conducteurs distants : ceinture bouclee
        static bool driverBeltSent;
        static float driverBeltResend;
        const int DriverBeltIndex = 0xFF;
        static readonly Dictionary<Transform, Belt> belts = new Dictionary<Transform, Belt>();
        static bool beltOn, beltHint;
        const float BeltForward = 0f;   // (sangle reliee a ses attaches, quitte a traverser le torse de l'avatar : demande de JD, 09/10 ; avant 0,17 m en avant, elle flottait)
        static readonly List<PlayMakerFSM> driveTriggers = new List<PlayMakerFSM>();
        // Zone du conducteur de chaque voiture (toutes, une place comprise) et voiture qui la porte.
        static readonly List<KeyValuePair<PlayMakerFSM, Transform>> driverZones = new List<KeyValuePair<PlayMakerFSM, Transform>>();
        static readonly HashSet<PlayMakerFSM> zonesOff = new HashSet<PlayMakerFSM>();
        static float nextZones;
        static bool iconOn;

        static readonly List<Seat> seats = new List<Seat>();
        static readonly Dictionary<int, Remote> remote = new Dictionary<int, Remote>();
        static Seat current;
        static Transform pivot, player, cam;
        static CharacterController controller;
        static PlayMakerFSM crouch;
        static float nextScan = -1, satAt, nextResend;
        static int gen = -1;   // VehicleSync.Generation au dernier releve
        static Vector3 headLocal;
        static int debugFrames;
        static float crouchCheckAt = -1;

        public static bool Seated { get { return current != null; } }
        public static Transform LocalCar { get { return current != null ? current.CarT : null; } }
        public static string SeatedCarKey { get { return current != null ? current.Car : null; } }   // (cle VehicleSync de la voiture)
        // Voiture ou le joueur local est assis en passager (null : aucune).
        public static Transform SeatedCar { get { return current != null ? current.CarT : null; } }

        public static void OnLevelLoaded()
        {
            zonesOff.Clear(); driverZones.Clear();
            seats.Clear(); current = null; pivot = null; player = cam = null; controller = null; crouch = null; remote.Clear(); gen = -1;
            belts.Clear(); beltOn = beltHint = false; driverBelt.Clear(); driverBeltSent = false; blockedHandle = null;
            nextScan = PlayerSync.InGame ? Time.realtimeSinceStartup + 14f : -1;
        }

        static void Scan()
        {
            // (Assis : la place gardee pointe sur l'ancienne liste, retrouvee ci-dessous par voiture et rang.)
            Seat was = current;
            seats.Clear();
            driveTriggers.Clear();
            driverZones.Clear();
            gen = VehicleSync.Generation;
            for (int ci = 0; ci < VehicleSync.LocalCount; ci++)
            {
                Rigidbody rb = VehicleSync.LocalBody(ci);
                if (rb == null) continue;
                string n = VehicleSync.LocalKey(ci);
                foreach (PlayMakerFSM f in rb.GetComponentsInChildren<PlayMakerFSM>(true))
                    if (f.FsmName == "PlayerTrigger" && f.gameObject.name.StartsWith("DriveTrigger")) driverZones.Add(new KeyValuePair<PlayMakerFSM, Transform>(f, rb.transform));
                if (n.StartsWith("JONNEZ"))
                {
                    // Jonnez : une place derriere le conducteur, au bout de la selle (demande de JD, 10/10) ; rang 1 (pas de
                    // ceinture). Zone serree : la moto est etroite.
                    Transform jh = Find(rb.transform, "DriverHeadPivot");
                    if (jh != null)
                    {
                        Vector3 jd = rb.transform.InverseTransformPoint(jh.position) + EyeFromPivot;
                        seats.Add(new Seat { Car = n, CarT = rb.transform, Index = 1, Head = new Vector3(jd.x, jd.y + Config.GetFloat("Test", "JonnezLeve", 0.04f), jd.z - Config.GetFloat("Test", "JonnezRecul", 0.38f)), ZoneX = 0.45f, ZoneZ = 0.45f, MaxX = 0.6f });
                    }
                    continue;
                }
                if (n.StartsWith("KEKMET") || n.StartsWith("FLATBED")) continue;   // une seule place
                foreach (PlayMakerFSM f in rb.GetComponentsInChildren<PlayMakerFSM>(true))
                    if (f.FsmName == "PlayerTrigger" && f.gameObject.name.StartsWith("DriveTrigger")) driveTriggers.Add(f);
                Transform dhp = Find(rb.transform, "DriverHeadPivot");
                if (dhp == null) continue;
                Vector3 d = rb.transform.InverseTransformPoint(dhp.position) + EyeFromPivot;
                seats.Add(new Seat { Car = n, CarT = rb.transform, Index = 0, Head = new Vector3(-d.x, d.y, d.z) });
                if (n.StartsWith("SORBET") || n.StartsWith("CORRIS") || n.StartsWith("BACHGLOTZ") || n.StartsWith("MACHTWAGEN"))
                {
                    // Banquette : 85 cm derriere, un peu plus haute.
                    seats.Add(new Seat { Car = n, CarT = rb.transform, Index = 1, Head = new Vector3(d.x, d.y + 0.06f, d.z - 0.85f) });
                    seats.Add(new Seat { Car = n, CarT = rb.transform, Index = 2, Head = new Vector3(-d.x, d.y + 0.06f, d.z - 0.85f) });
                }
                if (n.StartsWith("GIFU"))
                {
                    // Couchette de la cabine (derriere les sieges) : trois places assises cote a cote (demande d'un joueur, 08/10).
                    float bz = d.z + GifuBed.z, by = d.y + GifuBed.y;
                    // (On ne tient pas debout derriere les sieges : zone large, depuis les portieres ; la place avant, plus
                    // proche, passe d'abord tant qu'elle est libre.)
                    for (int k = 0; k < 3; k++) seats.Add(new Seat { Car = n, CarT = rb.transform, Index = 1 + k, Head = new Vector3((k - 1) * GifuBed.x, by, bz), ZoneX = 1.1f, ZoneZ = 1.1f, MaxX = 1.05f });   // (parois de la cabine a +-1,0 m)
                }
            }
            if (was != null)
                foreach (Seat x in seats) if (x.Car == was.Car && x.Index == was.Index) { current = x; break; }
            Log.Info("places passagers : " + seats.Count);
        }

        // Couchette de la GIFU par rapport aux yeux du conducteur : ecart entre les places (x), hauteur (y), recul (z). Coupe de
        // la cabine (essai sondeplaces) : couchette a 1,75 m (repere du camion), toit 2,60, paroi arriere z 1,9, yeux du
        // conducteur 2,05 / z 2,99. Dessous du toit a 2,42 : 67 cm seulement au-dessus de la couchette -> yeux juste sous le toit
        // (2,28), dos a la paroi ; assis tasse (le corps s'enfonce un peu dans la couchette).
        static Vector3 GifuBed { get { return ParseV(Config.Get("Test", "GifuCouchette", ""), new Vector3(0.5f, 0.23f, -0.80f)); } }
        static Vector3 ParseV(string s, Vector3 def)
        {
            string[] c = s.Split(',');
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            float x, y, z;
            if (c.Length == 3 && float.TryParse(c[0], System.Globalization.NumberStyles.Float, ci, out x) && float.TryParse(c[1], System.Globalization.NumberStyles.Float, ci, out y) && float.TryParse(c[2], System.Globalization.NumberStyles.Float, ci, out z)) return new Vector3(x, y, z);
            return def;
        }

        // Yeux du conducteur par rapport a DriverHeadPivot (repere voiture), mesures au volant de la SORBET.
        static readonly Vector3 EyeFromPivot = new Vector3(0f, -0.03f, 0.27f);

        // Yeux du conducteur au repos (repere de la voiture 'car'), d'apres son DriverHeadPivot ; faux sans pivot.
        static readonly Dictionary<Transform, Transform> headPivots = new Dictionary<Transform, Transform>();
        public static bool DriverHead(Transform car, out Vector3 local)
        {
            local = Vector3.zero;
            if (car == null) return false;
            Transform dhp;
            if (!headPivots.TryGetValue(car, out dhp) || (dhp == null && !ReferenceEquals(dhp, null))) { dhp = Find(car, "DriverHeadPivot"); headPivots[car] = dhp; }
            if (dhp == null) return false;
            local = car.InverseTransformPoint(dhp.position) + EyeFromPivot;
            return true;
        }

        static Transform Find(Transform t, string name)
        {
            if (t.name == name) return t;
            foreach (Transform c in t) { Transform r = Find(c, name); if (r != null) return r; }
            return null;
        }

        static bool FindPlayer()
        {
            if (player != null && cam != null) return true;
            GameObject go = GameObject.Find("PLAYER");
            if (go == null) return false;
            player = go.transform;
            controller = go.GetComponent<CharacterController>();
            cam = PlayerSync.LocalCamera ?? player;
            crouch = Game.FsmOn(go, "Crouch");
            return true;
        }

        public static void Update()
        {
            if (!Session.Active || nextScan < 0 || !PlayerSync.InGame) return;
            float now = Time.realtimeSinceStartup;
            // Liste des voitures changee apres le premier releve (taxi active) : releve tout de suite.
            if (now >= nextScan || (gen >= 0 && gen != VehicleSync.Generation)) { nextScan = now + 30f; Scan(); }
            if (!FindPlayer()) return;
            DriverBeltSend(now);
            if (now >= nextZones) { nextZones = now + 0.2f; DriverZones(); }
            if (current != null) TrackSpeed(current.CarT);
            if (current != null)
            {
                // Voiture detruite, ou rangee (taxi remis en place par son travail : SetActive(false), rejoue chez
                // tous) : le joueur accroche dessous serait inactif, sans camera ni commandes -- decroche tout de suite.
                if (pivot == null || current.CarT == null || !current.CarT.gameObject.activeInHierarchy)
                {
                    if (current.CarT != null) Log.Info("passager : " + current.Car + " rangee (inactive)");
                    Leave();
                    return;
                }
                // Endormi a sa place (couchette de la GIFU, retour d'un joueur, 10/10 : « l'avatar reste debout, flotte au
                // reveil, puis on ne peut plus s'asseoir, le jeu croit qu'on l'est ») : le jeu couche le joueur lui-meme ;
                // la place est rendue sans toucher au joueur (ni position, ni commandes : le jeu les reprend au reveil).
                if (Game.GlobalBool("PlayerSleeps")) { LeaveForSleep(); return; }
                if (debugFrames > 0)
                {
                    debugFrames--;
                    Log.Info("passager : image " + Time.frameCount + " joueur " + current.CarT.InverseTransformPoint(player.position).ToString("F3") + " local " + player.localPosition.ToString("F3")
                             + " parent " + (player.parent != null ? player.parent.name : "-") + " echelle " + player.localScale.ToString("F2") + " cc " + (controller != null && controller.enabled)
                             + " pivot " + current.CarT.InverseTransformPoint(pivot.position).ToString("F3"));
                }
                if (Input.GetKeyDown(KeyCode.Return) && now - satAt > 0.6f)
                {
                    // Pas de sortie en roulant (demande de JD, 05/10) : on descend a l'arret.
                    if (Moving(current.CarT)) { Refuse(Lang.T("La voiture roule : attends qu'elle s'arr\u00EAte pour descendre", "The car is moving: wait until it stops to get out")); return; }
                    Leave(); return;
                }
                // Tete : la ou la camera s'est posee (le jeu l'abaisse en 0,4 s), puis renvoyee de temps en temps.
                if (now >= nextResend && now - satAt > 0.7f)
                {
                    nextResend = now + 5f;
                    headLocal = current.CarT.InverseTransformPoint(cam.position);
                    SendSeat(current.Car, current.Index, headLocal);
                }
                BeltInput();
                UpdateBelts();
                return;
            }
            // Sorti dans l'habitacle : une fois la camera relevee par le jeu, accroupi si le toit est au-dessus.
            if (crouchCheckAt > 0 && now >= crouchCheckAt)
            {
                crouchCheckAt = -1;
                if (crouch != null && crouch.ActiveStateName == "Wait key" && UnderRoof()) Game.SetState(crouch, "Move down 1");
            }
            UpdateBelts();
            Seat best = null;
            if (VehicleSync.LocalDriving < 0 && !Game.GlobalBool("PlayerSeated") && !InDriverZone()) best = InZone();
            if (best != null) TrackSpeed(best.CarT);
            Icon(best != null);
            if (best != null && Input.GetKeyDown(KeyCode.Return))
            {
                if (Moving(best.CarT)) Refuse(Lang.T("La voiture roule : on monte \u00E0 l'arr\u00EAt", "The car is moving: get in once it stops"));
                else Sit(best);
            }
        }

        // Vitesse de la voiture d'apres sa position (la copie d'une voiture conduite par un autre est cinematique : sa
        // vitesse physique reste nulle). Plus de 1,5 m/s (5 km/h) : elle roule.
        static Transform speedCar;
        static Vector3 speedPos;
        static float speedAt, speedVal, refusedAt;

        static bool Moving(Transform car)
        {
            if (car == null) return false;
            float now = Time.realtimeSinceStartup;
            if (car != speedCar || now - speedAt > 0.5f) { speedCar = car; speedPos = car.position; speedAt = now; speedVal = 0f; }
            Rigidbody rb = car.GetComponent<Rigidbody>();
            float v = rb != null && !rb.isKinematic ? rb.velocity.magnitude : speedVal;
            return v > 1.5f;
        }

        // Chaque image : vitesse mesuree de la voiture ou l'on est assis, ou de la plus proche place libre.
        static void TrackSpeed(Transform car)
        {
            if (car == null) return;
            float now = Time.realtimeSinceStartup;
            if (car != speedCar) { speedCar = car; speedPos = car.position; speedAt = now; speedVal = 0f; return; }
            float dt = now - speedAt;
            if (dt < 0.1f) return;
            float v = (car.position - speedPos).magnitude / dt;
            speedVal = dt > 0.5f ? v : Mathf.Lerp(speedVal, v, 0.5f);
            speedPos = car.position; speedAt = now;
        }

        static void Refuse(string text)
        {
            if (Time.realtimeSinceStartup - refusedAt < 2f) return;
            refusedAt = Time.realtimeSinceStartup;
            Hud.Toast(text);
        }

        // La place ou se tient le joueur : dans l'habitacle (pieds sur le plancher, pas dehors), a moins de
        // 38 cm de cote et 50 cm en long du siege ; la plus proche.
        static Seat InZone()
        {
            Seat best = null;
            float bestD = 10f;   // (zones bornees par place ; couchette de la GIFU : jusqu'a 1,1 m)
            foreach (Seat s in seats)
            {
                if (s.CarT == null || SeatTaken(s)) continue;
                if ((s.CarT.position - player.position).sqrMagnitude > 25f || !s.CarT.gameObject.activeInHierarchy) continue;   // (taxi range : inactif)
                Vector3 p = s.CarT.InverseTransformPoint(player.position);
                if (!InSeatZone(s, p)) continue;
                float dx = Mathf.Abs(p.x - s.Head.x), dz = Mathf.Abs(p.z - (s.Head.z - 0.1f));
                float d = dx * dx + dz * dz;
                if (d < bestD) { bestD = d; best = s; }
            }
            return best;
        }

        // p : ancre du joueur (25 cm au-dessus de ses pieds) dans le repere de la voiture. Pieds a moins de 1,6 m sous les yeux de
        // la place assise : sinon, debout par terre a cote de la cabine haute de la GIFU, on s'asseyait dehors, sous la cabine
        // (retour de JD, 08/10 ; sol a -0,15, ancre a +0,10 : passait le seul seuil de -0,3). Couchette : dans la cabine (MaxX).
        static bool InSeatZone(Seat s, Vector3 p)
        {
            if (p.y < Mathf.Max(-0.3f, s.Head.y - 1.6f) || p.y > s.Head.y || Mathf.Abs(p.x) > s.MaxX) return false;
            return Mathf.Abs(p.x - s.Head.x) <= s.ZoneX && Mathf.Abs(p.z - (s.Head.z - 0.1f)) <= s.ZoneZ;
        }

        // Zone du conducteur coupee pour le joueur d'ici (son automate 'PlayerTrigger' arrete, icone du volant eteinte) :
        //  - voiture conduite par un autre joueur, assis au volant : le jeu proposait quand meme la place (retour de
        //    JD, 06/10). Un moteur laisse tournant, conducteur sorti, ne coupe rien : on reprend le volant (06/10 soir) ;
        //  - voiture ou l'on est assis en passager : la zone deborde sur la place avant, et l'icone du volant passait
        //    par-dessus les commandes du tableau de bord.
        // Rendue (automate rallume, en attente du joueur) des que ce n'est plus le cas. Jamais celle ou l'on conduit.
        static void DriverZones()
        {
            foreach (KeyValuePair<PlayMakerFSM, Transform> z in driverZones)
            {
                PlayMakerFSM f = z.Key;
                if (f == null || z.Value == null) continue;
                bool off = f.ActiveStateName != "Player in car"
                           && ((current != null && current.CarT == z.Value) || VehicleSync.RemotelySeated(z.Value));
                if (off && !zonesOff.Contains(f))
                {
                    if (f.ActiveStateName == "Press return") Game.SetGlobalBool("GUIdrive", false);
                    f.enabled = false;
                    zonesOff.Add(f);
                    Log.Info("passager : zone du conducteur de " + z.Value.name + " coupee ici (" + (current != null && current.CarT == z.Value ? "assis en passager" : "conduite par un autre") + ")");
                }
                else if (!off && zonesOff.Contains(f))
                {
                    zonesOff.Remove(f);
                    f.enabled = true;
                    if (f.Fsm.GetState("Wait for player") != null) Game.SetState(f, "Wait for player");
                    Log.Info("passager : zone du conducteur de " + z.Value.name + " rendue");
                }
            }
            // (une icone du volant laissee allumee par une zone coupee en cours de route)
            if (zonesOff.Count > 0 && VehicleSync.LocalDriving < 0 && Game.GlobalBool("GUIdrive"))
            {
                bool any = false;
                foreach (KeyValuePair<PlayMakerFSM, Transform> z in driverZones)
                    if (z.Key != null && z.Key.enabled && z.Key.ActiveStateName == "Press return") any = true;
                if (!any) Game.SetGlobalBool("GUIdrive", false);
            }
        }

        // Le jeu s'apprete a faire conduire le joueur (zone du conducteur, attente d'ENTREE) ?
        static bool InDriverZone()
        {
            foreach (PlayMakerFSM f in driveTriggers)
                if (f != null && (f.ActiveStateName == "Press return" || f.ActiveStateName == "Player in car")) return true;
            return false;
        }

        // Icone passager du jeu (comme le volant pour le conducteur) ; on ne l'eteint que si on l'a allumee.
        static void Icon(bool on)
        {
            if (on) { Game.SetGlobalBool("GUIpassenger", true); iconOn = true; }
            else if (iconOn) { Game.SetGlobalBool("GUIpassenger", false); iconOn = false; }
        }

        static bool SeatTaken(Seat s)
        {
            foreach (Remote r in remote.Values) if (r.Car == s.Car && r.Index == s.Index) return true;
            return false;
        }

        // Comme le conducteur : l'automate Crouch du joueur passe en « Incar » (camera assise, joueur fige).
        static void InCar(bool on)
        {
            FsmBool b = crouch != null ? crouch.FsmVariables.FindFsmBool("PlayerInCar") : null;
            if (b != null) b.Value = on;
            else Log.Warn("passager : variable PlayerInCar introuvable (automate Crouch)");
        }

        static void Sit(Seat s)
        {
            Icon(false);
            pivot = new GameObject("MWCoop-SiegePassager").transform;
            pivot.parent = s.CarT;
            pivot.position = player.position;          // la ou il est : pas de teleportation
            pivot.localRotation = Quaternion.identity;
            // Comme l'automate Stopping du jeu (PlayerStop), mais tout de suite : moteur de deplacement et
            // commandes coupes AVANT de bouger le joueur -- sinon le CharacterMotor, une image encore actif
            // avec le controleur coupe, se croit en l'air et remonte le joueur de sa hauteur de marche (0,4 m).
            Motor(false);
            if (controller != null) controller.enabled = false;
            Game.SetGlobalBool("PlayerStop", true);
            player.parent = pivot;
            player.localPosition = Vector3.zero;
            player.localRotation = Quaternion.identity; // tourne vers l'avant de la voiture
            InCar(true);
            current = s;
            satAt = Time.realtimeSinceStartup;
            nextResend = satAt + 0.7f;
            debugFrames = Config.GetInt("Test", "JournalPassager", 0);
            headLocal = s.Head;
            Log.Info("passager : assis dans " + s.Car + " (place " + s.Index + ") en " + s.CarT.InverseTransformPoint(player.position).ToString("F2"));
            SendSeat(s.Car, s.Index, s.Head);
        }

        static void LeaveForSleep()
        {
            string car = current != null ? current.Car : "?";
            current = null;
            beltOn = false;
            BlockHandle(false);
            BeltHint(null);
            Drop(null);
            if (player != null && pivot != null && player.parent == pivot) player.parent = null;
            InCar(false);
            if (pivot != null) Object.Destroy(pivot.gameObject);
            pivot = null;
            Log.Info("passager : endormi dans " + car + ", place rendue (le jeu couche le joueur)");
            SendSeat("", -1, Vector3.zero);
        }

        static void Leave()
        {
            current = null;
            beltOn = false;
            BlockHandle(false);
            BeltHint(null);
            Drop(null);
            if (player != null)
            {
                player.parent = null;                   // sur place, dans l'habitacle
                player.rotation = Quaternion.Euler(0f, player.eulerAngles.y, 0f);
            }
            InCar(false);
            if (controller != null) controller.enabled = true;
            if (player != null) Motor(true);
            Game.SetGlobalBool("PlayerStop", false);
            if (pivot != null) Object.Destroy(pivot.gameObject);
            pivot = null;
            crouchCheckAt = Time.realtimeSinceStartup + 0.6f;
            Log.Info("passager : sorti");
            SendSeat("", -1, Vector3.zero);
        }

        static bool UnderRoof()
        {
            foreach (RaycastHit h in Physics.RaycastAll(player.position, Vector3.up, 1.5f))
                if (h.collider != null && !h.collider.isTrigger && h.collider.transform.root != player.root && VehicleSync.CarRoot(h.collider.transform) != null) return true;
            return false;
        }

        static void Motor(bool on)
        {
            foreach (string n in new[] { "CharacterMotor", "FPSInputController" })
            {
                var b = player.GetComponent(n) as Behaviour;
                if (b != null) b.enabled = on;
            }
        }

        static void SendSeat(string car, int index, Vector3 head)
        {
            if (!Session.Active) return;
            Session.SendAll(new NetWriter(Msg.Seat).U8(Session.LocalId).Str(car).U8(index + 1).Vec(head).U8(beltOn && index == 0 ? 1 : 0), true);
        }

        // ---- ceintures
        // Automate d'une ceinture du conducteur (sous un objet 'Seatbelts') : reserve ici, jamais rejoue.
        public static bool IsBeltFsm(PlayMakerFSM f)
        {
            for (Transform t = f.transform; t != null; t = t.parent)
                if (t.name == "Seatbelts") { Replay.Claim(f, "ceinture"); return true; }
            return false;
        }

        // Conducteur ici : ceinture bouclee (variable du jeu) envoyee a chaque changement, et toutes les 5 s bouclee.
        static void DriverBeltSend(float now)
        {
            bool on = VehicleSync.LocalDriving >= 0 && Game.GlobalBool("PlayerSeatbeltsOn");
            if (on == driverBeltSent && (!on || now < driverBeltResend)) return;
            if (on != driverBeltSent) Log.Info("ceinture conducteur : " + (on ? "bouclee" : "detachee") + " (" + VehicleSync.LocalDrivingName + ")");
            driverBeltSent = on;
            driverBeltResend = now + 5f;
            Session.SendAll(new NetWriter(Msg.Seat).U8(Session.LocalId).Str(VehicleSync.LocalDrivingName ?? "").U8(DriverBeltIndex).U8(on ? 1 : 0), true);
        }
        static Transform FindUnder(Transform t, string name, string parent)
        {
            if (t.name == name && (parent == null || (t.parent != null && t.parent.name == parent))) return t;
            foreach (Transform c in t) { Transform r = FindUnder(c, name, parent); if (r != null) return r; }
            return null;
        }
        static Belt BeltOf(Transform car)
        {
            Belt b;
            if (car == null) return null;
            if (belts.TryGetValue(car, out b)) return b;
            Transform f = FindUnder(car, "Fastened_mesh", null) ?? FindUnder(car, "Close", "Seatbelts");
            Transform o = FindUnder(car, "PassengerBelt", null) ?? FindUnder(car, "Passenger", "Seatbelts");
            Transform dov = FindUnder(car, "HandleUpPivot", "DriverBelt");
            Transform k = FindUnder(car, "SeatbeltLock", null) ?? FindUnder(car, "BuckleUp", null) ?? FindUnder(car, "HandleDownPivot", null);
            b = null;
            if (f != null && f.GetComponent<MeshFilter>() != null)
            {
                b = new Belt { Fastened = f.gameObject, Open = o != null ? o.gameObject : null, DriverOpen = dov != null ? dov.gameObject : null };
                Vector3 kp = k != null ? car.InverseTransformPoint(k.position) : new Vector3(-0.2f, 0.45f, 0f);
                b.Buckle = new Vector3(-kp.x, kp.y, kp.z);   // (cote passager : en miroir)
                b.DriverBuckle = kp;
                // Attaches du conducteur (repere voiture) d'apres le modele de la ceinture bouclee : en haut du montant (point
                // le plus haut), au plancher cote portiere (le plus bas, le plus a l'exterieur).
                Mesh m = f.GetComponent<MeshFilter>().sharedMesh;
                if (m != null && m.isReadable && m.vertexCount > 0)
                {
                    Vector3[] vs = m.vertices;
                    Vector3 top = Vector3.zero, low = Vector3.zero; float minY = float.MaxValue, maxY = float.MinValue;
                    var pts = new Vector3[vs.Length];
                    for (int i = 0; i < vs.Length; i++) { pts[i] = car.InverseTransformPoint(f.TransformPoint(vs[i])); if (pts[i].y > maxY) { maxY = pts[i].y; top = pts[i]; } if (pts[i].y < minY) minY = pts[i].y; }
                    float outward = Mathf.Sign(kp.x == 0 ? -1f : top.x - kp.x), best = float.MinValue;
                    foreach (Vector3 pt in pts) if (pt.y < minY + 0.12f && pt.x * outward > best) { best = pt.x * outward; low = pt; }
                    // Bout de la languette dans le modele (le plus pres de l'axe de la voiture, en bas) : c'est lui qui doit rester
                    // sur la boucle (retour de JD, 10/10 : « la boucle ne rejoint pas l'attache » -- la sangle deformee etait tenue
                    // au point de clic de la boucle, a quelques centimetres : la languette partait avec la sangle).
                    Vector3 mb = kp; float bestIn = float.MaxValue;
                    foreach (Vector3 pt in pts) if (pt.y < minY + 0.3f && Mathf.Abs(pt.x) < bestIn) { bestIn = Mathf.Abs(pt.x); mb = pt; }
                    b.MeshBuckle = mb;
                    b.Top = top; b.Floor = low; b.Anchors = true;
                    Log.Info("ceinture : attaches de " + car.name + " haut " + top.ToString("F2") + ", plancher " + low.ToString("F2") + ", boucle " + kp.ToString("F2") + ", bout de la languette " + mb.ToString("F2") + " (" + ((mb - kp).magnitude * 100f).ToString("F0") + " cm)");
                }
                Log.Info("ceinture passager : " + car.name + " (bouclee " + f.name + ", rangee " + (o != null ? o.name : "-") + ")");
            }
            belts[car] = b;
            return b;
        }

        // Ceinture du passager avant, exactement comme celle du conducteur (automate 'Use' du jeu sur DriverBelt/.../
        // SeatbeltHandle et SeatbeltLock ; demande d'un joueur, 09/10 -- « les textes, la languette qui tourne, les sons ») :
        //  - viser la languette rangee (SeatbeltHandle de PassengerBelt) : "TAKE SEATBELT" ; clic tenu : elle est prise, rattachee
        //    a la camera la ou elle est (comme SetParent du jeu, pose gardee : elle ne tourne plus avec le regard), la sangle du
        //    jeu s'etire ; ni texte ni main pendant qu'on la tient ;
        //  - en la tenant, viser la boucle : "FASTEN SEATBELT" ; lacher la : bouclee (son seatbelt_fasten) ; lachee ailleurs, ou
        //    tiree a plus de 1,5 m du montant : elle se range (seatbelt_retract) ;
        //  - bouclee, viser la boucle : "UNFASTEN SEATBELT" ; clic : detachee (seatbelt_unfasten).
        static float beltHold;
        static void BeltInput()
        {
            Belt b = current.Index == 0 ? BeltOf(current.CarT) : null;
            bool aimBuckle = false, aimHandle = false;
            Transform handle = b != null && b.Open != null ? (heldHandle ?? FindUnder(b.Open.transform, "SeatbeltHandle", null)) : null;
            Transform view = Camera.main != null ? Camera.main.transform : cam;
            if (b != null && view != null)
            {
                Vector3 d = current.CarT.TransformPoint(b.Buckle) - view.position;
                aimBuckle = d.magnitude < 1.3f && Vector3.Angle(view.forward, d) < 16f;
                if (!beltOn && beltHold <= 0f && handle != null && handle.gameObject.activeInHierarchy)
                {
                    Vector3 hd = handle.position - view.position;
                    aimHandle = hd.magnitude < 1.3f && Vector3.Angle(view.forward, hd) < 14f;
                }
            }
            bool holding = beltHold > 0f;
            BlockHandle(aimHandle || aimBuckle || holding);
            BeltHint(beltOn ? (aimBuckle ? "UNFASTEN SEATBELT" : null) : holding ? (aimBuckle ? "FASTEN SEATBELT" : null) : aimHandle ? "TAKE SEATBELT" : null);
            if (beltOn)
            {
                if (heldHandle != null) Drop(b);   // (bouclee autrement : languette reposee)
                if (aimBuckle && Input.GetMouseButtonDown(0))
                {
                    beltOn = false;
                    Foley("seatbelt_unfasten", current.CarT.TransformPoint(b.Buckle));
                    Log.Info("ceinture passager : detachee (" + current.Car + ")");
                    SendSeat(current.Car, current.Index, headLocal);
                }
                beltHold = 0f;
                return;
            }
            if (!holding && aimHandle && Input.GetMouseButtonDown(0)) { Grab(b); Log.Info("ceinture passager : prise (" + current.Car + ")"); return; }
            if (!holding) return;
            Transform pivot = heldHandle != null ? heldPivot : null;
            bool tooFar = pivot != null && (heldHandle.position - pivot.position).magnitude > 1.5f;
            if ((Input.GetMouseButton(0) || testHold) && !tooFar) { beltHold += Time.deltaTime; return; }
            bool fasten = aimBuckle && !tooFar;
            Vector3 at = heldHandle != null ? heldHandle.position : current.CarT.TransformPoint(b.Buckle);
            Drop(b);
            if (fasten)
            {
                beltOn = true;
                Foley("seatbelt_fasten", current.CarT.TransformPoint(b.Buckle));
                Log.Info("ceinture passager : attachee (" + current.Car + ")");
                SendSeat(current.Car, current.Index, headLocal);
            }
            else
            {
                Foley("seatbelt_retract", at);
                Log.Info("ceinture passager : lachee " + (tooFar ? "trop loin du montant" : "hors de la boucle") + ", rangee");
            }
        }

        // Languette prise : rattachee a la camera, a la pose ou elle est (le jeu : SetParent sans remise a zero).
        static bool testHold;   // (essais : clic maintenu simule)
        static Transform heldHandle, heldPivot;
        static Vector3 heldPos, heldScale; static Quaternion heldRot;   // (echelle comprise : rattachee a la camera puis reposee, elle revenait aplatie)
        static void Grab(Belt b)
        {
            Transform h = b != null && b.Open != null ? FindUnder(b.Open.transform, "SeatbeltHandle", null) : null;
            Transform view = Camera.main != null ? Camera.main.transform : cam;
            if (h == null || view == null) return;
            if (!b.Open.activeSelf) { b.Open.SetActive(true); b.OpenHidden = false; }
            heldHandle = h; heldPivot = h.parent; heldPos = h.localPosition; heldRot = h.localRotation; heldScale = h.localScale;
            h.SetParent(view, true);
            beltHold = 0.01f;
        }
        static void Drop(Belt b)
        {
            beltHold = 0f;
            if (heldHandle == null) return;
            if (heldPivot != null) heldHandle.SetParent(heldPivot, false);
            heldHandle.localPosition = heldPos; heldHandle.localRotation = heldRot; heldHandle.localScale = heldScale;
            heldHandle = null; heldPivot = null;
        }

        // Son du jeu (groupe MasterAudio CarFoley, comme les automates de la ceinture du conducteur), a l'endroit donne.
        static readonly HashSet<string> foleyLogged = new HashSet<string>();
        static void Foley(string variation, Vector3 at)
        {
            GameObject g = GameObject.Find("MasterAudio/CarFoley/" + variation);
            AudioSource src = g != null ? g.GetComponent<AudioSource>() : null;
            if (src == null || src.clip == null) { Log.Info("ceinture : son CarFoley/" + variation + " introuvable"); return; }
            AudioSource.PlayClipAtPoint(src.clip, at, src.volume > 0f ? src.volume : 1f);
            if (foleyLogged.Add(variation)) Log.Info("ceinture : son CarFoley/" + variation + " joue");
        }

        // (pose a chaque image tant qu'on vise : les objets du jeu sous le regard -- poignee de la portiere arriere, juste
        // derriere la ceinture rangee -- effacent le texte a chaque image ; il ne restait que la main, sans "BUCKLE UP")
        // La ceinture n'a pas de collisionneur : sous le regard, c'est la poignee de la portiere arriere, juste derriere
        // (SORBET : DoorRear(right)/.../PlayerColl/Handle a 0,39 m, la ceinture a 0,32), qui prenait le clic tenu pour
        // tirer la ceinture -- portiere arriere ouverte. Coupee le temps de viser la ceinture.
        static Collider blockedHandle;
        static int handleLogs;
        static void BlockHandle(bool on)
        {
            if (!on || current == null || cam == null)
            {
                if (blockedHandle != null) blockedHandle.enabled = true;
                blockedHandle = null;
                return;
            }
            if (blockedHandle != null) return;
            foreach (RaycastHit h in Physics.RaycastAll(cam.position, cam.forward, 0.8f))
                if (h.collider.name == "Handle" && h.collider.enabled && h.collider.transform.IsChildOf(current.CarT)) { blockedHandle = h.collider; blockedHandle.enabled = false; if (handleLogs++ < 5) Log.Info("ceinture passager : poignee " + Recon.Path(h.collider.transform) + " coupee le temps de viser"); break; }
        }

        static void BeltHint(string text)
        {
            bool on = text != null;
            if (!on && !beltHint) return;
            beltHint = on;
            Game.SetGlobalBool("GUIuse", on);
            Game.SetGlobal("GUIinteraction", on ? text : "");
        }

        // Montre la ceinture bouclee (copie en miroir) des voitures dont le passager avant est attache, ici ou ailleurs.
        static void UpdateBelts()
        {
            var want = new HashSet<Transform>();
            if (current != null && current.Index == 0 && beltOn) want.Add(current.CarT);
            foreach (Remote r in remote.Values)
                if (r.Index == 0 && r.Belt)
                    foreach (Seat s in seats) if (s.Car == r.Car && s.Index == 0 && s.CarT != null) want.Add(s.CarT);
            var wantDriver = new HashSet<Transform>();
            foreach (KeyValuePair<int, bool> kv in driverBelt)
                if (kv.Value) { Transform t = VehicleSync.RemoteCarTransform(kv.Key); if (t != null) wantDriver.Add(t); }
            foreach (Transform car in want) BeltOf(car);
            foreach (Transform car in wantDriver) BeltOf(car);
            foreach (KeyValuePair<Transform, Belt> kv in belts)
            {
                Belt b = kv.Value;
                if (b == null) continue;
                bool on = kv.Key != null && want.Contains(kv.Key);
                // Le passager d'ici (premiere personne) voit la ceinture comme le conducteur du jeu voit la sienne : le modele
                // tel quel (fait pour la vue des yeux), pas celui deforme autour de l'avatar que voient les autres (demande de
                // JD, 10/10). A la troisieme personne : celle de l'avatar.
                bool plain = on && current != null && current.CarT == kv.Key && current.Index == 0 && beltOn && !ThirdPerson.Active;
                if (on && b.Copy != null && b.CopyPlain != plain) { Object.Destroy(b.Copy); b.Copy = null; }
                if (on && b.Copy == null && b.Fastened != null) { b.Copy = BeltMeshCopy(b, kv.Key, true, plain); b.CopyPlain = plain; }
                else if (!on && b.Copy != null) { Object.Destroy(b.Copy); b.Copy = null; }
                if (b.Open != null && on != b.OpenHidden) { b.Open.SetActive(!on); b.OpenHidden = on; }
                // Conducteur distant boucle : copie (sans miroir) et sangle pendante cachee (rendue si c'est nous qui l'avions cachee).
                bool don = kv.Key != null && wantDriver.Contains(kv.Key);
                if (don && b.DriverCopy == null && b.Fastened != null) b.DriverCopy = BeltCopy(b, kv.Key, false);
                else if (!don && b.DriverCopy != null) { Object.Destroy(b.DriverCopy); b.DriverCopy = null; }
                if (b.DriverOpen != null)
                {
                    if (don && !b.DriverOpenHidden && b.DriverOpen.activeSelf) { b.DriverOpen.SetActive(false); b.DriverOpenHidden = true; }
                    else if (!don && b.DriverOpenHidden) { b.DriverOpen.SetActive(true); b.DriverOpenHidden = false; }
                }
            }
        }

        // Ceinture bouclee montree (passager, ou conducteur distant) : le modele de la ceinture bouclee du jeu (Fastened_mesh :
        // sangle droite, boucle au bout), en miroir pour le passager, a sa place exacte -- ses bouts sur leurs attaches, quitte a
        // traverser le torse de l'avatar (demande de JD, 09/10). (Avant : decalee de 17 cm vers l'avant, elle flottait ; puis
        // des brins dessines, refuses : « la meme que la place conducteur ».)
        static GameObject BeltCopy(Belt b, Transform car, bool mirror) { return BeltMeshCopy(b, car, mirror, false); }

        // Copie du modele : en miroir par le plan median de la voiture (x -> -x) pour le passager, telle quelle pour le conducteur.
        static GameObject BeltMeshCopy(Belt b, Transform car, bool mirror, bool plain)
        {
            Transform src = b.Fastened.transform;
            GameObject c = new GameObject(mirror ? "MWCoop-CeinturePassager" : "MWCoop-CeintureConducteur");
            c.layer = b.Fastened.layer;
            c.transform.parent = car;
            Vector3 p = car.InverseTransformPoint(src.position);
            Quaternion q = Quaternion.Inverse(car.rotation) * src.rotation;
            float fwd; if (!float.TryParse(Config.Get("Test", "CeintureAvance", ""), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out fwd)) fwd = BeltForward;
            Vector3 sc = src.lossyScale, cs = car.lossyScale;
            c.transform.localPosition = new Vector3(mirror ? -p.x : p.x, p.y, p.z + fwd);
            c.transform.localRotation = mirror ? new Quaternion(q.x, -q.y, -q.z, q.w) : q;
            c.transform.localScale = new Vector3((mirror ? -sc.x : sc.x) / cs.x, sc.y / cs.y, sc.z / cs.z);
            c.AddComponent<MeshFilter>().sharedMesh = plain ? src.GetComponent<MeshFilter>().sharedMesh : BulgedMesh(b, car, src);
            MeshRenderer mr = src.GetComponent<MeshRenderer>();
            MeshRenderer cr = c.AddComponent<MeshRenderer>();
            if (mr != null) cr.sharedMaterials = mr.sharedMaterials;
            return c;
        }

        // Le modele deforme au milieu de chaque sangle, pas a ses bouts (haut du montant, plancher et boucle restent sur leurs
        // attaches) : l'avatar est plus epais que le joueur du jeu et la cachait toute entiere, et la diagonale du jeu descend
        // raide le long du cote portiere du torse. Diagonale : avancee devant la poitrine et ramenee vers le milieu (de l'epaule
        // a la boucle) ; sangle du bassin : a peine avancee. Calcule une fois par voiture (memes poids pour la copie en miroir).
        // Modele illisible : tel quel.
        static Mesh BulgedMesh(Belt b, Transform car, Transform src)
        {
            Mesh m = src.GetComponent<MeshFilter>().sharedMesh;
            if (b.Bulged != null) return b.Bulged;
            float bulge = Config.GetFloat("Test", "CeintureBombe", 0.26f), lap = Config.GetFloat("Test", "CeintureBassin", 0.1f),
                  inward = Config.GetFloat("Test", "CeintureMilieu", 0.1f), radius = Config.GetFloat("Test", "CeintureRayon", 0.3f);
            if (!b.Anchors || bulge <= 0f) return m;
            try
            {
                Vector3[] v = m.vertices;
                Vector3 fwd = src.InverseTransformVector(car.TransformVector(Vector3.forward));
                Vector3 side = src.InverseTransformVector(car.TransformVector(Vector3.right * Mathf.Sign(b.MeshBuckle.x - b.Top.x)));
                for (int i = 0; i < v.Length; i++)
                {
                    Vector3 cp = car.InverseTransformPoint(src.TransformPoint(v[i]));
                    // (repere du conducteur : le modele source n'est pas en miroir ; la languette et ses 8 cm ne bougent pas)
                    Vector3 mb = b.MeshBuckle;
                    float dB = Mathf.Max(0f, Vector3.Distance(cp, mb) - Config.GetFloat("Test", "CeintureLanguette", 0.08f));
                    float d = Mathf.Min(Vector3.Distance(cp, b.Top), Mathf.Min(Vector3.Distance(cp, b.Floor), dB));
                    float w = Mathf.SmoothStep(0f, 1f, d / radius), t = Mathf.Clamp01((cp.y - mb.y - 0.05f) / 0.15f);   // t : 1 sur la diagonale, 0 sur la sangle du bassin
                    v[i] += fwd * (Mathf.Lerp(lap, bulge, t) * w) + side * (inward * t * w);
                }
                Mesh n = Object.Instantiate(m);
                n.vertices = v;
                n.RecalculateBounds();
                b.Bulged = n;
                return n;
            }
            catch (System.Exception e) { Log.Info("ceinture : modele non bombe (" + e.Message + ")"); return m; }
        }

        public static void OnMessage(Peer from, NetReader r)
        {
            int who = r.U8();
            if (Session.IsHost) who = from.Id;
            string car = r.Str();
            int raw = r.U8();
            if (raw == DriverBeltIndex)
            {
                bool db = r.U8() != 0;
                if (Session.IsHost) Session.Broadcast(new NetWriter(Msg.Seat).U8(who).Str(car).U8(DriverBeltIndex).U8(db ? 1 : 0), true, who);
                bool was;
                if (!driverBelt.TryGetValue(who, out was) || was != db) Log.Info("ceinture conducteur : #" + who + (db ? " bouclee" : " detachee") + " (" + car + ")");
                driverBelt[who] = db;
                return;
            }
            int index = raw - 1;
            Vector3 head = r.Vec();
            bool belt = r.More && r.U8() != 0;
            if (Session.IsHost) Session.Broadcast(new NetWriter(Msg.Seat).U8(who).Str(car).U8(index + 1).Vec(head).U8(belt ? 1 : 0), true, who);
            Remote old;
            bool had = remote.TryGetValue(who, out old);
            if (index < 0) { remote.Remove(who); if (had) Log.Info("passager : #" + who + " est sorti de " + old.Car); return; }
            remote[who] = new Remote { Car = car, Index = index, Head = head, Belt = belt };
            if (!had || old.Car != car || old.Index != index) Log.Info("passager : #" + who + " assis dans " + car + " (place " + index + ")");
            if (had && old.Belt != belt) Log.Info("ceinture passager : #" + who + (belt ? " attache" : " detache"));
        }

        public static void PlayerLeft(int id) { remote.Remove(id); driverBelt.Remove(id); }

        // Avatar d'un autre joueur assis en passager : voiture locale et place de sa tete (repere voiture).
        public static bool RemoteSeat(int id, out Transform car, out Vector3 head, out string carName)
        {
            car = null; head = Vector3.zero; carName = null;
            // (joueur local -- son avatar a la troisieme personne : sa place d'ici)
            if (id == Net.Session.LocalId)
            {
                if (current == null || current.CarT == null) return false;
                car = current.CarT; carName = current.Car; head = current.Head;
                return true;
            }
            Remote rs;
            if (!remote.TryGetValue(id, out rs)) return false;
            // (Voiture inactive ici -- taxi range : l'avatar reste a la place envoyee.)
            foreach (Seat s in seats)
                if (s.Car == rs.Car && s.CarT != null && s.CarT.gameObject.activeInHierarchy)
                {
                    car = s.CarT; carName = s.Car;
                    head = rs.Head != Vector3.zero ? rs.Head : SeatHead(rs.Car, rs.Index);
                    return true;
                }
            return false;
        }

        // Place d'un autre joueur assis (rang ; -1 : pas assis).
        public static int RemoteSeatIndex(int id) { if (id == Net.Session.LocalId) return current != null ? current.Index : -1; Remote rs; return remote.TryGetValue(id, out rs) ? rs.Index : -1; }

        static Vector3 SeatHead(string car, int index)
        {
            foreach (Seat s in seats) if (s.Car == car && s.Index == index) return s.Head;
            return Vector3.zero;
        }

        // Essais : assoit le joueur local a la place 'index' de 'car' (debout dans l'habitacle, puis ENTREE).
        // Essais : autour de chaque place de la voiture 'car' : surface sous la tete (distance, objet), parois devant,
        // derriere, a gauche, a droite (rayons depuis la tete).
        public static string ProbeSeats(string car)
        {
            if (seats.Count == 0) Scan();
            var sb = new System.Text.StringBuilder();
            foreach (Seat s in seats)
            {
                if (!s.Car.StartsWith(car) || s.CarT == null) continue;
                Vector3 h = s.CarT.TransformPoint(s.Head);
                sb.Append(" | place ").Append(s.Index).Append(' ').Append(s.Head.ToString("F2")).Append(" :");
                Vector3[] dirs = { -s.CarT.up, s.CarT.up, s.CarT.forward, -s.CarT.forward, -s.CarT.right, s.CarT.right };
                string[] nm = { "bas", "haut", "avant", "arriere", "gauche", "droite" };
                for (int k = 0; k < dirs.Length; k++)
                {
                    RaycastHit hit = new RaycastHit(); bool any = false;
                    foreach (RaycastHit x in Physics.RaycastAll(h, dirs[k], 3f)) if (!x.collider.isTrigger && Game.RootName(x.collider.transform) != "PLAYER" && (!any || x.distance < hit.distance)) { hit = x; any = true; }
                    if (any) sb.Append(' ').Append(nm[k]).Append(' ').Append(hit.distance.ToString("F2")).Append(" (").Append(hit.collider.name).Append(')');
                    else sb.Append(' ').Append(nm[k]).Append(" -");
                }
            }
            // Ancre du joueur (PLAYER) par rapport a ses pieds, et sol a cote de la cabine (repere de la voiture) : la zone d'une
            // place doit exclure un joueur debout par terre (retour de JD, 08/10 : assis sous la cabine de la GIFU).
            // Points types (ancre du joueur, repere de la voiture) : places dont la zone les accepte.
            Vector3[] pts = { new Vector3(-1.4f, 0.10f, 2.19f), new Vector3(-1.2f, 0.9f, 2.4f), new Vector3(-0.5f, 1.35f, 2.4f), new Vector3(0.7f, 1.35f, 2.9f) };
            string[] pn = { "par terre a gauche", "marchepied gauche", "plancher de la cabine", "debout a la place avant" };
            for (int i = 0; i < pts.Length; i++)
            {
                sb.Append(" | ").Append(pn[i]).Append(" :");
                bool any = false;
                foreach (Seat s in seats) if (s.Car.StartsWith(car) && InSeatZone(s, pts[i])) { sb.Append(" place ").Append(s.Index); any = true; }
                if (!any) sb.Append(" aucune");
            }
            GameObject pl = GameObject.Find("PLAYER");
            CharacterController cc = pl != null ? pl.GetComponent<CharacterController>() : null;
            if (cc != null) sb.Append(" | joueur : pieds a ").Append((cc.center.y - cc.height / 2f).ToString("F2")).Append(" m de son ancre");
            foreach (Seat s in seats)
            {
                if (!s.Car.StartsWith(car) || s.CarT == null) continue;
                foreach (float sx in new[] { -1.7f, 1.7f })
                {
                    Vector3 o = s.CarT.TransformPoint(new Vector3(sx, 3.5f, s.Head.z));
                    float best = float.MaxValue;
                    foreach (RaycastHit x in Physics.RaycastAll(o, -s.CarT.up, 8f)) if (!x.collider.isTrigger && x.collider.transform.root != s.CarT.root && x.distance < best) best = x.distance;
                    sb.Append(" | sol a x ").Append(sx.ToString("F1")).Append(" z ").Append(s.Head.z.ToString("F2")).Append(" : ").Append(best < float.MaxValue ? (3.5f - best).ToString("F2") : "?");
                }
                break;
            }
            return sb.ToString();
        }

        // Essais : coupe de la cabine (repere de la voiture) : pour x et z donnes, toutes les surfaces traversees par un rayon
        // vertical descendant depuis 3 m (hauteur locale, objet).
        public static string ProbeGrid(string car)
        {
            if (seats.Count == 0) Scan();
            Transform ct = null;
            foreach (Seat s in seats) if (s.Car.StartsWith(car) && s.CarT != null) { ct = s.CarT; break; }
            if (ct == null) return "voiture absente";
            var sb = new System.Text.StringBuilder();
            for (float z = 3.2f; z >= 1.2f; z -= 0.2f)
                for (float x = -0.5f; x <= 0.51f; x += 0.5f)
                {
                    Vector3 o = ct.TransformPoint(new Vector3(x, 3.5f, z));
                    var hits = Physics.RaycastAll(o, -ct.up, 4f);
                    System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
                    sb.Append(" | x ").Append(x.ToString("F1")).Append(" z ").Append(z.ToString("F1")).Append(" :");
                    foreach (RaycastHit h in hits) if (!h.collider.isTrigger && h.collider.transform.root == ct) sb.Append(' ').Append(ct.InverseTransformPoint(h.point).y.ToString("F2")).Append('(').Append(h.collider.name).Append(')');
                }
            return sb.ToString();
        }

        public static string TestSit(string car, int index)
        {
            if (!FindPlayer()) return "pas de joueur";
            if (seats.Count == 0) Scan();
            foreach (Seat s in seats)
                if (s.Car.StartsWith(car) && s.Index == index && s.CarT != null)
                {
                    StandIn(s);
                    Sit(s);
                    return "assis dans " + s.Car + " place " + index;
                }
            return "aucune place " + index + " sur " + car + " (" + seats.Count + ")";
        }

        // Essais : comme ENTREE la ou se tient le joueur (zone d'une place).
        public static string TestEnter()
        {
            if (!FindPlayer()) return "pas de joueur";
            Seat z = InZone();
            if (z == null) return "hors zone : " + PlaceDans("");
            Sit(z);
            return "assis place " + z.Index;
        }

        static void StandIn(Seat s)
        {
            if (controller != null) controller.enabled = false;
            player.position = s.CarT.TransformPoint(s.Head + new Vector3(0f, -0.55f, -0.1f));
            if (controller != null) controller.enabled = true;
        }

        // Essais : met le joueur debout dans l'habitacle, a la place 'index' ; dit si la zone le voit.
        public static string TestStandIn(string car, int index)
        {
            if (!FindPlayer()) return "pas de joueur";
            if (seats.Count == 0) Scan();
            foreach (Seat s in seats)
                if (s.Car.StartsWith(car) && s.Index == index && s.CarT != null) { StandIn(s); return "debout dans " + s.Car + " place " + index + " : " + PlaceDans(car); }
            return "aucune place";
        }

        public static string PlaceDans(string car)
        {
            if (!FindPlayer()) return "?";
            foreach (Seat s in seats)
                if ((car.Length == 0 || s.Car.StartsWith(car)) && s.CarT != null && (car.Length > 0 || (s.CarT.position - player.position).sqrMagnitude < 25f))
                {
                    Seat z = current == null ? InZone() : null;
                    return "joueur " + s.CarT.InverseTransformPoint(player.position).ToString("F2") + ", camera " + s.CarT.InverseTransformPoint(cam.position).ToString("F2")
                           + (current != null ? ", assis place " + current.Index : z != null ? ", zone place " + z.Index : ", hors zone");
                }
            return "?";
        }

        // Essais : camera du joueur local et DriverHeadPivot dans le repere de la voiture 'car'.
        public static string DriverEyes(string car)
        {
            if (!FindPlayer()) return "pas de joueur";
            for (int ci = 0; ci < VehicleSync.LocalCount; ci++)
            {
                Rigidbody rb = VehicleSync.LocalBody(ci);
                if (rb == null || !rb.name.StartsWith(car)) continue;
                Transform dhp = Find(rb.transform, "DriverHeadPivot");
                return car + " : camera " + rb.transform.InverseTransformPoint(cam.position).ToString("F3")
                       + ", DriverHeadPivot " + (dhp != null ? rb.transform.InverseTransformPoint(dhp.position).ToString("F3") : "?");
            }
            return "pas de " + car;
        }

        public static string TestLeave() { if (current == null) return "pas assis"; Leave(); return "sorti"; }

        // Essais : attache / detache la ceinture du passager (comme un clic sur la boucle).
        // Essais : ce que voit la ceinture du passager avant depuis la camera (repere de la voiture) : camera, ceinture rangee
        // (centre et taille de ses rendus), boucle, distances et angles au regard, et ce que touche un rayon vers la ceinture.
        public static string TestBeltAim()
        {
            if (current == null || cam == null) return "pas assis";
            Belt b = current.Index == 0 ? BeltOf(current.CarT) : null;
            if (b == null) return "pas de ceinture ici";
            Transform car = current.CarT;
            var sb = new System.Text.StringBuilder("ceinture passager : camera " + car.InverseTransformPoint(cam.position).ToString("F2"));
            Vector3 bp = car.TransformPoint(b.Buckle);
            sb.Append(", boucle ").Append(b.Buckle.ToString("F2")).Append(" a ").Append((bp - cam.position).magnitude.ToString("F2")).Append(" m ")
              .Append(Vector3.Angle(cam.forward, bp - cam.position).ToString("F0")).Append(" deg");
            Renderer or = b.Open != null ? b.Open.GetComponentInChildren<Renderer>() : null;
            if (or == null) sb.Append(", ceinture rangee : aucun rendu (" + (b.Open != null ? b.Open.name : "-") + ")");
            else
            {
                Vector3 c = or.bounds.center;
                sb.Append(", ceinture rangee ").Append(or.name).Append(" centre ").Append(car.InverseTransformPoint(c).ToString("F2")).Append(" taille ").Append(or.bounds.size.ToString("F2"))
                  .Append(" a ").Append((c - cam.position).magnitude.ToString("F2")).Append(" m ").Append(Vector3.Angle(cam.forward, c - cam.position).ToString("F0")).Append(" deg");
                foreach (RaycastHit h in Physics.RaycastAll(cam.position, (c - cam.position).normalized, 2f))
                    sb.Append(" | rayon : ").Append(Recon.Path(h.collider.transform)).Append(" a ").Append(h.distance.ToString("F2")).Append(h.collider.isTrigger ? " (declencheur)" : "");
            }
            return sb.ToString();
        }

        public static string TestBeltCopy()
        {
            if (current == null) return "pas assis";
            Belt b = BeltOf(current.CarT);
            if (b == null || b.Copy == null) return "pas de copie";
            Renderer r = b.Copy.GetComponent<Renderer>();
            MeshFilter mf = b.Copy.GetComponent<MeshFilter>();
            return "copie " + (b.CopyPlain ? "telle quelle" : "bombee") + ", active " + b.Copy.activeInHierarchy + ", calque " + b.Copy.layer + ", rendu " + (r != null && r.enabled) + ", maillage " + (mf != null && mf.sharedMesh != null ? mf.sharedMesh.name + " " + mf.sharedMesh.vertexCount : "?")
                   + ", centre " + current.CarT.InverseTransformPoint(r.bounds.center).ToString("F2") + " taille " + r.bounds.size.ToString("F2") + ", tete " + current.Head.ToString("F2") + ", camera " + (cam != null ? current.CarT.InverseTransformPoint(cam.position).ToString("F2") : "?");
        }

        // Essais : point vise pour regarder la ceinture rangee (0), la boucle (1) ou le siege (2, comme en baissant les yeux).
        public static bool TestBeltTarget(int what, out Vector3 target)
        {
            target = Vector3.zero;
            if (current == null || current.Index != 0) return false;
            Belt b = BeltOf(current.CarT);
            if (b == null) return false;
            Renderer or = b.Open != null ? b.Open.GetComponentInChildren<Renderer>() : null;
            target = what == 0 && or != null ? or.bounds.center : what == 2 ? current.CarT.TransformPoint(current.Head + new Vector3(0f, -0.75f, 0.12f)) : current.CarT.TransformPoint(b.Buckle);
            return true;
        }

        // Essais : tourne le regard (camera de la tete) vers la ceinture rangee du passager (0) ou vers la boucle (1).
        public static string TestBeltLook(int what)
        {
            if (current == null || cam == null) return "pas assis";
            Belt b = current.Index == 0 ? BeltOf(current.CarT) : null;
            if (b == null) return "pas de ceinture";
            Renderer or = b.Open != null ? b.Open.GetComponentInChildren<Renderer>() : null;
            Vector3 target = what == 0 && or != null ? or.bounds.center : current.CarT.TransformPoint(b.Buckle);
            cam.rotation = Quaternion.LookRotation(target - cam.position);
            return "regard vers " + (what == 0 ? "la ceinture rangee" : "la boucle");
        }

        // Essais : la ceinture tenue (comme un clic maintenu sur la ceinture rangee) ; hold=false : lachee.
        public static string TestPull(bool hold)
        {
            if (current == null || current.Index != 0) return "pas assis a l'avant";
            Belt b = BeltOf(current.CarT);
            testHold = hold;
            if (hold)
            {
                if (beltHold <= 0f) Grab(b);
                return "ceinture tenue" + (heldHandle != null ? " : languette " + Recon.Path(heldHandle) + " en " + heldHandle.position.ToString("F2") : " : pas de languette");
            }
            return "ceinture lachee";   // (BeltInput la lache a l'image suivante : bouclee si on vise la boucle)
        }

        public static string TestBelt()
        {
            if (current == null) return "pas assis";
            Belt b = current.Index == 0 ? BeltOf(current.CarT) : null;
            if (b == null) return "pas de ceinture ici";
            beltOn = !beltOn;
            SendSeat(current.Car, current.Index, headLocal);
            UpdateBelts();
            string where = "";
            if (b.Copy != null)
            {
                Renderer cr = b.Copy.GetComponent<Renderer>(), fr = b.Fastened.GetComponent<Renderer>();
                Transform car = current.CarT;
                where = ", copie centre " + car.InverseTransformPoint(cr.bounds.center).ToString("F2") + " taille " + cr.bounds.size.ToString("F2")
                        + " ; modele centre " + car.InverseTransformPoint(fr.bounds.center).ToString("F2") + " actif " + b.Fastened.activeInHierarchy
                        + " ; tete " + current.Head.ToString("F2") + " ; echelle " + b.Copy.transform.localScale.ToString("F2");
            }
            return "ceinture " + (beltOn ? "attachee" : "detachee") + ", copie " + (b.Copy != null) + ", rangee cachee " + b.OpenHidden + where;
        }

        // Essais : copies de ceinture montrees ici (passager, conducteur) et sangle du conducteur cachee.
        public static string BeltState()
        {
            var sb = new System.Text.StringBuilder();
            foreach (KeyValuePair<int, bool> kv in driverBelt) sb.Append("#").Append(kv.Key).Append(kv.Value ? " boucle" : " detache").Append(", ");
            foreach (KeyValuePair<Transform, Belt> kv in belts)
                if (kv.Key != null && kv.Value != null)
                    sb.Append(kv.Key.name).Append(" : passager ").Append(kv.Value.Copy != null).Append(", conducteur ").Append(kv.Value.DriverCopy != null)
                      .Append(" (sangle cachee ").Append(kv.Value.DriverOpenHidden).Append(", copie a ").Append(kv.Value.DriverCopy != null ? kv.Key.InverseTransformPoint(kv.Value.DriverCopy.GetComponent<Renderer>().bounds.center).ToString("F2") : "-").Append("), ");
            return sb.Length > 0 ? sb.ToString() : "aucune";
        }

        // Essais ([Test] Autotest=detacher) : ceinture du conducteur de 'car' par les automates du jeu. action 1 : boucler
        // (SeatbeltHandle 'Use' -> State 2), 2 : detacher (SeatbeltLock 'Use' -> State 2), 0 : etat seulement -- boucle
        // visible, etat de son automate, et ce que croise le regard du joueur vers elle (ce qui prendrait le clic).
        public static string TestDriverBelt(string car, int action)
        {
            Rigidbody b = VehicleSync.Body(car);
            if (b == null) return "voiture " + car + " introuvable";
            Transform lk = FindUnder(b.transform, "SeatbeltLock", null), hd = FindUnder(b.transform, "SeatbeltHandle", null);
            PlayMakerFSM lu = lk != null ? Game.FsmOn(lk.gameObject, "Use") : null, hu = hd != null ? Game.FsmOn(hd.gameObject, "Use") : null;
            if (action == 1 && hu != null) { if (lk != null) lk.gameObject.SetActive(true); Game.SetState(hu, "State 2"); }   // (comme 'Wait lock' : boucle montree en tirant)
            if (action == 2 && lu != null) Game.SetState(lu, "State 2");
            var sb = new System.Text.StringBuilder("ceinture conducteur " + car + (action == 1 ? " (boucler)" : action == 2 ? " (detacher)" : "") + " : PlayerSeatbeltsOn " + Game.GlobalBool("PlayerSeatbeltsOn"));
            if (lk != null)
            {
                Collider c = lk.GetComponent<Collider>();
                sb.Append(", boucle ").Append(lk.gameObject.activeInHierarchy ? "visible" : "cachee").Append(" (").Append(lu != null ? lu.ActiveStateName : "?").Append(", collisionneur ").Append(c != null && c.enabled).Append(')');
                Transform cm = PlayerSync.LocalCamera;
                if (cm != null && c != null)
                {
                    Vector3 d = c.bounds.center - cm.position;
                    RaycastHit[] hits = Physics.RaycastAll(cm.position, d.normalized, d.magnitude + 0.1f);
                    System.Array.Sort(hits, (x, y) => x.distance.CompareTo(y.distance));
                    sb.Append(", regard ").Append(d.magnitude.ToString("F2")).Append(" m :");
                    foreach (RaycastHit h in hits) sb.Append(' ').Append(Recon.Path(h.collider.transform)).Append(h.collider.isTrigger ? "(decl)" : "").Append('@').Append(h.distance.ToString("F2"));
                }
            }
            else sb.Append(", pas de SeatbeltLock");
            if (hd != null) sb.Append(", poignee ").Append(hd.gameObject.activeInHierarchy ? "visible" : "cachee").Append(" (").Append(hu != null ? hu.ActiveStateName : "?").Append(')');
            foreach (Transform x in new[] { lk, hd })
                if (x != null && !x.gameObject.activeInHierarchy)
                    for (Transform p = x; p != null && p != b.transform; p = p.parent)
                        if (!p.gameObject.activeSelf) { sb.Append(", ").Append(x.name).Append(" eteint par ").Append(VehicleSync.RelPath(b.transform, p)); break; }
            sb.Append(" | ").Append(CarVisuals.State(car, "SeatbeltLock")).Append(" | ").Append(CarVisuals.State(car, "SeatbeltHandle"));
            return sb.ToString();
        }

        // Essais : places passagers de la voiture de cle 'car'.
        public static int CountFor(string car)
        {
            int n = 0;
            foreach (Seat s in seats) if (s.Car == car) n++;
            return n;
        }
    }
}
