using System.Collections.Generic;
using HutongGames.PlayMaker;
using MWCoop.Net;
using UnityEngine;

namespace MWCoop
{
    // Voitures Jokkis de location pour la course sur glace (16/10). Les joueurs sans CORRIS courent dans l'une des 16
    // voitures IA du jeu (RACES/ICERACE/Cars<club>/Disable/<numero>) : une voiture roulante MWCoop-Jokkis-<numero> est
    // construite chez TOUS (meme nom partout : VehicleSync la numerote comme n'importe quelle voiture) a partir d'une
    // copie de la SORBET du jeu -- la technique du mod prive Peugeot205 de JD :
    //  - copie de la SORBET (racine de la scene, CarDynamics) : etats Save/Load de ses automates vides (jamais la
    //    sauvegarde de la vraie SORBET), identifiants de pieces prefixes, portieres et hayon eteints (la zone de conduite
    //    est atteinte directement : les Jokkis sont des coques d'une piece), TOUS ses rendus caches (colliders gardes),
    //    LODGroup retire, Deformable coupes (pas de degats pour l'instant), reservoir maintenu plein ;
    //  - visuel de la Jokkis : copies de PIVOT (carrosserie, livree et numero dans les materiaux) et de LOD (vitres,
    //    chassis, siege, volant, compte-tours) reduites a Transform + MeshFilter + MeshRenderer, posees pour que le
    //    milieu des roues coincide ; essieux FRONT / REAR de la SORBET deplaces a l'empattement de la Jokkis ; jantes et
    //    pneus de la Jokkis sur les modeles de roue de la SORBET, au rayon de la SORBET ;
    //  - poste de conduite : zone d'entree (DriveTriggerX), pivot de tete (DriverHeadPivot), SpawnPlayer, MassDriver et
    //    colonne de direction (volant + cle IGNITIONxSorbet) deplaces du meme vecteur, de la tete de la SORBET a la tete
    //    du pilote IA (HeadPivot du squelette) ; le volant de la Jokkis est rattache au pivot du volant de la SORBET
    //    (il tourne avec) ; aiguille du compte-tours = regime du Drivetrain ;
    //  - mecanique : fiche du modele (wiki) sur le Drivetrain (puissance, couple, regime), masse, propulsion ; le reste
    //    (boite 5 de la SORBET, demarreur, batterie, sons moteur, pedales, frein a main) est celui de la SORBET au
    //    moment de la copie.
    // Autorite : l'hote attribue (une voiture par joueur, premier arrive premier servi, gratuit -- decision du 16/10),
    // diffuse la table (K_SET) ; chacun construit / detruit ; l'IA du numero loue reste eteinte la semaine (elle "fait
    // l'impasse" : IceRace la retire de la grille) ; rendue a la fin de la semaine (GlobalWeeksPassed), sur Release, ou
    // quand le joueur quitte. Table gardee par monde et semaine chez l'hote (jokkis-hote.ini).
    // [Coop] Jokkis=1 (option de l'hote, lanceur groupe 8 ; coupee par defaut, beta) : garde la demande d'ici et l'acceptation
    // par l'hote seulement -- la table recue de l'hote s'applique chez tous, option ou pas.
    // Messages Msg.Race, rangs 24+ (IceRace : 20-23 ; Races.OnMessage doit les passer ici : voir OnMessage).
    public static class Jokkis
    {
        public class Def { public int Number; public string Driver, Model, Club, AiPath; }

        public const string Prefix = "MWCoop-Jokkis-";
        //  24 K_ASK   invite -> hote : numero voulu (0 : rendre la sienne)
        //  25 K_SET   hote -> tous : semaine, n, (joueur, numero)... -- table complete (aussi l'instantane d'arrivee)
        //  26 K_DENY  hote -> un invite : numero, motif (1 deja prise, 2 en a deja une, 3 numero inconnu, 4 option coupee chez l'hote)
        const int K_ASK = 24, K_SET = 25, K_DENY = 26;

        // Les 16 voitures IA (RACES/ICERACE/Cars<club>/Disable/<numero>, DB 'SkinChange' DriverName / CarModel, vidage
        // du 08/10) et la fiche du modele (wiki : ch, kg ; toutes propulsion, ~160 km/h).
        static readonly object[][] Table = {
            new object[] { 10, "Kauppinen", "Costo MK2", "Perajarvi" },
            new object[] { 11, "Fleetari", "Fittan LX", "Perajarvi" },
            new object[] { 12, "Kusenius", "Bubple 1600", "Perajarvi" },
            new object[] { 13, "Kaartoranta", "Svoboda 180", "Perajarvi" },
            new object[] { 20, "Rasa-Pantti", "Fittan LX", "Lukka" },
            new object[] { 21, "Ravittaja", "Costo MK2", "Lukka" },
            new object[] { 26, "Jornonen", "Svoboda 180", "Lukka" },
            new object[] { 28, "Dahlfors", "Bubple 1600", "Lukka" },
            new object[] { 31, "Kalkkunainen", "Svoboda 180", "Raittimaa" },
            new object[] { 32, "Vilppula", "Fittan LX", "Raittimaa" },
            new object[] { 33, "Toffgren", "EDM 700", "Raittimaa" },
            new object[] { 37, "Saatunki", "Bubple 1600", "Raittimaa" },
            new object[] { 40, "Csevonius", "Svoboda 180", "Alivieska" },
            new object[] { 41, "K\u00F6s\u00F6", "Bubple 1600", "Alivieska" },
            new object[] { 42, "N\u00F6ss\u00F6nen", "Fittan LX", "Alivieska" },
            new object[] { 43, "Virponen", "Costo MK2", "Alivieska" },
        };
        class Spec { public float Hp, Kg; }
        static readonly Dictionary<string, Spec> Specs = new Dictionary<string, Spec> {
            { "Bubple 1600", new Spec { Hp = 151f, Kg = 775f } },
            { "Costo MK2", new Spec { Hp = 227f, Kg = 852f } },
            { "Svoboda 180", new Spec { Hp = 157f, Kg = 786f } },
            { "Fittan LX", new Spec { Hp = 111f, Kg = 680f } },
            { "EDM 700", new Spec { Hp = 162f, Kg = 870f } },
        };

        class Rent
        {
            public int Id, Number;
            public Def D;
            public GameObject Car;                 // racine MWCoop-Jokkis-<n> (null : pas encore construite ici)
            public GameObject Ai; public bool AiWasActive;
            public List<Renderer> Hidden = new List<Renderer>();
            public Drivetrain Dt; public Rigidbody Body;
            public Drivetrain AiDt; public float AiMass;   // mecanique de la voiture IA du numero (moteur, masse ; essieux copies une fois)
            public Transform Needle;
            public FsmFloat Fuel, FuelLine;
            public float NextHide, NextSpec, NextBuild, NextMask;
            public int BuildTries, MaskLogs;
        }

        static List<Def> defs;
        static readonly Dictionary<int, GameObject> aiCache = new Dictionary<int, GameObject>();   // numero -> objet IA (releve ScanAi)
        static float aiScanAt;

        // Objet de la voiture IA d'un numero. Chez l'hote : son chemin (Cars<club>/Disable/<n>, conteneur eteint compris :
        // Transform.Find voit les inactifs). Chez l'INVITE, Races.Mute sort chaque voiture IA de son conteneur (racine de la
        // scene, nom = numero) : le chemin ne mene plus a rien -> releve par nom + signature (enfants NavigationIce et DB,
        // CarDynamics) sous RACES/ICERACE puis parmi les racines de la scene (inactives comprises), garde en cache ; un
        // numero absent est recherche de nouveau au plus toutes les 2 s. La copie des maillages n'a pas besoin que l'objet
        // soit allume (CopyVisual et les positions de roues lisent des transforms inactifs).
        static GameObject FindAi(Def d)
        {
            if (d == null) return null;
            GameObject g;
            if (aiCache.TryGetValue(d.Number, out g) && g != null) return g;
            g = Game.FindAny(d.AiPath);
            if (g == null && Time.realtimeSinceStartup >= aiScanAt) { aiScanAt = Time.realtimeSinceStartup + 2f; ScanAi(); aiCache.TryGetValue(d.Number, out g); }
            else if (g != null) aiCache[d.Number] = g;
            return g;
        }

        static bool IsJokkisAi(Transform t) { return t.Find("NavigationIce") != null && t.Find("DB") != null && t.GetComponent("CarDynamics") != null; }

        static void ScanAi()
        {
            var want = new Dictionary<string, Def>();
            foreach (Def d in All()) if (!aiCache.ContainsKey(d.Number) || aiCache[d.Number] == null) want[d.Number.ToString()] = d;
            if (want.Count == 0) return;
            int found = 0;
            GameObject ice = Game.FindAny("RACES/ICERACE");
            if (ice != null)
                foreach (Transform t in ice.GetComponentsInChildren<Transform>(true))
                {
                    Def d;
                    if (want.TryGetValue(t.name, out d) && IsJokkisAi(t)) { aiCache[d.Number] = t.gameObject; want.Remove(t.name); found++; }
                }
            if (want.Count > 0)
                foreach (GameObject root in Recon.SceneRoots())
                {
                    Def d;
                    if (want.TryGetValue(root.name, out d) && IsJokkisAi(root.transform)) { aiCache[d.Number] = root; want.Remove(root.name); found++; }
                }
            if (found > 0 || want.Count > 0) Log.Info("jokkis : voitures IA relevees : " + found + " trouvee(s)" + (want.Count > 0 ? ", absentes : " + string.Join(", ", new List<string>(want.Keys).ToArray()) : "") + " (" + aiCache.Count + " connues)");
        }
        static readonly Dictionary<int, Rent> rents = new Dictionary<int, Rent>();     // joueur -> location (table appliquee ici)
        static readonly Dictionary<int, int> wanted = new Dictionary<int, int>();      // joueur -> numero (table de l'hote)
        static readonly Dictionary<string, int> byName = new Dictionary<string, int>(); // hote : pseudo -> numero (semaine, jokkis-hote.ini)
        static readonly Dictionary<int, int> lastLevel = new Dictionary<int, int>();    // hote : niveau vu par joueur (instantane a l'arrivee)
        static int week = -1, loadedWeek = -1;
        static bool enabled, hooked;
        public static bool Enabled { get { return enabled; } }   // ([Coop] Jokkis : location ouverte, onglet Course sur glace du F10)
        static float nextTick;
        public static event System.Action Changed;

        // ---------------------------------------------------------------- interface publique (IceRace / Races / menu)
        public static IList<Def> All()
        {
            if (defs != null) return defs;
            defs = new List<Def>();
            foreach (object[] row in Table)
            {
                var d = new Def { Number = (int)row[0], Driver = (string)row[1], Model = (string)row[2], Club = (string)row[3] };
                d.AiPath = "RACES/ICERACE/Cars" + d.Club + "/Disable/" + d.Number;
                defs.Add(d);
            }
            // Scene chargee : nom et modele lus dans DB 'SkinChange' (la table ne sert que si le vidage differe du jeu).
            // (Liste complete d'abord : FindAi -> ScanAi relit All().)
            if (PlayerSync.InGame)
                foreach (Def d in defs)
                {
                    GameObject ai = FindAi(d);
                    Transform db = ai != null ? ai.transform.Find("DB") : null;
                    PlayMakerFSM skin = db != null ? Game.FsmOn(db.gameObject, "SkinChange") : null;
                    if (skin == null) continue;
                    FsmString dn = skin.FsmVariables.FindFsmString("DriverName"), cm = skin.FsmVariables.FindFsmString("CarModel");
                    if (dn != null && !string.IsNullOrEmpty(dn.Value) && dn.Value != "-") d.Driver = dn.Value;
                    if (cm != null && !string.IsNullOrEmpty(cm.Value) && cm.Value != "-") d.Model = cm.Value;
                }
            return defs;
        }

        public static Def DefOf(int number) { foreach (Def d in All()) if (d.Number == number) return d; return null; }

        // 0 : aucune (le joueur court avec la CORRIS, ou ne court pas).
        public static int ChosenNumber(int playerId) { int n; return wanted.TryGetValue(playerId, out n) ? n : 0; }

        // Racine de la voiture louee par ce joueur, construite ici (null : aucune ou pas encore construite).
        public static Transform CarOf(int playerId) { Rent r; return rents.TryGetValue(playerId, out r) && r.Car != null ? r.Car.transform : null; }

        public static bool IsFree(int number)
        {
            if (DefOf(number) == null) return false;
            foreach (int n in wanted.Values) if (n == number) return false;
            return true;
        }

        public static bool IsRental(Transform carRoot) { return carRoot != null && carRoot.name.StartsWith(Prefix); }

        // Le joueur d'ici demande ce numero (gratuit) ; l'hote decide. Faux : refuse d'avance (hors partie, numero inconnu,
        // deja pris ou deja une voiture) -- le refus de l'hote arrive par K_DENY (message a l'ecran).
        public static bool Request(int number)
        {
            if (!enabled) { Hud.Toast(Lang.T("Location Jokkis coup\u00E9e ([Coop] Jokkis)", "Jokkis rental is off ([Coop] Jokkis)")); return false; }
            if (!Session.Active || !PlayerSync.InGame) return false;
            Def d = DefOf(number);
            if (d == null) { Hud.Toast(Lang.T("Jokkis : num\u00E9ro inconnu ", "Jokkis: unknown number ") + number); return false; }
            if (wanted.ContainsKey(Session.LocalId)) { Deny(2, number); return false; }
            if (!IsFree(number)) { Deny(1, number); return false; }
            if (Session.IsHost) { Assign(Session.LocalId, number); return true; }
            Session.SendToHost(new NetWriter(Msg.Race).U8(K_ASK).U8(number), true);
            Log.Info("jokkis : n " + number + " (" + d.Driver + ") demandee a l'hote");
            return true;
        }

        // Rend la voiture de ce joueur (hote : tout de suite et diffuse ; invite : seulement la sienne, demande a l'hote).
        public static void Release(int playerId)
        {
            if (!Session.Active) return;   // (pas de garde sur l'option : rendre reste toujours possible)
            if (Session.IsHost) { if (wanted.Remove(playerId)) { HostChanged("rendue par #" + playerId); } return; }
            if (playerId == Session.LocalId && wanted.ContainsKey(playerId)) Session.SendToHost(new NetWriter(Msg.Race).U8(K_ASK).U8(0), true);
        }

        // ---------------------------------------------------------------- cycle
        public static void OnLevelLoaded()
        {
            rents.Clear(); wanted.Clear(); byName.Clear(); lastLevel.Clear(); defs = null; aiCache.Clear(); aiScanAt = 0;
            week = -1; loadedWeek = -1; nextTick = 0; testStep = 0;
            // [Coop] Jokkis=1 (option de l'hote dans le lanceur, groupe 8, "Jokkis a louer pour la course sur glace (beta)" ;
            // coupee par defaut pour cette version) : ne garde que la DEMANDE d'ici et l'acceptation par l'hote. La table recue
            // de l'hote s'applique toujours (un invite option coupee construit quand meme les voitures des autres).
            enabled = Config.GetInt("Test", "Jokkis", 0) != 0;   // (18/10, JD : remplacees par la VAZ-2108, qui prend la place d'une IA ; essais seulement)
            if (!hooked) { hooked = true; Session.PlayerLeft += pi => { if (Session.IsHost && wanted.Remove(pi.Id)) HostChanged("depart de " + pi.Name); lastLevel.Remove(pi.Id); }; }
            if (PlayerSync.InGame) Log.Info("jokkis : location " + (enabled ? "ouverte ([Coop] Jokkis=1)" : "coupee ici ([Coop] Jokkis=0 ; la table de l'hote s'applique quand meme)"));
        }

        static int Week() { FsmInt wk = FsmVariables.GlobalVariables.FindFsmInt("GlobalWeeksPassed"); return wk != null ? wk.Value : 0; }
        static string World() { FsmFloat pid = FsmVariables.GlobalVariables.FindFsmFloat("PlayerID"); return pid != null ? pid.Value.ToString("F0") : ""; }
        static string NameOf(int id) { PlayerInfo pi; return Session.Players.TryGetValue(id, out pi) ? pi.Name : "#" + id; }
        static string Clean(string s) { return (s ?? "").Replace(";", "").Replace(":", "").Replace("=", "").Replace("|", ""); }

        public static void Update()
        {
            if (!Session.Active || !PlayerSync.InGame) return;   // (option coupee : la table de l'hote s'applique quand meme)
            float now = Time.realtimeSinceStartup;
            // Chaque image : aiguille du compte-tours, rendus de la SORBET recaches, IA eteinte, reservoir, fiche.
            foreach (Rent r in rents.Values) if (r.Car != null) Tick(r, now);
            if (now < nextTick) return;
            nextTick = now + 0.5f;
            if (Session.IsHost) HostStep();
            Reconcile(now);
        }

        // Hote : semaine (fin du week-end : tout rendu), numeros gardes (jokkis-hote.ini) rendus aux memes pseudos,
        // instantane de la table a chaque joueur qui arrive en partie.
        static void HostStep()
        {
            int w = Week();
            if (w != week)
            {
                if (week >= 0 && wanted.Count > 0) { wanted.Clear(); Log.Info("jokkis : semaine " + w + ", voitures louees rendues (fin du week-end)"); HostChanged("nouvelle semaine"); }
                week = w;
                byName.Clear(); LoadHost(); loadedWeek = w;
                if (byName.Count > 0) Log.Info("jokkis : semaine " + w + ", " + byName.Count + " numero(s) garde(s) (jokkis-hote.ini)");
            }
            // Option coupee chez l'hote : plus de location en cours (table videe, diffusee), rien de retrouve de la semaine.
            if (!enabled && wanted.Count > 0) { wanted.Clear(); HostChanged("option [Coop] Jokkis coupee"); }
            bool changed = false;
            foreach (PlayerInfo pi in Session.Players.Values)
            {
                int inGame = pi.Local ? 1 : pi.Level;
                int was; lastLevel.TryGetValue(pi.Id, out was);
                if (inGame == 1 && was != 1)
                {
                    // Arrive en partie : table envoyee ; son numero de la semaine (meme pseudo) lui revient.
                    int n;
                    if (enabled && !wanted.ContainsKey(pi.Id) && byName.TryGetValue(Clean(pi.Name), out n) && IsFree(n)) { wanted[pi.Id] = n; changed = true; Log.Info("jokkis : " + pi.Name + " retrouve la n " + n + " (semaine " + week + ")"); }
                    else if (!pi.Local && pi.Peer != null && Session.T != null) Session.T.SendReliable(pi.Peer, SetMsg().ToArray());
                }
                lastLevel[pi.Id] = inGame;
            }
            if (changed) HostChanged("numeros de la semaine");
        }

        static void Assign(int id, int number)
        {
            wanted[id] = number;
            Def d = DefOf(number);
            Hud.Toast(NameOf(id) + Lang.T(" prend la Jokkis n\u00B0 ", " takes Jokkis no. ") + number + (d != null ? " (" + d.Driver + ")" : ""));
            HostChanged(NameOf(id) + " -> n " + number);
        }

        // Hote : table changee -> gardee (pseudos de la semaine), diffusee, appliquee ici.
        static void HostChanged(string why)
        {
            if (!Session.IsHost) return;
            byName.Clear();
            foreach (KeyValuePair<int, int> kv in wanted) byName[Clean(NameOf(kv.Key))] = kv.Value;
            SaveHost();
            Log.Info("jokkis : table (" + why + ") : " + Describe());
            if (Session.RemoteCount > 0) Session.Broadcast(SetMsg(), true);
            if (Changed != null) Changed();
            Reconcile(Time.realtimeSinceStartup);
        }

        static NetWriter SetMsg()
        {
            var w = new NetWriter(Msg.Race).U8(K_SET).I32(week).U8(wanted.Count);
            foreach (KeyValuePair<int, int> kv in wanted) w.U8(kv.Key).U8(kv.Value);
            return w;
        }

        static string Describe()
        {
            if (wanted.Count == 0) return "aucune location";
            var sb = new System.Text.StringBuilder();
            foreach (KeyValuePair<int, int> kv in wanted) sb.Append(sb.Length > 0 ? ", " : "").Append(NameOf(kv.Key)).Append(" n ").Append(kv.Value);
            return sb.ToString();
        }

        static void Deny(int why, int number)
        {
            string s = why == 1 ? Lang.T("Jokkis n\u00B0 " + number + " d\u00E9j\u00E0 prise", "Jokkis no. " + number + " already taken")
                     : why == 2 ? Lang.T("Vous avez d\u00E9j\u00E0 une Jokkis", "You already have a Jokkis")
                     : why == 4 ? Lang.T("Location Jokkis coup\u00E9e chez l'h\u00F4te", "Jokkis rental is off on the host")
                     : Lang.T("Jokkis : num\u00E9ro inconnu ", "Jokkis: unknown number ") + number;
            Hud.Toast(s);
        }

        // ---------------------------------------------------------------- messages (Msg.Race, rangs 24-26)
        // Races.OnMessage doit ajouter, AVANT la ligne IceRace :  if (kind >= 24) { Jokkis.OnMessage(kind, from, r); return; }
        public static void OnMessage(int kind, Peer from, NetReader r)
        {
            if (kind == K_ASK && Session.IsHost)
            {
                int n = r.U8();
                if (n == 0) { if (wanted.Remove(from.Id)) HostChanged("rendue par " + NameOf(from.Id)); return; }
                int why = !enabled ? 4 : DefOf(n) == null ? 3 : wanted.ContainsKey(from.Id) ? 2 : !IsFree(n) ? 1 : 0;   // (4 : option coupee chez l'hote)
                if (why != 0)
                {
                    if (Session.T != null) Session.T.SendReliable(from, new NetWriter(Msg.Race).U8(K_DENY).U8(n).U8(why).ToArray());
                    Log.Info("jokkis : n " + n + " refusee a " + NameOf(from.Id) + " (motif " + why + ")");
                    return;
                }
                Assign(from.Id, n);
                return;
            }
            if (kind == K_SET && !Session.IsHost)
            {
                week = r.I32();
                wanted.Clear();
                for (int i = 0, n = r.U8(); i < n; i++) { int id = r.U8(), num = r.U8(); wanted[id] = num; }
                Log.Info("jokkis : table de l'hote (semaine " + week + ") : " + Describe());
                if (Changed != null) Changed();
                Reconcile(Time.realtimeSinceStartup);
                return;
            }
            if (kind == K_DENY && !Session.IsHost) { int n = r.U8(), why = r.U8(); Deny(why, n); }
        }

        // ---------------------------------------------------------------- jokkis-hote.ini : monde|semaine=pseudo:numero;...
        static string HostPath { get { return System.IO.Path.Combine(Log.DataDir, "jokkis-hote.ini"); } }

        static void LoadHost()
        {
            string key = World() + "|" + week + "=";
            if (!System.IO.File.Exists(HostPath)) return;
            try
            {
                foreach (string l in System.IO.File.ReadAllLines(HostPath))
                {
                    if (!l.StartsWith(key)) continue;
                    foreach (string part in l.Substring(key.Length).Split(';'))
                    {
                        int c = part.LastIndexOf(':'); int n;
                        if (c > 0 && int.TryParse(part.Substring(c + 1), out n)) byName[part.Substring(0, c)] = n;
                    }
                }
            }
            catch (System.Exception e) { Log.Warn("jokkis : lecture de jokkis-hote.ini : " + e.Message); }
        }

        static void SaveHost()
        {
            string key = World() + "|" + week + "=";
            var sb = new System.Text.StringBuilder(key);
            bool first = true;
            foreach (KeyValuePair<string, int> kv in byName) { if (!first) sb.Append(';'); first = false; sb.Append(kv.Key).Append(':').Append(kv.Value); }
            var lines = new List<string>();
            try
            {
                if (System.IO.File.Exists(HostPath)) foreach (string l in System.IO.File.ReadAllLines(HostPath)) if (!l.StartsWith(key)) lines.Add(l);
                if (byName.Count > 0) lines.Add(sb.ToString());
                System.IO.File.WriteAllLines(HostPath, lines.ToArray());
            }
            catch (System.Exception e) { Log.Warn("jokkis : ecriture de jokkis-hote.ini : " + e.Message); }
        }

        // ---------------------------------------------------------------- table -> voitures d'ici
        // Chaque cote : une voiture par ligne de la table (construite au plus 5 fois a 2 s d'ecart si la scene manque),
        // detruites celles qui n'y sont plus (ou dont le numero a change).
        static void Reconcile(float now)
        {
            if (!PlayerSync.InGame) return;
            var gone = new List<int>();
            foreach (KeyValuePair<int, Rent> kv in rents) { int n; if (!wanted.TryGetValue(kv.Key, out n) || n != kv.Value.Number) gone.Add(kv.Key); }
            foreach (int id in gone) { Destroy(rents[id]); rents.Remove(id); }
            foreach (KeyValuePair<int, int> kv in wanted)
            {
                Rent r;
                if (!rents.TryGetValue(kv.Key, out r))
                {
                    Def d = DefOf(kv.Value);
                    if (d == null) continue;
                    r = new Rent { Id = kv.Key, Number = kv.Value, D = d };
                    rents[kv.Key] = r;
                }
                if (r.Car == null && now >= r.NextBuild && r.BuildTries < 5)
                {
                    r.NextBuild = now + 2f; r.BuildTries++;
                    try { Build(r); }
                    catch (System.Exception e) { Log.Error("jokkis : construction de la n " + r.Number + " : " + e); if (r.Car != null) { Object.Destroy(r.Car); r.Car = null; } }
                }
            }
        }

        // ---------------------------------------------------------------- construction
        static GameObject FindSorbet()
        {
            GameObject g = Game.FindAny("SORBET(190-200psi)");
            if (g != null && g.GetComponent("CarDynamics") != null) return g;
            foreach (Rigidbody rb in Object.FindObjectsOfType<Rigidbody>())
                if (rb.transform.parent == null && rb.name.StartsWith("SORBET") && rb.GetComponent("CarDynamics") != null) return rb.gameObject;
            return null;
        }

        static Transform FindDeep(Transform t, string name)
        {
            if (t.name == name) return t;
            for (int i = 0; i < t.childCount; i++) { Transform r = FindDeep(t.GetChild(i), name); if (r != null) return r; }
            return null;
        }

        static void Build(Rent r)
        {
            GameObject sorbet = FindSorbet();
            GameObject ai = FindAi(r.D);
            if (sorbet == null || ai == null) { Log.Warn("jokkis : " + (sorbet == null ? "SORBET" : r.D.AiPath) + " introuvable, nouvel essai"); return; }
            Wheel[] aiWheels = ai.GetComponentsInChildren<Wheel>(true);
            if (aiWheels.Length != 4) { Log.Warn("jokkis : " + r.D.AiPath + " : " + aiWheels.Length + " roues, attendu 4"); return; }

            GameObject car = Object.Instantiate(sorbet);
            car.SetActive(false);   // (reglee eteinte : ses automates demarrent sur la version modifiee)
            car.name = Prefix + r.Number;
            r.Car = car;
            Transform ct = car.transform;
            ct.position = sorbet.transform.position; ct.rotation = sorbet.transform.rotation;

            // 1. Automates : etats Save/Load vides, SaveOptions coupe, identifiants prefixes (jamais ceux de la SORBET).
            int emptied = 0, ids = 0;
            Transform saveOpt = ct.Find("SaveOptions");
            foreach (PlayMakerFSM f in car.GetComponentsInChildren<PlayMakerFSM>(true))
            {
                if (saveOpt != null && f.transform.IsChildOf(saveOpt)) { f.enabled = false; emptied++; continue; }
                try
                {
                    f.Fsm.InitData();
                    foreach (FsmState s in f.FsmStates)
                        if (s.Name.IndexOf("Save", System.StringComparison.OrdinalIgnoreCase) >= 0 || s.Name.IndexOf("Load", System.StringComparison.OrdinalIgnoreCase) >= 0) { s.Actions = new FsmStateAction[0]; emptied++; }
                    FsmString[] strs = f.FsmVariables.StringVariables;
                    if (strs != null)
                        foreach (FsmString sv in strs)
                            if (sv != null && (sv.Name == "ID" || sv.Name.StartsWith("UniqueTag")) && !string.IsNullOrEmpty(sv.Value) && !sv.Value.StartsWith("J" + r.Number + "-")) { sv.Value = "J" + r.Number + "-" + sv.Value; ids++; }
                }
                catch (System.Exception e) { Log.Warn("jokkis : automate " + f.FsmName + " : " + e.Message); }
            }

            // 2. Visuel de la SORBET cache (colliders gardes), LODGroup retire, portieres / hayon eteints, degats coupes.
            foreach (Renderer rd in car.GetComponentsInChildren<Renderer>(true))
            {
                if (rd is ParticleRenderer || rd is ParticleSystemRenderer) continue;   // (fumee d'echappement gardee)
                rd.enabled = false; r.Hidden.Add(rd);
            }
            foreach (LODGroup lg in car.GetComponentsInChildren<LODGroup>(true)) Object.Destroy(lg);
            int doors = 0;
            foreach (string dp in new[] { "Doors/DoorFront(leftx)", "Doors/DoorFront(right)", "Doors/DoorRear(leftx)", "Doors/DoorRear(right)", "Hatch/Hatch" })
            {
                Transform d = ct.Find(dp);
                if (d != null && d.gameObject.activeSelf) { d.gameObject.SetActive(false); doors++; }
            }
            int deform = 0;
            foreach (Component c in car.GetComponentsInChildren<Component>(true))
                if (c != null && c.GetType().Name == "Deformable" && c is Behaviour) { ((Behaviour)c).enabled = false; deform++; }

            // 3. Alignement : milieu des roues de la Jokkis (repere de sa racine) sur celui de la SORBET ; essieux deplaces.
            Wheel[] sw = car.GetComponentsInChildren<Wheel>(true);
            Vector3 sMid = Vector3.zero, aMid = Vector3.zero; float aFront = 0, aRear = 0; int nf = 0, nr = 0;
            foreach (Wheel w in sw) sMid += ct.InverseTransformPoint(w.transform.position);
            if (sw.Length > 0) sMid /= sw.Length;
            Transform at = ai.transform;
            foreach (Wheel w in aiWheels)
            {
                Vector3 lp = at.InverseTransformPoint(w.transform.position);
                aMid += lp;
                if (w.wheelPos.ToString().StartsWith("FRONT")) { aFront += lp.z; nf++; } else { aRear += lp.z; nr++; }
            }
            aMid /= aiWheels.Length;
            Vector3 offset = sMid - aMid;
            if (nf > 0 && nr > 0)
            {
                aFront /= nf; aRear /= nr;
                Transform fr = ct.Find("FRONT"), re = ct.Find("REAR");
                if (fr != null) fr.localPosition = new Vector3(fr.localPosition.x, fr.localPosition.y, offset.z + aFront);
                if (re != null) re.localPosition = new Vector3(re.localPosition.x, re.localPosition.y, offset.z + aRear);
            }

            // 4. Carrosserie et habitacle de la Jokkis (PIVOT, LOD) : maillages seuls, sans le pilote IA ni les effets.
            var body = new GameObject("Jokkis");
            body.transform.SetParent(ct, false);
            body.transform.localPosition = offset; body.transform.localRotation = Quaternion.identity;
            int meshes = 0;
            foreach (string part in new[] { "PIVOT", "LOD" })
            {
                Transform src = at.Find(part);
                if (src == null) { Log.Warn("jokkis : " + r.D.AiPath + "/" + part + " absent"); continue; }
                GameObject cp = CopyVisual(src.gameObject, body.transform, part);
                cp.transform.localPosition = src.localPosition; cp.transform.localRotation = src.localRotation; cp.transform.localScale = src.localScale;
                meshes += cp.GetComponentsInChildren<MeshRenderer>(true).Length;
            }

            // 5. Roues : jantes et pneus de la Jokkis sur les modeles de roue de la SORBET, au rayon de la SORBET.
            int wheels = 0;
            foreach (Wheel w in sw)
            {
                Wheel mate = null;
                foreach (Wheel x in aiWheels) if (x.wheelPos == w.wheelPos) { mate = x; break; }
                GameObject rm = mate != null ? mate.model : null;
                if (rm == null && mate != null) { Transform wt = mate.transform.Find("Wheel"); rm = wt != null ? wt.gameObject : null; }
                if (rm == null) continue;
                Transform model = w.model != null ? w.model.transform : w.transform;
                GameObject cp = CopyVisual(rm, model, "JokkisRoue");
                float k = mate.radius > 0.05f ? w.radius / mate.radius : 1f;
                Vector3 ls = rm.transform.lossyScale * k, ms = model.lossyScale;
                cp.transform.localPosition = Vector3.zero; cp.transform.localRotation = Quaternion.identity;
                cp.transform.localScale = new Vector3(ls.x / ms.x, ls.y / ms.y, ls.z / ms.z);
                wheels++;
            }

            // 6. Poste de conduite : tete du pilote IA (HeadPivot du squelette, repere de la voiture) ; la zone d'entree, le
            //    pivot de tete, SpawnPlayer, MassDriver et la colonne de direction de la SORBET glissent du meme vecteur.
            Transform head = FindDeep(at, "HeadPivot");
            Transform hp = FindDeep(ct, "DriverHeadPivot");
            Vector3 delta = Vector3.zero;
            if (head != null && hp != null)
            {
                Vector3 jHead = offset + at.InverseTransformPoint(head.position) + new Vector3(0f, 0.05f, 0.05f);   // (yeux : un peu au-dessus et devant l'os)
                Vector3 sHead = ct.InverseTransformPoint(hp.position);
                delta = jHead - sHead;
                foreach (string mp in new[] { "Functions/PlayerTrigger/DriveTriggerX", "Functions/PlayerTrigger/DriverHeadPivot", "Functions/PlayerTrigger/SpawnPlayer", "Functions/MassDriver", "Functions/SteeringColumnPivot" })
                {
                    Transform m = ct.Find(mp);
                    if (m != null) m.position = ct.TransformPoint(ct.InverseTransformPoint(m.position) + delta);
                }
                foreach (Joint j in hp.GetComponents<Joint>())
                {
                    Vector3 aw = hp.TransformPoint(j.anchor);
                    j.autoConfigureConnectedAnchor = false;
                    j.connectedAnchor = j.connectedBody != null ? j.connectedBody.transform.InverseTransformPoint(aw) : aw;
                }
            }
            else Log.Warn("jokkis : " + (head == null ? "HeadPivot du pilote IA" : "DriverHeadPivot de la SORBET") + " introuvable : poste de conduite de la SORBET garde");
            // Volant de la Jokkis (enfant 'steering' de LOD le plus proche des mains du pilote) rattache au pivot du volant de la
            // SORBET, son axe (plus petite dimension du maillage) sur celui du volant de la SORBET : il tourne avec.
            Transform sPivot = FindDeep(ct, "SteeringPivotSorbett");
            Transform jSteer = PickSteering(body.transform, at, offset);
            if (sPivot != null && jSteer != null)
            {
                Transform sMesh = FindDeep(sPivot, "steering_wheel");
                Vector3 sAxis = sMesh != null ? sMesh.TransformDirection(ThinAxis(sMesh)) : sPivot.forward;
                Vector3 jAxis = jSteer.TransformDirection(ThinAxis(jSteer));
                if (Vector3.Dot(sAxis, jAxis) < 0f) jAxis = -jAxis;
                jSteer.rotation = Quaternion.FromToRotation(jAxis, sAxis) * jSteer.rotation;
                MeshFilter mf = jSteer.GetComponent<MeshFilter>();
                Vector3 centre = mf != null && mf.sharedMesh != null ? jSteer.TransformPoint(mf.sharedMesh.bounds.center) : jSteer.position;
                jSteer.position += sPivot.position - centre;
                jSteer.SetParent(sPivot, true);
            }
            r.Needle = FindDeep(body.transform, "tachometer_needle");

            // 7. Mecanique : fiche du modele, propulsion, masse ; reservoir plein.
            r.Body = car.GetComponent<Rigidbody>();
            r.Dt = car.GetComponent<Drivetrain>();
            AdoptAi(r, ai, ct, offset);   // (JD, 18/10 : le moteur, la masse, les essieux et les sons de la voiture IA, pas ceux de la SORBET)
            ApplySpec(r, true);
            Transform tank = ct.Find("Simulation/FuelTankSorbett");
            PlayMakerFSM tf = tank != null ? Game.FsmOn(tank.gameObject, "Data") : null;
            r.Fuel = tf != null ? tf.FsmVariables.FindFsmFloat("FuelLevel") : null;
            Transform eng = ct.Find("Simulation/Engine/EngineSim");
            PlayMakerFSM fl = eng != null ? Game.FsmOn(eng.gameObject, "FuelLine") : null;
            r.FuelLine = fl != null ? fl.FsmVariables.FindFsmFloat("FuelLevel") : null;
            if (r.Fuel != null) r.Fuel.Value = 50f;
            if (r.FuelLine != null) r.FuelLine.Value = 50f;

            // 8. Place au paddock, puis allumee ; VehicleSync la releve tout de suite.
            Vector3 pos; Quaternion rot;
            SpawnPose(r.D, out pos, out rot);
            ct.position = pos; ct.rotation = rot;
            if (r.Body != null) { r.Body.velocity = Vector3.zero; r.Body.angularVelocity = Vector3.zero; }
            car.SetActive(true);
            VehicleSync.RescanSoon();
            WheelMask(r);   // (colliders allumes : tout de suite, puis chaque seconde dans Tick)
            r.NextMask = Time.realtimeSinceStartup + 1f;

            // 9. L'IA du numero fait l'impasse : son objet reste eteint tant que la voiture est louee (Tick).
            r.Ai = ai; r.AiWasActive = ai.activeSelf;
            if (ai.activeSelf) ai.SetActive(false);

            Log.Info("jokkis : " + car.name + " (" + r.D.Driver + ", " + r.D.Model + ", club " + r.D.Club + ") construite pour " + NameOf(r.Id) + " en " + pos.ToString("F1")
                     + " : " + emptied + " etats de sauvegarde vides, " + ids + " identifiants prefixes, " + r.Hidden.Count + " rendus SORBET caches, " + doors + " portes eteintes, " + deform + " Deformable coupes, "
                     + meshes + " maillages Jokkis, " + wheels + " roues, decalage " + offset.ToString("F2") + ", tete " + delta.ToString("F2")
                     + (jSteer != null ? ", volant pose" : ", volant Jokkis introuvable") + (r.Needle != null ? ", compte-tours" : "") + (r.Dt != null ? ", " + r.Dt.maxPower.ToString("F0") + " ch" : ", sans Drivetrain"));
        }

        // Copie d'un sous-arbre reduite a Transform + MeshFilter + MeshRenderer (pilote IA, effets, projecteur retires), tout allume.
        static GameObject CopyVisual(GameObject src, Transform parent, string name)
        {
            GameObject cp = Object.Instantiate(src);
            cp.name = name;
            foreach (Transform t in cp.GetComponentsInChildren<Transform>(true))
            {
                if (t == null || t == cp.transform) continue;
                if (t.name == "Driver" || t.name == "Muzzle" || t.name.StartsWith("SnowCloudTrail") || t.name.StartsWith("CarShadowProjector") || t.name == "CarLightsAIJokkis") Object.DestroyImmediate(t.gameObject);
            }
            for (int pass = 0; pass < 3; pass++)
                foreach (Component c in cp.GetComponentsInChildren<Component>(true))
                {
                    if (c == null || c is Transform || c is MeshFilter || c is MeshRenderer) continue;
                    try { Object.DestroyImmediate(c); } catch { }   // (filtres audio avant leur AudioSource : passe suivante)
                }
            foreach (Transform t in cp.GetComponentsInChildren<Transform>(true)) t.gameObject.SetActive(true);
            cp.transform.SetParent(parent, false);
            return cp;
        }

        // Axe d'un maillage plat (volant) : sa plus petite dimension, repere local de l'objet.
        static Vector3 ThinAxis(Transform t)
        {
            MeshFilter mf = t.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null) return Vector3.forward;
            Vector3 s = mf.sharedMesh.bounds.size;
            return s.x <= s.y && s.x <= s.z ? Vector3.right : s.y <= s.z ? Vector3.up : Vector3.forward;
        }

        // Volant de la Jokkis : parmi les 'steering' copies sous LOD, le plus proche du milieu des mains du pilote IA.
        static Transform PickSteering(Transform body, Transform ai, Vector3 offset)
        {
            Transform lod = body.Find("LOD");
            if (lod == null) return null;
            Transform hl = FindDeep(ai, "hand_left"), hr = FindDeep(ai, "hand_right");
            Vector3 hands = hl != null && hr != null ? offset + (ai.InverseTransformPoint(hl.position) + ai.InverseTransformPoint(hr.position)) * 0.5f : Vector3.zero;
            Transform best = null; float bestD = float.MaxValue;
            Transform car = body.parent;
            for (int i = 0; i < lod.childCount; i++)
            {
                Transform c = lod.GetChild(i);
                if (c.name != "steering") continue;
                MeshFilter mf = c.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;
                if (hl == null || hr == null) return c;
                float d = (car.InverseTransformPoint(c.TransformPoint(mf.sharedMesh.bounds.center)) - hands).sqrMagnitude;
                if (d < bestD) { bestD = d; best = c; }
            }
            return best;
        }

        // Fiche du modele (wiki) : Drivetrain (puissance, couple, regimes), propulsion, masse. Remise toutes les 2 s (des
        // automates du jeu reecrivent la puissance de la SORBET).
        // Mecanique et sons de la voiture IA du numero (demande de JD, 18/10 : « ce sont des Sorbet deguisees, elles n'ont pas le
        // son ni le comportement des voitures des PNJ ») :
        //  - Drivetrain : puissance, couple, regimes de l'IA (ApplySpec, toutes les 2 s) ; la boite reste celle de la SORBET
        //    (5 rapports + marche arriere : ses automates de passage des vitesses en dependent) ;
        //  - masse de l'IA ; essieux (Axles de l'IA : suspensions, raideurs, adherence, pression, carrossage) recopies champ par
        //    champ (reflexion : Axle est une classe du jeu) puis CarDynamics.SetWheelsParams ;
        //  - sons : la SORBET perd son AudioEngine (Low / Med / High) ; l'objet Sounds de l'IA (EngineSound : source, filtres,
        //    automate 'Sound' lu sur le Drivetrain ; Backfire) est copie sous la Jokkis, ses references ramenees a elle. Chez les
        //    autres joueurs (copie), VehicleSync pilote ces sources comme toutes (SoundObjs : enfants « Sounds », volume et
        //    hauteur recus du conducteur).
        static void AdoptAi(Rent r, GameObject ai, Transform ct, Vector3 offset)
        {
            r.AiDt = ai.GetComponent<Drivetrain>();
            Rigidbody ab = ai.GetComponent<Rigidbody>();
            r.AiMass = ab != null && ab.mass > 100f ? ab.mass : 0f;
            // Essieux
            int axleFields = 0;
            try
            {
                Component sa = ct.GetComponent("Axles"), aa = ai.GetComponent("Axles");
                if (sa != null && aa != null)
                {
                    foreach (string an in new[] { "frontAxle", "rearAxle" })
                    {
                        System.Reflection.FieldInfo af = sa.GetType().GetField(an);
                        object src = af != null ? af.GetValue(aa) : null, dst = af != null ? af.GetValue(sa) : null;
                        if (src == null || dst == null) continue;
                        foreach (System.Reflection.FieldInfo fi in src.GetType().GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
                        {
                            if (fi.FieldType != typeof(float) || fi.Name.StartsWith("old") || fi.Name.StartsWith("delta")) continue;
                            float v = (float)fi.GetValue(src);
                            if (float.IsNaN(v) || float.IsInfinity(v)) continue;
                            fi.SetValue(dst, v); axleFields++;
                        }
                    }
                    Component cd = ct.GetComponent("CarDynamics");
                    System.Reflection.MethodInfo m = cd != null ? cd.GetType().GetMethod("SetWheelsParams") : null;
                    if (m != null) m.Invoke(cd, null);
                }
            }
            catch (System.Exception e) { Log.Warn("jokkis : essieux de l'IA : " + e.Message); }
            // Sons
            int snd = 0;
            try
            {
                Transform ae = ct.Find("Simulation/Engine/AudioEngine");
                if (ae != null) ae.gameObject.SetActive(false);
                Transform aiSounds = ai.transform.Find("Sounds");
                if (aiSounds != null)
                {
                    GameObject cp = Object.Instantiate(aiSounds.gameObject);
                    cp.name = "Sounds";
                    cp.transform.SetParent(ct, false);
                    cp.transform.localPosition = aiSounds.localPosition + offset; cp.transform.localRotation = aiSounds.localRotation; cp.transform.localScale = aiSounds.localScale;
                    cp.SetActive(true);
                    Transform muzzle = FindDeep(ct, "Muzzle");
                    foreach (PlayMakerFSM f in cp.GetComponentsInChildren<PlayMakerFSM>(true))
                    {
                        try
                        {
                            f.Fsm.InitData();
                            foreach (FsmObject ov in f.FsmVariables.ObjectVariables)
                                if (ov.Value is Drivetrain || ov.Name == "Drivetrain") ov.Value = r.Dt;
                            foreach (FsmGameObject gv in f.FsmVariables.GameObjectVariables)
                            {
                                if (gv.Value == ai) gv.Value = r.Car;
                                else if (gv.Name == "Muzzle") { if (muzzle != null) gv.Value = muzzle.gameObject; else f.enabled = false; }
                            }
                        }
                        catch (System.Exception e) { Log.Warn("jokkis : son " + f.FsmName + " : " + e.Message); }
                    }
                    snd = cp.GetComponentsInChildren<AudioSource>(true).Length;
                }
            }
            catch (System.Exception e) { Log.Warn("jokkis : sons de l'IA : " + e.Message); }
            Log.Info("jokkis : n " + r.Number + " : mecanique de l'IA (" + (r.AiDt != null ? r.AiDt.maxPower.ToString("F0") + " ch, " : "moteur ?, ") + (r.AiMass > 0f ? r.AiMass.ToString("F0") + " kg, " : "") + axleFields + " reglages d'essieux), " + snd + " source(s) de son de l'IA");
        }

        static void ApplySpec(Rent r, bool first)
        {
            if (r.AiDt != null)
            {
                Drivetrain a = r.AiDt, d = r.Dt;
                if (r.Body != null && r.AiMass > 0f && !Mathf.Approximately(r.Body.mass, r.AiMass)) r.Body.mass = r.AiMass;
                if (d == null) return;
                if (!Mathf.Approximately(d.maxPower, a.maxPower)) { d.maxPower = a.maxPower; d.originalMaxPower = a.maxPower; }
                if (!Mathf.Approximately(d.maxTorque, a.maxTorque)) d.maxTorque = a.maxTorque;
                if (!Mathf.Approximately(d.maxPowerRPM, a.maxPowerRPM)) d.maxPowerRPM = a.maxPowerRPM;
                if (!Mathf.Approximately(d.maxTorqueRPM, a.maxTorqueRPM)) d.maxTorqueRPM = a.maxTorqueRPM;
                if (!Mathf.Approximately(d.maxRPM, a.maxRPM)) d.maxRPM = a.maxRPM;
                if (!Mathf.Approximately(d.minRPM, a.minRPM)) d.minRPM = a.minRPM;
                if (d.transmission != a.transmission)
                {
                    d.transmission = a.transmission;
                    if (!first) { try { d.SetTransmission(a.transmission); } catch (System.Exception e) { Log.Warn("jokkis : SetTransmission : " + e.Message); } }
                }
                return;
            }
            Spec sp; if (!Specs.TryGetValue(r.D.Model, out sp)) sp = new Spec { Hp = 150f, Kg = 800f };
            if (r.Body != null && !Mathf.Approximately(r.Body.mass, sp.Kg)) r.Body.mass = sp.Kg;
            Drivetrain dt = r.Dt;
            if (dt == null) return;
            if (!Mathf.Approximately(dt.maxPower, sp.Hp)) { dt.maxPower = sp.Hp; dt.originalMaxPower = sp.Hp; }
            float torque = sp.Hp * 1.3f;
            if (!Mathf.Approximately(dt.maxTorque, torque)) dt.maxTorque = torque;
            if (!Mathf.Approximately(dt.maxPowerRPM, 6000f)) dt.maxPowerRPM = 6000f;
            if (!Mathf.Approximately(dt.maxTorqueRPM, 4000f)) dt.maxTorqueRPM = 4000f;
            if (!Mathf.Approximately(dt.maxRPM, 7000f)) dt.maxRPM = 7000f;
            if (dt.transmission != Drivetrain.Transmissions.RWD)
            {
                dt.transmission = Drivetrain.Transmissions.RWD;   // (avant son Start : il l'applique lui-meme ; apres : SetTransmission)
                if (!first) { try { dt.SetTransmission(Drivetrain.Transmissions.RWD); } catch (System.Exception e) { Log.Warn("jokkis : SetTransmission : " + e.Message); } }
            }
        }

        // Place au paddock de la glace : une rangee parallele aux stands des clubs (CarsPerajarvi -> CarsRaittimaa), 12 m
        // a cote, un emplacement de 4 m par numero (meme calcul partout ; l'hote fait foi de toute facon). [Coop]
        // JokkisParc=x,y,z,cap remplace la rangee (centre et cap en degres) -- a regler sur place si besoin.
        static void SpawnPose(Def d, out Vector3 pos, out Quaternion rot)
        {
            int slot = 0; IList<Def> all = All();
            for (int i = 0; i < all.Count; i++) if (all[i] == d) slot = i;
            Vector3 centre, dir;
            string conf = Config.Get("Coop", "JokkisParc", "");
            string[] p = conf.Split(',');
            float cx, cy, cz, cap;
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            if (p.Length >= 4 && float.TryParse(p[0].Trim(), System.Globalization.NumberStyles.Float, inv, out cx) && float.TryParse(p[1].Trim(), System.Globalization.NumberStyles.Float, inv, out cy)
                && float.TryParse(p[2].Trim(), System.Globalization.NumberStyles.Float, inv, out cz) && float.TryParse(p[3].Trim(), System.Globalization.NumberStyles.Float, inv, out cap))
            {
                centre = new Vector3(cx, cy, cz); dir = Quaternion.Euler(0, cap, 0) * Vector3.forward;
            }
            else
            {
                GameObject a = Game.FindAny("RACES/ICERACE/CarsPerajarvi"), b = Game.FindAny("RACES/ICERACE/CarsRaittimaa");
                Vector3 pa = a != null ? a.transform.position : new Vector3(-1141.7f, -4f, 839f);
                Vector3 pb = b != null ? b.transform.position : new Vector3(-1126.4f, -4f, 856.4f);
                dir = pb - pa; dir.y = 0f;
                if (dir.sqrMagnitude < 1f) dir = new Vector3(0.66f, 0f, 0.75f);
                dir.Normalize();
                Vector3 side = new Vector3(-dir.z, 0f, dir.x);
                centre = (pa + pb) * 0.5f + side * 12f;
            }
            Vector3 at = centre + dir * ((slot - 7.5f) * 4f);
            float y = at.y + 0.6f, best = float.MaxValue;
            foreach (RaycastHit h in Physics.RaycastAll(new Vector3(at.x, at.y + 25f, at.z), Vector3.down, 60f))
            {
                if (h.collider.isTrigger || h.collider.attachedRigidbody != null) continue;
                if (h.distance < best) { best = h.distance; y = h.point.y + 0.6f; }
            }
            pos = new Vector3(at.x, y, at.z);
            rot = Quaternion.LookRotation(dir);
        }

        // ---------------------------------------------------------------- vie de la voiture
        static void Tick(Rent r, float now)
        {
            if (r.Needle != null && r.Dt != null) r.Needle.localRotation = Quaternion.Euler(0f, Mathf.Clamp(r.Dt.rpm / -44.4444f, -225f, 0f), 0f);
            if (now >= r.NextHide)
            {
                r.NextHide = now + 0.5f;
                foreach (Renderer rd in r.Hidden) if (rd != null && rd.enabled) rd.enabled = false;   // (des automates rallument feux, vitres givrees...)
                if (r.Ai != null && r.Ai.activeSelf) r.Ai.SetActive(false);                              // (l'IA fait l'impasse)
                if (r.Fuel != null && r.Fuel.Value < 40f) r.Fuel.Value = 50f;
                if (r.FuelLine != null && r.FuelLine.Value < 40f) r.FuelLine.Value = 50f;
            }
            if (now >= r.NextSpec) { r.NextSpec = now + 2f; ApplySpec(r, false); }
            if (now >= r.NextMask) { r.NextMask = now + 1f; WheelMask(r); }
        }

        // Rayons des roues (Wheel.FixedUpdate : Physics.Raycast(moyeu, -up, course + rayon, layerMask)) : le masque de
        // UnityCar n'exclut que la couche de la voiture ; les colliders de caisse de la SORBET sont sur d'autres couches
        // (Collider2...). Essieux deplaces a l'empattement de la Jokkis, un collider de caisse (Colliders/front2, un
        // declencheur : Unity 5 les touche aussi au Raycast) peut tomber dans la course d'une roue : des que la voiture
        // penche, la roue "touche le sol" sur sa propre caisse, pousse et fait tourner la voiture sans fin (toupie de
        // l'Audi S1, 15/10). Couches des colliders de la voiture croises par la course d'une roue (petites spheres de 6 cm
        // tous les 5 cm du moyeu au bout de la course, Unity 5.0 : pas d'OverlapCapsule) retirees du masque, sauf celles
        // du decor (couche de la voiture, 0, Terrain, Water). Reprise de WheelMask de Peugeot205.cs.
        static void WheelMask(Rent r)
        {
            if (r.Car == null) return;
            Transform ct = r.Car.transform;
            Wheel[] wheels = r.Car.GetComponentsInChildren<Wheel>(true);
            int own = 0;
            var hit = new List<string>();
            foreach (Wheel w in wheels)
            {
                float travel = w.suspensionTravel > 0f ? w.suspensionTravel : 0.2f, rad = w.radius > 0f ? w.radius : 0.3f;
                for (float d = 0f; d <= travel + rad + 0.001f; d += 0.05f)
                    foreach (Collider c in Physics.OverlapSphere(w.transform.position - ct.up * d, 0.06f))
                    {
                        if (c == null || !c.transform.IsChildOf(ct)) continue;
                        int l = c.gameObject.layer;
                        string ln = LayerMask.LayerToName(l);
                        if (l == r.Car.layer || l == 0 || ln == "Terrain" || ln == "Water") continue;
                        if ((own & (1 << l)) == 0 && hit.Count < 12) hit.Add(VehicleSync.RelPath(ct, c.transform) + " [" + ln + (c.isTrigger ? ", declencheur" : "") + "]");
                        own |= 1 << l;
                    }
            }
            if (own == 0) return;
            System.Reflection.FieldInfo lm = typeof(Wheel).GetField("layerMask", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
            if (lm == null) { if (r.MaskLogs++ == 0) Log.Warn("jokkis : champ layerMask absent de Wheel : couches des roues laissees"); return; }
            foreach (Wheel w in wheels)
            {
                object v = lm.GetValue(w);
                int m = v is LayerMask ? ((LayerMask)v).value : v is int ? (int)v : 0;
                if ((m & own) == 0) continue;
                int nm = m & ~own;
                if (v is LayerMask) { LayerMask lk = (LayerMask)v; lk.value = nm; lm.SetValue(w, lk); } else lm.SetValue(w, nm);
                if (r.MaskLogs++ < 8) Log.Info("jokkis : roue " + w.wheelPos + " : couches retirees du rayon (" + string.Join(", ", hit.ToArray()) + ") : masque " + m + " -> " + nm);
            }
        }

        // Voiture detruite ici : ce qui n'est pas a elle en sort d'abord (joueur au volant, cameras des retroviseurs du
        // jeu, objets MWCoop : avatars assis...), l'IA revient si elle etait allumee a la copie.
        static void Destroy(Rent r)
        {
            if (r.Car != null)
            {
                Transform ct = r.Car.transform;
                if (VehicleSync.LocalDrivingRoot == ct) VehicleSync.ExitLocal();
                GameObject mirrors = Game.FindAny("Systems/Mirrors/Disable");
                foreach (Transform t in r.Car.GetComponentsInChildren<Transform>(true))
                {
                    if (t == null || t == ct) continue;
                    if (t.name == "PLAYER") t.SetParent(null, true);
                    else if (t.name.EndsWith("MirrorCam")) t.SetParent(mirrors != null ? mirrors.transform : null, true);
                    else if (t.name.StartsWith("MWCoop-")) t.SetParent(null, true);
                }
                Object.Destroy(r.Car);
                Log.Info("jokkis : " + r.Car.name + " de " + NameOf(r.Id) + " detruite");
                r.Car = null;
            }
            if (r.Ai != null && r.AiWasActive && !r.Ai.activeSelf) r.Ai.SetActive(true);
            r.Ai = null;
            if (Changed != null) Changed();
        }

        // ---------------------------------------------------------------- apercu (panneau 3D d'un autre module, Studio)
        // Copie visuelle seule de la Jokkis (carrosserie, habitacle, 4 roues a leur place), sur cette couche, a l'origine.
        public static GameObject BuildPreview(int number, int layer)
        {
            Def d = DefOf(number);
            GameObject ai = FindAi(d);
            if (ai == null) return null;
            var root = new GameObject("MWCoop-JokkisApercu-" + number);
            Transform at = ai.transform;
            foreach (string part in new[] { "PIVOT", "LOD" })
            {
                Transform src = at.Find(part);
                if (src == null) continue;
                GameObject cp = CopyVisual(src.gameObject, root.transform, part);
                cp.transform.localPosition = src.localPosition; cp.transform.localRotation = src.localRotation; cp.transform.localScale = src.localScale;
            }
            foreach (Wheel w in ai.GetComponentsInChildren<Wheel>(true))
            {
                GameObject rm = w.model;
                if (rm == null) { Transform wt = w.transform.Find("Wheel"); rm = wt != null ? wt.gameObject : null; }
                if (rm == null) continue;
                GameObject cp = CopyVisual(rm, root.transform, "roue" + w.wheelPos);
                cp.transform.localPosition = at.InverseTransformPoint(rm.transform.position);
                cp.transform.localRotation = Quaternion.Inverse(at.rotation) * rm.transform.rotation;
                Vector3 ls = rm.transform.lossyScale, rs = at.lossyScale;
                cp.transform.localScale = new Vector3(ls.x / rs.x, ls.y / rs.y, ls.z / rs.z);
            }
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;
            return root;
        }

        // ---------------------------------------------------------------- autotest "jokkis"
        // 20 s : l'hote prend la n 33 ; 30 s : il note la voiture (position, puissance, IA eteinte) ; 40 s : l'invite note
        // CarOf(0), sa position et ce qu'en sait VehicleSync ; 60 s : l'hote la rend ; 70 s : chacun note voiture partie, IA revenue.
        static int testStep;
        public static void Test(string mode, float t)
        {
            if (mode != "jokkis") return;
            bool host = Session.IsHost;
            if (host && testStep == 0 && t > 20f) { testStep = 1; Log.Info("autotest jokkis : demande n 33 -> " + Request(33)); }
            if (host && testStep == 1 && t > 30f) { testStep = 2; Log.Info("autotest jokkis : " + State(0)); }
            if (!host && testStep == 0 && t > 40f) { testStep = 2; Log.Info("autotest jokkis : " + State(0)); }
            if (host && testStep == 2 && t > 60f) { testStep = 3; Release(Session.LocalId); Log.Info("autotest jokkis : n 33 rendue"); }
            if (testStep >= 2 && testStep < 4 && t > 70f) { testStep = 4; Log.Info("autotest jokkis : apres retour : " + State(0)); }
        }

        static string State(int id)
        {
            Transform c = CarOf(id);
            Def d = DefOf(33);
            GameObject ai = FindAi(d);
            if (c == null) return "pas de voiture pour #" + id + " (numero choisi " + ChosenNumber(id) + ") ; IA 33 " + (ai == null ? "introuvable" : ai.activeSelf ? "allumee" : "eteinte");
            Rigidbody rb = c.GetComponent<Rigidbody>();
            Drivetrain dt = c.GetComponent<Drivetrain>();
            return c.name + " en " + c.position.ToString("F2") + ", " + (dt != null ? dt.maxPower.ToString("F0") + " ch, " + dt.transmission : "sans Drivetrain") + (rb != null ? ", " + rb.mass.ToString("F0") + " kg" : "")
                   + " ; VehicleSync : " + (VehicleSync.RootByKey(c.name) != null ? "connue, numero reseau " + VehicleSync.CarIndex(rb) : "inconnue")
                   + " ; IA 33 " + (ai == null ? "introuvable" : ai.activeSelf ? "allumee" : "eteinte");
        }
    }
}
