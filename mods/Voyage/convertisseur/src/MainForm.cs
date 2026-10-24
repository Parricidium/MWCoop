using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace Convertisseur
{
    // Fenetre sans cadre comme l'editeur de carte (barre de titre dessinee par la page, coins arrondis de Windows 11).
    // Hotes virtuels : https://app.local (interface), https://ac.local (circuits d'Assetto Corsa : apercus), https://dest.local
    // (destinations converties : apercu 3D). Page <-> programme : messages JSON { type: ... }.
    public class MainForm : Form
    {
        readonly WebView2 web = new WebView2();
        readonly JavaScriptSerializer json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
        const int Grip = 5;
        string ac, mwc;   // (mwc : dossier du jeu choisi, My Winter Car ou My Summer Car)
        Ac.GameInfo game = Ac.Mwc;
        List<Track> tracks = new List<Track>();
        Conversion running;

        public MainForm()
        {
            Text = "Voyage · Convertisseur de circuits";
            FormBorderStyle = FormBorderStyle.None;
            BackColor = Color.FromArgb(0x26, 0x26, 0x26);
            Size = new Size(1320, 840);
            MinimumSize = new Size(1000, 640);
            StartPosition = FormStartPosition.CenterScreen;
            if (Environment.GetEnvironmentVariable("CONVERTISSEUR_HORS_ECRAN") == "1") { StartPosition = FormStartPosition.Manual; Location = new Point(-4000, 100); ShowInTaskbar = false; }
            Padding = new Padding(Grip);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            web.Dock = DockStyle.Fill;
            web.DefaultBackgroundColor = Color.FromArgb(0x1a, 0x1a, 0x1a);
            Controls.Add(web);
            Load += async (s, e) => await InitWeb();
        }

        protected override CreateParams CreateParams
        {
            get { CreateParams cp = base.CreateParams; cp.Style |= 0x00020000; cp.ClassStyle |= 0x00020000; return cp; }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            int round = 2; try { DwmSetWindowAttribute(Handle, 33, ref round, 4); } catch { }
            int dark = 1; try { DwmSetWindowAttribute(Handle, 20, ref dark, 4); } catch { }
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            Padding = WindowState == FormWindowState.Maximized ? new Padding(0) : new Padding(Grip);
            Post(new Dictionary<string, object> { { "type", "fenetre" }, { "agrandie", WindowState == FormWindowState.Maximized } });
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x84 && WindowState == FormWindowState.Normal)
            {
                Point p = PointToClient(new Point((short)(m.LParam.ToInt64() & 0xFFFF), (short)((m.LParam.ToInt64() >> 16) & 0xFFFF)));
                int c = Grip * 2;
                bool l = p.X < c, r = p.X >= ClientSize.Width - c, t = p.Y < c, b = p.Y >= ClientSize.Height - c;
                m.Result = (IntPtr)(t && l ? 13 : t && r ? 14 : b && l ? 16 : b && r ? 17 : l ? 10 : r ? 11 : t ? 12 : b ? 15 : 1);
                return;
            }
            base.WndProc(ref m);
        }

        async System.Threading.Tasks.Task InitWeb()
        {
            try
            {
                string data = Path.Combine(Ac.ConfDir, "WebView2");
                Directory.CreateDirectory(data);
                var env = await CoreWebView2Environment.CreateAsync(null, data);
                await web.EnsureCoreWebView2Async(env);
            }
            catch (Exception e)
            {
                MessageBox.Show(this, "WebView2 (Microsoft Edge) est introuvable sur ce PC.\n\nInstalle « Microsoft Edge WebView2 Runtime », puis relance le convertisseur.\n\n(" + e.Message + ")", "Convertisseur Voyage", MessageBoxButtons.OK, MessageBoxIcon.Error);
                Close(); return;
            }
            CoreWebView2 w = web.CoreWebView2;
            try { await w.Profile.ClearBrowsingDataAsync(CoreWebView2BrowsingDataKinds.DiskCache); } catch { }
            w.Settings.AreDevToolsEnabled = Program.Debug;
            w.Settings.AreDefaultContextMenusEnabled = Program.Debug;
            w.Settings.IsStatusBarEnabled = false;
            w.Settings.IsZoomControlEnabled = false;
            w.Settings.AreBrowserAcceleratorKeysEnabled = Program.Debug;
            w.SetVirtualHostNameToFolderMapping("app.local", Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "ui"), CoreWebView2HostResourceAccessKind.Allow);
            ac = Ac.FindAc();
            string jeu = Program.Game ?? Ac.IniGet(Ac.ConfIni, "Dossiers", "Jeu");
            game = !string.IsNullOrEmpty(jeu) ? Ac.GameOf(jeu) : Ac.FindGame(Ac.Mwc) != null || Ac.FindGame(Ac.Msc) == null ? Ac.Mwc : Ac.Msc;
            mwc = Ac.CheckGame(game, Program.GameDir) ? Program.GameDir.TrimEnd('\\', '/') : Ac.FindGame(game);
            MapHosts();
            w.WebMessageReceived += OnMessage;
            w.NewWindowRequested += (s, e) => { e.Handled = true; try { Process.Start(e.Uri); } catch { } };
            w.Navigate("https://app.local/index.html");
        }

        void MapHosts()
        {
            CoreWebView2 w = web.CoreWebView2;
            try { w.ClearVirtualHostNameToFolderMapping("ac.local"); } catch { }
            try { w.ClearVirtualHostNameToFolderMapping("dest.local"); } catch { }
            if (ac != null) w.SetVirtualHostNameToFolderMapping("ac.local", Path.Combine(ac, @"content\tracks"), CoreWebView2HostResourceAccessKind.Allow);
            if (mwc != null) { Directory.CreateDirectory(Ac.DestinationsDir(mwc)); w.SetVirtualHostNameToFolderMapping("dest.local", Ac.DestinationsDir(mwc), CoreWebView2HostResourceAccessKind.Allow); }
        }

        void Post(Dictionary<string, object> msg)
        {
            if (IsDisposed) return;
            if (InvokeRequired) { try { BeginInvoke(new Action(() => Post(msg))); } catch (InvalidOperationException) { } return; }
            if (web.CoreWebView2 == null) return;
            web.CoreWebView2.PostWebMessageAsJson(json.Serialize(msg));
        }

        static string S(Dictionary<string, object> m, string k) { object v; return m.TryGetValue(k, out v) && v != null ? v.ToString() : ""; }
        static int N(Dictionary<string, object> m, string k) { int v; int.TryParse(S(m, k), out v); return v; }

        void OnMessage(object sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            Dictionary<string, object> m;
            try { m = json.Deserialize<Dictionary<string, object>>(e.WebMessageAsJson); } catch { return; }
            try
            {
                switch (S(m, "type"))
                {
                    case "pret": SendState(true); break;
                    case "fenetre":
                        string a = S(m, "action");
                        if (a == "reduire") WindowState = FormWindowState.Minimized;
                        else if (a == "agrandir") WindowState = WindowState == FormWindowState.Maximized ? FormWindowState.Normal : FormWindowState.Maximized;
                        else if (a == "fermer") Close();
                        else if (a == "deplacer") { ReleaseCapture(); SendMessage(Handle, 0xA1, (IntPtr)2, IntPtr.Zero); }
                        break;
                    case "choisirDossier": PickFolder(S(m, "quoi")); break;
                    case "actualiser": SendState(true); break;
                    case "convertir": Start(m); break;
                    case "annuler": if (running != null) running.Cancel = true; break;
                    case "modifier":
                        Store.Edit(Ac.DestinationsDir(mwc), S(m, "id"), S(m, "nom"), N(m, "prix"), S(m, "point"));
                        SendState(false); Post(new Dictionary<string, object> { { "type", "toast" }, { "cle", "enregistre" } });
                        break;
                    case "supprimer": Store.Delete(Ac.DestinationsDir(mwc), S(m, "id")); SendState(false); break;
                    case "ouvrirDossier":
                    {
                        string d = S(m, "id").Length > 0 ? Path.Combine(Ac.DestinationsDir(mwc), S(m, "id")) : S(m, "quoi") == "ac" ? ac : Ac.DestinationsDir(mwc);
                        if (d != null && Directory.Exists(d)) Process.Start("explorer.exe", "\"" + d + "\"");
                        break;
                    }
                    case "lien": try { Process.Start(S(m, "url")); } catch { } break;
                    case "installerMod": InstallMod(); break;
                    case "jeu":   // (My Summer Car / My Winter Car : chacun ses destinations, dans son dossier Mods)
                        game = Ac.GameOf(S(m, "id"));
                        Ac.Remember("Jeu", game.Id);
                        mwc = Ac.FindGame(game);
                        MapHosts();
                        SendState(false);
                        break;
                }
            }
            catch (Exception ex) { Post(new Dictionary<string, object> { { "type", "erreur" }, { "texte", ex.Message } }); }
        }

        void PickFolder(string what)
        {
            using (var d = new FolderBrowserDialog { Description = what == "ac" ? "Dossier d'Assetto Corsa (celui qui contient « content »)" : "Dossier de " + game.Name + " (celui qui contient " + game.Exe + ")", ShowNewFolderButton = false })
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                if (what == "ac") { if (!Ac.CheckAc(d.SelectedPath)) { Post(new Dictionary<string, object> { { "type", "toast" }, { "cle", "pasAc" } }); return; } ac = d.SelectedPath; Ac.Remember("AssettoCorsa", ac); }
                else { if (!Ac.CheckGame(game, d.SelectedPath)) { Post(new Dictionary<string, object> { { "type", "toast" }, { "cle", "pasMwc" } }); return; } mwc = d.SelectedPath; Ac.Remember(game.Key, mwc); }
                MapHosts();
                SendState(true);
            }
        }

        // ---------------------------------------------------------------- etat : dossiers, circuits, destinations
        void SendState(bool rescan)
        {
            if (rescan) tracks = ac != null ? Ac.Tracks(ac) : new List<Track>();
            string destRoot = mwc != null ? Ac.DestinationsDir(mwc) : null;
            var dests = Store.List(destRoot, ac);
            var converted = new HashSet<string>();
            foreach (var d in dests) converted.Add((string)d["id"]);
            var tl = new List<object>();
            foreach (Track t in tracks)
            {
                var ls = new List<object>();
                foreach (Layout l in t.Layouts)
                    ls.Add(new Dictionary<string, object>
                    {
                        { "id", l.Id }, { "nom", l.Name }, { "pays", l.Country }, { "ville", l.City }, { "auteur", l.Author }, { "description", l.Description },
                        { "longueur", l.Length }, { "stands", l.Pitboxes }, { "version", l.Version },
                        { "apercu", l.Preview != null ? Url(l.Preview) : "" }, { "plan", l.Outline != null ? Url(l.Outline) : "" },
                        { "converti", converted.Contains(Conversion.IdOf(t, l)) }, { "dest", Conversion.IdOf(t, l) },
                    });
                tl.Add(new Dictionary<string, object> { { "id", t.Id }, { "taille", t.Size }, { "configs", ls }, { "officiel", Ac.IsKunos(t.Id) } });
            }
            Post(new Dictionary<string, object>
            {
                { "type", "etat" }, { "ac", ac ?? "" }, { "mwc", mwc ?? "" }, { "destDir", destRoot ?? "" }, { "circuits", tl }, { "destinations", dests },
                { "modVoyage", ModState() }, { "mscloader", Ac.HasLoader(game, mwc) },
                { "jeu", game.Id }, { "jeuNom", game.Name }, { "autreJeu", Ac.FindGame(game == Ac.Mwc ? Ac.Msc : Ac.Mwc) != null },
                { "langue", Program.Lang ?? "" },
            });
        }

        // ---------------------------------------------------------------- mod Voyage (livre a cote du convertisseur : mod\Voyage.dll)
        static string Bundled { get { return Path.Combine(Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "mod"), "Voyage.dll"); } }

        // « ok », « ancien » (pas celui livre avec ce convertisseur), « absent » ; « inconnu » sans jeu
        string ModState()
        {
            if (mwc == null) return "inconnu";
            string f = Path.Combine(mwc, @"Mods\Voyage.dll");
            if (!File.Exists(f)) return "absent";
            if (!File.Exists(Bundled)) return "ok";
            try { return Same(f, Bundled) ? "ok" : "ancien"; } catch { return "ok"; }
        }

        static bool Same(string a, string b)
        {
            var fa = new FileInfo(a); var fb = new FileInfo(b);
            if (fa.Length != fb.Length) return false;
            byte[] x = File.ReadAllBytes(a), y = File.ReadAllBytes(b);
            for (int i = 0; i < x.Length; i++) if (x[i] != y[i]) return false;
            return true;
        }

        void InstallMod()
        {
            if (mwc == null || !File.Exists(Bundled)) return;
            string dir = Path.Combine(mwc, "Mods");
            Directory.CreateDirectory(dir);
            File.Copy(Bundled, Path.Combine(dir, "Voyage.dll"), true);
            SendState(false);
            Post(new Dictionary<string, object> { { "type", "toast" }, { "cle", "modInstalle" } });
        }

        string Url(string file)
        {
            string root = Path.Combine(ac, @"content\tracks") + "\\";
            return "https://ac.local/" + file.Substring(root.Length).Replace('\\', '/');
        }

        // ---------------------------------------------------------------- conversion (fil a part)
        void Start(Dictionary<string, object> m)
        {
            if (running != null) return;
            if (mwc == null) { Post(new Dictionary<string, object> { { "type", "toast" }, { "cle", "pasMwc" } }); return; }
            Track t = tracks.Find(x => x.Id == S(m, "circuit"));
            Layout l = t != null ? t.Layouts.Find(x => x.Id == S(m, "config")) : null;
            if (l == null) return;
            var o = new Options { MaxTexture = Math.Max(256, N(m, "textures")), Arrival = S(m, "point").Length > 0 ? S(m, "point") : "pit", Price = N(m, "prix"), Name = S(m, "nom"), Decor = S(m, "decor") != "False" };
            var c = running = new Conversion();
            string dest = Ac.DestinationsDir(mwc);
            c.Progress = (st, f, txt) => Post(new Dictionary<string, object> { { "type", "progression" }, { "etape", st }, { "f", f }, { "texte", txt } });
            c.Log = s => Post(new Dictionary<string, object> { { "type", "journal" }, { "texte", s } });
            var th = new Thread(() =>
            {
                var sw = Stopwatch.StartNew();
                try
                {
                    Conversion.Stats st = c.Run(t, l, dest, o);
                    Post(new Dictionary<string, object> { { "type", "fini" }, { "id", Conversion.IdOf(t, l) }, { "ms", sw.ElapsedMilliseconds }, { "triangles", st.Triangles }, { "octets", st.Bytes }, { "textures", st.Textures }, { "grises", st.TexturesGrey } });
                }
                catch (OperationCanceledException) { Post(new Dictionary<string, object> { { "type", "annule" } }); }
                catch (Exception ex) { Post(new Dictionary<string, object> { { "type", "echec" }, { "texte", ex.Message } }); }
                finally { running = null; BeginInvoke(new Action(() => SendState(false))); GC.Collect(); }
            }) { IsBackground = true, Name = "conversion" };
            th.Start();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (running != null) running.Cancel = true;
            base.OnFormClosing(e);
        }

        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
        [DllImport("user32.dll")] static extern bool ReleaseCapture();
        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, IntPtr l);
    }
}
