using System.Collections;
using System.IO;
using System.Text;
using HutongGames.PlayMaker;
using MWCoop.Net;
using UnityEngine;

namespace MWCoop
{
    // Releve du terrain autour du cinema (essai seulement, [Test] SalonkiReleve=<rayon m>) pour placer la salle d'exposition des
    // voitures (JD 29/10 : « a cote du cinema, le terrain s'y prete », assez grand pour l'agrandir) : grille de hauteurs tous les
    // 4 m (premier obstacle solide sous le point, avec son chemin), points des routes du jeu (TRAFFIC/Routes) et vue de dessus
    // orthographique -> <donnees>/dumps/releve.csv, releve-routes.csv, releve-vue.png.
    public static partial class Kauppa
    {
        static bool releveDone;
        static int saTest = -1; static float saTestAt;
        static Stand saTestStand;

        // essai du garage ([Test] SalonkiEssai=1) : vues rendues (facade et parking, atelier depuis la porte, depuis le fond, ponts,
        // bureau vu de l'atelier, vendeur), menu ouvert par le vendeur (capture du jeu, interface comprise), une autre voiture choisie
        // dans la liste, location (argent a 5000), vue du parking -> dumps/salonki-*.png ; « salonki essai : fin »
        static void SalonkiTest(float now)
        {
            if (salonki == null || Config.GetInt("Test", "SalonkiEssai", 0) == 0 || saTest >= 99) return;
            if (saTest < 0) { saTest = 0; saTestAt = now + 4f; return; }
            if (now < saTestAt) return;
            string dir = Path.Combine(Log.DataDir, "dumps");
            Transform t = salonki.transform;
            switch (saTest)
            {
                case 0:
                    SaShot(t.TransformPoint(new Vector3(9f, 4.5f, -24f)), t.TransformPoint(new Vector3(11f, 2.5f, 6f)), Path.Combine(dir, "salonki-facade.png"), 60f);
                    SaShot(t.TransformPoint(new Vector3(-16f, 16f, -40f)), t.TransformPoint(new Vector3(8f, 0f, -4f)), Path.Combine(dir, "salonki-ensemble.png"), 60f);
                    SaShot(t.TransformPoint(new Vector3(32f, 3.5f, -24f)), t.TransformPoint(new Vector3(12f, 0.5f, -8f)), Path.Combine(dir, "salonki-entree.png"), 60f);
                    SaShot(t.TransformPoint(new Vector3(10.7f, 1.7f, -1.5f)), t.TransformPoint(new Vector3(9f, 1.6f, 16f)), Path.Combine(dir, "salonki-atelier.png"), 75f);
                    SaShot(t.TransformPoint(new Vector3(13.5f, 2.2f, 20.8f)), t.TransformPoint(new Vector3(8f, 1.2f, 4f)), Path.Combine(dir, "salonki-atelier2.png"), 75f);
                    SaShot(t.TransformPoint(new Vector3(8.5f, 1.7f, 11f)), t.TransformPoint(new Vector3(3.5f, 2.2f, 11.5f)), Path.Combine(dir, "salonki-ponts.png"), 70f);
                    SaShot(t.TransformPoint(new Vector3(12.5f, 1.7f, 10.5f)), t.TransformPoint(new Vector3(19.5f, 1.4f, 4.5f)), Path.Combine(dir, "salonki-bureau.png"), 65f);
                    SaShot(t.TransformPoint(new Vector3(19.4f, 1.65f, 2.2f)), t.TransformPoint(SaVendorSpot + new Vector3(0f, 1.3f, 0f)), Path.Combine(dir, "salonki-vendeur.png"), 65f);
                    if (money == null) money = FsmVariables.GlobalVariables.FindFsmFloat("PlayerMoney");
                    if (money != null) money.Value = 5000f;
                    saTestStand = stands[0];
                    OpenSalonki(saTestStand);
                    saTest = 1; saTestAt = now + 1.5f; break;
                case 1:
                    Application.CaptureScreenshot(Path.Combine(dir, "salonki-menu.png"));
                    foreach (Stand st in stands) if (st.Folder == "r5maxi") saTestStand = st;
                    if (saTestStand == stands[0] && stands.Count > 1) saTestStand = stands[stands.Count - 1];
                    saMenu = saTestStand;   // (comme un clic dans la liste)
                    saTest = 2; saTestAt = now + 1f; break;
                case 2:
                    Application.CaptureScreenshot(Path.Combine(dir, "salonki-menu2.png"));
                    saTest = 3; saTestAt = now + 1f; break;
                case 3:
                    RentSalonki(saTestStand);
                    L("salonki essai : location " + saTestStand.Name + ", note : " + saNote);
                    saTest = 4; saTestAt = now + 1.2f; break;
                case 4:
                    Application.CaptureScreenshot(Path.Combine(dir, "salonki-menu3.png"));
                    saTest = 5; saTestAt = now + 9f; break;
                case 5:
                    SalonkiOpen = false;
                    Transform car = Vaz.CarOf(Session.Active ? Session.LocalId : 0);
                    L("salonki essai : voiture " + (car != null ? car.name + " en " + car.transform.position.ToString("F1") : "ABSENTE") + ", argent " + (money != null ? money.Value.ToString("F0") : "?"));
                    SaShot(t.TransformPoint(new Vector3(-3f, 4f, -20f)), t.TransformPoint(new Vector3(9f, 0.5f, -6f)), Path.Combine(dir, "salonki-livraison.png"), 60f);
                    saTest = 99; L("salonki essai : fin"); break;
            }
        }

        static void SaShot(Vector3 from, Vector3 at, string file, float fov)
        {
            var go = new GameObject("MWCoop salonki cam");
            try
            {
                Camera cam = go.AddComponent<Camera>();
                cam.enabled = false; cam.fieldOfView = fov; cam.nearClipPlane = 0.05f; cam.farClipPlane = 1500f;
                go.transform.position = from; go.transform.LookAt(at);
                var rt = new RenderTexture(1600, 900, 24);
                cam.targetTexture = rt; cam.Render();
                RenderTexture.active = rt;
                var tex = new Texture2D(1600, 900, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, 1600, 900), 0, 0); tex.Apply();
                RenderTexture.active = null; cam.targetTexture = null;
                File.WriteAllBytes(file, tex.EncodeToPNG());
                Object.Destroy(tex); rt.Release(); Object.Destroy(rt);
            }
            catch (System.Exception e) { L("salonki essai : capture " + e.Message); }
            Object.Destroy(go);
        }

        static void ReleveTick(float now)
        {
            if (releveDone || now < t0 + 6f) return;
            float r = Config.GetFloat("Test", "SalonkiReleve", 0f);
            if (r <= 0f) { releveDone = true; return; }
            releveDone = true;
            Vector3 c; float yaw; string fl;
            KinoFrame(out c, out yaw, out fl);
            string dir = Path.Combine(Log.DataDir, "dumps");
            Directory.CreateDirectory(dir);
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            var sb = new StringBuilder("x,z,y,objet\n");
            int n = 0;
            for (float x = c.x - r; x <= c.x + r; x += 4f)
                for (float z = c.z - r; z <= c.z + r; z += 4f)
                {
                    RaycastHit? h = GroundHit(new Vector3(x, 300f, z));
                    if (!h.HasValue) continue;
                    Transform t = h.Value.collider.transform;
                    string p = t.name; int up = 0;
                    while (t.parent != null && up < 3) { t = t.parent; p = t.name + "/" + p; up++; }
                    sb.Append(x.ToString("F1", ci)).Append(',').Append(z.ToString("F1", ci)).Append(',').Append(h.Value.point.y.ToString("F2", ci)).Append(',').Append(p.Replace(',', ' ')).Append('\n');
                    n++;
                }
            File.WriteAllText(Path.Combine(dir, "releve.csv"), sb.ToString());
            var rb = new StringBuilder("route,x,z,y\n");
            GameObject routes = Game.FindAny("TRAFFIC/Routes");
            if (routes != null)
                foreach (Transform ch in routes.transform)
                    foreach (PlayMakerArrayListProxy pr in ch.GetComponents<PlayMakerArrayListProxy>())
                    {
                        ArrayList a = pr._arrayList ?? pr.arrayList;
                        if (a == null) continue;
                        foreach (object o in a)
                        {
                            var go = o as GameObject; if (go == null) continue;
                            Vector3 q = go.transform.position;
                            if (Mathf.Abs(q.x - c.x) > r || Mathf.Abs(q.z - c.z) > r) continue;
                            rb.Append(ch.name).Append(',').Append(q.x.ToString("F1", ci)).Append(',').Append(q.z.ToString("F1", ci)).Append(',').Append(q.y.ToString("F1", ci)).Append('\n');
                        }
                        break;
                    }
            File.WriteAllText(Path.Combine(dir, "releve-routes.csv"), rb.ToString());
            // vue de dessus, nord en haut (z vers le haut de l'image)
            var go2 = new GameObject("MWCoop releve");
            try
            {
                Camera cam = go2.AddComponent<Camera>();
                cam.enabled = false; cam.orthographic = true; cam.orthographicSize = r; cam.nearClipPlane = 1f; cam.farClipPlane = 900f;
                go2.transform.position = new Vector3(c.x, 500f, c.z); go2.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
                var rt = new RenderTexture(2048, 2048, 24);
                cam.targetTexture = rt; cam.Render();
                RenderTexture.active = rt;
                var tex = new Texture2D(2048, 2048, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, 2048, 2048), 0, 0); tex.Apply();
                RenderTexture.active = null; cam.targetTexture = null;
                File.WriteAllBytes(Path.Combine(dir, "releve-vue.png"), tex.EncodeToPNG());
                Object.Destroy(tex); rt.Release(); Object.Destroy(rt);
            }
            finally { Object.Destroy(go2); }
            L(string.Format(ci, "releve : {0} points autour du cinema ({1:F1}, {2:F1}, cap {3:F1}), rayon {4} m -> {5}", n, c.x, c.z, yaw, r, dir));
        }
    }
}
