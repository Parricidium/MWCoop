using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace MWCoop
{
    // Places de l'outil de JD (outil prive des places, idee de Menor, 25/10) : MWCoop\places\<mwc|msc>\<voiture>.txt, ecrits par
    // l'outil (outils-prives/places) d'apres l'export du jeu (SeatExport). Format PLACES 1, repere de la voiture (celui du
    // Rigidbody, x droite, y haut, z avant), cap en degres :
    //   voiture <nom exact du corps>             (la cle de VehicleSync sans « #rang » ; VAZ et addons : modele-<dossier>)
    //   conducteur                               (sa place reste celle du jeu : posture et decalage seulement)
    //   place <n> <passager|couchette|debout|ridelle> <yeux x y z> <cap> <zone dx dz> [maxx=<m>] [sansceinture] [nom=<texte>]
    //   os <n|conducteur> <os> <qx qy qz qw>     (rotation locale ; conducteur : colonne, cou et tete seulement)
    //   corps <n|conducteur> <x y z>             (decalage du corps, repere de la voiture)
    //   pose <n|conducteur> <volant|camion>      (pose de base, sinon celle de la voiture)
    // Un fichier REMPLACE les places calculees de cette voiture (Seats.FileSeats) ; sans fichier, rien ne change. Un avatar assis a
    // une place du fichier (ou au volant, si le fichier a des lignes conducteur) : pose de base, os du fichier, pas d'ajustement
    // au siege (SeatFit, reglages par voiture), les yeux a la place, puis le decalage (Avatar). Les bras qui tiennent quelque chose
    // (volant, levier, objet en main) ne sont jamais pris du fichier. Les fichiers sont relus quand ils changent (toutes les 2 s).
    public static class SeatFiles
    {
        public class Place
        {
            public int N = -1;                       // (-1 : conducteur)
            public string Kind = "passager", Name = "", Pose;
            public Vector3 Eyes, Body;
            public float Yaw, ZoneX = 0.38f, ZoneZ = 0.5f, MaxX = 9f;
            public bool NoBelt, Lines;               // (Lines : le fichier dit quelque chose de cette place)
            public readonly Dictionary<string, Quaternion> Bones = new Dictionary<string, Quaternion>();
            public bool ArmRight, ArmLeft;           // (os d'un bras dans le fichier)

            // Os du fichier sur l'avatar (apres sa pose de base, avant les prises et le penche). Conducteur : colonne, cou, tete.
            public void Apply(Func<string, Transform> bone)
            {
                foreach (KeyValuePair<string, Quaternion> kv in Bones)
                {
                    if (N < 0 && !DriverBone(kv.Key)) continue;
                    Transform b = bone(kv.Key);
                    if (b != null) b.localRotation = kv.Value;
                    else if (missing.Add(kv.Key)) Log.Warn("places : os " + kv.Key + " absent de l'avatar (ignore)");
                }
            }
        }
        public class CarFile { public string Car, File, Hash; public Place Driver = new Place(); public readonly List<Place> Places = new List<Place>(); }

        static readonly Dictionary<string, CarFile> files = new Dictionary<string, CarFile>();
        static readonly HashSet<string> missing = new HashSet<string>();
        static string stamp;
        static float nextCheck;
        public static int Generation;   // (change a chaque relecture : Seats refait ses places)

        public static bool DriverBone(string n) { string l = n.ToLowerInvariant(); return l.Contains("spine") || l.Contains("neck") || l.Contains("head"); }
        static string Dir
        {
            get
            {
                string root = Environment.GetEnvironmentVariable("MWCOOP_DIR") ?? "MWCoop";
                return Path.Combine(Path.Combine(root, "places"), Game.Msc ? "msc" : "mwc");
            }
        }
        static string Key(string car)
        {
            if (car == null) return null;
            int h = car.IndexOf('#');
            if (h >= 0) car = car.Substring(0, h);
            return car.StartsWith(Vaz.Prefix) ? Vaz.SeatKey(car) ?? car : car;   // (VAZ et addons : par modele, « modele-evo3 »...)
        }

        public static CarFile For(string car)
        {
            Check();
            CarFile f;
            return car != null && files.TryGetValue(Key(car), out f) ? f : null;
        }
        // Place d'un avatar : rang (-1 : conducteur) ; null sans fichier, ou conducteur sans ligne a lui.
        public static Place For(string car, int index)
        {
            CarFile f = For(car);
            if (f == null) return null;
            if (index < 0) return f.Driver.Lines ? f.Driver : null;
            foreach (Place p in f.Places) if (p.N == index) return p;
            return null;
        }
        public static bool NoBelt(string car, int index) { Place p = index >= 0 ? For(car, index) : null; return p != null && p.NoBelt; }
        // Cle de pose pour Avatar.DriverPose (pose <n> camion / volant) : la voiture sinon.
        public static string PoseKey(Place p, string car) { return p == null || p.Pose == null ? car : p.Pose == "camion" ? "GIFU" : ""; }

        // Relecture si le dossier a change (date et taille de chaque fichier).
        static void Check()
        {
            float now = Time.realtimeSinceStartup;
            if (now < nextCheck) return;
            nextCheck = now + 2f;
            string dir = Dir, s = "";
            string[] list = Directory.Exists(dir) ? Directory.GetFiles(dir, "*.txt") : new string[0];
            Array.Sort(list, StringComparer.OrdinalIgnoreCase);
            foreach (string p in list) { var fi = new FileInfo(p); s += fi.Name + fi.Length + fi.LastWriteTimeUtc.Ticks + ";"; }
            if (s == stamp) return;
            stamp = s;
            files.Clear();
            foreach (string p in list)
            {
                try
                {
                    CarFile f = Read(p);
                    if (f == null || f.Car == null) { Log.Warn("places : " + Path.GetFileName(p) + " illisible (pas de PLACES 1 / voiture)"); continue; }
                    files[f.Car] = f;
                    Log.Info("places : " + f.Car + " lues dans " + Path.GetFileName(p) + " (" + f.Places.Count + " places" + (f.Driver.Lines ? " + conducteur" : "") + ", empreinte " + f.Hash + ")");
                }
                catch (Exception e) { Log.Warn("places : " + Path.GetFileName(p) + " : " + e.Message); }
            }
            Generation++;
        }

        static CarFile Read(string path)
        {
            string text = File.ReadAllText(path);
            var f = new CarFile { File = path, Hash = (text.GetHashCode() & 0xFFFFFF).ToString("x6") };
            bool ok = false;
            var ci = CultureInfo.InvariantCulture;
            Func<string, float> F = x => float.Parse(x, NumberStyles.Float, ci);
            foreach (string raw in text.Split('\n'))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line[0] == '#') continue;
                string[] w = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                switch (w[0])
                {
                    case "PLACES": ok = true; break;
                    case "voiture": f.Car = line.Substring(8).Trim(); break;
                    case "conducteur": f.Driver.Lines = true; break;
                    case "place":
                        if (w.Length < 9) break;
                        var p = new Place { N = int.Parse(w[1], ci), Kind = w[2], Eyes = new Vector3(F(w[3]), F(w[4]), F(w[5])), Yaw = F(w[6]), ZoneX = F(w[7]), ZoneZ = F(w[8]), Lines = true };
                        for (int i = 9; i < w.Length; i++)
                        {
                            if (w[i].StartsWith("maxx=")) p.MaxX = F(w[i].Substring(5));
                            else if (w[i] == "sansceinture") p.NoBelt = true;
                            else if (w[i].StartsWith("nom=")) { p.Name = line.Substring(line.IndexOf("nom=") + 4); break; }
                        }
                        f.Places.RemoveAll(x => x.N == p.N);
                        f.Places.Add(p);
                        break;
                    case "os":
                    case "corps":
                    case "pose":
                        {
                            Place t = w.Length > 1 ? (w[1] == "conducteur" ? f.Driver : f.Places.Find(x => x.N.ToString(ci) == w[1])) : null;
                            if (t == null) break;
                            t.Lines = true;
                            if (w[0] == "os" && w.Length >= 7)
                            {
                                t.Bones[w[2]] = new Quaternion(F(w[3]), F(w[4]), F(w[5]), F(w[6]));
                                string l = w[2].ToLowerInvariant();
                                if (l.Contains("collar") || l.Contains("shoulder") || l.Contains("arm") || l.Contains("hand") || l.Contains("finger"))
                                { if (l.Contains("right")) t.ArmRight = true; else if (l.Contains("left")) t.ArmLeft = true; }
                            }
                            else if (w[0] == "corps" && w.Length >= 5) t.Body = new Vector3(F(w[2]), F(w[3]), F(w[4]));
                            else if (w[0] == "pose" && w.Length >= 3) t.Pose = w[2] == "camion" ? "camion" : "volant";
                            break;
                        }
                }
            }
            return ok ? f : null;
        }

        public static void OnLevelLoaded() { stamp = null; nextCheck = 0f; missing.Clear(); }
    }

    // Seats : places du fichier a la place de celles calculees (appele a la fin de Scan).
    public static partial class Seats
    {
        static void FileSeats()
        {
            var done = new HashSet<string>();
            for (int ci = 0; ci < VehicleSync.LocalCount; ci++)
            {
                Rigidbody rb = VehicleSync.LocalBody(ci);
                string n = VehicleSync.LocalKey(ci);
                SeatFiles.CarFile f = rb != null ? SeatFiles.For(n) : null;
                if (f == null || !done.Add(n)) continue;
                // (SATSUMA : une place du fichier seulement si la place calculee existe -- siege monte)
                var had = new HashSet<int>();
                foreach (Seat s in seats) if (s.Car == n) had.Add(s.Index);
                bool satsuma = n.StartsWith("SATSUMA");
                seats.RemoveAll(s => s.Car == n);
                foreach (SeatFiles.Place p in f.Places)
                {
                    if (satsuma && !had.Contains(p.N) && p.Kind != "ridelle" && p.Kind != "couchette") continue;
                    seats.Add(new Seat { Car = n, CarT = rb.transform, Index = p.N, Head = p.Eyes, Yaw = p.Yaw, ZoneX = p.ZoneX, ZoneZ = p.ZoneZ, MaxX = p.MaxX,
                                         Bed = p.Kind == "ridelle", Stand = p.Kind == "debout" });
                }
            }
            fileGen = SeatFiles.Generation;
        }
        static int fileGen;
        // (fichier change en jeu : places refaites tout de suite)
        static bool FilesChanged { get { SeatFiles.For((string)null); return fileGen != SeatFiles.Generation; } }
    }
}
