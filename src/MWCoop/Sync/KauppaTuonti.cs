using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using MWCoop.Net;
using UnityEngine;

namespace MWCoop
{
    // Import d'une vraie video sur une cassette (demande de JD, 12/10) : la video (mp4, avi, wmv, mov... ce que Windows
    // sait lire) va dans <dossier du mod>\videot\importer ; cassette en main, tchat « /importer Nom.mp4 » (ou /import).
    // MWCoopRT.dll la convertit sur un fil a elle (Media Foundation : rt\mwcvideo.cpp) au format des films du camescope
    // ([Boutique] ImportLargeur x ImportHauteur, ImportImages par seconde, ImportMinutes au plus), puis elle s'ajoute aux
    // prises de la cassette comme un film filme en jeu : envoyee a tous, lue sur la TV chez tout le monde.
    public static partial class Kauppa
    {
        static string ImportDir { get { return Path.Combine(FilmDir, "importer"); } }

        [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)] static extern IntPtr LoadLibraryW(string path);
        [DllImport("kernel32", CharSet = CharSet.Ansi)] static extern IntPtr GetProcAddress(IntPtr mod, string name);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Unicode)] delegate int DVStart(string src, string dst, int w, int h, int fps, int maxSec, int quality);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Unicode)] delegate int DVPoll(float[] progress, StringBuilder msg, int msgLen);
        static DVStart vStart; static DVPoll vPoll; static bool vTried;

        static bool BindVideo()
        {
            if (vTried) return vStart != null;
            vTried = true;
            try
            {
                // (comme RayTracing : MWCoopRT.dll a cote du mwcoop.ini, chargee par son chemin)
                string own = Path.Combine(Path.GetDirectoryName(Config.IniPath), "MWCoopRT.dll");
                if (!File.Exists(own)) own = "MWCoopRT.dll";
                IntPtr h = LoadLibraryW(own);
                if (h == IntPtr.Zero) { L("video : MWCoopRT.dll pas chargee (" + Marshal.GetLastWin32Error() + ")"); return false; }
                IntPtr ps = GetProcAddress(h, "VidImportStart"), pp = GetProcAddress(h, "VidImportPoll");
                if (ps == IntPtr.Zero || pp == IntPtr.Zero) { L("video : MWCoopRT.dll sans import video (ancienne version ?)"); return false; }
                vStart = (DVStart)Marshal.GetDelegateForFunctionPointer(ps, typeof(DVStart));
                vPoll = (DVPoll)Marshal.GetDelegateForFunctionPointer(pp, typeof(DVPoll));
                return true;
            }
            catch (Exception e) { L("video : import indisponible : " + e.Message); return false; }
        }

        class Import { public string TapeId, Film, Name; public float NextPoll; public int LastPct = -1; }
        static Import import;

        static void EnsureImportDir() { try { Directory.CreateDirectory(ImportDir); } catch { } }

        // « /importer Nom.mp4 » (ou /import) : vrai si la commande est la sienne
        static bool ImportCommand(string text)
        {
            string low = text.ToLowerInvariant();
            int cut = low.StartsWith("/importer") ? 9 : low.StartsWith("/import") ? 7 : -1;
            if (cut < 0) return false;
            string arg = text.Substring(cut).Trim();
            EnsureImportDir();
            if (import != null) { Chat.System(Lang.T("Un import est déjà en cours : ", "An import is already running: ") + import.Name); return true; }
            Esine tape = esineet.Find(e => e.Kind == KVhs && e.Go != null && e.Go.transform.parent != null && Game.UnderPlayer(e.Go.transform));
            string[] files = new string[0];
            try { files = Directory.GetFiles(ImportDir); } catch { }
            if (arg.Length == 0 || tape == null)
            {
                if (tape == null) Chat.System(Lang.T("Prends d'abord une cassette VHS en main.", "Pick up a VHS tape first."));
                Chat.System(Lang.T("Mets ta vidéo dans : ", "Put your video in: ") + ImportDir);
                if (files.Length == 0) Chat.System(Lang.T("(le dossier est vide)", "(the folder is empty)"));
                else foreach (string f in files) Chat.System("  /importer " + Path.GetFileName(f));
                return true;
            }
            // nom exact, sinon le premier qui commence pareil (sans tenir compte des majuscules)
            string src = null;
            foreach (string f in files) if (string.Equals(Path.GetFileName(f), arg, StringComparison.OrdinalIgnoreCase)) src = f;
            if (src == null) foreach (string f in files) if (Path.GetFileName(f).StartsWith(arg, StringComparison.OrdinalIgnoreCase)) { src = f; break; }
            if (src == null)
            {
                Chat.System(Lang.T("Pas de fichier « ", "No file \"") + arg + Lang.T(" » dans ", "\" in ") + ImportDir);
                foreach (string f in files) Chat.System("  /importer " + Path.GetFileName(f));
                return true;
            }
            if (Clips(tape.Data).Count >= MaxClips) { Chat.System(Lang.T("Cette cassette est pleine.", "This tape is full.")); return true; }
            if (!BindVideo()) { Chat.System(Lang.T("Import impossible : MWCoopRT.dll manque ou est trop ancienne.", "Import unavailable: MWCoopRT.dll is missing or too old.")); return true; }
            string film = "f" + Session.LocalId + "-" + DateTime.Now.Ticks.ToString("x");
            try { Directory.CreateDirectory(FilmDir); } catch { }
            int w = Mathf.Clamp(Config.GetInt("Boutique", "ImportLargeur", 320), 64, 640), h = Mathf.Clamp(Config.GetInt("Boutique", "ImportHauteur", 240), 48, 480);
            int fps = Mathf.Clamp(Config.GetInt("Boutique", "ImportImages", 12), 1, 25);
            int maxSec = Mathf.RoundToInt(Mathf.Clamp(Config.GetFloat("Boutique", "ImportMinutes", 20f), 0.2f, 120f) * 60f);
            int r = vStart(src, FilmPath(film), w, h, fps, maxSec, Mathf.Clamp(Config.GetInt("Boutique", "ImportQualite", 50), 10, 95));
            if (r != 0) { Chat.System(Lang.T("Import pas lancé (", "Import not started (") + r + ")"); return true; }
            import = new Import { TapeId = tape.Id, Film = film, Name = Path.GetFileName(src) };
            Chat.System(Lang.T("Conversion de ", "Converting ") + import.Name + Lang.T(" pour la cassette… (tu peux continuer à jouer)", " for the tape... (you can keep playing)"));
            L("video : import de " + src + " -> " + film + " (" + w + "x" + h + ", " + fps + " i/s, " + maxSec + " s au plus)");
            return true;
        }

        static void TickImport(float now)
        {
            if (import == null || vPoll == null || now < import.NextPoll) return;
            import.NextPoll = now + 0.5f;
            var p = new float[1]; var sb = new StringBuilder(512);
            int st = vPoll(p, sb, 512);
            if (st == 1)
            {
                int pct = Mathf.FloorToInt(p[0] * 10f) * 10;
                if (pct != import.LastPct && pct > 0) { import.LastPct = pct; Hud.Toast(Lang.T("Import de la vidéo : ", "Video import: ") + pct + " %"); }
                return;
            }
            Import im = import;
            import = null;
            if (st != 2)
            {
                Chat.System(Lang.T("Import raté : ", "Import failed: ") + im.Name + " (" + sb + ")");
                L("video : import de " + im.Name + " rate : " + sb);
                return;
            }
            L("video : import de " + im.Name + " fini : " + sb);
            Esine tape = FindItem(im.TapeId);
            if (tape == null)
            {
                // (la cassette a ete inseree quelque part entre-temps : on cherche le lecteur qui la porte)
                Chat.System(Lang.T("Vidéo convertie, mais la cassette n'est plus là : reprends-la en main et relance /importer.", "Video converted, but the tape is gone: pick it up again and run /import again."));
                return;
            }
            string content = tape.Data, films = TapeFilm(content), title = TapeTitle(content);
            if (title.Length == 0)
            {
                title = Path.GetFileNameWithoutExtension(im.Name);
                foreach (char c in new[] { '|', '~', ';', '+', '\n', '\r' }) title = title.Replace(c, ' ');
                if (title.Length > 24) title = title.Substring(0, 24).Trim();
            }
            SetData(tape, (films.Length > 0 ? films + "+" : "") + im.Film + "~" + title);
            SendFilm(im.Film);
            Chat.System(Lang.T("Vidéo ajoutée sur la cassette « ", "Video added to the tape \"") + title + Lang.T(" » : ", "\": ") + sb);
        }
    }
}
