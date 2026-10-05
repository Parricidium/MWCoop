using System.Collections.Generic;
using HutongGames.PlayMaker;
using MWCoop.Net;
using UnityEngine;

namespace MWCoop
{
    // Gestes et etats du joueur que son avatar montre aux autres (en plus de PlayerSync : fumer, boire, saluer,
    // dormir, accroupi, assis). Tout est lu sur le joueur local, jamais sur les touches :
    //  - PLAYER/.../FPSCamera :: PlayerFunctions (touches du jeu) et les mains a la premiere personne qu'il allume
    //    sous FPSCamera/FPSCamera : Fist (coup, H : animation de Fist/Pivot), MiddleFinger (doigt, M : animation de
    //    MiddleFinger/Pivot), pouce leve (O : etats "Finger 2" -> "Thumb Up", jusqu'a "Finger 3"), Watch (montre, U :
    //    "Anim on" -> "Watch ON" -> "Anim off" -> "Close"), Hand Push (pousser une voiture) ;
    //  - FPSCamera/Piss :: Logic (P) : le jet Piss/Fluid est actif tant que le joueur urine ;
    //  - ivresse : globale PlayerDrunk (l'evanouissement d'ivresse est a 3,8 : Systems/PassOut.Limit) ;
    //  - se pencher : rotation et decalage de Pivot/AnimPivot/Camera (au-dessus du regard) par rapport a PLAYER ;
    //  - evanoui / assomme : Systems/PassOut :: Activate et Systems/KnockOut :: Activate, de "Pass out" au reveil
    //    (le joueur est deplace a son point de reveil pendant "Move" : l'avatar y reste allonge) ;
    //  - objet tenu (Props.Holding) : place de l'objet dans le repere de la camera et sa taille, pour que les bras
    //    de l'avatar aillent le chercher (l'objet lui-meme est deplace par Props, a sa vraie place) ;
    //  - assis hors vehicule (chaises, canapes, bancs, sauna : CrouchTrigger* :: PlayerTrigger en "Press return",
    //    globale PlayerSeated, Crouch en "Seated") : place et sens du siege, pour poser l'avatar assis dessus.
    // Coup et doigt partent en evenements (fiables) ; le reste en etat (fiable, a chaque changement et toutes les 4 s).
    // Flaques de la maison (YARD/PissAreas :: Logic, taches PISS3..7 qui grandissent sous le jet) : l'automate est
    // pris ici (WorldFsms le rejouerait sans jamais recevoir STOP : la tache grandirait sans fin chez l'autre) ;
    // celui qui urine envoie l'echelle des taches qui changent, les autres la posent.
    public static class Gestures
    {
        public const int G_Thumb = 1, G_Watch = 2, G_Push = 4, G_Piss = 8, G_Down = 16, G_Held = 32, G_Seat = 64,
            G_Finger = 128, G_Fist = 256;
        public const int E_Punch = 1, E_Finger = 2;
        const string Module = "gestes";

        // Etat recu d'un autre joueur (lu par son Avatar).
        public class Remote
        {
            public int Bits;
            public float Drunk, LeanSide, LeanFwd;   // ivresse (PlayerDrunk), penche (degres : droite +, avant +)
            public Vector3 Held; public float HeldSize;   // objet tenu : repere camera (m), plus grande largeur (m)
            public Vector3 Seat; public float SeatYaw;   // assis : sol sous le siege (x, sol, z), sens du siege
            public float PunchAt = -100f, FingerAt = -100f, Time;
        }

        static readonly Dictionary<int, Remote> remote = new Dictionary<int, Remote>();
        public static Remote Of(int id) { Remote r; return remote.TryGetValue(id, out r) ? r : null; }
        public static void PlayerLeft(int id) { remote.Remove(id); }

        // ---------------------------------------------------------------- joueur local
        static Transform player, camBase, cam;
        static GameObject middleFinger, fist, watch, push, fluid;
        static Animation fingerAnim, fistAnim;
        static PlayMakerFSM functions, crouch, hand, passOut, knockOut, pissFsm;
        static float nextFind, nextSend, lastSend, loadedAt;
        static bool fingerWas, fistWas, found;
        static int bits;
        static float drunk, leanSide, leanFwd, heldSize, seatYaw;
        static Vector3 heldOff, seatPos;
        static PlayMakerFSM seatTrigger;
        static float seatCheckAt;
        static FsmFloat drunkVar;
        static int sBits = -1, sDrunk, sSide, sFwd;
        static Vector3 sHeld, sSeat;
        static float sSeatYaw;
        // Essais : objet tenu force (sans objet en main).
        static bool testHeld;

        public static int LocalBits { get { return bits; } }

        static readonly HashSet<string> PassOutDown = new HashSet<string> { "Pass out", "Move", "State 2", "Sleep", "Day change", "Sleep time", "Calc rates 2", "Jail?", "Where?", "Cottage", "Random pos", "Set position", "Pena", "Active?", "Jail" };
        static readonly HashSet<string> KnockOutDown = new HashSet<string> { "Pass out", "Move", "State 2", "Calc rates", "Check time of day", "Advance day", "Check day", "Monday", "Set position" };

        public static void OnLevelLoaded()
        {
            player = camBase = cam = null;
            middleFinger = fist = watch = push = fluid = null;
            fingerAnim = fistAnim = null;
            functions = crouch = hand = passOut = knockOut = pissFsm = null;
            found = false; nextFind = 0; sBits = -1; bits = 0; testHeld = false;
            seatTrigger = null; triggers.Clear(); nextTriggerScan = 0;
            stainsFsm = null; stains.Clear(); stainsClaimed = false;
            remote.Clear();
            loadedAt = Time.realtimeSinceStartup;
            step = -1; testSeatAt = -1; testTrigger = null; forced = leftSeat = false; seatCheckAt = 0; drunkVar = null;
        }

        static bool FindLocal()
        {
            if (found && player != null) return true;
            if (Time.realtimeSinceStartup < nextFind) return false;
            nextFind = Time.realtimeSinceStartup + 2f;
            GameObject go = GameObject.Find("PLAYER");
            if (go == null) return false;
            player = go.transform;
            camBase = player.Find("Pivot/AnimPivot/Camera");
            Transform outer = player.Find("Pivot/AnimPivot/Camera/FPSCamera");
            Transform inner = outer != null ? outer.Find("FPSCamera") : null;
            cam = inner ?? outer ?? player;
            crouch = Game.FsmOn(go, "Crouch");
            if (outer != null)
            {
                functions = Game.FsmOn(outer.gameObject, "PlayerFunctions");
                Transform h = outer.Find("1Hand_Assemble/Hand");
                if (h != null) hand = Game.FsmOn(h.gameObject, "PickUp");
                Transform p = outer.Find("Piss");
                if (p != null) pissFsm = Game.FsmOn(p.gameObject, "Logic");
                Transform fl = outer.Find("Piss/Fluid");
                if (fl != null) fluid = fl.gameObject;
            }
            if (inner != null)
            {
                middleFinger = Child(inner, "MiddleFinger");
                fist = Child(inner, "Fist");
                watch = Child(inner, "Watch");
                push = Child(inner, "Hand Push");
                if (middleFinger != null) { Transform pv = middleFinger.transform.Find("Pivot"); if (pv != null) fingerAnim = pv.GetComponent<Animation>(); }
                if (fist != null) { Transform pv = fist.transform.Find("Pivot"); if (pv != null) fistAnim = pv.GetComponent<Animation>(); }
            }
            GameObject po = Game.FindAny("Systems/PassOut"), ko = Game.FindAny("Systems/KnockOut");
            if (po != null) passOut = Game.FsmOn(po, "Activate");
            if (ko != null) knockOut = Game.FsmOn(ko, "Activate");
            found = true;
            Log.Info("gestes : joueur releve (fonctions " + (functions != null) + ", doigt " + (middleFinger != null) + ", poing " + (fist != null)
                     + ", montre " + (watch != null) + ", jet " + (fluid != null) + ", evanouissement " + (passOut != null) + "/" + (knockOut != null) + ")");
            return true;
        }

        static GameObject Child(Transform t, string name) { Transform c = t.Find(name); return c != null ? c.gameObject : null; }

        static bool Active(GameObject g) { return g != null && g.activeInHierarchy; }

        static string FnState { get { return functions != null ? functions.ActiveStateName : ""; } }

        public static void Update()
        {
            if (!Session.Active || !PlayerSync.InGame) return;
            float now = Time.realtimeSinceStartup;
            if (!stainsClaimed && now - loadedAt > 4f) ClaimStains();
            if (!FindLocal()) return;
            Read(now);
            // Coup et doigt : evenements a l'instant ou leur animation demarre.
            bool fistNow = Active(fist) && (fistAnim == null || fistAnim.isPlaying) || FnState == "Fist";
            bool fingerNow = Active(middleFinger) && (fingerAnim == null || fingerAnim.isPlaying) || FnState == "Finger" || FnState == "Fuck singer";
            if (fistNow && !fistWas) SendEvent(E_Punch);
            if (fingerNow && !fingerWas) SendEvent(E_Finger);
            fistWas = fistNow; fingerWas = fingerNow;
            if (fistNow) bits |= G_Fist;
            if (fingerNow) bits |= G_Finger;
            SendState(now);
            Stains(now);
        }

        // Etats du joueur local.
        static void Read(float now)
        {
            int b = 0;
            string fs = FnState;
            if (fs == "Finger 2" || fs == "Thumb Up") b |= G_Thumb;
            if (Active(watch) || fs == "Anim on" || fs == "Watch ON" || fs == "Watch ON 2") b |= G_Watch;
            if (Active(push)) b |= G_Push;
            if (Active(fluid)) b |= G_Piss;
            if (passOut != null && PassOutDown.Contains(passOut.ActiveStateName) || knockOut != null && KnockOutDown.Contains(knockOut.ActiveStateName)) b |= G_Down;
            if (drunkVar == null) drunkVar = FsmVariables.GlobalVariables.FindFsmFloat("PlayerDrunk");
            drunk = drunkVar != null ? drunkVar.Value : 0f;
            bool inCar = VehicleSync.LocalDriving >= 0 || Seats.Seated;
            // Penche : rotation de Camera (au-dessus du regard, sous Pivot/AnimPivot) par rapport au joueur ; ou, si
            // le jeu deplace la camera sans la tourner, son decalage vu a 0,9 m (le dandinement de la marche reste sous 4 deg).
            leanSide = leanFwd = 0f;
            if (camBase != null && !inCar && (b & G_Down) == 0)
            {
                Vector3 e = (Quaternion.Inverse(player.rotation) * camBase.rotation).eulerAngles;
                Vector3 p = player.InverseTransformPoint(camBase.position);
                float roll = -Mathf.DeltaAngle(0f, e.z), pitch = Mathf.DeltaAngle(0f, e.x);
                leanSide = Mathf.Abs(roll) > 1.5f ? roll : Mathf.Atan2(p.x, 0.9f) * Mathf.Rad2Deg;
                leanFwd = Mathf.Abs(pitch) > 1.5f ? pitch : Mathf.Atan2(p.z, 0.9f) * Mathf.Rad2Deg;
                if (Mathf.Abs(leanSide) < 4f) leanSide = 0f;
                if (Mathf.Abs(leanFwd) < 4f) leanFwd = 0f;
            }
            // Objet tenu : centre de ses rendus dans le repere de la camera, plus grande largeur.
            GameObject held = Props.Holding && hand != null ? hand.FsmVariables.GetFsmGameObject("PickedObject").Value : null;
            if (held != null)
            {
                Bounds bb = new Bounds(held.transform.position, Vector3.zero);
                bool any = false;
                foreach (Renderer r in held.GetComponentsInChildren<Renderer>())
                {
                    if (!r.enabled) continue;
                    if (!any) { bb = r.bounds; any = true; } else bb.Encapsulate(r.bounds);
                }
                heldOff = cam.InverseTransformPoint(bb.center);
                heldSize = Mathf.Max(bb.size.x, bb.size.z);
                b |= G_Held;
            }
            else if (testHeld) { heldOff = new Vector3(0f, -0.3f, 0.6f); heldSize = 0.5f; b |= G_Held; }
            // Assis hors vehicule.
            bool seated = !inCar && (Game.GlobalBool("PlayerSeated") || crouch != null && crouch.ActiveStateName == "Seated");
            if (seated && (b & G_Down) == 0)
            {
                // Declencheur ou se tient le joueur (revu chaque seconde s'il n'y est plus : passe d'une chaise a l'autre).
                if ((seatTrigger == null || seatTrigger.ActiveStateName != "Press return") && now >= seatCheckAt)
                {
                    seatCheckAt = now + 1f;
                    PlayMakerFSM t = FindSeat();
                    if (t != seatTrigger) { seatTrigger = t; SeatPose(); }
                }
                if (seatTrigger != null) b |= G_Seat;
            }
            else seatTrigger = null;
            bits = b;
        }

        // ---------------------------------------------------------------- sieges
        static readonly List<PlayMakerFSM> triggers = new List<PlayMakerFSM>();
        static float nextTriggerScan;

        static PlayMakerFSM FindSeat()
        {
            if (Time.realtimeSinceStartup >= nextTriggerScan)
            {
                nextTriggerScan = Time.realtimeSinceStartup + 30f;
                triggers.Clear();
                foreach (Object o in Resources.FindObjectsOfTypeAll(typeof(PlayMakerFSM)))
                {
                    var f = (PlayMakerFSM)o;
                    if (f.hideFlags == HideFlags.None && f.FsmName == "PlayerTrigger" && f.gameObject.name.StartsWith("CrouchTrigger")) triggers.Add(f);
                }
            }
            PlayMakerFSM best = null;
            float bestD = 9f;
            bool bestIn = false;
            foreach (PlayMakerFSM f in triggers)
            {
                if (f == null || !f.gameObject.activeInHierarchy) continue;
                bool inside = f.ActiveStateName == "Press return";
                float d = (f.transform.position - player.position).sqrMagnitude;
                if (inside && !bestIn || inside == bestIn && d < bestD) { best = f; bestD = d; bestIn = inside; }
            }
            return best;
        }

        // Place et sens du siege : le meuble (parent du declencheur) ; son dossier est la ou un rayon tire depuis le
        // milieu du siege, a 75 cm du sol, touche le meuble le plus pres (sinon : le cote ou se tient le joueur) ;
        // le long d'un canape, le joueur garde sa place ; l'avatar a les pieds au sol du meuble.
        static void SeatPose()
        {
            if (seatTrigger == null) return;
            Transform t = seatTrigger.transform, furn = t.parent ?? t;
            Vector3 feet = PlayerSync.LocalFeet;
            Bounds bb = new Bounds(t.position, Vector3.zero);
            bool any = false;
            foreach (Renderer r in furn.GetComponentsInChildren<Renderer>())
            {
                if (!any) { bb = r.bounds; any = true; } else bb.Encapsulate(r.bounds);
            }
            if (!any || bb.size.x > 4f || bb.size.z > 4f)
            {
                // Pas de meuble a sa mesure (abri, batiment) : le volume du declencheur.
                Collider c = t.GetComponent<Collider>();
                bb = c != null ? c.bounds : new Bounds(t.position, Vector3.one * 0.6f);
                furn = t;
            }
            Vector3 center = bb.center;
            float floor = Mathf.Abs(bb.min.y - feet.y) < 0.6f ? bb.min.y : feet.y;
            Vector3[] dirs = { Flat(furn.forward), -Flat(furn.forward), Flat(furn.right), -Flat(furn.right) };
            Vector3 origin = new Vector3(center.x, floor + 0.75f, center.z);
            int back = -1;
            float bestHit = 0.8f;
            for (int i = 0; i < 4; i++)
                foreach (RaycastHit h in Physics.RaycastAll(origin, dirs[i], 0.8f))
                    if (h.collider != null && !h.collider.isTrigger && h.collider.transform.IsChildOf(furn) && h.distance < bestHit) { bestHit = h.distance; back = i; }
            Vector3 fwd;
            if (back >= 0) fwd = -dirs[back];
            else
            {
                Vector3 toP = Flat(feet - center);
                int front = 0; float bestDot = -9f;
                for (int i = 0; i < 4; i++) { float dd = Vector3.Dot(toP, dirs[i]); if (dd > bestDot) { bestDot = dd; front = i; } }
                fwd = toP.sqrMagnitude > 0.01f ? dirs[front] : Flat(cam.forward);
            }
            if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward;
            Vector3 lat = Vector3.Cross(Vector3.up, fwd).normalized;
            float half = Mathf.Abs(Vector3.Dot(bb.extents, new Vector3(Mathf.Abs(lat.x), 0f, Mathf.Abs(lat.z))));
            float along = Mathf.Clamp(Vector3.Dot(feet - center, lat), -Mathf.Max(0f, half - 0.3f), Mathf.Max(0f, half - 0.3f));
            Vector3 s = center + lat * along;
            seatPos = new Vector3(s.x, floor, s.z);
            seatYaw = Quaternion.LookRotation(fwd).eulerAngles.y;
            Log.Info("gestes : assis sur " + Recon.Path(t) + " : siege " + seatPos.ToString("F2") + ", sens " + seatYaw.ToString("F0") + " (" + (back >= 0 ? "dossier a " + bestHit.ToString("F2") + " m" : "sans dossier") + ")");
        }

        static Vector3 Flat(Vector3 v) { v.y = 0f; return v.sqrMagnitude > 1e-6f ? v.normalized : Vector3.zero; }

        // ---------------------------------------------------------------- envoi
        static void SendState(float now)
        {
            int dq = Mathf.Clamp(Mathf.RoundToInt(drunk * 50f), 0, 255);
            int sq = Mathf.Clamp(Mathf.RoundToInt(leanSide), -90, 90), fq = Mathf.Clamp(Mathf.RoundToInt(leanFwd), -90, 90);
            bool changed = bits != sBits || Mathf.Abs(dq - sDrunk) >= 4 || Mathf.Abs(sq - sSide) >= 3 || Mathf.Abs(fq - sFwd) >= 3
                           || (bits & G_Held) != 0 && (heldOff - sHeld).sqrMagnitude > 0.0025f
                           || (bits & G_Seat) != 0 && ((seatPos - sSeat).sqrMagnitude > 0.0025f || Mathf.Abs(Mathf.DeltaAngle(seatYaw, sSeatYaw)) > 3f);
            if (!(changed && now - lastSend > 0.2f) && now < nextSend) return;
            lastSend = now; nextSend = now + 4f;
            if (bits != sBits && Config.GetInt("Test", "JournalGestes", 0) != 0) Log.Info("gestes : etat local " + Names(bits));
            sBits = bits; sDrunk = dq; sSide = sq; sFwd = fq; sHeld = heldOff; sSeat = seatPos; sSeatYaw = seatYaw;
            Session.SendAll(StateMsg(Session.LocalId, bits & ~(G_Fist | G_Finger), dq, sq, fq, heldOff, heldSize, seatPos, seatYaw), true);
        }

        static NetWriter StateMsg(int who, int b, int dq, int sq, int fq, Vector3 held, float size, Vector3 seat, float yaw)
        {
            var w = new NetWriter(Msg.Gesture).U8(who).U8(0).U16(b).U8(dq).U8(sq + 128).U8(fq + 128);
            if ((b & G_Held) != 0) w.Vec(held).U8(Mathf.Clamp(Mathf.RoundToInt(size * 100f), 0, 255));
            if ((b & G_Seat) != 0) w.Vec(seat).F32(yaw);
            return w;
        }

        static void SendEvent(int ev)
        {
            if (Config.GetInt("Test", "JournalGestes", 0) != 0) Log.Info("gestes : geste local " + (ev == E_Punch ? "coup" : "doigt"));
            Session.SendAll(new NetWriter(Msg.Gesture).U8(Session.LocalId).U8(1).U8(ev), true);
        }

        public static void OnMessage(Peer from, NetReader r)
        {
            int who = r.U8();
            if (Session.IsHost) who = from.Id;
            int kind = r.U8();
            if (kind == 2) { OnStains(r, who); return; }
            Remote g;
            if (!remote.TryGetValue(who, out g)) remote[who] = g = new Remote();
            g.Time = Time.realtimeSinceStartup;
            if (kind == 1)
            {
                int ev = r.U8();
                if (ev == E_Punch) g.PunchAt = g.Time; else if (ev == E_Finger) g.FingerAt = g.Time;
                if (Session.IsHost) Session.Broadcast(new NetWriter(Msg.Gesture).U8(who).U8(1).U8(ev), true, who);
                if (Logging) Log.Info("autotest : gestes : recu de #" + who + " : " + (ev == E_Punch ? "coup" : "doigt"));
                return;
            }
            int b = r.U16(), dq = r.U8(), sq = r.U8() - 128, fq = r.U8() - 128;
            Vector3 held = Vector3.zero, seat = Vector3.zero; float size = 0f, yaw = 0f;
            if ((b & G_Held) != 0) { held = r.Vec(); size = r.U8() / 100f; }
            if ((b & G_Seat) != 0) { seat = r.Vec(); yaw = r.F32(); }
            if (Session.IsHost) Session.Broadcast(StateMsg(who, b, dq, sq, fq, held, size, seat, yaw), true, who);
            if (Logging && b != g.Bits) Log.Info("autotest : gestes : recu de #" + who + " : " + Names(b) + ", ivresse " + (dq / 50f).ToString("F2") + ", penche " + sq + "/" + fq);
            g.Bits = b; g.Drunk = dq / 50f; g.LeanSide = sq; g.LeanFwd = fq;
            g.Held = held; g.HeldSize = size; g.Seat = seat; g.SeatYaw = yaw;
        }

        static bool Logging { get { return Config.Get("Test", "Autotest", "").StartsWith("gestes") || Config.Get("Test", "Autotest", "") == "assis-chaise" || Config.GetInt("Test", "JournalGestes", 0) != 0; } }

        public static string Names(int b)
        {
            if (b == 0) return "rien";
            var l = new List<string>();
            if ((b & G_Thumb) != 0) l.Add("pouce");
            if ((b & G_Watch) != 0) l.Add("montre");
            if ((b & G_Push) != 0) l.Add("pousse");
            if ((b & G_Piss) != 0) l.Add("pipi");
            if ((b & G_Down) != 0) l.Add("a terre");
            if ((b & G_Held) != 0) l.Add("tient");
            if ((b & G_Seat) != 0) l.Add("assis");
            if ((b & G_Finger) != 0) l.Add("doigt");
            if ((b & G_Fist) != 0) l.Add("coup");
            return string.Join(" ", l.ToArray());
        }

        // ---------------------------------------------------------------- flaques (YARD/PissAreas)
        static PlayMakerFSM stainsFsm;
        static readonly List<Transform> stains = new List<Transform>();
        static readonly List<Vector3> stainSeen = new List<Vector3>();
        static bool stainsClaimed;
        static float nextStain;

        static void ClaimStains()
        {
            stainsClaimed = true;
            GameObject pa = Game.FindAny("YARD/PissAreas");
            stainsFsm = pa != null ? Game.FsmOn(pa, "Logic") : null;
            if (stainsFsm == null) { Log.Info("gestes : YARD/PissAreas :: Logic absent (pas de flaques suivies)"); return; }
            if (!Replay.Claim(stainsFsm, Module)) { Log.Warn("gestes : YARD/PissAreas deja pris par " + Replay.Owner(stainsFsm)); stainsFsm = null; return; }
            var names = new List<string>();
            foreach (FsmGameObject v in stainsFsm.FsmVariables.GameObjectVariables) if (v.Name.StartsWith("Stain")) names.Add(v.Name);
            names.Sort(System.StringComparer.Ordinal);
            stains.Clear(); stainSeen.Clear();
            foreach (string n in names)
            {
                GameObject s = stainsFsm.FsmVariables.GetFsmGameObject(n).Value;
                stains.Add(s != null ? s.transform : null);
                stainSeen.Add(s != null ? s.transform.localScale : Vector3.zero);
            }
            Log.Info("gestes : flaques de la maison suivies (" + stains.Count + ")");
        }

        static void Stains(float now)
        {
            if (stainsFsm == null || now < nextStain || Session.RemoteCount == 0) return;
            nextStain = now + 0.5f;
            NetWriter w = null;
            var changed = new List<int>();
            for (int i = 0; i < stains.Count; i++)
            {
                Transform s = stains[i];
                if (s == null || (s.localScale - stainSeen[i]).sqrMagnitude < 1e-5f) continue;
                stainSeen[i] = s.localScale;
                changed.Add(i);
            }
            if (changed.Count == 0) return;
            w = new NetWriter(Msg.Gesture).U8(Session.LocalId).U8(2).U8(changed.Count);
            foreach (int i in changed) w.U8(i).Vec(stains[i].localScale);
            Session.SendAll(w, true);
        }

        static void OnStains(NetReader r, int who)
        {
            int n = r.U8();
            var w = new NetWriter(Msg.Gesture).U8(who).U8(2).U8(n);
            for (int k = 0; k < n; k++)
            {
                int i = r.U8();
                Vector3 sc = r.Vec();
                w.U8(i).Vec(sc);
                if (i < stains.Count && stains[i] != null) { stains[i].localScale = sc; stainSeen[i] = sc; }
            }
            if (Session.IsHost) Session.Broadcast(w, true, who);
            if (Logging) Log.Info("autotest : gestes : flaques de #" + who + " : " + n + " changees");
        }

        static string StainsText()
        {
            var sb = new System.Text.StringBuilder();
            foreach (Transform s in stains) sb.Append(s != null ? s.localScale.x.ToString("F2") : "-").Append(' ');
            return sb.ToString().Trim();
        }

        // ---------------------------------------------------------------- essais
        // [Test] Autotest=gestes : l'acteur (l'hote, ou [Test] GestesActeur=1/0) enchaine a partir de [Test] GestesDebut
        // (40 s) un geste toutes les 6 s en pilotant les automates du jeu comme ses touches : coup (PlayerFunctions
        // -> "Fist"), doigt ("Finger"), pouce ("Finger 2", baisse a +4 s par "Finger 3"), montre ("Anim on"), pipi
        // (Piss :: Logic -> "Full power", arret a +4 s par "State 1"), penche (Reach -> "Move down", puis Camera
        // tournee de +-15 deg), ivresse (PlayerDrunk = 2,5 pendant 5 s), objet tenu (force), puis evanoui (Systems/
        // PassOut -> "Pass out", si [Test] GestesEvanoui=1, defaut). Il note ce que le jeu fait ("acteur").
        // L'observateur note chaque seconde ce que montre l'avatar ("avatar").
        // [Test] Autotest=assis-chaise : l'acteur est pose a 40 s dans le declencheur du canape de l'appartement
        // ([Test] Siege = debut du chemin, defaut HOMENEW/FurnitureDisable/sofa), releve a 70 s.
        static int step = -1;
        static float stepAt, nextLog, testSeatAt = -1;
        static float drunkWas;
        static Quaternion leanWas;
        static readonly string[] Steps = { "coup", "doigt", "pouce", "montre", "pipi", "penche", "ivresse", "porter", "evanoui" };

        public static void Test(string mode, float t)
        {
            if (mode != "gestes" && mode != "assis-chaise") return;
            if (!Session.Active || !FindLocal()) return;
            int role = Config.GetInt("Test", "GestesActeur", -1);
            bool actor = role < 0 ? Session.IsHost : role != 0;
            float start = Config.GetInt("Test", "GestesDebut", 40);
            float now = Time.realtimeSinceStartup;
            if (now >= nextLog && t > 10f)
            {
                nextLog = now + 1f;
                if (actor) Log.Info("autotest : " + mode + " : acteur : " + LocalText());
                foreach (Avatar a in PlayerSync.Avatars) Log.Info("autotest : " + mode + " : avatar " + a.Player.Name + " : " + a.GestureState());
            }
            if (!actor || t < start) return;
            if (mode == "assis-chaise") { TestSeat(t - start); return; }
            int k = (int)((t - start) / 6f);
            float dt = t - start - k * 6f;
            if (k != step && k < Steps.Length)
            {
                EndStep(step);
                step = k; stepAt = t;
                Log.Info("autotest : gestes : acteur : debut " + Steps[k] + " -> " + BeginStep(Steps[k]));
            }
            if (step < 0 || step >= Steps.Length) return;
            string s = Steps[step];
            if (s == "pouce" && dt > 4f && FnState == "Thumb Up") { Game.SetState(functions, "Finger 3"); Log.Info("autotest : gestes : acteur : pouce baisse"); }
            if (s == "pipi" && dt > 4f && Active(fluid) && pissFsm != null) { Game.SetState(pissFsm, "State 1"); Log.Info("autotest : gestes : acteur : pipi arrete"); }
            if (s == "penche" && dt > 1.5f && camBase != null)
            {
                // Camera tournee comme par une touche de penche (droite 1,5-3,5 s, gauche 3,5-5,5 s).
                float ang = dt < 3.5f ? -15f : dt < 5.5f ? 15f : 0f;
                camBase.localRotation = Quaternion.Euler(0f, 0f, ang) * leanWas;
            }
            if (s == "ivresse" && dt > 5f) { SetDrunk(drunkWas); }
        }

        static string BeginStep(string s)
        {
            switch (s)
            {
                case "coup": return functions != null && Game.SetState(functions, "Fist") ? "Fist" : "pas d'automate PlayerFunctions";
                case "doigt": return functions != null && Game.SetState(functions, "Finger") ? "Finger" : "pas d'automate PlayerFunctions";
                case "pouce": return functions != null && Game.SetState(functions, "Finger 2") ? "Finger 2" : "pas d'automate PlayerFunctions";
                case "montre": return functions != null && Game.SetState(functions, "Anim on") ? "Anim on" : "pas d'automate PlayerFunctions";
                case "pipi": return pissFsm != null && Game.SetState(pissFsm, "Full power") ? "Full power" : "pas d'automate Piss";
                case "penche":
                    {
                        if (camBase != null) leanWas = camBase.localRotation;
                        PlayMakerFSM reach = player != null ? Game.FsmOn(player.gameObject, "Reach") : null;
                        return reach != null && Game.SetState(reach, "Move down") ? "Reach -> Move down" : "pas d'automate Reach";
                    }
                case "ivresse":
                    {
                        FsmFloat d = FsmVariables.GlobalVariables.FindFsmFloat("PlayerDrunk");
                        drunkWas = d != null ? d.Value : 0f;
                        SetDrunk(2.5f);
                        return "PlayerDrunk " + drunkWas.ToString("F2") + " -> 2,5";
                    }
                case "porter": testHeld = true; Autotest.PoseFlags |= PlayerSync.F_Carry; return "objet tenu force";
                case "evanoui":
                    if (Config.GetInt("Test", "GestesEvanoui", 1) == 0) return "saute";
                    return passOut != null && Game.SetState(passOut, "Pass out") ? "PassOut -> Pass out" : "pas d'automate PassOut";
            }
            return "?";
        }

        static void EndStep(int k)
        {
            if (k < 0 || k >= Steps.Length) return;
            string s = Steps[k];
            if (s == "penche" && camBase != null) camBase.localRotation = leanWas;
            if (s == "ivresse") SetDrunk(drunkWas);
            if (s == "porter") { testHeld = false; Autotest.PoseFlags &= ~PlayerSync.F_Carry; }
            if (s == "pipi" && Active(fluid) && pissFsm != null) Game.SetState(pissFsm, "State 1");
            Log.Info("autotest : gestes : acteur : fin " + s);
        }

        static void SetDrunk(float v)
        {
            FsmFloat d = FsmVariables.GlobalVariables.FindFsmFloat("PlayerDrunk");
            if (d != null) d.Value = v;
        }

        static void TestSeat(float t)
        {
            if (testSeatAt < 0)
            {
                testSeatAt = t;
                string want = Config.Get("Test", "Siege", "HOMENEW/FurnitureDisable/sofa");
                nextTriggerScan = 0;
                FindSeat();
                PlayMakerFSM best = null;
                foreach (PlayMakerFSM f in triggers)
                    if (f != null && f.gameObject.activeInHierarchy && Recon.Path(f.transform).StartsWith(want)) { best = f; break; }
                if (best == null) { Log.Warn("autotest : assis-chaise : aucun declencheur actif sous " + want); return; }
                Teleport(best.transform.position + Vector3.up * 0.1f);
                Log.Info("autotest : assis-chaise : acteur : pose dans " + Recon.Path(best.transform) + " en " + best.transform.position.ToString("F2"));
                testTrigger = best;
                return;
            }
            float dt = t - testSeatAt;
            var cc = player.GetComponent<CharacterController>();
            if (dt < 2f && cc != null && cc.enabled) cc.Move(Vector3.down * 0.01f);   // un deplacement : le declencheur voit le joueur
            if (dt > 3f && !forced && testTrigger != null && !Game.GlobalBool("PlayerSeated") && testTrigger.ActiveStateName != "Press return")
            {
                forced = true;
                Log.Info("autotest : assis-chaise : acteur : declencheur pas entre (" + testTrigger.ActiveStateName + "), force en Press return");
                Game.SetState(testTrigger, "Press return");
            }
            if (dt > 30f && !leftSeat && testTrigger != null)
            {
                leftSeat = true;
                Vector3 away = player.position + Quaternion.Euler(0f, seatYaw, 0f) * Vector3.forward * 1.5f;
                Teleport(away);
                if (testTrigger != null && testTrigger.ActiveStateName == "Press return") Game.SetState(testTrigger, "Reset player");
                Log.Info("autotest : assis-chaise : acteur : releve en " + away.ToString("F2"));
            }
        }

        static PlayMakerFSM testTrigger;
        static bool forced, leftSeat;

        static void Teleport(Vector3 p)
        {
            var cc = player.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;
            player.position = p;
            if (cc != null) cc.enabled = true;
        }

        static string LocalText()
        {
            return Names(bits) + " | fonctions " + FnState + ", poing " + Active(fist) + "/" + (fistAnim != null && fistAnim.isPlaying)
                   + ", doigt " + Active(middleFinger) + "/" + (fingerAnim != null && fingerAnim.isPlaying) + ", montre " + Active(watch)
                   + ", jet " + Active(fluid) + " (" + (pissFsm != null ? pissFsm.ActiveStateName : "-") + ")"
                   + ", ivresse " + drunk.ToString("F2") + ", penche " + leanSide.ToString("F0") + "/" + leanFwd.ToString("F0")
                   + (camBase != null ? " (Camera " + (Quaternion.Inverse(player.rotation) * camBase.rotation).eulerAngles.ToString("F0") + ", Pivot " + player.Find("Pivot").localEulerAngles.ToString("F0") + ")" : "")
                   + ", evanoui " + (passOut != null ? passOut.ActiveStateName : "-") + "/" + (knockOut != null ? knockOut.ActiveStateName : "-")
                   + ((bits & G_Held) != 0 ? ", tenu " + heldOff.ToString("F2") + " " + heldSize.ToString("F2") + " m" : "")
                   + ", PlayerSeated " + Game.GlobalBool("PlayerSeated") + ", Crouch " + (crouch != null ? crouch.ActiveStateName : "-")
                   + ((bits & G_Seat) != 0 ? ", siege " + seatPos.ToString("F2") + " sens " + seatYaw.ToString("F0") : "")
                   + ", pieds " + PlayerSync.LocalFeet.ToString("F2")
                   + (stains.Count > 0 ? ", flaques " + StainsText() : "");
        }
    }
}
