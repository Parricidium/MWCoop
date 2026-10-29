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
    //   corps <n|conducteur> <x y z> [rx ry rz]  (decalage du corps, repere de la voiture ; rotation en plus : angles d'Euler du jeu, en
    //                                              degres, autour des yeux -- la tete reste a la place, le corps tourne dessous)
    //   prise conducteur <gauche|droite> <x y z> (main sur le volant, repere du volant : elle tourne avec lui ; JD 29/10)
    //   levier conducteur <x y z>                (main au levier de vitesse, repere du levier ; sinon le haut du pommeau)
    //   pose <n|conducteur> <volant|camion>      (pose de base, sinon celle de la voiture)
    // Ceinture d'une place (outil des places, JD 26/10 : « Tout me vas », idee de Menos) -- lue ici, dessinee par Seats :
    //   ceinture <n|conducteur> <sangle|menos|modele|jeu>  (sangle : ruban sur les points ; menos : pieces de son modele aux
    //                                              points ; modele : son modele entier deplace d'un bloc, ligne modele ; jeu : ce que fait
    //                                              le mod sans fichier)
    //   modele <n> <x y z> <rx ry rz>               (ceinture « modele » : decalage du modele de Menos du cote de la place, repere de la
    //                                              voiture, tourne autour du centre de ses pieces -- SORBET, JD 28/10)
    //   ancrage <n|conducteur> <haut|bas> <x y z>
    //   boucle <n|conducteur> <x y z> <rx ry rz>    (aussi le point vise pour s'attacher ; angles d'Euler du jeu, en degres)
    //   languette <n|conducteur> <x y z> <rx ry rz> (ceinture rangee)
    //   rangee <n|conducteur> <x y z> ...           (sangle rangee, de l'ancrage bas a l'ancrage haut, sans eux)
    //   bouclee <n|conducteur> <x y z> ...          (sangle bouclee : ancrage haut, epaule, poitrine, boucle, hanche, ancrage bas)
    //   largeur <n|conducteur> <m>                  (0,045 sans ligne)
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
            public Quaternion BodyRot = Quaternion.identity;
            public Vector3 GripL, GripR, Knob;       // (conducteur : prises sur le volant, main au levier -- Avatar.DriverHands)
            public bool HasGripL, HasGripR, HasKnob;
            public float Yaw, ZoneX = 0.38f, ZoneZ = 0.5f, MaxX = 9f;
            public bool NoBelt, Lines;               // (Lines : le fichier dit quelque chose de cette place)
            public readonly Dictionary<string, Quaternion> Bones = new Dictionary<string, Quaternion>();
            public bool ArmRight, ArmLeft;           // (os d'un bras dans le fichier)
            public Belt Belt;                        // (lignes ceinture ... de cette place ; null sans)

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
        // Ceinture d'une place du fichier, repere de la voiture. Has* : la ligne y est (sinon a Seats de garder la sienne).
        public class Belt
        {
            public string Kind = "sangle";           // (sangle, menos, jeu)
            public Vector3 Top, Bottom, Buckle, Tongue;
            public Quaternion BuckleRot = Quaternion.identity, TongueRot = Quaternion.identity;
            public bool HasTop, HasBottom, HasBuckle, HasTongue;
            public readonly List<Vector3> Stowed = new List<Vector3>(), Fastened = new List<Vector3>();
            public float Width = 0.045f;
            public Vector3 ModelPos; public Quaternion ModelRot = Quaternion.identity; public bool HasModel;   // (ceinture « modele »)
            // Trajets complets (avec les ancrages quand ils y sont) : rangee bas -> haut, bouclee haut -> bas.
            public List<Vector3> StowedPath()
            {
                var l = new List<Vector3>();
                if (HasBottom) l.Add(Bottom);
                l.AddRange(Stowed);
                if (HasTop) l.Add(Top);
                return l;
            }
            public List<Vector3> FastenedPath()
            {
                var l = new List<Vector3>();
                if (HasTop) l.Add(Top);
                l.AddRange(Fastened);
                if (HasBottom) l.Add(Bottom);
                return l;
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
        // Ceinture du fichier pour une place (rang, -1 : conducteur) : null sans fichier, sans lignes ceinture, ou « ceinture n jeu »
        // (Seats garde alors la sienne). Le conducteur n'a pas besoin de lignes os / corps / pose pour sa ceinture.
        public static Belt BeltFor(string car, int index)
        {
            CarFile f = For(car);
            if (f == null) return null;
            Place p = index < 0 ? f.Driver : f.Places.Find(x => x.N == index);
            return p != null && p.Belt != null && p.Belt.Kind != "jeu" && !p.NoBelt ? p.Belt : null;
        }
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
                    Log.Info("places : " + f.Car + " lues dans " + Path.GetFileName(p) + " (" + f.Places.Count + " places" + (f.Driver.Lines ? " + conducteur" : "") + Belts(f) + ", empreinte " + f.Hash + ")");
                }
                catch (Exception e) { Log.Warn("places : " + Path.GetFileName(p) + " : " + e.Message); }
            }
            Generation++;
        }

        static string Belts(CarFile f)
        {
            string s = "";
            if (f.Driver.Belt != null) s += " conducteur " + f.Driver.Belt.Kind;
            foreach (Place p in f.Places) if (p.Belt != null) s += " " + p.N + " " + p.Belt.Kind + " (" + p.Belt.Stowed.Count + "/" + p.Belt.Fastened.Count + " points)";
            return s.Length > 0 ? ", ceintures :" + s : "";
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
                    case "conducteur": break;   // (en-tete ecrit par l'outil : le conducteur ne compte que s'il a des lignes os / corps / pose)
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
                            else if (w[0] == "corps" && w.Length >= 5)
                            {
                                t.Body = new Vector3(F(w[2]), F(w[3]), F(w[4]));
                                if (w.Length >= 8) t.BodyRot = Quaternion.Euler(F(w[5]), F(w[6]), F(w[7]));
                            }
                            else if (w[0] == "pose" && w.Length >= 3) t.Pose = w[2] == "camion" ? "camion" : "volant";
                            break;
                        }
                    case "prise":
                    case "levier":
                        {
                            Place t = w.Length > 1 && w[1] == "conducteur" ? f.Driver : null;   // (conducteur seulement)
                            if (t == null) break;
                            if (w[0] == "prise" && w.Length >= 6)
                            {
                                var v = new Vector3(F(w[3]), F(w[4]), F(w[5]));
                                if (w[2] == "gauche") { t.GripL = v; t.HasGripL = true; } else if (w[2] == "droite") { t.GripR = v; t.HasGripR = true; }
                            }
                            else if (w[0] == "levier" && w.Length >= 5) { t.Knob = new Vector3(F(w[2]), F(w[3]), F(w[4])); t.HasKnob = true; }
                            break;
                        }
                    case "ceinture":
                    case "ancrage":
                    case "boucle":
                    case "languette":
                    case "rangee":
                    case "bouclee":
                    case "largeur":
                    case "modele":
                        {
                            Place t = w.Length > 2 ? (w[1] == "conducteur" ? f.Driver : f.Places.Find(x => x.N.ToString(ci) == w[1])) : null;
                            if (t == null) break;
                            Belt b = t.Belt ?? (t.Belt = new Belt());   // (pas de Lines : une ceinture seule ne change pas la pose)
                            if (w[0] == "ceinture") b.Kind = w[2] == "menos" || w[2] == "jeu" || w[2] == "modele" ? w[2] : "sangle";
                            else if (w[0] == "modele" && w.Length >= 5)
                            {
                                b.ModelPos = new Vector3(F(w[2]), F(w[3]), F(w[4]));
                                b.ModelRot = w.Length >= 8 ? Quaternion.Euler(F(w[5]), F(w[6]), F(w[7])) : Quaternion.identity;
                                b.HasModel = true;
                            }
                            else if (w[0] == "ancrage" && w.Length >= 6)
                            {
                                var v = new Vector3(F(w[3]), F(w[4]), F(w[5]));
                                if (w[2] == "haut") { b.Top = v; b.HasTop = true; } else if (w[2] == "bas") { b.Bottom = v; b.HasBottom = true; }
                            }
                            else if ((w[0] == "boucle" || w[0] == "languette") && w.Length >= 5)
                            {
                                var v = new Vector3(F(w[2]), F(w[3]), F(w[4]));
                                Quaternion q = w.Length >= 8 ? Quaternion.Euler(F(w[5]), F(w[6]), F(w[7])) : Quaternion.identity;
                                if (w[0] == "boucle") { b.Buckle = v; b.BuckleRot = q; b.HasBuckle = true; }
                                else { b.Tongue = v; b.TongueRot = q; b.HasTongue = true; }
                            }
                            else if (w[0] == "rangee" || w[0] == "bouclee")
                            {
                                List<Vector3> l = w[0] == "rangee" ? b.Stowed : b.Fastened;
                                l.Clear();
                                for (int i = 2; i + 2 < w.Length; i += 3) l.Add(new Vector3(F(w[i]), F(w[i + 1]), F(w[i + 2])));
                            }
                            else if (w[0] == "largeur") b.Width = Mathf.Clamp(F(w[2]), 0.01f, 0.15f);
                            break;
                        }
                }
            }
            return ok ? f : null;
        }

        public static void OnLevelLoaded() { stamp = null; nextCheck = 0f; missing.Clear(); levelAt = Time.realtimeSinceStartup; addonAsked = false; }

        // Chaque image, depuis Seats.Update (avant l'animation : les os ont encore la pose finale de l'image d'avant -- lus depuis
        // la pose de l'avatar, ils etaient parfois a moitie calcules) : relecture des fichiers, sonde, addon d'essai.
        static float nextProbe, levelAt; static bool addonAsked;
        public static void Tick()
        {
            Check();
            float now = Time.realtimeSinceStartup;
            if (now >= nextProbe) { nextProbe = now + 2f; Probe(); DumpPoses(); }
            // [Test] PlacesAddon=<dossier> (t16e2...) : l'hote sort cet addon a 15 s (essais des places d'un addon : l'invite s'y assoit
            // avec Autotest=passager, TestVoiture=MWCoop-VAZ-)
            string ad = Config.Get("Test", "PlacesAddon", "");
            if (ad.Length > 0 && !addonAsked && Net.Session.IsHost && now - levelAt > 15f)
            {
                addonAsked = true;
                int id = -1;
                for (int i = 1; i < VazCar.Models.Length; i++) if (VazCar.Models[i].Folder == ad) id = i;
                if (ad == "vaz") id = VazCar.ModelVaz;
                Log.Info("places : addon d'essai " + ad + (id < 0 ? " inconnu" : !VazCar.Has(id) ? " pas installe" : " -> " + Vaz.Request(3, id)));
            }
        }

        // [Test] PlacesPoses=<dossier> (avec PlacesSonde=<voiture>) : pose reelle des avatars assis dans cette voiture, pour l'outil (la
        // moto : bassin sur la selle, pieds aux repose-pieds, mains au guidon ou au porte-bagages, calcules en jeu) -- pose-<voiture>-<place>.json :
        // racine de l'avatar et chaque noeud (position et rotation locales), repere de la voiture
        static void DumpPoses()
        {
            string dir = Config.Get("Test", "PlacesPoses", ""), car = Config.Get("Test", "PlacesSonde", "");
            if (dir.Length == 0 || car.Length == 0) return;
            Transform ct = null; string key = null;
            for (int i = 0; i < VehicleSync.LocalCount && ct == null; i++)
                if (VehicleSync.LocalKey(i) != null && VehicleSync.LocalKey(i).StartsWith(car) && VehicleSync.LocalBody(i) != null) { ct = VehicleSync.LocalBody(i).transform; key = Key(VehicleSync.LocalKey(i)); }
            if (ct == null) return;
            var ci = CultureInfo.InvariantCulture;
            Func<float, string> F = x => x.ToString("R", ci);
            Func<Vector3, string> V = v => "[" + F(v.x) + "," + F(v.y) + "," + F(v.z) + "]";
            Func<Quaternion, string> Q = q => "[" + F(q.x) + "," + F(q.y) + "," + F(q.z) + "," + F(q.w) + "]";
            foreach (Avatar a in PlayerSync.Avatars)
            {
                if (a.Root == null || a.Player == null || (a.Root.transform.position - ct.position).sqrMagnitude > 25f) continue;
                int idx = Seats.RemoteSeatIndex(a.Player.Id);
                string rc = VehicleSync.RemoteCarName(a.Player.Id);
                string place = idx >= 0 ? idx.ToString(ci) : rc != null && rc.StartsWith(car) ? "c" : null;
                if (place == null) continue;
                Transform r = a.Root.transform;
                var sb = new System.Text.StringBuilder("{\"cle\":\"" + key + "\",\"place\":\"" + place + "\",\"racine\":{\"p\":" + V(ct.InverseTransformPoint(r.position)) + ",\"q\":" + Q(Quaternion.Inverse(ct.rotation) * r.rotation) + "},\"noeuds\":{");
                var seen = new HashSet<string>(); int n = 0;
                foreach (Transform t in r.GetComponentsInChildren<Transform>())
                {
                    if (t == r || !seen.Add(t.name)) continue;
                    sb.Append(n++ > 0 ? "," : "").Append('"').Append(t.name.Replace("\"", "")).Append("\":{\"p\":").Append(V(t.localPosition)).Append(",\"q\":").Append(Q(t.localRotation)).Append('}');
                }
                sb.Append("}}");
                try
                {
                    Directory.CreateDirectory(dir);
                    string safe = System.Text.RegularExpressions.Regex.Replace(key, "[^A-Za-z0-9_-]", "_");
                    File.WriteAllText(Path.Combine(dir, "pose-" + safe + "-" + place + ".json"), sb.ToString());
                }
                catch (Exception e) { Log.Warn("places : pose " + key + " " + place + " : " + e.Message); }
            }
        }

        // [Test] PlacesSonde=<voiture> : toutes les 2 s, os des avatars dans cette voiture (repere de la voiture) -- a comparer avec
        // l'outil des places, meme place (tete, mains, genoux, chevilles, bassin)
        static readonly string[] ProbeBones = { "pelvis", "head", "hand_left", "hand_right", "knee_left", "knee_right", "ankle_left", "ankle_right" };
        static void Probe()
        {
            string car = Config.Get("Test", "PlacesSonde", "");
            if (car.Length == 0) return;
            Transform ct = null;
            for (int i = 0; i < VehicleSync.LocalCount && ct == null; i++) if (VehicleSync.LocalKey(i) != null && VehicleSync.LocalKey(i).StartsWith(car) && VehicleSync.LocalBody(i) != null) ct = VehicleSync.LocalBody(i).transform;
            if (ct == null) return;
            foreach (Avatar a in PlayerSync.Avatars)
            {
                if (a.Root == null || (a.Root.transform.position - ct.position).sqrMagnitude > 25f) continue;
                var sb = new System.Text.StringBuilder("places : sonde " + car + " :");
                foreach (string n in ProbeBones)
                    foreach (Transform t in a.Root.GetComponentsInChildren<Transform>())
                        if (t.name == n) { Vector3 l = ct.InverseTransformPoint(t.position); sb.Append(' ').Append(n).Append(' ').Append(l.x.ToString("F3", CultureInfo.InvariantCulture)).Append(',').Append(l.y.ToString("F3", CultureInfo.InvariantCulture)).Append(',').Append(l.z.ToString("F3", CultureInfo.InvariantCulture)); break; }
                Log.Info(sb.ToString());
            }
        }
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
                // (SATSUMA : une place du fichier seulement si son siege est monte -- piece « seat passenger(Clone) » pour la place 0,
                // « seat rear(Clone) » pour les autres, sous la voiture ; un siege pose en vrac dans la voiture n'en est pas l'enfant)
                bool front = true, rear = true;
                if (n.StartsWith("SATSUMA"))
                {
                    front = rear = false;
                    foreach (Transform t in rb.GetComponentsInChildren<Transform>())
                    {
                        if (t.name == "seat passenger(Clone)") front = true;
                        else if (t.name == "seat rear(Clone)") rear = true;
                    }
                }
                seats.RemoveAll(s => s.Car == n);
                foreach (SeatFiles.Place p in f.Places)
                {
                    if (!(p.N == 0 ? front : rear) && p.Kind != "ridelle" && p.Kind != "couchette") continue;
                    seats.Add(new Seat { Car = n, CarT = rb.transform, Index = p.N, Head = p.Eyes, Yaw = p.Yaw, ZoneX = p.ZoneX, ZoneZ = p.ZoneZ, MaxX = p.MaxX,
                                         Bed = p.Kind == "ridelle", Stand = p.Kind == "debout" });
                }
            }
            fileGen = SeatFiles.Generation;
        }
        static int fileGen;
        // (fichier change en jeu : places refaites tout de suite)
        static bool FilesChanged { get { SeatFiles.Tick(); return fileGen != SeatFiles.Generation; } }
    }
}
