using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Web.Script.Serialization;
using Microsoft.Win32;

namespace Convertisseur
{
    // Assetto Corsa du joueur (bibliotheques Steam, ou dossier choisi) et ses circuits : content\tracks\<id>, avec leurs
    // configurations (models.ini ou models_<config>.ini, ui\ui_track.json ou ui\<config>\ui_track.json, data\ ou
    // <config>\data\). My Winter Car : ou ecrire les destinations (Mods\Assets\Voyage\destinations).
    public class Layout
    {
        public string Id = "", Name = "", Country = "", City = "", Author = "", Description = "", Length = "", Pitboxes = "", Version = "";
        public string Models, Ui, Data;   // (chemins)
        public string Preview, Outline;
    }

    public class Track
    {
        public string Id, Dir;
        public readonly List<Layout> Layouts = new List<Layout>();
        public long Size;
    }

    public static class Ac
    {
        public static string ConfDir { get { return Path.Combine(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MWCoop"), "convertisseur"); } }
        public static string ConfIni { get { return Path.Combine(ConfDir, "convertisseur.ini"); } }

        public static string FindAc()
        {
            string saved = IniGet(ConfIni, "Dossiers", "AssettoCorsa");
            if (CheckAc(saved)) return saved;
            foreach (string lib in SteamLibraries())
            {
                string d = Path.Combine(lib, @"steamapps\common\assettocorsa");
                if (CheckAc(d)) return d;
            }
            return null;
        }
        public static bool CheckAc(string d) { return !string.IsNullOrEmpty(d) && Directory.Exists(Path.Combine(d, @"content\tracks")); }

        // les deux jeux (le mod Voyage marche dans les deux, JD 24/10) : dossier Steam, exe, dossier _Data, cle du .ini
        public class GameInfo { public string Id, Name, Exe, Data, Steam, Key, LoaderPack; }
        public static readonly GameInfo Mwc = new GameInfo { Id = "mwc", Name = "My Winter Car", Exe = "mywintercar.exe", Data = "mywintercar_Data", Steam = "My Winter Car", Key = "MyWinterCar", LoaderPack = "mscloader-mwcoop" };
        public static readonly GameInfo Msc = new GameInfo { Id = "msc", Name = "My Summer Car", Exe = "mysummercar.exe", Data = "mysummercar_Data", Steam = "My Summer Car", Key = "MySummerCar", LoaderPack = "mscloader-mwcoop-msc" };
        public static GameInfo GameOf(string id) { return id == "msc" ? Msc : Mwc; }

        public static string FindGame(GameInfo g)
        {
            string o = Environment.GetEnvironmentVariable(g == Msc ? "CONVERTISSEUR_JEU_MSC" : "CONVERTISSEUR_JEU");   // (essais : instances de test)
            if (CheckGame(g, o)) return o.TrimEnd('\\', '/');
            string saved = IniGet(ConfIni, "Dossiers", g.Key);
            if (CheckGame(g, saved)) return saved;
            foreach (string lib in SteamLibraries())
            {
                string d = Path.Combine(lib, @"steamapps\common\" + g.Steam);
                if (CheckGame(g, d)) return d;
            }
            return null;
        }
        public static bool CheckGame(GameInfo g, string d) { return !string.IsNullOrEmpty(d) && File.Exists(Path.Combine(d, g.Exe)); }
        public static string FindMwc() { return FindGame(Mwc); }
        public static bool CheckMwc(string d) { return CheckGame(Mwc, d); }

        // MSCLoader : installe dans le jeu (officiel), ou celui que le lanceur de MWCoop telecharge pour ce jeu
        // (%LOCALAPPDATA%\MWCoop\mscloader-mwcoop[-msc], lance par le lanceur)
        public static bool HasLoader(GameInfo g, string dir)
        {
            if (dir == null) return false;
            if (File.Exists(Path.Combine(Path.Combine(Path.Combine(dir, g.Data), "Managed"), "MSCLoader.dll"))) return true;
            if (File.Exists(Path.Combine(dir, "winhttp.dll")) && File.Exists(Path.Combine(dir, "doorstop_config.ini"))) return true;
            string own = Path.Combine(Path.Combine(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MWCoop"), g.LoaderPack), "Managed");
            return File.Exists(Path.Combine(own, "MSCLoader.dll"));
        }

        // circuits d'origine du jeu (Kunos Simulazioni) : leur ui_track.json ne donne pas d'auteur
        static readonly string[] Kunos = { "imola", "magione", "monza", "mugello", "spa", "drift", "trento-bondone", "vallelunga", "nurburgring", "silverstone" };
        public static bool IsKunos(string id) { return id.StartsWith("ks_") || Array.IndexOf(Kunos, id) >= 0; }

        public static string DestinationsDir(string mwc) { return Path.Combine(mwc, @"Mods\Assets\Voyage\destinations"); }

        public static void Remember(string key, string dir)
        {
            Directory.CreateDirectory(ConfDir);
            if (!File.Exists(ConfIni)) File.WriteAllText(ConfIni, "[Dossiers]\r\n", Encoding.Unicode);
            WritePrivateProfileString("Dossiers", key, dir, ConfIni);
        }

        // ---------------------------------------------------------------- circuits
        public static List<Track> Tracks(string ac)
        {
            var l = new List<Track>();
            string root = Path.Combine(ac, @"content\tracks");
            foreach (string dir in Directory.GetDirectories(root))
            {
                try
                {
                    Track t = ReadTrack(dir);
                    if (t != null && t.Layouts.Count > 0) l.Add(t);
                }
                catch { }
            }
            l.Sort((a, b) => string.Compare(a.Layouts[0].Name, b.Layouts[0].Name, StringComparison.OrdinalIgnoreCase));
            return l;
        }

        public static Track ReadTrack(string dir)
        {
            var t = new Track { Id = Path.GetFileName(dir), Dir = dir };
            foreach (string f in Directory.GetFiles(dir, "*.kn5")) t.Size += new FileInfo(f).Length;
            var cfgs = new List<string>();
            foreach (string f in Directory.GetFiles(dir, "models_*.ini")) cfgs.Add(Path.GetFileNameWithoutExtension(f).Substring(7));
            if (cfgs.Count == 0)
            {
                string models = Path.Combine(dir, "models.ini");
                string kn5 = Path.Combine(dir, t.Id + ".kn5");
                if (!File.Exists(models) && !File.Exists(kn5)) return null;
                t.Layouts.Add(MakeLayout(t, "", File.Exists(models) ? models : kn5, Path.Combine(dir, "ui"), Path.Combine(dir, "data")));
            }
            else
            {
                cfgs.Sort(StringComparer.OrdinalIgnoreCase);
                foreach (string c in cfgs)
                    t.Layouts.Add(MakeLayout(t, c, Path.Combine(dir, "models_" + c + ".ini"), Path.Combine(Path.Combine(dir, "ui"), c), Path.Combine(Path.Combine(dir, c), "data")));
            }
            return t;
        }

        static Layout MakeLayout(Track t, string cfg, string models, string ui, string data)
        {
            var l = new Layout { Id = cfg, Models = models, Ui = ui, Data = Directory.Exists(data) ? data : Path.Combine(t.Dir, "data"), Name = t.Id };
            string json = Path.Combine(ui, "ui_track.json");
            if (File.Exists(json))
            {
                try
                {
                    // (fichiers parfois mal formes : virgules en trop, guillemets typographiques)
                    string txt = File.ReadAllText(json).Replace(",\r\n}", "\r\n}").Replace(",\n}", "\n}");
                    var d = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(txt);
                    l.Name = Str(d, "name", t.Id); l.Country = Str(d, "country", ""); l.City = Str(d, "city", ""); l.Author = Str(d, "author", "");
                    l.Description = Str(d, "description", ""); l.Length = Str(d, "length", ""); l.Pitboxes = Str(d, "pitboxes", ""); l.Version = Str(d, "version", "");
                }
                catch { }
            }
            if (l.Author.Length == 0 && IsKunos(t.Id)) l.Author = "Kunos Simulazioni";
            if (File.Exists(Path.Combine(ui, "preview.png"))) l.Preview = Path.Combine(ui, "preview.png");
            if (File.Exists(Path.Combine(ui, "outline.png"))) l.Outline = Path.Combine(ui, "outline.png");
            return l;
        }

        static string Str(Dictionary<string, object> d, string k, string def)
        {
            object v;
            if (d == null || !d.TryGetValue(k, out v) || v == null) return def;
            string s = v.ToString().Trim();
            return s.Length == 0 ? def : s;
        }

        // .kn5 d'une configuration : models*.ini ([MODEL_n] FILE=, POSITION=x,y,z), ou le .kn5 du circuit
        public class Model { public string File; public float X, Y, Z; }
        public static List<Model> Models(Track t, Layout l)
        {
            var list = new List<Model>();
            if (l.Models.EndsWith(".kn5", StringComparison.OrdinalIgnoreCase)) { list.Add(new Model { File = l.Models }); return list; }
            Model cur = null;
            foreach (string raw in File.ReadAllLines(l.Models))
            {
                string line = raw.Trim();
                if (line.StartsWith("[")) { cur = line.StartsWith("[MODEL", StringComparison.OrdinalIgnoreCase) ? new Model() : null; continue; }
                if (cur == null) continue;
                int eq = line.IndexOf('=');
                if (eq < 0) continue;
                string k = line.Substring(0, eq).Trim().ToUpperInvariant(), v = line.Substring(eq + 1).Trim();
                if (k == "FILE") { cur.File = Path.Combine(t.Dir, v); list.Add(cur); }
                else if (k == "POSITION")
                {
                    string[] p = v.Split(',');
                    var ci = System.Globalization.CultureInfo.InvariantCulture;
                    if (p.Length == 3) { float.TryParse(p[0], System.Globalization.NumberStyles.Float, ci, out cur.X); float.TryParse(p[1], System.Globalization.NumberStyles.Float, ci, out cur.Y); float.TryParse(p[2], System.Globalization.NumberStyles.Float, ci, out cur.Z); }
                }
            }
            list.RemoveAll(m => !File.Exists(m.File));
            return list;
        }

        // cles des surfaces physiques (data\surfaces.ini : [SURFACE_n] KEY=ROAD) ; un maillage « 1ROAD... » est une surface
        public static List<string> SurfaceKeys(Layout l)
        {
            var keys = new List<string>();
            string f = Path.Combine(l.Data, "surfaces.ini");
            if (!File.Exists(f)) return keys;
            foreach (string raw in File.ReadAllLines(f))
            {
                string line = raw.Trim();
                if (line.StartsWith("KEY=", StringComparison.OrdinalIgnoreCase)) keys.Add(line.Substring(4).Trim().ToUpperInvariant());
            }
            keys.Sort((a, b) => b.Length.CompareTo(a.Length));
            return keys;
        }

        // ---------------------------------------------------------------- Steam, ini
        public static List<string> SteamLibraries()
        {
            var roots = new List<string>();
            try { using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam")) { var v = k?.GetValue("SteamPath") as string; if (!string.IsNullOrEmpty(v)) roots.Add(v.Replace('/', '\\')); } } catch { }
            try { using (var k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Valve\Steam")) { var v = k?.GetValue("InstallPath") as string; if (!string.IsNullOrEmpty(v)) roots.Add(v); } } catch { }
            roots.Add(@"C:\Program Files (x86)\Steam");
            var libs = new List<string>();
            foreach (string r in roots)
            {
                Add(libs, r);
                try
                {
                    string vdf = Path.Combine(r, @"steamapps\libraryfolders.vdf");
                    if (!File.Exists(vdf)) continue;
                    foreach (string line in File.ReadAllLines(vdf))
                    {
                        string tl = line.Trim();
                        if (!tl.StartsWith("\"path\"")) continue;
                        int a = tl.IndexOf('"', 6), b = tl.LastIndexOf('"');
                        if (a > 0 && b > a) Add(libs, tl.Substring(a + 1, b - a - 1).Replace("\\\\", "\\"));
                    }
                }
                catch { }
            }
            return libs;
        }
        static void Add(List<string> l, string d) { foreach (string x in l) if (string.Equals(x.TrimEnd('\\'), d.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) return; l.Add(d); }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        static extern int GetPrivateProfileString(string section, string key, string def, StringBuilder ret, int size, string file);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        static extern bool WritePrivateProfileString(string section, string key, string val, string file);
        public static string IniGet(string file, string section, string key)
        {
            if (!File.Exists(file)) return "";
            var sb = new StringBuilder(1024);
            GetPrivateProfileString(section, key, "", sb, sb.Capacity, file);
            return sb.ToString().Trim();
        }
        public static void IniSet(string file, string section, string key, string val) { WritePrivateProfileString(section, key, val, file); }
    }
}
