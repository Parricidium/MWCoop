using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace EditeurCarte
{
    // Les deux jeux et ou les trouver : chemin retenu par l'editeur, puis celui du lanceur MWCoop ([Lanceur] Jeu / JeuMSC de
    // mwcoop-lanceur.ini), puis les bibliotheques Steam ; sinon le joueur choisit le .exe (joueurs russes : Steam inutilisable).
    public class GameDef
    {
        public string Id, Name, Exe, Data, SteamDir, LauncherKey;
        public static readonly GameDef[] All =
        {
            new GameDef { Id = "msc", Name = "My Summer Car", Exe = "mysummercar.exe", Data = "mysummercar_Data", SteamDir = "My Summer Car", LauncherKey = "JeuMSC" },
            new GameDef { Id = "mwc", Name = "My Winter Car", Exe = "mywintercar.exe", Data = "mywintercar_Data", SteamDir = "My Winter Car", LauncherKey = "Jeu" },
        };
        public static GameDef Get(string id) { foreach (GameDef g in All) if (g.Id == id) return g; return null; }
    }

    public class GameFound
    {
        public string Dir, Source, Version;   // Source : "steam" | "lanceur" | "choisi"
    }

    public static class Games
    {
        public static string LocalMwcoop
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MWCoop"); }
        }
        public static string EditorDir { get { return Path.Combine(LocalMwcoop, "editeur"); } }
        public static string EditorIni { get { return Path.Combine(EditorDir, "editeur.ini"); } }
        public static string LauncherIni { get { return Path.Combine(LocalMwcoop, "mwcoop-lanceur.ini"); } }

        // Cache des cartes preparees : Documents\My Seasons Car\Carte\<msc|mwc>
        public static string CacheRoot
        {
            get
            {
                string o = Environment.GetEnvironmentVariable("EDITEUR_CACHE");
                if (!string.IsNullOrEmpty(o)) return o;
                return Path.Combine(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "My Seasons Car"), "Carte");
            }
        }
        public static string CacheDir(string id) { return Path.Combine(CacheRoot, id); }

        public static GameFound Find(GameDef g)
        {
            // (lance depuis le lanceur, page Outils : le dossier du jeu qu'il connait passe devant tout)
            if (Program.GameDirArg != null && Program.LaunchedFor == g.Id && Check(g, Program.GameDirArg) == null) return Found(g, Program.GameDirArg, "lanceur");
            string saved = IniGet(EditorIni, "Jeux", g.Id);
            if (Check(g, saved) == null) return Found(g, saved, "choisi");
            string l = IniGet(LauncherIni, "Lanceur", g.LauncherKey);
            if (Check(g, l) == null) return Found(g, l, "lanceur");
            foreach (string lib in SteamLibraries())
            {
                string d = Path.Combine(Path.Combine(Path.Combine(lib, "steamapps"), "common"), g.SteamDir);
                if (Check(g, d) == null) return Found(g, d, "steam");
            }
            return null;
        }

        static GameFound Found(GameDef g, string dir, string src) { dir = dir.TrimEnd('\\', '/'); return new GameFound { Dir = dir, Source = src, Version = ReadVersion(dir) }; }

        // null si c'est bien le dossier de ce jeu ; sinon ce qui manque
        public static string Check(GameDef g, string dir)
        {
            if (string.IsNullOrEmpty(dir)) return "vide";
            try
            {
                if (!File.Exists(Path.Combine(dir, g.Exe))) return "exe";
                string data = Path.Combine(dir, g.Data);
                if (!Directory.Exists(data)) return "data";
                if (!File.Exists(Path.Combine(Path.Combine(data, "Managed"), "Assembly-CSharp.dll"))) return "managed";
            }
            catch { return "acces"; }
            return null;
        }

        // Version du jeu : premiere ligne de changelog.txt ("v.260917-03"), comme le lanceur.
        public static string ReadVersion(string dir)
        {
            try
            {
                string p = Path.Combine(dir, "changelog.txt");
                if (!File.Exists(p)) return "";
                using (var r = new StreamReader(p, Encoding.UTF8)) { string l = (r.ReadLine() ?? "").Trim(); return l.Length > 32 ? l.Substring(0, 32) : l; }
            }
            catch { return ""; }
        }

        // Le jeu d'un chemin choisi : le .exe, ou un dossier qui le contient.
        public static string DirOfPick(GameDef g, string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            if (Directory.Exists(path)) return path.TrimEnd('\\');
            return Path.GetDirectoryName(path);
        }

        public static void Remember(GameDef g, string dir, bool forLauncher)
        {
            Directory.CreateDirectory(EditorDir);
            // (fichier cree en UTF-16 : Windows y ecrit alors les chemins en Unicode -- dossiers en cyrillique des joueurs russes)
            if (!File.Exists(EditorIni)) File.WriteAllText(EditorIni, "[Jeux]\r\n", Encoding.Unicode);
            IniSet(EditorIni, "Jeux", g.Id, dir);
            if (forLauncher && Directory.Exists(LocalMwcoop)) IniSet(LauncherIni, "Lanceur", g.LauncherKey, dir.TrimEnd('\\') + "\\");
        }

        // Lanceur MWCoop qui fera l'export : celui installe dans AppData, sinon celui du dossier du jeu (EDITEUR_LANCEUR : essais).
        public static string Launcher(string gameDir)
        {
            string o = Environment.GetEnvironmentVariable("EDITEUR_LANCEUR");
            if (!string.IsNullOrEmpty(o) && File.Exists(o)) return o;
            if (Program.LauncherArg != null && File.Exists(Program.LauncherArg)) return Program.LauncherArg;   // (celui qui nous a lance)
            string a = Path.Combine(LocalMwcoop, "MWCoop.exe");
            if (File.Exists(a)) return a;
            string b = gameDir != null ? Path.Combine(gameDir, "MWCoop.exe") : null;
            return b != null && File.Exists(b) ? b : null;
        }

        static List<string> SteamLibraries()
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
                    string vdf = Path.Combine(Path.Combine(r, "steamapps"), "libraryfolders.vdf");
                    if (!File.Exists(vdf)) continue;
                    foreach (string line in File.ReadAllLines(vdf))
                    {
                        string t = line.Trim();
                        if (!t.StartsWith("\"path\"")) continue;
                        int a = t.IndexOf('"', 6), b = t.LastIndexOf('"');
                        if (a > 0 && b > a) Add(libs, t.Substring(a + 1, b - a - 1).Replace("\\\\", "\\"));
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
