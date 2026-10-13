using HutongGames.PlayMaker;
using UnityEngine;

namespace MWCoop
{
    // Vitre du passager avant qui s'ouvre (Menor, 12/10 : « la vitre passager de la SORBET et du taxi, comme celle du
    // conducteur »). Le jeu n'a de manivelle que cote conducteur : Doors/DoorFront(leftx)/FrontL/opener, automate 'Knob'
    // (molette : State 1 descend la vitre -- iTween vers une pose de WindowPivot/GlassPivot --, State 2 la remonte, et tourne
    // la manivelle). Cote passager, la vitre a son pivot (FrontR/WindowPivot/GlassPivot) et une manivelle dessinee
    // (« opener 3 ») sans automate. Ici, 3 s apres le chargement (avant que Jobs n'accroche les commandes : la copie est alors
    // relevee et rejouee comme les autres, sous son propre chemin), la manivelle du conducteur est copiee a la place de celle
    // du passager, sa manivelle a elle tournant. Chez tous de la meme facon. Portieres arriere : pas de pivot de vitre dans le
    // jeu (rien a faire bouger).
    // La vitre : les iTween de l'automate visent des poses ABSOLUES (rotation locale 14,0,0 ; position 0,1 / -0,1 / -0,4),
    // faites pour le pivot de gauche. Posees telles quelles sur le pivot de droite, la vitre basculait dans l'habitacle
    // (Menor, 12/10, video : « out of place », faces vues de dos). L'automate copie pilote donc un TEMOIN (objet vide, meme
    // repere que la vitre de gauche au repos) et la vitre de droite recoit, a chaque image, le mouvement du temoin en miroir
    // (plan de symetrie de la voiture, entre les deux vitres au repos), etalonne portieres fermees.
    public static class WindowCranks
    {
        static float at = -1;
        static int logs;

        class Mirror
        {
            public Transform Proxy, GlassL, GlassR, PivotL, PivotR, Car, DoorL, DoorR;
            public bool Ready; public Matrix4x4 A, B; public float WaitLog, DownMag;   // vitre de droite (locale) = A * temoin (local) * B
            public Vector3 LastP = new Vector3(1e9f, 0, 0); public Quaternion LastQ;
        }
        static readonly System.Collections.Generic.List<Mirror> mirrors = new System.Collections.Generic.List<Mirror>();

        public static void OnLevelLoaded() { at = PlayerSync.InGame ? Time.realtimeSinceStartup + 3f : -1; mirrors.Clear(); }

        public static void Update()
        {
            Drive();
            if (at < 0 || Time.realtimeSinceStartup < at) return;
            at = -1;
            int n = 0;
            foreach (PlayMakerFSM f in Object.FindObjectsOfType<PlayMakerFSM>())
            {
                if (f == null || f.FsmName != "Knob" || f.gameObject.name != "opener" || f.transform.parent == null || f.transform.parent.name != "FrontL") continue;
                Transform doorL = f.transform.parent.parent;                     // DoorFront(leftx)
                Transform doors = doorL != null ? doorL.parent : null;
                Transform frontR = doors != null ? doors.Find("DoorFront(right)/FrontR") : null;
                Transform glassR = frontR != null ? frontR.Find("WindowPivot/GlassPivot") : null;
                if (glassR == null || frontR.Find("opener MWCoop") != null) continue;
                bool hasKnob = false;
                foreach (PlayMakerFSM k in frontR.GetComponentsInChildren<PlayMakerFSM>(true)) hasKnob |= k.FsmName == "Knob";
                if (hasKnob) continue;
                if (Make(f, frontR, glassR)) n++;
            }
            if (n > 0) Log.Info("vitres : manivelle passager ajoutee a " + n + " voiture(s)");
        }

        static bool Make(PlayMakerFSM knob, Transform frontR, Transform glassR)
        {
            try
            {
                Transform src = knob.transform, frontL = src.parent;
                Transform drawn = frontR.Find("opener 3") ?? (frontR.parent != null ? frontR.parent.Find("opener 3") : null);   // (SORBET : sous la portiere)
                var c = (GameObject)Object.Instantiate(src.gameObject);
                c.name = "opener MWCoop";
                c.transform.SetParent(frontR, false);
                // A la place de la manivelle dessinee (cachee), sinon en miroir de celle du conducteur.
                if (drawn != null) { c.transform.position = drawn.position; c.transform.rotation = drawn.rotation; drawn.gameObject.SetActive(false); }
                else { Vector3 lp = src.localPosition; c.transform.localPosition = new Vector3(-lp.x, lp.y, lp.z); c.transform.localRotation = src.localRotation; }
                Transform handle = c.transform.Find("Pivot");
                Transform glassL = null;
                var proxy = new GameObject("vitre MWCoop").transform;   // (temoin : pose locale de la vitre de gauche)
                proxy.SetParent(c.transform, false);
                foreach (PlayMakerFSM f in c.GetComponents<PlayMakerFSM>())
                {
                    if (f.FsmName != "Knob") continue;
                    FsmGameObject w = f.FsmVariables.FindFsmGameObject("Windows"), h = f.FsmVariables.FindFsmGameObject("Handle");
                    if (w != null) { if (w.Value != null) glassL = w.Value.transform; w.Value = proxy.gameObject; }
                    if (w != null && Config.GetInt("Test", "VitreAncienne", 0) != 0) { w.Value = glassR.gameObject; glassL = null; }   // (essai : l'ancien defaut)
                    if (h != null && handle != null) h.Value = handle.gameObject;
                }
                if (glassL != null && glassL.parent != null && glassR.parent != null)
                    mirrors.Add(new Mirror { Proxy = proxy, GlassL = glassL, GlassR = glassR, PivotL = glassL.parent, PivotR = glassR.parent,
                        Car = frontL.parent != null && frontL.parent.parent != null ? frontL.parent.parent.parent : frontR.root,
                        DoorL = frontL.parent, DoorR = frontR.parent, DownMag = DownTarget(c) });
                if (logs++ < 4) Log.Info("vitres : manivelle passager de " + frontR.root.name + " (" + (drawn != null ? "a la place de opener 3" : "en miroir") + ")");
                return true;
            }
            catch (System.Exception e) { Log.Warn("vitres : manivelle passager : " + e.Message); return false; }
        }

        // Vitres de droite : etalonnage (une fois, les deux portieres fermees), puis le mouvement du temoin en miroir.
        static void Drive()
        {
            for (int i = mirrors.Count - 1; i >= 0; i--)
            {
                Mirror m = mirrors[i];
                if (m.Proxy == null || m.GlassR == null || m.GlassL == null || m.Car == null) { mirrors.RemoveAt(i); continue; }
                if (!m.Ready)
                {
                    if (!Closed(m.DoorL) || !Closed(m.DoorR))
                    {
                        if (Time.realtimeSinceStartup > m.WaitLog) { m.WaitLog = Time.realtimeSinceStartup + 20f; Log.Info("vitres : " + m.Car.name + " attend ses portieres fermees (charnieres " + Angle(m.DoorL) + " / " + Angle(m.DoorR) + ")"); }
                        continue;
                    }
                    Calibrate(m);
                }
                Vector3 p = m.Proxy.localPosition; Quaternion q = m.Proxy.localRotation;
                if (p == m.LastP && q == m.LastQ) continue;
                m.LastP = p; m.LastQ = q;
                Matrix4x4 t = m.A * Matrix4x4.TRS(p, q, Vector3.one) * m.B;
                Vector3 y = t.GetColumn(1), z = t.GetColumn(2);
                // Le jeu ne baisse sa vitre qu'aux deux tiers : le haut depasse de la portiere (a gauche, verre clair, a peine
                // visible). La vitre de droite, sans grattoir de l'interieur, est souvent givree : ce bord blanc en travers de
                // la fenetre (Menor, 12/10, « out of place »). Elle descend donc 15 cm de plus, au prorata de la course.
                float f = m.DownMag > 0.01f ? Mathf.Clamp01(p.magnitude / m.DownMag) : 0f;
                Vector3 extra = m.PivotR.InverseTransformVector(-m.Car.up * (ExtraDown * f));
                m.GlassR.localPosition = (Vector3)t.GetColumn(3) + extra;
                if (y.sqrMagnitude > 1e-8f && z.sqrMagnitude > 1e-8f) m.GlassR.localRotation = Quaternion.LookRotation(z, y);
            }
        }

        static string Angle(Transform door)
        {
            HingeJoint h = door != null ? door.GetComponent<HingeJoint>() : null;
            return h == null ? "sans" : h.angle.ToString("F1") + " (" + h.limits.min.ToString("F0") + ".." + h.limits.max.ToString("F0") + ")";
        }

        const float ExtraDown = 0.15f;

        // Course de la vitre : position visee par l'iTween de « State 1 » (0,1 / -0,1 / -0,4 sur la SORBET).
        static float DownTarget(GameObject knob)
        {
            foreach (PlayMakerFSM f in knob.GetComponents<PlayMakerFSM>())
            {
                if (f.FsmName != "Knob") continue;
                foreach (FsmState s in f.FsmStates)
                {
                    if (s.Name != "State 1") continue;
                    foreach (FsmStateAction a in s.Actions)
                    {
                        if (a == null || a.GetType().Name != "iTweenMoveUpdate") continue;
                        var fi = a.GetType().GetField("vectorPosition");
                        FsmVector3 v = fi != null ? fi.GetValue(a) as FsmVector3 : null;
                        if (v != null) return v.Value.magnitude;
                    }
                }
            }
            return 0f;
        }

        static bool Closed(Transform door)
        {
            HingeJoint h = door != null ? door.GetComponent<HingeJoint>() : null;
            // (butees 0..0 : portiere fermee, le jeu les pose ainsi en la refermant ; l'angle de la charniere, lui, n'est pas
            // fiable -- 120 deg sur une SORBET fermee)
            return h == null || h.limits.min == 0f && h.limits.max == 0f;
        }

        // Vitre de gauche au repos = pose locale nulle (l'automate y remonte : State 2 vise 0,0,0). Miroir R : plan x = x0 de la
        // voiture, a mi-chemin des deux vitres. Vitre de droite au repos (TR0) : telle qu'au chargement.
        //   monde(droite) = R * pivotG * T * echelleG * S, et a T = identite c'est pivotD * TR0  =>  S = (R pivotG echelleG)^-1 pivotD TR0
        //   locale(droite) = pivotD^-1 * R * pivotG * T * echelleG * S = A * T * B
        static void Calibrate(Mirror m)
        {
            Matrix4x4 wl = m.PivotL.localToWorldMatrix, wr = m.PivotR.localToWorldMatrix;
            Vector3 gl = m.Car.InverseTransformPoint(m.PivotL.position), gr = m.Car.InverseTransformPoint(m.GlassR.position);
            float x0 = (gl.x + gr.x) * 0.5f;
            Matrix4x4 rc = Matrix4x4.TRS(new Vector3(2f * x0, 0, 0), Quaternion.identity, new Vector3(-1f, 1f, 1f));   // (x -> 2 x0 - x)
            Matrix4x4 r = m.Car.localToWorldMatrix * rc * m.Car.worldToLocalMatrix;
            Matrix4x4 tr0 = Matrix4x4.TRS(m.GlassR.localPosition, m.GlassR.localRotation, m.GlassR.localScale);
            Matrix4x4 scl = Matrix4x4.Scale(m.GlassL.localScale);
            Matrix4x4 s = (r * wl * scl).inverse * wr * tr0;
            m.A = wr.inverse * r * wl;
            m.B = scl * s;
            m.Ready = true;
            if (m.DownMag < 0.01f) m.DownMag = DownTarget(m.Proxy.parent.gameObject);   // (actions de l'automate pas encore chargees a la copie)
            if (logs++ < 8) Log.Info("vitres : vitre passager de " + m.Car.name + " etalonnee (course " + m.DownMag.ToString("F2") + " m, plan x " + x0.ToString("F3") + ", repos " + m.GlassR.localPosition.ToString("F3") + " " + m.GlassR.localEulerAngles.ToString("F0") + ")");
        }

        // [Test] Autotest=vitre (solo) : 25 s apres le chargement, la manivelle passager de la SORBET tourne 3 s (State 1, comme
        // la molette) ; la vitre de droite et celle de gauche (meme commande sur la manivelle du conducteur) sont notees, en
        // repere de la voiture : attendu, le meme mouvement en miroir.
        // (place passager, x > 0 ; s = -1 : place conducteur, vers la vitre de gauche)
        static void Look(float s)
        {
            Vector3 eye = car.TransformPoint(new Vector3(0.25f * s, 0.8f, 0.05f)), at = car.TransformPoint(new Vector3(0.85f * s, 0.45f, 0.35f));
            tcam.transform.position = eye; tcam.transform.rotation = Quaternion.LookRotation(at - eye, car.up);
        }

        // (boite des maillages sous t, dans le repere de la voiture : coins des limites locales de chaque maillage)
        static string Box(Transform t)
        {
            Vector3 mn = Vector3.one * 1e9f, mx = -mn;
            foreach (MeshFilter mf in t.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null) continue;
                Bounds b = mf.sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 c = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) != 0 ? 1 : -1, (i & 2) != 0 ? 1 : -1, (i & 4) != 0 ? 1 : -1));
                    Vector3 p = car.InverseTransformPoint(mf.transform.TransformPoint(c));
                    mn = Vector3.Min(mn, p); mx = Vector3.Max(mx, p);
                }
                if (t.name.StartsWith("Front")) break;   // (portiere : son propre maillage seulement, le premier)
            }
            return mn.ToString("F2") + "-" + mx.ToString("F2");
        }

        static float MaxDiff(Matrix4x4 a, Matrix4x4 b) { float e = 0f; for (int i = 0; i < 3; i++) for (int j = 0; j < 4; j++) e = Mathf.Max(e, Mathf.Abs(a[i, j] - b[i, j])); return e; }
        static int tStep; static Vector3 l0, r0; static Quaternion ql0, qr0; static Matrix4x4 ml0, mr0; static Transform gl, gr, car; static PlayMakerFSM kl, kr; static Camera tcam;
        public static void Test(string mode, float t)
        {
            if (mode != "vitre") return;
            if (tStep == 0 && t > 25f)
            {
                GameObject s = GameObject.Find("SORBET(190-200psi)");
                if (s == null) return;
                car = s.transform;
                gl = car.Find("Doors/DoorFront(leftx)/FrontL/WindowPivot/GlassPivot"); gr = car.Find("Doors/DoorFront(right)/FrontR/WindowPivot/GlassPivot");
                Transform ol = car.Find("Doors/DoorFront(leftx)/FrontL/opener"), or = car.Find("Doors/DoorFront(right)/FrontR/opener MWCoop");
                if (gl == null || gr == null || ol == null || or == null) { tStep = 9; Log.Info("autotest : vitre, pieces introuvables (manivelle copiee " + (or != null) + ")"); return; }
                l0 = car.InverseTransformPoint(gl.position); r0 = car.InverseTransformPoint(gr.position);
                ql0 = Quaternion.Inverse(car.rotation) * gl.rotation; qr0 = Quaternion.Inverse(car.rotation) * gr.rotation;
                ml0 = car.worldToLocalMatrix * gl.localToWorldMatrix; mr0 = car.worldToLocalMatrix * gr.localToWorldMatrix;
                // (camera d'essai dans l'habitacle, cote conducteur, tournee vers la vitre passager)
                tcam = new GameObject("MWCoop-CameraEssai").AddComponent<Camera>();
                tcam.depth = 100; tcam.nearClipPlane = 0.02f; tcam.fieldOfView = 75f;
                Look(1f);
                Autotest.CaptureSoon("vitre-haut", 0.5f);
                Log.Info("autotest : vitre, repos : verre gauche " + Box(gl) + " droite " + Box(gr) + " ; portiere gauche " + Box(gl.parent.parent) + " droite " + Box(gr.parent.parent));
                kl = Game.FsmOn(ol.gameObject, "Knob"); kr = Game.FsmOn(or.gameObject, "Knob");
                tStep = 1;
                Log.Info("autotest : vitre, les deux manivelles tournent");
            }
            if (tStep == 1 && t < 31f) foreach (PlayMakerFSM f in new[] { kl, kr }) if (f != null && f.ActiveStateName != "State 1") Game.SetState(f, "State 1");   // (molette tenue)
            if (tStep == 1 && t > 31f)
            {
                tStep = 2;
                Vector3 dl = car.InverseTransformPoint(gl.position) - l0, dr = car.InverseTransformPoint(gr.position) - r0;
                Log.Info("autotest : vitre, gauche " + dl.ToString("F3") + " ; droite " + dr.ToString("F3") + " (repere voiture ; attendu : x oppose, y et z egaux) ; suivies par Jobs : gauche " + Jobs.Tracks(kl) + ", droite " + Jobs.Tracks(kr));
                // Orientation : rotation de la vitre de gauche depuis le repos (repere voiture), passee au miroir du plan x,
                // contre celle de la droite.
                Quaternion dql = (Quaternion.Inverse(car.rotation) * gl.rotation) * Quaternion.Inverse(ql0);
                Quaternion dqr = (Quaternion.Inverse(car.rotation) * gr.rotation) * Quaternion.Inverse(qr0);
                Quaternion mir = new Quaternion(dql.x, -dql.y, -dql.z, dql.w);
                Log.Info("autotest : vitre, rotation gauche " + Quaternion.Angle(Quaternion.identity, dql).ToString("F1") + " deg, droite " + Quaternion.Angle(Quaternion.identity, dqr).ToString("F1") + " deg, ecart au miroir " + Quaternion.Angle(mir, dqr).ToString("F1") + " deg (attendu ~0)");
                float al, ar; Vector3 axl, axr;
                dql.ToAngleAxis(out al, out axl); dqr.ToAngleAxis(out ar, out axr);
                Renderer rl = gl.GetComponentInChildren<Renderer>(), rr = gr.GetComponentInChildren<Renderer>();
                Log.Info("autotest : vitre, axes gauche " + axl.ToString("F2") + " " + al.ToString("F1") + " ; droite " + axr.ToString("F2") + " " + ar.ToString("F1")
                    + " ; centre du verre gauche " + (rl != null ? car.InverseTransformPoint(rl.bounds.center).ToString("F3") : "?") + " droite " + (rr != null ? car.InverseTransformPoint(rr.bounds.center).ToString("F3") : "?")
                    + " ; echelles pivot " + gl.parent.lossyScale.ToString("F2") + " / " + gr.parent.lossyScale.ToString("F2") + " ; verre " + (rl != null ? rl.transform.lossyScale.ToString("F2") + " " + rl.name : "") + " / " + (rr != null ? rr.transform.lossyScale.ToString("F2") + " " + rr.name : ""));
                // Mesure vraie (matrices completes : transform.rotation ignore la symetrie du pivot de droite, echelle -1) :
                // mouvement de la droite contre le miroir (plan x de la voiture) de celui de la gauche.
                Matrix4x4 dml = car.worldToLocalMatrix * gl.localToWorldMatrix * ml0.inverse, dmr = car.worldToLocalMatrix * gr.localToWorldMatrix * mr0.inverse;
                Matrix4x4 mx = Matrix4x4.Scale(new Vector3(-1f, 1f, 1f)), want = mx * dml * mx;
                float err = 0f; for (int a = 0; a < 3; a++) for (int b = 0; b < 4; b++) err = Mathf.Max(err, Mathf.Abs(want[a, b] - dmr[a, b]));
                Log.Info("autotest : vitre, ecart au miroir (matrices) " + err.ToString("F3") + " (attendu ~0) ; meme sens que la gauche (ancien defaut) " + MaxDiff(dml, dmr).ToString("F3"));
                Log.Info("autotest : vitre, bas : verre gauche " + Box(gl) + " droite " + Box(gr));
                foreach (Transform x in new[] { gl, gr }) foreach (MeshFilter mf in x.GetComponentsInChildren<MeshFilter>(true))
                    Log.Info("autotest : vitre, maillage " + mf.name + " sous " + x.parent.parent.name + " : local " + mf.transform.localPosition.ToString("F3") + " " + mf.transform.localEulerAngles.ToString("F1") + " x" + mf.transform.localScale.ToString("F2") + ", sommets " + (mf.sharedMesh != null ? mf.sharedMesh.vertexCount + " limites " + mf.sharedMesh.bounds.ToString("F3") : "?"));
                Autotest.CaptureSoon("vitre-bas", 0.05f);
            }
            if (tStep == 2 && t > 32f) { tStep = 3; Look(-1f); Autotest.CaptureSoon("vitre-bas-gauche", 0.3f); }
            if (tStep == 3 && t > 34f)
            {
                tStep = 4;
                if (tcam != null) Object.Destroy(tcam.gameObject);
            }
        }
    }
}
