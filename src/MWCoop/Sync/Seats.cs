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
    //    taxi (MACHTWAGEN, sous JOBS/TAXIJOB : voitures de VehicleSync, par leur cle) ; GIFU : le passager seul ;
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
        class Seat { public string Car; public Transform CarT; public int Index; public Vector3 Head; }
        class Remote { public string Car; public int Index; public Vector3 Head; public bool Belt; }
        // Ceinture du passager avant d'une voiture : modeles du jeu (ceinture bouclee du conducteur, ceinture du
        // passager rangee), boucle (repere voiture), copie en miroir montree quand le passager est attache.
        class Belt { public GameObject Fastened, Open, Copy; public Vector3 Buckle; public bool OpenHidden; public GameObject DriverOpen, DriverCopy; public bool DriverOpenHidden; }
        static readonly Dictionary<int, bool> driverBelt = new Dictionary<int, bool>();   // conducteurs distants : ceinture bouclee
        static bool driverBeltSent;
        static float driverBeltResend;
        const int DriverBeltIndex = 0xFF;
        static readonly Dictionary<Transform, Belt> belts = new Dictionary<Transform, Belt>();
        static bool beltOn, beltHint;
        const float BeltForward = 0.17f;
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

        public static void OnLevelLoaded()
        {
            zonesOff.Clear(); driverZones.Clear();
            seats.Clear(); current = null; pivot = null; player = cam = null; controller = null; crouch = null; remote.Clear(); gen = -1;
            belts.Clear(); beltOn = beltHint = false; driverBelt.Clear(); driverBeltSent = false;
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
                if (n.StartsWith("KEKMET") || n.StartsWith("JONNEZ") || n.StartsWith("FLATBED")) continue;   // une seule place
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
            }
            if (was != null)
                foreach (Seat x in seats) if (x.Car == was.Car && x.Index == was.Index) { current = x; break; }
            Log.Info("places passagers : " + seats.Count);
        }

        // Yeux du conducteur par rapport a DriverHeadPivot (repere voiture), mesures au volant de la SORBET.
        static readonly Vector3 EyeFromPivot = new Vector3(0f, -0.03f, 0.27f);

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
            float bestD = 1f;
            foreach (Seat s in seats)
            {
                if (s.CarT == null || SeatTaken(s)) continue;
                if ((s.CarT.position - player.position).sqrMagnitude > 25f || !s.CarT.gameObject.activeInHierarchy) continue;   // (taxi range : inactif)
                Vector3 p = s.CarT.InverseTransformPoint(player.position);
                if (p.y < -0.3f || p.y > s.Head.y) continue;
                float dx = Mathf.Abs(p.x - s.Head.x), dz = Mathf.Abs(p.z - (s.Head.z - 0.1f));
                if (dx > 0.38f || dz > 0.5f) continue;
                float d = dx * dx + dz * dz;
                if (d < bestD) { bestD = d; best = s; }
            }
            return best;
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

        static void Leave()
        {
            current = null;
            beltOn = false;
            BeltHint(false);
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
                Log.Info("ceinture passager : " + car.name + " (bouclee " + f.name + ", rangee " + (o != null ? o.name : "-") + ")");
            }
            belts[car] = b;
            return b;
        }

        // Assis a l'avant : viser la boucle et cliquer l'attache ou la detache (texte du jeu, comme le conducteur).
        static void BeltInput()
        {
            Belt b = current.Index == 0 ? BeltOf(current.CarT) : null;
            bool aim = false;
            if (b != null && cam != null)
            {
                Vector3 bp = current.CarT.TransformPoint(b.Buckle);
                Vector3 d = bp - cam.position;
                aim = d.magnitude < 1.3f && Vector3.Angle(cam.forward, d) < 16f;
            }
            BeltHint(aim);
            if (aim && Input.GetMouseButtonDown(0))
            {
                beltOn = !beltOn;
                Log.Info("ceinture passager : " + (beltOn ? "attachee" : "detachee") + " (" + current.Car + ")");
                SendSeat(current.Car, current.Index, headLocal);
            }
        }
        static void BeltHint(bool on)
        {
            if (on == beltHint) return;
            beltHint = on;
            Game.SetGlobalBool("GUIuse", on);
            Game.SetGlobal("GUIinteraction", on ? (beltOn ? Lang.T("DÉTACHER LA CEINTURE", "UNBUCKLE") : Lang.T("ATTACHER LA CEINTURE", "BUCKLE UP")) : "");
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
                if (on && b.Copy == null && b.Fastened != null) b.Copy = BeltCopy(b, kv.Key, true);
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

        // Copie de la ceinture bouclee du conducteur (Fastened_mesh) : en miroir par le plan median de la voiture (x -> -x)
        // pour le passager, telle quelle pour le conducteur ; un peu en avant (les avatars, des PNJ, ont le torse plus en
        // avant que le corps du joueur ; [Test] CeintureAvance).
        static GameObject BeltCopy(Belt b, Transform car, bool mirror)
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
            c.AddComponent<MeshFilter>().sharedMesh = src.GetComponent<MeshFilter>().sharedMesh;
            MeshRenderer mr = src.GetComponent<MeshRenderer>();
            MeshRenderer cr = c.AddComponent<MeshRenderer>();
            if (mr != null) cr.sharedMaterials = mr.sharedMaterials;
            return c;
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

        static Vector3 SeatHead(string car, int index)
        {
            foreach (Seat s in seats) if (s.Car == car && s.Index == index) return s.Head;
            return Vector3.zero;
        }

        // Essais : assoit le joueur local a la place 'index' de 'car' (debout dans l'habitacle, puis ENTREE).
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

        // Essais : places passagers de la voiture de cle 'car'.
        public static int CountFor(string car)
        {
            int n = 0;
            foreach (Seat s in seats) if (s.Car == car) n++;
            return n;
        }
    }
}
