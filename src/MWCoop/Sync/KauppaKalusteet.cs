using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace MWCoop
{
    // Boutique : meubles. Copies des maillages d'objets du jeu (MeshFilter + MeshRenderer, sans scripts ni physique).
    // Piege : les objets regroupes par le jeu (static batching, maillage "Combined Mesh" illisible en coordonnees monde)
    // ne se copient pas (boites de plusieurs km, jeu plante) : seulement des objets non regroupes.
    public static partial class Kauppa
    {
        static Quaternion YawOnly(Quaternion q) { return Quaternion.Euler(0, q.eulerAngles.y, 0); }

        // anchor : quel cote de la boite englobante (repere boutique) se pose sur "at" (-1 min, 0 centre, 1 max) ;
        // at.y = NaN : pose sur le sol du jeu sous le point. yaw : rotation ajoutee a l'orientation d'origine.
        static GameObject Clone(string path, int index, Vector3 at, float yaw, Vector3 anchor, bool collide = true, bool verbose = true)
        {
            GameObject src = FindPath(path, index);
            if (src == null) { L("meuble introuvable : " + path); return null; }
            var g = new GameObject("osuus_" + src.name);
            g.transform.SetParent(root.transform, false);
            Transform st = src.transform;
            int n = 0;
            foreach (MeshRenderer r in src.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (r.GetComponent<TextMesh>() != null) continue;
                var mf = r.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;
                Mesh mesh = mf.sharedMesh; Material[] mm = r.sharedMaterials;
                if (mesh.name.StartsWith("Combined Mesh") && !Unbatch(r, ref mesh, ref mm)) { L("meuble " + path + " : piece " + r.name + " regroupee, non extraite"); continue; }
                var c = new GameObject(r.name);
                c.transform.SetParent(g.transform, false);
                c.transform.localPosition = st.InverseTransformPoint(r.transform.position);
                c.transform.localRotation = Quaternion.Inverse(st.rotation) * r.transform.rotation;
                Vector3 ls = r.transform.lossyScale, ss = st.lossyScale;
                Vector3 sc = new Vector3(Div(ls.x, ss.x), Div(ls.y, ss.y), Div(ls.z, ss.z));
                if (!Ok(sc) || !Ok(c.transform.localPosition)) { L("meuble " + path + " : piece " + r.name + " ignoree (echelle " + ls + " / " + ss + ")"); UnityEngine.Object.Destroy(c); continue; }
                c.transform.localScale = sc;
                c.AddComponent<MeshFilter>().sharedMesh = mesh;
                c.AddComponent<MeshRenderer>().sharedMaterials = mm;
                n++;
            }
            g.transform.localScale = Ok(st.lossyScale) && st.lossyScale.sqrMagnitude > 1e-6f ? st.lossyScale : Vector3.one;
            g.transform.rotation = root.transform.rotation * Quaternion.Euler(0, yaw, 0) * Quaternion.Inverse(YawOnly(st.rotation)) * st.rotation;
            g.transform.localPosition = Vector3.zero;
            if (n == 0) { L("meuble sans maillage : " + path); UnityEngine.Object.Destroy(g); return null; }
            Bounds b = LocalBounds(g);
            if (float.IsNaN(at.y))
            {
                RaycastHit? h = GroundHit(root.transform.TransformPoint(new Vector3(at.x, 0, at.z)) + Vector3.up * 50f);
                at.y = h.HasValue ? root.transform.InverseTransformPoint(h.Value.point).y : 0f;
            }
            Vector3 pick = new Vector3(Pick(b, 0, anchor.x), Pick(b, 1, anchor.y), Pick(b, 2, anchor.z));
            g.transform.localPosition = at - pick;
            if (!Ok(b.size) || b.size.magnitude > 20f) { L("meuble " + path + " : taille anormale " + b.size + ", retire"); UnityEngine.Object.Destroy(g); return null; }
            if (collide)
            {
                b.center += at - pick;
                var cg = new GameObject("c");
                cg.transform.SetParent(root.transform, false);
                cg.transform.localPosition = b.center;
                cg.AddComponent<BoxCollider>().size = b.size;
            }
            if (verbose) L("meuble " + path + " : " + n + " pieces, taille " + b.size.ToString("F2") + " en " + g.transform.localPosition.ToString("F2"));
            return g;
        }

        // objet regroupe par le jeu (static batching) : son maillage est un gros bloc en coordonnees monde. On garde les
        // sous-maillages entierement dans la boite de l'objet, ramenes dans son repere.
        static readonly Dictionary<string, Mesh> unbatched = new Dictionary<string, Mesh>();
        static bool Unbatch(MeshRenderer r, ref Mesh mesh, ref Material[] mm)
        {
            string key = mesh.GetInstanceID() + "/" + r.GetInstanceID();
            Mesh done;
            Bounds wb = r.bounds; wb.Expand(0.02f);
            Vector3[] v;
            try { v = mesh.vertices; } catch (System.Exception e) { L("regroupe " + r.name + " : sommets illisibles " + e.Message); return false; }
            if (v == null || v.Length == 0) { L("regroupe " + r.name + " : 0 sommet (lisible " + mesh.isReadable + ")"); return false; }
            var subs = new List<int>();
            for (int i = 0; i < mesh.subMeshCount; i++)
            {
                int[] t = mesh.GetTriangles(i);
                if (t.Length == 0) continue;
                bool inside = true;
                foreach (int k in t) if (!wb.Contains(v[k])) { inside = false; break; }
                if (inside) subs.Add(i);
            }
            if (subs.Count == 0)
            {
                int best = -1, bestIn = 0, bestN = 0;
                for (int i = 0; i < mesh.subMeshCount; i++)
                {
                    int[] t = mesh.GetTriangles(i); int inn = 0;
                    foreach (int k in t) if (wb.Contains(v[k])) inn++;
                    if (inn > bestIn) { best = i; bestIn = inn; bestN = t.Length; }
                }
                L("regroupe " + r.name + " : aucun sous-maillage dans " + wb + " (actif " + r.gameObject.activeInHierarchy + ", " + mesh.subMeshCount + " sous-maillages, meilleur " + best + " : " + bestIn + "/" + bestN + " indices dedans)");
                return false;
            }
            if (!unbatched.TryGetValue(key, out done))
            {
                Vector3[] nrm = mesh.normals; Vector2[] uv = mesh.uv;
                var map = new Dictionary<int, int>();
                var nv = new List<Vector3>(); var nn = new List<Vector3>(); var nu = new List<Vector2>();
                var tris = new List<int[]>();
                foreach (int i in subs)
                {
                    var lt = new List<int>();
                    foreach (int k in mesh.GetTriangles(i))
                    {
                        int j;
                        if (!map.TryGetValue(k, out j))
                        {
                            j = nv.Count; map[k] = j;
                            nv.Add(r.transform.InverseTransformPoint(v[k]));
                            nn.Add(nrm != null && nrm.Length > k ? r.transform.InverseTransformDirection(nrm[k]) : Vector3.up);
                            nu.Add(uv != null && uv.Length > k ? uv[k] : Vector2.zero);
                        }
                        lt.Add(j);
                    }
                    tris.Add(lt.ToArray());
                }
                done = new Mesh { name = r.name + "_osuus" };
                done.vertices = nv.ToArray(); done.normals = nn.ToArray(); done.uv = nu.ToArray();
                done.subMeshCount = tris.Count;
                for (int i = 0; i < tris.Count; i++) done.SetTriangles(tris[i], i);
                done.RecalculateBounds();
                unbatched[key] = done;
            }
            mesh = done;
            if (mm.Length != subs.Count) L("regroupe " + r.name + " : " + subs.Count + " sous-maillages pour " + mm.Length + " materiaux");
            var pm = new Material[subs.Count];
            for (int i = 0; i < pm.Length; i++) pm[i] = mm[Mathf.Min(i, mm.Length - 1)];
            mm = pm;
            return true;
        }

        static float Div(float a, float b) { return Mathf.Abs(b) < 1e-6f ? 1f : a / b; }
        static bool Ok(Vector3 v) { return !float.IsNaN(v.x + v.y + v.z) && !float.IsInfinity(v.x + v.y + v.z) && Mathf.Abs(v.x) < 1e4f && Mathf.Abs(v.y) < 1e4f && Mathf.Abs(v.z) < 1e4f; }
        static float Pick(Bounds b, int axis, float a) { return a < 0 ? b.min[axis] : a > 0 ? b.max[axis] : b.center[axis]; }

        // boite englobante des rendus de g, dans le repere de la boutique
        static Bounds LocalBounds(GameObject g)
        {
            bool first = true; Bounds o = new Bounds();
            foreach (MeshRenderer r in g.GetComponentsInChildren<MeshRenderer>())
            {
                Bounds mb = r.GetComponent<MeshFilter>().sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 c = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    Vector3 p = root.transform.InverseTransformPoint(r.transform.TransformPoint(c));
                    if (first) { o = new Bounds(p, Vector3.zero); first = false; } else o.Encapsulate(p);
                }
            }
            return o;
        }

        // etagere en bois (montants, socle, planches, chapeau). axisX : longueur le long de x (sinon de z), de a0 a a1 ;
        // profondeur de b0 a b1. Renvoie la hauteur du dessus de chaque planche.
        static List<float> Shelf(float a0, float a1, float b0, float b1, bool axisX, float h, int levels)
        {
            var tops = new List<float>();
            float th = 0.025f;
            float v0 = Mathf.Min(b0, b1), v1 = Mathf.Max(b0, b1);
            ShelfBox(axisX, a0, a0 + 0.03f, 0, h, v0, v1, "tiski");
            ShelfBox(axisX, a1 - 0.03f, a1, 0, h, v0, v1, "tiski");
            ShelfBox(axisX, a0, a1, 0, 0.1f, v0, v1, "tiski");
            ShelfBox(axisX, a0, a1, h - th, h, v0, v1, "taso");
            for (int k = 0; k < levels; k++)
            {
                float y = 0.1f + k * (h - 0.1f) / levels;
                if (k > 0) ShelfBox(axisX, a0 + 0.03f, a1 - 0.03f, y, y + th, v0, v1, "taso");
                tops.Add(k > 0 ? y + th : 0.1f);
            }
            cols.Add(axisX ? new Bounds(new Vector3((a0 + a1) / 2, h / 2, (b0 + b1) / 2), new Vector3(a1 - a0, h, Mathf.Abs(b1 - b0)))
                           : new Bounds(new Vector3((b0 + b1) / 2, h / 2, (a0 + a1) / 2), new Vector3(Mathf.Abs(b1 - b0), h, a1 - a0)));
            return tops;
        }

        static Vector3 vendorSpot = new Vector3(3.2f, 0, -2.55f);
        const float CounterTop = 0.93f;   // dessus du tapis du comptoir de la PSK (repere boutique)
        static float fridgeYaw = -90f;   // portes vitrees vers l'interieur (+x)

        // gondole de magasin facon PSK (materiau "store_shelfs" du jeu) : socle, panneau central, joues, tablettes des
        // deux cotes ; le long de x de a0 a a1, profondeur de b0 a b1
        static void StoreGondola(float a0, float a1, float b0, float b1, float h, int levels)
        {
            float mid = (b0 + b1) / 2, t = 0.03f;
            Box(new Vector3(a0, 0, b0 + 0.04f), new Vector3(a1, 0.14f, b1 - 0.04f), "hyllytaso", false);              // socle
            Box(new Vector3(a0 + t, 0.14f, mid - 0.02f), new Vector3(a1 - t, h, mid + 0.02f), "hylly", false);    // panneau central
            Box(new Vector3(a0, 0, b0), new Vector3(a0 + t, h, b1), "hylly", false);                              // joues
            Box(new Vector3(a1 - t, 0, b0), new Vector3(a1, h, b1), "hylly", false);
            for (int k = 0; k < levels; k++)
            {
                float y = 0.14f + (k + 1) * (h - 0.2f) / (levels + 0.3f);
                Box(new Vector3(a0 + t, y, b0 + 0.02f), new Vector3(a1 - t, y + 0.025f, mid - 0.02f), "hyllytaso", false);
                Box(new Vector3(a0 + t, y, mid + 0.02f), new Vector3(a1 - t, y + 0.025f, b1 - 0.02f), "hyllytaso", false);
                // bord avant des tablettes (porte-etiquettes)
                Box(new Vector3(a0 + t, y - 0.03f, b0 + 0.02f), new Vector3(a1 - t, y + 0.035f, b0 + 0.035f), "hyllyreuna", false);
                Box(new Vector3(a0 + t, y - 0.03f, b1 - 0.035f), new Vector3(a1 - t, y + 0.035f, b1 - 0.02f), "hyllyreuna", false);
            }
            cols.Add(new Bounds(new Vector3((a0 + a1) / 2, h / 2, mid), new Vector3(a1 - a0, h, b1 - b0)));
        }

        static void ShelfBox(bool axisX, float u0, float u1, float y0, float y1, float v0, float v1, string key)
        {
            if (axisX) Box(new Vector3(u0, y0, v0), new Vector3(u1, y1, v1), key, false);
            else Box(new Vector3(v0, y0, u0), new Vector3(v1, y1, u1), key, false);
        }
    }
}
