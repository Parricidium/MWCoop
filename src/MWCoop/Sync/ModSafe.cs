using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace MWCoop
{
    // Copies de voitures du jeu (VAZ et addons, voitures des Jokkis : Object.Instantiate de la SORBET) a l'abri des mods
    // MSCLoader qui modifient la SORBET (JD 23/10 : « Sorbet Collection » -- ses scripts poses sur la SORBET, garnitures,
    // moteurs, boite automatique, physique du chassis, etaient copies avec elle et retravaillaient nos voitures de rallye :
    // maillages remplaces, voitures deformees). A la copie : tout script qui ne vient ni du jeu, ni de Unity, ni de MWCoop est
    // retire de la copie ; ses variables statiques (« Instance = this » a l'eveil de la copie) remises comme avant la copie,
    // pour que le mod continue de viser la vraie SORBET.
    public static class ModSafe
    {
        public class Snapshot { public readonly List<KeyValuePair<FieldInfo, object>> Statics = new List<KeyValuePair<FieldInfo, object>>(); }

        static bool OurAssembly(Assembly a)
        {
            string n = a.GetName().Name;
            return n.StartsWith("UnityEngine") || n.StartsWith("Unity.") || n == "Assembly-CSharp" || n == "Assembly-CSharp-firstpass"
                || n.StartsWith("Assembly-UnityScript") || n == "PlayMaker" || n == "ConsoleE" || a == typeof(ModSafe).Assembly;
        }

        static bool Foreign(Component c) { return c is MonoBehaviour && !OurAssembly(c.GetType().Assembly); }

        // avant Instantiate : variables statiques des types etrangers poses sur l'objet
        public static Snapshot Before(GameObject src)
        {
            var s = new Snapshot();
            var seen = new HashSet<System.Type>();
            foreach (MonoBehaviour m in src.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (m == null || !Foreign(m)) continue;
                for (System.Type t = m.GetType(); t != null && !OurAssembly(t.Assembly); t = t.BaseType)
                {
                    if (!seen.Add(t)) continue;
                    foreach (FieldInfo f in t.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                    {
                        if (f.IsLiteral || f.IsInitOnly || !typeof(Object).IsAssignableFrom(f.FieldType)) continue;
                        try { s.Statics.Add(new KeyValuePair<FieldInfo, object>(f, f.GetValue(null))); } catch { }
                    }
                }
            }
            return s;
        }

        // apres Instantiate : scripts etrangers retires de la copie, variables statiques qui la visent remises
        public static int Strip(GameObject copy, Snapshot s, string who)
        {
            var names = new SortedDictionary<string, int>();
            foreach (MonoBehaviour m in copy.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (m == null || !Foreign(m)) continue;
                string n = m.GetType().Assembly.GetName().Name + "/" + m.GetType().Name;
                int k; names.TryGetValue(n, out k); names[n] = k + 1;
                try { m.enabled = false; Object.DestroyImmediate(m); } catch (System.Exception e) { Log.Warn(who + " : script " + n + " non retire : " + e.Message); }
            }
            int restored = 0;
            if (s != null)
                foreach (var kv in s.Statics)
                {
                    try
                    {
                        object now = kv.Key.GetValue(null);
                        if (ReferenceEquals(now, kv.Value)) continue;
                        Object o = now as Object;
                        Component c = o as Component; GameObject g = o as GameObject;
                        Transform t = c != null ? c.transform : g != null ? g.transform : null;
                        // (pointe vers la copie, ou vers un script de la copie qui vient d'etre detruit)
                        if (now == null || (o == null && !ReferenceEquals(now, null)) || (t != null && t.IsChildOf(copy.transform)))
                        { kv.Key.SetValue(null, kv.Value); restored++; }
                    }
                    catch { }
                }
            if (names.Count > 0)
            {
                var sb = new System.Text.StringBuilder();
                foreach (var kv in names) sb.Append(sb.Length > 0 ? ", " : "").Append(kv.Key).Append(kv.Value > 1 ? " x" + kv.Value : "");
                Log.Info(who + " : scripts de mods retires de la copie de la SORBET (" + sb + ")" + (restored > 0 ? ", " + restored + " variable(s) statique(s) remise(s) sur la vraie" : ""));
            }
            return names.Count;
        }
    }
}
