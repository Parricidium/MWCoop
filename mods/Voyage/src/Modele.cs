using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace Voyage
{
    // Modeles embarques dans Voyage.dll (ressources Voyage.<nom>.vmesh et leurs textures, faits par outils\glb2voyage.py) :
    // un sous-maillage par matiere, textures de couleur, vitres transparentes ; faces doublees (matieres « deux faces »
    // du glTF : ailes et gouvernes fines vues des deux cotes).
    //  - Cessna 172 : « Plane » par osmosikum (Sketchfab), CC BY 4.0 -- credite dans le terminal et le LISEZMOI.
    // Circuits convertis (Destinations) : meme format, lus depuis leur dossier (textures a cote, compressees a la volee).
    static class Modele
    {
        static readonly Dictionary<string, Texture2D> textures = new Dictionary<string, Texture2D>();
        static readonly Dictionary<string, Texture2D> folderTextures = new Dictionary<string, Texture2D>();

        public static void ForgetFolderTextures()
        {
            foreach (Texture2D t in folderTextures.Values) if (t != null) UnityEngine.Object.Destroy(t);
            folderTextures.Clear();
        }

        static byte[] Resource(string name)
        {
            using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream("Voyage." + name))
            {
                if (s == null) return null;
                var b = new byte[s.Length];
                int off = 0;
                while (off < b.Length) { int r = s.Read(b, off, b.Length - off); if (r <= 0) break; off += r; }
                return b;
            }
        }

        public static byte[] Bytes(string name) { return Resource(name); }

        static Texture2D Texture(string name, Func<string, byte[]> read)
        {
            Texture2D t;
            var cache = read == null ? textures : folderTextures;
            if (cache.TryGetValue(name, out t) && t != null) return t;
            byte[] b = read == null ? Resource(name) : read(name);
            if (b == null) { Journal.Info("modele : texture absente " + name); return null; }
            if (name.EndsWith(".vtex"))   // (circuits convertis : DXT1 / DXT5 / BGRA32 tels quels, niveaux de detail compris)
            {
                t = Vtex(b, name);
                if (t != null) { cache[name] = t; return t; }
                Journal.Info("modele : texture illisible " + name);
                return null;
            }
            t = new Texture2D(2, 2, TextureFormat.RGBA32, true) { name = "voyage_" + name, anisoLevel = 4, wrapMode = TextureWrapMode.Repeat };
            t.LoadImage(b);
            if (read != null && t.width >= 256) t.Compress(true);   // (DXT en memoire, quatre a huit fois moins)
            cache[name] = t;
            return t;
        }

        // .vtex (convertisseur) : « VOYT », format (1 DXT1, 5 DXT5, 32 BGRA32), largeur, hauteur, niveaux, octets, donnees
        static Texture2D Vtex(byte[] b, string name)
        {
            if (b.Length < 24 || b[0] != 'V' || b[1] != 'O' || b[2] != 'Y' || b[3] != 'T') return null;
            int fmt = BitConverter.ToInt32(b, 4), w = BitConverter.ToInt32(b, 8), h = BitConverter.ToInt32(b, 12), mips = BitConverter.ToInt32(b, 16), n = BitConverter.ToInt32(b, 20);
            if (n <= 0 || 24 + n > b.Length) return null;
            TextureFormat tf = fmt == 1 ? TextureFormat.DXT1 : fmt == 5 ? TextureFormat.DXT5 : TextureFormat.BGRA32;
            try
            {
                var t = new Texture2D(w, h, tf, mips > 1) { name = "voyage_" + name, anisoLevel = 4, wrapMode = TextureWrapMode.Repeat };
                var raw = new byte[n];
                Buffer.BlockCopy(b, 24, raw, 0, n);
                t.LoadRawTextureData(raw);
                t.Apply(false, true);   // (plus lisible par le programme : la copie en memoire vive est liberee)
                return t;
            }
            catch (Exception e) { Journal.Info("modele : " + name + " : " + e.Message); return null; }
        }

        static string Str(BinaryReader r) { int n = r.ReadInt32(); return System.Text.Encoding.UTF8.GetString(r.ReadBytes(n)); }

        // modele sous 'parent' (repere local), null si la ressource manque ; parts : sous-objets par nom (helice...)
        public static GameObject Load(string name, Transform parent, out Dictionary<string, Transform> parts, Func<string, byte[]> read = null)
        {
            parts = new Dictionary<string, Transform>();
            byte[] data = read == null ? Resource(name + ".vmesh") : read(name + ".vmesh");
            if (data == null) { Journal.Info("modele : " + name + ".vmesh absent du mod"); return null; }
            var root = new GameObject("voyage_" + name);
            root.transform.SetParent(parent, false);
            Material basis = Construction.GameMat("metal_galvanized");
            Shader standard = basis != null ? basis.shader : Shader.Find("Standard");
            Shader glass = Shader.Find("Legacy Shaders/Transparent/Specular") ?? Shader.Find("Legacy Shaders/Transparent/Diffuse");
            Shader cutout = Shader.Find("Legacy Shaders/Transparent/Cutout/Diffuse") ?? Shader.Find("Transparent/Cutout/Diffuse");
            int tris = 0;
            if (data.Length > 2 && data[0] == 0x1f && data[1] == 0x8b)   // (gzip : la ville ; decompression en code gere)
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                data = Inflate.Gzip(data);
                Journal.Info("modele " + name + " : decompresse, " + (data.Length / 1024) + " Ko en " + sw.ElapsedMilliseconds + " ms");
            }
            int cols = 0;
            using (var r = new BinaryReader(new MemoryStream(data)))
            {
                if (new string(r.ReadChars(4)) != "VOYM") { Journal.Info("modele : " + name + " illisible"); return root; }
                int version = r.ReadInt32();
                int n = r.ReadInt32();
                for (int k = 0; k < n; k++)
                {
                    string sub = Str(r), tex = Str(r);
                    // v1 (Cessna) : octet « transparent », faces doublees ; v2+ : drapeaux 1 transparent, 2 deux faces,
                    // 4 collision, 8 normales a recalculer (absentes du fichier), 16 invisible (collision seule), 32 decoupe alpha
                    int flags = r.ReadByte();
                    if (version < 2) flags = (flags != 0 ? 1 : 0) | 2;
                    bool transparent = (flags & 1) != 0, twoSided = (flags & 2) != 0, collide = (flags & 4) != 0, recalc = (flags & 8) != 0;
                    bool invisible = (flags & 16) != 0, cut = (flags & 32) != 0;
                    float metal = r.ReadSingle(), smooth = r.ReadSingle();
                    var pivot = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
                    int nv = r.ReadInt32();
                    int mul = twoSided ? 2 : 1;
                    var v = new Vector3[nv * mul]; var no = new Vector3[nv * mul]; var uv = new Vector2[nv * mul];
                    for (int i = 0; i < nv; i++) v[i] = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle()) - pivot;
                    if (!recalc) for (int i = 0; i < nv; i++) no[i] = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
                    for (int i = 0; i < nv; i++) uv[i] = new Vector2(r.ReadSingle(), r.ReadSingle());
                    int ni = r.ReadInt32();
                    var t = new int[ni * mul];
                    for (int i = 0; i < ni; i++) t[i] = r.ReadInt32();
                    if (twoSided)   // faces de dos : sommets recopies, normales retournees, triangles a l'envers
                    {
                        for (int i = 0; i < nv; i++) { v[nv + i] = v[i]; no[nv + i] = -no[i]; uv[nv + i] = uv[i]; }
                        for (int i = 0; i < ni; i += 3) { t[ni + i] = t[i] + nv; t[ni + i + 1] = t[i + 2] + nv; t[ni + i + 2] = t[i + 1] + nv; }
                    }
                    var go = new GameObject("voyage_" + name + "_" + sub);
                    go.transform.SetParent(root.transform, false);
                    go.transform.localPosition = pivot;
                    var m = new Mesh { name = go.name, vertices = v, uv = uv, triangles = t };
                    if (!recalc) m.normals = no; else if (!invisible) m.RecalculateNormals();   // (collision seule : pas besoin de normales)
                    m.RecalculateBounds();
                    if (invisible)   // (murs invisibles, bords de piste : collision seule)
                    {
                        if (collide) { go.AddComponent<MeshCollider>().sharedMesh = m; cols++; }
                        parts[parts.ContainsKey(sub) ? sub + "#" + k : sub] = go.transform;
                        continue;
                    }
                    go.AddComponent<MeshFilter>().sharedMesh = m;
                    var mr = go.AddComponent<MeshRenderer>();
                    Material mat;
                    if (tex.StartsWith("jeu:"))   // (matiere du jeu recopiee : l'asphalte des routes)
                    {
                        Material g = Construction.GameMat(tex.Substring(4));
                        mat = g != null ? new Material(g) : new Material(Shader.Find("Legacy Shaders/Diffuse"));
                    }
                    else
                    {
                        Texture2D tx = tex.Length > 0 ? Texture(tex, read) : null;
                        if (cut || transparent && version >= 2)   // (ville : grilles en fer forge, decoupe alpha -- les shaders transparents
                        {                                  // « verre » ne sont pas dans le jeu, la decoupe oui : grillage de l'aerodrome)
                            mat = new Material(cutout ?? glass) { mainTexture = tx, color = Color.white };
                            if (mat.HasProperty("_Cutoff")) mat.SetFloat("_Cutoff", 0.4f);
                            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                        }
                        else if (transparent)
                        {
                            mat = new Material(glass) { mainTexture = tx, color = new Color(1f, 1f, 1f, 1f) };
                            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                        }
                        else
                        {
                            mat = basis != null ? new Material(basis) : new Material(standard);
                            mat.mainTexture = tx; mat.color = Color.white;
                            if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", Mathf.Min(metal, 0.35f));   // (pas de reflets du ciel : trop sombre sinon)
                            if (mat.HasProperty("_Glossiness")) mat.SetFloat("_Glossiness", Mathf.Clamp(smooth, 0.05f, 0.6f));
                            if (mat.HasProperty("_BumpMap")) mat.SetTexture("_BumpMap", null);
                            if (mat.HasProperty("_MetallicGlossMap")) mat.SetTexture("_MetallicGlossMap", null);
                            mat.DisableKeyword("_NORMALMAP"); mat.DisableKeyword("_METALLICGLOSSMAP");
                        }
                    }
                    mat.name = "voyage_" + name + "_" + sub;
                    mr.sharedMaterial = mat;
                    if (collide) { go.AddComponent<MeshCollider>().sharedMesh = m; cols++; }
                    parts[parts.ContainsKey(sub) ? sub + "#" + k : sub] = go.transform;
                    tris += ni / 3;
                }
            }
            Journal.Info("modele " + name + " : " + parts.Count + " pieces, " + tris + " triangles, " + cols + " collisions");
            return root;
        }
    }
}
