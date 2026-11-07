using System;
using System.Collections.Generic;
using System.IO;
using MWCoop.Net;
using UnityEngine;

namespace MWCoop
{
    // Films du camescope (etape 4 du coin video, 12/10) : partage, lecture, titre.
    //  - partage : a la fin d'une prise, celui qui a filme envoie le fichier a tous (@filmi, morceaux fiables de 1000
    //    octets, [Boutique] FilmiVauhti morceaux par image, 3 par defaut ; l'hote relaie). Chacun le range dans
    //    <profil>\videot. Il manque un film a la lecture : demande (@filmi?), l'hote (ou celui qui l'a filme) le renvoie.
    //  - une cassette porte une ou plusieurs prises : chaque enregistrement s'ajoute a la suite (comme une vraie VHS
    //    qu'on reprend apres un arret), au plus MaxClips ; la lecture les enchaine.
    //  - lecture : chaque machine joue le film elle-meme, depuis l'instant ou SON magnetoscope est passe en lecture (a la
    //    latence pres, tous ensemble) ; film arrive en retard : il part au bon endroit. Images JPEG sur l'ecran, son
    //    sur un AudioSource 3D a la TV ; apres la fin de la bande : neige et souffle.
    //  - titre : cassette en main, tchat « /titre Nom du film » (ou /title) ; ecrit sur la tranche (TextMesh, Arial).
    public static partial class Kauppa
    {
        static readonly Dictionary<string, Film> filmCache = new Dictionary<string, Film>();
        static readonly Dictionary<string, KeyValuePair<float, string>> playStart = new Dictionary<string, KeyValuePair<float, string>>();   // magnetoscope -> debut de la lecture ici, cassette

        static bool HaveFilm(string film) { return film.Length > 0 && File.Exists(FilmPath(film)); }

        // (LoadFilm, Film : KauppaFilmiVirta.cs -- lus au fil de la lecture, JD 07/11)

        // Longueur d'une cassette (s), ses prises toutes chargees ; -1 sinon
        static float TapeLength(string tape)
        {
            float s = 0f;
            List<string> clips = Clips(tape);
            if (clips.Count == 0) return -1f;
            foreach (string c in clips) { Film f; if (!filmCache.TryGetValue(c, out f) || f == null || !f.Done) return -1f; s += f.LengthMs / 1000f; }
            return s;
        }

        // Prises d'une cassette, dans l'ordre (chaque enregistrement s'ajoute a la suite : "f1+f2+f3~titre").
        public static List<string> Clips(string content)
        {
            var l = new List<string>();
            string films = TapeFilm(content);
            if (films.StartsWith("url:")) return l;   // (bobine « lien » : pas de film a soi, lue en continu -- KinoLinkki)
            foreach (string c in films.Split('+')) if (c.Length > 0) l.Add(c);
            return l;
        }

        // ---------------------------------------------------------------- lecture sur un ecran (appele par TickScreens)
        static void ShowFilm(Showing s, float now)
        {
            Esine v = s.Vcr;
            string tape = Tape(v);
            // (position de la bande : lecture, pause, avance, retour -- KauppaNauhuri ; JD 07/11)
            float t = VcrPos(v, now);
            bool sound = VcrMode(v) == VPlay;
            // les prises bout a bout ; il en manque une : neige en attendant (puis reprise au bon endroit)
            List<string> clips = Clips(tape);
            Film f = null; float clipStart = 0f, at = 0f;
            bool all = clips.Count > 0;
            var loaded = new List<Film>();
            foreach (string c in clips) { Film x = LoadFilm(c); if (x == null) { AskFilm(c, now); all = false; } else loaded.Add(x); }
            if (all)
                foreach (Film x in loaded)
                {
                    float len = FilmSeconds(x);
                    if (t < at + len + (x == loaded[loaded.Count - 1] ? 0.3f : 0f)) { f = x; clipStart = at; break; }
                    at += len;
                }
            Renderer r = s.Screen != null ? s.Screen.GetComponent<Renderer>() : null;
            if (r == null) return;
            if (f == null)
            {
                // neige (cassette vierge, film absent ou fini)
                if (s.Film != null) { s.Film = null; s.Frame = -1; }
                if (r.sharedMaterial != noiseMat) r.sharedMaterial = noiseMat;
                if (s.Audio != null && s.Audio.clip != hissClip) { s.Audio.clip = hissClip; s.Audio.loop = true; s.Audio.volume = 0.3f; s.Audio.Play(); }
                return;
            }
            float tc = t - clipStart;
            if (s.Film != f)
            {
                s.Film = f; s.Frame = -1;
                if (s.Tex == null) { s.Tex = new Texture2D(2, 2, TextureFormat.RGB24, false) { name = "kauppa_elokuva", wrapMode = TextureWrapMode.Clamp }; }
                if (s.Mat == null) { Shader us = Shader.Find("Unlit/Texture"); s.Mat = new Material(us != null ? us : noiseMat.shader) { mainTexture = s.Tex, name = "kauppa_elokuva" }; }
                if (s.Audio != null)
                {
                    s.Audio.Stop();
                    s.Audio.clip = f.Clip; s.Audio.loop = false; s.Audio.volume = 0.95f;
                    if (sound && f.Clip != null && tc < f.Clip.length - 0.05f) { s.Audio.time = Mathf.Max(0f, tc); s.Audio.Play(); }
                }
                L("video : " + v.Id + " lit la prise " + f.Id + " (" + (clips.IndexOf(f.Id) + 1) + "/" + clips.Count + ") depuis " + tc.ToString("F1") + " s");
            }
            if (r.sharedMaterial != s.Mat) r.sharedMaterial = s.Mat;
            // son : seulement en lecture, cale sur la bande (pause, avance, retour : muet)
            if (s.Audio != null && f.Clip != null && s.Audio.clip == f.Clip)
            {
                bool want = sound && tc < f.Clip.length - 0.05f;
                if (!want) { if (s.Audio.isPlaying) s.Audio.Pause(); }
                else if (!s.Audio.isPlaying || Mathf.Abs(s.Audio.time - tc) > 0.3f) { s.Audio.time = Mathf.Max(0f, tc); s.Audio.Play(); }
            }
            // image de l'instant (lue sur le disque pour un gros film)
            int idx = FrameAt(f, (uint)Mathf.Max(0f, tc * 1000f));
            if (idx >= 0 && idx != s.Frame)
            {
                byte[] jpg = FrameJpeg(f, idx);
                if (jpg != null) { s.Frame = idx; s.Tex.LoadImage(jpg); }
            }
        }

        static void TickPlayStarts()
        {
            // debut de lecture oublie quand le magnetoscope s'arrete
            var gone = new List<string>();
            foreach (string id in playStart.Keys) { Esine v = FindItem(id); if (v == null || !Playing(v)) gone.Add(id); }
            foreach (string id in gone) playStart.Remove(id);
        }

        // ---------------------------------------------------------------- partage des films
        // (1.11.1, JD 23/10 : un film de bobine de 302 Mo n'arrivait jamais chez l'invite -- plus de 200 000 morceaux, refuses
        // par la garde de OnFilmPart -- et partait a 300 Ko/s.) Envoi lu dans le fichier au fil de l'eau (pas tout en memoire),
        // autant de morceaux par image que le canal fiable en accepte (Peer.InFlight sous FilmiFile), au plus [Boutique] FilmiKoS
        // Ko/s (1500) ; reception ecrite directement dans le fichier a sa place (morceaux de FilmChunk octets), avancement
        // connu (FilmProgress : KinoJako l'affiche et le dit au partageur).
        class FilmOut { public string Film; public FileStream Fs; public long Length; public int Next, Count; public bool Again; public List<int> To; public List<int> RStart, RCount; public int RPos, ROff; }   // (To : destinataires, null = tous ; RStart/RCount : reprise, seulement ces plages)
        class FilmIn { public FileStream Fs; public System.Collections.BitArray Have; public int Got, Count, Prefix; public float Last, NextAsk; public int From = -1; }   // (From : qui l'envoie -- reprise)   // (Have : 1 bit par morceau -- films de plusieurs Go)   // (Prefix : morceaux 0..Prefix-1 tous arrives -- lecture pendant la reception)
        static readonly List<FilmOut> filmOut = new List<FilmOut>();
        static readonly Dictionary<string, FilmIn> filmIn = new Dictionary<string, FilmIn>();
        static readonly Dictionary<string, float> filmAsked = new Dictionary<string, float>();
        const int FilmChunk = 1000;   // (plus de taille maximale depuis le 07/11, JD « ont peux enlever la limite » -- 10 Go puis 4 Go avant : la place libre ici decide, FilmRoom)
        static float filmSendBudget, filmSendT;
        static byte[] filmBuf;

        // to : un joueur (film du cinema accepte, film demande) ; -1 : tous (prise du camescope, cassette)
        static void SendFilm(string film, int to = -1)
        {
            if (!Session.Active || Session.RemoteCount <= 0 || !HaveFilm(film)) return;
            FilmOut o = filmOut.Find(x => x.Film == film && x.RStart == null);
            if (o != null)
            {
                if (to < 0) o.To = null; else if (o.To != null && !o.To.Contains(to)) o.To.Add(to);
                if (o.Next > 0) o.Again = true;
                return;
            }
            try
            {
                var fs = new FileStream(FilmPath(film), FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                if ((fs.Length + FilmChunk - 1) / FilmChunk > int.MaxValue) { fs.Close(); L("video : film " + film + " trop gros pour l'envoi (" + fs.Length / 1048576 + " Mo)"); return; }   // (plus de 2 To : numeros de morceaux sur 32 bits)
                filmOut.Add(new FilmOut { Film = film, Fs = fs, Length = fs.Length, Count = (int)((fs.Length + FilmChunk - 1) / FilmChunk), To = to < 0 ? null : new List<int> { to } });
                L("video : envoi du film " + film + " (" + fs.Length / 1024 + " Ko" + (to < 0 ? ", a tous)" : ", a #" + to + ")"));
            }
            catch (Exception e) { L("video : envoi de " + film + " : " + e.Message); }
        }

        static void AskFilm(string film, float now)
        {
            if (!Session.Active || Session.RemoteCount <= 0 || KinoFilmRefused(film) || filmFailed.Contains(film)) return;
            float last;
            FilmIn fi;
            if (filmIn.TryGetValue(film, out fi) && now - fi.Last < 20f) return;   // (arrive)
            if (filmAsked.TryGetValue(film, out last) && now - last < 25f) return;
            filmAsked[film] = now;
            Session.SendAll(new NetWriter(Msg.Job).U8(Session.LocalId).Str("@filmi?").Str(film), true);
            L("video : film " + film + " demande");
        }

        // avancement de la reception ici : 0..1, -1 si rien n'arrive
        public static float FilmProgress(string film)
        {
            FilmIn fi;
            if (HaveFilm(film)) return 1f;
            return filmIn.TryGetValue(film, out fi) && fi.Count > 0 ? fi.Got / (float)fi.Count : -1f;
        }

        // le plus charge des canaux fiables (morceaux envoyes pas encore acquittes, ou en attente)
        static int MaxInFlight()
        {
            int m = 0;
            if (Session.T != null) foreach (Peer pr in Session.T.Peers) if (pr != null && pr.InFlight > m) m = pr.InFlight;
            return m;
        }

        static void TickFilms(float now)
        {
            TickPlayStarts();
            TickImport(now);
            TickFilmResume(now);
            if (filmOut.Count == 0) return;
            if (!Session.Active) { foreach (FilmOut x in filmOut) if (x.Fs != null) x.Fs.Close(); filmOut.Clear(); return; }
            // debit : FilmiKoS Ko/s au plus, et rien de plus tant que le canal fiable est plein
            float dt = filmSendT > 0f ? Mathf.Min(0.25f, now - filmSendT) : 0f;
            filmSendT = now;
            int kos = Config.GetInt("Test", "FilmiKoS", Config.GetInt("Boutique", "FilmiKoS", 1500));   // ([Test] FilmiKoS : reception lente simulee)
            filmSendBudget = Mathf.Min(filmSendBudget + dt * kos * 1024f / FilmChunk, 400f);
            int room = Config.GetInt("Boutique", "FilmiFile", 160) - MaxInFlight();
            FilmOut o = filmOut[0];
            if (o.To != null)
            {
                // (destinataires partis, ou qui ne veulent plus du film : plus rien a envoyer s'il n'en reste aucun)
                o.To.RemoveAll(id => !Session.Players.ContainsKey(id) || id == Session.LocalId);
                if (o.To.Count == 0) { if (o.Fs != null) o.Fs.Close(); filmOut.RemoveAt(0); L("video : envoi de " + o.Film + " arrete (plus personne ne l'attend)"); return; }
            }
            if (filmBuf == null) filmBuf = new byte[FilmChunk];
            try
            {
                for (; room > 0 && filmSendBudget >= 1f && FilmOutMore(o); room--, filmSendBudget -= 1f)
                {
                    int idx = FilmOutTake(o);
                    long off = (long)idx * FilmChunk;
                    int n = (int)Math.Min(FilmChunk, o.Length - off);
                    o.Fs.Position = off;
                    int got = 0;
                    while (got < n) { int k = o.Fs.Read(filmBuf, got, n - got); if (k <= 0) break; got += k; }
                    if (o.To == null) Session.SendAll(new NetWriter(Msg.Job).U8(Session.LocalId).Str("@filmi").Str(o.Film).I32(idx).I32(o.Count).Bytes(filmBuf, 0, got), true);
                    else
                    {
                        var w = new NetWriter(Msg.Job).U8(Session.LocalId).Str("@filmia").U8(o.To.Count);
                        foreach (int id in o.To) w.U8(id);
                        Session.SendTo(o.To, w.Str(o.Film).I32(idx).I32(o.Count).Bytes(filmBuf, 0, got), true);
                    }
                }
            }
            catch (Exception e) { L("video : envoi de " + o.Film + " interrompu : " + e.Message); o.Next = o.Count; o.Again = false; if (o.RStart != null) o.RPos = o.RStart.Count; }
            if (!FilmOutMore(o))
            {
                if (o.Again) { o.Again = false; o.Next = 0; }
                else { if (o.Fs != null) o.Fs.Close(); filmOut.RemoveAt(0); L("video : film " + o.Film + (o.RStart != null ? " : morceaux manquants renvoyes" : " envoye (" + o.Count + " morceaux)")); }
            }
        }

        // morceau suivant a envoyer : a la suite, ou dans les plages d'une reprise
        static bool FilmOutMore(FilmOut o) { return o.RStart != null ? o.RPos < o.RStart.Count : o.Next < o.Count; }
        static int FilmOutTake(FilmOut o)
        {
            if (o.RStart == null) return o.Next++;
            int i = o.RStart[o.RPos] + o.ROff;
            if (++o.ROff >= o.RCount[o.RPos]) { o.RPos++; o.ROff = 0; }
            return i;
        }

        // Reprise (07/11) : des morceaux d'un film en reception manquent (perdus en route : un film de 4,75 Go arrivait a 99 % et
        // y restait -- 65 364 morceaux jamais livres par le canal fiable ; envoi coupe) et rien n'arrive depuis 6 s : ces plages
        // seulement sont redemandees a celui qui l'envoyait (@filmir, par l'hote s'il faut), toutes les 8 s tant qu'il en manque.
        static void TickFilmResume(float now)
        {
            if (!Session.Active || filmIn.Count == 0) return;
            foreach (KeyValuePair<string, FilmIn> kv in filmIn)
            {
                FilmIn fi = kv.Value;
                if (fi.Got >= fi.Count || fi.From < 0 || fi.From == Session.LocalId || now - fi.Last < 6f || now < fi.NextAsk) continue;
                fi.NextAsk = now + 8f;
                if (!Session.Players.ContainsKey(fi.From)) continue;   // (parti : AskFilm redemande le film a l'hote)
                var st = new List<int>(); var ct = new List<int>(); int missing = 0;
                for (int i = fi.Prefix; i < fi.Count; i++)
                {
                    if (fi.Have[i]) continue;
                    missing++;
                    if (st.Count > 0 && st[st.Count - 1] + ct[ct.Count - 1] == i) ct[ct.Count - 1]++;
                    else if (st.Count < 100) { st.Add(i); ct.Add(1); }
                }
                if (st.Count == 0) continue;
                var w = new NetWriter(Msg.Job).U8(Session.LocalId).Str("@filmir").U8(fi.From).Str(kv.Key).I32(fi.Count).U8(st.Count);
                for (int k = 0; k < st.Count; k++) w.I32(st[k]).I32(ct[k]);
                Session.SendTo(new List<int> { fi.From }, w, true);
                L("video : film " + kv.Key + " : " + missing + " morceau(x) manquant(s), " + st.Count + " plage(s) redemandee(s) a #" + fi.From);
            }
        }

        // [Test] FilmPerte=p : la premiere fois, un morceau sur p (i % p == 3) et les morceaux 5000..5999 sont jetes a l'arrivee
        static readonly HashSet<string> filmLostTest = new HashSet<string>();
        static bool FilmLostTest(string film, int i)
        {
            int p = Config.GetInt("Test", "FilmPerte", 0);
            if (p <= 0 || !(i % p == 3 || (i >= 5000 && i < 6000))) return false;
            return filmLostTest.Add(film + "#" + i);
        }

        // plages manquantes chez who : renvoyees a lui seul (l'hote relaie si le film vient d'un autre invite)
        public static void OnFilmResume(int who, NetReader r)
        {
            int to = r.U8();
            if (to != Session.LocalId)
            {
                if (Session.IsHost && to != who && Session.Players.ContainsKey(to)) Session.SendTo(new List<int> { to }, new NetWriter(Msg.Job).U8(who).Str("@filmir").U8(to).Raw(r.Rest()), true);
                return;
            }
            string film = Session.Clean(r.Str(), 64); int count = r.I32(), nr = r.U8();
            if (film.Length == 0 || film.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || count <= 0 || nr <= 0 || !HaveFilm(film) || KinoRefusedBy(film, who)) return;
            var st = new List<int>(); var ct = new List<int>(); long total = 0;
            for (int k = 0; k < nr && r.More; k++)
            {
                int a = r.I32(), c = r.I32();
                if (a < 0 || c <= 0 || a >= count || c > count - a) return;
                st.Add(a); ct.Add(c); total += c;
            }
            if (st.Count == 0) return;
            FilmOut o = filmOut.Find(x => x.Film == film && x.RStart != null && x.To != null && x.To.Count == 1 && x.To[0] == who);
            if (o == null)
            {
                FileStream fs;
                try { fs = new FileStream(FilmPath(film), FileMode.Open, FileAccess.Read, FileShare.ReadWrite); }
                catch (Exception e) { L("video : reprise de " + film + " : " + e.Message); return; }
                long parts = (fs.Length + FilmChunk - 1) / FilmChunk;
                if (parts != count) { fs.Close(); return; }   // (pas le meme fichier)
                o = new FilmOut { Film = film, Fs = fs, Length = fs.Length, Count = count, To = new List<int> { who } };
                filmOut.Add(o);
            }
            o.RStart = st; o.RCount = ct; o.RPos = 0; o.ROff = 0;
            L("video : film " + film + " : " + total + " morceau(x) manquant(s) chez #" + who + ", renvoye(s) (" + st.Count + " plage(s))");
        }

        // morceau adresse (@filmia) : l'hote le relaie aux destinataires de la liste, chacun le prend s'il y est
        public static void OnFilmPartTo(int who, NetReader r)
        {
            int n = r.U8();
            if (n <= 0 || n > 16) return;
            var ids = new List<int>();
            for (int i = 0; i < n; i++) ids.Add(r.U8());
            if (Session.IsHost)
            {
                List<int> relay = ids.FindAll(id => id != Session.LocalId && id != who);
                if (relay.Count > 0)
                {
                    var w = new NetWriter(Msg.Job).U8(who).Str("@filmia").U8(ids.Count);
                    foreach (int id in ids) w.U8(id);
                    Session.SendTo(relay, w.Raw(r.Rest()), true);
                }
            }
            if (ids.Contains(Session.LocalId)) OnFilmPart(who, r);
        }

        // stop : ce joueur ne veut plus du film (refus, disque plein)
        static void FilmStopFor(string film, int who)
        {
            foreach (FilmOut o in filmOut) if (o.Film == film && o.To != null) o.To.Remove(who);
        }

        public static void OnFilmPart(int who, NetReader r)
        {
            string film = Session.Clean(r.Str(), 64); int i = r.I32(), n = r.I32(); byte[] b = r.Bytes();
            if (film.Length == 0 || film.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || n <= 0 || i < 0 || i >= n || b.Length > FilmChunk) return;
            if (HaveFilm(film) || KinoFilmRefused(film) || filmFailed.Contains(film)) return;
            FilmIn fi;
            try
            {
                if (!filmIn.TryGetValue(film, out fi) || fi.Count != n)
                {
                    if (fi != null && fi.Fs != null) fi.Fs.Close();
                    Directory.CreateDirectory(FilmDir);
                    string roomMsg = FilmRoom((long)n * FilmChunk);   // (pas assez de place : dit au joueur, plus redemande -- catch plus bas)
                    if (roomMsg != null) throw new IOException(roomMsg);
                    fi = new FilmIn { Fs = new FileStream(FilmPath(film) + ".osa", FileMode.Create, FileAccess.ReadWrite, FileShare.Read), Have = new System.Collections.BitArray(n), Count = n };
                    filmIn[film] = fi;
                }
                fi.Last = Time.realtimeSinceStartup; fi.From = who;
                if (fi.Have[i]) return;
                if (Config.Testing && i > 2000 && Config.GetInt("Test", "FilmDisquePlein", 0) != 0) throw new IOException("essai : disque plein");   // (essai du message)
                if (Config.Testing && FilmLostTest(film, i)) return;   // ([Test] FilmPerte : morceaux perdus en route, essai de la reprise)
                fi.Fs.Position = (long)i * FilmChunk;
                fi.Fs.Write(b, 0, b.Length);
                fi.Have[i] = true; fi.Got++;
                while (fi.Prefix < fi.Count && fi.Have[fi.Prefix]) fi.Prefix++;
                if (fi.Got < n) return;
                filmIn.Remove(film);
                fi.Fs.Close();
                File.Move(FilmPath(film) + ".osa", FilmPath(film));
                NoteRecu(film);   // (efface au prochain chargement s'il ne sert a rien de la sauvegarde -- CleanRecus)
                L("video : film " + film + " recu de #" + who + " (" + n + " morceaux)");
                KinoFilmArrived(film);
            }
            catch (Exception e)
            {
                L("video : film " + film + " pas ecrit : " + e.Message);
                if (filmIn.TryGetValue(film, out fi)) { if (fi.Fs != null) fi.Fs.Close(); filmIn.Remove(film); }
                // (morceaux deja recus : inutilisables, le film n'est plus redemande -- fichier .osa du mod lui-meme, rendu au disque)
                try { if (File.Exists(FilmPath(film) + ".osa")) File.Delete(FilmPath(film) + ".osa"); } catch { }
                // (disque plein, dossier refuse... : dit une fois au joueur, et plus redemande de la partie -- sinon il revenait sans fin)
                if (filmFailed.Add(film)) { Chat.System(Lang.T("Film pas enregistré (disque plein ?) : ", "Film not saved (disk full?): ") + e.Message); KinoFilmFailed(film); }
            }
        }

        public static void OnFilmAsk(int who, NetReader r)
        {
            string film = Session.Clean(r.Str(), 64);
            if (film.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || !HaveFilm(film)) return;
            // l'hote repond ; un invite seulement pour un film qu'il a filme lui-meme pendant cette partie
            if (KinoRefusedBy(film, who)) { L("video : film " + film + " redemande par #" + who + ", qui l'a refuse : pas envoye"); return; }
            if (Session.IsHost || film.StartsWith("f" + Session.LocalId + "-")) SendFilm(film, who);   // (a lui seul : les autres l'ont deja)
        }

        // ---------------------------------------------------------------- titre sur la tranche
        static Font arial;
        static Material labelMat;   // (le materiau d'Arial, GUI/Text Shader, se dessine a travers tout : transparent ordinaire)

        static void TickLabels()
        {
            foreach (Esine e in esineet)
            {
                if (e.Kind != KVhs || e.Go == null) continue;
                string title = TapeTitle(e.Data);
                TitleMesh(e.Go, "nimi", title, new Vector3(0.012f, 0f, -0.103f / 2f - 0.0014f), Quaternion.identity, 0.0101f, 1f);
                // (etiquette du dessus du modele : le titre sur sa premiere ligne -- JD 07/11 ; 6,3 cm de large au plus)
                if (e.Go.transform.Find("etiquette") != null)
                    TitleMesh(e.Go, "nimi_dessus", title, VhsTop + new Vector3(0f, 0.0002f, VhsTopSize.y * (0.5f - 0.36f)), Quaternion.LookRotation(Vector3.down, Vector3.forward), 0.0085f, 0.063f);
            }
        }

        // titre ecrit (police Arial du jeu) : em = hauteur des lettres (m), plus petit si le titre depasse width (m ; 1 : sans limite)
        static void TitleMesh(GameObject go, string name, string title, Vector3 pos, Quaternion rot, float em, float width)
        {
            Transform t = go.transform.Find(name);
            TextMesh tm = t != null ? t.GetComponent<TextMesh>() : null;
            if (tm == null)
            {
                if (title.Length == 0) return;
                if (arial == null) arial = Resources.GetBuiltinResource<Font>("Arial.ttf");
                if (arial == null) return;
                var g = new GameObject(name);
                g.transform.SetParent(go.transform, false);
                g.transform.localPosition = pos;
                g.transform.localRotation = rot;
                tm = g.AddComponent<TextMesh>();
                tm.font = arial; tm.fontSize = 48; tm.characterSize = em * 10f / 48f; tm.anchor = TextAnchor.MiddleCenter;
                tm.alignment = TextAlignment.Center; tm.color = new Color(0.08f, 0.1f, 0.32f);
                if (labelMat == null)
                {
                    Shader sh = Shader.Find("Legacy Shaders/Transparent/Diffuse") ?? Shader.Find("Transparent/Diffuse") ?? arial.material.shader;
                    labelMat = new Material(sh) { name = "kauppa_tranche", color = new Color(0.06f, 0.07f, 0.2f, 1f) };
                }
                g.GetComponent<MeshRenderer>().sharedMaterial = labelMat;
            }
            if (labelMat != null && labelMat.mainTexture != arial.material.mainTexture) labelMat.mainTexture = arial.material.mainTexture;   // (atlas refait par la police)
            if (tm.text != title)
            {
                tm.text = title;
                float e2 = width < 1f ? Mathf.Min(em, width / Mathf.Max(1f, title.Length * 0.52f)) : em;   // (Arial : ~0,52 em par lettre)
                tm.characterSize = e2 * 10f / 48f;
            }
        }

        // tchat : « /titre Nom » (ou /title) avec une cassette en main
        public static bool ChatCommand(string text)
        {
            string low = text.ToLowerInvariant();
            if (Active && (root != null || kino != null) && ImportCommand(text)) return true;
            if (Active && kino != null && LinkCommand(text)) return true;
            if (!(low.StartsWith("/titre") || low.StartsWith("/title"))) return false;
            if (!Active || (root == null && kino == null)) return false;
            string title = text.Length > 6 ? text.Substring(6).Trim() : "";
            foreach (char c in new[] { '|', '~', ';', '\n', '\r' }) title = title.Replace(c, ' ');
            if (title.Length > 24) title = title.Substring(0, 24).Trim();
            Esine tape = esineet.Find(e => (e.Kind == KVhs || e.Kind == KKela) && e.Go != null && e.Go.transform.parent != null && Game.UnderPlayer(e.Go.transform));
            if (tape == null) { Chat.System(Lang.T("Prends d'abord une cassette VHS ou une bobine en main.", "Pick up a VHS tape or a film reel first.")); return true; }
            int i = tape.Data.IndexOf('~');
            string content = i < 0 ? tape.Data : tape.Data.Substring(0, i);
            if (content.Length == 0) content = "tyhja";
            SetData(tape, content + "~" + title);
            Chat.System(title.Length > 0 ? Lang.T("Écrit sur la tranche : ", "Written on the spine: ") + title : Lang.T("Tranche effacée.", "Spine wiped."));
            return true;
        }

        static void OnLevelLoadedFilms()
        {
            foreach (Film f in filmCache.Values) { if (f.Clip != null) UnityEngine.Object.Destroy(f.Clip); if (f.Fs != null) try { f.Fs.Close(); } catch { } }
            filmExpect.Clear(); filmFailed.Clear();
            foreach (FilmOut o in filmOut) if (o.Fs != null) o.Fs.Close();
            foreach (FilmIn fi in filmIn.Values) if (fi.Fs != null) fi.Fs.Close();
            filmCache.Clear(); playStart.Clear(); filmOut.Clear(); filmIn.Clear(); filmAsked.Clear();
            // (tout est ferme : jamais pendant une seance ; au chargement de la partie seulement -- au menu, les fichiers du monde
            // d'un invite sont encore ceux de l'hote d'avant, la sauvegarde du nouvel hote arrive juste avant la partie)
            if (Game.LevelName == "GAME") CleanRecus();
        }

        // ---------------------------------------------------------------- films recus : nettoyes (JD 07/11)
        // « toujours nettoyer les films recus ! » : un film arrive par le reseau est note dans videot\recus.txt ; a chaque
        // chargement de la partie, ceux de la liste sont effaces, sauf ceux d'une cassette, d'un magnetoscope, d'un camescope ou d'une
        // bobine de la sauvegarde chargee (MWCoop-esineet.txt, MWCoop-kino.txt ; chez l'invite, celle de l'hote, recue en
        // entrant) -- sinon elle resterait vide, plus personne pour le renvoyer. Jamais un film filme, monte ou importe ici
        // (pas dans la liste), ni un film recu avant cette version (pas note). Morceaux .osa restes d'une reception coupee :
        // effaces aussi (toujours des films recus, inutilisables). [Test] NettoyerRecus=0 : rien.
        static string RecusFile { get { return Path.Combine(FilmDir, "recus.txt"); } }

        static void NoteRecu(string film)
        {
            try { File.AppendAllText(RecusFile, film + "\r\n"); } catch (Exception e) { L("video : recus.txt : " + e.Message); }
        }

        // films des cassettes et bobines de la sauvegarde chargee (en trop si un titre ressemble a un film : garde de trop, jamais l'inverse)
        static HashSet<string> SavedFilms()
        {
            var s = new HashSet<string>();
            foreach (string name in new[] { "MWCoop-esineet.txt", "MWCoop-kino.txt" })
            {
                string p = WorldFiles.PathOf(name);
                if (!File.Exists(p)) continue;
                bool kinoFile = name == "MWCoop-kino.txt";
                foreach (string l in File.ReadAllLines(p))
                {
                    string[] c = l.Split(';');
                    string data = kinoFile ? (c.Length >= 2 ? c[1] : "") : (c.Length >= 3 ? c[2] : "");
                    foreach (string part in data.Split('|')) foreach (string f in Clips(part)) s.Add(f);
                }
            }
            return s;
        }

        static void CleanRecus()
        {
            if (Config.Testing && Config.GetInt("Test", "NettoyerRecus", 1) == 0) return;
            try
            {
                if (!Directory.Exists(FilmDir)) return;
                int osa = 0;
                foreach (string p in Directory.GetFiles(FilmDir, "*.mwv.osa")) { try { File.Delete(p); osa++; } catch { } }
                if (!File.Exists(RecusFile)) { if (osa > 0) L("video : " + osa + " morceau(x) de film recu (.osa) efface(s)"); return; }
                HashSet<string> keep = SavedFilms();   // (illisible : exception, rien n'est efface)
                var stay = new List<string>(); var seen = new HashSet<string>();
                int gone = 0; long bytes = 0;
                foreach (string l in File.ReadAllLines(RecusFile))
                {
                    string f = l.Trim();
                    if (f.Length == 0 || f.Length > 64 || f.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || !seen.Add(f)) continue;
                    string p = FilmPath(f);
                    if (!File.Exists(p)) continue;
                    if (keep.Contains(f)) { stay.Add(f); continue; }
                    try { long n = new FileInfo(p).Length; File.Delete(p); gone++; bytes += n; }
                    catch (Exception e) { stay.Add(f); L("video : film recu " + f + " pas efface : " + e.Message); }
                }
                if (stay.Count > 0) File.WriteAllLines(RecusFile, stay.ToArray()); else File.Delete(RecusFile);
                if (gone + osa + stay.Count > 0)
                    L("video : films recus : " + gone + " efface(s) (" + bytes / 1048576 + " Mo)" + (osa > 0 ? ", " + osa + " morceau(x) .osa" : "") + ", " + stay.Count + " garde(s) (cassette ou bobine de la sauvegarde" + (stay.Count > 0 ? " : " + string.Join(", ", stay.ToArray()) : "") + ")");
            }
            catch (Exception e) { L("video : nettoyage des films recus : " + e.Message); }
        }
    }
}
