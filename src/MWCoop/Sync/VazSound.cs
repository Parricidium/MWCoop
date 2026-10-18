using System.Collections.Generic;
using System.IO;
using HutongGames.PlayMaker;
using UnityEngine;

namespace MWCoop
{
    // Son moteur de la VAZ-2108, FACULTATIF : boucles a regime constant dans MWCoop/vaz/sons (sons.txt : lignes
    // « fichier.wav tr/min charge|frein », et « fichier.wav 0 cle|demarreur|prise » pour le demarreur). Rien dans la release :
    // sans ce dossier, la VAZ garde le son de la SORBET. (JD, 18/10 : le son de sa 205, tire d'un enregistrement qui ne peut
    // pas etre publie -- pose seulement dans son jeu.) Reprise du mod prive Peugeot205 :
    //  - sons moteur de la copie (AudioEngine : Low/Med/High/ExhaustSound, sound_exhaust) rendus muets ; chaque boucle joue en
    //    continu, hauteur = regime / regime de la boucle, volume en fondu entre les deux boucles qui encadrent le regime, charge
    //    (gaz) et frein (pied leve) melanges selon l'accelerateur ;
    //  - regime et gaz lus sur le Drivetrain : chez les autres joueurs, VehicleSync y pose ceux du conducteur (copie
    //    cinematique) -- ils entendent le meme moteur s'ils ont les fichiers ;
    //  - demarreur : l'automate Starter de la copie joue la cle et la prise par MasterAudio (banque commune a la vraie SORBET) :
    //    ces actions sont coupees sur la copie, la boucle StarterSound rendue muette, nos sons suivent ses etats.
    public class VazSound
    {
        public static string Dir { get { return Path.Combine(VazCar.Dir, "sons"); } }
        public static bool HasFiles { get { return File.Exists(Path.Combine(Dir, "sons.txt")); } }

        class Loop { public AudioSource Src; public float Rpm; public bool Load; }
        readonly List<Loop> loops = new List<Loop>();
        readonly List<AudioSource> muted = new List<AudioSource>();
        readonly Dictionary<string, AudioClip> starter = new Dictionary<string, AudioClip>();
        static readonly Dictionary<string, AudioClip> clipCache = new Dictionary<string, AudioClip>();
        Drivetrain dt;
        PlayMakerFSM starterFsm;
        AudioSource crank, oneShot, starterMuted;
        string starterState = "";
        float load, catchTime = -100f;

        // at : point du moteur (enfant de la carrosserie). Null sans fichiers.
        public static VazSound Create(GameObject car, Transform at)
        {
            if (!HasFiles) return null;
            var s = new VazSound();
            s.Setup(car, at);
            return s.loops.Count > 0 ? s : null;
        }

        void Setup(GameObject car, Transform at)
        {
            dt = car.GetComponent<Drivetrain>();
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            foreach (string l in File.ReadAllLines(Path.Combine(Dir, "sons.txt")))
            {
                string[] p = l.Split(new[] { ' ', '\t' }, System.StringSplitOptions.RemoveEmptyEntries);
                if (p.Length < 3) continue;
                AudioClip clip = Wav(Path.Combine(Dir, p[0]));
                if (clip == null) { Log.Warn("vaz : son " + p[0] + " illisible"); continue; }
                if (p[2] != "charge" && p[2] != "frein") { starter[p[2]] = clip; continue; }
                float rpm;
                if (!float.TryParse(p[1], System.Globalization.NumberStyles.Float, ci, out rpm) || rpm <= 0f) continue;
                var go = new GameObject("VAZ_son_" + p[0]);
                go.transform.SetParent(at, false);
                var a = go.AddComponent<AudioSource>();
                a.clip = clip; a.loop = true; a.volume = 0f; a.spatialBlend = 1f; a.minDistance = 1f; a.maxDistance = 150f; a.dopplerLevel = 0.3f; a.playOnAwake = false;
                loops.Add(new Loop { Src = a, Rpm = rpm, Load = p[2] == "charge" });
            }
            loops.Sort((x, y) => x.Rpm.CompareTo(y.Rpm));
            Transform ae = Find(car.transform, "AudioEngine");
            if (ae != null)
                foreach (string n in new[] { "Low", "Med", "High", "ExhaustSound" })
                {
                    Transform t = ae.Find(n);
                    if (t != null && t.GetComponent<AudioSource>() != null) muted.Add(t.GetComponent<AudioSource>());
                }
            Transform far = Find(car.transform, "sound_exhaust");
            if (far != null && far.GetComponent<AudioSource>() != null) muted.Add(far.GetComponent<AudioSource>());
            // demarreur
            Transform st = Find(car.transform, "STARTERxSorbet");
            if (st != null && starter.Count > 0)
            {
                foreach (PlayMakerFSM f in st.GetComponents<PlayMakerFSM>()) if (f.FsmName == "Starter") starterFsm = f;
                if (starterFsm != null)
                {
                    starterFsm.Fsm.InitData();
                    foreach (FsmState state in starterFsm.FsmStates)
                        if (state.Name == "Turn key" || state.Name == "Start engine")
                            foreach (FsmStateAction ac in state.Actions)
                                if (ac.GetType().Name == "MasterAudioPlaySound") ac.Enabled = false;
                    Transform ss = Find(car.transform, "StarterSound");
                    if (ss != null) starterMuted = ss.GetComponent<AudioSource>();
                    var go = new GameObject("VAZ_demarreur");
                    go.transform.SetParent(at, false);
                    crank = go.AddComponent<AudioSource>(); oneShot = go.AddComponent<AudioSource>();
                    foreach (AudioSource a in new[] { crank, oneShot }) { a.spatialBlend = 1f; a.minDistance = 1f; a.maxDistance = 60f; a.playOnAwake = false; a.dopplerLevel = 0f; }
                    AudioClip c;
                    if (starter.TryGetValue("demarreur", out c)) { crank.clip = c; crank.loop = true; }
                }
            }
            Log.Info("vaz : son moteur : " + loops.Count + " boucles, " + starter.Count + " sons de demarreur" + (starterFsm == null ? " (automate Starter introuvable)" : "") + ", " + muted.Count + " sons de la SORBET coupes");
        }

        static Transform Find(Transform t, string name)
        {
            if (t.name == name) return t;
            for (int i = 0; i < t.childCount; i++) { Transform r = Find(t.GetChild(i), name); if (r != null) return r; }
            return null;
        }

        public void Update()
        {
            if (dt == null) return;
            foreach (AudioSource a in muted) if (a != null && !a.mute) a.mute = true;
            float rpm = dt.rpm, thr = Mathf.Clamp01(dt.throttle);
            load = Mathf.MoveTowards(load, thr, Time.deltaTime * 6f);
            bool on = rpm > 300f;
            Starter();
            float duck = Mathf.Clamp01((Time.time - catchTime - 0.7f) / 0.9f);
            // (enregistrement de la 205 : ralenti vers 1 850 tr/min, le moteur du jeu vers 900 -> sous 2 556 tr/min le regime
            // entendu est 0,55 x regime + 1 150)
            rpm = rpm < 2556f ? 0.55f * rpm + 1150f : rpm;
            float offW = (1f - load) * Mathf.Clamp01((rpm - 2000f) / 800f);
            Mix(true, rpm, on ? duck * 0.9f * (1f - 0.55f * offW) * (0.55f + 0.45f * load + 0.45f * (1f - Mathf.Clamp01((rpm - 1500f) / 1500f))) : 0f);
            Mix(false, rpm, on ? duck * 0.75f * offW : 0f);
        }

        void Starter()
        {
            if (starterFsm == null) return;
            if (starterMuted != null && !starterMuted.mute) starterMuted.mute = true;
            string s = starterFsm.ActiveStateName ?? "";
            if (s != starterState)
            {
                AudioClip c;
                if (s == "Turn key" && starter.TryGetValue("cle", out c)) oneShot.PlayOneShot(c, 1f);
                if (s == "Start engine") { catchTime = Time.time; if (crank.isPlaying) crank.Stop(); if (starter.TryGetValue("prise", out c)) oneShot.PlayOneShot(c, 1f); }
                starterState = s;
            }
            bool cranking = s == "Fuel Mixture" || s == "Fuel Mixture 2";
            if (cranking && crank.clip != null)
            {
                FsmFloat sp = starterFsm.FsmVariables.GetFsmFloat("StarterPitch");
                crank.pitch = 0.6f + 0.4f * (sp != null ? Mathf.Clamp(sp.Value, 0.4f, 1f) : 1f);
                if (!crank.isPlaying) crank.Play();
            }
            else if (crank.isPlaying) crank.Stop();
        }

        // fondu a puissance constante entre les deux boucles (charge ou frein) qui encadrent le regime
        void Mix(bool charge, float rpm, float vol)
        {
            var set = new List<Loop>();
            foreach (Loop l in loops) if (l.Load == charge) set.Add(l);
            if (set.Count == 0) return;
            int hi = set.FindIndex(x => x.Rpm >= rpm);
            int lo = hi < 0 ? set.Count - 1 : Mathf.Max(0, hi - 1);
            if (hi < 0) hi = lo;
            float k = hi == lo ? 0f : Mathf.Clamp01((rpm - set[lo].Rpm) / (set[hi].Rpm - set[lo].Rpm));
            for (int i = 0; i < set.Count; i++)
            {
                float w = i == lo && i == hi ? 1f : i == lo ? Mathf.Cos(k * Mathf.PI / 2f) : i == hi ? Mathf.Sin(k * Mathf.PI / 2f) : 0f;
                Loop L = set[i];
                L.Src.volume = vol * w;
                L.Src.pitch = Mathf.Clamp(rpm / L.Rpm, 0.4f, 2.2f);
                if (L.Src.volume > 0.001f) { if (!L.Src.isPlaying) L.Src.Play(); }
                else if (L.Src.isPlaying && vol <= 0f) L.Src.Stop();
            }
        }

        // WAV PCM 16 bits (mono ou stereo) -> AudioClip (partage par toutes les VAZ)
        static AudioClip Wav(string path)
        {
            AudioClip c;
            if (clipCache.TryGetValue(path, out c) && c != null) return c;
            if (!File.Exists(path)) return null;
            byte[] b = File.ReadAllBytes(path);
            if (b.Length < 44 || System.Text.Encoding.ASCII.GetString(b, 0, 4) != "RIFF" || System.Text.Encoding.ASCII.GetString(b, 8, 4) != "WAVE") return null;
            int ch = 1, rate = 44100, bits = 16, pos = 12;
            while (pos + 8 <= b.Length)
            {
                string id = System.Text.Encoding.ASCII.GetString(b, pos, 4);
                int len = System.BitConverter.ToInt32(b, pos + 4);
                if (id == "fmt ") { ch = System.BitConverter.ToInt16(b, pos + 10); rate = System.BitConverter.ToInt32(b, pos + 12); bits = System.BitConverter.ToInt16(b, pos + 22); }
                else if (id == "data")
                {
                    if (bits != 16) return null;
                    int n = System.Math.Min(len, b.Length - pos - 8) / 2;
                    var data = new float[n];
                    for (int i = 0; i < n; i++) data[i] = System.BitConverter.ToInt16(b, pos + 8 + i * 2) / 32768f;
                    c = AudioClip.Create(Path.GetFileNameWithoutExtension(path), n / ch, ch, rate, false);
                    c.SetData(data, 0);
                    c.hideFlags = HideFlags.HideAndDontSave;
                    clipCache[path] = c;
                    return c;
                }
                pos += 8 + len + (len & 1);
            }
            return null;
        }
    }
}
