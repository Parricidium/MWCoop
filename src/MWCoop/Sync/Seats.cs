using System.Collections.Generic;
using HutongGames.PlayMaker;
using MWCoop.Net;
using UnityEngine;

namespace MWCoop
{
    // Places passagers (le jeu n'a qu'une place, celle du conducteur), faites comme la sienne :
    //  - places : d'apres les yeux du conducteur (DriverHeadPivot + 27 cm vers l'avant, mesure au volant
    //    de la SORBET) : avant droite en symetrique ; banquette a l'arriere pour SORBET et CORRIS ;
    //  - on entre dans l'habitacle jusqu'au siege (pieds sur le plancher, a moins de 38 cm de cote et
    //    50 cm en long de la place) : l'icone passager du jeu s'affiche (GUIpassenger, comme le volant) ;
    //  - ENTREE : le joueur est accroche a la voiture LA OU IL EST (pas de teleportation), tourne vers
    //    l'avant ; l'automate Crouch du joueur passe en « Incar » (variable PlayerInCar), exactement comme
    //    pour le conducteur : il abaisse la camera a la hauteur assise et fige les deplacements ;
    //  - ENTREE de nouveau : decroche sur place (dans l'habitacle), Crouch « Get out » releve la camera.
    // Jamais quand le jeu s'apprete a faire conduire (zone du conducteur en attente d'ENTREE). Les autres
    // voient l'avatar assis, la tete a la place reelle de sa camera (envoyee), qui suit son regard. Les
    // commandes du vehicule restent accessibles et sont rejouees chez tous (Jobs).
    public static class Seats
    {
        class Seat { public string Car; public Transform CarT; public int Index; public Vector3 Head; }
        class Remote { public string Car; public int Index; public Vector3 Head; }
        static readonly List<PlayMakerFSM> driveTriggers = new List<PlayMakerFSM>();
        static bool iconOn;

        static readonly List<Seat> seats = new List<Seat>();
        static readonly Dictionary<int, Remote> remote = new Dictionary<int, Remote>();
        static Seat current;
        static Transform pivot, player, cam;
        static CharacterController controller;
        static PlayMakerFSM crouch;
        static float nextScan = -1, satAt, nextResend;
        static Vector3 headLocal;
        static int debugFrames;
        static float crouchCheckAt = -1;

        public static bool Seated { get { return current != null; } }

        public static void OnLevelLoaded()
        {
            seats.Clear(); current = null; pivot = null; player = cam = null; controller = null; crouch = null; remote.Clear();
            nextScan = PlayerSync.InGame ? Time.realtimeSinceStartup + 14f : -1;
        }

        static void Scan()
        {
            seats.Clear();
            driveTriggers.Clear();
            foreach (Rigidbody rb in Object.FindObjectsOfType<Rigidbody>())
            {
                if (rb.transform.parent != null || rb.GetComponent("CarDynamics") == null) continue;
                string n = rb.name;
                if (n.StartsWith("KEKMET") || n.StartsWith("JONNEZ") || n.StartsWith("FLATBED")) continue;   // une seule place
                foreach (PlayMakerFSM f in rb.GetComponentsInChildren<PlayMakerFSM>(true))
                    if (f.FsmName == "PlayerTrigger" && f.gameObject.name.StartsWith("DriveTrigger")) driveTriggers.Add(f);
                Transform dhp = Find(rb.transform, "DriverHeadPivot");
                if (dhp == null) continue;
                Vector3 d = rb.transform.InverseTransformPoint(dhp.position) + EyeFromPivot;
                seats.Add(new Seat { Car = n, CarT = rb.transform, Index = 0, Head = new Vector3(-d.x, d.y, d.z) });
                if (n.StartsWith("SORBET") || n.StartsWith("CORRIS"))
                {
                    // Banquette : 85 cm derriere, un peu plus haute.
                    seats.Add(new Seat { Car = n, CarT = rb.transform, Index = 1, Head = new Vector3(d.x, d.y + 0.06f, d.z - 0.85f) });
                    seats.Add(new Seat { Car = n, CarT = rb.transform, Index = 2, Head = new Vector3(-d.x, d.y + 0.06f, d.z - 0.85f) });
                }
            }
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
            if (now >= nextScan) { nextScan = now + 30f; Scan(); }
            if (!FindPlayer()) return;
            if (current != null)
            {
                if (pivot == null || current.CarT == null) { Leave(); return; }
                if (debugFrames > 0)
                {
                    debugFrames--;
                    Log.Info("passager : image " + Time.frameCount + " joueur " + current.CarT.InverseTransformPoint(player.position).ToString("F3") + " local " + player.localPosition.ToString("F3")
                             + " parent " + (player.parent != null ? player.parent.name : "-") + " echelle " + player.localScale.ToString("F2") + " cc " + (controller != null && controller.enabled)
                             + " pivot " + current.CarT.InverseTransformPoint(pivot.position).ToString("F3"));
                }
                if (Input.GetKeyDown(KeyCode.Return) && now - satAt > 0.6f) { Leave(); return; }
                // Tete : la ou la camera s'est posee (le jeu l'abaisse en 0,4 s), puis renvoyee de temps en temps.
                if (now >= nextResend && now - satAt > 0.7f)
                {
                    nextResend = now + 5f;
                    headLocal = current.CarT.InverseTransformPoint(cam.position);
                    SendSeat(current.Car, current.Index, headLocal);
                }
                return;
            }
            // Sorti dans l'habitacle : une fois la camera relevee par le jeu, accroupi si le toit est au-dessus.
            if (crouchCheckAt > 0 && now >= crouchCheckAt)
            {
                crouchCheckAt = -1;
                if (crouch != null && crouch.ActiveStateName == "Wait key" && UnderRoof()) Game.SetState(crouch, "Move down 1");
            }
            Seat best = null;
            if (VehicleSync.LocalDriving < 0 && !Game.GlobalBool("PlayerSeated") && !InDriverZone()) best = InZone();
            Icon(best != null);
            if (best != null && Input.GetKeyDown(KeyCode.Return)) Sit(best);
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
                if ((s.CarT.position - player.position).sqrMagnitude > 25f) continue;
                Vector3 p = s.CarT.InverseTransformPoint(player.position);
                if (p.y < -0.3f || p.y > s.Head.y) continue;
                float dx = Mathf.Abs(p.x - s.Head.x), dz = Mathf.Abs(p.z - (s.Head.z - 0.1f));
                if (dx > 0.38f || dz > 0.5f) continue;
                float d = dx * dx + dz * dz;
                if (d < bestD) { bestD = d; best = s; }
            }
            return best;
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
                if (h.collider != null && !h.collider.isTrigger && h.collider.transform.root != player.root && h.collider.transform.root.GetComponent("CarDynamics") != null) return true;
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
            Session.SendAll(new NetWriter(Msg.Seat).U8(Session.LocalId).Str(car).U8(index + 1).Vec(head), true);
        }

        public static void OnMessage(Peer from, NetReader r)
        {
            int who = r.U8();
            if (Session.IsHost) who = from.Id;
            string car = r.Str();
            int index = r.U8() - 1;
            Vector3 head = r.Vec();
            if (Session.IsHost) Session.Broadcast(new NetWriter(Msg.Seat).U8(who).Str(car).U8(index + 1).Vec(head), true, who);
            Remote old;
            bool had = remote.TryGetValue(who, out old);
            if (index < 0) { remote.Remove(who); if (had) Log.Info("passager : #" + who + " est sorti de " + old.Car); return; }
            remote[who] = new Remote { Car = car, Index = index, Head = head };
            if (!had || old.Car != car || old.Index != index) Log.Info("passager : #" + who + " assis dans " + car + " (place " + index + ")");
        }

        public static void PlayerLeft(int id) { remote.Remove(id); }

        // Avatar d'un autre joueur assis en passager : voiture locale et place de sa tete (repere voiture).
        public static bool RemoteSeat(int id, out Transform car, out Vector3 head, out string carName)
        {
            car = null; head = Vector3.zero; carName = null;
            Remote rs;
            if (!remote.TryGetValue(id, out rs)) return false;
            foreach (Seat s in seats)
                if (s.Car == rs.Car && s.CarT != null)
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
            foreach (Rigidbody rb in Object.FindObjectsOfType<Rigidbody>())
            {
                if (rb.transform.parent != null || !rb.name.StartsWith(car)) continue;
                Transform dhp = Find(rb.transform, "DriverHeadPivot");
                return car + " : camera " + rb.transform.InverseTransformPoint(cam.position).ToString("F3")
                       + ", DriverHeadPivot " + (dhp != null ? rb.transform.InverseTransformPoint(dhp.position).ToString("F3") : "?");
            }
            return "pas de " + car;
        }

        public static string TestLeave() { if (current == null) return "pas assis"; Leave(); return "sorti"; }
    }
}
