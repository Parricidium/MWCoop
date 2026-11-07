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
        class FilmOut { public string Film; public FileStream Fs; public long Length; public int Next, Count; public bool Again; }
        class FilmIn { public FileStream Fs; public bool[] Have; public int Got, Count, Prefix; public float Last; }   // (Prefix : morceaux 0..Prefix-1 tous arrives -- lecture pendant la reception)
        static readonly List<FilmOut> filmOut = new List<FilmOut>();
        static readonly Dictionary<string, FilmIn> filmIn = new Dictionary<string, FilmIn>();
        static readonly Dictionary<string, float> filmAsked = new Dictionary<string, float>();
        const int FilmChunk = 1000, FilmMaxParts = 10000000;   // (10 Go au plus : bobines de 3 h, 07/11 -- 4 Go avant)
        static float filmSendBudget, filmSendT;
        static byte[] filmBuf;

        static void SendFilm(string film)
        {
            if (!Session.Active || Session.RemoteCount <= 0 || !HaveFilm(film)) return;
            FilmOut o = filmOut.Find(x => x.Film == film);
            if (o != null) { if (o.Next > 0) o.Again = true; return; }
            try
            {
                var fs = new FileStream(FilmPath(film), FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                filmOut.Add(new FilmOut { Film = film, Fs = fs, Length = fs.Length, Count = (int)((fs.Length + FilmChunk - 1) / FilmChunk) });
                L("video : envoi du film " + film + " (" + fs.Length / 1024 + " Ko)");
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
            if (filmOut.Count == 0) return;
            if (!Session.Active) { foreach (FilmOut x in filmOut) if (x.Fs != null) x.Fs.Close(); filmOut.Clear(); return; }
            // debit : FilmiKoS Ko/s au plus, et rien de plus tant que le canal fiable est plein
            float dt = filmSendT > 0f ? Mathf.Min(0.25f, now - filmSendT) : 0f;
            filmSendT = now;
            int kos = Config.GetInt("Test", "FilmiKoS", Config.GetInt("Boutique", "FilmiKoS", 1500));   // ([Test] FilmiKoS : reception lente simulee)
            filmSendBudget = Mathf.Min(filmSendBudget + dt * kos * 1024f / FilmChunk, 400f);
            int room = Config.GetInt("Boutique", "FilmiFile", 160) - MaxInFlight();
            FilmOut o = filmOut[0];
            if (filmBuf == null) filmBuf = new byte[FilmChunk];
            try
            {
                for (; room > 0 && filmSendBudget >= 1f && o.Next < o.Count; room--, filmSendBudget -= 1f, o.Next++)
                {
                    long off = (long)o.Next * FilmChunk;
                    int n = (int)Math.Min(FilmChunk, o.Length - off);
                    o.Fs.Position = off;
                    int got = 0;
                    while (got < n) { int k = o.Fs.Read(filmBuf, got, n - got); if (k <= 0) break; got += k; }
                    Session.SendAll(new NetWriter(Msg.Job).U8(Session.LocalId).Str("@filmi").Str(o.Film).I32(o.Next).I32(o.Count).Bytes(filmBuf, 0, got), true);
                }
            }
            catch (Exception e) { L("video : envoi de " + o.Film + " interrompu : " + e.Message); o.Next = o.Count; o.Again = false; }
            if (o.Next >= o.Count)
            {
                if (o.Again) { o.Again = false; o.Next = 0; }
                else { if (o.Fs != null) o.Fs.Close(); filmOut.RemoveAt(0); L("video : film " + o.Film + " envoye (" + o.Count + " morceaux)"); }
            }
        }

        public static void OnFilmPart(int who, NetReader r)
        {
            string film = Session.Clean(r.Str(), 64); int i = r.I32(), n = r.I32(); byte[] b = r.Bytes();
            if (film.Length == 0 || film.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || n <= 0 || n > FilmMaxParts || i < 0 || i >= n || b.Length > FilmChunk) return;
            if (HaveFilm(film) || KinoFilmRefused(film) || filmFailed.Contains(film)) return;
            FilmIn fi;
            try
            {
                if (!filmIn.TryGetValue(film, out fi) || fi.Count != n)
                {
                    if (fi != null && fi.Fs != null) fi.Fs.Close();
                    Directory.CreateDirectory(FilmDir);
                    fi = new FilmIn { Fs = new FileStream(FilmPath(film) + ".osa", FileMode.Create, FileAccess.ReadWrite, FileShare.Read), Have = new bool[n], Count = n };
                    filmIn[film] = fi;
                }
                fi.Last = Time.realtimeSinceStartup;
                if (fi.Have[i]) return;
                if (Config.Testing && i > 2000 && Config.GetInt("Test", "FilmDisquePlein", 0) != 0) throw new IOException("essai : disque plein");   // (essai du message)
                fi.Fs.Position = (long)i * FilmChunk;
                fi.Fs.Write(b, 0, b.Length);
                fi.Have[i] = true; fi.Got++;
                while (fi.Prefix < fi.Count && fi.Have[fi.Prefix]) fi.Prefix++;
                if (fi.Got < n) return;
                filmIn.Remove(film);
                fi.Fs.Close();
                File.Move(FilmPath(film) + ".osa", FilmPath(film));
                L("video : film " + film + " recu de #" + who + " (" + n + " morceaux)");
                KinoFilmArrived(film);
            }
            catch (Exception e)
            {
                L("video : film " + film + " pas ecrit : " + e.Message);
                if (filmIn.TryGetValue(film, out fi)) { if (fi.Fs != null) fi.Fs.Close(); filmIn.Remove(film); }
                // (disque plein, dossier refuse... : dit une fois au joueur, et plus redemande de la partie -- sinon il revenait sans fin)
                if (filmFailed.Add(film)) { Chat.System(Lang.T("Film pas enregistré (disque plein ?) : ", "Film not saved (disk full?): ") + e.Message); KinoFilmFailed(film); }
            }
        }

        public static void OnFilmAsk(int who, NetReader r)
        {
            string film = Session.Clean(r.Str(), 64);
            if (film.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || !HaveFilm(film)) return;
            // l'hote repond ; un invite seulement pour un film qu'il a filme lui-meme pendant cette partie
            if (Session.IsHost || film.StartsWith("f" + Session.LocalId + "-")) SendFilm(film);
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
        }
    }
}
