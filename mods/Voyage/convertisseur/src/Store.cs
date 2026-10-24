using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace Convertisseur
{
    // Destinations deja converties (dossier du jeu) : liste, fiche, modification (nom, prix, point d'arrivee), suppression
    // (corbeille de Windows), etat par rapport au circuit d'Assetto Corsa (a jour, a reconvertir, circuit introuvable).
    public static class Store
    {
        static readonly JavaScriptSerializer json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };

        public static Dictionary<string, string> ReadIni(string file)
        {
            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (!File.Exists(file)) return d;
            foreach (string line in File.ReadAllLines(file, Encoding.UTF8))
            {
                int eq = line.IndexOf('=');
                if (eq <= 0 || line.StartsWith(";")) continue;
                d[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
            }
            return d;
        }

        static void WriteIni(string file, Dictionary<string, string> d)
        {
            var sb = new StringBuilder("; Destination du mod Voyage, convertie depuis l'Assetto Corsa du joueur (rien n'est partage)\r\n");
            foreach (var kv in d) sb.Append(kv.Key).Append('=').Append(kv.Value).Append("\r\n");
            File.WriteAllText(file, sb.ToString(), new UTF8Encoding(false));
        }

        public static List<Dictionary<string, object>> List(string destRoot, string ac)
        {
            var l = new List<Dictionary<string, object>>();
            if (destRoot == null || !Directory.Exists(destRoot)) return l;
            foreach (string dir in Directory.GetDirectories(destRoot))
            {
                if (dir.EndsWith(".nouveau")) continue;
                var ini = ReadIni(Path.Combine(dir, "destination.ini"));
                if (ini.Count == 0) continue;
                var e = new Dictionary<string, object>();
                foreach (var kv in ini) e[kv.Key] = kv.Value;
                e["id"] = Path.GetFileName(dir);
                try { string ij = Path.Combine(dir, "infos.json"); if (File.Exists(ij)) e["infos"] = json.DeserializeObject(File.ReadAllText(ij)); } catch { }
                e["apercu"] = File.Exists(Path.Combine(dir, "apercu.png"));
                e["plan"] = File.Exists(Path.Combine(dir, "plan.png"));
                e["etat"] = State(ini, ac);
                l.Add(e);
            }
            l.Sort((a, b) => string.Compare((string)a["nom"], (string)b["nom"], StringComparison.OrdinalIgnoreCase));
            return l;
        }

        // « ok » (a jour), « maj » (le circuit a change depuis la conversion), « introuvable » (plus dans Assetto Corsa)
        public static string State(Dictionary<string, string> ini, string ac)
        {
            string src; if (!ini.TryGetValue("source", out src) || ac == null) return "inconnu";
            string tid = src.Split('/')[0], lid = src.Contains("/") ? src.Substring(src.IndexOf('/') + 1) : "";
            string dir = Path.Combine(Path.Combine(ac, @"content\tracks"), tid);
            if (!Directory.Exists(dir)) return "introuvable";
            Track t = Ac.ReadTrack(dir);
            Layout l = t != null ? t.Layouts.Find(x => x.Id == lid) : null;
            if (l == null) return "introuvable";
            string fp; ini.TryGetValue("empreinte", out fp);
            string cv; ini.TryGetValue("convertisseur", out cv);
            return fp == Conversion.Fingerprint(t, l) && cv == Conversion.Version ? "ok" : "maj";
        }

        public static void Edit(string destRoot, string id, string name, int price, string point)
        {
            string dir = Path.Combine(destRoot, id), f = Path.Combine(dir, "destination.ini");
            var ini = ReadIni(f);
            if (ini.Count == 0) throw new IOException("destination introuvable");
            if (!string.IsNullOrEmpty(name)) ini["nom"] = name.Trim();
            if (price > 0) ini["prix"] = price.ToString(CultureInfo.InvariantCulture);
            if (!string.IsNullOrEmpty(point))
            {
                var info = json.DeserializeObject(File.ReadAllText(Path.Combine(dir, "infos.json"))) as Dictionary<string, object>;
                var pts = info != null && info.ContainsKey("points") ? info["points"] as Dictionary<string, object> : null;
                object p;
                if (pts != null && pts.TryGetValue(point, out p))
                {
                    var a = (object[])p;
                    Func<int, string> f2 = i => Convert.ToDouble(a[i], CultureInfo.InvariantCulture).ToString("0.###", CultureInfo.InvariantCulture);
                    ini["arrivee"] = f2(0) + "," + f2(1) + "," + f2(2);
                    ini["cap"] = f2(3);
                    ini["point"] = point;
                }
            }
            WriteIni(f, ini);
        }

        public static void Delete(string destRoot, string id)
        {
            string dir = Path.Combine(destRoot, id);
            if (!Directory.Exists(dir)) return;
            Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(dir, Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs, Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
        }
    }
}
