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
    // PlayerIDstring ('Player name' / 'Player time', puis 'Player name 2' / 'Player time 2' le dimanche), classement bati dans
    // 'Hash to array 2' (Names / Times -> NamesArray / TimesArray, tri). Les voitures IA et la table des pilotes IA (RallyCars ::
    // Results) sont deja partagees par Races.
    // Ici : chaque joueur annonce son temps cumule (PlayerTimeTotal) et sa classe a la fin de chaque journee ('Idle 4' le
    // samedi, 'Hash to array' le dimanche) ; a l'entree de 'Hash to array 2' chez chacun, les autres humains de sa classe sont
    // ajoutes aux tables Names / Times (cle MWC<id>, valeur le pseudo / le temps) : ils apparaissent dans le classement, et le
    // podium du jeu en tient compte. Rien d'autre n'est touche (les adversaires au hasard restent ceux de chacun). A verifier en
    // jeu sur un vrai week-end de rallye : essai impossible a automatiser (deux journees de speciale).
    public static class RallyMsc
    {
        class Human { public string Name, Cls; public float Time; public int Day; }
        static readonly Dictionary<int, Human> humans = new Dictionary<int, Human>();
        static PlayMakerFSM data;
        static Hashtable names, times;
        static float nextLook;
        static bool hooked;
        static string lastSent = "";

        public static void OnLevelLoaded() { humans.Clear(); data = null; names = times = null; nextLook = 0f; hooked = false; lastSent = ""; trophiesHooked = false; nextTrophyLook = 0f; }

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
            public int Kind;   // 0 : Hash to array 2 (classement) ; 1 : fin de journee (annonce)
            public override void OnEnter()
            {
                try { if (Kind == 0) InjectHumans(); else Announce(); } catch (System.Exception e) { Replay.HookError(e); }
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
                foreach (string st in new[] { "Hash to array 2" }) { FsmState s = d.Fsm.GetState(st); if (s != null) FsmHooks.Prepend(s, new Hook { Kind = 0 }); else Log.Warn("rallye (MSC) : etat " + st + " absent"); }
                foreach (string st in new[] { "Idle 4", "Hash to array" }) { FsmState s = d.Fsm.GetState(st); if (s != null) FsmHooks.Prepend(s, new Hook { Kind = 1 }); else Log.Warn("rallye (MSC) : etat " + st + " absent"); }
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
        static void Announce()
        {
            if (data == null || !Session.Active || Session.RemoteCount == 0) return;
            FsmFloat tot = data.FsmVariables.FindFsmFloat("PlayerTimeTotal");
            FsmBool d2 = data.FsmVariables.FindFsmBool("SecondDay");
            float t = tot != null ? tot.Value : 0f;
            if (t <= 0f) return;
            int day = d2 != null && d2.Value ? 2 : 1;
            string key = Cls() + ":" + day + ":" + t.ToString("F1");
            if (key == lastSent) return;
            lastSent = key;
            Session.SendAll(new NetWriter(Msg.Job).U8(Session.LocalId).Str("@rallymsc").Str(Session.Me != null ? Session.Me.Name : "?").Str(Cls()).U8((byte)day).F32(t), true);
            Log.Info("rallye (MSC) : journee " + day + ", temps cumule " + t.ToString("F1") + " s (" + Cls() + ") envoye");
        }

        public static void OnMessage(int who, NetReader r)
        {
            var h = new Human { Name = r.Str(), Cls = r.Str(), Day = r.U8(), Time = r.F32() };
            humans[who] = h;
            Log.Info("rallye (MSC) : " + h.Name + " (#" + who + ") journee " + h.Day + " : " + h.Time.ToString("F1") + " s (" + h.Cls + ")");
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
            }
        }

        // Classement affiche ici : les autres humains de ma classe dans les tables, avant que le jeu ne les copie dans les listes.
        static void InjectHumans()
        {
            if (names == null || times == null) return;
            string cls = Cls();
            int n = 0;
            foreach (KeyValuePair<int, Human> kv in humans)
            {
                Human h = kv.Value;
                if (h.Time <= 0f || h.Cls != cls) continue;
                string key = "MWC" + kv.Key;
                names[key] = h.Name; times[key] = h.Time;
                n++;
            }
            if (n > 0) Log.Info("rallye (MSC) : classement d'ici avec " + n + " autre(s) pilote(s) humain(s) (" + cls + ")");
        }
    }
}
