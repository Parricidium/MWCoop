using System.Collections.Generic;
using HutongGames.PlayMaker;
using MWCoop.Net;
using UnityEngine;

namespace MWCoop
{
    // Barrages de police (TRAFFIC/Police). Chaque jour l'automate Police tire au sort s'il y a des controles et ou
    // (aeroport, piste de ski, champs : SendRandomEvent), y accroche (Parenting : SetParent) les deux agents a
    // l'alcootest, les deux voitures, la herse et les deux radars, puis allume Checkpoints. Tire chez chacun, le
    // barrage etait ailleurs (ou absent) chez l'autre et ses agents invisibles a l'autre conducteur.
    //  - L'hote decide : il envoie (fiable, a chaque changement et toutes les 10 s) Checkpoints allume ou non et,
    //    pour chacun des 7 elements, son parent, sa pose locale et s'il est allume. L'invite coupe son automate
    //    Police et recopie.
    //  - Les voitures de police sont de la circulation (Traffic, conteneur TRAFFIC/Police) : leur pose et leur
    //    poursuite sont celles de l'hote ; le corps des agents suit l'hote (Npcs), sauf quand un agent controle
    //    un invite (Npcs le lui confie : alcootest, amende).
    // Restent PERSONNELS (chacun les siens, rien n'est envoye) : alcootest et amende (le joueur controle paie), radars
    // (CopRadar::Speedtrap flashe le joueur local), crimes et recherche (PlayerWanted, Systems), prison (JAIL), la
    // fuite a un barrage d'un invite (sa voiture de police est la copie de celle de l'hote). Les agents a domicile
    // (COPS), allumes chez le joueur recherche, sont montres aux autres par Npcs tant qu'ils sont a 80 m de lui.
    public static class Police
    {
        // Variables de l'automate Police qui designent les elements accroches au lieu du jour.
        static readonly string[] Vars = { "CopAlc1", "CopAlc2", "CopCar1", "CopCar2", "CopCheckpoint", "CopRadar1", "CopRadar2" };
        const int Kind = 10;   // genre du message de controle de Traffic
        static PlayMakerFSM logic;
        static GameObject checkpoints;
        static Transform[] items;
        static bool built, muted;
        static float buildAt = -1, nextCheck, nextFull, nextLog;
        static string lastSig;
        static int sent, recv;

        // ================================================================ police a domicile et prison (demande de JD, 08/10)
        // « Si les deux sont coupables, les deux en prison ; si l'un ou l'autre, seulement le coupable. »
        //  - Delits PERSONNELS : chacun a les siens (Systems/PlayerWanted::Activate : PoliceEvasion, ManSlaughter...). L'hote
        //    les a dans sa sauvegarde ; l'invite recevait ceux de l'hote avec elle a chaque session -- ils sont gardes a part
        //    (police-joueur.ini, par monde, comme son porte-monnaie) et remis apres le chargement ; premiere fois : aucun.
        //  - Agents a domicile (COPS, allumes par "At home" chez le joueur recherche) : un joueur recherche fait tourner LES
        //    SIENS (Npcs ne les fige jamais chez lui) ; les autres les voient (Npcs). L'etat "police a la porte" des poignees
        //    (CopsAtHome) n'est plus copie d'un joueur a l'autre (Interactions).
        //  - Arrestation (Activate entre dans "Pass out" : mandat signe, ou sortie devant les agents) : annoncee (@arrestation,
        //    position) ; un joueur lui aussi recherche, a la maison (30 m), est embarque avec sa propre peine ; les autres ont
        //    un message.
        static PlayMakerFSM wanted;
        static string wantedLast;
        static float nextWantedFind, crimesAt = -1, nextCrimeStore;
        static string crimesStored;
        static bool arrestApplying;
        static readonly string[] CrimeInts = { "AttemptedManslaughter", "ManSlaughter", "PoliceEvasion", "TrafficFatality", "DaysAttemptedManslaughter",
            "DaysEvasion", "DaysFines", "DaysManslaughter", "DaysTrafficFatality", "DaysInJail", "Sentence" };
        static readonly string[] CrimeFloats = { "Days", "FineWait" };
        static readonly HashSet<string> AtHome = new HashSet<string> { "At home", "Add crimes 2", "Calculate time 4", "Set data" };

        // Le joueur d'ici est recherche et ses agents sont a sa porte.
        // (l'automate reste sur "At home" apres la liberation, delits effaces par BackToHome::Reset : ce sont eux qui comptent)
        public static bool WantedAtHome() { return wanted != null && AtHome.Contains(wanted.ActiveStateName) && HasCrimes(); }

        static bool HasCrimes()
        {
            foreach (string n in new[] { "ManSlaughter", "PoliceEvasion", "TrafficFatality", "AttemptedManslaughter" })
            { FsmInt v = wanted.FsmVariables.FindFsmInt(n); if (v != null && v.Value > 0) return true; }
            return false;
        }

        static void FindWanted(float now)
        {
            if (wanted != null || now < nextWantedFind) return;
            nextWantedFind = now + 5f;
            GameObject g = Game.FindAny("Systems/PlayerWanted");
            wanted = g != null ? Game.FsmOn(g, "Activate") : null;
            if (wanted != null) Log.Info("police a domicile : PlayerWanted [" + wanted.ActiveStateName + "], " + CrimeLine());
        }

        static string CrimeLine()
        {
            var sb = new System.Text.StringBuilder();
            foreach (string n in CrimeInts) { FsmInt v = wanted.FsmVariables.FindFsmInt(n); if (v != null) sb.Append(sb.Length > 0 ? ";" : "").Append(n).Append(':').Append(v.Value); }
            foreach (string n in CrimeFloats) { FsmFloat v = wanted.FsmVariables.FindFsmFloat(n); if (v != null) sb.Append(sb.Length > 0 ? ";" : "").Append(n).Append(':').Append(v.Value.ToString("R", System.Globalization.CultureInfo.InvariantCulture)); }
            return sb.ToString();
        }

        static string CrimePath { get { return System.IO.Path.Combine(Log.DataDir, "police-joueur.ini"); } }
        static string World() { FsmFloat pid = FsmVariables.GlobalVariables.FindFsmFloat("PlayerID"); return pid != null ? pid.Value.ToString("F0") : ""; }

        // Invite : ses delits a lui (sinon aucun), puis la police remise d'accord (venue ou partie).
        static void RestoreCrimes()
        {
            string w = World(), line = null;
            if (w.Length == 0) return;
            if (System.IO.File.Exists(CrimePath))
                foreach (string l in System.IO.File.ReadAllLines(CrimePath)) if (l.StartsWith(w + "=")) line = l.Substring(w.Length + 1);
            var vals = new Dictionary<string, string>();
            if (line != null) foreach (string kv in line.Split(';')) { int c = kv.IndexOf(':'); if (c > 0) vals[kv.Substring(0, c)] = kv.Substring(c + 1); }
            foreach (string n in CrimeInts)
            {
                FsmInt v = wanted.FsmVariables.FindFsmInt(n); string s; int i;
                if (v != null) v.Value = vals.TryGetValue(n, out s) && int.TryParse(s, out i) ? i : 0;
            }
            foreach (string n in CrimeFloats)
            {
                FsmFloat v = wanted.FsmVariables.FindFsmFloat(n); string s; float f;
                if (v != null) v.Value = vals.TryGetValue(n, out s) && float.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out f) ? f : 0f;
            }
            crimesStored = CrimeLine();
            bool was = WantedAtHome();
            string st = wanted.ActiveStateName;
            if (st == "Idle" || was) Game.SetState(wanted, "Check crime");   // (JAIL -> At home, sinon Idle)
            if (was && !WantedAtHome())
            {   // plus recherche : agents et "police a la porte" d'ici retires (ceux de l'hote restent montres par Npcs)
                GameObject cops = Game.FindAny("COPS");
                if (cops != null && cops.activeSelf) cops.SetActive(false);
                foreach (Object o in Game.AllFsms()) { FsmBool b = ((PlayMakerFSM)o).FsmVariables.FindFsmBool("CopsAtHome"); if (b != null) b.Value = false; }
            }
            Log.Info("police a domicile : delits de l'invite " + (line != null ? "retrouves" : "a zero (premiere partie dans ce monde)") + " : " + crimesStored + " -> " + wanted.ActiveStateName);
        }

        static void StoreCrimes()
        {
            string w = World(), line = CrimeLine();
            if (w.Length == 0 || line == crimesStored) return;
            crimesStored = line;
            var lines = new List<string>();
            if (System.IO.File.Exists(CrimePath)) foreach (string l in System.IO.File.ReadAllLines(CrimePath)) if (!l.StartsWith(w + "=")) lines.Add(l);
            lines.Add(w + "=" + line);
            try { System.IO.File.WriteAllLines(CrimePath, lines.ToArray()); } catch { }
        }

        static void WantedUpdate(float now)
        {
            FindWanted(now);
            if (wanted == null) return;
            if (!Session.IsHost && Session.Active)
            {
                if (crimesAt > 0 && now >= crimesAt) { crimesAt = -1; RestoreCrimes(); }
                else if (crimesAt < 0 && now >= nextCrimeStore) { nextCrimeStore = now + 10f; StoreCrimes(); }
            }
            string st = wanted.ActiveStateName;
            if (st == wantedLast) return;
            string was = wantedLast;
            wantedLast = st;
            if (st == "At home" && was != null && !AtHome.Contains(was)) Log.Info("police a domicile : recherche, agents a la porte (" + CrimeLine() + ")");
            if (st != "Pass out" || was == null) return;
            Log.Info("police a domicile : arrete ici (depuis " + was + ")" + (arrestApplying ? " avec le joueur arrete" : ""));
            if (arrestApplying) { arrestApplying = false; return; }
            if (!Session.Active || Session.RemoteCount == 0) return;
            GameObject p = GameObject.Find("PLAYER");
            Session.SendAll(new NetWriter(Msg.Job).U8(Session.LocalId).Str("@arrestation").Vec(p != null ? p.transform.position : Vector3.zero), true);
        }

        // Prison en coop : la peine se compte en jours (JAIL/Functions::Time : DAY -> Subtract day ; finie -> l'heure venue,
        // Release -> retour en ville, delits effaces). En solo on dort sur le lit de la cellule pour passer les jours ; en coop
        // l'heure est celle de l'hote et dormir seul ne la fait pas avancer -- un jour de peine durait ~2 h de jeu reel. Reveil
        // dans la cellule (SleepTrigger::Activate "Wake up 2") sans qu'un jour soit passe pendant le sommeil : DAY envoye a SA
        // prison, un jour de peine purge, sans toucher a l'heure des autres.
        static PlayMakerFSM jailTime, jailSleep;
        static string jailTimeLast, jailSleepLast;
        static bool dayDuringSleep;
        static float nextJailFind;

        static void JailUpdate(float now)
        {
            if (jailTime == null || jailSleep == null)
            {
                if (now < nextJailFind) return;
                nextJailFind = now + 10f;
                GameObject f = Game.FindAny("JAIL/Functions"), sl = Game.FindAny("JAIL/Sleep/SleepTrigger");
                jailTime = f != null ? Game.FsmOn(f, "Time") : null;
                jailSleep = sl != null ? Game.FsmOn(sl, "Activate") : null;
                if (jailTime == null || jailSleep == null) return;
            }
            string ts = jailTime.ActiveStateName, ss = jailSleep.ActiveStateName;
            if (ts != jailTimeLast) { jailTimeLast = ts; if (ts == "Subtract day") dayDuringSleep = true; if (ts == "Release") Log.Info("prison : liberation"); }
            if (ss == jailSleepLast) return;
            jailSleepLast = ss;
            if (ss == "AnimateSleep" || ss == "Sleep") dayDuringSleep = false;
            if (ss != "Wake up 2" || !jailTime.gameObject.activeInHierarchy || !Session.Active || Session.RemoteCount == 0) return;
            if (dayDuringSleep) { Log.Info("prison : reveil, un jour est passe pendant le sommeil (rien a faire)"); return; }
            if (ts != "State 1") { Log.Info("prison : reveil, compte des jours en " + ts + " (pas de jour en plus)"); return; }
            // (Subtract day ne compte le jour que si le joueur est a moins de 10 m de la cellule ; la liberation, peine finie,
            // attend le jour -- heure de l'hote hors 20 h - 4 h -- comme en solo)
            FsmInt left = wanted != null ? wanted.FsmVariables.FindFsmInt("DaysInJail") : null;
            int before = left != null ? left.Value : -1;
            jailTime.SendEvent("DAY");
            int after = left != null ? left.Value : -1;
            Log.Info("prison : reveil dans la cellule, DAY envoye ici (jours " + before + " -> " + after + ")");
            if (after < before) Hud.Toast(after > 0 ? Lang.T("Une nuit en cellule : encore " + after + " jour(s)", "A night in the cell: " + after + " day(s) left")
                                                    : Lang.T("Peine purg\u00E9e : lib\u00E9r\u00E9 dans la journ\u00E9e", "Sentence served: released during the day"));
        }

        // Un autre joueur vient d'etre arrete chez lui (Jobs, @arrestation).
        public static void OnArrest(int who, Vector3 at)
        {
            PlayerInfo pi;
            string name = Session.Players.TryGetValue(who, out pi) ? pi.Name : "#" + who;
            GameObject p = GameObject.Find("PLAYER");
            float d = p != null ? Vector3.Distance(p.transform.position, at) : 999f;
            if (WantedAtHome() && d < 30f)
            {
                arrestApplying = true;
                Game.SetState(wanted, "Pass out");
                Log.Info("police a domicile : " + name + " arrete, recherche ici aussi et a " + d.ToString("F0") + " m : embarque avec lui");
                Hud.Toast(Lang.T("La police vous embarque aussi avec ", "The police take you too, with ") + name);
                return;
            }
            Log.Info("police a domicile : " + name + " arrete (ici " + (WantedAtHome() ? "recherche mais a " + d.ToString("F0") + " m" : "pas recherche") + ")");
            Hud.Toast(name + Lang.T(" a \u00E9t\u00E9 arr\u00EAt\u00E9 par la police", " was arrested by the police"));
        }

        public static void OnLevelLoaded()
        {
            wanted = null; wantedLast = null; nextWantedFind = 0; arrestApplying = false; crimesStored = null;
            jailTime = jailSleep = null; jailTimeLast = jailSleepLast = null; nextJailFind = 0; dayDuringSleep = false;
            crimesAt = PlayerSync.InGame ? Time.realtimeSinceStartup + 16f : -1;
            logic = null; checkpoints = null; items = null; built = muted = false; lastSig = null; sent = recv = 0; step = 0;
            buildAt = PlayerSync.InGame ? Time.realtimeSinceStartup + 7f : -1;
        }

        static void Build()
        {
            built = true;
            GameObject p = Game.FindAny("TRAFFIC/Police");
            logic = p != null ? Game.FsmOn(p, "Police") : null;
            if (logic == null) { Log.Warn("police : TRAFFIC/Police::Police introuvable"); return; }
            FsmGameObject cg = logic.FsmVariables.FindFsmGameObject("Checkpoints");
            checkpoints = cg != null && cg.Value != null ? cg.Value : Game.FindAny("TRAFFIC/Police/Checkpoints");
            items = new Transform[Vars.Length];
            int found = 0;
            for (int i = 0; i < Vars.Length; i++)
            {
                FsmGameObject v = logic.FsmVariables.FindFsmGameObject(Vars[i]);
                if (v != null && v.Value != null) { items[i] = v.Value.transform; found++; }
            }
            Log.Info("police : barrage " + (checkpoints != null ? "trouve" : "absent") + ", " + found + "/" + Vars.Length + " elements, automate " + logic.ActiveStateName);
        }

        public static void Update()
        {
            float now = Time.realtimeSinceStartup;
            if (PlayerSync.InGame) { WantedUpdate(now); JailUpdate(now); }
            if (!built) { if (buildAt > 0 && now >= buildAt) Build(); return; }
            if (logic == null || !Session.IsHost || Session.RemoteCount == 0 || now < nextCheck) return;
            nextCheck = now + 0.5f;
            string sig = Signature();
            if (sig == lastSig && now < nextFull) return;
            lastSig = sig; nextFull = now + 10f;
            Send();
        }

        // Ce qui compte du barrage : allume, et pour chaque element son parent, allume, place (au decimetre).
        static string Signature()
        {
            var sb = new System.Text.StringBuilder(checkpoints != null && checkpoints.activeSelf ? "1" : "0");
            foreach (Transform t in items)
            {
                if (t == null) { sb.Append("|-"); continue; }
                Vector3 p = t.localPosition;
                sb.Append('|').Append(t.parent != null ? t.parent.name : "").Append(t.gameObject.activeSelf ? '+' : '-')
                  .Append((int)(p.x * 10)).Append(',').Append((int)(p.y * 10)).Append(',').Append((int)(p.z * 10));
            }
            return sb.ToString();
        }

        // [U16 0xFFFF][U8 10][U8 Checkpoints allume][U8 n] puis par element [Str chemin du parent][Vec pose locale]
        // [Quat rotation locale][U8 allume].
        static void Send()
        {
            var w = new NetWriter(Msg.Traffic).U16(0xFFFF).U8(Kind).U8(checkpoints != null && checkpoints.activeSelf ? 1 : 0).U8(items.Length);
            foreach (Transform t in items)
            {
                if (t == null) { w.Str("").Vec(Vector3.zero).Quat(Quaternion.identity).U8(0); continue; }
                w.Str(t.parent != null ? Recon.Path(t.parent) : "").Vec(t.localPosition).Quat(t.localRotation).U8(t.gameObject.activeSelf ? 1 : 0);
            }
            Session.Broadcast(w, true);
            if (++sent <= 10) Log.Info("police : barrage envoye (" + lastSig + ")");
        }

        // Invite : barrage de l'hote recopie (automate Police coupe a la premiere reception).
        public static void OnMessage(Peer from, NetReader r)
        {
            if (Session.IsHost || !built || logic == null) return;
            bool on = r.U8() != 0;
            int n = r.U8();
            if (!muted) { muted = true; logic.enabled = false; Log.Info("police : barrages dictes par l'hote (automate Police coupe ici)"); }
            if (checkpoints != null && checkpoints.activeSelf != on) checkpoints.SetActive(on);
            for (int i = 0; i < n; i++)
            {
                string pp = r.Str();
                Vector3 lp = r.Vec();
                Quaternion lr = r.Quat();
                bool act = r.U8() != 0;
                if (items == null || i >= items.Length || items[i] == null || pp.Length == 0) continue;
                Transform t = items[i];
                // Voiture suivie par Traffic : seulement son parent (sa place vient du trafic de l'hote).
                bool car = Traffic.Follows(t);
                if (t.parent == null || Recon.Path(t.parent) != pp)
                {
                    GameObject g = Game.FindAny(pp);
                    if (g != null) t.SetParent(g.transform, car);
                }
                if (!car) { t.localPosition = lp; t.localRotation = lr; }
                if (!car && t.gameObject.activeSelf != act) t.gameObject.SetActive(act);   // (voiture : allumee par Traffic)
            }
            if (++recv <= 10 || Time.realtimeSinceStartup >= nextLog)
            {
                nextLog = Time.realtimeSinceStartup + 60f;
                Log.Info("police : barrage de l'hote recopie (" + (on ? "allume" : "eteint") + ", " + Describe() + ")");
            }
        }

        static string Describe()
        {
            if (items == null) return "?";
            Transform a = items[0];
            return Vars[0] + (a != null ? " sous " + (a.parent != null ? a.parent.name : "-") + " en " + a.position.ToString("F1") + (a.gameObject.activeInHierarchy ? " actif" : " inactif") : " ?");
        }

        // Essai 'police' : [Test] PoliceForcer=1 -> a 30 s l'hote relance le tirage (Police "State 1" : lieu au
        // hasard, Parenting, Cop1 allume) ; les deux cotes notent le barrage toutes les 2 s.
        static int step;
        static float nextTest;

        // [Test] Autotest=arrestation : a 30 s, recherche ici si [Test] Recherche=1 (delit PoliceEvasion, Check crime -> At home) ;
        // a 60 s l'hote est arrete (Pass out) ; a 78 s chacun note son etat, sa position et ses delits.
        static int arrStep;
        static void TestArrest(float t)
        {
            if (wanted == null) return;
            if (t > 30f && arrStep == 0)
            {
                arrStep = 1;
                if (Config.GetInt("Test", "Recherche", 0) != 0)
                {
                    FsmInt ev = wanted.FsmVariables.FindFsmInt("PoliceEvasion");
                    if (ev != null) ev.Value = Mathf.Max(ev.Value, 1);
                    Game.SetState(wanted, "Check crime");
                }
                GameObject p = GameObject.Find("PLAYER");
                Log.Info("autotest : arrestation, avant : " + wanted.ActiveStateName + ", recherche ici " + WantedAtHome() + ", joueur en " + (p != null ? p.transform.position.ToString("F1") : "?"));
            }
            if (t > 60f && arrStep == 1)
            {
                arrStep = 2;
                if (Session.IsHost) { Game.SetState(wanted, "Pass out"); Log.Info("autotest : arrestation de l'hote (Pass out)"); }
            }
            if (t > 84f && arrStep == 3 && Config.GetInt("Test", "ReveilCellule", 0) != 0)
            {
                arrStep = 4;
                nextJailFind = 0; JailUpdate(Time.realtimeSinceStartup);
                if (Config.GetInt("Test", "ReveilCellule", 0) == 2 && wanted != null) { FsmInt dj = wanted.FsmVariables.FindFsmInt("DaysInJail"); if (dj != null) dj.Value = 1; }
                if (jailSleep != null) { jailSleepLast = "Sleep"; dayDuringSleep = false; Game.SetState(jailSleep, "Wake up 2"); Log.Info("autotest : arrestation, reveil simule dans la cellule"); }
                else Log.Info("autotest : arrestation, lit de la cellule introuvable");
            }
            if (t > 125f && arrStep == 5)
            {
                arrStep = 6;
                GameObject p = GameObject.Find("PLAYER");
                Log.Info("autotest : arrestation, fin : prison " + (jailTime != null ? jailTime.ActiveStateName : "?") + ", joueur en " + (p != null ? p.transform.position.ToString("F1") : "?") + ", " + wanted.ActiveStateName + ", " + CrimeLine());
            }
            if (t > 92f && arrStep == 4)
            {
                arrStep = 5;
                Log.Info("autotest : arrestation, apres le reveil : prison " + (jailTime != null ? jailTime.ActiveStateName : "?") + ", " + CrimeLine());
            }
            if (t > 78f && arrStep == 2)
            {
                arrStep = 3;
                GameObject p = GameObject.Find("PLAYER");
                Log.Info("autotest : arrestation, apres : " + wanted.ActiveStateName + ", joueur en " + (p != null ? p.transform.position.ToString("F1") : "?") + ", " + CrimeLine());
            }
        }

        public static void Test(string mode, float t)
        {
            if (mode == "arrestation") { TestArrest(t); return; }
            if (mode != "police") return;
            if (Session.IsHost && step == 0 && t > 30f && logic != null && Config.GetInt("Test", "PoliceForcer", 0) != 0)
            {
                step = 1;
                string before = logic.ActiveStateName;
                Game.SetState(logic, "State 1");
                Log.Info("autotest : police : tirage relance (" + before + " => " + logic.ActiveStateName + ")");
            }
            float now = Time.realtimeSinceStartup;
            if (t < 20f || now < nextTest) return;
            nextTest = now + 2f;
            Log.Info("autotest : police barrage (" + (Session.IsHost ? "hote" : "invite") + ") automate " + (logic != null ? (logic.enabled ? logic.ActiveStateName : "coupe") : "?")
                     + ", checkpoints " + (checkpoints != null && checkpoints.activeInHierarchy) + ", " + Describe()
                     + ", voiture1 " + (items != null && items[2] != null ? items[2].position.ToString("F1") : "?") + ", envoyes " + sent + ", recus " + recv);
        }
    }
}
