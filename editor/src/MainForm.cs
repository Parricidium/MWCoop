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
            if (Program.Browser) { Load += (s, e) => StartBrowser(Environment.GetEnvironmentVariable("MWCOOP_WINE") != null ? "MWCOOP_WINE" : "Wine ou --navigateur"); return; }
            web.Dock = DockStyle.Fill;
            web.DefaultBackgroundColor = Color.FromArgb(0x1a, 0x1a, 0x1a);
            Controls.Add(web);
            Load += async (s, e) => await InitWeb();
        }

        // ---------------------------------------------------------------- mode navigateur (LocalServer)
        // Sans WebView2 (Linux / Proton, ou runtime absent) : la page s'ouvre dans le navigateur du joueur ; cette fenetre devient un petit
        // panneau (« ouvert dans ton navigateur », rouvrir, copier l'adresse, fermer) qui garde le programme en vie et porte ses boites
        // de dialogue (choisir l'exe, importer un modele ou une image).
        LocalServer server;
        Label urlLabel;

        void StartBrowser(string why)
        {
            if (server != null) return;
            Program.Browser = true;
            if (Controls.Contains(web)) { Controls.Remove(web); try { web.Dispose(); } catch { } }
            string ui = Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "ui");
            Directory.CreateDirectory(Games.CacheRoot);
            Directory.CreateDirectory(Projects.Root);
            server = new LocalServer(ui, Games.CacheRoot, Projects.Root, body =>
            {
                try { BeginInvoke(new Action(() => { Dictionary<string, object> m; try { m = json.Deserialize<Dictionary<string, object>>(body); } catch { return; } if (m != null) OnPage(m, null); })); }
                catch (InvalidOperationException) { }
            });
            string f = Environment.GetEnvironmentVariable("EDITEUR_URL_FICHIER");   // (essais : l'adresse pour le navigateur d'essai)
            if (!string.IsNullOrEmpty(f)) try { File.WriteAllText(f, server.Url); } catch { }

            string l = (Program.LangArg ?? System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName).ToLowerInvariant();
            Func<string, string, string, string, string> L = (fr, en, ru, ar) => l == "fr" ? fr : l == "ru" ? ru : l == "ar" ? ar : en;
            SuspendLayout();
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            Padding = new Padding(16);
            MinimumSize = Size.Empty;
            ClientSize = new Size(540, 190);
            if (StartPosition == FormStartPosition.CenterScreen) CenterToScreen();
            ForeColor = Color.FromArgb(0xee, 0xee, 0xee);
            Font = new Font("Segoe UI", 10f);
            if (l == "ar") { RightToLeft = RightToLeft.Yes; RightToLeftLayout = true; }
            var t1 = new Label { AutoSize = false, Location = new Point(16, 14), Size = new Size(508, 26), Font = new Font("Segoe UI", 12f, FontStyle.Bold),
                                 Text = L("L'éditeur de carte est ouvert dans ton navigateur.", "The map editor is open in your web browser.", "Редактор карты открыт в вашем браузере.", "محرر الخريطة مفتوح في متصفحك.") };
            var t2 = new Label { AutoSize = false, Location = new Point(16, 44), Size = new Size(508, 22),
                                 Text = L("Garde cette fenêtre ouverte tant que tu t'en sers.", "Keep this window open while you use it.", "Не закрывайте это окно, пока работаете в редакторе.", "أبقِ هذه النافذة مفتوحة طوال استخدامك للمحرر.") };
            urlLabel = new Label { AutoSize = false, Location = new Point(16, 72), Size = new Size(508, 22), ForeColor = Color.FromArgb(0x9a, 0x9a, 0x9a), Text = "http://127.0.0.1:" + server.Port + "/…", RightToLeft = RightToLeft.No };
            Func<string, int, Button> B = (text, x) => new Button { Text = text, Location = new Point(x, 130), Size = new Size(164, 34), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(0x33, 0x33, 0x33), ForeColor = ForeColor };
            Button open = B(L("Rouvrir dans le navigateur", "Open in the browser again", "Открыть в браузере снова", "افتحه في المتصفح مرة أخرى"), 16);
            Button copy = B(L("Copier l'adresse", "Copy the address", "Скопировать адрес", "انسخ العنوان"), 188);
            Button quit = B(L("Fermer l'éditeur", "Close the editor", "Закрыть редактор", "أغلق المحرر"), 360);
            open.Click += (s, e) => OpenBrowser();
            copy.Click += (s, e) => { try { Clipboard.SetText(server.Url); } catch { } };
            quit.Click += (s, e) => Close();
            Controls.AddRange(new Control[] { t1, t2, urlLabel, open, copy, quit });
            ResumeLayout();
            OpenBrowser();
            System.Diagnostics.Trace.WriteLine("editeur : mode navigateur (" + why + ") " + server.Url);
        }

        void OpenBrowser()
        {
            if (Environment.GetEnvironmentVariable("EDITEUR_SANS_NAVIGATEUR") == "1") return;   // (essais : le navigateur d'essai ouvre l'adresse)
            try { Process.Start(new ProcessStartInfo(server.Url) { UseShellExecute = true }); }
            catch { if (urlLabel != null) urlLabel.Text = server.Url; }   // (pas de navigateur par defaut : l'adresse a copier)
        }

        // (boites de dialogue en mode navigateur : la fenetre du programme passe devant le navigateur le temps du choix)
        void Front(bool on)
        {
            if (server == null) return;
            if (on) { if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal; TopMost = true; Activate(); }
            else TopMost = false;
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
            if (m.Msg == WM_NCHITTEST && WindowState == FormWindowState.Normal && server == null && !Program.Browser)
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
                StartBrowser("WebView2 : " + e.Message);   // (pas de WebView2 : la page dans le navigateur, plus de message d'erreur)
                return;
            }
            CoreWebView2 w = web.CoreWebView2;
            // cache du navigateur vide a chaque lancement : tout est sur le disque (rien a gagner), et une carte preparee de
            // nouveau numerote ses textures autrement -- l'ancien cache les melangeait (retour de JD, 21/10) ; idem pour l'interface
            // apres une mise a jour de l'editeur
            try { await w.Profile.ClearBrowsingDataAsync(CoreWebView2BrowsingDataKinds.DiskCache); } catch { }
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
            if (server != null) { server.Send(json.Serialize(msg)); return; }
            if (web.IsDisposed || web.CoreWebView2 == null) return;
            web.CoreWebView2.PostWebMessageAsJson(json.Serialize(msg));
        }

        // ---------------------------------------------------------------- messages de la page
        void OnMessage(object sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            Dictionary<string, object> m;
            try { m = json.Deserialize<Dictionary<string, object>>(e.WebMessageAsJson); } catch { return; }
            string dropped = null;
            if (S(m, "type") == "deposer" && e.AdditionalObjects != null)   // (fichier glisse sur la fenetre : chemin donne par WebView2)
                foreach (object o in e.AdditionalObjects) { var f = o as CoreWebView2File; if (f != null) { dropped = f.Path; break; } }
            OnPage(m, dropped);
        }

        // (messages de la page, par WebView2 ou par le serveur local ; dropped : fichier glisse, WebView2 seulement)
        void OnPage(Dictionary<string, object> m, string dropped)
        {
            string type = S(m, "type");
            switch (type)
            {
                case "pret": SendState(); break;
                case "fenetre": if (server == null) Window(S(m, "action")); break;
                case "choisirExe": PickExe(S(m, "jeu")); break;
                case "deposer": if (dropped != null) CheckPick(S(m, "jeu"), dropped); break;
                case "verifier": CheckPick(S(m, "jeu"), S(m, "chemin")); break;
                case "valider": Validate(S(m, "jeu"), S(m, "chemin"), m.ContainsKey("retenir") && m["retenir"] is bool && (bool)m["retenir"]); break;
                case "reessayer": SendState(); break;
                case "preparer": StartPrep(S(m, "jeu")); break;
                case "annuler": if (prep != null) prep.Cancelled = true; break;
                case "dossier": OpenFolder(S(m, "chemin")); break;
                // ---- projets de carte (V1)
                case "projets": Post(new Dictionary<string, object> { { "type", "projets" }, { "liste", Projects.List() } }); break;
                case "gererProjet":   // renommer, dupliquer, retirer du jeu, supprimer (corbeille) ; la liste revient a jour
                    {
                        string a = S(m, "action"), d = S(m, "dossier"), err = null;
                        try
                        {
                            if (a == "renommer") Projects.Rename(d, S(m, "nom"));
                            else if (a == "dupliquer") Projects.Duplicate(d, S(m, "suffixe"));
                            else if (a == "retirer") Projects.RemoveFromGame(d);
                            else if (a == "supprimer") Projects.Delete(d, m.ContainsKey("carteAussi") && m["carteAussi"] is bool && (bool)m["carteAussi"]);
                        }
                        catch (Exception ex) { err = ex.Message; }
                        Post(new Dictionary<string, object> { { "type", "projets" }, { "liste", Projects.List() }, { "erreur", err ?? "" } });
                        break;
                    }
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
                case "copierFichiers":   // (coller dans une autre carte : modeles et images du projet d'origine)
                    {
                        int n = 0;
                        if (m["fichiers"] is System.Collections.ArrayList)
                            foreach (object f in (System.Collections.ArrayList)m["fichiers"]) try { if (Projects.CopyFile(S(m, "de"), S(m, "vers"), f as string)) n++; } catch { }
                        Post(new Dictionary<string, object> { { "type", "fichiersCopies" }, { "n", n } });
                        break;
                    }
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
                Front(true); bool ok = d.ShowDialog(this) == DialogResult.OK; Front(false);
                if (ok) CheckPick(id, d.FileName);
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
                Front(true); DialogResult res = d.ShowDialog(this); Front(false);
                if (res != DialogResult.OK) return;
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
                Front(true); DialogResult res = d.ShowDialog(this); Front(false);
                if (res != DialogResult.OK) return;
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
                // mise a jour sous un autre nom (carte renommee, autre version) : l'ancienne ne reste pas chargee en jeu en double
                string old = S(m, "ancien");
                if (mod != null && !string.IsNullOrEmpty(old) && old.IndexOfAny(new[] { '\\', '/', ':' }) < 0
                    && !string.Equals(old, Path.GetFileName(path), StringComparison.OrdinalIgnoreCase) && File.Exists(Path.Combine(mod, old)))
                    Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(Path.Combine(mod, old), Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs, Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
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
