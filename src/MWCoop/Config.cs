using System;
using System.Collections.Generic;
using System.IO;

namespace MWCoop
{
    // MWCoop\mwcoop.ini (ecrit par le lanceur) : [Coop] pour le joueur, [Test] pour les essais.
    // Les arguments -mwcoop-<cle> <valeur> de la ligne de commande passent devant.
    public static class Config
    {
        static readonly Dictionary<string, string> values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public static string IniPath;
        public static string LaunchedBy = "";

        public static void Load()
        {
            string dir = Environment.GetEnvironmentVariable("MWCOOP_DIR");
            if (string.IsNullOrEmpty(dir)) dir = Path.Combine(Environment.CurrentDirectory, "MWCoop");
            IniPath = Path.Combine(dir, "mwcoop.ini");
            if (File.Exists(IniPath))
            {
                string section = "";
                foreach (string raw in File.ReadAllLines(IniPath))
                {
                    string l = raw.Trim();
                    if (l.Length == 0 || l[0] == ';' || l[0] == '#') continue;
                    if (l[0] == '[') { section = l.Trim('[', ']'); continue; }
                    int eq = l.IndexOf('=');
                    if (eq > 0) values[section + "." + l.Substring(0, eq).Trim()] = l.Substring(eq + 1).Trim();
                }
            }
            // MWCoop\lancement.ini (MWCoop.exe, juste avant de lancer le jeu) : passe devant [Coop],
            // valable 3 minutes, puis renomme pour qu'un lancement par Steam ne le reprenne pas.
            string launch = Path.Combine(dir, "lancement.ini");
            if (File.Exists(launch))
            {
                var l = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (string raw in File.ReadAllLines(launch))
                {
                    int eq = raw.IndexOf('=');
                    if (eq > 0) l[raw.Substring(0, eq).Trim()] = raw.Substring(eq + 1).Trim();
                }
                long ts, now = (long)(DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalSeconds;
                string s;
                if (l.TryGetValue("Horodatage", out s) && long.TryParse(s, out ts) && now - ts < 180 && ts - now < 60)
                {
                    foreach (KeyValuePair<string, string> kv in l)
                        if (kv.Key != "Horodatage") values["Coop." + kv.Key] = kv.Value;
                    LaunchedBy = "MWCoop.exe";
                }
                try { File.Copy(launch, Path.Combine(dir, "lancement.lu"), true); File.Delete(launch); } catch { }
            }
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; i++)
                if (args[i].StartsWith("-mwcoop-", StringComparison.OrdinalIgnoreCase) && !args[i + 1].StartsWith("-"))
                    values["Arg." + args[i].Substring(8)] = args[i + 1];
        }

        // L'argument -mwcoop-cle passe devant "Section.Cle" ; une valeur vide vaut 'def'.
        public static string Get(string section, string key, string def)
        {
            string v;
            if (values.TryGetValue("Arg." + key, out v) && v.Length > 0) return v;
            if (values.TryGetValue(section + "." + key, out v) && v.Length > 0) return v;
            return def;
        }

        public static int GetInt(string section, string key, int def)
        {
            int v;
            return int.TryParse(Get(section, key, null), out v) ? v : def;
        }

        // Ecrit Section/Cle dans mwcoop.ini (en gardant le reste du fichier).
        public static void Save(string section, string key, string value)
        {
            values[section + "." + key] = value;
            values.Remove("Arg." + key);
            var lines = new List<string>(File.Exists(IniPath) ? File.ReadAllLines(IniPath) : new string[0]);
            int sec = -1, end = lines.Count;
            for (int i = 0; i < lines.Count; i++)
            {
                string l = lines[i].Trim();
                if (l.StartsWith("["))
                {
                    if (sec >= 0) { end = i; break; }
                    if (string.Equals(l.Trim('[', ']'), section, StringComparison.OrdinalIgnoreCase)) sec = i;
                }
            }
            if (sec < 0) { lines.Add(""); lines.Add("[" + section + "]"); lines.Add(key + "=" + value); }
            else
            {
                bool done = false;
                for (int i = sec + 1; i < end && !done; i++)
                {
                    int eq = lines[i].IndexOf('=');
                    if (eq > 0 && string.Equals(lines[i].Substring(0, eq).Trim(), key, StringComparison.OrdinalIgnoreCase))
                    { lines[i] = key + "=" + value; done = true; }
                }
                if (!done)
                {
                    int at = end;
                    while (at > sec + 1 && lines[at - 1].Trim().Length == 0) at--;
                    lines.Insert(at, key + "=" + value);
                }
            }
            try { File.WriteAllLines(IniPath, lines.ToArray()); } catch (Exception e) { Log.Warn("ecriture de mwcoop.ini : " + e.Message); }
        }

        public static bool HasArg(string name)
        {
            foreach (string a in Environment.GetCommandLineArgs())
                if (string.Equals(a, "-mwcoop-" + name, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }
    }
}
