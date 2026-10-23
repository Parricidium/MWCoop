using System;
using System.Collections.Generic;
using HutongGames.PlayMaker;
using UnityEngine;

namespace MWCoop
{
    // Essais du magasin de pieces (kauppa-essai.txt, apres celles du cinema) :
    //  osat x y z tx ty tz nom [attente] [fov] : photo, comme "lieu", dans le repere du magasin
    //  osatlista : les articles en rayon ; osatosta n : achat de l'article n (argent donne) ; osattesti : objets de mod d'essai
    //  osatpaiva n : n jours de plus (GlobalDaysPassed) pour voir le renouvellement de l'occasion
    // [Test] OsatTesti=1 : objets de mod d'essai inscrits par CoopApi.AddShopItem des la construction (chez chaque joueur).
    public static partial class Kauppa
    {
        static bool osatTestDone;

        static void OsatTestItems()
        {
            if (osatTestDone) return;
            osatTestDone = true;
            GameObject vl = PostChild("VINLIST_order");
            var picks = new List<string>();
            if (vl != null)
                foreach (PlayMakerArrayListProxy p in vl.GetComponents<PlayMakerArrayListProxy>())
                    if ((p.referenceName == "BODY" || p.referenceName == "ENGINE") && p._arrayList != null && p._arrayList.Count > 0) picks.Add(p._arrayList[0] as string);
            int n = 0;
            foreach (string vin in picks)
            {
                string id = "Testi." + vin;
                CoopApi.AddShopItem(id, "Test " + PartName(Template(vin), vin), 25f + 10f * n++, Template(vin), (buyer, here, pos, rot) => TestBought(id, buyer, here, pos, rot));
            }
            CoopApi.AddShopItem("Testi.Kuutio", "Test cube", 5f, null, (buyer, here, pos, rot) => TestBought("Testi.Kuutio", buyer, here, pos, rot));
        }

        static void TestBought(string id, int buyer, bool here, Vector3 pos, Quaternion rot)
        {
            var c = GameObject.CreatePrimitive(PrimitiveType.Cube);
            c.name = "osat_essai_" + id;
            c.transform.position = pos; c.transform.rotation = rot; c.transform.localScale = Vector3.one * 0.25f;
            c.AddComponent<Rigidbody>().mass = 3f;
            L("essai : rappel du mod pour '" + id + "' : acheteur #" + buyer + (here ? " (ici)" : "") + ", cube en " + pos.ToString("F2"));
        }

        static void OsatEssai(string[] a, float now)
        {
            switch (a[1])
            {
                case "osat":
                {
                    if (osat == null) { L("essai : pas de magasin de pieces"); break; }
                    Vector3 at = osat.transform.TransformPoint(new Vector3(F(a[2]), F(a[3]), F(a[4]))), tg = osat.transform.TransformPoint(new Vector3(F(a[5]), F(a[6]), F(a[7])));
                    var b = new string[a.Length];
                    b[0] = a[0]; b[1] = "lieu";
                    var ci = System.Globalization.CultureInfo.InvariantCulture;
                    b[2] = at.x.ToString(ci); b[3] = at.y.ToString(ci); b[4] = at.z.ToString(ci); b[5] = tg.x.ToString(ci); b[6] = tg.y.ToString(ci); b[7] = tg.z.ToString(ci);
                    for (int i = 8; i < a.Length; i++) b[i] = a[i];
                    script.Insert(scriptAt, b);
                    break;
                }
                case "osatlista":
                    for (int i = 0; i < osatOffers.Count; i++)
                    {
                        OsatOffer o = osatOffers[i];
                        L("essai : article " + i + " aile " + o.Wing + " : " + o.Name + " " + o.Price + " mk" + (o.Used != null ? " (" + o.Used.Vin + ", usure " + o.Used.Wear + ")" : "") + (o.Slot != null ? " case " + o.Slot.Pos.ToString("F2") + (o.Slot.Big ? " estrade" : "") : "") + (o.Display != null ? "" : " sans modele"));
                    }
                    break;
                case "osatosta":
                {
                    int i = int.Parse(a[2]);
                    if (i >= osatOffers.Count) { L("essai : pas d'article " + i); break; }
                    KinoGui();
                    if (money != null) money.Value += 1000f;
                    osatLastBuy = -10f;
                    OsatTake(osatOffers[i], true);
                    break;
                }
                case "osatmaksa": { KinoGui(); if (money != null) money.Value += 1000f; osatLastBuy = -10f; OsatPay(now); break; }   // (paie le panier a la caisse)
                case "osattesti": OsatTestItems(); break;
                case "osattormays":
                {
                    // ce qui touche le magasin (+ parvis) et le parking : collisions (OverlapBox) et maillages dessines (boite
                    // englobante), hors le magasin lui-meme ; sommets de la barriere de la piste de ski
                    if (osat == null) break;
                    Transform ot = osat.transform;
                    foreach (var zn in new[] { new { N = "batiment+parvis", C = new Vector3(0f, 1.5f, 0.25f), H = new Vector3(OL + 0.6f, 2.2f, OD + 1.95f) },
                                               new { N = "parking+lampadaires", C = new Vector3(0f, 2f, 17.6f), H = new Vector3(9.5f, 3f, 3.6f) } })
                    {
                        var names = new SortedDictionary<string, int>();
                        Vector3 wc = ot.TransformPoint(zn.C);
                        foreach (Collider c in Physics.OverlapSphere(wc, zn.H.magnitude))   // (pas d'OverlapBox dans cet Unity : sphere, puis boite)
                        {
                            if (c == null || c.transform.IsChildOf(ot)) continue;
                            Bounds cb = c.bounds;
                            if (cb.size.magnitude > 300f) continue;
                            Vector3 cl = ot.InverseTransformPoint(cb.center) - zn.C;
                            if (Mathf.Abs(cl.x) > zn.H.x + Mathf.Max(cb.extents.x, cb.extents.z) || Mathf.Abs(cl.z) > zn.H.z + Mathf.Max(cb.extents.x, cb.extents.z) || Mathf.Abs(cl.y) > zn.H.y + cb.extents.y) continue;
                            string k = "collision " + ObjPath(c.transform) + (c.isTrigger ? " (declencheur)" : "");
                            int n; names.TryGetValue(k, out n); names[k] = n + 1;
                        }
                        foreach (Renderer r in UnityEngine.Object.FindObjectsOfType<Renderer>())
                        {
                            if (r == null || !r.enabled || r.transform.IsChildOf(ot)) continue;
                            Bounds b = r.bounds;
                            if (b.size.magnitude > 300f) continue;   // (sol, routes, foret fusionnes : a part)
                            Vector3 lc = ot.InverseTransformPoint(b.center) - zn.C;
                            Vector3 le = b.extents;
                            if (Mathf.Abs(lc.x) > zn.H.x + Mathf.Max(le.x, le.z) || Mathf.Abs(lc.z) > zn.H.z + Mathf.Max(le.x, le.z) || Mathf.Abs(lc.y) > zn.H.y + le.y) continue;
                            string k = "dessin " + ObjPath(r.transform);
                            int n; names.TryGetValue(k, out n); names[k] = n + 1;
                        }
                        L("essai : tormays " + zn.N + " : " + names.Count + " objet(s)" + (names.Count > 0 ? " -- " : "") + string.Join(" | ", new List<string>(names.Keys).ToArray()));
                    }
                    GameObject fence = GameObject.Find("SkihillFence");
                    MeshFilter mf = fence != null ? fence.GetComponent<MeshFilter>() : null;
                    if (mf != null && mf.sharedMesh != null)
                    {
                        float bestD = float.MaxValue; Vector3 bestP = Vector3.zero;
                        // (le long des aretes des triangles, tous les 25 cm : les panneaux passent entre les poteaux)
                        Vector3[] vs = mf.sharedMesh.vertices; int[] tri = mf.sharedMesh.triangles;
                        var pts = new List<Vector3>();
                        for (int t = 0; t + 2 < tri.Length; t += 3)
                            for (int e = 0; e < 3; e++)
                            {
                                Vector3 pa = fence.transform.TransformPoint(vs[tri[t + e]]), pb = fence.transform.TransformPoint(vs[tri[t + (e + 1) % 3]]);
                                if ((pa - ot.position).sqrMagnitude > 3600f && (pb - ot.position).sqrMagnitude > 3600f) continue;
                                int n = Mathf.Max(1, Mathf.CeilToInt((pb - pa).magnitude / 0.25f));
                                for (int i = 0; i <= n; i++) pts.Add(Vector3.Lerp(pa, pb, i / (float)n));
                            }
                        foreach (Vector3 w in pts)
                        {
                            Vector3 lp = ot.InverseTransformPoint(w);
                            float dx = Mathf.Max(0f, Mathf.Abs(lp.x) - OL), dz = Mathf.Max(0f, Mathf.Abs(lp.z) - OD);   // (aux murs)
                            float d = Mathf.Sqrt(dx * dx + dz * dz);
                            if (d < bestD) { bestD = d; bestP = lp; }
                        }
                        L("essai : tormays barriere de la piste de ski : point le plus proche a " + bestD.ToString("F2") + " m du magasin (local " + bestP.ToString("F1") + ")");
                    }
                    break;
                }
                case "osatpaiva":
                {
                    FsmInt d = FsmVariables.GlobalVariables.FindFsmInt("GlobalDaysPassed");
                    if (d != null) d.Value += int.Parse(a[2]);
                    L("essai : jours passes " + (d != null ? d.Value.ToString() : "?"));
                    break;
                }
            }
        }
    }
}
