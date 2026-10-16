using System.Collections;
using System.Collections.Generic;
using HutongGames.PlayMaker;
using MWCoop.Net;
using UnityEngine;

namespace MWCoop
{
    // Courses (racine RACES : rallye et course sur glace). Dans le jeu chaque joueur faisait tourner sa propre course :
    // voitures IA lancees au hasard, temps des adversaires tires au hasard, autre classement chez chacun.
    //
    // Voitures IA (tout corps a CarDynamics sous RACES : RALLYCAR1-3 du rallye, voitures des equipes de la glace) :
    //  - l'hote les fait rouler (logique du jeu intacte) et envoie 15 fois/s celles qui sont allumees : pose, vitesse,
    //    braquage, regime et vitesse affichee (Steering.RPM, Throttle.SpeedCurrent : la tele du rallye les montre),
    //    livree (SkinChange : matiere de la carrosserie, 3 pilotes par voiture), feux stop, son du moteur ;
    //  - chez l'invite : copies cinematiques sorties de leur parent (leur conduite, Navigation, Disable, Timing... coupes,
    //    'LOD', 'SkinChange', feux et pots gardes), roues qui tournent, son du moteur au regime de l'hote. Une copie qui
    //    renverse le joueur d'ici le tue comme le ferait la voiture du jeu (Throttle 'Die 3' : la copie cinematique ne
    //    recoit pas la collision du joueur).
    //  - chez l'hote, les distances au joueur de leur logique (attendre qu'il soit loin avant de placer une voiture au
    //    depart, de la retirer, de la remettre sur ses roues) comptent TOUS les joueurs : la voiture ne se pose plus sur
    //    un invite.
    // Rallye : 'AIdrivers' (choix de la voiture et du pilote) et 'GenerateTimes' (temps de depart tires au hasard) ne
    // tournent que chez l'hote ; sa table des resultats (RallyCars, hashtable Results : pilote -> temps) est recopiee
    // chez les invites, dont 'Leaderboard' refait le meme classement (tele, tableau).
    // Pilotes humains : Rally (rallye) et IceRace (course sur glace, relecture du 15/10, ecart n1), appeles d'ici (meme
    // boucle, memes messages Msg.Race : rangs 10-14 Rally, 20+ IceRace).
    public static class Races
    {
        class Car
        {
            public int Idx;
            public string Key;
            public Transform T;
            public Rigidbody Body;
            public PlayMakerFSM Skin;
            public FsmMaterial[] Skins;
            public Renderer BodyR;
            public FsmFloat Speed, Rpm;
            public GameObject Brake, Engine;
            public AudioSource EngineA;
            public Wheel[] Wheels;
            public float[] WheelRot;
            public bool Muted, WasOn, Known;
            // recu (invite)
            public bool On;
            public Vector3 Pos, Vel;
            public Quaternion Rot;
            public float Steer, SpeedV, RpmV, Pitch, Vol, LastRecv;
            public int SkinIdx, Flags;
        }

        const int F_ON = 1, F_BRAKE = 2, F_ENGINE = 4;
        const int K_CARS = 1, K_RESULTS = 2, K_HASH = 3, K_HASHREPLY = 4, K_ROOTS = 5, K_HELPERS = 6;
        // Course en place cette semaine (RACES 'SwapDates' : semaine paire le rallye, impaire la glace), d'apres l'hote :
        // chez l'invite le choix n'etait refait qu'a son reveil (WAKEUP) -- voitures IA de la glace roulant sur une piste eteinte.
        static readonly string[] Roots = { "RACES/RALLY", "RACES/ICERACE" };
        static float nextRoots;
        static readonly List<Car> cars = new List<Car>();
        static bool built;
        static float buildAt = -1, nextSend, nextOffSend, nextResults, nextLog, nextHashAsk;
        static uint listHash;
        static bool hashOk;                    // invite : meme liste que l'hote
        static readonly List<PlayMakerFSM> mutedLogic = new List<PlayMakerFSM>();
        static Hashtable results;              // RallyCars : Results
        static string resultsSig = "";
        static float deathCheck;

        // Automates des voitures IA gardes chez l'invite (visuels) ; tous les autres sont coupes.
        static readonly HashSet<string> KeepFsms = new HashSet<string> { "LOD", "Lights Switch", "Backfires", "Wait", "CalculateAngle", "FSM" };   // ('SkinChange' : livree posee par ApplySkin)
        // Automates de conteneurs du rallye coupes chez l'invite (logique de l'hote).
        static readonly string[] HostOnlyLogic = { "RACES/RALLY/RallyCars::AIdrivers", "RACES/RALLY/RallyCars::GenerateTimes" };

        public static void OnLevelLoaded()
        {
            cars.Clear(); mutedLogic.Clear(); built = false; hashOk = false; results = null; resultsSig = ""; pendingResults = null; nextResultsLook = 0;
            distHooked = false;
            helpers = null; helpersHooked = false; helpersCar = null; nextHelpersLook = 0; gHelpIdx = -1; gReplayed = -1; gEnd = false; retStep = 0; retLog = 0; retWarn = 0; retCar = null;
            buildAt = PlayerSync.InGame ? Time.realtimeSinceStartup + 7f : -1;
            IceRace.OnLevelLoaded();
        }

        static void Build()
        {
            built = true;
            GameObject root = Game.FindAny("RACES");
            if (root == null) { Log.Warn("courses : racine RACES introuvable"); return; }
            var ranks = new Dictionary<string, int>();
            uint h = 2166136261;
            foreach (Rigidbody rb in root.GetComponentsInChildren<Rigidbody>(true))
            {
                if (rb.GetComponent("CarDynamics") == null) continue;
                string path = Recon.Path(rb.transform);
                int k; ranks.TryGetValue(path, out k); ranks[path] = k + 1;
                var c = new Car { Idx = cars.Count, Key = path + "#" + k, T = rb.transform, Body = rb };
                c.Skin = Game.FsmOn(rb.gameObject, "SkinChange");
                if (c.Skin != null)
                {
                    FsmVariables v = c.Skin.FsmVariables;
                    c.Skins = new[] { v.FindFsmMaterial("Skin1"), v.FindFsmMaterial("Skin2"), v.FindFsmMaterial("Skin3") };
                    FsmGameObject body = v.FindFsmGameObject("CarBody");
                    if (body != null && body.Value != null) c.BodyR = body.Value.GetComponent<Renderer>();
                }
                PlayMakerFSM thr = Game.FsmOn(rb.gameObject, "Throttle"), st = Game.FsmOn(rb.gameObject, "Steering");
                c.Speed = thr != null ? thr.FsmVariables.FindFsmFloat("SpeedCurrent") : null;
                c.Rpm = st != null ? st.FsmVariables.FindFsmFloat("RPM") : null;
                Transform bl = rb.transform.Find("LOD/CarLightsAIRally/BrakeLights");
                c.Brake = bl != null ? bl.gameObject : null;
                Transform es = rb.transform.Find("Sounds/EngineSound");
                c.Engine = es != null ? es.gameObject : null;
                c.EngineA = es != null ? es.GetComponent<AudioSource>() : null;
                c.Wheels = rb.GetComponentsInChildren<Wheel>(true);
                c.WheelRot = new float[c.Wheels.Length];
                cars.Add(c);
                foreach (char ch in c.Key) { h ^= ch; h *= 16777619; }
            }
            listHash = h;
            Log.Info("courses : " + cars.Count + " voitures IA suivies (empreinte " + h.ToString("x8") + ")");
        }

        // Table des resultats du rallye (RallyCars, hashtable Results) : creee par le proxy a son reveil (RallyCars eteint
        // au chargement), cherchee jusqu'a la trouver.
        static float nextResultsLook;
        static void FindResults()
        {
            if (results != null || Time.realtimeSinceStartup < nextResultsLook) return;
            nextResultsLook = Time.realtimeSinceStartup + 2f;
            GameObject rc = Game.FindAny("RACES/RALLY/RallyCars");
            if (rc == null) return;
            foreach (PlayMakerHashTableProxy p in rc.GetComponents<PlayMakerHashTableProxy>())
                if (p.referenceName == "Results" && p.hashTable != null) results = p.hashTable;
            if (results != null) { Log.Info("courses : table des resultats du rallye trouvee (" + results.Count + " pilotes)"); ApplyResults(); }
        }

        static float nextIceError;
        public static void Update()
        {
            if (!Session.Active || !PlayerSync.InGame) return;
            float now = Time.realtimeSinceStartup;
            // Pilotes humains de la glace : a part, une erreur la-dedans ne doit pas arreter les voitures IA (1 trace / 10 s).
            try { IceRace.Update(); }
            catch (System.Exception e) { if (now >= nextIceError) { nextIceError = now + 10f; Log.Error("glace : " + e); } }
            if (!built) { if (buildAt > 0 && now >= buildAt) Build(); return; }
            FindResults();
            if (Session.IsHost)
            {
                HookDistances();
                HelpersHost(now);
                if (Session.RemoteCount == 0) return;
                if (now >= nextSend) { nextSend = now + 1f / 15f; SendCars(now >= nextOffSend); if (now >= nextOffSend) nextOffSend = now + 1f; }
                if (now >= nextResults) { nextResults = now + 1f; SendResults(false); }
                if (now >= nextRoots)
                {
                    nextRoots = now + 2f;
                    int m = 0;
                    for (int i = 0; i < Roots.Length; i++) { GameObject g = Game.FindAny(Roots[i]); if (g != null && g.activeSelf) m |= 1 << i; }
                    Session.Broadcast(new NetWriter(Msg.Race).U8(K_ROOTS).U8(m), false);
                }
                return;
            }
            if (!hashOk && now >= nextHashAsk)
            {
                nextHashAsk = now + 3f;
                Session.SendToHost(new NetWriter(Msg.Race).U8(K_HASH).I32((int)listHash).U8(cars.Count), true);
            }
            foreach (Car c in cars)
            {
                if (c.T == null || !c.Known) continue;
                // Eteinte chez l'hote : la logique d'ici (grille de la glace...) ne l'allume pas.
                if (!c.On) { if (c.T.gameObject.activeSelf) c.T.gameObject.SetActive(false); continue; }
                if (c.T.parent != null && !c.T.gameObject.activeInHierarchy) c.T.parent = null;   // rangee ici sous un parent eteint
                if (c.LastRecv > 0) Follow(c);
            }
            if (now >= deathCheck) { deathCheck = now + 0.05f; RunOver(); }
            HelpersGuest(now);
            if (now >= nextLog)
            {
                nextLog = now + 15f;
                int on = 0; foreach (Car c in cars) if (c.On) on++;
                if (on > 0) Log.Info("courses : " + on + " voitures IA en piste selon l'hote");
            }
        }

        // ---------------------------------------------------------------- hote
        static void SendCars(bool withOff)
        {
            var w = new NetWriter(Msg.Race).U8(K_CARS);
            var list = new List<Car>();
            foreach (Car c in cars)
            {
                if (c.T == null) continue;
                bool on = c.T.gameObject.activeInHierarchy;
                if (on || c.WasOn || withOff) list.Add(c);
                c.WasOn = on;
            }
            if (list.Count == 0) return;
            w.U8(list.Count);
            foreach (Car c in list)
            {
                bool on = c.T.gameObject.activeInHierarchy;
                int flags = (on ? F_ON : 0) | (c.Brake != null && c.Brake.activeInHierarchy ? F_BRAKE : 0) | (c.Engine != null && c.Engine.activeInHierarchy ? F_ENGINE : 0);
                w.U8(c.Idx).U8(flags);
                if (!on) continue;
                float steer = 0f;
                foreach (Wheel wh in c.Wheels) if (wh != null && Mathf.Abs(wh.steering) > Mathf.Abs(steer)) steer = wh.steering;
                w.Vec(c.T.position).Quat(c.T.rotation).Vec(c.Body.velocity).F32(steer)
                 .F32(c.Speed != null ? c.Speed.Value : 0f).F32(c.Rpm != null ? c.Rpm.Value : 0f)
                 .F32(c.EngineA != null ? c.EngineA.pitch : 1f).F32(c.EngineA != null ? c.EngineA.volume : 0f).U8(SkinOf(c));
            }
            Session.Broadcast(w, false);
        }

        // Livree : rang (1-3) de la matiere de la carrosserie parmi Skin1-3 ; 0 : inconnue.
        static int SkinOf(Car c)
        {
            if (c.BodyR == null || c.Skins == null) return 0;
            Material m = c.BodyR.sharedMaterial;
            for (int i = 0; i < c.Skins.Length; i++) if (c.Skins[i] != null && c.Skins[i].Value == m) return i + 1;
            return 0;
        }

        static void SendResults(bool force)
        {
            if (results == null) return;
            var keys = new List<string>();
            foreach (object k in results.Keys) keys.Add(k as string ?? k.ToString());
            keys.Sort(string.CompareOrdinal);
            var sb = new System.Text.StringBuilder();
            foreach (string k in keys) sb.Append(k).Append('=').Append(ToF(results[k]).ToString("R")).Append(';');
            string sig = sb.ToString();
            if (!force && sig == resultsSig) return;
            resultsSig = sig;
            var w = new NetWriter(Msg.Race).U8(K_RESULTS).U8(keys.Count);
            foreach (string k in keys) w.Str(k).F32(ToF(results[k]));
            Session.Broadcast(w, true);
        }

        static float ToF(object o)
        {
            if (o is float) return (float)o;
            if (o is double) return (float)(double)o;
            if (o is int) return (int)o;
            float f; return o != null && float.TryParse(o.ToString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out f) ? f : 0f;
        }

        // ---------------------------------------------------------------- messages
        public static void OnMessage(Peer from, NetReader r)
        {
            int kind = r.U8();
            if (kind >= 20) { IceRace.OnMessage(kind, from, r); return; }   // course sur glace a plusieurs pilotes
            if (kind >= 10) { Rally.OnMessage(kind, from, r); return; }   // rallye a plusieurs pilotes
            if (kind == K_HASH)
            {
                if (!Session.IsHost) return;
                uint h = (uint)r.I32(); int n = r.U8();
                bool same = h == listHash && n == cars.Count;
                Session.T.SendReliable(from, new NetWriter(Msg.Race).U8(K_HASHREPLY).Bool(same).ToArray());
                if (!same) Log.Warn("courses : voitures IA de " + from + " differentes (" + n + ", " + h.ToString("x8") + " ; ici " + cars.Count + ", " + listHash.ToString("x8") + ")");
                resultsSig = "";   // table des resultats renvoyee au prochain passage
                return;
            }
            if (Session.IsHost) return;
            if (kind == K_HASHREPLY) { OnHash(r.Bool()); return; }
            if (kind == K_RESULTS) { OnResults(r); return; }
            if (kind == K_ROOTS)
            {
                int m = r.U8();
                for (int i = 0; i < Roots.Length; i++)
                {
                    GameObject g = Game.FindAny(Roots[i]);
                    bool on = (m & (1 << i)) != 0;
                    if (g == null || g.activeSelf == on) continue;
                    g.SetActive(on);
                    Log.Info("courses : " + Roots[i] + (on ? " allume" : " eteint") + " ici comme chez l'hote");
                }
                return;
            }
            if (kind == K_HELPERS) { OnHelpers(r.U8()); return; }
            if (kind == K_CARS) OnCars(r);
        }

        static void OnCars(NetReader r)
        {
            if (!built || !hashOk) return;
            int n = r.U8();
            float now = Time.realtimeSinceStartup;
            for (int i = 0; i < n; i++)
            {
                int idx = r.U8(), flags = r.U8();
                Car c = idx < cars.Count ? cars[idx] : null;
                bool on = (flags & F_ON) != 0;
                Vector3 p = Vector3.zero, v = Vector3.zero; Quaternion q = Quaternion.identity;
                float steer = 0, spd = 0, rpm = 0, pitch = 1, vol = 0; int skin = 0;
                if (on) { p = r.Vec(); q = r.Quat(); v = r.Vec(); steer = r.F32(); spd = r.F32(); rpm = r.F32(); pitch = r.F32(); vol = r.F32(); skin = r.U8(); }
                if (c == null || c.T == null) continue;
                Mute(c);
                c.On = on; c.Flags = flags; c.Known = true;
                if (c.T.gameObject.activeSelf != on) c.T.gameObject.SetActive(on);
                if (!on) { c.LastRecv = 0; continue; }
                if (c.LastRecv <= 0 || (p - c.T.position).sqrMagnitude > 100f) { c.T.position = p; c.T.rotation = q; }
                c.Pos = p; c.Rot = q; c.Vel = v; c.Steer = steer; c.SpeedV = spd; c.RpmV = rpm; c.Pitch = pitch; c.Vol = vol; c.LastRecv = now;
                if (skin > 0 && skin != c.SkinIdx) ApplySkin(c, skin);
            }
        }

        static void OnResults(NetReader r)
        {
            int n = r.U8();
            var got = new List<KeyValuePair<string, float>>();
            for (int i = 0; i < n; i++) got.Add(new KeyValuePair<string, float>(r.Str(), r.F32()));
            pendingResults = got;
            ApplyResults();
        }

        static List<KeyValuePair<string, float>> pendingResults;
        static void ApplyResults()
        {
            if (results == null || pendingResults == null) return;
            var got = pendingResults; pendingResults = null;
            int n = got.Count;
            results.Clear();
            foreach (var kv in got) results[kv.Key] = kv.Value;
            Log.Info("courses : resultats du rallye de l'hote recopies (" + n + " pilotes)");
        }

        // ---------------------------------------------------------------- invite : copies
        static void Mute(Car c)
        {
            if (c.Muted) return;
            c.Muted = true;
            foreach (MonoBehaviour m in c.T.GetComponents<MonoBehaviour>())
            {
                var f = m as PlayMakerFSM;
                string n = m.GetType().Name;
                if (f != null && KeepFsms.Contains(f.FsmName)) continue;
                if (f != null || n == "MobileCarController" || n == "AxisCarController" || n == "Drivetrain") m.enabled = false;
            }
            foreach (PlayMakerFSM f in c.T.GetComponentsInChildren<PlayMakerFSM>(true))
                if (f.transform != c.T && !KeepFsms.Contains(f.FsmName) && f.FsmName != "Sound") f.enabled = false;   // Navigation, Passing, Brakezones...
            foreach (PlayMakerFSM f in c.T.GetComponentsInChildren<PlayMakerFSM>(true))
                if (f.FsmName == "Sound" && f.transform.parent != null && f.transform.parent.name == "Sounds") f.enabled = false;   // son du moteur : pilote d'ici
            foreach (Wheel wh in c.Wheels) if (wh != null) wh.enabled = false;
            c.Body.isKinematic = true;
            c.Body.interpolation = RigidbodyInterpolation.None;
            // Hors de son parent : chez l'hote elle passe sous NPCStartPosSSx (placement) puis revient sous son conteneur,
            // eteint ici si la course n'y a pas demarre a la meme seconde.
            c.T.parent = null;
            MuteHostLogic();
        }

        static void MuteHostLogic()
        {
            foreach (string s in HostOnlyLogic)
            {
                int i = s.IndexOf("::");
                GameObject go = Game.FindAny(s.Substring(0, i));
                PlayMakerFSM f = go != null ? Game.FsmOn(go, s.Substring(i + 2)) : null;
                if (f == null || mutedLogic.Contains(f)) continue;
                f.enabled = false;
                mutedLogic.Add(f);
                Log.Info("courses : " + s + " coupe ici (logique de l'hote)");
            }
        }

        // Livree posee directement (matiere de la carrosserie et des bavettes, nom du pilote lu dans l'etat "Skin N") :
        // rejouer CHANGE ne suffisait pas -- l'automate, allume a ce moment, passait d'abord par son etat de depart (meme
        // increment) et finissait un pilote plus loin.
        static void ApplySkin(Car c, int skin)
        {
            c.SkinIdx = skin;
            if (c.Skin == null || c.Skins == null || skin < 1 || skin > c.Skins.Length || c.Skins[skin - 1] == null) return;
            Material m = c.Skins[skin - 1].Value;
            FsmVariables v = c.Skin.FsmVariables;
            foreach (string n in new[] { "CarBody", "CarMudflaps" })
            {
                FsmGameObject g = v.FindFsmGameObject(n);
                Renderer r = g != null && g.Value != null ? g.Value.GetComponent<Renderer>() : null;
                if (r != null && m != null) r.sharedMaterial = m;
            }
            string name = null;
            try
            {
                FsmState st = c.Skin.Fsm.GetState("Skin " + skin);
                if (st != null)
                    foreach (FsmStateAction a in st.Actions)
                    {
                        var ss = a as HutongGames.PlayMaker.Actions.SetStringValue;
                        if (ss != null && ss.stringValue != null) { name = ss.stringValue.Value; break; }
                    }
            }
            catch { }
            FsmString dn = v.FindFsmString("DriverName");
            if (dn != null && name != null) dn.Value = name;
            FsmInt id = v.FindFsmInt("DriverID");
            if (id != null) id.Value = skin == 1 ? 0 : skin - 1;
            Log.Info("courses : " + c.T.name + " : pilote " + (name ?? "?") + " (livree " + skin + ")");
        }

        static void Follow(Car c)
        {
            if (!c.T.gameObject.activeInHierarchy) return;
            float dt = Mathf.Min(Time.realtimeSinceStartup - c.LastRecv, 0.25f);
            Vector3 target = c.Pos + c.Vel * dt;
            float k = 1f - Mathf.Exp(-15f * Time.deltaTime);
            c.T.position = Vector3.Lerp(c.T.position, target, k);
            c.T.rotation = Quaternion.Slerp(c.T.rotation, c.Rot, k);
            if (c.Speed != null) c.Speed.Value = c.SpeedV;
            if (c.Rpm != null) c.Rpm.Value = c.RpmV;
            if (c.Brake != null) { bool b = (c.Flags & F_BRAKE) != 0; if (c.Brake.activeSelf != b) c.Brake.SetActive(b); }
            if (c.Engine != null)
            {
                bool e = (c.Flags & F_ENGINE) != 0;
                if (c.Engine.activeSelf != e) c.Engine.SetActive(e);
                if (e && c.EngineA != null) { c.EngineA.pitch = c.Pitch; c.EngineA.volume = c.Vol; if (!c.EngineA.isPlaying && c.EngineA.loop) c.EngineA.Play(); }
            }
            float fwd = Vector3.Dot(c.Vel, c.T.forward);
            for (int i = 0; i < c.Wheels.Length; i++)
            {
                Wheel w = c.Wheels[i];
                if (w == null || w.model == null || w.radius <= 0f) continue;
                c.WheelRot[i] += fwd / w.radius * Time.deltaTime;
                w.model.transform.localRotation = Quaternion.Euler(0f, c.Steer * w.maxSteeringAngle, 0f) * Quaternion.AngleAxis(57.29578f * c.WheelRot[i], Vector3.right);
            }
        }

        // Copie qui renverse le joueur d'ici : comme Throttle 'Die 3' (vitesse relative > 5 m/s au contact).
        static void RunOver()
        {
            GameObject pl = Game.PlayerGo;
            if (pl == null) return;
            Vector3 pp = pl.transform.position + Vector3.up * 0.9f;
            foreach (Car c in cars)
            {
                if (!c.On || c.LastRecv <= 0 || c.Vel.sqrMagnitude < 25f || c.T == null) continue;
                if ((c.T.position - pp).sqrMagnitude > 36f) continue;
                bool hit = false;
                foreach (Collider col in c.T.GetComponentsInChildren<Collider>())
                {
                    if (col.isTrigger || !col.enabled) continue;
                    Bounds b = col.bounds; b.Expand(0.5f);
                    if (b.Contains(pp)) { hit = true; break; }
                }
                if (!hit) continue;
                GameObject death = Game.FindAny("Systems/Death");
                PlayMakerFSM f = death != null ? Game.FsmOn(death, "Activate Dead Body") : null;
                if (death == null || death.activeSelf) return;
                FsmBool ro = f != null ? f.FsmVariables.FindFsmBool("RunOverRally") : null;
                if (ro != null) ro.Value = true;
                death.SetActive(true);
                Log.Info("courses : renverse ici par la copie de " + c.T.name + " (" + c.Vel.magnitude.ToString("F0") + " m/s)");
                return;
            }
        }

        static void OnHash(bool same)
        {
            if (hashOk) return;
            hashOk = true;
            if (!same) Log.Warn("courses : liste des voitures IA differente de celle de l'hote, suivies quand meme par rang");
            else Log.Info("courses : voitures IA identiques a celles de l'hote (" + cars.Count + ")");
        }

        // ---------------------------------------------------------------- essais
        // [Test] Autotest=rallye : l'hote passe au samedi ([Test] RallyeJour, 6) a [Test] RallyeHeure (13) a 20 s (le jeu
        // lance le rallye : RACES/RALLY 'Reset' -> 'Rally time', RallyCars allume, 'AIdrivers' part). Chacun note, aux
        // secondes multiples de 5 de l'horloge du PC (meme instant des deux cotes), les voitures IA allumees (pose,
        // pilote, vitesse) et la table des resultats. Attendu chez l'invite : memes voitures, memes pilotes, a quelques
        // metres pres, et la meme table.
        static int testStep;
        static int testSec = -1;
        public static void Test(string mode, float t)
        {
            IceRace.Test(mode, t);
            bool ice = mode == "glace" || mode == "glace2" || mode == "glace3";   // (glace2/3 : meme semaine, pilotes humains -- IceRace.Test)
            if ((mode != "rallye" && !ice && mode != "rallye3" && mode != "retourne") || !built) return;
            // [Test] Autotest=glace : semaine de la course sur glace (GlobalWeeksPassed impair, RACES 'SwapDates' WAKEUP),
            // vendredi 10 h (essais libres : le jeu lance des voitures IA au hasard sur la piste).
            if (Session.IsHost && testStep == 0 && t > 20f && ice)
            {
                testStep = 1;
                FsmInt wk = FsmVariables.GlobalVariables.FindFsmInt("GlobalWeeksPassed");
                if (wk != null && wk.Value % 2 == 0) wk.Value++;
                FsmInt gd = FsmVariables.GlobalVariables.FindFsmInt("GlobalDay"); if (gd != null) gd.Value = Config.GetInt("Test", "RallyeJour", 5);
                PlayMakerFSM sun = Game.FindFsm("MAP/Sun/PivotSun/SUN", "Color");
                if (sun != null) { sun.FsmVariables.GetFsmInt("Time").Value = Config.GetInt("Test", "RallyeHeure", 10); sun.SendEvent("TIMESKIP"); }
                PlayMakerFSM sw = Game.FindFsm("RACES", "SwapDates");
                if (sw != null) sw.SendEvent("WAKEUP");
                PlayMakerFSM tf = Game.FindFsm("RACES/ICERACE/TrackFunctions", "Data");
                Log.Info("autotest : glace : semaine " + (wk != null ? wk.Value : -1) + ", SwapDates " + (sw != null ? sw.ActiveStateName : "?") + ", piste " + (tf != null ? tf.ActiveStateName : "?"));
            }
            if (Session.IsHost && testStep == 0 && t > 20f)
            {
                testStep = 1;
                int day = Config.GetInt("Test", "RallyeJour", 6), h = Config.GetInt("Test", "RallyeHeure", 13);
                FsmInt gd = FsmVariables.GlobalVariables.FindFsmInt("GlobalDay"); if (gd != null) gd.Value = day;
                PlayMakerFSM sun = Game.FindFsm("MAP/Sun/PivotSun/SUN", "Color");
                if (sun != null) { sun.FsmVariables.GetFsmInt("Time").Value = h; sun.SendEvent("TIMESKIP"); }
                Log.Info("autotest : rallye : jour " + day + ", " + h + " h ici");
            }
            if (mode == "retourne") TestRetourne(t);
            int sec = System.DateTime.Now.Second;
            if (t < 25f || sec % 5 != 0 || sec == testSec) return;
            testSec = sec;
            var sb = new System.Text.StringBuilder("autotest : " + mode + " " + System.DateTime.Now.ToString("mm:ss") + " :");
            if (mode == "glace2" || mode == "glace3") return;   // (releve par IceRace.Test)
            if (mode != "glace")
            {
                PlayMakerFSM reset = Game.FindFsm("RACES/RALLY", "Reset");
                sb.Append(" Reset ").Append(reset != null ? reset.ActiveStateName : "?");
                GameObject rc = Game.FindAny("RACES/RALLY/RallyCars");
                sb.Append(", RallyCars ").Append(rc != null && rc.activeInHierarchy ? "allume" : "eteint");
            }
            else
            {
                PlayMakerFSM tf = Game.FindFsm("RACES/ICERACE/TrackFunctions", "Data");
                sb.Append(" piste ").Append(tf != null ? tf.ActiveStateName : "?");
            }
            foreach (Car c in cars)
            {
                if (c.T == null || !c.T.gameObject.activeInHierarchy) continue;
                FsmString dn = c.Skin != null ? c.Skin.FsmVariables.FindFsmString("DriverName") : null;
                PlayMakerFSM nav = null;
                Transform nt = c.T.Find("Navigation");
                if (nt != null) nav = Game.FsmOn(nt.gameObject, "Navigation");
                sb.Append(" | ").Append(c.T.name).Append(Session.IsHost && nav != null ? "[" + nav.ActiveStateName + "]" : "").Append(' ').Append(c.T.position.ToString("F0")).Append(' ')
                  .Append(dn != null ? dn.Value : "?").Append(" v").Append((Session.IsHost ? c.Body.velocity.magnitude : c.Vel.magnitude).ToString("F0"));
            }
            if (results != null)
            {
                float sum = 0; foreach (object v in results.Values) sum += ToF(v);
                sb.Append(" ; resultats ").Append(results.Count).Append(" somme ").Append(sum.ToString("F1"));
            }
            Log.Info(sb.ToString());
        }

        // ---------------------------------------------------------------- hote : distances a tous les joueurs
        // GetDistance(soi ou voiture -> PLAYER) des automates des voitures IA et des aides (Spectators 'Helpers') : une
        // action ajoutee juste apres y met la distance au joueur le PLUS PROCHE (joueur d'ici et avatars des invites).
        static bool distHooked;

        class MinDist : FsmStateAction
        {
            public HutongGames.PlayMaker.Actions.GetDistance G;
            public bool PlayerIsOwner;   // GetDistance(PLAYER -> cible) ; sinon (objet -> PLAYER)
            public override void OnEnter() { Apply(); if (!G.everyFrame) Finish(); }
            public override void OnUpdate() { Apply(); }
            void Apply()
            {
                if (G.storeResult == null || G.storeResult.IsNone) return;
                GameObject other = PlayerIsOwner ? G.target.Value : Fsm.GetOwnerDefaultTarget(G.gameObject);
                if (other == null) return;
                float d = G.storeResult.Value;
                Vector3 p = other.transform.position;
                foreach (PlayerInfo pi in Session.Players.Values)
                {
                    if (pi.Local || pi.Level != 1) continue;
                    float dd = Vector3.Distance(p, pi.State.Feet);
                    if (dd < d) d = dd;
                }
                G.storeResult.Value = d;
            }
        }

        static void HookDistances()
        {
            if (distHooked || Session.RemoteCount == 0) return;
            distHooked = true;
            int n = 0;
            var fsms = new List<PlayMakerFSM>();
            foreach (Car c in cars) if (c.T != null) fsms.AddRange(c.T.GetComponentsInChildren<PlayMakerFSM>(true));
            GameObject spec = Game.FindAny("RACES/RALLY/Spectators");
            if (spec != null) { PlayMakerFSM h = Game.FsmOn(spec, "Helpers"); if (h != null) fsms.Add(h); }
            foreach (PlayMakerFSM f in fsms)
            {
                try
                {
                    if (f.Fsm.States.Length > 0 && !f.Fsm.States[0].IsInitialized) f.Fsm.InitData();
                    foreach (FsmState st in f.Fsm.States)
                    {
                        var list = new List<FsmStateAction>(st.Actions);
                        bool changed = false;
                        for (int i = 0; i < list.Count; i++)
                        {
                            var g = list[i] as HutongGames.PlayMaker.Actions.GetDistance;
                            if (g == null) continue;
                            bool toPlayer = g.target != null && g.target.Value != null && g.target.Value.name == "PLAYER";
                            GameObject own = g.gameObject != null && g.gameObject.OwnerOption == OwnerDefaultOption.SpecifyGameObject ? g.gameObject.GameObject.Value : null;
                            bool fromPlayer = own != null && own.name == "PLAYER";
                            if (!toPlayer && !fromPlayer) continue;
                            list.Insert(i + 1, new MinDist { G = g, PlayerIsOwner = fromPlayer });
                            i++; n++; changed = true;
                        }
                        if (changed) st.Actions = list.ToArray();
                    }
                }
                catch (System.Exception e) { Log.Warn("courses : distances de " + Recon.Path(f.transform) + "::" + f.FsmName + " pas accrochees (" + e.GetType().Name + ")"); }
            }
            Log.Info("courses : " + n + " mesures de distance au joueur comptent tous les joueurs (voitures IA, aides)");
        }

        // ---------------------------------------------------------------- rallye : spectateurs qui remettent une voiture IA
        // RACES/RALLY/Spectators :: Helpers (jeu) : la voiture IA renversee (son automate 'Disable', etat 'Over turn' : rayon
        // vers le haut qui touche le sol, Hit) pose RolledCar et envoie ROLLED. 'State 1' --ROLLED--> 'Player dis?' / 'TV car?'
        // / '2nd player check' (joueur a plus de 200 m, voiture pas a la tele) -> 'Activate' (enfant Helpers : 5
        // SpectatorHelper poses a la voiture et allumes) -> 'Being rolled' (chaque seconde : RolledCar::Disable.Hit ; faux =
        // sur ses roues) -> 'Player dis? 2' / 'TV car? 2' / '2nd player check 2' -> 'State 1' (Helpers eteint). Rien n'y
        // touche a la carrosserie : la voiture retombe sur ses roues, ou 'Disable' la replace (Navigation FULLRESET) au bout
        // de 60 s, joueurs loin -- chez l'hote, logique intacte, pose recopiee chez l'invite.
        // Chez l'invite, 'Disable' des copies est coupe (Mute) : ROLLED n'y partait jamais, les spectateurs restaient eteints.
        //  - hote : 'Being rolled' et 'Player dis? 2' accroches ; il dit quelle voiture (rang Idx de K_CARS) a la premiere
        //    entree, 255 a la fin, et redit toutes les 3 s tant que ca dure (arrivants). Ses distances comptent deja tous
        //    les joueurs (MinDist) : spectateurs poses quand tout le monde est a plus de 200 m.
        //  - invite : RolledCar = la copie, Disable.Hit de la copie vrai (automate coupe : rien d'autre ne l'ecrit), Helpers
        //    force en 'Activate' (distances et tele deja vues par l'hote) ; la copie suit la pose de l'hote. A la fin, Hit
        //    faux : 'Being rolled' repart seul vers 'Player dis? 2' (spectateurs eteints quand le joueur d'ici est loin).
        //    Un ROLLED d'ici (voiture IA pas encore copiee) est annule : retour en 'State 1'.
        static PlayMakerFSM helpers;
        static bool helpersHooked;
        static GameObject helpersCar;              // hote : voiture aidee en ce moment
        static float nextHelpersLook, nextHelpersSend, nextHelpersTry, nextHelpersWarn, nextGuardLog;
        static int gHelpIdx = -1, gReplayed = -1;  // invite : voiture aidee chez l'hote ; voiture rejouee ici
        static bool gEnd;                          // invite : fin recue, Helpers d'ici pas encore sorti de 'Being rolled'
        static readonly HashSet<string> HelpersBusy = new HashSet<string> { "Player dis?", "TV car?", "2nd player check", "Activate", "Being rolled" };

        class HelpersHook : FsmStateAction
        {
            public bool Start;
            public override void OnEnter()
            {
                try { HostHelpers(Start); }
                catch (System.Exception e) { Replay.HookError(e); }
                Finish();
            }
        }

        static PlayMakerFSM FindHelpers()
        {
            if (helpers == null && Time.realtimeSinceStartup >= nextHelpersLook)
            {
                nextHelpersLook = Time.realtimeSinceStartup + 2f;
                GameObject spec = Game.FindAny("RACES/RALLY/Spectators");
                helpers = spec != null ? Game.FsmOn(spec, "Helpers") : null;
            }
            return helpers;
        }

        // Pret : Spectators allume (jour de rallye) et automate initialise par PlayMaker (accroche apres : rien d'ecrase).
        static bool HelpersReady() { return helpers != null && helpers.gameObject.activeInHierarchy && helpers.Fsm != null && helpers.Fsm.Initialized; }

        static Car CarOf(GameObject go)
        {
            if (go == null) return null;
            foreach (Car c in cars) if (c.T != null && c.T.gameObject == go) return c;
            return null;
        }

        static FsmBool HitOf(Car c)
        {
            PlayMakerFSM d = c != null && c.T != null ? Game.FsmOn(c.T.gameObject, "Disable") : null;
            return d != null ? d.FsmVariables.FindFsmBool("Hit") : null;
        }

        static void HelpersHost(float now)
        {
            if (FindHelpers() == null) return;
            if (!helpersHooked && HelpersReady())
            {
                helpersHooked = true;
                FsmState br = helpers.Fsm.GetState("Being rolled"), back = helpers.Fsm.GetState("Player dis? 2");
                if (br == null || back == null) { Log.Warn("rallye : Spectators::Helpers sans 'Being rolled' / 'Player dis? 2' : spectateurs pas partages"); return; }
                FsmHooks.Prepend(br, new HelpersHook { Start = true });
                FsmHooks.Prepend(back, new HelpersHook { Start = false });
                Log.Info("rallye : Spectators::Helpers accroche (spectateurs partages avec les invites)");
            }
            if (helpersCar != null && Session.RemoteCount > 0 && now >= nextHelpersSend)
            {
                nextHelpersSend = now + 3f;
                Car c = CarOf(helpersCar);
                if (c != null && c.Idx < 255) Session.Broadcast(new NetWriter(Msg.Race).U8(K_HELPERS).U8(c.Idx), false);
            }
        }

        // Hote : Helpers entre en 'Being rolled' (start ; il y repasse chaque seconde tant que la voiture est sur le toit)
        // ou en 'Player dis? 2' (voiture remise).
        static void HostHelpers(bool start)
        {
            if (!Session.IsHost || helpers == null) return;
            if (start)
            {
                FsmGameObject rc = helpers.FsmVariables.FindFsmGameObject("RolledCar");
                GameObject go = rc != null ? rc.Value : null;
                if (go == null || go == helpersCar) return;
                helpersCar = go;
                if (Session.RemoteCount == 0) return;
                Car c = CarOf(go);
                if (c == null || c.Idx >= 255) { Log.Warn("rallye : spectateurs autour de " + go.name + " (pas une voiture IA suivie) : pas partages"); return; }
                Log.Info("rallye : spectateurs remettent " + go.name + " sur ses roues (hote)");
                Session.Broadcast(new NetWriter(Msg.Race).U8(K_HELPERS).U8(c.Idx), true);
                nextHelpersSend = Time.realtimeSinceStartup + 3f;
                return;
            }
            if (helpersCar == null) return;
            string n = helpersCar.name;
            helpersCar = null;
            if (Session.RemoteCount == 0) return;
            Log.Info("rallye : spectateurs : " + n + " de nouveau sur ses roues (hote)");
            Session.Broadcast(new NetWriter(Msg.Race).U8(K_HELPERS).U8(255), true);
        }

        // Invite : voiture aidee chez l'hote (rang de K_CARS ; 255 : plus aucune).
        static void OnHelpers(int idx)
        {
            if (idx == 255)
            {
                gHelpIdx = -1;
                if (gReplayed < 0 || gEnd) return;
                gEnd = true;
                Car c = gReplayed < cars.Count ? cars[gReplayed] : null;
                FsmBool hit = HitOf(c);
                if (hit != null) hit.Value = false;
                Log.Info("rallye : spectateurs : " + (c != null && c.T != null ? c.T.name : "?") + " de nouveau sur ses roues chez l'hote, spectateurs liberes ici");
                return;
            }
            if (idx == gHelpIdx) return;
            gHelpIdx = idx; nextHelpersTry = 0; nextHelpersWarn = 0;
        }

        static void HelpersGuest(float now)
        {
            if (FindHelpers() == null) return;
            if (gHelpIdx >= 0 && (gReplayed != gHelpIdx || gEnd) && now >= nextHelpersTry) { nextHelpersTry = now + 1f; TryReplay(now); }
            if (!HelpersReady()) return;
            string s = helpers.ActiveStateName;
            if (gReplayed >= 0 && gEnd && s != "Activate" && s != "Being rolled") { gReplayed = -1; gEnd = false; }   // episode fini ici
            if (gReplayed >= 0 || !HelpersBusy.Contains(s)) return;
            // ROLLED parti d'ici (voiture IA locale pas encore copiee) : les voitures IA sont celles de l'hote.
            Game.SetState(helpers, "State 1");
            if (now >= nextGuardLog) { nextGuardLog = now + 10f; Log.Info("rallye : spectateurs : ROLLED d'ici ignore ('" + s + "' -> 'State 1', voitures IA de l'hote)"); }
        }

        static void TryReplay(float now)
        {
            Car c = gHelpIdx < cars.Count ? cars[gHelpIdx] : null;
            string why = null;
            if (!hashOk) why = "voitures IA pas encore comparees a celles de l'hote";
            else if (c == null || c.T == null) why = "voiture " + gHelpIdx + " inconnue ici";
            else if (!c.Known || !c.On || !c.T.gameObject.activeInHierarchy) why = c.T.name + " pas en piste ici";
            else if (!HelpersReady()) why = "RACES/RALLY/Spectators eteint ici";
            if (why != null)
            {
                if (now >= nextHelpersWarn) { nextHelpersWarn = now + 10f; Log.Info("rallye : spectateurs de l'hote en attente ici (" + why + ")"); }
                return;
            }
            if (gReplayed >= 0 && gReplayed != gHelpIdx && gReplayed < cars.Count) { FsmBool old = HitOf(cars[gReplayed]); if (old != null) old.Value = false; }
            FsmBool hit = HitOf(c);
            if (hit != null) hit.Value = true;   // ('Disable' de la copie coupe : 'Being rolled' le lit, rien ne le reecrit)
            FsmGameObject rc = helpers.FsmVariables.FindFsmGameObject("RolledCar");
            if (rc != null) rc.Value = c.T.gameObject;
            gReplayed = gHelpIdx; gEnd = false;
            Game.SetState(helpers, "Activate");
            Log.Info("rallye : spectateurs de l'hote pour " + c.T.name + " rejoues ici (Helpers '" + helpers.ActiveStateName + "'" + (hit == null ? ", Disable.Hit absent" : "") + ")");
        }

        // [Test] Autotest=retourne (avec le samedi 13 h de l'essai rallye) : l'hote, des 25 s, retourne une voiture IA du
        // rallye allumee (180 degres autour de son axe avant, 1,5 m plus haut) et fait comme son 'Disable' ('Over turn') :
        // RolledCar + ROLLED a Spectators::Helpers ; 2 s plus tard, si Helpers attend encore (un joueur a moins de 200 m,
        // la voiture a la tele), 'Activate' est force pour l'essai. Chacun note a 30/35/40 s (puis jusqu'a 60 s) l'etat de
        // Helpers, la voiture aidee, son haut (up.y) et son Hit. Attendu chez l'invite : 'Being rolled', meme voiture,
        // enfant Helpers allume a quelques metres d'elle ; "spectateurs de l'hote pour X rejoues ici" dans le journal.
        static int retStep, retLog;
        static float retAt, retWarn;
        static Car retCar;
        static void TestRetourne(float t)
        {
            FindHelpers();
            if (Session.IsHost && retStep == 0 && t > 25f)
            {
                Car pick = null;
                foreach (Car c in cars) if (c.T != null && c.T.gameObject.activeInHierarchy && c.Key.StartsWith("RACES/RALLY/")) { pick = c; break; }
                string why = pick == null ? "pas de voiture IA du rallye allumee" : !HelpersReady() ? "Spectators::Helpers pas pret" : !helpersHooked ? "Helpers pas encore accroche" : null;
                if (why != null)
                {
                    if (t >= retWarn) { retWarn = t + 10f; Log.Info("autotest retourne : hote : en attente (" + why + ")"); }
                    if (t > 120f) { retStep = 9; Log.Warn("autotest retourne : abandon (" + why + ")"); }
                }
                else
                {
                    retStep = 1; retAt = t; retCar = pick;
                    pick.T.position = pick.T.position + Vector3.up * 1.5f;
                    pick.T.rotation = Quaternion.AngleAxis(180f, pick.T.forward) * pick.T.rotation;
                    if (pick.Body != null && !pick.Body.isKinematic) { pick.Body.velocity = Vector3.zero; pick.Body.angularVelocity = Vector3.zero; }
                    FsmBool hit = HitOf(pick);
                    if (hit != null) hit.Value = true;
                    FsmGameObject rc = helpers.FsmVariables.FindFsmGameObject("RolledCar");
                    if (rc != null) rc.Value = pick.T.gameObject;
                    string before = helpers.ActiveStateName;
                    helpers.SendEvent("ROLLED");
                    Log.Info("autotest retourne : hote : " + pick.T.name + " retournee a " + pick.T.position.ToString("F0") + ", ROLLED a Helpers ('" + before + "' -> '" + helpers.ActiveStateName + "')");
                }
            }
            if (Session.IsHost && retStep == 1 && t > retAt + 2f)
            {
                retStep = 2;
                string s = helpers.ActiveStateName;
                if (s == "State 1" || s == "Player dis?" || s == "TV car?" || s == "2nd player check")
                {
                    FsmGameObject rc = helpers.FsmVariables.FindFsmGameObject("RolledCar");
                    if (rc != null && retCar != null && retCar.T != null) rc.Value = retCar.T.gameObject;
                    Game.SetState(helpers, "Activate");
                    Log.Info("autotest retourne : hote : Helpers attendait en '" + s + "' (joueur a moins de 200 m ou voiture a la tele) : 'Activate' force pour l'essai -> '" + helpers.ActiveStateName + "'");
                }
            }
            int k = (int)(t / 5f);
            if (t < 30f || k == retLog || k > 12) return;
            retLog = k;
            var sb = new System.Text.StringBuilder("autotest retourne : " + (Session.IsHost ? "hote" : "invite") + " t=" + (k * 5) + " : Helpers ");
            if (helpers == null) sb.Append("introuvable");
            else
            {
                sb.Append('\'').Append(helpers.ActiveStateName).Append('\'').Append(helpers.gameObject.activeInHierarchy ? "" : " (Spectators eteint)");
                Transform ht = helpers.transform.Find("Helpers");
                bool on = ht != null && ht.gameObject.activeInHierarchy;
                sb.Append(", enfant Helpers ").Append(on ? "allume a " + ht.position.ToString("F0") + " (" + ht.childCount + " spectateurs)" : "eteint");
                FsmGameObject rc = helpers.FsmVariables.FindFsmGameObject("RolledCar");
                GameObject go = rc != null ? rc.Value : null;
                sb.Append(", RolledCar ").Append(go != null ? go.name : "aucune");
                Car c = CarOf(go);
                if (c != null && c.T != null)
                {
                    FsmBool hit = HitOf(c);
                    sb.Append(" a ").Append(c.T.position.ToString("F0")).Append(" haut ").Append(c.T.up.y.ToString("F2")).Append(" Hit ").Append(hit != null ? (hit.Value ? "vrai" : "faux") : "?");
                    if (on) sb.Append(", ecart ").Append(Vector3.Distance(ht.position, c.T.position).ToString("F0")).Append(" m");
                }
            }
            if (Session.IsHost) sb.Append(" ; aidee ").Append(helpersCar != null ? helpersCar.name : "aucune");
            else sb.Append(" ; chez l'hote ").Append(gHelpIdx >= 0 && gHelpIdx < cars.Count && cars[gHelpIdx].T != null ? cars[gHelpIdx].T.name : "aucune")
                   .Append(", rejouee ").Append(gReplayed >= 0 && gReplayed < cars.Count && cars[gReplayed].T != null ? cars[gReplayed].T.name : "non").Append(gEnd ? " (finie)" : "");
            Log.Info(sb.ToString());
        }
    }
}
