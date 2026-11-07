using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace MWCoop
{
    // Carnet de notes du copilote (idee et choix de JD, 07/11 ; spec MWCoop-carnet\SPEC-carnet-copilote.md) : les donnees.
    //  - speciales : celles du KARTTURI (Kartturi.cs : trace tous les 2 m, coupe a l'arrivee, notes calculees), avec en plus le
    //    carre, l'enchaine (moins de 20 m jusqu'a la note suivante), l'arrivee et le pont (notes par defaut).
    //  - fichier carnet (format cale avec [Launcher] le 07/11, memes regles des deux cotes) : %LOCALAPPDATA%\MWCoop\carnets\
    //    <fichier>.json ; le carnet actif dans carnet-actif-<jeu>.txt (nom du fichier seul ; vide ou absent : notes automatiques).
    //    Une speciale absente du carnet prend les notes automatiques ; presente (meme vide), seulement les siennes. Fichier hors
    //    format : refuse en entier (cle inconnue ignoree, valeur inconnue refusee, speciale inconnue ignoree).
    //  - export des traces pour l'editeur du lanceur : speciales-<jeu>.json, au premier monde charge puis a chaque version du mod :
    //    par speciale, id, nom, jour (6 samedi, 7 dimanche), longueur, points x/z tous les 2 m, notes automatiques.
    //  [Test] RacineVisages : dossier de ces fichiers pour une instance d'essai (comme les casques et les visages).
    public static class Carnet
    {
        // ---------------------------------------------------------------- notes
        public const int MLong = 1, MResserre = 2, MOuvre = 4, MNc = 8, MDanger = 16, MEnchaine = 32;
        static readonly string[] modNames = { "long", "resserre", "ouvre", "nc", "danger", "enchaine" };
        public static readonly string[] Types = { "virage", "carre", "bosse", "pont", "danger", "arrivee", "droit", "texte" };
        public const int MaxNotes = 1000, MaxBytes = 256 * 1024, MaxText = 40, MaxM = 100000;

        // Side : -1 gauche, 1 droite (virage, carre) ; Grade : 0 = H, 1 a 6 (virage) ; Mods : bits M*
        public class Note { public int M; public string Type; public int Side; public int Grade; public int Mods; public string Text = ""; }
        public class Book { public string Name = "Mes notes", Game = ""; public readonly Dictionary<string, List<Note>> Stages = new Dictionary<string, List<Note>>(); }

        public static string GameId { get { return Game.Msc ? "MSC" : "MWC"; } }
        // speciales de chaque jeu (cles du carnet) et leur jour
        static readonly string[] StagesMsc = { "SS1", "SS2" }, StagesMwc = { "SS1", "SS2", "SS3" };
        static readonly int[] DaysMsc = { 6, 7 }, DaysMwc = { 6, 6, 7 };
        static bool KnownStage(string game, string id) { return Array.IndexOf(game == "MSC" ? StagesMsc : StagesMwc, id) >= 0; }
        static int DayOf(int k) { int[] d = Game.Msc ? DaysMsc : DaysMwc; return k >= 0 && k < d.Length ? d[k] : 6; }

        // tri par m, puis ordre d'origine (stable)
        static List<Note> Sorted(List<Note> l)
        {
            var idx = new List<KeyValuePair<Note, int>>();
            for (int i = 0; i < l.Count; i++) idx.Add(new KeyValuePair<Note, int>(l[i], i));
            idx.Sort((a, b) => a.Key.M != b.Key.M ? a.Key.M.CompareTo(b.Key.M) : a.Value.CompareTo(b.Value));
            var o = new List<Note>(l.Count);
            foreach (KeyValuePair<Note, int> kv in idx) o.Add(kv.Key);
            return o;
        }

        // Notes automatiques de la speciale k (une fois par niveau) : virages et bosses du KARTTURI (m = entree du virage, sommet
        // de la bosse), carres, enchaine sous 20 m, pont(s), arrivee (m = longueur).
        static readonly Dictionary<int, List<Note>> autoCache = new Dictionary<int, List<Note>>();
        public static List<Note> AutoNotes(int k)
        {
            List<Note> l;
            if (autoCache.TryGetValue(k, out l)) return l;
            l = new List<Note>();
            float step = Kartturi.StepM;
            List<Kartturi.Note> src = Kartturi.StageNotes(k);
            for (int i = 0; i < src.Count; i++)
            {
                Kartturi.Note a = src[i];
                var n = new Note();
                if (a.Crest) { n.Type = "bosse"; n.M = Mathf.RoundToInt((a.I + a.J) * 0.5f * step); }
                else
                {
                    n.Type = a.Square ? "carre" : "virage"; n.Side = a.Side < 0 ? -1 : 1; n.Grade = a.Grade; n.M = Mathf.RoundToInt(a.I * step);
                    if (!a.Square)
                    {
                        if (a.Long) n.Mods |= MLong;
                        if (a.Tightens) n.Mods |= MResserre; else if (a.Opens) n.Mods |= MOuvre;
                    }
                }
                if (i + 1 < src.Count && (src[i + 1].I - a.J) * step < 20f) n.Mods |= MEnchaine;
                l.Add(n);
            }
            foreach (int b in Kartturi.StageBridges(k)) l.Add(new Note { Type = "pont", M = Mathf.RoundToInt(b * step) });
            Vector3[] p = Kartturi.StagePoints(k);
            l.Add(new Note { Type = "arrivee", M = Mathf.RoundToInt((p.Length - 1) * step) });
            l = Sorted(l);
            autoCache[k] = l;
            return l;
        }

        // Notes de la speciale k pour un carnet (le mien ou celui d'un autre joueur ; null : notes automatiques).
        public static List<Note> NotesFor(int k, Book book)
        {
            List<Note> l;
            if (book != null && book.Stages.TryGetValue(Kartturi.StageName(k), out l)) return l;
            return AutoNotes(k);
        }

        // ---------------------------------------------------------------- lecture (stricte) d'un carnet
        public static Book Parse(string text, out string err)
        {
            err = null;
            try
            {
                if (text == null) throw new FormatException("vide");
                if (Encoding.UTF8.GetByteCount(text) > MaxBytes) throw new FormatException("plus de 256 Ko");
                if (text.Length > 0 && text[0] == '﻿') text = text.Substring(1);
                var root = new JsonReader(text).Parse() as Dictionary<string, object>;
                if (root == null) throw new FormatException("pas un objet JSON");
                if (Int(root, "v") != 1) throw new FormatException("v n'est pas 1");
                string game = Str(root, "jeu");
                if (game != "MSC" && game != "MWC") throw new FormatException("jeu inconnu « " + game + " »");
                string name = Str(root, "nom").Trim();
                if (name.Length < 1 || name.Length > MaxText || HasControl(name)) throw new FormatException("nom : 1 a 40 caracteres");
                var sp = Need(root, "speciales") as Dictionary<string, object>;
                if (sp == null) throw new FormatException("speciales : objet attendu");
                var book = new Book { Name = name, Game = game };
                foreach (KeyValuePair<string, object> kv in sp)
                {
                    if (!KnownStage(game, kv.Key)) continue;   // (speciale inconnue : ignoree, pas lue)
                    var so = kv.Value as Dictionary<string, object>;
                    if (so == null) throw new FormatException(kv.Key + " : objet attendu");
                    var arr = Need(so, "notes") as List<object>;
                    if (arr == null) throw new FormatException(kv.Key + " : notes, liste attendue");
                    if (arr.Count > MaxNotes) throw new FormatException(kv.Key + " : plus de " + MaxNotes + " notes");
                    var list = new List<Note>(arr.Count);
                    for (int i = 0; i < arr.Count; i++) list.Add(ParseNote(arr[i] as Dictionary<string, object>, kv.Key + " note " + (i + 1)));
                    book.Stages[kv.Key] = Sorted(list);
                }
                return book;
            }
            catch (FormatException e) { err = e.Message; return null; }
            catch (Exception e) { err = e.GetType().Name + " : " + e.Message; return null; }
        }

        static Note ParseNote(Dictionary<string, object> o, string where)
        {
            if (o == null) throw new FormatException(where + " : objet attendu");
            var n = new Note { M = Int(o, "m"), Type = Str(o, "type") };
            if (n.M < 0 || n.M > MaxM) throw new FormatException(where + " : m hors de 0 a " + MaxM);
            if (Array.IndexOf(Types, n.Type) < 0) throw new FormatException(where + " : type inconnu « " + n.Type + " »");
            if (n.Type == "virage" || n.Type == "carre")
            {
                string s = Str(o, "sens");
                if (s != "g" && s != "d") throw new FormatException(where + " : sens « " + s + " » (g ou d)");
                n.Side = s == "g" ? -1 : 1;
            }
            if (n.Type == "virage") { n.Grade = Int(o, "cote"); if (n.Grade < 0 || n.Grade > 6) throw new FormatException(where + " : cote hors de 0 a 6"); }
            if (n.Type == "texte")
            {
                n.Text = Str(o, "texte").Trim();
                if (n.Text.Length < 1 || n.Text.Length > MaxText || HasControl(n.Text)) throw new FormatException(where + " : texte de 1 a 40 caracteres");
            }
            object mo;
            if (o.TryGetValue("mods", out mo))
            {
                var ml = mo as List<object>;
                if (ml == null) throw new FormatException(where + " : mods, liste attendue");
                foreach (object x in ml)
                {
                    string ms = x as string;
                    int bit = ms != null ? Array.IndexOf(modNames, ms) : -1;
                    if (bit < 0) throw new FormatException(where + " : modificateur inconnu « " + ms + " »");
                    if ((n.Mods & (1 << bit)) != 0) throw new FormatException(where + " : modificateur « " + ms + " » en double");
                    n.Mods |= 1 << bit;
                }
                if ((n.Mods & MResserre) != 0 && (n.Mods & MOuvre) != 0) throw new FormatException(where + " : resserre et ouvre ensemble");
            }
            return n;
        }

        static object Need(Dictionary<string, object> o, string k) { object v; if (!o.TryGetValue(k, out v)) throw new FormatException(k + " absent"); return v; }
        static string Str(Dictionary<string, object> o, string k) { string s = Need(o, k) as string; if (s == null) throw new FormatException(k + " : texte attendu"); return s; }
        static int Int(Dictionary<string, object> o, string k)
        {
            object v = Need(o, k);
            if (!(v is double)) throw new FormatException(k + " : nombre attendu");
            double d = (double)v;
            if (d != Math.Floor(d) || d < int.MinValue || d > int.MaxValue) throw new FormatException(k + " : entier attendu");
            return (int)d;
        }
        static bool HasControl(string s) { foreach (char c in s) if (c < 0x20 || (c >= 0x7F && c <= 0x9F)) return true; return false; }

        // ---------------------------------------------------------------- ecriture
        public static string Write(Book b)
        {
            var sb = new StringBuilder();
            sb.Append("{\n  \"v\": 1,\n  \"jeu\": "); JStr(sb, b.Game); sb.Append(",\n  \"nom\": "); JStr(sb, b.Name); sb.Append(",\n  \"speciales\": {");
            var ids = new List<string>(b.Stages.Keys);
            ids.Sort(string.CompareOrdinal);
            for (int s = 0; s < ids.Count; s++)
            {
                sb.Append(s == 0 ? "\n    " : ",\n    "); JStr(sb, ids[s]); sb.Append(": { \"notes\": [");
                List<Note> l = b.Stages[ids[s]];
                for (int i = 0; i < l.Count; i++) { sb.Append(i == 0 ? "\n      " : ",\n      "); NoteJson(sb, l[i]); }
                sb.Append(l.Count > 0 ? "\n    ] }" : "] }");
            }
            sb.Append(ids.Count > 0 ? "\n  }\n}\n" : "}\n}\n");
            return sb.ToString();
        }

        static void NoteJson(StringBuilder sb, Note n)
        {
            sb.Append("{ \"m\": ").Append(n.M.ToString(CultureInfo.InvariantCulture)).Append(", \"type\": "); JStr(sb, n.Type);
            if (n.Type == "virage" || n.Type == "carre") sb.Append(", \"sens\": \"").Append(n.Side < 0 ? 'g' : 'd').Append('"');
            if (n.Type == "virage") sb.Append(", \"cote\": ").Append(n.Grade.ToString(CultureInfo.InvariantCulture));
            if (n.Mods != 0)
            {
                sb.Append(", \"mods\": [");
                bool first = true;
                for (int k = 0; k < modNames.Length; k++) if ((n.Mods & (1 << k)) != 0) { if (!first) sb.Append(", "); first = false; JStr(sb, modNames[k]); }
                sb.Append(']');
            }
            if (n.Type == "texte") { sb.Append(", \"texte\": "); JStr(sb, n.Text); }
            sb.Append(" }");
        }

        static void JStr(StringBuilder sb, string v)
        {
            sb.Append('"');
            foreach (char c in v ?? "")
            {
                if (c == '"') sb.Append("\\\"");
                else if (c == '\\') sb.Append("\\\\");
                else if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                else sb.Append(c);
            }
            sb.Append('"');
        }

        // ---------------------------------------------------------------- fichiers
        public static string Root { get { string o = Config.Get("Test", "RacineVisages", ""); return o.Length > 0 ? o : Path.Combine(Environment.GetEnvironmentVariable("LOCALAPPDATA") ?? ".", "MWCoop"); } }
        static string PtrFile { get { return Path.Combine(Root, "carnet-actif-" + GameId + ".txt"); } }
        static string LibDir { get { return Path.Combine(Root, "carnets"); } }

        public static Book Active;               // carnet actif lu (null : notes automatiques)
        public static string ActiveError = "";   // pourquoi il n'est pas lu (vide : pas de carnet actif, ou lu)
        static string activePath = "";
        static DateTime activeStamp;

        // nom de fichier du carnet actif (vide : aucun) ; refuse un chemin
        static string ReadPtr()
        {
            try
            {
                string p = PtrFile;
                if (!File.Exists(p)) return "";
                string n = File.ReadAllText(p);
                int nl = n.IndexOfAny(new[] { '\r', '\n' });
                if (nl >= 0) n = n.Substring(0, nl);
                n = n.Trim().TrimStart('﻿');
                if (n.Length == 0) return "";
                if (!ValidFileName(n)) { Log.Info("carnet : " + p + " : nom de fichier refuse « " + n + " »"); return ""; }
                return n;
            }
            catch (Exception e) { Log.Info("carnet : carnet actif illisible : " + e.Message); return ""; }
        }
        static bool ValidFileName(string n)
        {
            return n.Length <= 120 && n.EndsWith(".json", StringComparison.OrdinalIgnoreCase) && n.IndexOf("..", StringComparison.Ordinal) < 0
                && n.IndexOfAny(new[] { '\\', '/', ':', '*', '?', '"', '<', '>', '|' }) < 0;
        }

        // Relit le carnet actif si le fichier choisi ou sa date ont change (chargement du monde, carnet leve) ; force : toujours.
        public static void Refresh(bool force)
        {
            string name = ReadPtr();
            string path = name.Length > 0 ? Path.Combine(LibDir, name) : "";
            DateTime st = DateTime.MinValue;
            try { if (path.Length > 0 && File.Exists(path)) st = File.GetLastWriteTimeUtc(path); } catch (Exception) { }
            if (!force && path == activePath && st == activeStamp) return;
            activePath = path; activeStamp = st; Active = null; ActiveError = "";
            if (path.Length == 0) { Log.Info("carnet : pas de carnet actif (" + PtrFile + "), notes automatiques"); return; }
            if (st == DateTime.MinValue) { ActiveError = "introuvable"; Log.Info("carnet : carnet actif " + path + " introuvable, notes automatiques"); return; }
            try
            {
                if (new FileInfo(path).Length > MaxBytes) { ActiveError = "plus de 256 Ko"; Log.Info("carnet : " + name + " refuse (plus de 256 Ko), notes automatiques"); return; }
                string err;
                Book b = Parse(File.ReadAllText(path, Encoding.UTF8), out err);
                if (b == null) { ActiveError = err; Log.Info("carnet : " + name + " refuse (" + err + "), notes automatiques"); return; }
                if (b.Game != GameId) { ActiveError = "carnet de " + b.Game; Log.Info("carnet : " + name + " est un carnet " + b.Game + ", notes automatiques"); return; }
                Active = b;
                Log.Info("carnet : " + name + " « " + b.Name + " » lu, " + b.Stages.Count + " speciale(s) a lui");
            }
            catch (Exception e) { ActiveError = e.Message; Log.Info("carnet : " + name + " illisible (" + e.Message + "), notes automatiques"); }
        }

        // ecrit un fichier en .tmp puis le met a sa place
        static void WriteFile(string path, string text)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, text, new UTF8Encoding(false));
            File.Copy(tmp, path, true);
            File.Delete(tmp);
        }

        // ---------------------------------------------------------------- reconnaissance : notes posees et retirees en jeu
        // Ajoute une note (add) ou retire la plus proche de removeM (a 80 m au plus) dans la speciale k du carnet actif, puis
        // l'ecrit. Pas de carnet actif : carnets\carnet-<jeu>.json (« Mes notes »), inscrit comme carnet actif. Speciale absente du
        // carnet : ses notes automatiques y sont d'abord recopiees (sinon une note posee effacerait toutes les autres). Carnet actif
        // illisible : rien n'est touche (le fichier du joueur reste tel quel). what : ce qui a ete fait, pour l'ecran.
        public static int Changes;   // (augmente a chaque modification : envoi aux autres)
        public static bool Edit(int k, Note add, int removeM, out string what)
        {
            what = "";
            if (k < 0 || k >= Kartturi.StageCount) { what = Lang.T("hors spéciale", "not on a stage"); return false; }
            if (Active == null && ActiveError.Length > 0) { what = Lang.T("carnet actif illisible, rien n'est modifié", "active notebook unreadable, nothing changed") + " (" + ActiveError + ")"; return false; }
            try
            {
                if (Active == null)
                {
                    string name = "carnet-" + GameId + ".json";
                    for (int i = 2; File.Exists(Path.Combine(LibDir, name)) && i < 100; i++) name = "carnet-" + GameId + "-" + i + ".json";
                    Active = new Book { Name = Lang.T("Mes notes", "My notes"), Game = GameId };
                    activePath = Path.Combine(LibDir, name);
                    WriteFile(PtrFile, name);
                    Log.Info("carnet : nouveau carnet actif " + activePath);
                }
                string id = Kartturi.StageName(k);
                List<Note> l;
                if (!Active.Stages.TryGetValue(id, out l))
                {
                    l = new List<Note>();
                    foreach (Note a in AutoNotes(k)) l.Add(new Note { M = a.M, Type = a.Type, Side = a.Side, Grade = a.Grade, Mods = a.Mods, Text = a.Text });
                    Active.Stages[id] = l;
                }
                if (add != null)
                {
                    if (l.Count >= MaxNotes) { what = Lang.T("carnet plein", "notebook full"); return false; }
                    l.Add(add);
                    Active.Stages[id] = l = Sorted(l);
                    what = Lang.T("note posée", "note added");
                }
                else
                {
                    int best = -1; int bd = 81;
                    for (int i = 0; i < l.Count; i++) { int d = Math.Abs(l[i].M - removeM); if (d < bd) { bd = d; best = i; } }
                    if (best < 0) { what = Lang.T("pas de note à moins de 80 m", "no note within 80 m"); return false; }
                    what = Lang.T("note retirée", "note removed") + " (" + l[best].Type + ")";
                    l.RemoveAt(best);
                }
                WriteFile(activePath, Write(Active));
                activeStamp = File.GetLastWriteTimeUtc(activePath);
                Changes++;
                Log.Info("carnet : " + id + " " + (add != null ? "note " + add.Type + " posee a " + add.M + " m" : "note retiree pres de " + removeM + " m") + ", " + l.Count + " notes, " + activePath);
                return true;
            }
            catch (Exception e) { what = e.Message; Log.Warn("carnet : modification : " + e.Message); return false; }
        }

        // ---------------------------------------------------------------- envoi aux autres joueurs (binaire compact)
        // « CNB1 », nom, n speciales ; par speciale : id, n notes ; par note : m (int32), type (octet, index de Types), cote
        // (octet), sens (-1/1), mods (octet), texte (pour le type texte). Recu : memes controles que le fichier.
        public static byte[] Pack(Book b)
        {
            var ms = new MemoryStream();
            var w = new BinaryWriter(ms, Encoding.UTF8);
            w.Write(new[] { (byte)'C', (byte)'N', (byte)'B', (byte)'1' });
            w.Write(b.Name); w.Write(b.Game);
            var ids = new List<string>(b.Stages.Keys); ids.Sort(string.CompareOrdinal);
            w.Write((byte)ids.Count);
            foreach (string id in ids)
            {
                List<Note> l = b.Stages[id];
                w.Write(id); w.Write((ushort)l.Count);
                foreach (Note n in l)
                {
                    w.Write(n.M); w.Write((byte)Array.IndexOf(Types, n.Type)); w.Write((byte)n.Grade); w.Write((sbyte)(n.Side < 0 ? -1 : 1)); w.Write((byte)n.Mods);
                    if (n.Type == "texte") w.Write(n.Text);
                }
            }
            w.Flush();
            return ms.ToArray();
        }

        public static Book Unpack(byte[] data, out string err)
        {
            err = null;
            try
            {
                var r = new BinaryReader(new MemoryStream(data), Encoding.UTF8);
                byte[] mg = r.ReadBytes(4);
                if (mg.Length != 4 || mg[0] != 'C' || mg[1] != 'N' || mg[2] != 'B' || mg[3] != '1') throw new FormatException("en-tete");
                var b = new Book { Name = r.ReadString(), Game = r.ReadString() };
                if (b.Name.Length < 1 || b.Name.Length > MaxText || HasControl(b.Name)) throw new FormatException("nom");
                if (b.Game != "MSC" && b.Game != "MWC") throw new FormatException("jeu");
                int ns = r.ReadByte();
                for (int s = 0; s < ns; s++)
                {
                    string id = r.ReadString(); int nn = r.ReadUInt16();
                    if (!KnownStage(b.Game, id) || nn > MaxNotes || b.Stages.ContainsKey(id)) throw new FormatException("speciale " + id);
                    var l = new List<Note>(nn);
                    for (int i = 0; i < nn; i++)
                    {
                        var n = new Note { M = r.ReadInt32() };
                        int t = r.ReadByte(); n.Grade = r.ReadByte(); n.Side = r.ReadSByte(); n.Mods = r.ReadByte();
                        if (t >= Types.Length || n.M < 0 || n.M > MaxM || n.Grade > 6 || (n.Side != -1 && n.Side != 1) || (n.Mods & ~63) != 0 || ((n.Mods & MResserre) != 0 && (n.Mods & MOuvre) != 0)) throw new FormatException("note");
                        n.Type = Types[t];
                        if (n.Type == "texte") { n.Text = r.ReadString(); if (n.Text.Length < 1 || n.Text.Length > MaxText || HasControl(n.Text)) throw new FormatException("texte"); }
                        l.Add(n);
                    }
                    b.Stages[id] = Sorted(l);
                }
                if (r.BaseStream.Position != r.BaseStream.Length) throw new FormatException("octets en trop");
                return b;
            }
            catch (Exception e) { err = e.Message; return null; }
        }

        public static string Hash(byte[] b)
        {
            using (var md5 = System.Security.Cryptography.MD5.Create())
            {
                byte[] h = md5.ComputeHash(b);
                var sb = new StringBuilder();
                for (int i = 0; i < 6; i++) sb.Append(h[i].ToString("x2"));
                return sb.ToString();
            }
        }

        // ---------------------------------------------------------------- export des traces (lanceur)
        static bool exported;
        static void ExportStages()
        {
            exported = true;
            string f = Path.Combine(Root, "speciales-" + GameId + ".json");
            bool force = Config.GetInt("Test", "CarnetExport", 0) != 0;   // ([Test] CarnetExport=1 : reecrit a chaque niveau)
            try
            {
                if (!force && File.Exists(f))
                {
                    var old = new JsonReader(File.ReadAllText(f, Encoding.UTF8).TrimStart('﻿')).Parse() as Dictionary<string, object>;
                    object mv, jv;
                    if (old != null && old.TryGetValue("mod", out mv) && mv as string == Version.Text && old.TryGetValue("jeu", out jv) && jv as string == GameId) return;
                }
            }
            catch (Exception) { }   // (illisible : reecrit)
            try
            {
                var ci = CultureInfo.InvariantCulture;
                var sb = new StringBuilder();
                sb.Append("{ \"v\": 1, \"jeu\": \"").Append(GameId).Append("\", \"mod\": "); JStr(sb, Version.Text);
                sb.Append(", \"step\": ").Append(Kartturi.StepM.ToString(ci)).Append(",\n  \"speciales\": [");
                int n = Kartturi.StageCount;
                for (int k = 0; k < n; k++)
                {
                    Vector3[] p = Kartturi.StagePoints(k);
                    sb.Append(k == 0 ? "\n    { \"id\": " : ",\n    { \"id\": "); JStr(sb, Kartturi.StageName(k));
                    sb.Append(", \"nom\": "); JStr(sb, Kartturi.StageName(k));
                    sb.Append(", \"jour\": ").Append(DayOf(k)).Append(", \"longueur\": ").Append(Mathf.RoundToInt((p.Length - 1) * Kartturi.StepM)).Append(",\n      \"xz\": [");
                    for (int i = 0; i < p.Length; i++)
                    {
                        if (i > 0) sb.Append(i % 8 == 0 ? ",\n        " : ", ");
                        sb.Append(p[i].x.ToString("F1", ci)).Append(", ").Append(p[i].z.ToString("F1", ci));
                    }
                    sb.Append("],\n      \"notes\": [");
                    List<Note> l = AutoNotes(k);
                    for (int i = 0; i < l.Count; i++) { sb.Append(i == 0 ? "\n        " : ",\n        "); NoteJson(sb, l[i]); }
                    sb.Append(l.Count > 0 ? "\n      ] }" : "] }");
                }
                sb.Append("\n  ] }\n");
                WriteFile(f, sb.ToString());
                Log.Info("carnet : traces des speciales ecrits pour le lanceur, " + f + " (" + n + " speciales, " + sb.Length / 1024 + " Ko)");
            }
            catch (Exception e) { Log.Warn("carnet : export des speciales : " + e.Message); }
        }

        // ---------------------------------------------------------------- chaque image (KauppaKartturi) et niveau
        static float lookAt;
        public static void Tick(float now)
        {
            if (exported || now < lookAt) return;
            lookAt = now + 5f;
            if (Time.timeSinceLevelLoad < 15f || !Kartturi.Ready) return;   // (routes du rallye lues en jeu ; Build reessaie toutes les 10 s)
            ExportStages();
            Refresh(true);
            if (Config.Testing && Config.GetInt("Test", "CarnetDump", 0) != 0) Dump();
        }

        public static void OnLevelLoaded()
        {
            autoCache.Clear(); exported = false; lookAt = 0f;
        }

        // [Test] CarnetDump=1 : notes de chaque speciale (carnet actif ou automatiques) dans dumps\carnet-notes.txt, et un aller-retour
        // du carnet actif (Write puis Parse : memes notes)
        static void Dump()
        {
            var sb = new StringBuilder();
            sb.Append("carnet actif : ").Append(Active != null ? Active.Name : "aucun").Append(ActiveError.Length > 0 ? " (" + ActiveError + ")" : "").Append('\n');
            for (int k = 0; k < Kartturi.StageCount; k++)
            {
                List<Note> l = NotesFor(k, Active);
                sb.Append("== ").Append(Kartturi.StageName(k)).Append(", ").Append(l.Count).Append(" notes\n");
                foreach (Note n in l) { NoteJson(sb, n); sb.Append('\n'); }
            }
            if (Active != null)
            {
                string err; Book again = Parse(Write(Active), out err);
                sb.Append("aller-retour : ").Append(again == null ? "REFUSE " + err : Write(again) == Write(Active) ? "identique" : "DIFFERENT").Append('\n');
            }
            string dir = Path.Combine(Log.DataDir, "dumps");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "carnet-notes.txt"), sb.ToString());
            Log.Info("carnet : notes dans " + Path.Combine(dir, "carnet-notes.txt"));
        }

        // ---------------------------------------------------------------- JSON
        class JsonReader
        {
            readonly string s; int i;
            public JsonReader(string text) { s = text; }
            public object Parse() { Ws(); object v = Value(0); Ws(); if (i != s.Length) throw new FormatException("JSON : texte apres la fin (" + i + ")"); return v; }
            void Ws() { while (i < s.Length && (s[i] == ' ' || s[i] == '\t' || s[i] == '\n' || s[i] == '\r')) i++; }
            object Value(int depth)
            {
                if (depth > 20) throw new FormatException("JSON : trop imbrique");
                if (i >= s.Length) throw new FormatException("JSON : fin inattendue");
                char c = s[i];
                if (c == '{') return Obj(depth);
                if (c == '[') return Arr(depth);
                if (c == '"') return Str();
                if (c == 't') { Lit("true"); return true; }
                if (c == 'f') { Lit("false"); return false; }
                if (c == 'n') { Lit("null"); return null; }
                if (c == '-' || (c >= '0' && c <= '9')) return Num();
                throw new FormatException("JSON : valeur inattendue (" + i + ")");
            }
            void Lit(string w) { if (i + w.Length > s.Length || string.CompareOrdinal(s, i, w, 0, w.Length) != 0) throw new FormatException("JSON : mot inconnu (" + i + ")"); i += w.Length; }
            Dictionary<string, object> Obj(int depth)
            {
                var d = new Dictionary<string, object>();
                i++; Ws();
                if (i < s.Length && s[i] == '}') { i++; return d; }
                while (true)
                {
                    Ws();
                    if (i >= s.Length || s[i] != '"') throw new FormatException("JSON : cle attendue (" + i + ")");
                    string k = Str(); Ws();
                    if (i >= s.Length || s[i] != ':') throw new FormatException("JSON : ':' attendu (" + i + ")");
                    i++; Ws();
                    object v = Value(depth + 1);
                    if (d.ContainsKey(k)) throw new FormatException("JSON : cle « " + k + " » en double");
                    d[k] = v; Ws();
                    if (i < s.Length && s[i] == ',') { i++; continue; }
                    if (i < s.Length && s[i] == '}') { i++; return d; }
                    throw new FormatException("JSON : ',' ou '}' attendu (" + i + ")");
                }
            }
            List<object> Arr(int depth)
            {
                var l = new List<object>();
                i++; Ws();
                if (i < s.Length && s[i] == ']') { i++; return l; }
                while (true)
                {
                    Ws(); l.Add(Value(depth + 1)); Ws();
                    if (i < s.Length && s[i] == ',') { i++; continue; }
                    if (i < s.Length && s[i] == ']') { i++; return l; }
                    throw new FormatException("JSON : ',' ou ']' attendu (" + i + ")");
                }
            }
            string Str()
            {
                i++;
                var sb = new StringBuilder();
                while (true)
                {
                    if (i >= s.Length) throw new FormatException("JSON : texte non ferme");
                    char c = s[i++];
                    if (c == '"') return sb.ToString();
                    if (c < 0x20) throw new FormatException("JSON : caractere de controle dans un texte");
                    if (c != '\\') { sb.Append(c); continue; }
                    if (i >= s.Length) throw new FormatException("JSON : texte non ferme");
                    char e = s[i++];
                    switch (e)
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'u':
                            int u;
                            if (i + 4 > s.Length || !int.TryParse(s.Substring(i, 4), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out u)) throw new FormatException("JSON : \\u illisible");
                            sb.Append((char)u); i += 4; break;
                        default: throw new FormatException("JSON : echappement inconnu \\" + e);
                    }
                }
            }
            object Num()
            {
                int a = i;
                if (s[i] == '-') i++;
                while (i < s.Length && ((s[i] >= '0' && s[i] <= '9') || s[i] == '.' || s[i] == 'e' || s[i] == 'E' || s[i] == '+' || s[i] == '-')) i++;
                double v;
                if (!double.TryParse(s.Substring(a, i - a), NumberStyles.Float, CultureInfo.InvariantCulture, out v)) throw new FormatException("JSON : nombre illisible (" + a + ")");
                return v;
            }
        }
    }
}
