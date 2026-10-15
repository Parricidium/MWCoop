using System.Collections.Generic;
using HutongGames.PlayMaker;
using MWCoop.Net;
using UnityEngine;

namespace MWCoop
{
    // Vitre du passager avant qui s'ouvre (Menor, 12/10 : « la vitre passager de la SORBET et du taxi, comme celle du
    // conducteur »). Le jeu n'a de manivelle que cote conducteur : Doors/DoorFront(leftx)/FrontL/opener, automate 'Knob'
    // (molette : State 1 descend la vitre -- iTween vers une pose de WindowPivot/GlassPivot --, State 2 la remonte, et tourne
    // la manivelle). Cote passager, la vitre a son pivot (FrontR/WindowPivot/GlassPivot) et une manivelle dessinee
    // (« opener 3 ») sans automate. Ici, 3 s apres le chargement (avant que Jobs n'accroche les commandes : la copie est alors
    // relevee et rejouee comme les autres, sous son propre chemin), la manivelle du conducteur est copiee a la place de celle
    // du passager, sa manivelle a elle tournant. Chez tous de la meme facon. Portieres arriere (Menor, meme liste : « et les
    // deux vitres arriere ») : la SORBET y a aussi un pivot de vitre et une manivelle dessinee (opener 1 / opener 2) -- meme
    // copie, en miroir a droite, simplement deplacee a gauche (meme cote que le conducteur).
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
            public bool Ready, Flip = true; public Matrix4x4 A, B; public float WaitLog, DownMag;   // vitre de droite (locale) = A * temoin (local) * B
            public bool Straight; public Vector3 RestP, RestS; public Quaternion RestQ; public int UpAxis = -1; public float Bottom;   // vitre arriere : raccourcie en descendant (axe vertical local, bas sous l'origine)
            public Vector3 LastP = new Vector3(1e9f, 0, 0); public Quaternion LastQ;
        }
        static readonly System.Collections.Generic.List<Mirror> mirrors = new System.Collections.Generic.List<Mirror>();
        class Follow { public Transform Src, Dst; public Quaternion SrcRest, DstRest; }
        static readonly System.Collections.Generic.List<Follow> followers = new System.Collections.Generic.List<Follow>();

        public static void OnLevelLoaded() { at = PlayerSync.InGame ? Time.realtimeSinceStartup + 3f : -1; mirrors.Clear(); followers.Clear(); wins.Clear(); nextWinScan = 0f; }

        public static void Update()
        {
            Drive();
            TickWins();
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
            // Portieres arriere : chaque DoorRear* avec un pivot de vitre et une manivelle sans automate.
            int nr = 0;
            foreach (PlayMakerFSM f in Object.FindObjectsOfType<PlayMakerFSM>())
            {
                if (f == null || f.FsmName != "Knob" || f.gameObject.name != "opener" || f.transform.parent == null || f.transform.parent.name != "FrontL") continue;
                Transform doors = f.transform.parent.parent != null ? f.transform.parent.parent.parent : null;
                if (doors == null) continue;
                foreach (Transform door in doors)
                {
                    if (!door.name.StartsWith("DoorRear")) continue;
                    Transform glass = null, drawn = null;
                    bool knob = false;
                    foreach (Transform x in door.GetComponentsInChildren<Transform>(true))
                    {
                        if (glass == null && x.name == "GlassPivot") glass = x;
                        if (drawn == null && x.name.StartsWith("opener") && x.name != "opener MWCoop") drawn = x;
                        if (x.name == "opener MWCoop") knob = true;
                        foreach (PlayMakerFSM k in x.GetComponents<PlayMakerFSM>()) knob |= k.FsmName == "Knob";
                    }
                    if (glass == null || drawn == null || knob) continue;
                    Transform side = glass.parent != null && glass.parent.parent != null ? glass.parent.parent : door;
                    Transform car = doors.parent;
                    bool flip = car != null && car.InverseTransformPoint(glass.position).x * car.InverseTransformPoint(f.transform.position).x < 0f;
                    if (Make(f, side, glass, drawn, flip)) nr++;
                }
            }
            if (n > 0) Log.Info("vitres : manivelle passager ajoutee a " + n + " voiture(s)");
            if (nr > 0) Log.Info("vitres : " + nr + " manivelle(s) arriere ajoutee(s)");
            nextWinScan = 0f;   // (vitres a suivre, temoins compris)
        }

        static bool Make(PlayMakerFSM knob, Transform frontR, Transform glassR) { return Make(knob, frontR, glassR, null, true); }
        static bool Make(PlayMakerFSM knob, Transform frontR, Transform glassR, Transform drawnRear, bool flip)
        {
            try
            {
                Transform src = knob.transform, frontL = src.parent;
                Transform drawn = drawnRear ?? frontR.Find("opener 3") ?? (frontR.parent != null ? frontR.parent.Find("opener 3") : null);   // (SORBET : sous la portiere)
                var c = (GameObject)Object.Instantiate(src.gameObject);
                c.name = "opener MWCoop";
                c.transform.SetParent(frontR, false);
                // A la place de la manivelle dessinee, sinon en miroir de celle du conducteur. La manivelle dessinee (le modele du
                // jeu, tourne vers l'habitacle) reste visible et la copie invisible (clic et automate seulement) : le modele du
                // conducteur pose de ce cote montrait son envers, tourne vers la portiere (retour de JD, 13/10). La manivelle
                // dessinee tourne comme le pivot de la copie (Follow).
                if (drawn != null)
                {
                    c.transform.position = drawn.position; c.transform.rotation = drawn.rotation;
                    foreach (Renderer rr in c.GetComponentsInChildren<Renderer>(true)) rr.enabled = false;
                    Transform cp = c.transform.Find("Pivot") ?? c.transform, dp = drawn.Find("Pivot") ?? drawn;
                    followers.Add(new Follow { Src = cp, Dst = dp, SrcRest = cp.localRotation, DstRest = dp.localRotation });
                }
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
                        DoorL = frontL.parent, DoorR = drawnRear != null ? DoorOf(glassR) : frontR.parent, DownMag = DownTarget(c), Flip = flip, Straight = drawnRear != null });
                if (logs++ < 8) Log.Info("vitres : manivelle " + (drawnRear != null ? "arriere (" + frontR.name + (flip ? ", miroir" : "") + ")" : "passager") + " de " + frontR.root.name + " (" + (drawn != null ? "a la place de " + drawn.name : "en miroir") + ")");
                return true;
            }
            catch (System.Exception e) { Log.Warn("vitres : manivelle passager : " + e.Message); return false; }
        }

        // Vitres de droite : etalonnage (une fois, les deux portieres fermees), puis le mouvement du temoin en miroir.
        static void Drive()
        {
            foreach (Follow fo in followers) if (fo.Src != null && fo.Dst != null) fo.Dst.localRotation = fo.DstRest * (Quaternion.Inverse(fo.SrcRest) * fo.Src.localRotation);
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
                // Vitre arriere : le trajet de la vitre avant est en biais (sa coulisse suit le montant avant de la portiere) ;
                // repris tel quel, la vitre arriere baissee glissait vers l'avant, hors de sa portiere -- plaque givree en travers
                // de l'ouverture (videos de Menor, 13/10). Elle descend donc tout droit (vers le bas de la voiture), de la meme course.
                if (m.Straight)
                {
                    // La portiere arriere est pleine (pas de creux entre panneau et tole) : descendue, la vitre se voyait a travers
                    // le garnissage, inclinee elle sortait par la tole ou le panneau (Menor 14/10, JD 15/10). Elle est donc
                    // raccourcie en descendant (echelle sur son axe vertical, bas tenu sur la ceinture de caisse) : seule sa partie
                    // dans l'ouverture existe, rien dans la portiere, quel que soit l'angle de la portiere (tout en repere local).
                    if (m.UpAxis < 0)
                    {
                        Vector3 ul = m.GlassR.InverseTransformDirection(m.Car.up);
                        m.UpAxis = Mathf.Abs(ul.x) >= Mathf.Abs(ul.y) && Mathf.Abs(ul.x) >= Mathf.Abs(ul.z) ? 0 : Mathf.Abs(ul.y) >= Mathf.Abs(ul.z) ? 1 : 2;
                        Renderer rr = m.GlassR.GetComponentInChildren<Renderer>();
                        m.Bottom = 0.08f;
                        if (rr != null)
                        {
                            Bounds bb = rr.bounds; float lo = float.MaxValue;
                            for (int k = 0; k < 8; k++)
                            {
                                Vector3 c = new Vector3((k & 1) != 0 ? bb.max.x : bb.min.x, (k & 2) != 0 ? bb.max.y : bb.min.y, (k & 4) != 0 ? bb.max.z : bb.min.z);
                                lo = Mathf.Min(lo, Vector3.Dot(c - m.GlassR.position, m.Car.up));
                            }
                            m.Bottom = Mathf.Max(0f, -lo);
                        }
                    }
                    float s = 1f - 0.97f * f;
                    Vector3 sc = m.RestS; sc[m.UpAxis] = m.RestS[m.UpAxis] * s;
                    m.GlassR.localScale = sc;
                    m.GlassR.localRotation = m.RestQ;
                    m.GlassR.localPosition = m.RestP + new Vector3(0f, 0f, -m.Bottom * (1f - s));   // (repere du pivot : Z = haut)
                    continue;
                }
                Vector3 extra = m.PivotR.InverseTransformVector(-m.Car.up * (ExtraDown * f));
                m.GlassR.localPosition = (Vector3)t.GetColumn(3) + extra;
                if (y.sqrMagnitude > 1e-8f && z.sqrMagnitude > 1e-8f) m.GlassR.localRotation = Quaternion.LookRotation(z, y);
            }
        }


        // Portiere (charniere) qui porte cette vitre.
        static Transform DoorOf(Transform t) { for (; t != null; t = t.parent) if (t.GetComponent<HingeJoint>() != null) return t; return null; }

        static string Angle(Transform door)
        {
            HingeJoint h = door != null ? door.GetComponent<HingeJoint>() : null;
            return h == null ? "sans" : h.angle.ToString("F1") + " (" + h.limits.min.ToString("F0") + ".." + h.limits.max.ToString("F0") + ")";
        }

        const float ExtraDown = 0.15f;

        // ---------------------------------------------------------------- pose des vitres
        // La manivelle est rejouee chez les autres (Jobs), mais leur automate repart aussitot ("State 1" -> "Get scroll" ->
        // "Mouse off 2" : la souris n'y est pas) et l'iTween ne bouge la vitre que l'image ou l'etat est actif : chez l'autre
        // la vitre bougeait a peine (journaux de .nolly, 12/10 : « -> Mouse off 2 (voulu State 1) »). Ici, celui qui tourne la
        // manivelle (il la vise, a moins de 1,3 m) envoie la pose locale de la vitre 10 fois par seconde tant qu'elle bouge, puis
        // une derniere fois (fiable) ; les autres la posent. L'hote redit toutes les 15 s la pose de chaque vitre (arrivants).
        // Canal des evenements (Events), cle « vitre:<chemin de la manivelle> ». Toutes les voitures dont la manivelle ('Knob',
        // variable Windows) vise une vitre, et le temoin de la vitre passager (Drive la pose en miroir).
        class Win { public string Key; public PlayMakerFSM Knob; public Transform Glass; public float AimAt = -9f, LastSend, RemoteAt = -9f; public Vector3 SentPos; public Quaternion SentRot; public bool Moving; }
        static readonly List<Win> wins = new List<Win>();
        static float nextWinScan, nextWinFull;
        public const string WinPrefix = "vitre:";

        static void ScanWins()
        {
            foreach (PlayMakerFSM f in Object.FindObjectsOfType<PlayMakerFSM>())
            {
                if (f == null || f.FsmName != "Knob") continue;
                FsmGameObject w = f.FsmVariables.FindFsmGameObject("Windows");
                if (w == null || w.Value == null) continue;
                string gn = w.Value.name;
                if (gn != "vitre MWCoop" && gn.IndexOf("lass") < 0) continue;   // (pas les molettes du chauffage : Windows = CarTemp...)
                if (VehicleSync.CarRoot(f.transform) == null) continue;
                bool known = false;
                foreach (Win x in wins) if (x.Knob == f) { known = true; break; }
                if (known) continue;
                wins.Add(new Win { Key = WinPrefix + Recon.Path(f.transform), Knob = f, Glass = w.Value.transform });
            }
        }

        static void TickWins()
        {
            if (!Session.Active || !PlayerSync.InGame) return;
            float now = Time.realtimeSinceStartup;
            if (now >= nextWinScan) { nextWinScan = now + 30f; int before = wins.Count; ScanWins(); if (wins.Count != before && logs++ < 12) Log.Info("vitres : " + wins.Count + " vitres suivies (pose de celui qui tourne la manivelle)"); }
            if (wins.Count == 0) return;
            Transform cam = PlayerSync.LocalCamera;
            for (int i = wins.Count - 1; i >= 0; i--)
            {
                Win w = wins[i];
                if (w.Knob == null || w.Glass == null) { wins.RemoveAt(i); continue; }
                if (cam != null)
                {
                    Vector3 d = w.Knob.transform.position - cam.position;
                    if (d.sqrMagnitude < 1.3f * 1.3f && Vector3.Angle(cam.forward, d) < 12f) w.AimAt = now;
                }
                bool mine = now - w.AimAt < 0.6f && now - w.RemoteAt > 0.5f;
                if (!mine) { w.Moving = false; continue; }
                Vector3 p = w.Glass.localPosition; Quaternion q = w.Glass.localRotation;
                bool changed = (p - w.SentPos).sqrMagnitude > 1e-6f || Quaternion.Angle(q, w.SentRot) > 0.2f;
                if (changed && now - w.LastSend >= 0.1f) { SendWin(w, false); w.Moving = true; }
                else if (!changed && w.Moving && now - w.LastSend > 0.4f) { SendWin(w, true); w.Moving = false; }
            }
            if (Session.IsHost && Session.RemoteCount > 0 && now >= nextWinFull)
            {
                nextWinFull = now + 15f;
                foreach (Win w in wins) if (!w.Moving) SendWin(w, true);
            }
        }

        static void SendWin(Win w, bool reliable)
        {
            w.SentPos = w.Glass.localPosition; w.SentRot = w.Glass.localRotation; w.LastSend = Time.realtimeSinceStartup;
            Session.SendAll(new NetWriter(Msg.Event).Str(w.Key).Vec(w.SentPos).Quat(w.SentRot), reliable);
        }

        public static void OnWinMessage(Peer from, string key, NetReader r, bool reliable)
        {
            Vector3 p = r.Vec(); Quaternion q = r.Quat();
            if (Session.IsHost) Session.Broadcast(new NetWriter(Msg.Event).Str(key).Vec(p).Quat(q), reliable, from.Id);
            Win w = null;
            foreach (Win x in wins) if (x.Key == key) { w = x; break; }
            if (w == null || w.Glass == null) return;
            float now = Time.realtimeSinceStartup;
            if (now - w.AimAt < 1f) return;   // (on la tourne ici en ce moment : la notre fait foi)
            w.RemoteAt = now;
            w.Glass.localPosition = p; w.Glass.localRotation = q;
            w.SentPos = p; w.SentRot = q;
        }

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
            Matrix4x4 rc = m.Flip ? Matrix4x4.TRS(new Vector3(2f * x0, 0, 0), Quaternion.identity, new Vector3(-1f, 1f, 1f)) : Matrix4x4.identity;   // (x -> 2 x0 - x ; meme cote : le mouvement tel quel)
            Matrix4x4 r = m.Car.localToWorldMatrix * rc * m.Car.worldToLocalMatrix;
            Matrix4x4 tr0 = Matrix4x4.TRS(m.GlassR.localPosition, m.GlassR.localRotation, m.GlassR.localScale);
            m.RestP = m.GlassR.localPosition; m.RestQ = m.GlassR.localRotation; m.RestS = m.GlassR.localScale;
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
            float dz = Config.Get("Test", "VitreArriere", "").Length > 0 ? -0.75f : 0f;   // (vitre arriere : meme vue, reculee)
            Vector3 eye = car.TransformPoint(new Vector3(0.25f * s, 0.8f, 0.05f + dz)), at = car.TransformPoint(new Vector3(0.85f * s, 0.45f, 0.35f + dz));
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

        // [Test] Autotest=vitreporte (solo) : la portiere [Test] VitrePorte (DoorRear(leftx) par defaut) de la SORBET vue de
        // dehors, fermee a 25 s puis ouverte a 28 s (photos vp-ferme / vp-ouverte), et la vitre de cette portiere dans le repere
        // de la portiere aux deux moments (attendu : la meme -- elle suit la portiere).
        static int vpStep; static Transform vpDoor, vpGlass; static Vector3 vpPos; static Quaternion vpRot;
        static void TestDoorGlass(float t)
        {
            string door = Config.Get("Test", "VitrePorte", "DoorRear(leftx)");
            if (vpStep == 0 && t > 25f)
            {
                GameObject s = GameObject.Find("SORBET(190-200psi)");
                if (s == null) return;
                car = s.transform;
                vpStep = 1;
                Transform d = car.Find("Doors/" + door);
                if (d != null) foreach (Transform x in d.GetComponentsInChildren<Transform>(true)) if (vpGlass == null && x.name == "GlassPivot") vpGlass = x;
                vpDoor = vpGlass != null ? DoorOf(vpGlass) : null;
                if (vpGlass == null || vpDoor == null) { Log.Info("autotest : vitreporte, " + door + " : vitre " + (vpGlass != null) + ", charniere " + (vpDoor != null)); vpStep = 9; return; }
                vpPos = vpDoor.InverseTransformPoint(vpGlass.position); vpRot = Quaternion.Inverse(vpDoor.rotation) * vpGlass.rotation;
                float sx = car.InverseTransformPoint(vpGlass.position).x < 0f ? -1f : 1f;
                tcam = new GameObject("MWCoop-CameraEssai").AddComponent<Camera>();
                tcam.depth = 100; tcam.nearClipPlane = 0.02f; tcam.fieldOfView = 70f;
                Vector3 g = car.InverseTransformPoint(vpGlass.position);
                Vector3 eye = car.TransformPoint(new Vector3(sx * 3.2f, 1.6f, g.z - 1.6f)), at = car.TransformPoint(new Vector3(sx * 0.9f, 0.9f, g.z));
                tcam.transform.position = eye; tcam.transform.rotation = Quaternion.LookRotation(at - eye, car.up);
                Autotest.CaptureSoon("vp-ferme", 0.3f);
                Log.Info("autotest : vitreporte, " + Recon.Path(vpGlass) + " (charniere " + vpDoor.name + ", parent de la vitre " + vpGlass.parent.name + ")");
            }
            if (vpStep == 1 && t > 26.5f) { vpStep = 2; Log.Info("autotest : vitreporte, ouvre : " + CarDoors.TestOpen("SORBET", true, door)); }
            if (vpStep == 2 && t > 29f)
            {
                vpStep = 3;
                Autotest.CaptureSoon("vp-ouverte", 0.05f);
                Vector3 p = vpDoor.InverseTransformPoint(vpGlass.position); Quaternion q = Quaternion.Inverse(vpDoor.rotation) * vpGlass.rotation;
                var sb = new System.Text.StringBuilder();
                foreach (Renderer r in vpDoor.GetComponentsInChildren<Renderer>(true))
                    if (r.name.ToLowerInvariant().Contains("glass") || r.name.ToLowerInvariant().Contains("window") || r.name.ToLowerInvariant().Contains("frost")) sb.Append(" [").Append(r.name).Append(r.enabled && r.gameObject.activeInHierarchy ? "" : " cache").Append(" sous ").Append(r.transform.parent != null ? r.transform.parent.name : "-").Append("]");
                Log.Info("autotest : vitreporte, charniere " + Angle(vpDoor) + ", vitre dans le repere de la portiere : ecart " + (p - vpPos).magnitude.ToString("F3") + " m, " + Quaternion.Angle(q, vpRot).ToString("F1") + " deg ;" + sb);
            }
            if (vpStep == 3 && t > 31f) { vpStep = 4; if (tcam != null) Object.Destroy(tcam.gameObject); }
        }

        static float MaxDiff(Matrix4x4 a, Matrix4x4 b) { float e = 0f; for (int i = 0; i < 3; i++) for (int j = 0; j < 4; j++) e = Mathf.Max(e, Mathf.Abs(a[i, j] - b[i, j])); return e; }
        static int tStep; static Vector3 l0, r0; static Quaternion ql0, qr0; static Matrix4x4 ml0, mr0; static Transform gl, gr, car; static PlayMakerFSM kl, kr; static Camera tcam;
        static bool suiviLogged;
        public static void Test(string mode, float t)
        {
            // [Test] Autotest=vitre-suivi (invite, avec Autotest=vitre chez l'hote) : a 50 s, pose des deux vitres de la SORBET
            // dans leur pivot (attendu : celle de l'hote apres ses 3 s de manivelle, droite comprise en miroir).
            if (mode == "vitre-suivi" && t > 50f && !suiviLogged)
            {
                suiviLogged = true;
                GameObject s = GameObject.Find("SORBET(190-200psi)");
                Transform a = s != null ? s.transform.Find("Doors/DoorFront(leftx)/FrontL/WindowPivot/GlassPivot") : null, b = s != null ? s.transform.Find("Doors/DoorFront(right)/FrontR/WindowPivot/GlassPivot") : null;
                Log.Info("autotest : vitre-suivi, gauche " + (a != null ? a.localPosition.ToString("F3") : "?") + " ; droite " + (b != null ? b.localPosition.ToString("F3") : "?") + " (" + wins.Count + " vitres suivies)");
                return;
            }
            if (mode == "vitreporte") { TestDoorGlass(t); return; }
            if (mode != "vitre") return;
            if (tStep == 1 && t < 31f) foreach (Win w in wins) if (w.Key.Contains("SORBET")) w.AimAt = Time.realtimeSinceStartup;   // (comme si on la visait)
            if (tStep == 0 && t > 25f)
            {
                GameObject s = GameObject.Find("SORBET(190-200psi)");
                if (s == null) return;
                car = s.transform;
                gl = car.Find("Doors/DoorFront(leftx)/FrontL/WindowPivot/GlassPivot"); gr = car.Find("Doors/DoorFront(right)/FrontR/WindowPivot/GlassPivot");
                Transform ol = car.Find("Doors/DoorFront(leftx)/FrontL/opener"), or = car.Find("Doors/DoorFront(right)/FrontR/opener MWCoop");
                string rearDoor = Config.Get("Test", "VitreArriere", "");   // ([Test] VitreArriere=DoorRear(right) : vitre arriere au lieu du passager)
                if (rearDoor.Length > 0)
                {
                    Transform rd = car.Find("Doors/" + rearDoor);
                    gr = null; or = null;
                    if (rd != null) foreach (Transform x in rd.GetComponentsInChildren<Transform>(true)) { if (gr == null && x.name == "GlassPivot") gr = x; if (x.name == "opener MWCoop") or = x; }
                }
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
