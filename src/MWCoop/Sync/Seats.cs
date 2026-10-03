using System.Collections.Generic;
using MWCoop.Net;
using UnityEngine;

namespace MWCoop
{
    // Places passagers (le jeu n'a qu'une place, celle du conducteur). Pour chaque voiture, d'apres la
    // tete du conducteur (DriverHeadPivot) : la place avant droite en symetrique, et pour les voitures
    // a banquette (SORBET, CORRIS) deux places a l'arriere. Pres d'une place (tete a moins de 1,1 m),
    // ENTREE assoit le joueur : il est accroche a la voiture (il suit sa copie quand un autre conduit),
    // ne marche plus, garde la vue libre ; ENTREE le fait ressortir cote portiere. Les autres voient son
    // avatar assis a cette place, la tete qui suit son regard. Les commandes du vehicule (cle, frein a
    // main, vitres...) restent accessibles et sont rejouees chez tous (Jobs).
    public static class Seats
    {
        class Seat { public string Car; public Transform CarT; public int Index; public Vector3 Head; }

        static readonly List<Seat> seats = new List<Seat>();
        static readonly Dictionary<int, KeyValuePair<string, int>> remote = new Dictionary<int, KeyValuePair<string, int>>();
        static Seat current;
        static Transform pivot, player, cam;
        static CharacterController controller;
        static float nextScan = -1, nextHint, satAt, nextResend;

        public static bool Seated { get { return current != null; } }

        public static void OnLevelLoaded()
        {
            seats.Clear(); current = null; pivot = null; player = cam = null; controller = null;
            nextScan = PlayerSync.InGame ? Time.realtimeSinceStartup + 14f : -1;
        }

        static void Scan()
        {
            seats.Clear();
            foreach (Rigidbody rb in Object.FindObjectsOfType<Rigidbody>())
            {
                if (rb.transform.parent != null || rb.GetComponent("CarDynamics") == null) continue;
                string n = rb.name;
                if (n.StartsWith("KEKMET") || n.StartsWith("JONNEZ") || n.StartsWith("FLATBED")) continue;   // une seule place
                Transform dhp = Find(rb.transform, "DriverHeadPivot");
                if (dhp == null) continue;
                Vector3 d = rb.transform.InverseTransformPoint(dhp.position);
                seats.Add(new Seat { Car = n, CarT = rb.transform, Index = 0, Head = new Vector3(-d.x, d.y, d.z) });
                if (n.StartsWith("SORBET") || n.StartsWith("CORRIS"))
                {
                    seats.Add(new Seat { Car = n, CarT = rb.transform, Index = 1, Head = new Vector3(d.x, d.y, d.z - 0.85f) });
                    seats.Add(new Seat { Car = n, CarT = rb.transform, Index = 2, Head = new Vector3(-d.x, d.y, d.z - 0.85f) });
                }
            }
            Log.Info("places passagers : " + seats.Count);
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
            if (VehicleSync.LocalDriving >= 0 || Game.GlobalBool("PlayerSeated")) return;
            Seat best = null;
            float bd = 1.1f * 1.1f;
            foreach (Seat s in seats)
            {
                if (s.CarT == null || SeatTaken(s)) continue;
                float dist = (s.CarT.TransformPoint(s.Head) - cam.position).sqrMagnitude;
                if (dist < bd) { bd = dist; best = s; }
            }
            if (best == null) return;
            if (now >= nextHint) { nextHint = now + 8f; Hud.Toast("ENTREE : s'asseoir en passager"); }
            if (Input.GetKeyDown(KeyCode.Return)) Sit(best);
        }

        static bool SeatTaken(Seat s)
        {
            foreach (KeyValuePair<string, int> kv in remote.Values) if (kv.Key == s.Car && kv.Value == s.Index) return true;
            return false;
        }

        static void Sit(Seat s)
        {
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

        public static string TestLeave() { if (current == null) return "pas assis"; Leave(); return "sorti"; }
    }
}
