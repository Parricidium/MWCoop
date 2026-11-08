using System.Collections.Generic;
using HutongGames.PlayMaker;
using MWCoop.Net;
using UnityEngine;

namespace MWCoop
{
    // [Test] Autotest=carnetroute (2 joueurs, [MWC] 08/11) : carnet du copilote leve, voiture a 140 km/h (JD 08/11 : « on le
    // voit partir en arriere et revenir [...] a grande vitesse sur l'autoroute, ca fait des mini rollback »).
    //  - invite : carnet demande a 30 s, pris a 34 s, assis a l'avant de TestVoiture a 37 s, leve a 41 s ;
    //  - hote : TestVoiture posee (cinematique) au depart de SS1 des 32 s, joueur a la portiere a 40 s, au volant a 42 s ; des que
    //    le carnet de l'invite est annonce leve ET suivi dans la voiture (au plus tard a 110 s) : camera d'essai fixee a la voiture
    //    (cote conducteur, vers les mains du passager), puis 2 s apres, 20 s de ligne droite, mise en vitesse en 6 s jusqu'a
    //    [Test] CarnetVitesse km/h (140), 6 m au-dessus du depart, sans gravite ni collisions (rien a heurter, pas de relief),
    //    rotation bloquee ;
    //  - chaque seconde : chez l'hote (de 7 a 20 s de route), le carnet tel qu'il sera rendu (fin de l'image) face a la pose recue
    //    dans la voiture, et ses sauts d'une image a l'autre ; chez l'invite (copie a plus de 30 km/h), vitesse de sa copie ;
    //  - captures : hote a 11 et 15 s de route (camera d'essai), invite a sa 5e seconde au-dessus de 100 km/h (sa vue).
    // Ancien chemin : [Test] SuiviVoitureAncien=1 (suivi chez l'hote). (Envoi : la pose relue par Props au placement du carnet et a
    // l'image suivante etaient identiques a 140 km/h, essai du 08/11 sur My Summer Car -- rien a corriger de ce cote.)
    public static partial class Kauppa
    {
        static int cr, crShot, crFast;
        static bool crOn;
        static float crT, crNext, crLateNext, crGo, crStart, crUpAt;
        static Vector3 crDir, crGuestLast;
        static float crGuestLastAt;
        static Camera crCam;
        static RigidbodyConstraints crConstraints;
        // releve de l'hote, par seconde
        static int crN, crJumps;
        static float crSum, crMax, crFwdMin, crFwdMax, crJumpMax, crRelMin, crRelMax;
        static Vector3 crPrev;
        static int crPrevFrame = -1;

        static void CrReset()
        {
            crN = crJumps = 0; crSum = crMax = crJumpMax = 0f;
            crFwdMin = crRelMin = float.MaxValue; crFwdMax = crRelMax = float.MinValue;
        }

        public static void TestCarnetRoute(string mode, float t)
        {
            if (mode != "carnetroute" || !Active || root == null) return;
            crOn = true; crT = t;
            bool guest = Session.Active && !Session.IsHost;
            string carName = Config.Get("Test", "TestVoiture", Game.Msc ? "SATSUMA" : "SORBET");
            float kmh = Config.GetFloat("Test", "CarnetVitesse", 140f);
            string dir = System.IO.Path.Combine(Log.DataDir, "dumps");
            if (guest) { TestCarnetRouteGuest(t, carName, dir); return; }

            Rigidbody car = VehicleSync.Body(carName);
            if (car == null) return;
            if (cr < 5 && t > 32f)
            {
                Vector3 p, d;
                if (cr == 0) { if (!Kartturi.StagePoint(0, 6f, out p, out d)) return; crDir = new Vector3(d.x, 0f, d.z).normalized; car.isKinematic = true; car.transform.position = p + Vector3.up * 0.6f; cr = 1; L("essai carnetroute : " + carName + " au depart de SS1"); }
                car.isKinematic = true; car.transform.rotation = Quaternion.LookRotation(crDir, Vector3.up); car.velocity = Vector3.zero; car.angularVelocity = Vector3.zero;
            }
            if (cr == 1 && t > 40f) { cr = 2; L("essai carnetroute : hote " + VehicleSync.TestEnter(carName, false)); }
            if (cr == 2 && t > 42f) { cr = 3; L("essai carnetroute : hote au volant, automate " + VehicleSync.TestEnter(carName, true)); }
            if (cr == 3 && t > 44f)
            {
                // carnet de l'invite annonce leve, et suivi ici dans une voiture
                Esine up = null; Vector3 r0; int c0 = -1;
                foreach (KeyValuePair<string, CnSaid> kv in cnSaidBy) if (kv.Value.Up) { up = FindItem(kv.Key); if (up != null) break; }
                bool ready = up != null && up.Rb != null && Props.TestRel(up.Rb, out r0, out c0) && c0 >= 0;
                if (ready || t > 110f)
                {
                    cr = 4; crGo = t + 2f;
                    Transform ct = car.transform;
                    foreach (Avatar av in PlayerSync.Avatars)
                    {
                        Transform hb = av.HeadBone;
                        if (hb == null) continue;
                        if (crCam == null) { crCam = new GameObject("MWCoop-CameraCarnetRoute").AddComponent<Camera>(); crCam.depth = 100; crCam.nearClipPlane = 0.02f; crCam.fieldOfView = 70f; }
                        crCam.transform.position = hb.position - ct.right * (Game.Msc ? 0.6f : 0.5f) + ct.forward * 0.1f - ct.up * 0.02f;
                        crCam.transform.LookAt(hb.position - ct.up * 0.3f + ct.forward * 0.22f, ct.up);
                        crCam.transform.parent = ct;   // (fixee a la voiture : elle la suit sans retard)
                        break;
                    }
                    Rigidbody rb = VehicleSync.LocalRideBody();
                    L("essai carnetroute : hote dans " + (rb != null ? rb.name : "aucune voiture") + ", camera d'essai " + (crCam != null)
                      + (ready ? ", carnet " + up.Id + " de l'invite leve et suivi dans la voiture " + c0 : ", carnet de l'invite PAS leve ou pas suivi a 110 s"));
                }
            }
            if (cr == 4 && t > crGo)
            {
                cr = 5; crStart = t;
                car.transform.position = car.transform.position + Vector3.up * 6f;
                car.isKinematic = false; car.useGravity = false; car.detectCollisions = false;
                crConstraints = car.constraints; car.constraints = RigidbodyConstraints.FreezeRotation;   // (essai du 08/11 : la SATSUMA tournait de 90 a 150 deg en 16 s)
                car.velocity = Vector3.zero; car.angularVelocity = Vector3.zero;
                CrReset(); crLateNext = 0f;
                L("essai carnetroute : depart en ligne droite a " + kmh.ToString("F0") + " km/h" + (Config.GetInt("Test", "SuiviVoitureAncien", 0) != 0 ? " (suivi ANCIEN)" : " (suivi nouveau)"));
            }
            if (cr == 5)
            {
                // (mise en vitesse en 6 s : d'un coup a 140 km/h, le jeu tuait les deux joueurs -- « accident », essai du 08/11)
                if (t < crStart + 20f) { car.velocity = crDir * (kmh / 3.6f * Mathf.Clamp01((t - crStart) / 6f)); car.angularVelocity = Vector3.zero; }
                else { cr = 6; car.velocity = Vector3.zero; car.constraints = crConstraints; car.isKinematic = true; L("essai carnetroute : arret, cap " + Vector3.Angle(car.transform.forward, crDir).ToString("F1") + " deg du depart"); }
            }
            if (cr >= 5 && t > crStart + 11f && crShot == 0) { crShot = 1; Application.CaptureScreenshot(System.IO.Path.Combine(dir, "carnetroute-hote-a.png")); }
            if (cr >= 5 && t > crStart + 15f && crShot == 1) { crShot = 2; Application.CaptureScreenshot(System.IO.Path.Combine(dir, "carnetroute-hote-b.png")); }
        }

        static void TestCarnetRouteGuest(float t, string carName, string dir)
        {
            if (cr == 0 && t > 30f && EyeT != null) { cr = 1; SpawnItem(KCarnet, "", EyeT.position + EyeT.forward * 0.6f, Quaternion.identity); L("essai carnetroute : carnet demande"); }
            if (cr == 1 && t > 34f)
            {
                cr = 2;
                Esine k = esineet.FindLast(e => e.Kind == KCarnet); PlayMakerFSM pf = PickUpFsm();
                if (k != null && pf != null) { k.Rb.isKinematic = false; if (EyeT != null) k.Go.transform.position = EyeT.position + EyeT.forward * 0.6f; pf.FsmVariables.GetFsmGameObject("PickedObject").Value = k.Go; Game.SetState(pf, PickSetState); }
                L("essai carnetroute : carnet " + (k != null ? k.Id : "absent") + ", main " + (pf != null ? pf.ActiveStateName : "?"));
            }
            if (cr == 2 && t > 37f) { cr = 3; L("essai carnetroute : " + Seats.TestSit(carName, 0) + ", tenu " + (HeldCarnet() != null)); }
            if (cr == 3 && t > 41f) { cr = 4; crUpAt = t; cnTestUp = true; }
            if (cr < 4 || t < crUpAt + 2f || t > crUpAt + 80f) return;
            float now = Time.realtimeSinceStartup;
            if (now < crNext) return;
            crNext = now + 1f;
            Rigidbody cb = VehicleSync.Body(carName);
            Vector3 at = cb != null ? cb.transform.position : Vector3.zero;
            float kmh = crGuestLastAt > 0f && now > crGuestLastAt ? (at - crGuestLast).magnitude / (now - crGuestLastAt) * 3.6f : 0f;
            crGuestLast = at; crGuestLastAt = now;
            if (kmh > 30f) L("essai carnetroute : invite t=" + t.ToString("F0") + ", copie a " + kmh.ToString("F0") + " km/h, leve " + CarnetUp + ", assis " + Seats.Seated);
            if (kmh > 100f && ++crFast == 5) Application.CaptureScreenshot(System.IO.Path.Combine(dir, "carnetroute-invite.png"));
        }

        // Fin de l'image (Autotest.LateMeasure) : chez l'hote, le carnet de l'invite tel qu'il sera rendu, dans la voiture.
        public static void LateCarnetRoute()
        {
            if (!crOn || !Session.IsHost || cr != 5 || crT < crStart + 7f) return;   // (apres la mise en vitesse)
            Rigidbody nb = null; Vector3 rel = Vector3.zero; int relCar = -1;
            foreach (Esine e in esineet)
            {
                Vector3 r; int c;
                if (e.Kind == KCarnet && e.Rb != null && Props.TestRel(e.Rb, out r, out c) && c >= 0) { nb = e.Rb; rel = r; relCar = c; break; }
            }
            if (nb == null) { if (Time.realtimeSinceStartup >= crLateNext) { crLateNext = Time.realtimeSinceStartup + 1f; L("essai carnetroute : hote t=" + crT.ToString("F0") + " : aucun carnet suivi dans une voiture"); } return; }
            Rigidbody cb = VehicleSync.CarBody(relCar);
            if (cb == null) return;
            Transform ct = cb.transform;
            Vector3 local = Quaternion.Inverse(ct.rotation) * (nb.transform.position - ct.position);
            Vector3 e2 = local - rel;
            crN++; crSum += e2.magnitude; crMax = Mathf.Max(crMax, e2.magnitude);
            crFwdMin = Mathf.Min(crFwdMin, e2.z); crFwdMax = Mathf.Max(crFwdMax, e2.z);
            crRelMin = Mathf.Min(crRelMin, rel.z); crRelMax = Mathf.Max(crRelMax, rel.z);
            if (crPrevFrame == Time.frameCount - 1)
            {
                float j = (local - crPrev).magnitude;
                crJumpMax = Mathf.Max(crJumpMax, j);
                if (j > 0.25f) crJumps++;
            }
            crPrev = local; crPrevFrame = Time.frameCount;
            if (Time.realtimeSinceStartup >= crLateNext)
            {
                crLateNext = Time.realtimeSinceStartup + 1f;
                L("essai carnetroute : hote t=" + crT.ToString("F0") + ", voiture a " + (cb.velocity.magnitude * 3.6f).ToString("F0") + " km/h, " + crN + " images : carnet affiche / pose recue, ecart moyen "
                  + (crSum / Mathf.Max(crN, 1)).ToString("F3") + " m, max " + crMax.ToString("F3") + " m, avant/arriere " + crFwdMin.ToString("F3") + " .. " + crFwdMax.ToString("F3")
                  + " m ; sauts > 0,25 m : " + crJumps + " (plus grand " + crJumpMax.ToString("F3") + " m) ; pose recue avant/arriere " + crRelMin.ToString("F3") + " .. " + crRelMax.ToString("F3") + " m");
                CrReset();
            }
        }
    }
}
