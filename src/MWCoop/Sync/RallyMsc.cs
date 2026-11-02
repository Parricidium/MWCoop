using System.Collections;
using System.Collections.Generic;
using HutongGames.PlayMaker;
using MWCoop.Net;
using UnityEngine;

namespace MWCoop
{
    // Rallye de My Summer Car a plusieurs pilotes humains (22/10, JD : « fait tout »). Le jeu n'a qu'une participation,
    // RALLY/ResultsWeekend 'Data' : deux journees (samedi, dimanche), classe Junior / Amateur, adversaires tires de
    // RALLY/DriversList avec des temps au hasard (Opponent time), le joueur ajoute aux tables Names / Times par son
    // PlayerIDstring ('Player name' / 'Player time', puis 'Player name 2' / 'Player time 2' le dimanche). Le dimanche, 'Player
    // time 2' -> 'Hash to array' (Names / Times -> NamesArray / TimesArray) -> tri -> 'Class?' -> 'Player position' : le podium
    // (or / argent / bronze, trophee, prix). 'Hash to array 2' (OPENRESULTS depuis 'Idle 4') ne sert qu'a la feuille de resultats.
    // Les voitures IA et la table des pilotes IA (RallyCars :: Results) sont deja partagees par Races.
    // Ici : chaque joueur annonce son temps cumule (PlayerTimeTotal) et sa classe a la fin de chaque journee ('Idle 4' le
    // samedi, 'Hash to array' le dimanche) ; a l'entree de 'Hash to array' (podium) et de 'Hash to array 2' (feuille), les autres
    // humains de sa classe sont ajoutes aux tables Names / Times (cle MWC<id>, valeur le pseudo / le temps). Rien d'autre n'est
    // touche (les adversaires au hasard restent ceux de chacun).
    // Podium (decision de JD, 24/10 : « une limite de temps, calculee sur le nombre de joueurs reels, avec une moyenne des temps
    // des PNJ ») : 'Hash to array' passe en sequence et commence par Hold, qui attend que chaque humain engage (temps du samedi
    // annonce dans la meme classe, toujours dans la partie) ait annonce son dimanche ; au plus (OppMinTimeD2 + OppMaxTimeD2) / 2
    // x 1,25 + 60 s par humain attendu, compte depuis l'arrivee ici, message a l'ecran toutes les 10 s. Puis classement et podium
    // avec les temps recus. [Test] RallyeAttente=s : delai maximum force (essais).
    public static class RallyMsc
    {
        // (temps cumules du samedi et du dimanche, gardes a part : la feuille du samedi et le podium du dimanche n'ont pas les memes)
        class Human { public string Name, Cls; public float Time1, Time2; }
        static int myDay;   // derniere journee annoncee ici (feuille de resultats : temps de cette journee-la)
        static readonly Dictionary<int, Human> humans = new Dictionary<int, Human>();
        static PlayMakerFSM data;
        static Hashtable names, times;
        static float nextLook;
        static bool hooked;
        static string lastSent = "";
        // (audit MSC 02/11) Temps cumules annonces d'ici, samedi [1] et dimanche [2], et leur classe : redits aux arrivants (l'hote,
        // RedireA) et apres un rechargement (Recharge) -- avant, une seule annonce, perdue pour qui n'etait pas la a ce moment.
        static readonly float[] mine = new float[3];
        static string mineCls = "";
        static bool loaded;          // participation chargee par le jeu (Data sorti de 'State 2' / 'Check day' / 'Load 2')
        static int weekDay;          // dernier GlobalDay vu (fin du week-end : temps des autres effaces)

        public static void OnLevelLoaded()
        {
            humans.Clear(); data = null; names = times = null; nextLook = 0f; hooked = false; lastSent = ""; trophiesHooked = false; nextTrophyLook = 0f; myDay = 0;
            mine[1] = mine[2] = 0f; mineCls = ""; loaded = false; weekDay = 0; nextStore = 0f; storedLine = null;
            snapshots.Clear(); stageBy.Clear(); clocks[1] = clocks[2] = null; nextStageSend = nextClockLook = 0f;
        }

        // Humains engages qui n'ont pas encore annonce leur temps du dimanche (ma classe, temps du samedi recu, encore la).
        static List<Human> Pending()
        {
            var l = new List<Human>();
            string cls = Cls();
            foreach (KeyValuePair<int, Human> kv in humans)
                if (kv.Value.Cls == cls && kv.Value.Time1 > 0f && kv.Value.Time2 <= 0f && Session.Players.ContainsKey(kv.Key)) l.Add(kv.Value);
            return l;
        }

        static float MaxWait(int waited)
        {
            float forced = Config.GetFloat("Test", "RallyeAttente", 0f);
            if (forced > 0f) return forced;
            FsmFloat lo = data != null ? data.FsmVariables.FindFsmFloat("OppMinTimeD2") : null, hi = data != null ? data.FsmVariables.FindFsmFloat("OppMaxTimeD2") : null;
            float avg = lo != null && hi != null && hi.Value > 0f ? (lo.Value + hi.Value) * 0.5f : 400f;
            return avg * 1.25f + 60f * waited;
        }

        // En tete de 'Hash to array' (dimanche, sequence) : attend les autres humains engages, au plus MaxWait.
        class Hold : ModHook
        {
            public override string Module { get { return "rallye (MSC)"; } }
            float until, nextToast;
            public override void OnEnter()
            {
                try
                {
                    List<Human> p = Pending();
                    if (p.Count == 0 || !Session.Active) { Finish(); return; }
                    float max = MaxWait(p.Count);
                    until = Time.realtimeSinceStartup + max; nextToast = 0f;
                    MWCoop.Log.Info("rallye (MSC) : podium en attente de " + Names(p) + " (au plus " + (int)max + " s)");
                }
                catch (System.Exception e) { Replay.HookError(e); Finish(); }
            }
            public override void OnUpdate()
            {
                try
                {
                    List<Human> p = Pending();
                    float now = Time.realtimeSinceStartup;
                    if (p.Count == 0 || now >= until || !Session.Active)
                    {
                        MWCoop.Log.Info("rallye (MSC) : podium calcule " + (p.Count == 0 ? "avec tous les temps" : "sans " + Names(p) + " (delai depasse)"));
                        Finish();
                        return;
                    }
                    if (now >= nextToast)
                    {
                        nextToast = now + 10f;
                        int left = Mathf.CeilToInt(until - now);
                        Hud.Toast(string.Format(Lang.T("Podium du rallye : en attente des temps de {0} (encore {1})", "Rally podium: waiting for the times of {0} ({1} left)"),
                                                Names(p), (left / 60) + ":" + (left % 60).ToString("00")));
                    }
                }
                catch (System.Exception e) { Replay.HookError(e); Finish(); }
            }
        }

        static string Names(List<Human> p)
        {
            var n = new List<string>();
            foreach (Human h in p) n.Add(h.Name);
            return string.Join(", ", n.ToArray());
        }

        // Trophees du podium (prix du rallye, 24/10) : 'Player position' -> 'Gold' / 'Silver' / 'Bronze' envoie SPAWNITEM a
        // Spawner/CreateTrophiesAmateur (ou Junior) :: Gold / Silver / Bronze, dont 'State 7' cree le trophee a l'arrivee du
        // dimanche et compte ObjectNumberInt (sauve dans trophies.txt) -- chez le gagnant seulement : un invite qui gagnait avait
        // son trophee chez lui, jamais chez l'hote, donc absent de la sauvegarde (perdu au rechargement). Ici : crochet en tete de
        // 'State 7' ; fait ici -> @trophee a tous, qui rejouent SPAWNITEM (meme trophee, meme compteur). Le prix en argent
        // (RALLY/Sunday/FinishArea/Stuff/PayMoney) reste celui de chacun (portefeuilles separes).
        static readonly string[] TrophyRoots = { "Spawner/CreateTrophiesAmateur", "Spawner/CreateTrophiesJunior" };
        static readonly string[] TrophyFsms = { "Gold", "Silver", "Bronze" };
        static bool trophiesHooked, trophyApplying;
        static float nextTrophyLook;

        class TrophyHook : ModHook
        {
            public override string Module { get { return "rallye (MSC)"; } }
            public string Root, Fsm;
            public override void OnEnter()
            {
                try
                {
                    if (!trophyApplying && Session.Active && Session.RemoteCount > 0)
                    {
                        Session.SendAll(new NetWriter(Msg.Job).U8(Session.LocalId).Str("@trophee").Str(Root).Str(Fsm), true);
                        MWCoop.Log.Info("rallye (MSC) : trophee " + Fsm + " (" + Root + ") cree ici, envoye");
                    }
                }
                catch (System.Exception e) { Replay.HookError(e); }
                Finish();
            }
        }

        static void HookTrophies()
        {
            if (trophiesHooked || Time.realtimeSinceStartup < nextTrophyLook) return;
            nextTrophyLook = Time.realtimeSinceStartup + 10f;
            int n = 0;
            foreach (string root in TrophyRoots)
            {
                GameObject go = Game.FindAny(root);
                if (go == null) continue;
                foreach (string fn in TrophyFsms)
                {
                    PlayMakerFSM f = Game.FsmOn(go, fn);
                    FsmState s = f != null ? f.Fsm.GetState("State 7") : null;
                    if (s == null || !s.IsInitialized) continue;
                    FsmHooks.Prepend(s, new TrophyHook { Root = root, Fsm = fn });
                    n++;
                }
            }
            if (n == 0) return;
            trophiesHooked = true;
            Log.Info("rallye (MSC) : " + n + " createurs de trophees suivis");
        }

        public static void OnTrophy(int who, NetReader r)
        {
            string root = r.Str(), fn = r.Str();
            GameObject go = Game.FindAny(root);
            PlayMakerFSM f = go != null ? Game.FsmOn(go, fn) : null;
            if (f == null) { Log.Warn("rallye (MSC) : createur de trophee " + root + "::" + fn + " introuvable ici"); return; }
            trophyApplying = true; Replay.Depth++;
            try { f.SendEvent("SPAWNITEM"); }
            finally { trophyApplying = false; Replay.Depth--; }
            Log.Info("rallye (MSC) : trophee " + fn + " de #" + who + " cree ici aussi");
        }

        class Hook : ModHook
        {
            public override string Module { get { return "rallye (MSC)"; } }
            public int Kind;   // 0 : classement (humains ajoutes) ; 1 : fin de journee (annonce)
            // Journee : 1 ('Idle 4', samedi), 2 ('Hash to array', dimanche) ; 0 pour la feuille ('Hash to array 2') = myDay.
            // (pas {SecondDay} du jeu : il passe a vrai des la fin du samedi, 'Day 1', et le restait -- le samedi partait en « journee 2 »)
            public int Day;
            public override void OnEnter()
            {
                try { if (Kind == 0) InjectHumans(Day); else Announce(Day); } catch (System.Exception e) { Replay.HookError(e); }
                Finish();
            }
        }

        static bool Find()
        {
            if (data != null) return true;
            if (Time.realtimeSinceStartup < nextLook) return false;
            nextLook = Time.realtimeSinceStartup + 5f;
            GameObject go = Game.FindAny("RALLY/ResultsWeekend");
            if (go == null) return false;
            PlayMakerFSM d = Game.FsmOn(go, "Data");
            if (d == null || string.IsNullOrEmpty(d.ActiveStateName)) return false;   // (RALLY eteint hors week-end)
            foreach (PlayMakerHashTableProxy p in go.GetComponents<PlayMakerHashTableProxy>())
            {
                if (p.referenceName == "Names") names = p.hashTable;
                if (p.referenceName == "Times") times = p.hashTable;
            }
            if (names == null || times == null) return false;
            data = d;
            if (!hooked)
            {
                hooked = true;
                // (feuille de resultats : les humains ajoutes ; samedi : annonce)
                FsmState sheet = d.Fsm.GetState("Hash to array 2"), sat = d.Fsm.GetState("Idle 4"), sun = d.Fsm.GetState("Hash to array");
                if (sheet != null) FsmHooks.Prepend(sheet, new Hook { Kind = 0, Day = 0 }); else Log.Warn("rallye (MSC) : etat Hash to array 2 absent");
                if (sat != null) FsmHooks.Prepend(sat, new Hook { Kind = 1, Day = 1 }); else Log.Warn("rallye (MSC) : etat Idle 4 absent");
                // (dimanche, podium : annonce, attente des autres, humains ajoutes, puis les actions du jeu -- en sequence)
                if (sun != null)
                {
                    FsmHooks.Prepend(sun, new Hook { Kind = 0, Day = 2 });
                    FsmHooks.Prepend(sun, new Hold());
                    FsmHooks.Prepend(sun, new Hook { Kind = 1, Day = 2 });
                    sun.IsSequence = true;
                }
                else Log.Warn("rallye (MSC) : etat Hash to array absent");
            }
            Log.Info("rallye (MSC) : participation suivie (" + d.ActiveStateName + ", classe " + Cls() + ")");
            return true;
        }

        static string Cls() { FsmString c = data != null ? data.FsmVariables.FindFsmString("CurrentClass") : null; return c != null && c.Value != null ? c.Value : ""; }

        public static void Update()
        {
            // (My Summer Car seulement : sur My Winter Car, la recherche du rallye de MSC coutait 32 a 39 ms toutes les 10 s --
            // a-coup regulier, journal de Menor 1.15.2, 25/10)
            if (!Game.Msc || !Session.Active || !PlayerSync.InGame) return;
            float now = Time.realtimeSinceStartup;
            WeekTick();
            if (Find())
            {
                if (!loaded) AfterLoad();
                if (loaded && !Session.IsHost && now >= nextStore) { nextStore = now + 10f; StoreGuest(); }
            }
            HookTrophies();
            Snapshots(now);
            StageSend(now);
        }

        static int GlobalDay() { FsmInt d = FsmVariables.GlobalVariables.FindFsmInt("GlobalDay"); return d != null ? d.Value : -1; }

        // Fin du week-end (GlobalDay 6 ou 7 -> lundi a vendredi) : temps des autres effaces (audit MSC 02/11 : `humans` n'etait vide
        // qu'au chargement -- un temps du week-end precedent, d'un pilote qui ne courait plus ou etait parti, revenait au podium
        // suivant et pouvait prendre l'or a celui d'ici). Les horloges suivent celle de l'hote : tout le monde efface en meme temps,
        // un temps du samedi ou du dimanche reste valable jusqu'au lundi. [Test] RallyeSemaine=0 : comme avant.
        static void WeekTick()
        {
            int d = GlobalDay();
            if (d < 1) return;
            if ((weekDay == 6 || weekDay == 7) && d < 6 && Config.GetInt("Test", "RallyeSemaine", 1) != 0)
            {
                bool any = humans.Count > 0 || mine[1] > 0f || mine[2] > 0f;
                humans.Clear(); lastSent = ""; myDay = 0; mine[1] = mine[2] = 0f;
                if (any) Log.Info("rallye (MSC) : nouveau week-end, temps des autres effaces");
            }
            weekDay = d;
        }

        // Classe d'apres le niveau (CurrentClass n'est pas sauve : « Junior » au chargement jusqu'au SATURDAY / SUNDAY du jeu ;
        // 'Class?' / 'State 4' : PlayerClassLevel >= RequiredProClass -> Amateur).
        static string LevelCls()
        {
            FsmInt lv = data != null ? data.FsmVariables.FindFsmInt("PlayerClassLevel") : null, req = data != null ? data.FsmVariables.FindFsmInt("RequiredProClass") : null;
            if (lv == null) return Cls();
            return lv.Value >= (req != null ? req.Value : 10) ? "Amateur" : "Junior";
        }

        // Une fois la participation chargee par le jeu ('State 2' -> 'Check day' -> 'Load 2' -> 'Rally unfinised?') : chez l'invite,
        // la sienne remise a la place de celle de l'hote ; chez chacun, un samedi deja couru (rechargement, reconnexion) est redit.
        static readonly HashSet<string> Loading = new HashSet<string> { "State 2", "Check day", "Load 2" };
        static void AfterLoad()
        {
            if (data == null || Loading.Contains(data.ActiveStateName)) return;
            PlayMakerFSM pen = Game.FsmOn(data.gameObject, "Penalties");
            if (pen != null && Loading.Contains(pen.ActiveStateName)) return;
            loaded = true;
            if (!Session.IsHost && Config.GetInt("Test", "RallyeInvite", 1) != 0) RestoreGuest();
            Recharge();
        }

        // Samedi deja couru ici (SecondDay vrai, temps cumule, week-end) apres un chargement : redit a tous (audit MSC 02/11 : apres
        // une reconnexion ou une reprise de la partie, les autres n'avaient plus le Time1 d'ici : leur podium du dimanche ne
        // l'attendait plus et ne le classait plus).
        static void Recharge()
        {
            FsmBool sd = data.FsmVariables.FindFsmBool("SecondDay");
            FsmFloat tot = data.FsmVariables.FindFsmFloat("PlayerTimeTotal");
            int gd = GlobalDay();
            if (sd == null || !sd.Value || tot == null || tot.Value <= 0f || (gd != 6 && gd != 7) || mine[1] > 0f) return;
            mine[1] = tot.Value; mine[2] = 0f; mineCls = LevelCls(); myDay = 1;
            lastSent = mineCls + ":1:" + tot.Value.ToString("F1");
            Log.Info("rallye (MSC) : samedi deja couru ici (" + tot.Value.ToString("F1") + " s, " + mineCls + ")" + (Session.RemoteCount > 0 ? ", redit aux autres" : ""));
            RedireA(null);
        }

        // Nos temps du week-end, a un joueur (hote : arrivant) ou a tous (null).
        static void RedireA(Peer p)
        {
            if (!Session.Active || Session.RemoteCount == 0) return;
            for (int d = 1; d <= 2; d++)
            {
                if (mine[d] <= 0f) continue;
                NetWriter w = new NetWriter(Msg.Job).U8(Session.LocalId).Str("@rallymsc").Str(Session.Me != null ? Session.Me.Name : "?").Str(mineCls).U8((byte)d).F32(mine[d]);
                if (p == null) Session.SendAll(w, true); else FireTools.SendTo(p, w);
            }
        }

        // Hote : a l'arrivee d'un joueur (PlayerSync.SnapshotAll, 20 s), nos temps et ceux des autres humains connus (leur numero en
        // tete, comme s'ils venaient d'eux ; l'arrivant n'est pas hote, il ne relaie pas). Pas les siens : il les a (sa participation).
        static readonly List<KeyValuePair<float, Peer>> snapshots = new List<KeyValuePair<float, Peer>>();
        public static void ScheduleSnapshot(Peer p) { if (Session.IsHost && p != null && Game.Msc) snapshots.Add(new KeyValuePair<float, Peer>(Time.realtimeSinceStartup + 20f, p)); }

        static void Snapshots(float now)
        {
            for (int i = snapshots.Count - 1; i >= 0; i--)
            {
                if (now < snapshots[i].Key) continue;
                Peer p = snapshots[i].Value; snapshots.RemoveAt(i);
                if (!p.Accepted || Session.T == null) continue;
                RedireA(p);
                int n = mine[1] > 0f || mine[2] > 0f ? 1 : 0;
                foreach (KeyValuePair<int, Human> kv in humans)
                {
                    Human h = kv.Value;
                    if (kv.Key == p.Id || (h.Time1 <= 0f && h.Time2 <= 0f)) continue;
                    if (h.Time1 > 0f) FireTools.SendTo(p, new NetWriter(Msg.Job).U8(kv.Key).Str("@rallymsc").Str(h.Name ?? "?").Str(h.Cls ?? "").U8(1).F32(h.Time1));
                    if (h.Time2 > 0f) FireTools.SendTo(p, new NetWriter(Msg.Job).U8(kv.Key).Str("@rallymsc").Str(h.Name ?? "?").Str(h.Cls ?? "").U8(2).F32(h.Time2));
                    n++;
                }
                if (n > 0) Log.Info("rallye (MSC) : temps du week-end de " + n + " pilote(s) envoyes a " + p);
            }
        }

        // Fin d'une journee ici : temps cumule et classe envoyes a tous (une fois par valeur).
        static void Announce(int day)
        {
            if (data == null) return;
            myDay = day;
            FsmFloat tot = data.FsmVariables.FindFsmFloat("PlayerTimeTotal");
            float t = tot != null ? tot.Value : 0f;
            if (t <= 0f) return;
            // (garde meme seul : redit aux arrivants -- avant, un temps fait sans personne n'etait jamais annonce)
            mine[day] = t; if (day == 1) mine[2] = 0f; mineCls = Cls();
            if (!Session.IsHost) StoreGuest();
            if (!Session.Active || Session.RemoteCount == 0) return;
            string key = Cls() + ":" + day + ":" + t.ToString("F1");
            if (key == lastSent) return;
            lastSent = key;
            Session.SendAll(new NetWriter(Msg.Job).U8(Session.LocalId).Str("@rallymsc").Str(Session.Me != null ? Session.Me.Name : "?").Str(Cls()).U8((byte)day).F32(t), true);
            Log.Info("rallye (MSC) : journee " + day + ", temps cumule " + t.ToString("F1") + " s (" + Cls() + ") envoye");
        }

        public static void OnMessage(int who, NetReader r)
        {
            string name = r.Str(), cls = r.Str();
            int day = r.U8();
            float time = r.F32();
            // (jamais soi-meme, meme sous son ancien numero apres une reconnexion : on serait son propre adversaire, cle MWC<soi>)
            if (who == Session.LocalId || Session.Me != null && name == Session.Me.Name) return;
            // (meme pseudo sous un autre numero : le joueur est revenu -- son ancienne entree retiree, sinon deux fois au podium)
            int old = -1;
            foreach (KeyValuePair<int, Human> kv in humans) if (kv.Key != who && kv.Value.Name == name) old = kv.Key;
            if (old >= 0) humans.Remove(old);
            Human h;
            if (!humans.TryGetValue(who, out h) || h.Cls != cls) humans[who] = h = new Human { Cls = cls };   // (autre classe : nouveau week-end)
            h.Name = name;
            if (day == 2) h.Time2 = time; else { h.Time1 = time; h.Time2 = 0f; }
            Log.Info("rallye (MSC) : " + name + " (#" + who + ") journee " + day + " : " + time.ToString("F1") + " s (" + cls + ")");
        }

        // [Test] Autotest=mscpodium (24/10, attente du podium, a deux) : RALLY/ResultsWeekend allume (hors week-end) ; l'invite
        // annonce un faux temps du samedi a 35 s et celui du dimanche a [Test] PodiumDimanche s (75 par defaut, 0 = jamais) ; l'hote
        // entre dans 'Hash to array' a [Test] PodiumA s (50) : il doit attendre l'invite (ou le delai), puis classer.
        static int pStep;
        public static void TestPodium(float t)
        {
            if (t > 20f && t < 200f)
            {
                GameObject rw = Game.FindAny("RALLY/ResultsWeekend");
                for (Transform p = rw != null ? rw.transform : null; p != null; p = p.parent) if (!p.gameObject.activeSelf) p.gameObject.SetActive(true);
                Find();
            }
            if (!Session.IsHost)
            {
                float d2 = Config.GetFloat("Test", "PodiumDimanche", 75f);
                if (pStep == 0 && t > 35f) { pStep = 1; SendTime(1, 700f); }
                if (pStep == 1 && d2 > 0f && t > d2) { pStep = 2; SendTime(2, 1400f); }
                return;
            }
            if (pStep == 0 && t > Config.GetFloat("Test", "PodiumA", 50f) && data != null)   // ([Test] PodiumA=s, audit MSC 02/11 : 50 par defaut)
            {
                pStep = 1;
                Log.Info("autotest mscpodium : humains " + humans.Count + ", attendus " + Names(Pending()) + ", classe " + Cls() + " ; entree dans Hash to array");
                Game.SetState(data, "Hash to array");
            }
            if (pStep == 1 && data != null && Time.frameCount % 120 == 0) Log.Info("autotest mscpodium : Data [" + data.ActiveStateName + "]");
        }
        static void SendTime(int day, float time)
        {
            Session.SendAll(new NetWriter(Msg.Job).U8(Session.LocalId).Str("@rallymsc").Str(Session.Me != null ? Session.Me.Name : "?").Str(Cls()).U8((byte)day).F32(time), true);
            Log.Info("autotest mscpodium : temps du jour " + day + " (" + time + " s, classe " + Cls() + ") envoye");
        }

        // [Test] Autotest=msctrophee (24/10, prix du rallye) : le joueur [Test] Monteur (0 = hote) envoie a 40 s SPAWNITEM a
        // Spawner/CreateTrophiesAmateur :: Gold, comme le podium du jeu ('Player position' -> 'Gold') ; a 60 s chacun compte ses
        // trophees d'or (objets trophygold*) et leur position.
        static int tStep;
        // Essai (Autotest=mscdepart, Jani 31/10 : « le chrono du depart ne demarre pas ») : a DepartA s (40, hote ; invite : son horloge),
        // RALLY/Saturday allume des deux cotes ; [Test] DepartPilote=hote|invite monte dans DepartVoiture (SATSUMA) a DepartA+2,
        // la pose 6 m avant la zone Lines/Staging a DepartA+6 et la pousse dedans a 2 m/s 3 s ; chacun note toutes les 2 s l'etat de
        // Staging, Countdown, Clock (Start, TimeTotal) et Timing jusqu'a DepartA+60.
        static int dStep; static float dLog;
        public static void TestStart(float t)
        {
            float a = Config.GetFloat("Test", "DepartA", 40f) - (Session.IsHost ? 0f : Config.GetFloat("Test", "DepartDecalage", 70f));
            bool pilot = Config.Get("Test", "DepartPilote", "hote") == (Session.IsHost ? "hote" : "invite");
            string car = Config.Get("Test", "DepartVoiture", "SATSUMA(557kg, 248)");
            GameObject sat = Game.FindAny("RALLY/Saturday");
            if (dStep == 0 && t > a)
            {
                dStep = 1;
                if (sat != null) { for (Transform x = sat.transform; x != null; x = x.parent) if (!x.gameObject.activeSelf) x.gameObject.SetActive(true); }
                Log.Info("autotest : depart, RALLY/Saturday " + (sat != null ? "allume" : "introuvable"));
            }
            if (!pilot) { DepartLog(t, a); return; }
            if (dStep == 1 && t > a + 2f) { dStep = 2; Log.Info("autotest : depart, " + VehicleSync.TestEnter(car, false)); }
            if (dStep == 2 && t > a + 4f) { dStep = 3; Log.Info("autotest : depart, au volant : " + VehicleSync.TestEnter(car, true)); }
            Transform st = sat != null ? sat.transform.Find("TimingSaturday/Lines/Staging") : null;
            Rigidbody cb = VehicleSync.Body(car);
            if (dStep == 3 && t > a + 6f && st != null && cb != null)
            {
                dStep = 4;
                Vector3 back = -st.forward; back.y = 0f;
                Vector3 to = st.position + back.normalized * 6f + Vector3.up * 1f;
                Log.Info("autotest : depart, " + VehicleSync.TestShift(car, to - cb.position) + " (Staging en " + st.position.ToString("F1") + ")");
                cb.transform.rotation = Quaternion.LookRotation(new Vector3(st.forward.x, 0f, st.forward.z));
            }
            if (dStep == 4 && t > a + 8f && t < a + 11f && cb != null && st != null) { Vector3 f = st.forward; f.y = 0f; cb.velocity = f.normalized * 2f + Vector3.up * Mathf.Min(cb.velocity.y, 0f); }
            DepartLog(t, a);
        }
        static void DepartLog(float t, float a)
        {
            if (t < a + 5f || t > a + 60f || Time.realtimeSinceStartup < dLog) return;
            dLog = Time.realtimeSinceStartup + 2f;
            var sb = new System.Text.StringBuilder("autotest : depart t=" + t.ToString("F0"));
            foreach (string[] s in new[] { new[] { "RALLY/Saturday/TimingSaturday/Lines/Staging", "Checkpoint" }, new[] { "RALLY/Saturday/TimingSaturday", "Countdown" }, new[] { "RALLY/Saturday/TimingSaturday", "Clock" }, new[] { "RALLY/Saturday/TimingSaturday", "Timing" } })
            {
                GameObject fo = Game.FindAny(s[0]); PlayMakerFSM f = fo != null ? Game.FsmOn(fo, s[1]) : null;
                sb.Append(" | ").Append(s[1]).Append(' ');
                if (f == null) { sb.Append('?'); continue; }
                sb.Append(f.ActiveStateName).Append(f.enabled ? "" : " (coupe)");
                if (s[1] == "Clock") { FsmBool b = f.FsmVariables.FindFsmBool("Start"); FsmFloat tt = f.FsmVariables.FindFsmFloat("TimeTotal"); sb.Append(" Start=").Append(b != null && b.Value).Append(" T=").Append(tt != null ? tt.Value.ToString("F1") : "?"); }
                if (s[1] == "Countdown") { FsmFloat w = f.FsmVariables.FindFsmFloat("Time"); sb.Append(" Time=").Append(w != null ? w.Value.ToString("F1") : "?"); }
            }
            Log.Info(sb.ToString());
        }

        public static void Test(float t)
        {
            if (tStep == 0 && t > 40f)
            {
                tStep = 1;
                if (Session.LocalId != Config.GetInt("Test", "Monteur", 0)) return;
                GameObject sp = Game.FindAny("Spawner/CreateTrophiesAmateur");
                PlayMakerFSM g = sp != null ? Game.FsmOn(sp, "Gold") : null;
                if (g != null) g.SendEvent("SPAWNITEM");
                Log.Info("autotest msctrophee : SPAWNITEM -> " + (g != null ? "Gold (" + g.ActiveStateName + ")" : "Spawner/CreateTrophiesAmateur::Gold introuvable"));
            }
            if (tStep == 1 && t > 60f)
            {
                tStep = 2;
                var sb = new System.Text.StringBuilder();
                int n = 0;
                foreach (GameObject go in Resources.FindObjectsOfTypeAll(typeof(GameObject)))
                    if (go.name.ToLowerInvariant().Contains("troph") && go.hideFlags == HideFlags.None && go.transform.parent == null)
                    { n++; sb.Append(' ').Append(go.name).Append(go.transform.position.ToString("F1")).Append(go.activeInHierarchy ? "" : " (eteint)"); }
                GameObject sp2 = Game.FindAny("Spawner/CreateTrophiesAmateur");
                PlayMakerFSM g2 = sp2 != null ? Game.FsmOn(sp2, "Gold") : null;
                FsmInt cnt = g2 != null ? g2.FsmVariables.FindFsmInt("ObjectNumberInt") : null;
                Log.Info("autotest msctrophee : " + n + " objet(s) trophee a la racine ici :" + sb + " ; Gold " + (g2 != null ? g2.ActiveStateName + ", ObjectNumberInt " + (cnt != null ? cnt.Value.ToString() : "?") : "introuvable"));
                // ([Test] TropheeCapture=1 : l'hote se place a 1,6 m du trophee, face a lui, capture dumps/ecran-trophee.png)
                if (Session.IsHost && Config.GetInt("Test", "TropheeCapture", 0) != 0)
                {
                    GameObject tr = null;
                    foreach (GameObject go in Resources.FindObjectsOfTypeAll(typeof(GameObject)))
                        if (go.name == "1st place trophy(itemx)" && go.activeInHierarchy && go.hideFlags == HideFlags.None) tr = go;
                    GameObject pl = Game.PlayerGo;
                    var cc = pl != null ? pl.GetComponent<CharacterController>() : null;
                    if (tr != null && cc != null)
                    {
                        Vector3 at = tr.transform.position + new Vector3(0f, 0f, -1.6f);
                        cc.enabled = false; pl.transform.position = at + Vector3.up * 0.2f; pl.transform.rotation = Quaternion.LookRotation(Vector3.forward); cc.enabled = true;
                        Autotest.CaptureSoon("trophee", 2f);
                    }
                    Log.Info("autotest msctrophee : capture " + (tr != null ? "devant le trophee" : "sans trophee"));
                }
            }
        }

        // Classement affiche ici : les autres humains de ma classe dans les tables, avant que le jeu ne les copie dans les listes.
        static void InjectHumans(int day)
        {
            if (names == null || times == null) return;
            if (day == 0) day = myDay > 0 ? myDay : 1;
            // (audit MSC 02/11 : les cles MWC<id> d'avant, sauvees avec les tables -- 'Save time' : HashTableEasySave Names / Times --
            // et relues au chargement ('Load 2'), restaient : le dimanche, 'Add index 2' les traitait en pilotes IA, un fantome au
            // temps invente pouvait prendre une place du podium ; chez l'invite, sa propre cle (sauvegarde de l'hote) : un double de
            // lui-meme. Toutes retirees ici, puis celles des humains connus remises. Cles du jeu : des nombres, jamais « MWC ».)
            if (Config.GetInt("Test", "RallyeFantomes", 1) != 0) StripHumans();
            string cls = Cls();
            int n = 0;
            foreach (KeyValuePair<int, Human> kv in humans)
            {
                Human h = kv.Value;
                string key = "MWC" + kv.Key;
                float t = day == 2 ? h.Time2 : h.Time1;
                // (pas de temps pour cette journee : retire s'il etait la -- le samedi seul ne compte pas au podium du dimanche)
                if (t <= 0f || h.Cls != cls) { names.Remove(key); times.Remove(key); continue; }
                names[key] = h.Name; times[key] = t;
                n++;
            }
            if (n > 0) Log.Info("rallye (MSC) : classement d'ici avec " + n + " autre(s) pilote(s) humain(s) (" + cls + ")");
        }

        static int StripHumans()
        {
            if (names == null || times == null) return 0;
            var drop = new List<object>();
            foreach (object k in names.Keys) { string s = k as string; if (s != null && s.StartsWith("MWC")) drop.Add(k); }
            foreach (object k in times.Keys) { string s = k as string; if (s != null && s.StartsWith("MWC") && !drop.Contains(k)) drop.Add(k); }
            foreach (object k in drop) { names.Remove(k); times.Remove(k); }
            return drop.Count;
        }

        // ---------------------------------------------------------------- invite : participation gardee par monde
        // (audit MSC 02/11) La sauvegarde recue a chaque connexion est celle de l'hote : 'Load 2' y relisait SA participation
        // (RallyClass -> PlayerClassLevel, RallyDay2 -> SecondDay, RallyTimePlayer -> PlayerTimeTotal, penalites, tables) : la classe
        // gagnee par l'invite (+5 par or) revenait a celle de l'hote, son samedi etait perdu, ou il heritait de celui de l'hote
        // (inscription du dimanche gratuite, 'Check day' -> 'Good name 2', avec le temps de l'hote). Comme Rally (MWC, rallye.ini)
        // et le porte-monnaie : sa participation gardee par monde (PlayerID de l'hote) dans rallye-msc.ini, ecrite toutes les 10 s
        // et a chaque fin de journee, remise au chargement ; sans ligne pour ce monde (premiere venue) : participation neuve.
        // [Test] RallyeInvite=0 : comme avant (participation de l'hote).
        static float nextStore;
        static string storedLine;
        static readonly System.Globalization.CultureInfo Inv = System.Globalization.CultureInfo.InvariantCulture;
        static string StorePath { get { return System.IO.Path.Combine(Log.DataDir, "rallye-msc.ini"); } }
        static string World() { FsmFloat pid = FsmVariables.GlobalVariables.FindFsmFloat("PlayerID"); return pid != null && pid.Value != 0f ? pid.Value.ToString("F0") : ""; }

        static string GuestLine()
        {
            var sb = new System.Text.StringBuilder();
            FsmInt lv = data.FsmVariables.FindFsmInt("PlayerClassLevel");
            FsmFloat tot = data.FsmVariables.FindFsmFloat("PlayerTimeTotal");
            FsmBool sd = data.FsmVariables.FindFsmBool("SecondDay");
            if (lv != null) sb.Append("PlayerClassLevel:").Append(lv.Value).Append(';');
            if (tot != null) sb.Append("PlayerTimeTotal:").Append(tot.Value.ToString("R", Inv)).Append(';');
            if (sd != null) sb.Append("SecondDay:").Append(sd.Value ? 1 : 0).Append(';');
            PlayMakerFSM pen = Game.FsmOn(data.gameObject, "Penalties");
            if (pen != null)
            {
                FsmFloat o = pen.FsmVariables.FindFsmFloat("OverallTimePenalty"); FsmInt j = pen.FsmVariables.FindFsmInt("PenaltyJumpstart"), p = pen.FsmVariables.FindFsmInt("PenaltyParcferme");
                if (o != null) sb.Append("pen.OverallTimePenalty:").Append(o.Value.ToString("R", Inv)).Append(';');
                if (j != null) sb.Append("pen.PenaltyJumpstart:").Append(j.Value).Append(';');
                if (p != null) sb.Append("pen.PenaltyParcferme:").Append(p.Value).Append(';');
            }
            return sb.ToString();
        }

        static void StoreGuest()
        {
            if (Session.IsHost || !loaded || data == null || Config.GetInt("Test", "RallyeInvite", 1) == 0) return;
            string w = World();
            if (w.Length == 0) return;
            string line = GuestLine();
            if (line == storedLine) return;
            storedLine = line;
            var lines = new List<string>();
            try
            {
                if (System.IO.File.Exists(StorePath)) foreach (string l in System.IO.File.ReadAllLines(StorePath)) if (!l.StartsWith(w + "=")) lines.Add(l);
                lines.Add(w + "=" + line);
                System.IO.File.WriteAllLines(StorePath, lines.ToArray());
            }
            catch (System.Exception e) { Log.Warn("rallye (MSC) : participation pas ecrite (" + e.GetType().Name + ")"); }
        }

        static void RestoreGuest()
        {
            string w = World(), line = null;
            if (w.Length == 0) { Log.Warn("rallye (MSC) : PlayerID inconnu, participation de l'hote gardee ici"); return; }
            try { if (System.IO.File.Exists(StorePath)) foreach (string l in System.IO.File.ReadAllLines(StorePath)) if (l.StartsWith(w + "=")) line = l.Substring(w.Length + 1); }
            catch (System.Exception e) { Log.Warn("rallye (MSC) : participation pas relue (" + e.GetType().Name + ")"); }
            var vals = new Dictionary<string, string>();
            if (line != null) foreach (string kv in line.Split(';')) { int c = kv.IndexOf(':'); if (c > 0) vals[kv.Substring(0, c)] = kv.Substring(c + 1); }
            string s; int i; float f;
            FsmInt lv = data.FsmVariables.FindFsmInt("PlayerClassLevel");
            FsmFloat tot = data.FsmVariables.FindFsmFloat("PlayerTimeTotal"), pt = data.FsmVariables.FindFsmFloat("PlayerTime");
            FsmBool sd = data.FsmVariables.FindFsmBool("SecondDay");
            int hostLv = lv != null ? lv.Value : -1; bool hostSd = sd != null && sd.Value;
            if (lv != null) lv.Value = vals.TryGetValue("PlayerClassLevel", out s) && int.TryParse(s, out i) ? i : 1;   // (premiere venue : niveau d'une partie neuve)
            if (tot != null) tot.Value = vals.TryGetValue("PlayerTimeTotal", out s) && float.TryParse(s, System.Globalization.NumberStyles.Float, Inv, out f) ? f : 0f;
            if (pt != null && line == null) pt.Value = 0f;
            // (samedi couru hors du week-end en cours : comme 'Weekend?' du jeu, plus de deuxieme journee)
            int gd = GlobalDay();
            if (sd != null) sd.Value = vals.TryGetValue("SecondDay", out s) && s == "1" && (gd == 6 || gd == 7);
            PlayMakerFSM pen = Game.FsmOn(data.gameObject, "Penalties");
            if (pen != null)
                foreach (string pn in new[] { "OverallTimePenalty", "PenaltyJumpstart", "PenaltyParcferme" })
                {
                    FsmFloat pf = pen.FsmVariables.FindFsmFloat(pn); FsmInt pi = pen.FsmVariables.FindFsmInt(pn);
                    bool has = vals.TryGetValue("pen." + pn, out s);
                    if (pf != null) pf.Value = has && float.TryParse(s, System.Globalization.NumberStyles.Float, Inv, out f) ? f : 0f;
                    else if (pi != null) pi.Value = has && int.TryParse(s, out i) ? i : 0;
                }
            // Sa ligne dans les tables (cle PlayerIDstring, relue de la sauvegarde de l'hote : celle de l'hote) : son temps du samedi,
            // sinon retiree ; et les cles MWC<id> d'avant.
            FsmString pid = data.FsmVariables.FindFsmString("PlayerIDstring");
            if (names != null && times != null && pid != null && !string.IsNullOrEmpty(pid.Value))
            {
                if (sd != null && sd.Value && tot != null)
                {
                    times[pid.Value] = tot.Value;
                    FsmString pn = FsmVariables.GlobalVariables.FindFsmString("PlayerName");
                    if (!names.ContainsKey(pid.Value)) names[pid.Value] = pn != null ? pn.Value : " ";
                }
                else { names.Remove(pid.Value); times.Remove(pid.Value); }
            }
            int ghosts = StripHumans();
            storedLine = GuestLine();
            Log.Info("rallye (MSC) : participation d'ici " + (line != null ? "retrouvee" : "neuve (premiere venue dans ce monde)") + " : niveau " + (lv != null ? lv.Value.ToString() : "?")
                     + ", samedi couru " + (sd != null && sd.Value) + ", temps " + (tot != null ? tot.Value.ToString("F1") : "?") + " (sauvegarde de l'hote : niveau " + hostLv + ", samedi " + hostSd + ")"
                     + (ghosts > 0 ? ", " + ghosts + " cle(s) MWC retiree(s)" : ""));
        }

        // ---------------------------------------------------------------- invite en speciale : voitures IA retenues chez l'hote
        // (audit MSC 02/11) Chez l'invite : chrono de sa speciale (TimingSaturday / TimingSunday :: Clock en 'Get time') envoye a
        // l'hote chaque seconde (Msg.Race K_MSCSTAGE, non fiable). Chez l'hote : Races.HoldTiming lit StageHold (le plus petit temps
        // recu depuis moins de 2,5 s ce jour-la). Comme Rally.StageTick sur My Winter Car, sans ecrire dans le Clock de l'hote.
        static readonly string[] ClockPaths = { null, "RALLY/Saturday/TimingSaturday", "RALLY/Sunday/TimingSunday" };
        static readonly PlayMakerFSM[] clocks = new PlayMakerFSM[3];
        static float nextStageSend, nextClockLook;
        static readonly Dictionary<int, float[]> stageBy = new Dictionary<int, float[]>();   // hote : joueur -> (-, temps samedi, temps dimanche, -, recu samedi, recu dimanche)
        static readonly bool[] stageLogged = new bool[3];

        static void StageSend(float now)
        {
            if (Session.IsHost || now < nextStageSend) return;
            nextStageSend = now + 1f;
            bool look = now >= nextClockLook;
            if (look) nextClockLook = now + 5f;
            for (int d = 1; d <= 2; d++)
            {
                if (clocks[d] == null && look) { GameObject go = Game.FindAny(ClockPaths[d]); clocks[d] = go != null ? Game.FsmOn(go, "Clock") : null; }
                PlayMakerFSM c = clocks[d];
                if (c == null || !c.gameObject.activeInHierarchy || c.ActiveStateName != "Get time") continue;
                FsmFloat el = c.FsmVariables.FindFsmFloat("TimeTotalFPS");
                if (el != null) Session.SendToHost(new NetWriter(Msg.Race).U8(Races.K_MSCSTAGE).U8(d).F32(el.Value), false);
            }
        }

        public static void OnStage(int who, int day, float el)
        {
            if (day < 1 || day > 2 || float.IsNaN(el) || el < 0f) return;
            float[] s;
            if (!stageBy.TryGetValue(who, out s)) stageBy[who] = s = new float[6];
            s[day] = el; s[3 + day] = Time.realtimeSinceStartup;
        }

        // Hote : plus petit temps de speciale d'un invite qui court la journee 'day' (recu depuis moins de 2,5 s), sinon -1.
        public static float StageHold(int day)
        {
            if (day < 1 || day > 2 || stageBy.Count == 0) return -1f;
            float now = Time.realtimeSinceStartup, best = -1f;
            foreach (KeyValuePair<int, float[]> kv in stageBy)
                if (now - kv.Value[3 + day] < 2.5f && (best < 0f || kv.Value[day] < best)) best = kv.Value[day];
            if ((best >= 0f) != stageLogged[day])
            {
                stageLogged[day] = best >= 0f;
                Log.Info("rallye (MSC) : " + (best >= 0f ? "un invite court (" + (day == 1 ? "samedi" : "dimanche") + ", " + best.ToString("F0") + " s) : depart des voitures IA retenu ici" : "plus d'invite en speciale (" + (day == 1 ? "samedi" : "dimanche") + ")"));
            }
            return best;
        }

        // ---------------------------------------------------------------- essais (audit MSC 02/11)
        // [Test] Autotest=mscrallyeinvite (a deux) : a 30 s l'invite allume RALLY/ResultsWeekend (comme TestPodium), pose
        // PlayerClassLevel=[Test] RallyeNiveau (7), SecondDay vrai, PlayerTimeTotal=700 et une cle fantome MWC9 dans Names / Times,
        // puis ecrit sa participation (StoreGuest) ; a 40 s il la remet comme au chargement (RestoreGuest, comme apres la sauvegarde
        // de l'hote) apres avoir pose les valeurs de l'hote (niveau 1, samedi faux, 0 s) et journalise. L'hote, lui, journalise a
        // 60 s les humains connus (le samedi de l'invite redit par Recharge / RedireA).
        static int gStep;
        public static string TestGuestStore(float t)
        {
            if (t > 20f && t < 200f)
            {
                GameObject rw = Game.FindAny("RALLY/ResultsWeekend");
                for (Transform p = rw != null ? rw.transform : null; p != null; p = p.parent) if (!p.gameObject.activeSelf) p.gameObject.SetActive(true);
                Find();
            }
            if (data == null) return null;
            FsmInt lv = data.FsmVariables.FindFsmInt("PlayerClassLevel"); FsmFloat tot = data.FsmVariables.FindFsmFloat("PlayerTimeTotal"); FsmBool sd = data.FsmVariables.FindFsmBool("SecondDay");
            if (Session.IsHost)
            {
                if (gStep == 0 && t > 60f) { gStep = 1; string r = "autotest mscrallyeinvite : hote, humains " + HumansText(); Log.Info(r); return r; }
                return null;
            }
            if (gStep == 0 && t > 30f && lv != null && tot != null && sd != null)
            {
                gStep = 1; loaded = true;
                FsmInt gd = FsmVariables.GlobalVariables.FindFsmInt("GlobalDay"); if (gd != null && gd.Value != 6 && gd.Value != 7) gd.Value = 6;
                lv.Value = Config.GetInt("Test", "RallyeNiveau", 7); sd.Value = true; tot.Value = 700f;
                if (names != null && times != null) { names["MWC9"] = "fantome"; times["MWC9"] = 100f; }
                storedLine = null; StoreGuest();
                Log.Info("autotest mscrallyeinvite : participation ecrite (" + storedLine + ")");
            }
            if (gStep == 1 && t > 40f)
            {
                gStep = 2;
                lv.Value = 1; sd.Value = false; tot.Value = 0f;   // (comme la sauvegarde de l'hote relue par 'Load 2')
                mine[1] = mine[2] = 0f;
                RestoreGuest(); Recharge();
                string r = "autotest mscrallyeinvite : apres la remise, niveau " + lv.Value + ", samedi " + sd.Value + ", temps " + tot.Value.ToString("F1")
                           + ", MWC9 " + (names != null && names.ContainsKey("MWC9") ? "encore la" : "retiree") + ", annonce samedi " + mine[1].ToString("F1");
                Log.Info(r);
                return r;
            }
            return null;
        }

        // [Test] Autotest=mscpodiumsemaine (hote seul ou a deux) : a 30 s, un faux humain #9 (Time1 700, Time2 1400, classe d'ici) et
        // GlobalDay 7 ; a 32 s GlobalDay 1 (lundi) ; a 34 s le nombre d'humains (attendu : 0) et, en entrant dans 'Hash to array 2'
        // avec une cle fantome MWC8 dans les tables, sa presence (attendu : retiree).
        static int wStep;
        public static string TestWeek(float t)
        {
            if (t > 20f && t < 200f)
            {
                GameObject rw = Game.FindAny("RALLY/ResultsWeekend");
                for (Transform p = rw != null ? rw.transform : null; p != null; p = p.parent) if (!p.gameObject.activeSelf) p.gameObject.SetActive(true);
                Find();
            }
            FsmInt gd = FsmVariables.GlobalVariables.FindFsmInt("GlobalDay");
            if (gd == null) return null;
            if (wStep == 0 && t > 30f) { wStep = 1; humans[9] = new Human { Name = "fantome", Cls = Cls(), Time1 = 700f, Time2 = 1400f }; gd.Value = 7; Log.Info("autotest mscpodiumsemaine : humain #9, dimanche"); }
            if (wStep == 1 && t > 32f) { wStep = 2; gd.Value = 1; Log.Info("autotest mscpodiumsemaine : lundi"); }
            if (wStep == 2 && t > 34f)
            {
                wStep = 3;
                if (names != null && times != null) { names["MWC8"] = "fantome"; times["MWC8"] = 100f; }
                InjectHumans(1);
                string r = "autotest mscpodiumsemaine : humains " + humans.Count + " (attendu 0), MWC8 " + (names != null && names.ContainsKey("MWC8") ? "encore la" : "retiree");
                Log.Info(r);
                return r;
            }
            return null;
        }

        static string HumansText()
        {
            var sb = new System.Text.StringBuilder();
            foreach (KeyValuePair<int, Human> kv in humans) sb.Append(kv.Value.Name).Append(" #").Append(kv.Key).Append(' ').Append(kv.Value.Cls).Append(' ').Append(kv.Value.Time1.ToString("F1")).Append('/').Append(kv.Value.Time2.ToString("F1")).Append("; ");
            return sb.Length > 0 ? sb.ToString() : "aucun";
        }
    }
}
