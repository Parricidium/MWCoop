using System;
using System.Reflection;
using MWCoop.Net;
using UnityEngine;

namespace MWCoop
{
    // Compatibilite du mod « Utility Tow Cart » (TheGaneson22, Nexus 1548 ; demande de Somegames1, 12/10). Son chariot
    // (« TowCart », corps physique) se tire par sa poignee (TowCartPoint1, prise en main : articulation chariot-poignee) ou
    // s'attelle a une voiture (Use pres de l'attelage HitchmountXxx/DetectorTrigger : articulation chariot-voiture, feux de la
    // voiture recopies, poignee cachee) ; sa bequille se regle a la molette (StandPivot/Stand). Tout etait local.
    // Ici, comme une remorque : un seul joueur fait foi pour le chariot -- celui qui tient la poignee, ou dont la voiture
    // l'a attele, sinon l'hote -- et envoie sa pose (@tow : 15 fois/s quand il bouge, 1 fois/s sinon) avec l'attelage et la
    // bequille ; chez les autres le chariot est cinematique et suit, l'attelage montre (poignee cachee, feux) sans
    // articulation. Prendre la poignee chez soi ou atteler sa voiture fait passer la main.
    public static class ModTowCart
    {
        const BindingFlags A = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        static GameObject cart;
        static Rigidbody body;
        static Transform point, standPivot, stand;
        static Component towStand;
        static float findAt, nextSend, lastRecv = -99f;
        static int owner = -1;                 // qui fait foi (-1 : personne encore -- l'hote)
        static Vector3 rPos, rVel, rAng; static Quaternion rRot; static string rHitch = ""; static float rStand = -1f;
        static string shownHitch = "";         // attelage montre ici (suiveur)
        static bool wasFollower;
        static Assembly asm;

        public static void OnLevelLoaded() { cart = null; body = null; owner = -1; lastRecv = -99f; shownHitch = ""; findAt = Time.realtimeSinceStartup + 15f; wasFollower = false; }

        static bool Find()
        {
            if (cart != null && body != null) return true;
            if (Time.realtimeSinceStartup < findAt) return false;
            findAt = Time.realtimeSinceStartup + 5f;
            if (asm == null)
                foreach (Assembly a in AppDomain.CurrentDomain.GetAssemblies())
                {
                    Type t = null; try { t = a.GetType("Utility_Tow_Cart.TowStand", false); } catch { }
                    if (t != null) { asm = a; break; }
                }
            if (asm == null) { findAt = Time.realtimeSinceStartup + 60f; return false; }
            cart = GameObject.Find("TowCart");
            body = cart != null ? cart.GetComponent<Rigidbody>() : null;
            if (body == null) { cart = null; return false; }
            point = cart.transform.Find("TowCartPoint1");
            standPivot = cart.transform.Find("StandPivot");
            stand = standPivot != null ? standPivot.Find("Stand") : null;
            towStand = standPivot != null ? standPivot.GetComponent(asm.GetType("Utility_Tow_Cart.TowStand")) : null;
            Log.Info("mods : Utility Tow Cart present, chariot suivi (" + cart.transform.position.ToString("F1") + ")");
            return true;
        }

        // Poignee tenue ici (dans la main du joueur d'ici) ; voiture attelee ici (articulation du chariot vers une voiture).
        static bool Held() { return point != null && point.parent != null && point.parent.name == "ItemPivot" && Game.UnderPlayer(point); }
        static string HitchedHere()
        {
            foreach (ConfigurableJoint j in cart.GetComponents<ConfigurableJoint>())
                if (j != null && j.connectedBody != null && (point == null || j.connectedBody.transform != point))
                {
                    Transform mount = MountOf(j.connectedBody.transform);
                    return mount != null ? mount.name : "";
                }
            return "";
        }
        static Transform MountOf(Transform car)
        {
            foreach (Transform c in car.GetComponentsInChildren<Transform>(true)) if (c.name.StartsWith("Hitchmount")) return c;
            return null;
        }
        static Transform MountByName(string n)
        {
            foreach (Transform t in UnityEngine.Object.FindObjectsOfType<Transform>()) if (t.name == n && t.parent != null) return t;
            return null;
        }

        public static void Update()
        {
            if (!Session.Active || !PlayerSync.InGame || !Find()) return;
            float now = Time.realtimeSinceStartup;
            bool held = Held();
            string hitch = HitchedHere();
            bool claim = held || hitch.Length > 0;
            if (claim && owner != Session.LocalId) { owner = Session.LocalId; Log.Info("mods : chariot, la main ici (" + (held ? "poignee tenue" : "attele a " + hitch) + ")"); }
            if (owner < 0 && Session.IsHost) owner = Session.LocalId;
            if (owner != Session.LocalId && owner >= 0 && now - lastRecv > 6f && Session.IsHost) owner = Session.LocalId;   // (l'autre est parti ou se tait)
            bool mine = owner == Session.LocalId;
            if (mine)
            {
                if (wasFollower) { wasFollower = false; body.isKinematic = false; body.velocity = rVel; ShowHitch(""); }
                if (Session.RemoteCount == 0) return;
                bool moving = body.velocity.sqrMagnitude > 0.0025f || body.angularVelocity.sqrMagnitude > 0.0025f || held;
                if (now < nextSend) return;
                nextSend = now + (moving ? 1f / 15f : 1f);
                float sy = stand != null ? stand.localPosition.y : -1f;
                Session.SendAll(new NetWriter(Msg.Job).U8(Session.LocalId).Str("@tow").U8(claim ? 1 : 0)
                    .Vec(body.position).Quat(body.rotation).Vec(body.velocity).Vec(body.angularVelocity).Str(hitch).F32(sy), !moving);
                return;
            }
            // suiveur : cinematique, rejoint la pose recue (extrapolee) -- rien avant le premier message
            if (lastRecv < 0f) return;
            if (!wasFollower) { wasFollower = true; body.isKinematic = true; }
            if (!body.isKinematic) body.isKinematic = true;
            float dt = Mathf.Min(now - lastRecv, 0.3f);
            Vector3 target = rPos + rVel * dt;
            Quaternion rot = rAng.sqrMagnitude > 1e-4f ? Quaternion.AngleAxis(rAng.magnitude * dt * Mathf.Rad2Deg, rAng.normalized) * rRot : rRot;
            float k = 1f - Mathf.Exp(-12f * Time.deltaTime);
            if ((cart.transform.position - target).sqrMagnitude > 16f) { cart.transform.position = target; cart.transform.rotation = rot; }
            else { cart.transform.position = Vector3.Lerp(cart.transform.position, target, k); cart.transform.rotation = Quaternion.Slerp(cart.transform.rotation, rot, k); }
            if (rHitch != shownHitch) ShowHitch(rHitch);
            if (rStand >= 0f && towStand != null)
            {
                FieldInfo f = towStand.GetType().GetField("targetStandPos", A);
                if (f != null) { Vector3 v = (Vector3)f.GetValue(towStand); if (Mathf.Abs(v.y - rStand) > 0.001f) { v.y = rStand; f.SetValue(towStand, v); } }
            }
        }

        // Attelage montre chez un suiveur (sans articulation : la pose vient de celui qui fait foi) : poignee cachee et feux
        // du chariot relies a ceux de la voiture, comme le mod.
        static void ShowHitch(string mount)
        {
            if (mount == shownHitch) return;
            try
            {
                Type lt = asm.GetType("Utility_Tow_Cart.CarLightstoTc"), tl = asm.GetType("Utility_Tow_Cart.TowCartLights");
                if (shownHitch.Length > 0)
                {
                    Transform old = MountByName(shownHitch);
                    Component cl = old != null && lt != null ? old.GetComponent(lt) : null;
                    if (cl != null && lt.GetField("TCL", A).GetValue(cl) != null) { lt.GetMethod("RemoveConfigs", A).Invoke(cl, null); lt.GetField("TowCart", A).SetValue(cl, null); }
                    if (point != null) point.gameObject.SetActive(true);
                }
                if (mount.Length > 0)
                {
                    Transform m = MountByName(mount);
                    Component cl = m != null && lt != null ? m.GetComponent(lt) : null;
                    Transform lights = cart.transform.Find("Lights");
                    if (cl != null && lights != null && tl != null) { lt.GetField("TowCart", A).SetValue(cl, cart); lt.GetField("TCL", A).SetValue(cl, lights.GetComponent(tl)); }
                    if (point != null && point.parent == cart.transform) point.gameObject.SetActive(false);
                }
                Log.Info("mods : chariot " + (mount.Length > 0 ? "attele a " + mount : "detele") + " chez l'autre, montre ici");
            }
            catch (Exception e) { Log.Warn("mods : chariot, attelage montre : " + e.Message); }
            shownHitch = mount;
        }

        public static void OnMessage(int who, NetReader r)
        {
            if (!Find()) return;
            bool claim = r.U8() != 0;
            Vector3 p = r.Vec(); Quaternion q = r.Quat(); Vector3 v = r.Vec(), w = r.Vec(); string hitch = r.Str(); float sy = r.F32();
            bool mineNow = owner == Session.LocalId && (Held() || HitchedHere().Length > 0);
            if (mineNow && !claim) return;                 // (je la tiens : la sienne ne compte pas)
            if (owner != who) Log.Info("mods : chariot, la main a #" + who + (claim ? " (poignee ou attelage)" : ""));
            owner = who;
            rPos = p; rRot = q; rVel = v; rAng = w; rHitch = hitch; rStand = sy; lastRecv = Time.realtimeSinceStartup;
        }

        // Essai [Test] Autotest=chariot : l'hote pousse le chariot (4 s, 1,5 m/s) a 40 s ; chacun note sa pose a 50 s.
        // [Test] ChariotInvite=1 : c'est l'invite qui le prend (poignee simulee : il fait foi) a 55 s et le pousse.
        static int tStep;
        public static void Test(string mode, float t)
        {
            if (mode != "chariot" || !Find()) return;
            bool guestTakes = Config.GetInt("Test", "ChariotInvite", 0) != 0;
            if (Session.IsHost && t > 40f && t < 44f) { owner = Session.LocalId; body.isKinematic = false; body.velocity = cart.transform.forward * 1.5f; }
            if (t > 50f && tStep == 0) { tStep = 1; Log.Info("autotest : chariot en " + cart.transform.position.ToString("F2") + ", cinematique " + body.isKinematic + ", main a #" + owner); }
            if (guestTakes && !Session.IsHost && t > 55f && t < 59f) { owner = Session.LocalId; wasFollower = false; body.isKinematic = false; body.velocity = -cart.transform.forward * 1.5f; }
            if (t > 85f && tStep == 2) { tStep = 3; Log.Info("autotest : chariot (fin) en " + cart.transform.position.ToString("F2") + ", cinematique " + body.isKinematic + ", main a #" + owner); }
            if (t > 66f && tStep == 1) { tStep = 2; Log.Info("autotest : chariot en " + cart.transform.position.ToString("F2") + ", cinematique " + body.isKinematic + ", main a #" + owner); }
        }
    }
}
