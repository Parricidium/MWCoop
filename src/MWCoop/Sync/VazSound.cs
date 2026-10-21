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
    //
    // Par defaut (sans ce dossier) : le moteur de la Rivett montee en rallye (JD, 18/10 : « les memes sons que la Rivett
    // Rally ») -- sons du jeu, deja chez tous. La CORRIS a deux jeux de sons sous Simulation/ExhaustCorris/ExhaustSmoke :
    // AudioEngineCorrisMuffled (silencieux) et AudioEngineCorrisTune (echappement de course : collecteur, tube avant
    // racing, sortie laterale -> son automate Logic l'allume, diviseur de volume 2). AudioEngineCorrisTune (Low, Med, High,
    // Combustion et leurs automates) et EngineValveSound (soupapes) sont copies sous la VAZ, au moteur ; leurs references a
    // la CORRIS ramenees a la VAZ (Drivetrain : regime et gaz), la lecture du diviseur coupee (pose a 2), les explosions
    // (Combustion) sans la simulation des cylindres de la CORRIS. Le nom AudioEngine... fait que VehicleSync en envoie
    // volume et hauteur aux autres joueurs, comme pour les voitures du jeu. Sons moteur de la SORBET coupes.
    //
    // Demarreur des addons : « fichier.wav 0 demarrage » (lancement + prise d'un seul tenant, joue a l'appui sur le demarreur) ou
    // « demarreur » (boucle) ; la cle garde le son du jeu (JD 21/10).
    // Addons (JD 21/10 : sons du pack « rally cars » de rFactor, MWCoop/addons/<dossier>/sons, outils/rf1/sfx2sons.py) : lignes
    // « fichier.wav <regime naturel> charge|frein <mini> <maxi> <volume> » -- comme rFactor, chaque echantillon joue sur sa
    // plage de regime (fondu ou les plages se chevauchent), hauteur = regime / regime naturel ; « ralenti <tr/min> » : regime
    // du son de ralenti, le ralenti du jeu (~900) y est ramene (puis rejoint le vrai regime a 3 000).
    public class VazSound
    {
        string dir;
        bool HasFiles { get { return File.Exists(Path.Combine(dir, "sons.txt")); } }

        class Loop { public AudioSource Src; public float Rpm, Min, Max = -1f, Vol = 1f; public bool Load; }
        float idleSound;   // (0 : enregistrement de la VAZ, regime remappe comme avant)
        readonly List<Loop> loops = new List<Loop>();
        readonly List<AudioSource> muted = new List<AudioSource>();
        readonly Dictionary<string, AudioClip> starter = new Dictionary<string, AudioClip>();
        static readonly Dictionary<string, AudioClip> clipCache = new Dictionary<string, AudioClip>();
        Drivetrain dt;
        PlayMakerFSM starterFsm;
        AudioSource crank, oneShot, starterMuted, startSrc;
        bool wasCranking;
        string starterState = "";
        float load, catchTime = -100f;

        // at : point du moteur (enfant de la carrosserie). Sons de MWCoop/<dossier du modele>/sons s'il y en a, sinon ceux de la Rivett rallye.
        public static VazSound Create(GameObject car, Transform at, VazCar.Model m)
        {
            var s = new VazSound { dir = Path.Combine(m.Dir, "sons") };
            if (s.HasFiles) s.Setup(car, at);
            if (s.loops.Count == 0) s.Rivett(car, at);
            return s.loops.Count > 0 || s.rivett ? s : null;
        }

        bool rivett;
        // Regime des automates Low/Med/High/Valves de la copie (CurrentRPM) : sur la CORRIS, son automate Throttle le prend
        // dans la variable GLOBALE « RPM » (celle de la Rivett) -- sur la VAZ elle restait a 1 (Rivett arretee) : hauteur
        // 1/600, son muet (JD, 18/10 : « elles ont pas de son moteur »). Ces lectures sont coupees, le regime de la VAZ
        // (Drivetrain, pose par VehicleSync chez les autres) donne ici a chaque image.
        readonly List<FsmFloat> rpmVars = new List<FsmFloat>();

        void Rivett(GameObject car, Transform at)
        {
            dt = car.GetComponent<Drivetrain>();
            GameObject tune = Game.FindAny("CORRIS/Simulation/ExhaustCorris/ExhaustSmoke/AudioEngineCorrisTune");
            GameObject valve = Game.FindAny("CORRIS/Simulation/ExhaustCorris/EngineValveSound");
            GameObject corris = Game.FindAny("CORRIS");
            if (tune == null || corris == null) { Log.Warn("vaz : sons de la Rivett (AudioEngineCorrisTune) introuvables : son de la SORBET garde"); return; }
            GameObject cp = Object.Instantiate(tune);
            cp.name = "AudioEngineCorrisTune";
            cp.transform.SetParent(at, false);
            cp.transform.localPosition = Vector3.zero; cp.transform.localRotation = Quaternion.identity;
            GameObject vcp = null;
            if (valve != null)
            {
                vcp = Object.Instantiate(valve);
                vcp.name = "EngineValveSound";
                vcp.transform.SetParent(cp.transform, false);
                vcp.transform.localPosition = new Vector3(0f, 0.15f, 0.1f);   // (soupapes : au-dessus du bloc)
            }
            foreach (Transform t in cp.GetComponentsInChildren<Transform>(true)) if (t.name != "Sound") t.gameObject.SetActive(true);
            int rebound = 0, cut = 0;
            foreach (PlayMakerFSM f in cp.GetComponentsInChildren<PlayMakerFSM>(true))
            {
                try
                {
                    f.Fsm.InitData();
                    foreach (FsmObject ov in f.FsmVariables.ObjectVariables)
                    {
                        Object v = ov.Value;
                        if (v is Drivetrain) { ov.Value = dt; rebound++; }
                        else if (v is Component && ((Component)v).transform.IsChildOf(tune.transform))
                        {
                            Transform m = Twin(tune.transform, cp.transform, ((Component)v).transform);
                            if (m != null) { ov.Value = m.GetComponent(v.GetType()); rebound++; }
                        }
                    }
                    foreach (FsmGameObject gv in f.FsmVariables.GameObjectVariables)
                    {
                        GameObject v = gv.Value;
                        if (v == null || !v.transform.IsChildOf(corris.transform)) continue;
                        if (v.transform.IsChildOf(tune.transform)) { Transform m = Twin(tune.transform, cp.transform, v.transform); gv.Value = m != null ? m.gameObject : null; }
                        else if (valve != null && v == valve) gv.Value = vcp;
                        else gv.Value = null;   // (Exhaust : diviseur de volume ; SimCombustion : cylindres de la CORRIS)
                        rebound++;
                    }
                    if (f.FsmName == "Throttle")
                        foreach (FsmState st in f.FsmStates)
                            foreach (FsmStateAction ac in st.Actions)
                            {
                                if (ac.GetType().Name != "SetFsmFloat") continue;
                                System.Reflection.FieldInfo sv = ac.GetType().GetField("setValue"), vn = ac.GetType().GetField("variableName");
                                FsmFloat v = sv != null ? sv.GetValue(ac) as FsmFloat : null;
                                FsmString n = vn != null ? vn.GetValue(ac) as FsmString : null;
                                if (v != null && v.UseVariable && v.Name == "RPM" && n != null && n.Value == "CurrentRPM") { ac.Enabled = false; cut++; }
                            }
                    if (f.FsmName == "Low" || f.FsmName == "Med" || f.FsmName == "High" || f.FsmName == "Valves")
                    { FsmFloat cr = f.FsmVariables.FindFsmFloat("CurrentRPM"); if (cr != null) rpmVars.Add(cr); }
                    FsmFloat div = f.FsmVariables.FindFsmFloat("VolDivider");
                    if (div != null) div.Value = 2f;   // (echappement de course : collecteur long, sortie laterale)
                    f.enabled = true;
                }
                catch (System.Exception e) { Log.Warn("vaz : son de la Rivett, automate " + f.FsmName + " : " + e.Message); }
            }
            // sons moteur de la SORBET coupes
            Transform ae = Find(car.transform, "AudioEngine");
            if (ae != null) foreach (AudioSource a in ae.GetComponentsInChildren<AudioSource>(true)) muted.Add(a);
            Transform far = Find(car.transform, "sound_exhaust");
            if (far != null && far.GetComponent<AudioSource>() != null) muted.Add(far.GetComponent<AudioSource>());
            rivett = true;
            Log.Info("vaz : son moteur de la Rivett rallye (AudioEngineCorrisTune" + (vcp != null ? " + soupapes" : "") + ") : " + cp.GetComponentsInChildren<AudioSource>(true).Length + " sources, " + rebound + " references ramenees a la VAZ, " + muted.Count + " sons de la SORBET coupes, regime de la VAZ donne a " + rpmVars.Count + " automates (" + cut + " lectures du regime de la Rivett coupees)");
        }

        // Objet de la copie qui correspond a 'o' (meme chemin sous la racine copiee).
        static Transform Twin(Transform src, Transform copy, Transform o)
        {
            if (o == src) return copy;
            var names = new List<string>();
            for (Transform t = o; t != null && t != src; t = t.parent) names.Insert(0, t.name);
            Transform r = copy;
            foreach (string n in names) { r = r.Find(n); if (r == null) return null; }
            return r;
        }

        void Setup(GameObject car, Transform at)
        {
            dt = car.GetComponent<Drivetrain>();
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            foreach (string l in File.ReadAllLines(Path.Combine(dir, "sons.txt")))
            {
                string[] p = l.Split(new[] { ' ', '\t' }, System.StringSplitOptions.RemoveEmptyEntries);
                if (p.Length == 2 && p[0] == "ralenti") { float.TryParse(p[1], System.Globalization.NumberStyles.Float, ci, out idleSound); continue; }
                if (p.Length < 3) continue;
                AudioClip clip = Wav(Path.Combine(dir, p[0]));
                if (clip == null) { Log.Warn("vaz : son " + p[0] + " illisible"); continue; }
                if (p[2] != "charge" && p[2] != "frein") { starter[p[2]] = clip; continue; }
                float rpm;
                if (!float.TryParse(p[1], System.Globalization.NumberStyles.Float, ci, out rpm) || rpm <= 0f) continue;
                var go = new GameObject("VAZ_son_" + p[0]);
                go.transform.SetParent(at, false);
                var a = go.AddComponent<AudioSource>();
                a.clip = clip; a.loop = true; a.volume = 0f; a.spatialBlend = 1f; a.minDistance = 1f; a.maxDistance = 150f; a.dopplerLevel = 0.3f; a.playOnAwake = false;
                var lp = new Loop { Src = a, Rpm = rpm, Load = p[2] == "charge" };
                if (p.Length >= 5) { float.TryParse(p[3], System.Globalization.NumberStyles.Float, ci, out lp.Min); float.TryParse(p[4], System.Globalization.NumberStyles.Float, ci, out lp.Max); }
                if (p.Length >= 6) float.TryParse(p[5], System.Globalization.NumberStyles.Float, ci, out lp.Vol);
                loops.Add(lp);
            }
            loops.Sort((x, y) => x.Max >= 0f ? x.Min.CompareTo(y.Min) : x.Rpm.CompareTo(y.Rpm));
            float vmax = 0f; foreach (Loop x in loops) vmax = Mathf.Max(vmax, x.Vol);
            if (vmax > 0f) foreach (Loop x in loops) x.Vol /= vmax;
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
                    // (sons du jeu coupes seulement s'ils sont remplaces : la cle -- JD 21/10, addons : « pour le contact le son
                    // d'origine de la cle du jeu » -- et la prise du moteur)
                    bool keyOwn = starter.ContainsKey("cle"), catchOwn = starter.ContainsKey("prise") || starter.ContainsKey("demarrage");
                    foreach (FsmState state in starterFsm.FsmStates)
                        if ((state.Name == "Turn key" && keyOwn) || (state.Name == "Start engine" && catchOwn))
                            foreach (FsmStateAction ac in state.Actions)
                                if (ac.GetType().Name == "MasterAudioPlaySound") ac.Enabled = false;
                    Transform ss = Find(car.transform, "StarterSound");
                    if (ss != null) starterMuted = ss.GetComponent<AudioSource>();
                    var go = new GameObject("VAZ_demarreur");
                    go.transform.SetParent(at, false);
                    crank = go.AddComponent<AudioSource>(); oneShot = go.AddComponent<AudioSource>(); startSrc = go.AddComponent<AudioSource>();
                    if (starter.ContainsKey("demarrage")) startSrc.clip = starter["demarrage"];
                    foreach (AudioSource a in new[] { crank, oneShot, startSrc }) { a.spatialBlend = 1f; a.minDistance = 1f; a.maxDistance = 60f; a.playOnAwake = false; a.dopplerLevel = 0f; }
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
            if (rivett) { float r = dt.rpm; foreach (FsmFloat v in rpmVars) v.Value = r; return; }   // (automates du jeu, au regime de la VAZ)
            float rpm = dt.rpm, thr = Mathf.Clamp01(dt.throttle);
            load = Mathf.MoveTowards(load, thr, Time.deltaTime * 6f);
            bool on = rpm > 300f;
            Starter();
            float duck = Mathf.Clamp01((Time.time - catchTime - 0.7f) / 0.9f);
            // (enregistrement de la 205 : ralenti vers 1 850 tr/min, le moteur du jeu vers 900 -> sous 2 556 tr/min le regime
            // entendu est 0,55 x regime + 1 150)
            if (idleSound > 0f) { if (rpm < 3000f) rpm = idleSound + (rpm - 900f) * (3000f - idleSound) / 2100f; }
            else rpm = rpm < 2556f ? 0.55f * rpm + 1150f : rpm;
            float offW = (1f - load) * Mathf.Clamp01((rpm - 2000f) / 800f);
            Mix(true, rpm, on ? duck * 0.9f * (1f - 0.55f * offW) * (0.55f + 0.45f * load + 0.45f * (1f - Mathf.Clamp01((rpm - 1500f) / 1500f))) : 0f);
            Mix(false, rpm, on ? duck * 0.75f * offW : 0f);
        }

        // ([Test] VazSon) etat des boucles
        public string Describe()
        {
            var sb = new System.Text.StringBuilder(rivett ? "Rivett" : loops.Count + " boucles, regime " + (dt != null ? dt.rpm.ToString("F0") : "?") + " :");
            foreach (Loop l in loops) if (l.Src != null && l.Src.volume > 0.001f) sb.Append(' ').Append(l.Src.clip != null ? l.Src.clip.name : "?").Append(l.Load ? "(c)" : "(f)").Append(" v ").Append(l.Src.volume.ToString("F2")).Append(" h ").Append(l.Src.pitch.ToString("F2")).Append(l.Src.isPlaying ? "" : " ARRET");
            return sb.ToString();
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
            // demarrage complet enregistre (rFactor : lancement + prise, « demarrage ») : depuis le debut a chaque appui sur le
            // demarreur ; coupe si on lache avant que le moteur prenne, sinon joue jusqu'au bout
            if (startSrc != null && startSrc.clip != null)
            {
                if (cranking && !wasCranking) { startSrc.Stop(); startSrc.volume = 1f; startSrc.Play(); }
                else if (!cranking && wasCranking && s != "Start engine" && s != "Running" && startSrc.isPlaying) startSrc.Stop();
                // moteur pris : l'enregistrement s'eteint en 0,4 s, 0,6 s apres la prise (Sport Quattro, JD 21/10 : 12 s de
                // demarrage entendues par-dessus le moteur jusqu'aux premiers metres)
                if (startSrc.isPlaying && !cranking && Time.time - catchTime > 0.6f && Time.time - catchTime < 30f)
                {
                    startSrc.volume = Mathf.MoveTowards(startSrc.volume, 0f, Time.deltaTime / 0.4f);
                    if (startSrc.volume <= 0f) startSrc.Stop();
                }
            }
            wasCranking = cranking;
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
            if (set[0].Max >= 0f) { MixRanges(set, rpm, vol); return; }
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

        // Plages rFactor : poids 1 dans [mini, maxi], fondu lineaire de 150 a 600 tr/min aux bords (la largeur du chevauchement
        // avec les autres plages), sauf sous la plage la plus basse et au-dessus de la plus haute (prolongees) ; poids normalises
        // a puissance constante ; hauteur = regime / regime naturel. (Les plages se chevauchent dans tous les sens -- Evo III :
        // 3 000-7 550 et 3 450-3 530 -- d'ou un fondu par bord et non entre voisines.)
        static void MixRanges(List<Loop> set, float rpm, float vol)
        {
            var w = new float[set.Count]; float sum = 0f;
            int lo = 0, hi = 0;
            for (int i = 1; i < set.Count; i++) { if (set[i].Min < set[lo].Min) lo = i; if (set[i].Max > set[hi].Max) hi = i; }
            for (int i = 0; i < set.Count; i++)
            {
                Loop L = set[i];
                float fin = 150f, fout = 150f;
                foreach (Loop o in set)
                {
                    if (o == L) continue;
                    if (o.Min < L.Min && o.Max > L.Min) fin = Mathf.Max(fin, Mathf.Min(o.Max - L.Min, 600f));
                    if (o.Max > L.Max && o.Min < L.Max) fout = Mathf.Max(fout, Mathf.Min(L.Max - o.Min, 600f));
                }
                fin = Mathf.Min(fin, (L.Max - L.Min) * 0.5f); fout = Mathf.Min(fout, (L.Max - L.Min) * 0.5f);
                float a = i == lo ? 1f : Mathf.Clamp01((rpm - L.Min) / Mathf.Max(1f, fin)), b = i == hi ? 1f : Mathf.Clamp01((L.Max - rpm) / Mathf.Max(1f, fout));
                w[i] = a * b; sum += w[i] * w[i];
            }
            float k = sum > 1e-6f ? 1f / Mathf.Sqrt(sum) : 0f;
            for (int i = 0; i < set.Count; i++)
            {
                Loop L = set[i];
                L.Src.volume = vol * w[i] * k * L.Vol;
                L.Src.pitch = Mathf.Clamp(rpm / L.Rpm, 0.2f, 2.5f);
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
