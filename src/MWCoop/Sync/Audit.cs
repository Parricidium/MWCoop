using System.Collections.Generic;
using System.IO;
using System.Text;
using HutongGames.PlayMaker;
using MWCoop.Net;
using UnityEngine;

namespace MWCoop
{
    // Audit de la synchro (demande de JD : voir TOUT ce qui est partage ou non, sans attendre qu'un joueur le
    // remarque). Trois outils sur un meme releve des automates du monde (hors PLAYER, GUI, avatars) :
    //  - recensement : chaque automate interactif (clic, touche) ou sauvegarde, par lieu, avec le module de
    //    MWCoop qui le suit, ou NON SUIVI -> dumps/recensement.txt (F10 > SYNCHRO, ou au bout d'une minute) ;
    //  - empreintes : toutes les 15 s l'hote demande « SNAP n » ; chaque invite lui envoie ce qui a change
    //    depuis sa derniere empreinte (etat de chaque automate, cle hachee), l'hote compare a la sienne. Un
    //    ecart present a deux empreintes de suite est une DESYNC (journal + dumps/desync.txt + F10) ; les
    //    automates qui changent sans arret (minuteries, boucles) sont ignores, ceux d'un seul joueur aussi
    //    (LOD, declencheurs de distance) ;
    //  - enregistreur : un automate qui change d'etat juste apres un clic ou une touche du joueur local, a
    //    moins de 4 m de lui, est une ACTION -> dumps/actions.txt, avec le contexte (a pied, au volant,
    //    passager) et son module, ou NON SUIVI : ce qui ne part pas chez les autres.
    // [Coop] Audit=0 coupe tout.
    public static class Audit
    {
        class Obs
        {
            public string Key, Area, FsmName; public uint Hash; public PlayMakerFSM F;
            public bool Input, Persist, Personal, HostDriven, Checked;
            public string Flags = "";                                   // I commande, T declencheur, S cree des objets, R hasard, D distance au joueur
            public string Last; public float LastChange;
            public float[] Recent = new float[6]; public int RecentN;   // instants des derniers changements
            public bool Spied; public float ActedFor = -1;              // espion pose ; clic deja note
        }

        // Espion : en tete de chaque etat d'un automate interactif. N'agit pas sur le jeu, ne reclame rien :
        // note l'etat atteint (les etats fugaces, entre deux regards, aussi).
        class Spy : FsmStateAction
        {
            public Obs O;
            public override void OnEnter()
            {
                try { Entered(O, State.Name); } catch (System.Exception e) { Replay.HookError(e); }
                Finish();
            }
        }

        static readonly List<Obs> obs = new List<Obs>();
        static readonly List<Obs> inputs = new List<Obs>();   // interactifs : relus a chaque image (enregistreur)
        static readonly Dictionary<uint, Obs> byHash = new Dictionary<uint, Obs>();
        static readonly Dictionary<PlayMakerFSM, Obs> byFsm = new Dictionary<PlayMakerFSM, Obs>();
        static readonly Dictionary<string, int> keyCount = new Dictionary<string, int>();
        static List<GameObject> scanRoots;
        static readonly Stack<Transform> scanStack = new Stack<Transform>();
        static int scanIndex = -1, pollIndex;
        static float scanAt = -1, rescanAt, censusAt = -1, lastInputAt = -100, nextSnap;
        static int snapN;
        static bool enabled;
        static StreamWriter actions;

        // Hote : etat de chaque invite (cle hachee -> valeur), empreintes en cours, ecarts.
        class PeerAudit { public Dictionary<uint, string> Values = new Dictionary<uint, string>(); public int Complete = -1; public HashSet<uint> Diff = new HashSet<uint>(); public Dictionary<uint, int> Streak = new Dictionary<uint, int>(); public float Since; public HashSet<uint> Reported = new HashSet<uint>(); }
        static readonly Dictionary<int, PeerAudit> peers = new Dictionary<int, PeerAudit>();
        static readonly Dictionary<int, Dictionary<uint, string>> hostSnaps = new Dictionary<int, Dictionary<uint, string>>();
        // Invite : ce qui a ete envoye a l'hote (pour n'envoyer que les changements), file d'envoi.
        static readonly Dictionary<uint, string> sent = new Dictionary<uint, string>();
        static readonly Queue<NetWriter> outbox = new Queue<NetWriter>();
        static bool fresh = true;   // invite : premiere empreinte depuis le chargement

        public static readonly List<string> Desyncs = new List<string>();      // F10 : ecarts en cours (hote)
        public static readonly List<string> Unshared = new List<string>();     // F10 : actions locales non suivies
        public static string Summary = "";

        public static void OnLevelLoaded()
        {
            obs.Clear(); inputs.Clear(); byHash.Clear(); byFsm.Clear(); keyCount.Clear(); peers.Clear(); hostSnaps.Clear(); sent.Clear(); outbox.Clear();
            Desyncs.Clear(); Unshared.Clear(); Summary = "";
            scanRoots = null; scanIndex = -1; pollIndex = 0; snapN = 0; scanStack.Clear(); fresh = true;
            if (!subscribed) { subscribed = true; Session.PlayerLeft += pi => peers.Remove(pi.Id); }
            enabled = Config.GetInt("Coop", "Audit", 1) != 0;
            scanAt = enabled && PlayerSync.InGame ? Time.realtimeSinceStartup + 20f : -1;
            censusAt = scanAt > 0 ? scanAt + 45f : -1;
            nextSnap = Time.realtimeSinceStartup + 40f;
            if (actions != null) { actions.Close(); actions = null; }
        }

        // ------------------------------------------------------------ releve
        static readonly HashSet<string> skipRoots = new HashSet<string> { "PLAYER", "GUI", "MWCoop", "cObject", "HOTween" };
        // Conduits par l'hote (positions), logique propre a chacun : recenses, jamais compares.
        static readonly HashSet<string> hostDrivenRoots = new HashSet<string> { "NPC_CARS", "TRAFFIC", "TRAIN" };
        static bool subscribed;
        static readonly HashSet<string> localFsmNames = new HashSet<string> { "LOD", "Lod", "LODSwitch", "Update Cursor", "Distance", "DistanceCheck", "Raycast", "Normalize", "SwitchCamera", "CrouchTriggers", "PlayerTrigger" };
        // Etats qui ne disent que « le joueur local est la / assis / au volant » : un ecart y est normal.
        static readonly HashSet<string> presenceStates = new HashSet<string> { "Player in car", "Wait for player", "Press return", "Sitting in car", "Not sitting", "Check player", "Wait player" };

        public static void Update()
        {
            if (!enabled) return;
            float now = Time.realtimeSinceStartup;
            if (Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1) || Input.GetKeyDown(KeyCode.F) || Input.GetKeyDown(KeyCode.E) || Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)) lastInputAt = now;
            if (scanAt > 0 && now >= scanAt)
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                bool first = scanIndex < 0;
                if (scanIndex < 0) { scanRoots = WorldFsms.CachedRoots != null ? new List<GameObject>(WorldFsms.CachedRoots) : Recon.SceneRoots(); scanIndex = 0; scanStack.Clear(); }
                // Parcours en profondeur, au plus 500 objets par image (pas d'a-coup), puis un nouveau releve
                // toutes les 5 min (objets crees en jeu).
                int budget = 500;
                while (budget > 0 && sw.ElapsedMilliseconds < 4 && (scanStack.Count > 0 || scanIndex < scanRoots.Count))
                {
                    if (scanStack.Count == 0)
                    {
                        GameObject r = scanRoots[scanIndex++];
                        if (r != null && !skipRoots.Contains(r.name) && !r.name.StartsWith("MWCoop")) scanStack.Push(r.transform);
                        continue;
                    }
                    Transform t = scanStack.Pop();
                    budget--;
                    if (t == null) continue;
                    long t0 = sw.ElapsedMilliseconds;
                    ScanObject(t);
                    if (sw.ElapsedMilliseconds - t0 > 30) Log.Info("audit : objet lourd " + Recon.Path(t) + " " + (sw.ElapsedMilliseconds - t0) + " ms");
                    for (int c = t.childCount - 1; c >= 0; c--) scanStack.Push(t.GetChild(c));
                }
                if (sw.ElapsedMilliseconds > 20) Log.Info("audit : releve " + sw.ElapsedMilliseconds + " ms" + (first ? " (racines)" : "") + ", " + (500 - budget) + " objets");
                if (scanStack.Count == 0 && scanIndex >= scanRoots.Count) { scanAt = -1; rescanAt = now + 300f; scanIndex = -1; Log.Info("audit : " + obs.Count + " automates suivis du regard"); }
            }
            else if (scanAt < 0 && rescanAt > 0 && now >= rescanAt) { rescanAt = -1; scanAt = now; }
            Poll(now);
            if (censusAt > 0 && now >= censusAt && scanAt < 0) { censusAt = -1; Log.Info("audit : recensement " + Census()); }
            // Empreintes : l'hote donne le rythme.
            if (Session.Active && Session.IsHost && Session.RemoteCount > 0 && now >= nextSnap && obs.Count > 0)
            {
                nextSnap = now + 15f;
                snapN++;
                hostSnaps[snapN] = Values();
                hostSnaps.Remove(snapN - 3);
                Session.Broadcast(new NetWriter(Msg.Audit).U8(0).U16(snapN), true);
            }
            for (int i = 0; i < 6 && outbox.Count > 0; i++) Session.SendToHost(outbox.Dequeue(), true);
        }

        static void ScanObject(Transform t)
        {
            foreach (PlayMakerFSM f in t.GetComponents<PlayMakerFSM>())
            {
                if (f == null || byFsm.ContainsKey(f)) continue;
                string path = Recon.Path(f.transform) + "::" + f.FsmName;
                int k; keyCount.TryGetValue(path, out k); keyCount[path] = k + 1;
                var o = new Obs { Key = path + "#" + k, F = f, FsmName = f.FsmName, Last = f.ActiveStateName };
                o.Hash = Fnv(o.Key);
                o.Area = AreaOf(f.transform);
                o.Personal = localFsmNames.Contains(f.FsmName);
                o.HostDriven = hostDrivenRoots.Contains(f.transform.root.name);
                foreach (FsmString s in f.FsmVariables.StringVariables) if (s.Name.StartsWith("UniqueTag") || s.Name.StartsWith("UT")) o.Persist = true;
                if (byHash.ContainsKey(o.Hash)) continue;   // (collision : tres rare, l'un des deux est ignore)
                obs.Add(o); byHash[o.Hash] = o; byFsm[f] = o;
                if (f.gameObject.activeInHierarchy) CheckActions(o);   // (sinon au premier passage actif : actions pas chargees)
            }
        }

        // Types d'actions de l'automate : commande du joueur (-> enregistreur + espion), declencheur, creation
        // d'objets, hasard, distance au joueur.
        static readonly HashSet<string> inputTypes = new HashSet<string> { "MousePickEvent", "GetButtonDown", "GetButtonUp", "GetButton", "GetMouseButtonDown", "GetMouseButtonUp", "GetMouseButton", "GetKeyDown", "GetKeyUp", "GetKey", "GetAxis" };
        static readonly HashSet<string> randomTypes = new HashSet<string> { "RandomFloat", "RandomInt", "RandomBool", "SendRandomEvent", "RandomEvent", "RandomWait", "ArrayListGetRandom", "GetRandomChild", "SelectRandomString", "SelectRandomGameObject" };
        static void CheckActions(Obs o)
        {
            if (o.Checked) return;
            o.Checked = true;
            bool t = false, sp = false, r = false, d = false;
            try
            {
                foreach (FsmState st in o.F.Fsm.States)
                    foreach (FsmStateAction a in st.Actions)
                    {
                        if (a == null) continue;
                        string tn = a.GetType().Name;
                        if (inputTypes.Contains(tn)) o.Input = true;
                        else if (tn.StartsWith("Trigger") || tn.StartsWith("Collision")) t = true;
                        else if (tn == "CreateObject" || tn == "CreateEmptyObject" || tn.StartsWith("Spawn")) sp = true;
                        else if (randomTypes.Contains(tn)) r = true;
                        else if (tn == "GetDistance") d = true;
                    }
            }
            catch { }
            o.Flags = (o.Input ? "I" : "-") + (t ? "T" : "-") + (sp ? "S" : "-") + (r ? "R" : "-") + (d ? "D" : "-");
            if (o.Input) { inputs.Add(o); SpyOn(o); }
        }

        static void SpyOn(Obs o)
        {
            if (o.Spied || !o.F.gameObject.activeInHierarchy) return;
            try
            {
                foreach (FsmState st in o.F.Fsm.States)
                {
                    FsmStateAction[] old = st.Actions;
                    var arr = new FsmStateAction[old.Length + 1];
                    arr[0] = new Spy { O = o };
                    System.Array.Copy(old, 0, arr, 1, old.Length);
                    st.Actions = arr;
                }
                o.Spied = true;
            }
            catch { }
        }

        // Etat atteint par un automate interactif : une ACTION s'il suit de pres un clic du joueur local.
        static void Entered(Obs o, string st)
        {
            float now = Time.realtimeSinceStartup;
            if (Replay.Depth > 0 || now - lastInputAt > 1.5f || o.ActedFor == lastInputAt) return;
            if (st.StartsWith("Mouse") || st.StartsWith("Check") || st.StartsWith("Wait for")) return;   // (survol, attente)
            o.ActedFor = lastInputAt;
            Action(o, o.Last, st, now);
        }

        static uint Fnv(string s)
        {
            uint h = 2166136261;
            for (int i = 0; i < s.Length; i++) { h ^= s[i]; h *= 16777619; }
            return h;
        }

        // Lieux reperes du monde (positions du releve de la scene) ; vehicule si l'objet en fait partie.
        static readonly string[] areaNames = { "appartement", "maison des parents", "station-magasin", "Perajarvi (ville)", "port", "garage", "controle technique", "station d'eau", "decharge", "salle de danse", "cabane", "chalet", "terrain de foot" };
        static readonly Vector3[] areaPos = {
            new Vector3(-1285.6f, 1.5f, 1078.8f), new Vector3(-7.5f, 0.4f, 5.4f), new Vector3(-1548.3f, 3.3f, 1182.9f), new Vector3(-1405.6f, 9f, 1148.9f),
            new Vector3(-1738.8f, 3.2f, 952.8f), new Vector3(1557.5f, 3.9f, 723.2f), new Vector3(-1533.8f, 3f, 1269.8f), new Vector3(-1522.6f, 8.5f, 1408.3f),
            new Vector3(-767.6f, -2.9f, -639.8f), new Vector3(455.1f, 13.1f, 1318.2f), new Vector3(-170f, -3.8f, 1010f), new Vector3(-849.3f, -3.7f, 510.2f), new Vector3(-1260.8f, 0f, 1170.7f) };
        static string AreaOf(Transform t)
        {
            Transform root = t.root;
            if (root.GetComponent("CarDynamics") != null) return "vehicule " + root.name;
            Vector3 p = t.position;
            if (p.sqrMagnitude < 0.25f && root.position.sqrMagnitude < 0.25f) return "en reserve (0,0,0)";
            if (p.sqrMagnitude < 1f && t != root) p = root.position;
            int best = -1; float bd = 150f * 150f;
            for (int i = 0; i < areaPos.Length; i++) { float d = (areaPos[i] - p).sqrMagnitude; if (d < bd) { bd = d; best = i; } }
            return best >= 0 ? areaNames[best] : "ailleurs (" + root.name + ")";
        }

        // Modules de MWCoop qui suivent cet automate (« a+b » : accroche par deux modules), ou null. Lu dans ses
        // etats (actions ModHook posees par les modules), plus ce qui est suivi sans crochet.
        public static string Owner(PlayMakerFSM f)
        {
            var set = new List<string>();
            Obs o;
            bool loaded = f.gameObject.activeInHierarchy || (byFsm.TryGetValue(f, out o) && o.Checked);
            if (loaded)
            {
                try
                {
                    foreach (FsmState st in f.Fsm.States)
                        foreach (FsmStateAction a in st.Actions)
                        {
                            var m = a as ModHook;
                            if (m != null && !set.Contains(m.Module)) set.Add(m.Module);
                        }
                }
                catch { }
            }
            if (set.Count == 0)
            {
                if (CarDoors.Tracks(f)) set.Add("portieres");
                else if (Interactions.Tracks(f)) set.Add("interactions");
                else if (Consume.Tracks(f)) set.Add("consommables");
                else if (Jobs.Tracks(f)) set.Add("quetes");
                else if (WorldFsms.Tracks(f)) set.Add("monde");
                else if (Wallet.Observes(f)) set.Add("argent");
                else { string r = Replay.Owner(f); if (r != null) set.Add(r); }
            }
            string cov = Covers(f);
            if (cov != null && !set.Contains(cov)) set.Add(cov);
            return set.Count == 0 ? null : string.Join("+", set.ToArray());
        }

        // Suivis sans crochet (un module lit ou ecrit leurs variables, ou les mene autrement).
        static string Covers(PlayMakerFSM f)
        {
            string n = f.FsmName, go = f.gameObject.name, path = Recon.Path(f.transform);
            if (n == "PlayerTrigger" && (go.StartsWith("DriveTrigger") || go == "PlayerTrigger")) return "voitures";
            if (path.Contains("FuelPumps_")) return "machines";
            if (n == "Activate" && go.StartsWith("SleepTrigger")) return "monde (sommeil)";
            if (path.StartsWith("MAP/WEATHER") || path.StartsWith("MAP/Sun")) return "monde (meteo, heure)";
            return null;
        }

        // ------------------------------------------------------------ regard (etats, actions)
        static void Poll(float now)
        {
            if (obs.Count == 0) return;
            foreach (Obs o in inputs) Look(o, now);
            int budget = Mathf.Min(400, obs.Count);
            for (int i = 0; i < budget; i++)
            {
                if (pollIndex >= obs.Count) pollIndex = 0;
                Obs o = obs[pollIndex++];
                if (!o.Input) Look(o, now);
            }
        }

        static void Look(Obs o, float now)
        {
            if (o.F == null) return;
            string st = o.F.enabled && o.F.gameObject.activeInHierarchy ? o.F.ActiveStateName : "(arrete)";
            if (!o.Checked && st != "(arrete)") CheckActions(o);
            if (st == o.Last) return;
            string was = o.Last;
            o.Last = st; o.LastChange = now;
            o.Recent[o.RecentN++ % o.Recent.Length] = now;
            if (!o.Spied && now - lastInputAt < 1.5f && Replay.Depth == 0 && o.Input && st != "(arrete)" && o.ActedFor != lastInputAt) { o.ActedFor = lastInputAt; Action(o, was, st, now); }
        }

        // Change sans arret : plus de 5 changements dans les 60 dernieres secondes.
        static bool Volatile(Obs o, float now)
        {
            if (o.RecentN < o.Recent.Length) return false;
            float oldest = float.MaxValue;
            foreach (float t in o.Recent) if (t < oldest) oldest = t;
            return now - oldest < 60f;
        }

        static void Action(Obs o, string was, string st, float now)
        {
            GameObject pl = GameObject.Find("PLAYER");
            if (pl == null) return;
            bool inCar = VehicleSync.LocalDriving >= 0 && o.F.transform.root.name == VehicleSync.LocalDrivingName;
            if (!inCar && (o.F.transform.position - pl.transform.position).sqrMagnitude > 25f) return;
            string owner = Owner(o.F);
            string ctx = VehicleSync.LocalDriving >= 0 ? "au volant de " + VehicleSync.LocalDrivingName : Seats.Seated ? "passager" : "a pied";
            if (VehicleSync.LocalDriving >= 0) ctx += Strafe();
            string line = System.DateTime.Now.ToString("HH:mm:ss") + " | " + o.Area + " | " + o.Key + " | " + was + " -> " + st + " | " + (owner ?? "NON SUIVI") + " | " + ctx;
            try
            {
                if (actions == null)
                {
                    string dir = Path.Combine(Log.DataDir, "dumps");
                    Directory.CreateDirectory(dir);
                    actions = new StreamWriter(Path.Combine(dir, "actions.txt"), true, Encoding.UTF8);
                    actions.AutoFlush = true;
                }
                actions.WriteLine(line);
            }
            catch { }
            if (owner == null)
            {
                Log.Info("audit : action non partagee : " + o.Key + " " + was + " -> " + st + " (" + ctx + ")");
                Unshared.Insert(0, o.Area + " : " + o.F.gameObject.name + " :: " + o.FsmName + " -> " + st + " (" + ctx + ")");
                if (Unshared.Count > 40) Unshared.RemoveAt(Unshared.Count - 1);
            }
        }

        // Penche au volant ? (automate 'Strafe' de la tete du conducteur : hors de son etat de repos)
        static string Strafe()
        {
            foreach (KeyValuePair<PlayMakerFSM, Obs> kv in byFsm)
                if (kv.Value.FsmName == "Strafe" && kv.Key != null && kv.Key.transform.root.name == VehicleSync.LocalDrivingName)
                    return kv.Key.ActiveStateName != "State 1" ? ", penche (" + kv.Key.ActiveStateName + ")" : "";
            return "";
        }

        // ------------------------------------------------------------ empreintes
        // Etat compare : automates non personnels, hors ceux qui changent sans arret.
        static Dictionary<uint, string> Values()
        {
            float now = Time.realtimeSinceStartup;
            var v = new Dictionary<uint, string>(obs.Count);
            foreach (Obs o in obs)
            {
                if (o.F == null || o.Personal || o.HostDriven || o.Last == "(arrete)") continue;   // (objet eteint : souvent par distance, propre a chacun)
                v[o.Hash] = Volatile(o, now) || now - o.LastChange < 5f ? "~" : o.Last;   // (« ~ » : change sans arret, ou pas encore pose)
            }
            return v;
        }

        public static void OnMessage(Peer from, NetReader r)
        {
            int kind = r.U8();
            if (kind == 0 && !Session.IsHost)
            {
                // Hote : « SNAP n » -> nos changements depuis la derniere empreinte, par paquets.
                var sw = System.Diagnostics.Stopwatch.StartNew();
                int n = r.U16();
                if (obs.Count == 0) return;
                Dictionary<uint, string> v = Values();
                // Cles disparues (objet eteint, detruit) : pierre tombale « - », l'hote les oublie.
                var gone = new List<uint>();
                foreach (uint h in sent.Keys) if (!v.ContainsKey(h)) gone.Add(h);
                foreach (uint h in gone) { sent.Remove(h); v[h] = "-"; }
                var w = new NetWriter(Msg.Audit).U8(1).U16(n).U8(fresh ? 1 : 0);
                fresh = false;
                int inChunk = 0, total = 0;
                foreach (KeyValuePair<uint, string> kv in v)
                {
                    string old;
                    if (sent.TryGetValue(kv.Key, out old) && old == kv.Value) continue;
                    if (kv.Value != "-") sent[kv.Key] = kv.Value;
                    w.I32((int)kv.Key).Str(kv.Value);
                    inChunk++; total++;
                    if (inChunk >= 40) { outbox.Enqueue(w.I32(0).Str("")); w = new NetWriter(Msg.Audit).U8(1).U16(n).U8(0); inChunk = 0; }
                }
                // Dernier paquet : marque de fin (cle 0, valeur « $fin »).
                outbox.Enqueue(w.I32(0).Str("$fin"));
                if (sw.ElapsedMilliseconds > 15) Log.Info("audit : empreinte " + n + " en " + sw.ElapsedMilliseconds + " ms (" + total + " changements)");
                return;
            }
            if (kind == 1 && Session.IsHost)
            {
                int n = r.U16();
                bool isFresh = r.U8() != 0;
                PeerAudit pa;
                if (isFresh || !peers.TryGetValue(from.Id, out pa)) peers[from.Id] = pa = new PeerAudit { Since = Time.realtimeSinceStartup };   // (invite arrive ou revenu : tout repart de zero)
                while (true)
                {
                    uint h = (uint)r.I32(); string val = r.Str();
                    if (h == 0) { if (val == "$fin") { pa.Complete = n; if (Time.realtimeSinceStartup - pa.Since > 30f) Compare(from, pa, n); } break; }
                    if (val == "-") pa.Values.Remove(h); else pa.Values[h] = val;
                }
            }
        }

        static void Compare(Peer from, PeerAudit pa, int n)
        {
            Dictionary<uint, string> hv;
            if (!hostSnaps.TryGetValue(n, out hv)) return;
            string name = Session.Players.ContainsKey(from.Id) ? Session.Players[from.Id].Name : "#" + from.Id;
            var now = new HashSet<uint>();
            foreach (KeyValuePair<uint, string> kv in hv)
            {
                string g;
                if (!pa.Values.TryGetValue(kv.Key, out g)) continue;
                if (kv.Value == "~" || g == "~" || kv.Value == g) continue;
                if (presenceStates.Contains(kv.Value) || presenceStates.Contains(g)) continue;   // (presence d'un joueur : propre a chacun)
                now.Add(kv.Key);
            }
            // Ecart present a trois empreintes de suite (45 s) : desync (signalee une fois, jusqu'a ce qu'elle se resorbe).
            foreach (uint h in now)
            {
                int c; pa.Streak.TryGetValue(h, out c); pa.Streak[h] = c + 1;
            }
            foreach (uint h in new List<uint>(pa.Streak.Keys)) if (!now.Contains(h)) pa.Streak.Remove(h);
            foreach (uint h in now)
                if (pa.Streak[h] >= 3 && !pa.Reported.Contains(h))
                {
                    pa.Reported.Add(h);
                    Obs o; byHash.TryGetValue(h, out o);
                    string g = pa.Values[h];
                    Log.Info("DESYNC " + (o != null ? o.Area + " | " + o.Key + " | " + (Owner(o.F) ?? "NON SUIVI") : h.ToString("x8")) + " | hote=" + hv[h] + " " + name + "=" + g);
                }
            foreach (uint h in new List<uint>(pa.Reported))
                if (!now.Contains(h)) { pa.Reported.Remove(h); Obs o; byHash.TryGetValue(h, out o); Log.Info("resync " + (o != null ? o.Key : h.ToString("x8")) + " (" + name + ")"); }
            pa.Diff = now;
            Desyncs.Clear();
            foreach (KeyValuePair<int, PeerAudit> kv in peers)
                foreach (uint h in kv.Value.Reported)
                {
                    Obs o; byHash.TryGetValue(h, out o);
                    string hs; hv.TryGetValue(h, out hs);
                    Desyncs.Add((o != null ? o.Area + " : " + o.F.gameObject.name + " :: " + o.FsmName : h.ToString("x8")) + "  hote=" + hs + " / " + kv.Value.Values[h]);
                }
            Summary = "empreinte " + n + " : " + Desyncs.Count + " ecart(s) durable(s), " + now.Count + " ecart(s) a cet instant";
            try { File.WriteAllText(Path.Combine(Path.Combine(Log.DataDir, "dumps"), "desync.txt"), Summary + "\n" + string.Join("\n", Desyncs.ToArray()) + "\n"); } catch { }
        }

        // ------------------------------------------------------------ recensement
        public static string Census()
        {
            var perArea = new SortedDictionary<string, int[]>();
            var lines = new SortedDictionary<string, List<string>>();
            var tsv = new StringBuilder("cle\tlieu\tracine\tautomate\tdrapeaux\tmodules\tstatut\tetat\n");
            foreach (Obs o in obs)
            {
                if (o.F == null || (!o.Input && !o.Persist) || o.Personal) continue;
                string owner = Owner(o.F);
                string status = o.HostDriven ? "conduit par l'hote" : owner == null ? "NON SUIVI" : owner.Contains("+") ? "DOUBLE" : "suivi";
                int[] c; if (!perArea.TryGetValue(o.Area, out c)) perArea[o.Area] = c = new int[3];
                c[0]++; if (owner != null || o.HostDriven) c[1]++; if (status == "DOUBLE") c[2]++;
                string key = o.Area + " | " + status;
                List<string> l; if (!lines.TryGetValue(key, out l)) lines[key] = l = new List<string>();
                l.Add(o.Flags + (o.Persist ? "P" : "-") + " " + o.Key + " [" + o.Last + "]" + (owner != null ? " <" + owner + ">" : ""));
                tsv.Append(o.Key).Append('\t').Append(o.Area).Append('\t').Append(o.F.transform.root.name).Append('\t').Append(o.FsmName).Append('\t').Append(o.Flags + (o.Persist ? "P" : "-"))
                   .Append('\t').Append(owner ?? "").Append('\t').Append(status).Append('\t').Append(o.Last).Append('\n');
            }
            var sb = new StringBuilder("RECENSEMENT des automates interactifs ou sauvegardes, par lieu : total, suivis, doubles\n");
            sb.Append("Drapeaux : I commande du joueur, T declencheur, S cree des objets, R hasard, D distance au joueur, P sauvegarde\n\n");
            int tot = 0, ok = 0, dbl = 0;
            foreach (KeyValuePair<string, int[]> kv in perArea) { sb.Append(kv.Key).Append(" : ").Append(kv.Value[0]).Append(", ").Append(kv.Value[1]).Append(" suivis").Append(kv.Value[2] > 0 ? ", " + kv.Value[2] + " DOUBLES" : "").Append('\n'); tot += kv.Value[0]; ok += kv.Value[1]; dbl += kv.Value[2]; }
            sb.Append('\n');
            foreach (KeyValuePair<string, List<string>> kv in lines)
            {
                kv.Value.Sort(System.StringComparer.Ordinal);
                sb.Append("===== ").Append(kv.Key).Append(" (").Append(kv.Value.Count).Append(")\n");
                foreach (string l in kv.Value) sb.Append("  ").Append(l).Append('\n');
            }
            string dir = Path.Combine(Log.DataDir, "dumps");
            Directory.CreateDirectory(dir);
            string file = Path.Combine(dir, "recensement.txt");
            File.WriteAllText(file, sb.ToString());
            File.WriteAllText(Path.Combine(dir, "recensement.tsv"), tsv.ToString());
            Summary = tot + " automates interactifs ou sauvegardes, " + ok + " suivis, " + dbl + " accroches par deux modules";
            return file + " (" + Summary + ")";
        }

        // Essais : « clic » sur l'automate interactif non suivi le plus proche (premiere transition de son etat).
        public static string TestAct()
        {
            GameObject pl = GameObject.Find("PLAYER");
            if (pl == null) return "pas de joueur";
            Obs best = null; float bd = 25f;
            foreach (Obs o in obs)
            {
                if (o.F == null || !o.Input || !o.F.gameObject.activeInHierarchy || Owner(o.F) != null) continue;
                FsmState st = o.F.Fsm.GetState(o.F.ActiveStateName);
                if (st == null || st.Transitions.Length == 0) continue;
                float d = (o.F.transform.position - pl.transform.position).sqrMagnitude;
                if (d < bd) { bd = d; best = o; }
            }
            if (best == null) return "rien de non suivi a 5 m";
            lastInputAt = Time.realtimeSinceStartup;
            string to = best.F.Fsm.GetState(best.F.ActiveStateName).Transitions[0].ToState;
            Game.SetState(best.F, to);
            return best.Key + " -> " + to;
        }

        public static string State() { return obs.Count + " automates, " + Desyncs.Count + " desyncs, " + Unshared.Count + " actions non partagees"; }
    }
}
