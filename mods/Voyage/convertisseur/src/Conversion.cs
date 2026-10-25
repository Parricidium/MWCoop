using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Convertisseur
{
    // Conversion d'un circuit Assetto Corsa (une configuration) en destination du mod Voyage, dans le dossier du jeu :
    //   <MWC>\Mods\Assets\Voyage\destinations\<id>\ : destination.ini (fiche lue par le mod), circuit.vmesh (maillages),
    //   textures\N.vtex, apercu.png / plan.png (images du circuit), infos.json (pour le convertisseur).
    // Affiche : maillages rendus, version detaillee seulement (LodIn = 0 ; les versions de loin se superposeraient),
    // regroupes par texture (morceaux de 60 000 sommets au plus). Collisions : maillages physiques (« 1ROAD », « 08WALL »...,
    // cles de data\surfaces.ini), invisibles. Arrivee : AC_PIT_0 (stand), sinon AC_START_0 / AC_HOTLAP_START_0.
    // Rien n'est envoye nulle part : la destination reste sur le PC du joueur.
    public class Options
    {
        public int MaxTexture = 1024;      // (cote max des textures : 512, 1024, 2048)
        public bool Decor = true;          // (objets hors piste : tribunes, arbres...)
        public string Arrival = "pit";     // pit | start | hotlap
        public int Price = 0;              // (0 : d'apres la longueur)
        public string Name;                // (nom affiche ; vide : celui du circuit)
    }

    public class Conversion
    {
        public const string Version = "4";   // (2 : repere retourne en X, aides AC_ / KS_START_LIGHT ignorees ; 3 : matieres multicouches cuites ; 4 : decalques souleves)
        public volatile bool Cancel;
        public Action<int, double, string> Progress = (s, f, t) => { };   // (etape 0..4, avancement 0..1, texte)
        public Action<string> Log = s => { };
        static readonly CultureInfo ci = CultureInfo.InvariantCulture;

        public class Stats { public int Triangles, Vertices, Colliders, Textures, TexturesGrey, Meshes, Skipped; public long Bytes; public float MinY = float.MaxValue, MaxY = float.MinValue, Radius; }

        public static string IdOf(Track t, Layout l) { return l.Id.Length > 0 ? t.Id + "-" + l.Id : t.Id; }

        public static string Fingerprint(Track t, Layout l)
        {
            var sb = new StringBuilder();
            try
            {
                foreach (Ac.Model m in Ac.Models(t, l)) { var fi = new FileInfo(m.File); sb.Append(fi.Name).Append(fi.Length).Append(fi.LastWriteTimeUtc.Ticks); }
                string s = Path.Combine(l.Data, "surfaces.ini");
                if (File.Exists(s)) sb.Append(File.ReadAllText(s));
                sb.Append(File.Exists(l.Models) ? File.ReadAllText(l.Models) : "");
            }
            catch { }
            using (var sha = SHA1.Create()) return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString()))).Replace("-", "").Substring(0, 16).ToLowerInvariant();
        }

        // ---------------------------------------------------------------- conversion
        public Stats Run(Track t, Layout l, string destRoot, Options o)
        {
            var st = new Stats();
            string id = IdOf(t, l);
            string final = Path.Combine(destRoot, id), tmp = final + ".nouveau";
            if (Directory.Exists(tmp)) Directory.Delete(tmp, true);
            Directory.CreateDirectory(Path.Combine(tmp, "textures"));
            var files = new List<Kn5>();   // (fichiers ouverts a la demande : fermes a la fin, Kn5.Dispose)
            try
            {
                // 1. lecture
                var models = Ac.Models(t, l);
                if (models.Count == 0) throw new InvalidDataException("aucun fichier .kn5 pour cette configuration");
                var keys = Ac.SurfaceKeys(l);
                Log(models.Count + " fichier(s) .kn5, " + keys.Count + " surface(s) physique(s)");
                for (int k = 0; k < models.Count; k++)
                {
                    if (Cancel) throw new OperationCanceledException();
                    Progress(0, k / (double)models.Count, Path.GetFileName(models[k].File));
                    Ac.Model m = models[k];
                    Kn5 kn = Kn5.Read(m.File, Mat.Translation(m.X, m.Y, m.Z));
                    Log(Path.GetFileName(m.File) + " : " + kn.Meshes.Count + " maillages, " + kn.Textures.Count + " textures, " + kn.Materials.Count + " matieres");
                    files.Add(kn);
                }
                // 2. tri des maillages
                var phys = new Regex(@"^\d+(" + string.Join("|", keys.ConvertAll(Regex.Escape).ToArray()) + ")", RegexOptions.IgnoreCase);
                var groups = new Dictionary<string, Group>();
                var colliders = new List<Item>();
                var decals = DecalLayers(files);
                foreach (Kn5 kn in files)
                    foreach (Kn5.Mesh me in kn.Meshes)
                    {
                        bool isPhys = keys.Count > 0 && phys.IsMatch(me.Name);
                        if (isPhys) colliders.Add(new Item { File = kn, Mesh = me });
                        bool show = me.Renderable && me.Visible && me.LodIn <= 0.01f && !Helper(me.Name);
                        if (!show) { if (!isPhys) st.Skipped++; continue; }
                        Kn5.Material mat = me.MaterialId >= 0 && me.MaterialId < kn.Materials.Count ? kn.Materials[me.MaterialId] : null;
                        if (!o.Decor && !isPhys && me.Layer != 0) { st.Skipped++; continue; }
                        string tex = null;
                        if (mat != null && mat.Samplers.TryGetValue("txDiffuse", out tex)) { } else tex = null;
                        bool tree = mat != null && mat.Shader.IndexOf("Tree", StringComparison.OrdinalIgnoreCase) >= 0;
                        bool cut = mat != null && (mat.AlphaTest != 0 || mat.Blend != 0 || tree || mat.Shader.EndsWith("AT", StringComparison.Ordinal));
                        bool two = tree || mat != null && mat.AlphaTest != 0;
                        // matieres multicouches (herbe, terre, roche) : texture de base presque blanche, couleur donnee par
                        // quatre textures de detail melangees par un masque -> une texture « cuite » par matiere
                        bool multi = mat != null && tex != null && mat.Shader.StartsWith("ksMultilayer", StringComparison.OrdinalIgnoreCase) && mat.Samplers.ContainsKey("txMask");
                        if (multi) { cut = false; two = false; }
                        // decalques (traces de gomme, lignes, bords des bacs a sable) : poses pile sur la route ; Assetto Corsa les
                        // dessine apres, sans ecrire la profondeur. Ici, souleves de quelques cm (une couche de plus par matiere,
                        // dans l'ordre du fichier) : sinon ils clignotent avec la route (z-fighting, JD 24/10, Laguna Seca)
                        float lift = 0f;
                        int layer;
                        if (!multi && decals.TryGetValue(DecalKey(kn, me), out layer)) lift = 0.02f + 0.01f * Math.Min(layer, 5);
                        string gk = Path.GetFileName(kn.File) + "|" + (multi ? "ml:" + mat.Name : tex ?? "-") + "|" + (cut ? 1 : 0) + (two ? 1 : 0) + "|" + lift.ToString(ci);
                        Group g;
                        if (!groups.TryGetValue(gk, out g)) groups[gk] = g = new Group { File = kn, Texture = tex, Cut = cut, TwoSided = two, Multi = multi ? mat : null, Lift = lift, Mat = mat != null ? mat.Name : "-" };
                        g.Items.Add(new Item { File = kn, Mesh = me });
                        st.Meshes++;
                    }
                if (colliders.Count == 0)   // (circuit sans surfaces nommees : on roule sur ce qui est affiche)
                {
                    Log("aucun maillage physique reconnu : collisions sur les maillages affiches");
                    foreach (Group g in groups.Values) foreach (Item it in g.Items) colliders.Add(it);
                }
                // 3. textures (seulement celles affichees)
                var texFiles = new Dictionary<string, string>();
                int ti = 0, tn = 0;
                foreach (Group g in groups.Values) if (g.Texture != null) tn++;
                foreach (Group g in groups.Values)
                {
                    if (Cancel) throw new OperationCanceledException();
                    if (g.Texture == null) continue;
                    Kn5.Texture tx = g.File.Textures.Find(x => x.Name == g.Texture);
                    if (tx == null) { g.Texture = null; continue; }
                    string key = g.Multi != null ? "ml|" + Path.GetFileName(g.File.File) + "|" + g.Multi.Name : tx.Name + "|" + tx.Size;
                    string fn;
                    if (!texFiles.TryGetValue(key, out fn))
                    {
                        Dds.Result r = g.Multi != null ? Bake(g.File, g.Multi, tx, o.MaxTexture) : null;
                        if (r == null) r = Dds.Convert(g.File.Bytes(tx.Offset, tx.Size), 0, tx.Size, o.MaxTexture);
                        fn = "textures/" + texFiles.Count + ".vtex";
                        File.WriteAllBytes(Path.Combine(tmp, fn.Replace('/', '\\')), r.Vtex);
                        texFiles[key] = fn;
                        st.Textures++;
                        if (!r.Supported) { st.TexturesGrey++; Log("texture " + tx.Name + " non prise en charge (" + r.Note + ") : grise"); }
                    }
                    g.TexFile = fn;
                    Progress(1, ++ti / (double)Math.Max(1, tn), tx.Name);
                }
                // 4. maillages
                string vmesh = Path.Combine(tmp, "circuit.vmesh");
                using (var fs = new FileStream(vmesh, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20))
                {
                    var w = new BinaryWriter(fs);
                    w.Write(new[] { (byte)'V', (byte)'O', (byte)'Y', (byte)'M' }); w.Write(3);
                    long countPos = fs.Position; w.Write(0);
                    int subs = 0, gi = 0;
                    foreach (Group g in groups.Values)
                    {
                        if (Cancel) throw new OperationCanceledException();
                        int flags = (g.Cut ? 32 : 0) | (g.TwoSided ? 2 : 0);
                        Log("m" + gi + " : " + g.Mat + " (" + Path.GetFileName(g.File.File) + ", " + (g.Texture ?? "sans texture") + (g.Lift > 0 ? ", souleve de " + (g.Lift * 100).ToString("0.#", ci) + " cm" : "") + ")");
                        subs += WriteChunks(w, "m" + gi, g.TexFile ?? "", flags, g.Items, g.TwoSided ? 30000 : 60000, false, st, g.Lift);
                        Progress(2, ++gi / (double)groups.Count, "");
                    }
                    subs += WriteChunks(w, "collision", "", 4 | 16 | 8, colliders, 60000, true, st);
                    Progress(3, 1, "");
                    fs.Position = countPos; w.Write(subs);
                }
                // arrivee (stand, depart, tour chrono) ; cap verifie par la trajectoire de l'IA (ai\fast_lane.ai)
                var spots = new Dictionary<string, float[]>();
                foreach (Kn5 kn in files)
                    foreach (Kn5.Dummy d in kn.Dummies)
                    {
                        string k = d.Name == "AC_PIT_0" ? "pit" : d.Name == "AC_START_0" ? "start" : d.Name == "AC_HOTLAP_START_0" ? "hotlap" : null;
                        if (k == null || spots.ContainsKey(k)) continue;
                        float x, y, z, fx, fy, fz;
                        d.World.Point(0, 0, 0, out x, out y, out z);
                        d.World.Dir(0, 0, 1, out fx, out fy, out fz);
                        spots[k] = new[] { -x, y, z, (float)(Math.Atan2(-fx, fz) * 180.0 / Math.PI) };   // (X retourne, voir WriteChunks)
                    }
                CheckHeading(t, l, spots);
                float[] arr;
                if (!spots.TryGetValue(o.Arrival, out arr) && !spots.TryGetValue("pit", out arr) && !spots.TryGetValue("start", out arr) && !spots.TryGetValue("hotlap", out arr))
                    arr = new[] { 0f, st.MaxY > st.MinY ? st.MaxY : 0f, 0f, 0f };
                // 5. fiche, images, infos
                Progress(4, 0.5, "");
                float km = 0; float.TryParse(new string(Array.FindAll((l.Length ?? "").ToCharArray(), c => char.IsDigit(c) || c == '.')), NumberStyles.Float, ci, out km);
                if (km > 100) km /= 1000f;   // (longueur en metres dans la plupart des ui_track.json)
                int price = o.Price > 0 ? o.Price : (int)(Math.Round((300 + km * 60) / 50.0) * 50);
                string name = string.IsNullOrEmpty(o.Name) ? l.Name : o.Name;
                var ini = new StringBuilder();
                ini.AppendLine("; Destination du mod Voyage, convertie depuis l'Assetto Corsa de ce joueur (en co-op, l'hote l'envoie a ses invites avec ses mods)");
                ini.AppendLine("nom=" + name);
                ini.AppendLine("pays=" + l.Country);
                ini.AppendLine("auteur=" + l.Author);
                ini.AppendLine("prix=" + price);
                ini.AppendLine("arrivee=" + F(arr[0]) + "," + F(arr[1]) + "," + F(arr[2]));
                ini.AppendLine("cap=" + F(arr[3]));
                ini.AppendLine("point=" + o.Arrival);
                ini.AppendLine("rayon=" + F(Math.Max(500f, st.Radius)));
                ini.AppendLine("basy=" + F(st.MinY == float.MaxValue ? 0f : st.MinY));
                ini.AppendLine("empreinte=" + Fingerprint(t, l));
                ini.AppendLine("source=" + t.Id + (l.Id.Length > 0 ? "/" + l.Id : ""));
                ini.AppendLine("textures=" + o.MaxTexture);
                ini.AppendLine("decor=" + (o.Decor ? 1 : 0));
                ini.AppendLine("convertisseur=" + Version);
                ini.AppendLine("date=" + DateTime.Now.ToString("yyyy-MM-dd HH:mm", ci));
                File.WriteAllText(Path.Combine(tmp, "destination.ini"), ini.ToString(), new UTF8Encoding(false));
                if (l.Preview != null) File.Copy(l.Preview, Path.Combine(tmp, "apercu.png"), true);
                if (l.Outline != null) File.Copy(l.Outline, Path.Combine(tmp, "plan.png"), true);
                st.Bytes = 0;
                foreach (string f in Directory.GetFiles(tmp, "*", SearchOption.AllDirectories)) st.Bytes += new FileInfo(f).Length;
                var info = new Dictionary<string, object>
                {
                    { "triangles", st.Triangles }, { "sommets", st.Vertices }, { "collisions", st.Colliders }, { "textures", st.Textures }, { "texturesGrises", st.TexturesGrey },
                    { "maillages", st.Meshes }, { "ignores", st.Skipped }, { "octets", st.Bytes }, { "rayon", st.Radius }, { "basy", st.MinY }, { "hauty", st.MaxY },
                    { "points", spots }, { "longueur", l.Length }, { "description", l.Description }, { "ville", l.City },
                };
                File.WriteAllText(Path.Combine(tmp, "infos.json"), new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(info), new UTF8Encoding(false));
                // remplacement de l'ancienne version (en dernier : un echec garde l'ancienne)
                if (Directory.Exists(final)) Directory.Delete(final, true);
                Directory.Move(tmp, final);
                Progress(4, 1, "");
                Log("termine : " + st.Triangles + " triangles, " + st.Textures + " textures, " + (st.Bytes / 1048576) + " Mo");
                return st;
            }
            catch
            {
                try { if (Directory.Exists(tmp)) Directory.Delete(tmp, true); } catch { }
                throw;
            }
            finally { foreach (Kn5 k in files) k.Dispose(); }
        }

        static bool IsDecal(Kn5.Material m) { return m != null && (m.Blend != 0 || m.Depth != 0); }
        static string DecalKey(Kn5 kn, Kn5.Mesh me) { return Path.GetFileName(kn.File) + "|" + me.MaterialId; }

        // couches des decalques : une matiere passe au-dessus de toutes celles d'avant (ordre du fichier, celui d'Assetto Corsa)
        // qu'elle recouvre (cases de 30 cm) ; deux decalques qui ne se touchent jamais restent a la meme hauteur
        Dictionary<string, int> DecalLayers(List<Kn5> files)
        {
            var order = new List<string>();
            var cells = new Dictionary<string, HashSet<long>>();
            foreach (Kn5 kn in files)
                foreach (Kn5.Mesh me in kn.Meshes)
                {
                    if (!me.Renderable || !me.Visible || me.LodIn > 0.01f || Helper(me.Name) || me.IndexCount < 3) continue;
                    Kn5.Material mat = me.MaterialId >= 0 && me.MaterialId < kn.Materials.Count ? kn.Materials[me.MaterialId] : null;
                    if (!IsDecal(mat)) continue;
                    string dk = DecalKey(kn, me);
                    HashSet<long> hs;
                    if (!cells.TryGetValue(dk, out hs)) { cells[dk] = hs = new HashSet<long>(); order.Add(dk); }
                    int bo; byte[] d = kn.MeshBytes(me, out bo);
                    int vo = me.VertexOffset - bo, io = me.IndexOffset - bo;
                    for (int i = 0; i + 2 < me.IndexCount; i += 3)
                    {
                        float cx = 0, cz = 0;
                        for (int c = 0; c < 3; c++)
                        {
                            int o = vo + BitConverter.ToUInt16(d, io + (i + c) * 2) * 44;
                            float x, y, z;
                            me.World.Point(BitConverter.ToSingle(d, o), BitConverter.ToSingle(d, o + 4), BitConverter.ToSingle(d, o + 8), out x, out y, out z);
                            cx += x / 3f; cz += z / 3f;
                        }
                        hs.Add(((long)Math.Floor(cx / 0.3f) << 32) | (uint)(int)Math.Floor(cz / 0.3f));
                    }
                }
            var layer = new Dictionary<string, int>();
            for (int i = 0; i < order.Count; i++)
            {
                int l = 0;
                for (int j = 0; j < i; j++)
                    if (layer[order[j]] + 1 > l && cells[order[i]].Overlaps(cells[order[j]])) l = layer[order[j]] + 1;
                layer[order[i]] = l;
            }
            if (order.Count > 0) Log(order.Count + " decalque(s) (traces, lignes, bordures) en " + (1 + System.Linq.Enumerable.Max(layer.Values)) + " couche(s)");
            return layer;
        }

        // reperes et aides du jeu, pas du decor : equipe des stands, cases de depart, feux de depart (100.kn5)...
        static readonly string[] helpers = { "AC_CREW", "AC_PIT_", "AC_START_", "AC_HOTLAP_", "AC_TIME_", "AC_AUDIO", "KS_START_LIGHT" };
        static bool Helper(string n) { foreach (string h in helpers) if (n.StartsWith(h, StringComparison.OrdinalIgnoreCase)) return true; return false; }

        static string F(float v) { return v.ToString("0.###", ci); }

        class Item { public Kn5 File; public Kn5.Mesh Mesh; }
        class Group { public Kn5 File; public string Texture, TexFile; public bool Cut, TwoSided; public float Lift; public string Mat; public Kn5.Material Multi; public readonly List<Item> Items = new List<Item>(); }

        // couleur = base x (somme des couleurs moyennes des details R, G, B, A ponderees par le masque) x magicMult ; le
        // grain fin des details (repetes a petite echelle) est perdu, les couleurs sont justes. null : on garde la base.
        Dds.Result Bake(Kn5 kn, Kn5.Material mat, Kn5.Texture diffuse, int maxSize)
        {
            Func<string, Kn5.Texture> find = s => { string n; return mat.Samplers.TryGetValue(s, out n) ? kn.Textures.Find(x => x.Name == n) : null; };
            Kn5.Texture mask = find("txMask");
            if (mask == null) { Log("matiere multicouche " + mat.Name + " : masque introuvable"); return null; }
            int w, h, mw, mh;
            byte[] px = Dds.Decode(kn.Bytes(diffuse.Offset, diffuse.Size), 0, diffuse.Size, maxSize, out w, out h);
            byte[] mk = Dds.Decode(kn.Bytes(mask.Offset, mask.Size), 0, mask.Size, maxSize, out mw, out mh);
            if (px == null || mk == null || w % 4 != 0 || h % 4 != 0) { Log("matiere multicouche " + mat.Name + " : textures illisibles (" + (px == null ? "base" : mk == null ? "masque" : w + "x" + h) + ")"); return null; }
            var avg = new float[4][];
            string[] det = { "txDetailR", "txDetailG", "txDetailB", "txDetailA" };
            for (int i = 0; i < 4; i++)
            {
                Kn5.Texture t = find(det[i]);
                avg[i] = t != null ? Dds.Average(kn.Bytes(t.Offset, t.Size), 0, t.Size) : null;
                if (avg[i] == null) avg[i] = new[] { 0f, 0f, 0f };
            }
            float magic; if (!mat.Props.TryGetValue("magicMult", out magic) || magic <= 0) magic = 1f;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int q = (y * w + x) * 4, mq = ((y * mh / h) * mw + x * mw / w) * 4;
                    for (int c = 0; c < 3; c++)
                    {
                        float v = 0;
                        for (int i = 0; i < 4; i++) v += mk[mq + i] / 255f * avg[i][c];
                        px[q + c] = (byte)Math.Max(0, Math.Min(255, px[q + c] * v * magic + 0.5f));
                    }
                    px[q + 3] = 255;
                }
            Log("matiere multicouche " + mat.Name + " : texture cuite " + w + "x" + h);
            return Dds.EncodeDxt1(px, w, h);
        }

        // sommets transformes dans le repere monde du circuit ; morceaux de 'limit' sommets au plus. Assetto Corsa (DirectX) est
        // en repere droit, Unity en repere gauche : X retourne (positions, normales) et triangles dans l'autre sens, sinon tout
        // est en miroir (numeros des stands, panneaux a l'envers)
        int WriteChunks(BinaryWriter w, string name, string tex, int flags, List<Item> items, int limit, bool collider, Stats st, float lift = 0f)
        {
            int written = 0;
            var pos = new List<float>(); var nor = new List<float>(); var uv = new List<float>(); var idx = new List<int>();
            var remap = new Dictionary<int, int>();
            Action flush = () =>
            {
                if (idx.Count == 0) return;
                WriteSub(w, name + "_" + written, tex, flags, pos, nor, uv, idx, collider);
                written++;
                pos.Clear(); nor.Clear(); uv.Clear(); idx.Clear();
            };
            foreach (Item it in items)
            {
                Kn5.Mesh me = it.Mesh;
                if (me.VertexCount == 0 || me.IndexCount < 3) continue;
                int bo; byte[] d = it.File.MeshBytes(me, out bo);
                int vo = me.VertexOffset - bo, io = me.IndexOffset - bo;
                int vbase = pos.Count / 3;
                if (vbase + me.VertexCount > limit) { flush(); vbase = 0; }
                bool whole = me.VertexCount <= limit;
                var tv = new float[me.VertexCount * 8];
                for (int i = 0; i < me.VertexCount; i++)
                {
                    int o = vo + i * 44;
                    float x, y, z, nx, ny, nz;
                    me.World.Point(BitConverter.ToSingle(d, o), BitConverter.ToSingle(d, o + 4), BitConverter.ToSingle(d, o + 8), out x, out y, out z);
                    me.World.Dir(BitConverter.ToSingle(d, o + 12), BitConverter.ToSingle(d, o + 16), BitConverter.ToSingle(d, o + 20), out nx, out ny, out nz);
                    float nl = (float)Math.Sqrt(nx * nx + ny * ny + nz * nz); if (nl > 1e-6f) { nx /= nl; ny /= nl; nz /= nl; }
                    if (lift > 0f) y += lift;   // (decalque : au-dessus de la route ; a la verticale : leurs normales ne pointent pas toujours vers le haut)
                    tv[i * 8] = -x; tv[i * 8 + 1] = y; tv[i * 8 + 2] = z; tv[i * 8 + 3] = -nx; tv[i * 8 + 4] = ny; tv[i * 8 + 5] = nz;
                    tv[i * 8 + 6] = BitConverter.ToSingle(d, o + 24); tv[i * 8 + 7] = BitConverter.ToSingle(d, o + 28);
                    if (!collider)
                    {
                        if (y < st.MinY) st.MinY = y; if (y > st.MaxY) st.MaxY = y;
                        float r = (float)Math.Sqrt(x * x + z * z); if (r > st.Radius) st.Radius = r;
                    }
                }
                if (whole)
                {
                    for (int i = 0; i < me.VertexCount; i++) Add(pos, nor, uv, tv, i);
                    for (int i = 0; i + 2 < me.IndexCount; i += 3)
                    {
                        idx.Add(vbase + BitConverter.ToUInt16(d, io + i * 2));
                        idx.Add(vbase + BitConverter.ToUInt16(d, io + (i + 2) * 2));
                        idx.Add(vbase + BitConverter.ToUInt16(d, io + (i + 1) * 2));
                    }
                }
                else   // (maillage plus grand que la limite : triangle par triangle)
                {
                    remap.Clear();
                    for (int i = 0; i + 2 < me.IndexCount; i += 3)
                    {
                        if (pos.Count / 3 + 3 > limit) { flush(); remap.Clear(); }
                        foreach (int c in order)
                        {
                            int src = BitConverter.ToUInt16(d, io + (i + c) * 2), dst;
                            if (!remap.TryGetValue(src, out dst)) { dst = pos.Count / 3; remap[src] = dst; Add(pos, nor, uv, tv, src); }
                            idx.Add(dst);
                        }
                    }
                }
                if (collider) st.Colliders++;
                else { st.Triangles += me.IndexCount / 3; st.Vertices += me.VertexCount; }
            }
            flush();
            return written;
        }

        static readonly int[] order = { 0, 2, 1 };

        static void Add(List<float> pos, List<float> nor, List<float> uv, float[] tv, int i)
        {
            pos.Add(tv[i * 8]); pos.Add(tv[i * 8 + 1]); pos.Add(tv[i * 8 + 2]);
            nor.Add(tv[i * 8 + 3]); nor.Add(tv[i * 8 + 4]); nor.Add(tv[i * 8 + 5]);
            uv.Add(tv[i * 8 + 6]); uv.Add(tv[i * 8 + 7]);
        }

        static void WriteSub(BinaryWriter w, string name, string tex, int flags, List<float> pos, List<float> nor, List<float> uv, List<int> idx, bool collider)
        {
            WStr(w, name); WStr(w, tex);
            w.Write((byte)flags); w.Write(0f); w.Write(0.15f); w.Write(0f); w.Write(0f); w.Write(0f);
            int nv = pos.Count / 3;
            w.Write(nv);
            foreach (float f in pos) w.Write(f);
            if ((flags & 8) == 0) foreach (float f in nor) w.Write(f);
            foreach (float f in uv) w.Write(f);
            w.Write(idx.Count);
            foreach (int i in idx) w.Write(i);
        }

        static void WStr(BinaryWriter w, string s) { byte[] b = Encoding.UTF8.GetBytes(s); w.Write(b.Length); w.Write(b); }

        // la trajectoire de l'IA dit dans quel sens on roule : si le repere d'arrivee regarde a l'envers, on le retourne
        void CheckHeading(Track t, Layout l, Dictionary<string, float[]> spots)
        {
            string[] cands = { Path.Combine(Path.Combine(t.Dir, l.Id.Length > 0 ? l.Id : "."), @"ai\fast_lane.ai"), Path.Combine(t.Dir, @"ai\fast_lane.ai") };
            string f = Array.Find(cands, File.Exists);
            if (f == null) return;
            try
            {
                byte[] d = File.ReadAllBytes(f);
                int n = BitConverter.ToInt32(d, 4);
                if (n < 4 || 16 + n * 20 > d.Length) return;
                foreach (string k in new[] { "start", "hotlap", "pit" })
                {
                    float[] s; if (!spots.TryGetValue(k, out s)) continue;
                    int best = -1; float bd = float.MaxValue;
                    for (int i = 0; i < n; i++)
                    {
                        int o = 16 + i * 20;
                        float dx = -BitConverter.ToSingle(d, o) - s[0], dz = BitConverter.ToSingle(d, o + 8) - s[2];
                        float dd = dx * dx + dz * dz; if (dd < bd) { bd = dd; best = i; }
                    }
                    int a = 16 + best * 20, b = 16 + ((best + 3) % n) * 20;
                    float tx = BitConverter.ToSingle(d, a) - BitConverter.ToSingle(d, b), tz = BitConverter.ToSingle(d, b + 8) - BitConverter.ToSingle(d, a + 8);
                    double yaw = Math.Atan2(tx, tz) * 180.0 / Math.PI;
                    double diff = Math.Abs(((yaw - s[3]) % 360 + 540) % 360 - 180);
                    Log("cap de " + k + " : repere " + s[3].ToString("0", ci) + ", trajectoire " + yaw.ToString("0", ci) + (k != "pit" && diff > 90 ? " (retourne)" : ""));
                    if (k != "pit" && diff > 90) s[3] = (float)yaw;
                }
            }
            catch (Exception e) { Log("trajectoire de l'IA illisible : " + e.Message); }
        }
    }
}
