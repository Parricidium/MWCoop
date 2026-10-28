using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace Showroom
{
    // Essais caches (variable d'environnement SHOWROOM_ESSAI) : « secondes:commande;... » comptees depuis le chargement --
    // touche (entrer / sortir), bouton=<spot0..4|tout|prec|suiv|sortir>, tourne=<cap du joueur>, capture=<nom> (Mods\showroom-<nom>.png).
    static class Essai
    {
        static readonly List<KeyValuePair<float, string>> todo = new List<KeyValuePair<float, string>>();
        static float t0;
        static string due;

        public static void Start(Pilote p)
        {
            todo.Clear();
            string e = Environment.GetEnvironmentVariable("SHOWROOM_ESSAI");
            if (string.IsNullOrEmpty(e)) return;
            t0 = Time.realtimeSinceStartup;
            foreach (string c in e.Split(';'))
            {
                int k = c.IndexOf(':'); float t;
                if (k > 0 && float.TryParse(c.Substring(0, k), NumberStyles.Float, CultureInfo.InvariantCulture, out t)) todo.Add(new KeyValuePair<float, string>(t, c.Substring(k + 1).Trim()));
            }
            Journal.Info("essai : " + todo.Count + " commande(s)");
        }

        // une commande arrivee a son heure (une par image)
        public static void Tick()
        {
            due = null;
            if (todo.Count == 0 || Time.realtimeSinceStartup - t0 < todo[0].Key) return;
            string c = todo[0].Value; todo.RemoveAt(0);
            Journal.Info("essai : " + c);
            if (c.StartsWith("capture="))
            {
                string f = Path.Combine(Path.GetDirectoryName(Application.dataPath), Path.Combine("Mods", "showroom-" + c.Substring(8) + ".png"));
                Application.CaptureScreenshot(f);
                Journal.Info("essai : capture " + f);
            }
            else if (c.StartsWith("tourne="))
            {
                float y;
                if (Jeu.Player != null && float.TryParse(c.Substring(7), NumberStyles.Float, CultureInfo.InvariantCulture, out y)) Jeu.Player.rotation = Quaternion.Euler(0f, y, 0f);
            }
            else due = c;
        }

        public static bool Take(string what) { if (due != what) return false; due = null; return true; }
        public static string TakeButton() { if (due == null || !due.StartsWith("bouton=")) return null; string b = due.Substring(7); due = null; return b; }
    }
}
