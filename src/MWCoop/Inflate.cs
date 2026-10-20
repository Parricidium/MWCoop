using System;

namespace MWCoop
{
    // Decompression « deflate » (RFC 1951) en C# pur, pour les zip des cartes (MapPacks.cs) : le Mono d'Unity 5.0 n'a pas zlib
    // (DeflateStream : « CreateZStream »). Blocs stockes, Huffman fixe et dynamique.
    public static class Inflate
    {
        static readonly int[] LenBase = { 3, 4, 5, 6, 7, 8, 9, 10, 11, 13, 15, 17, 19, 23, 27, 31, 35, 43, 51, 59, 67, 83, 99, 115, 131, 163, 195, 227, 258 };
        static readonly int[] LenExtra = { 0, 0, 0, 0, 0, 0, 0, 0, 1, 1, 1, 1, 2, 2, 2, 2, 3, 3, 3, 3, 4, 4, 4, 4, 5, 5, 5, 5, 0 };
        static readonly int[] DistBase = { 1, 2, 3, 4, 5, 7, 9, 13, 17, 25, 33, 49, 65, 97, 129, 193, 257, 385, 513, 769, 1025, 1537, 2049, 3073, 4097, 6145, 8193, 12289, 16385, 24577 };
        static readonly int[] DistExtra = { 0, 0, 0, 0, 1, 1, 2, 2, 3, 3, 4, 4, 5, 5, 6, 6, 7, 7, 8, 8, 9, 9, 10, 10, 11, 11, 12, 12, 13, 13 };
        static readonly int[] ClOrder = { 16, 17, 18, 0, 8, 7, 9, 6, 10, 5, 11, 4, 12, 3, 13, 2, 14, 1, 15 };

        class Huff { public int[] Count = new int[16], Symbol; }

        static Huff Build(int[] lengths, int n)
        {
            var h = new Huff { Symbol = new int[n] };
            for (int i = 0; i < n; i++) h.Count[lengths[i]]++;
            h.Count[0] = 0;
            var offs = new int[16];
            for (int i = 1; i < 16; i++) offs[i] = offs[i - 1] + h.Count[i - 1];
            for (int i = 0; i < n; i++) if (lengths[i] != 0) h.Symbol[offs[lengths[i]]++] = i;
            return h;
        }

        public static byte[] Run(byte[] src, int start, int len, int outSize)
        {
            var o = new byte[outSize];
            int op = 0, ip = start, end = start + len, bitBuf = 0, bitCnt = 0;
            Func<int, int> bits = need =>
            {
                while (bitCnt < need)
                {
                    if (ip >= end) throw new Exception("deflate : donnees coupees");
                    bitBuf |= src[ip++] << bitCnt; bitCnt += 8;
                }
                int v = bitBuf & ((1 << need) - 1);
                bitBuf >>= need; bitCnt -= need;
                return v;
            };
            Func<Huff, int> decode = h =>
            {
                int code = 0, first = 0, index = 0;
                for (int l = 1; l < 16; l++)
                {
                    code |= bits(1);
                    int count = h.Count[l];
                    if (code - count < first) return h.Symbol[index + (code - first)];
                    index += count; first += count; first <<= 1; code <<= 1;
                }
                throw new Exception("deflate : code invalide");
            };
            Huff fixLen = null, fixDist = null;
            bool last;
            do
            {
                last = bits(1) == 1;
                int type = bits(2);
                if (type == 0)
                {
                    bitBuf = 0; bitCnt = 0;   // (alignement sur l'octet)
                    int n = src[ip] | src[ip + 1] << 8;
                    ip += 4;
                    Buffer.BlockCopy(src, ip, o, op, n);
                    ip += n; op += n;
                    continue;
                }
                Huff lh, dh;
                if (type == 1)
                {
                    if (fixLen == null)
                    {
                        var l = new int[288];
                        for (int i = 0; i < 288; i++) l[i] = i < 144 ? 8 : i < 256 ? 9 : i < 280 ? 7 : 8;
                        fixLen = Build(l, 288);
                        var d = new int[30];
                        for (int i = 0; i < 30; i++) d[i] = 5;
                        fixDist = Build(d, 30);
                    }
                    lh = fixLen; dh = fixDist;
                }
                else if (type == 2)
                {
                    int nlen = bits(5) + 257, ndist = bits(5) + 1, ncode = bits(4) + 4;
                    var cl = new int[19];
                    for (int i = 0; i < ncode; i++) cl[ClOrder[i]] = bits(3);
                    Huff ch = Build(cl, 19);
                    var lens = new int[nlen + ndist];
                    for (int i = 0; i < nlen + ndist;)
                    {
                        int sym = decode(ch);
                        if (sym < 16) lens[i++] = sym;
                        else
                        {
                            int rep, val = 0;
                            if (sym == 16) { val = lens[i - 1]; rep = 3 + bits(2); }
                            else if (sym == 17) rep = 3 + bits(3);
                            else rep = 11 + bits(7);
                            while (rep-- > 0) lens[i++] = val;
                        }
                    }
                    var ll = new int[nlen]; Array.Copy(lens, ll, nlen);
                    var dl = new int[ndist]; Array.Copy(lens, nlen, dl, 0, ndist);
                    lh = Build(ll, nlen); dh = Build(dl, ndist);
                }
                else throw new Exception("deflate : type de bloc invalide");
                while (true)
                {
                    int sym = decode(lh);
                    if (sym < 256) { o[op++] = (byte)sym; continue; }
                    if (sym == 256) break;
                    sym -= 257;
                    int length = LenBase[sym] + bits(LenExtra[sym]);
                    int ds = decode(dh);
                    int dist = DistBase[ds] + bits(DistExtra[ds]);
                    for (int k = 0; k < length; k++, op++) o[op] = o[op - dist];
                }
            } while (!last);
            return o;
        }
    }
}
