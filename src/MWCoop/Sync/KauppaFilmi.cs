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
        class Film { public string Id; public byte[] Data; public readonly List<int> Off = new List<int>(), Len = new List<int>(); public readonly List<uint> Ms = new List<uint>(); public float[] Sound; public uint LengthMs; public AudioClip Clip; }
        static readonly Dictionary<string, Film> filmCache = new Dictionary<string, Film>();
        static readonly Dictionary<string, KeyValuePair<float, string>> playStart = new Dictionary<string, KeyValuePair<float, string>>();   // magnetoscope -> debut de la lecture ici, cassette

        static bool HaveFilm(string film) { return film.Length > 0 && File.Exists(FilmPath(film)); }

        static Film LoadFilm(string id)
        {
            Film f;
            if (filmCache.TryGetValue(id, out f)) return f;
            if (!HaveFilm(id)) return null;
            try
            {
                byte[] all = File.ReadAllBytes(FilmPath(id));
                if (all.Length < 11 || all[0] != 'M' || all[1] != 'W' || all[2] != 'V' || all[3] != '1') { L("video : film " + id + " illisible"); return null; }
                f = new Film { Id = id, Data = all };
                var snd = new List<byte>();
                int p = 11;
                while (p + 9 <= all.Length)
                {
                    int kind = all[p]; uint ms = BitConverter.ToUInt32(all, p + 1); int n = BitConverter.ToInt32(all, p + 5);
                    p += 9;
                    if (n < 0 || p + n > all.Length) break;
                    if (kind == 1) { f.Off.Add(p); f.Len.Add(n); f.Ms.Add(ms); }
                    else if (kind == 2) for (int i = 0; i < n; i++) snd.Add(all[p + i]);
                    if (ms > f.LengthMs) f.LengthMs = ms;
                    p += n;
                }
                f.Sound = new float[snd.Count];
                for (int i = 0; i < snd.Count; i++) f.Sound[i] = MuLawDecode(snd[i]);
                f.LengthMs = Math.Max(f.LengthMs, (uint)(f.Sound.Length * 1000L / RecRate));
                if (f.Sound.Length > 0)
                {
                    f.Clip = AudioClip.Create("kauppa_" + id, f.Sound.Length, 1, RecRate, false);
                    f.Clip.SetData(f.Sound, 0);
                }
                // (garde les prises des cassettes en lecture, et quelques autres)
                if (filmCache.Count >= 6)
                {
                    string drop = null;
                    foreach (string k in filmCache.Keys) if (!showing.Exists(s => s.Vcr != null && Clips(Tape(s.Vcr)).Contains(k))) { drop = k; break; }
                    if (drop != null) { if (filmCache[drop].Clip != null) UnityEngine.Object.Destroy(filmCache[drop].Clip); filmCache.Remove(drop); }
                }
                filmCache[id] = f;
                L("video : film " + id + " charge (" + f.Ms.Count + " images, " + (f.LengthMs / 1000f).ToString("F1") + " s)");
                return f;
            }
            catch (Exception e) { L("video : film " + id + " : " + e.Message); return null; }
        }

        // Prises d'une cassette, dans l'ordre (chaque enregistrement s'ajoute a la suite : "f1+f2+f3~titre").
        public static List<string> Clips(string content)
        {
            var l = new List<string>();
            foreach (string c in TapeFilm(content).Split('+')) if (c.Length > 0) l.Add(c);
            return l;
        }

        // ---------------------------------------------------------------- lecture sur un ecran (appele par TickScreens)
        static void ShowFilm(Showing s, float now)
        {
            Esine v = s.Vcr;
            string tape = Tape(v);
            KeyValuePair<float, string> ps;
            if (!playStart.TryGetValue(v.Id, out ps) || ps.Value != tape) { ps = new KeyValuePair<float, string>(now, tape); playStart[v.Id] = ps; }   // (autre cassette : depuis le debut)
            float t = now - ps.Key;
            // les prises bout a bout ; il en manque une : neige en attendant (puis reprise au bon endroit)
            List<string> clips = Clips(tape);
            Film f = null; float clipStart = 0f, at = 0f;
            bool all = clips.Count > 0;
            var loaded = new List<Film>();
            foreach (string c in clips) { Film x = LoadFilm(c); if (x == null) { AskFilm(c, now); all = false; } else loaded.Add(x); }
            if (all)
                foreach (Film x in loaded)
                {
                    float len = x.LengthMs / 1000f;
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
                    if (f.Clip != null && tc < f.Clip.length - 0.05f) { s.Audio.time = Mathf.Max(0f, tc); s.Audio.Play(); }
                }
                L("video : " + v.Id + " lit la prise " + f.Id + " (" + (clips.IndexOf(f.Id) + 1) + "/" + clips.Count + ") depuis " + tc.ToString("F1") + " s");
            }
            if (r.sharedMaterial != s.Mat) r.sharedMaterial = s.Mat;
            // image de l'instant
            uint ms = (uint)Mathf.Max(0f, tc * 1000f);
            int lo = 0, hi = f.Ms.Count - 1, idx = -1;
            while (lo <= hi) { int mid = (lo + hi) / 2; if (f.Ms[mid] <= ms) { idx = mid; lo = mid + 1; } else hi = mid - 1; }
            if (idx >= 0 && idx != s.Frame)
            {
                s.Frame = idx;
                var jpg = new byte[f.Len[idx]];
                Buffer.BlockCopy(f.Data, f.Off[idx], jpg, 0, jpg.Length);
                s.Tex.LoadImage(jpg);
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
        class FilmOut { public string Film; public byte[] Data; public int Next, Count; public bool Again; }
        class FilmIn { public byte[][] Parts; public int Got; public float Last; }
        static readonly List<FilmOut> filmOut = new List<FilmOut>();
        static readonly Dictionary<string, FilmIn> filmIn = new Dictionary<string, FilmIn>();
        static readonly Dictionary<string, float> filmAsked = new Dictionary<string, float>();
        const int FilmChunk = 1000;

        static void SendFilm(string film)
        {
            if (!Session.Active || Session.RemoteCount <= 0 || !HaveFilm(film)) return;
            FilmOut o = filmOut.Find(x => x.Film == film);
            if (o != null) { if (o.Next > 0) o.Again = true; return; }
            try
            {
                byte[] b = File.ReadAllBytes(FilmPath(film));
                filmOut.Add(new FilmOut { Film = film, Data = b, Count = (b.Length + FilmChunk - 1) / FilmChunk });
                L("video : envoi du film " + film + " (" + b.Length / 1024 + " Ko)");
            }
            catch (Exception e) { L("video : envoi de " + film + " : " + e.Message); }
        }

        static void AskFilm(string film, float now)
        {
            if (!Session.Active || Session.RemoteCount <= 0) return;
            float last;
            FilmIn fi;
            if (filmIn.TryGetValue(film, out fi) && now - fi.Last < 20f) return;   // (arrive)
            if (filmAsked.TryGetValue(film, out last) && now - last < 25f) return;
            filmAsked[film] = now;
            Session.SendAll(new NetWriter(Msg.Job).U8(Session.LocalId).Str("@filmi?").Str(film), true);
            L("video : film " + film + " demande");
        }

        static void TickFilms(float now)
        {
            TickPlayStarts();
            if (filmOut.Count == 0) return;
            if (!Session.Active) { filmOut.Clear(); return; }
            int budget = Mathf.Clamp(Config.GetInt("Boutique", "FilmiVauhti", 3), 1, 20);
            FilmOut o = filmOut[0];
            for (int k = 0; k < budget && o.Next < o.Count; k++, o.Next++)
            {
                int off = o.Next * FilmChunk, n = Math.Min(FilmChunk, o.Data.Length - off);
                Session.SendAll(new NetWriter(Msg.Job).U8(Session.LocalId).Str("@filmi").Str(o.Film).I32(o.Next).I32(o.Count).Bytes(o.Data, off, n), true);
            }
            if (o.Next >= o.Count)
            {
                if (o.Again) { o.Again = false; o.Next = 0; }
                else { filmOut.RemoveAt(0); L("video : film " + o.Film + " envoye (" + o.Count + " morceaux)"); }
            }
        }

        public static void OnFilmPart(int who, NetReader r)
        {
            string film = Session.Clean(r.Str(), 64); int i = r.I32(), n = r.I32(); byte[] b = r.Bytes();
            if (film.Length == 0 || film.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || n <= 0 || n > 200000 || i < 0 || i >= n) return;
            if (HaveFilm(film)) return;
            FilmIn fi;
            if (!filmIn.TryGetValue(film, out fi) || fi.Parts.Length != n) { fi = new FilmIn { Parts = new byte[n][] }; filmIn[film] = fi; }
            fi.Last = Time.realtimeSinceStartup;
            if (fi.Parts[i] != null) return;
            fi.Parts[i] = b; fi.Got++;
            if (fi.Got < n) return;
            filmIn.Remove(film);
            try
            {
                Directory.CreateDirectory(FilmDir);
                string tmp = FilmPath(film) + ".osa";
                using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write))
                    foreach (byte[] part in fi.Parts) fs.Write(part, 0, part.Length);
                File.Move(tmp, FilmPath(film));
                L("video : film " + film + " recu de #" + who + " (" + n + " morceaux)");
            }
            catch (Exception e) { L("video : film " + film + " pas ecrit : " + e.Message); }
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
                Transform t = e.Go.transform.Find("nimi");
                TextMesh tm = t != null ? t.GetComponent<TextMesh>() : null;
                if (tm == null)
                {
                    if (title.Length == 0) continue;
                    if (arial == null) arial = Resources.GetBuiltinResource<Font>("Arial.ttf");
                    if (arial == null) continue;
                    var g = new GameObject("nimi");
                    g.transform.SetParent(e.Go.transform, false);
                    g.transform.localPosition = new Vector3(0.012f, 0f, -0.103f / 2f - 0.0014f);
                    g.transform.localRotation = Quaternion.identity;
                    tm = g.AddComponent<TextMesh>();
                    tm.font = arial; tm.fontSize = 48; tm.characterSize = 0.0021f; tm.anchor = TextAnchor.MiddleCenter;
                    tm.alignment = TextAlignment.Center; tm.color = new Color(0.08f, 0.1f, 0.32f);
                    if (labelMat == null)
                    {
                        Shader sh = Shader.Find("Legacy Shaders/Transparent/Diffuse") ?? Shader.Find("Transparent/Diffuse") ?? arial.material.shader;
                        labelMat = new Material(sh) { name = "kauppa_tranche", color = new Color(0.06f, 0.07f, 0.2f, 1f) };
                    }
                    g.GetComponent<MeshRenderer>().sharedMaterial = labelMat;
                }
                if (labelMat != null && labelMat.mainTexture != arial.material.mainTexture) labelMat.mainTexture = arial.material.mainTexture;   // (atlas refait par la police)
                if (tm.text != title) tm.text = title;
            }
        }

        // tchat : « /titre Nom » (ou /title) avec une cassette en main
        public static bool ChatCommand(string text)
        {
            string low = text.ToLowerInvariant();
            if (!(low.StartsWith("/titre") || low.StartsWith("/title"))) return false;
            if (!Active || root == null) return false;
            string title = text.Length > 6 ? text.Substring(6).Trim() : "";
            foreach (char c in new[] { '|', '~', ';', '\n', '\r' }) title = title.Replace(c, ' ');
            if (title.Length > 24) title = title.Substring(0, 24).Trim();
            Esine tape = esineet.Find(e => e.Kind == KVhs && e.Go != null && e.Go.transform.parent != null && Game.UnderPlayer(e.Go.transform));
            if (tape == null) { Chat.System(Lang.T("Prends d'abord une cassette VHS en main.", "Pick up a VHS tape first.")); return true; }
            int i = tape.Data.IndexOf('~');
            string content = i < 0 ? tape.Data : tape.Data.Substring(0, i);
            if (content.Length == 0) content = "tyhja";
            SetData(tape, content + "~" + title);
            Chat.System(title.Length > 0 ? Lang.T("Écrit sur la tranche : ", "Written on the spine: ") + title : Lang.T("Tranche effacée.", "Spine wiped."));
            return true;
        }

        static void OnLevelLoadedFilms()
        {
            foreach (Film f in filmCache.Values) if (f.Clip != null) UnityEngine.Object.Destroy(f.Clip);
            filmCache.Clear(); playStart.Clear(); filmOut.Clear(); filmIn.Clear(); filmAsked.Clear();
        }
    }
}
