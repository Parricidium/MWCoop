using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace MWCoop
{
    // Releve de performance (retour d'un joueur, 10/10 : ~10 images/s de moins avec MSCLoader, 40 -> 15-20 sur un PC
    // modeste) : images par seconde, image la plus longue, et ce que coute le mod (chaque module de la boucle, poses,
    // physique, interface) en ms par image. Une ligne par minute dans le journal ([Test] JournalPerf=N : toutes les N s).
    public static class Perf
    {
        static readonly Dictionary<string, double> acc = new Dictionary<string, double>();
        static readonly System.Diagnostics.Stopwatch part = new System.Diagnostics.Stopwatch();
        static int frames;
        static float since = -1, worst, lastFrame, period = -1;

        public static void Add(string what, double ms)
        {
            double v;
            acc.TryGetValue(what, out v);
            acc[what] = v + ms;
        }
        public static void Begin() { part.Reset(); part.Start(); }

        // Sous-partie d'un module (detail du releve, entre crochets) ; les exceptions remontent comme avant.
        static readonly System.Diagnostics.Stopwatch sub = new System.Diagnostics.Stopwatch();
        public static void Sub(string what, System.Action a)
        {
            sub.Reset(); sub.Start();
            try { a(); }
            finally { sub.Stop(); Add("[" + what + "]", sub.Elapsed.TotalMilliseconds); }
        }
        public static void End(string what) { part.Stop(); Add(what, part.Elapsed.TotalMilliseconds); }

        // [Test] PilesJournal=texte : d'ou viennent les messages Unity qui contiennent ce texte (pile d'appels du mod),
        // comptes et ecrits toutes les 20 s.
        static readonly Dictionary<string, int> stacks = new Dictionary<string, int>();
        static string traceText;
        static float traceAt;
        static void OnUnityLog(string msg, string st, LogType type)
        {
            if (msg == null || msg.IndexOf(traceText, System.StringComparison.Ordinal) < 0) return;
            string[] lines = System.Environment.StackTrace.Split('\n');
            var sb = new StringBuilder();
            int n = 0;
            foreach (string l in lines)
            {
                if (l.IndexOf("MWCoop.", System.StringComparison.Ordinal) < 0 || l.IndexOf("MWCoop.Perf", System.StringComparison.Ordinal) >= 0) continue;
                sb.Append(l.Trim().Replace("at MWCoop.", "")).Append(" < ");
                if (++n >= 4) break;
            }
            string k = sb.Length > 0 ? sb.ToString() : "(hors du mod)";
            int c; stacks.TryGetValue(k, out c); stacks[k] = c + 1;
        }

        public static void Frame()
        {
            float now = Time.realtimeSinceStartup;
            if (traceText == null)
            {
                traceText = Config.Get("Test", "PilesJournal", "");
                if (traceText.Length > 0) { Application.logMessageReceived += OnUnityLog; traceAt = now + 20f; }
            }
            if (traceText.Length > 0 && now >= traceAt && stacks.Count > 0)
            {
                traceAt = now + 20f;
                foreach (var kv in stacks) Log.Info("piles : " + kv.Value + " x " + kv.Key);
                stacks.Clear();
            }
            if (since < 0) { since = lastFrame = now; return; }
            frames++;
            worst = Mathf.Max(worst, now - lastFrame);
            lastFrame = now;
            if (period < 0) period = Mathf.Max(5, Config.GetInt("Test", "JournalPerf", 60));
            if (now - since < period || frames == 0) return;
            double total = 0;
            var items = new List<KeyValuePair<string, double>>(acc);
            foreach (var kv in items) if (!kv.Key.StartsWith("[")) total += kv.Value;   // (sous-parties : deja dans leur module)
            items.Sort((a, b) => b.Value.CompareTo(a.Value));
            var sb = new StringBuilder("perf : " + (frames / (now - since)).ToString("F0") + " images/s (plus longue " + (worst * 1000f).ToString("F0") + " ms), mod "
                                       + (total / frames).ToString("F2") + " ms/image" + (Application.targetFrameRate > 0 ? ", limite " + Application.targetFrameRate : "")
                                       + (QualitySettings.vSyncCount > 0 ? ", vsync " + QualitySettings.vSyncCount : ""));
            for (int i = 0; i < items.Count && i < 10; i++) sb.Append(i == 0 ? " : " : ", ").Append(items[i].Key).Append(' ').Append((items[i].Value / frames).ToString("F2"));
            Log.Info(sb.ToString());
            acc.Clear();
            frames = 0; worst = 0; since = now;
        }
    }
}
