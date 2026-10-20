using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;

namespace EditeurCarte
{
    // Preparation de la carte d'un jeu : le lanceur MWCoop (/exportcarte <jeu> <dossier>) demarre le jeu en arriere-plan, profil
    // isole « carte-export », nouvelle partie ; le mod (MapExport.cs) ecrit le cache et etat.txt, puis ferme le jeu. Ici :
    // suivi de etat.txt, puis le dossier de preparation remplace l'ancien cache.
    public class Prep
    {
        public readonly GameDef Game;
        public readonly string GameDir;
        public volatile bool Cancelled;
        public event Action<Dictionary<string, object>> Changed;   // (fil de travail : l'appelant repasse sur l'interface)
        Thread thread;

        public Prep(GameDef g, string gameDir) { Game = g; GameDir = gameDir; }

        public string WorkDir { get { return Games.CacheDir(Game.Id) + "-preparation"; } }

        public void Start()
        {
            thread = new Thread(Run) { IsBackground = true, Name = "preparation" };
            thread.Start();
        }

        void Send(string stage, double progress, string text, bool done, string error)
        {
            var d = new Dictionary<string, object> { { "type", "prep" }, { "jeu", Game.Id }, { "etape", stage }, { "avance", progress }, { "texte", text }, { "fini", done } };
            if (error != null) d["erreur"] = error;
            Changed?.Invoke(d);
        }

        void Run()
        {
            try { RunCore(); }
            catch (Exception e) { Send("erreur", 0, "", true, e.Message); }
        }

        void RunCore()
        {
            string launcher = Games.Launcher(GameDir);
            if (launcher == null) { Send("erreur", 0, "", true, "lanceur"); return; }
            Directory.CreateDirectory(WorkDir);
            ClearDir(WorkDir);
            Send("lancement", 0.01, "", false, null);
            DateTime t0 = DateTime.Now;
            var psi = new ProcessStartInfo(launcher, "/exportcarte " + Game.Id + " \"" + WorkDir + "\"") { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(launcher) };
            using (Process p = Process.Start(psi))
            {
                if (!p.WaitForExit(60000)) { Send("erreur", 0, "", true, "lanceur-delai"); return; }
                if (p.ExitCode != 0)
                {
                    string why = ReadKey(Path.Combine(WorkDir, "lanceur.txt"), "erreur") ?? ("code " + p.ExitCode);
                    Send("erreur", 0, "", true, why);
                    return;
                }
            }
            string etat = Path.Combine(WorkDir, "etat.txt"), last = "";
            DateTime lastChange = DateTime.Now, gameSeen = DateTime.MinValue;
            while (true)
            {
                if (Cancelled) { KillGame(t0); Send("annule", 0, "", true, "annule"); return; }
                Thread.Sleep(400);
                string txt = null;
                try { if (File.Exists(etat)) txt = File.ReadAllText(etat, Encoding.UTF8); } catch { }
                if (GameRunning(t0)) gameSeen = DateTime.Now;
                if (txt != null && txt != last)
                {
                    last = txt; lastChange = DateTime.Now;
                    var kv = Parse(txt);
                    string stage = Get(kv, "etape"), err = Get(kv, "erreur");
                    double pr; double.TryParse(Get(kv, "avance"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out pr);
                    bool done = Get(kv, "fini") == "1";
                    if (done)
                    {
                        if (err != null || !File.Exists(Path.Combine(WorkDir, "carte.json"))) { Send("erreur", pr, "", true, err ?? "carte.json"); return; }
                        Send("cache", 0.98, "", false, null);
                        Swap();
                        Send("fini", 1, "", true, null);
                        return;
                    }
                    Send(stage, pr, Get(kv, "texte"), false, null);
                }
                // jeu ferme sans fin d'export, ou plus rien depuis longtemps
                bool noGame = gameSeen != DateTime.MinValue ? (DateTime.Now - gameSeen).TotalSeconds > 15 : (DateTime.Now - t0).TotalSeconds > 90;
                if (noGame) { Send("erreur", 0, "", true, "jeu-ferme"); return; }
                if ((DateTime.Now - lastChange).TotalMinutes > 8) { KillGame(t0); Send("erreur", 0, "", true, "delai"); return; }
            }
        }

        // Le dossier de preparation devient le cache (ancien cache : fichiers retires un par un, jamais d'effacement recursif).
        void Swap()
        {
            string dst = Games.CacheDir(Game.Id);
            if (Directory.Exists(dst)) { ClearDir(dst); try { Directory.Delete(Path.Combine(dst, "textures")); } catch { } Directory.Delete(dst); }
            Directory.Move(WorkDir, dst);
        }

        static void ClearDir(string d)
        {
            string tex = Path.Combine(d, "textures");
            if (Directory.Exists(tex)) foreach (string f in Directory.GetFiles(tex)) File.Delete(f);
            foreach (string f in Directory.GetFiles(d)) File.Delete(f);
        }

        bool GameRunning(DateTime since)
        {
            foreach (Process p in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(Game.Exe)))
            {
                try { if (p.StartTime >= since.AddSeconds(-5)) return true; } catch { }
                finally { p.Dispose(); }
            }
            return false;
        }

        // Annulation : seulement le jeu demarre pour l'export (apres le debut de la preparation).
        void KillGame(DateTime since)
        {
            foreach (Process p in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(Game.Exe)))
            {
                try { if (p.StartTime >= since.AddSeconds(-5)) p.Kill(); } catch { }
                finally { p.Dispose(); }
            }
        }

        static Dictionary<string, string> Parse(string txt)
        {
            var d = new Dictionary<string, string>();
            foreach (string l in txt.Split('\n'))
            {
                int eq = l.IndexOf('=');
                if (eq > 0) d[l.Substring(0, eq).Trim()] = l.Substring(eq + 1).Trim();
            }
            return d;
        }
        static string Get(Dictionary<string, string> d, string k) { string v; return d.TryGetValue(k, out v) ? v : null; }
        static string ReadKey(string file, string key)
        {
            try { return File.Exists(file) ? Get(Parse(File.ReadAllText(file, Encoding.UTF8)), key) : null; } catch { return null; }
        }
    }
}
