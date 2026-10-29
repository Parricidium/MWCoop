using System.Collections.Generic;
using UnityEngine;

namespace MWCoop
{
    // Antennes souples des voitures addons (antenne_1 / antenne_2.p205 du dossier du modele ; JD 29/10 : « fait que toutes les
    // antennes aient une physique », et plus de collision -- la voiture retournee tenait sur la pointe de son antenne) : fouet
    // tenu a sa base, la pointe suit un ressort amorti en 2 axes (lateral, longitudinal) vers la deviation que donnent
    // l'acceleration ressentie a la base et le vent de la vitesse ; chaque sommet suit en d^2 (d = hauteur relative depuis la base),
    // longueur gardee a peu pres (la pointe descend un peu quand elle plie).
    // Synchro : rien a envoyer -- comme les bavettes (VazFlaps), vitesse d'une voiture menee ailleurs tiree de son deplacement.
    public class VazAntennas
    {
        class Ant
        {
            public readonly List<MeshFilter> Mfs = new List<MeshFilter>();
            public readonly List<Vector3[]> RestV = new List<Vector3[]>(), CurV = new List<Vector3[]>();
            public Vector3 Base; public float Bottom, Height;
            public Vector2 S, Sv;   // (deviation de la pointe en fraction de la hauteur : x lateral, y longitudinal ; et sa vitesse)
            public Vector3 LastVel, Acc, LastPos, KinVel; public bool HasVel, HasPos;
        }
        readonly List<Ant> ants = new List<Ant>();
        readonly Transform body; readonly Rigidbody rb;

        public int Count { get { return ants.Count; } }

        public VazAntennas(Transform body, Rigidbody rb) { this.body = body; this.rb = rb; }

        // go : enfant de la carrosserie a l'origine, pieces de l'antenne dessous (sommets dans le repere de la carrosserie)
        public void Add(Transform go)
        {
            var a = new Ant();
            var all = new List<Vector3>();
            foreach (MeshFilter mf in go.GetComponentsInChildren<MeshFilter>(true))
            {
                Mesh m = Object.Instantiate(mf.sharedMesh);   // (maillages du cache partages par toutes les voitures)
                m.hideFlags = HideFlags.HideAndDontSave;
                mf.sharedMesh = m;
                a.Mfs.Add(mf); a.RestV.Add(m.vertices); a.CurV.Add(m.vertices);
                all.AddRange(m.vertices);
                Bounds bb = m.bounds; bb.Expand(0.8f); m.bounds = bb;
            }
            if (all.Count == 0) return;
            float bottom = float.MaxValue, top = float.MinValue;
            foreach (Vector3 v in all) { bottom = Mathf.Min(bottom, v.y); top = Mathf.Max(top, v.y); }
            Vector3 s = Vector3.zero; int n = 0;
            foreach (Vector3 v in all) if (v.y < bottom + 0.02f) { s += v; n++; }
            a.Bottom = bottom; a.Height = Mathf.Max(0.05f, top - bottom); a.Base = s / Mathf.Max(1, n);
            ants.Add(a);
        }

        Vector3 PointVel(Ant a, Vector3 bw, float dt)
        {
            if (rb != null && !rb.isKinematic) { a.HasPos = false; return rb.GetPointVelocity(bw); }
            if (a.HasPos)
            {
                Vector3 raw = (bw - a.LastPos) / dt;
                if (raw.sqrMagnitude < 80f * 80f) a.KinVel = Vector3.Lerp(a.KinVel, raw, Mathf.Clamp01(dt / 0.06f));   // (saut : recalage, ignore)
            }
            a.LastPos = bw; a.HasPos = true;
            return a.KinVel;
        }

        public void Update()
        {
            float dt = Time.deltaTime;
            if (body == null || dt <= 0f || ants.Count == 0) return;
            foreach (Ant a in ants)
            {
                Vector3 bw = body.TransformPoint(a.Base);
                Vector3 vel = PointVel(a, bw, dt);
                if (a.HasVel) a.Acc = Vector3.Lerp(a.Acc, (vel - a.LastVel) / dt, Mathf.Clamp01(dt / 0.03f));
                a.LastVel = vel; a.HasVel = true;
                Vector3 aL = body.InverseTransformDirection(a.Acc), vL = body.InverseTransformDirection(vel);
                // (cible : la pointe part a l'oppose de l'acceleration -- 0,015 de la hauteur par m/s2 --, et en arriere avec le vent,
                // 0,25 a 100 km/h)
                Vector2 target = new Vector2(-aL.x, -aL.z) * 0.015f + new Vector2(0f, -0.25f * vL.z * Mathf.Abs(vL.z) / (27.8f * 27.8f));
                target = Vector2.ClampMagnitude(target, 0.6f);
                int steps = Mathf.Clamp(Mathf.CeilToInt(dt / 0.004f), 1, 12);
                float h = dt / steps;
                for (int i = 0; i < steps; i++)
                {
                    Vector2 acc = 160f * (target - a.S) - 3.5f * a.Sv;   // (~2 Hz, peu amortie : elle oscille un peu)
                    a.Sv += acc * h; a.S += a.Sv * h;
                }
                a.S = Vector2.ClampMagnitude(a.S, 0.7f);
                for (int m = 0; m < a.Mfs.Count; m++)
                {
                    if (a.Mfs[m] == null) continue;
                    Vector3[] rv = a.RestV[m], cv = a.CurV[m];
                    for (int i = 0; i < rv.Length; i++)
                    {
                        float d = Mathf.Clamp01((rv[i].y - a.Bottom) / a.Height), d2 = d * d, L = a.Height;
                        float dx = a.S.x * d2 * L, dz = a.S.y * d2 * L;
                        cv[i] = new Vector3(rv[i].x + dx, rv[i].y - 0.5f * (dx * dx + dz * dz) / Mathf.Max(0.05f, L * d), rv[i].z + dz);
                    }
                    a.Mfs[m].sharedMesh.vertices = cv;
                }
            }
        }

        // ([Test] journal) deviation de la pointe, en cm
        public string Describe()
        {
            var sb = new System.Text.StringBuilder();
            foreach (Ant a in ants) sb.Append(sb.Length > 0 ? " | " : "").Append((a.S.x * a.Height * 100f).ToString("F0")).Append(" / ").Append((a.S.y * a.Height * 100f).ToString("F0")).Append(" cm");
            return sb.ToString();
        }
    }
}
