using System.Collections.Generic;
using HutongGames.PlayMaker;
using UnityEngine;

namespace MWCoop
{
    // Carnet de notes du copilote en boutique et en main (JD 07/11 ; donnees : Carnet.cs ; page : CarnetSivu.cs).
    //  - objet ("carnet", 59 mk, lampe comprise) : a l'Osuuskauppa sur la planche sous les KARTTURI ; au Cardinal (My Winter Car)
    //    dans la case sous les KARTTURI. Comme les autres objets : pris en main, sauve, suivi par Props.
    //  - copilote seulement (le pilote seul est mis de cote par JD) : tenu en main et assis en passager, F = le lever. Ce qu'on
    //    voit : une copie devant les yeux, en bas a gauche de l'image, qui remonte quand on baisse les yeux ; F ou clic droit = le
    //    baisser. Comme le KARTTURI : la main du jeu est mise en pause, l'objet reste le sien (les autres le voient tenu devant
    //    l'avatar, du cote gauche).
    //  - page : chaque carnet a moins de 15 m de la camera dessine la sienne (le carnet leve ici : 30 fois par seconde, avec le
    //    carnet actif du joueur ; les autres : 6 fois par seconde) ; plus loin, papier seul (la page est liberee).
    public static partial class Kauppa
    {
        public const string KCarnet = "carnet";
        public const int CarnetPrice = 59;

        static readonly Dictionary<Esine, CarnetSivu.Page> pages = new Dictionary<Esine, CarnetSivu.Page>();
        static Material cnPaper;

        // ---------------------------------------------------------------- modele (debout, page vers -z, spirale a gauche)
        static void CarnetModel(GameObject g, out Vector3 size, out Transform page)
        {
            page = null;
            if (ModelFile("carnet", g, out size)) { page = g.transform.Find("page"); if (page != null) page.GetComponent<Renderer>().sharedMaterial = CarnetPaper(); return; }
            size = new Vector3(0.21f, 0.3f, 0.012f);
            Piece(g, PrimitiveType.Cube, Vector3.zero, Vector3.zero, size, Plain(new Color(0.86f, 0.85f, 0.8f)));
            GameObject q = Piece(g, PrimitiveType.Quad, new Vector3(0f, 0f, -size.z / 2f - 0.0008f), Vector3.zero, new Vector3(size.x * 0.97f, size.y * 0.97f, 1f), CarnetPaper());
            q.name = "page"; page = q.transform;
        }

        // papier seul (carnet loin, en rayon) : la page du mod sans encre
        static Material CarnetPaper()
        {
            if (cnPaper != null) return cnPaper;
            Texture2D t = Res("carnet-page.png", TextureFormat.RGB24);
            t.wrapMode = TextureWrapMode.Clamp;
            Material b = GameMat("wood_panel7");
            cnPaper = new Material(b != null ? b.shader : Shader.Find("Legacy Shaders/Diffuse")) { mainTexture = t, color = Color.white, name = "carnet_papier" };
            return cnPaper;
        }

        // ---------------------------------------------------------------- vente
        // Osuuskauppa (RallyOffers) : deux carnets debout sur la planche sous les KARTTURI, page vers le client
        static void CarnetOffer(float x, float y, float zc, float tagX)
        {
            var o = new Offer { Item = KCarnet, Price = CarnetPrice, Fr = "CARNET DE COPILOTE (+ LAMPE)", En = "CO-DRIVER'S NOTEBOOK (+ LAMP)" };
            foreach (float z in new[] { -1.78f, -1.32f }) Display(o, KCarnet, new Vector3(x + 0.04f, y, z), 90f, -10f);
            OfferCol(o, new Vector3(x, y + 0.16f, zc), new Vector3(0.38f, 0.34f, 1.0f));
            if (!mats.ContainsKey("hlcarnet")) OwnMat("hlcarnet", "hintalappu-carnet.png");
            Plane("hlcarnet", new Vector3(tagX, y - 0.035f, zc), 0.12f, 0.06f, Vector3.left);
            L("rallye : carnet du copilote " + CarnetPrice + " mk");
        }

        // ---------------------------------------------------------------- en main
        static Esine HeldCarnet()
        {
            foreach (Esine e in esineet)
                if (e.Kind == KCarnet && e.Go != null && e.Go.transform.parent != null && Game.UnderPlayer(e.Go.transform)) return e;
            return null;
        }

        static Esine cnEq;
        static int cnStage, cnSince, cnReenable = -1;   // 0 non, 1 en attente de la main du jeu, 2 leve
        static PlayMakerFSM cnFsm;
        static bool cnRestart, cnKin, cnTestUp;
        static Vector3 cnPos; static Quaternion cnRot;
        static float cnLift;
        public static bool CarnetUp { get { return cnStage == 2; } }

        static void TickCarnet(float now)
        {
            if (cnReenable >= 0 && Time.frameCount >= cnReenable)
            {
                cnReenable = -1;
                if (cnFsm != null) { cnFsm.enabled = true; cnFsm.Fsm.RestartOnEnable = cnRestart; }
                cnFsm = null;
            }
            Esine held = HeldCarnet();
            if (cnStage == 0)
            {
                if (held != null && Seats.Seated && eqStage == 0 && !KameraBusy && gpsStage == 0 && (Input.GetKeyDown(Keys.CameraViewfinder) || cnTestUp)) { cnEq = held; cnStage = 1; cnSince = Time.frameCount; cnTestUp = false; }
            }
            else if (cnEq == null || cnEq.Go == null || held != cnEq || !Seats.Seated) CarnetDown();
            else if (cnStage == 1)
            {
                PlayMakerFSM f = PickUpFsm();
                if (f == null || Time.frameCount - cnSince > 60) { L("carnet : pas leve (main du jeu " + (f != null ? f.ActiveStateName : "introuvable") + ")"); cnStage = 0; cnEq = null; }
                else if (f.ActiveStateName == "Part picked" || f.ActiveStateName == "Item picked")
                {
                    cnFsm = f; cnRestart = f.Fsm.RestartOnEnable;
                    f.Fsm.RestartOnEnable = false;
                    f.enabled = false;
                    Transform t = cnEq.Go.transform;
                    cnPos = t.localPosition; cnRot = t.localRotation;
                    if (cnEq.Rb != null) { cnKin = cnEq.Rb.isKinematic; cnEq.Rb.isKinematic = true; }
                    cnStage = 2; cnLift = 0f;
                    Carnet.Refresh(false);   // (le lanceur a pu changer le carnet actif)
                    CarnetView(true);
                    L("carnet : leve (" + (Carnet.Active != null ? "carnet « " + Carnet.Active.Name + " »" : "notes automatiques") + ")");
                }
            }
            else if (!KameraBusy && !cnWheel && (Input.GetKeyDown(Keys.CameraViewfinder) || Input.GetMouseButtonDown(1))) CarnetDown();
            TickCarnetEdit(now);
            TickCarnetNet(now);
            TickCarnetLamp(now);
            TickPages(now);
        }

        // Pose (a chaque image, apres la camera) : la copie devant les yeux, en bas a gauche, plus haut quand on baisse les yeux ;
        // l'objet (ce que voient les autres) a hauteur de poitrine, du cote gauche, page vers les yeux.
        public static void LateCarnet()
        {
            if (cnStage != 2 || cnEq == null || cnEq.Go == null) return;
            Transform eye = EyeT;
            if (eye == null) return;
            float down = Mathf.Clamp01((Vector3.Angle(eye.forward, Vector3.down) < 90f ? 90f - Vector3.Angle(eye.forward, Vector3.down) : 0f) / 50f);
            cnLift = Mathf.Lerp(cnLift, down, 1f - Mathf.Exp(-10f * Time.deltaTime));
            if (cnView != null)
            {
                Vector3 p = eye.position + eye.forward * Mathf.Lerp(0.40f, 0.34f, cnLift) - eye.up * Mathf.Lerp(0.20f, 0.06f, cnLift) - eye.right * Mathf.Lerp(0.13f, 0.03f, cnLift);
                cnView.transform.position = p;
                cnView.transform.rotation = Quaternion.LookRotation(p - eye.position, eye.up) * Quaternion.Euler(0f, 0f, Mathf.Lerp(7f, 2f, cnLift));
            }
            Transform car = Seats.SeatedCar;
            Vector3 up = car != null ? car.up : Vector3.up, fw = Vector3.ProjectOnPlane(eye.forward, up);
            if (fw.sqrMagnitude < 1e-4f) fw = car != null ? car.forward : Vector3.forward;
            fw.Normalize();
            Vector3 right = Vector3.Cross(up, fw).normalized;
            Vector3 q = eye.position + fw * 0.33f - up * Mathf.Lerp(0.3f, 0.24f, cnLift) - right * 0.08f;
            Transform t = cnEq.Go.transform;
            t.position = q;
            t.rotation = Quaternion.LookRotation(q - eye.position, up);
        }

        static GameObject cnView;
        static readonly List<Renderer> cnHidden = new List<Renderer>();
        static void CarnetView(bool on)
        {
            if (cnView != null) { UnityEngine.Object.Destroy(cnView); cnView = null; }
            foreach (Renderer r in cnHidden) if (r != null) r.enabled = true;
            cnHidden.Clear();
            if (!on || cnEq == null || cnEq.Go == null) return;
            Vector3 size; Transform page;
            cnView = Model(KCarnet, out size, out page);
            cnView.name = "carnet (nakyma)";
            int layer = cnEq.Go.layer;
            foreach (Transform c in cnView.GetComponentsInChildren<Transform>(true)) c.gameObject.layer = layer;
            foreach (Renderer r in cnEq.Go.GetComponentsInChildren<Renderer>()) if (r.enabled) { r.enabled = false; cnHidden.Add(r); }
            cnViewPage = page != null ? page.GetComponent<Renderer>() : null;
        }
        static Renderer cnViewPage;

        static void CarnetDown()
        {
            if (cnStage == 2 && cnEq != null && cnEq.Go != null)
            {
                Transform t = cnEq.Go.transform;
                t.localPosition = cnPos; t.localRotation = cnRot;
                if (cnEq.Rb != null) cnEq.Rb.isKinematic = cnKin;
                if (cnFsm != null) cnReenable = Time.frameCount + 1;
                SayCarnetDown(cnEq);
                L("carnet : baisse");
            }
            if (cnStage == 2) CarnetView(false);
            cnStage = 0; cnEq = null;
        }

        // ---------------------------------------------------------------- pages
        static float pagesLook;
        static void TickPages(float now)
        {
            Transform eye = EyeT;
            if (eye == null) return;
            bool look = now >= pagesLook;
            if (look) pagesLook = now + 0.5f;
            Esine inHand = HeldCarnet();
            foreach (Esine e in esineet)
            {
                if (e.Kind != KCarnet || e.Go == null || e.Screen == null) continue;
                bool mine = e == cnEq && cnStage == 2;
                CarnetSivu.Page p;
                bool has = pages.TryGetValue(e, out p);
                float d2 = (e.Go.transform.position - eye.position).sqrMagnitude;
                if (look && !mine && has && d2 > 18f * 18f)
                {
                    // loin : page liberee, papier seul
                    CarnetSivu.Release(p); pages.Remove(e);
                    Renderer r0 = e.Screen.GetComponent<Renderer>(); if (r0 != null) r0.sharedMaterial = CarnetPaper();
                    continue;
                }
                if (!has)
                {
                    if (!mine && d2 > 15f * 15f) continue;
                    Material b = GameMat("wood_panel7");
                    p = CarnetSivu.NewPage(b != null ? b.shader : null);
                    if (p == null) continue;
                    pages[e] = p;
                    Renderer r = e.Screen.GetComponent<Renderer>(); if (r != null) r.sharedMaterial = p.Mat;
                }
                if (mine && cnViewPage != null && cnViewPage.sharedMaterial != p.Mat) cnViewPage.sharedMaterial = p.Mat;
                p.Book = mine || e == inHand ? Carnet.Active : RemoteBook(e);   // (le mien, meme baisse : mon carnet actif)
                // vitesse : celle de la voiture ou on est assis (carnet tenu ici), sinon celle de l'objet
                Vector3 v = Vector3.zero;
                if (mine || Game.UnderPlayer(e.Go.transform)) { Transform car = Seats.SeatedCar; Rigidbody cr = car != null ? car.GetComponent<Rigidbody>() : null; if (cr != null) v = cr.velocity; }
                else if (e.Rb != null && !e.Rb.isKinematic) v = e.Rb.velocity;
                CarnetSivu.Tick(p, e.Go.transform.position, v, now, mine);
            }
        }

        // Core.OnGUI (DrawKameraHint) : rappel des touches, carnet en main
        static bool DrawCarnetHint()
        {
            if (KameraBusy) return false;
            Esine g = HeldCarnet();
            if (g == null) return false;
            DrawCarnetWheel();
            if (cnWheel) return true;
            string s = cnStage == 2 ? Keys.Label(Keys.CameraViewfinder) + Lang.T(" : BAISSER  -  ", ": LOWER  -  ") + Keys.Label(Keys.CarnetWheel) + Lang.T(" : NOTER  -  ", ": WRITE  -  ")
                       + Keys.Label(Keys.CarnetErase) + Lang.T(" : EFFACER  -  ", ": ERASE  -  ") + Keys.Label(Keys.CarnetLamp) + Lang.T(" : LAMPE", ": LAMP")
                     : Seats.Seated ? Lang.T("CARNET DE COPILOTE  -  ", "CO-DRIVER'S NOTEBOOK  -  ") + Keys.Label(Keys.CameraViewfinder) + Lang.T(" : LE LEVER", ": HOLD IT UP")
                     : Lang.T("CARNET DE COPILOTE  -  ASSIEDS-TOI EN PASSAGER POUR LE LIRE", "CO-DRIVER'S NOTEBOOK  -  SIT AS A PASSENGER TO READ IT");
            Style.Text(new Rect(0, Screen.height - Style.Px(cnStage == 2 ? 40 : 120), Screen.width, Style.Px(30)), s, cnStage == 2 ? 16 : 20, TextAnchor.MiddleCenter, new Color(1f, 1f, 1f, cnStage == 2 ? 0.6f : 0.85f));
            return true;
        }

        static void OnLevelLoadedCarnet()
        {
            foreach (CarnetSivu.Page p in pages.Values) CarnetSivu.Release(p);
            pages.Clear(); cnStage = 0; cnEq = null; cnFsm = null; cnReenable = -1; cnView = null; cnHidden.Clear(); cnViewPage = null; cnPaper = null; cnTest = 0;
            CarnetSivu.OnLevelLoaded();
            OnLevelLoadedCarnetNet();
        }

        // ---------------------------------------------------------------- essais
        // [Test] Autotest=carnet (seul) : carnet cree et pris en main (25-28 s), assis a l'avant de la SORBET / SATSUMA (31 s), leve
        // (34 s) ; puis la voiture (cinematique) roule la SS1 par troncons a 12 m/s autour du depart, du pont, du premier carre et de
        // l'arrivee : pages en PNG (dumps\carnet-page-<lieu>-<n>.png, lues sur le GPU) 30 m avant, sur la note, 12 m apres ; une
        // capture de l'ecran sur la note (dumps\carnet-vue-<lieu>.png) ; etat du suivi au journal.
        static int cnTest, cnLeg = -1;
        static float cnLegT, cnShotM;
        static readonly List<KeyValuePair<string, float>> cnLegs = new List<KeyValuePair<string, float>>();
        public static void TestCarnet(string mode, float t)
        {
            if (mode != "carnet" || !Active || root == null) return;
            if (cnTest == 0 && t > 25f && EyeT != null) { cnTest = 1; CreateItem("essaicarnet", KCarnet, EyeT.position + EyeT.forward * 0.6f, Quaternion.identity, ""); }
            if (cnTest == 1 && t > 28f)
            {
                cnTest = 2;
                Esine k = esineet.FindLast(e => e.Kind == KCarnet); PlayMakerFSM pf = PickUpFsm();
                if (k != null && pf != null) { k.Rb.isKinematic = false; k.Go.transform.position = EyeT.position + EyeT.forward * 0.6f; pf.FsmVariables.GetFsmGameObject("PickedObject").Value = k.Go; Game.SetState(pf, PickSetState); }
                L("essai carnet : objet " + (k != null ? k.Id : "absent") + ", main " + (pf != null ? pf.ActiveStateName : "?"));
            }
            string carName = Game.Msc ? "SATSUMA" : "SORBET";
            if (cnTest == 2 && t > 31f) { cnTest = 3; L("essai carnet : " + Seats.TestSit(carName, 0) + ", tenu " + (HeldCarnet() != null)); }
            if (cnTest == 3 && t > 34f) { cnTest = 4; cnTestUp = true; }
            if (cnTest == 4 && t > 37f && Kartturi.Ready)
            {
                cnTest = 5;
                cnLegs.Clear();
                List<Carnet.Note> l = Carnet.AutoNotes(0);
                Carnet.Note pont = l.Find(x => x.Type == "pont"), carre = l.Find(x => x.Type == "carre"), fin = l.Find(x => x.Type == "arrivee");
                cnLegs.Add(new KeyValuePair<string, float>("depart", 40f));
                if (pont != null) cnLegs.Add(new KeyValuePair<string, float>("pont", pont.M));
                if (carre != null) cnLegs.Add(new KeyValuePair<string, float>("carre", carre.M));
                if (fin != null) cnLegs.Add(new KeyValuePair<string, float>("arrivee", fin.M));
                cnLeg = 0; cnLegT = t; cnShotM = -1f;
                L("essai carnet : leve " + CarnetUp + ", troncons " + string.Join(", ", cnLegs.ConvertAll(x => x.Key + " " + x.Value.ToString("F0")).ToArray()));
            }
            if (cnTest != 5 || cnLeg < 0) return;
            Rigidbody car = VehicleSync.Body(carName);
            Transform walker = car == null && Game.Msc ? Game.PlayerT : null;
            if (car == null && walker == null) return;
            if (cnLeg >= cnLegs.Count) { cnTest = 6; if (car != null) car.isKinematic = false; L("essai carnet : fin"); return; }
            float target = cnLegs[cnLeg].Value, m0 = Mathf.Max(0f, target - 54f), speed = 12f;
            float m = m0 + (t - cnLegT) * speed;
            Vector3 p, d;
            if (Kartturi.StagePoint(0, m, out p, out d))
            {
                if (car != null) { car.isKinematic = true; car.transform.position = p + Vector3.up * 0.6f; car.transform.rotation = Quaternion.LookRotation(d, Vector3.up); car.velocity = Vector3.zero; }
                else { var cc = walker.GetComponent<CharacterController>(); if (cc != null) cc.enabled = false; walker.position = p + Vector3.up * 1.0f; walker.rotation = Quaternion.LookRotation(d, Vector3.up); }
            }
            string dir = System.IO.Path.Combine(Log.DataDir, "dumps");
            foreach (float off in new[] { -30f, 0f, 12f })
            {
                float at = target + off;
                if (cnShotM < at && m >= at + 2f)   // (2 m de plus : la page a suivi)
                {
                    cnShotM = at;
                    Esine e = cnEq; CarnetSivu.Page pg;
                    string tag = cnLegs[cnLeg].Key + "-" + (off < 0 ? "avant" : off > 0 ? "apres" : "sur");
                    if (e != null && pages.TryGetValue(e, out pg))
                    {
                        CarnetSivu.SavePng(pg, System.IO.Path.Combine(dir, "carnet-page-" + tag + ".png"));
                        L("essai carnet : page " + tag + " a " + m.ToString("F0") + " m, suivi " + (pg.Stage >= 0 ? Kartturi.StageName(pg.Stage) + " " + pg.M.ToString("F0") + " m" : "aucun"));
                    }
                    else L("essai carnet : pas de page (" + tag + ", leve " + CarnetUp + ")");
                    if (off == 0f) Application.CaptureScreenshot(System.IO.Path.Combine(dir, "carnet-vue-" + cnLegs[cnLeg].Key + ".png"));
                }
            }
            if (m > target + 20f) { cnLeg++; cnLegT = t; cnShotM = -1f; }
        }
    }
}
