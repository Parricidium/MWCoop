using UnityEngine;

namespace Voyage
{
    // Le Cessna de l'aerodrome (JD, 23/10) : gare sur l'aire ; quand un voyageur part, il saute au bout ouest de la piste,
    // roule, decolle vers l'est et s'eloigne (puis revient se garer 40 s apres) ; au retour d'un voyageur, il arrive de
    // l'ouest, se pose, freine, puis se regare. Animation en fonction du temps (meme mouvement chez chacun : en co-op, le
    // depart et l'arrivee sont annonces par l'API de MWCoop). Helice qui tourne, son « propeller1 » du jeu (l'hydroglisseur).
    static class Avion
    {
        public static Transform T, Prop;
        static AudioSource snd;
        static Vector3 park;
        static Quaternion parkRot;
        enum Mode { Parked, TakeOff, Landing, Gone }
        static Mode mode;
        static float t0, until;

        const float Accel = 3.2f, LiftT = 9f;           // (roulage : 1,6 t^2 m, decollage a 9 s, 29 m/s)
        const float TouchT = 9.5f, Brake = 6f;          // (approche a 30 m/s, toucher a 9,5 s, freinage 6 m/s^2)
        static float StartX { get { return Lieux.StartU; } }       // (abscisses le long de la piste : repere Lieux.RunO / RunDir)
        static float ApproachX { get { return Lieux.ApproachU; } }
        const float Sink = 0.09f;                                       // (origine du modele : roues 2 cm au-dessus du sol)

        public static void Setup(Transform t, Transform prop)
        {
            T = t; Prop = prop; park = t.position; parkRot = t.rotation; mode = Mode.Parked;
            AudioClip clip = null;
            string want = Jeu.Msc ? "boat_4000rpm" : "propeller1";   // (My Summer Car n'a pas l'hydroglisseur : le moteur du bateau)
            foreach (AudioClip c in Resources.FindObjectsOfTypeAll<AudioClip>()) if (c != null && c.name == want) { clip = c; break; }
            if (clip != null)
            {
                snd = t.gameObject.AddComponent<AudioSource>();
                snd.clip = clip; snd.loop = true; snd.playOnAwake = false; snd.spatialBlend = 1f;
                snd.rolloffMode = AudioRolloffMode.Logarithmic; snd.minDistance = 15f; snd.maxDistance = 700f; snd.volume = 0.9f; snd.dopplerLevel = 0.6f;
            }
            Journal.Info("avion : gare en " + park + (clip != null ? ", son " + want : ", sans son (" + want + " introuvable)"));
        }

        public static bool Busy { get { return mode == Mode.TakeOff || mode == Mode.Landing; } }

        public static void TakeOff()
        {
            if (T == null || mode == Mode.TakeOff) return;
            mode = Mode.TakeOff; t0 = Time.time;
            T.gameObject.SetActive(true);
            if (snd != null && !snd.isPlaying) snd.Play();
            Journal.Info("avion : decollage");
        }

        public static void Land()
        {
            if (T == null || mode == Mode.Landing) return;
            mode = Mode.Landing; t0 = Time.time;
            T.gameObject.SetActive(true);
            if (snd != null && !snd.isPlaying) snd.Play();
            Journal.Info("avion : atterrissage");
        }

        static void Park()
        {
            mode = Mode.Parked;
            T.gameObject.SetActive(true);
            T.position = park; T.rotation = parkRot;
            if (snd != null) snd.Stop();
        }

        // u : abscisse le long de la piste (sens du decollage), h : hauteur au-dessus du sol, pitch : nez leve
        static void Put(float u, float h, float pitch)
        {
            Vector3 p = Lieux.RunO + Lieux.RunDir * u;
            T.position = new Vector3(p.x, Lieux.GroundY(p.x, p.z) - Sink + h, p.z);
            T.rotation = Quaternion.LookRotation(Lieux.RunDir) * Quaternion.Euler(-pitch, 0f, 0f);
        }

        public static void Tick()
        {
            if (T == null) return;
            float t = Time.time - t0, speed = 0f;
            switch (mode)
            {
                case Mode.TakeOff:
                    if (t < LiftT) { Put(StartX + 0.5f * Accel * t * t, 0f, Mathf.Clamp01(t - 8f) * 6f); speed = Accel * t; }
                    else
                    {
                        float d = t - LiftT;
                        Put(StartX + 0.5f * Accel * LiftT * LiftT + Accel * LiftT * d + 0.75f * d * d, 1.2f * d * d, Mathf.Min(10f, 6f + d * 3f));
                        speed = Accel * LiftT + 1.5f * d;
                    }
                    if (t > 24f) { mode = Mode.Gone; until = Time.time + 40f; T.gameObject.SetActive(false); if (snd != null) snd.Stop(); }
                    break;
                case Mode.Landing:
                    if (t < TouchT) { float a = TouchT - t; Put(ApproachX + 30f * t, 0.7f * a * a, 4f); speed = 30f; }
                    else
                    {
                        float d = Mathf.Min(t - TouchT, 30f / Brake);
                        Put(ApproachX + 30f * TouchT + 30f * d - 0.5f * Brake * d * d, 0f, Mathf.Max(0f, 4f - (t - TouchT) * 4f));
                        speed = Mathf.Max(0f, 30f - Brake * (t - TouchT));
                    }
                    if (t > TouchT + 30f / Brake + 6f) Park();
                    break;
                case Mode.Gone:
                    if (Time.time > until) Park();
                    break;
            }
            if (Prop != null && Busy) Prop.Rotate(0f, 0f, 1500f * Time.deltaTime, Space.Self);
            if (snd != null && snd.isPlaying) snd.pitch = 0.75f + Mathf.Clamp01(speed / 30f) * 0.5f;
        }
    }

    // Camera de cinema : posee au bord de la piste, elle suit l'avion (cadrage resserre quand il s'eloigne)
    static class Cine
    {
        static Camera cam;
        public static Camera Cam { get { return cam != null && cam.enabled ? cam : null; } }

        public static void Watch(Vector3 at)
        {
            if (cam == null)
            {
                var g = new GameObject("VOYAGE_CINE");
                cam = g.AddComponent<Camera>();
                cam.nearClipPlane = 0.2f; cam.farClipPlane = 3000f;
            }
            Camera main = Camera.main;
            cam.depth = (main != null ? main.depth : 0f) + 20f;
            if (main != null) cam.cullingMask = main.cullingMask;
            cam.transform.position = at;
            cam.enabled = true;
            Tick();
        }

        public static void Stop() { if (cam != null) cam.enabled = false; }

        public static void Tick()
        {
            if (cam == null || !cam.enabled || Avion.T == null) return;
            Vector3 target = Avion.T.position + Vector3.up * 1.4f;
            cam.transform.LookAt(target);
            float dist = Vector3.Distance(cam.transform.position, target);
            cam.fieldOfView = Mathf.Clamp(2f * Mathf.Atan(9f / Mathf.Max(dist, 1f)) * Mathf.Rad2Deg, 9f, 60f);
        }
    }
}
