using System;
using System.Collections;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace MWCoop
{
    // MSCLoader (onglet MODS du lanceur, experimental). Le lanceur ne peut pas lire le nom, la version ni les options
    // d'un mod dans sa DLL (c'est du code) : MSCLoader les connait une fois les mods charges. Toutes les 15 s, s'il est
    // la, on releve par reflexion (rien de son code n'est repris : il reste une dependance facultative)
    // ModLoader.LoadedMods (sans ses deux mods internes MSCLoader_*) et, pour chaque mod, sa liste d'options
    // (modSettingsList : type, ID, libelle, valeur, defaut, bornes, decimales, choix) et ses raccourcis
    // (modKeybindsList) ; ecrit dans MWCoop\cache\mscloader.txt s'il a change. Une ligne par element, champs separes
    // par des tabulations (les tabulations et retours a la ligne des textes deviennent des espaces) :
    //   loader <version de MSCLoader>
    //   mod    <fichier dll> <ID> <nom> <version> <auteur> <desactive 0|1> <description>
    //   set    <type> <ID> <libelle> <valeur> <defaut> <min> <max> <decimales> <choix separes par '|'> <groupe>
    //   key    <ID> <libelle> <titre 0|1>
    // Les valeurs choisies sont dans les settings.json de MSCLoader (le lanceur les lit et les ecrit lui-meme).
    public static class MscMods
    {
        static float next = 8f;
        static string lastText;
        static bool warned;

        public static void Update()
        {
            if (Time.realtimeSinceStartup < next) return;
            next = Time.realtimeSinceStartup + 15f;
            try { Export(); }
            catch (Exception e) { if (!warned) { warned = true; Log.Warn("mscloader : releve impossible : " + e.Message); } next += 60f; }
        }

        static Type FindType(string name)
        {
            foreach (Assembly a in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (a.GetName().Name != "MSCLoader") continue;
                Type t = a.GetType(name);
                if (t != null) return t;
            }
            return null;
        }

        const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        static object Get(object o, string name)
        {
            for (Type t = o.GetType(); t != null; t = t.BaseType)
            {
                FieldInfo f = t.GetField(name, Any | BindingFlags.DeclaredOnly);
                if (f != null) return f.GetValue(o);
                PropertyInfo p = t.GetProperty(name, Any | BindingFlags.DeclaredOnly);
                if (p != null && p.GetIndexParameters().Length == 0) return p.GetValue(o, null);
            }
            return null;
        }

        static string Clean(object v)
        {
            if (v == null) return "";
            string s = v is float ? ((float)v).ToString("R", System.Globalization.CultureInfo.InvariantCulture)
                     : v is double ? ((double)v).ToString("R", System.Globalization.CultureInfo.InvariantCulture)
                     : v is bool ? ((bool)v ? "1" : "0")
                     : Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture);
            return s.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
        }

        static string Items(object v)
        {
            var arr = v as string[];
            if (arr == null) return "";
            var sb = new StringBuilder();
            for (int i = 0; i < arr.Length; i++) { if (i > 0) sb.Append('|'); sb.Append(Clean(arr[i]).Replace('|', '/')); }
            return sb.ToString();
        }

        static void Export()
        {
            Type ml = FindType("MSCLoader.ModLoader");
            if (ml == null) { next += 45f; return; }   // pas de MSCLoader : on regarde plus rarement
            PropertyInfo lp = ml.GetProperty("LoadedMods", BindingFlags.Public | BindingFlags.Static);
            var mods = lp != null ? lp.GetValue(null, null) as IList : null;
            if (mods == null) return;
            var sb = new StringBuilder();
            sb.Append("loader\t").Append(Clean(ml.Assembly.GetName().Version)).Append('\n');
            int n = 0;
            foreach (object m in mods)
            {
                if (m == null) continue;
                string id = Clean(Get(m, "ID"));
                if (id.Length == 0 || id.StartsWith("MSCLoader_")) continue;
                n++;
                string file = "";
                try { file = Path.GetFileName(m.GetType().Assembly.Location); } catch { }
                sb.Append("mod\t").Append(Clean(file)).Append('\t').Append(id).Append('\t').Append(Clean(Get(m, "Name"))).Append('\t')
                  .Append(Clean(Get(m, "Version"))).Append('\t').Append(Clean(Get(m, "Author"))).Append('\t').Append(Clean(Get(m, "isDisabled"))).Append('\t')
                  .Append(Clean(Get(m, "Description"))).Append('\n');
                var settings = Get(m, "modSettingsList") as IList;
                if (settings != null)
                    foreach (object s in settings)
                    {
                        if (s == null) continue;
                        object items = Get(s, "ArrayOfItems") ?? Get(s, "TextValues");
                        object def = Get(s, "DefaultValue") ?? Get(s, "DefaultColorValue");
                        sb.Append("set\t").Append(Clean(Get(s, "SettingType"))).Append('\t').Append(Clean(Get(s, "ID"))).Append('\t').Append(Clean(Get(s, "Name"))).Append('\t')
                          .Append(Clean(Get(s, "Value"))).Append('\t').Append(Clean(def)).Append('\t').Append(Clean(Get(s, "MinValue"))).Append('\t')
                          .Append(Clean(Get(s, "MaxValue"))).Append('\t').Append(Clean(Get(s, "DecimalPoints"))).Append('\t').Append(Items(items)).Append('\t')
                          .Append(Clean(Get(s, "CheckBoxGroup"))).Append('\n');
                    }
                var keys = Get(m, "modKeybindsList") as IList;
                if (keys != null)
                    foreach (object k in keys)
                        if (k != null) sb.Append("key\t").Append(Clean(Get(k, "ID"))).Append('\t').Append(Clean(Get(k, "Name"))).Append('\t').Append(Clean(Get(k, "IsHeader"))).Append('\n');
            }
            string text = sb.ToString();
            if (text == lastText) return;
            lastText = text;
            string dir = Path.Combine(Environment.GetEnvironmentVariable("MWCOOP_DIR") ?? "MWCoop", "cache");
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, "mscloader.txt"), tmp = path + ".tmp";
            File.WriteAllText(tmp, text, new UTF8Encoding(false));
            if (File.Exists(path)) File.Delete(path);
            File.Move(tmp, path);
            Log.Info("mscloader : " + n + " mod(s) releve(s) pour le lanceur (" + Application.loadedLevelName + ")");
        }
    }
}
