using System.Collections;
using System.Collections.Generic;
using HutongGames.PlayMaker;
using MWCoop.Net;
using UnityEngine;

namespace MWCoop
{
    // Piste de dragsters de My Summer Car a plusieurs pilotes humains (24/10). Le classement du jeu (feuille RESULTS des tentes,
    // Sheets/DragResults) est tenu par DRAGRACE/LOD/DRAGSTRIP/DragTiming : table 'Results' (identifiant de concurrent ->
    // temps total, reaction comprise) et listes 'Names' / 'Cars' / 'Speeds' rangees par identifiant ; l'automate 'Leaderboard'
    // les relit toutes les 4 s ('State 1'), trie les temps et remplit ResultsNames / ResultsTimes / ResultsSpeeds / ResultsCars
    // (12 lignes). L'automate 'Results' n'y entre un concurrent que par son etat 'Leaderboard' (premier temps ou meilleur temps).
    // Ici : chaque joueur annonce son meilleur temps quand le jeu l'y entre pour lui (Name == {PlayerName}) ; chez les autres, a
    // chaque 'State 1' de 'Leaderboard', chaque pilote humain recu est un concurrent de plus (identifiant au-dela des listes du
    // jeu, sous son pseudo) : il apparait sur la feuille RESULTS de tous. Les temps des pilotes IA restent ceux de chacun.
    public static class DragMsc
    {
        class Human { public string Name, Car, Speed; public float Time; public int Index = -1; }
        static readonly Dictionary<int, Human> humans = new Dictionary<int, Human>();
        static PlayMakerFSM results, board;
        static Hashtable table;
        static ArrayList names, cars, speeds;
        static float nextLook, best = float.MaxValue;
        static bool hooked;

        public static void OnLevelLoaded() { humans.Clear(); results = board = null; table = null; names = cars = speeds = null; nextLook = 0f; hooked = false; best = float.MaxValue; }

        class Hook : ModHook
        {
            public override string Module { get { return "dragsters (MSC)"; } }
            public int Kind;   // 0 : 'Leaderboard' de Results (temps entre au classement) ; 1 : 'State 1' de Leaderboard (relecture)
            public override void OnEnter()
            {
                try { if (Kind == 0) Announce(); else Inject(); } catch (System.Exception e) { Replay.HookError(e); }
                Finish();
            }
        }

        static bool Find()
        {
            if (board != null && results != null) return true;
            if (Time.realtimeSinceStartup < nextLook) return false;
            nextLook = Time.realtimeSinceStartup + 5f;
            GameObject go = Game.FindAny("DRAGRACE/LOD/DRAGSTRIP/DragTiming");
            if (go == null) return false;   // (piste eteinte : seulement le vendredi, pres d'elle)
            PlayMakerFSM r = Game.FsmOn(go, "Results"), b = Game.FsmOn(go, "Leaderboard");
            if (r == null || b == null || string.IsNullOrEmpty(b.ActiveStateName)) return false;
            foreach (PlayMakerHashTableProxy p in go.GetComponents<PlayMakerHashTableProxy>()) if (p.referenceName == "Results") table = p.hashTable;
            foreach (PlayMakerArrayListProxy p in go.GetComponents<PlayMakerArrayListProxy>())
            {
                if (p.referenceName == "Names") names = p.arrayList;
                if (p.referenceName == "Cars") cars = p.arrayList;
                if (p.referenceName == "Speeds") speeds = p.arrayList;
            }
            if (table == null || names == null || cars == null || speeds == null) return false;
            results = r; board = b;
            if (!hooked)
            {
                hooked = true;
                FsmState lb = r.Fsm.GetState("Leaderboard"), s1 = b.Fsm.GetState("State 1");
                if (lb != null) FsmHooks.Append(lb, new Hook { Kind = 0 }); else Log.Warn("dragsters (MSC) : etat Results/Leaderboard absent");
                if (s1 != null) FsmHooks.Prepend(s1, new Hook { Kind = 1 }); else Log.Warn("dragsters (MSC) : etat Leaderboard/State 1 absent");
            }
            Log.Info("dragsters (MSC) : classement suivi (" + names.Count + " concurrents du jeu)");
            return true;
        }

        public static void Update()
        {
            if (!Session.Active || !PlayerSync.InGame) return;
            Find();
        }

        // Fin de l'etat 'Leaderboard' de Results : temps du concurrent entre au classement ; le notre -> envoye a tous.
        static void Announce()
        {
            if (results == null || !Session.Active || Session.RemoteCount == 0) return;
            FsmVariables v = results.FsmVariables;
            FsmString nm = v.FindFsmString("Name"), car = v.FindFsmString("Car"), spd = v.FindFsmString("Speed");
            FsmFloat tot = v.FindFsmFloat("TimeOverall");
            FsmString me = FsmVariables.GlobalVariables.FindFsmString("PlayerName");
            if (nm == null || tot == null || me == null || nm.Value != me.Value || tot.Value <= 0f || tot.Value >= best) return;
            best = tot.Value;
            SendBest(Session.Me != null ? Session.Me.Name : "?", car != null ? car.Value : "", spd != null ? spd.Value : "", best);
        }

        static void SendBest(string name, string car, string speed, float time)
        {
            Session.SendAll(new NetWriter(Msg.Job).U8(Session.LocalId).Str("@dragmsc").Str(name).Str(car ?? "").Str(speed ?? "").F32(time), true);
            Log.Info("dragsters (MSC) : meilleur temps " + time.ToString("F3") + " s (" + car + ") envoye");
        }

        public static void OnMessage(int who, NetReader r)
        {
            string name = r.Str(), car = r.Str(), speed = r.Str();
            float time = r.F32();
            Human h;
            if (!humans.TryGetValue(who, out h)) humans[who] = h = new Human();
            if (h.Time > 0f && time >= h.Time) return;   // (seulement un meilleur temps)
            h.Name = name; h.Car = car; h.Speed = speed; h.Time = time;
            Log.Info("dragsters (MSC) : " + name + " (#" + who + ") " + time.ToString("F3") + " s (" + car + ")");
            if (Find()) Inject();
        }

        // Les pilotes humains recus, concurrents en plus dans les tables du jeu (avant le tri de 'Leaderboard').
        static void Inject()
        {
            if (table == null || names == null || cars == null || speeds == null) return;
            int n = 0;
            foreach (KeyValuePair<int, Human> kv in humans)
            {
                Human h = kv.Value;
                if (h.Time <= 0f) continue;
                // (index perdu si le jeu a vide ou rempli ses listes depuis : place reprise a la fin)
                if (h.Index < 0 || h.Index >= names.Count || !Equals(names[h.Index], h.Name))
                {
                    h.Index = names.Count;
                    names.Add(h.Name); cars.Add(h.Car); speeds.Add(h.Speed);
                }
                names[h.Index] = h.Name; cars[h.Index] = h.Car;
                while (speeds.Count <= h.Index) speeds.Add("");
                while (cars.Count <= h.Index) cars.Add("");
                speeds[h.Index] = h.Speed;
                table[h.Index.ToString()] = h.Time;
                n++;
            }
            if (n > 0 && injectLogs++ < 5) Log.Info("dragsters (MSC) : classement d'ici avec " + n + " autre(s) pilote(s) humain(s)");
        }
        static int injectLogs;

        // [Test] Autotest=mscdrag (MSC, a deux, piste pas besoin d'etre ouverte) : a 30 s l'invite annonce un faux meilleur temps
        // ([Test] DragTemps, 12.345 par defaut) ; chacun note toutes les 5 s si la piste est suivie, les humains recus et les 12
        // premieres lignes (ResultsNames/ResultsTimes) de son classement.
        static int tStep, tCap; static float tNext;
        public static void Test(float t)
        {
            // ([Test] DragOuvrir=1 : piste allumee hors du vendredi, pour que 'Leaderboard' tourne ; DRAGRACE/TriggerIn :: Trigger,
            // qui allume et eteint DRAGRACE/LOD au passage du joueur, suspendu)
            if (Config.GetInt("Test", "DragOuvrir", 0) != 0 && t > 15f && t < 120f)
            {
                GameObject ti = Game.FindAny("DRAGRACE/TriggerIn");
                PlayMakerFSM tf = ti != null ? Game.FsmOn(ti, "Trigger") : null;
                if (tf != null && tf.enabled) { tf.enabled = false; Log.Info("autotest mscdrag : DRAGRACE/TriggerIn::Trigger suspendu"); }
                foreach (string p in new[] { "DRAGRACE/LOD", "DRAGRACE/LOD/DRAGSTRIP" })
                {
                    GameObject g = Game.FindAny(p);
                    if (g != null && !g.activeSelf) { g.SetActive(true); Log.Info("autotest mscdrag : " + p + " allume"); }
                }
            }
            if (tStep == 0 && t > 30f && !Session.IsHost)
            {
                tStep = 1;
                SendBest(Session.Me != null ? Session.Me.Name : "?", "Satsuma", "150 km/h", Config.GetFloat("Test", "DragTemps", 12.345f));
            }
            // ([Test] DragCapture=1 : l'hote ouvre la feuille RESULTS une fois un humain recu dans ResultsNames -- le jeu ne la
            // remplit qu'a son passage suivant dans 'State 1', toutes les 4 s -- capture dumps/ecran-drag-resultats.png)
            if (tCap == 0 && Session.IsHost && board != null && Config.GetInt("Test", "DragCapture", 0) != 0)
            {
                bool listed = false;
                foreach (PlayMakerArrayListProxy p in board.GetComponents<PlayMakerArrayListProxy>())
                    if (p.referenceName == "ResultsNames") foreach (KeyValuePair<int, Human> kv in humans) if (p.arrayList.Contains(kv.Value.Name)) listed = true;
                if (listed)
                {
                    tCap = 1;
                    GameObject sheet = Game.FindAny("Sheets/DragResults");
                    if (sheet != null) { sheet.SetActive(true); Autotest.CaptureSoon("drag-resultats", 3f); }
                    Log.Info("autotest mscdrag : feuille RESULTS " + (sheet != null ? "ouverte" : "introuvable"));
                }
            }
            if (t < tNext || t < 20f) return;
            tNext = t + 5f;
            var sb = new System.Text.StringBuilder("autotest mscdrag : piste " + (board != null ? "suivie" : "pas trouvee") + ", humains " + humains());
            GameObject go = Game.FindAny("DRAGRACE/LOD/DRAGSTRIP/DragTiming");
            if (go != null)
                foreach (PlayMakerArrayListProxy p in go.GetComponents<PlayMakerArrayListProxy>())
                    if (p.referenceName == "ResultsNames" || p.referenceName == "ResultsTimes")
                    {
                        sb.Append(" | ").Append(p.referenceName).Append(':');
                        for (int i = 0; i < p.arrayList.Count && i < 12; i++) sb.Append(' ').Append(p.arrayList[i]);
                    }
            Log.Info(sb.ToString());
        }
        static string humains()
        {
            var sb = new System.Text.StringBuilder();
            foreach (KeyValuePair<int, Human> kv in humans) sb.Append(kv.Value.Name).Append('=').Append(kv.Value.Time.ToString("F3")).Append(" [").Append(kv.Value.Index).Append("] ");
            return sb.Length > 0 ? sb.ToString().Trim() : "aucun";
        }
    }
}
