using System.Collections.Generic;
using System.Reflection;
using HutongGames.PlayMaker;
using UnityEngine;

namespace MWCoop
{
    // Tenue de route de la VAZ-2108 (copie de SORBET : UnityCar) : celle de la Rivett (CORRIS) montee en rallye -- demande de
    // JD, 18/10 : « qu'elle ait le meme comportement ». Les reglages de base de la Rivett (moteur, embrayage, essieux : freins,
    // anti-roulis, carrossage, pneus ; roues : formules de pneu, masse) ne sont pas dans le vidage du jeu : lus sur la CORRIS de
    // la scene, une fois, a la construction. Par-dessus, ce que posent ses automates avec les pieces de rallye (vidage
    // ref/v.261008-01, releve du 18/10) :
    //  - arbre a cames rallye : puissance maxi a 7 500 tr/min, couple maxi a 4 800 ; carburateur de course, collecteur c0,
    //    echappement complet : multiplicateur de puissance ~1,43 (Valves / Cylinders) ; volant moteur allege (inertie 0,072) ;
    //    frottement du moteur 0,35 ;
    //  - boite 5 (3,65 / 1,97 / 1,37 / 1,00 / 0,82, MA 3,324), pont 4,44 autobloquant (differentialLockCoefficient 49) ;
    //    propulsion, comme la Rivett ;
    //  - ressorts 7,5" 200 lb (35 020 N/m, course 0,19), amortisseurs course 2 200 / 2 200 ;
    //  - cremaillere : braquage 26 ; repartition du freinage 0,6 avant ;
    //  - pneus : cloutes de course (StuddedRacing), adherence selon le sol comme la Rivett (WHEELc_* 'Data' / 'SetGrip') : nom
    //    de la PhysicMaterial sous la roue (Wheel.physicMaterial) -> glace de course 1,65, route enneigee 1,60, route glacee
    //    1,25, neige profonde 1,0, autre : la valeur d'avant ; + aide d'adherence des options du jeu (OptionsGripHelper),
    //    bornee a [0,1 ; 1,7] ; par essieu (CarDynamics.SetWheelsParams recopie les essieux dans les roues a chaque pas) :
    //    la plus faible de ses deux roues. Matieres du sol (CarDynamics.physicMaterials : adherence propre a chaque sol) : celles
    //    de la Rivett.
    // Automates de la SORBET copies avec elle et qui ecraseraient tout cela (releve du vidage) : 'SetGrip' des roues (0,45
    // fixe, chaque image) et 'Suspension' de Simulation/CoG (ressorts et amortisseurs de la SORBET, chaque seconde) : coupes ;
    // 'OperatingTemp' (powerMultiplier = 1 / starter, ~chaque seconde) : powerMultiplier remis chaque image. Le frottement
    // du moteur reste celui du jeu (le demarreur le monte a 6 le temps de lancer le moteur).
    // Masse : celle de la Rivett de la scene si elle est montee (plus de 600 kg), sinon 950 kg. Sans CORRIS : RALLYCAR3.
    // Remis toutes les 2 s (des automates de la SORBET reecrivent puissance, masse, braquage). Corrections des voitures du pack
    // prive de JD : reaction du moteur (Drivetrain.FixedUpdate : -engineOrientation * couple en repere MONDE ; la SORBET met
    // (0,1,0) et la voiture tournerait sur elle-meme) sur l'axe longitudinal, chaque image ; SteerLimit (reecrit l'angle des
    // roues avant chaque image d'apres SON MaxAngle) regle ; anti-calage (frein a main, ou freinage sous 2 000 tr/min : debraye).
    // Evo III Gr. A (extension privee, JD 20/10) : meme base (Rivett rallye : essieux, pneus cloutes selon le sol, matieres du
    // sol), avec ses propres valeurs -- celles du mod a part de JD (Grupo A engine.ini du mod rFactor) : 370 ch a 5 900 tr/min,
    // 495 Nm a 4 200, rupteur 7 500, inertie 0,15, integrale, boite 2,95 / 2,0 / 1,52 / 1,2 / 0,95 (MA 3,3), pont 4,4,
    // 1 230 kg, 61 % sur l'avant (moteur transversal avant) ; ressorts et amortisseurs de la VAZ a l'echelle de la masse ;
    // braquage 35 (voitures du rallye du pack prive) ; pas de multiplicateur de puissance. Valeurs dans VazCar.Model.Sp, comme
    // celles de la 205 T16 Evo 1 (JD 21/10).
    public static class VazHandling
    {
        static VazCar.Spec Sp(VazCar.Built b) { return b != null && b.M != null ? b.M.Sp : null; }
        const float SteerDeg = 26f;
        const float SpringRate = 35020f, SpringTravel = 0.19f, Damp = 2200f;
        const float PowerMul = 1.43f;
        // cloutes de course (TireDB StuddedRacing) par nom de sol ; « Snow » (neige profonde) : 1,0 pour tous les pneus
        static readonly Dictionary<string, float> GripBySoil = new Dictionary<string, float> { { "TrackIce", 1.65f }, { "RoadSnow", 1.60f }, { "RoadIce", 1.25f }, { "Snow", 1.0f } };
        static readonly float[] Gears = { -3.324f, 0f, 3.65f, 1.97f, 1.37f, 1.00f, 0.82f };

        static readonly string[] BaseDrive = { "maxPower", "maxTorque", "originalMaxPower", "minRPM", "drivetrainInertia", "clutchMaxTorque", "clutchTorqueMultiplier", "engageRPM", "disengageRPM", "revLimiterTime" };
        static readonly string[] BaseWheel = { "a", "b", "c", "mass", "rollingFrictionCoefficient" };
        static readonly string[] Skip = { "powered", "maxSteeringAngle", "suspensionRate", "suspensionTravel", "bumpRate", "reboundRate", "forwardGripFactor", "sidewaysGripFactor" };

        static float mass = 950f;
        static float comFrac = -1f, comHeight = float.NaN;

        static GameObject Source(out string name)
        {
            GameObject c = Game.FindAny("CORRIS");
            if (c != null && c.GetComponent<Drivetrain>() != null) { name = "Rivett (CORRIS)"; return c; }
            name = "RALLYCAR3";
            return VazCar.RallyCar();
        }

        public static void Apply(VazCar.Built b, bool first)
        {
            GameObject car = b.Car;
            if (car == null) return;
            if (first)
            {
                string sn;
                GameObject src = Source(out sn);
                int n = 0;
                if (src != null)
                {
                    Rigidbody srb = src.GetComponent<Rigidbody>();
                    if (srb != null && srb.mass > 600f) mass = srb.mass;
                    if (b.Rb != null && srb != null) { b.Rb.drag = srb.drag; b.Rb.angularDrag = srb.angularDrag; }
                    n += CopyFields(src.GetComponent<Drivetrain>(), b.Dt, BaseDrive);
                    n += CopyFields(src.GetComponent("CarDynamics"), car.GetComponent("CarDynamics"), new[] { "inertiaFactor", "physicMaterials" });
                    Component sa = src.GetComponent("Axles"), ca = car.GetComponent("Axles");
                    if (sa != null && ca != null)
                        foreach (string an in new[] { "frontAxle", "rearAxle" })
                        {
                            FieldInfo af = sa.GetType().GetField(an);
                            object s = af != null ? af.GetValue(sa) : null, d = af != null ? af.GetValue(ca) : null;
                            if (s == null || d == null) continue;
                            foreach (FieldInfo f in s.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
                                if (System.Array.IndexOf(Skip, f.Name) < 0 && (f.FieldType == typeof(float) || f.FieldType == typeof(int) || f.FieldType == typeof(bool) || f.FieldType.IsEnum))
                                { f.SetValue(d, f.GetValue(s)); n++; }
                        }
                    Wheel[] sw = src.GetComponentsInChildren<Wheel>(true);
                    foreach (Wheel w in car.GetComponentsInChildren<Wheel>(true))
                        foreach (Wheel x in sw) if (x.wheelPos == w.wheelPos) { n += CopyFields(x, w, BaseWheel); break; }
                    // centre de gravite de la Rivett : place entre ses essieux et hauteur au-dessus des centres de roues
                    Component scd = src.GetComponent("CarDynamics");
                    FieldInfo cf = scd != null ? scd.GetType().GetField("centerOfMass") : null;
                    Transform com = cf != null ? cf.GetValue(scd) as Transform : null;
                    float zF, zR, yW;
                    if (com != null && Axles(src, out zF, out zR, out yW) && Mathf.Abs(zF - zR) > 1f)
                    {
                        Vector3 lp = src.transform.InverseTransformPoint(com.position);
                        float f = (lp.z - zR) / (zF - zR);
                        if (f > 0.2f && f < 0.8f) { comFrac = f; comHeight = lp.y - yW; }
                    }
                }
                int off = 0;
                foreach (PlayMakerFSM f in car.GetComponentsInChildren<PlayMakerFSM>(true))
                    if (f.FsmName == "SetGrip" || (f.FsmName == "Suspension" && f.transform.name == "CoG")) { f.enabled = false; off++; }
                Log.Info("vaz : " + off + " automate(s) de la SORBET coupes (adherence, suspension)");
                Log.Info("vaz : base de la " + sn + (src == null ? " INTROUVABLE (reglages de la SORBET)" : "") + " : " + n + " valeurs, masse " + mass.ToString("F0") + " kg"
                         + (comFrac > 0f ? ", centre de gravite a " + (comFrac * 100f).ToString("F0") + " % de l'empattement depuis l'arriere, " + comHeight.ToString("F2") + " m au-dessus des roues" : ""));
            }
            Spec(b, first);
        }

        static bool Axles(GameObject c, out float zF, out float zR, out float yW)
        {
            zF = zR = yW = 0f; int nf = 0, nr = 0;
            foreach (Wheel w in c.GetComponentsInChildren<Wheel>(true))
            {
                Vector3 lp = c.transform.InverseTransformPoint(w.transform.position);
                yW += lp.y;
                if (w.wheelPos.ToString().StartsWith("FRONT")) { zF += lp.z; nf++; } else { zR += lp.z; nr++; }
            }
            if (nf == 0 || nr == 0) return false;
            zF /= nf; zR /= nr; yW /= nf + nr;
            return true;
        }

        static float Helper()
        {
            FsmFloat h = FsmVariables.GlobalVariables.FindFsmFloat("OptionsGripHelper");
            return h != null ? h.Value : 0f;
        }

        // Pieces de rallye de la Rivett (le jeu les reecrit : remis toutes les 2 s).
        static void Spec(VazCar.Built b, bool first)
        {
            GameObject car = b.Car;
            int n = 0;
            VazCar.Spec sp = Sp(b);
            bool evo = sp != null;
            float wantMass = evo ? sp.Mass : mass, steer = evo ? sp.Steer : SteerDeg, spring = evo ? sp.Spring : SpringRate, damp = evo ? sp.Damp : Damp;
            Drivetrain.Transmissions tr = evo ? Drivetrain.Transmissions.AWD : Drivetrain.Transmissions.RWD;
            float[] gears = evo ? sp.Gears : Gears;
            if (b.Rb != null && !Mathf.Approximately(b.Rb.mass, wantMass)) { b.Rb.mass = wantMass; n++; }
            Drivetrain dt = b.Dt;
            if (dt != null && evo)
            {
                int pw = SetF(dt, "maxPowerRPM", sp.PowerRpm) + SetF(dt, "maxPower", sp.Power) + SetF(dt, "maxTorque", sp.Torque);
                if (pw > 0 || first) { try { dt.CalcValues(1f, false); } catch (System.Exception e) { Log.Warn("vaz : CalcValues : " + e.Message); } }
                n += pw + SetF(dt, "originalMaxPower", sp.Power) + SetF(dt, "maxTorqueRPM", sp.TorqueRpm) + SetF(dt, "maxRPM", sp.MaxRpm) + SetF(dt, "revLimiter", sp.MaxRpm)
                   + SetF(dt, "engineInertia", sp.Inertia) + SetF(dt, "drivetrainInertia", 1.0f) + SetF(dt, "engineFrictionFactor", 0.2f)
                   + SetF(dt, "disengageRPM", 1400f) + SetF(dt, "engageRPM", 2000f) + SetF(dt, "clutchMaxTorque", sp.Clutch)
                   + SetF(dt, "powerMultiplier", 1f) + SetF(dt, "finalDriveRatio", sp.Final) + SetF(dt, "differentialLockCoefficient", 49f);
            }
            else if (dt != null)
            {
                // (courbe de couple au-dessus du couple maxi : maxPowerAngVel, calcule par CalcValues -> recalcule si le regime change)
                int pw = SetF(dt, "maxPowerRPM", 7500f);
                if (pw > 0 || first) { try { dt.CalcValues(1f, false); } catch (System.Exception e) { Log.Warn("vaz : CalcValues : " + e.Message); } }
                n += pw + SetF(dt, "maxTorqueRPM", 4800f) + SetF(dt, "maxRPM", 8000f) + SetF(dt, "revLimiter", 8000f)
                   + SetF(dt, "engineInertia", 0.072f) + SetF(dt, "powerMultiplier", PowerMul)
                   + SetF(dt, "finalDriveRatio", 4.44f) + SetF(dt, "differentialLockCoefficient", 49f);
            }
            if (dt != null)
            {
                if (dt.gearRatios == null || dt.gearRatios.Length != gears.Length || !Same(dt.gearRatios, gears)) { dt.gearRatios = (float[])gears.Clone(); n++; }
                if (dt.transmission != tr)
                {
                    dt.transmission = tr;
                    if (!first) { try { dt.SetTransmission(tr); } catch (System.Exception e) { Log.Warn("vaz : SetTransmission : " + e.Message); } }
                    n++;
                }
            }
            float helper = Helper();
            Component ax = car.GetComponent("Axles");
            if (ax != null)
                foreach (string an in new[] { "frontAxle", "rearAxle" })
                {
                    FieldInfo af = ax.GetType().GetField(an);
                    object axl = af != null ? af.GetValue(ax) : null;
                    if (axl == null) continue;
                    n += SetF(axl, "suspensionRate", spring) + SetF(axl, "suspensionTravel", SpringTravel) + SetF(axl, "bumpRate", damp) + SetF(axl, "reboundRate", damp);
                    if (evo && sp.AntiRoll > 0f) n += SetF(axl, "antiRollBarRate", sp.AntiRoll);
                    if (an == "frontAxle") n += SetF(axl, "maxSteeringAngle", steer);
                }
            Component cd = car.GetComponent("CarDynamics");
            // (frein a main d'un addon : pose directement sur les roues arriere -- 1.8.4 recopiait tous les essieux dans les roues
            // par CarDynamics.SetWheelsParams : conduite « bizarre », retour de JD 21/10, revenu a la 1.8.3)
            if (evo && sp.Handbrake > 0f)
                foreach (Wheel w in car.GetComponentsInChildren<Wheel>(true))
                    if (w.wheelPos.ToString().StartsWith("REAR")) n += SetF(w, "handbrakeFrictionTorque", sp.Handbrake);
            foreach (Wheel w in car.GetComponentsInChildren<Wheel>(true)) { n += SetF(w, "wheelFrictionTorque", 11f) + SetF(w, "rotationalInertia", 0.753f); }
            if (cd != null) n += SetF(cd, "frontRearBrakeBalance", 0.6f);
            // retour de force coupe : le couple d'auto-alignement (Wheel.CalcAligningForce, qui ne sert qu'a lui) divise par la
            // charge de la roue -- roue avant en l'air sur les bosses de la glace : 0/0 = NaN, 48 fois en 15 min de course (JD 21/10)
            FieldInfo ff = cd != null ? cd.GetType().GetField("enableForceFeedback") : null;
            if (ff != null && ff.FieldType == typeof(bool) && (bool)ff.GetValue(cd)) { ff.SetValue(cd, false); n++; }
            // centre de gravite (CarDynamics recopie chaque pas la position de son objet centerOfMass dans le corps)
            FieldInfo cf = cd != null ? cd.GetType().GetField("centerOfMass") : null;
            Transform com = cf != null ? cf.GetValue(cd) as Transform : null;
            float zF, zR, yW;
            if (com != null && Axles(car, out zF, out zR, out yW))
            {
                Vector3 lp = car.transform.InverseTransformPoint(com.position);
                float wz = zR + (evo ? sp.Com : comFrac > 0f ? comFrac : 0.55f) * (zF - zR), wy = float.IsNaN(comHeight) ? lp.y : yW + comHeight - (evo ? sp.ComDrop : 0f);   // (abaissement : seulement depuis la hauteur mesuree, jamais cumule)
                if (Mathf.Abs(lp.z - wz) > 0.005f || Mathf.Abs(lp.y - wy) > 0.005f) { com.position = car.transform.TransformPoint(new Vector3(lp.x, wy, wz)); n++; }
            }
            foreach (PlayMakerFSM f in car.GetComponents<PlayMakerFSM>())
                if (f.FsmName == "SteerLimit")
                {
                    FsmFloat mx = f.FsmVariables.GetFsmFloat("MaxAngle");
                    if (mx != null && !Mathf.Approximately(mx.Value, steer)) { mx.Value = steer; n++; }
                }
            if (first) Log.Info("vaz : " + (evo ? sp.Name : "Rivett rallye") + " : " + n + " valeurs posees, " + (dt != null ? dt.maxPower.ToString("F0") + " ch x " + dt.powerMultiplier.ToString("F2") + ", " + dt.maxTorque.ToString("F0") + " Nm, " + dt.transmission : "sans Drivetrain")
                                + ", " + wantMass.ToString("F0") + " kg, aide d'adherence " + helper.ToString("F2") + ", braquage " + steer);
            else if (n > 0 && respec++ < 5) Log.Info("vaz : " + n + " reglage(s) remis (le jeu les avait changes)");
        }
        static int respec;

        // Adherence par essieu d'apres le sol sous ses roues (la plus faible des deux ; sol inconnu ou en l'air : la valeur d'avant).
        static void Grip(VazCar.Built b)
        {
            Component ax = b.Car.GetComponent("Axles");
            if (ax == null) return;
            float helper = Helper();
            float[] g = { -1f, -1f };
            foreach (Wheel w in b.Car.GetComponentsInChildren<Wheel>(true))
            {
                float v;
                if (w.physicMaterial == null || !GripBySoil.TryGetValue(w.physicMaterial.name.Replace(" (Instance)", ""), out v)) continue;
                int k = w.wheelPos.ToString().StartsWith("FRONT") ? 0 : 1;
                g[k] = g[k] < 0f ? v : Mathf.Min(g[k], v);
            }
            for (int k = 0; k < 2; k++)
            {
                if (g[k] < 0f) g[k] = b.LastGrip[k] > 0f ? b.LastGrip[k] : GripBySoil["RoadSnow"];
                b.LastGrip[k] = g[k];
                FieldInfo af = ax.GetType().GetField(k == 0 ? "frontAxle" : "rearAxle");
                object axl = af != null ? af.GetValue(ax) : null;
                if (axl == null) continue;
                float grip = Mathf.Clamp(g[k] + helper, 0.1f, 1.7f);
                SetF(axl, "forwardGripFactor", grip); SetF(axl, "sidewaysGripFactor", grip);
            }
        }

        static bool Same(float[] a, float[] b) { for (int i = 0; i < a.Length; i++) if (!Mathf.Approximately(a[i], b[i])) return false; return true; }

        // ([Test] VazRalenti, VazRalentiGaz, VazRalentiGain : essais)
        static float IdleRpm { get { if (idleRpm < 0f) { idleRpm = Config.GetFloat("Test", "VazRalenti", 900f); idleBase = Config.GetFloat("Test", "VazRalentiGaz", 0.104f); idleGain = Config.GetFloat("Test", "VazRalentiGain", 0.00005f); } return idleRpm; } }
        static float IdleBase { get { float r = IdleRpm; return idleBase; } }
        static float IdleGain { get { float r = IdleRpm; return idleGain; } }
        static float idleRpm = -1f, idleBase, idleGain;
        static MethodInfo disengage;
        static FieldInfo hbField, brField, engagingField;

        // Voiture de rallye prete a partir (JD, 18/10 : « les VAZ ne demarraient pas ») : le demarreur repris de la SORBET
        // (STARTERxSorbet) attend, moteur froid (< 10 degres), un moteur amorce -- Radiator/Priming : essence de la chambre a
        // 20 +- 0,69 sans starter, sinon il tourne dans le vide ; noyee (> 20,7), il faut que l'essence s'evapore a 0,03/s,
        // des minutes -- et abandonne si la batterie (Electricity/Power Charge) passe sous 80, qu'il vide a chaque image
        // (vite a 144 images/s). La VAZ copiait l'etat de la SORBET du monde (-12 degres chez JD). Ici : chambre a 20,
        // amorcee, jamais noyee ; batterie remise a 125 sous 110 ; reservoir remis a 40 sous 15.
        static void ReadyToStart(VazCar.Built b, float now)
        {
            Transform ct = b.Car.transform;
            if (b.Priming == null) { Transform r = ct.Find("Simulation/CarTempSorbet/Radiator"); b.Priming = r != null ? Game.FsmOn(r.gameObject, "Priming") : null; }
            if (b.Power == null) { Transform e = ct.Find("Simulation/Electricity"); b.Power = e != null ? Game.FsmOn(e.gameObject, "Power") : null; }
            if (b.Tank == null) { Transform t = ct.Find("Simulation/FuelTankSorbett"); b.Tank = t != null ? Game.FsmOn(t.gameObject, "Data") : null; }
            string what = "";
            if (b.Priming != null)
            {
                FsmFloat ch = b.Priming.FsmVariables.FindFsmFloat("FuelChamber");
                FsmBool ok = b.Priming.FsmVariables.FindFsmBool("PrimingOK");
                if (ch != null && Mathf.Abs(ch.Value - 20f) > 0.3f) { what += " chambre " + ch.Value.ToString("F1") + "->20"; ch.Value = 20f; }
                if (ok != null && !ok.Value) { ok.Value = true; what += " amorcee"; }
                if (b.Priming.ActiveStateName == "Flooded") { Game.SetState(b.Priming, "State 1"); what += " plus noyee"; }
            }
            FsmFloat charge = b.Power != null ? b.Power.FsmVariables.FindFsmFloat("Charge") : null;
            if (charge != null && charge.Value < 110f) { what += " batterie " + charge.Value.ToString("F0") + "->125"; charge.Value = 125f; }
            FsmFloat fuel = b.Tank != null ? b.Tank.FsmVariables.FindFsmFloat("FuelLevel") : null;
            if (fuel != null && fuel.Value < 15f) { what += " reservoir " + fuel.Value.ToString("F1") + "->40"; fuel.Value = 40f; }
            if (what.Length > 0 && now >= b.NextStartLog) { b.NextStartLog = now + 30f; Log.Info("vaz : " + b.Car.name + " prete a partir :" + what); }
        }

        public static void Idle(VazCar.Built b)
        {
            Drivetrain dt = b.Dt;
            if (dt == null || dt.rpm <= 300f) return;
            dt.idlethrottle = Mathf.Clamp(IdleBase + IdleGain * (IdleRpm - dt.rpm), IdleBase - 0.04f, IdleBase + 0.05f);
            // Moteur simule ici (pas une copie) : jamais sous le gaz de ralenti. A la prise du volant d'une VAZ qui tournait chez
            // un autre, le gaz restait a 0 quelques images (commandes pas encore la) ; avec l'inertie du moteur de la Rivett (0,4,
            // SORBET 2,75) il passait de 1 065 a 213 tr/min en deux images et calait (essai vazcle CleReprise, 18/10).
            if (dt.enabled && b.Rb != null && !b.Rb.isKinematic && dt.throttle < dt.idlethrottle) dt.throttle = dt.idlethrottle;
            if (Config.GetInt("Test", "VazRalentiLog", 0) != 0 && Time.frameCount % 20 == 0)
                Log.Info("vaz : ralenti " + b.Car.name + " regime " + dt.rpm.ToString("F0") + " gaz " + dt.throttle.ToString("F3") + " ralenti " + dt.idlethrottle.ToString("F3") + (dt.enabled ? "" : " coupe") + " base " + IdleBase + " vise " + IdleRpm);
        }

        public static void Tick(VazCar.Built b, float now)
        {
            Drivetrain dt = b.Dt;
            if (dt == null) return;
            if (now >= b.NextSpec) { b.NextSpec = now + 2f; Spec(b, false); }
            if (now >= b.NextStart) { b.NextStart = now + 0.5f; if (Config.GetInt("Test", "VazPrete", 1) != 0) ReadyToStart(b, now); }   // ([Test] VazPrete=0 : comme la 0.99.2)
            dt.engineOrientation = b.Car.transform.forward;
            // Ralenti (JD, 18/10 : « elle cale ») : le gaz de ralenti de la SORBET (0,1, pose par ses automates) sur le moteur de la
            // Rivett rallye tenait 560-640 tr/min, sous son regime mini (650) : au moindre creux le moteur calait -- en prenant le
            // volant d'une VAZ qui tournait, en passant a cote (la main du moteur change), ou tout seul. Ce moteur est tres
            // sensible (0,16 : il monte seul a 6 500 tr/min) : petit regulateur vers IdleRpm (900) autour du gaz de la SORBET,
            // pose dans LateUpdate (Idle) : apres les automates du jeu, qui remettent 0,104 a chaque image (selon l'ordre des
            // scripts, chez l'invite d'essai le leur passait apres le notre et le regulateur ne servait a rien).
            float pm = Sp(b) != null ? 1f : PowerMul;
            if (!Mathf.Approximately(dt.powerMultiplier, pm)) dt.powerMultiplier = pm;
            if (now >= b.NextGrip) { b.NextGrip = now + 0.25f; Grip(b); }
            if (b.Axis == null || dt.rpm < 300f) return;
            if (hbField == null) { hbField = b.Axis.GetType().GetField("handbrakeInput"); brField = b.Axis.GetType().GetField("brakeInput"); }
            object hb = hbField != null ? hbField.GetValue(b.Axis) : null, br = brField != null ? brField.GetValue(b.Axis) : null;
            float h = hb is float ? (float)hb : 0f, k = br is float ? (float)br : 0f;
            if (h > 0.05f || (k > 0.2f && dt.rpm < 2000f))
            {
                if (engagingField == null) engagingField = typeof(Drivetrain).GetField("engaging", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (engagingField != null) engagingField.SetValue(dt, false);
                if (disengage == null) disengage = typeof(Drivetrain).GetMethod("DisengageClutch", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (disengage != null) disengage.Invoke(dt, null);
            }
        }

        static int SetF(object o, string f, float v)
        {
            FieldInfo fi = o.GetType().GetField(f);
            if (fi == null || fi.FieldType != typeof(float) || Mathf.Approximately((float)fi.GetValue(o), v)) return 0;
            fi.SetValue(o, v);
            return 1;
        }

        static int CopyFields(Component from, Component to, string[] names)
        {
            if (from == null || to == null) return 0;
            int n = 0;
            foreach (string fn in names)
            {
                FieldInfo sf = from.GetType().GetField(fn), df = to.GetType().GetField(fn);
                if (sf == null || df == null || sf.FieldType != df.FieldType) continue;
                object v = sf.GetValue(from);
                if (v is System.Array) v = ((System.Array)v).Clone();
                df.SetValue(to, v); n++;
            }
            return n;
        }
    }
}
