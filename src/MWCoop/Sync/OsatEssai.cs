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
                    OsatBuy(osatOffers[i], now);
                    break;
                }
                case "osattesti": OsatTestItems(); break;
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
