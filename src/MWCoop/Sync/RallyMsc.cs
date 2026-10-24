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

        public static void OnLevelLoaded() { humans.Clear(); data = null; names = times = null; nextLook = 0f; hooked = false; lastSent = ""; trophiesHooked = false; nextTrophyLook = 0f; myDay = 0; }

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
            if (!Session.Active || !PlayerSync.InGame) return;
            Find();
            HookTrophies();
        }

        // Fin d'une journee ici : temps cumule et classe envoyes a tous (une fois par valeur).
        static void Announce(int day)
        {
            if (data == null) return;
            myDay = day;
            if (!Session.Active || Session.RemoteCount == 0) return;
            FsmFloat tot = data.FsmVariables.FindFsmFloat("PlayerTimeTotal");
            float t = tot != null ? tot.Value : 0f;
            if (t <= 0f) return;
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
            Human h;
            if (!humans.TryGetValue(who, out h) || h.Cls != cls) humans[who] = h = new Human { Cls = cls };   // (autre classe : nouveau week-end)
            h.Name = name;
            if (day == 2) h.Time2 = time; else { h.Time1 = time; h.Time2 = 0f; }
            Log.Info("rallye (MSC) : " + name + " (#" + who + ") journee " + day + " : " + time.ToString("F1") + " s (" + cls + ")");
        }

        // [Test] Autotest=mscpodium (24/10, attente du podium, a deux) : RALLY/ResultsWeekend allume (hors week-end) ; l'invite
        // annonce un faux temps du samedi a 35 s et celui du dimanche a [Test] PodiumDimanche s (75 par defaut, 0 = jamais) ; l'hote
        // entre dans 'Hash to array' a 50 s : il doit attendre l'invite (ou le delai), puis classer.
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
            if (pStep == 0 && t > 50f && data != null)
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
    }
}
