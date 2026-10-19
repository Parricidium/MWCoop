using System.Collections.Generic;
using System.Reflection;
using MWCoop.Net;
using UnityEngine;

namespace MWCoop
{
    // Capots des voitures du pack rallye de JD (Audi, Escort, 205, Celica... retour de JD, 19/10 : ouverture du capot pas
    // synchronisee). Le pack ouvre lui-meme son capot (charniere « 205_capot », clic dans sa zone) : ni automate ni corps
    // articule, rien que MWCoop voie passer. Le mod de la voiture (CarHost) garde l'etat dans son champ « hoodOpen » (et pose l'angle
    // a chaque image vers lui) : lu ici toutes les 0,2 s, envoye quand il change ("@capot", relaye par l'hote), pose tel quel
    // chez les autres -- le pack anime l'ouverture de lui-meme.
    public static class PackHoods
    {
        class Hood { public object C; public FieldInfo Open, Pivot; public string Key; public bool Last; }
        static readonly List<Hood> hoods = new List<Hood>();
        static readonly Dictionary<System.Type, FieldInfo> fields = new Dictionary<System.Type, FieldInfo>();
        static float nextScan, nextRead;
        static PropertyInfo loadedMods;
        static bool told;

        public static void OnLevelLoaded() { hoods.Clear(); nextScan = Time.realtimeSinceStartup + 10f; nextRead = 0f; }

        // (chaque voiture du pack est un mod de MSCLoader -- CarHost -- qui garde ses champs : liste ModLoader.LoadedMods)
        static void Scan()
        {
            if (loadedMods == null)
            {
                foreach (Assembly asm in System.AppDomain.CurrentDomain.GetAssemblies())
                {
                    System.Type ml = asm.GetType("MSCLoader.ModLoader", false);
                    if (ml != null) { loadedMods = ml.GetProperty("LoadedMods", BindingFlags.Public | BindingFlags.Static); break; }
                }
                if (loadedMods == null) { nextScan = Time.realtimeSinceStartup + 60f; if (!told) { told = true; Log.Info("capots : MSCLoader introuvable"); } return; }
            }
            var mods = loadedMods.GetValue(null, null) as System.Collections.IList;
            if (mods == null) { if (!told) { told = true; Log.Info("capots : liste des mods de MSCLoader illisible"); } return; }
            if (!told)
            {
                told = true;
                var sb = new System.Text.StringBuilder();
                foreach (object m in mods) if (m != null) sb.Append(' ').Append(m.GetType().Name);
                Log.Info("capots : " + mods.Count + " mods de MSCLoader :" + sb);
            }
            // (le mod du pack tient ses voitures dans un tableau ou une liste : un niveau plus bas aussi)
            var objs = new List<object>();
            foreach (object m in mods)
            {
                if (m == null) continue;
                objs.Add(m);
                foreach (FieldInfo fi in m.GetType().GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                {
                    if (!typeof(System.Collections.IEnumerable).IsAssignableFrom(fi.FieldType) || fi.FieldType == typeof(string)) continue;
                    var en = fi.GetValue(m) as System.Collections.IEnumerable;
                    if (en == null) continue;
                    try { foreach (object e in en) if (e != null && !(e is Object) && OpenField(e.GetType()) != null) objs.Add(e); } catch { }
                }
            }
            foreach (object m in objs)
            {
                if (m == null || hoods.Exists(x => x.C == m)) continue;
                FieldInfo f = OpenField(m.GetType());
                if (f == null) continue;
                FieldInfo pv = Field(m.GetType(), "hoodPivot");
                Transform t = pv.GetValue(m) as Transform;
                if (t == null) continue;   // (voiture pas encore appelee)
                var h = new Hood { C = m, Open = f, Pivot = pv, Key = t.root.name };
                h.Last = (bool)f.GetValue(m);
                hoods.Add(h);
                Log.Info("capots : " + h.Key + " suivi (" + m.GetType().Name + ", " + (h.Last ? "ouvert" : "ferme") + ")");
            }
        }

        // Champ bool « hoodOpen » d'un script qui a aussi sa charniere « hoodPivot » (Transform) : le capot du pack.
        static FieldInfo OpenField(System.Type t)
        {
            FieldInfo f;
            if (fields.TryGetValue(t, out f)) return f;
            f = Field(t, "hoodOpen");
            FieldInfo pivot = Field(t, "hoodPivot");
            if (f == null || f.FieldType != typeof(bool) || pivot == null || pivot.FieldType != typeof(Transform)) f = null;
            fields[t] = f;
            return f;
        }

        // (champ prive d'une classe de base -- CarHost du pack : GetField sur la classe de la voiture ne le voit pas)
        static FieldInfo Field(System.Type t, string name)
        {
            for (; t != null && t != typeof(object); t = t.BaseType)
            {
                FieldInfo f = t.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);
                if (f != null) return f;
            }
            return null;
        }

        public static void Update()
        {
            if (!Session.Active || !PlayerSync.InGame) return;
            float now = Time.realtimeSinceStartup;
            if (now >= nextScan) { nextScan = now + 10f; Scan(); }
            if (hoods.Count == 0 || now < nextRead) return;
            nextRead = now + 0.2f;
            foreach (Hood h in hoods)
            {
                if (h.C == null || h.Pivot.GetValue(h.C) as Transform == null) continue;
                bool open = (bool)h.Open.GetValue(h.C);
                if (open == h.Last) continue;
                h.Last = open;
                Session.SendAll(new NetWriter(Msg.Job).U8(Session.LocalId).Str("@capot").Str(h.Key).Bool(open), true);
                Log.Info("capots : " + h.Key + " " + (open ? "ouvert" : "ferme") + " ici, envoye");
            }
        }

        // Essais (autotest « mscmort », [Test] TestCapot=1) : capot de la voiture ouvert ou ferme comme par le clic du joueur.
        public static string TestSet(string car, bool open)
        {
            Scan();
            Hood h = hoods.Find(x => x.C != null && x.Key.StartsWith(car));
            if (h == null) return "capot : " + car + " introuvable (" + hoods.Count + " suivis)";
            h.Open.SetValue(h.C, open);
            return "capot de " + h.Key + " " + (open ? "ouvert" : "ferme") + " (comme un clic)";
        }
        public static string State()
        {
            var sb = new System.Text.StringBuilder("capots :");
            foreach (Hood h in hoods) if (h.C != null) sb.Append(' ').Append(h.Key).Append(' ').Append((bool)h.Open.GetValue(h.C) ? "ouvert" : "ferme").Append(';');
            return sb.ToString();
        }

        public static void OnMessage(int who, NetReader r)
        {
            string key = r.Str();
            bool open = r.Bool();
            Hood h = hoods.Find(x => x.C != null && x.Key == key);
            if (h == null) { Scan(); h = hoods.Find(x => x.C != null && x.Key == key); }
            if (h == null) { Log.Warn("capots : " + key + " " + (open ? "ouvert" : "ferme") + " par #" + who + ", voiture introuvable ici"); return; }
            h.Open.SetValue(h.C, open);
            h.Last = open;
            Log.Info("capots : " + key + " " + (open ? "ouvert" : "ferme") + " par #" + who);
        }
    }
}
