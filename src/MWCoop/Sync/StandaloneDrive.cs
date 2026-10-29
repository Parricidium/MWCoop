using System;
using System.Collections.Generic;
using System.Reflection;
using HutongGames.PlayMaker;
using UnityEngine;

namespace MWCoop
{
    // Contrat de conduite de la voiture standalone (spec du 28/10, §2 et « Conduite ») : ce que fait l'automate DriveTrigger des
    // voitures de MSC (releve sur la Ruscko et le bateau, ref/msc-v.250908-04), fait par notre code -- rien de copie :
    //  - zone d'entree : a cote de la portiere conducteur (la caisse est fermee : boites de collision), voiture presque arretee et
    //    sur ses roues -> GUIdrive + « ENTER DRIVING MODE » ; touche du jeu DrivingMode (cInput) pour monter ;
    //  - au volant : PlayerStop, PlayerCurrentVehicle (« R5Maxi » : nom a nous, aucun automate du jeu ne le reconnait -- avec
    //    « Ruscko », les phares de la vraie Ruscko repondraient), PlayerInCar de l'automate Crouch (camera basse, joueur sans
    //    controle), CharacterController coupe, PLAYER range sous le pivot du conducteur, yeux poses au point de vue du modele
    //    (pivot de tete + EyeFromPivot du jeu, comme la copie) ; touches du jeu lues par cInput (gaz, frein, direction, frein a
    //    main, embrayage, rapport +/-) ;
    //  - descente : meme touche, voiture arretee (< 0,5 m/s) ; joueur pose au sol a cote de la portiere, tout remis ;
    //  - tableau de bord : boutons du modele (Knobs) -- contact (rouge : clic tenu = demarreur, clic moteur tournant = arret),
    //    phares (blanc : clic) ; vises au centre de l'ecran comme les commandes du jeu ;
    //  - moteur coupe a la naissance (Drivetrain.Start le demarre tout seul), son de demarrage de l'addon (VazSound).
    public static partial class StandaloneCar
    {
        public const string VehicleName = "R5Maxi";
        static VazCar.Built driving;
        static VazCar.Built prompted;        // (voiture dont nous avons pose le texte d'entree)
        static int hovered = -1;             // (bouton du tableau de bord vise, texte pose par nous)
        static float enteredAt, exitBlock, footHeight = -1f;
        static FieldInfo engineVelo;

        public static bool IsDriving { get { return driving != null; } }

        // Camera du joueur (FPSCamera/FPSCamera, celle qui rend) : PlayerSync.LocalCamera n'est remplie qu'en partie a plusieurs
        // (essai monte du 28/10 : vide en solo, yeux jamais recales)
        static Transform playerCam;
        static Transform PlayerCam()
        {
            if (playerCam != null) return playerCam;
            GameObject pl = Game.PlayerGo;
            if (pl == null) return null;
            playerCam = pl.transform.Find("Pivot/AnimPivot/Camera/FPSCamera/FPSCamera") ?? pl.transform.Find("Pivot/AnimPivot/Camera/FPSCamera");
            return playerCam;
        }

        // ---------------------------------------------------------------- cInput (touches du jeu, reglables dans ses options)
        static MethodInfo axisRaw, buttonDown;
        static bool cinTried;
        static void Cin()
        {
            if (cinTried) return;
            cinTried = true;
            Type t = Type.GetType("cInput, cInput");
            if (t == null) foreach (Assembly a in AppDomain.CurrentDomain.GetAssemblies()) { t = a.GetType("cInput"); if (t != null) break; }
            if (t == null) { Log.Warn("standalone : cInput introuvable (clavier de secours)"); return; }
            axisRaw = t.GetMethod("GetAxisRaw", new[] { typeof(string) });
            buttonDown = t.GetMethod("GetButtonDown", new[] { typeof(string) });
            buttonHeld = t.GetMethod("GetButton", new[] { typeof(string) });
            // (noms des touches et axes du jeu que lit la standalone : definis dans cInput ?)
            MethodInfo ax = t.GetMethod("IsAxisDefined", new[] { typeof(string) }), kd = t.GetMethod("IsKeyDefined", new[] { typeof(string) });
            var sb = new System.Text.StringBuilder("standalone : cInput");
            try
            {
                foreach (string n in new[] { "Throttle", "Brake", "Horizontal", "Handbrake", "Clutch" }) sb.Append(" ").Append(n).Append(ax != null && (bool)ax.Invoke(null, new object[] { n }) ? "" : " (ABSENT)");
                foreach (string n in new[] { "ShiftUp", "ShiftDown", "DrivingMode" }) sb.Append(" ").Append(n).Append(kd != null && (bool)kd.Invoke(null, new object[] { n }) ? "" : " (ABSENT)");
            }
            catch (Exception e) { sb.Append(" : ").Append(e.Message); }
            Log.Info(sb.ToString());
        }
        public static float Axis(string n)
        {
            Cin();
            if (axisRaw == null) return 0f;
            try { float v = (float)axisRaw.Invoke(null, new object[] { n }); return float.IsNaN(v) ? 0f : Mathf.Clamp(v, -1f, 1f); } catch { return 0f; }
        }
        static MethodInfo buttonHeld;
        public static bool Held(string n)
        {
            Cin();
            if (buttonHeld == null) return false;
            try { return (bool)buttonHeld.Invoke(null, new object[] { n }); } catch { return false; }
        }
        public static bool Down(string n)
        {
            Cin();
            if (buttonDown == null) return n == "DrivingMode" && Input.GetKeyDown(KeyCode.Return);
            try { return (bool)buttonDown.Invoke(null, new object[] { n }); } catch { return false; }
        }

        // ---------------------------------------------------------------- construction (appelee par Build)
        static void SetupDrive(VazCar.Built b, State st, Dictionary<string, Vector3> inf)
        {
            VazCar.Model m = b.M;
            var head = new GameObject("DriverPivot").transform;
            head.SetParent(b.Car.transform, false);
            head.localPosition = m.Head;
            st.Pivot = LeanRig(b, st, head);
            Bounds cb; st.HalfWidth = VazCar.ModelBounds(m, "carrosserie.p205", out cb) ? Mathf.Max(Mathf.Abs(cb.min.x), Mathf.Abs(cb.max.x)) : 0.85f;
            // boutons visibles du tableau de bord (cylindres colores, comme sur la copie)
            VazCar.Knob[] kns = KnobsOf(m);
            if (kns != null)
                for (int i = 0; i < kns.Length; i++)
                {
                    VazCar.Knob kn = kns[i];
                    GameObject g = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                    UnityEngine.Object.DestroyImmediate(g.GetComponent<Collider>());
                    g.name = "bouton_" + i;
                    g.transform.SetParent(b.Body, false);
                    g.transform.localPosition = kn.At;
                    g.transform.localRotation = Quaternion.FromToRotation(Vector3.up, kn.Normal);
                    g.transform.localScale = new Vector3(0.017f, 0.006f, 0.017f);
                    Shader ks = Shader.Find("Legacy Shaders/Specular") ?? Shader.Find("Specular");
                    if (ks != null) g.GetComponent<MeshRenderer>().sharedMaterial = new Material(ks) { color = kn.Col, name = "bouton_" + i };
                }
            // phares : deux projecteurs aux feux avant du modele
            var heads = new List<Light>();
            foreach (string n in new[] { "headlight_l", "headlight_r" })
            {
                Vector3 p;
                if (!inf.TryGetValue(n, out p)) continue;
                var lg = new GameObject("phare_" + n.Substring(10)).transform;
                lg.SetParent(b.Body, false);
                lg.localPosition = p + new Vector3(0f, 0f, 0.08f);
                lg.localRotation = Quaternion.Euler(4f, 0f, 0f);
                Light l = lg.gameObject.AddComponent<Light>();
                l.type = LightType.Spot; l.range = 45f; l.spotAngle = 70f; l.intensity = 2.2f; l.color = new Color(1f, 0.96f, 0.86f); l.enabled = false;
                heads.Add(l);
            }
            st.Heads = heads.ToArray();
            // portieres sur charniere (JD 28/10 : « les portes ne s'ouvrent pas ») : pivot au point de charniere du modele (infos.txt
            // door_dside_f / door_pside_f, arete avant), axe vertical, la portiere du modele accrochee dessous ; clic pour ouvrir (Doors)
            st.Doors = new Transform[2]; st.DoorCenter = new Vector3[2]; st.DoorOpen = new bool[2]; st.DoorAngle = new float[2];
            string[][] dd = { new[] { "portiere_g.p205", "door_dside_f" }, new[] { "portiere_d.p205", "door_pside_f" } };
            for (int i = 0; i < 2; i++)
            {
                if (!System.IO.File.Exists(System.IO.Path.Combine(m.Dir, dd[i][0]))) continue;
                Vector3 h;
                if (!inf.TryGetValue(dd[i][1], out h)) { VazCar.AddModel(b, b.Body, dd[i][0]); continue; }   // (sans charniere : portiere fixe)
                var piv = new GameObject("portiere_" + i).transform;
                piv.SetParent(b.Body, false); piv.localPosition = h;
                var inner = new GameObject("pieces").transform;
                inner.SetParent(piv, false); inner.localPosition = -h;
                VazCar.AddModel(b, inner, dd[i][0]);
                Bounds db; st.DoorCenter[i] = VazCar.ModelBounds(m, dd[i][0], out db) ? db.center : h;
                st.Doors[i] = piv;
                // (portiere conducteur : elle ferme l'habitacle pour le joueur et tourne avec la portiere -- ouverte, on passe)
                if (buildPlayerLayer >= 0 && db.size.y > 0.3f)
                {
                    if (i == 0) st.DriverDoor = db;
                    st.DoorBox[i] = db;
                    var dc = new GameObject("joueur_portiere"); dc.layer = buildPlayerLayer;
                    dc.transform.SetParent(inner, false);
                    var bc = dc.AddComponent<BoxCollider>(); bc.center = db.center; bc.size = db.size;
                }
            }
            Shell(b, st);
            // son moteur de l'addon (boucles + demarrage) au moteur du modele
            var eng = new GameObject("moteur_son").transform;
            eng.SetParent(b.Body, false); eng.localPosition = m.Engine;
            b.Sound = VazSound.Create(b.Car, eng, m);
            st.EngineOn = false;
            Lamps(b, st);
            st.Speedo = Gauge.Make(b, inf, "v", "cadran_vitesse.png");
            st.Tacho = Gauge.Make(b, inf, "r", "cadran_regime.png");
        }

        // Boutons du tableau de bord : ceux du modele (copie), sauf la R5 standalone -- JD 28/10, capture annotee : en ligne sur le dessus
        // de la console centrale, sous la barre blanche, de gauche a droite contact (rouge), starter (bleu), phares (blanc), warning
        // (orange) ; points releves par rayons de la camera du conducteur sur le modele (dessus de console a y 0,619, face vers le haut).
        static readonly Vector3 ConsoleUp = new Vector3(0f, 1f, -0.06f).normalized;
        static readonly VazCar.Knob[] R5Knobs = {
            new VazCar.Knob { At = new Vector3(0.036f, 0.623f, 0.494f), Normal = ConsoleUp, Col = new Color(0.80f, 0.06f, 0.05f) },
            new VazCar.Knob { At = new Vector3(0.085f, 0.623f, 0.494f), Normal = ConsoleUp, Col = new Color(0.10f, 0.25f, 0.85f) },
            new VazCar.Knob { At = new Vector3(0.137f, 0.623f, 0.490f), Normal = ConsoleUp, Col = new Color(0.92f, 0.92f, 0.90f) },
            new VazCar.Knob { At = new Vector3(0.183f, 0.623f, 0.485f), Normal = ConsoleUp, Col = new Color(0.95f, 0.45f, 0.05f) } };
        static VazCar.Knob[] KnobsOf(VazCar.Model m) { return m == VazCar.R5 ? R5Knobs : m.Knobs; }

        // ---------------------------------------------------------------- compteurs
        // Compteurs fonctionnels (JD 28/10 : « importer les compteurs et qu'ils soient fonctionnels ») : comme le mod rFactor, une
        // aiguille (aiguille.png) tournee sur la face du cadran (cadran_vitesse.png / cadran_regime.png) autour de son centre ;
        // reperes et angles dans infos.txt (outils/rf1/r5maxi_cadrans.py) : cadran_<v|r>_centre, _haut, _droite (demi-cote de la
        // texture), _angles (degres depuis le haut, sens horaire), _valeurs. Face et aiguille sans eclairage (lisibles la nuit).
        public class Gauge
        {
            public Transform Needle; public Vector3 Axis; public float A0, A1, V0, V1, Shown = -1f;

            public static Gauge Make(VazCar.Built b, Dictionary<string, Vector3> inf, string k, string faceFile)
            {
                Vector3 c, up, right, ang, val;
                if (!inf.TryGetValue("cadran_" + k + "_centre", out c) || !inf.TryGetValue("cadran_" + k + "_haut", out up) || !inf.TryGetValue("cadran_" + k + "_droite", out right)
                    || !inf.TryGetValue("cadran_" + k + "_angles", out ang) || !inf.TryGetValue("cadran_" + k + "_valeurs", out val)) return null;
                Texture2D face = Png(b.M, faceFile), needle = Png(b.M, "aiguille.png");
                if (face == null || needle == null) return null;
                Vector3 n = Vector3.Cross(right, up).normalized;   // (vers l'avant : le conducteur regarde le cadran dans ce sens)
                Shader unlit = Shader.Find("Unlit/Texture"), transp = Shader.Find("Unlit/Transparent") ?? Shader.Find("Unlit/Transparent Cutout");
                var fq = Quad("cadran_" + k, b.Body, c - n * 0.001f, Vector3.zero, up, right);
                fq.GetComponent<MeshRenderer>().sharedMaterial = new Material(unlit) { mainTexture = face, color = new Color(0.85f, 0.85f, 0.85f) };
                var pivot = new GameObject("aiguille_" + k).transform;
                pivot.SetParent(b.Body, false); pivot.localPosition = c - n * 0.003f;
                var nq = Quad("pieces", pivot, Vector3.zero, Vector3.zero, up, right);
                nq.GetComponent<MeshRenderer>().sharedMaterial = new Material(transp) { mainTexture = needle };
                Log.Info("standalone : compteur " + (k == "v" ? "vitesse" : "regime") + " pose (" + val.x + "-" + val.y + ", " + ang.x + "-" + ang.y + " degres)");
                return new Gauge { Needle = pivot, Axis = n, A0 = ang.x, A1 = ang.y, V0 = val.x, V1 = val.y };
            }

            public void Set(float value)
            {
                if (Needle == null) return;
                float v = Mathf.Clamp(value, V0, V1 + (V1 - V0) * 0.05f);
                if (Mathf.Abs(v - Shown) < 0.002f * (V1 - V0)) return;
                Shown = v;
                // (sens horaire vu par le conducteur, qui regarde le long d'Axis : angle NEGATIF autour d'Axis -- capture de pres du 28/10,
                // aiguilles a 100 km/h et 4 000 tr/min pointees a droite au lieu de la gauche avec l'angle positif)
                Needle.localRotation = Quaternion.AngleAxis(-(A0 + (A1 - A0) * (v - V0) / (V1 - V0)), Axis);
            }

            // carre plat centre en c (repere du parent), cotes 2 x up et 2 x right, texture entiere (haut de l'image vers up)
            static GameObject Quad(string name, Transform parent, Vector3 c, Vector3 unused, Vector3 up, Vector3 right)
            {
                var g = new GameObject(name);
                g.transform.SetParent(parent, false); g.transform.localPosition = c;
                var m = new Mesh { name = name };
                m.vertices = new[] { -right - up, right - up, right + up, -right + up };
                m.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) };
                // (face visible depuis le conducteur, cote -Cross(right, up) : triangles tournes vers lui)
                m.triangles = new[] { 0, 2, 1, 0, 3, 2 };
                m.RecalculateNormals(); m.RecalculateBounds();
                g.AddComponent<MeshFilter>().sharedMesh = m;
                g.AddComponent<MeshRenderer>();
                return g;
            }

            static Texture2D Png(VazCar.Model m, string file)
            {
                string f = System.IO.Path.Combine(m.Dir, file);
                if (!System.IO.File.Exists(f)) { Log.Info("standalone : " + file + " absent (compteurs du modele)"); return null; }
                var t = new Texture2D(2, 2, TextureFormat.RGBA32, true) { name = "standalone_" + file, wrapMode = TextureWrapMode.Clamp, anisoLevel = 4 };
                t.LoadImage(System.IO.File.ReadAllBytes(f));
                return t;
            }
        }

        // [Test] StandaloneSonde=x,y|x,y|... (pixels d'une capture 1280 x 720, depuis le haut a gauche) : rayons de la camera du
        // conducteur sur le modele (colliders de maillage poses le temps du releve) -> point et normale, repere de la voiture.
        // (colliders poses a un appel, rayons a l'appel suivant : la physique ne les connait qu'apres son pas ; vrai quand c'est fini)
        static List<MeshCollider> probeCols; static bool probeDone;
        static bool Probe(VazCar.Built b)
        {
            string spec = Config.Get("Test", "StandaloneSonde", "");
            Transform ct = PlayerCam(); Camera cam = ct != null ? ct.GetComponent<Camera>() : null;
            if (probeDone || spec.Length == 0 || cam == null) return true;
            if (probeCols == null)
            {
                probeCols = new List<MeshCollider>();
                foreach (MeshFilter mf in b.Body.GetComponentsInChildren<MeshFilter>()) { var mc = mf.gameObject.AddComponent<MeshCollider>(); mc.sharedMesh = mf.sharedMesh; probeCols.Add(mc); }
                return false;
            }
            var added = probeCols;
            Log.Info("standalone : sonde, camera " + cam.pixelWidth + " x " + cam.pixelHeight + " en " + b.Car.transform.InverseTransformPoint(ct.position).ToString("F3") + " vers " + b.Car.transform.InverseTransformDirection(ct.forward).ToString("F2") + ", champ " + cam.fieldOfView.ToString("F0"));
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            foreach (string pt in spec.Split('|'))
            {
                string[] xy = pt.Split(',');
                if (xy.Length != 2) continue;
                float px = float.Parse(xy[0], ci) * cam.pixelWidth / 1280f, py = cam.pixelHeight - float.Parse(xy[1], ci) * cam.pixelHeight / 720f;
                Ray r = cam.ScreenPointToRay(new Vector3(px, py, 0f));
                float best = 9f; RaycastHit bh = new RaycastHit(); bool any = false;
                foreach (MeshCollider mc in added) { RaycastHit h; if (mc.Raycast(r, out h, 3f) && h.distance < best) { best = h.distance; bh = h; any = true; } }
                Log.Info("standalone : sonde " + pt + " -> " + (any ? bh.collider.name + " " + b.Car.transform.InverseTransformPoint(bh.point).ToString("F3") + " normale " + b.Car.transform.InverseTransformDirection(bh.normal).ToString("F2") : "rien"));
            }
            foreach (MeshCollider mc in added) UnityEngine.Object.Destroy(mc);
            probeCols = new List<MeshCollider>();   // (fait : plus de releve)
            probeDone = true;
            return true;
        }

        static bool gaugeFreeze; static Vector3 eyesSaved;   // (essai standalonemonte)
        static void Gauges(VazCar.Built b, State st)
        {
            if (gaugeFreeze) return;
            if (st.Speedo != null) st.Speedo.Set(Mathf.Abs(b.Car.transform.InverseTransformDirection(b.Rb.velocity).z) * 3.6f);
            if (st.Tacho != null) st.Tacho.Set(st.EngineOn ? b.Dt.rpm : 0f);
        }

        // ---------------------------------------------------------------- chaque image
        static void Drive(VazCar.Built b, State st)
        {
            var d = b.Axis as StandaloneDriver;
            if (d == null) return;
            Engine(b, st);
            if (b.Sound != null) b.Sound.Update();
            Lamps(b, st);
            DoorAnim(b, st);
            Gauges(b, st);
            GameObject pl = Game.PlayerGo;
            if (pl == null || !PlayerSync.InGame) return;
            WheelMask(b, st);
            if (driving == null)
            {
                Doors(b, st);
                CabinPresence(b, st, pl.transform);
                bool zone = InZone(b, st, pl.transform) && b.Rb.velocity.magnitude < 2f && Vector3.Angle(b.Car.transform.up, Vector3.up) < 45f;
                if (zone)
                {
                    prompted = b;
                    Game.SetGlobalBool("GUIdrive", true); Game.SetGlobal("GUIinteraction", "ENTER DRIVING MODE");
                    if (Time.realtimeSinceStartup > exitBlock && Down("DrivingMode")) Enter(b, st);
                }
                else if (prompted == b) { prompted = null; Game.SetGlobalBool("GUIdrive", false); Game.SetGlobal("GUIinteraction", ""); }
                return;
            }
            if (driving != b) return;
            if (Crash(b, st)) return;
            Knobs(b, st);
            Lean(b, st);
            if (hovered < 0) Doors(b, st);
            GearText(b);
            // phares a la touche du jeu (HighBeam, L par defaut -- JD 28/10 : « je ne peux pas allumer ses phares avec L »)
            if (Down("HighBeam")) { st.LightsOn = !st.LightsOn; Log.Info("standalone : phares " + (st.LightsOn ? "allumes" : "eteints") + " (touche)"); }
            if (Time.realtimeSinceStartup - enteredAt > 0.5f && Down("DrivingMode"))
            {
                if (b.Rb.velocity.magnitude < 0.5f) Exit(b, false);
                else Log.Info("standalone : descente refusee, la voiture roule (" + (b.Rb.velocity.magnitude * 3.6f).ToString("F0") + " km/h)");
            }
        }

        // Portieres : visee au centre de l'ecran (a moins de 35 cm du centre de la portiere, a moins de 2 m), texte du jeu, clic gauche
        // ou touche Use : ouverte de 70 degres vers l'exterieur, ou refermee.
        static int doorHover = -1;
        static void Doors(VazCar.Built b, State st)
        {
            Transform cam = PlayerCam();
            if (cam == null || st.Doors == null) return;
            int hit = -1; float best = 0.35f;
            for (int i = 0; i < 2; i++)
            {
                if (st.Doors[i] == null) continue;
                Vector3 v = st.Doors[i].TransformPoint(st.DoorCenter[i] - st.Doors[i].localPosition) - cam.position;
                float along = Vector3.Dot(v, cam.forward);
                if (along < 0.1f || along > 2f) continue;
                float perp = (v - cam.forward * along).magnitude;
                if (perp < best) { best = perp; hit = i; }
            }
            if (hit < 0) { if (doorHover >= 0) { doorHover = -1; Game.SetGlobalBool("GUIuse", false); if (prompted == null) Game.SetGlobal("GUIinteraction", ""); } return; }
            doorHover = hit;
            Game.SetGlobalBool("GUIuse", true);
            if (prompted == null) Game.SetGlobal("GUIinteraction", st.DoorOpen[hit] ? "CLOSE DOOR" : "OPEN DOOR");
            if (Input.GetMouseButtonDown(0) || Down("Use")) { st.DoorOpen[hit] = !st.DoorOpen[hit]; Log.Info("standalone : portiere " + (hit == 0 ? "conducteur" : "passager") + (st.DoorOpen[hit] ? " ouverte" : " fermee")); }
        }
        static void DoorAnim(VazCar.Built b, State st)
        {
            if (st.Doors == null) return;
            for (int i = 0; i < 2; i++)
            {
                if (st.Doors[i] == null) continue;
                float want = st.DoorOpen[i] ? 70f : 0f;
                if (Mathf.Approximately(st.DoorAngle[i], want)) continue;
                st.DoorAngle[i] = Mathf.MoveTowards(st.DoorAngle[i], want, 220f * Time.deltaTime);
                st.Doors[i].localRotation = Quaternion.Euler(0f, i == 0 ? st.DoorAngle[i] : -st.DoorAngle[i], 0f);
            }
        }

        // Habitacle ou l'on entre a pied (JD 28/10 : « je ne peux pas rentrer dans la voiture, comme dans les voitures du jeu, apres avoir
        // ouvert la porte ») -- comme les voitures du jeu : coque creuse pour le joueur (couche PlayerColl) -- plancher, toit, avant
        // (planche), arriere, flanc droit, morceau derriere la portiere, et la portiere conducteur elle-meme qui tourne avec elle --,
        // bornes tirees de la portiere conducteur du modele (habitacle de son arriere -13 cm jusqu'a 27 cm avant sa charniere, entre
        // son bas et son haut, largeur a son bord interieur).
        static void Shell(VazCar.Built b, State st)
        {
            Bounds bb, db = st.DriverDoor;
            if (buildPlayerLayer < 0 || db.size.y < 0.3f || !VazCar.ModelBounds(b.M, "carrosserie.p205", out bb)) { Log.Warn("standalone : pas d'habitacle creux (couche joueur ou portiere absente)"); return; }
            int L = buildPlayerLayer; Transform ct = b.Car.transform;
            float zMin = db.min.z - 0.13f, zMax = db.max.z - 0.27f, fl = db.min.y + 0.02f, rf = db.max.y + 0.02f, ix = Mathf.Min(Mathf.Abs(db.max.x), Mathf.Abs(db.min.x)) + 0.01f;
            float x0 = bb.min.x, x1 = bb.max.x;
            // (rien sur la course des roues : le rayon d'une roue prenait le plancher pour le sol -- essai du 28/10, voiture envolee ;
            // plancher sous l'habitacle seulement, blocs avant / arriere au-dessus des roues, et cette couche retiree du rayon : WheelMask)
            float wheelTop = 0f; foreach (Wheel w in b.Car.GetComponentsInChildren<Wheel>(true)) wheelTop = Mathf.Max(wheelTop, w.transform.localPosition.y - StandaloneCar.HubDrop + w.radius + 0.03f);
            float fb = Mathf.Max(fl, wheelTop);
            // (plancher du joueur 8 cm sous le bas de la portiere : marche de 24 cm depuis le sol -- a 32 cm le joueur restait parfois
            // bloque sur le seuil, essai du 29/10)
            Box(ct, L, "joueur_plancher", new Vector3(0f, fl - 0.16f, (zMin + zMax) * 0.5f), new Vector3(2f * ix, 0.16f, zMax - zMin));
            Box(ct, L, "joueur_toit", new Vector3(0f, (rf + bb.max.y + 0.02f) * 0.5f, bb.center.z), new Vector3(bb.size.x, bb.max.y + 0.02f - rf, bb.size.z));
            Box(ct, L, "joueur_avant", new Vector3(0f, (fb + rf) * 0.5f, (zMax + bb.max.z) * 0.5f), new Vector3(bb.size.x, rf - fb, bb.max.z - zMax));
            Box(ct, L, "joueur_arriere", new Vector3(0f, (fb + rf) * 0.5f, (bb.min.z + zMin) * 0.5f), new Vector3(bb.size.x, rf - fb, zMin - bb.min.z));
            // (cote passager : comme le conducteur si sa portiere est la -- JD 28/10 : « pas cote passager, le passage est toujours
            // bloque, porte ouverte » --, sinon flanc plein)
            Bounds pd = st.DoorBox[1];
            if (pd.size.y > 0.3f) Box(ct, L, "joueur_flanc_d_ar", new Vector3((ix + x1) * 0.5f, (fl + rf) * 0.5f, (zMin + pd.min.z) * 0.5f), new Vector3(x1 - ix, rf - fl, pd.min.z - zMin));
            else Box(ct, L, "joueur_flanc_d", new Vector3((ix + x1) * 0.5f, (fl + rf) * 0.5f, (zMin + zMax) * 0.5f), new Vector3(x1 - ix, rf - fl, zMax - zMin));
            Box(ct, L, "joueur_flanc_g_ar", new Vector3((x0 - ix) * 0.5f, (fl + rf) * 0.5f, (zMin + db.min.z) * 0.5f), new Vector3(-ix - x0, rf - fl, db.min.z - zMin));
            st.Cabin = new Bounds(); st.Cabin.SetMinMax(new Vector3(-ix, fl, zMin), new Vector3(ix, rf, zMax));
            st.HasCabin = true;
            Log.Info("standalone : habitacle creux " + st.Cabin.min.ToString("F2") + " - " + st.Cabin.max.ToString("F2") + ", couche " + LayerMask.LayerToName(L));
        }

        // Choc mortel. Les voitures du jeu tuent par la liaison du pivot de tete du conducteur (releve 29/10 sur la Satsuma : tete de
        // 0,5 kg, liaison bloquee sur les trois axes, rupture a 300 N au volant, 500 avec le casque -- automate HelmetSim) : ~43 km/h
        // perdus en un pas de physique (0,02 s) au point de la tete. Ici la meme mesure (CrashSensor, a chaque pas), mais seulement a
        // l'horizontale : un vrai choc contre un obstacle, pas une bosse prise vite, la suspension qui talonne ni la reception d'un saut
        // (JD 29/10 : « je suis mort trop facilement en passant vite sur une bosse » -- la 1re version comptait toute vitesse perdue en
        // 0,1 s, verticale comprise, des 40 km/h). Seuil : tenue.ini [Poste] ChocMortel (45 km/h a 50 pas/s ; x1,6 casque mis). Puis la
        // mort du jeu par accident (Systems/Death, cause Crash : sang, ecran, reapparition en co-op -- Respawn.TestDie).
        public class CrashSensor : MonoBehaviour
        {
            public Rigidbody Rb; public Vector3 Head;   // (tete, repere de la voiture)
            public float MaxH, MaxV;                    // (plus grandes vitesses perdues en un pas depuis la derniere lecture, m/s)
            Vector3 prev; bool has;
            public void Reset() { has = false; MaxH = MaxV = 0f; }   // (vitesse posee a la main : essais, teleportation)
            void FixedUpdate()
            {
                if (Rb == null || Rb.isKinematic) { has = false; return; }
                Vector3 v = Rb.GetPointVelocity(transform.TransformPoint(Head));
                if (has)
                {
                    Vector3 d = v - prev, vert = Vector3.Project(d, Vector3.up);
                    MaxH = Mathf.Max(MaxH, (d - vert).magnitude); MaxV = Mathf.Max(MaxV, vert.magnitude);
                }
                prev = v; has = true;
            }
        }

        static bool Crash(VazCar.Built b, State st)
        {
            CrashSensor s = st.Sensor;
            if (s == null) { s = st.Sensor = b.Car.AddComponent<CrashSensor>(); s.Rb = b.Rb; s.Head = b.M.Head; }
            float h = s.MaxH * 3.6f, v = s.MaxV * 3.6f;
            s.MaxH = s.MaxV = 0f;
            if (b.Rb.isKinematic || Time.realtimeSinceStartup - enteredAt < 1f) return false;
            float lim = st.T.ChocMortel * Time.fixedDeltaTime / 0.02f;   // (meme force sur la tete : la vitesse perdue en un pas suit la duree du pas)
            FsmBool helmet = FsmVariables.GlobalVariables.FindFsmBool("PlayerHelmet");
            if (helmet != null && helmet.Value) lim *= 1.6f;
            if (h > crashMaxH) crashMaxH = h; if (v > crashMaxV) crashMaxV = v;
            if (h < lim) return false;
            Log.Info("standalone : choc mortel, " + h.ToString("F0") + " km/h perdus a l'horizontale en un pas (" + v.ToString("F0") + " a la verticale, limite " + lim.ToString("F0") + ")");
            Exit(b, true);
            Log.Info("standalone : " + Respawn.TestDie("Crash"));
            return true;
        }
        static float crashMaxH, crashMaxV;   // (plus grands chocs vus au volant : essais)

        // Releve de la liaison du pivot de tete des voitures du jeu (comment le jeu decide la mort au volant)
        static void HeadJointProbe()
        {
            foreach (ConfigurableJoint j in UnityEngine.Object.FindObjectsOfType<ConfigurableJoint>())
            {
                if (j.name != "DriverHeadPivot") continue;
                Rigidbody r = j.GetComponent<Rigidbody>();
                Log.Info("standalone : tete " + Recon.Path(j.transform) + " : masse " + (r != null ? r.mass.ToString("F1") : "?") + ", drag " + (r != null ? r.drag.ToString("F1") : "?")
                         + ", axes x/y/z " + j.xMotion + "/" + j.yMotion + "/" + j.zMotion + ", rotations " + j.angularXMotion + "/" + j.angularYMotion + "/" + j.angularZMotion
                         + ", limite " + j.linearLimit.limit.ToString("F3") + ", ressort x " + j.xDrive.positionSpring.ToString("F0") + " y " + j.yDrive.positionSpring.ToString("F0") + " z " + j.zDrive.positionSpring.ToString("F0")
                         + ", rupture " + j.breakForce.ToString("F0") + " / " + j.breakTorque.ToString("F0") + ", ancre " + j.anchor.ToString("F2") + ", relie a " + (j.connectedBody != null ? j.connectedBody.name : "monde"));
            }
        }

        // Se pencher au volant (JD 29/10 : « je ne peux pas me pencher en conducteur comme dans les autres voitures ») -- comme les
        // voitures du jeu (automate Strafe de DriverHeadPivot/CameraPivot, releve sur la Satsuma) : joueur range sous
        // DriverHeadPivot/CameraPivot/Pivot, CameraPivot porte les animations car_strafe_left1/2 et right1/2 du jeu (clips repris de
        // la voiture du jeu, rien de recopie), touches ReachLeft / ReachRight tenues (cInput) ; vers la portiere seulement si elle est
        // ouverte de plus de 20 degres (la Satsuma : porte absente, vitre baissee ou porte ouverte). Positions de repos de CameraPivot
        // et Pivot reprises de la voiture du jeu ; sens de chaque clip mesure sur nos objets (fin du clip : Pivot a gauche ou a droite).
        static Transform LeanRig(VazCar.Built b, State st, Transform head)
        {
            Transform srcHead = null;
            foreach (string pth in new[] { "SATSUMA(557kg, 248)/DriverHeadPivot", "RCO_RUSCKO12(270)/LOD/DriverHeadPivot" }) { GameObject g = Game.FindAny(pth); if (g != null && g.transform.Find("CameraPivot/Pivot") != null) { srcHead = g.transform; break; } }
            Transform cp = new GameObject("CameraPivot").transform; cp.SetParent(head, false);
            Transform pv = new GameObject("Pivot").transform; pv.SetParent(cp, false);
            if (srcHead != null)
            {
                Transform scp = srcHead.Find("CameraPivot"), spv = scp.Find("Pivot");
                Transform root = srcHead.root;
                head.localRotation = Quaternion.Inverse(root.rotation) * srcHead.rotation;
                cp.localPosition = scp.localPosition; cp.localRotation = scp.localRotation;
                pv.localPosition = spv.localPosition; pv.localRotation = spv.localRotation;
                Animation sa = scp.GetComponent<Animation>();
                if (sa != null)
                {
                    Animation an = cp.gameObject.AddComponent<Animation>(); an.playAutomatically = false; an.cullingType = AnimationCullingType.AlwaysAnimate;   // (aucun rendu sous le pivot : sinon jamais animee)
                    foreach (string c in new[] { "car_strafe_left1", "car_strafe_left2", "car_strafe_right1", "car_strafe_right2" }) { AnimationClip clip = sa.GetClip(c); if (clip != null) { an.AddClip(clip, c); an[c].wrapMode = c.EndsWith("1") ? WrapMode.ClampForever : WrapMode.Once; } }   // (penche tenu tant que la touche l'est)
                    st.Lean = an;
                }
            }
            st.RestCarFromPivot = b.Car.transform.worldToLocalMatrix * pv.localToWorldMatrix;
            // sens des clips : Pivot en fin de « 1 », repere de la voiture, puis retour au repos
            if (st.Lean != null && st.Lean.GetClip("car_strafe_left1") != null && st.Lean.GetClip("car_strafe_right1") != null)
            {
                Vector3 rest = b.Car.transform.InverseTransformPoint(pv.position), cpPos = cp.localPosition; Quaternion cpRot = cp.localRotation;
                float lx = LeanEnd(st.Lean, "car_strafe_left1", b.Car.transform, pv) - rest.x; cp.localPosition = cpPos; cp.localRotation = cpRot;
                float rx = LeanEnd(st.Lean, "car_strafe_right1", b.Car.transform, pv) - rest.x; cp.localPosition = cpPos; cp.localRotation = cpRot;
                float doorSide = Mathf.Abs(b.M.Head.x) > 0.01f ? Mathf.Sign(b.M.Head.x) : -1f;
                bool leftIsOut = Mathf.Sign(lx) == doorSide;
                st.LeanOut = leftIsOut ? "car_strafe_left1" : "car_strafe_right1"; st.LeanBackOut = leftIsOut ? "car_strafe_left2" : "car_strafe_right2";
                st.LeanIn = leftIsOut ? "car_strafe_right1" : "car_strafe_left1"; st.LeanBackIn = leftIsOut ? "car_strafe_right2" : "car_strafe_left2";
                Log.Info("standalone : se pencher, animations de " + (srcHead != null ? srcHead.root.name : "?") + " : left1 " + lx.ToString("F2") + " m, right1 " + rx.ToString("F2") + " m (x de la voiture) ; vers la portiere = " + st.LeanOut);
            }
            else Log.Warn("standalone : animations pour se pencher introuvables (voiture du jeu absente)");
            return pv;
        }
        static float LeanEnd(Animation an, string clip, Transform car, Transform pv)
        {
            AnimationState s = an[clip];
            s.enabled = true; s.weight = 1f; s.time = s.length; an.Sample(); s.enabled = false;
            return car.InverseTransformPoint(pv.position).x;
        }

        // 0 droit, 1 penche vers la portiere, 2 vers l'interieur ; touche ReachLeft / ReachRight tenue (gauche = cote conducteur ici)
        static int leanForce = -1;   // (essai : penche impose, les touches ne se simulent pas)
        static void Lean(VazCar.Built b, State st)
        {
            if (st.Lean == null || st.LeanOut == null) return;
            float doorSide = Mathf.Abs(b.M.Head.x) > 0.01f ? Mathf.Sign(b.M.Head.x) : -1f;
            bool outKey = Held(doorSide < 0f ? "ReachLeft" : "ReachRight"), inKey = Held(doorSide < 0f ? "ReachRight" : "ReachLeft");
            bool doorOpen = st.DoorAngle != null && st.DoorAngle[0] > 20f;
            int want = leanForce >= 0 ? leanForce : outKey && doorOpen ? 1 : inKey ? 2 : 0;
            if (want == st.LeanState) return;
            if (st.LeanState == 1) st.Lean.Play(st.LeanBackOut, PlayMode.StopAll);
            else if (st.LeanState == 2) st.Lean.Play(st.LeanBackIn, PlayMode.StopAll);
            if (want == 1) st.Lean.Play(st.LeanOut, PlayMode.StopAll);
            else if (want == 2) st.Lean.Play(st.LeanIn, PlayMode.StopAll);
            st.LeanState = want;
        }

        // Couche des colliders du joueur retiree du rayon des roues (pose par Wheel.Start, apres l'allumage)
        static FieldInfo wheelMaskField;
        static void WheelMask(VazCar.Built b, State st)
        {
            if (st.MaskDone || buildPlayerLayer < 0) return;
            if (wheelMaskField == null) wheelMaskField = typeof(Wheel).GetField("layerMask", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (wheelMaskField == null) { st.MaskDone = true; return; }
            int n = 0;
            foreach (Wheel w in b.Car.GetComponentsInChildren<Wheel>(true))
            {
                object v = wheelMaskField.GetValue(w);
                int m = v is LayerMask ? ((LayerMask)v).value : v is int ? (int)v : 0;
                if (m == 0) return;   // (Start pas encore passe)
                int nm = m & ~(1 << buildPlayerLayer);
                if (nm != m) { if (v is LayerMask) wheelMaskField.SetValue(w, (LayerMask)nm); else wheelMaskField.SetValue(w, nm); n++; }
            }
            st.MaskDone = true;
            Log.Info("standalone : couche joueur retiree du rayon de " + n + " roue(s)");
        }

        // Joueur dans l'habitacle ou devant la portiere conducteur ouverte : PlayerInCar de l'automate Crouch (le jeu le rend petit,
        // camera basse -- ce que fait le PlayerTrigger autour de l'habitacle des voitures du jeu), rendu a la sortie de la zone.
        static VazCar.Built cabinCar;
        static void CabinPresence(VazCar.Built b, State st, Transform pl)
        {
            if (!st.HasCabin) return;
            Vector3 lp = b.Car.transform.InverseTransformPoint(pl.position);
            Bounds c = st.Cabin, d = st.DriverDoor;
            bool inside = Mathf.Abs(lp.x) < c.max.x + 0.05f && lp.z > c.min.z && lp.z < c.max.z && lp.y > -0.8f && lp.y < 1.8f;
            bool atDoor = st.DoorOpen[0] && lp.x < -c.max.x && lp.x > -(st.HalfWidth + 0.45f) && lp.z > d.min.z && lp.z < d.max.z - 0.1f && lp.y > -0.8f && lp.y < 1.8f;
            Bounds p = st.DoorBox[1];
            if (!atDoor && p.size.y > 0.3f && st.DoorOpen[1]) atDoor = lp.x > c.max.x && lp.x < st.HalfWidth + 0.45f && lp.z > p.min.z && lp.z < p.max.z - 0.1f && lp.y > -0.8f && lp.y < 1.8f;
            bool want = inside || atDoor;
            if (want && cabinCar != b) { cabinCar = b; InCar(true); Log.Info("standalone : joueur dans l'habitacle" + (inside ? "" : " (devant la portiere ouverte)")); }
            else if (!want && cabinCar == b) { cabinCar = null; InCar(false); Log.Info("standalone : joueur sorti de l'habitacle"); }
        }

        // Zone d'entree en conduite (le « trigger » des voitures du jeu) : sur le siege conducteur, dans l'habitacle -- tenue.ini [Poste]
        // EntreeZ (centre, depart : le siege) et EntreeLongueur ; et, portiere ouverte, devant elle dehors (si l'on ne passe pas).
        // (JD 28/10 : la zone d'avant, dehors le long du flanc, etait mal centree par rapport au vrai siege)
        static bool InZone(VazCar.Built b, State st, Transform pl)
        {
            Vector3 lp = b.Car.transform.InverseTransformPoint(pl.position);
            bool alongSeat = Mathf.Abs(lp.z - st.T.EntreeZ) < st.T.EntreeLongueur * 0.5f && lp.y > -1.0f && lp.y < 2.0f;
            if (!alongSeat) return false;
            if (Mathf.Abs(lp.x - b.M.Head.x) < 0.32f) return true;
            float side = Mathf.Abs(b.M.Head.x) > 0.01f ? Mathf.Sign(b.M.Head.x) : -1f, flank = st.HalfWidth > 0.3f ? st.HalfWidth : 0.85f;
            return st.DoorOpen != null && st.DoorOpen[0] && lp.x * side > flank - 0.12f && lp.x * side < flank + 1.1f;
        }

        static void Enter(VazCar.Built b, State st)
        {
            GameObject pl = Game.PlayerGo;
            float gy;
            footHeight = Ground(pl.transform.position + Vector3.up * 0.3f, pl.transform, b.Car.transform, out gy) ? pl.transform.position.y - gy : 1.0f;
            footHeight = Mathf.Clamp(footHeight, 0.2f, 2f);
            var cc = pl.GetComponent<CharacterController>(); if (cc != null) cc.enabled = false;
            Game.SetGlobalBool("GUIdrive", false); Game.SetGlobal("GUIinteraction", ""); prompted = null;
            Game.SetGlobalBool("PlayerStop", true);
            Game.SetGlobal("PlayerCurrentVehicle", VehicleName);
            InCar(true);
            pl.transform.SetParent(st.Pivot, true);
            pl.transform.localRotation = Quaternion.identity;
            pl.transform.localPosition = Vector3.zero;
            var d = (StandaloneDriver)b.Axis;
            d.Keys = true; d.Handbrake = 0f;
            driving = b; enteredAt = Time.realtimeSinceStartup;
            GearHud(true, b);
            Log.Info("standalone : joueur au volant de " + b.Car.name + " (pied a " + footHeight.ToString("F2") + " m)");
        }

        // force : sortie d'urgence (voiture detruite, fin de partie) sans condition de vitesse
        static void Exit(VazCar.Built b, bool force)
        {
            GameObject pl = Game.PlayerGo;
            Transform ct = b.Car != null ? b.Car.transform : null;
            State xs; bool cabin = !force && ct != null && states.TryGetValue(b, out xs) && xs.HasCabin;
            if (pl != null)
            {
                pl.transform.SetParent(null, true);
                // (descente normale : on reste a sa place, dans l'habitacle, petit comme dans les voitures du jeu -- JD 28/10 : « quand je
                // sors du mode conducteur, que ca me laisse dans la voiture, et non pas la teleportation a l'exterieur » ; sortie forcee
                // ou sans habitacle creux : a cote de la portiere, comme avant)
                if (cabin)
                {
                    // (bas de la capsule du joueur pose 3 cm au-dessus du plancher de l'habitacle : rallume dans le plancher, il en etait
                    // expulse et partait a 12 m -- essai du 28/10)
                    pl.transform.rotation = Quaternion.Euler(0f, ct.eulerAngles.y, 0f);
                    var cc0 = pl.GetComponent<CharacterController>();
                    State cs; states.TryGetValue(b, out cs);
                    if (cc0 != null && cs != null)
                    {
                        float bottom = pl.transform.position.y + (cc0.center.y - cc0.height * 0.5f) * pl.transform.lossyScale.y;
                        float floorW = ct.TransformPoint(new Vector3(0f, cs.Cabin.min.y, 0f)).y + 0.03f;
                        if (bottom < floorW) pl.transform.position += Vector3.up * (floorW - bottom);
                    }
                }
                else if (ct != null)
                {
                    float side = Mathf.Abs(b.M.Head.x) > 0.01f ? Mathf.Sign(b.M.Head.x) : -1f;
                    State es; float ez = states.TryGetValue(b, out es) ? es.T.EntreeZ : b.M.Head.z, ew = es != null && es.HalfWidth > 0.3f ? es.HalfWidth : 0.85f;
                    Vector3 at = ct.TransformPoint(new Vector3(side * (ew + 0.75f), 0.4f, ez));
                    float gy;
                    if (Ground(at + Vector3.up * 2f, pl.transform, ct, out gy)) at.y = gy + (footHeight > 0f ? footHeight : 1f);
                    pl.transform.position = at;
                    pl.transform.rotation = Quaternion.Euler(0f, ct.eulerAngles.y, 0f);
                }
                var cc = pl.GetComponent<CharacterController>(); if (cc != null) cc.enabled = true;
            }
            Game.SetGlobalBool("PlayerStop", false);
            Game.SetGlobal("PlayerCurrentVehicle", "");
            if (cabin) { InCar(true); cabinCar = b; } else { InCar(false); cabinCar = null; }
            GearHud(false, b);
            ClearHover();
            var d = b.Axis as StandaloneDriver;
            if (d != null) { d.Keys = false; d.Throttle = 0f; d.Brake = 0f; d.Steer = 0f; d.Clutch = 0f; d.Handbrake = 1f; }
            driving = null; exitBlock = Time.realtimeSinceStartup + 0.6f;
            Log.Info("standalone : joueur descendu" + (force ? " (force)" : "") + (pl != null ? " en " + pl.transform.position.ToString("F1") : ""));
        }

        // Rapport en bas a droite (HUD du jeu) : GUI/Indicators/Gear allume au volant, texte GUIgear (R, N, 1-6) -- ce que font
        // l'automate PlayerTrigger et le GearIndicator des voitures du jeu (JD 28/10 : « pas d'indication de vitesse en bas a droite »)
        static GameObject gearGui; static int gearShown = -99;
        static void GearHud(bool on, VazCar.Built b)
        {
            if (gearGui == null) gearGui = Game.FindAny("GUI/Indicators/Gear");
            if (gearGui != null) gearGui.SetActive(on);
            gearShown = -99;
            if (on) GearText(b); else Game.SetGlobal("GUIgear", "");
        }
        static void GearText(VazCar.Built b)
        {
            Drivetrain dt = b.Dt;
            if (dt == null || dt.gearRatios == null || dt.gear == gearShown) return;
            gearShown = dt.gear;
            int n = Array.IndexOf(dt.gearRatios, 0f);
            Game.SetGlobal("GUIgear", dt.gear == n ? "N" : dt.gear < n ? "R" : (dt.gear - n).ToString());
        }

        static void InCar(bool on)
        {
            GameObject pl = Game.PlayerGo;
            PlayMakerFSM cr = pl != null ? Game.FsmOn(pl, "Crouch") : null;
            FsmBool v = cr != null ? cr.FsmVariables.FindFsmBool("PlayerInCar") : null;
            if (v != null) v.Value = on;
        }

        // Sol sous un point (premier collider plein qui n'est ni le joueur ni la voiture)
        static bool Ground(Vector3 from, Transform player, Transform car, out float y)
        {
            y = 0f; float best = float.MaxValue; bool ok = false;
            foreach (RaycastHit h in Physics.RaycastAll(from, Vector3.down, 6f))
            {
                if (h.collider == null || h.collider.isTrigger) continue;
                if (player != null && h.collider.transform.IsChildOf(player)) continue;
                if (car != null && h.collider.transform.IsChildOf(car)) continue;
                if (h.distance < best) { best = h.distance; y = h.point.y; ok = true; }
            }
            return ok;
        }

        // Apres les automates et la camera du joueur : yeux du joueur ramenes au point de vue (pivot de tete + EyeFromPivot du jeu,
        // comme la copie) ; volant visible tourne avec la direction.
        static void DriveLate(VazCar.Built b, State st)
        {
            var d = b.Axis as CarController;
            // (volant visible d'apres l'angle REEL des roues avant -- braquage selon la vitesse compris --, x tenue.ini [Direction]
            // Demultiplication (14 : comme une voiture de serie), et lisse a 720 degres de volant par seconde : JD 29/10, « l'animation
            // du volant doit suivre la rotation des roues pour etre coherente » -- avant, la touche : un tour complet a toute vitesse)
            if (b.SteerPivot != null && b.FrontWheels != null)
            {
                float wa = 0f; int nw = 0;
                foreach (Wheel w in b.FrontWheels) if (w != null) { wa += w.steering * w.maxSteeringAngle; nw++; }
                float want = nw > 0 ? wa / nw * st.T.Demultiplication : 0f;
                st.WheelVis = Mathf.MoveTowards(st.WheelVis, want, 720f * Time.deltaTime);
                b.SteerPivot.localRotation = Quaternion.AngleAxis(-st.WheelVis, b.SteerAxis);
            }
            else if (b.SteerPivot != null && b.FrontWheels == null && b.Car.GetComponent<Axles>() != null) b.FrontWheels = b.Car.GetComponent<Axles>().frontAxle.wheels;
            if (driving != b) return;
            GameObject pl = Game.PlayerGo;
            Transform cam = PlayerCam();
            if (pl == null || cam == null || pl.transform.parent != st.Pivot) return;
            // (yeux portes par le pivot, anime quand on se penche : point de vue au repos ramene dans le repere du pivot)
            Vector3 eye = st.Pivot.TransformPoint(st.RestCarFromPivot.inverse.MultiplyPoint3x4(st.T.Yeux));
            Vector3 delta = eye - cam.position;
            if (delta.sqrMagnitude > 1e-6f && delta.sqrMagnitude < 9f) pl.transform.position += delta;
        }

        // ---------------------------------------------------------------- moteur
        public static bool EngineRunning(VazCar.Built b) { State st; return states.TryGetValue(b, out st) && st.EngineOn; }
        public static void SetEngine(VazCar.Built b, bool on)
        {
            State st;
            if (!states.TryGetValue(b, out st)) return;
            st.EngineOn = on; st.CrankT = -1f;
            if (on) b.Dt.StartEngine();
            Log.Info("standalone : moteur " + (on ? "demarre" : "coupe"));
        }

        static void Engine(VazCar.Built b, State st)
        {
            if (st.EngineOn) return;
            // (moteur coupe : Drivetrain.Start le demarre a la naissance, et le ralenti le relancerait -- regime tenu a zero)
            if (b.Dt.rpm > 1f)
            {
                if (engineVelo == null) engineVelo = typeof(Drivetrain).GetField("engineAngularVelo", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                if (engineVelo != null) engineVelo.SetValue(b.Dt, 0f);
            }
            b.Dt.idlethrottle = 0f;
        }

        // ---------------------------------------------------------------- tableau de bord
        const int KIgnition = 0, KLights = 2;
        static void Knobs(VazCar.Built b, State st)
        {
            Transform cam = PlayerCam();
            VazCar.Knob[] ks = KnobsOf(b.M);
            if (cam == null || ks == null) return;
            int hit = -1; float bestPerp = 0.035f;
            foreach (int i in new[] { KIgnition, KLights })
            {
                if (i >= ks.Length) continue;
                Vector3 v = b.Car.transform.TransformPoint(ks[i].At) - cam.position;
                float along = Vector3.Dot(v, cam.forward);
                if (along < 0.15f || along > 1.3f) continue;
                float perp = (v - cam.forward * along).magnitude;
                if (perp < bestPerp) { bestPerp = perp; hit = i; }
            }
            if (hit < 0) { ClearHover(); CrankStop(b, st); return; }
            hovered = hit;
            Game.SetGlobalBool("GUIuse", true);
            Game.SetGlobal("GUIinteraction", hit == KIgnition ? (st.EngineOn ? "ENGINE OFF" : "START ENGINE") : st.LightsOn ? "LIGHTS OFF" : "LIGHTS ON");
            if (hit == KLights) { if (Input.GetMouseButtonDown(0)) { st.LightsOn = !st.LightsOn; Log.Info("standalone : phares " + (st.LightsOn ? "allumes" : "eteints")); } return; }
            // contact : clic moteur tournant = arret ; moteur coupe, clic tenu = demarreur, le moteur prend apres 0,9 s
            if (st.EngineOn) { if (Input.GetMouseButtonDown(0)) SetEngine(b, false); return; }
            if (Input.GetMouseButton(0))
            {
                if (st.CrankT < 0f) { st.CrankT = Time.time; if (b.Sound != null) b.Sound.ExternalStarter(true, false); }
                if (Time.time - st.CrankT > 0.9f) { SetEngine(b, true); if (b.Sound != null) b.Sound.ExternalStarter(false, true); }
            }
            else CrankStop(b, st);
        }
        static void CrankStop(VazCar.Built b, State st) { if (st.CrankT >= 0f) { st.CrankT = -1f; if (b.Sound != null) b.Sound.ExternalStarter(false, false); } }
        static void ClearHover() { if (hovered < 0) return; hovered = -1; Game.SetGlobalBool("GUIuse", false); Game.SetGlobal("GUIinteraction", ""); }

        // Feux du modele : avant allumes avec les phares, arriere faibles avec les phares et forts au freinage, recul en marche arriere.
        static void Lamps(VazCar.Built b, State st)
        {
            var d = b.Axis as CarController;
            bool f = st.LightsOn, br = d != null && d.brake > 0.1f, rv = b.Dt != null && b.Dt.gearRatios != null && b.Dt.gear >= 0 && b.Dt.gear < b.Dt.gearRatios.Length && b.Dt.gearRatios[b.Dt.gear] < 0f;
            if (b.LampsInit && f == b.FrontOn && br == b.BrakeOn && rv == b.ReverseOn) return;
            b.LampsInit = true; b.FrontOn = f; b.RearOn = f; b.BrakeOn = br; b.ReverseOn = rv;
            if (st.Heads != null) foreach (Light l in st.Heads) if (l != null) l.enabled = f;
            foreach (KeyValuePair<MeshRenderer, string> kv in b.Lamps)
            {
                if (kv.Key == null) continue;
                string k = kv.Value; Material m = null;
                if (k.EndsWith("_ar")) { if (br) b.Lit.TryGetValue(k, out m); else if (f && !b.Dim.TryGetValue(k, out m)) b.Lit.TryGetValue(k, out m); }
                else if ((k.EndsWith("_av") && f) || (k.EndsWith("_re") && rv)) b.Lit.TryGetValue(k, out m);
                Material basic; b.Mats.TryGetValue(k, out basic);
                kv.Key.sharedMaterial = m ?? basic;
            }
        }

        // ---------------------------------------------------------------- [Test] Autotest=standalonemonte (+ AddonStandalone=1,
        // StandaloneEssai=1) : sur la piste plate du banc, 20 cycles (voiture tournee de 0 / 120 / 240 degres) : joueur pose dans la zone
        // d'entree (texte du jeu verifie), monte (Enter : la touche du jeu ne se simule pas), yeux au point de vue, 2 s de gaz en 1re
        // puis frein, descend ; au sol a cote, CharacterController et PlayerStop rendus. Bilan : cycles reussis.
        static int mStep = -1, mCycle, mOk, mSide; static float mAt, mLog; static float mFly, mLeanAt;
        // (essai : voiture posee en p0, cap +z, joueur au volant ; entree remise a plus d'1 s pour que le choc compte tout de suite)
        static void PlaceSeated(VazCar.Built b, State st, GameObject pl, Vector3 p0)
        {
            Transform ct = b.Car.transform;
            b.Rb.velocity = Vector3.zero; b.Rb.angularVelocity = Vector3.zero;
            ct.position = p0; ct.rotation = Quaternion.identity; b.Rb.position = p0; b.Rb.rotation = Quaternion.identity;
            st.DoorOpen[0] = st.DoorOpen[1] = false; st.DoorAngle[0] = st.DoorAngle[1] = 0f;
            if (st.Doors[0] != null) st.Doors[0].localRotation = Quaternion.identity; if (st.Doors[1] != null) st.Doors[1].localRotation = Quaternion.identity;
            if (driving != b)
            {
                var cc = pl.GetComponent<CharacterController>(); if (cc != null) cc.enabled = false;
                pl.transform.position = ct.TransformPoint(new Vector3(b.M.Head.x, 0.5f, st.T.EntreeZ));
                if (cc != null) cc.enabled = true;
                Enter(b, st);
            }
            enteredAt = Time.realtimeSinceStartup - 2f;
        } static string mWhy; static bool mShot;
        public static void MountTest(float t)
        {
            Test(t);
            if (mStep == 99 || cars.Count == 0 || cars[0].Car == null) return;
            VazCar.Built b = cars[0]; State st; if (!states.TryGetValue(b, out st)) return;
            var d = (StandaloneDriver)b.Axis;
            GameObject pl = Game.PlayerGo; if (pl == null) return;
            float e = t - mAt;
            Transform ct = b.Car.transform;
            switch (mStep)
            {
                case -1:
                    VazTenue.MakeTrack(); mStep = 0; mAt = t; mCycle = 0; mOk = 0;
                    if (!EngineRunning(b)) SetEngine(b, true);
                    break;
                case 0:   // voiture posee, joueur dans la zone
                {
                    Quaternion r = Quaternion.Euler(0f, (mCycle % 3) * 120f, 0f);
                    Vector3 p0 = new Vector3(mCycle * 30f, VazTenue.TrackY + 0.6f, 0f);
                    b.Rb.velocity = Vector3.zero; b.Rb.angularVelocity = Vector3.zero;
                    ct.position = p0; ct.rotation = r; b.Rb.position = p0; b.Rb.rotation = r;
                    var cc = pl.GetComponent<CharacterController>(); if (cc != null) cc.enabled = false;
                    pl.transform.position = ct.TransformPoint(new Vector3(-(st.HalfWidth + 0.45f), 0.6f, st.T.EntreeZ));
                    pl.transform.rotation = Quaternion.LookRotation(Vector3.ProjectOnPlane(ct.right, Vector3.up));   // (face a la portiere)
                    if (cc != null) cc.enabled = true;
                    // (portiere conducteur ouverte : on entre par elle)
                    st.DoorOpen[0] = true; st.DoorAngle[0] = 70f; st.Doors[0].localRotation = Quaternion.Euler(0f, 70f, 0f);
                    mWhy = ""; mStep = 1; mAt = t;
                    break;
                }
                case 1:
                    // 1er cycle : le joueur marche de dehors jusqu'au siege, a travers la portiere ouverte (3 s au plus) ; il doit passer dans
                    // l'habitacle (PlayerInCar : petit) et etre dans la zone d'entree ; les autres cycles : devant la portiere ouverte
                    if (mCycle == 0 && e < 3.2f)
                    {
                        var cm = pl.GetComponent<CharacterController>();
                        Vector3 goal = ct.TransformPoint(new Vector3(b.M.Head.x, 0f, st.T.EntreeZ)), d0 = Vector3.ProjectOnPlane(goal - pl.transform.position, Vector3.up);
                        if (cm != null && cm.enabled && d0.magnitude > 0.08f) cm.Move(d0.normalized * Mathf.Min(1.5f * Time.deltaTime, d0.magnitude) + Vector3.down * 0.2f * Time.deltaTime);
                        if (e > 0.9f && e < 1.0f && !mShot) { mShot = true; Autotest.CaptureSoon("standalone-portiere", 0f); }
                        break;
                    }
                    if (e < 1.5f) break;
                    if (mCycle == 0)
                    {
                        Vector3 lq = ct.InverseTransformPoint(pl.transform.position);
                        Log.Info("standalone : monte, joueur entre par la portiere : arrive a " + lq.ToString("F2") + (cabinCar == b ? ", dans l'habitacle (PlayerInCar)" : ", PAS dans l'habitacle"));
                        if (cabinCar != b) mWhy += " pas entre dans l'habitacle";
                    }
                    FsmString gi = FsmVariables.GlobalVariables.FindFsmString("GUIinteraction");
                    if (!InZone(b, st, pl.transform)) mWhy += " hors zone (" + ct.InverseTransformPoint(pl.transform.position).ToString("F2") + ")";
                    else if (gi == null || gi.Value != "ENTER DRIVING MODE") mWhy += " texte '" + (gi != null ? gi.Value : "?") + "'";
                    Enter(b, st); mStep = 2; mAt = t;
                    break;
                case 2:
                    if (e < 1.5f) break;
                    if (mCycle == 1 && !Probe(b)) break;
                    {
                        FsmBool ps = FsmVariables.GlobalVariables.FindFsmBool("PlayerStop"); FsmString cv = FsmVariables.GlobalVariables.FindFsmString("PlayerCurrentVehicle");
                        Transform cam = PlayerCam();
                        float eyeErr = cam != null ? (cam.position - ct.TransformPoint(st.T.Yeux)).magnitude : 9f;
                        if (ps == null || !ps.Value) mWhy += " PlayerStop faux";
                        if (cv == null || cv.Value != VehicleName) mWhy += " vehicule '" + (cv != null ? cv.Value : "?") + "'";
                        if (pl.transform.parent != st.Pivot) mWhy += " pas sous le pivot";
                        if (eyeErr > 0.05f) mWhy += " yeux a " + eyeErr.ToString("F2") + " m";
                        if (mCycle == 2 && st.Lean != null && st.LeanIn != null) { leanForce = 2; Autotest.CaptureSoon("standalone-penche", 0.8f); mLeanAt = t; }
                        if (mCycle == 0)
                        {
                            // (cadrans de pres, aiguilles tenues a 100 km/h et 4 000 tr/min : sens et graduations verifiables)
                            st.LightsOn = true; gaugeFreeze = true; st.Speedo.Set(100f); st.Tacho.Set(4000f);
                            eyesSaved = st.T.Yeux; st.T.Yeux = new Vector3(-0.295f, 0.735f, 0.36f);
                            Autotest.CaptureSoon("standalone-cadrans", 0.4f);
                        }
                        FsmString gg = FsmVariables.GlobalVariables.FindFsmString("GUIgear");
                        if (gg == null || gg.Value != "N") mWhy += " rapport affiche '" + (gg != null ? gg.Value : "?") + "'";
                        if (gearGui == null || !gearGui.activeSelf) mWhy += " indicateur de rapport eteint";
                        d.Keys = false; d.Handbrake = 0f; d.Gear = 2; d.Throttle = 0.5f; d.Brake = 0f;
                        mStep = 3; mAt = t;
                    }
                    break;
                case 3:
                    if (e < 2f) break;
                    if (mCycle == 2 && mLeanAt > 0f)
                    {
                        Transform cm2 = PlayerCam();
                        Vector3 off = cm2 != null ? ct.InverseTransformPoint(cm2.position) - st.T.Yeux : Vector3.zero;
                        AnimationState ls = st.Lean[st.LeanIn];
                        Log.Info("standalone : monte, penche vers l'interieur : yeux deplaces de " + off.ToString("F2") + " (repere de la voiture) ; animation " + (st.Lean.isPlaying ? "en cours" : "arretee") + ", t " + (ls != null ? ls.time.ToString("F2") + "/" + ls.length.ToString("F2") + " " + ls.wrapMode : "?") + ", CameraPivot " + st.Lean.transform.localPosition.ToString("F2") + " " + st.Lean.transform.localEulerAngles.ToString("F0") + ", parent du joueur " + (pl.transform.parent != null ? pl.transform.parent.name : "-"));
                        if (off.magnitude < 0.08f) mWhy += " ne se penche pas";
                        leanForce = -1; mLeanAt = 0f;
                    }
                    if (mCycle == 0) { st.T.Yeux = eyesSaved; gaugeFreeze = false; Autotest.CaptureSoon("standalone-compteurs", 0f); Log.Info("standalone : monte, compteurs a " + (b.Rb.velocity.magnitude * 3.6f).ToString("F0") + " km/h, " + b.Dt.rpm.ToString("F0") + " tr/min"); }
                    d.Throttle = 0f; d.Brake = 1f; d.Gear = 1; mStep = 4; mAt = t;
                    if (b.Rb.velocity.magnitude < 1f) mWhy += " n'a pas roule";
                    if (pl.transform.parent != st.Pivot) mWhy += " ejecte en roulant";
                    break;
                case 4:
                    if (e < 2.5f && b.Rb.velocity.magnitude > 0.3f) break;
                    if (b.Rb.velocity.magnitude >= 0.5f) { mWhy += " pas arretee"; b.Rb.velocity = Vector3.zero; }
                    d.Brake = 0f; d.Gear = -1;
                    Exit(b, false); mStep = 5; mAt = t;
                    break;
                case 5:   // descendu : assis dans l'habitacle, petit
                    if (e < 1.0f) break;
                    {
                        FsmBool ps = FsmVariables.GlobalVariables.FindFsmBool("PlayerStop");
                        var cc = pl.GetComponent<CharacterController>();
                        Vector3 lp = ct.InverseTransformPoint(pl.transform.position);
                        if (pl.transform.parent != null) mWhy += " encore range";
                        if (ps != null && ps.Value) mWhy += " PlayerStop vrai";
                        if (cc != null && !cc.enabled) mWhy += " CharacterController coupe";
                        if (cabinCar != b || Mathf.Abs(lp.x) > st.Cabin.max.x + 0.05f) mWhy += " pas reste dans l'habitacle (" + lp.ToString("F2") + ")";
                        // sortie a pied : un cycle sur deux par la portiere passager (ouverte), sinon par celle du conducteur
                        mSide = mCycle % 2 == 1 ? 1 : 0;
                        if (mSide == 1) { st.DoorOpen[1] = true; st.DoorAngle[1] = 70f; st.Doors[1].localRotation = Quaternion.Euler(0f, -70f, 0f); }
                        mStep = 6; mAt = t;
                    }
                    break;
                case 6:   // marche vers dehors, 3 s au plus
                    if (e < 3f)
                    {
                        var cm = pl.GetComponent<CharacterController>();
                        Bounds db = st.DoorBox[mSide];
                        Vector3 goal = ct.TransformPoint(new Vector3((mSide == 0 ? -1f : 1f) * (st.HalfWidth + 1.0f), 0f, db.center.z - 0.15f));
                        Vector3 d0 = Vector3.ProjectOnPlane(goal - pl.transform.position, Vector3.up);
                        if (cm != null && cm.enabled && d0.magnitude > 0.08f) cm.Move(d0.normalized * Mathf.Min(1.5f * Time.deltaTime, d0.magnitude) + Vector3.down * 0.2f * Time.deltaTime);
                        break;
                    }
                    {
                        Vector3 lp = ct.InverseTransformPoint(pl.transform.position);
                        if (Mathf.Abs(lp.x) < st.HalfWidth + 0.1f) mWhy += " pas sorti par la portiere " + (mSide == 0 ? "conducteur" : "passager") + " (" + lp.ToString("F2") + ")";
                        if (cabinCar == b) mWhy += " PlayerInCar encore vrai dehors";
                        if (pl.transform.position.y < VazTenue.TrackY - 0.2f || pl.transform.position.y > VazTenue.TrackY + 3f) mWhy += " hauteur " + (pl.transform.position.y - VazTenue.TrackY).ToString("F2");
                        if (mSide == 1) { st.DoorOpen[1] = false; st.DoorAngle[1] = 0f; st.Doors[1].localRotation = Quaternion.identity; }
                        bool ok = mWhy.Length == 0;
                        if (ok) mOk++;
                        Log.Info("standalone : monte " + (mCycle + 1) + "/20 (cap " + ((mCycle % 3) * 120) + ", sortie " + (mSide == 0 ? "conducteur" : "passager") + ") " + (ok ? "OK" : "ECHEC :" + mWhy) + ", joueur dehors a " + lp.ToString("F2") + " de la voiture");
                        mCycle++;
                        if (mCycle >= Config.GetInt("Test", "StandaloneCycles", 20)) { Log.Info("standalone : monte fini, " + mOk + "/" + mCycle + " reussis"); mStep = Config.GetInt("Test", "StandaloneChoc", 1) != 0 ? 10 : 99; mAt = t; }
                        else { mStep = 0; mAt = t; }
                    }
                    break;
                case 10:   // bosses et saut a 100 km/h, au volant : pas de mort attendue
                {
                    HeadJointProbe();
                    int tl = LayerMask.NameToLayer("Terrain");
                    Vector3 p0 = new Vector3(-200f, VazTenue.TrackY + 0.6f, -400f);
                    // (bosses arrondies : cylindres couches en travers, 15 cm hors du sol ; tremplin : 7 m a 12 degres)
                    for (int k = 0; k < 6; k++)
                    {
                        var bump = GameObject.CreatePrimitive(PrimitiveType.Cylinder); bump.name = "MWCoop-BosseEssai";
                        if (tl >= 0) bump.layer = tl;
                        bump.transform.position = new Vector3(p0.x, VazTenue.TrackY - 0.20f, p0.z + 40f + k * 6f);
                        bump.transform.rotation = Quaternion.Euler(0f, 0f, 90f); bump.transform.localScale = new Vector3(0.7f, 6f, 0.7f);
                    }
                    var ramp = GameObject.CreatePrimitive(PrimitiveType.Cube); ramp.name = "MWCoop-TremplinEssai";
                    if (tl >= 0) ramp.layer = tl;
                    ramp.transform.rotation = Quaternion.Euler(-12f, 0f, 0f); ramp.transform.localScale = new Vector3(10f, 0.4f, 7f);
                    ramp.transform.position = new Vector3(p0.x, VazTenue.TrackY + 0.53f, p0.z + 95f);
                    PlaceSeated(b, st, pl, p0);
                    d.Keys = false; d.Handbrake = 0f; d.Gear = 6; d.Throttle = 1f; d.Brake = 0f;
                    b.Rb.velocity = ct.forward * 27.8f; foreach (Wheel w in b.Car.GetComponentsInChildren<Wheel>()) w.angularVelocity = 27.8f / Mathf.Max(0.1f, w.radius);
                    if (st.Sensor != null) st.Sensor.Reset();
                    crashMaxH = crashMaxV = 0f; mFly = 0f;
                    mStep = 11; mAt = t;
                    Log.Info("standalone : choc, bosses (6 x 15 cm) et tremplin a 100 km/h");
                    break;
                }
                case 11:
                    if (b.Car.transform.position.y > VazTenue.TrackY + 1.6f) mFly = Mathf.Max(mFly, b.Car.transform.position.y - VazTenue.TrackY);
                    if (driving != b) { Log.Info("standalone : choc, ECHEC : mort sur les bosses ou le saut"); mStep = 12; mAt = t; break; }
                    if (e < 7f) break;
                    Log.Info("standalone : choc, bosses et saut OK, pas de mort (vitesse " + (b.Rb.velocity.magnitude * 3.6f).ToString("F0") + " km/h, saut jusqu'a " + mFly.ToString("F1") + " m, plus grand choc " + crashMaxH.ToString("F0") + " km/h a l'horizontale / " + crashMaxV.ToString("F0") + " a la verticale)");
                    mStep = 12; mAt = t;
                    break;
                case 12:   // mur a 60 km/h, de face : mort attendue
                {
                    if (driving == b) { d.Throttle = 0f; d.Brake = 1f; }
                    if (e < 1f) break;
                    Vector3 p0 = new Vector3(200f, VazTenue.TrackY + 0.6f, -400f);
                    var wall = GameObject.CreatePrimitive(PrimitiveType.Cube); wall.name = "MWCoop-MurEssai";
                    int tl = LayerMask.NameToLayer("Terrain"); if (tl >= 0) wall.layer = tl;   // (la caisse ne heurte pas la couche Default)
                    wall.transform.position = p0 + new Vector3(0f, 1f, 18f); wall.transform.localScale = new Vector3(20f, 3f, 1f);
                    PlaceSeated(b, st, pl, p0);
                    d.Keys = false; d.Handbrake = 0f; d.Gear = 4; d.Throttle = 0.3f; d.Brake = 0f;
                    b.Rb.velocity = ct.forward * 16.7f; foreach (Wheel w in b.Car.GetComponentsInChildren<Wheel>()) w.angularVelocity = 16.7f / Mathf.Max(0.1f, w.radius);
                    if (st.Sensor != null) st.Sensor.Reset();
                    crashMaxH = crashMaxV = 0f;
                    mStep = 13; mAt = t;
                    Log.Info("standalone : choc, mur a 18 m, 60 km/h");
                    break;
                }
                case 13:
                    if (driving != b) { Log.Info("standalone : choc fini, mur : conducteur mort"); mStep = 99; break; }
                    if (e > 6f) { Log.Info("standalone : choc fini, mur : PAS de mort (plus grand choc " + crashMaxH.ToString("F0") + " km/h, vitesse " + (b.Rb.velocity.magnitude * 3.6f).ToString("F0") + ")"); mStep = 99; }
                    break;
            }
        }
    }
}
