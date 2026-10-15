using System.Collections;
using System.Collections.Generic;
using HutongGames.PlayMaker;
using MWCoop.Net;
using UnityEngine;

namespace MWCoop
{
    // Course sur glace a plusieurs pilotes humains (relecture du 15/10, ecart n1 : chacun courait seul, Races ne faisait
    // suivre que les voitures IA). Transpose de Rally.cs, en plus simple : pas d'heures de depart (tout le monde part de la
    // grille), pas de classe.
    // Dans le jeu, la participation est celle d'UN joueur (voiture n 14 = CORRIS, Cars[14] de TrackFunctions) :
    //  - RACES/ICERACE/TrackFunctions/LINEUPS 'Logic' : inscription (REGISTER -> 'State 4' : PlayerRegistered, autocollants,
    //    CORRIS/DB allume, Cars[14] = CORRIS ; sinon Cars[14] = Reserved), listes des manches Qual1-4, Pickup1-2,
    //    Semifinal1-2, Final (numeros de voiture, textes) ;
    //  - RACES/ICERACE/TrackFunctions/RESULTS 'Leaderboard' : essais du vendredi, table LapTimes (numero -> meilleur tour) ;
    //    les 16 tours des IA sont TIRES AU HASARD ('State 7', 80-100 s), differents chez chacun -> grilles differentes ;
    //    le vendredi, la table est triee toutes les 4 s et remplit Qual1-4 ;
    //  - CORRIS/DB/RaceTrigger 'Data' : tours du joueur d'ici (LapTimes["14"]), arrivee d'une manche (ResultsNumbers).
    // WorldFsms saute LINEUPS (inscription personnelle, une seule CORRIS pour tous) : ce module relie les joueurs :
    //  - inscription : chacun annonce la sienne a l'hote (crochet sur 'State 4'), tous la voient (tableau, message) ;
    //  - tirage de l'hote : les tours d'essai des 16 IA de l'hote (sa table LapTimes, son tirage de la semaine) sont
    //    envoyes a tous ; l'invite les recopie dans sa table (crochet apres 'Set result' et toutes les 2 s) -> meme grille
    //    de qualification pour tous, le joueur d'ici place selon son propre tour ;
    //  - resultats : chacun annonce son meilleur tour d'essai et son arrivee de manche (manche, place) ; l'hote tient le
    //    tableau commun, le classe (manche la plus avancee, place, tour) et le renvoie a tous (a chaque changement et
    //    toutes les 20 s : arrivants) ;
    //  - invite : sa participation est gardee par monde (glace.ini, comme rallye.ini) ; la sauvegarde recue a chaque
    //    connexion contient celle de l'hote, remplacee par la sienne (ou effacee) au chargement.
    // Reste comme avant (voir TODO en bas) : les autres humains ne sont pas sur la grille d'ici (une seule voiture n 14),
    // et les arrivees des voitures IA d'une manche (ResultsNumbers) ne sont pas celles de l'hote chez l'invite.
    // [Coop] CourseGlace=0 coupe tout le module (vrai jeu : si cela se comporte mal).
    public static class IceRace
    {
        const int K_HUMAN = 20, K_BOARD = 21;   // (Msg.Race, rangs >= 20 : Races.OnMessage les passe ici ; 10-14 : Rally)
        const string ME = "14";                 // numero de la voiture du joueur (CORRIS/DB 'SkinChange' Number)
        static readonly string[] HeatNames = { "Q1", "Q2", "Q3", "Q4", "P1", "P2", "SF1", "SF2", "F" };   // HeatStage 0-8

        class Human { public int Id; public string Name = ""; public bool Registered; public float Lap; public int Heat = -1, Pos; }

        static PlayMakerFSM lineups, board, track;
        static Hashtable lapTimes;
        static ArrayList resultsNumbers;
        static bool enabled, hookedLineups, hookedBoard, restored;
        static float readyAt = -1, nextLook, nextReport, nextStore, nextBoard, nextApply;
        static string lastReport = "", lastRank = "";
        static int boardWeek = -1, localWeek = -1, lastHeat = -1, lastPos;
        static readonly Dictionary<int, Human> humans = new Dictionary<int, Human>();
        static readonly Dictionary<string, float> hostLaps = new Dictionary<string, float>();   // tirage de l'hote : numero -> tour (s)
        static readonly System.Globalization.CultureInfo Inv = System.Globalization.CultureInfo.InvariantCulture;

        public static void OnLevelLoaded()
        {
            lineups = board = track = null; lapTimes = null; resultsNumbers = null;
            hookedLineups = hookedBoard = restored = false; lastReport = lastRank = "";
            humans.Clear(); hostLaps.Clear(); boardWeek = localWeek = -1; lastHeat = -1; lastPos = 0;
            enabled = Config.GetInt("Coop", "CourseGlace", 1) != 0;
            readyAt = PlayerSync.InGame && enabled ? Time.realtimeSinceStartup + 10f : -1;
            if (PlayerSync.InGame && !enabled) Log.Info("glace : module coupe ([Coop] CourseGlace=0)");
        }

        class Hook : ModHook
        {
            public override string Module { get { return "courses"; } }
            public int Kind;   // 0 tirage des essais fait (apres 'Set result'), 2 inscription
            public override void OnEnter()
            {
                try
                {
                    if (Kind == 2) OnRegistered();
                    else ApplyHostLaps(true);
                }
                catch (System.Exception e) { Replay.HookError(e); }
                Finish();
            }
        }

        // ---------------------------------------------------------------- releve
        // ICERACE est eteint les semaines de rallye (Races 'SwapDates') : ses automates ne tournent pas, on repasse toutes
        // les 3 s. Les crochets ne sont poses que sur un automate demarre (etat actif connu), comme Rally.
        static bool Find()
        {
            if (lineups != null && board != null) return true;
            if (Time.realtimeSinceStartup < nextLook) return false;
            nextLook = Time.realtimeSinceStartup + 3f;
            if (lineups == null)
            {
                GameObject go = Game.FindAny("RACES/ICERACE/TrackFunctions/LINEUPS");
                PlayMakerFSM l = go != null ? Game.FsmOn(go, "Logic") : null;
                if (l == null || string.IsNullOrEmpty(l.ActiveStateName)) return false;
                GameObject tf = Game.FindAny("RACES/ICERACE/TrackFunctions");
                track = tf != null ? Game.FsmOn(tf, "Data") : null;
                lineups = l;
                if (!hookedLineups) { hookedLineups = true; Inject(lineups, "State 4", 2); }
                Log.Info("glace : inscription suivie (" + lineups.ActiveStateName + ", inscrit " + Registered() + ")");
            }
            if (board == null)
            {
                GameObject go = Game.FindAny("RACES/ICERACE/TrackFunctions/RESULTS");
                PlayMakerFSM b = go != null ? Game.FsmOn(go, "Leaderboard") : null;
                if (b == null || string.IsNullOrEmpty(b.ActiveStateName)) return false;
                foreach (PlayMakerHashTableProxy p in go.GetComponents<PlayMakerHashTableProxy>()) if (p.referenceName == "LapTimes") lapTimes = p.hashTable;
                foreach (PlayMakerArrayListProxy p in go.GetComponents<PlayMakerArrayListProxy>()) if (p.referenceName == "ResultsNumbers") resultsNumbers = p.arrayList;
                if (lapTimes == null || resultsNumbers == null) { Log.Warn("glace : table LapTimes ou liste ResultsNumbers absente de RESULTS"); nextLook = Time.realtimeSinceStartup + 30f; return false; }
                board = b;
                // Apres le tirage du jeu (et non avant : 'Set result' ecrirait par-dessus les tours de l'hote).
                if (!hookedBoard) { hookedBoard = true; FsmState s = board.Fsm.GetState("Set result"); if (s != null) FsmHooks.Append(s, new Hook { Kind = 0 }); else Log.Warn("glace : etat 'Set result' absent"); }
                Log.Info("glace : essais suivis (" + board.ActiveStateName + ", " + lapTimes.Count + " tours, mon tour " + MyLap().ToString("F1") + ")");
            }
            return lineups != null && board != null;
        }

        static void Inject(PlayMakerFSM f, string state, int kind)
        {
            FsmState s = f.Fsm.GetState(state);
            if (s == null) { Log.Warn("glace : etat " + state + " absent de " + f.FsmName); return; }
            FsmHooks.Prepend(s, new Hook { Kind = kind });
        }

        static bool Registered() { FsmBool b = lineups != null ? lineups.FsmVariables.FindFsmBool("PlayerRegistered") : null; return b != null && b.Value; }
        static int TrackHeat() { FsmInt h = track != null ? track.FsmVariables.FindFsmInt("HeatStage") : null; return h != null ? h.Value : -1; }
        static string HeatName(int h) { return h >= 0 && h < HeatNames.Length ? HeatNames[h] : "?"; }

        // Meilleur tour d'essai du joueur d'ici (0 : aucun ; 999 est la valeur du jeu pour "pas de tour").
        static float MyLap()
        {
            if (lapTimes == null || !lapTimes.ContainsKey(ME)) return 0f;
            float v = ToF(lapTimes[ME]);
            return v > 0f && v < 900f ? v : 0f;
        }

        static float ToF(object o)
        {
            if (o is float) return (float)o;
            if (o is double) return (float)(double)o;
            if (o is int) return (int)o;
            float f; return o != null && float.TryParse(o.ToString(), System.Globalization.NumberStyles.Float, Inv, out f) ? f : 0f;
        }

        static string World()
        {
            FsmFloat pid = FsmVariables.GlobalVariables.FindFsmFloat("PlayerID");
            return pid != null ? pid.Value.ToString("F0") : "";
        }

        static int Week() { FsmInt wk = FsmVariables.GlobalVariables.FindFsmInt("GlobalWeeksPassed"); return wk != null ? wk.Value : 0; }

        // ---------------------------------------------------------------- mise a jour
        public static void Update()
        {
            if (!enabled || !Session.Active || !PlayerSync.InGame || readyAt < 0 || Time.realtimeSinceStartup < readyAt) return;
            if (!Find()) return;
            string wld = World();
            if (wld.Length == 0 || wld == "0") return;   // identifiant de la partie pas encore charge : cles par monde fausses
            float now = Time.realtimeSinceStartup;
            int w = Week();
            if (w != localWeek)
            {
                // Nouvelle semaine : la derniere arrivee de manche ne compte plus (le jeu remet les listes a zero).
                if (localWeek >= 0) { lastHeat = -1; lastPos = 0; lastReport = ""; }
                localWeek = w;
            }
            if (!Session.IsHost && !restored)
            {
                // Apres le chargement (State 1 -> Exists -> Load -> Check status -> State 4 / Reset decals -> State 2) :
                // sinon Load ecraserait la participation d'ici par celle de la sauvegarde de l'hote.
                if (lineups.ActiveStateName != "State 2") return;
                restored = true; RestoreGuest();
            }
            if (Session.IsHost)
            {
                HostWeek(w);
                if (now >= nextBoard) { nextBoard = now + 20f; SendBoard(); }   // (arrivants)
            }
            else if (now >= nextApply) { nextApply = now + 2f; ApplyHostLaps(false); }
            if (now >= nextReport) { nextReport = now + 5f; Report(false); }
            if (now >= nextStore) { nextStore = now + 10f; Store(); }
        }

        // Arrivee de manche du joueur d'ici : son numero dans ResultsNumbers (ordre d'arrivee, remis a zero a la manche
        // suivante) ; la derniere arrivee est gardee (lastHeat, lastPos) jusqu'a la semaine suivante.
        static void ReadFinish()
        {
            if (resultsNumbers == null) return;
            for (int i = 0; i < resultsNumbers.Count; i++)
            {
                object o = resultsNumbers[i];
                if (o == null || o.ToString() != ME) continue;
                int heat = TrackHeat();
                if (lastPos != i + 1 || lastHeat != heat) Log.Info("glace : arrive " + (i + 1) + "e de la manche " + HeatName(heat) + " ici");
                lastPos = i + 1; lastHeat = heat;
                return;
            }
        }

        // Notre participation, annoncee a l'hote quand elle change (et toutes les 30 s si inscrit : un hote qui revient).
        static void Report(bool force)
        {
            if (lineups == null) return;
            ReadFinish();
            bool reg = Registered();
            float lap = MyLap();
            string sig = reg + "|" + lap.ToString("R") + "|" + lastHeat + "|" + lastPos;
            if (!force && sig == lastReport && Time.realtimeSinceStartup % 30f > 5f) return;
            if (!reg && lap == 0f && lastPos == 0 && lastReport.Length == 0) return;
            lastReport = sig;
            var h = new Human { Id = Session.LocalId, Name = Session.Me != null ? Session.Me.Name : "?", Registered = reg, Lap = lap, Heat = lastHeat, Pos = lastPos };
            if (Session.IsHost) { SetHuman(h); return; }
            Session.SendToHost(new NetWriter(Msg.Race).U8(K_HUMAN).Str(h.Name).Bool(h.Registered).F32(h.Lap).U8(h.Heat + 1).U8(h.Pos), true);
        }

        static void SetHuman(Human h)
        {
            Human old;
            if (humans.TryGetValue(h.Id, out old) && old.Registered == h.Registered && old.Lap == h.Lap && old.Heat == h.Heat && old.Pos == h.Pos) return;
            bool newReg = h.Registered && (old == null || !old.Registered);
            humans[h.Id] = h;
            Log.Info("glace : " + h.Name + (h.Registered ? " inscrit" : "") + ", tour " + h.Lap.ToString("F1") + (h.Pos > 0 ? ", " + h.Pos + "e en " + HeatName(h.Heat) : ""));
            if (newReg && h.Id != Session.LocalId) Hud.Toast(h.Name + Lang.T(" s'inscrit à la course sur glace", " registered for the ice race"));
            ShowRank();
            SendBoard();
        }

        // ---------------------------------------------------------------- hote : semaine, tirage, tableau
        static void HostWeek(int w)
        {
            if (w == boardWeek) return;
            boardWeek = w;
            humans.Clear();
            lastReport = "";
            Log.Info("glace : semaine " + w + ", tirage des essais de l'hote donne a tous");
            SendBoard();
        }

        static void SendBoard()
        {
            if (!Session.IsHost || Session.RemoteCount == 0) return;
            Session.Broadcast(BoardMsg(), true);
        }

        // Tableau : semaine, tours d'essai des IA de l'hote (sa table LapTimes sans sa propre voiture), humains dans l'ordre
        // du classement de l'hote.
        static NetWriter BoardMsg()
        {
            var w = new NetWriter(Msg.Race).U8(K_BOARD).I32(boardWeek);
            var laps = new List<KeyValuePair<string, float>>();
            if (lapTimes != null)
                foreach (object k in lapTimes.Keys)
                {
                    string key = k.ToString();
                    if (key == ME) continue;
                    laps.Add(new KeyValuePair<string, float>(key, ToF(lapTimes[key])));
                }
            w.U8(laps.Count);
            foreach (var kv in laps) w.Str(kv.Key).F32(kv.Value);
            List<Human> ranked = Ranked();
            w.U8(ranked.Count);
            foreach (Human h in ranked) w.U8(h.Id).Str(h.Name).Bool(h.Registered).F32(h.Lap).U8(h.Heat + 1).U8(h.Pos);
            return w;
        }

        // ---------------------------------------------------------------- messages
        public static void OnMessage(int kind, Peer from, NetReader r)
        {
            if (!enabled) return;
            if (kind == K_HUMAN && Session.IsHost)
            {
                PlayerInfo pi;
                var h = new Human { Id = from.Id, Name = r.Str(), Registered = r.Bool(), Lap = r.F32(), Heat = r.U8() - 1, Pos = r.U8() };
                if (Session.Players.TryGetValue(from.Id, out pi)) h.Name = pi.Name;
                SetHuman(h);
                return;
            }
            if (kind == K_BOARD && !Session.IsHost)
            {
                boardWeek = r.I32();
                hostLaps.Clear();
                for (int i = 0, n = r.U8(); i < n; i++) { string k = r.Str(); hostLaps[k] = r.F32(); }
                humans.Clear();
                for (int i = 0, n = r.U8(); i < n; i++)
                {
                    var h = new Human { Id = r.U8(), Name = r.Str(), Registered = r.Bool(), Lap = r.F32(), Heat = r.U8() - 1, Pos = r.U8() };
                    humans[h.Id] = h;
                }
                nextApply = 0;
                ShowRank();
            }
        }

        // ---------------------------------------------------------------- inscription
        static void OnRegistered()
        {
            if (!Session.Active) return;
            if (!Session.IsHost && !restored) return;   // chargement : c'est l'inscription de l'hote (sauvegarde), pas la notre
            lastReport = "";
            Report(true);
        }

        // ---------------------------------------------------------------- tirage de l'hote chez l'invite
        // Les tours d'essai des 16 IA de l'hote remplacent ceux tires ici (table LapTimes, sauf notre voiture) : le
        // 'Leaderboard' les retrie toutes les 4 s le vendredi et refait Qual1-4 -> meme grille que chez l'hote.
        static void ApplyHostLaps(bool afterDraw)
        {
            if (Session.IsHost || lapTimes == null || hostLaps.Count == 0) return;
            int changed = 0;
            foreach (KeyValuePair<string, float> kv in hostLaps)
            {
                if (kv.Key == ME || kv.Value <= 0f) continue;
                if (lapTimes.ContainsKey(kv.Key) && Mathf.Abs(ToF(lapTimes[kv.Key]) - kv.Value) < 0.001f) continue;
                lapTimes[kv.Key] = kv.Value;
                changed++;
            }
            if (changed > 0) Log.Info("glace : " + changed + " tours d'essai des IA recopies de l'hote" + (afterDraw ? " (apres le tirage du jeu ici)" : ""));
        }

        // ---------------------------------------------------------------- classement commun
        // Manche la plus avancee d'abord, puis la place, puis le meilleur tour d'essai (sans tour : dernier).
        static List<Human> Ranked()
        {
            var l = new List<Human>(humans.Values);
            l.Sort(delegate(Human a, Human b)
            {
                if (a.Heat != b.Heat) return b.Heat.CompareTo(a.Heat);
                int pa = a.Pos > 0 ? a.Pos : 99, pb = b.Pos > 0 ? b.Pos : 99;
                if (pa != pb) return pa.CompareTo(pb);
                float la = a.Lap > 0f ? a.Lap : 1e9f, lb = b.Lap > 0f ? b.Lap : 1e9f;
                if (la != lb) return la.CompareTo(lb);
                return a.Id.CompareTo(b.Id);
            });
            return l;
        }

        static string LapText(float s) { return s > 0f ? ((int)(s / 60f)) + ":" + (s % 60f).ToString("00.0", Inv) : "-"; }

        // Le tableau commun, montre (message) quand il change ; seuls les inscrits ou ceux qui ont couru y figurent.
        static void ShowRank()
        {
            var sb = new System.Text.StringBuilder();
            int n = 0;
            foreach (Human h in Ranked())
            {
                if (!h.Registered && h.Lap == 0f && h.Pos == 0) continue;
                if (n > 0) sb.Append(", ");
                sb.Append(++n).Append(". ").Append(h.Name);
                if (h.Pos > 0) sb.Append(' ').Append(HeatName(h.Heat)).Append(' ').Append(h.Pos).Append(Lang.T("e", h.Pos == 1 ? "st" : h.Pos == 2 ? "nd" : h.Pos == 3 ? "rd" : "th"));
                if (h.Lap > 0f) sb.Append(' ').Append(LapText(h.Lap));
            }
            string s = sb.ToString();
            if (s == lastRank) return;
            bool first = lastRank.Length == 0;
            lastRank = s;
            if (n == 0) return;
            Log.Info("glace : classement commun : " + s);
            if (!first || n > 1) Hud.Toast(Lang.T("Glace : ", "Ice race: ") + s);
        }

        // ---------------------------------------------------------------- participation gardee par monde
        // Invite : inscription, etapes et meilleur tour (la sauvegarde recue est celle de l'hote). Tous : derniere arrivee
        // de manche (semaine, manche, place), que le jeu ne garde pas.
        static string StorePath { get { return System.IO.Path.Combine(Log.DataDir, "glace.ini"); } }

        static void Store()
        {
            string w = World();
            if (w.Length == 0 || lineups == null) return;
            var sb = new System.Text.StringBuilder();
            sb.Append("Registered:").Append(Registered() ? 1 : 0).Append(';');
            FsmInt rs = lineups.FsmVariables.FindFsmInt("RaceStage"), hs = lineups.FsmVariables.FindFsmInt("HeatStage");
            if (rs != null) sb.Append("RaceStage:").Append(rs.Value).Append(';');
            if (hs != null) sb.Append("HeatStage:").Append(hs.Value).Append(';');
            sb.Append("Lap:").Append(MyLap().ToString("R", Inv)).Append(';');
            sb.Append("Week:").Append(localWeek).Append(';');
            sb.Append("Heat:").Append(lastHeat).Append(';');
            sb.Append("Pos:").Append(lastPos).Append(';');
            string line = w + "=" + sb;
            var lines = new List<string>();
            bool same = false;
            if (System.IO.File.Exists(StorePath)) foreach (string l in System.IO.File.ReadAllLines(StorePath)) { if (l == line) same = true; if (!l.StartsWith(w + "=")) lines.Add(l); }
            if (same) return;   // (rien de neuf : pas d'ecriture toutes les 10 s)
            lines.Add(line);
            try { System.IO.File.WriteAllLines(StorePath, lines.ToArray()); } catch { }
        }

        static Dictionary<string, string> Load()
        {
            var d = new Dictionary<string, string>();
            string w = World(), line = null;
            if (w.Length > 0 && System.IO.File.Exists(StorePath))
                foreach (string l in System.IO.File.ReadAllLines(StorePath)) if (l.StartsWith(w + "=")) line = l.Substring(w.Length + 1);
            if (line == null) return null;
            foreach (string kv in line.Split(';'))
            {
                int c = kv.IndexOf(':');
                if (c > 0) d[kv.Substring(0, c)] = kv.Substring(c + 1);
            }
            return d;
        }

        // La sauvegarde recue est celle de l'hote : sa participation est remplacee par celle de l'invite (ou effacee).
        // 'Check status' du jeu refait ce qui en decoule (Cars[14] = CORRIS ou Reserved, autocollants, CORRIS/DB) et
        // 'State 2' redonne HeatStage a TrackFunctions.
        static void RestoreGuest()
        {
            Dictionary<string, string> d = Load();
            FsmBool reg = lineups.FsmVariables.FindFsmBool("PlayerRegistered");
            FsmInt rs = lineups.FsmVariables.FindFsmInt("RaceStage"), hs = lineups.FsmVariables.FindFsmInt("HeatStage");
            bool hadHost = Registered();
            int i; float f;
            if (d == null)
            {
                if (reg != null) reg.Value = false;
                if (rs != null) rs.Value = 0; if (hs != null) hs.Value = 0;
                if (lapTimes != null && lapTimes.ContainsKey(ME)) lapTimes[ME] = 999f;   // (valeur du jeu : pas de tour)
                if (hadHost) Log.Info("glace : participation de l'hote (sauvegarde) effacee ici : pas inscrit");
            }
            else
            {
                string v;
                if (reg != null && d.TryGetValue("Registered", out v)) reg.Value = v == "1";
                if (rs != null && d.TryGetValue("RaceStage", out v) && int.TryParse(v, out i)) rs.Value = i;
                if (hs != null && d.TryGetValue("HeatStage", out v) && int.TryParse(v, out i)) hs.Value = i;
                if (lapTimes != null && d.TryGetValue("Lap", out v) && float.TryParse(v, System.Globalization.NumberStyles.Float, Inv, out f)) lapTimes[ME] = f > 0f ? f : 999f;
                int wk = -1;
                if (d.TryGetValue("Week", out v)) int.TryParse(v, out wk);
                if (wk == localWeek)
                {
                    if (d.TryGetValue("Heat", out v) && int.TryParse(v, out i)) lastHeat = i;
                    if (d.TryGetValue("Pos", out v) && int.TryParse(v, out i)) lastPos = i;
                }
                Log.Info("glace : participation d'ici retrouvee (inscrit " + Registered() + ", tour " + MyLap().ToString("F1") + (lastPos > 0 ? ", " + lastPos + "e en " + HeatName(lastHeat) : "") + ")");
            }
            if (d != null || hadHost)
            {
                Replay.Depth++;
                try { Game.SetState(lineups, "Check status"); }
                finally { Replay.Depth--; }
            }
            lastReport = "";
        }

        // ---------------------------------------------------------------- essais
        // [Test] Autotest=glace2 (avec la semaine de la glace de Races 'glace', vendredi 10 h) : chacun s'inscrit a 30 s
        // (REGISTER a LINEUPS, comme la feuille), a 45 s pose son meilleur tour d'essai ([Test] TourGlace, hote 88 s,
        // invite 86 s, dans LapTimes["14"] comme CORRIS/DB/RaceTrigger 'Set result'), a 70 s note sa table LapTimes, ses
        // listes Qual1-4 et le tableau des humains. Attendu : memes tours des IA des deux cotes (ceux de l'hote), les deux
        // pilotes humains inscrits dans le tableau de chacun, l'invite devant.
        static int testStep;
        public static void Test(string mode, float t)
        {
            if (mode != "glace2" || lineups == null || board == null) return;
            if (testStep == 0 && t > 30f)
            {
                testStep = 1;
                lineups.SendEvent("REGISTER");
                Log.Info("autotest : glace2 : inscription -> " + lineups.ActiveStateName + ", inscrit " + Registered());
            }
            if (testStep == 1 && t > 45f)
            {
                testStep = 2;
                float lap = Config.GetInt("Test", Session.IsHost ? "TourGlace" : "TourGlaceInvite", Session.IsHost ? 88 : 86);
                if (lapTimes != null) lapTimes[ME] = lap;
                Log.Info("autotest : glace2 : meilleur tour d'ici " + lap + " s");
            }
            if (testStep == 2 && t > 70f)
            {
                testStep = 3;
                var sb = new System.Text.StringBuilder();
                if (lapTimes != null)
                {
                    var keys = new List<string>();
                    foreach (object k in lapTimes.Keys) keys.Add(k.ToString());
                    keys.Sort(string.CompareOrdinal);
                    foreach (string k in keys) sb.Append(' ').Append(k).Append('=').Append(ToF(lapTimes[k]).ToString("F1"));
                }
                var ql = new System.Text.StringBuilder();
                foreach (PlayMakerArrayListProxy p in lineups.gameObject.GetComponents<PlayMakerArrayListProxy>())
                {
                    if (!p.referenceName.StartsWith("Qual")) continue;
                    ql.Append(' ').Append(p.referenceName).Append('[');
                    for (int i = 0; i < p.arrayList.Count; i++) ql.Append(i > 0 ? "," : "").Append(p.arrayList[i]);
                    ql.Append(']');
                }
                var hs = new System.Text.StringBuilder();
                foreach (Human h in Ranked()) hs.Append(' ').Append(h.Name).Append('=').Append(h.Registered ? "inscrit" : "-").Append('/').Append(h.Lap.ToString("F1"));
                Log.Info("autotest : glace2 : tours" + sb + " ; grilles" + ql + " ; humains" + hs);
            }
        }

        // TODO (a verifier en jeu, vidage a faire) -- ce qui reste de l'ecart n1 :
        //  - autres humains sur la grille d'ici : une seule voiture n 14 (CORRIS, Cars[14]) ; mettre un autre humain dans
        //    Qual1-4 demanderait un numero libre et une entree Cars[n] (RACES/ICERACE/Reserved ? ses avatars de voiture
        //    VehicleSync) -- a voir avec 'Create grid' (ConvertStringToInt CarID -> Cars[CarNumber], Navigation
        //    StartPositionID) ;
        //  - arrivees d'une manche chez l'invite : ResultsNumbers n'y recoit que "14" (RaceTrigger des IA coupes par
        //    Races.Mute) -> Semifinal/Pickup/Final refaits a partir d'une liste incomplete ('State 1', 'State 4'... de
        //    TrackFunctions 'Data'). Envoyer ResultsNumbers de l'hote a 'Wait results' / 'Wait results 2' et y inserer
        //    le "14" de l'invite selon son instant d'arrivee (a horodater : CORRIS/DB/RaceTrigger 'State 2') ;
        //  - listes de manches de l'invite gardees entre deux sessions (Qual1-4... de LINEUPS, et RESULTSPublic) : pour
        //    l'instant seules les variables (inscription, etapes, tour) le sont ; le vendredi les Qual sont refaites
        //    toutes les 4 s par 'Leaderboard', les autres jours elles viennent de la sauvegarde de l'hote ;
        //  - prix (TentContents/Prices/PriceMoneyRace, 'Get pos' : podium par ResultsNumbers[0..2] == "14") : chacun
        //    touche le sien chez lui (Wallet), depend du point precedent ;
        //  - feuille des resultats (Sheets/IceRacePractice, TentContents/Results) : le tableau commun n'y est pas, il
        //    passe par message (Hud.Toast) et le journal.
    }
}
