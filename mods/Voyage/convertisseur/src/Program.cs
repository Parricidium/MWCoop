using System;
using System.IO;
using System.Windows.Forms;

namespace Convertisseur
{
    // Convertisseur Voyage (demande de JD, 24/10) : convertit les circuits de l'Assetto Corsa du joueur en destinations du
    // mod Voyage (My Winter Car) et les gere (liste, apercu 3D, nom, prix, point d'arrivee, reconversion, suppression).
    // Rien n'est livre ni partage : chaque joueur convertit ses propres circuits.
    // Arguments : --jeu msc|mwc, --dossier <dossier du jeu> et --langue fr|en|ru|ar (le lanceur de MWCoop, page Outils), --debug (outils de
    // WebView2) ; essais sans fenetre :
    //   --convertir <dossier du circuit> [--config <id>] --sortie <dossier des destinations> [--textures 1024] [--point pit]
    static class Program
    {
        public static bool Debug;
        public static string GameDir, Lang, Game;

        [STAThread]
        static int Main(string[] args)
        {
            string conv = null, cfg = "", outDir = null, point = "pit"; int tex = 1024;
            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i], v = i + 1 < args.Length ? args[i + 1] : null;
                if (a == "--debug") Debug = true;
                else if (a == "--dossier" && v != null) { GameDir = v; i++; }
                else if (a == "--langue" && v != null) { Lang = v; i++; }
                else if (a == "--jeu" && v != null) { Game = v == "msc" ? "msc" : "mwc"; i++; }
                else if (a == "--convertir" && v != null) { conv = v; i++; }
                else if (a == "--config" && v != null) { cfg = v; i++; }
                else if (a == "--sortie" && v != null) { outDir = v; i++; }
                else if (a == "--textures" && v != null) { int.TryParse(v, out tex); i++; }
                else if (a == "--point" && v != null) { point = v; i++; }
            }
            if (conv != null) return Cli(conv, cfg, outDir, tex, point);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
            return 0;
        }

        static int Cli(string dir, string cfg, string outDir, int tex, string point)
        {
            string log = Path.Combine(outDir ?? ".", "conversion.log");
            Directory.CreateDirectory(outDir);
            using (var w = new StreamWriter(log, false))
            {
                try
                {
                    Track t = Ac.ReadTrack(dir);
                    Layout l = t.Layouts.Find(x => x.Id == cfg) ?? t.Layouts[0];
                    var c = new Conversion();
                    c.Log = s => { w.WriteLine(s); w.Flush(); };
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    var st = c.Run(t, l, outDir, new Options { MaxTexture = tex, Arrival = point });
                    w.WriteLine("en " + sw.ElapsedMilliseconds + " ms");
                    return 0;
                }
                catch (Exception e) { w.WriteLine("ERREUR " + e); return 1; }
            }
        }
    }
}
