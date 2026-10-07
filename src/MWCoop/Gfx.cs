using System;
using System.Collections.Generic;
using System.IO;
using HutongGames.PlayMaker;
using UnityEngine;

namespace MWCoop
{
    // Graphismes (demande de JD, 07/10 : comme les GTA, les reglages dans le lanceur et le menu F10). [Graphismes] de
    // mwcoop.ini, valeurs entieres ; une cle absente (ou vide) laisse le jeu decider.
    //  - Options du jeu (son menu, automate Systems/OptionsDB::GFX, sauvees dans options.txt) : posees dans ses variables,
    //    puis son etat "Save data" (sauve, relit et applique tout, comme son menu). Changees dans son menu : recopiees dans
    //    mwcoop.ini (le lanceur les retrouve).
    //  - En plus (reglages du moteur, que le jeu ne propose pas) : limite d'images, synchro verticale, distance et finesse
    //    des ombres, niveau de detail, textures, filtrage des textures. Au chargement de chaque niveau.
    //  - mwcoop.ini relu des qu'il change (lanceur ouvert pendant la partie, menu F10) : applique tout de suite.
    // Rien n'est envoye aux autres : chacun son affichage.
    public static class Gfx
    {
        public enum Kind { Bool, Int, Float }
        public class GameOpt { public string Key; public string[] Vars; public Kind K; }

        // Options du jeu : cle de mwcoop.ini -> variable(s) de Systems/OptionsDB::GFX.
        public static readonly GameOpt[] GameOpts = {
            new GameOpt { Key = "Anticrenelage", Vars = new[] { "Antialiasing" }, K = Kind.Bool },
            new GameOpt { Key = "HDR", Vars = new[] { "HDR" }, K = Kind.Bool },
            new GameOpt { Key = "Bloom", Vars = new[] { "Bloom" }, K = Kind.Bool },
            new GameOpt { Key = "Contraste", Vars = new[] { "Contrast" }, K = Kind.Bool },
            new GameOpt { Key = "RayonsSoleil", Vars = new[] { "Sunshafts" }, K = Kind.Bool },
            new GameOpt { Key = "ChampVision", Vars = new[] { "FOV" }, K = Kind.Float },
            new GameOpt { Key = "OmbresSoleil", Vars = new[] { "ShadowsSun" }, K = Kind.Bool },
            new GameOpt { Key = "OmbresLune", Vars = new[] { "ShadowsMoon" }, K = Kind.Bool },
            new GameOpt { Key = "OmbresMaisons", Vars = new[] { "ShadowsHouse" }, K = Kind.Bool },
            new GameOpt { Key = "Distance", Vars = new[] { "DrawDistance" }, K = Kind.Float },
            new GameOpt { Key = "Retroviseurs", Vars = new[] { "MirrorLeft", "MirrorRight", "MirrorRear" }, K = Kind.Int },
            new GameOpt { Key = "Balancement", Vars = new[] { "HeadBob" }, K = Kind.Bool },
            new GameOpt { Key = "BalancementVoiture", Vars = new[] { "HeadBobDrive" }, K = Kind.Int },
            new GameOpt { Key = "CompteurImages", Vars = new[] { "FPSIndicator" }, K = Kind.Bool },
            new GameOpt { Key = "IndicateurRapport", Vars = new[] { "GearIndicator" }, K = Kind.Bool },
            new GameOpt { Key = "PoussiereTrafic", Vars = new[] { "AIDustClouds" }, K = Kind.Bool },
            new GameOpt { Key = "TracesTrafic", Vars = new[] { "AISkidmarks" }, K = Kind.Bool },
        };
        // Reglages du moteur en plus.
        public static readonly string[] EngineKeys = { "ImagesMax", "SyncVerticale", "OmbresDistance", "OmbresCascades", "Details", "Textures", "Filtrage" };

        static readonly Dictionary<string, int> ini = new Dictionary<string, int>();   // [Graphismes] lu
        static readonly Dictionary<string, int> applied = new Dictionary<string, int>(); // options du jeu posees / lues
        static DateTime iniTime;
        static float nextCheck, gfxAt = -1;
        static PlayMakerFSM gfx;
        static bool gameApplied;
        // Reglages du moteur du niveau d'origine (choisi dans la fenetre Unity) : rendus quand une cle est retiree.
        static float baseShadow = -1, baseLod; static int baseCascades, baseTex, baseVsync, baseFps; static AnisotropicFiltering baseAniso;

        public static void OnLevelLoaded()
        {
            gfx = null; gameApplied = false; applied.Clear();
            gfxAt = Application.loadedLevelName == "GAME" ? Time.realtimeSinceStartup + 3f : -1;
            if (baseShadow < 0)
            {
                baseShadow = QualitySettings.shadowDistance; baseLod = QualitySettings.lodBias; baseCascades = QualitySettings.shadowCascades;
                baseTex = QualitySettings.masterTextureLimit; baseVsync = QualitySettings.vSyncCount; baseFps = Application.targetFrameRate; baseAniso = QualitySettings.anisotropicFiltering;
            }
            ReadIni(true);
            ApplyEngine();
        }

        public static void Update()
        {
            float now = Time.realtimeSinceStartup;
            if (now < nextCheck) return;
            nextCheck = now + 1f;
            if (ReadIni(false)) { ApplyEngine(); if (gameApplied) ApplyGame(false); }
            if (gfxAt > 0 && now >= gfxAt && gfx == null)
            {
                GameObject db = Game.FindAny("Systems/OptionsDB");
                gfx = db != null ? Game.FsmOn(db, "GFX") : null;
                if (gfx == null) { gfxAt = now + 2f; return; }
            }
            if (gfx == null) return;
            if (!gameApplied)
            {
                if (gfx.ActiveStateName != "State 1") return;   // (le jeu finit de charger options.txt)
                gameApplied = true;
                ApplyGame(true);
                return;
            }
            WatchGameMenu();
        }

        // [Graphismes] de mwcoop.ini (relu s'il a change, ou force). Vrai si une valeur a change.
        static bool ReadIni(bool force)
        {
            string path = Config.IniPath;
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return false;
            DateTime t;
            try { t = File.GetLastWriteTimeUtc(path); } catch { return false; }
            if (!force && t == iniTime) return false;
            iniTime = t;
            var fresh = new Dictionary<string, int>();
            try
            {
                bool inSec = false;
                foreach (string raw in File.ReadAllLines(path))
                {
                    string l = raw.Trim();
                    if (l.StartsWith("[")) { inSec = l.Trim('[', ']').Equals("Graphismes", StringComparison.OrdinalIgnoreCase); continue; }
                    int eq = l.IndexOf('=');
                    int v;
                    if (inSec && eq > 0 && int.TryParse(l.Substring(eq + 1).Trim(), out v)) fresh[l.Substring(0, eq).Trim()] = v;
                }
            }
            catch { return false; }
            bool changed = fresh.Count != ini.Count;
            foreach (KeyValuePair<string, int> kv in fresh) { int o; if (!ini.TryGetValue(kv.Key, out o) || o != kv.Value) changed = true; }
            ini.Clear();
            foreach (KeyValuePair<string, int> kv in fresh) ini[kv.Key] = kv.Value;
            return changed;
        }

        static bool Has(string k, out int v) { return ini.TryGetValue(k, out v); }

        // Moteur : chaque cle presente posee, absente rendue au niveau d'origine.
        static void ApplyEngine()
        {
            int v;
            QualitySettings.vSyncCount = Has("SyncVerticale", out v) ? (v != 0 ? 1 : 0) : baseVsync;
            Application.targetFrameRate = Has("ImagesMax", out v) ? (v <= 0 ? -1 : v) : baseFps;
            QualitySettings.shadowDistance = Has("OmbresDistance", out v) ? Mathf.Clamp(v, 10, 1000) : baseShadow;
            QualitySettings.shadowCascades = Has("OmbresCascades", out v) ? (v >= 4 ? 4 : v >= 2 ? 2 : 1) : baseCascades;
            QualitySettings.lodBias = Has("Details", out v) ? Mathf.Clamp(v, 30, 500) / 100f : baseLod;
            QualitySettings.masterTextureLimit = Has("Textures", out v) ? Mathf.Clamp(v, 0, 3) : baseTex;
            QualitySettings.anisotropicFiltering = Has("Filtrage", out v) ? (v >= 2 ? AnisotropicFiltering.ForceEnable : v == 1 ? AnisotropicFiltering.Enable : AnisotropicFiltering.Disable) : baseAniso;
        }

        // Options du jeu : variables de GFX posees, puis "Save data" (sauve dans options.txt, relit et applique).
        static void ApplyGame(bool first)
        {
            if (gfx == null) return;
            int n = 0;
            var absent = new List<KeyValuePair<string, int>>();
            foreach (GameOpt o in GameOpts)
            {
                int v;
                if (!Has(o.Key, out v)) { applied[o.Key] = Read(o); absent.Add(new KeyValuePair<string, int>(o.Key, applied[o.Key])); continue; }
                if (Read(o) != v) { Write(o, v); n++; }
                applied[o.Key] = v;
            }
            // (premier chargement : les valeurs du jeu recopiees, le lanceur les montre)
            if (first && absent.Count > 0) SaveMany(absent);
            if (n > 0) Game.SetState(gfx, "Save data");
            if (n > 0 || first) Log.Info("graphismes : " + n + " option(s) du jeu posee(s) d'apres mwcoop.ini" + (first ? " (chargement)" : "") + " ; moteur : ombres " + QualitySettings.shadowDistance + " m x" + QualitySettings.shadowCascades
                                         + ", details " + QualitySettings.lodBias + ", textures 1/" + (1 << QualitySettings.masterTextureLimit) + ", filtrage " + QualitySettings.anisotropicFiltering
                                         + ", images max " + Application.targetFrameRate + ", synchro " + QualitySettings.vSyncCount);
        }

        static int Read(GameOpt o)
        {
            NamedVariable nv = Var(o.Vars[0]);
            if (nv is FsmBool) return ((FsmBool)nv).Value ? 1 : 0;
            if (nv is FsmInt) return ((FsmInt)nv).Value;
            if (nv is FsmFloat) return Mathf.RoundToInt(((FsmFloat)nv).Value);
            return 0;
        }

        static void Write(GameOpt o, int v)
        {
            foreach (string name in o.Vars)
            {
                NamedVariable nv = Var(name);
                if (nv is FsmBool) ((FsmBool)nv).Value = v != 0;
                else if (nv is FsmInt) ((FsmInt)nv).Value = v;
                else if (nv is FsmFloat) ((FsmFloat)nv).Value = v;
            }
        }

        static NamedVariable Var(string name)
        {
            FsmVariables vs = gfx.FsmVariables;
            return (NamedVariable)vs.FindFsmBool(name) ?? (NamedVariable)vs.FindFsmInt(name) ?? vs.FindFsmFloat(name);
        }

        // Change dans le menu du jeu (ou par F3 pour les retroviseurs) : recopie dans mwcoop.ini.
        static void WatchGameMenu()
        {
            if (gfx.ActiveStateName != "State 1") return;   // (le jeu applique)
            var changed = new List<KeyValuePair<string, int>>();
            foreach (GameOpt o in GameOpts)
            {
                int now = Read(o), was;
                if (applied.TryGetValue(o.Key, out was) && was == now) continue;
                applied[o.Key] = now;
                changed.Add(new KeyValuePair<string, int>(o.Key, now));
            }
            if (changed.Count == 0) return;
            SaveMany(changed);
            Log.Info("graphismes : " + changed[0].Key + " = " + changed[0].Value + (changed.Count > 1 ? " (+" + (changed.Count - 1) + ")" : "") + " change dans le menu du jeu, recopie dans mwcoop.ini");
        }

        static void SaveMany(List<KeyValuePair<string, int>> kvs)
        {
            foreach (KeyValuePair<string, int> kv in kvs) { Config.Save("Graphismes", kv.Key, kv.Value.ToString()); ini[kv.Key] = kv.Value; }
            try { iniTime = File.GetLastWriteTimeUtc(Config.IniPath); } catch { }
        }

        static void SaveKey(string key, int v)
        {
            Config.Save("Graphismes", key, v.ToString());
            ini[key] = v;
            try { iniTime = File.GetLastWriteTimeUtc(Config.IniPath); } catch { }
        }

        // ---- pour le menu F10
        public static int Get(string key, int def) { int v; return ini.TryGetValue(key, out v) ? v : GameValue(key, def); }
        public static bool IsSet(string key) { return ini.ContainsKey(key); }

        static int GameValue(string key, int def)
        {
            if (gfx == null) return def;
            foreach (GameOpt o in GameOpts) if (o.Key == key) return Read(o);
            return def;
        }

        // Pose des valeurs (F10) : mwcoop.ini, puis appliquees tout de suite (-1 : rendue au jeu).
        public static void Set(string key, int v) { SetMany(new List<KeyValuePair<string, int>> { new KeyValuePair<string, int>(key, v) }); }
        public static void SetMany(List<KeyValuePair<string, int>> kvs)
        {
            var keep = new List<KeyValuePair<string, int>>();
            foreach (KeyValuePair<string, int> kv in kvs)
            {
                if (kv.Value != -1) { keep.Add(kv); continue; }
                Config.Save("Graphismes", kv.Key, "");
                ini.Remove(kv.Key);
            }
            if (keep.Count > 0) SaveMany(keep);
            try { iniTime = File.GetLastWriteTimeUtc(Config.IniPath); } catch { }
            ApplyEngine();
            if (gameApplied) ApplyGame(false);
        }

        // ---- lignes du menu F10 (les memes que la page du lanceur, launcher/gfx.inc)
        public class Row { public string Key, Fr, En; public int Def; public int[] Vals; public string[] Lf, Le; public int Group; public bool Heavy; }
        static Row R(string k, string fr, string en, int def, int group, bool heavy = false) { return new Row { Key = k, Fr = fr, En = en, Def = def, Group = group, Heavy = heavy }; }
        static Row S(string k, string fr, string en, int def, int group, int[] vals, string[] lf, string[] le, bool heavy = false)
        { return new Row { Key = k, Fr = fr, En = en, Def = def, Group = group, Vals = vals, Lf = lf, Le = le ?? lf, Heavy = heavy }; }
        static readonly string[] fov = { "50\u00B0", "55\u00B0", "60\u00B0", "65\u00B0", "70\u00B0", "75\u00B0", "80\u00B0", "85\u00B0", "90\u00B0" };
        public static readonly Row[] Rows = {
            R("Anticrenelage", "Anticr\u00E9nelage", "Anti-aliasing", 1, 0),
            R("HDR", "HDR (lumi\u00E8re riche)", "HDR (rich light)", 1, 0),
            R("Bloom", "Halo lumineux (bloom)", "Bloom", 0, 0),
            R("Contraste", "Contraste renforc\u00E9", "Contrast boost", 0, 0),
            R("RayonsSoleil", "Rayons du soleil", "Sun shafts", 0, 0),
            S("ChampVision", "Champ de vision", "Field of view", 60, 0, new[] { 50, 55, 60, 65, 70, 75, 80, 85, 90 }, fov, null),
            S("ImagesMax", "Images par seconde max.", "Max frames per second", -1, 0, new[] { -1, 30, 60, 75, 120, 144, 165, 240, 0 },
              new[] { "Jeu", "30", "60", "75", "120", "144", "165", "240", "Illimit\u00E9es" }, new[] { "Game", "30", "60", "75", "120", "144", "165", "240", "Unlimited" }),
            S("SyncVerticale", "Synchro verticale", "V-sync", -1, 0, new[] { -1, 0, 1 }, new[] { "Jeu", "Non", "Oui" }, new[] { "Game", "Off", "On" }),
            R("OmbresSoleil", "Ombres du soleil", "Sun shadows", 1, 1, true),
            R("OmbresLune", "Ombres de la lune", "Moon shadows", 0, 1),
            R("OmbresMaisons", "Ombres des maisons", "House shadows", 1, 1),
            S("OmbresDistance", "Distance des ombres", "Shadow distance", -1, 1, new[] { -1, 30, 60, 100, 150, 250, 400 },
              new[] { "Jeu", "30 m", "60 m", "100 m", "150 m", "250 m", "400 m" }, new[] { "Game", "30 m", "60 m", "100 m", "150 m", "250 m", "400 m" }, true),
            S("OmbresCascades", "Finesse des ombres", "Shadow detail", -1, 1, new[] { -1, 1, 2, 4 }, new[] { "Jeu", "Simple", "Fine", "Tr\u00E8s fine" }, new[] { "Game", "Basic", "Fine", "Very fine" }),
            S("Distance", "Distance d'affichage", "Draw distance", 3000, 2, new[] { 500, 1000, 1500, 2000, 3000, 4500, 6000, 9000 },
              new[] { "500 m", "1 km", "1,5 km", "2 km", "3 km", "4,5 km", "6 km", "9 km" }, new[] { "500 m", "1 km", "1.5 km", "2 km", "3 km", "4.5 km", "6 km", "9 km" }, true),
            S("Details", "Niveau de d\u00E9tail", "Level of detail", -1, 2, new[] { -1, 70, 100, 150, 200, 300 },
              new[] { "Jeu", "Bas", "Normal", "\u00C9lev\u00E9", "Tr\u00E8s \u00E9lev\u00E9", "Max" }, new[] { "Game", "Low", "Normal", "High", "Very high", "Max" }),
            S("Textures", "Textures", "Textures", -1, 2, new[] { -1, 2, 1, 0 }, new[] { "Jeu", "Quart", "Moiti\u00E9", "Pleines" }, new[] { "Game", "Quarter", "Half", "Full" }),
            S("Filtrage", "Filtrage des textures", "Texture filtering", -1, 2, new[] { -1, 0, 1, 2 }, new[] { "Jeu", "Non", "Oui", "Forc\u00E9" }, new[] { "Game", "Off", "On", "Forced" }),
            S("Retroviseurs", "R\u00E9troviseurs", "Mirrors", 1, 3, new[] { 0, 1, 2 }, new[] { "Coup\u00E9s", "Simples", "Complets" }, new[] { "Off", "Simple", "Full" }),
            R("Balancement", "T\u00EAte qui balance \u00E0 pied", "Head bob on foot", 1, 3),
            S("BalancementVoiture", "T\u00EAte qui balance en voiture", "Head bob in cars", 2, 3, new[] { 0, 1, 2 }, new[] { "Non", "50 %", "Oui" }, new[] { "Off", "50 %", "On" }),
            R("CompteurImages", "Compteur d'images", "FPS counter", 0, 3),
            R("IndicateurRapport", "Rapport engag\u00E9 \u00E0 l'\u00E9cran", "Gear on screen", 1, 3),
            R("PoussiereTrafic", "Poussi\u00E8re de la circulation", "Traffic dust", 1, 3),
            R("TracesTrafic", "Traces de pneus de la circulation", "Traffic skid marks", 0, 3),
        };

        public class Preset { public string Fr, En; public int[] V; }
        // (meme ordre que PresetKeys ; comme dans le lanceur)
        public static readonly string[] PresetKeys = { "Anticrenelage", "HDR", "Bloom", "RayonsSoleil", "OmbresSoleil", "OmbresLune", "OmbresMaisons", "OmbresDistance", "OmbresCascades",
                                                       "Distance", "Details", "Textures", "Filtrage", "Retroviseurs", "PoussiereTrafic", "TracesTrafic" };
        public static readonly Preset[] Presets = {
            new Preset { Fr = "Performance", En = "Performance", V = new[] { 0, 0, 0, 0, 1, 0, 0, 30, 1, 1500, 70, 1, 0, 1, 0, 0 } },
            new Preset { Fr = "\u00C9quilibr\u00E9", En = "Balanced", V = new[] { 1, 1, 0, 0, 1, 0, 1, 60, 2, 3000, 100, 0, 1, 1, 1, 0 } },
            new Preset { Fr = "Beau", En = "Pretty", V = new[] { 1, 1, 1, 1, 1, 1, 1, 150, 2, 4500, 150, 0, 1, 2, 1, 1 } },
            new Preset { Fr = "Ultra", En = "Ultra", V = new[] { 1, 1, 1, 1, 1, 1, 1, 400, 4, 9000, 300, 0, 2, 2, 1, 1 } },
        };

        public static int Value(Row r) { int v; return ini.TryGetValue(r.Key, out v) ? v : r.Vals == null || r.Def != -1 ? GameValue(r.Key, r.Def) : -1; }

        public static int PresetOn()
        {
            for (int i = 0; i < Presets.Length; i++)
            {
                bool all = true;
                for (int k = 0; k < PresetKeys.Length && all; k++) { int v; if (!ini.TryGetValue(PresetKeys[k], out v) || v != Presets[i].V[k]) all = false; }
                if (all) return i;
            }
            return -1;
        }

        public static void ApplyPreset(int i)
        {
            var kvs = new List<KeyValuePair<string, int>>();
            for (int k = 0; k < PresetKeys.Length; k++) kvs.Add(new KeyValuePair<string, int>(PresetKeys[k], Presets[i].V[k]));
            SetMany(kvs);
            Log.Info("graphismes : prereglage " + Presets[i].Fr + " (menu F10)");
        }

        // Rend une cle au jeu (F10 : « Jeu »).
        public static void Clear(string key)
        {
            Config.Save("Graphismes", key, "");
            ini.Remove(key);
            try { iniTime = File.GetLastWriteTimeUtc(Config.IniPath); } catch { }
            ApplyEngine();
        }

        // Essais : etat courant.
        public static string Describe()
        {
            var sb = new System.Text.StringBuilder("ini :");
            foreach (KeyValuePair<string, int> kv in ini) sb.Append(' ').Append(kv.Key).Append('=').Append(kv.Value);
            sb.Append(" ; jeu :");
            if (gfx != null) foreach (GameOpt o in GameOpts) sb.Append(' ').Append(o.Key).Append('=').Append(Read(o));
            else sb.Append(" (pas d'automate GFX)");
            sb.Append(" ; moteur : ombres ").Append(QualitySettings.shadowDistance).Append(" x").Append(QualitySettings.shadowCascades).Append(", details ").Append(QualitySettings.lodBias)
              .Append(", textures ").Append(QualitySettings.masterTextureLimit).Append(", filtrage ").Append(QualitySettings.anisotropicFiltering).Append(", images ").Append(Application.targetFrameRate)
              .Append(", synchro ").Append(QualitySettings.vSyncCount);
            Camera c = Camera.main;
            if (c != null) sb.Append(", camera loin ").Append(c.farClipPlane).Append(" fov ").Append(c.fieldOfView.ToString("F0"));
            return sb.ToString();
        }
    }
}
