using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace MWCoop
{
    // Films lus au fil de la lecture (JD 07/11 : bobines de plus de 30 min, et seance du cinema en streaming au lieu d'attendre le
    // film entier). Avant, un film etait lu en entier en memoire (images et son en flottants) : 2 h de film = plusieurs Go.
    //  - petit film complet (32 Mo au plus : prises du camescope, courts imports) : en memoire comme avant (son en AudioClip plein) ;
    //  - gros film, ou film en cours de reception (fichier .osa, morceaux arrives dans l'ordre) : index des images lu peu a peu
    //    (en-tetes des blocs, 2 ms par image au plus), chaque image lue sur le disque quand elle s'affiche, son garde en mu-law
    //    (11 Ko par seconde) et joue par un AudioClip en flux (PCMReaderCallback, sous verrou : fil du son) ; ce qui n'est pas encore
    //    arrive se joue en silence et l'image reste sur la derniere : la lecture rattrape d'elle-meme l'instant commun.
    //  - un film pas lu jusqu'au bout (index en cours, reception) n'a pas encore de fin connue : sa duree annoncee (partage du
    //    cinema, @kinojako op 1), sinon 10 h -- la seance ne s'arrete pas avant.
    public static partial class Kauppa
    {
        const long FilmMemMax = 32L << 20;
        const int FilmStreamSamples = 4 * 3600 * RecRate;   // (AudioClip en flux : 4 h, silence au-dela du son)
        static readonly Dictionary<string, uint> filmExpect = new Dictionary<string, uint>();   // film -> duree annoncee (ms)
        static readonly HashSet<string> filmFailed = new HashSet<string>();                     // (reception impossible ici : disque plein...)
        static readonly HashSet<string> filmReadLogged = new HashSet<string>();

        class Film
        {
            public string Id; public byte[] Data; public FileStream Fs; public bool Growing, Done;
            public long ScanPos = 11;
            public int W = 256, H = 192, Fps = 10;
            public readonly List<long> Off = new List<long>(); public readonly List<int> Len = new List<int>(); public readonly List<uint> Ms = new List<uint>();
            readonly List<byte[]> mu = new List<byte[]>(); readonly List<long> muAt = new List<long>();
            long muCount, readPos;
            public uint LengthMs, ExpectMs; public AudioClip Clip; public float LastUse;

            public long MuCount { get { lock (mu) return muCount; } }
            public void AddMu(byte[] b) { lock (mu) { muAt.Add(muCount); mu.Add(b); muCount += b.Length; } }
            int Seg(long k) { int lo = 0, hi = mu.Count - 1, r = -1; while (lo <= hi) { int m = (lo + hi) / 2; if (muAt[m] <= k) { r = m; lo = m + 1; } else hi = m - 1; } return r; }
            // echantillon mu-law k (silence s'il n'est pas la)
            public byte MuAt(long k)
            {
                lock (mu) { int s = Seg(k); return s >= 0 && k - muAt[s] < mu[s].Length ? mu[s][k - muAt[s]] : (byte)0xFF; }
            }
            // AudioClip en flux (fil du son)
            public void PcmRead(float[] data)
            {
                lock (mu)
                {
                    long k = readPos;
                    int s = Seg(k);
                    for (int i = 0; i < data.Length; i++, k++)
                    {
                        while (s >= 0 && s + 1 < mu.Count && k >= muAt[s + 1]) s++;
                        if (s < 0 && mu.Count > 0 && k >= muAt[0]) s = 0;
                        data[i] = s >= 0 && k - muAt[s] < mu[s].Length ? MuLawDecode(mu[s][k - muAt[s]]) : 0f;
                    }
                    readPos = k;
                }
            }
            public void PcmSetPos(int p) { lock (mu) readPos = p; }
            public float[] AllSound() { lock (mu) { var r = new float[muCount]; for (int s = 0; s < mu.Count; s++) for (int i = 0; i < mu[s].Length; i++) r[muAt[s] + i] = MuLawDecode(mu[s][i]); return r; } }
        }

        // octets du fichier en reception deja arrives a la suite (morceaux 0..Prefix-1)
        static long PrefixBytes(string id) { FilmIn fi; return filmIn.TryGetValue(id, out fi) && fi.Fs != null ? Math.Min((long)fi.Prefix * FilmChunk, fi.Fs.Length) : 0L; }

        static bool ReadAt(Film f, long pos, byte[] buf, int n)
        {
            if (pos < 0 || n < 0) return false;
            if (f.Data != null) { if (pos + n > f.Data.Length) return false; Buffer.BlockCopy(f.Data, (int)pos, buf, 0, n); return true; }
            FileStream s = f.Fs;
            if (f.Growing) { FilmIn fi; s = filmIn.TryGetValue(f.Id, out fi) ? fi.Fs : null; if (pos + n > PrefixBytes(f.Id)) return false; }
            if (s == null) return false;
            try
            {
                s.Position = pos;
                int got = 0;
                while (got < n) { int k = s.Read(buf, got, n - got); if (k <= 0) break; got += k; }
                return got == n;
            }
            catch (Exception e)
            {
                // (fichier enleve ou disque debranche pendant la lecture : image et son s'arretent, pas de plantage)
                if (filmReadLogged.Add(f.Id)) L("video : film " + f.Id + " illisible pendant la lecture : " + e.Message);
                return false;
            }
        }

        static byte[] FrameJpeg(Film f, int idx)
        {
            if (idx < 0 || idx >= f.Off.Count) return null;
            var b = new byte[f.Len[idx]];
            return ReadAt(f, f.Off[idx], b, b.Length) ? b : null;
        }

        static readonly byte[] scanHdr = new byte[9];
        // index des blocs a la suite de ScanPos : maxMs millisecondes au plus (0 : jusqu'au bout)
        static void ScanFilm(Film f, float maxMs)
        {
            if (f.Done) return;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            long limit = f.Data != null ? f.Data.Length : f.Growing ? PrefixBytes(f.Id) : f.Fs != null ? f.Fs.Length : 0L;
            while (f.ScanPos + 9 <= limit)
            {
                if (maxMs > 0f && sw.Elapsed.TotalMilliseconds > maxMs) return;
                if (!ReadAt(f, f.ScanPos, scanHdr, 9)) break;
                int kind = scanHdr[0]; uint ms = BitConverter.ToUInt32(scanHdr, 1); int n = BitConverter.ToInt32(scanHdr, 5);
                long p = f.ScanPos + 9;
                if (n < 0 || n > (64 << 20)) { L("video : film " + f.Id + " : bloc illisible a " + f.ScanPos); f.Done = !f.Growing; limit = 0; break; }
                if (p + n > limit) break;   // (bloc pas encore arrive en entier)
                if (kind == 1) { f.Off.Add(p); f.Len.Add(n); f.Ms.Add(ms); }
                else if (kind == 2 && n > 0) { var b = new byte[n]; if (!ReadAt(f, p, b, n)) break; f.AddMu(b); }
                if (ms > f.LengthMs) f.LengthMs = ms;
                f.ScanPos = p + n;
            }
            f.LengthMs = Math.Max(f.LengthMs, (uint)(f.MuCount * 1000L / RecRate));
            if (!f.Growing && f.ScanPos + 9 > limit) f.Done = true;
        }

        // duree d'une prise pour l'enchainement (s) : pas lue jusqu'au bout -> annoncee, sinon 10 h (pas de fin avant)
        static float FilmSeconds(Film f)
        {
            if (f.Done) return f.LengthMs / 1000f;
            return Math.Max(f.LengthMs, f.ExpectMs > 0 ? f.ExpectMs : 36000000u) / 1000f;
        }

        static FileStream OpenFilm(string path) { return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 65536); }

        static bool FilmInUse(Film f)
        {
            if (showing.Exists(s => s.Film == f)) return true;
            foreach (Projector p in projectors) if (p != null && p.Film == f) return true;
            return mClips.Contains(f);
        }

        static void DropFilm(string id)
        {
            Film f;
            if (!filmCache.TryGetValue(id, out f)) return;
            if (f.Clip != null) UnityEngine.Object.Destroy(f.Clip);
            if (f.Fs != null) try { f.Fs.Close(); } catch { }
            filmCache.Remove(id);
        }

        static Film LoadFilm(string id)
        {
            Film f;
            float now = Time.realtimeSinceStartup;
            if (filmCache.TryGetValue(id, out f))
            {
                f.LastUse = now;
                if (f.Growing && !filmIn.ContainsKey(id))
                {
                    // reception finie (fichier renomme) ou abandonnee
                    if (!HaveFilm(id)) { DropFilm(id); return null; }
                    try { f.Fs = OpenFilm(FilmPath(id)); f.Growing = false; }
                    catch (Exception e) { L("video : film " + id + " : " + e.Message); DropFilm(id); return null; }
                }
                if (!f.Done) ScanFilm(f, 2f);
                return f;
            }
            bool growing = !HaveFilm(id) && PrefixBytes(id) >= 11;
            if (!growing && !HaveFilm(id)) return null;
            try
            {
                f = new Film { Id = id, Growing = growing, LastUse = now };
                uint ex; if (filmExpect.TryGetValue(id, out ex)) f.ExpectMs = ex;
                if (!growing)
                {
                    if (new FileInfo(FilmPath(id)).Length <= FilmMemMax) f.Data = File.ReadAllBytes(FilmPath(id));
                    else f.Fs = OpenFilm(FilmPath(id));
                }
                var h = new byte[11];
                if (!ReadAt(f, 0, h, 11) || h[0] != 'M' || h[1] != 'W' || h[2] != 'V' || h[3] != '1')
                {
                    if (f.Fs != null) f.Fs.Close();
                    if (!growing) L("video : film " + id + " illisible");
                    return null;
                }
                f.W = h[4] | h[5] << 8; f.H = h[6] | h[7] << 8; f.Fps = Math.Max(1, (int)h[8]);
                ScanFilm(f, f.Data != null ? 0f : 4f);
                if (f.Data != null)
                {
                    if (f.MuCount > 0)
                    {
                        float[] snd = f.AllSound();
                        f.Clip = AudioClip.Create("kauppa_" + id, snd.Length, 1, RecRate, false);
                        f.Clip.SetData(snd, 0);
                    }
                }
                else f.Clip = AudioClip.Create("kauppa_" + id, FilmStreamSamples, 1, RecRate, true, f.PcmRead, f.PcmSetPos);
                // (garde les films en cours de lecture, et quelques autres)
                while (filmCache.Count >= 6)
                {
                    Film old = null;
                    foreach (Film x in filmCache.Values) if (!FilmInUse(x) && (old == null || x.LastUse < old.LastUse)) old = x;
                    if (old == null) break;
                    DropFilm(old.Id);
                }
                filmCache[id] = f;
                L("video : film " + id + (f.Data != null ? " charge (" : f.Growing ? " lu pendant sa reception (" : " lu sur le disque (") + f.Ms.Count + " images, " + (f.LengthMs / 1000f).ToString("F1") + " s" + (f.Done ? "" : " pour l'instant") + ")");
                return f;
            }
            catch (Exception e) { L("video : film " + id + " : " + e.Message); if (f != null && f.Fs != null) try { f.Fs.Close(); } catch { } return null; }
        }

        // Film complet lu jusqu'au bout (montage, duree annoncee au partage) ; null s'il manque
        static Film LoadFilmAll(string id)
        {
            if (!HaveFilm(id)) return null;
            Film f = LoadFilm(id);
            if (f != null && !f.Done) ScanFilm(f, 0f);
            return f;
        }

        // image de l'instant ms d'une prise (derniere image arrivee si on est en avance sur la reception) ; -1 avant la premiere
        static int FrameAt(Film f, uint ms)
        {
            int lo = 0, hi = f.Ms.Count - 1, idx = -1;
            while (lo <= hi) { int mid = (lo + hi) / 2; if (f.Ms[mid] <= ms) { idx = mid; lo = mid + 1; } else hi = mid - 1; }
            return idx;
        }

        // ici, la lecture attend la suite du film (seance plus avancee que la reception)
        static bool FilmBehind(Film f, float tc) { return f != null && !f.Done && tc * 1000f > f.LengthMs + 500f; }
    }
}
