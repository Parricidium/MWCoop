using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace MWCoop
{
    // Lecture du fichier de la scene du jeu (levelN, format d'Unity 5.0 : version 14, sans arbre des types) pour les lots
    // statiques : Unity ne garde pas en memoire les sommets des « Combined Mesh » (illisibles par les scripts), mais ils sont
    // dans le fichier. Seulement ce qu'il faut a l'export de la carte (MapExport.cs) : les maillages regroupes (sommets,
    // normales, uv, sous-maillages, indices 16 bits) et, pour chaque objet regroupe, son nom, ses matieres et ses sous-maillages
    // (m_SubsetIndices). Dispositions d'Unity 5.0 (comme AssetStudio) ; chaque maillage lu est verifie contre le jeu (nombre de
    // sommets et de sous-maillages), sinon ignore. Lecture seule, rien n'est copie hors du PC.
    public class SceneFile
    {
        public class Obj { public long PathId; public long Start; public int Size; public int ClassId; }
        public class Batched { public string Name, MeshName, Path; public int MeshFile; public long MeshPath; public string[] Mats; public int[] Subsets; public bool Used; }
        public class MeshData { public string Name; public int VertexCount; public Vector3[] V; public Vector3[] N; public Vector2[] Uv; public int[][] Subs; }

        readonly string path;
        readonly Dictionary<long, Obj> objs = new Dictionary<long, Obj>();
        readonly List<string> externals = new List<string>();
        readonly Dictionary<string, SceneFile> others = new Dictionary<string, SceneFile>();
        long dataOffset;
        BinaryReader r;
        public string Error;

        public SceneFile(string path) { this.path = path; }
        public string FilePath { get { return path; } }

        public bool Open()
        {
            try
            {
                r = new BinaryReader(File.OpenRead(path));
                uint metaSize = BE(r.ReadUInt32()); BE(r.ReadUInt32());
                uint version = BE(r.ReadUInt32());
                dataOffset = BE(r.ReadUInt32());
                byte endian = r.ReadByte(); r.ReadBytes(3);
                if (version != 14 || endian != 0) { Error = "format " + version + "/" + endian + " (attendu 14, Unity 5.0)"; return false; }
                CStr();                       // version d'Unity
                r.ReadInt32();                // plateforme
                bool tree = r.ReadBoolean();
                if (tree) { Error = "arbre des types present (non prevu)"; return false; }
                int types = r.ReadInt32();
                for (int i = 0; i < types; i++) { int cid = r.ReadInt32(); if (cid < 0) r.ReadBytes(16); r.ReadBytes(16); }
                int count = r.ReadInt32();
                for (int i = 0; i < count; i++)
                {
                    Align();
                    var o = new Obj { PathId = r.ReadInt64(), Start = r.ReadUInt32() };
                    o.Size = (int)r.ReadUInt32();
                    r.ReadInt32();                // typeID
                    o.ClassId = r.ReadUInt16();
                    r.ReadInt16();                // scriptTypeIndex
                    objs[o.PathId] = o;
                }
                int scripts = r.ReadInt32();
                for (int i = 0; i < scripts; i++) { r.ReadInt32(); Align(); r.ReadInt64(); }
                int ext = r.ReadInt32();
                for (int i = 0; i < ext; i++) { CStr(); r.ReadBytes(16); r.ReadInt32(); externals.Add(CStr()); }
                return true;
            }
            catch (Exception e) { Error = e.Message; return false; }
        }

        public void Close()
        {
            if (r != null) r.Close();
            r = null;
            foreach (SceneFile f in others.Values) f.Close();
        }

        static uint BE(uint v) { return (v >> 24) | ((v >> 8) & 0xFF00) | ((v << 8) & 0xFF0000) | (v << 24); }
        string CStr() { var b = new List<byte>(); byte c; while ((c = r.ReadByte()) != 0) b.Add(c); return Encoding.UTF8.GetString(b.ToArray()); }
        void Align() { long p = r.BaseStream.Position; if ((p & 3) != 0) r.BaseStream.Position = p + (4 - (p & 3)); }
        string Str() { int n = r.ReadInt32(); string s = Encoding.UTF8.GetString(r.ReadBytes(n)); Align(); return s; }
        void Seek(Obj o) { r.BaseStream.Position = dataOffset + o.Start; }
        void PPtr(out int file, out long id) { file = r.ReadInt32(); id = r.ReadInt64(); }

        public int Count(int classId) { int n = 0; foreach (Obj o in objs.Values) if (o.ClassId == classId) n++; return n; }

        // Nom d'un objet (premier champ m_Name : Material, GameObject apres ses composants -- voir Name pour GameObject)
        string NamedObject(long id)
        {
            Obj o;
            if (!objs.TryGetValue(id, out o)) return null;
            Seek(o);
            return Str();
        }

        string GameObjectName(long id)
        {
            Obj o;
            if (!objs.TryGetValue(id, out o) || o.ClassId != 1) return null;
            Seek(o);
            int n = r.ReadInt32();
            for (int i = 0; i < n; i++) { r.ReadInt32(); int f; long p; PPtr(out f, out p); }
            r.ReadInt32();   // m_Layer
            return Str();
        }

        // Fichier d'un PPtr : 0 celui-ci, sinon le n-ieme fichier externe (sharedassetsN.assets, a cote)
        SceneFile Other(int file)
        {
            if (file == 0) return this;
            if (file - 1 >= externals.Count) return null;
            string name = Path.GetFileName(externals[file - 1].Replace('\\', '/'));
            SceneFile other;
            if (!others.TryGetValue(name, out other))
            {
                string p = Path.Combine(Path.GetDirectoryName(path), name);
                other = new SceneFile(p);
                if (!File.Exists(p) || !other.Open()) other = null;
                others[name] = other;
            }
            return other;
        }

        string MaterialName(int file, long id) { SceneFile f = Other(file); return f != null ? f.NamedObject(id) : null; }

        public MeshData ReadMesh(int file, long id) { SceneFile f = Other(file); return f != null ? f.ReadMesh(id) : null; }

        // Hierarchie du fichier (Transform, Unity 5.0 : m_GameObject, rotation, position, echelle, m_Children, m_Father) :
        // chemin unique d'un GameObject, comme MapExport.UniquePath dans le jeu (« nom#i » : i freres de meme nom avant lui).
        Dictionary<long, long> trOfGo, fatherOf, goOfTr;
        Dictionary<long, long[]> childrenOf;
        readonly Dictionary<long, string> goNames = new Dictionary<long, string>();
        void ReadTransforms()
        {
            trOfGo = new Dictionary<long, long>(); fatherOf = new Dictionary<long, long>(); goOfTr = new Dictionary<long, long>(); childrenOf = new Dictionary<long, long[]>();
            foreach (Obj o in objs.Values)
            {
                if (o.ClassId != 4) continue;
                Seek(o);
                int f; long go, father;
                PPtr(out f, out go);
                r.BaseStream.Position += 16 + 12 + 12;
                int n = r.ReadInt32();
                if (n < 0 || n > 100000) continue;
                var ch = new long[n];
                for (int i = 0; i < n; i++) { long c; PPtr(out f, out c); ch[i] = c; }
                PPtr(out f, out father);
                trOfGo[go] = o.PathId; goOfTr[o.PathId] = go; fatherOf[o.PathId] = father; childrenOf[o.PathId] = ch;
            }
        }
        string NameOfGo(long go) { string n; if (!goNames.TryGetValue(go, out n)) goNames[go] = n = GameObjectName(go) ?? ""; return n; }
        public string PathOf(long go)
        {
            if (trOfGo == null) ReadTransforms();
            long tr;
            if (!trOfGo.TryGetValue(go, out tr)) return null;
            var parts = new List<string>();
            for (int guard = 0; tr != 0 && guard < 64; guard++)
            {
                long g = goOfTr[tr], father;
                string name = NameOfGo(g);
                int same = 0;
                if (fatherOf.TryGetValue(tr, out father) && father != 0)
                {
                    long[] sib;
                    if (childrenOf.TryGetValue(father, out sib))
                        foreach (long s in sib) { if (s == tr) break; long sg; if (goOfTr.TryGetValue(s, out sg) && NameOfGo(sg) == name) same++; }
                }
                parts.Add(same > 0 ? name + "#" + same : name);
                if (!fatherOf.TryGetValue(tr, out tr)) break;
            }
            parts.Reverse();
            return string.Join("/", parts.ToArray());
        }

        // Objets regroupes : MeshFilter (GameObject -> maillage), puis MeshRenderer avec des sous-maillages de lot.
        public List<Batched> ReadBatched()
        {
            var meshOf = new Dictionary<long, long>();
            var meshFile = new Dictionary<long, int>();
            foreach (Obj o in objs.Values)
            {
                if (o.ClassId != 33) continue;   // MeshFilter
                Seek(o);
                int f, mfi; long go, mesh;
                PPtr(out f, out go); PPtr(out mfi, out mesh);
                meshOf[go] = mesh; meshFile[go] = mfi;
            }
            var list = new List<Batched>();
            var meshNames = new Dictionary<long, string>();
            foreach (Obj o in objs.Values)
            {
                if (o.ClassId != 23) continue;   // MeshRenderer
                Seek(o);
                int f; long go;
                PPtr(out f, out go);
                r.ReadBoolean(); r.ReadByte(); r.ReadBoolean(); Align();   // m_Enabled, m_CastShadows, m_ReceiveShadows
                r.ReadUInt16(); r.ReadUInt16(); r.ReadInt32();             // m_LightmapIndex, m_LightmapIndexDynamic, (4 octets : -1)
                r.ReadBytes(32);                                           // m_LightmapTilingOffset, ...Dynamic
                int nm = r.ReadInt32();
                if (nm < 0 || nm > 64) continue;
                var mf = new int[nm]; var mp = new long[nm];
                for (int i = 0; i < nm; i++) PPtr(out mf[i], out mp[i]);
                int ns = r.ReadInt32();
                if (ns <= 0 || ns > 256) continue;
                var subs = new int[ns];
                for (int i = 0; i < ns; i++) subs[i] = (int)r.ReadUInt32();
                long mesh;
                if (!meshOf.TryGetValue(go, out mesh)) continue;
                var b = new Batched { MeshFile = meshFile[go], MeshPath = mesh, Subsets = subs, Mats = new string[nm] };
                for (int i = 0; i < nm; i++) b.Mats[i] = MaterialName(mf[i], mp[i]) ?? "";
                b.Name = NameOfGo(go);
                try { b.Path = PathOf(go); } catch { }
                long mk = mesh * 64 + b.MeshFile;
                string mn;
                if (!meshNames.TryGetValue(mk, out mn)) { SceneFile mf2 = Other(b.MeshFile); meshNames[mk] = mn = (mf2 != null ? mf2.NamedObject(mesh) : null) ?? ""; }
                b.MeshName = mn;
                list.Add(b);
            }
            return list;
        }

        // Maillage (Unity 5.0) : sommets, normales, uv0, triangles de chaque sous-maillage. null si la lecture ne tient pas.
        public MeshData ReadMesh(long id)
        {
            Obj o;
            if (!objs.TryGetValue(id, out o) || o.ClassId != 43) return null;
            Seek(o);
            var m = new MeshData { Name = Str() };
            int nsub = r.ReadInt32();
            if (nsub < 0 || nsub > 100000) return null;
            var first = new uint[nsub]; var cnt = new uint[nsub]; var topo = new int[nsub];
            for (int i = 0; i < nsub; i++)
            {
                first[i] = r.ReadUInt32(); cnt[i] = r.ReadUInt32(); topo[i] = r.ReadInt32();
                r.ReadUInt32(); r.ReadUInt32();      // firstVertex, vertexCount
                r.ReadBytes(24);                     // localAABB
            }
            // m_Shapes (BlendShapeData)
            int nv = r.ReadInt32(); r.BaseStream.Position += (long)nv * 40;
            int nshape = r.ReadInt32();
            for (int i = 0; i < nshape; i++) { r.ReadUInt32(); r.ReadUInt32(); r.ReadBoolean(); r.ReadBoolean(); Align(); }
            int nch = r.ReadInt32();
            for (int i = 0; i < nch; i++) { Str(); r.ReadUInt32(); r.ReadInt32(); r.ReadInt32(); }
            int nw = r.ReadInt32(); r.BaseStream.Position += (long)nw * 4;
            int nbind = r.ReadInt32(); r.BaseStream.Position += (long)nbind * 64;   // m_BindPose
            int nhash = r.ReadInt32(); r.BaseStream.Position += (long)nhash * 4;    // m_BoneNameHashes
            r.ReadUInt32();                                                          // m_RootBoneNameHash
            r.ReadByte(); r.ReadBoolean(); r.ReadBoolean(); r.ReadBoolean(); Align();   // compression, lisible, garder sommets / indices
            int ib = r.ReadInt32();
            if (ib < 0 || ib > o.Size) return null;
            byte[] idx = r.ReadBytes(ib); Align();
            int nskin = r.ReadInt32(); r.BaseStream.Position += (long)nskin * 32;   // m_Skin
            // m_VertexData
            r.ReadUInt32();                          // m_CurrentChannels
            m.VertexCount = (int)r.ReadUInt32();
            int nchan = r.ReadInt32();
            if (nchan <= 0 || nchan > 16) return null;
            var cs = new byte[nchan]; var co = new byte[nchan]; var cf = new byte[nchan]; var cd = new byte[nchan];
            for (int i = 0; i < nchan; i++) { cs[i] = r.ReadByte(); co[i] = r.ReadByte(); cf[i] = r.ReadByte(); cd[i] = r.ReadByte(); }
            int size = r.ReadInt32();
            if (size < 0 || size > o.Size) return null;
            byte[] data = r.ReadBytes(size);
            // flux : canaux du flux s a la suite, debut de chaque flux aligne sur 16 octets (GetStreams d'Unity 5)
            int nstream = 0;
            for (int i = 0; i < nchan; i++) if (cd[i] > 0) nstream = Math.Max(nstream, cs[i] + 1);
            var soff = new int[nstream]; var sstride = new int[nstream];
            int off = 0;
            for (int s = 0; s < nstream; s++)
            {
                int stride = 0;
                for (int i = 0; i < nchan; i++) if (cs[i] == s && cd[i] > 0) stride += cd[i] * FormatSize(cf[i]);
                soff[s] = off; sstride[s] = stride;
                off += m.VertexCount * stride;
                off = (off + 15) & ~15;
            }
            int n = m.VertexCount;
            m.V = Channel3(data, 0, n, cs, co, cf, cd, soff, sstride);
            if (m.V == null) return null;
            m.N = nchan > 1 ? Channel3(data, 1, n, cs, co, cf, cd, soff, sstride) : null;
            if (nchan > 3 && cd[3] >= 2 && (cf[3] == 0 || cf[3] == 1))   // uv0 : flottants, ou demi-flottants (lots de MSC)
            {
                m.Uv = new Vector2[n];
                int s = cs[3], b0 = soff[s] + co[3], w = cf[3] == 0 ? 4 : 2;
                for (int k = 0; k < n; k++)
                {
                    int p = b0 + k * sstride[s];
                    if (p + 2 * w > data.Length) return null;
                    m.Uv[k] = w == 4 ? new Vector2(BitConverter.ToSingle(data, p), BitConverter.ToSingle(data, p + 4)) : new Vector2(Half(data, p), Half(data, p + 2));
                }
            }
            m.Subs = new int[nsub][];
            for (int i = 0; i < nsub; i++)
            {
                if (topo[i] != 0) { m.Subs[i] = new int[0]; continue; }   // (triangles seulement)
                int a = (int)(first[i] / 2), c = (int)cnt[i];
                if ((a + c) * 2 > idx.Length) return null;
                var t = new int[c];
                for (int k = 0; k < c; k++) { t[k] = BitConverter.ToUInt16(idx, (a + k) * 2); if (t[k] >= n) return null; }
                m.Subs[i] = t;
            }
            return m;
        }

        static int FormatSize(byte f) { return f == 0 ? 4 : f == 1 ? 2 : 1; }

        static float Half(byte[] d, int p)
        {
            int h = d[p] | (d[p + 1] << 8);
            int sign = (h >> 15) & 1, e = (h >> 10) & 31, f = h & 1023;
            float v = e == 0 ? f * (1f / 16777216f) : e == 31 ? (f == 0 ? float.PositiveInfinity : float.NaN) : (1f + f / 1024f) * Mathf.Pow(2f, e - 15);
            return sign != 0 ? -v : v;
        }

        static Vector3[] Channel3(byte[] data, int ch, int n, byte[] cs, byte[] co, byte[] cf, byte[] cd, int[] soff, int[] sstride)
        {
            if (cd[ch] < 3 || cf[ch] != 0) return null;
            int s = cs[ch], b0 = soff[s] + co[ch];
            var v = new Vector3[n];
            for (int k = 0; k < n; k++)
            {
                int p = b0 + k * sstride[s];
                if (p + 12 > data.Length) return null;
                v[k] = new Vector3(BitConverter.ToSingle(data, p), BitConverter.ToSingle(data, p + 4), BitConverter.ToSingle(data, p + 8));
            }
            return v;
        }
    }
}
