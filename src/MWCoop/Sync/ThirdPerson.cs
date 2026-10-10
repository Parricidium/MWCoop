using System.Collections;
using MWCoop.Net;
using UnityEngine;

namespace MWCoop
{
    // Vue a la troisieme personne (demande de JD, 10/10 : « a pied et en vehicule ») : touche Keys.View (F5, lanceur).
    //  - le joueur local a son propre avatar (meme modele, meme apparence que chez les autres), mene par son etat de
    //    chaque image (PlayerSync.BuildLocal) et ses gestes (Gestures), assis au volant ou en passager comme les autres ;
    //  - la camera du jeu n'est reculee QUE pendant le rendu (Camera.onPreCull -> fin d'image) : les rayons du jeu (viser,
    //    prendre, ouvrir), le son et tout le reste restent aux yeux. A pied : derriere la tete, un peu au-dessus, dans
    //    l'axe du regard (le centre de l'ecran vise presque le meme point qu'a la premiere personne) ; en vehicule :
    //    autour du vehicule, dans le sens du regard. Un mur ou le sol entre les deux : la camera s'arrete devant ;
    //  - les mains a la premiere personne (HandCamera) sont eteintes, l'outil tenu est montre dans la main de l'avatar.
    public static class ThirdPerson
    {
        public static bool Active;
        static Avatar me;
        static string meSkin;
        static Camera cam, handCam;
        static bool handCamWas;
        static Vector3 eye; static Quaternion eyeRot; static bool moved;
        static float dist = -1f;
        static bool hooked;
        static int lastFrame = -1;

        // Yeux du joueur local (la camera, hors du rendu ou elle est reculee).
        public static Vector3 EyeOf(Transform c) { return moved && cam != null && c == cam.transform ? eye : c.position; }
        public static Vector3 EyePosition { get { return moved ? eye : PlayerSync.LocalCamera != null ? PlayerSync.LocalCamera.position : Vector3.zero; } }

        public static void OnLevelLoaded() { Off(); cam = null; handCam = null; }

        static void Off()
        {
            if (me != null) { me.Destroy(); me = null; }
            if (handCam != null) handCam.enabled = handCamWas;
            if (Active) Log.Info("vue : premiere personne");
            Active = false; dist = -1f;
        }

        public static void Update()
        {
            if (!PlayerSync.InGame || !Session.Active) { if (Active) Off(); return; }
            if (!Menu.Open && !Menu.ChatOpen && !MapPanel.Open && !CheatPanel.Open && !WalletPanel.Open && Input.GetKeyDown(Keys.View))
            {
                if (Active) Off();
                else if (Config.GetInt("Coop", "SansTroisiemePersonne", 0) == 0) { Active = true; Log.Info("vue : troisieme personne"); }
            }
            if (Config.GetInt("Test", "TroisiemePersonne", 0) != 0 && !Active) { Active = true; Log.Info("vue : troisieme personne (essai)"); }
            if (!Active) return;
            if (!hooked) { hooked = true; Camera.onPreCull += PreCull; }
            if (cam == null)
            {
                Transform c = PlayerSync.LocalCamera;
                cam = c != null ? c.GetComponent<Camera>() : null;
                Transform hc = c != null ? c.Find("HandCamera") : null;
                handCam = hc != null ? hc.GetComponent<Camera>() : null;
                if (handCam != null) handCamWas = handCam.enabled;
                if (cam == null) return;
            }
            if (handCam != null && handCam.enabled) handCam.enabled = false;
            PlayerInfo pi = Session.Me;
            if (pi == null) return;
            if (me != null && (me.Root == null || meSkin != pi.Skin && me.Body != Looks.Parse(pi.Skin).Body)) { me.Destroy(); me = null; }
            if (me == null)
            {
                me = Avatar.Create(pi);
                if (me == null) return;
                meSkin = pi.Skin;
            }
            pi.State = PlayerSync.BuildLocal();
            pi.StateTime = Time.realtimeSinceStartup;
            me.Apply(pi);
        }

        // Apres les poses des autres avatars (PlayerSync.LateUpdate).
        public static void LatePose()
        {
            if (Active && me != null && me.Root != null) me.LatePose();
        }
        public static Avatar Me { get { return Active ? me : null; } }

        // Rendu de la camera du joueur : reculee le temps de l'image, remise aux yeux a la fin de l'image.
        static void PreCull(Camera c)
        {
            if (!Active || c == null || c != cam || moved || Time.frameCount == lastFrame) return;
            Transform t = c.transform;
            eye = t.position; eyeRot = t.rotation;
            Vector3 fwd = t.forward;
            Vector3 pivot, want;
            Transform car = VehicleSync.LocalDrivingRoot ?? Seats.LocalCar;
            float target;
            Transform ignore = car;
            if (car != null)
            {
                // Vehicule : autour de son centre, a une distance selon sa taille.
                Bounds b = new Bounds(car.position, Vector3.zero);
                bool any = false;
                foreach (Renderer r in car.GetComponentsInChildren<Renderer>())
                {
                    if (!r.enabled || r.bounds.size.sqrMagnitude > 400f) continue;
                    if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
                }
                pivot = b.center + Vector3.up * (b.extents.y * 0.6f);
                target = Mathf.Clamp(b.extents.magnitude * 1.7f, 4.5f, 14f) * Config.GetFloat("Coop", "VueDistanceVehicule", 1f);
                want = pivot - fwd * target + Vector3.up * 0.4f;
            }
            else
            {
                pivot = eye + Vector3.up * 0.25f;
                target = Mathf.Clamp(Config.GetFloat("Coop", "VueDistance", 2.4f), 1f, 6f);
                want = pivot - fwd * target;
            }
            // Obstacle entre le pivot et la camera : devant lui (le joueur et le vehicule ne comptent pas).
            Vector3 d = want - pivot;
            float len = d.magnitude, free = len;
            if (len > 0.01f)
            {
                Vector3 dir = d / len;
                foreach (RaycastHit h in Physics.SphereCastAll(pivot, 0.18f, dir, len))
                {
                    if (h.collider.isTrigger || Game.UnderPlayer(h.collider.transform) || ignore != null && h.collider.transform.IsChildOf(ignore)) continue;
                    if (h.distance < free) free = h.distance;
                }
                free = Mathf.Max(0.2f, free - 0.05f);
                // (rapprochee d'un coup, eloignee en douceur : pas de va-et-vient contre un mur)
                dist = dist < 0f || free < dist ? free : Mathf.Lerp(dist, free, 1f - Mathf.Exp(-4f * Time.deltaTime));
                t.position = pivot + dir * Mathf.Min(dist, free);
            }
            moved = true;
            lastFrame = Time.frameCount;
            if (Core.I != null) Core.I.StartCoroutine(Restore());
        }

        static IEnumerator Restore()
        {
            yield return new WaitForEndOfFrame();
            if (moved && cam != null) { cam.transform.position = eye; cam.transform.rotation = eyeRot; }
            moved = false;
        }

        // [Test] Autotest=vue : troisieme personne a 25 s (captures a pied), en voiture si TestVoiture et le joueur y monte.
        public static void Test(string mode, float t)
        {
            if (mode == "pantin")
            {
                // invite : mort de 30 a 45 s ; hote : captures pendant la chute, au sol, apres.
                if (!Session.IsHost) Autotest.TestDead = t > 30f && t < 45f;
                else
                {
                    bool rag = false;
                    foreach (Avatar a in PlayerSync.Avatars) if (a.Root != null && a.Ragdolled) rag = true;
                    if (rag && testStep == 0) { testStep = 1; testAt = t; }
                    if (testStep == 1 && t > testAt + 0.4f) { testStep = 11; Autotest.CaptureSoon("mort-chute", 0.05f); }
                    if (testStep == 11 && t > testAt + 4f)
                    {
                        testStep = 2; Autotest.CaptureSoon("mort-sol", 0.05f);
                        foreach (Avatar a in PlayerSync.Avatars) if (a.Root != null) Log.Info("autotest : mort, avatar #" + a.Player.Id + (a.Ragdolled ? " en pantin" : " PAS en pantin") + ", bassin " + (a.PelvisT != null ? a.PelvisT.position.ToString("F2") : "?") + ", racine " + a.Root.transform.position.ToString("F2"));
                    }
                    if (!rag && testStep == 2) { testStep = 21; testAt = t; }
                    if (testStep == 21 && t > testAt + 2f)
                    {
                        testStep = 3; Autotest.CaptureSoon("mort-apres", 0.05f);
                        foreach (Avatar a in PlayerSync.Avatars) if (a.Root != null) Log.Info("autotest : mort, apres : avatar #" + a.Player.Id + (a.Ragdolled ? " ENCORE en pantin" : " debout") + ", bassin " + (a.PelvisT != null ? a.PelvisT.position.ToString("F2") : "?"));
                    }
                }
                return;
            }
            if (mode != "vue") return;
            if (t > 25f && !Active && testStep == 0) { testStep = 1; Active = true; Log.Info("autotest : vue troisieme personne"); }
            string car = Config.Get("Test", "VueVoiture", "");
            if (car.Length > 0 && Session.IsHost)
            {
                if (t > 34f && testCar == 0) { testCar = 1; Log.Info("autotest : vue, voiture -> " + VehicleSync.TestEnter(car, false)); }
                if (t > 38f && testCar == 1) { testCar = 2; Log.Info("autotest : vue, volant -> " + VehicleSync.TestEnter(car, true)); }
                if (t > 46f && testCar == 2) { testCar = 3; Autotest.CaptureSoon("vue-voiture", 0.05f); Transform vc = VehicleSync.LocalDrivingRoot; Vector3 dh;
                    Log.Info("autotest : vue, avatar local " + (me != null && me.Root != null ? "a " + me.Root.transform.position.ToString("F1") : "ABSENT") + ", voiture " + (vc != null ? vc.name : "aucune")
                             + (vc != null && me != null && me.Root != null ? ", repere voiture : racine " + vc.InverseTransformPoint(me.Root.transform.position).ToString("F2") + " bassin " + (me.PelvisT != null ? vc.InverseTransformPoint(me.PelvisT.position).ToString("F2") : "?") + " yeux " + vc.InverseTransformPoint(EyePosition).ToString("F2") + (Seats.DriverHead(vc, out dh) ? " tete-conducteur " + dh.ToString("F2") : "") + ", main droite a " + (me.HandRight != null ? vc.InverseTransformPoint(me.HandRight.position).ToString("F2") : "?") : "")); }
            }
            if (t > 32f && testStep == 1) { testStep = 2; Autotest.CaptureSoon("vue-pied", 0.05f); Log.Info("autotest : vue, avatar local " + (me != null && me.Root != null ? "present a " + me.Root.transform.position.ToString("F1") : "ABSENT") + ", yeux " + EyePosition.ToString("F1")); }
        }
        static int testStep, testCar; static float testAt;
    }
}
