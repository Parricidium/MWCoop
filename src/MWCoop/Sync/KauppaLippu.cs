using System.Collections.Generic;
using UnityEngine;

namespace MWCoop
{
    // Formes de decor de la boutique et du musee. (Le drapeau corse de la boutique -- tissu d'Unity, Cloth, qui figeait le
    // jeu chez des joueurs, retire en 0.83.1, puis vagues sans physique jamais rebranchees -- est parti : le drapeau est au
    // musee, MuseoRakennus ; code mort retire a la refonte du 14/10.)
    public static partial class Kauppa
    {
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
