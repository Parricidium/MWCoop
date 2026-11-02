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
        static string bestCar = "", bestSpeed = "";   // (audit MSC 02/11 : notre meilleur temps, redit aux arrivants par l'hote)
        static bool hooked;

        public static void OnLevelLoaded() { humans.Clear(); results = board = null; table = null; names = cars = speeds = null; nextLook = 0f; hooked = false; best = float.MaxValue; bestCar = bestSpeed = ""; snapshots.Clear(); }

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
            // (My Summer Car seulement, audit MSC 02/11 : sur My Winter Car, DRAGRACE n'existe pas -- Game.FindAny relevait toutes les
            // racines toutes les 5 s, comme RallyMsc avant sa garde)
            if (!Game.Msc || !Session.Active || !PlayerSync.InGame) return;
            Find();
            Snapshots();
        }

        // Fin de l'etat 'Leaderboard' de Results : temps du concurrent entre au classement ; le notre -> envoye a tous.
        static void Announce()
        {
            if (results == null) return;
            FsmVariables v = results.FsmVariables;
            FsmString nm = v.FindFsmString("Name"), car = v.FindFsmString("Car"), spd = v.FindFsmString("Speed");
            FsmFloat tot = v.FindFsmFloat("TimeOverall");
            FsmString me = FsmVariables.GlobalVariables.FindFsmString("PlayerName");
            // (audit MSC 02/11 : la copie d'une voiture qu'un autre conduit garde son StagingWheel -- Data.Name = le pseudo de celui
            // d'ici s'il l'a conduite avant, jamais remis -- et passe les declencheurs de la piste : sa manche etait annoncee sous
            // notre pseudo. Seule une voiture que personne d'autre ne conduit compte. [Test] DragCopie=0 : comme avant.)
            FsmGameObject comp = v.FindFsmGameObject("Competitor");
            if (Config.GetInt("Test", "DragCopie", 1) != 0 && comp != null && comp.Value != null && VehicleSync.RemotelyDriven(comp.Value.transform))
            {
                Log.Info("dragsters (MSC) : manche de " + comp.Value.transform.root.name + ", conduite par un autre joueur : pas la notre");
                return;
            }
            if (nm == null || tot == null || me == null || nm.Value != me.Value || tot.Value <= 0f || tot.Value >= best) return;
            // (garde meme seul : redit aux arrivants -- avant, un temps fait sans personne n'etait jamais annonce)
            best = tot.Value; bestCar = car != null ? car.Value : ""; bestSpeed = spd != null ? spd.Value : "";
            if (!Session.Active || Session.RemoteCount == 0) return;
            SendBest(Session.Me != null ? Session.Me.Name : "?", bestCar, bestSpeed, best);
        }

        // Hote : a l'arrivee d'un joueur (PlayerSync.SnapshotAll, 20 s), notre meilleur temps et ceux des autres humains connus (leur
        // numero en tete, comme s'ils venaient d'eux) -- audit MSC 02/11 : un arrivant (ou un invite revenu) ne voyait les autres
        // que s'ils battaient leur temps apres son arrivee ; le jeu ne sauve pas ces tables.
        static readonly List<KeyValuePair<float, Peer>> snapshots = new List<KeyValuePair<float, Peer>>();
        public static void ScheduleSnapshot(Peer p) { if (Session.IsHost && p != null && Game.Msc) snapshots.Add(new KeyValuePair<float, Peer>(Time.realtimeSinceStartup + 20f, p)); }

        static void Snapshots()
        {
            float now = Time.realtimeSinceStartup;
            for (int i = snapshots.Count - 1; i >= 0; i--)
            {
                if (now < snapshots[i].Key) continue;
                Peer p = snapshots[i].Value; snapshots.RemoveAt(i);
                if (!p.Accepted || Session.T == null) continue;
                int n = 0;
                if (best < float.MaxValue) { FireTools.SendTo(p, Write(Session.LocalId, Session.Me != null ? Session.Me.Name : "?", bestCar, bestSpeed, best)); n++; }
                foreach (KeyValuePair<int, Human> kv in humans)
                {
                    if (kv.Key == p.Id || kv.Value.Time <= 0f) continue;
                    FireTools.SendTo(p, Write(kv.Key, kv.Value.Name, kv.Value.Car, kv.Value.Speed, kv.Value.Time));
                    n++;
                }
                if (n > 0) Log.Info("dragsters (MSC) : " + n + " meilleur(s) temps envoye(s) a " + p);
            }
        }

        static NetWriter Write(int who, string name, string car, string speed, float time)
        {
            return new NetWriter(Msg.Job).U8(who).Str("@dragmsc").Str(name ?? "?").Str(car ?? "").Str(speed ?? "").F32(time);
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
            if (!Game.Msc) return;
            // (le notre, renvoye par l'hote apres un rechargement : pas un concurrent de plus, mais le temps a battre pour l'annoncer)
            // (sous son ancien numero aussi, apres une reconnexion : meme pseudo que le notre)
            if (who == Session.LocalId || Session.Me != null && name == Session.Me.Name) { if (time > 0f && time < best) best = time; return; }
            Human h;
            if (!humans.TryGetValue(who, out h))
            {
                // (meme pseudo sous un autre numero : le joueur est revenu -- sa ligne reprise, meme place du classement et meilleur
                // temps garde ; avant, une deuxieme ligne a son nom)
                int old = -1;
                foreach (KeyValuePair<int, Human> kv in humans) if (kv.Value.Name == name) old = kv.Key;
                if (old >= 0) { h = humans[old]; humans.Remove(old); } else h = new Human();
                humans[who] = h;
            }
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
        static int tStep, tCap, tHost; static float tNext;
        public static void Test(float t)
        {
            // (audit MSC 02/11, [Test] DragHoteTemps=s : l'hote retient a 10 s un faux meilleur temps, comme une manche faite seul
            // avant l'arrivee de l'invite ; l'invite lance ~60 s plus tard doit le recevoir par ScheduleSnapshot, 20 s apres son arrivee)
            float ht = Config.GetFloat("Test", "DragHoteTemps", 0f);
            if (tHost == 0 && t > 10f && Session.IsHost && ht > 0f) { tHost = 1; TestHostBest(ht); }
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
        // Essai : meilleur temps d'ici retenu sans manche (comme Announce, seul : rien d'envoye).
        public static string TestHostBest(float time)
        {
            best = time; bestCar = "Satsuma"; bestSpeed = "150 km/h";
            string r = "autotest mscdrag : meilleur temps d'ici retenu " + time.ToString("F3") + " s (" + Session.RemoteCount + " autre(s) joueur(s))";
            Log.Info(r);
            return r;
        }

        // Essai (copie, audit MSC 02/11) : le Competitor de Results serait-il annonce ? (voiture conduite par un autre : non)
        public static string TestCopy(string car)
        {
            Transform t = VehicleSync.Body(car) != null ? VehicleSync.Body(car).transform : null;
            string r = "autotest mscdrag : " + car + (t == null ? " introuvable" : VehicleSync.RemotelyDriven(t) ? " conduite par un autre : manche pas annoncee ici" : " pas conduite par un autre : manche annoncee si elle porte notre pseudo");
            Log.Info(r);
            return r;
        }

        static string humains()
        {
            var sb = new System.Text.StringBuilder();
            foreach (KeyValuePair<int, Human> kv in humans) sb.Append(kv.Value.Name).Append('=').Append(kv.Value.Time.ToString("F3")).Append(" [").Append(kv.Value.Index).Append("] ");
            return sb.Length > 0 ? sb.ToString().Trim() : "aucun";
        }
    }
}
