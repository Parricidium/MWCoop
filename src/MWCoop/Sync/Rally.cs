using System.Collections;
using System.Collections.Generic;
using HutongGames.PlayMaker;
using MWCoop.Net;
using UnityEngine;

namespace MWCoop
{
    // Rallye a plusieurs pilotes humains (demande de JD : chacun court a son tour, son temps au classement de tous).
    // Dans le jeu, la participation est celle d'UN joueur : RACES/RALLY/ResultsWeekend 'Data' (inscription, temps des
    // speciales, classe Junior/Amateur, tables Names/Times du classement, podium et prix) et 'Penalties'. Chaque joueur
    // garde la sienne (WorldFsms ne la rejoue plus) ; ce module la relie aux autres :
    //  - adversaires communs : le jeu tire 50 adversaires par classe (RACES/RALLY/DriversList) avec des temps au hasard,
    //    differents chez chacun. L'hote donne une graine par semaine de rallye ; chacun en tire les memes temps par
    //    speciale (memes plages que le jeu : SS1 305-450 / 335-480, SS2 95-125 / 105-145, SS3 335-470 / 365-500 s) ;
    //  - pilotes humains : chacun annonce sa classe, ses speciales faites et son temps total (penalites comprises) ;
    //    l'hote tient le tableau et le renvoie a tous ;
    //  - classement : a l'entree de 'Hash to array 2' (classement affiche, et podium apres SS3), les tables Names/Times
    //    d'ici sont refaites : adversaires communs (cumul des speciales que le joueur d'ici a faites), le joueur d'ici
    //    (son pseudo : le nom de la sauvegarde est celui de l'hote), les autres humains de sa classe (cle MWC<id>). Le
    //    podium du jeu (egalite exacte avec les 3 premiers temps) en tient compte : chacun touche son prix chez lui ;
    //  - heures de depart : une seule CORRIS pour tous, l'hote donne a chaque inscrit une heure de SS1 (14-17 h, SS2 =
    //    +3 h) et de SS3 (10-14 h) libres ;
    //  - voitures IA : elles ne partent pas sur un joueur en speciale -- chez l'hote, le chrono de la speciale (Clock
    //    TimeTotalFPS, lu par leur Navigation) et RallyStageOccupied (SS2) prennent celui de l'invite qui court ;
    //  - invite : sa participation est gardee par monde (rallye.ini, comme son porte-monnaie) ; la sauvegarde recue a
    //    chaque connexion contient celle de l'hote, remplacee par la sienne (ou effacee) au chargement.
    public static class Rally
    {
        const int K_HUMAN = 10, K_BOARD = 11, K_SLOTASK = 12, K_SLOT = 13, K_ONSTAGE = 14;

        class Human { public int Id; public string Name = "", Cls = ""; public int Mask; public float Total; public bool Registered; }

        static PlayMakerFSM data, pen;
        static Hashtable names, times;
        static ArrayList playerTimes, startTimes, playerStages;
        static bool hooked, restored;
        static float readyAt = -1, nextReport, nextStore, nextLook, nextStage, nextBoard, lastTotal = -1;
        static string lastReport = "";
        static int boardWeek = -1, seed;
        static readonly Dictionary<int, Human> humans = new Dictionary<int, Human>();
        static readonly Dictionary<string, int[]> slots = new Dictionary<string, int[]>();   // hote : pseudo -> (SS1, SS3)
        static readonly float[] remoteStage = new float[4], remoteStageAt = { -100, -100, -100, -100 };
        static readonly bool[] stageForced = new bool[4];

        static readonly float[,] RangeAm = { { 305, 450 }, { 95, 125 }, { 335, 470 } }, RangeJr = { { 335, 480 }, { 105, 145 }, { 365, 500 } };

        public static void OnLevelLoaded()
        {
            data = pen = null; names = times = null; playerTimes = startTimes = playerStages = null;
            hooked = restored = false; lastReport = ""; lastTotal = -1;
            humans.Clear(); slots.Clear(); boardWeek = -1;
            for (int i = 0; i < 4; i++) { remoteStageAt[i] = -100; stageForced[i] = false; }
            readyAt = PlayerSync.InGame ? Time.realtimeSinceStartup + 10f : -1;
        }

        class Hook : ModHook
        {
            public override string Module { get { return "courses"; } }
            public int Kind;   // 0 classement, 1 resultat a annoncer, 2 inscription
            public override void OnEnter()
            {
                try
                {
                    if (Kind == 2) OnRegistered();
                    else { Report(true); if (Kind == 0) Inject(); }
                }
                catch (System.Exception e) { Replay.HookError(e); }
                Finish();
            }
        }

        // ---------------------------------------------------------------- releve
        static bool Find()
        {
            if (data != null) return true;
            if (Time.realtimeSinceStartup < nextLook) return false;
            nextLook = Time.realtimeSinceStartup + 3f;
            GameObject go = Game.FindAny("RACES/RALLY/ResultsWeekend");
            if (go == null) return false;
            PlayMakerFSM d = Game.FsmOn(go, "Data");
            if (d == null || string.IsNullOrEmpty(d.ActiveStateName)) return false;   // pas encore demarre (RALLY eteint)
            foreach (PlayMakerHashTableProxy p in go.GetComponents<PlayMakerHashTableProxy>())
            {
                if (p.referenceName == "Names") names = p.hashTable;
                if (p.referenceName == "Times") times = p.hashTable;
            }
            foreach (PlayMakerArrayListProxy p in go.GetComponents<PlayMakerArrayListProxy>())
            {
                if (p.referenceName == "PlayerTimes") playerTimes = p.arrayList;
                if (p.referenceName == "StartTimes") startTimes = p.arrayList;
                if (p.referenceName == "PlayerStages") playerStages = p.arrayList;
            }
            if (names == null || times == null || playerTimes == null || startTimes == null || playerStages == null) return false;
            data = d;
            pen = Game.FsmOn(go, "Penalties");
            if (!hooked)
            {
                hooked = true;
                Inject(data, "Hash to array 2", 0);
                Inject(data, "Idle 4", 1);
                Inject(data, "State 1", 2);
            }
            Log.Info("rallye : participation suivie (" + data.ActiveStateName + ", inscrit " + Bool("Registered") + ", classe " + Cls() + ")");
            return true;
        }

        static void Inject(PlayMakerFSM f, string state, int kind)
        {
            FsmState s = f.Fsm.GetState(state);
            if (s == null) { Log.Warn("rallye : etat " + state + " absent"); return; }
            var l = new List<FsmStateAction>(s.Actions);
            l.Insert(0, new Hook { Kind = kind });
            s.Actions = l.ToArray();
        }

        static FsmFloat F(string n) { return data.FsmVariables.FindFsmFloat(n); }
        static FsmInt I(string n) { return data.FsmVariables.FindFsmInt(n); }
        static bool Bool(string n) { FsmBool b = data != null ? data.FsmVariables.FindFsmBool(n) : null; return b != null && b.Value; }
        static void SetBool(string n, bool v) { FsmBool b = data.FsmVariables.FindFsmBool(n); if (b != null) b.Value = v; }
        static string Cls() { FsmInt c = data != null ? I("PlayerClassLevel") : null; return c != null && c.Value >= 10 ? "Amateur" : "Junior"; }

        static int Mask()
        {
            int m = 0;
            for (int i = 0; i < 3 && i < playerStages.Count; i++) if (playerStages[i] is bool && (bool)playerStages[i]) m |= 1 << i;
            return m;
        }

        // ---------------------------------------------------------------- mise a jour
        public static void Update()
        {
            if (!Session.Active || !PlayerSync.InGame || readyAt < 0 || Time.realtimeSinceStartup < readyAt) return;
            if (!Find()) return;
            string wld = World();
            if (wld.Length == 0 || wld == "0") return;   // identifiant de la partie pas encore charge : cles par monde fausses
            float now = Time.realtimeSinceStartup;
            if (!Session.IsHost && !restored)
            {
                // Apres le chargement du jeu (State 2 -> Exists -> Load 2 -> State 17 -> 'Rally unfinised?') : sinon Load 2
                // ecraserait la participation d'ici par celle de la sauvegarde de l'hote.
                string st = data.ActiveStateName;
                if (st == "State 2" || st == "Exists" || st == "Load 2" || st == "State 17" || st == "State 18" || st == "State 19") return;
                restored = true; RestoreGuest();
            }
            if (Session.IsHost)
            {
                HostWeek();
                if (now >= nextBoard) { nextBoard = now + 20f; SendBoard(); }   // (arrivants)
            }
            if (now >= nextReport) { nextReport = now + 5f; Report(false); }
            if (!Session.IsHost && now >= nextStore) { nextStore = now + 10f; StoreGuest(); }
            if (now >= nextStage) { nextStage = now + 1f; StageTick(); }
        }

        // Notre participation, annoncee a l'hote quand elle change (et toutes les 5 s si inscrit : un hote qui revient).
        static void Report(bool force)
        {
            if (data == null) return;
            bool reg = Bool("Registered");
            int mask = Mask();
            FsmFloat tt = F("PlayerTimeTotal");
            float total = tt != null ? tt.Value : 0f;
            string sig = reg + "|" + mask + "|" + total.ToString("R") + "|" + Cls();
            if (!force && sig == lastReport && Time.realtimeSinceStartup % 30f > 5f) return;
            if (!reg && mask == 0 && lastReport.Length == 0) return;
            lastReport = sig;
            var h = new Human { Id = Session.LocalId, Name = Session.Me != null ? Session.Me.Name : "?", Cls = Cls(), Mask = mask, Total = total, Registered = reg };
            if (Session.IsHost) { SetHuman(h); return; }
            Session.SendToHost(new NetWriter(Msg.Race).U8(K_HUMAN).Str(h.Name).Str(h.Cls).U8(h.Mask).F32(h.Total).Bool(h.Registered), true);
        }

        static void SetHuman(Human h)
        {
            Human old;
            if (humans.TryGetValue(h.Id, out old) && old.Mask == h.Mask && old.Total == h.Total && old.Cls == h.Cls && old.Registered == h.Registered) return;
            humans[h.Id] = h;
            Log.Info("rallye : " + h.Name + " (" + h.Cls + ")" + (h.Registered ? " inscrit" : "") + ", speciales " + h.Mask + ", total " + h.Total.ToString("F2"));
            SendBoard();
        }

        // ---------------------------------------------------------------- hote : semaine, graine, tableau
        static void HostWeek()
        {
            FsmInt wk = FsmVariables.GlobalVariables.FindFsmInt("GlobalWeeksPassed");
            int w = wk != null ? wk.Value : 0;
            if (w == boardWeek) return;
            boardWeek = w;
            humans.Clear(); slots.Clear();
            // Graine et heures de depart gardees par monde et par semaine (rallye-hote.ini) : un hote qui relance son jeu en
            // plein week-end garde les memes adversaires et ne redonne pas une heure deja prise.
            seed = 0;
            string key = World() + "|" + w + "=";
            if (System.IO.File.Exists(HostPath))
                foreach (string l in System.IO.File.ReadAllLines(HostPath))
                {
                    if (!l.StartsWith(key)) continue;
                    string[] parts = l.Substring(key.Length).Split(';');
                    int.TryParse(parts[0], out seed);
                    for (int i = 1; i < parts.Length; i++)
                    {
                        string[] q = parts[i].Split(':');
                        int a, b;
                        if (q.Length == 3 && int.TryParse(q[1], out a) && int.TryParse(q[2], out b)) slots[q[0]] = new[] { a, b };
                    }
                }
            bool kept = seed != 0;
            if (!kept) { seed = new System.Random().Next(1, int.MaxValue); SaveHost(); }
            Log.Info("rallye : semaine " + w + ", adversaires communs (graine " + seed + (kept ? ", gardee, " + slots.Count + " heures de depart" : "") + ")");
            lastReport = "";
            SendBoard();
        }

        static string HostPath { get { return System.IO.Path.Combine(Log.DataDir, "rallye-hote.ini"); } }

        static void SaveHost()
        {
            string key = World() + "|" + boardWeek + "=";
            var sb = new System.Text.StringBuilder(key).Append(seed);
            foreach (KeyValuePair<string, int[]> kv in slots) sb.Append(';').Append(kv.Key.Replace(";", "").Replace(":", "")).Append(':').Append(kv.Value[0]).Append(':').Append(kv.Value[1]);
            var lines = new List<string>();
            if (System.IO.File.Exists(HostPath)) foreach (string l in System.IO.File.ReadAllLines(HostPath)) if (!l.StartsWith(key)) lines.Add(l);
            lines.Add(sb.ToString());
            try { System.IO.File.WriteAllLines(HostPath, lines.ToArray()); } catch { }
        }

        static void SendBoard()
        {
            if (!Session.IsHost || Session.RemoteCount == 0) return;
            Session.Broadcast(BoardMsg(), true);
        }

        static NetWriter BoardMsg()
        {
            var w = new NetWriter(Msg.Race).U8(K_BOARD).I32(boardWeek).I32(seed).U8(humans.Count);
            foreach (Human h in humans.Values) w.U8(h.Id).Str(h.Name).Str(h.Cls).U8(h.Mask).F32(h.Total).Bool(h.Registered);
            return w;
        }

        // ---------------------------------------------------------------- messages
        public static void OnMessage(int kind, Peer from, NetReader r)
        {
            if (kind == K_HUMAN && Session.IsHost)
            {
                PlayerInfo pi;
                var h = new Human { Id = from.Id, Name = r.Str(), Cls = r.Str(), Mask = r.U8(), Total = r.F32(), Registered = r.Bool() };
                if (Session.Players.TryGetValue(from.Id, out pi)) h.Name = pi.Name;
                SetHuman(h);
                return;
            }
            if (kind == K_BOARD && !Session.IsHost)
            {
                boardWeek = r.I32(); seed = r.I32();
                humans.Clear();
                for (int i = 0, n = r.U8(); i < n; i++)
                {
                    var h = new Human { Id = r.U8(), Name = r.Str(), Cls = r.Str(), Mask = r.U8(), Total = r.F32(), Registered = r.Bool() };
                    humans[h.Id] = h;
                }
                return;
            }
            if (kind == K_SLOTASK && Session.IsHost)
            {
                int[] s = Slot(from.Id);
                Session.T.SendReliable(from, new NetWriter(Msg.Race).U8(K_SLOT).U8(s[0]).U8(s[1]).ToArray());
                return;
            }
            if (kind == K_SLOT && !Session.IsHost) { ApplySlot(r.U8(), r.U8()); return; }
            if (kind == K_ONSTAGE && Session.IsHost)
            {
                int st = r.U8(); float el = r.F32();
                if (st >= 1 && st <= 3) { remoteStage[st] = el; remoteStageAt[st] = Time.realtimeSinceStartup; }
            }
        }

        // ---------------------------------------------------------------- inscription : heures de depart
        static void OnRegistered()
        {
            if (!Session.Active) return;
            if (Session.IsHost) { int[] s = Slot(Session.LocalId); ApplySlot(s[0], s[1]); }
            else Session.SendToHost(new NetWriter(Msg.Race).U8(K_SLOTASK), true);
            lastReport = "";
        }

        static int[] Slot(int id)
        {
            PlayerInfo pi;
            string who = Session.Players.TryGetValue(id, out pi) ? pi.Name : "#" + id;
            who = who.Replace(";", "").Replace(":", "");
            int[] s;
            if (slots.TryGetValue(who, out s)) return s;
            var used1 = new HashSet<int>(); var used3 = new HashSet<int>();
            foreach (int[] x in slots.Values) { used1.Add(x[0]); used3.Add(x[1]); }
            var rnd = new System.Random();
            s = new[] { Free(14, 17, used1, rnd), Free(10, 14, used3, rnd) };
            slots[who] = s;
            SaveHost();
            Log.Info("rallye : heures de depart de " + who + " : SS1 " + s[0] + " h, SS2 " + (s[0] + 3) + " h, SS3 " + s[1] + " h");
            return s;
        }

        static int Free(int a, int b, HashSet<int> used, System.Random rnd)
        {
            var free = new List<int>();
            for (int h = a; h <= b; h++) if (!used.Contains(h)) free.Add(h);
            if (free.Count == 0) return rnd.Next(a, b + 1);
            return free[rnd.Next(free.Count)];
        }

        static void ApplySlot(int t1, int t3)
        {
            if (data == null || startTimes == null) return;
            while (startTimes.Count < 4) startTimes.Add(0);
            startTimes[1] = t1; startTimes[2] = t1 + 3; startTimes[3] = t3;
            FsmInt a = I("TimeSS1"), b = I("TimeSS2"), c = I("TimeSS3");
            if (a != null) a.Value = t1; if (b != null) b.Value = t1 + 3; if (c != null) c.Value = t3;
            Log.Info("rallye : heures de depart d'ici : SS1 " + t1 + " h, SS2 " + (t1 + 3) + " h, SS3 " + t3 + " h");
            Hud.Toast(Lang.T("Rallye : départs à ", "Rally: starts at ") + t1 + " h, " + (t1 + 3) + " h, " + Lang.T("dimanche ", "Sunday ") + t3 + " h");
        }

        // ---------------------------------------------------------------- classement
        static void Inject()
        {
            if (data == null || boardWeek < 0) return;   // pas de tableau de l'hote : classement du jeu
            string cls = Cls();
            int mask = Mask();
            if (mask == 0) mask = 1;
            FsmString pid = data.FsmVariables.FindFsmString("PlayerIDstring");
            FsmFloat tt = F("PlayerTimeTotal");
            names.Clear(); times.Clear();
            int opp = 0;
            foreach (KeyValuePair<string, float[]> o in Opponents(cls))
            {
                float total = 0;
                for (int i = 0; i < 3; i++) if ((mask & (1 << i)) != 0) total += o.Value[i];
                string key = "OPP" + opp++;
                names[key] = o.Key; times[key] = total;
            }
            string me = pid != null ? pid.Value : "player";
            names[me] = Session.Me != null ? Session.Me.Name : "?";
            times[me] = tt != null ? tt.Value : 0f;
            int others = 0;
            foreach (Human h in humans.Values)
            {
                if (h.Id == Session.LocalId || h.Cls != cls || h.Mask == 0) continue;
                // Temps deja present (egalite exacte) : le jeu retrouve le nom par le temps (HashTableGetKeyFromValue) et
                // ecrirait deux fois le meme pilote. Le notre reste exact (podium : egalite avec PlayerTimeTotal).
                float v = h.Total;
                while (times.ContainsValue(v)) v += 0.01f;
                names["MWC" + h.Id] = h.Name; times["MWC" + h.Id] = v; others++;
            }
            Log.Info("rallye : classement " + cls + " refait ici : " + opp + " adversaires communs, " + others + " autres pilotes, moi " + times[me]);
        }

        // Adversaires de la classe (DriversList, 50 noms) et leurs temps des 3 speciales, tires de la graine de l'hote.
        static List<KeyValuePair<string, float[]>> Opponents(string cls)
        {
            var list = new List<KeyValuePair<string, float[]>>();
            GameObject dl = Game.FindAny("RACES/RALLY/DriversList");
            Hashtable t = null;
            if (dl != null) foreach (PlayMakerHashTableProxy p in dl.GetComponents<PlayMakerHashTableProxy>()) if (p.referenceName == cls) t = p.hashTable;
            if (t == null) return list;
            var keys = new List<string>();
            foreach (object k in t.Keys) keys.Add(k.ToString());
            keys.Sort(string.CompareOrdinal);
            var rnd = new System.Random(seed ^ (cls == "Amateur" ? 0x5A5A : 0x3C3C));
            float[,] rg = cls == "Amateur" ? RangeAm : RangeJr;
            foreach (string k in keys)
            {
                var tt = new float[3];
                for (int i = 0; i < 3; i++) tt[i] = (float)(rg[i, 0] + rnd.NextDouble() * (rg[i, 1] - rg[i, 0]));
                object v = t[k];
                list.Add(new KeyValuePair<string, float[]>(v != null ? v.ToString() : k, tt));
            }
            return list;
        }

        // ---------------------------------------------------------------- speciales : voitures IA retenues
        static readonly string[] Clocks = { null, "RACES/RALLY/SS1/TimingSS1", "RACES/RALLY/SS2/TimingSS2", "RACES/RALLY/SS3/TimingSS3" };

        static void StageTick()
        {
            float now = Time.realtimeSinceStartup;
            for (int n = 1; n <= 3; n++)
            {
                PlayMakerFSM c = Game.FindFsm(Clocks[n], "Clock");
                if (c == null) continue;
                FsmFloat el = c.FsmVariables.FindFsmFloat("TimeTotalFPS");
                bool mine = c.gameObject.activeInHierarchy && c.ActiveStateName == "Get time";
                if (!Session.IsHost)
                {
                    if (mine && el != null) Session.SendToHost(new NetWriter(Msg.Race).U8(K_ONSTAGE).U8(n).F32(el.Value), false);
                    continue;
                }
                if (mine || el == null) { stageForced[n] = false; continue; }
                bool remote = now - remoteStageAt[n] < 2.5f;
                if (remote)
                {
                    el.Value = Mathf.Max(0.2f, remoteStage[n]);
                    if (n == 2) Game.SetGlobalBool("RallyStageOccupied", true);
                    if (!stageForced[n]) Log.Info("rallye : un invite court SS" + n + " : voitures IA retenues ici");
                    stageForced[n] = true;
                }
                else if (stageForced[n])
                {
                    stageForced[n] = false;
                    el.Value = 0f;
                    if (n == 2) Game.SetGlobalBool("RallyStageOccupied", false);
                    Log.Info("rallye : SS" + n + " libre");
                }
            }
        }

        // ---------------------------------------------------------------- invite : participation gardee par monde
        static string StorePath { get { return System.IO.Path.Combine(Log.DataDir, "rallye.ini"); } }
        static readonly string[] SnapF = { "PlayerTime", "PlayerTimeTotal" };
        static readonly string[] SnapI = { "PlayerClassLevel", "TimeSS1", "TimeSS2", "TimeSS3" };
        static readonly string[] SnapB = { "Registered", "SecondDay", "RaceOver" };
        static readonly System.Globalization.CultureInfo Inv = System.Globalization.CultureInfo.InvariantCulture;

        static string World()
        {
            FsmFloat pid = FsmVariables.GlobalVariables.FindFsmFloat("PlayerID");
            return pid != null ? pid.Value.ToString("F0") : "";
        }

        static void StoreGuest()
        {
            string w = World();
            if (w.Length == 0 || data == null) return;
            var sb = new System.Text.StringBuilder();
            foreach (string n in SnapF) { FsmFloat v = F(n); if (v != null) sb.Append(n).Append(':').Append(v.Value.ToString("R", Inv)).Append(';'); }
            foreach (string n in SnapI) { FsmInt v = I(n); if (v != null) sb.Append(n).Append(':').Append(v.Value).Append(';'); }
            foreach (string n in SnapB) { FsmBool v = data.FsmVariables.FindFsmBool(n); if (v != null) sb.Append(n).Append(':').Append(v.Value ? 1 : 0).Append(';'); }
            if (pen != null)
            {
                FsmFloat o = pen.FsmVariables.FindFsmFloat("OverallTimePenalty"); FsmInt j = pen.FsmVariables.FindFsmInt("PenaltyJumpstart"), p = pen.FsmVariables.FindFsmInt("PenaltyParcferme");
                if (o != null) sb.Append("pen.OverallTimePenalty:").Append(o.Value.ToString("R", Inv)).Append(';');
                if (j != null) sb.Append("pen.PenaltyJumpstart:").Append(j.Value).Append(';');
                if (p != null) sb.Append("pen.PenaltyParcferme:").Append(p.Value).Append(';');
            }
            sb.Append("PlayerTimes:").Append(Join(playerTimes)).Append(';');
            sb.Append("StartTimes:").Append(Join(startTimes)).Append(';');
            sb.Append("PlayerStages:").Append(Join(playerStages)).Append(';');
            var lines = new List<string>();
            if (System.IO.File.Exists(StorePath)) foreach (string l in System.IO.File.ReadAllLines(StorePath)) if (!l.StartsWith(w + "=")) lines.Add(l);
            lines.Add(w + "=" + sb);
            try { System.IO.File.WriteAllLines(StorePath, lines.ToArray()); } catch { }
        }

        static string Join(ArrayList a)
        {
            var parts = new List<string>();
            foreach (object o in a)
            {
                if (o is bool) parts.Add((bool)o ? "b1" : "b0");
                else if (o is int) parts.Add("i" + (int)o);
                else if (o is float) parts.Add("f" + ((float)o).ToString("R", Inv));
                else parts.Add("s" + (o != null ? o.ToString().Replace(",", "").Replace(";", "") : ""));
            }
            return string.Join(",", parts.ToArray());
        }

        static void Split(ArrayList a, string s)
        {
            a.Clear();
            if (s.Length == 0) return;
            foreach (string p in s.Split(','))
            {
                if (p.Length == 0) continue;
                string v = p.Substring(1);
                int i; float f;
                switch (p[0])
                {
                    case 'b': a.Add(v == "1"); break;
                    case 'i': a.Add(int.TryParse(v, out i) ? i : 0); break;
                    case 'f': a.Add(float.TryParse(v, System.Globalization.NumberStyles.Float, Inv, out f) ? f : 0f); break;
                    default: a.Add(v); break;
                }
            }
        }

        // La sauvegarde recue est celle de l'hote : sa participation est remplacee par celle de l'invite (ou effacee).
        static void RestoreGuest()
        {
            string w = World(), line = null;
            if (w.Length > 0 && System.IO.File.Exists(StorePath))
                foreach (string l in System.IO.File.ReadAllLines(StorePath)) if (l.StartsWith(w + "=")) line = l.Substring(w.Length + 1);
            if (line == null)
            {
                bool hadHost = Bool("Registered") || Mask() != 0;
                SetBool("Registered", false); SetBool("SecondDay", false); SetBool("RaceOver", false);
                FsmFloat a = F("PlayerTime"), b = F("PlayerTimeTotal"); if (a != null) a.Value = 0; if (b != null) b.Value = 0;
                for (int i = 0; i < playerStages.Count; i++) playerStages[i] = false;
                for (int i = 0; i < playerTimes.Count; i++) playerTimes[i] = 0f;
                if (pen != null) { FsmFloat o = pen.FsmVariables.FindFsmFloat("OverallTimePenalty"); if (o != null) o.Value = 0; FsmInt j = pen.FsmVariables.FindFsmInt("PenaltyJumpstart"), p = pen.FsmVariables.FindFsmInt("PenaltyParcferme"); if (j != null) j.Value = 0; if (p != null) p.Value = 0; }
                if (hadHost) Log.Info("rallye : participation de l'hote (sauvegarde) effacee ici : pas inscrit");
            }
            else
            {
                foreach (string kv in line.Split(';'))
                {
                    int c = kv.IndexOf(':');
                    if (c <= 0) continue;
                    string n = kv.Substring(0, c), v = kv.Substring(c + 1);
                    float f; int i;
                    if (n == "PlayerTimes") { Split(playerTimes, v); continue; }
                    if (n == "StartTimes") { Split(startTimes, v); continue; }
                    if (n == "PlayerStages") { Split(playerStages, v); continue; }
                    if (n.StartsWith("pen."))
                    {
                        if (pen == null) continue;
                        string pn = n.Substring(4);
                        FsmFloat pf = pen.FsmVariables.FindFsmFloat(pn); FsmInt pi = pen.FsmVariables.FindFsmInt(pn);
                        if (pf != null && float.TryParse(v, System.Globalization.NumberStyles.Float, Inv, out f)) pf.Value = f;
                        else if (pi != null && int.TryParse(v, out i)) pi.Value = i;
                        continue;
                    }
                    FsmFloat vf = data.FsmVariables.FindFsmFloat(n); FsmInt vi = data.FsmVariables.FindFsmInt(n); FsmBool vb = data.FsmVariables.FindFsmBool(n);
                    if (vf != null && float.TryParse(v, System.Globalization.NumberStyles.Float, Inv, out f)) vf.Value = f;
                    else if (vi != null && int.TryParse(v, out i)) vi.Value = i;
                    else if (vb != null) vb.Value = v == "1";
                }
                Log.Info("rallye : participation d'ici retrouvee (inscrit " + Bool("Registered") + ", speciales " + Mask() + ")");
            }
            // Autocollants, road book, parc ferme selon la participation d'ici.
            Replay.Depth++;
            try { if (data.ActiveStateName == "Rally unfinised?" || data.ActiveStateName == "Idle 4" || data.ActiveStateName == "Idle") Game.SetState(data, "Rally unfinised?"); }
            finally { Replay.Depth--; }
            lastReport = "";
        }

        // ---------------------------------------------------------------- essais
        // [Test] Autotest=rallye2 : chacun s'inscrit a 30 s (comme la feuille : REGISTER, classe Junior), puis a 40 s fait
        // SS1 avec un temps d'essai ([Test] TempsSS1, hote 400 s, invite 380 s : Clock.TimeTotal/TimeTotalFPS poses, SS1
        // envoye a 'Data' comme a la fin de la speciale), a 55 s ouvre le classement (OPENRESULTS). Chacun note ses heures
        // de depart, son classement (Leaderboard) et le tableau des humains. Attendu : heures de SS1 differentes, et les
        // deux pilotes humains dans le classement de chacun, avec les memes adversaires.
        static int testStep;
        public static void Test(string mode, float t)
        {
            // [Test] Autotest=rallye3 (avec le samedi 13 h de Races) : l'invite « court » SS1 de 35 s a 135 s (chrono de la
            // speciale allume, 'Clock' en "Get time", Timing.Start vrai). Chez l'hote : « voitures IA retenues » ; aucune
            // voiture ne quitte "Timing?" pendant ce temps (sauf si le chrono de l'invite passe 150 s).
            if (mode == "rallye3" && !Session.IsHost)
            {
                PlayMakerFSM c = Game.FindFsm(Clocks[1], "Clock"), tm = Game.FindFsm(Clocks[1], "Timing");
                if (testStep == 0 && t > 35f && c != null)
                {
                    testStep = 1;
                    for (Transform p = c.transform; p != null; p = p.parent) if (!p.gameObject.activeSelf) p.gameObject.SetActive(true);
                    FsmBool st = tm != null ? tm.FsmVariables.FindFsmBool("Start") : null;
                    if (st != null) st.Value = true;
                    Game.SetState(c, "Get time");
                    Log.Info("autotest : rallye3 : SS1 courue ici -> " + c.ActiveStateName);
                }
                if (testStep == 1 && t > 135f && tm != null)
                {
                    testStep = 2;
                    FsmBool st = tm.FsmVariables.FindFsmBool("Start");
                    if (st != null) st.Value = false;
                    Log.Info("autotest : rallye3 : SS1 finie ici -> " + (c != null ? c.ActiveStateName : "?"));
                }
                return;
            }
            if (mode != "rallye2" || data == null) return;
            if (testStep == 0 && t > 30f)
            {
                testStep = 1;
                FsmInt cl = I("PlayerClassLevel"); if (cl != null) cl.Value = 1;
                data.SendEvent("REGISTER");
                Log.Info("autotest : rallye2 : inscription -> " + data.ActiveStateName);
            }
            if (testStep == 1 && t > 40f)
            {
                testStep = 2;
                float tm = Config.GetInt("Test", Session.IsHost ? "TempsSS1" : "TempsSS1Invite", Session.IsHost ? 400 : 380);
                PlayMakerFSM c = Game.FindFsm(Clocks[1], "Clock");
                if (c != null) { c.FsmVariables.FindFsmFloat("TimeTotal").Value = tm; c.FsmVariables.FindFsmFloat("TimeTotalFPS").Value = tm; }
                data.SendEvent("SS1");
                Log.Info("autotest : rallye2 : SS1 finie en " + tm + " s -> " + data.ActiveStateName);
            }
            if (testStep == 2 && t > 60f)
            {
                testStep = 3;
                data.SendEvent("OPENRESULTS");
                Log.Info("autotest : rallye2 : classement demande -> " + data.ActiveStateName);
            }
            if (testStep == 3 && t > 68f)
            {
                testStep = 4;
                GameObject go = data.gameObject;
                ArrayList lb = null;
                foreach (PlayMakerArrayListProxy p in go.GetComponents<PlayMakerArrayListProxy>()) if (p.referenceName == "Leaderboard") lb = p.arrayList;
                var sb = new System.Text.StringBuilder();
                if (lb != null) for (int i = 0; i < lb.Count; i++) if (i < 3 || (lb[i] != null && lb[i].ToString().Contains("Joueur"))) sb.Append(" [").Append(i + 1).Append("] ").Append(lb[i]);
                if (lb != null) sb.Append(" (").Append(lb.Count).Append(" lignes)");
                var hs = new System.Text.StringBuilder();
                foreach (Human h in humans.Values) hs.Append(' ').Append(h.Name).Append('=').Append(h.Total.ToString("F0"));
                Log.Info("autotest : rallye2 : departs " + Join(startTimes) + " ; humains" + hs + " ; classement" + sb);
            }
        }
    }
}
