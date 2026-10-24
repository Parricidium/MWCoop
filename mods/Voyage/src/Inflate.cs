namespace Voyage
{
    // Decompression gzip en code gere (d'apres puff.c de zlib, Mark Adler) : le Mono du jeu n'a pas la zlib native
    // (GZipStream -> EntryPointNotFoundException: CreateZStream). Pour les modeles compresses (gzip).
    sealed class Inflate
    {
        public static byte[] Gzip(byte[] d)
        {
            int p = 10, flg = d[3];
            if ((flg & 4) != 0) p += 2 + (d[p] | d[p + 1] << 8);
            if ((flg & 8) != 0) while (d[p++] != 0) { }
            if ((flg & 16) != 0) while (d[p++] != 0) { }
            if ((flg & 2) != 0) p += 2;
            int size = d[d.Length - 4] | d[d.Length - 3] << 8 | d[d.Length - 2] << 16 | d[d.Length - 1] << 24;
            var z = new Inflate { inb = d, inpos = p, outb = new byte[size] };
            z.Run();
            return z.outb;
        }

        byte[] inb, outb;
        int inpos, outpos, bitbuf, bitcnt;

        int Bits(int need)
        {
            int val = bitbuf;
            while (bitcnt < need) { val |= inb[inpos++] << bitcnt; bitcnt += 8; }
            bitbuf = val >> need; bitcnt -= need;
            return val & ((1 << need) - 1);
        }

        sealed class Huff { public short[] Count = new short[16]; public short[] Symbol; }

        static int Construct(Huff h, short[] length, int off, int n)
        {
            for (int len = 0; len < 16; len++) h.Count[len] = 0;
            for (int s = 0; s < n; s++) h.Count[length[off + s]]++;
            if (h.Count[0] == n) return 0;
            int left = 1;
            for (int len = 1; len < 16; len++) { left <<= 1; left -= h.Count[len]; if (left < 0) return left; }
            var offs = new short[16];
            for (int len = 1; len < 15; len++) offs[len + 1] = (short)(offs[len] + h.Count[len]);
            h.Symbol = new short[n];
            for (int s = 0; s < n; s++) if (length[off + s] != 0) h.Symbol[offs[length[off + s]]++] = (short)s;
            return left;
        }

        int Decode(Huff h)
        {
            int code = 0, first = 0, index = 0;
            for (int len = 1; len < 16; len++)
            {
                code |= Bits(1);
                int count = h.Count[len];
                if (code - count < first) return h.Symbol[index + (code - first)];
                index += count; first += count; first <<= 1; code <<= 1;
            }
            throw new System.Exception("inflate : code invalide");
        }

        static readonly short[] LBase = { 3, 4, 5, 6, 7, 8, 9, 10, 11, 13, 15, 17, 19, 23, 27, 31, 35, 43, 51, 59, 67, 83, 99, 115, 131, 163, 195, 227, 258 };
        static readonly short[] LExt = { 0, 0, 0, 0, 0, 0, 0, 0, 1, 1, 1, 1, 2, 2, 2, 2, 3, 3, 3, 3, 4, 4, 4, 4, 5, 5, 5, 5, 0 };
        static readonly short[] DBase = { 1, 2, 3, 4, 5, 7, 9, 13, 17, 25, 33, 49, 65, 97, 129, 193, 257, 385, 513, 769, 1025, 1537, 2049, 3073, 4097, 6145, 8193, 12289, 16385, 24577 };
        static readonly short[] DExt = { 0, 0, 0, 0, 1, 1, 2, 2, 3, 3, 4, 4, 5, 5, 6, 6, 7, 7, 8, 8, 9, 9, 10, 10, 11, 11, 12, 12, 13, 13 };
        static readonly short[] Order = { 16, 17, 18, 0, 8, 7, 9, 6, 10, 5, 11, 4, 12, 3, 13, 2, 14, 1, 15 };

        void Codes(Huff lencode, Huff distcode)
        {
            for (;;)
            {
                int sym = Decode(lencode);
                if (sym < 256) { outb[outpos++] = (byte)sym; continue; }
                if (sym == 256) return;
                sym -= 257;
                int len = LBase[sym] + Bits(LExt[sym]);
                int ds = Decode(distcode);
                int dist = DBase[ds] + Bits(DExt[ds]);
                int from = outpos - dist;
                for (int k = 0; k < len; k++) outb[outpos++] = outb[from + k];
            }
        }

        static Huff fixLen, fixDist;

        void Fixed()
        {
            if (fixLen == null)
            {
                var lengths = new short[288];
                int s = 0;
                for (; s < 144; s++) lengths[s] = 8;
                for (; s < 256; s++) lengths[s] = 9;
                for (; s < 280; s++) lengths[s] = 7;
                for (; s < 288; s++) lengths[s] = 8;
                fixLen = new Huff(); Construct(fixLen, lengths, 0, 288);
                var dl = new short[30];
                for (s = 0; s < 30; s++) dl[s] = 5;
                fixDist = new Huff(); Construct(fixDist, dl, 0, 30);
            }
            Codes(fixLen, fixDist);
        }

        void Dynamic()
        {
            int nlen = Bits(5) + 257, ndist = Bits(5) + 1, ncode = Bits(4) + 4;
            var lengths = new short[320];
            int index;
            for (index = 0; index < ncode; index++) lengths[Order[index]] = (short)Bits(3);
            for (; index < 19; index++) lengths[Order[index]] = 0;
            var lencode = new Huff();
            if (Construct(lencode, lengths, 0, 19) != 0) throw new System.Exception("inflate : codes incomplets");
            index = 0;
            while (index < nlen + ndist)
            {
                int sym = Decode(lencode);
                if (sym < 16) { lengths[index++] = (short)sym; continue; }
                short len = 0; int rep;
                if (sym == 16) { len = lengths[index - 1]; rep = 3 + Bits(2); }
                else if (sym == 17) rep = 3 + Bits(3);
                else rep = 11 + Bits(7);
                while (rep-- > 0) lengths[index++] = len;
            }
            lencode = new Huff(); Construct(lencode, lengths, 0, nlen);
            var distcode = new Huff(); Construct(distcode, lengths, nlen, ndist);
            Codes(lencode, distcode);
        }

        void Run()
        {
            int last;
            do
            {
                last = Bits(1);
                int type = Bits(2);
                if (type == 0)
                {
                    bitbuf = 0; bitcnt = 0;
                    int len = inb[inpos] | inb[inpos + 1] << 8;
                    inpos += 4;
                    System.Buffer.BlockCopy(inb, inpos, outb, outpos, len);
                    inpos += len; outpos += len;
                }
                else if (type == 1) Fixed();
                else if (type == 2) Dynamic();
                else throw new System.Exception("inflate : bloc invalide");
            } while (last == 0);
        }
    }
}
