using System;
using System.IO;

namespace Convertisseur
{
    // Textures DDS des .kn5 -> .vtex du mod Voyage, sans decodage : DXT1 et DXT5 gardes tels quels (le jeu les charge
    // directement, LoadRawTextureData), BGRA 32 bits aussi (RGBA recopie dans l'ordre BGRA). Reduction = on saute les
    // plus grands niveaux de detail (mipmaps) au-dessus de la taille choisie. Lignes dans l'ordre du fichier : avec les
    // coordonnees de texture de Direct3D (v vers le bas), l'image tombe juste dans Unity sans rien retourner.
    // .vtex : « VOYT », format (1 DXT1, 5 DXT5, 32 BGRA32), largeur, hauteur, niveaux, octets, donnees.
    public static class Dds
    {
        public class Result { public byte[] Vtex; public int Width, Height, Format; public bool Supported; public string Note = ""; }

        public static Result Convert(byte[] d, int off, int size, int maxSize)
        {
            var r = new Result();
            if (size < 128 || d[off] != 'D' || d[off + 1] != 'D' || d[off + 2] != 'S' || d[off + 3] != ' ')
            {
                r.Note = size >= 4 && d[off] == 0x89 && d[off + 1] == 'P' ? "png" : "inconnu";
                return Grey(r);
            }
            int h = BitConverter.ToInt32(d, off + 12), w = BitConverter.ToInt32(d, off + 16);
            int mips = Math.Max(1, BitConverter.ToInt32(d, off + 28));
            int pfFlags = BitConverter.ToInt32(d, off + 80);
            string fourcc = System.Text.Encoding.ASCII.GetString(d, off + 84, 4);
            int bits = BitConverter.ToInt32(d, off + 88);
            uint rmask = BitConverter.ToUInt32(d, off + 92), amask = BitConverter.ToUInt32(d, off + 104);
            int data = off + 128, end = off + size;
            int fmt;
            if ((pfFlags & 4) != 0)
            {
                if (fourcc == "DXT1") fmt = 1;
                else if (fourcc == "DXT5") fmt = 5;
                else if (fourcc == "DX10")
                {
                    int dxgi = BitConverter.ToInt32(d, off + 128);
                    data += 20;
                    if (dxgi == 71 || dxgi == 72) fmt = 1;
                    else if (dxgi == 77 || dxgi == 78) fmt = 5;
                    else if (dxgi == 87 || dxgi == 91) fmt = 32;
                    else if (dxgi == 28 || dxgi == 29) fmt = 33;
                    else { r.Note = "DX10 format " + dxgi; return Grey(r); }
                }
                else { r.Note = fourcc.Trim('\0'); return Grey(r); }
            }
            else if (bits == 32) fmt = rmask == 0x00ff0000 ? 32 : 33;   // (BGRA tel quel ; RGBA : rouge et bleu echanges)
            else if (bits == 16)   // (ARGB 4444 / RGB 565 : decodees puis recompressees)
            {
                int dw, dh;
                byte[] px = Decode(d, off, size, maxSize, out dw, out dh);
                if (px == null || dw % 4 != 0 || dh % 4 != 0) { r.Note = "16 bits"; return Grey(r); }
                return EncodeDxt1(px, dw, dh);
            }
            else { r.Note = bits + " bits"; return Grey(r); }
            // niveaux : tailles en octets, puis saut des plus grands
            var lw = new int[mips]; var lh = new int[mips]; var lo = new int[mips]; var ls = new int[mips];
            int pos = data;
            for (int i = 0; i < mips; i++)
            {
                lw[i] = Math.Max(1, w >> i); lh[i] = Math.Max(1, h >> i); lo[i] = pos;
                ls[i] = fmt == 1 ? Math.Max(1, (lw[i] + 3) / 4) * Math.Max(1, (lh[i] + 3) / 4) * 8
                      : fmt == 5 ? Math.Max(1, (lw[i] + 3) / 4) * Math.Max(1, (lh[i] + 3) / 4) * 16
                      : lw[i] * lh[i] * 4;
                pos += ls[i];
                if (pos > end) { mips = i; break; }
            }
            if (mips == 0) { r.Note = "tronquee"; return Grey(r); }
            int first = 0;
            while (first < mips - 1 && Math.Max(lw[first], lh[first]) > maxSize) first++;
            // (Unity veut la chaine complete jusqu'a 1x1, sinon un seul niveau ; DXT : cotes multiples de 4)
            int full = 1 + (int)Math.Floor(Math.Log(Math.Max(lw[first], lh[first]), 2));
            int count = mips - first >= full ? full : 1;
            if ((fmt == 1 || fmt == 5) && (lw[first] % 4 != 0 || lh[first] % 4 != 0)) { r.Note = "taille " + lw[first] + "x" + lh[first]; return Grey(r); }
            int bytes = 0;
            for (int i = first; i < first + count; i++) bytes += ls[i];
            var ms = new MemoryStream(32 + bytes);
            var bw = new BinaryWriter(ms);
            bw.Write(new[] { (byte)'V', (byte)'O', (byte)'Y', (byte)'T' });
            bw.Write(fmt == 33 ? 32 : fmt); bw.Write(lw[first]); bw.Write(lh[first]); bw.Write(count); bw.Write(bytes);
            if (fmt == 33)
            {
                var tmp = new byte[bytes];
                Buffer.BlockCopy(d, lo[first], tmp, 0, bytes);
                for (int i = 0; i + 3 < bytes; i += 4) { byte t = tmp[i]; tmp[i] = tmp[i + 2]; tmp[i + 2] = t; }
                bw.Write(tmp);
                fmt = 32;
            }
            else bw.Write(d, lo[first], bytes);
            r.Vtex = ms.ToArray(); r.Width = lw[first]; r.Height = lh[first]; r.Format = fmt; r.Supported = true;
            if (amask == 0 && fmt == 32) r.Note = "sans alpha";
            return r;
        }

        // ---------------------------------------------------------------- decodage (matieres multicouches d'Assetto Corsa)
        class Info { public int Fmt, Mips; public int[] W, H, Off, Size; }

        static Info Parse(byte[] d, int off, int size)
        {
            if (size < 128 || d[off] != 'D' || d[off + 1] != 'D' || d[off + 2] != 'S' || d[off + 3] != ' ') return null;
            int h = BitConverter.ToInt32(d, off + 12), w = BitConverter.ToInt32(d, off + 16);
            int mips = Math.Max(1, BitConverter.ToInt32(d, off + 28));
            int pfFlags = BitConverter.ToInt32(d, off + 80);
            string fourcc = System.Text.Encoding.ASCII.GetString(d, off + 84, 4);
            int bits = BitConverter.ToInt32(d, off + 88);
            uint rmask = BitConverter.ToUInt32(d, off + 92);
            int data = off + 128, end = off + size, fmt;
            if ((pfFlags & 4) != 0)
            {
                if (fourcc == "DXT1") fmt = 1;
                else if (fourcc == "DXT5") fmt = 5;
                else if (fourcc == "DX10")
                {
                    int dxgi = BitConverter.ToInt32(d, off + 128); data += 20;
                    fmt = dxgi == 71 || dxgi == 72 ? 1 : dxgi == 77 || dxgi == 78 ? 5 : dxgi == 87 || dxgi == 91 ? 32 : dxgi == 28 || dxgi == 29 ? 33 : 0;
                }
                else fmt = 0;
            }
            else fmt = bits == 32 ? (rmask == 0x00ff0000 ? 32 : 33) : bits == 16 && rmask == 0x0f00 ? 44 : bits == 16 && rmask == 0xf800 ? 56 : 0;   // (44 : ARGB 4444, masques ; 56 : RGB 565)
            if (fmt == 0) return null;
            var inf = new Info { Fmt = fmt, W = new int[mips], H = new int[mips], Off = new int[mips], Size = new int[mips] };
            int pos = data, n = mips;
            for (int i = 0; i < mips; i++)
            {
                inf.W[i] = Math.Max(1, w >> i); inf.H[i] = Math.Max(1, h >> i); inf.Off[i] = pos;
                int bw = Math.Max(1, (inf.W[i] + 3) / 4), bh = Math.Max(1, (inf.H[i] + 3) / 4);
                inf.Size[i] = fmt == 1 ? bw * bh * 8 : fmt == 5 ? bw * bh * 16 : fmt == 44 || fmt == 56 ? inf.W[i] * inf.H[i] * 2 : inf.W[i] * inf.H[i] * 4;
                pos += inf.Size[i];
                if (pos > end) { n = i; break; }
            }
            inf.Mips = n;
            return n > 0 ? inf : null;
        }

        // pixels RGBA du plus grand niveau dont le cote ne depasse pas maxSize (null : format non pris en charge)
        public static byte[] Decode(byte[] d, int off, int size, int maxSize, out int w, out int h)
        {
            w = h = 0;
            Info inf = Parse(d, off, size);
            if (inf == null) return null;
            int lv = 0;
            while (lv < inf.Mips - 1 && Math.Max(inf.W[lv], inf.H[lv]) > maxSize) lv++;
            w = inf.W[lv]; h = inf.H[lv];
            var px = new byte[w * h * 4];
            int o = inf.Off[lv];
            if (inf.Fmt == 32 || inf.Fmt == 33)
            {
                for (int i = 0; i < w * h; i++)
                {
                    int s = o + i * 4;
                    px[i * 4] = d[s + (inf.Fmt == 32 ? 2 : 0)]; px[i * 4 + 1] = d[s + 1]; px[i * 4 + 2] = d[s + (inf.Fmt == 32 ? 0 : 2)]; px[i * 4 + 3] = d[s + 3];
                }
                return px;
            }
            if (inf.Fmt == 44 || inf.Fmt == 56)
            {
                for (int i = 0; i < w * h; i++)
                {
                    int v = d[o + i * 2] | d[o + i * 2 + 1] << 8;
                    if (inf.Fmt == 44) { px[i * 4] = (byte)(((v >> 8) & 15) * 17); px[i * 4 + 1] = (byte)(((v >> 4) & 15) * 17); px[i * 4 + 2] = (byte)((v & 15) * 17); px[i * 4 + 3] = (byte)((v >> 12) * 17); }
                    else { px[i * 4] = (byte)((v >> 11) * 255 / 31); px[i * 4 + 1] = (byte)(((v >> 5) & 63) * 255 / 63); px[i * 4 + 2] = (byte)((v & 31) * 255 / 31); px[i * 4 + 3] = 255; }
                }
                return px;
            }
            int bs = inf.Fmt == 1 ? 8 : 16, bw = Math.Max(1, (w + 3) / 4), bh = Math.Max(1, (h + 3) / 4);
            var alpha = new byte[16];
            int[] r = new int[4], g = new int[4], bl = new int[4], al = new int[4];
            for (int by = 0; by < bh; by++)
                for (int bx = 0; bx < bw; bx++)
                {
                    int b = o + (by * bw + bx) * bs;
                    if (inf.Fmt == 5)
                    {
                        int a0 = d[b], a1 = d[b + 1];
                        ulong bitsA = 0; for (int k = 0; k < 6; k++) bitsA |= (ulong)d[b + 2 + k] << (8 * k);
                        for (int k = 0; k < 16; k++)
                        {
                            int c = (int)((bitsA >> (3 * k)) & 7);
                            alpha[k] = (byte)(c == 0 ? a0 : c == 1 ? a1 : a0 > a1 ? ((8 - c) * a0 + (c - 1) * a1) / 7 : c == 6 ? 0 : c == 7 ? 255 : ((6 - c) * a0 + (c - 1) * a1) / 5);
                        }
                        b += 8;
                    }
                    int c0 = d[b] | d[b + 1] << 8, c1 = d[b + 2] | d[b + 3] << 8;
                    r[0] = (c0 >> 11) * 255 / 31; g[0] = ((c0 >> 5) & 63) * 255 / 63; bl[0] = (c0 & 31) * 255 / 31;
                    r[1] = (c1 >> 11) * 255 / 31; g[1] = ((c1 >> 5) & 63) * 255 / 63; bl[1] = (c1 & 31) * 255 / 31;
                    al[0] = al[1] = al[2] = al[3] = 255;
                    if (c0 > c1 || inf.Fmt == 5)
                    {
                        r[2] = (2 * r[0] + r[1]) / 3; g[2] = (2 * g[0] + g[1]) / 3; bl[2] = (2 * bl[0] + bl[1]) / 3;
                        r[3] = (r[0] + 2 * r[1]) / 3; g[3] = (g[0] + 2 * g[1]) / 3; bl[3] = (bl[0] + 2 * bl[1]) / 3;
                    }
                    else { r[2] = (r[0] + r[1]) / 2; g[2] = (g[0] + g[1]) / 2; bl[2] = (bl[0] + bl[1]) / 2; r[3] = g[3] = bl[3] = 0; al[3] = 0; }
                    uint idx = BitConverter.ToUInt32(d, b + 4);
                    for (int k = 0; k < 16; k++)
                    {
                        int x = bx * 4 + (k & 3), y = by * 4 + (k >> 2);
                        if (x >= w || y >= h) continue;
                        int c = (int)((idx >> (2 * k)) & 3), q = (y * w + x) * 4;
                        px[q] = (byte)r[c]; px[q + 1] = (byte)g[c]; px[q + 2] = (byte)bl[c]; px[q + 3] = inf.Fmt == 5 ? alpha[k] : (byte)al[c];
                    }
                }
            return px;
        }

        // couleur moyenne (0..1) d'apres un petit niveau de detail ; null si illisible
        public static float[] Average(byte[] d, int off, int size)
        {
            int w, h;
            byte[] px = Decode(d, off, size, 64, out w, out h);
            if (px == null) return null;
            double r = 0, g = 0, b = 0; int n = w * h;
            for (int i = 0; i < n; i++) { r += px[i * 4]; g += px[i * 4 + 1]; b += px[i * 4 + 2]; }
            return new[] { (float)(r / n / 255), (float)(g / n / 255), (float)(b / n / 255) };
        }

        // pixels RGBA -> .vtex DXT1 avec toute la chaine de niveaux (reduction 2x2 ; cotes multiples de 4)
        public static Result EncodeDxt1(byte[] px, int w, int h)
        {
            var r = new Result { Width = w, Height = h, Format = 1, Supported = true };
            if (w % 4 != 0 || h % 4 != 0) return Grey(r);
            var ms = new MemoryStream();
            var bwr = new BinaryWriter(ms);
            bwr.Write(new[] { (byte)'V', (byte)'O', (byte)'Y', (byte)'T' });
            int levels = 1 + (int)Math.Floor(Math.Log(Math.Max(w, h), 2));
            bwr.Write(1); bwr.Write(w); bwr.Write(h); bwr.Write(levels);
            long sizePos = ms.Position; bwr.Write(0);
            int total = 0, lw = w, lh = h;
            byte[] cur = px;
            for (int lv = 0; lv < levels; lv++)
            {
                total += Dxt1Level(bwr, cur, lw, lh);
                if (lv == levels - 1) break;
                int nw = Math.Max(1, lw / 2), nh = Math.Max(1, lh / 2);
                var nx = new byte[nw * nh * 4];
                for (int y = 0; y < nh; y++)
                    for (int x = 0; x < nw; x++)
                    {
                        int x0 = Math.Min(lw - 1, x * 2), x1 = Math.Min(lw - 1, x * 2 + 1), y0 = Math.Min(lh - 1, y * 2), y1 = Math.Min(lh - 1, y * 2 + 1);
                        for (int c = 0; c < 4; c++)
                            nx[(y * nw + x) * 4 + c] = (byte)((cur[(y0 * lw + x0) * 4 + c] + cur[(y0 * lw + x1) * 4 + c] + cur[(y1 * lw + x0) * 4 + c] + cur[(y1 * lw + x1) * 4 + c] + 2) / 4);
                    }
                cur = nx; lw = nw; lh = nh;
            }
            ms.Position = sizePos; bwr.Write(total);
            r.Vtex = ms.ToArray();
            return r;
        }

        // DXT1 simple : extremites = coin min / coin max de la boite des couleurs du bloc, index du plus proche
        static int Dxt1Level(BinaryWriter bw, byte[] px, int w, int h)
        {
            int bwc = Math.Max(1, (w + 3) / 4), bhc = Math.Max(1, (h + 3) / 4);
            var blk = new int[48];
            var pr = new int[4, 3];
            int[] mn = new int[3], mx = new int[3];
            for (int by = 0; by < bhc; by++)
                for (int bx = 0; bx < bwc; bx++)
                {
                    mn[0] = mn[1] = mn[2] = 255; mx[0] = mx[1] = mx[2] = 0;
                    for (int k = 0; k < 16; k++)
                    {
                        int x = Math.Min(w - 1, bx * 4 + (k & 3)), y = Math.Min(h - 1, by * 4 + (k >> 2)), q = (y * w + x) * 4;
                        for (int c = 0; c < 3; c++) { int v = px[q + c]; blk[k * 3 + c] = v; if (v < mn[c]) mn[c] = v; if (v > mx[c]) mx[c] = v; }
                    }
                    int c0 = To565(mx), c1 = To565(mn);
                    if (c0 < c1) { int t = c0; c0 = c1; c1 = t; }
                    for (int c = 0; c < 3; c++)
                    {
                        int a = From565(c0, c), b = From565(c1, c);
                        pr[0, c] = a; pr[1, c] = b; pr[2, c] = (2 * a + b) / 3; pr[3, c] = (a + 2 * b) / 3;
                    }
                    uint idx = 0;
                    if (c0 != c1)
                        for (int k = 0; k < 16; k++)
                        {
                            int best = 0, bd = int.MaxValue;
                            for (int i = 0; i < 4; i++)
                            {
                                int dr = blk[k * 3] - pr[i, 0], dg = blk[k * 3 + 1] - pr[i, 1], db = blk[k * 3 + 2] - pr[i, 2], dd = dr * dr + dg * dg + db * db;
                                if (dd < bd) { bd = dd; best = i; }
                            }
                            idx |= (uint)best << (2 * k);
                        }
                    bw.Write((ushort)c0); bw.Write((ushort)c1); bw.Write(idx);
                }
            return bwc * bhc * 8;
        }

        static int To565(int[] c) { return (c[0] * 31 + 127) / 255 << 11 | (c[1] * 63 + 127) / 255 << 5 | (c[2] * 31 + 127) / 255; }
        static int From565(int v, int c) { return c == 0 ? (v >> 11) * 255 / 31 : c == 1 ? ((v >> 5) & 63) * 255 / 63 : (v & 31) * 255 / 31; }

        static Result Grey(Result r)
        {
            var ms = new MemoryStream();
            var bw = new BinaryWriter(ms);
            bw.Write(new[] { (byte)'V', (byte)'O', (byte)'Y', (byte)'T' });
            bw.Write(32); bw.Write(4); bw.Write(4); bw.Write(1); bw.Write(64);
            for (int i = 0; i < 16; i++) { bw.Write((byte)128); bw.Write((byte)128); bw.Write((byte)128); bw.Write((byte)255); }
            r.Vtex = ms.ToArray(); r.Width = r.Height = 4; r.Format = 32; r.Supported = false;
            return r;
        }
    }
}
