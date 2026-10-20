using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Web.Script.Serialization;

namespace EditeurCarte
{
    // Projets de carte (V1, 20/10) : Documents\My Seasons Car\Projets\<dossier>\projet.json (objets places, objets du jeu
    // masques ; le format est ecrit par la page : ui/edition.js) et modeles\ (fichiers 3D importes, avec leurs textures).
    // Compiler : la page envoie le paquet deja assemble (carte.json, maillages des modeles, textures) ; il est ecrit en
    // .mwcmap (un zip) dans Documents\My Seasons Car\Cartes et dans le dossier des cartes de MWCoop pour ce jeu.
    public static class Projects
    {
        public static string Root
        {
            get
            {
                string o = Environment.GetEnvironmentVariable("EDITEUR_PROJETS");
                if (!string.IsNullOrEmpty(o)) return o;
                return Path.Combine(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "My Seasons Car"), "Projets");
            }
        }
        public static string CompiledRoot { get { return Path.Combine(Path.GetDirectoryName(Root), "Cartes"); } }

        static readonly JavaScriptSerializer json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };

        public static List<object> List()
        {
            var l = new List<object>();
            if (!Directory.Exists(Root)) return l;
            foreach (string d in Directory.GetDirectories(Root))
            {
                string pj = Path.Combine(d, "projet.json");
                if (!File.Exists(pj)) continue;
                try
                {
                    var m = json.Deserialize<Dictionary<string, object>>(File.ReadAllText(pj, Encoding.UTF8));
                    object o; int n = m.TryGetValue("objets", out o) && o is System.Collections.ArrayList ? ((System.Collections.ArrayList)o).Count : 0;
                    l.Add(new Dictionary<string, object> { { "dossier", Path.GetFileName(d) }, { "nom", m.ContainsKey("nom") ? m["nom"] : Path.GetFileName(d) },
                                                           { "jeu", m.ContainsKey("jeu") ? m["jeu"] : "" }, { "objets", n },
                                                           { "modifie", File.GetLastWriteTime(pj).ToString("dd/MM/yyyy HH:mm") }, { "t", File.GetLastWriteTimeUtc(pj).Ticks } });
                }
                catch { }
            }
            l.Sort((a, b) => ((long)((Dictionary<string, object>)b)["t"]).CompareTo((long)((Dictionary<string, object>)a)["t"]));
            return l;
        }

        // Nom de dossier sur a partir du nom de la carte (accents gardes, caracteres interdits retires), unique.
        public static string Create(string name, string game)
        {
            Directory.CreateDirectory(Root);
            var sb = new StringBuilder();
            foreach (char c in name.Trim()) sb.Append(Path.GetInvalidFileNameChars().Contains(c) || c == '.' ? '-' : c);
            string baseName = sb.ToString().Trim('-', ' ');
            if (baseName.Length == 0) baseName = "carte";
            if (baseName.Length > 48) baseName = baseName.Substring(0, 48);
            string dir = baseName;
            for (int i = 2; Directory.Exists(Path.Combine(Root, dir)); i++) dir = baseName + " " + i;
            string full = Path.Combine(Root, dir);
            Directory.CreateDirectory(Path.Combine(full, "modeles"));
            var p = new Dictionary<string, object> { { "format", 1 }, { "nom", name.Trim() }, { "jeu", game }, { "auteur", "" }, { "version", "1.0" }, { "description", "" },
                                                     { "cree", DateTime.Now.ToString("yyyy-MM-dd HH:mm") }, { "objets", new object[0] }, { "masques", new object[0] } };
            File.WriteAllText(Path.Combine(full, "projet.json"), json.Serialize(p), new UTF8Encoding(false));
            return dir;
        }

        public static string Dir(string dossier)
        {
            if (string.IsNullOrEmpty(dossier) || dossier.IndexOfAny(new[] { '\\', '/', ':' }) >= 0 || dossier.Contains("..")) return null;
            string d = Path.Combine(Root, dossier);
            return Directory.Exists(d) ? d : null;
        }

        public static string Read(string dossier)
        {
            string d = Dir(dossier);
            return d != null ? File.ReadAllText(Path.Combine(d, "projet.json"), Encoding.UTF8) : null;
        }

        public static bool Save(string dossier, string data)
        {
            string d = Dir(dossier);
            if (d == null || string.IsNullOrEmpty(data)) return false;
            string p = Path.Combine(d, "projet.json"), tmp = p + ".tmp";
            File.WriteAllText(tmp, data, new UTF8Encoding(false));
            if (File.Exists(p)) File.Replace(tmp, p, null); else File.Move(tmp, p);
            return true;
        }

        // Modele importe : copie dans modeles\ (nom unique), avec les images du meme dossier (textures d'un .obj / .fbx),
        // 60 Mo au plus. Rend le nom du fichier copie.
        public static string ImportModel(string dossier, string file)
        {
            string d = Dir(dossier);
            if (d == null || !File.Exists(file)) return null;
            string md = Path.Combine(d, "modeles");
            Directory.CreateDirectory(md);
            string name = Path.GetFileNameWithoutExtension(file), ext = Path.GetExtension(file).ToLowerInvariant(), dst = name + ext;
            for (int i = 2; File.Exists(Path.Combine(md, dst)); i++) dst = name + "-" + i + ext;
            File.Copy(file, Path.Combine(md, dst));
            long total = new FileInfo(file).Length;
            string src = Path.GetDirectoryName(file);
            var copy = new List<string>();
            foreach (string f in Directory.GetFiles(src))
            {
                string e = Path.GetExtension(f).ToLowerInvariant();
                if (e == ".mtl" || e == ".png" || e == ".jpg" || e == ".jpeg" || e == ".tga" || e == ".bmp" || e == ".bin") copy.Add(f);
            }
            foreach (string sub in new[] { "textures", "Textures", "tex" })
                if (Directory.Exists(Path.Combine(src, sub))) foreach (string f in Directory.GetFiles(Path.Combine(src, sub))) copy.Add(f);
            foreach (string f in copy)
            {
                total += new FileInfo(f).Length;
                if (total > 60L << 20) break;
                string rel = f.Substring(src.Length + 1), to = Path.Combine(md, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(to));
                if (!File.Exists(to)) File.Copy(f, to);
            }
            return dst;
        }

        // Paquet compile : fichiers envoyes par la page (nom -> base64), zippes en <slug>-<version>.mwcmap.
        public static string Compile(string dossier, string fileName, Dictionary<string, object> files, string modCartes)
        {
            Directory.CreateDirectory(CompiledRoot);
            string outPath = Path.Combine(CompiledRoot, fileName);
            string tmp = outPath + ".tmp";
            if (File.Exists(tmp)) File.Delete(tmp);
            using (var fs = File.Create(tmp))
            using (var z = new ZipArchive(fs, ZipArchiveMode.Create))
                foreach (var kv in files)
                {
                    string n = kv.Key.Replace('\\', '/');
                    if (n.Contains("..") || n.StartsWith("/")) continue;
                    var e = z.CreateEntry(n, CompressionLevel.Optimal);   // (deflate lu par Inflate.cs du mod : le Mono d'Unity 5.0 n'a pas zlib)
                    byte[] b = Convert.FromBase64String(kv.Value as string ?? "");
                    using (var s = e.Open()) s.Write(b, 0, b.Length);
                }
            if (File.Exists(outPath)) File.Delete(outPath);
            File.Move(tmp, outPath);
            if (!string.IsNullOrEmpty(modCartes))
            {
                Directory.CreateDirectory(modCartes);
                File.Copy(outPath, Path.Combine(modCartes, fileName), true);
            }
            return outPath;
        }
    }

    static class Ext { public static bool Contains(this char[] a, char c) { return Array.IndexOf(a, c) >= 0; } }
}
