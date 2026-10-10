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

        static readonly Dictionary<string, double> peak = new Dictionary<string, double>();
        public static void Add(string what, double ms)
        {
            double v;
            acc.TryGetValue(what, out v);
            acc[what] = v + ms;
            if (ms > 2.0 && (!peak.TryGetValue(what, out v) || ms > v)) peak[what] = ms;   // (pointes : a-coups)
        }
        public static void Begin() { part.Reset(); part.Start(); if (AllocOn) partMem = System.GC.GetTotalMemory(false); }

        // [Test] PerfAlloc=1 : memoire allouee par chaque module (Ko par seconde), et par toute l'image (jeu compris).
        static int allocOn = -1;
        public static bool AllocOn { get { if (allocOn < 0) allocOn = Config.GetInt("Test", "PerfAlloc", 0); return allocOn != 0; } }
        static long partMem, frameMem = -1;
        static double frameAlloc;
        static readonly Dictionary<string, double> alloc = new Dictionary<string, double>();
        public static long MemNow() { return AllocOn ? System.GC.GetTotalMemory(false) : 0; }
        public static void AddAlloc(string what, long before)
        {
            if (!AllocOn) return;
            long d = System.GC.GetTotalMemory(false) - before;
            if (d <= 0) return;   // (GC pendant le module)
            double v; alloc.TryGetValue(what, out v); alloc[what] = v + d;
        }

        // Sous-partie d'un module (detail du releve, entre crochets) ; les exceptions remontent comme avant.
        static readonly System.Diagnostics.Stopwatch sub = new System.Diagnostics.Stopwatch();
        public static void Sub(string what, System.Action a)
        {
            long mem = MemNow();
            sub.Reset(); sub.Start();
            try { a(); }
            finally { sub.Stop(); Add("[" + what + "]", sub.Elapsed.TotalMilliseconds); if (AllocOn) AddAlloc("[" + what + "]", mem); }
        }
        // Etapes successives d'un meme passage : temps (et allocations) depuis la marque precedente.
        static readonly System.Diagnostics.Stopwatch markSw = new System.Diagnostics.Stopwatch();
        static long markMem;
        public static void MarkStart() { markSw.Reset(); markSw.Start(); markMem = MemNow(); }
        public static void Mark(string what)
        {
            Add(what, markSw.Elapsed.TotalMilliseconds);
            if (AllocOn) AddAlloc(what, markMem);
            markSw.Reset(); markSw.Start(); markMem = MemNow();
        }

        public static void End(string what) { part.Stop(); Add(what, part.Elapsed.TotalMilliseconds); if (AllocOn) AddAlloc(what, partMem); }

        // Moteur physique du jeu (retour de joueurs, 10/10 : a-coups pres des pieces de voiture) : temps entre la fin de
        // notre FixedUpdate et le pas suivant (ou l'Update de l'image) -- la simulation PhysX y est, avec les FixedUpdate
        // des autres scripts. Et les a-coups : ramasse-miettes (GC) pendant l'image, ou non.
        static readonly System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
        static double fixedEnd = -1, physMs;
        static int gcWas = -1, gcFrames, slowGc, slowOther;
        public static void FixedStart() { double n = clock.Elapsed.TotalMilliseconds; if (fixedEnd >= 0) physMs += n - fixedEnd; }
        public static void FixedEnd() { fixedEnd = clock.Elapsed.TotalMilliseconds; }
        public static void UpdateStart() { if (fixedEnd >= 0) physMs += clock.Elapsed.TotalMilliseconds - fixedEnd; fixedEnd = -1; }

        static string Bodies()
        {
            int all = 0, awake = 0;
            var near = new Dictionary<string, int>();
            Camera cam = Camera.main;
            foreach (Rigidbody rb in Object.FindObjectsOfType<Rigidbody>())
            {
                all++;
                if (rb.isKinematic || rb.IsSleeping()) continue;
                awake++;
                if (cam != null && (rb.position - cam.transform.position).sqrMagnitude < 900f)
                {
                    string n = rb.name; int c; near.TryGetValue(n, out c); near[n] = c + 1;
                }
            }
            var sb = new StringBuilder(", corps " + awake + " eveilles / " + all);
            if (near.Count > 0)
            {
                sb.Append(" (a 30 m :");
                int k = 0;
                foreach (var kv in near) { if (k++ >= 8) { sb.Append(" ..."); break; } sb.Append(' ').Append(kv.Key).Append(kv.Value > 1 ? " x" + kv.Value : ""); }
                sb.Append(')');
            }
            return sb.ToString();
        }

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
            int gc = System.GC.CollectionCount(0);
            bool gcHere = gcWas >= 0 && gc != gcWas;
            gcWas = gc;
            if (since < 0) { since = lastFrame = now; return; }
            frames++;
            if (gcHere) gcFrames++;
            if (AllocOn)
            {
                long m = System.GC.GetTotalMemory(false);
                if (frameMem >= 0 && !gcHere && m > frameMem) frameAlloc += m - frameMem;
                frameMem = m;
            }
            if (now - lastFrame > 0.05f) { if (gcHere) slowGc++; else slowOther++; }
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
                                       + (QualitySettings.vSyncCount > 0 ? ", vsync " + QualitySettings.vSyncCount : "")
                                       + ", physique du jeu " + (physMs / frames).ToString("F2") + " ms/image (pas " + (Time.fixedDeltaTime * 1000f).ToString("F1") + " ms), GC " + gcFrames + " (tas " + (System.GC.GetTotalMemory(false) >> 20) + " Mo)"
                                       + ", a-coups > 50 ms : " + slowGc + " avec GC, " + slowOther + " sans"
                                       + (Game.RootScans > 0 ? ", racines relevees " + Game.RootScans + " fois (" + Game.RootScanMs.ToString("F0") + " ms)" : "")
                                       + (PlayerSync.InGame ? Bodies() : ""));
            if (Game.FsmScans > 0) sb.Append(", automates releves " + Game.FsmScans + " fois (" + Game.FsmScanMs.ToString("F0") + " ms)");
            Game.RootScans = 0; Game.RootScanMs = 0; Game.FsmScans = 0; Game.FsmScanMs = 0;
            for (int i = 0; i < items.Count && i < 10; i++) sb.Append(i == 0 ? " : " : ", ").Append(items[i].Key).Append(' ').Append((items[i].Value / frames).ToString("F2"));
            if (peak.Count > 0)
            {
                var pk = new List<KeyValuePair<string, double>>(peak);
                pk.Sort((a, b) => b.Value.CompareTo(a.Value));
                sb.Append(" ; pointes :");
                for (int i = 0; i < pk.Count && i < 6; i++) sb.Append(' ').Append(pk[i].Key).Append(' ').Append(pk[i].Value.ToString("F0")).Append(" ms");
                peak.Clear();
            }
            Log.Info(sb.ToString());
            if (AllocOn)
            {
                float dt = now - since;
                var al = new List<KeyValuePair<string, double>>(alloc);
                al.Sort((a, b) => b.Value.CompareTo(a.Value));
                double modAll = 0; foreach (var kv in al) if (!kv.Key.StartsWith("[")) modAll += kv.Value;
                var sa = new StringBuilder("perf : allocations " + (frameAlloc / 1024 / dt).ToString("F0") + " Ko/s en tout (images sans GC), mod " + (modAll / 1024 / dt).ToString("F0") + " Ko/s");
                for (int i = 0; i < al.Count && i < 12; i++) sa.Append(i == 0 ? " : " : ", ").Append(al[i].Key).Append(' ').Append((al[i].Value / 1024 / dt).ToString("F0"));
                Log.Info(sa.ToString());
                alloc.Clear(); frameAlloc = 0;
            }
            acc.Clear();
            frames = 0; worst = 0; since = now; physMs = 0; gcFrames = slowGc = slowOther = 0;
        }
    }
}
