using System.Collections;
using System.Collections.Generic;
using HutongGames.PlayMaker;
using MWCoop.Net;
using UnityEngine;

namespace MWCoop
{
    // Course sur glace avec la VAZ-2108 et chronos en direct (demande de JD, 18/10 : feuille d'inscription « HYLATTY » avec
    // la VAZ, les Jokkis de location ne servent plus -- une VAZ remplace une voiture IA --, un tableau des positions et des
    // temps de chacun).
    //  - feuille (Sheets/IceRaceForm/Functions/ButtonConfirm 'Button') : 'State 16' mesure la distance de la CORRIS a
    //    RACES/SceneryRace/RallyTent/DistanceCheck (< 5 m, sinon « la voiture doit etre a l'inspection », refus), puis
    //    'State 2' a 'State 19' relisent l'inspection de la Rivett (arceau, harnais...). Accroche au debut de 'State 16'
    //    (posee une fois l'automate demarre : avant, ses actions seraient rechargees) : la VAZ du joueur d'ici a moins de
    //    10 m du point -> evenement MWCOOP_VAZ -> 'Check money' (VAZ de rallye prete : pas d'inspection ; les 150 mk restent).
    //    LINEUPS 'State 4' qui suit est reduit a PlayerRegistered, comme une location (pas d'autocollants sur la Rivett) ;
    //  - numero : l'invite inscrit avec sa VAZ l'annonce a l'hote (K_VAZ, toutes les 5 s tant qu'il est inscrit) ; l'hote
    //    lui donne le numero d'une voiture IA (AiNumbers, le premier libre) : cette IA ne court pas de la semaine, la VAZ
    //    prend sa place par le chemin des locations (RentalNumber / RentalCar : Cars[n] = la VAZ, grille, tours, arrivee).
    //    Chez un invite, sa VAZ (et celle des autres) court si le numero recu de l'hote (K_GRID) est celui d'une IA.
    //    Remis a zero chaque semaine (inscription du week-end) ; garde par monde dans glace.ini (Vaz:1).
    //  - chronos : chaque voiture de la piste (IA, VAZ, Rivett) est suivie ici aux lignes de la piste (Checkpoint1,
    //    Checkpoint2, ligne d'arrivee, comme TrackRental) : tours, dernier et meilleur tour, ordre de passage ; vendredi le
    //    classement des meilleurs tours (table du jeu : tirage des IA, tours des humains), en manche les positions (arrivees
    //    du jeu d'abord, puis tours, points de passage, instant du dernier passage). Montres par IceLive (au circuit) et
    //    l'onglet Course sur glace du F10 (RacePanel).
    public static partial class IceRace
    {
        const int K_VAZ = 29;   // invite -> hote : semaine -- « inscrit avec ma VAZ » (Races.OnMessage)
        const float TentDist = 10f;   // (le jeu : 5 m pour la Rivett ; 10 m pour la VAZ, JD 18/10 : sa VAZ garee le long de la tente etait refusee)
        static readonly Dictionary<int, int> vazNums = new Dictionary<int, int>();   // hote : joueur -> numero de l'IA remplacee
        static readonly HashSet<int> vazEntries = new HashSet<int>();                  // hote : inscrits avec leur VAZ (semaine)
        static bool vazMine, vazRestored, formHooked, formWaitLogged;
        static float nextVazSend, nextFormLook, nextTentToast;
        static PlayMakerFSM form;
        static FsmEvent vazEvent;

        public static bool VazMine { get { return vazMine; } }

        static void VazReset()
        {
            vazNums.Clear(); vazEntries.Clear();
            vazMine = vazRestored = formHooked = formWaitLogged = nameSet = false; form = null; zoneSet = false;
            nextVazSend = nextFormLook = nextTentToast = 0f; vazTest = 0;
            LiveReset();
        }

        static void VazNewWeek(int w)
        {
            if (vazEntries.Count > 0 || vazNums.Count > 0 || vazMine) Log.Info("glace : semaine " + w + " : inscriptions des VAZ remises a zero");
            vazEntries.Clear(); vazNums.Clear(); vazMine = false;
            live.Clear(); liveKey = "";
        }

        // Numero de course de la VAZ du joueur id (0 : il ne court pas avec une VAZ).
        static int VazNumber(int id)
        {
            if (id < 0 || Vaz.CarOf(id) == null) return 0;
            if (Session.IsHost)
            {
                if (!vazEntries.Contains(id)) return 0;
                int n;
                if (vazNums.TryGetValue(id, out n)) return n;
                var used = new HashSet<int>(vazNums.Values);
                foreach (int x in AiNumbers)
                    if (!used.Contains(x)) { vazNums[id] = x; Log.Info("glace : VAZ de #" + id + " a la place de la voiture IA n " + x); return x; }
                Log.Warn("glace : plus de voiture IA a remplacer pour la VAZ de #" + id);
                return 0;
            }
            if (id == Session.LocalId && !vazMine) return 0;
            int m = 0;
            if (id == Session.LocalId) m = myNum; else numbers.TryGetValue(id, out m);
            return System.Array.IndexOf(AiNumbers, m) >= 0 ? m : 0;
        }

        // Apres LINEUPS 'State 4' (OnRegistered) : inscription faite avec la VAZ -> annoncee.
        static void VazRegistered()
        {
            if (!vazMine) return;
            if (Session.IsHost) vazEntries.Add(Session.LocalId);
            else SendVaz();
            rentalsDirty = true;
            Hud.Toast(Lang.T("Course sur glace : inscrit avec ta VAZ, tu prends la place d'une voiture IA (ton numéro arrive)",
                             "Ice race: registered with your VAZ, you take an AI car's place (your number is coming)"));
            Log.Info("glace : inscrit avec ma VAZ" + (Session.IsHost ? "" : " (annonce a l'hote)"));
        }

        static void SendVaz()
        {
            nextVazSend = Time.realtimeSinceStartup + 5f;
            Session.SendToHost(new NetWriter(Msg.Race).U8(K_VAZ).I32(localWeek), true);
        }

        static void OnVazEntry(Peer from, NetReader r)
        {
            int wk = r.I32();
            if (!Session.IsHost || wk != localWeek) return;
            if (Vaz.CarOf(from.Id) == null) return;   // (sa VAZ pas encore ici : redit dans 5 s)
            if (!vazEntries.Add(from.Id)) return;
            Human h; string name = humans.TryGetValue(from.Id, out h) ? h.Name : "#" + from.Id;
            Log.Info("glace : " + name + " inscrit avec sa VAZ");
            rentalsDirty = true;
        }

        static void VazTick(float now)
        {
            if (!vazRestored)
            {
                vazRestored = true;
                Dictionary<string, string> d = Load();
                string v, wk;
                if (d != null && d.TryGetValue("Vaz", out v) && v == "1" && d.TryGetValue("Week", out wk) && wk == localWeek.ToString() && Registered())
                {
                    vazMine = true;
                    if (Session.IsHost) vazEntries.Add(Session.LocalId);
                    rentalsDirty = true;
                    Log.Info("glace : inscription avec ma VAZ reprise (semaine " + localWeek + ")");
                }
            }
            HookForm(now);
            FormName();
            if (formMuted.Count > 0 && Time.frameCount > formMutedFrame) { foreach (FsmStateAction a in formMuted) a.Enabled = true; formMuted.Clear(); }
            if (vazMine && !Session.IsHost && Registered() && now >= nextVazSend) SendVaz();
            LiveTick();
        }

        // ---------------------------------------------------------------- feuille d'inscription
        class FormHook : ModHook
        {
            public override string Module { get { return "courses"; } }
            public override void OnEnter()
            {
                try
                {
                    if (VazAtTent())
                    {
                        // (la suite de l'etat -- distance de la CORRIS, note « pas de voiture » -- coupee le temps du passage)
                        foreach (FsmStateAction a in State.Actions) if (a != null && a != this && a.Enabled) { a.Enabled = false; formMuted.Add(a); }
                        formMutedFrame = Time.frameCount;
                        vazPassed = true;
                        Fsm.Event(vazEvent);
                    }
                }
                catch (System.Exception e) { Replay.HookError(e); }
                Finish();
            }
        }

        // 'Check money' apres la VAZ : la suite de 'State 16' a quand meme allume la note « la voiture doit etre a l'inspection »
        // (xNoCar) -- eteinte ici.
        static bool vazPassed;
        static readonly List<FsmStateAction> formMuted = new List<FsmStateAction>();
        static int formMutedFrame;
        class PassHook : ModHook
        {
            public override string Module { get { return "courses"; } }
            public override void OnEnter()
            {
                try
                {
                    if (vazPassed)
                    {
                        vazPassed = false;
                        Transform sym = form != null && form.transform.parent != null ? form.transform.parent.Find("Symbols") : null;
                        foreach (string l in new[] { "FI/xNoCar", "EN/xNoCar" }) { Transform x = sym != null ? sym.Find(l) : null; if (x != null) x.gameObject.SetActive(false); }
                    }
                }
                catch (System.Exception e) { Replay.HookError(e); }
                Finish();
            }
        }

        static bool VazAtTent()
        {
            Transform car = Vaz.CarOf(Session.LocalId);
            if (car == null) return false;
            GameObject tent = Game.FindAny("RACES/SceneryRace/RallyTent/DistanceCheck");
            if (tent == null) { Log.Warn("glace : RallyTent/DistanceCheck introuvable : VAZ pas reconnue a l'inscription"); return false; }
            float d = Vector3.Distance(car.position, tent.transform.position);
            if (d >= TentDist && !(Config.Testing && Config.GetInt("Test", "VazPartout", 0) != 0))   // ([Test] VazPartout=1 : distance ignoree)
            {
                Log.Info("glace : feuille d'inscription : ma VAZ est a " + d.ToString("F0") + " m de la tente (il faut moins de " + TentDist + " m)");
                if (Time.realtimeSinceStartup >= nextTentToast)
                {
                    nextTentToast = Time.realtimeSinceStartup + 10f;
                    Hud.Toast(Lang.T("Ta VAZ est à ", "Your VAZ is ") + d.ToString("F0") + Lang.T(" m de la tente d'inspection : gare-la juste à côté (moins de 10 m)",
                                                                                           " m from the inspection tent: park it right next to it (under 10 m)"));
                }
                return false;
            }
            vazMine = true;
            Log.Info("glace : feuille d'inscription : ma VAZ a " + d.ToString("F1") + " m de la tente, inspection de la Rivett sautee");
            return true;
        }

        // Nom du pilote sur la feuille (JD, 18/10 : il restait vide) : la feuille ecrit la globale PlayerName (nom du joueur du
        // jeu, souvent vide) dans Symbols/PlayerName a la confirmation ; tant que la feuille est ouverte, elle vaut le pseudo
        // MWCoop du joueur d'ici, rendue a la fermeture.
        static string nameWas; static bool nameSet;
        static void FormName()
        {
            if (form == null || form.transform.parent == null || form.transform.parent.parent == null) return;
            bool open = form.transform.parent.parent.gameObject.activeInHierarchy;
            FsmString pn = FsmVariables.GlobalVariables.FindFsmString("PlayerName");
            if (pn == null) return;
            string me = Session.Me != null && !string.IsNullOrEmpty(Session.Me.Name) ? Session.Me.Name : null;
            if (open && me != null && !nameSet) { nameWas = pn.Value; nameSet = true; pn.Value = me; Log.Info("glace : feuille d'inscription : nom du pilote " + me); }
            else if (!open && nameSet) { nameSet = false; pn.Value = nameWas ?? ""; }
        }

        static void HookForm(float now)
        {
            if (formHooked) return;
            if (form == null)
            {
                if (now < nextFormLook) return;
                nextFormLook = now + 3f;
                GameObject go = Game.FindAny("Sheets/IceRaceForm/Functions/ButtonConfirm");
                form = go != null ? Game.FsmOn(go, "Button") : null;
                if (form == null)   // (une autre racine « Sheets » -- un mod -- cache celle du jeu : recherche par l'objet)
                    foreach (PlayMakerFSM f in Resources.FindObjectsOfTypeAll<PlayMakerFSM>())
                        if (f.FsmName == "Button" && f.gameObject.name == "ButtonConfirm" && f.transform.parent != null && f.transform.parent.parent != null
                            && f.transform.parent.parent.name == "IceRaceForm" && f.gameObject.hideFlags == HideFlags.None) { form = f; break; }
                if (form == null) return;
                Log.Info("glace : feuille d'inscription trouvee (" + (form.gameObject.activeInHierarchy ? "ouverte" : "rangee") + ", etat '" + form.ActiveStateName + "')");
            }
            if (string.IsNullOrEmpty(form.ActiveStateName))   // (feuille jamais ouverte : automate pas demarre)
            {
                if (form.gameObject.activeInHierarchy && !formWaitLogged) { formWaitLogged = true; Log.Info("glace : feuille d'inscription ouverte, automate pas encore demarre"); }
                return;
            }
            formHooked = true;
            FsmState s = form.Fsm.GetState("State 16"), pay = form.Fsm.GetState("Check money");
            if (s == null || pay == null) { Log.Warn("glace : feuille d'inscription : etats 'State 16' / 'Check money' absents, VAZ pas reconnue"); return; }
            vazEvent = FsmEvent.GetFsmEvent("MWCOOP_VAZ");
            var tr = new FsmTransition { FsmEvent = vazEvent, ToState = "Check money" };
            System.Reflection.PropertyInfo tp = typeof(FsmTransition).GetProperty("ToFsmState");
            if (tp != null && tp.CanWrite) tp.SetValue(tr, pay, null);
            var list = new List<FsmTransition>(s.Transitions ?? new FsmTransition[0]);
            list.Add(tr);
            s.Transitions = list.ToArray();
            FsmHooks.Prepend(s, new FormHook());
            FsmHooks.Prepend(pay, new PassHook());
            Log.Info("glace : feuille d'inscription accrochee (VAZ acceptee a moins de " + TentDist + " m de la tente)");
        }

        // ---------------------------------------------------------------- etat pour l'interface
        public class Entrant { public string Name; public bool Registered, Vaz, Me; public int Number; public float Lap; public string Heat; public int Pos; }

        // Pilotes humains (inscrits d'abord) avec leur numero d'ici.
        public static List<Entrant> Entrants()
        {
            var l = new List<Entrant>();
            if (!enabled) return l;
            foreach (Human h in Ranked())
                l.Add(new Entrant { Name = h.Name, Registered = h.Registered, Me = h.Id == Session.LocalId, Number = h.Registered ? NumOf(h) : 0,
                                    Vaz = h.Registered && RentalNumber(h.Id) > 0 && Vaz.CarOf(h.Id) != null, Lap = h.Lap, Heat = h.Pos > 0 ? HeatName(h.Heat) : null, Pos = h.Pos });
            if (l.Count == 0 && PlayerSync.InGame && lineups != null)
                l.Add(new Entrant { Name = Session.Me != null ? Session.Me.Name : "?", Registered = Registered(), Me = true, Number = Registered() ? (myRental ? myNum : 14) : 0, Vaz = myRental && vazMine });
            return l;
        }

        public static bool Ready { get { return enabled && lineups != null; } }
        public static bool MeRegistered { get { return lineups != null && Registered(); } }
        public static int MyNumber { get { return !MeRegistered ? 0 : myRental ? myNum : 14; } }
        public static bool MeWithVaz { get { return MeRegistered && myRental && vazMine; } }
        public static bool VazPending { get { return MeRegistered && vazMine && !myRental; } }
        public static string HeatNow { get { int h = TrackHeat(); return h >= 0 ? HeatName(h) : null; } }

        // ---------------------------------------------------------------- essai
        // Autotest « glacevaz » (2 joueurs ; semaine de la course posee par Races.Test a 20 s) : chacun prend une VAZ (hote n 3 a 8 s, invite n 5 a 6 s), la gare a 3 m de la
        // tente d'inspection a 34 s (le joueur a cote), ouvre la feuille d'inscription a 38 s et la confirme a 42 s
        // (ButtonConfirm -> 'State 16', comme le clic) ; bilan a 56 s (inscrit, VAZ, numero, location) et a 76 s (numeros
        // de l'hote, Cars[n], chronos). [Test] VazLoin=1 : la VAZ reste a 30 m (attendu : refus du jeu, xNoCar).
        static int vazTest; static bool mondeZero;
        static void TestVaz(float t)
        {
            // [Test] MondeZero=1 : PlayerID pose a 0 a 5 s (nouvelle partie pas encore sauvegardee, comme chez JD)
            if (vazTest == 0 && t > 5f && Config.GetInt("Test", "MondeZero", 0) != 0 && !mondeZero) { mondeZero = true; FsmFloat pid = FsmVariables.GlobalVariables.FindFsmFloat("PlayerID"); if (pid != null) pid.Value = 0f; Log.Info("autotest glacevaz : PlayerID = 0"); }
            if (vazTest == 0 && t > (Session.IsHost ? 8f : 6f)) { vazTest = 1; Log.Info("autotest glacevaz : demande -> " + Vaz.Request(Session.IsHost ? 3 : 5)); }
            // (« Faire venir » : le joueur pose a 2 m de la tente, sa VAZ amenee a cote de lui par l'hote, comme le bouton du F10)
            if (vazTest == 1 && t > 28f)
            {
                vazTest = 10;
                GameObject tent = Game.FindAny("RACES/SceneryRace/RallyTent/DistanceCheck"), pl = GameObject.Find("PLAYER");
                float off = Config.GetInt("Test", "VazLoin", 0) != 0 ? 30f : 2f;
                if (tent != null && pl != null) pl.transform.position = tent.transform.position + (Session.IsHost ? Vector3.right : Vector3.left) * off + Vector3.up * 1f;
                FsmFloat money = FsmVariables.GlobalVariables.FindFsmFloat("PlayerMoney");
                if (money != null && money.Value < 500f) money.Value = 1000f;
                Log.Info("autotest glacevaz : joueur pose a " + off + " m de la tente, semaine " + localWeek);
            }
            if (vazTest == 10 && t > 31f) { vazTest = 2; Vaz.Call(); Log.Info("autotest glacevaz : faire venir"); }
            if (vazTest == 2 && t > 39f)
            {
                vazTest = 3;
                Transform car = Vaz.CarOf(Session.LocalId); GameObject tent = Game.FindAny("RACES/SceneryRace/RallyTent/DistanceCheck");
                GameObject pl = GameObject.Find("PLAYER");
                if (pl != null && tent != null) pl.transform.position = tent.transform.position + Vector3.forward * 2f + Vector3.up * 1f;
                if (car != null && tent != null) Log.Info("autotest glacevaz : VAZ a " + Vector3.Distance(car.position, tent.transform.position).ToString("F1") + " m de la tente apres la sortie");
                PlayMakerFSM reg = Game.FindFsm("RACES/ICERACE/TentContents/Friday/Registration", "Activate");
                if (reg != null && !string.IsNullOrEmpty(reg.ActiveStateName)) { Game.SetState(reg, "Open results"); Log.Info("autotest glacevaz : feuille ouverte par la tente (Registration " + reg.ActiveStateName + ")"); }
                else
                {
                    GameObject f = Game.FindAny("Sheets/IceRaceForm");
                    if (f == null) { Log.Info("autotest glacevaz : Sheets/IceRaceForm introuvable"); return; }
                    f.SetActive(true);
                    Log.Info("autotest glacevaz : feuille ouverte a la main (Registration " + (reg != null ? "'" + reg.ActiveStateName + "'" : "introuvable") + ")");
                }
            }
            if (vazTest == 3 && t > 42f)
            {
                vazTest = 4;
                Log.Info("autotest glacevaz : feuille " + (form != null ? form.ActiveStateName : "?") + ", accrochee " + formHooked);
                if (form != null) Game.SetState(form, "State 16");
                if (Session.IsHost) Autotest.CaptureSoon("glace-feuille", 0.8f);
                Log.Info("autotest glacevaz : confirmer -> " + (form != null ? form.ActiveStateName : "?"));
            }
            if (vazTest == 4 && t > 56f)
            {
                vazTest = 5;
                Log.Info("autotest glacevaz : feuille " + (form != null ? form.ActiveStateName : "?") + ", inscrit " + MeRegistered + ", VAZ " + vazMine + ", numero " + myNum + ", location " + myRental + " (RentalNumber " + RentalNumber(Session.LocalId) + ")");
            }
            if (vazTest == 5 && t > 76f)
            {
                vazTest = 6;
                var sb = new System.Text.StringBuilder();
                foreach (KeyValuePair<int, int> kv in vazNums) sb.Append(" #").Append(kv.Key).Append("->").Append(kv.Value);
                var ns = new System.Text.StringBuilder();
                foreach (KeyValuePair<int, int> kv in numbers) ns.Append(" #").Append(kv.Key).Append('=').Append(kv.Value);
                int mn = MyNumber;
                GameObject slot = carsList != null && mn > 0 && mn < carsList.Count ? carsList[mn] as GameObject : null;
                string title; List<LiveRow> rows = LiveRows(out title);
                var es = new System.Text.StringBuilder();
                foreach (Entrant e in Entrants()) es.Append(' ').Append(e.Name).Append(e.Registered ? "=inscrit" : "=-").Append(e.Vaz ? "/VAZ" : "").Append("/n").Append(e.Number);
                Log.Info("autotest glacevaz : VAZ de l'hote" + sb + " ; numeros" + ns + " ; Cars[" + mn + "] = " + (slot != null ? slot.name : "-") + " ; pilotes" + es + " ; chronos '" + title + "' " + rows.Count + " lignes");
            }
            // captures (hote) : onglet Course sur glace du F10 a 80 s, chronos en direct sur la ligne d'arrivee a 88 s
            if (Session.IsHost && vazTest == 6 && t > 80f) { vazTest = 7; Menu.Show(Menu.TabRace); Autotest.CaptureSoon("glace-f10", 1.5f); }
            if (Session.IsHost && vazTest == 7 && t > 86f)
            {
                vazTest = 8; Menu.Open = false;
                GameObject pl = GameObject.Find("PLAYER");
                if (pl != null && lines != null) pl.transform.position = lines[2].center + Vector3.up * 3f + Vector3.right * 12f;
                Autotest.CaptureSoon("glace-chronos", 3f);
                Log.Info("autotest glacevaz : au bord de la ligne d'arrivee, chronos " + (LiveOn ? "montres" : "caches") + ", essais " + Practice + ", zone " + zone.min.ToString("F0") + " - " + zone.max.ToString("F0"));
            }
            if (Session.IsHost && vazTest == 8 && t > 92f)
            {
                vazTest = 9;
                GameObject pl = GameObject.Find("PLAYER");
                if (pl != null) pl.transform.position = new Vector3(-1282f, 3f, 1078f);   // (la maison : 300 m de la ligne)
            }
            if (Session.IsHost && vazTest == 9 && t > 95f)
            {
                vazTest = 11;
                Log.Info("autotest glacevaz : a la maison, chronos " + (LiveOn ? "MONTRES" : "caches") + ", zone " + zone.min.ToString("F0") + " - " + zone.max.ToString("F0"));
            }
        }

        // ---------------------------------------------------------------- chronos en direct
        class LiveCar
        {
            public string Num, Name; public Transform T;
            public readonly bool[] In = new bool[3];
            public Vector3 Last; public bool Has, C1, C2, Timing;
            public float LapStart, LastLap, BestLap, LastPass; public int Laps; public bool Human;
        }
        public class LiveRow { public int Pos; public string Num, Name, Laps, Last, Best; public bool Me, Human, Done; }

        static readonly Dictionary<string, LiveCar> live = new Dictionary<string, LiveCar>();
        static string liveKey = ""; static float nextLiveScan, liveStateAt; static string liveState = "";

        static void LiveReset() { live.Clear(); liveKey = ""; nextLiveScan = 0f; liveState = ""; }

        static bool Practice { get { return lineTime != null && lineTime.activeInHierarchy; } }
        static bool HeatRunning
        {
            get
            {
                if (lineLaps == null || !lineLaps.activeInHierarchy || track == null) return false;
                string st = track.ActiveStateName;
                return st == "Start race" || st == "Start race 2" || st == "Wait results" || st == "Wait results 2" || st == "State 14" || st == "State 15";
            }
        }
        static bool Gridding { get { string st = track != null ? track.ActiveStateName : ""; return st == "Create grid" || st == "Create grid 2" || st == "Wait command" || st == "Wait command 2"; } }

        // Dans la zone du circuit (JD, 18/10 : 700 m autour de la ligne, c'etait jusqu'a la maison) : boite des lignes de la
        // piste et de la tente, agrandie par les positions des voitures IA en piste (elles font le tour : la boite finit par
        // couvrir le trace), plus 40 m ; avec des essais ou une manche en cours.
        static Bounds zone; static bool zoneSet;
        static void GrowZone(Vector3 p) { if (zoneSet && (p - lines[2].center).sqrMagnitude < 600f * 600f) zone.Encapsulate(p); }
        public static bool LiveOn
        {
            get
            {
                if (!enabled || lines == null || track == null || !PlayerSync.InGame || !zoneSet) return false;
                Transform me = Game.PlayerT;
                if (me == null) return false;
                Vector3 p = me.position, mn = zone.min, mx = zone.max;
                const float m = 40f;
                if (p.x < mn.x - m || p.x > mx.x + m || p.z < mn.z - m || p.z > mx.z + m) return false;
                return Practice || HeatRunning || Gridding;
            }
        }
        public static bool AtTrack { get { return LiveOn; } }

        static Transform CarOfNumber(string local, out string name, out bool human, out bool me)
        {
            name = null; human = me = false;
            int n; if (!int.TryParse(local, out n)) return null;
            int id = IdOfNumber(Canon(local));
            if (local == MyKey() && MeRegistered) { me = human = true; name = Session.Me != null ? Session.Me.Name : "?"; return myRental ? RentalCar(Session.LocalId) : Game.FindAny("CORRIS") != null ? Game.FindAny("CORRIS").transform : null; }
            if (id >= 0)
            {
                Human h; human = true; name = humans.TryGetValue(id, out h) ? h.Name : "#" + id;
                return RentalNumber(id) == LocalNum(n) ? RentalCar(id) : null;   // (Rivett d'un autre : pas suivie ici)
            }
            if (standIns.ContainsKey(n) || carsList == null || n < 0 || n >= carsList.Count) return null;
            var go = carsList[n] as GameObject;
            if (go == null || go.name == "Reserved") return null;
            Transform db = go.transform.Find("DB");
            PlayMakerFSM skin = db != null ? Game.FsmOn(db.gameObject, "SkinChange") : null;
            FsmString dn = skin != null ? skin.FsmVariables.FindFsmString("DriverName") : null;
            name = dn != null && !string.IsNullOrEmpty(dn.Value) ? dn.Value : go.name;
            return go.transform;
        }

        static bool CrossedBox(LiveCar c, int i, Vector3 a, Vector3 b)
        {
            Bounds bx = lines[i];
            bool now = bx.Contains(b), was = c.In[i];
            if (!now && !was)
            {
                Vector3 d = b - a; float len = d.magnitude, dist;
                if (len > 0.001f && bx.IntersectRay(new Ray(a, d / len), out dist) && dist <= len) now = true;
            }
            c.In[i] = bx.Contains(b);
            return now && !was;
        }

        // Chaque image : les voitures de la piste aux lignes. Tours remis a zero a chaque grille (manche) ; le vendredi les
        // meilleurs tours restent ceux de la semaine.
        static void LiveTick()
        {
            if (lines == null || track == null || carsList == null) return;
            if (!zoneSet)
            {
                zone = lines[0]; zone.Encapsulate(lines[1]); zone.Encapsulate(lines[2]);
                GameObject tent = Game.FindAny("RACES/SceneryRace/RallyTent/DistanceCheck");
                if (tent != null) zone.Encapsulate(tent.transform.position);
                zoneSet = true;
            }
            string st = track.ActiveStateName ?? "";
            float now = Time.time;
            if (st != liveState) { liveState = st; liveStateAt = now; if (st == "Start race" || st == "Start race 2") foreach (LiveCar c in live.Values) { c.LapStart = now; c.LastPass = now; } }
            if (Gridding)
            {
                string key = localWeek + "|" + TrackHeat();
                if (key != liveKey) { liveKey = key; foreach (LiveCar c in live.Values) { c.Laps = 0; c.C1 = c.C2 = false; c.LastLap = 0f; c.LastPass = 0f; } }
                foreach (LiveCar c in live.Values) c.Has = false;
            }
            if (now >= nextLiveScan)
            {
                nextLiveScan = now + 2f;
                for (int n = 0; n < carsList.Count; n++)
                {
                    string k = n.ToString(), name; bool human, me;
                    Transform t = CarOfNumber(k, out name, out human, out me);
                    LiveCar c;
                    if (t == null) { live.Remove(k); continue; }
                    if (!live.TryGetValue(k, out c)) { c = new LiveCar { Num = k }; live[k] = c; }
                    if (c.T != t) { c.T = t; c.Has = false; }
                    c.Name = name; c.Human = human;
                }
            }
            if (!Practice && !HeatRunning) return;
            bool practice = Practice;
            foreach (LiveCar c in live.Values)
            {
                if (c.T == null) continue;
                Vector3 p = c.T.position;
                if (!c.Has || (p - c.Last).sqrMagnitude > 2500f) { c.Last = p; c.Has = true; for (int i = 0; i < 3; i++) c.In[i] = lines[i].Contains(p); continue; }
                if (!c.Human && (p - c.Last).sqrMagnitude > 0.01f) GrowZone(p);   // (IA seulement : un joueur qui rentre chez lui n'agrandit pas la zone)
                bool c1 = CrossedBox(c, 0, c.Last, p), c2 = CrossedBox(c, 1, c.Last, p), sf = CrossedBox(c, 2, c.Last, p);
                c.Last = p;
                if (c1 && !c.C1) { c.C1 = true; c.LastPass = now; }
                if (c2 && !c.C2 && c.C1) { c.C2 = true; c.LastPass = now; }
                if (!sf) continue;
                if (practice)
                {
                    if (c.Timing && c.C1 && c.C2) { c.LastLap = now - c.LapStart; if (c.BestLap <= 0f || c.LastLap < c.BestLap) c.BestLap = c.LastLap; }
                    c.Timing = true; c.LapStart = now; c.C1 = c.C2 = false; c.LastPass = now;
                    continue;
                }
                if (c.C1 && c.C2)
                {
                    c.Laps++;
                    if (c.LapStart > 0f) { c.LastLap = now - c.LapStart; if (c.BestLap <= 0f || c.LastLap < c.BestLap) c.BestLap = c.LastLap; }
                }
                c.LapStart = now; c.C1 = c.C2 = false; c.LastPass = now;
            }
        }

        // Tableau : vendredi les meilleurs tours (table du jeu), en manche les positions de la liste de la manche.
        public static List<LiveRow> LiveRows(out string title)
        {
            var rows = new List<LiveRow>();
            title = null;
            if (!enabled || track == null) return rows;
            bool heat = HeatRunning || Gridding;
            if (heat)
            {
                int hs = TrackHeat(), dist = RaceDistance();
                ArrayList list = null;
                if (hs >= 0 && hs < HeatLists.Length) heatLists.TryGetValue(HeatLists[hs], out list);
                var nums = new List<string>();
                if (list != null) foreach (object o in list) if (o != null && o.ToString().Length > 0 && !nums.Contains(o.ToString())) nums.Add(o.ToString());
                var done = new List<string>();
                if (resultsNumbers != null) foreach (object o in resultsNumbers) if (o != null) done.Add(o.ToString());
                foreach (string d in done) if (!nums.Contains(d)) nums.Add(d);
                var cars = new List<LiveCar>();
                foreach (string k in nums) { LiveCar c; if (!live.TryGetValue(k, out c)) { c = new LiveCar { Num = k }; string nm; bool h, m; CarOfNumber(k, out nm, out h, out m); c.Name = nm; } cars.Add(c); }
                cars.Sort(delegate(LiveCar a, LiveCar b)
                {
                    int da = done.IndexOf(a.Num), db = done.IndexOf(b.Num);
                    if (da >= 0 || db >= 0) return da < 0 ? 1 : db < 0 ? -1 : da.CompareTo(db);
                    if (a.Laps != b.Laps) return b.Laps.CompareTo(a.Laps);
                    int sa = (a.C1 ? 1 : 0) + (a.C2 ? 1 : 0), sb = (b.C1 ? 1 : 0) + (b.C2 ? 1 : 0);
                    if (sa != sb) return sb.CompareTo(sa);
                    return a.LastPass.CompareTo(b.LastPass);
                });
                int lead = cars.Count > 0 ? cars[0].Laps : 0;
                for (int i = 0; i < cars.Count; i++)
                {
                    LiveCar c = cars[i];
                    bool fin = done.Contains(c.Num);
                    rows.Add(new LiveRow { Pos = i + 1, Num = c.Num, Name = c.Name ?? "-", Laps = fin ? Lang.T("arrivé", "finished") : Mathf.Min(c.Laps + (HeatRunning ? 1 : 0), dist) + "/" + dist,
                                           Last = LapText(c.LastLap), Best = LapText(c.BestLap), Me = c.Num == MyKey() && MeRegistered, Human = IdOfNumber(Canon(c.Num)) >= 0 || (c.Num == MyKey() && MeRegistered), Done = fin });
                }
                title = Lang.T("Manche ", "Heat ") + HeatName(hs) + (Gridding ? Lang.T(" · sur la grille", " · on the grid") : Lang.T(" · en course", " · racing")) + " · " + dist + Lang.T(" tours", " laps");
                return rows;
            }
            // vendredi : meilleurs tours (table du jeu ; mesure d'ici a defaut)
            var best = new List<KeyValuePair<string, float>>();
            var seen = new HashSet<string>();
            if (lapTimes != null)
                foreach (DictionaryEntry e in lapTimes)
                {
                    string k = e.Key.ToString(); float v = ToF(e.Value);
                    LiveCar c; if (live.TryGetValue(k, out c) && c.BestLap > 0f && (v <= 0f || v >= 900f)) v = c.BestLap;
                    best.Add(new KeyValuePair<string, float>(k, v)); seen.Add(k);
                }
            foreach (LiveCar c in live.Values) if (!seen.Contains(c.Num) && c.BestLap > 0f) best.Add(new KeyValuePair<string, float>(c.Num, c.BestLap));
            best.Sort(delegate(KeyValuePair<string, float> a, KeyValuePair<string, float> b)
            {
                bool na = a.Value <= 0f || a.Value >= 900f, nb = b.Value <= 0f || b.Value >= 900f;
                if (na != nb) return na ? 1 : -1;
                return a.Value.CompareTo(b.Value);
            });
            for (int i = 0; i < best.Count; i++)
            {
                string k = best[i].Key, nm; bool h, m;
                LiveCar c; live.TryGetValue(k, out c);
                CarOfNumber(k, out nm, out h, out m);
                if (nm == null && c != null) nm = c.Name;
                float v = best[i].Value;
                rows.Add(new LiveRow { Pos = i + 1, Num = k, Name = nm ?? "-", Laps = c != null && c.Timing ? Lang.T("en piste", "on track") : "", Last = LapText(c != null ? c.LastLap : 0f),
                                       Best = v > 0f && v < 900f ? LapText(v) : "-", Me = m, Human = h });
            }
            title = Practice ? Lang.T("Essais libres · meilleurs tours (17 premiers en qualifications)", "Free practice · best laps (top 17 go to qualifying)")
                             : Lang.T("Meilleurs tours de la semaine", "Best laps of the week");
            return rows;
        }
    }
}
