using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace MWCoop
{
    // Pilote « addon standalone » (spec du 28/10, JD via [Directives]) : la Rihno 5 Maxi sur My Summer Car construite par notre code
    // avec les composants UnityCar du jeu -- plus une copie de la Ruscko : rien de ses automates (SteerLimit, prise d'air noyee,
    // frein a main tenu, puissance et masse reecrites), seulement ce qu'on pose. Cachee : [Test] AddonStandalone=1 ; sans ce
    // reglage, aucun code d'ici ne tourne.
    // Construction (ordre impose par l'initialisation d'UnityCar, code du jeu relu le 28/10) : racine eteinte -> Rigidbody, puis
    // Axles, Drivetrain, CarDynamics, notre controleur (StandaloneDriver) ; leurs champs de valeur repris de la Ruscko de la scene (ni ses objets, ni
    // ses references : chaque composant retrouve ses voisins a l'Awake) ; essieux neufs branches sur nos quatre roues (enfants de la
    // racine : Wheel.Awake remonte les parents) ; Axles.SetWheels appele avant l'allumage (CarDynamics.Awake et Drivetrain.Awake lisent
    // les roues des essieux, sans ordre garanti entre les Awake) ; objet CoG a nous ; puis allumee. Les reglages de tenue de route
    // sont ensuite ceux de la R5 Maxi copie (VazHandling : base de la Satsuma, Spec, rallye) pour que JD compare a l'identique.
    public static partial class StandaloneCar
    {
        public static bool On { get { return Game.Msc && Config.GetInt("Test", "AddonStandalone", 0) != 0; } }
        public const string Prefix = "R5MAXI_STANDALONE";
        static readonly List<VazCar.Built> cars = new List<VazCar.Built>();
        // (par voiture : sa tenue de route lue, son centre de gravite, ses echeances)
        class State
        {
            public StandaloneTenue T; public Transform Cog; public float NextGrip, NextFile;
            public Transform Pivot; public float HalfWidth; public bool EngineOn, LightsOn; public float CrankT = -1f; public Light[] Heads;   // (StandaloneDrive)
            public Transform[] Doors; public Vector3[] DoorCenter; public bool[] DoorOpen; public float[] DoorAngle;
        }
        static readonly Dictionary<VazCar.Built, State> states = new Dictionary<VazCar.Built, State>();
        // Course de suspension au-dessus du moyeu (m) : Wheel pose son modele a (compression - course) sous son objet ; ~0,10 au repos
        // avec les ressorts de la R5 (rallye : 25 000 x 905 / 507,6 N/m, course 0,15) -> moyeux du modele a la hauteur de infos.txt.
        public const float HubDrop = 0.10f;

        public static VazCar.Built Build(string name, int number, Color paint, Vector3 pos, Quaternion rot, VazCar.Model m)
        {
            if (!On) return null;
            GameObject tpl = VazCar.Template();
            if (tpl == null) { Log.Warn("standalone : Ruscko de la scene introuvable"); return null; }
            if (m == null || !m.HasFiles) { Log.Warn("standalone : fichiers du modele absents"); return null; }
            Dictionary<string, Vector3> inf = VazCar.ModelInfo(m);
            Wheel[] tw = tpl.GetComponentsInChildren<Wheel>(true);
            if (tw.Length < 4) { Log.Warn("standalone : roues de la Ruscko introuvables"); return null; }
            Bounds wb;
            if (!VazCar.ModelBounds(m, "roue.p205", out wb)) { Log.Warn("standalone : roue.p205 absente"); return null; }
            float radius = Mathf.Max(wb.extents.y, wb.extents.z);
            if (radius < 0.15f || radius > 0.6f) { Log.Warn("standalone : rayon de roue hors bornes (" + radius.ToString("F3") + ")"); return null; }

            var b = new VazCar.Built { Number = number, Shown = number, M = m, Paint = paint };
            var go = new GameObject(name);
            go.SetActive(false);
            go.layer = tpl.layer;
            Transform ct = go.transform;
            ct.position = pos; ct.rotation = rot;
            b.Car = go;

            // 1. Corps et composants UnityCar (Rigidbody d'abord : RequireComponent)
            Rigidbody trb = tpl.GetComponent<Rigidbody>();
            Rigidbody rb = go.AddComponent<Rigidbody>();
            if (trb != null) { rb.mass = trb.mass; rb.drag = trb.drag; rb.angularDrag = trb.angularDrag; rb.interpolation = trb.interpolation; rb.collisionDetectionMode = trb.collisionDetectionMode; rb.maxAngularVelocity = trb.maxAngularVelocity; rb.solverIterationCount = trb.solverIterationCount; }
            Axles axles = go.AddComponent<Axles>();
            Drivetrain dt = go.AddComponent<Drivetrain>();
            CarDynamics cd = go.AddComponent<CarDynamics>();
            StandaloneDriver ac = go.AddComponent<StandaloneDriver>();
            int n = Copy(tpl.GetComponent<Drivetrain>(), dt) + Copy(tpl.GetComponent<CarDynamics>(), cd) + Copy(tpl.GetComponent<CarController>(), ac, typeof(CarController));
            CarDynamics tcd = tpl.GetComponent<CarDynamics>();
            if (tcd != null) cd.skidmarks = tcd.skidmarks;   // (traces de pneus de la scene, partagees)
            // (controleur « externe » pour CarDynamics : sinon son Awake ajoute un AxisCarController ; Drivetrain prend le notre)
            cd.controller = CarDynamics.Controller.external;

            // 2. Roues : objets enfants a la place des moyeux du modele (+ course), Wheel de la Ruscko de meme position, modele visible
            //    (roue.p205, retournee pour le cote droit comme VazCar.MscWheels) dans l'objet model que Wheel deplace et fait tourner.
            Axles tax = tpl.GetComponent<Axles>();
            var front = new Axle(); var rear = new Axle();
            if (tax != null) { n += Copy(tax.frontAxle, front); n += Copy(tax.rearAxle, rear); }
            int wheelLayer = tw[0].gameObject.layer;
            foreach (string wn in new[] { "wheel_lf", "wheel_rf", "wheel_lr", "wheel_rr" })
            {
                Vector3 hub;
                if (!inf.TryGetValue(wn, out hub)) { Log.Warn("standalone : " + wn + " absent de infos.txt"); UnityEngine.Object.Destroy(go); return null; }
                bool left = wn[6] == 'l', fr = wn[7] == 'f';
                Wheel src = null;
                foreach (Wheel x in tw) { Vector3 lp = tpl.transform.InverseTransformPoint(x.transform.position); if ((lp.x < 0f) == left && (lp.z > 0f) == fr) { src = x; break; } }
                var wg = new GameObject((fr ? "wheelF" : "wheelR") + (left ? "L" : "R"));
                wg.layer = wheelLayer;
                wg.transform.SetParent(ct, false);
                wg.transform.localPosition = hub + Vector3.up * HubDrop;
                Wheel w = wg.AddComponent<Wheel>();
                if (src != null) n += Copy(src, w);
                w.radius = radius; w.width = wb.size.x;
                var model = new GameObject("model");
                model.transform.SetParent(wg.transform, false);
                Quaternion q = left ? Quaternion.identity : Quaternion.Euler(0f, 180f, 0f);
                var rw = new GameObject("VAZ_roue").transform;
                rw.SetParent(model.transform, false);
                rw.localRotation = q; rw.localPosition = -(q * wb.center);
                VazCar.AddModel(b, rw, "roue.p205");
                w.model = model; w.caliperModel = null;
                if (fr) { if (left) front.leftWheel = w; else front.rightWheel = w; }
                else { if (left) rear.leftWheel = w; else rear.rightWheel = w; }
            }
            axles.frontAxle = front; axles.rearAxle = rear; axles.otherAxles = new Axle[0];
            axles.SetWheels();

            // 3. Carrosserie (a l'origine de la racine : repere du modele), portieres et volant fixes pour le pilote
            var body = new GameObject("VAZ").transform;
            body.SetParent(ct, false);
            b.Body = body;
            foreach (string f in new[] { "carrosserie.p205", "capot.p205", "hayon.p205" })   // (portieres : sur charniere, SetupDrive)
                if (System.IO.File.Exists(System.IO.Path.Combine(m.Dir, f))) VazCar.AddModel(b, body, f);
            Vector3 sc;
            if (inf.TryGetValue("volant_centre", out sc))
            {
                b.SteerPivot = new GameObject("VAZ_volant").transform;
                b.SteerPivot.SetParent(body, false);
                b.SteerPivot.localPosition = sc;
                var inner = new GameObject("pieces").transform;
                inner.SetParent(b.SteerPivot, false);
                inner.localPosition = -sc;
                VazCar.AddModel(b, inner, "volant.p205");
                Vector3 axv;
                if (inf.TryGetValue("volant_axe", out axv) && axv.sqrMagnitude > 0.1f) b.SteerAxis = axv.normalized;
            }

            // 4. Collisions : deux boites d'apres la carrosserie (bas de caisse sur toute la longueur, habitacle plus etroit), sur la
            //    couche du plus gros collider plein de la Ruscko -- la couche de la racine (Datsun) ne heurte pas le terrain : premier
            //    essai du 28/10, retournee au depart puis passee sous la carte. Le rayon des roues ignore cette couche-la aussi : il ne
            //    voit pas la racine ni les roues, et les boites restent au-dessus du bas des pneus.
            int bodyLayer = BodyLayer(tpl, wheelLayer);
            Bounds bb;
            if (VazCar.ModelBounds(m, "carrosserie.p205", out bb))
            {
                float floor = Mathf.Max(bb.min.y, inf["wheel_lf"].y - radius * 0.4f), belt = bb.min.y + bb.size.y * 0.55f;
                Box(ct, bodyLayer, "boite_caisse", new Vector3(bb.center.x, (floor + belt) * 0.5f, bb.center.z), new Vector3(bb.size.x * 0.97f, belt - floor, bb.size.z * 0.97f));
                Box(ct, bodyLayer, "boite_habitacle", new Vector3(bb.center.x, (belt + bb.max.y) * 0.5f, bb.center.z - bb.size.z * 0.08f), new Vector3(bb.size.x * 0.8f, bb.max.y - belt, bb.size.z * 0.5f));
                // (le joueur traversait la caisse -- JD 28/10, capture : il ne heurte pas cette couche ; les voitures du jeu ont pour lui
                // des colliders a part, Colliders/PlayerColl : memes boites sur leur couche)
                int pl = PlayerLayer(tpl);
                if (pl >= 0 && pl != bodyLayer)
                {
                    Box(ct, pl, "joueur_caisse", new Vector3(bb.center.x, (floor + belt) * 0.5f, bb.center.z), new Vector3(bb.size.x * 0.97f, belt - floor, bb.size.z * 0.97f));
                    Box(ct, pl, "joueur_habitacle", new Vector3(bb.center.x, (belt + bb.max.y) * 0.5f, bb.center.z - bb.size.z * 0.08f), new Vector3(bb.size.x * 0.8f, bb.max.y - belt, bb.size.z * 0.5f));
                }
            }

            // 5. Centre de gravite : objet a nous (CarDynamics.Start le recopie dans le corps), entre les essieux, 0,3 m au-dessus
            //    des moyeux (VazHandling le recale ensuite d'apres la Spec)
            var cog = new GameObject("CoG").transform;
            cog.SetParent(ct, false);
            float zF = (inf["wheel_lf"].z + inf["wheel_rf"].z) * 0.5f, zR = (inf["wheel_lr"].z + inf["wheel_rr"].z) * 0.5f;
            cog.localPosition = new Vector3(0f, inf["wheel_lf"].y + 0.3f, zR + (m.Sp != null ? m.Sp.Com : 0.5f) * (zF - zR));
            cd.centerOfMass = cog;

            // 6. Allumee ; base de la R5 Maxi copie (VazHandling : moteur, pneus et matieres de sol de la Satsuma, Spec), puis tenue.ini
            //    par-dessus -- avant le Start d'UnityCar, qui passe essieux et freins aux roues
            go.SetActive(true);
            b.Rb = rb; b.Dt = dt; b.Axis = ac;
            VazHandling.Apply(b, true);
            StandaloneTenue tune = StandaloneTenue.Load(m);
            tune.Apply(b, cog, true);
            if (!Sane(b)) { Log.Warn("standalone : valeurs invalides apres reglage, voiture retiree"); UnityEngine.Object.Destroy(go); return null; }
            cars.Add(b);
            Axis("Throttle");   // (journal des touches du jeu, une fois)
            var stNew = new State { T = tune, Cog = cog };
            states[b] = stNew;
            SetupDrive(b, stNew, inf);
            Rigidbody trb2 = tpl.GetComponent<Rigidbody>();
            Log.Info("standalone : inertie " + rb.inertiaTensor.ToString("F0") + " (Ruscko " + (trb2 != null ? trb2.inertiaTensor.ToString("F0") + " pour " + trb2.mass.ToString("F0") + " kg" : "?") + "), centre " + rb.centerOfMass.ToString("F2"));
            Log.Info("standalone : " + name + " construite en " + pos.ToString("F1") + " (" + n + " valeurs reprises de " + tpl.name + ", roues rayon " + radius.ToString("F3") + " largeur " + wb.size.x.ToString("F3")
                     + ", " + dt.transmission + ", " + rb.mass.ToString("F0") + " kg, couche " + LayerMask.LayerToName(go.layer) + " / roues " + LayerMask.LayerToName(wheelLayer) + ")");
            return b;
        }

        // Couche du plus gros collider plein de la voiture modele (hors roues), journal des couches que le terrain heurte
        static int PlayerLayer(GameObject tpl)
        {
            foreach (Collider c in tpl.GetComponentsInChildren<Collider>(true))
                if (!c.isTrigger && c.name == "PlayerColl")
                {
                    int l = c.gameObject.layer; GameObject pl = Game.PlayerGo;
                    Log.Info("standalone : collision joueur sur la couche " + LayerMask.LayerToName(l) + (pl != null ? ", le joueur (" + LayerMask.LayerToName(pl.layer) + ") la heurte : " + !Physics.GetIgnoreLayerCollision(l, pl.layer) : ""));
                    return l;
                }
            Log.Warn("standalone : PlayerColl de la Ruscko introuvable : pas de collision pour le joueur");
            return -1;
        }

        static int BodyLayer(GameObject tpl, int wheelLayer)
        {
            Collider best = null; float vol = 0f;
            foreach (Collider c in tpl.GetComponentsInChildren<Collider>(true))
            {
                if (c.isTrigger || c.gameObject.layer == wheelLayer) continue;
                Vector3 z = c.bounds.size; float v = z.x * z.y * z.z;
                if (v > vol) { vol = v; best = c; }
            }
            int l = best != null ? best.gameObject.layer : tpl.layer, terrain = LayerMask.NameToLayer("Terrain");
            Log.Info("standalone : caisse sur la couche " + LayerMask.LayerToName(l) + " (" + (best != null ? Recon.Path(best.transform) : "racine") + ")"
                     + (terrain >= 0 ? ", heurte le terrain : " + !Physics.GetIgnoreLayerCollision(l, terrain) + " (racine " + LayerMask.LayerToName(tpl.layer) + " : " + !Physics.GetIgnoreLayerCollision(tpl.layer, terrain) + ")" : ""));
            return l;
        }

        static void Box(Transform parent, int layer, string name, Vector3 center, Vector3 size)
        {
            var g = new GameObject(name);
            g.layer = layer;
            g.transform.SetParent(parent, false);
            var c = g.AddComponent<BoxCollider>();
            c.center = center; c.size = new Vector3(Mathf.Max(0.1f, size.x), Mathf.Max(0.1f, size.y), Mathf.Max(0.1f, size.z));
        }

        // Champs de valeur d'un composant (ou d'un essieu) vers un autre de meme type : nombres, booleens, enums, vecteurs, courbes et
        // tableaux de valeurs clones ; parmi les objets Unity, seulement les ressources partagees (matieres, sons) -- jamais un
        // composant ni un objet de la scene (references retrouvees a l'Awake).
        // (top : la classe de base dont les champs sont repris -- CarController, de l'AxisCarController de la Ruscko vers le notre)
        static int Copy(object from, object to, Type top = null)
        {
            if (from == null || to == null) return 0;
            if (top == null ? from.GetType() != to.GetType() : !top.IsInstanceOfType(from) || !top.IsInstanceOfType(to)) return 0;
            int n = 0;
            for (Type k = top ?? from.GetType(); k != null && k != typeof(MonoBehaviour) && k != typeof(Behaviour) && k != typeof(Component) && k != typeof(object); k = k.BaseType)
                foreach (FieldInfo f in k.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                {
                    if (f.IsLiteral || f.IsInitOnly) continue;
                    Type ft = f.FieldType;
                    object v;
                    try { v = f.GetValue(from); } catch { continue; }
                    if (typeof(UnityEngine.Object).IsAssignableFrom(ft))
                    {
                        if (v is PhysicMaterial || v is Material || v is AudioClip || v is Texture) { f.SetValue(to, v); n++; }
                        continue;
                    }
                    if (ft.IsArray)
                    {
                        Type e = ft.GetElementType();
                        if (v == null || !(e.IsPrimitive || e.IsEnum || e == typeof(Vector3) || e == typeof(Vector2) || e == typeof(PhysicMaterial))) continue;
                        f.SetValue(to, ((Array)v).Clone()); n++;
                        continue;
                    }
                    if (ft == typeof(AnimationCurve)) { if (v != null) { f.SetValue(to, new AnimationCurve(((AnimationCurve)v).keys)); n++; } continue; }
                    if (ft.IsValueType || ft == typeof(string)) { f.SetValue(to, v); n++; }
                }
            return n;
        }

        // Jamais de NaN ni d'infini dans le corps, les essieux et les roues (regle du 21/10)
        static bool Sane(VazCar.Built b)
        {
            Rigidbody rb = b.Rb;
            if (rb == null || Bad(rb.mass) || rb.mass < 100f || Bad(rb.centerOfMass.x + rb.centerOfMass.y + rb.centerOfMass.z)) return false;
            foreach (Wheel w in b.Car.GetComponentsInChildren<Wheel>(true))
                if (Bad(w.radius) || Bad(w.suspensionRate) || Bad(w.bumpRate) || Bad(w.reboundRate) || Bad(w.mass)) return false;
            return true;
        }
        static bool Bad(float f) { return float.IsNaN(f) || float.IsInfinity(f); }

        public static VazCar.Built Find(GameObject car) { foreach (VazCar.Built b in cars) if (b.Car == car) return b; return null; }

        public static void Remove(VazCar.Built b)
        {
            if (b == null) return;
            if (driving == b) Exit(b, true);   // (jamais detruire la voiture avec le joueur dedans : le PLAYER partirait avec)
            cars.Remove(b); states.Remove(b);
            if (b.Car != null) UnityEngine.Object.Destroy(b.Car);
        }

        // Vaz.Update / Vaz.LateUpdate : tenue de route a chaque image (braquage selon la vitesse, adherence selon le sol, embrayage
        // ouvert au frein a main), tenue.ini relue quand elle change ou a la touche ; ralenti apres la physique ; voitures detruites
        // retirees. Rien n'est remis toutes les 2 s comme sur la copie : aucun automate du jeu ne reecrit cette voiture.
        static KeyCode reloadKey = KeyCode.None;
        public static void Update()
        {
            if (cars.Count == 0) return;
            float now = Time.realtimeSinceStartup;
            if (reloadKey == KeyCode.None) { try { reloadKey = (KeyCode)Enum.Parse(typeof(KeyCode), Config.Get("Test", "StandaloneTouche", "F9"), true); } catch { reloadKey = KeyCode.F9; } }
            bool key = Input.GetKeyDown(reloadKey);
            for (int i = cars.Count - 1; i >= 0; i--)
            {
                VazCar.Built b = cars[i];
                State st;
                if (b.Car == null || !states.TryGetValue(b, out st)) { if (driving == b) Exit(b, true); cars.RemoveAt(i); states.Remove(b); continue; }
                if (key || (now >= st.NextFile && st.T.Changed())) { Reload(b, st, key ? "touche " + reloadKey : "fichier enregistre"); }
                if (now >= st.NextFile) st.NextFile = now + 1f;
                b.Dt.engineOrientation = b.Car.transform.forward;
                var dr = b.Axis as CarController;
                bool hb = dr != null && dr.handbrakeInput > 0.3f;
                st.T.Tick(b, hb);
                if (hb != b.Handbraking || now >= st.NextGrip) { b.Handbraking = hb; st.NextGrip = now + 0.25f; st.T.Grip(b, hb); }
                if (dr != null && b.Dt.rpm > 300f && (dr.handbrakeInput > 0.05f || (dr.brakeInput > 0.2f && b.Dt.rpm < 2000f))) Disengage(b.Dt);
                Drive(b, st);
            }
        }

        static void Reload(VazCar.Built b, State st, string why)
        {
            StandaloneTenue t = StandaloneTenue.Load(b.M);
            t.Apply(b, st.Cog, false);
            st.T = t;
            Log.Info("standalone : tenue.ini rechargee (" + why + ") : " + t.Masse + " kg, " + t.Puissance + " ch, ressorts " + t.Av.Ressort + " / " + t.Ar.Ressort + ", braquage " + t.BraquageMax + ", " + t.Transmission);
            HudMsg("tenue.ini rechargee", "tenue.ini reloaded");
        }
        static void HudMsg(string fr, string en) { try { Hud.Toast(Lang.T(fr, en)); } catch { } }

        // (embrayage ouvert : sinon frein a main tire ou freinage a bas regime calent le moteur -- comme VazHandling sur la copie)
        static FieldInfo engagingField; static MethodInfo disengage;
        static void Disengage(Drivetrain dt)
        {
            if (engagingField == null) engagingField = typeof(Drivetrain).GetField("engaging", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (disengage == null) disengage = typeof(Drivetrain).GetMethod("DisengageClutch", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (engagingField != null) engagingField.SetValue(dt, false);
            if (disengage != null) disengage.Invoke(dt, null);
        }

        // [Test] StandaloneJournal=1 (banc vaztenue) : toutes les 2 s, pour la standalone ou la copie du F10 (comparaison), corps (vitesse, hauteur, roulis, contacts de la caisse) et chaque roue (au sol,
        // charge, compression, rotation, glissement, effort longitudinal, adherence, matiere du sol)
        static float nextLog;
        static void Journal(GameObject car)
        {
            Rigidbody rb = car.GetComponent<Rigidbody>(); Drivetrain dt = car.GetComponent<Drivetrain>();
            if (rb == null || dt == null) return;
            var sb = new System.Text.StringBuilder("standalone : journal " + car.name + " ");
            sb.Append((rb.velocity.magnitude * 3.6f).ToString("F0")).Append(" km/h, pos ").Append(car.transform.position.ToString("F1")).Append(", roulis ").Append(Vector3.Angle(car.transform.up, Vector3.up).ToString("F0"))
              .Append(", regime ").Append(dt.rpm.ToString("F0")).Append(" rapport ").Append(dt.gear).Append(" embrayage ").Append(dt.clutch != null ? dt.clutch.GetClutchPosition().ToString("F2") : "?")
              .Append(", gaz ").Append(dt.throttle.ToString("F2")).Append(rb.isKinematic ? ", CINEMATIQUE" : "");
            var dr = car.GetComponent<CarController>();
            if (dr != null) sb.Append(", entrees gaz ").Append(dr.throttleInput.ToString("F2")).Append(" frein ").Append(dr.brakeInput.ToString("F2")).Append(" fam ").Append(dr.handbrakeInput.ToString("F2")).Append(" dir ").Append(dr.steerInput.ToString("F2"));
            Contacts c = car.GetComponent<Contacts>() ?? car.AddComponent<Contacts>();
            sb.Append(", caisse touche : ").Append(c.Last.Length > 0 ? c.Last : "rien");
            c.Last = "";
            foreach (Wheel w in car.GetComponentsInChildren<Wheel>())
                sb.Append(" | ").Append(w.wheelPos).Append(w.onGroundDown ? " sol" : " AIR").Append(" N ").Append(w.normalForce.ToString("F0")).Append(" c ").Append(w.compression.ToString("F3"))
                  .Append(" fr ").Append(w.brake.ToString("F2")).Append("/").Append(w.handbrake.ToString("F2")).Append(" w ").Append(w.angularVelocity.ToString("F0")).Append(" gl ").Append(w.slipRatio.ToString("F2")).Append(" Fx ").Append(w.Fx.ToString("F0"))
                  .Append(" adh ").Append(w.forwardGripFactor.ToString("F2")).Append("/").Append(w.sidewaysGripFactor.ToString("F2")).Append(" ").Append(w.physicMaterial != null ? w.physicMaterial.name : "-");
            Log.Info(sb.ToString());
        }

        public class Contacts : MonoBehaviour
        {
            public string Last = "";
            void OnCollisionStay(Collision c)
            {
                string n = c.collider != null ? c.collider.name : "?";
                if (Last.Length < 200 && Last.IndexOf(n) < 0) Last += (Last.Length > 0 ? ", " : "") + n + " (" + (c.contacts.Length > 0 ? c.contacts[0].thisCollider.name : "?") + ")";
            }
        }

        public static void LateUpdate()
        {
            foreach (VazCar.Built b in cars) if (b.Car != null) { VazHandling.Idle(b); State st; if (states.TryGetValue(b, out st)) DriveLate(b, st); }
        }

        // Entree du F10 (VazPanel) : la R5 standalone posee a 6 m devant le joueur, sur le sol ; ou retiree.
        public static bool Spawned { get { return cars.Count > 0; } }
        public static void Toggle()
        {
            if (cars.Count > 0) { foreach (VazCar.Built x in new List<VazCar.Built>(cars)) Remove(x); Log.Info("standalone : retiree (F10)"); return; }
            Transform p = Game.PlayerT;
            if (p == null) return;
            Vector3 fw = Vector3.ProjectOnPlane(p.forward, Vector3.up).normalized;
            if (fw.sqrMagnitude < 0.5f) fw = Vector3.forward;
            Vector3 at = p.position + fw * 6f;
            float gy;
            at.y = Ground(at + Vector3.up * 4f, p, null, out gy) ? gy + 0.6f : p.position.y + 0.5f;
            if (at.y < -4f) { HudMsg("Trop bas (eau) : pas de voiture ici", "Too low (water): no car here"); return; }
            Quaternion rot = Quaternion.LookRotation(Quaternion.Euler(0f, 90f, 0f) * fw);   // (de profil : portiere conducteur vers le joueur)
            if (Build(Prefix, 7, Vaz.ColorOf(7), at, rot, VazCar.R5) == null) HudMsg("R5 standalone : construction impossible (journal)", "Standalone R5: could not build (log)");
            else Log.Info("standalone : posee depuis le F10");
        }

        // [Test] Autotest=vaztenue + StandaloneEssai=1 : a 19 s, la R5 standalone posee a 6 m devant le joueur ; le banc la prend avec
        // [Test] VazTenueVoiture=R5MAXI_STANDALONE.
        static bool tested;
        public static void Test(float t)
        {
            if (Config.GetInt("Test", "StandaloneJournal", 0) != 0 && Time.realtimeSinceStartup >= nextLog)
            {
                nextLog = Time.realtimeSinceStartup + 2f;
                Transform jc = cars.Count > 0 && cars[0].Car != null ? cars[0].Car.transform : Vaz.CarOf(Vaz.MeId);
                if (jc != null) Journal(jc.gameObject);
            }
            if (tested || t < 19f || !On || Config.GetInt("Test", "StandaloneEssai", 0) == 0) return;
            tested = true;
            Transform p = Game.PlayerT;
            if (p == null) { Log.Warn("standalone : essai sans joueur"); return; }
            Vector3 fw = Vector3.ProjectOnPlane(p.forward, Vector3.up).normalized;
            Vector3 at = p.position + fw * 6f + Vector3.up * 1f;
            if (at.y < -4f) at.y = -3f;
            Build(Prefix, 7, new Color(0.9f, 0.9f, 0.92f), at, Quaternion.LookRotation(fw), VazCar.R5);
        }

        // [Test] Autotest=standaloneroule (sans le banc) : la R5 standalone a 19 s, posee au milieu de la piste plate du banc (6 x 6 km a
        // 1 500 m, VazTenue.MakeTrack ; la cour de la maison la bloquait contre un objet), cap +z : 6 s pour se poser,
        // 1re et plein gaz 12 s (rapport superieur a 6 500 tr/min), pied leve 3 s, demi-braquage gauche 3 s, freinage a fond ; journal
        // chaque seconde (vitesse, position, cap, regime, rapport, roues).
        static float rollAt = -1f, rollShift; static bool rollDone; static int rollEdit; static string rollText;
        public static void Roll(float t)
        {
            Test(t);
            if (rollDone || cars.Count == 0 || cars[0].Car == null) return;
            VazCar.Built b = cars[0];
            var d = b.Axis as StandaloneDriver;
            if (d == null) return;
            if (rollAt < 0f)
            {
                rollAt = t;
                VazTenue.MakeTrack();
                Vector3 p0 = new Vector3(0f, VazTenue.TrackY + 0.6f, 0f);
                b.Rb.velocity = Vector3.zero; b.Rb.angularVelocity = Vector3.zero;
                b.Car.transform.position = p0; b.Car.transform.rotation = Quaternion.identity; b.Rb.position = p0; b.Rb.rotation = Quaternion.identity;
                Log.Info("standalone : roule, posee sur la piste, depart dans 6 s");
            }
            float e = t - rollAt;
            if (e < 6f) { d.Handbrake = 1f; return; }
            d.Handbrake = 0f; d.Brake = 0f; d.Steer = 0f; d.Throttle = 0f;
            if (!EngineRunning(b)) SetEngine(b, true);
            if (e < 18f)
            {
                d.Throttle = 1f;
                if (d.Gear < 2) d.Gear = 2;
                float slip = 0f; foreach (Wheel w in b.Car.GetComponentsInChildren<Wheel>()) if (w.isPowered) slip = Mathf.Max(slip, Mathf.Abs(w.slipRatio));
                if (b.Dt.rpm > 6500f && slip < 0.3f && t > rollShift && d.Gear < b.Dt.gearRatios.Length - 1) { d.Gear++; rollShift = t + 0.8f; }   // (pas en patinant)
            }
            else if (e < 21f) { }
            else if (e < 24f) d.Steer = -0.5f;
            else if (e < 28f) d.Brake = 1f;
            else if (e < 29f) { d.Gear = 1; d.Handbrake = 1f; }
            if (Time.realtimeSinceStartup >= nextLog) { nextLog = Time.realtimeSinceStartup + 1f; float fx = 0f; foreach (Wheel w in b.Car.GetComponentsInChildren<Wheel>()) fx += w.Fx; Log.Info("standalone : roule " + e.ToString("F0") + " s, somme Fx " + fx.ToString("F0") + " N, cap " + b.Car.transform.eulerAngles.y.ToString("F0") + " deg, vers " + Vector3.Angle(b.Rb.velocity, b.Car.transform.forward).ToString("F0") + " deg"); Journal(b.Car); }
            // relecture : tenue.ini modifiee a 29 s (masse 1200, ressort avant fou, puissance illisible), releve a 32 s, remise a 33 s
            State st;
            if (e >= 29f && rollEdit == 0 && states.TryGetValue(b, out st))
            {
                rollEdit = 1;
                rollText = System.IO.File.ReadAllText(st.T.File);
                string x = rollText.Replace("Masse=905", "Masse=1200").Replace("Puissance=355", "Puissance=abc");
                int i = x.IndexOf("Ressort="); int j = x.IndexOf(' ', i);
                x = x.Substring(0, i) + "Ressort=999999" + x.Substring(j);
                System.IO.File.WriteAllText(st.T.File, x, new System.Text.UTF8Encoding(true));
                Log.Info("standalone : roule, tenue.ini modifiee (masse 1200, ressort avant 999999, puissance abc)");
            }
            if (e >= 32f && rollEdit == 1 && states.TryGetValue(b, out st))
            {
                rollEdit = 2;
                Axles ax = b.Car.GetComponent<Axles>();
                Log.Info("standalone : roule, apres relecture : masse " + b.Rb.mass + ", ressort essieu avant " + ax.frontAxle.suspensionRate + " roue " + ax.frontAxle.leftWheel.suspensionRate + ", puissance " + b.Dt.maxPower + ", centre " + b.Rb.centerOfMass.ToString("F2"));
                System.IO.File.WriteAllText(st.T.File, rollText, new System.Text.UTF8Encoding(true));
            }
            if (e >= 34f && rollEdit == 2) { rollDone = true; Log.Info("standalone : roule fini, masse remise " + b.Rb.mass); }
        }
    }

    // Controleur de conduite de la voiture standalone (a nous : CarController du jeu, dont seules les entrees changent). L'AxisCarController
    // lit les touches du joueur par cInput : sans voiture menee, ses axes vides levaient une exception a chaque image (16 732 dans le
    // journal du jeu en 4 min, essai du 28/10) et ses entrees restaient figees. Ici les entrees sont celles que notre code pose
    // (banc d'essai, reseau, conduite) ; garee : frein a main serre, rien d'autre.
    public class StandaloneDriver : CarController
    {
        public float Throttle, Brake, Steer, Handbrake = 1f, Clutch;
        public bool StartEngine;
        public int Gear = -1;   // (rapport voulu ; -1 : celui de la boite)
        public bool Keys;       // (au volant d'ici : touches du jeu par cInput, axes et rapports comme l'AxisCarController de la Ruscko)

        protected override void GetInput(out float throttleInput, out float brakeInput, out float steerInput, out float handbrakeInput, out float clutchInput, out bool startEngineInput, out int targetGear)
        {
            startEngineInput = StartEngine;
            if (Keys)
            {
                throttleInput = Mathf.Clamp01(StandaloneCar.Axis("Throttle")); brakeInput = Mathf.Clamp01(StandaloneCar.Axis("Brake")); steerInput = StandaloneCar.Axis("Horizontal");
                handbrakeInput = Mathf.Clamp01(StandaloneCar.Axis("Handbrake")); clutchInput = Mathf.Clamp01(StandaloneCar.Axis("Clutch"));
                int g = drivetrain != null ? drivetrain.gear : 0;
                if (StandaloneCar.Down("ShiftUp")) g++;
                if (StandaloneCar.Down("ShiftDown")) g--;
                targetGear = drivetrain != null && drivetrain.gearRatios != null ? Mathf.Clamp(g, 0, drivetrain.gearRatios.Length - 1) : 0;
                return;
            }
            throttleInput = Throttle; brakeInput = Brake; steerInput = Steer; handbrakeInput = Handbrake; clutchInput = Clutch;
            targetGear = Gear >= 0 && drivetrain != null && drivetrain.gearRatios != null && Gear < drivetrain.gearRatios.Length ? Gear : drivetrain != null ? drivetrain.gear : 0;
        }
    }
}
