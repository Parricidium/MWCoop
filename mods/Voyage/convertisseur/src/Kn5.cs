using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Convertisseur
{
    // Lecture d'un fichier .kn5 d'Assetto Corsa (modeles 3D des circuits et des voitures) : textures (DDS le plus souvent,
    // gardees telles quelles), materiaux (shader, transparence, textures par emplacement), arbre des noeuds (reperes,
    // maillages ; matrices 4x4 en lignes, translation dans la derniere ligne). Les sommets restent dans le fichier : on garde
    // leur position (Offset) et on les lit a l'ecriture. Format : « sc6969 », version (5 ou 6, +1 entier en 6).
    public class Kn5
    {
        public class Texture { public string Name; public int Offset, Size; }
        public class Material
        {
            public string Name, Shader;
            public byte Blend, AlphaTest;
            public int Depth;   // (0 normal ; 1 lecture seule, 2 sans profondeur : decalques poses sur la route)
            public readonly Dictionary<string, string> Samplers = new Dictionary<string, string>();   // txDiffuse -> nom de texture
            public readonly Dictionary<string, float> Props = new Dictionary<string, float>();
        }
        public class Mesh
        {
            public string Name, Path;
            public bool Visible, Renderable, Transparent;
            public int VertexCount, VertexOffset, IndexCount, IndexOffset, MaterialId, Layer;
            public float LodIn, LodOut;
            public Mat World;
        }
        public class Dummy { public string Name; public Mat World; }

        public string File;
        public byte[] Data;
        public int Version;
        public readonly List<Texture> Textures = new List<Texture>();
        public readonly List<Material> Materials = new List<Material>();
        public readonly List<Mesh> Meshes = new List<Mesh>();
        public readonly List<Dummy> Dummies = new List<Dummy>();
        int p;

        public static Kn5 Read(string file, Mat root)
        {
            var k = new Kn5 { File = file, Data = System.IO.File.ReadAllBytes(file) };
            k.Parse(root);
            return k;
        }

        int I() { int v = BitConverter.ToInt32(Data, p); p += 4; return v; }
        float F() { float v = BitConverter.ToSingle(Data, p); p += 4; return v; }
        byte B() { return Data[p++]; }
        string S() { int n = I(); if (n < 0 || n > 4096 || p + n > Data.Length) throw new InvalidDataException("nom illisible a " + p); string s = Encoding.UTF8.GetString(Data, p, n); p += n; return s; }

        void Parse(Mat root)
        {
            if (Data.Length < 10 || Encoding.ASCII.GetString(Data, 0, 6) != "sc6969") throw new InvalidDataException("ce n'est pas un fichier .kn5 lisible (protege ou chiffre ?)");
            p = 6;
            Version = I();
            if (Version > 5) I();
            int nt = I();
            for (int k = 0; k < nt; k++)
            {
                I(); string name = S(); int size = I();
                if (size < 0 || p + size > Data.Length) throw new InvalidDataException("texture " + name + " illisible");
                Textures.Add(new Texture { Name = name, Offset = p, Size = size });
                p += size;
            }
            int nm = I();
            for (int k = 0; k < nm; k++)
            {
                var m = new Material { Name = S(), Shader = S() };
                m.Blend = B(); m.AlphaTest = B(); m.Depth = I();
                int np = I();
                for (int j = 0; j < np; j++) { string pn = S(); m.Props[pn] = F(); p += 36; }
                int ns = I();
                for (int j = 0; j < ns; j++) { string sn = S(); I(); m.Samplers[sn] = S(); }
                Materials.Add(m);
            }
            Node(root, "");
            if (p != Data.Length) throw new InvalidDataException("fin du fichier inattendue (" + p + " / " + Data.Length + ")");
        }

        void Node(Mat parent, string path)
        {
            int cls = I(); string name = S(); int nch = I(); B();
            Mat world = parent;
            string here = path.Length > 0 ? path + "/" + name : name;
            if (cls == 1)
            {
                var m = new Mat();
                for (int i = 0; i < 16; i++) m.M[i] = F();
                world = Mat.Mul(m, parent);
                if (name.StartsWith("AC_", StringComparison.Ordinal)) Dummies.Add(new Dummy { Name = name, World = world });
            }
            else if (cls == 2)
            {
                var me = new Mesh { Name = name, Path = here, World = parent };
                B(); me.Visible = B() != 0; me.Transparent = B() != 0;
                me.VertexCount = I(); me.VertexOffset = p; p += me.VertexCount * 44;
                me.IndexCount = I(); me.IndexOffset = p; p += me.IndexCount * 2;
                me.MaterialId = I(); me.Layer = I(); me.LodIn = F(); me.LodOut = F();
                p += 16;   // (sphere englobante)
                me.Renderable = B() != 0;
                Meshes.Add(me);
            }
            else if (cls == 3)   // (maillage avec os : voitures, spectateurs animes ; pas pour un circuit statique)
            {
                B(); B(); B();
                int nb = I();
                for (int j = 0; j < nb; j++) { S(); p += 64; }
                int nv = I(); p += nv * 76; int ni = I(); p += ni * 2; I(); I(); p += 8;
            }
            else throw new InvalidDataException("noeud de type " + cls + " inconnu (fichier protege ?)");
            for (int j = 0; j < nch; j++) Node(world, here);
        }
    }

    // Matrice 4x4 en lignes (convention d'Assetto Corsa / Direct3D : point * matrice, translation en M[12..14])
    public class Mat
    {
        public readonly float[] M = new float[16];
        public static Mat Identity { get { var m = new Mat(); m.M[0] = m.M[5] = m.M[10] = m.M[15] = 1f; return m; } }
        public static Mat Mul(Mat a, Mat b)
        {
            var r = new Mat();
            for (int i = 0; i < 4; i++)
                for (int j = 0; j < 4; j++)
                {
                    float s = 0f;
                    for (int k = 0; k < 4; k++) s += a.M[i * 4 + k] * b.M[k * 4 + j];
                    r.M[i * 4 + j] = s;
                }
            return r;
        }
        public static Mat Translation(float x, float y, float z) { var m = Identity; m.M[12] = x; m.M[13] = y; m.M[14] = z; return m; }
        public void Point(float x, float y, float z, out float ox, out float oy, out float oz)
        {
            ox = x * M[0] + y * M[4] + z * M[8] + M[12];
            oy = x * M[1] + y * M[5] + z * M[9] + M[13];
            oz = x * M[2] + y * M[6] + z * M[10] + M[14];
        }
        public void Dir(float x, float y, float z, out float ox, out float oy, out float oz)
        {
            ox = x * M[0] + y * M[4] + z * M[8];
            oy = x * M[1] + y * M[5] + z * M[9];
            oz = x * M[2] + y * M[6] + z * M[10];
        }
        public bool Identityish { get { return Math.Abs(M[0] - 1) < 1e-5 && Math.Abs(M[5] - 1) < 1e-5 && Math.Abs(M[10] - 1) < 1e-5 && Math.Abs(M[1]) + Math.Abs(M[2]) + Math.Abs(M[4]) + Math.Abs(M[6]) + Math.Abs(M[8]) + Math.Abs(M[9]) < 1e-5; } }
    }
}
