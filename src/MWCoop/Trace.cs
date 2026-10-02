using System.Collections.Generic;
using UnityEngine;

namespace MWCoop
{
    // Diagnostic : journalise pendant quelques secondes chaque changement d'etat d'automates choisis
    // (avec l'evenement de la derniere transition).
    public static class Trace
    {
        class T { public PlayMakerFSM F; public string Label, Last; public float Until; }
        static readonly List<T> list = new List<T>();

        public static void Watch(PlayMakerFSM f, string label, float seconds)
        {
            if (f == null) return;
            list.Add(new T { F = f, Label = label, Last = f.ActiveStateName, Until = Time.realtimeSinceStartup + seconds });
            Log.Info("trace " + label + " : " + f.ActiveStateName);
        }

        public static void Update()
        {
            float now = Time.realtimeSinceStartup;
            for (int i = list.Count - 1; i >= 0; i--)
            {
                T t = list[i];
                if (t.F == null || now > t.Until) { list.RemoveAt(i); continue; }
                string s = t.F.ActiveStateName;
                if (s == t.Last) continue;
                var tr = t.F.Fsm.LastTransition;
                Log.Info("trace " + t.Label + " : " + t.Last + " -> " + s + (tr != null ? " (" + tr.EventName + ")" : ""));
                t.Last = s;
            }
        }
    }
}
