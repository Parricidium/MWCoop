using System;
using System.Collections.Generic;
using System.Reflection;

namespace MWCoop
{
    // Champs des mods lus par reflexion (on ne compile pas contre leurs DLL), gardes par type et par nom : un GetField a
    // chaque lecture coutait une recherche dans les metadonnees (300D : 60 lectures, 4 fois par seconde ; refonte du 14/10).
    public static class ModRefl
    {
        public const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        static readonly Dictionary<Type, Dictionary<string, FieldInfo>> fields = new Dictionary<Type, Dictionary<string, FieldInfo>>();

        public static FieldInfo Field(Type t, string name)
        {
            Dictionary<string, FieldInfo> d;
            if (!fields.TryGetValue(t, out d)) fields[t] = d = new Dictionary<string, FieldInfo>();
            FieldInfo f;
            if (!d.TryGetValue(name, out f)) d[name] = f = t.GetField(name, Any);   // (absent : null garde aussi)
            return f;
        }
        public static T Get<T>(object o, string name)
        {
            if (o == null) return default(T);
            FieldInfo f = Field(o.GetType(), name);
            object v = f != null ? f.GetValue(o) : null;
            return v is T ? (T)v : default(T);
        }
        public static void Set(object o, string name, object v)
        {
            if (o == null) return;
            FieldInfo f = Field(o.GetType(), name);
            if (f != null) f.SetValue(o, v);
        }
    }
}
