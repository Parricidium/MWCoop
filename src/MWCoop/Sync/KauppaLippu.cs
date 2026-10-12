using System.Collections.Generic;
using UnityEngine;

namespace MWCoop
{
    // Boutique : drapeau corse (Tete de Maure ; demande de JD, 12/10) sur un mat en biais au coin de la facade, cote
    // porte. Le drapeau est un vrai tissu (Cloth d'Unity) : bord du haut tenu au mat, le reste pend et ondule au vent.
    // Local a chaque joueur (decor).
    public static partial class Kauppa
    {
        static void Lippu()
        {
            // mat : part du coin (facade + pignon gauche vu de dehors), sous l'avant-toit, vers l'exterieur et le haut
            Vector3 foot = new Vector3(-W2 - 0.06f, 2.72f, D2 + 0.06f);
            Vector3 dir = new Vector3(-0.72f, 0.62f, 0.3f).normalized;
            float len = 2.4f, rad = 0.025f;
            var pole = new GameObject("osuus_lipputanko");
            pole.transform.SetParent(root.transform, false);
            pole.AddComponent<MeshFilter>().sharedMesh = Cylinder(foot, foot + dir * len, rad, 10);
            pole.AddComponent<MeshRenderer>().sharedMaterial = mats.ContainsKey("metalli") ? mats["metalli"] : null;
            // support sur la planche d'angle et boule au bout
            var sup = new GameObject("osuus_lipputanko_tuki");
            sup.transform.SetParent(root.transform, false);
            sup.AddComponent<MeshFilter>().sharedMesh = Cylinder(foot - dir * 0.12f, foot + dir * 0.18f, rad * 1.8f, 10);
            sup.AddComponent<MeshRenderer>().sharedMaterial = pole.GetComponent<MeshRenderer>().sharedMaterial;

            // tissu : grille de nu x nv sommets, bord du haut le long du mat (de 0,35 a 1,55 m), pend vers le bas
            const int nu = 16, nv = 11;
            const float w = 1.2f, h = 0.72f, from = 0.6f;
            var v = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>();
            for (int j = 0; j <= nv; j++)
                for (int i = 0; i <= nu; i++)
                {
                    Vector3 top = foot + dir * (from + w * i / nu) - Vector3.up * (rad + 0.005f);
                    v.Add(top + Vector3.down * (h * j / nv));
                    uv.Add(new Vector2(i / (float)nu, 1f - j / (float)nv));
                }
            for (int j = 0; j < nv; j++)
                for (int i = 0; i < nu; i++)
                {
                    int a = j * (nu + 1) + i, b = a + 1, c = a + nu + 1, d = c + 1;
                    t.Add(a); t.Add(b); t.Add(c); t.Add(b); t.Add(d); t.Add(c);    // (une face : l'envers est LippuBack)
                }
            var m = new Mesh { name = "osuus_lippu" };
            m.vertices = v.ToArray(); m.uv = uv.ToArray(); m.triangles = t.ToArray();
            m.RecalculateNormals(); m.RecalculateBounds();

            var flag = new GameObject("osuus_lippu");
            flag.transform.SetParent(root.transform, false);
            var smr = flag.AddComponent<SkinnedMeshRenderer>();
            smr.sharedMesh = m;
            smr.updateWhenOffscreen = true;
            Texture2D tex = Res("lippu-korsika.png", TextureFormat.RGB24);
            tex.wrapMode = TextureWrapMode.Clamp;
            Material basis = GameMat("wood_panel7");
            smr.sharedMaterial = new Material(basis != null ? basis.shader : Shader.Find("Legacy Shaders/Diffuse")) { name = "osuus_lippu", mainTexture = tex, color = Color.white };
            var cloth = flag.AddComponent<Cloth>();
            var co = new ClothSkinningCoefficient[v.Count];
            for (int k = 0; k < co.Length; k++)
            {
                bool pinned = k <= nu;   // rang du haut : tenu au mat
                co[k].maxDistance = pinned ? 0f : 3f;
                co[k].collisionSphereDistance = 0f;
            }
            cloth.coefficients = co;
            // pesanteur propre au tissu : le jeu change celle du moteur par endroits (SetGravity) et le drapeau montait
            cloth.useGravity = false;
            cloth.damping = 0.2f;
            cloth.stretchingStiffness = 0.95f;
            cloth.bendingStiffness = 0.2f;
            // vent leger : du parking vers la foret, rafales au hasard
            cloth.externalAcceleration = new Vector3(0f, -9.81f, 0f) + root.transform.TransformDirection(new Vector3(-1.4f, 0f, -0.8f));
            cloth.randomAcceleration = new Vector3(2.5f, 0.6f, 2.5f);
            // envers : copie du tissu, faces retournees, mise a jour a chaque image (TickLippu)
            var back = new GameObject("osuus_lippu_taka");
            back.transform.SetParent(flag.transform, false);
            lippuBackMesh = new Mesh { name = "osuus_lippu_taka" };
            lippuBackMesh.vertices = v.ToArray(); lippuBackMesh.uv = uv.ToArray();
            var bt = new int[t.Count];
            for (int k = 0; k < t.Count; k += 3) { bt[k] = t[k]; bt[k + 1] = t[k + 2]; bt[k + 2] = t[k + 1]; }
            lippuBackMesh.triangles = bt;
            lippuBackMesh.RecalculateNormals(); lippuBackMesh.RecalculateBounds();
            back.AddComponent<MeshFilter>().sharedMesh = lippuBackMesh;
            back.AddComponent<MeshRenderer>().sharedMaterial = smr.sharedMaterial;
            lippu = cloth; lippuFoot = foot;
            L("drapeau corse en place (" + v.Count + " points de tissu)");
        }

        static Cloth lippu;
        static Vector3 lippuFoot;
        static Mesh lippuBackMesh;
        static Vector3[] lippuNrm;

        // envers du drapeau : les points du tissu, normales retournees (seulement si on le voit)
        static void TickLippu()
        {
            if (lippu == null || lippuBackMesh == null) return;
            Camera cam = Camera.main;
            if (cam != null && (cam.transform.position - lippu.transform.position).sqrMagnitude > 120f * 120f) return;
            Vector3[] vs = lippu.vertices, ns = lippu.normals;
            if (vs == null || vs.Length != lippuBackMesh.vertexCount) return;
            if (lippuNrm == null || lippuNrm.Length != ns.Length) lippuNrm = new Vector3[ns.Length];
            for (int i = 0; i < ns.Length; i++) lippuNrm[i] = -ns[i];
            lippuBackMesh.vertices = vs;
            lippuBackMesh.normals = lippuNrm;
            lippuBackMesh.RecalculateBounds();
        }

        // essais : ou est le tissu
        static void LippuState()
        {
            if (lippu == null) { L("essai : pas de drapeau"); return; }
            Vector3[] vs = lippu.vertices;
            float ymin = float.MaxValue, ymax = float.MinValue;
            foreach (Vector3 p in vs) { ymin = Mathf.Min(ymin, p.y); ymax = Mathf.Max(ymax, p.y); }
            var smr = lippu.GetComponent<SkinnedMeshRenderer>();
            Texture tx = smr != null && smr.sharedMaterial != null ? smr.sharedMaterial.mainTexture : null;
            L("essai : drapeau, tissu " + (lippu.enabled ? "actif" : "coupe") + ", " + vs.Length + " points, y " + ymin.ToString("F2") + " .. " + ymax.ToString("F2")
              + " (pied du mat " + root.transform.TransformPoint(lippuFoot).y.ToString("F2") + "), premier point " + (vs.Length > 0 ? vs[0].ToString("F2") : "-") + ", dernier " + (vs.Length > 0 ? vs[vs.Length - 1].ToString("F2") : "-")
              + ", texture " + (tx != null ? tx.name + " " + tx.width + "x" + tx.height : "aucune") + ", shader " + (smr != null && smr.sharedMaterial != null ? smr.sharedMaterial.shader.name : "-"));
        }

        // cylindre de a vers b (repere boutique), sans bouchons
        static Mesh Cylinder(Vector3 a, Vector3 b, float r, int seg)
        {
            Vector3 ax = (b - a).normalized;
            Vector3 u = Vector3.Cross(ax, Vector3.up); if (u.sqrMagnitude < 1e-4f) u = Vector3.Cross(ax, Vector3.right);
            u.Normalize();
            Vector3 w = Vector3.Cross(ax, u);
            var v = new List<Vector3>(); var n = new List<Vector3>(); var t = new List<int>();
            for (int s = 0; s <= seg; s++)
            {
                float ang = s * Mathf.PI * 2f / seg;
                Vector3 d = u * Mathf.Cos(ang) + w * Mathf.Sin(ang);
                v.Add(a + d * r); n.Add(d);
                v.Add(b + d * r); n.Add(d);
            }
            for (int s = 0; s < seg; s++)
            {
                int i0 = s * 2, i1 = i0 + 1, i2 = i0 + 2, i3 = i0 + 3;
                t.Add(i0); t.Add(i1); t.Add(i2); t.Add(i2); t.Add(i1); t.Add(i3);
                t.Add(i0); t.Add(i2); t.Add(i1); t.Add(i2); t.Add(i3); t.Add(i1);   // (deux faces : le sens ne compte pas)
            }
            var m = new Mesh { name = "osuus_putki" };
            m.vertices = v.ToArray(); m.normals = n.ToArray(); m.triangles = t.ToArray();
            m.RecalculateBounds();
            return m;
        }
    }
}
