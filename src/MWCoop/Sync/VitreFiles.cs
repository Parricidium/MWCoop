using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace MWCoop
{
    // Vitres de l'outil des places (onglet Vitres, suggestion 46, JD 06/11 : les vitres arriere, qui sortaient de la portiere quand le
    // code calculait leur trajet) : MWCoop\vitres\<mwc|msc>\<voiture>.txt, lus par WindowCranks. Un fichier a part : un fichier de
    // places remplace toutes les places calculees de la voiture. Format VITRES 1 :
    //   voiture <nom exact du corps>            (comme les places)
    //   vitre <chemin de l'objet qui bouge, depuis la voiture>
    //   haut <x y z> <qx qy qz qw>              (pose locale de l'objet, vitre fermee)
    //   bas <x y z> <qx qy qz qw>               (pose locale de l'objet, vitre baissee ; entre les deux : interpole selon la manivelle)
    //   coupe <x y z> <nx ny nz>                (plan, repere du parent de l'objet -- il suit la portiere : ce qui est du cote oppose
    //                                             a la normale n'est pas dessine)
    // Sans fichier, rien ne change. Relu quand il change (toutes les 2 s), meme en partie.
    public static class VitreFiles
    {
        public class Glass
        {
            public string Path;
            public Vector3 UpP, DownP, CutP, CutN;
            public Quaternion UpQ = Quaternion.identity, DownQ = Quaternion.identity;
            public bool HasUp, HasDown, HasCut;
        }
        public class CarFile { public string Car; public readonly List<Glass> Glasses = new List<Glass>(); }

        static readonly Dictionary<string, CarFile> files = new Dictionary<string, CarFile>();
        static string stamp;
        static float nextCheck;
        public static int Generation;

        static string Dir
        {
            get
            {
                string root = Environment.GetEnvironmentVariable("MWCOOP_DIR") ?? "MWCoop";
                return Path.Combine(Path.Combine(root, "vitres"), Game.Msc ? "msc" : "mwc");
            }
        }

        public static void OnLevelLoaded() { stamp = null; nextCheck = 0f; }

        // (export pour l'outil : les maillages entiers, a leur pose d'origine)
        public static CarFile For(string car)
        {
            CarFile f;
            return !SeatExport.Active && car != null && files.TryGetValue(SeatFiles.KeyOf(car), out f) ? f : null;
        }
        public static Glass Find(string car, string path)
        {
            CarFile f = For(car);
            if (f != null) foreach (Glass g in f.Glasses) if (g.Path == path) return g;
            return null;
        }

        public static void Check()
        {
            float now = Time.realtimeSinceStartup;
            if (now < nextCheck) return;
            nextCheck = now + 2f;
            string s = "";
            List<string> list = SeatFiles.FilesOf(Dir, "vitres");   // (et le dossier d'une voiture d'un autre mod, JD 10/11)
            foreach (string p in list) { var fi = new FileInfo(p); s += fi.FullName + fi.Length + fi.LastWriteTimeUtc.Ticks + ";"; }
            if (s == stamp) return;
            stamp = s;
            files.Clear();
            var from = new Dictionary<string, string>();
            foreach (string p in list)
            {
                try
                {
                    CarFile f = Read(p);
                    if (f == null || f.Car == null) { Log.Warn("vitres : " + Path.GetFileName(p) + " illisible (pas de VITRES 1 / voiture)"); continue; }
                    string had; from.TryGetValue(f.Car, out had);
                    if (had != null && !SeatFiles.Newer(p, had)) continue;
                    files[f.Car] = f; from[f.Car] = p;
                    Log.Info("vitres : " + f.Car + " : " + f.Glasses.Count + " vitre(s) lue(s) dans " + Path.GetFileName(p));
                }
                catch (Exception e) { Log.Warn("vitres : " + Path.GetFileName(p) + " : " + e.Message); }
            }
            Generation++;
        }

        static CarFile Read(string path)
        {
            var f = new CarFile();
            bool ok = false;
            Glass g = null;
            var ci = CultureInfo.InvariantCulture;
            Func<string, float> F = x => float.Parse(x, NumberStyles.Float, ci);
            foreach (string raw in File.ReadAllText(path).Split('\n'))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line[0] == '#') continue;
                string[] w = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                switch (w[0])
                {
                    case "VITRES": ok = true; break;
                    case "voiture": f.Car = line.Substring(8).Trim(); break;
                    case "vitre": g = new Glass { Path = line.Substring(6).Trim() }; f.Glasses.Add(g); break;
                    case "haut":
                    case "bas":
                        if (g == null || w.Length < 8) break;
                        var p = new Vector3(F(w[1]), F(w[2]), F(w[3]));
                        var q = new Quaternion(F(w[4]), F(w[5]), F(w[6]), F(w[7]));
                        if (w[0] == "haut") { g.UpP = p; g.UpQ = q; g.HasUp = true; } else { g.DownP = p; g.DownQ = q; g.HasDown = true; }
                        break;
                    case "coupe":
                        if (g == null || w.Length < 7) break;
                        g.CutP = new Vector3(F(w[1]), F(w[2]), F(w[3])); g.CutN = new Vector3(F(w[4]), F(w[5]), F(w[6])); g.HasCut = g.CutN.sqrMagnitude > 1e-6f;
                        break;
                }
            }
            f.Glasses.RemoveAll(x => !x.HasDown);   // (sans pose basse : rien a animer)
            return ok ? f : null;
        }
    }
}
