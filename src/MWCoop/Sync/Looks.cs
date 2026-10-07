using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace MWCoop
{
    // Apparence complete d'un joueur (demande de JD et d'un joueur sur Nexus, 07/10) : haut, pantalon, visage,
    // corpulence, chapeau, lunettes, cheveux. Tout vient des PNJ du jeu (releve : dumps/apparences.txt) :
    //  - le corps des PNJ (bodymesh, 20 os, meme squelette pour tous) a trois sous-maillages : chemise, pantalon,
    //    visage (matieres char_shirtNN, char_pantsNN, char_faceNN, et celles des uniformes) ;
    //  - six corps d'adultes (mince... costaud, deux plus petits) et le cochon : chacun avec SON squelette (copie de
    //    son PNJ : un corps plus petit sur le squelette d'un grand serait etire) ; cle "<sommets>-<largeur cm>"
    //    ("pig-1627-37" pour un maillage nomme autrement que bodymesh) ;
    //  - chapeaux, lunettes, cheveux : objets sous l'os "head" des PNJ ; cle "<maillage>@<matiere>", pose relative
    //    a la tete.
    // Chaine d'apparence (reseau, [Coop] du mwcoop.ini) : "haut|pantalon|visage|corps|chapeau|lunettes|cheveux" ;
    // un champ vide = celui du modele ; une chaine sans '|' = le haut seul (ancien format).
    // Apercu du lanceur : cache\perso.mesh + cache\perso\<matiere>.png (Export), refaits a chaque version du mod.
    public static class Looks
    {
        public const int KHat = 0, KGlasses = 1, KHair = 2;

        public class Look
        {
            public string Shirt = "", Pants = "", Face = "", Body = "", Hat = "", Glasses = "", Hair = "";
        }

        public static Look Parse(string s)
        {
            var l = new Look();
            if (string.IsNullOrEmpty(s)) return l;
            string[] p = s.Split('|');
            l.Shirt = p[0];
            if (p.Length > 1) l.Pants = p[1];
            if (p.Length > 2) l.Face = p[2];
            if (p.Length > 3) l.Body = p[3];
            if (p.Length > 4) l.Hat = p[4];
            if (p.Length > 5) l.Glasses = p[5];
            if (p.Length > 6) l.Hair = p[6];
            return l;
        }

        public static string Compose(Look l)
        {
            string s = l.Shirt + "|" + l.Pants + "|" + l.Face + "|" + l.Body + "|" + l.Hat + "|" + l.Glasses + "|" + l.Hair;
            return s.TrimEnd('|');
        }

        // Celle du joueur local : [Coop] Apparence (haut), Pantalon, Visage, Corps, Chapeau, Lunettes, Cheveux.
        public static string FromConfig()
        {
            var l = new Look
            {
                Shirt = Config.Get("Coop", "Apparence", "char_shirt21"), Pants = Config.Get("Coop", "Pantalon", ""), Face = Config.Get("Coop", "Visage", ""),
                Body = Config.Get("Coop", "Corps", ""), Hat = Config.Get("Coop", "Chapeau", ""), Glasses = Config.Get("Coop", "Lunettes", ""), Hair = Config.Get("Coop", "Cheveux", "")
            };
            return Compose(l);
        }

        public static string WithShirt(string look, string shirt) { Look l = Parse(look); l.Shirt = shirt; return Compose(l); }

        // ---------------------------------------------------------------- catalogue (pris dans la scene)
        public class BodyInfo { public string Key; public Transform Char; public Mesh Mesh; public Material[] Mats; }
        public class AccInfo { public string Key; public int Kind; public Mesh Mesh; public Material[] Mats; public Vector3 Pos; public Quaternion Rot; public Vector3 Scale; }

        static Dictionary<string, BodyInfo> bodies;
        static Dictionary<string, AccInfo> accs;
        static List<string> shirts, pants, faces;

        public static void Reset() { bodies = null; accs = null; shirts = pants = faces = null; }

        public static string BodyKey(Mesh m)
        {
            if (m == null) return "";
            string pre = m.name == "bodymesh" ? "" : m.name.Replace("bodymesh_", "").Replace("bodymesh", "") + "-";
            return pre + m.vertexCount + "-" + Mathf.RoundToInt(m.bounds.size.x * 100f);
        }

        static string Clean(string n) { return n.Replace(" (Instance)", ""); }

        static int AccKind(string mesh)
        {
            string n = mesh.ToLowerInvariant();
            if (n.Contains("glasses")) return KGlasses;
            if (n.Contains("ponytail") || n.Contains("hyppyritukka")) return KHair;
            if (n.Contains("hat") || n.Contains("cap") || n.Contains("helmet") || n.Contains("latsa") || n.Contains("karvalakki")) return KHat;
            return -1;
        }

        static Transform FindBone(Transform t, string name)
        {
            if (t.name == name) return t;
            foreach (Transform c in t) { Transform r = FindBone(c, name); if (r != null) return r; }
            return null;
        }

        static void Scan()
        {
            if (bodies != null) return;
            bodies = new Dictionary<string, BodyInfo>();
            accs = new Dictionary<string, AccInfo>();
            var sh = new SortedDictionary<string, bool>();
            var pa = new SortedDictionary<string, bool>();
            var fa = new SortedDictionary<string, bool>();
            var chars = new List<Transform>();
            foreach (Object o in Resources.FindObjectsOfTypeAll(typeof(SkinnedMeshRenderer)))
            {
                var smr = (SkinnedMeshRenderer)o;
                Mesh m = smr.sharedMesh;
                if (smr.hideFlags != HideFlags.None || m == null || smr.bones == null || smr.bones.Length < 10 || !m.name.StartsWith("bodymesh")) continue;
                Transform ch = smr.transform.parent;
                if (ch == null || ch.root.name.StartsWith("MWCoop")) continue;
                Material[] mats = smr.sharedMaterials;
                if (mats.Length < 3 || mats[0] == null || mats[0].name.Contains("ghost")) continue;
                chars.Add(ch);
                for (int i = 0; i < 3; i++)
                {
                    if (mats[i] == null || mats[i].name.Contains("(Instance)")) continue;
                    (i == 0 ? sh : i == 1 ? pa : fa)[mats[i].name] = true;
                }
                if (m.name.Contains("kid") || m.vertexCount < 1200) continue;
                string key = BodyKey(m);
                BodyInfo b;
                bool usable = ch.Find("skeleton") != null && ch.GetComponentsInChildren<Animation>(true).Length > 0;
                if (!usable) continue;
                if (bodies.TryGetValue(key, out b) && (b.Char.gameObject.activeInHierarchy || !ch.gameObject.activeInHierarchy)) continue;
                bodies[key] = new BodyInfo { Key = key, Char = ch, Mesh = m, Mats = mats };
            }
            foreach (Transform ch in chars)
            {
                Transform head = FindBone(ch, "head");
                if (head == null) continue;
                foreach (Renderer r in head.GetComponentsInChildren<Renderer>(true))
                {
                    if (r is SkinnedMeshRenderer) continue;
                    MeshFilter mf = r.GetComponent<MeshFilter>();
                    if (mf == null || mf.sharedMesh == null || r.sharedMaterial == null) continue;
                    int kind = AccKind(mf.sharedMesh.name);
                    if (kind < 0) continue;
                    string key = mf.sharedMesh.name + "@" + Clean(r.sharedMaterial.name);
                    if (accs.ContainsKey(key)) continue;
                    Matrix4x4 rel = head.worldToLocalMatrix * r.transform.localToWorldMatrix;
                    Vector3 sx = rel.GetColumn(0), sy = rel.GetColumn(1), sz = rel.GetColumn(2);
                    var mats = new List<Material>();
                    foreach (Material x in r.sharedMaterials) if (x != null) mats.Add(x);
                    accs[key] = new AccInfo
                    {
                        Key = key, Kind = kind, Mesh = mf.sharedMesh, Mats = mats.ToArray(),
                        Pos = rel.GetColumn(3), Rot = Quaternion.LookRotation(sz, sy), Scale = new Vector3(sx.magnitude, sy.magnitude, sz.magnitude)
                    };
                }
            }
            shirts = new List<string>(sh.Keys);
            pants = new List<string>(pa.Keys);
            faces = new List<string>(fa.Keys);
            var bl = new List<string>(bodies.Keys);
            var al = new List<string>(accs.Keys);
            Log.Info("apparences : " + bodies.Count + " corps (" + string.Join(", ", bl.ToArray()) + "), " + accs.Count + " accessoires, "
                     + shirts.Count + " hauts, " + pants.Count + " pantalons, " + faces.Count + " visages");
            Log.Info("apparences : accessoires " + string.Join(", ", al.ToArray()));
        }

        public static BodyInfo Body(string key) { Scan(); BodyInfo b; return !string.IsNullOrEmpty(key) && bodies.TryGetValue(key, out b) && b.Char != null ? b : null; }
        public static AccInfo Acc(string key) { Scan(); AccInfo a; return !string.IsNullOrEmpty(key) && accs.TryGetValue(key, out a) && a.Mesh != null ? a : null; }
        public static List<string> Bodies() { Scan(); return new List<string>(bodies.Keys); }
        public static List<string> Accessories(int kind) { Scan(); var l = new List<string>(); foreach (AccInfo a in accs.Values) if (a.Kind == kind) l.Add(a.Key); l.Sort(); return l; }
        public static List<string> Pants() { Scan(); return pants; }
        public static List<string> Faces() { Scan(); return faces; }

        // ---------------------------------------------------------------- export pour le lanceur
        // cache\perso.mesh, petit-boutiste. Chaines : u16 longueur + UTF-8. Matrices : 16 x f32 (m00, m10, m20, m30,
        // m01... : colonne par colonne). Sommets : 8 x f32 (position x y z, normale x y z, u v), repere de l'avatar
        // (pieds a l'origine, face vers +z, metres, repere gauche d'Unity).
        //   "MWCP", u32 version (1), chaine version du mod,
        //   3 listes (hauts, pantalons, visages) : u32 n + n chaines,
        //   u32 corps : chaine cle, 3 chaines matieres du modele, matrice de la tete, u32 sommets + sommets,
        //               u32 sous-maillages + (u32 indices + indices) chacun,
        //   u32 accessoires : chaine cle, u8 genre (0 chapeau, 1 lunettes, 2 cheveux), u32 matieres + chaines,
        //               matrice (repere de la tete), u32 sommets + sommets, u32 sous-maillages + indices chacun.
        // Textures : cache\perso\<matiere>.png (256 px au plus) ; couleurs des matieres sans texture : couleurs.txt.
        public static string PreviewPath { get { return Path.Combine(Path.Combine(System.Environment.GetEnvironmentVariable("MWCOOP_DIR") ?? "MWCoop", "cache"), "perso.mesh"); } }
        static string TexDir { get { return Path.Combine(Path.GetDirectoryName(PreviewPath), "perso"); } }

        public static bool PreviewFresh()
        {
            try
            {
                if (!File.Exists(PreviewPath)) return false;
                using (var r = new BinaryReader(File.OpenRead(PreviewPath)))
                {
                    if (new string(r.ReadChars(4)) != "MWCP" || r.ReadUInt32() != 1) return false;
                    int n = r.ReadUInt16();
                    return System.Text.Encoding.UTF8.GetString(r.ReadBytes(n)) == Version.Text;
                }
            }
            catch { return false; }
        }

        static void Str(BinaryWriter w, string s) { byte[] b = System.Text.Encoding.UTF8.GetBytes(s ?? ""); w.Write((ushort)b.Length); w.Write(b); }
        static void Mat(BinaryWriter w, Matrix4x4 m) { for (int c = 0; c < 4; c++) for (int r = 0; r < 4; r++) w.Write(m[r, c]); }

        static void Verts(BinaryWriter w, Vector3[] p, Vector3[] n, Vector2[] uv)
        {
            w.Write((uint)p.Length);
            for (int i = 0; i < p.Length; i++)
            {
                Vector3 q = p[i], nn = n != null && i < n.Length ? n[i] : Vector3.up;
                Vector2 u = uv != null && i < uv.Length ? uv[i] : Vector2.zero;
                w.Write(q.x); w.Write(q.y); w.Write(q.z); w.Write(nn.x); w.Write(nn.y); w.Write(nn.z); w.Write(u.x); w.Write(u.y);
            }
        }
        static void Subs(BinaryWriter w, Mesh m)
        {
            w.Write((uint)m.subMeshCount);
            for (int s = 0; s < m.subMeshCount; s++)
            {
                int[] t = m.GetTriangles(s);
                w.Write((uint)t.Length);
                foreach (int i in t) w.Write((uint)i);
            }
        }

        // Pose debout de l'avatar d'apercu (b : son corps) : peau calculee a la main (os x matrices de liaison), dans
        // le repere de sa racine.
        static void Skin(SkinnedMeshRenderer smr, Transform root, out Vector3[] pos, out Vector3[] nrm)
        {
            Mesh m = smr.sharedMesh;
            Vector3[] v = m.vertices, n = m.normals;
            BoneWeight[] bw = m.boneWeights;
            Matrix4x4[] bp = m.bindposes;
            Transform[] bones = smr.bones;
            var mats = new Matrix4x4[bones.Length];
            for (int i = 0; i < bones.Length; i++) mats[i] = root.worldToLocalMatrix * (bones[i] != null ? bones[i].localToWorldMatrix : Matrix4x4.identity) * bp[i];
            pos = new Vector3[v.Length];
            nrm = new Vector3[v.Length];
            for (int i = 0; i < v.Length; i++)
            {
                BoneWeight b = bw.Length > i ? bw[i] : new BoneWeight { weight0 = 1f };
                Vector3 p = Vector3.zero, q = Vector3.zero;
                int[] bi = { b.boneIndex0, b.boneIndex1, b.boneIndex2, b.boneIndex3 };
                float[] wt = { b.weight0, b.weight1, b.weight2, b.weight3 };
                for (int k = 0; k < 4; k++)
                {
                    if (wt[k] <= 0f || bi[k] >= mats.Length) continue;
                    p += mats[bi[k]].MultiplyPoint3x4(v[i]) * wt[k];
                    if (n != null && i < n.Length) q += mats[bi[k]].MultiplyVector(n[i]) * wt[k];
                }
                pos[i] = p;
                nrm[i] = q.sqrMagnitude > 0f ? q.normalized : Vector3.up;
            }
        }

        static void SaveTexture(Material m, HashSet<string> done, List<string> colors)
        {
            if (m == null) return;
            string name = Clean(m.name);
            if (!done.Add(name) || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return;
            if (m.HasProperty("_Color")) { Color c = m.color; colors.Add(name + "\t" + c.r.ToString("F3", System.Globalization.CultureInfo.InvariantCulture) + "," + c.g.ToString("F3", System.Globalization.CultureInfo.InvariantCulture) + "," + c.b.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)); }
            Texture t = m.mainTexture;
            if (t == null) return;
            int w = Mathf.Min(256, t.width), h = Mathf.Max(1, t.height * w / Mathf.Max(1, t.width));
            var rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32);
            RenderTexture prev = RenderTexture.active;
            Graphics.Blit(t, rt);
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.ARGB32, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            string f = Path.Combine(TexDir, name + ".png");
            File.WriteAllBytes(f + ".tmp", tex.EncodeToPNG());
            if (File.Exists(f)) File.Delete(f);
            File.Move(f + ".tmp", f);
            Object.Destroy(tex);
        }

        public static string Export(int layer)
        {
            Scan();
            Directory.CreateDirectory(TexDir);
            var done = new HashSet<string>();
            var colors = new List<string>();
            string tmp = PreviewPath + ".tmp";
            int nb = 0, na = 0;
            using (var w = new BinaryWriter(File.Create(tmp)))
            {
                w.Write(new[] { (byte)'M', (byte)'W', (byte)'C', (byte)'P' });
                w.Write((uint)1);
                Str(w, Version.Text);
                foreach (List<string> l in new[] { shirts, pants, faces })
                {
                    w.Write((uint)l.Count);
                    foreach (string s in l) { Str(w, s); SaveTexture(Avatar.FindMaterial(s), done, colors); }
                }
                var keys = new List<string>();
                foreach (string k in bodies.Keys) keys.Add(k);
                var models = new List<Avatar>();
                var heads = new List<Matrix4x4>();
                var skinned = new List<KeyValuePair<Vector3[], Vector3[]>>();
                var meshes = new List<Mesh>();
                var defs = new List<Material[]>();
                foreach (string k in keys)
                {
                    Avatar a = Avatar.CreatePreview("MWCoop-Export-" + k, layer, k);
                    if (a == null) continue;
                    a.PoseStanding(0f);
                    Vector3[] p, n;
                    SkinnedMeshRenderer smr = a.BodyRenderer;
                    Skin(smr, a.Root.transform, out p, out n);
                    skinned.Add(new KeyValuePair<Vector3[], Vector3[]>(p, n));
                    meshes.Add(smr.sharedMesh);
                    defs.Add(smr.sharedMaterials);
                    heads.Add(a.HeadBone != null ? a.Root.transform.worldToLocalMatrix * a.HeadBone.localToWorldMatrix : Matrix4x4.identity);
                    models.Add(a);
                }
                w.Write((uint)models.Count);
                for (int i = 0; i < models.Count; i++)
                {
                    Str(w, models[i].Body);
                    for (int k = 0; k < 3; k++) { Material dm = defs[i].Length > k ? defs[i][k] : null; Str(w, dm != null ? Clean(dm.name) : ""); SaveTexture(dm, done, colors); }
                    Mat(w, heads[i]);
                    Verts(w, skinned[i].Key, skinned[i].Value, meshes[i].uv);
                    Subs(w, meshes[i]);
                    models[i].Destroy();
                    nb++;
                }
                w.Write((uint)accs.Count);
                foreach (AccInfo ai in accs.Values)
                {
                    Str(w, ai.Key);
                    w.Write((byte)ai.Kind);
                    w.Write((uint)ai.Mats.Length);
                    foreach (Material m in ai.Mats) { Str(w, Clean(m.name)); SaveTexture(m, done, colors); }
                    Mat(w, Matrix4x4.TRS(ai.Pos, ai.Rot, ai.Scale));
                    Verts(w, ai.Mesh.vertices, ai.Mesh.normals, ai.Mesh.uv);
                    Subs(w, ai.Mesh);
                    na++;
                }
            }
            File.WriteAllLines(Path.Combine(TexDir, "couleurs.txt"), colors.ToArray());
            if (File.Exists(PreviewPath)) File.Delete(PreviewPath);
            File.Move(tmp, PreviewPath);
            return nb + " corps, " + na + " accessoires, " + done.Count + " matieres, " + PreviewPath;
        }
    }
}
