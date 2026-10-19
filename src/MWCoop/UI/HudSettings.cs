using System;
using System.Collections.Generic;
using System.IO;

namespace MWCoop
{
    // Reglages du HUD (UI/NewHud.cs), section [Hud] de mwcoop.ini : ecrits par le menu F10 (onglet HUD) et par le lanceur
    // (Reglages > HUD, memes cles et memes valeurs : launcher/gfx.inc, lignes de groupe 16 a 19). Le fichier est relu des
    // qu'il change (le lanceur reste ouvert pendant la partie : un changement la-bas s'applique tout de suite en jeu).
    public static class HudSettings
    {
        public const string Sec = "Hud";
        public const int Vanilla = 0, Carte = 1, Anneaux = 2, Bandeau = 3;
        // Groupes (onglet HUD du F10, cartes du lanceur) : 0 style, 1 personnaliser, 2 jauges affichees, 3 coop.
        public class Opt { public string Key, Fr, En, HelpFr, HelpEn; public int Def, Group; public int[] Vals; public string[] Lf, Le; }
        static Opt O(string k, string fr, string en, int def, int g, int[] vals = null, string[] lf = null, string[] le = null, string hf = null, string he = null)
        { return new Opt { Key = k, Fr = fr, En = en, Def = def, Group = g, Vals = vals, Lf = lf, Le = le ?? lf, HelpFr = hf, HelpEn = he }; }

        public static readonly Opt[] Opts = {
            O("Style", "Style du HUD", "HUD style", 0, 0, new[] { 0, 1, 2, 3 }, new[] { "Vanilla", "Carte", "Anneaux", "Bandeau" }, new[] { "Vanilla", "Card", "Rings", "Strip" },
              "Vanilla : le HUD d'origine du jeu", "Vanilla: the game's own HUD"),
            O("Taille", "Taille", "Size", 100, 1, new[] { 70, 80, 90, 100, 110, 125, 150 }, new[] { "70 %", "80 %", "90 %", "100 %", "110 %", "125 %", "150 %" }),
            O("Position", "Position", "Position", -1, 1, new[] { -1, 0, 1, 2, 3, 4, 5 },
              new[] { "Auto", "Haut gauche", "Haut droite", "Bas gauche", "Bas droite", "Haut centre", "Bas centre" },
              new[] { "Auto", "Top left", "Top right", "Bottom left", "Bottom right", "Top centre", "Bottom centre" },
              "Auto : la place prévue pour ce style", "Auto: the usual place for this style"),
            O("Couleur", "Couleur", "Colour", 0, 1, new[] { 0, 1, 2, 3 }, new[] { "Glace", "Bleu ciel", "Aurore", "Lilas" }, new[] { "Ice", "Sky blue", "Aurora", "Lilac" }),
            O("Fond", "Opacité du fond", "Background opacity", 60, 1, new[] { 0, 30, 45, 60, 75, 90 }, new[] { "Aucun", "30 %", "45 %", "60 %", "75 %", "90 %" }, new[] { "None", "30 %", "45 %", "60 %", "75 %", "90 %" }),
            O("Estomper", "Estomper les jauges au repos", "Fade gauges at rest", 1, 1, null, null, null,
              "Presque vides, elles s'effacent (Anneaux : elles disparaissent)", "Nearly empty, they fade out (Rings: they disappear)"),
            O("Valeurs", "Chiffres et noms", "Numbers and names", 0, 1, null, null, null,
              "Carte : la valeur de chaque jauge · Anneaux et Bandeau : les noms toujours visibles", "Card: each gauge's value · Rings and Strip: names always shown"),
            O("JaugeSoif", "Soif / problème", "Thirst / problem", 1, 2),
            O("JaugeFaim", "Faim", "Hunger", 1, 2),
            O("JaugeStress", "Stress", "Stress", 1, 2),
            O("JaugeUrine", "Urine", "Urine", 1, 2),
            O("JaugeFatigue", "Fatigue", "Fatigue", 1, 2),
            O("JaugeSalete", "Saleté", "Dirtiness", 0, 2),
            O("JaugeTemperature", "Température", "Body temperature", 1, 2),
            O("JaugeSueur", "Sueur", "Sweat", 1, 2),
            O("JaugeArgent", "Argent", "Money", 1, 2),
            O("JaugeJour", "Jour", "Day", 1, 2),
            O("Equipe", "Panneau Équipe", "Team panel", 1, 3, null, null, null,
              "Les jauges des autres joueurs, en petit (aussi avec Vanilla)", "The other players' gauges, small (also with Vanilla)"),
            O("Alertes", "Alertes en haut de l'écran", "Alerts at the top of the screen", 1, 3, null, null, null,
              "Très faim, épuisé, gelé… pour vous et vos amis", "Very hungry, exhausted, freezing… for you and your friends"),
            // Menus du mod (F10 et petits panneaux, refonte du 18/10) : taille et place, reglees dans F10 > Graphismes.
            O("Menu", "Taille du menu", "Menu size", 100, 9, new[] { 85, 100, 120 }, new[] { "Petit", "Normal", "Grand" }, new[] { "Small", "Normal", "Large" },
              "Le menu F10 et les petits panneaux (triches, porte-monnaie, tchat)", "The F10 menu and the small panels (cheats, wallet, chat)"),
            O("MenuPosition", "Position du menu", "Menu position", 0, 9, new[] { 0, 1 }, new[] { "Centre", "Gauche" }, new[] { "Centre", "Left" },
              "Gauche : le jeu reste visible à droite", "Left: the game stays visible on the right"),
        };

        static readonly Dictionary<string, Opt> byKey = new Dictionary<string, Opt>(StringComparer.OrdinalIgnoreCase);
        static readonly Dictionary<string, int> vals = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        static DateTime iniTime;
        static float nextCheck = -1;

        public static Opt Find(string key)
        {
            if (byKey.Count == 0) foreach (Opt o in Opts) byKey[o.Key] = o;
            Opt r;
            return byKey.TryGetValue(key, out r) ? r : null;
        }

        public static int Get(string key)
        {
            Reload(false);
            int v;
            if (vals.TryGetValue(key, out v)) return v;
            Opt o = Find(key);
            return o != null ? o.Def : 0;
        }

        // (My Summer Car : ni temperature du corps ni sueur dans ce jeu -- JD, 19/10 : jauges toujours coupees)
        public static bool On(string key) { if (Game.Msc && (key == "JaugeTemperature" || key == "JaugeSueur")) return false; return Get(key) != 0; }
        public static int StyleNow { get { return Get("Style"); } }

        // Pose (F10) : ecrite tout de suite dans mwcoop.ini.
        public static void Set(string key, int v)
        {
            vals[key] = v;
            Config.Save(Sec, key, v.ToString());
            try { iniTime = File.GetLastWriteTimeUtc(Config.IniPath); } catch { }
            Log.Info("hud : " + key + " = " + v);
        }

        // Tout sauf le style : valeurs par defaut (cles videes dans le .ini).
        public static void ResetCustom()
        {
            foreach (Opt o in Opts)
            {
                if (o.Group < 1 || o.Group > 3 || !vals.ContainsKey(o.Key)) continue;   // (style et menus du mod gardes)
                vals.Remove(o.Key);
                Config.Save(Sec, o.Key, "");
            }
            try { iniTime = File.GetLastWriteTimeUtc(Config.IniPath); } catch { }
            Log.Info("hud : personnalisation remise par defaut");
        }

        // Relecture de la section [Hud] quand le fichier a change (au plus une fois par seconde).
        static void Reload(bool force)
        {
            float now = UnityEngine.Time.realtimeSinceStartup;
            if (!force && now < nextCheck) return;
            nextCheck = now + 1f;
            if (string.IsNullOrEmpty(Config.IniPath)) return;
            DateTime t;
            try { t = File.GetLastWriteTimeUtc(Config.IniPath); } catch { return; }
            if (!force && t == iniTime) return;
            iniTime = t;
            string[] lines;
            try { lines = File.ReadAllLines(Config.IniPath); } catch { return; }
            vals.Clear();
            bool inSec = false;
            foreach (string raw in lines)
            {
                string l = raw.Trim();
                if (l.StartsWith("[")) { inSec = string.Equals(l.Trim('[', ']'), Sec, StringComparison.OrdinalIgnoreCase); continue; }
                if (!inSec || l.StartsWith(";")) continue;
                int eq = l.IndexOf('=');
                if (eq <= 0) continue;
                int v;
                if (int.TryParse(l.Substring(eq + 1).Trim(), out v)) vals[l.Substring(0, eq).Trim()] = v;
            }
        }
    }
}
