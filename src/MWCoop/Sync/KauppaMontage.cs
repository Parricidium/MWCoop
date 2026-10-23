using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using MWCoop.Net;
using UnityEngine;

namespace MWCoop
{
    // Montage d'une cassette (demande de JD, 13/10) : R en visant une cassette posee ou en la tenant en main.
    // Panneau (souris libre, joueur immobile : Menu le compte parmi ses panneaux) :
    //  - titre modifiable ; prises et durees ; petit ecran de lecture avec le son (lecture / pause, barre de temps ou l'on
    //    clique pour se deplacer) ; deux reperes DEBUT et FIN (la selection) ;
    //  - ROGNER : ne garder que la selection (un seul film, envoye a tous ; les prochains enregistrements s'ajoutent a la
    //    suite) ; EFFACER : cassette vierge (titre garde) ; EXPORTER : la selection en .mp4 (H.264 + AAC, image x3) dans
    //    le dossier choisi ([Boutique] DossierVideos, par defaut Videos\CoopVideos ; « Changer » ouvre la fenetre de
    //    Windows), en tache de fond (MWCoopRT.dll : VidExportStart, suivi comme l'import), logo du mod en haut a gauche,
    //    semi-transparent ([Boutique] ExportLogo=0 : sans).
    public static partial class Kauppa
    {
        public static bool MontageOpen;
        static Esine mTape;
        static readonly List<Film> mClips = new List<Film>();
        static bool mMissing;                 // une prise pas encore recue
        static float mTotal, mT, mA, mB;     // secondes sur la cassette entiere
        static bool mPlaying;
        static int mClip = -1, mFrame = -1;
        static Texture2D mTex;
        static GameObject mAudioGo;
        static AudioSource mAudio;
        static string mTitle = "", mNote; static float mNoteUntil; static bool mNoteBad;
        static int mConfirm; static float mConfirmUntil;   // 1 rogner, 2 effacer : second clic pour confirmer
        static bool mDrag;
        static float mOpenedAt, mRDown = -1f;
        const string TitleField = "mwcoop-montage-titre";

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern int SHGetFolderPath(IntPtr owner, int folder, IntPtr token, int flags, StringBuilder path);

        static string VideoFolder
        {
            get
            {
                string f = Config.Get("Boutique", "DossierVideos", "");
                if (f.Length > 0) return f;
                string v = "";
                // (dossier Videos de Windows : SHGetFolderPath, CSIDL_MYVIDEO -- .NET 3.5 n'a pas SpecialFolder.MyVideos)
                try { var sb = new StringBuilder(260); if (SHGetFolderPath(IntPtr.Zero, 0x000E, IntPtr.Zero, 0, sb) == 0) v = sb.ToString(); } catch { }
                if (v.Length == 0) try { v = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments); } catch { }
                return Path.Combine(v.Length > 0 ? v : Log.DataDir, "CoopVideos");
            }
        }

        // ---------------------------------------------------------------- ouvrir / fermer
        static Esine aimedTape;   // cassette posee visee (InteractItems)
        static float testRUntil; static bool testRWas, testFocus;
        // Focus clavier a vider au prochain passage d'OnGUI : mis a 0 hors d'OnGUI (Echap), GUIUtility.keyboardControl revenait
        // au champ du titre -- a la reouverture il avait deja le focus (essai vhs : « keyboardControl 1 » apres fermeture) et,
        // champ texte actif, Unity avale les touches du jeu (Input.eatKeyPressOnTextFieldFocus). Retour de Marshal, 13/10 :
        // « R ouvre le montage une fois ; ferme, il ne s'ouvre plus ».
        static bool mUnfocus;
        static float mWhyAt;   // (essai : R tenu simule)
        static Esine HeldTape() { return esineet.Find(e => (e.Kind == KVhs || e.Kind == KKela) && e.Go != null && e.Go.transform.parent != null && Game.UnderPlayer(e.Go.transform)); }

        // R tenu : appele chaque image avec la cassette visee ou tenue (null : rien)
        static void TickMontageKey(Esine tape, float now)
        {
            bool simR = now < testRUntil;
            if (testRUntil > 0f && simR != testRWas) { testRWas = simR; L("essai : R " + (simR ? "enfonce" : "relache") + " (cassette visee " + (tape != null) + ", montage " + MontageOpen + ", occupe " + KameraBusy + ", viseur " + Viewfinder + ")"); }
            if (!MontageOpen && tape == null && Input.GetKeyDown(Keys.CameraRec) && now > mWhyAt && (lookedItem != null && lookedItem.Kind == KVhs || HeldTape() != null))
            {
                mWhyAt = now + 5f;   // (journal : pourquoi R ne fait rien sur une cassette)
                L("video : R sur une cassette sans montage (occupe " + KameraBusy + ", viseur " + Viewfinder + ", menu " + Menu.Open + ", tchat " + Menu.ChatOpen + ", focus clavier " + GUIUtility.keyboardControl + ")");
            }
            if (MontageOpen || tape == null) { mRDown = -1f; return; }
            // Un appui suffit (Marshal, 13/10, journaux et video : il appuyait sur R sans le tenir -- « je ne peux pas ouvrir le
            // montage ». R tenu 0,7 s avant).
            if (Input.GetKeyDown(Keys.CameraRec) || simR && mRDown < 0f) { mRDown = now; OpenMontage(tape); }
            if (!Input.GetKey(Keys.CameraRec) && !simR) mRDown = -1f;
        }

        static void OpenMontage(Esine tape)
        {
            mTape = tape; MontageOpen = true; mOpenedAt = Time.realtimeSinceStartup; mUnfocus = true;
            mTitle = TapeTitle(tape.Data); mNote = null; mConfirm = 0; mDrag = false;
            WalletPanel.Open = false; CheatPanel.Open = false; MapPanel.Open = false;
            LoadMontage();
            mA = 0f; mB = mTotal; mT = 0f; mPlaying = false; mClip = -1; mFrame = -1;
            L("video : montage de la cassette " + tape.Id + " (" + mClips.Count + " prise(s), " + mTotal.ToString("F1") + " s)");
        }

        static void LoadMontage()
        {
            mClips.Clear(); mMissing = false; mTotal = 0f;
            foreach (string c in Clips(mTape.Data))
            {
                Film f = LoadFilm(c);
                if (f == null) { mMissing = true; AskFilm(c, Time.realtimeSinceStartup); continue; }
                mClips.Add(f); mTotal += f.LengthMs / 1000f;
            }
        }

        static void CloseMontage()
        {
            MontageOpen = false; mPlaying = false; mTape = null;
            if (mAudio != null) mAudio.Stop();
            GUIUtility.keyboardControl = 0; mUnfocus = true;
        }

        // ---------------------------------------------------------------- chaque image
        static void TickMontage(float now)
        {
            TickExport(now);
            TickPick();
            if (!MontageOpen) return;
            if (mTape == null || mTape.Go == null || !esineet.Contains(mTape)) { CloseMontage(); return; }
            if (Input.GetKeyDown(KeyCode.Escape)) { CloseMontage(); return; }
            if (mMissing && now - mOpenedAt > 1f && Mathf.Repeat(now, 1f) < Time.unscaledDeltaTime * 2f)
            {
                // une prise arrivee entre-temps : recharge
                bool all = true;
                foreach (string c in Clips(mTape.Data)) if (!HaveFilm(c)) all = false;
                if (all) { LoadMontage(); mB = mTotal; }
            }
            if (mPlaying)
            {
                mT += Time.unscaledDeltaTime;
                if (mT >= mTotal) { mT = mTotal; mPlaying = false; if (mAudio != null) mAudio.Stop(); }
            }
            ShowMontageFrame(false);
        }

        // image (et son) de l'instant mT
        static void ShowMontageFrame(bool seek)
        {
            if (mClips.Count == 0) return;
            float off = 0f; int ci = mClips.Count - 1;
            for (int i = 0; i < mClips.Count; i++) { float len = mClips[i].LengthMs / 1000f; if (mT < off + len || i == mClips.Count - 1) { ci = i; break; } off += len; }
            Film f = mClips[ci];
            float tc = Mathf.Max(0f, mT - off);
            if (mTex == null) mTex = new Texture2D(2, 2, TextureFormat.RGB24, false) { name = "kauppa_montage", wrapMode = TextureWrapMode.Clamp };
            if (ci != mClip) { mClip = ci; mFrame = -1; seek = true; }
            uint ms = (uint)(tc * 1000f);
            int lo = 0, hi = f.Ms.Count - 1, idx = -1;
            while (lo <= hi) { int mid = (lo + hi) / 2; if (f.Ms[mid] <= ms) { idx = mid; lo = mid + 1; } else hi = mid - 1; }
            if (idx < 0 && f.Ms.Count > 0) idx = 0;
            if (idx >= 0 && idx != mFrame)
            {
                mFrame = idx;
                var jpg = new byte[f.Len[idx]];
                Buffer.BlockCopy(f.Data, f.Off[idx], jpg, 0, jpg.Length);
                mTex.LoadImage(jpg);
            }
            if (mAudioGo == null) { mAudioGo = new GameObject("MWCoop montage"); UnityEngine.Object.DontDestroyOnLoad(mAudioGo); mAudio = mAudioGo.AddComponent<AudioSource>(); mAudio.spatialBlend = 0f; mAudio.volume = 0.9f; mAudio.loop = false; }
            if (!mPlaying) { if (mAudio.isPlaying) mAudio.Stop(); return; }
            if (seek || mAudio.clip != f.Clip || !mAudio.isPlaying && f.Clip != null && tc < f.Clip.length - 0.05f)
            {
                mAudio.Stop();
                mAudio.clip = f.Clip;
                if (f.Clip != null && tc < f.Clip.length - 0.05f) { mAudio.time = tc; mAudio.Play(); }
            }
        }

        static void Seek(float t) { mT = Mathf.Clamp(t, 0f, mTotal); mFrame = -1; ShowMontageFrame(true); }

        static void Note(string s, bool bad) { mNote = s; mNoteBad = bad; mNoteUntil = Time.realtimeSinceStartup + 6f; }

        // ---------------------------------------------------------------- couper (rogner, exporter)
        // Ecrit dans 'path' un film fait de [a, b] (secondes) de la cassette : images de cette plage (plus celle visible a
        // a), son de cette plage (complete de silence quand une prise a moins de son que d'images : les suivantes restent
        // calees). Renvoie le nombre d'images.
        static int WriteCut(List<Film> clips, float a, float b, string path)
        {
            int frames = 0;
            int w = 256, h = 192, fps = 10;
            foreach (Film f in clips)
                if (f.Data.Length >= 11)
                {
                    if (frames == 0) { w = f.Data[4] | f.Data[5] << 8; h = f.Data[6] | f.Data[7] << 8; }
                    fps = Mathf.Max(fps, f.Data[8]);
                    frames = 1;
                }
            frames = 0;
            using (var wr = new BinaryWriter(new FileStream(path, FileMode.Create, FileAccess.Write)))
            {
                wr.Write(new[] { (byte)'M', (byte)'W', (byte)'V', (byte)'1' });
                wr.Write((ushort)w); wr.Write((ushort)h); wr.Write((byte)fps); wr.Write((ushort)RecRate);
                float off = 0f;
                foreach (Film f in clips)
                {
                    float len = f.LengthMs / 1000f;
                    float ca = Mathf.Max(0f, a - off), cb = Mathf.Min(len, b - off);
                    if (cb > ca)
                    {
                        // images : celle visible au debut de la plage, puis celles de la plage
                        int first = -1;
                        for (int i = 0; i < f.Ms.Count; i++) if (f.Ms[i] <= ca * 1000f) first = i; else break;
                        if (first < 0) first = 0;
                        for (int i = first; i < f.Ms.Count; i++)
                        {
                            float tf = f.Ms[i] / 1000f;
                            if (i > first && tf >= cb) break;
                            float tg = Mathf.Max(0f, off + tf - a);
                            wr.Write((byte)1); wr.Write((uint)(tg * 1000f)); wr.Write(f.Len[i]); wr.Write(f.Data, f.Off[i], f.Len[i]);
                            frames++;
                        }
                        // son de la plage (silence s'il en manque)
                        int s0 = Mathf.RoundToInt(ca * RecRate), s1 = Mathf.RoundToInt(cb * RecRate);
                        if (s1 > s0)
                        {
                            var mu = new byte[s1 - s0];
                            byte silence = MuLaw(0f);
                            for (int i = 0; i < mu.Length; i++) { int k = s0 + i; mu[i] = f.Sound != null && k < f.Sound.Length ? MuLaw(f.Sound[k]) : silence; }
                            wr.Write((byte)2); wr.Write((uint)(Mathf.Max(0f, off + ca - a) * 1000f)); wr.Write(mu.Length); wr.Write(mu);
                        }
                    }
                    off += len;
                    if (off >= b) break;
                }
            }
            return frames;
        }

        static void Trim()
        {
            if (mClips.Count == 0 || mMissing) return;
            float a = mA, b = mB;
            if (b - a < 0.2f) { Note(Lang.T("Sélection trop courte.", "Selection too short."), true); return; }
            string film = "f" + Session.LocalId + "-" + DateTime.Now.Ticks.ToString("x");
            int n;
            try { Directory.CreateDirectory(FilmDir); n = WriteCut(mClips, a, b, FilmPath(film)); }
            catch (Exception e) { Note(Lang.T("Rognage raté : ", "Trim failed: ") + e.Message, true); return; }
            if (n == 0) { try { File.Delete(FilmPath(film)); } catch { } Note(Lang.T("Rien dans la sélection.", "Nothing in the selection."), true); return; }
            SetData(mTape, film + "~" + Clean(mTitle));
            if (mTape.Kind != KKela) SendFilm(film);   // (bobine : proposee au projecteur -- KinoJako)
            L("video : cassette " + mTape.Id + " rognee " + a.ToString("F1") + "-" + b.ToString("F1") + " s -> " + film + " (" + n + " images)");
            LoadMontage();
            mA = 0f; mB = mTotal; Seek(0f);
            Note(Lang.T("Rognée : il reste ", "Trimmed: ") + Clock(mTotal) + Lang.T(". Les prochains enregistrements s'ajoutent à la suite.", " left. Next recordings are added after it."), false);
        }

        static void Erase()
        {
            SetData(mTape, "tyhja~" + Clean(mTitle));
            L("video : cassette " + mTape.Id + " effacee");
            LoadMontage(); mA = mB = mT = 0f; mPlaying = false;
            Note(Lang.T("Cassette effacée.", "Tape erased."), false);
        }

        static string Clean(string s)
        {
            s = s ?? "";
            foreach (char c in new[] { '|', '~', ';', '+', '\n', '\r' }) s = s.Replace(c, ' ');
            s = s.Trim();
            return s.Length > 24 ? s.Substring(0, 24).Trim() : s;
        }

        static string Clock(float t) { int s = Mathf.FloorToInt(t); return (s / 60) + ":" + (s % 60).ToString("00") + "." + Mathf.FloorToInt((t - s) * 10f); }

        // ---------------------------------------------------------------- export .mp4 et dossier
        [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Unicode)] delegate int DVExport(string src, string dst, int scale, string logo);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Unicode)] delegate int DVPick(string initial);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Unicode)] delegate int DVPickPoll(StringBuilder outPath, int n);
        static DVExport vExport; static DVPick vPick; static DVPickPoll vPickPoll; static bool vExTried;
        static string exportDst, exportTmp; static float exportNext; static int exportPct = -1;
        public static bool Exporting { get { return exportDst != null; } }

        static bool BindExport()
        {
            if (vExTried) return vExport != null;
            vExTried = true;
            if (!BindVideo()) return false;
            try
            {
                string own = Path.Combine(Path.GetDirectoryName(Config.IniPath), "MWCoopRT.dll");
                if (!File.Exists(own)) own = "MWCoopRT.dll";
                IntPtr h = LoadLibraryW(own);
                IntPtr pe = GetProcAddress(h, "VidExportStart"), pp = GetProcAddress(h, "VidPickFolderStart"), pq = GetProcAddress(h, "VidPickFolderPoll");
                if (pe == IntPtr.Zero || pp == IntPtr.Zero || pq == IntPtr.Zero) { L("video : MWCoopRT.dll sans export (ancienne version ?)"); return false; }
                vExport = (DVExport)Marshal.GetDelegateForFunctionPointer(pe, typeof(DVExport));
                vPick = (DVPick)Marshal.GetDelegateForFunctionPointer(pp, typeof(DVPick));
                vPickPoll = (DVPickPoll)Marshal.GetDelegateForFunctionPointer(pq, typeof(DVPickPoll));
                return true;
            }
            catch (Exception e) { L("video : export indisponible : " + e.Message); return false; }
        }

        static void Export()
        {
            if (mClips.Count == 0 || mMissing) return;
            if (Exporting || import != null) { Note(Lang.T("Un export ou un import est déjà en cours.", "An export or import is already running."), true); return; }
            if (!BindExport()) { Note(Lang.T("Export impossible : MWCoopRT.dll manque ou est trop ancienne (Windows 7 : pas d'export).", "Export unavailable: MWCoopRT.dll missing or too old (no export on Windows 7)."), true); return; }
            string dir = VideoFolder;
            try { Directory.CreateDirectory(dir); } catch (Exception e) { Note(Lang.T("Dossier impossible : ", "Can't use folder: ") + e.Message, true); return; }
            string name = Clean(mTitle);
            foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            if (name.Length == 0) name = "MWCoop";
            string stamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
            string dst = Path.Combine(dir, name + "_" + stamp + ".mp4");
            exportTmp = Path.Combine(FilmDir, "export-" + stamp + ".mwv");
            int n;
            try { n = WriteCut(mClips, mA, mB, exportTmp); }
            catch (Exception e) { Note(Lang.T("Export raté : ", "Export failed: ") + e.Message, true); return; }
            if (n == 0) { Note(Lang.T("Rien dans la sélection.", "Nothing in the selection."), true); return; }
            int scale = Mathf.Clamp(Config.GetInt("Boutique", "ExportEchelle", 3), 1, 6);
            int r = vExport(exportTmp, dst, scale, LogoFile());
            if (r != 0) { Note(Lang.T("Export pas lancé (", "Export not started (") + r + ")", true); return; }
            exportDst = dst; exportPct = -1; exportNext = 0f;
            Note(Lang.T("Export en cours…", "Exporting…"), false);
            L("video : export de " + mTape.Id + " (" + mA.ToString("F1") + "-" + mB.ToString("F1") + " s) vers " + dst);
        }

        static void TickExport(float now)
        {
            if (exportDst == null || vPoll == null || now < exportNext) return;
            exportNext = now + 0.25f;
            var p = new float[1]; var sb = new StringBuilder(512);
            int st = vPoll(p, sb, 512);
            if (st == 1) { exportPct = Mathf.FloorToInt(p[0] * 100f); return; }
            string dst = exportDst;
            exportDst = null; exportPct = -1;
            try { if (exportTmp != null) File.Delete(exportTmp); } catch { }
            if (st == 2)
            {
                L("video : export fini : " + dst + " (" + sb + ")");
                Note(Lang.T("Exporté : ", "Exported: ") + Path.GetFileName(dst), false);
                Hud.Toast(Lang.T("Vidéo exportée : ", "Video exported: ") + dst);
            }
            else { L("video : export rate : " + sb); Note(Lang.T("Export raté : ", "Export failed: ") + sb, true); }
        }

        // logo du mod (ressource du lanceur) en fichier, pour l'export : en haut a gauche, semi-transparent
        static string LogoFile()
        {
            if (Config.GetInt("Boutique", "ExportLogo", 1) == 0) return "";
            string f = Path.Combine(FilmDir, "logo-mwcoop.png");
            try
            {
                if (!File.Exists(f))
                    using (var s = typeof(Kauppa).Assembly.GetManifestResourceStream("MWCoop.logo-titre.png"))
                    {
                        if (s == null) return "";
                        var b = new byte[s.Length]; s.Read(b, 0, b.Length);
                        File.WriteAllBytes(f, b);
                    }
                return f;
            }
            catch { return ""; }
        }

        static bool picking;
        static void PickFolder()
        {
            if (picking) return;
            if (!BindExport()) { Note(Lang.T("Choix du dossier indisponible.", "Folder picker unavailable."), true); return; }
            string cur = VideoFolder;
            try { Directory.CreateDirectory(cur); } catch { }
            if (vPick(cur) == 0) picking = true;
        }

        static void TickPick()
        {
            if (!picking || vPickPoll == null) return;
            var sb = new StringBuilder(1024);
            int st = vPickPoll(sb, 1024);
            if (st == 1 || st == 0) return;
            picking = false;
            if (pickReel != null) { Esine r = pickReel; pickReel = null; if (st == 2 && sb.Length > 0 && esineet.Contains(r)) StartImport(r, sb.ToString()); return; }   // (video choisie pour une bobine)
            if (st == 2 && sb.Length > 0)
            {
                Config.Save("Boutique", "DossierVideos", sb.ToString());
                Note(Lang.T("Dossier : ", "Folder: ") + sb, false);
                L("video : dossier des videos " + sb);
            }
        }

        static void OpenFolder()
        {
            string dir = VideoFolder;
            try { Directory.CreateDirectory(dir); System.Diagnostics.Process.Start("explorer.exe", "\"" + dir + "\""); }
            catch (Exception e) { Note(e.Message, true); }
        }

        // ---------------------------------------------------------------- dessin (Core.OnGUI)
        static bool MButton(Rect r, string label, bool accent, Event e, bool enabled = true, bool danger = false)
        {
            bool hover = enabled && r.Contains(e.mousePosition);
            Color bg = !enabled ? new Color(1f, 1f, 1f, 0.04f)
                     : danger ? new Color(0.85f, 0.25f, 0.2f, hover ? 0.95f : 0.75f)
                     : accent ? (hover ? Style.Accent : Style.AccentDeep) : new Color(1f, 1f, 1f, hover ? 0.14f : 0.08f);
            if (e.type == EventType.Repaint)
            {
                Style.Round(r, r.height / 2, bg);
                Style.Text(r, label, 15, TextAnchor.MiddleCenter, !enabled ? Style.Dim : accent && !danger ? Style.OnAccent : Style.White);
            }
            if (enabled && e.type == EventType.MouseDown && e.button == 0 && hover) { e.Use(); return true; }
            return false;
        }

        public static void DrawMontage()
        {
            if (mUnfocus && Event.current.type == EventType.Repaint) { mUnfocus = false; GUIUtility.keyboardControl = 0; }
            if (!MontageOpen || mTape == null) return;
            Event e = Event.current;
            float now = Time.realtimeSinceStartup;
            float k = Mathf.Clamp01((now - mOpenedAt) / 0.14f);
            Style.Alpha = 1f - (1f - k) * (1f - k);
            float pad = Style.Px(22), W = Mathf.Min(Style.Px(980), Screen.width - Style.Px(32)), H = Mathf.Min(Style.Px(720), Screen.height - Style.Px(32));
            float x = Mathf.Round((Screen.width - W) / 2), y = Mathf.Round((Screen.height - H) / 2);
            if (e.type == EventType.Repaint) Style.Glass(new Rect(x, y, W, H), Style.Px(20));
            bool reel = mTape.Kind == KKela;
            Style.Title(new Rect(x + pad, y + Style.Px(12), W - 2 * pad, Style.Px(40)), reel ? Lang.T("BOBINE DE FILM", "FILM REEL") : Lang.T("MONTAGE DE LA CASSETTE", "TAPE EDITING"), Style.White, TextAnchor.MiddleLeft, 26);
            if (MButton(new Rect(x + W - pad - Style.Px(130), y + Style.Px(16), Style.Px(130), Style.Px(34)), Lang.T("FERMER (Échap)", "CLOSE (Esc)"), false, e)) { CloseMontage(); Style.Alpha = 1f; return; }
            float cy = y + Style.Px(64);
            // titre
            Style.Text(new Rect(x + pad, cy, Style.Px(80), Style.Px(38)), Lang.T("Titre", "Title"), 16, TextAnchor.MiddleLeft, Style.Dim);
            var rf = new Rect(x + pad + Style.Px(80), cy, W - 2 * pad - Style.Px(80) - Style.Px(150), Style.Px(38));
            bool focused = GUI.GetNameOfFocusedControl() == TitleField;
            if (e.type == EventType.Repaint) Style.FieldBack(rf, focused);
            bool enter = focused && e.type == EventType.KeyDown && (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter);
            if (enter) e.Use();
            GUI.SetNextControlName(TitleField);
            mTitle = GUI.TextField(rf, mTitle, 24, Style.Field(19));
            if (testFocus) { testFocus = false; GUI.FocusControl(TitleField); }
            string saved = TapeTitle(mTape.Data);
            if (MButton(new Rect(rf.xMax + Style.Px(10), cy + Style.Px(2), Style.Px(140), Style.Px(34)), Lang.T("ÉCRIRE", "WRITE"), true, e, Clean(mTitle) != saved) || enter && Clean(mTitle) != saved)
            {
                int i = mTape.Data.IndexOf('~');
                string content = i < 0 ? mTape.Data : mTape.Data.Substring(0, i);
                SetData(mTape, (content.Length > 0 ? content : "tyhja") + "~" + Clean(mTitle));
                mTitle = Clean(mTitle);
                Note(Lang.T("Titre écrit sur la tranche.", "Title written on the spine."), false);
                GUIUtility.keyboardControl = 0;
            }
            cy += Style.Px(54);
            // ecran
            float pw = Mathf.Min(Style.Px(560), W * 0.6f), ph = Mathf.Round(pw * 0.75f);
            var pr = new Rect(x + pad, cy, pw, ph);
            if (e.type == EventType.Repaint)
            {
                Style.Fill(pr, Color.black);
                if (mClips.Count > 0 && mTex != null && mFrame >= 0) GUI.DrawTexture(pr, mTex, ScaleMode.ScaleToFit, false);
                else if (reel && TapeFilm(mTape.Data).StartsWith("url:")) Style.Text(pr, Lang.T("LIEN : ", "LINK: ") + TapeFilm(mTape.Data).Substring(4), 15, TextAnchor.MiddleCenter, Style.Accent, false, true);
                else Style.Text(pr, mMissing ? Lang.T("Film en cours de réception…", "Film still arriving…") : reel ? Lang.T("BOBINE VIERGE", "BLANK REEL") : Lang.T("CASSETTE VIERGE", "BLANK TAPE"), 20, TextAnchor.MiddleCenter, Style.Dim);
            }
            // colonne de droite : infos et lecture
            float rx = pr.xMax + pad, rw = x + W - pad - rx, ry = cy;
            int clips = Clips(mTape.Data).Count;
            Style.Text(new Rect(rx, ry, rw, Style.Px(28)), clips + Lang.T(" prise(s) — ", " take(s) — ") + Clock(mTotal), 18, TextAnchor.MiddleLeft, Style.White);
            ry += Style.Px(30);
            Style.Text(new Rect(rx, ry, rw, Style.Px(26)), Lang.T("Position ", "Position ") + Clock(mT), 16, TextAnchor.MiddleLeft, Style.Dim);
            ry += Style.Px(26);
            Style.Text(new Rect(rx, ry, rw, Style.Px(26)), Lang.T("Sélection ", "Selection ") + Clock(mA) + " → " + Clock(mB) + "  (" + Clock(mB - mA) + ")", 16, TextAnchor.MiddleLeft, Style.Accent);
            ry += Style.Px(40);
            bool have = mClips.Count > 0 && !mMissing;
            float bh = Style.Px(36), gap = Style.Px(8);
            if (MButton(new Rect(rx, ry, rw, bh), mPlaying ? Lang.T("❚❚  PAUSE", "❚❚  PAUSE") : Lang.T("▶  LIRE", "▶  PLAY"), true, e, have))
            { if (!mPlaying && mT >= mTotal - 0.05f) mT = 0f; mPlaying = !mPlaying; ShowMontageFrame(true); }
            ry += bh + gap;
            float hw = (rw - gap) / 2;
            if (MButton(new Rect(rx, ry, hw, bh), Lang.T("DÉBUT ICI", "START HERE"), false, e, have)) { mA = Mathf.Min(mT, mB); }
            if (MButton(new Rect(rx + hw + gap, ry, hw, bh), Lang.T("FIN ICI", "END HERE"), false, e, have)) { mB = Mathf.Max(mT, mA); }
            ry += bh + gap;
            if (MButton(new Rect(rx, ry, hw, bh), Lang.T("TOUT", "ALL"), false, e, have)) { mA = 0f; mB = mTotal; }
            if (MButton(new Rect(rx + hw + gap, ry, hw, bh), Lang.T("ALLER AU DÉBUT", "GO TO START"), false, e, have)) Seek(mA);
            if (reel) DrawReelExtras(rx, ry + bh + gap * 3, rw, e);   // (importer une video, lien : KinoLinkki)
            // barre de temps
            float ty = cy + ph + Style.Px(16);
            var bar = new Rect(x + pad, ty, W - 2 * pad, Style.Px(30));
            if (e.type == EventType.Repaint)
            {
                Style.Fill(bar, new Color(1f, 1f, 1f, 0.06f));
                if (mTotal > 0f)
                {
                    float off = 0f;
                    for (int i = 0; i < mClips.Count; i++)
                    {
                        float len = mClips[i].LengthMs / 1000f;
                        var seg = new Rect(bar.x + bar.width * off / mTotal, bar.y + Style.Px(6), Mathf.Max(1f, bar.width * len / mTotal - Style.Px(2)), bar.height - Style.Px(12));
                        Style.Fill(seg, (i & 1) == 0 ? new Color(1f, 1f, 1f, 0.22f) : new Color(1f, 1f, 1f, 0.32f));
                        off += len;
                    }
                    var sel = new Rect(bar.x + bar.width * mA / mTotal, bar.y, bar.width * (mB - mA) / mTotal, bar.height);
                    Style.Fill(sel, new Color(Style.Accent.r, Style.Accent.g, Style.Accent.b, 0.30f));
                    float th = Mathf.Max(2f, Style.Px(3));
                    Style.Fill(new Rect(sel.x - th / 2, bar.y - Style.Px(4), th, bar.height + Style.Px(8)), Style.Accent);
                    Style.Fill(new Rect(sel.xMax - th / 2, bar.y - Style.Px(4), th, bar.height + Style.Px(8)), Style.Accent);
                    Style.Fill(new Rect(bar.x + bar.width * mT / mTotal - th / 2, bar.y - Style.Px(8), th, bar.height + Style.Px(16)), Color.white);
                }
            }
            if (have && mTotal > 0f)
            {
                if (e.type == EventType.MouseDown && e.button == 0 && bar.Contains(e.mousePosition)) { mDrag = true; e.Use(); }
                if (mDrag && (e.type == EventType.MouseDown || e.type == EventType.MouseDrag)) { Seek((e.mousePosition.x - bar.x) / bar.width * mTotal); e.Use(); }
                if (e.type == EventType.MouseUp) mDrag = false;
            }
            Style.Text(new Rect(bar.x, bar.yMax + Style.Px(2), bar.width, Style.Px(22)), Lang.T("Clique ou glisse sur la barre pour te déplacer ; DÉBUT ICI / FIN ICI choisissent le morceau à garder ou à exporter.", "Click or drag the bar to move; START HERE / END HERE pick the part to keep or export."), 13, TextAnchor.MiddleLeft, Style.Dim, false);
            // actions
            float ay = bar.yMax + Style.Px(34), aw = (W - 2 * pad - 2 * gap) / 3;
            if (mConfirm != 0 && now > mConfirmUntil) mConfirm = 0;
            if (MButton(new Rect(x + pad, ay, aw, Style.Px(42)), mConfirm == 1 ? Lang.T("SÛR ? CLIQUE ENCORE", "SURE? CLICK AGAIN") : Lang.T("ROGNER (GARDER LA SÉLECTION)", "TRIM (KEEP THE SELECTION)"), false, e, have && (mA > 0.05f || mB < mTotal - 0.05f), mConfirm == 1))
            { if (mConfirm == 1) { mConfirm = 0; Trim(); } else { mConfirm = 1; mConfirmUntil = now + 3f; } }
            if (MButton(new Rect(x + pad + aw + gap, ay, aw, Style.Px(42)), mConfirm == 2 ? Lang.T("SÛR ? CLIQUE ENCORE", "SURE? CLICK AGAIN") : reel ? Lang.T("EFFACER LA BOBINE", "ERASE THE REEL") : Lang.T("EFFACER LA CASSETTE", "ERASE THE TAPE"), false, e, clips > 0 || TapeFilm(mTape.Data).StartsWith("url:"), true))
            { if (mConfirm == 2) { mConfirm = 0; Erase(); } else { mConfirm = 2; mConfirmUntil = now + 3f; } }
            if (MButton(new Rect(x + pad + 2 * (aw + gap), ay, aw, Style.Px(42)), Exporting ? Lang.T("EXPORT… ", "EXPORTING… ") + Mathf.Max(0, exportPct) + " %" : Lang.T("EXPORTER EN .MP4", "EXPORT AS .MP4"), true, e, have && !Exporting))
                Export();
            // dossier
            float fy = ay + Style.Px(56);
            Style.Text(new Rect(x + pad, fy, W - 2 * pad - Style.Px(300), Style.Px(34)), Lang.T("Dossier : ", "Folder: ") + VideoFolder, 15, TextAnchor.MiddleLeft, Style.Dim, false);
            if (MButton(new Rect(x + W - pad - Style.Px(290), fy, Style.Px(140), Style.Px(34)), picking ? "…" : Lang.T("CHANGER", "CHANGE"), false, e, !picking)) PickFolder();
            if (MButton(new Rect(x + W - pad - Style.Px(140), fy, Style.Px(140), Style.Px(34)), Lang.T("OUVRIR", "OPEN"), false, e)) OpenFolder();
            // note
            if (mNote != null && now < mNoteUntil)
                Style.Text(new Rect(x + pad, y + H - Style.Px(44), W - 2 * pad, Style.Px(30)), mNote, 16, TextAnchor.MiddleLeft, mNoteBad ? Style.Warn : Style.Good, false);
            Style.Alpha = 1f;
        }

        static void OnLevelLoadedMontage() { if (MontageOpen) CloseMontage(); mClips.Clear(); }
    }
}
