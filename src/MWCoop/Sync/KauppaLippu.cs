using System.Collections.Generic;
using UnityEngine;

namespace MWCoop
{
    // Boutique : drapeau corse (Tete de Maure ; demande de JD, 12/10) sur un mat en biais au coin de la facade, cote
    // porte. Bord du haut tenu au mat, le reste pend et ondule : vagues calculees (sinus), SANS physique. Le tissu
    // d'Unity (Cloth) figeait le jeu chez des joueurs (retire en 0.83.1, rappel de JD le 13/10) : n'y jamais revenir.
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

            // tissu : grille de LippuU x LippuV sommets, bord du haut le long du mat (de 0,6 a 1,8 m), pend vers le bas
            const float w = 1.2f, h = 0.72f, from = 0.6f;
            var v = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>();
            for (int j = 0; j <= LippuV; j++)
                for (int i = 0; i <= LippuU; i++)
                {
                    Vector3 top = foot + dir * (from + w * i / LippuU) - Vector3.up * (rad + 0.005f);
                    v.Add(top + Vector3.down * (h * j / LippuV));
                    uv.Add(new Vector2(i / (float)LippuU, 1f - j / (float)LippuV));
                }
            for (int j = 0; j < LippuV; j++)
                for (int i = 0; i < LippuU; i++)
                {
                    int a = j * (LippuU + 1) + i, b = a + 1, c = a + LippuU + 1, d = c + 1;
                    t.Add(a); t.Add(b); t.Add(c); t.Add(b); t.Add(d); t.Add(c);    // (une face : l'envers est un second maillage)
                }
            lippuRest = v.ToArray();
            lippuPos = new Vector3[lippuRest.Length];
            lippuNrm = new Vector3[lippuRest.Length];
            // vagues : de part et d'autre du plan du drapeau, vent du parking vers la foret
            lippuSide = Vector3.Cross(dir, Vector3.up).normalized;
            lippuWind = new Vector3(-1.4f, 0f, -0.8f).normalized;
            lippuMesh = new Mesh { name = "osuus_lippu" };
            lippuMesh.vertices = lippuRest; lippuMesh.uv = uv.ToArray(); lippuMesh.triangles = t.ToArray();
            lippuMesh.RecalculateNormals(); lippuMesh.RecalculateBounds();
            lippuMesh.MarkDynamic();

            var flag = new GameObject("osuus_lippu");
            flag.transform.SetParent(root.transform, false);
            flag.AddComponent<MeshFilter>().sharedMesh = lippuMesh;
            var mr = flag.AddComponent<MeshRenderer>();
            Texture2D tex = Res("lippu-korsika.png", TextureFormat.RGB24);
            tex.wrapMode = TextureWrapMode.Clamp;
            Material basis = GameMat("wood_panel7");
            mr.sharedMaterial = new Material(basis != null ? basis.shader : Shader.Find("Legacy Shaders/Diffuse")) { name = "osuus_lippu", mainTexture = tex, color = Color.white };
            // envers : memes points, faces retournees, normales opposees
            var back = new GameObject("osuus_lippu_taka");
            back.transform.SetParent(flag.transform, false);
            lippuBackMesh = new Mesh { name = "osuus_lippu_taka" };
            lippuBackMesh.vertices = lippuRest; lippuBackMesh.uv = uv.ToArray();
            var bt = new int[t.Count];
            for (int k = 0; k < t.Count; k += 3) { bt[k] = t[k]; bt[k + 1] = t[k + 2]; bt[k + 2] = t[k + 1]; }
            lippuBackMesh.triangles = bt;
            lippuBackMesh.RecalculateNormals(); lippuBackMesh.RecalculateBounds();
            lippuBackMesh.MarkDynamic();
            back.AddComponent<MeshFilter>().sharedMesh = lippuBackMesh;
            back.AddComponent<MeshRenderer>().sharedMaterial = mr.sharedMaterial;
            lippu = flag.transform; lippuFoot = foot;
            L("drapeau corse en place (" + v.Count + " points, vagues sans physique)");
        }

        const int LippuU = 16, LippuV = 11;
        static Transform lippu;
        static Vector3 lippuFoot, lippuSide, lippuWind;
        static Mesh lippuMesh, lippuBackMesh;
        static Vector3[] lippuRest, lippuPos, lippuNrm;

        // vagues du drapeau (seulement si on le voit de pres) : le bord tenu au mat ne bouge pas, l'ecart croit vers le
        // bas ; une onde qui court du mat vers le bout libre et une rafale lente qui pousse le tout dans le vent
        static void TickLippu()
        {
            if (lippu == null || lippuMesh == null || lippuRest == null) return;
            Camera cam = Game.MainCam;
            if (cam != null && (cam.transform.position - lippu.position).sqrMagnitude > 120f * 120f) return;
            float time = Time.time;
            float gust = 0.55f + 0.45f * Mathf.Sin(time * 0.37f) * Mathf.Sin(time * 0.23f + 1.3f);
            for (int j = 0; j <= LippuV; j++)
            {
                float f = j / (float)LippuV;           // 0 au mat, 1 en bas
                for (int i = 0; i <= LippuU; i++)
                {
                    float g = i / (float)LippuU;
                    float wave = Mathf.Sin(time * 2.6f - g * 5.5f - f * 2.2f) * 0.065f + Mathf.Sin(time * 4.1f - g * 9f + f) * 0.02f;
                    Vector3 off = lippuSide * (wave * f * (0.6f + 0.6f * gust)) + lippuWind * (0.18f * gust * f * f);
                    off.y += 0.05f * gust * f * f;     // le vent souleve un peu le bas
                    int k = j * (LippuU + 1) + i;
                    lippuPos[k] = lippuRest[k] + off;
                }
            }
            lippuMesh.vertices = lippuPos;
            lippuMesh.RecalculateNormals();
            lippuMesh.RecalculateBounds();
            Vector3[] ns = lippuMesh.normals;
            for (int k = 0; k < ns.Length; k++) lippuNrm[k] = -ns[k];
            lippuBackMesh.vertices = lippuPos;
            lippuBackMesh.normals = lippuNrm;
            lippuBackMesh.RecalculateBounds();
        }

        // essais : ou est le drapeau
        static void LippuState()
        {
            if (lippu == null || lippuMesh == null) { L("essai : pas de drapeau"); return; }
            Vector3[] vs = lippuMesh.vertices;
            float ymin = float.MaxValue, ymax = float.MinValue;
            foreach (Vector3 p in vs) { ymin = Mathf.Min(ymin, p.y); ymax = Mathf.Max(ymax, p.y); }
            var mr = lippu.GetComponent<MeshRenderer>();
            Texture tx = mr != null && mr.sharedMaterial != null ? mr.sharedMaterial.mainTexture : null;
            L("essai : drapeau sans physique, " + vs.Length + " points, y local " + ymin.ToString("F2") + " .. " + ymax.ToString("F2")
              + ", Cloth " + (lippu.GetComponent<Cloth>() != null ? "PRESENT" : "aucun")
              + ", texture " + (tx != null ? tx.name + " " + tx.width + "x" + tx.height : "aucune") + ", shader " + (mr != null && mr.sharedMaterial != null ? mr.sharedMaterial.shader.name : "-"));
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
