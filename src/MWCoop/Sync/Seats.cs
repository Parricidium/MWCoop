using System.Collections.Generic;
using HutongGames.PlayMaker;
using MWCoop.Net;
using UnityEngine;

namespace MWCoop
{
    // Places passagers (le jeu n'a qu'une place, celle du conducteur). Pour chaque voiture, d'apres les
    // yeux du conducteur (DriverHeadPivot + 27 cm vers l'avant : la vraie camera au volant, mesuree sur la
    // SORBET) : la place avant droite en symetrique, et pour les voitures a banquette (SORBET, CORRIS)
    // deux places a l'arriere. Quand le regard TOUCHE le siege (rayon depuis la camera : premier objet
    // solide = assise ou dossier d'une place libre, a moins de 2,2 m), l'icone passager du jeu s'affiche
    // (GUIpassenger, comme le volant pour le conducteur) et ENTREE assoit le joueur -- jamais quand le jeu s'apprete deja a le
    // faire conduire (zone du conducteur en attente d'ENTREE) : il est accroche a la voiture (il suit sa copie quand un autre conduit),
    // ne marche plus, garde la vue libre ; ENTREE le fait ressortir cote portiere. Les autres voient son
    // avatar assis a cette place, la tete qui suit son regard. Les commandes du vehicule (cle, frein a
    // main, vitres...) restent accessibles et sont rejouees chez tous (Jobs).
    public static class Seats
    {
        class Seat { public string Car; public Transform CarT; public int Index; public Vector3 Head; }
        static readonly List<PlayMakerFSM> driveTriggers = new List<PlayMakerFSM>();
        static bool iconOn;

        static readonly List<Seat> seats = new List<Seat>();
        static readonly Dictionary<int, KeyValuePair<string, int>> remote = new Dictionary<int, KeyValuePair<string, int>>();
        static Seat current;
        static Transform pivot, player, cam;
        static CharacterController controller;
        static float nextScan = -1, nextHint, satAt, nextResend;

        public static bool Seated { get { return current != null; } }

        public static void OnLevelLoaded()
        {
            seats.Clear(); current = null; pivot = null; player = cam = null; controller = null; remote.Clear();
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
                if (Input.GetKeyDown(KeyCode.Return) && now - satAt > 0.6f) { Leave(); return; }
                if (now >= nextResend) { nextResend = now + 5f; SendSeat(current.Car, current.Index); }
                return;
            }
            Seat best = null;
            if (VehicleSync.LocalDriving < 0 && !Game.GlobalBool("PlayerSeated") && !InDriverZone()) best = Aimed();
            Icon(best != null);
            if (best != null && Input.GetKeyDown(KeyCode.Return)) Sit(best);
        }

        // La place dont le regard touche le siege : premier objet solide sur le rayon de la camera (2,2 m),
        // appartenant a une voiture, a hauteur d'assise ou de dossier (entre le plancher et les yeux), a
        // moins de 30 cm de cote et 35 cm en tout du centre de la place. Le toit, la portiere fermee, la
        // carrosserie arretent le rayon : pas d'icone en regardant la voiture de dehors.
        static Seat Aimed() { return AimedFrom(cam.position, cam.forward); }
        static string aimInfo = "";

        static Seat AimedFrom(Vector3 origin, Vector3 dir)
        {
            RaycastHit[] hits = Physics.RaycastAll(origin, dir, 2.2f);
            RaycastHit hit = default(RaycastHit);
            bool any = false;
            foreach (RaycastHit h in hits)
            {
                if (h.collider == null || h.collider.isTrigger || h.collider.transform.root == player.root) continue;
                if (h.collider.name == "CarCollider") continue;   // enveloppe de la voiture contre le decor (couvre vitres et portes ouvertes)
                if (!any || h.distance < hit.distance) { hit = h; any = true; }
            }
            aimInfo = any ? hit.collider.name + " a " + hit.distance.ToString("F2") + " m" : "rien touche";
            if (!any) return null;
            Transform car = hit.collider.transform.root;
            if (car.GetComponent("CarDynamics") == null) return null;
            Vector3 p = car.InverseTransformPoint(hit.point);
            aimInfo += " " + p.ToString("F2");
            Seat best = null;
            float bestD = 0.35f;
            foreach (Seat s in seats)
            {
                if (s.CarT != car) continue;
                if (p.y > s.Head.y + 0.05f || p.y < s.Head.y - 0.95f) continue;   // ni le toit, ni le plancher
                float dx = p.x - s.Head.x, dz = p.z - (s.Head.z - 0.2f);            // centre : un peu derriere les yeux
                float dd = Mathf.Sqrt(dx * dx + dz * dz);
                if (Mathf.Abs(dx) < 0.3f && dd < bestD) { bestD = dd; best = s; }
            }
            return best != null && !SeatTaken(best) ? best : null;
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
            foreach (KeyValuePair<string, int> kv in remote.Values) if (kv.Key == s.Car && kv.Value == s.Index) return true;
            return false;
        }

        static void Sit(Seat s)
        {
            Icon(false);
            pivot = new GameObject("MWCoop-SiegePassager").transform;
            pivot.parent = s.CarT;
            pivot.localPosition = s.Head;
            pivot.localRotation = Quaternion.identity;
            if (controller != null) controller.enabled = false;
            Game.SetGlobalBool("PlayerStop", true);
            Game.SetGlobalBool("PlayerSeated", true);
            player.parent = pivot;
            player.localRotation = Quaternion.identity;
            player.position += pivot.position - cam.position;   // les yeux sur la place
            current = s;
            satAt = Time.realtimeSinceStartup;
            Log.Info("passager : assis dans " + s.Car + " (place " + s.Index + ")");
            SendSeat(s.Car, s.Index);
        }

        static void Leave()
        {
            Seat s = current;
            current = null;
            if (player != null)
            {
                player.parent = null;
                if (s != null && s.CarT != null)
                {
                    // Dehors, cote de la place (droite pour l'avant droit, gauche ou droite a l'arriere).
                    float side = Mathf.Sign(s.Head.x == 0f ? 1f : s.Head.x);
                    Vector3 outPos = s.CarT.TransformPoint(s.Head + new Vector3(side * 1.4f, 0f, 0f));
                    player.position = outPos - (cam.position - player.position) + Vector3.up * 0.1f;
                }
                player.rotation = Quaternion.Euler(0f, player.eulerAngles.y, 0f);
            }
            if (controller != null) controller.enabled = true;
            Game.SetGlobalBool("PlayerStop", false);
            Game.SetGlobalBool("PlayerSeated", false);
            if (pivot != null) Object.Destroy(pivot.gameObject);
            pivot = null;
            Log.Info("passager : sorti");
            SendSeat("", -1);
        }

        static void SendSeat(string car, int index)
        {
            if (!Session.Active) return;
            Session.SendAll(new NetWriter(Msg.Seat).U8(Session.LocalId).Str(car).U8(index + 1), true);
        }

        public static void OnMessage(Peer from, NetReader r)
        {
            int who = r.U8();
            if (Session.IsHost) who = from.Id;
            string car = r.Str();
            int index = r.U8() - 1;
            if (Session.IsHost) Session.Broadcast(new NetWriter(Msg.Seat).U8(who).Str(car).U8(index + 1), true, who);
            KeyValuePair<string, int> old;
            bool had = remote.TryGetValue(who, out old);
            if (index < 0) { remote.Remove(who); if (had) Log.Info("passager : #" + who + " est sorti de " + old.Key); return; }
            remote[who] = new KeyValuePair<string, int>(car, index);
            if (!had || old.Key != car || old.Value != index) Log.Info("passager : #" + who + " assis dans " + car + " (place " + index + ")");
        }

        public static void PlayerLeft(int id) { remote.Remove(id); }

        // Avatar d'un autre joueur assis en passager : voiture locale et place de sa tete (repere voiture).
        public static bool RemoteSeat(int id, out Transform car, out Vector3 head, out string carName)
        {
            car = null; head = Vector3.zero; carName = null;
            KeyValuePair<string, int> kv;
            if (!remote.TryGetValue(id, out kv)) return false;
            foreach (Seat s in seats)
                if (s.Car == kv.Key && s.Index == kv.Value && s.CarT != null) { car = s.CarT; head = s.Head; carName = s.Car; return true; }
            return false;
        }

        // Essais : assoit le joueur local a la place 'index' de 'car' (sans touche).
        public static string TestSit(string car, int index)
        {
            if (!FindPlayer()) return "pas de joueur";
            if (seats.Count == 0) Scan();
            foreach (Seat s in seats)
                if (s.Car.StartsWith(car) && s.Index == index) { Sit(s); return "assis dans " + s.Car + " place " + index; }
            return "aucune place " + index + " sur " + car + " (" + seats.Count + ")";
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

        // Essais : regard depuis dehors, a cote de la place 'index' de 'car' (hauteur debout) : vers le
        // siege, vers le toit, vers la portiere.
        public static string TestAim(string car, int index)
        {
            if (!FindPlayer()) return "pas de joueur";
            if (seats.Count == 0) Scan();
            foreach (Seat st in seats)
            {
                if (!st.Car.StartsWith(car) || st.Index != index || st.CarT == null) continue;
                float side = Mathf.Sign(st.Head.x == 0f ? 1f : st.Head.x);
                Vector3 o = st.CarT.TransformPoint(st.Head + new Vector3(side * 0.9f, 0.35f, 0f));
                System.Func<Vector3, string> look = local =>
                {
                    Seat r = AimedFrom(o, (st.CarT.TransformPoint(local) - o).normalized);
                    return (r == null ? "rien" : "place " + r.Index) + " [" + aimInfo + "]";
                };
                return car + " place " + index + " : siege -> " + look(st.Head + new Vector3(0f, -0.45f, -0.2f))
                       + ", toit -> " + look(st.Head + new Vector3(0f, 0.45f, 0f))
                       + ", portiere -> " + look(st.Head + new Vector3(side * 0.7f, -0.6f, 0f));
            }
            return "aucune place";
        }

        public static string TestLeave() { if (current == null) return "pas assis"; Leave(); return "sorti"; }
    }
}
