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

        public static void OnLevelLoaded() { humans.Clear(); data = null; names = times = null; nextLook = 0f; hooked = false; lastSent = ""; }

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
