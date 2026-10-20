using System.Collections.Generic;
using UnityEngine;

namespace MWCoop
{
    // Bavettes souples des voitures addons (bavette_ag / bavette_ad / bavette_vg / bavette_vd.p205 du dossier du modele ; 205 T16,
    // JD 21/10 : « la physique sur les bavettes, seulement sur la verticalite, pas l'horizontalite ») -- reprise de la 2052 du
    // mod prive de JD (validee en jeu), SANS le balancement lateral : chaque bavette plie d'avant en arriere autour de l'axe
    // lateral passant par son bord haut ; angle a la profondeur d (0 en haut, 1 en bas) = a*d + b*d^2, a (raide) et b (souple)
    // suivant chacun un ressort amorti vers la direction de la gravite ressentie au point d'attache (gravite - acceleration)
    // plus le vent de la vitesse (~40 degres a 100 km/h) ; borne vers l'avant (le pneu est devant).
    // Synchro : rien a envoyer -- la voiture d'un autre joueur est deplacee ici par VehicleSync (corps cinematique) ; sa vitesse
    // est tiree de son deplacement d'une image a l'autre (lissee), les bavettes suivent les memes mouvements chez tous.
    public class VazFlaps
    {
        class Flap
        {
            public readonly List<MeshFilter> Mfs = new List<MeshFilter>();
            public readonly List<Vector3[]> RestV = new List<Vector3[]>(), RestN = new List<Vector3[]>(), CurV = new List<Vector3[]>(), CurN = new List<Vector3[]>();
            public Vector3 Hinge; public float Top, Height;
            public float A, Av, B, Bv;
            public Vector3 LastVel, Acc, LastPos, KinVel; public bool HasVel, HasPos;
        }
        readonly List<Flap> flaps = new List<Flap>();
        Transform body; Rigidbody rb;

        public int Count { get { return flaps.Count; } }
        static float testWind = float.NaN;
        static float TestWind { get { if (float.IsNaN(testWind)) testWind = Config.GetFloat("Test", "VazBavettesVent", -1f); return testWind; } }

        public VazFlaps(Transform body, Rigidbody rb) { this.body = body; this.rb = rb; }

        // go : enfant de la carrosserie a l'origine, pieces de la bavette dessous (sommets dans le repere de la carrosserie)
        public void Add(Transform go)
        {
            var f = new Flap();
            var all = new List<Vector3>();
            foreach (MeshFilter mf in go.GetComponentsInChildren<MeshFilter>(true))   // (voiture encore eteinte a la construction)
            {
                Mesh m = Object.Instantiate(mf.sharedMesh);   // (maillages du cache partages par toutes les voitures)
                m.hideFlags = HideFlags.HideAndDontSave;
                mf.sharedMesh = m;
                f.Mfs.Add(mf); f.RestV.Add(m.vertices); f.RestN.Add(m.normals); f.CurV.Add(m.vertices); f.CurN.Add(m.normals);
                all.AddRange(m.vertices);
                Bounds bb = m.bounds; bb.Expand(0.6f); m.bounds = bb;
            }
            if (all.Count == 0) return;
            float top = float.MinValue, bottom = float.MaxValue;
            foreach (Vector3 v in all) { top = Mathf.Max(top, v.y); bottom = Mathf.Min(bottom, v.y); }
            Vector3 s = Vector3.zero; int n = 0; float sx = 0f;
            foreach (Vector3 v in all) { sx += v.x; if (v.y > top - 0.025f) { s += v; n++; } }
            f.Top = top; f.Height = Mathf.Max(0.05f, top - bottom);
            f.Hinge = new Vector3(sx / all.Count, top, s.z / Mathf.Max(1, n));
            flaps.Add(f);
        }

        // vitesse du point d'attache : celle du corps physique ; corps cinematique (voiture d'un autre joueur), par son deplacement
        Vector3 PointVel(Flap f, Vector3 hw, float dt)
        {
            if (rb != null && !rb.isKinematic) { f.HasPos = false; return rb.GetPointVelocity(hw); }
            if (f.HasPos)
            {
                Vector3 raw = (hw - f.LastPos) / dt;
                if (raw.sqrMagnitude < 80f * 80f) f.KinVel = Vector3.Lerp(f.KinVel, raw, Mathf.Clamp01(dt / 0.06f));   // (saut : recalage, ignore)
            }
            f.LastPos = hw; f.HasPos = true;
            return f.KinVel;
        }

        public void Update()
        {
            float dt = Time.deltaTime;
            if (body == null || dt <= 0f || flaps.Count == 0) return;
            foreach (Flap f in flaps)
            {
                Vector3 hw = body.TransformPoint(f.Hinge);
                Vector3 vel = PointVel(f, hw, dt);
                if (f.HasVel) f.Acc = Vector3.Lerp(f.Acc, (vel - f.LastVel) / dt, Mathf.Clamp01(dt / 0.03f));
                f.LastVel = vel; f.HasVel = true;
                Vector3 aL = body.InverseTransformDirection(f.Acc), vL = body.InverseTransformDirection(vel);
                Vector3 gL = body.InverseTransformDirection(Physics.gravity) - aL;   // gravite ressentie
                float vz = TestWind >= 0f ? TestWind / 3.6f : vL.z;                 // ([Test] VazBavettesVent=<km/h> : vent impose)
                gL.z -= 0.0106f * vz * Mathf.Abs(vz);                              // vent
                float phi = Mathf.Clamp(Mathf.Atan2(-gL.z, -gL.y), -0.35f, 1.25f);
                int steps = Mathf.Clamp(Mathf.CeilToInt(dt / 0.004f), 1, 12);
                float h = dt / steps;
                for (int i = 0; i < steps; i++)
                {
                    float aa = 400f * (0.3f * phi - f.A) - 18f * f.Av;
                    f.Av += aa * h; f.A += f.Av * h;
                    float ba = 70f * (0.7f * phi - f.B) - 3.2f * f.Bv - 0.5f * aa - 0.45f * aL.y;
                    f.Bv += ba * h; f.B += f.Bv * h;
                }
                if (f.A + f.B < -0.35f) { float over = -0.35f - (f.A + f.B); f.B += over; if (f.Bv < 0f) f.Bv = -f.Bv * 0.3f; }
                for (int m = 0; m < f.Mfs.Count; m++)
                {
                    if (f.Mfs[m] == null) continue;
                    Vector3[] rv = f.RestV[m], rn = f.RestN[m], cv = f.CurV[m], cn = f.CurN[m];
                    for (int i = 0; i < rv.Length; i++)
                    {
                        float d = Mathf.Clamp01((f.Top - rv[i].y) / f.Height);
                        Quaternion q = Quaternion.AngleAxis((f.A * d + f.B * d * d) * Mathf.Rad2Deg, Vector3.right);
                        cv[i] = f.Hinge + q * (rv[i] - f.Hinge);
                        if (rn.Length == rv.Length) cn[i] = q * rn[i];
                    }
                    Mesh mesh = f.Mfs[m].sharedMesh;
                    mesh.vertices = cv;
                    if (rn.Length == rv.Length) mesh.normals = cn;
                }
            }
        }

        // ([Test] journal) angles en degres
        public string Describe()
        {
            var sb = new System.Text.StringBuilder();
            foreach (Flap f in flaps) sb.Append(sb.Length > 0 ? " | " : "").Append("a ").Append((f.A * Mathf.Rad2Deg).ToString("F0")).Append(" b ").Append((f.B * Mathf.Rad2Deg).ToString("F0"));
            return sb.ToString();
        }
    }
}
