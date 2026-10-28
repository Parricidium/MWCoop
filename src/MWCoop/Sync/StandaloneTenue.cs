using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace MWCoop
{
    // Tenue de route de la voiture standalone (spec du 28/10, §4) : MWCoop\addons\<dossier>\tenue.ini, lisible et commente, unites SI.
    // Ecrit avec les valeurs de la R5 Maxi copie s'il n'existe pas (meme voiture au depart : comparaison juste), relu a la construction,
    // a chaque enregistrement du fichier (verifie chaque seconde) et a la touche [Test] StandaloneTouche (F9). Chaque valeur est
    // bornee : hors borne ou illisible -> ramenee dans la borne (ou valeur de depart) et notee au journal ; jamais de NaN. Les champs
    // fins des pneus de Wheel (pression, raideurs, amortissement) ne sont jamais ecrits ici.
    // Les essieux ne passent aux roues que par CarDynamics.SetWheelsParams (le jeu ne l'appelle qu'au demarrage de la voiture) :
    // appele apres chaque lecture ; l'adherence selon le sol, elle, est posee directement sur les roues.
    public class StandaloneTenue
    {
        public float Masse = 905f, CgHauteur = 0.36f, CgAvant = 0.49f, Trainee = 0.02f, TraineeLacet = 0.6f;
        public float Puissance = 355f, PuissanceRegime = 6750f, Couple = 398f, CoupleRegime = 5500f, Rupteur = 7000f, InertieMoteur = 0.15f;
        public float[] Rapports = { -3.3f, 0f, 2.95f, 2.0f, 1.52f, 1.2f, 0.95f };
        public float Pont = 4.4f, Blocage = 49f, Embrayage = 420f;
        public string Transmission = "propulsion";
        public float FreinAvant = 500f, FreinArriere = 250f, FreinRepartition = 0.6f, FreinAMain = 6000f;
        public float BraquageMax = 33f, BraquageVitesse = 3.3f, BraquageMin = 2f;
        public class Essieu { public float Ressort, Course, Compression, Detente, AntiRoulis, Carrossage, Adherence, AdherenceFreinAMain; }
        public Essieu Av = new Essieu { Ressort = 44573f, Course = 0.15f, Compression = 7132f, Detente = 7132f, AntiRoulis = 24000f, Carrossage = -1.4f, Adherence = 1f, AdherenceFreinAMain = 1f };
        public Essieu Ar = new Essieu { Ressort = 44573f, Course = 0.15f, Compression = 7132f, Detente = 7132f, AntiRoulis = 12000f, Carrossage = 0f, Adherence = 1.15f, AdherenceFreinAMain = 0.45f };
        public Dictionary<string, float> Sols = new Dictionary<string, float> { { "Track", 0.95f }, { "Offroad", 1.25f }, { "Grass", 1.15f }, { "Swamp", 1.0f } };
        public float SolInconnu = 1.0f;

        public string File;
        public DateTime Stamp;
        int fixes;

        // ---------------------------------------------------------------- lecture
        public static StandaloneTenue Load(VazCar.Model m)
        {
            var t = new StandaloneTenue { File = Path.Combine(m.Dir, "tenue.ini") };
            try
            {
                if (!System.IO.File.Exists(t.File)) { System.IO.File.WriteAllText(t.File, t.Text(), new UTF8Encoding(true)); Log.Info("standalone : " + t.File + " cree (valeurs de la " + m.Name + " copie)"); }
                t.Stamp = System.IO.File.GetLastWriteTimeUtc(t.File);
                t.Parse(System.IO.File.ReadAllLines(t.File, Encoding.UTF8));
            }
            catch (Exception e) { Log.Warn("standalone : tenue.ini : " + e.Message + " -- valeurs de depart"); }
            return t;
        }

        public bool Changed()
        {
            try { return System.IO.File.Exists(File) && System.IO.File.GetLastWriteTimeUtc(File) != Stamp; } catch { return false; }
        }

        void Parse(string[] lines)
        {
            var v = new Dictionary<string, string>();
            string sec = "";
            foreach (string l0 in lines)
            {
                string l = l0; int c = l.IndexOf(';'); if (c >= 0) l = l.Substring(0, c);
                l = l.Trim();
                if (l.Length == 0) continue;
                if (l.StartsWith("[") && l.EndsWith("]")) { sec = l.Substring(1, l.Length - 2).Trim(); continue; }
                int eq = l.IndexOf('=');
                if (eq > 0) v[sec + "." + l.Substring(0, eq).Trim()] = l.Substring(eq + 1).Trim();
            }
            fixes = 0;
            Masse = F(v, "Caisse.Masse", Masse, 300f, 3000f);
            CgHauteur = F(v, "Caisse.CentreGraviteHauteur", CgHauteur, 0f, 1.2f);
            CgAvant = F(v, "Caisse.CentreGraviteAvant", CgAvant, 0.2f, 0.8f);
            Trainee = F(v, "Caisse.Trainee", Trainee, 0f, 1f);
            TraineeLacet = F(v, "Caisse.TraineeLacet", TraineeLacet, 0f, 5f);
            Puissance = F(v, "Moteur.Puissance", Puissance, 20f, 1500f);
            PuissanceRegime = F(v, "Moteur.PuissanceRegime", PuissanceRegime, 1000f, 12000f);
            Couple = F(v, "Moteur.Couple", Couple, 20f, 2000f);
            CoupleRegime = F(v, "Moteur.CoupleRegime", CoupleRegime, 1000f, 12000f);
            Rupteur = F(v, "Moteur.Rupteur", Rupteur, 2000f, 12000f);
            InertieMoteur = F(v, "Moteur.Inertie", InertieMoteur, 0.02f, 2f);
            Rapports = Gears(v, "Boite.Rapports", Rapports);
            Pont = F(v, "Boite.Pont", Pont, 1f, 10f);
            Blocage = F(v, "Boite.Blocage", Blocage, 0f, 100f);
            Embrayage = F(v, "Boite.Embrayage", Embrayage, 50f, 3000f);
            string tr; if (v.TryGetValue("Boite.Transmission", out tr)) { tr = tr.ToLowerInvariant(); if (tr == "propulsion" || tr == "traction" || tr == "integrale") Transmission = tr; else Fix("Boite.Transmission", tr, Transmission); }
            FreinAvant = F(v, "Freins.Avant", FreinAvant, 0f, 5000f);
            FreinArriere = F(v, "Freins.Arriere", FreinArriere, 0f, 5000f);
            FreinRepartition = F(v, "Freins.Repartition", FreinRepartition, 0f, 1f);
            FreinAMain = F(v, "Freins.FreinAMain", FreinAMain, 0f, 20000f);
            BraquageMax = F(v, "Direction.BraquageMax", BraquageMax, 5f, 60f);
            BraquageVitesse = F(v, "Direction.BraquageVitesse", BraquageVitesse, 0.5f, 20f);
            BraquageMin = F(v, "Direction.BraquageMin", BraquageMin, 0f, BraquageMax);
            foreach (KeyValuePair<string, Essieu> kv in new[] { new KeyValuePair<string, Essieu>("EssieuAvant", Av), new KeyValuePair<string, Essieu>("EssieuArriere", Ar) })
            {
                Essieu e = kv.Value; string s = kv.Key + ".";
                e.Ressort = F(v, s + "Ressort", e.Ressort, 5000f, 200000f);
                e.Course = F(v, s + "Course", e.Course, 0.05f, 0.4f);
                e.Compression = F(v, s + "Compression", e.Compression, 100f, 30000f);
                e.Detente = F(v, s + "Detente", e.Detente, 100f, 30000f);
                e.AntiRoulis = F(v, s + "AntiRoulis", e.AntiRoulis, 0f, 100000f);
                e.Carrossage = F(v, s + "Carrossage", e.Carrossage, -10f, 10f);
                e.Adherence = F(v, s + "Adherence", e.Adherence, 0.3f, 2f);
                e.AdherenceFreinAMain = F(v, s + "AdherenceFreinAMain", e.AdherenceFreinAMain, 0.1f, 1.5f);
            }
            foreach (string k in new List<string>(v.Keys))
            {
                if (!k.StartsWith("Sols.")) continue;
                string n = k.Substring(5);
                if (n == "Autres") SolInconnu = F(v, k, SolInconnu, 0.1f, 2f);
                else Sols[n] = F(v, k, Sols.ContainsKey(n) ? Sols[n] : 1f, 0.1f, 2f);
            }
            Log.Info("standalone : tenue.ini lue (" + v.Count + " valeurs" + (fixes > 0 ? ", " + fixes + " ramenee(s) dans leurs bornes" : "") + ")");
        }

        float F(Dictionary<string, string> v, string key, float def, float min, float max)
        {
            string s;
            if (!v.TryGetValue(key, out s)) return def;
            float f;
            if (!float.TryParse(s.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out f) || float.IsNaN(f) || float.IsInfinity(f)) { Fix(key, s, def.ToString(CultureInfo.InvariantCulture)); return def; }
            if (f < min) { Fix(key, s, min.ToString(CultureInfo.InvariantCulture)); return min; }
            if (f > max) { Fix(key, s, max.ToString(CultureInfo.InvariantCulture)); return max; }
            return f;
        }

        float[] Gears(Dictionary<string, string> v, string key, float[] def)
        {
            string s;
            if (!v.TryGetValue(key, out s)) return def;
            var l = new List<float>();
            foreach (string p in s.Split(' ', ';', '/'))
            {
                if (p.Trim().Length == 0) continue;
                float f;
                if (!float.TryParse(p.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out f) || float.IsNaN(f) || Mathf.Abs(f) > 10f) { Fix(key, s, "valeurs de depart"); return def; }
                l.Add(f);
            }
            // (point mort obligatoire, une marche arriere avant lui et au moins un rapport avant apres : UnityCar le veut ainsi)
            int n = l.IndexOf(0f);
            if (n < 1 || n >= l.Count - 1 || l[0] >= 0f) { Fix(key, s, "valeurs de depart (il faut : marche arriere negative, 0, puis les rapports)"); return def; }
            return l.ToArray();
        }

        void Fix(string key, string was, string now) { fixes++; Log.Warn("standalone : tenue.ini " + key + " = " + was + " hors borne -> " + now); }

        // ---------------------------------------------------------------- fichier de depart
        static string N(float f) { return f.ToString("0.###", CultureInfo.InvariantCulture); }
        string Text()
        {
            var sb = new StringBuilder();
            sb.Append("; Tenue de route de la voiture standalone (pilote MWCoop, [Test] AddonStandalone=1). Unites SI.\r\n");
            sb.Append("; Relu a chaque enregistrement de ce fichier (et touche F9 en jeu). Une valeur hors borne est ramenee dans la borne (journal).\r\n");
            sb.Append("; Valeurs de depart : celles de la voiture copie actuelle. Supprimer ce fichier le recree avec elles.\r\n\r\n");
            sb.Append("[Caisse]\r\n");
            sb.Append("Masse=").Append(N(Masse)).Append("                    ; kg (300 - 3000)\r\n");
            sb.Append("CentreGraviteHauteur=").Append(N(CgHauteur)).Append("    ; m au-dessus des moyeux (0 - 1,2) : plus bas = moins de roulis\r\n");
            sb.Append("CentreGraviteAvant=").Append(N(CgAvant)).Append("      ; part du poids sur l'avant (0,2 - 0,8)\r\n");
            sb.Append("Trainee=").Append(N(Trainee)).Append("                 ; trainee de la caisse (0 - 1)\r\n");
            sb.Append("TraineeLacet=").Append(N(TraineeLacet)).Append("             ; amortissement des rotations (0 - 5) : plus haut = pivote moins vite\r\n\r\n");
            sb.Append("[Moteur]\r\n");
            sb.Append("Puissance=").Append(N(Puissance)).Append("                ; ch (20 - 1500)\r\n");
            sb.Append("PuissanceRegime=").Append(N(PuissanceRegime)).Append("         ; tr/min de la puissance maxi\r\n");
            sb.Append("Couple=").Append(N(Couple)).Append("                   ; N.m (20 - 2000)\r\n");
            sb.Append("CoupleRegime=").Append(N(CoupleRegime)).Append("            ; tr/min du couple maxi\r\n");
            sb.Append("Rupteur=").Append(N(Rupteur)).Append("                 ; tr/min (2000 - 12000)\r\n");
            sb.Append("Inertie=").Append(N(InertieMoteur)).Append("                 ; kg.m2 (0,02 - 2) : plus haut = monte en regime moins vite\r\n\r\n");
            sb.Append("[Boite]\r\n");
            var g = new StringBuilder(); foreach (float r in Rapports) g.Append(g.Length > 0 ? " " : "").Append(N(r));
            sb.Append("Rapports=").Append(g).Append("   ; marche arriere (negative), 0 (point mort), puis les rapports\r\n");
            sb.Append("Pont=").Append(N(Pont)).Append("                      ; rapport de pont (1 - 10)\r\n");
            sb.Append("Transmission=").Append(Transmission).Append("         ; propulsion, traction ou integrale\r\n");
            sb.Append("Blocage=").Append(N(Blocage)).Append("                    ; blocage du differentiel (0 - 100)\r\n");
            sb.Append("Embrayage=").Append(N(Embrayage)).Append("                 ; couple maxi de l'embrayage, N.m (50 - 3000)\r\n\r\n");
            sb.Append("[Freins]\r\n");
            sb.Append("Avant=").Append(N(FreinAvant)).Append("                     ; couple de freinage avant, N.m (0 - 5000)\r\n");
            sb.Append("Arriere=").Append(N(FreinArriere)).Append("                   ; couple de freinage arriere, N.m (0 - 5000)\r\n");
            sb.Append("Repartition=").Append(N(FreinRepartition)).Append("               ; part du freinage sur l'avant (0 - 1)\r\n");
            sb.Append("FreinAMain=").Append(N(FreinAMain)).Append("               ; couple du frein a main sur l'arriere, N.m (0 - 20000)\r\n\r\n");
            sb.Append("[Direction]\r\n");
            sb.Append("BraquageMax=").Append(N(BraquageMax)).Append("                ; degres a l'arret (5 - 60)\r\n");
            sb.Append("BraquageVitesse=").Append(N(BraquageVitesse)).Append("           ; braquage = BraquageMax x BraquageVitesse / vitesse en m/s (0,5 - 20) : plus bas = moins de braquage a haute vitesse\r\n");
            sb.Append("BraquageMin=").Append(N(BraquageMin)).Append("                  ; degres mini a haute vitesse\r\n\r\n");
            foreach (KeyValuePair<string, Essieu> kv in new[] { new KeyValuePair<string, Essieu>("EssieuAvant", Av), new KeyValuePair<string, Essieu>("EssieuArriere", Ar) })
            {
                Essieu e = kv.Value;
                sb.Append("[").Append(kv.Key).Append("]\r\n");
                sb.Append("Ressort=").Append(N(e.Ressort)).Append("              ; N/m (5000 - 200000)\r\n");
                sb.Append("Course=").Append(N(e.Course)).Append("                ; m de debattement (0,05 - 0,4)\r\n");
                sb.Append("Compression=").Append(N(e.Compression)).Append("           ; amortisseur en compression (100 - 30000)\r\n");
                sb.Append("Detente=").Append(N(e.Detente)).Append("               ; amortisseur en detente (100 - 30000)\r\n");
                sb.Append("AntiRoulis=").Append(N(e.AntiRoulis)).Append("           ; barre antiroulis, N/m (0 - 100000)\r\n");
                sb.Append("Carrossage=").Append(N(e.Carrossage)).Append("            ; degres (-10 - 10)\r\n");
                sb.Append("Adherence=").Append(N(e.Adherence)).Append("               ; multiplie l'adherence du sol (0,3 - 2)\r\n");
                sb.Append("AdherenceFreinAMain=").Append(N(e.AdherenceFreinAMain)).Append("     ; adherence laterale pendant le frein a main, x (0,1 - 1,5)\r\n\r\n");
            }
            sb.Append("[Sols]\r\n; adherence par matiere du sol (nom de la matiere du jeu)\r\n");
            foreach (KeyValuePair<string, float> kv in Sols) sb.Append(kv.Key).Append("=").Append(N(kv.Value)).Append("\r\n");
            sb.Append("Autres=").Append(N(SolInconnu)).Append("        ; sol absent de la liste\r\n");
            return sb.ToString();
        }

        // ---------------------------------------------------------------- application
        // A la construction (avant le Start d'UnityCar : first) et a chaque relecture. cog : objet centre de gravite de la voiture.
        public void Apply(VazCar.Built b, Transform cog, bool first)
        {
            Rigidbody rb = b.Rb; Drivetrain dt = b.Dt;
            CarDynamics cd = b.Car.GetComponent<CarDynamics>(); Axles ax = b.Car.GetComponent<Axles>();
            if (rb == null || dt == null || cd == null || ax == null) return;
            rb.mass = Masse; rb.drag = Trainee; rb.angularDrag = TraineeLacet;
            dt.maxPower = dt.originalMaxPower = Puissance; dt.maxPowerRPM = PuissanceRegime; dt.maxTorque = Couple; dt.maxTorqueRPM = CoupleRegime;
            dt.maxRPM = Rupteur; dt.engineInertia = InertieMoteur; dt.finalDriveRatio = Pont; dt.differentialLockCoefficient = Blocage; dt.clutchMaxTorque = Embrayage;
            dt.powerMultiplier = 1f;
            try { dt.CalcValues(1f, false); } catch (Exception e) { Log.Warn("standalone : CalcValues : " + e.Message); }
            if (dt.clutch != null) dt.clutch.maxTorque = Embrayage;
            dt.gearRatios = (float[])Rapports.Clone();
            if (dt.gear >= dt.gearRatios.Length) dt.gear = Array.IndexOf(dt.gearRatios, 0f);
            Drivetrain.Transmissions tr = Transmission == "traction" ? Drivetrain.Transmissions.FWD : Transmission == "integrale" ? Drivetrain.Transmissions.AWD : Drivetrain.Transmissions.RWD;
            dt.transmission = tr;
            if (!first) { try { dt.SetTransmission(tr); } catch (Exception e) { Log.Warn("standalone : SetTransmission : " + e.Message); } }
            SetAxle(ax.frontAxle, Av); SetAxle(ax.rearAxle, Ar);
            ax.frontAxle.brakeFrictionTorque = FreinAvant; ax.rearAxle.brakeFrictionTorque = FreinArriere;
            ax.frontAxle.handbrakeFrictionTorque = 0f; ax.rearAxle.handbrakeFrictionTorque = FreinAMain;
            ax.frontAxle.maxSteeringAngle = BraquageMax; ax.rearAxle.maxSteeringAngle = 0f;
            cd.frontRearBrakeBalance = FreinRepartition; cd.frontRearHandBrakeBalance = 0f;
            cd.enableForceFeedback = false;   // (couple d'auto-alignement : 0/0 roue en l'air, NaN -- 21/10)
            // centre de gravite : hauteur au-dessus des moyeux, position entre les essieux
            float zF = 0f, zR = 0f, yW = 0f; int nf = 0, nr = 0;
            foreach (Wheel w in ax.allWheels ?? new Wheel[0])
            {
                if (w == null) continue;
                Vector3 lp = w.transform.localPosition; yW += lp.y;
                if (w.wheelPos == WheelPos.FRONT_LEFT || w.wheelPos == WheelPos.FRONT_RIGHT) { zF += lp.z; nf++; } else { zR += lp.z; nr++; }
            }
            if (cog != null && nf > 0 && nr > 0)
            {
                zF /= nf; zR /= nr; yW /= nf + nr;
                cog.localPosition = new Vector3(0f, yW - StandaloneCar.HubDrop + CgHauteur, zR + CgAvant * (zF - zR));
            }
            if (!first)
            {
                cd.SetWheelsParams(); cd.SetBrakes();
                cd.SetCenterOfMass();
            }
        }

        static void SetAxle(Axle a, Essieu e)
        {
            a.suspensionRate = e.Ressort; a.suspensionTravel = e.Course; a.bumpRate = e.Compression; a.reboundRate = e.Detente;
            a.antiRollBarRate = e.AntiRoulis; a.camber = e.Carrossage;
        }

        // Chaque image : braquage maxi selon la vitesse (formule des voitures de MSC, reglee ici) ; toutes les 0,25 s : adherence selon
        // le sol sous chaque essieu (la plus faible des deux roues), arriere reduit pendant le frein a main.
        public void Tick(VazCar.Built b, bool handbraking)
        {
            Rigidbody rb = b.Rb;
            if (rb == null || rb.isKinematic) return;
            float a = Mathf.Clamp(BraquageMax * BraquageVitesse / Mathf.Max(rb.velocity.magnitude, 0.01f), BraquageMin, BraquageMax);
            Axles ax = b.Car.GetComponent<Axles>();
            if (ax == null || ax.allWheels == null) return;
            ax.frontAxle.maxSteeringAngle = a;
            foreach (Wheel w in ax.frontAxle.wheels) if (w != null) w.maxSteeringAngle = a;
        }

        public void Grip(VazCar.Built b, bool handbraking)
        {
            Axles ax = b.Car.GetComponent<Axles>();
            if (ax == null) return;
            for (int k = 0; k < 2; k++)
            {
                Axle axle = k == 0 ? ax.frontAxle : ax.rearAxle;
                Essieu e = k == 0 ? Av : Ar;
                float g = -1f;
                foreach (Wheel w in axle.wheels ?? new Wheel[0])
                {
                    if (w == null || !w.onGroundDown) continue;
                    float v;
                    string n = w.physicMaterial != null ? w.physicMaterial.name.Replace(" (Instance)", "") : "";
                    if (!Sols.TryGetValue(n, out v)) v = SolInconnu;
                    g = g < 0f ? v : Mathf.Min(g, v);
                }
                if (g < 0f) g = b.LastGrip[k] > 0f ? b.LastGrip[k] : SolInconnu;
                b.LastGrip[k] = g;
                float fwd = Mathf.Clamp(g * e.Adherence, 0.1f, 3f), side = Mathf.Clamp(fwd * (handbraking ? e.AdherenceFreinAMain : 1f), 0.05f, 3f);
                axle.forwardGripFactor = fwd; axle.sidewaysGripFactor = side;
                foreach (Wheel w in axle.wheels ?? new Wheel[0]) if (w != null) { w.forwardGripFactor = fwd; w.sidewaysGripFactor = side; }
            }
        }
    }
}
