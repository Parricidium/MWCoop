using System;
using System.IO;

namespace MWCoop
{
    // Journal du mod : <donnees>\logs\mwcoop.log (dossier du profil pour une instance de test).
    public static class Log
    {
        static StreamWriter w;
        static readonly object gate = new object();
        public static string DataDir;

        public static void Open()
        {
            DataDir = Environment.GetEnvironmentVariable("MWCOOP_DATA");
            if (string.IsNullOrEmpty(DataDir)) DataDir = Path.Combine(Environment.CurrentDirectory, "MWCoop");
            try { w = OpenIn(DataDir); }
            catch (Exception)
            {
                // Dossier du jeu en lecture seule : %LOCALAPPDATA%\MWCoop (le chargeur y bascule aussi).
                string local = Environment.GetEnvironmentVariable("LOCALAPPDATA");
                if (string.IsNullOrEmpty(local)) return;
                DataDir = Path.Combine(local, "MWCoop");
                try { w = OpenIn(DataDir); } catch (Exception) { return; }
                Write("ATTENTION ", "dossier du jeu en lecture seule : donnees dans " + DataDir);
            }
        }

        static StreamWriter OpenIn(string data)
        {
            string dir = Path.Combine(data, "logs");
            Directory.CreateDirectory(dir);
            var sw = new StreamWriter(Path.Combine(dir, "mwcoop.log"), false, new System.Text.UTF8Encoding(false));
            sw.AutoFlush = true;
            return sw;
        }

        static void Write(string level, string msg)
        {
            lock (gate)
            {
                if (w == null) return;
                w.WriteLine(DateTime.Now.ToString("HH:mm:ss.fff") + " " + level + msg);
            }
        }

        public static void Info(string msg) { Write("", msg); }
        public static void Warn(string msg) { Write("ATTENTION ", msg); }
        public static void Error(string msg) { Write("ERREUR ", msg); }
    }
}
