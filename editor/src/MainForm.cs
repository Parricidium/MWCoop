using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace EditeurCarte
{
    // Fenetre sans cadre (comme l'application Claude : barre de titre dessinee par la page), bord de 5 px pour redimensionner,
    // coins arrondis et ombre de Windows 11. Toute l'interface est dans ui\ (https://editeur.local), les cartes preparees dans
    // https://carte.local (Documents\My Seasons Car\Carte). Page <-> programme : messages JSON { type: ... }.
    public class MainForm : Form
    {
        readonly WebView2 web = new WebView2();
        readonly JavaScriptSerializer json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
        Prep prep;
        const int Grip = 5;

        public MainForm()
        {
            Text = "My Seasons Car · Éditeur de carte";
            FormBorderStyle = FormBorderStyle.None;
            BackColor = Color.FromArgb(0x26, 0x26, 0x26);
            Size = new Size(1280, 800);
            MinimumSize = new Size(960, 600);
            StartPosition = FormStartPosition.CenterScreen;
            if (Environment.GetEnvironmentVariable("EDITEUR_HORS_ECRAN") == "1") { StartPosition = FormStartPosition.Manual; Location = new Point(-4000, 100); ShowInTaskbar = false; }   // (essais : jamais sur l'ecran du joueur)
            Padding = new Padding(Grip);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            web.Dock = DockStyle.Fill;
            web.DefaultBackgroundColor = Color.FromArgb(0x1a, 0x1a, 0x1a);
            Controls.Add(web);
            Load += async (s, e) => await InitWeb();
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.Style |= 0x00020000;          // WS_MINIMIZEBOX : reduire depuis la barre des taches
                cp.ClassStyle |= 0x00020000;     // CS_DROPSHADOW
                return cp;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            int round = 2;   // DWMWA_WINDOW_CORNER_PREFERENCE = DWMWCP_ROUND (Windows 11 ; ignore ailleurs)
            try { DwmSetWindowAttribute(Handle, 33, ref round, 4); } catch { }
            int dark = 1;    // DWMWA_USE_IMMERSIVE_DARK_MODE
            try { DwmSetWindowAttribute(Handle, 20, ref dark, 4); } catch { }
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            Padding = WindowState == FormWindowState.Maximized ? new Padding(0) : new Padding(Grip);
            Post(new Dictionary<string, object> { { "type", "fenetre" }, { "agrandie", WindowState == FormWindowState.Maximized } });
        }

        protected override void WndProc(ref Message m)
        {
            const int WM_NCHITTEST = 0x84;
            if (m.Msg == WM_NCHITTEST && WindowState == FormWindowState.Normal)
            {
                Point p = PointToClient(new Point((short)(m.LParam.ToInt64() & 0xFFFF), (short)((m.LParam.ToInt64() >> 16) & 0xFFFF)));
                int c = Grip * 2;
                bool l = p.X < c, r = p.X >= ClientSize.Width - c, t = p.Y < c, b = p.Y >= ClientSize.Height - c;
                int hit = t && l ? 13 : t && r ? 14 : b && l ? 16 : b && r ? 17 : l ? 10 : r ? 11 : t ? 12 : b ? 15 : 1;
                m.Result = (IntPtr)hit;
                return;
            }
            base.WndProc(ref m);
        }

        async System.Threading.Tasks.Task InitWeb()
        {
            try
            {
                string data = Path.Combine(Games.EditorDir, "WebView2");
                Directory.CreateDirectory(data);
                var env = await CoreWebView2Environment.CreateAsync(null, data);
                await web.EnsureCoreWebView2Async(env);
            }
            catch (Exception e)
            {
                MessageBox.Show(this, "WebView2 (Microsoft Edge) est introuvable sur ce PC.\n\nInstalle « Microsoft Edge WebView2 Runtime » depuis le site de Microsoft, puis relance l'éditeur.\n\n(" + e.Message + ")",
                                "Éditeur de carte", MessageBoxButtons.OK, MessageBoxIcon.Error);
                Close();
                return;
            }
            CoreWebView2 w = web.CoreWebView2;
            CoreWebView2Settings st = w.Settings;
            st.AreDevToolsEnabled = Program.Debug;
            st.AreDefaultContextMenusEnabled = Program.Debug;
            st.IsStatusBarEnabled = false;
            st.IsZoomControlEnabled = false;
            st.AreBrowserAcceleratorKeysEnabled = Program.Debug;
            string ui = Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "ui");
            w.SetVirtualHostNameToFolderMapping("editeur.local", ui, CoreWebView2HostResourceAccessKind.Allow);
            Directory.CreateDirectory(Games.CacheRoot);
            w.SetVirtualHostNameToFolderMapping("carte.local", Games.CacheRoot, CoreWebView2HostResourceAccessKind.Allow);
            Directory.CreateDirectory(Projects.Root);
            w.SetVirtualHostNameToFolderMapping("projets.local", Projects.Root, CoreWebView2HostResourceAccessKind.Allow);   // (modeles importes)
            w.WebMessageReceived += OnMessage;
            w.NewWindowRequested += (s, e) => { e.Handled = true; try { Process.Start(e.Uri); } catch { } };
            w.Navigate("https://editeur.local/index.html");
        }

        void Post(Dictionary<string, object> msg)
        {
            if (IsDisposed) return;
            if (InvokeRequired) { try { BeginInvoke(new Action(() => Post(msg))); } catch (InvalidOperationException) { } return; }   // (fil de la preparation)
            if (web.CoreWebView2 == null) return;
            web.CoreWebView2.PostWebMessageAsJson(json.Serialize(msg));
        }

        // ---------------------------------------------------------------- messages de la page
        void OnMessage(object sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            Dictionary<string, object> m;
            try { m = json.Deserialize<Dictionary<string, object>>(e.WebMessageAsJson); } catch { return; }
            string type = S(m, "type");
            switch (type)
            {
                case "pret": SendState(); break;
                case "fenetre": Window(S(m, "action")); break;
                case "choisirExe": PickExe(S(m, "jeu")); break;
                case "deposer":   // fichier glisse sur la fenetre (chemin donne par WebView2)
                    if (e.AdditionalObjects != null)
                        foreach (object o in e.AdditionalObjects) { var f = o as CoreWebView2File; if (f != null) { CheckPick(S(m, "jeu"), f.Path); break; } }
                    break;
                case "verifier": CheckPick(S(m, "jeu"), S(m, "chemin")); break;
                case "valider": Validate(S(m, "jeu"), S(m, "chemin"), m.ContainsKey("retenir") && m["retenir"] is bool && (bool)m["retenir"]); break;
                case "reessayer": SendState(); break;
                case "preparer": StartPrep(S(m, "jeu")); break;
                case "annuler": if (prep != null) prep.Cancelled = true; break;
                case "dossier": OpenFolder(S(m, "chemin")); break;
                // ---- projets de carte (V1)
                case "projets": Post(new Dictionary<string, object> { { "type", "projets" }, { "liste", Projects.List() } }); break;
                case "creerProjet":
                    {
                        string d = Projects.Create(S(m, "nom"), S(m, "jeu"));
                        Post(new Dictionary<string, object> { { "type", "projet" }, { "dossier", d }, { "data", Projects.Read(d) } });
                        break;
                    }
                case "ouvrirProjet": Post(new Dictionary<string, object> { { "type", "projet" }, { "dossier", S(m, "dossier") }, { "data", Projects.Read(S(m, "dossier")) } }); break;
                case "enregistrer":
                    {
                        bool ok = false; try { ok = Projects.Save(S(m, "dossier"), S(m, "data")); } catch { }
                        Post(new Dictionary<string, object> { { "type", "enregistre" }, { "ok", ok } });
                        break;
                    }
                case "importerModele": ImportModel(S(m, "dossier")); break;
                case "importerImage": ImportImage(S(m, "dossier")); break;
                case "images": Post(new Dictionary<string, object> { { "type", "images" }, { "liste", Projects.Images(S(m, "dossier")) } }); break;
                case "dossierProjet": { string d = Projects.Dir(S(m, "dossier")); if (d != null) OpenFolder(d); break; }
                case "compiler": Compile(m); break;
            }
        }

        static string S(Dictionary<string, object> m, string k) { object v; return m.TryGetValue(k, out v) && v != null ? v.ToString() : ""; }

        void Window(string a)
        {
            if (a == "reduire") WindowState = FormWindowState.Minimized;
            else if (a == "agrandir")
            {
                if (WindowState == FormWindowState.Maximized) WindowState = FormWindowState.Normal;
                else { MaximizedBounds = Screen.FromHandle(Handle).WorkingArea; WindowState = FormWindowState.Maximized; }
            }
            else if (a == "fermer") Close();
            else if (a == "deplacer")
            {
                ReleaseCapture();
                SendMessage(Handle, 0xA1, (IntPtr)2, IntPtr.Zero);   // WM_NCLBUTTONDOWN, HTCAPTION : Windows deplace la fenetre (et l'aimante)
            }
        }

        void SendState()
        {
            var games = new List<object>();
            foreach (GameDef g in GameDef.All)
            {
                GameFound f = Games.Find(g);
                string cache = Games.CacheDir(g.Id), info = Path.Combine(cache, "carte.json");
                var d = new Dictionary<string, object> { { "id", g.Id }, { "nom", g.Name }, { "exe", g.Exe }, { "trouve", f != null } };
                if (f != null) { d["dossier"] = f.Dir; d["source"] = f.Source; d["version"] = f.Version; d["lanceur"] = Games.Launcher(f.Dir) != null; }
                if (File.Exists(info))
                {
                    var fi = new FileInfo(Path.Combine(cache, "maillages.bin"));
                    long size = fi.Exists ? fi.Length : 0;
                    try { foreach (string t in Directory.GetFiles(Path.Combine(cache, "textures"))) size += new FileInfo(t).Length; } catch { }
                    d["carte"] = new Dictionary<string, object> { { "date", File.GetLastWriteTime(info).ToString("dd/MM/yyyy HH:mm") }, { "taille", size } };
                }
                games.Add(d);
            }
            Post(new Dictionary<string, object> { { "type", "etat" }, { "jeux", games }, { "cache", Games.CacheRoot }, { "ouvrir", Program.OpenGame ?? "" },
                                                  { "langue", Program.LangArg ?? System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName }, { "langueImposee", Program.LangArg != null } });
            Program.OpenGame = null;
        }

        void PickExe(string id)
        {
            GameDef g = GameDef.Get(id);
            if (g == null) return;
            using (var d = new OpenFileDialog())
            {
                d.Title = "Choisir " + g.Exe;
                d.Filter = g.Exe + "|" + g.Exe + "|Programmes (*.exe)|*.exe";
                d.FileName = g.Exe;
                d.CheckFileExists = true;
                if (d.ShowDialog(this) == DialogResult.OK) CheckPick(id, d.FileName);
            }
        }

        // Verification d'un chemin choisi (exe ou dossier) : chaque point pour la page.
        void CheckPick(string id, string path)
        {
            GameDef g = GameDef.Get(id);
            if (g == null) return;
            string dir = Games.DirOfPick(g, path);
            bool exe = dir != null && File.Exists(Path.Combine(dir, g.Exe));
            bool data = dir != null && Directory.Exists(Path.Combine(dir, g.Data));
            bool ok = dir != null && Games.Check(g, dir) == null;
            Post(new Dictionary<string, object> { { "type", "verification" }, { "jeu", id }, { "chemin", dir != null ? Path.Combine(dir, g.Exe) : path ?? "" }, { "dossier", dir ?? "" },
                                                  { "exe", exe }, { "data", data }, { "ok", ok }, { "version", ok ? Games.ReadVersion(dir) : "" }, { "lanceur", ok && Games.Launcher(dir) != null } });
        }

        void Validate(string id, string path, bool forLauncher)
        {
            GameDef g = GameDef.Get(id);
            string dir = g != null ? Games.DirOfPick(g, path) : null;
            if (g == null || dir == null || Games.Check(g, dir) != null) return;
            Games.Remember(g, dir, forLauncher);
            SendState();
        }

        void StartPrep(string id)
        {
            GameDef g = GameDef.Get(id);
            GameFound f = g != null ? Games.Find(g) : null;
            if (f == null || (prep != null && !prep.Cancelled && prepBusy)) return;
            prep = new Prep(g, f.Dir);
            prepBusy = true;
            prep.Changed += d => { if (d.ContainsKey("fini") && (bool)d["fini"]) prepBusy = false; Post(d); if (d.ContainsKey("fini") && (bool)d["fini"]) BeginInvoke(new Action(SendState)); };
            prep.Start();
        }
        volatile bool prepBusy;

        void ImportModel(string dossier)
        {
            using (var d = new OpenFileDialog())
            {
                d.Title = "Modèle 3D";
                d.Filter = "Modèles 3D (*.glb, *.gltf, *.fbx, *.obj)|*.glb;*.gltf;*.fbx;*.obj";
                d.CheckFileExists = true;
                if (d.ShowDialog(this) != DialogResult.OK) return;
                string f = null, err = null;
                try { f = Projects.ImportModel(dossier, d.FileName); } catch (Exception e) { err = e.Message; }
                Post(new Dictionary<string, object> { { "type", "modele" }, { "fichier", f ?? "" }, { "nom", Path.GetFileNameWithoutExtension(d.FileName) }, { "erreur", err ?? (f == null ? "copie" : "") } });
            }
        }

        // Image perso (textures des objets, demande de JD, 20/10) : copiee dans textures\ du projet.
        void ImportImage(string dossier)
        {
            using (var d = new OpenFileDialog())
            {
                d.Title = "Image";
                d.Filter = "Images (*.png, *.jpg, *.jpeg, *.bmp, *.webp)|*.png;*.jpg;*.jpeg;*.bmp;*.webp";
                d.CheckFileExists = true;
                if (d.ShowDialog(this) != DialogResult.OK) return;
                string f = null, err = null;
                try { f = Projects.ImportImage(dossier, d.FileName); } catch (Exception e) { err = e.Message; }
                Post(new Dictionary<string, object> { { "type", "image" }, { "fichier", f ?? "" }, { "erreur", err ?? (f == null ? "copie" : "") } });
            }
        }

        // Paquet assemble par la page (fichiers en base64) -> .mwcmap dans Documents et dans les cartes du mod pour ce jeu.
        void Compile(Dictionary<string, object> m)
        {
            var reply = new Dictionary<string, object> { { "type", "compile" } };
            try
            {
                GameDef g = GameDef.Get(S(m, "jeu"));
                GameFound f = g != null ? Games.Find(g) : null;
                string mod = g != null ? Games.ModCartes(g, f != null ? f.Dir : null) : null;
                var files = m["fichiers"] as Dictionary<string, object>;
                string path = Projects.Compile(S(m, "dossier"), S(m, "nomFichier"), files, mod);
                reply["chemin"] = path; reply["dossier"] = Path.GetDirectoryName(path); reply["mod"] = mod ?? "";
                reply["taille"] = new FileInfo(path).Length;
            }
            catch (Exception e) { reply["erreur"] = e.Message; }
            Post(reply);
        }

        void OpenFolder(string p)
        {
            try { if (Directory.Exists(p)) Process.Start("explorer.exe", "\"" + p + "\""); } catch { }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (prep != null && prepBusy) prep.Cancelled = true;
            base.OnFormClosing(e);
        }

        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
        [DllImport("user32.dll")] static extern bool ReleaseCapture();
        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, IntPtr l);
    }
}
